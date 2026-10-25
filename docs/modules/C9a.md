# C9a: syntactic diagnostics of JavaScript files — brief

Small module of Phase 3 ([phase3-plan.md](../phase3-plan.md)), part of C9, run in parallel with C2 and C3 (decided by the author, 2026-10-25). Track O only. Written by the orchestrator. Standing rules of [README.md](README.md); run only the tests and gates your change affects.

## Starting point

`main` at 5f339bd plus the regenerated Gate C oracle (JavaScript/JSX units are checked with `allowJs: true`). All 1,299 JS/JSX units crash in the C# checker dump: `Program.GetSyntacticDiagnostics` (`src/NeverSharp.Optimized/Program/Program.cs`, line 171) throws `NotSupportedException` for the additional syntactic diagnostics of JavaScript files.

## Goal

Port `getJSSyntacticDiagnosticsForFile` from `external/typescript/src/compiler/program.ts` (line 2992, about 250 lines: the walk that reports TypeScript-only syntax in JavaScript files — type annotations, `implements`, interfaces, type aliases, enums, modifiers, non-null assertions, `as`/`satisfies`, type parameters and arguments, parameter properties, decorators in the positions it checks, …, each with the exact diagnostic, position and message tsc uses) and wire it into `Program.GetSyntacticDiagnostics`, plus whatever else on the JS path throws `NotSupportedException` before the checker runs (other program-layer stubs for JS files). Do not port checker JS typing (that is the rest of C9); where the checker reaches an unported JS path, leave a clearly marked stub that does not crash (returning the types C1 already computes) rather than throwing.

## What may change

`src/NeverSharp.Optimized/Program/` and, only for non-crashing stubs, the smallest possible edits in `src/NeverSharp.Optimized/Checker/` (two other agents, C2 and C3, are editing the checker in parallel — keep checker edits minimal and separate). Not the parser/binder, Track F, `eng/`.

## Gate

`gate.mjs --track optimized --phase checker --main …` (full corpus; the C# dump is fast); target: 0 crashed units, more JS units identical in `diagnostics`; no regression in TS units. Limit: 20 gate runs.

## Working

One attempt: Opus 5.5, medium effort, no sub-agents, own worktree; scratch `<session scratch>/C9a/`. Junctions as in [O9.md](O9.md) (list them; unlink, never delete recursively). C# with the file tools only (also renames). Do not download or install anything. Commit with `git commit -s`; do not push. Before the final report, stop every background task you started.

## Report

Model; gate runs and the last summary (identical per section, crashed); what was ported; every checker file touched (for merging with C2/C3); junctions; commit hash.
