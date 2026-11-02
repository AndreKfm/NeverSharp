# CO6: surviving checker objects under Workstation GC, at 4 and 12 checkers — brief

Sixth optimization step of the Phase 3 checker. It follows [CO5](CO5.md) and the §18 run with 12 checkers (`results/phase3/check12/2026-11-02T06-16-29-174Z/NOTE.md`).

Where things stand:
- `cs-o-aot-ws-check12` vs `tsgo7-check12`: G = 1.305 [1.292, 1.315];
- `cs-o-aot-srv-check12`: 0.890;
- at 4 checkers, additional run 2: 1.025;
- the endpoints stay as they are.

Track O only. Written by the orchestrator. The standing rules of [README.md](README.md) apply; run only the tests and gates your change affects. The hard constraint and the working rules of [CO2.md](CO2.md) apply unchanged.

## Starting point

`main` at this brief's parent (f157691 + this brief).
- Gate C passes: 50,177 of 50,177.
- tuning-check and the five repository programs are line-identical with tsc 6.0.3.
- The drivers take `--checkers N` (§18), with file *i* assigned to checker *i* mod N.
- Layout: [phase3-checker-layout.md](../phase3-checker-layout.md).
- Account of the pause: [phase1-progress.md](../phase1-progress.md), section "CO5".

**Under Workstation GC the pause grows with the checker count**, from the §18 run (Table 5):

| Program | Pause, 4 checkers | Pause, 12 checkers | Wall time, 12 checkers |
|---|---:|---:|---:|
| vscode | 3.9 s | 5.8 s | 11.45 s |
| bluesky | 0.19 s | 0.27 s | 0.81 s |
| playwright | 0.34 s | 0.62 s | 1.48 s |

Allocations also grow with the checker count. From single-threaded to 12 checkers, vscode goes from 11.5 to 16.0 GB and playwright from 1.12 to 1.81 GB.

Under Server GC, with the same allocations, the pause is 0.8 s on vscode.

**CO5's account:**
- Gen0 and gen1 collections are about 99 % of the pause.
- 97 % of the promoted bytes are reached through the card table: new checker data stored into long-lived stores.
- The pause follows the surviving objects **with references**, not the number of collections or the reference-free bytes. 8–16 % fewer allocations removed only 2–5 % of the pause.
- Every collection stops all checker threads, and the fixed part of a collection grows with the number of running checkers.

**Largest groups (CO5 "What is left"):**
1. **Type mappers.** About 190K mapper objects per small program, nearly all long-lived: `ArrayTypeMapper`, `SimpleTypeMapper`, `CompositeTypeMapper` and the mapper table. They are the largest group of surviving objects with references in the check phase.
2. **Duplicate-declaration info** of `reportMergeSymbolError`, about 8 MB per checker on tc2500.
   - Every checker builds it in construction, for every file pair. Three quarters of it is for files the checker does not keep.
   - It caused 18 % of the tc2500 pause at 4 checkers and multiplies with the checker count.
   - The outputs can land in any file of the declarations, so a checker cannot simply skip pairs without changing the count rule of `reportAmalgamatedDuplicates`.
3. **Other surviving data with references:**
   - the instantiable kinds' data and their `object[]` table;
   - `ProgramDiagnostic`s;
   - `ConditionalData`;
   - the expression links with their `HashSet`;
   - literal and signature records in doubling arrays.

**Already tried, and not to repeat as is:**
- **CO3:** signature parameters as arena ranges, and array/deferred mappers with `TypeList` sources and targets. Both made the run slower.
- **CO5 step 2** (reverted). It made the parallel run faster, but `--single` was 0.3–1 % slower with no single cause found. Patches in the session scratch `CO5/step2-wip/`; parts may be reused only if your measurement shows `--single` not slower.
- **CO4:** symbol slot pages on the large object heap had no effect on the pause.
- **CO1:** pinned arrays added pause.

**The runtime GC configuration is not an option.** The author chose redesigning the data over changing the GC configuration (CO1), and `eng/` stays unchanged. The NativeAOT drivers also ignore `DOTNET_GC*` environment variables.

## Goal

**1. Mappers without objects.**
- Store type mappers as records of a per-checker store: kind, source and target, and the two mapper handles of composite and merged mappers.
- The array kinds' `Type[]` go in a side table or an arena.
- Deferred and function mappers keep their delegates in a per-checker object table.
- `TypeMapper` becomes a handle (an int-sized struct) at its ~200 uses. `getMappedType`'s dispatch becomes a switch on the record's kind.
- The creation order, the identity semantics (`mapper === other` comparisons and caches keyed by mapper) and every result stay tsc's.
- Keep the CPU cost down. CO3's partial attempt lost on `--single`, so make the common kinds' lookup a direct read, not a dictionary lookup.

**2. Duplicate-declaration info that does not scale with the checker count.**
- Build the per-file-pair data once per program before the checkers start, as read-only data that all checkers share, and let each checker report from it. Alternatively build it lazily and reference-free.
- Results must not change:
  - single-threaded output and the parallel diagnostics at 4 and at 12 checkers stay byte-identical with the baseline driver's;
  - nothing writes into shared data while checkers run.
- If sharing is impossible without changing results, make it reference-free per checker: no per-symbol `HashSet`/`List`/`Dictionary` objects.

**3. Further surviving objects with references** from the list above, where your account shows them, with the CPU caveat of CO3 and CO5.

**Measure at both checker counts.** The target configuration is now also 12 checkers. Use `--checkers 12` on the tuning lists alongside the default of 4.

**Hard constraint, as in CO1–CO5: results do not change.**
- Type, symbol and signature creation order per checker stays tsc's.
- The file-to-checker assignment stays *i* mod N.
- Parallel diagnostics stay as they are:
  - at 4 checkers, tuning-check 130,435;
  - at 12 checkers, record the baseline count and the parallel dumps of the five repositories first, and keep both.
- Single-threaded output stays identical with tsc.
- Each checker keeps its own memory, except for read-only shared data built before the checkers start (goal 2). Nothing writes into a file's store while checkers run.

**Corpora:**
- Tune on the tuning-check list: the whole program, plus tc1500 and tc2500 as in CO4 and CO5.
- The five benchmark repositories may be used to check an account, not to decide changes.

**Target:**
- tc1500 and tc2500 `--check --checkers 12` clearly faster, with less Workstation GC pause.
- tuning-check `--check`, `--check --checkers 12` and `--single` not slower.
- `cs-o-aot-srv --check` not slower.

## What may change

As in [CO5.md](CO5.md):
- `src/NeverSharp.Optimized/Checker/` and `src/NeverSharp.Optimized/Program/`.
- `Arena/` if needed; then run the parser gate.
- Read-only accessors in `Binder/`; then run Gate B.

Must not change:
- Track F;
- `eng/`;
- the oracle and Gate C's harness;
- `bench.mjs` and `report.mjs`;
- the tuning-check list;
- `docs/preregistration.md`;
- the committed driver behaviour. Driver changes are allowed only for measurement switches that default to off and are not committed.

## Tests to run

As in [CO5.md](CO5.md):
- Gate C `--sample 2000` after each kept step.
- The full corpus after the last step, with no change to `src/` after the last full run. Keep its summary line.
- At the end:
  - `check-equivalence.mjs --corpus tuning-check --skip-tsrs`;
  - `check-equivalence.mjs --skip-tsrs` on the five repositories;
  - parallel dumps of the five repositories at 4 **and** at 12 checkers, identical with the baseline driver's;
  - `cs-o-aot-ws --check` on tuning-check 4 times (no crash, 130,435 each time), and twice with `--checkers 12` (no crash, the baseline count);
  - `cs-o-aot-srv --check` on tuning-check twice.
- Limit: 12 Gate C runs.

## Working

- **Who:** one attempt with Opus 5.5 at high effort, no sub-agents.
- **Where:** directly on `main` in the main checkout. Scratch: `<session scratch>/CO6/`. Reuse the tools in earlier CO folders, such as CO5's GC probe and trace reader, but do not modify them.
- **Before the first change:** build the starting drivers (`build-cs.mjs --tracks optimized --runtimes aot-ws,aot-srv,jit-ws`) into `<session scratch>/CO6/base/`.
- **Editing C#:** with the file tools only (Edit/Write). No sed, no Python or other scripts on C# files.
- **Probes:** remove temporary probes before committing.
- **Tools:** no downloads or installs.
- **Commits:** commit each kept step with `git commit -s`; do not push.
- **Keep rule:** a step is kept only if your own measurement shows it is not slower.
  - Measure, interleaved with the previous step, at least 5 runs each:
    - tuning-check `--check`, `--check --checkers 12` and `--check --single` (AOT ws);
    - tc1500 and tc2500 `--check` and `--check --checkers 12`.
  - Otherwise revert the step and report it.
  - A single-threaded change of about 1 % with no extra work may be AOT code placement. Show that with a no-op control, as CO4 did, before reporting it as such.
- **Before the final report:** stop every background task you started.
- **Docs at the end:** add a CO6 section to [phase1-progress.md](../phase1-progress.md). Update [phase3-checker-layout.md](../phase3-checker-layout.md) if the layout changes, as it does for mappers.

## Report

- The account at 4 and 12 checkers: pause by phase and the largest promoted groups, before and after.
- The steps with their design and your own measurements: tc1500/tc2500 and tuning-check, at 4 and 12 checkers, parallel and single, ws and srv, with GC count and pause before and after.
- What was tried and reverted.
- What is left.
- Gate C runs and the last full summary line.
- Equivalence and stability, including the 12-checker counts.
- NoAlloc count.
- Files changed outside `Checker/`.
- Commit hashes.

## Measurement (orchestrator)

- **Runs:** baseline drivers (this brief's parent) against the final `main`, rig-compliant, 20 runs each:
  - tuning-check `cs-o-aot-ws-check`, `cs-o-aot-ws-check12` and `cs-o-aot-ws-check-single`;
  - tc1500 and tc2500 at 4 and 12 checkers with hyperfine.
- **Kept** if every gate passes and tuning-check is not slower by the 95 % interval in any of the three configurations. Otherwise the commits are reverted.
- **Then,** on the author's decision, an additional run on the benchmark repositories, profile `check12` and/or `check`.
