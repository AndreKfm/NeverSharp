# EO1 orchestrator measurement (2026-11-04)

Baseline drivers of fcbb5dd (built by the EO1 agent into its scratch `EO1/base/`) against c11d738 (src = 940fc59, built here). Both builds with `WarningsNotAsErrors=NU1903` in the environment, because restore now fails on advisory GHSA-p3jp-9v83-76w9 for `Microsoft.DotNet.ILCompiler` 10.0.13; same compiler as all earlier results. Median ratio final/base, 95 % percentile bootstrap (`stats.mjs bootstrapMedianRatio`).

| Configuration | Base | Final | Ratio [95 %] |
|---|---:|---:|---|
| tuning emit list `--check --emit` (4 checkers), hyperfine 20 runs, alternating | 14.399 s | 13.900 s | 0.965 [0.960, 0.971] |
| tuning emit list `--check --emit --single`, hyperfine 20 runs, alternating | 25.999 s | 23.776 s | 0.914 [0.912, 0.916] |
| tuning-check `cs-o-aot-ws-check`, `bench.mjs` 20 runs, rig-compliant | 13.571 s | 13.612 s | 1.003 [0.996, 1.007] |
| tuning-check `cs-o-aot-ws-check-single`, `bench.mjs` 20 runs, rig-compliant | 21.358 s | 21.452 s | 1.004 [1.002, 1.008] |

- `bench.mjs` blocks ran final first, then baseline (not interleaved). The hyperfine runs alternate final and base.
- Diagnostics unchanged (130,435 parallel, 135,770 single); allocation and GC counts unchanged.
- **Keep rule:** `cs-o-aot-ws-check-single` is slower by the interval (+0.4 %). The decision is the author's (see phase1-progress.md, EO1).
