# PC1: per-checker speed, with tsrs's instruction cuts as the model — brief

Track O. **Author's decision of 2026-11-06:** after AR2, work on the speed of each checker. That is the part of the gap left under Server GC, and it helps both GC modes.

The author's standing direction holds:
- optimize now, conformity at the end;
- workloads of 30 s or less;
- many short steps rather than long runs;
- no full gates for now (a small Gate C sample suffices);
- report results as the factor against tsrs per repository.

Written by the orchestrator. The standing rules of [README.md](README.md) apply, except where this brief relaxes them.

## Why

After AR2, the factor against tsrs (geometric mean over the five repositories, `results/phase3/AR2/run1/`):
- **1.89** with `--pool tsrs` under Server GC (16 checkers); AR2 did not move it;
- **2.35** under Workstation GC (12 checkers).

With tsrs's scheduling (SC1), the remaining Server GC gap is mostly per-checker speed:
- TG1 ([tsrs-gap.md](../tsrs-gap.md) §2, §3) found that Track O and tsrs do the same work: counts within 1–4 %, and the four tsrs memos together worth 0.7–3 %.
- Track O's check phase is 1.45–2.47× slower per unit of work, geometric mean 1.98× single-threaded. At the same pool the factor is 1.97 on vscode.
- Since then:
  - RS1 (struct callbacks, pooled relaters);
  - RS2 (flow memo, −14 % on tuning-check single);
  - AR1 and AR2 (arenas, closures, transient strings).

**Profiles stay flat** (TG1, RS1, RS2): no single spot above about 3 %. Profile-driven hot-spot fixing is therefore exhausted, and this brief uses three other levers.

1. **tsrs's own instruction-count rounds.** tsrs documents output-preserving changes that cut instructions per operation, each with its exactness argument:
   - `notes/perf-icount-hillclimb.md`: 39 commits, −38.7 % instructions at 8 checkers;
   - `notes/perf-icount-round4.md`: −7.9 % single-threaded;
   - `notes/cpu-checker.md`: −9.5 %, including "Port divergences found (Go vs tsrs call counts)" and "Relation and instantiation caches (site-counts)";
   - `notes/perf-checker-cpu3.md`: −4.2 %;
   - `notes/perf-checker-algorithms.md`, `perf-union-inference.md`, `perf-flow-union-inference.md`, `perf-heavy-files-infer-memo.md`, `perf-t3code-instantiation.md`.

   TG1 rank 5 names the subset that maps onto Track O's layout. Only the flow memo (RS2) has been ported so far. The rest of that subset is open:
   - named-union membership by set;
   - the union front cache;
   - memo bits in `ObjectFlags`;
   - the IA memo;
   - the infer memo.
2. **Call-count comparison.** Count the calls of the main checker functions in Track O and in tsrs on the same workload (`cpu-checker.md` did this for Go against tsrs). A function called much more often in Track O shows a port divergence or a missing cache, which a flat profile hides.
3. **Representation costs named by TG1** (§2, "Design differences"):
   - `RelationKey` keeps a string for pairs of generic references, where tsrs uses a hash key without the key builder for non-references;
   - two memory accesses for most type reads (record plus kind side array), where tsrs keeps the payload behind the header;
   - type-argument clones.

   Take these only where the comparison or a count shows a share.

**Parallel loss.** Also measure how much slower a checker is at 16 checkers than alone (cache and memory traffic). Compare the CPU time per checker at 16 checkers with single-threaded CPU time for the same files, on Track O and on tsrs (`notes/perf-memory-traffic-32.md`). If Track O loses much more than tsrs, report it, together with the likely cause.

## Starting point

`main` at this brief's parent (d142b8d + this brief), which includes SC1, FE1, AR1 and AR2.
- **Checker:** `src/NeverSharp.Optimized/Checker/`.
- **Driver:** `tools/NeverSharp.ParseDriver/`.
- **Probes:** `NEVERSHARP_POOL_STATS=1`, `NEVERSHARP_PHASES=1`.
- **Tools from earlier modules,** in the session scratch; reuse them, do not modify them:
  - RS1's profiler `RS1/prof`;
  - EA1's `acctool`;
  - AR1's and AR2's measurement scripts `AR2/`.
- **The aot-ws build carries a gen0 budget of 32 MB (AR2 step 0).** For JIT runs that must match it, set `DOTNET_GCgen0size=2000000`.

**tsrs** is read-only at `external/tsrs/`:
- the notes above;
- `docs/CHECKER.md`;
- the code in `crates/tsrs_checker/`.

Find out whether tsrs's driver (`artifacts/bin/parse-rs.exe`, or its sources) offers statistics or counters that help the call-count comparison (for example environment switches named in the notes). If not, use counts from the code paths both sides share, such as types, relations and instantiations created, and say so.

## Goal

1. **Survey first** (about an hour, no code change):
   - **The applicable list.** Go through tsrs's instruction-count notes and list every change that maps onto Track O, with:
     - its tsrs gain;
     - its exactness argument;
     - whether Track O already has it (several may have come with the port or with RS1/RS2);
     - an estimate of the effort.
   - **The call-count comparison** on bluesky and playwright `--check --single` (and sentry if cheap). Take the functions with the largest ratio of Track O calls to tsrs calls.
   - **The parallel loss** on sentry and vscode at 16 checkers (Server GC).
   - Order the steps by expected gain per effort, from these three lists.
2. **One change per step.** Port tsrs's change (structure, memo or code shape) with its exactness argument, or fix the port divergence that the call counts show. A Track O-only change is allowed where tsrs has nothing better.
3. **Re-measure at the end:** the factor table against tsrs and typescript-go, the call-count comparison for the changed functions, and the parallel loss.

**Every ported structure:**
- names its tsrs source (note, file and function) in the code comment;
- marks itself as Track O / tsrs-only where it has no counterpart in TypeScript 6.0.3.

Where tsrs has a shadow mode for a memo (computing both ways and comparing), port it behind an environment variable, off by default, and run it once on the main workloads.

**Hard constraints:**
- Single-threaded check and emit output stays identical with tsc 6.0.3 (Gate C `--sample 200` after each code step).
- Type, symbol and signature creation order per checker stays tsc's, and ids stay what they are. A memo that skips work must not skip the creation of a type or symbol that is observable (Gate C's types and symbols sections).
- **With the default pool,** the parallel `--dump-diagnostics` stays identical with the starting driver's on sentry, bluesky and tuning-check.
- **With `--pool tsrs` under Workstation GC,** `--dump-diagnostics` stays identical on vscode and sentry. AR2 saw one rare run differ through stealing: retry a differing dump up to twice before calling it a difference.
- `--dump-emit` at the default pool stays identical on playwright and tldraw.
- Nothing is shared between checkers that was not shared before.

## Fast loop

**Workloads** (each run 30 s or less):
- **main:**
  - bluesky and playwright `--check --single` (aot-srv and aot-ws);
  - sentry and vscode `--check --pool tsrs` (aot-srv);
- **guard:**
  - sentry and vscode `--check --pool tsrs` (aot-ws);
  - tldraw `--check --pool tsrs` (aot-srv);
  - vscode `--check` with the default pool (aot-ws);
  - playwright `--check --emit --single` after every few steps;
- **for comparison:** `tsrs-check` and `tsgo7-check` in the same rounds.

**Measuring:** interleaved with `results/phase3/SC1/run1/inter.mjs` and `cmp.mjs`, previous step against this step, 5–7 rounds after 1 warm-up round, with `harness/bench/stats.mjs bootstrapMedianRatio`.
- **Keep a step** if the main workloads are faster or equal by the median, and no main or guard workload is slower by the 95 % interval.
- **Code placement exception:** AOT single-threaded runs have moved by 1–2.5 % from code placement alone (CO4, AR1, AR2). If only a single-threaded AOT workload is slower by the interval, and the JIT drivers at full optimization (`DOTNET_TieredCompilation=0`, 8 rounds) show no slowdown, the step may be kept. Disclose each such case.
- **Reject it otherwise.**
- **A step within noise** may be kept if it removes counted work (calls, instructions or allocations from a probe) and the median is not worse.

**Per step:**
1. Build aot-srv and aot-ws, with `WarningsNotAsErrors=NU1903` in the environment.
2. Run the dump checks of the hard constraints (seconds each).
3. Measure.
4. Commit each kept step at once.

| Gate | Run limit |
|---|---:|
| Gate C (`--sample 200` only) | 15 |

No full gates, by the author's decision.

## What may change

- `src/NeverSharp.Optimized/Checker/`;
- shared Track O types the checker uses, where a step needs it;
- `tools/NeverSharp.ParseDriver/`: probes and counters behind environment variables, off by default.

Must not change:
- Track F;
- `eng/`;
- the oracles, the gate harnesses, `bench.mjs` and `report.mjs`;
- the file lists;
- `docs/preregistration.md`;
- `external/` (read only).

## Working

- **Who:** one attempt with Opus 5.5 at medium effort, no sub-agents.
- **Where:** directly on `main`. Scratch: `<session scratch>/PC1/`. Build the starting drivers (aot-ws, aot-srv, jit-ws) into `<session scratch>/PC1/base/` first.
- **Editing C#:** with the file tools only (Edit/Write). No sed, no Python or other scripts on any source file, comments included.
- **Tools:** no downloads or installs.
- **Other processes:** never name, inspect or stop them. If the machine is busy, wait.
- **Commits:** `git commit -s` per kept step; do not push.
- **Size:** up to 12 kept steps, or about 7 hours. Stop earlier when three candidates in a row fail and the survey has nothing left above 1 %.
- **Before the final report:** stop every background task you started, and leave the tree clean.
- **Docs at the end:** a PC1 section in [phase1-progress.md](../phase1-progress.md), with:
  - the survey (the applicable list, the call-count comparison, the parallel loss);
  - one table row per step.

## Report

- **The survey:**
  - the applicable list of tsrs changes, with gain, status in Track O and effort;
  - the call-count comparison, top 15 by ratio;
  - the parallel loss of Track O and tsrs.
- **Each step:**
  - the change and its tsrs source;
  - its exactness argument;
  - the counted work removed;
  - the ratios with intervals against the previous step;
  - the commit.
- **Base against final:** the main and guard workloads, interleaved, 7 rounds, with the factor against tsrs and typescript-go per repository.
- **What is left,** by the survey.
- What was tried and rejected.
- The Gate C samples used, and the dump checks.
- The commit hashes.
- Every rule bent.

## Measurement (orchestrator)

- The five repositories, interleaved, 5 rounds:
  - base and final drivers with `--pool tsrs` (srv, ws);
  - the default pool (ws);
  - `--single` (ws);
  - `tsrs-check` and `tsgo7-check`.
- Reported as the factor against tsrs per repository, as in `results/phase3/AR2/run1/NOTE.md`.
