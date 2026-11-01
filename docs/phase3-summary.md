# Phase 3: type checker — summary (closed 2026-11-01) — Windows only

Phase 3 ported the type checker of TypeScript 6.0.3 (`src/compiler/checker.ts`), with the program layer it needs, for the optimized track (Track O) only. The author decided this on 2026-10-25. Plan: [phase3-plan.md](phase3-plan.md); layout: [phase3-checker-layout.md](phase3-checker-layout.md). The pre-registration amendment `prereg/v9` (§17: scope, Gate C, check mode, endpoint, tsrs as secondary reference) was tagged and timestamped on 2026-10-29, before any checker measurement. Details of every module and step: [phase1-progress.md](phase1-progress.md), section "Phase 3".

## Equivalence

**Gate C passes.** The canonical checker dumps of Track O — diagnostics, types and symbols, in JSDoc mode `types` — are byte-identical to tsc 6.0.3's on 50,177 of 50,177 equivalence units.
- At the close of C9f (2026-10-29) it was 50,176. Unit 027977 timed out under gate load (identical when run alone), and the author accepted that.
- Every full run since CO3 has had 50,177 identical units. The last one was at the end of CO5: `{"units":50177,"identical":50177,"differing":0,"crashed":0,"outcomeMismatch":0,"library":"identical"}`.

**Programs.** Each of the five benchmark repositories, checked single-threaded as one program, gives diagnostics line-identical with tsc 6.0.3's (`check-equivalence.mjs`): vscode 101,493, sentry 237,529, bluesky 33,337, playwright 30,808, tldraw 51,900.

**tuning-check.** The tuning corpus as one program, without the 7 units that tsc 6.0.3 itself cannot check together (author's decision in C12), is also identical with tsc: 135,770 diagnostics.

**Parallel diagnostics differ, as in tsc.** With four checkers the parallel diagnostics differ from the single-threaded ones (tuning-check 130,435). The difference is inherent to tsc's structure:
- tsc reports some errors of a merged symbol at all its declarations while checking the first file; with four checkers, the other files land in another checker.
- Message texts follow the order in which each checker created its types.

They are stable from run to run, the same under Workstation and Server GC, and every optimization step kept them unchanged.

## Verdict and additional runs (one program per repository, parse+bind+check, `cs-o-aot-ws-check` vs `tsgo7-check`)

| Run (harness commit) | Steps since | vs typescript-go (G) | single vs single | Server GC (98.75 %) | vs tsrs (secondary) | single vs tsrs single |
|---|---|---:|---:|---:|---:|---:|
| **Checker endpoint** (b42ecc2) | C0–C11 | **1.680 [1.675, 1.689]: no parity** | 0.954 | 1.055 | 8.57 | 2.007 |
| Additional 1 (7ec02cf) | CO1–CO3 | 1.068 [1.064, 1.070] | 0.823 | 0.914 | 5.388 | 1.731 |
| Additional 2 (db97ac0) | CO4 | **1.025 [1.020, 1.030]** | **0.818** | **0.850** | 5.185 | 1.705 |

All three runs used profile `check` (§17), passed the rig checks and passed the variance gate (binding rows: tsgo7-check-single, cs-o-aot-ws-check-single).
- **The endpoint** is the first protocol-compliant run after Gate C passed. It measured the unoptimized port: single-threaded it was already faster than typescript-go, but its parallel run was held back by allocation (vscode 20.2 GB, 13.0 s of Workstation GC pause).
- **The additional runs** are published next to the endpoint and never replace it.
- **CO5** was measured on tuning-check only (parallel 0.993, single 0.989) and has no additional run.

Last run, per repository, against typescript-go:

| | vscode | sentry | bluesky | playwright | tldraw |
|---|---:|---:|---:|---:|---:|
| ws, parallel | 1.037 | 0.796 | 1.217 | 1.238 | 0.910 |
| Server GC, parallel | 0.818 | 0.608 | 1.062 | 1.086 | 0.776 |

vscode in the last run:

| | Parallel | Single-threaded | Peak working set |
|---|---:|---:|---:|
| Track O, Workstation GC | 15.01 s | 37.95 s | 4,796 MiB |
| Track O, Server GC | 11.84 s | | |
| typescript-go | 14.48 s | 42.72 s | 6,991 MiB |
| tsrs | 2.28 s | | 2,746 MiB |

Track O allocates 16.0 GB on vscode, with 3.9 s of Workstation GC pause.

## What may be claimed

- **Checker endpoint:** no parity (G = 1.680, lower bound above 1.20). Parse+bind+check of a program with four checkers is "more than 20 % slower than typescript-go 7.0.2".
- **Endpoint, secondary:**
  - single-threaded, faster than typescript-go (0.954);
  - Server GC, within 20 % of typescript-go (1.055, 98.75 %).
- **From additional runs (labeled as such):**
  - within 20 % of typescript-go with four checkers (1.025, upper bound 1.030);
  - single-threaded about 18 % faster (0.818);
  - with Server GC faster than typescript-go (0.850, upper bound 0.855).
- **Not claimed:**
  - parity on the small programs bluesky and playwright: both lower bounds are above 1.20 under Workstation GC;
  - anything near tsrs (5.19 times slower with four against its 16 checkers, 1.71 single-threaded).

## Disclosed differences

- typescript-go's and tsrs's checkers port TypeScript 7 and differ from 6.0.3's in places; their diagnostic counts differ.
- All sides check with TypeScript 6.0.3's default options, passed explicitly to typescript-go and tsrs.
- The repositories' dependencies are not installed, so only imports between program files resolve, identically on all sides.
- **Checker pools:** Track O and typescript-go use 4 checkers with the fixed assignment file i → checker i % 4; tsrs uses 16 checkers with work stealing.
- The parallel C# diagnostics differ from the single-threaded ones (above). The time measured is the same work either way.
- The checker endpoint and its additional runs report the NativeAOT runtimes only. JSDoc parsing mode and code generation are as in Phase 1 (docs/references.md §2a).
- Windows only; the Linux replication (A4) has not been run.

## Modules and steps

| Module / step | What | Result |
|---|---|---|
| P1 | printer gate (the oracle prints types and nodes through tsc's printer) | 50,177 / 50,177 |
| C0 | checker oracle (`dump-ts-checker.mjs`, types mode), compare, `gate.mjs --phase checker`, program layer, checker skeleton | Gate C 1,607 (empty checker) |
| C1–C5, C7 | core (symbols, declared types, globals), type printing, instantiation, relations, expressions, control flow and narrowing | stepwise |
| C6 | type inference; lazy diagnostics made eager as in tsc | 37,504 |
| C8 | statements and declarations | 39,740 |
| C9b | JSX | 46,330 |
| C9c | general TS/TSX expression and flow typing (optional chains, late-bound names, import.meta, default type arguments) | 49,125 |
| C9d | JavaScript and JSDoc | 49,905 |
| C9e | decorators, lib hints, grammar, remaining types | 50,140 |
| C9f + 160d746 | synthetic element access, `pushFakeScope` undo, last units | **Gate C passes** (50,176, timeout accepted) |
| C10 | module resolution between program files, `--check` drivers (C#, typescript-go), profile `check` | §17 accepted, `prereg/v9` |
| C11 | module interop of files, deferred nodes per file, tsrs check mode | 5 repositories identical with tsc |
| C12 | the tuning corpus as one program: tuning-check list, lazy diagnostics deferred as in tsc, global diagnostics once in the pool | tuning-check identical with tsc |
| CO1 | checker scaling and allocations: per-checker duplicate reports, property caches | tuning-check parallel 0.729, single 0.769 |
| CO2 | reference-free checker data: names as ids, link slots, value-keyed caches, type parameter records | 0.800 / 0.932 |
| CO3 | object types as records, member lists and symbol tables in arenas | 0.926 / 0.980 |
| CO4 | parallel check of small programs: per-item checker construction, imports resolved in parallel, pool checkers keep only their own files' diagnostics | 0.964 / 1.011: single slower, kept by the author as an exception (AOT code placement) |
| CO5 | Workstation GC pause on small programs: account of the pause, fewer allocations of work in progress | 0.993 / 0.989; small programs 0.98, target not reached |

From CO1 to CO5, tuning-check `cs-o-aot-ws-check` went from 27.57 s to 14.02 s and `--single` from 30.54 s to 21.52 s (separate measurements, each against its own baseline).

**NoAlloc after CO5:** Track O 2,440 of 6,284 methods annotated, the checker 66 of 2,747. There are 55 `[NoAllocUnchecked]` exceptions, none in the checker or the program layer.

## What is left

- **The Workstation GC pause on small programs** is the main remaining gap. Under Server GC, bluesky and playwright are within 9 % of typescript-go. CO5's account:
  - 97 % of the promoted bytes are reached through cards, i.e. new checker data stored into long-lived stores.
  - The largest group of surviving objects with references is the type mappers (about 190K per small program).
  - Candidates:
    - mappers as struct records of a per-checker store; CO3's partial attempt cost CPU;
    - the duplicate-declaration info that every checker builds in construction (about 8 MB);
    - the path strings of module resolution (`Core/`).
- **Imbalance at the end of the pool:** the expensive files (lib.dom.d.ts) sit on fixed checkers. Nothing can be gained without changing the i % 4 assignment, which fixes the parallel diagnostics.
- **The tsrs gap.**
- **The Linux replication (A4).**

## Process notes

- **Races on shared stores.** Two races wrote into a file's node store that the four checkers share, both found by crashes in protocol runs and fixed before the measured runs:
  - b42ecc2: the jsDoc lists of getJSDocTags;
  - 7ec02cf: `modifierFlagsCache`.

  The rule since then: nothing writes into a file's store while checkers run.
- **tuning-check.** C12's first attempt found that tsc 6.0.3 itself crashes on the whole tuning corpus as one program; the C# crash was a faithful port. The author chose the tuning-check list instead (883d16c).
- **Gate C's history:**
  - the 027977 timeout was accepted when Gate C first passed;
  - C9e ended short of passing; C9f and the orchestrator's 160d746 completed it.
- **Single-threaded losses in CO4 and CO5.**
  - CO4's single-threaded loss (1.1 %) violated the keep rule. After a follow-up (a no-op branch moves it, the JIT build shows none), the author kept CO4 as a documented exception.
  - CO5 reverted a step whose single-threaded loss could not be shown to be code placement.
- From 2026-10-25, one agent ran at a time, working directly on `main`.
