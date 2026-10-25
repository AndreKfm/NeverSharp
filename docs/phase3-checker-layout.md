# Phase 3: Track O program layer and checker data layout (module C0)

Decided in module C0 ([modules/C0.md](modules/C0.md), [phase3-plan.md](phase3-plan.md)). Code: `src/NeverSharp.Optimized/Program/` (`CompilerOptions.cs`, `CompilerHost.cs`, `DefaultLibrary.cs`, `LibraryNames.cs`, `Program.cs`, `Diagnostics.cs`) and `src/NeverSharp.Optimized/Checker/` (`Handles.cs`, `TypeStore.cs`, `Links.cs`, `Checker.cs`). C0 implements the program layer for Gate C and the storage of the checker; modules C1–C9 port the checker on top of it and may refine the details below. Later optimization steps change the layout only after profiling, as in Phases 1–2.

## Program layer

| Part | TypeScript 6.0.3 | Track O (C0) |
|---|---|---|
| Options | `CompilerOptions`, computed values `computedOptions` | `CompilerOptions` (moved from `Binder.cs`, extended; nullable members = undefined), the 26 computed values as `Get*` methods and `ComputedOptionValues()` (the checker dump writes them, the oracle writes `ts.computedOptions`) |
| Host | `CompilerHost` | `ICompilerHost` (getSourceFile returns a parsed file; default lib name and folder; jsDocParsingMode); `MemoryCompilerHost` is Gate C's host (one file from memory, library files under `/.ts/`, nothing else exists) |
| Program | `createProgram` | `Program.Create(rootNames, options, host)`: root files, the default library and `/// <reference lib>` in TypeScript's processing order (post-order), default library files stably sorted by `getDefaultLibFilePriority` (`LibraryNames`, the `libEntries` table), then the other files; TS6504/TS6054/TS6053 with the "file is in the program because" chain for root files. Only `noResolve: true` (Gate C and the checker endpoint); other configurations throw `NotSupportedException` instead of differing silently |
| Diagnostics | `Diagnostic` with `messageText: string \| DiagnosticMessageChain` | `ProgramDiagnostic` / `ProgramRelatedInformation` (subclasses of the parser's `Diagnostic` types) with their `SourceFile` and an optional `DiagnosticMessageChain`; `ProgramDiagnostics.FlattenDiagnosticMessageText` |

Known gaps, left to the modules named: comment directives (`@ts-ignore`, `@ts-expect-error`) and the program diagnostics of a file such as unknown lib references (C8); JavaScript files with `allowJs` (C9); the external module indicator for files that are modules only by their format is set after the parse, so such a file with a top-level `await` misses the parser's top-level-await reparse (needs a parser seam, C1).

## Default library: loading decision

- **Source:** the pinned `typescript@6.0.3` package that the oracle uses, `harness/astdump/node_modules/typescript/lib` (in a worktree: the main checkout's, through `--lib-dir`, which the gate passes). Not a copy in `artifacts/`: the oracle and the C# side then read the very same bytes, the version is pinned by `package-lock.json` and is part of the gate's oracle stamp, and there is nothing to keep in sync. The benchmark endpoint can point `--lib-dir` at the same folder.
- **Once per process, shared read-only:** `DefaultLibrary.Get(directory, virtualFolder, options, jsDocParsingMode)` returns one instance per combination of the inputs that change parse or bind (emit target, JSDoc mode, alwaysStrict, noFallthroughCasesInSwitch). Each file is parsed (`Parser.ParseFile`, Track O's reader), its line map computed and bound once, behind a `Lazy<T>` with `ExecutionAndPublication`; afterwards nothing writes to it. The 82 files of the default program (`lib.es2025.full.d.ts` and its references, DOM included) take about 0.3 s once per process in the C# dump writer. TypeScript's harness and language service share library source files between programs the same way, and so does the oracle (per worker).
- **Thread safety:** a result's node store and binder output are independent of later parses (Phases 1–2). The rules for checkers: never write into a shared file's stores (no lazy caches in node records or side tables, no symbol ids or merge ids in binder records, no rebuild of a binder table's hash index — the binder leaves it uncommitted, a lookup scans); everything a checker computes about library symbols and nodes goes into its own link stores below. C1 must audit the utilities it ports for such writes (e.g. modifier-flag caches).

## Checker data layout

| Checker data | TypeScript 6.0.3 | typescript-go | Track O |
|---|---|---|---|
| Type | object, `id = ++typeCount` per checker | `*Type` (flags, objectFlags, id, symbol, alias, `data TypeData`) | `Type` handle (int, 0 = undefined) into the checker's `TypeStore`: one common record per type in creation order (flags, objectFlags, symbol, alias symbol and arguments, data index); **the handle is the type id**, so the union/intersection member order by id is the order by handle. Kind-specific data (intrinsic, literal, object, union, …) in arrays of their own, as typescript-go's `TypeData` variants |
| Type lists | arrays | slices | `TypeList` (start, count) in an append-only arena of the store |
| Signature | object | `*Signature` | `Signature` handle into `SignatureStore` (records in creation order; handle = id) |
| Symbol | binder or transient objects; `getSymbolId` assigns process-wide ids lazily | `*ast.Symbol` | `SymbolRef` (file, index): file 1..n = binder symbol of the program's n-th file, file 0 = transient symbol in the checker's `TransientSymbolStore` (merged clones, instantiated and synthetic symbols, checker-made symbols). No ids written into binder records |
| Node | object, `getNodeId` lazily | `*ast.Node` | `NodeRef` (file, node handle): Track O node handles are per file |
| SymbolLinks | one object per symbol in `symbolLinks[getSymbolId(s)]` (transient symbols carry their links) | per-kind `core.LinkStore[*ast.Symbol, V]` (`ValueSymbolLinks`, `AliasSymbolLinks`, `DeclaredTypeLinks`, …) | per-kind `SymbolLinkStore<T>`: per file a dense slot array indexed by the symbol index (binder indices are dense per file), records in one array; only symbols with links of that kind cost a record |
| NodeLinks | one object per node in `nodeLinks[getNodeId(n)]` | per-kind `LinkStore[*ast.Node, V]` (`NodeLinks`, `SymbolNodeLinks`, `TypeNodeLinks`, `SignatureLinks`, …) | per-kind `NodeLinkStore<T>`: per file an open-addressing map from node handle to record (handles are sparse record offsets; as the binder's node maps) |
| Diagnostics | per checker | per checker | per checker (`ProgramDiagnostic` with source file) |

### Why

- **Handles and per-checker arrays, as the parser and binder.** Records in arrays mean one allocation per growth instead of one per type, no references for the collector to trace once the arrays are native or pooled, and access through the stores only, so the layout can change (slabs, struct-of-arrays) without touching the checker. typescript-go keeps the same structure with Go pointers; its per-kind link stores are adopted because the checker's link kinds are many and sparse.
- **Ids in creation order.** TypeScript's checker results depend on type ids (union ordering, caches) and on creation order (risk named in the plan). A handle that is the id cannot drift from it; checkers never share types, so ids are per checker as in TypeScript.
- **Symbols and nodes as (file, handle).** Track O's handles are per file; library files are shared between programs and threads, so a program-wide numbering stored in the files is impossible, and (file, index) needs no table. Lazily assigned process-wide symbol and node ids (TypeScript) are not reproduced: they matter only where TypeScript orders by them; C1–C4 port those places with per-checker counters where they are observable (to be checked when they appear; the oracle's determinism checks below found no dependence on unit order).
- **C0 keeps managed arrays** (grown ×2); the checker is not measured until Gate C passes. Candidates once profiled (not decided): per-thread slabs as for parser and binder, native records, pooling of the per-file link tables.

## Oracle facts the layout must respect

- The oracle shares the parsed and bound library between the units of a worker (warm-up program first, so every unit sees the same library state). Dumps are identical over two full runs, and a 201-unit sample gave the same dumps run alone, in reverse order and inside the full run: no dependence on what was checked before.
- Gate C options `{ noResolve: true }`; TypeScript 6.0.3's defaults then give target ES2025, module ES2022, moduleResolution bundler, `strict` on, default library `lib.es2025.full.d.ts` (82 files including DOM).
