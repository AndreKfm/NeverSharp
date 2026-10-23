# Track O additional run after O12 and O24 (2026-10-23) — Windows only

Protocol-compliant run (rig checks ok, idle 10.9 %, close to the 12 % limit; variance gate PASS; crosscheck A6 driver/tsgo 0.82–1.00) of Track O at harness commit 4e32b26, label `track-o-additional-run-after-O24`, the same 14 configurations as the endpoint run. **Additional run, not the endpoint:** the Track O endpoint stays G = 0.60 [0.59, 0.61] (`../../endpoint/2026-10-22T18-55-52-105Z/`). Steps since the previous additional run: O12 (node creation in one record write), O24 (scanner, second round). Full tables: [report.md](report.md).

| Comparison (95 % percentile bootstrap, seed 20261021) | Endpoint run | After O23 | After O11 | This run |
|---|---|---|---|---|
| cs-o-aot-ws vs tsgo7-parse (G) | 0.60 [0.59, 0.61] | 0.47 | 0.46 | **0.43 [0.42, 0.44]** |
| cs-o-aot-ws-single vs tsgo7-parse-single | 0.920 | 0.762 | 0.720 | 0.618 [0.616, 0.620] |
| cs-o-aot-ws vs tsrs-parse (secondary) | 1.52 | 1.19 | 1.16 | **1.06 [1.05, 1.07]**: within 20 % of tsrs (secondary) |
| per repository vs tsrs-parse | — | 1.13–1.28 | 1.08–1.21 | vscode 0.95 [0.92, 0.99], sentry 1.13, bluesky 1.13, playwright 1.07, tldraw 1.03 |
| cs-o-aot-ws-single vs tsrs-parse-single | 1.724 | 1.435 | 1.357 | 1.167 [1.162, 1.172] |
| cs-o-aot-srv vs tsgo7-parse / tsrs-parse | 0.73 / 1.85 | 0.51 / 1.30 | 0.50 / 1.25 | 0.47 / 1.16 |
| cs-o-aot-ws vs cs-aot-ws (Track F, same run) | 0.095 | 0.075 | 0.073 | 0.069 |

vscode medians across the additional runs (same tsrs and typescript-go binaries): cs-o-aot-ws 0.397 → 0.375 → 0.332 s; tsrs-parse 0.310 → 0.328 → 0.348 s; tsgo7-parse 1.037 → 1.038 → 1.030 s. Part of the change against tsrs on vscode is therefore run-to-run variation of tsrs itself (its vscode MAD/median 3.15 % here, 4.27–5.40 % before); the per-repository vscode ratio below 1.00 is reported, not claimed (pre-registration §8: secondary results; no faster-than claim against tsrs is made from a single repository of an additional run).

Peak working set vscode: cs-o-aot-ws 881 MiB, tsrs 955 MiB, tsgo 2,188 MiB; 62 MB allocated, GC pause 37.4 ms in total.

Variance notes (above 3 %): tsrs-parse vscode 3.15 %, cs-aot-ws playwright 6.60 %, cs-o-aot-ws tldraw 3.31 %, cs-o-aot-srv bluesky 5.82 % / playwright 4.56 % / tldraw 3.82 % / vscode 3.18 %, cs-o-jit-srv vscode 3.73 %, cs-o-jit-srv-single playwright 3.28 %. Disclosed differences in the work done as in the endpoint verdict (ParseForTypeErrors vs typescript-go's eager JSDoc, about 4 % for Track O; tsrs parses JSDoc in .ts lazily).
