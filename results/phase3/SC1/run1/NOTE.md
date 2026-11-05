# SC1 measurement (orchestrator), 2026-11-05

Interleaved `--check`, 5 rounds after 1 warm-up, all six variants in rotated order per round, file lists `artifacts/bench/filelists/<repo>.txt`. Median wall time; ratio = `bootstrapMedianRatio` (95 % interval) against tsrs.

- `base-ws`, `base-srv`: drivers built from 30dff92 (default pool: 4 checkers, *i* mod 4).
- `ws-tsrs`, `srv-tsrs`: drivers built from 18495f7 with `--pool tsrs` (locality + stealing; 12 checkers ws, 16 srv).
- `tsrs`, `tsgo`: `artifacts/bin/parse-rs.exe` / `parse-go.exe --check` (their defaults).

Factor against tsrs (lower is better):

| repo | tsrs (s) | base-ws | base-srv | ws-tsrs | srv-tsrs | tsgo |
|---|---:|---|---|---|---|---|
| bluesky | 0.212 | 4.24 [3.90, 4.84] | 3.84 [3.53, 4.01] | 3.05 [2.80, 3.17] | 2.45 [2.19, 2.70] | 3.88 |
| playwright | 0.323 | 4.80 [4.53, 5.01] | 4.29 [4.05, 4.49] | 3.18 [3.00, 3.32] | 2.27 [2.15, 2.55] | 4.67 |
| tldraw | 0.394 | 4.19 [3.93, 4.32] | 3.67 [3.48, 3.80] | 2.83 [2.69, 3.04] | 1.96 [1.84, 2.09] | 5.01 |
| sentry | 0.951 | 3.66 [3.54, 3.70] | 2.95 [2.83, 2.98] | 2.74 [2.65, 2.81] | 1.71 [1.64, 1.81] | 5.09 |
| vscode | 2.275 | 5.91 [5.61, 6.03] | 4.80 [4.55, 4.95] | 3.83 [3.63, 4.01] | 2.30 [2.19, 2.78] | 6.35 |
| geo. mean | | 4.50 | 3.86 | 3.10 | 2.12 | 4.94 |

Against tsgo: ws-tsrs 0.54-0.79, srv-tsrs 0.34-0.63. Raw rounds in `il-<repo>.json`; scripts `inter.mjs`, `cmp.mjs`, `run.sh`.
The parallel diagnostics with `--pool tsrs` differ from the default pool's and, with Server GC and stealing, vary from run to run (SC1 progress entry).
