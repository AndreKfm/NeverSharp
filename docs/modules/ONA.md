# O-NA: remaining allocations and NoAlloc coverage — brief

The "NoAlloc compliance" block of [phase1-plan.md](../phase1-plan.md) §9 for Track O, run as one step without variants (it removes allocations; it does not choose between designs). Pre-registration §5.4: "`[NoAlloc(Constraints.NoLock | Constraints.NoBlock)]` on all ported code; the only intended allocations are arena growth and the per-file result. Every exception is `[NoAllocUnchecked(reason)]` or `Allow` + `Reason` and is counted in each result." Written by the orchestrator before the attempt started. The standing rules of [README.md](README.md) apply (Track O follows pre-registration §5.4).

## Starting point

Track O after O5a (`main`): tuning corpus `cs-o-aot-ws` 579 ms, 579 MB allocated, GC pause 347 ms; `M5.ParseTuningCorpus` 959 ms, 276 MB; NoAlloc 39 of 2,251 methods. The O5a attempt's allocation profile (dotnet-trace, JIT, single-threaded, tuning corpus) of what is left:

| Item | About |
|---|---:|
| exact copies of the node record `int[]` made when a parse ends | 100 MB |
| `CommentRange` objects and arrays (comment ranges for JSDoc and directives) | 40 MB |
| file contents as bytes (reading; that is step O1, not this one) | 39 MB |
| closures (`DisplayClass`), e.g. `Core.Some(..., a0 => ...)` and a parser closure | 24 MB |
| boxed `Node` | 19 MB |
| `Func` delegates | 12 MB |

## Goal

1. Remove the allocations above except file reading: avoid the end-of-parse copies where a pooled or exactly-sized arena can be kept instead (the per-file result must stay valid after the parse and independent of later parses); comment ranges without per-range objects (structs, spans or a range arena); no closures or delegates per call on hot paths (static lambdas with state passed explicitly, struct visitors, or methods); no boxing of `Node`.
2. Extend `[NoAlloc(Constraints.NoLock | Constraints.NoBlock)]` to every method of Track O that is allocation-free after this step, starting with the scanner and the parser's hot paths; the NoAlloc analyzer (Track O Gate 2) checks them. Allocating methods that must stay allocating (arena growth, the per-file result, error paths, regex checking, rare numeric forms) get `[NoAllocUnchecked(reason)]` only where they sit inside otherwise annotated code; count and list them.
3. Behaviour stays byte-identical (Gate 1), thread safety as before.

## What may change

`src/NeverSharp.Optimized/` and `tools/NeverSharp.Generate/emit-optimized.mjs` (rerun `generate.mjs`; generated files reproduced exactly). Not Track F, tests, harness, docs, tools. Not file reading (step O1).

## Gate

```
<main>/.tools/node-24.21.0/node.exe harness/port/gate.mjs --track optimized --main D:/Private/Source/Andre/projects/NeverSharp
```

All Track O gates, 50,177/50,177; Track F's gate still passes. Limit: 60 gate runs.

## Working

One attempt: Opus 5.5, high effort, no sub-agents (or at most 2 for mechanical annotation). Scratch folder `<session scratch>/ONA/`. Profile with `.tools/dotnet-trace` (`gc-verbose`) and `tools/NeverSharp.TraceSummary` as in [track-o-profile.md](../track-o-profile.md). Do not copy the benchmark project into other folders under the main checkout. Commit with `git commit -s`; do not push.

## Report

Model; last gate JSON line and gate runs; each allocation removed (how, bytes before/after); NoAlloc count before/after, `[NoAllocUnchecked]` list with reasons (`harness/port/noalloc-report.mjs`); your own timing and bytes against main; what is left; commit hash.

## Measurement (orchestrator)

As for O7a/O5a: parse-only harness on the tuning corpus (`cs-o-aot-ws`, 20 runs, rig-compliant) against main, plus `M5.ParseTuningCorpus`. Merged if every gate passes and it is not slower than main by the 95 % interval.
