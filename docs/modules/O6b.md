# O6b: node arena layout, up to 3 variants — brief

Step O6b of Track O ([track-o-steps.md](../track-o-steps.md)). O6a converted Track O to node handles behind `NodeStore` with one simple layout. O6b compares **three storage layouts behind that same API** (pre-registration §5.4: at most 3 variants per step; candidates from [phase1-plan.md](../phase1-plan.md) §7, O6). Each variant is one attempt; the winner is chosen by the pre-registered rule and merged. Written by the orchestrator before any attempt started. The standing rules of [README.md](README.md) apply (Track O follows pre-registration §5.4).

## Starting point

`src/NeverSharp.Optimized/` after O6a: `Arena/Node.cs` (`readonly struct Node`, one `int`), `Arena/NodeStore.cs` and `Generated/NodeStore.g.cs` (`internal sealed partial class NodeStore`: `Create`, `GetMark`/`Truncate`, base fields `Kind`, `Pos`, `End`, `Flags`, `Parent`, `TransformFlags`, `ModifierFlagsCache`, `Original`, `EmitNode`, 160 generated field accessors, `Is{Class}`), `Generated/NodeVisitor.g.cs` (`ForEachChild` over handles), emitter `tools/NeverSharp.Generate/emit-optimized.mjs`. The O6a layout: a 40-byte record per node plus per-kind blocks of int slots and object slots; see [phase1-progress.md](../phase1-progress.md), O6a.

Measured after O6a (`M5.ParseTuningCorpus`, NativeAOT): 1,649 ms, 823.5 MB allocated, GCs 66/34/3 (Track F: 2,495 ms, 737.8 MB).

## The three variants

| Variant | Layout | Ideas to evaluate (not prescribed) |
|---|---|---|
| **A: compact records (array of structs)** | One record per node, as small as possible; child handles and scalars of each kind inline in a fixed per-kind record shape or a compact slot block | move rare fields (`Original`, `ModifierFlagsCache`, `EmitNode`, mostly-null `JsDoc`) to side tables; exact initial sizing from the source length; growth without large over-capacity |
| **B: structure of arrays** | One array per base field (`kind[]`, `pos[]`, `end[]`, `flags[]`, `parent[]`, …) plus per-field or per-kind child arrays | the scanner/parser's hottest accesses (kind, pos, end, flags) touch dense arrays; truncation resets counts only |
| **C: header plus per-kind payload arenas** | A small common header per node (kind, pos, end, flags, parent, payload index) and one typed payload arena per node kind (struct arrays generated from the schema) | payload structs per kind without per-access offset lookups; object fields (strings, lists) in per-kind object arrays |

All variants keep: the `NodeStore` API used by the parser and factory (names and signatures as after O6a; adding members is allowed, changing callers is not), truncation to a mark, one store per parse, thread safety (no mutable static state except pools; a pool of reusable buffers is the counterpart of typescript-go's per-kind node arenas, `internal/ast/ast.go`), and Gate 1.

## What may change

- `src/NeverSharp.Optimized/Arena/` and `src/NeverSharp.Optimized/Generated/NodeStore.g.cs`, `NodeVisitor.g.cs`, and the emitter `tools/NeverSharp.Generate/emit-optimized.mjs` (rerun `generate.mjs`; the generated files must be reproduced exactly by it).
- Nothing else: not the parser, factory, scanner or core files of Track O, not Track F, not the tools, tests, harness or docs. A variant that needs an API addition adds it in the store and uses it only inside the store.
- `[NoAlloc]` on store members that are allocation-free is welcome (Track O Gate 2 checks it); `harness/port/noalloc-report.mjs` counts it.

## Gate

```
<main>/.tools/node-24.21.0/node.exe harness/port/gate.mjs --track optimized --main D:/Private/Source/Andre/projects/NeverSharp
```

All gates of Track O, 50,177 of 50,177 units in every comparison. Limit: 40 gate runs (standing rule 2).

## Working

- Attempt per variant: Opus 5.5, high effort (optimization work), no sub-agents. Scratch folder `<session scratch>/O6b-<variant>/`. C# with the file tools.
- Measure for yourself with the ranking workload (below) to iterate; the orchestrator's measurement decides.
- Commit with `git commit -s`; do not push.

## Report

Model; last gate JSON line and gate runs; the layout (record sizes, arrays, sizing and growth, truncation); your own M5 timing and bytes allocated against O6a; NoAlloc count; commit hash.

## Ranking (orchestrator, fixed before the attempts)

As phase1-plan.md §7 and pre-registration §5.4 fix it for Track O steps:

1. **Module-level median (decides):** the parse-only harness on the tuning corpus, `bench.mjs --corpus tuning --configs cs-o-aot-ws` (primary Track O configuration: NativeAOT, Workstation GC, parallel; 20 runs after 3 warmups), with each variant's driver built by `build-cs.mjs --tracks optimized`, the variants one after another on an otherwise idle machine. Median and 95 % percentile-bootstrap interval as the report computes them (`stats.mjs`, seed 20261021).
2. **Per-function measurement (reported):** `bench/NeverSharp.Benchmarks` `M5.ParseTuningCorpus` on Track O (environment variable `NeverSharpTrack=Optimized`; BenchmarkDotNet, NativeAOT, MemoryDiagnoser): wall-clock and bytes allocated.
3. **Rule:** lowest median (1.); if the 95 % intervals overlap, fewer bytes allocated (2.); then fewer lines of the storage layer. O6a's own layout is measured alongside as the reference; a variant that is not better than O6a by this rule is not merged.
