# AR1: checker data in arenas, as tsrs keeps it — brief

Track O. **Author's decision of 2026-11-05:** after FE1, move the checker's surviving data out of the collector's reach, with tsrs's per-thread arenas as the model.

The author's standing direction holds:
- optimize now, conformity at the end;
- workloads of 30 s or less;
- many short steps rather than long runs;
- no full gates for now (a small Gate C sample suffices);
- report results as the factor against tsrs per repository.

Written by the orchestrator. The standing rules of [README.md](README.md) apply, except where this brief relaxes them.

## Why

After SC1 and FE1, the factor against tsrs (geometric mean over the five repositories, `results/phase3/FE1/run1/`) is:
- **2.04** with `--pool tsrs` under Server GC (16 checkers);
- **2.97** under Workstation GC (12 checkers).

What is left (FE1 progress entry, [tsrs-gap.md](../tsrs-gap.md) §3):
1. **Workstation GC pause:** vscode `--pool tsrs` ws spends 3.97 s of 8.31 s in GC pauses; srv spends 0.75 s of 5.12 s. The pause follows the data that survives a collection (CO5, CO6, RS1), not the number of collections.
2. **Per-checker speed:** about 1.7–2× tsrs's for the same work (the same numbers of types and symbols). Part of it is allocation and collection: allocation slow paths about 8 % and `PollGC` waits in the profiles of RS1 and RS2, and GC pause about 5 % even single-threaded.
3. **Process exit:** 0.36 s of kernel teardown of 6.6 GB on vscode srv. Less memory shortens it.

**tsrs has no collector.** Types, symbols, signatures, nodes and most checker state live in per-thread arenas that are never scanned and are freed in one piece (`external/tsrs/docs/RUST.md`, `docs/CHECKER.md`, the `notes/mem-*.md` series). Track O already does this for the parser and binder (`src/NeverSharp.Optimized/Arena/`: `Slab`, `NodeStore`, `Text`; native memory). In the checker it does it only partly:
- `TypeStore`, `SignatureStore`, `TransientSymbolStore`, `MapperStore` (CO6) and `Chunks<T>` are struct records in managed arrays;
- several records still hold managed references (strings, class-kind data, object tables);
- the chunks themselves are small-object arrays that the collector copies when it promotes them.

**CO6's account** of what survives with references (tc2500, 12 checkers, after CO6):
- the `object[]` tables (the instantiable kinds' data classes and the mapper store's object table);
- the inference contexts kept alive by the mapper store;
- `SignatureRecord[]` and the signature parameter arrays;
- the name tables' `string[]`;
- the type predicate cache;
- the expression links (`HashSet<string>`);
- the per-checker globals merge.

CO3 tried arenas for parameters and mappers; they cost more CPU than they saved on tuning-check. The difference now: the target is 12–16 checkers, where the pause is about half of the wall time, and the records have since become reference-free handles.

## Starting point

`main` at this brief's parent (f7d77ef + this brief), which includes SC1 and FE1.
- **Checker storage:** `src/NeverSharp.Optimized/Checker/` (`TypeStore.cs`, `Handles.cs`, `ItemLists.cs`, `Links.cs`, `SymTable.cs`, `Checker.Instantiate.cs` for the mapper store), and `docs/phase3-checker-layout.md`.
- **Arenas:** `src/NeverSharp.Optimized/Arena/`.
- **Driver probes:** `NEVERSHARP_PHASES=1` (FE1), `NEVERSHARP_POOL_STATS=1` (SC1). The driver's JSON line reports `gcPauseMs`.
- **Tools from earlier modules,** reuse them: CO5's GC probe and its promoted-bytes tool for JIT ws traces (phase1-progress.md, CO5 and CO6), and EA1's `acctool` in the session scratch.

**tsrs** is read-only at `external/tsrs/`. Read these first:
- `docs/RUST.md`, `docs/CHECKER.md` (the arena and handle model);
- `notes/mem-checker-heap.md` (what a checker holds outside the arena);
- `notes/mem-checker-scratch.md`;
- `notes/mem-dense-link-tables.md`;
- `notes/mem-flat-symbol-tables.md`;
- `notes/mem-per-checker-duplication.md`;
- `notes/mem-pointer-compression.md`;
- `notes/mem-checker-gc.md` (why tsrs does not collect checker data);
- `notes/fix-arena-recycle-uaf.md` (a lifetime bug to avoid).

## Goal

1. **Census first.**
   - Measure on vscode `--check --pool tsrs` under Workstation GC (12 checkers), and on sentry the same way:
     - the GC pause split into checker construction, check and the rest;
     - the bytes promoted per collection by type, with references and reference-free;
     - the survivors at the end of the check by type (count, bytes, whether they hold references).
   - Also measure the allocation rate per checker (bytes allocated per second of check), single-threaded on bluesky and playwright.
   - Map the largest groups to tsrs's counterpart: which tsrs arena or store holds the same data.
   - Order the steps by measured pause share, not by the list in goal 2.
2. **Move the largest surviving groups out of the collector's reach, one group per step.** Expected candidates, to confirm or replace by the census:
   1. **The record chunks themselves** (`Chunks<T>` of type, signature, transient symbol and mapper records, type lists, declaration lists): native memory per checker, as `Slab` does for the parser, or large reference-free arrays that the collector does not copy. Measure which of the two is cheaper.
   2. **Records that still hold references,** made reference-free:
      - strings as name ids or ranges of a per-checker text arena (`Arena/Text.cs`);
      - the instantiable kinds' data classes (`IndexedAccessData`, `IndexData`, `TemplateLiteralData`, `StringMappingData`, `SubstitutionData`, `ConditionalRoot`, `ConditionalData`) as struct records in chunks;
      - the signature parameter arrays as ranges of a symbol-list arena.
   3. **Inference contexts and the mapper store's object table:** records with handles, or scratch that is reused instead of kept, as tsrs does (`mem-checker-scratch.md`, `free_type_lists`).
   4. **Link stores and caches** (expression links, type predicate cache, relation and instantiation caches): dense per-checker tables keyed by id without managed objects per entry (`mem-dense-link-tables.md`).
   5. **Per-checker globals merge and its tables:** at 12–16 checkers each checker builds them again (CO6, FE1). Make the merged tables reference-free records. Sharing them between checkers is not part of this step.
3. **Lifetime:** a checker's arenas are freed when the program and its checkers are released.
   - Do it in one piece, as tsrs frees a thread's arenas, or leave it to process exit where the driver ends anyway.
   - No use after free: the emitter and the declaration printer read checker data after the check. Keep them working, and test `--check --emit` on playwright and tldraw.
4. **GC settings, as an experiment only.**
   - You may measure the gen0 budget (`GCgen0size`), non-concurrent Workstation GC or `GCConserveMemory` in scratch builds, and report what each would give.
   - Do not commit build or runtime-configuration changes: they change the registered builds, which is the author's decision.
5. **Re-measure at the end:** the census again, the pause, and the factor table against tsrs and typescript-go.

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
- No use after free. Unsafe code is allowed where the parser's arenas already use it. Every native block has one owner, and the owner frees it.

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
- **A step within noise** may be kept if it removes measured pause or promoted bytes (from the census) and the median is not worse.
- **Report the pause** of every variant (the driver's `gcPauseMs`).

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

- `src/NeverSharp.Optimized/Checker/` and `src/NeverSharp.Optimized/Arena/`;
- shared Track O types the checker uses, where a step needs it;
- `tools/NeverSharp.ParseDriver/`: probes behind environment variables, off by default.

Must not change:
- Track F;
- `eng/`;
- the build and runtime settings of the registered builds (GC mode, GC knobs);
- the oracles, the gate harnesses, `bench.mjs` and `report.mjs`;
- the file lists;
- `docs/preregistration.md`;
- `external/` (read only).

## Working

- **Who:** one attempt with Opus 5.5 at medium effort, no sub-agents.
- **Where:** directly on `main`. Scratch: `<session scratch>/AR1/`. Build the starting drivers (aot-ws, aot-srv, jit-ws) into `<session scratch>/AR1/base/` first.
- **Editing C#:** with the file tools only (Edit/Write). No sed, no Python or other scripts on any source file, comments included.
- **Tools:** no downloads or installs.
- **Other processes:** never name, inspect or stop them. If the machine is busy, wait.
- **Commits:** `git commit -s` per kept step; do not push.
- **Size:** up to 8 kept steps, or about 6 hours. Stop earlier when the Workstation GC pause on vscode at 12 checkers is below Server GC's level (about 0.8 s), or when three candidates in a row fail.
- **Before the final report:** stop every background task you started, and leave the tree clean.
- **Docs at the end:** an AR1 section in [phase1-progress.md](../phase1-progress.md), with the census before and after.

## Report

- **The census before the first step** (goal 1), with tsrs's counterpart per group.
- **Each step:** the group moved, its share of the pause or promoted bytes, the tsrs source, what was changed, the ratios with intervals against the previous step, the pause, and the commit.
- **Base against final:** the main and guard workloads, interleaved, 7 rounds, with the factor against tsrs and typescript-go per repository, and the pause.
- **The census after the last step,** and what is left.
- The GC-setting experiments, if run.
- What was tried and rejected.
- The Gate C samples used, and the dump checks.
- The commit hashes.

## Measurement (orchestrator)

- The five repositories, interleaved, 5 rounds: base and final drivers with `--pool tsrs` (srv, ws), the default pool (ws), `tsrs-check` and `tsgo7-check`.
- Reported as the factor against tsrs per repository, as in `results/phase3/FE1/run1/NOTE.md`.
