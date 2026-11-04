// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (late-bound members: isLateBindableName, hasLateBindableName, lateBindMember,
// addDeclarationToLateBoundSymbol, getResolvedMembersOrExportsOfSymbol, getLateBoundSymbol)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C3. Late-bound index signatures (lateBindIndexSignature) and the
// assignment declaration members of JavaScript (C9) are left out; the late symbols live in the checker's tables.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private readonly Dictionary<SymbolRef, SymTable> resolvedLateMembers = [];
    private readonly Dictionary<SymbolRef, SymTable> resolvedLateExports = [];

    // links.lateSymbol of the early-bound `__computed` symbols, and NodeLinks.resolvedSymbol of late-bound members
    private readonly Dictionary<SymbolRef, SymbolRef> lateSymbolsOfEarlySymbols = [];
    private readonly Dictionary<NodeRef, SymbolRef> lateBoundMemberSymbols = [];

    // checker.ts isLateBindableName / isLateBindableAST (computed property names of entity name expressions whose type
    // is a string or number literal type or a unique symbol type)
    private bool IsLateBindableName(NodeRef name)
    {
        var store = StoreOf(name);
        if (store.Kind(name.Node) != SyntaxKind.ComputedPropertyName && store.Kind(name.Node) != SyntaxKind.ElementAccessExpression)
        {
            return false;
        }
        var expression = store.Kind(name.Node) == SyntaxKind.ComputedPropertyName ? store.Expression(name.Node) : store.ArgumentExpression(name.Node);
        return Utilities.IsEntityNameExpression(store, expression) &&
            IsTypeUsableAsPropertyName(store.Kind(name.Node) == SyntaxKind.ComputedPropertyName ? CheckComputedPropertyName(name) : CheckExpressionCached(At(name, expression)));
    }

    // checker.ts hasLateBindableName
    private bool HasLateBindableName(NodeRef node)
    {
        var name = GetNameOfDeclarationRef(node);
        return !name.IsNone && IsLateBindableName(name);
    }

    // checker.ts isLateBindableIndexSignature (isLateBindableAST and isTypeUsableAsIndexSignature)
    private bool IsLateBindableIndexSignature(NodeRef name)
    {
        if (name.IsNone)
        {
            return false;
        }
        var store = StoreOf(name);
        if (store.Kind(name.Node) != SyntaxKind.ComputedPropertyName && store.Kind(name.Node) != SyntaxKind.ElementAccessExpression)
        {
            return false;
        }
        var expression = store.Kind(name.Node) == SyntaxKind.ComputedPropertyName ? store.Expression(name.Node) : store.ArgumentExpression(name.Node);
        return Utilities.IsEntityNameExpression(store, expression) &&
            IsTypeAssignableTo(store.Kind(name.Node) == SyntaxKind.ComputedPropertyName ? CheckComputedPropertyName(name) : CheckExpressionCached(At(name, expression)), stringNumberSymbolType);
    }

    // checker.ts hasLateBindableIndexSignature
    private bool HasLateBindableIndexSignature(NodeRef node) => IsLateBindableIndexSignature(GetNameOfDeclarationRef(node));

    // checker.ts lateBindIndexSignature
    private void LateBindIndexSignature(SymTable? earlySymbols, SymTable lateSymbols, NodeRef decl)
    {
        // First, late bind the index symbol itself, if needed
        var indexSymbol = lateSymbols.Get(InternalSymbolName.Index);
        if (indexSymbol.IsNone)
        {
            var early = earlySymbols?.Get(InternalSymbolName.Index) ?? SymbolRef.None;
            if (early.IsNone)
            {
                indexSymbol = CreateSymbol(SymbolFlags.None, InternalSymbolName.Index, CheckFlags.Late);
            }
            else
            {
                indexSymbol = CloneSymbol(early);
                TransientSymbols.Record(indexSymbol).CheckFlags |= CheckFlags.Late;
            }
            lateSymbols.Set(InternalSymbolName.Index, indexSymbol);
        }
        // Then just add the computed name as a late bound declaration
        // (note: unlike `addDeclarationToLateBoundSymbol` we do not set up a `.lateSymbol` on `decl`'s links,
        // since that would point at an index symbol and not a single property symbol, like most consumers would expect)
        var declSymbol = NodeSymbol(decl);
        if (TransientSymbols.Record(indexSymbol).Declarations.IsUndefined)
        {
            TransientSymbols.Record(indexSymbol).Declarations = NewDeclarations([decl]);
        }
        else if (declSymbol.IsNone || declSymbol.IsTransient || BindingOf(declSymbol.File).IsReplaceableByMethod(declSymbol.BinderSymbol) != true)
        {
            AddDeclaration(indexSymbol, decl);
        }
    }

    // checker.ts getMembersOfDeclaration
    private static NodeList GetMembersOfDeclaration(NodeStore store, Node node) => store.Kind(node) switch
    {
        SyntaxKind.InterfaceDeclaration or SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression or SyntaxKind.TypeLiteral => store.Members(node),
        SyntaxKind.ObjectLiteralExpression => store.Properties(node),
        _ => NodeList.None,
    };

    // getSymbolLinks(symbol).resolvedExports = undefined; .resolvedMembers = undefined (getCommonJsExportEquals)
    private void ResetResolvedMembersOrExports(SymbolRef symbol)
    {
        resolvedLateExports.Remove(symbol);
        resolvedLateMembers.Remove(symbol);
        resolvedModuleExports.Remove(symbol);
        symbolStateVersion++; // module EO2: a cached export table is dropped (Checker.ExportIndex.cs)
    }

    /// <summary>checker.ts getResolvedMembersOrExportsOfSymbol.</summary>
    private SymTable GetResolvedMembersOrExportsOfSymbol(SymbolRef symbol, bool isStatic)
    {
        var cache = isStatic ? resolvedLateExports : resolvedLateMembers;
        if (cache.TryGetValue(symbol, out var resolved))
        {
            return resolved;
        }
        var earlySymbols = !isStatic ? MembersOf(symbol)
            : (SymbolFlagsOf(symbol) & SymbolFlags.Module) != 0 ? GetExportsOfModule(symbol)
            : ExportsOf(symbol);
        // In the event we recursively resolve the members/exports of the symbol, we
        // set the initial value of resolvedMembers/resolvedExports to the early-bound
        // members/exports of the symbol.
        cache[symbol] = earlySymbols ?? emptySymbols;
        // fill in any as-yet-unresolved late-bound members.
        var lateSymbols = new SymTable(Names);
        foreach (var decl in DeclarationsOf(symbol) ?? [])
        {
            var store = StoreOf(decl);
            var members = GetMembersOfDeclaration(store, decl.Node);
            if (members.IsNone)
            {
                continue;
            }
            foreach (var member in store.Nodes(members))
            {
                var memberRef = At(decl, member);
                if (isStatic == Utilities.HasSyntacticModifierUncached(store, member, ModifierFlags.Static) && HasLateBindableName(memberRef))
                {
                    LateBindMember(symbol, earlySymbols, lateSymbols, memberRef);
                }
                else if (isStatic == Utilities.HasSyntacticModifierUncached(store, member, ModifierFlags.Static) && HasLateBindableIndexSignature(memberRef))
                {
                    LateBindIndexSignature(earlySymbols, lateSymbols, memberRef);
                }
            }
        }
        var valueDeclaration = ValueDeclarationOf(symbol);
        var assignmentsOwner = symbol;
        if (!valueDeclaration.IsNone && Kind(valueDeclaration) is SyntaxKind.ArrowFunction or SyntaxKind.FunctionExpression)
        {
            var parentSymbol = GetSymbolOfNode(Parent(valueDeclaration));
            if (!parentSymbol.IsNone && !parentSymbol.IsTransient && !BindingOf(parentSymbol.File).AssignmentDeclarationMembers(parentSymbol.BinderSymbol).IsUndefined)
            {
                assignmentsOwner = parentSymbol;
            }
        }
        if (!assignmentsOwner.IsTransient)
        {
            foreach (var member in BindingOf(assignmentsOwner.File).AssignmentDeclarationMembers(assignmentsOwner.BinderSymbol))
            {
                var memberRef = new NodeRef(assignmentsOwner.File, member);
                var store = StoreOf(memberRef);
                var assignmentKind = Utilities.GetAssignmentDeclarationKind(store, member);
                var isInstanceMember = assignmentKind is AssignmentDeclarationKind.PrototypeProperty or AssignmentDeclarationKind.ObjectDefinePrototypeProperty or AssignmentDeclarationKind.Prototype ||
                    (store.Kind(member) == SyntaxKind.BinaryExpression && IsPossiblyAliasedThisProperty(memberRef, assignmentKind));
                if (isStatic == !isInstanceMember && HasLateBindableName(memberRef))
                {
                    LateBindMember(symbol, earlySymbols, lateSymbols, memberRef);
                }
            }
        }
        var combined = CombineSymbolTables(earlySymbols, lateSymbols);
        // If this is a CommonJS export merge (getCommonJsExportEquals), the late-bound members of the declarations'
        // own symbols were resolved there (lateBindMember binds a member once), so they are merged in from those
        if (symbol.IsTransient && cjsExportMerged.ContainsKey(symbol) && DeclarationsOf(symbol) is { } declarations)
        {
            foreach (var decl in declarations)
            {
                SymTable? original = cache.TryGetValue(NodeSymbol(decl), out var o) ? o : null;
                if (combined is null)
                {
                    combined = original;
                    continue;
                }
                if (original is null)
                {
                    continue;
                }
                var target = combined.Value;
                var source = original.Value;
                for (var i = 0; i < source.Count; i++)
                {
                    var name = source.NameIdAt(i);
                    var s = source.ValueAt(i);
                    var existing = target.Get(name);
                    if (existing.IsNone)
                    {
                        target.Set(name, s);
                    }
                    else if (existing != s)
                    {
                        target.Set(name, MergeSymbol(existing, s));
                    }
                }
            }
        }
        resolved = combined ?? emptySymbols;
        cache[symbol] = resolved;
        return resolved;
    }

    // checker.ts combineSymbolTables
    private SymTable? CombineSymbolTables(SymTable? first, SymTable? second)
    {
        if (first is null || first.Count == 0)
        {
            return second is { Count: > 0 } ? second : first;
        }
        if (second is null || second.Count == 0)
        {
            return first;
        }
        // (mergeSymbolTable(combined, first): the merged symbol of each entry, e.g. the late-bound clone of an early
        // `__index` symbol that lateBindIndexSignature recorded as its merge target)
        var combined = new SymTable(Names);
        MergeSymbolTable(combined, first.Value);
        MergeSymbolTable(combined, second.Value);
        return combined;
    }

    // checker.ts lateBindMember
    private SymbolRef LateBindMember(SymbolRef parent, SymTable? earlySymbols, SymTable lateSymbols, NodeRef decl)
    {
        var declSymbol = NodeSymbol(decl);
        if (!lateBoundMemberSymbols.TryGetValue(decl, out var resolvedSymbol))
        {
            // In the event we attempt to resolve the late-bound name of this member recursively,
            // fall back to the early-bound name of this member.
            lateBoundMemberSymbols[decl] = declSymbol;
            var declName = GetNameOfDeclarationRef(decl);
            var store = StoreOf(declName);
            var type = store.Kind(declName.Node) == SyntaxKind.ElementAccessExpression ? CheckExpressionCached(At(declName, store.ArgumentExpression(declName.Node))) : CheckComputedPropertyName(declName);
            if (IsTypeUsableAsPropertyName(type))
            {
                var memberName = GetPropertyNameFromType(type);
                var symbolFlags = SymbolFlagsOf(declSymbol);
                // Get or add a late-bound symbol for the member. This allows us to merge late-bound accessor declarations.
                var lateSymbol = lateSymbols.Get(memberName);
                if (lateSymbol.IsNone)
                {
                    lateSymbols.Set(memberName, lateSymbol = CreateSymbol(SymbolFlags.None, memberName, CheckFlags.Late));
                }
                // Report an error if there's a symbol declaration with the same name and conflicting flags.
                var earlySymbol = earlySymbols?.Get(memberName) ?? SymbolRef.None;
                // Duplicate property declarations of classes are checked in checkClassForDuplicateDeclarations.
                if ((SymbolFlagsOf(parent) & SymbolFlags.Class) == 0 && (SymbolFlagsOf(lateSymbol) & GetExcludedSymbolFlags(symbolFlags)) != 0)
                {
                    // If we have an existing early-bound member, combine its declarations so that we can
                    // report an error at each declaration.
                    var declarations = new List<NodeRef>();
                    if (!earlySymbol.IsNone)
                    {
                        declarations.AddRange(DeclarationsOf(earlySymbol) ?? []);
                    }
                    declarations.AddRange(DeclarationsOf(lateSymbol) ?? []);
                    var name = (Types.Flags(type) & TypeFlags.UniqueESSymbol) == 0 && memberName.Length > 0 ? Utilities.UnescapeLeadingUnderscores(memberName) : Utilities.DeclarationNameToString(store, declName.Node);
                    foreach (var declaration in declarations)
                    {
                        var declarationName = GetNameOfDeclarationRef(declaration);
                        Error(!declarationName.IsNone ? declarationName : declaration, Diagnostics.Property_0_was_also_declared_here, name);
                    }
                    Error(declName, Diagnostics.Duplicate_property_0, name);
                    lateSymbol = CreateSymbol(SymbolFlags.None, memberName, CheckFlags.Late);
                }
                ValueSymbolLinks.Get(lateSymbol).NameType = type;
                AddDeclarationToLateBoundSymbol(lateSymbol, decl, declSymbol, symbolFlags);
                ref var record = ref TransientSymbols.Record(lateSymbol);
                if (record.Parent.IsNone)
                {
                    record.Parent = parent;
                }
                lateBoundMemberSymbols[decl] = lateSymbol;
                return lateSymbol;
            }
            return declSymbol;
        }
        return resolvedSymbol;
    }

    // checker.ts addDeclarationToLateBoundSymbol
    private void AddDeclarationToLateBoundSymbol(SymbolRef symbol, NodeRef member, SymbolRef memberSymbol, SymbolFlags symbolFlags)
    {
        ref var record = ref TransientSymbols.Record(symbol);
        record.Flags |= symbolFlags;
        if ((record.Flags & SymbolFlags.Alias) != 0)
        {
            symbolStateVersion++; // module EO2: resolveSymbol's answer for an alias may change (Checker.ExportIndex.cs)
        }
        lateSymbolsOfEarlySymbols[memberSymbol] = symbol;
        if (record.Declarations.IsUndefined)
        {
            record.Declarations = NewDeclarations([member]);
        }
        else if (memberSymbol.IsNone || memberSymbol.IsTransient || BindingOf(memberSymbol.File).IsReplaceableByMethod(memberSymbol.BinderSymbol) != true)
        {
            AddDeclaration(symbol, member);
        }
        if ((symbolFlags & SymbolFlags.Value) != 0)
        {
            // setValueDeclaration
            var valueDeclaration = record.ValueDeclaration;
            if (valueDeclaration.IsNone ||
                (Kind(member) != SyntaxKind.ModuleDeclaration && Kind(valueDeclaration) == SyntaxKind.ModuleDeclaration))
            {
                record.ValueDeclaration = member;
            }
        }
    }

    /// <summary>checker.ts getLateBoundSymbol.</summary>
    internal SymbolRef GetLateBoundSymbol(SymbolRef symbol)
    {
        if (symbol.IsNone || (SymbolFlagsOf(symbol) & SymbolFlags.ClassMember) == 0 || NameOf(symbol) != InternalSymbolName.Computed)
        {
            return symbol;
        }
        if (!lateSymbolsOfEarlySymbols.TryGetValue(symbol, out var late))
        {
            var declarations = DeclarationsOf(symbol) ?? [];
            if (declarations.Exists(HasLateBindableName))
            {
                // force late binding of members/exports. This will set the late-bound symbol
                var parent = GetMergedSymbol(ParentOf(symbol));
                if (declarations.Exists(d => Utilities.HasSyntacticModifierUncached(StoreOf(d), d.Node, ModifierFlags.Static)))
                {
                    GetExportsOfSymbol(parent);
                }
                else
                {
                    GetMembersOfSymbol(parent);
                }
            }
            if (!lateSymbolsOfEarlySymbols.TryGetValue(symbol, out late))
            {
                lateSymbolsOfEarlySymbols[symbol] = late = symbol;
            }
        }
        return late;
    }
}
