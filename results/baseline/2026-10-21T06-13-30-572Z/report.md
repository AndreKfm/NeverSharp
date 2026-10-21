# Parse-only benchmark, run 2026-10-21T06-13-30-572Z

**Status: protocol result.** All rig checks passed.

Label: baseline

## Environment

|  |  |
| --- | --- |
| Machine | AMD Ryzen 9 3900X 12-Core Processor, 12 cores / 24 threads, 64 GiB, Micro-Star International Co., Ltd. X570-A PRO (MS-7C37), BIOS H.60 |
| OS | Microsoft Windows 11 Pro 10.0.26200.6584 |
| Rig | power scheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c, min/max processor state 100/100 %, processor performance under all-core load 100 % of 4150 MHz, idle before run 4.1 % (median of 1 s windows: 10.8, 11.1, 16, 14.3, 4, 2.8, 1.7, 2.7, 2.3, 4.2) |
| Toolchain | Node v24.21.0, typescript 6.0.3 / 7.0.2, Go go1.26.4-1-microsoft, .NET SDK 10.0.402, hyperfine 1.20.0 |
| Harness | commit 7470b7bc84, pins.json sha256 fb8d8763ad34… |
| Settings | 20 runs after 3 warmup runs (hyperfine, --shell=none); driver priority class: high; memory: 5 separate runs |

Rig checks:

| Check | Required | Value | Result |
| --- | --- | --- | --- |
| min-processor-state | 100 | 100 | ok |
| max-processor-state | 100 | 100 | ok |
| boost-off | processor performance under all-core load <= 101 % | 100 % | ok |
| idle-before-run | median CPU of ten 1 s windows < 12 % | 4.1 % (range 1.7-16.0 %) | ok |
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

- `tsc6-parse`: tsc 6.0.3 (Node 24), single-threaded
- `tsgo7-parse`: typescript-go 7.0.2, parallel (default)
- `tsgo7-parse-single`: typescript-go 7.0.2, single-threaded

## Table 1. Wall-clock, throughput, peak memory (parse-only)

| Repository | Configuration | Median (s) | MAD (s) | MAD/median | Files/s | MB/s | Peak working set (MiB) | Peak commit (MiB) |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| vscode | tsc6-parse | 12.03 | 0.0940 | 0.78 % | 975 | 11.7 | 3192 | 3224 |
| vscode | tsgo7-parse | 1.046 | 0.0158 | 1.51 % | 11203 | 134.9 | 2175 | 2218 |
| vscode | tsgo7-parse-single | 5.005 | 0.0176 | 0.35 % | 2342 | 28.2 | 2111 | 2178 |
| sentry | tsc6-parse | 4.813 | 0.1214 | 2.52 % | 1856 | 9.5 | 1215 | 1208 |
| sentry | tsgo7-parse | 0.3998 | 0.0092 | 2.30 % | 22341 | 114.9 | 794 | 818 |
| sentry | tsgo7-parse-single | 2.286 | 0.0185 | 0.81 % | 3907 | 20.1 | 763 | 776 |
| bluesky | tsc6-parse | 1.234 | 0.0143 | 1.16 % | 1368 | 5.8 | 368 | 341 |
| bluesky | tsgo7-parse | 0.0986 | 0.0019 | 1.95 % | 17127 | 73.0 | 156 | 169 |
| bluesky | tsgo7-parse-single | 0.4200 | 0.0035 | 0.84 % | 4019 | 17.1 | 142 | 152 |
| playwright | tsc6-parse | 1.799 | 0.0177 | 0.99 % | 786 | 9.6 | 555 | 533 |
| playwright | tsgo7-parse | 0.1406 | 0.0024 | 1.70 % | 10065 | 122.6 | 269 | 284 |
| playwright | tsgo7-parse-single | 0.5855 | 0.0033 | 0.56 % | 2417 | 29.4 | 255 | 265 |
| tldraw | tsc6-parse | 1.861 | 0.0153 | 0.82 % | 1348 | 7.3 | 542 | 520 |
| tldraw | tsgo7-parse | 0.1522 | 0.0043 | 2.82 % | 16478 | 89.5 | 271 | 287 |
| tldraw | tsgo7-parse-single | 0.7055 | 0.0082 | 1.17 % | 3555 | 19.3 | 256 | 269 |

## Table 2. Median wall-clock relative to `tsgo7-parse`, with 95 % confidence interval

Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.

| Configuration | vscode | sentry | bluesky | playwright | tldraw | Geometric mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | 11.49 [11.40, 11.76] | 12.04 [11.76, 12.55] | 12.52 [12.19, 12.77] | 12.80 [12.51, 12.94] | 12.23 [11.85, 12.47] | 12.21 [12.10, 12.35] |
| tsgo7-parse | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| tsgo7-parse-single | 4.78 [4.75, 4.89] | 5.72 [5.61, 5.84] | 4.26 [4.16, 4.33] | 4.16 [4.08, 4.21] | 4.64 [4.49, 4.73] | 4.68 [4.64, 4.72] |

## Table 3. Variance gate (MAD/median < 3 %)

Binding rows: single-threaded configurations (rig check). Parallel rows: reported spread, not gated; their run-to-run spread is a property of the parallel program (docs/benchmark-harness.md, B11).

| Configuration | Role | vscode | sentry | bluesky | playwright | tldraw | Worst |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| tsc6-parse | rig check | 0.78 % | 2.52 % | 1.16 % | 0.99 % | 0.82 % | 2.52 % |
| tsgo7-parse | reported | 1.51 % | 2.30 % | 1.95 % | 1.70 % | 2.82 % | 2.82 % |
| tsgo7-parse-single | rig check | 0.35 % | 0.81 % | 0.84 % | 0.56 % | 1.17 % | 1.17 % |

Variance gate (rig check): **PASS**.

## Table 4. Runtime metrics (C# configurations only)

No C# configuration in this run.

## Notes

- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.
- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.
- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).
- Raw timings: the hyperfine exports next to run.json (15 files).
