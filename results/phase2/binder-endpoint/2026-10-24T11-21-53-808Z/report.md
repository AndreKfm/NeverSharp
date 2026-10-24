# Parse-only benchmark, run 2026-10-24T11-21-53-808Z

**Status: protocol result.** All rig checks passed.

Label: binder-endpoint-run1

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 99 % of 4150 MHz, idle before run 6.2 % (median of 1 s windows: 13.4, 7.8, 6.2, 3.6, 14.7, 6.1, 5.7, 2.8, 1.5, 6.4) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 70190575b6, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 99 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 6.2 % (range 1.5-14.7 %) | ok |
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

- `tsgo7-bind`: typescript-go 7.0.2, parse+bind, parallel (default)
- `tsgo7-bind-single`: typescript-go 7.0.2, parse+bind, single-threaded
- `tsrs-bind`: tsrs 0.18.0 (Rust), parse+bind, parallel (rayon)
- `tsrs-bind-single`: tsrs 0.18.0 (Rust), parse+bind, single-threaded
- `cs-o-aot-ws-bind`: C# Track O, NativeAOT, Workstation GC, parse+bind, parallel (parse+bind endpoint)
- `cs-o-aot-ws-bind-single`: C# Track O, NativeAOT, Workstation GC, parse+bind, single-threaded
- `cs-o-aot-srv-bind`: C# Track O, NativeAOT, Server GC, parse+bind, parallel
- `cs-o-aot-srv-bind-single`: C# Track O, NativeAOT, Server GC, parse+bind, single-threaded

## Table 1. Wall-clock, throughput, peak memory (parse-only; `*-bind` rows: parse+bind)

| Repository | Configuration | Median (s) | MAD (s) | MAD/median | Files/s | MB/s | Peak working set (MiB) | Peak commit (MiB) |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| vscode | tsgo7-bind | 1.357 | 0.0365 | 2.69 % | 8642 | 104.1 | 2759 | 2803 |
| vscode | tsgo7-bind-single | 6.466 | 0.0233 | 0.36 % | 1813 | 21.8 | 2689 | 2735 |
| vscode | tsrs-bind | 0.3989 | 0.0247 | 6.20 % | 29388 | 353.9 | 1148 | 2346 |
| vscode | tsrs-bind-single | 3.312 | 0.0122 | 0.37 % | 3540 | 42.6 | 1149 | 1273 |
| vscode | cs-o-aot-ws-bind | 1.519 | 0.0291 | 1.91 % | 7716 | 92.9 | 2963 | 4374 |
| vscode | cs-o-aot-ws-bind-single | 6.475 | 0.0116 | 0.18 % | 1811 | 21.8 | 2905 | 4015 |
| vscode | cs-o-aot-srv-bind | 1.179 | 0.0410 | 3.48 % | 9946 | 119.8 | 3135 | 4684 |
| vscode | cs-o-aot-srv-bind-single | 6.675 | 0.0122 | 0.18 % | 1756 | 21.2 | 3040 | 4183 |
| sentry | tsgo7-bind | 0.5394 | 0.0096 | 1.79 % | 16558 | 85.2 | 1043 | 1068 |
| sentry | tsgo7-bind-single | 2.874 | 0.0081 | 0.28 % | 3107 | 16.0 | 1015 | 1040 |
| sentry | tsrs-bind | 0.1849 | 0.0024 | 1.30 % | 48310 | 248.5 | 421 | 799 |
| sentry | tsrs-bind-single | 1.593 | 0.0082 | 0.51 % | 5606 | 28.8 | 426 | 642 |
| sentry | cs-o-aot-ws-bind | 0.7289 | 0.0108 | 1.48 % | 12254 | 63.0 | 1120 | 1562 |
| sentry | cs-o-aot-ws-bind-single | 2.781 | 0.0119 | 0.43 % | 3212 | 16.5 | 1088 | 1408 |
| sentry | cs-o-aot-srv-bind | 0.5758 | 0.0259 | 4.49 % | 15511 | 79.8 | 1203 | 1681 |
| sentry | cs-o-aot-srv-bind-single | 2.950 | 0.0162 | 0.55 % | 3028 | 15.6 | 1177 | 1503 |
| bluesky | tsgo7-bind | 0.1272 | 0.0033 | 2.59 % | 13273 | 56.6 | 205 | 219 |
| bluesky | tsgo7-bind-single | 0.5329 | 0.0036 | 0.68 % | 3168 | 13.5 | 187 | 195 |
| bluesky | tsrs-bind | 0.0542 | 0.0014 | 2.59 % | 31170 | 132.8 | 83 | 219 |
| bluesky | tsrs-bind-single | 0.3054 | 0.0020 | 0.65 % | 5528 | 23.6 | 76 | 110 |
| bluesky | cs-o-aot-ws-bind | 0.1576 | 0.0052 | 3.27 % | 10709 | 45.6 | 221 | 302 |
| bluesky | cs-o-aot-ws-bind-single | 0.5173 | 0.0030 | 0.58 % | 3263 | 13.9 | 206 | 262 |
| bluesky | cs-o-aot-srv-bind | 0.1658 | 0.0133 | 8.02 % | 10181 | 43.4 | 257 | 354 |
| bluesky | cs-o-aot-srv-bind-single | 0.5789 | 0.0074 | 1.28 % | 2916 | 12.4 | 224 | 286 |
| playwright | tsgo7-bind | 0.1803 | 0.0024 | 1.32 % | 7850 | 95.6 | 343 | 358 |
| playwright | tsgo7-bind-single | 0.7639 | 0.0043 | 0.56 % | 1852 | 22.6 | 324 | 337 |
| playwright | tsrs-bind | 0.0675 | 0.0015 | 2.20 % | 20966 | 255.3 | 147 | 387 |
| playwright | tsrs-bind-single | 0.3937 | 0.0016 | 0.41 % | 3594 | 43.8 | 138 | 205 |
| playwright | cs-o-aot-ws-bind | 0.2015 | 0.0035 | 1.72 % | 7021 | 85.5 | 383 | 569 |
| playwright | cs-o-aot-ws-bind-single | 0.7641 | 0.0029 | 0.38 % | 1852 | 22.6 | 360 | 487 |
| playwright | cs-o-aot-srv-bind | 0.2272 | 0.0195 | 8.60 % | 6229 | 75.9 | 418 | 623 |
| playwright | cs-o-aot-srv-bind-single | 0.8418 | 0.0098 | 1.17 % | 1681 | 20.5 | 387 | 521 |
| tldraw | tsgo7-bind | 0.1917 | 0.0034 | 1.75 % | 13083 | 71.1 | 344 | 357 |
| tldraw | tsgo7-bind-single | 0.8924 | 0.0043 | 0.48 % | 2811 | 15.3 | 327 | 337 |
| tldraw | tsrs-bind | 0.0764 | 0.0015 | 2.02 % | 32810 | 178.3 | 143 | 360 |
| tldraw | tsrs-bind-single | 0.4920 | 0.0033 | 0.67 % | 5097 | 27.7 | 135 | 198 |
| tldraw | cs-o-aot-ws-bind | 0.2342 | 0.0081 | 3.45 % | 10710 | 58.2 | 379 | 530 |
| tldraw | cs-o-aot-ws-bind-single | 0.8809 | 0.0039 | 0.44 % | 2847 | 15.5 | 358 | 468 |
| tldraw | cs-o-aot-srv-bind | 0.2287 | 0.0139 | 6.06 % | 10965 | 59.6 | 426 | 602 |
| tldraw | cs-o-aot-srv-bind-single | 0.9850 | 0.0080 | 0.82 % | 2546 | 13.8 | 386 | 503 |

## Table 1b. Parse+bind: symbols created (driver report of the validation run)

Sum of the files' symbol counts. The C# configurations bind with TypeScript 6.0.3's binder (Track O, Gate B), typescript-go and tsrs with TypeScript 7's, which differs in places: the counts show that binding happened, they are not compared.

| Configuration | vscode | sentry | bluesky | playwright | tldraw |
| --- | ---: | ---: | ---: | ---: | ---: |
| tsgo7-bind | 2033744 | 978799 | 163216 | 273702 | 262537 |
| tsgo7-bind-single | 2033744 | 978799 | 163216 | 273702 | 262537 |
| tsrs-bind | 2033744 | 978799 | 163216 | 273702 | 262537 |
| tsrs-bind-single | 2033744 | 978799 | 163216 | 273702 | 262537 |
| cs-o-aot-ws-bind | 2033744 | 978798 | 163216 | 273701 | 262536 |
| cs-o-aot-ws-bind-single | 2033744 | 978798 | 163216 | 273701 | 262536 |
| cs-o-aot-srv-bind | 2033744 | 978798 | 163216 | 273701 | 262536 |
| cs-o-aot-srv-bind-single | 2033744 | 978798 | 163216 | 273701 | 262536 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |

## Table 2c. Parse+bind: median wall-clock relative to `tsgo7-bind`, with 95 % confidence interval

Endpoint of pre-registration §15: parse+bind per file, against typescript-go 7.0.2 (parser.ParseSourceFile and binder.BindSourceFile per file, parallel). Same method as Table 2. typescript-go's binder ports TypeScript 7 and differs from 6.0.3's in places.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-bind | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-bind-single | 4.77 [4.68, 4.86] | 5.33 [5.26, 5.40] | 4.19 [4.10, 4.27] | 4.24 [4.16, 4.29] | 4.66 [4.61, 4.77] | 4.62 [4.59, 4.65] |
| tsrs-bind | 0.29 [0.28, 0.31] | 0.34 [0.33, 0.35] | 0.43 [0.41, 0.44] | 0.37 [0.37, 0.38] | 0.40 [0.39, 0.41] | 0.36 [0.36, 0.37] |
| tsrs-bind-single | 2.44 [2.40, 2.49] | 2.95 [2.92, 3.00] | 2.40 [2.35, 2.45] | 2.18 [2.15, 2.22] | 2.57 [2.54, 2.63] | 2.50 [2.48, 2.52] |
| cs-o-aot-ws-bind | 1.12 [1.09, 1.14] | 1.35 [1.33, 1.37] | 1.24 [1.19, 1.27] | 1.12 [1.09, 1.14] | 1.22 [1.19, 1.26] | 1.21 [1.19, 1.22] |
| cs-o-aot-ws-bind-single | 4.77 [4.69, 4.86] | 5.15 [5.09, 5.23] | 4.07 [3.98, 4.15] | 4.24 [4.17, 4.29] | 4.60 [4.55, 4.71] | 4.55 [4.52, 4.58] |
| cs-o-aot-srv-bind | 0.87 [0.82, 0.89] | 1.07 [1.02, 1.09] | 1.30 [1.25, 1.46] | 1.26 [1.20, 1.37] | 1.19 [1.15, 1.29] | 1.13 [1.10, 1.16] |
| cs-o-aot-srv-bind-single | 4.92 [4.83, 5.02] | 5.47 [5.40, 5.55] | 4.55 [4.44, 4.66] | 4.67 [4.59, 4.75] | 5.14 [5.07, 5.26] | 4.94 [4.90, 4.98] |

## Table 2d. Parse+bind: median wall-clock relative to `tsrs-bind`, with 95 % confidence interval

Secondary reference: parse+bind against tsrs 0.18.0 (tsrs_parser::parse_source_file_owned and tsrs_binder::bind_source_file per file, parallel). Same method as Table 2.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-bind | 3.40 [3.18, 3.53] | 2.92 [2.87, 2.99] | 2.35 [2.29, 2.43] | 2.67 [2.61, 2.73] | 2.51 [2.45, 2.57] | 2.75 [2.70, 2.78] |
| tsgo7-bind-single | 16.21 [15.20, 16.71] | 15.55 [15.41, 15.91] | 9.84 [9.69, 10.08] | 11.32 [11.10, 11.46] | 11.67 [11.58, 11.94] | 12.68 [12.50, 12.84] |
| tsrs-bind | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-bind-single | 8.30 [7.78, 8.56] | 8.62 [8.54, 8.83] | 5.64 [5.54, 5.77] | 5.83 [5.72, 5.93] | 6.44 [6.37, 6.58] | 6.86 [6.76, 6.94] |
| cs-o-aot-ws-bind | 3.81 [3.55, 3.93] | 3.94 [3.90, 4.05] | 2.91 [2.80, 3.00] | 2.99 [2.92, 3.05] | 3.06 [2.98, 3.17] | 3.31 [3.25, 3.36] |
| cs-o-aot-ws-bind-single | 16.23 [15.22, 16.73] | 15.04 [14.91, 15.40] | 9.55 [9.41, 9.78] | 11.32 [11.11, 11.47] | 11.52 [11.43, 11.77] | 12.49 [12.32, 12.65] |
| cs-o-aot-srv-bind | 2.95 [2.67, 3.08] | 3.11 [2.97, 3.20] | 3.06 [2.95, 3.42] | 3.37 [3.19, 3.67] | 2.99 [2.88, 3.24] | 3.09 [3.01, 3.20] |
| cs-o-aot-srv-bind-single | 16.73 [15.69, 17.25] | 15.95 [15.82, 16.35] | 10.69 [10.49, 10.97] | 12.47 [12.22, 12.70] | 12.89 [12.74, 13.19] | 13.56 [13.36, 13.73] |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: binder. Binding rows (rig check): tsgo7-bind-single, cs-o-aot-ws-bind-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-bind | reported | 2.69 % | 1.79 % | 2.59 % | 1.32 % | 1.75 % | 2.69 % |
| tsgo7-bind-single | rig check | 0.36 % | 0.28 % | 0.68 % | 0.56 % | 0.48 % | 0.68 % |
| tsrs-bind | reported | 6.20 % | 1.30 % | 2.59 % | 2.20 % | 2.02 % | 6.20 % |
| tsrs-bind-single | reported | 0.37 % | 0.51 % | 0.65 % | 0.41 % | 0.67 % | 0.67 % |
| cs-o-aot-ws-bind | reported | 1.91 % | 1.48 % | 3.27 % | 1.72 % | 3.45 % | 3.45 % |
| cs-o-aot-ws-bind-single | rig check | 0.18 % | 0.43 % | 0.58 % | 0.38 % | 0.44 % | 0.58 % |
| cs-o-aot-srv-bind | reported | 3.48 % | 4.49 % | 8.02 % | 8.60 % | 6.06 % | 8.60 % |
| cs-o-aot-srv-bind-single | reported | 0.18 % | 0.55 % | 1.28 % | 1.17 % | 0.82 % | 1.28 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-bind | 0.0210 | 0.87 % | 16.5 % | 9 |
| tsgo7-bind-single | 0.0209 | 1.12 % | 3.9 % | 9 |
| tsrs-bind | 0.0183 | 1.13 % | 33.9 % | 8 |
| tsrs-bind-single | 0.0166 | 0.65 % | 5.4 % | 7 |
| cs-o-aot-ws-bind | 0.0231 | 0.67 % | 14.7 % | 10 |
| cs-o-aot-ws-bind-single | 0.0230 | 0.49 % | 4.4 % | 10 |
| cs-o-aot-srv-bind | 0.0279 | 1.32 % | 16.8 % | 12 |
| cs-o-aot-srv-bind-single | 0.0285 | 2.89 % | 4.9 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-bind | 3984250808 | 178/93/8 | 581.0 |
| vscode | cs-o-aot-ws-bind-single | 3919474992 | 156/83/9 | 203.9 |
| vscode | cs-o-aot-srv-bind | 4072837376 | 27/17/14 | 124.0 |
| vscode | cs-o-aot-srv-bind-single | 3919191368 | 35/25/22 | 120.4 |
| sentry | cs-o-aot-ws-bind | 1432228064 | 78/43/7 | 256.2 |
| sentry | cs-o-aot-ws-bind-single | 1380612912 | 66/36/6 | 114.3 |
| sentry | cs-o-aot-srv-bind | 1498403648 | 22/15/13 | 90.0 |
| sentry | cs-o-aot-srv-bind-single | 1380647520 | 27/18/17 | 68.6 |
| bluesky | cs-o-aot-ws-bind | 267446056 | 16/10/4 | 42.7 |
| bluesky | cs-o-aot-ws-bind-single | 240776984 | 14/9/4 | 20.7 |
| bluesky | cs-o-aot-srv-bind | 289472616 | 15/12/10 | 30.3 |
| bluesky | cs-o-aot-srv-bind-single | 240803304 | 16/13/11 | 13.6 |
| playwright | cs-o-aot-ws-bind | 483881624 | 25/15/5 | 56.2 |
| playwright | cs-o-aot-ws-bind-single | 449891776 | 21/13/5 | 21.8 |
| playwright | cs-o-aot-srv-bind | 506358792 | 19/14/12 | 36.9 |
| playwright | cs-o-aot-srv-bind-single | 449925768 | 21/17/15 | 15.4 |
| tldraw | cs-o-aot-ws-bind | 467655792 | 26/16/5 | 65.0 |
| tldraw | cs-o-aot-ws-bind-single | 444820640 | 23/14/5 | 34.8 |
| tldraw | cs-o-aot-srv-bind | 503287408 | 17/10/10 | 35.1 |
| tldraw | cs-o-aot-srv-bind-single | 444852080 | 19/15/14 | 18.4 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (40 files).
