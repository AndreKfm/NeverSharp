# NeverSharp: overall report v1.2 (2026-11-03) — Windows only

This report collects the results of the project, from the pre-registration (`prereg/v1`, 2026-10-21) to the last amendment (`prereg/v10`, 2026-11-02).

**v1.1** (2026-11-03) adds the optimization step CO6 and the two additional runs after it (profile `check` and profile `check12`). No endpoint changes. v1 was f157691.

**v1.2** (2026-11-03) corrects §8, §9 and §11 after the analysis TG1 of the tsrs gap ([tsrs-gap.md](tsrs-gap.md)): what the gap consists of, tsrs's locality assignment and assignment-independent output, and its driver build without PGO. No numbers of the protocol change.

NeverSharp is a C# port of the TypeScript 6.0.3 compiler front end: scanner, parser, binder and type checker. It is measured against typescript-go 7.0.2, Microsoft's Go port, under a pre-registered protocol ([preregistration.md](preregistration.md)). The detailed sources are:
- the phase summaries: [track-o-summary.md](track-o-summary.md), [phase2-summary.md](phase2-summary.md) and [phase3-summary.md](phase3-summary.md);
- the progress log [phase1-progress.md](phase1-progress.md);
- the result directories under `results/`, each with run.json, report.md, the raw hyperfine exports and a VERDICT.md or NOTE.md.

Nothing in this report is a new measurement. Statements marked **post hoc** are interpretations made after seeing the data.

## 1. The question and the short answer

The pre-registered question (§1) asks:

> Does a faithful C# port of tsc land within 20 % of typescript-go's wall-clock on Microsoft's benchmark corpus?

**For parsing, no.**
- **Track F, the faithful port**, follows typescript-go's porting rules: no optimization without a typescript-go counterpart. It parses **6.31 [6.20, 6.41]** times as slow as typescript-go. This is the only pre-registered verdict on the question.

**A second question was registered separately.** How fast can a performance-optimized C# port of the same compiler be? Track O answers it, and it is never reported in place of the Track F verdict.
- **Parsing:** Track O parses faster than typescript-go (endpoint G = **0.60**).
- **Binder and checker:** Track O was extended to the binder (Phase 2) and the checker (Phase 3).
  - The binder endpoint is **inconclusive** (1.207).
  - The checker endpoint is **no parity** (1.680).
  - Both endpoints measured the unoptimized ports. Later additional runs, labeled as such, put parse+bind at 0.425 and parse+bind+check at 0.970 of typescript-go's time.

All numbers below are from one machine: AMD Ryzen 9 3900X, 12 cores / 24 threads, fixed all-core clock ([hardware.md](hardware.md)). All are Windows only; the Linux replication (§8, A4) has not been run and was deferred by the author on 2026-11-01.

## 2. Results at a glance

G is the geometric mean over the five repositories of median(C#) / median(reference), with a 95 % percentile bootstrap interval (seed 20261021). Values below 1 mean the C# side is faster. The decision rule (§8):

| Interval | Verdict |
|---|---|
| upper bound ≤ 1.20 | within 20 % |
| lower bound > 1.20 | no parity |
| contains 1.20 | inconclusive |
| upper bound < 1.00 | faster |

**Pre-registered endpoints.** Each is the first protocol-compliant run after its equivalence gate passed.

| Scope | Comparison | G [95 % CI] | Verdict | Repositories above 1.20 | Peak working set (G) | Source |
|---|---|---|---|---|---:|---|
| Parsing, faithful (Track F) | `cs-aot-ws` vs `tsgo7-parse` | **6.31 [6.20, 6.41]** | **no parity** | all five | 1.14 | `results/m6/2026-10-22T07-01-02-524Z/` |
| Parsing, optimized (Track O) | `cs-o-aot-ws` vs `tsgo7-parse` | **0.60 [0.59, 0.61]** | **faster** | none | 0.45 | `results/track-o/endpoint/2026-10-22T18-55-52-105Z/` |
| Parse+bind, optimized | `cs-o-aot-ws-bind` vs `tsgo7-bind` | **1.207 [1.191, 1.219]** | **inconclusive** | sentry | 1.09 | `results/phase2/binder-endpoint/2026-10-24T11-21-53-808Z/` |
| Parse+bind+check of a program, optimized, 4 checkers | `cs-o-aot-ws-check` vs `tsgo7-check` | **1.680 [1.675, 1.689]** | **no parity** | all five | 0.93 | `results/phase3/checker-endpoint/2026-10-29T14-41-57-488Z/` |

**Last additional runs.** These are published next to the endpoints and never replace them.

| Scope | Comparison | G [95 % CI] | Reading | Repositories above 1.20 | Peak working set (G) | Source |
|---|---|---|---|---|---:|---|
| Parsing, optimized | vs `tsgo7-parse` | 0.40 [0.39, 0.40] | faster | none | 0.43 | `results/track-o/additional/2026-10-24T06-41-11-991Z/` |
| Parse+bind, optimized | vs `tsgo7-bind` | 0.425 [0.419, 0.431] | faster | none | 0.53 | `results/phase2/additional/2026-10-25T05-13-51-186Z/` |
| Parse+bind+check, optimized, 4 checkers | vs `tsgo7-check` | 0.970 [0.965, 0.976] | faster | none | 0.69 | `results/phase3/additional/2026-11-02T22-22-25-874Z/` |

**Pre-registered secondary comparison, §18 (`prereg/v10`), 12 checkers on both sides.**

| Comparison | G [CI] | Reading | Repositories above 1.20 | Peak working set (G) | Source |
|---|---|---|---|---:|---|
| Workstation GC, `cs-o-aot-ws-check12` vs `tsgo7-check12` | 1.305 [1.292, 1.315] | no parity | vscode, bluesky, playwright, tldraw | 0.77 | `results/phase3/check12/2026-11-02T06-16-29-174Z/` |
| Server GC, `cs-o-aot-srv-check12` vs `tsgo7-check12`, 98.75 % | 0.890 [0.878, 0.900] | faster | none | 0.93 | same run |
| Additional run after CO6, Workstation GC | 1.236 [1.226, 1.244] | no parity | vscode, bluesky, playwright, tldraw | 0.77 | `results/phase3/check12-additional/2026-11-02T20-56-02-668Z/` |
| Additional run after CO6, Server GC, 98.75 % | 0.889 [0.878, 0.897] | faster | none | 0.94 | same run |

Peak working set (G) is the geometric mean over the repositories of the C# median peak working set divided by the reference's. It was computed for this report from the archived run.json files.

## 3. Protocol in brief

- **References,** pinned by commit:
  - the port source and first oracle is TypeScript `v6.0.3` (`050880ce`);
  - the baseline and second oracle is typescript-go `7.0.2` (`2bd066d8`).
  - From `prereg/v5` on, the secondary reference is tsrs 0.18.0, a Rust port of TypeScript 7 (7.1.0-dev).
- **Benchmark corpus:** the five codebases of Microsoft's TypeScript 7 announcement: VS Code, Sentry, Bluesky, Playwright, tldraw. Each is pinned by a mechanical commit rule. The corpus is used only for the protocol runs.
- **Tuning corpus:** TypeScript's `tests/cases` plus its own sources. Every speed-dependent choice was made on it.
- **Equivalence:** 50,177 units, which are TypeScript's test cases plus every benchmark file. Each phase has a byte-identical gate against tsc 6.0.3, with no tolerance:
  - Gate 1: AST dumps, including JSDoc.
  - Gate B: binder dumps, in both JSDoc modes.
  - Gate C: diagnostics, types and symbols dumps.
- **Measurement:**
  - hyperfine, 20 timed runs after 3 warmups;
  - drivers in the high priority class, peak memory from 5 separate runs;
  - rig checks: fixed processor state, no boost, idle before each run;
  - a variance gate on the binding single-threaded rows: MAD/median < 3 %.
- **One final measurement:** the endpoint is the first protocol-compliant run after the gate passes. Additional runs never replace it.
- **Timestamps:** every pre-registration tag (`prereg/v1` … `v10`) has an OpenTimestamps proof anchored in Bitcoin ([timestamps/README.md](timestamps/README.md)). The repository is private until publication.

**Kill criteria (§9):** none was triggered.
- The oracles agreed under the pre-registered normalization: 50,092 units identical, 85 listed divergences.
- Track F passed Gate 1 on 2026-10-21, inside the 18-week budget.
- The variance gate passed in every deciding run.

## 4. Phase 1: parsing

### Track F (faithful)

Track F ports `scanner.ts` and `parser.ts`, with the parts of `types.ts`, `utilities.ts` and the diagnostic messages they need. It follows the port rules of §5: no optimization without a typescript-go counterpart, UTF-16 strings, classes as in the source.

The work was done in modules M1–M5, each in several attempts by agents. The attempts were ranked on the tuning corpus (`results/ranking/`). Gate 1 passed on all 50,177 units on 2026-10-21.

**Verdict (M6 run 3, 2026-10-22, after `prereg/v4`).**

> Parsing, faithful track: the C# port takes 6.31× [6.20, 6.41] the wall-clock of typescript-go 7.0.2. The geometric mean is over Microsoft's five benchmark repositories: vscode 8.41, sentry 7.17, bluesky 4.54, playwright 6.18, tldraw 5.92; all five are above 1.20. Peak working set is 1.14× typescript-go's. Windows only.

For comparison, tsc 6.0.3 itself takes 11.90×, so Track F parses about 1.9 times as fast as tsc. Single-threaded, Track F is about 2.2 times as slow as typescript-go.

Post hoc: a faithful object-per-node port allocates heavily, 3.3 GB on vscode with 6.8 s of GC pause in total. Most of the parallel gap is the garbage collector.

### Track O (optimized)

Track O started as a copy of Track F (step O0). It may use optimizations without a typescript-go counterpart (§5.4): SIMD, `stackalloc`, NoAlloc, arenas, and at most 3 measured variants per step. It passes the same Gate 1.

**Endpoint (2026-10-22, after O0–O1).**

> Parsing, optimized track: the C# port takes 0.60× [0.59, 0.61] the wall-clock of typescript-go 7.0.2. Per repository: vscode 0.54, sentry 0.62, bluesky 0.62, playwright 0.61, tldraw 0.62; none is above 1.20. Peak working set is 0.45× typescript-go's. Windows only.

**Additional runs.** Six additional runs followed the steps O9 to O29, each run after its steps. They brought G to **0.40 [0.39, 0.40]** against typescript-go. Against tsrs the last run was **1.04 [1.03, 1.05]**: within 20 %, but not faster, because the lower bound is above 1.00.

On vscode alone, Track O was below tsrs in four consecutive runs (0.92–0.95). This is reported per repository, not claimed.

On vscode in the last run, Track O allocates 65 MB with 26 ms of GC pause. Track F allocates 3.3 GB with 6.8 s.

**Steps.** The evaluated and not-evaluated steps (A9) are in [track-o-steps.md](track-o-steps.md) and [track-o-summary.md](track-o-summary.md). Not evaluated: O4 (literals), O7b (node-list layouts), O8 (parallel scheduling).

## 5. Phase 2: binder (Track O only)

The binder of TypeScript 6.0.3 was ported for Track O only, by the author's decision. The amendments are `prereg/v7` (§15) and `prereg/v8` (§16).
- **Equivalence:** Gate B passes on 50,177 of 50,177 units in both JSDoc modes. Symbol totals match tsc on all corpora.

**Endpoint (2026-10-24, the unoptimized port after B0–B4).**

> Parse+bind, optimized track: 1.207× [1.191, 1.219] the wall-clock of typescript-go 7.0.2, so **inconclusive**. Per repository: vscode 1.12, sentry 1.35, bluesky 1.24, playwright 1.12, tldraw 1.22. Only sentry's interval lies entirely above 1.20. Peak working set is 1.09× typescript-go's. Windows only.

Single-threaded the port was already even with typescript-go (0.985). The parallel gap was allocation: 3,984 MB on vscode.

**Additional runs.** Four optimization steps followed, BO1 to BO4. Afterwards parse+bind was at **0.425 [0.419, 0.431]** against typescript-go and **1.146** against tsrs, which is within 20 % but not faster.

## 6. Phase 3: type checker (Track O only)

The checker of TypeScript 6.0.3 (`checker.ts`) was ported for Track O only, about 71,000 lines of C#, together with the program layer it needs. The amendment is `prereg/v9` (§17). It replaced §10's placeholder, a "monthly conformance line", with a gate and an endpoint.
- **Gate C passes:** diagnostics, types and symbols dumps are byte-identical with tsc 6.0.3 on 50,177 of 50,177 units. At first close it was 50,176, with one timeout under load that the author accepted. Every full run since CO3 has had all 50,177 identical.
- **Programs:** the five repository programs, checked single-threaded, give diagnostics line-identical with tsc 6.0.3.
  - Measured as one program per repository: parse+bind+check of every file plus the default library, with TypeScript 6.0.3's default options.
  - Dependencies are not installed, so only imports between program files resolve, the same on all sides.
  - typescript-go uses its own checker pool: 4 checkers, file *i* to checker *i* mod 4. Track O ports that pool.

**Endpoint (2026-10-29, the unoptimized port after C0–C11).**

> Parse+bind+check of a program, optimized track, 4 checkers: 1.680× [1.675, 1.689] the wall-clock of typescript-go 7.0.2, so **no parity**. Per repository: vscode 1.752, sentry 1.335, bluesky 1.901, playwright 2.008, tldraw 1.499; all five are above 1.20. Peak working set is 0.93× typescript-go's. Windows only.

Secondary results of the endpoint run:
- single-threaded, faster than typescript-go (0.954);
- Server GC, within 20 % (1.055 at 98.75 %).

The parallel gap was again allocation: 20.2 GB on vscode, with 13.0 s of Workstation GC pause.

**Additional runs.** The optimization steps CO1 to CO6 followed. The last additional run (after CO6, 2026-11-03) gave:
- 4 checkers: **0.970 [0.965, 0.976]**, faster (after CO4: 1.025);
- single-threaded: **0.800**;
- Server GC: **0.827**, faster.

No repository is above 1.20 any more; bluesky and playwright are at 1.168. CO6 stored the type mappers as records instead of objects and the duplicate-declaration data without objects. On vscode the Workstation GC pause at 4 checkers fell from 3.9 s to 3.3 s.

**12 checkers (§18, `prereg/v10`).** The unregistered exploration CN1 tried 4, 8, 12 and 16 checkers. Its predictions were ws 1.310 and srv 0.890 at 12 checkers, and the protocol run reproduced both.
- With **Workstation GC, no parity: 1.305** [1.292, 1.315].
- With **Server GC, faster: 0.890** [0.878, 0.900].

From 4 to 12 checkers:
- typescript-go gains 33–44 %;
- Track O gains 25–43 % with Server GC;
- Track O gains only 15–24 % with Workstation GC.

On vscode with 12 checkers and Server GC, Track O takes 6.76 s against typescript-go's 8.85 s.

The additional run with 12 checkers after CO6 gave Workstation GC **1.236 [1.226, 1.244]**, still no parity, and Server GC 0.889, unchanged. On vscode the Workstation GC pause fell from 5.80 s to 5.05 s of 10.85 s.

## 7. What may be claimed

Every claim names the track and scope, and gives G with its interval.

**Faithful track:**
- Parsing is more than 20 % slower than typescript-go: 6.31 [6.20, 6.41]. This is the answer to the pre-registered question.

**Optimized track, endpoints:**
- Parsing is faster than typescript-go: 0.60 [0.59, 0.61].
- Parse+bind is inconclusive: 1.207.
- Parse+bind+check of a program with 4 checkers is more than 20 % slower: 1.680.

**Optimized track, labeled additional runs:**
- Parsing is about 2.5 times as fast as typescript-go: 0.40.
- Parse+bind is about 2.4 times as fast: 0.425.
- Parse+bind+check with 4 checkers is faster than typescript-go: 0.970 [0.965, 0.976], no repository above 1.20, peak working set 0.69.
- Single-threaded, the check is about 20 % faster (0.800).
- With Server GC, the check is faster (0.827).

**Optimized track, §18 secondary:**
- With 12 checkers on both sides, Server GC is faster than typescript-go: 0.890.
- With 12 checkers on both sides, Workstation GC is more than 20 % slower: 1.305 (additional run after CO6: 1.236).

**Against tsrs, secondary:**
- Parsing is within 20 % (1.04).
- Parse+bind is within 20 % (1.146).

**Not claimed:**
- that the faithful port is near typescript-go;
- that the optimized checker matches typescript-go at its pre-registered endpoint;
- that any Track O result is faster than tsrs in any phase;
- that the checker is near tsrs. After CO6 it is 3.9–4.9 times as slow with Workstation GC and 2.8 times with Server GC at 12 checkers;
- anything about Linux.

## 8. Why, in short (post hoc)

- **Where the time went.** In every phase, the unoptimized C# port was about as fast as typescript-go single-threaded, or faster. The parallel gap was allocation: with Workstation GC, gen0 and gen1 collections stop all worker threads and are collected by one thread.
  - Parsing and binding became nearly allocation-free with arenas and records in per-thread memory. vscode parse+bind: 89 MB allocated, 50 ms of GC pause.
  - The checker still allocates 13.4 GB on vscode at 4 checkers. CO5 and CO6 showed that the pause follows the surviving objects with references, not the allocated bytes. CO6 removed the largest group, the type mappers.
- **Why Server GC differs.** Server GC collects in parallel, which explains its gap to Workstation GC.
  - The gap grows with the checker count: 3.3 s of pause at 4 checkers, 5.05 s at 12, on vscode after CO6.
  - Go's collector marks concurrently and does not move objects, so typescript-go keeps scaling.
- **What C# gives an optimized port.** The C# advantages used were value types and spans, arenas in native or pinned memory, SIMD, NativeAOT and the NoAlloc analyzer. They make the optimized parser and binder faster than typescript-go. A faithful port, whose representation follows TypeScript's object graph, cannot use them.
- **The tsrs gap in the checker** was analyzed in TG1 ([tsrs-gap.md](tsrs-gap.md); exploratory measurements, not protocol runs).
  - tsrs does the same checking work: same programs including the default library, and types and symbols within 1–4 % once its lazy members are off. Its switchable shortcuts are worth at most 2–3 %.
  - Single-threaded, parse and bind cost the same on both sides. The whole gap is the check phase, about 2 times as slow per operation in Track O (geometric mean 1.98).
  - Parallel, about half of the gap (in log terms) is per-checker speed, not the pool. At typescript-go's pool settings (4 checkers, file *i* to checker *i* mod 4, no stealing), tsrs is still 2.49 times as fast as Track O ws and 1.97 times as fast as srv on vscode.
  - The rest on vscode: Workstation GC pause 1.26, 16 instead of 4 checkers 1.76, tsrs's assignment by directory locality 1.22, work stealing 1.18.
  - typescript-go itself is 3.2 times as slow as tsrs with 12 checkers.

## 9. Disclosed differences

- **Versions:**
  - The C# tracks port TypeScript 6.0.3.
  - typescript-go 7.0.2 ports a TypeScript 7 branch, and tsrs ports TypeScript 7.1.0-dev.
  - Their binders and checkers differ from 6.0.3's in places, so symbol totals and diagnostic counts differ slightly.
- **JSDoc:**
  - The C# tracks parse as tsc's host does (`ParseForTypeErrors`).
  - typescript-go parses all JSDoc eagerly. Parsing all JSDoc costs Track O about 4 %.
  - tsrs parses JSDoc in `.ts` files lazily.
- **Code generation:** each side is built as its project builds it.
  - Go and Rust target the x86-64 baseline; NativeAOT targets x86-64-v2.
  - No build uses AVX2; an x86-64-v3 build of Track O was not faster.
  - The tsrs driver is built with fat LTO but without the PGO (and, on Linux, BOLT) of tsrs's release builds. The measured tsrs is therefore slower than the shipped one, by up to about 13 % by tsrs's own measurement.
- **Checker pools:**
  - Track O and typescript-go use a fixed assignment, 4 checkers (12 in §18).
  - tsrs uses 16 checkers on this machine, assigns files by directory locality, and steals work.
  - With more than one checker, the diagnostics of Track O and typescript-go depend on the assignment. This is inherent to tsc's structure: tldraw has 52,608 diagnostics in typescript-go at 12 checkers against 52,604 single-threaded.
  - tsrs's default output does not depend on the assignment, by design, apart from known cases; only its `go` mode behaves like typescript-go.
- **Runtimes:**
  - The checker and binder endpoints report the NativeAOT runtimes only (§16).
  - The primary C# configuration has been NativeAOT with Workstation GC, parallel, since `prereg/v1`. Server GC rows are secondary.

## 10. Deviations, decisions after data, and process

**Amendments.** Every amendment is in §12.2, tagged and timestamped before the measurement it could affect.
- **Errata or new references, not decided after seeing data:**
  - v2: the MSBuild property name for Server GC.
  - v5: tsrs as a secondary reference.
- **Process rules, not decided after seeing data:**
  - v3: attempts and models per module.
  - v6: the NoAlloc allowlist.
  - v8: measurement profiles.
- **Scope extensions:**
  - v7: binder.
  - v9: checker.
  - v10: 12 checkers.
- **Decided after seeing data:**
  - **v4,** the variance gate binding fewer rows. It was adopted after M6 runs 1 and 2 failed it only in JIT and Server GC configurations. Those runs carry no verdict, and their primary numbers were 6.45 and 6.28.
  - **v10,** 12 checkers. N = 12 was chosen after the unregistered exploration CN1. This is disclosed in §18, and the measurement is secondary.

**Author's decisions during optimization.**
- O23 was decided on a repeat ranking because of a tie.
- CO4 was kept although its single-threaded time was 1.1 % slower, attributed to the placement of AOT code.
- A CO5 step was reverted.
- CO6 was kept although Server GC on the tuning list was 0.4 % slower [0.2, 0.8 %]. Server GC is not in the keep rule, which binds the three Workstation GC configurations.
- Gate C's first pass accepted one timeout under load.

**Runs not used,** all archived:
- Track O ranking runs:
  - O12/O24 run 2: the idle check failed.
  - O25/O26 run 2: an orchestration error.
- The first checker endpoint attempt was aborted by a crash. Four defects, among them a race on a shared store, were fixed and passed Gate C before the deciding run.

**Defects found by measurement:**
- Two races in which checkers wrote into a shared file store (b42ecc2, 7ec02cf).
- A crash reachable with 8 or more checkers (6bd5cd6): a global diagnostic was created at no node instead of as tsc's `createError`.

All were fixed with a full Gate C.

**Workflow:**
- Opus 5.5 orchestrated; ports and optimization steps were done by agents in separate attempts.
- From 2026-10-25, one agent ran at a time, directly on `main`.
- Gates and the rig checks were rerun by the orchestrator before every merge and every protocol run.

## 11. Open

- **Linux replication (A4, §8):** deferred by the author. Until it exists, every result is "Windows only".
- **The Workstation GC pause in the checker:** still the main reason for the gap with 12 checkers (Server GC with the same code: 0.889). Not pursued after CO6, by the author's decision. Candidates:
  - the globals merge, which every checker repeats; merged once before the checkers start, it would also let the checkers share the duplicate-declaration data;
  - inference contexts kept alive by the mapper store;
  - module-resolution strings.
- **The tsrs gap in the checker:** about 4–5 times; account and ranked candidates in [tsrs-gap.md](tsrs-gap.md) (TG1). Not implemented: the author closed the checker work after TG1 (2026-11-03); the TG2 brief was withdrawn before any change. Locality assignment and work stealing would change the parallel diagnostics and need the author's decision and an amendment.
- **Track O steps not evaluated:**
  - parser: O4, O7b, O8;
  - checker: dynamic scheduling, which would change the parallel diagnostics.
- **Out of scope for v1 (§10):** emitter, language service, incremental parsing, tsserver, public API.

## 12. Timeline

| Date | Milestone |
|---|---|
| 2026-10-14 | Phase 0 starts: harness, corpora, oracles, pins |
| 2026-10-21 | `prereg/v1`; Track F Gate 1 passes |
| 2026-10-22 | Track F verdict (6.31, no parity); Track O endpoint (0.60, faster) |
| 2026-10-24 | Track O closed (last 0.40); `prereg/v7`, `v8`; binder endpoint (1.207, inconclusive) |
| 2026-10-25 | Phase 2 closed (last 0.425) |
| 2026-10-29 | Gate C passes; `prereg/v9`; checker endpoint (1.680, no parity) |
| 2026-11-01 | Phase 3 closed (last 1.025) |
| 2026-11-02 | `prereg/v10`; 12-checker run (ws 1.305, srv 0.890); CO6 |
| 2026-11-03 | Additional runs after CO6: 4 checkers 0.970 (faster), 12 checkers 1.236; checker optimization closed; TG1 (tsrs gap analysis) |

The 18-week budget for Phase 1 (to 2027-02-24) was not needed. Every phase finished inside it.

## 13. Reproducing

- Pins: `pins.json` and `global.json`. Fetch the references with `node scripts/fetch-references.mjs`.
- Builds: `harness/bench/build-cs.mjs`, `build-go.mjs` and `build-rs.mjs`.
- Gates: `node harness/port/gate.mjs --track optimized --phase parser|binder|checker`; Track F: `--track faithful`.
- Protocol runs: `node harness/bench/bench.mjs --profile full|binder|check|check12`, then `node harness/bench/report.mjs <run.json>` ([benchmark-harness.md](benchmark-harness.md)).
- Verify a timestamp: `node scripts/ots.mjs verify docs/timestamps/prereg-vN.txt.ots`.
