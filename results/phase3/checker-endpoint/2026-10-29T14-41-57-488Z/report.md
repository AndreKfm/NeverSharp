# Parse-only benchmark, run 2026-10-29T14-41-57-488Z

**Status: protocol result.** All rig checks passed.

Label: checker endpoint (prereg/v9)

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 4.6 % (median of 1 s windows: 9.2, 4.9, 4.1, 7.4, 11.3, 4.3, 5.1, 1.3, 2.5, 0.8) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit b42ecc2b63, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 4.6 % (range 0.8-11.3 %) | ok |
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
- `tsrs-check`: tsrs 0.18.0 (Rust), program parse+bind+check, parallel (tsrs's default checker pool)
- `tsrs-check-single`: tsrs 0.18.0 (Rust), program parse+bind+check, single-threaded
- `cs-o-aot-ws-check`: C# Track O, NativeAOT, Workstation GC, program parse+bind+check, parallel (4 checkers) (checker endpoint)
- `cs-o-aot-ws-check-single`: C# Track O, NativeAOT, Workstation GC, program parse+bind+check, single-threaded
- `cs-o-aot-srv-check`: C# Track O, NativeAOT, Server GC, program parse+bind+check, parallel (4 checkers)
- `cs-o-aot-srv-check-single`: C# Track O, NativeAOT, Server GC, program parse+bind+check, single-threaded

## Table 1. Wall-clock, throughput, peak memory (parse-only; `*-check` rows: the file list as one program, parse+bind+check)

| Repository | Configuration | Median (s) | MAD (s) | MAD/median | Files/s | MB/s | Peak working set (MiB) | Peak commit (MiB) |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| vscode | tsgo7-check | 14.25 | 0.1111 | 0.78 % | 823 | 9.9 | 7067 | 7085 |
| vscode | tsgo7-check-single | 42.52 | 0.1267 | 0.30 % | 276 | 3.3 | 6141 | 6158 |
| vscode | tsrs-check | 2.221 | 0.0478 | 2.15 % | 5278 | 63.6 | 2767 | 4529 |
| vscode | tsrs-check-single | 19.21 | 0.0642 | 0.33 % | 610 | 7.3 | 2164 | 2628 |
| vscode | cs-o-aot-ws-check | 24.97 | 0.1094 | 0.44 % | 470 | 5.7 | 6636 | 7293 |
| vscode | cs-o-aot-ws-check-single | 43.93 | 0.1519 | 0.35 % | 267 | 3.2 | 5717 | 5985 |
| vscode | cs-o-aot-srv-check | 14.42 | 0.0982 | 0.68 % | 813 | 9.8 | 7294 | 7884 |
| vscode | cs-o-aot-srv-check-single | 38.85 | 0.2152 | 0.55 % | 302 | 3.6 | 6187 | 6592 |
| sentry | tsgo7-check | 4.820 | 0.0131 | 0.27 % | 1853 | 9.5 | 2028 | 2043 |
| sentry | tsgo7-check-single | 17.43 | 0.0456 | 0.26 % | 512 | 2.6 | 1945 | 1959 |
| sentry | tsrs-check | 0.9305 | 0.0167 | 1.79 % | 9600 | 49.4 | 997 | 2223 |
| sentry | tsrs-check-single | 8.158 | 0.0115 | 0.14 % | 1095 | 5.6 | 886 | 1333 |
| sentry | cs-o-aot-ws-check | 6.436 | 0.0276 | 0.43 % | 1388 | 7.1 | 1759 | 2024 |
| sentry | cs-o-aot-ws-check-single | 12.49 | 0.0336 | 0.27 % | 715 | 3.7 | 1608 | 1683 |
| sentry | cs-o-aot-srv-check | 3.927 | 0.0319 | 0.81 % | 2274 | 11.7 | 2047 | 2378 |
| sentry | cs-o-aot-srv-check-single | 11.47 | 0.0513 | 0.45 % | 779 | 4.0 | 1941 | 2101 |
| bluesky | tsgo7-check | 0.8270 | 0.0103 | 1.24 % | 2041 | 8.7 | 430 | 431 |
| bluesky | tsgo7-check-single | 2.251 | 0.0117 | 0.52 % | 750 | 3.2 | 397 | 394 |
| bluesky | tsrs-check | 0.2018 | 0.0036 | 1.80 % | 8366 | 35.7 | 259 | 821 |
| bluesky | tsrs-check-single | 1.230 | 0.0068 | 0.55 % | 1373 | 5.9 | 199 | 239 |
| bluesky | cs-o-aot-ws-check | 1.572 | 0.0157 | 1.00 % | 1074 | 4.6 | 399 | 478 |
| bluesky | cs-o-aot-ws-check-single | 2.641 | 0.0051 | 0.19 % | 639 | 2.7 | 346 | 374 |
| bluesky | cs-o-aot-srv-check | 1.076 | 0.0247 | 2.30 % | 1568 | 6.7 | 547 | 675 |
| bluesky | cs-o-aot-srv-check-single | 2.510 | 0.0086 | 0.34 % | 673 | 2.9 | 485 | 556 |
| playwright | tsgo7-check | 1.496 | 0.0145 | 0.97 % | 946 | 11.5 | 909 | 909 |
| playwright | tsgo7-check-single | 3.950 | 0.0160 | 0.41 % | 358 | 4.4 | 813 | 811 |
| playwright | tsrs-check | 0.3073 | 0.0068 | 2.21 % | 4605 | 56.1 | 461 | 1237 |
| playwright | tsrs-check-single | 1.859 | 0.0077 | 0.41 % | 761 | 9.3 | 348 | 406 |
| playwright | cs-o-aot-ws-check | 3.003 | 0.0183 | 0.61 % | 471 | 5.7 | 876 | 1038 |
| playwright | cs-o-aot-ws-check-single | 4.712 | 0.0151 | 0.32 % | 300 | 3.7 | 679 | 739 |
| playwright | cs-o-aot-srv-check | 1.925 | 0.0414 | 2.15 % | 735 | 9.0 | 1111 | 1343 |
| playwright | cs-o-aot-srv-check-single | 4.241 | 0.0168 | 0.40 % | 334 | 4.1 | 813 | 935 |
| tldraw | tsgo7-check | 1.961 | 0.0161 | 0.82 % | 1279 | 6.9 | 840 | 841 |
| tldraw | tsgo7-check-single | 6.190 | 0.0130 | 0.21 % | 405 | 2.2 | 741 | 740 |
| tldraw | tsrs-check | 0.3766 | 0.0083 | 2.21 % | 6659 | 36.2 | 445 | 1198 |
| tldraw | tsrs-check-single | 2.764 | 0.0106 | 0.38 % | 907 | 4.9 | 330 | 405 |
| tldraw | cs-o-aot-ws-check | 2.939 | 0.0270 | 0.92 % | 853 | 4.6 | 797 | 929 |
| tldraw | cs-o-aot-ws-check-single | 4.718 | 0.0056 | 0.12 % | 532 | 2.9 | 618 | 643 |
| tldraw | cs-o-aot-srv-check | 1.856 | 0.0412 | 2.22 % | 1351 | 7.3 | 994 | 1185 |
| tldraw | cs-o-aot-srv-check-single | 4.304 | 0.0227 | 0.53 % | 583 | 3.2 | 787 | 872 |

## Table 1c. Program check: diagnostics reported (driver report of the validation run)

Semantic diagnostics of every program file plus the global diagnostics. The C# configurations check with TypeScript 6.0.3's checker (Track O, Gate C), typescript-go and tsrs with TypeScript 7's, which differs in places: the counts show that checking happened; the C# diagnostics were compared once with tsc 6.0.3's (harness/bench/check-equivalence.mjs).

| Configuration | vscode | sentry | bluesky | playwright | tldraw |
| --- | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | 101933 | 237531 | 33337 | 30838 | 52608 |
| tsgo7-check-single | 101933 | 237531 | 33337 | 30838 | 52604 |
| tsrs-check | 101941 | 237533 | 33337 | 30838 | 52612 |
| tsrs-check-single | 101941 | 237533 | 33337 | 30838 | 52612 |
| cs-o-aot-ws-check | 101475 | 237529 | 33337 | 30791 | 51900 |
| cs-o-aot-ws-check-single | 101493 | 237529 | 33337 | 30808 | 51900 |
| cs-o-aot-srv-check | 101475 | 237529 | 33337 | 30791 | 51900 |
| cs-o-aot-srv-check-single | 101493 | 237529 | 33337 | 30808 | 51900 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |

## Table 2e. Program check: median wall-clock relative to `tsgo7-check`, with 95 % confidence interval

Endpoint of draft pre-registration §17: each repository's file list as one program, parse+bind+check, against typescript-go 7.0.2 (compiler.NewProgram, GetSemanticDiagnostics and GetGlobalDiagnostics on its default checker pool of 4 checkers). Same method as Table 2. typescript-go's checker ports TypeScript 7 and differs from 6.0.3's in places.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-check-single | 2.98 [2.97, 3.00] | 3.62 [3.61, 3.63] | 2.72 [2.70, 2.75] | 2.64 [2.62, 2.67] | 3.16 [3.14, 3.18] | 3.00 [3.00, 3.02] |
| tsrs-check | 0.16 [0.15, 0.16] | 0.19 [0.19, 0.20] | 0.24 [0.24, 0.25] | 0.21 [0.20, 0.21] | 0.19 [0.19, 0.20] | 0.20 [0.19, 0.20] |
| tsrs-check-single | 1.35 [1.34, 1.36] | 1.69 [1.69, 1.70] | 1.49 [1.47, 1.50] | 1.24 [1.24, 1.26] | 1.41 [1.40, 1.42] | 1.43 [1.42, 1.43] |
| cs-o-aot-ws-check | 1.75 [1.74, 1.76] | 1.34 [1.33, 1.34] | 1.90 [1.89, 1.93] | 2.01 [1.99, 2.03] | 1.50 [1.48, 1.52] | 1.68 [1.68, 1.69] |
| cs-o-aot-ws-check-single | 3.08 [3.07, 3.11] | 2.59 [2.58, 2.60] | 3.19 [3.17, 3.23] | 3.15 [3.14, 3.19] | 2.41 [2.39, 2.43] | 2.87 [2.86, 2.88] |
| cs-o-aot-srv-check | 1.01 [1.01, 1.02] | 0.81 [0.81, 0.82] | 1.30 [1.28, 1.33] | 1.29 [1.27, 1.32] | 0.95 [0.93, 0.96] | 1.05 [1.05, 1.06] |
| cs-o-aot-srv-check-single | 2.73 [2.71, 2.75] | 2.38 [2.37, 2.39] | 3.03 [3.01, 3.07] | 2.84 [2.82, 2.87] | 2.19 [2.18, 2.22] | 2.62 [2.61, 2.63] |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: check. Binding rows (rig check): tsgo7-check-single, cs-o-aot-ws-check-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | reported | 0.78 % | 0.27 % | 1.24 % | 0.97 % | 0.82 % | 1.24 % |
| tsgo7-check-single | rig check | 0.30 % | 0.26 % | 0.52 % | 0.41 % | 0.21 % | 0.52 % |
| tsrs-check | reported | 2.15 % | 1.79 % | 1.80 % | 2.21 % | 2.21 % | 2.21 % |
| tsrs-check-single | reported | 0.33 % | 0.14 % | 0.55 % | 0.41 % | 0.38 % | 0.55 % |
| cs-o-aot-ws-check | reported | 0.44 % | 0.43 % | 1.00 % | 0.61 % | 0.92 % | 1.00 % |
| cs-o-aot-ws-check-single | rig check | 0.35 % | 0.27 % | 0.19 % | 0.32 % | 0.12 % | 0.35 % |
| cs-o-aot-srv-check | reported | 0.68 % | 0.81 % | 2.30 % | 2.15 % | 2.22 % | 2.30 % |
| cs-o-aot-srv-check-single | reported | 0.55 % | 0.45 % | 0.34 % | 0.40 % | 0.53 % | 0.55 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-check | 0.0218 | 1.17 % | 2.6 % | 10 |
| tsgo7-check-single | 0.0222 | 1.96 % | 1.0 % | 10 |
| tsrs-check | 0.0167 | 2.39 % | 8.3 % | 6 |
| tsrs-check-single | 0.0168 | 1.71 % | 1.4 % | 6 |
| cs-o-aot-ws-check | 0.0231 | 1.31 % | 1.5 % | 10 |
| cs-o-aot-ws-check-single | 0.0235 | 2.10 % | 0.9 % | 10 |
| cs-o-aot-srv-check | 0.0286 | 3.26 % | 2.7 % | 12 |
| cs-o-aot-srv-check-single | 0.0284 | 1.01 % | 1.1 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-check | 20170955728 | 1999/610/11 | 13000.2 |
| vscode | cs-o-aot-ws-check-single | 17575038512 | 1705/521/10 | 7991.6 |
| vscode | cs-o-aot-srv-check | 20175922032 | 73/42/29 | 2330.6 |
| vscode | cs-o-aot-srv-check-single | 17575078352 | 68/37/24 | 3151.9 |
| sentry | cs-o-aot-ws-check | 5047725104 | 533/172/9 | 3080.4 |
| sentry | cs-o-aot-ws-check-single | 4769056856 | 496/162/7 | 2238.0 |
| sentry | cs-o-aot-srv-check | 5050395536 | 48/27/20 | 680.9 |
| sentry | cs-o-aot-srv-check-single | 4769099368 | 47/25/21 | 1232.3 |
| bluesky | cs-o-aot-ws-check | 1056364592 | 116/43/7 | 597.7 |
| bluesky | cs-o-aot-ws-check-single | 960116224 | 105/35/5 | 438.1 |
| bluesky | cs-o-aot-srv-check | 1064501688 | 28/17/13 | 164.4 |
| bluesky | cs-o-aot-srv-check-single | 960142968 | 25/14/11 | 271.3 |
| playwright | cs-o-aot-ws-check | 2185482272 | 224/80/8 | 1381.3 |
| playwright | cs-o-aot-ws-check-single | 1742369632 | 176/62/7 | 785.0 |
| playwright | cs-o-aot-srv-check | 2197107536 | 35/21/18 | 346.8 |
| playwright | cs-o-aot-srv-check-single | 1742406576 | 29/16/13 | 434.2 |
| tldraw | cs-o-aot-ws-check | 2179562168 | 228/79/8 | 1390.0 |
| tldraw | cs-o-aot-ws-check-single | 1774534024 | 188/65/7 | 795.1 |
| tldraw | cs-o-aot-srv-check | 2183127384 | 35/21/17 | 322.7 |
| tldraw | cs-o-aot-srv-check-single | 1774547296 | 30/17/14 | 424.3 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (40 files).
