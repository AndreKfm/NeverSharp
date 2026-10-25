// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (createTypeChecker: the checker's state, initializeTypeChecker, the diagnostics
// collection and the public surface) and src/compiler/utilities.ts (createDiagnosticCollection, compareDiagnostics)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go (Checker struct, NewChecker, initializeChecker)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#. Module C0: the skeleton (per-checker storage, docs/phase3-checker-layout.md).
// Module C1: the checker core: global symbol table and library merging, intrinsic and global types, the diagnostics
// collection; the other parts are in Checker.*.cs (symbols, name resolution, types, members, locations, printing,
// the check walk). Later modules (C2-C9) are marked "TODO(Cn)" where their parts are stubbed.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// checker.ts: the TypeChecker of one program (createTypeChecker(host)); typescript-go: checker.Checker. All the
/// checker's data (types, signatures, transient symbols, symbol and node links, diagnostics) lives in this instance;
/// the program's binder output is read, never written (library files are shared between checkers on other threads).
/// One thread uses a checker at a time.
/// </summary>
internal sealed partial class Checker
{
    private readonly Program program;
    private readonly CompilerOptions compilerOptions;
    private readonly Dictionary<SourceFile, int> fileIndex = new(ReferenceEqualityComparer.Instance);
    private readonly DiagnosticCollection diagnostics = new();
    private readonly DiagnosticCollection suggestionDiagnostics = new();
    private readonly List<Action> deferredDiagnostics = [];

    // compiler options the checker reads (createTypeChecker's vars)
    private readonly ScriptTarget languageVersion;
    private readonly ModuleKind moduleKind;
    private readonly bool strictNullChecks;
    private readonly bool noImplicitAny;
    private readonly bool exactOptionalPropertyTypes;
    private readonly bool noUncheckedSideEffectImports;

    internal Checker(Program program)
    {
        ArgumentNullException.ThrowIfNull(program);
        this.program = program;
        compilerOptions = program.Options;
        var files = program.SourceFiles;
        for (var i = 0; i < files.Count; i++)
        {
            fileIndex[files[i]] = i + 1;
        }
        Types = new TypeStore();
        Signatures = new SignatureStore();
        TransientSymbols = new TransientSymbolStore();
        ValueSymbolLinks = new SymbolLinkStore<ValueSymbolLinks>(files.Count);
        AliasSymbolLinks = new SymbolLinkStore<AliasSymbolLinks>(files.Count);
        DeclaredTypeLinks = new SymbolLinkStore<DeclaredTypeLinks>(files.Count);
        NodeLinks = new NodeLinkStore<NodeLinks>(files.Count);
        SymbolNodeLinks = new NodeLinkStore<SymbolNodeLinks>(files.Count);
        TypeNodeLinks = new NodeLinkStore<TypeNodeLinks>(files.Count);
        SignatureLinks = new NodeLinkStore<SignatureLinks>(files.Count);
        binderNames = new string?[files.Count + 1][];
        for (var i = 0; i < binderNames.Length; i++)
        {
            binderNames[i] = [];
        }

        languageVersion = compilerOptions.GetEmitScriptTarget();
        moduleKind = compilerOptions.GetEmitModuleKind();
        strictNullChecks = compilerOptions.GetStrictOptionValue(compilerOptions.StrictNullChecks);
        noImplicitAny = compilerOptions.GetStrictOptionValue(compilerOptions.NoImplicitAny);
        exactOptionalPropertyTypes = compilerOptions.ExactOptionalPropertyTypes == true;
        noUncheckedSideEffectImports = true; // compilerOptions.noUncheckedSideEffectImports !== false (the option is not in Track O's options yet)

        // createTypeChecker's symbols, in TypeScript's order
        globals = new SymTable();
        undefinedSymbol = CreateSymbol(SymbolFlags.Property, "undefined");
        TransientSymbols.Record(undefinedSymbol).Declarations = [];
        globalThisSymbol = CreateSymbol(SymbolFlags.Module, "globalThis", CheckFlags.Readonly);
        TransientSymbols.Record(globalThisSymbol).Exports = globals;
        TransientSymbols.Record(globalThisSymbol).Declarations = [];
        globals.Set("globalThis", globalThisSymbol);
        argumentsSymbol = CreateSymbol(SymbolFlags.Property, "arguments");
        requireSymbol = CreateSymbol(SymbolFlags.Property, "require");
        unknownSymbol = CreateSymbol(SymbolFlags.Property, "unknown");
        resolvingSymbol = CreateSymbol(SymbolFlags.None, InternalSymbolName.Resolving);
        CreateIntrinsicTypes();
        InitializeTypeChecker();
    }

    internal Program Program => program;

    internal CompilerOptions CompilerOptions => compilerOptions;

    internal TypeStore Types { get; }

    internal SignatureStore Signatures { get; }

    internal TransientSymbolStore TransientSymbols { get; }

    internal SymbolLinkStore<ValueSymbolLinks> ValueSymbolLinks { get; }

    internal SymbolLinkStore<AliasSymbolLinks> AliasSymbolLinks { get; }

    internal SymbolLinkStore<DeclaredTypeLinks> DeclaredTypeLinks { get; }

    internal NodeLinkStore<NodeLinks> NodeLinks { get; }

    internal NodeLinkStore<SymbolNodeLinks> SymbolNodeLinks { get; }

    internal NodeLinkStore<TypeNodeLinks> TypeNodeLinks { get; }

    internal NodeLinkStore<SignatureLinks> SignatureLinks { get; }

    /// <summary>The program's source file of a <see cref="NodeRef"/> or <see cref="SymbolRef"/> file number (1-based).</summary>
    internal SourceFile FileAt(int file) => program.SourceFiles[file - 1];

    /// <summary>The file number of a program source file (1-based; 0 if the file is not in the program).</summary>
    internal int FileNumber(SourceFile file) => fileIndex.TryGetValue(file, out var i) ? i : 0;

    /// <summary>A node of <paramref name="file"/> as a program-wide reference.</summary>
    internal NodeRef Ref(SourceFile file, Node node) => new(FileNumber(file), node);

    /// <summary>
    /// checker.ts initializeTypeChecker: binds the files, merges the globals of the script files (library files and the
    /// program's scripts) and the global augmentations, adds `undefined`, sets up the global types and reports the
    /// duplicate declarations across files.
    /// </summary>
    private void InitializeTypeChecker()
    {
        foreach (var file in program.SourceFiles)
        {
            Binder.BindSourceFile(file, compilerOptions);
        }

        amalgamatedDuplicates = [];

        // Initialize global symbol table
        List<(int File, List<Node> Names)>? augmentations = null;
        var files = program.SourceFiles;
        for (var f = 1; f <= files.Count; f++)
        {
            var file = files[f - 1];
            var store = file.Store;
            var binding = file.Binding!;
            var locals = binding.LocalsOf(file.Root);
            if (!Utilities.IsExternalOrCommonJsModule(store, file.Root, binding))
            {
                // It is an error for a non-external-module (i.e. script) to declare its own `globalThis`.
                if (!locals.IsNone)
                {
                    var fileGlobalThisSymbol = TableOf(f, locals).Get("globalThis");
                    if (!fileGlobalThisSymbol.IsNone && DeclarationsOf(fileGlobalThisSymbol) is { } globalThisDeclarations)
                    {
                        foreach (var declaration in globalThisDeclarations)
                        {
                            diagnostics.Add(CreateDiagnosticForNode(declaration, Diagnostics.Declaration_name_conflicts_with_built_in_global_identifier_0, "globalThis"));
                        }
                    }
                    MergeSymbolTable(globals, TableOf(f, locals));
                }
            }
            if (!binding.JsGlobalAugmentations.IsNone)
            {
                MergeSymbolTable(globals, TableOf(f, binding.JsGlobalAugmentations));
            }
            // TODO(C9): pattern ambient modules (`declare module "*.css"`) are collected here.
            var moduleAugmentations = CollectModuleAugmentations(store, file.Root, binding);
            if (moduleAugmentations.Count > 0)
            {
                (augmentations ??= []).Add((f, moduleAugmentations));
            }
            var fileSymbol = NodeSymbol(new NodeRef(f, file.Root));
            if (!fileSymbol.IsNone)
            {
                var globalExports = binding.GlobalExports(fileSymbol.BinderSymbol);
                if (!globalExports.IsNone)
                {
                    // Merge in UMD exports with first-in-wins semantics (see #9771)
                    var source = TableOf(f, globalExports);
                    for (var i = 0; i < source.Count; i++)
                    {
                        if (!globals.Has(source.NameAt(i)))
                        {
                            globals.Set(source.NameAt(i), source.ValueAt(i));
                        }
                    }
                }
            }
        }

        // We do global augmentations separately from module augmentations (and before creating global types)
        if (augmentations is not null)
        {
            foreach (var (f, list) in augmentations)
            {
                foreach (var augmentation in list)
                {
                    var store = FileAt(f).Store;
                    if (!Utilities.IsGlobalScopeAugmentation(store, store.Parent(augmentation)))
                    {
                        continue;
                    }
                    MergeModuleAugmentation(new NodeRef(f, augmentation));
                }
            }
        }

        AddUndefinedToGlobalsOrErrorOnRedeclaration();

        ValueSymbolLinks.Get(undefinedSymbol).ResolvedType = undefinedWideningType;
        ValueSymbolLinks.Get(argumentsSymbol).ResolvedType = GetGlobalType("IArguments", 0, reportErrors: true);
        ValueSymbolLinks.Get(unknownSymbol).ResolvedType = errorType;
        ValueSymbolLinks.Get(globalThisSymbol).ResolvedType = CreateObjectType(ObjectFlags.Anonymous, globalThisSymbol);

        // Initialize special types
        globalArrayType = GetGlobalType("Array", 1, reportErrors: true);
        globalObjectType = GetGlobalType("Object", 0, reportErrors: true);
        globalFunctionType = GetGlobalType("Function", 0, reportErrors: true);
        var strictBindCallApply = compilerOptions.GetStrictOptionValue(compilerOptions.StrictBindCallApply);
        globalCallableFunctionType = strictBindCallApply ? GetGlobalType("CallableFunction", 0, reportErrors: true) : globalFunctionType;
        if (globalCallableFunctionType.IsNone)
        {
            globalCallableFunctionType = globalFunctionType;
        }
        globalNewableFunctionType = strictBindCallApply ? GetGlobalType("NewableFunction", 0, reportErrors: true) : globalFunctionType;
        if (globalNewableFunctionType.IsNone)
        {
            globalNewableFunctionType = globalFunctionType;
        }
        globalStringType = GetGlobalType("String", 0, reportErrors: true);
        globalNumberType = GetGlobalType("Number", 0, reportErrors: true);
        globalBooleanType = GetGlobalType("Boolean", 0, reportErrors: true);
        globalRegExpType = GetGlobalType("RegExp", 0, reportErrors: true);
        anyArrayType = CreateArrayType(anyType);
        autoArrayType = CreateArrayType(autoType);
        if (autoArrayType == emptyObjectType)
        {
            autoArrayType = CreateAnonymousType(SymbolRef.None, emptySymbols);
        }
        globalReadonlyArrayType = GetGlobalTypeOrUndefined("ReadonlyArray", 1);
        if (globalReadonlyArrayType.IsNone)
        {
            globalReadonlyArrayType = globalArrayType;
        }
        anyReadonlyArrayType = !globalReadonlyArrayType.IsNone ? CreateTypeFromGenericGlobalType(globalReadonlyArrayType, [anyType]) : anyArrayType;
        globalThisType = GetGlobalTypeOrUndefined("ThisType", 1);

        if (augmentations is not null)
        {
            // merge _nonglobal_ module augmentations.
            foreach (var (f, list) in augmentations)
            {
                foreach (var augmentation in list)
                {
                    var store = FileAt(f).Store;
                    if (Utilities.IsGlobalScopeAugmentation(store, store.Parent(augmentation)))
                    {
                        continue;
                    }
                    MergeModuleAugmentation(new NodeRef(f, augmentation));
                }
            }
        }

        ReportAmalgamatedDuplicates();
        amalgamatedDuplicates = null;
    }

    /// <summary>
    /// program.ts collectExternalModuleReferences: the names of the file's module augmentations (sourceFile.moduleAugmentations,
    /// which TypeScript's program stores in the file; Track O computes them here and does not write the shared stores).
    /// </summary>
    private static List<Node> CollectModuleAugmentations(NodeStore store, Node file, BinderStore binding)
    {
        var result = new List<Node>();
        var isExternalModuleFile = Utilities.IsExternalOrCommonJsModule(store, file, binding);
        var isDeclarationFile = store.IsDeclarationFile(file);
        foreach (var statement in store.Nodes(store.Statements(file)))
        {
            Collect(statement, inAmbientModule: false);
        }
        return result;

        void Collect(Node node, bool inAmbientModule)
        {
            if (store.Kind(node) != SyntaxKind.ModuleDeclaration || !Utilities.IsAmbientModule(store, node))
            {
                return;
            }
            if (inAmbientModule || Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Ambient) || isDeclarationFile)
            {
                var name = store.Name(node);
                var nameText = store.Texts.GetString(Utilities.GetTextOfIdentifierOrLiteral(store, name))!;
                // Ambient module declarations can be interpreted as augmentations for some existing external modules.
                if (isExternalModuleFile || (inAmbientModule && !Utilities.IsExternalModuleNameRelative(nameText)))
                {
                    result.Add(name);
                }
                else if (!inAmbientModule)
                {
                    var body = store.Body(node);
                    if (!body.IsNone && store.Kind(body) == SyntaxKind.ModuleBlock)
                    {
                        foreach (var statement in store.Nodes(store.Statements(body)))
                        {
                            Collect(statement, inAmbientModule: true);
                        }
                    }
                }
            }
        }
    }

    // ---- diagnostics ----

    /// <summary>checker.ts createDiagnosticForNode (utilities.ts createDiagnosticForNodeInSourceFile).</summary>
    internal ProgramDiagnostic CreateDiagnosticForNode(NodeRef node, DiagnosticMessage message, params object?[] args)
    {
        var file = FileAt(node.File);
        var d = Utilities.CreateDiagnosticForNodeInSourceFile(file.Store, file.Root, node.Node, message, args);
        return ProgramDiagnostic.FromFile(file, d);
    }

    /// <summary>A related-information entry for a node (createDiagnosticForNode used as relatedInformation).</summary>
    internal ProgramRelatedInformation CreateRelatedForNode(NodeRef node, DiagnosticMessage message, params object?[] args)
    {
        var d = CreateDiagnosticForNode(node, message, args);
        return new ProgramRelatedInformation
        {
            SourceFile = d.SourceFile,
            File = d.File,
            StartOrUndefined = d.StartOrUndefined,
            LengthOrUndefined = d.LengthOrUndefined,
            Code = d.Code,
            Category = d.Category,
            MessageText = d.MessageText,
        };
    }

    /// <summary>checker.ts error(location, message, ...args): adds and returns the diagnostic.</summary>
    internal ProgramDiagnostic Error(NodeRef location, DiagnosticMessage message, params object?[] args)
    {
        var d = location.IsNone ? CreateCompilerDiagnostic(message, args) : CreateDiagnosticForNode(location, message, args);
        diagnostics.Add(d);
        return d;
    }

    /// <summary>checker.ts errorOrSuggestion(isError, location, message, ...args).</summary>
    internal void ErrorOrSuggestion(bool isError, NodeRef location, DiagnosticMessage message, params object?[] args)
    {
        // Error or suggestion only for files the checker reports on (suggestions are not reported in declaration files).
        var d = CreateDiagnosticForNode(location, message, args);
        if (isError)
        {
            diagnostics.Add(d);
        }
        else
        {
            d.Category = DiagnosticCategory.Suggestion;
            suggestionDiagnostics.Add(d);
        }
    }

    /// <summary>checker.ts lookupOrIssueError.</summary>
    internal ProgramDiagnostic LookupOrIssueError(NodeRef location, DiagnosticMessage message, params object?[] args)
    {
        var d = location.IsNone ? CreateCompilerDiagnostic(message, args) : CreateDiagnosticForNode(location, message, args);
        var existing = diagnostics.Lookup(d);
        if (existing is not null)
        {
            return existing;
        }
        diagnostics.Add(d);
        return d;
    }

    private static ProgramDiagnostic CreateCompilerDiagnostic(DiagnosticMessage message, params object?[] args)
    {
        var text = Utilities.GetLocaleSpecificMessage(message);
        if (args.Length > 0)
        {
            text = Utilities.FormatStringFromArgs(text, args);
        }
        return new ProgramDiagnostic
        {
            Code = message.Code,
            Category = message.Category,
            MessageText = text,
            ReportsUnnecessary = message.ReportsUnnecessary,
            ReportsDeprecated = message.ReportsDeprecated,
        };
    }

    /// <summary>checker.ts addLazyDiagnostic: run when the file's check completes (ensurePendingDiagnosticWorkComplete).</summary>
    private void AddLazyDiagnostic(Action diagnostic)
    {
        if (checkingFile)
        {
            deferredDiagnostics.Add(diagnostic);
        }
        else
        {
            diagnostic();
        }
    }

    private bool checkingFile;
    private readonly HashSet<int> checkedFiles = [];

    /// <summary>checker.ts getDiagnostics(sourceFile): checks the file (once) and returns its diagnostics.</summary>
    internal List<ProgramDiagnostic> GetDiagnostics(SourceFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var previousGlobal = diagnostics.GlobalCount;
        CheckSourceFileOnce(FileNumber(file));
        var result = new List<ProgramDiagnostic>();
        if (previousGlobal == 0 && diagnostics.GlobalCount > 0)
        {
            result.AddRange(diagnostics.GetGlobalDiagnostics());
        }
        else if (diagnostics.GlobalCount > previousGlobal)
        {
            // (relativeComplement of the global diagnostics before and after the check)
            var all = diagnostics.GetGlobalDiagnostics();
            result.AddRange(all.GetRange(previousGlobal, all.Count - previousGlobal));
        }
        result.AddRange(diagnostics.GetDiagnostics(file));
        return result;
    }

    private void CheckSourceFileOnce(int file)
    {
        if (file == 0 || !checkedFiles.Add(file))
        {
            return;
        }
        // skipTypeChecking: canIncludeBindAndCheckDiagnostics (JavaScript files: module C9)
        if (!CanIncludeBindAndCheckDiagnostics(file))
        {
            return;
        }
        checkingFile = true;
        try
        {
            CheckSourceFile(new NodeRef(file, FileAt(file).Root));
        }
        finally
        {
            checkingFile = false;
        }
        // ensurePendingDiagnosticWorkComplete
        for (var i = 0; i < deferredDiagnostics.Count; i++)
        {
            deferredDiagnostics[i]();
        }
        deferredDiagnostics.Clear();
    }

    // utilities.ts canIncludeBindAndCheckDiagnostics (plain and checked JavaScript files included since module C9a)
    private bool CanIncludeBindAndCheckDiagnostics(int file) => Program.CanIncludeBindAndCheckDiagnostics(FileAt(file), compilerOptions);

    /// <summary>checker.ts getSuggestionDiagnostics(file).</summary>
    internal List<ProgramDiagnostic> GetSuggestionDiagnostics(SourceFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var number = FileNumber(file);
        CheckSourceFileOnce(number);
        var result = suggestionDiagnostics.GetDiagnostics(file);
        if (checkedFiles.Contains(number) && CanIncludeBindAndCheckDiagnostics(number))
        {
            result.AddRange(GetUnusedSuggestions(number));
        }
        return result;
    }

    /// <summary>checker.ts getGlobalDiagnostics().</summary>
    internal List<ProgramDiagnostic> GetGlobalDiagnostics() => [.. diagnostics.GetGlobalDiagnostics()];

    private void CheckNode(NodeRef node)
    {
        if (node.File < 1 || node.File > program.SourceFiles.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(node), "not a node of this checker's program");
        }
    }

    /// <summary>types.go Type.Flags.</summary>
    internal TypeFlags GetTypeFlags(Type type) => Types.Flags(type);

    /// <summary>The intrinsic name of an intrinsic type (types.ts IntrinsicType.intrinsicName).</summary>
    internal string IntrinsicName(Type type) => Types.IntrinsicName(type);

    /// <summary>The declarations of a symbol (types.ts Symbol.declarations; null when undefined).</summary>
    internal List<NodeRef>? Declarations(SymbolRef symbol) => DeclarationsOf(symbol);
}

/// <summary>
/// utilities.ts createDiagnosticCollection: per file a list sorted by compareDiagnosticsSkipRelatedInformation without
/// duplicates (insertSorted with diagnosticsEqualityComparer), and the diagnostics without a file.
/// </summary>
internal sealed class DiagnosticCollection
{
    private readonly Dictionary<SourceFile, List<ProgramDiagnostic>> fileDiagnostics = new(ReferenceEqualityComparer.Instance);
    private readonly List<ProgramDiagnostic> nonFileDiagnostics = [];

    internal int GlobalCount => nonFileDiagnostics.Count;

    internal void Add(ProgramDiagnostic diagnostic)
    {
        List<ProgramDiagnostic> list;
        if (diagnostic.SourceFile is { } file)
        {
            if (!fileDiagnostics.TryGetValue(file, out var existing))
            {
                fileDiagnostics[file] = existing = [];
            }
            list = existing;
        }
        else
        {
            list = nonFileDiagnostics;
        }
        InsertSorted(list, diagnostic);
    }

    internal ProgramDiagnostic? Lookup(ProgramDiagnostic diagnostic)
    {
        var list = diagnostic.SourceFile is { } file ? fileDiagnostics.GetValueOrDefault(file) : nonFileDiagnostics;
        if (list is null)
        {
            return null;
        }
        var result = BinarySearch(list, diagnostic);
        if (result >= 0)
        {
            return list[result];
        }
        if (~result > 0 && EqualityComparer(diagnostic, list[~result - 1]))
        {
            return list[~result - 1];
        }
        return null;
    }

    internal List<ProgramDiagnostic> GetGlobalDiagnostics() => nonFileDiagnostics;

    internal List<ProgramDiagnostic> GetDiagnostics(SourceFile file) => fileDiagnostics.TryGetValue(file, out var list) ? [.. list] : [];

    private static void InsertSorted(List<ProgramDiagnostic> list, ProgramDiagnostic insert)
    {
        if (list.Count == 0)
        {
            list.Add(insert);
            return;
        }
        var insertIndex = BinarySearch(list, insert);
        if (insertIndex < 0)
        {
            var idx = ~insertIndex;
            if (idx > 0 && EqualityComparer(insert, list[idx - 1]))
            {
                return;
            }
            if (idx < list.Count && EqualityComparer(insert, list[idx]))
            {
                list[idx] = insert;
                return;
            }
            list.Insert(idx, insert);
        }
    }

    private static int BinarySearch(List<ProgramDiagnostic> list, ProgramDiagnostic value)
    {
        var low = 0;
        var high = list.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var c = CompareSkipRelatedInformation(list[middle], value);
            if (c == 0)
            {
                return middle;
            }
            if (c < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return ~low;
    }

    // utilities.ts diagnosticsEqualityComparer: same file, start, length, code and message text.
    private static bool EqualityComparer(ProgramDiagnostic a, ProgramDiagnostic b) => CompareSkipRelatedInformation(a, b) == 0;

    /// <summary>utilities.ts compareDiagnosticsSkipRelatedInformation.</summary>
    internal static int CompareSkipRelatedInformation(DiagnosticRelatedInformation d1, DiagnosticRelatedInformation d2)
    {
        var c = string.CompareOrdinal(FilePath(d1), FilePath(d2));
        if (c != 0)
        {
            return Math.Sign(c);
        }
        c = CompareValues(d1.StartOrUndefined, d2.StartOrUndefined);
        if (c != 0)
        {
            return c;
        }
        c = CompareValues(d1.LengthOrUndefined, d2.LengthOrUndefined);
        if (c != 0)
        {
            return c;
        }
        c = d1.Code.CompareTo(d2.Code);
        if (c != 0)
        {
            return Math.Sign(c);
        }
        return Math.Sign(string.CompareOrdinal(ProgramDiagnostics.FlattenDiagnosticMessageText(d1, "\n"), ProgramDiagnostics.FlattenDiagnosticMessageText(d2, "\n")));
    }

    private static string? FilePath(DiagnosticRelatedInformation d)
    {
        var file = d switch
        {
            ProgramDiagnostic p => p.SourceFile,
            ProgramRelatedInformation r => r.SourceFile,
            _ => null,
        };
        return file?.Store.FileName(file.Root);
    }

    // core.ts compareValues: undefined sorts first.
    private static int CompareValues(int? a, int? b) => a == b ? 0 : a is null ? -1 : b is null ? 1 : a.Value < b.Value ? -1 : 1;
}
