# C9b: JSX — brief

Module C9b of Phase 3 ([phase3-plan.md](../phase3-plan.md)). Track O only. Runs alone. Written by the orchestrator. Standing rules of [README.md](README.md); run only the tests and gates your change affects.

C9 (JavaScript and JSDoc typing, JSX, decorators, the rest until Gate C passes) is split by the orchestrator into sequential modules, one agent at a time: C9a (syntactic diagnostics of JavaScript files, done), **C9b JSX** (this brief), C9c JavaScript and JSDoc typing, C9d decorators, lib suggestions and the remaining expression and flow typing until Gate C passes.

## Starting point

`main` after C8 (f22cd49) — see the C8 section of [phase1-progress.md](../phase1-progress.md): Gate C 39,740 of 50,177 identical (diagnostics 49,220, types 40,050, symbols 49,459), 1 crashed (027977, load-only timeout). The orchestrator's breakdown of the last full run (differing units by kind): TSX 7,140 of 11,037 (types 7,099, diagnostics 519, symbols 71), JSX 7 of 15, TS 2,519, JS 771. In 6,720 TSX units the first difference is an identifier typed `any` where tsc has its type; in most of them it is the name of a JSX attribute (`icon : string` vs `icon : any`, e.g. 001564 `contextuallyTypedJsxChildren.tsx`); further: JSX elements and fragments typed `any` instead of `JSX.Element` (005005, 008024, 013289), contextual types in attributes (015907: `(k: "left" | "right") => void` vs `(k: any) => void`), `JsxNamespacedName` (005104). Diagnostics missing in TSX include 2741, 2322/2728 under JSX elaboration, 2786, 2604, 2745; extra 6133 (likely the React import not marked as used by JSX). The JSX stubs are marked `TODO(C9)` (`Checker.Jsx.cs`, `Checker.Contextual.cs`, `Checker.Calls.cs`, `Checker.RelationErrors.cs`, `Checker.Expressions.cs` and others; `git grep "TODO(C9"`).

## Goal

Port JSX checking from checker.ts (structure as in checker.go):

- JSX namespace and element types: `getJsxNamespaceAt`, `getJsxNamespaceContainerForImplicitImport`/`jsxImportSource` and the `jsx` modes (preserve, react, react-jsx/-jsxdev, react-native) as far as Gate C's one-file programs reach them, `getJsxElementTypeAt`, `getJsxElementClassTypeAt`, `getJsxElementPropertiesName`/`ElementAttributesProperty`, `getJsxElementChildrenPropertyName`/`ElementChildrenAttribute`, `IntrinsicElements`, `IntrinsicAttributes`/`IntrinsicClassAttributes`, `LibraryManagedAttributes`, `getJsxReferenceKind`, `getIntrinsicTagSymbol`, `JsxNamespacedName`;
- `checkJsxElement`, `checkJsxSelfClosingElement`, `checkJsxFragment`, `checkJsxOpeningLikeElementOrOpeningFragment` (the element type, tag name checks, `checkJsxPreconditions`, factory and fragment factory references incl. `markJsxAliasReferenced` so the React import counts as used), `checkJsxReturnAssignableToAppropriateBound`, `checkJsxExpression`, `checkJsxChildren`;
- attributes: `createJsxAttributesTypeFromAttributesProperty` (spreads, explicit `children` vs body children, `isKnownProperty` for excess properties), `checkJsxAttribute` and the type of attribute names (`getTypeOfNode` for the dump's identifier lines), `checkJsxAttributes`;
- contextual typing: `getContextualTypeForJsxAttribute`, `getContextualTypeForJsxExpression`, `getContextualTypeForChildJsxExpression`, `getContextualJsxElementAttributesType`, `getJsxPropsTypeFromCallSignature`/`getJsxPropsTypeFromClassElement`, replacing C1's approximations of parameter types in attribute functions;
- resolution: `resolveJsxOpeningLikeElement` (SFC and class component signatures, `getUninstantiatedJsxSignaturesOfType`, explicit type arguments on JSX tags, overload resolution and inference through `getEffectiveCallArguments` of JSX, `getJsxStatelessElementTypeAt`), the call diagnostics of JSX (2604, 2786, 2745, 2746, 2747, 7026 and relatives);
- elaboration: `elaborateJsxComponents` and the JSX parts of `elaborateError` (2741, 2322/2326/2559 pointing into attributes and children).

JavaScript/JSDoc (C9c) and decorators (C9d) stay stubbed (`TODO(C9)`, may be renamed `TODO(C9c)`/`TODO(C9d)` where you touch them). If a TSX difference turns out to belong to general expression typing (optional chains, `import.meta`, default type arguments in printed types — C9d) fix it only if it is small and on the JSX path; otherwise leave it and name it in the report. Do not change parser or binder; if a JSX difference is a parser or binder bug, report it.

Order of work if time runs short: the JSX namespace and element types, attribute types and contextual typing (the bulk of the TSX units), resolution, diagnostics and elaboration, then the rest. Commit working increments.

## Gate

Take a full baseline first and keep its identical-unit list. While iterating: a units file with the TSX and JSX units (from `artifacts/equivalence/manifest.json`, kind `TSX`/`JSX`) run through the C# checker dump and `harness/astdump/compare-checker.mjs --units` directly (the commands `gate.mjs --phase checker` runs), or `gate.mjs --track optimized --phase checker --sample 2000`; the full corpus at the end. Target: most TSX/JSX units identical, more identical units than at the start in every section, 0 crashed (027977 excepted if it times out only under gate load), and every unit that was identical at the start and becomes different listed with the module it waits for (or fixed). Limit: 30 gate runs (each dump-and-compare counts as one). Work economically: no broad exploration, read what the port needs.

## Working

One attempt: Opus 5.5, medium effort, no sub-agents. Work directly on `main` in the main checkout (no worktree, no branch); scratch `<session scratch>/C9b/`. C# with the file tools only (also renames). Do not download or install anything. Commit with `git commit -s` (noreply identity as configured); do not push. Before the final report, stop every background task you started. At the end add a C9b section to [phase1-progress.md](../phase1-progress.md) in the style of the C8 section.

## Report

Brief: gate runs and the last full summary (identical per section, crashed); TSX/JSX units identical before and after; regressions with their waiting module; what was ported and what is left (remaining JSX `TODO(C9)`, should be none; the main causes of the TSX units that still differ); commit hash(es).
