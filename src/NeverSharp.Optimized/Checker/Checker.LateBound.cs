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
        return Utilities.IsEntityNameExpression(store, expression) && IsTypeUsableAsPropertyName(CheckLateBoundNameExpression(At(name, expression)));
    }

    // checkComputedPropertyName / checkExpressionCached of the name's expression (the computed name checks: C8)
    private Type CheckLateBoundNameExpression(NodeRef expression) => CheckExpressionCached(expression);

    // checker.ts hasLateBindableName
    private bool HasLateBindableName(NodeRef node)
    {
        var name = GetNameOfDeclarationRef(node);
        return !name.IsNone && IsLateBindableName(name);
    }

    // checker.ts getMembersOfDeclaration
    private static NodeList GetMembersOfDeclaration(NodeStore store, Node node) => store.Kind(node) switch
    {
        SyntaxKind.InterfaceDeclaration or SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression or SyntaxKind.TypeLiteral => store.Members(node),
        SyntaxKind.ObjectLiteralExpression => store.Properties(node),
        _ => NodeList.None,
    };

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
        var lateSymbols = new SymTable();
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
                // (lateBindIndexSignature: computed names of non-literal key types, C8)
            }
        }
        // (JavaScript assignment declaration members: C9)
        resolved = CombineSymbolTables(earlySymbols, lateSymbols) ?? emptySymbols;
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
        var combined = new SymTable(first);
        MergeSymbolTable(combined, second);
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
            var type = CheckLateBoundNameExpression(At(declName, store.Kind(declName.Node) == SyntaxKind.ElementAccessExpression ? store.ArgumentExpression(declName.Node) : store.Expression(declName.Node)));
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
        lateSymbolsOfEarlySymbols[memberSymbol] = symbol;
        if (record.Declarations is null)
        {
            record.Declarations = [member];
        }
        else
        {
            // (isReplaceableByMethod members of JavaScript: C9)
            record.Declarations.Add(member);
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
