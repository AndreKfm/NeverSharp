# RS2: fast loop on the check, where tsrs is faster — brief

Second step on the checker with tsrs as the model (Track O). It follows [RS1](RS1.md) (kept; `results/phase3/RS1/run1/`) and the quick estimate `results/estimates/2026-11-05T03-07-28-698Z/` (check 0.95, check12 1.20, emit 0.96 against typescript-go; tsrs still about 4.7× faster).

**Author's direction (2026-11-05):**
- Protocol conformity does not matter now; it comes at the end.
- Find the parts where tsrs is faster and optimize those parts in NeverSharp.
- Use workloads that run 30 s or less.
- Prefer many small, fast optimization steps over long measurement runs.

Track O only. Written by the orchestrator. The standing rules of [README.md](README.md) apply, except where this brief relaxes measurement.

## Starting point

`main` at this brief's parent (ffb9baf + this brief). Gate C and Gate D pass (50,177 each).

**Where tsrs is faster** (TG1, [tsrs-gap.md](../tsrs-gap.md) §2, single-threaded, before EO1, EO2 and RS1):

| repository | tsrs check (s) | Track O check (s) | ratio |
|---|---:|---:|---:|
| bluesky | 0.70 | 1.72 | 2.47 |
| playwright | 1.33 | 3.29 | 2.47 |
| tldraw | 1.98 | 3.18 | 1.61 |
| sentry | 5.46 | 7.92 | 1.45 |

- The front end (parse, resolve, bind) costs the same on both sides. The gap is the check, per unit of work: the same numbers of types and symbols are created.
- bluesky and playwright have the largest factor and are short. They are the main workloads of this loop.

**RS1's re-profile** (vscode and tuning-check single), with tsrs's counterparts:
- GC waits 10–11 %;
- the flow walk (`GetTypeAtFlowNode` 7 %, `GetTypeAtFlowAssignment` 4.3 %, `IsMatchingReference`, `ContainsMatchingReference`); tsrs has a tsrs-only flow memo here, which was not ported;
- `TableOf` 3 %;
- `ResolveNameHelper` 2.4 % exclusive, 9.6 % inclusive;
- `KeyMap.ClearScratch`, `MarkNodeAssignments`, `NameTable.Intern`, `SymbolNodeLinks.Get` (tsrs: `IdLinkStore` pages by vector), `SymTableStore.Find`;
- `ItemArena.Store` (the `getNamedMembers` copies), `CreateUnionOrIntersectionProperty`, `InferTypesFromTemplateLiteralType`.

**tsrs's output-preserving instruction cuts not ported yet** (TG1 rank 5): named-union membership by set, the union front cache with its exactness rules, the memo bits in `ObjectFlags`, the IA memo, the infer and flow memos. They are in `external/tsrs/notes/` (`perf-icount-hillclimb.md`, `perf-icount-round4.md`, `perf-checker-cpu3.md`, `perf-checker-algorithms.md`, `cpu-checker.md`), each with its exactness argument and, where tsrs has one, a shadow mode.

## Goal

Make the single-threaded check of bluesky and playwright faster, step by step, and the other workloads not slower.

**Each round:**
1. Profile a short workload (bluesky or playwright `--check --single`, jit-ws, `DOTNET_TieredCompilation=0`, `dotnet-trace --profile cpu-sampling`). Repeat the profile after every few kept steps.
2. Pick the hottest spot.
3. Read tsrs's code and notes for that spot, and port tsrs's structure or code shape together with its exactness argument. Where tsrs has a tsrs-only memo or cache for it, port that.
4. If tsrs has nothing better, a Track O-only change is allowed.

Small steps are wanted: one function, one cache or one representation at a time.

**Every ported structure:**
- names its tsrs source in the code comment;
- marks itself as Track O / tsrs-only where it has no counterpart in TypeScript 6.0.3.

**Hard constraint: results do not change.**
- Single-threaded check output stays identical with tsc 6.0.3. Gate C's diagnostics, types and symbols sections stay identical, so creation order per checker stays tsc's.
- The parallel check diagnostics at 4 checkers stay identical with the baseline driver's.
- Nothing is shared between checkers.
- Scheduling changes (locality assignment, work stealing, more checkers) are **not** part of this step. They change the parallel diagnostics; that is the author's decision later.

## Fast loop

**Workloads** (each run 30 s or less; the file lists are in `artifacts/bench/filelists/`):
- **main:** bluesky and playwright `--check --single`, and `--check` at 4 checkers;
- **guard:** tldraw and sentry `--check --single`, and tuning-check `--check` at 4 checkers (13 s);
- **emit guard:** playwright `--check --emit --single`, after every few steps.

**Per step:**
1. Build aot-ws only, with `WarningsNotAsErrors=NU1903` in the environment.
2. Correctness: `--dump-diagnostics`
   - single-threaded and at 4 checkers on bluesky, playwright and tldraw;
   - at 4 checkers on tuning-check.

   All must be identical with the baseline driver's. Seconds each.
3. Timing: interleaved with `results/phase4/EO1/run1/follow-up/interleave.mjs`, previous step against this step, 7 rounds after 1 warm-up round, on the main workloads.
4. Decide with `bootstrapMedianRatio` (`harness/bench/stats.mjs`).
   - Keep a step if the main workloads are faster or equal by the median and none is slower by the 95 % interval.
   - Reject it otherwise.
   - Small steps that are within noise may be kept if they remove work (count from a probe) and the median is not worse.
5. Commit each kept step at once.

**Every 3 kept steps:**
- Gate C `--sample 2000`;
- the guard workloads, interleaved, 5 rounds, base driver against the current one;
- the emit guard.

**At the end:**
- Gate C full and Gate D full;
- emit equivalence (`emit-equivalence.mjs --skip-tsgo --skip-tsrs`) on the tuning emit list and the five repositories;
- parallel dumps at 4 and 12 checkers (`--dump-diagnostics`) and at 4 (`--dump-emit`) on the five repositories, against the baseline driver.

| Gate | Run limit |
|---|---:|
| Gate C | 12 |
| Gate D | 3 |

**Size:**
- Up to 15 kept steps, or about 6 hours of work.
- Stop earlier when three candidates in a row fail or the remaining hot spots are each under 0.5 % of the check.
- No step may need a long measurement. If one seems to need one, report it instead.

## What may change

- `src/NeverSharp.Optimized/Checker/`.
- Shared Track O types the checker uses (`src/NeverSharp.Optimized/`), when a step needs it.

Must not change:
- Track F;
- `eng/`;
- the oracles, the gate harnesses, `bench.mjs` and `report.mjs`;
- the file lists;
- `docs/preregistration.md`;
- `external/` (read only);
- the committed driver behaviour. Measurement switches default to off and are not committed.

## Working

- **Who:** one attempt with Opus 5.5 at medium effort, no sub-agents.
- **Where:** directly on `main` in the main checkout. Scratch: `<session scratch>/RS2/`. Reuse RS1's tools (`<session scratch>/RS1/prof`, EA1's `acctool`), but do not modify them.
- **Before the first change:** build the starting drivers (aot-ws, jit-ws) into `<session scratch>/RS2/base/`.
- **Editing C#:** with the file tools only (Edit/Write). No sed, no Python or other scripts on C# files.
- **Tools:** no downloads or installs.
- **Other processes:** never name, inspect or stop them. If the machine is busy, wait.
- **Commits:** one commit per kept step with `git commit -s`; do not push.
- **Before the final report:** stop every background task you started, and leave the working tree clean.
- **Docs at the end:** an RS2 section in [phase1-progress.md](../phase1-progress.md), with one table row per step: the hot spot, its share, the tsrs source, and the ratio on bluesky and playwright single.

## Report

- **The list of steps,** kept and rejected. For each: the hot spot and its share, what tsrs does there, what was changed, the ratios with intervals, and the commit.
- **Base against final:** all main and guard workloads, 7 rounds interleaved.
- **The last profile:** the top ten, with tsrs's counterparts.
- The last summaries of Gate C and Gate D, the emit equivalence, and the parallel dump comparison.
- The gate runs used.

## Measurement (orchestrator)

- The quick estimate (5 runs, Workstation GC rows with the typescript-go and tsrs references, about 20 minutes) against `results/estimates/2026-11-05T03-07-28-698Z/`.
- Protocol runs only at the end of the optimization work, on the author's decision.
