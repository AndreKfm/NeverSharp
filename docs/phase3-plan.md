# Phase 3: type checker (Track O only) — plan

Decided by the author on 2026-10-25: Phase 3 ports the type checker of TypeScript 6.0.3 (`src/compiler/checker.ts`, 54,434 lines; typescript-go 7.0.2's `internal/checker`, 26 files) in full, **for Track O only**; standing rules of Phases 1–2 apply (Track F not extended; run only the tests and gates a change affects). Draft pre-registration amendment: [preregistration.md](preregistration.md) §17 (`prereg/v9`, to be accepted and tagged before any checker measurement).

## Why this phase is different

The parser and binder work per file; the checker works per **program**: it resolves names across files, needs the default library (`lib.*.d.ts` of TypeScript 6.0.3), module resolution, compiler options, and it formats types into diagnostic messages (`typeToString`, which needs the node builder and the printer). Its output depends on whole programs, and partial ports cannot be gated per section the way the binder was. So the plan builds the program layer and the oracle first, then ports the checker in areas, and measures progress as the share of units whose checker output is already identical.

## Gate C (checker equivalence)

- **Unit:** every equivalence unit as a one-file program: the unit's file plus the default library of TypeScript 6.0.3, fixed compiler options (`{}` with `noLib: false`, `noResolve: true`, `allowJs: true` for JavaScript/JSX units (author, 2026-10-25; applied to the oracle after C1), the unit's script kind; TypeScript 6.0.3's defaults otherwise), no file system access beyond the unit and the library files. Units whose test of origin needs several files are checked alone; unresolved imports then report what tsc reports for them.
- **Oracle:** `harness/astdump/dump-ts-checker.mjs` runs tsc 6.0.3's `createProgram` with that host and writes a canonical dump: (1) **diagnostics** — `getSemanticDiagnostics` (and `getSuggestionDiagnostics`, `getGlobalDiagnostics`) in order with start, length, code, category and the flattened message chain; (2) **types** — in the style of TypeScript's own `.types` baselines (`src/harness/typeWriter.ts`): for every expression and declaration name in source order, its text and `typeToString` of its type (and symbol name where the baseline writes one). The library files' own diagnostics are written once, separately.
- **C# side:** the same dump from Track O's program and checker (`tools/NeverSharp.Dump.Optimized --checker`).
- **Gate C:** byte-identical dumps for all units, both sections; reported as identical / differing / crashing units per section so progress is visible while modules land; the phase is complete when all units are identical. Gate B and the parser gates run only when binder or parser code changes.

## Modules (Track O; order follows dependencies; one attempt each unless a step compares designs)

| Module | Content |
|---|---|
| C0 | oracle (diagnostics + types dump), compare, `gate.mjs --phase checker`; Track O program layer: compiler options, host, default library loading (parse+bind the lib files once per process and share them read-only), file set, `Program` with per-file parse/bind results; a checker skeleton and the C# dump writer; data layout decision for types, signatures, symbol links and node links (handles and per-checker arenas, following typescript-go's checker structure) |
| C1 | checker core: `initializeTypeChecker`, global symbol table and lib merging, intrinsic types, `resolveName`, `getSymbolOfNode`, symbol and node links, `getTypeOfSymbol`, declared types of classes/interfaces/enums/aliases, type references, unions/intersections, literal types |
| C2 | type printing: node builder (the printer itself is module P1, ported in parallel with C0) and printer subset needed by `typeToString` / `symbolToString` (diagnostic messages and the types dump depend on it) |
| C3 | instantiation and mappers, generics, conditional/indexed/mapped types, template literal types |
| C4 | relations: assignability, comparability, subtype, identity, structural comparison, relation caches, elaboration of diagnostic chains |
| C5 | expressions: `checkExpression` family, literals, operators, property access, calls and overload resolution, contextual typing |
| C6 | inference |
| C7 | control-flow analysis and narrowing (flow nodes from the binder) |
| C8 | statements and declarations: `checkSourceElement` family, classes and inheritance, interfaces, enums, modules and exports, grammar checks, unused checks |
| C9 | JavaScript and JSDoc typing, JSX, decorators, the remaining areas until Gate C passes |
| C10 | check drivers and measurement (below) |

Optimization steps after Gate C, as in Phases 1–2 (profile first).

## Measurement (draft, §17)

**Endpoint:** type-checking each benchmark repository's file list as one program with the same fixed options as Gate C (default library, `noResolve: true`), parse+bind+check, wall-clock: `cs-o-aot-ws-check` against `tsgo7-check` (typescript-go 7.0.2 creating the same program from the file list and calling the checker on every file through its own checker pool), G, interval and decision rule as §8; profile `check` (new, §16 style: the single-threaded variants as the variance gate's binding rows). Because the benchmark repositories' dependencies are not installed and imports are not resolved, the programs produce many diagnostics; both sides check the same programs with the same options, and the work done is reported (diagnostic counts per side) and compared. tsrs only if it exposes a callable checker. Windows only.

## Risks

- Size: the checker is about 2.8 times the parser, scanner and binder together; expect many modules and sessions.
- Message texts: every diagnostic message with a type in it depends on C2 (printing); types dumps depend on it everywhere, so C2 comes early.
- Ordering: the checker's results depend on the order types are created and cached (type ids, union ordering); Track O must create types in tsc's order — union and intersection member order follows type ids.
- Recursion depth and performance traps (deep instantiation, relation caches) must match tsc's limits exactly (the same "excessively deep" diagnostics).
