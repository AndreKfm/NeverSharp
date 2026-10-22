# O6a: handle-based node API for Track O — brief

Step O6 of Track O ([phase1-plan.md](../phase1-plan.md) §7, order from [track-o-profile.md](../track-o-profile.md)) is split (author's decision, 2026-10-22): **O6a** converts Track O once from node objects to node handles behind a storage API, with one straightforward storage; **O6b** then compares up to 3 storage layouts behind that API (pre-registration §5.4: at most 3 variants per step, selected on the tuning corpus). O6a has no variants; it must keep Gate 1 and is measured for the record. Written by the orchestrator before the attempt started. The standing rules of [README.md](README.md) apply, except that Track O follows pre-registration §5.4 instead of rules 1–5 of §5.

## Starting point and goal

`src/NeverSharp.Optimized/` is the Track O seed (step O0): a copy of Track F with node objects (`Node` subclasses from `Generated/Nodes.g.cs`), `NodeArray : List<Node>`, `NodeVisitor.ForEachChild`, `JSDocContainers`. It passes the complete gate.

Goal of O6a: **nodes are `int` handles into a per-parse arena**, as the fixed design of pre-registration §5.4 says ("Nodes are struct records in arenas, referenced by `int` index; … parent links are indices. Speculative parsing (`lookAhead`, `tryParse`) discards nodes by truncating the arenas to a mark"). Concretely:

- A node is an `int` (or a one-field `readonly struct` wrapping it). Kind, pos, end, flags, parent, and every child and value field of the node kinds are read and written **only through a storage API** (`NodeStore` or a name of your choice): e.g. `Kind(n)`, `Pos(n)`, `SetPos(n, v)`, `Flags(n)`, one getter/setter per node field (`Name(n)`, `Expression(n)`, …). Nothing outside the storage implementation knows how nodes are laid out.
- The storage API is the seam for O6b: the layout variants replace only its implementation (and its generator), not the parser or the factory. Keep the implementation in its own files (e.g. `Arena/`), generated where the field set is mechanical (a new emitter `tools/NeverSharp.Generate/emit-optimized.mjs`; `generate.mjs` calls it for Track O instead of `emitFaithful(schema, "Optimized")`).
- The O6a storage itself: the simplest layout that works (e.g. one record per node in a growable array, child fields in a side array); performance is O6b's business. It must support truncation to a mark for `lookAhead`/`tryParse`.
- Node lists may stay as lists of handles in O6a (ranges in an index arena are step O7). Text values may stay `string` (spans are step O5).
- `ForEachChild` over handles in TypeScript's order, generated from the same schema (`forEachChildTable`), and the `jsDoc` of a node through the store.

## Fixed API for the tools (the dump writer, driver and benchmarks call it)

`tools/NeverSharp.Dump.Optimized`, the Track O driver and the benchmarks need, under `#if TRACK_OPTIMIZED`:

- `Parser.CreateSourceFile(string fileName, string sourceText, CreateSourceFileOptions options, bool setParentNodes = false, ScriptKind? scriptKind = null)` returning a result from which the tools can get: the root node, `NodeCount` (as TypeScript's `sourceFile.nodeCount`), the parse diagnostics and JSDoc diagnostics (`Code`, `Start`, `Length`), and for any node: `Kind`, `Pos`, `End`, `Flags` (`NodeFlags`), its `jsDoc` comments (handles) and its children in `forEachChild` order.

You adapt the **Track O branches** of the tools to this API, and nothing else in them:

- `tools/NeverSharp.Dump/AstDump.cs`: only the traversal in `DumpUnit` (the `#if TRACK_OPTIMIZED` part you add); `Render`, `FlagNames`, `Replay`, the outcome handling and everything for Track F stay byte-for-byte as they are. The same order as now: node line, its jsDoc comments as `@` subtrees, then its children.
- `tools/NeverSharp.ParseDriver/Program.cs` and `bench/NeverSharp.Benchmarks/M34.cs`: only the lines that call the parser and read `NodeCount`, under `#if TRACK_OPTIMIZED`.
- The orchestrator reviews these diffs line by line.

## Rules

- Track O rules (pre-registration §5.4): the fixed design above; SIMD only behind runtime checks; output byte-identical to tsc 6.0.3 (Gate 1); Track O Gate 2 (`eng/Gate2.Optimized.props`: banned list, NoAlloc analyzer on annotated code). `[NoAlloc]` annotations are welcome where code is already allocation-free but not required in O6a; do not annotate code that allocates.
- Thread safety as before: no mutable static state except pools.
- Only `src/NeverSharp.Optimized/`, the new emitter and its call in `generate.mjs`, and the tool branches above change. Not Track F, not tests, not harness, not docs, not other tools.

## Gate (run it, iterate until it passes)

```
<main>/.tools/node-24.21.0/node.exe harness/port/gate.mjs --track optimized --main D:/Private/Source/Andre/projects/NeverSharp
```

All gates of Track O: Gate 2, unit tests (M1, M2), G1a, G1 with ParseNone, Gate 1 (ParseAll), each on all 50,177 units, byte-identical to tsc 6.0.3. Limit: 100 gate runs (standing rule 2). Track F's gate (`--module M5`) must still pass at the end (you do not change Track F, so it will).

## Working

- One attempt, Opus 5.5, high effort (optimization work, prereg/v3). Sub-agents of the same model allowed, at most 4, for mechanical parts (e.g. converting parser files to the handle API); they follow this brief.
- Scratch folder: `<session scratch>/O6a/`. C# with the file tools, not shell heredocs.
- Commit with `git commit -s` on the worktree branch; do not push.

## Report

1. Model (and sub-agents).
2. Last gate JSON line, number of gate runs.
3. The storage API (file, the main members) and the O6a layout; how truncation to a mark works.
4. The tool diffs (files, what changed).
5. Deviations and open points for O6b, O5, O7.
6. A rough timing of your own on the tuning corpus (not the ranking), and the bytes allocated, against the seed.
7. Commit hash.
