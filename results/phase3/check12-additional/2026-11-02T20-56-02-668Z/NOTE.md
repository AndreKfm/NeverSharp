# Program check with 12 checkers, additional run 1, after CO6 (2026-11-02) — Windows only

Protocol-compliant run (profile `check12`, §18 `prereg/v10`; rig checks ok, idle 4.0 %; variance gate PASS, binding rows tsgo7-check-single ≤ 0.90 %, cs-o-aot-ws-check-single ≤ 0.55 %) at commit 5d9e8a5, label `check12 additional run 1 (after CO6, 5d9e8a5)`. **Additional run, not the protocol result of §18:** that stays G = 1.305 [1.292, 1.315] (no parity, `results/phase3/check12/2026-11-02T06-16-29-174Z/NOTE.md`). Step since: CO6 (type mappers as records, duplicate-declaration info without objects). Full tables: [report.md](report.md) (Table 2f).

| Parse+bind+check, 12 checkers (95 % percentile bootstrap, seed 20261021) | §18 protocol run | This run (after CO6) |
|---|---|---|
| cs-o-aot-ws-check12 vs tsgo7-check12 (G) | 1.305 [1.292, 1.315] | **1.236 [1.226, 1.244]**: lower bound > 1.20, **no parity** |
| per repository | vscode 1.293, sentry 1.095, bluesky 1.377, playwright 1.519, tldraw 1.278 | vscode 1.239, sentry 1.034, bluesky 1.335, playwright 1.400, tldraw 1.206 |
| cs-o-aot-srv-check12 vs tsgo7-check12 (98.75 %) | 0.890 [0.878, 0.900] | 0.889 [0.878, 0.897] (bluesky 1.128, playwright 1.039) |
| cs-o-aot-srv-check12 vs cs-o-aot-ws-check12 | 0.682 | 0.719 |
| single-threaded vs tsgo7-check-single | 0.807 [0.805, 0.808] | 0.798 [0.797, 0.801] |
| cs-o-aot-ws-check12 vs tsrs-check | 4.149 | 3.935 [3.895, 3.960] |
| cs-o-aot-srv-check12 vs tsrs-check | 2.828 | 2.830 [2.797, 2.848] |
| tsgo7-check12 vs tsrs-check | 3.179 | 3.183 [3.151, 3.209] |

Peak working set G vs tsgo7-check12 0.77 (vscode 6,128 MiB, typescript-go 7,881). Repositories above 1.20: vscode, bluesky, playwright, tldraw.

Medians, Track O ws with 12 checkers (§18 run → this run): vscode 11.45 → 10.85 s (typescript-go 8.76 s, Server GC 6.48 s). Workstation GC pause on vscode 5.80 → 5.05 s (Table 5; Server GC 0.78 s). Server GC unchanged: CO6 removes pause that Server GC did not have.

Diagnostics (Table 1c): as in the §18 run (C# with 12 checkers: vscode 101,471, playwright 30,798; tsgo7-check12 tldraw 52,608). No variance notes (highest reported row: tsrs-check playwright 2.61 %).
