# Parse-only benchmark, run 2026-10-22T05-35-48-155Z

**Status: protocol result.** All rig checks passed.

Label: m6-track-f-run2

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 9.2 % (median of 1 s windows: 35.3, 17.4, 13.3, 10.1, 5.1, 5.1, 6.5, 8.3, 12.9, 7.1) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, hyperfine 1.20.0 |
| Harness | commit b3fd637836, pins.json sha256 fb8d8763ad34… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 9.2 % (range 5.1-35.3 %) | ok |
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
| vscode | tsc6-parse | 12.14 | 0.1131 | 0.93 % | 966 | 11.6 | 3193 | 3224 |
| vscode | tsgo7-parse | 1.052 | 0.0199 | 1.89 % | 11147 | 134.2 | 2176 | 2213 |
| vscode | tsgo7-parse-single | 5.035 | 0.0209 | 0.42 % | 2329 | 28.0 | 2121 | 2170 |
| vscode | cs-aot-ws | 8.609 | 0.2696 | 3.13 % | 1362 | 16.4 | 2637 | 2643 |
| vscode | cs-aot-ws-single | 12.79 | 0.0477 | 0.37 % | 916 | 11.0 | 2637 | 2644 |
| vscode | cs-aot-srv | 3.842 | 0.2134 | 5.55 % | 3052 | 36.7 | 3087 | 3216 |
| vscode | cs-aot-srv-single | 10.31 | 0.2759 | 2.68 % | 1137 | 13.7 | 2981 | 3118 |
| vscode | cs-jit-ws | 7.497 | 0.1359 | 1.81 % | 1564 | 18.8 | 2707 | 2688 |
| vscode | cs-jit-ws-single | 11.77 | 0.1005 | 0.85 % | 996 | 12.0 | 2678 | 2665 |
| vscode | cs-jit-srv | 3.752 | 0.1549 | 4.13 % | 3125 | 37.6 | 2943 | 3181 |
| vscode | cs-jit-srv-single | 8.800 | 0.1335 | 1.52 % | 1332 | 16.0 | 3076 | 3349 |
| sentry | tsc6-parse | 4.960 | 0.0325 | 0.66 % | 1801 | 9.3 | 1216 | 1208 |
| sentry | tsgo7-parse | 0.4199 | 0.0090 | 2.14 % | 21272 | 109.4 | 809 | 829 |
| sentry | tsgo7-parse-single | 2.296 | 0.0092 | 0.40 % | 3890 | 20.0 | 766 | 779 |
| sentry | cs-aot-ws | 2.982 | 0.0733 | 2.46 % | 2995 | 15.4 | 915 | 914 |
| sentry | cs-aot-ws-single | 4.653 | 0.0158 | 0.34 % | 1920 | 9.9 | 913 | 910 |
| sentry | cs-aot-srv | 1.431 | 0.0356 | 2.49 % | 6240 | 32.1 | 1041 | 1072 |
| sentry | cs-aot-srv-single | 3.687 | 0.0254 | 0.69 % | 2422 | 12.5 | 1042 | 1110 |
| sentry | cs-jit-ws | 2.651 | 0.0372 | 1.40 % | 3370 | 17.3 | 956 | 936 |
| sentry | cs-jit-ws-single | 5.429 | 0.0804 | 1.48 % | 1645 | 8.5 | 953 | 932 |
| sentry | cs-jit-srv | 1.613 | 0.0319 | 1.98 % | 5538 | 28.5 | 1094 | 1154 |
| sentry | cs-jit-srv-single | 4.606 | 0.2341 | 5.08 % | 1939 | 10.0 | 1102 | 1225 |
| bluesky | tsc6-parse | 1.248 | 0.0061 | 0.49 % | 1353 | 5.8 | 368 | 341 |
| bluesky | tsgo7-parse | 0.1073 | 0.0023 | 2.18 % | 15733 | 67.1 | 159 | 171 |
| bluesky | tsgo7-parse-single | 0.4307 | 0.0031 | 0.72 % | 3919 | 16.7 | 143 | 151 |
| bluesky | cs-aot-ws | 0.4838 | 0.0111 | 2.30 % | 3489 | 14.9 | 167 | 163 |
| bluesky | cs-aot-ws-single | 0.8523 | 0.0143 | 1.68 % | 1981 | 8.4 | 167 | 165 |
| bluesky | cs-aot-srv | 0.1881 | 0.0019 | 1.02 % | 8973 | 38.2 | 197 | 205 |
| bluesky | cs-aot-srv-single | 0.6056 | 0.0047 | 0.78 % | 2787 | 11.9 | 190 | 199 |
| bluesky | cs-jit-ws | 0.5703 | 0.0125 | 2.19 % | 2960 | 12.6 | 206 | 187 |
| bluesky | cs-jit-ws-single | 1.447 | 0.0137 | 0.95 % | 1166 | 5.0 | 208 | 184 |
| bluesky | cs-jit-srv | 0.3656 | 0.0057 | 1.57 % | 4617 | 19.7 | 235 | 227 |
| bluesky | cs-jit-srv-single | 1.198 | 0.0029 | 0.24 % | 1409 | 6.0 | 226 | 245 |
| playwright | tsc6-parse | 1.817 | 0.0090 | 0.49 % | 779 | 9.5 | 558 | 535 |
| playwright | tsgo7-parse | 0.1462 | 0.0042 | 2.84 % | 9676 | 117.8 | 270 | 288 |
| playwright | tsgo7-parse-single | 0.5975 | 0.0025 | 0.42 % | 2368 | 28.8 | 255 | 264 |
| playwright | cs-aot-ws | 0.9373 | 0.0328 | 3.50 % | 1510 | 18.4 | 322 | 319 |
| playwright | cs-aot-ws-single | 1.491 | 0.0126 | 0.85 % | 949 | 11.6 | 320 | 319 |
| playwright | cs-aot-srv | 0.4016 | 0.0091 | 2.28 % | 3524 | 42.9 | 362 | 387 |
| playwright | cs-aot-srv-single | 1.085 | 0.0186 | 1.72 % | 1304 | 15.9 | 371 | 403 |
| playwright | cs-jit-ws | 0.9694 | 0.0198 | 2.05 % | 1460 | 17.8 | 361 | 339 |
| playwright | cs-jit-ws-single | 2.525 | 0.0211 | 0.84 % | 560 | 6.8 | 359 | 334 |
| playwright | cs-jit-srv | 0.5985 | 0.0318 | 5.31 % | 2364 | 28.8 | 411 | 464 |
| playwright | cs-jit-srv-single | 2.163 | 0.0327 | 1.51 % | 654 | 8.0 | 409 | 421 |
| tldraw | tsc6-parse | 1.876 | 0.0172 | 0.92 % | 1337 | 7.3 | 543 | 520 |
| tldraw | tsgo7-parse | 0.1572 | 0.0048 | 3.07 % | 15954 | 86.7 | 274 | 286 |
| tldraw | tsgo7-parse-single | 0.7123 | 0.0053 | 0.75 % | 3521 | 19.1 | 257 | 268 |
| tldraw | cs-aot-ws | 0.9172 | 0.0222 | 2.42 % | 2734 | 14.9 | 310 | 308 |
| tldraw | cs-aot-ws-single | 1.583 | 0.0079 | 0.50 % | 1584 | 8.6 | 306 | 303 |
| tldraw | cs-aot-srv | 0.4979 | 0.0302 | 6.07 % | 5037 | 27.4 | 357 | 406 |
| tldraw | cs-aot-srv-single | 1.199 | 0.0057 | 0.48 % | 2093 | 11.4 | 345 | 384 |
| tldraw | cs-jit-ws | 0.9765 | 0.0107 | 1.10 % | 2568 | 14.0 | 351 | 327 |
| tldraw | cs-jit-ws-single | 2.570 | 0.1013 | 3.94 % | 976 | 5.3 | 341 | 320 |
| tldraw | cs-jit-srv | 0.5486 | 0.0246 | 4.49 % | 4571 | 24.8 | 382 | 401 |
| tldraw | cs-jit-srv-single | 2.169 | 0.0624 | 2.87 % | 1156 | 6.3 | 385 | 403 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 11.54 [11.29, 11.71] | 11.81 [11.46, 11.93] | 11.63 [11.46, 12.04] | 12.42 [12.18, 12.81] | 11.93 [11.60, 12.25] | 11.86 [11.74, 11.98] |
| tsgo7-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-parse-single | 4.79 [4.69, 4.85] | 5.47 [5.36, 5.53] | 4.01 [3.95, 4.15] | 4.09 [4.00, 4.21] | 4.53 [4.41, 4.65] | 4.55 [4.51, 4.59] |
| cs-aot-ws | 8.19 [7.86, 8.38] | 7.10 [6.91, 7.29] | 4.51 [4.44, 4.72] | 6.41 [6.11, 6.62] | 5.83 [5.64, 6.00] | 6.28 [6.19, 6.38] |
| cs-aot-ws-single | 12.16 [11.92, 12.32] | 11.08 [10.87, 11.21] | 7.94 [7.78, 8.22] | 10.19 [9.99, 10.52] | 10.07 [9.80, 10.31] | 10.19 [10.09, 10.30] |
| cs-aot-srv | 3.65 [3.51, 3.92] | 3.41 [3.25, 3.48] | 1.75 [1.73, 1.82] | 2.75 [2.67, 2.87] | 3.17 [2.69, 3.27] | 2.86 [2.76, 2.91] |
| cs-aot-srv-single | 9.80 [9.41, 10.01] | 8.78 [8.61, 8.88] | 5.64 [5.56, 5.85] | 7.42 [7.21, 7.65] | 7.62 [7.43, 7.82] | 7.72 [7.63, 7.80] |
| cs-jit-ws | 7.13 [6.94, 7.33] | 6.31 [6.18, 6.43] | 5.32 [5.23, 5.54] | 6.63 [6.48, 6.87] | 6.21 [6.03, 6.37] | 6.29 [6.23, 6.37] |
| cs-jit-ws-single | 11.19 [10.95, 11.35] | 12.93 [12.66, 13.13] | 13.49 [13.30, 13.98] | 17.27 [16.87, 17.81] | 16.35 [15.28, 16.92] | 14.07 [13.84, 14.23] |
| cs-jit-srv | 3.57 [3.39, 3.70] | 3.84 [3.71, 3.89] | 3.41 [3.35, 3.54] | 4.09 [3.85, 4.22] | 3.49 [3.36, 3.64] | 3.67 [3.60, 3.72] |
| cs-jit-srv-single | 8.37 [8.16, 8.49] | 10.97 [10.71, 11.65] | 11.16 [11.02, 11.54] | 14.79 [14.50, 15.32] | 13.80 [13.16, 14.19] | 11.59 [11.45, 11.78] |

## Table 3. Variance gate (MAD/median < 3 %)

Binding rows: single-threaded configurations (rig check). Parallel rows: reported spread, not gated; their run-to-run spread is a property of the parallel program (docs/benchmark-harness.md, B11).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | rig check | 0.93 % | 0.66 % | 0.49 % | 0.49 % | 0.92 % | 0.93 % |
| tsgo7-parse | reported | 1.89 % | 2.14 % | 2.18 % | 2.84 % | 3.07 % | 3.07 % |
| tsgo7-parse-single | rig check | 0.42 % | 0.40 % | 0.72 % | 0.42 % | 0.75 % | 0.75 % |
| cs-aot-ws | reported | 3.13 % | 2.46 % | 2.30 % | 3.50 % | 2.42 % | 3.50 % |
| cs-aot-ws-single | rig check | 0.37 % | 0.34 % | 1.68 % | 0.85 % | 0.50 % | 1.68 % |
| cs-aot-srv | reported | 5.55 % | 2.49 % | 1.02 % | 2.28 % | 6.07 % | 6.07 % |
| cs-aot-srv-single | rig check | 2.68 % | 0.69 % | 0.78 % | 1.72 % | 0.48 % | 2.68 % |
| cs-jit-ws | reported | 1.81 % | 1.40 % | 2.19 % | 2.05 % | 1.10 % | 2.19 % |
| cs-jit-ws-single | rig check | 0.85 % | 1.48 % | 0.95 % | 0.84 % | **3.94 %** | **3.94 %** |
| cs-jit-srv | reported | 4.13 % | 1.98 % | 1.57 % | 5.31 % | 4.49 % | 5.31 % |
| cs-jit-srv-single | rig check | 1.52 % | **5.08 %** | 0.24 % | 1.51 % | 2.87 % | **5.08 %** |

Variance gate (rig check): **FAIL**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsc6-parse | 0.2643 | 1.08 % | 21.2 % | 97 |
| tsgo7-parse | 0.0204 | 1.16 % | 19.0 % | 9 |
| tsgo7-parse-single | 0.0203 | 1.28 % | 4.7 % | 9 |
| cs-aot-ws | 0.0229 | 0.91 % | 4.7 % | 10 |
| cs-aot-ws-single | 0.0228 | 0.77 % | 2.7 % | 10 |
| cs-aot-srv | 0.0278 | 1.01 % | 14.8 % | 12 |
| cs-aot-srv-single | 0.0280 | 2.10 % | 4.6 % | 12 |
| cs-jit-ws | 0.0764 | 0.86 % | 13.4 % | 27 |
| cs-jit-ws-single | 0.0765 | 1.11 % | 5.3 % | 27 |
| cs-jit-srv | 0.0821 | 1.88 % | 22.4 % | 28 |
| cs-jit-srv-single | 0.0820 | 1.28 % | 6.8 % | 28 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-aot-ws | 3278341632 | 390/351/6 | 6922.7 |
| vscode | cs-aot-ws-single | 3274805696 | 381/345/8 | 4827.3 |
| vscode | cs-aot-srv | 3278111216 | 25/10/5 | 2184.6 |
| vscode | cs-aot-srv-single | 3274783504 | 26/12/8 | 2558.1 |
| vscode | cs-jit-ws | 3283526440 | 387/352/3 | 4909.0 |
| vscode | cs-jit-ws-single | 3267368552 | 381/347/8 | 3900.2 |
| vscode | cs-jit-srv | 3283554400 | 38/12/3 | 1352.5 |
| vscode | cs-jit-srv-single | 3269933280 | 22/10/6 | 1704.1 |
| sentry | cs-aot-ws | 1094873736 | 139/134/6 | 2472.8 |
| sentry | cs-aot-ws-single | 1092463424 | 135/134/6 | 1433.7 |
| sentry | cs-aot-srv | 1094474512 | 17/7/4 | 1036.2 |
| sentry | cs-aot-srv-single | 1092435200 | 18/8/5 | 741.3 |
| sentry | cs-jit-ws | 1098074288 | 136/135/3 | 1696.3 |
| sentry | cs-jit-ws-single | 1093208536 | 135/133/6 | 1315.1 |
| sentry | cs-jit-srv | 1098002896 | 12/4/2 | 489.4 |
| sentry | cs-jit-srv-single | 1094132816 | 15/7/4 | 677.1 |
| bluesky | cs-aot-ws | 185081008 | 25/24/3 | 367.8 |
| bluesky | cs-aot-ws-single | 184364480 | 24/23/4 | 216.0 |
| bluesky | cs-aot-srv | 184729712 | 5/2/1 | 75.2 |
| bluesky | cs-aot-srv-single | 184349936 | 5/2/1 | 78.1 |
| bluesky | cs-jit-ws | 186255072 | 24/23/2 | 281.1 |
| bluesky | cs-jit-ws-single | 185744984 | 26/25/4 | 231.9 |
| bluesky | cs-jit-srv | 186194584 | 5/2/1 | 73.9 |
| bluesky | cs-jit-srv-single | 185709592 | 5/2/1 | 101.8 |
| playwright | cs-aot-ws | 378771288 | 46/45/4 | 708.0 |
| playwright | cs-aot-ws-single | 378159520 | 47/45/5 | 429.5 |
| playwright | cs-aot-srv | 378630760 | 11/6/4 | 276.0 |
| playwright | cs-aot-srv-single | 378157232 | 9/4/2 | 217.9 |
| playwright | cs-jit-ws | 380138672 | 45/44/2 | 549.5 |
| playwright | cs-jit-ws-single | 379794944 | 47/44/5 | 434.6 |
| playwright | cs-jit-srv | 380135840 | 11/6/3 | 210.4 |
| playwright | cs-jit-srv-single | 379762600 | 10/5/3 | 157.3 |
| tldraw | cs-aot-ws | 356400616 | 47/44/4 | 741.4 |
| tldraw | cs-aot-ws-single | 355454760 | 46/43/5 | 443.1 |
| tldraw | cs-aot-srv | 356257096 | 12/5/2 | 316.8 |
| tldraw | cs-aot-srv-single | 355437288 | 11/5/2 | 253.6 |
| tldraw | cs-jit-ws | 357707872 | 45/42/2 | 567.8 |
| tldraw | cs-jit-ws-single | 356909288 | 46/43/5 | 434.1 |
| tldraw | cs-jit-srv | 357530960 | 7/3/2 | 174.1 |
| tldraw | cs-jit-srv-single | 356896352 | 9/3/2 | 234.5 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (55 files).
