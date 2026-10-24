// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's storage of binder output (module B0, docs/phase2-binder-layout.md).

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
/// localSymbol, locals, nextContainer, flowNode, endFlowNode, returnFlowNode, fallthroughFlowNode) live in three
/// open-addressing maps keyed by node handle, since node handles are record offsets and the node records have no room
/// for them. Everything is read and written through this class, so the layout can change without touching the binder.
/// </para>
/// </summary>
internal sealed class BinderStore
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

    private struct FlowRecord
    {
        public FlowFlags Flags;
        public Node Node;               // the node it refers to; for a switch clause the switch statement
        public FlowNode Antecedent;     // all but labels
        public int FirstAntecedent;     // labels: link chain of antecedents; 0: undefined
        public int LastAntecedent;
        public int A;                   // switch clause: clauseStart; reduce label: target
        public int B;                   // switch clause: clauseEnd; reduce label: first link of its antecedents
    }

    /// <summary>The per-node fields of declarations.</summary>
    private struct DeclarationFields
    {
        public Symbol Symbol;
        public Symbol LocalSymbol;
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

    private SymbolRecord[] symbols = new SymbolRecord[16];
    private int symbolsUsed = 1; // record 0 is Symbol.None
    private TableRecord[] tables = new TableRecord[16];
    private int tablesUsed = 1;
    private EntryRecord[] entries = new EntryRecord[16];
    private int entriesUsed = 1;
    // Module B1: a hash index over the entries of all tables (chains of entries by hash of table and name), so
    // Map.get and Map.set do not scan a table; grown ×2 at load 1/2.
    private int[] buckets = new int[32];
    private FlowRecord[] flows = new FlowRecord[16];
    private int flowsUsed = 1;
    private Link[] links = new Link[16];
    private int linksUsed = 1;

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

    private NodeMap<DeclarationFields> declarationFields;
    private NodeMap<FlowNode> flowNodes;
    private NodeMap<ContainerFields> containerFields;
    private NodeMap<FlowExtraFields> flowExtraFields;

    // Rare symbol members (UMD global exports, JavaScript assignment declarations): only files that have them pay.
    private Dictionary<int, SymbolTable>? globalExports;
    private Dictionary<int, (int First, int Last)>? assignmentDeclarationMembers;

    internal BinderStore(NodeStore nodes)
    {
        Nodes = nodes;
    }

    /// <summary>The node store of the bound file's parse (names are texts of its <see cref="TextTable"/>).</summary>
    internal NodeStore Nodes { get; }

    /// <summary>types.ts: SourceFile.bindDiagnostics.</summary>
    internal List<DiagnosticWithLocation> BindDiagnostics { get; } = [];

    /// <summary>types.ts: SourceFile.bindSuggestionDiagnostics (undefined until the first one).</summary>
    internal List<DiagnosticWithLocation>? BindSuggestionDiagnostics { get; set; }

    /// <summary>types.ts: SourceFile.symbolCount.</summary>
    internal int SymbolCount { get; set; }

    /// <summary>types.ts: SourceFile.commonJsModuleIndicator (set by the binder; None when undefined).</summary>
    internal Node CommonJsModuleIndicator { get; set; }

    /// <summary>types.ts: SourceFile.jsGlobalAugmentations (None when undefined).</summary>
    internal SymbolTable JsGlobalAugmentations { get; set; }

    // ---- symbols ----

    internal Symbol CreateSymbol(SymbolFlags flags, TextRef name)
    {
        if (symbolsUsed == symbols.Length)
        {
            Array.Resize(ref symbols, symbols.Length * 2);
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
    internal SymbolTable GlobalExports(Symbol symbol) => globalExports is not null && globalExports.TryGetValue(symbol.Index, out var table) ? table : SymbolTable.None;

    internal void SetGlobalExports(Symbol symbol, SymbolTable value) => (globalExports ??= [])[symbol.Index] = value;

    /// <summary>types.ts: Symbol.assignmentDeclarationMembers, the declarations in insertion order.</summary>
    internal NodeChain AssignmentDeclarationMembers(Symbol symbol) =>
        new(this, assignmentDeclarationMembers is not null && assignmentDeclarationMembers.TryGetValue(symbol.Index, out var chain) ? chain.First : 0);

    /// <summary>Adds a declaration to Symbol.assignmentDeclarationMembers (the caller skips one already there, as the Map keyed by node id does).</summary>
    internal void AddAssignmentDeclarationMember(Symbol symbol, Node declaration)
    {
        assignmentDeclarationMembers ??= [];
        assignmentDeclarationMembers.TryGetValue(symbol.Index, out var chain);
        Append(ref chain.First, ref chain.Last, declaration.Index);
        assignmentDeclarationMembers[symbol.Index] = chain;
    }

    /// <summary>types.ts: Symbol.constEnumOnlyModule (null when undefined).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool? ConstEnumOnlyModule(Symbol symbol)
    {
        var bits = symbols[symbol.Index].Bits;
        return (bits & SymbolBits.ConstEnumOnlyModuleSet) == 0 ? null : (bits & SymbolBits.ConstEnumOnlyModule) != 0;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetConstEnumOnlyModule(Symbol symbol, bool value)
    {
        ref var bits = ref symbols[symbol.Index].Bits;
        bits = (bits & ~SymbolBits.ConstEnumOnlyModule) | SymbolBits.ConstEnumOnlyModuleSet | (value ? SymbolBits.ConstEnumOnlyModule : SymbolBits.None);
    }

    /// <summary>types.ts: Symbol.isReplaceableByMethod (null when undefined).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool? IsReplaceableByMethod(Symbol symbol)
    {
        var bits = symbols[symbol.Index].Bits;
        return (bits & SymbolBits.ReplaceableByMethodSet) == 0 ? null : (bits & SymbolBits.ReplaceableByMethod) != 0;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetIsReplaceableByMethod(Symbol symbol, bool value)
    {
        ref var bits = ref symbols[symbol.Index].Bits;
        bits = (bits & ~SymbolBits.ReplaceableByMethod) | SymbolBits.ReplaceableByMethodSet | (value ? SymbolBits.ReplaceableByMethod : SymbolBits.None);
    }

    // ---- symbol tables ----

    /// <summary>utilities.ts: createSymbolTable() (an empty table).</summary>
    internal SymbolTable CreateTable()
    {
        if (tablesUsed == tables.Length)
        {
            Array.Resize(ref tables, tables.Length * 2);
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
        if (entriesUsed == entries.Length)
        {
            Array.Resize(ref entries, entries.Length * 2);
        }
        var entry = entriesUsed++;
        if (entriesUsed * 2 > buckets.Length)
        {
            GrowBuckets();
        }
        var bucket = hash & (buckets.Length - 1);
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
        for (var e = buckets[hash & (buckets.Length - 1)]; e != 0; e = entries[e].NextInBucket)
        {
            ref var entry = ref entries[e];
            if (entry.Hash == hash && entry.Table == table.Index && Nodes.Texts.Equals(entry.Name, name))
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

    private void GrowBuckets()
    {
        buckets = new int[buckets.Length * 2];
        var mask = buckets.Length - 1;
        for (var e = 1; e < entriesUsed - 1; e++)
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
        ref var s = ref symbols[symbol.Index];
        if (s.Id == 0)
        {
            s.Id = Interlocked.Increment(ref nextSymbolId);
            (AssignedSymbolIds ??= []).Add(s.Id);
        }
        return s.Id;
    }

    private static int nextSymbolId;

    /// <summary>The symbol ids this file's binding assigned, in order (null if none).</summary>
    internal List<int>? AssignedSymbolIds { get; private set; }

    // ---- flow nodes ----

    /// <summary>binder.ts: createFlowNode.</summary>
    internal FlowNode CreateFlowNode(FlowFlags flags, Node node, FlowNode antecedent)
    {
        if (flowsUsed == flows.Length)
        {
            Array.Resize(ref flows, flows.Length * 2);
        }
        flows[flowsUsed] = new FlowRecord { Flags = flags, Node = node, Antecedent = antecedent };
        return new FlowNode(flowsUsed++);
    }

    /// <summary>binder.ts: createFlowSwitchClause's node, { switchStatement, clauseStart, clauseEnd }.</summary>
    internal FlowNode CreateSwitchClause(FlowNode antecedent, Node switchStatement, int clauseStart, int clauseEnd)
    {
        var flow = CreateFlowNode(FlowFlags.SwitchClause, switchStatement, antecedent);
        flows[flow.Index].A = clauseStart;
        flows[flow.Index].B = clauseEnd;
        return flow;
    }

    /// <summary>binder.ts: createReduceLabel; its antecedents are the label's antecedent list (shared, as the array is in TypeScript).</summary>
    internal FlowNode CreateReduceLabel(FlowNode target, FlowNode antecedentsOf, FlowNode antecedent)
    {
        var flow = CreateFlowNode(FlowFlags.ReduceLabel, Node.None, antecedent);
        flows[flow.Index].A = target.Index;
        flows[flow.Index].B = flows[antecedentsOf.Index].FirstAntecedent;
        return flow;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowFlags Flags(FlowNode flow) => flows[flow.Index].Flags;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetFlags(FlowNode flow, FlowFlags value) => flows[flow.Index].Flags = value;

    /// <summary>FlowNode.node where it is a node (for a switch clause: the switch statement).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node NodeOf(FlowNode flow) => flows[flow.Index].Node;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetNodeOf(FlowNode flow, Node value) => flows[flow.Index].Node = value;

    /// <summary>FlowNode.antecedent of a node that is not a label.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowNode Antecedent(FlowNode flow) => flows[flow.Index].Antecedent;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetAntecedent(FlowNode flow, FlowNode value) => flows[flow.Index].Antecedent = value;

    /// <summary>FlowLabel.antecedent (<see cref="FlowChain.IsUndefined"/> when undefined).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowChain Antecedents(FlowNode label) => new(this, flows[label.Index].FirstAntecedent);

    /// <summary>Appends to a label's antecedents (binder.ts addAntecedent checks reachability and uniqueness).</summary>
    internal void AddAntecedent(FlowNode label, FlowNode antecedent)
    {
        ref var f = ref flows[label.Index];
        Append(ref f.FirstAntecedent, ref f.LastAntecedent, antecedent.Index);
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
            for (var l = flows[label.Index].FirstAntecedent; l != 0; l = links[l].Next)
            {
                var value = links[l].Value;
                Append(ref flows[target.Index].FirstAntecedent, ref flows[target.Index].LastAntecedent, value);
            }
        }
    }

    /// <summary>Whether the label's antecedent array contains the flow node (core.ts contains).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool HasAntecedent(FlowNode label, FlowNode antecedent)
    {
        for (var l = flows[label.Index].FirstAntecedent; l != 0; l = links[l].Next)
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
        var first = flows[label.Index].FirstAntecedent;
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
    internal bool HasAntecedents(FlowNode label) => flows[label.Index].FirstAntecedent != 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int ClauseStart(FlowNode switchClause) => flows[switchClause.Index].A;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int ClauseEnd(FlowNode switchClause) => flows[switchClause.Index].B;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowNode ReduceTarget(FlowNode reduceLabel) => new(flows[reduceLabel.Index].A);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowChain ReduceAntecedents(FlowNode reduceLabel) => new(this, flows[reduceLabel.Index].B);

    // ---- per-node fields ----

    internal Symbol SymbolOf(Node node) => declarationFields.Get(node).Symbol;

    internal void SetSymbolOf(Node node, Symbol value) => declarationFields.GetOrAdd(node).Symbol = value;

    internal Symbol LocalSymbolOf(Node node) => declarationFields.Get(node).LocalSymbol;

    internal void SetLocalSymbolOf(Node node, Symbol value) => declarationFields.GetOrAdd(node).LocalSymbol = value;

    internal FlowNode FlowNodeOf(Node node) => flowNodes.Get(node);

    internal void SetFlowNodeOf(Node node, FlowNode value) => flowNodes.GetOrAdd(node) = value;

    internal SymbolTable LocalsOf(Node node) => containerFields.Get(node).Locals;

    internal void SetLocalsOf(Node node, SymbolTable value) => containerFields.GetOrAdd(node).Locals = value;

    internal Node NextContainerOf(Node node) => containerFields.Get(node).NextContainer;

    internal void SetNextContainerOf(Node node, Node value) => containerFields.GetOrAdd(node).NextContainer = value;

    internal FlowNode EndFlowNodeOf(Node node) => containerFields.Get(node).EndFlowNode;

    internal void SetEndFlowNodeOf(Node node, FlowNode value) => containerFields.GetOrAdd(node).EndFlowNode = value;

    internal FlowNode ReturnFlowNodeOf(Node node) => containerFields.Get(node).ReturnFlowNode;

    internal void SetReturnFlowNodeOf(Node node, FlowNode value) => containerFields.GetOrAdd(node).ReturnFlowNode = value;

    internal FlowNode FallthroughFlowNodeOf(Node node) => containerFields.Get(node).FallthroughFlowNode;

    internal void SetFallthroughFlowNodeOf(Node node, FlowNode value) => containerFields.GetOrAdd(node).FallthroughFlowNode = value;

    /// <summary>types.ts: ConditionalExpression.flowNodeWhenTrue.</summary>
    internal FlowNode FlowNodeWhenTrueOf(Node node) => flowExtraFields.Get(node).WhenTrue;

    internal void SetFlowNodeWhenTrueOf(Node node, FlowNode value) => flowExtraFields.GetOrAdd(node).WhenTrue = value;

    /// <summary>types.ts: ConditionalExpression.flowNodeWhenFalse.</summary>
    internal FlowNode FlowNodeWhenFalseOf(Node node) => flowExtraFields.Get(node).WhenFalse;

    internal void SetFlowNodeWhenFalseOf(Node node, FlowNode value) => flowExtraFields.GetOrAdd(node).WhenFalse = value;

    /// <summary>types.ts: SwitchStatement.possiblyExhaustive (false, as createSwitchStatement initializes it, until the binder sets it).</summary>
    internal bool PossiblyExhaustiveOf(Node node) => flowExtraFields.Get(node).PossiblyExhaustive;

    internal void SetPossiblyExhaustiveOf(Node node, bool value)
    {
        if (value || flowExtraFields.Get(node).PossiblyExhaustive)
        {
            flowExtraFields.GetOrAdd(node).PossiblyExhaustive = value;
        }
    }

    // ---- chains ----

    private void Append(ref int first, ref int last, int value)
    {
        if (linksUsed == links.Length)
        {
            Array.Resize(ref links, links.Length * 2);
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
    /// load at most 1/2); no table until the first entry. Node.None (0) marks a free slot.
    /// </summary>
    private struct NodeMap<T>
        where T : struct
    {
        private int[]? keys;
        private T[]? values;
        private int count;
        private int shift;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public readonly T Get(Node node)
        {
            if (keys is null)
            {
                return default;
            }
            var mask = keys.Length - 1;
            for (var i = Slot(node.Index, shift); ; i = (i + 1) & mask)
            {
                var key = keys[i];
                if (key == node.Index)
                {
                    return values![i];
                }
                if (key == 0)
                {
                    return default;
                }
            }
        }

        public ref T GetOrAdd(Node node)
        {
            if (keys is null || (count + 1) * 2 > keys.Length)
            {
                Grow();
            }
            var mask = keys!.Length - 1;
            for (var i = Slot(node.Index, shift); ; i = (i + 1) & mask)
            {
                var key = keys[i];
                if (key == node.Index)
                {
                    return ref values![i];
                }
                if (key == 0)
                {
                    keys[i] = node.Index;
                    count++;
                    return ref values![i];
                }
            }
        }

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        private static int Slot(int key, int shift) => (int)(((uint)key * 0x9E3779B9u) >> shift);

        private void Grow()
        {
            var oldKeys = keys;
            var oldValues = values;
            var capacity = oldKeys is null ? 64 : oldKeys.Length * 2;
            keys = new int[capacity];
            values = new T[capacity];
            shift = 32 - System.Numerics.BitOperations.Log2((uint)capacity);
            if (oldKeys is null)
            {
                return;
            }
            var mask = capacity - 1;
            for (var j = 0; j < oldKeys.Length; j++)
            {
                if (oldKeys[j] == 0)
                {
                    continue;
                }
                var i = Slot(oldKeys[j], shift);
                while (keys[i] != 0)
                {
                    i = (i + 1) & mask;
                }
                keys[i] = oldKeys[j];
                values[i] = oldValues![j];
            }
        }
    }
}
