# AR2 measurement (orchestrator), 2026-11-05

Interleaved `--check`, 5 rounds after 1 warm-up, seven variants in rotated order per round, file lists `artifacts/bench/filelists/<repo>.txt`. Median wall time and GC pause; ratio = `bootstrapMedianRatio` (95 % interval). Scripts: `results/phase3/SC1/run1/inter.mjs`, `cmp.mjs`; variants from `mk.js`.

- `base-*`: drivers built from 501a52e (AR1 final + AR2 brief); `final-*`: built from 97ee73b (AR2 step 5; a6cd49e is docs only). The final aot-ws build carries the gen0 budget of 32 MB (06b78b8); aot-srv has no GC setting.
- `*-tsrs`: `--pool tsrs` (srv 16 checkers, ws 12). `final-ws-default`: default pool (4 checkers).
- `tsrs`, `tsgo`: `artifacts/bin/parse-rs.exe` / `parse-go.exe --check`.

Factor against tsrs (lower is better):

| repo | tsrs (s) | base srv | final srv | base ws | final ws | final ws default | tsgo |
|---|---:|---|---|---|---|---|---|
| bluesky | 0.208 | 2.30 | 2.31 [2.18, 2.60] | 2.63 | 2.38 [2.25, 2.73] | 3.75 | 3.99 |
| playwright | 0.339 | 2.03 | 2.05 [1.93, 2.49] | 2.79 | 2.58 [2.41, 2.72] | 4.14 | 4.59 |
| tldraw | 0.399 | 1.83 | 1.77 [1.67, 1.88] | 2.47 | 2.14 [2.01, 2.24] | 3.70 | 5.06 |
| sentry | 0.974 | 1.48 | 1.47 [1.43, 1.52] | 2.22 | 2.08 [2.01, 2.12] | 3.12 | 5.03 |
| vscode | 2.472 | 1.97 | 1.97 [1.83, 2.09] | 3.04 | 2.63 [2.44, 2.89] | 4.79 | 6.05 |
| geo. mean | | 1.90 | 1.89 | 2.62 | 2.35 | 3.86 | 4.90 |

Final against base: ws bluesky 0.907 [0.852, 1.040], playwright 0.922 [0.834, 0.948], tldraw 0.864 [0.843, 0.895], sentry 0.938 [0.906, 0.968], vscode 0.865 [0.783, 0.928] (pause 3.00 -> 2.00 s); srv 1.006, 1.010, 0.968, 0.993, 1.001 (unchanged within noise).
The machine was slower in these rounds than in AR1's (tsrs vscode 2.47 s against 2.25 s), so the base factors here differ from AR1's final ones; compare within this table.
