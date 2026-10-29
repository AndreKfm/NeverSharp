# C9f: the last Gate C units — brief

Module C9f of Phase 3 ([phase3-plan.md](../phase3-plan.md)). Track O only. Runs alone. Written by the orchestrator. Standing rules of [README.md](README.md); run only the tests and gates your change affects.

C9 was split into sequential modules, one agent at a time: C9a (syntactic diagnostics of JavaScript files), C9b JSX, C9c expression and flow typing of TS/TSX, C9d JavaScript and JSDoc typing, C9e decorators, lib suggestions and the rest (all done). C9e did not reach Gate C; **C9f** (this brief) takes the units it left. The three `TODO(C9e)` markers on pattern ambient modules stay unless a unit needs them.

## Starting point

`main` after C9e (b8d71e6) — see the C9e section of [phase1-progress.md](../phase1-progress.md): Gate C 50,140 of 50,177 identical in C9e's last full run (diagnostics 50,163, types 50,142, symbols 50,172), 1 crashed (027977, timeout under gate load; 23 s alone). One unit of that run, 012875, was fixed after the run and not measured in full; the expected count at b8d71e6 is 50,141.

The 37 units of C9e's last full run, grouped by the orchestrator from first differences and C9e's report:

- narrowing of destructured declarations (6): 002458, 005981, 009524, 010397, 033769, probably 027148. `head : number` expected, `string | number` produced; dependent destructured variables (7022 missing); `hadErrors` follows. checker.ts `getFlowTypeOfDestructuring` uses `getSyntheticElementAccess`, which builds a synthetic element/property access node with the binding element's flow node and narrows it as a reference. The C# side has no mechanism for synthetic nodes yet. Design one that keeps the node arena's invariants (a checker-owned synthetic node, or a reference key that `isMatchingReference`/`getFlowTypeOfReference` accept as if it were the access), and say in the report what you chose and why;
- union member order (5 TSX): 036147, 036149, 036151, 037779, 038845. `keyof X | "as"` expected, `"as" | keyof X` produced: the `keyof X` type gets its id later than in tsc. Find the point where tsc creates it earlier (resolution of the type alias, the declared type of the property, an `as` expression's checks) and match the order of creation; do not sort to fake the order;
- JavaScript (12): the defaulted container `x.y || {}` / `x.y ?? {}` typed `typeof x.y` instead of `{}` (015614–015619, 004803: the container's type and the subtype reduction against the fresh `{}` in `checkBinaryLikeExpression`/`getUnionType` with `UnionReduction.Subtype`); getter/setter pairs of JS object literals (002063); `@class` functions called without `new` typed `C3` instead of `any` (015353); 000148 and 012875 (6133 on JS members), 012718 and 012873 (underline lines of the position markers that follow from a type);
- smaller TS groups: circularity through an assertion call's effects signature, 2456 missing (001628); `this` in object-literal functions inside class property initializers (003759, 007905: `() => any` expected); `| undefined` of reverse-mapped optional properties (016029); the write type through an intersection with a union (005346, `hadErrors`);
- not analysed by C9e (8): 000838 (2339 extra), 001971 (a long signature), 003843 (2312 vs 2430), 012440 (2612 missing), 025286 (an object literal's type), 035889 (7053 extra), 048046 (an underline line), 027977 (one underline line when run alone).

Use `<session scratch>/C9f/` for your own scripts; the dumps are `artifacts/equivalence/dumps/ts-checker-types` (oracle) and `artifacts/dumps/cs-optimized-checker-types` (C#).

## Goal

Make Gate C pass: every unit identical in all three sections. Port what checker.ts does for the groups above (structure as in checker.go). The synthetic element access is the main design task; port it as far as it decides the units and say in the report which other callers of `getSyntheticElementAccess` remain open, if any. If a difference is a parser or binder bug, do not change parser or binder; report it with the units it affects. If a difference is an oracle quirk that needs code without a counterpart in checker.ts, do not hack around it; list it with the reason.

027977: fix its underline line. Its timeout under gate load is a harness and load question, not a checker one: do not change the harness's timeouts; report the unit's time alone and in the gate run, and whether it passes there.

Order of work if time runs short: destructuring narrowing, union order, JavaScript, the smaller TS groups, the unanalysed units. Commit working increments.

## Gate

Take a full baseline first and keep its identical-unit list. While iterating: a units file with the units that differ at the baseline, run through the C# checker dump and `harness/astdump/compare-checker.mjs --units` directly (the commands `gate.mjs --phase checker` runs), plus `gate.mjs --track optimized --phase checker --sample 2000` after changes to shared paths (flow analysis, union creation, synthetic nodes); the full corpus at the end. Target: Gate C passes; otherwise every differing unit listed with its cause, more identical units than at the start in every section, and no unit identical at the start that differs at the end (no unmeasured fixes after the last full run: if you fix something after it, run the full corpus again). Limit: 20 gate runs (each dump-and-compare counts as one). Work economically: no broad exploration, read what the port needs.

## Working

One attempt: Opus 5.5, medium effort, no sub-agents. Work directly on `main` in the main checkout (no worktree, no branch); scratch `<session scratch>/C9f/`. C# with the file tools only (also renames and small edits; no sed or scripts on C# files). Changes outside `Checker/` only where the checker needs them and never in parser, scanner or binder behaviour; name each such file in the report. Do not download or install anything. Commit with `git commit -s` (noreply identity as configured); do not push. Before the final report, stop every background task you started. At the end add a C9f section to [phase1-progress.md](../phase1-progress.md) in the style of the C9e section, and state whether Gate C passes.

## Report

Brief: gate runs and the last full summary (identical per section, crashed); units per group above before and after; regressions; the design of the synthetic element access; parser or binder bugs and oracle quirks found; what is left with causes; whether Gate C passes; 027977's times; commit hash(es).
