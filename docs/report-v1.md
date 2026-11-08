# NeverSharp: overall report v1.4 (2026-11-07) — Windows only

This report collects the results of the project, from the pre-registration (`prereg/v1`, 2026-10-21) to the last amendment (`prereg/v12`, 2026-11-06).

**The project was closed by the author on 2026-11-07** after the `prereg/v12` runs. No further optimization or measurement is planned. The items in §13 stay open as listed.

**v1.1** (2026-11-03) adds the optimization step CO6 and the two additional runs after it (profile `check` and profile `check12`). No endpoint changes. v1 was f157691.

**v1.2** (2026-11-03) corrects §8, §9 and §11 after the analysis TG1 of the tsrs gap ([tsrs-gap.md](tsrs-gap.md)): what the gap consists of, tsrs's locality assignment and assignment-independent output, and its driver build without PGO. No numbers of the protocol change. (In v1.3 these sections are numbered §9, §10 and §12.)

**v1.3** (2026-11-04) adds Phase 4, the emitter (§7, amendment `prereg/v11`), with its endpoint: within 20 % of typescript-go (1.030). The checker results are unchanged.

**v1.4** (2026-11-07) adds the work against tsrs after Phase 4 (§8): the optimization modules EO1–MM1, which were tuned on the benchmark corpus, and the amendment `prereg/v12` (§20 of the pre-registration) with its first protocol run. That run compares the program check with tsrs's kind of checker pool against tsrs: **2.15** times tsrs's time under Workstation GC and **1.83** under Server GC. It also adds the additional runs of profiles `check` (0.756 of typescript-go's time) and `emit` (0.782). The endpoints are unchanged. Sections 8–14 of v1.3 are now §9–§15.

NeverSharp is a C# port of the TypeScript 6.0.3 compiler: scanner, parser, binder, type checker and, for Track O, the emitter. It is measured against typescript-go 7.0.2, Microsoft's Go port, under a pre-registered protocol ([preregistration.md](preregistration.md)). The detailed sources are:
- the phase summaries: [track-o-summary.md](track-o-summary.md), [phase2-summary.md](phase2-summary.md), [phase3-summary.md](phase3-summary.md) and [phase4-summary.md](phase4-summary.md);
- the progress log [phase1-progress.md](phase1-progress.md);
- the result directories under `results/`, each with run.json, report.md, the raw hyperfine exports and a VERDICT.md or NOTE.md.

Nothing in this report is a new measurement. Statements marked **post hoc** are interpretations made after seeing the data.

## 1. The question and the short answer

The pre-registered question (§1) asks:

> Does a faithful C# port of tsc land within 20 % of typescript-go's wall-clock on Microsoft's benchmark corpus?

**For parsing, no.**
- **Track F, the unoptimized faithful port**, follows typescript-go's porting rules: no optimization without a typescript-go counterpart. It parses **6.31 [6.20, 6.41]** times as slow as typescript-go. This is the only pre-registered verdict on the question.

**A second question was registered separately.** How fast can a performance-optimized C# port of the same compiler be? Track O answers it, and it is never reported in place of the Track F verdict.
- **Parsing:** Track O parses faster than typescript-go (endpoint G = **0.60**).
- **Binder, checker and emitter:** Track O was extended to the binder (Phase 2), the checker (Phase 3) and the emitter (Phase 4).
  - The binder endpoint is **inconclusive** (1.207).
  - The checker endpoint is **no parity** (1.680).
  - The emitter endpoint, parse+bind+check+emit of a program, is **within 20 %** (1.030), measured after the checker optimizations.
  - The binder and checker endpoints measured the unoptimized ports. Later additional runs, labeled as such, put parse+bind at 0.425 and parse+bind+check at 0.756 of typescript-go's time.
- **Against tsrs, the Rust port** (secondary, `prereg/v12`): with tsrs's kind of checker pool, the program check takes **2.15** times tsrs's time under Workstation GC and **1.83** times under Server GC. The C# side was tuned on the benchmark corpus for this (§8).

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
| Parsing, unoptimized (Track F, faithful port) | `cs-aot-ws` vs `tsgo7-parse` | **6.31 [6.20, 6.41]** | **no parity** | all five | 1.14 | `results/m6/2026-10-22T07-01-02-524Z/` |
| Parsing, optimized (Track O) | `cs-o-aot-ws` vs `tsgo7-parse` | **0.60 [0.59, 0.61]** | **faster** | none | 0.45 | `results/track-o/endpoint/2026-10-22T18-55-52-105Z/` |
| Parse+bind, optimized | `cs-o-aot-ws-bind` vs `tsgo7-bind` | **1.207 [1.191, 1.219]** | **inconclusive** | sentry | 1.09 | `results/phase2/binder-endpoint/2026-10-24T11-21-53-808Z/` |
| Parse+bind+check of a program, optimized, 4 checkers | `cs-o-aot-ws-check` vs `tsgo7-check` | **1.680 [1.675, 1.689]** | **no parity** | all five | 0.93 | `results/phase3/checker-endpoint/2026-10-29T14-41-57-488Z/` |
| Parse+bind+check+emit of a program, optimized, 4 checkers | `cs-o-aot-ws-emit` vs `tsgo7-emit` | **1.030 [1.026, 1.035]** | **within 20 %** | none | 0.56 | `results/phase4/emit/2026-11-03T19-39-41-764Z/` |

**Last additional runs.** These are published next to the endpoints and never replace them.

| Scope | Comparison | G [95 % CI] | Reading | Repositories above 1.20 | Peak working set (G) | Source |
|---|---|---|---|---|---:|---|
| Parsing, optimized | vs `tsgo7-parse` | 0.40 [0.39, 0.40] | faster | none | 0.43 | `results/track-o/additional/2026-10-24T06-41-11-991Z/` |
| Parse+bind, optimized | vs `tsgo7-bind` | 0.425 [0.419, 0.431] | faster | none | 0.53 | `results/phase2/additional/2026-10-25T05-13-51-186Z/` |
| Parse+bind+check, optimized, 4 checkers | vs `tsgo7-check` | 0.756 [0.753, 0.759] | faster | none | 0.72 | `results/phase3/additional/2026-11-06T18-16-31-540Z/` |
| Parse+bind+check+emit, optimized, 4 checkers | vs `tsgo7-emit` | 0.782 [0.779, 0.786] | faster | none | 0.56 | `results/phase4/emit-additional/2026-11-06T20-28-04-213Z/` |

The two runs of 2026-11-06 come after the modules EO1–MM1, which were tuned on the benchmark corpus (§8). The check run before them, after CO6, gave 0.970 [0.965, 0.976].

**Pre-registered secondary comparison, §18 (`prereg/v10`), 12 checkers on both sides.**

| Comparison | G [CI] | Reading | Repositories above 1.20 | Peak working set (G) | Source |
|---|---|---|---|---:|---|
| Workstation GC, `cs-o-aot-ws-check12` vs `tsgo7-check12` | 1.305 [1.292, 1.315] | no parity | vscode, bluesky, playwright, tldraw | 0.77 | `results/phase3/check12/2026-11-02T06-16-29-174Z/` |
| Server GC, `cs-o-aot-srv-check12` vs `tsgo7-check12`, 98.75 % | 0.890 [0.878, 0.900] | faster | none | 0.93 | same run |
| Additional run after CO6, Workstation GC | 1.236 [1.226, 1.244] | no parity | vscode, bluesky, playwright, tldraw | 0.77 | `results/phase3/check12-additional/2026-11-02T20-56-02-668Z/` |
| Additional run after CO6, Server GC, 98.75 % | 0.889 [0.878, 0.897] | faster | none | 0.94 | same run |

**Pre-registered secondary comparison, §20 (`prereg/v12`): tsrs's kind of pool against tsrs.** The factor is the C# time divided by tsrs's; lower is better.

| Comparison | Factor G [CI] | vscode | sentry | bluesky | playwright | tldraw | Peak working set (G) | Source |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Workstation GC, 12 checkers, `cs-o-aot-ws-checkpool` vs `tsrs-check` | 2.149 [2.122, 2.182] | 2.42 | 1.91 | 2.22 | 2.24 | 2.01 | 1.57 | `results/phase3/checkpool/2026-11-06T16-46-12-267Z/` |
| Server GC, 16 checkers, `cs-o-aot-srv-checkpool` vs `tsrs-check`, 98.75 % | 1.832 [1.796, 1.866] | 1.97 | 1.45 | 2.12 | 1.98 | 1.73 | 1.82 | same run |
| `tsgo7-check` (4 checkers) vs `tsrs-check`, for comparison | 5.10 | 6.49 | 5.22 | 4.08 | 4.82 | 5.18 | — | same run |
| `tsgo7-check12` vs `tsrs-check`, for comparison | 3.20 | 3.99 | 2.90 | 2.92 | 3.17 | 3.16 | — | same run |

Peak working set (G) is the geometric mean over the repositories of the C# median peak working set divided by the reference's. It was computed for this report from the archived run.json files.

## 3. Protocol in brief

- **References,** pinned by commit:
  - the port source and first oracle is TypeScript `v6.0.3` (`050880ce`);
  - the baseline and second oracle is typescript-go `7.0.2` (`2bd066d8`).
  - From `prereg/v5` on, the secondary reference is tsrs 0.18.0, a Rust port of TypeScript 7 (7.1.0-dev).
- **Benchmark corpus:** the five codebases of Microsoft's TypeScript 7 announcement: VS Code, Sentry, Bluesky, Playwright, tldraw. Each is pinned by a mechanical commit rule. Up to `prereg/v11` the corpus was used only for the protocol runs.
- **Tuning corpus:** TypeScript's `tests/cases` plus its own sources. Every speed-dependent choice up to `prereg/v11` was made on it. After `prereg/v11` the modules EO1–MM1 were tuned on the benchmark corpus, by the author's decision (§8, disclosed in §20 of the pre-registration).
- **Equivalence:** 50,177 units, which are TypeScript's test cases plus every benchmark file. Each phase has a byte-identical gate against tsc 6.0.3, with no tolerance:
  - Gate 1: AST dumps, including JSDoc.
  - Gate B: binder dumps, in both JSDoc modes.
  - Gate C: diagnostics, types and symbols dumps.
  - Gate D: emit dumps (every output file, the emit diagnostics, `emitSkipped`).
- **Measurement:**
  - hyperfine, 20 timed runs after 3 warmups;
  - drivers in the high priority class, peak memory from 5 separate runs;
  - rig checks: fixed processor state, no boost, idle before each run;
  - a variance gate on the binding single-threaded rows: MAD/median < 3 %.
- **One final measurement:** the endpoint is the first protocol-compliant run after the gate passes. Additional runs never replace it.
- **Timestamps:** every pre-registration tag (`prereg/v1` … `v12`) has an OpenTimestamps proof anchored in Bitcoin ([timestamps/README.md](timestamps/README.md)). The repository is private until publication.

**Kill criteria (§9):** none was triggered.
- The oracles agreed under the pre-registered normalization: 50,092 units identical, 85 listed divergences.
- Track F passed Gate 1 on 2026-10-21, inside the 18-week budget.
- The variance gate passed in every deciding run.

## 4. Phase 1: parsing

### Track F (unoptimized, faithful)

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

## 7. Phase 4: emitter (Track O only)

The emitter of TypeScript 6.0.3 was ported for Track O only, about 17,500 lines of C#. It produces `.js` and `.d.ts` output under TypeScript 6.0.3's default options plus `declaration: true`:
- target ES2025, so module kind ES2022;
- no source maps;
- comments kept.

This is the author's decision of 2026-11-03; the amendment is `prereg/v11` (§19).

**What was ported:**
- the transformers TypeScript, ESNext, ES decorators, class fields, ES module, CommonJS for `.cts`/`.cjs`, and declarations;
- the emit resolver, the emit helpers and comment emission;
- the node builder's declaration mode.

The work was done in modules D0–D8 ([phase4-summary.md](phase4-summary.md)).

**Equivalence.**
- **Gate D passes:** the emit dumps are byte-identical with tsc 6.0.3 on 50,177 of 50,177 units.
  - Four units on which tsc 6.0.3 itself fails with a `Debug Failure` fail in Track O at the same ported assertion.
  - tsc's process-wide symbol id inside generated names is normalized on both sides: 1 unit, 2 lines.
- **Programs:** the five repository programs, emitted single-threaded, give output byte-identical with tsc 6.0.3's, for example vscode with 22,942 files and 161,946,338 bytes.
- **Parallel:** with several checkers the `.d.ts` output depends on the number of checkers, on both the C# side and typescript-go. The `.js` output does not.

**Endpoint (2026-11-03, the first port after Gate D, no emitter optimization).**

> Parse+bind+check+emit of a program, optimized track, 4 checkers: 1.030× [1.026, 1.035] the wall-clock of typescript-go 7.0.2, so **within 20 %**. Per repository: vscode 1.147, sentry 0.812, bluesky 1.153, playwright 1.153, tldraw 0.938; none is above 1.20. Peak working set is 0.56× typescript-go's. Windows only.

Secondary results of the endpoint run:
- single-threaded, faster (0.821);
- Server GC, faster (0.894 at 98.75 %);
- against tsrs, 5.057 parallel and 1.630 single-threaded.

**Where the emit adds time (post hoc).** Compared with the check-only additional run, which is a separate run:
- On vscode the emit costs Track O 9.1 s under Workstation GC and typescript-go 5.9 s, and the GC pause grows from 3.3 s to 4.4 s.
- On the other four repositories both sides pay about the same for the emit.

## 8. After Phase 4: the gap to tsrs (`prereg/v12`)

After the emitter endpoint, the author's question became the gap to tsrs. On 2026-11-04 and 2026-11-05 the author decided to:
- optimize in short loops against tsrs, on runs of the benchmark repositories;
- port tsrs's code shapes and data structures where they are exact for 6.0.3;
- take over tsrs's scheduling as a new driver option.

These decisions break two rules of the pre-registration (§5.4 rules 5 and 6). §20 discloses this, and so does §12 below. **Every C# result after 2026-11-04 is fitted to the corpus it is measured on.**

**Modules** (one agent attempt each; steps kept by interleaved runs against the previous step, with a Gate C sample of 200 units between steps; details in [phase1-progress.md](phase1-progress.md)):

| Module | What | Kept |
|---|---|---|
| EO1, EO2 | emit: tsrs's accessible-chain cache, export index and empty-result memo | both |
| RS1, RS2 | checker: stored type lists, mapper caches as scratch, callbacks without closures, pooled relaters; tsrs's flow memo | both (RS2: 2 steps; 3 rejected) |
| SC1 | scheduling: `--pool tsrs`, i.e. tsrs's directory-locality assignment (FENNEL) and tail work stealing, 12 checkers under Workstation GC and 16 under Server GC; new switch, default off | yes |
| FE1 | front end on the pool: assignment, default library, line maps, resolver | yes |
| AR1 | checker data in native per-checker arenas: link stores, signature parameters, inferences | yes |
| AR2 | gen0 budget 32 MB for `-aot-ws` (amendment of A7), fewer young allocations | yes |
| PC1 | per-checker speed: no call-count divergence from tsrs found; two small fast paths | yes, within noise |
| MM1 | memory model: inference contexts as arena records, stored base types, reference-free relation caches and name index | yes (5 of 10 steps) |
| CG1 | code generation: x86-64-v3, `IlcOptimizationPreference=Speed`: 0–4 % | not adopted |

**Factor against tsrs over the modules** (orchestrator measurements, 5 interleaved rounds per repository, exploratory, not protocol runs; geometric mean of median / tsrs median):

| After | Server GC, `--pool tsrs` | Workstation GC, `--pool tsrs` | Workstation GC, default pool |
|---|---:|---:|---:|
| RS2 (before SC1) | — | — | 4.50 |
| SC1 | 2.12 | 3.10 | — |
| FE1 | 2.04 | 2.97 | 4.37 |
| AR1 | 1.93 | 2.57 | 4.10 |
| AR2 | 1.89 | 2.35 | 3.86 |
| MM1 | 1.81 | 2.15 | 3.84 |

**Conformity.** On 1eabf90, the last commit that changes C# code, the full Gate C and the full Gate D passed on all 50,177 units. Single-threaded output is unchanged: identical with tsc 6.0.3.

**`prereg/v12`** (§20, accepted 2026-11-06, OpenTimestamps) registered:
- profile `checkpool` as a secondary comparison against `tsrs-check`, with a determinism pass;
- the gen0 budget;
- the disclosures above.

The endpoints are unchanged.

**Protocol run of profile `checkpool`** (2026-11-06, variance gate PASS):

> Parse+bind+check of a program with tsrs's kind of pool, optimized track, against tsrs 0.18.0: Workstation GC with 12 checkers **2.149× [2.122, 2.182]** tsrs's wall-clock (vscode 2.42, sentry 1.91, bluesky 2.22, playwright 2.24, tldraw 2.01); Server GC with 16 checkers **1.832× [1.796, 1.866]** at 98.75 % (vscode 1.97, sentry 1.45, bluesky 2.12, playwright 1.98, tldraw 1.73). Peak working set 1.57× (ws) and 1.82× (srv) tsrs's. Tuned on this corpus. Windows only.

For comparison in the same run:
- typescript-go takes 5.10 times tsrs's time with its default 4 checkers, and 3.20 times with 12.
- Against typescript-go, the C# pool configurations take 0.42 (ws) and 0.36 (srv) of `tsgo7-check`'s time, and 0.67 and 0.57 of `tsgo7-check12`'s.
- On vscode the medians are: tsrs 2.21 s, Track O ws 5.34 s, srv 4.34 s, typescript-go 14.33 s (8.81 s with 12 checkers).

**Determinism (Table 1e).** Work stealing makes the parallel diagnostics depend on timing. In 5 untimed runs per repository:
- **Workstation GC:** vscode and bluesky gave one output each; sentry and tldraw gave 2 distinct outputs with equal counts; playwright gave 5 of 5 (30,826–30,835 diagnostics).
- **Server GC:** vscode gave 3 of 5 distinct outputs; playwright gave 5 of 5 (30,755–30,816).

tsrs's output is designed not to depend on the assignment; the C# pool does not port the canonical caches that achieve this.

**Single-threaded against tsrs** (additional runs of 2026-11-06):
- check: 1.466 [1.462, 1.469] (vscode 1.72, sentry 1.13);
- emit: 1.350 [1.347, 1.352].

At the TG1 analysis (after CO6) the check was 1.685.

## 9. What may be claimed

Every claim names the track and scope, and gives G with its interval.

**Unoptimized faithful track (Track F):**
- Parsing is more than 20 % slower than typescript-go: 6.31 [6.20, 6.41]. This is the answer to the pre-registered question.

**Optimized track, endpoints:**
- Parsing is faster than typescript-go: 0.60 [0.59, 0.61].
- Parse+bind is inconclusive: 1.207.
- Parse+bind+check of a program with 4 checkers is more than 20 % slower: 1.680.
- Parse+bind+check+emit of a program with 4 checkers is within 20 % of typescript-go: 1.030 [1.026, 1.035]. Single-threaded it is faster (0.821), and so is Server GC (0.894).

**Optimized track, labeled additional runs:**
- Parsing is about 2.5 times as fast as typescript-go: 0.40.
- Parse+bind is about 2.4 times as fast: 0.425.
- Parse+bind+check with 4 checkers is faster than typescript-go. After CO6 it was 0.970 [0.965, 0.976], before any tuning on the benchmark corpus. After MM1 it is 0.756 [0.753, 0.759], tuned on the corpus. No repository is above 1.20.
- Single-threaded, the check is faster: 0.800 after CO6, 0.696 after MM1.
- With Server GC, the check is faster: 0.827 after CO6, 0.710 after MM1.
- Parse+bind+check+emit with 4 checkers is faster than typescript-go after MM1: 0.782 [0.779, 0.786], tuned on the corpus. The endpoint stays 1.030.

**Optimized track, §18 secondary:**
- With 12 checkers on both sides, Server GC is faster than typescript-go: 0.890.
- With 12 checkers on both sides, Workstation GC is more than 20 % slower: 1.305 (additional run after CO6: 1.236).

**Against tsrs, secondary:**
- Parsing is within 20 % (1.04).
- Parse+bind is within 20 % (1.146).
- The program check with tsrs's kind of pool (§20, `prereg/v12`) takes 2.149 [2.122, 2.182] times tsrs's time under Workstation GC and 1.832 [1.796, 1.866] under Server GC. It is tuned on the corpus, and its parallel diagnostics vary between runs.

**Not claimed:**
- that the faithful port is near typescript-go;
- that the optimized checker matches typescript-go at its pre-registered endpoint;
- that any Track O result is faster than tsrs in any phase;
- that the checker is near tsrs. With tsrs's kind of pool it is 1.83–2.15 times as slow, and single-threaded 1.47 times;
- that the emit endpoint is faster than typescript-go with Workstation GC: its lower bound is 1.026. The later 0.782 is an additional run, tuned on the corpus;
- that the emit is near tsrs: 3.85 parallel after MM1, 1.35 single-threaded;
- that any result after 2026-11-04 would hold on a corpus it was not tuned on;
- anything about Linux.

## 10. Why, in short (post hoc)

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
- **The emitter** is the only phase whose endpoint was measured after the checker optimizations. The checker under it was already at 0.970. The emit brings that to 1.030, mostly on vscode, through allocation and Workstation GC pause as before.
- **What closed part of the tsrs gap after TG1** (§8). Of the factors TG1 named:
  - **scheduling:** checker count, locality and stealing (SC1);
  - **the Workstation GC pause:** arenas, the gen0 budget and fewer surviving objects (AR1, AR2, MM1). On vscode at 12 checkers the pause fell from about 5 s to 1.28 s of 5.34 s.
  - **per-operation cost:** tsrs's memos (RS1, RS2) brought the single-threaded check from 1.69 to 1.47 times tsrs's.
- **What is left** (MM1's probe, exploratory):
  - Without any collection, Server GC on vscode would be only about 7 % faster, and on sentry about 16 %.
  - At 16 checkers on 12 cores, memory traffic costs about 11 %.
  - PC1 found no call-count divergence from tsrs. So the rest is the cost per operation of the C# object model and code (single-threaded 1.47), plus the parallel loss: per type, Track O is 13–21 % slower at 16 checkers than single-threaded, while tsrs stays flat.

## 11. Disclosed differences

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
  - No build uses AVX2. An x86-64-v3 build of Track O was not faster for the parser. In CG1 it moved the check by 0–4 %, together with `IlcOptimizationPreference=Speed`, and was not adopted.
  - From `prereg/v12` on, `-aot-ws` builds carry a gen0 budget of 32 MB (amendment of A7). Server GC keeps its defaults.
  - The tsrs driver is built with fat LTO but without the PGO (and, on Linux, BOLT) of tsrs's release builds. The measured tsrs is therefore slower than the shipped one, by up to about 13 % by tsrs's own measurement.
- **Checker pools:**
  - Track O and typescript-go use a fixed assignment, 4 checkers (12 in §18).
  - tsrs uses 16 checkers on this machine, assigns files by directory locality, and steals work.
  - With more than one checker, the diagnostics of Track O and typescript-go depend on the assignment. This is inherent to tsc's structure: tldraw has 52,608 diagnostics in typescript-go at 12 checkers against 52,604 single-threaded.
  - tsrs's default output does not depend on the assignment, by design, apart from known cases; only its `go` mode behaves like typescript-go.
  - Track O's `--pool tsrs` (§20) ports tsrs's locality assignment and work stealing, not its canonical caches. Its parallel diagnostics vary from run to run (§8).
- **Emit:**
  - typescript-go's and tsrs's emitters follow TypeScript 7. Their output bytes differ from the C# side by at most 0.2 %, and sentry has one output file more (17,841 against 17,840).
  - Track O emits one work item per checker; typescript-go emits one per file.
  - The tuning emit list leaves out 139 units that tsc 6.0.3 itself cannot emit together. It was used for tuning only.
- **Runtimes:**
  - The binder, checker and emitter endpoints report the NativeAOT runtimes only (§16).
  - The primary C# configuration has been NativeAOT with Workstation GC, parallel, since `prereg/v1`. Server GC rows are secondary.

## 12. Deviations, decisions after data, and process

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
  - v11: emitter. The CommonJS transformer and the symbol-id normalization of Gate D were decided by the author while the gate still failed, before any emit measurement.
- **Decided after seeing data:**
  - **v4,** the variance gate binding fewer rows. It was adopted after M6 runs 1 and 2 failed it only in JIT and Server GC configurations. Those runs carry no verdict, and their primary numbers were 6.45 and 6.28.
  - **v10,** 12 checkers. N = 12 was chosen after the unregistered exploration CN1. This is disclosed in §18, and the measurement is secondary.
  - **v12,** tsrs's kind of pool and the gen0 budget. Both were chosen after runs on the benchmark corpus (SC1, AR2). This is disclosed in §20, and the measurement is secondary.

**Rules not followed (disclosed in §20).** From EO1 to MM1 (2026-11-04 to 2026-11-06), these rules were set aside by the author's decision:
- §5.4 rule 6: no tuning on the benchmark corpus;
- §5.4 rule 5: at most 3 variants per step, ranked on the tuning corpus.

Instead, keep decisions were made on interleaved runs of the benchmark repositories, with only a Gate C sample of 200 units between steps. The full gates were run once at the end, on 1eabf90.
- Agents bent rules in places, all logged in phase1-progress.md: a git stash and checkout on the working tree, a process-wide registry slot per checker in MM1, and one listing of processes (PC1).
- The §12.2 row for `prereg/v11` had been missing from the amendment table; it was added together with v12's.

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

**Defects found by the emit equivalence of the programs** (D8): two declaration-emit defects that no one-file unit of Gate D reaches. Both were fixed before `prereg/v11` was tagged.

**Workflow:**
- Opus 5.5 orchestrated; ports and optimization steps were done by agents in separate attempts.
- From 2026-10-25, one agent ran at a time, directly on `main`.
- Gates and the rig checks were rerun by the orchestrator before every merge and every protocol run.

## 13. Open

- **Linux replication (A4, §8):** deferred by the author. Until it exists, every result is "Windows only".
- **The tsrs gap in the checker:** 1.83 (srv) to 2.15 (ws) with tsrs's kind of pool, 1.47 single-threaded (§8, §10). What is left:
  - per-operation cost: records of types, symbols and signatures as objects; MM1's attempt to make type records native was 6.7 % slower;
  - the parallel loss at 16 checkers;
  - deterministic parallel output, which would need tsrs's canonical caches.
- **The Workstation GC pause:** 1.28 s of 5.34 s on vscode with 12 checkers. The gap from Workstation to Server GC is about 1.2 times.
- **Not done:**
  - an evaluation of the tuned code on a corpus it was not tuned on;
  - the parser steps O4, O7b and O8.
- **Out of scope (§10, §19):**
  - the emitter under other options: downlevel targets, JSX, legacy decorators, the AMD, UMD and System modules, source maps;
  - the language service, incremental parsing, tsserver and the public API.

## 14. Timeline

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
| 2026-11-03 | Additional runs after CO6: 4 checkers 0.970 (faster), 12 checkers 1.236; checker optimization closed; TG1 (tsrs gap analysis); Phase 4 D0–D8; Gate D passes; `prereg/v11`; emitter endpoint (1.030, within 20 %) |
| 2026-11-04 | EA1 (emit allocation account); EO1, EO2 (emit, tsrs structures); tuning on the benchmark corpus begins |
| 2026-11-05 | RS1, RS2 (checker, tsrs memos); SC1 (`--pool tsrs`); FE1 (front end); AR1, AR2 (arenas, gen0 budget) |
| 2026-11-06 | PC1, MM1, CG1; full Gate C and Gate D pass on 1eabf90; `prereg/v12`; `checkpool` run (ws 2.149, srv 1.832 vs tsrs); additional runs check 0.756, emit 0.782 |
| 2026-11-07 | Report v1.4; project closed by the author |

The 18-week budget for Phase 1 (to 2027-02-24) was not needed. Every phase finished inside it.

## 15. Reproducing

- Pins: `pins.json` and `global.json`. Fetch the references with `node scripts/fetch-references.mjs`.
- Builds: `harness/bench/build-cs.mjs`, `build-go.mjs` and `build-rs.mjs`.
- Gates: `node harness/port/gate.mjs --track optimized --phase parser|binder|checker|emit`; Track F: `--track faithful`.
- Emit equivalence of the programs: `node harness/bench/emit-equivalence.mjs`.
- Protocol runs: `node harness/bench/bench.mjs --profile full|binder|check|check12|emit|checkpool`, then `node harness/bench/report.mjs <run.json>` ([benchmark-harness.md](benchmark-harness.md)).
- The HTML page of this report: `node scripts/report-page.mjs docs/report-v1.md <out.html>`.
- Builds currently need `WarningsNotAsErrors=NU1903` in the environment. The advisory GHSA-p3jp-9v83-76w9 on `Microsoft.DotNet.ILCompiler` 10.0.13 fails `dotnet publish`. Whether to upgrade or suppress it was left open by the author.
- Verify a timestamp: `node scripts/ots.mjs verify docs/timestamps/prereg-vN.txt.ots`.
