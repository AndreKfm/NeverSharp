# Phase 2: binder — summary (closed 2026-10-25) — Windows only

Phase 2 ported the binder of TypeScript 6.0.3 (`src/compiler/binder.ts`) for the optimized track (Track O) only, decided by the author on 2026-10-24 (Track F not extended; tests run only where a change affects them). Plan: [phase2-plan.md](phase2-plan.md); pre-registration amendments `prereg/v7` (§15: scope, Gate B, endpoint) and `prereg/v8` (§16: measurement profiles), both tagged and timestamped before any binder measurement.

## Equivalence

**Gate B passes:** the canonical binder dumps of Track O (symbols, symbol tables, containers and container chain, the control-flow graph including `flowNodeWhenTrue`/`flowNodeWhenFalse`/`possiblyExhaustive`, node flags set by the binder, bind diagnostics) are byte-identical to tsc 6.0.3's `bindSourceFile(file, {})` on all 50,177 equivalence units in both JSDoc modes (ParseAll and ParseForTypeErrors; dump format v2). Every optimization step passed Gate B again on the orchestrator's rerun; steps touching parser storage (BO1) also passed the full Track O parser gate. Symbol totals reported by the parse+bind driver match tsc 6.0.3 on all six corpora.

## Verdict and additional runs (parse+bind per file, `cs-o-aot-ws-bind` vs `tsgo7-bind`)

| Run (harness commit) | Steps since | vs typescript-go (G) | single vs single | vs tsrs (secondary) | single vs tsrs single |
|---|---|---:|---:|---:|---:|
| **Binder endpoint** (7019057) | B0–B4 | **1.207 [1.191, 1.219]: inconclusive** | 0.985 | 3.31 | 1.822 |
| Additional 1 (f10774a) | BO1 | 0.489 [0.483, 0.498] | 0.763 | 1.334 [1.310, 1.352] | 1.416 |
| Additional 2 (07ac6df) | BO2 | 0.456 [0.451, 0.462] | 0.654 | 1.222 [1.207, 1.238] | 1.209 |
| Additional 3 (36c6cb2) | BO3 | 0.434 [0.430, 0.440] | 0.635 | 1.193 [1.172, 1.204] | 1.172 |
| Additional 4 (68a7829) | BO4 | **0.425 [0.419, 0.431]** | 0.635 | **1.146 [1.127, 1.161]** | 1.176 |

All runs: profile `binder` (§16), rig checks ok, variance gate PASS (binding: tsgo7-bind-single, cs-o-aot-ws-bind-single). The endpoint is the first protocol-compliant run after Gate B passed; it measured the unoptimized port, whose parallel runs were held back by allocation (vscode 3,984 MB, 581 ms GC pause; single-threaded it was already even with typescript-go). The additional runs are published next to it and never replace it. vscode, last run: Track O 0.450 s, typescript-go 1.325 s, tsrs 0.404 s; peak working set 1,404 / 2,764 / 1,148 MiB; Track O allocates 89 MB with 50 ms GC pause in total.

## What may be claimed

- **Binder endpoint:** inconclusive (G = 1.207, interval contains 1.20); neither "within 20 % of typescript-go" nor "more than 20 % slower" for parse+bind.
- **From additional runs (labeled as such):** parse+bind about 2.4 times as fast as typescript-go 7.0.2 (0.425, upper bound < 1.00); within 20 % of tsrs 0.18.0 (secondary: 1.146, upper bound 1.161).
- **Not claimed:** faster than tsrs (overall and on every repository Track O stays above it: 1.12–1.19 in the last run).

## Disclosed differences

- typescript-go's and tsrs's binders port TypeScript 7 and differ from 6.0.3's in places (symbol totals differ by 1 on four repositories and by 398 on the tuning corpus); typescript-go 7.0.2's `BindSourceFile` takes no compiler options, Track O binds with `{}` as tsc's `bindSourceFile`.
- The binder endpoint and its additional runs report the NativeAOT runtimes only (§16); the JIT runtimes are not measured for the binder.
- JSDoc parsing mode and code generation as in Phase 1 (docs/references.md §2a): C# drivers `ParseForTypeErrors`, typescript-go eager JSDoc, tsrs lazy JSDoc in `.ts`; Go and Rust at the x86-64 baseline, NativeAOT at x86-64-v2.
- Windows only; the Linux replication (A4) has not been run.

## Modules and steps

| Module / step | What | Result |
|---|---|---|
| B0 | oracle (`dump-ts-binder.mjs`), compare, `gate.mjs --phase binder`, Track O layout (records and int handles, insertion-ordered chains) | oracle deterministic |
| B1 | traversal, symbols, declarations, containers; the whole control-flow traversal (needed for symbol order in unreachable code); strict-mode checks | symbols 49,420 / 50,177 (rest attributed to B3) |
| B2 | control-flow graph | done inside B1; verification and missing fields in B3 |
| B3 | JavaScript special assignments, CommonJS, JSDoc typedefs, remaining flow fields | **Gate B passes** |
| B4 | `--bind` drivers (Track O, typescript-go, tsrs), configurations, report tables | symbol totals match tsc |
| BO1 | allocations: dense parent array, binder storage in per-thread slabs | tuning parse+bind 0.517 |
| BO2 | CPU hot paths: modifier flags without the side table, per-identifier checks, direct child dispatch | 0.936 |
| BO3 | per-file setup: scratch block and compact commit (page faults −15 %), token fast path | 0.988 (not slower) |
| BO4 | smaller binder output and store object (module name without strings, 40-byte symbols, 12-byte flow records) | 0.988 (not slower); sentry −5 % |

NoAlloc after BO4: Track O 2,371 of 2,985 methods annotated, binder 129 of 385; 55 `[NoAllocUnchecked]` exceptions, unchanged since prereg/v6.

## Notes for a later checker

The binder keeps everything the checker reads (`flowNodeWhenTrue`/`flowNodeWhenFalse`, `possiblyExhaustive`, symbol ids for private names). BO4 does not keep the symbol tables' hash index after binding: lookups scan the table and an insert rebuilds the index, so a checker should rebuild it once per file before heavy lookups.

## Process notes

- B3's attempt accidentally reverted its own edits (`git checkout <base> -- .`) and re-applied them; the gate passed in the attempt and on the orchestrator's rerun on main.
- The oracle stamp hashed raw bytes, so worktrees (LF) rebuilt the cached dumps of the main checkout (CRLF); fixed by normalizing line endings.
- B4 was cherry-picked onto main (main had moved past its base).
- Two attempts used `sed` for renames against the standing rule; the diffs were checked and the gates passed.
