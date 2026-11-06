# MM1 measurement (orchestrator), 2026-11-06

Interleaved `--check`, 5 rounds after 1 warm-up, eight variants in rotated order per round, file lists `artifacts/bench/filelists/<repo>.txt`. Median wall time and GC pause; ratio = `bootstrapMedianRatio` (95 % interval). Scripts: `results/phase3/SC1/run1/inter.mjs`, `cmp.mjs`; variants from `mk.js`.

- `base-*`: drivers built from af5e773 (AR2 + PC1 + MM1 brief); `final-*`: built by the orchestrator from 151538e (MM1 step 9 2fe9c1d + docs). aot-ws carries the gen0 budget of 32 MB (AR2 step 0) on both sides.
- `*-tsrs`: `--pool tsrs` (srv 16 checkers, ws 12). `final-ws-default`: default pool (4 checkers). `final-ws-single`: `--single`, against tsrs's parallel run.
- `tsrs`, `tsgo`: `artifacts/bin/parse-rs.exe` / `parse-go.exe --check`.

Factor against tsrs (lower is better):

| repo | tsrs (s) | base srv | final srv | base ws | final ws | final ws default | final ws single | tsgo |
|---|---:|---|---|---|---|---|---|---|
| bluesky | 0.206 | 2.19 | 2.12 [2.00, 2.44] | 2.33 | 2.15 [2.05, 2.36] | 3.65 | 9.43 | 3.98 |
| playwright | 0.310 | 2.07 | 2.05 [1.88, 2.21] | 2.53 | 2.30 [2.14, 2.40] | 4.18 | 10.40 | 4.84 |
| tldraw | 0.395 | 1.69 | 1.63 [1.52, 1.76] | 2.12 | 2.00 [1.88, 2.12] | 3.55 | 9.25 | 5.03 |
| sentry | 0.932 | 1.49 | 1.43 [1.34, 1.49] | 2.08 | 1.91 [1.79, 1.93] | 3.11 | 9.95 | 5.18 |
| vscode | 2.208 | 2.11 | 1.93 [1.87, 2.12] | 2.77 | 2.46 [2.35, 2.55] | 4.92 | 14.99 | 6.56 |
| geo. mean | | 1.89 | 1.81 | 2.35 | 2.15 | 3.84 | 10.62 | 5.05 |

Final against base: ws bluesky 0.924 [0.800, 1.012], playwright 0.908 [0.870, 0.962], tldraw 0.945 [0.903, 0.998], sentry 0.916 [0.897, 0.940], vscode 0.887 [0.863, 0.912] (pause 1.85 -> 1.31 s); srv 0.968, 0.992, 0.964, 0.958 [0.931, 1.016], vscode 0.912 [0.866, 1.005] (pause 0.51 -> 0.40 s).
