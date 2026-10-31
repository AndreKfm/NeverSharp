# CO3: object types and member lists without references — brief

Third optimization step of the Phase 3 checker; continues [CO2](CO2.md), which stopped after five of its steps (kept: tuning-check `cs-o-aot-ws-check` 0.800 [0.796, 0.804], `-single` 0.932 against 0441e34; `results/phase3/CO2/run1/`). Track O only. Written by the orchestrator. Standing rules of [README.md](README.md); run only the tests and gates your change affects. The goal, the hard constraint and the rules of [CO2.md](CO2.md) apply unchanged; this brief names the remaining parts.

## Starting point

`main` at 8ca2c12 (after this brief's commit: its parent). Gate C passes (50,177 of 50,177); tuning-check and the five repository programs are line-identical with tsc 6.0.3. After CO1+CO2: tuning-check `--check` 16.07 s, `--single` 21.95 s; vscode aot-ws `--check` 18.26 s with 6.47 s Workstation GC pause (CO2's measurement). The layout as of now: [phase3-checker-layout.md](../phase3-checker-layout.md); CO2's account and its "What is left": [phase1-progress.md](../phase1-progress.md), section "CO2".

Surviving objects after checking, vscode (CO2's heap snapshot after step 5): `List<Signature>` 2.0M + `Signature[]` 0.56M; `ObjectTypeData` 1.68M; `SymTable` 1.65M + entry arrays 1.29M + index arrays 0.3M (0.68M of the tables are the checker's copies of binder tables); `List<SymbolRef>` 1.4M + `SymbolRef[]` 1.35M (0.74M of them signature parameters); `List<IndexInfo>` 1.0M (mostly empty); mappers (`ArrayTypeMapper` 0.58M with 1.1M `Type[]`, simple/composite/deferred 0.45M, `Func<Type>` closures); 1.47M strings (literal values, the binder-name intern dictionary, the tuple and template-literal caches still keyed by strings, module resolution). CO2's heap-snapshot tool and survivor probe are in its scratch (`<session scratch>/CO2/`).

## Goal

In steps, each gated, measured and committed on its own (a stop after any step leaves a consistent, faster `main`):

1. **Object types as struct records** (`ObjectTypeData` → per-kind or common records in chunks) with their resolved and declared member lists — properties, call and construct signatures, index infos, base types — as ranges in per-checker arenas. This changes the member-list API at about 200 call sites (`GetSignaturesOfType`, `GetPropertiesOfType`, `GetIndexInfosOfType`, `ResolveStructuredTypeMembers`, the declared variants). Keep TypeScript's semantics where it compares lists by identity (e.g. `getInstantiatedSignatures`, `resolvedTypeArguments === typeParameters`-style checks) and where it distinguishes `undefined` from an empty array; keep the order of every list. Remaining per-type objects (instantiation caches, mapped/tuple data) into records as far as the profile shows them surviving.
2. **Symbol tables in an arena:** `SymTable` entries and index in per-checker arenas (handles instead of objects); avoid the checker's copies of binder tables where TypeScript's copy is never mutated afterwards (copy on first write), or say why it is needed.
3. **Signature data:** parameters and composite signatures as arena ranges.
4. **Mappers as records:** type mappers as handles to records (kind, sources/targets as `TypeList`s, the deferred and function mappers by the minimum the remaining closures need); `Func<Type>` closures off the long-lived data.
5. **The remaining strings** where they survive: the tuple and template-literal caches by value keys (as CO2's `KeyMap`), literal values interned or in a value table, the binder-name intern dictionary.
6. `[NoAlloc]` on checker hot paths where it now holds.

Order: 1 first (the largest share and the base for 3); then by the profile. If the work clearly exceeds one session, stop after a completed step and report what is done and what is left.

Target: the surviving objects and references on vscode and tuning-check down by a large factor again, the Workstation GC pause with them; tuning-check `cs-o-aot-ws --check` clearly faster than 8ca2c12 and `--single` not slower.

## What may change

As in [CO2.md](CO2.md): `src/NeverSharp.Optimized/Checker/`, `src/NeverSharp.Optimized/Program/` where the checker needs it, `Arena/` if the checker's arenas build on it (then the parser gate too), read-only accessors in `Binder/` (then Gate B), `tools/NeverSharp.Dump.Optimized` only if the checker dump needs adapting (its output must not change). Not Track F, not `eng/`, not the oracle, Gate C's harness, `bench.mjs`/`report.mjs`, the tuning-check list or `docs/preregistration.md`. Driver changes only for measurement switches that default off and are not committed.

## Tests to run

As in [CO2.md](CO2.md): Gate C `--sample 2000` after each step; the full corpus after the last step, with no change to `src/` after the last full run (keep its summary line); Gate B / parser gate only if those areas change. At the end: `check-equivalence.mjs --corpus tuning-check --skip-tsrs`, `check-equivalence.mjs --skip-tsrs` on the five repositories, parallel dumps of the five repositories identical with the baseline's, `cs-o-aot-ws --check` on tuning-check 4 times (no crash, 130,435 each time). Limit: 20 Gate C runs.

## Working

One attempt: Opus 5.5, high effort (optimization), no sub-agents. Work directly on `main` in the main checkout (no worktree, no branch); scratch `<session scratch>/CO3/` (CO1's and CO2's folders: reuse tools, do not modify). **Before the first change**, build the starting drivers (`build-cs.mjs --tracks optimized --runtimes aot-ws,aot-srv,jit-ws`) to `<session scratch>/CO3/base/` and take the baseline heap snapshot (vscode, tuning-check). **C# with the file tools only (Edit/Write): no sed, no Python or other scripts on C# files, not even for comments or renames.** Temporary probes removed before committing. No downloads or installs beyond the existing tools. Commit each kept step with `git commit -s`; do not push. A step is kept only if your own measurement shows it is not slower (tuning-check `--check` and `--check --single`, AOT ws, interleaved with the previous step, at least 5 runs each); revert it otherwise and report it. Before the final report, stop every background task you started. At the end add a CO3 section to [phase1-progress.md](../phase1-progress.md) and update [phase3-checker-layout.md](../phase3-checker-layout.md).

## Report

As in [CO2.md](CO2.md): the steps with their design and own measurements (tuning-check parallel and single, ws and srv; vscode ws parallel), surviving objects/references before/after, GC count and pause; tried and reverted; what is left; Gate C runs and the last full summary line; equivalence; stability; NoAlloc count; files outside `Checker/`; commit hashes.

## Measurement (orchestrator)

Tuning-check `cs-o-aot-ws-check` (decides) and `cs-o-aot-ws-check-single`, 20 runs each, rig-compliant, baseline drivers (8ca2c12) against the final `main`. Kept if every gate passes and it is not slower by the 95 % interval; otherwise the commits are reverted. Then an additional checker run on the benchmark repositories (profile `check`), on the author's decision.
