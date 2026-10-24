# Parse-only benchmark, run 2026-10-24T22-33-44-479Z

**Status: protocol result.** All rig checks passed.

Label: binder-additional-run-after-BO3

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 6.4 % (median of 1 s windows: 14.5, 5.6, 2.2, 2.8, 4.9, 7.2, 8.4, 16.4, 7.8, 5.1) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 36c6cb2395, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 6.4 % (range 2.2-16.4 %) | ok |
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
| vscode | tsgo7-bind | 1.358 | 0.0457 | 3.37 % | 8636 | 104.0 | 2756 | 2800 |
| vscode | tsgo7-bind-single | 6.442 | 0.0329 | 0.51 % | 1820 | 21.9 | 2681 | 2745 |
| vscode | tsrs-bind | 0.3933 | 0.0134 | 3.41 % | 29810 | 359.0 | 1148 | 2321 |
| vscode | tsrs-bind-single | 3.308 | 0.0089 | 0.27 % | 3544 | 42.7 | 1132 | 1273 |
| vscode | cs-o-aot-ws-bind | 0.4590 | 0.0062 | 1.34 % | 25543 | 307.6 | 1535 | 1913 |
| vscode | cs-o-aot-ws-bind-single | 3.950 | 0.0093 | 0.23 % | 2968 | 35.7 | 1487 | 1599 |
| vscode | cs-o-aot-srv-bind | 0.4470 | 0.0100 | 2.24 % | 26230 | 315.9 | 1544 | 1940 |
| vscode | cs-o-aot-srv-bind-single | 3.982 | 0.0083 | 0.21 % | 2944 | 35.5 | 1509 | 1625 |
| sentry | tsgo7-bind | 0.5299 | 0.0113 | 2.14 % | 16855 | 86.7 | 1069 | 1092 |
| sentry | tsgo7-bind-single | 2.870 | 0.0109 | 0.38 % | 3112 | 16.0 | 1010 | 1040 |
| sentry | tsrs-bind | 0.1847 | 0.0030 | 1.64 % | 48355 | 248.7 | 422 | 793 |
| sentry | tsrs-bind-single | 1.600 | 0.0080 | 0.50 % | 5582 | 28.7 | 409 | 641 |
| sentry | cs-o-aot-ws-bind | 0.2335 | 0.0043 | 1.85 % | 38261 | 196.8 | 564 | 764 |
| sentry | cs-o-aot-ws-bind-single | 1.822 | 0.0090 | 0.50 % | 4902 | 25.2 | 539 | 549 |
| sentry | cs-o-aot-srv-bind | 0.2210 | 0.0054 | 2.46 % | 40408 | 207.8 | 572 | 790 |
| sentry | cs-o-aot-srv-bind-single | 1.831 | 0.0107 | 0.59 % | 4878 | 25.1 | 537 | 553 |
| bluesky | tsgo7-bind | 0.1329 | 0.0029 | 2.17 % | 12701 | 54.1 | 202 | 215 |
| bluesky | tsgo7-bind-single | 0.5397 | 0.0042 | 0.78 % | 3127 | 13.3 | 186 | 195 |
| bluesky | tsrs-bind | 0.0553 | 0.0017 | 3.02 % | 30536 | 130.1 | 83 | 227 |
| bluesky | tsrs-bind-single | 0.3077 | 0.0025 | 0.81 % | 5486 | 23.4 | 76 | 110 |
| bluesky | cs-o-aot-ws-bind | 0.0638 | 0.0006 | 0.95 % | 26451 | 112.7 | 120 | 166 |
| bluesky | cs-o-aot-ws-bind-single | 0.3518 | 0.0025 | 0.70 % | 4799 | 20.5 | 103 | 100 |
| bluesky | cs-o-aot-srv-bind | 0.0756 | 0.0009 | 1.19 % | 22318 | 95.1 | 126 | 184 |
| bluesky | cs-o-aot-srv-bind-single | 0.3605 | 0.0023 | 0.65 % | 4682 | 20.0 | 106 | 112 |
| playwright | tsgo7-bind | 0.1786 | 0.0054 | 3.04 % | 7921 | 96.5 | 342 | 357 |
| playwright | tsgo7-bind-single | 0.7622 | 0.0034 | 0.44 % | 1856 | 22.6 | 324 | 338 |
| playwright | tsrs-bind | 0.0661 | 0.0013 | 1.98 % | 21394 | 260.5 | 147 | 387 |
| playwright | tsrs-bind-single | 0.3947 | 0.0020 | 0.50 % | 3585 | 43.7 | 138 | 205 |
| playwright | cs-o-aot-ws-bind | 0.0797 | 0.0011 | 1.36 % | 17746 | 216.1 | 219 | 349 |
| playwright | cs-o-aot-ws-bind-single | 0.4861 | 0.0022 | 0.45 % | 2911 | 35.4 | 190 | 205 |
| playwright | cs-o-aot-srv-bind | 0.0955 | 0.0012 | 1.29 % | 14817 | 180.4 | 225 | 364 |
| playwright | cs-o-aot-srv-bind-single | 0.4928 | 0.0023 | 0.46 % | 2872 | 35.0 | 192 | 215 |
| tldraw | tsgo7-bind | 0.1870 | 0.0027 | 1.47 % | 13413 | 72.9 | 349 | 364 |
| tldraw | tsgo7-bind-single | 0.8943 | 0.0047 | 0.53 % | 2805 | 15.2 | 327 | 338 |
| tldraw | tsrs-bind | 0.0770 | 0.0020 | 2.54 % | 32554 | 176.9 | 143 | 363 |
| tldraw | tsrs-bind-single | 0.4921 | 0.0023 | 0.47 % | 5097 | 27.7 | 135 | 198 |
| tldraw | cs-o-aot-ws-bind | 0.0906 | 0.0010 | 1.11 % | 27676 | 150.4 | 202 | 313 |
| tldraw | cs-o-aot-ws-bind-single | 0.5696 | 0.0026 | 0.45 % | 4403 | 23.9 | 182 | 190 |
| tldraw | cs-o-aot-srv-bind | 0.0995 | 0.0013 | 1.28 % | 25217 | 137.0 | 207 | 314 |
| tldraw | cs-o-aot-srv-bind-single | 0.5739 | 0.0033 | 0.58 % | 4370 | 23.7 | 180 | 197 |

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
| tsgo7-bind-single | 4.75 [4.60, 4.87] | 5.42 [5.35, 5.54] | 4.06 [4.00, 4.14] | 4.27 [4.15, 4.38] | 4.78 [4.65, 4.84] | 4.63 [4.58, 4.68] |
| tsrs-bind | 0.29 [0.28, 0.31] | 0.35 [0.34, 0.36] | 0.42 [0.40, 0.43] | 0.37 [0.36, 0.39] | 0.41 [0.40, 0.42] | 0.36 [0.36, 0.37] |
| tsrs-bind-single | 2.44 [2.36, 2.50] | 3.02 [2.99, 3.09] | 2.32 [2.28, 2.36] | 2.21 [2.15, 2.27] | 2.63 [2.56, 2.66] | 2.51 [2.48, 2.53] |
| cs-o-aot-ws-bind | 0.34 [0.33, 0.35] | 0.44 [0.43, 0.45] | 0.48 [0.47, 0.49] | 0.45 [0.43, 0.46] | 0.48 [0.47, 0.49] | 0.43 [0.43, 0.44] |
| cs-o-aot-ws-bind-single | 2.91 [2.82, 2.99] | 3.44 [3.40, 3.52] | 2.65 [2.61, 2.70] | 2.72 [2.65, 2.79] | 3.05 [2.96, 3.08] | 2.94 [2.91, 2.97] |
| cs-o-aot-srv-bind | 0.33 [0.32, 0.34] | 0.42 [0.41, 0.43] | 0.57 [0.56, 0.60] | 0.53 [0.52, 0.55] | 0.53 [0.52, 0.54] | 0.47 [0.46, 0.47] |
| cs-o-aot-srv-bind-single | 2.93 [2.84, 3.01] | 3.46 [3.41, 3.53] | 2.71 [2.67, 2.77] | 2.76 [2.68, 2.83] | 3.07 [2.98, 3.11] | 2.97 [2.95, 3.01] |

## Table 2d. Parse+bind: median wall-clock relative to `tsrs-bind`, with 95 % confidence interval

Secondary reference: parse+bind against tsrs 0.18.0 (tsrs_parser::parse_source_file_owned and tsrs_binder::bind_source_file per file, parallel). Same method as Table 2.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-bind | 3.45 [3.20, 3.56] | 2.87 [2.78, 2.92] | 2.40 [2.34, 2.50] | 2.70 [2.59, 2.78] | 2.43 [2.36, 2.50] | 2.75 [2.69, 2.78] |
| tsgo7-bind-single | 16.38 [15.21, 16.55] | 15.54 [15.24, 15.77] | 9.76 [9.55, 10.13] | 11.52 [11.25, 11.64] | 11.61 [11.34, 11.79] | 12.72 [12.49, 12.81] |
| tsrs-bind | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-bind-single | 8.41 [7.82, 8.50] | 8.66 [8.50, 8.81] | 5.57 [5.44, 5.77] | 5.97 [5.83, 6.04] | 6.39 [6.24, 6.50] | 6.88 [6.77, 6.94] |
| cs-o-aot-ws-bind | 1.17 [1.08, 1.19] | 1.26 [1.23, 1.29] | 1.15 [1.13, 1.20] | 1.21 [1.17, 1.23] | 1.18 [1.15, 1.20] | 1.19 [1.17, 1.20] |
| cs-o-aot-ws-bind-single | 10.04 [9.34, 10.15] | 9.86 [9.67, 10.02] | 6.36 [6.23, 6.60] | 7.35 [7.17, 7.42] | 7.39 [7.22, 7.51] | 8.07 [7.93, 8.14] |
| cs-o-aot-srv-bind | 1.14 [1.05, 1.15] | 1.20 [1.17, 1.24] | 1.37 [1.34, 1.46] | 1.44 [1.40, 1.46] | 1.29 [1.26, 1.32] | 1.28 [1.26, 1.30] |
| cs-o-aot-srv-bind-single | 10.12 [9.40, 10.23] | 9.91 [9.72, 10.06] | 6.52 [6.39, 6.76] | 7.45 [7.27, 7.53] | 7.45 [7.28, 7.57] | 8.17 [8.02, 8.23] |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: binder. Binding rows (rig check): tsgo7-bind-single, cs-o-aot-ws-bind-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-bind | reported | 3.37 % | 2.14 % | 2.17 % | 3.04 % | 1.47 % | 3.37 % |
| tsgo7-bind-single | rig check | 0.51 % | 0.38 % | 0.78 % | 0.44 % | 0.53 % | 0.78 % |
| tsrs-bind | reported | 3.41 % | 1.64 % | 3.02 % | 1.98 % | 2.54 % | 3.41 % |
| tsrs-bind-single | reported | 0.27 % | 0.50 % | 0.81 % | 0.50 % | 0.47 % | 0.81 % |
| cs-o-aot-ws-bind | reported | 1.34 % | 1.85 % | 0.95 % | 1.36 % | 1.11 % | 1.85 % |
| cs-o-aot-ws-bind-single | rig check | 0.23 % | 0.50 % | 0.70 % | 0.45 % | 0.45 % | 0.70 % |
| cs-o-aot-srv-bind | reported | 2.24 % | 2.46 % | 1.19 % | 1.29 % | 1.28 % | 2.46 % |
| cs-o-aot-srv-bind-single | reported | 0.21 % | 0.59 % | 0.65 % | 0.46 % | 0.58 % | 0.65 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-bind | 0.0209 | 1.66 % | 15.7 % | 9 |
| tsgo7-bind-single | 0.0206 | 1.62 % | 3.8 % | 9 |
| tsrs-bind | 0.0190 | 1.05 % | 34.4 % | 8 |
| tsrs-bind-single | 0.0172 | 1.56 % | 5.6 % | 7 |
| cs-o-aot-ws-bind | 0.0232 | 1.00 % | 36.3 % | 10 |
| cs-o-aot-ws-bind-single | 0.0245 | 1.80 % | 7.0 % | 10 |
| cs-o-aot-srv-bind | 0.0278 | 2.06 % | 36.8 % | 12 |
| cs-o-aot-srv-bind-single | 0.0271 | 1.45 % | 7.5 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-bind | 93054424 | 8/5/3 | 41.9 |
| vscode | cs-o-aot-ws-bind-single | 53795128 | 7/4/1 | 32.0 |
| vscode | cs-o-aot-srv-bind | 152723328 | 8/6/5 | 27.1 |
| vscode | cs-o-aot-srv-bind-single | 53792296 | 4/2/1 | 33.0 |
| sentry | cs-o-aot-ws-bind | 73313576 | 6/4/3 | 31.3 |
| sentry | cs-o-aot-ws-bind-single | 30342112 | 3/1/0 | 16.4 |
| sentry | cs-o-aot-srv-bind | 78030448 | 7/6/5 | 11.1 |
| sentry | cs-o-aot-srv-bind-single | 30341440 | 3/1/0 | 9.1 |
| bluesky | cs-o-aot-ws-bind | 40142696 | 2/2/2 | 1.6 |
| bluesky | cs-o-aot-ws-bind-single | 7172896 | 0/0/0 | 0.0 |
| bluesky | cs-o-aot-srv-bind | 48613768 | 8/7/6 | 9.0 |
| bluesky | cs-o-aot-srv-bind-single | 7174624 | 2/1/0 | 4.8 |
| playwright | cs-o-aot-ws-bind | 46125360 | 2/2/2 | 1.5 |
| playwright | cs-o-aot-ws-bind-single | 7342368 | 0/0/0 | 0.0 |
| playwright | cs-o-aot-srv-bind | 66790752 | 8/7/6 | 10.0 |
| playwright | cs-o-aot-srv-bind-single | 7343424 | 1/0/0 | 1.6 |
| tldraw | cs-o-aot-ws-bind | 48219520 | 3/3/2 | 5.5 |
| tldraw | cs-o-aot-ws-bind-single | 10585576 | 1/0/0 | 5.1 |
| tldraw | cs-o-aot-srv-bind | 57922712 | 8/7/6 | 10.4 |
| tldraw | cs-o-aot-srv-bind-single | 10586248 | 2/1/0 | 4.4 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (40 files).
