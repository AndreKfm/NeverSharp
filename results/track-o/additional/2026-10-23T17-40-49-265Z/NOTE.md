# Track O additional run after O25 and O26 (2026-10-23) — Windows only

Protocol-compliant run (rig checks ok, idle 6.9 %; variance gate PASS; crosscheck A6 driver/tsgo 0.77–1.01) of Track O at harness commit dadb71f, label `track-o-additional-run-after-O26`, the same 14 configurations as the endpoint run. **Additional run, not the endpoint:** the Track O endpoint stays G = 0.60 [0.59, 0.61] (`../../endpoint/2026-10-22T18-55-52-105Z/`). Steps since the previous additional run: O25 (expression descent), O26 (token consumption). Full tables: [report.md](report.md).

| Comparison (95 % percentile bootstrap, seed 20261021) | Endpoint run | After O23 | After O11 | After O24 | This run |
|---|---|---|---|---|---|
| cs-o-aot-ws vs tsgo7-parse (G) | 0.60 | 0.47 | 0.46 | 0.43 | **0.41 [0.41, 0.42]** |
| cs-o-aot-ws-single vs tsgo7-parse-single | 0.920 | 0.762 | 0.720 | 0.618 | 0.571 [0.569, 0.573] |
| cs-o-aot-ws vs tsrs-parse (secondary) | 1.52 | 1.19 | 1.16 | 1.06 | **1.04 [1.03, 1.06]**: within 20 % of tsrs (secondary) |
| per repository vs tsrs-parse | — | 1.13–1.28 | 1.08–1.21 | 0.95–1.13 | vscode 0.95 [0.90, 0.98], sentry 1.12, bluesky 1.03, playwright 1.08, tldraw 1.04 |
| cs-o-aot-ws-single vs tsrs-parse-single | 1.724 | 1.435 | 1.357 | 1.167 | 1.077 [1.072, 1.080] |
| cs-o-aot-srv vs tsgo7-parse / tsrs-parse | 0.73 / 1.85 | 0.51 / 1.30 | 0.50 / 1.25 | 0.47 / 1.16 | 0.45 / 1.14 |
| cs-o-aot-ws vs cs-aot-ws (Track F, same run) | 0.095 | 0.075 | 0.073 | 0.069 | 0.066 |

vscode medians across the additional runs (same tsrs and typescript-go binaries): cs-o-aot-ws 0.397 → 0.375 → 0.332 → 0.313 s; tsrs-parse 0.310 → 0.328 → 0.348 → 0.329 s; tsgo7-parse 1.037 → 1.038 → 1.030 → 1.028 s. On vscode, cs-o-aot-ws is below tsrs-parse in two consecutive additional runs (0.95 [0.92, 0.99] and 0.95 [0.90, 0.98]); tsrs-parse's vscode MAD/median is 5.59 % here. Reported per repository, not claimed (pre-registration §8: secondary results; no faster-than claim against tsrs from a single repository of an additional run).

Peak working set vscode: cs-o-aot-ws 881 MiB, tsrs 955 MiB, tsgo 2,177 MiB; 61 MB allocated, GC pause 34.0 ms in total.

Variance notes (above 3 %): tsgo7-parse playwright 4.17 %, tsrs-parse vscode 5.59 %, cs-aot-ws vscode 3.90 %, cs-o-aot-ws sentry 4.01 %, cs-o-aot-srv bluesky 5.97 % / playwright 3.34 % / tldraw 3.58 %. Disclosed differences in the work done (ParseForTypeErrors vs typescript-go's eager JSDoc, about 4 % for Track O; tsrs parses JSDoc in .ts lazily) and in code generation (Go and Rust x86-64 baseline, NativeAOT x86-64-v2; docs/references.md §2a).
