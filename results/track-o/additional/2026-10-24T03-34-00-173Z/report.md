# Parse-only benchmark, run 2026-10-24T03-34-00-173Z

**Status: protocol result.** All rig checks passed.

Label: track-o-additional-run-after-O28

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 4.3 % (median of 1 s windows: 4.2, 3.4, 1.9, 2.9, 13.8, 7.4, 6.5, 3.4, 4.3, 6.4) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 3c6cb2dfb8, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 4.3 % (range 1.9-13.8 %) | ok |
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
| vscode | tsc6-parse | 12.02 | 0.0561 | 0.47 % | 975 | 11.7 | 3183 | 3224 |
| vscode | tsgo7-parse | 1.033 | 0.0222 | 2.15 % | 11345 | 136.6 | 2169 | 2208 |
| vscode | tsgo7-parse-single | 5.010 | 0.0233 | 0.46 % | 2340 | 28.2 | 2116 | 2169 |
| vscode | tsrs-parse | 0.3202 | 0.0110 | 3.44 % | 36618 | 440.9 | 956 | 1904 |
| vscode | tsrs-parse-single | 2.490 | 0.0128 | 0.51 % | 4709 | 56.7 | 940 | 1253 |
| vscode | cs-aot-ws | 8.680 | 0.3556 | 4.10 % | 1351 | 16.3 | 2638 | 2644 |
| vscode | cs-o-aot-ws | 0.2939 | 0.0054 | 1.84 % | 39892 | 480.4 | 885 | 1051 |
| vscode | cs-o-aot-ws-single | 2.572 | 0.0067 | 0.26 % | 4558 | 54.9 | 854 | 871 |
| vscode | cs-o-aot-srv | 0.2880 | 0.0132 | 4.59 % | 40709 | 490.2 | 891 | 1083 |
| vscode | cs-o-aot-srv-single | 2.574 | 0.0066 | 0.26 % | 4555 | 54.8 | 860 | 886 |
| vscode | cs-o-jit-ws | 1.002 | 0.0268 | 2.68 % | 11704 | 140.9 | 920 | 1044 |
| vscode | cs-o-jit-ws-single | 4.684 | 0.0830 | 1.77 % | 2503 | 30.1 | 897 | 894 |
| vscode | cs-o-jit-srv | 0.9755 | 0.0177 | 1.82 % | 12019 | 144.7 | 927 | 1049 |
| vscode | cs-o-jit-srv-single | 4.688 | 0.0408 | 0.87 % | 2501 | 30.1 | 898 | 907 |
| sentry | tsc6-parse | 4.994 | 0.1039 | 2.08 % | 1789 | 9.2 | 1207 | 1207 |
| sentry | tsgo7-parse | 0.4214 | 0.0087 | 2.07 % | 21195 | 109.0 | 789 | 811 |
| sentry | tsgo7-parse-single | 2.337 | 0.0158 | 0.68 % | 3822 | 19.7 | 760 | 776 |
| sentry | tsrs-parse | 0.1641 | 0.0023 | 1.39 % | 54438 | 280.0 | 342 | 658 |
| sentry | tsrs-parse-single | 1.291 | 0.0075 | 0.58 % | 6918 | 35.6 | 329 | 634 |
| sentry | cs-aot-ws | 2.905 | 0.0534 | 1.84 % | 3074 | 15.8 | 916 | 914 |
| sentry | cs-o-aot-ws | 0.1733 | 0.0025 | 1.46 % | 51540 | 265.1 | 321 | 423 |
| sentry | cs-o-aot-ws-single | 1.271 | 0.0073 | 0.58 % | 7029 | 36.2 | 303 | 305 |
| sentry | cs-o-aot-srv | 0.1684 | 0.0047 | 2.80 % | 53042 | 272.8 | 323 | 446 |
| sentry | cs-o-aot-srv-single | 1.289 | 0.0088 | 0.69 % | 6930 | 35.6 | 305 | 314 |
| sentry | cs-o-jit-ws | 0.5030 | 0.0051 | 1.01 % | 17757 | 91.3 | 357 | 449 |
| sentry | cs-o-jit-ws-single | 2.953 | 0.0416 | 1.41 % | 3025 | 15.6 | 342 | 325 |
| sentry | cs-o-jit-srv | 0.4929 | 0.0087 | 1.76 % | 18121 | 93.2 | 358 | 454 |
| sentry | cs-o-jit-srv-single | 2.827 | 0.0294 | 1.04 % | 3159 | 16.2 | 344 | 334 |
| bluesky | tsc6-parse | 1.253 | 0.0104 | 0.83 % | 1348 | 5.7 | 359 | 340 |
| bluesky | tsgo7-parse | 0.1132 | 0.0027 | 2.38 % | 14916 | 63.6 | 159 | 171 |
| bluesky | tsgo7-parse-single | 0.4421 | 0.0036 | 0.81 % | 3819 | 16.3 | 143 | 151 |
| bluesky | tsrs-parse | 0.0489 | 0.0010 | 2.08 % | 34554 | 147.3 | 70 | 213 |
| bluesky | tsrs-parse-single | 0.2481 | 0.0021 | 0.85 % | 6804 | 29.0 | 61 | 109 |
| bluesky | cs-aot-ws | 0.4885 | 0.0088 | 1.81 % | 3456 | 14.7 | 171 | 164 |
| bluesky | cs-o-aot-ws | 0.0529 | 0.0011 | 2.00 % | 31917 | 136.0 | 69 | 88 |
| bluesky | cs-o-aot-ws-single | 0.2612 | 0.0014 | 0.52 % | 6464 | 27.5 | 61 | 58 |
| bluesky | cs-o-aot-srv | 0.0669 | 0.0026 | 3.82 % | 25231 | 107.5 | 75 | 103 |
| bluesky | cs-o-aot-srv-single | 0.2632 | 0.0029 | 1.10 % | 6414 | 27.3 | 64 | 70 |
| bluesky | cs-o-jit-ws | 0.2610 | 0.0063 | 2.43 % | 6466 | 27.6 | 106 | 108 |
| bluesky | cs-o-jit-ws-single | 0.8648 | 0.0033 | 0.38 % | 1952 | 8.3 | 97 | 77 |
| bluesky | cs-o-jit-srv | 0.2739 | 0.0041 | 1.50 % | 6162 | 26.3 | 112 | 121 |
| bluesky | cs-o-jit-srv-single | 0.8809 | 0.0059 | 0.67 % | 1916 | 8.2 | 99 | 89 |
| playwright | tsc6-parse | 1.820 | 0.0128 | 0.70 % | 777 | 9.5 | 549 | 535 |
| playwright | tsgo7-parse | 0.1467 | 0.0059 | 3.99 % | 9647 | 117.5 | 269 | 285 |
| playwright | tsgo7-parse-single | 0.6039 | 0.0047 | 0.78 % | 2343 | 28.5 | 256 | 264 |
| playwright | tsrs-parse | 0.0588 | 0.0015 | 2.57 % | 24080 | 293.3 | 124 | 367 |
| playwright | tsrs-parse-single | 0.3010 | 0.0025 | 0.83 % | 4700 | 57.2 | 115 | 203 |
| playwright | cs-aot-ws | 0.9279 | 0.0116 | 1.25 % | 1525 | 18.6 | 324 | 318 |
| playwright | cs-o-aot-ws | 0.0619 | 0.0011 | 1.74 % | 22845 | 278.2 | 128 | 187 |
| playwright | cs-o-aot-ws-single | 0.3180 | 0.0023 | 0.73 % | 4450 | 54.2 | 112 | 116 |
| playwright | cs-o-aot-srv | 0.0746 | 0.0020 | 2.63 % | 18977 | 231.1 | 134 | 205 |
| playwright | cs-o-aot-srv-single | 0.3273 | 0.0020 | 0.62 % | 4323 | 52.6 | 116 | 129 |
| playwright | cs-o-jit-ws | 0.3045 | 0.0030 | 0.97 % | 4647 | 56.6 | 166 | 213 |
| playwright | cs-o-jit-ws-single | 1.202 | 0.0128 | 1.06 % | 1177 | 14.3 | 148 | 136 |
| playwright | cs-o-jit-srv | 0.3258 | 0.0067 | 2.04 % | 4343 | 52.9 | 170 | 227 |
| playwright | cs-o-jit-srv-single | 1.221 | 0.0126 | 1.03 % | 1159 | 14.1 | 151 | 147 |
| tldraw | tsc6-parse | 1.887 | 0.0120 | 0.64 % | 1329 | 7.2 | 534 | 520 |
| tldraw | tsgo7-parse | 0.1576 | 0.0047 | 2.98 % | 15910 | 86.4 | 270 | 284 |
| tldraw | tsgo7-parse-single | 0.7184 | 0.0058 | 0.81 % | 3491 | 19.0 | 257 | 268 |
| tldraw | tsrs-parse | 0.0669 | 0.0010 | 1.54 % | 37467 | 203.6 | 121 | 338 |
| tldraw | tsrs-parse-single | 0.3894 | 0.0028 | 0.72 % | 6441 | 35.0 | 112 | 196 |
| tldraw | cs-aot-ws | 0.9551 | 0.0093 | 0.98 % | 2626 | 14.3 | 311 | 307 |
| tldraw | cs-o-aot-ws | 0.0684 | 0.0011 | 1.67 % | 36687 | 199.3 | 117 | 161 |
| tldraw | cs-o-aot-ws-single | 0.3970 | 0.0032 | 0.81 % | 6318 | 34.3 | 106 | 106 |
| tldraw | cs-o-aot-srv | 0.0800 | 0.0018 | 2.24 % | 31364 | 170.4 | 121 | 168 |
| tldraw | cs-o-aot-srv-single | 0.4095 | 0.0045 | 1.10 % | 6124 | 33.3 | 108 | 118 |
| tldraw | cs-o-jit-ws | 0.2947 | 0.0039 | 1.32 % | 8511 | 46.2 | 156 | 187 |
| tldraw | cs-o-jit-ws-single | 1.282 | 0.0043 | 0.33 % | 1957 | 10.6 | 142 | 126 |
| tldraw | cs-o-jit-srv | 0.3108 | 0.0046 | 1.49 % | 8069 | 43.8 | 158 | 198 |
| tldraw | cs-o-jit-srv-single | 1.299 | 0.0088 | 0.68 % | 1931 | 10.5 | 144 | 137 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 11.63 [11.56, 11.92] | 11.85 [11.42, 12.15] | 11.07 [10.89, 11.41] | 12.41 [11.87, 12.83] | 11.97 [11.65, 12.25] | 11.78 [11.62, 11.93] |
| tsgo7-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-parse-single | 4.85 [4.81, 4.97] | 5.55 [5.46, 5.68] | 3.91 [3.84, 4.02] | 4.12 [3.93, 4.25] | 4.56 [4.43, 4.66] | 4.56 [4.50, 4.62] |
| tsrs-parse | 0.31 [0.30, 0.32] | 0.39 [0.38, 0.40] | 0.43 [0.42, 0.45] | 0.40 [0.38, 0.42] | 0.42 [0.41, 0.44] | 0.39 [0.38, 0.40] |
| tsrs-parse-single | 2.41 [2.39, 2.47] | 3.06 [3.01, 3.14] | 2.19 [2.16, 2.26] | 2.05 [1.96, 2.12] | 2.47 [2.40, 2.53] | 2.41 [2.38, 2.45] |
| cs-aot-ws | 8.40 [8.02, 8.72] | 6.89 [6.73, 7.10] | 4.32 [4.22, 4.46] | 6.33 [6.05, 6.56] | 6.06 [5.88, 6.21] | 6.26 [6.15, 6.35] |
| cs-o-aot-ws | 0.28 [0.28, 0.29] | 0.41 [0.40, 0.42] | 0.47 [0.46, 0.49] | 0.42 [0.40, 0.44] | 0.43 [0.42, 0.45] | 0.40 [0.39, 0.40] |
| cs-o-aot-ws-single | 2.49 [2.47, 2.55] | 3.02 [2.97, 3.09] | 2.31 [2.27, 2.38] | 2.17 [2.07, 2.24] | 2.52 [2.45, 2.58] | 2.48 [2.45, 2.52] |
| cs-o-aot-srv | 0.28 [0.27, 0.29] | 0.40 [0.39, 0.41] | 0.59 [0.58, 0.64] | 0.51 [0.48, 0.53] | 0.51 [0.49, 0.53] | 0.44 [0.44, 0.45] |
| cs-o-aot-srv-single | 2.49 [2.47, 2.55] | 3.06 [3.01, 3.14] | 2.33 [2.28, 2.40] | 2.23 [2.14, 2.30] | 2.60 [2.52, 2.66] | 2.53 [2.49, 2.56] |
| cs-o-jit-ws | 0.97 [0.93, 1.00] | 1.19 [1.17, 1.23] | 2.31 [2.26, 2.39] | 2.08 [1.99, 2.15] | 1.87 [1.82, 1.92] | 1.60 [1.57, 1.62] |
| cs-o-jit-ws-single | 4.53 [4.48, 4.66] | 7.01 [6.90, 7.55] | 7.64 [7.52, 7.87] | 8.19 [7.84, 8.53] | 8.13 [7.91, 8.31] | 6.95 [6.87, 7.09] |
| cs-o-jit-srv | 0.94 [0.93, 0.97] | 1.17 [1.15, 1.20] | 2.42 [2.38, 2.50] | 2.22 [2.12, 2.31] | 1.97 [1.92, 2.03] | 1.64 [1.62, 1.66] |
| cs-o-jit-srv-single | 4.54 [4.50, 4.68] | 6.71 [6.59, 6.89] | 7.78 [7.66, 8.02] | 8.33 [7.96, 8.67] | 8.24 [8.02, 8.43] | 6.95 [6.87, 7.06] |

## Table 2b. Median wall-clock relative to `tsrs-parse` (secondary reference), with 95 % confidence interval

Same method as Table 2, against tsrs (Rust port of TypeScript 7 / typescript-go), parallel. Secondary: never replaces the primary endpoint against typescript-go. tsrs parses JSDoc in .ts files lazily (TypeScript 7.1), typescript-go 7.0.2 eagerly, the C# tracks as tsc's host does (ParseForTypeErrors); see docs/references.md.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 37.55 [36.13, 38.18] | 30.44 [29.49, 30.83] | 25.64 [24.99, 26.01] | 30.98 [30.48, 31.72] | 28.20 [27.65, 28.52] | 30.31 [29.93, 30.52] |
| tsgo7-parse | 3.23 [3.09, 3.29] | 2.57 [2.50, 2.62] | 2.32 [2.22, 2.36] | 2.50 [2.41, 2.64] | 2.35 [2.28, 2.42] | 2.57 [2.53, 2.61] |
| tsgo7-parse-single | 15.65 [15.06, 15.91] | 14.24 [14.09, 14.44] | 9.05 [8.78, 9.17] | 10.28 [10.10, 10.51] | 10.73 [10.50, 10.85] | 11.73 [11.60, 11.81] |
| tsrs-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-parse-single | 7.78 [7.49, 7.92] | 7.87 [7.78, 7.99] | 5.08 [4.94, 5.15] | 5.12 [5.02, 5.23] | 5.82 [5.70, 5.89] | 6.21 [6.14, 6.26] |
| cs-aot-ws | 27.11 [25.65, 28.01] | 17.71 [17.37, 18.03] | 10.00 [9.68, 10.17] | 15.79 [15.53, 16.23] | 14.27 [13.94, 14.45] | 16.10 [15.85, 16.25] |
| cs-o-aot-ws | 0.92 [0.88, 0.94] | 1.06 [1.04, 1.07] | 1.08 [1.05, 1.12] | 1.05 [1.03, 1.08] | 1.02 [1.00, 1.04] | 1.02 [1.01, 1.04] |
| cs-o-aot-ws-single | 8.03 [7.73, 8.17] | 7.74 [7.66, 7.85] | 5.35 [5.21, 5.42] | 5.41 [5.32, 5.54] | 5.93 [5.80, 6.00] | 6.39 [6.32, 6.44] |
| cs-o-aot-srv | 0.90 [0.85, 0.93] | 1.03 [1.00, 1.05] | 1.37 [1.33, 1.47] | 1.27 [1.24, 1.32] | 1.19 [1.17, 1.24] | 1.14 [1.12, 1.16] |
| cs-o-aot-srv-single | 8.04 [7.73, 8.17] | 7.86 [7.77, 7.98] | 5.39 [5.22, 5.46] | 5.57 [5.48, 5.70] | 6.12 [5.98, 6.19] | 6.50 [6.43, 6.54] |
| cs-o-jit-ws | 3.13 [2.98, 3.20] | 3.07 [3.02, 3.11] | 5.34 [5.17, 5.47] | 5.18 [5.10, 5.30] | 4.40 [4.32, 4.49] | 4.11 [4.05, 4.14] |
| cs-o-jit-ws-single | 14.63 [14.05, 14.94] | 18.00 [17.81, 19.27] | 17.70 [17.28, 17.94] | 20.45 [20.13, 21.17] | 19.15 [18.80, 19.36] | 17.88 [17.70, 18.15] |
| cs-o-jit-srv | 3.05 [2.93, 3.10] | 3.00 [2.96, 3.05] | 5.61 [5.45, 5.71] | 5.54 [5.43, 5.72] | 4.64 [4.55, 4.73] | 4.21 [4.16, 4.25] |
| cs-o-jit-srv-single | 14.64 [14.09, 14.98] | 17.23 [17.00, 17.48] | 18.03 [17.56, 18.28] | 20.78 [20.43, 21.58] | 19.40 [19.05, 19.63] | 17.89 [17.71, 18.05] |

## Table 3. Variance gate (MAD/median < 3 %)

Binding rows (rig check): tsc6-parse, tsgo7-parse-single and the primary single-threaded C# configuration of each track. Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md, B11 as amended by prereg/v4).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | rig check | 0.47 % | 2.08 % | 0.83 % | 0.70 % | 0.64 % | 2.08 % |
| tsgo7-parse | reported | 2.15 % | 2.07 % | 2.38 % | 3.99 % | 2.98 % | 3.99 % |
| tsgo7-parse-single | rig check | 0.46 % | 0.68 % | 0.81 % | 0.78 % | 0.81 % | 0.81 % |
| tsrs-parse | reported | 3.44 % | 1.39 % | 2.08 % | 2.57 % | 1.54 % | 3.44 % |
| tsrs-parse-single | reported | 0.51 % | 0.58 % | 0.85 % | 0.83 % | 0.72 % | 0.85 % |
| cs-aot-ws | reported | 4.10 % | 1.84 % | 1.81 % | 1.25 % | 0.98 % | 4.10 % |
| cs-o-aot-ws | reported | 1.84 % | 1.46 % | 2.00 % | 1.74 % | 1.67 % | 2.00 % |
| cs-o-aot-ws-single | rig check | 0.26 % | 0.58 % | 0.52 % | 0.73 % | 0.81 % | 0.81 % |
| cs-o-aot-srv | reported | 4.59 % | 2.80 % | 3.82 % | 2.63 % | 2.24 % | 4.59 % |
| cs-o-aot-srv-single | reported | 0.26 % | 0.69 % | 1.10 % | 0.62 % | 1.10 % | 1.10 % |
| cs-o-jit-ws | reported | 2.68 % | 1.01 % | 2.43 % | 0.97 % | 1.32 % | 2.68 % |
| cs-o-jit-ws-single | reported | 1.77 % | 1.41 % | 0.38 % | 1.06 % | 0.33 % | 1.77 % |
| cs-o-jit-srv | reported | 1.82 % | 1.76 % | 1.50 % | 2.04 % | 1.49 % | 2.04 % |
| cs-o-jit-srv-single | reported | 0.87 % | 1.04 % | 0.67 % | 1.03 % | 0.68 % | 1.04 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsc6-parse | 0.2663 | 0.77 % | 21.3 % | 88 |
| tsgo7-parse | 0.0215 | 1.15 % | 19.0 % | 9 |
| tsgo7-parse-single | 0.0207 | 0.93 % | 4.7 % | 9 |
| tsrs-parse | 0.0193 | 2.97 % | 39.5 % | 8 |
| tsrs-parse-single | 0.0173 | 1.44 % | 7.0 % | 7 |
| cs-aot-ws | 0.0231 | 1.26 % | 4.7 % | 10 |
| cs-o-aot-ws | 0.0242 | 1.34 % | 45.8 % | 10 |
| cs-o-aot-ws-single | 0.0242 | 1.62 % | 9.3 % | 10 |
| cs-o-aot-srv | 0.0276 | 0.93 % | 41.2 % | 12 |
| cs-o-aot-srv-single | 0.0277 | 2.21 % | 10.5 % | 12 |
| cs-o-jit-ws | 0.0779 | 0.74 % | 29.8 % | 27 |
| cs-o-jit-ws-single | 0.0775 | 0.85 % | 9.0 % | 27 |
| cs-o-jit-srv | 0.0844 | 3.31 % | 30.8 % | 28 |
| cs-o-jit-srv-single | 0.0919 | 5.58 % | 10.4 % | 28 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-aot-ws | 3278430872 | 391/349/7 | 7335.5 |
| vscode | cs-o-aot-ws | 65404880 | 6/4/3 | 25.2 |
| vscode | cs-o-aot-ws-single | 37074296 | 5/3/1 | 22.8 |
| vscode | cs-o-aot-srv | 65446264 | 6/5/4 | 9.8 |
| vscode | cs-o-aot-srv-single | 37069456 | 3/1/0 | 8.0 |
| vscode | cs-o-jit-ws | 65212840 | 7/5/3 | 33.6 |
| vscode | cs-o-jit-ws-single | 37979064 | 5/3/1 | 19.7 |
| vscode | cs-o-jit-srv | 65252088 | 7/6/5 | 13.4 |
| vscode | cs-o-jit-srv-single | 37976896 | 3/1/0 | 8.0 |
| sentry | cs-aot-ws | 1094682752 | 139/137/6 | 2420.2 |
| sentry | cs-o-aot-ws | 47742376 | 5/5/3 | 23.1 |
| sentry | cs-o-aot-ws-single | 20397504 | 2/1/0 | 9.1 |
| sentry | cs-o-aot-srv | 53761888 | 6/5/4 | 9.0 |
| sentry | cs-o-aot-srv-single | 20397168 | 3/1/0 | 7.1 |
| sentry | cs-o-jit-ws | 46158456 | 5/5/3 | 25.3 |
| sentry | cs-o-jit-ws-single | 21232640 | 2/1/0 | 9.7 |
| sentry | cs-o-jit-srv | 45330512 | 6/5/4 | 10.3 |
| sentry | cs-o-jit-srv-single | 21259928 | 3/1/0 | 7.4 |
| bluesky | cs-aot-ws | 185018216 | 25/24/3 | 372.0 |
| bluesky | cs-o-aot-ws | 24943344 | 2/2/2 | 1.7 |
| bluesky | cs-o-aot-ws-single | 4771352 | 0/0/0 | 0.0 |
| bluesky | cs-o-aot-srv | 28616952 | 6/5/4 | 10.1 |
| bluesky | cs-o-aot-srv-single | 4772048 | 1/0/0 | 1.9 |
| bluesky | cs-o-jit-ws | 25677536 | 2/2/2 | 3.0 |
| bluesky | cs-o-jit-ws-single | 5489832 | 0/0/0 | 0.0 |
| bluesky | cs-o-jit-srv | 25045136 | 6/5/4 | 10.3 |
| bluesky | cs-o-jit-srv-single | 5491392 | 1/0/0 | 1.9 |
| playwright | cs-aot-ws | 378775824 | 46/45/4 | 714.3 |
| playwright | cs-o-aot-ws | 28736808 | 2/2/2 | 1.9 |
| playwright | cs-o-aot-ws-single | 4919496 | 0/0/0 | 0.0 |
| playwright | cs-o-aot-srv | 32343960 | 6/5/4 | 9.6 |
| playwright | cs-o-aot-srv-single | 4920192 | 1/0/0 | 1.9 |
| playwright | cs-o-jit-ws | 28622560 | 2/2/2 | 3.7 |
| playwright | cs-o-jit-ws-single | 5633584 | 0/0/0 | 0.0 |
| playwright | cs-o-jit-srv | 28332008 | 6/5/4 | 12.1 |
| playwright | cs-o-jit-srv-single | 5635144 | 1/0/0 | 1.8 |
| tldraw | cs-aot-ws | 356546312 | 47/43/4 | 757.9 |
| tldraw | cs-o-aot-ws | 30144696 | 2/2/2 | 2.1 |
| tldraw | cs-o-aot-ws-single | 7459960 | 0/0/0 | 0.0 |
| tldraw | cs-o-aot-srv | 33088464 | 6/5/4 | 9.8 |
| tldraw | cs-o-aot-srv-single | 7460992 | 2/1/0 | 4.7 |
| tldraw | cs-o-jit-ws | 31588264 | 3/3/2 | 7.1 |
| tldraw | cs-o-jit-ws-single | 8191560 | 0/0/0 | 0.0 |
| tldraw | cs-o-jit-srv | 31962760 | 6/5/4 | 12.8 |
| tldraw | cs-o-jit-srv-single | 8193680 | 2/1/0 | 4.7 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (70 files).
