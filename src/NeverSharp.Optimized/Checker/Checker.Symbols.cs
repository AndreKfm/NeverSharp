// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (createSymbol, cloneSymbol, mergeSymbol, mergeSymbolTable, recordMergedSymbol,
// getMergedSymbol, getSymbolOfDeclaration, getSymbol, getExportsOfSymbol, getExportsOfModule, the duplicate-declaration
// reports of initializeTypeChecker, mergeModuleAugmentation, addUndefinedToGlobalsOrErrorOnRedeclaration)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go (symbol merging, getSymbol, getExportsOfSymbol)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C1. Binder symbols are read through SymbolRef accessors (their
// names, declarations and tables copied into checker-owned caches, nothing written into the binder stores); merged
// symbols are transient symbols of the checker; mergeId/mergedSymbols become a per-checker map.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private readonly SymTable globals;
    private readonly SymTable emptySymbols = new();
    private readonly Dictionary<SymbolRef, SymbolRef> mergedSymbols = [];
    private readonly SymbolRef undefinedSymbol;
    private readonly SymbolRef globalThisSymbol;
    private readonly SymbolRef argumentsSymbol;
    private readonly SymbolRef requireSymbol;
    private readonly SymbolRef unknownSymbol;
    private readonly SymbolRef resolvingSymbol;

    // Caches of binder data in checker-owned form (the binder stores are shared and read-only).
    private readonly string?[][] binderNames;
    private readonly Dictionary<long, SymTable> binderTables = [];
    private readonly Dictionary<SymbolRef, List<NodeRef>?> binderDeclarations = [];

    // ---- symbol accessors (binder or transient) ----

    internal BinderStore BindingOf(int file) => FileAt(file).Binding!;

    internal NodeStore StoreOf(int file) => FileAt(file).Store;

    internal NodeStore StoreOf(NodeRef node) => FileAt(node.File).Store;

    /// <summary>symbol.flags.</summary>
    internal SymbolFlags SymbolFlagsOf(SymbolRef symbol) =>
        symbol.IsTransient ? TransientSymbols.Record(symbol).Flags : BindingOf(symbol.File).Flags(symbol.BinderSymbol);

    /// <summary>symbol.escapedName.</summary>
    internal string NameOf(SymbolRef symbol)
    {
        if (symbol.IsTransient)
        {
            return TransientSymbols.Record(symbol).EscapedName;
        }
        var names = binderNames[symbol.File];
        if (symbol.Index >= names.Length)
        {
            Array.Resize(ref names, Math.Max(symbol.Index + 1, names.Length * 2));
            binderNames[symbol.File] = names;
        }
        return names[symbol.Index] ??= StoreOf(symbol.File).Texts.GetString(BindingOf(symbol.File).Name(symbol.BinderSymbol)) ?? "";
    }

    /// <summary>symbol.declarations (null when undefined; the list must not be modified).</summary>
    internal List<NodeRef>? DeclarationsOf(SymbolRef symbol)
    {
        if (symbol.IsNone)
        {
            return null;
        }
        if (symbol.IsTransient)
        {
            return TransientSymbols.Record(symbol).Declarations;
        }
        if (binderDeclarations.TryGetValue(symbol, out var cached))
        {
            return cached;
        }
        var chain = BindingOf(symbol.File).Declarations(symbol.BinderSymbol);
        List<NodeRef>? result = null;
        if (!chain.IsUndefined)
        {
            result = [];
            foreach (var node in chain)
            {
                result.Add(new NodeRef(symbol.File, node));
            }
        }
        binderDeclarations[symbol] = result;
        return result;
    }

    /// <summary>symbol.valueDeclaration.</summary>
    internal NodeRef ValueDeclarationOf(SymbolRef symbol)
    {
        if (symbol.IsTransient)
        {
            return TransientSymbols.Record(symbol).ValueDeclaration;
        }
        var node = BindingOf(symbol.File).ValueDeclaration(symbol.BinderSymbol);
        return node.IsNone ? NodeRef.None : new NodeRef(symbol.File, node);
    }

    /// <summary>symbol.parent.</summary>
    internal SymbolRef ParentOf(SymbolRef symbol)
    {
        if (symbol.IsTransient)
        {
            return TransientSymbols.Record(symbol).Parent;
        }
        var parent = BindingOf(symbol.File).Parent(symbol.BinderSymbol);
        return parent.IsNone ? SymbolRef.None : new SymbolRef(symbol.File, parent.Index);
    }

    /// <summary>symbol.exportSymbol.</summary>
    internal SymbolRef ExportSymbolOf(SymbolRef symbol)
    {
        if (symbol.IsTransient)
        {
            return TransientSymbols.Record(symbol).ExportSymbol;
        }
        var exportSymbol = BindingOf(symbol.File).ExportSymbol(symbol.BinderSymbol);
        return exportSymbol.IsNone ? SymbolRef.None : new SymbolRef(symbol.File, exportSymbol.Index);
    }

    /// <summary>symbol.members (null when undefined).</summary>
    internal SymTable? MembersOf(SymbolRef symbol)
    {
        if (symbol.IsTransient)
        {
            return TransientSymbols.Record(symbol).Members;
        }
        var table = BindingOf(symbol.File).Members(symbol.BinderSymbol);
        return table.IsNone ? null : TableOf(symbol.File, table);
    }

    /// <summary>symbol.exports (null when undefined).</summary>
    internal SymTable? ExportsOf(SymbolRef symbol)
    {
        if (symbol.IsTransient)
        {
            return TransientSymbols.Record(symbol).Exports;
        }
        var table = BindingOf(symbol.File).Exports(symbol.BinderSymbol);
        return table.IsNone ? null : TableOf(symbol.File, table);
    }

    /// <summary>symbol.constEnumOnlyModule.</summary>
    private bool ConstEnumOnlyModuleOf(SymbolRef symbol) =>
        symbol.IsTransient ? TransientSymbols.Record(symbol).ConstEnumOnlyModule : BindingOf(symbol.File).ConstEnumOnlyModule(symbol.BinderSymbol) == true;

    /// <summary>The checker-owned copy of a binder symbol table of <paramref name="file"/> (built on first use).</summary>
    internal SymTable TableOf(int file, SymbolTable table)
    {
        var key = ((long)file << 32) | (uint)table.Index;
        if (binderTables.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var binding = BindingOf(file);
        var result = new SymTable(binding.Count(table));
        foreach (var (_, symbol) in binding.Entries(table))
        {
            var reference = new SymbolRef(file, symbol.Index);
            result.Set(NameOf(reference), reference);
        }
        binderTables[key] = result;
        return result;
    }

    /// <summary>node.symbol (the binder's symbol of a node; None if it has none).</summary>
    internal SymbolRef NodeSymbol(NodeRef node)
    {
        if (node.IsNone)
        {
            return SymbolRef.None;
        }
        var symbol = BindingOf(node.File).SymbolOf(node.Node);
        return symbol.IsNone ? SymbolRef.None : new SymbolRef(node.File, symbol.Index);
    }

    /// <summary>node.localSymbol.</summary>
    internal SymbolRef LocalSymbolOfNode(NodeRef node)
    {
        var symbol = BindingOf(node.File).LocalSymbolOf(node.Node);
        return symbol.IsNone ? SymbolRef.None : new SymbolRef(node.File, symbol.Index);
    }

    /// <summary>node.locals (null when undefined).</summary>
    internal SymTable? LocalsOf(NodeRef node)
    {
        var table = BindingOf(node.File).LocalsOf(node.Node);
        return table.IsNone ? null : TableOf(node.File, table);
    }

    // ---- creation and merging ----

    /// <summary>checker.ts createSymbol(flags, name, checkFlags).</summary>
    internal SymbolRef CreateSymbol(SymbolFlags flags, string name, CheckFlags checkFlags = CheckFlags.None) =>
        TransientSymbols.Create(flags, name, checkFlags);

    private void RecordMergedSymbol(SymbolRef target, SymbolRef source) => mergedSymbols[source] = target;

    /// <summary>checker.ts cloneSymbol.</summary>
    private SymbolRef CloneSymbol(SymbolRef symbol)
    {
        var result = CreateSymbol(SymbolFlagsOf(symbol), NameOf(symbol));
        ref var r = ref TransientSymbols.Record(result);
        var declarations = DeclarationsOf(symbol);
        r.Declarations = declarations is null ? [] : [.. declarations];
        r.Parent = ParentOf(symbol);
        r.ValueDeclaration = ValueDeclarationOf(symbol);
        r.ConstEnumOnlyModule = ConstEnumOnlyModuleOf(symbol);
        var members = MembersOf(symbol);
        if (members is not null)
        {
            r.Members = new SymTable(members);
        }
        var exports = ExportsOf(symbol);
        if (exports is not null)
        {
            r.Exports = new SymTable(exports);
        }
        RecordMergedSymbol(result, symbol);
        return result;
    }

    /// <summary>checker.ts getMergedSymbol.</summary>
    internal SymbolRef GetMergedSymbol(SymbolRef symbol) =>
        !symbol.IsNone && mergedSymbols.TryGetValue(symbol, out var merged) ? merged : symbol;

    private static SymbolFlags GetExcludedSymbolFlags(SymbolFlags flags)
    {
        SymbolFlags result = 0;
        if ((flags & SymbolFlags.BlockScopedVariable) != 0) result |= SymbolFlags.BlockScopedVariableExcludes;
        if ((flags & SymbolFlags.FunctionScopedVariable) != 0) result |= SymbolFlags.FunctionScopedVariableExcludes;
        if ((flags & SymbolFlags.Property) != 0) result |= SymbolFlags.PropertyExcludes;
        if ((flags & SymbolFlags.EnumMember) != 0) result |= SymbolFlags.EnumMemberExcludes;
        if ((flags & SymbolFlags.Function) != 0) result |= SymbolFlags.FunctionExcludes;
        if ((flags & SymbolFlags.Class) != 0) result |= SymbolFlags.ClassExcludes;
        if ((flags & SymbolFlags.Interface) != 0) result |= SymbolFlags.InterfaceExcludes;
        if ((flags & SymbolFlags.RegularEnum) != 0) result |= SymbolFlags.RegularEnumExcludes;
        if ((flags & SymbolFlags.ConstEnum) != 0) result |= SymbolFlags.ConstEnumExcludes;
        if ((flags & SymbolFlags.ValueModule) != 0) result |= SymbolFlags.ValueModuleExcludes;
        if ((flags & SymbolFlags.Method) != 0) result |= SymbolFlags.MethodExcludes;
        if ((flags & SymbolFlags.GetAccessor) != 0) result |= SymbolFlags.GetAccessorExcludes;
        if ((flags & SymbolFlags.SetAccessor) != 0) result |= SymbolFlags.SetAccessorExcludes;
        if ((flags & SymbolFlags.TypeParameter) != 0) result |= SymbolFlags.TypeParameterExcludes;
        if ((flags & SymbolFlags.TypeAlias) != 0) result |= SymbolFlags.TypeAliasExcludes;
        if ((flags & SymbolFlags.Alias) != 0) result |= SymbolFlags.AliasExcludes;
        return result;
    }

    /// <summary>utilities.ts setValueDeclaration on a transient symbol.</summary>
    private void SetValueDeclaration(SymbolRef symbol, NodeRef node)
    {
        ref var r = ref TransientSymbols.Record(symbol);
        var valueDeclaration = r.ValueDeclaration;
        if (valueDeclaration.IsNone)
        {
            r.ValueDeclaration = node;
            return;
        }
        var nodeFlags = StoreOf(node).Flags(node.Node);
        var valueFlags = StoreOf(valueDeclaration).Flags(valueDeclaration.Node);
        var nodeKind = Kind(node);
        var valueKind = Kind(valueDeclaration);
        static bool IsAssignmentDeclaration(SyntaxKind kind) => kind is SyntaxKind.BinaryExpression or SyntaxKind.PropertyAccessExpression
            or SyntaxKind.ElementAccessExpression or SyntaxKind.Identifier or SyntaxKind.CallExpression;
        if ((!((nodeFlags & NodeFlags.Ambient) != 0 && !Utilities.IsInJSFile(StoreOf(node), node.Node) && (valueFlags & NodeFlags.Ambient) == 0)
                && IsAssignmentDeclaration(valueKind) && !IsAssignmentDeclaration(nodeKind))
            || (valueKind != nodeKind && valueKind is SyntaxKind.ModuleDeclaration or SyntaxKind.Identifier))
        {
            r.ValueDeclaration = node;
        }
    }

    /// <summary>
    /// checker.ts mergeSymbol: if target is transient it is mutated and returned, otherwise a transient clone of it is
    /// made, mutated and returned.
    /// </summary>
    private SymbolRef MergeSymbol(SymbolRef target, SymbolRef source, bool unidirectional = false)
    {
        var targetFlags = SymbolFlagsOf(target);
        var sourceFlags = SymbolFlagsOf(source);
        if ((targetFlags & GetExcludedSymbolFlags(sourceFlags)) == 0 || ((sourceFlags | targetFlags) & SymbolFlags.Assignment) != 0)
        {
            if (source == target)
            {
                return target;
            }
            if (!target.IsTransient)
            {
                var resolvedTarget = ResolveSymbol(target);
                if (resolvedTarget == unknownSymbol)
                {
                    return source;
                }
                var resolvedFlags = SymbolFlagsOf(resolvedTarget);
                if ((resolvedFlags & GetExcludedSymbolFlags(sourceFlags)) == 0 || ((sourceFlags | resolvedFlags) & SymbolFlags.Assignment) != 0)
                {
                    target = CloneSymbol(resolvedTarget);
                }
                else
                {
                    ReportMergeSymbolError(target, source);
                    return source;
                }
            }
            ref var t = ref TransientSymbols.Record(target);
            // Javascript static-property-assignment declarations always merge, even though they are also values
            if ((sourceFlags & SymbolFlags.ValueModule) != 0 && (t.Flags & SymbolFlags.ValueModule) != 0 && t.ConstEnumOnlyModule && !ConstEnumOnlyModuleOf(source))
            {
                // reset flag when merging instantiated module into value module that has only const enums
                t.ConstEnumOnlyModule = false;
            }
            t.Flags |= sourceFlags;
            var sourceValueDeclaration = ValueDeclarationOf(source);
            if (!sourceValueDeclaration.IsNone)
            {
                SetValueDeclaration(target, sourceValueDeclaration);
            }
            if (DeclarationsOf(source) is { } sourceDeclarations)
            {
                (TransientSymbols.Record(target).Declarations ??= []).AddRange(sourceDeclarations);
            }
            var sourceMembers = MembersOf(source);
            if (sourceMembers is not null)
            {
                var targetMembers = TransientSymbols.Record(target).Members ??= new SymTable();
                MergeSymbolTable(targetMembers, sourceMembers, unidirectional);
            }
            var sourceExports = ExportsOf(source);
            if (sourceExports is not null)
            {
                var targetExports = TransientSymbols.Record(target).Exports ??= new SymTable();
                MergeSymbolTable(targetExports, sourceExports, unidirectional, target);
            }
            if (!unidirectional)
            {
                RecordMergedSymbol(target, source);
            }
        }
        else if ((targetFlags & SymbolFlags.NamespaceModule) != 0)
        {
            // Do not report an error when merging `var globalThis` with the built-in `globalThis`,
            // as we will already report a "Declaration name conflicts..." error, and this error
            // won't make much sense.
            if (target != globalThisSymbol)
            {
                var sourceDeclarations = DeclarationsOf(source);
                Error(sourceDeclarations is { Count: > 0 } ? GetNameOfDeclarationRef(sourceDeclarations[0]) : NodeRef.None,
                    Diagnostics.Cannot_augment_module_0_with_value_exports_because_it_resolves_to_a_non_module_entity, SymbolToString(target));
            }
        }
        else
        {
            ReportMergeSymbolError(target, source);
        }
        return target;
    }

    private void ReportMergeSymbolError(SymbolRef target, SymbolRef source)
    {
        var targetFlags = SymbolFlagsOf(target);
        var sourceFlags = SymbolFlagsOf(source);
        var isEitherEnum = ((targetFlags | sourceFlags) & SymbolFlags.Enum) != 0;
        var isEitherBlockScoped = ((targetFlags | sourceFlags) & SymbolFlags.BlockScopedVariable) != 0;
        var message = isEitherEnum ? Diagnostics.Enum_declarations_can_only_merge_with_namespace_or_other_enum_declarations
            : isEitherBlockScoped ? Diagnostics.Cannot_redeclare_block_scoped_variable_0
            : Diagnostics.Duplicate_identifier_0;
        var sourceDeclarations = DeclarationsOf(source);
        var targetDeclarations = DeclarationsOf(target);
        var sourceSymbolFile = sourceDeclarations is { Count: > 0 } ? sourceDeclarations[0].File : 0;
        var targetSymbolFile = targetDeclarations is { Count: > 0 } ? targetDeclarations[0].File : 0;
        // (isPlainJsFile: JavaScript files are not in Track O's programs yet, module C9)
        var symbolName = SymbolToString(source);

        // Collect top-level duplicate identifier errors into one mapping, so we can then merge their diagnostics if there are a bunch
        if (sourceSymbolFile != 0 && targetSymbolFile != 0 && amalgamatedDuplicates is not null && !isEitherEnum && sourceSymbolFile != targetSymbolFile)
        {
            var sourcePath = FileNameOf(sourceSymbolFile);
            var targetPath = FileNameOf(targetSymbolFile);
            var firstFile = string.CompareOrdinal(sourcePath, targetPath) < 0 ? sourceSymbolFile : targetSymbolFile;
            var secondFile = firstFile == sourceSymbolFile ? targetSymbolFile : sourceSymbolFile;
            var key = FileNameOf(firstFile) + "|" + FileNameOf(secondFile);
            var filesDuplicates = amalgamatedDuplicates.Find(d => d.Key == key);
            if (filesDuplicates is null)
            {
                filesDuplicates = new DuplicateInfoForFiles(key, firstFile, secondFile);
                amalgamatedDuplicates.Add(filesDuplicates);
            }
            var conflictingSymbolInfo = filesDuplicates.ConflictingSymbols.Find(c => c.Name == symbolName);
            if (conflictingSymbolInfo is null)
            {
                conflictingSymbolInfo = new DuplicateInfoForSymbol(symbolName, isEitherBlockScoped);
                filesDuplicates.ConflictingSymbols.Add(conflictingSymbolInfo);
            }
            AddDuplicateLocations(conflictingSymbolInfo.FirstFileLocations, source);
            AddDuplicateLocations(conflictingSymbolInfo.SecondFileLocations, target);
        }
        else
        {
            AddDuplicateDeclarationErrorsForSymbols(source, message, symbolName, target);
            AddDuplicateDeclarationErrorsForSymbols(target, message, symbolName, source);
        }
    }

    private void AddDuplicateLocations(List<NodeRef> locations, SymbolRef symbol)
    {
        if (DeclarationsOf(symbol) is { } declarations)
        {
            foreach (var declaration in declarations)
            {
                if (!locations.Contains(declaration))
                {
                    locations.Add(declaration);
                }
            }
        }
    }

    private void AddDuplicateDeclarationErrorsForSymbols(SymbolRef target, DiagnosticMessage message, string symbolName, SymbolRef source)
    {
        if (DeclarationsOf(target) is { } declarations)
        {
            foreach (var node in declarations)
            {
                AddDuplicateDeclarationError(node, message, symbolName, DeclarationsOf(source));
            }
        }
    }

    private void AddDuplicateDeclarationError(NodeRef node, DiagnosticMessage message, string symbolName, List<NodeRef>? relatedNodes)
    {
        // (getExpandoInitializer / getNameOfExpando: JavaScript expando declarations, module C9)
        var errorNode = GetNameOfDeclarationRef(node);
        if (errorNode.IsNone)
        {
            errorNode = node;
        }
        var err = LookupOrIssueError(errorNode, message, symbolName);
        foreach (var relatedNode in relatedNodes ?? [])
        {
            var adjustedNode = GetNameOfDeclarationRef(relatedNode);
            if (adjustedNode.IsNone)
            {
                adjustedNode = relatedNode;
            }
            if (adjustedNode == errorNode)
            {
                continue;
            }
            err.RelatedInformation ??= [];
            var leadingMessage = CreateRelatedForNode(adjustedNode, Diagnostics._0_was_also_declared_here, symbolName);
            var followOnMessage = CreateRelatedForNode(adjustedNode, Diagnostics.and_here);
            if (err.RelatedInformation.Count >= 5 || err.RelatedInformation.Exists(r =>
                    DiagnosticCollection.CompareSkipRelatedInformation(r, followOnMessage) == 0 || DiagnosticCollection.CompareSkipRelatedInformation(r, leadingMessage) == 0))
            {
                continue;
            }
            err.RelatedInformation.Add(err.RelatedInformation.Count == 0 ? leadingMessage : followOnMessage);
        }
    }

    private sealed class DuplicateInfoForFiles(string key, int firstFile, int secondFile)
    {
        public string Key { get; } = key;
        public int FirstFile { get; } = firstFile;
        public int SecondFile { get; } = secondFile;
        public List<DuplicateInfoForSymbol> ConflictingSymbols { get; } = [];
    }

    private sealed class DuplicateInfoForSymbol(string name, bool isBlockScoped)
    {
        public string Name { get; } = name;
        public bool IsBlockScoped { get; } = isBlockScoped;
        public List<NodeRef> FirstFileLocations { get; } = [];
        public List<NodeRef> SecondFileLocations { get; } = [];
    }

    private List<DuplicateInfoForFiles>? amalgamatedDuplicates;

    // initializeTypeChecker: amalgamatedDuplicates.forEach(...)
    private void ReportAmalgamatedDuplicates()
    {
        foreach (var files in amalgamatedDuplicates!)
        {
            // If not many things conflict, issue individual errors
            if (files.ConflictingSymbols.Count < 8)
            {
                foreach (var info in files.ConflictingSymbols)
                {
                    var message = info.IsBlockScoped ? Diagnostics.Cannot_redeclare_block_scoped_variable_0 : Diagnostics.Duplicate_identifier_0;
                    foreach (var node in info.FirstFileLocations)
                    {
                        AddDuplicateDeclarationError(node, message, info.Name, info.SecondFileLocations);
                    }
                    foreach (var node in info.SecondFileLocations)
                    {
                        AddDuplicateDeclarationError(node, message, info.Name, info.FirstFileLocations);
                    }
                }
            }
            else
            {
                // Otherwise issue top-level error since the files appear very identical in terms of what they contain
                var list = string.Join(", ", files.ConflictingSymbols.ConvertAll(c => c.Name));
                var first = new NodeRef(files.FirstFile, FileAt(files.FirstFile).Root);
                var second = new NodeRef(files.SecondFile, FileAt(files.SecondFile).Root);
                var d1 = CreateDiagnosticForNode(first, Diagnostics.Definitions_of_the_following_identifiers_conflict_with_those_in_another_file_Colon_0, list);
                d1.RelatedInformation = [CreateRelatedForNode(second, Diagnostics.Conflicts_are_in_this_file)];
                diagnostics.Add(d1);
                var d2 = CreateDiagnosticForNode(second, Diagnostics.Definitions_of_the_following_identifiers_conflict_with_those_in_another_file_Colon_0, list);
                d2.RelatedInformation = [CreateRelatedForNode(first, Diagnostics.Conflicts_are_in_this_file)];
                diagnostics.Add(d2);
            }
        }
    }

    private string FileNameOf(int file)
    {
        var sourceFile = FileAt(file);
        return sourceFile.Store.FileName(sourceFile.Root);
    }

    /// <summary>checker.ts mergeSymbolTable.</summary>
    private void MergeSymbolTable(SymTable target, SymTable source, bool unidirectional = false, SymbolRef mergedParent = default)
    {
        for (var i = 0; i < source.Count; i++)
        {
            var id = source.NameAt(i);
            var sourceSymbol = source.ValueAt(i);
            var targetSymbol = target.Get(id);
            var merged = !targetSymbol.IsNone ? MergeSymbol(targetSymbol, sourceSymbol, unidirectional) : GetMergedSymbol(sourceSymbol);
            if (!mergedParent.IsNone && !targetSymbol.IsNone)
            {
                // If a merge was performed on the target symbol, set its parent to the merged parent that initiated the merge
                // of its exports. Otherwise, `merged` came only from `sourceSymbol` and can keep its parent.
                if (merged.IsTransient)
                {
                    TransientSymbols.Record(merged).Parent = mergedParent;
                }
            }
            target.Set(id, merged);
        }
    }

    /// <summary>checker.ts mergeModuleAugmentation.</summary>
    private void MergeModuleAugmentation(NodeRef moduleName)
    {
        var moduleAugmentation = Parent(moduleName);
        var augmentationSymbol = NodeSymbol(moduleAugmentation);
        var augmentationDeclarations = DeclarationsOf(augmentationSymbol);
        if (augmentationDeclarations is null || augmentationDeclarations[0] != moduleAugmentation)
        {
            // this is a combined symbol for multiple augmentations within the same file.
            // its symbol already has accumulated information for all declarations
            // so we need to add it just once - do the work only for first declaration
            return;
        }
        var store = StoreOf(moduleAugmentation);
        if (Utilities.IsGlobalScopeAugmentation(store, moduleAugmentation.Node))
        {
            MergeSymbolTable(globals, ExportsOf(augmentationSymbol) ?? emptySymbols);
        }
        else
        {
            // find a module that about to be augmented
            // do not validate names of augmentations that are defined in ambient context
            var moduleNotFoundError = (store.Flags(store.Parent(moduleAugmentation.Node)) & NodeFlags.Ambient) == 0
                ? Diagnostics.Invalid_module_name_in_augmentation_module_0_cannot_be_found
                : null;
            var mainModule = ResolveExternalModuleNameWorker(moduleName, moduleName, moduleNotFoundError, ignoreErrors: false, isForAugmentation: true);
            if (mainModule.IsNone)
            {
                return;
            }
            // obtain item referenced by 'export='
            mainModule = ResolveExternalModuleSymbol(mainModule);
            if ((SymbolFlagsOf(mainModule) & SymbolFlags.Namespace) != 0)
            {
                // TODO(C9): pattern ambient module augmentations and `export *` targets of the augmented module.
                mainModule = MergeSymbol(mainModule, augmentationSymbol);
            }
            else
            {
                Error(moduleName, Diagnostics.Cannot_augment_module_0_because_it_resolves_to_a_non_module_entity, StoreOf(moduleName).Texts.GetString(StoreOf(moduleName).Text(moduleName.Node)));
            }
        }
    }

    /// <summary>checker.ts addUndefinedToGlobalsOrErrorOnRedeclaration.</summary>
    private void AddUndefinedToGlobalsOrErrorOnRedeclaration()
    {
        var name = NameOf(undefinedSymbol);
        var targetSymbol = globals.Get(name);
        if (!targetSymbol.IsNone)
        {
            foreach (var declaration in DeclarationsOf(targetSymbol) ?? [])
            {
                // checkTypeNameIsReserved will have added better diagnostics for type declarations.
                if (!IsTypeDeclaration(declaration))
                {
                    diagnostics.Add(CreateDiagnosticForNode(declaration, Diagnostics.Declaration_name_conflicts_with_built_in_global_identifier_0, Utilities.UnescapeLeadingUnderscores(name)));
                }
            }
        }
        else
        {
            globals.Set(name, undefinedSymbol);
        }
    }

    /// <summary>utilities.ts isTypeDeclaration.</summary>
    private bool IsTypeDeclaration(NodeRef node)
    {
        switch (Kind(node))
        {
            case SyntaxKind.TypeParameter:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.JSDocTypedefTag:
            case SyntaxKind.JSDocCallbackTag:
            case SyntaxKind.JSDocEnumTag:
                return true;
            case SyntaxKind.ImportClause:
                return StoreOf(node).IsTypeOnly(node.Node);
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.ExportSpecifier:
            {
                var store = StoreOf(node);
                return store.IsTypeOnly(store.Parent(store.Parent(node.Node)));
            }
            default:
                return false;
        }
    }

    // ---- lookups ----

    /// <summary>checker.ts getSymbol(symbols, name, meaning): the symbol of the name if it has (or aliases) the meaning.</summary>
    internal SymbolRef GetSymbol(SymTable? symbols, string name, SymbolFlags meaning)
    {
        if (meaning != 0 && symbols is not null)
        {
            var symbol = GetMergedSymbol(symbols.Get(name));
            if (!symbol.IsNone)
            {
                var flags = SymbolFlagsOf(symbol);
                if ((flags & meaning) != 0)
                {
                    return symbol;
                }
                if ((flags & SymbolFlags.Alias) != 0)
                {
                    var targetFlags = GetSymbolFlags(symbol);
                    if ((targetFlags & meaning) != 0)
                    {
                        return symbol;
                    }
                }
            }
        }
        // return undefined if we can't find a symbol.
        return SymbolRef.None;
    }

    /// <summary>checker.ts getSymbolOfDeclaration (late-bound symbols: TODO(C3), dynamic member names).</summary>
    internal SymbolRef GetSymbolOfDeclaration(NodeRef node) => GetMergedSymbol(NodeSymbol(node));

    /// <summary>checker.ts getSymbolOfNode.</summary>
    internal SymbolRef GetSymbolOfNode(NodeRef node) =>
        Utilities.CanHaveSymbol(StoreOf(node), node.Node) ? GetSymbolOfDeclaration(node) : SymbolRef.None;

    /// <summary>checker.ts getParentOfSymbol.</summary>
    internal SymbolRef GetParentOfSymbol(SymbolRef symbol) => GetMergedSymbol(ParentOf(symbol));

    /// <summary>checker.ts getExportSymbolOfValueSymbolIfExported.</summary>
    internal SymbolRef GetExportSymbolOfValueSymbolIfExported(SymbolRef symbol)
    {
        if (symbol.IsNone)
        {
            return symbol;
        }
        var exportSymbol = (SymbolFlagsOf(symbol) & SymbolFlags.ExportValue) != 0 ? ExportSymbolOf(symbol) : SymbolRef.None;
        return GetMergedSymbol(!exportSymbol.IsNone ? exportSymbol : symbol);
    }

    /// <summary>utilities.ts getDeclarationOfKind.</summary>
    internal NodeRef GetDeclarationOfKind(SymbolRef symbol, SyntaxKind kind)
    {
        foreach (var declaration in DeclarationsOf(symbol) ?? [])
        {
            if (Kind(declaration) == kind)
            {
                return declaration;
            }
        }
        return NodeRef.None;
    }

    /// <summary>utilities.ts getLocalSymbolForExportDefault.</summary>
    private SymbolRef GetLocalSymbolForExportDefault(SymbolRef symbol)
    {
        if (!IsExportDefaultSymbol(symbol))
        {
            return SymbolRef.None;
        }
        foreach (var declaration in DeclarationsOf(symbol)!)
        {
            var localSymbol = LocalSymbolOfNode(declaration);
            if (!localSymbol.IsNone)
            {
                return localSymbol;
            }
        }
        return SymbolRef.None;
    }

    // utilities.ts isExportDefaultSymbol
    private bool IsExportDefaultSymbol(SymbolRef symbol)
    {
        var declarations = DeclarationsOf(symbol);
        if (symbol.IsNone || declarations is not { Count: > 0 })
        {
            return false;
        }
        var first = declarations[0];
        return (Utilities.GetSyntacticModifierFlagsUncached(StoreOf(first), first.Node) & ModifierFlags.Default) != 0;
    }

    /// <summary>checker.ts symbolIsValue.</summary>
    internal bool SymbolIsValue(SymbolRef symbol, bool includeTypeOnlyMembers = false)
    {
        var flags = SymbolFlagsOf(symbol);
        return (flags & SymbolFlags.Value) != 0
            || ((flags & SymbolFlags.Alias) != 0 && (GetSymbolFlags(symbol, !includeTypeOnlyMembers) & SymbolFlags.Value) != 0);
    }

    /// <summary>
    /// checker.ts getExportsOfSymbol. TODO(C3): late-bound members of classes, functions and literals
    /// (getResolvedMembersOrExportsOfSymbol); C1 uses the declared exports.
    /// </summary>
    internal SymTable GetExportsOfSymbol(SymbolRef symbol)
    {
        var flags = SymbolFlagsOf(symbol);
        return (flags & SymbolFlags.LateBindingContainer) != 0 ? ExportsOf(symbol) ?? emptySymbols
            : (flags & SymbolFlags.Module) != 0 ? GetExportsOfModule(symbol)
            : ExportsOf(symbol) ?? emptySymbols;
    }

    /// <summary>checker.ts getMembersOfSymbol (late-bound members: TODO(C3)).</summary>
    internal SymTable GetMembersOfSymbol(SymbolRef symbol) => MembersOf(symbol) ?? emptySymbols;

    private readonly Dictionary<SymbolRef, SymTable> resolvedModuleExports = [];

    /// <summary>checker.ts getExportsOfModule (links.resolvedExports).</summary>
    internal SymTable GetExportsOfModule(SymbolRef moduleSymbol)
    {
        if (resolvedModuleExports.TryGetValue(moduleSymbol, out var cached))
        {
            return cached;
        }
        var visited = new List<SymbolRef>();
        var resolved = ResolveExternalModuleSymbol(moduleSymbol);
        var exports = VisitModuleExports(resolved, visited) ?? emptySymbols;
        resolvedModuleExports[moduleSymbol] = exports;
        return exports;
    }

    // getExportsOfModuleWorker's visit (type-only export stars: TODO(C8)).
    private SymTable? VisitModuleExports(SymbolRef symbol, List<SymbolRef> visitedSymbols)
    {
        var symbolExports = symbol.IsNone ? null : ExportsOf(symbol);
        if (symbolExports is null || visitedSymbols.Contains(symbol))
        {
            return null;
        }
        visitedSymbols.Add(symbol);
        var symbols = new SymTable(symbolExports);
        // All export * declarations are collected in an __export symbol by the binder
        var exportStars = symbolExports.Get(InternalSymbolName.ExportStar);
        if (!exportStars.IsNone)
        {
            var nestedSymbols = new SymTable();
            var lookupTable = new Dictionary<string, (string SpecifierText, List<NodeRef>? ExportsWithDuplicate)>();
            var order = new List<string>();
            foreach (var node in DeclarationsOf(exportStars) ?? [])
            {
                var store = StoreOf(node);
                var specifier = store.ModuleSpecifier(node.Node);
                var resolvedModule = ResolveExternalModuleName(node, new NodeRef(node.File, specifier));
                var exportedSymbols = VisitModuleExports(resolvedModule, visitedSymbols);
                if (exportedSymbols is null)
                {
                    continue;
                }
                for (var i = 0; i < exportedSymbols.Count; i++)
                {
                    var id = exportedSymbols.NameAt(i);
                    if (id == InternalSymbolName.Default)
                    {
                        continue;
                    }
                    var sourceSymbol = exportedSymbols.ValueAt(i);
                    var targetSymbol = nestedSymbols.Get(id);
                    if (targetSymbol.IsNone)
                    {
                        nestedSymbols.Set(id, sourceSymbol);
                        lookupTable[id] = (Utilities.GetSourceTextOfNodeFromSourceFile(store, Utilities.RootOf(store, specifier), specifier), null);
                        order.Add(id);
                    }
                    else if (ResolveSymbol(targetSymbol) != ResolveSymbol(sourceSymbol))
                    {
                        var tracker = lookupTable[id];
                        (tracker.ExportsWithDuplicate ??= []).Add(node);
                        lookupTable[id] = tracker;
                    }
                }
            }
            foreach (var id in order)
            {
                var (specifierText, exportsWithDuplicate) = lookupTable[id];
                // It's not an error if the file with multiple `export *`s with duplicate names exports a member with that name itself
                if (id == InternalSymbolName.ExportEquals || exportsWithDuplicate is not { Count: > 0 } || symbols.Has(id))
                {
                    continue;
                }
                foreach (var node in exportsWithDuplicate)
                {
                    diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.Module_0_has_already_exported_a_member_named_1_Consider_explicitly_re_exporting_to_resolve_the_ambiguity,
                        specifierText, Utilities.UnescapeLeadingUnderscores(id)));
                }
            }
            for (var i = 0; i < nestedSymbols.Count; i++)
            {
                var id = nestedSymbols.NameAt(i);
                if (id != InternalSymbolName.Default && !symbols.Has(id))
                {
                    symbols.Set(id, nestedSymbols.ValueAt(i));
                }
            }
        }
        return symbols;
    }

    /// <summary>checker.ts resolveExternalModuleSymbol (getCommonJsExportEquals: TODO(C9), JavaScript).</summary>
    internal SymbolRef ResolveExternalModuleSymbol(SymbolRef moduleSymbol, bool dontResolveAlias = false)
    {
        var exports = moduleSymbol.IsNone ? null : ExportsOf(moduleSymbol);
        if (exports is not null)
        {
            var exportEquals = ResolveSymbol(exports.Get(InternalSymbolName.ExportEquals), dontResolveAlias);
            var exported = GetMergedSymbol(exportEquals);
            return !exported.IsNone ? exported : moduleSymbol;
        }
        return SymbolRef.None;
    }

    /// <summary>checker.ts tryFindAmbientModule.</summary>
    internal SymbolRef TryFindAmbientModule(string moduleName, bool withAugmentations)
    {
        if (Utilities.IsExternalModuleNameRelative(moduleName))
        {
            return SymbolRef.None;
        }
        var symbol = GetSymbol(globals, "\"" + moduleName + "\"", SymbolFlags.ValueModule);
        // merged symbol is module declaration symbol combined with all augmentations
        return !symbol.IsNone && withAugmentations ? GetMergedSymbol(symbol) : symbol;
    }

    // ---- node helpers ----

    internal SyntaxKind Kind(NodeRef node) => StoreOf(node).Kind(node.Node);

    internal NodeRef Parent(NodeRef node)
    {
        var parent = StoreOf(node).Parent(node.Node);
        return parent.IsNone ? NodeRef.None : new NodeRef(node.File, parent);
    }

    /// <summary>getNameOfDeclaration(declaration) as a reference (None when it has no name).</summary>
    internal NodeRef GetNameOfDeclarationRef(NodeRef declaration)
    {
        var name = Utilities.GetNameOfDeclaration(StoreOf(declaration), declaration.Node);
        return name.IsNone ? NodeRef.None : new NodeRef(declaration.File, name);
    }

    internal static NodeRef At(NodeRef context, Node node) => node.IsNone ? NodeRef.None : new NodeRef(context.File, node);
}
