# BO1: binder allocations — brief

First optimization step of the Phase 2 binder ([phase2-plan.md](../phase2-plan.md)), after the binder endpoint (G = 1.207 [1.191, 1.219] vs tsgo7-bind, inconclusive; `results/phase2/binder-endpoint/2026-10-24T11-21-53-808Z/VERDICT.md`; later gains are reported as additional runs next to it, never instead of it). Track O only. One attempt (the two parts below complement each other); variant A is `main`. Written by the orchestrator. Standing rules of [README.md](README.md); Phase 2 rule: run only the tests and gates your change affects.

## Starting point

`main` at 542f27c. Single-threaded, Track O's parse+bind is as fast as typescript-go's (0.985); in parallel it is 1.21× slower because the binder allocates: vscode parse+bind 3,984 MB (parse only about 65 MB), 178 gen0 collections, 581 ms GC pause. Allocation profile (vscode, `--bind --single`, JIT, `dotnet-trace --profile gc-verbose`, `tools/NeverSharp.TraceSummary`; `artifacts/profile/b-vscode-alloc.nettrace` in the main checkout), 3,922 MB sampled:

| Share | Type | Likely source |
|---:|---|---|
| 64.8 % | `System.Byte[]` (2,539 MB) | `Arena/Slab.cs` managed slabs: the binder sets `parent` on every node; in Track O `parent` is a side-table field (`NodeStore.SetParent` → `SetSideInt` → `AddSide`), and side-table writes after `Commit` move the region to slabs the result owns (O10's design for rare post-commit writes), so every file grows a side table with one entry per node — verify |
| 7.1 % | `Int32[]` (277 MB) | binder store arrays / index buckets growing by doubling |
| 7.0 % | `SymbolRecord[]` (276 MB) | `BinderStore` symbols, `Array.Resize` doubling per file |
| 5.1 % + 5.1 % | `FlowNode[]`, `FlowRecord[]` (400 MB) | flow nodes and node maps |
| 3.5 % | `EntryRecord[]` | symbol-table entries |
| 2.3 % + 1.8 % + 1.0 % + 0.8 % | `DeclarationFields[]`, `Link[]`, `TableRecord[]`, `ContainerFields[]` | node maps and chains |

## Goal

1. **Parent pointers without the side table:** store `parent` where writing it for every node is cheap — a dense per-store parent array (one int per node handle, or per record word if handles are offsets) allocated once per bind in per-thread slab memory with the result's ownership rules, or a record word if that is cheaper overall. Reading `parent` stays correct for nodes the parser sets (`setParentNodes: true`, used by the dump tool and Gate 1), JSDoc nodes, and nodes created after commit. Measure parse-only too: it must not get slower (the parser does not set parents in the measured mode).
2. **Binder storage like the node store:** symbols, tables, entries, flow nodes, links and the node maps in per-thread slabs (or pooled scratch sealed with exact sizes, whichever measures better — O9 found slabs better than exact copies for nodes), sized from the file (node count) so that growth is rare; no per-file `Array.Resize` chains. Result ownership, independence from later parses, release with `SourceFile.Release()` and thread safety as in O9/O10.
3. Whatever else the allocation profile shows after 1 and 2. Target: parse+bind allocation in the same order as parse only (tens of MB on vscode, not GB).
4. `[NoAlloc]` on the binder's hot paths where it now holds (Track O Gate 2 checks it).

## What may change

`src/NeverSharp.Optimized/Binder/`, `src/NeverSharp.Optimized/Arena/` (parent storage, slabs), `tools/NeverSharp.Generate/emit-optimized.mjs` if the parent accessor is generated (rerun `generate.mjs`; generated files reproduced exactly), `tools/NeverSharp.Dump.Optimized` only if the binder dump needs adapting. Not Track F, drivers, harness, `eng/`.

## Tests to run

Gate B (`gate.mjs --track optimized --phase binder --jsdoc-mode all,types --main …`) after binder changes; the full Track O parser gate (`gate.mjs --track optimized --main …`) because `parent`/arena code changes; nothing else. Limit: 40 gate runs.

## Working

One attempt: Opus 5.5, **high effort** (optimization), no sub-agents, own worktree; scratch `<session scratch>/BO1/`. Junctions as in [O9.md](O9.md) (list them; unlink, never delete recursively). C# with the file tools only. Measure against a main driver built from 542f27c: tuning corpus `--bind` parallel and `--single`, vscode `--bind --single`, bytes allocated and GC counts; parse-only tuning parallel (must not regress); allocation profile before/after. Before the final report, stop every background task you started. Commit with `git commit -s`; do not push.

## Report

Model; gate runs (Gate B and parser gate summaries); the design (parent storage, binder storage, ownership); allocation before/after per type; your own timing against main (bind and parse-only); NoAlloc count; junctions; commit hash.

## Measurement (orchestrator)

Tuning corpus `cs-o-aot-ws-bind` (decides) and `cs-o-aot-ws-bind-single`, plus parse-only `cs-o-aot-ws`, 20 runs each, rig-compliant, against main. Merged if every gate passes, bind is not slower and parse-only is not slower by the 95 % interval.
