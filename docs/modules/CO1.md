# CO1: checker scaling and allocations — brief

First optimization step of the Phase 3 checker ([phase3-plan.md](../phase3-plan.md): "optimization steps after Gate C, as in Phases 1–2, profile first"), after the checker endpoint (G = 1.680 [1.675, 1.689] `cs-o-aot-ws-check` vs `tsgo7-check`, no parity; `results/phase3/checker-endpoint/2026-10-29T14-41-57-488Z/VERDICT.md`; later gains are reported as additional runs next to it, never instead of it). Track O only. Written by the orchestrator. Standing rules of [README.md](README.md); run only the tests and gates your change affects.

## Starting point

`main` at bfa872b. Gate C passes (50,176 of 50,177; 027977's timeout under load accepted); the five repository programs and the tuning-check program (C12, 24,504 files) are line-identical with tsc 6.0.3.

Endpoint: single-threaded C# is faster than typescript-go (0.954), in parallel it is 1.68× slower. From one to four checkers C# gains 1.76×, typescript-go 2.98×; vscode allocates 20.2 GB with 13.0 s GC pause under Workstation GC (2.3 s under Server GC); Server GC alone gives 1.055.

The tuning-check program shows the same gap (orchestrator, one run each, 2026-10-30, wall time, other load possible):

| Driver | `--check` (4 checkers) | `--check --single` | speedup |
|---|---:|---:|---:|
| typescript-go 7.0.2 (`parse-go`) | 18.4 s | 33.6 s | 1.83× |
| C# `cs-o-aot-ws` | 27.7 s | 30.7 s | 1.11× |
| C# `cs-o-aot-srv` | 20.2 s | 28.7 s | 1.42× |

Allocation profile, tuning-check `--check --single`, JIT ws (`dotnet-trace --profile gc-verbose`, `tools/NeverSharp.TraceSummary`; `artifacts/profile/c-tc-single-alloc.nettrace` in the main checkout): 12,483 MB sampled, 1,214 GCs, 3,177 ms pause.

| Share | Type |
|---:|---|
| 13.3 % | `String` (1,661 MB) |
| 9.9 % | `String[]` (1,239 MB) |
| 6.1 % | `Dictionary<string, int>` entries (757 MB) |
| 5.9 % | `Dictionary<(Type, string), SymbolRef>` entries (742 MB) |
| 5.1 % | `DiagnosticRelatedInformation[]` (641 MB) |
| 5.0 % | `Type[]` (623 MB) |
| 4.8 % | `TransientSymbolRecord[]` (604 MB) |
| 3.9 % / 3.5 % | `Int32[]` (482 MB), `SymbolRef[]` (438 MB) |
| 2.7 % each | `ProgramDiagnostic` (342 MB), `Char[]` (332 MB) |
| 2.2 % each | `StringBuilder` (278 MB), `ValueSymbolLinks[]` (269 MB) |
| ≤ 1.6 % | `DiagnosticWithLocation`, `List<Type>`, `TypeRecord[]`, `Relater`, `NodeRef[]`, `ObjectTypeData`, closures and delegates (`Func<Type,bool>`, display classes, `Predicate<…>`, `Action`) |

The pool has no locks (`Program/CheckerPool.cs`: file i to checker i % 4, as typescript-go), so the scaling loss is not explained yet: GC (ws vs srv already shows about 7.5 s), the allocation volume, load imbalance between the checkers, work every checker repeats, or memory bandwidth.

## Goal

1. **Find where parallel time goes.** Per-checker wall time and file share on tuning-check and vscode (C# and, where its driver can show it, typescript-go); a thread-time profile of the parallel run (`dotnet-common,dotnet-sampled-thread-time`, `harness/profile/speedscope-summary.mjs`); GC count and pause in parallel ws vs srv. Say what the 1.11× (ws) / 1.42× (srv) is made of before changing code for it.
2. **Cut the checker's allocations** where the profile points, following Phases 1–2: per-checker arenas or slabs for types, transient symbols, links and lists instead of growing managed arrays; no strings where the checker only needs identities (find which code builds the `String`/`String[]`/`Dictionary<string,int>` volume — symbol-table keys, property caches keyed by `(Type, string)`, escaped names, message formatting — and replace them with interned text or handles); diagnostics and related information built once without intermediate arrays; no closures or delegates on hot paths. Each checker keeps its own memory (the b42ecc2 rule: never write into a store shared with other checkers).
3. **Whatever else** the profiles show after 1 and 2 (e.g. imbalance, repeated per-checker work typescript-go avoids in the same structure).
4. `[NoAlloc]` on checker hot paths where it now holds (Track O Gate 2 checks it).

Hard constraint: the checker's results do not change. Type and symbol creation order stays tsc's (type ids, union member order); no caching that changes which diagnostics are produced or their text; single-threaded output stays identical with tsc.

Target: parallel tuning-check `cs-o-aot-ws --check` clearly faster than main and single-threaded not slower; allocation volume down by a large factor; a measured account of the remaining scaling gap.

## What may change

`src/NeverSharp.Optimized/Checker/`, `src/NeverSharp.Optimized/Program/`, `src/NeverSharp.Optimized/Arena/` where the checker needs it (then the parser gate too). Not the parser, scanner or binder behaviour, not Track F, not `eng/`, not the oracle, Gate C's harness, `bench.mjs`/`report.mjs`, the tuning-check list or `docs/preregistration.md`. Driver changes only for measurement switches that default off (say so).

## Tests to run

Gate C (`gate.mjs --track optimized --phase checker --sample 2000`) after each substantial change; the full corpus at the end, with no change to `src/` after the last full run. The full Track O parser gate (`gate.mjs --track optimized`) only if `Arena/` or node storage changes. At the end: `check-equivalence.mjs --corpus tuning-check --skip-tsrs` (the tsc side cached in your scratch) and `check-equivalence.mjs --skip-tsrs` on the five repositories; `cs-o-aot-ws --check` on tuning-check 4 times (no crash, 130,435 each time). Limit: 12 Gate C runs.

## Working

One attempt: Opus 5.5, high effort (optimization), no sub-agents. Work directly on `main` in the main checkout (no worktree, no branch); scratch `<session scratch>/CO1/`. **Before the first change**, build and copy the starting drivers (`build-cs.mjs --tracks optimized --runtimes aot-ws,aot-srv,jit-ws`) to `<session scratch>/CO1/base/` as the baseline. **C# with the file tools only (Edit/Write): no sed, no Python or other scripts on C# files, not even for comments.** Temporary probes in C# are removed before committing. Do not download or install anything beyond the existing tools (`.tools/dotnet-trace`). Commit each kept change with `git commit -s` (noreply identity as configured); do not push. A change is kept only if your own measurement shows it is not slower (tuning-check `--check` and `--check --single`, AOT ws, interleaved with the baseline, at least 5 runs each); revert it otherwise and report it. `bench.mjs` needs a clean tree (`git stash -u` around it, or run hyperfine directly). Before the final report, stop every background task you started. At the end add a CO1 section to [phase1-progress.md](../phase1-progress.md).

## Report

Brief: where the parallel time goes (per-checker times, GC, profile summary); the changes with their own measurements against the baseline (parallel and single, ws and srv), allocation before/after per type, GC count and pause; what was tried and reverted; the remaining scaling gap and its causes; Gate C runs and the last full summary; tuning-check and five-repository equivalence; stability runs; NoAlloc count; files outside `Checker/`; commit hashes.

## Measurement (orchestrator)

Tuning-check `cs-o-aot-ws-check` (decides) and `cs-o-aot-ws-check-single`, 20 runs each, rig-compliant, baseline drivers against the final `main`. Kept if every gate passes and it is not slower by the 95 % interval; otherwise the commits are reverted. An additional checker run on the benchmark repositories (profile `check`) follows on the author's decision.
