# Phase 2: Track O binder output layout (module B0)

Decided in module B0 ([modules/B0.md](modules/B0.md), [phase2-plan.md](phase2-plan.md)). Code: `src/NeverSharp.Optimized/Binder/` (`Handles.cs`, `BinderStore.cs`, `Binder.cs`). B0 implements only the storage; modules B1–B3 port the binder on top of it and may refine the details below. Later optimization steps change the layout only after profiling, as in Phase 1.

## Decision

| Binder output | TypeScript 6.0.3 | typescript-go | Track O |
|---|---|---|---|
| Symbol | object (`Symbol` constructor) | `*ast.Symbol` from the binder's `symbolArena` | `Symbol` handle (int, 0 = undefined): a record in `BinderStore.symbols` (flags, name as a `TextRef`, declaration chain, value declaration, parent, export symbol, members, exports, defined bits for `constEnumOnlyModule` / `isReplaceableByMethod`) |
| `declarations` | array | slice | link chain (first, last) in the store's link array; undefined = no chain (TypeScript never creates it empty) |
| SymbolTable (`members`, `exports`, `locals`, `globalExports`) | `Map` (insertion order) | Go `map` (no order) | `SymbolTable` handle: entry chain in insertion order (Map order, needed for the dump and for checker iteration order); undefined (`None`) and empty are distinct. Lookup: scan in B0; since B1 one hash index per file over the entries of all its tables (key: table and name characters, chained buckets, grown ×2 at load 1/2) |
| FlowNode | object `{ flags, node, antecedent }` | `*ast.FlowNode` from `flowNodeArena`, lists from `flowListArena` | `FlowNode` handle: a record in `BinderStore.flows` (flags, node, antecedent, label antecedent chain, two data ints: switch clause start/end or reduce label target/antecedent chain) |
| Label antecedents | array, appended | `FlowList`, prepended | link chain, appended (TypeScript's order; the dump follows TypeScript) |
| `symbol`, `localSymbol` | node properties | `DeclarationBase.Symbol`, `ExportableBase.LocalSymbol` in the node struct | open-addressing map node handle → (symbol, localSymbol) |
| `flowNode` | node property | `FlowNodeBase.FlowNode` | open-addressing map node handle → flow node |
| `locals`, `nextContainer`, `endFlowNode`, `returnFlowNode`, `fallthroughFlowNode` | node properties | `LocalsContainerBase`, `BodyBase`, `CaseOrDefaultClause` | open-addressing map node handle → (locals, nextContainer, endFlowNode, returnFlowNode, fallthroughFlowNode) |
| NodeFlags set by the binder | `node.flags \|=` | `node.Flags \|=` | written into the parse's node records (`NodeStore.SetFlags`), like both references |
| `bindDiagnostics`, `bindSuggestionDiagnostics`, `symbolCount` | SourceFile properties | SourceFile fields | `BinderStore` properties (`List<DiagnosticWithLocation>`, as the parser's diagnostics) |
| Symbol ids (`getSymbolId`, used by the binder only for private names `__#<id>@#name`) | lazily from the process-wide `nextSymbolId` | — | lazily from a process-wide counter (Interlocked), kept in the symbol record; the binder dump renumbers them in manifest order, as the oracle's sequential run assigns them |
| Rare members (`globalExports`, `assignmentDeclarationMembers`) | Symbol properties | (moved elsewhere in TS 7) | small dictionaries in the store, allocated only by files that have them (UMD, JavaScript) |

## Why

- **Handles and records, not objects.** Track O's nodes are int handles into a per-parse store (pre-registration §5.4); symbols and flow nodes follow the same pattern, which is also typescript-go's: its binder allocates symbols, flow nodes and flow lists from per-binder arenas. Records in arrays mean one allocation per array growth instead of one per symbol or flow node, and no references for the collector to trace. All access goes through `BinderStore`, so the layout can change (per-thread slabs, struct-of-arrays) without touching the binder.
- **Per-node fields in maps, not in the node records.** A `Node` handle is a record offset in the parse's record slab; records have no spare words, and adding binder words to every record kind that can carry them would change the parse-only layout and cost (the closed Track O endpoint) for files that are never bound. Arrays indexed by node handle would cost 4 bytes per record word and field (about 20–30 bytes per node per field). The maps cost about 16 bytes per entry at load ≤ 1/2, only for nodes that have the field, and an insert is a multiply and a probe. Fields are grouped by who carries them (declarations; flow-carrying expressions and statements; containers and clauses), so the frequent groups do not pay for the rare fields. If profiling shows the probes, the alternative is a binder word in the record layout (parser/arena change, parser gates).
- **Insertion order.** TypeScript's `Map` and arrays are ordered and the checker's results depend on it in places (declaration order, `exports` iteration); typescript-go's unordered maps are one of the places where TS 7 differs. Track O keeps TypeScript's order, which the dump checks.
- **Ownership.** `Binder.BindSourceFile(SourceFile, CompilerOptions)` stores the output in `SourceFile.Binding`; the result owns it, as it owns its node store (records, lists, texts). Nothing is shared between files: the unreachable flow node is created per file (as in typescript-go; TypeScript shares one per binder, whose Referenced/Shared bits therefore depend on earlier files, so the dump masks them). A file is bound once (`if (!file.locals)`), on one thread at a time; binding writes node flags into the result's records, which no other parse touches. Symbol names are `TextRef`s of the parse's `TextTable` (identifiers are already interned there; computed names such as `__call` or quoted module names are added to its arena).
- **NoAlloc.** Readers and setters of existing records are `[NoAlloc]`; the creating methods allocate only when an array doubles. B1–B3 keep the binder's hot paths allocation-free as the parser's are (growth helpers outlined, as in `NodeStore`).

## Starting point for later optimization

B0's arrays are managed arrays per file, grown ×2 and kept by the result (no copy at the end). Candidates once profiled (not decided): per-thread pooled arrays or slabs as for the node data (step O9 variant C), a size estimate from the node count, a hash index for large tables, a binder word in the node records.

## Added in B3

`BinderStore` also holds the CommonJS module indicator and `jsGlobalAugmentations` per file, and a node map for the rare flow fields `flowNodeWhenTrue`, `flowNodeWhenFalse` and `possiblyExhaustive` (only `true` is stored; absent means `false`, as tsc's factory initializes every switch statement).

## Changed in BO1

All `BinderStore` arrays (symbols, tables, entries, buckets, flows, links, node-map keys and values) are regions of one range in a per-thread `SlabKind.Binder` slab, sized from the node count (about 0.12–0.16 symbols and 0.16–0.17 flow nodes per node); an array that outgrows its region moves to a region twice the size. `node.flowNode` is a dense array indexed by `handle >> 2` instead of a node map. Parents written after the parse live in a dense per-file array (`SlabKind.Parents`), not in the side table. The binding is committed at the end of `BindSourceFile`, owned by the result and released with `SourceFile.Release()`.
