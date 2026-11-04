# EO1 orchestrator measurement (2026-11-04)

Baseline drivers of fcbb5dd (built by the EO1 agent into its scratch `EO1/base/`) against c11d738 (src = 940fc59, built here). Both builds with `WarningsNotAsErrors=NU1903` in the environment, because restore now fails on advisory GHSA-p3jp-9v83-76w9 for `Microsoft.DotNet.ILCompiler` 10.0.13; same compiler as all earlier results. Median ratio final/base, 95 % percentile bootstrap (`stats.mjs bootstrapMedianRatio`).

| Configuration | Base | Final | Ratio [95 %] |
|---|---:|---:|---|
| tuning emit list `--check --emit` (4 checkers), hyperfine 20 runs | 14.399 s | 13.900 s | 0.965 [0.960, 0.971] |
| tuning emit list `--check --emit --single`, hyperfine 20 runs | 25.999 s | 23.776 s | 0.914 [0.912, 0.916] |
| tuning-check `cs-o-aot-ws-check`, `bench.mjs` 20 runs, rig-compliant | 13.571 s | 13.612 s | 1.003 [0.996, 1.007] |
| tuning-check `cs-o-aot-ws-check-single`, `bench.mjs` 20 runs, rig-compliant | 21.358 s | 21.452 s | 1.004 [1.002, 1.008] |

- Not interleaved: the `bench.mjs` blocks ran final first, then baseline; each hyperfine invocation ran its 20 final runs, then its 20 base runs. (Correction: an earlier version of this note said "alternating" for hyperfine.)
- Diagnostics unchanged (130,435 parallel, 135,770 single); allocation and GC counts unchanged.
- **Keep rule:** `cs-o-aot-ws-check-single` is slower by the interval (+0.4 %). The decision is the author's (see phase1-progress.md, EO1).

## Follow-up (author's choice: check before deciding)

- **Calls:** an uncommitted probe build (counter in `GetAccessibleSymbolChain`, switch `EO1_NOCACHE` that skips the cache's key, lookup and store; removed afterwards) counts **673** calls (316 cache hits) in tuning-check `--check --single`, 700 in `--check` (4 checkers), against 20,390 (15,884 hits) in the tuning emit list `--check --emit --single`. Diagnostics with the cache bypassed: 135,770, unchanged. 673 calls cannot cost 85 ms.
- **Interleaved timing** (`follow-up/interleave.mjs`, 20 rounds, 2 warm-up rounds, every round runs each variant once, order rotated; tuning-check `--check --single`, AOT ws; `follow-up/il-check-single.json`):

| Variant | Median | Ratio [95 %] |
|---|---:|---|
| base (fcbb5dd) | 21.423 s | |
| final (c11d738) | 21.461 s | final/base 1.002 [0.999, 1.008] |
| probe, cache on | 21.431 s | probe-cache/base 1.000 [0.998, 1.004] |
| probe, cache bypassed | 21.526 s | probe-nocache/base 1.005 [1.003, 1.006] |

- Interleaved, final is not slower by the interval. The probe with the cache bypassed does no cache work at all and is still 0.5 % slower than base: differences of this size come from the build (code placement), not from the cache's work.
