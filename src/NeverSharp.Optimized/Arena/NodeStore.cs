// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's node arena (pre-registration §5.4), step O6b variant C's layout.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// The nodes of one parse. A <see cref="Node"/> is an index into this store; everything about a node is read and
/// written through it, so the layout below can change without touching the parser or the factory.
/// <para>
/// Layout (O6b variant C, header plus per-kind payload arenas): one 20-byte header per node (kind and payload
/// index in one int, flags, pos, end, transform flags) in an array without references on the pinned object heap,
/// which the collector neither moves nor scans. The fields of a node class form one payload struct
/// ({Class}Payload, generated from the schema with an explicit layout: Generated/NodeStore.g.cs). The payloads
/// live in typed arenas, one per payload shape (size and reference offsets: kinds whose payloads have the same
/// shape share an arena, 17 shapes for 194 classes), created with the shape's first node and kept inline in the
/// store; the header holds the node's index in its arena. A field of one class is read through the typed
/// payload; a field several classes share at its offset in the payload of the node's kind (a table per field).
/// Fields the parser rarely sets (parent, original node, modifier flags cache, JSDoc, emit node) are side
/// tables, allocated on first use. Header 0 is <see cref="Node.None"/>: its fields read as undefined.
/// </para>
/// <para>
/// Sizing (tuning corpus: 11.5 characters per node on average, but 2.4 to 37 for the middle 90 % of the files):
/// the headers start small (a node per 48 characters) and an arena starts with its shape's share of a node per 16
/// characters; a full array grows to the count extrapolated from how far the parser has come in the text.
/// </para>
/// <para>
/// Speculative parsing discards the nodes it created by truncating to a <see cref="Mark"/> taken before
/// (<see cref="GetMark"/>, <see cref="Truncate"/>): the discarded headers name the payloads to release, which are
/// the tails of their arenas because payloads are allocated in node order.
/// </para>
/// </summary>
internal sealed partial class NodeStore
{
    // Kind (SyntaxKind.Count is below 512) and payload index share the header's first int.
    private const int KindBits = 9;
    private const int KindMask = (1 << KindBits) - 1;
    private const int MaxPayloads = 1 << (32 - KindBits);

    // Offset tables: the kind's class has no such field.
    private const int NoField = 0xFFFF;

    // ShapeIds: the kind's class has no payload.
    private const int NoShape = 0xFF;

    private struct Header
    {
        public int KindPayload;
        public NodeFlags Flags;
        public int Pos;
        public int End;
        public TransformFlags TransformFlags;
    }

    /// <summary>The payload arena of one shape: a Shape{n}[] (null until the shape's first node) and its fill level.</summary>
    private struct Arena
    {
        public Array? Items;
        public int Count;
        public int PayloadSize;
    }

    /// <summary>The number of nodes when the mark was taken; truncating to it discards everything created after it.</summary>
    internal readonly struct Mark
    {
        internal readonly int Nodes;

        internal Mark(int nodes)
        {
            Nodes = nodes;
        }
    }

    private Header[] headers;
    private int count;
    private readonly int expectedNodes;

    // One arena per payload shape, inline (ShapeIds maps a kind to its shape).
    private Arenas arenas;

    // Side tables, indexed by node, created on first use.
    private int[]? parents;
    private int[]? originals;
    private int[]? modifierFlagsCaches;
    private Dictionary<int, List<Node>>? jsDocs;
    private int jsDocMaxIndex;

    // TypeScript's node.emitNode (factory data the parser does not create); keyed by node index, rarely used.
    private Dictionary<int, object>? emitNodes;

    private readonly int textLength;

    /// <summary>A store for a source text of the given length (the arrays grow as needed).</summary>
    public NodeStore(int textLength)
    {
        // Sizes chosen by simulating the store's growth on the tuning corpus (fewest bytes allocated in total).
        this.textLength = textLength;
        expectedNodes = 16 + (textLength / 16);
        headers = NewHeaders(32 + (textLength / 48));
        // Header 0 is Node.None.
        headers[0] = new Header { KindPayload = (int)SyntaxKind.Unknown, Pos = -1, End = -1 };
        count = 1;
    }

    // Uninitialized: Create writes every field of a header.
    // On the pinned object heap: measured faster than the small object heap (the collector never copies them).
    private static Header[] NewHeaders(int length) => GC.AllocateUninitializedArray<Header>(length, pinned: true);

    /// <summary>The number of headers in use (including <see cref="Node.None"/>'s).</summary>
    internal int Count => count;

    /// <summary>A new node of the given kind: pos and end -1, flags none, every field undefined (utilities.ts: function Node).</summary>
    internal Node Create(SyntaxKind kind)
    {
        if (count == headers.Length)
        {
            GrowHeaders();
        }
        var payload = 0;
        int shape = ShapeIds[(int)kind & KindMask];
        if (shape != NoShape)
        {
            ref var arena = ref ArenaAt(shape);
            if (arena.Items is null)
            {
                AddArena(ref arena, shape, kind);
            }
            payload = arena.Count;
            if (payload == arena.Items!.Length)
            {
                GrowArena(ref arena, shape);
            }
            arena.Count = payload + 1;
        }
        var index = count++;
        ref var h = ref headers[index];
        h.KindPayload = (int)kind | (payload << KindBits);
        h.Flags = default;
        h.Pos = -1;
        h.End = -1;
        h.TransformFlags = default;
        return new Node(index);
    }

    private void GrowHeaders()
    {
        var grown = NewHeaders((int)Math.Min(Array.MaxLength, Extrapolate(count, 64)));
        // Also the headers of discarded nodes: a stale handle still reads a valid kind and payload.
        headers.CopyTo(grown, 0);
        headers = grown;
    }

    /// <summary>
    /// A new capacity for an array holding <paramref name="filled"/> items of the nodes parsed so far: the count
    /// extrapolated to the whole source text from how far the parser has come (the furthest end among the latest
    /// nodes), with a margin of a quarter; at least 1.25 times the count, at most 8 times while the parser is in the
    /// first 64th of the text and 16 times after.
    /// </summary>
    private long Extrapolate(int filled, int minimum)
    {
        var position = 1;
        for (var i = Math.Max(1, count - 32); i < count; i++)
        {
            position = Math.Max(position, headers[i].End);
        }
        var estimate = ((long)filled * textLength / position) + (filled / 4) + 2;
        var lower = filled + (filled / 4) + 1;
        var upper = Math.Max(lower, (long)filled * (position * 64L < textLength ? 8 : 16));
        return Math.Max(minimum, Math.Clamp(estimate, lower, upper));
    }

    //// Payload arenas.

    /// <summary>Creates the arena of a payload shape, for the shape's share of the expected nodes.</summary>
    private void AddArena(ref Arena arena, int shape, SyntaxKind kind)
    {
        var capacity = Math.Max(1, (int)(((long)expectedNodes * ShapeShares[shape]) >> 16));
        arena.Items = NewPayloads(shape, capacity);
        arena.PayloadSize = PayloadSizes[(int)kind];
    }

    private void GrowArena(ref Arena arena, int shape)
    {
        var items = arena.Items!;
        if (items.Length >= MaxPayloads)
        {
            throw new InvalidOperationException($"More than {MaxPayloads} nodes of one payload shape in a source file");
        }
        var grown = NewPayloads(shape, (int)Math.Min(MaxPayloads, Extrapolate(arena.Count, 4)));
        Array.Copy(items, grown, arena.Count);
        arena.Items = grown;
    }

    /// <summary>The payload of a node (header <paramref name="h"/>) whose class's payload struct is <typeparamref name="T"/>, of the given shape.</summary>
    private ref T PayloadOf<T>(int h, int shape)
        where T : struct => ref Unsafe.Add(ref MemoryMarshal.GetReference(Unsafe.As<T[]>(ArenaAt(shape).Items).AsSpan()), h >>> KindBits);

    /// <summary>
    /// A byte of the payload of a node (header <paramref name="h"/>): <paramref name="field"/> is an offset table
    /// entry, the payload's shape in its high five bits and the byte's offset in its low eleven.
    /// </summary>
    private ref byte PayloadByte(int h, int field)
    {
        ref var arena = ref ArenaAt(field >> 11);
        // The span only yields the start of the array's elements (its length counts payloads, not bytes).
        return ref Unsafe.Add(ref MemoryMarshal.GetReference(Unsafe.As<byte[]>(arena.Items).AsSpan()), ((nint)(h >>> KindBits) * arena.PayloadSize) + (field & 0x7FF));
    }

    // The arena of a shape (below ShapeCount: unchecked access). A node's payload index is below its arena's
    // count, so the payload accesses above need no bounds checks either.
    private ref Arena ArenaAt(int shape) => ref Unsafe.Add(ref Unsafe.As<Arenas, Arena>(ref arenas), shape);

    internal Mark GetMark() => new(count);

    /// <summary>Discards every node created after <paramref name="mark"/> (speculative parsing that is rolled back).</summary>
    internal void Truncate(Mark mark)
    {
        Debug.Assert(mark.Nodes <= count, "Mark is beyond the store's fill level");
        for (var i = mark.Nodes; i < count; i++)
        {
            var h = headers[i].KindPayload;
            int shape = ShapeIds[h & KindMask];
            if (shape != NoShape)
            {
                // The first discarded node of a shape holds the lowest discarded payload of its arena; the
                // released payloads are cleared for reuse.
                ref var arena = ref ArenaAt(shape);
                var payload = h >>> KindBits;
                if (payload < arena.Count)
                {
                    Array.Clear(arena.Items!, payload, arena.Count - payload);
                    arena.Count = payload;
                }
            }
        }
        ClearColumn(parents, mark.Nodes, count);
        ClearColumn(originals, mark.Nodes, count);
        ClearColumn(modifierFlagsCaches, mark.Nodes, count);
        if (jsDocs is not null && jsDocMaxIndex >= mark.Nodes)
        {
            jsDocMaxIndex = 0;
            foreach (var key in jsDocs.Keys)
            {
                if (key >= mark.Nodes)
                {
                    jsDocs.Remove(key);
                }
                else
                {
                    jsDocMaxIndex = Math.Max(jsDocMaxIndex, key);
                }
            }
        }
        if (emitNodes is not null)
        {
            for (var i = mark.Nodes; i < count; i++)
            {
                emitNodes.Remove(i);
            }
        }
        count = mark.Nodes;
    }

    //// Base fields (utilities.ts: function Node).

    public SyntaxKind Kind(Node node) => (SyntaxKind)(headers[node.Index].KindPayload & KindMask);

    public int Pos(Node node) => headers[node.Index].Pos;

    public void SetPos(Node node, int value) => headers[node.Index].Pos = value;

    public int End(Node node) => headers[node.Index].End;

    public void SetEnd(Node node, int value) => headers[node.Index].End = value;

    public NodeFlags Flags(Node node) => headers[node.Index].Flags;

    public void SetFlags(Node node, NodeFlags value) => headers[node.Index].Flags = value;

    public TransformFlags TransformFlags(Node node) => headers[node.Index].TransformFlags;

    public void SetTransformFlags(Node node, TransformFlags value) => headers[node.Index].TransformFlags = value;

    public Node Parent(Node node) => new(ColumnValue(parents, node.Index));

    public void SetParent(Node node, Node value) => SetColumnValue(ref parents, node.Index, value.Index);

    public ModifierFlags ModifierFlagsCache(Node node) => (ModifierFlags)ColumnValue(modifierFlagsCaches, node.Index);

    public void SetModifierFlagsCache(Node node, ModifierFlags value) => SetColumnValue(ref modifierFlagsCaches, node.Index, (int)value);

    public Node Original(Node node) => new(ColumnValue(originals, node.Index));

    public void SetOriginal(Node node, Node value) => SetColumnValue(ref originals, node.Index, value.Index);

    public object? EmitNode(Node node) => emitNodes is not null && emitNodes.TryGetValue(node.Index, out var value) ? value : null;

    public void SetEmitNode(Node node, object? value)
    {
        if (value is null)
        {
            emitNodes?.Remove(node.Index);
            return;
        }
        (emitNodes ??= [])[node.Index] = value;
    }

    //// Side tables.

    private static int ColumnValue(int[]? column, int index) => column is not null && (uint)index < (uint)column.Length ? column[index] : 0;

    private void SetColumnValue(ref int[]? column, int index, int value)
    {
        if (column is null || index >= column.Length)
        {
            if (value == 0)
            {
                return;
            }
            Array.Resize(ref column, headers.Length);
        }
        column[index] = value;
    }

    private static void ClearColumn(int[]? column, int from, int to)
    {
        if (column is not null && from < column.Length)
        {
            column.AsSpan(from, Math.Min(to, column.Length) - from).Clear();
        }
    }

    private List<Node>? JsDocEntry(Node node) => jsDocs is not null && jsDocs.TryGetValue(node.Index, out var value) ? value : null;

    private void SetJsDocEntry(Node node, List<Node>? value)
    {
        if (value is null)
        {
            jsDocs?.Remove(node.Index);
            return;
        }
        (jsDocs ??= [])[node.Index] = value;
        jsDocMaxIndex = Math.Max(jsDocMaxIndex, node.Index);
    }

    private static void ThrowNoField(SyntaxKind kind, string field)
    {
        throw new InvalidOperationException($"{kind} has no field {field}");
    }
}
