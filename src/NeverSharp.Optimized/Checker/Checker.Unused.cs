// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (symbol.isReferenced, markPropertyAsReferenced, registerForUnusedIdentifiersCheck,
// checkUnusedIdentifiers, checkUnusedClassMembers, checkUnusedTypeParameters, checkUnusedInferTypeParameter,
// checkUnusedLocalsAndParameters, isValidUnusedLocalDeclaration, getSuggestionDiagnostics' unused suggestions)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go (checkUnusedIdentifiers)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#. Brought forward from module C8 in module C1 because the unused checks
// only need resolveName's reference marking (core) and their suggestions are a large part of the diagnostics dumps.
// `isReferenced` is kept per checker (binder symbols of shared files are not written).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private readonly Dictionary<SymbolRef, SymbolFlags> referencedSymbols = [];
    private readonly List<NodeRef> potentiallyUnusedIdentifiers = [];

    /// <summary>symbol.isReferenced |= meaning.</summary>
    private void MarkReferenced(SymbolRef symbol, SymbolFlags meaning) =>
        referencedSymbols[symbol] = (referencedSymbols.TryGetValue(symbol, out var flags) ? flags : 0) | meaning;

    /// <summary>symbol.isReferenced (0 when undefined).</summary>
    private SymbolFlags IsReferenced(SymbolRef symbol) => referencedSymbols.TryGetValue(symbol, out var flags) ? flags : 0;

    /// <summary>checker.ts markPropertyAsReferenced.</summary>
    private void MarkPropertyAsReferenced(SymbolRef prop, NodeRef nodeForCheckWriteOnly, bool isSelfTypeAccess)
    {
        if (prop.IsNone || (SymbolFlagsOf(prop) & SymbolFlags.ClassMember) == 0)
        {
            return;
        }
        var valueDeclaration = ValueDeclarationOf(prop);
        if (valueDeclaration.IsNone)
        {
            return;
        }
        var store = StoreOf(valueDeclaration);
        var hasPrivateModifier = (Utilities.GetEffectiveModifierFlags(store, valueDeclaration.Node) & ModifierFlags.Private) != 0; // hasEffectiveModifier
        var name = Utilities.DeclarationNameField(store, valueDeclaration.Node);
        var hasPrivateIdentifier = !name.IsNone && store.Kind(name) == SyntaxKind.PrivateIdentifier;
        if (!hasPrivateModifier && !hasPrivateIdentifier)
        {
            return;
        }
        if (!nodeForCheckWriteOnly.IsNone && Utilities.IsWriteOnlyAccess(StoreOf(nodeForCheckWriteOnly), nodeForCheckWriteOnly.Node) && (SymbolFlagsOf(prop) & SymbolFlags.SetAccessor) == 0)
        {
            return;
        }
        if (isSelfTypeAccess && !nodeForCheckWriteOnly.IsNone)
        {
            // Find any FunctionLikeDeclaration because those create a new 'this' binding. But this should only matter for methods (or getters/setters).
            var s = StoreOf(nodeForCheckWriteOnly);
            for (var n = nodeForCheckWriteOnly.Node; !n.IsNone; n = s.Parent(n))
            {
                if (Utilities.IsFunctionLikeDeclaration(s, n))
                {
                    if (NodeSymbol(At(nodeForCheckWriteOnly, n)) == prop)
                    {
                        return;
                    }
                    break;
                }
            }
        }
        // (getCheckFlags(prop) & CheckFlags.Instantiated ? getSymbolLinks(prop).target : prop).isReferenced
        MarkReferenced((GetCheckFlags(prop) & CheckFlags.Instantiated) != 0 ? ValueSymbolLinks.Get(prop).Target : prop, SymbolFlags.All);
    }

    /// <summary>checker.ts registerForUnusedIdentifiersCheck (lazy diagnostics run eagerly while a file is checked).</summary>
    private void RegisterForUnusedIdentifiersCheck(NodeRef node) => potentiallyUnusedIdentifiers.Add(node);

    /// <summary>getSuggestionDiagnostics: the unused-identifier suggestions of a checked file, in registration order.</summary>
    private List<ProgramDiagnostic> GetUnusedSuggestions(int file)
    {
        var result = new List<ProgramDiagnostic>();
        foreach (var node in potentiallyUnusedIdentifiers)
        {
            if (node.File != file)
            {
                continue;
            }
            CheckUnusedIdentifiers(node, (containingNode, isParameter, diag) =>
            {
                var store = StoreOf(containingNode);
                if (!Utilities.ContainsParseError(store, containingNode.Node) && !UnusedIsError(isParameter, (store.Flags(containingNode.Node) & NodeFlags.Ambient) != 0))
                {
                    diag.Category = DiagnosticCategory.Suggestion;
                    result.Add(diag);
                }
            });
        }
        return result;
    }

    private bool UnusedIsError(bool isParameter, bool isAmbient)
    {
        if (isAmbient)
        {
            return false;
        }
        return isParameter ? compilerOptions.NoUnusedParameters == true : compilerOptions.NoUnusedLocals == true;
    }

    // checker.ts checkUnusedIdentifiers (for one registered node)
    private void CheckUnusedIdentifiers(NodeRef node, Action<NodeRef, bool, ProgramDiagnostic> addDiagnostic)
    {
        var store = StoreOf(node);
        switch (store.Kind(node.Node))
        {
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
                CheckUnusedClassMembers(node, addDiagnostic);
                CheckUnusedTypeParameters(node, addDiagnostic);
                break;
            case SyntaxKind.SourceFile:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.Block:
            case SyntaxKind.CaseBlock:
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
                CheckUnusedLocalsAndParameters(node, addDiagnostic);
                break;
            case SyntaxKind.Constructor:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                // Only report unused parameters on the implementation, not overloads.
                if (!store.Body(node.Node).IsNone)
                {
                    CheckUnusedLocalsAndParameters(node, addDiagnostic);
                }
                CheckUnusedTypeParameters(node, addDiagnostic);
                break;
            case SyntaxKind.MethodSignature:
            case SyntaxKind.CallSignature:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.InterfaceDeclaration:
                CheckUnusedTypeParameters(node, addDiagnostic);
                break;
            case SyntaxKind.InferType:
            {
                var typeParameter = At(node, store.TypeParameter(node.Node));
                if (IsTypeParameterUnused(typeParameter))
                {
                    addDiagnostic(node, true, CreateDiagnosticForNode(node, Diagnostics._0_is_declared_but_its_value_is_never_read,
                        store.Texts.GetString(Utilities.IdText(store, store.Name(typeParameter.Node)))));
                }
                break;
            }
        }
    }

    private void ErrorUnusedLocal(NodeRef declaration, string name, Action<NodeRef, bool, ProgramDiagnostic> addDiagnostic)
    {
        var node = GetNameOfDeclarationRef(declaration);
        if (node.IsNone)
        {
            node = declaration;
        }
        var message = IsTypeDeclaration(declaration) ? Diagnostics._0_is_declared_but_never_used : Diagnostics._0_is_declared_but_its_value_is_never_read;
        addDiagnostic(declaration, false, CreateDiagnosticForNode(node, message, name));
    }

    private bool IsIdentifierThatStartsWithUnderscore(NodeRef node)
    {
        var store = StoreOf(node);
        return store.Kind(node.Node) == SyntaxKind.Identifier && store.Texts.Span(Utilities.IdText(store, node.Node)) is var text && text.Length > 0 && text[0] == '_';
    }

    private void CheckUnusedClassMembers(NodeRef node, Action<NodeRef, bool, ProgramDiagnostic> addDiagnostic)
    {
        var store = StoreOf(node);
        foreach (var memberNode in store.Nodes(store.Members(node.Node)))
        {
            var member = At(node, memberNode);
            switch (store.Kind(memberNode))
            {
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                {
                    if (store.Kind(memberNode) == SyntaxKind.SetAccessor && (SymbolFlagsOf(NodeSymbol(member)) & SymbolFlags.GetAccessor) != 0)
                    {
                        // Already would have reported an error on the getter.
                        break;
                    }
                    var symbol = GetSymbolOfDeclaration(member);
                    var name = store.Name(memberNode);
                    if (!symbol.IsNone && IsReferenced(symbol) == 0
                        && ((Utilities.GetEffectiveModifierFlags(store, memberNode) & ModifierFlags.Private) != 0 || (!name.IsNone && store.Kind(name) == SyntaxKind.PrivateIdentifier)) // hasEffectiveModifier
                        && (store.Flags(memberNode) & NodeFlags.Ambient) == 0)
                    {
                        addDiagnostic(member, false, CreateDiagnosticForNode(At(node, name), Diagnostics._0_is_declared_but_its_value_is_never_read, SymbolToString(symbol)));
                    }
                    break;
                }
                case SyntaxKind.Constructor:
                    foreach (var parameter in store.Nodes(store.Parameters(memberNode)))
                    {
                        var parameterSymbol = NodeSymbol(At(node, parameter));
                        if (!parameterSymbol.IsNone && IsReferenced(parameterSymbol) == 0 && Utilities.HasSyntacticModifierUncached(store, parameter, ModifierFlags.Private))
                        {
                            addDiagnostic(At(node, parameter), false, CreateDiagnosticForNode(At(node, store.Name(parameter)), Diagnostics.Property_0_is_declared_but_its_value_is_never_read, SymbolName(parameterSymbol)));
                        }
                    }
                    break;
            }
        }
    }

    private void CheckUnusedTypeParameters(NodeRef node, Action<NodeRef, bool, ProgramDiagnostic> addDiagnostic)
    {
        // Only report errors on the last declaration for the type parameter container;
        // this ensures that all uses have been accounted for.
        var symbol = GetSymbolOfDeclaration(node);
        var declarations = symbol.IsNone ? null : DeclarationsOf(symbol);
        if (declarations is not { } list || list[^1] != node)
        {
            return;
        }
        var store = StoreOf(node);
        var seenParentsWithEveryUnused = new HashSet<Node>();
        foreach (var typeParameterNode in GetEffectiveTypeParameterDeclarations(store, node.Node))
        {
            var typeParameter = At(node, typeParameterNode);
            if (!IsTypeParameterUnused(typeParameter))
            {
                continue;
            }
            var name = store.Texts.GetString(Utilities.IdText(store, store.Name(typeParameterNode)))!;
            var parent = store.Parent(typeParameterNode);
            var parentTypeParameters = store.TypeParameters(parent);
            var typeParameterNodes = store.Nodes(parentTypeParameters).ToArray();
            if (store.Kind(parent) != SyntaxKind.InferType && Array.TrueForAll(typeParameterNodes, t => IsTypeParameterUnused(At(node, t))))
            {
                if (seenParentsWithEveryUnused.Add(parent))
                {
                    var file = FileAt(node.File);
                    int pos, end;
                    if (store.Kind(parent) == SyntaxKind.JSDocTemplateTag)
                    {
                        // Whole @template tag (rangeOfNode)
                        pos = Scanner.SkipTrivia(store.SourceFileText(file.Root).Chars, store.Pos(parent));
                        end = store.End(parent);
                    }
                    else
                    {
                        // Include the `<>` in the error message (rangeOfTypeParameters)
                        pos = store.Pos(parentTypeParameters) - 1;
                        end = Scanner.SkipTrivia(store.SourceFileText(file.Root).Chars, store.End(parentTypeParameters)) + 1;
                    }
                    var only = typeParameterNodes.Length == 1;
                    var d = only
                        ? Utilities.CreateFileDiagnostic(store, file.Root, pos, end - pos, Diagnostics._0_is_declared_but_its_value_is_never_read, name)
                        : Utilities.CreateFileDiagnostic(store, file.Root, pos, end - pos, Diagnostics.All_type_parameters_are_unused);
                    addDiagnostic(typeParameter, true, ProgramDiagnostic.FromFile(file, d));
                }
            }
            else
            {
                addDiagnostic(typeParameter, true, CreateDiagnosticForNode(typeParameter, Diagnostics._0_is_declared_but_its_value_is_never_read, name));
            }
        }
    }

    private bool IsTypeParameterUnused(NodeRef typeParameter) =>
        (IsReferenced(GetMergedSymbol(NodeSymbol(typeParameter))) & SymbolFlags.TypeParameter) == 0 && !IsIdentifierThatStartsWithUnderscore(At(typeParameter, StoreOf(typeParameter).Name(typeParameter.Node)));

    // checker.ts isValidUnusedLocalDeclaration
    private bool IsValidUnusedLocalDeclaration(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var n = declaration.Node;
        if (store.Kind(n) == SyntaxKind.BindingElement)
        {
            if (store.Kind(store.Parent(n)) == SyntaxKind.ObjectBindingPattern)
            {
                // ignore starts with underscore names _
                // const { a: _a } = { a: 1 }
                return !store.PropertyName(n).IsNone && IsIdentifierThatStartsWithUnderscore(At(declaration, store.Name(n)));
            }
            return IsIdentifierThatStartsWithUnderscore(At(declaration, store.Name(n)));
        }
        return Utilities.IsAmbientModule(store, n) ||
            (((store.Kind(n) == SyntaxKind.VariableDeclaration && store.Kind(store.Parent(store.Parent(n))) is SyntaxKind.ForInStatement or SyntaxKind.ForOfStatement) || IsImportedDeclaration(store, n))
                && IsIdentifierThatStartsWithUnderscore(At(declaration, store.Name(n))));
    }

    private static bool IsImportedDeclaration(NodeStore store, Node node) =>
        store.Kind(node) is SyntaxKind.ImportClause or SyntaxKind.ImportSpecifier or SyntaxKind.NamespaceImport;

    // checker.ts importClauseFromImported
    private static Node ImportClauseFromImported(NodeStore store, Node decl) => store.Kind(decl) switch
    {
        SyntaxKind.ImportClause => decl,
        SyntaxKind.NamespaceImport => store.Parent(decl),
        _ => store.Parent(store.Parent(decl)),
    };

    private void CheckUnusedLocalsAndParameters(NodeRef nodeWithLocals, Action<NodeRef, bool, ProgramDiagnostic> addDiagnostic)
    {
        var locals = LocalsOf(nodeWithLocals);
        if (locals is null)
        {
            return;
        }
        // Ideally we could use the ImportClause directly as a key, but must wait until we have full ES6 maps. So must store key along with value.
        var unusedImports = new List<(Node Key, List<NodeRef> Values)>();
        var unusedDestructures = new List<(Node Key, List<NodeRef> Values)>();
        var unusedVariables = new List<(Node Key, List<NodeRef> Values)>();
        static void AddToGroup(List<(Node Key, List<NodeRef> Values)> map, Node key, NodeRef value)
        {
            var index = map.FindIndex(e => e.Key == key);
            if (index >= 0)
            {
                map[index].Values.Add(value);
            }
            else
            {
                map.Add((key, [value]));
            }
        }
        for (var i = 0; i < locals.Count; i++)
        {
            var local = locals.ValueAt(i);
            var flags = SymbolFlagsOf(local);
            // If it's purely a type parameter, ignore, will be checked in `checkUnusedTypeParameters`.
            // If it's a type parameter merged with a parameter, check if the parameter-side is used.
            if ((flags & SymbolFlags.TypeParameter) != 0
                ? !((flags & SymbolFlags.Variable) != 0 && (IsReferenced(local) & SymbolFlags.Variable) == 0)
                : IsReferenced(local) != 0 || !ExportSymbolOf(local).IsNone)
            {
                continue;
            }
            foreach (var declaration in DeclarationsOf(local) ?? [])
            {
                if (IsValidUnusedLocalDeclaration(declaration))
                {
                    continue;
                }
                var store = StoreOf(declaration);
                var d = declaration.Node;
                if (IsImportedDeclaration(store, d))
                {
                    AddToGroup(unusedImports, ImportClauseFromImported(store, d), declaration);
                }
                else if (store.Kind(d) == SyntaxKind.BindingElement && store.Kind(store.Parent(d)) == SyntaxKind.ObjectBindingPattern)
                {
                    // In `{ a, ...b }, `a` is considered used since it removes a property from `b`. `b` may still be unused though.
                    var elements = store.Nodes(store.Elements(store.Parent(d)));
                    var lastElement = elements[^1];
                    if (d == lastElement || store.DotDotDotToken(lastElement).IsNone)
                    {
                        AddToGroup(unusedDestructures, store.Parent(d), declaration);
                    }
                }
                else if (store.Kind(d) == SyntaxKind.VariableDeclaration)
                {
                    var blockScopeKind = Utilities.GetCombinedNodeFlags(store, d) & NodeFlags.BlockScoped;
                    var name = Utilities.GetNameOfDeclaration(store, d);
                    if ((blockScopeKind != NodeFlags.Using && blockScopeKind != NodeFlags.AwaitUsing) || name.IsNone || !IsIdentifierThatStartsWithUnderscore(At(declaration, name)))
                    {
                        AddToGroup(unusedVariables, store.Parent(d), declaration);
                    }
                }
                else
                {
                    var valueDeclaration = ValueDeclarationOf(local);
                    var parameter = NodeRef.None;
                    if (!valueDeclaration.IsNone)
                    {
                        var vs = StoreOf(valueDeclaration);
                        var root = Utilities.GetRootDeclaration(vs, valueDeclaration.Node);
                        if (vs.Kind(root) == SyntaxKind.Parameter)
                        {
                            parameter = At(valueDeclaration, root);
                        }
                    }
                    var nameNode = valueDeclaration.IsNone ? NodeRef.None : GetNameOfDeclarationRef(valueDeclaration);
                    if (!parameter.IsNone && !nameNode.IsNone)
                    {
                        var ps = StoreOf(parameter);
                        if (!Utilities.IsParameterPropertyDeclaration(ps, parameter.Node, ps.Parent(parameter.Node)) && !ParameterIsThisKeyword(ps, parameter.Node) && !IsIdentifierThatStartsWithUnderscore(nameNode))
                        {
                            if (store.Kind(d) == SyntaxKind.BindingElement && store.Kind(store.Parent(d)) == SyntaxKind.ArrayBindingPattern)
                            {
                                AddToGroup(unusedDestructures, store.Parent(d), declaration);
                            }
                            else
                            {
                                addDiagnostic(parameter, true, CreateDiagnosticForNode(nameNode, Diagnostics._0_is_declared_but_its_value_is_never_read, SymbolName(local)));
                            }
                        }
                    }
                    else
                    {
                        ErrorUnusedLocal(declaration, SymbolName(local), addDiagnostic);
                    }
                }
            }
        }
        foreach (var (importClause, unuseds) in unusedImports)
        {
            var store = StoreOf(nodeWithLocals);
            var importDecl = At(nodeWithLocals, store.Parent(importClause));
            var namedBindings = store.NamedBindings(importClause);
            var nDeclarations = (!store.Name(importClause).IsNone ? 1 : 0) +
                (!namedBindings.IsNone ? (store.Kind(namedBindings) == SyntaxKind.NamespaceImport ? 1 : store.Length(store.Elements(namedBindings))) : 0);
            if (nDeclarations == unuseds.Count)
            {
                addDiagnostic(importDecl, false, unuseds.Count == 1
                    ? CreateDiagnosticForNode(importDecl, Diagnostics._0_is_declared_but_its_value_is_never_read, store.Texts.GetString(Utilities.IdText(store, store.Name(unuseds[0].Node))))
                    : CreateDiagnosticForNode(importDecl, Diagnostics.All_imports_in_import_declaration_are_unused));
            }
            else
            {
                foreach (var unused in unuseds)
                {
                    ErrorUnusedLocal(unused, store.Texts.GetString(Utilities.IdText(store, store.Name(unused.Node)))!, addDiagnostic);
                }
            }
        }
        foreach (var (bindingPattern, bindingElements) in unusedDestructures)
        {
            var store = StoreOf(nodeWithLocals);
            var patternParent = store.Parent(bindingPattern);
            var isParameter = store.Kind(Utilities.GetRootDeclaration(store, patternParent)) == SyntaxKind.Parameter;
            if (store.Length(store.Elements(bindingPattern)) == bindingElements.Count)
            {
                if (bindingElements.Count == 1 && store.Kind(patternParent) == SyntaxKind.VariableDeclaration && store.Kind(store.Parent(patternParent)) == SyntaxKind.VariableDeclarationList)
                {
                    AddToGroup(unusedVariables, store.Parent(patternParent), At(nodeWithLocals, patternParent));
                }
                else
                {
                    addDiagnostic(At(nodeWithLocals, bindingPattern), isParameter, bindingElements.Count == 1
                        ? CreateDiagnosticForNode(At(nodeWithLocals, bindingPattern), Diagnostics._0_is_declared_but_its_value_is_never_read, BindingNameText(At(nodeWithLocals, store.Name(bindingElements[0].Node))))
                        : CreateDiagnosticForNode(At(nodeWithLocals, bindingPattern), Diagnostics.All_destructured_elements_are_unused));
                }
            }
            else
            {
                foreach (var e in bindingElements)
                {
                    addDiagnostic(e, isParameter, CreateDiagnosticForNode(e, Diagnostics._0_is_declared_but_its_value_is_never_read, BindingNameText(At(e, store.Name(e.Node)))));
                }
            }
        }
        foreach (var (declarationList, declarations) in unusedVariables)
        {
            var store = StoreOf(nodeWithLocals);
            if (store.Length(store.Declarations(declarationList)) == declarations.Count)
            {
                var listParent = store.Parent(declarationList);
                addDiagnostic(At(nodeWithLocals, declarationList), false, declarations.Count == 1
                    ? CreateDiagnosticForNode(At(nodeWithLocals, store.Name(declarations[0].Node)), Diagnostics._0_is_declared_but_its_value_is_never_read, BindingNameText(At(nodeWithLocals, store.Name(declarations[0].Node))))
                    : CreateDiagnosticForNode(At(nodeWithLocals, store.Kind(listParent) == SyntaxKind.VariableStatement ? listParent : declarationList), Diagnostics.All_variables_are_unused));
            }
            else
            {
                foreach (var decl in declarations)
                {
                    addDiagnostic(decl, false, CreateDiagnosticForNode(decl, Diagnostics._0_is_declared_but_its_value_is_never_read, BindingNameText(At(decl, store.Name(decl.Node)))));
                }
            }
        }
    }

    // utilities.ts parameterIsThisKeyword
    private static bool ParameterIsThisKeyword(NodeStore store, Node parameter) => Utilities.IsThisIdentifier(store, store.Name(parameter));

    // checker.ts bindingNameText
    private string BindingNameText(NodeRef name)
    {
        var store = StoreOf(name);
        switch (store.Kind(name.Node))
        {
            case SyntaxKind.Identifier:
                return store.Texts.GetString(Utilities.IdText(store, name.Node))!;
            case SyntaxKind.ArrayBindingPattern:
            case SyntaxKind.ObjectBindingPattern:
            {
                var first = store.Element(store.Elements(name.Node), 0);
                return BindingNameText(At(name, store.Name(first)));
            }
            default:
                return "";
        }
    }
}
