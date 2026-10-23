# JSDoc probe (development measurement, not a protocol result), 2026-10-23

Question: how much of the Track O endpoint (G = 0.60 [0.59, 0.61], cs-o-aot-ws vs tsgo7-parse) comes from the disclosed difference in JSDoc work (C# tracks: `ParseForTypeErrors` as tsc's host; typescript-go 7.0.2: eager JSDoc)?

Method: `probe.mjs` (hyperfine as bench.mjs: --shell=none, 3 warmups, 20 runs, high priority), Track O driver at 51f5036 plus the probe switch `NEVERSHARP_JSDOC=all` (`JSDocParsingMode.ParseAll`; bench.mjs never sets it), five benchmark repositories, geometric mean of median ratios with the 95 % bootstrap of stats.mjs. No rig check, no variance gate.

| Ratio | G [95 %] |
|---|---|
| Track O ParseAll / tsgo7-parse | 0.624 [0.614, 0.633] |
| Track O default / tsgo7-parse | 0.601 [0.592, 0.607] |
| Track O ParseAll / Track O default | 1.039 [1.026, 1.053] |

Per repository (median s, ParseAll / default / tsgo): vscode 0.627 / 0.585 / 1.107, sentry 0.279 / 0.277 / 0.441, bluesky 0.070 / 0.070 / 0.113, playwright 0.102 / 0.091 / 0.156, tldraw 0.104 / 0.105 / 0.160.

Result: parsing every JSDoc comment costs Track O about 4 %; with it, Track O still takes about 0.62 of typescript-go's wall-clock. The JSDoc difference does not explain the endpoint. On vscode, single-threaded: nodes 16,928,765 → 16,988,240, allocated 668 → 688 MB.
