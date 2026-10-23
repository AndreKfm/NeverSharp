# Where Track O's time goes against tsrs (development probe, not a protocol result), 2026-10-23

Track O at 51f5036 plus the probe switch `NEVERSHARP_PROBE=read` (read and decode every file as `Parser.ParseFile` does, no parse; bench.mjs never sets it); hyperfine --shell=none, 2 warmups, 10 runs, high priority; warm file cache. Raw: `io-<repo>-{read,full}.json`.

| vscode (sentry) | wall-clock | user CPU | system CPU |
|---|---:|---:|---:|
| Track O read only, single | 0.76 s (0.56 s) | 0.10 s | 0.66 s |
| Track O full parse, single | 4.95 s (2.07 s) | 3.87 s (1.34 s) | 1.08 s (0.72 s) |
| tsrs full parse, single | 2.49 s (1.28 s) | 1.36 s (0.54 s) | 1.12 s (0.73 s) |
| Track O read only, parallel | 0.13 s (0.10 s) | 0.58 s | 1.41 s |
| Track O full parse, parallel | 0.56 s (0.25 s) | 6.01 s (2.19 s) | 2.12 s (1.46 s) |
| tsrs full parse, parallel | 0.35 s (0.16 s) | 1.99 s (0.82 s) | 2.38 s (1.34 s) |

Reading and decoding is a small part (0.76 of 4.95 s single-threaded on vscode) and system time is the same for both. The gap to tsrs is user CPU in parsing: 3.87 s against 1.36 s on vscode (2.8×), 1.34 s against 0.54 s on sentry (2.5×); tsrs parses JSDoc in .ts files lazily (worth about 4 % for Track O, ../jsdoc/NOTE.md).

CPU profile (dotnet-trace sampled thread time, JIT build, vscode single-threaded, about 100 Hz, so coarse; `artifacts/profile/o-vscode-cpu.speedscope.json`): parse 54 % inclusive, reading 37 % (file open 23 %), scanner 10.7 %, speculation (`SpeculationHelper`) 8.5 %, `NodeStore.Seal` 6.9 % (exact copies: allocation 4.5 %, copy 2.4 %), parser pool (`ConcurrentBag`, `Monitor.Enter_Slowpath` in `GetParser`/`PutParser`) about 3 %, interning hash tables zeroed per file about 1 %; the rest spread thinly over the grammar.
