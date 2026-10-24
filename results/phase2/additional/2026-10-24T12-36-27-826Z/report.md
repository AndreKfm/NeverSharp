# Parse-only benchmark, run 2026-10-24T12-36-27-826Z

**Status: protocol result.** All rig checks passed.

Label: binder-additional-run-after-BO1

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 99 % of 4150 MHz, idle before run 9.0 % (median of 1 s windows: 18, 10.8, 10.1, 3.5, 1.6, 7.9, 11.3, 13.6, 6.7, 4) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit f10774a778, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 99 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 9.0 % (range 1.6-18.0 %) | ok |
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
| vscode | tsgo7-bind | 1.340 | 0.0324 | 2.42 % | 8747 | 105.3 | 2777 | 2815 |
| vscode | tsgo7-bind-single | 6.482 | 0.0289 | 0.45 % | 1809 | 21.8 | 2722 | 2747 |
| vscode | tsrs-bind | 0.4303 | 0.0201 | 4.67 % | 27246 | 328.1 | 1148 | 2251 |
| vscode | tsrs-bind-single | 3.318 | 0.0164 | 0.49 % | 3533 | 42.5 | 1149 | 1273 |
| vscode | cs-o-aot-ws-bind | 0.5619 | 0.0069 | 1.24 % | 20866 | 251.3 | 1797 | 2169 |
| vscode | cs-o-aot-ws-bind-single | 4.921 | 0.0149 | 0.30 % | 2383 | 28.7 | 1761 | 1899 |
| vscode | cs-o-aot-srv-bind | 0.5557 | 0.0103 | 1.86 % | 21097 | 254.0 | 1803 | 2209 |
| vscode | cs-o-aot-srv-bind-single | 4.924 | 0.0117 | 0.24 % | 2381 | 28.7 | 1784 | 1925 |
| sentry | tsgo7-bind | 0.5266 | 0.0123 | 2.33 % | 16961 | 87.2 | 1062 | 1085 |
| sentry | tsgo7-bind-single | 2.873 | 0.0136 | 0.47 % | 3109 | 16.0 | 1015 | 1040 |
| sentry | tsrs-bind | 0.1885 | 0.0048 | 2.57 % | 47378 | 243.7 | 421 | 813 |
| sentry | tsrs-bind-single | 1.591 | 0.0072 | 0.45 % | 5616 | 28.9 | 426 | 641 |
| sentry | cs-o-aot-ws-bind | 0.2677 | 0.0058 | 2.15 % | 33367 | 171.6 | 679 | 881 |
| sentry | cs-o-aot-ws-bind-single | 2.174 | 0.0078 | 0.36 % | 4109 | 21.1 | 666 | 684 |
| sentry | cs-o-aot-srv-bind | 0.2538 | 0.0031 | 1.23 % | 35192 | 181.0 | 689 | 905 |
| sentry | cs-o-aot-srv-bind-single | 2.178 | 0.0068 | 0.31 % | 4100 | 21.1 | 664 | 687 |
| bluesky | tsgo7-bind | 0.1322 | 0.0050 | 3.81 % | 12768 | 54.4 | 206 | 218 |
| bluesky | tsgo7-bind-single | 0.5371 | 0.0034 | 0.63 % | 3143 | 13.4 | 186 | 194 |
| bluesky | tsrs-bind | 0.0528 | 0.0008 | 1.54 % | 31992 | 136.3 | 83 | 231 |
| bluesky | tsrs-bind-single | 0.3052 | 0.0019 | 0.61 % | 5531 | 23.6 | 76 | 110 |
| bluesky | cs-o-aot-ws-bind | 0.0692 | 0.0017 | 2.53 % | 24404 | 104.0 | 134 | 183 |
| bluesky | cs-o-aot-ws-bind-single | 0.4086 | 0.0018 | 0.44 % | 4131 | 17.6 | 122 | 123 |
| bluesky | cs-o-aot-srv-bind | 0.0825 | 0.0029 | 3.54 % | 20453 | 87.2 | 138 | 192 |
| bluesky | cs-o-aot-srv-bind-single | 0.4189 | 0.0027 | 0.64 % | 4029 | 17.2 | 126 | 135 |
| playwright | tsgo7-bind | 0.1852 | 0.0086 | 4.67 % | 7641 | 93.1 | 340 | 356 |
| playwright | tsgo7-bind-single | 0.7619 | 0.0040 | 0.52 % | 1857 | 22.6 | 325 | 338 |
| playwright | tsrs-bind | 0.0702 | 0.0033 | 4.72 % | 20166 | 245.6 | 147 | 382 |
| playwright | tsrs-bind-single | 0.3969 | 0.0022 | 0.56 % | 3565 | 43.4 | 138 | 205 |
| playwright | cs-o-aot-ws-bind | 0.0925 | 0.0025 | 2.66 % | 15302 | 186.4 | 242 | 366 |
| playwright | cs-o-aot-ws-bind-single | 0.5986 | 0.0016 | 0.27 % | 2364 | 28.8 | 223 | 243 |
| playwright | cs-o-aot-srv-bind | 0.1092 | 0.0054 | 4.95 % | 12963 | 157.9 | 247 | 378 |
| playwright | cs-o-aot-srv-bind-single | 0.6107 | 0.0066 | 1.09 % | 2317 | 28.2 | 227 | 255 |
| tldraw | tsgo7-bind | 0.2047 | 0.0034 | 1.68 % | 12252 | 66.6 | 346 | 363 |
| tldraw | tsgo7-bind-single | 0.9042 | 0.0068 | 0.75 % | 2774 | 15.1 | 327 | 336 |
| tldraw | tsrs-bind | 0.0776 | 0.0018 | 2.36 % | 32307 | 175.5 | 143 | 363 |
| tldraw | tsrs-bind-single | 0.4912 | 0.0022 | 0.45 % | 5106 | 27.7 | 135 | 198 |
| tldraw | cs-o-aot-ws-bind | 0.1024 | 0.0025 | 2.47 % | 24491 | 133.1 | 232 | 321 |
| tldraw | cs-o-aot-ws-bind-single | 0.6826 | 0.0025 | 0.37 % | 3674 | 20.0 | 220 | 228 |
| tldraw | cs-o-aot-srv-bind | 0.1193 | 0.0033 | 2.77 % | 21019 | 114.2 | 235 | 339 |
| tldraw | cs-o-aot-srv-bind-single | 0.6845 | 0.0043 | 0.63 % | 3664 | 19.9 | 217 | 235 |

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
| tsgo7-bind-single | 4.84 [4.77, 4.95] | 5.46 [5.32, 5.53] | 4.06 [3.98, 4.23] | 4.11 [4.02, 4.34] | 4.42 [4.36, 4.48] | 4.55 [4.51, 4.62] |
| tsrs-bind | 0.32 [0.31, 0.34] | 0.36 [0.35, 0.37] | 0.40 [0.39, 0.42] | 0.38 [0.37, 0.41] | 0.38 [0.37, 0.39] | 0.37 [0.36, 0.38] |
| tsrs-bind-single | 2.48 [2.45, 2.54] | 3.02 [2.95, 3.06] | 2.31 [2.26, 2.40] | 2.14 [2.09, 2.26] | 2.40 [2.37, 2.44] | 2.45 [2.43, 2.49] |
| cs-o-aot-ws-bind | 0.42 [0.41, 0.43] | 0.51 [0.49, 0.52] | 0.52 [0.51, 0.55] | 0.50 [0.48, 0.53] | 0.50 [0.49, 0.52] | 0.49 [0.48, 0.50] |
| cs-o-aot-ws-bind-single | 3.67 [3.63, 3.76] | 4.13 [4.03, 4.18] | 3.09 [3.03, 3.22] | 3.23 [3.16, 3.40] | 3.33 [3.29, 3.39] | 3.47 [3.44, 3.53] |
| cs-o-aot-srv-bind | 0.41 [0.41, 0.43] | 0.48 [0.47, 0.49] | 0.62 [0.60, 0.68] | 0.59 [0.57, 0.64] | 0.58 [0.57, 0.60] | 0.53 [0.53, 0.55] |
| cs-o-aot-srv-bind-single | 3.67 [3.63, 3.76] | 4.14 [4.03, 4.19] | 3.17 [3.11, 3.30] | 3.30 [3.22, 3.49] | 3.34 [3.30, 3.40] | 3.51 [3.48, 3.57] |

## Table 2d. Parse+bind: median wall-clock relative to `tsrs-bind`, with 95 % confidence interval

Secondary reference: parse+bind against tsrs 0.18.0 (tsrs_parser::parse_source_file_owned and tsrs_binder::bind_source_file per file, parallel). Same method as Table 2.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-bind | 3.11 [2.96, 3.23] | 2.79 [2.72, 2.88] | 2.51 [2.40, 2.56] | 2.64 [2.42, 2.72] | 2.64 [2.55, 2.68] | 2.73 [2.66, 2.76] |
| tsgo7-bind-single | 15.06 [14.35, 15.61] | 15.24 [14.86, 15.44] | 10.18 [10.01, 10.27] | 10.86 [10.36, 11.05] | 11.65 [11.31, 11.80] | 12.42 [12.22, 12.52] |
| tsrs-bind | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-bind-single | 7.71 [7.35, 8.00] | 8.44 [8.23, 8.56] | 5.78 [5.68, 5.84] | 5.66 [5.39, 5.76] | 6.33 [6.15, 6.42] | 6.70 [6.59, 6.76] |
| cs-o-aot-ws-bind | 1.31 [1.24, 1.35] | 1.42 [1.38, 1.46] | 1.31 [1.27, 1.34] | 1.32 [1.25, 1.36] | 1.32 [1.27, 1.36] | 1.33 [1.31, 1.35] |
| cs-o-aot-ws-bind-single | 11.44 [10.89, 11.85] | 11.53 [11.25, 11.68] | 7.74 [7.62, 7.82] | 8.53 [8.14, 8.66] | 8.79 [8.56, 8.91] | 9.48 [9.33, 9.56] |
| cs-o-aot-srv-bind | 1.29 [1.23, 1.34] | 1.35 [1.31, 1.37] | 1.56 [1.52, 1.68] | 1.56 [1.47, 1.63] | 1.54 [1.47, 1.57] | 1.45 [1.43, 1.48] |
| cs-o-aot-srv-bind-single | 11.44 [10.90, 11.86] | 11.56 [11.27, 11.72] | 7.94 [7.81, 8.01] | 8.70 [8.28, 8.88] | 8.82 [8.58, 8.96] | 9.58 [9.42, 9.66] |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: binder. Binding rows (rig check): tsgo7-bind-single, cs-o-aot-ws-bind-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-bind | reported | 2.42 % | 2.33 % | 3.81 % | 4.67 % | 1.68 % | 4.67 % |
| tsgo7-bind-single | rig check | 0.45 % | 0.47 % | 0.63 % | 0.52 % | 0.75 % | 0.75 % |
| tsrs-bind | reported | 4.67 % | 2.57 % | 1.54 % | 4.72 % | 2.36 % | 4.72 % |
| tsrs-bind-single | reported | 0.49 % | 0.45 % | 0.61 % | 0.56 % | 0.45 % | 0.61 % |
| cs-o-aot-ws-bind | reported | 1.24 % | 2.15 % | 2.53 % | 2.66 % | 2.47 % | 2.66 % |
| cs-o-aot-ws-bind-single | rig check | 0.30 % | 0.36 % | 0.44 % | 0.27 % | 0.37 % | 0.44 % |
| cs-o-aot-srv-bind | reported | 1.86 % | 1.23 % | 3.54 % | 4.95 % | 2.77 % | 4.95 % |
| cs-o-aot-srv-bind-single | reported | 0.24 % | 0.31 % | 0.64 % | 1.09 % | 0.63 % | 1.09 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-bind | 0.0215 | 2.61 % | 16.3 % | 9 |
| tsgo7-bind-single | 0.0211 | 1.37 % | 3.9 % | 9 |
| tsrs-bind | 0.0193 | 2.42 % | 36.6 % | 8 |
| tsrs-bind-single | 0.0171 | 2.09 % | 5.6 % | 7 |
| cs-o-aot-ws-bind | 0.0233 | 0.60 % | 33.7 % | 10 |
| cs-o-aot-ws-bind-single | 0.0249 | 2.12 % | 6.1 % | 10 |
| cs-o-aot-srv-bind | 0.0280 | 2.98 % | 34.0 % | 12 |
| cs-o-aot-srv-bind-single | 0.0284 | 3.61 % | 6.8 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-bind | 91189752 | 8/5/3 | 51.3 |
| vscode | cs-o-aot-ws-bind-single | 53775480 | 7/4/1 | 33.0 |
| vscode | cs-o-aot-srv-bind | 91757560 | 6/4/3 | 57.1 |
| vscode | cs-o-aot-srv-bind-single | 53768936 | 4/2/1 | 35.0 |
| sentry | cs-o-aot-ws-bind | 65464952 | 6/5/3 | 31.7 |
| sentry | cs-o-aot-ws-bind-single | 30119192 | 3/1/0 | 17.3 |
| sentry | cs-o-aot-srv-bind | 70386064 | 7/6/5 | 11.7 |
| sentry | cs-o-aot-srv-bind-single | 30118520 | 3/1/0 | 9.3 |
| bluesky | cs-o-aot-ws-bind | 35530432 | 2/2/2 | 1.7 |
| bluesky | cs-o-aot-ws-bind-single | 6858984 | 0/0/0 | 0.0 |
| bluesky | cs-o-aot-srv-bind | 45138376 | 7/6/5 | 9.4 |
| bluesky | cs-o-aot-srv-bind-single | 6860696 | 2/1/0 | 4.8 |
| playwright | cs-o-aot-ws-bind | 39100160 | 2/2/2 | 1.6 |
| playwright | cs-o-aot-ws-bind-single | 7167176 | 0/0/0 | 0.0 |
| playwright | cs-o-aot-srv-bind | 45378432 | 7/6/5 | 8.7 |
| playwright | cs-o-aot-srv-bind-single | 7168888 | 2/1/0 | 4.5 |
| tldraw | cs-o-aot-ws-bind | 43421184 | 3/3/2 | 5.5 |
| tldraw | cs-o-aot-ws-bind-single | 10410448 | 1/0/0 | 5.6 |
| tldraw | cs-o-aot-srv-bind | 51517552 | 8/7/6 | 10.3 |
| tldraw | cs-o-aot-srv-bind-single | 10411120 | 2/1/0 | 4.4 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (40 files).
