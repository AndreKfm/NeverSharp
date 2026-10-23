# Parse-only benchmark, run 2026-10-23T11-50-41-097Z

**Status: protocol result.** All rig checks passed.

Label: track-o-additional-run-after-O11

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 6.5 % (median of 1 s windows: 4.8, 3.4, 5.2, 9.7, 10, 26.2, 10.2, 7.8, 3.7, 3.6) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit cfbabcfd08, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 6.5 % (range 3.4-26.2 %) | ok |
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
| vscode | tsc6-parse | 12.08 | 0.0535 | 0.44 % | 970 | 11.7 | 3192 | 3224 |
| vscode | tsgo7-parse | 1.038 | 0.0255 | 2.46 % | 11291 | 136.0 | 2187 | 2219 |
| vscode | tsgo7-parse-single | 5.019 | 0.0251 | 0.50 % | 2336 | 28.1 | 2121 | 2172 |
| vscode | tsrs-parse | 0.3281 | 0.0177 | 5.40 % | 35735 | 430.3 | 955 | 1974 |
| vscode | tsrs-parse-single | 2.463 | 0.0091 | 0.37 % | 4760 | 57.3 | 956 | 1253 |
| vscode | cs-aot-ws | 8.405 | 0.3123 | 3.72 % | 1395 | 16.8 | 2637 | 2647 |
| vscode | cs-o-aot-ws | 0.3745 | 0.0072 | 1.93 % | 31308 | 377.0 | 881 | 1023 |
| vscode | cs-o-aot-ws-single | 3.637 | 0.0048 | 0.13 % | 3224 | 38.8 | 853 | 870 |
| vscode | cs-o-aot-srv | 0.3544 | 0.0092 | 2.59 % | 33079 | 398.3 | 887 | 1071 |
| vscode | cs-o-aot-srv-single | 3.637 | 0.0104 | 0.29 % | 3223 | 38.8 | 859 | 885 |
| vscode | cs-o-jit-ws | 1.434 | 0.0538 | 3.75 % | 8174 | 98.4 | 918 | 1043 |
| vscode | cs-o-jit-ws-single | 5.601 | 0.0557 | 0.99 % | 2093 | 25.2 | 896 | 893 |
| vscode | cs-o-jit-srv | 1.496 | 0.0571 | 3.81 % | 7837 | 94.4 | 925 | 1057 |
| vscode | cs-o-jit-srv-single | 5.551 | 0.0587 | 1.06 % | 2112 | 25.4 | 897 | 906 |
| sentry | tsc6-parse | 4.772 | 0.0837 | 1.75 % | 1872 | 9.6 | 1216 | 1208 |
| sentry | tsgo7-parse | 0.4136 | 0.0070 | 1.69 % | 21594 | 111.1 | 804 | 823 |
| sentry | tsgo7-parse-single | 2.286 | 0.0078 | 0.34 % | 3907 | 20.1 | 766 | 776 |
| sentry | tsrs-parse | 0.1601 | 0.0028 | 1.78 % | 55803 | 287.0 | 342 | 660 |
| sentry | tsrs-parse-single | 1.261 | 0.0080 | 0.63 % | 7082 | 36.4 | 346 | 634 |
| sentry | cs-aot-ws | 2.930 | 0.0509 | 1.74 % | 3048 | 15.7 | 914 | 911 |
| sentry | cs-o-aot-ws | 0.1922 | 0.0070 | 3.63 % | 46471 | 239.0 | 321 | 421 |
| sentry | cs-o-aot-ws-single | 1.629 | 0.0055 | 0.34 % | 5484 | 28.2 | 304 | 305 |
| sentry | cs-o-aot-srv | 0.1814 | 0.0060 | 3.29 % | 49253 | 253.3 | 321 | 440 |
| sentry | cs-o-aot-srv-single | 1.626 | 0.0079 | 0.49 % | 5492 | 28.2 | 305 | 314 |
| sentry | cs-o-jit-ws | 0.5959 | 0.0040 | 0.67 % | 14988 | 77.1 | 357 | 462 |
| sentry | cs-o-jit-ws-single | 3.748 | 0.1057 | 2.82 % | 2383 | 12.3 | 342 | 325 |
| sentry | cs-o-jit-srv | 0.5763 | 0.0059 | 1.03 % | 15500 | 79.7 | 356 | 452 |
| sentry | cs-o-jit-srv-single | 3.432 | 0.0567 | 1.65 % | 2603 | 13.4 | 344 | 334 |
| bluesky | tsc6-parse | 1.247 | 0.0095 | 0.76 % | 1353 | 5.8 | 368 | 340 |
| bluesky | tsgo7-parse | 0.1064 | 0.0019 | 1.78 % | 15870 | 67.6 | 159 | 170 |
| bluesky | tsgo7-parse-single | 0.4334 | 0.0032 | 0.75 % | 3895 | 16.6 | 143 | 151 |
| bluesky | tsrs-parse | 0.0472 | 0.0009 | 1.87 % | 35744 | 152.3 | 70 | 212 |
| bluesky | tsrs-parse-single | 0.2443 | 0.0026 | 1.06 % | 6909 | 29.4 | 61 | 109 |
| bluesky | cs-aot-ws | 0.4867 | 0.0088 | 1.80 % | 3468 | 14.8 | 169 | 163 |
| bluesky | cs-o-aot-ws | 0.0569 | 0.0013 | 2.29 % | 29651 | 126.4 | 69 | 86 |
| bluesky | cs-o-aot-ws-single | 0.3082 | 0.0023 | 0.74 % | 5476 | 23.3 | 61 | 58 |
| bluesky | cs-o-aot-srv | 0.0674 | 0.0017 | 2.54 % | 25037 | 106.7 | 74 | 103 |
| bluesky | cs-o-aot-srv-single | 0.3117 | 0.0022 | 0.71 % | 5415 | 23.1 | 64 | 70 |
| bluesky | cs-o-jit-ws | 0.2711 | 0.0042 | 1.55 % | 6226 | 26.5 | 106 | 109 |
| bluesky | cs-o-jit-ws-single | 1.088 | 0.0042 | 0.39 % | 1552 | 6.6 | 97 | 77 |
| bluesky | cs-o-jit-srv | 0.2843 | 0.0029 | 1.02 % | 5936 | 25.3 | 110 | 122 |
| bluesky | cs-o-jit-srv-single | 1.092 | 0.0045 | 0.41 % | 1546 | 6.6 | 100 | 89 |
| playwright | tsc6-parse | 1.802 | 0.0128 | 0.71 % | 785 | 9.6 | 558 | 535 |
| playwright | tsgo7-parse | 0.1452 | 0.0032 | 2.21 % | 9746 | 118.7 | 270 | 284 |
| playwright | tsgo7-parse-single | 0.5958 | 0.0025 | 0.42 % | 2375 | 28.9 | 255 | 264 |
| playwright | tsrs-parse | 0.0588 | 0.0020 | 3.42 % | 24083 | 293.3 | 124 | 373 |
| playwright | tsrs-parse-single | 0.2994 | 0.0019 | 0.62 % | 4726 | 57.6 | 115 | 203 |
| playwright | cs-aot-ws | 0.9085 | 0.0259 | 2.85 % | 1557 | 19.0 | 322 | 318 |
| playwright | cs-o-aot-ws | 0.0678 | 0.0005 | 0.79 % | 20862 | 254.1 | 126 | 189 |
| playwright | cs-o-aot-ws-single | 0.4348 | 0.0014 | 0.33 % | 3254 | 39.6 | 112 | 116 |
| playwright | cs-o-aot-srv | 0.0804 | 0.0014 | 1.72 % | 17604 | 214.4 | 132 | 202 |
| playwright | cs-o-aot-srv-single | 0.4422 | 0.0026 | 0.59 % | 3200 | 39.0 | 115 | 128 |
| playwright | cs-o-jit-ws | 0.3401 | 0.0038 | 1.13 % | 4161 | 50.7 | 163 | 212 |
| playwright | cs-o-jit-ws-single | 1.776 | 0.0275 | 1.55 % | 797 | 9.7 | 149 | 135 |
| playwright | cs-o-jit-srv | 0.3564 | 0.0048 | 1.35 % | 3970 | 48.4 | 169 | 225 |
| playwright | cs-o-jit-srv-single | 1.779 | 0.0188 | 1.05 % | 795 | 9.7 | 151 | 147 |
| tldraw | tsc6-parse | 1.883 | 0.0160 | 0.85 % | 1332 | 7.2 | 543 | 520 |
| tldraw | tsgo7-parse | 0.1576 | 0.0032 | 2.02 % | 15911 | 86.4 | 273 | 285 |
| tldraw | tsgo7-parse-single | 0.7198 | 0.0048 | 0.67 % | 3484 | 18.9 | 257 | 268 |
| tldraw | tsrs-parse | 0.0708 | 0.0013 | 1.79 % | 35417 | 192.4 | 121 | 338 |
| tldraw | tsrs-parse-single | 0.3944 | 0.0046 | 1.16 % | 6359 | 34.5 | 112 | 196 |
| tldraw | cs-aot-ws | 0.9521 | 0.0171 | 1.80 % | 2634 | 14.3 | 311 | 307 |
| tldraw | cs-o-aot-ws | 0.0764 | 0.0010 | 1.30 % | 32834 | 178.4 | 116 | 161 |
| tldraw | cs-o-aot-ws-single | 0.5189 | 0.0033 | 0.64 % | 4834 | 26.3 | 105 | 106 |
| tldraw | cs-o-aot-srv | 0.0922 | 0.0027 | 2.92 % | 27201 | 147.8 | 120 | 166 |
| tldraw | cs-o-aot-srv-single | 0.5245 | 0.0042 | 0.80 % | 4781 | 26.0 | 108 | 117 |
| tldraw | cs-o-jit-ws | 0.3332 | 0.0035 | 1.06 % | 7527 | 40.9 | 154 | 185 |
| tldraw | cs-o-jit-ws-single | 1.712 | 0.0212 | 1.24 % | 1465 | 8.0 | 143 | 126 |
| tldraw | cs-o-jit-srv | 0.3504 | 0.0038 | 1.10 % | 7157 | 38.9 | 156 | 197 |
| tldraw | cs-o-jit-srv-single | 1.746 | 0.0199 | 1.14 % | 1436 | 7.8 | 145 | 137 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 11.64 [11.43, 11.87] | 11.54 [11.30, 12.03] | 11.73 [11.53, 11.92] | 12.41 [12.24, 12.65] | 11.95 [11.63, 12.01] | 11.85 [11.74, 11.96] |
| tsgo7-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-parse-single | 4.83 [4.75, 4.93] | 5.53 [5.45, 5.63] | 4.07 [4.00, 4.14] | 4.10 [4.05, 4.18] | 4.57 [4.46, 4.60] | 4.59 [4.55, 4.62] |
| tsrs-parse | 0.32 [0.31, 0.33] | 0.39 [0.38, 0.39] | 0.44 [0.44, 0.46] | 0.40 [0.39, 0.42] | 0.45 [0.44, 0.46] | 0.40 [0.39, 0.40] |
| tsrs-parse-single | 2.37 [2.33, 2.42] | 3.05 [3.01, 3.11] | 2.30 [2.26, 2.34] | 2.06 [2.03, 2.10] | 2.50 [2.44, 2.53] | 2.44 [2.41, 2.45] |
| cs-aot-ws | 8.09 [7.76, 8.35] | 7.08 [6.90, 7.24] | 4.58 [4.46, 4.65] | 6.26 [5.99, 6.42] | 6.04 [5.85, 6.12] | 6.30 [6.20, 6.36] |
| cs-o-aot-ws | 0.36 [0.35, 0.37] | 0.46 [0.45, 0.49] | 0.54 [0.52, 0.54] | 0.47 [0.46, 0.48] | 0.48 [0.47, 0.49] | 0.46 [0.45, 0.46] |
| cs-o-aot-ws-single | 3.50 [3.44, 3.57] | 3.94 [3.88, 4.01] | 2.90 [2.85, 2.94] | 2.99 [2.96, 3.05] | 3.29 [3.22, 3.32] | 3.30 [3.28, 3.33] |
| cs-o-aot-srv | 0.34 [0.33, 0.35] | 0.44 [0.43, 0.46] | 0.63 [0.62, 0.67] | 0.55 [0.54, 0.58] | 0.58 [0.56, 0.62] | 0.50 [0.49, 0.51] |
| cs-o-aot-srv-single | 3.50 [3.44, 3.57] | 3.93 [3.88, 4.01] | 2.93 [2.88, 2.98] | 3.05 [3.00, 3.10] | 3.33 [3.25, 3.35] | 3.33 [3.30, 3.35] |
| cs-o-jit-ws | 1.38 [1.34, 1.45] | 1.44 [1.42, 1.47] | 2.55 [2.49, 2.60] | 2.34 [2.31, 2.41] | 2.11 [2.07, 2.14] | 1.91 [1.89, 1.93] |
| cs-o-jit-ws-single | 5.39 [5.28, 5.51] | 9.06 [8.78, 9.23] | 10.23 [10.06, 10.38] | 12.23 [12.01, 12.57] | 10.86 [10.60, 11.07] | 9.21 [9.11, 9.29] |
| cs-o-jit-srv | 1.44 [1.39, 1.48] | 1.39 [1.37, 1.43] | 2.67 [2.62, 2.72] | 2.45 [2.41, 2.51] | 2.22 [2.17, 2.25] | 1.96 [1.94, 1.98] |
| cs-o-jit-srv-single | 5.35 [5.25, 5.47] | 8.30 [8.05, 8.45] | 10.26 [10.10, 10.43] | 12.25 [12.07, 12.52] | 11.08 [10.76, 11.16] | 9.08 [8.99, 9.15] |

## Table 2b. Median wall-clock relative to `tsrs-parse` (secondary reference), with 95 % confidence interval

Same method as Table 2, against tsrs (Rust port of TypeScript 7 / typescript-go), parallel. Secondary: never replaces the primary endpoint against typescript-go. tsrs parses JSDoc in .ts files lazily (TypeScript 7.1), typescript-go 7.0.2 eagerly, the C# tracks as tsc's host does (ParseForTypeErrors); see docs/references.md.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 36.83 [34.94, 37.68] | 29.82 [29.39, 30.96] | 26.41 [25.61, 26.79] | 30.67 [29.77, 31.38] | 26.59 [26.07, 27.01] | 29.84 [29.40, 30.16] |
| tsgo7-parse | 3.16 [2.99, 3.26] | 2.58 [2.54, 2.64] | 2.25 [2.18, 2.30] | 2.47 [2.38, 2.54] | 2.23 [2.19, 2.29] | 2.52 [2.48, 2.55] |
| tsgo7-parse-single | 15.30 [14.51, 15.63] | 14.28 [14.17, 14.57] | 9.18 [8.90, 9.29] | 10.14 [9.85, 10.38] | 10.16 [9.99, 10.34] | 11.56 [11.39, 11.66] |
| tsrs-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-parse-single | 7.51 [7.12, 7.67] | 7.88 [7.82, 8.04] | 5.17 [5.02, 5.26] | 5.10 [4.94, 5.21] | 5.57 [5.47, 5.67] | 6.13 [6.04, 6.19] |
| cs-aot-ws | 25.62 [24.04, 26.52] | 18.31 [17.91, 18.68] | 10.31 [9.97, 10.46] | 15.46 [14.70, 15.90] | 13.45 [13.09, 13.73] | 15.87 [15.55, 16.02] |
| cs-o-aot-ws | 1.14 [1.08, 1.17] | 1.20 [1.18, 1.26] | 1.21 [1.16, 1.22] | 1.15 [1.12, 1.18] | 1.08 [1.06, 1.10] | 1.16 [1.14, 1.17] |
| cs-o-aot-ws-single | 11.09 [10.52, 11.32] | 10.18 [10.09, 10.38] | 6.53 [6.33, 6.61] | 7.40 [7.19, 7.57] | 7.33 [7.20, 7.47] | 8.32 [8.20, 8.39] |
| cs-o-aot-srv | 1.08 [1.02, 1.12] | 1.13 [1.11, 1.18] | 1.43 [1.38, 1.51] | 1.37 [1.32, 1.42] | 1.30 [1.26, 1.39] | 1.25 [1.23, 1.28] |
| cs-o-aot-srv-single | 11.09 [10.52, 11.32] | 10.16 [10.08, 10.37] | 6.60 [6.41, 6.69] | 7.53 [7.30, 7.70] | 7.41 [7.27, 7.54] | 8.39 [8.26, 8.45] |
| cs-o-jit-ws | 4.37 [4.12, 4.59] | 3.72 [3.69, 3.80] | 5.74 [5.56, 5.84] | 5.79 [5.62, 5.96] | 4.71 [4.62, 4.80] | 4.80 [4.73, 4.86] |
| cs-o-jit-ws-single | 17.07 [16.18, 17.49] | 23.42 [22.75, 23.87] | 23.03 [22.33, 23.32] | 30.22 [29.26, 31.11] | 24.18 [23.74, 24.84] | 23.21 [22.83, 23.43] |
| cs-o-jit-srv | 4.56 [4.26, 4.68] | 3.60 [3.57, 3.68] | 6.02 [5.83, 6.10] | 6.07 [5.86, 6.22] | 4.95 [4.85, 5.05] | 4.95 [4.86, 4.99] |
| cs-o-jit-srv-single | 16.92 [16.05, 17.36] | 21.44 [20.86, 21.84] | 23.12 [22.44, 23.43] | 30.28 [29.37, 31.07] | 24.66 [24.13, 25.08] | 22.87 [22.51, 23.06] |

## Table 3. Variance gate (MAD/median < 3 %)

Binding rows (rig check): tsc6-parse, tsgo7-parse-single and the primary single-threaded C# configuration of each track. Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md, B11 as amended by prereg/v4).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | rig check | 0.44 % | 1.75 % | 0.76 % | 0.71 % | 0.85 % | 1.75 % |
| tsgo7-parse | reported | 2.46 % | 1.69 % | 1.78 % | 2.21 % | 2.02 % | 2.46 % |
| tsgo7-parse-single | rig check | 0.50 % | 0.34 % | 0.75 % | 0.42 % | 0.67 % | 0.75 % |
| tsrs-parse | reported | 5.40 % | 1.78 % | 1.87 % | 3.42 % | 1.79 % | 5.40 % |
| tsrs-parse-single | reported | 0.37 % | 0.63 % | 1.06 % | 0.62 % | 1.16 % | 1.16 % |
| cs-aot-ws | reported | 3.72 % | 1.74 % | 1.80 % | 2.85 % | 1.80 % | 3.72 % |
| cs-o-aot-ws | reported | 1.93 % | 3.63 % | 2.29 % | 0.79 % | 1.30 % | 3.63 % |
| cs-o-aot-ws-single | rig check | 0.13 % | 0.34 % | 0.74 % | 0.33 % | 0.64 % | 0.74 % |
| cs-o-aot-srv | reported | 2.59 % | 3.29 % | 2.54 % | 1.72 % | 2.92 % | 3.29 % |
| cs-o-aot-srv-single | reported | 0.29 % | 0.49 % | 0.71 % | 0.59 % | 0.80 % | 0.80 % |
| cs-o-jit-ws | reported | 3.75 % | 0.67 % | 1.55 % | 1.13 % | 1.06 % | 3.75 % |
| cs-o-jit-ws-single | reported | 0.99 % | 2.82 % | 0.39 % | 1.55 % | 1.24 % | 2.82 % |
| cs-o-jit-srv | reported | 3.81 % | 1.03 % | 1.02 % | 1.35 % | 1.10 % | 3.81 % |
| cs-o-jit-srv-single | reported | 1.06 % | 1.65 % | 0.41 % | 1.05 % | 1.14 % | 1.65 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsc6-parse | 0.2647 | 0.58 % | 21.2 % | 97 |
| tsgo7-parse | 0.0215 | 1.19 % | 20.2 % | 9 |
| tsgo7-parse-single | 0.0215 | 2.03 % | 5.0 % | 9 |
| tsrs-parse | 0.0188 | 2.48 % | 39.8 % | 8 |
| tsrs-parse-single | 0.0168 | 1.58 % | 6.9 % | 7 |
| cs-aot-ws | 0.0231 | 0.79 % | 4.7 % | 10 |
| cs-o-aot-ws | 0.0229 | 0.87 % | 40.2 % | 10 |
| cs-o-aot-ws-single | 0.0238 | 2.32 % | 7.7 % | 10 |
| cs-o-aot-srv | 0.0281 | 1.59 % | 41.6 % | 12 |
| cs-o-aot-srv-single | 0.0281 | 2.55 % | 9.0 % | 12 |
| cs-o-jit-ws | 0.0759 | 0.60 % | 28.0 % | 27 |
| cs-o-jit-ws-single | 0.0772 | 1.38 % | 7.1 % | 27 |
| cs-o-jit-srv | 0.0813 | 1.05 % | 28.6 % | 28 |
| cs-o-jit-srv-single | 0.0809 | 1.01 % | 7.4 % | 28 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-aot-ws | 3278081816 | 390/344/7 | 7194.7 |
| vscode | cs-o-aot-ws | 59947528 | 6/4/3 | 22.9 |
| vscode | cs-o-aot-ws-single | 36056800 | 5/3/1 | 20.8 |
| vscode | cs-o-aot-srv | 60130664 | 5/4/3 | 5.8 |
| vscode | cs-o-aot-srv-single | 36050400 | 3/1/0 | 8.0 |
| vscode | cs-o-jit-ws | 61836280 | 7/5/3 | 44.2 |
| vscode | cs-o-jit-ws-single | 36959912 | 5/3/1 | 20.2 |
| vscode | cs-o-jit-srv | 62243896 | 7/6/5 | 11.0 |
| vscode | cs-o-jit-srv-single | 36957096 | 3/1/0 | 7.9 |
| sentry | cs-aot-ws | 1094989608 | 139/138/6 | 2475.2 |
| sentry | cs-o-aot-ws | 43174200 | 5/5/3 | 24.7 |
| sentry | cs-o-aot-ws-single | 20361488 | 2/1/0 | 9.2 |
| sentry | cs-o-aot-srv | 49012352 | 5/4/4 | 6.2 |
| sentry | cs-o-aot-srv-single | 20361152 | 3/1/0 | 7.0 |
| sentry | cs-o-jit-ws | 45173040 | 5/5/3 | 26.3 |
| sentry | cs-o-jit-ws-single | 21195880 | 2/1/0 | 8.8 |
| sentry | cs-o-jit-srv | 44408776 | 6/5/4 | 8.7 |
| sentry | cs-o-jit-srv-single | 21223168 | 3/1/0 | 7.2 |
| bluesky | cs-aot-ws | 185057672 | 25/24/3 | 362.1 |
| bluesky | cs-o-aot-ws | 23135136 | 2/2/2 | 1.7 |
| bluesky | cs-o-aot-ws-single | 4669800 | 0/0/0 | 0.0 |
| bluesky | cs-o-aot-srv | 29335800 | 7/6/5 | 9.4 |
| bluesky | cs-o-aot-srv-single | 4670496 | 1/0/0 | 2.0 |
| bluesky | cs-o-jit-ws | 24706312 | 2/2/2 | 3.3 |
| bluesky | cs-o-jit-ws-single | 5387536 | 0/0/0 | 0.0 |
| bluesky | cs-o-jit-srv | 24281280 | 6/5/4 | 11.3 |
| bluesky | cs-o-jit-srv-single | 5389096 | 1/0/0 | 1.9 |
| playwright | cs-aot-ws | 378739328 | 47/46/4 | 733.8 |
| playwright | cs-o-aot-ws | 26392880 | 2/2/2 | 1.9 |
| playwright | cs-o-aot-ws-single | 4424728 | 0/0/0 | 0.0 |
| playwright | cs-o-aot-srv | 33570008 | 7/6/5 | 9.5 |
| playwright | cs-o-aot-srv-single | 4425424 | 1/0/0 | 1.9 |
| playwright | cs-o-jit-ws | 26250648 | 2/2/2 | 3.3 |
| playwright | cs-o-jit-ws-single | 5138072 | 0/0/0 | 0.0 |
| playwright | cs-o-jit-srv | 27678000 | 7/6/5 | 13.5 |
| playwright | cs-o-jit-srv-single | 5139632 | 1/0/0 | 1.8 |
| tldraw | cs-aot-ws | 356334496 | 45/43/4 | 692.7 |
| tldraw | cs-o-aot-ws | 28784720 | 2/2/2 | 1.9 |
| tldraw | cs-o-aot-ws-single | 7358408 | 0/0/0 | 0.0 |
| tldraw | cs-o-aot-srv | 31939704 | 6/5/4 | 11.5 |
| tldraw | cs-o-aot-srv-single | 7359440 | 2/1/0 | 4.7 |
| tldraw | cs-o-jit-ws | 30049696 | 2/2/2 | 2.8 |
| tldraw | cs-o-jit-ws-single | 8089264 | 0/0/0 | 0.0 |
| tldraw | cs-o-jit-srv | 30249416 | 7/6/5 | 16.1 |
| tldraw | cs-o-jit-srv-single | 8091384 | 2/1/0 | 4.8 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (70 files).
