# EO2: the accessibility search with tsrs's structures — brief

Second optimization step of the emitter (Track O). It follows [EO1](EO1.md) (kept by the author on 2026-11-04) and [emit-account.md](../emit-account.md), candidates 2 and 3.

The author asked on 2026-11-04 to also take over changes that imitate tsrs, that is a similar code structure. In this step that means: where tsrs has its own structure for one of our hot spots, port that structure, together with its exactness argument, rather than inventing a new one.

Track O only. Written by the orchestrator. The standing rules of [README.md](README.md) apply; run only the gates your change affects.

## Starting point

`main` at this brief's parent (20a57da + this brief).
- Gate C passes (50,177 of 50,177) and Gate D passes (50,177 of 50,177).
- The emit equivalence is identical with tsc 6.0.3 on the five repositories and the tuning emit list.
- The emitter endpoint `results/phase4/emit/2026-11-03T19-39-41-764Z/` (G = 1.030) stays the endpoint whatever follows.

**EO1's re-profile** (phase1-progress.md, EO1), vscode `--check --emit --single`, emit window 17.6 s:
- leaves: `Checker.TableOf` 18.3 %, `GetAlternativeContainingModules` 7.9 %, `GetAliasForSymbolInContainer` 7.7 %;
- inclusive: `GetContainersOfSymbol` 35.6 %, of which `GetAlternativeContainingModules` 34.4 %.
- On the tuning emit list the same functions come after `TrySymbolTable` (10.7 %) and waiting for collections (`PollGC` 15.4 %).

**tsrs's structures for the same code.** tsrs is at `external/tsrs/`, the pinned tag. tsrs's emit of vscode takes 1.33 s against typescript-go's 4.68 s (`notes/mem-emit-regions.md`).
- `exports_by_target` (`crates/tsrs_checker/src/printer.rs`, around line 1270): the loop of `getAliasForSymbolInContainer` over an export table, answered from an index of that table by merged resolved target. The `ExportsByTarget` type is a sorted array of (target, entry) pairs, one allocation per table.
- `modules_exporting` with `ModuleExportIndex` (printer.rs, around lines 1152 and 2216): the whole-program loop of `getAlternativeContainingModules`, answered from a reverse index "target → modules" for modules whose answer is final ("summarized").
- `alias_cache_blockers` (checker.rs:1553, checker_08.rs:1932, 2159): both indexes are bypassed while an alias resolution or a module export table computation is in progress, and until the checker's initialization has merged the globals.
- The comments at these places give the exactness argument. They name when the index may answer and when the loop has to run.
- tsrs's symbol tables (`notes/perf-symbol-map-lookups.md`, `notes/mem-flat-symbol-tables.md`) are background for step 3.

## Goal

1. **`getAliasForSymbolInContainer`: tsrs's export table index.**
   - Port `exports_by_target` and `get_symbol_target_for_alias_lookup`, adapted to Track O's layout.
     - The index is per checker and keyed by the table (`SymTable` identity).
     - Store it as one array of pairs per table, or as ranges in a per-checker store.
   - Take over tsrs's conditions: the blocker counter, and the size guard for replaced tables.
   - Take over the first-indexing order: first entry, then `symbol`, then the rest. Alias targets are then resolved in the loop's order.
2. **`getAlternativeContainingModules`: no repeated walks.**
   - Port tsrs's `ModuleExportIndex` for the loop over all external modules.
   - For the loop over the enclosing file's imports, add the memo of empty per-file results (EA1 candidate 2). tsrs and TypeScript store only non-empty results there.
     - The memo may answer only when the repeat would have no effect: for example, the modules resolved and the alias targets already cached.
     - Write the argument into the code comment, as tsrs does.
   - Measure which part carries the cost before you build both. If one part makes the other worthless, build only that one and report it.
3. **`Checker.TableOf` without the dictionary lookup.**
   - Replace `binderTables` (`Dictionary<long, SymTable>`) with a per-file array indexed by the binder table's index, filled on first use.
   - Alternative: store the copy on the binder symbol's checker slot. Use it if that is the smaller change and not slower.
   - Do not change insertion order or table identity: a table copied once stays the same table.
4. **Re-profile and one optional tsrs-style step.**
   - Profile vscode and the tuning emit list `--check --emit --single` again with EO1's method (`dotnet-trace` of the jit-ws driver, EA1's `acctool cpu`).
   - If an emit or node-builder hot spot now has a tsrs counterpart that is local, you may port it as step 5 under the same rules. Examples of such counterparts:
     - closures on a hot path replaced by monomorphized callbacks (in C#: a generic method over a `struct` that implements an interface, as `impl FnMut`; see `notes/monomorphization-audit.md`);
     - a list returned as the stored slice instead of a copy;
     - a reused scratch buffer (`free_type_lists`, `free_strings` in checker.rs:1540).
   - Otherwise, report what is hot and what tsrs does there. That is the input for the next step.

**Every ported structure:**
- names its tsrs source (file and function) in the code comment;
- marks itself as having no counterpart in TypeScript 6.0.3 ("Track O / tsrs-only").

**Hard constraint: results do not change.**
- Single-threaded check and emit output stays identical with tsc 6.0.3.
- The parallel check diagnostics and the parallel emit output (`--dump-emit`) at 4 checkers stay byte-identical with the baseline driver's, on the five repositories and the tuning emit list.
- Type, symbol and signature creation order per checker stays tsc's. An index may resolve aliases only where and in the order the loop it replaces would.
- Nothing is shared between checkers.

**Corpora:**
- Tune on the tuning emit list (`artifacts/bench/filelists/tuning-check-emit.txt`).
- The five benchmark repositories may be used to check the account and the equivalence, not to decide changes.

**Target:**
- The tuning emit list `--check --emit` (4 checkers) and `--check --emit --single` are faster.
- `--check` and `--check --single` on tuning-check are not slower.

## What may change

- `src/NeverSharp.Optimized/Checker/`.
- `Emitter/`, only if a step needs a hook there.

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
- **Parallel dumps:** at 4 checkers, `--dump-emit` and `--dump-diagnostics` on the five repositories and the tuning emit list, identical with the baseline driver's. Also single-threaded on the tuning emit list.
- **Stability:** `cs-o-aot-ws --check --emit` on the tuning emit list 4 times, with no crash.
- No change to `src/` after the last full gate runs.

| Gate | Run limit |
|---|---:|
| Gate C | 7 |
| Gate D | 3 |

## Working

- **Who:** one attempt with Opus 5.5 at medium effort, no sub-agents.
- **Where:** directly on `main` in the main checkout. Scratch: `<session scratch>/EO2/`. Reuse EA1's and EO1's tools (`EA1/tools`, `EA1/acctool`), but do not modify them.
- **Before the first change:** build the starting drivers (`build-cs.mjs --tracks optimized --runtimes aot-ws,aot-srv,jit-ws`) into `<session scratch>/EO2/base/`.
- **Building:** `dotnet publish` currently fails restore with NU1903 (advisory against `Microsoft.DotNet.ILCompiler` 10.0.13). Set the environment variable `WarningsNotAsErrors=NU1903` for every build. Commit nothing about it; change no package (author's decision of 2026-11-04).
- **Editing C#:** with the file tools only (Edit/Write). No sed, no Python or other scripts on C# files.
- **Probes:** remove temporary probes before committing.
- **Tools:** no downloads or installs.
- **Other processes:** never name, inspect or stop them. If the machine is busy, wait.
- **Commits:** commit each kept step with `git commit -s`; do not push.
- **Keep rule:** a step is kept only if your own measurement shows it is not slower.
  - Measure **interleaved** with `results/phase4/EO1/run1/follow-up/interleave.mjs`: each round runs every variant once, in rotated order. Run at least 10 rounds after 2 warm-up rounds, AOT ws, previous step against this step. Measure:
    - the tuning emit list `--check --emit` and `--check --emit --single`;
    - tuning-check `--check` and `--check --single`.
  - Decide with the 95 % bootstrap interval of the median ratio (`harness/bench/stats.mjs bootstrapMedianRatio`).
  - Placement effects are about ±0.5 % (EO1 follow-up). A step may be slower only in a check-alone configuration in which it does no work. Show this with a call count from an uncommitted probe. In that case, do not revert the step: report it, and the orchestrator decides.
  - Otherwise revert a slower step and report it.
- **Before the final report:** stop every background task you started, and leave the working tree clean.
- **Docs at the end:** an EO2 section in [phase1-progress.md](../phase1-progress.md).

## Report

- **Design of each step:** which tsrs structure it ports and how it maps onto Track O's layout. Give the exactness argument in short.
- **Your own measurements before and after:** the tuning emit list parallel and single, and tuning-check check parallel and single.
- **vscode `--check --emit`, parallel and single,** before and after. These numbers are for the account only.
- **The re-profile:** the hottest leaves and phases now, and tsrs's counterpart for each of the top five where there is one.
- What was tried and reverted.
- The last summaries of Gate C and Gate D, the emit equivalence, and the parallel dump comparison.
- The gate runs used.
- The commit hashes.

## Measurement (orchestrator)

- **Runs:** baseline drivers (this brief's parent) against the final `main`, interleaved (`interleave.mjs`), 20 rounds after 2 warm-up rounds:
  - the tuning emit list `--check --emit` and `--check --emit --single`;
  - tuning-check `--check` and `--check --single`.
- **Kept** if every gate passes and no configuration is slower by the 95 % interval. Otherwise the commits are reverted, unless the author decides otherwise.
- **Then,** on the author's decision, an additional run of profile `emit` on the benchmark repositories.
