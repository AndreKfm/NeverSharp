# RS1: the checker with tsrs's structures — brief

First step on the checker that imitates tsrs's code structure (Track O). The author asked on 2026-11-04 to take over changes that imitate tsrs, that is a similar code structure, and agreed to this step after EO2. It reopens the checker optimization, closed after CO6, for this purpose only.

The step follows [tsrs-gap.md](../tsrs-gap.md) (TG1), candidates 1, 3 and 5. The method is that of EO2: where tsrs has its own structure or code shape for one of our hot spots, port that structure together with its exactness argument, rather than inventing a new one.

Track O only. Written by the orchestrator. The standing rules of [README.md](README.md) apply; run only the gates your change affects.

## Starting point

`main` at this brief's parent (6cf5cfe + this brief).
- Gate C passes (50,177 of 50,177) and Gate D passes (50,177 of 50,177). The emit equivalence is identical with tsc 6.0.3.
- EO2 is kept (`results/phase4/EO2/run1/`, interleaved): tuning-check `--check` 0.993, `--check --single` 0.991.
- The endpoints stay what they are whatever follows: checker 1.680, check12 1.305, emitter 1.030.

**TG1's findings:**
- **Single-threaded, the gap to tsrs is all in the check phase.** It is 1.98× per unit of work, with the same numbers of types and symbols on both sides.
- **Named causes:**
  - GC and allocation: pause about 5 % single-threaded, allocation slow paths 8 %, `Clone()` 3.9 % and `Memmove` 2.8 % in the profile;
  - `Func<>` delegates on hot paths (the `DisplayClass` entries);
  - `RelationKey` keeping a string for pairs of generic references;
  - name lookups (`NameIdOf` 2 % exclusive).
- **In parallel,** the Workstation GC pause costs up to 1.26× at 4 checkers and 1.7× at 12 on vscode.
- The surviving data that drives that pause is listed in report-v1 §11 and in CO6's leftovers:
  - inference contexts retained by the mapper store;
  - signature parameter arrays;
  - the instantiable kinds' data classes;
  - the per-checker globals merge at 12 checkers.

**tsrs's structures and code shapes**, read-only at `external/tsrs/`. Read `docs/CHECKER.md`, `docs/RUST.md`, `notes/cpu-checker.md`, `notes/perf-checker-cpu3.md`, `notes/perf-symbol-map-lookups.md`, `notes/monomorphization-audit.md` and `notes/mem-checker-scratch.md` first.
- **Stored slices instead of copies** (`cpu-checker.md` changes 7, 8, 10): `getTypeArguments`, `getElementTypes`, the property, signature and index-info lists, property-name helpers.
  - Iterating a stored list keeps the copy's snapshot semantics only if stored lists are replaced, never mutated.
  - Check this for every list you return.
- **Pooled scratch** (`cpu-checker.md` changes 1 and 4; checker.rs `free_type_lists`, `free_strings`, `free_name_sets`): inference state maps, active-mapper caches, temporary lists reused instead of allocated per call.
- **Monomorphized callbacks:** tsrs's closures are `impl FnMut` generics. The C# counterpart is a generic method over a `struct` that implements an interface. Callers then pass no delegate and allocate no closure.
- **Keys without strings:** typescript-go and tsrs build relation, instantiation and union keys with `keyBuilder` (xxh3-128); tsrs shortcuts the common cases (`perf-checker-cpu3.md` changes 2, 3).
  - Track O must stay exact. A key may replace the string only if two keys are equal exactly when their strings are: for example ids in a value-type key, or an interned id of the type-argument list.
  - A hash alone is not enough unless it is backed by a full comparison.
- **Symbol table lookups** (`perf-symbol-map-lookups.md`, `perf-checker-cpu3.md` changes 1, 7): a per-table filter before the search, and same-name shortcuts.

## Goal

1. **Profile first.**
   - Take a sampled profile of tuning-check `--check --single` and of vscode `--check --single` with EO2's method (`dotnet-trace --profile cpu-sampling` of the jit-ws driver, EA1's `acctool cpu`).
   - Also take an allocation account of tuning-check `--check` at 4 checkers: allocated bytes and the largest allocating types (EA1's method).
   - Map the top hot spots and allocation sources to the tsrs structures above. Order the steps by measured share, not by this list.
2. **Port the tsrs structures that the profile ranks highest, one step per structure.** Expected first candidates, to confirm or replace by the profile:
   1. stored lists instead of `Clone()` / copied type-argument and member lists;
   2. delegates on hot paths (`MapType`, relater and `forEachType`-style callbacks) as struct-generic callbacks;
   3. `RelationKey` and the instantiation key without strings for generic references;
   4. pooled scratch for inference and mapper caches.
3. **Workstation GC in parallel.** After the CPU steps, re-measure the pause at 4 and 12 checkers.
   - If a remaining surviving-object source has a tsrs counterpart that is local (for example scratch reused instead of retained, or data in a per-checker arena as the parser and binder have), you may port it as a further step.
   - Otherwise, report the sources, their sizes and tsrs's counterpart.
4. **Re-profile at the end.** Report what is hot now, and tsrs's counterpart for each of the top ten.

**Every ported structure:**
- names its tsrs source (file and function or note) in the code comment;
- marks itself as Track O / tsrs-only where it has no counterpart in TypeScript 6.0.3.

**Hard constraint: results do not change.**
- Single-threaded check and emit output stays identical with tsc 6.0.3. Gate C's diagnostics, types and symbols sections stay identical, so type, symbol and signature creation order per checker stays tsc's.
- The parallel check diagnostics at 4 and 12 checkers, and the parallel emit output (`--dump-emit`) at 4 checkers, stay byte-identical with the baseline driver's, on the five repositories and the tuning lists.
- Nothing is shared between checkers.

**Corpora:**
- Tune on tuning-check (`artifacts/bench/filelists/tuning-check.txt`) and the tuning emit list.
- The five benchmark repositories may be used to check the account and the equivalence, not to decide changes.

**Target:**
- tuning-check `--check` (4 checkers), `--check --checkers 12` and `--check --single` are faster.
- The tuning emit list `--check --emit` and `--check --emit --single` are not slower.

## What may change

- `src/NeverSharp.Optimized/Checker/`.
- Shared Track O types the checker uses (`src/NeverSharp.Optimized/`), when a step needs it, with the gates the change affects.

Must not change:
- Track F;
- `eng/`;
- the oracles, the gate harnesses, `bench.mjs` and `report.mjs`;
- the tuning lists;
- `docs/preregistration.md`;
- `external/` (read only);
- the committed driver behaviour. Driver changes are allowed only for measurement switches that default to off and are not committed.

## Tests to run

- **Gate C:** `--sample 2000` after each kept step; the full corpus at the end.
- **Gate D:** a full run at the end.
- **Emit equivalence:** `emit-equivalence.mjs --skip-tsgo --skip-tsrs` on the tuning emit list and the five repositories at the end.
- **Parallel dumps:** identical with the baseline driver's on the five repositories and the tuning lists:
  - `--dump-diagnostics` at 4 and at 12 checkers;
  - `--dump-emit` at 4 checkers.

  Run them on the tuning lists after each kept step, and on all at the end.
- **Stability:** `cs-o-aot-ws --check` at 12 checkers and `--check --emit` at 4 on the tuning lists 4 times each, with no crash.
- No change to `src/` after the last full gate runs.

| Gate | Run limit |
|---|---:|
| Gate C | 10 |
| Gate D | 3 |

## Working

- **Who:** one attempt with Opus 5.5 at medium effort, no sub-agents.
- **Where:** directly on `main` in the main checkout. Scratch: `<session scratch>/RS1/`. Reuse EA1's and EO2's tools (`EA1/acctool`, `EO2/`), but do not modify them.
- **Before the first change:** build the starting drivers (`build-cs.mjs --tracks optimized --runtimes aot-ws,aot-srv,jit-ws`) into `<session scratch>/RS1/base/`.
- **Building:** set the environment variable `WarningsNotAsErrors=NU1903` for every build. Commit nothing about it; change no package.
- **Editing C#:** with the file tools only (Edit/Write). No sed, no Python or other scripts on C# files.
- **Probes:** remove temporary probes before committing.
- **Tools:** no downloads or installs.
- **Other processes:** never name, inspect or stop them. If the machine is busy, wait.
- **Commits:** commit each kept step with `git commit -s`; do not push.
- **Keep rule:** a step is kept only if your own measurement shows it is not slower.
  - Measure **interleaved** with `results/phase4/EO1/run1/follow-up/interleave.mjs`: at least 10 rounds after 2 warm-up rounds, AOT ws, previous step against this step. Measure:
    - tuning-check `--check`, `--check --checkers 12` and `--check --single`;
    - the tuning emit list `--check --emit --single`.
  - Decide with the 95 % bootstrap interval of the median ratio (`harness/bench/stats.mjs bootstrapMedianRatio`).
  - Placement effects are about ±0.5 %. A step may be slower only in a configuration in which it does no work. Show this with a call count from an uncommitted probe. In that case, do not revert the step: report it, and the orchestrator decides.
  - Otherwise revert a slower step and report it.
- **Size:** stop after at most six kept steps, or when the next candidate's measured share is below 1 % of the check. Report what is left.
- **Before the final report:** stop every background task you started, and leave the working tree clean.
- **Docs at the end:** an RS1 section in [phase1-progress.md](../phase1-progress.md).

## Report

- **The profile and allocation account before the first step,** and how its hot spots map to tsrs's structures.
- **Design of each step:** which tsrs structure it ports and how it maps onto Track O's layout. Give the exactness argument in short.
- **Your own measurements before and after:** each step against the previous one, and base against final, for the configurations of the keep rule.
- **vscode `--check` at 4 and 12 checkers and `--check --single`,** before and after, with the GC pause. These numbers are for the account only.
- **The re-profile:** the hottest leaves now, and tsrs's counterpart for each of the top ten.
- What was tried and reverted.
- The last summaries of Gate C and Gate D, the emit equivalence, and the parallel dump comparison.
- The gate runs used.
- The commit hashes.

## Measurement (orchestrator)

- **Runs:** baseline drivers (this brief's parent) against the final `main`, interleaved, 20 rounds after 2 warm-up rounds:
  - tuning-check `--check`, `--check --checkers 12` and `--check --single`;
  - the tuning emit list `--check --emit` and `--check --emit --single`.
- **Kept** if every gate passes and no configuration is slower by the 95 % interval. Otherwise the commits are reverted, unless the author decides otherwise.
- **Then,** on the author's decision, additional runs of profiles `check`, `check12` and `emit` on the benchmark repositories.
