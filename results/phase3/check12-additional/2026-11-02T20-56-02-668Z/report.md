# Parse-only benchmark, run 2026-11-02T20-56-02-668Z

**Status: protocol result.** All rig checks passed.

Label: check12 additional run 1 (after CO6, 5d9e8a5)

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 99 % of 4150 MHz, idle before run 4.0 % (median of 1 s windows: 13.1, 7.3, 4.4, 3.9, 9.3, 4.1, 2.2, 1.3, 4, 3.8) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 5d9e8a5fa6, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 99 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 4.0 % (range 1.3-13.1 %) | ok |
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
| vscode | tsgo7-check-single | 42.81 | 0.1137 | 0.27 % | 274 | 3.3 | 5803 | 5825 |
| vscode | tsgo7-check12 | 8.759 | 0.0703 | 0.80 % | 1339 | 16.1 | 7881 | 7901 |
| vscode | tsrs-check | 2.195 | 0.0383 | 1.74 % | 5340 | 64.3 | 2760 | 4498 |
| vscode | cs-o-aot-ws-check-single | 36.86 | 0.0638 | 0.17 % | 318 | 3.8 | 3957 | 4303 |
| vscode | cs-o-aot-ws-check12 | 10.85 | 0.0697 | 0.64 % | 1081 | 13.0 | 6128 | 6948 |
| vscode | cs-o-aot-srv-check12 | 6.478 | 0.0616 | 0.95 % | 1810 | 21.8 | 6494 | 7140 |
| sentry | tsgo7-check-single | 17.44 | 0.0324 | 0.19 % | 512 | 2.6 | 1918 | 1931 |
| sentry | tsgo7-check12 | 2.733 | 0.0509 | 1.86 % | 3269 | 16.8 | 2270 | 2292 |
| sentry | tsrs-check | 0.9457 | 0.0184 | 1.95 % | 9445 | 48.6 | 996 | 2195 |
| sentry | cs-o-aot-ws-check-single | 10.61 | 0.0581 | 0.55 % | 842 | 4.3 | 1180 | 1245 |
| sentry | cs-o-aot-ws-check12 | 2.825 | 0.0334 | 1.18 % | 3162 | 16.3 | 1616 | 1933 |
| sentry | cs-o-aot-srv-check12 | 1.950 | 0.0340 | 1.74 % | 4581 | 23.6 | 1880 | 2225 |
| bluesky | tsgo7-check-single | 2.265 | 0.0205 | 0.90 % | 745 | 3.2 | 394 | 398 |
| bluesky | tsgo7-check12 | 0.5836 | 0.0110 | 1.89 % | 2892 | 12.3 | 465 | 469 |
| bluesky | tsrs-check | 0.2047 | 0.0044 | 2.16 % | 8245 | 35.1 | 259 | 832 |
| bluesky | cs-o-aot-ws-check-single | 2.254 | 0.0120 | 0.53 % | 749 | 3.2 | 262 | 291 |
| bluesky | cs-o-aot-ws-check12 | 0.7794 | 0.0087 | 1.12 % | 2166 | 9.2 | 370 | 469 |
| bluesky | cs-o-aot-srv-check12 | 0.6581 | 0.0102 | 1.55 % | 2565 | 10.9 | 501 | 619 |
| playwright | tsgo7-check-single | 3.967 | 0.0257 | 0.65 % | 357 | 4.3 | 792 | 791 |
| playwright | tsgo7-check12 | 0.9816 | 0.0218 | 2.22 % | 1442 | 17.6 | 1064 | 1072 |
| playwright | tsrs-check | 0.3056 | 0.0080 | 2.61 % | 4631 | 56.4 | 462 | 1237 |
| playwright | cs-o-aot-ws-check-single | 3.878 | 0.0078 | 0.20 % | 365 | 4.4 | 481 | 533 |
| playwright | cs-o-aot-ws-check12 | 1.375 | 0.0190 | 1.38 % | 1029 | 12.5 | 835 | 1003 |
| playwright | cs-o-aot-srv-check12 | 1.020 | 0.0165 | 1.62 % | 1388 | 16.9 | 1015 | 1217 |
| tldraw | tsgo7-check-single | 6.169 | 0.0103 | 0.17 % | 407 | 2.2 | 746 | 745 |
| tldraw | tsgo7-check12 | 1.171 | 0.0143 | 1.22 % | 2142 | 11.6 | 964 | 969 |
| tldraw | tsrs-check | 0.3783 | 0.0053 | 1.41 % | 6629 | 36.0 | 446 | 1203 |
| tldraw | cs-o-aot-ws-check-single | 3.925 | 0.0118 | 0.30 % | 639 | 3.5 | 443 | 474 |
| tldraw | cs-o-aot-ws-check12 | 1.412 | 0.0180 | 1.28 % | 1776 | 9.7 | 770 | 932 |
| tldraw | cs-o-aot-srv-check12 | 1.052 | 0.0192 | 1.82 % | 2384 | 13.0 | 984 | 1141 |

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
| tsgo7-check-single | 4.89 [4.82, 4.91] | 6.38 [6.30, 6.52] | 3.88 [3.77, 3.91] | 4.04 [3.99, 4.10] | 5.27 [5.21, 5.33] | 4.81 [4.77, 4.84] |
| tsgo7-check12 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-check | 0.25 [0.25, 0.26] | 0.35 [0.34, 0.36] | 0.35 [0.34, 0.36] | 0.31 [0.31, 0.32] | 0.32 [0.32, 0.33] | 0.31 [0.31, 0.32] |
| cs-o-aot-ws-check-single | 4.21 [4.15, 4.23] | 3.88 [3.82, 3.97] | 3.86 [3.76, 3.91] | 3.95 [3.91, 4.02] | 3.35 [3.31, 3.39] | 3.84 [3.81, 3.86] |
| cs-o-aot-ws-check12 | 1.24 [1.22, 1.25] | 1.03 [1.01, 1.06] | 1.34 [1.30, 1.36] | 1.40 [1.38, 1.43] | 1.21 [1.18, 1.22] | 1.24 [1.23, 1.24] |
| cs-o-aot-srv-check12 | 0.74 [0.73, 0.75] | 0.71 [0.70, 0.73] | 1.13 [1.10, 1.14] | 1.04 [1.02, 1.06] | 0.90 [0.88, 0.91] | 0.89 [0.88, 0.90] |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: check12. Binding rows (rig check): tsgo7-check-single, cs-o-aot-ws-check-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check-single | rig check | 0.27 % | 0.19 % | 0.90 % | 0.65 % | 0.17 % | 0.90 % |
| tsgo7-check12 | reported | 0.80 % | 1.86 % | 1.89 % | 2.22 % | 1.22 % | 2.22 % |
| tsrs-check | reported | 1.74 % | 1.95 % | 2.16 % | 2.61 % | 1.41 % | 2.61 % |
| cs-o-aot-ws-check-single | rig check | 0.17 % | 0.55 % | 0.53 % | 0.20 % | 0.30 % | 0.55 % |
| cs-o-aot-ws-check12 | reported | 0.64 % | 1.18 % | 1.12 % | 1.38 % | 1.28 % | 1.38 % |
| cs-o-aot-srv-check12 | reported | 0.95 % | 1.74 % | 1.55 % | 1.62 % | 1.82 % | 1.82 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-check-single | 0.0213 | 0.78 % | 0.9 % | 10 |
| tsgo7-check12 | 0.0222 | 1.44 % | 3.8 % | 10 |
| tsrs-check | 0.0177 | 1.62 % | 8.7 % | 6 |
| cs-o-aot-ws-check-single | 0.0233 | 0.67 % | 1.0 % | 10 |
| cs-o-aot-ws-check12 | 0.0235 | 1.06 % | 3.0 % | 10 |
| cs-o-aot-srv-check12 | 0.0283 | 1.41 % | 4.3 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-check-single | 11361649032 | 1031/287/8 | 1646.1 |
| vscode | cs-o-aot-ws-check12 | 15752518504 | 1366/397/10 | 5052.7 |
| vscode | cs-o-aot-srv-check12 | 15691862344 | 62/36/22 | 780.4 |
| sentry | cs-o-aot-ws-check-single | 3079811800 | 300/90/6 | 522.2 |
| sentry | cs-o-aot-ws-check12 | 3842342856 | 369/118/8 | 1319.1 |
| sentry | cs-o-aot-srv-check12 | 3828398240 | 38/22/15 | 270.3 |
| bluesky | cs-o-aot-ws-check-single | 602925360 | 60/21/4 | 95.0 |
| bluesky | cs-o-aot-ws-check12 | 806760368 | 80/29/6 | 246.9 |
| bluesky | cs-o-aot-srv-check12 | 805657232 | 25/15/11 | 84.3 |
| playwright | cs-o-aot-ws-check-single | 1104843168 | 101/32/4 | 148.2 |
| playwright | cs-o-aot-ws-check12 | 1799501400 | 156/49/8 | 509.8 |
| playwright | cs-o-aot-srv-check12 | 1803396856 | 30/20/15 | 142.1 |
| tldraw | cs-o-aot-ws-check-single | 1149054840 | 114/36/6 | 181.1 |
| tldraw | cs-o-aot-ws-check12 | 1779768344 | 162/53/8 | 549.7 |
| tldraw | cs-o-aot-srv-check12 | 1787645120 | 32/19/15 | 139.1 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (30 files).
