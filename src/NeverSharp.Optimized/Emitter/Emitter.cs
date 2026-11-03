// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/emitter.ts (emitFiles, forEachEmittedFile, getOutputPathsFor, getOutputExtension,
// emitJsFileOrBundle, emitDeclarationFileOrBundle, printSourceFileOrBundle) and src/compiler/utilities.ts
// (getSourceFilesToEmit, sourceFileMayBeEmitted, getOwnEmitOutputFilePath, getDeclarationEmitOutputFilePath)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/compiler/emitter.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; Gate D's options only (no outFile/bundles, outDir, declarationDir,
// emitDeclarationOnly, noEmit, source maps, declaration maps, build info, emitBOM). Module D0: the script and
// declaration transformer lists are empty, so the parse tree is printed (later modules add their transformers).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>emitter.ts: emitFiles and the output names of a source file.</summary>
internal static class Emitter
{
    /// <summary>
    /// utilities.ts getSourceFilesToEmit(host, targetSourceFile): the target file (or all files) that may be emitted
    /// (no outFile).
    /// </summary>
    internal static List<SourceFile> GetSourceFilesToEmit(EmitHost host, SourceFile? targetSourceFile)
    {
        var result = new List<SourceFile>();
        if (targetSourceFile is not null)
        {
            if (SourceFileMayBeEmitted(targetSourceFile))
            {
                result.Add(targetSourceFile);
            }
            return result;
        }
        foreach (var file in host.GetSourceFiles())
        {
            if (SourceFileMayBeEmitted(file))
            {
                result.Add(file);
            }
        }
        return result;
    }

    /// <summary>
    /// utilities.ts sourceFileMayBeEmitted: not a declaration file, not from an external library (Track O's programs
    /// have none), and not a JSON file (emitted only with outFile or outDir).
    /// </summary>
    internal static bool SourceFileMayBeEmitted(SourceFile sourceFile)
    {
        var store = sourceFile.Store;
        // Js files are emitted only if option is enabled (noEmitForJsFiles: not an option Track O supports)
        // Declaration files are not emitted
        if (store.IsDeclarationFile(sourceFile.Root))
        {
            return false;
        }
        // (Source file from node_modules are not emitted; source files from referenced projects are not emitted.)
        // Any non json file should be emitted
        if (store.ScriptKind(sourceFile.Root) != ScriptKind.JSON)
        {
            return true;
        }
        // Json file is not emitted if outDir is not specified (Track O has no outFile or outDir)
        return false;
    }

    /// <summary>emitter.ts getOutputPathsFor(sourceFile, host, forceDtsPaths) for a source file without outDir.</summary>
    internal static EmitFileNames GetOutputPathsFor(SourceFile sourceFile, EmitHost host, bool forceDtsPaths)
    {
        var options = host.GetCompilerOptions();
        var store = sourceFile.Store;
        var fileName = store.FileName(sourceFile.Root);
        // getOwnEmitOutputFilePath(fileName, host, getOutputExtension(fileName, options)) without outDir
        var ownOutputFilePath = Program.RemoveFileExtension(fileName) + host.Program.GetOutputExtension(fileName);
        var isJsonFile = store.ScriptKind(sourceFile.Root) == ScriptKind.JSON;
        // If json file emits to the same location skip writing it, if emitDeclarationOnly skip writing it
        var isJsonEmittedToSameLocation = isJsonFile &&
            string.Equals(TsPath.GetNormalizedAbsolutePath(fileName, host.GetCurrentDirectory()), TsPath.GetNormalizedAbsolutePath(ownOutputFilePath, host.GetCurrentDirectory()), StringComparison.Ordinal);
        var jsFilePath = isJsonEmittedToSameLocation ? null : ownOutputFilePath;
        var declarationFilePath = forceDtsPaths || (options.GetEmitDeclarations() && !isJsonFile) ? GetDeclarationEmitOutputFilePath(fileName) : null;
        return new EmitFileNames(jsFilePath, declarationFilePath);
    }

    /// <summary>utilities.ts getDeclarationEmitOutputFilePath without declarationDir or outDir.</summary>
    internal static string GetDeclarationEmitOutputFilePath(string fileName)
    {
        return Program.RemoveFileExtension(fileName) + Program.GetDeclarationEmitExtensionForPath(fileName);
    }

    /// <summary>
    /// emitter.ts emitFiles(resolver, host, targetSourceFile, transformers, emitOnly: false, onlyBuildInfo: false,
    /// forceDtsEmit: false): every file of <see cref="GetSourceFilesToEmit"/> as its .js output, then its .d.ts output,
    /// each written through the host. The transformer lists are empty (module D0), so the parse tree is printed.
    /// </summary>
    internal static EmitResult EmitFiles(EmitHost host, SourceFile? targetSourceFile)
    {
        var compilerOptions = host.GetCompilerOptions();
        var emitterDiagnostics = new List<ProgramDiagnostic>();
        var newLine = EmitTextWriter.GetNewLineCharacter(new PrinterOptions { NewLine = compilerOptions.NewLine });
        var writer = new EmitTextWriter(newLine);
        var emitSkipped = false;

        // forEachEmittedFile(host, emitSourceFileOrBundle, getSourceFilesToEmit(host, targetSourceFile, forceDtsEmit), ...)
        foreach (var sourceFile in GetSourceFilesToEmit(host, targetSourceFile))
        {
            var names = GetOutputPathsFor(sourceFile, host, forceDtsPaths: false);
            EmitJsFile(sourceFile, names.JsFilePath);
            EmitDeclarationFile(sourceFile, names.DeclarationFilePath);
            // (emitBuildInfo: no build info without incremental or composite)
        }

        return new EmitResult(emitSkipped, DiagnosticCollection.SortAndDeduplicate(emitterDiagnostics));

        void EmitJsFile(SourceFile sourceFile, string? jsFilePath)
        {
            if (jsFilePath is null)
            {
                return;
            }

            // Make sure not to write js file and source map file if any of them cannot be written
            if (host.IsEmitBlocked(jsFilePath))
            {
                emitSkipped = true;
                return;
            }

            // (markLinkedReferences, transformNodes(resolver, host, factory, compilerOptions, [sourceFile],
            // scriptTransformers, allowDtsFiles: false): no script transformers yet, the result is the source file.)
            var printerOptions = new PrinterOptions
            {
                RemoveComments = compilerOptions.RemoveComments == true,
                NewLine = compilerOptions.NewLine,
                Target = compilerOptions.GetEmitScriptTarget(),
            };

            // Create a printer to print the nodes (no handlers without transformers)
            var printer = Printer.CreatePrinter(printerOptions);
            PrintSourceFile(jsFilePath, sourceFile, printer);
        }

        void EmitDeclarationFile(SourceFile sourceFile, string? declarationFilePath)
        {
            if (declarationFilePath is null)
            {
                return;
            }
            // (filesForEmit: the file, it is not JSON; collectLinkedAliases and the declaration transform: no
            // declaration transformers yet, so no diagnostics and the result is the source file.)
            var declBlocked = host.IsEmitBlocked(declarationFilePath);
            emitSkipped = emitSkipped || declBlocked;
            if (!declBlocked)
            {
                var printerOptions = new PrinterOptions
                {
                    RemoveComments = compilerOptions.RemoveComments == true,
                    NewLine = compilerOptions.NewLine,
                    NoEmitHelpers = true,
                    Target = compilerOptions.Target,
                    OnlyPrintJsDocStyle = true,
                };
                var declarationPrinter = Printer.CreatePrinter(printerOptions);
                PrintSourceFile(declarationFilePath, sourceFile, declarationPrinter);
            }
        }

        void PrintSourceFile(string jsFilePath, SourceFile sourceFile, Printer printer)
        {
            printer.WriteFile(sourceFile.Store, sourceFile.Root, writer);
            // (no source map generator)
            writer.WriteLine();

            // Write the output file
            var text = writer.GetText();
            host.WriteFile(jsFilePath, text, writeByteOrderMark: false);

            // Reset state
            writer.Clear();
        }
    }
}
