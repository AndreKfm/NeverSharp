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
// root files, the default library and lib reference directives with `noResolve: true` (no referenced files, type
// reference directives or module resolution: imports stay unresolved, as tsc's with noResolve; other options throw
// NotSupportedException), no project references, no old program.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// program.ts: Program. Holds the program's source files in TypeScript's order (default library files sorted by their
/// priority, then the other files in processing order), the diagnostics of file processing, and the type checker
/// (created on first use; it binds the files that are not bound yet, as checker.ts initializeTypeChecker does).
/// One thread uses a program at a time; library files are shared read-only (<see cref="DefaultLibrary"/>).
/// </summary>
internal sealed class Program
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
    private List<SourceFile>? processingDefaultLibFiles = [];
    private List<SourceFile>? processingOtherFiles = [];
    private readonly List<SourceFile> files;
    private Checker? checker;

    private Program(IReadOnlyList<string> rootNames, CompilerOptions options, ICompilerHost host)
    {
        this.options = options;
        this.host = host;
        currentDirectory = host.GetCurrentDirectory();
        defaultLibraryPath = host.GetDefaultLibLocation();
        RootNames = rootNames;

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
    }

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
    /// program.getOptionsDiagnostics(): the diagnostics of file processing without a file (e.g. TS6504 for a JavaScript
    /// root file without allowJs). (Option validation, verifyCompilerOptions, finds nothing for the options C0
    /// supports; TypeScript sorts and deduplicates these, sortAndDeduplicateDiagnostics: not needed while at most one
    /// exists per program, C8 ports it.)
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
        return result;
    }

    /// <summary>program.getGlobalDiagnostics(): the checker's global diagnostics.</summary>
    internal List<ProgramDiagnostic> GetGlobalDiagnostics() => GetTypeChecker().GetGlobalDiagnostics();

    /// <summary>
    /// program.getSyntacticDiagnostics(file): the parse diagnostics (for JavaScript files also the additional syntactic
    /// diagnostics, which need allowJs; not ported yet).
    /// </summary>
    internal List<ProgramDiagnostic> GetSyntacticDiagnostics(SourceFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (options.GetAllowJSCompilerOption() && file.Store.ScriptKind(file.Root) is ScriptKind.JS or ScriptKind.JSX)
        {
            throw new NotSupportedException("Program (module C0): the additional syntactic diagnostics of JavaScript files (module C9)");
        }
        var result = new List<ProgramDiagnostic>();
        foreach (var d in file.ParseDiagnostics)
        {
            result.Add(ProgramDiagnostic.FromFile(file, d));
        }
        return result;
    }

    /// <summary>
    /// program.getSemanticDiagnostics(file): getBindAndCheckDiagnosticsForFile, the bind diagnostics (for the script
    /// kinds that include them) followed by the checker's diagnostics. Module C0: no comment-directive filtering
    /// (@ts-ignore, @ts-expect-error), no program diagnostics of the file (e.g. unknown lib references); C8 completes.
    /// </summary>
    internal List<ProgramDiagnostic> GetSemanticDiagnostics(SourceFile file)
    {
        var checker = GetTypeChecker();
        var result = new List<ProgramDiagnostic>();
        var store = file.Store;
        var scriptKind = store.ScriptKind(file.Root);
        var isTsNoCheck = store.CheckJsDirective(file.Root) is { Enabled: false };
        // By default, only type-check .ts, .tsx, Deferred, plain JS, checked JS and External
        // - plain JS: .js files with no // ts-check and checkJs: undefined (module C9 adds the JavaScript kinds)
        var includeBindAndCheckDiagnostics = !isTsNoCheck && scriptKind is ScriptKind.TS or ScriptKind.TSX or ScriptKind.External or ScriptKind.Deferred;
        if (includeBindAndCheckDiagnostics && file.Binding is { } binding)
        {
            foreach (var d in binding.BindDiagnostics)
            {
                result.Add(ProgramDiagnostic.FromFile(file, d));
            }
        }
        if (includeBindAndCheckDiagnostics)
        {
            result.AddRange(checker.GetDiagnostics(file));
        }
        return result;
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
        // (processImportedModules: with noResolve no imported file is added; module resolution is the checker's
        // concern for diagnostics, imports stay unresolved.)
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
            // diagnostic of the file ("Cannot find lib definition for '{0}'."); TODO(C8).
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
}
