# MM1: the checker's memory model as tsrs keeps it — brief

Track O. **Author's decision of 2026-11-06:** after PC1, rework the checker's memory model. Types, symbols and signatures, and the data that hangs off them, become records in native per-checker arenas without managed references, as tsrs keeps them. A probe step first estimates the gain.

The author's standing direction holds:
- optimize now, conformity at the end;
- workloads of 30 s or less;
- many short steps rather than long runs;
- no full gates for now (a small Gate C sample suffices);
- report results as the factor against tsrs per repository.

Written by the orchestrator. The standing rules of [README.md](README.md) apply, except where this brief relaxes them.

## Why

After AR2 and PC1, the factor against tsrs (geometric mean over the five repositories, `results/phase3/AR2/run1/`; PC1 changed it within noise):
- **1.89** with `--pool tsrs` under Server GC (16 checkers);
- **2.35** under Workstation GC (12 checkers).

PC1 ([phase1-progress.md](../phase1-progress.md), PC1) found:
- **No call-count divergence:** Track O does the same work as tsrs, and every shared count is within 0.9–1.1.
- **No tsrs instruction cut above 1 %** that is also exact for TypeScript 6.0.3.
- **The parallel loss:**
  - at 16 checkers (Server GC), Track O is 13–21 % slower per type created than single-threaded, while tsrs stays nearly flat;
  - the Server GC pause stops all 16 checkers and takes 12–20 % of the summed checker time;
  - the rest is likely memory traffic.

AR1 and AR2 moved link stores, signature parameters, inference infos and candidates into the native `CheckerArena`, and cut closures and transient strings. Two lessons from them:
- Moving large reference-free arrays out of the collected heap did not lower the pause (AR1 step 2, AR2 steps 2 and 7).
- What a collection costs is:
  - the objects with references it marks and copies;
  - the cards it scans: new data stored into long-lived stores;
  - the fixed part per collection.

**What survives with references** (AR1's census at the end of the check, vscode):
- 1.59M strings: literal values and the name tables;
- 325K inference contexts;
- 103K diagnostics;
- `Type[]` and `List<Type>`: base types and others;
- the instantiable kinds' data classes behind `TypeStore.instantiables` (an `object[]`): `IndexedAccessData`, `IndexData`, `TemplateLiteralData`, `StringMappingData`, `SubstitutionData`, `ConditionalRoot`, `ConditionalData`;
- `SignatureRecord.CompositeSignatures`, `IndexInfo.Components`, `LiteralData`;
- the transient symbol and object type chunks, through `SymTable`'s store field;
- the per-file and per-checker dictionaries and caches.

**tsrs** keeps all checker data in per-thread arenas with 32-bit handles; it is never scanned or copied. See:
- `docs/CHECKER.md` ("Types": one header with the kind's payload behind it);
- `docs/RUST.md`;
- `notes/mem-checker-gc.md`, `mem-checker-heap.md`, `mem-layout3.md`, `mem-pointer-compression.md`, `mem-flat-symbol-tables.md`, `mem-dense-link-tables.md`, `mem-census.md`, `mem-round2.md`, `mem-round3.md`.

## Starting point

`main` at this brief's parent (a0da6c6 + this brief).
- **Checker storage:**
  - `src/NeverSharp.Optimized/Checker/`: `TypeStore.cs`, `Handles.cs`, `ItemLists.cs`, `Links.cs`, `SymTable.cs`, the mapper store in `Checker.Instantiate.cs`;
  - `docs/phase3-checker-layout.md`.
- **Arenas:** `src/NeverSharp.Optimized/Arena/` (`CheckerArena.cs`, `Slab`, `NodeStore`, `Text`).
- **The aot-ws build carries a gen0 budget of 32 MB** (AR2 step 0). For JIT runs that must match it, set `DOTNET_GCgen0size=2000000`. `DOTNET_GCgen0size` also works on the AOT drivers (AR2).
- **Tools from earlier modules,** in the session scratch; reuse them, do not modify them:
  - AR1's GC probe (`AR1/ArProbe.cs.txt`, `probe-hooks.patch`), `AR1/acc` (allocation and promotion accounts), `AR1/heapclass.mjs`;
  - AR2's and PC1's measurement scripts;
  - PC1's per-checker time and parallel-loss scripts (`PC1/ploss*`).

## Goal

### Step 0: the probe, no committed code

Estimate what the rework can gain at most, before building it.

1. **Upper bound without collection.** Run the drivers with the collector practically off for the check, using environment settings only:
   - a gen0 budget far above the run's allocation;
   - or `GC.TryStartNoGCRegion` in a scratch driver.

   Workloads: sentry and vscode `--check --pool tsrs` with Server GC and with Workstation GC, plus bluesky and playwright `--single`. Report:
   - wall time and the change against the normal run;
   - the per-checker time and the parallel loss (PC1's method).

   The difference is the most that removing the collector's work can gain. It does not include the memory-traffic part.
2. **The census of what a collection marks.** At the end of the check, and per collection in the check, on vscode and sentry:
   - objects with references, by type, count and bytes;
   - the cards scanned;
   - the share of each group in the mark time.

   Use AR1's tools.
3. **The memory-traffic part.** With the collector off (item 1), compare the per-type cost at 16 checkers against single-threaded. Whatever loss remains is memory traffic and layout, the part that dense records (one access per type read, tsrs's header and payload) would address.
4. **Plan the steps** by the measured share of each group, and estimate the gain per step.

**Stop rule after step 0:** if the upper bound (item 1) is below 8 % on both sentry and vscode with Server GC, and the memory-traffic loss (item 3) is below 8 %, stop. Report step 0 to the author instead of building.

### Steps 1 and later: one group per step, ordered by step 0

Expected candidates, to confirm or replace by step 0:
1. **The instantiable kinds' data classes** (`TypeStore.instantiables`) as struct records in arena chunks. Their type references become handles.
2. **Strings** (literal values, name tables, string-keyed literal caches) as ids or ranges of a per-checker text arena (`Arena/Text.cs`), with string-free comparison and hashing where the readers only compare or look up. The first readers that need a `string` get it at the edge (diagnostic text, emit).
3. **Inference contexts** as arena records:
   - the mapper store's object table holds handles;
   - the comparer becomes an index;
   - contexts that did not escape are recycled, as tsrs does (`mem-checker-scratch.md`).
4. **Type lists** (`Type[]`, `List<Type>` for base types and others) as ranges of an append-only list arena (as AR1 step 3 did for signature parameters).
5. **The transient symbol and object type chunks:** remove the managed references (`SymTable`'s store field and others), so the chunks become native or reference-free; then put them in the arena.
6. **The remaining records with references:** `CompositeSignatures`, `IndexInfo.Components`, `LiteralData`, the per-file dictionaries and caches.
7. **Dense type records** (one access for the hot reads): only if step 0 shows a memory-traffic share. Model it on tsrs's header and payload, for the most-read kinds first (object types, unions).

**Every ported structure:**
- names its tsrs source (file and type, or note) in the code comment;
- marks itself as Track O / tsrs-only where it has no counterpart in TypeScript 6.0.3.

**Hard constraints:**
- Single-threaded check and emit output stays identical with tsc 6.0.3 (Gate C `--sample 200` after each code step).
- Type, symbol and signature creation order per checker stays tsc's, and ids stay what they are.
- **With the default pool,** the parallel `--dump-diagnostics` stays identical with the starting driver's on sentry, bluesky and tuning-check.
- **With `--pool tsrs` under Workstation GC,** `--dump-diagnostics` stays identical on vscode and sentry. Retry a differing dump up to twice (rare stealing difference, AR2).
- `--dump-emit` at the default pool stays identical on playwright and tldraw.
- Nothing is shared between checkers that was not shared before.
- **Memory safety:**
  - no use after free;
  - every native block has one owner, and the owner frees it;
  - the emitter and the declaration printer read checker data after the check, so the arenas live as long as the checker (as in AR1).
- **Unsafe code** is allowed where the parser's arenas and `CheckerArena` already use it.

## Fast loop

**Workloads** (each run 30 s or less):
- **main:** sentry and vscode `--check --pool tsrs`, under Server GC (aot-srv) and under Workstation GC (aot-ws);
- **guard:**
  - bluesky and playwright `--check --single` (aot-ws);
  - tldraw `--check --pool tsrs` (aot-srv);
  - vscode `--check` with the default pool (aot-ws);
  - playwright `--check --emit --single` after every few steps;
- **for comparison:** `tsrs-check` and `tsgo7-check` in the same rounds.

**Measuring:** interleaved with `results/phase3/SC1/run1/inter.mjs` and `cmp.mjs`, previous step against this step, 5–7 rounds after 1 warm-up round, with `harness/bench/stats.mjs bootstrapMedianRatio`.
- **Keep a step** if the main workloads are faster or equal by the median, and no main or guard workload is slower by the 95 % interval.
- **Code-placement exception** (as in PC1): if only a single-threaded AOT workload is slower by the interval, and the JIT drivers at full optimization (`DOTNET_TieredCompilation=0`, 8 rounds) show no slowdown, the step may be kept. Disclose each such case.
- **A step within noise** may be kept if it removes measured mark time, surviving objects with references or pause, and the median is not worse. Some groups are small alone but are a precondition for a later step (for example, removing the last reference from a chunk). Such a step may be kept within noise if it says which later step it enables.
- **Reject it otherwise.**
- **Report** the pause, the gen0/gen1 collection counts and the peak working set of every variant.

**Per step:**
1. Build aot-ws and aot-srv, with `WarningsNotAsErrors=NU1903` in the environment.
2. Run the dump checks of the hard constraints (seconds each).
3. Measure.
4. Commit each kept step at once.

| Gate | Run limit |
|---|---:|
| Gate C (`--sample 200` only) | 15 |

No full gates, by the author's decision.

## What may change

- `src/NeverSharp.Optimized/Checker/` and `src/NeverSharp.Optimized/Arena/`;
- shared Track O types the checker, the emitter and the declaration printer use, where a step needs it;
- `tools/NeverSharp.ParseDriver/`: probes behind environment variables, off by default.

Must not change:
- Track F;
- `eng/`;
- the oracles, the gate harnesses, `bench.mjs` and `report.mjs`;
- the file lists;
- `docs/preregistration.md`;
- `external/` (read only).

## Working

- **Who:** one attempt with Opus 5.5 at medium effort, no sub-agents.
- **Where:** directly on `main`. Scratch: `<session scratch>/MM1/`. Build the starting drivers (aot-ws, aot-srv, jit-ws) into `<session scratch>/MM1/base/` first.
- **Editing C#:** with the file tools only (Edit/Write). No sed, no Python or other scripts on any source file, comments included.
- **Tools:** no downloads or installs.
- **Other processes:** never name, list, inspect or stop them, not even filtered (no `tasklist`, `Get-Process` or similar). Check that your own runs ended through the tools that started them. If the machine is busy, wait.
- **Commits:** `git commit -s` per kept step; do not push.
- **Size:** up to 10 kept steps after step 0, or about 8 hours. Stop earlier:
  - by step 0's stop rule;
  - or when three candidates in a row fail.
- **Before the final report:** stop every background task you started, and leave the tree clean.
- **Docs at the end:** an MM1 section in [phase1-progress.md](../phase1-progress.md), with:
  - step 0 (the upper bound, the census and the memory-traffic part);
  - one table row per step;
  - the census after the last step.

## Report

- **Step 0:**
  - the upper bound without collection, per workload and GC mode;
  - the census of what a collection marks;
  - the memory-traffic part;
  - the step plan with estimated gains.
- **Each step:**
  - the group moved and its share;
  - the tsrs source;
  - what was changed;
  - the ratios with intervals against the previous step;
  - the pause, the collections and the peak working set;
  - the commit.
- **Base against final:** the main and guard workloads, interleaved, 7 rounds, with the factor against tsrs and typescript-go per repository, and the pause.
- **The census after the last step,** and what is left.
- What was tried and rejected.
- The Gate C samples used, and the dump checks.
- The commit hashes.
- Every rule bent.

## Measurement (orchestrator)

- The five repositories, interleaved, 5 rounds:
  - base and final drivers with `--pool tsrs` (srv, ws);
  - the default pool (ws);
  - `--single` (ws);
  - `tsrs-check` and `tsgo7-check`.
- Reported as the factor against tsrs per repository, as in `results/phase3/AR2/run1/NOTE.md`.
