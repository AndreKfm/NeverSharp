# CO2: reference-free checker data — brief

Second optimization step of the Phase 3 checker, after [CO1](CO1.md) (kept: tuning-check `cs-o-aot-ws-check` 0.729 [0.723, 0.731], `-single` 0.769 against 581431f; `results/phase3/CO1/run1/`). Track O only. Written by the orchestrator. Standing rules of [README.md](README.md); run only the tests and gates your change affects.

## Starting point

`main` at 0441e34. Gate C passes (50,177 of 50,177 on the last full run); tuning-check and the five repository programs are line-identical with tsc 6.0.3.

CO1's account of the remaining gap ([phase1-progress.md](../phase1-progress.md), section "CO1"): the largest part is the **Workstation GC pause** — 7.2 s on tuning-check `--check` (20.1 s wall), 10.1 s on vscode (22.0 s wall), with all four checkers stopped. It is proportional to the checker data that survives (heap at the end 4.2 GB on vscode, about 1 GB per checker), not to the number of collections (`DOTNET_GCgen0size=256 MB`: 1,757 → 118 collections, pause 8.8 → 9.9 s). The survivors are millions of small managed objects per checker; vscode heap snapshot: 3.3M strings, 1.8M `List<NodeRef>` (declaration copies of binder symbols), 1.4M mostly empty `List<Signature>`, 1.2M `SymTable`, 1.2M `ObjectTypeData`. Server GC marks them in parallel (srv `--check` 14.3 s on tuning-check), Workstation GC on one thread. The measured endpoint configuration is Workstation GC; the author decided (2026-10-30) to remove the cause in the checker's data rather than change the runtime configuration.

The layout decision of C0 ([phase3-checker-layout.md](../phase3-checker-layout.md)) already uses handles (`Type`, `Signature`, `SymbolRef`, `NodeRef`) and per-checker record arrays, and names "no references for the collector to trace" as the reason; C0 kept managed objects inside the records until measured. Today the records still hold references, e.g. (`Checker/TypeStore.cs`, `Checker/Links.cs`, `Checker/Checker.Instantiate.cs`):
- `ObjectTypeData` (a class per object type) with `SymTable? Members`, `List<SymbolRef>? Properties`, `List<Signature>?` call/construct signatures, `List<IndexInfo>?`, the declared variants, `List<Type>? ResolvedBaseTypes`, `Dictionary<string, Type>? Instantiations`, `TypeMapper? Mapper`, `ElementFlags[]?`, `NodeRef[]?`; the other kind data (`TypeParameterData`, `IndexedAccessData`, `ConditionalData`, `IndexInfo`, …) are classes too;
- `TransientSymbolRecord` with `string EscapedName`, `List<NodeRef>? Declarations`, `SymTable?` members and exports;
- `SymTable`: an entry array of (`string` name, `SymbolRef`);
- `SignatureRecord` with `SymbolRef[] Parameters`, `Signature[]? CompositeSignatures`;
- instantiation and relation caches keyed by strings built from type ids; mappers as class hierarchies.

The binder already stores names as `TextRef` (interned per file, Phase 2) and declarations as link chains (`Binder/BinderStore.cs`).

## Goal

Checker data that lives as long as the checker contains no object references the collector has to trace, so the Workstation GC pause shrinks with it:

1. **Names without strings:** symbol and property names as interned ids (one name table per checker, or the binder's `TextRef` interning where the name comes from a file), symbol tables keyed by them; strings only where text is produced (messages, printing).
2. **Lists in arenas:** properties, signatures, index infos, base types, declarations, parameters, composite signatures as (start, count) ranges in per-checker append-only arenas, as `TypeList` already is; empty lists as a range of length 0 with "absent" distinguished where TypeScript distinguishes `undefined` from `[]` (C12's `TypeList.PresentEmpty`).
3. **Declarations not copied:** transient symbols refer to the binder's declaration chains where they are copies of them (the 1.8M lists), and own a range only when they add declarations.
4. **Kind data as struct records** in per-kind chunked arrays (CO1's `Chunks<T>`), symbol tables in arenas too; caches keyed by values (type ids, list ranges), not by built strings.
5. **Mappers, relation and instantiation caches:** whatever the profile shows as surviving after 1–4, by the same means. Short-lived scratch objects may stay if they die young.
6. `[NoAlloc]` on the checker hot paths where it now holds.

Hard constraint, as in CO1: the checker's results do not change. Type, symbol and signature creation order stays tsc's (type ids, union member order); the iteration order of every symbol table and list stays insertion order as in TypeScript's `Map`s and arrays; single-threaded output stays identical with tsc. Each checker keeps its own memory (the b42ecc2 rule: never write into a store shared with other checkers — binder chains and file stores are read-only for the checker).

This is a larger redesign than CO1. Work in steps (e.g. names → lists → declarations → kind data → caches), each step gated, measured and committed on its own, so that a stop after any step leaves a consistent, faster `main`. If the work clearly exceeds one session, stop after a completed step and report what is done and what is left.

Target: the surviving checker heap and the ws GC pause on tuning-check and vscode down by a large factor; tuning-check `cs-o-aot-ws --check` clearly faster than 0441e34 and `--single` not slower.

## What may change

`src/NeverSharp.Optimized/Checker/`, `src/NeverSharp.Optimized/Program/` where the checker needs it, `src/NeverSharp.Optimized/Arena/` if the checker's arenas build on it (then the parser gate too), read-only accessors in `Binder/` for the declaration chains and names (no binder behaviour change; then Gate B too). `tools/NeverSharp.Dump.Optimized` only if the checker dump needs adapting to the new accessors (its output must not change). Not Track F, not `eng/` (no runtime configuration change), not the oracle, Gate C's harness, `bench.mjs`/`report.mjs`, the tuning-check list or `docs/preregistration.md`. Driver changes only for measurement switches that default off (say so).

## Tests to run

Gate C (`gate.mjs --track optimized --phase checker --sample 2000`) after each step; the full corpus after the last step, with no change to `src/` after the last full run. Gate B (`gate.mjs --track optimized --phase binder --jsdoc-mode all,types`) if `Binder/` changes; the full Track O parser gate if `Arena/` or node storage changes. At the end: `check-equivalence.mjs --corpus tuning-check --skip-tsrs` and `check-equivalence.mjs --skip-tsrs` on the five repositories; parallel dumps of the five repositories identical with the baseline's; `cs-o-aot-ws --check` on tuning-check 4 times (no crash, 130,435 each time). Limit: 20 Gate C runs.

## Working

One attempt: Opus 5.5, high effort (optimization), no sub-agents. Work directly on `main` in the main checkout (no worktree, no branch); scratch `<session scratch>/CO2/`. **Before the first change**, build the starting drivers (`build-cs.mjs --tracks optimized --runtimes aot-ws,aot-srv,jit-ws`) and copy them to `<session scratch>/CO2/base/` as the baseline; take a heap snapshot / survivor profile of the baseline (vscode and tuning-check, as CO1 did) to measure each step against. **C# with the file tools only (Edit/Write): no sed, no Python or other scripts on C# files, not even for comments or renames.** Temporary probes in C# are removed before committing. Do not download or install anything beyond the existing tools (`.tools/dotnet-trace`). Commit each kept step with `git commit -s` (noreply identity as configured); do not push. A step is kept only if your own measurement shows it is not slower (tuning-check `--check` and `--check --single`, AOT ws, interleaved with the baseline or the previous step, at least 5 runs each); revert it otherwise and report it. `bench.mjs` needs a clean tree (`git stash -u` around it, or run hyperfine directly). Before the final report, stop every background task you started. At the end add a CO2 section to [phase1-progress.md](../phase1-progress.md) and update the Track O column of [phase3-checker-layout.md](../phase3-checker-layout.md) to the new layout.

## Report

Brief: the steps done (design per data kind: names, lists, declarations, kind records, caches), each with its own measurement against the baseline (tuning-check parallel and single, ws and srv; vscode ws parallel), surviving heap and object counts before/after, GC count and pause; what was tried and reverted; what is left; Gate C runs and the last full summary; tuning-check and five-repository equivalence; stability runs; NoAlloc count; files outside `Checker/`; commit hashes.

## Measurement (orchestrator)

Tuning-check `cs-o-aot-ws-check` (decides) and `cs-o-aot-ws-check-single`, 20 runs each, rig-compliant, baseline drivers (0441e34) against the final `main`. Kept if every gate passes and it is not slower by the 95 % interval; otherwise the commits are reverted. An additional checker run on the benchmark repositories (profile `check`) follows on the author's decision.
