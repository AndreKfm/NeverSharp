// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts and src/compiler/utilitiesPublic.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/utilities.go and internal/binder/binder.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; the utilities the binder needs (module B1), on Track O's node store.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>utilities.ts / utilitiesPublic.ts: the helpers of binder.ts that Track O did not have yet.</summary>
internal static partial class Utilities
{
    /// <summary>utilitiesPublic.ts: getNameOfDeclaration.</summary>
    internal static Node GetNameOfDeclaration(NodeStore store, Node declaration)
    {
        if (declaration.IsNone)
        {
            return default;
        }
        var name = GetNonAssignedNameOfDeclaration(store, declaration);
        if (!name.IsNone)
        {
            return name;
        }
        var kind = store.Kind(declaration);
        return kind == SyntaxKind.FunctionExpression || kind == SyntaxKind.ArrowFunction || kind == SyntaxKind.ClassExpression ? GetAssignedName(store, declaration) : default;
    }

    /// <summary>utilitiesPublic.ts: getNonAssignedNameOfDeclaration.</summary>
    internal static Node GetNonAssignedNameOfDeclaration(NodeStore store, Node declaration)
    {
        switch (store.Kind(declaration))
        {
            case SyntaxKind.Identifier:
                return declaration;
            case SyntaxKind.JSDocPropertyTag:
            case SyntaxKind.JSDocParameterTag:
            {
                var name = store.Name(declaration);
                if (store.Kind(name) == SyntaxKind.QualifiedName)
                {
                    return store.Right(name);
                }
                break;
            }
            case SyntaxKind.CallExpression:
            case SyntaxKind.BinaryExpression:
            {
                switch (GetAssignmentDeclarationKind(store, declaration))
                {
                    case AssignmentDeclarationKind.ExportsProperty:
                    case AssignmentDeclarationKind.ThisProperty:
                    case AssignmentDeclarationKind.Property:
                    case AssignmentDeclarationKind.PrototypeProperty:
                        return GetElementOrPropertyAccessArgumentExpressionOrName(store, store.Left(declaration));
                    case AssignmentDeclarationKind.ObjectDefinePropertyValue:
                    case AssignmentDeclarationKind.ObjectDefinePropertyExports:
                    case AssignmentDeclarationKind.ObjectDefinePrototypeProperty:
                        return store.Element(store.Arguments(declaration), 1);
                    default:
                        return default;
                }
            }
            case SyntaxKind.JSDocTypedefTag:
                // getNameOfJSDocTypedef: declaration.name || nameForNamelessJSDocTypedef(declaration).
                return !store.Name(declaration).IsNone ? store.Name(declaration) : NameForNamelessJSDocTypedef(store, declaration);
            case SyntaxKind.JSDocEnumTag:
                return NameForNamelessJSDocTypedef(store, declaration);
            case SyntaxKind.ExportAssignment:
            {
                var expression = store.Expression(declaration);
                return store.Kind(expression) == SyntaxKind.Identifier ? expression : default;
            }
            case SyntaxKind.ElementAccessExpression:
                if (IsBindableStaticElementAccessExpression(store, declaration))
                {
                    return store.ArgumentExpression(declaration);
                }
                break;
        }
        return store.Name(declaration);
    }

    /// <summary>utilitiesPublic.ts: nameForNamelessJSDocTypedef.</summary>
    private static Node NameForNamelessJSDocTypedef(NodeStore store, Node declaration)
    {
        var hostNode = store.Parent(store.Parent(declaration));
        if (hostNode.IsNone)
        {
            return default;
        }
        // Covers classes, functions - any named declaration host node
        if (IsDeclaration(store, hostNode))
        {
            return GetDeclarationIdentifier(store, hostNode);
        }
        // Covers remaining cases (returning undefined if none match).
        switch (store.Kind(hostNode))
        {
            case SyntaxKind.VariableStatement:
            {
                var list = store.DeclarationList(hostNode);
                if (!list.IsNone && store.Length(store.Declarations(list)) > 0)
                {
                    return GetDeclarationIdentifier(store, store.Element(store.Declarations(list), 0));
                }
                break;
            }
            case SyntaxKind.ExpressionStatement:
            {
                var expr = store.Expression(hostNode);
                if (store.Kind(expr) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(expr)) == SyntaxKind.EqualsToken)
                {
                    expr = store.Left(expr);
                }
                switch (store.Kind(expr))
                {
                    case SyntaxKind.PropertyAccessExpression:
                        return store.Name(expr);
                    case SyntaxKind.ElementAccessExpression:
                        var arg = store.ArgumentExpression(expr);
                        if (store.Kind(arg) == SyntaxKind.Identifier)
                        {
                            return arg;
                        }
                        break;
                }
                break;
            }
            case SyntaxKind.ParenthesizedExpression:
                return GetDeclarationIdentifier(store, store.Expression(hostNode));
            case SyntaxKind.LabeledStatement:
                if (IsDeclaration(store, store.Statement(hostNode)) || UtilitiesPublic.IsExpression(store, store.Statement(hostNode)))
                {
                    return GetDeclarationIdentifier(store, store.Statement(hostNode));
                }
                break;
        }
        return default;
    }

    /// <summary>utilitiesPublic.ts: getDeclarationIdentifier.</summary>
    private static Node GetDeclarationIdentifier(NodeStore store, Node node)
    {
        var name = GetNameOfDeclaration(store, node);
        return !name.IsNone && store.Kind(name) == SyntaxKind.Identifier ? name : default;
    }

    /// <summary>utilitiesPublic.ts: getAssignedName.</summary>
    internal static Node GetAssignedName(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        if (parent.IsNone)
        {
            return default;
        }
        var parentKind = store.Kind(parent);
        if (parentKind == SyntaxKind.PropertyAssignment || parentKind == SyntaxKind.BindingElement)
        {
            return store.Name(parent);
        }
        if (parentKind == SyntaxKind.BinaryExpression && node == store.Right(parent))
        {
            var left = store.Left(parent);
            if (store.Kind(left) == SyntaxKind.Identifier)
            {
                return left;
            }
            if (IsAccessExpression(store, left))
            {
                return GetElementOrPropertyAccessArgumentExpressionOrName(store, left);
            }
        }
        else if (parentKind == SyntaxKind.VariableDeclaration && store.Kind(store.Name(parent)) == SyntaxKind.Identifier)
        {
            return store.Name(parent);
        }
        return default;
    }

    /// <summary>utilitiesPublic.ts: isDeclaration.</summary>
    internal static bool IsDeclaration(NodeStore store, Node node)
    {
        if (store.Kind(node) == SyntaxKind.TypeParameter)
        {
            var parent = store.Parent(node);
            return (!parent.IsNone && store.Kind(parent) != SyntaxKind.JSDocTemplateTag) || IsInJSFile(store, node);
        }
        return IsDeclarationKind(store.Kind(node));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsDeclarationKind(SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.BindingElement:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.ClassStaticBlockDeclaration:
            case SyntaxKind.Constructor:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.EnumMember:
            case SyntaxKind.ExportSpecifier:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.ImportClause:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.JsxAttribute:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.NamespaceExportDeclaration:
            case SyntaxKind.NamespaceImport:
            case SyntaxKind.NamespaceExport:
            case SyntaxKind.Parameter:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.ShorthandPropertyAssignment:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.TypeParameter:
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.JSDocTypedefTag:
            case SyntaxKind.JSDocCallbackTag:
            case SyntaxKind.JSDocPropertyTag:
            case SyntaxKind.NamedTupleMember:
                return true;
            default:
                return false;
        }
    }

    /// <summary>utilities.ts: isAmbientModule.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAmbientModule(NodeStore store, Node node)
    {
        return store.Kind(node) == SyntaxKind.ModuleDeclaration && (store.Kind(store.Name(node)) == SyntaxKind.StringLiteral || IsGlobalScopeAugmentation(store, node));
    }

    /// <summary>utilities.ts: isGlobalScopeAugmentation.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsGlobalScopeAugmentation(NodeStore store, Node module)
    {
        return (store.Flags(module) & NodeFlags.GlobalAugmentation) != 0;
    }

    /// <summary>utilities.ts: isModuleAugmentationExternal.</summary>
    internal static bool IsModuleAugmentationExternal(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        switch (store.Kind(parent))
        {
            case SyntaxKind.SourceFile:
                return IsExternalModule(store, parent);
            case SyntaxKind.ModuleBlock:
                var grandParent = store.Parent(parent);
                return IsAmbientModule(store, grandParent) && store.Kind(store.Parent(grandParent)) == SyntaxKind.SourceFile && !IsExternalModule(store, store.Parent(grandParent));
        }
        return false;
    }

    /// <summary>parser.ts: isExternalModule (file.externalModuleIndicator !== undefined).</summary>
    internal static bool IsExternalModule(NodeStore store, Node file)
    {
        return store.ExternalModuleIndicator(file) is not null;
    }

    /// <summary>utilities.ts: getContainingClass.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetContainingClass(NodeStore store, Node node)
    {
        for (var n = store.Parent(node); !n.IsNone; n = store.Parent(n))
        {
            if (IsClassLike(store, n))
            {
                return n;
            }
        }
        return default;
    }

    /// <summary>utilitiesPublic.ts: isClassLike.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsClassLike(NodeStore store, Node node)
    {
        return !node.IsNone && (store.Kind(node) == SyntaxKind.ClassDeclaration || store.Kind(node) == SyntaxKind.ClassExpression);
    }

    /// <summary>utilitiesPublic.ts: isClassElement.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsClassElement(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        return kind == SyntaxKind.Constructor
            || kind == SyntaxKind.PropertyDeclaration
            || kind == SyntaxKind.MethodDeclaration
            || kind == SyntaxKind.GetAccessor
            || kind == SyntaxKind.SetAccessor
            || kind == SyntaxKind.IndexSignature
            || kind == SyntaxKind.ClassStaticBlockDeclaration
            || kind == SyntaxKind.SemicolonClassElement;
    }

    /// <summary>utilities.ts: isStatic.</summary>
    internal static bool IsStatic(NodeStore store, Node node)
    {
        // https://tc39.es/ecma262/#sec-static-semantics-isstatic
        return IsClassElement(store, node) && HasSyntacticModifier(store, node, ModifierFlags.Static) || store.Kind(node) == SyntaxKind.ClassStaticBlockDeclaration;
    }

    /// <summary>utilities.ts: isPropertyNameLiteral.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPropertyNameLiteral(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.NumericLiteral:
                return true;
            default:
                return false;
        }
    }

    /// <summary>utilities.ts: getEscapedTextOfIdentifierOrLiteral.</summary>
    internal static TextRef GetEscapedTextOfIdentifierOrLiteral(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        return kind == SyntaxKind.Identifier || kind == SyntaxKind.PrivateIdentifier ? store.EscapedText(node)
            : kind == SyntaxKind.JsxNamespacedName ? GetEscapedTextOfJsxNamespacedName(store, node)
            : UtilitiesPublic.EscapeLeadingUnderscores(store.Texts, store.Text(node));
    }

    /// <summary>utilities.ts: getTextOfIdentifierOrLiteral.</summary>
    internal static TextRef GetTextOfIdentifierOrLiteral(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        if (kind == SyntaxKind.Identifier || kind == SyntaxKind.PrivateIdentifier)
        {
            return UtilitiesPublic.IdText(store, node);
        }
        if (kind == SyntaxKind.JsxNamespacedName)
        {
            // `${idText(node.namespace)}:${idText(node.name)}`
            var builder = new TextBuilder(store.Texts);
            builder.Append(UtilitiesPublic.IdText(store, store.Namespace(node)));
            builder.Append(':');
            builder.Append(UtilitiesPublic.IdText(store, store.Name(node)));
            return builder.Result();
        }
        return store.Text(node);
    }

    /// <summary>utilities.ts: isJSDocConstructSignature.</summary>
    internal static bool IsJSDocConstructSignature(NodeStore store, Node node)
    {
        if (store.Kind(node) != SyntaxKind.JSDocFunctionType)
        {
            return false;
        }
        var parameters = store.Parameters(node);
        if (parameters.IsNone || store.Length(parameters) == 0)
        {
            return false;
        }
        var name = store.Name(store.Element(parameters, 0));
        return !name.IsNone && store.Kind(name) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(name), "new");
    }

    /// <summary>
    /// utilitiesPublic.ts: getCombinedModifierFlags. Track O: with the syntactic modifier flags; the JSDoc modifier
    /// flags getEffectiveModifierFlags adds in JavaScript files (accessibility, readonly, override, deprecated) are
    /// never among the flags the binder tests (Export, Default, Const).
    /// </summary>
    internal static ModifierFlags GetCombinedModifierFlags(NodeStore store, Node node)
    {
        if (store.Kind(node) == SyntaxKind.BindingElement)
        {
            node = WalkUpBindingElementsAndPatterns(store, node);
        }
        var flags = GetSyntacticModifierFlags(store, node);
        if (store.Kind(node) == SyntaxKind.VariableDeclaration)
        {
            node = store.Parent(node);
        }
        if (!node.IsNone && store.Kind(node) == SyntaxKind.VariableDeclarationList)
        {
            flags |= GetSyntacticModifierFlags(store, node);
            node = store.Parent(node);
        }
        if (!node.IsNone && store.Kind(node) == SyntaxKind.VariableStatement)
        {
            flags |= GetSyntacticModifierFlags(store, node);
        }
        return flags;
    }

    /// <summary>utilitiesPublic.ts: getCombinedNodeFlags.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static NodeFlags GetCombinedNodeFlags(NodeStore store, Node node)
    {
        if (store.Kind(node) == SyntaxKind.BindingElement)
        {
            node = WalkUpBindingElementsAndPatterns(store, node);
        }
        var flags = store.Flags(node);
        if (store.Kind(node) == SyntaxKind.VariableDeclaration)
        {
            node = store.Parent(node);
        }
        if (!node.IsNone && store.Kind(node) == SyntaxKind.VariableDeclarationList)
        {
            flags |= store.Flags(node);
            node = store.Parent(node);
        }
        if (!node.IsNone && store.Kind(node) == SyntaxKind.VariableStatement)
        {
            flags |= store.Flags(node);
        }
        return flags;
    }

    /// <summary>utilitiesPublic.ts: walkUpBindingElementsAndPatterns.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node WalkUpBindingElementsAndPatterns(NodeStore store, Node binding)
    {
        var node = store.Parent(binding);
        while (store.Kind(store.Parent(node)) == SyntaxKind.BindingElement)
        {
            node = store.Parent(store.Parent(node));
        }
        return store.Parent(node);
    }

    /// <summary>utilities.ts: getRootDeclaration.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetRootDeclaration(NodeStore store, Node node)
    {
        while (store.Kind(node) == SyntaxKind.BindingElement)
        {
            node = store.Parent(store.Parent(node));
        }
        return node;
    }

    /// <summary>utilities.ts: isBlockOrCatchScoped.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsBlockOrCatchScoped(NodeStore store, Node declaration)
    {
        if ((GetCombinedNodeFlags(store, declaration) & NodeFlags.BlockScoped) != 0)
        {
            return true;
        }
        // isCatchClauseVariableDeclarationOrBindingElement
        var node = GetRootDeclaration(store, declaration);
        return store.Kind(node) == SyntaxKind.VariableDeclaration && store.Kind(store.Parent(node)) == SyntaxKind.CatchClause;
    }

    /// <summary>utilities.ts: isPartOfParameterDeclaration.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPartOfParameterDeclaration(NodeStore store, Node node)
    {
        return store.Kind(GetRootDeclaration(store, node)) == SyntaxKind.Parameter;
    }

    /// <summary>utilities.ts: isEnumConst.</summary>
    internal static bool IsEnumConst(NodeStore store, Node node)
    {
        return (GetCombinedModifierFlags(store, node) & ModifierFlags.Const) != 0;
    }

    /// <summary>utilities.ts: isObjectLiteralMethod.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsObjectLiteralMethod(NodeStore store, Node node)
    {
        return !node.IsNone && store.Kind(node) == SyntaxKind.MethodDeclaration && store.Kind(store.Parent(node)) == SyntaxKind.ObjectLiteralExpression;
    }

    /// <summary>utilities.ts: isObjectLiteralOrClassExpressionMethodOrAccessor.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsObjectLiteralOrClassExpressionMethodOrAccessor(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        if (kind != SyntaxKind.MethodDeclaration && kind != SyntaxKind.GetAccessor && kind != SyntaxKind.SetAccessor)
        {
            return false;
        }
        var parentKind = store.Kind(store.Parent(node));
        return parentKind == SyntaxKind.ObjectLiteralExpression || parentKind == SyntaxKind.ClassExpression;
    }

    /// <summary>utilitiesPublic.ts: isFunctionLike.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsFunctionLike(NodeStore store, Node node)
    {
        return !node.IsNone && IsFunctionLikeKind(store.Kind(node));
    }

    /// <summary>utilitiesPublic.ts: isFunctionLikeKind.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsFunctionLikeKind(SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.MethodSignature:
            case SyntaxKind.CallSignature:
            case SyntaxKind.JSDocSignature:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.FunctionType:
            case SyntaxKind.JSDocFunctionType:
            case SyntaxKind.ConstructorType:
                return true;
            default:
                return IsFunctionLikeDeclarationKind(kind);
        }
    }

    /// <summary>utilitiesPublic.ts: isFunctionLikeDeclarationKind.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsFunctionLikeDeclarationKind(SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.Constructor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
                return true;
            default:
                return false;
        }
    }

    /// <summary>utilitiesPublic.ts: isFunctionLikeOrClassStaticBlockDeclaration.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsFunctionLikeOrClassStaticBlockDeclaration(NodeStore store, Node node)
    {
        return !node.IsNone && (IsFunctionLikeKind(store.Kind(node)) || store.Kind(node) == SyntaxKind.ClassStaticBlockDeclaration);
    }

    /// <summary>utilities.ts: isAsyncFunction.</summary>
    internal static bool IsAsyncFunction(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.MethodDeclaration:
                return !store.Body(node).IsNone
                    && store.AsteriskToken(node).IsNone
                    && HasSyntacticModifier(store, node, ModifierFlags.Async);
        }
        return false;
    }

    /// <summary>utilitiesPublic.ts: isAutoAccessorPropertyDeclaration.</summary>
    internal static bool IsAutoAccessorPropertyDeclaration(NodeStore store, Node node)
    {
        return store.Kind(node) == SyntaxKind.PropertyDeclaration && HasSyntacticModifier(store, node, ModifierFlags.Accessor);
    }

    /// <summary>utilitiesPublic.ts: isParameterPropertyDeclaration.</summary>
    internal static bool IsParameterPropertyDeclaration(NodeStore store, Node node, Node parent)
    {
        return store.Kind(node) == SyntaxKind.Parameter && HasSyntacticModifier(store, node, ModifierFlags.ParameterPropertyModifier) && store.Kind(parent) == SyntaxKind.Constructor;
    }

    /// <summary>utilities.ts: exportAssignmentIsAlias (getExportAssignmentExpression, isAliasableExpression).</summary>
    internal static bool ExportAssignmentIsAlias(NodeStore store, Node node)
    {
        var e = store.Kind(node) == SyntaxKind.ExportAssignment ? store.Expression(node) : store.Right(node);
        return IsEntityNameExpression(store, e) || store.Kind(e) == SyntaxKind.ClassExpression;
    }

    /// <summary>utilities.ts: moduleExportNameIsDefault.</summary>
    internal static bool ModuleExportNameIsDefault(NodeStore store, Node node)
    {
        return store.Texts.Equals(store.Kind(node) == SyntaxKind.StringLiteral ? store.Text(node) : store.EscapedText(node), InternalSymbolName.Default);
    }

    /// <summary>utilities.ts: getImmediatelyInvokedFunctionExpression.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetImmediatelyInvokedFunctionExpression(NodeStore store, Node func)
    {
        if (store.Kind(func) == SyntaxKind.FunctionExpression || store.Kind(func) == SyntaxKind.ArrowFunction)
        {
            var prev = func;
            var parent = store.Parent(func);
            while (store.Kind(parent) == SyntaxKind.ParenthesizedExpression)
            {
                prev = parent;
                parent = store.Parent(parent);
            }
            if (store.Kind(parent) == SyntaxKind.CallExpression && store.Expression(parent) == prev)
            {
                return parent;
            }
        }
        return default;
    }

    /// <summary>utilities.ts: isPotentiallyExecutableNode.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPotentiallyExecutableNode(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        if (SyntaxKind.FirstStatement <= kind && kind <= SyntaxKind.LastStatement)
        {
            if (kind == SyntaxKind.VariableStatement)
            {
                var list = store.DeclarationList(node);
                if ((GetCombinedNodeFlags(store, list) & NodeFlags.BlockScoped) != 0)
                {
                    return true;
                }
                foreach (var d in store.Nodes(store.Declarations(list)))
                {
                    if (!store.Initializer(d).IsNone)
                    {
                        return true;
                    }
                }
                return false;
            }
            return true;
        }
        return kind == SyntaxKind.ClassDeclaration || kind == SyntaxKind.EnumDeclaration || kind == SyntaxKind.ModuleDeclaration;
    }

    /// <summary>utilities.ts: canHaveFlowNode.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool CanHaveFlowNode(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        if (kind >= SyntaxKind.FirstStatement && kind <= SyntaxKind.LastStatement)
        {
            return true;
        }
        switch (kind)
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.ThisKeyword:
            case SyntaxKind.SuperKeyword:
            case SyntaxKind.QualifiedName:
            case SyntaxKind.MetaProperty:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.BindingElement:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                return true;
            default:
                return false;
        }
    }

    /// <summary>utilitiesPublic.ts: canHaveLocals.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool CanHaveLocals(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.Block:
            case SyntaxKind.CallSignature:
            case SyntaxKind.CaseBlock:
            case SyntaxKind.CatchClause:
            case SyntaxKind.ClassStaticBlockDeclaration:
            case SyntaxKind.ConditionalType:
            case SyntaxKind.Constructor:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionType:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.JSDocCallbackTag:
            case SyntaxKind.JSDocEnumTag:
            case SyntaxKind.JSDocFunctionType:
            case SyntaxKind.JSDocSignature:
            case SyntaxKind.JSDocTypedefTag:
            case SyntaxKind.MappedType:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.SourceFile:
            case SyntaxKind.TypeAliasDeclaration:
                return true;
            default:
                return false;
        }
    }

    /// <summary>utilitiesPublic.ts: canHaveSymbol.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool CanHaveSymbol(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.BinaryExpression:
            case SyntaxKind.BindingElement:
            case SyntaxKind.CallExpression:
            case SyntaxKind.CallSignature:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.ClassStaticBlockDeclaration:
            case SyntaxKind.Constructor:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.EnumMember:
            case SyntaxKind.ExportAssignment:
            case SyntaxKind.ExportDeclaration:
            case SyntaxKind.ExportSpecifier:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionType:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.Identifier:
            case SyntaxKind.ImportClause:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.JSDocCallbackTag:
            case SyntaxKind.JSDocEnumTag:
            case SyntaxKind.JSDocFunctionType:
            case SyntaxKind.JSDocParameterTag:
            case SyntaxKind.JSDocPropertyTag:
            case SyntaxKind.JSDocSignature:
            case SyntaxKind.JSDocTypedefTag:
            case SyntaxKind.JSDocTypeLiteral:
            case SyntaxKind.JsxAttribute:
            case SyntaxKind.JsxAttributes:
            case SyntaxKind.JsxSpreadAttribute:
            case SyntaxKind.MappedType:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.NamedTupleMember:
            case SyntaxKind.NamespaceExport:
            case SyntaxKind.NamespaceExportDeclaration:
            case SyntaxKind.NamespaceImport:
            case SyntaxKind.NewExpression:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.Parameter:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.ShorthandPropertyAssignment:
            case SyntaxKind.SourceFile:
            case SyntaxKind.SpreadAssignment:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.TypeLiteral:
            case SyntaxKind.TypeParameter:
            case SyntaxKind.VariableDeclaration:
                return true;
            default:
                return false;
        }
    }

    /// <summary>utilities.ts: isDestructuringAssignment.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsDestructuringAssignment(NodeStore store, Node node)
    {
        if (IsAssignmentExpression(store, node, /*excludeCompoundAssignment*/ true))
        {
            var kind = store.Kind(store.Left(node));
            return kind == SyntaxKind.ObjectLiteralExpression || kind == SyntaxKind.ArrayLiteralExpression;
        }
        return false;
    }

    /// <summary>utilities.ts: isDottedName.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsDottedName(NodeStore store, Node node)
    {
        while (true)
        {
            var kind = store.Kind(node);
            if (kind == SyntaxKind.Identifier || kind == SyntaxKind.ThisKeyword || kind == SyntaxKind.SuperKeyword || kind == SyntaxKind.MetaProperty)
            {
                return true;
            }
            if (kind != SyntaxKind.PropertyAccessExpression && kind != SyntaxKind.ParenthesizedExpression)
            {
                return false;
            }
            node = store.Expression(node);
        }
    }

    /// <summary>utilities.ts: isPushOrUnshiftIdentifier.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPushOrUnshiftIdentifier(NodeStore store, Node node)
    {
        var text = store.EscapedText(node);
        return store.Texts.Equals(text, "push") || store.Texts.Equals(text, "unshift");
    }

    /// <summary>utilitiesPublic.ts: isOptionalChainRoot.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsOptionalChainRoot(NodeStore store, Node node)
    {
        return UtilitiesPublic.IsOptionalChain(store, node) && store.Kind(node) != SyntaxKind.NonNullExpression && !store.QuestionDotToken(node).IsNone;
    }

    /// <summary>utilitiesPublic.ts: isExpressionOfOptionalChainRoot.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsExpressionOfOptionalChainRoot(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        return IsOptionalChainRoot(store, parent) && store.Expression(parent) == node;
    }

    /// <summary>utilitiesPublic.ts: isOutermostOptionalChain.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsOutermostOptionalChain(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        return !UtilitiesPublic.IsOptionalChain(store, parent) // cases 1, 2, and 3
            || IsOptionalChainRoot(store, parent) // case 4
            || node != store.Expression(parent); // case 5
    }

    /// <summary>utilitiesPublic.ts: isNullishCoalesce.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNullishCoalesce(NodeStore store, Node node)
    {
        return store.Kind(node) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(node)) == SyntaxKind.QuestionQuestionToken;
    }

    /// <summary>utilities.ts: isLogicalOrCoalescingBinaryOperator (isBinaryLogicalOperator or ??).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsLogicalOrCoalescingBinaryOperator(SyntaxKind token)
    {
        return token == SyntaxKind.BarBarToken || token == SyntaxKind.AmpersandAmpersandToken || token == SyntaxKind.QuestionQuestionToken;
    }

    /// <summary>utilities.ts: isLogicalOrCoalescingBinaryExpression.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsLogicalOrCoalescingBinaryExpression(NodeStore store, Node expr)
    {
        return store.Kind(expr) == SyntaxKind.BinaryExpression && IsLogicalOrCoalescingBinaryOperator(store.Kind(store.OperatorToken(expr)));
    }

    /// <summary>utilities.ts: isLogicalOrCoalescingAssignmentExpression.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsLogicalOrCoalescingAssignmentExpression(NodeStore store, Node expr)
    {
        return store.Kind(expr) == SyntaxKind.BinaryExpression && IsLogicalOrCoalescingAssignmentOperator(store.Kind(store.OperatorToken(expr)));
    }

    /// <summary>utilities.ts: isAssignmentTarget (getAssignmentTarget).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAssignmentTarget(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        while (true)
        {
            switch (store.Kind(parent))
            {
                case SyntaxKind.BinaryExpression:
                    return IsAssignmentOperator(store.Kind(store.OperatorToken(parent))) && store.Left(parent) == node;
                case SyntaxKind.PrefixUnaryExpression:
                case SyntaxKind.PostfixUnaryExpression:
                    var unaryOperator = store.Operator(parent);
                    return unaryOperator == SyntaxKind.PlusPlusToken || unaryOperator == SyntaxKind.MinusMinusToken;
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                    return store.Initializer(parent) == node;
                case SyntaxKind.ParenthesizedExpression:
                case SyntaxKind.ArrayLiteralExpression:
                case SyntaxKind.SpreadElement:
                case SyntaxKind.NonNullExpression:
                    node = parent;
                    break;
                case SyntaxKind.SpreadAssignment:
                    node = store.Parent(parent);
                    break;
                case SyntaxKind.ShorthandPropertyAssignment:
                    if (store.Name(parent) != node)
                    {
                        return false;
                    }
                    node = store.Parent(parent);
                    break;
                case SyntaxKind.PropertyAssignment:
                    if (store.Name(parent) == node)
                    {
                        return false;
                    }
                    node = store.Parent(parent);
                    break;
                default:
                    return false;
            }
            parent = store.Parent(node);
        }
    }

    /// <summary>utilities.ts: isPrologueDirective.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPrologueDirective(NodeStore store, Node node)
    {
        return store.Kind(node) == SyntaxKind.ExpressionStatement && store.Kind(store.Expression(node)) == SyntaxKind.StringLiteral;
    }

    /// <summary>utilities.ts: isPartOfTypeQuery.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPartOfTypeQuery(NodeStore store, Node node)
    {
        while (store.Kind(node) == SyntaxKind.QualifiedName || store.Kind(node) == SyntaxKind.Identifier)
        {
            node = store.Parent(node);
        }
        return store.Kind(node) == SyntaxKind.TypeQuery;
    }

    /// <summary>utilities.ts: declarationNameToString.</summary>
    internal static string DeclarationNameToString(NodeStore store, Node name)
    {
        return name.IsNone || GetFullWidth(store, name) == 0 ? "(Missing)" : GetSourceTextOfNodeFromSourceFile(store, GetSourceFileOfNode(store, name), name);
    }

    /// <summary>utilitiesPublic.ts: nodeHasName.</summary>
    internal static bool NodeHasName(NodeStore store, Node statement, Node name)
    {
        var statementName = store.Name(statement);
        if (!statementName.IsNone && store.Kind(statementName) == SyntaxKind.Identifier && store.Texts.Equals(UtilitiesPublic.IdText(store, statementName), UtilitiesPublic.IdText(store, name)))
        {
            return true;
        }
        if (store.Kind(statement) == SyntaxKind.VariableStatement)
        {
            foreach (var d in store.Nodes(store.Declarations(store.DeclarationList(statement))))
            {
                if (NodeHasName(store, d, name))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>utilities.ts: hasDynamicName.</summary>
    internal static bool HasDynamicName(NodeStore store, Node declaration)
    {
        var name = GetNameOfDeclaration(store, declaration);
        return !name.IsNone && IsDynamicName(store, name);
    }

    /// <summary>utilities.ts: isJSDocTypeAlias.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocTypeAlias(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        return kind == SyntaxKind.JSDocTypedefTag || kind == SyntaxKind.JSDocCallbackTag || kind == SyntaxKind.JSDocEnumTag;
    }

    /// <summary>utilities.ts: getJSDocHost (getJSDocRoot: the enclosing JSDoc comment, whose parent is the host if it is its last one).</summary>
    internal static Node GetJSDocHost(NodeStore store, Node node)
    {
        var jsDoc = store.Parent(node);
        while (!jsDoc.IsNone && store.Kind(jsDoc) != SyntaxKind.JSDoc)
        {
            jsDoc = store.Parent(jsDoc);
        }
        if (jsDoc.IsNone)
        {
            return default;
        }
        var host = store.Parent(jsDoc);
        if (!host.IsNone)
        {
            var hostJsDoc = store.JsDoc(host);
            if (hostJsDoc is not null && hostJsDoc.Count > 0 && hostJsDoc[^1] == jsDoc)
            {
                return host;
            }
        }
        return default;
    }

    /// <summary>utilities.ts: getEffectiveJSDocHost.</summary>
    internal static Node GetEffectiveJSDocHost(NodeStore store, Node node)
    {
        var host = GetJSDocHost(store, node);
        if (host.IsNone)
        {
            return default;
        }
        // getSourceOfDefaultedAssignment
        if (store.Kind(host) == SyntaxKind.ExpressionStatement)
        {
            var expression = store.Expression(host);
            if (store.Kind(expression) == SyntaxKind.BinaryExpression)
            {
                var right = store.Right(expression);
                if (GetAssignmentDeclarationKind(store, expression) != AssignmentDeclarationKind.None
                    && store.Kind(right) == SyntaxKind.BinaryExpression
                    && (store.Kind(store.OperatorToken(right)) == SyntaxKind.BarBarToken || store.Kind(store.OperatorToken(right)) == SyntaxKind.QuestionQuestionToken)
                    && !store.Right(right).IsNone)
                {
                    return store.Right(right);
                }
                // getSourceOfAssignment
                if (store.Kind(store.OperatorToken(expression)) == SyntaxKind.EqualsToken)
                {
                    var source = GetRightMostAssignedExpression(store, expression);
                    if (!source.IsNone)
                    {
                        return source;
                    }
                }
            }
        }
        // getSingleInitializerOfVariableStatementOrPropertyDeclaration
        switch (store.Kind(host))
        {
            case SyntaxKind.VariableStatement:
            {
                var v = GetSingleVariableOfVariableStatement(store, host);
                if (!v.IsNone && !store.Initializer(v).IsNone)
                {
                    return store.Initializer(v);
                }
                break;
            }
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertyAssignment:
                if (!store.Initializer(host).IsNone)
                {
                    return store.Initializer(host);
                }
                break;
        }
        // getSingleVariableOfVariableStatement
        var single = GetSingleVariableOfVariableStatement(store, host);
        if (!single.IsNone)
        {
            return single;
        }
        // getNestedModuleDeclaration
        if (store.Kind(host) == SyntaxKind.ModuleDeclaration && !store.Body(host).IsNone && store.Kind(store.Body(host)) == SyntaxKind.ModuleDeclaration)
        {
            return store.Body(host);
        }
        return host;
    }

    /// <summary>utilities.ts: getHostSignatureFromJSDoc.</summary>
    internal static Node GetHostSignatureFromJSDoc(NodeStore store, Node node)
    {
        var host = GetEffectiveJSDocHost(store, node);
        if (!host.IsNone)
        {
            if (store.Kind(host) == SyntaxKind.PropertySignature && !store.Type(host).IsNone && IsFunctionLike(store, store.Type(host)))
            {
                return store.Type(host);
            }
            return IsFunctionLike(store, host) ? host : default;
        }
        return default;
    }

    /// <summary>utilities.ts: getEffectiveContainerForJSDocTemplateTag.</summary>
    internal static Node GetEffectiveContainerForJSDocTemplateTag(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        if (store.Kind(parent) == SyntaxKind.JSDoc && !store.Tags(parent).IsNone)
        {
            // A @template tag belongs to any @typedef, @callback, or @enum tags in the same comment block, if they exist.
            foreach (var tag in store.Nodes(store.Tags(parent)))
            {
                if (IsJSDocTypeAlias(store, tag))
                {
                    return tag;
                }
            }
        }
        // otherwise it belongs to the host it annotates
        return GetHostSignatureFromJSDoc(store, node);
    }

    /// <summary>utilities.ts: isBlockScope.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsBlockScope(NodeStore store, Node node, Node parentNode)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.SourceFile:
            case SyntaxKind.CaseBlock:
            case SyntaxKind.CatchClause:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
            case SyntaxKind.Constructor:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.ClassStaticBlockDeclaration:
                return true;
            case SyntaxKind.Block:
                // function block is not considered block-scope container
                // see comment in binder.ts: bind(...), case for SyntaxKind.Block
                return !IsFunctionLikeOrClassStaticBlockDeclaration(store, parentNode);
        }
        return false;
    }

    /// <summary>utilities.ts: getEnclosingBlockScopeContainer.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetEnclosingBlockScopeContainer(NodeStore store, Node node)
    {
        for (var current = store.Parent(node); !current.IsNone; current = store.Parent(current))
        {
            if (IsBlockScope(store, current, store.Parent(current)))
            {
                return current;
            }
        }
        return default;
    }

    /// <summary>utilities.ts: getEnclosingContainer.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetEnclosingContainer(NodeStore store, Node node)
    {
        for (var n = store.Parent(node); !n.IsNone; n = store.Parent(n))
        {
            if ((Binder.GetContainerFlags(store, n) & ContainerFlags.IsContainer) != 0)
            {
                return n;
            }
        }
        return default;
    }

    /// <summary>utilities.ts: isSpecialPropertyDeclaration.</summary>
    internal static bool IsSpecialPropertyDeclaration(NodeStore store, Node expr)
    {
        var parent = store.Parent(expr);
        return IsInJSFile(store, expr)
            && !parent.IsNone && store.Kind(parent) == SyntaxKind.ExpressionStatement
            && (store.Kind(expr) != SyntaxKind.ElementAccessExpression || IsStringOrNumericLiteralLike(store, store.ArgumentExpression(expr)))
            && !UtilitiesPublic.GetJSDocTypeTag(store, parent).IsNone;
    }

    /// <summary>utilities.ts: removeFileExtension.</summary>
    internal static string RemoveFileExtension(string path)
    {
        foreach (var ext in RemovableExtensions)
        {
            if (TsPath.FileExtensionIs(path, ext))
            {
                return path[..(path.Length - ext.Length)];
            }
        }
        return path;
    }

    // utilities.ts: extensionsToRemove.
    private static readonly string[] RemovableExtensions = [Extension.Dts, Extension.Dmts, Extension.Dcts, Extension.Mjs, Extension.Mts, Extension.Cjs, Extension.Cts, Extension.Ts, Extension.Js, Extension.Tsx, Extension.Jsx, Extension.Json];

    /// <summary>utilities.ts: tryParsePattern, as far as the binder uses it: whether the pattern has at most one '*'.</summary>
    internal static bool IsValidPattern(ReadOnlySpan<char> pattern)
    {
        var indexOfStar = pattern.IndexOf('*');
        return indexOfStar == -1 || pattern[(indexOfStar + 1)..].IndexOf('*') == -1;
    }

    // ---- module B3: JavaScript special assignments ----

    /// <summary>utilities.ts: getNameOrArgument.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetNameOrArgument(NodeStore store, Node expr)
    {
        return store.Kind(expr) == SyntaxKind.PropertyAccessExpression ? store.Name(expr) : store.ArgumentExpression(expr);
    }

    /// <summary>utilities.ts: isLiteralLikeAccess (x.y OR x[0]).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsLiteralLikeAccess(NodeStore store, Node node)
    {
        return store.Kind(node) == SyntaxKind.PropertyAccessExpression || IsLiteralLikeElementAccess(store, node);
    }

    /// <summary>utilities.ts: isEmptyObjectLiteral.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsEmptyObjectLiteral(NodeStore store, Node expression)
    {
        return store.Kind(expression) == SyntaxKind.ObjectLiteralExpression && store.Length(store.Properties(expression)) == 0;
    }

    /// <summary>utilities.ts: isAliasableExpression.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAliasableExpression(NodeStore store, Node e)
    {
        return IsEntityNameExpression(store, e) || store.Kind(e) == SyntaxKind.ClassExpression;
    }

    /// <summary>utilitiesPublic.ts: isFunctionLikeDeclaration (false for undefined).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsFunctionLikeDeclaration(NodeStore store, Node node)
    {
        return !node.IsNone && IsFunctionLikeDeclarationKind(store.Kind(node));
    }

    /// <summary>
    /// utilities.ts: getAssignedExpandoInitializer. Get the assignment 'initializer' -- the righthand side-- when the
    /// initializer is container-like (See getExpandoInitializer). We treat the right hand side of assignments with
    /// container-like initializers as declarations.
    /// </summary>
    internal static Node GetAssignedExpandoInitializer(NodeStore store, Node node)
    {
        if (node.IsNone)
        {
            return default;
        }
        var parent = store.Parent(node);
        if (!parent.IsNone && store.Kind(parent) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(parent)) == SyntaxKind.EqualsToken)
        {
            var isPrototypeAssignment = IsPrototypeAccess(store, store.Left(parent));
            var expando = GetExpandoInitializer(store, store.Right(parent), isPrototypeAssignment);
            return !expando.IsNone ? expando : GetDefaultedExpandoInitializer(store, store.Left(parent), store.Right(parent), isPrototypeAssignment);
        }
        if (store.Kind(node) == SyntaxKind.CallExpression && IsBindableObjectDefinePropertyCall(store, node))
        {
            var args = store.Arguments(node);
            var result = HasExpandoValueProperty(store, store.Element(args, 2), store.Texts.Equals(store.Text(store.Element(args, 1)), "prototype"));
            if (!result.IsNone)
            {
                return result;
            }
        }
        return default;
    }

    // utilities.ts hasExpandoValueProperty (node: the third argument of Object.defineProperty; properties of anything but
    // an object literal are undefined, forEach of undefined is undefined).
    private static Node HasExpandoValueProperty(NodeStore store, Node node, bool isPrototypeAssignment)
    {
        if (store.Kind(node) != SyntaxKind.ObjectLiteralExpression)
        {
            return default;
        }
        foreach (var p in store.Nodes(store.Properties(node)))
        {
            if (store.Kind(p) == SyntaxKind.PropertyAssignment &&
                store.Kind(store.Name(p)) == SyntaxKind.Identifier &&
                store.Texts.Equals(store.EscapedText(store.Name(p)), "value") &&
                !store.Initializer(p).IsNone)
            {
                var result = GetExpandoInitializer(store, store.Initializer(p), isPrototypeAssignment);
                if (!result.IsNone)
                {
                    return result;
                }
            }
        }
        return default;
    }

    /// <summary>
    /// utilities.ts: getExpandoInitializer. Recognized expando initializers are:
    /// 1. (function() {})() -- IIFEs
    /// 2. function() { } -- Function expressions
    /// 3. class { } -- Class expressions
    /// 4. {} -- Empty object literals
    /// 5. { ... } -- Non-empty object literals, when used to initialize a prototype, like `C.prototype = { m() { } }`
    ///
    /// This function returns the provided initializer, or undefined if it is not valid.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetExpandoInitializer(NodeStore store, Node initializer, bool isPrototypeAssignment)
    {
        var kind = store.Kind(initializer);
        if (kind == SyntaxKind.CallExpression)
        {
            var e = SkipParentheses(store, store.Expression(initializer));
            return store.Kind(e) == SyntaxKind.FunctionExpression || store.Kind(e) == SyntaxKind.ArrowFunction ? initializer : default;
        }
        if (kind == SyntaxKind.FunctionExpression || kind == SyntaxKind.ClassExpression || kind == SyntaxKind.ArrowFunction)
        {
            return initializer;
        }
        if (kind == SyntaxKind.ObjectLiteralExpression && (store.Length(store.Properties(initializer)) == 0 || isPrototypeAssignment))
        {
            return initializer;
        }
        return default;
    }

    /// <summary>
    /// utilities.ts: getDefaultedExpandoInitializer. A defaulted expando initializer matches the pattern
    /// `Lhs = Lhs || ExpandoInitializer` or `var Lhs = Lhs || ExpandoInitializer`.
    /// The second Lhs is required to be the same as the first except that it may be prefixed with
    /// 'window.', 'global.' or 'self.' The second Lhs is otherwise ignored by the binder and checker.
    /// </summary>
    private static Node GetDefaultedExpandoInitializer(NodeStore store, Node name, Node initializer, bool isPrototypeAssignment)
    {
        if (store.Kind(initializer) != SyntaxKind.BinaryExpression)
        {
            return default;
        }
        var op = store.Kind(store.OperatorToken(initializer));
        if (op != SyntaxKind.BarBarToken && op != SyntaxKind.QuestionQuestionToken)
        {
            return default;
        }
        var e = GetExpandoInitializer(store, store.Right(initializer), isPrototypeAssignment);
        if (!e.IsNone && IsSameEntityName(store, name, store.Left(initializer)))
        {
            return e;
        }
        return default;
    }

    /// <summary>
    /// utilities.ts: isSameEntityName. Is the 'declared' name the same as the one in the initializer?
    /// true for identical entity names, as well as ones where the initializer is prefixed with
    /// 'window', 'self' or 'global'. For example:
    ///
    /// var my = my || {}
    /// var min = window.min || {}
    /// my.app = self.my.app || class { }
    /// </summary>
    internal static bool IsSameEntityName(NodeStore store, Node name, Node initializer)
    {
        if (IsPropertyNameLiteral(store, name) && IsPropertyNameLiteral(store, initializer))
        {
            return store.Texts.Equals(GetTextOfIdentifierOrLiteral(store, name), GetTextOfIdentifierOrLiteral(store, initializer));
        }
        var nameKind = store.Kind(name);
        if (
            (nameKind == SyntaxKind.Identifier || nameKind == SyntaxKind.PrivateIdentifier) && IsLiteralLikeAccess(store, initializer) &&
            (store.Kind(store.Expression(initializer)) == SyntaxKind.ThisKeyword ||
                store.Kind(store.Expression(initializer)) == SyntaxKind.Identifier &&
                    (store.Texts.Equals(store.EscapedText(store.Expression(initializer)), "window") ||
                        store.Texts.Equals(store.EscapedText(store.Expression(initializer)), "self") ||
                        store.Texts.Equals(store.EscapedText(store.Expression(initializer)), "global"))))
        {
            return IsSameEntityName(store, name, GetNameOrArgument(store, initializer));
        }
        if (IsLiteralLikeAccess(store, name) && IsLiteralLikeAccess(store, initializer))
        {
            var a = GetElementOrPropertyAccessName(store, name);
            var b = GetElementOrPropertyAccessName(store, initializer);
            return (a.IsNone && b.IsNone || store.Texts.Equals(a, b))
                && IsSameEntityName(store, store.Expression(name), store.Expression(initializer));
        }
        return false;
    }
}
