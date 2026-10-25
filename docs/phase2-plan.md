# Phase 2: binder (Track O only) — plan

Phase 2 closed on 2026-10-25: summary in [phase2-summary.md](phase2-summary.md).

Decided by the author on 2026-10-24: Phase 2 ports the binder of TypeScript 6.0.3 (`src/compiler/binder.ts`, 3,916 lines; typescript-go's `internal/binder` about 3,600 lines of Go) **for Track O only**; Track F is not extended. Tests and gates are run only where a change affects them (binder changes run the binder gate; the parser gates run only when parser, scanner or store code changes). Pre-registration amendment: [preregistration.md](preregistration.md) §15 (`prereg/v7`, accepted and tagged 2026-10-24).

## What the binder produces

For every source file: symbols for declarations (`Symbol`: name, `SymbolFlags`, declarations, value declaration, `members`, `exports`, merged per declaration space), the symbol tables of containers (`locals`), the container chain (`nextContainer`), `node.symbol` / `node.localSymbol`, the control-flow graph (`FlowNode` with `FlowFlags`, antecedents, `flowNode`, `endFlowNode`, `returnFlowNode`, `fallthroughFlowNode` on the nodes that carry them), node flags the binder sets, and the bind diagnostics (`bindDiagnostics`, `bindSuggestionDiagnostics`).

## Gate B (binder equivalence)

- **Oracle:** `harness/astdump/dump-ts-binder.mjs` parses every equivalence unit with tsc 6.0.3 (`ts.createSourceFile`, the unit's script kind, `setParentNodes`) and binds it with `ts.bindSourceFile(file, options)` (options: `{}`; the binder derives strictness and target defaults itself), then writes a canonical binder dump: per node in tree order its symbol and local symbol (by stable id), the symbols (name, flags, declarations by node reference, value declaration, members and exports in insertion order), every container's locals in insertion order, the flow graph (flow nodes numbered in first-visit order from the nodes, with flags, node reference and antecedents), node flags after binding, and the bind diagnostics. Sections (`symbols`, `flow`, `diagnostics`) can be compared separately so modules can be gated on their part.
- **C# side:** the same dump from Track O's binder (a binder dump writer in `tools/NeverSharp.Dump.Optimized`).
- **Gate B:** byte-identical dumps for all 50,177 units in two JSDoc modes (ParseAll, and ParseForTypeErrors, the measured mode), per section as a module requires; `gate.mjs --track optimized --phase binder` runs only the binder gate (plus Track O Gate 2 build and headers).

## Modules (Track O, one attempt each unless a step compares designs)

| Module | Content | Gate |
|---|---|---|
| B0 | oracle, binder dump writer skeleton, compare, `--phase binder` in gate.mjs; Track O data layout decision for symbols and flow nodes (handles in per-thread slabs like the node store, or managed objects first) | the TS dump is deterministic (two runs identical); the C# skeleton compiles |
| B1 | symbols, declarations, containers and scopes, symbol tables, `declareSymbol` and merging, module instance state | Gate B section `symbols` + `diagnostics` (declaration errors) |
| B2 | control-flow graph (ported already in B1; verification and the missing flow fields moved to B3) | section `flow` |
| B3 | the rest: JavaScript special assignments (`module.exports`, `this.x =`, prototype assignments), JSDoc typedefs and callbacks, strict-mode and other bind diagnostics | all sections, both modes |
| B4 | parse+bind driver configurations (`cs-o-*-bind`, `tsgo7-bind`), measurement | — |

Afterwards, optimization steps as in Phase 1 (profile first, up to 3 variants per step, measured on the tuning corpus).

## Endpoint (draft, §15)

Parse+bind of every file: `cs-o-aot-ws-bind` against `tsgo7-bind` (typescript-go 7.0.2, `parser.ParseSourceFile` plus `binder.BindSourceFile` per file on its work group), the same G, interval and decision rule as §8, from the first protocol-compliant run after Gate B passes for B3; Windows only; tsrs reported only if it exposes a binder that can be called per file. Disclosed: typescript-go's binder follows TypeScript 7 and differs in places from 6.0.3's; the dumps are compared only against tsc 6.0.3.
