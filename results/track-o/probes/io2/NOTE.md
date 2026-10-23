# Reading vs parsing against tsrs after O26 (development probe), 2026-10-23

Same method as `../io/NOTE.md` (Track O at 9321f51, `NEVERSHARP_PROBE=read` for read and decode only; hyperfine --shell=none, 2 warmups, 10 runs, high priority; warm file cache).

| vscode (sentry) | wall-clock | user CPU | system CPU |
|---|---:|---:|---:|
| Track O read only, single | 0.754 s (0.548 s) | 0.089 s (0.073 s) | 0.655 s (0.470 s) |
| Track O full parse, single | 2.726 s (1.322 s) | 1.788 s (0.691 s) | 0.930 s (0.619 s) |
| tsrs full parse, single | 2.491 s (1.262 s) | 1.317 s (0.545 s) | 1.164 s (0.700 s) |
| Track O full parse, parallel | 0.303 s (0.180 s) | 2.578 s (0.931 s) | 1.872 s (1.216 s) |
| tsrs full parse, parallel | 0.312 s (0.158 s) | 1.917 s (0.914 s) | 2.511 s (1.353 s) |

The gap to tsrs is still user CPU in parsing (vscode single-threaded 1.79 s against 1.32 s, of which reading is 0.09 s); Track O's system time is lower than tsrs's. First probe (before O1–O26): 3.87 s against 1.36 s.
