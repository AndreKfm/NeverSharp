# CO4: parallel check on small programs — brief

Fourth optimization step of the Phase 3 checker, after [CO1](CO1.md)–[CO3](CO3.md) and the checker additional run 1 (`results/phase3/additional/2026-10-31T10-13-22-286Z/NOTE.md`: `cs-o-aot-ws-check` vs `tsgo7-check` G = 1.068 [1.064, 1.070], single-threaded 0.823; the endpoint stays 1.680). Track O only. Written by the orchestrator. Standing rules of [README.md](README.md); run only the tests and gates your change affects. The hard constraint and the working rules of [CO2.md](CO2.md) apply unchanged.

## Starting point

`main` at afe075c (this brief's parent). Gate C passes (50,177 of 50,177); tuning-check and the five repository programs are line-identical with tsc 6.0.3. Layout: [phase3-checker-layout.md](../phase3-checker-layout.md); CO3's "what is left": [phase1-progress.md](../phase1-progress.md), section "CO3".

The remaining gap is the parallel check of small programs. Single-threaded, Track O is at or below typescript-go's time everywhere; with four checkers it gains less (additional run 1, medians):

| Program (files) | tsgo par / single | speedup | C# ws par / single | speedup | C# ws vs tsgo par | process CPU (user) par: tsgo / C# |
|---|---|---:|---|---:|---:|---|
| bluesky (1,688) | 0.80 / 2.24 s | 2.79× | 1.06 / 2.32 s | 2.20× | 1.31 | 3.2 s / 2.3 s |
| playwright (1,415) | 1.47 / 3.92 s | 2.67× | 1.85 / 3.99 s | 2.16× | 1.26 | 6.2 s / 4.6 s |
| tldraw (2,508) | 1.94 / 6.18 s | 3.19× | 1.83 / 4.08 s | 2.23× | 0.94 | 8.2 s / 4.6 s |
| vscode (11,724) | 14.2 / 42.6 s | 3.00× | 15.0 / 37.7 s | 2.51× | 1.06 | 68 s / 44 s |

On bluesky typescript-go keeps about 3.9 cores busy over the wall time, Track O about 2.2: cores stand idle. Candidates, none measured yet: serial phases before or after checking (program construction, module resolution, reading, the order of parse/bind, checker construction and global initialization, collecting and sorting diagnostics), imbalance between the four checkers (file i to checker i % 4, as typescript-go), the per-checker global/lib work every checker repeats (typescript-go repeats it too, but its share in a small program is what counts), and the Workstation GC (bluesky ws 161 ms pause, srv 0.95 s wall). Remaining allocations after CO3: 1.47M surviving strings, mappers, signature parameter arrays, diagnostic objects.

## Goal

1. **Account for the parallel time of a small program** before changing code: a timeline of `--check` with four checkers — per phase (read, parse, bind, program/resolution, checker construction and global initialization, check per checker, diagnostics) and per thread (busy vs waiting) — with a thread-time profile (`dotnet-common,dotnet-sampled-thread-time`, `harness/profile/speedscope-summary.mjs`) and, where typescript-go's driver can show it, the same phases for typescript-go. Say what the 2.2 cores are made of.
2. **Remove what keeps cores idle:** serial phases run in parallel where TypeScript's semantics allow it, checker construction and global initialization per checker on its own thread, imbalance at the end of the pool reduced without changing which checker checks which file.
3. **Cut the per-checker fixed cost** (global initialization, lib symbol tables and types every checker builds) where the profile shows it, keeping each checker's own memory (the b42ecc2 rule; 7ec02cf: no writes into a file's store while checkers run).
4. Whatever else the profiles show, including the CO3 leftovers (strings, mappers, signature parameters, diagnostic objects), and `[NoAlloc]` on hot paths where it now holds.

Hard constraint as in CO1–CO3: results do not change. Type, symbol and signature creation order per checker stays tsc's; the file-to-checker assignment (i % 4) stays, so the parallel diagnostics stay as they are (tuning-check 130,435); single-threaded output stays identical with tsc.

**Corpora:** tune on the tuning-check list — the whole program, and small programs made of a part of it (e.g. its first 1,500 and 2,500 files, as scratch lists; say which). The five benchmark repositories may be used to check the account of goal 1, not to decide changes.

Target: small programs from the tuning-check list clearly faster in parallel, with more cores busy; tuning-check `cs-o-aot-ws --check` not slower, `--single` not slower.

## What may change

As in [CO3.md](CO3.md): `src/NeverSharp.Optimized/Checker/`, `src/NeverSharp.Optimized/Program/` (the pool, program construction, resolution, the driver's check path), `Arena/` if needed (then the parser gate), read-only accessors in `Binder/` (then Gate B). Parser or binder changes only for scheduling (where and when they run), not behaviour; then the Track O parser gate or Gate B. Not Track F, not `eng/`, not the oracle, Gate C's harness, `bench.mjs`/`report.mjs`, the tuning-check list or `docs/preregistration.md`. Driver changes only for measurement switches that default off and are not committed.

## Tests to run

As in [CO3.md](CO3.md): Gate C `--sample 2000` after each kept step; the full corpus after the last step, with no change to `src/` after the last full run (keep its summary line); Gate B / parser gate only if those areas change. At the end: `check-equivalence.mjs --corpus tuning-check --skip-tsrs`, `check-equivalence.mjs --skip-tsrs` on the five repositories, parallel dumps of the five repositories identical with the baseline's, `cs-o-aot-ws --check` on tuning-check 4 times (no crash, 130,435 each time) and `cs-o-aot-srv --check` on tuning-check twice. Limit: 12 Gate C runs.

## Working

One attempt: Opus 5.5, high effort (optimization), no sub-agents. Work directly on `main` in the main checkout; scratch `<session scratch>/CO4/` (earlier CO folders: reuse tools, do not modify). **Before the first change**, build the starting drivers (`build-cs.mjs --tracks optimized --runtimes aot-ws,aot-srv,jit-ws`) to `<session scratch>/CO4/base/`. **C# with the file tools only (Edit/Write): no sed, no Python or other scripts on C# files.** Temporary probes removed before committing. No downloads or installs beyond the existing tools. Commit each kept step with `git commit -s`; do not push. A step is kept only if your own measurement shows it is not slower (tuning-check `--check` and `--check --single`, AOT ws, and the small tuning-check lists `--check`, interleaved with the previous step, at least 5 runs each); revert it otherwise and report it. Before the final report, stop every background task you started. At the end add a CO4 section to [phase1-progress.md](../phase1-progress.md) and update [phase3-checker-layout.md](../phase3-checker-layout.md) if the layout changes.

## Report

The account of goal 1 (phases, threads, cores busy; typescript-go where measurable); the steps with design and own measurements (small lists and tuning-check, parallel and single, ws and srv); tried and reverted; what is left; Gate C runs and the last full summary line; equivalence; stability; NoAlloc count; files outside `Checker/`; commit hashes.

## Measurement (orchestrator)

Tuning-check `cs-o-aot-ws-check` and `cs-o-aot-ws-check-single`, 20 runs each, rig-compliant, baseline drivers (this brief's parent) against the final `main`; plus the agent's small tuning-check lists with hyperfine, 20 runs each. Kept if every gate passes and tuning-check is not slower by the 95 % interval; otherwise the commits are reverted. Then a checker additional run on the benchmark repositories (profile `check`), on the author's decision.
