# Phase 4: emitter — summary (2026-11-04) — Windows only

Phase 4 ported the emitter of TypeScript 6.0.3 for the optimized track (Track O) only. It covers `.js` and `.d.ts` output under TypeScript 6.0.3's default options plus `declaration: true`. The author decided this on 2026-11-03.
- **Plan:** [phase4-plan.md](phase4-plan.md).
- **Layout:** [phase4-emitter-layout.md](phase4-emitter-layout.md).
- **Amendment:** `prereg/v11` (§19: scope, Gate D, endpoint, profile `emit`). It was accepted and tagged after Gate D passed, before any emit measurement. Its OpenTimestamps proof is anchored in Bitcoin block 974431.
- **Details of every module:** [phase1-progress.md](phase1-progress.md), section "Phase 4".

**Options:**
- target ES2025, so the module kind is ES2022;
- no source maps;
- comments kept;
- `declaration: true`.

**Transformers ported:**
- TypeScript, ESNext, ES decorators, class fields, the ES module transformer and declarations;
- the CommonJS module transformer for `.cts`/`.cjs` files, which the author added on 2026-11-03;
- the emit resolver, the emit helpers, comment emission, and the node builder's declaration mode.

The emitter has about 17,500 lines of C# in `Emitter/`. Phase 4 added about 29,000 lines to Track O in all, checker and printer included.

## Equivalence

**Gate D passes.** Every equivalence unit is emitted as a one-file program. The canonical emit dumps of Track O are byte-identical with tsc 6.0.3's on 50,177 of 50,177 units: every output file, the emit diagnostics and `emitSkipped`.

| Section | Identical | Differing |
|---|---:|---:|
| js | 50,173 | 0 |
| dts | 50,173 | 0 |
| diagnostics | 50,173 | 0 |

- **No output:** the four units on which tsc 6.0.3 itself fails with a `Debug Failure` (000863, 000913, 002379, 008381) fail in Track O at the same ported assertion. They have no sections.
- **Symbol ids:** tsc's process-wide symbol id inside two kinds of generated names is replaced by a placeholder on both sides (author's decision). This changes 1 unit and 2 lines.
- **Other gates:** Gate C and the printer gate (both comment modes) still pass.

**Programs.** Each of the five benchmark repositories is emitted single-threaded as one program. The output is byte-identical with tsc 6.0.3's: every output file, the emit diagnostics and the check diagnostics (`emit-equivalence.mjs`).

| Program | Output files | Output bytes | Emit diagnostics |
|---|---:|---:|---:|
| vscode | 22,942 | 161,946,338 | 21 |
| sentry | 17,840 | 47,395,141 | 2 |
| bluesky | 3,373 | 7,298,176 | 1 |
| playwright | 2,670 | 13,474,595 | 3 |
| tldraw | 4,948 | 15,945,686 | 19 |
| tuning emit list | 41,681 | 39,725,430 | 859 |

The tuning emit list leaves out 139 units, because tsc 6.0.3 itself cannot emit the whole tuning list as one program. It was used for tuning only.

**Parallel emit depends on the number of checkers, as in typescript-go.**
- With several checkers, the `.d.ts` output and the emit diagnostics differ from the single-threaded ones. Causes:
  - union member order from per-checker type ids;
  - visibility marked by earlier files' declaration emit, which stays inside the checker that emitted them.
- The `.js` output does not depend on it.
- On the five repositories, the output bytes of the C# side are the same in all four C# configurations (Table 1d).

## Endpoint (one program per repository, parse+bind+check+emit to memory, 4 checkers)

Protocol run of 2026-11-03, harness commit 9435b89, `results/phase4/emit/2026-11-03T19-39-41-764Z/` ([NOTE.md](../results/phase4/emit/2026-11-03T19-39-41-764Z/NOTE.md)).
- **Checks:** the rig checks passed. The variance gate passed (binding rows at most 0.56 %).
- **The port measured:** the first port after Gate D passed. No optimization step was made for the emitter.

| Comparison (percentile bootstrap, seed 20261021) | G [interval] | Verdict (§8) |
|---|---|---|
| **Endpoint:** `cs-o-aot-ws-emit` vs `tsgo7-emit`, 95 % | **1.030 [1.026, 1.035]** | **within 20 % of typescript-go** |
| single-threaded, `cs-o-aot-ws-emit-single` vs `tsgo7-emit-single`, 95 % | 0.821 [0.820, 0.822] | faster |
| Server GC, `cs-o-aot-srv-emit` vs `tsgo7-emit`, 98.75 % | 0.894 [0.888, 0.900] | faster |
| Server GC single-threaded, 98.75 % | 0.816 [0.814, 0.818] | faster |
| vs `tsrs-emit` (secondary reference) | 5.057 [5.032, 5.096] | no parity |
| single-threaded vs `tsrs-emit-single` | 1.630 [1.627, 1.633] | no parity |

Per repository against typescript-go:

| | vscode | sentry | bluesky | playwright | tldraw |
|---|---:|---:|---:|---:|---:|
| ws, parallel | 1.147 | 0.812 | 1.153 | 1.153 | 0.938 |
| Server GC, parallel | 0.953 | 0.666 | 1.041 | 1.045 | 0.827 |
| ws, single-threaded | 0.953 | 0.643 | 0.934 | 0.936 | 0.696 |

No repository is above 1.20. vscode, bluesky and playwright are above 1.10.

**vscode:**

| | Parallel | Single-threaded | Peak working set |
|---|---:|---:|---:|
| Track O, Workstation GC | 23.15 s | 62.21 s | 5,957 MiB |
| Track O, Server GC | 19.23 s | 61.46 s | 6,056 MiB |
| typescript-go | 20.18 s | 65.26 s | 10,976 MiB |
| tsrs | 3.25 s | 30.60 s | 3,196 MiB |

- **Allocation on vscode:** Track O allocates 19.9 GB, with 4.42 s of Workstation GC pause (2.31 s single-threaded, 0.70 s under Server GC).
- **Peak working set:** over the five repositories it is 0.56 times typescript-go's (geometric mean).

**Cost of the emit** (post hoc). Compared with the check-only medians of the last checker additional run, which is a separate run:
- **vscode:** the emit costs Track O 9.1 s under Workstation GC, typescript-go 5.9 s. The check alone was at 0.98 there.
- **The other four repositories:** the emit costs about the same on both sides.
- **Single-threaded:** the emit costs Track O more than typescript-go on vscode (25.4 against 22.6 s), and less on the other four.

## What may be claimed

- **Emitter endpoint:** within 20 % of typescript-go (G = 1.030, upper bound 1.035). Parse+bind+check+emit of a program with four checkers is "within 20 % of typescript-go 7.0.2" on Microsoft's five benchmark repositories.
- **Secondary:**
  - single-threaded, about 18 % faster than typescript-go (0.821);
  - with Server GC, faster than typescript-go (0.894, upper bound 0.900).
- **Not claimed:**
  - that the emit is faster than typescript-go with Workstation GC: the lower bound is above 1.00;
  - anything near tsrs: about 5 times as slow parallel, 1.63 times single-threaded;
  - anything about Linux.

## Disclosed differences

- **Versions:** typescript-go's and tsrs's emitters follow TypeScript 7 and differ from 6.0.3's in places.
  - Output bytes differ from the C# side by at most 0.2 %.
  - sentry has one output file more on their side (17,841 against 17,840).
- **Options:** all sides emit with TypeScript 6.0.3's default options, given to typescript-go and tsrs explicitly.
- **Dependencies:** the repositories' dependencies are not installed, so only imports between program files resolve, identically on all sides.
- **Work items:**
  - Track O emits one work item per checker, with that checker's files in program order.
  - typescript-go queues one work item per file and locks the file's checker per resolver call.
  - tsrs uses its own pool: 16 checkers, assignment by directory locality, work stealing.
- **Parallel check diagnostics:** in the parallel C# rows they differ slightly from the single-threaded ones (vscode 101,474 against 101,493). This is the dependence on the checker count known from Phase 3; typescript-go shows it too (tldraw 52,608 against 52,604).
- **Runtimes:** the endpoint reports the NativeAOT runtimes only.
- **Platform:** Windows only; the Linux replication (A4) has not been run.

## Modules

| Module | What | Gate D after (identical js / dts) |
|---|---|---|
| D0 | emit oracle (`dump-ts-emit.mjs`), Gate D harness, emit plumbing, comment emission in the printer | 8,105 / 4,258 |
| D1 | transformation context, visitors, factory updates, emit helpers, emit resolver; start of `ts.ts` | 41,490 / 4,258 |
| D2 | TypeScript transformer: enums, namespaces, import-equals, substitutions | 44,079 / 4,258 |
| D3 | ES module transformer, `esnext.ts` (`using`), CommonJS module transformer | 46,820 / 4,258 |
| D4 | ES decorators and class fields | 50,173 / 4,258 |
| D5 | declaration emit, its diagnostics, the node builder's declaration mode, JavaScript declarations | 50,173 / 50,170 |
| D6 | symbol-id normalization, the last three units, tsc's `checkDefined` assertion | **Gate D passes** |
| D7 | emit drivers (C#, typescript-go, tsrs), profile `emit`, Tables 1d and 2g–2i, `emit-equivalence.mjs` | 4 repositories identical |
| D8 | two declaration-emit bugs found in the repository programs | 5 repositories identical |

**Defects Gate D could not show.** D8 fixed two, found only in programs of several files. Both now have an account of why the one-file units missed them:
- **Inference-fallback reports:** reports of the syntactic type node builder that switch off the node builder's type cache (vscode `promptCrafting.d.ts`).
- **Index signatures:** `createLateBoundIndexSignatures` read the merged symbol instead of the class declaration's own. A merge across files is only possible in a program, not in a one-file unit.

## What is left

- **The Workstation GC pause on vscode:** it grows from 3.3 s in the check alone to 4.42 s with the emit. Together with the emit's extra CPU time, it is the main gap to typescript-go there. With Server GC the same code is at 0.953 on vscode.
- **The small programs bluesky and playwright** are at 1.15, as they were in the check (1.168).
- **No optimization step** was made for the emitter.
- **The tsrs gap,** as in the checker.
- **The Linux replication (A4).**

## Process notes

- **Agents:** one attempt per module, Opus 5.5 at medium effort, one agent at a time, directly on `main`. The orchestrator wrote the briefs D2–D8 and reran every gate before continuing.
- **Rule breaks:** two agents broke the rule to edit C# with the file tools only (D1, D5), one `sed` substitution each. Both were disclosed and reviewed.
- **Corrected accounts:**
  - D5's account of unit 012675 was wrong; D6 found the actual cause in alias resolution.
  - D7 found the two defects of D8 through the emit equivalence of the repository programs.
- **Author's decisions:**
  - the default options only;
  - the CommonJS transformer in scope;
  - the symbol-id normalization;
  - acceptance of §19 after Gate D and the emit equivalence.
