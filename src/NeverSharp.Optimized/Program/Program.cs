// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/program.ts (createProgram: processRootFile, processSourceFile,
// getSourceFileFromReferenceWorker, findSourceFile, processLibReferenceDirectives, pathForLibFile,
// getDefaultLibFilePriority, getSemanticDiagnostics and friends) and src/compiler/utilities.ts
// (getSupportedExtensions, getSetExternalModuleIndicator)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/compiler/program.go and internal/compiler/fileloader.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C0 implements the program of Gate C and the checker endpoint:
// root files, the default library and lib reference directives with `noResolve: true` (no referenced files or type
// reference directives; other options throw NotSupportedException), no project references, no old program. Module C10
// adds processImportedModules (Program.Resolution.cs): every module name of every file is resolved and recorded, as
// tsc does with noResolve; resolved files are not added to the program.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// program.ts: Program. Holds the program's source files in TypeScript's order (default library files sorted by their
/// priority, then the other files in processing order), the diagnostics of file processing, and the type checker
/// (created on first use; it binds the files that are not bound yet, as checker.ts initializeTypeChecker does).
/// One thread uses a program at a time; library files are shared read-only (<see cref="DefaultLibrary"/>).
/// </summary>
internal sealed partial class Program
{
    // program.ts FileIncludeKind (the kinds this program creates).
    private enum FileIncludeKind
    {
        RootFile,
        LibFile,
        LibReferenceDirective,
    }

    private readonly record struct FileIncludeReason(FileIncludeKind Kind, int Index, string? File);

    private readonly CompilerOptions options;
    private readonly ICompilerHost host;
    private readonly string currentDirectory;
    private readonly string defaultLibraryPath;
    private readonly Dictionary<string, SourceFile?> filesByName = new(StringComparer.Ordinal);
    private readonly HashSet<string> libFiles = new(StringComparer.Ordinal);
    private readonly List<ProgramDiagnostic> fileProcessingDiagnostics = [];
    private readonly List<ProgramDiagnostic> configDiagnostics = [];
    private List<SourceFile>? processingDefaultLibFiles = [];
    private List<SourceFile>? processingOtherFiles = [];
    private readonly List<SourceFile> files;
    private Checker? checker;
    private Dictionary<SourceFile, List<Diagnostic>>? additionalSyntacticDiagnostics;

    private Program(IReadOnlyList<string> rootNames, CompilerOptions options, ICompilerHost host)
    {
        this.options = options;
        this.host = host;
        currentDirectory = host.GetCurrentDirectory();
        defaultLibraryPath = host.GetDefaultLibLocation();
        RootNames = rootNames;
        // (no host.resolveModuleNames: createModuleResolutionCache)
        moduleResolutionCache = new ModuleResolutionCache(currentDirectory, host.GetCanonicalFileName);

        for (var i = 0; i < rootNames.Count; i++)
        {
            ProcessRootFile(rootNames[i], isDefaultLib: false, new FileIncludeReason(FileIncludeKind.RootFile, i, null));
        }

        // (automatic type directives: the host has no directories, so getAutomaticTypeDirectiveNames finds none.)

        if (rootNames.Count > 0 && options.NoLib != true)
        {
            // If '--lib' is not specified, include default library file according to '--target'
            // otherwise, using options specified in '--lib' instead of '--target' default library file
            var defaultLibraryFileName = host.GetDefaultLibFileName(options);
            if (options.Lib is null)
            {
                libFiles.Add(ToPath(defaultLibraryFileName));
                ProcessRootFile(defaultLibraryFileName, isDefaultLib: true, new FileIncludeReason(FileIncludeKind.LibFile, -1, null));
            }
            else
            {
                for (var i = 0; i < options.Lib.Count; i++)
                {
                    ProcessRootFile(PathForLibFile(options.Lib[i]), isDefaultLib: true, new FileIncludeReason(FileIncludeKind.LibFile, i, null));
                }
            }
        }

        // files = toSorted(processingDefaultLibFiles, compareDefaultLibFiles).concat(processingOtherFiles) (a stable sort).
        var defaultLibFiles = processingDefaultLibFiles!;
        var order = new int[defaultLibFiles.Count];
        var priorities = new int[defaultLibFiles.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
            priorities[i] = GetDefaultLibFilePriority(defaultLibFiles[i]);
        }
        Array.Sort(order, (a, b) => priorities[a] != priorities[b] ? priorities[a].CompareTo(priorities[b]) : a.CompareTo(b));
        files = new List<SourceFile>(defaultLibFiles.Count + processingOtherFiles!.Count);
        foreach (var i in order)
        {
            files.Add(defaultLibFiles[i]);
        }
        files.AddRange(processingOtherFiles);
        processingDefaultLibFiles = null;
        processingOtherFiles = null;

        VerifyEmitFilePaths();
    }

    // program.ts verifyCompilerOptions, its last part: "If the emit is enabled make sure that every output file is unique
    // and not overwriting any of the input files" (forEachEmittedFile, getOutputPathsFor, verifyEmitFilePath). Track O's
    // options have no noEmit, suppressOutputPathCheck, emitDeclarationOnly, outFile, outDir or declarationDir, so the
    // emit is enabled and every output lies next to its input; no config file, so the tsconfig hint is always chained.
    private void VerifyEmitFilePaths()
    {
        var emitFilesSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            // getSourceFilesToEmit: sourceFileMayBeEmitted (declaration files are not emitted; root files are not from
            // an external library; JSON files are not emitted without outDir/outFile)
            var store = file.Store;
            var fileName = store.FileName(file.Root);
            if (store.IsDeclarationFile(file.Root) || TsPath.FileExtensionIs(fileName, ".json"))
            {
                continue;
            }
            var withoutExtension = RemoveFileExtension(fileName);
            VerifyEmitFilePath(withoutExtension + GetOutputExtension(fileName), emitFilesSeen);
            if (options.GetEmitDeclarations())
            {
                VerifyEmitFilePath(withoutExtension + GetDeclarationEmitExtensionForPath(fileName), emitFilesSeen);
            }
        }
    }

    private void VerifyEmitFilePath(string emitFileName, HashSet<string> emitFilesSeen)
    {
        var emitFilePath = ToPath(emitFileName);
        // Report error if the output overwrites input file
        if (filesByName.ContainsKey(emitFilePath))
        {
            // The program is from either an inferred project or an external project
            var chain = ProgramDiagnostics.ChainDiagnosticMessages(null, Diagnostics.Adding_a_tsconfig_json_file_will_help_organize_projects_that_contain_both_TypeScript_and_JavaScript_files_Learn_more_at_https_Colon_Slash_Slashaka_ms_Slashtsconfig);
            chain = ProgramDiagnostics.ChainDiagnosticMessages([chain], Diagnostics.Cannot_write_file_0_because_it_would_overwrite_input_file, emitFileName);
            configDiagnostics.Add(ProgramDiagnostics.CreateCompilerDiagnosticFromMessageChain(chain));
        }
        var emitFileKey = !host.UseCaseSensitiveFileNames ? emitFilePath.ToLowerInvariant() : emitFilePath;
        // Report error if multiple files write into same file
        if (!emitFilesSeen.Add(emitFileKey))
        {
            // Already seen the same emit file - report error
            configDiagnostics.Add(ProgramDiagnostics.CreateCompilerDiagnosticFromMessageChain(ProgramDiagnostics.ChainDiagnosticMessages(null, Diagnostics.Cannot_write_file_0_because_it_would_be_overwritten_by_multiple_input_files, emitFileName)));
        }
    }

    // emitter.ts getOutputExtension
    private string GetOutputExtension(string fileName)
    {
        return TsPath.FileExtensionIs(fileName, ".json") ? ".json"
            : options.Jsx == JsxEmit.Preserve && FileExtensionIsOneOf(fileName, ".jsx", ".tsx") ? ".jsx"
            : FileExtensionIsOneOf(fileName, ".mts", ".mjs") ? ".mjs"
            : FileExtensionIsOneOf(fileName, ".cts", ".cjs") ? ".cjs"
            : ".js";
    }

    // utilities.ts getDeclarationEmitExtensionForPath
    private static string GetDeclarationEmitExtensionForPath(string path)
    {
        return FileExtensionIsOneOf(path, ".mjs", ".mts") ? ".d.mts"
            : FileExtensionIsOneOf(path, ".cjs", ".cts") ? ".d.cts"
            : FileExtensionIsOneOf(path, ".json") ? ".d.json.ts"
            : ".d.ts";
    }

    // utilities.ts removeFileExtension (extensionsToRemove)
    private static string RemoveFileExtension(string path)
    {
        foreach (var extension in ExtensionsToRemove)
        {
            if (TsPath.FileExtensionIs(path, extension))
            {
                return path[..^extension.Length];
            }
        }
        return path;
    }

    private static readonly string[] ExtensionsToRemove = [".d.ts", ".d.mts", ".d.cts", ".mjs", ".mts", ".cjs", ".cts", ".ts", ".js", ".tsx", ".jsx", ".json"];

    /// <summary>program.ts createProgram(rootNames, options, host).</summary>
    internal static Program Create(IReadOnlyList<string> rootNames, CompilerOptions options, ICompilerHost host)
    {
        ArgumentNullException.ThrowIfNull(rootNames);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(host);
        if (options.NoResolve != true)
        {
            throw new NotSupportedException("Program (module C0) supports noResolve: true only");
        }
        if (options.LibReplacement == true || options.Jsx is JsxEmit.ReactJSX or JsxEmit.ReactJSXDev)
        {
            throw new NotSupportedException("Program (module C0): libReplacement and jsx react-jsx are not supported yet");
        }
        return new Program(rootNames, options, host);
    }

    internal CompilerOptions Options => options;

    internal ICompilerHost Host => host;

    internal IReadOnlyList<string> RootNames { get; }

    /// <summary>program.getSourceFiles(): default library files first (by priority), then the others.</summary>
    internal IReadOnlyList<SourceFile> SourceFiles => files;

    /// <summary>program.getSourceFile(fileName).</summary>
    internal SourceFile? GetSourceFile(string fileName) => filesByName.TryGetValue(ToPath(fileName), out var file) ? file : null;

    /// <summary>program.isSourceFileDefaultLibrary: the file was included as a default library file.</summary>
    internal bool IsSourceFileDefaultLibrary(SourceFile file) => libFiles.Contains(ToPath(file.Store.FileName(file.Root)));

    /// <summary>program.getTypeChecker(): created on first use.</summary>
    internal Checker GetTypeChecker() => checker ??= new Checker(this);

    /// <summary>
    /// typescript-go program.go BindSourceFiles: binds every file that is not bound yet, one work item per file on the
    /// thread pool (typescript-go's core.WorkGroup), or sequentially when <paramref name="singleThreaded"/>. Checkers
    /// created afterwards find every file bound (module C10: the check driver's checker pool).
    /// </summary>
    internal void BindSourceFiles(bool singleThreaded)
    {
        if (singleThreaded)
        {
            foreach (var file in files)
            {
                Binder.BindSourceFile(file, options);
            }
            return;
        }
        var tasks = new List<Task>(files.Count);
        foreach (var file in files)
        {
            if (file.Binding is null)
            {
                tasks.Add(Task.Run(() => Binder.BindSourceFile(file, options)));
            }
        }
        Task.WaitAll(tasks);
    }

    /// <summary>
    /// program.getOptionsDiagnostics(): the diagnostics of file processing without a file (e.g. TS6504 for a JavaScript
    /// root file without allowJs) and the emit-path check of verifyCompilerOptions (TS5055 for a JavaScript root file
    /// whose output would overwrite it; the rest of option validation finds nothing for the options C0 supports).
    /// Sorted and deduplicated (sortAndDeduplicateDiagnostics, module C8).
    /// </summary>
    internal List<ProgramDiagnostic> GetOptionsDiagnostics()
    {
        var result = new List<ProgramDiagnostic>();
        foreach (var d in fileProcessingDiagnostics)
        {
            if (d.SourceFile is null)
            {
                result.Add(d);
            }
        }
        // the configuration diagnostics of verifyCompilerOptions (TS5055/TS5056 of the emit paths; module C9a)
        result.AddRange(configDiagnostics);
        return DiagnosticCollection.SortAndDeduplicate(result);
    }

    /// <summary>program.getGlobalDiagnostics(): the checker's global diagnostics.</summary>
    internal List<ProgramDiagnostic> GetGlobalDiagnostics() => GetTypeChecker().GetGlobalDiagnostics();

    /// <summary>
    /// program.getSyntacticDiagnostics(file), getSyntacticDiagnosticsForFile: the parse diagnostics, for JavaScript files
    /// preceded by the additional syntactic diagnostics (TypeScript-only constructs, getJSSyntacticDiagnosticsForFile;
    /// computed once per file, as TypeScript caches them in sourceFile.additionalSyntacticDiagnostics).
    /// </summary>
    internal List<ProgramDiagnostic> GetSyntacticDiagnostics(SourceFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var result = new List<ProgramDiagnostic>();
        // For JavaScript files, we report semantic errors for using TypeScript-only
        // constructs from within a JavaScript file as syntactic errors.
        if (Utilities.IsInJSFile(file.Store, file.Root))
        {
            additionalSyntacticDiagnostics ??= new Dictionary<SourceFile, List<Diagnostic>>(ReferenceEqualityComparer.Instance);
            if (!additionalSyntacticDiagnostics.TryGetValue(file, out var additional))
            {
                additional = JsSyntacticDiagnostics.GetForFile(file, options);
                additionalSyntacticDiagnostics.Add(file, additional);
            }
            foreach (var d in additional)
            {
                result.Add(ProgramDiagnostic.FromFile(file, d));
            }
        }
        foreach (var d in file.ParseDiagnostics)
        {
            result.Add(ProgramDiagnostic.FromFile(file, d));
        }
        // getDiagnosticsHelper: sortAndDeduplicateDiagnostics (module C8)
        return DiagnosticCollection.SortAndDeduplicate(result);
    }

    /// <summary>
    /// program.getSemanticDiagnostics(file): getBindAndCheckDiagnosticsForFileNoCache, the bind diagnostics followed by
    /// the checker's diagnostics (in plain JavaScript files only the codes of plainJSErrors; in checked JavaScript files
    /// also the JSDoc parse diagnostics), filtered by the comment directives (not in plain JavaScript files). No program
    /// diagnostics of the file (e.g. unknown lib references); C8 completes.
    /// </summary>
    internal List<ProgramDiagnostic> GetSemanticDiagnostics(SourceFile file) => GetSemanticDiagnostics(file, GetTypeChecker());

    /// <summary>
    /// The semantic diagnostics of <paramref name="file"/> from the given checker (typescript-go's
    /// getSemanticDiagnosticsWithChecker; the check driver's checker pool, module C10).
    /// </summary>
    internal List<ProgramDiagnostic> GetSemanticDiagnostics(SourceFile file, Checker checker)
    {
        var result = new List<ProgramDiagnostic>();
        if (!CanIncludeBindAndCheckDiagnostics(file, options))
        {
            return result; // skipTypeChecking
        }
        var store = file.Store;
        var scriptKind = store.ScriptKind(file.Root);
        var isJs = scriptKind is ScriptKind.JS or ScriptKind.JSX;
        var isPlainJs = IsPlainJsFile(file, options.CheckJs);
        var isCheckJs = isJs && IsCheckJsEnabledForFile(file, options) == true;
        if (file.Binding is { } binding)
        {
            foreach (var d in binding.BindDiagnostics)
            {
                if (!isPlainJs || PlainJSErrors.Contains(d.Code))
                {
                    result.Add(ProgramDiagnostic.FromFile(file, d));
                }
            }
        }
        foreach (var d in checker.GetDiagnostics(file))
        {
            if (!isPlainJs || PlainJSErrors.Contains(d.Code))
            {
                result.Add(d);
            }
        }
        if (isCheckJs && file.JsDocDiagnostics is { } jsDocDiagnostics)
        {
            foreach (var d in jsDocDiagnostics)
            {
                result.Add(ProgramDiagnostic.FromFile(file, d));
            }
        }
        // getMergedBindAndCheckDiagnostics: skip ts-expect-error errors in plain JS files
        var commentDirectives = store.CommentDirectives(file.Root);
        if (isPlainJs || commentDirectives is not { Count: > 0 })
        {
            return DiagnosticCollection.SortAndDeduplicate(result);
        }
        // getDiagnosticsHelper: sortAndDeduplicateDiagnostics (module C8)
        return DiagnosticCollection.SortAndDeduplicate(GetDiagnosticsWithPrecedingDirectives(file, commentDirectives, result));
    }

    // utilities.ts isPlainJsFile
    internal static bool IsPlainJsFile(SourceFile? file, bool? checkJs)
    {
        return file is not null && file.Store.ScriptKind(file.Root) is ScriptKind.JS or ScriptKind.JSX && file.Store.CheckJsDirective(file.Root) is null && checkJs is null;
    }

    // utilities.ts isCheckJsEnabledForFile
    internal static bool? IsCheckJsEnabledForFile(SourceFile file, CompilerOptions options)
    {
        return file.Store.CheckJsDirective(file.Root) is { } directive ? directive.Enabled : options.CheckJs;
    }

    /// <summary>utilities.ts canIncludeBindAndCheckDiagnostics (the part of skipTypeChecking Track O's options reach).</summary>
    internal static bool CanIncludeBindAndCheckDiagnostics(SourceFile file, CompilerOptions options)
    {
        var store = file.Store;
        if (store.CheckJsDirective(file.Root) is { Enabled: false })
        {
            return false;
        }
        var scriptKind = store.ScriptKind(file.Root);
        if (scriptKind is ScriptKind.TS or ScriptKind.TSX or ScriptKind.External)
        {
            return true;
        }
        var isJs = scriptKind is ScriptKind.JS or ScriptKind.JSX;
        var isCheckJs = isJs && IsCheckJsEnabledForFile(file, options) == true;
        var isPlainJs = IsPlainJsFile(file, options.CheckJs);

        // By default, only type-check .ts, .tsx, Deferred, plain JS, checked JS and External
        // - plain JS: .js files with no // ts-check and checkJs: undefined
        // - check JS: .js files with either // ts-check or checkJs: true
        // - external: files that are added by plugins
        return isPlainJs || isCheckJs || scriptKind == ScriptKind.Deferred;
    }

    // program.ts getMergedBindAndCheckDiagnostics / getDiagnosticsWithPrecedingDirectives / markPrecedingCommentDirectiveLine:
    // diagnostics suppressed by a @ts-ignore or @ts-expect-error directive on a preceding line (only comment lines in
    // between), plus an error for each unused @ts-expect-error
    private static List<ProgramDiagnostic> GetDiagnosticsWithPrecedingDirectives(SourceFile file, List<CommentDirective> commentDirectives, List<ProgramDiagnostic> diagnostics)
    {
        var store = file.Store;
        var sourceFileLike = new SourceFileNodeLike(store, file.Root);
        var lineStarts = Scanner.GetLineStarts(sourceFileLike);
        var text = store.SourceFileText(file.Root).Chars.ToString();
        // createCommentDirectivesMap: keyed by the line of each directive's end (a later directive on a line replaces the
        // earlier one but keeps its position in the map's order)
        var lines = new List<int>();
        var directivesByLine = new Dictionary<int, CommentDirective>();
        foreach (var directive in commentDirectives)
        {
            var line = Scanner.ComputeLineAndCharacterOfPosition(lineStarts, directive.Range.End).Line;
            if (!directivesByLine.ContainsKey(line))
            {
                lines.Add(line);
            }
            directivesByLine[line] = directive;
        }
        var usedLines = new HashSet<int>();
        var result = new List<ProgramDiagnostic>();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.SourceFile is null || diagnostic.StartOrUndefined is not { } start || !IsSuppressed(start))
            {
                result.Add(diagnostic);
            }
        }
        foreach (var line in lines)
        {
            var directive = directivesByLine[line];
            if (directive.Type == CommentDirectiveType.ExpectError && !usedLines.Contains(line))
            {
                var d = Utilities.CreateFileDiagnostic(store, file.Root, directive.Range.Pos, directive.Range.End - directive.Range.Pos, Diagnostics.Unused_ts_expect_error_directive);
                result.Add(ProgramDiagnostic.FromFile(file, d));
            }
        }
        return result;

        bool IsSuppressed(int start)
        {
            for (var line = Scanner.ComputeLineAndCharacterOfPosition(lineStarts, start).Line - 1; line >= 0; line--)
            {
                if (directivesByLine.ContainsKey(line))
                {
                    usedLines.Add(line);
                    return true;
                }
                var lineEnd = line + 1 < lineStarts.Count ? lineStarts[line + 1] : text.Length;
                var lineText = text[lineStarts[line]..lineEnd].Trim();
                if (lineText.Length != 0 && !IsLineComment(lineText))
                {
                    return false;
                }
            }
            return false;
        }

        // /^\s*\/\/.*$/ on the trimmed line ('.' does not match line terminators)
        static bool IsLineComment(string lineText)
        {
            if (!lineText.StartsWith("//", StringComparison.Ordinal))
            {
                return false;
            }
            foreach (var c in lineText)
            {
                if (c is '\n' or '\r' or (char)0x2028 or (char)0x2029)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>program.getSuggestionDiagnostics(file): the checker's suggestion diagnostics.</summary>
    internal List<ProgramDiagnostic> GetSuggestionDiagnostics(SourceFile file) => GetTypeChecker().GetSuggestionDiagnostics(file);

    private string ToPath(string fileName) => host.GetCanonicalFileName(TsPath.GetNormalizedAbsolutePath(fileName, currentDirectory));

    private void ProcessRootFile(string fileName, bool isDefaultLib, FileIncludeReason reason)
    {
        ProcessSourceFile(TsPath.NormalizePath(fileName), isDefaultLib, reason);
    }

    private void ProcessSourceFile(string fileName, bool isDefaultLib, FileIncludeReason reason)
    {
        GetSourceFileFromReferenceWorker(fileName, isDefaultLib, reason);
    }

    /// <summary>program.ts getSourceFileFromReferenceWorker (the case of a file name with an extension).</summary>
    private SourceFile? GetSourceFileFromReferenceWorker(string fileName, bool isDefaultLib, FileIncludeReason reason)
    {
        if (!HasExtension(fileName))
        {
            // (Files without an extension: tried with each supported extension; no root or library file of C0's
            // programs has none.)
            throw new NotSupportedException("Program (module C0): a file name without an extension: " + fileName);
        }
        var canonicalFileName = host.GetCanonicalFileName(fileName);
        if (!IsSupportedExtension(canonicalFileName))
        {
            if (HasJSFileExtension(canonicalFileName))
            {
                AddFileExplainingDiagnostic(reason, Diagnostics.File_0_is_a_JavaScript_file_Did_you_mean_to_enable_the_allowJs_option, fileName);
            }
            else
            {
                AddFileExplainingDiagnostic(reason, Diagnostics.File_0_has_an_unsupported_extension_The_only_supported_extensions_are_1, fileName,
                    "'" + string.Join("', '", SupportedExtensionsFlat()) + "'");
            }
            return null;
        }
        var sourceFile = FindSourceFile(fileName, isDefaultLib, reason);
        if (sourceFile is null)
        {
            AddFileExplainingDiagnostic(reason, Diagnostics.File_0_not_found, fileName);
        }
        return sourceFile;
    }

    /// <summary>program.ts findSourceFile / findSourceFileWorker (no redirects, no project references).</summary>
    private SourceFile? FindSourceFile(string fileName, bool isDefaultLib, FileIncludeReason reason)
    {
        var path = ToPath(fileName);
        if (filesByName.TryGetValue(path, out var existing))
        {
            // (the include reason of an existing file is recorded by TypeScript for explanations; C0 needs only the first.)
            return existing;
        }
        var file = host.GetSourceFile(fileName, new CreateSourceFileOptions
        {
            LanguageVersion = options.GetEmitScriptTarget(),
            JsDocParsingMode = host.JsDocParsingMode,
        });
        filesByName[path] = file;
        if (file is null)
        {
            return null;
        }
        ApplyExternalModuleIndicator(file);

        // (!options.noResolve: processReferencedFiles, processTypeReferenceDirectives; noResolve is true here.)
        if (options.NoLib != true)
        {
            ProcessLibReferenceDirectives(file);
        }
        // always process imported modules to record module name resolutions
        ProcessImportedModules(file);
        if (isDefaultLib)
        {
            processingDefaultLibFiles!.Add(file);
        }
        else
        {
            processingOtherFiles!.Add(file);
        }
        return file;
    }

    /// <summary>program.ts processLibReferenceDirectives.</summary>
    private void ProcessLibReferenceDirectives(SourceFile file)
    {
        var references = file.Store.LibReferenceDirectives(file.Root);
        for (var i = 0; i < references.Count; i++)
        {
            var libFileName = LibraryNames.GetLibFileNameFromLibReference(references[i].FileName);
            if (libFileName is not null)
            {
                ProcessRootFile(PathForLibFile(libFileName), isDefaultLib: true,
                    new FileIncludeReason(FileIncludeKind.LibReferenceDirective, i, file.Store.FileName(file.Root)));
            }
            // else: programDiagnostics.addFileProcessingDiagnostic(FilePreprocessingLibReferenceDiagnostic), a
            // diagnostic of the file ("Cannot find lib definition for '{0}'."): not ported, no Gate C unit references an unknown lib.
        }
    }

    /// <summary>program.ts pathForLibFile (no libReplacement: the file in the default library folder).</summary>
    private string PathForLibFile(string libFileName)
    {
        var actual = TsPath.CombinePaths(defaultLibraryPath, libFileName);
        libFiles.Add(ToPath(actual));
        return actual;
    }

    /// <summary>program.ts getDefaultLibFilePriority.</summary>
    private int GetDefaultLibFilePriority(SourceFile a)
    {
        var fileName = a.Store.FileName(a.Root);
        if (ContainsPath(defaultLibraryPath, fileName))
        {
            var basename = TsPath.GetBaseFileName(fileName);
            if (basename is "lib.d.ts" or "lib.es6.d.ts")
            {
                return 0;
            }
            var name = RemoveSuffix(RemovePrefix(basename, "lib."), ".d.ts");
            var index = LibraryNames.IndexOf(name);
            if (index != -1)
            {
                return index + 1;
            }
        }
        return LibraryNames.Count + 2;
    }

    /// <summary>
    /// utilities.ts getSetExternalModuleIndicator, applied after the parse: the parser has set
    /// isFileProbablyExternalModule (its default); this adds the program's other checks. (Known limitation of module
    /// C0: TypeScript runs this inside the parse, before the top-level-await reparse; a file that is a module only by
    /// its format and has a top-level await is not reparsed here. Needs a parser seam; TODO(C1).)
    /// </summary>
    private void ApplyExternalModuleIndicator(SourceFile file)
    {
        var store = file.Store;
        var root = file.Root;
        switch (options.GetEmitModuleDetectionKind())
        {
            case ModuleDetectionKind.Force:
                // All non-declaration files are modules, declaration files still do the usual isFileProbablyExternalModule
                if (store.ExternalModuleIndicator(root) is null && !store.IsDeclarationFile(root))
                {
                    store.SetExternalModuleIndicator(root, true);
                }
                return;
            case ModuleDetectionKind.Legacy:
                // Files are modules if they have imports, exports, or import.meta
                return;
            case ModuleDetectionKind.Auto:
                // If module is nodenext or node16, all esm format files are modules
                // If jsx is react-jsx or react-jsxdev then jsx tags force module-ness
                // otherwise, the presence of import or export statments (or import.meta) implies module-ness
                // (jsx react-jsx is rejected by Create.) isFileForcedToBeModuleByFormat:
                if (store.ExternalModuleIndicator(root) is null && !store.IsDeclarationFile(root)
                    && (store.ImpliedNodeFormat(root) == ModuleKind.ESNext || FileExtensionIsOneOf(store.FileName(root), ".cjs", ".cts", ".mjs", ".mts")))
                {
                    store.SetExternalModuleIndicator(root, true);
                }
                return;
        }
    }

    private void AddFileExplainingDiagnostic(FileIncludeReason reason, DiagnosticMessage message, params object?[] args)
    {
        // program.ts createDiagnosticExplainingFile for a file processing diagnostic without a referencing location:
        // the message, then "The file is in the program because:" and the reason.
        var reasonChain = reason.Kind switch
        {
            FileIncludeKind.RootFile => ProgramDiagnostics.ChainDiagnosticMessages(null, Diagnostics.Root_file_specified_for_compilation),
            _ => throw new NotSupportedException("Program (module C0): explaining a library file's processing diagnostic"),
        };
        var details = ProgramDiagnostics.ChainDiagnosticMessages([reasonChain], Diagnostics.The_file_is_in_the_program_because_Colon);
        var chain = ProgramDiagnostics.ChainDiagnosticMessages([details], message, args);
        fileProcessingDiagnostics.Add(ProgramDiagnostics.CreateCompilerDiagnosticFromMessageChain(chain));
    }

    // utilities.ts getSupportedExtensions(options) and getSupportedExtensionsWithJsonIfResolveJsonModule: without
    // allowJs the TypeScript extensions, plus .json with resolveJsonModule.
    private static readonly string[][] SupportedTSExtensions = [[".ts", ".tsx", ".d.ts"], [".cts", ".d.cts"], [".mts", ".d.mts"]];
    private static readonly string[][] AllSupportedExtensions = [[".ts", ".tsx", ".d.ts", ".js", ".jsx"], [".cts", ".d.cts", ".cjs"], [".mts", ".d.mts", ".mjs"]];

    private string[][] SupportedExtensions() => options.GetAllowJSCompilerOption() ? AllSupportedExtensions : SupportedTSExtensions;

    private List<string> SupportedExtensionsFlat()
    {
        var result = new List<string>();
        foreach (var group in SupportedExtensions())
        {
            result.AddRange(group);
        }
        return result;
    }

    private bool IsSupportedExtension(string fileName)
    {
        foreach (var group in SupportedExtensions())
        {
            foreach (var extension in group)
            {
                if (TsPath.FileExtensionIs(fileName, extension))
                {
                    return true;
                }
            }
        }
        return options.GetResolveJsonModule() && TsPath.FileExtensionIs(fileName, ".json");
    }

    // utilities.ts hasJSFileExtension (supportedJSExtensionsFlat: .js, .jsx, .mjs, .cjs).
    private static bool HasJSFileExtension(string fileName) => FileExtensionIsOneOf(fileName, ".js", ".jsx", ".mjs", ".cjs");

    private static bool FileExtensionIsOneOf(string fileName, params string[] extensions)
    {
        foreach (var extension in extensions)
        {
            if (TsPath.FileExtensionIs(fileName, extension))
            {
                return true;
            }
        }
        return false;
    }

    // path.ts hasExtension.
    private static bool HasExtension(string fileName) => TsPath.GetBaseFileName(fileName).Contains('.', StringComparison.Ordinal);

    // path.ts containsPath(parent, child, ignoreCase: false) for normalized absolute paths.
    private bool ContainsPath(string parent, string child)
    {
        var p = ToPath(parent);
        var c = ToPath(child);
        if (p.Length == 0 || c.Length == 0)
        {
            return false;
        }
        if (p == c)
        {
            return true;
        }
        var prefix = TsPath.EnsureTrailingDirectorySeparator(p);
        return c.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string RemovePrefix(string s, string prefix) => s.StartsWith(prefix, StringComparison.Ordinal) ? s[prefix.Length..] : s;

    private static string RemoveSuffix(string s, string suffix) => s.EndsWith(suffix, StringComparison.Ordinal) ? s[..^suffix.Length] : s;

    // program.ts plainJSErrors: the bind and check diagnostics reported in plain JavaScript files.
    private static readonly HashSet<int> PlainJSErrors =
    [
        // binder errors
        Diagnostics.Cannot_redeclare_block_scoped_variable_0.Code,
        Diagnostics.A_module_cannot_have_multiple_default_exports.Code,
        Diagnostics.Another_export_default_is_here.Code,
        Diagnostics.The_first_export_default_is_here.Code,
        Diagnostics.Identifier_expected_0_is_a_reserved_word_at_the_top_level_of_a_module.Code,
        Diagnostics.Identifier_expected_0_is_a_reserved_word_in_strict_mode_Modules_are_automatically_in_strict_mode.Code,
        Diagnostics.Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here.Code,
        Diagnostics.constructor_is_a_reserved_word.Code,
        Diagnostics.delete_cannot_be_called_on_an_identifier_in_strict_mode.Code,
        Diagnostics.Code_contained_in_a_class_is_evaluated_in_JavaScript_s_strict_mode_which_does_not_allow_this_use_of_0_For_more_information_see_https_Colon_Slash_Slashdeveloper_mozilla_org_Slashen_US_Slashdocs_SlashWeb_SlashJavaScript_SlashReference_SlashStrict_mode.Code,
        Diagnostics.Invalid_use_of_0_Modules_are_automatically_in_strict_mode.Code,
        Diagnostics.Invalid_use_of_0_in_strict_mode.Code,
        Diagnostics.A_label_is_not_allowed_here.Code,
        Diagnostics.with_statements_are_not_allowed_in_strict_mode.Code,
        // grammar errors
        Diagnostics.A_break_statement_can_only_be_used_within_an_enclosing_iteration_or_switch_statement.Code,
        Diagnostics.A_break_statement_can_only_jump_to_a_label_of_an_enclosing_statement.Code,
        Diagnostics.A_class_declaration_without_the_default_modifier_must_have_a_name.Code,
        Diagnostics.A_class_member_cannot_have_the_0_keyword.Code,
        Diagnostics.A_comma_expression_is_not_allowed_in_a_computed_property_name.Code,
        Diagnostics.A_continue_statement_can_only_be_used_within_an_enclosing_iteration_statement.Code,
        Diagnostics.A_continue_statement_can_only_jump_to_a_label_of_an_enclosing_iteration_statement.Code,
        Diagnostics.A_default_clause_cannot_appear_more_than_once_in_a_switch_statement.Code,
        Diagnostics.A_default_export_must_be_at_the_top_level_of_a_file_or_module_declaration.Code,
        Diagnostics.A_definite_assignment_assertion_is_not_permitted_in_this_context.Code,
        Diagnostics.A_destructuring_declaration_must_have_an_initializer.Code,
        Diagnostics.A_get_accessor_cannot_have_parameters.Code,
        Diagnostics.A_rest_element_cannot_contain_a_binding_pattern.Code,
        Diagnostics.A_rest_element_cannot_have_a_property_name.Code,
        Diagnostics.A_rest_element_cannot_have_an_initializer.Code,
        Diagnostics.A_rest_element_must_be_last_in_a_destructuring_pattern.Code,
        Diagnostics.A_rest_parameter_cannot_have_an_initializer.Code,
        Diagnostics.A_rest_parameter_must_be_last_in_a_parameter_list.Code,
        Diagnostics.A_rest_parameter_or_binding_pattern_may_not_have_a_trailing_comma.Code,
        Diagnostics.A_return_statement_cannot_be_used_inside_a_class_static_block.Code,
        Diagnostics.A_set_accessor_cannot_have_rest_parameter.Code,
        Diagnostics.A_set_accessor_must_have_exactly_one_parameter.Code,
        Diagnostics.An_export_declaration_can_only_be_used_at_the_top_level_of_a_module.Code,
        Diagnostics.An_export_declaration_cannot_have_modifiers.Code,
        Diagnostics.An_import_declaration_can_only_be_used_at_the_top_level_of_a_module.Code,
        Diagnostics.An_import_declaration_cannot_have_modifiers.Code,
        Diagnostics.An_object_member_cannot_be_declared_optional.Code,
        Diagnostics.Argument_of_dynamic_import_cannot_be_spread_element.Code,
        Diagnostics.Cannot_assign_to_private_method_0_Private_methods_are_not_writable.Code,
        Diagnostics.Cannot_redeclare_identifier_0_in_catch_clause.Code,
        Diagnostics.Catch_clause_variable_cannot_have_an_initializer.Code,
        Diagnostics.Class_decorators_can_t_be_used_with_static_private_identifier_Consider_removing_the_experimental_decorator.Code,
        Diagnostics.Classes_can_only_extend_a_single_class.Code,
        Diagnostics.Classes_may_not_have_a_field_named_constructor.Code,
        Diagnostics.Did_you_mean_to_use_a_Colon_An_can_only_follow_a_property_name_when_the_containing_object_literal_is_part_of_a_destructuring_pattern.Code,
        Diagnostics.Duplicate_label_0.Code,
        Diagnostics.Dynamic_imports_can_only_accept_a_module_specifier_and_an_optional_set_of_attributes_as_arguments.Code,
        Diagnostics.for_await_loops_cannot_be_used_inside_a_class_static_block.Code,
        Diagnostics.JSX_attributes_must_only_be_assigned_a_non_empty_expression.Code,
        Diagnostics.JSX_elements_cannot_have_multiple_attributes_with_the_same_name.Code,
        Diagnostics.JSX_expressions_may_not_use_the_comma_operator_Did_you_mean_to_write_an_array.Code,
        Diagnostics.JSX_property_access_expressions_cannot_include_JSX_namespace_names.Code,
        Diagnostics.Jump_target_cannot_cross_function_boundary.Code,
        Diagnostics.Line_terminator_not_permitted_before_arrow.Code,
        Diagnostics.Modifiers_cannot_appear_here.Code,
        Diagnostics.Only_a_single_variable_declaration_is_allowed_in_a_for_in_statement.Code,
        Diagnostics.Only_a_single_variable_declaration_is_allowed_in_a_for_of_statement.Code,
        Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies.Code,
        Diagnostics.Private_identifiers_are_only_allowed_in_class_bodies_and_may_only_be_used_as_part_of_a_class_member_declaration_property_access_or_on_the_left_hand_side_of_an_in_expression.Code,
        Diagnostics.Property_0_is_not_accessible_outside_class_1_because_it_has_a_private_identifier.Code,
        Diagnostics.Tagged_template_expressions_are_not_permitted_in_an_optional_chain.Code,
        Diagnostics.The_left_hand_side_of_a_for_of_statement_may_not_be_async.Code,
        Diagnostics.The_variable_declaration_of_a_for_in_statement_cannot_have_an_initializer.Code,
        Diagnostics.The_variable_declaration_of_a_for_of_statement_cannot_have_an_initializer.Code,
        Diagnostics.Trailing_comma_not_allowed.Code,
        Diagnostics.Variable_declaration_list_cannot_be_empty.Code,
        Diagnostics._0_and_1_operations_cannot_be_mixed_without_parentheses.Code,
        Diagnostics._0_expected.Code,
        Diagnostics._0_is_not_a_valid_meta_property_for_keyword_1_Did_you_mean_2.Code,
        Diagnostics._0_list_cannot_be_empty.Code,
        Diagnostics._0_modifier_already_seen.Code,
        Diagnostics._0_modifier_cannot_appear_on_a_constructor_declaration.Code,
        Diagnostics._0_modifier_cannot_appear_on_a_module_or_namespace_element.Code,
        Diagnostics._0_modifier_cannot_appear_on_a_parameter.Code,
        Diagnostics._0_modifier_cannot_appear_on_class_elements_of_this_kind.Code,
        Diagnostics._0_modifier_cannot_be_used_here.Code,
        Diagnostics._0_modifier_must_precede_1_modifier.Code,
        Diagnostics._0_declarations_can_only_be_declared_inside_a_block.Code,
        Diagnostics._0_declarations_must_be_initialized.Code,
        Diagnostics.extends_clause_already_seen.Code,
        Diagnostics.let_is_not_allowed_to_be_used_as_a_name_in_let_or_const_declarations.Code,
        Diagnostics.Class_constructor_may_not_be_a_generator.Code,
        Diagnostics.Class_constructor_may_not_be_an_accessor.Code,
        Diagnostics.await_expressions_are_only_allowed_within_async_functions_and_at_the_top_levels_of_modules.Code,
        Diagnostics.await_using_statements_are_only_allowed_within_async_functions_and_at_the_top_levels_of_modules.Code,
        Diagnostics.Private_field_0_must_be_declared_in_an_enclosing_class.Code,
        // Type errors
        Diagnostics.This_condition_will_always_return_0_since_JavaScript_compares_objects_by_reference_not_value.Code,
    ];
}
