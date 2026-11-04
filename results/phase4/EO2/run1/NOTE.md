# EO2 orchestrator measurement (2026-11-04)

Baseline drivers of efa4038 (built by the EO2 agent into its scratch `EO2/base/`) against 0cf4ab9 (src = c5a9923, built here). Both builds with `WarningsNotAsErrors=NU1903` in the environment (author's decision of 2026-11-04). AOT ws.

Interleaved (`results/phase4/EO1/run1/follow-up/interleave.mjs`, script `run.sh`): 20 rounds after 2 warm-up rounds, every round runs base and final once, order rotated. Median ratio final/base, 95 % percentile bootstrap (`stats.mjs bootstrapMedianRatio`, `cmp.mjs`). No rig checks (not `bench.mjs`).

| Configuration | Base | Final | Ratio [95 %] |
|---|---:|---:|---|
| tuning emit list `--check --emit` (4 checkers) | 13.802 s | 13.115 s | 0.950 [0.945, 0.957] |
| tuning emit list `--check --emit --single` | 23.682 s | 21.587 s | 0.912 [0.908, 0.913] |
| tuning-check `--check` (4 checkers) | 13.652 s | 13.551 s | 0.993 [0.989, 0.998] |
| tuning-check `--check --single` | 21.371 s | 21.169 s | 0.991 [0.986, 0.993] |

**Keep rule:** no configuration is slower by the interval; all four are faster. Every gate passes (agent's report, phase1-progress.md EO2). **EO2 kept.**
