# O5a: text as spans, identifier interning — brief

Step O5 of Track O ([track-o-steps.md](../track-o-steps.md)). Pre-registration §5.4 fixes the design: "Identifier and literal text is referenced as `(pos, length)` into the source text; values that differ from the source text (escapes, normalized numbers) go to a character arena. Identifiers are interned in a table keyed by span." phase1-plan.md §7 lists the variants of the lookup step (O5: "switch on length and first character; perfect hash; dictionary with span alternate lookup"). As with O6 and O7 the step is split: **O5a** converts text values once to spans plus a character arena, with one simple interning table (a dictionary with span alternate lookup); **O5b** may then compare the other lookup variants behind that seam. O5a has no variants; it must keep Gate 1 and is measured for the record. Written by the orchestrator before the attempt started. The standing rules of [README.md](README.md) apply (Track O follows pre-registration §5.4).

## Why

In the Track F profile ([track-o-profile.md](../track-o-profile.md)) strings were the largest single type (21 % of the bytes): every identifier, string literal, template part and numeric literal value is a substring, and the scanner builds token values as strings. After O6 and O7 Track O stores nodes and lists in arenas; texts are still `string` objects in the node store's object slots, and they and the remaining closures and boxing are what the GC still traces.

## Goal of O5a

- A text value is a handle (e.g. `readonly struct TextRef`): either a span `(pos, length)` of the source text, or an entry in a per-parse **character arena** for values that differ from the source (escape sequences, normalized numeric literals, cooked template text, identifier escapes, `escapeLeadingUnderscores`).
- The scanner produces token values as text handles (no `string` for a token value unless TypeScript's code needs one that cannot be a span); comparisons against constant strings (`text === "constructor"`, keyword checks, pragma names, …) use span comparisons.
- Identifiers (and private identifiers) are interned in a per-parse table keyed by the span's characters: equal texts get the same id, as TypeScript's `identifiers` map (`sourceFile.identifiers`) and `internIdentifier` intend. O5a's table: a dictionary with span alternate lookup (`Dictionary<string, …>.GetAlternateLookup<ReadOnlySpan<char>>` or an equivalent).
- Node fields holding text become text handles in int slots; object slots for text disappear.
- The places that still need a `string` (diagnostic arguments, `SourceFile.Identifiers` if a tool reads it, file names) materialize it on demand.
- Truncation for `lookAhead`/`tryParse` covers the character arena and the interning table's additions.
- Out of scope unless they fall out naturally: the per-call closures (`Core.Some(..., a0 => ...)`, one parser closure) and the boxing of `Node` that the O7a report found; if you remove them, list each change.

## What may change

- `src/NeverSharp.Optimized/` — scanner, parser, factory, core and arena code as needed for the text representation, and the emitter `tools/NeverSharp.Generate/emit-optimized.mjs`.
- The Track O branches of the tools only if the API change requires it (`tools/NeverSharp.Dump/TokenDump.cs` reads `GetTokenValue()`, which must still return TypeScript's token value as a `string` for the G1a dump; `AstDump.cs` under `#if TRACK_OPTIMIZED`). The orchestrator reviews tool diffs line by line.
- Nothing else: not Track F, tests, harness, docs, other tools.

## Gate

```
<main>/.tools/node-24.21.0/node.exe harness/port/gate.mjs --track optimized --main D:/Private/Source/Andre/projects/NeverSharp
```

All Track O gates (Gate 2 with NoAlloc, M1/M2 unit tests — the scanner's helpers must keep their results —, G1a, G1 ParseNone, Gate 1), 50,177 of 50,177 units; Track F's gate (`--module M5`) must still pass. Limit: 100 gate runs.

## Working

- One attempt: Opus 5.5, high effort. Sub-agents of the same model allowed, at most 4, for mechanical conversion. Scratch folder `<session scratch>/O5a/`. C# with the file tools. Do not copy the benchmark project into other folders under the main checkout. Commit with `git commit -s`; do not push.
- Measure for yourself with the parse driver on the tuning corpus and `M5.ParseTuningCorpus` (`NeverSharpTrack=Optimized`), as in O7a.

## Report

Model (and sub-agents); last gate JSON line and gate runs; the text handle, the character arena, the interning table, truncation; which places still materialize strings and why; tool diffs; your own timing and bytes against main (O7a: tuning corpus cs-o-aot-ws 845 ms, 679 MB; M5 1.206 s, 417.0 MB); NoAlloc count; what O5b could gain; commit hash.

## Measurement (orchestrator)

As for O7a: the parse-only harness on the tuning corpus (`cs-o-aot-ws`, 20 runs, rig-compliant) against the current main, and `M5.ParseTuningCorpus`. Merged if every gate passes and it is not slower than main by the 95 % interval.
