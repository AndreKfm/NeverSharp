# C9c: expression and flow typing — brief

Module C9c of Phase 3 ([phase3-plan.md](../phase3-plan.md)). Track O only. Runs alone. Written by the orchestrator. Standing rules of [README.md](README.md); run only the tests and gates your change affects.

C9 is split into sequential modules, one agent at a time. After C9b the author swapped the order of the remaining ones (general typing affects about 3,070 TS/TSX units, JavaScript 777): C9a (syntactic diagnostics of JavaScript files, done), C9b JSX (done), **C9c general expression and flow typing of TS/TSX** (this brief), C9d JavaScript and JSDoc typing, C9e decorators, lib suggestions and the rest until Gate C passes. Older texts (the C9b brief and its progress section, `TODO(C9c)`/`TODO(C9d)` markers in the code) use the old numbering: their C9c is now C9d, their C9d is now C9c (expression typing, meta properties) or C9e (decorators, lib suggestions). Renumber the markers you touch; at the end `git grep "TODO(C9"` should name only C9d and C9e.

## Starting point

`main` after C9b (d262919) — see the C9b section of [phase1-progress.md](../phase1-progress.md): Gate C 46,330 of 50,177 identical (diagnostics 49,725, types 46,614, symbols 49,510), 1 crashed (027977, load-only timeout). Differing units by kind: TS 2,276 of 36,930 (types 2,062, diagnostics 366, symbols 123), TSX 794 of 11,037, JS 771, JSX 6.

The orchestrator's grouping by first differing line of the types section (TS units; TSX in brackets, from the C9b report):

- optional call chains without `| undefined`: about 650 TS units (`x?.f()` typed `void` instead of `void | undefined`, likewise `boolean`, `string`, `number`, `Promise<void>`, …) [365 TSX]. `getReturnTypeOfSignature`/`getTypeOfCallExpression` do not apply the optional-chain markers (`isInnerCallChain`/`isOuterCallChain`, `addOptionalTypeMarker`, `getOptionalType`, `propagateOptionalTypeMarker`); check optional element and property access chains along the way;
- late-bound computed property names: object literals with `[Key]: …` typed as index signatures (`{ [x: number]: string; }` instead of `{ [Key]: string; }`): 230 units first differing at an object literal, 40 at an assignment, more inside call results (`{ [m]: number; }[]`), `[Symbol.dispose]` (14) [83 TSX, enum members as names]; also negative numeric names printed `[-NaN]` instead of `[-1]` (11). Concerns `checkObjectLiteral` with `isTypeUsableAsPropertyName`/`getPropertyNameFromType`, late-bound members (`getMembersOfSymbol` late-bound part, `lateBindMember`) and the node builder's printing of late-bound names;
- `import.meta`: 232 units typed `any` (`ImportMeta`) [8 TSX]: `checkMetaProperty`/`checkImportMetaProperty`, `getGlobalImportMetaType`, `new.target` on the same path;
- default type arguments printed: `Iterable<T, any, any>` instead of `Iterable<T>`, `AsyncIterable<…>` likewise (about 25) [17 TSX]: the node builder's omission of type arguments equal to their defaults (`typeArgumentsToTypeNodes`/`mapToTypeNodes` with `getEffectiveTypeArgumentsOfTypeReference`-style trimming as in checker.ts `typeReferenceToTypeNode`);
- anonymous class types (mixins): `typeof (Anonymous class)` instead of `{ new (...args: any[]): (Anonymous class); prototype: …; } & TBase` (e.g. 000244 `anonClassDeclarationEmitIsAnon.ts`, 000228): the node builder's `createAnonymousTypeNode`/`shouldWriteTypeOfFunctionSymbol` decision for class expressions (and the resulting extra 2322 in 000228);
- expando assignments on functions in TS files: `() => null as any` typed `() => any` instead of `{ (): any; defaultProps: …; }` (33 arrow functions, 5 function expressions, e.g. 003207 `expandoFunctionContextualTypes.ts`): the TS-file part of `getTypeOfFuncClassEnumModule` with expando members (`isExpandoDeclaration` symbols bound by the binder) and their contextual types; JavaScript-only assignment declarations (`this.x =`, `module.exports`, prototype assignments) stay for C9d;
- smaller groups: property accesses typed `any` (43, e.g. 000838), `await` results (23, e.g. 010029 `Atomics.waitAsync`), element accesses on tuples (14, `a[0] : 1` vs `1 | undefined`, 000314), parameters with binding-pattern initializers printed with the initializer instead of `?` (29, 009016), `as const` arrays not readonly [27 TSX], `1e999` typed `NaN` instead of `Infinity` (6, 003507).

Group sizes overlap; a unit is counted under its first difference only. Use `<session scratch>/C9c/` for your own grouping scripts; the dumps are `artifacts/equivalence/dumps/ts-checker-types` (oracle) and `artifacts/dumps/cs-optimized-checker-types` (C#).

## Goal

Port the general expression, flow and printing parts of checker.ts (structure as in checker.go) that decide the TS and TSX units above, in the order of the list. Diagnostics that change because the types change belong here; fix further diagnostic differences only where they come from typing on these paths. Not in C9c: decorators (1240, 1241 and relatives), lib suggestions (2550 instead of 2339 and relatives), the `.mts`/`.cts` syntax errors (7059) — C9e; JavaScript and JSDoc typing and the JS/JSX units — C9d. If a difference is a parser or binder bug, do not change parser or binder; report it.

Order of work if time runs short: optional chains, late-bound names, `import.meta`, default type arguments, anonymous class types, TS expando assignments, then the smaller groups. Commit working increments.

## Gate

Take a full baseline first and keep its identical-unit list. While iterating: a units file with the TS and TSX units that differ at the baseline (from `artifacts/equivalence/manifest.json` and the baseline dumps) run through the C# checker dump and `harness/astdump/compare-checker.mjs --units` directly (the commands `gate.mjs --phase checker` runs), plus `gate.mjs --track optimized --phase checker --sample 2000` now and then to catch regressions; the full corpus at the end. Target: most units of the groups above identical, more identical units than at the start in every section, 0 crashed (027977 excepted if it times out only under gate load), and every unit that was identical at the start and becomes different listed with the module it waits for (or fixed). Limit: 30 gate runs (each dump-and-compare counts as one). Work economically: no broad exploration, read what the port needs.

## Working

One attempt: Opus 5.5, medium effort, no sub-agents. Work directly on `main` in the main checkout (no worktree, no branch); scratch `<session scratch>/C9c/`. C# with the file tools only (also renames). Do not download or install anything. Commit with `git commit -s` (noreply identity as configured); do not push. Before the final report, stop every background task you started. At the end add a C9c section to [phase1-progress.md](../phase1-progress.md) in the style of the C9b section.

## Report

Brief: gate runs and the last full summary (identical per section, crashed); TS and TSX units identical before and after; units per group above before and after; regressions with their waiting module; what was ported and what is left (the main causes of the TS/TSX units that still differ, with the module they belong to); commit hash(es).
