# Checker additional run 1, after CO1–CO3 (2026-10-31) — Windows only

Protocol-compliant run (profile `check`, §17; rig checks ok, idle 6.1 %; variance gate PASS, binding rows tsgo7-check-single ≤ 0.55 %, cs-o-aot-ws-check-single ≤ 0.62 %) at harness commit 7ec02cf, label `checker additional run 1 (after CO3 and 7ec02cf)`. **Additional run, not the endpoint:** the checker endpoint stays G = 1.680 [1.675, 1.689] (no parity, `results/phase3/checker-endpoint/2026-10-29T14-41-57-488Z/VERDICT.md`). Steps since the endpoint: CO1 (checker scaling and allocations), CO2 (reference-free checker data), CO3 (object types and member lists without references), and the race fix 7ec02cf. Full tables: [report.md](report.md).

An earlier attempt of this run (results directory not written) was aborted in the validation run of `cs-o-aot-srv-check` on tldraw by an access violation: getEffectiveModifierFlags cached modifier flags in a file store shared by the four checkers (the same kind of race as b42ecc2). Fixed in 7ec02cf, followed by Gate C (`--sample 2000`) and the five-repository equivalence with tsc 6.0.3 (identical).

| Parse+bind+check (95 % percentile bootstrap, seed 20261021) | Endpoint | This run |
|---|---|---|
| cs-o-aot-ws-check vs tsgo7-check (G) | 1.680 [1.675, 1.689] | **1.068 [1.064, 1.070]**: upper bound ≤ 1.20, **within 20 % of typescript-go** |
| per repository | vscode 1.752, sentry 1.335, bluesky 1.901, playwright 2.008, tldraw 1.499 | vscode 1.056, sentry 0.842, bluesky 1.313, playwright 1.258, tldraw 0.943 |
| single-threaded vs tsgo7-check-single | 0.954 [0.953, 0.956] | 0.823 [0.822, 0.825] |
| cs-o-aot-srv-check vs tsgo7-check (98.75 %) | 1.055 [1.047, 1.065] | 0.914 [0.908, 0.917] (bluesky 1.181, playwright 1.096) |
| srv single-threaded | 0.871 [0.869, 0.873] | 0.810 [0.809, 0.812] |
| cs-o-aot-ws-check vs tsrs-check (secondary; tsrs: 16 checkers with work stealing) | 8.57 [8.52, 8.66] | 5.388 [5.342, 5.434] |
| single-threaded vs tsrs-check-single | 2.007 [2.004, 2.012] | 1.731 [1.727, 1.735] |

tsrs vs tsgo7-check 0.198, single-threaded 0.476 (as at the endpoint).

vscode parse+bind+check (medians): Track O 15.01 s (endpoint 25.0 s), Server GC 12.01 s, typescript-go 14.21 s, tsrs 2.21 s; single-threaded Track O 37.74 s, typescript-go 42.61 s. Track O on vscode allocates 15.7 GB (endpoint 20.2 GB) with 3.5 s of Workstation GC pause (endpoint 13.0 s); peak working set 4,825 MiB (typescript-go 6,996, tsrs 2,769). The remaining gap to typescript-go in parallel is on the small programs (bluesky, playwright), where repeated per-checker work on the libraries weighs more.

Diagnostics (Table 1c): single-threaded counts unchanged from the endpoint and identical with tsc 6.0.3 (check-equivalence.mjs after 7ec02cf). Parallel C# counts vscode 101,474 (endpoint 101,475) and playwright 30,789 (endpoint 30,791): with four checkers a file's diagnostics depend on which types its checker created before (C12); Workstation and Server GC give the same counts. Variance notes: none above 3 % (highest reported row: tsrs-check tldraw 2.75 %). Disclosed differences as at the endpoint.
