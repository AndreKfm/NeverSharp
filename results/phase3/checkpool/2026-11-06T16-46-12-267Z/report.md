# Parse-only benchmark, run 2026-11-06T16-46-12-267Z

**Status: protocol result.** All rig checks passed.

Label: checkpool protocol run (prereg/v12)

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 3.4 % (median of 1 s windows: 3.9, 2.2, 4.5, 8.1, 6.4, 2.4, 2.8, 2.5, 11.4, 1.9) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 6920a1c43f, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 3.4 % (range 1.9-11.4 %) | ok |
| harness-committed | clean working tree | clean | ok |

## Corpus

| Repository | Commit | Files | MB |
| --- | --- | ---: | ---: |
| vscode | `f507e492b8` | 11724 | 141.2 |
| sentry | `b4e1ddf51f` | 8932 | 45.9 |
| bluesky | `ea3d6827c7` | 1688 | 7.2 |
| playwright | `a061d96365` | 1415 | 17.2 |
| tldraw | `9986114e34` | 2508 | 13.6 |

Configurations:

- `tsgo7-check`: typescript-go 7.0.2, program parse+bind+check, parallel (default checker pool)
- `tsgo7-check-single`: typescript-go 7.0.2, program parse+bind+check, single-threaded
- `tsgo7-check12`: typescript-go 7.0.2, program parse+bind+check, parallel (12 checkers)
- `tsrs-check`: tsrs 0.18.0 (Rust), program parse+bind+check, parallel (tsrs's default checker pool)
- `cs-o-aot-ws-check-single`: C# Track O, NativeAOT, Workstation GC, program parse+bind+check, single-threaded
- `cs-o-aot-ws-checkpool`: C# Track O, NativeAOT, Workstation GC, program parse+bind+check, parallel (--pool tsrs: 12 checkers, locality, stealing)
- `cs-o-aot-srv-checkpool`: C# Track O, NativeAOT, Server GC, program parse+bind+check, parallel (--pool tsrs: 16 checkers, locality, stealing)

## Table 1. Wall-clock, throughput, peak memory (parse-only; `*-check` rows: the file list as one program, parse+bind+check)

| Repository | Configuration | Median (s) | MAD (s) | MAD/median | Files/s | MB/s | Peak working set (MiB) | Peak commit (MiB) |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| vscode | tsgo7-check | 14.33 | 0.0903 | 0.63 % | 818 | 9.9 | 6983 | 7006 |
| vscode | tsgo7-check-single | 42.93 | 0.1081 | 0.25 % | 273 | 3.3 | 5964 | 5988 |
| vscode | tsgo7-check12 | 8.806 | 0.1303 | 1.48 % | 1331 | 16.0 | 7850 | 7876 |
| vscode | tsrs-check | 2.207 | 0.0263 | 1.19 % | 5313 | 64.0 | 2766 | 4448 |
| vscode | cs-o-aot-ws-check-single | 33.06 | 0.0347 | 0.10 % | 355 | 4.3 | 3745 | 4001 |
| vscode | cs-o-aot-ws-checkpool | 5.335 | 0.1320 | 2.47 % | 2198 | 26.5 | 4975 | 5578 |
| vscode | cs-o-aot-srv-checkpool | 4.336 | 0.0498 | 1.15 % | 2704 | 32.6 | 5346 | 5918 |
| sentry | tsgo7-check | 4.806 | 0.0185 | 0.38 % | 1858 | 9.6 | 2020 | 2043 |
| sentry | tsgo7-check-single | 17.52 | 0.0385 | 0.22 % | 510 | 2.6 | 1897 | 1910 |
| sentry | tsgo7-check12 | 2.667 | 0.0285 | 1.07 % | 3349 | 17.2 | 2165 | 2181 |
| sentry | tsrs-check | 0.9207 | 0.0214 | 2.33 % | 9701 | 49.9 | 995 | 2171 |
| sentry | cs-o-aot-ws-check-single | 9.289 | 0.0106 | 0.11 % | 962 | 4.9 | 1153 | 1196 |
| sentry | cs-o-aot-ws-checkpool | 1.754 | 0.0261 | 1.49 % | 5092 | 26.2 | 1489 | 1761 |
| sentry | cs-o-aot-srv-checkpool | 1.333 | 0.0265 | 1.99 % | 6702 | 34.5 | 1717 | 2058 |
| bluesky | tsgo7-check | 0.8122 | 0.0051 | 0.63 % | 2078 | 8.9 | 427 | 431 |
| bluesky | tsgo7-check-single | 2.261 | 0.0070 | 0.31 % | 746 | 3.2 | 402 | 406 |
| bluesky | tsgo7-check12 | 0.5820 | 0.0121 | 2.07 % | 2900 | 12.4 | 465 | 471 |
| bluesky | tsrs-check | 0.1990 | 0.0037 | 1.85 % | 8482 | 36.1 | 259 | 834 |
| bluesky | cs-o-aot-ws-check-single | 1.917 | 0.0073 | 0.38 % | 881 | 3.8 | 277 | 307 |
| bluesky | cs-o-aot-ws-checkpool | 0.4417 | 0.0125 | 2.84 % | 3821 | 16.3 | 368 | 464 |
| bluesky | cs-o-aot-srv-checkpool | 0.4210 | 0.0140 | 3.32 % | 4009 | 17.1 | 447 | 558 |
| playwright | tsgo7-check | 1.478 | 0.0112 | 0.76 % | 957 | 11.7 | 895 | 901 |
| playwright | tsgo7-check-single | 3.948 | 0.0210 | 0.53 % | 358 | 4.4 | 791 | 796 |
| playwright | tsgo7-check12 | 0.9711 | 0.0185 | 1.90 % | 1457 | 17.7 | 1078 | 1087 |
| playwright | tsrs-check | 0.3066 | 0.0105 | 3.41 % | 4615 | 56.2 | 462 | 1221 |
| playwright | cs-o-aot-ws-check-single | 3.218 | 0.0117 | 0.36 % | 440 | 5.4 | 494 | 537 |
| playwright | cs-o-aot-ws-checkpool | 0.6856 | 0.0209 | 3.05 % | 2064 | 25.1 | 744 | 930 |
| playwright | cs-o-aot-srv-checkpool | 0.6078 | 0.0228 | 3.75 % | 2328 | 28.4 | 887 | 1084 |
| tldraw | tsgo7-check | 1.945 | 0.0095 | 0.49 % | 1289 | 7.0 | 858 | 865 |
| tldraw | tsgo7-check-single | 6.274 | 0.0182 | 0.29 % | 400 | 2.2 | 745 | 750 |
| tldraw | tsgo7-check12 | 1.186 | 0.0298 | 2.51 % | 2115 | 11.5 | 1022 | 1030 |
| tldraw | tsrs-check | 0.3756 | 0.0083 | 2.20 % | 6677 | 36.3 | 446 | 1197 |
| tldraw | cs-o-aot-ws-check-single | 3.635 | 0.0115 | 0.32 % | 690 | 3.7 | 451 | 477 |
| tldraw | cs-o-aot-ws-checkpool | 0.7540 | 0.0159 | 2.11 % | 3326 | 18.1 | 690 | 840 |
| tldraw | cs-o-aot-srv-checkpool | 0.6506 | 0.0235 | 3.61 % | 3855 | 20.9 | 816 | 989 |

## Table 1c. Program check: diagnostics reported (driver report of the validation run)

Semantic diagnostics of every program file plus the global diagnostics. The C# configurations check with TypeScript 6.0.3's checker (Track O, Gate C), typescript-go and tsrs with TypeScript 7's, which differs in places: the counts show that checking happened; the C# diagnostics were compared once with tsc 6.0.3's (harness/bench/check-equivalence.mjs).

| Configuration | vscode | sentry | bluesky | playwright | tldraw |
| --- | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | 101933 | 237531 | 33337 | 30838 | 52608 |
| tsgo7-check-single | 101933 | 237531 | 33337 | 30838 | 52604 |
| tsgo7-check12 | 101933 | 237531 | 33337 | 30838 | 52608 |
| tsrs-check | 101941 | 237533 | 33337 | 30838 | 52612 |
| cs-o-aot-ws-check-single | 101493 | 237529 | 33337 | 30808 | 51900 |
| cs-o-aot-ws-checkpool | 101489 | 237529 | 33337 | 30835 | 51900 |
| cs-o-aot-srv-checkpool | 101485 | 237529 | 33337 | 30800 | 51900 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |

## Table 2e. Program check: median wall-clock relative to `tsgo7-check`, with 95 % confidence interval

Endpoint of draft pre-registration §17: each repository's file list as one program, parse+bind+check, against typescript-go 7.0.2 (compiler.NewProgram, GetSemanticDiagnostics and GetGlobalDiagnostics on its default checker pool of 4 checkers). Same method as Table 2. typescript-go's checker ports TypeScript 7 and differs from 6.0.3's in places.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-check-single | 3.00 [2.98, 3.01] | 3.65 [3.63, 3.66] | 2.78 [2.76, 2.80] | 2.67 [2.66, 2.70] | 3.23 [3.20, 3.24] | 3.05 [3.04, 3.05] |
| tsgo7-check12 | 0.61 [0.61, 0.62] | 0.55 [0.55, 0.56] | 0.72 [0.71, 0.74] | 0.66 [0.65, 0.66] | 0.61 [0.60, 0.62] | 0.63 [0.62, 0.63] |
| tsrs-check | 0.15 [0.15, 0.16] | 0.19 [0.19, 0.20] | 0.25 [0.24, 0.25] | 0.21 [0.20, 0.21] | 0.19 [0.19, 0.20] | 0.20 [0.19, 0.20] |
| cs-o-aot-ws-check-single | 2.31 [2.29, 2.32] | 1.93 [1.93, 1.94] | 2.36 [2.34, 2.37] | 2.18 [2.16, 2.19] | 1.87 [1.85, 1.88] | 2.12 [2.11, 2.12] |
| cs-o-aot-ws-checkpool | 0.37 [0.37, 0.38] | 0.36 [0.36, 0.37] | 0.54 [0.53, 0.56] | 0.46 [0.46, 0.48] | 0.39 [0.38, 0.39] | 0.42 [0.42, 0.43] |
| cs-o-aot-srv-checkpool | 0.30 [0.30, 0.31] | 0.28 [0.27, 0.29] | 0.52 [0.51, 0.53] | 0.41 [0.40, 0.43] | 0.33 [0.33, 0.34] | 0.36 [0.36, 0.36] |

## Table 2f. Program check: median wall-clock relative to `tsgo7-check12`, with 95 % confidence interval

Pre-registration §18 (secondary): the same program check with 12 checkers on both sides (typescript-go 7.0.2 with CompilerOptions.Checkers = 12, Track O with 12 checkers; file i to checker i % 12 on both sides). Same method as Table 2.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | 1.63 [1.60, 1.64] | 1.80 [1.78, 1.82] | 1.40 [1.36, 1.41] | 1.52 [1.51, 1.55] | 1.64 [1.61, 1.67] | 1.59 [1.58, 1.60] |
| tsgo7-check-single | 4.87 [4.80, 4.92] | 6.57 [6.50, 6.64] | 3.89 [3.78, 3.93] | 4.07 [4.03, 4.14] | 5.29 [5.18, 5.38] | 4.85 [4.81, 4.88] |
| tsgo7-check12 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-check | 0.25 [0.25, 0.25] | 0.35 [0.34, 0.35] | 0.34 [0.33, 0.35] | 0.32 [0.31, 0.33] | 0.32 [0.31, 0.33] | 0.31 [0.31, 0.32] |
| cs-o-aot-ws-check-single | 3.75 [3.70, 3.78] | 3.48 [3.45, 3.52] | 3.29 [3.20, 3.33] | 3.31 [3.28, 3.37] | 3.06 [3.00, 3.12] | 3.37 [3.34, 3.40] |
| cs-o-aot-ws-checkpool | 0.61 [0.60, 0.62] | 0.66 [0.65, 0.67] | 0.76 [0.73, 0.79] | 0.71 [0.69, 0.74] | 0.64 [0.62, 0.65] | 0.67 [0.66, 0.68] |
| cs-o-aot-srv-checkpool | 0.49 [0.48, 0.50] | 0.50 [0.49, 0.52] | 0.72 [0.70, 0.75] | 0.63 [0.61, 0.65] | 0.55 [0.53, 0.56] | 0.57 [0.56, 0.58] |

## Table 1e. Program check with work stealing: diagnostics per run (determinism pass)

Untimed runs with --dump-diagnostics after the memory pass (draft pre-registration §20). With work stealing a file's checker depends on timing, and with it the diagnostics that depend on earlier types in that checker. Cells: diagnostic counts of the runs, then the number of distinct diagnostic dumps (SHA-256) out of the runs.

| Configuration | vscode | sentry | bluesky | playwright | tldraw |
| --- | --- | --- | --- | --- | --- |
| cs-o-aot-ws-checkpool | 101489; 1 of 5 distinct | 237529; 2 of 5 distinct | 33337; 1 of 5 distinct | 30826 / 30835 / 30829; 5 of 5 distinct | 51900; 2 of 5 distinct |
| cs-o-aot-srv-checkpool | 101485; 3 of 5 distinct | 237529; 1 of 5 distinct | 33337; 1 of 5 distinct | 30775 / 30755 / 30816 / 30791 / 30809; 5 of 5 distinct | 51900; 2 of 5 distinct |

## Table 2j. Program check: factor against `tsrs-check` (draft pre-registration §20, secondary), with 95 % confidence interval

C# median / tsrs median per repository (lower is better); same method as Table 2. tsrs 0.18.0 with its default checker pool (16 checkers on the reference machine, directory locality, work stealing); the C# `*-checkpool` configurations port that kind of pool (`--pool tsrs`: 12 checkers under Workstation GC, 16 under Server GC). The Track O optimization modules after prereg/v11 were tuned on these repositories (§20, disclosed).

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | 6.49 [6.39, 6.55] | 5.22 [5.10, 5.32] | 4.08 [4.01, 4.14] | 4.82 [4.67, 4.93] | 5.18 [5.07, 5.26] | 5.10 [5.04, 5.14] |
| tsgo7-check-single | 19.45 [19.17, 19.59] | 19.03 [18.59, 19.37] | 11.36 [11.17, 11.53] | 12.88 [12.50, 13.18] | 16.70 [16.34, 16.92] | 15.53 [15.36, 15.65] |
| tsgo7-check12 | 3.99 [3.93, 4.05] | 2.90 [2.82, 2.96] | 2.92 [2.86, 3.02] | 3.17 [3.05, 3.24] | 3.16 [3.07, 3.24] | 3.20 [3.16, 3.24] |
| cs-o-aot-ws-check-single | 14.98 [14.76, 15.07] | 10.09 [9.86, 10.26] | 9.63 [9.47, 9.77] | 10.50 [10.18, 10.72] | 9.68 [9.47, 9.81] | 10.81 [10.69, 10.89] |
| cs-o-aot-ws-checkpool | 2.42 [2.38, 2.48] | 1.91 [1.86, 1.95] | 2.22 [2.16, 2.30] | 2.24 [2.16, 2.35] | 2.01 [1.95, 2.05] | 2.15 [2.12, 2.18] |
| cs-o-aot-srv-checkpool | 1.97 [1.93, 1.99] | 1.45 [1.41, 1.50] | 2.12 [2.05, 2.18] | 1.98 [1.90, 2.08] | 1.73 [1.67, 1.78] | 1.83 [1.80, 1.86] |

## Table 2k. Program check with tsrs's kind of pool: comparisons of draft pre-registration §20

Geometric mean over the repositories of the ratio of medians, percentile bootstrap (10,000 resamples, seed 20261021) at the stated level (Server GC rows Bonferroni). All secondary; the endpoints of §17 and §19 are unchanged. The wording of §8 applies to the typescript-go rows.

| Comparison | Level | G [interval] |
| --- | ---: | ---: |
| `cs-o-aot-ws-checkpool` vs `tsrs-check` | 95 % | 2.15 [2.12, 2.18] |
| `cs-o-aot-srv-checkpool` vs `tsrs-check` | 98.75 % | 1.83 [1.80, 1.87] |
| `cs-o-aot-ws-checkpool` vs `tsgo7-check` | 95 % | 0.42 [0.42, 0.43] |
| `cs-o-aot-srv-checkpool` vs `tsgo7-check` | 98.75 % | 0.36 [0.35, 0.36] |
| `cs-o-aot-ws-checkpool` vs `tsgo7-check12` | 95 % | 0.67 [0.66, 0.68] |
| `cs-o-aot-srv-checkpool` vs `tsgo7-check12` | 98.75 % | 0.57 [0.56, 0.58] |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: checkpool. Binding rows (rig check): tsgo7-check-single, cs-o-aot-ws-check-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | reported | 0.63 % | 0.38 % | 0.63 % | 0.76 % | 0.49 % | 0.76 % |
| tsgo7-check-single | rig check | 0.25 % | 0.22 % | 0.31 % | 0.53 % | 0.29 % | 0.53 % |
| tsgo7-check12 | reported | 1.48 % | 1.07 % | 2.07 % | 1.90 % | 2.51 % | 2.51 % |
| tsrs-check | reported | 1.19 % | 2.33 % | 1.85 % | 3.41 % | 2.20 % | 3.41 % |
| cs-o-aot-ws-check-single | rig check | 0.10 % | 0.11 % | 0.38 % | 0.36 % | 0.32 % | 0.38 % |
| cs-o-aot-ws-checkpool | reported | 2.47 % | 1.49 % | 2.84 % | 3.05 % | 2.11 % | 3.05 % |
| cs-o-aot-srv-checkpool | reported | 1.15 % | 1.99 % | 3.32 % | 3.75 % | 3.61 % | 3.75 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-check | 0.0212 | 1.41 % | 2.6 % | 10 |
| tsgo7-check-single | 0.0212 | 1.16 % | 0.9 % | 10 |
| tsgo7-check12 | 0.0214 | 1.51 % | 3.7 % | 10 |
| tsrs-check | 0.0166 | 1.22 % | 8.4 % | 6 |
| cs-o-aot-ws-check-single | 0.0251 | 0.84 % | 1.3 % | 11 |
| cs-o-aot-ws-checkpool | 0.0241 | 2.77 % | 5.5 % | 10 |
| cs-o-aot-srv-checkpool | 0.0273 | 0.84 % | 6.5 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-check-single | 5856001160 | 113/44/6 | 735.6 |
| vscode | cs-o-aot-ws-checkpool | 7371288584 | 142/62/7 | 1282.2 |
| vscode | cs-o-aot-srv-checkpool | 7871719320 | 47/28/19 | 365.6 |
| sentry | cs-o-aot-ws-check-single | 1631723624 | 37/19/5 | 359.2 |
| sentry | cs-o-aot-ws-checkpool | 1980459280 | 45/30/6 | 582.8 |
| sentry | cs-o-aot-srv-checkpool | 2025486560 | 28/18/14 | 197.8 |
| bluesky | cs-o-aot-ws-check-single | 322993936 | 8/5/3 | 57.8 |
| bluesky | cs-o-aot-ws-checkpool | 445630872 | 12/9/4 | 113.9 |
| bluesky | cs-o-aot-srv-checkpool | 483145784 | 19/12/10 | 57.4 |
| playwright | cs-o-aot-ws-check-single | 679808744 | 16/8/4 | 76.1 |
| playwright | cs-o-aot-ws-checkpool | 1083488928 | 24/15/5 | 179.2 |
| playwright | cs-o-aot-srv-checkpool | 1091326656 | 26/16/13 | 77.3 |
| tldraw | cs-o-aot-ws-check-single | 721778336 | 18/9/4 | 125.5 |
| tldraw | cs-o-aot-ws-checkpool | 1065018864 | 25/17/5 | 217.5 |
| tldraw | cs-o-aot-srv-checkpool | 1100931672 | 28/18/14 | 98.8 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (35 files).
