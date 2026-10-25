# BO4: binder per-file memory handling, sentry — brief

Fourth optimization step of the Phase 2 binder, after BO3 (binder additional run after BO3: parse+bind 0.434 [0.430, 0.440] of typescript-go, **1.193 [1.172, 1.204] of tsrs** (upper bound just above 1.20), single-threaded 1.172 of tsrs; per repository vs tsrs: vscode 1.17, **sentry 1.26**, bluesky 1.15, playwright 1.21, tldraw 1.18; `results/phase2/additional/2026-10-24T22-33-44-479Z/NOTE.md`; the binder endpoint 1.207 stays). Track O only. One attempt; variant A is `main`. Written by the orchestrator. Standing rules of [README.md](README.md); Phase 2 rule: run only the tests and gates your change affects.

## Starting point

`main` at 379b510. CPU profile after BO3 (vscode × 4, `--bind --single`, JIT; `artifacts/profile/bo3-vscode4-cpu.speedscope.json` in the main checkout; EventPipe over-represents native memory operations and I/O, see docs/track-o-profile.md): `Binder.BindFile` 26.2 % inclusive; attributed self time: `BinderStore.CommitScratch` 4.4 % (5.8 % inclusive; the copy into the exact-size committed range BO3 introduced), `BinderStore..ctor` 3.9 %, `Compact` 1.3 %, `NodeStore.EnsureParents` 1.2 %, `NewZeroed` 1.0 % — about 12 % of all samples, nearly half the binder's — then `BindChildren` 2.4 %, `Bind` 2.0 %, `BindEachChild` 1.4 %, `BindEach` 1.2 %, `CheckContextualIdentifier` 0.9 %. BO3 measured in NativeAOT that page faults of fresh slab pages dominate the setup (272 ns per page single-threaded, 530–660 ns when 12–24 threads fault at once) and chose scratch + compacted copy for small files (faults −15 %).

## Goal

Cheaper per-file memory handling with byte-identical results, measured in NativeAOT (not from the JIT profile):

- the commit copy: can results keep their data without the copy and without the slack's page faults — e.g. committing in place when the scratch region's used part is close to its reserved size, handing over a scratch block that is nearly full and starting a fresh one, packing several files' committed data into one shared committed slab (no slack per file), or copying only the parts that are not already compact;
- the constructor and zeroing: what remains per file after BO3, and whether it can be cut further (no zeroing of regions that are fully written before being read, generation-stamped maps);
- `EnsureParents` per file;
- **sentry** (the slowest repository against tsrs, 1.26): measure what is different there (file size distribution, constructs) and whether a specific binder path costs more on it;
- anything else your AOT measurements show.

## What may change

`src/NeverSharp.Optimized/Binder/`, `src/NeverSharp.Optimized/Arena/` (then the full Track O parser gate is required and parse-only must not regress). Not the parser's grammar, Track F, drivers, harness, `eng/`.

## Tests to run

Gate B (`gate.mjs --track optimized --phase binder --jsdoc-mode all,types --main …`); the full Track O parser gate only if `Arena/` changes. Limit: 40 gate runs.

## Working

One attempt: Opus 5.5, high effort (optimization), no sub-agents, own worktree; scratch `<session scratch>/BO4/` (BO2's and BO3's microbenchmarks and instrumented trees are in the sibling folders). Junctions as in [O9.md](O9.md) (list them; unlink, never delete recursively). C# with the file tools only (also for renames). Measure against a main driver built from 379b510, NativeAOT: tuning corpus `--bind` parallel and `--single`, sentry and vscode `--bind` parallel and `--single`, an interleaved in-memory parse+bind microbenchmark, page-fault counts. Before the final report, stop every background task you started. Commit with `git commit -s`; do not push.

## Report

Model; gate runs and summaries; AOT costs before/after (commit, setup, faults); what sentry showed; each change with its measured effect; your own timing against main; NoAlloc count; junctions; commit hash.

## Measurement (orchestrator)

Tuning corpus `cs-o-aot-ws-bind` (decides) and `cs-o-aot-ws-bind-single` (plus parse-only `cs-o-aot-ws` if `Arena/` changed), 20 runs, rig-compliant, against main. Merged if every gate passes and it is not slower by the 95 % interval.
