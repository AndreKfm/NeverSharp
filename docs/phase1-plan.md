# Phase 1 plan: scanner and parser, two tracks

Status: 2026-10-21. Items marked **[DECISION, CONFIRMED]** were proposed by the assistant and confirmed by the author on 2026-10-21; **[OPEN]** needs the author's input.
Depends on: [preregistration.md](preregistration.md) (Track F / Track O, Gate 1, Gate 2, budget), [ast-dump-format.md](ast-dump-format.md), [benchmark-harness.md](benchmark-harness.md).

## 1. What Phase 1 delivers

For **each track**, a C# scanner and parser ported from tsc 6.0.3 that

1. produce a canonical dump byte-identical to tsc 6.0.3 on all 50,177 units, including the JSDoc section (Gate 1),
2. build with zero diagnostics under the track's Gate 2,
3. run in the parse-only harness through a driver that follows the driver contract (`NEVERSHARP_PRIORITY`, JSON line with file count, node count, priority, GC metrics),

and one protocol-compliant measurement per track against typescript-go, under the decision rule of the pre-registration.

Budget: 18 weeks from the tag date at about 7 h/week, common to both tracks (kill criterion 2; raised from 12 weeks after the estimate in section 9).

## 2. The slice: what the parser pulls in

Computed, not estimated: [harness/analysis/slice.mjs](../harness/analysis/slice.mjs) builds a TypeScript program over `src/compiler/**` of v6.0.3 and follows every reference, resolved by the type checker, transitively from `createSourceFile` and `forEachChild`. A unit is a top-level or namespace-level declaration; `createNodeFactory`'s inner functions are units of their own, reached through the `NodeFactory` member a call uses. The sources import through a namespace barrel, so import lists alone would not show where a name lives.

Result: 1,991 units; 20,746 lines of executable code, 2,165 lines of enums, 5,652 lines of type declarations; 152 diagnostic messages referenced.

| File | Executable lines (units) | Enums | Types | Phase 1 |
|---|---:|---:|---:|---|
| parser.ts | 8,971 (521) | 61 | 27 | yes, except `createSyntaxCursor` (100 lines, incremental parsing) |
| factory/nodeFactory.ts | 3,894 (242; 229 of 488 inner functions) | 11 | — | the `create*` functions the parser calls |
| scanner.ts | 3,708 (64) | 18 | 83 | yes; `createScanner` alone is 3,011 lines incl. the regular-expression validator |
| types.ts | 79 | 1,860 | 5,304 | enums (SyntaxKind, NodeFlags, TokenFlags, …) and node shapes |
| utilities.ts | 1,314 (124) | 195 | 73 | yes |
| core.ts, utilitiesPublic.ts, path.ts, factory/nodeTests.ts, factory/utilities.ts, factory/emitNode.ts, factory/utilitiesPublic.ts | 1,514 | 6 | 18 | yes (small helpers) |
| commandLineParser.ts | 157 | — | 14 | `convertToJson` (JSON diagnostics, `parser.ts:1616`) and the target-name lookup |
| debug.ts | 179 | — | 1 | replaced by `Debug.Assert` and plain exceptions; the formatting helpers are not ported |
| factory/parenthesizerRules.ts, factory/nodeConverters.ts | 802 | — | — | **no**: the parser's factory is created with `NoParenthesizerRules | NoNodeConverters`; only the 43 lines of null implementations matter |
| performance.ts, performanceCore.ts, tracing.ts | 128 | 9 | 19 | **no**: instrumentation, no-ops for parsing |
| moduleNameResolver.ts | — | — | 98 | type references only |

Net executable code to port: about **19,600 lines of TypeScript** (20,746 minus parenthesizer rules and node converters without their null implementations 759, instrumentation 128, `createSyntaxCursor` 100, debug formatting 179), plus about 2,200 lines of enums and the node shapes. The largest single units: `createScanner` 3,011, `parseJSDocCommentWorker` 1,039, `forEachChildTable` 632.

Diagnostic messages: 152 of the 2,130 messages in `diagnosticMessages.json` are referenced. The C# message table is generated from the JSON (all messages, so later phases need no regeneration), as TypeScript's build and typescript-go (`internal/diagnostics/diagnostics_generated.go`) both do.

## 3. Generated code

typescript-go generates its node types, factory, child visitor and kind enums from a schema (`_scripts/ast.json`, `_scripts/generate-go-ast.ts`), and its diagnostic table from `diagnosticMessages.json`. Under port rule 3 that is the counterpart for doing the same in C#.

**[DECISION, CONFIRMED]** One generator, `tools/NeverSharp.Generate`, produces for each track:

- the diagnostic message table (from `diagnosticMessages.json` at the pinned commit);
- `SyntaxKind`, `NodeFlags`, `TokenFlags`, `ModifierFlags` and the other enums, with TypeScript's names and numeric values;
- node types, factory `create*` methods and `ForEachChild`, from a **TypeScript 6.0.3 node schema** (`tools/NeverSharp.Generate/ast-ts6.json`). It is seeded from typescript-go's `ast.json` (Apache-2.0, attributed) and corrected to TypeScript 6.0.3's node shapes and child order (e.g. `JSDocUnknownType`, `OmittedExpression` in array binding patterns, no `JSTypeAliasDeclaration`). The child order must equal `forEachChildTable` in `parser.ts`; the generator's output is checked against it by the dump gate.

Track F and Track O get different emitters from the same schema: node classes (Track F), struct records in arenas (Track O).

## 4. Module boundaries and order (both tracks)

| # | Module | Content | Gate before the next module |
|---|---|---|---|
| M0 | Infrastructure | generator (section 3); C# dump writer implementing [ast-dump-format.md](ast-dump-format.md) incl. the JSDoc section; parse-only driver (driver contract); harness integration (`dump-cs`, `cs-*` configurations); Gate 2 rulesets for both tracks | generator output compiles; driver passes `bench.mjs` validation on the tuning corpus |
| M1 | Core | character codes, text ranges, the `path.ts` subset (`getNormalizedAbsolutePath`, extension checks), `Debug` | unit tests |
| M2 | Scanner | `createScanner`: trivia and comments, identifiers and Unicode tables, numeric/string/template literals, JSX text and identifiers, JSDoc tokens, regular expressions incl. the validator, keyword table, comment directives, error reporting | **G1a: token-stream dump identical to tsc 6.0.3** |
| M3 | AST | node types, factory `create*`, `ForEachChild` (generated), node lists | compiles; covered by G1 |
| M4 | Parser core | source files, statements, declarations, expressions, types, modifiers, decorators, JSX, list parsing and error recovery, speculative parsing (`lookAhead`, `tryParse`), context flags, top-level await reparse, pragmas, JSON (`parseJsonText`, `convertToJson`) | G1 on all units, JSDoc section excluded |
| M5 | JSDoc parser | `parseJSDocCommentWorker` and JSDoc type parsing, both `JSDocParsingMode.ParseAll` and `ParseForTypeErrors` | **G1 complete (Gate 1)** |
| M6 | Measurement | protocol-compliant run with `tsgo7-parse` in the same session | — |

**[DECISION, CONFIRMED] Intermediate gate G1a for the scanner.** tsc exposes its scanner (`ts.createScanner`). A token-stream dump (kind, token start, end, token flags, token value for literals and identifiers, scanner diagnostics) of every unit, scanned in normal mode, is compared byte for byte before the parser starts. It finds scanner errors where they are cheap to find; contextual rescans (`reScanGreaterToken`, template and regex rescans, JSX and JSDoc modes) are covered by G1 later.

## 5. JSDoc scope

In scope (pre-registration 5.3), completely:

- JSDoc scanning (`scanJsDocToken`, `scanJsDocCommentTextToken`) and the JSDoc parser in both modes. Benchmarks use `ParseForTypeErrors` (what tsc uses); the equivalence corpus uses `ParseAll`.
- **Dump format, JSDoc section [DECISION, CONFIRMED]:** after a node's line, each comment in its `jsDoc` array is written as a subtree whose lines carry the prefix `@` (kind, pos, end, flags; children by the JSDoc nodes' own visitor), before the node's regular children. JSDoc diagnostics remain in `!jsdocdiag`. The tsc 6.0.3 dumper gains this as an option; typescript-go is not compared on it.
- **The known v6.0.3 defect in `scanJsDocToken` [DECISION, CONFIRMED]:** both tracks port v6.0.3 as it is, including the operator-precedence defect that makes a JSDoc identifier ending in `-` at the end of the scan range loop forever ([references.md](references.md), section 2). A faithful port reproduces its source; the equivalence corpus does not trigger it. The C# dumper runs units with a timeout, like the tsc dumper.

Consequence: about 1,300 more lines to port (JSDoc parser and scanning) than a JSDoc-free parser, and the equivalence gate grows by the JSDoc section.

## 6. Track F: faithful port

- **Shape:** node classes with the source's fields; functions and data; the factory and the parser's closure state as in the source (rule 1). Strings are UTF-16 (rule 2).
- **Permitted optimizations (rule 3, with counterpart):** parser and scanner pooling (`getParser`/`putParser`, `internal/parser/parser.go`), node allocation from per-type arenas where typescript-go's schema marks `"arena": true` (`_scripts/ast.json`), node-list slice arenas (`nodeSliceArena`), parallel file parsing on a work-group counterpart (`internal/core/workgroup.go`). Each is cited where it is used.
- **Work per module:** ported by the models of the multi-model workflow from the same inputs; Gate 1 (or G1a), then Gate 2; survivors ranked by module-level speed on the tuning corpus and bytes allocated. **[OPEN]** models and attempts per model.
- **Measured at every step,** for the record, not for selection beyond the ranking above: each merged module gets a measurement record on the tuning corpus.

## 7. Track O: optimized port

**Starting point [DECISION, CONFIRMED]:** each Track O module starts from the Track F module once that module has passed its gate, and is then transformed. Correctness is inherited and re-verified by the same gate after every change; Track O does not re-derive the port from scratch.

**Fixed design (pre-registration 5.4):**

- **Arena tree.** Nodes are struct records in arenas, referenced by `int` index; node lists are `(start, count)` ranges in an index arena; parent links are indices. Speculative parsing (`lookAhead`, `tryParse`) discards nodes by truncating the arenas to a mark, which keeps the source's semantics.
- **Text.** Identifier and literal text is referenced as `(pos, length)` into the source text; values that differ from the source text (escapes, normalized numbers) go to a character arena. Identifiers are interned in a table keyed by span.
- **NoAlloc.** `[NoAlloc(Constraints.NoLock | Constraints.NoBlock)]` on all ported code; the only intended allocations are arena growth and the per-file result. Every exception is `[NoAllocUnchecked(reason)]` or `Allow` + `Reason` and is counted in each result.
- **SIMD** only behind `IsSupported` / `IsHardwareAccelerated` with a scalar fallback; NativeAOT with the default instruction-set baseline.

**Steps with variants (at most 3 each).** Candidate list; the mandatory techniques (SIMD, stack allocation, allocation-free layout) each appear in at least one step:

| Step | What | Candidate variants |
|---|---|---|
| O1 | File reading and decoding | `File.ReadAllText`; UTF-8 → UTF-16 with `Utf8.ToUtf16` (SIMD) into a pooled buffer; memory-mapped file + transcoding |
| O2 | Character classification | ASCII lookup table; branch chain as in the source; `Vector128` range compares for identifier runs |
| O3 | Trivia and comment skipping | scalar loop; `SearchValues<char>` / `IndexOfAnyExcept` (SIMD); vectorized line-break scan |
| O4 | String, template and numeric literals | scalar; `IndexOfAny(SearchValues)` for quote, backslash, line break; `stackalloc` buffers for escape and number decoding |
| O5 | Keywords and identifier interning | switch on length and first character; perfect hash; dictionary with span alternate lookup |
| O6 | Node arena layout | array of structs (one record per node); structure of arrays (kind, pos, end, flags, parent, payload index); header arrays + per-kind payload arenas |
| O7 | Node lists | ranges in one index arena; per-list pooled arrays; small-list inline storage |
| O8 | Parallel scheduling | `Parallel.ForEach`; thread-pool work items (work-group counterpart); partitioned queue, largest files first |

**Process per step:** build up to 3 variants → Gate 1 (or G1a) and Track O Gate 2 → measure survivors with BenchmarkDotNet (MemoryDiagnoser) per function and with the parse-only harness on the tuning corpus (`tests/cases`, 23,910 units) → select by the pre-registered rule (lowest module-level median; if the 95 % intervals overlap, fewer bytes allocated, then fewer lines) → log all variants and numbers → continue with the winner. The five benchmark repositories are never used for a choice.

**[DECISION, CONFIRMED] Order of Track O steps by profile:** O1–O8 are candidates, not a fixed sequence. The order is set by a profile of the finished Track F parser on the tuning corpus: steps whose code takes the largest share of time go first. If the budget runs short, the remaining steps are skipped and reported as not evaluated.

## 8. C# layout

```
src/NeverSharp.Faithful/          Track F: core, scanner, AST (generated), parser
src/NeverSharp.Optimized/         Track O: same modules, arena representation
tools/NeverSharp.Generate/        schema, generator, emitters for both tracks
tools/NeverSharp.Dump/            canonical dump writer (both tracks), token-stream dump (G1a)
tools/NeverSharp.ParseDriver/     parse-only driver, one executable per track and runtime configuration
bench/NeverSharp.Benchmarks/      BenchmarkDotNet micro benchmarks (Track O variants, supplementary table)
analyzers/NeverSharp.NoAlloc/     NoAlloc analyzer (Track O Gate 2)
```

The tracks share no runtime code. They share only generated data (diagnostic table, enum values) and the tools.

## 9. Effort and the step most likely to kill it

These are estimates of the author's time with AI models doing the porting; the line counts of section 2 are measured, the hours are not. Uncertainty is large, about ±50 %.

| Block | Hours |
|---|---:|
| M0 infrastructure: generator and TS 6.0.3 schema (10), dump writer, token dump and driver (5), JSDoc dump section on the tsc side (3), Gate 2 rulesets (2) | 20 |
| Track F: core (2), scanner incl. regex validator and JSDoc scanning (8), AST review (2), parser core (14), JSDoc parser (5), JSON and pragmas (2), driving the dump to zero mismatches (15) | 48 |
| Track O: arena emitter and design (8), scanner steps O1–O5 with variants (15), parser on arenas with O6–O7 (15), O8 (3), NoAlloc compliance (6), dump to zero mismatches (8) | 55 |
| **Total** | **≈ 123** |
| Budget: 18 weeks × 7 h (raised from 12 weeks, see below) | **126** |

**With the original 12 weeks (84 h) the plan did not fit:** infrastructure plus Track F (≈ 68 h) fitted, Track O on top did not. Options put to the author:

- (a) Track F first; Track O gets what remains (about 16 h) and runs only the profile-ranked first steps, the rest reported as not evaluated. Kill criterion 2 then most likely ends Track O with a gap analysis.
- (b) Raise the common budget to about 18 weeks.
- (c) Cut Track O to fewer steps up front (e.g. O1, O3, O6: one SIMD step, one decoding step, the arena layout) and keep 12 weeks.

**Decided by the author 2026-10-21: (b), 18 weeks.** The estimate of ≈ 123 h uses almost all of the 126 h, so there is no reserve for the uncertainty stated above; if the budget runs short, Track O's remaining steps are skipped and reported as not evaluated (section 7), before Track F is put at risk.

**Most likely to kill it:**

- **Track F: the long tail of the parser's error recovery.** Gate 1 allows zero mismatches over 50,177 units. The units that will differ last are erroneous inputs: `ThisNodeHasError` placement, positions of missing nodes, diagnostic spans. The oracle comparison shows how subtle these are: 73 of the 85 units on which tsc and typescript-go disagree are exactly such cases. The 15 hours above are the least certain number in the table.
- **Track O: the arena tree under speculative parsing.** Every `create*` call and every `lookAhead`/`tryParse` path changes representation at once, and node positions, flags and error recovery must stay byte-identical. A rollback bug shows up as a wrong tree in a handful of units deep into the corpus.

## 10. Open before the pre-registration tag

- [x] Budget: 18 weeks (section 9).
- [ ] **[OPEN]** Models and attempts per model (pre-registration 5.2).
- [x] Author confirmed the **[DECISION]** items of this plan (2026-10-21): one generator with a TS 6.0.3 schema (3), token-stream gate G1a (4), JSDoc dump section format and reproducing the v6.0.3 JSDoc defect (5), Track O starting from Track F modules and profile-ordered steps (7).
