# TG1: account of the tsrs gap in the checker — brief

First step on the open item "the tsrs gap in the checker" ([report-v1.md](../report-v1.md) §11). It follows the closing of the checker optimization after CO6 (author, 2026-11-03).

This is an **analysis**: no change to `src/`, and nothing in it is a protocol result. Track O only. Written by the orchestrator. The standing rules of [README.md](README.md) apply.

## Starting point

`main` at this brief's parent (460b601). Last additional runs (after CO6), parse+bind+check of each repository's program:

| Ratio (G, five repositories) | 4 checkers / tsrs default | Single-threaded |
|---|---:|---:|
| Track O ws vs typescript-go | 0.970 | 0.800 |
| Track O ws vs tsrs | 4.906 | 1.685 |
| typescript-go vs tsrs | 5.057 | about 2.1 |

- Sources: `results/phase3/additional/2026-11-02T22-22-25-874Z/` (profile `check`) and `results/phase3/check12-additional/2026-11-02T20-56-02-668Z/` (profile `check12`).
- **vscode medians:**

  | | tsrs | Track O ws | Track O srv | typescript-go |
  |---|---:|---:|---:|---:|
  | parallel | 2.20 s | 14.04 s (4 checkers), 10.85 s (12) | 6.48 s (12) | 14.29 s (4), 8.76 s (12) |
  | single-threaded | 19.22 s | 36.81 s | | 42.68 s |

  Speedup from single-threaded to parallel: tsrs about 8.7×, Track O srv at 12 checkers about 5.7×, typescript-go at 12 about 4.9×.
- **tsrs's driver:** `harness/bench/rust/parse-rs/src/main.rs`, `--check`. It uses the program's own checker pool (`tsrs_compiler/src/checkerpool.rs`):
  - checker count: half the available parallelism, but every core up to 16;
  - files assigned by directory locality;
  - work stealing.

  `--single` sets `ProgramOptions.single_threaded`.
- **Diagnostic counts:** tsrs reports almost the same counts as typescript-go (Table 1c: vscode 101,941 vs 101,933). Track O checks TypeScript 6.0.3, tsrs and typescript-go check TypeScript 7.
- **Sources:**
  - tsrs at the pinned tag: `external/tsrs/`. Read `docs/CHECKER.md`, `docs/RUST.md`, `notes/cpu-checker.md`, `notes/design-shared-type-layer.md` and `notes/checker-11.md` first.
  - typescript-go: `external/typescript-go/`.
  - Track O: `src/NeverSharp.Optimized/`, layout in [phase3-checker-layout.md](../phase3-checker-layout.md).

## Questions

**1. Does tsrs do the same work?**
- Same files checked, including the default library.
- Same deferred checks.
- Does any part of checking short-circuit or skip work that typescript-go and Track O do? Examples: lazy checks, skipped bodies, a cache shared between checkers, `skipLibCheck`-like behaviour, persisted or precomputed library data.
- Use counts where possible: types, symbols, signatures and relation checks per side on one or two repositories, as far as each side can report them without changing committed code.

**2. Single-threaded, where does tsrs's factor of about 1.7 over Track O (and about 2.1 over typescript-go) come from?**
- Split by phase (parse, bind, check, diagnostics), then inside check by the largest parts.
- Name the design differences that explain it, with evidence: type representation and interning, arenas and ids, relation and instantiation caches, string and name handling, symbol tables, allocation and memory layout, and anything in `cpu-checker.md`.

**3. Parallel: why does tsrs scale about 8.7× and Track O about 2.6× (4 checkers, ws) to 5.7× (12, srv)?**
Separate:
- the checker count;
- the assignment (directory locality vs *i* mod N);
- work stealing vs a fixed assignment;
- duplicated per-checker work: globals merge, library types each checker creates again;
- memory and GC.

If the tsrs API allows it, run tsrs with fixed checker counts (4, 12, 16) and without stealing. Use an uncommitted switch in the Rust driver for this. On the Track O side, use the existing `--checkers N`.

**4. What could Track O take over?** A ranked list of candidates with an estimate of the gain and of the effort. Each must be marked as one of:
- (a) **keeps results:** single-threaded identical with tsc, parallel diagnostics unchanged;
- (b) **changes the parallel diagnostics:** any change of assignment or stealing. It would need the author's decision and an amendment before any protocol run;
- (c) **would break Gate C or tsc equivalence:** out of scope.

## Rules

- **No change to `src/`, `eng/`, the oracle, Gate C's harness, `bench.mjs`, `report.mjs`, the tuning lists or `docs/preregistration.md`.**
- **Measurement switches** in the drivers (Rust, Go, C#) are allowed only uncommitted and must be removed at the end. Build the Rust driver with `harness/bench/build-rs.mjs`, the C# drivers with `build-cs.mjs`.
- **Measurements are exploratory,** not rig-compliant protocol runs. Say so wherever a number is given.
  - Use medians of at least 5 runs.
  - Note when other load was present. Describe it generically; do not name, inspect or stop other processes.
- **Corpora:**
  - the five benchmark repositories;
  - the tuning-check list and tc1500/tc2500 (the first 1,500 and 2,500 lines of `artifacts/bench/filelists/tuning-check.txt`).
- **Profiling:** use only tools already on the machine or in the repository (for example `tools/NeverSharp.TraceSummary`, Windows Performance Recorder if present, tsrs's own timing output). No downloads or installs.
- **Who and where:**
  - One attempt with Opus 5.5, no sub-agents, directly in the main checkout.
  - Scratch: `<session scratch>/TG1/`.
  - Edit C# only with the file tools.
- **Before the final report:** stop every background task you started, and leave the working tree clean except for the deliverable.

## Deliverable

`docs/tsrs-gap.md`:
- the account, answering questions 1–4 with evidence (file and line references into `external/tsrs` and `src/`, and the measurements with their conditions);
- the ranked candidate list;
- what remains unexplained.

Commit it with `git commit -s` (noreply email as in earlier steps); do not push.

## Report

- Short answers to questions 1–4.
- The top candidates with category (a)/(b)/(c), estimated gain and effort.
- The measurements you made and under what load.
- Anything that contradicts a statement in [report-v1.md](../report-v1.md) §8 or §9.
- The commit hash.
