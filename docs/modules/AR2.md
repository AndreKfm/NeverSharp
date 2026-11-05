# AR2: a larger gen0 budget, and less short-lived garbage in the check — brief

Track O. **Author's decisions of 2026-11-05**, after AR1:
1. Raise the GC gen0 budget in the builds. This is a build/runtime setting and an amendment of pre-registration §6.4 (A7); the author approved it. The amendment text goes into the pre-registration at the end, with the other conformity work.
2. Cut the short-lived (young) allocations of the check.

The author's standing direction holds:
- optimize now, conformity at the end;
- workloads of 30 s or less;
- many short steps rather than long runs;
- no full gates for now (a small Gate C sample suffices);
- report results as the factor against tsrs per repository.

Written by the orchestrator. The standing rules of [README.md](README.md) apply, except where this brief relaxes them.

## Why

After AR1, the factor against tsrs (geometric mean over the five repositories, `results/phase3/AR1/run1/`) is:
- **1.93** with `--pool tsrs` under Server GC (16 checkers);
- **2.57** under Workstation GC (12 checkers); vscode ws 3.16, with 2.91 s of 7.1 s in GC pauses.

AR1's census after its last step ([phase1-progress.md](../phase1-progress.md), AR1, "What is left"):
- **The pause follows the number of collections and their fixed part.** On vscode ws the check has 519 gen0 and 209 gen1 collections. A gen0 collection costs about 3 ms with 12 threads: suspension and the stack roots about 0.7 ms, card marking 0.8 ms, plan, relocate and compact about 1.6 ms. The number of collections follows the small-object allocation volume: 6.4 GB on vscode in the check, including:
  - strings from `TextTable.GetString`, about 550 MB;
  - closures and delegates, about 1 GB;
  - call arguments and relater workers.
- **GC setting experiment** (JIT ws, vscode `--pool tsrs`; the AOT drivers ignore the GC environment variables):

  | setting | pause | gen0/gen1 collections |
  |---|---|---|
  | default | 3.16 s | 857/250 |
  | `GCgen0size` 64 MB | 2.80 s (−11 %) | 113/60 |
  | `GCgen0size` 256 MB | 2.68 s (−15 %) | 61/59 |

- **RS2's saved allocation-cut patch** (`<session scratch>/RS2/alloc-cuts-wip.patch`, never committed), applied to a scratch tree at AR1 step 1: gen0 817 → 683 collections, pause 3.45 → 3.24 s on vscode ws.
- **Survivors with references:**
  - 1.59M strings (string literal values, the name tables);
  - 325K inference contexts;
  - the transient symbol and object type chunks (through `SymTable`'s store field);
  - `SignatureRecord.CompositeSignatures`, the instantiable kinds' data classes, `IndexInfo.Components`, `LiteralData`.

**tsrs:**
- allocates checker data in per-thread arenas and recycles scratch (`notes/mem-checker-scratch.md`, `free_type_lists`, contexts that did not escape);
- keeps texts in its arenas (`notes/mem-checker-heap.md`);
- has no closures on hot paths (RS1 ported its struct callbacks for the relater).

## Starting point

`main` at this brief's parent (49bdcd3 + this brief), which includes SC1, FE1 and AR1.
- **Build configurations:** `eng/RuntimeConfiguration.props`.
- **Checker:** `src/NeverSharp.Optimized/Checker/`, `src/NeverSharp.Optimized/Arena/` (`CheckerArena.cs`).
- **Driver:** `tools/NeverSharp.ParseDriver/`; its JSON line reports `gcPauseMs`.
- **Tools from AR1** (session scratch `AR1/`), reuse them, do not modify them:
  - the GC probe `AR_PROBE` (`ArProbe.cs.txt`, `probe-hooks.patch`);
  - `acc/` (allocation and promotion accounts from JIT traces);
  - `heapclass.mjs`;
  - `measure.sh`, `dumpcheck.sh`, `factor.mjs`.

**tsrs** is read-only at `external/tsrs/`. Read these first:
- `notes/mem-checker-scratch.md`;
- `notes/mem-checker-heap.md`;
- `notes/mem-checker-gc.md`;
- RS1's ports of the struct callbacks (phase1-progress.md, RS1) as the model for closure removal.

## Goal

### Step 0: the gen0 budget in the builds

1. **Find how the AOT drivers take the setting.** NativeAOT ignores the `DOTNET_GCgen0size` environment variable here (AR1). Candidates:
   - a `RuntimeHostConfigurationOption` item (for example `System.GC.Gen0Size`, or the knob's private name), which ILC embeds into the binary;
   - an ILC runtime-knob argument.

   Prove that it takes effect: the gen0 collection count in the check must drop as in the JIT experiment.
2. **Choose the value by measurement:** 64, 128 and 256 MB, interleaved, on the main and guard workloads.
   - Report:
     - wall time and pause;
     - collections;
     - peak working set (a pre-registered metric).
   - **Server GC:** decide separately whether it gets the setting. Its per-heap budgets are already large; give it the setting only if it helps.
   - **Single-threaded runs:** must not get slower by the interval.
3. **Commit the chosen setting in `eng/RuntimeConfiguration.props`** for the configurations it helps.
   - Use the same knob in the JIT configuration of the same GC mode, so AOT and JIT stay comparable.
   - Add a comment there: an amendment of §6.4 A7, decided by the author 2026-11-05, to be recorded in the pre-registration at the end.
   - Do not change `docs/preregistration.md` now.
4. **Re-baseline.** All later steps are measured with the new setting; re-take AR1's census (pause by phase, promoted bytes and survivors, allocation volume) with it before step 1. With far fewer collections, the pause should follow what each collection marks and copies, so the order of the later steps may change.

### Steps 1 and later: less young garbage, one group per step, ordered by the census after step 0

Expected candidates, to confirm or replace by the census:
1. **RS2's allocation-cut patch.** Port its changes onto today's tree as separate small steps where they still apply. The patch is a work in progress: check each change's exactness.
2. **Closures and delegates on hot paths** (about 1 GB on vscode): struct callbacks or static lambdas with explicit state, as RS1 did for the relater.
3. **Strings from `TextTable.GetString`** (about 550 MB) and other transient strings: compare and hash text ranges without making a `string`, where the reader only compares or looks up.
4. **Scratch reused instead of allocated:** call argument arrays, relater workers, temporary lists. tsrs's `free_type_lists` and its recycled inference contexts (`mem-checker-scratch.md`) are the model. Only where the object provably does not escape.
5. **Surviving strings and inference contexts:** only if the census after step 0 shows them as a large share of the pause:
   - string literal values and name tables as text ranges or ids;
   - inference contexts as arena records, or recycled.

**Every ported structure:**
- names its tsrs source (file and type, or note) in the code comment;
- marks itself as Track O / tsrs-only where it has no counterpart in TypeScript 6.0.3.

**Hard constraints:**
- Single-threaded check and emit output stays identical with tsc 6.0.3 (Gate C `--sample 200` after each code step).
- Type, symbol and signature creation order per checker stays tsc's, and ids stay what they are.
- **With the default pool,** the parallel `--dump-diagnostics` stays identical with the starting driver's on sentry, bluesky and tuning-check.
- **With `--pool tsrs` under Workstation GC,** `--dump-diagnostics` stays identical on vscode and sentry.
- `--dump-emit` at the default pool stays identical on playwright and tldraw.
- Nothing is shared between checkers that was not shared before.
- No use after free: scratch handed back must not be read again. Every native block has one owner, and the owner frees it.

## Fast loop

**Workloads** (each run 30 s or less):
- **main:** vscode and sentry `--check --pool tsrs` under Workstation GC (aot-ws);
- **guard:**
  - the same under Server GC (aot-srv);
  - bluesky and playwright `--check --single` (aot-ws);
  - vscode `--check` with the default pool (aot-ws);
  - playwright `--check --emit --single` after every few steps;
- **for comparison:** `tsrs-check` and `tsgo7-check` in the same rounds.

**Measuring:** interleaved with `results/phase3/SC1/run1/inter.mjs` and `cmp.mjs`, previous step against this step, 5–7 rounds after 1 warm-up round, with `harness/bench/stats.mjs bootstrapMedianRatio`.
- **Keep a step** if the main workloads are faster or equal by the median, and no main or guard workload is slower by the 95 % interval.
- **Reject it otherwise.**
- **A step within noise** may be kept if it removes measured allocation volume or pause (from the census or a probe) and the median is not worse.
- **Report** the pause and the gen0/gen1 collection counts of every variant.

**Per step:**
1. Build aot-ws and aot-srv, with `WarningsNotAsErrors=NU1903` in the environment.
2. Run the dump checks of the hard constraints (seconds each).
3. Measure.
4. Commit each kept step at once.

| Gate | Run limit |
|---|---:|
| Gate C (`--sample 200` only) | 12 |

No full gates, by the author's decision.

## What may change

- `eng/RuntimeConfiguration.props`: step 0 only, the gen0 budget setting with its comment. Nothing else in `eng/`.
- `src/NeverSharp.Optimized/Checker/`, `src/NeverSharp.Optimized/Arena/`.
- Shared Track O types the checker uses, where a step needs it.
- `tools/NeverSharp.ParseDriver/`: probes behind environment variables, off by default.

Must not change:
- Track F;
- the GC mode and every other build or runtime setting;
- the oracles, the gate harnesses, `bench.mjs` and `report.mjs`;
- the file lists;
- `docs/preregistration.md`;
- `external/` (read only).

## Working

- **Who:** one attempt with Opus 5.5 at medium effort, no sub-agents.
- **Where:** directly on `main`. Scratch: `<session scratch>/AR2/`. Build the starting drivers (aot-ws, aot-srv, jit-ws) into `<session scratch>/AR2/base/` first.
- **Editing C#:** with the file tools only (Edit/Write). No sed, no Python or other scripts on any source file, comments included. Applying RS2's patch with `git apply` is not allowed on the repository; port its changes with the file tools.
- **Tools:** no downloads or installs.
- **Other processes:** never name, inspect or stop them. If the machine is busy, wait.
- **Commits:** `git commit -s` per kept step; do not push.
- **Size:** up to 8 kept steps after step 0, or about 6 hours. Stop earlier when three candidates in a row fail.
- **Before the final report:** stop every background task you started, and leave the tree clean.
- **Docs at the end:** an AR2 section in [phase1-progress.md](../phase1-progress.md), with:
  - step 0's measurements;
  - the census before step 1 and after the last step.

## Report

- **Step 0:**
  - how the setting reaches the AOT binary;
  - the measured values;
  - the chosen setting per configuration, with wall, pause, collections and peak working set;
  - the commit.
- **The census after step 0,** with tsrs's counterpart per group.
- **Each later step:**
  - the allocation group and its share;
  - the tsrs source;
  - what was changed;
  - the ratios with intervals against the previous step;
  - the pause and the collections;
  - the commit.
- **Base against final:** the main and guard workloads, interleaved, 7 rounds, with the factor against tsrs and typescript-go per repository, and the pause.
- **The census after the last step,** and what is left.
- What was tried and rejected.
- The Gate C samples used, and the dump checks.
- The commit hashes.

## Measurement (orchestrator)

- The five repositories, interleaved, 5 rounds:
  - base and final drivers with `--pool tsrs` (srv, ws);
  - the default pool (ws);
  - `tsrs-check` and `tsgo7-check`.
- Reported as the factor against tsrs per repository, as in `results/phase3/AR1/run1/NOTE.md`.
