# EO1: port `accessibleChainCache` — brief

First optimization step of the emitter (Track O). It follows the account [EA1](EA1.md), [emit-account.md](../emit-account.md), candidate 1. The author agreed on 2026-11-04.

Track O only. Written by the orchestrator. The standing rules of [README.md](README.md) apply; run only the gates your change affects.

## Starting point

`main` at this brief's parent (e3e1c87 + this brief).
- Gate C passes (50,177 of 50,177) and Gate D passes (50,177 of 50,177).
- The emit equivalence (`emit-equivalence.mjs`) is identical with tsc 6.0.3 on the five repositories and the tuning emit list.
- The emitter endpoint is `results/phase4/emit/2026-11-03T19-39-41-764Z/`: G = 1.030. It stays the endpoint whatever follows.

**EA1's finding.**
- On vscode, 52 % of the single-threaded emit samples are in the declaration emit's accessibility search (`GetAccessibleSymbolChain`, `TrySymbolTable`, `TableOf`).
- TypeScript 6.0.3 caches the result of `getAccessibleSymbolChain` in `links.accessibleChainCache`. The key is `useOnlyExternalAliasing`, the first location with a symbol table and the meaning. typescript-go has the same cache (`symbolContainerLinks.accessibleChainCache`).
- Track O left it out (`Checker/Checker.SymbolAccessibility.cs`).

**EA1's experiment** (uncommitted, env-gated, removed; patch in the session scratch `EA1/probe-src/`, `EA_CHAINCACHE`):
- Outputs: emit dumps and check diagnostics byte-identical on six lists, single-threaded and with 4 checkers.
- vscode, 4 checkers: 23.28 → 20.79 s. Single-threaded: 62.51 → 56.99 s.
- Tuning emit list: −4.3 % with 4 checkers, −9.1 % single-threaded.
- Check alone: unchanged.

## Goal

1. **Port the cache** faithfully, as TypeScript 6.0.3 has it:
   - per checker, in the symbol's links;
   - same key, same point of lookup and store;
   - the node builder's fake scope gets its own key, as in tsc.
   - Store the cached chain without a new object per entry where the existing layout allows it (a range in a per-checker store, for example). A plain list is acceptable if that is not slower.
2. **Remove the per-call `Predicate` closures in `TrySymbolTable`** (EA1, ~105 MB on vscode), if this is local and not slower.
3. **Re-profile at the end.** Take a sampled profile of vscode and the tuning emit list `--check --emit --single`, with EA1's method and `acctool cpu`. Report what is hot now. This is input for the next step (EA1 candidates 2 and 3); do not implement those here.

**Hard constraint: results do not change.**
- Single-threaded check and emit output stays identical with tsc 6.0.3.
- The parallel check diagnostics and the parallel emit output (`--dump-emit`) at 4 checkers stay byte-identical with the baseline driver's, on the five repositories and the tuning emit list.
- Type, symbol and signature creation order per checker stays tsc's.
- Nothing is shared between checkers.

**Corpora:**
- Tune on the tuning emit list (`artifacts/bench/filelists/tuning-check-emit.txt`).
- The five benchmark repositories may be used to check the account and the equivalence, not to decide changes.

**Target:**
- The tuning emit list `--check --emit` (4 checkers) and `--check --emit --single` are faster.
- `--check` alone and `--check --single` on tuning-check are not slower.

## What may change

- `src/NeverSharp.Optimized/Checker/`.
- `Emitter/`, only if the cache needs a hook there.

Must not change:
- Track F;
- `eng/`;
- the oracles, the gate harnesses, `bench.mjs` and `report.mjs`;
- the tuning lists;
- `docs/preregistration.md`;
- the committed driver behaviour. Driver changes are allowed only for measurement switches that default to off and are not committed.

## Tests to run

- **Gate C:** `--sample 2000` after each kept step; the full corpus at the end.
- **Gate D:** a full run at the end.
- **Emit equivalence:** `emit-equivalence.mjs` on the tuning emit list and the five repositories at the end.
- **Parallel dumps:** at 4 checkers, `--dump-emit` and `--dump-diagnostics` on the five repositories and the tuning emit list, identical with the baseline driver's.
- **Stability:** `cs-o-aot-ws --check --emit` on the tuning emit list 4 times, with no crash.
- No change to `src/` after the last full gate runs.

| Gate | Run limit |
|---|---:|
| Gate C | 6 |
| Gate D | 3 |

## Working

- **Who:** one attempt with Opus 5.5 at medium effort, no sub-agents.
- **Where:** directly on `main` in the main checkout. Scratch: `<session scratch>/EO1/`. Reuse EA1's tools (`EA1/tools`, `EA1/acctool`, the probe patch), but do not modify them.
- **Before the first change:** build the starting drivers (`build-cs.mjs --tracks optimized --runtimes aot-ws,aot-srv,jit-ws`) into `<session scratch>/EO1/base/`.
- **Editing C#:** with the file tools only (Edit/Write). No sed, no Python or other scripts on C# files.
- **Probes:** remove temporary probes before committing.
- **Tools:** no downloads or installs.
- **Other processes:** never name, inspect or stop them. If the machine is busy, wait.
- **Commits:** commit each kept step with `git commit -s`; do not push.
- **Keep rule:** a step is kept only if your own measurement shows it is not slower.
  - Measure interleaved with the previous step, at least 5 runs each, AOT ws:
    - the tuning emit list `--check --emit` and `--check --emit --single`;
    - tuning-check `--check` and `--check --single`.
  - Otherwise revert the step and report it.
- **Before the final report:** stop every background task you started, and leave the working tree clean.
- **Docs at the end:** an EO1 section in [phase1-progress.md](../phase1-progress.md).

## Report

- The design of each step, and your own measurements before and after: the tuning emit list parallel and single, and tuning-check check parallel and single.
- vscode `--check --emit`, parallel and single, before and after. These numbers are for the account only.
- The re-profile: the hottest leaves and phases now.
- What was tried and reverted.
- The last summaries of Gate C and Gate D, the emit equivalence, and the parallel dump comparison.
- The gate runs used.
- The commit hashes.

## Measurement (orchestrator)

- **Runs:** baseline drivers (this brief's parent) against the final `main`, rig-compliant, 20 runs each with hyperfine, on the tuning emit list:
  - `cs-o-aot-ws` with `--check --emit`;
  - `--check --emit --single`;
  - tuning-check `--check`.
- **Kept** if every gate passes and no configuration is slower by the 95 % interval. Otherwise the commits are reverted.
- **Then,** on the author's decision, an additional run of profile `emit` on the benchmark repositories.
