// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts, src/compiler/utilitiesPublic.ts and src/services/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/utilities.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C1: the syntactic utilities the checker core and the checker dump's
// type walker need (isExpressionNode, isPartOfTypeNode, isDeclarationName, getThisContainer, ...), read-only on shared stores.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>services/types.ts SemanticMeaning.</summary>
[Flags]
internal enum SemanticMeaning
{
    None = 0,
    Value = 1,
    Type = 2,
    Namespace = 4,
    All = Value | Type | Namespace,
}

internal static partial class Utilities
{
    /// <summary>utilities.ts isExpressionNode.</summary>
    internal static bool IsExpressionNode(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.SuperKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.RegularExpressionLiteral:
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.CallExpression:
            case SyntaxKind.NewExpression:
            case SyntaxKind.TaggedTemplateExpression:
            case SyntaxKind.AsExpression:
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.SatisfiesExpression:
            case SyntaxKind.NonNullExpression:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.VoidExpression:
            case SyntaxKind.DeleteExpression:
            case SyntaxKind.TypeOfExpression:
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
            case SyntaxKind.BinaryExpression:
            case SyntaxKind.ConditionalExpression:
            case SyntaxKind.SpreadElement:
            case SyntaxKind.TemplateExpression:
            case SyntaxKind.OmittedExpression:
            case SyntaxKind.JsxElement:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxFragment:
            case SyntaxKind.YieldExpression:
            case SyntaxKind.AwaitExpression:
                return true;
            case SyntaxKind.MetaProperty:
            {
                // `import.defer` in `import.defer(...)` is not an expression
                var parent = store.Parent(node);
                return !IsImportCall(store, parent) || store.Expression(parent) != node;
            }
            case SyntaxKind.ExpressionWithTypeArguments:
            {
                var parentKind = store.Kind(store.Parent(node));
                return parentKind != SyntaxKind.HeritageClause && parentKind != SyntaxKind.JSDocAugmentsTag;
            }
            case SyntaxKind.QualifiedName:
            case SyntaxKind.JSDocMemberName:
            {
                var kind = store.Kind(node);
                while (store.Kind(store.Parent(node)) == kind)
                {
                    node = store.Parent(node);
                }
                return IsTypeQueryOrJSDocReference(store, store.Parent(node)) || IsJsxTagName(store, node);
            }
            case SyntaxKind.PrivateIdentifier:
            {
                var parent = store.Parent(node);
                return store.Kind(parent) == SyntaxKind.BinaryExpression && store.Left(parent) == node && store.Kind(store.OperatorToken(parent)) == SyntaxKind.InKeyword;
            }
            case SyntaxKind.Identifier:
                if (IsTypeQueryOrJSDocReference(store, store.Parent(node)) || IsJsxTagName(store, node))
                {
                    return true;
                }
                return IsInExpressionContext(store, node);
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.ThisKeyword:
                return IsInExpressionContext(store, node);
            default:
                return false;
        }
    }

    // node.parent.kind === TypeQuery || isJSDocLinkLike(parent) || isJSDocNameReference(parent) || isJSDocMemberName(parent)
    private static bool IsTypeQueryOrJSDocReference(NodeStore store, Node parent) => store.Kind(parent) is SyntaxKind.TypeQuery
        or SyntaxKind.JSDocLink or SyntaxKind.JSDocLinkCode or SyntaxKind.JSDocLinkPlain or SyntaxKind.JSDocNameReference or SyntaxKind.JSDocMemberName;

    /// <summary>utilities.ts isInExpressionContext.</summary>
    internal static bool IsInExpressionContext(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        switch (store.Kind(parent))
        {
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.Parameter:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.EnumMember:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.BindingElement:
                return store.Initializer(parent) == node;
            case SyntaxKind.ExpressionStatement:
            case SyntaxKind.IfStatement:
            case SyntaxKind.DoStatement:
            case SyntaxKind.WhileStatement:
            case SyntaxKind.ReturnStatement:
            case SyntaxKind.WithStatement:
            case SyntaxKind.SwitchStatement:
            case SyntaxKind.CaseClause:
            case SyntaxKind.ThrowStatement:
                return store.Expression(parent) == node;
            case SyntaxKind.ForStatement:
            {
                var initializer = store.Initializer(parent);
                return (initializer == node && store.Kind(initializer) != SyntaxKind.VariableDeclarationList) ||
                    store.Condition(parent) == node ||
                    store.Incrementor(parent) == node;
            }
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
            {
                var initializer = store.Initializer(parent);
                return (initializer == node && store.Kind(initializer) != SyntaxKind.VariableDeclarationList) ||
                    store.Expression(parent) == node;
            }
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.AsExpression:
                return node == store.Expression(parent);
            case SyntaxKind.TemplateSpan:
                return node == store.Expression(parent);
            case SyntaxKind.ComputedPropertyName:
                return node == store.Expression(parent);
            case SyntaxKind.Decorator:
            case SyntaxKind.JsxExpression:
            case SyntaxKind.JsxSpreadAttribute:
            case SyntaxKind.SpreadAssignment:
                return true;
            case SyntaxKind.ExpressionWithTypeArguments:
                return store.Expression(parent) == node && !IsPartOfTypeNode(store, parent);
            case SyntaxKind.ShorthandPropertyAssignment:
                return store.ObjectAssignmentInitializer(parent) == node;
            case SyntaxKind.SatisfiesExpression:
                return node == store.Expression(parent);
            default:
                return IsExpressionNode(store, parent);
        }
    }

    /// <summary>utilities.ts isPartOfTypeNode.</summary>
    internal static bool IsPartOfTypeNode(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        if (kind >= SyntaxKind.FirstTypeNode && kind <= SyntaxKind.LastTypeNode)
        {
            return true;
        }
        switch (kind)
        {
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.UnknownKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.BigIntKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.ObjectKeyword:
            case SyntaxKind.UndefinedKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.NeverKeyword:
                return true;
            case SyntaxKind.VoidKeyword:
                return store.Kind(store.Parent(node)) != SyntaxKind.VoidExpression;
            case SyntaxKind.ExpressionWithTypeArguments:
                return IsPartOfTypeExpressionWithTypeArguments(store, node);
            case SyntaxKind.TypeParameter:
                return store.Kind(store.Parent(node)) is SyntaxKind.MappedType or SyntaxKind.InferType;
            case SyntaxKind.Identifier:
            case SyntaxKind.QualifiedName:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ThisKeyword:
            {
                if (kind == SyntaxKind.Identifier)
                {
                    var p = store.Parent(node);
                    if (store.Kind(p) == SyntaxKind.QualifiedName && store.Right(p) == node)
                    {
                        node = p;
                    }
                    else if (store.Kind(p) == SyntaxKind.PropertyAccessExpression && store.Name(p) == node)
                    {
                        node = p;
                    }
                }
                var parent = store.Parent(node);
                var parentKind = store.Kind(parent);
                if (parentKind == SyntaxKind.TypeQuery)
                {
                    return false;
                }
                if (parentKind == SyntaxKind.ImportType)
                {
                    return !store.IsTypeOf(parent);
                }
                if (parentKind >= SyntaxKind.FirstTypeNode && parentKind <= SyntaxKind.LastTypeNode)
                {
                    return true;
                }
                switch (parentKind)
                {
                    case SyntaxKind.ExpressionWithTypeArguments:
                        return IsPartOfTypeExpressionWithTypeArguments(store, parent);
                    case SyntaxKind.TypeParameter:
                        return node == store.Constraint(parent);
                    case SyntaxKind.JSDocTemplateTag:
                        return node == store.Constraint(parent);
                    case SyntaxKind.PropertyDeclaration:
                    case SyntaxKind.PropertySignature:
                    case SyntaxKind.Parameter:
                    case SyntaxKind.VariableDeclaration:
                        return node == store.Type(parent);
                    case SyntaxKind.FunctionDeclaration:
                    case SyntaxKind.FunctionExpression:
                    case SyntaxKind.ArrowFunction:
                    case SyntaxKind.Constructor:
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.MethodSignature:
                    case SyntaxKind.GetAccessor:
                    case SyntaxKind.SetAccessor:
                        return node == store.Type(parent);
                    case SyntaxKind.CallSignature:
                    case SyntaxKind.ConstructSignature:
                    case SyntaxKind.IndexSignature:
                        return node == store.Type(parent);
                    case SyntaxKind.TypeAssertionExpression:
                        return node == store.Type(parent);
                    case SyntaxKind.CallExpression:
                    case SyntaxKind.NewExpression:
                    case SyntaxKind.TaggedTemplateExpression:
                    {
                        var typeArguments = store.TypeArguments(parent);
                        return !typeArguments.IsNone && store.Nodes(typeArguments).Contains(node);
                    }
                }
                return false;
            }
        }
        return false;
    }

    private static bool IsPartOfTypeExpressionWithTypeArguments(NodeStore store, Node node)
    {
        var parentKind = store.Kind(store.Parent(node));
        return parentKind is SyntaxKind.JSDocImplementsTag or SyntaxKind.JSDocAugmentsTag
            || (parentKind == SyntaxKind.HeritageClause && !IsExpressionWithTypeArgumentsInClassExtendsClause(store, node));
    }

    /// <summary>utilities.ts isExpressionWithTypeArgumentsInClassExtendsClause (tryGetClassExtendingExpressionWithTypeArguments).</summary>
    internal static bool IsExpressionWithTypeArgumentsInClassExtendsClause(NodeStore store, Node node)
    {
        if (node.IsNone || store.Kind(node) != SyntaxKind.ExpressionWithTypeArguments)
        {
            return false;
        }
        var parent = store.Parent(node);
        return store.Kind(parent) == SyntaxKind.HeritageClause && store.Token(parent) == SyntaxKind.ExtendsKeyword && IsClassLike(store, store.Parent(parent));
    }

    /// <summary>utilities.ts isDeclarationName.</summary>
    internal static bool IsDeclarationName(NodeStore store, Node name)
    {
        var kind = store.Kind(name);
        if (kind == SyntaxKind.SourceFile || kind is SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern)
        {
            return false;
        }
        var parent = store.Parent(name);
        return !parent.IsNone && IsDeclaration(store, parent) && DeclarationNameField(store, parent) == name;
    }

    // `declaration.name` (the node's name member; undefined for declarations without one).
    internal static Node DeclarationNameField(NodeStore store, Node declaration) => store.Kind(declaration) switch
    {
        SyntaxKind.Constructor or SyntaxKind.ClassStaticBlockDeclaration or SyntaxKind.ArrowFunction => Node.None,
        _ => store.Name(declaration),
    };

    /// <summary>utilities.ts isJSXTagName.</summary>
    internal static bool IsJsxTagName(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        return store.Kind(parent) is SyntaxKind.JsxOpeningElement or SyntaxKind.JsxSelfClosingElement or SyntaxKind.JsxClosingElement
            && store.TagName(parent) == node;
    }

    /// <summary>utilitiesPublic.ts isPropertyAccessOrQualifiedName.</summary>
    internal static bool IsPropertyAccessOrQualifiedName(NodeStore store, Node node) =>
        store.Kind(node) is SyntaxKind.PropertyAccessExpression or SyntaxKind.QualifiedName;

    /// <summary>services/utilities.ts isLabelName (isLabelOfLabeledStatement || isJumpStatementTarget).</summary>
    internal static bool IsLabelName(NodeStore store, Node node)
    {
        if (store.Kind(node) != SyntaxKind.Identifier)
        {
            return false;
        }
        var parent = store.Parent(node);
        return store.Kind(parent) switch
        {
            SyntaxKind.LabeledStatement => store.Label(parent) == node,
            SyntaxKind.BreakStatement or SyntaxKind.ContinueStatement => store.Label(parent) == node,
            _ => false,
        };
    }

    /// <summary>utilities.ts isImportCall.</summary>
    internal static bool IsImportCall(NodeStore store, Node node) =>
        !node.IsNone && store.Kind(node) == SyntaxKind.CallExpression && store.Kind(store.Expression(node)) == SyntaxKind.ImportKeyword;

    /// <summary>utilities.ts isIntrinsicJsxName (lower-case first letter or a dash).</summary>
    internal static bool IsIntrinsicJsxName(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }
        var ch = name[0];
        return (ch >= 'a' && ch <= 'z') || name.Contains('-', StringComparison.Ordinal);
    }

    /// <summary>
    /// services/utilities.ts getMeaningFromDeclaration (the type walker's filter for identifiers).
    /// </summary>
    internal static SemanticMeaning GetMeaningFromDeclaration(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.VariableDeclaration:
                return SemanticMeaning.Value; // (JSDoc @enum on JavaScript variables: module C9)
            case SyntaxKind.Parameter:
            case SyntaxKind.BindingElement:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.ShorthandPropertyAssignment:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.Constructor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.CatchClause:
            case SyntaxKind.JsxAttribute:
                return SemanticMeaning.Value;
            case SyntaxKind.TypeParameter:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.TypeLiteral:
                return SemanticMeaning.Type;
            case SyntaxKind.JSDocTypedefTag:
                return store.Name(node).IsNone ? SemanticMeaning.Value | SemanticMeaning.Type : SemanticMeaning.Type;
            case SyntaxKind.EnumMember:
            case SyntaxKind.ClassDeclaration:
                return SemanticMeaning.Value | SemanticMeaning.Type;
            case SyntaxKind.ModuleDeclaration:
                if (IsAmbientModule(store, node))
                {
                    return SemanticMeaning.Namespace | SemanticMeaning.Value;
                }
                return GetModuleInstanceState(store, node) == ModuleInstanceState.Instantiated
                    ? SemanticMeaning.Namespace | SemanticMeaning.Value
                    : SemanticMeaning.Namespace;
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.NamedImports:
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ExportAssignment:
            case SyntaxKind.ExportDeclaration:
                return SemanticMeaning.All;
            case SyntaxKind.SourceFile:
                return SemanticMeaning.Namespace | SemanticMeaning.Value;
        }
        return SemanticMeaning.All;
    }

    /// <summary>
    /// binder.ts getModuleInstanceState, read-only (the bound tree has its parents; the binder's own copy may set
    /// parents first, which a checker must not do on shared files).
    /// </summary>
    internal static ModuleInstanceState GetModuleInstanceState(NodeStore store, Node node)
    {
        var visited = new Dictionary<int, ModuleInstanceState?>();
        return ModuleInstanceStateOf(store, node, visited);
    }

    private static ModuleInstanceState ModuleInstanceStateOf(NodeStore store, Node node, Dictionary<int, ModuleInstanceState?> visited)
    {
        var body = store.Body(node);
        return !body.IsNone ? ModuleInstanceStateCached(store, body, visited) : ModuleInstanceState.Instantiated;
    }

    private static ModuleInstanceState ModuleInstanceStateCached(NodeStore store, Node node, Dictionary<int, ModuleInstanceState?> visited)
    {
        if (visited.TryGetValue(node.Index, out var cached))
        {
            return cached ?? ModuleInstanceState.NonInstantiated;
        }
        visited[node.Index] = null;
        var result = ModuleInstanceStateWorker(store, node, visited);
        visited[node.Index] = result;
        return result;
    }

    private static ModuleInstanceState ModuleInstanceStateWorker(NodeStore store, Node node, Dictionary<int, ModuleInstanceState?> visited)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
                return ModuleInstanceState.NonInstantiated;
            case SyntaxKind.EnumDeclaration:
                if (IsEnumConst(store, node))
                {
                    return ModuleInstanceState.ConstEnumOnly;
                }
                break;
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
                if (!HasSyntacticModifierUncached(store, node, ModifierFlags.Export))
                {
                    return ModuleInstanceState.NonInstantiated;
                }
                break;
            case SyntaxKind.ExportDeclaration:
            {
                var exportClause = store.ExportClause(node);
                if (store.ModuleSpecifier(node).IsNone && !exportClause.IsNone && store.Kind(exportClause) == SyntaxKind.NamedExports)
                {
                    var state = ModuleInstanceState.NonInstantiated;
                    foreach (var specifier in store.Nodes(store.Elements(exportClause)))
                    {
                        var specifierState = ModuleInstanceStateForAliasTarget(store, specifier, visited);
                        if (specifierState > state)
                        {
                            state = specifierState;
                        }
                        if (state == ModuleInstanceState.Instantiated)
                        {
                            return state;
                        }
                    }
                    return state;
                }
                break;
            }
            case SyntaxKind.ModuleBlock:
            {
                var state = ModuleInstanceState.NonInstantiated;
                foreach (var n in store.Nodes(store.Statements(node)))
                {
                    var childState = ModuleInstanceStateCached(store, n, visited);
                    if (childState == ModuleInstanceState.NonInstantiated)
                    {
                        continue;
                    }
                    if (childState == ModuleInstanceState.ConstEnumOnly)
                    {
                        state = ModuleInstanceState.ConstEnumOnly;
                        continue;
                    }
                    state = ModuleInstanceState.Instantiated;
                    break;
                }
                return state;
            }
            case SyntaxKind.ModuleDeclaration:
                return ModuleInstanceStateOf(store, node, visited);
            case SyntaxKind.Identifier:
                if ((store.Flags(node) & NodeFlags.IdentifierIsInJSDocNamespace) != 0)
                {
                    return ModuleInstanceState.NonInstantiated;
                }
                break;
        }
        return ModuleInstanceState.Instantiated;
    }

    private static ModuleInstanceState ModuleInstanceStateForAliasTarget(NodeStore store, Node specifier, Dictionary<int, ModuleInstanceState?> visited)
    {
        var name = !store.PropertyName(specifier).IsNone ? store.PropertyName(specifier) : store.Name(specifier);
        if (store.Kind(name) != SyntaxKind.Identifier)
        {
            return ModuleInstanceState.Instantiated;
        }
        for (var p = store.Parent(specifier); !p.IsNone; p = store.Parent(p))
        {
            var kind = store.Kind(p);
            if (kind == SyntaxKind.Block || kind == SyntaxKind.ModuleBlock || kind == SyntaxKind.SourceFile)
            {
                ModuleInstanceState? found = null;
                foreach (var statement in store.Nodes(store.Statements(p)))
                {
                    if (NodeHasName(store, statement, name))
                    {
                        var state = ModuleInstanceStateCached(store, statement, visited);
                        if (found is null || state > found)
                        {
                            found = state;
                        }
                        if (found == ModuleInstanceState.Instantiated)
                        {
                            return found.Value;
                        }
                    }
                }
                if (found is not null)
                {
                    return found.Value;
                }
                break;
            }
        }
        return ModuleInstanceState.Instantiated;
    }

    /// <summary>utilities.ts isExternalOrCommonJsModule.</summary>
    internal static bool IsExternalOrCommonJsModule(NodeStore store, Node file, BinderStore? binding) =>
        IsExternalModule(store, file) || (binding is not null && !binding.CommonJsModuleIndicator.IsNone);

    /// <summary>
    /// utilities.ts getThisContainer(node, includeArrowFunctions, includeClassComputedPropertyName).
    /// </summary>
    internal static Node GetThisContainer(NodeStore store, Node node, bool includeArrowFunctions, bool includeClassComputedPropertyName)
    {
        while (true)
        {
            node = store.Parent(node);
            if (node.IsNone)
            {
                return node;
            }
            switch (store.Kind(node))
            {
                case SyntaxKind.ComputedPropertyName:
                    if (includeClassComputedPropertyName && IsClassLike(store, store.Parent(store.Parent(node))))
                    {
                        return node;
                    }
                    node = store.Parent(store.Parent(node));
                    break;
                case SyntaxKind.Decorator:
                {
                    var parent = store.Parent(node);
                    if (store.Kind(parent) == SyntaxKind.Parameter && IsClassElement(store, store.Parent(parent)))
                    {
                        node = store.Parent(parent);
                    }
                    else if (IsClassElement(store, parent))
                    {
                        node = parent;
                    }
                    break;
                }
                case SyntaxKind.ArrowFunction:
                    if (!includeArrowFunctions)
                    {
                        continue;
                    }
                    return node;
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.ClassStaticBlockDeclaration:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.Constructor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.CallSignature:
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.SourceFile:
                    return node;
            }
        }
    }

    /// <summary>utilities.ts isInTypeQuery.</summary>
    internal static bool IsInTypeQuery(NodeStore store, Node node)
    {
        // TypeScript 1.0 spec (April 2014): 3.6.3
        // A type query consists of the keyword typeof followed by an expression.
        // The expression is restricted to a single identifier or a sequence of identifiers separated by periods
        for (var n = node; !n.IsNone; n = store.Parent(n))
        {
            var kind = store.Kind(n);
            if (kind == SyntaxKind.TypeQuery)
            {
                return true;
            }
            if (kind is not (SyntaxKind.Identifier or SyntaxKind.QualifiedName))
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>checker.ts isTypeReferenceIdentifier.</summary>
    internal static bool IsTypeReferenceIdentifier(NodeStore store, Node node)
    {
        while (store.Kind(store.Parent(node)) == SyntaxKind.QualifiedName)
        {
            node = store.Parent(node);
        }
        return store.Kind(store.Parent(node)) == SyntaxKind.TypeReference;
    }

    /// <summary>utilities.ts isRightSideOfQualifiedNameOrPropertyAccess.</summary>
    internal static bool IsRightSideOfQualifiedNameOrPropertyAccess(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        return store.Kind(parent) switch
        {
            SyntaxKind.QualifiedName => store.Right(parent) == node,
            SyntaxKind.PropertyAccessExpression => store.Name(parent) == node,
            SyntaxKind.MetaProperty => store.Name(parent) == node,
            _ => false,
        };
    }

    /// <summary>utilities.ts getExternalModuleName.</summary>
    internal static Node GetExternalModuleName(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ExportDeclaration:
            case SyntaxKind.JSDocImportTag:
                return store.ModuleSpecifier(node);
            case SyntaxKind.ImportEqualsDeclaration:
            {
                var reference = store.ModuleReference(node);
                return store.Kind(reference) == SyntaxKind.ExternalModuleReference ? store.Expression(reference) : Node.None;
            }
            case SyntaxKind.ImportType:
            {
                var argument = store.Argument(node);
                return store.Kind(argument) == SyntaxKind.LiteralType ? store.Literal(argument) : Node.None;
            }
            case SyntaxKind.CallExpression:
            {
                var arguments = store.Arguments(node);
                return arguments.IsNone || store.Length(arguments) == 0 ? Node.None : store.Element(arguments, 0);
            }
            case SyntaxKind.ModuleDeclaration:
            {
                var name = store.Name(node);
                return store.Kind(name) == SyntaxKind.StringLiteral ? name : Node.None;
            }
        }
        return Node.None;
    }

    /// <summary>utilities.ts isInternalModuleImportEqualsDeclaration.</summary>
    internal static bool IsInternalModuleImportEqualsDeclaration(NodeStore store, Node node) =>
        store.Kind(node) == SyntaxKind.ImportEqualsDeclaration && store.Kind(store.ModuleReference(node)) != SyntaxKind.ExternalModuleReference;

    /// <summary>utilities.ts isExternalModuleImportEqualsDeclaration.</summary>
    internal static bool IsExternalModuleImportEqualsDeclaration(NodeStore store, Node node) =>
        store.Kind(node) == SyntaxKind.ImportEqualsDeclaration && store.Kind(store.ModuleReference(node)) == SyntaxKind.ExternalModuleReference;

    /// <summary>utilities.ts getFirstIdentifier.</summary>
    internal static Node GetFirstIdentifier(NodeStore store, Node node)
    {
        while (true)
        {
            switch (store.Kind(node))
            {
                case SyntaxKind.QualifiedName:
                    node = store.Left(node);
                    continue;
                case SyntaxKind.PropertyAccessExpression:
                    node = store.Expression(node);
                    continue;
                default:
                    return node;
            }
        }
    }

    /// <summary>utilities.ts isClassElement.</summary>
    internal static bool IsClassElementKind(SyntaxKind kind) => kind is SyntaxKind.Constructor or SyntaxKind.PropertyDeclaration
        or SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor or SyntaxKind.IndexSignature
        or SyntaxKind.ClassStaticBlockDeclaration or SyntaxKind.SemicolonClassElement;

    /// <summary>utilities.ts isModuleOrEnumDeclaration.</summary>
    internal static bool IsModuleOrEnumDeclaration(NodeStore store, Node node) =>
        store.Kind(node) is SyntaxKind.ModuleDeclaration or SyntaxKind.EnumDeclaration;

    /// <summary>utilities.ts isConstAssertion (`as const` / `&lt;const&gt;`).</summary>
    internal static bool IsConstAssertion(NodeStore store, Node location)
    {
        var kind = store.Kind(location);
        if (kind is not (SyntaxKind.AsExpression or SyntaxKind.TypeAssertionExpression))
        {
            return false;
        }
        var type = store.Type(location);
        return store.Kind(type) == SyntaxKind.TypeReference && store.Kind(store.TypeName(type)) == SyntaxKind.Identifier
            && store.Texts.Equals(store.EscapedText(store.TypeName(type)), "const");
    }

    /// <summary>utilities.ts isWriteOnlyAccess / accessKind: whether the reference is only written.</summary>
    internal static bool IsWriteOnlyAccess(NodeStore store, Node node) => AccessKindOf(store, node) == 1;

    /// <summary>utilities.ts isWriteAccess.</summary>
    internal static bool IsWriteAccess(NodeStore store, Node node) => AccessKindOf(store, node) != 0;

    // utilities.ts accessKind: 0 read, 1 write, 2 read-write.
    private static int AccessKindOf(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        switch (store.Kind(parent))
        {
            case SyntaxKind.ParenthesizedExpression:
                return AccessKindOf(store, parent);
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
            {
                var op = store.Operator(parent);
                return op is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken ? 2 : 0;
            }
            case SyntaxKind.BinaryExpression:
            {
                var left = store.Left(parent);
                var op = store.Kind(store.OperatorToken(parent));
                return left == node && IsAssignmentOperator(op)
                    ? op == SyntaxKind.EqualsToken ? 1 : 2
                    : 0;
            }
            case SyntaxKind.PropertyAccessExpression:
                return store.Name(parent) != node ? 0 : AccessKindOf(store, parent);
            case SyntaxKind.PropertyAssignment:
            {
                var parentAccess = AccessKindOf(store, store.Parent(parent));
                // In `({ x: varname }) = { x: 1 }`, the left `x` is a read, the right `x` is a write.
                return node == store.Name(parent) ? ReverseAccessKind(parentAccess) : parentAccess;
            }
            case SyntaxKind.ShorthandPropertyAssignment:
                // Assume it's the local variable being accessed, since we don't check public properties for --noUnusedLocals.
                return node == store.ObjectAssignmentInitializer(parent) ? 0 : AccessKindOf(store, store.Parent(parent));
            case SyntaxKind.ArrayLiteralExpression:
                return AccessKindOf(store, parent);
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
                return node == store.Initializer(parent) ? 1 : 0;
            default:
                return 0;
        }
    }

    private static int ReverseAccessKind(int a) => a switch
    {
        0 => 1,
        1 => 0,
        _ => 2,
    };

    // utilitiesPublic.ts functions under the names the checker's port uses
    internal static string UnescapeLeadingUnderscores(string identifier) => UtilitiesPublic.UnescapeLeadingUnderscores(identifier);

    internal static string EscapeLeadingUnderscores(string identifier) => UtilitiesPublic.EscapeLeadingUnderscores(identifier);

    internal static TextRef IdText(NodeStore store, Node identifierOrPrivateName) => UtilitiesPublic.IdText(store, identifierOrPrivateName);

    internal static bool IsStringLiteralLike(NodeStore store, Node node) => UtilitiesPublic.IsStringLiteralLike(store, node);

    internal static bool IsBindingPattern(NodeStore store, Node node) => UtilitiesPublic.IsBindingPattern(store, node);

    internal static bool IsJSDocNode(NodeStore store, Node node) => UtilitiesPublic.IsJSDocNode(store, node);

    /// <summary>moduleNameResolver.ts isExternalModuleNameRelative (pathIsRelative || isRootedDiskPath).</summary>
    internal static bool IsExternalModuleNameRelative(string moduleName) =>
        // TypeScript 1.0 spec (April 2014): 11.2.1
        // An external module name is "relative" if the first term is "." or "..".
        // Update: We also consider a path like `C:\foo.ts` "relative" because we do not search for it in `node_modules` or treat it as an ambient module.
        PathIsRelative(moduleName) || TsPath.GetRootLength(moduleName) > 0;

    // path.ts pathIsRelative: /^\.\.?(?:$|[\\/])/
    private static bool PathIsRelative(string path)
    {
        if (path.Length == 0 || path[0] != '.')
        {
            return false;
        }
        var i = path.Length > 1 && path[1] == '.' ? 2 : 1;
        return i == path.Length || path[i] == '/' || path[i] == '\\';
    }

    /// <summary>utilitiesPublic.ts identifierToKeywordKind for an escaped name (null when it is no keyword).</summary>
    internal static SyntaxKind? IdentifierToKeywordKind(string text)
    {
        var token = Scanner.StringToToken(text);
        return token is { } kind && IsKeyword(kind) ? kind : null;
    }

    /// <summary>utilities.ts getContainingFunction (None if there is none).</summary>
    internal static Node GetContainingFunctionOrNone(NodeStore store, Node node)
    {
        for (var n = store.Parent(node); !n.IsNone; n = store.Parent(n))
        {
            if (IsFunctionLike(store, n))
            {
                return n;
            }
        }
        return Node.None;
    }

    /// <summary>utilities.ts getSourceFileOfNode, on a store: the root node.</summary>
    internal static Node RootOf(NodeStore store, Node node)
    {
        while (store.Kind(node) != SyntaxKind.SourceFile)
        {
            node = store.Parent(node);
        }
        return node;
    }
}
