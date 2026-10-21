# Hostile review of the protocol (Phase 0, task 5)

Status: 2026-10-21; additions decided (section 4). Reviewed: [preregistration.md](preregistration.md), [benchmark-harness.md](benchmark-harness.md), [phase1-plan.md](phase1-plan.md), [ast-dump-format.md](ast-dump-format.md), [hardware.md](hardware.md) at commit `c83b5fc`.

Two reviewers who want a particular answer:

- **The Go advocate** wants to discredit a C# parity result.
- **The .NET advocate** wants to explain away a C# loss.

For each attack: is it already blocked, and by what; if not, what to add. Proposed additions are collected in section 4 as **A1–A9**; none of them is adopted until the author decides.

Verdict legend: **blocked**, **partly blocked**, **open** (not blocked; needs an addition or must be stated as a limitation).

## 1. The Go advocate: "the C# parity result is not real"

| # | Attack | Verdict | Why / what to add |
|---|---|---|---|
| G1 | "Parsing is 10 % of a compile. Go's advantage is in the checker's parallelism and memory layout. Parity on the parser says nothing." | **partly blocked** | §1 limits the Phase 1 answer to parsing, but nothing stops a headline from dropping that qualifier. → **A3** wording rule. |
| G2 | "You measured Go through a driver you wrote, not through tsgo. Maybe your driver is slow." | **partly blocked** | The driver is ~30 lines around typescript-go's own `WorkGroup`, `osvfs` and `ParseSourceFile`, built with Microsoft's toolset and flags (B5), and published. Not shown: that it parses as fast as tsgo itself does. → **A6** cross-check against tsgo's own parse time. |
| G3 | "Windows. Go's runtime and scheduler are tuned on Linux; Windows thread and memory-commit costs hit Go harder." | **open** | The reference machine is Windows by necessity, and the system time of parallel tsgo is ~5× its wall-clock (benchmark-harness §8). Whether that is a Windows tax on Go is unknown. → **A4** Linux replication, or an explicit limitation. |
| G4 | "The optimized C# is hand-tuned with SIMD and arenas; Microsoft did not tune Go. Unfair." | **blocked** | Only Track F carries the verdict; Track O is always labeled "optimized track" and reported next to it, never instead (§1, §8). → **A3** makes the labeling a wording rule. |
| G5 | "You ran eight C# configurations and reported the best." | **blocked** | The primary configuration (NativeAOT, Workstation GC, parallel) is fixed before any C# exists (§6.4); secondary configurations need Bonferroni-adjusted intervals and are labeled secondary (§8). |
| G6 | "The file sets favor C#: they include tests and fixtures, e.g. an 8 MiB copy of checker.ts." | **partly blocked** | The corpus is exactly Microsoft's five repositories, commits by a rule fixed before looking (§3), files by `git ls-files` with no free parameter (B1). Both sides parse the same files. Residual: the parse set is not what `tsc -p` compiles. State as a limitation; a tsconfig-based subset would add exactly the free choices B1 avoids. |
| G7 | "On bluesky a run takes 0.1 s. That is mostly process start-up, and NativeAOT starts faster than a Go binary." | **open** | Both start-ups are in both numbers, but their size is unknown, so a reader cannot tell how much of a small-repo ratio is start-up. → **A2** fixed-cost run. |
| G8 | "The geometric mean hides that C# loses on VS Code." | **partly blocked** | Per-repository intervals are always reported (§8), but the parity sentence may still be written without them. → **A3**: a parity statement lists every repository whose own interval lies above 1.20. |
| G9 | "You relaxed the variance gate after seeing your data (B11), and raised the idle limit (B10)." | **partly blocked** | Disclosed with dates and reasons, including that B11's rationale was later qualified (§12.1, benchmark-harness §9); all happened before any C# code. It cannot be undone, only made inspectable: the deciding run reports MAD/median of the parallel configurations next to the verdict (already in §8). |
| G10 | "The C# side was picked from several AI-generated ports, the fastest one. That is optimization by selection, inside your 'faithful' track." | **open — real gap** | §5.2 ranks survivors "on the primary endpoint of section 8", which is measured on the **benchmark corpus**. Selecting on the corpus that is later judged is fitting to the test set. → **A1** (critical): rank on the tuning corpus only, as Track O already does. |
| G11 | "C# uses AVX2 behind runtime checks; the Go binary is built for GOAMD64=v1." | **blocked** | The Go standard library dispatches on CPU features at run time as well (e.g. byte searches); C# is restricted to runtime checks with the default baseline (§5.4 rule 2). Neither side assumes AVX2 at build time. |
| G12 | "The oracles differ: your C# reproduces TypeScript 6 behavior, Go implements TypeScript 7. Different work." | **blocked** | After normalization 50,092 of 50,177 units are identical; the 85 that differ are listed and categorized (§4.3). In the benchmark corpus (TS files only) the differences are 10 JSDoc-attachment cases and one error-recovery case in a fixture. The JSDoc work is the same in scope (ParseForTypeErrors vs typescript-go's lazy parsing with eager `@see`/`@link`). |
| G13 | "C# needs more memory; parity on time ignores that." | **partly blocked** | Peak working set and peak commit are secondary endpoints, always reported (§7). → **A3**: a parity statement on time states the memory ratio in the same sentence. |
| G14 | "You extended the budget twice (8 → 12 → 18 weeks)." | **blocked** | Before the tag, disclosed with reasons (§12.1). After the tag the budget is part of the protocol. |

## 2. The .NET advocate: "the C# loss is not real"

| # | Attack | Verdict | Why / what to add |
|---|---|---|---|
| N1 | "A line-by-line port of JavaScript-shaped code is unidiomatic C#. Of course it loses." | **blocked** | That is exactly what Track O answers: same source, same gates, optimizations allowed. The faithful comparison remains the pre-registered question, because typescript-go is a faithful port too. |
| N2 | "Wrong runtime settings: should have used Server GC, DATAS, ReadyToRun, a different tiering mode." | **partly blocked** | Four runtime/GC combinations per track are always reported (§6.4). What a configuration contains exactly (runtimeconfig values, ILC options) is not fixed yet, so "you forgot setting X" stays possible. → **A7** pin the full configuration, defaults unless stated. |
| N3 | "CLI wall-clock includes JIT start-up; measure steady state." | **blocked** | The question is about the compiler as users run it, so CLI wall-clock is primary; the primary configuration is NativeAOT, which has no JIT. In-process numbers are the supplementary BenchmarkDotNet table (§7). |
| N4 | "Windows with VBS and .NET's W^X mapping slow the JIT down." | **partly blocked** | Affects the JIT configurations, not NativeAOT (primary). VBS is left as found (hardware.md §3.4) and is the same for both sides. State as a known factor for the JIT rows. |
| N5 | "Small repositories measure start-up, where .NET is weak." | **open** | Same gap as G7, from the other side. → **A2**. |
| N6 | "Your C# driver reads files differently from the Go driver." | **partly blocked** | The driver contract fixes the protocol (priority, output, one work item per file, trees kept alive) but not how the C# driver reads and decodes files. → **A8** pin the driver behavior: read and decode like the source (`ts.sys.readFile` semantics: BOM detection, UTF-8 default), Track F with the counterpart API, Track O as one of its measured steps (O1). |
| N7 | "Track O ran out of time; not every optimization was tried." | **partly blocked** | Steps are ordered by profile and unfinished steps are reported as "not evaluated" (phase1-plan §7), so the gap is visible, not hidden. → **A9**: every Track O result lists evaluated and not evaluated steps. |
| N8 | "Variants were tuned on tests/cases: thousands of tiny synthetic files. Real code behaves differently, so the optimized track was tuned for the wrong input." | **open** | Plausible: tests/cases units are small and unusual. → **A5** add real-world code that is not in the benchmark corpus to the tuning corpus: TypeScript v6.0.3's own `src/**/*.ts`. |
| N9 | "Runtime ISA checks cost time in NativeAOT." | **blocked** | The author chose runtime checks to match typescript-go's `GOAMD64=v1` build (§5.4 rule 2); the cost is part of the comparison by design. |
| N10 | "Zen 2's two CCDs penalize the .NET thread pool more than Go's scheduler." | **open (limitation)** | One machine. Both runtimes run on the same topology, and SMT/affinity are left at defaults (hardware.md §3.2). State as a limitation; replication on other hardware is welcome and the harness is public. |
| N11 | "Gate 1 forces C# to reproduce TypeScript 6's quirks, e.g. the scanJsDocToken defect." | **blocked** | The defect only changes behavior on inputs that never terminate; it is not reached by any corpus file. typescript-go reproduces TypeScript's semantics just as strictly. |
| N12 | "Peak working set charges the .NET runtime's own footprint." | **blocked** | The same measurement for every process, including Go's runtime and Node for tsc; reported, not part of the verdict. |

## 3. Both camps

| # | Attack | Verdict | Why / what to add |
|---|---|---|---|
| B1 | "The author is a .NET architect with a stake in the answer." | **blocked as far as a protocol can** | Everything that decides the answer is fixed and published before C# exists; all changes before the tag are listed (§12.1); after the tag only dated amendments. The Go side is Microsoft's code. |
| B2 | "AI models wrote the code; quality depends on luck." | **partly blocked** | Every attempt and every failure is logged and published (§5.2); the models are fixed before Phase 1. After **A1**, selection does not touch the benchmark corpus. |
| B3 | "One machine, one OS." | **open (limitation)** | → **A4** or state it. |

## 4. Proposed additions

**Decided by the author 2026-10-21:** A1, A2, A3, A5, A7, A8, A9 adopted; A4 adopted as a Linux replication; A6 feasible and adopted (`harness/bench/crosscheck.mjs`; feasibility check: driver/tsgo parse time 0.72–1.05). All are now in [preregistration.md](preregistration.md) (§3, §5.2, §6.3, §6.4, §8, §11, §14); A2 is implemented in the harness.

| Id | Addition | Answers | Cost |
|---|---|---|---|
| **A1** | **Track F ranking on the tuning corpus.** §5.2: survivors are ranked on module-level speed on the tuning corpus, not on the primary endpoint. The benchmark corpus is used for nothing but the final measurement, in both tracks. | G10, B2 | text only; **critical** |
| A2 | **Fixed-cost run.** Each configuration also runs on an empty file list (process start-up, runtime init, driver overhead), reported per configuration next to every result. Not subtracted from anything; it lets a reader judge the small repositories. | G7, N5 | small: one extra parse set |
| A3 | **Wording rule** for every published claim: names the track; says "parsing" (Phase 1); gives G with its interval; lists every repository whose own interval lies above 1.20; states the peak-memory ratio in the same sentence. Template in §11 of the pre-registration. | G1, G4, G8, G13 | text only |
| A4 | **Linux replication** on the same hardware (native Linux boot, not WSL), after the Windows verdict, reported as a pre-registered secondary replication; or, if not done, "Windows only" stated as a limitation with every result. | G3, B3 | harness: env checks and peak memory on Linux (`/usr/bin/time -v`), a Linux install; ~6–8 h |
| A5 | **Tuning corpus extended** with TypeScript v6.0.3's own `src/**/*.ts` (real-world code, 601 files, 16.8 MiB, not in the benchmark corpus) next to `tests/cases`. | N8 | small |
| A6 | **Driver cross-check.** Compare the Go driver's parse time with tsgo's own reported parse time on the same files (e.g. `tsgo --extendedDiagnostics` with resolution turned off and an explicit file list). A large gap would mean the driver handicaps Go. Feasibility of a resolution-free tsgo run is not verified yet. | G2 | ~2 h, feasibility first |
| A7 | **Pin each C# configuration completely**: project and runtimeconfig properties (GC mode, concurrent GC, DATAS, tiering, ReadyToRun off), ILC options, all at .NET defaults unless the configuration name says otherwise; listed in the pre-registration. | N2 | text + project files |
| A8 | **Pin the C# drivers' file reading**: decoding semantics of `ts.sys.readFile`; Track F with the counterpart API; Track O as step O1. | N6 | text only |
| A9 | **Track O coverage list**: every Track O result lists the evaluated steps (with winner) and the steps not evaluated. | N7 | text only |

Not proposed, and why:

- Subtracting start-up time from results: it would turn a measured quantity into a modeled one. A2 reports it instead.
- A tsconfig-based parse set (G6): it reintroduces per-repository choices that B1 avoids.
- Turning off VBS or Defender: security changes to speed up benchmarks (hardware.md §3.3, §3.4), and they affect both sides.
