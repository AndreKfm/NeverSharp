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

    // compiler options the checker reads (createTypeChecker's vars)
    private readonly ScriptTarget languageVersion;
    private readonly ModuleKind moduleKind;
    private readonly bool strictNullChecks;
    private readonly bool noImplicitAny;
    private readonly bool exactOptionalPropertyTypes;
    private readonly bool noUncheckedSideEffectImports;

    // The empty jsDoc lists getJSDocTags gives nodes without comments while this checker runs (UtilitiesPublic.CheckerEmptyJsDocLists)
    private readonly HashSet<(NodeStore Store, int Node)> emptyJsDocLists = [];

    /// <summary>Makes this checker the one that runs on the current thread (its own record of JSDoc lookups).</summary>
    internal void EnterThread() => UtilitiesPublic.CheckerEmptyJsDocLists = emptyJsDocLists;

    internal Checker(Program program)
    {
        ArgumentNullException.ThrowIfNull(program);
        this.program = program;
        EnterThread();
        compilerOptions = program.Options;
        var files = program.SourceFiles;
        for (var i = 0; i < files.Count; i++)
        {
            fileIndex[files[i]] = i + 1;
        }
        // The program's module resolutions, if no checker pool has resolved them yet (module CO4: the program resolves
        // its files' imports after its walk, before they are bound)
        program.ResolveImportedModules(singleThreaded: true);
        // (initializeTypeChecker binds the files; done first here, as the per-symbol tables are laid out by the files'
        // symbol counts)
        foreach (var file in files)
        {
            Binder.BindSourceFile(file, compilerOptions);
        }
        symbolSlotBases = SymbolSlots.Bases(program);
        Types = new TypeStore();
        Signatures = new SignatureStore();
        TransientSymbols = new TransientSymbolStore(Names);
        emptySymbols = new SymTable(Names);
        ValueSymbolLinks = new SymbolLinkStore<ValueSymbolLinks>(symbolSlotBases);
        AliasSymbolLinks = new SymbolLinkStore<AliasSymbolLinks>(symbolSlotBases);
        ExportTypeLinks = new SymbolLinkStore<ExportTypeLinks>(symbolSlotBases);
        DeclaredTypeLinks = new SymbolLinkStore<DeclaredTypeLinks>(symbolSlotBases);
        NodeLinks = new NodeLinkStore<NodeLinks>(files.Count);
        SymbolNodeLinks = new NodeLinkStore<SymbolNodeLinks>(files.Count);
        TypeNodeLinks = new NodeLinkStore<TypeNodeLinks>(files.Count);
        SignatureLinks = new NodeLinkStore<SignatureLinks>(files.Count);
        binderNames = new SymbolSlots(symbolSlotBases);

        languageVersion = compilerOptions.GetEmitScriptTarget();
        moduleKind = compilerOptions.GetEmitModuleKind();
        strictNullChecks = compilerOptions.GetStrictOptionValue(compilerOptions.StrictNullChecks);
        noImplicitAny = compilerOptions.GetStrictOptionValue(compilerOptions.NoImplicitAny);
        exactOptionalPropertyTypes = compilerOptions.ExactOptionalPropertyTypes == true;
        noUncheckedSideEffectImports = true; // compilerOptions.noUncheckedSideEffectImports !== false (the option is not in Track O's options yet)

        // createTypeChecker's symbols, in TypeScript's order
        globals = new SymTable(Names);
        undefinedSymbol = CreateSymbol(SymbolFlags.Property, "undefined");
        TransientSymbols.Record(undefinedSymbol).Declarations = NewDeclarations([]);
        globalThisSymbol = CreateSymbol(SymbolFlags.Module, "globalThis", CheckFlags.Readonly);
        TransientSymbols.Record(globalThisSymbol).Exports = globals;
        TransientSymbols.Record(globalThisSymbol).Declarations = NewDeclarations([]);
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

    /// <summary>The escaped names of this checker's symbols and symbol tables as ids (module CO2).</summary>
    internal NameTable Names { get; } = new();

    // The layout of the per-symbol tables (SymbolSlots.Bases), shared by the symbol link stores.
    private readonly int[] symbolSlotBases;

    internal TypeStore Types { get; }

    internal SignatureStore Signatures { get; }

    internal TransientSymbolStore TransientSymbols { get; }

    internal SymbolLinkStore<ValueSymbolLinks> ValueSymbolLinks { get; }

    internal SymbolLinkStore<AliasSymbolLinks> AliasSymbolLinks { get; }

    internal SymbolLinkStore<ExportTypeLinks> ExportTypeLinks { get; }

    internal SymbolLinkStore<DeclaredTypeLinks> DeclaredTypeLinks { get; }

    internal NodeLinkStore<NodeLinks> NodeLinks { get; }

    internal NodeLinkStore<SymbolNodeLinks> SymbolNodeLinks { get; }

    internal NodeLinkStore<TypeNodeLinks> TypeNodeLinks { get; }

    internal NodeLinkStore<SignatureLinks> SignatureLinks { get; }

    /// <summary>The program's source file of a <see cref="NodeRef"/> or <see cref="SymbolRef"/> file number (1-based).</summary>
    internal SourceFile FileAt(int file) => program.SourceFiles[file - 1];

    /// <summary>The file number of a program source file (1-based; 0 if the file is not in the program).</summary>
    internal int FileNumber(SourceFile file) => fileIndex.TryGetValue(file, out var i) ? i : 0;

    // program.isSourceFileDefaultLibrary per file number (0: not computed yet, 1: a default library file, 2: not one)
    private sbyte[]? defaultLibraryFiles;

    /// <summary>program.isSourceFileDefaultLibrary(file) of a program file number, computed once per file.</summary>
    internal bool IsDefaultLibraryFile(int file)
    {
        defaultLibraryFiles ??= new sbyte[program.SourceFiles.Count + 1];
        var known = defaultLibraryFiles[file];
        if (known == 0)
        {
            known = program.IsSourceFileDefaultLibrary(FileAt(file)) ? (sbyte)1 : (sbyte)2;
            defaultLibraryFiles[file] = known;
        }
        return known == 1;
    }

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
            // file.patternAmbientModules (`declare module "*.css"`; module C10)
            CollectPatternAmbientModules(f);
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
        // (the types are computed before the links are written: the calls can grow them)
        var argumentsType = GetGlobalType("IArguments", 0, reportErrors: true);
        ValueSymbolLinks.Get(argumentsSymbol).ResolvedType = argumentsType;
        ValueSymbolLinks.Get(unknownSymbol).ResolvedType = errorType;
        var globalThisObjectType = CreateObjectType(ObjectFlags.Anonymous, globalThisSymbol);
        ValueSymbolLinks.Get(globalThisSymbol).ResolvedType = globalThisObjectType;

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
        amalgamatedDuplicatesByFiles = null;
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
        var span = Utilities.GetErrorSpanForNode(file.Store, file.Root, node.Node);
        return CreateProgramFileDiagnostic(file, span.Start, span.Length, message, args);
    }

    /// <summary>
    /// utilities.ts createFileDiagnostic as a program diagnostic of <paramref name="file"/>: the members
    /// Utilities.CreateFileDiagnostic gives the parser's diagnostic and ProgramDiagnostic.FromFile copies, built
    /// directly (module CO1: no intermediate diagnostic, the message formatted in this checker's builder).
    /// </summary>
    internal ProgramDiagnostic CreateProgramFileDiagnostic(SourceFile file, int start, int length, DiagnosticMessage message, object?[] args)
    {
        // assertDiagnosticLocation
        var textLength = file.Store.SourceFileText(file.Root).Chars.Length;
        Debug.AssertGreaterThanOrEqual(start, 0);
        Debug.AssertGreaterThanOrEqual(length, 0);
        Debug.AssertLessThanOrEqual(start, textLength);
        Debug.AssertLessThanOrEqual(start + length, textLength);
        var text = Utilities.GetLocaleSpecificMessage(message);
        if (args.Length != 0)
        {
            text = FormatStringFromArgs(text, args);
        }
        return new ProgramDiagnostic
        {
            SourceFile = file,
            File = file.Root,
            StartOrUndefined = start,
            LengthOrUndefined = length,
            MessageText = text,
            Category = message.Category,
            Code = message.Code,
            ReportsUnnecessary = message.ReportsUnnecessary,
            ReportsDeprecated = message.ReportsDeprecated,
        };
    }

    // The builder FormatStringFromArgs reuses (a message is complete before the next one is formatted)
    private readonly System.Text.StringBuilder messageBuilder = new();

    /// <summary>utilities.ts formatStringFromArgs (Utilities.FormatStringFromArgs with this checker's builder).</summary>
    private string FormatStringFromArgs(string text, object?[] args)
    {
        var result = messageBuilder;
        var started = false;
        var last = 0;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '{')
            {
                var j = i + 1;
                while (j < text.Length && text[j] >= '0' && text[j] <= '9')
                {
                    j++;
                }
                if (j > i + 1 && j < text.Length && text[j] == '}')
                {
                    if (!started)
                    {
                        result.Clear();
                        started = true;
                    }
                    result.Append(text, last, i - last);
                    // `+index`: digits only, so the number is a non-negative integer (or beyond any index).
                    var index = long.TryParse(text.AsSpan(i + 1, j - i - 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n < args.Length ? (int)n : -1;
                    // JavaScript's `"" + value` for the values of DiagnosticArguments (string | number)
                    switch (Debug.CheckDefined(index >= 0 ? args[index] : null))
                    {
                        case string s:
                            result.Append(s);
                            break;
                        case int k:
                            result.Append(System.Globalization.CultureInfo.InvariantCulture, $"{k}");
                            break;
                        case double d:
                            result.Append(JsNumber.ToString(d));
                            break;
                        case var other:
                            result.Append(Debug.Stringify(other));
                            break;
                    }
                    i = j + 1;
                    last = i;
                    continue;
                }
            }
            i++;
        }
        if (!started)
        {
            return text;
        }
        result.Append(text, last, text.Length - last);
        var formatted = result.ToString();
        if (result.Capacity > 4096)
        {
            result.Clear();
            result.Capacity = 256;
        }
        return formatted;
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

    // checker.ts deferredDiagnosticsCallbacks, and whether addLazyDiagnostic currently runs its argument at once
    // (checkSourceFileWithEagerDiagnostics replaces addLazyDiagnostic by `cb => cb()` while it checks a file)
    private List<Action> deferredDiagnosticsCallbacks = [];
    private bool eagerLazyDiagnostics;

    /// <summary>
    /// checker.ts addLazyDiagnostic. Files are only checked for their diagnostics (checkSourceFileWithEagerDiagnostics),
    /// which run lazy diagnostics immediately; outside a file check (initializeTypeChecker: name resolution errors of
    /// aliases resolved while the globals are merged) they wait for ensurePendingDiagnosticWorkComplete.
    /// </summary>
    private void AddLazyDiagnostic(Action diagnostic)
    {
        if (eagerLazyDiagnostics)
        {
            diagnostic();
        }
        else
        {
            deferredDiagnosticsCallbacks.Add(diagnostic);
        }
    }

    /// <summary>checker.ts ensurePendingDiagnosticWorkComplete.</summary>
    private void EnsurePendingDiagnosticWorkComplete()
    {
        // Invoke any existing lazy diagnostics to add them, clear the backlog of diagnostics
        // (for-of over the array: callbacks added by a callback run in the same loop)
        for (var i = 0; i < deferredDiagnosticsCallbacks.Count; i++)
        {
            deferredDiagnosticsCallbacks[i]();
        }
        deferredDiagnosticsCallbacks = [];
    }

    private readonly HashSet<int> checkedFiles = [];

    /// <summary>checker.ts getDiagnostics(sourceFile) (getDiagnosticsWorker): checks the file (once) and returns its diagnostics.</summary>
    internal List<ProgramDiagnostic> GetDiagnostics(SourceFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        EnsurePendingDiagnosticWorkComplete();
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
        // checkSourceFileWithEagerDiagnostics
        var oldEager = eagerLazyDiagnostics;
        eagerLazyDiagnostics = true;
        CheckSourceFile(new NodeRef(file, FileAt(file).Root));
        eagerLazyDiagnostics = oldEager;
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
    internal List<ProgramDiagnostic> GetGlobalDiagnostics()
    {
        EnsurePendingDiagnosticWorkComplete();
        return [.. diagnostics.GetGlobalDiagnostics()];
    }

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
    internal List<NodeRef>? Declarations(SymbolRef symbol) => DeclarationsOf(symbol) is { } declarations ? [.. declarations] : null;
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

    /// <summary>
    /// utilities.ts diagnosticsEqualityComparer: same file, start, length, code (the canonical head's when set) and
    /// head message text (the canonical head's when set).
    /// </summary>
    internal static bool EqualityComparer(ProgramDiagnostic a, ProgramDiagnostic b) =>
        string.Equals(FilePath(a), FilePath(b), StringComparison.Ordinal) &&
        CompareValues(a.StartOrUndefined, b.StartOrUndefined) == 0 &&
        CompareValues(a.LengthOrUndefined, b.LengthOrUndefined) == 0 &&
        DiagnosticCode(a) == DiagnosticCode(b) &&
        string.Equals(DiagnosticHeadMessage(a), DiagnosticHeadMessage(b), StringComparison.Ordinal);

    /// <summary>
    /// utilitiesPublic.ts sortAndDeduplicateDiagnostics (sortAndDeduplicate with compareDiagnostics and
    /// diagnosticsEqualityComparer): a stable sort, then runs of equal diagnostics are reduced to their first.
    /// </summary>
    internal static List<ProgramDiagnostic> SortAndDeduplicate(List<ProgramDiagnostic> diagnostics)
    {
        if (diagnostics.Count < 2)
        {
            return diagnostics;
        }
        var indexed = new (ProgramDiagnostic Diagnostic, int Index)[diagnostics.Count];
        for (var i = 0; i < indexed.Length; i++)
        {
            indexed[i] = (diagnostics[i], i);
        }
        Array.Sort(indexed, (a, b) =>
        {
            var c = CompareDiagnostics(a.Diagnostic, b.Diagnostic);
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        });
        var result = new List<ProgramDiagnostic>(indexed.Length) { indexed[0].Diagnostic };
        for (var i = 1; i < indexed.Length; i++)
        {
            if (!EqualityComparer(result[^1], indexed[i].Diagnostic))
            {
                result.Add(indexed[i].Diagnostic);
            }
        }
        return result;
    }

    // utilities.ts compareDiagnostics
    private static int CompareDiagnostics(DiagnosticRelatedInformation d1, DiagnosticRelatedInformation d2)
    {
        var c = CompareSkipRelatedInformation(d1, d2);
        return c != 0 ? c : CompareRelatedInformation(d1, d2);
    }

    // utilities.ts compareRelatedInformation: a diagnostic with more elaboration is *less than* one with less elaboration
    // that is otherwise similar.
    private static int CompareRelatedInformation(DiagnosticRelatedInformation d1, DiagnosticRelatedInformation d2)
    {
        var r1 = (d1 as Diagnostic)?.RelatedInformation;
        var r2 = (d2 as Diagnostic)?.RelatedInformation;
        if (r1 is null && r2 is null)
        {
            return 0;
        }
        if (r1 is not null && r2 is not null)
        {
            var c = r2.Count.CompareTo(r1.Count);
            if (c != 0)
            {
                return Math.Sign(c);
            }
            for (var i = 0; i < r1.Count; i++)
            {
                c = CompareDiagnostics(r1[i], r2[i]);
                if (c != 0)
                {
                    return c;
                }
            }
            return 0;
        }
        return r1 is not null ? -1 : 1;
    }

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
        c = DiagnosticCode(d1).CompareTo(DiagnosticCode(d2));
        if (c != 0)
        {
            return Math.Sign(c);
        }
        return CompareMessageText(d1, d2);
    }

    // utilities.ts getDiagnosticCode / getDiagnosticMessage (the canonical head's when set)
    private static int DiagnosticCode(DiagnosticRelatedInformation d) => d is ProgramDiagnostic { CanonicalHead: { } head } && head.Code != 0 ? head.Code : d.Code;

    private static string DiagnosticHeadMessage(DiagnosticRelatedInformation d) =>
        d is ProgramDiagnostic { CanonicalHead: { } head } && head.MessageText.Length > 0 ? head.MessageText : d.MessageText;

    private static List<DiagnosticMessageChain>? ChainNext(DiagnosticRelatedInformation d) => d switch
    {
        ProgramDiagnostic p => p.Chain?.Next,
        ProgramRelatedInformation r => r.Chain?.Next,
        _ => null,
    };

    // utilities.ts compareMessageText: a diagnostic message with more elaboration is *less than* one with less
    // elaboration that is otherwise similar.
    private static int CompareMessageText(DiagnosticRelatedInformation d1, DiagnosticRelatedInformation d2)
    {
        var res = Math.Sign(string.CompareOrdinal(DiagnosticHeadMessage(d1), DiagnosticHeadMessage(d2)));
        if (res != 0)
        {
            return res;
        }
        res = CompareMessageChain(ChainNext(d1), ChainNext(d2));
        if (res != 0)
        {
            return res;
        }
        var h1 = d1 is ProgramDiagnostic { CanonicalHead: not null };
        var h2 = d2 is ProgramDiagnostic { CanonicalHead: not null };
        return h1 && !h2 ? -1 : h2 && !h1 ? 1 : 0;
    }

    // utilities.ts compareMessageChain: first by size of the message chain, then by content.
    private static int CompareMessageChain(List<DiagnosticMessageChain>? c1, List<DiagnosticMessageChain>? c2)
    {
        if (c1 is null && c2 is null)
        {
            return 0;
        }
        if (c1 is null)
        {
            return 1;
        }
        if (c2 is null)
        {
            return -1;
        }
        var res = CompareMessageChainSize(c1, c2);
        return res != 0 ? res : CompareMessageChainContent(c1, c2);
    }

    private static int CompareMessageChainSize(List<DiagnosticMessageChain>? c1, List<DiagnosticMessageChain>? c2)
    {
        if (c1 is null && c2 is null)
        {
            return 0;
        }
        if (c1 is null)
        {
            return 1;
        }
        if (c2 is null)
        {
            return -1;
        }
        var res = c2.Count.CompareTo(c1.Count);
        if (res != 0)
        {
            return Math.Sign(res);
        }
        for (var i = 0; i < c2.Count; i++)
        {
            res = CompareMessageChainSize(c1[i].Next, c2[i].Next);
            if (res != 0)
            {
                return res;
            }
        }
        return 0;
    }

    // Assumes the two chains have the same shape.
    private static int CompareMessageChainContent(List<DiagnosticMessageChain> c1, List<DiagnosticMessageChain> c2)
    {
        for (var i = 0; i < c2.Count; i++)
        {
            var res = Math.Sign(string.CompareOrdinal(c1[i].MessageText, c2[i].MessageText));
            if (res != 0)
            {
                return res;
            }
            if (c1[i].Next is null)
            {
                continue;
            }
            res = CompareMessageChainContent(c1[i].Next!, c2[i].Next!);
            if (res != 0)
            {
                return res;
            }
        }
        return 0;
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
