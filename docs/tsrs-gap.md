# The tsrs gap in the checker (TG1, analysis)

Brief: [modules/TG1.md](modules/TG1.md). This is an exploratory analysis, not a protocol result. No file under `src/` was changed. Track O only.

Every number below comes from exploratory measurements, not from rig-compliant protocol runs. Conditions are in [Measurements](#measurements).

## Short answers

1. **Same work: yes, within a few percent.**
   - With tsrs's lazy members switched off, both sides create almost the same numbers of types and checker symbols. On vscode, single-threaded:
     - types: tsrs 2,155,068, Track O 2,255,010;
     - checker or transient symbols: tsrs 4,798,401, Track O 4,855,353.
   - The same files are checked, the default library included.
   - The work that tsrs skips and Track O does not is worth at most 2–3 % of tsrs's single-threaded time. This covers lazy members and its four memos.
2. **Single-threaded, the whole gap is in the check phase.**
   - Parse, resolve and bind cost about the same on both sides (vscode: 4.50 s tsrs, 4.88 s Track O).
   - The check phase takes 14.70 s on tsrs and 31.75 s on Track O on vscode, so tsrs is 2.16× faster.
   - Over the five repositories the check-phase ratio is 1.45–2.47×, geometric mean 1.98×.
   - Track O does as many types and symbols as tsrs, so this is a cost per unit of work, not more work.
   - Contributing factors:
     - GC and allocation: about 5 % pause single-threaded, plus allocation slow paths and array clones in the profile;
     - two caches that TypeScript 6.0.3 has and Track O left out (`accessibleChainCache`, and the `isVisible` cache of `isDeclarationVisible`);
     - many tsrs-only representation and instruction-count changes;
     - code generation.
   - No sampling profile of tsrs was possible here, so the factor of about 2 is not split function by function (see [Unexplained](#what-remains-unexplained)).
3. **Parallel: under Track O's pool conditions, tsrs scales like Track O with Server GC.** The conditions are 4 checkers, file *i* to checker *i* mod 4, no stealing.
   - Speedup from single-threaded to 4 checkers on vscode:
     - tsrs 3.43×;
     - Track O srv 3.31×;
     - Track O ws 2.63×.
   - The rest of tsrs's 8.7× comes from:
     - more checkers (4 → 16: 1.76× on vscode);
     - directory-locality assignment (1.22×);
     - work stealing (1.18×).
   - Duplicated per-checker work is the same on both sides with the *i* mod N assignment: +34 % types at 4 checkers, +83–86 % at 16. Locality halves it (+44 % at 16).
   - The vscode gap at Track O's protocol conditions (ws, 4 checkers) is 6.3× against tsrs's default. It multiplies out as:
     - Workstation GC pause: 1.26×;
     - per-checker speed: 1.97×;
     - checker count: 1.76×;
     - locality: 1.22×;
     - stealing: 1.18×.
4. **Candidates for Track O.** The ranked list is [below](#4-candidates-for-track-o-ranked).
   - The largest results-keeping (a) levers:
     - the Workstation GC pause in parallel runs: up to 1.26× at 4 checkers and 1.7× at 12–16;
     - the per-checker check speed. Cheap first items are the left-out `accessibleChainCache`, the parallel front end, and fewer clones and temporary lists.
   - Locality assignment, more checkers than 12 and stealing all change the parallel diagnostics, so they are (b).
   - Stealing with deterministic output needs tsrs's "canonical" caches. Those change single-threaded output against tsc in rare cases, so that part is (c).

## 1. Does tsrs do the same work?

### Same program

- Both sides check one program per repository with TypeScript 6.0.3's default options and `noResolve`.
  - tsrs's driver: `harness/bench/rust/parse-rs/src/main.rs`, `check_options`.
  - Track O's driver: `tools/NeverSharp.ParseDriver/CheckMode.cs`, `ProgramOptions`.
- The program file counts are equal: vscode 11,807, sentry 9,014, bluesky 1,770, with the default library on both sides.
  - tsrs uses its bundled TypeScript 7 library (`bundled::wrap_fs`). Track O uses TypeScript 6.0.3's.
  - The bundled library is text that tsrs parses at run time. It is not a persisted or pre-parsed image: `external/tsrs/docs/PERSISTED_FRONTEND.md` says "design, not built".
- Library files are type-checked on both sides. Neither driver sets `skipLibCheck` or `skipDefaultLibCheck`; see tsrs `tsrs_compiler/src/program.rs:1577` `skip_type_checking`.
- Diagnostic counts: on vscode, single-threaded and parallel, tsrs reports 101,941, and its toggles below do not change that.
- The driver does not turn on tsrs's CLI-only savings, which only the CLI's own `--noEmit` check enables:
  - lazy declaration-file member lists: `fileregions.rs:107-125`, `enable_lazy_dts` is called only from `tsrs_execute/src/execute.rs:443`;
  - leaf-file freeing;
  - checker recycling.

### What tsrs skips or shortcuts, measured

tsrs has five switchable work savings that tsgo and Track O do not have:
- **lazy members:** `tsrs_core/src/lazymembers.rs:20-24`, a port of TypeScript PRs #64475 and #64526, which are not in 6.0.3. It instantiates the members of class and interface references, and of `keyof` mapped types, one at a time when they are needed;
- **union front cache:** `tsrs_checker/src/unioncache.rs`;
- **indexed-access memo:** `unioncache.rs:60-69`;
- **inference-walk memo:** `tsrs_checker/src/infermemo.rs`;
- **flow memo:** `tsrs_checker/src/flowmemo.rs`.

`TSRS_CHECKER_ASSIGNMENT=go` turns on Go's check history (`tsrs_core/src/compat.rs`), which switches the union cache, the IA memo and the infer memo off.

tsrs single-threaded, hyperfine medians of 5 (exploratory):

| | default | `TSRS_LAZY_MEMBERS=0` | all four memos off | Go history + lazy off + flow memo off |
|---|---:|---:|---:|---:|
| vscode wall (s) | 19.39 | 19.76 (+1.9 %) | 19.54 (+0.7 %) | 19.81 (+2.1 %) |
| sentry wall (s) | 8.23 | 8.26 (+0.4 %) | 8.30 (+0.8 %) | 8.29 (+0.7 %) |
| bluesky wall (s) | 1.27 | 1.27 (0 %) | 1.31 (+3.0 %) | 1.30 (+2.6 %) |

Counts from the same runs (single-threaded; `checker_symbols` is tsrs's checker symbol count, Track O's is its transient symbols):

| | tsrs default | tsrs lazy off | Track O |
|---|---:|---:|---:|
| vscode types | 2,154,962 | 2,155,068 | 2,255,010 |
| vscode checker symbols | 1,881,616 | 4,798,401 | 4,855,353 |
| vscode instantiations | 2,745,226 | 2,746,020 | (not counted in Track O) |
| sentry types / symbols | 682,758 / 543,389 | 682,767 / 731,664 | 677,686 / 734,285 |
| bluesky types / symbols | 131,700 / 93,639 | 131,700 / 119,169 | 132,837 / 120,706 |
| binder symbols (vscode) | 2,075,314 | | 2,075,320 |

- Lazy members remove 61 % of the checker symbols on vscode but only 1.9 % of the time.
- With lazy members off, the type and symbol counts of tsrs and Track O differ by 1–4 %. tsrs creates fewer types partly because of TypeScript 7 changes and its shared tuple element parameters (`notes/perf-shared-tuple-elements.md`, on in every mode).
- **So tsrs does the same checking work, and no switchable shortcut explains more than about 3 % of its time.**

Not switchable, and so not measured here: many always-on changes in tsrs that keep output identical but cut instructions per operation.
- `notes/perf-icount-hillclimb.md` reports −38.7 % instructions on a 38k-file codebase over 39 commits. Examples:
  - named-union membership through one pointer set (−11.3 %);
  - long keys in the union front cache (−7 %);
  - memo bits in spare `ObjectFlags`;
  - inline fast paths.
- `notes/cpu-checker.md` reports −9.5 %.
- `perf-checker-cpu2/3.md` report further cuts.
- They cannot be told apart from implementation speed without a profiler on tsrs, so they count under question 2.

### Deferred checks

tsrs runs the same deferred node checks as Go. Its tsrs-only deferred type-argument checks are switched off under Go history (`checker_02.rs`, referenced in `notes/design-shared-type-layer.md` §3.1). The counts above match with Go history on and off, so deferred checks do not explain the difference.

## 2. Single-threaded: where tsrs's factor comes from

### By phase

The drivers carried an uncommitted switch, `NEVERSHARP_TG1_PHASES`, that timed the phases. The diff is kept in the scratch folder.
- tsrs: `new_program` (parse + resolve), `bind_source_files`, `get_semantic_diagnostics`.
- Track O: parse, `Program.Create` (with the default library's parse and bind), the `CheckerPool` constructor (import resolution + bind), `GetSemanticDiagnostics`.

The table gives medians of 5 interleaved runs per side, `--single`, AOT ws for Track O.

| repository | tsrs front end (s) | Track O front end (s) | tsrs check (s) | Track O check (s) | check ratio | Track O GC pause (s, whole run) |
|---|---:|---:|---:|---:|---:|---:|
| vscode | 4.50 (3.71 + 0.79) | 4.88 (2.62 + 0.13 + 2.13) | 14.70 | 31.75 | 2.16 | 1.68 |
| sentry | 2.60 | 2.45 | 5.46 | 7.92 | 1.45 | 0.52 |
| bluesky | 0.51 | 0.50 | 0.70 | 1.72 | 2.47 | 0.10 |
| playwright | 0.52 | 0.55 | 1.33 | 3.29 | 2.47 | 0.15 |
| tldraw | 0.73 | 0.69 | 1.98 | 3.18 | 1.61 | 0.19 |

- The front end costs the same on both sides, so the whole single-threaded gap is in checking, with a geometric mean of 1.98×.
- The global-diagnostics phase is negligible on both sides (under 1 ms).

### Inside check (Track O)

A CPU sample of Track O was taken with dotnet-trace (already installed) and summarized with a scratch tool on TraceEvent (already cached):
- JIT ws build, `DOTNET_TieredCompilation=0`, playwright, `--single`;
- samples restricted to `CheckSourceFileOnce`.

The JIT build's check phase took 6.0 s against 3.3 s for AOT, so the shares are indicative only.

- The profile is flat. The largest exclusive entries are:
  - `Thread.PollGCWorker` (the GC suspension and allocation slow path) 8.0 %;
  - `Object.MemberwiseClone` (array clones such as `(Type[])types.Clone()` in `Checker.Instantiate.cs:549`, `Checker.Generics.cs:471`) 3.9 %;
  - `Buffer.Memmove` 2.8 %;
  - `GetAccessibleSymbolChain`'s `TrySymbolTable` 3.1 %;
  - `NameIdOf` 2.0 %.
- By declaring type: dictionaries in total about 3 %, link stores about 1 %.
- Inclusive: the relater 24.7 %, `ResolveCall` 20.3 %, flow typing 8.2 %.
  - **Type printing for error messages (`TypeToString`) is 10.6 %**, of which `GetAccessibleSymbolChain` is 6.3 %.
  - These repositories are error-rich because their dependencies are not installed: vscode has 101,941 diagnostics.
- **Track O left out caches that TypeScript 6.0.3 has.** `Checker.SymbolAccessibility.cs:13-15` says "The accessibleChainCache is left out (a cache of the pure result)" and also leaves out the `isVisible` cache of `isDeclarationVisible`.
  - TypeScript 6.0.3 has the first: `typescript.js` line 54934, `links.accessibleChainCache`.
  - typescript-go has it too (`symbolaccessibility.go:455-474`).
  - So does tsrs (`printer.rs:1425-1449`).
  - This is the one concrete, faithful, cheap item that the profile shows.

### Design differences that explain the rest

The evidence is from the code and from tsrs's own notes. None of these could be timed separately here.

- **Memory management.**
  - tsrs never frees checker data during a check. It keeps leak arenas per thread, compressed 32-bit handles `P<T>` (`docs/PORTING.md:72-74`) and mimalloc (the driver's `#[global_allocator]`).
  - Track O runs on .NET's collector.
    - Single-threaded the pause is 5 % of the run (vscode 1.68 s).
    - Allocation slow paths (8 %) and clones (4 %) also show in the profile.
    - The checker allocates 603 MB on bluesky single-threaded (driver JSON line) and 13.4 GB on vscode at 4 checkers (report-v1 §8).
  - tsrs's notes also find allocation churn and cache misses, not instructions, to be the largest remaining cost (`notes/cpu-checker.md`, "What remains").
- **Type representation.**
  - tsrs: one 24-byte header with the kind's payload allocated right behind it in the same block (`docs/CHECKER.md` "Types"); slices are `&'static [T]` returned without copies (`docs/RUST.md` rule 4).
  - Track O: a 4-word record in a type store plus kind data in chunked side arrays (`phase3-checker-layout.md`), which is two memory accesses for most reads.
  - Several instantiable kinds are still objects, and type-argument lists are still copied (`Clone` above).
- **Relation keys.**
  - tsrs: Go's `keyBuilder` with xxh3-128 hashes, and no key builder for non-references ("relation keys for non-references without the key builder", hillclimb).
  - Track O: `RelationKey` keeps a string for pairs of generic references (`Checker.Relations.cs:94-111`) in a `Dictionary` (`Checker.Relations.cs:86-92`).
- **Names and symbol tables.**
  - tsrs compares names by pointer and length, with a Bloom filter per small table (`notes/perf-symbol-map-lookups.md`).
  - Track O maps names to ids per checker (`NameTable`) and reads binder tables through a cached checker copy (`phase3-checker-layout.md`, "Symbol tables"). `NameIdOf` is 2 % exclusive.
- **Closures.** Track O passes many `Func<>` delegates on hot paths (`MapType(Type, Func<Type,Type>)`, relater callbacks: the `DisplayClass` entries in the profile). tsrs's closures are monomorphized generics (`impl FnMut`) that LLVM inlines.
- **Code generation.** tsrs here: rustc 1.99.1, fat LTO, one codegen unit, x86-64 baseline, **without PGO**. tsrs's own release builds add PGO (−13–14 % instructions, `notes/perf-pgo.md`) and BOLT on Linux, so the tsrs measured here is slower than the shipped one. Track O: NativeAOT x86-64-v2.
- **Instruction-count rounds.** tsrs's notes document several rounds of output-preserving CPU changes after the port:
  - `cpu-checker.md`: −9.5 %;
  - `perf-checker-cpu3.md`: −4.2 % single-threaded;
  - `perf-icount-hillclimb.md`: −38.7 % at 8 checkers;
  - `perf-icount-round4.md`: −7.9 % single-threaded.

  Each was measured on tsrs's own corpora. tsgo is 2.1× slower than tsrs single-threaded, and Track O is 0.80× tsgo, so Track O sits between them.

## 3. Parallel scaling

The tsrs driver got a second uncommitted switch, `NEVERSHARP_TG1_CHECKERS=N`, which sets `CompilerOptions.checkers`. tsrs's own environment variables gave the assignments; the first two come from `tsrs_compiler/src/checkerpool.rs`:
- **steal:** tsrs default: directory-locality assignment (`locality_associations`, :2039) plus work stealing (`stealing_enabled`, :1214) and heavy files first (:1248).
- **loc:** locality without stealing, `TSRS_CHECKER_ASSIGNMENT=locality`. Naming an assignment turns stealing off.
- **mod:** file *i* to checker *i* mod N without stealing, `TSRS_CHECKER_ASSIGNMENT=file:<list>`. This is typescript-go 7.0.2's and Track O's assignment (`CheckerPool.cs:52`; `external/typescript-go/internal/compiler/checkerpool.go:115-117`). tsrs's own `go` mode uses FENNEL, a later tsgo, so it was not used for this.
  - Caches stay in tsrs's canonical mode.

On this 24-thread machine, tsrs's default checker count is 16 (`default_checkers_for_parallelism`, :1973).

The tables give hyperfine medians of 5 runs (1 warmup), in seconds.

**vscode** (single-threaded: tsrs 19.39, Track O ws 36.97)

| N | tsrs steal | tsrs loc | tsrs mod | Track O ws | Track O srv |
|---:|---:|---:|---:|---:|---:|
| 4 | 4.58 | 4.87 | 5.66 | 14.07 | 11.16 |
| 12 | 2.34 | 2.62 | 3.27 | 10.99 | 6.54 |
| 16 | 2.24 | 2.64 | 3.22 | 11.75 | 6.74 |
| tsrs default (16) | 2.23 | | | | |

**sentry** (single: tsrs 8.23, Track O ws 10.58)

| N | steal | loc | mod | ws | srv |
|---:|---:|---:|---:|---:|---:|
| 4 | 1.83 | 1.98 | 1.94 | 3.58 | 2.85 |
| 12 | 0.98 | 1.04 | 1.08 | 2.82 | 1.83 |
| 16 | 0.96 | 1.02 | 1.01 | 2.89 | 1.76 |

**bluesky** (single: tsrs 1.27, Track O ws 2.33)

| N | steal | loc | mod | ws | srv |
|---:|---:|---:|---:|---:|---:|
| 4 | 0.32 | 0.35 | 0.40 | 0.94 | 0.83 |
| 12 | 0.23 | 0.26 | 0.29 | 0.76 | 0.66 |
| 16 | 0.23 | 0.26 | 0.30 | 0.81 | 0.62 |

**Speedup from single-threaded on vscode:**

| N | tsrs mod | tsrs loc | tsrs steal | Track O srv | Track O ws |
|---:|---:|---:|---:|---:|---:|
| 4 | 3.43 | 3.98 | 4.23 | 3.31 | 2.63 |
| 12 | 5.93 | 7.40 | 8.29 | 5.65 | 3.36 |
| 16 | 6.02 | 7.34 | 8.66 | 5.48 | 3.15 |

**Duplicated work** (vscode, one run per cell, types summed over checkers; with *mod* the counts are deterministic):

| | single | 4 | 12 | 16 |
|---|---:|---:|---:|---:|
| tsrs mod | 2.155M | 2.889M (+34 %) | 3.688M (+71 %) | 3.954M (+83 %) |
| tsrs loc | | 2.483M (+15 %) | 2.895M (+34 %) | 3.104M (+44 %) |
| tsrs steal | | 2.490M | 2.931M | 3.180M (+48 %) |
| Track O (*i* mod N) | 2.255M | 3.075M (+36 %) | 3.927M (+74 %) | 4.205M (+86 %) |
| Track O transient symbols | 4.86M | 6.50M | 7.94M | 8.38M |

**Track O ws, check phase and GC pause on vscode:**

| N | check (s) | GC pause (s) | check − pause (s) | tsrs mod check (s) | ratio without pause |
|---:|---:|---:|---:|---:|---:|
| 4 | 12.69 | 3.25 | 9.43 | 4.93 | 1.91 |
| 12 | 9.38 | 5.14 | 4.24 | 2.55 | 1.66 |
| 16 | 10.21 | 5.95 | 4.26 | 2.37 | 1.80 |

How each factor contributes:
- **Checker count.** At the same assignment, going from 4 to 16 checkers gives tsrs 1.76× on vscode, 1.92× on sentry and 1.33× on bluesky. Track O srv gains about as much from 4 to 12 (1.71×) and loses a little from 12 to 16. tsrs *mod* also stops gaining at 12 → 16 (3.27 → 3.22), so past 12 the duplicated work and the slowest checker cancel the extra cores on both sides.
- **Assignment.**
  - Locality against *i* mod N at 16 checkers: vscode 1.22×, bluesky 1.15×, sentry 0.99× (none).
  - Locality creates 21 % fewer types at 16 checkers on vscode (3.10M against 3.95M) and 14 % fewer at 4.
  - The *i* mod N assignment duplicates the same share of types in Track O as in tsrs, so Track O would gain the same from locality, plus less garbage-collected surviving data.
- **Work stealing** against static locality: vscode 1.18× at 16, sentry 1.06×, bluesky 1.13×.
  - It also costs duplicated work (+2.4 % types against loc at 16).
  - tsrs can steal only because its default output does not depend on which checker checks which file (`notes/perf-order-independence.md`; `compat.rs`). This needed "canonical" variants of several typescript-go caches, such as `get_undefined_property` keyed by sibling property (`checker_09.rs:2255-2270`).
- **Duplicated per-checker work.**
  - Both sides merge the globals and create library types once per checker: tsrs in `new_checker`, Track O in `InitializeTypeChecker` (`Checker.cs:187`).
  - tsrs parses and binds the lazy member lists of the global library in parallel before the checkers start (`fileregions.rs:145` `force_shared_lists`). With the driver's eager parsing this does nothing, since lazy declaration parsing is off.
  - The duplication rates are equal on both sides (table above), so duplication explains none of the per-checker difference. It does explain why neither side scales past about 6× with *i* mod N.
- **Memory and GC.**
  - With Workstation GC, Track O's pause grows from 3.25 s to 5.95 s from 4 to 16 checkers, and its time goes up from 12 to 16 checkers.
  - Without the pause, Track O's check phase at the same assignment is 1.66–1.91× tsrs's, close to the single-threaded 2.16×.
  - Server GC removes most of the pause: srv against ws is 1.26× at 4 checkers and 1.68–1.74× at 12–16 on vscode.
  - tsrs has no collector. Its per-thread arenas are never collected during the check.
- **Front end in parallel.**
  - tsrs parses, resolves and binds vscode in 0.41–0.50 s.
  - Track O takes 0.26 s to parse, 0.11 s for the program walk and 0.62–0.69 s for import resolution and binding (`CheckerPool` constructor), about 1.0 s in all. Single-threaded both front ends cost the same.
  - About 0.5 s of vscode's parallel wall time is in Track O's parallel resolve and bind. That is 4 % at ws 4 but 8 % of srv 12's 6.54 s and 23 % of tsrs's whole run.

**The vscode gap at Track O's protocol conditions (ws, 4 checkers, 14.07 s) against tsrs's default (2.23 s) is 6.3×.** It multiplies out (14.07/11.16 · 11.16/5.66 · 5.66/3.22 · 3.22/2.64 · 2.64/2.24) as:

| factor | vscode | sentry | bluesky |
|---|---:|---:|---:|
| Workstation GC pause (ws/srv at 4) | 1.26 | 1.26 | 1.13 |
| per-checker speed at equal pool (srv 4 / tsrs mod 4) | 1.97 | 1.47 | 2.08 |
| checker count 4 → 16 (tsrs mod) | 1.76 | 1.92 | 1.33 |
| locality (mod/loc at 16) | 1.22 | 0.99 | 1.15 |
| stealing (loc/steal at 16) | 1.18 | 1.06 | 1.13 |
| product (= ws 4 / tsrs default) | 6.29 | 3.73 | 4.09 |

## 4. Candidates for Track O, ranked

The gain is the estimated wall-time factor on the configuration named. The effort is a rough size.

| rank | candidate | category | estimated gain | effort |
|---:|---|---|---|---|
| 1 | **Shrink the Workstation GC pause in parallel runs:** the remaining surviving objects (inference contexts kept by the mapper store, signature parameter arrays, the instantiable kinds' data classes, diagnostics, module-resolution strings: report-v1 §11), and checker data in per-thread native arenas as the parser and binder have | (a) | up to the ws/srv gap: 1.26× at 4 checkers, 1.7× at 12 on vscode | high (CO-style steps, Gate C each) |
| 2 | **Port the left-out `accessibleChainCache`** (TypeScript 6.0.3 `getAccessibleSymbolChain`), and check the left-out `isDeclarationVisible` cache the same way | (a), faithful | up to about 6 % of check on error-rich repositories (playwright profile: 6.3 % inclusive); less where few types are printed | low |
| 3 | **Allocation churn in the check:** type-argument and type-list copies (`Clone()` 3.9 %, `Memmove` 2.8 % in the profile) returned as stored lists, as tsrs did (`notes/cpu-checker.md` changes 6–8), and fewer `Func<>` delegates on hot paths | (a) | several % single-threaded, more under Workstation GC in parallel | medium |
| 4 | **Parallel front end:** find why import resolution + bind take 0.62–0.69 s in parallel on vscode against 2.13 s single-threaded (3.2×). tsrs needs 0.41–0.50 s for parse + resolve + bind | (a) | about 0.5 s on vscode: 4 % at ws 4, 8 % at srv 12 | low to medium |
| 5 | **tsrs's output-preserving instruction cuts**, the subset that maps onto Track O's layout: named-union membership by set, the union front cache with its exactness rules, the memo bits in `ObjectFlags`, the IA memo, the infer and flow memos. tsrs's are documented with exactness arguments and shadow modes | (a), each must pass Gate C | per item 0.5–3 % (on our repositories, tsrs's own four memos together: 0.7–3 %); union membership −11 % instructions on tsrs's monorepo, unmeasured here | medium per item |
| 6 | **Lazy members** (TypeScript #64475/#64526, not in 6.0.3) | (a) only if Gate C stays identical, otherwise (c); tsrs claims identical output | single-threaded about 2 % time; −61 % checker symbols (memory and surviving data) | high |
| 7 | **Directory-locality assignment** (`locality_associations`: directory subtrees packed with FENNEL) | (b) | vscode 1.22× at 16 checkers, 1.15× bluesky, none on sentry; −14 % to −21 % duplicated types, so also less GC work | medium (about 300 lines) |
| 8 | **More checkers than 12** (§18 has 12) | (b) | none on Track O at 16 against 12 (vscode srv 6.74 against 6.54, ws worse) until 1 and 7 land | trivial |
| 9 | **Work stealing with heavy files first** | (b) as scheduling, but deterministic output needs (c) | 1.06–1.18× at 16 after locality | medium for the scheduler, high with exactness |

Notes on the categories:
- Rank 9 as a scheduler alone is (b), and the parallel diagnostics would then change from run to run. Assignment-independent output needs tsrs's canonical caches (`notes/perf-order-independence.md`). Those differ from tsc single-threaded in rare cases ("E-tsgo-1" exceptions in `notes/design-shared-type-layer.md` §2), and that part is (c).
- What Track O can take over without the author's decision is ranks 1–6.
  - On vscode, ranks 1 and 2–4 bear on the 1.26× and the 1.97× factors of the decomposition.
  - The 1.76 × 1.22 × 1.18 of checker count, locality and stealing (2.5×) needs (b) decisions.

Not candidates:
- **Server GC as the primary configuration.** It changes the registered configuration rather than Track O.
- **A persisted or shared default library.** tsrs does not have one either. Its own design notes find a shared type layer cannot be both exact and wall-neutral (`notes/design-shared-type-layer.md`, verdict).

## What remains unexplained

- **The per-unit-work factor of about 2 in the single-threaded check is not attributed function by function.**
  - The tsrs driver is built with `strip = "symbols"`. No sampling profiler for native code could be installed, and the rule forbids one.
  - The Track O profile comes from a JIT build whose check phase is 1.8× slower than AOT's.
  - It names allocation, clones, the left-out cache and a flat rest. Which share of the rest is code generation (LLVM fat LTO against NativeAOT), memory layout or tsrs's instruction-count work is open.
- **Why the check ratio varies from 1.45× (sentry) to 2.47× (bluesky, playwright).** Diagnostics per file do not explain it: sentry has 26 per file and bluesky 19.
- **The parallel front end** (candidate 4): it was not split into resolution and binding.
- **Not measured:**
  - tsgo's phase split;
  - per-checker CPU times (the slowest checker's share);
  - the tuning-check, tc1500 and tc2500 lists;
  - playwright and tldraw in parallel.

## Measurements

- **Machine:** Windows 11, 24 logical processors; drivers at high priority (`NEVERSHARP_PRIORITY=high`).
- **Load:** before each series the median CPU busy share over ten 1 s windows was 3.0–7.6 % (scratch `CN1/idle.mjs`). No other heavy load was noticed.
- **Spread:** hyperfine standard deviations were 0.2–0.5 % on the long runs.
- **Dates:** 2026-11-03, 04:50–05:45.
- **Builds:**
  - The C# AOT drivers (ws, srv) and the JIT ws driver were rebuilt from `main` at e3a7465 (src unchanged since 460b601) with `build-cs.mjs`, plus the uncommitted phase switch.
  - The tsrs driver was rebuilt with `build-rs.mjs` (offline, pinned rustc 1.99.1, release profile: fat LTO, no PGO) with the two switches.
  - When neither switch is set, both switches only add a few clock reads.
  - The bench binaries in `artifacts/bin` were restored from copies afterwards, and both driver sources were reverted.
- **Wrappers:**
  - tsrs commands ran under Git's `env.exe` to set variables, which adds a few ms to every tsrs command, its default included.
  - Track O commands ran directly.
- **Medians:**
  - hyperfine: 1 warmup and 5 runs per command;
  - phase splits: medians of 5 interleaved runs, wall from the shell;
  - counts: one run each (deterministic except under stealing).
- **Profile:** dotnet-trace (`Microsoft-DotNETCore-SampleProfiler`), JIT ws build, `DOTNET_TieredCompilation=0`, playwright `--single`, 3,180 samples inside `CheckSourceFileOnce`.
- **Scratch:** `<session scratch>/TG1/`: `phases.log`, `toggles.log`, `par-*.log`, `hf/*.json`, `counts-vscode.log`, `prof/`, and `drivers.patch` (the measurement switches).

## Against report-v1 §8 and §9

- **§8**, "The tsrs gap in the checker ...": "tsrs reports almost the same diagnostic counts as typescript-go, so it does the same checking work. It runs 16 checkers with work stealing, compared with fixed assignment".
  - The first part is confirmed with counts: types and symbols agree within 1–4 % once lazy members are off.
  - The second part is incomplete as an explanation. **At typescript-go's pool (4 checkers, *i* mod 4, no stealing) tsrs is still 2.49× faster than Track O ws and 1.97× faster than srv on vscode.** About half of the gap, in log terms, is per-checker speed, not the pool.
  - tsrs's pool also differs in its assignment (directory locality), which §8 does not name.
- **§9**, "Checker pools: ... tsrs uses 16 checkers with work stealing": correct for this 24-thread machine. Two things are missing:
  - the locality assignment;
  - that tsrs's default output does not depend on the assignment, by design (`notes/perf-order-independence.md`), apart from known cases. So for tsrs the sentence "With more than one checker, the diagnostics depend on the assignment ... the same in typescript-go" holds only in its `go` mode.
- **§9**, "Code generation: each side is built as its project builds it": the driver build of tsrs has fat LTO but not the PGO (and Linux BOLT) of tsrs's release builds. The tsrs measured is therefore slower than the shipped tsrs by up to about 13 % (tsrs's own measurement).
