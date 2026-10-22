# Track F verdict (M6 run 3, 2026-10-22) — Windows only

Deciding run under pre-registration v4: first protocol-compliant run after the prereg/v4 tag that passes the amended variance gate. Harness commit cc1b180, label `m6-track-f-run3`; rig checks ok (idle 11.9 %); variance gate PASS (tsc6-parse ≤ 2.51 %, tsgo7-parse-single ≤ 0.89 %, cs-aot-ws-single ≤ 0.68 %); crosscheck A6 driver/tsgo 0.76–1.00.

**Primary endpoint** (cs-aot-ws vs tsgo7-parse, geometric mean over the five repositories, 95 % percentile bootstrap, seed 20261021):

> G = 6.31 [6.20, 6.41]

Lower bound > 1.20 → **no parity**: the faithful C# port (Track F, NativeAOT, Workstation GC, parallel) parses **more than 20 % slower than typescript-go** — about 6.3 times slower on Microsoft's benchmark corpus. Windows only; the Linux replication (A4) is pending.

Secondary (95 % intervals; parity statements would need the Bonferroni-adjusted 98.75 % intervals, none comes near 1.20): cs-aot-srv 2.91 [2.86, 3.01], cs-jit-srv 3.65 [3.61, 3.72], cs-jit-ws 6.37 [6.31, 6.43]; single-threaded cs-aot-ws-single vs tsgo7-parse-single 10.31 / 4.60 ≈ 2.2×. Variance notes (reported configurations above 3 %): cs-jit-ws-single sentry 3.62 %, cs-jit-srv-single sentry 3.91 %; parallel configurations as listed in report.md. tsc6-parse for reference: 11.90 [11.82, 12.04], i.e. Track F is about 1.9 × faster than tsc 6.0.3.

Earlier runs 1 and 2 (before prereg/v4) failed the then-binding gate in JIT/Server-GC configurations and carry no verdict; their primary numbers were 6.45 [6.33, 6.53] and 6.28 [6.19, 6.38].
