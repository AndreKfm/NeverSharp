# Parse-only benchmark, run 2026-10-22T07-01-02-524Z

**Status: protocol result.** All rig checks passed.

Label: m6-track-f-run3

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 99 % of 4150 MHz, idle before run 11.9 % (median of 1 s windows: 10.9, 11.7, 19.6, 22.6, 25.4, 17.8, 12.1, 7, 3.5, 5.5) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, hyperfine 1.20.0 |
| Harness | commit cc1b18057c, pins.json sha256 fb8d8763ad34… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 99 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 11.9 % (range 3.5-25.4 %) | ok |
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
- `cs-aot-ws`: C# Track F, NativeAOT, Workstation GC, parallel (primary C# configuration)
- `cs-aot-ws-single`: C# Track F, NativeAOT, Workstation GC, single-threaded
- `cs-aot-srv`: C# Track F, NativeAOT, Server GC, parallel
- `cs-aot-srv-single`: C# Track F, NativeAOT, Server GC, single-threaded
- `cs-jit-ws`: C# Track F, .NET 10 JIT (TieredPGO default), Workstation GC, parallel
- `cs-jit-ws-single`: C# Track F, .NET 10 JIT (TieredPGO default), Workstation GC, single-threaded
- `cs-jit-srv`: C# Track F, .NET 10 JIT, Server GC, parallel
- `cs-jit-srv-single`: C# Track F, .NET 10 JIT, Server GC, single-threaded

## Table 1. Wall-clock, throughput, peak memory (parse-only)

| Repository | Configuration | Median (s) | MAD (s) | MAD/median | Files/s | MB/s | Peak working set (MiB) | Peak commit (MiB) |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| vscode | tsc6-parse | 12.13 | 0.1455 | 1.20 % | 967 | 11.6 | 3192 | 3224 |
| vscode | tsgo7-parse | 1.052 | 0.0146 | 1.38 % | 11143 | 134.2 | 2187 | 2221 |
| vscode | tsgo7-parse-single | 4.999 | 0.0252 | 0.50 % | 2345 | 28.2 | 2108 | 2182 |
| vscode | cs-aot-ws | 8.844 | 0.2504 | 2.83 % | 1326 | 16.0 | 2635 | 2645 |
| vscode | cs-aot-ws-single | 12.76 | 0.0223 | 0.17 % | 919 | 11.1 | 2637 | 2644 |
| vscode | cs-aot-srv | 3.986 | 0.1817 | 4.56 % | 2941 | 35.4 | 2988 | 3104 |
| vscode | cs-aot-srv-single | 10.21 | 0.1287 | 1.26 % | 1148 | 13.8 | 3022 | 3232 |
| vscode | cs-jit-ws | 7.470 | 0.0923 | 1.24 % | 1569 | 18.9 | 2709 | 2690 |
| vscode | cs-jit-ws-single | 11.73 | 0.0746 | 0.64 % | 999 | 12.0 | 2676 | 2662 |
| vscode | cs-jit-srv | 3.547 | 0.1634 | 4.61 % | 3306 | 39.8 | 2977 | 3225 |
| vscode | cs-jit-srv-single | 8.645 | 0.1868 | 2.16 % | 1356 | 16.3 | 3074 | 3325 |
| sentry | tsc6-parse | 4.824 | 0.1210 | 2.51 % | 1852 | 9.5 | 1207 | 1208 |
| sentry | tsgo7-parse | 0.4002 | 0.0077 | 1.92 % | 22319 | 114.8 | 803 | 824 |
| sentry | tsgo7-parse-single | 2.293 | 0.0081 | 0.35 % | 3895 | 20.0 | 761 | 777 |
| sentry | cs-aot-ws | 2.870 | 0.1257 | 4.38 % | 3112 | 16.0 | 916 | 914 |
| sentry | cs-aot-ws-single | 4.619 | 0.0139 | 0.30 % | 1934 | 9.9 | 913 | 912 |
| sentry | cs-aot-srv | 1.406 | 0.0907 | 6.45 % | 6353 | 32.7 | 1050 | 1076 |
| sentry | cs-aot-srv-single | 3.626 | 0.0162 | 0.45 % | 2463 | 12.7 | 1066 | 1110 |
| sentry | cs-jit-ws | 2.678 | 0.0397 | 1.48 % | 3335 | 17.2 | 952 | 933 |
| sentry | cs-jit-ws-single | 5.604 | 0.2028 | 3.62 % | 1594 | 8.2 | 951 | 930 |
| sentry | cs-jit-srv | 1.573 | 0.0437 | 2.78 % | 5677 | 29.2 | 1101 | 1163 |
| sentry | cs-jit-srv-single | 4.801 | 0.1877 | 3.91 % | 1861 | 9.6 | 1099 | 1225 |
| bluesky | tsc6-parse | 1.236 | 0.0072 | 0.58 % | 1365 | 5.8 | 359 | 340 |
| bluesky | tsgo7-parse | 0.1075 | 0.0027 | 2.52 % | 15709 | 66.9 | 161 | 172 |
| bluesky | tsgo7-parse-single | 0.4288 | 0.0038 | 0.89 % | 3937 | 16.8 | 143 | 151 |
| bluesky | cs-aot-ws | 0.4877 | 0.0065 | 1.33 % | 3461 | 14.7 | 168 | 162 |
| bluesky | cs-aot-ws-single | 0.8393 | 0.0040 | 0.48 % | 2011 | 8.6 | 167 | 165 |
| bluesky | cs-aot-srv | 0.1882 | 0.0021 | 1.11 % | 8968 | 38.2 | 197 | 205 |
| bluesky | cs-aot-srv-single | 0.5994 | 0.0061 | 1.01 % | 2816 | 12.0 | 190 | 199 |
| bluesky | cs-jit-ws | 0.5691 | 0.0069 | 1.21 % | 2966 | 12.6 | 206 | 186 |
| bluesky | cs-jit-ws-single | 1.439 | 0.0124 | 0.86 % | 1173 | 5.0 | 208 | 184 |
| bluesky | cs-jit-srv | 0.3620 | 0.0052 | 1.44 % | 4663 | 19.9 | 235 | 226 |
| bluesky | cs-jit-srv-single | 1.199 | 0.0044 | 0.36 % | 1408 | 6.0 | 226 | 245 |
| playwright | tsc6-parse | 1.791 | 0.0211 | 1.18 % | 790 | 9.6 | 548 | 534 |
| playwright | tsgo7-parse | 0.1462 | 0.0038 | 2.61 % | 9676 | 117.8 | 270 | 284 |
| playwright | tsgo7-parse-single | 0.5994 | 0.0037 | 0.61 % | 2361 | 28.8 | 256 | 265 |
| playwright | cs-aot-ws | 0.9032 | 0.0533 | 5.90 % | 1567 | 19.1 | 323 | 318 |
| playwright | cs-aot-ws-single | 1.506 | 0.0098 | 0.65 % | 940 | 11.4 | 320 | 320 |
| playwright | cs-aot-srv | 0.4132 | 0.0212 | 5.12 % | 3424 | 41.7 | 360 | 386 |
| playwright | cs-aot-srv-single | 1.075 | 0.0188 | 1.75 % | 1317 | 16.0 | 360 | 390 |
| playwright | cs-jit-ws | 0.9612 | 0.0120 | 1.25 % | 1472 | 17.9 | 362 | 338 |
| playwright | cs-jit-ws-single | 2.511 | 0.0327 | 1.30 % | 563 | 6.9 | 358 | 334 |
| playwright | cs-jit-srv | 0.5723 | 0.0233 | 4.07 % | 2473 | 30.1 | 416 | 467 |
| playwright | cs-jit-srv-single | 2.155 | 0.0286 | 1.33 % | 657 | 8.0 | 409 | 422 |
| tldraw | tsc6-parse | 1.874 | 0.0182 | 0.97 % | 1339 | 7.3 | 543 | 520 |
| tldraw | tsgo7-parse | 0.1535 | 0.0032 | 2.06 % | 16341 | 88.8 | 272 | 285 |
| tldraw | tsgo7-parse-single | 0.7130 | 0.0056 | 0.79 % | 3517 | 19.1 | 257 | 268 |
| tldraw | cs-aot-ws | 0.9080 | 0.0156 | 1.71 % | 2762 | 15.0 | 310 | 309 |
| tldraw | cs-aot-ws-single | 1.586 | 0.0107 | 0.68 % | 1581 | 8.6 | 306 | 303 |
| tldraw | cs-aot-srv | 0.4853 | 0.0379 | 7.80 % | 5168 | 28.1 | 357 | 403 |
| tldraw | cs-aot-srv-single | 1.202 | 0.0040 | 0.33 % | 2087 | 11.3 | 352 | 398 |
| tldraw | cs-jit-ws | 0.9722 | 0.0148 | 1.52 % | 2580 | 14.0 | 347 | 325 |
| tldraw | cs-jit-ws-single | 2.407 | 0.0594 | 2.47 % | 1042 | 5.7 | 343 | 319 |
| tldraw | cs-jit-srv | 0.5673 | 0.0206 | 3.64 % | 4421 | 24.0 | 393 | 408 |
| tldraw | cs-jit-srv-single | 2.175 | 0.0518 | 2.38 % | 1153 | 6.3 | 388 | 403 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 11.52 [11.39, 11.72] | 12.05 [11.76, 12.45] | 11.51 [11.30, 11.75] | 12.25 [12.10, 12.54] | 12.21 [12.01, 12.46] | 11.90 [11.82, 12.04] |
| tsgo7-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-parse-single | 4.75 [4.71, 4.83] | 5.73 [5.62, 5.79] | 3.99 [3.90, 4.07] | 4.10 [4.05, 4.19] | 4.65 [4.57, 4.74] | 4.60 [4.57, 4.64] |
| cs-aot-ws | 8.41 [8.19, 8.64] | 7.17 [6.93, 7.45] | 4.54 [4.44, 4.65] | 6.18 [5.78, 6.46] | 5.92 [5.79, 6.11] | 6.31 [6.20, 6.41] |
| cs-aot-ws-single | 12.12 [12.04, 12.32] | 11.54 [11.32, 11.66] | 7.81 [7.67, 7.98] | 10.30 [10.16, 10.52] | 10.33 [10.18, 10.55] | 10.31 [10.24, 10.39] |
| cs-aot-srv | 3.79 [3.66, 3.94] | 3.51 [3.39, 3.73] | 1.75 [1.72, 1.79] | 2.83 [2.73, 3.23] | 3.16 [2.97, 3.42] | 2.91 [2.86, 3.01] |
| cs-aot-srv-single | 9.71 [9.33, 9.89] | 9.06 [8.88, 9.17] | 5.58 [5.45, 5.70] | 7.35 [7.18, 7.50] | 7.83 [7.72, 7.99] | 7.77 [7.68, 7.83] |
| cs-jit-ws | 7.10 [6.98, 7.22] | 6.69 [6.53, 6.81] | 5.30 [5.18, 5.42] | 6.57 [6.46, 6.74] | 6.33 [6.23, 6.49] | 6.37 [6.31, 6.43] |
| cs-jit-ws-single | 11.15 [11.04, 11.34] | 14.00 [13.47, 14.63] | 13.39 [13.11, 13.67] | 17.17 [16.90, 17.59] | 15.69 [15.46, 16.22] | 14.13 [13.99, 14.33] |
| cs-jit-srv | 3.37 [3.28, 3.64] | 3.93 [3.82, 4.03] | 3.37 [3.31, 3.46] | 3.91 [3.83, 4.09] | 3.70 [3.57, 3.82] | 3.65 [3.61, 3.72] |
| cs-jit-srv-single | 8.22 [8.08, 8.41] | 12.00 [11.57, 12.31] | 11.16 [10.95, 11.45] | 14.74 [14.46, 15.07] | 14.17 [13.71, 14.48] | 11.81 [11.68, 11.93] |

## Table 3. Variance gate (MAD/median < 3 %)

Binding rows (rig check): tsc6-parse, tsgo7-parse-single and the primary single-threaded C# configuration of each track. Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md, B11 as amended by prereg/v4).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | rig check | 1.20 % | 2.51 % | 0.58 % | 1.18 % | 0.97 % | 2.51 % |
| tsgo7-parse | reported | 1.38 % | 1.92 % | 2.52 % | 2.61 % | 2.06 % | 2.61 % |
| tsgo7-parse-single | rig check | 0.50 % | 0.35 % | 0.89 % | 0.61 % | 0.79 % | 0.89 % |
| cs-aot-ws | reported | 2.83 % | 4.38 % | 1.33 % | 5.90 % | 1.71 % | 5.90 % |
| cs-aot-ws-single | rig check | 0.17 % | 0.30 % | 0.48 % | 0.65 % | 0.68 % | 0.68 % |
| cs-aot-srv | reported | 4.56 % | 6.45 % | 1.11 % | 5.12 % | 7.80 % | 7.80 % |
| cs-aot-srv-single | reported | 1.26 % | 0.45 % | 1.01 % | 1.75 % | 0.33 % | 1.75 % |
| cs-jit-ws | reported | 1.24 % | 1.48 % | 1.21 % | 1.25 % | 1.52 % | 1.52 % |
| cs-jit-ws-single | reported | 0.64 % | 3.62 % | 0.86 % | 1.30 % | 2.47 % | 3.62 % |
| cs-jit-srv | reported | 4.61 % | 2.78 % | 1.44 % | 4.07 % | 3.64 % | 4.61 % |
| cs-jit-srv-single | reported | 2.16 % | 3.91 % | 0.36 % | 1.33 % | 2.38 % | 3.91 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsc6-parse | 0.2644 | 0.63 % | 21.4 % | 88 |
| tsgo7-parse | 0.0204 | 1.40 % | 19.0 % | 9 |
| tsgo7-parse-single | 0.0209 | 1.27 % | 4.9 % | 9 |
| cs-aot-ws | 0.0238 | 1.15 % | 4.9 % | 10 |
| cs-aot-ws-single | 0.0237 | 1.16 % | 2.8 % | 10 |
| cs-aot-srv | 0.0283 | 2.81 % | 15.0 % | 12 |
| cs-aot-srv-single | 0.0276 | 1.87 % | 4.6 % | 12 |
| cs-jit-ws | 0.0782 | 1.53 % | 13.7 % | 26 |
| cs-jit-ws-single | 0.0769 | 1.18 % | 5.3 % | 26 |
| cs-jit-srv | 0.0860 | 4.00 % | 23.8 % | 28 |
| cs-jit-srv-single | 0.0883 | 5.17 % | 7.4 % | 28 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-aot-ws | 3278179008 | 391/358/7 | 7130.1 |
| vscode | cs-aot-ws-single | 3274805144 | 382/346/8 | 4921.6 |
| vscode | cs-aot-srv | 3277829576 | 26/11/5 | 3151.1 |
| vscode | cs-aot-srv-single | 3274782416 | 23/12/7 | 2114.7 |
| vscode | cs-jit-ws | 3283592832 | 387/349/3 | 4881.2 |
| vscode | cs-jit-ws-single | 3268353048 | 381/348/8 | 3842.3 |
| vscode | cs-jit-srv | 3283611888 | 53/18/3 | 1578.4 |
| vscode | cs-jit-srv-single | 3269261688 | 22/10/6 | 1695.7 |
| sentry | cs-aot-ws | 1094870416 | 138/136/6 | 2501.9 |
| sentry | cs-aot-ws-single | 1092458368 | 135/134/6 | 1393.0 |
| sentry | cs-aot-srv | 1094810784 | 17/7/4 | 983.1 |
| sentry | cs-aot-srv-single | 1092432096 | 17/7/4 | 725.6 |
| sentry | cs-jit-ws | 1098149056 | 136/135/2 | 1723.5 |
| sentry | cs-jit-ws-single | 1093935160 | 135/133/6 | 1329.9 |
| sentry | cs-jit-srv | 1097938496 | 15/5/2 | 709.5 |
| sentry | cs-jit-srv-single | 1094108984 | 15/7/4 | 682.4 |
| bluesky | cs-aot-ws | 185036008 | 25/24/3 | 380.1 |
| bluesky | cs-aot-ws-single | 184364488 | 25/24/4 | 223.9 |
| bluesky | cs-aot-srv | 184740304 | 5/2/1 | 77.7 |
| bluesky | cs-aot-srv-single | 184349936 | 5/2/1 | 81.4 |
| bluesky | cs-jit-ws | 186225960 | 23/22/1 | 285.6 |
| bluesky | cs-jit-ws-single | 185716608 | 26/25/4 | 227.0 |
| bluesky | cs-jit-srv | 186154600 | 5/2/1 | 79.8 |
| bluesky | cs-jit-srv-single | 185709592 | 5/2/1 | 107.1 |
| playwright | cs-aot-ws | 378671536 | 47/44/4 | 719.8 |
| playwright | cs-aot-ws-single | 378160608 | 47/45/5 | 438.3 |
| playwright | cs-aot-srv | 378611496 | 10/4/2 | 227.7 |
| playwright | cs-aot-srv-single | 378165776 | 11/6/4 | 231.0 |
| playwright | cs-jit-ws | 380159168 | 45/44/2 | 550.5 |
| playwright | cs-jit-ws-single | 379796480 | 47/44/5 | 427.8 |
| playwright | cs-jit-srv | 380135080 | 11/6/3 | 209.5 |
| playwright | cs-jit-srv-single | 379781432 | 10/5/3 | 161.7 |
| tldraw | cs-aot-ws | 356522512 | 47/44/4 | 786.2 |
| tldraw | cs-aot-ws-single | 355451064 | 46/43/5 | 428.1 |
| tldraw | cs-aot-srv | 356182072 | 12/5/2 | 342.0 |
| tldraw | cs-aot-srv-single | 355442280 | 11/5/2 | 289.6 |
| tldraw | cs-jit-ws | 357701736 | 45/42/2 | 556.3 |
| tldraw | cs-jit-ws-single | 356908168 | 46/43/5 | 432.8 |
| tldraw | cs-jit-srv | 357522928 | 9/3/2 | 154.0 |
| tldraw | cs-jit-srv-single | 356897200 | 9/3/2 | 236.8 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (55 files).
