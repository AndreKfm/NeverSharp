# SC1: tsrs's work distribution over the checkers — brief

Track O. **Author's decision of 2026-11-05:** take over tsrs's work distribution. That covers more checkers, files assigned by directory locality, and work stealing. The parallel diagnostics may then differ from today's driver; single-threaded output stays identical with tsc 6.0.3.

The author's standing direction holds:
- optimize now, conformity at the end;
- workloads of 30 s or less;
- many short steps rather than long runs;
- no full gates for now (a small Gate C sample suffices).

Written by the orchestrator. The standing rules of [README.md](README.md) apply, except where this brief relaxes them.

## Why

The gap to tsrs is about 4.7× (quick estimate `results/estimates/2026-11-05T03-07-28-698Z/`). [tsrs-gap.md](../tsrs-gap.md) §3 splits vscode's 6.3× into these factors:
- Workstation GC pause 1.26×;
- per-checker speed 1.97×;
- checker count 1.76× (tsrs 16, Track O 4);
- locality assignment 1.22×;
- work stealing 1.18×.

The last three, about 2.5× together, are scheduling. Under Track O's pool conditions (4 checkers, *i* mod 4, no stealing), tsrs scales like Track O with Server GC. Track O does not gain from 16 checkers over 12 with Workstation GC, because the pause grows with the number of checkers (CN1, TG1 rank 8). So more checkers need Server GC, or less pause.

## Starting point

`main` at this brief's parent, which includes RS2's final commits.
- Track O's pool is in `src/NeverSharp.Optimized/` (`CheckerPool`; files go to checker *i* mod N; `--checkers N` in the driver).
- The driver is `tools/NeverSharp.ParseDriver/Program.cs`.

**tsrs** is read-only at `external/tsrs/`:
- `crates/tsrs_compiler/src/checkerpool.rs`: association policy and order, base weights, `checked_file_weights`, stealing queues;
- the locality assignment and `affinity.rs`;
- notes:
  - `mem-assignment.md`: the locality assignment, directory subtrees placed with FENNEL;
  - `perf-clustered-assignment.md`;
  - `perf-checker-stealing.md`: tail stealing, one `AtomicU64` per queue;
  - `perf-balance.md`;
  - `perf-checker-scaling.md`;
  - `perf-default-checkers-16.md`;
  - `perf-order-independence.md`: tsrs's canonical caches make the output independent of the assignment. Track O does not have them.

## Goal

1. **Locality assignment.**
   - Port tsrs's assignment: directory-subtree groups placed over the checkers with FENNEL over the import edges, with tsrs's weights.
   - Make it a pool option, with a driver switch `--assignment locality`. The default stays *i* mod N for now.
   - It is deterministic, so a run's output does not depend on timing.
2. **Checker count.** Measure 4, 8, 12 and 16 checkers with the locality assignment, under both Workstation and Server GC (the existing `aot-srv` build).
   - Report where the curve flattens and why: pause, duplicated work, or imbalance.
3. **Work stealing.**
   - Port tsrs's tail stealing: each checker takes its own files from the front of its queue; an idle checker takes not-yet-started files from the back of the queue with the most estimated work left. Make it a pool option, with a driver switch `--steal`.
   - Track O's output depends on the assignment, so with stealing the parallel diagnostics may vary from run to run. Measure that: the number of distinct `--dump-diagnostics` outputs over 5 runs on vscode, sentry and tuning-check, and the number of differing lines.
   - Report it plainly. Keep the option even if it is not deterministic; the author decides later whether it may be a default.
4. **Less pause at many checkers**, if Workstation GC stays the limit. Only local changes that keep single-threaded output identical, for example less duplicated per-checker work through locality, or scratch reused instead of retained. Report what limits it.
5. **A "tsrs-like" configuration.**
   - Combine the best settings into one driver switch, for example `--pool tsrs` for locality + N checkers + stealing.
   - Do this for both builds (ws, srv), so the orchestrator can measure it against `tsrs-check` and `tsgo7-check`.

**Hard constraints:**
- Single-threaded check and emit output stay identical with tsc 6.0.3 (Gate C `--sample 200` after each code step).
- With the default pool settings (no new switch), the parallel diagnostics stay identical with the baseline driver's. The new switches are off by default.
- Every diagnostic of a parallel run with the new settings must be a correct diagnostic of the program: report how its set differs from the single-threaded set, as is already the case for *i* mod N.
- Nothing is shared between checkers that was not shared before.

## Fast loop

**Workloads** (each run 30 s or less):
- vscode `--check` (about 10–14 s), sentry `--check` (about 3.6 s), tuning-check `--check` (about 13 s);
- for comparison: `tsrs-check` (`artifacts/bin/` tsrs driver as the bench uses it; `bench.mjs --dry-run --configs tsrs-check` prints the command) and `tsgo7-check`, interleaved in the same rounds.

**Per step:**
1. Build aot-ws (and aot-srv when needed), with `WarningsNotAsErrors=NU1903` in the environment.
2. Check that the default settings keep the parallel `--dump-diagnostics` identical on sentry and tuning-check.
3. Measure interleaved (`results/phase4/EO1/run1/follow-up/interleave.mjs`, 5–7 rounds), with `stats.mjs bootstrapMedianRatio`.
4. Commit each kept step.

**Also report per checker:** CPU or wall time per checker (slowest against mean), types created per checker (duplication), and the GC pause. tsrs's `TSRS_ASSIGNMENT_STATS=times` gives the same for tsrs.

| Gate | Run limit |
|---|---:|
| Gate C (`--sample 200` only) | 10 |

No full gates, by the author's decision.

## What may change

- `src/NeverSharp.Optimized/`: the pool, the program, and the checker where a step needs it.
- `tools/NeverSharp.ParseDriver/`: new switches, off by default, and the per-checker statistics behind an environment variable.

Must not change:
- Track F;
- `eng/`;
- the oracles, the gate harnesses, `bench.mjs` and `report.mjs`;
- the file lists;
- `docs/preregistration.md`;
- `external/` (read only).

## Working

- **Who:** one attempt with Opus 5.5 at medium effort, no sub-agents.
- **Where:** directly on `main`. Scratch: `<session scratch>/SC1/`. Build the starting drivers (aot-ws, aot-srv) into `<session scratch>/SC1/base/` first.
- **Editing C#:** with the file tools only (Edit/Write).
- **Tools:** no downloads or installs.
- **Other processes:** never name, inspect or stop them. If the machine is busy, wait.
- **Commits:** `git commit -s` per kept step; do not push.
- **Size:** about 6 hours. Stop earlier if the tsrs-like configuration is within reach of tsrs's numbers, or a limit is clearly identified.
- **Before the final report:** stop background tasks and leave the tree clean.
- **Docs at the end:** an SC1 section in [phase1-progress.md](../phase1-progress.md).

## Report

- **Each step:** design, tsrs source, and measurements against the previous step and against `tsrs-check` and `tsgo7-check`.
- **The checker-count curve** (4/8/12/16 × ws/srv, locality on), with pause, duplication and imbalance.
- **Stealing:** the gain, and the run-to-run variation of the parallel diagnostics.
- **The tsrs-like configuration:** its switch and its numbers on vscode, sentry and tuning-check against tsrs and typescript-go.
- **What limits it now.**
- The Gate C samples, and the parallel default-settings dump checks.
- The commit hashes.
