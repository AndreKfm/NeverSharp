// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (forEachSymbolTableInScope, getAccessibleSymbolChain, needsQualification,
// getContainersOfSymbol, getAliasForSymbolInContainer, getSymbolIfSameReference, isSymbolAccessible, isAnySymbolAccessible,
// hasVisibleDeclarations, isDeclarationVisible, isEntityNameVisible, getMeaningOfEntityNameReference)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/symbolaccessibility.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C2. The node builder's fake scopes (a Block with locals whose
// parent is the enclosing declaration, checker.ts enterNewScope) are a FakeScope chain on top of the enclosing
// declaration (Track O nodes of a file cannot point at a synthesized parent). Module EO1: the accessibleChainCache
// (one map per checker keyed by the symbol and TypeScript's key; a fake scope is a key location of its own). Module D5: the node links' isVisible cache (isVisibleLinks), which declaration emit paints
// (Checker.DeclarationEmit.cs); the versions with aliases to make visible are there.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>checker.ts SymbolAccessibility.</summary>
internal enum SymbolAccessibility
{
    Accessible,
    NotAccessible,
    CannotBeNamed,
    NotResolved,
}

internal sealed partial class Checker
{
    /// <summary>
    /// A fake scope of the node builder (checker.ts enterNewScope's pushFakeScope): a Block whose locals are
    /// <see cref="Locals"/>, whose parent is <see cref="Parent"/> or, at the bottom of the chain, the enclosing declaration.
    /// </summary>
    internal sealed class FakeScope
    {
        public required string Kind;
        public required SymTable Locals;
        public FakeScope? Parent;
    }

    // checker.ts forEachSymbolTableInScope (callback(symbolTable, ignoreQualification, isLocalNameLookup))
    private T? ForEachSymbolTableInScope<T>(NodeRef enclosingDeclaration, FakeScope? fakeScope, Func<SymTable, bool, bool, T?> callback)
        where T : class
    {
        for (var fake = fakeScope; fake is not null; fake = fake.Parent)
        {
            // A Block: its locals, nothing else
            var result = callback(fake.Locals, false, true);
            if (result is not null)
            {
                return result;
            }
        }
        for (var location = enclosingDeclaration; !location.IsNone; location = Parent(location))
        {
            var store = StoreOf(location);
            var n = location.Node;
            // Locals of a source file are not in scope (because they get merged into the global symbol table)
            if (Utilities.CanHaveLocals(store, n) && !IsGlobalSourceFile(location))
            {
                var locals = LocalsOf(location);
                if (locals is { } localsTable)
                {
                    var result = callback(localsTable, false, true);
                    if (result is not null)
                    {
                        return result;
                    }
                }
            }
            switch (store.Kind(n))
            {
                case SyntaxKind.SourceFile:
                case SyntaxKind.ModuleDeclaration:
                {
                    if (store.Kind(n) == SyntaxKind.SourceFile && !Utilities.IsExternalOrCommonJsModule(store, n, BindingOf(location.File)))
                    {
                        break;
                    }
                    var sym = GetSymbolOfDeclaration(location);
                    // `sym` may not have exports if this module declaration is backed by the symbol for a `const` that's being rewritten
                    // into a namespace - in such cases, it's best to just let the namespace appear empty (the const members couldn't have referred
                    // to one another anyway)
                    var result = callback((sym.IsNone ? null : ExportsOf(sym)) ?? emptySymbols, false, true);
                    if (result is not null)
                    {
                        return result;
                    }
                    break;
                }
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                case SyntaxKind.InterfaceDeclaration:
                {
                    // Type parameters are bound into `members` lists so they can merge across declarations
                    SymTable? table = null;
                    var members = MembersOf(GetSymbolOfDeclaration(location));
                    if (members is not null)
                    {
                        for (var i = 0; i < members.Count; i++)
                        {
                            if ((SymbolFlagsOf(members.ValueAt(i)) & (SymbolFlags.Type & ~SymbolFlags.Assignment)) != 0)
                            {
                                (table ??= new SymTable(Names)).Set(members.NameIdAt(i), members.ValueAt(i));
                            }
                        }
                    }
                    if (table is { } typeTable)
                    {
                        var result = callback(typeTable, false, false);
                        if (result is not null)
                        {
                            return result;
                        }
                    }
                    break;
                }
            }
        }
        return callback(globals, false, true);
    }

    // checker.ts getAccessibleSymbolChain's firstRelevantLocation, forEachSymbolTableInScope(enclosingDeclaration,
    // (_, __, ___, node) => node): the location of the first table ForEachSymbolTableInScope visits outside the fake
    // scopes (the caller takes the innermost fake scope first); NodeRef.None when that is the globals (no scope node).
    private NodeRef FirstRelevantLocation(NodeRef enclosingDeclaration)
    {
        for (var location = enclosingDeclaration; !location.IsNone; location = Parent(location))
        {
            var store = StoreOf(location);
            var n = location.Node;
            if (Utilities.CanHaveLocals(store, n) && !IsGlobalSourceFile(location) && LocalsOf(location) is not null)
            {
                return location;
            }
            switch (store.Kind(n))
            {
                case SyntaxKind.SourceFile:
                case SyntaxKind.ModuleDeclaration:
                    if (store.Kind(n) == SyntaxKind.SourceFile && !Utilities.IsExternalOrCommonJsModule(store, n, BindingOf(location.File)))
                    {
                        break;
                    }
                    return location;
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                case SyntaxKind.InterfaceDeclaration:
                {
                    // the filtered table of ForEachSymbolTableInScope is visited only when it is not empty
                    var members = MembersOf(GetSymbolOfDeclaration(location));
                    if (members is not null)
                    {
                        for (var i = 0; i < members.Count; i++)
                        {
                            if ((SymbolFlagsOf(members.ValueAt(i)) & (SymbolFlags.Type & ~SymbolFlags.Assignment)) != 0)
                            {
                                return location;
                            }
                        }
                    }
                    break;
                }
            }
        }
        return NodeRef.None;
    }

    /// <summary>
    /// The key of checker.ts's <c>links.accessibleChainCache</c> (<c>`${useOnlyExternalAliasing ? 0 : 1}|${firstRelevantLocation
    /// ? getNodeId(firstRelevantLocation) : 0}|${meaning}`</c>; typescript-go accessibleChainCacheKey) with the symbol whose
    /// links hold the map: the checker keeps one map for all its symbols. The first relevant location is a node or, when
    /// the node builder has pushed fake scopes, the innermost one (TypeScript's fake scope is a synthesized Block with an
    /// id of its own).
    /// </summary>
    private readonly struct AccessibleChainCacheKey(SymbolRef symbol, bool useOnlyExternalAliasing, NodeRef location, FakeScope? fakeScope, SymbolFlags meaning)
        : IEquatable<AccessibleChainCacheKey>
    {
        private readonly SymbolRef symbol = symbol;
        private readonly bool useOnlyExternalAliasing = useOnlyExternalAliasing;
        private readonly NodeRef location = location;
        private readonly FakeScope? fakeScope = fakeScope;
        private readonly SymbolFlags meaning = meaning;

        public bool Equals(AccessibleChainCacheKey other) =>
            symbol == other.symbol && useOnlyExternalAliasing == other.useOnlyExternalAliasing && location == other.location
            && ReferenceEquals(fakeScope, other.fakeScope) && meaning == other.meaning;

        public override bool Equals(object? obj) => obj is AccessibleChainCacheKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(symbol.GetHashCode(), location.GetHashCode(), fakeScope is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(fakeScope), (int)meaning, useOnlyExternalAliasing);
    }

    // checker.ts SymbolLinks.accessibleChainCache (typescript-go ContainingSymbolLinks.accessibleChainCache), per checker;
    // the cached chains are the lists getAccessibleSymbolChain returned (never modified by their readers)
    private readonly Dictionary<AccessibleChainCacheKey, List<SymbolRef>?> accessibleChainCache = [];

    private static SymbolFlags GetQualifiedLeftMeaning(SymbolFlags rightMeaning) =>
        // If we are looking in value space, the parent meaning is value, other wise it is namespace
        rightMeaning == SymbolFlags.Value ? SymbolFlags.Value : SymbolFlags.Namespace;

    // checker.ts getAccessibleSymbolChain
    private List<SymbolRef>? GetAccessibleSymbolChain(SymbolRef symbol, NodeRef enclosingDeclaration, FakeScope? fakeScope, SymbolFlags meaning, bool useOnlyExternalAliasing, Dictionary<SymbolRef, List<SymTable>>? visitedSymbolTablesMap = null)
    {
        if (symbol.IsNone || IsPropertyOrMethodDeclarationSymbol(symbol))
        {
            return null;
        }
        // Go from enclosingDeclaration to the first scope we check, so the cache is keyed off the scope and thus shared more
        var key = new AccessibleChainCacheKey(symbol, useOnlyExternalAliasing, fakeScope is null ? FirstRelevantLocation(enclosingDeclaration) : NodeRef.None, fakeScope, meaning);
        if (accessibleChainCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        visitedSymbolTablesMap ??= [];
        if (!visitedSymbolTablesMap.TryGetValue(symbol, out var visitedSymbolTables))
        {
            visitedSymbolTablesMap[symbol] = visitedSymbolTables = [];
        }
        var chain = ForEachSymbolTableInScope(enclosingDeclaration, fakeScope, (table, ignoreQualification, isLocalNameLookup) => GetAccessibleSymbolChainFromSymbolTable(table, ignoreQualification, isLocalNameLookup));
        accessibleChainCache[key] = chain;
        return chain;

        List<SymbolRef>? GetAccessibleSymbolChainFromSymbolTable(SymTable symbols, bool ignoreQualification, bool isLocalNameLookup)
        {
            if (visitedSymbolTables.Contains(symbols))
            {
                return null;
            }
            visitedSymbolTables.Add(symbols);
            var result = TrySymbolTable(symbols, ignoreQualification, isLocalNameLookup);
            visitedSymbolTables.RemoveAt(visitedSymbolTables.Count - 1);
            return result;
        }

        bool CanQualifySymbol(SymbolRef symbolFromSymbolTable, SymbolFlags meaning)
        {
            // If the symbol is equivalent and doesn't need further qualification, this symbol is accessible
            return !NeedsQualification(symbolFromSymbolTable, enclosingDeclaration, fakeScope, meaning) ||
                // If symbol needs qualification, make sure that parent is accessible, if it is then this symbol is accessible too
                GetAccessibleSymbolChain(ParentOf(symbolFromSymbolTable), enclosingDeclaration, fakeScope, GetQualifiedLeftMeaning(meaning), useOnlyExternalAliasing, visitedSymbolTablesMap) is not null;
        }

        bool IsAccessible(SymbolRef symbolFromSymbolTable, SymbolRef resolvedAliasSymbol, bool ignoreQualification)
        {
            var target = !resolvedAliasSymbol.IsNone ? resolvedAliasSymbol : symbolFromSymbolTable;
            return (symbol == target || GetMergedSymbol(symbol) == GetMergedSymbol(target)) &&
                // if the symbolFromSymbolTable is not external module (it could be if it was determined as ambient external module and would be in globals table)
                // and if symbolFromSymbolTable or alias resolution matches the symbol,
                // check the symbol can be qualified, it is only then this symbol is accessible
                !(DeclarationsOf(symbolFromSymbolTable) ?? []).Exists(HasNonGlobalAugmentationExternalModuleSymbol) &&
                (ignoreQualification || CanQualifySymbol(GetMergedSymbol(symbolFromSymbolTable), meaning));
        }

        List<SymbolRef>? TrySymbolTable(SymTable symbols, bool ignoreQualification, bool isLocalNameLookup)
        {
            // If symbol is directly available by its name in the symbol table
            var direct = symbols.Get(NameOf(symbol));
            if (!direct.IsNone && IsAccessible(direct, SymbolRef.None, ignoreQualification))
            {
                return [symbol];
            }
            // Check if symbol is any of the aliases in scope
            for (var i = 0; i < symbols.Count; i++)
            {
                var symbolFromSymbolTable = symbols.ValueAt(i);
                var name = NameOf(symbolFromSymbolTable);
                if ((SymbolFlagsOf(symbolFromSymbolTable) & SymbolFlags.Alias) != 0
                    && name != InternalSymbolName.ExportEquals
                    && name != InternalSymbolName.Default
                    && !(IsUmdExportSymbol(symbolFromSymbolTable) && !enclosingDeclaration.IsNone && Utilities.IsExternalModule(StoreOf(enclosingDeclaration.File), Utilities.RootOf(StoreOf(enclosingDeclaration.File), enclosingDeclaration.Node)))
                    // If `!useOnlyExternalAliasing`, we can use any type of alias to get the name
                    && (!useOnlyExternalAliasing || (DeclarationsOf(symbolFromSymbolTable) ?? []).Exists(d => Utilities.IsExternalModuleImportEqualsDeclaration(StoreOf(d), d.Node)))
                    // If we're looking up a local name to reference directly, omit namespace reexports, otherwise when we're trawling through an export list to make a dotted name, we can keep it
                    && (!isLocalNameLookup || !(DeclarationsOf(symbolFromSymbolTable) ?? []).Exists(IsNamespaceReexportDeclaration))
                    // While exports are generally considered to be in scope, export-specifier declared symbols are _not_
                    // See similar comment in `resolveName` for details
                    && (ignoreQualification || GetDeclarationOfKind(symbolFromSymbolTable, SyntaxKind.ExportSpecifier).IsNone))
                {
                    var resolvedImportedSymbol = ResolveAlias(symbolFromSymbolTable);
                    var candidate = GetCandidateListForSymbol(symbolFromSymbolTable, resolvedImportedSymbol, ignoreQualification);
                    if (candidate is not null)
                    {
                        return candidate;
                    }
                }
                if (name == NameOf(symbol) && !ExportSymbolOf(symbolFromSymbolTable).IsNone)
                {
                    if (IsAccessible(GetMergedSymbol(ExportSymbolOf(symbolFromSymbolTable)), SymbolRef.None, ignoreQualification))
                    {
                        return [symbol];
                    }
                }
            }
            // If there's no result and we're looking at the global symbol table, treat `globalThis` like an alias and try to lookup thru that
            return symbols == globals ?GetCandidateListForSymbol(globalThisSymbol, globalThisSymbol, ignoreQualification) : null;
        }

        List<SymbolRef>? GetCandidateListForSymbol(SymbolRef symbolFromSymbolTable, SymbolRef resolvedImportedSymbol, bool ignoreQualification)
        {
            if (IsAccessible(symbolFromSymbolTable, resolvedImportedSymbol, ignoreQualification))
            {
                return [symbolFromSymbolTable];
            }
            // Look in the exported members, if we can find accessibleSymbolChain, symbol is accessible using this chain
            // but only if the symbolFromSymbolTable can be qualified
            var candidateTable = GetExportsOfSymbol(resolvedImportedSymbol);
            var accessibleSymbolsFromExports = GetAccessibleSymbolChainFromSymbolTable(candidateTable, true, false);
            if (accessibleSymbolsFromExports is not null && CanQualifySymbol(symbolFromSymbolTable, GetQualifiedLeftMeaning(meaning)))
            {
                return [symbolFromSymbolTable, .. accessibleSymbolsFromExports];
            }
            return null;
        }
    }

    // utilities.ts isUMDExportSymbol
    private bool IsUmdExportSymbol(SymbolRef symbol)
    {
        var declarations = DeclarationsOf(symbol);
        return declarations is { Count: > 0 } d && Kind(d[0]) == SyntaxKind.NamespaceExportDeclaration;
    }

    // utilities.ts isNamespaceReexportDeclaration: `export * as ns from "..."`
    private bool IsNamespaceReexportDeclaration(NodeRef node)
    {
        if (Kind(node) != SyntaxKind.NamespaceExport)
        {
            return false;
        }
        var parent = Parent(node);
        return !parent.IsNone && !StoreOf(parent).ModuleSpecifier(parent.Node).IsNone;
    }

    // checker.ts needsQualification
    private bool NeedsQualification(SymbolRef symbol, NodeRef enclosingDeclaration, FakeScope? fakeScope, SymbolFlags meaning)
    {
        var qualify = false;
        var name = NameOf(symbol);
        ForEachSymbolTableInScope<object>(enclosingDeclaration, fakeScope, (symbolTable, _, _) =>
        {
            // If symbol of this name is not available in the symbol table we are ok
            var symbolFromSymbolTable = GetMergedSymbol(symbolTable.Get(name));
            if (symbolFromSymbolTable.IsNone)
            {
                // Continue to the next symbol table
                return null;
            }
            // If the symbol with this name is present it should refer to the symbol
            if (symbolFromSymbolTable == symbol)
            {
                // No need to qualify
                return true;
            }
            // Qualify if the symbol from symbol table has same meaning as expected
            var shouldResolveAlias = (SymbolFlagsOf(symbolFromSymbolTable) & SymbolFlags.Alias) != 0 && GetDeclarationOfKind(symbolFromSymbolTable, SyntaxKind.ExportSpecifier).IsNone;
            symbolFromSymbolTable = shouldResolveAlias ? ResolveAlias(symbolFromSymbolTable) : symbolFromSymbolTable;
            var flags = shouldResolveAlias ? GetSymbolFlags(symbolFromSymbolTable) : SymbolFlagsOf(symbolFromSymbolTable);
            if ((flags & meaning) != 0)
            {
                qualify = true;
                return true;
            }
            // Continue to the next symbol table
            return null;
        });
        return qualify;
    }

    private bool IsPropertyOrMethodDeclarationSymbol(SymbolRef symbol)
    {
        var declarations = DeclarationsOf(symbol);
        if (declarations is not { Count: > 0 })
        {
            return false;
        }
        foreach (var declaration in declarations)
        {
            switch (Kind(declaration))
            {
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    continue;
                default:
                    return false;
            }
        }
        return true;
    }

    // utilities.ts hasNonGlobalAugmentationExternalModuleSymbol: a module with a string literal name or an external module file
    private bool HasNonGlobalAugmentationExternalModuleSymbol(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var kind = store.Kind(declaration.Node);
        if (kind == SyntaxKind.ModuleDeclaration && store.Kind(store.Name(declaration.Node)) == SyntaxKind.StringLiteral)
        {
            return true;
        }
        return kind == SyntaxKind.SourceFile && Utilities.IsExternalOrCommonJsModule(store, declaration.Node, BindingOf(declaration.File));
    }

    // checker.ts hasExternalModuleSymbol
    private bool HasExternalModuleSymbol(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        return Utilities.IsAmbientModule(store, declaration.Node) ||
            (store.Kind(declaration.Node) == SyntaxKind.SourceFile && Utilities.IsExternalOrCommonJsModule(store, declaration.Node, BindingOf(declaration.File)));
    }

    // checker.ts getExternalModuleContainer
    private SymbolRef GetExternalModuleContainer(NodeRef declaration)
    {
        for (var n = declaration; !n.IsNone; n = Parent(n))
        {
            if (HasExternalModuleSymbol(n))
            {
                return GetSymbolOfDeclaration(n);
            }
        }
        return SymbolRef.None;
    }

    // checker.ts getContainersOfSymbol
    private List<SymbolRef>? GetContainersOfSymbol(SymbolRef symbol, NodeRef enclosingDeclaration, FakeScope? fakeScope, SymbolFlags meaning)
    {
        var container = GetParentOfSymbol(symbol);
        // Type parameters end up in the `members` lists but are not externally visible
        if (!container.IsNone && (SymbolFlagsOf(symbol) & SymbolFlags.TypeParameter) == 0)
        {
            return GetWithAlternativeContainers(container);
        }
        var candidates = new List<SymbolRef>();
        foreach (var d in DeclarationsOf(symbol) ?? [])
        {
            var store = StoreOf(d);
            var parent = Parent(d);
            if (!Utilities.IsAmbientModule(store, d.Node) && !parent.IsNone)
            {
                // direct children of a module
                if (HasNonGlobalAugmentationExternalModuleSymbol(parent))
                {
                    candidates.Add(GetSymbolOfDeclaration(parent));
                    continue;
                }
                // export ='d member of an ambient module
                if (Kind(parent) == SyntaxKind.ModuleBlock && !Parent(parent).IsNone && ResolveExternalModuleSymbol(GetSymbolOfDeclaration(Parent(parent))) == symbol)
                {
                    candidates.Add(GetSymbolOfDeclaration(Parent(parent)));
                    continue;
                }
            }
            if (store.Kind(d.Node) == SyntaxKind.ClassExpression && !parent.IsNone && Kind(parent) == SyntaxKind.BinaryExpression &&
                store.Kind(store.OperatorToken(parent.Node)) == SyntaxKind.EqualsToken && Utilities.IsAccessExpression(store, store.Left(parent.Node)) &&
                Utilities.IsEntityNameExpression(store, store.Expression(store.Left(parent.Node))))
            {
                var left = store.Left(parent.Node);
                if (Utilities.IsModuleExportsAccessExpression(store, left) || Utilities.IsExportsIdentifier(store, store.Expression(left)))
                {
                    candidates.Add(GetSymbolOfDeclaration(At(d, Utilities.RootOf(store, d.Node))));
                    continue;
                }
                CheckExpressionCached(At(d, store.Expression(left)));
                var resolved = SymbolNodeLinks.Has(At(d, store.Expression(left))) ? SymbolNodeLinks.Get(At(d, store.Expression(left))).ResolvedSymbol : SymbolRef.None;
                if (!resolved.IsNone)
                {
                    candidates.Add(resolved);
                }
            }
        }
        if (candidates.Count == 0)
        {
            return null;
        }
        var bestContainers = new List<SymbolRef>();
        var alternativeContainers = new List<SymbolRef>();
        foreach (var candidate in candidates)
        {
            if (candidate.IsNone || GetAliasForSymbolInContainer(candidate, symbol).IsNone)
            {
                continue;
            }
            var all = GetWithAlternativeContainers(candidate);
            bestContainers.Add(all[0]);
            alternativeContainers.AddRange(all.GetRange(1, all.Count - 1));
        }
        return [.. bestContainers, .. alternativeContainers];

        List<SymbolRef> GetWithAlternativeContainers(SymbolRef container)
        {
            var additionalContainers = new List<SymbolRef>();
            foreach (var d in DeclarationsOf(container) ?? [])
            {
                var fileSymbol = GetFileSymbolIfFileSymbolExportEqualsContainer(d, container);
                if (!fileSymbol.IsNone)
                {
                    additionalContainers.Add(fileSymbol);
                }
            }
            var reexportContainers = !enclosingDeclaration.IsNone ? GetAlternativeContainingModules(symbol, enclosingDeclaration) : null;
            var objectLiteralContainer = GetVariableDeclarationOfObjectLiteral(container, meaning);
            if (!enclosingDeclaration.IsNone &&
                (SymbolFlagsOf(container) & GetQualifiedLeftMeaning(meaning)) != 0 &&
                GetAccessibleSymbolChain(container, enclosingDeclaration, fakeScope, SymbolFlags.Namespace, false) is not null)
            {
                // This order expresses a preference for the real container if it is in scope
                var preferred = new List<SymbolRef> { container };
                preferred.AddRange(additionalContainers);
                if (reexportContainers is not null)
                {
                    preferred.AddRange(reexportContainers);
                }
                if (!objectLiteralContainer.IsNone)
                {
                    preferred.Add(objectLiteralContainer);
                }
                return preferred;
            }
            // we potentially have a symbol which is a member of the instance side of something - look for a variable in scope with the container's type
            // which may be acting like a namespace (eg, `Symbol` acts like a namespace when looking up `Symbol.toStringTag`)
            var firstVariableMatch = SymbolRef.None;
            if ((SymbolFlagsOf(container) & GetQualifiedLeftMeaning(meaning)) == 0 &&
                (SymbolFlagsOf(container) & SymbolFlags.Type) != 0 &&
                (Types.Flags(GetDeclaredTypeOfSymbol(container)) & TypeFlags.Object) != 0 &&
                meaning == SymbolFlags.Value)
            {
                var found = ForEachSymbolTableInScope<object>(enclosingDeclaration, fakeScope, (t, _, _) =>
                {
                    for (var i = 0; i < t.Count; i++)
                    {
                        var s = t.ValueAt(i);
                        if ((SymbolFlagsOf(s) & GetQualifiedLeftMeaning(meaning)) != 0 && GetTypeOfSymbol(s) == GetDeclaredTypeOfSymbol(container))
                        {
                            return s;
                        }
                    }
                    return null;
                });
                if (found is SymbolRef match)
                {
                    firstVariableMatch = match;
                }
            }
            var res = new List<SymbolRef>();
            if (!firstVariableMatch.IsNone)
            {
                res.Add(firstVariableMatch);
            }
            res.AddRange(additionalContainers);
            res.Add(container);
            if (!objectLiteralContainer.IsNone)
            {
                res.Add(objectLiteralContainer);
            }
            if (reexportContainers is not null)
            {
                res.AddRange(reexportContainers);
            }
            return res;
        }
    }

    // SymbolLinks.extendedContainersByFile (by file number) and extendedContainers of getAlternativeContainingModules
    private readonly Dictionary<SymbolRef, Dictionary<int, List<SymbolRef>>> extendedContainersByFile = [];
    private readonly Dictionary<SymbolRef, List<SymbolRef>> extendedContainers = [];

    // checker.ts getAlternativeContainingModules
    private List<SymbolRef> GetAlternativeContainingModules(SymbolRef symbol, NodeRef enclosingDeclaration)
    {
        var containingFile = enclosingDeclaration.File;
        List<SymbolRef>? results = null;
        if (extendedContainersByFile.TryGetValue(symbol, out var byFile) && byFile.TryGetValue(containingFile, out results))
        {
            return results;
        }
        // Try to make an import using an import already in the enclosing file, if possible
        // (the synthesized JSX runtime import is not in the list: synthetic names can't be resolved by `resolveExternalModuleName`)
        foreach (var importRef in program.GetImports(FileAt(containingFile)))
        {
            var resolvedModule = ResolveExternalModuleName(enclosingDeclaration, new NodeRef(containingFile, importRef), ignoreErrors: true);
            if (resolvedModule.IsNone)
            {
                continue;
            }
            var reference = GetAliasForSymbolInContainer(resolvedModule, symbol);
            if (reference.IsNone)
            {
                continue;
            }
            (results ??= []).Add(resolvedModule);
        }
        if (results is { Count: > 0 })
        {
            if (byFile is null)
            {
                extendedContainersByFile[symbol] = byFile = [];
            }
            byFile[containingFile] = results;
            return results;
        }
        if (extendedContainers.TryGetValue(symbol, out var cached))
        {
            return cached;
        }
        // No results from files already being imported by this file - expand search (expensive, but not location-specific, so cached)
        var files = program.SourceFiles;
        for (var f = 1; f <= files.Count; f++)
        {
            var file = files[f - 1];
            if (!Utilities.IsExternalModule(file.Store, file.Root))
            {
                continue;
            }
            var sym = GetSymbolOfDeclaration(new NodeRef(f, file.Root));
            var reference = GetAliasForSymbolInContainer(sym, symbol);
            if (reference.IsNone)
            {
                continue;
            }
            (results ??= []).Add(sym);
        }
        results ??= [];
        extendedContainers[symbol] = results;
        return results;
    }

    // checker.ts getVariableDeclarationOfObjectLiteral
    private SymbolRef GetVariableDeclarationOfObjectLiteral(SymbolRef symbol, SymbolFlags meaning)
    {
        // If we're trying to reference some object literal in, eg `var a = { x: 1 }`, the symbol for the literal, `__object`, is distinct
        // from the symbol of the declaration it is being assigned to. Since we can use the declaration to refer to the literal, however,
        // we'd like to make that connection here - potentially causing us to paint the declaration's visibility, and therefore the literal.
        var declarations = DeclarationsOf(symbol);
        if ((meaning & SymbolFlags.Value) != 0 && declarations is { Count: > 0 } list)
        {
            var firstDecl = list[0];
            var parent = Parent(firstDecl);
            if (!parent.IsNone && Kind(parent) == SyntaxKind.VariableDeclaration)
            {
                var store = StoreOf(parent);
                if ((Kind(firstDecl) == SyntaxKind.ObjectLiteralExpression && firstDecl.Node == store.Initializer(parent.Node)) ||
                    (Kind(firstDecl) == SyntaxKind.TypeLiteral && firstDecl.Node == store.Type(parent.Node)))
                {
                    return GetSymbolOfDeclaration(parent);
                }
            }
        }
        return SymbolRef.None;
    }

    // checker.ts getFileSymbolIfFileSymbolExportEqualsContainer
    private SymbolRef GetFileSymbolIfFileSymbolExportEqualsContainer(NodeRef d, SymbolRef container)
    {
        var fileSymbol = GetExternalModuleContainer(d);
        if (fileSymbol.IsNone)
        {
            return SymbolRef.None;
        }
        var exported = ExportsOf(fileSymbol)?.Get(InternalSymbolName.ExportEquals) ?? SymbolRef.None;
        return !exported.IsNone && !GetSymbolIfSameReference(exported, container).IsNone ? fileSymbol : SymbolRef.None;
    }

    // checker.ts getAliasForSymbolInContainer
    private SymbolRef GetAliasForSymbolInContainer(SymbolRef container, SymbolRef symbol)
    {
        if (container == GetParentOfSymbol(symbol))
        {
            // fast path, `symbol` is either already the alias or isn't aliased
            return symbol;
        }
        // Check if container is a thing with an `export=` which points directly at `symbol`, and if so, return
        // the container itself as the alias for the symbol
        var exportEquals = ExportsOf(container)?.Get(InternalSymbolName.ExportEquals) ?? SymbolRef.None;
        if (!exportEquals.IsNone && !GetSymbolIfSameReference(exportEquals, symbol).IsNone)
        {
            return container;
        }
        var exports = GetExportsOfSymbol(container);
        var quick = exports.Get(NameOf(symbol));
        if (!quick.IsNone && !GetSymbolIfSameReference(quick, symbol).IsNone)
        {
            return quick;
        }
        for (var i = 0; i < exports.Count; i++)
        {
            if (!GetSymbolIfSameReference(exports.ValueAt(i), symbol).IsNone)
            {
                return exports.ValueAt(i);
            }
        }
        return SymbolRef.None;
    }

    // checker.ts getSymbolIfSameReference: checks if two symbols, through aliasing and/or merging, refer to the same thing
    private SymbolRef GetSymbolIfSameReference(SymbolRef s1, SymbolRef s2) =>
        GetMergedSymbol(ResolveSymbol(GetMergedSymbol(s1))) == GetMergedSymbol(ResolveSymbol(GetMergedSymbol(s2))) ? s1 : SymbolRef.None;

    // checker.ts isTypeSymbolAccessible / isValueSymbolAccessible
    private bool IsTypeSymbolAccessible(SymbolRef typeSymbol, NodeRef enclosingDeclaration, FakeScope? fakeScope = null) =>
        IsSymbolAccessibleWorker(typeSymbol, enclosingDeclaration, fakeScope, SymbolFlags.Type, true) == SymbolAccessibility.Accessible;

    private bool IsValueSymbolAccessible(SymbolRef typeSymbol, NodeRef enclosingDeclaration, FakeScope? fakeScope = null) =>
        IsSymbolAccessibleWorker(typeSymbol, enclosingDeclaration, fakeScope, SymbolFlags.Value, true) == SymbolAccessibility.Accessible;

    /// <summary>checker.ts isSymbolAccessible (shouldComputeAliasesToMakeVisible false; the accessibility only).</summary>
    private SymbolAccessibility IsSymbolAccessible(SymbolRef symbol, NodeRef enclosingDeclaration, FakeScope? fakeScope, SymbolFlags meaning) =>
        IsSymbolAccessibleWorker(symbol, enclosingDeclaration, fakeScope, meaning, true);

    private SymbolAccessibility IsSymbolAccessibleWorker(SymbolRef symbol, NodeRef enclosingDeclaration, FakeScope? fakeScope, SymbolFlags meaning, bool allowModules)
    {
        if (!symbol.IsNone && (!enclosingDeclaration.IsNone || fakeScope is not null))
        {
            var result = IsAnySymbolAccessible([symbol], enclosingDeclaration, fakeScope, symbol, meaning, allowModules);
            if (result is not null)
            {
                return result.Value;
            }
            // This could be a symbol that is not exported in the external module
            // or it could be a symbol from different external module that is not aliased and hence cannot be named
            var symbolExternalModule = SymbolRef.None;
            foreach (var d in DeclarationsOf(symbol) ?? [])
            {
                symbolExternalModule = GetExternalModuleContainer(d);
                if (!symbolExternalModule.IsNone)
                {
                    break;
                }
            }
            if (!symbolExternalModule.IsNone)
            {
                var enclosingExternalModule = GetExternalModuleContainer(enclosingDeclaration);
                if (symbolExternalModule != enclosingExternalModule)
                {
                    // name from different external module that is not visible
                    return SymbolAccessibility.CannotBeNamed;
                }
            }
            // Just a local name that is not accessible
            return SymbolAccessibility.NotAccessible;
        }
        return SymbolAccessibility.Accessible;
    }

    // checker.ts isAnySymbolAccessible (shouldComputeAliasesToMakeVisible false)
    private SymbolAccessibility? IsAnySymbolAccessible(List<SymbolRef>? symbols, NodeRef enclosingDeclaration, FakeScope? fakeScope, SymbolRef initialSymbol, SymbolFlags meaning, bool allowModules)
    {
        if (symbols is not { Count: > 0 })
        {
            return null;
        }
        var hadAccessibleChain = SymbolRef.None;
        foreach (var symbol in symbols)
        {
            // Symbol is accessible if it by itself is accessible
            var accessibleSymbolChain = GetAccessibleSymbolChain(symbol, enclosingDeclaration, fakeScope, meaning, false);
            if (accessibleSymbolChain is not null)
            {
                hadAccessibleChain = symbol;
                if (HasVisibleDeclarations(accessibleSymbolChain[0]))
                {
                    return SymbolAccessibility.Accessible;
                }
            }
            if (allowModules)
            {
                if ((DeclarationsOf(symbol) ?? []).Exists(HasNonGlobalAugmentationExternalModuleSymbol))
                {
                    // Any meaning of a module symbol is always accessible via an `import` type
                    return SymbolAccessibility.Accessible;
                }
            }
            // If we haven't got the accessible symbol, it doesn't mean the symbol is actually inaccessible.
            // It could be a qualified symbol and hence verify the path
            var containers = GetContainersOfSymbol(symbol, enclosingDeclaration, fakeScope, meaning);
            var parentResult = IsAnySymbolAccessible(containers, enclosingDeclaration, fakeScope, initialSymbol, initialSymbol == symbol ? GetQualifiedLeftMeaning(meaning) : meaning, allowModules);
            if (parentResult is not null)
            {
                return parentResult;
            }
        }
        if (!hadAccessibleChain.IsNone)
        {
            return SymbolAccessibility.NotAccessible;
        }
        return null;
    }

    // checker.ts hasVisibleDeclarations (shouldComputeAliasToMakeVisible false: a boolean)
    private bool HasVisibleDeclarations(SymbolRef symbol)
    {
        foreach (var declaration in DeclarationsOf(symbol) ?? [])
        {
            if (Kind(declaration) != SyntaxKind.Identifier && !GetIsDeclarationVisible(declaration))
            {
                return false;
            }
        }
        return true;

        bool GetIsDeclarationVisible(NodeRef declaration)
        {
            if (IsDeclarationVisible(declaration))
            {
                return true;
            }
            // Mark the unexported alias as visible if its parent is visible
            // because these kind of aliases can be used to name types in declaration file
            var store = StoreOf(declaration);
            var n = declaration.Node;
            var anyImportSyntax = GetAnyImportSyntax(store, n);
            if (!anyImportSyntax.IsNone &&
                !Utilities.HasSyntacticModifierUncached(store, anyImportSyntax, ModifierFlags.Export) && // import clause without export
                IsDeclarationVisible(At(declaration, store.Parent(anyImportSyntax))))
            {
                return true;
            }
            var kind = store.Kind(n);
            if (kind == SyntaxKind.VariableDeclaration && store.Kind(store.Parent(store.Parent(n))) == SyntaxKind.VariableStatement &&
                !Utilities.HasSyntacticModifierUncached(store, store.Parent(store.Parent(n)), ModifierFlags.Export) && // unexported variable statement
                IsDeclarationVisible(At(declaration, store.Parent(store.Parent(store.Parent(n))))))
            {
                return true;
            }
            if (IsLateVisibilityPaintedStatement(kind) // unexported top-level statement
                && !Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Export)
                && IsDeclarationVisible(At(declaration, store.Parent(n))))
            {
                return true;
            }
            if (kind == SyntaxKind.BindingElement)
            {
                if ((SymbolFlagsOf(symbol) & SymbolFlags.Alias) != 0 && Utilities.IsInJSFile(store, n))
                {
                    // exported import-like top-level JS require statement (C9)
                    var p = store.Parent(n);
                    var vd = p.IsNone ? Node.None : store.Parent(p);
                    if (!vd.IsNone && store.Kind(vd) == SyntaxKind.VariableDeclaration)
                    {
                        var vs = store.Parent(store.Parent(vd));
                        if (!vs.IsNone && store.Kind(vs) == SyntaxKind.VariableStatement && !Utilities.HasSyntacticModifierUncached(store, vs, ModifierFlags.Export) &&
                            !store.Parent(vs).IsNone && IsDeclarationVisible(At(declaration, store.Parent(vs))))
                        {
                            return true;
                        }
                    }
                }
                else if ((SymbolFlagsOf(symbol) & SymbolFlags.BlockScopedVariable) != 0)
                {
                    var rootDeclaration = Utilities.WalkUpBindingElementsAndPatterns(store, n);
                    if (store.Kind(rootDeclaration) == SyntaxKind.Parameter)
                    {
                        return false;
                    }
                    var variableStatement = store.Parent(store.Parent(rootDeclaration));
                    if (store.Kind(variableStatement) != SyntaxKind.VariableStatement)
                    {
                        return false;
                    }
                    if (Utilities.HasSyntacticModifierUncached(store, variableStatement, ModifierFlags.Export))
                    {
                        return true;
                    }
                    return IsDeclarationVisible(At(declaration, store.Parent(variableStatement)));
                }
            }
            // Declaration is not visible
            return false;
        }
    }

    // utilities.ts isLateVisibilityPaintedStatement
    private static bool IsLateVisibilityPaintedStatement(SyntaxKind kind) => kind is
        SyntaxKind.ImportDeclaration or SyntaxKind.ImportEqualsDeclaration or SyntaxKind.VariableStatement or SyntaxKind.ClassDeclaration or
        SyntaxKind.FunctionDeclaration or SyntaxKind.ModuleDeclaration or SyntaxKind.TypeAliasDeclaration or SyntaxKind.InterfaceDeclaration or
        SyntaxKind.EnumDeclaration;

    // utilities.ts getAnyImportSyntax
    private static Node GetAnyImportSyntax(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ImportEqualsDeclaration:
                return node;
            case SyntaxKind.ImportClause:
                return store.Parent(node);
            case SyntaxKind.NamespaceImport:
                return store.Parent(store.Parent(node));
            case SyntaxKind.ImportSpecifier:
                return store.Parent(store.Parent(store.Parent(node)));
            default:
                return Node.None;
        }
    }

    // checker.ts isDeclarationVisible (without the node links cache)
    private bool IsDeclarationVisible(NodeRef node)
    {
        if (node.IsNone)
        {
            return false;
        }
        // NodeLinks.isVisible (module D5): the computed result is kept, and declaration emit paints aliases visible
        // (hasVisibleDeclarations, collectLinkedAliases); before D5 nothing painted and the result was recomputed.
        if (isVisibleLinks.TryGetValue(node, out var isVisible))
        {
            return isVisible;
        }
        isVisible = DetermineIfDeclarationIsVisible(node);
        isVisibleLinks[node] = isVisible;
        return isVisible;
    }

    // NodeLinks.isVisible of every node whose visibility was computed or painted
    private readonly Dictionary<NodeRef, bool> isVisibleLinks = [];

    // checker.ts isDeclarationVisible's determineIfDeclarationIsVisible
    private bool DetermineIfDeclarationIsVisible(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.JSDocCallbackTag:
            case SyntaxKind.JSDocTypedefTag:
            case SyntaxKind.JSDocEnumTag:
            {
                // Top-level jsdoc type aliases are considered exported
                // First parent is comment node, second is hosting declaration or token; we only care about those tokens or declarations whose parent is a source file
                var p = store.Parent(n);
                var pp = p.IsNone ? Node.None : store.Parent(p);
                var ppp = pp.IsNone ? Node.None : store.Parent(pp);
                return !ppp.IsNone && store.Kind(ppp) == SyntaxKind.SourceFile;
            }
            case SyntaxKind.BindingElement:
                return IsDeclarationVisible(At(node, store.Parent(store.Parent(n))));
            case SyntaxKind.VariableDeclaration:
            {
                var name = store.Name(n);
                if (store.Kind(name) is SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern && store.Length(store.Elements(name)) == 0)
                {
                    // If the binding pattern is empty, this variable declaration is not visible
                    return false;
                }
                goto case SyntaxKind.ModuleDeclaration;
            }
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
            {
                // external module augmentation is always visible
                if (IsExternalModuleAugmentation(store, n))
                {
                    return true;
                }
                var parent = GetDeclarationContainer(store, n);
                // If the node is not exported or it is not ambient module element (except import declaration)
                if ((Utilities.GetCombinedModifierFlags(store, n) & ModifierFlags.Export) == 0 &&
                    !(store.Kind(n) != SyntaxKind.ImportEqualsDeclaration && store.Kind(parent) != SyntaxKind.SourceFile && (store.Flags(parent) & NodeFlags.Ambient) != 0))
                {
                    return IsGlobalSourceFile(At(node, parent));
                }
                // Exported members/ambient module elements (exception import declaration) are visible if parent is visible
                return IsDeclarationVisible(At(node, parent));
            }
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
                // hasEffectiveModifier (JSDoc accessibility tags in JavaScript: C9)
                if ((Utilities.GetSyntacticModifierFlagsNoCache(store, n) & (ModifierFlags.Private | ModifierFlags.Protected)) != 0)
                {
                    // Private/protected properties/methods are not visible
                    return false;
                }
                // Public properties/methods are visible if its parents are visible, so:
                goto case SyntaxKind.Constructor;
            case SyntaxKind.Constructor:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.CallSignature:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.Parameter:
            case SyntaxKind.ModuleBlock:
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.TypeLiteral:
            case SyntaxKind.TypeReference:
            case SyntaxKind.ArrayType:
            case SyntaxKind.TupleType:
            case SyntaxKind.UnionType:
            case SyntaxKind.IntersectionType:
            case SyntaxKind.ParenthesizedType:
            case SyntaxKind.NamedTupleMember:
                return IsDeclarationVisible(At(node, store.Parent(n)));
            // Default binding, import specifier and namespace import is visible
            // only on demand so by default it is not visible
            case SyntaxKind.ImportClause:
            case SyntaxKind.NamespaceImport:
            case SyntaxKind.ImportSpecifier:
                return false;
            // Type parameters are always visible
            case SyntaxKind.TypeParameter:
            // Source file and namespace export are always visible
            case SyntaxKind.SourceFile:
            case SyntaxKind.NamespaceExportDeclaration:
                return true;
            // Export assignments do not create name bindings outside the module
            case SyntaxKind.ExportAssignment:
                return false;
            default:
                return false;
        }
    }

    // utilities.ts isExternalModuleAugmentation
    private static bool IsExternalModuleAugmentation(NodeStore store, Node node)
    {
        // isAmbientModule: a module with a string literal name or the global scope augmentation
        if (!Utilities.IsAmbientModule(store, node))
        {
            return false;
        }
        // isModuleAugmentationExternal
        var parent = store.Parent(node);
        switch (store.Kind(parent))
        {
            case SyntaxKind.SourceFile:
                return Utilities.IsExternalModule(store, parent);
            case SyntaxKind.ModuleBlock:
            {
                var grandParent = store.Parent(parent);
                return Utilities.IsAmbientModule(store, grandParent) && store.Kind(store.Parent(grandParent)) == SyntaxKind.SourceFile && !Utilities.IsExternalModule(store, store.Parent(grandParent));
            }
        }
        return false;
    }

    // utilities.ts getDeclarationContainer
    private static Node GetDeclarationContainer(NodeStore store, Node node)
    {
        var n = node;
        // findAncestor(getRootDeclaration(node), node => isVariableDeclarationList ... ) skipping variable declaration lists etc.
        while (store.Kind(n) == SyntaxKind.BindingElement)
        {
            n = store.Parent(store.Parent(n));
        }
        for (var p = n; !p.IsNone; p = store.Parent(p))
        {
            switch (store.Kind(p))
            {
                case SyntaxKind.VariableDeclaration:
                case SyntaxKind.VariableDeclarationList:
                case SyntaxKind.ImportSpecifier:
                case SyntaxKind.NamedImports:
                case SyntaxKind.NamespaceImport:
                case SyntaxKind.ImportClause:
                    continue;
                default:
                    return store.Parent(p);
            }
        }
        return Node.None;
    }

    // checker.ts getMeaningOfEntityNameReference
    private static SymbolFlags GetMeaningOfEntityNameReference(NodeStore store, Node entityName)
    {
        var parent = store.Parent(entityName);
        var parentKind = store.Kind(parent);
        if (parentKind == SyntaxKind.TypeQuery ||
            (parentKind == SyntaxKind.ExpressionWithTypeArguments && !Utilities.IsPartOfTypeNode(store, parent)) ||
            parentKind == SyntaxKind.ComputedPropertyName ||
            (parentKind == SyntaxKind.TypePredicate && store.ParameterName(parent) == entityName))
        {
            // Typeof value
            return SymbolFlags.Value | SymbolFlags.ExportValue;
        }
        var kind = store.Kind(entityName);
        if (kind is SyntaxKind.QualifiedName or SyntaxKind.PropertyAccessExpression ||
            parentKind == SyntaxKind.ImportEqualsDeclaration ||
            (parentKind == SyntaxKind.QualifiedName && store.Left(parent) == entityName) ||
            (parentKind == SyntaxKind.PropertyAccessExpression && store.Expression(parent) == entityName) ||
            (parentKind == SyntaxKind.ElementAccessExpression && store.Expression(parent) == entityName))
        {
            // Left identifier from type reference or TypeAlias
            // Entity name of the import declaration
            return SymbolFlags.Namespace;
        }
        // Type Reference or TypeAlias entity = Identifier
        return SymbolFlags.Type;
    }

    // checker.ts isEntityNameVisible (shouldComputeAliasToMakeVisible false: the accessibility only)
    private SymbolAccessibility IsEntityNameVisible(NodeRef entityName, NodeRef enclosingDeclaration, FakeScope? fakeScope)
    {
        var store = StoreOf(entityName);
        var meaning = GetMeaningOfEntityNameReference(store, entityName.Node);
        var firstIdentifier = At(entityName, Utilities.GetFirstIdentifier(store, entityName.Node));
        var symbol = ResolveNameInScope(enclosingDeclaration, fakeScope, store.Texts.GetString(store.EscapedText(firstIdentifier.Node)) ?? "", meaning);
        if (!symbol.IsNone && (SymbolFlagsOf(symbol) & SymbolFlags.TypeParameter) != 0 && (meaning & SymbolFlags.Type) != 0)
        {
            return SymbolAccessibility.Accessible;
        }
        if (symbol.IsNone && Utilities.IsThisIdentifier(store, firstIdentifier.Node) &&
            IsSymbolAccessible(GetSymbolOfDeclaration(At(entityName, Utilities.GetThisContainer(store, firstIdentifier.Node, false, false))), firstIdentifier, null, meaning) == SymbolAccessibility.Accessible)
        {
            return SymbolAccessibility.Accessible;
        }
        if (symbol.IsNone)
        {
            return SymbolAccessibility.NotResolved;
        }
        // Verify if the symbol is accessible
        return HasVisibleDeclarations(symbol) ? SymbolAccessibility.Accessible : SymbolAccessibility.NotAccessible;
    }

    /// <summary>
    /// resolveName(location, name, meaning, undefined, isUse: false) where the location may be a node builder fake scope
    /// (a Block whose locals are looked up first, as resolveNameHelper does for any block).
    /// </summary>
    private SymbolRef ResolveNameInScope(NodeRef location, FakeScope? fakeScope, string name, SymbolFlags meaning, bool isUse = false)
    {
        for (var fake = fakeScope; fake is not null; fake = fake.Parent)
        {
            var result = GetSymbol(fake.Locals, name, meaning);
            if (!result.IsNone)
            {
                return result;
            }
        }
        if (location.IsNone)
        {
            return GetSymbol(globals, name, meaning);
        }
        if (fakeScope is not null)
        {
            // The enclosing declaration is reached from the fake Block (lastLocation, a synthesized node): in a function-like
            // declaration its local types are not in scope there (resolveNameHelper's function-like locals rule; type
            // parameters and parameters are, since the last location is synthesized)
            var store = StoreOf(location);
            if (Utilities.IsFunctionLike(store, location.Node) && (meaning & SymbolFlags.Type) != 0)
            {
                var local = GetSymbol(LocalsOf(location), name, meaning);
                if (!local.IsNone && (meaning & SymbolFlagsOf(local) & SymbolFlags.Type) != 0 && (SymbolFlagsOf(local) & SymbolFlags.TypeParameter) == 0)
                {
                    var parent = Parent(location);
                    return parent.IsNone ? GetSymbol(globals, name, meaning) : ResolveName(parent, name, NodeRef.None, meaning, null, isUse);
                }
            }
        }
        return ResolveName(location, name, NodeRef.None, meaning, null, isUse);
    }
}
