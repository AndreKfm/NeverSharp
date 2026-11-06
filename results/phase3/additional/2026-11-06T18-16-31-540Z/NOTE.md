# Checker additional run 4, after MM1 (2026-11-06) — Windows only

Protocol-compliant run of profile `check` (§17):
- harness commit 6920a1c (`prereg/v12`), drivers built from 1eabf90, label `check additional run 4 (after MM1, prereg/v12)`;
- rig checks ok;
- variance gate PASS: binding rows tsgo7-check-single ≤ 0.70 %, cs-o-aot-ws-check-single ≤ 0.46 %; no reported row above 3 %.

**Additional run, not the endpoint.** The checker endpoint stays G = 1.680 [1.675, 1.689] (no parity, `../../checker-endpoint/2026-10-29T14-41-57-488Z/VERDICT.md`). The modules since run 3 (`../2026-11-02T22-22-25-874Z/NOTE.md`) are EO1–MM1. They were tuned on these repositories (§20, disclosed), so this run is fitted to the corpus it measures. Full tables: [report.md](report.md).

| Parse+bind+check (95 % percentile bootstrap, seed 20261021) | Endpoint | Run 3 (after CO6) | This run (after MM1) |
|---|---|---|---|
| cs-o-aot-ws-check vs tsgo7-check (G) | 1.680 [1.675, 1.689] | 0.970 [0.965, 0.976] | **0.756 [0.753, 0.759]** |
| per repository | vscode 1.752, sentry 1.335, bluesky 1.901, playwright 2.008, tldraw 1.499 | vscode 0.983, sentry 0.745, bluesky 1.168, playwright 1.168, tldraw 0.860 | vscode 0.761, sentry 0.593, bluesky 0.907, playwright 0.863, tldraw 0.701 |
| single-threaded vs tsgo7-check-single | 0.954 [0.953, 0.956] | 0.800 [0.799, 0.802] | 0.696 [0.694, 0.697] |
| cs-o-aot-srv-check vs tsgo7-check (98.75 %) | 1.055 [1.047, 1.065] | 0.827 [0.821, 0.834] | 0.710 [0.706, 0.715] |
| srv single-threaded (98.75 %) | 0.871 [0.869, 0.873] | 0.794 [0.792, 0.797] | 0.702 [0.700, 0.704] |
| cs-o-aot-ws-check vs tsrs-check (secondary) | 8.57 [8.52, 8.66] | 4.906 [4.868, 4.958] | **3.794 [3.764, 3.819]** (vscode 4.91, sentry 3.03, bluesky 3.60, playwright 4.04, tldraw 3.64) |
| cs-o-aot-srv-check vs tsrs-check | — | — | 3.563 [3.535, 3.590] |
| single-threaded vs tsrs-check-single | 2.007 [2.004, 2.012] | 1.685 [1.683, 1.689] | **1.466 [1.462, 1.469]** (vscode 1.72, sentry 1.13, bluesky 1.56, playwright 1.72, tldraw 1.30) |
| tsgo7-check vs tsrs-check | | | 5.017 [4.979, 5.050] |

Parallel medians, Track O ws (run 3 → this run), with typescript-go in brackets:
- vscode 14.04 → 10.89 s (14.32 s);
- sentry 3.58 → 2.87 s (4.84 s);
- bluesky 0.95 → 0.743 s (0.818 s);
- playwright 1.73 → 1.29 s (1.49 s);
- tldraw 1.68 → 1.38 s (1.97 s).

Peak working set on vscode: 4,530 MiB, against typescript-go 6,997 and tsrs 2,770.
