# Track O additional run after O1, O9, O10, O23 (2026-10-23) — Windows only

Protocol-compliant run (rig checks ok, idle 4.9 %; variance gate PASS; crosscheck A6 driver/tsgo 0.76–1.01) of Track O at harness commit 36d6262, label `track-o-additional-run-after-O23`, the same 14 configurations as the endpoint run. **Additional run, not the endpoint:** the Track O endpoint stays G = 0.60 [0.59, 0.61] from `../../endpoint/2026-10-22T18-55-52-105Z/` (pre-registration §8, "One final measurement"); this run is published next to it. Steps since the endpoint: O1 (C native text), O9 (C per-thread slabs), O10 (side table), O23 (B lookup tables, keyword perfect hash) — docs/track-o-steps.md. Full tables: [report.md](report.md).

| Comparison (95 % percentile bootstrap, seed 20261021) | Endpoint run | This run |
|---|---|---|
| cs-o-aot-ws vs tsgo7-parse (G) | 0.60 [0.59, 0.61] | **0.47 [0.46, 0.48]** |
| per repository vs tsgo7-parse | vscode 0.54, sentry 0.62, bluesky 0.62, playwright 0.61, tldraw 0.62 | vscode 0.38, sentry 0.50, bluesky 0.51, playwright 0.48, tldraw 0.49 |
| cs-o-aot-ws-single vs tsgo7-parse-single | 0.920 [0.918, 0.922] | 0.762 [0.761, 0.765] |
| cs-o-aot-ws vs tsrs-parse (secondary) | 1.52 [1.50, 1.54] | **1.19 [1.18, 1.21]** — contains 1.20: inconclusive for "within 20 % of tsrs" |
| cs-o-aot-ws-single vs tsrs-parse-single | 1.724 | 1.435 [1.431, 1.440] |
| cs-o-aot-srv vs tsgo7-parse / tsrs-parse | 0.73 / 1.85 | 0.51 / 1.30 |
| cs-o-aot-ws vs cs-aot-ws (Track F, same run) | 0.095 | 0.075 |
| Track F cs-aot-ws vs tsgo7-parse | 6.33 | 6.30 [6.21, 6.37] |

vscode: cs-o-aot-ws 0.397 s (tsgo7-parse 1.037 s, tsrs-parse 0.310 s); peak working set 881 MiB (tsgo 2,186, tsrs 956); 61 MB allocated, GC pause 32.8 ms in total (endpoint run: 691 MB, 61.8 ms).

Variance notes (reported configurations above 3 %): tsgo7-parse tldraw 3.77 %, tsrs-parse vscode 4.27 %, cs-aot-ws vscode 3.52 %, cs-o-jit-ws vscode 4.51 %, cs-o-jit-ws-single playwright 3.53 %, cs-o-jit-srv vscode 5.72 %, cs-o-jit-srv-single playwright 4.64 %. Disclosed differences in the work done as in the endpoint verdict (ParseForTypeErrors vs typescript-go's eager JSDoc, worth about 4 % for Track O: ../../probes/jsdoc/NOTE.md; tsrs parses JSDoc in .ts lazily).
