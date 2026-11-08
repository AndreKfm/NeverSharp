// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// Portions Copyright 2026 Max Schwenk and the tsrs contributors
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts (SymbolLinks, NodeLinks) and src/compiler/checker.ts (getSymbolLinks, getNodeLinks)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/types.go (per-kind link structs: ValueSymbolLinks, AliasSymbolLinks, ...,
// NodeLinks, TypeNodeLinks, SignatureLinks, ...) and internal/core/linkstore.go (LinkStore[K, V])
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: per-checker stores keyed by SymbolRef / NodeRef (docs/phase3-checker-layout.md); module C0 defines the
// stores and the first link kinds, the checker modules add the kinds they need (one store per kind, as typescript-go).

using System.Runtime.CompilerServices;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// One int per symbol of a checker's program (module CO2): binder symbol <c>i</c> of file <c>f</c> at
/// <c>bases[f] + i</c> (the file regions are sized by the files' symbol record counts; binder indices are dense),
/// transient symbols after the last file. The ints live in pages of <see cref="Size"/> allocated on first write, so a
/// checker has a few hundred pages per kind instead of one array per file, and none of them holds references. Module
/// AR1 (Track O / tsrs-only; tsrs crates/tsrs_checker/src/links.rs IdLinkStore's dense pages,
/// notes/mem-dense-link-tables.md): the pages are
/// native blocks of the checker's <see cref="CheckerArena"/>, not arrays the collector copies.
/// </summary>
internal sealed unsafe class SymbolSlots
{
    private const int Shift = 14;
    private const int Size = 1 << Shift;
    private const int Mask = Size - 1;

    private readonly int[] bases; // [file] -> start of its region; [0] -> start of the transient symbols
    private readonly CheckerArena arena;
    private nint[] pages; // native int[Size] blocks (0: not allocated)

    internal SymbolSlots(int[] bases, CheckerArena arena)
    {
        this.bases = bases;
        this.arena = arena;
        pages = new nint[(bases[0] >> Shift) + 16];
    }

    /// <summary>The regions of the program's files (shared by the slot tables of one checker).</summary>
    internal static int[] Bases(Program program)
    {
        var files = program.SourceFiles;
        var bases = new int[files.Count + 2];
        var next = 0;
        for (var i = 0; i < files.Count; i++)
        {
            bases[i + 1] = next;
            next = checked(next + files[i].Binding!.SymbolRecordCount);
        }
        bases[files.Count + 1] = next; // the end of the last file's region
        bases[0] = next;
        return bases;
    }

    /// <summary>The slot of <paramref name="symbol"/> (its page allocated if needed).</summary>
    internal ref int Slot(SymbolRef symbol)
    {
        var i = bases[symbol.File] + symbol.Index;
        if (symbol.File != 0 && i >= bases[symbol.File + 1])
        {
            throw new InvalidOperationException("binder symbol index beyond its file's symbol count");
        }
        var p = i >> Shift;
        if (p >= pages.Length)
        {
            Array.Resize(ref pages, Math.Max(p + 1, pages.Length * 2));
        }
        var page = pages[p];
        if (page == 0)
        {
            page = pages[p] = (nint)arena.Allocate(Size * sizeof(int));
        }
        return ref ((int*)page)[i & Mask];
    }

    /// <summary>The value of <paramref name="symbol"/>'s slot (0 when it has none).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int Peek(SymbolRef symbol)
    {
        // (no region check: a slot is only written through Slot, which checks it)
        var i = bases[symbol.File] + symbol.Index;
        var p = i >> Shift;
        return p < pages.Length && pages[p] is var page and not 0 ? ((int*)page)[i & Mask] : 0;
    }
}

/// <summary>
/// typescript-go's <c>core.LinkStore[*ast.Symbol, V]</c> for one link kind <typeparamref name="T"/>: the links of a
/// symbol, created zeroed on first access. Keyed by <see cref="SymbolRef"/> through the checker's
/// <see cref="SymbolSlots"/> layout (module CO2; before: a slot array per file), the records in chunks in creation
/// order. Only symbols that have links of this kind cost a record. Module AR1: slot pages and record chunks in the
/// checker's <see cref="CheckerArena"/> (tsrs crates/tsrs_checker/src/links.rs IdLinkStore: link records outside any
/// collector).
/// </summary>
internal sealed class SymbolLinkStore<T>
    where T : unmanaged
{
    private readonly SymbolSlots slots; // record index (0: none)
    private readonly NativeChunks<T> records; // records[0] unused
    private int count;

    internal SymbolLinkStore(int[] slotBases, CheckerArena arena)
    {
        slots = new SymbolSlots(slotBases, arena);
        records = new NativeChunks<T>(arena);
    }

    /// <summary>The links of <paramref name="symbol"/>, created zeroed if it has none (getSymbolLinks).</summary>
    internal ref T Get(SymbolRef symbol)
    {
        ref var slot = ref slots.Slot(symbol);
        if (slot == 0)
        {
            slot = ++count;
            records.EnsureIndex(slot);
        }
        return ref records[slot];
    }

    /// <summary>Whether <paramref name="symbol"/> has links of this kind (typescript-go LinkStore.Has).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool Has(SymbolRef symbol) => slots.Peek(symbol) != 0;
}

/// <summary>
/// typescript-go's <c>core.LinkStore[*ast.Node, V]</c> for one link kind <typeparamref name="T"/>: the links of a node,
/// created zeroed on first access. Node handles are record offsets (sparse), so per file an open-addressing map from
/// node handle to record index (linear probing, load at most 1/2, as the binder's node maps; module CO2: one array of
/// (handle, record) pairs per file instead of two), the records in one array. Module AR1 (Track O / tsrs-only; tsrs
/// crates/tsrs_checker/src/links.rs NodeLinkStore, notes/mem-dense-link-tables.md): the per-file maps are native blocks of the
/// checker's <see cref="CheckerArena"/>, and so are the records when they hold no references (in 4,096-record chunks
/// that never move); records with references stay in a managed array.
/// </summary>
internal sealed unsafe class NodeLinkStore<T>
    where T : struct
{
    private struct Entry
    {
        public int Node; // 0: free
        public int Record;
    }

    private const int ChunkShift = 12;
    private const int ChunkSize = 1 << ChunkShift;
    private const int ChunkMask = ChunkSize - 1;

    // (an intrinsic the compiler folds per instantiation)
    private static bool NativeRecords
    {
        get => !RuntimeHelpers.IsReferenceOrContainsReferences<T>();
    }

    private readonly CheckerArena arena;
    private readonly nint[] maps; // [file] -> native Entry[capacities[file]] (0: none yet)
    private readonly int[] capacities;
    private readonly int[] counts;
    private T[] records = []; // records[0] unused (records with references)
    private nint[] chunks = new nint[4]; // native record chunks (reference-free records)
    private int count;

    internal NodeLinkStore(int fileCount, CheckerArena arena)
    {
        this.arena = arena;
        maps = new nint[fileCount + 1];
        capacities = new int[fileCount + 1];
        counts = new int[fileCount + 1];
        if (!NativeRecords)
        {
            records = new T[64];
        }
    }

    /// <summary>The links of <paramref name="node"/>, created zeroed if it has none (getNodeLinks).</summary>
    internal ref T Get(NodeRef node)
    {
        var file = node.File;
        if ((counts[file] + 1) * 2 > capacities[file])
        {
            Grow(file);
        }
        var map = (Entry*)maps[file];
        var capacity = capacities[file];
        var mask = capacity - 1;
        var handle = node.Node.Index;
        for (var i = Slot(handle, capacity); ; i = (i + 1) & mask)
        {
            ref var entry = ref map[i];
            if (entry.Node == handle)
            {
                return ref Record(entry.Record);
            }
            if (entry.Node == 0)
            {
                var record = ++count;
                if (NativeRecords)
                {
                    var c = record >> ChunkShift;
                    if (c >= chunks.Length)
                    {
                        Array.Resize(ref chunks, chunks.Length * 2);
                    }
                    if (chunks[c] == 0)
                    {
                        chunks[c] = (nint)arena.Allocate((nint)ChunkSize * Unsafe.SizeOf<T>());
                    }
                }
                else if (record == records.Length)
                {
                    Array.Resize(ref records, records.Length * 2);
                }
                entry.Node = handle;
                entry.Record = record;
                counts[file]++;
                return ref Record(record);
            }
        }
    }

    private ref T Record(int record) =>
        ref NativeRecords
            ? ref Unsafe.Add(ref Unsafe.AsRef<T>((void*)chunks[record >> ChunkShift]), record & ChunkMask)
            : ref records[record];

    /// <summary>Whether <paramref name="node"/> has links of this kind.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool Has(NodeRef node)
    {
        var capacity = capacities[node.File];
        if (capacity == 0)
        {
            return false;
        }
        var map = (Entry*)maps[node.File];
        var mask = capacity - 1;
        var handle = node.Node.Index;
        for (var i = Slot(handle, capacity); ; i = (i + 1) & mask)
        {
            if (map[i].Node == handle)
            {
                return true;
            }
            if (map[i].Node == 0)
            {
                return false;
            }
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int Slot(int handle, int capacity) => (int)(((uint)handle * 0x9E3779B1u) >> (32 - System.Numerics.BitOperations.Log2((uint)capacity)));

    private void Grow(int file)
    {
        var old = (Entry*)maps[file];
        var oldCapacity = capacities[file];
        var capacity = Math.Max(16, oldCapacity * 2);
        var grown = (Entry*)arena.Allocate((nint)capacity * sizeof(Entry));
        var mask = capacity - 1;
        for (var k = 0; k < oldCapacity; k++)
        {
            var entry = old[k];
            if (entry.Node == 0)
            {
                continue;
            }
            var i = Slot(entry.Node, capacity);
            while (grown[i].Node != 0)
            {
                i = (i + 1) & mask;
            }
            grown[i] = entry;
        }
        maps[file] = (nint)grown;
        capacities[file] = capacity;
        arena.Free(old, (nint)oldCapacity * sizeof(Entry));
    }
}

/// <summary>types.go ValueSymbolLinks (checker.ts SymbolLinks: type, writeType, target, mapper, nameType, containingType).</summary>
internal struct ValueSymbolLinks
{
    public Type ResolvedType;
    public Type WriteType;
    public SymbolRef Target;
    public int Mapper;
    public Type NameType;
    public Type ContainingType;
    public bool FunctionOrConstructorChecked;
}

/// <summary>types.go AliasSymbolLinks.</summary>
internal struct AliasSymbolLinks
{
    public SymbolRef ImmediateTarget;
    public SymbolRef AliasTarget;
    public bool Referenced;
    public NodeRef TypeOnlyDeclaration;
    public byte TypeOnlyState; // links.typeOnlyDeclaration: 0 undefined, 1 false, 2 a declaration (module C8)
}

/// <summary>types.go ExportTypeLinks (checker.ts SymbolLinks.target and originatingImport of the module symbols
/// cloneTypeAsModuleType creates).</summary>
internal struct ExportTypeLinks
{
    public SymbolRef Target;
    public NodeRef OriginatingImport;
}

/// <summary>types.go DeclaredTypeLinks (declared types of type parameters, classes, interfaces, enums).</summary>
internal struct DeclaredTypeLinks
{
    public Type DeclaredType;
    public bool InterfaceChecked;
    public bool IndexSignaturesChecked;
    public bool TypeParametersChecked;
    public bool EnumChecked;
}

/// <summary>types.go NodeLinks (checker.ts NodeLinks.flags and the members typescript-go keeps with them).</summary>
internal struct NodeLinks
{
    public NodeCheckFlags Flags;
    public sbyte DeclarationRequiresScopeChange; // core.Tristate: 0 unknown, 1 false, 2 true
    public bool HasReportedStatementInAmbientContext;
}

/// <summary>types.go SymbolNodeLinks (checker.ts NodeLinks.resolvedSymbol).</summary>
internal struct SymbolNodeLinks
{
    public SymbolRef ResolvedSymbol;
}

/// <summary>types.go TypeNodeLinks (checker.ts NodeLinks.resolvedType, outerTypeParameters).</summary>
internal struct TypeNodeLinks
{
    public Type ResolvedType;
    public TypeList OuterTypeParameters;
}

/// <summary>types.go SignatureLinks (checker.ts NodeLinks.resolvedSignature, effectsSignature, decoratorSignature).</summary>
internal struct SignatureLinks
{
    public Signature ResolvedSignature;
    public Signature EffectsSignature;
    public Signature DecoratorSignature;
}
