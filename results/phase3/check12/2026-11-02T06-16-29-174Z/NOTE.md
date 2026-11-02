# Program check with 12 checkers, protocol run (2026-11-02) — Windows only

This is a protocol-compliant run of profile `check12`, pre-registration §18 (`prereg/v10`, tagged before this run).
- **Harness:** commit 61b36e9, label `check12 protocol run (prereg/v10, 12 checkers)`.
- **Rig checks:** ok; idle 3.9 %.
- **Variance gate:** PASS. The binding rows reach at most 0.49 % (`tsgo7-check-single`) and 0.55 % (`cs-o-aot-ws-check-single`). No reported row is above 3 %; the highest is `tsrs-check` playwright at 2.54 %.

**These comparisons are secondary.** The checker endpoint stays G = 1.680 [1.675, 1.689] (no parity, `../../checker-endpoint/2026-10-29T14-41-57-488Z/VERDICT.md`). The 4-checker additional runs are unaffected. N = 12 was chosen after the unregistered exploration CN1, which predicted ws 1.310 and srv 0.890; this run reproduces both. Full tables: [report.md](report.md).

| Parse+bind+check, 12 checkers on both sides (percentile bootstrap, seed 20261021) | Result |
|---|---|
| cs-o-aot-ws-check12 vs tsgo7-check12 (G, 95 %) | **1.305 [1.292, 1.315]**: lower bound > 1.20, **no parity at 12 checkers under Workstation GC** |
| per repository | vscode 1.293, sentry 1.095, bluesky 1.377, playwright 1.519, tldraw 1.278 |
| cs-o-aot-srv-check12 vs tsgo7-check12 (G, 98.75 %) | **0.890 [0.878, 0.900]**: upper bound ≤ 1.20, within 20 % of typescript-go, faster on the geometric mean |
| per repository | vscode 0.764, sentry 0.696, bluesky 1.104, playwright 1.064, tldraw 0.894 |
| cs-o-aot-srv-check12 vs cs-o-aot-ws-check12 | 0.682 [0.677, 0.687] |
| single-threaded, cs-o-aot-ws-check-single vs tsgo7-check-single (binding rows, as in profile `check`) | 0.807 [0.805, 0.808] |
| vs tsrs-check (secondary; tsrs: its default pool, work stealing) | ws12 4.149 [4.110, 4.183], srv12 2.828 [2.800, 2.852], tsgo7-check12 3.179 [3.148, 3.212] |

From 4 to 12 checkers (medians, additional run 2 at 4 checkers → this run):

| Repository | typescript-go | Track O ws | Track O srv |
|---|---|---|---|
| vscode | 14.48 → 8.85 s | 15.01 → 11.45 s | 11.84 → 6.76 s |
| sentry | 4.82 → 2.68 s | 3.83 → 2.94 s | 2.93 → 1.86 s |
| bluesky | 0.815 → 0.589 s | 0.993 → 0.812 s | 0.866 → 0.650 s |
| playwright | 1.48 → 0.977 s | 1.84 → 1.48 s | 1.61 → 1.04 s |
| tldraw | 1.95 → 1.19 s | 1.78 → 1.52 s | 1.52 → 1.07 s |

typescript-go gains 33–44 % from the extra checkers, and Track O with Server GC gains 25–43 %. Track O with Workstation GC gains only 15–24 %.

Under Workstation GC the pause grows with the checker count (Table 5). On vscode it is 5.80 s at 12 checkers against 3.9 s at 4, and 1.89 s single-threaded, within a wall time of 11.45 s. Gen0 and gen1 collections stop every checker thread and are collected by one thread. Server GC collects in parallel and pauses 0.80 s on vscode. On vscode, Track O with Server GC is the fastest C# or Go configuration at 6.76 s, against typescript-go's 8.85 s (tsrs: 2.19 s).

The two small programs, bluesky and playwright, stay slower than typescript-go even with Server GC (1.10 and 1.06).

Memory, peak working set on vscode:

| Configuration | MiB |
|---|---:|
| ws12 | 6,076 |
| srv12 | 6,443 |
| tsgo7-check12 | 7,921 |
| tsrs | 2,759 |

**Diagnostics (Table 1c).** Counts change with N on both sides, as CN1 found.
- tsgo7-check12 tldraw: 52,608 against 52,604 single-threaded.
- Track O at 12 checkers: vscode 101,471, playwright 30,798.
- Workstation and Server GC give the same counts.
- Single-threaded counts are unchanged and identical with tsc 6.0.3.
