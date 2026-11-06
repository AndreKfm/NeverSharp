# Emit additional run 1, after MM1 (2026-11-06) — Windows only

Protocol-compliant run of profile `emit` (§19):
- harness commit 6920a1c (`prereg/v12`), drivers built from 1eabf90, label `emit additional run 1 (after MM1, prereg/v12)`;
- rig checks ok;
- variance gate PASS: binding rows tsgo7-emit-single ≤ 0.39 %, cs-o-aot-ws-emit-single ≤ 0.45 %; no reported row above 3 %.

**Additional run, not the endpoint.** The emit endpoint stays G = 1.030 [1.026, 1.035] (within 20 % of typescript-go, `../../emit/2026-11-03T19-39-41-764Z/NOTE.md`). The modules since then are EO1–MM1. They were tuned on these repositories (§20, disclosed), so this run is fitted to the corpus it measures. Full tables: [report.md](report.md).

| Parse+bind+check+emit (percentile bootstrap, seed 20261021) | Endpoint run | This run (after MM1) |
|---|---|---|
| cs-o-aot-ws-emit vs tsgo7-emit (G, 95 %) | 1.030 [1.026, 1.035] | **0.782 [0.779, 0.786]** |
| per repository | vscode 1.147, bluesky 1.153, playwright 1.153 | vscode 0.737, sentry 0.682, bluesky 0.907, playwright 0.871, tldraw 0.736 |
| single-threaded vs tsgo7-emit-single | 0.821 | 0.68 [0.68, 0.68] |
| cs-o-aot-srv-emit vs tsgo7-emit (98.75 %) | 0.894 | 0.73 [0.73, 0.74] |
| cs-o-aot-ws-emit vs tsrs-emit (secondary) | 5.057 | **3.848 [3.812, 3.870]** (vscode 4.56, sentry 3.42, bluesky 3.64, playwright 4.12, tldraw 3.62) |
| cs-o-aot-srv-emit vs tsrs-emit | — | 3.605 [3.568, 3.625] |
| single-threaded vs tsrs-emit-single | — | **1.350 [1.347, 1.352]** |

Medians on vscode: ws 14.96 s, srv 13.83 s. Workstation GC pause on vscode: 1.50 s (Table 5).
