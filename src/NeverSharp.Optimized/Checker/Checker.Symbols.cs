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
    private readonly SymTable emptySymbols;
    private readonly Dictionary<SymbolRef, SymbolRef> mergedSymbols = [];
    private readonly SymbolRef undefinedSymbol;
    private readonly SymbolRef globalThisSymbol;
    private readonly SymbolRef argumentsSymbol;
    private readonly SymbolRef requireSymbol;
    private readonly SymbolRef unknownSymbol;
    private readonly SymbolRef resolvingSymbol;

    // Caches of binder data in checker-owned form (the binder stores are shared and read-only).
    private readonly SymbolSlots binderNames; // binder symbol -> name id + 1 (0: not read yet)
    private readonly Dictionary<long, SymTable> binderTables = [];

    // The declarations arrays of transient symbols that are not a binder symbol's (module CO2)
    internal DeclarationStore DeclarationLists { get; } = new();

    // ---- symbol accessors (binder or transient) ----

    internal BinderStore BindingOf(int file) => FileAt(file).Binding!;

    internal NodeStore StoreOf(int file) => FileAt(file).Store;

    internal NodeStore StoreOf(NodeRef node) => FileAt(node.File).Store;

    /// <summary>symbol.flags.</summary>
    internal SymbolFlags SymbolFlagsOf(SymbolRef symbol) =>
        symbol.IsTransient ? TransientSymbols.Record(symbol).Flags : BindingOf(symbol.File).Flags(symbol.BinderSymbol);

    /// <summary>symbol.escapedName.</summary>
    internal string NameOf(SymbolRef symbol) => Names[NameIdOf(symbol)];

    /// <summary>symbol.escapedName as an id of <see cref="Names"/>.</summary>
    internal int NameIdOf(SymbolRef symbol)
    {
        if (symbol.IsTransient)
        {
            return TransientSymbols.Record(symbol).Name;
        }
        ref var slot = ref binderNames.Slot(symbol);
        if (slot == 0)
        {
            var text = BindingOf(symbol.File).Name(symbol.BinderSymbol);
            slot = (text.IsNone ? Names.Intern("") : Names.Intern(StoreOf(symbol.File).Texts.Span(text))) + 1;
        }
        return slot - 1;
    }

    /// <summary>symbol.declarations (null when undefined; module CO2: a view, the binder's chain read in place).</summary>
    internal Decls? DeclarationsOf(SymbolRef symbol) => symbol.IsNone ? null : ViewOf(DeclRefOf(symbol));

    /// <summary>symbol.declarations as a reference (assigning it to a transient symbol shares the array).</summary>
    internal DeclRef DeclRefOf(SymbolRef symbol) =>
        symbol.IsNone ? default
        : symbol.IsTransient ? TransientSymbols.Record(symbol).Declarations
        : BindingOf(symbol.File).Declarations(symbol.BinderSymbol).IsUndefined ? default
        : new DeclRef(symbol.File, symbol.Index);

    /// <summary>The declarations a reference stands for (null when undefined).</summary>
    internal Decls? ViewOf(DeclRef declarations)
    {
        if (declarations.File > 0)
        {
            var chain = BindingOf(declarations.File).Declarations(new SymbolRef(declarations.File, declarations.Index).BinderSymbol);
            return chain.IsUndefined ? null : new Decls(chain, declarations.File);
        }
        return declarations.Index == 0 ? null : DeclarationLists.View(declarations);
    }

    /// <summary>
    /// `(symbol.declarations ??= []).push(...declarations)` on a transient symbol whose declarations are the checker's
    /// own array (or undefined).
    /// </summary>
    internal void AddDeclarations(SymbolRef symbol, Decls declarations)
    {
        if (TransientSymbols.Record(symbol).Declarations.IsUndefined)
        {
            TransientSymbols.Record(symbol).Declarations = DeclarationLists.Create([]);
        }
        var list = TransientSymbols.Record(symbol).Declarations;
        foreach (var node in declarations)
        {
            DeclarationLists.Add(list, node);
        }
    }

    /// <summary>`(symbol.declarations ??= []).push(declaration)` on a transient symbol (see AddDeclarations).</summary>
    internal void AddDeclaration(SymbolRef symbol, NodeRef declaration)
    {
        if (TransientSymbols.Record(symbol).Declarations.IsUndefined)
        {
            TransientSymbols.Record(symbol).Declarations = DeclarationLists.Create([]);
        }
        DeclarationLists.Add(TransientSymbols.Record(symbol).Declarations, declaration);
    }

    /// <summary>A new declarations array of the checker with these declarations.</summary>
    internal DeclRef NewDeclarations(ReadOnlySpan<NodeRef> declarations) => DeclarationLists.Create(declarations);

    /// <summary>A new declarations array of the checker with a copy of these declarations (`[...declarations]`).</summary>
    internal DeclRef CopyDeclarations(Decls? declarations) =>
        declarations is { } d ? DeclarationLists.CreateCopy(d) : DeclarationLists.Create([]);

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
            return TransientSymbols.Record(symbol).Members.OrNull;
        }
        var table = BindingOf(symbol.File).Members(symbol.BinderSymbol);
        return table.IsNone ? null : TableOf(symbol.File, table);
    }

    /// <summary>symbol.exports (null when undefined).</summary>
    internal SymTable? ExportsOf(SymbolRef symbol)
    {
        if (symbol.IsTransient)
        {
            return TransientSymbols.Record(symbol).Exports.OrNull;
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
        var result = new SymTable(Names, binding.Count(table));
        foreach (var (_, symbol) in binding.Entries(table))
        {
            var reference = new SymbolRef(file, symbol.Index);
            result.Set(NameIdOf(reference), reference);
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

    /// <summary>checker.ts createSymbol(flags, name, checkFlags) with the name as an id of <see cref="Names"/>.</summary>
    internal SymbolRef CreateSymbol(SymbolFlags flags, int name, CheckFlags checkFlags = CheckFlags.None) =>
        TransientSymbols.Create(flags, name, checkFlags);

    private void RecordMergedSymbol(SymbolRef target, SymbolRef source) => mergedSymbols[source] = target;

    /// <summary>checker.ts cloneSymbol.</summary>
    private SymbolRef CloneSymbol(SymbolRef symbol)
    {
        var result = TransientSymbols.Create(SymbolFlagsOf(symbol), NameIdOf(symbol));
        ref var r = ref TransientSymbols.Record(result);
        r.Declarations = CopyDeclarations(DeclarationsOf(symbol));
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
                AddDeclarations(target, sourceDeclarations);
            }
            var sourceMembers = MembersOf(source);
            if (sourceMembers is { } sourceMembersTable)
            {
                var targetMembers = SymTable.OrNew(ref TransientSymbols.Record(target).Members, Names);
                MergeSymbolTable(targetMembers, sourceMembersTable, unidirectional);
            }
            var sourceExports = ExportsOf(source);
            if (sourceExports is { } sourceExportsTable)
            {
                var targetExports = SymTable.OrNew(ref TransientSymbols.Record(target).Exports, Names);
                MergeSymbolTable(targetExports, sourceExportsTable, unidirectional, target);
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
                Error(sourceDeclarations is { Count: > 0 } sd ? GetNameOfDeclarationRef(sd[0]) : NodeRef.None,
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
        var sourceSymbolFile = sourceDeclarations is { Count: > 0 } sd ? sd[0].File : 0;
        var targetSymbolFile = targetDeclarations is { Count: > 0 } td ? td[0].File : 0;
        var isSourcePlainJs = sourceSymbolFile != 0 && Program.IsPlainJsFile(FileAt(sourceSymbolFile), compilerOptions.CheckJs);
        var isTargetPlainJs = targetSymbolFile != 0 && Program.IsPlainJsFile(FileAt(targetSymbolFile), compilerOptions.CheckJs);
        var symbolName = SymbolToString(source);

        // Collect top-level duplicate identifier errors into one mapping, so we can then merge their diagnostics if there are a bunch
        if (sourceSymbolFile != 0 && targetSymbolFile != 0 && amalgamatedDuplicates is not null && !isEitherEnum && sourceSymbolFile != targetSymbolFile)
        {
            var sourcePath = FileNameOf(sourceSymbolFile);
            var targetPath = FileNameOf(targetSymbolFile);
            var firstFile = string.CompareOrdinal(sourcePath, targetPath) < 0 ? sourceSymbolFile : targetSymbolFile;
            var secondFile = firstFile == sourceSymbolFile ? targetSymbolFile : sourceSymbolFile;
            // getOrUpdate(amalgamatedDuplicates, `${firstFile.path}|${secondFile.path}`, ...) and
            // getOrUpdate(filesDuplicates.conflictingSymbols, symbolName, ...)
            var duplicates = amalgamatedDuplicates;
            var pair = duplicates.PairOf(firstFile, secondFile);
            var conflictingSymbolInfo = duplicates.SymbolOf(pair, symbolName, isEitherBlockScoped);
            if (!isSourcePlainJs)
            {
                AddDuplicateLocations(duplicates, conflictingSymbolInfo, secondList: false, source);
            }
            if (!isTargetPlainJs)
            {
                AddDuplicateLocations(duplicates, conflictingSymbolInfo, secondList: true, target);
            }
        }
        else
        {
            if (!isSourcePlainJs)
            {
                AddDuplicateDeclarationErrorsForSymbols(source, message, symbolName, target);
            }
            if (!isTargetPlainJs)
            {
                AddDuplicateDeclarationErrorsForSymbols(target, message, symbolName, source);
            }
        }
    }

    // checker.ts addDuplicateLocations (pushIfUnique)
    private void AddDuplicateLocations(AmalgamatedDuplicates duplicates, int symbolInfo, bool secondList, SymbolRef symbol)
    {
        if (DeclarationsOf(symbol) is { } declarations)
        {
            foreach (var declaration in declarations)
            {
                duplicates.AddLocationIfUnique(symbolInfo, secondList, declaration);
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

    private void AddDuplicateDeclarationError(NodeRef node, DiagnosticMessage message, string symbolName, IEnumerable<NodeRef>? relatedNodes)
    {
        // (getExpandoInitializer / getNameOfExpando: JavaScript expando declarations, module C9)
        var errorNode = GetNameOfDeclarationRef(node);
        if (errorNode.IsNone)
        {
            errorNode = node;
        }
        if (!KeepsDiagnosticsOf(errorNode.File))
        {
            // (module CO4: the error and its related information would not be kept; nothing else depends on them)
            return;
        }
        var err = LookupOrIssueError(errorNode, message, symbolName);
        foreach (var relatedNode in relatedNodes ?? [])
        {
            // Once the error has five related entries, no later iteration adds one or changes anything (the messages
            // it would create are compared only); stop before creating them.
            if (err.RelatedInformation is { Count: >= 5 })
            {
                break;
            }
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
            if (err.RelatedInformation.Count >= 5 || ContainsRelated(err.RelatedInformation, followOnMessage, leadingMessage))
            {
                continue;
            }
            err.RelatedInformation.Add(err.RelatedInformation.Count == 0 ? leadingMessage : followOnMessage);
        }
    }

    // some(related, r => compareDiagnostics(r, a) === EqualTo || compareDiagnostics(r, b) === EqualTo)
    private static bool ContainsRelated(List<DiagnosticRelatedInformation> related, DiagnosticRelatedInformation a, DiagnosticRelatedInformation b)
    {
        foreach (var r in related)
        {
            if (DiagnosticCollection.CompareSkipRelatedInformation(r, a) == 0 || DiagnosticCollection.CompareSkipRelatedInformation(r, b) == 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// checker.ts amalgamatedDuplicates (module CO6: records without references instead of objects per file pair and per
    /// symbol). The Map keyed by <c>`${firstFile.path}|${secondFile.path}`</c> (one program file per path, so the pair of
    /// file numbers is the same key) is the pair records in insertion order with an index by the file numbers; a pair's
    /// conflictingSymbols Map (DuplicateInfoForFiles) is a chain of symbol records (DuplicateInfoForSymbol) in insertion
    /// order with an index by (pair, name); each of a symbol's location lists (pushIfUnique) is a chain of location
    /// records, with one set of (list, location) for the uniqueness test. Lives only while the checker is constructed.
    /// </summary>
    private sealed class AmalgamatedDuplicates
    {
        internal struct Pair
        {
            public int FirstFile;
            public int SecondFile;
            public int FirstSymbol; // the chain of the pair's symbols (0: none)
            public int LastSymbol;
            public int Count;
        }

        internal struct Symbol
        {
            public int Name;
            public bool IsBlockScoped;
            public int Next;
            public int FirstHead; // firstFileLocations: the chain of locations (0: empty)
            public int FirstTail;
            public int SecondHead; // secondFileLocations
            public int SecondTail;
        }

        internal struct Location
        {
            public NodeRef Node;
            public int Next;
        }

        // (index 0 of each array: none)
        internal Pair[] Pairs = new Pair[64];
        internal int PairCount;
        internal Symbol[] Symbols = new Symbol[64];
        private int symbolCount;
        private Location[] locations = new Location[256];
        private int locationCount;
        private readonly Dictionary<long, int> pairIndex = [];
        private readonly Dictionary<long, int> symbolIndex = [];
        private readonly Dictionary<string, int> nameIds = new(StringComparer.Ordinal);
        internal readonly List<string> Names = [];
        private readonly HashSet<(int List, NodeRef Node)> locationSet = [];

        internal int PairOf(int firstFile, int secondFile)
        {
            var key = ((long)firstFile << 32) | (uint)secondFile;
            if (!pairIndex.TryGetValue(key, out var pair))
            {
                pair = ++PairCount;
                if (pair == Pairs.Length)
                {
                    Array.Resize(ref Pairs, Pairs.Length * 2);
                }
                Pairs[pair] = new Pair { FirstFile = firstFile, SecondFile = secondFile };
                pairIndex.Add(key, pair);
            }
            return pair;
        }

        internal int SymbolOf(int pair, string name, bool isBlockScoped)
        {
            if (!nameIds.TryGetValue(name, out var nameId))
            {
                nameId = Names.Count;
                Names.Add(name);
                nameIds.Add(name, nameId);
            }
            var key = ((long)pair << 32) | (uint)nameId;
            if (!symbolIndex.TryGetValue(key, out var symbol))
            {
                symbol = ++symbolCount;
                if (symbol == Symbols.Length)
                {
                    Array.Resize(ref Symbols, Symbols.Length * 2);
                }
                Symbols[symbol] = new Symbol { Name = nameId, IsBlockScoped = isBlockScoped };
                ref var p = ref Pairs[pair];
                if (p.LastSymbol == 0)
                {
                    p.FirstSymbol = symbol;
                }
                else
                {
                    Symbols[p.LastSymbol].Next = symbol;
                }
                p.LastSymbol = symbol;
                p.Count++;
                symbolIndex.Add(key, symbol);
            }
            return symbol;
        }

        internal void AddLocationIfUnique(int symbol, bool secondList, NodeRef node)
        {
            if (!locationSet.Add((secondList ? -symbol : symbol, node)))
            {
                return;
            }
            var location = ++locationCount;
            if (location == locations.Length)
            {
                Array.Resize(ref locations, locations.Length * 2);
            }
            locations[location] = new Location { Node = node };
            ref var s = ref Symbols[symbol];
            ref var tail = ref secondList ? ref s.SecondTail : ref s.FirstTail;
            if (tail == 0)
            {
                (secondList ? ref s.SecondHead : ref s.FirstHead) = location;
            }
            else
            {
                locations[tail].Next = location;
            }
            tail = location;
        }

        /// <summary>The locations of a chain, in order, into <paramref name="list"/> (cleared first).</summary>
        internal void CopyLocations(int head, List<NodeRef> list)
        {
            list.Clear();
            for (var l = head; l != 0; l = locations[l].Next)
            {
                list.Add(locations[l].Node);
            }
        }
    }

    private AmalgamatedDuplicates? amalgamatedDuplicates;

    // initializeTypeChecker: amalgamatedDuplicates.forEach(...)
    private void ReportAmalgamatedDuplicates()
    {
        var duplicates = amalgamatedDuplicates!;
        List<NodeRef>? firstFileLocations = null;
        List<NodeRef>? secondFileLocations = null;
        for (var p = 1; p <= duplicates.PairCount; p++)
        {
            var files = duplicates.Pairs[p];
            // If not many things conflict, issue individual errors
            if (files.Count < 8)
            {
                for (var s = files.FirstSymbol; s != 0; s = duplicates.Symbols[s].Next)
                {
                    var info = duplicates.Symbols[s];
                    var name = duplicates.Names[info.Name];
                    var message = info.IsBlockScoped ? Diagnostics.Cannot_redeclare_block_scoped_variable_0 : Diagnostics.Duplicate_identifier_0;
                    duplicates.CopyLocations(info.FirstHead, firstFileLocations ??= []);
                    duplicates.CopyLocations(info.SecondHead, secondFileLocations ??= []);
                    foreach (var node in firstFileLocations)
                    {
                        AddDuplicateDeclarationError(node, message, name, secondFileLocations);
                    }
                    foreach (var node in secondFileLocations)
                    {
                        AddDuplicateDeclarationError(node, message, name, firstFileLocations);
                    }
                }
            }
            else
            {
                // Otherwise issue top-level error since the files appear very identical in terms of what they contain
                var names = new List<string>(files.Count);
                for (var s = files.FirstSymbol; s != 0; s = duplicates.Symbols[s].Next)
                {
                    names.Add(duplicates.Names[duplicates.Symbols[s].Name]);
                }
                var list = string.Join(", ", names);
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
        if (augmentationDeclarations is not { } ad || ad[0] != moduleAugmentation)
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
                // If we're merging an augmentation to a pattern ambient module, we want to
                // perform the merge unidirectionally from the augmentation ('a.foo') to
                // the pattern ('*.foo'), so that 'getMergedSymbol()' on a.foo gives you
                // all the exports both from the pattern and from the augmentation, but
                // 'getMergedSymbol()' on *.foo only gives you exports from *.foo. (module C10)
                if (patternAmbientModules is not null && patternAmbientModules.Exists(module => module.Symbol == mainModule))
                {
                    var merged = MergeSymbol(augmentationSymbol, mainModule, unidirectional: true);
                    patternAmbientModuleAugmentations ??= [];
                    // moduleName will be a StringLiteral since this is not `declare global`.
                    var nameStore = StoreOf(moduleName);
                    patternAmbientModuleAugmentations[nameStore.Texts.GetString(nameStore.Text(moduleName.Node))!] = merged;
                }
                else
                {
                    var mainExports = ExportsOf(mainModule);
                    var augmentationExports = ExportsOf(augmentationSymbol);
                    if (mainExports is not null && !mainExports.Get(InternalSymbolName.ExportStar).IsNone && augmentationExports is { Count: > 0 })
                    {
                        // We may need to merge the module augmentation's exports into the target symbols of the resolved exports
                        var resolvedExports = GetResolvedMembersOrExportsOfSymbol(mainModule, isStatic: true);
                        var entries = new List<(string Key, SymbolRef Value)>();
                        for (var i = 0; i < augmentationExports.Count; i++)
                        {
                            entries.Add((augmentationExports.NameAt(i), augmentationExports.ValueAt(i)));
                        }
                        foreach (var (key, value) in entries)
                        {
                            if (resolvedExports.Has(key) && !mainExports.Has(key))
                            {
                                MergeSymbol(resolvedExports.Get(key), value);
                            }
                        }
                    }
                    mainModule = MergeSymbol(mainModule, augmentationSymbol);
                }
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

    /// <summary>checker.ts getSymbolOfDeclaration (late-bound symbols: module C3).</summary>
    internal SymbolRef GetSymbolOfDeclaration(NodeRef node)
    {
        var symbol = NodeSymbol(node);
        return GetMergedSymbol(symbol.IsNone ? symbol : GetLateBoundSymbol(symbol));
    }

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
        if (symbol.IsNone || declarations is not { Count: > 0 } list)
        {
            return false;
        }
        var first = list[0];
        return (Utilities.GetSyntacticModifierFlagsUncached(StoreOf(first), first.Node) & ModifierFlags.Default) != 0;
    }

    /// <summary>checker.ts symbolIsValue.</summary>
    internal bool SymbolIsValue(SymbolRef symbol, bool includeTypeOnlyMembers = false)
    {
        var flags = SymbolFlagsOf(symbol);
        return (flags & SymbolFlags.Value) != 0
            || ((flags & SymbolFlags.Alias) != 0 && (GetSymbolFlags(symbol, !includeTypeOnlyMembers) & SymbolFlags.Value) != 0);
    }

    /// <summary>checker.ts getExportsOfSymbol (late-bound members: Checker.LateBound.cs, module C3).</summary>
    internal SymTable GetExportsOfSymbol(SymbolRef symbol)
    {
        var flags = SymbolFlagsOf(symbol);
        return (flags & SymbolFlags.LateBindingContainer) != 0 ? GetResolvedMembersOrExportsOfSymbol(symbol, isStatic: true)
            : (flags & SymbolFlags.Module) != 0 ? GetExportsOfModule(symbol)
            : ExportsOf(symbol) ?? emptySymbols;
    }

    /// <summary>checker.ts getMembersOfSymbol (late-bound members: Checker.LateBound.cs, module C3).</summary>
    internal SymTable GetMembersOfSymbol(SymbolRef symbol) =>
        (SymbolFlagsOf(symbol) & SymbolFlags.LateBindingContainer) != 0 ? GetResolvedMembersOrExportsOfSymbol(symbol, isStatic: false) : MembersOf(symbol) ?? emptySymbols;

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

    // getExportsOfModuleWorker's visit (typeOnlyExportStarMap: `export type *` re-exports name other files, which Gate C
    // programs do not resolve; not ported).
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
            var nestedSymbols = new SymTable(Names);
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

    /// <summary>checker.ts resolveExternalModuleSymbol.</summary>
    internal SymbolRef ResolveExternalModuleSymbol(SymbolRef moduleSymbol, bool dontResolveAlias = false)
    {
        var exports = moduleSymbol.IsNone ? null : ExportsOf(moduleSymbol);
        if (exports is not null)
        {
            var exportEquals = ResolveSymbol(exports.Get(InternalSymbolName.ExportEquals), dontResolveAlias);
            var exported = GetMergedSymbol(GetCommonJsExportEquals(GetMergedSymbol(exportEquals), GetMergedSymbol(moduleSymbol)));
            return !exported.IsNone ? exported : moduleSymbol;
        }
        return SymbolRef.None;
    }

    // links.cjsExportMerged
    private readonly Dictionary<SymbolRef, SymbolRef> cjsExportMerged = [];

    // checker.ts getCommonJsExportEquals
    private SymbolRef GetCommonJsExportEquals(SymbolRef exported, SymbolRef moduleSymbol)
    {
        if (exported.IsNone || exported == unknownSymbol || exported == moduleSymbol || ExportsOf(moduleSymbol)!.Count == 1 || (SymbolFlagsOf(exported) & SymbolFlags.Alias) != 0)
        {
            return exported;
        }
        if (cjsExportMerged.TryGetValue(exported, out var cached))
        {
            return cached;
        }
        var merged = exported.IsTransient ? exported : CloneSymbol(exported);
        ref var record = ref TransientSymbols.Record(merged);
        record.Flags |= SymbolFlags.ValueModule;
        var mergedExports = SymTable.OrNew(ref record.Exports, Names);
        var moduleExports = ExportsOf(moduleSymbol)!;
        for (var i = 0; i < moduleExports.Count; i++)
        {
            var name = moduleExports.NameAt(i);
            if (name == InternalSymbolName.ExportEquals)
            {
                continue;
            }
            var s = moduleExports.ValueAt(i);
            mergedExports.Set(name, mergedExports.Has(name) ? MergeSymbol(mergedExports.Get(name), s) : s);
        }
        if (merged == exported)
        {
            // We just mutated a symbol, reset any cached links we may have already set
            // (Notably required to make late bound members appear)
            ResetResolvedMembersOrExports(merged);
        }
        cjsExportMerged[merged] = merged;
        cjsExportMerged[exported] = merged;
        return merged;
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
