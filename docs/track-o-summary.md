# Track O: summary (closed 2026-10-24) — Windows only

Track O is the optimized C# port of the TypeScript 6.0.3 scanner and parser (pre-registration §5.4: SIMD, `stackalloc`, NoAlloc, arenas; at most 3 variants per step). It started on 2026-10-22 as a copy of Track F (O0) and was closed by the author on 2026-10-24, when the comparison against the Rust port had reached its run-to-run noise (about ±2 %). Every step passed Gate 1 (byte-identical AST dumps against tsc on all 50,177 equivalence units, in ParseAll, ParseNone and the token dump G1a), Track O's Gate 2 (analyzers including the NoAlloc analyzer) and the reader check; Track F's gate passed after every step.

## Verdicts

| | Result | Source |
|---|---|---|
| **Track F verdict** (faithful port, primary) | G = 6.31 [6.20, 6.41] vs typescript-go 7.0.2: **no parity** (more than 20 % slower) | `results/m6/2026-10-22T07-01-02-524Z/VERDICT.md` |
| **Track O endpoint** ("optimized track") | G = **0.60 [0.59, 0.61]** vs typescript-go 7.0.2: **faster** (upper bound < 1.00); vs tsrs 1.52 [1.50, 1.54] (secondary) | `results/track-o/endpoint/2026-10-22T18-55-52-105Z/VERDICT.md` |
| **Last additional run** (after O29; not the endpoint) | **0.40 [0.39, 0.40]** vs typescript-go; **1.04 [1.03, 1.05]** vs tsrs (secondary, within 20 %) | `results/track-o/additional/2026-10-24T06-41-11-991Z/NOTE.md` |

The endpoint is the first protocol-compliant run with Track O and typescript-go (pre-registration §8, "One final measurement"); the six additional runs are published next to it and never replace it. G is the geometric mean over the five benchmark repositories (vscode, sentry, bluesky, playwright, tldraw) of median(C#) / median(reference), 95 % percentile bootstrap, seed 20261021; primary configuration `cs-o-aot-ws` (NativeAOT, Workstation GC, parallel).

## Progression across the protocol runs

| Run (harness commit) | Steps since the previous run | vs typescript-go | single vs tsgo single | vs tsrs | single vs tsrs single | vscode vs tsrs |
|---|---|---:|---:|---:|---:|---:|
| Endpoint (51f5036) | O0–O1 | 0.60 [0.59, 0.61] | 0.920 | 1.52 [1.50, 1.54] | 1.724 | 1.72 |
| Additional 1 (36d6262) | O9, O10, O23 | 0.47 [0.46, 0.48] | 0.762 | 1.19 [1.18, 1.21] | 1.435 | 1.28 |
| Additional 2 (cfbabcf) | O5b, O11 | 0.46 [0.45, 0.46] | 0.720 | 1.16 [1.14, 1.17] | 1.357 | 1.14 |
| Additional 3 (4e32b26) | O12, O24 | 0.43 [0.42, 0.44] | 0.618 | 1.06 [1.05, 1.07] | 1.167 | 0.95 |
| Additional 4 (dadb71f) | O25, O26 | 0.41 [0.41, 0.42] | 0.571 | 1.04 [1.03, 1.06] | 1.077 | 0.95 |
| Additional 5 (3c6cb2d) | O27, O28 | 0.40 [0.39, 0.40] | 0.545 | 1.02 [1.01, 1.04] | 1.029 | 0.92 |
| Additional 6 (95b038e) | O29 | 0.40 [0.39, 0.40] | 0.540 | 1.04 [1.03, 1.05] | 1.021 | 0.92 |

All runs: rig checks ok, variance gate PASS, crosscheck A6 within 0.73–1.03. vscode, last run: Track O 0.295 s, tsrs 0.321 s, typescript-go 1.036 s; peak working set 881 / 956 / 2,176 MiB; Track O allocates 65 MB with 26 ms of GC pause in total (Track F: 3.3 GB, 6.8 s).

Development measurements on the tuning corpus (never on the benchmark repositories): `cs-o-aot-ws` from about 1,390 ms (seed) to about 358 ms; `M5.ParseTuningCorpus` (BenchmarkDotNet, in memory) from 1,649 ms (O6a) to 407 ms.

## What may be claimed

- **Primary (Track O endpoint):** the optimized C# port parses faster than typescript-go 7.0.2, G = 0.60 [0.59, 0.61], Windows only. Reported next to, never instead of, the Track F verdict (no parity, 6.31).
- **From additional runs (labeled as such):** about 2.5 times as fast as typescript-go (0.40); within 20 % of tsrs (secondary: 1.02–1.04, upper bounds ≤ 1.05).
- **Not claimed:** "faster than tsrs". Overall Track O stays above tsrs (lower bounds 1.01–1.03). On vscode alone Track O was below tsrs in four consecutive additional runs (0.95, 0.95, 0.92, 0.92), reported per repository, not claimed (a single repository of an additional run).

## Disclosed differences

- **Work done at parse time:** the C# tracks and tsc parse as tsc's compiler host does (`JSDocParsingMode.ParseForTypeErrors`); typescript-go 7.0.2 parses JSDoc eagerly; tsrs parses JSDoc in `.ts` files lazily (TypeScript 7.1). Parsing all JSDoc costs Track O about 4 % (`results/track-o/probes/jsdoc/NOTE.md`).
- **Code generation (each competitor as its project builds it, author's decision 2026-10-23):** typescript-go with `GOAMD64=v1` as in its release binary; tsrs with its own release profile (`opt-level = 3`, fat LTO, one codegen unit) and Rust's default target (x86-64 baseline); the C# drivers with .NET's defaults, whose NativeAOT target is x86-64-v2 (up to SSE4.2). No build uses AVX2; an x86-64-v3 build of Track O was no faster (`results/track-o/probes/avx/NOTE.md`). docs/references.md §2a.
- **Windows only:** the Linux replication (A4) has not been run.

## Steps (A9)

Evaluated, with the merged variant ([track-o-steps.md](track-o-steps.md) has every ratio; [phase1-progress.md](phase1-progress.md) every report):

| Step | What | Merged |
|---|---|---|
| O0 | seed from Track F | — |
| O6a / O6b | node handles; node arena layout (3 variants) | A: compact records |
| O5a | text as spans, character arena, interning | — |
| O7a | node lists as handles, one index arena | — |
| O-NA | remaining allocations, NoAlloc coverage (prereg/v6 allowlist) | — |
| O1 | file reading and decoding (A seam, B pooled bytes, C native text) | C: native text |
| O9 | per-file overhead (B hand over the scratch, C per-thread slabs) | C: per-thread slabs |
| O10 | side table without per-file allocation | one attempt |
| O2 + O3 (O23) | character classification, trivia (B lookup tables, C SIMD) | B (decided on a repeat run on the author's choice) |
| O5b | interning hash (keyword lookup part done inside O23) | one attempt |
| O11 | speculative parsing (B token replay, C exact pre-decisions) | C: exact pre-decisions |
| O12 | node creation in one record write | one attempt |
| O24 | scanner, second round (O23 C's SIMD parts on top of B) | one attempt |
| O25 | expression descent, exact shortcuts | one attempt |
| O26 | token consumption | one attempt |
| O27 | identifier interning keyed by characters | one attempt |
| O28 | call/member rest, node lists | one attempt |
| O29 | NativeAOT inlining audit | one attempt |

Not evaluated: **O4** (string, template and numeric literals as a step of its own; O24 covered string and template bodies and simple integers), **O7b** (further node-list layouts), **O8** (parallel scheduling; a probe showed Track O already scaling better than tsrs). Probes (development measurements, not steps): JSDoc mode, reading vs parsing (twice), AVX.

## NoAlloc

2,241 of 2,595 methods of Track O annotated `[NoAlloc(Constraints.NoLock | Constraints.NoBlock)]` (86.4 %), 0 with lifted constraints, 55 `[NoAllocUnchecked]` exceptions (arena growth, per-file result, error paths, regex checking, rare numeric forms), the allowlist of prereg/v6 unchanged since (`node harness/port/noalloc-report.mjs`).

## Process notes

- Runs not used, all archived: O12/O24 run 2 (idle before run 13.9 %, rig check failed; repeated); O25/O26 run 2 (orchestrator's error: O25 was not yet on main, so "O26 on O25" measured O26 alone; repeated after cherry-picking O25).
- Tie-break decisions by the author: O23 (B and C tied; the byte difference in the parallel run was noise, so the ranking was repeated and decided on the repeat), and for later steps the deterministic single-threaded bytes as the byte tie-break.
- The parse-only harness on the tuning corpus became dominated by file reading as parsing got faster; from O12 on, gains showed mainly in the in-memory benchmark and on vscode.
- Memory safety: Track O's source text (O1) and node data (O9, O10) live in native or pinned per-thread memory owned by the result; a tree must not be read after `SourceFile.Release()` (not called anywhere in the repository; finalizers as backstop).
