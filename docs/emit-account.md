# Account of the emit's allocations and time (EA1, analysis)

Brief: [modules/EA1.md](modules/EA1.md). This is an exploratory analysis, not a protocol result. No file under `src/` was changed in a commit; the probes and the one experiment below were uncommitted and are removed. Track O only.

**Every number below is exploratory**: medians of 5 runs (3 on vscode) on the reference machine, not rig-compliant protocol runs. No other load was present during the measurements (system CPU at 0–2 % before each batch). Conditions and commands are in [Measurements](#measurements). The endpoint stays `results/phase4/emit/2026-11-03T19-39-41-764Z/` (G = 1.030).

## Short answers

1. **What the emit adds on vscode (Workstation GC, 4 checkers):** +9.07 s wall (14.30 → 23.37 s), +6.44 GB allocated, +555 gen0 / +89 gen1 / +3 gen2 collections, +1.20 s pause, 690 MB promoted. typescript-go adds +6.30 s. Single-threaded: +25.0 s (typescript-go +22.75 s), +0.70 s pause.
   - **Where the time goes depends on the repository.** On vscode the declaration emit's node builder and accessibility checks take 53 % of the emit (single-threaded, probe), the JavaScript printer 35 %. On sentry, bluesky, playwright and tldraw the JavaScript printer takes 64–74 %, the node builder and accessibility checks 4–18 %.
   - **Where the bytes go:** the printer (js 42 %), the emit copy of the store (27 %; mostly pinned arrays) and the output strings (10 %).
2. **What survives:** 80–86 % of the bytes promoted during the emit (sampled: vscode 84 %, sentry 86 %, bluesky 80 %) are the output texts. The driver's in-memory `WriteFile` keeps every output string to the end, so each small output string is copied twice (gen0 → gen1 → gen2). They are ref-free. Objects with references that survive are small: about 96 MB of 1,562 MB sampled on vscode (`EmitNode`s, the checker's expression links grown during the emit). The emit's pause follows the **number** of gen0 collections, about 2 ms each at four threads whatever they promote; that number follows the small-object allocation volume (about 10 MB per gen0 collection).
3. **CPU outside GC:** on vscode, 52 % of the single-threaded emit samples have their leaf in the accessibility search of the declaration emit: `GetAccessibleSymbolChain` (`TrySymbolTable`, 21 %), `Checker.TableOf` (16 %), `GetAlternativeContainingModules` (8 %), `GetAliasForSymbolInContainer` (7 %). TypeScript 6.0.3 and typescript-go cache the result of `getAccessibleSymbolChain` (`accessibleChainCache`); Track O left that cache out. **This explains the vscode difference:** with the cache (uncommitted experiment, outputs identical) vscode check+emit drops from 23.28 to 20.79 s (emit +6.5 s, typescript-go +6.30 s) and single-threaded from 62.51 to 56.99 s. It is Track O's code, made visible by vscode's files: many inferred types in exported declarations (518,044 accessibility calls and 27,892 node builder calls on vscode against 180,854 and 9,047 on sentry).
4. **Parallel:** the emit is balanced on five of the six lists (idle 3.5–7 %), not on vscode: idle 21.8 %, because one file (`promptCrafting.ts`, 2.3 s) sits on checker 0, which needs 9.14 s while the others need 6.1–6.7 s. A per-file queue would bring the span to 7.37 s (simulated upper bound). With the cache of answer 3 that file costs little, and vscode's emit is balanced too (idle 8.2 %).
5. **First step:** port `accessibleChainCache` (mark (a)). Measured in the experiment: vscode −2.5 s (−10.7 %) parallel, −5.5 s single; tuning emit list −0.62 s (−4.3 %) parallel, −2.37 s single; sentry and the check alone unchanged. Next: the remaining accessibility search (`getAlternativeContainingModules`' repeated walk, `Checker.TableOf`'s dictionary lookups), then the emit's young garbage. Ranked list in [5](#5-candidates).

## 1. What does the emit add?

Base drivers built from 253c11d with `build-cs.mjs` (aot-ws). `--check` against `--check --emit`; the emit run also uses Gate D's options (`declaration: true`, `newLine: lf`), as in the endpoint. typescript-go: `parse-go.exe` with the same flags, `GODEBUG=gctrace=1`.

| List | Track O ws parallel: check → check+emit | added alloc | added GCs (gen0/1/2) | added pause | Track O single: added wall, pause | typescript-go added wall: parallel / single |
|---|---|---:|---|---:|---|---|
| vscode | 14.30 → 23.37 s (+9.07) | +6.44 GB | +555/+89/+3 | +1.200 s | +25.01 s, +0.702 s | +6.30 / +22.75 s |
| sentry | 3.65 → 5.19 s (+1.54) | +2.16 GB | +193/+42/+3 | +0.321 s | +4.51 s, +0.150 s | +1.40 / +6.06 s |
| bluesky | 0.98 → 1.23 s (+0.25) | +0.38 GB | +34/+9/+1 | +0.026 s | +0.83 s, +0.025 s | +0.28 / +1.09 s |
| playwright | 1.76 → 2.18 s (+0.43) | +0.59 GB | +52/+12/+1 | +0.058 s | +1.37 s, +0.024 s | +0.39 / +1.76 s |
| tldraw | 1.70 → 2.28 s (+0.58) | +0.73 GB | +66/+16/+2 | +0.077 s | +1.76 s, +0.037 s | +0.50 / +2.11 s |
| tuning emit list | 11.29 → 14.50 s (+3.21) | +2.57 GB | +207/+51/+2 | +0.541 s | +8.60 s, +0.382 s | +2.09 / +6.47 s |

- **Single-threaded allocation** is the same as parallel (vscode +6.35 GB, tuning +2.56 GB): the emit duplicates no work across checkers.
- **Promoted bytes** in the collections of the emit phase (gen0 + gen1, AOT probe, parallel / single): vscode 690 / 611 MB; sentry 261 / 226; tuning 244 / 220; tldraw 85 / 72; playwright 73 / 59; bluesky 48 / 38 MB. For comparison, the check phase of vscode promotes 1,606 MB.
- **Cost per output MB, single-threaded:** Track O 95–113 ms/MB on sentry, bluesky, playwright and tldraw, 154 on vscode, 217 on the tuning emit list; typescript-go 128–150 ms/MB everywhere, 163 on the tuning list.

**Split by phase** (exclusive time of each phase, summed over threads; probe level 2, AOT ws; vscode 3 runs, others 5). The node builder and accessibility rows are the resolver's calls from the declaration transformer; time spent in them is not counted in "declarations". Substitution and notification hooks of the transformers run during printing and are counted in the printer.

| Phase | vscode single | vscode parallel (sum over 4 threads) | sentry single | tuning single | bluesky single |
|---|---:|---:|---:|---:|---:|
| emit copy of the store (+ factory, resolver) | 0.29 s, 1,707 MB | 0.38 s | 0.15 s, 721 MB | 0.22 s, 900 MB | 0.03 s, 129 MB |
| TypeScript transformer | 0.75 s, 316 MB | 0.99 s | 0.14 s, 85 MB | 0.18 s, 134 MB | 0.02 s |
| ESNext / ES decorators / class fields / module | 0.27 s, 95 MB | 0.31 s | 0.04 s, 35 MB | 0.12 s, 88 MB | 0.01 s |
| declarations transformer (own work) | 0.64 s, 206 MB | 0.74 s | 0.20 s, 80 MB | 0.34 s, 111 MB | 0.04 s |
| node builder (resolver `createTypeOf*`, `createReturnType*`, `createLiteralConstValue`, late-bound index signatures, JS declarations) | 5.98 s, 300 MB, 27,892 calls | 6.59 s | 0.19 s, 34 MB | 3.39 s, 254 MB | 0.02 s |
| accessibility (resolver `isSymbolAccessible`, `isEntityNameVisible`, `isDeclarationVisible`, `collectLinkedAliases`) | 7.38 s, 120 MB, 518,044 calls | 7.69 s | 0.13 s, 15 MB | 1.64 s, 23 MB | 0.01 s |
| printer, .js (comments included) | 8.75 s, 2,686 MB | 10.06 s | 3.29 s, 888 MB | 1.92 s, 652 MB | 0.60 s, 150 MB |
| printer, .d.ts | 0.84 s, 203 MB | 0.94 s | 0.22 s, 55 MB | 0.34 s, 118 MB | 0.05 s |
| output to string (`GetText`, writer reset) | 0.06 s, 644 MB | 0.12 s | 0.03 s, 196 MB | 0.03 s, 161 MB | 0.003 s, 30 MB |
| write callback (driver's in-memory `WriteFile`) | 0.01 s | 0.02 s | 0.01 s | 0.01 s | 0.002 s |
| rest (resolver lookup, dispose, release, `GetDiagnostics`) | 0.11 s, 63 MB | 0.15 s | 0.08 s, 51 MB | 0.18 s, 102 MB | 0.01 s |
| **sum** | **25.13 s, 6,342 MB** | **28.38 s** | **4.51 s, 2,158 MB** | **8.38 s, 2,544 MB** | **0.81 s, 374 MB** |

- **Comments:** with the finer probe (level 3) the comment functions of the printer take 2.34 s of the vscode emit and 0.76 s of sentry's single-threaded; that probe adds about 1.8 s of overhead on vscode (31 million calls), so the sampled profile is the better number: comments 3.0 % of the vscode emit samples, 10.5 % on sentry, 3.2 % on the tuning list.
- **Script resolver calls** (`isReferencedAliasDeclaration`, `getReferencedExportContainer`, …, level 3): 0.05 s on vscode (314,892 calls); negligible.
- **Parallel inflation:** the same work costs more in parallel: 28.38 s summed over threads against 25.13 s single-threaded on vscode (+13 %), 5.80 against 4.51 s on sentry (+29 %), mostly in the printer (memory traffic and suspensions for collections).
- **Probe overhead:** level 2 changes the wall time by less than 1 % (vscode 23.51 against 23.37 s).

## 2. What survives?

Method of CO5's account ([phase1-progress.md](phase1-progress.md), "CO5", "Account of the pause"): JIT ws traces with GC verbose, `GCHeapSurvivalAndMovement` and `GCSampledObjectAllocationHigh`, read by the scratch tool `acctool`, which follows each sampled object through the survival and move ranges of every collection. EA1 adds the emit: objects allocated between the probe marks `phase:emit` and `phase:after` have origin "emit" and a sub-phase taken from the allocation's call stack. One trace each of vscode, sentry and bluesky, `--check --emit`, 4 checkers. Sampled numbers are estimates and lean towards large objects (a sample carries the bytes of its type since the previous sample); the AOT probe's exact numbers above are the reference for allocation by phase.

**Collections of the emit phase (vscode, AOT probe, parallel):** 471 gen0 (919 ms), 81 gen1 (174 ms), 3 background gen2 (2.6 ms); 1,156 ms in all of the run's 4.38 s. A gen0 collection costs 1.95 ms on average in the emit and 1.92 ms in the check, while the emit's gen0 collections promote only 0.78 MB each: **the pause follows the count of collections** (CO5's fit: about 1 ms fixed plus 1.25 ms per promoted MB; the fixed part grows with the number of running threads). Single-threaded the emit's gen0 collections cost 1.33 ms each (490, 652 ms). 60 % of the single-threaded emit's collections start while the JavaScript printer runs (325 of 545), matching its share of the allocation.

**Promoted in the emit-phase collections (vscode, sampled, every promotion counted):** 1,562 MB, of which

| Group | MB | Kind |
|---|---:|---|
| `System.String`: the output texts (`EmitTextWriter.GetText`), kept by the driver's `WriteFile` to the end | 1,309 | ref-free, survives: 652 MB of them reach gen2 |
| `System.Char[]`: the writer's `StringBuilder` chunks | 102 | ref-free, mid-life (one file) |
| `EmitNode` (TypeScript transformer: parameters, comment ranges, type nodes; node builder clones) | 42 | references, live until the file's `Dispose` |
| `Entry[]`, `Int32[]` (emit copy's interned-text table, line maps) | 51 | ref-free, mid-life |
| `Entry[ExpressionNodeLinks][]` (checker node links grown by the resolver during the emit: `GetConstantValueOf`, `CheckExpressionCached`) | 16 | references, permanent checker data |
| `Object[]`, `Slab`, `ObjectTextRange`, `TrackedSymbol[]`, synthesized comments, lists | about 25 | references |

- **Ref-free 1,466 MB, with references 96 MB.** sentry and bluesky show the same picture: output strings 86 % and 80 % of the promoted bytes (vscode 84 %); with references 6 % and 7 % (vscode 6 %).
- **Mark roots** of the emit's gen0 collections (vscode, JIT): through the card table ("Older") 243 MB, from the four stacks 98 MB. The output strings are reached from the driver's per-file lists, which are old.
- **Dies young (cheap), by group** (vscode, sampled; survives no collection): the emit copy's pinned slabs (POH, about 1.5 GB, never counted against gen0), closures and delegates allocated per call in the printer's helpers (`Scanner.ComputeLineOfPosition` → `Core.BinarySearch` with a comparer delegate, about 250 MB; `Utilities.GetSourceTextOfNodeFromSourceFile` → `IsJSDocTypeExpressionOrChild` with a `FindAncestor` closure, about 115 MB), `StringBuilder` chunk growth in `EmitTextWriter.WriteText` and the per-line indent concatenation (about 120 MB), `Predicate` closures in `GetAccessibleSymbolChain` (about 105 MB), `BinaryFrame[]` per binary expression (about 40 MB), flow-analysis arrays of the checker work the TypeScript transformer triggers (about 80 MB), the `params string[]` of `Program.GetImpliedNodeFormatForEmitWorker` called per substituted node (sentry 122 MB, bluesky 31 MB).
- **Writer reset:** `EmitTextWriter.Clear` sets the `StringBuilder`'s length to 0; with several chunks .NET then allocates one new array of the full capacity. That is half of the "output to string" phase's 644 MB on vscode.

**Largest groups that survive:** the output strings (ref-free, 80–86 % of the emit's promoted bytes; they would survive under any change that keeps the in-memory `WriteFile`), then `EmitNode`s and the checker's node-link tables (with references, about 4 % together). Unlike the check (CO5: mappers, 1/3 of the promoted bytes with references), the emit has no large group of surviving objects with references.

## 3. CPU outside GC

Sampled thread-time profiles (`dotnet-sampled-thread-time`, JIT ws base driver, `--check --emit --single`, one trace each of vscode, sentry and the tuning emit list). The emit window is the span of the worker thread's samples whose stack reaches `CheckerPool.Emit`; every sample of that thread inside it is emit work. vscode: 12,001 samples over 21.9 s.

| Leaf (vscode emit samples) | % |
|---|---:|
| `GetAccessibleSymbolChain` → `TrySymbolTable` | 21.3 |
| `Checker.TableOf` (dictionary lookup of the checker's copy of a binder symbol table) | 16.2 |
| `GetAlternativeContainingModules` | 7.6 |
| `GetAliasForSymbolInContainer` | 6.7 |
| waiting for a collection (`Thread.PollGC`) | 5.5 |
| array allocation slow path (`GC.AllocateNewArrayWorker`: slabs, `StringBuilder` chunks) | 4.8 |
| `Buffer.Memmove` (copy, `GetText`) | 3.0 |
| printer: `EmitNodeListItems`, `GetSourceTextOfNodeFromSourceFile`, comment iteration, `ComputeLineStarts`/`ComputeLineOfPosition`, … | each ≤ 1.5 |
| `ImpliedNodeFormatDependentModuleTransformer.OnSubstituteNode` | 1.3 |

- **Inclusive (vscode):** `GetContainersOfSymbol` 34 %, `DeclarationsTransformer.TrackSymbol` → `IsSymbolAccessibleFull` 31 %, `SymbolToTypeNode` 29 %, the JavaScript printer about 30 %.
- **Tuning emit list:** the same picture, smaller: `TrySymbolTable` 28 % of the leaves, `GetAccessibleSymbolChain` 36 % inclusive, `Decls.FindIndex` 5 %.
- **sentry:** the printer (`print-js` + printer frames 37 %, comments 10.5 %), the TypeScript transformer 18 % (mostly checker work it triggers: `ResolveNameHelper`, flow types), waiting for collections and array allocation 26 % (JIT single-threaded); the accessibility search is about 2 %.

**typescript-go on the same lists** (wall time with and without `--emit`, `gctrace`): its emit costs 128–163 ms per output MB on every list, with 1 more collection and 0.1–1.1 s more concurrent mark time; its pauses stay at 1–10 ms. Track O's emit costs less than typescript-go's single-threaded where the JavaScript printer dominates (sentry 0.74, bluesky 0.76, playwright 0.78, tldraw 0.83 of typescript-go's added time) and more where the declaration emit dominates (vscode 1.10, tuning list 1.33).

**Why vscode:** the declaration emit's node builder and accessibility checks take 13.4 s of vscode's 25.1 s single-threaded emit, 0.54 µs per `.d.ts` character, against 0.32 s of sentry's 4.5 s (0.07 µs per character). vscode's files export many declarations with inferred types, so the declaration emit serializes many types and checks every symbol it names (518,044 accessibility calls). Each check runs `getAccessibleSymbolChain` over the scope's symbol tables, the globals included, and walks every alias in each. TypeScript 6.0.3 (`links.accessibleChainCache`, keyed by `useOnlyExternalAliasing`, the first scope with a table, and the meaning) and typescript-go (`symbolContainerLinks.accessibleChainCache`) cache the result per symbol; Track O left the cache out (`Checker.SymbolAccessibility.cs`, header: "a cache of the pure result"; TG1 listed it for the check). **So it is Track O's code, exposed by vscode's files**, not large outputs or many comments: vscode's `.d.ts` outputs are 15 % of its output characters, and the comments are 3 % of its emit samples.

**Experiment (uncommitted, env-gated, removed):** the cache as TypeScript 6.0.3 keys it, per checker, in `GetAccessibleSymbolChain` (the first relevant location computed as `forEachSymbolTableInScope` finds it; a fake scope of the node builder is its own key).
- **Outputs:** emit dumps and check diagnostics byte-identical with the base driver on all six lists, single-threaded and with 4 checkers (12 of 12 compared).
- **Wall time** (AOT ws, interleaved, against the probe driver without the cache):

| | without | with cache | change |
|---|---:|---:|---:|
| vscode check+emit, 4 checkers | 23.28 s | 20.79 s | −2.49 s (−10.7 %) |
| vscode single-threaded | 62.51 s | 56.99 s | −5.52 s (−8.8 %) |
| tuning emit list, 4 checkers | 14.52 s | 13.90 s | −0.62 s (−4.3 %) |
| tuning emit list, single-threaded | 26.04 s | 23.66 s | −2.37 s (−9.1 %) |
| sentry, 4 checkers / single | 5.14 / 15.03 s | 5.09 / 15.01 s | −1 % / 0 % |
| check only (vscode, tuning, sentry) | 14.14 / 11.32 / 3.62 s | 14.17 / 11.32 / 3.60 s | none |

- With the cache the emit adds 6.5 s on vscode (typescript-go 6.30 s) and 19.8 s single-threaded (typescript-go 22.75 s). Allocation and pause barely change (vscode −0.1 GB).
- **The emit with the cache** (probe level 2, vscode, 3 runs): single-threaded sum 19.83 s (was 25.13): node builder 5.98 → 2.31 s, accessibility 7.38 → 5.67 s, everything else unchanged; the JavaScript printer is now the largest phase (8.83 s, 45 %). `promptCrafting.ts` drops out of the slowest files (largest file now 1.0 s, `test-checker.ts`).
- **What remains hot** (sampled profile with the cache, vscode, single-threaded, 9,684 samples): `Checker.TableOf` 19.8 % of the leaves, `GetAliasForSymbolInContainer` 8.4 %, `GetAlternativeContainingModules` 8.0 %, `TrySymbolTable` 4.0 %; inclusive `GetContainersOfSymbol` → `GetAlternativeContainingModules` 36.6 %. `getAlternativeContainingModules` walks every import of the enclosing file and every export of each imported module for each symbol named; TypeScript 6.0.3 and typescript-go cache only non-empty per-file results, so the walk repeats whenever the symbol is not reachable through the file's imports. In Track O every `ExportsOf`/`MembersOf` on the way is a dictionary lookup (`TableOf`) where TypeScript reads a field.

## 4. Parallel

One work item per checker, files in program order (file *i* to checker *i* mod 4). Probe marks at the start and end of each checker's emit; per-file emit times from the probe; "per-file queue" simulates four threads taking files in program order from one queue with the measured per-file times (an upper bound for typescript-go's one work item per file: it ignores that a file's resolver calls need its checker).

| List | per checker (ms) | span | idle at the end of the pool | per-file queue (simulated) | ideal |
|---|---|---:|---:|---:|---:|
| vscode | 9,135 / 6,603 / 6,695 / 6,120 | 9,135 ms | 8,044 thread-ms (21.8 %); the first checker done waits 3,041 ms | 7,371 ms | 7,095 ms |
| sentry | 1,436 / 1,490 / 1,487 / 1,362 | 1,491 | 207 (3.5 %) | 1,453 | 1,451 |
| tuning emit list | 2,536 / 2,770 / 3,049 / 2,996 | 3,049 | 848 (7.0 %) | 2,829 | 2,826 |
| bluesky | 256 / 241 / 233 / 267 | 267 | 62 (5.9 %) | 250 | 249 |
| playwright | 433 / 387 / 416 / 418 | 433 | 74 (4.0 %) | 419 | 411 |
| tldraw | 533 / 533 / 482 / 545 | 547 | 90 (4.0 %) | 525 | 523 |

- **vscode:** checker 0 holds `extensions/copilot/src/extension/xtab/common/promptCrafting.ts` (2.3 s parallel, 2.1 s single-threaded: the file whose inference-fallback reports switch off the node builder's type cache, D8) and `codiconsLibrary.ts` of the copilot extension (0.8 s). Without them it would finish with the others.
- **Check phase for comparison:** vscode idle 7.6 % (first done waits 1.86 s), tuning emit list 33.8 % (lib.dom and the expensive units on fixed checkers, as in CO5), sentry 3.1 %.
- **Overlap of check and emit:** each checker could start its emit when its own check ends instead of at the barrier. On vscode the critical checker would end at 11.69 + 9.14 = 20.83 s instead of 12.66 + 9.14 = 21.80 s (−1.0 s); on the tuning list the critical checker is the same in both phases (no gain).
- **With the `accessibleChainCache` experiment** (section 3) vscode's emit is balanced: per checker 5,775 / 5,872 / 6,509 / 5,997 ms, idle 8.2 %, per-file queue 6,327 ms against a span of 6,510 ms. The overlap of check and emit then gains nothing on vscode either (checker 2 is last in both phases).

## 5. Candidates

Gains are estimates for the endpoint configuration (ws, 4 checkers) unless stated; "measured" means the uncommitted experiment above.

| # | Candidate | Gain (wall, pause) | Effort | Mark |
|---|---|---|---|---|
| 1 | **Port `accessibleChainCache`** (TypeScript 6.0.3 `getAccessibleSymbolChain`, per checker, same key) | measured: vscode −2.5 s parallel (−10.7 %), −5.5 s single; tuning list −0.6 s parallel, −2.4 s single; others ≈ 0; pause unchanged | low (one function, a key helper; Gate C and Gate D runs) | (a): outputs identical in the experiment; faithful to tsc 6.0.3 and typescript-go |
| 2 | **`getAlternativeContainingModules` without the repeated walk**: remember per checker, per symbol and file, that the file's imports give no alternative container (TypeScript 6.0.3 and typescript-go cache only non-empty per-file results, so the walk over the imports and their exports repeats; the result depends only on the bound program) | after 1: the walk is 36.6 % of vscode's remaining emit samples (about 7 s single-threaded); estimate −3 to −5 s single, −0.8 to −1.3 s parallel on vscode; small elsewhere | low | (a): result-identical; a Track O cache with no counterpart in TypeScript |
| 3 | **`Checker.TableOf` without a dictionary lookup** (the checker's copy of each binder table in a per-file array indexed by table, or on the symbol's slot) | after 1: 19.8 % of vscode's emit leaves (16 % before); estimate −1.5 to −2.5 s single, −0.4 to −0.6 s parallel on vscode (overlaps with 2); also helps the check (exports, members, locals) | low–medium | (a) |
| 4 | **Young garbage of the printer and its helpers**: `ComputeLineOfPosition` without a comparer delegate, `GetSourceTextOfNodeFromSourceFile` without the `FindAncestor` closure, `GetImpliedNodeFormatForEmitWorker` without `params` arrays (and its result cached per file), `TrySymbolTable` without per-call `Predicate`s, a reused `BinaryFrame` stack, the writer's indent without a concatenated string per line | about 0.6–0.8 GB of vscode's emit allocation (10–13 %), so about 60–80 fewer gen0 collections: −0.1 to −0.15 s pause parallel, plus −0.2 to −0.4 s CPU single-threaded; sentry −0.15 GB (module `params` arrays alone 122 MB) | low (each item local) | (a) |
| 5 | **Writer buffer reused**: one `char[]` (or `StringBuilder` with one chunk of the largest size seen) per writer, no reallocation in `Clear`; `GetText` copies once | −0.32 GB on vscode, −0.1 GB on sentry, of large arrays; −0.05 s | low | (a) |
| 6 | **Emit copy without managed pinned slabs**: the copy's slabs as native memory (the `Slab(native: true)` path exists) or reused per thread from file to file; the interned-text table copied instead of rehashed | removes about 1.5 GB of POH allocation on vscode (0.7 GB sentry), the `Slab` finalizers and part of `AllocateNewArrayWorker` (1–2 % of the emit samples); gen2 budget: fewer background collections; −0.1 to −0.2 s | low–medium | (a) |
| 7 | **Output as UTF-8 bytes in pooled buffers** instead of strings (the brief's example) | the output strings are 80–86 % of the emit's promoted bytes; halving them and keeping them out of the small-object heap: about −0.2 to −0.3 s pause on vscode parallel | medium; changes the `WriteFileCallback` contract and the driver's in-memory output | (b): output bytes identical, but the measured workload differs from typescript-go's (which hands strings); needs the author's decision and an amendment to §19 |
| 8 | **Overlap check and emit per checker** (no barrier between the pool's check and emit; each work item checks, then emits its files) | before 1: vscode −1.0 s; after 1: ≈ 0 on vscode and the tuning list | low in code; the driver's call order (`GetSemanticDiagnostics`, `GetGlobalDiagnostics`, `Emit`) changes | (b): global diagnostics read after a checker's emit may differ; needs the author's decision |
| 9 | **Work items per file** with the file's checker locked per resolver call (typescript-go's scheme) | before 1: upper bound vscode −1.8 s (span 9.14 → 7.37 s); after 1: −0.18 s (6.51 → 6.33 s); others ≤ 0.2 s | medium–high (locks, thread-safe emit state) | (b): the order of declaration emit within a checker changes, so the parallel `.d.ts` output and emit diagnostics may change |
| 10 | **Node builder's nodes in the emit store, fewer emit copies, pooled lists in the transformation context** (the brief's other examples) | small: the node builder allocates 220–300 MB on vscode, the transformation context 25–50 MB; there is already one emit copy per file | medium | (a) |

**Not candidates:** `promptCrafting.ts` (the type cache switched off by inference-fallback reports is TypeScript 6.0.3's behavior), the output strings themselves (kept by the in-memory `WriteFile` on both sides), Server GC (a configuration decision; already 0.70 s pause with emit).

**First step: candidate 1.** It is the largest measured gain, it is a faithful port of a cache both TypeScript 6.0.3 and typescript-go have, the experiment kept every output byte-identical on all six lists in both modes, and it is small. It removes most of the vscode difference (emit +6.5 s against typescript-go's +6.30 s) and with it the emit's load imbalance on vscode (idle 21.8 → 8.2 %), which makes candidates 8 and 9 nearly worthless. The next step would be 2 and 3 (the remaining accessibility search, re-profiled after 1); 4–6 are (a) and cheap but each worth 0.05–0.4 s, so they fit in one later step on allocation. 7–9 need the author.

## Measurements

- **Machine:** the reference machine ([hardware.md](hardware.md)), Windows 11, no other load during the runs (system CPU 0–2 % before each batch).
- **Drivers:** `node harness/bench/build-cs.mjs --tracks optimized --runtimes aot-ws,jit-ws` at 253c11d, copied to scratch (`base/`). Probe drivers: the same `dotnet publish` command into scratch (`probe/`), with the uncommitted probe; `artifacts/bin/parse-go.exe` (built in D7) for typescript-go.
- **Lists:** `artifacts/bench/filelists/{vscode,sentry,bluesky,playwright,tldraw,tuning-check-emit}.txt`.
- **Batch 1 (question 1, 3):** `run.mjs` interleaved, 5 runs (vscode 3): Track O `--check`, `--check --emit`, each also `--single`; typescript-go the same four with `GODEBUG=gctrace=1`.
- **Batch 2 (questions 1, 2, 4):** the probe AOT driver with `EA_PROBE=2` (phase marks with GC counts and thread ids, a polling thread recording every collection from `GC.GetGCMemoryInfo`, exclusive phase timers with `GC.GetAllocatedBytesForCurrentThread`, per-checker and per-file emit times), same configurations; then `EA_PROBE=3` (comments and script resolver calls timed too), 3 runs.
- **Traces (question 2):** `dotnet-trace collect --show-child-io --providers Microsoft-Windows-DotNETRuntime:0x1680001:5 --buffersize 4096 -- <probe jit-ws driver> --check --emit <list>` with `EA_PROBE=1`, vscode, sentry, bluesky; read by `acctool <trace> <probe log>`.
- **Profiles (question 3):** `dotnet-trace collect --profile dotnet-common,dotnet-sampled-thread-time -- <base jit-ws driver> --check --emit --single <list>`, vscode, sentry, tuning emit list; read by `acctool cpu <trace>`.
- **Experiment:** env `EA_CHAINCACHE=1` in the probe driver; identity with `--dump-emit` and `--dump-diagnostics` against the base driver (`eqcheck.sh`); timing batch 4, 5 runs (vscode 3), interleaved with the base driver and the probe driver without the cache.

**Scratch** (session scratch `…/fdf5d608-…/scratchpad/EA1/`, not in the repository): `tools/run.mjs` (interleaved runs), `tools/psum.mjs` (probe summary, pool balance, per-file simulation), `tools/b1sum.mjs`, `tools/batch1.sh`–`batch4.sh`, `tools/eqcheck.sh`, `acctool/` (CO5's tool extended for the emit, with the CPU reader `Cpu.cs`), `probe-src/` (the probe and its hooks as a patch), `logs/`, `p2/`, `p3/`, `p4/` (probe logs).

**Not done:** a profile of typescript-go's emit by function (its driver has no profiler hook); the comparison stays at wall time and `gctrace`.
