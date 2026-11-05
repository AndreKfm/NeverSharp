// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// Portions Copyright 2026 Max Schwenk and the tsrs contributors
// SPDX-License-Identifier: Apache-2.0
//
// Ported from crates/tsrs_checker/src/flowmemo.rs and the flow memo parts of crates/tsrs_checker/src/flow.rs of tsrs
// (notes/perf-flow-union-inference.md section 1, upstream/flow-memo.md)
//   https://github.com/oxc-project/tsrs at tag v0.18.0 (5f92808bc92bb97b06212153ab73d28d551c0b9b)
// Track O / tsrs-only, no counterpart in TypeScript 6.0.3: a memo of getTypeAtFlowNode results across the walks of
// src/compiler/checker.ts getFlowTypeOfReference at commit 050880ce59e30b356b686bd3144efe24f875ebc8.
// Modified: translated from Rust to C#; module RS2. Differences to tsrs, all on the side of storing less:
// - frames are memoized (consulted and stored) only while no loop analysis is in progress (tsrs also stores the frames
//   computed under a loop's back edges);
// - a reduce label in process anywhere (TypeScript 6.0.3 installs its antecedents on the label for every walk, not per
//   walk as typescript-go) and the inline limit of narrowType taint every active frame and turn the memo off for the
//   frames that begin meanwhile;
// - a circular or in-progress resolution taints every active frame (tsrs: the frames younger than that resolution);
// - reaching the instantiation limit (TS2589, reported at the current node) marks the frame as having effects;
// - the key of a property access chain is two 64-bit hashes of its structure instead of xxh3-128.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    /// <summary>No taint source (flowmemo.rs UNTAINTED).</summary>
    private const uint FlowMemoUntainted = uint.MaxValue;

    /// <summary>
    /// The frame instantiated a type, ran inside an instantiation, or created a type, symbol or signature
    /// (flowmemo.rs FLAG_EFFECTS).
    /// </summary>
    private const byte FlowMemoEffects = 1;

    /// <summary>The frame read flowTypeCache (FLAG_TYPE_CACHE).</summary>
    private const byte FlowMemoTypeCache = 2;

    /// <summary>The frame may have reset the instantiation count (FLAG_COUNT_RESET).</summary>
    private const byte FlowMemoCountReset = 4;

    private const int FlowMemoBits = 12;
    private const int FlowDepthLimit = 2000;

    /// <summary>A walk's memo key (flowmemo.rs compute_flow_memo_key): the reference's structure and the walk's types and container.</summary>
    private readonly record struct FlowMemoKey(ulong Root, ulong Chain, ulong Types, ulong Container);

    private struct FlowMemoSlot
    {
        public FlowRef Flow;
        public FlowMemoKey Key;
        public Type Type;
        public uint Epoch;
        public ushort Height;
        public bool CountReset;
        public bool Used;
    }

    /// <summary>A memo answer (flowmemo.rs MemoHit).</summary>
    private readonly record struct FlowMemoHit(Type Type, ushort Height, uint Epoch, bool CountReset)
    {
        public byte Flags => (byte)((Epoch != 0 ? FlowMemoTypeCache : 0) | (CountReset ? FlowMemoCountReset : 0));
    }

    /// <summary>What FlowFrameBegin saved (flowmemo.rs FlowFrame).</summary>
    private readonly record struct FlowFrame(uint Start, uint SavedTransient, uint SavedReference, ushort SavedHeight, byte SavedFlags,
        int InstantiationCount, long TotalInstantiationCount, long Created, bool InInstantiation);

    /// <summary>The taint sources older than a frame that it consumed, and its flags (flowmemo.rs FrameTaint).</summary>
    private readonly record struct FlowFrameTaint(uint Transient, uint Reference, byte Flags)
    {
        public bool IsPure => Transient == FlowMemoUntainted && Reference == FlowMemoUntainted;
    }

    /// <summary>What a sharedFlows entry's frame consumed (flow_types.rs SharedFlow's memo fields).</summary>
    private readonly record struct FlowSharedMemo(uint Transient, uint Reference, ushort Height, byte Flags);

    private FlowMemoSlot[]? flowMemoSlots;
    private uint flowMemoSerial = 1;
    private bool flowMemoOff;

    // the registers of the innermost active frame (flowmemo.rs Regs)
    private uint flowMemoTransient = FlowMemoUntainted;
    private uint flowMemoReference = FlowMemoUntainted;
    private ushort flowMemoHeight;
    private byte flowMemoFlags;

    /// <summary>Identifies the current flowTypeCache (a new value whenever it is replaced or an entry changes).</summary>
    private uint flowMemoTypeCacheEpoch = FlowMemoUntainted;

    /// <summary>Checkpoints the active memoized frames iterated past.</summary>
    private readonly List<FlowRef> flowMemoCheckpoints = [];

    /// <summary>The memo fields of sharedFlowNodes (index for index).</summary>
    private readonly List<FlowSharedMemo> sharedFlowMemo = [];

    /// <summary>The taint source of each flow loop stack entry (flowLoopNodes index for index).</summary>
    private readonly List<uint> flowLoopSerials = [];

    /// <summary>Instantiations since the checker was created (instantiationCount is reset by checkExpression).</summary>
    private long totalInstantiationCount;

    /// <summary>A fresh serial (flowmemo.rs next_serial).</summary>
    private uint FlowMemoNextSerial()
    {
        var s = flowMemoSerial++;
        if (flowMemoSerial == FlowMemoUntainted)
        {
            // serials would wrap: stop memoizing, keep the rest exact
            flowMemoOff = true;
            flowMemoSerial = 1;
        }
        return s;
    }

    /// <summary>Taints the active frames younger than <paramref name="source"/> (0: every active frame).</summary>
    private void FlowMemoTaint(uint source)
    {
        if (source < flowMemoTransient)
        {
            flowMemoTransient = source;
        }
    }

    private void FlowMemoTaintReference(uint source)
    {
        if (source < flowMemoReference)
        {
            flowMemoReference = source;
        }
    }

    private long FlowMemoCreated() => (long)Types.Count + TransientSymbols.Count + Signatures.Count;

    private FlowFrame FlowFrameBegin()
    {
        var frame = new FlowFrame(FlowMemoNextSerial(), flowMemoTransient, flowMemoReference, flowMemoHeight, flowMemoFlags,
            instantiationCount, totalInstantiationCount, FlowMemoCreated(), activeTypeMappersCount > 0);
        flowMemoTransient = FlowMemoUntainted;
        flowMemoReference = FlowMemoUntainted;
        flowMemoHeight = 0;
        flowMemoFlags = 0;
        return frame;
    }

    private void FlowFrameCounters(in FlowFrame frame)
    {
        if (totalInstantiationCount != frame.TotalInstantiationCount || frame.InInstantiation || FlowMemoCreated() != frame.Created)
        {
            flowMemoFlags |= FlowMemoEffects;
        }
        if (frame.InstantiationCount == 0 || instantiationCount != frame.InstantiationCount)
        {
            flowMemoFlags |= FlowMemoCountReset;
        }
    }

    /// <summary>Ends a frame whose sub-walk has height <paramref name="height"/> (flowmemo.rs end_with).</summary>
    private FlowFrameTaint FlowFrameEndWith(in FlowFrame frame, ushort height)
    {
        FlowFrameCounters(frame);
        var taint = new FlowFrameTaint(
            flowMemoTransient < frame.Start ? flowMemoTransient : FlowMemoUntainted,
            flowMemoReference < frame.Start ? flowMemoReference : FlowMemoUntainted,
            flowMemoFlags);
        flowMemoTransient = Math.Min(flowMemoTransient, frame.SavedTransient);
        flowMemoReference = Math.Min(flowMemoReference, frame.SavedReference);
        var reported = height == ushort.MaxValue ? height : (ushort)(height + 1);
        flowMemoHeight = Math.Max(frame.SavedHeight, reported);
        flowMemoFlags |= frame.SavedFlags;
        return taint;
    }

    /// <summary>Ends a frame that computed its result: its height is what its children reported (flowmemo.rs end).</summary>
    private FlowFrameTaint FlowFrameEnd(in FlowFrame frame, out ushort height)
    {
        height = flowMemoHeight;
        return FlowFrameEndWith(frame, height);
    }

    /// <summary>
    /// A sharedFlows value's taint reaches the frames that consume it (flowmemo.rs flow_memo_consume): a transient source
    /// that is no longer active (the value outlived its loop analysis) taints every active frame.
    /// </summary>
    private void FlowMemoConsume(uint transient, uint reference)
    {
        if (transient != FlowMemoUntainted)
        {
            var live = false;
            if (transient != 0)
            {
                for (var i = 0; i < flowLoopCount && !live; i++)
                {
                    live = flowLoopSerials[i] == transient;
                }
            }
            FlowMemoTaint(live ? transient : 0);
        }
        if (reference != FlowMemoUntainted)
        {
            FlowMemoTaintReference(reference);
        }
    }

    /// <summary>
    /// The walk's memo key, computed once per walk (flowmemo.rs compute_flow_memo_key): only identifiers, `this` and
    /// chains of plain property accesses on them get one. Computing it has no side effects: it reads an identifier's
    /// resolved symbol only if it is cached.
    /// </summary>
    private bool TryGetFlowMemoKey(FlowState f, out FlowMemoKey key)
    {
        key = default;
        var reference = f.Reference;
        if (IsSyntheticReference(reference) || IsSyntheticElementAccess(reference))
        {
            return false;
        }
        var store = StoreOf(reference);
        ulong root;
        ulong chain;
        switch (store.Kind(reference.Node))
        {
            case SyntaxKind.Identifier:
            {
                var symbol = FlowMemoRootSymbol(store, reference);
                if (symbol.IsNone)
                {
                    return false;
                }
                root = ((ulong)(uint)symbol.File << 32) | (uint)symbol.Index;
                chain = 0;
                break;
            }
            case SyntaxKind.ThisKeyword:
                root = 0;
                chain = 1;
                break;
            case SyntaxKind.PropertyAccessExpression:
            {
                ulong h1 = 0xcbf29ce484222325UL, h2 = 0x9E3779B97F4A7C15UL;
                if (!HashFlowMemoReference(store, reference, ref h1, ref h2))
                {
                    return false;
                }
                root = h1 | (1UL << 63);
                chain = h2 | 2;
                break;
            }
            default:
                return false;
        }
        var container = f.FlowContainer.IsNone ? ulong.MaxValue : ((ulong)(uint)f.FlowContainer.File << 32) | (uint)f.FlowContainer.Node.Index;
        key = new FlowMemoKey(root, chain, ((ulong)(uint)f.DeclaredType.Id << 32) | (uint)f.InitialType.Id, container);
        return true;
    }

    /// <summary>The symbol an identifier reference resolved to, if cached, unless it is `this` in a type query (flowmemo.rs flow_memo_root_symbol).</summary>
    private SymbolRef FlowMemoRootSymbol(NodeStore store, NodeRef node)
    {
        if (!SymbolNodeLinks.Has(node))
        {
            return SymbolRef.None;
        }
        var symbol = SymbolNodeLinks.Get(node).ResolvedSymbol;
        if (symbol.IsNone || symbol == unknownSymbol || IsThisInTypeQuery(store, node.Node))
        {
            return SymbolRef.None;
        }
        return symbol;
    }

    private static void FlowMemoMix(ref ulong h1, ref ulong h2, ulong value)
    {
        h1 = (h1 ^ value) * 0x100000001b3UL;
        h2 = System.Numerics.BitOperations.RotateLeft((h2 ^ value) * 0xFF51AFD7ED558CCDUL, 31) * 0xC4CEB9FE1A85EC53UL;
    }

    /// <summary>The structure of a reference (flowmemo.rs write_flow_memo_reference), hashed.</summary>
    private bool HashFlowMemoReference(NodeStore store, NodeRef node, ref ulong h1, ref ulong h2)
    {
        switch (store.Kind(node.Node))
        {
            case SyntaxKind.Identifier:
            {
                var symbol = FlowMemoRootSymbol(store, node);
                if (symbol.IsNone)
                {
                    return false;
                }
                FlowMemoMix(ref h1, ref h2, 'I');
                FlowMemoMix(ref h1, ref h2, ((ulong)(uint)symbol.File << 32) | (uint)symbol.Index);
                return true;
            }
            case SyntaxKind.ThisKeyword:
                FlowMemoMix(ref h1, ref h2, 'T');
                return true;
            case SyntaxKind.PropertyAccessExpression:
            {
                if ((store.Flags(node.Node) & NodeFlags.OptionalChain) != 0 || !HashFlowMemoReference(store, At(node, store.Expression(node.Node)), ref h1, ref h2))
                {
                    return false;
                }
                var name = store.Texts.Span(store.EscapedText(store.Name(node.Node)));
                FlowMemoMix(ref h1, ref h2, 'P');
                FlowMemoMix(ref h1, ref h2, (ulong)name.Length);
                foreach (var c in name)
                {
                    FlowMemoMix(ref h1, ref h2, c);
                }
                return true;
            }
            default:
                return false;
        }
    }

    private static int FlowMemoSlotIndex(FlowRef flow, in FlowMemoKey key)
    {
        var h = ((((ulong)(uint)flow.File << 32) | (uint)flow.Node.Index) * 0x9E3779B97F4A7C15UL) ^ key.Root ^ System.Numerics.BitOperations.RotateLeft(key.Chain, 29) ^
            System.Numerics.BitOperations.RotateLeft(key.Types, 17) ^ System.Numerics.BitOperations.RotateLeft(key.Container, 43);
        return (int)((h * 0xFF51AFD7ED558CCDUL) >> (64 - FlowMemoBits));
    }

    private bool FlowMemoLookup(FlowRef flow, in FlowMemoKey key, out FlowMemoHit hit)
    {
        hit = default;
        if (flowMemoSlots is null)
        {
            return false;
        }
        ref var slot = ref flowMemoSlots[FlowMemoSlotIndex(flow, key)];
        if (!slot.Used || slot.Flow != flow || slot.Key != key)
        {
            return false;
        }
        hit = new FlowMemoHit(slot.Type, slot.Height, slot.Epoch, slot.CountReset);
        return true;
    }

    private void FlowMemoStore(FlowRef flow, in FlowMemoKey key, Type type, ushort height, uint epoch, bool countReset)
    {
        flowMemoSlots ??= new FlowMemoSlot[1 << FlowMemoBits];
        flowMemoSlots[FlowMemoSlotIndex(flow, key)] = new FlowMemoSlot
        {
            Flow = flow, Key = key, Type = type, Epoch = epoch, Height = height, CountReset = countReset, Used = true,
        };
    }

    /// <summary>
    /// Whether a fresh walk of the frame now could read a value the memo's walk did not, or leave the instantiation count
    /// elsewhere (flowmemo.rs flow_memo_consult_ok).
    /// </summary>
    private bool FlowMemoConsultOk(FlowState f, in FlowMemoHit hit) =>
        !f.MemoFaithful && flowLoopCount == 0 && (hit.Epoch == 0 || hit.Epoch == flowMemoTypeCacheEpoch) &&
        activeTypeMappersCount == 0 && (!hit.CountReset || instantiationCount == 0) && f.ImpureShared == 0;

    /// <summary>About one in four flow nodes, chosen by the node (flow.rs is_flow_memo_checkpoint).</summary>
    private static bool IsFlowMemoCheckpoint(FlowRef flow) =>
        ((((ulong)(uint)flow.File << 32) | (uint)flow.Node.Index) * 0x9E3779B97F4A7C15UL) >> 62 == 0;

    private bool LabelHasSeveralAntecedents(FlowRef label)
    {
        var n = 0;
        foreach (var _ in LabelAntecedents(label))
        {
            if (++n > 1)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether to ask the memo at this node, and its answer (flow.rs flow_memo_at_node): at the frame's first node, at a
    /// node that ends its iteration, and at checkpoints.
    /// </summary>
    private bool FlowMemoAtNode(FlowState f, FlowRef entryFlow, FlowRef flow, FlowFlags flags, int entryDepth, out FlowMemoHit hit, out bool endsIteration)
    {
        hit = default;
        endsIteration = (flags & (FlowFlags.Condition | FlowFlags.SwitchClause)) != 0 ||
            ((flags & (FlowFlags.BranchLabel | FlowFlags.LoopLabel)) != 0 && LabelHasSeveralAntecedents(flow));
        if (!endsIteration && flow != entryFlow)
        {
            if (!IsFlowMemoCheckpoint(flow))
            {
                return false;
            }
            flowMemoCheckpoints.Add(flow);
        }
        if (!FlowMemoLookup(flow, f.MemoKey, out hit) || !FlowMemoConsultOk(f, hit))
        {
            return false;
        }
        // the original walk of this sub-graph reaches at most `height` levels below here: it must not reach the limit
        if (entryDepth + hit.Height >= FlowDepthLimit)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Stores the frame's answer under its first node, the node that ended its iteration and the checkpoints it iterated
    /// past (flow.rs flow_memo_fill).
    /// </summary>
    private void FlowMemoFill(FlowState f, int checkpointStart, FlowRef entryFlow, FlowRef finalFlow, FlowType type, in FlowFrameTaint taint,
        ushort height, int steps, bool hasHit)
    {
        if (!taint.IsPure || (taint.Flags & FlowMemoEffects) != 0 || type.Incomplete || flowAnalysisDisabled || labelAntecedentOverrides.Count != 0)
        {
            return;
        }
        if (height == 0 && steps < 4 && !hasHit)
        {
            // a short iteration ending without recursion costs less to walk again than to keep
            return;
        }
        var epoch = (taint.Flags & FlowMemoTypeCache) != 0 ? flowMemoTypeCacheEpoch : 0;
        var countReset = (taint.Flags & FlowMemoCountReset) != 0;
        var key = f.MemoKey;
        if (entryFlow != finalFlow || !hasHit)
        {
            FlowMemoStore(entryFlow, key, type.Type, height, epoch, countReset);
        }
        if (finalFlow != entryFlow && !hasHit)
        {
            FlowMemoStore(finalFlow, key, type.Type, height, epoch, countReset);
        }
        for (var i = checkpointStart; i < flowMemoCheckpoints.Count; i++)
        {
            var checkpoint = flowMemoCheckpoints[i];
            if (checkpoint != finalFlow)
            {
                FlowMemoStore(checkpoint, key, type.Type, height, epoch, countReset);
            }
        }
    }

    /// <summary>Records a sharedFlows entry with the memo fields of the frame that computed it.</summary>
    private void RecordSharedFlow(FlowRef flow, FlowType type, FlowSharedMemo memo)
    {
        SetAt(sharedFlowNodes, sharedFlowCount, flow);
        SetAt(sharedFlowTypes, sharedFlowCount, type);
        SetAt(sharedFlowMemo, sharedFlowCount, memo);
        sharedFlowCount++;
    }

    /// <summary>
    /// resolvingSignature as a contextual signature: not the final one (tsrs checker_14.rs, the flow memo taints every
    /// active frame).
    /// </summary>
    private Signature FlowMemoResolvingSignature()
    {
        FlowMemoTaint(0);
        return resolvingSignature;
    }

    /// <summary>narrowType's inline limit was reached: what it narrows depends on the inline depth (false: no inlining).</summary>
    private bool FlowMemoInlineLimit()
    {
        FlowMemoTaint(0);
        return false;
    }

    /// <summary>
    /// isConstantReference(f.reference) (flow.rs is_constant_reference_of_walk): for an access expression it reads the
    /// property symbol the reference node resolved to, which another reference with the same key may not share.
    /// </summary>
    private bool IsConstantReferenceOfWalk(FlowState f)
    {
        var reference = f.Reference;
        if (IsSyntheticReference(reference) || IsSyntheticElementAccess(reference) || Utilities.IsAccessExpression(StoreOf(reference), reference.Node))
        {
            FlowMemoTaintReference(f.WalkFloor);
        }
        return IsConstantReference(reference);
    }

    /// <summary>checker.ts flowTypeCache = undefined, with the memo's epoch (flowmemo.rs take_flow_type_cache).</summary>
    private (Dictionary<NodeRef, Type>? Cache, uint Epoch) TakeFlowTypeCache()
    {
        var saved = (flowTypeCache, flowMemoTypeCacheEpoch);
        flowTypeCache = null;
        flowMemoTypeCacheEpoch = FlowMemoNextSerial();
        return saved;
    }

    private void RestoreFlowTypeCache((Dictionary<NodeRef, Type>? Cache, uint Epoch) saved)
    {
        flowTypeCache = saved.Cache;
        flowMemoTypeCacheEpoch = saved.Epoch;
    }
}
