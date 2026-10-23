# Parse-only benchmark, run 2026-10-23T08-51-58-122Z

**Status: protocol result.** All rig checks passed.

Label: track-o-additional-run-after-O23

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 4.9 % (median of 1 s windows: 9.1, 2.3, 3.8, 3, 16.9, 6.1, 10.7, 11.7, 1.7, 2.9) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 36d62621dd, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 4.9 % (range 1.7-16.9 %) | ok |
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

- `tsc6-parse`: tsc 6.0.3 (Node 24), single-threaded
- `tsgo7-parse`: typescript-go 7.0.2, parallel (default)
- `tsgo7-parse-single`: typescript-go 7.0.2, single-threaded
- `tsrs-parse`: tsrs 0.18.0 (Rust), parallel (rayon, as tsrs parses files)
- `tsrs-parse-single`: tsrs 0.18.0 (Rust), single-threaded
- `cs-aot-ws`: C# Track F, NativeAOT, Workstation GC, parallel (primary C# configuration)
- `cs-o-aot-ws`: C# Track O, NativeAOT, Workstation GC, parallel (primary Track O configuration)
- `cs-o-aot-ws-single`: C# Track O, NativeAOT, Workstation GC, single-threaded
- `cs-o-aot-srv`: C# Track O, NativeAOT, Server GC, parallel
- `cs-o-aot-srv-single`: C# Track O, NativeAOT, Server GC, single-threaded
- `cs-o-jit-ws`: C# Track O, .NET 10 JIT (TieredPGO default), Workstation GC, parallel
- `cs-o-jit-ws-single`: C# Track O, .NET 10 JIT (TieredPGO default), Workstation GC, single-threaded
- `cs-o-jit-srv`: C# Track O, .NET 10 JIT, Server GC, parallel
- `cs-o-jit-srv-single`: C# Track O, .NET 10 JIT, Server GC, single-threaded

## Table 1. Wall-clock, throughput, peak memory (parse-only)

| Repository | Configuration | Median (s) | MAD (s) | MAD/median | Files/s | MB/s | Peak working set (MiB) | Peak commit (MiB) |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| vscode | tsc6-parse | 12.03 | 0.0857 | 0.71 % | 975 | 11.7 | 3193 | 3224 |
| vscode | tsgo7-parse | 1.037 | 0.0251 | 2.42 % | 11311 | 136.2 | 2186 | 2215 |
| vscode | tsgo7-parse-single | 4.967 | 0.0101 | 0.20 % | 2361 | 28.4 | 2115 | 2179 |
| vscode | tsrs-parse | 0.3104 | 0.0133 | 4.27 % | 37768 | 454.8 | 956 | 1902 |
| vscode | tsrs-parse-single | 2.459 | 0.0086 | 0.35 % | 4767 | 57.4 | 956 | 1253 |
| vscode | cs-aot-ws | 8.591 | 0.3026 | 3.52 % | 1365 | 16.4 | 2638 | 2645 |
| vscode | cs-o-aot-ws | 0.3970 | 0.0049 | 1.22 % | 29530 | 355.6 | 881 | 1013 |
| vscode | cs-o-aot-ws-single | 3.904 | 0.0098 | 0.25 % | 3003 | 36.2 | 853 | 870 |
| vscode | cs-o-aot-srv | 0.3801 | 0.0103 | 2.71 % | 30846 | 371.4 | 887 | 1068 |
| vscode | cs-o-aot-srv-single | 3.903 | 0.0093 | 0.24 % | 3004 | 36.2 | 859 | 885 |
| vscode | cs-o-jit-ws | 1.575 | 0.0710 | 4.51 % | 7446 | 89.7 | 919 | 1042 |
| vscode | cs-o-jit-ws-single | 5.760 | 0.1578 | 2.74 % | 2035 | 24.5 | 896 | 893 |
| vscode | cs-o-jit-srv | 1.554 | 0.0888 | 5.72 % | 7543 | 90.8 | 923 | 1052 |
| vscode | cs-o-jit-srv-single | 5.675 | 0.1516 | 2.67 % | 2066 | 24.9 | 898 | 906 |
| sentry | tsc6-parse | 4.815 | 0.0976 | 2.03 % | 1855 | 9.5 | 1216 | 1208 |
| sentry | tsgo7-parse | 0.4071 | 0.0091 | 2.25 % | 21942 | 112.9 | 789 | 811 |
| sentry | tsgo7-parse-single | 2.289 | 0.0087 | 0.38 % | 3902 | 20.1 | 765 | 776 |
| sentry | tsrs-parse | 0.1615 | 0.0012 | 0.71 % | 55300 | 284.4 | 342 | 660 |
| sentry | tsrs-parse-single | 1.261 | 0.0104 | 0.82 % | 7084 | 36.4 | 346 | 634 |
| sentry | cs-aot-ws | 2.895 | 0.0470 | 1.63 % | 3086 | 15.9 | 914 | 914 |
| sentry | cs-o-aot-ws | 0.2024 | 0.0030 | 1.51 % | 44141 | 227.0 | 321 | 430 |
| sentry | cs-o-aot-ws-single | 1.711 | 0.0079 | 0.46 % | 5221 | 26.9 | 303 | 305 |
| sentry | cs-o-aot-srv | 0.1875 | 0.0052 | 2.80 % | 47634 | 245.0 | 319 | 440 |
| sentry | cs-o-aot-srv-single | 1.716 | 0.0083 | 0.49 % | 5205 | 26.8 | 305 | 314 |
| sentry | cs-o-jit-ws | 0.6209 | 0.0074 | 1.19 % | 14385 | 74.0 | 357 | 451 |
| sentry | cs-o-jit-ws-single | 3.689 | 0.0345 | 0.94 % | 2421 | 12.5 | 342 | 325 |
| sentry | cs-o-jit-srv | 0.6039 | 0.0106 | 1.76 % | 14791 | 76.1 | 356 | 462 |
| sentry | cs-o-jit-srv-single | 3.492 | 0.0407 | 1.17 % | 2558 | 13.2 | 344 | 334 |
| bluesky | tsc6-parse | 1.237 | 0.0099 | 0.80 % | 1364 | 5.8 | 386 | 357 |
| bluesky | tsgo7-parse | 0.1074 | 0.0024 | 2.24 % | 15719 | 67.0 | 159 | 171 |
| bluesky | tsgo7-parse-single | 0.4309 | 0.0026 | 0.60 % | 3917 | 16.7 | 143 | 151 |
| bluesky | tsrs-parse | 0.0489 | 0.0007 | 1.50 % | 34507 | 147.1 | 70 | 212 |
| bluesky | tsrs-parse-single | 0.2451 | 0.0016 | 0.67 % | 6886 | 29.3 | 61 | 109 |
| bluesky | cs-aot-ws | 0.4764 | 0.0117 | 2.45 % | 3543 | 15.1 | 169 | 165 |
| bluesky | cs-o-aot-ws | 0.0551 | 0.0007 | 1.26 % | 30620 | 130.5 | 69 | 89 |
| bluesky | cs-o-aot-ws-single | 0.3197 | 0.0019 | 0.59 % | 5280 | 22.5 | 61 | 58 |
| bluesky | cs-o-aot-srv | 0.0698 | 0.0017 | 2.44 % | 24198 | 103.1 | 74 | 103 |
| bluesky | cs-o-aot-srv-single | 0.3284 | 0.0043 | 1.30 % | 5140 | 21.9 | 64 | 70 |
| bluesky | cs-o-jit-ws | 0.2754 | 0.0014 | 0.52 % | 6130 | 26.1 | 105 | 108 |
| bluesky | cs-o-jit-ws-single | 1.132 | 0.0046 | 0.41 % | 1492 | 6.4 | 97 | 77 |
| bluesky | cs-o-jit-srv | 0.2910 | 0.0039 | 1.33 % | 5800 | 24.7 | 111 | 122 |
| bluesky | cs-o-jit-srv-single | 1.140 | 0.0048 | 0.42 % | 1480 | 6.3 | 99 | 89 |
| playwright | tsc6-parse | 1.803 | 0.0144 | 0.80 % | 785 | 9.6 | 557 | 534 |
| playwright | tsgo7-parse | 0.1467 | 0.0030 | 2.06 % | 9645 | 117.5 | 269 | 284 |
| playwright | tsgo7-parse-single | 0.5958 | 0.0029 | 0.49 % | 2375 | 28.9 | 255 | 264 |
| playwright | tsrs-parse | 0.0592 | 0.0014 | 2.35 % | 23895 | 291.0 | 124 | 370 |
| playwright | tsrs-parse-single | 0.2997 | 0.0023 | 0.78 % | 4721 | 57.5 | 115 | 202 |
| playwright | cs-aot-ws | 0.9375 | 0.0092 | 0.99 % | 1509 | 18.4 | 322 | 318 |
| playwright | cs-o-aot-ws | 0.0705 | 0.0012 | 1.73 % | 20072 | 244.4 | 127 | 187 |
| playwright | cs-o-aot-ws-single | 0.4639 | 0.0027 | 0.57 % | 3050 | 37.1 | 112 | 116 |
| playwright | cs-o-aot-srv | 0.0835 | 0.0019 | 2.30 % | 16936 | 206.3 | 133 | 198 |
| playwright | cs-o-aot-srv-single | 0.4723 | 0.0027 | 0.57 % | 2996 | 36.5 | 115 | 128 |
| playwright | cs-o-jit-ws | 0.3535 | 0.0051 | 1.44 % | 4002 | 48.7 | 163 | 213 |
| playwright | cs-o-jit-ws-single | 1.826 | 0.0645 | 3.53 % | 775 | 9.4 | 148 | 135 |
| playwright | cs-o-jit-srv | 0.3783 | 0.0068 | 1.79 % | 3740 | 45.6 | 169 | 227 |
| playwright | cs-o-jit-srv-single | 1.820 | 0.0844 | 4.64 % | 777 | 9.5 | 151 | 147 |
| tldraw | tsc6-parse | 1.877 | 0.0157 | 0.84 % | 1336 | 7.3 | 549 | 526 |
| tldraw | tsgo7-parse | 0.1571 | 0.0059 | 3.77 % | 15961 | 86.7 | 276 | 290 |
| tldraw | tsgo7-parse-single | 0.7175 | 0.0056 | 0.78 % | 3496 | 19.0 | 257 | 268 |
| tldraw | tsrs-parse | 0.0684 | 0.0016 | 2.38 % | 36686 | 199.3 | 121 | 343 |
| tldraw | tsrs-parse-single | 0.3886 | 0.0022 | 0.58 % | 6454 | 35.1 | 112 | 196 |
| tldraw | cs-aot-ws | 0.9347 | 0.0107 | 1.15 % | 2683 | 14.6 | 310 | 308 |
| tldraw | cs-o-aot-ws | 0.0771 | 0.0011 | 1.49 % | 32528 | 176.7 | 116 | 161 |
| tldraw | cs-o-aot-ws-single | 0.5447 | 0.0024 | 0.44 % | 4604 | 25.0 | 105 | 106 |
| tldraw | cs-o-aot-srv | 0.0898 | 0.0021 | 2.35 % | 27928 | 151.7 | 121 | 175 |
| tldraw | cs-o-aot-srv-single | 0.5520 | 0.0028 | 0.50 % | 4544 | 24.7 | 108 | 118 |
| tldraw | cs-o-jit-ws | 0.3451 | 0.0024 | 0.70 % | 7266 | 39.5 | 154 | 187 |
| tldraw | cs-o-jit-ws-single | 1.823 | 0.0466 | 2.56 % | 1375 | 7.5 | 143 | 126 |
| tldraw | cs-o-jit-srv | 0.3612 | 0.0035 | 0.97 % | 6943 | 37.7 | 156 | 197 |
| tldraw | cs-o-jit-srv-single | 1.849 | 0.0389 | 2.10 % | 1356 | 7.4 | 145 | 137 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 11.60 [11.42, 11.87] | 11.83 [11.56, 12.21] | 11.52 [11.30, 11.75] | 12.29 [12.09, 12.50] | 11.95 [11.50, 12.29] | 11.83 [11.71, 11.96] |
| tsgo7-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-parse-single | 4.79 [4.72, 4.90] | 5.62 [5.52, 5.74] | 4.01 [3.93, 4.09] | 4.06 [4.00, 4.12] | 4.57 [4.40, 4.70] | 4.58 [4.53, 4.62] |
| tsrs-parse | 0.30 [0.29, 0.32] | 0.40 [0.39, 0.40] | 0.46 [0.44, 0.47] | 0.40 [0.39, 0.41] | 0.44 [0.41, 0.45] | 0.39 [0.39, 0.40] |
| tsrs-parse-single | 2.37 [2.34, 2.43] | 3.10 [3.04, 3.16] | 2.28 [2.24, 2.33] | 2.04 [2.00, 2.07] | 2.47 [2.38, 2.54] | 2.43 [2.40, 2.45] |
| cs-aot-ws | 8.29 [7.91, 8.58] | 7.11 [6.91, 7.27] | 4.44 [4.32, 4.56] | 6.39 [6.25, 6.48] | 5.95 [5.72, 6.11] | 6.30 [6.21, 6.37] |
| cs-o-aot-ws | 0.38 [0.38, 0.39] | 0.50 [0.49, 0.51] | 0.51 [0.50, 0.53] | 0.48 [0.47, 0.49] | 0.49 [0.47, 0.50] | 0.47 [0.46, 0.48] |
| cs-o-aot-ws-single | 3.77 [3.71, 3.86] | 4.20 [4.13, 4.29] | 2.98 [2.92, 3.03] | 3.16 [3.11, 3.21] | 3.47 [3.34, 3.56] | 3.49 [3.45, 3.52] |
| cs-o-aot-srv | 0.37 [0.36, 0.38] | 0.46 [0.45, 0.47] | 0.65 [0.63, 0.67] | 0.57 [0.56, 0.58] | 0.57 [0.55, 0.61] | 0.51 [0.51, 0.52] |
| cs-o-aot-srv-single | 3.77 [3.71, 3.85] | 4.22 [4.14, 4.30] | 3.06 [2.99, 3.12] | 3.22 [3.17, 3.27] | 3.51 [3.38, 3.61] | 3.53 [3.50, 3.56] |
| cs-o-jit-ws | 1.52 [1.44, 1.59] | 1.53 [1.49, 1.56] | 2.56 [2.52, 2.61] | 2.41 [2.36, 2.45] | 2.20 [2.11, 2.26] | 1.99 [1.96, 2.02] |
| cs-o-jit-ws-single | 5.56 [5.41, 5.80] | 9.06 [8.89, 9.26] | 10.54 [10.34, 10.73] | 12.44 [11.63, 12.69] | 11.60 [11.09, 12.07] | 9.48 [9.33, 9.61] |
| cs-o-jit-srv | 1.50 [1.45, 1.61] | 1.48 [1.45, 1.52] | 2.71 [2.64, 2.77] | 2.58 [2.51, 2.62] | 2.30 [2.22, 2.38] | 2.04 [2.02, 2.08] |
| cs-o-jit-srv-single | 5.48 [5.37, 5.74] | 8.58 [8.42, 8.79] | 10.62 [10.41, 10.80] | 12.41 [11.71, 12.87] | 11.77 [11.22, 12.10] | 9.39 [9.22, 9.52] |

## Table 2b. Median wall-clock relative to `tsrs-parse` (secondary reference), with 95 % confidence interval

Same method as Table 2, against tsrs (Rust port of TypeScript 7 / typescript-go), parallel. Secondary: never replaces the primary endpoint against typescript-go. tsrs parses JSDoc in .ts files lazily (TypeScript 7.1), typescript-go 7.0.2 eagerly, the C# tracks as tsc's host does (ParseForTypeErrors); see docs/references.md.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 38.75 [36.23, 39.63] | 29.81 [29.35, 30.59] | 25.29 [24.86, 25.72] | 30.45 [29.71, 31.00] | 27.46 [26.99, 28.29] | 30.03 [29.61, 30.35] |
| tsgo7-parse | 3.34 [3.11, 3.43] | 2.52 [2.47, 2.57] | 2.20 [2.14, 2.25] | 2.48 [2.42, 2.53] | 2.30 [2.23, 2.42] | 2.54 [2.50, 2.57] |
| tsgo7-parse-single | 16.00 [14.97, 16.36] | 14.17 [14.11, 14.24] | 8.81 [8.65, 8.94] | 10.06 [9.81, 10.22] | 10.49 [10.34, 10.81] | 11.61 [11.45, 11.71] |
| tsrs-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-parse-single | 7.92 [7.41, 8.11] | 7.81 [7.76, 7.87] | 5.01 [4.92, 5.09] | 5.06 [4.93, 5.14] | 5.68 [5.62, 5.86] | 6.17 [6.08, 6.22] |
| cs-aot-ws | 27.68 [25.64, 28.53] | 17.92 [17.53, 18.11] | 9.74 [9.50, 10.01] | 15.83 [15.41, 16.08] | 13.67 [13.44, 14.09] | 15.99 [15.72, 16.17] |
| cs-o-aot-ws | 1.28 [1.20, 1.31] | 1.25 [1.23, 1.26] | 1.13 [1.10, 1.15] | 1.19 [1.16, 1.22] | 1.13 [1.11, 1.16] | 1.19 [1.18, 1.21] |
| cs-o-aot-ws-single | 12.58 [11.77, 12.87] | 10.59 [10.54, 10.65] | 6.54 [6.42, 6.63] | 7.83 [7.64, 7.96] | 7.97 [7.87, 8.20] | 8.85 [8.73, 8.93] |
| cs-o-aot-srv | 1.22 [1.15, 1.27] | 1.16 [1.13, 1.19] | 1.43 [1.39, 1.47] | 1.41 [1.38, 1.45] | 1.31 [1.29, 1.41] | 1.30 [1.29, 1.33] |
| cs-o-aot-srv-single | 12.57 [11.77, 12.87] | 10.62 [10.56, 10.68] | 6.71 [6.57, 6.85] | 7.98 [7.78, 8.10] | 8.07 [7.98, 8.32] | 8.96 [8.84, 9.05] |
| cs-o-jit-ws | 5.07 [4.66, 5.28] | 3.84 [3.80, 3.88] | 5.63 [5.54, 5.71] | 5.97 [5.82, 6.08] | 5.05 [4.98, 5.20] | 5.06 [4.97, 5.12] |
| cs-o-jit-ws-single | 18.56 [17.28, 19.23] | 22.84 [22.69, 23.02] | 23.14 [22.75, 23.43] | 30.83 [28.82, 31.49] | 26.67 [26.05, 27.89] | 24.06 [23.63, 24.38] |
| cs-o-jit-srv | 5.01 [4.64, 5.37] | 3.74 [3.69, 3.79] | 5.95 [5.81, 6.06] | 6.39 [6.20, 6.51] | 5.28 [5.21, 5.49] | 5.19 [5.11, 5.28] |
| cs-o-jit-srv-single | 18.28 [17.10, 19.14] | 21.62 [21.50, 21.89] | 23.31 [22.90, 23.63] | 30.74 [29.05, 31.96] | 27.04 [26.14, 27.91] | 23.81 [23.36, 24.15] |

## Table 3. Variance gate (MAD/median < 3 %)

Binding rows (rig check): tsc6-parse, tsgo7-parse-single and the primary single-threaded C# configuration of each track. Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md, B11 as amended by prereg/v4).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | rig check | 0.71 % | 2.03 % | 0.80 % | 0.80 % | 0.84 % | 2.03 % |
| tsgo7-parse | reported | 2.42 % | 2.25 % | 2.24 % | 2.06 % | 3.77 % | 3.77 % |
| tsgo7-parse-single | rig check | 0.20 % | 0.38 % | 0.60 % | 0.49 % | 0.78 % | 0.78 % |
| tsrs-parse | reported | 4.27 % | 0.71 % | 1.50 % | 2.35 % | 2.38 % | 4.27 % |
| tsrs-parse-single | reported | 0.35 % | 0.82 % | 0.67 % | 0.78 % | 0.58 % | 0.82 % |
| cs-aot-ws | reported | 3.52 % | 1.63 % | 2.45 % | 0.99 % | 1.15 % | 3.52 % |
| cs-o-aot-ws | reported | 1.22 % | 1.51 % | 1.26 % | 1.73 % | 1.49 % | 1.73 % |
| cs-o-aot-ws-single | rig check | 0.25 % | 0.46 % | 0.59 % | 0.57 % | 0.44 % | 0.59 % |
| cs-o-aot-srv | reported | 2.71 % | 2.80 % | 2.44 % | 2.30 % | 2.35 % | 2.80 % |
| cs-o-aot-srv-single | reported | 0.24 % | 0.49 % | 1.30 % | 0.57 % | 0.50 % | 1.30 % |
| cs-o-jit-ws | reported | 4.51 % | 1.19 % | 0.52 % | 1.44 % | 0.70 % | 4.51 % |
| cs-o-jit-ws-single | reported | 2.74 % | 0.94 % | 0.41 % | 3.53 % | 2.56 % | 3.53 % |
| cs-o-jit-srv | reported | 5.72 % | 1.76 % | 1.33 % | 1.79 % | 0.97 % | 5.72 % |
| cs-o-jit-srv-single | reported | 2.67 % | 1.17 % | 0.42 % | 4.64 % | 2.10 % | 4.64 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsc6-parse | 0.2657 | 0.63 % | 21.5 % | 97 |
| tsgo7-parse | 0.0202 | 1.51 % | 18.8 % | 9 |
| tsgo7-parse-single | 0.0205 | 0.98 % | 4.8 % | 9 |
| tsrs-parse | 0.0186 | 1.44 % | 37.9 % | 8 |
| tsrs-parse-single | 0.0174 | 1.15 % | 7.1 % | 7 |
| cs-aot-ws | 0.0242 | 1.75 % | 5.1 % | 10 |
| cs-o-aot-ws | 0.0227 | 1.15 % | 41.2 % | 10 |
| cs-o-aot-ws-single | 0.0230 | 1.21 % | 7.2 % | 10 |
| cs-o-aot-srv | 0.0269 | 0.96 % | 38.6 % | 12 |
| cs-o-aot-srv-single | 0.0265 | 0.88 % | 8.1 % | 12 |
| cs-o-jit-ws | 0.0779 | 1.08 % | 28.3 % | 26 |
| cs-o-jit-ws-single | 0.0764 | 0.92 % | 6.8 % | 26 |
| cs-o-jit-srv | 0.0826 | 1.83 % | 28.4 % | 28 |
| cs-o-jit-srv-single | 0.0818 | 0.68 % | 7.2 % | 28 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-aot-ws | 3278194720 | 390/350/6 | 6435.6 |
| vscode | cs-o-aot-ws | 61189120 | 7/5/3 | 32.8 |
| vscode | cs-o-aot-ws-single | 36057536 | 5/3/1 | 21.4 |
| vscode | cs-o-aot-srv | 60099936 | 5/4/3 | 6.3 |
| vscode | cs-o-aot-srv-single | 36050400 | 3/1/0 | 8.1 |
| vscode | cs-o-jit-ws | 62220208 | 7/5/3 | 40.3 |
| vscode | cs-o-jit-ws-single | 36961728 | 5/3/1 | 20.0 |
| vscode | cs-o-jit-srv | 62077552 | 7/6/5 | 11.5 |
| vscode | cs-o-jit-srv-single | 36957096 | 3/1/0 | 8.1 |
| sentry | cs-aot-ws | 1094842816 | 139/137/6 | 2317.8 |
| sentry | cs-o-aot-ws | 45720168 | 5/5/3 | 23.2 |
| sentry | cs-o-aot-ws-single | 20361488 | 2/1/0 | 9.3 |
| sentry | cs-o-aot-srv | 48968640 | 5/4/4 | 6.7 |
| sentry | cs-o-aot-srv-single | 20361152 | 3/1/0 | 7.3 |
| sentry | cs-o-jit-ws | 45167616 | 5/5/3 | 25.1 |
| sentry | cs-o-jit-ws-single | 21220504 | 2/1/0 | 8.8 |
| sentry | cs-o-jit-srv | 45665304 | 7/6/5 | 10.8 |
| sentry | cs-o-jit-srv-single | 21223168 | 3/1/0 | 7.6 |
| bluesky | cs-aot-ws | 185071176 | 25/24/3 | 344.9 |
| bluesky | cs-o-aot-ws | 23184648 | 2/2/2 | 2.0 |
| bluesky | cs-o-aot-ws-single | 4669800 | 0/0/0 | 0.0 |
| bluesky | cs-o-aot-srv | 27846760 | 6/5/4 | 12.7 |
| bluesky | cs-o-aot-srv-single | 4670496 | 1/0/0 | 2.0 |
| bluesky | cs-o-jit-ws | 24017952 | 2/2/2 | 3.7 |
| bluesky | cs-o-jit-ws-single | 5387536 | 0/0/0 | 0.0 |
| bluesky | cs-o-jit-srv | 24263952 | 6/5/4 | 11.8 |
| bluesky | cs-o-jit-srv-single | 5389096 | 1/0/0 | 1.9 |
| playwright | cs-aot-ws | 379018432 | 47/46/4 | 769.6 |
| playwright | cs-o-aot-ws | 26264432 | 2/2/2 | 1.9 |
| playwright | cs-o-aot-ws-single | 4424728 | 0/0/0 | 0.0 |
| playwright | cs-o-aot-srv | 30664968 | 7/6/5 | 9.6 |
| playwright | cs-o-aot-srv-single | 4425424 | 1/0/0 | 1.8 |
| playwright | cs-o-jit-ws | 27123192 | 2/2/2 | 3.5 |
| playwright | cs-o-jit-ws-single | 5138072 | 0/0/0 | 0.0 |
| playwright | cs-o-jit-srv | 26681040 | 6/5/4 | 12.3 |
| playwright | cs-o-jit-srv-single | 5139632 | 1/0/0 | 2.2 |
| tldraw | cs-aot-ws | 356436240 | 47/44/4 | 747.2 |
| tldraw | cs-o-aot-ws | 29809848 | 2/2/2 | 1.9 |
| tldraw | cs-o-aot-ws-single | 7358408 | 0/0/0 | 0.0 |
| tldraw | cs-o-aot-srv | 33881056 | 7/6/5 | 10.6 |
| tldraw | cs-o-aot-srv-single | 7359440 | 2/1/0 | 4.6 |
| tldraw | cs-o-jit-ws | 30569024 | 2/2/2 | 2.8 |
| tldraw | cs-o-jit-ws-single | 8089264 | 0/0/0 | 0.0 |
| tldraw | cs-o-jit-srv | 30236616 | 6/5/4 | 13.7 |
| tldraw | cs-o-jit-srv-single | 8116008 | 2/1/0 | 4.8 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (70 files).
