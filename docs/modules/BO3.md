# BO3: binder setup per file and traversal dispatch — brief

Third optimization step of the Phase 2 binder, after BO2 (binder additional run after BO2: parse+bind 0.456 [0.451, 0.462] of typescript-go, **1.222 [1.207, 1.238] of tsrs** (lower bound just above 1.20), single-threaded 1.209 of tsrs; `results/phase2/additional/2026-10-24T15-16-07-263Z/NOTE.md`; the binder endpoint 1.207 stays). Track O only. One attempt; variant A is `main`. Written by the orchestrator. Standing rules of [README.md](README.md); Phase 2 rule: run only the tests and gates your change affects.

## Starting point

`main` at 8cce173. CPU profile after BO2 (vscode × 4, `--bind --single`, JIT; `artifacts/profile/bo2-vscode4-cpu.speedscope.json` in the main checkout; `node harness/profile/hot-paths.mjs`; EventPipe caveats in docs/track-o-profile.md): `Binder.BindFile` 35.0 % of all samples inclusive (parse 63.2 %, of which reading 27.0 %). Attributed self time: `BinderStore..ctor` 5.8 % (7.7 % inclusive) + `NewZeroed` 1.7 % + `GrowArray` 1.5 %, `BindChildren` 2.6 %, `BindWorker` 1.9 %, `BindEachChild` 1.8 %, `Bind` 1.6 %, `BindEach` 1.4 %, `CheckContextualIdentifier` 1.3 %, `NodeStore.EnsureParents` 1.2 %, `DeclareSymbol` 0.9 %, `DeclareSymbolAndAddToSymbolTable` 0.9 %. BO2's report: the store reserves about 222 MB per tuning round, uses about 100 MB, zeroes 44.5 MB and copies 18 MB on growth; removing the zeroing of the dense flow-node array alone measured as noise in AOT — the rest of the setup was not measured.

## Goal

Cheaper binding with byte-identical results:

- **Per-file setup** measured in the NativeAOT build (not the JIT profile): what `BinderStore`'s constructor, `NewZeroed`, `GrowArray` and `EnsureParents` cost per file; sizing that avoids both growth copies and large over-reservation (better estimates from the node count by kind, or counts the parser already has), zeroing only where a slot can be read before it is written (generation stamps or lazy clearing for maps), reuse of the thread's previous binder region layout;
- **Parents:** the binder writes `parent` for every node it visits; measure whether letting the parser write parents into the dense array while it creates nodes, when the caller will bind (e.g. a flag in `CreateSourceFileOptions` or `ParseFile`; parse-only stays without parents), is cheaper than the binder's pass — only if the binder's `parent` reads before writes keep their semantics (BO2 noted that `getModuleInstanceState` reads `body.parent` before it is set), so the tree's parents as observed by the binder must not change;
- **Traversal dispatch:** `Bind` / `BindWorker` / `BindEach` / `BindEachChild` overhead per node (repeated kind switches, flow-state saves for non-containers, the remaining delegate paths for rarer kinds);
- whatever else your AOT measurements show; `[NoAlloc]` where it holds.

## What may change

`src/NeverSharp.Optimized/Binder/`, `src/NeverSharp.Optimized/Arena/` and the parser only for the parent option above (then the full Track O parser gate is required). Not Track F, drivers (except passing the parent option in `--bind` mode, if you add one — `tools/NeverSharp.ParseDriver`), harness, `eng/`.

## Tests to run

Gate B (`gate.mjs --track optimized --phase binder --jsdoc-mode all,types --main …`); the full Track O parser gate if `Arena/` or parser code changes; parse-only tuning parallel must not regress then. Limit: 40 gate runs.

## Working

One attempt: Opus 5.5, high effort (optimization), no sub-agents, own worktree; scratch `<session scratch>/BO3/`. Junctions as in [O9.md](O9.md) (list them; unlink, never delete recursively). C# with the file tools only. Measure against a main driver built from 8cce173 with NativeAOT: tuning corpus `--bind` parallel and `--single`, vscode `--bind --single`, an interleaved in-memory parse+bind microbenchmark (AOT). Before the final report, stop every background task you started. Commit with `git commit -s`; do not push.

## Report

Model; gate runs and summaries; AOT cost of the per-file setup before/after; each change with its measured effect; your own timing against main; NoAlloc count; junctions; commit hash.

## Measurement (orchestrator)

Tuning corpus `cs-o-aot-ws-bind` (decides) and `cs-o-aot-ws-bind-single` (plus parse-only `cs-o-aot-ws` if parser/arena code changed), 20 runs, rig-compliant, against main. Merged if every gate passes and it is not slower by the 95 % interval.
