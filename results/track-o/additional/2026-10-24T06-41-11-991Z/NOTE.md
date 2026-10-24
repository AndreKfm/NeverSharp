# Track O additional run after O29 (2026-10-24) — Windows only

Protocol-compliant run (rig checks ok, idle 7.6 %; variance gate PASS; crosscheck A6 driver/tsgo 0.78–1.01) of Track O at harness commit 95b038e, label `track-o-additional-run-after-O29`, the same 14 configurations as the endpoint run. **Additional run, not the endpoint:** the Track O endpoint stays G = 0.60 [0.59, 0.61]. Step since the previous additional run: O29 (NativeAOT inlining audit; neutral on the tuning corpus, about 1–3 % in memory). Full tables: [report.md](report.md).

| Comparison (95 % percentile bootstrap, seed 20261021) | Endpoint | After O26 | After O28 | This run |
|---|---|---|---|---|
| cs-o-aot-ws vs tsgo7-parse (G) | 0.60 | 0.41 | 0.40 [0.39, 0.40] | **0.40 [0.39, 0.40]** |
| cs-o-aot-ws-single vs tsgo7-parse-single | 0.920 | 0.571 | 0.545 | 0.540 [0.539, 0.542] |
| cs-o-aot-ws vs tsrs-parse (secondary) | 1.52 | 1.04 | 1.02 [1.01, 1.04] | **1.04 [1.03, 1.05]**: within 20 % of tsrs (secondary) |
| per repository vs tsrs-parse | — | 0.95–1.12 | 0.92–1.08 | vscode 0.92 [0.88, 0.96], sentry 1.08, bluesky 1.13, playwright 1.05, tldraw 1.02 |
| cs-o-aot-ws-single vs tsrs-parse-single | 1.724 | 1.077 | 1.029 | 1.021 [1.016, 1.026] |
| cs-o-aot-ws vs cs-aot-ws (Track F, same run) | 0.095 | 0.066 | 0.064 | 0.063 |

Against the previous additional run, the overall ratio against tsrs moved from 1.02 to 1.04 with overlapping intervals, while single-threaded improved from 1.029 to 1.021: the change from O29 is within the run-to-run spread of the parallel comparison (bluesky alone 1.08 → 1.13). vscode stays below tsrs for the fourth run in a row (0.92 [0.88, 0.96]; tsrs vscode MAD/median 3.71 %), reported, not claimed.

vscode: cs-o-aot-ws 0.295 s, tsrs 0.321 s, tsgo 1.036 s; peak working set 881 / 956 / 2,176 MiB. Variance notes (above 3 %): tsrs-parse vscode 3.71 %, cs-aot-ws playwright 5.17 %, cs-o-aot-srv bluesky 5.57 %, cs-o-jit-ws vscode 3.54 %. Disclosed differences in the work done and in code generation as in the previous notes (docs/references.md §2a).
