# Parse-only benchmark, run 2026-10-31T10-13-22-286Z

**Status: protocol result.** All rig checks passed.

Label: checker additional run 1 (after CO3 and 7ec02cf)

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 6.1 % (median of 1 s windows: 10.2, 13.8, 10.6, 4.4, 3.8, 5, 0.7, 1.5, 9.6, 7.2) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 7ec02cf3b3, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 6.1 % (range 0.7-13.8 %) | ok |
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

- `tsgo7-check`: typescript-go 7.0.2, program parse+bind+check, parallel (default checker pool)
- `tsgo7-check-single`: typescript-go 7.0.2, program parse+bind+check, single-threaded
- `tsrs-check`: tsrs 0.18.0 (Rust), program parse+bind+check, parallel (tsrs's default checker pool)
- `tsrs-check-single`: tsrs 0.18.0 (Rust), program parse+bind+check, single-threaded
- `cs-o-aot-ws-check`: C# Track O, NativeAOT, Workstation GC, program parse+bind+check, parallel (4 checkers) (checker endpoint)
- `cs-o-aot-ws-check-single`: C# Track O, NativeAOT, Workstation GC, program parse+bind+check, single-threaded
- `cs-o-aot-srv-check`: C# Track O, NativeAOT, Server GC, program parse+bind+check, parallel (4 checkers)
- `cs-o-aot-srv-check-single`: C# Track O, NativeAOT, Server GC, program parse+bind+check, single-threaded

## Table 1. Wall-clock, throughput, peak memory (parse-only; `*-check` rows: the file list as one program, parse+bind+check)

| Repository | Configuration | Median (s) | MAD (s) | MAD/median | Files/s | MB/s | Peak working set (MiB) | Peak commit (MiB) |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| vscode | tsgo7-check | 14.21 | 0.0819 | 0.58 % | 825 | 9.9 | 6996 | 7012 |
| vscode | tsgo7-check-single | 42.61 | 0.1326 | 0.31 % | 275 | 3.3 | 5864 | 5879 |
| vscode | tsrs-check | 2.209 | 0.0430 | 1.95 % | 5307 | 63.9 | 2769 | 4498 |
| vscode | tsrs-check-single | 19.20 | 0.0421 | 0.22 % | 611 | 7.4 | 2164 | 2628 |
| vscode | cs-o-aot-ws-check | 15.01 | 0.0401 | 0.27 % | 781 | 9.4 | 4825 | 5444 |
| vscode | cs-o-aot-ws-check-single | 37.74 | 0.0278 | 0.07 % | 311 | 3.7 | 3922 | 4249 |
| vscode | cs-o-aot-srv-check | 12.01 | 0.0286 | 0.24 % | 977 | 11.8 | 5123 | 5726 |
| vscode | cs-o-aot-srv-check-single | 36.52 | 0.0417 | 0.11 % | 321 | 3.9 | 4199 | 4485 |
| sentry | tsgo7-check | 4.764 | 0.0256 | 0.54 % | 1875 | 9.6 | 2109 | 2127 |
| sentry | tsgo7-check-single | 17.40 | 0.0365 | 0.21 % | 513 | 2.6 | 1901 | 1916 |
| sentry | tsrs-check | 0.9238 | 0.0167 | 1.81 % | 9669 | 49.7 | 994 | 2220 |
| sentry | tsrs-check-single | 8.140 | 0.0201 | 0.25 % | 1097 | 5.6 | 886 | 1333 |
| sentry | cs-o-aot-ws-check | 4.013 | 0.0188 | 0.47 % | 2226 | 11.4 | 1362 | 1630 |
| sentry | cs-o-aot-ws-check-single | 10.70 | 0.0235 | 0.22 % | 835 | 4.3 | 1154 | 1199 |
| sentry | cs-o-aot-srv-check | 3.391 | 0.0195 | 0.58 % | 2634 | 13.5 | 1624 | 1928 |
| sentry | cs-o-aot-srv-check-single | 10.53 | 0.0292 | 0.28 % | 848 | 4.4 | 1412 | 1514 |
| bluesky | tsgo7-check | 0.8039 | 0.0055 | 0.68 % | 2100 | 8.9 | 428 | 433 |
| bluesky | tsgo7-check-single | 2.241 | 0.0105 | 0.47 % | 753 | 3.2 | 422 | 419 |
| bluesky | tsrs-check | 0.2026 | 0.0043 | 2.13 % | 8331 | 35.5 | 259 | 818 |
| bluesky | tsrs-check-single | 1.225 | 0.0063 | 0.51 % | 1377 | 5.9 | 199 | 239 |
| bluesky | cs-o-aot-ws-check | 1.055 | 0.0056 | 0.53 % | 1600 | 6.8 | 310 | 395 |
| bluesky | cs-o-aot-ws-check-single | 2.318 | 0.0051 | 0.22 % | 728 | 3.1 | 267 | 300 |
| bluesky | cs-o-aot-srv-check | 0.9498 | 0.0089 | 0.94 % | 1777 | 7.6 | 421 | 522 |
| bluesky | cs-o-aot-srv-check-single | 2.315 | 0.0157 | 0.68 % | 729 | 3.1 | 329 | 365 |
| playwright | tsgo7-check | 1.470 | 0.0084 | 0.57 % | 963 | 11.7 | 908 | 909 |
| playwright | tsgo7-check-single | 3.922 | 0.0217 | 0.55 % | 361 | 4.4 | 815 | 815 |
| playwright | tsrs-check | 0.3028 | 0.0073 | 2.42 % | 4673 | 56.9 | 460 | 1209 |
| playwright | tsrs-check-single | 1.859 | 0.0121 | 0.65 % | 761 | 9.3 | 348 | 406 |
| playwright | cs-o-aot-ws-check | 1.849 | 0.0086 | 0.47 % | 765 | 9.3 | 632 | 775 |
| playwright | cs-o-aot-ws-check-single | 3.993 | 0.0104 | 0.26 % | 354 | 4.3 | 491 | 537 |
| playwright | cs-o-aot-srv-check | 1.611 | 0.0155 | 0.96 % | 878 | 10.7 | 800 | 980 |
| playwright | cs-o-aot-srv-check-single | 3.992 | 0.0111 | 0.28 % | 354 | 4.3 | 579 | 637 |
| tldraw | tsgo7-check | 1.936 | 0.0088 | 0.45 % | 1295 | 7.0 | 881 | 883 |
| tldraw | tsgo7-check-single | 6.184 | 0.0118 | 0.19 % | 406 | 2.2 | 745 | 744 |
| tldraw | tsrs-check | 0.3776 | 0.0104 | 2.75 % | 6641 | 36.1 | 450 | 1209 |
| tldraw | tsrs-check-single | 2.755 | 0.0142 | 0.52 % | 910 | 4.9 | 330 | 405 |
| tldraw | cs-o-aot-ws-check | 1.827 | 0.0088 | 0.48 % | 1373 | 7.5 | 596 | 716 |
| tldraw | cs-o-aot-ws-check-single | 4.078 | 0.0252 | 0.62 % | 615 | 3.3 | 445 | 477 |
| tldraw | cs-o-aot-srv-check | 1.583 | 0.0186 | 1.17 % | 1584 | 8.6 | 798 | 942 |
| tldraw | cs-o-aot-srv-check-single | 3.964 | 0.0135 | 0.34 % | 633 | 3.4 | 540 | 577 |

## Table 1c. Program check: diagnostics reported (driver report of the validation run)

Semantic diagnostics of every program file plus the global diagnostics. The C# configurations check with TypeScript 6.0.3's checker (Track O, Gate C), typescript-go and tsrs with TypeScript 7's, which differs in places: the counts show that checking happened; the C# diagnostics were compared once with tsc 6.0.3's (harness/bench/check-equivalence.mjs).

| Configuration | vscode | sentry | bluesky | playwright | tldraw |
| --- | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | 101933 | 237531 | 33337 | 30838 | 52608 |
| tsgo7-check-single | 101933 | 237531 | 33337 | 30838 | 52604 |
| tsrs-check | 101941 | 237533 | 33337 | 30838 | 52612 |
| tsrs-check-single | 101941 | 237533 | 33337 | 30838 | 52612 |
| cs-o-aot-ws-check | 101474 | 237529 | 33337 | 30789 | 51900 |
| cs-o-aot-ws-check-single | 101493 | 237529 | 33337 | 30808 | 51900 |
| cs-o-aot-srv-check | 101474 | 237529 | 33337 | 30789 | 51900 |
| cs-o-aot-srv-check-single | 101493 | 237529 | 33337 | 30808 | 51900 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |

## Table 2e. Program check: median wall-clock relative to `tsgo7-check`, with 95 % confidence interval

Endpoint of draft pre-registration §17: each repository's file list as one program, parse+bind+check, against typescript-go 7.0.2 (compiler.NewProgram, GetSemanticDiagnostics and GetGlobalDiagnostics on its default checker pool of 4 checkers). Same method as Table 2. typescript-go's checker ports TypeScript 7 and differs from 6.0.3's in places.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-check-single | 3.00 [2.99, 3.01] | 3.65 [3.62, 3.67] | 2.79 [2.76, 2.80] | 2.67 [2.65, 2.69] | 3.19 [3.18, 3.21] | 3.04 [3.03, 3.05] |
| tsrs-check | 0.16 [0.15, 0.16] | 0.19 [0.19, 0.20] | 0.25 [0.25, 0.26] | 0.21 [0.20, 0.21] | 0.20 [0.19, 0.20] | 0.20 [0.20, 0.20] |
| tsrs-check-single | 1.35 [1.35, 1.36] | 1.71 [1.70, 1.71] | 1.52 [1.51, 1.54] | 1.26 [1.26, 1.28] | 1.42 [1.42, 1.43] | 1.45 [1.44, 1.45] |
| cs-o-aot-ws-check | 1.06 [1.05, 1.06] | 0.84 [0.84, 0.85] | 1.31 [1.30, 1.32] | 1.26 [1.25, 1.27] | 0.94 [0.94, 0.95] | 1.07 [1.06, 1.07] |
| cs-o-aot-ws-check-single | 2.66 [2.64, 2.67] | 2.25 [2.23, 2.26] | 2.88 [2.86, 2.89] | 2.72 [2.71, 2.74] | 2.11 [2.09, 2.12] | 2.50 [2.50, 2.51] |
| cs-o-aot-srv-check | 0.85 [0.84, 0.85] | 0.71 [0.70, 0.71] | 1.18 [1.17, 1.19] | 1.10 [1.09, 1.11] | 0.82 [0.81, 0.82] | 0.91 [0.91, 0.92] |
| cs-o-aot-srv-check-single | 2.57 [2.56, 2.58] | 2.21 [2.19, 2.22] | 2.88 [2.85, 2.90] | 2.72 [2.70, 2.73] | 2.05 [2.04, 2.06] | 2.46 [2.46, 2.47] |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: check. Binding rows (rig check): tsgo7-check-single, cs-o-aot-ws-check-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | reported | 0.58 % | 0.54 % | 0.68 % | 0.57 % | 0.45 % | 0.68 % |
| tsgo7-check-single | rig check | 0.31 % | 0.21 % | 0.47 % | 0.55 % | 0.19 % | 0.55 % |
| tsrs-check | reported | 1.95 % | 1.81 % | 2.13 % | 2.42 % | 2.75 % | 2.75 % |
| tsrs-check-single | reported | 0.22 % | 0.25 % | 0.51 % | 0.65 % | 0.52 % | 0.65 % |
| cs-o-aot-ws-check | reported | 0.27 % | 0.47 % | 0.53 % | 0.47 % | 0.48 % | 0.53 % |
| cs-o-aot-ws-check-single | rig check | 0.07 % | 0.22 % | 0.22 % | 0.26 % | 0.62 % | 0.62 % |
| cs-o-aot-srv-check | reported | 0.24 % | 0.58 % | 0.94 % | 0.96 % | 1.17 % | 1.17 % |
| cs-o-aot-srv-check-single | reported | 0.11 % | 0.28 % | 0.68 % | 0.28 % | 0.34 % | 0.68 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-check | 0.0213 | 2.02 % | 2.6 % | 10 |
| tsgo7-check-single | 0.0220 | 1.47 % | 1.0 % | 10 |
| tsrs-check | 0.0171 | 2.93 % | 8.4 % | 6 |
| tsrs-check-single | 0.0170 | 1.95 % | 1.4 % | 6 |
| cs-o-aot-ws-check | 0.0238 | 0.67 % | 2.3 % | 10 |
| cs-o-aot-ws-check-single | 0.0237 | 0.66 % | 1.0 % | 10 |
| cs-o-aot-srv-check | 0.0274 | 1.12 % | 2.9 % | 12 |
| cs-o-aot-srv-check-single | 0.0309 | 4.72 % | 1.3 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-check | 15665709016 | 1472/429/9 | 3547.8 |
| vscode | cs-o-aot-ws-check-single | 13721886672 | 1311/369/8 | 2003.2 |
| vscode | cs-o-aot-srv-check | 15676593832 | 76/45/28 | 637.5 |
| vscode | cs-o-aot-srv-check-single | 13721923232 | 69/35/20 | 810.5 |
| sentry | cs-o-aot-ws-check | 3923387864 | 393/118/8 | 825.2 |
| sentry | cs-o-aot-ws-check-single | 3645709880 | 367/113/6 | 555.8 |
| sentry | cs-o-aot-srv-check | 3922533808 | 46/26/19 | 252.3 |
| sentry | cs-o-aot-srv-check-single | 3645778008 | 47/25/20 | 354.5 |
| bluesky | cs-o-aot-ws-check | 822250632 | 84/29/6 | 160.7 |
| bluesky | cs-o-aot-ws-check-single | 740950152 | 76/24/4 | 111.9 |
| bluesky | cs-o-aot-srv-check | 829279336 | 26/17/12 | 66.1 |
| bluesky | cs-o-aot-srv-check-single | 740974272 | 24/14/11 | 103.7 |
| playwright | cs-o-aot-ws-check | 1613409800 | 150/49/8 | 322.7 |
| playwright | cs-o-aot-ws-check-single | 1303379328 | 125/40/5 | 192.5 |
| playwright | cs-o-aot-srv-check | 1623979504 | 34/21/17 | 108.9 |
| playwright | cs-o-aot-srv-check-single | 1303420368 | 31/18/14 | 157.7 |
| tldraw | cs-o-aot-ws-check | 1685865784 | 163/52/7 | 369.2 |
| tldraw | cs-o-aot-ws-check-single | 1382697008 | 142/42/6 | 228.7 |
| tldraw | cs-o-aot-srv-check | 1692167952 | 36/23/18 | 124.9 |
| tldraw | cs-o-aot-srv-check-single | 1382723672 | 26/15/12 | 143.0 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (40 files).
