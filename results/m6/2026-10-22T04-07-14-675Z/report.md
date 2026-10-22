written artifacts\bench\results\2026-10-22T04-07-14-675Z\report.md
tocol result.** All rig checks passed.

Label: m6-track-f

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 8.3 % (median of 1 s windows: 14.5, 9.2, 9.2, 7.5, 9.9, 10, 2.2, 3.3, 2.1, 7) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, hyperfine 1.20.0 |
| Harness | commit 7f2baa8991, pins.json sha256 fb8d8763ad34… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 8.3 % (range 2.1-14.5 %) | ok |
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
| vscode | tsc6-parse | 12.05 | 0.1134 | 0.94 % | 973 | 11.7 | 3182 | 3223 |
| vscode | tsgo7-parse | 1.059 | 0.0130 | 1.23 % | 11070 | 133.3 | 2172 | 2217 |
| vscode | tsgo7-parse-single | 5.004 | 0.0175 | 0.35 % | 2343 | 28.2 | 2112 | 2177 |
| vscode | cs-aot-ws | 8.525 | 0.3452 | 4.05 % | 1375 | 16.6 | 2632 | 2645 |
| vscode | cs-aot-ws-single | 12.86 | 0.0937 | 0.73 % | 912 | 11.0 | 2636 | 2645 |
| vscode | cs-aot-srv | 3.931 | 0.2357 | 6.00 % | 2982 | 35.9 | 2957 | 3031 |
| vscode | cs-aot-srv-single | 10.08 | 0.3583 | 3.55 % | 1163 | 14.0 | 2975 | 3113 |
| vscode | cs-jit-ws | 7.493 | 0.1630 | 2.18 % | 1565 | 18.8 | 2694 | 2677 |
| vscode | cs-jit-ws-single | 11.88 | 0.1209 | 1.02 % | 987 | 11.9 | 2671 | 2658 |
| vscode | cs-jit-srv | 3.779 | 0.2023 | 5.35 % | 3102 | 37.4 | 2993 | 3157 |
| vscode | cs-jit-srv-single | 8.870 | 0.1347 | 1.52 % | 1322 | 15.9 | 3075 | 3320 |
| sentry | tsc6-parse | 4.946 | 0.0530 | 1.07 % | 1806 | 9.3 | 1215 | 1208 |
| sentry | tsgo7-parse | 0.4126 | 0.0112 | 2.73 % | 21648 | 111.3 | 792 | 817 |
| sentry | tsgo7-parse-single | 2.282 | 0.0132 | 0.58 % | 3914 | 20.1 | 765 | 776 |
| sentry | cs-aot-ws | 2.910 | 0.0578 | 1.99 % | 3069 | 15.8 | 915 | 912 |
| sentry | cs-aot-ws-single | 4.644 | 0.0227 | 0.49 % | 1923 | 9.9 | 912 | 910 |
| sentry | cs-aot-srv | 1.451 | 0.0772 | 5.32 % | 6156 | 31.7 | 1042 | 1076 |
| sentry | cs-aot-srv-single | 3.647 | 0.0318 | 0.87 % | 2449 | 12.6 | 1063 | 1110 |
| sentry | cs-jit-ws | 2.652 | 0.0355 | 1.34 % | 3368 | 17.3 | 953 | 933 |
| sentry | cs-jit-ws-single | 5.584 | 0.1878 | 3.36 % | 1600 | 8.2 | 950 | 930 |
| sentry | cs-jit-srv | 1.601 | 0.0520 | 3.25 % | 5579 | 28.7 | 1110 | 1201 |
| sentry | cs-jit-srv-single | 4.619 | 0.1714 | 3.71 % | 1934 | 9.9 | 1102 | 1225 |
| bluesky | tsc6-parse | 1.246 | 0.0081 | 0.65 % | 1354 | 5.8 | 367 | 340 |
| bluesky | tsgo7-parse | 0.1031 | 0.0021 | 2.09 % | 16380 | 69.8 | 158 | 171 |
| bluesky | tsgo7-parse-single | 0.4315 | 0.0062 | 1.43 % | 3912 | 16.7 | 142 | 152 |
| bluesky | cs-aot-ws | 0.4891 | 0.0103 | 2.11 % | 3451 | 14.7 | 167 | 164 |
| bluesky | cs-aot-ws-single | 0.8419 | 0.0059 | 0.70 % | 2005 | 8.5 | 166 | 165 |
| bluesky | cs-aot-srv | 0.1798 | 0.0075 | 4.18 % | 9387 | 40.0 | 196 | 204 |
| bluesky | cs-aot-srv-single | 0.5972 | 0.0022 | 0.37 % | 2827 | 12.0 | 189 | 199 |
| bluesky | cs-jit-ws | 0.5658 | 0.0117 | 2.06 % | 2983 | 12.7 | 206 | 186 |
| bluesky | cs-jit-ws-single | 1.430 | 0.0099 | 0.69 % | 1180 | 5.0 | 207 | 184 |
| bluesky | cs-jit-srv | 0.3620 | 0.0105 | 2.90 % | 4662 | 19.9 | 234 | 226 |
| bluesky | cs-jit-srv-single | 1.213 | 0.0267 | 2.21 % | 1392 | 5.9 | 225 | 245 |
| playwright | tsc6-parse | 1.794 | 0.0118 | 0.66 % | 789 | 9.6 | 556 | 534 |
| playwright | tsgo7-parse | 0.1411 | 0.0036 | 2.57 % | 10026 | 122.1 | 270 | 282 |
| playwright | tsgo7-parse-single | 0.5925 | 0.0038 | 0.64 % | 2388 | 29.1 | 255 | 265 |
| playwright | cs-aot-ws | 0.9449 | 0.0370 | 3.92 % | 1498 | 18.2 | 321 | 317 |
| playwright | cs-aot-ws-single | 1.511 | 0.0092 | 0.61 % | 936 | 11.4 | 319 | 319 |
| playwright | cs-aot-srv | 0.4189 | 0.0228 | 5.45 % | 3378 | 41.1 | 377 | 402 |
| playwright | cs-aot-srv-single | 1.054 | 0.0205 | 1.95 % | 1343 | 16.4 | 365 | 399 |
| playwright | cs-jit-ws | 0.9624 | 0.0144 | 1.50 % | 1470 | 17.9 | 360 | 339 |
| playwright | cs-jit-ws-single | 2.507 | 0.0514 | 2.05 % | 565 | 6.9 | 359 | 338 |
| playwright | cs-jit-srv | 0.5934 | 0.0131 | 2.21 % | 2385 | 29.0 | 414 | 468 |
| playwright | cs-jit-srv-single | 2.170 | 0.0199 | 0.92 % | 652 | 7.9 | 407 | 420 |
| tldraw | tsc6-parse | 1.876 | 0.0110 | 0.59 % | 1337 | 7.3 | 543 | 521 |
| tldraw | tsgo7-parse | 0.1517 | 0.0030 | 1.97 % | 16531 | 89.8 | 270 | 285 |
| tldraw | tsgo7-parse-single | 0.7079 | 0.0057 | 0.81 % | 3543 | 19.2 | 256 | 268 |
| tldraw | cs-aot-ws | 0.9362 | 0.0217 | 2.32 % | 2679 | 14.6 | 307 | 304 |
| tldraw | cs-aot-ws-single | 1.573 | 0.0068 | 0.43 % | 1594 | 8.7 | 305 | 303 |
| tldraw | cs-aot-srv | 0.4880 | 0.0426 | 8.73 % | 5139 | 27.9 | 355 | 400 |
| tldraw | cs-aot-srv-single | 1.215 | 0.0147 | 1.21 % | 2065 | 11.2 | 345 | 384 |
| tldraw | cs-jit-ws | 0.9674 | 0.0097 | 1.00 % | 2593 | 14.1 | 344 | 326 |
| tldraw | cs-jit-ws-single | 2.411 | 0.0624 | 2.59 % | 1040 | 5.7 | 341 | 320 |
| tldraw | cs-jit-srv | 0.5457 | 0.0131 | 2.40 % | 4596 | 25.0 | 392 | 405 |
| tldraw | cs-jit-srv-single | 2.136 | 0.0760 | 3.56 % | 1174 | 6.4 | 384 | 403 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 11.38 [11.23, 11.48] | 11.99 [11.70, 12.35] | 12.10 [11.90, 12.27] | 12.71 [12.44, 12.99] | 12.37 [12.20, 12.57] | 12.10 [11.99, 12.20] |
| tsgo7-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-parse-single | 4.72 [4.66, 4.75] | 5.53 [5.42, 5.70] | 4.19 [4.11, 4.26] | 4.20 [4.10, 4.28] | 4.67 [4.60, 4.74] | 4.64 [4.60, 4.67] |
| cs-aot-ws | 8.05 [7.70, 8.27] | 7.05 [6.81, 7.28] | 4.75 [4.66, 4.87] | 6.69 [6.26, 6.89] | 6.17 [6.05, 6.37] | 6.45 [6.33, 6.53] |
| cs-aot-ws-single | 12.14 [11.86, 12.21] | 11.26 [11.01, 11.59] | 8.17 [8.03, 8.31] | 10.71 [10.47, 10.95] | 10.37 [10.26, 10.56] | 10.44 [10.34, 10.53] |
| cs-aot-srv | 3.71 [3.51, 4.05] | 3.52 [3.41, 3.73] | 1.74 [1.68, 1.83] | 2.97 [2.85, 3.28] | 3.22 [2.38, 3.38] | 2.93 [2.77, 3.03] |
| cs-aot-srv-single | 9.52 [9.25, 9.87] | 8.84 [8.66, 9.10] | 5.79 [5.70, 5.87] | 7.47 [7.25, 7.66] | 8.01 [7.89, 8.15] | 7.81 [7.73, 7.90] |
| cs-jit-ws | 7.08 [6.89, 7.17] | 6.43 [6.28, 6.68] | 5.49 [5.38, 5.62] | 6.82 [6.66, 6.98] | 6.38 [6.29, 6.50] | 6.41 [6.35, 6.48] |
| cs-jit-ws-single | 11.22 [11.02, 11.31] | 13.53 [13.11, 14.21] | 13.88 [13.65, 14.07] | 17.76 [17.16, 18.15] | 15.89 [15.61, 17.06] | 14.29 [14.13, 14.52] |
| cs-jit-srv | 3.57 [3.37, 3.69] | 3.88 [3.76, 4.03] | 3.51 [3.45, 3.65] | 4.20 [4.07, 4.32] | 3.60 [3.53, 3.71] | 3.74 [3.69, 3.80] |
| cs-jit-srv-single | 8.38 [8.22, 8.48] | 11.19 [10.79, 11.69] | 11.77 [11.55, 12.49] | 15.37 [14.99, 15.74] | 14.08 [13.68, 14.74] | 11.90 [11.77, 12.13] |

## Table 3. Variance gate (MAD/median < 3 %)

Binding rows: single-threaded configurations (rig check). Parallel rows: reported spread, not gated; their run-to-run spread is a property of the parallel program (docs/benchmark-harness.md, B11).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | rig check | 0.94 % | 1.07 % | 0.65 % | 0.66 % | 0.59 % | 1.07 % |
| tsgo7-parse | reported | 1.23 % | 2.73 % | 2.09 % | 2.57 % | 1.97 % | 2.73 % |
| tsgo7-parse-single | rig check | 0.35 % | 0.58 % | 1.43 % | 0.64 % | 0.81 % | 1.43 % |
| cs-aot-ws | reported | 4.05 % | 1.99 % | 2.11 % | 3.92 % | 2.32 % | 4.05 % |
| cs-aot-ws-single | rig check | 0.73 % | 0.49 % | 0.70 % | 0.61 % | 0.43 % | 0.73 % |
| cs-aot-srv | reported | 6.00 % | 5.32 % | 4.18 % | 5.45 % | 8.73 % | 8.73 % |
| cs-aot-srv-single | rig check | **3.55 %** | 0.87 % | 0.37 % | 1.95 % | 1.21 % | **3.55 %** |
| cs-jit-ws | reported | 2.18 % | 1.34 % | 2.06 % | 1.50 % | 1.00 % | 2.18 % |
| cs-jit-ws-single | rig check | 1.02 % | **3.36 %** | 0.69 % | 2.05 % | 2.59 % | **3.36 %** |
| cs-jit-srv | reported | 5.35 % | 3.25 % | 2.90 % | 2.21 % | 2.40 % | 5.35 % |
| cs-jit-srv-single | rig check | 1.52 % | **3.71 %** | 2.21 % | 0.92 % | **3.56 %** | **3.71 %** |

Variance gate (rig check): **FAIL**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsc6-parse | 0.2549 | 0.92 % | 20.4 % | 87 |
| tsgo7-parse | 0.0142 | 2.06 % | 13.7 % | 9 |
| tsgo7-parse-single | 0.0134 | 1.00 % | 3.1 % | 9 |
| cs-aot-ws | 0.0152 | 1.09 % | 3.1 % | 9 |
| cs-aot-ws-single | 0.0174 | 11.34 % | 2.1 % | 9 |
| cs-aot-srv | 0.0542 | 43.30 % | 30.1 % | 11 |
| cs-aot-srv-single | 0.0441 | 26.51 % | 7.4 % | 11 |
| cs-jit-ws | 0.0728 | 1.76 % | 12.9 % | 25 |
| cs-jit-ws-single | 0.0678 | 1.33 % | 4.7 % | 25 |
| cs-jit-srv | 0.0801 | 7.16 % | 22.1 % | 27 |
| cs-jit-srv-single | 0.0764 | 3.53 % | 6.3 % | 27 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-aot-ws | 3278330344 | 391/348/7 | 7528.2 |
| vscode | cs-aot-ws-single | 3274797760 | 382/346/8 | 4966.5 |
| vscode | cs-aot-srv | 3278663304 | 25/9/5 | 3154.1 |
| vscode | cs-aot-srv-single | 3274792256 | 24/12/7 | 2624.5 |
| vscode | cs-jit-ws | 3283587792 | 387/353/3 | 5001.7 |
| vscode | cs-jit-ws-single | 3268343088 | 381/347/8 | 3871.5 |
| vscode | cs-jit-srv | 3283485264 | 52/17/3 | 1433.5 |
| vscode | cs-jit-srv-single | 3268191976 | 22/10/6 | 1694.2 |
| sentry | cs-aot-ws | 1094945768 | 139/137/6 | 2351.4 |
| sentry | cs-aot-ws-single | 1092453408 | 135/134/6 | 1466.7 |
| sentry | cs-aot-srv | 1094486640 | 17/7/4 | 996.4 |
| sentry | cs-aot-srv-single | 1092437280 | 16/7/4 | 704.1 |
| sentry | cs-jit-ws | 1098032680 | 135/134/2 | 1713.6 |
| sentry | cs-jit-ws-single | 1093177528 | 135/133/6 | 1343.5 |
| sentry | cs-jit-srv | 1097982344 | 14/4/2 | 592.5 |
| sentry | cs-jit-srv-single | 1095949232 | 15/7/4 | 691.8 |
| bluesky | cs-aot-ws | 184878504 | 25/24/4 | 318.2 |
| bluesky | cs-aot-ws-single | 184368440 | 25/24/4 | 220.2 |
| bluesky | cs-aot-srv | 184728920 | 5/2/1 | 83.6 |
| bluesky | cs-aot-srv-single | 184349936 | 5/2/1 | 66.8 |
| bluesky | cs-jit-ws | 186228824 | 23/22/1 | 287.4 |
| bluesky | cs-jit-ws-single | 185722624 | 26/25/4 | 227.1 |
| bluesky | cs-jit-srv | 186245120 | 5/2/1 | 74.7 |
| bluesky | cs-jit-srv-single | 185709592 | 5/2/1 | 102.9 |
| playwright | cs-aot-ws | 378685312 | 46/45/3 | 640.4 |
| playwright | cs-aot-ws-single | 378159472 | 47/45/5 | 461.0 |
| playwright | cs-aot-srv | 378578704 | 11/5/3 | 209.8 |
| playwright | cs-aot-srv-single | 378171496 | 11/6/4 | 232.8 |
| playwright | cs-jit-ws | 380144048 | 45/44/2 | 565.4 |
| playwright | cs-jit-ws-single | 379794960 | 47/44/5 | 439.6 |
| playwright | cs-jit-srv | 380128608 | 11/5/3 | 171.2 |
| playwright | cs-jit-srv-single | 379781000 | 10/5/3 | 163.2 |
| tldraw | cs-aot-ws | 356320344 | 46/42/4 | 853.3 |
| tldraw | cs-aot-ws-single | 355451952 | 46/43/5 | 449.8 |
| tldraw | cs-aot-srv | 356132888 | 12/5/2 | 351.2 |
| tldraw | cs-aot-srv-single | 355441544 | 11/5/2 | 266.9 |
| tldraw | cs-jit-ws | 357713816 | 45/41/2 | 566.0 |
| tldraw | cs-jit-ws-single | 356905744 | 46/43/5 | 437.1 |
| tldraw | cs-jit-srv | 357717312 | 7/3/2 | 161.3 |
| tldraw | cs-jit-srv-single | 356893640 | 9/3/2 | 230.4 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (55 files).
