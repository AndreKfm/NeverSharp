# Parse-only benchmark, run 2026-11-02T22-22-25-874Z

**Status: protocol result.** All rig checks passed.

Label: checker additional run 3 (after CO6, 5d9e8a5)

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 4.3 % (median of 1 s windows: 32.9, 22.6, 3.1, 2.5, 2.8, 3.1, 8.2, 7.4, 5.5, 1) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 5d9e8a5fa6, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 4.3 % (range 1.0-32.9 %) | ok |
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
| vscode | tsgo7-check | 14.29 | 0.0767 | 0.54 % | 821 | 9.9 | 6999 | 7016 |
| vscode | tsgo7-check-single | 42.68 | 0.1423 | 0.33 % | 275 | 3.3 | 5814 | 5835 |
| vscode | tsrs-check | 2.204 | 0.0329 | 1.49 % | 5321 | 64.1 | 2761 | 4520 |
| vscode | tsrs-check-single | 19.22 | 0.0650 | 0.34 % | 610 | 7.3 | 2164 | 2628 |
| vscode | cs-o-aot-ws-check | 14.04 | 0.0737 | 0.52 % | 835 | 10.1 | 4839 | 5473 |
| vscode | cs-o-aot-ws-check-single | 36.81 | 0.0419 | 0.11 % | 319 | 3.8 | 3964 | 4308 |
| vscode | cs-o-aot-srv-check | 11.21 | 0.0339 | 0.30 % | 1045 | 12.6 | 5258 | 5978 |
| vscode | cs-o-aot-srv-check-single | 36.05 | 0.0907 | 0.25 % | 325 | 3.9 | 4282 | 4585 |
| sentry | tsgo7-check | 4.800 | 0.0327 | 0.68 % | 1861 | 9.6 | 2120 | 2139 |
| sentry | tsgo7-check-single | 17.41 | 0.0287 | 0.16 % | 513 | 2.6 | 1895 | 1916 |
| sentry | tsrs-check | 0.9384 | 0.0234 | 2.49 % | 9518 | 49.0 | 997 | 2185 |
| sentry | tsrs-check-single | 8.146 | 0.0151 | 0.18 % | 1096 | 5.6 | 886 | 1333 |
| sentry | cs-o-aot-ws-check | 3.577 | 0.0295 | 0.82 % | 2497 | 12.8 | 1329 | 1602 |
| sentry | cs-o-aot-ws-check-single | 10.52 | 0.0204 | 0.19 % | 849 | 4.4 | 1180 | 1245 |
| sentry | cs-o-aot-srv-check | 2.877 | 0.0116 | 0.40 % | 3105 | 16.0 | 1611 | 1955 |
| sentry | cs-o-aot-srv-check-single | 10.41 | 0.0580 | 0.56 % | 858 | 4.4 | 1367 | 1485 |
| bluesky | tsgo7-check | 0.8135 | 0.0051 | 0.63 % | 2075 | 8.8 | 429 | 434 |
| bluesky | tsgo7-check-single | 2.255 | 0.0149 | 0.66 % | 748 | 3.2 | 409 | 413 |
| bluesky | tsrs-check | 0.2034 | 0.0025 | 1.21 % | 8298 | 35.4 | 259 | 815 |
| bluesky | tsrs-check-single | 1.226 | 0.0045 | 0.36 % | 1377 | 5.9 | 199 | 239 |
| bluesky | cs-o-aot-ws-check | 0.9497 | 0.0123 | 1.30 % | 1777 | 7.6 | 311 | 394 |
| bluesky | cs-o-aot-ws-check-single | 2.268 | 0.0078 | 0.35 % | 744 | 3.2 | 265 | 292 |
| bluesky | cs-o-aot-srv-check | 0.8467 | 0.0156 | 1.84 % | 1994 | 8.5 | 387 | 488 |
| bluesky | cs-o-aot-srv-check-single | 2.257 | 0.0068 | 0.30 % | 748 | 3.2 | 297 | 334 |
| playwright | tsgo7-check | 1.485 | 0.0178 | 1.20 % | 953 | 11.6 | 892 | 893 |
| playwright | tsgo7-check-single | 3.948 | 0.0141 | 0.36 % | 358 | 4.4 | 733 | 739 |
| playwright | tsrs-check | 0.3047 | 0.0073 | 2.41 % | 4643 | 56.5 | 459 | 1228 |
| playwright | tsrs-check-single | 1.860 | 0.0074 | 0.40 % | 761 | 9.3 | 348 | 406 |
| playwright | cs-o-aot-ws-check | 1.735 | 0.0164 | 0.95 % | 816 | 9.9 | 650 | 797 |
| playwright | cs-o-aot-ws-check-single | 3.893 | 0.0076 | 0.19 % | 363 | 4.4 | 481 | 533 |
| playwright | cs-o-aot-srv-check | 1.558 | 0.0131 | 0.84 % | 908 | 11.1 | 807 | 991 |
| playwright | cs-o-aot-srv-check-single | 3.911 | 0.0128 | 0.33 % | 362 | 4.4 | 546 | 603 |
| tldraw | tsgo7-check | 1.955 | 0.0137 | 0.70 % | 1283 | 7.0 | 865 | 865 |
| tldraw | tsgo7-check-single | 6.201 | 0.0136 | 0.22 % | 404 | 2.2 | 736 | 742 |
| tldraw | tsrs-check | 0.3820 | 0.0056 | 1.48 % | 6566 | 35.7 | 449 | 1193 |
| tldraw | tsrs-check-single | 2.765 | 0.0088 | 0.32 % | 907 | 4.9 | 330 | 405 |
| tldraw | cs-o-aot-ws-check | 1.682 | 0.0190 | 1.13 % | 1491 | 8.1 | 602 | 747 |
| tldraw | cs-o-aot-ws-check-single | 3.929 | 0.0180 | 0.46 % | 638 | 3.5 | 443 | 474 |
| tldraw | cs-o-aot-srv-check | 1.475 | 0.0186 | 1.26 % | 1701 | 9.2 | 789 | 951 |
| tldraw | cs-o-aot-srv-check-single | 3.911 | 0.0066 | 0.17 % | 641 | 3.5 | 527 | 566 |

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
| tsgo7-check-single | 2.99 [2.97, 3.00] | 3.63 [3.61, 3.65] | 2.77 [2.74, 2.79] | 2.66 [2.62, 2.68] | 3.17 [3.15, 3.19] | 3.03 [3.01, 3.03] |
| tsrs-check | 0.15 [0.15, 0.16] | 0.20 [0.19, 0.20] | 0.25 [0.25, 0.25] | 0.21 [0.20, 0.21] | 0.20 [0.19, 0.20] | 0.20 [0.20, 0.20] |
| tsrs-check-single | 1.35 [1.34, 1.35] | 1.70 [1.69, 1.71] | 1.51 [1.50, 1.51] | 1.25 [1.24, 1.26] | 1.41 [1.40, 1.42] | 1.44 [1.43, 1.44] |
| cs-o-aot-ws-check | 0.98 [0.98, 0.99] | 0.75 [0.74, 0.75] | 1.17 [1.16, 1.20] | 1.17 [1.15, 1.18] | 0.86 [0.85, 0.87] | 0.97 [0.96, 0.98] |
| cs-o-aot-ws-check-single | 2.58 [2.57, 2.59] | 2.19 [2.18, 2.21] | 2.79 [2.77, 2.80] | 2.62 [2.59, 2.64] | 2.01 [1.99, 2.03] | 2.42 [2.41, 2.43] |
| cs-o-aot-srv-check | 0.78 [0.78, 0.79] | 0.60 [0.59, 0.60] | 1.04 [1.03, 1.06] | 1.05 [1.03, 1.06] | 0.75 [0.74, 0.76] | 0.83 [0.82, 0.83] |
| cs-o-aot-srv-check-single | 2.52 [2.51, 2.54] | 2.17 [2.15, 2.19] | 2.77 [2.76, 2.79] | 2.63 [2.60, 2.65] | 2.00 [1.99, 2.01] | 2.40 [2.39, 2.41] |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: check. Binding rows (rig check): tsgo7-check-single, cs-o-aot-ws-check-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | reported | 0.54 % | 0.68 % | 0.63 % | 1.20 % | 0.70 % | 1.20 % |
| tsgo7-check-single | rig check | 0.33 % | 0.16 % | 0.66 % | 0.36 % | 0.22 % | 0.66 % |
| tsrs-check | reported | 1.49 % | 2.49 % | 1.21 % | 2.41 % | 1.48 % | 2.49 % |
| tsrs-check-single | reported | 0.34 % | 0.18 % | 0.36 % | 0.40 % | 0.32 % | 0.40 % |
| cs-o-aot-ws-check | reported | 0.52 % | 0.82 % | 1.30 % | 0.95 % | 1.13 % | 1.30 % |
| cs-o-aot-ws-check-single | rig check | 0.11 % | 0.19 % | 0.35 % | 0.19 % | 0.46 % | 0.46 % |
| cs-o-aot-srv-check | reported | 0.30 % | 0.40 % | 1.84 % | 0.84 % | 1.26 % | 1.84 % |
| cs-o-aot-srv-check-single | reported | 0.25 % | 0.56 % | 0.30 % | 0.33 % | 0.17 % | 0.56 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-check | 0.0212 | 0.64 % | 2.6 % | 10 |
| tsgo7-check-single | 0.0221 | 1.24 % | 1.0 % | 10 |
| tsrs-check | 0.0173 | 1.49 % | 8.5 % | 6 |
| tsrs-check-single | 0.0169 | 0.85 % | 1.4 % | 6 |
| cs-o-aot-ws-check | 0.0232 | 0.66 % | 2.4 % | 10 |
| cs-o-aot-ws-check-single | 0.0234 | 0.55 % | 1.0 % | 10 |
| cs-o-aot-srv-check | 0.0271 | 1.71 % | 3.2 % | 12 |
| cs-o-aot-srv-check-single | 0.0289 | 2.68 % | 1.3 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-check | 13390369248 | 1188/343/10 | 3314.7 |
| vscode | cs-o-aot-ws-check-single | 11361815680 | 1032/298/8 | 1644.2 |
| vscode | cs-o-aot-srv-check | 13366904728 | 71/40/29 | 601.5 |
| vscode | cs-o-aot-srv-check-single | 11361241504 | 63/35/23 | 682.0 |
| sentry | cs-o-aot-ws-check | 3456138328 | 338/107/8 | 905.4 |
| sentry | cs-o-aot-ws-check-single | 3079682360 | 301/91/6 | 519.1 |
| sentry | cs-o-aot-srv-check | 3458885832 | 39/22/16 | 222.3 |
| sentry | cs-o-aot-srv-check-single | 3079669056 | 37/21/15 | 247.0 |
| bluesky | cs-o-aot-ws-check | 710553320 | 70/25/6 | 157.7 |
| bluesky | cs-o-aot-ws-check-single | 602830624 | 59/18/4 | 100.4 |
| bluesky | cs-o-aot-srv-check | 720627984 | 24/15/12 | 50.5 |
| bluesky | cs-o-aot-srv-check-single | 602915704 | 23/14/11 | 93.1 |
| playwright | cs-o-aot-ws-check | 1406979784 | 125/42/8 | 258.2 |
| playwright | cs-o-aot-ws-check-single | 1105389688 | 102/32/4 | 146.6 |
| playwright | cs-o-aot-srv-check | 1414512528 | 29/20/17 | 123.0 |
| playwright | cs-o-aot-srv-check-single | 1104899856 | 28/16/13 | 131.2 |
| tldraw | cs-o-aot-ws-check | 1468566264 | 136/43/7 | 317.0 |
| tldraw | cs-o-aot-ws-check-single | 1148467096 | 114/38/6 | 179.2 |
| tldraw | cs-o-aot-srv-check | 1475236104 | 32/20/16 | 112.9 |
| tldraw | cs-o-aot-srv-check-single | 1149117416 | 25/14/12 | 134.5 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (40 files).
