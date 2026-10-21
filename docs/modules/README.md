# Module briefs

One brief per module of [phase1-plan.md](../phase1-plan.md) §4, written by the orchestrator before any attempt starts (pre-registration §5.2): `M1.md`, `M2.md`, … with the generated unit lists `M<n>-units.md`. Attempts, gate results and rankings are logged in [phase1-progress.md](../phase1-progress.md).

## Standing rules for every brief from M3 on

Learned in M1 and M2; each new brief repeats them.

1. **Compiler and JIT hints are not optimizations with a counterpart.** `MethodImpl(AggressiveInlining)`, `SkipLocalsInit` and similar hints have no counterpart in typescript-go's code (port rule 3, strict reading decided by the author on 2026-10-21); they belong to Track O.
2. **A gate run is any run of a gate part over the whole suite or corpus.** Running the build, the unit tests, or a full dump or comparison by hand counts toward the brief's limit like a full gate run; probing single units or small sets with one's own tools does not.
3. **Own scratch folder.** Each attempt keeps helper files only in `<session scratch>/<module>-attempt-<n>/` and does not read other folders there.
4. **Write C# with the file tools.** Shell heredocs in this environment drop backslashes.
