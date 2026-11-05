# Quick estimate after EO1, EO2 and RS1 (2026-11-05), not protocol-compliant

Commit e8606ef. 5 runs, 1 warm-up, 1 memory run (`quick.sh`); only the Workstation GC rows of Track O and their typescript-go and tsrs references, measured in the same session. **An estimate, not an additional run:** fewer runs than the protocol (20), no single-threaded or Server GC rows. Variance gate of the reported rows: pass. Endpoints unchanged (checker 1.680, check12 1.305, emitter 1.030).

| G over the five repositories | Last additional run | This estimate |
|---|---|---|
| cs-o-aot-ws-check vs tsgo7-check | 0.970 (after CO6) | 0.95 [0.94, 0.97] |
| cs-o-aot-ws-check12 vs tsgo7-check12 | 1.236 (after CO6) | 1.20 [1.18, 1.23] |
| cs-o-aot-ws-emit vs tsgo7-emit | 1.030 (emit endpoint run) | 0.96 [0.95, 0.97] |
| cs-o-aot-ws-emit vs tsrs-emit | 5.057 | 4.69 [4.60, 4.77] |
| cs-o-aot-ws-check vs tsrs-check (from Table 1 medians) | 4.906 | about 4.74 |

Per repository (check / check12 / emit vs typescript-go): vscode 0.96 / 1.17 / 0.93, sentry 0.75 / 1.06 / 0.81, bluesky 1.18 / 1.29 / 1.15, playwright 1.06 / 1.33 / 1.05, tldraw 0.85 / 1.19 / 0.89. Workstation GC pause vscode: check 3.21 s, check12 4.81 s, emit 4.23 s.
