# Parse-only benchmark, run 2026-11-06T18-16-31-540Z

**Status: protocol result.** All rig checks passed.

Label: check additional run 4 (after MM1, prereg/v12)

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 6.1 % (median of 1 s windows: 7, 12.6, 6.8, 3.5, 3, 3.6, 1.6, 6, 12.6, 6.2) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 6920a1c43f, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 6.1 % (range 1.6-12.6 %) | ok |
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
| vscode | tsgo7-check | 14.32 | 0.1000 | 0.70 % | 819 | 9.9 | 6997 | 7020 |
| vscode | tsgo7-check-single | 42.89 | 0.1281 | 0.30 % | 273 | 3.3 | 5813 | 5834 |
| vscode | tsrs-check | 2.220 | 0.0304 | 1.37 % | 5282 | 63.6 | 2770 | 4509 |
| vscode | tsrs-check-single | 19.34 | 0.0219 | 0.11 % | 606 | 7.3 | 2166 | 2628 |
| vscode | cs-o-aot-ws-check | 10.89 | 0.0403 | 0.37 % | 1077 | 13.0 | 4530 | 5067 |
| vscode | cs-o-aot-ws-check-single | 33.17 | 0.0622 | 0.19 % | 353 | 4.3 | 3744 | 4000 |
| vscode | cs-o-aot-srv-check | 10.06 | 0.0436 | 0.43 % | 1165 | 14.0 | 4786 | 5344 |
| vscode | cs-o-aot-srv-check-single | 33.33 | 0.0367 | 0.11 % | 352 | 4.2 | 3881 | 4118 |
| sentry | tsgo7-check | 4.838 | 0.0196 | 0.40 % | 1846 | 9.5 | 2114 | 2139 |
| sentry | tsgo7-check-single | 17.57 | 0.0267 | 0.15 % | 508 | 2.6 | 1973 | 1997 |
| sentry | tsrs-check | 0.9470 | 0.0136 | 1.44 % | 9432 | 48.5 | 995 | 2197 |
| sentry | tsrs-check-single | 8.218 | 0.0198 | 0.24 % | 1087 | 5.6 | 888 | 1333 |
| sentry | cs-o-aot-ws-check | 2.867 | 0.0221 | 0.77 % | 3116 | 16.0 | 1319 | 1583 |
| sentry | cs-o-aot-ws-check-single | 9.296 | 0.0237 | 0.26 % | 961 | 4.9 | 1143 | 1186 |
| sentry | cs-o-aot-srv-check | 2.546 | 0.0218 | 0.85 % | 3508 | 18.0 | 1500 | 1793 |
| sentry | cs-o-aot-srv-check-single | 9.392 | 0.0142 | 0.15 % | 951 | 4.9 | 1223 | 1284 |
| bluesky | tsgo7-check | 0.8183 | 0.0056 | 0.68 % | 2063 | 8.8 | 428 | 432 |
| bluesky | tsgo7-check-single | 2.275 | 0.0160 | 0.70 % | 742 | 3.2 | 407 | 411 |
| bluesky | tsrs-check | 0.2064 | 0.0030 | 1.43 % | 8180 | 34.9 | 259 | 832 |
| bluesky | tsrs-check-single | 1.233 | 0.0069 | 0.56 % | 1369 | 5.8 | 201 | 239 |
| bluesky | cs-o-aot-ws-check | 0.7425 | 0.0076 | 1.02 % | 2273 | 9.7 | 320 | 393 |
| bluesky | cs-o-aot-ws-check-single | 1.922 | 0.0089 | 0.46 % | 878 | 3.7 | 276 | 307 |
| bluesky | cs-o-aot-srv-check | 0.7208 | 0.0080 | 1.10 % | 2342 | 10.0 | 358 | 447 |
| bluesky | cs-o-aot-srv-check-single | 1.947 | 0.0074 | 0.38 % | 867 | 3.7 | 299 | 335 |
| playwright | tsgo7-check | 1.494 | 0.0129 | 0.87 % | 947 | 11.5 | 890 | 896 |
| playwright | tsgo7-check-single | 3.989 | 0.0143 | 0.36 % | 355 | 4.3 | 747 | 751 |
| playwright | tsrs-check | 0.3191 | 0.0077 | 2.41 % | 4434 | 54.0 | 461 | 1250 |
| playwright | tsrs-check-single | 1.877 | 0.0094 | 0.50 % | 754 | 9.2 | 350 | 406 |
| playwright | cs-o-aot-ws-check | 1.289 | 0.0104 | 0.81 % | 1098 | 13.4 | 651 | 791 |
| playwright | cs-o-aot-ws-check-single | 3.226 | 0.0135 | 0.42 % | 439 | 5.3 | 494 | 537 |
| playwright | cs-o-aot-srv-check | 1.257 | 0.0151 | 1.20 % | 1126 | 13.7 | 703 | 868 |
| playwright | cs-o-aot-srv-check-single | 3.258 | 0.0130 | 0.40 % | 434 | 5.3 | 506 | 559 |
| tldraw | tsgo7-check | 1.965 | 0.0088 | 0.45 % | 1276 | 6.9 | 836 | 843 |
| tldraw | tsgo7-check-single | 6.238 | 0.0154 | 0.25 % | 402 | 2.2 | 746 | 752 |
| tldraw | tsrs-check | 0.3782 | 0.0056 | 1.49 % | 6631 | 36.0 | 447 | 1186 |
| tldraw | tsrs-check-single | 2.786 | 0.0108 | 0.39 % | 900 | 4.9 | 331 | 405 |
| tldraw | cs-o-aot-ws-check | 1.377 | 0.0075 | 0.55 % | 1822 | 9.9 | 604 | 741 |
| tldraw | cs-o-aot-ws-check-single | 3.634 | 0.0101 | 0.28 % | 690 | 3.7 | 449 | 477 |
| tldraw | cs-o-aot-srv-check | 1.296 | 0.0155 | 1.20 % | 1936 | 10.5 | 680 | 821 |
| tldraw | cs-o-aot-srv-check-single | 3.666 | 0.0075 | 0.20 % | 684 | 3.7 | 479 | 515 |

## Table 1c. Program check: diagnostics reported (driver report of the validation run)

Semantic diagnostics of every program file plus the global diagnostics. The C# configurations check with TypeScript 6.0.3's checker (Track O, Gate C), typescript-go and tsrs with TypeScript 7's, which differs in places: the counts show that checking happened; the C# diagnostics were compared once with tsc 6.0.3's (harness/bench/check-equivalence.mjs).

| Configuration | vscode | sentry | bluesky | playwright | tldraw |
| --- | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | 101933 | 237531 | 33337 | 30838 | 52608 |
| tsgo7-check-single | 101933 | 237531 | 33337 | 30838 | 52604 |
| tsrs-check | 101941 | 237533 | 33337 | 30838 | 52612 |
| tsrs-check-single | 101941 | 237533 | 33337 | 30838 | 52612 |
| cs-o-aot-ws-check | 101474 | 237529 | 33337 | 30789 | 51900 |
| cs-o-aot-ws-check-single | 101493 | 237529 | 33337 | 30808 | 51900 |
| cs-o-aot-srv-check | 101474 | 237529 | 33337 | 30789 | 51900 |
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
| tsgo7-check-single | 3.00 [2.98, 3.02] | 3.63 [3.62, 3.64] | 2.78 [2.75, 2.80] | 2.67 [2.65, 2.69] | 3.17 [3.16, 3.19] | 3.03 [3.02, 3.04] |
| tsrs-check | 0.16 [0.15, 0.16] | 0.20 [0.19, 0.20] | 0.25 [0.25, 0.26] | 0.21 [0.21, 0.22] | 0.19 [0.19, 0.20] | 0.20 [0.20, 0.20] |
| tsrs-check-single | 1.35 [1.34, 1.36] | 1.70 [1.69, 1.70] | 1.51 [1.49, 1.52] | 1.26 [1.24, 1.27] | 1.42 [1.41, 1.42] | 1.44 [1.43, 1.44] |
| cs-o-aot-ws-check | 0.76 [0.76, 0.77] | 0.59 [0.59, 0.60] | 0.91 [0.90, 0.92] | 0.86 [0.85, 0.87] | 0.70 [0.70, 0.70] | 0.76 [0.75, 0.76] |
| cs-o-aot-ws-check-single | 2.32 [2.31, 2.33] | 1.92 [1.91, 1.93] | 2.35 [2.33, 2.36] | 2.16 [2.14, 2.17] | 1.85 [1.84, 1.86] | 2.11 [2.10, 2.11] |
| cs-o-aot-srv-check | 0.70 [0.70, 0.71] | 0.53 [0.52, 0.53] | 0.88 [0.87, 0.90] | 0.84 [0.83, 0.85] | 0.66 [0.65, 0.67] | 0.71 [0.71, 0.71] |
| cs-o-aot-srv-check-single | 2.33 [2.32, 2.34] | 1.94 [1.93, 1.95] | 2.38 [2.36, 2.40] | 2.18 [2.16, 2.20] | 1.87 [1.86, 1.87] | 2.13 [2.12, 2.13] |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: check. Binding rows (rig check): tsgo7-check-single, cs-o-aot-ws-check-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | reported | 0.70 % | 0.40 % | 0.68 % | 0.87 % | 0.45 % | 0.87 % |
| tsgo7-check-single | rig check | 0.30 % | 0.15 % | 0.70 % | 0.36 % | 0.25 % | 0.70 % |
| tsrs-check | reported | 1.37 % | 1.44 % | 1.43 % | 2.41 % | 1.49 % | 2.41 % |
| tsrs-check-single | reported | 0.11 % | 0.24 % | 0.56 % | 0.50 % | 0.39 % | 0.56 % |
| cs-o-aot-ws-check | reported | 0.37 % | 0.77 % | 1.02 % | 0.81 % | 0.55 % | 1.02 % |
| cs-o-aot-ws-check-single | rig check | 0.19 % | 0.26 % | 0.46 % | 0.42 % | 0.28 % | 0.46 % |
| cs-o-aot-srv-check | reported | 0.43 % | 0.85 % | 1.10 % | 1.20 % | 1.20 % | 1.20 % |
| cs-o-aot-srv-check-single | reported | 0.11 % | 0.15 % | 0.38 % | 0.40 % | 0.20 % | 0.40 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-check | 0.0217 | 1.50 % | 2.6 % | 10 |
| tsgo7-check-single | 0.0223 | 2.29 % | 1.0 % | 10 |
| tsrs-check | 0.0169 | 0.81 % | 8.2 % | 6 |
| tsrs-check-single | 0.0177 | 2.46 % | 1.4 % | 6 |
| cs-o-aot-ws-check | 0.0242 | 1.31 % | 3.3 % | 10 |
| cs-o-aot-ws-check-single | 0.0241 | 0.84 % | 1.3 % | 10 |
| cs-o-aot-srv-check | 0.0275 | 0.78 % | 3.8 % | 12 |
| cs-o-aot-srv-check-single | 0.0286 | 1.06 % | 1.5 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-check | 7106336384 | 135/57/8 | 1064.7 |
| vscode | cs-o-aot-ws-check-single | 5855866648 | 115/45/7 | 737.9 |
| vscode | cs-o-aot-srv-check | 7115289024 | 53/31/25 | 303.2 |
| vscode | cs-o-aot-srv-check-single | 5855953192 | 46/28/21 | 556.5 |
| sentry | cs-o-aot-ws-check | 1858594024 | 43/27/6 | 500.6 |
| sentry | cs-o-aot-ws-check-single | 1631723584 | 37/18/5 | 360.3 |
| sentry | cs-o-aot-srv-check | 1859482296 | 29/19/16 | 183.4 |
| sentry | cs-o-aot-srv-check-single | 1635381472 | 28/17/14 | 321.5 |
| bluesky | cs-o-aot-ws-check | 393584168 | 12/9/5 | 81.7 |
| bluesky | cs-o-aot-ws-check-single | 322938720 | 8/5/3 | 59.9 |
| bluesky | cs-o-aot-srv-check | 401469608 | 18/13/11 | 47.8 |
| bluesky | cs-o-aot-srv-check-single | 322832424 | 15/11/9 | 51.1 |
| playwright | cs-o-aot-ws-check | 900568120 | 21/13/6 | 122.1 |
| playwright | cs-o-aot-ws-check-single | 679843144 | 16/8/4 | 75.4 |
| playwright | cs-o-aot-srv-check | 914164912 | 28/20/17 | 60.2 |
| playwright | cs-o-aot-srv-check-single | 679928664 | 22/14/12 | 59.0 |
| tldraw | cs-o-aot-ws-check | 960104816 | 23/14/5 | 178.5 |
| tldraw | cs-o-aot-ws-check-single | 721885416 | 18/9/4 | 126.8 |
| tldraw | cs-o-aot-srv-check | 968088336 | 26/17/14 | 73.2 |
| tldraw | cs-o-aot-srv-check-single | 722027832 | 22/15/12 | 84.1 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (40 files).
