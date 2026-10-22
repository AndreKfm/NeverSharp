# O7a: node lists as handles into a list store — brief

Step O7 of Track O ([track-o-steps.md](../track-o-steps.md); candidates in [phase1-plan.md](../phase1-plan.md) §7: "ranges in one index arena; per-list pooled arrays; small-list inline storage"). As with O6, the step is split: **O7a** converts Track O's node lists once from objects to handles behind a list store, with the first candidate layout (ranges in one index arena); **O7b** may then compare up to 2 further layouts behind the same seam, if the O7a measurement leaves room for it. O7a has no variants; it must keep Gate 1 and is measured for the record. Written by the orchestrator before the attempt started. The standing rules of [README.md](README.md) apply (Track O follows pre-registration §5.4).

## Why

After O6b (compact node records), GC pause is still about 60 % of the parallel wall time; the variant reports trace it to objects outside the node store, first of all the node lists: every list is a `NodeArray : List<Node>` object with its own backing array, stored in an object slot of its node (`src/NeverSharp.Optimized/Arena/NodeArray.cs`, 279 mentions in `Parser/` and `Factory/`). Pre-registration §5.4's fixed design says: "node lists are `(start, count)` ranges in an index arena".

## Goal of O7a

- A node list is a handle (e.g. `readonly struct NodeList`, one `int`; a distinguished value for "undefined" and TypeScript's missing list) into a **list store** owned by the parse (inside `NodeStore` or next to it, one per parse).
- The O7a layout: the elements of all finished lists in one growable index arena (`Node` handles), each list a header with start, count, pos, end, `hasTrailingComma`, `transformFlags` and the missing-list mark. Lists under construction (`parseList`, `parseDelimitedList`, the factory's list building) are built in a reusable scratch stack and copied into the arena when TypeScript's code calls `createNodeArray`; nested list construction must work (a stack of frames).
- Truncation for `lookAhead`/`tryParse` covers the list store as well (the mark includes its counts).
- Lists are stored in the node records as handles in int slots, no longer as objects in object slots.
- Everything else stays: Gate 1 byte-identical, thread safety (no mutable static state except pools), the NodeStore API for nodes.

## What may change

- `src/NeverSharp.Optimized/` — the list representation and every use of it in `Parser/`, `Factory/`, `Core/` (helpers taking lists), `Arena/`, and the emitter `tools/NeverSharp.Generate/emit-optimized.mjs` (list-typed fields become list handles; `ForEachChild` visits list elements through the store).
- The Track O branches of the tools as in O6a: only the traversal in `tools/NeverSharp.Dump/AstDump.cs` under `#if TRACK_OPTIMIZED`, if the list API change requires it; the orchestrator reviews the diff line by line.
- Nothing else: not Track F, not tests, harness, docs, other tools.
- `[NoAlloc]` on allocation-free list-store members is welcome; the benchmark project now knows the attribute types.

## Gate

```
<main>/.tools/node-24.21.0/node.exe harness/port/gate.mjs --track optimized --main D:/Private/Source/Andre/projects/NeverSharp
```

All Track O gates, 50,177 of 50,177 units in every comparison; Track F's gate (`--module M5`) must still pass. Limit: 100 gate runs (standing rule 2).

## Working

- One attempt: Opus 5.5, high effort. Sub-agents of the same model allowed, at most 4, for the mechanical conversion of call sites. Scratch folder `<session scratch>/O7a/`. C# with the file tools. Commit with `git commit -s`; do not push.
- Measure for yourself with the parse driver on the tuning corpus (`build-cs.mjs --tracks optimized --runtimes aot-ws`, then `artifacts/bin/cs/optimized-aot-ws/parse-cs-optimized-aot-ws.exe <main>/artifacts/bench/filelists/tuning.txt`) and with `M5.ParseTuningCorpus` (environment variable `NeverSharpTrack=Optimized`).

## Report

Model (and sub-agents); last gate JSON line and gate runs; the list API and layout (headers, arena, scratch frames, truncation); tool diffs; your own timing and bytes against the current main (O6b A: tuning corpus cs-o-aot-ws 1,093 ms, 695 MB; M5 1.456 s, 457.6 MB); NoAlloc count; what O7b could still gain; commit hash.

## Measurement (orchestrator)

For the record, as for O6a: the parse-only harness on the tuning corpus (`cs-o-aot-ws`, 20 runs, rig-compliant) against the current main, and `M5.ParseTuningCorpus`. O7a is merged if it passes every gate and is not slower than main by the 95 % interval.
