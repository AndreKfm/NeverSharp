# Phase 4: Track O emitter layout (module D0)

Decided in module D0 ([modules/D0.md](modules/D0.md), [phase4-plan.md](phase4-plan.md)). Modules D1–D6 add the transformers and may refine the details below.

## Where things live

| Part | TypeScript 6.0.3 | Track O |
|---|---|---|
| Entry point | `program.emit(sourceFile, writeFile)` → `emitWorker` | `Program.Emit(sourceFile, writeFileCallback)` in `src/NeverSharp.Optimized/Program/Program.Emit.cs`. Before emitting it checks the target file (or every file) with the program's checker, as `getEmitResolver(sourceFile)` does, so the checker state matches tsc's; the resolver itself is D1 |
| Emit host | `program.ts` `getEmitHost(writeFileCallback)` | `EmitHost` (`Emitter/EmitHost.cs`): options, current directory, source files, `IsEmitBlocked`, `WriteFile` (forwards to the callback). `WriteFileCallback(fileName, text, writeByteOrderMark)`, `EmitResult(EmitSkipped, Diagnostics)`, `EmitFileNames(JsFilePath, DeclarationFilePath)` |
| Blocked outputs | `hasEmitBlockingDiagnostics` (`blockEmittingOfFile` in `verifyEmitFilePath`) | `Program.hasEmitBlockingDiagnostics`, filled by `VerifyEmitFilePath` (TS5055 overwrite, TS5056 duplicate), read by `Program.IsEmitBlocked`. A JavaScript unit's `.js` output would overwrite its input, so its JS emit is skipped (`emitSkipped` true) while its `.d.ts` is still written |
| Files to emit, output names | `getSourceFilesToEmit`, `sourceFileMayBeEmitted`, `getOutputPathsFor`, `getOutputExtension`, `getDeclarationEmitOutputFilePath` | `Emitter.GetSourceFilesToEmit`, `SourceFileMayBeEmitted` (no declaration files, no JSON without outDir), `GetOutputPathsFor` (`.js`/`.jsx`/`.mjs`/`.cjs` via `Program.GetOutputExtension`, `.d.ts`/`.d.mts`/`.d.cts` via `Program.GetDeclarationEmitExtensionForPath`; no outDir, declarationDir or outFile) |
| emitFiles | `emitter.ts` `emitFiles`, `emitJsFileOrBundle`, `emitDeclarationFileOrBundle`, `printSourceFileOrBundle` | `Emitter.EmitFiles(host, targetSourceFile)` (`Emitter/Emitter.cs`): per file the JS output, then the declaration output, each printed into one shared `EmitTextWriter`, followed by a new line, written through the host and cleared. Script printer: `removeComments`, `newLine`, `target`; declaration printer: also `noEmitHelpers` and `onlyPrintJsDocStyle`. **The transformer lists are empty in D0**: the parse tree is printed. D1 adds `transformNodes` and the transformation context between "file to emit" and "print"; D2–D5 add their transformers in `getScriptTransformers` / `getDeclarationTransformers` order; declaration-transform diagnostics go into `emitterDiagnostics` and block the `.d.ts` |
| Comment state | `createPrinter`'s closure: `containerPos`, `containerEnd`, `declarationListContainerEnd`, `detachedCommentsInfo`, `hasWrittenComment`, `commentsDisabled` | fields of `Printer` in `Printer/Printer.Comments.cs` (with the comment functions). `commentsDisabled` starts as `PrinterOptions.RemoveComments`; `PipelineEmit` runs the notification handler (typeWriter's underlining), then `PipelineEmitWithComments` when comments are on and the node is not a source file, then the worker. Nested binary expressions save the container state in their stack frame (`BinaryFrame`) |
| Comment callbacks | closures passed to `forEachLeadingCommentRange` / `forEachTrailingCommentRange` | the enum `CommentCallback`, dispatched over `Scanner.CommentRangeIterator` (no allocation per range) |
| Options | `removeComments`, `newLine`, `declaration` | `CompilerOptions.RemoveComments`, `CompilerOptions.NewLine` (added in D0), `Declaration`; `PrinterOptions.OnlyPrintJsDocStyle` and `NoEmitHelpers` (added in D0). Track O's `PrinterOptions.RemoveComments` still defaults to true (the type printer's); the emitter passes the program's value |

## Not yet supported (and how it fails)

- `parenthesizeExpressionForNoAsi` of a partially emitted expression whose comments would put a new line after `return`/`yield` throws `NotSupportedException`: rebuilding the spine needs the node factory of the transformation (D1/D2). Parse trees never contain such expressions.
- Emit helpers (`EmitHelpers` throws when a node has any), generated names, substitution and emit notification hooks of transformers: D1.
- Custom transformers, `emitOnly`, `forceDtsEmit`, build info, source maps, `outFile`, `outDir`, `noEmit`, `noEmitOnError`, `emitBOM`: outside Gate D's options.

## Gate D harness

- Oracle `harness/astdump/dump-ts-emit.mjs`: `program.emit(unitFile, writeFile)` per unit, dump format in its header; coverage report `<out>.coverage.json` / `.coverage.txt` from a second, wrapped run of the default transformers on the same program after the dump.
- C# dump `tools/NeverSharp.Dump.Optimized emit` (`EmitDump.cs`), compare `harness/astdump/compare-emit.mjs` (sections js, dts, diagnostics incl. `emitSkipped`; with `--coverage` also the units that need no transformer), gate `harness/port/gate.mjs --track optimized --phase emit [--sample N]`.
