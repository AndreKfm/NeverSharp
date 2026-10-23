# Parse-only benchmark, run 2026-10-23T17-40-49-265Z

**Status: protocol result.** All rig checks passed.

Label: track-o-additional-run-after-O26

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 6.9 % (median of 1 s windows: 7.3, 13.6, 6.4, 1.2, 8.6, 2.5, 3.6, 7.3, 8.4, 4.6) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit dadb71f854, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 6.9 % (range 1.2-13.6 %) | ok |
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
| vscode | tsc6-parse | 11.98 | 0.0591 | 0.49 % | 979 | 11.8 | 3192 | 3224 |
| vscode | tsgo7-parse | 1.028 | 0.0208 | 2.03 % | 11405 | 137.3 | 2177 | 2212 |
| vscode | tsgo7-parse-single | 4.992 | 0.0143 | 0.29 % | 2349 | 28.3 | 2131 | 2160 |
| vscode | tsrs-parse | 0.3291 | 0.0184 | 5.59 % | 35628 | 429.0 | 955 | 1987 |
| vscode | tsrs-parse-single | 2.468 | 0.0081 | 0.33 % | 4751 | 57.2 | 940 | 1253 |
| vscode | cs-aot-ws | 8.448 | 0.3295 | 3.90 % | 1388 | 16.7 | 2639 | 2647 |
| vscode | cs-o-aot-ws | 0.3129 | 0.0061 | 1.94 % | 37466 | 451.2 | 881 | 1034 |
| vscode | cs-o-aot-ws-single | 2.719 | 0.0072 | 0.26 % | 4311 | 51.9 | 853 | 870 |
| vscode | cs-o-aot-srv | 0.2908 | 0.0082 | 2.81 % | 40313 | 485.4 | 889 | 1061 |
| vscode | cs-o-aot-srv-single | 2.715 | 0.0064 | 0.24 % | 4319 | 52.0 | 859 | 885 |
| vscode | cs-o-jit-ws | 1.009 | 0.0267 | 2.64 % | 11616 | 139.9 | 918 | 1042 |
| vscode | cs-o-jit-ws-single | 4.719 | 0.0407 | 0.86 % | 2484 | 29.9 | 896 | 893 |
| vscode | cs-o-jit-srv | 0.9763 | 0.0204 | 2.09 % | 12009 | 144.6 | 923 | 1062 |
| vscode | cs-o-jit-srv-single | 4.716 | 0.0414 | 0.88 % | 2486 | 29.9 | 897 | 906 |
| sentry | tsc6-parse | 4.887 | 0.0911 | 1.86 % | 1828 | 9.4 | 1216 | 1208 |
| sentry | tsgo7-parse | 0.4075 | 0.0088 | 2.16 % | 21920 | 112.7 | 797 | 820 |
| sentry | tsgo7-parse-single | 2.293 | 0.0098 | 0.43 % | 3895 | 20.0 | 760 | 776 |
| sentry | tsrs-parse | 0.1591 | 0.0029 | 1.79 % | 56126 | 288.7 | 342 | 656 |
| sentry | tsrs-parse-single | 1.271 | 0.0061 | 0.48 % | 7025 | 36.1 | 329 | 634 |
| sentry | cs-aot-ws | 2.896 | 0.0566 | 1.95 % | 3085 | 15.9 | 913 | 911 |
| sentry | cs-o-aot-ws | 0.1779 | 0.0071 | 4.01 % | 50207 | 258.2 | 321 | 433 |
| sentry | cs-o-aot-ws-single | 1.319 | 0.0056 | 0.42 % | 6772 | 34.8 | 304 | 305 |
| sentry | cs-o-aot-srv | 0.1586 | 0.0037 | 2.35 % | 56315 | 289.6 | 319 | 441 |
| sentry | cs-o-aot-srv-single | 1.334 | 0.0052 | 0.39 % | 6697 | 34.4 | 305 | 314 |
| sentry | cs-o-jit-ws | 0.5090 | 0.0053 | 1.04 % | 17546 | 90.2 | 357 | 443 |
| sentry | cs-o-jit-ws-single | 3.042 | 0.0741 | 2.44 % | 2936 | 15.1 | 342 | 325 |
| sentry | cs-o-jit-srv | 0.4988 | 0.0061 | 1.22 % | 17906 | 92.1 | 357 | 457 |
| sentry | cs-o-jit-srv-single | 2.983 | 0.0796 | 2.67 % | 2994 | 15.4 | 344 | 334 |
| bluesky | tsc6-parse | 1.251 | 0.0118 | 0.95 % | 1349 | 5.8 | 368 | 340 |
| bluesky | tsgo7-parse | 0.1087 | 0.0026 | 2.35 % | 15526 | 66.2 | 159 | 170 |
| bluesky | tsgo7-parse-single | 0.4382 | 0.0030 | 0.70 % | 3852 | 16.4 | 143 | 152 |
| bluesky | tsrs-parse | 0.0508 | 0.0012 | 2.35 % | 33231 | 141.6 | 70 | 211 |
| bluesky | tsrs-parse-single | 0.2447 | 0.0017 | 0.71 % | 6899 | 29.4 | 61 | 109 |
| bluesky | cs-aot-ws | 0.4717 | 0.0090 | 1.91 % | 3578 | 15.3 | 168 | 165 |
| bluesky | cs-o-aot-ws | 0.0522 | 0.0010 | 1.93 % | 32331 | 137.8 | 69 | 88 |
| bluesky | cs-o-aot-ws-single | 0.2617 | 0.0022 | 0.85 % | 6451 | 27.5 | 61 | 57 |
| bluesky | cs-o-aot-srv | 0.0682 | 0.0041 | 5.97 % | 24747 | 105.5 | 74 | 103 |
| bluesky | cs-o-aot-srv-single | 0.2734 | 0.0015 | 0.55 % | 6175 | 26.3 | 64 | 70 |
| bluesky | cs-o-jit-ws | 0.2592 | 0.0029 | 1.13 % | 6513 | 27.8 | 105 | 107 |
| bluesky | cs-o-jit-ws-single | 0.8825 | 0.0042 | 0.48 % | 1913 | 8.2 | 97 | 77 |
| bluesky | cs-o-jit-srv | 0.2721 | 0.0020 | 0.74 % | 6205 | 26.4 | 111 | 121 |
| bluesky | cs-o-jit-srv-single | 0.8935 | 0.0069 | 0.78 % | 1889 | 8.1 | 99 | 89 |
| playwright | tsc6-parse | 1.807 | 0.0083 | 0.46 % | 783 | 9.5 | 558 | 535 |
| playwright | tsgo7-parse | 0.1515 | 0.0063 | 4.17 % | 9341 | 113.8 | 272 | 286 |
| playwright | tsgo7-parse-single | 0.5934 | 0.0016 | 0.27 % | 2384 | 29.0 | 255 | 265 |
| playwright | tsrs-parse | 0.0577 | 0.0009 | 1.49 % | 24509 | 298.5 | 124 | 364 |
| playwright | tsrs-parse-single | 0.3002 | 0.0023 | 0.77 % | 4714 | 57.4 | 115 | 203 |
| playwright | cs-aot-ws | 0.9290 | 0.0258 | 2.78 % | 1523 | 18.5 | 324 | 319 |
| playwright | cs-o-aot-ws | 0.0625 | 0.0009 | 1.48 % | 22626 | 275.5 | 127 | 187 |
| playwright | cs-o-aot-ws-single | 0.3343 | 0.0021 | 0.61 % | 4233 | 51.6 | 112 | 116 |
| playwright | cs-o-aot-srv | 0.0744 | 0.0025 | 3.34 % | 19018 | 231.6 | 134 | 201 |
| playwright | cs-o-aot-srv-single | 0.3490 | 0.0037 | 1.06 % | 4054 | 49.4 | 115 | 128 |
| playwright | cs-o-jit-ws | 0.3059 | 0.0037 | 1.22 % | 4626 | 56.3 | 163 | 209 |
| playwright | cs-o-jit-ws-single | 1.235 | 0.0057 | 0.46 % | 1146 | 14.0 | 147 | 135 |
| playwright | cs-o-jit-srv | 0.3189 | 0.0025 | 0.77 % | 4437 | 54.0 | 169 | 226 |
| playwright | cs-o-jit-srv-single | 1.242 | 0.0043 | 0.35 % | 1140 | 13.9 | 150 | 147 |
| tldraw | tsc6-parse | 1.867 | 0.0137 | 0.73 % | 1343 | 7.3 | 544 | 521 |
| tldraw | tsgo7-parse | 0.1573 | 0.0038 | 2.40 % | 15946 | 86.6 | 275 | 290 |
| tldraw | tsgo7-parse-single | 0.7158 | 0.0033 | 0.46 % | 3504 | 19.0 | 257 | 269 |
| tldraw | tsrs-parse | 0.0673 | 0.0013 | 1.93 % | 37270 | 202.5 | 121 | 337 |
| tldraw | tsrs-parse-single | 0.3889 | 0.0021 | 0.55 % | 6449 | 35.0 | 112 | 196 |
| tldraw | cs-aot-ws | 0.9389 | 0.0204 | 2.17 % | 2671 | 14.5 | 310 | 308 |
| tldraw | cs-o-aot-ws | 0.0700 | 0.0009 | 1.24 % | 35819 | 194.6 | 117 | 162 |
| tldraw | cs-o-aot-ws-single | 0.4129 | 0.0029 | 0.71 % | 6074 | 33.0 | 105 | 106 |
| tldraw | cs-o-aot-srv | 0.0857 | 0.0031 | 3.58 % | 29277 | 159.1 | 121 | 164 |
| tldraw | cs-o-aot-srv-single | 0.4270 | 0.0033 | 0.77 % | 5873 | 31.9 | 108 | 118 |
| tldraw | cs-o-jit-ws | 0.2930 | 0.0020 | 0.69 % | 8560 | 46.5 | 154 | 183 |
| tldraw | cs-o-jit-ws-single | 1.314 | 0.0043 | 0.32 % | 1909 | 10.4 | 141 | 126 |
| tldraw | cs-o-jit-srv | 0.3127 | 0.0044 | 1.41 % | 8021 | 43.6 | 156 | 196 |
| tldraw | cs-o-jit-srv-single | 1.314 | 0.0091 | 0.69 % | 1908 | 10.4 | 143 | 136 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 11.66 [11.39, 11.84] | 11.99 [11.65, 12.31] | 11.51 [11.29, 11.76] | 11.93 [11.75, 12.58] | 11.87 [11.65, 12.09] | 11.79 [11.68, 11.95] |
| tsgo7-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-parse-single | 4.86 [4.76, 4.94] | 5.63 [5.51, 5.74] | 4.03 [3.96, 4.12] | 3.92 [3.86, 4.13] | 4.55 [4.46, 4.62] | 4.56 [4.52, 4.61] |
| tsrs-parse | 0.32 [0.31, 0.34] | 0.39 [0.38, 0.40] | 0.47 [0.45, 0.48] | 0.38 [0.37, 0.41] | 0.43 [0.42, 0.44] | 0.39 [0.39, 0.40] |
| tsrs-parse-single | 2.40 [2.35, 2.44] | 3.12 [3.05, 3.18] | 2.25 [2.21, 2.30] | 1.98 [1.95, 2.09] | 2.47 [2.43, 2.51] | 2.42 [2.40, 2.45] |
| cs-aot-ws | 8.22 [7.88, 8.55] | 7.11 [6.74, 7.25] | 4.34 [4.26, 4.50] | 6.13 [5.92, 6.49] | 5.97 [5.82, 6.09] | 6.22 [6.12, 6.32] |
| cs-o-aot-ws | 0.30 [0.29, 0.31] | 0.44 [0.42, 0.46] | 0.48 [0.47, 0.49] | 0.41 [0.41, 0.44] | 0.45 [0.44, 0.45] | 0.41 [0.41, 0.42] |
| cs-o-aot-ws-single | 2.65 [2.59, 2.69] | 3.24 [3.17, 3.30] | 2.41 [2.36, 2.46] | 2.21 [2.17, 2.33] | 2.63 [2.58, 2.67] | 2.60 [2.58, 2.64] |
| cs-o-aot-srv | 0.28 [0.27, 0.29] | 0.39 [0.38, 0.40] | 0.63 [0.59, 0.67] | 0.49 [0.48, 0.52] | 0.54 [0.52, 0.56] | 0.45 [0.44, 0.46] |
| cs-o-aot-srv-single | 2.64 [2.59, 2.68] | 3.27 [3.20, 3.33] | 2.51 [2.47, 2.57] | 2.30 [2.27, 2.43] | 2.72 [2.66, 2.77] | 2.67 [2.65, 2.70] |
| cs-o-jit-ws | 0.98 [0.95, 1.02] | 1.25 [1.22, 1.27] | 2.38 [2.34, 2.44] | 2.02 [1.99, 2.13] | 1.86 [1.83, 1.89] | 1.62 [1.60, 1.64] |
| cs-o-jit-ws-single | 4.59 [4.48, 4.67] | 7.47 [7.30, 8.00] | 8.12 [7.98, 8.30] | 8.15 [8.03, 8.60] | 8.35 [8.20, 8.49] | 7.17 [7.11, 7.30] |
| cs-o-jit-srv | 0.95 [0.92, 1.00] | 1.22 [1.20, 1.25] | 2.50 [2.46, 2.56] | 2.11 [2.07, 2.22] | 1.99 [1.94, 2.02] | 1.65 [1.63, 1.67] |
| cs-o-jit-srv-single | 4.59 [4.48, 4.68] | 7.32 [7.01, 7.47] | 8.22 [8.08, 8.40] | 8.20 [8.07, 8.65] | 8.36 [8.21, 8.52] | 7.17 [7.10, 7.26] |

## Table 2b. Median wall-clock relative to `tsrs-parse` (secondary reference), with 95 % confidence interval

Same method as Table 2, against tsrs (Rust port of TypeScript 7 / typescript-go), parallel. Secondary: never replaces the primary endpoint against typescript-go. tsrs parses JSDoc in .ts files lazily (TypeScript 7.1), typescript-go 7.0.2 eagerly, the C# tracks as tsc's host does (ParseForTypeErrors); see docs/references.md.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 36.41 [34.74, 37.17] | 30.71 [30.03, 31.49] | 24.63 [24.09, 25.19] | 31.30 [30.58, 31.59] | 27.75 [27.22, 28.19] | 29.90 [29.50, 30.17] |
| tsgo7-parse | 3.12 [2.98, 3.23] | 2.56 [2.51, 2.63] | 2.14 [2.08, 2.20] | 2.62 [2.47, 2.67] | 2.34 [2.28, 2.39] | 2.54 [2.49, 2.57] |
| tsgo7-parse-single | 15.17 [14.49, 15.48] | 14.41 [14.31, 14.67] | 8.63 [8.45, 8.82] | 10.28 [10.07, 10.38] | 10.64 [10.44, 10.79] | 11.56 [11.41, 11.65] |
| tsrs-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-parse-single | 7.50 [7.16, 7.66] | 7.99 [7.93, 8.12] | 4.82 [4.72, 4.93] | 5.20 [5.07, 5.26] | 5.78 [5.67, 5.86] | 6.13 [6.06, 6.18] |
| cs-aot-ws | 25.67 [24.23, 26.78] | 18.20 [17.31, 18.52] | 9.29 [9.09, 9.65] | 16.09 [15.39, 16.41] | 13.95 [13.61, 14.21] | 15.77 [15.47, 15.97] |
| cs-o-aot-ws | 0.95 [0.90, 0.98] | 1.12 [1.09, 1.17] | 1.03 [1.00, 1.06] | 1.08 [1.06, 1.10] | 1.04 [1.02, 1.06] | 1.04 [1.03, 1.06] |
| cs-o-aot-ws-single | 8.26 [7.89, 8.43] | 8.29 [8.23, 8.43] | 5.15 [5.03, 5.27] | 5.79 [5.66, 5.85] | 6.14 [6.02, 6.23] | 6.60 [6.52, 6.65] |
| cs-o-aot-srv | 0.88 [0.84, 0.92] | 1.00 [0.97, 1.02] | 1.34 [1.27, 1.43] | 1.29 [1.24, 1.32] | 1.27 [1.22, 1.31] | 1.14 [1.12, 1.16] |
| cs-o-aot-srv-single | 8.25 [7.88, 8.42] | 8.38 [8.32, 8.52] | 5.38 [5.28, 5.50] | 6.05 [5.89, 6.12] | 6.35 [6.22, 6.45] | 6.77 [6.69, 6.83] |
| cs-o-jit-ws | 3.07 [2.92, 3.20] | 3.20 [3.17, 3.26] | 5.10 [4.99, 5.23] | 5.30 [5.16, 5.35] | 4.35 [4.27, 4.42] | 4.10 [4.04, 4.14] |
| cs-o-jit-ws-single | 14.34 [13.68, 14.67] | 19.12 [18.88, 20.53] | 17.37 [17.02, 17.76] | 21.39 [20.91, 21.59] | 19.52 [19.16, 19.80] | 18.19 [17.97, 18.46] |
| cs-o-jit-srv | 2.97 [2.83, 3.12] | 3.13 [3.10, 3.20] | 5.36 [5.25, 5.48] | 5.52 [5.39, 5.59] | 4.65 [4.54, 4.73] | 4.18 [4.13, 4.23] |
| cs-o-jit-srv-single | 14.33 [13.68, 14.68] | 18.74 [17.99, 19.09] | 17.59 [17.23, 18.01] | 21.51 [21.04, 21.74] | 19.53 [19.18, 19.88] | 18.18 [17.93, 18.34] |

## Table 3. Variance gate (MAD/median < 3 %)

Binding rows (rig check): tsc6-parse, tsgo7-parse-single and the primary single-threaded C# configuration of each track. Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md, B11 as amended by prereg/v4).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | rig check | 0.49 % | 1.86 % | 0.95 % | 0.46 % | 0.73 % | 1.86 % |
| tsgo7-parse | reported | 2.03 % | 2.16 % | 2.35 % | 4.17 % | 2.40 % | 4.17 % |
| tsgo7-parse-single | rig check | 0.29 % | 0.43 % | 0.70 % | 0.27 % | 0.46 % | 0.70 % |
| tsrs-parse | reported | 5.59 % | 1.79 % | 2.35 % | 1.49 % | 1.93 % | 5.59 % |
| tsrs-parse-single | reported | 0.33 % | 0.48 % | 0.71 % | 0.77 % | 0.55 % | 0.77 % |
| cs-aot-ws | reported | 3.90 % | 1.95 % | 1.91 % | 2.78 % | 2.17 % | 3.90 % |
| cs-o-aot-ws | reported | 1.94 % | 4.01 % | 1.93 % | 1.48 % | 1.24 % | 4.01 % |
| cs-o-aot-ws-single | rig check | 0.26 % | 0.42 % | 0.85 % | 0.61 % | 0.71 % | 0.85 % |
| cs-o-aot-srv | reported | 2.81 % | 2.35 % | 5.97 % | 3.34 % | 3.58 % | 5.97 % |
| cs-o-aot-srv-single | reported | 0.24 % | 0.39 % | 0.55 % | 1.06 % | 0.77 % | 1.06 % |
| cs-o-jit-ws | reported | 2.64 % | 1.04 % | 1.13 % | 1.22 % | 0.69 % | 2.64 % |
| cs-o-jit-ws-single | reported | 0.86 % | 2.44 % | 0.48 % | 0.46 % | 0.32 % | 2.44 % |
| cs-o-jit-srv | reported | 2.09 % | 1.22 % | 0.74 % | 0.77 % | 1.41 % | 2.09 % |
| cs-o-jit-srv-single | reported | 0.88 % | 2.67 % | 0.78 % | 0.35 % | 0.69 % | 2.67 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsc6-parse | 0.2648 | 0.47 % | 21.2 % | 97 |
| tsgo7-parse | 0.0205 | 1.30 % | 18.8 % | 9 |
| tsgo7-parse-single | 0.0212 | 1.18 % | 4.8 % | 9 |
| tsrs-parse | 0.0189 | 1.30 % | 37.2 % | 8 |
| tsrs-parse-single | 0.0167 | 0.47 % | 6.8 % | 7 |
| cs-aot-ws | 0.0229 | 0.85 % | 4.9 % | 10 |
| cs-o-aot-ws | 0.0231 | 0.38 % | 44.3 % | 10 |
| cs-o-aot-ws-single | 0.0232 | 1.31 % | 8.9 % | 10 |
| cs-o-aot-srv | 0.0281 | 2.53 % | 41.2 % | 12 |
| cs-o-aot-srv-single | 0.0303 | 6.58 % | 11.1 % | 12 |
| cs-o-jit-ws | 0.0765 | 1.15 % | 29.5 % | 27 |
| cs-o-jit-ws-single | 0.0765 | 0.58 % | 8.7 % | 27 |
| cs-o-jit-srv | 0.0833 | 2.78 % | 30.6 % | 28 |
| cs-o-jit-srv-single | 0.0812 | 0.50 % | 9.1 % | 28 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-aot-ws | 3278347008 | 390/349/7 | 6845.8 |
| vscode | cs-o-aot-ws | 61019448 | 7/5/3 | 34.0 |
| vscode | cs-o-aot-ws-single | 36055312 | 5/3/1 | 21.9 |
| vscode | cs-o-aot-srv | 65142504 | 5/4/3 | 5.9 |
| vscode | cs-o-aot-srv-single | 36053648 | 3/1/0 | 7.8 |
| vscode | cs-o-jit-ws | 62138648 | 7/5/3 | 38.7 |
| vscode | cs-o-jit-ws-single | 36960320 | 5/3/1 | 19.8 |
| vscode | cs-o-jit-srv | 61063032 | 6/5/4 | 9.0 |
| vscode | cs-o-jit-srv-single | 36936464 | 3/1/0 | 7.9 |
| sentry | cs-aot-ws | 1094933240 | 139/137/6 | 2440.1 |
| sentry | cs-o-aot-ws | 45023672 | 5/5/3 | 24.5 |
| sentry | cs-o-aot-ws-single | 20364736 | 2/1/0 | 9.4 |
| sentry | cs-o-aot-srv | 45648560 | 4/3/3 | 5.3 |
| sentry | cs-o-aot-srv-single | 20364400 | 3/1/0 | 7.6 |
| sentry | cs-o-jit-ws | 45477080 | 5/5/3 | 25.9 |
| sentry | cs-o-jit-ws-single | 21199872 | 2/1/0 | 8.8 |
| sentry | cs-o-jit-srv | 44500344 | 6/5/4 | 9.1 |
| sentry | cs-o-jit-srv-single | 21202536 | 3/1/0 | 7.2 |
| bluesky | cs-aot-ws | 185048600 | 25/24/3 | 357.5 |
| bluesky | cs-o-aot-ws | 23098216 | 2/2/2 | 1.7 |
| bluesky | cs-o-aot-ws-single | 4673048 | 0/0/0 | 0.0 |
| bluesky | cs-o-aot-srv | 26330416 | 7/6/5 | 9.8 |
| bluesky | cs-o-aot-srv-single | 4673744 | 1/0/0 | 2.5 |
| bluesky | cs-o-jit-ws | 24011752 | 2/2/2 | 3.0 |
| bluesky | cs-o-jit-ws-single | 5391528 | 0/0/0 | 0.0 |
| bluesky | cs-o-jit-srv | 24481360 | 6/5/4 | 11.3 |
| bluesky | cs-o-jit-srv-single | 5417712 | 1/0/0 | 1.9 |
| playwright | cs-aot-ws | 378865488 | 47/46/4 | 713.8 |
| playwright | cs-o-aot-ws | 26449648 | 2/2/2 | 1.7 |
| playwright | cs-o-aot-ws-single | 4427976 | 0/0/0 | 0.0 |
| playwright | cs-o-aot-srv | 34595512 | 7/6/5 | 9.2 |
| playwright | cs-o-aot-srv-single | 4428672 | 1/0/0 | 1.9 |
| playwright | cs-o-jit-ws | 27248440 | 2/2/2 | 3.5 |
| playwright | cs-o-jit-ws-single | 5142064 | 0/0/0 | 0.0 |
| playwright | cs-o-jit-srv | 26624800 | 6/5/4 | 12.3 |
| playwright | cs-o-jit-srv-single | 5143624 | 1/0/0 | 1.8 |
| tldraw | cs-aot-ws | 356389840 | 47/43/4 | 742.1 |
| tldraw | cs-o-aot-ws | 28673520 | 2/2/2 | 1.9 |
| tldraw | cs-o-aot-ws-single | 7361656 | 0/0/0 | 0.0 |
| tldraw | cs-o-aot-srv | 34822640 | 6/5/4 | 9.4 |
| tldraw | cs-o-aot-srv-single | 7362688 | 2/1/0 | 4.8 |
| tldraw | cs-o-jit-ws | 29858464 | 2/2/2 | 2.9 |
| tldraw | cs-o-jit-ws-single | 8093256 | 0/0/0 | 0.0 |
| tldraw | cs-o-jit-srv | 30840248 | 7/6/5 | 14.2 |
| tldraw | cs-o-jit-srv-single | 8095376 | 2/1/0 | 5.1 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (70 files).
