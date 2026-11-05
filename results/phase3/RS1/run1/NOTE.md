# RS1 orchestrator measurement (2026-11-05)

Baseline drivers of 5af33ed (built by the RS1 agent into its scratch `RS1/base/`) against 905b415 (src = be7fdc9, built here). Both builds with `WarningsNotAsErrors=NU1903` in the environment (author's decision of 2026-11-04). AOT ws.

Interleaved (`results/phase4/EO1/run1/follow-up/interleave.mjs`, script `run.sh`): 20 rounds after 2 warm-up rounds, every round runs base and final once, order rotated. Median ratio final/base, 95 % percentile bootstrap (`stats.mjs bootstrapMedianRatio`, `cmp.mjs`). No rig checks (not `bench.mjs`).

| Configuration | Base | Final | Ratio [95 %] |
|---|---:|---:|---|
| tuning-check `--check` (4 checkers) | 13.547 s | 13.441 s | 0.992 [0.984, 0.997] |
| tuning-check `--check --checkers 12` | 15.063 s | 14.648 s | 0.972 [0.968, 0.982] |
| tuning-check `--check --single` | 21.320 s | 21.051 s | 0.987 [0.985, 0.989] |
| tuning emit list `--check --emit` (4 checkers) | 13.121 s | 12.874 s | 0.981 [0.975, 0.989] |
| tuning emit list `--check --emit --single` | 21.837 s | 21.286 s | 0.975 [0.973, 0.978] |

**Keep rule:** no configuration is slower by the interval; all five are faster. Every gate passes (agent's report, phase1-progress.md RS1). **RS1 kept.**
