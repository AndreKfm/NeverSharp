# Parse-only benchmark, run 2026-11-03T19-39-41-764Z

**Status: protocol result.** All rig checks passed.

Label: Phase 4 emit endpoint, protocol run (prereg/v11)

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 5.8 % (median of 1 s windows: 14.1, 9.9, 4, 10.7, 3.1, 0.5, 6.8, 18.3, 4.9, 2.1) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 9435b89438, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 5.8 % (range 0.5-18.3 %) | ok |
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

- `tsgo7-emit`: typescript-go 7.0.2, program parse+bind+check+emit, parallel (default checker pool)
- `tsgo7-emit-single`: typescript-go 7.0.2, program parse+bind+check+emit, single-threaded
- `tsrs-emit`: tsrs 0.18.0 (Rust), program parse+bind+check+emit, parallel (tsrs's default checker pool)
- `tsrs-emit-single`: tsrs 0.18.0 (Rust), program parse+bind+check+emit, single-threaded
- `cs-o-aot-ws-emit`: C# Track O, NativeAOT, Workstation GC, program parse+bind+check+emit, parallel (4 checkers) (emitter endpoint)
- `cs-o-aot-ws-emit-single`: C# Track O, NativeAOT, Workstation GC, program parse+bind+check+emit, single-threaded
- `cs-o-aot-srv-emit`: C# Track O, NativeAOT, Server GC, program parse+bind+check+emit, parallel (4 checkers)
- `cs-o-aot-srv-emit-single`: C# Track O, NativeAOT, Server GC, program parse+bind+check+emit, single-threaded

## Table 1. Wall-clock, throughput, peak memory (parse-only; `*-emit` rows: the file list as one program, parse+bind+check+emit to memory)

| Repository | Configuration | Median (s) | MAD (s) | MAD/median | Files/s | MB/s | Peak working set (MiB) | Peak commit (MiB) |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| vscode | tsgo7-emit | 20.18 | 0.0638 | 0.32 % | 581 | 7.0 | 10976 | 11118 |
| vscode | tsgo7-emit-single | 65.26 | 0.2379 | 0.36 % | 180 | 2.2 | 8797 | 8909 |
| vscode | tsrs-emit | 3.249 | 0.0516 | 1.59 % | 3608 | 43.4 | 3196 | 4952 |
| vscode | tsrs-emit-single | 30.60 | 0.1451 | 0.47 % | 383 | 4.6 | 2416 | 2834 |
| vscode | cs-o-aot-ws-emit | 23.15 | 0.0823 | 0.36 % | 507 | 6.1 | 5957 | 6711 |
| vscode | cs-o-aot-ws-emit-single | 62.21 | 0.0900 | 0.14 % | 188 | 2.3 | 4740 | 5058 |
| vscode | cs-o-aot-srv-emit | 19.23 | 0.0444 | 0.23 % | 610 | 7.3 | 6056 | 6851 |
| vscode | cs-o-aot-srv-emit-single | 61.46 | 0.2196 | 0.36 % | 191 | 2.3 | 4765 | 5105 |
| sentry | tsgo7-emit | 6.314 | 0.0356 | 0.56 % | 1415 | 7.3 | 3218 | 3246 |
| sentry | tsgo7-emit-single | 23.42 | 0.1070 | 0.46 % | 381 | 2.0 | 2892 | 2913 |
| sentry | tsrs-emit | 1.249 | 0.0204 | 1.63 % | 7151 | 36.8 | 1151 | 2454 |
| sentry | tsrs-emit-single | 11.93 | 0.0223 | 0.19 % | 748 | 3.8 | 953 | 1372 |
| sentry | cs-o-aot-ws-emit | 5.126 | 0.0371 | 0.72 % | 1742 | 9.0 | 1649 | 1957 |
| sentry | cs-o-aot-ws-emit-single | 15.06 | 0.0142 | 0.09 % | 593 | 3.1 | 1475 | 1543 |
| sentry | cs-o-aot-srv-emit | 4.207 | 0.0387 | 0.92 % | 2123 | 10.9 | 1847 | 2204 |
| sentry | cs-o-aot-srv-emit-single | 14.94 | 0.0487 | 0.33 % | 598 | 3.1 | 1583 | 1698 |
| bluesky | tsgo7-emit | 1.066 | 0.0078 | 0.73 % | 1583 | 6.7 | 630 | 632 |
| bluesky | tsgo7-emit-single | 3.306 | 0.0129 | 0.39 % | 511 | 2.2 | 589 | 589 |
| bluesky | tsrs-emit | 0.2645 | 0.0024 | 0.89 % | 6381 | 27.2 | 293 | 898 |
| bluesky | tsrs-emit-single | 1.838 | 0.0060 | 0.33 % | 918 | 3.9 | 215 | 240 |
| bluesky | cs-o-aot-ws-emit | 1.230 | 0.0050 | 0.41 % | 1373 | 5.9 | 375 | 461 |
| bluesky | cs-o-aot-ws-emit-single | 3.086 | 0.0136 | 0.44 % | 547 | 2.3 | 309 | 339 |
| bluesky | cs-o-aot-srv-emit | 1.110 | 0.0172 | 1.55 % | 1521 | 6.5 | 460 | 558 |
| bluesky | cs-o-aot-srv-emit-single | 3.109 | 0.0162 | 0.52 % | 543 | 2.3 | 355 | 393 |
| playwright | tsgo7-emit | 1.890 | 0.0210 | 1.11 % | 749 | 9.1 | 1269 | 1283 |
| playwright | tsgo7-emit-single | 5.678 | 0.0168 | 0.30 % | 249 | 3.0 | 1137 | 1145 |
| playwright | tsrs-emit | 0.4097 | 0.0090 | 2.21 % | 3454 | 42.1 | 527 | 1347 |
| playwright | tsrs-emit-single | 2.871 | 0.0123 | 0.43 % | 493 | 6.0 | 376 | 424 |
| playwright | cs-o-aot-ws-emit | 2.179 | 0.0134 | 0.62 % | 649 | 7.9 | 735 | 899 |
| playwright | cs-o-aot-ws-emit-single | 5.317 | 0.0174 | 0.33 % | 266 | 3.2 | 556 | 615 |
| playwright | cs-o-aot-srv-emit | 1.975 | 0.0247 | 1.25 % | 717 | 8.7 | 876 | 1053 |
| playwright | cs-o-aot-srv-emit-single | 5.298 | 0.0159 | 0.30 % | 267 | 3.3 | 593 | 655 |
| tldraw | tsgo7-emit | 2.452 | 0.0198 | 0.81 % | 1023 | 5.6 | 1239 | 1249 |
| tldraw | tsgo7-emit-single | 8.260 | 0.0109 | 0.13 % | 304 | 1.6 | 1043 | 1047 |
| tldraw | tsrs-emit | 0.5029 | 0.0044 | 0.88 % | 4987 | 27.1 | 531 | 1338 |
| tldraw | tsrs-emit-single | 3.983 | 0.0139 | 0.35 % | 630 | 3.4 | 362 | 434 |
| tldraw | cs-o-aot-ws-emit | 2.299 | 0.0223 | 0.97 % | 1091 | 5.9 | 739 | 892 |
| tldraw | cs-o-aot-ws-emit-single | 5.746 | 0.0322 | 0.56 % | 436 | 2.4 | 542 | 571 |
| tldraw | cs-o-aot-srv-emit | 2.028 | 0.0263 | 1.30 % | 1236 | 6.7 | 899 | 1068 |
| tldraw | cs-o-aot-srv-emit-single | 5.683 | 0.0121 | 0.21 % | 441 | 2.4 | 593 | 635 |

## Table 1d. Program emit: work done (driver report of the validation run)

Output files and output bytes (UTF-8) written to memory, emit diagnostics (declaration emit) and emitSkipped, and the diagnostics of the check before the emit (semantic diagnostics of every program file plus the global diagnostics). The C# configurations emit with TypeScript 6.0.3's emitter (Track O, Gate D), typescript-go and tsrs with TypeScript 7's, which differs in places; the C# outputs of each repository program were compared once with tsc 6.0.3's (harness/bench/emit-equivalence.mjs).

| Configuration | Count | vscode | sentry | bluesky | playwright | tldraw |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| tsgo7-emit | output files | 22942 | 17841 | 3373 | 2670 | 4948 |
| tsgo7-emit | output bytes | 162220858 | 47402510 | 7297751 | 13473686 | 15946339 |
| tsgo7-emit | emit diagnostics (emitSkipped) | 21 (true) | 1 (true) | 1 (true) | 6 (true) | 19 (true) |
| tsgo7-emit | check diagnostics | 101933 | 237531 | 33337 | 30838 | 52608 |
| tsgo7-emit-single | output files | 22942 | 17841 | 3373 | 2670 | 4948 |
| tsgo7-emit-single | output bytes | 162220858 | 47402510 | 7297751 | 13473686 | 15946339 |
| tsgo7-emit-single | emit diagnostics (emitSkipped) | 21 (true) | 1 (true) | 1 (true) | 6 (true) | 19 (true) |
| tsgo7-emit-single | check diagnostics | 101933 | 237531 | 33337 | 30838 | 52604 |
| tsrs-emit | output files | 22942 | 17841 | 3373 | 2670 | 4948 |
| tsrs-emit | output bytes | 161028849 | 47405091 | 7297751 | 13473769 | 15946339 |
| tsrs-emit | emit diagnostics (emitSkipped) | 21 (true) | 1 (true) | 1 (true) | 3 (true) | 10 (true) |
| tsrs-emit | check diagnostics | 101941 | 237533 | 33337 | 30838 | 52612 |
| tsrs-emit-single | output files | 22942 | 17841 | 3373 | 2670 | 4948 |
| tsrs-emit-single | output bytes | 161028849 | 47405091 | 7297751 | 13473769 | 15946339 |
| tsrs-emit-single | emit diagnostics (emitSkipped) | 21 (true) | 1 (true) | 1 (true) | 3 (true) | 10 (true) |
| tsrs-emit-single | check diagnostics | 101941 | 237533 | 33337 | 30838 | 52612 |
| cs-o-aot-ws-emit | output files | 22942 | 17840 | 3373 | 2670 | 4948 |
| cs-o-aot-ws-emit | output bytes | 161946338 | 47395141 | 7298176 | 13474595 | 15945686 |
| cs-o-aot-ws-emit | emit diagnostics (emitSkipped) | 21 (true) | 2 (true) | 1 (true) | 3 (true) | 19 (true) |
| cs-o-aot-ws-emit | check diagnostics | 101474 | 237529 | 33337 | 30789 | 51900 |
| cs-o-aot-ws-emit-single | output files | 22942 | 17840 | 3373 | 2670 | 4948 |
| cs-o-aot-ws-emit-single | output bytes | 161946338 | 47395141 | 7298176 | 13474595 | 15945686 |
| cs-o-aot-ws-emit-single | emit diagnostics (emitSkipped) | 21 (true) | 2 (true) | 1 (true) | 3 (true) | 19 (true) |
| cs-o-aot-ws-emit-single | check diagnostics | 101493 | 237529 | 33337 | 30808 | 51900 |
| cs-o-aot-srv-emit | output files | 22942 | 17840 | 3373 | 2670 | 4948 |
| cs-o-aot-srv-emit | output bytes | 161946338 | 47395141 | 7298176 | 13474595 | 15945686 |
| cs-o-aot-srv-emit | emit diagnostics (emitSkipped) | 21 (true) | 2 (true) | 1 (true) | 3 (true) | 19 (true) |
| cs-o-aot-srv-emit | check diagnostics | 101474 | 237529 | 33337 | 30789 | 51900 |
| cs-o-aot-srv-emit-single | output files | 22942 | 17840 | 3373 | 2670 | 4948 |
| cs-o-aot-srv-emit-single | output bytes | 161946338 | 47395141 | 7298176 | 13474595 | 15945686 |
| cs-o-aot-srv-emit-single | emit diagnostics (emitSkipped) | 21 (true) | 2 (true) | 1 (true) | 3 (true) | 19 (true) |
| cs-o-aot-srv-emit-single | check diagnostics | 101493 | 237529 | 33337 | 30808 | 51900 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |

## Table 2g. Program emit: median wall-clock relative to `tsgo7-emit`, with 95 % confidence interval

Endpoint of draft pre-registration §19: each repository's file list as one program with Gate D's options, parse+bind+check (the diagnostics of §17) and emit of every file to memory, against typescript-go 7.0.2 (compiler.NewProgram, GetSemanticDiagnostics and GetGlobalDiagnostics, then Program.Emit with an in-memory WriteFile, on its default checker pool of 4 checkers). Same method as Table 2. typescript-go's checker and emitter follow TypeScript 7 and differ from 6.0.3's in places; output bytes and file counts in Table 1d.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-emit | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-emit-single | 3.23 [3.22, 3.25] | 3.71 [3.68, 3.72] | 3.10 [3.06, 3.11] | 3.00 [2.97, 3.05] | 3.37 [3.34, 3.38] | 3.27 [3.26, 3.28] |
| tsrs-emit | 0.16 [0.16, 0.16] | 0.20 [0.20, 0.20] | 0.25 [0.24, 0.25] | 0.22 [0.21, 0.22] | 0.21 [0.20, 0.21] | 0.20 [0.20, 0.20] |
| tsrs-emit-single | 1.52 [1.51, 1.52] | 1.89 [1.88, 1.90] | 1.72 [1.70, 1.73] | 1.52 [1.50, 1.54] | 1.62 [1.61, 1.63] | 1.65 [1.64, 1.65] |
| cs-o-aot-ws-emit | 1.15 [1.14, 1.15] | 0.81 [0.81, 0.82] | 1.15 [1.14, 1.16] | 1.15 [1.14, 1.17] | 0.94 [0.93, 0.95] | 1.03 [1.03, 1.03] |
| cs-o-aot-ws-emit-single | 3.08 [3.07, 3.09] | 2.39 [2.37, 2.39] | 2.89 [2.85, 2.90] | 2.81 [2.79, 2.85] | 2.34 [2.32, 2.35] | 2.69 [2.68, 2.70] |
| cs-o-aot-srv-emit | 0.95 [0.95, 0.96] | 0.67 [0.66, 0.67] | 1.04 [1.03, 1.06] | 1.04 [1.03, 1.06] | 0.83 [0.82, 0.83] | 0.89 [0.89, 0.90] |
| cs-o-aot-srv-emit-single | 3.05 [3.03, 3.06] | 2.37 [2.35, 2.37] | 2.92 [2.88, 2.93] | 2.80 [2.78, 2.84] | 2.32 [2.30, 2.32] | 2.67 [2.66, 2.68] |

## Table 2h. Program emit: median wall-clock relative to `tsrs-emit`, with 95 % confidence interval

Secondary reference: the same program emit against tsrs 0.18.0 (tsrs_compiler::new_program, the same diagnostics, then Program::emit with an in-memory write_file, on tsrs's default checker pool: 16 checkers on the reference machine, files assigned by directory locality with work stealing). Same method as Table 2.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-emit | 6.21 [6.16, 6.30] | 5.05 [4.98, 5.13] | 4.03 [4.02, 4.10] | 4.61 [4.52, 4.73] | 4.88 [4.85, 4.93] | 4.91 [4.88, 4.95] |
| tsgo7-emit-single | 20.08 [19.94, 20.38] | 18.75 [18.47, 18.98] | 12.50 [12.44, 12.62] | 13.86 [13.63, 14.16] | 16.43 [16.36, 16.57] | 16.07 [15.99, 16.18] |
| tsrs-emit | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-emit-single | 9.42 [9.35, 9.58] | 9.55 [9.42, 9.67] | 6.95 [6.92, 7.02] | 7.01 [6.89, 7.16] | 7.92 [7.88, 7.99] | 8.09 [8.05, 8.15] |
| cs-o-aot-ws-emit | 7.12 [7.07, 7.23] | 4.10 [4.04, 4.17] | 4.65 [4.63, 4.70] | 5.32 [5.23, 5.44] | 4.57 [4.54, 4.64] | 5.06 [5.03, 5.10] |
| cs-o-aot-ws-emit-single | 19.15 [19.01, 19.42] | 12.06 [11.89, 12.19] | 11.67 [11.60, 11.79] | 12.98 [12.76, 13.27] | 11.43 [11.36, 11.53] | 13.19 [13.13, 13.28] |
| cs-o-aot-srv-emit | 5.92 [5.88, 6.01] | 3.37 [3.31, 3.41] | 4.20 [4.17, 4.28] | 4.82 [4.74, 4.94] | 4.03 [3.99, 4.08] | 4.39 [4.36, 4.42] |
| cs-o-aot-srv-emit-single | 18.92 [18.77, 19.20] | 11.96 [11.78, 12.10] | 11.75 [11.70, 11.88] | 12.93 [12.72, 13.20] | 11.30 [11.25, 11.40] | 13.12 [13.06, 13.21] |

## Table 2i. Program emit: comparisons of draft pre-registration §19 and verdicts (decision rule of §8)

Geometric mean over the repositories of the ratio of medians, percentile bootstrap (10,000 resamples, seed 20261021) at the stated level. Only the endpoint row carries the verdict of §19; the other rows are secondary and never replace it. The verdict holds only for a protocol-compliant run whose variance gate passes (Table 3).

| Comparison | Role | Level | G [interval] | Verdict |
| --- | --- | ---: | ---: | --- |
| `cs-o-aot-ws-emit` vs `tsgo7-emit` | endpoint (§19) | 95 % | 1.03 [1.03, 1.03] | within 20 % of typescript-go (parity claim allowed) |
| `cs-o-aot-ws-emit-single` vs `tsgo7-emit-single` | secondary: single-threaded | 95 % | 0.82 [0.82, 0.82] | faster than typescript-go (upper bound < 1.00; within 20 %) |
| `cs-o-aot-srv-emit` vs `tsgo7-emit` | secondary: Server GC (Bonferroni) | 98.75 % | 0.89 [0.89, 0.90] | faster than typescript-go (upper bound < 1.00; within 20 %) |
| `cs-o-aot-srv-emit-single` vs `tsgo7-emit-single` | secondary: Server GC, single-threaded (Bonferroni) | 98.75 % | 0.82 [0.81, 0.82] | faster than typescript-go (upper bound < 1.00; within 20 %) |
| `cs-o-aot-ws-emit` vs `tsrs-emit` | secondary reference: tsrs | 95 % | 5.06 [5.03, 5.10] | no parity: more than 20 % slower than tsrs |
| `cs-o-aot-ws-emit-single` vs `tsrs-emit-single` | secondary reference: tsrs, single-threaded | 95 % | 1.63 [1.63, 1.63] | no parity: more than 20 % slower than tsrs |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: emit. Binding rows (rig check): tsgo7-emit-single, cs-o-aot-ws-emit-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-emit | reported | 0.32 % | 0.56 % | 0.73 % | 1.11 % | 0.81 % | 1.11 % |
| tsgo7-emit-single | rig check | 0.36 % | 0.46 % | 0.39 % | 0.30 % | 0.13 % | 0.46 % |
| tsrs-emit | reported | 1.59 % | 1.63 % | 0.89 % | 2.21 % | 0.88 % | 2.21 % |
| tsrs-emit-single | reported | 0.47 % | 0.19 % | 0.33 % | 0.43 % | 0.35 % | 0.47 % |
| cs-o-aot-ws-emit | reported | 0.36 % | 0.72 % | 0.41 % | 0.62 % | 0.97 % | 0.97 % |
| cs-o-aot-ws-emit-single | rig check | 0.14 % | 0.09 % | 0.44 % | 0.33 % | 0.56 % | 0.56 % |
| cs-o-aot-srv-emit | reported | 0.23 % | 0.92 % | 1.55 % | 1.25 % | 1.30 % | 1.55 % |
| cs-o-aot-srv-emit-single | reported | 0.36 % | 0.33 % | 0.52 % | 0.30 % | 0.21 % | 0.52 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-emit | 0.0225 | 1.53 % | 2.1 % | 10 |
| tsgo7-emit-single | 0.0216 | 1.53 % | 0.7 % | 10 |
| tsrs-emit | 0.0167 | 0.86 % | 6.3 % | 6 |
| tsrs-emit-single | 0.0173 | 1.14 % | 0.9 % | 6 |
| cs-o-aot-ws-emit | 0.0236 | 1.38 % | 1.9 % | 10 |
| cs-o-aot-ws-emit-single | 0.0237 | 1.23 % | 0.8 % | 11 |
| cs-o-aot-srv-emit | 0.0296 | 4.95 % | 2.7 % | 12 |
| cs-o-aot-srv-emit-single | 0.0277 | 0.78 % | 0.9 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-emit | 19875476136 | 1749/432/13 | 4417.9 |
| vscode | cs-o-aot-ws-emit-single | 17758771120 | 1582/352/12 | 2310.6 |
| vscode | cs-o-aot-srv-emit | 19838947456 | 80/46/31 | 697.8 |
| vscode | cs-o-aot-srv-emit-single | 17758881784 | 79/43/26 | 872.1 |
| sentry | cs-o-aot-ws-emit | 5676960336 | 537/149/10 | 1191.7 |
| sentry | cs-o-aot-ws-emit-single | 5298610432 | 493/140/9 | 667.7 |
| sentry | cs-o-aot-srv-emit | 5665429224 | 53/31/23 | 303.4 |
| sentry | cs-o-aot-srv-emit-single | 5298720128 | 54/31/25 | 423.1 |
| bluesky | cs-o-aot-ws-emit | 1096793920 | 105/35/7 | 196.4 |
| bluesky | cs-o-aot-ws-emit-single | 983862392 | 96/31/8 | 124.3 |
| bluesky | cs-o-aot-srv-emit | 1099784744 | 30/18/14 | 68.8 |
| bluesky | cs-o-aot-srv-emit-single | 983812264 | 34/22/20 | 108.5 |
| playwright | cs-o-aot-ws-emit | 2033953952 | 183/55/10 | 324.1 |
| playwright | cs-o-aot-ws-emit-single | 1729512544 | 159/45/8 | 178.8 |
| playwright | cs-o-aot-srv-emit | 2041616448 | 40/26/20 | 108.0 |
| playwright | cs-o-aot-srv-emit-single | 1729905896 | 39/24/21 | 171.6 |
| tldraw | cs-o-aot-ws-emit | 2227855496 | 206/61/9 | 402.8 |
| tldraw | cs-o-aot-ws-emit-single | 1901803568 | 182/56/9 | 229.3 |
| tldraw | cs-o-aot-srv-emit | 2228069216 | 39/25/20 | 130.5 |
| tldraw | cs-o-aot-srv-emit-single | 1901837256 | 38/23/20 | 217.8 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (40 files).
