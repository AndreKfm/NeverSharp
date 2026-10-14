# Canonical AST dump and oracle agreement (Phase 0, task 1)

Status: measured 2026-10-14. Decisions 1–5 in section 7 confirmed by the author 2026-10-14.
Code: [harness/astdump/](../harness/astdump/). Reproduce with `node harness/bench/filelists.mjs` (once, for the benchmark file set), then `node harness/astdump/run.mjs` (about 2.5 min on the reference machine).

Updated 2026-10-14 (same day): the sources of the five benchmark repositories were added to the corpus, as the brief requires. This changed the counts in sections 1, 2, 5, 6 and the size of the divergence list in decision 2 (74 → 85 units). It did not add a rule or a category.

## 1. Result in one paragraph

On the 50,177 parse units of the equivalence corpus (23,910 from `tests/cases/**`, 26,267 source files of the five benchmark repositories), **no** unit is byte-identical between tsc 6.0.3 and typescript-go 7.0.2 as dumped raw. After nine normalization rules, each of which maps a documented representation difference and changes nothing else, **50,092 units (99.83 %) are identical**: 28,131,392 nodes plus parse diagnostics. The remaining **85 units (0.17 %)** all fall into named categories (16 labels, 13 groups in section 6) of intentional, error-recovery or JSDoc-attachment differences in typescript-go. 73 of them involve erroneous input; on error-free input, 11 units differ in JSDoc attachment and 1 in top-level-await context flags. None is unexplained. On `tests/cases/**` alone: 23,836 identical, 74 residual. The brief's requirement that all three dumps be byte-identical therefore cannot hold for typescript-go; section 7 restates the gate and the kill criterion.

## 2. Corpus

| | |
|---|---|
| Source A | `tests/cases/**` at TypeScript `v6.0.3` (`050880ce…`), 19,585 files |
| Splitting (A) | `tests/cases/compiler` and `tests/cases/conformance` are split into units at `// @filename:` directives, following `TestCaseParser.makeUnitsFromTest` (`src/harness/harnessIO.ts`). Option lines (`// @key: value`) and symlink lines (`// @link:`) are removed, lines are joined with `\n`, exactly as the harness does. All other directories (`fourslash`, `project`, `projects`, `transpile`, `unittests`) contain one unit per file. |
| Source B | The parse-only benchmark file set of the five benchmark repositories at their pinned commits: every git-tracked `.ts .tsx .mts .cts` file ([benchmark-harness.md](benchmark-harness.md), section 3). One unit per file, named `<repo>/<path>`. |
| Decoding | `ts.sys.readFile` semantics (UTF-8, UTF-8 BOM, UTF-16 LE/BE BOM), then written as UTF-8 for both dumpers |
| File name | `ts.getNormalizedAbsolutePath(unitName, "/")` (typescript-go requires normalized absolute names) |
| Script kind | by extension: `.ts .mts .cts` → TS (`.d.*ts` → declaration file), `.tsx` → TSX, `.js .mjs .cjs` → JS, `.jsx` → JSX, `.json` → JSON |
| Units | **50,177**. A: 23,910 (TS 20,116, TS declaration 1,127, TSX 457, JS 1,284, JSX 15, JSON 911). B: 26,267 (TS 15,343, TS declaration 344, TSX 10,580). |
| Skipped | 8 units of A with non-source extensions (`.map` 3, `.txt` 2, `.css` 1, `.md` 1, no extension 1). No unit was skipped for encoding reasons; no harness prefix errors. |

Known limitation: `fourslash/` files are parsed as the test scripts they are (TypeScript source with `////` comment lines), not as the embedded `////` content.

## 3. Parse configuration

| | tsc 6.0.3 | typescript-go 7.0.2 |
|---|---|---|
| Entry point | `ts.createSourceFile(fileName, text, options, setParentNodes=false, scriptKind)` | `parser.ParseSourceFile(opts, text, scriptKind)` |
| Language version | `ScriptTarget.Latest` | not a parser option; the scanner defaults to `ScriptTargetLatest` |
| JSDoc | `JSDocParsingMode.ParseAll` (the `createSourceFile` default) | eager in JS files, lazy in TS files (parsed on first access; `@see`/`@link` eager) |
| Module detection | default: `isFileProbablyExternalModule` | `ExternalModuleIndicatorOptions{}` → `isFileProbablyExternalModule` |
| Build | npm `typescript@6.0.3`, integrity checked against pins.json | `go build -overlay` inside the pinned checkout with Go 1.26.9; the checkout is not modified |

tsc 6.0.3 runs each unit in a worker thread with a 10 s timeout because of the known non-terminating path in `scanJsDocToken` ([references.md](references.md), section 2). **No unit timed out**, so that defect is not triggered by this corpus.

## 4. Dump format, version 0

```
<indent><Kind> <pos> <end>[ <Flag>|<Flag>...]     one line per node, pre-order, 2 spaces per depth level
!diag <code> <start> <length>                     parse diagnostics, emission order
!jsdiag <code> <start> <length>                   JS-only grammar diagnostics (typescript-go only)
!jsdocdiag <code> <start> <length>                JSDoc parse diagnostics
```

- **Children** are exactly what each implementation's own child visitor yields (`ts.forEachChild`, `ast.Node.ForEachChild`). Neither visits JSDoc comments, so JSDoc nodes are not in the tree. JSDoc *type syntax* inside regular code (`?number`, `function(string)`) is.
- **Kind** is the enum member name, not its number: the numeric values differ between the two implementations. For TypeScript, the first declared name per value is used (aliases such as `FirstToken` are skipped). For Go, `Kind.String()` without the `Kind` prefix.
- **Flags** are NodeFlags names, sorted. Contextual aliases are resolved per node kind: on `Identifier`, the `ContainsThis` bit is `IdentifierHasExtendedUnicodeEscape` and the `HasAsyncFunctions` bit is `IdentifierIsInJSDocNamespace`; in Go, on `ModuleDeclaration`, the `OptionalChain` bit is `NestedNamespace`.
- **Derived attributes** are prefixed with `=`. They carry information that one side stores as a NodeFlag and the other in a field: Go `ModuleDeclaration.Keyword` → `=Namespace` / `=GlobalAugmentation`; TypeScript `node.jsDoc` non-empty → `=HasJSDoc`.
- **Positions** are UTF-16 code unit offsets. TypeScript (and C#) strings are UTF-16; typescript-go uses UTF-8 byte offsets, which the Go dumper converts with a per-unit offset table. Diagnostic `start` and `length` are converted the same way.

The dumpers write raw output. All normalization happens in the comparer, so the raw dumps stay inspectable and every rule is counted on its own.

## 5. Normalization rules

Applied cumulatively, in this order. "Units changed" counts units the rule altered on either side, whether or not they end up identical.

| Rule | What it maps | Identical after | Units changed | Nodes removed |
|---|---|---|---|---|
| raw | | 0 | | |
| N1 | Kind name: Go `EndOfFile` = TS `EndOfFileToken` | 38,554 | all 50,177 | 0 |
| N2 | Derived attributes become flags: `Namespace`, `GlobalAugmentation` (Go field), `HasJSDoc` (TS `node.jsDoc`) | 48,855 | TS 8,598, TSX 2,193, JS 574, JSX 3 | 0 |
| N3 | Nested namespace `A.B`: Go adds an implicit `ExportKeyword` modifier flagged `Reparsed` to the inner declaration; TS sets `NestedNamespace` on it | 48,951 | TS 95, TSX 2 | 233 |
| N4 | Array binding hole: Go childless `BindingElement` in `ArrayBindingPattern` = TS `OmittedExpression` | 49,346 | TS 322, TSX 82 | 0 |
| N5 | JSDoc cast in JS (`/** @type {T} */ (x)`, `@satisfies`): unwrap the `AsExpression`/`SatisfiesExpression` that typescript-go's reparser wraps around the expression (`makeNewCast`, the one reparser construct without the `Reparsed` flag) | 49,351 | JS 44 | 446 |
| N6 | Remove Go nodes flagged `Reparsed`: TypeScript nodes synthesized from JSDoc by typescript-go's reparser (`@typedef`, `@param {T}`, `@import`, …). TypeScript keeps that information in JSDoc, which is not in the tree. | 49,844 | JS 500, JSX 3 | 6,528 |
| N7 | Drop TS flag `IdentifierHasExtendedUnicodeEscape`. typescript-go defines the constant but never sets it; TypeScript uses it only for the ES5-target error, and ES5 is gone in 7.0. | 49,852 | TS 7, TSX 1 | 0 |
| N8 | Drop TS flag `HasAggregatedChildData`: the memoization bit of `aggregateChildData` (utilities.ts); parser.ts comments list it among the "cached flags", it is not parse output | 49,853 | TS 1 | 0 |
| N9 | Drop JSDoc-derived deprecation flags: TS `Deprecated` (set from a parsed `@deprecated` tag) and Go `PossiblyContainsDeprecatedTag` (set from a text scan). Different semantics; both belong to JSDoc, which v0 does not compare. | 50,092 | TS 171, TSX 70, JS 1 | 0 |

Rule classes:

- **Encoding (lossless):** N1–N4 and the UTF-16 position conversion. The same information, represented differently; the mapping is one-to-one.
- **JSDoc scope:** N5, N6, N9. They follow from JSDoc not being part of the v0 dump. N5 and N6 change **only JS/JSX units**; after N3, no TS/TSX unit contains a `Reparsed` node, so N6 cannot hide a difference in TypeScript files.
- **Removed or internal information:** N7, N8. Information one side does not have (N7) or that is not parse output (N8).

Diagnostics compared: only the `!diag` section.

- `!jsdiag` (typescript-go only; 173 diagnostics in 65 units, all in source A) is excluded. typescript-go reports JS-only grammar errors from the parser; TypeScript reports the same checks from `program.getJSSyntacticDiagnosticsForFile`, outside the parser.
- `!jsdocdiag` (TS 41 in 27 units; Go 87 in 54 units, JS only because TS-file JSDoc is lazy in Go) is excluded together with JSDoc.

## 6. Residual divergences

85 units differ after N1–N9 (74 from `tests/cases/**`, 11 from the benchmark repositories). Each difference is assigned to a category; a unit can carry several. The full list is [harness/astdump/oracle-divergences.tsv](../harness/astdump/oracle-divergences.tsv). `compare.mjs` fails if the current result deviates from it in any way.

| Category | Units | What differs | Example |
|---|---|---|---|
| `diag-1127-length` | 28 | "Invalid character": same code and start, TS length 0, Go length 1 | compiler/constructorWithIncompleteTypeAnnotation.ts |
| `error-recovery-ThisNodeHasError` | 19 | `ThisNodeHasError` set on different nodes for the same erroneous input and the same diagnostics (e.g. `super<T>()`) | compiler/superWithTypeArgument.ts |
| `error-recovery-node-range` | 11 | Different range for a recovery node: zero-width missing identifier in TS vs. spanning the string literal in Go (`import { "x" }` without `as`); JSX attribute without initializer ends 1 character later in Go | conformance/es2022/arbitraryModuleNamespaceIdentifiers/… |
| `diag-2880-import-assert-in-parser` | 10 | typescript-go reports "Import assertions have been replaced by import attributes" from the parser; TypeScript reports it from the checker (`checker.ts` grammar checks). Go also marks the node `ThisNodeHasError`. | compiler/importAssertionsDeprecated.ts |
| `diag-1003-js-jsdoc-reparse` | 10 | JS files with JSDoc `@enum` / `@typedef` tags: typescript-go additionally reports "Identifier expected" (TypeScript reports nothing in the parser) | compiler/jsEnumCrossFileExport.ts |
| `error-flag-js-jsdoc-reparse` | 10 | Same units: the error flag that goes with that diagnostic | compiler/jsEnumCrossFileExport.ts |
| `jsdoc-attachment` | 12 | A JSDoc comment is attached to a different node (`HasJSDoc`), e.g. a `/** … */` comment inside a type literal before a property (`{/** Relative */ fileName: string}`): typescript-go attaches it to the `PropertySignature`, TypeScript does not. 10 of the 12 units are VS Code sources, all error-free. | compiler/commentsVarDecl.ts, bench/vscode/…/fileLoading.ts |
| `jsdoc-type-syntax-in-ts-file` | 2 | JSDoc-only type syntax in a TS file: typescript-go has no `JSDocUnknownType` (lone `?`) and parses `function(...)` types differently; reports 1110 etc. instead | compiler/expressionWithJSDocTypeArguments.ts |
| `missing-token-context-flags` | 2 | Missing (zero-width) `CommaToken` in a JSX file carries `JavaScriptFile` in TS, not in Go | compiler/jsFileCompilationTypeArgumentSyntaxOfCall.ts |
| `diag-go-only (1110 / 1005 / 1109 / 1128)` | 2 | Diagnostics that go with `jsdoc-type-syntax-in-ts-file` | conformance/jsdoc/jsdocDisallowedInTypescript.ts |
| `diag-2458-amd-removed` | 1 | "An AMD module cannot have multiple name assignments": reported by the TypeScript parser (pragma processing); typescript-go defines the message but never reports it (AMD removed in 7.0) | compiler/amdModuleName2.ts |
| `top-level-await-context` | 1 | After the top-level-await reparse, typescript-go sets `AwaitContext` on statements that TypeScript leaves unflagged | conformance/externalModules/topLevelAwait.1.ts |
| `diag-position (1453)` | 1 | `resolution-mode` error in a triple-slash reference points to a different offset | conformance/node/nodeModulesTripleSlashReferenceModeOverrideModeError.ts |

Observations:

- 73 of the 85 units have at least one parse diagnostic on either side, i.e. the difference is in error handling. On error-free input, 12 units differ: 11 `jsdoc-attachment`, 1 `top-level-await-context`. The one benchmark unit with an error-recovery difference is VS Code's 8 MiB colorizer fixture `test-checker.ts`, a copy of `checker.ts` that contains a syntax error.
- None of these is a normalization candidate: each is a real behavioral difference between the two compilers, however small.

## 7. Proposed protocol decisions

**[DECISION 1, CONFIRMED] The C# port gate compares against tsc 6.0.3 only, raw.** The C# dump must be byte-identical to the tsc 6.0.3 dump on all 23,910 units, with no normalization and with the `!diag` section (plus `!jsdocdiag` if JSDoc is in scope, see the Phase 1 plan). The C# port is a port of TypeScript 6.0.3, so it must reproduce 6.0.3's representation, including the flags typescript-go dropped. One mismatch fails the module, as in the brief.

**[DECISION 2, CONFIRMED] Oracle agreement is defined as TS = Go after N1–N9, except the pre-registered units.** All inputs are pinned, so this result is deterministic: 50,092 identical, 85 in the categories of section 6, listed unit by unit in `oracle-divergences.tsv`. (Confirmed with 74 units on `tests/cases/**`; the count rose to 85 when the benchmark sources were added the same day, with no new category.) The C# port is additionally reported against the Go dump under the same normalization, as a cross-check.

**[DECISION 3, CONFIRMED] Restate kill criterion 1.** The brief says: stop if the AST dump cannot be made identical between tsc 6.0 and typescript-go. Taken literally, it fires today: zero raw matches, 85 residual units. Proposed wording: *"Stop and publish if the comparison of tsc 6.0.3 and typescript-go 7.0.2 shows any difference that is neither removed by a pre-registered normalization rule nor listed in the pre-registered divergence list."* The rule list and the divergence list are frozen with the pre-registration tag; changing either is an amendment.

**[DECISION 4, CONFIRMED] JSDoc content is not in dump v0.** Only the presence of a JSDoc comment (`HasJSDoc`) is compared. Reasons: neither child visitor includes JSDoc; typescript-go parses JSDoc in TS files lazily and turns JSDoc in JS files into synthesized TypeScript nodes. The final in/out decision for v1 belongs to the Phase 1 plan (task 4). If JSDoc is in scope, the C# gate (decision 1) adds a JSDoc section compared against tsc 6.0.3 only, since typescript-go's JSDoc representation is not comparable node for node.

**[DECISION 5, CONFIRMED] `fourslash/` files are parsed as whole files.** Extracting their `////` content would be a second splitting rule of our own. As test scripts they are valid TypeScript and exercise the parser as they are.
