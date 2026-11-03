# Pre-registration: a faithful C# port of the TypeScript compiler, measured against typescript-go

Status: **binding since tag `prereg/v1`, 2026-10-21**, timestamped with OpenTimestamps (§11.1; proof in `docs/timestamps/`). Changes only by dated amendment tags (§12). Phase 1 budget: 2026-10-21 to 2027-02-24 (18 weeks, §9). Items marked **[DECISION, CONFIRMED]** were proposed by the author's assistant and confirmed by the author on 2026-10-21, items marked **[OPEN]** need the author's input.

Author: Andre Kaufmann. Repository: NeverSharp. License: Apache-2.0.

"TypeScript" is a trademark of Microsoft Corporation. This project is not affiliated with or endorsed by Microsoft.

## 1. Question

> Does a faithful C# port of tsc land within 20 % of typescript-go's wall-clock on Microsoft's benchmark corpus?

This is a question, not a claim. The protocol below fixes, before any C# code exists, what will be measured, how, what counts as an answer, and when the project stops.

**Two tracks** (decided by the author 2026-10-21, before any C# code exists):

- **Track F (faithful)** answers the question above: a faithful port under the port rules of section 5, the way typescript-go is a faithful port. **Only Track F carries the pre-registered verdict.**
- **Track O (optimized)** answers a second, separately labeled question: *how fast can a performance-optimized C# port of the same compiler be, compared with typescript-go?* It may use optimizations without a typescript-go counterpart (section 5.4). Its results are always reported as "optimized track" and never replace or blur the Track F verdict.

Both tracks port from tsc 6.0.3 and pass the same Gate 1: byte-identical to tsc 6.0.3.

**Scope of the answer per phase.** Phase 1 answers the question for **parsing only** (scanner + parser). A Phase 1 result says nothing about the full compiler. Later phases extend the measured scope (section 10).

## 2. Ground truth

Two independent references, pinned by commit ([references.md](references.md), [pins.json](../pins.json)):

| Role | Repository | Tag | Commit |
|---|---|---|---|
| Port source and oracle 1 | microsoft/TypeScript | `v6.0.3` | `050880ce59e30b356b686bd3144efe24f875ebc8` |
| Performance baseline and oracle 2 | microsoft/typescript-go | `typescript/v7.0.2` | `2bd066d87f5bafd315be9f40889d0a60b9e58e0b` |

The Go side is Microsoft's code. Nothing written for this project is used as a comparison target. The only code of ours on the Go side is the parse-only driver loop (about 30 lines around typescript-go's own `core.WorkGroup`, `osvfs` and `parser.ParseSourceFile`), built with Microsoft's Go toolset and the release flags ([benchmark-harness.md](benchmark-harness.md), B5).

Known oracle difference, pre-registered: typescript-go 7.0.2 was ported from TypeScript `4d4f005c` (branch `tsgo-port`), not from `v6.0.3`. In the Phase 1 scope the two differ in one line of `scanJsDocToken` ([references.md](references.md), section 2).

## 3. Corpora

**Benchmark corpus.** Exactly the five codebases of Microsoft's "Announcing TypeScript 7.0" (2026-07-08): VS Code, Sentry, Bluesky (social-app), Playwright, tldraw. No additions, no removals. Microsoft published no commits; the commits were selected by a mechanical rule fixed before looking at them: the last first-parent commit of the default branch before 2026-07-08T00:00:00Z ([references.md](references.md), section 3).

| Repository | Commit | Parse set: files | MiB |
|---|---|---:|---:|
| vscode | `f507e492b81074fff62b0806d47760671c3990b5` | 11,724 | 134.6 |
| sentry | `b4e1ddf51f55e4aa973b2d0c028485f13de779a6` | 8,932 | 43.8 |
| bluesky | `ea3d6827c75c2c2063bdb8d3240c62d8f041c7fa` | 1,688 | 6.9 |
| playwright | `a061d963659048a43169bd87c84960d89f496cdb` | 1,415 | 16.4 |
| tldraw | `9986114e3493118095f60146becd0219e100745e` | 2,508 | 13.0 |

Parse set: every git-tracked `.ts .tsx .mts .cts` file at the pinned commit (B1). A content hash over all paths and contents is recorded in every result.

**Tuning corpus** (A5, A1). Used for every choice that depends on speed, in both tracks: the 23,910 `tests/cases/**` units of the equivalence corpus plus TypeScript v6.0.3's own `src/**/*.ts` (601 files, 16.8 MiB of real-world code that is not in the benchmark corpus). The benchmark corpus is used for nothing but the final measurements.

**Equivalence corpus.** 50,177 parse units: every file under `tests/cases/**` of TypeScript `v6.0.3`, split into units as TypeScript's own test harness does (23,910 units), plus every file of the five parse sets (26,267 units) ([ast-dump-format.md](ast-dump-format.md), section 2).

## 4. Equivalence gates

### 4.1 Canonical AST dump

Defined in [ast-dump-format.md](ast-dump-format.md): one line per node in pre-order (kind name, UTF-16 `pos`, `end`, sorted NodeFlags names), children as each implementation's own child visitor yields them, then parse diagnostics (code, start, length).

### 4.2 Gate 1 (per module, per variant, both tracks)

The C# dump of either track must be **byte-identical to the tsc 6.0.3 dump on all 50,177 units, raw, without normalization**, in the `!diag` section and, because JSDoc is in Phase 1 scope (section 5.3), in the JSDoc section added to the dump format before Phase 1 starts. One mismatch fails the module. There is no tolerance and no exclusion list for the C# side.

### 4.3 Oracle agreement

tsc 6.0.3 and typescript-go 7.0.2 are compared after the nine normalization rules N1–N9 ([ast-dump-format.md](ast-dump-format.md), section 5). Result, deterministic for the pinned inputs: 50,092 units identical, 85 units differ, each listed with its category in [harness/astdump/oracle-divergences.tsv](../harness/astdump/oracle-divergences.tsv). The C# dump is additionally reported against the typescript-go dump under the same normalization, as a cross-check.

## 5. Port rules

Rules 1–5 and sections 5.1–5.3 apply to **Track F**. Track O has its own rules in section 5.4. The port mirrors typescript-go's approach (a port, not a rewrite).

1. **Functions and data.** No class hierarchies beyond what the TypeScript source has. Node representation as in the source: kind, pos, end, parent, flags.
2. **Strings and positions are UTF-16**, as in TypeScript. (typescript-go uses UTF-8 because Go strings are bytes; C# strings are UTF-16, so the source's representation is kept.)
3. **No optimization without a counterpart in typescript-go**, cited by file and function in a comment and in the commit message. Examples of counterparts that may be mirrored: parser and scanner pooling (`getParser`/`putParser`, `internal/parser/parser.go`), slice arenas for nodes (`nodeSliceArena`), parallel parsing of files on a work group (`core.WorkGroup`, `internal/core/workgroup.go`; used in `internal/compiler/program.go`).
4. **Idiomatic C# substitutions** (`ReadOnlySpan<char>` instead of substring slicing, structs for small immutable records) only where typescript-go made the analogous Go substitution, cited by file.
5. **Every ported file** carries the header of [CONTRIBUTING.md](../CONTRIBUTING.md) naming the TypeScript source file and commit and the typescript-go file used as a structural reference.

### 5.1 Gate 2: analyzer ruleset

**[DECISION, CONFIRMED 2026-10-21]** Gate 2 is a Release build of the port with zero diagnostics under:

- `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<AnalysisLevel>10.0-all</AnalysisLevel>`, `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`;
- `Microsoft.CodeAnalysis.BannedApiAnalyzers` with a banned list derived from the port rules: `System.Linq` in `src/` (no counterpart in Go, allocates), `System.Reflection` and `dynamic`, `System.Text.RegularExpressions` (the TypeScript scanner and parser use no regular expressions), parallelism other than the work-group counterpart of rule 3;
- a header check for rule 5 (`scripts/check-headers.mjs`).

Committed files ([eng/](../eng/)): `Gate2.Common.props`, `Gate2.Faithful.props`, `BannedSymbols.Faithful.txt`; the analyzer package is pinned in `Directory.Packages.props` (`Microsoft.CodeAnalysis.BannedApiAnalyzers` 4.14.0).

Refinements made while implementing (2026-10-21):

- **Reflection** is banned by its APIs (member infos, `Assembly`, `Activator`, `Reflection.Emit`, `Type.Get*` lookups), not by the whole `System.Reflection` namespace: the SDK-generated assembly attributes live in that namespace, and `AnalysisLevel` all requires one of them (CA1016).
- **Track F additionally bans** `System.Runtime.Intrinsics`, `System.Numerics.Vector`, `Unsafe` and `MemoryMarshal`: explicit SIMD and unsafe code have no counterpart in typescript-go (rule 3); they belong to Track O, which must evaluate them, so the difference between the tracks shows what they contribute (confirmed by the author 2026-10-21). The BCL's own internal vectorization (e.g. in `string.IndexOf`) is not affected, just as Go's standard library vectorizes internally.
- Verified with deliberate violations: Track F rejects LINQ and intrinsics (RS0030); Track O rejects heap allocation (NS001) and `lock` (NS003), and accepts SIMD, `SearchValues`, UTF-8 transcoding and `stackalloc` under `[NoAlloc(NoLock | NoBlock)]`.

The ruleset files are committed before Phase 1 starts and pinned by that commit; changing them later is an amendment (section 12). Suppressions are allowed only with a comment citing the typescript-go counterpart.

### 5.2 Multi-model workflow

Each module is ported independently by several AI models from the same inputs: the TypeScript source file at the pinned commit, the corresponding typescript-go file as a worked example, the port rules, and the dump tooling. Gate 1, then Gate 2. Only survivors are ranked, by module-level median wall-clock on the **tuning corpus** (section 3) and then by bytes allocated; never on the benchmark corpus (A1). Every failure is logged with the diff that killed it; those logs are published.

**[DECISION, 2026-10-21; the author delegated the choice of models to the assistant]** Models and attempts:

| Role | Model | What it does |
|---|---|---|
| Orchestrator | Claude Opus 5.5 (`claude-opus-5-5`) | assembles the identical inputs, starts the porting agents, runs Gate 1 and Gate 2, ranks, logs; never edits an attempt's code |
| Track F, attempts 1 and 2 per module | Claude Opus 5.5 (`claude-opus-5-5`) | independent porting agents, each with a fresh context |
| Track F, attempt 3 per module | Claude Sonnet 5.5 (`claude-sonnet-5-5`) | independent porting agent, fresh context; keeps a comparison between models |
| Track O variants | Claude Opus 5.5 (`claude-opus-5-5`) | one agent per variant; variants differ by optimization strategy (§5.4) |

- **3 attempts per Track F module** (confirmed by the author), the same upper bound as Track O's variants. **Amended (prereg/v3, 2026-10-21): from module M5 on, 2 attempts per Track F module — attempt 1 Opus 5.5, attempt 2 Sonnet 5.5 — and agents run at medium reasoning effort; high effort only for Track O optimization work.** Reason: token cost (the three M3+M4 attempts with their sub-agents hit the account's spend limit). Two attempts keep both the chance of a passing port and the model comparison; the spread between independent attempts of one model is no longer measured from M5 on.
- **An attempt** is one agent run from the identical inputs. It may iterate against the Gate 1 and Gate 2 diffs until it passes or its budget is used up; attempts do not see each other's code. The orchestrator records each attempt's iterations, final diffs and pass/fail.
- Why this split: two Opus attempts carry the chance of reaching byte-identical output within the budget; the Sonnet attempt keeps the brief's comparison between models at small cost. With a single model family the comparison measures mostly the spread between independent attempts; this is stated with every ranking.
- Model identifiers are recorded with each attempt's log. If a listed model is unavailable when a module is ported, the replacement is an amendment.

### 5.3 JSDoc

**[DECISION, CONFIRMED 2026-10-21] JSDoc parsing is in Phase 1 scope.** tsc parses JSDoc during parsing (in the benchmark: `JSDocParsingMode.ParseForTypeErrors`, i.e. all JSDoc in JS files, JSDoc with `@see`/`@link` in TS files), and typescript-go does the equivalent work. A C# parser without JSDoc would do less work than both references and get a speed advantage it did not earn. Consequence for Gate 1: the dump format gains a JSDoc section (JSDoc nodes and JSDoc diagnostics), compared against tsc 6.0.3 only, because typescript-go's JSDoc representation is not comparable node for node ([ast-dump-format.md](ast-dump-format.md), decision 4). The detailed scope is in the Phase 1 plan.

### 5.4 Track O: optimized port

Source and equivalence as Track F: ported from tsc 6.0.3, Gate 1 byte-identical to tsc 6.0.3 on all 50,177 units including the JSDoc section. Everything else differs:

1. **Optimizations are allowed without a typescript-go counterpart.** Every module's variant set (rule 5) must include an evaluation of: SIMD (`System.Runtime.Intrinsics`, `Vector128`/`Vector256`), stack allocation and spans (`stackalloc`, `Span<T>`), and allocation-free data layout. A technique that loses is recorded with its numbers, not silently dropped.
2. **SIMD behind runtime checks only.** NativeAOT is built with the default instruction-set baseline; wider instructions (AVX2 on the reference machine) are used only behind `IsSupported`/`IsHardwareAccelerated` checks with a scalar fallback. Reason: the typescript-go release binary is built for `GOAMD64=v1` and does not assume AVX2 either.
3. **No GC wherever possible.** Ported code carries `[NoAlloc(Constraints.NoLock | Constraints.NoBlock)]` by default, verified by the repository's NoAlloc analyzer ([analyzers/NeverSharp.NoAlloc/](../analyzers/NeverSharp.NoAlloc/), diagnostics NS001–NS015 as errors). Code that cannot meet it says so with `Allow` + `Reason` or `[NoAllocUnchecked(reason)]`; the number of such members is reported with every Track O result.
4. **Syntax tree as structs in arenas.** Nodes are structs stored in large pooled arrays and referenced by index; arena growth is the only allocation of the parse phase. (typescript-go uses a slice arena for node lists, but individual nodes are heap objects; Track F keeps the source's node objects.)
5. **Variant process, before each step continues.** For each step (module or function, as listed in the Phase 1 plan), at most **3 variants** are built: different optimization strategies, implementations by different AI models, or both. Each variant must pass Gate 1 and Gate 2 (Track O ruleset, below). Survivors are measured at two levels: **micro** (BenchmarkDotNet with MemoryDiagnoser, per function) and **module** (the parse-only harness on the tuning corpus). The step continues with the winner only.
6. **Tuning corpus ≠ benchmark corpus.** Variants are compared on the tuning corpus only (section 3). The five benchmark repositories are not used for any choice in Track O; they are measured once, for the result. Otherwise the result would be fitted to the corpus it is judged on.
7. **Selection rule.** The winner is the variant with the lowest module-level median on the tuning corpus; if the 95 % intervals of the best variants overlap, the one with fewer bytes allocated wins, then the simpler one (fewer lines). Every variant, its numbers and the reason it lost are logged and published.

**Track O Gate 2:** the Track F settings of 5.1 (nullable, warnings as errors, `AnalysisLevel` all, code style) plus the NoAlloc analyzer; the banned-API list is reduced to reflection, `dynamic` and `System.Linq` in `src/`. Intrinsics, `Unsafe` and `stackalloc` are allowed. Files: `eng/Gate2.Optimized.props`, `eng/BannedSymbols.Optimized.txt`, and `eng/simd.noalloc-allowlist.toml`: the runtime members the mandatory SIMD, span and decoding variants need (Vector128/256, SSE2/SSE4.1/AVX2 intrinsics, `SearchValues<T>` lookups, `IndexOfAny`/`IndexOfAnyExcept` with `SearchValues`, `Utf8.ToUtf16`), each with its reason, because the analyzer's built-in BCL baseline does not list them. **Amended (prereg/v6, 2026-10-22):** a second allowlist, `eng/optimized.noalloc-allowlist.toml`, is part of the ruleset: (1) 24 further runtime members the ported code calls that do not allocate, lock or block (array copy/clear, span helpers, dictionary and set lookups, `List<T>` count/indexer/`RemoveRange`, ordinal string comparisons, `Type ==`), each with its reason; (2) one mirror entry per `[NoAllocUnchecked(reason)]` member, so that `[NoAlloc]` callers keep their contract on the rare paths that reach an exception (the analyzer reports NS013 otherwise). The mirror entries add no exceptions: the exceptions are the `[NoAllocUnchecked]` members, counted and listed with every result (`harness/port/noalloc-report.mjs`, which also reports the number of allowlist entries).

## 6. Measurement

### 6.1 Reference machine

[hardware.md](hardware.md): AMD Ryzen 9 3900X (12 cores / 24 threads, SMT on, fixed all-core clock 4150 MHz as measured), 64 GB DDR4-3200, MSI X570-A PRO, BIOS H.60, Corsair Force MP510 NVMe (benchmark volume), Windows 11 Pro 10.0.26200.6584. Power plan High performance, minimum and maximum processor state 100 %. Defender on with a path exclusion for the project directory; VBS unchanged. No hardware, BIOS, OS feature or driver change during the project; any such change is an amendment and requires a new baseline.

### 6.2 Toolchain

| Tool | Version |
|---|---|
| .NET SDK | 10.0.402 (runtime 10.0.13), `global.json` with `rollForward: disable` |
| Go | Microsoft build of Go 1.26.4-1 (the toolset of the tsgo 7.0.2 release binary), release flags |
| Node.js | 24.21.0 (runs tsc 6.0.3) |
| hyperfine | 1.20.0 |
| tsc | npm `typescript@6.0.3`, integrity-checked |
| Rust | 1.99.1 via rustup, local under `.tools/rust` (tsrs's pinned toolchain; prereg/v5) |

### 6.3 Method

[benchmark-harness.md](benchmark-harness.md), sections 4–7, in force at the tagged commit:

- Parse-only drivers read every file of the parse set, parse it, and keep all trees alive until the end (B2), in the high priority class (B9).
- CLI wall-clock with hyperfine, `--shell=none`, 3 warmup runs, **N = 20** timed runs per (repository, configuration); fixed configuration order (B7).
- Peak working set (Windows counterpart of peak RSS) and peak commit from 5 separate runs (B8).
- Rig checks before every run: processor states, no clock above nominal under load, idle median of ten 1 s windows < 12 % (B10), clean committed harness. A run that fails any check is a probe and is never reported as a result.
- **Driver file reading (A8):** every C# driver reads and decodes files with the semantics of `ts.sys.readFile` (UTF-16 LE/BE and UTF-8 byte order marks detected, UTF-8 otherwise). Track F uses the counterpart API (`File.ReadAllText`); in Track O, reading and decoding is step O1 and measured like every other step.
- **Fixed cost (A2):** every configuration also runs on an empty file list (process start-up, runtime initialization, driver overhead), with the same method, in the same run. Reported next to every result, never subtracted from it.
- **Same session:** every comparison comes from one protocol-compliant run that contains both the C# configurations and the typescript-go configurations. The baseline of 2026-10-21 validates the rig; it is not reused as the comparison partner of a later C# run.

### 6.4 Configurations, all reported

| Id | What | Role |
|---|---|---|
| `tsgo7-parse` | typescript-go 7.0.2, parallel (default) | **baseline** |
| `tsgo7-parse-single` | typescript-go 7.0.2, single-threaded | reference |
| `tsrs-parse` / `tsrs-parse-single` | tsrs 0.18.0 (Rust port of TypeScript 7 / typescript-go), parallel on rayon as tsrs parses files / single-threaded | **secondary reference** (prereg/v5) |
| `tsc6-parse` | tsc 6.0.3 on Node 24, single-threaded | reference |
| `cs-aot-ws` / `cs-aot-ws-single` | C# NativeAOT, Workstation GC, parallel / single-threaded | **primary C# configuration** (parallel) |
| `cs-aot-srv` / `cs-aot-srv-single` | C# NativeAOT, Server GC | secondary |
| `cs-jit-ws` / `cs-jit-ws-single` | C# .NET 10 JIT (TieredPGO default), Workstation GC | secondary |
| `cs-jit-srv` / `cs-jit-srv-single` | C# .NET 10 JIT, Server GC | secondary |
| `cs-o-aot-ws` / `cs-o-aot-ws-single` | Track O, NativeAOT, Workstation GC | **primary Track O configuration** (parallel) |
| `cs-o-aot-srv`, `cs-o-jit-ws`, `cs-o-jit-srv` (each also `-single`) | Track O, other runtime and GC combinations | Track O secondary |

**Configuration contents (A7).** All configurations use .NET defaults except what their name states; the project and runtime properties are committed before Phase 1 and are part of the protocol:

| Property | `-aot-` | `-jit-` |
|---|---|---|
| `PublishAot` | true | false |
| `PublishReadyToRun` | false | false |
| `TieredCompilation`, `TieredPGO` | not applicable | default (on) |
| `ServerGarbageCollection` | false for `-ws`, true for `-srv` | same |
| `ConcurrentGarbageCollection`, GC dynamic adaptation (DATAS) | default | default |
| `IlcInstructionSet`, `IlcOptimizationPreference` | unset (defaults) | not applicable |
| `InvariantGlobalization` | true (repository default; the parser uses no culture-sensitive operations) | true |

The `cs-*` rows without `-o-` are Track F. Parallel C# configurations schedule one work item per file (read + parse), the counterpart of typescript-go's `core.WorkGroup`, with the runtime's default degree of parallelism.

**[DECISION, CONFIRMED 2026-10-21] The primary C# configuration is NativeAOT with Workstation GC, parallel.** It is fixed now so that it cannot be chosen after the numbers exist. Reasons: typescript-go ships as a natively compiled single binary, so NativeAOT is the like-for-like deployment model for a CLI wall-clock comparison; Workstation GC is the .NET default. The other configurations are always reported; they cannot carry the primary claim (section 8).

## 7. Metrics

| Metric | Source | Role |
|---|---|---|
| Wall-clock per repository (median of N = 20) | hyperfine | **primary** |
| MAD/median | hyperfine timings | reported; rig gate on single-threaded configurations (B11) |
| Files/s, MB/s (10^6 bytes) | parse set size / median | reported |
| Peak working set, peak commit | peakmem, median of 5 | reported |
| Bytes allocated, GC count per generation, total GC pause | C# driver (`GC.GetTotalAllocatedBytes`, `GC.CollectionCount`, `GC.GetTotalPauseDuration`) | reported, C# only |
| In-process parse time and allocations | BenchmarkDotNet with MemoryDiagnoser | supplementary table |

## 8. Endpoints and decision rule

**Primary endpoint.** For the primary C# configuration `cs-aot-ws` against the baseline `tsgo7-parse`, from one protocol-compliant run:

> G = geometric mean over the five repositories of median(C#) / median(typescript-go),

with a 95 % percentile-bootstrap confidence interval: 10,000 resamples, independent resampling of the 20 timed runs of each (repository, configuration), all repositories resampled jointly, seed `20261021` (`harness/bench/stats.mjs`).

**[DECISION, CONFIRMED 2026-10-21] Decision rule:**

| Interval for G | Verdict |
|---|---|
| upper bound ≤ 1.20 | **parity claim allowed**: "within 20 % of typescript-go" for parsing |
| lower bound > 1.20 | **no parity**: "more than 20 % slower than typescript-go" for parsing |
| contains 1.20 | **inconclusive**: reported as such; neither claim is made |

A claim that C# is *faster* than typescript-go additionally requires the upper bound < 1.00.

**Secondary endpoints.** Per-repository ratios with their intervals; all other C# configurations against `tsgo7-parse`; single-threaded C# against `tsgo7-parse-single`; peak memory ratios. A parity statement about a secondary configuration is allowed only with a Bonferroni-adjusted interval (98.75 % for the four parallel C# configurations) and is labeled secondary. Secondary results never replace the primary verdict.

**Secondary reference: tsrs (amended, prereg/v5, 2026-10-22).** tsrs 0.18.0, the Rust port of TypeScript 7 (it ports typescript-go at TypeScript commit `b85298b6a81f`, 7.1.0-dev), is measured in the same run as `tsrs-parse` / `tsrs-parse-single` (driver `harness/bench/rust/parse-rs`: tsrs's file reading and `parse_source_file_owned` per file, rayon pool as in tsrs's compiler, tsrs's allocator, release profile and Cargo.lock). Every C# configuration is reported against it as well (report Table 2b: the same ratio, interval and seed). A statement against tsrs uses the same thresholds as the primary rule ("within 20 %" needs the upper bound ≤ 1.20, "faster" the upper bound < 1.00), is labeled **secondary**, and never replaces the verdict against typescript-go. Disclosed differences in the work done (docs/references.md §2a): tsrs parses JSDoc in `.ts` files lazily (TypeScript 7.1), typescript-go 7.0.2 eagerly, the C# tracks and tsc as tsc's host does (`ParseForTypeErrors`); tsrs represents `implements` clauses as `TypeReference` (TypeScript 7.1 AST). Decided before any Track O measurement; the Track F verdict (M6 run 3) was measured without tsrs and stands unchanged.

**Track O endpoint.** The same G, interval and decision rule for `cs-o-aot-ws` against `tsgo7-parse`, from the same run as the Track F verdict if both are ready, otherwise from its own protocol-compliant run with `tsgo7-parse` in it. It is reported as **"optimized track"** next to, never instead of, the Track F verdict, together with the list of Track O steps evaluated (each with its winning variant) and not evaluated (A9). Secondary Track O configurations follow the secondary rule above.

**Linux replication (A4).** After the Windows verdict, the deciding measurement is repeated on the same hardware under a native Linux installation (not WSL), with the same harness, corpus, configurations and decision rule, and reported as a pre-registered **secondary replication**. Its verdict is reported next to the Windows verdict; if the two differ, both are stated, and neither replaces the other. Distribution, kernel and rig settings for Linux are fixed by amendment before that run. Until it exists, every result states "Windows only".

**One final measurement.** The verdict comes from the first protocol-compliant run after Gate 1 and Gate 2 pass for the complete Phase 1 module. Replication runs may be published, each in full and alongside the first; they do not replace it.

**Variance gate (kill criterion 3).** Every single-threaded configuration on every repository has MAD/median < 3 % in the deciding run; otherwise the rig is fixed and the run repeated before any number is published (B11). The parallel configurations' MAD/median is reported next to each median. **Amended (prereg/v4, 2026-10-22; decided after seeing two runs):** the gate binds `tsc6-parse`, `tsgo7-parse-single` and the primary single-threaded C# configuration of each track (`cs-aot-ws-single`, `cs-o-aot-ws-single`); the other single-threaded C# configurations report their spread, with a variance note on any secondary result above 3 %. The verdict comes from the first protocol-compliant run after this tag that passes the amended gate; M6 runs 1 and 2 (before the tag) are published in full but carry no verdict.

## 9. Kill criteria for the project

1. **Oracles disagree.** Stop and publish if the comparison of tsc 6.0.3 and typescript-go 7.0.2 shows any difference that is neither removed by a pre-registered normalization rule (N1–N9) nor listed in the pre-registered divergence list ([ast-dump-format.md](ast-dump-format.md), decision 3).
2. **Phase 1 not equivalent in time.** Common budget for both tracks: **18 calendar weeks from the tag date at about 7 h/week** (decided by the author 2026-10-21, after the effort estimate in [phase1-plan.md](phase1-plan.md), section 9).
   - Track F does not pass Gate 1 within the budget: publish the gap analysis (the failing units and categories) and stop the project; without Track F there is no verdict.
   - Track O does not pass Gate 1 within the budget: publish its gap analysis and end Track O; the Track F verdict is unaffected.
3. **Rig variance.** The variance gate fails on the reference machine and cannot be fixed: no port numbers are published.

## 10. Phases

| Phase | Scope | Measured as |
|---|---|---|
| 0 | harness, corpus, baselines, this document | — |
| 1 | `scanner.ts`, `parser.ts` and the slice of `types.ts`, `utilities.ts`, `diagnosticMessages.json` they need | parse-only, this document |
| 2 | binder | pre-registered by amendment before Phase 2 code exists |
| 3 | checker | monthly "% of conformance suite passing" progress line; method pre-registered by amendment before Phase 3 code exists |

Out of scope for v1: emitter, language service, incremental parsing (syntax cursor), tsserver/LSP, the public compiler API.

**[DECISION, CONFIRMED 2026-10-21]** The optional reference column "oxc_parser (Rust), best-in-class per language, not a port" from the project brief is **not** part of the pre-registered results. It adds a third toolchain and invites a debate about which non-port parser is representative, without bearing on the question. It may be added later as a column labeled post hoc.

## 11. Reporting rule

**Wording rule (A3).** Every published claim about speed

- names the track ("faithful" or "optimized");
- names the measured scope ("parsing" in Phase 1);
- gives G with its 95 % interval;
- lists every repository whose own interval lies above 1.20;
- states the peak working-set ratio in the same sentence;
- says "Windows only" until the Linux replication exists.

Template (illustrative numbers, not a result): *"Parsing, faithful track: the C# port takes 1.12× [1.08, 1.17] the wall-clock of typescript-go (geometric mean over Microsoft's five benchmark repositories; per repository …; above 1.20: none), with 0.9× its peak memory. Windows only."*

Nothing outside this protocol is reported as a result. Post-hoc observations are allowed and are labeled **post hoc** wherever they appear. Every failure of Gate 1 or Gate 2 is logged with its diff. Probe runs are never reported as results. All result files (run.json, report, raw hyperfine exports) are published with every result.

### 11.1 Timestamp of the pre-registration

The repository stays private until the project's results are published (author's decision, 2026-10-21), so a commit or tag date alone could not show that this document existed before the first C# measurement. **[DECISION, CONFIRMED 2026-10-21] Every pre-registration tag (`prereg/v1` and every later amendment tag) gets an OpenTimestamps proof:**

1. A stamp file `docs/timestamps/prereg-vN.txt` records the tag name, the full hash of the tagged commit, and the SHA-256 of `docs/preregistration.md` at that commit.
2. `ots stamp` submits only the hash of that stamp file to the public OpenTimestamps calendars; the content stays private. The resulting `prereg-vN.txt.ots` proof is committed next to it, and upgraded with `ots upgrade` once the Bitcoin attestation is complete (usually a few hours).
3. Anyone can later verify with `ots verify` that the stamp file, and through the hashes in it the tagged commit and this document, existed no later than the time of the Bitcoin block.

The proof shows existence at a time, not authorship, and not that no earlier version existed; earlier versions are visible in the repository history once it is public.

## 12. Amendment rule

The protocol changes only by a new dated tag (`prereg/v2`, …) with a public note of what changed and why, and only before the next measurement whose outcome the change could affect. Changes to pinned inputs (pins.json, global.json, the analyzer ruleset, the normalization rules, the divergence list) are amendments.

### 12.1 Changes before the first tag

For transparency, the decisions that were revised during Phase 0, before this document was tagged. Each is documented where it was made.

| Date | Change | Reason | Where |
|---|---|---|---|
| 2026-10-14 | Go toolchain: upstream Go 1.26.9 → Microsoft Go 1.26.4-1 | identical toolset to the tsgo release binary | references.md §5 |
| 2026-10-14 | Equivalence corpus extended by the benchmark sources; divergence list 74 → 85 units | the brief requires the benchmark sources in the corpus | ast-dump-format.md |
| 2026-10-14 | Parse-only drivers keep all trees alive | discarding trees made memory and GC unrealistic | benchmark-harness.md B2 |
| 2026-10-14 | Reference machine: fixed all-core clock documented instead of "boost off = 3.8 GHz" | measured behavior of the machine | hardware.md §2.1 |
| 2026-10-15 | Drivers in the high priority class | background load on short parallel runs | benchmark-harness.md B9 |
| 2026-10-21 | Idle limit 5 % → 12 % | the machine idles at 7–10 % | B10 |
| 2026-10-21 | Variance gate on single-threaded configurations; CI-based verdicts | spread of short parallel runs in probe runs; **decided after seeing probe data**; its rationale was later qualified by the quiet baseline run | B11, benchmark-harness.md §9 |
| 2026-10-21 | Idle check: median of ten 1 s windows instead of one 3 s mean | single short window decided by momentary spikes | B10 amendment |
| 2026-10-21 | Two tracks: faithful (carries the verdict) and optimized (SIMD, stack allocation, NoAlloc, arenas, ≤ 3 measured variants per step, separately labeled) | author's decision before any C# code | §1, §5.4 |
| 2026-10-21 | Additions from the hostile review: A1 ranking on the tuning corpus (closes a gap: Track F ranking was on the benchmark corpus), A2 fixed-cost run, A3 wording rule, A4 Linux replication, A5 tuning corpus with TypeScript's own sources, A7 pinned configurations, A8 driver file reading, A9 Track O coverage list | author's decision | hostile-review.md |
| 2026-10-21 | Multi-model workflow fixed: Opus 5.5 orchestrates; Track F 3 attempts per module (2 × Opus 5.5, 1 × Sonnet 5.5); Track O variants by Opus 5.5 | author's decision on Opus 5.5 with agents and 3 attempts; split between models delegated to the assistant | §5.2 |
| 2026-10-21 | Phase 1 budget 8 weeks → 12 weeks → 18 weeks, common to both tracks | two tracks do not fit into 8 weeks; the Phase 1 plan's estimate (≈ 123 h) does not fit into 12 weeks (84 h) | §9, phase1-plan.md §9 |

### 12.2 Amendments after the first tag

| Date | Tag | Change | Reason | Where |
|---|---|---|---|---|
| 2026-10-22 | `prereg/v6` | §5.4 Track O Gate 2: second NoAlloc allowlist `eng/optimized.noalloc-allowlist.toml` (24 non-allocating runtime members; mirror entries of the `[NoAllocUnchecked]` exceptions) | written by step O-NA to keep 87 % NoAlloc coverage; accepted by the author; changes coverage reporting only, not speed; before the Track O endpoint run | eng/, phase1-progress.md |
| 2026-10-24 | `prereg/v7` | §15 Phase 2: the binder of TypeScript 6.0.3 for Track O only; Gate B (byte-identical binder dumps against tsc 6.0.3 `bindSourceFile`, both JSDoc modes); endpoint parse+bind `cs-o-aot-ws-bind` vs `tsgo7-bind` with the §8 rule | scope decided by the author (Track F not extended); tagged after Gate B passed and before any binder measurement | phase2-plan.md, phase1-progress.md (Phase 2) |
| 2026-10-24 | `prereg/v8` | §16 measurement profiles (full, quick, binder) with the variance gate binding per profile; binder endpoint with the NativeAOT runtimes only | run time; decided by the author before any binder measurement; earlier results unchanged | benchmark-harness.md, bench.mjs, report.mjs |
| 2026-10-29 | `prereg/v9` | §17 Phase 3: the checker of TypeScript 6.0.3 for Track O only; Gate C (byte-identical diagnostics, types and symbols dumps against tsc 6.0.3 for all units; 027977's timeout under load accepted); endpoint parse+bind+check of each repository program `cs-o-aot-ws-check` vs `tsgo7-check` with the §8 rule, profile `check`, tsrs 0.18.0 as secondary reference | scope decided by the author on 2026-10-25; the endpoint details and tsrs decided on 2026-10-29 before any checker measurement | phase3-plan.md, benchmark-harness.md, bench.mjs, check-equivalence.mjs, the drivers |
| 2026-11-02 | `prereg/v10` | §18 program check with 12 checkers on both sides (`--checkers 12`, profile `check12`, report Table 2f): `cs-o-aot-ws-check12` and `cs-o-aot-srv-check12` vs `tsgo7-check12`, secondary; the endpoint (§17) unchanged | the author wants the C# checker compared at a checker count that uses the machine; N = 12 chosen after the unregistered exploration CN1 (disclosed in §18); accepted by the author before any profile-`check12` measurement | phase1-progress.md (CN1), benchmark-harness.md, bench.mjs, report.mjs, the drivers |
| 2026-10-22 | `prereg/v5` | Secondary reference tsrs 0.18.0 (`tsrs-parse`, `tsrs-parse-single`, report Table 2b), pins `references.tsrs` and `tools.rust` (Rust 1.99.1) | a Rust port of TypeScript 7 appeared (Cloudflare/Oxc, 2026-10); the author wants Track O compared with it as well; decided before any Track O measurement | §6.2, §6.4, §8, references.md §2a, pins.json |
| 2026-10-22 | `prereg/v4` | §8 variance gate: binding for tsc6-parse, tsgo7-parse-single and the primary single-threaded C# configuration of each track; other single-threaded C# configurations reported with a variance note | M6 runs 1 and 2 failed the gate only in JIT/Server-GC "single-threaded" configurations (3.36–5.08 %) while the rig configurations stayed ≤ 1.43 % and cs-aot-ws-single ≤ 1.68 %; **decided after seeing both runs**; the verdict comes from a run after this tag | benchmark-harness.md B11, results/m6/ |
| 2026-10-21 | `prereg/v3` | §5.2: 2 attempts per Track F module from M5 on (1 × Opus 5.5, 1 × Sonnet 5.5), agents at medium effort, high only for Track O | token cost; M3+M4 attempt 1 was stopped by the author before finishing (logged in phase1-progress.md) | phase1-progress.md, modules/README.md |
| 2026-10-21 | `prereg/v2` | §6.4 A7 table: property name `ServerGarbageCollector` → `ServerGarbageCollection` | erratum: `ServerGarbageCollector` is not an MSBuild property and is silently ignored, so the `-srv` configurations would have run with Workstation GC; the intent (Server GC for `-srv`) is unchanged. Found by the first publish of the four configurations, before any C# measurement. | eng/RuntimeConfiguration.props, phase1-progress.md |

## 13. Prior art

typescript-go (the reference and the method this project copies); oxc and swc (Rust parsers, not ports); ezno and stc (Rust type-checker attempts, experimental or abandoned).

## 14. Before tagging

- [x] Author confirmed every **[DECISION]** in this document (2026-10-21).
- [x] Multi-model workflow: models and attempts per model (5.2).
- [x] Phase 1 plan committed ([phase1-plan.md](phase1-plan.md)): module boundaries, JSDoc scope, effort estimate.
- [x] JSDoc section of the dump format specified and the tsc 6.0.3 side implemented (4.2; ast-dump-format.md §4, format version 1).
- [x] Analyzer ruleset committed for both tracks (5.1, 5.4): `eng/Gate2.*.props`, `eng/BannedSymbols.*.txt`, `eng/simd.noalloc-allowlist.toml`, `scripts/check-headers.mjs`; placeholder projects `src/NeverSharp.Faithful`, `src/NeverSharp.Optimized` build clean.
- [x] C# driver contract (6.3, 7): [benchmark-harness.md](benchmark-harness.md) §4.1; the runner stores the C# GC metrics.
- [x] Tuning corpus manifest generated (section 3): `harness/bench/tuning.mjs`, 24,511 files (23,910 tests/cases units, 601 TypeScript sources), 37.5 MiB.
- [x] Configuration project files committed (A7): `eng/RuntimeConfiguration.props` committed; publish of each configuration verified 2026-10-21 by `harness/bench/build-cs.mjs` (each driver reports its runtime: NativeAOT or JIT, Server or Workstation GC). The check found the erratum in §12.2.
- [x] **A6** feasible and adopted: `harness/bench/crosscheck.mjs` runs in the deciding session and is reported with the result: per repository, the Go driver's parse time (median minus fixed cost) against tsgo's own `Parse time` (`--extendedDiagnostics`, resolution and lib files off, median of 5). Feasibility check 2026-10-21 (different sessions, not a measurement): driver/tsgo 0.72–1.05 across the five repositories, i.e. the driver is not slower than tsgo's own parse phase except +5 % on vscode.
- [ ] Harness support for Linux (rig checks, peak memory) before the replication run (A4); may follow the tag.
- [x] OpenTimestamps proof for the tag (§11.1): `docs/timestamps/prereg-v1.txt` and its `.ots` proof, opentimestamps-client 0.7.2.
- [ ] Repository published when the author decides the project is done (not a condition for the tag; §11.1 covers the timestamp).

## 15. Phase 2: binder (amendment `prereg/v7`, accepted 2026-10-24)

**Scope (decided by the author, 2026-10-24).** The binder of TypeScript 6.0.3 (`src/compiler/binder.ts`) is ported for Track O only; Track F is not extended. Port rules of §5.4 apply. Plan: [phase2-plan.md](phase2-plan.md).

**Gate B.** Byte-identical canonical binder dumps (symbols, symbol tables, containers, flow graph, node flags after binding, bind diagnostics) against tsc 6.0.3 (`ts.bindSourceFile` with options `{}`) for all 50,177 equivalence units, in JSDoc modes ParseAll and ParseForTypeErrors. Track O Gate 2 applies to the binder code.

**Endpoint.** Parse+bind per file: `cs-o-aot-ws-bind` against `tsgo7-bind` (typescript-go 7.0.2: `parser.ParseSourceFile` and `binder.BindSourceFile` per file, parallel on its work group), with G, interval and decision rule as in §8, from the first protocol-compliant run after Gate B passes completely. Single-threaded and the other C# runtimes as secondary configurations, as in §8. Disclosed with every comparison: typescript-go's binder ports TypeScript 7 and differs from 6.0.3's in places.

**Status.** Accepted by the author on 2026-10-24 and tagged `prereg/v7` (OpenTimestamps) after Gate B passed and before any binder measurement.

## 16. Measurement profiles (amendment `prereg/v8`, accepted 2026-10-24)

**Decided by the author on 2026-10-24, before any binder measurement.** Protocol runs select one of three fixed configuration sets (`harness/bench/bench.mjs --profile`; the profile is recorded in run.json):

| Profile | Configurations | Variance gate binds | Used for |
|---|---|---|---|
| `full` | every configuration | tsc6-parse, tsgo7-parse-single, cs-aot-ws-single, cs-o-aot-ws-single (prereg/v4) | runs that report every configuration |
| `quick` | tsgo7-parse(-single), tsrs-parse(-single), cs-o-aot-ws(-single) | tsgo7-parse-single, cs-o-aot-ws-single | additional parse-only runs of Track O |
| `binder` | tsgo7-bind(-single), tsrs-bind(-single), cs-o-aot-ws-bind(-single), cs-o-aot-srv-bind(-single) | tsgo7-bind-single, cs-o-aot-ws-bind-single | the Phase 2 binder endpoint (§15) and its additional runs |

Every binding configuration of the run's profile must be present and below the limit; otherwise the gate fails. This replaces §15's "the other C# runtimes as secondary configurations" for the binder: the binder endpoint reports the NativeAOT runtimes (Workstation and Server GC) only; the JIT runtimes are not measured for the binder (secondary configurations in every Phase 1 run, never close to the primary one). Reason: run time (the last full parse-only run took 45 minutes, of which tsc6-parse 24 %, Track F 16 % and the JIT single-threaded configurations 25 %). The Track F verdict, the Track O endpoint and the six additional runs are unchanged and keep the rules they were measured under.

## 17. Phase 3: type checker (amendment `prereg/v9`, accepted 2026-10-29)

**Scope (decided by the author, 2026-10-25).** The type checker of TypeScript 6.0.3 (`src/compiler/checker.ts`) in full, for Track O only, with the program layer it needs (compiler options, host, default library, file set). Port rules of §5.4 apply. Plan: [phase3-plan.md](phase3-plan.md).

**Gate C.** Every equivalence unit as a one-file program (the unit plus TypeScript 6.0.3's default library; options `{}` with `noResolve: true`, plus `allowJs: true` for JavaScript/JSX units so that JavaScript typing is covered — decided by the author on 2026-10-25): byte-identical canonical dumps against tsc 6.0.3 of (1) the semantic, global and suggestion diagnostics with flattened message chains, (2) the types of every expression and declaration name in the style of TypeScript's `.types` baselines and (3) the symbols the baselines write. Track O Gate 2 applies to the checker code. Gate C counts as passed since 2026-10-29 (commit 160d746): 50,176 of 50,177 units identical in all three sections, none differing; unit 027977 is identical when run alone and exceeds the per-unit timeout only under the gate's load, which the author accepted.

**Endpoint.** Each benchmark repository's file list as one program with the Gate C options, parse+bind+check: `cs-o-aot-ws-check` against `tsgo7-check` (typescript-go 7.0.2 checking the same program), with G, interval and decision rule as in §8, from the first protocol-compliant run after Gate C has passed; a measurement profile `check` (§16 style: `cs-o-aot-ws-check(-single)`, `cs-o-aot-srv-check(-single)`, `tsgo7-check(-single)`, `tsrs-check(-single)`) with the single-threaded variants of `cs-o-aot-ws-check` and `tsgo7-check` as the variance gate's binding rows. **Secondary reference (decided by the author, 2026-10-29):** tsrs 0.18.0 checking the same program (`tsrs_compiler::new_program`, semantic and global diagnostics, the same explicit options as typescript-go) is reported as for parsing (§8, prereg/v5), not binding; its default checker pool differs (checker count from the core count, 16 on the reference machine, files assigned by directory and stolen by idle checkers) and is disclosed with every comparison. Checked: the semantic diagnostics of every file and the global diagnostics (no suggestion diagnostics). Both sides resolve module names between the files of the program as tsc 6.0.3 does with `noResolve: true` (imports of files in the program resolve; files outside it are not added). Options: TypeScript 6.0.3's defaults, given to typescript-go explicitly where its TypeScript 7 defaults differ (table in docs/benchmark-harness.md). Parallel: typescript-go's default checker pool (4 checkers) and the same number of checkers with the same file assignment on the C# side; single-threaded: one checker. The diagnostic counts of both sides are reported with every comparison, and the C# diagnostics of each repository program are compared once with tsc 6.0.3's (counts, identical or not, differences by cause; `harness/bench/check-equivalence.mjs`). Before the tag all five repository programs were line-identical with tsc 6.0.3 (modules C10, C11). Disclosed: typescript-go's checker ports TypeScript 7 and differs from 6.0.3's in places; the repositories' dependencies are not installed and imports are not resolved, identically on both sides.

**Status.** Accepted by the author on 2026-10-29 and tagged `prereg/v9` (OpenTimestamps) after Gate C passed and before any checker measurement.

## 18. Program check with 12 checkers (amendment `prereg/v10`, accepted 2026-11-02)

**Purpose (decided by the author, 2026-11-02).** A secondary comparison of the program check of §17 with more checkers than typescript-go's default pool of 4, on both sides. It does not change the checker endpoint, Gate C, profile `check` or any result of §17; the endpoint stays G = 1.680.

**Configurations** (profile `check12`):
- `tsgo7-check12`: typescript-go 7.0.2 as `tsgo7-check`, with `CompilerOptions.Checkers = 12` (its own option; `checkerpool.go` assigns program file *i* to checker *i* mod 12).
- `cs-o-aot-ws-check12` and `cs-o-aot-srv-check12`: Track O as `cs-o-aot-ws-check` / `cs-o-aot-srv-check`, with 12 checkers and the same assignment (driver option `--checkers 12`; the checker itself is unchanged).
- `tsrs-check`: reported next to them, with its own pool (§17).
- `tsgo7-check-single` and `cs-o-aot-ws-check-single`: the binding rows of the variance gate, as in profile `check`.

**Comparisons.** Both are secondary and reported with G, interval and the wording of §8:
- `cs-o-aot-ws-check12` vs `tsgo7-check12` at 95 %;
- `cs-o-aot-srv-check12` vs `tsgo7-check12` at 98.75 % (Bonferroni, as the Server GC comparison of §17).

Table 2f compares every check configuration of the run with `tsgo7-check12`; tsrs is reported as in §17. The comparison is taken from the first protocol-compliant run of profile `check12` after the tag. Later runs are additional runs.

**Why 12.** 12 is the reference machine's physical core count. It was chosen after an exploratory measurement (CN1, 2026-11-01; not pre-registered, raw data in the session scratch, summarized in phase1-progress.md), which this amendment discloses:
- N = 12 was Track O's best count on both runtimes;
- for typescript-go, N = 12 came within 3 % of its best count (N = 16).

In CN1, at equal N:
- Track O under Workstation GC fell behind typescript-go (geometric mean 1.31 at N = 12), because the Workstation GC pause grows with the number of checkers;
- under Server GC it stayed ahead (0.89).

**Diagnostics.** With N checkers a file's diagnostics depend on which types its checker created before (C12), so the 12-checker counts differ from the 4-checker counts, on both sides. They are reported with every comparison, and CN1 found them deterministic from run to run. The fixed assignment *i* mod N can make a larger N slower for some programs (CN1: tuning-check at N = 12 against N = 4), which is disclosed.

**Status.** Accepted by the author on 2026-11-02 and tagged `prereg/v10` (OpenTimestamps) after the harness changes were committed and before any profile-`check12` measurement.

## 19. Phase 4: emitter (draft amendment `prereg/v11`)

**Scope (decided by the author, 2026-11-03).**
- The emitter of TypeScript 6.0.3 for Track O only: `.js` and `.d.ts` output under TypeScript 6.0.3's default options (target ES2025, module kind ES2022, no source maps, comments kept), plus `declaration: true`.
- This covers the transformers those options run (`getScriptTransformers` and `getDeclarationTransformers` of `transformer.ts`): TypeScript, ESNext, ES decorators, class fields, the ES module transformer, and declarations, with the emit resolver, emit helpers and comment emission.
- The CommonJS module transformer is in scope for files whose implied module format is CommonJS (`.cts`, `.cjs`). The default options run it for those files too (`transformImpliedNodeFormatDependentModule`); decided by the author on 2026-11-03.
- Transformers only other options need are out of scope: JSX, legacy decorators, downlevel targets, generators, the AMD, UMD and System modules, and source maps.
- Port rules of §5.4 apply. Plan: [phase4-plan.md](phase4-plan.md).

**Gate D.**
- Every equivalence unit as a one-file program with Gate C's options (§17) plus `declaration: true` and `newLine: lf`, emitted even when it has errors.
- The canonical dumps must be byte-identical against tsc 6.0.3 (`program.emit` with an in-memory `writeFile`) in three parts:
  1. every output file in emit order, with name and text;
  2. the emit diagnostics, with flattened message chains;
  3. `emitSkipped`.
- **Symbol ids (decided by the author, 2026-11-03).** Before comparing, both sides replace tsc's process-wide symbol id with a placeholder. This applies only to the names tsc builds from it: the late-bound name `__@<name>@<id>` and the local name serialization derives from it (`____<name>_<id>`, with `getUnusedName`'s optional suffix).
  - The id depends on how many symbols the oracle's worker numbered before the unit, not on the unit.
  - On the corpus this changes 1 unit (015420) and 2 lines. The compare reports the count with every run (`compare-emit.mjs`, `symbolIdNormalized`).
- **Units on which tsc 6.0.3 itself fails.** Four units (000863, 000913, 002379, 008381) fail with a `Debug Failure`. They match when Track O fails at the same ported assertion. They have no sections.
- Track O Gate 2 applies to the emitter code. Gate C and the printer gate stay passed.

**Endpoint.**
- Each benchmark repository's file list as one program with Gate D's options: parse+bind+check (the semantic and global diagnostics of §17) and emit of every file to memory.
- `cs-o-aot-ws-emit` against `tsgo7-emit`: typescript-go 7.0.2 with the same diagnostics, then `Program.Emit` with an in-memory `WriteFile`, through its default checker pool of 4. The C# side uses the same number of checkers and the same file assignment.
- G, interval and decision rule as in §8, from the first protocol-compliant run after Gate D has passed.
- Measurement profile `emit`, in the style of §16:
  - `cs-o-aot-ws-emit(-single)`, `cs-o-aot-srv-emit(-single)`, `tsgo7-emit(-single)`, `tsrs-emit(-single)`;
  - the single-threaded variants of `cs-o-aot-ws-emit` and `tsgo7-emit` are the variance gate's binding rows.
- **Secondary:**
  - Server GC at 98.75 % (Bonferroni);
  - tsrs 0.18.0 (`Program::emit`, the same explicit options) as for §17, with its own checker pool disclosed.
- **Reported with every comparison:** the output bytes and file counts of every side, and the diagnostic counts as in §17.
- **Equivalence:** the C# output of each repository program is compared once with tsc 6.0.3's (identical or not, differences by cause).
- **Options:** TypeScript 6.0.3's defaults, given to typescript-go and tsrs explicitly where their TypeScript 7 defaults differ.

**Disclosed:**
- typescript-go's and tsrs's emitters follow TypeScript 7 and differ from 6.0.3's in places;
- the repositories' dependencies are not installed and imports are not resolved, which affects import elision and declaration emit identically on all sides;
- **Parallel emit and the number of checkers:**
  - With several checkers, the `.d.ts` output and the emit diagnostics depend on the number of checkers, on both the C# side and typescript-go. The `.js` output does not.
  - Causes: union member order from per-checker type ids, and visibility marked by earlier files' declaration emit, which stays inside the checker that emitted them. This is the same kind of dependence as the parallel check diagnostics of §17.
  - The emit equivalence with tsc 6.0.3 is therefore taken single-threaded.
  - Output bytes and file counts of every configuration are reported with every comparison.
- **Work items:** the C# driver emits one work item per checker, with that checker's files in program order. typescript-go queues one work item per file and locks the file's checker per resolver call.
- **Tuning emit list:** tsc 6.0.3 cannot emit the tuning list as one program (a `Debug Failure` on 002379, and a TypeError once expando properties merge across global-script units). The tuning emit list leaves out 139 units (`harness/bench/tuning-emit-exclude.txt`). It is used for tuning only, never for the endpoint.
- **Profile `full`** keeps its earlier set. The emit configurations run only in profile `emit` or with `--configs`.
- Windows only.

**Status.** Draft, 2026-11-03. To be accepted by the author and tagged `prereg/v11` (OpenTimestamps) after Gate D passes and before any emit measurement.
