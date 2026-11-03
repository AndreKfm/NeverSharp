# Checker additional run 3, after CO6 (2026-11-03) — Windows only

Protocol-compliant run (profile `check`, §17; rig checks ok, idle 4.3 %; variance gate PASS, binding rows tsgo7-check-single ≤ 0.66 %, cs-o-aot-ws-check-single ≤ 0.46 %) at commit 5d9e8a5, label `checker additional run 3 (after CO6, 5d9e8a5)`. **Additional run, not the endpoint:** the checker endpoint stays G = 1.680 [1.675, 1.689] (no parity, `results/phase3/checker-endpoint/2026-10-29T14-41-57-488Z/VERDICT.md`). Steps since additional run 2 (`../2026-11-01T06-48-39-651Z/NOTE.md`): CO5 step 1 and CO6 (type mappers as records, duplicate-declaration info without objects). Full tables: [report.md](report.md).

| Parse+bind+check (95 % percentile bootstrap, seed 20261021) | Endpoint | Run 2 (after CO4) | This run (after CO6) |
|---|---|---|---|
| cs-o-aot-ws-check vs tsgo7-check (G) | 1.680 [1.675, 1.689] | 1.025 [1.020, 1.030] | **0.970 [0.965, 0.976]**: upper bound < 1.00, **faster than typescript-go** |
| per repository | vscode 1.752, sentry 1.335, bluesky 1.901, playwright 2.008, tldraw 1.499 | vscode 1.037, sentry 0.796, bluesky 1.217, playwright 1.238, tldraw 0.910 | vscode 0.983, sentry 0.745, bluesky 1.168, playwright 1.168, tldraw 0.860 |
| single-threaded vs tsgo7-check-single | 0.954 [0.953, 0.956] | 0.818 [0.814, 0.819] | 0.800 [0.799, 0.802] |
| cs-o-aot-srv-check vs tsgo7-check (98.75 %) | 1.055 [1.047, 1.065] | 0.850 [0.844, 0.855] | 0.827 [0.821, 0.834] (bluesky 1.041, playwright 1.049) |
| srv single-threaded (98.75 %) | 0.871 [0.869, 0.873] | 0.810 [0.805, 0.814] | 0.794 [0.792, 0.797] |
| cs-o-aot-ws-check vs tsrs-check (secondary; tsrs: 16 checkers with work stealing) | 8.57 [8.52, 8.66] | 5.185 [5.129, 5.218] | 4.906 [4.868, 4.958] |
| single-threaded vs tsrs-check-single | 2.007 [2.004, 2.012] | 1.705 [1.700, 1.709] | 1.685 [1.683, 1.689] |

Peak working set G vs tsgo7-check 0.69 (vscode 4,839 MiB, typescript-go 6,999, tsrs 2,761). Repositories above 1.20: none (bluesky and playwright 1.168).

Parallel medians, Track O ws (run 2 → this run): vscode 15.01 → 14.04 s (typescript-go 14.29 s), sentry 3.834 → 3.58 s (4.80 s), bluesky 0.993 → 0.95 s (0.81 s), playwright 1.837 → 1.73 s (1.49 s), tldraw 1.777 → 1.68 s (1.95 s). Workstation GC pause (Table 5): vscode 3.9 → 3.31 s, bluesky 186 → 158 ms, playwright 337 → 258 ms; Server GC 602 / 51 / 123 ms.

Diagnostics (Table 1c): as in run 2 (single-threaded identical with tsc 6.0.3; parallel vscode 101,474, playwright 30,789; ws and srv equal). No variance notes (highest reported row: tsrs-check sentry 2.49 %). Disclosed differences as at the endpoint.
