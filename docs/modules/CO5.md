# CO5: Workstation GC pause on small programs — brief

Fifth optimization step of the Phase 3 checker, after [CO1](CO1.md)–[CO4](CO4.md) and the checker additional run 2 (`results/phase3/additional/2026-11-01T06-48-39-651Z/NOTE.md`: `cs-o-aot-ws-check` vs `tsgo7-check` G = 1.025 [1.020, 1.030], single-threaded 0.818; the endpoint stays 1.680). Track O only. Written by the orchestrator. Standing rules of [README.md](README.md); run only the tests and gates your change affects. The hard constraint and the working rules of [CO2.md](CO2.md) apply unchanged.

## Starting point

`main` at this brief's parent (15dddf4 + this brief). Gate C passes (50,177 of 50,177); tuning-check and the five repository programs are line-identical with tsc 6.0.3. Layout: [phase3-checker-layout.md](../phase3-checker-layout.md); CO3's and CO4's "what is left": [phase1-progress.md](../phase1-progress.md), sections "CO3" and "CO4".

The remaining parallel gap is on the small programs, and it is the Workstation GC pause. With Server GC both are within 9 % of typescript-go (additional run 2, medians):

| Program (files) | tsgo par | C# ws par | ws vs tsgo | C# srv par | srv vs tsgo | ws GC pause (count gen0/1/2) | srv GC pause |
|---|---:|---:|---:|---:|---:|---|---:|
| bluesky (1,688) | 0.815 s | 0.993 s | 1.217 | 0.866 s | 1.062 | 186 ms (87/29/6) | 69 ms |
| playwright (1,415) | 1.483 s | 1.837 s | 1.238 | 1.610 s | 1.086 | 337 ms (151/49/8) | 124 ms |
| tldraw (2,508) | 1.953 s | 1.777 s | 0.910 | 1.516 s | 0.776 | 395 ms (165/54/7) | 115 ms |
| vscode (11,724) | 14.48 s | 15.01 s | 1.037 | 11.84 s | 0.818 | 3.87 s (1486/434/9) | 633 ms |

CO4's account (tc2500 = the first 2,500 lines of the tuning-check list): the ws pause is about 25 % of the wall time, and every collection stops all four checkers. The pause follows what survives a collection:
- checker data allocated as small objects: strings (1.47M surviving on vscode after CO3), diagnostics, signature parameter arrays, mappers and `Type[]`;
- the parse and bind data of the program's files, promoted during program and checker construction;
- mid-life objects of the four checkers' work in progress. The gen0 budget of the concurrent ws GC is about 6 MB.

Already tried:
- **CO3, slower:** signature parameters as arena ranges (`--single` 1.011, `--check` 1.018); array and deferred mappers with `TypeList` sources and targets (1.016 / 1.005).
- **CO4, no effect on the pause:** symbol slot pages on the large object heap.
- **CO1, more pause:** symbol-table arrays on the pinned object heap.

The runtime GC configuration is not an option: in CO1 the author chose redesigning the data over changing the GC configuration, and `eng/` stays unchanged.

## Goal

1. **Account for the pause first, before changing code.** Use tc1500, tc2500 and bluesky with `--check`, AOT ws.
   - Per collection: generation, pause, promoted bytes, and what is promoted by type and by origin (program/parse/bind, checker construction, checker data, work in progress). Use `.tools/dotnet-trace` with GC events (`gc-verbose` profile or GC keywords incl. `GCHeapSurvivalAndMovement`) and the runtime's GC statistics. Nothing else is installed for this: no `dotnet-gcdump`, no downloads.
   - Say which share of the pause is gen0/gen1 promotion of checker data and which is gen2 or background work, and what would remove the largest share.
2. **Fewer surviving small objects** where the account shows them. Candidates, depending on the account:
   - strings created by the checker: names, escaped names, literal values, type-to-string caches, and diagnostic message arguments as ids or arena spans;
   - diagnostics as records or arena entries until they are reported;
   - mappers and signature parameters in a form that does not cost the CPU that CO3's attempts cost (for example only the long-lived ones, or created lazily);
   - `Type[]`/`List<>` for long-lived type lists.
3. **Less promotion of program data:** parse/bind data that is still managed and survives into gen1/gen2 during program and checker construction, moved to native or arena storage, or allocated so that it is not copied again.
4. **Fewer allocations of work in progress** on the hot paths of the four checkers (pooled or stack-allocated temporaries, fewer closures and iterators, `[NoAlloc]` where it now holds), so that collections happen less often.

**Hard constraint, as in CO1–CO4:** results do not change.
- Type, symbol and signature creation order per checker stays tsc's.
- The file-to-checker assignment (i % 4) stays.
- Parallel diagnostics stay as they are (tuning-check 130,435).
- Single-threaded output stays identical with tsc.
- Each checker keeps its own memory (the b42ecc2 rule), and nothing writes into a file's store while checkers run (7ec02cf).

**Corpora:** tune on the tuning-check list: the whole program plus tc1500 and tc2500 (the first 1,500 and 2,500 lines of `artifacts/bench/filelists/tuning-check.txt`, as scratch lists). The five benchmark repositories may be used to check the account of goal 1, not to decide changes.

**Target:**
- tc1500 and tc2500 `cs-o-aot-ws --check` clearly faster, with less GC pause;
- tuning-check `cs-o-aot-ws --check` not slower;
- `--single` not slower.

## What may change

As in [CO4.md](CO4.md):
- `src/NeverSharp.Optimized/Checker/` and `src/NeverSharp.Optimized/Program/`.
- `Arena/` if needed; then run the parser gate.
- Read-only accessors in `Binder/`; then run Gate B.
- Parser or binder storage, only if goal 3 needs it and behaviour does not change; then the Track O parser gate and Gate B.

Must not change: Track F, `eng/` (no GC or runtime configuration), the oracle, Gate C's harness, `bench.mjs`/`report.mjs`, the tuning-check list and `docs/preregistration.md`. Driver changes are allowed only for measurement switches that default to off and are not committed.

## Tests to run

As in [CO4.md](CO4.md):
- Gate C `--sample 2000` after each kept step.
- The full corpus after the last step, with no change to `src/` after the last full run. Keep its summary line.
- Gate B or the parser gate only if those areas change.
- At the end:
  - `check-equivalence.mjs --corpus tuning-check --skip-tsrs`;
  - `check-equivalence.mjs --skip-tsrs` on the five repositories;
  - parallel dumps of the five repositories identical with the baseline's;
  - `cs-o-aot-ws --check` on tuning-check 4 times: no crash, 130,435 each time;
  - `cs-o-aot-srv --check` on tuning-check twice.
- Limit: 12 Gate C runs.

## Working

- **Who:** one attempt with Opus 5.5 at high effort (optimization), no sub-agents.
- **Where:** directly on `main` in the main checkout. Scratch: `<session scratch>/CO5/`. Earlier CO folders: reuse their tools, do not modify them.
- **Before the first change:** build the starting drivers (`build-cs.mjs --tracks optimized --runtimes aot-ws,aot-srv,jit-ws`) into `<session scratch>/CO5/base/`.
- **Editing C#:** with the file tools only (Edit/Write). No sed, no Python or other scripts on C# files.
- **Probes:** remove temporary probes before committing.
- **Tools:** no downloads or installs beyond the existing tools.
- **Commits:** commit each kept step with `git commit -s`; do not push.
- **Keep rule:** a step is kept only if your own measurement shows it is not slower. Measure tuning-check `--check` and `--check --single` (AOT ws) and tc1500/tc2500 `--check`, interleaved with the previous step, at least 5 runs each. Otherwise revert the step and report it.
  - A single-threaded change of about 1 % with no extra work may be AOT code placement (CO4's follow-up). Show that with a no-op control, as CO4 did, before reporting it as such.
- **Before the final report:** stop every background task you started.
- **Docs at the end:** add a CO5 section to [phase1-progress.md](../phase1-progress.md), and update [phase3-checker-layout.md](../phase3-checker-layout.md) if the layout changes.

## Report

- The account from goal 1: per collection, promoted bytes by type and origin, and the shares of the pause.
- The steps with their design and your own measurements: tc1500/tc2500 and tuning-check, parallel and single, ws and srv, with GC count and pause before and after.
- What was tried and reverted.
- What is left.
- Gate C runs and the last full summary line.
- Equivalence and stability.
- NoAlloc count.
- Files changed outside `Checker/`.
- Commit hashes.

## Measurement (orchestrator)

- **Runs:** tuning-check `cs-o-aot-ws-check` and `cs-o-aot-ws-check-single`, 20 runs each, rig-compliant, baseline drivers (this brief's parent) against the final `main`. Plus tc1500/tc2500 with hyperfine, 20 runs each.
- **Kept** if every gate passes and tuning-check is not slower by the 95 % interval; otherwise the commits are reverted.
- **Then** a checker additional run on the benchmark repositories (profile `check`), on the author's decision.
