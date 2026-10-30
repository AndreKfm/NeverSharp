// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's storage of binder output (module B0, docs/phase2-binder-layout.md).

using System.Diagnostics;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// The binder output of one source file (Binder.BindSourceFile): symbols, symbol tables, flow nodes, the binder's
/// per-node fields and the bind diagnostics. Owned by the <see cref="SourceFile"/> result (<see cref="SourceFile.Binding"/>)
/// like the node store; nothing in it is shared with other files or later parses, and one thread binds a file.
/// <para>
/// Layout (docs/phase2-binder-layout.md): symbols, symbol tables, flow nodes and the lists they use are records in
/// arrays of their own, referenced by int handles (<see cref="Symbol"/>, <see cref="SymbolTable"/>,
/// <see cref="FlowNode"/>; 0 is undefined), the counterpart of typescript-go's per-binder arenas (symbolArena,
/// flowNodeArena, flowListArena in internal/binder/binder.go). Lists that grow by appending (declarations, label
/// antecedents, table entries) are chains of links in one link array, kept in insertion order (TypeScript's arrays and
/// Map order; typescript-go prepends flow list entries, the dumps follow TypeScript). The per-node fields (symbol,
/// localSymbol, locals, nextContainer, endFlowNode, returnFlowNode, fallthroughFlowNode) live in open-addressing maps
/// keyed by node handle, since node handles are record offsets and the node records have no room for them; flowNode,
/// which about half of all nodes carry, in a dense array indexed by handle >> 2 (step BO1; records are at least 5 ints
/// long). Everything is read and written through this class, so the layout can change without touching the binder.
/// </para>
/// <para>
/// Memory (step BO1, as the node store's in step O9): every array is a region of one range of the binding thread's
/// binder slab (Slab.cs), native memory the thread fills binding after binding; regions are sized from the file's node
/// count (on the tuning corpus and vscode a few per cent of the files outgrow one), and an array that outgrows its
/// region moves to a region twice the size, the old one left unused. The range reaches to the end of the slab while
/// the binding runs (<see cref="ThreadSlabs.ActiveBinder"/>) and shrinks to the regions in use at <see cref="Commit"/>;
/// the next binding on the thread continues after it. The result holds its slabs (<see cref="Release"/>, called by
/// <see cref="SourceFile.Release"/>; the slab's finalizer is the backstop): no array per file, and nothing for the
/// collector to trace.
/// </para>
/// </summary>
internal sealed unsafe class BinderStore
{
    // Bits of a symbol's rare boolean members; "Set" means the member is defined (TypeScript distinguishes undefined).
    [Flags]
    private enum SymbolBits : byte
    {
        None = 0,
        ConstEnumOnlyModuleSet = 1,
        ConstEnumOnlyModule = 2,
        ReplaceableByMethodSet = 4,
        ReplaceableByMethod = 8,
    }

    private struct SymbolRecord
    {
        public SymbolFlags Flags;
        public TextRef Name;
        public int FirstDeclaration; // link chain of declaration nodes; 0: declarations is undefined
        public int LastDeclaration;
        public Node ValueDeclaration;
        public Symbol Parent;
        public Symbol ExportSymbol;
        public SymbolTable Members;
        public SymbolTable Exports;
    }

    // Step BO4: a symbol's rare members, in a map keyed by symbol (symbols that have neither take no room).
    private struct SymbolExtras
    {
        public SymbolBits Bits;
        public int Id; // checker.ts/utilities.ts getSymbolId: 0 until assigned
    }

    // A link of a chain: a handle (node, flow node) and the next link (0: end).
    private struct Link
    {
        public int Value;
        public int Next;
    }

    private struct TableRecord
    {
        public int First; // entry chain in insertion order
        public int Last;
        public int Count;
    }

    private struct EntryRecord
    {
        public TextRef Name;
        public Symbol Symbol;
        public int Next;
        public int Table;      // the table the entry belongs to
        public int Hash;       // hash of the table and the name's characters
        public int NextInBucket;
    }

    // Step BO4: two words after the flags, read by kind (labels have neither a node nor a single antecedent, the other
    // kinds no antecedent list); the two data words of switch clauses and reduce labels in a map keyed by flow node.
    private struct FlowRecord
    {
        public FlowFlags Flags;
        public int X;                   // labels: first link of the antecedents (0: undefined); others: the node it refers to (switch clause: the switch statement)
        public int Y;                   // labels: last link of the antecedents; others: the antecedent
    }

    private struct FlowData
    {
        public int A;                   // switch clause: clauseStart; reduce label: target
        public int B;                   // switch clause: clauseEnd; reduce label: first link of its antecedents
    }

    /// <summary>The per-node fields of containers and of the nodes that end or fall through a flow.</summary>
    private struct ContainerFields
    {
        public SymbolTable Locals;
        public Node NextContainer;
        public FlowNode EndFlowNode;
        public FlowNode ReturnFlowNode;
        public FlowNode FallthroughFlowNode;
    }

    // The arrays (regions of the binder range): capacity records each, of which ...Used are in use; record 0 of each is
    // the undefined handle's (zero).
    private SymbolRecord* symbols;
    private int symbolsCapacity;
    private int symbolsUsed = 1; // record 0 is Symbol.None
    private TableRecord* tables;
    private int tablesCapacity;
    private int tablesUsed = 1;
    private EntryRecord* entries;
    private int entriesCapacity;
    private int entriesUsed = 1;
    // Module B1: a hash index over the entries of all tables (chains of entries by hash of table and name), so
    // Map.get and Map.set do not scan a table; grown ×2 at load 1/2 (bucketsLength, a power of two).
    private int* buckets;
    private int bucketsLength;
    private FlowRecord* flows;
    private int flowsCapacity;
    private int flowsUsed = 1;
    private Link* links;
    private int linksCapacity;
    private int linksUsed = 1;

    // Step BO1: node.flowNode, dense (flowNodeLength handles >> 2; 0 = undefined).
    private int* flowNodeArray;
    private int flowNodeLength;

    // Step BO1: the binder range (Slab.cs): the regions above are allocated from top on; the thread's slabs while the
    // binding runs (null afterwards, and for a binding that started while another one ran on the thread).
    private SlabRange range;
    private long top;
    private ThreadSlabs? local;

    // Step BO3: a small file binds into the thread's scratch block (memory the thread reuses binding after binding, so
    // it is already mapped and mostly in cache; the binder slab's fresh pages are what a binding into it pays for), and
    // Commit copies what is in use, compacted, into the binder slab. While scratch is set, range.Start and
    // range.Capacity are the current block (range.Slab stays null) and regions are allocated from top on; blocks left by
    // an overflow stay alive in overflowBlocks until Commit. A block is a slab the thread holds, sized from twice the
    // estimate of the bindings it served (at least ScratchBlockSize); as for the thread's other slabs, a small one is a
    // pinned managed array (a thread that binds only a few files, as the dump tool's threads do, leaves garbage the
    // collector sees), a large one native memory, freed when the thread replaces it or by the finalizer.
    private const long ScratchLimit = 384 << 10;
    private const long ScratchBlockSize = 64 << 10;

    [ThreadStatic]
    private static Slab? scratchBlock;

    [ThreadStatic]
    private static List<Slab>? overflowBlocks;

    private bool scratch;

    /// <summary>
    /// Module B3: the rare flow fields of conditional expressions in return position (flowNodeWhenTrue,
    /// flowNodeWhenFalse) and of switch statements (possiblyExhaustive; only true values are stored).
    /// </summary>
    private struct FlowExtraFields
    {
        public FlowNode WhenTrue;
        public FlowNode WhenFalse;
        public bool PossiblyExhaustive;
    }

    // The per-node fields of declarations: node.symbol, and in a map of its own node.localSymbol, which only exported
    // declarations and a few others have (step BO4: the frequent map's slots are half the size).
    private NodeMap<Symbol> symbolFields;
    private NodeMap<Symbol> localSymbolFields;
    private NodeMap<ContainerFields> containerFields;
    private NodeMap<FlowExtraFields> flowExtraFields;

    // Step BO4: the rare members of symbols, keyed by symbol, and the data words of switch clauses and reduce labels,
    // keyed by flow node (NodeMaps whose keys are symbol or flow node handles).
    private NodeMap<SymbolExtras> symbolExtras;
    private NodeMap<FlowData> flowData;

    // Rare symbol members (UMD global exports, JavaScript assignment declarations): only files that have them pay.
    // Step BO4: what few files have (rare symbol members, diagnostics, the symbol ids assigned, JavaScript module
    // fields), in an object of its own created on first use, so that the store object stays small (a store is alive as
    // long as its result, and the collector copies what survives).
    private sealed class RareFields
    {
        public Dictionary<int, SymbolTable>? GlobalExports;
        public Dictionary<int, (int First, int Last)>? AssignmentDeclarationMembers;
        public List<DiagnosticWithLocation>? BindDiagnostics;
        public List<DiagnosticWithLocation>? BindSuggestionDiagnostics;
        public List<int>? AssignedSymbolIds;
        public Node CommonJsModuleIndicator;
        public SymbolTable JsGlobalAugmentations;
    }

    private RareFields? rare;

    private RareFields Rare => rare ??= new RareFields();

    /// <summary>
    /// The binder output of the file of <paramref name="nodes"/>, written into the binder slab of the current thread
    /// until <see cref="Commit"/>. The arrays are sized from the node count (per node on the tuning corpus and on vscode:
    /// 0.12 to 0.16 symbols, 0.08 to 0.12 tables, 0.10 to 0.13 table entries, 0.16 to 0.17 flow nodes, 0.19 to 0.22
    /// links, 0.12 to 0.14 declarations with a symbol, 0.02 to 0.04 containers). Step BO3: the record arrays well above
    /// the average (a file outgrowing one copies it; capacity that is never written costs neither a copy nor, in a fresh
    /// slab or the scratch block, a page), the zeroed ones (hash index, node maps) a little above it.
    /// </summary>
    internal BinderStore(NodeStore nodes)
    {
        Nodes = nodes;
        var n = nodes.Count;
        var symbolCapacity = (n / 4) + 16;
        var tableCapacity = (n / 5) + 16;
        var entryCapacity = (n / 4) + 16;
        var flowCapacity = (n / 4) + 16;
        var linkCapacity = (n / 3) + 16;
        var bucketCount = PowerOfTwo(((n / 6) + 8) * 2);
        var flowNodeCount = (nodes.RecordWords / 5) + 1;
        var declarationCount = NodeMap<Symbol>.CapacityFor((n / 6) + 8);
        var containerCount = NodeMap<ContainerFields>.CapacityFor((n / 24) + 4);
        var estimate = ((long)symbolCapacity * sizeof(SymbolRecord)) + ((long)tableCapacity * sizeof(TableRecord)) +
            ((long)entryCapacity * sizeof(EntryRecord)) + ((long)flowCapacity * sizeof(FlowRecord)) + ((long)linkCapacity * sizeof(Link)) +
            (((long)bucketCount + flowNodeCount) * sizeof(int)) + ((long)declarationCount * (sizeof(int) + sizeof(Symbol))) +
            ((long)containerCount * (sizeof(int) + sizeof(ContainerFields))) + (16 * 16);
        var thread = ThreadSlabs.Local;
        if (thread.ActiveBinder is null)
        {
            thread.ActiveBinder = this;
            local = thread;
            if (estimate <= ScratchLimit)
            {
                var block = scratchBlock;
                if (block is null || block.Size < estimate * 2)
                {
                    block?.RemoveHolder();
                    block = NewScratchBlock(Math.Max(estimate * 2, (block?.Size ?? 0) * 2));
                }
                UseScratch(block);
            }
            else
            {
                range.Open(thread, SlabKind.Binder, estimate);
            }
        }
        symbols = NewArray<SymbolRecord>(symbolCapacity);
        symbolsCapacity = symbolCapacity;
        tables = NewArray<TableRecord>(tableCapacity);
        tablesCapacity = tableCapacity;
        entries = NewArray<EntryRecord>(entryCapacity);
        entriesCapacity = entryCapacity;
        flows = NewArray<FlowRecord>(flowCapacity);
        flowsCapacity = flowCapacity;
        links = NewArray<Link>(linkCapacity);
        linksCapacity = linkCapacity;
        buckets = NewZeroed<int>(bucketCount);
        bucketsLength = bucketCount;
        flowNodeArray = NewZeroed<int>(flowNodeCount);
        flowNodeLength = flowNodeCount;
        symbolFields.Init(this, declarationCount);
        containerFields.Init(this, containerCount);
    }

    /// <summary>
    /// The end of the binding: the range shrinks to the regions in use and the thread's next binding starts after it;
    /// arrays that grow afterwards move to memory of the store's own. (Idempotent.)
    /// </summary>
    internal void Commit()
    {
        var thread = local;
        if (thread is null)
        {
            return;
        }
        if (scratch)
        {
            CommitScratch(thread);
        }
        else
        {
            range.Commit(top, thread, SlabKind.Binder);
        }
        if (ReferenceEquals(thread.ActiveBinder, this))
        {
            thread.ActiveBinder = null;
        }
        local = null;
    }

    /// <summary>Drops the store's holds on its slabs (SourceFile.Release): the binder output must not be read afterwards.</summary>
    internal void Release()
    {
        Commit();
        range.Release();
        top = 0;
        symbols = null;
        symbolsCapacity = 0;
        tables = null;
        tablesCapacity = 0;
        entries = null;
        entriesCapacity = 0;
        flows = null;
        flowsCapacity = 0;
        links = null;
        linksCapacity = 0;
        buckets = null;
        bucketsLength = 0;
        flowNodeArray = null;
        flowNodeLength = 0;
        symbolFields = default;
        localSymbolFields = default;
        containerFields = default;
        flowExtraFields = default;
        symbolExtras = default;
        flowData = default;
    }

    // ---- memory ----

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int PowerOfTwo(int minimum) => (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(minimum, 16));

    /// <summary>
    /// A region of <paramref name="bytes"/> bytes at the top of the range: while the binding runs, in the thread's
    /// binder slab (a range that is full moves on to the thread's next slab, its old regions stay held); afterwards in
    /// memory of the store's own.
    /// </summary>
    private byte* Region(long bytes)
    {
        bytes = SlabRange.Align(bytes);
        if (range.Capacity - top < bytes)
        {
            if (scratch)
            {
                // Step BO3: the regions in use stay where they are, their block stays alive until Commit.
                (overflowBlocks ??= []).Add(scratchBlock!);
                UseScratch(NewScratchBlock(Math.Max(range.Capacity * 2, bytes * 2)));
            }
            else
            {
                // The regions in use stay where they are: the range keeps the slab it leaves held.
                range.Grow(0, bytes, local, SlabKind.Binder);
                top = 0;
            }
        }
        var region = range.Start + top;
        top += bytes;
        return region;
    }

    private static Slab NewScratchBlock(long minimum)
    {
        var size = SlabRange.Align(Math.Max(minimum, ScratchBlockSize));
        return new Slab(size, native: size >= SlabKind.NativeSize);
    }

    // Step BO3: regions from now on in the block, which becomes the thread's scratch block.
    private void UseScratch(Slab block)
    {
        scratchBlock = block;
        scratch = true;
        range.Start = block.Memory;
        range.Capacity = block.Size;
        top = 0;
    }

    /// <summary>
    /// Step BO3: the end of a binding in the scratch block. The arrays in use move to one committed range of the
    /// thread's binder slab, each cut to the records in use (later additions grow them, as for any committed store); the
    /// node maps take the smallest size that keeps their load at most 1/2 (copied if they have it, rebuilt otherwise).
    /// Step BO4: the hash index is not kept (a lookup after the binding scans the table, <see cref="Scan"/>; adding an
    /// entry rebuilds it).
    /// </summary>
    private void CommitScratch(ThreadSlabs thread)
    {
        var symbolCount = symbolFields.CompactCapacity;
        var localSymbolCount = localSymbolFields.CompactCapacity;
        var containerCount = containerFields.CompactCapacity;
        var flowExtraCount = flowExtraFields.CompactCapacity;
        var symbolExtraCount = symbolExtras.CompactCapacity;
        var flowDataCount = flowData.CompactCapacity;
        var total =SlabRange.Align((long)symbolsUsed * sizeof(SymbolRecord)) + SlabRange.Align((long)tablesUsed * sizeof(TableRecord)) +
            SlabRange.Align((long)entriesUsed * sizeof(EntryRecord)) + SlabRange.Align((long)flowsUsed * sizeof(FlowRecord)) +
            SlabRange.Align((long)linksUsed * sizeof(Link)) + SlabRange.Align((long)flowNodeLength * sizeof(int)) +
            NodeMap<Symbol>.Bytes(symbolCount) + NodeMap<Symbol>.Bytes(localSymbolCount) +
            NodeMap<ContainerFields>.Bytes(containerCount) + NodeMap<FlowExtraFields>.Bytes(flowExtraCount) +
            NodeMap<SymbolExtras>.Bytes(symbolExtraCount) + NodeMap<FlowData>.Bytes(flowDataCount);
        scratch = false;
        range.Start = null;
        range.Capacity = 0;
        range.OpenCommitted(thread, SlabKind.Binder, total);
        top = 0;
        symbols = Compact(symbols, symbolsUsed);
        symbolsCapacity = symbolsUsed;
        tables = Compact(tables, tablesUsed);
        tablesCapacity = tablesUsed;
        entries = Compact(entries, entriesUsed);
        entriesCapacity = entriesUsed;
        flows = Compact(flows, flowsUsed);
        flowsCapacity = flowsUsed;
        links = Compact(links, linksUsed);
        linksCapacity = linksUsed;
        flowNodeArray = Compact(flowNodeArray, flowNodeLength);
        buckets = null;
        bucketsLength = 0;
        symbolFields.Rehash(this, symbolCount);
        localSymbolFields.Rehash(this, localSymbolCount);
        containerFields.Rehash(this, containerCount);
        flowExtraFields.Rehash(this, flowExtraCount);
        symbolExtras.Rehash(this, symbolExtraCount);
        flowData.Rehash(this, flowDataCount);
        var overflow = overflowBlocks;
        if (overflow is not null)
        {
            foreach (var block in overflow)
            {
                block.RemoveHolder();
            }
            overflow.Clear();
        }
    }

    // The first used records of the array, copied to a new region of exactly that size.
    private T* Compact<T>(T* array, int used)
        where T : unmanaged
    {
        var bytes = (long)used * sizeof(T);
        var compact = (T*)Region(bytes);
        Buffer.MemoryCopy(array, compact, bytes, bytes);
        return compact;
    }

    // A region for capacity records whose record 0 is zero (the undefined handle's).
    private T* NewArray<T>(int capacity)
        where T : unmanaged
    {
        var array = (T*)Region((long)capacity * sizeof(T));
        array[0] = default;
        return array;
    }

    private T* NewZeroed<T>(int length)
        where T : unmanaged
    {
        var array = (T*)Region((long)length * sizeof(T));
        new Span<T>(array, length).Clear();
        return array;
    }

    // The array in a region twice the size, with its used records copied.
    private T* GrowArray<T>(T* array, ref int capacity, int used)
        where T : unmanaged
    {
        var grown = (T*)Region((long)capacity * 2 * sizeof(T));
        Buffer.MemoryCopy(array, grown, (long)capacity * 2 * sizeof(T), (long)used * sizeof(T));
        capacity *= 2;
        return grown;
    }

    /// <summary>The node store of the bound file's parse (names are texts of its <see cref="TextTable"/>).</summary>
    internal NodeStore Nodes { get; }

    /// <summary>types.ts: SourceFile.bindDiagnostics (step BO4: the list is created when first read; most files have none).</summary>
    internal List<DiagnosticWithLocation> BindDiagnostics => Rare.BindDiagnostics ??= [];

    /// <summary>types.ts: SourceFile.bindSuggestionDiagnostics (undefined until the first one).</summary>
    internal List<DiagnosticWithLocation>? BindSuggestionDiagnostics
    {
        get => rare?.BindSuggestionDiagnostics;
        set => Rare.BindSuggestionDiagnostics = value;
    }

    /// <summary>types.ts: SourceFile.symbolCount.</summary>
    internal int SymbolCount { get; set; }

    /// <summary>
    /// The number of symbol records including record 0 (the symbols have indices 1..SymbolRecordCount-1; unlike
    /// <see cref="SymbolCount"/> it includes the symbols bound after the file's main pass, e.g. JSDoc typedefs). Read by
    /// the checker's per-symbol tables (module CO2).
    /// </summary>
    internal int SymbolRecordCount => symbolsUsed;

    /// <summary>types.ts: SourceFile.commonJsModuleIndicator (set by the binder; None when undefined).</summary>
    internal Node CommonJsModuleIndicator
    {
        get => rare is null ? Node.None : rare.CommonJsModuleIndicator;
        set => Rare.CommonJsModuleIndicator = value;
    }

    /// <summary>types.ts: SourceFile.jsGlobalAugmentations (None when undefined).</summary>
    internal SymbolTable JsGlobalAugmentations
    {
        get => rare is null ? SymbolTable.None : rare.JsGlobalAugmentations;
        set => Rare.JsGlobalAugmentations = value;
    }

    // ---- symbols ----

    internal Symbol CreateSymbol(SymbolFlags flags, TextRef name)
    {
        if (symbolsUsed == symbolsCapacity)
        {
            symbols = GrowArray(symbols, ref symbolsCapacity, symbolsUsed);
        }
        symbols[symbolsUsed] = new SymbolRecord { Flags = flags, Name = name };
        return new Symbol(symbolsUsed++);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SymbolFlags Flags(Symbol symbol) => symbols[symbol.Index].Flags;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetFlags(Symbol symbol, SymbolFlags value) => symbols[symbol.Index].Flags = value;

    /// <summary>types.ts: Symbol.escapedName.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal TextRef Name(Symbol symbol) => symbols[symbol.Index].Name;

    /// <summary>types.ts: Symbol.declarations (<see cref="NodeChain.IsUndefined"/> when undefined).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal NodeChain Declarations(Symbol symbol) => new(this, symbols[symbol.Index].FirstDeclaration);

    /// <summary>Appends a declaration (the caller checks uniqueness, as appendIfUnique does).</summary>
    internal void AddDeclaration(Symbol symbol, Node declaration)
    {
        ref var s = ref symbols[symbol.Index];
        Append(ref s.FirstDeclaration, ref s.LastDeclaration, declaration.Index);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node ValueDeclaration(Symbol symbol) => symbols[symbol.Index].ValueDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetValueDeclaration(Symbol symbol, Node value) => symbols[symbol.Index].ValueDeclaration = value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Symbol Parent(Symbol symbol) => symbols[symbol.Index].Parent;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetParent(Symbol symbol, Symbol value) => symbols[symbol.Index].Parent = value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Symbol ExportSymbol(Symbol symbol) => symbols[symbol.Index].ExportSymbol;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetExportSymbol(Symbol symbol, Symbol value) => symbols[symbol.Index].ExportSymbol = value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SymbolTable Members(Symbol symbol) => symbols[symbol.Index].Members;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetMembers(Symbol symbol, SymbolTable value) => symbols[symbol.Index].Members = value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SymbolTable Exports(Symbol symbol) => symbols[symbol.Index].Exports;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetExports(Symbol symbol, SymbolTable value) => symbols[symbol.Index].Exports = value;

    /// <summary>types.ts: Symbol.globalExports.</summary>
    internal SymbolTable GlobalExports(Symbol symbol) => rare?.GlobalExports is { } globalExports && globalExports.TryGetValue(symbol.Index, out var table) ? table : SymbolTable.None;

    internal void SetGlobalExports(Symbol symbol, SymbolTable value) => (Rare.GlobalExports ??= [])[symbol.Index] = value;

    /// <summary>types.ts: Symbol.assignmentDeclarationMembers, the declarations in insertion order.</summary>
    internal NodeChain AssignmentDeclarationMembers(Symbol symbol) =>
        new(this, rare?.AssignmentDeclarationMembers is { } members && members.TryGetValue(symbol.Index, out var chain) ? chain.First : 0);

    /// <summary>Adds a declaration to Symbol.assignmentDeclarationMembers (the caller skips one already there, as the Map keyed by node id does).</summary>
    internal void AddAssignmentDeclarationMember(Symbol symbol, Node declaration)
    {
        var assignmentDeclarationMembers = Rare.AssignmentDeclarationMembers ??= [];
        assignmentDeclarationMembers.TryGetValue(symbol.Index, out var chain);
        Append(ref chain.First, ref chain.Last, declaration.Index);
        assignmentDeclarationMembers[symbol.Index] = chain;
    }

    /// <summary>types.ts: Symbol.constEnumOnlyModule (null when undefined).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool? ConstEnumOnlyModule(Symbol symbol)
    {
        var bits = symbolExtras.Get(new Node(symbol.Index)).Bits;
        return (bits & SymbolBits.ConstEnumOnlyModuleSet) == 0 ? null : (bits & SymbolBits.ConstEnumOnlyModule) != 0;
    }

    internal void SetConstEnumOnlyModule(Symbol symbol, bool value)
    {
        ref var bits = ref symbolExtras.GetOrAdd(this, new Node(symbol.Index)).Bits;
        bits = (bits & ~SymbolBits.ConstEnumOnlyModule) | SymbolBits.ConstEnumOnlyModuleSet | (value ? SymbolBits.ConstEnumOnlyModule : SymbolBits.None);
    }

    /// <summary>types.ts: Symbol.isReplaceableByMethod (null when undefined).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool? IsReplaceableByMethod(Symbol symbol)
    {
        var bits = symbolExtras.Get(new Node(symbol.Index)).Bits;
        return (bits & SymbolBits.ReplaceableByMethodSet) == 0 ? null : (bits & SymbolBits.ReplaceableByMethod) != 0;
    }

    internal void SetIsReplaceableByMethod(Symbol symbol, bool value)
    {
        ref var bits = ref symbolExtras.GetOrAdd(this, new Node(symbol.Index)).Bits;
        bits = (bits & ~SymbolBits.ReplaceableByMethod) | SymbolBits.ReplaceableByMethodSet | (value ? SymbolBits.ReplaceableByMethod : SymbolBits.None);
    }

    // ---- symbol tables ----

    /// <summary>utilities.ts: createSymbolTable() (an empty table).</summary>
    internal SymbolTable CreateTable()
    {
        if (tablesUsed == tablesCapacity)
        {
            tables = GrowArray(tables, ref tablesCapacity, tablesUsed);
        }
        tables[tablesUsed] = default;
        return new SymbolTable(tablesUsed++);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int Count(SymbolTable table) => tables[table.Index].Count;

    /// <summary>The entries in insertion order.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal TableEntries Entries(SymbolTable table) => new(this, tables[table.Index].First);

    /// <summary>Map.get.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Symbol Get(SymbolTable table, TextRef name)
    {
        var e = Find(table, name, Hash(table, name));
        return e == 0 ? Symbol.None : entries[e].Symbol;
    }

    /// <summary>Map.has.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool Has(SymbolTable table, TextRef name) => Find(table, name, Hash(table, name)) != 0;

    /// <summary>Map.get, with the hash of the key for a following <see cref="AddNew"/> (step BO2).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Symbol Get(SymbolTable table, TextRef name, out int hash)
    {
        hash = Hash(table, name);
        var e = Find(table, name, hash);
        return e == 0 ? Symbol.None : entries[e].Symbol;
    }

    /// <summary>Map.set: replaces the value of an existing key in place (its position is kept), otherwise appends.</summary>
    internal void Set(SymbolTable table, TextRef name, Symbol symbol)
    {
        var hash = Hash(table, name);
        var found = Find(table, name, hash);
        if (found != 0)
        {
            entries[found].Symbol = symbol;
            return;
        }
        AddNew(table, name, symbol, hash);
    }

    /// <summary>
    /// Map.set for a key the table does not have (the caller has just looked it up, <see cref="Get(SymbolTable, TextRef, out int)"/>,
    /// which gave <paramref name="hash"/>): appends.
    /// </summary>
    internal void AddNew(SymbolTable table, TextRef name, Symbol symbol, int hash)
    {
        if (entriesUsed == entriesCapacity)
        {
            entries = GrowArray(entries, ref entriesCapacity, entriesUsed);
        }
        var entry = entriesUsed++;
        if (entriesUsed * 2 > bucketsLength)
        {
            GrowBuckets();
        }
        var bucket = hash & (bucketsLength - 1);
        entries[entry] = new EntryRecord { Name = name, Symbol = symbol, Table = table.Index, Hash = hash, NextInBucket = buckets[bucket] };
        buckets[bucket] = entry;
        ref var t = ref tables[table.Index];
        if (t.First == 0)
        {
            t.First = entry;
        }
        else
        {
            entries[t.Last].Next = entry;
        }
        t.Last = entry;
        t.Count++;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int Find(SymbolTable table, TextRef name, int hash)
    {
        if (bucketsLength == 0)
        {
            return Scan(table, name);
        }
        for (var e = buckets[hash & (bucketsLength - 1)]; e != 0; e = entries[e].NextInBucket)
        {
            ref var entry = ref entries[e];
            if (entry.Hash == hash && entry.Table == table.Index && Nodes.Texts.Equals(entry.Name, name))
            {
                return e;
            }
        }
        return 0;
    }

    // Step BO4: a lookup in a store committed without its hash index (lookups after the binding are rare; adding an
    // entry rebuilds the index): the table's entries in order.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int Scan(SymbolTable table, TextRef name)
    {
        for (var e = tables[table.Index].First; e != 0; e = entries[e].Next)
        {
            if (Nodes.Texts.Equals(entries[e].Name, name))
            {
                return e;
            }
        }
        return 0;
    }

    // FNV-1a over the name's characters, mixed with the table.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int Hash(SymbolTable table, TextRef name)
    {
        var h = 2166136261u ^ (uint)table.Index;
        foreach (var c in Nodes.Texts.Span(name))
        {
            h = (h ^ c) * 16777619u;
        }
        return (int)(h ^ (h >> 15));
    }

    // A full hash index (or, step BO4, none: a store committed from the scratch block does not keep it) moves to a new
    // one at load at most 1/2. (The entry being added, the last one, is linked by the caller.)
    private void GrowBuckets()
    {
        Index(Math.Max(bucketsLength * 2, PowerOfTwo(entriesUsed * 2)), entriesUsed - 1);
    }

    // A new hash index of the given size over the entries before <paramref name="end"/> (keys are unique, so the order
    // within a chain does not matter).
    private void Index(int length, int end)
    {
        bucketsLength = length;
        buckets = NewZeroed<int>(length);
        var mask = length - 1;
        for (var e = 1; e < end; e++)
        {
            ref var entry = ref entries[e];
            var bucket = entry.Hash & mask;
            entry.NextInBucket = buckets[bucket];
            buckets[bucket] = e;
        }
    }

    /// <summary>
    /// checker.ts getSymbolId: the symbol's id, assigned on first use from the process-wide counter (TypeScript's
    /// module-level nextSymbolId, which starts at 1). The binder needs it for the names of private identifiers.
    /// </summary>
    internal int SymbolId(Symbol symbol)
    {
        ref var s = ref symbolExtras.GetOrAdd(this, new Node(symbol.Index));
        if (s.Id == 0)
        {
            s.Id = Interlocked.Increment(ref nextSymbolId);
            (Rare.AssignedSymbolIds ??= []).Add(s.Id);
        }
        return s.Id;
    }

    private static int nextSymbolId;

    /// <summary>The symbol ids this file's binding assigned, in order (null if none).</summary>
    internal List<int>? AssignedSymbolIds => rare?.AssignedSymbolIds;

    // ---- flow nodes ----

    /// <summary>binder.ts: createFlowNode.</summary>
    internal FlowNode CreateFlowNode(FlowFlags flags, Node node, FlowNode antecedent)
    {
        if (flowsUsed == flowsCapacity)
        {
            flows = GrowArray(flows, ref flowsCapacity, flowsUsed);
        }
        // (A label is created with neither: its words are an empty antecedent list.)
        flows[flowsUsed] = new FlowRecord { Flags = flags, X = node.Index, Y = antecedent.Index };
        return new FlowNode(flowsUsed++);
    }

    /// <summary>binder.ts: createFlowSwitchClause's node, { switchStatement, clauseStart, clauseEnd }.</summary>
    internal FlowNode CreateSwitchClause(FlowNode antecedent, Node switchStatement, int clauseStart, int clauseEnd)
    {
        var flow = CreateFlowNode(FlowFlags.SwitchClause, switchStatement, antecedent);
        flowData.GetOrAdd(this, new Node(flow.Index)) = new FlowData { A = clauseStart, B = clauseEnd };
        return flow;
    }

    /// <summary>binder.ts: createReduceLabel; its antecedents are the label's antecedent list (shared, as the array is in TypeScript).</summary>
    internal FlowNode CreateReduceLabel(FlowNode target, FlowNode antecedentsOf, FlowNode antecedent)
    {
        var flow = CreateFlowNode(FlowFlags.ReduceLabel, Node.None, antecedent);
        flowData.GetOrAdd(this, new Node(flow.Index)) = new FlowData { A = target.Index, B = flows[antecedentsOf.Index].X };
        return flow;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowFlags Flags(FlowNode flow) => flows[flow.Index].Flags;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetFlags(FlowNode flow, FlowFlags value) => flows[flow.Index].Flags = value;

    /// <summary>FlowNode.node where it is a node (for a switch clause: the switch statement).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node NodeOf(FlowNode flow)
    {
        ref var f = ref flows[flow.Index];
        return (f.Flags & FlowFlags.Label) != 0 ? Node.None : new Node(f.X);
    }

    /// <summary>Sets FlowNode.node of a flow node that is not a label (a start node's).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetNodeOf(FlowNode flow, Node value)
    {
        Debug.Assert((flows[flow.Index].Flags & FlowFlags.Label) == 0, "a label has no node");
        flows[flow.Index].X = value.Index;
    }

    /// <summary>FlowNode.antecedent of a node that is not a label (None for a label).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowNode Antecedent(FlowNode flow)
    {
        ref var f = ref flows[flow.Index];
        return (f.Flags & FlowFlags.Label) != 0 ? FlowNode.None : new FlowNode(f.Y);
    }

    /// <summary>FlowLabel.antecedent (<see cref="FlowChain.IsUndefined"/> when undefined).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowChain Antecedents(FlowNode label) => new(this, flows[label.Index].X);

    /// <summary>Appends to a label's antecedents (binder.ts addAntecedent checks reachability and uniqueness).</summary>
    internal void AddAntecedent(FlowNode label, FlowNode antecedent)
    {
        ref var f = ref flows[label.Index];
        Append(ref f.X, ref f.Y, antecedent.Index);
    }

    /// <summary>
    /// binder.ts bindTryStatement: `finallyLabel.antecedent = concatenate(concatenate(a, b), c)` (the antecedents of
    /// the three labels appended in order; undefined if all three are). TypeScript may share one of the arrays instead
    /// of copying it; none of them is changed afterwards, so a copy reads the same.
    /// </summary>
    internal void ConcatenateAntecedents(FlowNode target, FlowNode a, FlowNode b, FlowNode c)
    {
        foreach (var label in (ReadOnlySpan<FlowNode>)[a, b, c])
        {
            for (var l = flows[label.Index].X; l != 0; l = links[l].Next)
            {
                var value = links[l].Value;
                Append(ref flows[target.Index].X, ref flows[target.Index].Y, value);
            }
        }
    }

    /// <summary>Whether the label's antecedent array contains the flow node (core.ts contains).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool HasAntecedent(FlowNode label, FlowNode antecedent)
    {
        for (var l = flows[label.Index].X; l != 0; l = links[l].Next)
        {
            if (links[l].Value == antecedent.Index)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The label's single antecedent if it has exactly one, None otherwise; <paramref name="count"/>: 0, 1 or 2 (two or more).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowNode SingleAntecedent(FlowNode label, out int count)
    {
        var first = flows[label.Index].X;
        if (first == 0)
        {
            count = 0;
            return FlowNode.None;
        }
        count = links[first].Next == 0 ? 1 : 2;
        return count == 1 ? new FlowNode(links[first].Value) : FlowNode.None;
    }

    /// <summary>Whether the label's antecedent array is defined.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool HasAntecedents(FlowNode label) => flows[label.Index].X != 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int ClauseStart(FlowNode switchClause) => flowData.Get(new Node(switchClause.Index)).A;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int ClauseEnd(FlowNode switchClause) => flowData.Get(new Node(switchClause.Index)).B;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowNode ReduceTarget(FlowNode reduceLabel) => new(flowData.Get(new Node(reduceLabel.Index)).A);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowChain ReduceAntecedents(FlowNode reduceLabel) => new(this, flowData.Get(new Node(reduceLabel.Index)).B);

    // ---- per-node fields ----

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Symbol SymbolOf(Node node) => symbolFields.Get(node);

    internal void SetSymbolOf(Node node, Symbol value) => symbolFields.GetOrAdd(this, node) = value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Symbol LocalSymbolOf(Node node) => localSymbolFields.Get(node);

    internal void SetLocalSymbolOf(Node node, Symbol value) => localSymbolFields.GetOrAdd(this, node) = value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowNode FlowNodeOf(Node node)
    {
        var i = FlowNodeSlot(node);
        return (uint)i < (uint)flowNodeLength ? new FlowNode(flowNodeArray[i]) : FlowNode.None;
    }

    internal void SetFlowNodeOf(Node node, FlowNode value)
    {
        var i = FlowNodeSlot(node);
        if ((uint)i >= (uint)flowNodeLength)
        {
            GrowFlowNodes(i);
        }
        flowNodeArray[i] = value.Index;
    }

    // Step BO4: the slot of a node's flowNode, handle / 5 (records are at least 5 ints long, so the slots of two records
    // differ; one slot per 5 record words instead of 4).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int FlowNodeSlot(Node node) => (int)((uint)node.Index / 5);

    // (A node created after the dense array was sized.)
    private void GrowFlowNodes(int index)
    {
        var length = Math.Max(index + 1, flowNodeLength * 2);
        var grown = (int*)Region((long)length * sizeof(int));
        new Span<int>(flowNodeArray, flowNodeLength).CopyTo(new Span<int>(grown, length));
        new Span<int>(grown + flowNodeLength, length - flowNodeLength).Clear();
        flowNodeArray = grown;
        flowNodeLength = length;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SymbolTable LocalsOf(Node node) => containerFields.Get(node).Locals;

    internal void SetLocalsOf(Node node, SymbolTable value) => containerFields.GetOrAdd(this, node).Locals = value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node NextContainerOf(Node node) => containerFields.Get(node).NextContainer;

    internal void SetNextContainerOf(Node node, Node value) => containerFields.GetOrAdd(this, node).NextContainer = value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowNode EndFlowNodeOf(Node node) => containerFields.Get(node).EndFlowNode;

    internal void SetEndFlowNodeOf(Node node, FlowNode value) => containerFields.GetOrAdd(this, node).EndFlowNode = value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowNode ReturnFlowNodeOf(Node node) => containerFields.Get(node).ReturnFlowNode;

    internal void SetReturnFlowNodeOf(Node node, FlowNode value) => containerFields.GetOrAdd(this, node).ReturnFlowNode = value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowNode FallthroughFlowNodeOf(Node node) => containerFields.Get(node).FallthroughFlowNode;

    internal void SetFallthroughFlowNodeOf(Node node, FlowNode value) => containerFields.GetOrAdd(this, node).FallthroughFlowNode = value;

    /// <summary>types.ts: ConditionalExpression.flowNodeWhenTrue.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowNode FlowNodeWhenTrueOf(Node node) => flowExtraFields.Get(node).WhenTrue;

    internal void SetFlowNodeWhenTrueOf(Node node, FlowNode value) => flowExtraFields.GetOrAdd(this, node).WhenTrue = value;

    /// <summary>types.ts: ConditionalExpression.flowNodeWhenFalse.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowNode FlowNodeWhenFalseOf(Node node) => flowExtraFields.Get(node).WhenFalse;

    internal void SetFlowNodeWhenFalseOf(Node node, FlowNode value) => flowExtraFields.GetOrAdd(this, node).WhenFalse = value;

    /// <summary>types.ts: SwitchStatement.possiblyExhaustive (false, as createSwitchStatement initializes it, until the binder sets it).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool PossiblyExhaustiveOf(Node node) => flowExtraFields.Get(node).PossiblyExhaustive;

    internal void SetPossiblyExhaustiveOf(Node node, bool value)
    {
        if (value || flowExtraFields.Get(node).PossiblyExhaustive)
        {
            flowExtraFields.GetOrAdd(this, node).PossiblyExhaustive = value;
        }
    }

    // ---- chains ----

    private void Append(ref int first, ref int last, int value)
    {
        if (linksUsed == linksCapacity)
        {
            links = GrowArray(links, ref linksCapacity, linksUsed);
        }
        var link = linksUsed++;
        links[link] = new Link { Value = value };
        if (first == 0)
        {
            first = link;
        }
        else
        {
            links[last].Next = link;
        }
        last = link;
    }

    /// <summary>A chain of nodes in insertion order (a declarations array).</summary>
    internal readonly struct NodeChain
    {
        private readonly BinderStore store;
        private readonly int first;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        internal NodeChain(BinderStore store, int first)
        {
            this.store = store;
            this.first = first;
        }

        /// <summary>Whether the array is undefined (TypeScript never creates these arrays empty).</summary>
        public bool IsUndefined
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            get => first == 0;
        }

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Enumerator GetEnumerator() => new(store, first);

        internal struct Enumerator
        {
            private readonly BinderStore store;
            private int next;
            private int current;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal Enumerator(BinderStore store, int first)
            {
                this.store = store;
                next = first;
            }

            public readonly Node Current
            {
                [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
                get => new(store.links[current].Value);
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool MoveNext()
            {
                if (next == 0)
                {
                    return false;
                }
                current = next;
                next = store.links[next].Next;
                return true;
            }
        }
    }

    /// <summary>A chain of flow nodes in insertion order (a label's antecedent array).</summary>
    internal readonly struct FlowChain
    {
        private readonly BinderStore store;
        private readonly int first;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        internal FlowChain(BinderStore store, int first)
        {
            this.store = store;
            this.first = first;
        }

        /// <summary>Whether the array is undefined (TypeScript never creates it empty).</summary>
        public bool IsUndefined
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            get => first == 0;
        }

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Enumerator GetEnumerator() => new(store, first);

        internal struct Enumerator
        {
            private readonly BinderStore store;
            private int next;
            private int current;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal Enumerator(BinderStore store, int first)
            {
                this.store = store;
                next = first;
            }

            public readonly FlowNode Current
            {
                [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
                get => new(store.links[current].Value);
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool MoveNext()
            {
                if (next == 0)
                {
                    return false;
                }
                current = next;
                next = store.links[next].Next;
                return true;
            }
        }
    }

    /// <summary>The entries of a symbol table in insertion order.</summary>
    internal readonly struct TableEntries
    {
        private readonly BinderStore store;
        private readonly int first;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        internal TableEntries(BinderStore store, int first)
        {
            this.store = store;
            this.first = first;
        }

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Enumerator GetEnumerator() => new(store, first);

        internal struct Enumerator
        {
            private readonly BinderStore store;
            private int next;
            private int current;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal Enumerator(BinderStore store, int first)
            {
                this.store = store;
                next = first;
            }

            public readonly (TextRef Name, Symbol Symbol) Current
            {
                [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
                get => (store.entries[current].Name, store.entries[current].Symbol);
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool MoveNext()
            {
                if (next == 0)
                {
                    return false;
                }
                current = next;
                next = store.entries[next].Next;
                return true;
            }
        }
    }

    /// <summary>
    /// An open-addressing map from node handle to the binder's fields of the node (linear probing, multiplicative hash,
    /// load at most 1/2), keys and values in regions of the store's binder range (step BO1); no table until
    /// <see cref="Init"/> or the first entry. Node.None (0) marks a free slot; a new entry's value starts as default.
    /// Step BO4: keys and values in one region (the values after the keys), the hash shift taken from the capacity, so
    /// that the map is 16 bytes of the store object.
    /// </summary>
    private struct NodeMap<T>
        where T : unmanaged
    {
        private int* keys;
        private int capacity; // a power of two; 0: no table
        private int count;

        /// <summary>The capacity that holds <paramref name="entries"/> entries at load 1/2.</summary>
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        internal static int CapacityFor(int entries) => PowerOfTwo(entries * 2);

        // The values, after the keys.
        private readonly T* Values
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            get => (T*)(keys + capacity);
        }

        /// <summary>The table, empty, with <paramref name="size"/> slots (a power of two).</summary>
        internal void Init(BinderStore store, int size)
        {
            keys = (int*)store.Region((long)size * (sizeof(int) + sizeof(T)));
            new Span<int>(keys, size).Clear();
            capacity = size;
            count = 0;
        }

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public readonly T Get(Node node)
        {
            if (capacity == 0)
            {
                return default;
            }
            var mask = capacity - 1;
            for (var i = Slot(node.Index, capacity); ; i = (i + 1) & mask)
            {
                var key = keys[i];
                if (key == node.Index)
                {
                    return Values[i];
                }
                if (key == 0)
                {
                    return default;
                }
            }
        }

        public ref T GetOrAdd(BinderStore store, Node node)
        {
            if ((count + 1) * 2 > capacity)
            {
                Grow(store);
            }
            var mask = capacity - 1;
            for (var i = Slot(node.Index, capacity); ; i = (i + 1) & mask)
            {
                var key = keys[i];
                if (key == node.Index)
                {
                    return ref Values[i];
                }
                if (key == 0)
                {
                    keys[i] = node.Index;
                    count++;
                    var values = Values;
                    values[i] = default;
                    return ref values[i];
                }
            }
        }

        // Multiplicative hash: the top log2(capacity) bits of the product.
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        private static int Slot(int key, int capacity) => (int)(((uint)key * 0x9E3779B9u) >> (System.Numerics.BitOperations.LeadingZeroCount((uint)capacity) + 1));

        /// <summary>Step BO3: the smallest capacity that holds the entries at load 1/2 (0: no table, if there are none).</summary>
        internal readonly int CompactCapacity
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            get => count == 0 ? 0 : CapacityFor(count);
        }

        /// <summary>Step BO3: the bytes of the regions of a table with <paramref name="size"/> slots.</summary>
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        internal static long Bytes(int size) => size == 0 ? 0 : SlabRange.Align((long)size * (sizeof(int) + sizeof(T)));

        /// <summary>Step BO3: the entries moved to a new table with <paramref name="size"/> slots (0: no table; only when there are no entries).</summary>
        internal void Rehash(BinderStore store, int size)
        {
            if (size == 0)
            {
                this = default;
                return;
            }
            if (size == capacity)
            {
                // The same size: the table copied as it is.
                keys = (int*)store.Compact((byte*)keys, size * (sizeof(int) + sizeof(T)));
                return;
            }
            Move(store, size);
        }

        private void Grow(BinderStore store) => Move(store, capacity == 0 ? 16 : capacity * 2);

        private void Move(BinderStore store, int size)
        {
            var oldKeys = keys;
            var oldValues = Values;
            var oldCapacity = capacity;
            Init(store, size);
            var values = Values;
            var mask = capacity - 1;
            for (var j = 0; j < oldCapacity; j++)
            {
                if (oldKeys[j] == 0)
                {
                    continue;
                }
                var i = Slot(oldKeys[j], capacity);
                while (keys[i] != 0)
                {
                    i = (i + 1) & mask;
                }
                keys[i] = oldKeys[j];
                values[i] = oldValues[j];
                count++;
            }
        }
    }
}
