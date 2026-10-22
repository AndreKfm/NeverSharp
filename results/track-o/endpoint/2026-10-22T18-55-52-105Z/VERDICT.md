# Track O endpoint ("optimized track", 2026-10-22) — Windows only

Own protocol-compliant run with `tsgo7-parse` in it (pre-registration §8, Track O endpoint; the Track F verdict came from M6 run 3 before Track O was ready). Harness commit 51f5036 (Track O after step O1), label `track-o-endpoint-run1`, 2026-10-22 18:55–19:50 UTC. Rig checks ok (idle 5.8 %); variance gate PASS (tsc6-parse ≤ 1.33 %, tsgo7-parse-single ≤ 0.71 %, cs-o-aot-ws-single ≤ 0.59 %); crosscheck A6 driver/tsgo 0.77–1.03. Full tables: [report.md](report.md).

**Track O endpoint** (cs-o-aot-ws vs tsgo7-parse, geometric mean over the five repositories, 95 % percentile bootstrap, seed 20261021):

> G = 0.60 [0.59, 0.61]

Upper bound ≤ 1.20 → parity claim allowed; upper bound < 1.00 → the "faster" claim is allowed as well: the optimized C# port (Track O, NativeAOT, Workstation GC, parallel) **parses faster than typescript-go 7.0.2** — about 1.7 times as fast (0.60 of its wall-clock) on Microsoft's benchmark corpus. Per repository: vscode 0.54 [0.52, 0.55], sentry 0.62 [0.60, 0.63], bluesky 0.62 [0.61, 0.63], playwright 0.61 [0.60, 0.63], tldraw 0.62 [0.59, 0.64]. Reported as the optimized track, next to and never instead of the Track F verdict (G = 6.31 [6.20, 6.41], no parity). Windows only; the Linux replication (A4) is pending.

**Disclosed difference in the work done** (pre-registration §8, docs/references.md §2a): the C# tracks parse as tsc's compiler host does (`JSDocParsingMode.ParseForTypeErrors`), typescript-go 7.0.2 parses JSDoc eagerly. Both drivers report the same file counts; the comparison is the pre-registered one, and this difference stays attached to the claim.

**Secondary** (95 % intervals; parity statements about secondary configurations would need the Bonferroni-adjusted 98.75 % intervals):

- Other Track O configurations vs tsgo7-parse: cs-o-aot-srv 0.73 [0.72, 0.75], cs-o-jit-ws 2.18 [2.16, 2.21], cs-o-jit-srv 2.28 [2.26, 2.31].
- Single-threaded: cs-o-aot-ws-single vs tsgo7-parse-single 0.920 [0.918, 0.922] (`../single.mjs`). Single-threaded Track O is 8 % faster than single-threaded typescript-go; most of the parallel gap is scaling (parallel/single: Track O 0.11–0.17, typescript-go 0.18–0.24 per repository). Not analysed further here; on vscode Track O allocates 0.69 GB with 61.8 ms GC pause in total.
- Against tsrs 0.18.0 (Rust; secondary reference, Table 2b): cs-o-aot-ws vs tsrs-parse 1.52 [1.50, 1.54] → lower bound > 1.20: **more than 20 % slower than tsrs** (secondary); single-threaded 1.724 [1.718, 1.729]. tsrs vs tsgo7-parse: 0.39 [0.39, 0.40]. Disclosed: tsrs parses JSDoc in `.ts` files lazily (TypeScript 7.1).
- Track O vs Track F in the same run: cs-o-aot-ws / cs-aot-ws 0.095 [0.093, 0.096] (about 10.5 times as fast); Track F's cs-aot-ws vs tsgo7-parse here 6.33 [6.24, 6.42], consistent with its verdict run.
- Peak working set, vscode: cs-o-aot-ws 922 MiB, tsgo7-parse 2,175 MiB, tsrs-parse 955 MiB, cs-aot-ws 2,641 MiB.
- Variance notes (reported configurations above 3 %): tsgo7-parse vscode 3.10 %, tsrs-parse vscode 4.51 %, cs-aot-ws vscode 3.49 % / playwright 3.42 %, cs-o-aot-ws tldraw 3.06 %, cs-o-aot-srv sentry 3.99 %, cs-o-jit-ws-single playwright 3.66 %, cs-o-jit-srv-single playwright 7.11 %.

**Track O steps** (A9, [docs/track-o-steps.md](../../../../docs/track-o-steps.md)): evaluated O0 seed, O6a handles, O6b node layout (A compact records), O5a text spans and interning, O7a list handles, O-NA allocations and NoAlloc coverage, O1 file reading (C native text); not evaluated O2 character classification, O3 trivia and comments, O4 literals, O5b interning lookup, O7b list layouts, O8 parallel scheduling. NoAlloc: 2,121 of 2,453 methods annotated, 55 `[NoAllocUnchecked]` exceptions (prereg/v6 allowlist).
