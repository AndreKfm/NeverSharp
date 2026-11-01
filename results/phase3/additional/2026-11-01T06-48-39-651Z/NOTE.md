# Checker additional run 2, after CO4 (2026-11-01) — Windows only

Protocol-compliant run (profile `check`, §17; rig checks ok, idle 4.2 %; variance gate PASS, binding rows tsgo7-check-single ≤ 1.10 %, cs-o-aot-ws-check-single ≤ 0.54 %) at harness commit db97ac0, label `checker additional run 2 (after CO4, db97ac0)`. **Additional run, not the endpoint:** the checker endpoint stays G = 1.680 [1.675, 1.689] (no parity, `results/phase3/checker-endpoint/2026-10-29T14-41-57-488Z/VERDICT.md`). Step since additional run 1 (`../2026-10-31T10-13-22-286Z/NOTE.md`): CO4 (parallel check of small programs: per-item checker construction, imports resolved after the walk and in parallel, pool checkers keep only their own files' diagnostics), kept by the author's decision despite tuning-check `--single` 1.011. Full tables: [report.md](report.md).

| Parse+bind+check (95 % percentile bootstrap, seed 20261021) | Endpoint | Run 1 (after CO3) | This run (after CO4) |
|---|---|---|---|
| cs-o-aot-ws-check vs tsgo7-check (G) | 1.680 [1.675, 1.689] | 1.068 [1.064, 1.070] | **1.025 [1.020, 1.030]**: upper bound ≤ 1.20, **within 20 % of typescript-go** |
| per repository | vscode 1.752, sentry 1.335, bluesky 1.901, playwright 2.008, tldraw 1.499 | vscode 1.056, sentry 0.842, bluesky 1.313, playwright 1.258, tldraw 0.943 | vscode 1.037, sentry 0.796, bluesky 1.217, playwright 1.238, tldraw 0.910 |
| single-threaded vs tsgo7-check-single | 0.954 [0.953, 0.956] | 0.823 [0.822, 0.825] | 0.818 [0.814, 0.819] |
| cs-o-aot-srv-check vs tsgo7-check (98.75 %) | 1.055 [1.047, 1.065] | 0.914 [0.908, 0.917] (bluesky 1.181, playwright 1.096) | 0.850 [0.844, 0.855] (bluesky 1.062, playwright 1.086) |
| srv single-threaded | 0.871 [0.869, 0.873] | 0.810 [0.809, 0.812] | 0.810 [0.806, 0.813] |
| cs-o-aot-ws-check vs tsrs-check (secondary; tsrs: 16 checkers with work stealing) | 8.57 [8.52, 8.66] | 5.388 [5.342, 5.434] | 5.185 [5.129, 5.218] |
| single-threaded vs tsrs-check-single | 2.007 [2.004, 2.012] | 1.731 [1.727, 1.735] | 1.705 [1.700, 1.709] |

tsrs vs tsgo7-check 0.198, single-threaded 0.480 (as before).

Parallel medians, Track O ws (run 1 → this run): bluesky 1.06 → 0.993 s (typescript-go 0.815 s), playwright 1.85 → 1.837 s (1.483 s), tldraw 1.83 → 1.777 s (1.953 s), sentry 3.834 s (4.817 s), vscode 15.01 → 15.01 s (14.48 s). CO4 helps bluesky most; playwright barely moves and stays the largest gap with bluesky. With Server GC both are within 9 % of typescript-go (bluesky 0.866 s, playwright 1.610 s), so the Workstation GC pause is still the main part of the parallel gap on small programs (ws pause bluesky 186 ms, playwright 337 ms; srv 69 / 124 ms).

vscode parse+bind+check (medians): Track O 15.01 s, Server GC 11.84 s, typescript-go 14.48 s, tsrs 2.28 s; single-threaded Track O 37.95 s, typescript-go 42.72 s. Track O on vscode allocates 16.0 GB (run 1 15.7 GB) with 3.9 s of Workstation GC pause (run 1 3.5 s); peak working set 4,796 MiB (typescript-go 6,991, tsrs 2,746).

Diagnostics (Table 1c): single-threaded counts unchanged and identical with tsc 6.0.3 (CO4's check-equivalence.mjs on the five repositories). Parallel C# counts as in run 1 (vscode 101,474, playwright 30,789; C12: with four checkers a file's diagnostics depend on which types its checker created before); Workstation and Server GC give the same counts. Variance notes: none above 3 % (highest reported row: tsrs-check vscode 2.26 %). Disclosed differences as at the endpoint.
