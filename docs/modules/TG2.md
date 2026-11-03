# TG2: candidates from the tsrs gap that keep results — brief

**Withdrawn 2026-11-03, before any change:** the author closed the checker (Gate C) work after TG1 and moved on to the next phase. Kept for reference.

Implementation step after the analysis [TG1](TG1.md) ([tsrs-gap.md](../tsrs-gap.md)).

Track O only. Written by the orchestrator. The standing rules of [README.md](README.md) apply; run only the tests and gates your change affects. The hard constraint and the working rules of [CO2.md](CO2.md) and [CO6.md](CO6.md) apply unchanged.

## Starting point

`main` at this brief's parent (c6f730f).
- Gate C passes: 50,177 of 50,177.
- tuning-check and the five repository programs are line-identical with tsc 6.0.3.
- Layout: [phase3-checker-layout.md](../phase3-checker-layout.md).

TG1's account (exploratory) explains the gap to tsrs on vscode, a factor of 6.3, as these factors multiplied:

| Factor | Size | Covered here? |
|---|---:|---|
| Workstation GC pause | 1.26 | no: closed by the author after CO6 |
| per-checker speed of the check phase | 1.97 | yes: goals 1–3 |
| more checkers | 1.76 | no: changes the parallel diagnostics |
| locality assignment | 1.22 | no: changes the parallel diagnostics |
| work stealing | 1.18 | no: changes the parallel diagnostics |

Single-threaded, parse and bind cost the same as tsrs's, and the check phase is 1.98 times as slow per operation.

The JIT profile (playwright, `--single`, inside `CheckSourceFileOnce`) is flat:
- `PollGCWorker` / allocation slow path 8.0 %;
- `MemberwiseClone` 3.9 %;
- `Memmove` 2.8 %;
- `TypeToString` 10.6 % inclusive, of which `GetAccessibleSymbolChain` 6.3 %;
- `NameIdOf` 2.0 %.

## Goal

The goals come from TG1's ranks 2–5, all category (a): results do not change.

**1. Port the left-out caches of TypeScript 6.0.3** (TG1 rank 2).
- Port `accessibleChainCache` of `getAccessibleSymbolChain` as tsc has it: `links.accessibleChainCache`, with tsc's key and its conditions for using the cache.
  - The comment at `Checker.SymbolAccessibility.cs:13-15` leaves it out.
  - typescript-go: `symbolaccessibility.go:455-474`.
- Check the left-out `isVisible` cache of `isDeclarationVisible` the same way, and port it if it is faithful and pays.
- Both caches live in the checker's own memory: nothing writes into a file's store while checkers run.
- Update the port comments.

**2. Allocation churn in the check** (TG1 rank 3).
- Type-argument and type-list copies (`(Type[])types.Clone()` at `Checker.Instantiate.cs:549`, `Checker.Generics.cs:471` and similar): return stored lists where nothing writes into them, as tsrs did (`external/tsrs/notes/cpu-checker.md`, changes 6–8).
- Fewer `Func<>` delegates on hot paths, for example `MapType(Type, Func<Type,Type>)` and the relater callbacks, where a struct or a static path does the same.
- **CO3's lesson:** list types and ranges in place of arrays made the run slower. Keep the common case a direct read.

**3. Parallel front end** (TG1 rank 4).
- In parallel on vscode, import resolution plus bind take 0.62–0.69 s, against 2.13 s single-threaded. tsrs needs 0.41–0.50 s for parse, resolution and bind together.
- First split the time into resolution and bind and find the cause. Then fix it if a fix keeps results.

**4. Optional, if time remains** (TG1 rank 5): one or two of tsrs's output-preserving memos that map onto Track O's layout, each through Gate C. Examples: named-union membership by set, the indexed-access memo. Report which ones and why.

**Not in scope:**
- the Workstation GC pause as its own goal (CO7, closed);
- lazy members (TG1 rank 6);
- assignment, more checkers or stealing (ranks 7–9, category (b));
- anything that changes the dumps of Gate C.

**Hard constraint, as in CO1–CO6: results do not change.**
- Type, symbol and signature creation order per checker stays tsc's.
- The file-to-checker assignment stays *i* mod N.
- The parallel diagnostics stay as they are:
  - at 4 checkers: tuning-check 130,435, vscode 101,474, playwright 30,789;
  - at 12 checkers: tuning-check 128,748, vscode 101,471, playwright 30,798;
  - the other repositories as in CO6's report.
- Single-threaded output stays identical with tsc.
- Each checker keeps its own memory, except for read-only shared data built before the checkers start.

**Corpora:**
- Tune on the tuning-check list and on tc1500/tc2500 (the first 1,500 and 2,500 lines of `artifacts/bench/filelists/tuning-check.txt`).
- The five repositories may be used to check an account and for goal 3's split, not to decide changes.

**Target:**
- tuning-check `--check --single` clearly faster;
- `--check` and `--check --checkers 12` (AOT ws) not slower, and faster where the goal allows;
- `cs-o-aot-srv --check` not slower.

## What may change

- `src/NeverSharp.Optimized/Checker/` and `src/NeverSharp.Optimized/Program/`.
- `Binder/` and module resolution, for goal 3; then run Gate B.
- `Arena/` if needed; then run the parser gate.

Must not change, as in [CO6.md](CO6.md):
- Track F;
- `eng/`;
- the oracle and Gate C's harness;
- `bench.mjs` and `report.mjs`;
- the tuning-check list;
- `docs/preregistration.md`;
- the committed driver behaviour. Measurement switches only uncommitted and default off.

## Tests to run

As in [CO6.md](CO6.md):
- Gate C `--sample 2000` after each kept step.
- Gate B after any `Binder/` change.
- The full corpus after the last step, with no change to `src/` after the last full run. Keep its summary line.
- At the end:
  - `check-equivalence.mjs --corpus tuning-check --skip-tsrs`;
  - `check-equivalence.mjs --skip-tsrs` on the five repositories;
  - parallel dumps of the five repositories at 4 and at 12 checkers, identical with the baseline driver's;
  - `cs-o-aot-ws --check` on tuning-check 4 times (no crash, 130,435 each time) and twice with `--checkers 12` (128,748);
  - `cs-o-aot-srv --check` twice.
- Limit: 12 Gate C runs.

## Working

As in [CO6.md](CO6.md):
- **Who:** one attempt with Opus 5.5 at high effort, no sub-agents.
- **Where:** directly on `main` in the main checkout. Scratch: `<session scratch>/TG2/`.
- **Before the first change:** build the starting drivers into `<session scratch>/TG2/base/` (`build-cs.mjs --tracks optimized --runtimes aot-ws,aot-srv,jit-ws`).
- **Editing C#:** with the file tools only.
- **Tools:** no downloads or installs. dotnet-trace and TraceEvent are already there (TG1). TG1's scratch tools in `<session scratch>/TG1/` may be reused, not modified.
- **Probes:** remove temporary probes before committing.
- **Commits:** commit each kept step with `git commit -s`; do not push.
- **Keep rule:** a step is kept only if your own measurement shows it is not slower.
  - Measure, interleaved with the previous step, at least 5 runs each:
    - tuning-check `--check`, `--check --checkers 12` and `--check --single` (AOT ws);
    - tc1500 and tc2500 `--check`, `--check --checkers 12` and `--single`.
  - A single-threaded change of about 1 % with no extra work may be AOT code placement. Show that with a no-op control before reporting it as such.
  - Otherwise revert the step and report it.
- **Before the final report:** stop every background task you started.
- **Docs at the end:**
  - add a TG2 section to [phase1-progress.md](../phase1-progress.md);
  - update [phase3-checker-layout.md](../phase3-checker-layout.md) where the layout changes;
  - update the port comments where a left-out cache is ported.

## Report

- The steps with their design and your own measurements:
  - tc1500/tc2500 and tuning-check;
  - parallel at 4 and 12 checkers and single-threaded;
  - ws and srv;
  - GC count and pause before and after.
- Goal 3's split of the parallel front end and what was done about it.
- What was tried and reverted.
- What is left of TG1's list.
- Gate C runs and the last full summary line; Gate B if run.
- Equivalence and stability, including the 12-checker counts.
- NoAlloc count.
- Files changed outside `Checker/`.
- Commit hashes.

## Measurement (orchestrator)

- **Runs:** baseline drivers (this brief's parent) against the final `main`, rig-compliant, 20 runs each:
  - tuning-check `cs-o-aot-ws-check`, `cs-o-aot-ws-check12`, `cs-o-aot-ws-check-single` and `cs-o-aot-srv-check`;
  - tc1500 and tc2500 at 4 and 12 checkers with hyperfine.
- **Kept** if every gate passes and tuning-check is not slower by the 95 % interval in any of the three Workstation GC configurations. Otherwise the commits are reverted.
- **Then,** on the author's decision, additional runs on the benchmark repositories.
