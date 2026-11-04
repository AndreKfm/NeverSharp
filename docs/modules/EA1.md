# EA1: account of the emit's allocations and time — brief

First step on the open item "the emitter: no optimization step was made" ([report-v1.md](../report-v1.md) §12, [phase4-summary.md](../phase4-summary.md)). The author asked for an account first, before any change (2026-11-04).

This is an **analysis**: no change to `src/`, and nothing in it is a protocol result. Track O only. Written by the orchestrator. The standing rules of [README.md](README.md) apply.

## Starting point

`main` at this brief's parent. The emitter endpoint is `results/phase4/emit/2026-11-03T19-39-41-764Z/` (profile `emit`, `prereg/v11`): G = 1.030 [1.026, 1.035], within 20 % of typescript-go. It stays the endpoint whatever follows.

Medians, parse+bind+check+emit to memory, 4 checkers. The check-only column comes from the last checker additional run, `results/phase3/additional/2026-11-02T22-22-25-874Z/`, a separate run:

| Repository | Track O ws: check → check+emit | typescript-go: check → check+emit | ratio ws / tsgo with emit |
|---|---|---|---:|
| vscode | 14.04 → 23.15 s | 14.29 → 20.18 s | 1.147 |
| sentry | 3.58 → 5.13 s | 4.80 → 6.31 s | 0.812 |
| bluesky | 0.95 → 1.23 s | 0.81 → 1.07 s | 1.153 |
| playwright | 1.73 → 2.18 s | 1.49 → 1.89 s | 1.153 |
| tldraw | 1.68 → 2.30 s | 1.95 → 2.45 s | 0.938 |

- **Single-threaded vscode:** Track O 36.81 → 62.21 s, typescript-go 42.68 → 65.26 s.
- **Allocation (Table 5):**
  - vscode ws: 13.4 GB, 3.31 s pause (check only) → 19.9 GB, 4.42 s pause (check+emit);
  - Server GC with emit: 0.70 s pause.

**Where the code is:**
- **Emitter:** `src/NeverSharp.Optimized/Emitter/` (transformers in `Emitter/Transformers/`), layout in [phase4-emitter-layout.md](../phase4-emitter-layout.md).
- **Printer:** `Printer/`.
- **Declaration emit in the checker:** `Checker/Checker.DeclarationEmit.cs`, `Checker.EmitResolver.cs`, `Checker.NodeBuilder*.cs`.
- **Pool:** `Program/CheckerPool.cs` (`Emit`), `Program/Program.Emit.cs`.
- **Driver:** `tools/NeverSharp.ParseDriver` (`--check --emit [--single] [--checkers N] [--dump-emit]`).

**Tools from CO5's account of the pause** (session scratch `…/fdf5d608-…/scratchpad/CO5/`, readable):
- `acctool/`: reads JIT ws traces from `dotnet-trace`, with GC verbose, `GCHeapSurvivalAndMovement` and `GCSampledObjectAllocationHigh`. It attributes the promoted bytes of every collection to type, allocation site and phase.
- `probe-src/`: an env-gated GC probe for the AOT drivers, which have no EventPipe.

`dotnet-trace` is in `.tools/dotnet-trace`. The method is described in [phase1-progress.md](../phase1-progress.md), "CO5", "Account of the pause".

## Questions

**1. What does the emit add?** Compare `--check` with `--check --emit`, on Workstation GC with 4 checkers and single-threaded, and report:
- wall time;
- allocated bytes;
- GC count;
- pause;
- promoted bytes.

Split the emit by phase:
- the transformers, each separately: TypeScript, ESNext, ES decorators, class fields, module, declarations;
- the declaration emit's node builder and resolver calls;
- the printer, comments included;
- turning output into strings, and the write callback.

**2. What survives?** Attribute the promoted bytes and the pause added by the emit to type, allocation site and phase, as CO5 did for the check. Separate:
- garbage that dies young (cheap);
- objects with references that survive a collection (expensive under Workstation GC).

Name the largest groups.

**3. CPU outside GC.** Where does the emit spend time single-threaded? Use a sampling profile of a JIT build. Compare with typescript-go's emit cost on the same list as far as its driver allows (wall time with and without `--emit`; `GODEBUG=gctrace=1`).
- Track O's emit costs more than typescript-go's on vscode: 25.4 against 22.6 s single-threaded, 9.1 against 5.9 s parallel.
- On the other four repositories it costs about the same or less. Explain the difference if you can. Is it something in vscode's files, such as large `.d.ts` outputs, deep declaration emit or many comments, or is it Track O's code?

**4. Parallel.** Is the emit phase's load balanced across the 4 checkers? (One work item per checker, files in program order; typescript-go uses one work item per file.) Report the idle time at the end of the pool.

**5. Candidates.** A ranked list, each with:
- an estimate of the gain (wall time and pause);
- the effort;
- one of these marks:
  - **(a)** keeps every output byte-identical (Gate D, the emit equivalence, Gate C, the printer gate);
  - **(b)** changes the parallel `.d.ts` output or diagnostics: needs the author's decision and an amendment;
  - **(c)** would break equivalence: out of scope.

Examples to check, not a list to confirm:
- the printer writing into pooled UTF-8 buffers instead of strings;
- the node builder's nodes in the emit store;
- pooled lists in the transformation context;
- fewer emit copies of the store;
- work items per file.

## Rules

- **Unchanged:** `src/`, `eng/`, the oracles, the gate harness, `bench.mjs`, `report.mjs`, the tuning lists, `docs/preregistration.md` and `results/`.
- **Measurement switches and probes** in the drivers or in `src/` are allowed only uncommitted, and must be removed at the end.
- **Measurements are exploratory,** not rig-compliant protocol runs. Say so wherever a number is given.
  - Use medians of at least 5 runs (3 on vscode).
  - Note when other load was present. Describe it generically; do not name, inspect or stop other processes.
- **Corpora:**
  - the tuning emit list (`emit-equivalence.mjs --corpus tuning-check` writes it; `harness/bench/tuning-emit-exclude.txt`) and small prefixes of it;
  - the five benchmark repositories, for the account only. Any later choice between variants is made on the tuning list (§5.4).
- **Tools:** only those already on the machine or in the repository. No downloads or installs.
- **Who and where:**
  - One attempt with Opus 5.5 at medium effort, no sub-agents, directly in the main checkout.
  - Scratch: `<session scratch>/EA1/`.
  - Edit C# only with the file tools.
- **Before the final report:** stop every background task you started, and leave the working tree clean except for the deliverable.

## Deliverable

- **`docs/emit-account.md`:** the answers to questions 1–5, with the commands and the scratch tools used. Commit it with `git commit -s`; do not push.
- **Report:**
  - the main numbers;
  - the ranked candidates;
  - which candidate you would take as the first step, and why.
