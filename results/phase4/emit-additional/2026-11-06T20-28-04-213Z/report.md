# Parse-only benchmark, run 2026-11-06T20-28-04-213Z

**Status: protocol result.** All rig checks passed.

Label: emit additional run 1 (after MM1, prereg/v12)

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 4.9 % (median of 1 s windows: 11.3, 10.3, 6.2, 2.9, 2.1, 11, 7.3, 3.6, 1.4, 0.5) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, rustc 1.94.0-nightly (fa5eda19b 2025-12-12), hyperfine 1.20.0 |
| Harness | commit 6920a1c43f, pins.json sha256 4a7207fd124a… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 4.9 % (range 0.5-11.3 %) | ok |
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
| vscode | tsgo7-emit | 20.30 | 0.0952 | 0.47 % | 577 | 7.0 | 10581 | 10705 |
| vscode | tsgo7-emit-single | 65.58 | 0.2550 | 0.39 % | 179 | 2.2 | 9182 | 9315 |
| vscode | tsrs-emit | 3.284 | 0.0392 | 1.20 % | 3570 | 43.0 | 3203 | 5035 |
| vscode | tsrs-emit-single | 30.76 | 0.1113 | 0.36 % | 381 | 4.6 | 2416 | 2834 |
| vscode | cs-o-aot-ws-emit | 14.96 | 0.0367 | 0.25 % | 784 | 9.4 | 5153 | 5706 |
| vscode | cs-o-aot-ws-emit-single | 44.63 | 0.0610 | 0.14 % | 263 | 3.2 | 4268 | 4520 |
| vscode | cs-o-aot-srv-emit | 13.83 | 0.0864 | 0.62 % | 848 | 10.2 | 5247 | 5828 |
| vscode | cs-o-aot-srv-emit-single | 44.87 | 0.0545 | 0.12 % | 261 | 3.1 | 4295 | 4554 |
| sentry | tsgo7-emit | 6.258 | 0.0310 | 0.50 % | 1427 | 7.3 | 3114 | 3144 |
| sentry | tsgo7-emit-single | 23.47 | 0.0603 | 0.26 % | 381 | 2.0 | 2759 | 2786 |
| sentry | tsrs-emit | 1.247 | 0.0245 | 1.96 % | 7164 | 36.8 | 1148 | 2450 |
| sentry | tsrs-emit-single | 12.00 | 0.0365 | 0.30 % | 744 | 3.8 | 953 | 1372 |
| sentry | cs-o-aot-ws-emit | 4.267 | 0.0203 | 0.48 % | 2093 | 10.8 | 1561 | 1838 |
| sentry | cs-o-aot-ws-emit-single | 13.33 | 0.0190 | 0.14 % | 670 | 3.4 | 1385 | 1448 |
| sentry | cs-o-aot-srv-emit | 3.765 | 0.0315 | 0.84 % | 2372 | 12.2 | 1678 | 1999 |
| sentry | cs-o-aot-srv-emit-single | 13.46 | 0.0255 | 0.19 % | 663 | 3.4 | 1397 | 1463 |
| bluesky | tsgo7-emit | 1.087 | 0.0165 | 1.52 % | 1553 | 6.6 | 625 | 631 |
| bluesky | tsgo7-emit-single | 3.322 | 0.0121 | 0.36 % | 508 | 2.2 | 619 | 628 |
| bluesky | tsrs-emit | 0.2710 | 0.0040 | 1.46 % | 6230 | 26.5 | 292 | 897 |
| bluesky | tsrs-emit-single | 1.852 | 0.0080 | 0.43 % | 911 | 3.9 | 215 | 240 |
| bluesky | cs-o-aot-ws-emit | 0.9852 | 0.0141 | 1.43 % | 1713 | 7.3 | 391 | 484 |
| bluesky | cs-o-aot-ws-emit-single | 2.666 | 0.0119 | 0.45 % | 633 | 2.7 | 320 | 346 |
| bluesky | cs-o-aot-srv-emit | 0.9577 | 0.0134 | 1.40 % | 1763 | 7.5 | 428 | 523 |
| bluesky | cs-o-aot-srv-emit-single | 2.744 | 0.0137 | 0.50 % | 615 | 2.6 | 330 | 373 |
| playwright | tsgo7-emit | 1.912 | 0.0187 | 0.98 % | 740 | 9.0 | 1264 | 1285 |
| playwright | tsgo7-emit-single | 5.777 | 0.0116 | 0.20 % | 245 | 3.0 | 1099 | 1114 |
| playwright | tsrs-emit | 0.4043 | 0.0072 | 1.78 % | 3500 | 42.6 | 528 | 1352 |
| playwright | tsrs-emit-single | 2.887 | 0.0101 | 0.35 % | 490 | 6.0 | 376 | 424 |
| playwright | cs-o-aot-ws-emit | 1.664 | 0.0177 | 1.07 % | 850 | 10.4 | 717 | 877 |
| playwright | cs-o-aot-ws-emit-single | 4.420 | 0.0115 | 0.26 % | 320 | 3.9 | 562 | 608 |
| playwright | cs-o-aot-srv-emit | 1.609 | 0.0150 | 0.93 % | 879 | 10.7 | 781 | 943 |
| playwright | cs-o-aot-srv-emit-single | 4.516 | 0.0199 | 0.44 % | 313 | 3.8 | 576 | 644 |
| tldraw | tsgo7-emit | 2.475 | 0.0165 | 0.67 % | 1013 | 5.5 | 1270 | 1288 |
| tldraw | tsgo7-emit-single | 8.317 | 0.0190 | 0.23 % | 302 | 1.6 | 1056 | 1068 |
| tldraw | tsrs-emit | 0.5037 | 0.0063 | 1.24 % | 4979 | 27.1 | 531 | 1335 |
| tldraw | tsrs-emit-single | 3.987 | 0.0121 | 0.30 % | 629 | 3.4 | 362 | 434 |
| tldraw | cs-o-aot-ws-emit | 1.821 | 0.0086 | 0.47 % | 1377 | 7.5 | 711 | 852 |
| tldraw | cs-o-aot-ws-emit-single | 5.024 | 0.0094 | 0.19 % | 499 | 2.7 | 526 | 557 |
| tldraw | cs-o-aot-srv-emit | 1.715 | 0.0164 | 0.96 % | 1463 | 7.9 | 763 | 902 |
| tldraw | cs-o-aot-srv-emit-single | 5.100 | 0.0204 | 0.40 % | 492 | 2.7 | 550 | 590 |

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
| tsgo7-emit-single | 3.23 [3.21, 3.24] | 3.75 [3.73, 3.77] | 3.06 [3.01, 3.08] | 3.02 [2.99, 3.04] | 3.36 [3.35, 3.39] | 3.27 [3.26, 3.28] |
| tsrs-emit | 0.16 [0.16, 0.16] | 0.20 [0.20, 0.20] | 0.25 [0.24, 0.25] | 0.21 [0.21, 0.22] | 0.20 [0.20, 0.21] | 0.20 [0.20, 0.21] |
| tsrs-emit-single | 1.51 [1.50, 1.52] | 1.92 [1.91, 1.93] | 1.70 [1.68, 1.72] | 1.51 [1.49, 1.52] | 1.61 [1.60, 1.62] | 1.65 [1.64, 1.65] |
| cs-o-aot-ws-emit | 0.74 [0.73, 0.74] | 0.68 [0.68, 0.69] | 0.91 [0.89, 0.92] | 0.87 [0.86, 0.88] | 0.74 [0.73, 0.74] | 0.78 [0.78, 0.79] |
| cs-o-aot-ws-emit-single | 2.20 [2.19, 2.21] | 2.13 [2.12, 2.14] | 2.45 [2.42, 2.47] | 2.31 [2.28, 2.33] | 2.03 [2.02, 2.04] | 2.22 [2.21, 2.23] |
| cs-o-aot-srv-emit | 0.68 [0.67, 0.69] | 0.60 [0.60, 0.61] | 0.88 [0.86, 0.89] | 0.84 [0.83, 0.85] | 0.69 [0.69, 0.70] | 0.73 [0.73, 0.74] |
| cs-o-aot-srv-emit-single | 2.21 [2.20, 2.22] | 2.15 [2.14, 2.16] | 2.53 [2.48, 2.54] | 2.36 [2.33, 2.38] | 2.06 [2.05, 2.08] | 2.26 [2.25, 2.26] |

## Table 2h. Program emit: median wall-clock relative to `tsrs-emit`, with 95 % confidence interval

Secondary reference: the same program emit against tsrs 0.18.0 (tsrs_compiler::new_program, the same diagnostics, then Program::emit with an in-memory write_file, on tsrs's default checker pool: 16 checkers on the reference machine, files assigned by directory locality with work stealing). Same method as Table 2.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-emit | 6.18 [6.10, 6.25] | 5.02 [4.92, 5.08] | 4.01 [3.96, 4.09] | 4.73 [4.54, 4.79] | 4.91 [4.86, 4.94] | 4.92 [4.87, 4.95] |
| tsgo7-emit-single | 19.97 [19.70, 20.16] | 18.82 [18.47, 19.06] | 12.26 [12.14, 12.45] | 14.29 [13.74, 14.35] | 16.51 [16.34, 16.60] | 16.12 [15.96, 16.18] |
| tsrs-emit | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsrs-emit-single | 9.37 [9.25, 9.45] | 9.63 [9.45, 9.75] | 6.84 [6.77, 6.95] | 7.14 [6.86, 7.18] | 7.91 [7.83, 7.96] | 8.10 [8.02, 8.13] |
| cs-o-aot-ws-emit | 4.56 [4.50, 4.60] | 3.42 [3.36, 3.47] | 3.64 [3.59, 3.72] | 4.12 [3.96, 4.17] | 3.62 [3.58, 3.65] | 3.85 [3.81, 3.87] |
| cs-o-aot-ws-emit-single | 13.59 [13.44, 13.72] | 10.69 [10.49, 10.82] | 9.84 [9.74, 10.00] | 10.93 [10.51, 10.98] | 9.97 [9.88, 10.03] | 10.93 [10.82, 10.97] |
| cs-o-aot-srv-emit | 4.21 [4.15, 4.26] | 3.02 [2.96, 3.06] | 3.53 [3.48, 3.60] | 3.98 [3.82, 4.02] | 3.40 [3.37, 3.45] | 3.61 [3.57, 3.63] |
| cs-o-aot-srv-emit-single | 13.66 [13.50, 13.79] | 10.80 [10.58, 10.93] | 10.13 [10.00, 10.29] | 11.17 [10.73, 11.23] | 10.12 [10.02, 10.19] | 11.11 [10.99, 11.15] |

## Table 2i. Program emit: comparisons of draft pre-registration §19 and verdicts (decision rule of §8)

Geometric mean over the repositories of the ratio of medians, percentile bootstrap (10,000 resamples, seed 20261021) at the stated level. Only the endpoint row carries the verdict of §19; the other rows are secondary and never replace it. The verdict holds only for a protocol-compliant run whose variance gate passes (Table 3).

| Comparison | Role | Level | G [interval] | Verdict |
| --- | --- | ---: | ---: | --- |
| `cs-o-aot-ws-emit` vs `tsgo7-emit` | endpoint (§19) | 95 % | 0.78 [0.78, 0.79] | faster than typescript-go (upper bound < 1.00; within 20 %) |
| `cs-o-aot-ws-emit-single` vs `tsgo7-emit-single` | secondary: single-threaded | 95 % | 0.68 [0.68, 0.68] | faster than typescript-go (upper bound < 1.00; within 20 %) |
| `cs-o-aot-srv-emit` vs `tsgo7-emit` | secondary: Server GC (Bonferroni) | 98.75 % | 0.73 [0.73, 0.74] | faster than typescript-go (upper bound < 1.00; within 20 %) |
| `cs-o-aot-srv-emit-single` vs `tsgo7-emit-single` | secondary: Server GC, single-threaded (Bonferroni) | 98.75 % | 0.69 [0.69, 0.69] | faster than typescript-go (upper bound < 1.00; within 20 %) |
| `cs-o-aot-ws-emit` vs `tsrs-emit` | secondary reference: tsrs | 95 % | 3.85 [3.81, 3.87] | no parity: more than 20 % slower than tsrs |
| `cs-o-aot-ws-emit-single` vs `tsrs-emit-single` | secondary reference: tsrs, single-threaded | 95 % | 1.35 [1.35, 1.35] | no parity: more than 20 % slower than tsrs |

## Table 3. Variance gate (MAD/median < 3 %)

Profile: emit. Binding rows (rig check): tsgo7-emit-single, cs-o-aot-ws-emit-single (prereg/v8). Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsgo7-emit | reported | 0.47 % | 0.50 % | 1.52 % | 0.98 % | 0.67 % | 1.52 % |
| tsgo7-emit-single | rig check | 0.39 % | 0.26 % | 0.36 % | 0.20 % | 0.23 % | 0.39 % |
| tsrs-emit | reported | 1.20 % | 1.96 % | 1.46 % | 1.78 % | 1.24 % | 1.96 % |
| tsrs-emit-single | reported | 0.36 % | 0.30 % | 0.43 % | 0.35 % | 0.30 % | 0.43 % |
| cs-o-aot-ws-emit | reported | 0.25 % | 0.48 % | 1.43 % | 1.07 % | 0.47 % | 1.43 % |
| cs-o-aot-ws-emit-single | rig check | 0.14 % | 0.14 % | 0.45 % | 0.26 % | 0.19 % | 0.45 % |
| cs-o-aot-srv-emit | reported | 0.62 % | 0.84 % | 1.40 % | 0.93 % | 0.96 % | 1.40 % |
| cs-o-aot-srv-emit-single | reported | 0.12 % | 0.19 % | 0.50 % | 0.44 % | 0.40 % | 0.50 % |

Variance gate (rig check): **PASS**.

## Table 4. Fixed cost (empty file list)

Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.

| Configuration | Median (s) | MAD/median | Share of the smallest repository's median | Peak working set (MiB) |
| --- | ---: | ---: | ---: | ---: |
| tsgo7-emit | 0.0222 | 1.26 % | 2.0 % | 10 |
| tsgo7-emit-single | 0.0211 | 1.17 % | 0.6 % | 10 |
| tsrs-emit | 0.0170 | 1.40 % | 6.3 % | 6 |
| tsrs-emit-single | 0.0166 | 0.66 % | 0.9 % | 6 |
| cs-o-aot-ws-emit | 0.0246 | 1.22 % | 2.5 % | 10 |
| cs-o-aot-ws-emit-single | 0.0244 | 1.24 % | 0.9 % | 11 |
| cs-o-aot-srv-emit | 0.0287 | 2.70 % | 3.0 % | 12 |
| cs-o-aot-srv-emit-single | 0.0277 | 0.39 % | 1.0 % | 12 |

## Table 5. Runtime metrics (C# configurations only)

| Repository | Configuration | Bytes allocated | GC count (gen0/1/2) | GC pause total (ms) |
| --- | --- | ---: | ---: | ---: |
| vscode | cs-o-aot-ws-emit | 12117429176 | 237/97/13 | 1503.9 |
| vscode | cs-o-aot-ws-emit-single | 10780093608 | 216/83/15 | 885.8 |
| vscode | cs-o-aot-srv-emit | 12092600600 | 75/43/36 | 494.3 |
| vscode | cs-o-aot-srv-emit-single | 10350282544 | 80/44/32 | 856.6 |
| sentry | cs-o-aot-ws-emit | 3606263496 | 83/46/11 | 615.0 |
| sentry | cs-o-aot-ws-emit-single | 3362499096 | 75/37/10 | 463.2 |
| sentry | cs-o-aot-srv-emit | 3601828912 | 47/28/25 | 221.0 |
| sentry | cs-o-aot-srv-emit-single | 3362289360 | 46/29/26 | 452.8 |
| bluesky | cs-o-aot-ws-emit | 697473016 | 19/13/6 | 117.1 |
| bluesky | cs-o-aot-ws-emit-single | 622680480 | 19/12/8 | 81.0 |
| bluesky | cs-o-aot-srv-emit | 707176272 | 22/16/13 | 62.2 |
| bluesky | cs-o-aot-srv-emit-single | 622704840 | 30/21/20 | 68.3 |
| playwright | cs-o-aot-ws-emit | 1353977424 | 32/18/8 | 155.8 |
| playwright | cs-o-aot-ws-emit-single | 1130408216 | 30/17/10 | 100.9 |
| playwright | cs-o-aot-srv-emit | 1361358576 | 33/23/19 | 66.6 |
| playwright | cs-o-aot-srv-emit-single | 1130441136 | 38/27/25 | 118.3 |
| tldraw | cs-o-aot-ws-emit | 1532142696 | 36/21/7 | 216.0 |
| tldraw | cs-o-aot-ws-emit-single | 1287452376 | 35/17/9 | 135.5 |
| tldraw | cs-o-aot-srv-emit | 1538097736 | 33/21/18 | 83.2 |
| tldraw | cs-o-aot-srv-emit-single | 1287453632 | 37/25/22 | 115.8 |

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (40 files).
