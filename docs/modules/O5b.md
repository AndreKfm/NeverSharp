# O5b: identifier interning lookup — brief

Step O5b of Track O ([track-o-steps.md](../track-o-steps.md), [phase1-plan.md](../phase1-plan.md) §7: "switch on length and first character; perfect hash; dictionary with span alternate lookup"), after the endpoint (later gains are reported next to G = 0.60, never instead of it). The keyword part was already done inside O23 (perfect hash in the scanner); this step is the interning of identifier and literal texts. One attempt; variant A is `main`. Written by the orchestrator before the attempt started. The standing rules of [README.md](README.md) apply. Runs in parallel with step O11 (speculation; `Parser/` and `Scanner/`), so change only what this brief allows.

## Starting point

`main` after O23 (commit edb712f). Profile after O23 (see [O11.md](O11.md)): `TextTable.Intern` 2.3 % self, 2.4 % inclusive; `Parser.CreateIdentifier` 0.7 %; `NodeStore.Create` 2.0 %. Interning (`Arena/Text.cs`: `TextTable.Intern`, the per-thread hash table from O9, buckets cleared per used entry) hashes and compares every identifier text of a file.

## Goal

1. Faster interning with the same results (the same `TextRef` identity semantics the parser and the dump rely on): ideas to evaluate, not prescribed — hash computed while the scanner scans the identifier (the scanner already walks every character; pass the hash with the token), cheaper hash for short identifiers, comparing by length and first characters before a full compare, a tag byte per bucket, better bucket layout.
2. If you pass a hash from the scanner, change the scanner only at the identifier paths and keep its API for other callers.
3. Behaviour byte-identical (Gate 1, G1a), thread safety, the NoAlloc contract.

## What may change

`src/NeverSharp.Optimized/Arena/Text.cs`, the identifier paths of `Scanner/` (`ScanIdentifier*`, token value bookkeeping) and of `Parser/` (`CreateIdentifier`, `InternIdentifier`). Keep these edits small: step O11 works on `Parser/` and `Scanner/` at the same time and its speculation code must still apply on top. Not the emitter, Track F, drivers, harness, tests' expectations, docs.

## Gate

```
<main>/.tools/node-24.21.0/node.exe harness/port/gate.mjs --track optimized --main D:/Private/Source/Andre/projects/NeverSharp
```

All Track O gates, 50,177/50,177; Track F's gate still passes. Limit: 30 gate runs.

## Working

One attempt: Opus 5.5, high effort, no sub-agents, own worktree; scratch folder `<session scratch>/O5b/`. Junctions as in [O9.md](O9.md) (list them). C# with the file tools only. Measure against a main driver built from edb712f: tuning corpus parallel and `--single`, vscode `--single`; an interning microbenchmark is welcome. Before the final report, stop every background task you started. Commit with `git commit -s`; do not push.

## Report

Model; last gate JSON line and gate runs; the design; your own timing and bytes against main; NoAlloc count and exceptions; junctions; commit hash.

## Measurement (orchestrator)

As for O10: tuning corpus `cs-o-aot-ws` and `cs-o-aot-ws-single`, 20 runs, rig-compliant, against main. Merged if every gate passes and it is not slower than main by the 95 % interval.
