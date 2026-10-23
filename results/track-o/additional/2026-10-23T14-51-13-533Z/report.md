# Parse-only benchmark, run 2026-10-23T14-51-13-533Z

**Status: protocol result.** All rig checks passed.

Label: track-o-additional-run-after-O24

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 99 % of 4150 MHz, idle before run 10.9 % (median of 1 s windows: 12.3, 11.2, 8.7, 8.8, 34.5, 10.7, 13.1, 12.5, 7.8, 8) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 4e32b2600b, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 99 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 10.9 % (range 7.8-34.5 %) | ok |
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
| vscode | tsc6-parse | 12.13 | 0.1171 | 0.97 % | 967 | 11.6 | 3193 | 3225 |
| vscode | tsgo7-parse | 1.030 | 0.0204 | 1.98 % | 11385 | 137.1 | 2188 | 2216 |
| vscode | tsgo7-parse-single | 5.087 | 0.0495 | 0.97 % | 2305 | 27.8 | 2112 | 2178 |
| vscode | tsrs-parse | 0.3484 | 0.0110 | 3.15 % | 33650 | 405.2 | 955 | 1938 |
| vscode | tsrs-parse-single | 2.489 | 0.0090 | 0.36 % | 4710 | 56.7 | 940 | 1253 |
| vscode | cs-aot-ws | 8.905 | 0.2284 | 2.57 % | 1317 | 15.9 | 2635 | 2647 |
| vscode | cs-o-aot-ws | 0.3315 | 0.0077 | 2.31 % | 35363 | 425.8 | 881 | 1037 |
| vscode | cs-o-aot-ws-single | 3.052 | 0.0038 | 0.12 % | 3841 | 46.3 | 853 | 870 |
| vscode | cs-o-aot-srv | 0.3106 | 0.0099 | 3.18 % | 37741 | 454.5 | 891 | 1087 |
| vscode | cs-o-aot-srv-single | 3.025 | 0.0108 | 0.36 % | 3876 | 46.7 | 859 | 885 |
| vscode | cs-o-jit-ws | 1.181 | 0.0238 | 2.02 % | 9931 | 119.6 | 918 | 1042 |
| vscode | cs-o-jit-ws-single | 4.824 | 0.0555 | 1.15 % | 2430 | 29.3 | 895 | 893 |
| vscode | cs-o-jit-srv | 1.106 | 0.0413 | 3.73 % | 10597 | 127.6 | 923 | 1058 |
| vscode | cs-o-jit-srv-single | 4.776 | 0.0707 | 1.48 % | 2455 | 29.6 | 901 | 910 |
| sentry | tsc6-parse | 4.967 | 0.0785 | 1.58 % | 1798 | 9.2 | 1217 | 1208 |
| sentry | tsgo7-parse | 0.4217 | 0.0092 | 2.17 % | 21182 | 108.9 | 803 | 822 |
| sentry | tsgo7-parse-single | 2.317 | 0.0094 | 0.41 % | 3854 | 19.8 | 760 | 778 |
| sentry | tsrs-parse | 0.1654 | 0.0038 | 2.33 % | 54009 | 277.8 | 342 | 658 |
| sentry | tsrs-parse-single | 1.274 | 0.0065 | 0.51 % | 7009 | 36.0 | 329 | 634 |
| sentry | cs-aot-ws | 2.942 | 0.0508 | 1.73 % | 3036 | 15.6 | 915 | 914 |
| sentry | cs-o-aot-ws | 0.1875 | 0.0015 | 0.80 % | 47638 | 245.0 | 321 | 426 |
| sentry | cs-o-aot-ws-single | 1.436 | 0.0084 | 0.58 % | 6219 | 32.0 | 303 | 305 |
| sentry | cs-o-aot-srv | 0.1707 | 0.0042 | 2.45 % | 52313 | 269.1 | 320 | 454 |
| sentry | cs-o-aot-srv-single | 1.437 | 0.0072 | 0.50 % | 6217 | 32.0 | 305 | 314 |
| sentry | cs-o-jit-ws | 0.5484 | 0.0065 | 1.18 % | 16287 | 83.8 | 358 | 451 |
| sentry | cs-o-jit-ws-single | 3.113 | 0.0230 | 0.74 % | 2869 | 14.8 | 342 | 325 |
| sentry | cs-o-jit-srv | 0.5263 | 0.0083 | 1.58 % | 16973 | 87.3 | 356 | 467 |
| sentry | cs-o-jit-srv-single | 3.106 | 0.0904 | 2.91 % | 2876 | 14.8 | 344 | 334 |
| bluesky | tsc6-parse | 1.257 | 0.0096 | 0.77 % | 1343 | 5.7 | 368 | 341 |
| bluesky | tsgo7-parse | 0.1103 | 0.0030 | 2.76 % | 15304 | 65.2 | 159 | 170 |
| bluesky | tsgo7-parse-single | 0.4345 | 0.0031 | 0.72 % | 3885 | 16.6 | 143 | 151 |
| bluesky | tsrs-parse | 0.0492 | 0.0004 | 0.79 % | 34343 | 146.4 | 70 | 211 |
| bluesky | tsrs-parse-single | 0.2462 | 0.0022 | 0.88 % | 6856 | 29.2 | 61 | 109 |
| bluesky | cs-aot-ws | 0.4854 | 0.0098 | 2.02 % | 3478 | 14.8 | 168 | 164 |
| bluesky | cs-o-aot-ws | 0.0554 | 0.0013 | 2.29 % | 30453 | 129.8 | 69 | 86 |
| bluesky | cs-o-aot-ws-single | 0.2760 | 0.0026 | 0.95 % | 6116 | 26.1 | 61 | 57 |
| bluesky | cs-o-aot-srv | 0.0680 | 0.0040 | 5.82 % | 24839 | 105.9 | 74 | 102 |
| bluesky | cs-o-aot-srv-single | 0.2823 | 0.0023 | 0.80 % | 5979 | 25.5 | 64 | 70 |
| bluesky | cs-o-jit-ws | 0.2700 | 0.0043 | 1.60 % | 6251 | 26.6 | 106 | 109 |
| bluesky | cs-o-jit-ws-single | 0.9299 | 0.0039 | 0.42 % | 1815 | 7.7 | 97 | 77 |
| bluesky | cs-o-jit-srv | 0.2807 | 0.0030 | 1.07 % | 6014 | 25.6 | 111 | 121 |
| bluesky | cs-o-jit-srv-single | 0.9389 | 0.0061 | 0.65 % | 1798 | 7.7 | 99 | 89 |
| playwright | tsc6-parse | 1.822 | 0.0101 | 0.55 % | 777 | 9.5 | 558 | 534 |
| playwright | tsgo7-parse | 0.1517 | 0.0039 | 2.60 % | 9328 | 113.6 | 270 | 284 |
| playwright | tsgo7-parse-single | 0.6024 | 0.0034 | 0.56 % | 2349 | 28.6 | 256 | 265 |
| playwright | tsrs-parse | 0.0623 | 0.0012 | 1.96 % | 22717 | 276.7 | 124 | 367 |
| playwright | tsrs-parse-single | 0.3018 | 0.0025 | 0.84 % | 4689 | 57.1 | 115 | 202 |
| playwright | cs-aot-ws | 0.8845 | 0.0584 | 6.60 % | 1600 | 19.5 | 322 | 318 |
| playwright | cs-o-aot-ws | 0.0664 | 0.0012 | 1.76 % | 21309 | 259.5 | 126 | 188 |
| playwright | cs-o-aot-ws-single | 0.3687 | 0.0018 | 0.48 % | 3838 | 46.7 | 112 | 116 |
| playwright | cs-o-aot-srv | 0.0803 | 0.0037 | 4.56 % | 17620 | 214.6 | 133 | 199 |
| playwright | cs-o-aot-srv-single | 0.3749 | 0.0027 | 0.73 % | 3774 | 46.0 | 115 | 128 |
| playwright | cs-o-jit-ws | 0.3183 | 0.0031 | 0.98 % | 4445 | 54.1 | 164 | 211 |
| playwright | cs-o-jit-ws-single | 1.441 | 0.0223 | 1.54 % | 982 | 12.0 | 148 | 135 |
| playwright | cs-o-jit-srv | 0.3364 | 0.0055 | 1.63 % | 4207 | 51.2 | 169 | 226 |
| playwright | cs-o-jit-srv-single | 1.360 | 0.0447 | 3.28 % | 1040 | 12.7 | 150 | 147 |
| tldraw | tsc6-parse | 1.891 | 0.0142 | 0.75 % | 1327 | 7.2 | 544 | 520 |
| tldraw | tsgo7-parse | 0.1595 | 0.0031 | 1.92 % | 15719 | 85.4 | 271 | 284 |
| tldraw | tsgo7-parse-single | 0.7227 | 0.0019 | 0.26 % | 3470 | 18.9 | 257 | 269 |
| tldraw | tsrs-parse | 0.0713 | 0.0012 | 1.64 % | 35160 | 191.0 | 121 | 341 |
| tldraw | tsrs-parse-single | 0.3931 | 0.0035 | 0.90 % | 6380 | 34.7 | 112 | 196 |
| tldraw | cs-aot-ws | 0.9533 | 0.0212 | 2.22 % | 2631 | 14.3 | 310 | 308 |
| tldraw | cs-o-aot-ws | 0.0735 | 0.0024 | 3.31 % | 34116 | 185.4 | 116 | 163 |
| tldraw | cs-o-aot-ws-single | 0.4497 | 0.0044 | 0.99 % | 5578 | 30.3 | 105 | 106 |
| tldraw | cs-o-aot-srv | 0.0897 | 0.0034 | 3.82 % | 27962 | 151.9 | 120 | 167 |
| tldraw | cs-o-aot-srv-single | 0.4574 | 0.0034 | 0.75 % | 5483 | 29.8 | 108 | 117 |
| tldraw | cs-o-jit-ws | 0.3179 | 0.0028 | 0.88 % | 7890 | 42.9 | 153 | 183 |
| tldraw | cs-o-jit-ws-single | 1.453 | 0.0195 | 1.34 % | 1726 | 9.4 | 142 | 126 |
| tldraw | cs-o-jit-srv | 0.3319 | 0.0058 | 1.74 % | 7556 | 41.1 | 156 | 196 |
| tldraw | cs-o-jit-srv-single | 1.490 | 0.0202 | 1.35 % | 1683 | 9.1 | 144 | 137 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 11.78 [11.59, 11.97] | 11.78 [11.38, 12.20] | 11.39 [11.14, 11.73] | 12.01 [11.79, 12.43] | 11.85 [11.60, 12.01] | 11.76 [11.64, 11.90] |
| tsgo7-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-parse-single | 4.94 [4.84, 5.00] | 5.50 [5.45, 5.70] | 3.94 [3.86, 4.06] | 3.97 [3.90, 4.11] | 4.53 [4.44, 4.59] | 4.54 [4.50, 4.59] |
| tsrs-parse | 0.34 [0.33, 0.35] | 0.39 [0.38, 0.41] | 0.45 [0.44, 0.46] | 0.41 [0.40, 0.42] | 0.45 [0.44, 0.46] | 0.40 [0.40, 0.41] |
| tsrs-parse-single | 2.42 [2.38, 2.45] | 3.02 [2.99, 3.13] | 2.23 [2.18, 2.31] | 1.99 [1.95, 2.06] | 2.46 [2.41, 2.50] | 2.40 [2.38, 2.43] |
| cs-aot-ws | 8.65 [8.31, 8.83] | 6.98 [6.90, 7.27] | 4.40 [4.27, 4.54] | 5.83 [5.60, 6.32] | 5.97 [5.79, 6.07] | 6.21 [6.12, 6.34] |
| cs-o-aot-ws | 0.32 [0.32, 0.33] | 0.44 [0.44, 0.46] | 0.50 [0.49, 0.53] | 0.44 [0.43, 0.45] | 0.46 [0.45, 0.47] | 0.43 [0.42, 0.44] |
| cs-o-aot-ws-single | 2.96 [2.92, 3.00] | 3.41 [3.37, 3.53] | 2.50 [2.44, 2.58] | 2.43 [2.39, 2.52] | 2.82 [2.76, 2.86] | 2.80 [2.78, 2.84] |
| cs-o-aot-srv | 0.30 [0.29, 0.31] | 0.40 [0.40, 0.42] | 0.62 [0.59, 0.68] | 0.53 [0.51, 0.55] | 0.56 [0.54, 0.59] | 0.47 [0.46, 0.48] |
| cs-o-aot-srv-single | 2.94 [2.90, 2.97] | 3.41 [3.37, 3.53] | 2.56 [2.50, 2.64] | 2.47 [2.42, 2.56] | 2.87 [2.81, 2.90] | 2.83 [2.81, 2.86] |
| cs-o-jit-ws | 1.15 [1.13, 1.17] | 1.30 [1.28, 1.35] | 2.45 [2.39, 2.52] | 2.10 [2.06, 2.17] | 1.99 [1.95, 2.02] | 1.72 [1.71, 1.75] |
| cs-o-jit-ws-single | 4.68 [4.61, 4.75] | 7.38 [7.30, 7.78] | 8.43 [8.25, 8.68] | 9.50 [8.75, 9.82] | 9.11 [8.91, 9.31] | 7.59 [7.45, 7.69] |
| cs-o-jit-srv | 1.07 [1.04, 1.12] | 1.25 [1.23, 1.30] | 2.54 [2.48, 2.62] | 2.22 [2.17, 2.29] | 2.08 [2.03, 2.12] | 1.74 [1.72, 1.76] |
| cs-o-jit-srv-single | 4.64 [4.54, 4.73] | 7.37 [7.28, 7.69] | 8.51 [8.33, 8.76] | 8.97 [8.78, 9.69] | 9.34 [9.10, 9.48] | 7.54 [7.47, 7.70] |

## Table 2b. Median wall-clock relative to `tsrs-parse` (secondary reference), with 95 % confidence interval

Same method as Table 2, against tsrs (Rust port of TypeScript 7 / typescript-go), parallel. Secondary: never replaces the primary endpoint against typescript-go. tsrs parses JSDoc in .ts files lazily (TypeScript 7.1), typescript-go 7.0.2 eagerly, the C# tracks as tsc's host does (ParseForTypeErrors); see docs/references.md.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 34.81 [33.90, 35.87] | 30.03 [28.95, 30.62] | 25.57 [25.27, 25.80] | 29.24 [28.95, 29.90] | 26.51 [26.02, 26.91] | 29.06 [28.76, 29.32] |
| tsgo7-parse | 2.96 [2.87, 3.05] | 2.55 [2.45, 2.61] | 2.24 [2.18, 2.30] | 2.44 [2.36, 2.51] | 2.24 [2.19, 2.30] | 2.47 [2.44, 2.50] |
| tsgo7-parse-single | 14.60 [14.18, 15.00] | 14.01 [13.73, 14.28] | 8.84 [8.75, 8.93] | 9.67 [9.58, 9.88] | 10.13 [9.99, 10.28] | 11.21 [11.12, 11.31] |
| tsrs-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-parse-single | 7.14 [6.97, 7.34] | 7.71 [7.54, 7.85] | 5.01 [4.96, 5.09] | 4.84 [4.78, 4.95] | 5.51 [5.40, 5.60] | 5.93 [5.89, 5.99] |
| cs-aot-ws | 25.56 [24.39, 26.35] | 17.79 [17.42, 18.24] | 9.87 [9.67, 10.02] | 14.20 [13.69, 15.37] | 13.36 [12.97, 13.62] | 15.35 [15.14, 15.64] |
| cs-o-aot-ws | 0.95 [0.92, 0.99] | 1.13 [1.11, 1.16] | 1.13 [1.12, 1.16] | 1.07 [1.04, 1.09] | 1.03 [1.00, 1.06] | 1.06 [1.05, 1.07] |
| cs-o-aot-ws-single | 8.76 [8.55, 9.00] | 8.68 [8.50, 8.84] | 5.62 [5.55, 5.69] | 5.92 [5.86, 6.05] | 6.30 [6.19, 6.42] | 6.93 [6.87, 6.99] |
| cs-o-aot-srv | 0.89 [0.86, 0.93] | 1.03 [1.01, 1.06] | 1.38 [1.34, 1.50] | 1.29 [1.26, 1.33] | 1.26 [1.21, 1.32] | 1.16 [1.14, 1.18] |
| cs-o-aot-srv-single | 8.68 [8.46, 8.92] | 8.69 [8.50, 8.84] | 5.74 [5.68, 5.80] | 6.02 [5.95, 6.15] | 6.41 [6.30, 6.51] | 6.99 [6.93, 7.05] |
| cs-o-jit-ws | 3.39 [3.30, 3.50] | 3.32 [3.24, 3.39] | 5.49 [5.41, 5.55] | 5.11 [5.05, 5.23] | 4.46 [4.39, 4.53] | 4.26 [4.22, 4.31] |
| cs-o-jit-ws-single | 13.84 [13.48, 14.25] | 18.83 [18.42, 19.71] | 18.92 [18.74, 19.08] | 23.14 [21.45, 23.72] | 20.38 [19.98, 20.89] | 18.76 [18.40, 18.98] |
| cs-o-jit-srv | 3.18 [3.06, 3.33] | 3.18 [3.11, 3.26] | 5.71 [5.63, 5.75] | 5.40 [5.30, 5.52] | 4.65 [4.54, 4.76] | 4.29 [4.24, 4.34] |
| cs-o-jit-srv-single | 13.71 [13.28, 14.15] | 18.78 [18.38, 19.37] | 19.10 [18.91, 19.26] | 21.84 [21.51, 23.58] | 20.89 [20.36, 21.26] | 18.63 [18.46, 18.99] |

## Table 3. Variance gate (MAD/median < 3 %)

Binding rows (rig check): tsc6-parse, tsgo7-parse-single and the primary single-threaded C# configuration of each track. Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md, B11 as amended by prereg/v4).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | rig check | 0.97 % | 1.58 % | 0.77 % | 0.55 % | 0.75 % | 1.58 % |
| tsgo7-parse | reported | 1.98 % | 2.17 % | 2.76 % | 2.60 % | 1.92 % | 2.76 % |
| tsgo7-parse-single | rig check | 0.97 % | 0.41 % | 0.72 % | 0.56 % | 0.26 % | 0.97 % |
| tsrs-parse | reported | 3.15 % | 2.33 % | 0.79 % | 1.96 % | 1.64 % | 3.15 % |
| tsrs-parse-single | reported | 0.36 % | 0.51 % | 0.88 % | 0.84 % | 0.90 % | 0.90 % |
| cs-aot-ws | reported | 2.57 % | 1.73 % | 2.02 % | 6.60 % | 2.22 % | 6.60 % |
| cs-o-aot-ws | reported | 2.31 % | 0.80 % | 2.29 % | 1.76 % | 3.31 % | 3.31 % |
| cs-o-aot-ws-single | rig check | 0.12 % | 0.58 % | 0.95 % | 0.48 % | 0.99 % | 0.99 % |
| cs-o-aot-srv | reported | 3.18 % | 2.45 % | 5.82 % | 4.56 % | 3.82 % | 5.82 % |
| cs-o-aot-srv-single | reported | 0.36 % | 0.50 % | 0.80 % | 0.73 % | 0.75 % | 0.80 % |
| cs-o-jit-ws | reported | 2.02 % | 1.18 % | 1.60 % | 0.98 % | 0.88 % | 2.02 % |
| cs-o-jit-ws-single | reported | 1.15 % | 0.74 % | 0.42 % | 1.54 % | 1.34 % | 1.54 % |
| cs-o-jit-srv | reported | 3.73 % | 1.58 % | 1.07 % | 1.63 % | 1.74 % | 3.73 % |
| cs-o-jit-srv-single | reported | 1.48 % | 2.91 % | 0.65 % | 3.28 % | 1.35 % | 3.28 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsc6-parse | 0.2642 | 0.64 % | 21.0 % | 97 |
| tsgo7-parse | 0.0202 | 1.56 % | 18.3 % | 9 |
| tsgo7-parse-single | 0.0204 | 0.91 % | 4.7 % | 9 |
| tsrs-parse | 0.0184 | 0.75 % | 37.4 % | 8 |
| tsrs-parse-single | 0.0166 | 1.08 % | 6.7 % | 7 |
| cs-aot-ws | 0.0239 | 0.83 % | 4.9 % | 10 |
| cs-o-aot-ws | 0.0240 | 1.38 % | 43.2 % | 10 |
| cs-o-aot-ws-single | 0.0230 | 0.73 % | 8.3 % | 10 |
| cs-o-aot-srv | 0.0269 | 0.85 % | 39.6 % | 12 |
| cs-o-aot-srv-single | 0.0278 | 3.89 % | 9.9 % | 12 |
| cs-o-jit-ws | 0.0768 | 0.67 % | 28.4 % | 27 |
| cs-o-jit-ws-single | 0.0760 | 0.92 % | 8.2 % | 26 |
| cs-o-jit-srv | 0.0823 | 1.84 % | 29.3 % | 28 |
| cs-o-jit-srv-single | 0.0826 | 2.77 % | 8.8 % | 28 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-aot-ws | 3278130712 | 390/351/7 | 6791.5 |
| vscode | cs-o-aot-ws | 62109344 | 7/5/3 | 37.4 |
| vscode | cs-o-aot-ws-single | 36057248 | 5/3/1 | 21.9 |
| vscode | cs-o-aot-srv | 104364480 | 7/6/5 | 8.9 |
| vscode | cs-o-aot-srv-single | 36053648 | 3/1/0 | 8.2 |
| vscode | cs-o-jit-ws | 61994248 | 7/5/3 | 38.3 |
| vscode | cs-o-jit-ws-single | 36961488 | 5/3/1 | 19.7 |
| vscode | cs-o-jit-srv | 62224520 | 6/5/4 | 9.4 |
| vscode | cs-o-jit-srv-single | 36960320 | 3/1/0 | 8.1 |
| sentry | cs-aot-ws | 1094869672 | 138/134/6 | 2494.7 |
| sentry | cs-o-aot-ws | 47913064 | 5/5/3 | 26.0 |
| sentry | cs-o-aot-ws-single | 20364736 | 2/1/0 | 9.6 |
| sentry | cs-o-aot-srv | 50275304 | 5/4/4 | 6.3 |
| sentry | cs-o-aot-srv-single | 20364400 | 3/1/0 | 7.5 |
| sentry | cs-o-jit-ws | 45497352 | 5/5/3 | 31.3 |
| sentry | cs-o-jit-ws-single | 21223728 | 2/1/0 | 9.4 |
| sentry | cs-o-jit-srv | 45341816 | 6/5/4 | 9.6 |
| sentry | cs-o-jit-srv-single | 21201768 | 3/1/0 | 7.5 |
| bluesky | cs-aot-ws | 185106504 | 25/24/3 | 387.5 |
| bluesky | cs-o-aot-ws | 22951104 | 2/2/2 | 1.9 |
| bluesky | cs-o-aot-ws-single | 4673048 | 0/0/0 | 0.0 |
| bluesky | cs-o-aot-srv | 27880544 | 6/5/4 | 10.6 |
| bluesky | cs-o-aot-srv-single | 4673744 | 1/0/0 | 1.9 |
| bluesky | cs-o-jit-ws | 23993592 | 2/2/2 | 3.8 |
| bluesky | cs-o-jit-ws-single | 5390760 | 0/0/0 | 0.0 |
| bluesky | cs-o-jit-srv | 24083320 | 6/5/4 | 11.6 |
| bluesky | cs-o-jit-srv-single | 5392320 | 1/0/0 | 2.0 |
| playwright | cs-aot-ws | 378646304 | 47/45/4 | 664.4 |
| playwright | cs-o-aot-ws | 25281032 | 2/2/2 | 1.9 |
| playwright | cs-o-aot-ws-single | 4427976 | 0/0/0 | 0.0 |
| playwright | cs-o-aot-srv | 30983656 | 7/6/5 | 8.9 |
| playwright | cs-o-aot-srv-single | 4428672 | 1/0/0 | 2.1 |
| playwright | cs-o-jit-ws | 27399608 | 2/2/2 | 3.6 |
| playwright | cs-o-jit-ws-single | 5141296 | 0/0/0 | 0.0 |
| playwright | cs-o-jit-srv | 26458360 | 6/5/4 | 11.8 |
| playwright | cs-o-jit-srv-single | 5142856 | 1/0/0 | 1.9 |
| tldraw | cs-aot-ws | 356410504 | 47/43/4 | 756.9 |
| tldraw | cs-o-aot-ws | 30663056 | 2/2/2 | 1.9 |
| tldraw | cs-o-aot-ws-single | 7361656 | 0/0/0 | 0.0 |
| tldraw | cs-o-aot-srv | 34809048 | 6/5/4 | 11.0 |
| tldraw | cs-o-aot-srv-single | 7362688 | 2/1/0 | 4.8 |
| tldraw | cs-o-jit-ws | 30269952 | 2/2/2 | 3.0 |
| tldraw | cs-o-jit-ws-single | 8092488 | 0/0/0 | 0.0 |
| tldraw | cs-o-jit-srv | 31396296 | 7/6/5 | 16.5 |
| tldraw | cs-o-jit-srv-single | 8094608 | 2/1/0 | 5.1 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (70 files).
