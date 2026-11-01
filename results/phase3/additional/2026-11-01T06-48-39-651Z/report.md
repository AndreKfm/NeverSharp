# Parse-only benchmark, run 2026-11-01T06-48-39-651Z

**Status: protocol result.** All rig checks passed.

Label: checker additional run 2 (after CO4, db97ac0)

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 4.2 % (median of 1 s windows: 9.6, 21.6, 14.9, 2.3, 2.7, 0.4, 2.9, 3.2, 10.2, 5.3) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit db97ac0925, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 4.2 % (range 0.4-21.6 %) | ok |
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
| vscode | tsgo7-check | 14.48 | 0.2044 | 1.41 % | 810 | 9.8 | 6991 | 7014 |
| vscode | tsgo7-check-single | 42.72 | 0.4686 | 1.10 % | 274 | 3.3 | 5931 | 5954 |
| vscode | tsrs-check | 2.284 | 0.0517 | 2.26 % | 5133 | 61.8 | 2746 | 4487 |
| vscode | tsrs-check-single | 19.79 | 0.1371 | 0.69 % | 593 | 7.1 | 2143 | 2628 |
| vscode | cs-o-aot-ws-check | 15.01 | 0.0783 | 0.52 % | 781 | 9.4 | 4796 | 5384 |
| vscode | cs-o-aot-ws-check-single | 37.95 | 0.2044 | 0.54 % | 309 | 3.7 | 3927 | 4256 |
| vscode | cs-o-aot-srv-check | 11.84 | 0.2507 | 2.12 % | 990 | 11.9 | 5229 | 5917 |
| vscode | cs-o-aot-srv-check-single | 37.26 | 0.2653 | 0.71 % | 315 | 3.8 | 4340 | 4689 |
| sentry | tsgo7-check | 4.817 | 0.0250 | 0.52 % | 1854 | 9.5 | 2024 | 2045 |
| sentry | tsgo7-check-single | 17.56 | 0.1065 | 0.61 % | 509 | 2.6 | 1892 | 1914 |
| sentry | tsrs-check | 0.9432 | 0.0187 | 1.98 % | 9470 | 48.7 | 997 | 2179 |
| sentry | tsrs-check-single | 8.191 | 0.0237 | 0.29 % | 1091 | 5.6 | 866 | 1333 |
| sentry | cs-o-aot-ws-check | 3.834 | 0.0521 | 1.36 % | 2330 | 12.0 | 1336 | 1601 |
| sentry | cs-o-aot-ws-check-single | 10.74 | 0.0207 | 0.19 % | 832 | 4.3 | 1186 | 1252 |
| sentry | cs-o-aot-srv-check | 2.928 | 0.0242 | 0.83 % | 3050 | 15.7 | 1649 | 1965 |
| sentry | cs-o-aot-srv-check-single | 10.60 | 0.0842 | 0.79 % | 843 | 4.3 | 1413 | 1512 |
| bluesky | tsgo7-check | 0.8154 | 0.0048 | 0.58 % | 2070 | 8.8 | 429 | 434 |
| bluesky | tsgo7-check-single | 2.263 | 0.0117 | 0.52 % | 746 | 3.2 | 395 | 398 |
| bluesky | tsrs-check | 0.2040 | 0.0038 | 1.87 % | 8274 | 35.3 | 259 | 830 |
| bluesky | tsrs-check-single | 1.247 | 0.0097 | 0.78 % | 1354 | 5.8 | 178 | 239 |
| bluesky | cs-o-aot-ws-check | 0.9928 | 0.0105 | 1.06 % | 1700 | 7.2 | 312 | 395 |
| bluesky | cs-o-aot-ws-check-single | 2.306 | 0.0115 | 0.50 % | 732 | 3.1 | 267 | 300 |
| bluesky | cs-o-aot-srv-check | 0.8659 | 0.0121 | 1.40 % | 1949 | 8.3 | 408 | 507 |
| bluesky | cs-o-aot-srv-check-single | 2.305 | 0.0137 | 0.59 % | 732 | 3.1 | 329 | 365 |
| playwright | tsgo7-check | 1.483 | 0.0133 | 0.90 % | 954 | 11.6 | 907 | 913 |
| playwright | tsgo7-check-single | 3.940 | 0.0179 | 0.46 % | 359 | 4.4 | 781 | 786 |
| playwright | tsrs-check | 0.2991 | 0.0057 | 1.92 % | 4731 | 57.6 | 463 | 1235 |
| playwright | tsrs-check-single | 1.864 | 0.0090 | 0.48 % | 759 | 9.2 | 328 | 406 |
| playwright | cs-o-aot-ws-check | 1.837 | 0.0184 | 1.00 % | 770 | 9.4 | 638 | 795 |
| playwright | cs-o-aot-ws-check-single | 3.994 | 0.0130 | 0.33 % | 354 | 4.3 | 489 | 543 |
| playwright | cs-o-aot-srv-check | 1.610 | 0.0098 | 0.61 % | 879 | 10.7 | 829 | 1008 |
| playwright | cs-o-aot-srv-check-single | 3.979 | 0.0226 | 0.57 % | 356 | 4.3 | 555 | 616 |
| tldraw | tsgo7-check | 1.953 | 0.0063 | 0.32 % | 1284 | 7.0 | 834 | 842 |
| tldraw | tsgo7-check-single | 6.185 | 0.0152 | 0.25 % | 405 | 2.2 | 733 | 739 |
| tldraw | tsrs-check | 0.3784 | 0.0067 | 1.76 % | 6628 | 36.0 | 446 | 1201 |
| tldraw | tsrs-check-single | 2.784 | 0.0192 | 0.69 % | 901 | 4.9 | 309 | 405 |
| tldraw | cs-o-aot-ws-check | 1.777 | 0.0165 | 0.93 % | 1412 | 7.7 | 604 | 744 |
| tldraw | cs-o-aot-ws-check-single | 4.026 | 0.0098 | 0.24 % | 623 | 3.4 | 446 | 477 |
| tldraw | cs-o-aot-srv-check | 1.516 | 0.0139 | 0.92 % | 1655 | 9.0 | 784 | 942 |
| tldraw | cs-o-aot-srv-check-single | 3.986 | 0.0179 | 0.45 % | 629 | 3.4 | 535 | 573 |

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
| tsgo7-check-single | 2.95 [2.92, 3.02] | 3.65 [3.63, 3.67] | 2.78 [2.75, 2.79] | 2.66 [2.63, 2.68] | 3.17 [3.16, 3.18] | 3.02 [3.01, 3.04] |
| tsrs-check | 0.16 [0.16, 0.16] | 0.20 [0.19, 0.20] | 0.25 [0.25, 0.25] | 0.20 [0.20, 0.21] | 0.19 [0.19, 0.20] | 0.20 [0.20, 0.20] |
| tsrs-check-single | 1.37 [1.35, 1.38] | 1.70 [1.69, 1.71] | 1.53 [1.51, 1.54] | 1.26 [1.25, 1.27] | 1.43 [1.41, 1.43] | 1.45 [1.44, 1.45] |
| cs-o-aot-ws-check | 1.04 [1.03, 1.05] | 0.80 [0.78, 0.80] | 1.22 [1.20, 1.23] | 1.24 [1.23, 1.25] | 0.91 [0.91, 0.92] | 1.03 [1.02, 1.03] |
| cs-o-aot-ws-check-single | 2.62 [2.59, 2.65] | 2.23 [2.22, 2.24] | 2.83 [2.80, 2.85] | 2.69 [2.67, 2.71] | 2.06 [2.06, 2.07] | 2.47 [2.46, 2.48] |
| cs-o-aot-srv-check | 0.82 [0.80, 0.83] | 0.61 [0.60, 0.61] | 1.06 [1.05, 1.07] | 1.09 [1.07, 1.09] | 0.78 [0.77, 0.78] | 0.85 [0.85, 0.85] |
| cs-o-aot-srv-check-single | 2.57 [2.54, 2.61] | 2.20 [2.19, 2.22] | 2.83 [2.80, 2.84] | 2.68 [2.66, 2.70] | 2.04 [2.03, 2.05] | 2.45 [2.44, 2.46] |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: check. Binding rows (rig check): tsgo7-check-single, cs-o-aot-ws-check-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | reported | 1.41 % | 0.52 % | 0.58 % | 0.90 % | 0.32 % | 1.41 % |
| tsgo7-check-single | rig check | 1.10 % | 0.61 % | 0.52 % | 0.46 % | 0.25 % | 1.10 % |
| tsrs-check | reported | 2.26 % | 1.98 % | 1.87 % | 1.92 % | 1.76 % | 2.26 % |
| tsrs-check-single | reported | 0.69 % | 0.29 % | 0.78 % | 0.48 % | 0.69 % | 0.78 % |
| cs-o-aot-ws-check | reported | 0.52 % | 1.36 % | 1.06 % | 1.00 % | 0.93 % | 1.36 % |
| cs-o-aot-ws-check-single | rig check | 0.54 % | 0.19 % | 0.50 % | 0.33 % | 0.24 % | 0.54 % |
| cs-o-aot-srv-check | reported | 2.12 % | 0.83 % | 1.40 % | 0.61 % | 0.92 % | 2.12 % |
| cs-o-aot-srv-check-single | reported | 0.71 % | 0.79 % | 0.59 % | 0.57 % | 0.45 % | 0.79 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-check | 0.0211 | 0.92 % | 2.6 % | 10 |
| tsgo7-check-single | 0.0215 | 1.01 % | 0.9 % | 10 |
| tsrs-check | 0.0173 | 1.74 % | 8.5 % | 6 |
| tsrs-check-single | 0.0173 | 1.98 % | 1.4 % | 6 |
| cs-o-aot-ws-check | 0.0246 | 1.00 % | 2.5 % | 10 |
| cs-o-aot-ws-check-single | 0.0244 | 1.96 % | 1.1 % | 10 |
| cs-o-aot-srv-check | 0.0276 | 0.70 % | 3.2 % | 12 |
| cs-o-aot-srv-check-single | 0.0279 | 1.04 % | 1.2 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-check | 15968791432 | 1486/434/9 | 3868.6 |
| vscode | cs-o-aot-ws-check-single | 13727743176 | 1313/374/8 | 1994.7 |
| vscode | cs-o-aot-srv-check | 15891128520 | 73/41/27 | 632.7 |
| vscode | cs-o-aot-srv-check-single | 13727687392 | 71/40/23 | 589.4 |
| sentry | cs-o-aot-ws-check | 4020559168 | 404/123/8 | 1027.4 |
| sentry | cs-o-aot-ws-check-single | 3645878760 | 368/108/6 | 590.9 |
| sentry | cs-o-aot-srv-check | 4011780184 | 45/26/18 | 266.9 |
| sentry | cs-o-aot-srv-check-single | 3645827952 | 38/22/15 | 319.2 |
| bluesky | cs-o-aot-ws-check | 853207864 | 87/29/6 | 186.1 |
| bluesky | cs-o-aot-ws-check-single | 742383616 | 76/24/4 | 115.6 |
| bluesky | cs-o-aot-srv-check | 853500072 | 27/17/12 | 69.4 |
| bluesky | cs-o-aot-srv-check-single | 742493856 | 23/13/11 | 88.8 |
| playwright | cs-o-aot-ws-check | 1626445392 | 151/49/8 | 336.7 |
| playwright | cs-o-aot-ws-check-single | 1303707840 | 124/37/4 | 192.0 |
| playwright | cs-o-aot-srv-check | 1634336880 | 32/21/17 | 123.6 |
| playwright | cs-o-aot-srv-check-single | 1303790616 | 29/16/13 | 133.6 |
| tldraw | cs-o-aot-ws-check | 1721160656 | 165/54/7 | 395.0 |
| tldraw | cs-o-aot-ws-check-single | 1384022120 | 142/43/6 | 224.5 |
| tldraw | cs-o-aot-srv-check | 1714310464 | 35/22/17 | 114.6 |
| tldraw | cs-o-aot-srv-check-single | 1384023168 | 29/16/12 | 156.1 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (40 files).
