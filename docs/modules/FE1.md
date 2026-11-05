# FE1: the parallel front end and the serial parts around the check — brief

Track O. **Author's decision of 2026-11-05:** after SC1, shorten the wall time outside the checkers' check, with tsrs as the model.

The author's standing direction holds:
- optimize now, conformity at the end;
- workloads of 30 s or less;
- many short steps rather than long runs;
- no full gates for now (a small Gate C sample suffices);
- report results as the factor against tsrs per repository.

Written by the orchestrator. The standing rules of [README.md](README.md) apply, except where this brief relaxes them.

## Why

SC1's tsrs-like pool (`--pool tsrs`: locality + stealing; 16 checkers with Server GC, 12 with Workstation GC) brought the factor against tsrs from 4.50 to 2.12 (srv) and 3.10 (ws), as a geometric mean over the five repositories. The measurement is in `results/phase3/SC1/run1/`.

- **On vscode with `--pool tsrs` srv,** the wall time is 5.23 s, but the slowest checker's creation plus check takes 3.89 s. That leaves about 1.3 s outside the checkers, before and after the check:
  - parse;
  - program walk and import resolution;
  - binding;
  - default library;
  - the locality assignment;
  - checker construction (globals merge, duplicate-declaration errors of all files, per checker);
  - global diagnostics, sorting and output;
  - process exit.
- **tsrs** parses, resolves and binds vscode in 0.41–0.50 s, and its whole run takes 2.24 s ([tsrs-gap.md](../tsrs-gap.md) §3).
- **Gain:** at srv 16 the 1.3 s are about 25 % of the wall time. Cutting them to tsrs's level would gain about 0.8 s on vscode (factor 2.30 → about 1.95).
- **Known items from earlier work** (phase1-progress.md, CO4 and TG1):
  - in parallel, import resolution plus binding take 0.62–0.69 s on vscode, against 2.13 s single-threaded, so only 3.2× on 4+ cores;
  - the default library is parsed and bound serially in `Program.Create` (about 38 ms), because `Program.Create` does not know whether the run is single-threaded;
  - each checker merges the globals of all script files and creates the duplicate-declaration errors of all files, and three quarters of those errors are never read;
  - the GC promotes the just-parsed files during program construction.

## Starting point

`main` at this brief's parent (9c5f5e1 + this brief), which includes SC1.
- The front end is in `src/NeverSharp.Optimized/Program/` (`Program.cs`, `Program.Resolution.cs`, `CompilerHost.cs`, `ModuleNameResolver.cs`, `DefaultLibrary.cs`, `CheckerPool.cs`, `CheckerAssignment.cs`) and the parser and binder directories.
- The driver is `tools/NeverSharp.ParseDriver/` (`Program.cs`, `CheckMode.cs`). `NEVERSHARP_POOL_STATS=1` already prints creation and check seconds per checker.

**tsrs** is read-only at `external/tsrs/`. Read these notes first: each describes where tsrs's front end and fixed costs went and what it changed.
- `notes/speed-frontend.md`: phases outside checking, root file lookups, sequential load and module resolution, checker creation and assignment;
- `notes/perf-frontend-64.md`: program construction scaling with threads, per-thread front-end statistics;
- `notes/perf-front-end-fixed-costs.md`: config, parallel parse, serial steps, statistics, process start and exit;
- `notes/perf-serial-load.md`: the sequential load per import edge;
- `notes/perf-resolver-allocations.md`, `notes/perf-parse.md`, `notes/perf-parse-heap-faults.md`, `notes/mem-frontend.md`;
- the code in `crates/tsrs_compiler/` (program, file loader), `crates/tsrs_module/` (resolver) and `crates/tsrs_binder/`.

## Goal

1. **Timeline first.**
   - Add a phase probe behind an environment variable, off by default. Use `NEVERSHARP_POOL_STATS` or a new `NEVERSHARP_PHASES=1`.
   - The probe prints the wall-time phases from process start to exit:
     - parse;
     - program walk;
     - resolution;
     - bind;
     - default library;
     - assignment;
     - checker construction (per checker: merge, duplicate-declaration errors);
     - check;
     - global diagnostics;
     - sort and output;
     - exit (time from the driver's last output to process end, measured from outside).
   - Report this table for vscode, sentry and bluesky with `--pool tsrs` (srv and ws) and with the default pool, next to tsrs's front end (tsrs-gap.md §3, or `--extendedDiagnostics` if the bench's tsrs driver offers it).
   - Order the steps by measured share, not by the list in goal 2.
2. **Shorten the largest serial or poorly parallel parts, one step at a time.** Expected candidates, to confirm or replace by the timeline:
   1. **Import resolution and binding in parallel:** find why they scale only 3.2×. Look at lock waits on the resolution cache, GC promotion of the parsed files, and work items that are too coarse or too fine. Port tsrs's shapes where they apply: speculative resolution during parsing, per-thread caches, string handling of `perf-resolver-allocations.md`.
   2. **The default library parsed and bound in parallel,** or overlapped with the program walk. Single-threaded output must stay identical; if `Program.Create` needs to know about `--single`, a parameter that defaults to today's behaviour is allowed.
   3. **Checker construction:**
      - per-checker work that does not need the checker's own state;
      - the duplicate-declaration errors of other checkers' files that are never read (create them lazily, or only for the checker's own files when it keeps only its own files' diagnostics);
      - the globals merge.
      Creation order of types and symbols per checker must stay what it is for every diagnostic that is reported. If a change would alter a reported diagnostic, leave it out and report it.
   4. **The tail after the check:**
      - global diagnostics, sorting and output;
      - process exit: no finalizers or teardown work at exit; leave the process as tsrs does (`perf-front-end-fixed-costs.md`).
   5. **Parse:** only if the timeline shows it scaling poorly.
3. **Re-measure at the end:** the timeline again, and the factor table against tsrs and typescript-go.

**Every ported structure:**
- names its tsrs source (file and function, or note) in the code comment;
- marks itself as Track O / tsrs-only where it has no counterpart in TypeScript 6.0.3.

**Hard constraints:**
- Single-threaded check and emit output stays identical with tsc 6.0.3 (Gate C `--sample 200` after each code step).
- **With the default pool,** the parallel `--dump-diagnostics` stays identical with the starting driver's on sentry, bluesky and tuning-check.
- **With `--pool tsrs` under Workstation GC** (deterministic on vscode and sentry, SC1), `--dump-diagnostics` stays identical with the starting driver's on vscode and sentry.
- The `--listFiles` order, the resolution results and the module and file order of the program do not change.
- Nothing is shared between checkers that was not shared before. Data shared before the check (parsed files, binder data, resolution cache) may be built differently, as long as the results are the same.

## Fast loop

**Workloads** (each run 30 s or less), all `--check`:
- **main:** vscode and sentry with `--pool tsrs` (aot-srv and aot-ws);
- **guard:** bluesky, playwright and tldraw with `--pool tsrs` srv; vscode and sentry with the default pool (aot-ws); bluesky `--single`;
- **for comparison:** `tsrs-check` and `tsgo7-check` in the same rounds, for the factor. Use the bench's commands (`bench.mjs --dry-run --configs tsrs-check`).

**Measuring:** interleaved with SC1's per-variant script `results/phase3/SC1/run1/inter.mjs` and `cmp.mjs`, previous step against this step, 5–7 rounds after 1 warm-up round, with `harness/bench/stats.mjs bootstrapMedianRatio`.
- **Keep a step** if the main workloads are faster or equal by the median, and none is slower by the 95 % interval.
- **Reject it otherwise.**
- **Steps within noise** may be kept if they remove measured serial time (from the phase probe) and the median is not worse.

**Per step:**
1. Build aot-srv and aot-ws, with `WarningsNotAsErrors=NU1903` in the environment.
2. Run the dump checks of the hard constraints (seconds each).
3. Measure.
4. Commit each kept step at once.

| Gate | Run limit |
|---|---:|
| Gate C (`--sample 200` only) | 10 |

No full gates, by the author's decision.

## What may change

- `src/NeverSharp.Optimized/`: program, resolution, host, parser and binder entry points, pool and checker construction, where a step needs it.
- `tools/NeverSharp.ParseDriver/`: the phase probe (off by default) and the exit path.

Must not change:
- Track F;
- `eng/`;
- the oracles, the gate harnesses, `bench.mjs` and `report.mjs`;
- the file lists;
- `docs/preregistration.md`;
- `external/` (read only);
- the default behaviour of the driver's output.

## Working

- **Who:** one attempt with Opus 5.5 at medium effort, no sub-agents.
- **Where:** directly on `main`. Scratch: `<session scratch>/FE1/`. Build the starting drivers (aot-ws, aot-srv) into `<session scratch>/FE1/base/` first.
- **Editing C#:** with the file tools only (Edit/Write). No sed, no Python or other scripts on any source file, comments included.
- **Tools:** no downloads or installs.
- **Other processes:** never name, inspect or stop them. If the machine is busy, wait.
- **Commits:** `git commit -s` per kept step; do not push.
- **Size:** up to 8 kept steps, or about 5 hours. Stop earlier when the parts outside the check are at tsrs's level, or when three candidates in a row fail.
- **Before the final report:** stop every background task you started, and leave the tree clean.
- **Docs at the end:** an FE1 section in [phase1-progress.md](../phase1-progress.md), with the timeline before and after.

## Report

- **The timeline before the first step** (goal 1), with tsrs's counterpart per phase.
- **Each step:** what was serial or slow, the tsrs source, what was changed, the ratios with intervals against the previous step, and the commit.
- **Base against final:** the main and guard workloads, interleaved, 7 rounds, with the factor against tsrs and typescript-go per repository.
- **The timeline after the last step,** and what is left outside the check.
- What was tried and rejected.
- The Gate C samples used, and the dump checks.
- The commit hashes.

## Measurement (orchestrator)

- The five repositories, interleaved, 5 rounds: base and final drivers with `--pool tsrs` (srv, ws), the default pool (ws), `tsrs-check` and `tsgo7-check`.
- Reported as the factor against tsrs per repository, as in `results/phase3/SC1/run1/NOTE.md`.
