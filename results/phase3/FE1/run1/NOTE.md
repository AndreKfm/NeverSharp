# FE1 measurement (orchestrator), 2026-11-05

Interleaved `--check`, 5 rounds after 1 warm-up, all seven variants in rotated order per round, file lists `artifacts/bench/filelists/<repo>.txt`. Median wall time; ratio = `bootstrapMedianRatio` (95 % interval). Scripts: `results/phase3/SC1/run1/inter.mjs`, `cmp.mjs`; variants from `mk.js`.

- `base-*`: drivers built from 65a6d4c (SC1 + FE1 brief); `final-*`: built from 926f484 (FE1 step 4; 4c5d971 is docs only).
- `*-tsrs`: `--pool tsrs` (locality + stealing; srv 16 checkers, ws 12). `final-ws-default`: default pool (4 checkers, *i* mod 4).
- `tsrs`, `tsgo`: `artifacts/bin/parse-rs.exe` / `parse-go.exe --check`.

Factor against tsrs (lower is better):

| repo | tsrs (s) | base srv | final srv | base ws | final ws | final ws default | tsgo |
|---|---:|---|---|---|---|---|---|
| bluesky | 0.206 | 2.65 | 2.37 [2.15, 2.44] | 3.07 | 2.84 [2.69, 2.95] | 4.06 | 3.95 |
| playwright | 0.307 | 2.47 | 2.31 [2.09, 2.56] | 3.40 | 3.27 [2.98, 3.42] | 4.87 | 4.81 |
| tldraw | 0.383 | 2.05 | 1.87 [1.66, 2.02] | 2.90 | 2.84 [2.52, 2.95] | 4.17 | 5.12 |
| sentry | 0.979 | 1.76 | 1.56 [1.48, 1.63] | 2.78 | 2.44 [2.32, 2.55] | 3.40 | 5.00 |
| vscode | 2.299 | 2.31 | 2.23 [2.12, 2.58] | 3.78 | 3.62 [3.53, 3.94] | 5.67 | 6.26 |
| geo. mean | | 2.22 | 2.04 | 3.17 | 2.97 | 4.37 | 4.98 |

Final against base: srv bluesky 0.896, playwright 0.933, tldraw 0.912, sentry 0.885, vscode 0.965 [0.881, 1.063]; ws 0.925, 0.961, 0.978, 0.877, 0.957.
Against tsgo (final): srv 0.31-0.60, ws 0.49-0.72.
