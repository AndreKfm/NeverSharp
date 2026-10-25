# Parse-only benchmark, run 2026-10-25T05-13-51-186Z

**Status: protocol result.** All rig checks passed.

Label: binder-additional-run-after-BO4

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 99 % of 4150 MHz, idle before run 5.2 % (median of 1 s windows: 19.5, 4.8, 1.8, 3.1, 2.3, 2.5, 14.4, 14.4, 6, 5.6) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 68a7829636, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 99 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 5.2 % (range 1.8-19.5 %) | ok |
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
| vscode | tsgo7-bind | 1.325 | 0.0418 | 3.15 % | 8851 | 106.6 | 2764 | 2796 |
| vscode | tsgo7-bind-single | 6.427 | 0.0324 | 0.50 % | 1824 | 22.0 | 2690 | 2730 |
| vscode | tsrs-bind | 0.4036 | 0.0156 | 3.87 % | 29049 | 349.8 | 1148 | 2307 |
| vscode | tsrs-bind-single | 3.297 | 0.0112 | 0.34 % | 3556 | 42.8 | 1132 | 1273 |
| vscode | cs-o-aot-ws-bind | 0.4500 | 0.0077 | 1.72 % | 26053 | 313.7 | 1404 | 1724 |
| vscode | cs-o-aot-ws-bind-single | 3.934 | 0.0121 | 0.31 % | 2980 | 35.9 | 1363 | 1444 |
| vscode | cs-o-aot-srv-bind | 0.4395 | 0.0081 | 1.85 % | 26677 | 321.2 | 1414 | 1776 |
| vscode | cs-o-aot-srv-bind-single | 3.990 | 0.0061 | 0.15 % | 2938 | 35.4 | 1364 | 1453 |
| sentry | tsgo7-bind | 0.5247 | 0.0097 | 1.85 % | 17022 | 87.5 | 1045 | 1070 |
| sentry | tsgo7-bind-single | 2.871 | 0.0136 | 0.47 % | 3111 | 16.0 | 1011 | 1038 |
| sentry | tsrs-bind | 0.1837 | 0.0065 | 3.56 % | 48611 | 250.0 | 422 | 790 |
| sentry | tsrs-bind-single | 1.581 | 0.0094 | 0.60 % | 5650 | 29.1 | 409 | 641 |
| sentry | cs-o-aot-ws-bind | 0.2192 | 0.0050 | 2.27 % | 40745 | 209.6 | 520 | 737 |
| sentry | cs-o-aot-ws-bind-single | 1.818 | 0.0075 | 0.41 % | 4913 | 25.3 | 491 | 500 |
| sentry | cs-o-aot-srv-bind | 0.2097 | 0.0041 | 1.97 % | 42602 | 219.1 | 524 | 768 |
| sentry | cs-o-aot-srv-bind-single | 1.817 | 0.0058 | 0.32 % | 4915 | 25.3 | 493 | 509 |
| bluesky | tsgo7-bind | 0.1272 | 0.0029 | 2.27 % | 13271 | 56.6 | 206 | 219 |
| bluesky | tsgo7-bind-single | 0.5332 | 0.0042 | 0.78 % | 3166 | 13.5 | 186 | 196 |
| bluesky | tsrs-bind | 0.0547 | 0.0015 | 2.70 % | 30875 | 131.6 | 83 | 222 |
| bluesky | tsrs-bind-single | 0.3040 | 0.0026 | 0.86 % | 5552 | 23.7 | 76 | 110 |
| bluesky | cs-o-aot-ws-bind | 0.0624 | 0.0014 | 2.28 % | 27034 | 115.2 | 111 | 159 |
| bluesky | cs-o-aot-ws-bind-single | 0.3503 | 0.0026 | 0.73 % | 4819 | 20.5 | 94 | 95 |
| bluesky | cs-o-aot-srv-bind | 0.0727 | 0.0010 | 1.43 % | 23211 | 98.9 | 116 | 169 |
| bluesky | cs-o-aot-srv-bind-single | 0.3562 | 0.0030 | 0.85 % | 4739 | 20.2 | 97 | 106 |
| playwright | tsgo7-bind | 0.1788 | 0.0044 | 2.46 % | 7913 | 96.4 | 344 | 358 |
| playwright | tsgo7-bind-single | 0.7576 | 0.0025 | 0.33 % | 1868 | 22.7 | 325 | 338 |
| playwright | tsrs-bind | 0.0675 | 0.0016 | 2.32 % | 20971 | 255.4 | 147 | 389 |
| playwright | tsrs-bind-single | 0.3921 | 0.0017 | 0.44 % | 3609 | 44.0 | 138 | 205 |
| playwright | cs-o-aot-ws-bind | 0.0775 | 0.0019 | 2.41 % | 18267 | 222.5 | 204 | 315 |
| playwright | cs-o-aot-ws-bind-single | 0.4864 | 0.0026 | 0.53 % | 2909 | 35.4 | 176 | 188 |
| playwright | cs-o-aot-srv-bind | 0.0952 | 0.0041 | 4.28 % | 14868 | 181.1 | 210 | 331 |
| playwright | cs-o-aot-srv-bind-single | 0.4957 | 0.0049 | 0.98 % | 2855 | 34.8 | 179 | 199 |
| tldraw | tsgo7-bind | 0.1950 | 0.0047 | 2.39 % | 12865 | 69.9 | 349 | 366 |
| tldraw | tsgo7-bind-single | 0.8974 | 0.0028 | 0.31 % | 2795 | 15.2 | 327 | 339 |
| tldraw | tsrs-bind | 0.0797 | 0.0025 | 3.16 % | 31483 | 171.1 | 143 | 357 |
| tldraw | tsrs-bind-single | 0.4935 | 0.0039 | 0.78 % | 5082 | 27.6 | 135 | 198 |
| tldraw | cs-o-aot-ws-bind | 0.0901 | 0.0016 | 1.74 % | 27842 | 151.3 | 186 | 280 |
| tldraw | cs-o-aot-ws-bind-single | 0.5666 | 0.0026 | 0.46 % | 4427 | 24.1 | 164 | 169 |
| tldraw | cs-o-aot-srv-bind | 0.1023 | 0.0043 | 4.21 % | 24519 | 133.2 | 191 | 284 |
| tldraw | cs-o-aot-srv-bind-single | 0.5787 | 0.0055 | 0.94 % | 4334 | 23.5 | 166 | 180 |

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
| tsgo7-bind-single | 4.85 [4.68, 4.95] | 5.47 [5.40, 5.54] | 4.19 [4.10, 4.26] | 4.24 [4.14, 4.33] | 4.60 [4.53, 4.72] | 4.65 [4.59, 4.69] |
| tsrs-bind | 0.30 [0.29, 0.32] | 0.35 [0.34, 0.36] | 0.43 [0.41, 0.44] | 0.38 [0.37, 0.39] | 0.41 [0.40, 0.42] | 0.37 [0.37, 0.38] |
| tsrs-bind-single | 2.49 [2.40, 2.53] | 3.01 [2.97, 3.06] | 2.39 [2.34, 2.43] | 2.19 [2.14, 2.24] | 2.53 [2.49, 2.60] | 2.51 [2.48, 2.53] |
| cs-o-aot-ws-bind | 0.34 [0.33, 0.35] | 0.42 [0.41, 0.43] | 0.49 [0.48, 0.50] | 0.43 [0.42, 0.45] | 0.46 [0.45, 0.48] | 0.43 [0.42, 0.43] |
| cs-o-aot-ws-bind-single | 2.97 [2.87, 3.02] | 3.46 [3.42, 3.51] | 2.75 [2.69, 2.80] | 2.72 [2.65, 2.78] | 2.91 [2.86, 2.98] | 2.95 [2.92, 2.98] |
| cs-o-aot-srv-bind | 0.33 [0.32, 0.34] | 0.40 [0.39, 0.41] | 0.57 [0.56, 0.59] | 0.53 [0.51, 0.55] | 0.52 [0.51, 0.55] | 0.46 [0.46, 0.47] |
| cs-o-aot-srv-bind-single | 3.01 [2.91, 3.07] | 3.46 [3.42, 3.51] | 2.80 [2.73, 2.85] | 2.77 [2.70, 2.86] | 2.97 [2.92, 3.05] | 2.99 [2.96, 3.02] |

## Table 2d. Parse+bind: median wall-clock relative to `tsrs-bind`, with 95 % confidence interval

Secondary reference: parse+bind against tsrs 0.18.0 (tsrs_parser::parse_source_file_owned and tsrs_binder::bind_source_file per file, parallel). Same method as Table 2.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-bind | 3.28 [3.11, 3.46] | 2.86 [2.78, 2.96] | 2.33 [2.26, 2.41] | 2.65 [2.57, 2.73] | 2.45 [2.36, 2.52] | 2.69 [2.65, 2.74] |
| tsgo7-bind-single | 15.92 [15.10, 16.45] | 15.62 [15.26, 16.13] | 9.75 [9.53, 10.01] | 11.23 [11.01, 11.42] | 11.27 [10.97, 11.54] | 12.51 [12.34, 12.67] |
| tsrs-bind | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-bind-single | 8.17 [7.75, 8.43] | 8.60 [8.40, 8.89] | 5.56 [5.43, 5.71] | 5.81 [5.69, 5.91] | 6.19 [6.03, 6.35] | 6.76 [6.66, 6.84] |
| cs-o-aot-ws-bind | 1.12 [1.06, 1.16] | 1.19 [1.16, 1.24] | 1.14 [1.11, 1.18] | 1.15 [1.11, 1.17] | 1.13 [1.10, 1.17] | 1.15 [1.13, 1.16] |
| cs-o-aot-ws-bind-single | 9.75 [9.25, 10.06] | 9.89 [9.66, 10.21] | 6.41 [6.25, 6.57] | 7.21 [7.06, 7.33] | 7.11 [6.92, 7.30] | 7.95 [7.83, 8.04] |
| cs-o-aot-srv-bind | 1.09 [1.03, 1.13] | 1.14 [1.11, 1.18] | 1.33 [1.30, 1.39] | 1.41 [1.35, 1.44] | 1.28 [1.24, 1.35] | 1.25 [1.22, 1.27] |
| cs-o-aot-srv-bind-single | 9.89 [9.38, 10.21] | 9.89 [9.65, 10.21] | 6.52 [6.36, 6.70] | 7.35 [7.20, 7.52] | 7.26 [7.07, 7.46] | 8.06 [7.95, 8.16] |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: binder. Binding rows (rig check): tsgo7-bind-single, cs-o-aot-ws-bind-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-bind | reported | 3.15 % | 1.85 % | 2.27 % | 2.46 % | 2.39 % | 3.15 % |
| tsgo7-bind-single | rig check | 0.50 % | 0.47 % | 0.78 % | 0.33 % | 0.31 % | 0.78 % |
| tsrs-bind | reported | 3.87 % | 3.56 % | 2.70 % | 2.32 % | 3.16 % | 3.87 % |
| tsrs-bind-single | reported | 0.34 % | 0.60 % | 0.86 % | 0.44 % | 0.78 % | 0.86 % |
| cs-o-aot-ws-bind | reported | 1.72 % | 2.27 % | 2.28 % | 2.41 % | 1.74 % | 2.41 % |
| cs-o-aot-ws-bind-single | rig check | 0.31 % | 0.41 % | 0.73 % | 0.53 % | 0.46 % | 0.73 % |
| cs-o-aot-srv-bind | reported | 1.85 % | 1.97 % | 1.43 % | 4.28 % | 4.21 % | 4.28 % |
| cs-o-aot-srv-bind-single | reported | 0.15 % | 0.32 % | 0.85 % | 0.98 % | 0.94 % | 0.98 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-bind | 0.0221 | 0.72 % | 17.4 % | 9 |
| tsgo7-bind-single | 0.0209 | 2.05 % | 3.9 % | 9 |
| tsrs-bind | 0.0189 | 1.03 % | 34.5 % | 8 |
| tsrs-bind-single | 0.0171 | 1.26 % | 5.6 % | 7 |
| cs-o-aot-ws-bind | 0.0242 | 1.71 % | 38.8 % | 10 |
| cs-o-aot-ws-bind-single | 0.0246 | 1.46 % | 7.0 % | 10 |
| cs-o-aot-srv-bind | 0.0331 | 14.99 % | 45.5 % | 12 |
| cs-o-aot-srv-bind-single | 0.0280 | 1.62 % | 7.9 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-bind | 89171528 | 8/6/3 | 50.2 |
| vscode | cs-o-aot-ws-bind-single | 46339512 | 6/3/1 | 27.0 |
| vscode | cs-o-aot-srv-bind | 87545432 | 7/5/4 | 25.3 |
| vscode | cs-o-aot-srv-bind-single | 46335928 | 3/1/0 | 8.4 |
| sentry | cs-o-aot-ws-bind | 89671840 | 5/5/3 | 24.1 |
| sentry | cs-o-aot-ws-bind-single | 24846080 | 2/1/0 | 9.6 |
| sentry | cs-o-aot-srv-bind | 66400952 | 6/5/4 | 7.8 |
| sentry | cs-o-aot-srv-bind-single | 24845408 | 3/1/0 | 7.2 |
| bluesky | cs-o-aot-ws-bind | 40319400 | 2/2/2 | 1.7 |
| bluesky | cs-o-aot-ws-bind-single | 6210208 | 0/0/0 | 0.0 |
| bluesky | cs-o-aot-srv-bind | 45607528 | 7/6/5 | 9.3 |
| bluesky | cs-o-aot-srv-bind-single | 6211264 | 1/0/0 | 2.0 |
| playwright | cs-o-aot-ws-bind | 43337616 | 2/2/2 | 1.5 |
| playwright | cs-o-aot-ws-bind-single | 6538376 | 0/0/0 | 0.0 |
| playwright | cs-o-aot-srv-bind | 51212328 | 7/6/5 | 8.2 |
| playwright | cs-o-aot-srv-bind-single | 6539432 | 1/0/0 | 1.7 |
| tldraw | cs-o-aot-ws-bind | 45931328 | 3/3/2 | 5.3 |
| tldraw | cs-o-aot-ws-bind-single | 9121040 | 0/0/0 | 0.0 |
| tldraw | cs-o-aot-srv-bind | 50751168 | 8/6/5 | 11.7 |
| tldraw | cs-o-aot-srv-bind-single | 9122768 | 2/1/0 | 4.8 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (40 files).
