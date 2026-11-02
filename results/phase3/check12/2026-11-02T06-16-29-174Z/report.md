# Parse-only benchmark, run 2026-11-02T06-16-29-174Z

**Status: protocol result.** All rig checks passed.

Label: check12 protocol run (prereg/v10, 12 checkers)

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 3.9 % (median of 1 s windows: 4, 5, 1.4, 2.2, 3.8, 3.8, 9.9, 7.5, 7.8, 3.5) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 61b36e9eef, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 3.9 % (range 1.4-9.9 %) | ok |
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

- `tsgo7-check-single`: typescript-go 7.0.2, program parse+bind+check, single-threaded
- `tsgo7-check12`: typescript-go 7.0.2, program parse+bind+check, parallel (12 checkers)
- `tsrs-check`: tsrs 0.18.0 (Rust), program parse+bind+check, parallel (tsrs's default checker pool)
- `cs-o-aot-ws-check-single`: C# Track O, NativeAOT, Workstation GC, program parse+bind+check, single-threaded
- `cs-o-aot-ws-check12`: C# Track O, NativeAOT, Workstation GC, program parse+bind+check, parallel (12 checkers)
- `cs-o-aot-srv-check12`: C# Track O, NativeAOT, Server GC, program parse+bind+check, parallel (12 checkers)

## Table 1. Wall-clock, throughput, peak memory (parse-only; `*-check` rows: the file list as one program, parse+bind+check)

| Repository | Configuration | Median (s) | MAD (s) | MAD/median | Files/s | MB/s | Peak working set (MiB) | Peak commit (MiB) |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| vscode | tsgo7-check-single | 42.70 | 0.0918 | 0.22 % | 275 | 3.3 | 5872 | 5887 |
| vscode | tsgo7-check12 | 8.851 | 0.1169 | 1.32 % | 1325 | 16.0 | 7921 | 7944 |
| vscode | tsrs-check | 2.188 | 0.0253 | 1.16 % | 5358 | 64.5 | 2759 | 4434 |
| vscode | cs-o-aot-ws-check-single | 37.28 | 0.0637 | 0.17 % | 314 | 3.8 | 3924 | 4252 |
| vscode | cs-o-aot-ws-check12 | 11.45 | 0.0427 | 0.37 % | 1024 | 12.3 | 6076 | 6909 |
| vscode | cs-o-aot-srv-check12 | 6.761 | 0.0566 | 0.84 % | 1734 | 20.9 | 6443 | 7128 |
| sentry | tsgo7-check-single | 17.48 | 0.0403 | 0.23 % | 511 | 2.6 | 1949 | 1964 |
| sentry | tsgo7-check12 | 2.680 | 0.0456 | 1.70 % | 3333 | 17.1 | 2250 | 2270 |
| sentry | tsrs-check | 0.9337 | 0.0187 | 2.00 % | 9566 | 49.2 | 997 | 2233 |
| sentry | cs-o-aot-ws-check-single | 10.58 | 0.0192 | 0.18 % | 844 | 4.3 | 1185 | 1250 |
| sentry | cs-o-aot-ws-check12 | 2.936 | 0.0359 | 1.22 % | 3043 | 15.6 | 1625 | 1938 |
| sentry | cs-o-aot-srv-check12 | 1.865 | 0.0288 | 1.54 % | 4790 | 24.6 | 1925 | 2249 |
| bluesky | tsgo7-check-single | 2.255 | 0.0107 | 0.47 % | 749 | 3.2 | 404 | 401 |
| bluesky | tsgo7-check12 | 0.5894 | 0.0116 | 1.97 % | 2864 | 12.2 | 464 | 470 |
| bluesky | tsrs-check | 0.2078 | 0.0045 | 2.15 % | 8123 | 34.6 | 260 | 839 |
| bluesky | cs-o-aot-ws-check-single | 2.273 | 0.0091 | 0.40 % | 743 | 3.2 | 263 | 296 |
| bluesky | cs-o-aot-ws-check12 | 0.8118 | 0.0119 | 1.47 % | 2079 | 8.9 | 368 | 464 |
| bluesky | cs-o-aot-srv-check12 | 0.6504 | 0.0132 | 2.04 % | 2595 | 11.1 | 493 | 612 |
| playwright | tsgo7-check-single | 3.960 | 0.0195 | 0.49 % | 357 | 4.4 | 796 | 794 |
| playwright | tsgo7-check12 | 0.9771 | 0.0181 | 1.85 % | 1448 | 17.6 | 1080 | 1081 |
| playwright | tsrs-check | 0.3109 | 0.0079 | 2.54 % | 4551 | 55.4 | 463 | 1255 |
| playwright | cs-o-aot-ws-check-single | 3.961 | 0.0217 | 0.55 % | 357 | 4.4 | 486 | 541 |
| playwright | cs-o-aot-ws-check12 | 1.484 | 0.0297 | 2.00 % | 953 | 11.6 | 833 | 1007 |
| playwright | cs-o-aot-srv-check12 | 1.039 | 0.0158 | 1.52 % | 1361 | 16.6 | 1033 | 1229 |
| tldraw | tsgo7-check-single | 6.211 | 0.0072 | 0.12 % | 404 | 2.2 | 740 | 739 |
| tldraw | tsgo7-check12 | 1.192 | 0.0285 | 2.39 % | 2105 | 11.4 | 992 | 993 |
| tldraw | tsrs-check | 0.3801 | 0.0085 | 2.24 % | 6599 | 35.9 | 449 | 1214 |
| tldraw | cs-o-aot-ws-check-single | 3.985 | 0.0108 | 0.27 % | 629 | 3.4 | 445 | 476 |
| tldraw | cs-o-aot-ws-check12 | 1.523 | 0.0184 | 1.21 % | 1646 | 8.9 | 782 | 952 |
| tldraw | cs-o-aot-srv-check12 | 1.065 | 0.0198 | 1.86 % | 2355 | 12.8 | 982 | 1171 |

## Table 1c. Program check: diagnostics reported (driver report of the validation run)

Semantic diagnostics of every program file plus the global diagnostics. The C# configurations check with TypeScript 6.0.3's checker (Track O, Gate C), typescript-go and tsrs with TypeScript 7's, which differs in places: the counts show that checking happened; the C# diagnostics were compared once with tsc 6.0.3's (harness/bench/check-equivalence.mjs).

| Configuration | vscode | sentry | bluesky | playwright | tldraw |
| --- | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check-single | 101933 | 237531 | 33337 | 30838 | 52604 |
| tsgo7-check12 | 101933 | 237531 | 33337 | 30838 | 52608 |
| tsrs-check | 101941 | 237533 | 33337 | 30838 | 52612 |
| cs-o-aot-ws-check-single | 101493 | 237529 | 33337 | 30808 | 51900 |
| cs-o-aot-ws-check12 | 101471 | 237529 | 33337 | 30798 | 51900 |
| cs-o-aot-srv-check12 | 101471 | 237529 | 33337 | 30798 | 51900 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |

## Table 2f. Program check: median wall-clock relative to `tsgo7-check12`, with 95 % confidence interval

Pre-registration §18 (secondary): the same program check with 12 checkers on both sides (typescript-go 7.0.2 with CompilerOptions.Checkers = 12, Track O with 12 checkers; file i to checker i % 12 on both sides). Same method as Table 2.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check-single | 4.82 [4.76, 4.87] | 6.52 [6.43, 6.60] | 3.83 [3.74, 3.87] | 4.05 [3.97, 4.11] | 5.21 [5.14, 5.31] | 4.80 [4.76, 4.83] |
| tsgo7-check12 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-check | 0.25 [0.24, 0.25] | 0.35 [0.34, 0.36] | 0.35 [0.34, 0.36] | 0.32 [0.31, 0.33] | 0.32 [0.31, 0.33] | 0.31 [0.31, 0.32] |
| cs-o-aot-ws-check-single | 4.21 [4.16, 4.25] | 3.95 [3.89, 4.00] | 3.86 [3.77, 3.90] | 4.05 [3.97, 4.11] | 3.34 [3.30, 3.41] | 3.87 [3.84, 3.90] |
| cs-o-aot-ws-check12 | 1.29 [1.28, 1.31] | 1.10 [1.08, 1.11] | 1.38 [1.34, 1.40] | 1.52 [1.48, 1.55] | 1.28 [1.26, 1.30] | 1.31 [1.29, 1.32] |
| cs-o-aot-srv-check12 | 0.76 [0.75, 0.77] | 0.70 [0.68, 0.71] | 1.10 [1.07, 1.12] | 1.06 [1.04, 1.09] | 0.89 [0.88, 0.92] | 0.89 [0.88, 0.90] |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: check12. Binding rows (rig check): tsgo7-check-single, cs-o-aot-ws-check-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check-single | rig check | 0.22 % | 0.23 % | 0.47 % | 0.49 % | 0.12 % | 0.49 % |
| tsgo7-check12 | reported | 1.32 % | 1.70 % | 1.97 % | 1.85 % | 2.39 % | 2.39 % |
| tsrs-check | reported | 1.16 % | 2.00 % | 2.15 % | 2.54 % | 2.24 % | 2.54 % |
| cs-o-aot-ws-check-single | rig check | 0.17 % | 0.18 % | 0.40 % | 0.55 % | 0.27 % | 0.55 % |
| cs-o-aot-ws-check12 | reported | 0.37 % | 1.22 % | 1.47 % | 2.00 % | 1.21 % | 2.00 % |
| cs-o-aot-srv-check12 | reported | 0.84 % | 1.54 % | 2.04 % | 1.52 % | 1.86 % | 2.04 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-check-single | 0.0217 | 2.50 % | 1.0 % | 10 |
| tsgo7-check12 | 0.0214 | 2.72 % | 3.6 % | 10 |
| tsrs-check | 0.0171 | 1.96 % | 8.2 % | 6 |
| cs-o-aot-ws-check-single | 0.0248 | 1.66 % | 1.1 % | 10 |
| cs-o-aot-ws-check12 | 0.0243 | 2.56 % | 3.0 % | 10 |
| cs-o-aot-srv-check12 | 0.0275 | 0.70 % | 4.2 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-check-single | 11530139760 | 1050/295/8 | 1891.0 |
| vscode | cs-o-aot-ws-check12 | 15965619544 | 1389/399/10 | 5803.5 |
| vscode | cs-o-aot-srv-check12 | 16023372808 | 63/36/23 | 798.5 |
| sentry | cs-o-aot-ws-check-single | 3092766784 | 302/95/6 | 562.1 |
| sentry | cs-o-aot-ws-check12 | 3856478872 | 370/119/8 | 1423.0 |
| sentry | cs-o-aot-srv-check12 | 3849954120 | 39/24/15 | 288.8 |
| bluesky | cs-o-aot-ws-check-single | 605736880 | 59/21/4 | 109.2 |
| bluesky | cs-o-aot-ws-check12 | 806385792 | 80/29/6 | 271.6 |
| bluesky | cs-o-aot-srv-check12 | 810107712 | 25/14/11 | 75.5 |
| playwright | cs-o-aot-ws-check-single | 1115630208 | 102/32/4 | 184.6 |
| playwright | cs-o-aot-ws-check12 | 1810197872 | 157/53/8 | 615.1 |
| playwright | cs-o-aot-srv-check12 | 1817494584 | 35/21/17 | 137.2 |
| tldraw | cs-o-aot-ws-check-single | 1157523904 | 114/34/5 | 212.5 |
| tldraw | cs-o-aot-ws-check12 | 1788325184 | 162/54/7 | 664.0 |
| tldraw | cs-o-aot-srv-check12 | 1786118216 | 30/20/15 | 154.1 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (30 files).
