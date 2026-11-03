# Phase 4: emitter (Track O only) — plan

Decided by the author on 2026-11-03.
- **Scope:** Phase 4 ports the emitter of TypeScript 6.0.3: `.js` and `.d.ts` output, for Track O only.
- **Options:** restricted to **TypeScript 6.0.3's default options**:
  - target ES2025, so the module kind is ES2022;
  - no source maps;
  - comments kept;
  - plus `declaration: true` for the `.d.ts` output.
- **Rules:** the standing rules of Phases 1–3 apply. Track F is not extended; run only the tests and gates a change affects.
- **Amendment:** draft [preregistration.md](preregistration.md) §19 (`prereg/v11`). It is accepted and tagged after Gate D passes and before any emit measurement, as §17 was for the checker.

## What the default options need

With the default options, tsc 6.0.3's `getScriptTransformers` (`transformer.ts`:127 ff.) runs these, in this order:

| Transformer | File | Lines | Note |
|---|---|---:|---|
| TypeScript | `transformers/ts.ts` | 2,750 | type erasure, import elision, enums, namespaces, parameter properties |
| ESNext | `transformers/esnext.ts` | 800 | `using` / `await using` (ES2025 < ESNext) |
| ES decorators | `transformers/esDecorators.ts` | 2,533 | standard decorators (ES2025 < ESNext, no `experimentalDecorators`) |
| class fields | `transformers/classFields.ts` | 3,360 | with `useDefineForClassFields` true, mostly auto-accessors, private names in decorators, `static` blocks with decorators |
| ES module | `transformers/module/esnextAnd2015.ts` | 439 | module kind ES2022 |
| CommonJS module | `transformers/module/module.ts` | 2,504 | only for files whose implied format is CommonJS (`.cts`, `.cjs`, 84 units). Added by the author on 2026-11-03, ported in D3. |
| declarations | `transformers/declarations.ts`, `declarations/diagnostics.ts` | 2,789 | `.d.ts` output and declaration emit diagnostics |

Shared parts:
- `transformers/utilities.ts`, `namedEvaluation.ts`, `classThis.ts`, `destructuring.ts`: about 2,150 lines;
- `transformer.ts`: 696;
- `factory/emitHelpers.ts`: 1,491;
- the emitter's comment emission and `emitFiles` (`emitter.ts`).

**Not needed** under the default options:
- JSX transform: `jsx` is unset, so JSX is preserved;
- legacy decorators and decorator metadata;
- every downlevel transformer below ES2021;
- generators;
- AMD, UMD and System;
- source maps.

**Already in Track O since Phase 3:**
- **the printer** (P1): `createPrinter` with every `emit*` function. Its gate is byte-identical on all 50,177 units, but only with `removeComments: true`.
- **the node factory** (`Factory/`, about 6,100 lines), with emit nodes;
- **the node builder** (C2), which declaration emit reuses for inferred types.

New code to port: about 30,000 lines of TypeScript, about half the size of the checker.

## Gate D (emit equivalence)

- **Unit:** every equivalence unit as a one-file program, as in Gate C.
  - Options: Gate C's options (`{}` with `noResolve: true`, plus `allowJs: true` for JavaScript and JSX units), plus `declaration: true`.
  - A fixed `newLine: lf` (TypeScript 6.0.3's default).
  - Units are emitted even when they have errors, as tsc does by default (`noEmitOnError` false).
- **Oracle:** `harness/astdump/dump-ts-emit.mjs` runs tsc 6.0.3's `program.emit()` with an in-memory `writeFile` and writes a canonical dump:
  1. every output file in emit order, with its name and its text;
  2. the emit diagnostics `program.emit` returns (declaration diagnostics) with start, length, code, category and the flattened message chain;
  3. `emitSkipped`.
- **C# side:** the same dump from Track O's `Program.Emit` (`tools/NeverSharp.Dump.Optimized --emit`).
- **Gate D:**
  - byte-identical dumps for all units;
  - reported as identical, differing or crashing units per section (`.js`, `.d.ts`, diagnostics), so that progress is visible while modules land;
  - `gate.mjs --phase emit`.
  - Gate C and the printer gate run when checker or printer code changes.
- **Coverage report:** the oracle also records which transformers changed each unit, so that the share of units exercising each module is known before porting.

## Modules (Track O; order follows dependencies; one attempt each)

| Module | Content |
|---|---|
| D0 | Oracle (emit dump, transformer coverage), compare, `gate.mjs --phase emit`. Track O emit plumbing: `Program.Emit`, output names, the emit host, `emitFiles` without transformers (emit of the parse tree). Comment emission in the printer (leading and trailing comments, detached comments, pinned comments, `removeComments: false`). The C# dump writer. |
| D1 | Transformation context and pipeline (`transformer.ts`): lexical environment, hoisting, `onEmitNode` / `onSubstituteNode` chains, emit helpers (`factory/emitHelpers.ts`). The emit resolver from the checker (`createResolver` in `checker.ts`). |
| D2 | TypeScript transformer (`ts.ts`): type erasure, import and export elision, enums (`const enum` inlining through the resolver), namespaces, parameter properties, `export =`. |
| D3 | ES module transformer for module kind ES2022, and `esnext.ts` (`using` declarations). |
| D4 | Class fields and ES decorators (`classFields.ts`, `esDecorators.ts`, `namedEvaluation.ts`, `classThis.ts`, the parts of `destructuring.ts` they need). |
| D5 | Declaration emit (`declarations.ts`, `declarations/diagnostics.ts`): visibility, inferred types through the node builder, declaration emit diagnostics. |
| D6 | The rest until Gate D passes. Emit drivers and measurement (below). |

Optimization steps after Gate D, as in Phases 1–3: profile first.

## Measurement (draft §19, prereg/v11)

**Endpoint:**
- Each benchmark repository's file list as one program with Gate D's options: parse, bind, check (semantic and global diagnostics as in §17), and emit every file to memory.
- Configurations `cs-o-aot-ws-emit` vs `tsgo7-emit`. typescript-go 7.0.2 runs `Program.Emit` with an in-memory `WriteFile` after the same diagnostics, through its own checker pool of 4.
- G, interval and decision rule as in §8.
- Profile `emit`, in the style of §16: single-threaded variants as the variance gate's binding rows; Server GC and tsrs 0.18.0 (`Program::emit`) secondary.

**Equivalence of the repository programs:** the C# output of each repository program is compared once with tsc 6.0.3's (identical or not, differences by cause), as `check-equivalence.mjs` does for diagnostics. The total output bytes of every side are reported with every comparison.

**Disclosed differences:**
- typescript-go and tsrs emit as TypeScript 7 does, which differs from 6.0.3 in places;
- the repositories' dependencies are not installed, which affects import elision and declaration emit identically on all sides.

Windows only.

## Risks

- **Comments:** comment emission is not covered by the printer gate (it ran with `removeComments: true`). It touches every `emit*` function through `emitLeadingComments` / `emitTrailingComments` and detached-comment ranges, so D0 must bring it up across the whole corpus before transformers land.
- **The emit resolver** reads checker state: import elision, constant values of enums, `isReferencedAliasDeclaration`, declaration visibility. Its results must match tsc's in the same order, and with several checkers each file's resolver must come from its own checker, as in typescript-go.
- **Declaration emit** depends on the node builder in modes Gate C's printing never used: `declaration` flags, `reuseExistingTypeNodes`, accessibility tracking. Expect differences there late in the phase.
- **Synthesized nodes:** the transformers create many synthesized nodes with emit flags and original-node links. Track O's arena layout for synthesized nodes was built for type printing and may need extending.
- **Size:** about half the checker. The order of modules makes Gate D's progress visible early (D0 and D2 cover most units).
