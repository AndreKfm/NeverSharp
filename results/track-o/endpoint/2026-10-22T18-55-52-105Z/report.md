# Parse-only benchmark, run 2026-10-22T18-55-52-105Z

**Status: protocol result.** All rig checks passed.

Label: track-o-endpoint-run1

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 5.8 % (median of 1 s windows: 4.4, 3.1, 5.7, 9.1, 5.8, 0.7, 3.1, 7.7, 7.4, 6.6) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 51f5036fa1, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 5.8 % (range 0.7-9.1 %) | ok |
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
| vscode | tsc6-parse | 12.15 | 0.0720 | 0.59 % | 965 | 11.6 | 3183 | 3223 |
| vscode | tsgo7-parse | 1.031 | 0.0320 | 3.10 % | 11370 | 136.9 | 2175 | 2216 |
| vscode | tsgo7-parse-single | 5.025 | 0.0328 | 0.65 % | 2333 | 28.1 | 2115 | 2175 |
| vscode | tsrs-parse | 0.3219 | 0.0145 | 4.51 % | 36418 | 438.5 | 955 | 1927 |
| vscode | tsrs-parse-single | 2.486 | 0.0118 | 0.47 % | 4717 | 56.8 | 940 | 1253 |
| vscode | cs-aot-ws | 8.654 | 0.3019 | 3.49 % | 1355 | 16.3 | 2641 | 2655 |
| vscode | cs-o-aot-ws | 0.5549 | 0.0085 | 1.54 % | 21129 | 254.4 | 922 | 951 |
| vscode | cs-o-aot-ws-single | 4.929 | 0.0067 | 0.14 % | 2378 | 28.6 | 888 | 910 |
| vscode | cs-o-aot-srv | 0.5804 | 0.0138 | 2.39 % | 20200 | 243.2 | 956 | 1001 |
| vscode | cs-o-aot-srv-single | 5.050 | 0.0107 | 0.21 % | 2322 | 28.0 | 897 | 928 |
| vscode | cs-o-jit-ws | 1.763 | 0.0499 | 2.83 % | 6652 | 80.1 | 971 | 978 |
| vscode | cs-o-jit-ws-single | 6.163 | 0.0634 | 1.03 % | 1902 | 22.9 | 929 | 930 |
| vscode | cs-o-jit-srv | 1.714 | 0.0512 | 2.98 % | 6840 | 82.4 | 977 | 996 |
| vscode | cs-o-jit-srv-single | 6.218 | 0.0579 | 0.93 % | 1885 | 22.7 | 930 | 942 |
| sentry | tsc6-parse | 4.786 | 0.0636 | 1.33 % | 1866 | 9.6 | 1207 | 1208 |
| sentry | tsgo7-parse | 0.4108 | 0.0094 | 2.30 % | 21744 | 111.8 | 801 | 823 |
| sentry | tsgo7-parse-single | 2.309 | 0.0054 | 0.23 % | 3868 | 19.9 | 762 | 777 |
| sentry | tsrs-parse | 0.1610 | 0.0034 | 2.11 % | 55473 | 285.3 | 342 | 654 |
| sentry | tsrs-parse-single | 1.269 | 0.0079 | 0.62 % | 7037 | 36.2 | 329 | 634 |
| sentry | cs-aot-ws | 2.956 | 0.0395 | 1.34 % | 3021 | 15.5 | 917 | 913 |
| sentry | cs-o-aot-ws | 0.2546 | 0.0051 | 1.99 % | 35081 | 180.4 | 326 | 324 |
| sentry | cs-o-aot-ws-single | 2.073 | 0.0037 | 0.18 % | 4308 | 22.2 | 306 | 304 |
| sentry | cs-o-aot-srv | 0.2944 | 0.0118 | 3.99 % | 30339 | 156.0 | 352 | 369 |
| sentry | cs-o-aot-srv-single | 2.167 | 0.0221 | 1.02 % | 4122 | 21.2 | 309 | 313 |
| sentry | cs-o-jit-ws | 0.6946 | 0.0069 | 0.99 % | 12859 | 66.1 | 360 | 342 |
| sentry | cs-o-jit-ws-single | 3.549 | 0.0140 | 0.39 % | 2516 | 12.9 | 344 | 319 |
| sentry | cs-o-jit-srv | 0.7339 | 0.0114 | 1.55 % | 12170 | 62.6 | 361 | 352 |
| sentry | cs-o-jit-srv-single | 3.595 | 0.0117 | 0.33 % | 2485 | 12.8 | 348 | 334 |
| bluesky | tsc6-parse | 1.249 | 0.0127 | 1.02 % | 1351 | 5.8 | 359 | 341 |
| bluesky | tsgo7-parse | 0.1076 | 0.0023 | 2.14 % | 15694 | 66.9 | 158 | 171 |
| bluesky | tsgo7-parse-single | 0.4411 | 0.0031 | 0.71 % | 3827 | 16.3 | 143 | 152 |
| bluesky | tsrs-parse | 0.0493 | 0.0009 | 1.87 % | 34270 | 146.1 | 70 | 213 |
| bluesky | tsrs-parse-single | 0.2549 | 0.0023 | 0.92 % | 6622 | 28.2 | 61 | 109 |
| bluesky | cs-aot-ws | 0.4855 | 0.0125 | 2.58 % | 3477 | 14.8 | 170 | 165 |
| bluesky | cs-o-aot-ws | 0.0668 | 0.0009 | 1.40 % | 25284 | 107.8 | 71 | 71 |
| bluesky | cs-o-aot-ws-single | 0.3828 | 0.0022 | 0.59 % | 4409 | 18.8 | 62 | 55 |
| bluesky | cs-o-aot-srv | 0.0936 | 0.0017 | 1.77 % | 18026 | 76.8 | 78 | 85 |
| bluesky | cs-o-aot-srv-single | 0.4166 | 0.0078 | 1.86 % | 4051 | 17.3 | 65 | 67 |
| bluesky | cs-o-jit-ws | 0.2907 | 0.0027 | 0.94 % | 5806 | 24.7 | 108 | 90 |
| bluesky | cs-o-jit-ws-single | 1.324 | 0.0198 | 1.49 % | 1275 | 5.4 | 98 | 74 |
| bluesky | cs-o-jit-srv | 0.3151 | 0.0044 | 1.40 % | 5356 | 22.8 | 112 | 103 |
| bluesky | cs-o-jit-srv-single | 1.346 | 0.0277 | 2.06 % | 1254 | 5.3 | 101 | 85 |
| playwright | tsc6-parse | 1.816 | 0.0086 | 0.47 % | 779 | 9.5 | 549 | 534 |
| playwright | tsgo7-parse | 0.1442 | 0.0026 | 1.81 % | 9809 | 119.5 | 271 | 287 |
| playwright | tsgo7-parse-single | 0.6042 | 0.0033 | 0.55 % | 2342 | 28.5 | 255 | 264 |
| playwright | tsrs-parse | 0.0588 | 0.0008 | 1.39 % | 24070 | 293.1 | 124 | 371 |
| playwright | tsrs-parse-single | 0.3084 | 0.0027 | 0.88 % | 4589 | 55.9 | 115 | 203 |
| playwright | cs-aot-ws | 0.9249 | 0.0316 | 3.42 % | 1530 | 18.6 | 324 | 319 |
| playwright | cs-o-aot-ws | 0.0877 | 0.0017 | 1.99 % | 16134 | 196.5 | 138 | 155 |
| playwright | cs-o-aot-ws-single | 0.5761 | 0.0015 | 0.26 % | 2456 | 29.9 | 115 | 114 |
| playwright | cs-o-aot-srv | 0.1094 | 0.0022 | 1.99 % | 12934 | 157.5 | 148 | 172 |
| playwright | cs-o-aot-srv-single | 0.5977 | 0.0055 | 0.92 % | 2367 | 28.8 | 121 | 127 |
| playwright | cs-o-jit-ws | 0.3808 | 0.0018 | 0.48 % | 3716 | 45.3 | 177 | 177 |
| playwright | cs-o-jit-ws-single | 2.060 | 0.0755 | 3.66 % | 687 | 8.4 | 153 | 134 |
| playwright | cs-o-jit-srv | 0.4075 | 0.0084 | 2.07 % | 3473 | 42.3 | 181 | 189 |
| playwright | cs-o-jit-srv-single | 2.062 | 0.1466 | 7.11 % | 686 | 8.4 | 157 | 146 |
| tldraw | tsc6-parse | 1.892 | 0.0148 | 0.78 % | 1326 | 7.2 | 534 | 520 |
| tldraw | tsgo7-parse | 0.1594 | 0.0039 | 2.42 % | 15735 | 85.5 | 273 | 289 |
| tldraw | tsgo7-parse-single | 0.7251 | 0.0038 | 0.52 % | 3459 | 18.8 | 257 | 269 |
| tldraw | tsrs-parse | 0.0668 | 0.0012 | 1.80 % | 37539 | 204.0 | 121 | 339 |
| tldraw | tsrs-parse-single | 0.3916 | 0.0033 | 0.85 % | 6404 | 34.8 | 112 | 196 |
| tldraw | cs-aot-ws | 0.9235 | 0.0249 | 2.70 % | 2716 | 14.8 | 310 | 307 |
| tldraw | cs-o-aot-ws | 0.0992 | 0.0030 | 3.06 % | 25290 | 137.4 | 127 | 131 |
| tldraw | cs-o-aot-ws-single | 0.6560 | 0.0024 | 0.36 % | 3823 | 20.8 | 109 | 104 |
| tldraw | cs-o-aot-srv | 0.1248 | 0.0030 | 2.37 % | 20093 | 109.2 | 134 | 146 |
| tldraw | cs-o-aot-srv-single | 0.7013 | 0.0114 | 1.62 % | 3576 | 19.4 | 112 | 113 |
| tldraw | cs-o-jit-ws | 0.3818 | 0.0060 | 1.58 % | 6569 | 35.7 | 167 | 148 |
| tldraw | cs-o-jit-ws-single | 1.789 | 0.0181 | 1.01 % | 1402 | 7.6 | 147 | 124 |
| tldraw | cs-o-jit-srv | 0.4002 | 0.0083 | 2.08 % | 6267 | 34.0 | 166 | 160 |
| tldraw | cs-o-jit-srv-single | 1.810 | 0.0126 | 0.70 % | 1386 | 7.5 | 152 | 136 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 11.79 [11.53, 12.10] | 11.65 [11.41, 12.13] | 11.61 [11.42, 11.79] | 12.59 [12.42, 12.82] | 11.87 [11.62, 12.17] | 11.90 [11.80, 12.05] |
| tsgo7-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-parse-single | 4.87 [4.76, 5.00] | 5.62 [5.51, 5.75] | 4.10 [4.04, 4.16] | 4.19 [4.13, 4.26] | 4.55 [4.47, 4.66] | 4.64 [4.60, 4.68] |
| tsrs-parse | 0.31 [0.30, 0.33] | 0.39 [0.38, 0.40] | 0.46 [0.44, 0.47] | 0.41 [0.40, 0.42] | 0.42 [0.41, 0.43] | 0.39 [0.39, 0.40] |
| tsrs-parse-single | 2.41 [2.35, 2.47] | 3.09 [3.03, 3.16] | 2.37 [2.33, 2.41] | 2.14 [2.11, 2.18] | 2.46 [2.41, 2.52] | 2.47 [2.45, 2.50] |
| cs-aot-ws | 8.39 [8.02, 8.77] | 7.20 [6.98, 7.35] | 4.51 [4.40, 4.60] | 6.41 [6.32, 6.64] | 5.79 [5.68, 6.02] | 6.33 [6.24, 6.42] |
| cs-o-aot-ws | 0.54 [0.52, 0.55] | 0.62 [0.60, 0.63] | 0.62 [0.61, 0.63] | 0.61 [0.60, 0.63] | 0.62 [0.59, 0.64] | 0.60 [0.59, 0.61] |
| cs-o-aot-ws-single | 4.78 [4.68, 4.89] | 5.05 [4.95, 5.16] | 3.56 [3.51, 3.61] | 3.99 [3.95, 4.07] | 4.12 [4.04, 4.22] | 4.27 [4.23, 4.31] |
| cs-o-aot-srv | 0.56 [0.54, 0.58] | 0.72 [0.68, 0.74] | 0.87 [0.86, 0.90] | 0.76 [0.75, 0.86] | 0.78 [0.77, 0.82] | 0.73 [0.72, 0.75] |
| cs-o-aot-srv-single | 4.90 [4.79, 5.01] | 5.27 [5.16, 5.39] | 3.87 [3.76, 3.93] | 4.14 [4.09, 4.23] | 4.40 [4.31, 4.53] | 4.49 [4.45, 4.54] |
| cs-o-jit-ws | 1.71 [1.65, 1.78] | 1.69 [1.66, 1.73] | 2.70 [2.66, 2.74] | 2.64 [2.61, 2.69] | 2.40 [2.34, 2.46] | 2.18 [2.16, 2.21] |
| cs-o-jit-ws-single | 5.98 [5.84, 6.17] | 8.64 [8.47, 8.83] | 12.31 [12.08, 12.53] | 14.28 [12.31, 14.63] | 11.22 [11.02, 11.64] | 10.04 [9.76, 10.17] |
| cs-o-jit-srv | 1.66 [1.60, 1.72] | 1.79 [1.75, 1.85] | 2.93 [2.89, 2.99] | 2.82 [2.76, 2.89] | 2.51 [2.46, 2.59] | 2.28 [2.26, 2.31] |
| cs-o-jit-srv-single | 6.03 [5.89, 6.22] | 8.75 [8.58, 8.94] | 12.51 [12.16, 12.70] | 14.29 [12.68, 14.78] | 11.35 [11.16, 11.73] | 10.14 [9.90, 10.27] |

## Table 2b. Median wall-clock relative to `tsrs-parse` (secondary reference), with 95 % confidence interval

Same method as Table 2, against tsrs (Rust port of TypeScript 7 / typescript-go), parallel. Secondary: never replaces the primary endpoint against typescript-go. tsrs parses JSDoc in .ts files lazily (TypeScript 7.1), typescript-go 7.0.2 eagerly, the C# tracks as tsc's host does (ParseForTypeErrors); see docs/references.md.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 37.76 [36.10, 38.85] | 29.73 [29.29, 30.91] | 25.36 [24.96, 26.35] | 30.90 [30.61, 31.38] | 28.31 [27.57, 28.71] | 30.15 [29.83, 30.52] |
| tsgo7-parse | 3.20 [3.04, 3.32] | 2.55 [2.49, 2.62] | 2.18 [2.15, 2.27] | 2.45 [2.40, 2.50] | 2.39 [2.30, 2.44] | 2.53 [2.50, 2.57] |
| tsgo7-parse-single | 15.61 [14.92, 16.02] | 14.34 [14.20, 14.70] | 8.96 [8.84, 9.30] | 10.28 [10.17, 10.42] | 10.85 [10.63, 10.98] | 11.75 [11.63, 11.87] |
| tsrs-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-parse-single | 7.72 [7.38, 7.92] | 7.88 [7.80, 8.09] | 5.17 [5.10, 5.39] | 5.25 [5.19, 5.33] | 5.86 [5.73, 5.95] | 6.27 [6.21, 6.34] |
| cs-aot-ws | 26.88 [25.30, 28.12] | 18.36 [17.88, 18.77] | 9.86 [9.62, 10.23] | 15.73 [15.56, 16.30] | 13.82 [13.48, 14.26] | 16.03 [15.79, 16.26] |
| cs-o-aot-ws | 1.72 [1.65, 1.77] | 1.58 [1.54, 1.62] | 1.36 [1.33, 1.41] | 1.49 [1.47, 1.54] | 1.48 [1.40, 1.51] | 1.52 [1.50, 1.54] |
| cs-o-aot-ws-single | 15.31 [14.65, 15.70] | 12.88 [12.75, 13.20] | 7.77 [7.67, 8.07] | 9.80 [9.72, 9.94] | 9.82 [9.63, 9.94] | 10.81 [10.70, 10.92] |
| cs-o-aot-srv | 1.80 [1.72, 1.87] | 1.83 [1.75, 1.89] | 1.90 [1.87, 1.99] | 1.86 [1.83, 2.10] | 1.87 [1.82, 1.93] | 1.85 [1.83, 1.90] |
| cs-o-aot-srv-single | 15.69 [15.00, 16.08] | 13.46 [13.25, 13.81] | 8.46 [8.22, 8.76] | 10.17 [10.07, 10.38] | 10.50 [10.22, 10.69] | 11.38 [11.24, 11.50] |
| cs-o-jit-ws | 5.48 [5.21, 5.71] | 4.31 [4.25, 4.42] | 5.90 [5.81, 6.13] | 6.48 [6.42, 6.58] | 5.71 [5.57, 5.80] | 5.53 [5.46, 5.59] |
| cs-o-jit-ws-single | 19.14 [18.31, 19.78] | 22.04 [21.82, 22.60] | 26.87 [26.42, 27.97] | 35.04 [30.20, 35.82] | 26.77 [26.22, 27.56] | 25.43 [24.70, 25.77] |
| cs-o-jit-srv | 5.32 [5.06, 5.49] | 4.56 [4.48, 4.71] | 6.40 [6.32, 6.68] | 6.93 [6.79, 7.07] | 5.99 [5.84, 6.11] | 5.78 [5.71, 5.86] |
| cs-o-jit-srv-single | 19.32 [18.46, 19.96] | 22.33 [22.10, 22.89] | 27.32 [26.58, 28.33] | 35.07 [31.12, 36.26] | 27.09 [26.54, 27.73] | 25.69 [25.04, 26.01] |

## Table 3. Variance gate (MAD/median < 3 %)

Binding rows (rig check): tsc6-parse, tsgo7-parse-single and the primary single-threaded C# configuration of each track. Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md, B11 as amended by prereg/v4).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | rig check | 0.59 % | 1.33 % | 1.02 % | 0.47 % | 0.78 % | 1.33 % |
| tsgo7-parse | reported | 3.10 % | 2.30 % | 2.14 % | 1.81 % | 2.42 % | 3.10 % |
| tsgo7-parse-single | rig check | 0.65 % | 0.23 % | 0.71 % | 0.55 % | 0.52 % | 0.71 % |
| tsrs-parse | reported | 4.51 % | 2.11 % | 1.87 % | 1.39 % | 1.80 % | 4.51 % |
| tsrs-parse-single | reported | 0.47 % | 0.62 % | 0.92 % | 0.88 % | 0.85 % | 0.92 % |
| cs-aot-ws | reported | 3.49 % | 1.34 % | 2.58 % | 3.42 % | 2.70 % | 3.49 % |
| cs-o-aot-ws | reported | 1.54 % | 1.99 % | 1.40 % | 1.99 % | 3.06 % | 3.06 % |
| cs-o-aot-ws-single | rig check | 0.14 % | 0.18 % | 0.59 % | 0.26 % | 0.36 % | 0.59 % |
| cs-o-aot-srv | reported | 2.39 % | 3.99 % | 1.77 % | 1.99 % | 2.37 % | 3.99 % |
| cs-o-aot-srv-single | reported | 0.21 % | 1.02 % | 1.86 % | 0.92 % | 1.62 % | 1.86 % |
| cs-o-jit-ws | reported | 2.83 % | 0.99 % | 0.94 % | 0.48 % | 1.58 % | 2.83 % |
| cs-o-jit-ws-single | reported | 1.03 % | 0.39 % | 1.49 % | 3.66 % | 1.01 % | 3.66 % |
| cs-o-jit-srv | reported | 2.98 % | 1.55 % | 1.40 % | 2.07 % | 2.08 % | 2.98 % |
| cs-o-jit-srv-single | reported | 0.93 % | 0.33 % | 2.06 % | 7.11 % | 0.70 % | 7.11 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsc6-parse | 0.2640 | 0.74 % | 21.1 % | 88 |
| tsgo7-parse | 0.0218 | 1.20 % | 20.3 % | 9 |
| tsgo7-parse-single | 0.0211 | 1.10 % | 4.8 % | 10 |
| tsrs-parse | 0.0187 | 1.72 % | 38.0 % | 8 |
| tsrs-parse-single | 0.0165 | 1.31 % | 6.5 % | 7 |
| cs-aot-ws | 0.0228 | 1.37 % | 4.7 % | 10 |
| cs-o-aot-ws | 0.0229 | 0.99 % | 34.3 % | 10 |
| cs-o-aot-ws-single | 0.0227 | 1.61 % | 5.9 % | 10 |
| cs-o-aot-srv | 0.0286 | 4.58 % | 30.5 % | 12 |
| cs-o-aot-srv-single | 0.0272 | 2.26 % | 6.5 % | 12 |
| cs-o-jit-ws | 0.0760 | 0.96 % | 26.1 % | 27 |
| cs-o-jit-ws-single | 0.0772 | 0.72 % | 5.8 % | 27 |
| cs-o-jit-srv | 0.0821 | 1.17 % | 26.0 % | 28 |
| cs-o-jit-srv-single | 0.0812 | 0.77 % | 6.0 % | 28 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-aot-ws | 3278439784 | 390/350/7 | 6805.5 |
| vscode | cs-o-aot-ws | 691421976 | 12/7/4 | 61.8 |
| vscode | cs-o-aot-ws-single | 668359904 | 12/10/6 | 35.6 |
| vscode | cs-o-aot-srv | 709397088 | 17/15/13 | 43.0 |
| vscode | cs-o-aot-srv-single | 668404888 | 20/17/17 | 22.2 |
| vscode | cs-o-jit-ws | 694925016 | 13/10/5 | 70.9 |
| vscode | cs-o-jit-ws-single | 669264328 | 12/10/6 | 31.4 |
| vscode | cs-o-jit-srv | 694314488 | 18/16/15 | 39.4 |
| vscode | cs-o-jit-srv-single | 669311592 | 20/18/17 | 19.5 |
| sentry | cs-aot-ws | 1095040944 | 139/136/6 | 2578.9 |
| sentry | cs-o-aot-ws | 228420504 | 8/6/4 | 33.3 |
| sentry | cs-o-aot-ws-single | 214653320 | 5/5/3 | 10.2 |
| sentry | cs-o-aot-srv | 245145632 | 13/11/10 | 30.7 |
| sentry | cs-o-aot-srv-single | 214676064 | 14/12/12 | 13.4 |
| sentry | cs-o-jit-ws | 229093328 | 8/7/5 | 29.6 |
| sentry | cs-o-jit-ws-single | 215483560 | 5/5/3 | 15.5 |
| sentry | cs-o-jit-srv | 229663480 | 14/13/12 | 27.7 |
| sentry | cs-o-jit-srv-single | 215566448 | 14/12/12 | 13.6 |
| bluesky | cs-aot-ws | 184930576 | 25/24/3 | 353.8 |
| bluesky | cs-o-aot-ws | 47604376 | 2/2/2 | 2.4 |
| bluesky | cs-o-aot-ws-single | 38264176 | 2/2/2 | 0.8 |
| bluesky | cs-o-aot-srv | 50382464 | 7/6/5 | 10.5 |
| bluesky | cs-o-aot-srv-single | 38280128 | 7/7/6 | 3.5 |
| bluesky | cs-o-jit-ws | 48412072 | 2/2/2 | 3.8 |
| bluesky | cs-o-jit-ws-single | 38985624 | 2/2/2 | 2.1 |
| bluesky | cs-o-jit-srv | 48569592 | 8/7/6 | 14.6 |
| bluesky | cs-o-jit-srv-single | 38997960 | 7/7/6 | 4.9 |
| playwright | cs-aot-ws | 378682992 | 47/45/4 | 739.3 |
| playwright | cs-o-aot-ws | 102210144 | 3/3/3 | 3.5 |
| playwright | cs-o-aot-ws-single | 78033008 | 3/3/3 | 0.9 |
| playwright | cs-o-aot-srv | 109818392 | 10/9/8 | 12.7 |
| playwright | cs-o-aot-srv-single | 78069224 | 11/10/9 | 4.7 |
| playwright | cs-o-jit-ws | 111010320 | 3/3/3 | 5.6 |
| playwright | cs-o-jit-ws-single | 78756392 | 3/3/3 | 1.2 |
| playwright | cs-o-jit-srv | 111495888 | 10/9/8 | 16.9 |
| playwright | cs-o-jit-srv-single | 78806336 | 11/10/9 | 8.0 |
| tldraw | cs-aot-ws | 356377008 | 47/44/4 | 719.5 |
| tldraw | cs-o-aot-ws | 90079368 | 3/3/2 | 11.0 |
| tldraw | cs-o-aot-ws-single | 76026104 | 2/2/2 | 0.8 |
| tldraw | cs-o-aot-srv | 102267448 | 9/8/7 | 11.7 |
| tldraw | cs-o-aot-srv-single | 76044912 | 11/10/9 | 6.8 |
| tldraw | cs-o-jit-ws | 91600736 | 4/2/2 | 13.3 |
| tldraw | cs-o-jit-ws-single | 76755944 | 2/2/2 | 1.2 |
| tldraw | cs-o-jit-srv | 91796952 | 10/9/8 | 17.2 |
| tldraw | cs-o-jit-srv-single | 76786576 | 10/9/9 | 5.4 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (70 files).
