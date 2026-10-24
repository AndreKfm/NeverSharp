# BO2: binder CPU hot paths — brief

Second optimization step of the Phase 2 binder, after BO1 (binder additional run: parse+bind 0.489 [0.483, 0.498] of typescript-go, 1.334 [1.310, 1.352] of tsrs, single-threaded 1.416 of tsrs; `results/phase2/additional/2026-10-24T12-36-27-826Z/NOTE.md`; the binder endpoint 1.207 stays). Track O only. One attempt; variant A is `main`. Written by the orchestrator. Standing rules of [README.md](README.md); Phase 2 rule: run only the tests and gates your change affects.

## Starting point

`main` at a704727. CPU profile of parse+bind, vscode × 4, single-threaded, JIT (`artifacts/profile/bo1-vscode4-cpu.speedscope.json` in the main checkout; `node harness/profile/hot-paths.mjs`; EventPipe over-represents native I/O and GC polls, see docs/track-o-profile.md): `Binder.BindFile` 41.3 % of all samples inclusive (parse 55.1 %, of which reading 29.4 %). Attributed self time in the binder: `BindChildren` 9.4 %, `BinderStore..ctor` 4.4 % (plus `NewZeroed` 0.9 %), `NodeStore.GrowSide` 2.7 % and `FindSide` 0.6 % (the side table is still written during binding), `CheckContextualIdentifier` 1.9 %, `BindCallExpressionFlow` 1.5 %, `BindWorker` 1.5 %, `BinderStore.GrowArray` 1.2 %, `NodeStore.EnsureParents` 1.1 %, `DeclareSymbol` 1.0 %; closures `Binder.<.ctor>b__39_0` / `b__39_1` 2.8 % / 3.3 % inclusive (delegates in the traversal).

## Goal

Cheaper binding with byte-identical results:

- find out what `BindChildren` spends its self time on and remove it (flow-state save/restore, repeated container checks, per-node switches; typescript-go's `bindChildren` for comparison);
- the `BinderStore` constructor: no zeroing of large regions per file (lazily cleared or generation-stamped maps, uninitialized regions where every slot is written before it is read, sizes that fit without over-allocation);
- why the side table is still written during binding (which fields: find them, move them to the dense arrays or the binder store), and `EnsureParents` cost;
- delegates and closures on the traversal replaced by struct callbacks or direct calls (as step O-NA did for the parser);
- `CheckContextualIdentifier` and other per-identifier checks with a cheap pre-check;
- whatever else the profile and your measurements show; `[NoAlloc]` on the binder hot paths where it holds.

## What may change

`src/NeverSharp.Optimized/Binder/`, `src/NeverSharp.Optimized/Arena/` (only for the side-table/parents question). Not the parser's grammar, Track F, drivers, harness, `eng/`.

## Tests to run

Gate B (`gate.mjs --track optimized --phase binder --jsdoc-mode all,types --main …`); the full Track O parser gate only if you change `Arena/` code. Limit: 40 gate runs.

## Working

One attempt: Opus 5.5, high effort (optimization), no sub-agents, own worktree; scratch `<session scratch>/BO2/`. Junctions as in [O9.md](O9.md) (list them; unlink, never delete recursively). C# with the file tools only. Measure against a main driver built from a704727: tuning corpus `--bind` parallel and `--single`, vscode `--bind --single`, an interleaved in-memory parse+bind microbenchmark; parse-only tuning parallel must not regress if you touched `Arena/`. Before the final report, stop every background task you started. Commit with `git commit -s`; do not push.

## Report

Model; gate runs and summaries; what `BindChildren` spent its time on; each change with its measured effect; your own timing against main; NoAlloc count; junctions; commit hash.

## Measurement (orchestrator)

Tuning corpus `cs-o-aot-ws-bind` (decides) and `cs-o-aot-ws-bind-single` (plus parse-only `cs-o-aot-ws` if `Arena/` changed), 20 runs, rig-compliant, against main. Merged if every gate passes and it is not slower by the 95 % interval.
