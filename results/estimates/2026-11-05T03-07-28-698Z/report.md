# Parse-only benchmark, run 2026-11-05T03-07-28-698Z

**Status: protocol result.** All rig checks passed.

Label: quick estimate after EO1, EO2, RS1 (e8606ef), 5 runs

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 6.0 % (median of 1 s windows: 10, 11, 10.4, 2.1, 5.9, 11.1, 4.1, 2.7, 0.8, 6.2) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit e8606efb35, pins.json sha256 4a7207fd124a… |
| Settings | 5 runs after 1 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 1 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 6.0 % (range 0.8-11.1 %) | ok |
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
- `tsgo7-check12`: typescript-go 7.0.2, program parse+bind+check, parallel (12 checkers)
- `tsrs-check`: tsrs 0.18.0 (Rust), program parse+bind+check, parallel (tsrs's default checker pool)
- `tsgo7-emit`: typescript-go 7.0.2, program parse+bind+check+emit, parallel (default checker pool)
- `tsrs-emit`: tsrs 0.18.0 (Rust), program parse+bind+check+emit, parallel (tsrs's default checker pool)
- `cs-o-aot-ws-check`: C# Track O, NativeAOT, Workstation GC, program parse+bind+check, parallel (4 checkers) (checker endpoint)
- `cs-o-aot-ws-check12`: C# Track O, NativeAOT, Workstation GC, program parse+bind+check, parallel (12 checkers)
- `cs-o-aot-ws-emit`: C# Track O, NativeAOT, Workstation GC, program parse+bind+check+emit, parallel (4 checkers) (emitter endpoint)

## Table 1. Wall-clock, throughput, peak memory (parse-only; `*-check` rows: the file list as one program, parse+bind+check; `*-emit` rows: the file list as one program, parse+bind+check+emit to memory)

| Repository | Configuration | Median (s) | MAD (s) | MAD/median | Files/s | MB/s | Peak working set (MiB) | Peak commit (MiB) |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| vscode | tsgo7-check | 14.24 | 0.0571 | 0.40 % | 823 | 9.9 | 6966 | 6983 |
| vscode | tsgo7-check12 | 8.939 | 0.0519 | 0.58 % | 1311 | 15.8 | 7828 | 7849 |
| vscode | tsrs-check | 2.385 | 0.0812 | 3.41 % | 4915 | 59.2 | 2761 | 4590 |
| vscode | tsgo7-emit | 20.24 | 0.2334 | 1.15 % | 579 | 7.0 | 10368 | 10472 |
| vscode | tsrs-emit | 3.277 | 0.0634 | 1.94 % | 3578 | 43.1 | 3202 | 5033 |
| vscode | cs-o-aot-ws-check | 13.73 | 0.0579 | 0.42 % | 854 | 10.3 | 4930 | 5551 |
| vscode | cs-o-aot-ws-check12 | 10.46 | 0.0169 | 0.16 % | 1121 | 13.5 | 6170 | 6993 |
| vscode | cs-o-aot-ws-emit | 18.76 | 0.1071 | 0.57 % | 625 | 7.5 | 5972 | 6725 |
| sentry | tsgo7-check | 4.815 | 0.0300 | 0.62 % | 1855 | 9.5 | 2115 | 2133 |
| sentry | tsgo7-check12 | 2.679 | 0.0238 | 0.89 % | 3335 | 17.2 | 2151 | 2169 |
| sentry | tsrs-check | 0.9254 | 0.0147 | 1.59 % | 9652 | 49.6 | 993 | 2123 |
| sentry | tsgo7-emit | 6.228 | 0.0092 | 0.15 % | 1434 | 7.4 | 3131 | 3158 |
| sentry | tsrs-emit | 1.245 | 0.0195 | 1.57 % | 7177 | 36.9 | 1155 | 2445 |
| sentry | cs-o-aot-ws-check | 3.616 | 0.0926 | 2.56 % | 2470 | 12.7 | 1347 | 1617 |
| sentry | cs-o-aot-ws-check12 | 2.843 | 0.0061 | 0.21 % | 3142 | 16.2 | 1634 | 1940 |
| sentry | cs-o-aot-ws-emit | 5.067 | 0.0592 | 1.17 % | 1763 | 9.1 | 1661 | 1984 |
| bluesky | tsgo7-check | 0.8013 | 0.0018 | 0.22 % | 2107 | 9.0 | 427 | 430 |
| bluesky | tsgo7-check12 | 0.6012 | 0.0075 | 1.24 % | 2808 | 12.0 | 462 | 466 |
| bluesky | tsrs-check | 0.2012 | 0.0015 | 0.76 % | 8390 | 35.8 | 260 | 825 |
| bluesky | tsgo7-emit | 1.064 | 0.0093 | 0.88 % | 1587 | 6.8 | 635 | 644 |
| bluesky | tsrs-emit | 0.2731 | 0.0054 | 1.99 % | 6180 | 26.3 | 298 | 943 |
| bluesky | cs-o-aot-ws-check | 0.9435 | 0.0078 | 0.83 % | 1789 | 7.6 | 310 | 385 |
| bluesky | cs-o-aot-ws-check12 | 0.7738 | 0.0054 | 0.70 % | 2181 | 9.3 | 368 | 465 |
| bluesky | cs-o-aot-ws-emit | 1.220 | 0.0065 | 0.53 % | 1384 | 5.9 | 391 | 494 |
| playwright | tsgo7-check | 1.461 | 0.0044 | 0.30 % | 968 | 11.8 | 895 | 902 |
| playwright | tsgo7-check12 | 0.9624 | 0.0151 | 1.57 % | 1470 | 17.9 | 1079 | 1088 |
| playwright | tsrs-check | 0.3064 | 0.0059 | 1.94 % | 4618 | 56.2 | 458 | 1236 |
| playwright | tsgo7-emit | 1.863 | 0.0068 | 0.37 % | 759 | 9.2 | 1267 | 1288 |
| playwright | tsrs-emit | 0.3953 | 0.0168 | 4.25 % | 3580 | 43.6 | 533 | 1360 |
| playwright | cs-o-aot-ws-check | 1.555 | 0.0096 | 0.62 % | 910 | 11.1 | 659 | 812 |
| playwright | cs-o-aot-ws-check12 | 1.283 | 0.0016 | 0.12 % | 1103 | 13.4 | 872 | 1037 |
| playwright | cs-o-aot-ws-emit | 1.958 | 0.0325 | 1.66 % | 723 | 8.8 | 745 | 908 |
| tldraw | tsgo7-check | 1.941 | 0.0196 | 1.01 % | 1292 | 7.0 | 877 | 885 |
| tldraw | tsgo7-check12 | 1.190 | 0.0045 | 0.37 % | 2108 | 11.5 | 1021 | 1029 |
| tldraw | tsrs-check | 0.3695 | 0.0056 | 1.52 % | 6788 | 36.9 | 450 | 1208 |
| tldraw | tsgo7-emit | 2.441 | 0.0093 | 0.38 % | 1027 | 5.6 | 1234 | 1243 |
| tldraw | tsrs-emit | 0.4940 | 0.0043 | 0.86 % | 5077 | 27.6 | 533 | 1405 |
| tldraw | cs-o-aot-ws-check | 1.653 | 0.0022 | 0.13 % | 1517 | 8.2 | 636 | 800 |
| tldraw | cs-o-aot-ws-check12 | 1.412 | 0.0240 | 1.70 % | 1777 | 9.7 | 772 | 939 |
| tldraw | cs-o-aot-ws-emit | 2.179 | 0.0122 | 0.56 % | 1151 | 6.3 | 739 | 901 |

## Table 1c. Program check: diagnostics reported (driver report of the validation run)

Semantic diagnostics of every program file plus the global diagnostics. The C# configurations check with TypeScript 6.0.3's checker (Track O, Gate C), typescript-go and tsrs with TypeScript 7's, which differs in places: the counts show that checking happened; the C# diagnostics were compared once with tsc 6.0.3's (harness/bench/check-equivalence.mjs).

| Configuration | vscode | sentry | bluesky | playwright | tldraw |
| --- | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | 101933 | 237531 | 33337 | 30838 | 52608 |
| tsgo7-check12 | 101933 | 237531 | 33337 | 30838 | 52608 |
| tsrs-check | 101941 | 237533 | 33337 | 30838 | 52612 |
| cs-o-aot-ws-check | 101474 | 237529 | 33337 | 30789 | 51900 |
| cs-o-aot-ws-check12 | 101471 | 237529 | 33337 | 30798 | 51900 |

## Table 1d. Program emit: work done (driver report of the validation run)

Output files and output bytes (UTF-8) written to memory, emit diagnostics (declaration emit) and emitSkipped, and the diagnostics of the check before the emit (semantic diagnostics of every program file plus the global diagnostics). The C# configurations emit with TypeScript 6.0.3's emitter (Track O, Gate D), typescript-go and tsrs with TypeScript 7's, which differs in places; the C# outputs of each repository program were compared once with tsc 6.0.3's (harness/bench/emit-equivalence.mjs).

| Configuration | Count | vscode | sentry | bluesky | playwright | tldraw |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| tsgo7-emit | output files | 22942 | 17841 | 3373 | 2670 | 4948 |
| tsgo7-emit | output bytes | 162220858 | 47402510 | 7297751 | 13473686 | 15946339 |
| tsgo7-emit | emit diagnostics (emitSkipped) | 21 (true) | 1 (true) | 1 (true) | 6 (true) | 19 (true) |
| tsgo7-emit | check diagnostics | 101933 | 237531 | 33337 | 30838 | 52608 |
| tsrs-emit | output files | 22942 | 17841 | 3373 | 2670 | 4948 |
| tsrs-emit | output bytes | 161028849 | 47405091 | 7297751 | 13473769 | 15946339 |
| tsrs-emit | emit diagnostics (emitSkipped) | 21 (true) | 1 (true) | 1 (true) | 3 (true) | 10 (true) |
| tsrs-emit | check diagnostics | 101941 | 237533 | 33337 | 30838 | 52612 |
| cs-o-aot-ws-emit | output files | 22942 | 17840 | 3373 | 2670 | 4948 |
| cs-o-aot-ws-emit | output bytes | 161946338 | 47395141 | 7298176 | 13474595 | 15945686 |
| cs-o-aot-ws-emit | emit diagnostics (emitSkipped) | 21 (true) | 2 (true) | 1 (true) | 3 (true) | 19 (true) |
| cs-o-aot-ws-emit | check diagnostics | 101474 | 237529 | 33337 | 30789 | 51900 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |

## Table 2e. Program check: median wall-clock relative to `tsgo7-check`, with 95 % confidence interval

Endpoint of draft pre-registration §17: each repository's file list as one program, parse+bind+check, against typescript-go 7.0.2 (compiler.NewProgram, GetSemanticDiagnostics and GetGlobalDiagnostics on its default checker pool of 4 checkers). Same method as Table 2. typescript-go's checker ports TypeScript 7 and differs from 6.0.3's in places.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-check12 | 0.63 [0.62, 0.65] | 0.56 [0.55, 0.58] | 0.75 [0.72, 0.76] | 0.66 [0.64, 0.70] | 0.61 [0.59, 0.63] | 0.64 [0.63, 0.65] |
| tsrs-check | 0.17 [0.16, 0.18] | 0.19 [0.19, 0.20] | 0.25 [0.24, 0.26] | 0.21 [0.20, 0.21] | 0.19 [0.18, 0.20] | 0.20 [0.20, 0.20] |
| cs-o-aot-ws-check | 0.96 [0.95, 0.98] | 0.75 [0.73, 0.82] | 1.18 [1.16, 1.19] | 1.06 [1.04, 1.09] | 0.85 [0.83, 0.86] | 0.95 [0.94, 0.97] |
| cs-o-aot-ws-check12 | 0.73 [0.73, 0.74] | 0.59 [0.57, 0.66] | 0.97 [0.94, 0.99] | 0.88 [0.86, 0.94] | 0.73 [0.70, 0.77] | 0.77 [0.76, 0.79] |

## Table 2f. Program check: median wall-clock relative to `tsgo7-check12`, with 95 % confidence interval

Pre-registration §18 (secondary): the same program check with 12 checkers on both sides (typescript-go 7.0.2 with CompilerOptions.Checkers = 12, Track O with 12 checkers; file i to checker i % 12 on both sides). Same method as Table 2.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | 1.59 [1.53, 1.61] | 1.80 [1.72, 1.83] | 1.33 [1.32, 1.39] | 1.52 [1.44, 1.56] | 1.63 [1.59, 1.70] | 1.57 [1.54, 1.59] |
| tsgo7-check12 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-check | 0.27 [0.26, 0.28] | 0.35 [0.33, 0.35] | 0.33 [0.33, 0.35] | 0.32 [0.30, 0.33] | 0.31 [0.30, 0.33] | 0.31 [0.31, 0.32] |
| cs-o-aot-ws-check | 1.54 [1.47, 1.56] | 1.35 [1.28, 1.47] | 1.57 [1.55, 1.63] | 1.62 [1.53, 1.66] | 1.39 [1.36, 1.45] | 1.49 [1.47, 1.52] |
| cs-o-aot-ws-check12 | 1.17 [1.12, 1.18] | 1.06 [1.02, 1.19] | 1.29 [1.25, 1.35] | 1.33 [1.26, 1.42] | 1.19 [1.15, 1.26] | 1.20 [1.18, 1.23] |

## Table 2g. Program emit: median wall-clock relative to `tsgo7-emit`, with 95 % confidence interval

Endpoint of draft pre-registration §19: each repository's file list as one program with Gate D's options, parse+bind+check (the diagnostics of §17) and emit of every file to memory, against typescript-go 7.0.2 (compiler.NewProgram, GetSemanticDiagnostics and GetGlobalDiagnostics, then Program.Emit with an in-memory WriteFile, on its default checker pool of 4 checkers). Same method as Table 2. typescript-go's checker and emitter follow TypeScript 7 and differ from 6.0.3's in places; output bytes and file counts in Table 1d.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-emit | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-emit | 0.16 [0.15, 0.17] | 0.20 [0.19, 0.20] | 0.26 [0.25, 0.27] | 0.21 [0.20, 0.23] | 0.20 [0.20, 0.20] | 0.20 [0.20, 0.21] |
| cs-o-aot-ws-emit | 0.93 [0.91, 0.94] | 0.81 [0.80, 0.83] | 1.15 [1.12, 1.16] | 1.05 [1.03, 1.09] | 0.89 [0.88, 0.90] | 0.96 [0.95, 0.97] |

## Table 2h. Program emit: median wall-clock relative to `tsrs-emit`, with 95 % confidence interval

Secondary reference: the same program emit against tsrs 0.18.0 (tsrs_compiler::new_program, the same diagnostics, then Program::emit with an in-memory write_file, on tsrs's default checker pool: 16 checkers on the reference machine, files assigned by directory locality with work stealing). Same method as Table 2.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-emit | 6.18 [5.94, 6.46] | 5.00 [4.89, 5.19] | 3.90 [3.68, 4.06] | 4.71 [4.41, 4.92] | 4.94 [4.89, 5.08] | 4.89 [4.80, 4.98] |
| tsrs-emit | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| cs-o-aot-ws-emit | 5.73 [5.51, 5.99] | 4.07 [3.96, 4.21] | 4.47 [4.22, 4.65] | 4.95 [4.63, 5.17] | 4.41 [4.35, 4.53] | 4.69 [4.60, 4.77] |

## Table 2i. Program emit: comparisons of draft pre-registration §19 and verdicts (decision rule of §8)

Geometric mean over the repositories of the ratio of medians, percentile bootstrap (10,000 resamples, seed 20261021) at the stated level. Only the endpoint row carries the verdict of §19; the other rows are secondary and never replace it. The verdict holds only for a protocol-compliant run whose variance gate passes (Table 3).

| Comparison | Role | Level | G [interval] | Verdict |
| --- | --- | ---: | ---: | --- |
| `cs-o-aot-ws-emit` vs `tsgo7-emit` | endpoint (§19) | 95 % | 0.96 [0.95, 0.97] | faster than typescript-go (upper bound < 1.00; within 20 %) |
| `cs-o-aot-ws-emit` vs `tsrs-emit` | secondary reference: tsrs | 95 % | 4.69 [4.60, 4.77] | no parity: more than 20 % slower than tsrs |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: custom. Binding rows (rig check): tsc6-parse, tsgo7-parse-single, cs-aot-ws-single, cs-o-aot-ws-single (B11 as amended by prereg/v4). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-check | reported | 0.40 % | 0.62 % | 0.22 % | 0.30 % | 1.01 % | 1.01 % |
| tsgo7-check12 | reported | 0.58 % | 0.89 % | 1.24 % | 1.57 % | 0.37 % | 1.57 % |
| tsrs-check | reported | 3.41 % | 1.59 % | 0.76 % | 1.94 % | 1.52 % | 3.41 % |
| tsgo7-emit | reported | 1.15 % | 0.15 % | 0.88 % | 0.37 % | 0.38 % | 1.15 % |
| tsrs-emit | reported | 1.94 % | 1.57 % | 1.99 % | 4.25 % | 0.86 % | 4.25 % |
| cs-o-aot-ws-check | reported | 0.42 % | 2.56 % | 0.83 % | 0.62 % | 0.13 % | 2.56 % |
| cs-o-aot-ws-check12 | reported | 0.16 % | 0.21 % | 0.70 % | 0.12 % | 1.70 % | 1.70 % |
| cs-o-aot-ws-emit | reported | 0.57 % | 1.17 % | 0.53 % | 1.66 % | 0.56 % | 1.66 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-check | 0.0217 | 2.24 % | 2.7 % | 10 |
| tsgo7-check12 | 0.0219 | 0.45 % | 3.6 % | 10 |
| tsrs-check | 0.0171 | 0.97 % | 8.5 % | 6 |
| tsgo7-emit | 0.0227 | 4.86 % | 2.1 % | 10 |
| tsrs-emit | 0.0180 | 0.61 % | 6.6 % | 6 |
| cs-o-aot-ws-check | 0.0246 | 1.47 % | 2.6 % | 10 |
| cs-o-aot-ws-check12 | 0.0259 | 1.57 % | 3.3 % | 10 |
| cs-o-aot-ws-emit | 0.0250 | 0.66 % | 2.0 % | 10 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-check | 11750995936 | 984/284/9 | 3205.4 |
| vscode | cs-o-aot-ws-check12 | 13911198888 | 1142/334/10 | 4808.2 |
| vscode | cs-o-aot-ws-emit | 18101691104 | 1531/367/12 | 4234.6 |
| sentry | cs-o-aot-ws-check | 3339120808 | 321/100/8 | 934.8 |
| sentry | cs-o-aot-ws-check12 | 3728471728 | 352/116/8 | 1324.6 |
| sentry | cs-o-aot-ws-emit | 5506952120 | 514/144/10 | 1182.1 |
| bluesky | cs-o-aot-ws-check | 687789552 | 67/25/6 | 160.6 |
| bluesky | cs-o-aot-ws-check12 | 779592544 | 77/28/6 | 237.5 |
| bluesky | cs-o-aot-ws-emit | 1068816848 | 102/32/7 | 200.8 |
| playwright | cs-o-aot-ws-check | 1297648400 | 109/37/7 | 262.7 |
| playwright | cs-o-aot-ws-check12 | 1664843040 | 139/47/8 | 500.3 |
| playwright | cs-o-aot-ws-emit | 1876547248 | 162/48/9 | 310.1 |
| tldraw | cs-o-aot-ws-check | 1372736920 | 123/41/7 | 308.8 |
| tldraw | cs-o-aot-ws-check12 | 1677149264 | 148/50/8 | 529.4 |
| tldraw | cs-o-aot-ws-emit | 2104965352 | 189/56/8 | 382.4 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (40 files).
