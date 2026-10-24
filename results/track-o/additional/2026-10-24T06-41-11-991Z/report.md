# Parse-only benchmark, run 2026-10-24T06-41-11-991Z

**Status: protocol result.** All rig checks passed.

Label: track-o-additional-run-after-O29

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 99 % of 4150 MHz, idle before run 7.6 % (median of 1 s windows: 8.1, 6.1, 6.7, 35.2, 11.3, 13.1, 9.5, 7, 6.5, 6.4) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 95b038e428, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 99 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 7.6 % (range 6.1-35.2 %) | ok |
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
| vscode | tsc6-parse | 12.04 | 0.1009 | 0.84 % | 974 | 11.7 | 3192 | 3223 |
| vscode | tsgo7-parse | 1.036 | 0.0116 | 1.12 % | 11316 | 136.3 | 2176 | 2219 |
| vscode | tsgo7-parse-single | 4.979 | 0.0110 | 0.22 % | 2355 | 28.4 | 2116 | 2169 |
| vscode | tsrs-parse | 0.3206 | 0.0119 | 3.71 % | 36571 | 440.4 | 956 | 1993 |
| vscode | tsrs-parse-single | 2.458 | 0.0044 | 0.18 % | 4769 | 57.4 | 940 | 1253 |
| vscode | cs-aot-ws | 8.974 | 0.2285 | 2.55 % | 1306 | 15.7 | 2641 | 2645 |
| vscode | cs-o-aot-ws | 0.2954 | 0.0075 | 2.55 % | 39690 | 477.9 | 881 | 1023 |
| vscode | cs-o-aot-ws-single | 2.523 | 0.0100 | 0.40 % | 4647 | 56.0 | 854 | 871 |
| vscode | cs-o-aot-srv | 0.2749 | 0.0080 | 2.90 % | 42642 | 513.5 | 897 | 1095 |
| vscode | cs-o-aot-srv-single | 2.537 | 0.0132 | 0.52 % | 4621 | 55.6 | 860 | 886 |
| vscode | cs-o-jit-ws | 0.9946 | 0.0352 | 3.54 % | 11788 | 141.9 | 921 | 1050 |
| vscode | cs-o-jit-ws-single | 4.714 | 0.0719 | 1.53 % | 2487 | 29.9 | 896 | 894 |
| vscode | cs-o-jit-srv | 0.9387 | 0.0077 | 0.82 % | 12489 | 150.4 | 923 | 1053 |
| vscode | cs-o-jit-srv-single | 4.758 | 0.1168 | 2.45 % | 2464 | 29.7 | 897 | 907 |
| sentry | tsc6-parse | 4.871 | 0.1185 | 2.43 % | 1834 | 9.4 | 1216 | 1208 |
| sentry | tsgo7-parse | 0.4098 | 0.0076 | 1.85 % | 21795 | 112.1 | 804 | 823 |
| sentry | tsgo7-parse-single | 2.300 | 0.0046 | 0.20 % | 3883 | 20.0 | 761 | 775 |
| sentry | tsrs-parse | 0.1601 | 0.0033 | 2.08 % | 55790 | 286.9 | 342 | 658 |
| sentry | tsrs-parse-single | 1.268 | 0.0073 | 0.57 % | 7043 | 36.2 | 329 | 634 |
| sentry | cs-aot-ws | 2.907 | 0.0788 | 2.71 % | 3073 | 15.8 | 914 | 912 |
| sentry | cs-o-aot-ws | 0.1736 | 0.0017 | 0.97 % | 51460 | 264.7 | 322 | 424 |
| sentry | cs-o-aot-ws-single | 1.260 | 0.0052 | 0.41 % | 7088 | 36.5 | 304 | 305 |
| sentry | cs-o-aot-srv | 0.1565 | 0.0020 | 1.30 % | 57056 | 293.5 | 323 | 441 |
| sentry | cs-o-aot-srv-single | 1.259 | 0.0048 | 0.38 % | 7094 | 36.5 | 305 | 314 |
| sentry | cs-o-jit-ws | 0.4976 | 0.0030 | 0.61 % | 17949 | 92.3 | 357 | 460 |
| sentry | cs-o-jit-ws-single | 3.101 | 0.0456 | 1.47 % | 2880 | 14.8 | 342 | 325 |
| sentry | cs-o-jit-srv | 0.4874 | 0.0089 | 1.83 % | 18327 | 94.3 | 358 | 452 |
| sentry | cs-o-jit-srv-single | 3.145 | 0.0899 | 2.86 % | 2840 | 14.6 | 344 | 334 |
| bluesky | tsc6-parse | 1.243 | 0.0184 | 1.48 % | 1358 | 5.8 | 368 | 341 |
| bluesky | tsgo7-parse | 0.1097 | 0.0024 | 2.15 % | 15390 | 65.6 | 157 | 170 |
| bluesky | tsgo7-parse-single | 0.4383 | 0.0030 | 0.68 % | 3851 | 16.4 | 143 | 150 |
| bluesky | tsrs-parse | 0.0469 | 0.0006 | 1.38 % | 35997 | 153.4 | 70 | 212 |
| bluesky | tsrs-parse-single | 0.2460 | 0.0028 | 1.13 % | 6862 | 29.2 | 61 | 109 |
| bluesky | cs-aot-ws | 0.4852 | 0.0112 | 2.31 % | 3479 | 14.8 | 169 | 166 |
| bluesky | cs-o-aot-ws | 0.0530 | 0.0009 | 1.67 % | 31877 | 135.9 | 69 | 88 |
| bluesky | cs-o-aot-ws-single | 0.2568 | 0.0017 | 0.67 % | 6573 | 28.0 | 61 | 58 |
| bluesky | cs-o-aot-srv | 0.0665 | 0.0037 | 5.57 % | 25366 | 108.1 | 75 | 102 |
| bluesky | cs-o-aot-srv-single | 0.2569 | 0.0032 | 1.26 % | 6572 | 28.0 | 64 | 70 |
| bluesky | cs-o-jit-ws | 0.2546 | 0.0030 | 1.16 % | 6629 | 28.3 | 106 | 109 |
| bluesky | cs-o-jit-ws-single | 0.8591 | 0.0040 | 0.47 % | 1965 | 8.4 | 97 | 77 |
| bluesky | cs-o-jit-srv | 0.2661 | 0.0027 | 1.03 % | 6344 | 27.0 | 111 | 123 |
| bluesky | cs-o-jit-srv-single | 0.8680 | 0.0063 | 0.72 % | 1945 | 8.3 | 99 | 89 |
| playwright | tsc6-parse | 1.817 | 0.0083 | 0.46 % | 779 | 9.5 | 558 | 535 |
| playwright | tsgo7-parse | 0.1454 | 0.0028 | 1.91 % | 9733 | 118.5 | 274 | 288 |
| playwright | tsgo7-parse-single | 0.5953 | 0.0038 | 0.63 % | 2377 | 28.9 | 255 | 265 |
| playwright | tsrs-parse | 0.0587 | 0.0013 | 2.24 % | 24121 | 293.8 | 124 | 370 |
| playwright | tsrs-parse-single | 0.3001 | 0.0026 | 0.85 % | 4715 | 57.4 | 115 | 202 |
| playwright | cs-aot-ws | 0.9242 | 0.0478 | 5.17 % | 1531 | 18.6 | 324 | 320 |
| playwright | cs-o-aot-ws | 0.0618 | 0.0011 | 1.79 % | 22907 | 279.0 | 128 | 188 |
| playwright | cs-o-aot-ws-single | 0.3112 | 0.0020 | 0.66 % | 4547 | 55.4 | 112 | 116 |
| playwright | cs-o-aot-srv | 0.0721 | 0.0018 | 2.45 % | 19637 | 239.1 | 134 | 201 |
| playwright | cs-o-aot-srv-single | 0.3173 | 0.0021 | 0.66 % | 4460 | 54.3 | 116 | 129 |
| playwright | cs-o-jit-ws | 0.3004 | 0.0031 | 1.03 % | 4710 | 57.4 | 165 | 213 |
| playwright | cs-o-jit-ws-single | 1.177 | 0.0020 | 0.17 % | 1202 | 14.6 | 148 | 135 |
| playwright | cs-o-jit-srv | 0.3163 | 0.0046 | 1.46 % | 4473 | 54.5 | 171 | 229 |
| playwright | cs-o-jit-srv-single | 1.189 | 0.0049 | 0.42 % | 1190 | 14.5 | 151 | 147 |
| tldraw | tsc6-parse | 1.891 | 0.0130 | 0.69 % | 1327 | 7.2 | 543 | 519 |
| tldraw | tsgo7-parse | 0.1672 | 0.0043 | 2.56 % | 14997 | 81.5 | 276 | 288 |
| tldraw | tsgo7-parse-single | 0.7220 | 0.0067 | 0.93 % | 3474 | 18.9 | 257 | 268 |
| tldraw | tsrs-parse | 0.0660 | 0.0018 | 2.72 % | 38022 | 206.6 | 120 | 335 |
| tldraw | tsrs-parse-single | 0.3879 | 0.0037 | 0.94 % | 6465 | 35.1 | 112 | 196 |
| tldraw | cs-aot-ws | 0.9384 | 0.0226 | 2.41 % | 2673 | 14.5 | 311 | 309 |
| tldraw | cs-o-aot-ws | 0.0674 | 0.0009 | 1.32 % | 37189 | 202.1 | 118 | 162 |
| tldraw | cs-o-aot-ws-single | 0.3905 | 0.0033 | 0.83 % | 6422 | 34.9 | 106 | 106 |
| tldraw | cs-o-aot-srv | 0.0812 | 0.0020 | 2.52 % | 30878 | 167.8 | 123 | 170 |
| tldraw | cs-o-aot-srv-single | 0.3987 | 0.0040 | 1.01 % | 6291 | 34.2 | 108 | 118 |
| tldraw | cs-o-jit-ws | 0.2927 | 0.0036 | 1.24 % | 8569 | 46.6 | 155 | 186 |
| tldraw | cs-o-jit-ws-single | 1.285 | 0.0113 | 0.88 % | 1951 | 10.6 | 142 | 126 |
| tldraw | cs-o-jit-srv | 0.3029 | 0.0036 | 1.18 % | 8280 | 45.0 | 158 | 197 |
| tldraw | cs-o-jit-srv-single | 1.289 | 0.0070 | 0.54 % | 1945 | 10.6 | 144 | 137 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 11.62 [11.53, 11.77] | 11.89 [11.52, 12.27] | 11.33 [10.93, 11.56] | 12.50 [12.23, 12.57] | 11.31 [11.07, 11.57] | 11.72 [11.59, 11.83] |
| tsgo7-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-parse-single | 4.81 [4.78, 4.86] | 5.61 [5.56, 5.72] | 4.00 [3.86, 4.05] | 4.09 [4.01, 4.13] | 4.32 [4.23, 4.42] | 4.53 [4.49, 4.56] |
| tsrs-parse | 0.31 [0.30, 0.32] | 0.39 [0.38, 0.40] | 0.43 [0.41, 0.44] | 0.40 [0.39, 0.41] | 0.39 [0.38, 0.41] | 0.38 [0.38, 0.39] |
| tsrs-parse-single | 2.37 [2.36, 2.40] | 3.09 [3.06, 3.16] | 2.24 [2.16, 2.28] | 2.06 [2.02, 2.09] | 2.32 [2.28, 2.38] | 2.40 [2.38, 2.42] |
| cs-aot-ws | 8.66 [8.39, 8.82] | 7.09 [6.93, 7.27] | 4.42 [4.26, 4.51] | 6.36 [5.79, 6.52] | 5.61 [5.43, 5.77] | 6.27 [6.14, 6.34] |
| cs-o-aot-ws | 0.29 [0.28, 0.30] | 0.42 [0.42, 0.43] | 0.48 [0.47, 0.50] | 0.42 [0.41, 0.43] | 0.40 [0.39, 0.41] | 0.40 [0.39, 0.40] |
| cs-o-aot-ws-single | 2.44 [2.42, 2.47] | 3.07 [3.04, 3.13] | 2.34 [2.26, 2.38] | 2.14 [2.10, 2.17] | 2.34 [2.29, 2.39] | 2.45 [2.43, 2.47] |
| cs-o-aot-srv | 0.27 [0.26, 0.28] | 0.38 [0.38, 0.39] | 0.61 [0.58, 0.67] | 0.50 [0.48, 0.51] | 0.49 [0.47, 0.50] | 0.43 [0.43, 0.44] |
| cs-o-aot-srv-single | 2.45 [2.43, 2.48] | 3.07 [3.04, 3.13] | 2.34 [2.26, 2.39] | 2.18 [2.14, 2.20] | 2.38 [2.34, 2.44] | 2.47 [2.45, 2.49] |
| cs-o-jit-ws | 0.96 [0.93, 0.99] | 1.21 [1.19, 1.24] | 2.32 [2.24, 2.36] | 2.07 [2.03, 2.10] | 1.75 [1.72, 1.80] | 1.58 [1.56, 1.59] |
| cs-o-jit-ws-single | 4.55 [4.51, 4.67] | 7.57 [7.45, 7.73] | 7.83 [7.57, 7.94] | 8.10 [7.93, 8.15] | 7.69 [7.53, 7.89] | 7.00 [6.94, 7.07] |
| cs-o-jit-srv | 0.91 [0.90, 0.92] | 1.19 [1.17, 1.22] | 2.43 [2.34, 2.46] | 2.18 [2.13, 2.21] | 1.81 [1.78, 1.86] | 1.59 [1.58, 1.61] |
| cs-o-jit-srv-single | 4.59 [4.52, 4.70] | 7.68 [7.46, 7.86] | 7.91 [7.64, 8.03] | 8.18 [8.01, 8.25] | 7.71 [7.56, 7.89] | 7.06 [6.99, 7.13] |

## Table 2b. Median wall-clock relative to `tsrs-parse` (secondary reference), with 95 % confidence interval

Same method as Table 2, against tsrs (Rust port of TypeScript 7 / typescript-go), parallel. Secondary: never replaces the primary endpoint against typescript-go. tsrs parses JSDoc in .ts files lazily (TypeScript 7.1), typescript-go 7.0.2 eagerly, the C# tracks as tsc's host does (ParseForTypeErrors); see docs/references.md.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 37.56 [36.22, 38.77] | 30.42 [29.30, 31.16] | 26.50 [25.99, 26.96] | 30.97 [30.66, 31.68] | 28.66 [28.28, 29.41] | 30.61 [30.22, 30.96] |
| tsgo7-parse | 3.23 [3.10, 3.33] | 2.56 [2.49, 2.60] | 2.34 [2.29, 2.43] | 2.48 [2.45, 2.55] | 2.54 [2.47, 2.61] | 2.61 [2.58, 2.64] |
| tsgo7-parse-single | 15.53 [15.00, 16.03] | 14.37 [14.04, 14.53] | 9.35 [9.21, 9.46] | 10.15 [10.04, 10.38] | 10.95 [10.79, 11.23] | 11.83 [11.71, 11.95] |
| tsrs-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-parse-single | 7.67 [7.41, 7.92] | 7.92 [7.74, 8.03] | 5.25 [5.15, 5.32] | 5.12 [5.06, 5.24] | 5.88 [5.82, 6.06] | 6.26 [6.20, 6.33] |
| cs-aot-ws | 27.99 [26.60, 28.93] | 18.16 [17.60, 18.51] | 10.35 [10.12, 10.53] | 15.75 [14.35, 16.33] | 14.23 [13.77, 14.67] | 16.38 [16.00, 16.58] |
| cs-o-aot-ws | 0.92 [0.88, 0.96] | 1.08 [1.06, 1.10] | 1.13 [1.11, 1.16] | 1.05 [1.03, 1.08] | 1.02 [1.01, 1.05] | 1.04 [1.03, 1.05] |
| cs-o-aot-ws-single | 7.87 [7.60, 8.12] | 7.87 [7.68, 7.96] | 5.48 [5.39, 5.55] | 5.31 [5.26, 5.44] | 5.92 [5.84, 6.08] | 6.39 [6.33, 6.46] |
| cs-o-aot-srv | 0.86 [0.82, 0.90] | 0.98 [0.95, 0.99] | 1.42 [1.37, 1.56] | 1.23 [1.20, 1.27] | 1.23 [1.21, 1.27] | 1.12 [1.11, 1.15] |
| cs-o-aot-srv-single | 7.91 [7.64, 8.17] | 7.86 [7.68, 7.95] | 5.48 [5.38, 5.59] | 5.41 [5.36, 5.54] | 6.04 [5.95, 6.21] | 6.45 [6.38, 6.52] |
| cs-o-jit-ws | 3.10 [2.95, 3.23] | 3.11 [3.02, 3.14] | 5.43 [5.34, 5.52] | 5.12 [5.08, 5.25] | 4.44 [4.38, 4.58] | 4.12 [4.07, 4.17] |
| cs-o-jit-ws-single | 14.70 [14.16, 15.29] | 19.37 [18.89, 19.65] | 18.32 [18.04, 18.53] | 20.07 [19.90, 20.54] | 19.49 [19.21, 20.04] | 18.28 [18.10, 18.50] |
| cs-o-jit-srv | 2.93 [2.82, 3.03] | 3.04 [2.97, 3.11] | 5.67 [5.58, 5.74] | 5.39 [5.32, 5.54] | 4.59 [4.53, 4.72] | 4.16 [4.12, 4.21] |
| cs-o-jit-srv-single | 14.84 [14.27, 15.39] | 19.65 [18.97, 20.01] | 18.51 [18.21, 18.75] | 20.26 [20.08, 20.74] | 19.55 [19.33, 20.07] | 18.45 [18.23, 18.66] |

## Table 3. Variance gate (MAD/median < 3 %)

Binding rows (rig check): tsc6-parse, tsgo7-parse-single and the primary single-threaded C# configuration of each track. Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md, B11 as amended by prereg/v4).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | rig check | 0.84 % | 2.43 % | 1.48 % | 0.46 % | 0.69 % | 2.43 % |
| tsgo7-parse | reported | 1.12 % | 1.85 % | 2.15 % | 1.91 % | 2.56 % | 2.56 % |
| tsgo7-parse-single | rig check | 0.22 % | 0.20 % | 0.68 % | 0.63 % | 0.93 % | 0.93 % |
| tsrs-parse | reported | 3.71 % | 2.08 % | 1.38 % | 2.24 % | 2.72 % | 3.71 % |
| tsrs-parse-single | reported | 0.18 % | 0.57 % | 1.13 % | 0.85 % | 0.94 % | 1.13 % |
| cs-aot-ws | reported | 2.55 % | 2.71 % | 2.31 % | 5.17 % | 2.41 % | 5.17 % |
| cs-o-aot-ws | reported | 2.55 % | 0.97 % | 1.67 % | 1.79 % | 1.32 % | 2.55 % |
| cs-o-aot-ws-single | rig check | 0.40 % | 0.41 % | 0.67 % | 0.66 % | 0.83 % | 0.83 % |
| cs-o-aot-srv | reported | 2.90 % | 1.30 % | 5.57 % | 2.45 % | 2.52 % | 5.57 % |
| cs-o-aot-srv-single | reported | 0.52 % | 0.38 % | 1.26 % | 0.66 % | 1.01 % | 1.26 % |
| cs-o-jit-ws | reported | 3.54 % | 0.61 % | 1.16 % | 1.03 % | 1.24 % | 3.54 % |
| cs-o-jit-ws-single | reported | 1.53 % | 1.47 % | 0.47 % | 0.17 % | 0.88 % | 1.53 % |
| cs-o-jit-srv | reported | 0.82 % | 1.83 % | 1.03 % | 1.46 % | 1.18 % | 1.83 % |
| cs-o-jit-srv-single | reported | 2.45 % | 2.86 % | 0.72 % | 0.42 % | 0.54 % | 2.86 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsc6-parse | 0.2654 | 0.50 % | 21.4 % | 97 |
| tsgo7-parse | 0.0202 | 1.40 % | 18.4 % | 9 |
| tsgo7-parse-single | 0.0204 | 0.89 % | 4.7 % | 9 |
| tsrs-parse | 0.0191 | 1.48 % | 40.7 % | 8 |
| tsrs-parse-single | 0.0173 | 1.15 % | 7.0 % | 7 |
| cs-aot-ws | 0.0232 | 1.98 % | 4.8 % | 10 |
| cs-o-aot-ws | 0.0240 | 1.60 % | 45.3 % | 10 |
| cs-o-aot-ws-single | 0.0231 | 1.34 % | 9.0 % | 10 |
| cs-o-aot-srv | 0.0269 | 0.93 % | 40.4 % | 12 |
| cs-o-aot-srv-single | 0.0282 | 2.31 % | 11.0 % | 12 |
| cs-o-jit-ws | 0.0768 | 1.65 % | 30.2 % | 26 |
| cs-o-jit-ws-single | 0.0764 | 1.02 % | 8.9 % | 27 |
| cs-o-jit-srv | 0.0847 | 2.93 % | 31.8 % | 28 |
| cs-o-jit-srv-single | 0.0815 | 1.00 % | 9.4 % | 28 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-aot-ws | 3278295536 | 390/347/7 | 6926.1 |
| vscode | cs-o-aot-ws | 65173184 | 6/4/3 | 26.0 |
| vscode | cs-o-aot-ws-single | 37073568 | 5/3/1 | 21.1 |
| vscode | cs-o-aot-srv | 66764952 | 6/5/4 | 9.1 |
| vscode | cs-o-aot-srv-single | 37069456 | 3/1/0 | 8.2 |
| vscode | cs-o-jit-ws | 65578536 | 7/5/3 | 39.2 |
| vscode | cs-o-jit-ws-single | 37978432 | 5/3/1 | 21.7 |
| vscode | cs-o-jit-srv | 65354600 | 7/6/5 | 14.2 |
| vscode | cs-o-jit-srv-single | 37976896 | 3/1/0 | 8.0 |
| sentry | cs-aot-ws | 1094713560 | 138/137/5 | 2059.3 |
| sentry | cs-o-aot-ws | 45938544 | 5/5/3 | 27.2 |
| sentry | cs-o-aot-ws-single | 20397504 | 2/1/0 | 9.2 |
| sentry | cs-o-aot-srv | 50095832 | 6/5/4 | 8.1 |
| sentry | cs-o-aot-srv-single | 20397168 | 3/1/0 | 7.3 |
| sentry | cs-o-jit-ws | 45910352 | 5/5/3 | 26.6 |
| sentry | cs-o-jit-ws-single | 21257264 | 2/1/0 | 8.8 |
| sentry | cs-o-jit-srv | 46285568 | 6/5/4 | 8.9 |
| sentry | cs-o-jit-srv-single | 21235304 | 3/1/0 | 7.4 |
| bluesky | cs-aot-ws | 185100120 | 25/24/3 | 358.3 |
| bluesky | cs-o-aot-ws | 24173040 | 2/2/2 | 1.6 |
| bluesky | cs-o-aot-ws-single | 4771352 | 0/0/0 | 0.0 |
| bluesky | cs-o-aot-srv | 30761096 | 6/5/4 | 10.2 |
| bluesky | cs-o-aot-srv-single | 4772048 | 1/0/0 | 1.9 |
| bluesky | cs-o-jit-ws | 24850440 | 2/2/2 | 3.5 |
| bluesky | cs-o-jit-ws-single | 5489832 | 0/0/0 | 0.0 |
| bluesky | cs-o-jit-srv | 25705768 | 6/5/4 | 10.4 |
| bluesky | cs-o-jit-srv-single | 5491392 | 1/0/0 | 2.0 |
| playwright | cs-aot-ws | 378816960 | 47/46/4 | 722.8 |
| playwright | cs-o-aot-ws | 27697672 | 2/2/2 | 1.9 |
| playwright | cs-o-aot-ws-single | 4919496 | 0/0/0 | 0.0 |
| playwright | cs-o-aot-srv | 33964888 | 6/5/4 | 8.1 |
| playwright | cs-o-aot-srv-single | 4920192 | 1/0/0 | 1.9 |
| playwright | cs-o-jit-ws | 28790976 | 2/2/2 | 3.4 |
| playwright | cs-o-jit-ws-single | 5633584 | 0/0/0 | 0.0 |
| playwright | cs-o-jit-srv | 28827088 | 7/6/5 | 13.7 |
| playwright | cs-o-jit-srv-single | 5635144 | 1/0/0 | 1.9 |
| tldraw | cs-aot-ws | 356371728 | 47/43/4 | 720.7 |
| tldraw | cs-o-aot-ws | 29914160 | 2/2/2 | 1.9 |
| tldraw | cs-o-aot-ws-single | 7459960 | 0/0/0 | 0.0 |
| tldraw | cs-o-aot-srv | 37398968 | 6/5/4 | 9.9 |
| tldraw | cs-o-aot-srv-single | 7460992 | 2/1/0 | 4.5 |
| tldraw | cs-o-jit-ws | 31681576 | 3/3/2 | 6.4 |
| tldraw | cs-o-jit-ws-single | 8191560 | 0/0/0 | 0.0 |
| tldraw | cs-o-jit-srv | 30941416 | 6/5/4 | 11.7 |
| tldraw | cs-o-jit-srv-single | 8193680 | 2/1/0 | 4.8 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (70 files).
