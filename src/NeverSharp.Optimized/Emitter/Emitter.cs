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
        var transformers = Transformers.GetTransformers(compilerOptions);

        // forEachEmittedFile(host, emitSourceFileOrBundle, getSourceFilesToEmit(host, targetSourceFile, forceDtsEmit), ...)
        foreach (var sourceFile in GetSourceFilesToEmit(host, targetSourceFile))
        {
            var names = GetOutputPathsFor(sourceFile, host, forceDtsPaths: false);

            // Track O (module D1): the transformers write into the emit copy of the file's store (nothing writes into
            // the file's own store); the file's resolver comes from the checker that checked it.
            var emitStore = sourceFile.Store.CreateEmitCopy();
            var factory = NodeFactory.CreateTransformFactory(emitStore);
            var resolver = host.Program.GetTypeChecker().GetEmitResolver(sourceFile);
            resolver.EmitStore = emitStore;

            EmitJsFile(sourceFile, names.JsFilePath, emitStore, factory, resolver);
            EmitDeclarationFile(sourceFile, names.DeclarationFilePath, emitStore, factory, resolver);
            // (emitBuildInfo: no build info without incremental or composite)

            emitStore.Release();
        }

        return new EmitResult(emitSkipped, DiagnosticCollection.SortAndDeduplicate(emitterDiagnostics));

        void EmitJsFile(SourceFile sourceFile, string? jsFilePath, NodeStore emitStore, NodeFactory factory, EmitResolver resolver)
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

            // (noCheck is not a Track O option.)
            if (!Program.CanIncludeBindAndCheckDiagnostics(sourceFile, compilerOptions))
            {
                MarkLinkedReferences(sourceFile, resolver);
            }

            // Transform the source files
            var transform = Transformers.TransformNodes(resolver, host, factory, compilerOptions, sourceFile.Root, transformers.ScriptTransformers, allowDtsFiles: false);

            var printerOptions = new PrinterOptions
            {
                RemoveComments = compilerOptions.RemoveComments == true,
                NewLine = compilerOptions.NewLine,
                // (noEmitHelpers: not an option Track O supports, so false)
                Target = compilerOptions.GetEmitScriptTarget(),
            };

            // Create a printer to print the nodes
            var printer = Printer.CreatePrinter(printerOptions, new PrintHandlers
            {
                // resolver hooks
                HasGlobalName = resolver.HasGlobalName,

                // transform hooks
                OnEmitNode = transform.EmitNodeWithNotification,
                IsEmitNotificationEnabled = transform.IsEmitNotificationEnabled,
                SubstituteNode = transform.SubstituteNode,
            });
            // Track O (module D2): what the printer reads from the nodes in TypeScript: the binder's locals
            // (generateNameForModuleOrEnum).
            printer.Binding = sourceFile.Binding;

            PrintSourceFile(jsFilePath, emitStore, transform.Transformed, printer);

            // Clean up emit nodes on parse tree
            transform.Dispose();
        }

        void EmitDeclarationFile(SourceFile sourceFile, string? declarationFilePath, NodeStore emitStore, NodeFactory factory, EmitResolver resolver)
        {
            if (declarationFilePath is null)
            {
                return;
            }
            // (filesForEmit: the file, it is not JSON; collectLinkedAliases (module D5).)
            var declarationTransform = Transformers.TransformNodes(resolver, host, factory, compilerOptions, sourceFile.Root, transformers.DeclarationTransformers, allowDtsFiles: false);
            if (declarationTransform.Diagnostics.Count > 0)
            {
                foreach (var diagnostic in declarationTransform.Diagnostics)
                {
                    emitterDiagnostics.Add(diagnostic);
                }
            }
            var declBlocked = host.IsEmitBlocked(declarationFilePath) || declarationTransform.Diagnostics.Count > 0;
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
                var declarationPrinter = Printer.CreatePrinter(printerOptions, new PrintHandlers
                {
                    // resolver hooks
                    HasGlobalName = resolver.HasGlobalName,

                    // transform hooks
                    OnEmitNode = declarationTransform.EmitNodeWithNotification,
                    IsEmitNotificationEnabled = declarationTransform.IsEmitNotificationEnabled,
                    SubstituteNode = declarationTransform.SubstituteNode,
                });
                PrintSourceFile(declarationFilePath, emitStore, declarationTransform.Transformed, declarationPrinter);
            }
            declarationTransform.Dispose();
        }

        // emitter.ts markLinkedReferences (module D2): for a file that is not checked (`// @ts-nocheck`), the resolver
        // marks the aliases every node references, as checking would have (forEachChildRecursively: preorder, source
        // order, without the children of skipped nodes).
        static void MarkLinkedReferences(SourceFile file, EmitResolver resolver)
        {
            var store = file.Store;
            if (store.ScriptKind(file.Root) is ScriptKind.JS or ScriptKind.JSX)
            {
                return; // JS files don't use reference calculations as they don't do import ellision, no need to calculate it
            }
            var stack = new List<Node>();
            var children = new List<Node>();
            PushChildren(file.Root);
            while (stack.Count > 0)
            {
                var n = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                var kind = store.Kind(n);
                // (uncached: nothing writes into a file's store, the modifier flags cache included)
                if (kind == SyntaxKind.ImportEqualsDeclaration && (Utilities.GetSyntacticModifierFlagsUncached(store, n) & ModifierFlags.Export) == 0)
                {
                    continue; // These are deferred and marked in a chain when referenced
                }
                if (kind == SyntaxKind.ImportDeclaration)
                {
                    continue; // likewise, these are ultimately what get marked by calls on other nodes - we want to skip them
                }
                resolver.MarkLinkedReferences(n);
                PushChildren(n);
            }

            void PushChildren(Node node)
            {
                children.Clear();
                NodeVisitor.ForEachChild<object>(store, node, child =>
                {
                    children.Add(child);
                    return null;
                });
                for (var i = children.Count - 1; i >= 0; i--)
                {
                    stack.Add(children[i]);
                }
            }
        }

        void PrintSourceFile(string jsFilePath, NodeStore emitStore, Node sourceFileNode, Printer printer)
        {
            printer.WriteFile(emitStore, sourceFileNode, writer);
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
