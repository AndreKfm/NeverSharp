# Parse-only benchmark, run 2026-10-24T15-16-07-263Z

**Status: protocol result.** All rig checks passed.

Label: binder-additional-run-after-BO2

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 5.5 % (median of 1 s windows: 3.4, 2.1, 1.3, 5.4, 8.1, 15, 8.7, 4.4, 8.3, 5.7) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 07ac6df0e3, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 5.5 % (range 1.3-15.0 %) | ok |
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
| vscode | tsgo7-bind | 1.360 | 0.0306 | 2.25 % | 8622 | 103.8 | 2753 | 2800 |
| vscode | tsgo7-bind-single | 6.440 | 0.0217 | 0.34 % | 1820 | 21.9 | 2681 | 2743 |
| vscode | tsrs-bind | 0.4373 | 0.0191 | 4.37 % | 26809 | 322.8 | 1148 | 2308 |
| vscode | tsrs-bind-single | 3.308 | 0.0111 | 0.34 % | 3544 | 42.7 | 1132 | 1273 |
| vscode | cs-o-aot-ws-bind | 0.5159 | 0.0068 | 1.32 % | 22725 | 273.7 | 1680 | 2043 |
| vscode | cs-o-aot-ws-bind-single | 4.091 | 0.0073 | 0.18 % | 2866 | 34.5 | 1648 | 1774 |
| vscode | cs-o-aot-srv-bind | 0.4912 | 0.0111 | 2.25 % | 23870 | 287.4 | 1691 | 2058 |
| vscode | cs-o-aot-srv-bind-single | 4.115 | 0.0152 | 0.37 % | 2849 | 34.3 | 1670 | 1801 |
| sentry | tsgo7-bind | 0.5383 | 0.0153 | 2.85 % | 16593 | 85.3 | 1070 | 1093 |
| sentry | tsgo7-bind-single | 2.882 | 0.0129 | 0.45 % | 3099 | 15.9 | 1010 | 1041 |
| sentry | tsrs-bind | 0.1893 | 0.0020 | 1.07 % | 47188 | 242.7 | 421 | 796 |
| sentry | tsrs-bind-single | 1.599 | 0.0082 | 0.51 % | 5585 | 28.7 | 409 | 641 |
| sentry | cs-o-aot-ws-bind | 0.2423 | 0.0038 | 1.59 % | 36871 | 189.6 | 628 | 826 |
| sentry | cs-o-aot-ws-bind-single | 1.880 | 0.0100 | 0.53 % | 4752 | 24.4 | 616 | 633 |
| sentry | cs-o-aot-srv-bind | 0.2317 | 0.0054 | 2.35 % | 38551 | 198.3 | 637 | 855 |
| sentry | cs-o-aot-srv-bind-single | 1.872 | 0.0041 | 0.22 % | 4771 | 24.5 | 614 | 636 |
| bluesky | tsgo7-bind | 0.1293 | 0.0033 | 2.53 % | 13058 | 55.7 | 203 | 216 |
| bluesky | tsgo7-bind-single | 0.5359 | 0.0035 | 0.64 % | 3150 | 13.4 | 187 | 194 |
| bluesky | tsrs-bind | 0.0539 | 0.0018 | 3.31 % | 31333 | 133.5 | 83 | 217 |
| bluesky | tsrs-bind-single | 0.3051 | 0.0023 | 0.74 % | 5532 | 23.6 | 76 | 110 |
| bluesky | cs-o-aot-ws-bind | 0.0655 | 0.0007 | 1.12 % | 25777 | 109.9 | 125 | 176 |
| bluesky | cs-o-aot-ws-bind-single | 0.3607 | 0.0023 | 0.63 % | 4680 | 19.9 | 114 | 115 |
| bluesky | cs-o-aot-srv-bind | 0.0778 | 0.0013 | 1.72 % | 21687 | 92.4 | 130 | 182 |
| bluesky | cs-o-aot-srv-bind-single | 0.3695 | 0.0033 | 0.89 % | 4569 | 19.5 | 118 | 127 |
| playwright | tsgo7-bind | 0.1780 | 0.0032 | 1.80 % | 7947 | 96.8 | 341 | 355 |
| playwright | tsgo7-bind-single | 0.7652 | 0.0044 | 0.58 % | 1849 | 22.5 | 325 | 335 |
| playwright | tsrs-bind | 0.0686 | 0.0010 | 1.41 % | 20616 | 251.1 | 147 | 387 |
| playwright | tsrs-bind-single | 0.3956 | 0.0019 | 0.48 % | 3577 | 43.6 | 138 | 205 |
| playwright | cs-o-aot-ws-bind | 0.0829 | 0.0012 | 1.49 % | 17059 | 207.8 | 227 | 347 |
| playwright | cs-o-aot-ws-bind-single | 0.5023 | 0.0015 | 0.29 % | 2817 | 34.3 | 209 | 227 |
| playwright | cs-o-aot-srv-bind | 0.0993 | 0.0042 | 4.19 % | 14257 | 173.6 | 231 | 368 |
| playwright | cs-o-aot-srv-bind-single | 0.5099 | 0.0031 | 0.61 % | 2775 | 33.8 | 210 | 238 |
| tldraw | tsgo7-bind | 0.1921 | 0.0041 | 2.12 % | 13057 | 70.9 | 345 | 359 |
| tldraw | tsgo7-bind-single | 0.8984 | 0.0059 | 0.66 % | 2792 | 15.2 | 328 | 336 |
| tldraw | tsrs-bind | 0.0767 | 0.0024 | 3.07 % | 32699 | 177.7 | 143 | 368 |
| tldraw | tsrs-bind-single | 0.4936 | 0.0025 | 0.50 % | 5081 | 27.6 | 135 | 198 |
| tldraw | cs-o-aot-ws-bind | 0.0941 | 0.0018 | 1.89 % | 26646 | 144.8 | 216 | 310 |
| tldraw | cs-o-aot-ws-bind-single | 0.5853 | 0.0020 | 0.34 % | 4285 | 23.3 | 206 | 213 |
| tldraw | cs-o-aot-srv-bind | 0.1099 | 0.0036 | 3.24 % | 22825 | 124.0 | 221 | 325 |
| tldraw | cs-o-aot-srv-bind-single | 0.5907 | 0.0034 | 0.57 % | 4246 | 23.1 | 203 | 220 |

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
| tsgo7-bind-single | 4.74 [4.69, 4.88] | 5.35 [5.24, 5.49] | 4.15 [4.10, 4.24] | 4.30 [4.18, 4.35] | 4.68 [4.61, 4.77] | 4.62 [4.59, 4.67] |
| tsrs-bind | 0.32 [0.30, 0.33] | 0.35 [0.34, 0.36] | 0.42 [0.40, 0.43] | 0.39 [0.38, 0.39] | 0.40 [0.39, 0.41] | 0.37 [0.37, 0.38] |
| tsrs-bind-single | 2.43 [2.41, 2.50] | 2.97 [2.90, 3.05] | 2.36 [2.33, 2.42] | 2.22 [2.16, 2.25] | 2.57 [2.53, 2.62] | 2.50 [2.48, 2.53] |
| cs-o-aot-ws-bind | 0.38 [0.37, 0.39] | 0.45 [0.44, 0.46] | 0.51 [0.50, 0.52] | 0.47 [0.45, 0.47] | 0.49 [0.48, 0.50] | 0.46 [0.45, 0.46] |
| cs-o-aot-ws-bind-single | 3.01 [2.98, 3.10] | 3.49 [3.41, 3.58] | 2.79 [2.76, 2.86] | 2.82 [2.75, 2.85] | 3.05 [3.01, 3.11] | 3.02 [3.00, 3.06] |
| cs-o-aot-srv-bind | 0.36 [0.35, 0.37] | 0.43 [0.42, 0.45] | 0.60 [0.59, 0.62] | 0.56 [0.54, 0.58] | 0.57 [0.55, 0.59] | 0.50 [0.49, 0.50] |
| cs-o-aot-srv-bind-single | 3.03 [2.99, 3.11] | 3.48 [3.41, 3.57] | 2.86 [2.82, 2.93] | 2.86 [2.78, 2.90] | 3.08 [3.03, 3.13] | 3.05 [3.03, 3.09] |

## Table 2d. Parse+bind: median wall-clock relative to `tsrs-bind`, with 95 % confidence interval

Secondary reference: parse+bind against tsrs 0.18.0 (tsrs_parser::parse_source_file_owned and tsrs_binder::bind_source_file per file, parallel). Same method as Table 2.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-bind | 3.11 [3.00, 3.29] | 2.84 [2.75, 2.91] | 2.40 [2.30, 2.47] | 2.59 [2.55, 2.67] | 2.50 [2.45, 2.56] | 2.68 [2.64, 2.72] |
| tsgo7-bind-single | 14.73 [14.35, 15.59] | 15.23 [14.99, 15.33] | 9.95 [9.60, 10.23] | 11.15 [11.03, 11.25] | 11.71 [11.52, 11.93] | 12.38 [12.26, 12.54] |
| tsrs-bind | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-bind-single | 7.56 [7.37, 8.01] | 8.45 [8.31, 8.50] | 5.66 [5.46, 5.83] | 5.76 [5.70, 5.82] | 6.44 [6.32, 6.56] | 6.69 [6.63, 6.78] |
| cs-o-aot-ws-bind | 1.18 [1.14, 1.25] | 1.28 [1.26, 1.30] | 1.22 [1.17, 1.25] | 1.21 [1.19, 1.22] | 1.23 [1.20, 1.26] | 1.22 [1.21, 1.24] |
| cs-o-aot-ws-bind-single | 9.35 [9.11, 9.91] | 9.93 [9.76, 9.99] | 6.70 [6.46, 6.89] | 7.32 [7.24, 7.38] | 7.63 [7.52, 7.78] | 8.09 [8.02, 8.20] |
| cs-o-aot-srv-bind | 1.12 [1.09, 1.19] | 1.22 [1.20, 1.25] | 1.44 [1.39, 1.50] | 1.45 [1.40, 1.51] | 1.43 [1.38, 1.48] | 1.33 [1.31, 1.35] |
| cs-o-aot-srv-bind-single | 9.41 [9.17, 9.96] | 9.89 [9.74, 9.96] | 6.86 [6.62, 7.07] | 7.43 [7.34, 7.50] | 7.70 [7.58, 7.84] | 8.18 [8.10, 8.28] |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: binder. Binding rows (rig check): tsgo7-bind-single, cs-o-aot-ws-bind-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-bind | reported | 2.25 % | 2.85 % | 2.53 % | 1.80 % | 2.12 % | 2.85 % |
| tsgo7-bind-single | rig check | 0.34 % | 0.45 % | 0.64 % | 0.58 % | 0.66 % | 0.66 % |
| tsrs-bind | reported | 4.37 % | 1.07 % | 3.31 % | 1.41 % | 3.07 % | 4.37 % |
| tsrs-bind-single | reported | 0.34 % | 0.51 % | 0.74 % | 0.48 % | 0.50 % | 0.74 % |
| cs-o-aot-ws-bind | reported | 1.32 % | 1.59 % | 1.12 % | 1.49 % | 1.89 % | 1.89 % |
| cs-o-aot-ws-bind-single | rig check | 0.18 % | 0.53 % | 0.63 % | 0.29 % | 0.34 % | 0.63 % |
| cs-o-aot-srv-bind | reported | 2.25 % | 2.35 % | 1.72 % | 4.19 % | 3.24 % | 4.19 % |
| cs-o-aot-srv-bind-single | reported | 0.37 % | 0.22 % | 0.89 % | 0.61 % | 0.57 % | 0.89 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-bind | 0.0217 | 2.40 % | 16.8 % | 9 |
| tsgo7-bind-single | 0.0205 | 1.32 % | 3.8 % | 9 |
| tsrs-bind | 0.0189 | 1.77 % | 35.1 % | 8 |
| tsrs-bind-single | 0.0184 | 1.44 % | 6.0 % | 7 |
| cs-o-aot-ws-bind | 0.0245 | 1.84 % | 37.4 % | 10 |
| cs-o-aot-ws-bind-single | 0.0237 | 1.85 % | 6.6 % | 10 |
| cs-o-aot-srv-bind | 0.0274 | 2.64 % | 35.2 % | 12 |
| cs-o-aot-srv-bind-single | 0.0272 | 1.31 % | 7.4 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-bind | 95028112 | 9/6/3 | 52.0 |
| vscode | cs-o-aot-ws-bind-single | 53597968 | 7/4/1 | 32.8 |
| vscode | cs-o-aot-srv-bind | 90778904 | 7/5/4 | 28.7 |
| vscode | cs-o-aot-srv-bind-single | 53595552 | 4/2/1 | 32.9 |
| sentry | cs-o-aot-ws-bind | 65769128 | 6/5/3 | 29.8 |
| sentry | cs-o-aot-ws-bind-single | 30042592 | 3/1/0 | 18.7 |
| sentry | cs-o-aot-srv-bind | 70435824 | 7/6/5 | 10.0 |
| sentry | cs-o-aot-srv-bind-single | 30041920 | 3/1/0 | 9.5 |
| bluesky | cs-o-aot-ws-bind | 35626336 | 2/2/2 | 1.7 |
| bluesky | cs-o-aot-ws-bind-single | 6846512 | 0/0/0 | 0.0 |
| bluesky | cs-o-aot-srv-bind | 53200560 | 8/7/6 | 10.3 |
| bluesky | cs-o-aot-srv-bind-single | 6848224 | 2/1/0 | 4.6 |
| playwright | cs-o-aot-ws-bind | 39414600 | 2/2/2 | 1.6 |
| playwright | cs-o-aot-ws-bind-single | 7145888 | 0/0/0 | 0.0 |
| playwright | cs-o-aot-srv-bind | 46224584 | 7/6/5 | 8.2 |
| playwright | cs-o-aot-srv-bind-single | 7146928 | 1/0/0 | 1.6 |
| tldraw | cs-o-aot-ws-bind | 42319936 | 3/3/2 | 5.7 |
| tldraw | cs-o-aot-ws-bind-single | 10389040 | 1/0/0 | 6.0 |
| tldraw | cs-o-aot-srv-bind | 55207016 | 8/7/6 | 10.9 |
| tldraw | cs-o-aot-srv-bind-single | 10389712 | 2/1/0 | 4.6 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (40 files).
