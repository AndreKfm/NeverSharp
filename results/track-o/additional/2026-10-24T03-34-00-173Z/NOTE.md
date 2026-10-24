# Track O additional run after O27 and O28 (2026-10-24) — Windows only

Protocol-compliant run (rig checks ok, idle 4.3 %; variance gate PASS; crosscheck A6 driver/tsgo 0.75–0.98) of Track O at harness commit 3c6cb2d, label `track-o-additional-run-after-O28`, the same 14 configurations as the endpoint run. **Additional run, not the endpoint:** the Track O endpoint stays G = 0.60 [0.59, 0.61] (`../../endpoint/2026-10-22T18-55-52-105Z/`). Steps since the previous additional run: O27 (identifier interning), O28 (call/member rest, node lists). Full tables: [report.md](report.md).

| Comparison (95 % percentile bootstrap, seed 20261021) | Endpoint | After O23 | After O11 | After O24 | After O26 | This run |
|---|---|---|---|---|---|---|
| cs-o-aot-ws vs tsgo7-parse (G) | 0.60 | 0.47 | 0.46 | 0.43 | 0.41 | **0.40 [0.39, 0.40]** |
| cs-o-aot-ws-single vs tsgo7-parse-single | 0.920 | 0.762 | 0.720 | 0.618 | 0.571 | 0.545 [0.543, 0.547] |
| cs-o-aot-ws vs tsrs-parse (secondary) | 1.52 | 1.19 | 1.16 | 1.06 | 1.04 | **1.02 [1.01, 1.04]**: within 20 % of tsrs (secondary) |
| per repository vs tsrs-parse | — | 1.13–1.28 | 1.08–1.21 | 0.95–1.13 | 0.95–1.12 | vscode 0.92 [0.88, 0.94], sentry 1.06, bluesky 1.08, playwright 1.05, tldraw 1.02 |
| cs-o-aot-ws-single vs tsrs-parse-single | 1.724 | 1.435 | 1.357 | 1.167 | 1.077 | 1.029 [1.025, 1.033] |
| cs-o-aot-srv vs tsgo7-parse / tsrs-parse | 0.73 / 1.85 | 0.51 / 1.30 | 0.50 / 1.25 | 0.47 / 1.16 | 0.45 / 1.14 | 0.44 / 1.14 |
| cs-o-aot-ws vs cs-aot-ws (Track F, same run) | 0.095 | 0.075 | 0.073 | 0.069 | 0.066 | 0.064 |

vscode medians across the additional runs (same tsrs and typescript-go binaries): cs-o-aot-ws 0.397 → 0.375 → 0.332 → 0.313 → 0.294 s; tsrs-parse 0.310 → 0.328 → 0.348 → 0.329 → 0.320 s; tsgo7-parse 1.037 → 1.038 → 1.030 → 1.028 → 1.033 s. On vscode, cs-o-aot-ws is below tsrs-parse in the third consecutive additional run (this run 0.92 [0.88, 0.94]; tsrs-parse vscode MAD/median 3.44 %). Reported per repository, not claimed (pre-registration §8: secondary results; no faster-than claim against tsrs from a single repository of an additional run). Overall Track O stays above tsrs (1.02, lower bound 1.01).

Peak working set vscode: cs-o-aot-ws 885 MiB, tsrs 956 MiB, tsgo 2,169 MiB; 65 MB allocated, GC pause 25.2 ms in total.

Variance notes (above 3 %): tsgo7-parse playwright 3.99 %, tsrs-parse vscode 3.44 %, cs-aot-ws vscode 4.10 %, cs-o-aot-srv vscode 4.59 % / bluesky 3.82 %. Disclosed differences in the work done (ParseForTypeErrors vs typescript-go's eager JSDoc, about 4 % for Track O; tsrs parses JSDoc in .ts lazily) and in code generation (Go and Rust x86-64 baseline, NativeAOT x86-64-v2; docs/references.md §2a; an x86-64-v3 build was no faster: ../../probes/avx/NOTE.md).
