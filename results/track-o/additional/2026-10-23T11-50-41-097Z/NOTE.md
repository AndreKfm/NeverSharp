# Track O additional run after O5b and O11 (2026-10-23) — Windows only

Protocol-compliant run (rig checks ok, idle 6.5 %; variance gate PASS; crosscheck A6 driver/tsgo 0.73–0.94) of Track O at harness commit cfbabcf, label `track-o-additional-run-after-O11`, the same 14 configurations as the endpoint run. **Additional run, not the endpoint:** the Track O endpoint stays G = 0.60 [0.59, 0.61] (`../../endpoint/2026-10-22T18-55-52-105Z/`); this run is published next to it and next to the previous additional run (`../2026-10-23T08-51-58-122Z/`). Steps since the previous additional run: O5b (word-hash interning), O11 (C exact pre-decisions). Full tables: [report.md](report.md).

| Comparison (95 % percentile bootstrap, seed 20261021) | Endpoint run | Additional run after O23 | This run |
|---|---|---|---|
| cs-o-aot-ws vs tsgo7-parse (G) | 0.60 [0.59, 0.61] | 0.47 [0.46, 0.48] | **0.46 [0.45, 0.46]** |
| per repository vs tsgo7-parse | 0.54–0.62 | 0.38–0.51 | vscode 0.36, sentry 0.46, bluesky 0.54, playwright 0.47, tldraw 0.48 |
| cs-o-aot-ws-single vs tsgo7-parse-single | 0.920 | 0.762 | 0.720 [0.718, 0.722] |
| cs-o-aot-ws vs tsrs-parse (secondary) | 1.52 [1.50, 1.54] | 1.19 [1.18, 1.21] | **1.16 [1.14, 1.17]**: upper bound ≤ 1.20, "within 20 % of tsrs" (secondary) |
| per repository vs tsrs-parse | — | 1.13–1.28 | vscode 1.14, sentry 1.20, bluesky 1.21, playwright 1.15, tldraw 1.08 |
| cs-o-aot-ws-single vs tsrs-parse-single | 1.724 | 1.435 | 1.357 [1.352, 1.362] |
| cs-o-aot-srv vs tsgo7-parse / tsrs-parse | 0.73 / 1.85 | 0.51 / 1.30 | 0.50 / 1.25 |
| cs-o-aot-ws vs cs-aot-ws (Track F, same run) | 0.095 | 0.075 | 0.073 |

The statement against tsrs is secondary (pre-registration §8, prereg/v5: same thresholds, labeled secondary, never replaces the verdict against typescript-go) and comes from an additional run, not from the endpoint. Variance note: tsrs-parse vscode 5.40 % and playwright 3.42 % MAD/median (reported configuration, not gated); cs-o-aot-ws sentry 3.63 %.

vscode: cs-o-aot-ws 0.375 s (tsgo7-parse 1.038 s, tsrs-parse 0.328 s); peak working set 881 MiB (tsgo 2,187, tsrs 955); 60 MB allocated, GC pause 22.9 ms in total.

Other variance notes (above 3 %): cs-aot-ws vscode 3.72 %, cs-o-aot-srv sentry 3.29 %, cs-o-jit-ws vscode 3.75 %, cs-o-jit-srv vscode 3.81 %. Disclosed differences in the work done as in the endpoint verdict (ParseForTypeErrors vs typescript-go's eager JSDoc, about 4 % for Track O; tsrs parses JSDoc in .ts lazily).
