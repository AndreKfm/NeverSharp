# NeverSharp

A C# port of the TypeScript 6.0.3 compiler (scanner, parser, binder, type checker, emitter), measured against typescript-go 7.0.2 under a pre-registered protocol. tsrs, a Rust port, is a secondary reference.

**Status: closed (2026-11-07).** This is a research project, not a usable compiler. There is no CLI for end users, and no further work is planned. All measurements are from one Windows machine (AMD Ryzen 9 3900X, 12 cores); a Linux replication was not run.

The full write-up is [docs/report-v1.md](docs/report-v1.md) (report v1.4).

## The question

The pre-registered question asks whether a faithful C# port of tsc lands within 20 % of typescript-go's wall-clock time on Microsoft's benchmark corpus (vscode, sentry, bluesky, playwright, tldraw).

There are two tracks:
- **Track F, unoptimized (faithful port):** follows typescript-go's porting rules, with no optimization that typescript-go does not also have. Parsing only.
- **Track O, optimized:** a performance-oriented port of the same compiler, using value types, arenas, SIMD and NativeAOT. Its output must stay byte-identical to tsc's.

## Results

G is the geometric mean over the five repositories of median(C#) / median(reference), with a 95 % bootstrap interval. Values below 1 mean the C# side is faster.

**Pre-registered endpoints, against typescript-go:**

| Scope | G [95 % CI] | Verdict |
|---|---|---|
| Parsing, unoptimized (Track F) | 6.31 [6.20, 6.41] | no parity (the answer to the question) |
| Parsing, optimized (Track O) | 0.60 [0.59, 0.61] | faster |
| Parse+bind, optimized | 1.207 [1.191, 1.219] | inconclusive |
| Parse+bind+check, optimized, 4 checkers | 1.680 [1.675, 1.689] | no parity |
| Parse+bind+check+emit, optimized, 4 checkers | 1.030 [1.026, 1.035] | within 20 % |

**Later additional runs, after optimization on the same corpus** (fitted to the corpus they measure; disclosed in the report):
- Against typescript-go: parse+bind+check 0.756, parse+bind+check+emit 0.782.
- Against tsrs, with tsrs's kind of checker pool: the check takes 2.15× tsrs's time with Workstation GC and 1.83× with Server GC (vscode 1.97×, sentry 1.45×).
- Single-threaded, the check takes 1.47× tsrs's time.

**Output equivalence:** the gates compare Track O's output byte for byte with tsc 6.0.3 and pass on all 50,177 units of the equivalence corpus:
- ASTs;
- binder dumps;
- checker dumps (Gate C);
- emitted `.js` and `.d.ts` (Gate D; one normalization of symbol ids in late-bound names, and 4 units where tsc itself fails fail in both).

The emitted programs of all five repositories are identical too.

## How it was made

- The code was written by AI agents (Claude Opus 5.5 and Sonnet 5.5), orchestrated by Claude Opus 5.5, under the author's direction and decisions.
- The workflow is fixed in [§5.2 of the pre-registration](docs/preregistration.md); every deviation is logged in [docs/phase1-progress.md](docs/phase1-progress.md) and listed in the report.
- Each pre-registration version is a git tag (`prereg/v1` … `prereg/v12`) with an OpenTimestamps proof in [docs/timestamps](docs/timestamps).

## Repository layout

| Path | Contents |
|---|---|
| `src/` | Track F (`NeverSharp.Faithful`), Track O (`NeverSharp.Optimized`) and tables generated from upstream data (`Generated`) |
| `harness/` | equivalence gates and dumpers (`astdump`, `port`), benchmark harness (`bench`), profiling |
| `analyzers/` | the NoAlloc Roslyn analyzer used by Track O |
| `results/` | every measurement: run.json, report.md, raw hyperfine exports, VERDICT.md / NOTE.md |
| `docs/` | pre-registration, report, phase plans and summaries, progress log, timestamps |
| `scripts/` | reference fetch, toolchain install, header check, OpenTimestamps, report page |

## Reproducing

- **Pins:** the upstream references are pinned in `pins.json`. Fetch them into `external/` with `node scripts/fetch-references.mjs`.
- **Toolchain:** the .NET SDK version is pinned in `global.json`. Install the pinned Node, Go, Rust, .NET and hyperfine with `node scripts/install-tools.mjs`.
- **NU1903:** builds currently need `WarningsNotAsErrors=NU1903` in the environment. The advisory GHSA-p3jp-9v83-76w9 on `Microsoft.DotNet.ILCompiler` 10.0.13 otherwise fails `dotnet publish`.
- **Gates, benchmarks and the report page:** see [§15 of the report](docs/report-v1.md) and [docs/benchmark-harness.md](docs/benchmark-harness.md).

## Upstream references

| | Repository | Pinned |
|---|---|---|
| Port source, oracle 1 | [microsoft/TypeScript](https://github.com/microsoft/TypeScript) | `v6.0.3` @ `050880ce59e30b356b686bd3144efe24f875ebc8` |
| Baseline, oracle 2 | [microsoft/typescript-go](https://github.com/microsoft/typescript-go) | `typescript/v7.0.2` @ `2bd066d87f5bafd315be9f40889d0a60b9e58e0b` |
| Secondary reference, oracle 3 | [oxc-project/tsrs](https://github.com/oxc-project/tsrs) | `v0.18.0` @ `5f92808bc92bb97b06212153ab73d28d551c0b9b` |

Details, provenance and known differences: [docs/references.md](docs/references.md).

## License

Apache License 2.0, see [LICENSE](LICENSE) and [NOTICE](NOTICE). The repository contains code ported from TypeScript and typescript-go (Copyright (c) Microsoft Corporation) and data structures adapted from tsrs (Copyright 2026 Max Schwenk and the tsrs contributors), all Apache License 2.0. Their license files are in [licenses/](licenses).

"TypeScript" is a trademark of Microsoft Corporation. This project is not affiliated with, sponsored by, or endorsed by Microsoft Corporation, the typescript-go team or the tsrs project.
