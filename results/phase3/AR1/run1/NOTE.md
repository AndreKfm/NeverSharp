# AR1 measurement (orchestrator), 2026-11-05

Interleaved `--check`, 5 rounds after 1 warm-up, seven variants in rotated order per round, file lists `artifacts/bench/filelists/<repo>.txt`. Median wall time and GC pause; ratio = `bootstrapMedianRatio` (95 % interval). Scripts: `results/phase3/SC1/run1/inter.mjs`, `cmp.mjs`; variants from `mk.js`.

- `base-*`: drivers built from 13a5cc9 (FE1 + AR1 brief); `final-*`: built from 129a74a (AR1 step 6; 28bfcf5 is docs only).
- `*-tsrs`: `--pool tsrs` (srv 16 checkers, ws 12). `final-ws-default`: default pool (4 checkers).
- `tsrs`, `tsgo`: `artifacts/bin/parse-rs.exe` / `parse-go.exe --check`.

Factor against tsrs (lower is better):

| repo | tsrs (s) | base srv | final srv | base ws | final ws | final ws default | tsgo |
|---|---:|---|---|---|---|---|---|
| bluesky | 0.207 | 2.59 | 2.25 [2.09, 2.47] | 2.87 | 2.55 [2.48, 2.70] | 3.90 | 3.96 |
| playwright | 0.316 | 2.20 | 2.15 [2.03, 2.24] | 3.13 | 2.81 [2.71, 2.98] | 4.49 | 4.68 |
| tldraw | 0.388 | 1.90 | 1.82 [1.77, 1.93] | 2.64 | 2.40 [2.37, 2.46] | 3.89 | 5.11 |
| sentry | 0.981 | 1.52 | 1.46 [1.39, 1.55] | 2.43 | 2.06 [2.01, 2.15] | 3.13 | 4.98 |
| vscode | 2.250 | 2.24 | 2.10 [2.03, 2.26] | 3.70 | 3.16 [3.05, 3.30] | 5.44 | 6.36 |
| geo. mean | | 2.06 | 1.93 | 2.92 | 2.57 | 4.10 | 4.96 |

Final against base: ws bluesky 0.889, playwright 0.899, tldraw 0.908, sentry 0.847, vscode 0.856 (pause 3.96 -> 2.91 s); srv 0.870, 0.977, 0.954, 0.958, 0.936 (vscode pause 0.74 -> 0.47 s).
Against tsgo (final): srv 0.29-0.57, ws 0.42-0.64.
