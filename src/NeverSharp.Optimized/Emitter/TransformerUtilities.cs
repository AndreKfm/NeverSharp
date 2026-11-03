// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilitiesPublic.ts (isStatement, isStatementKindButNotDeclarationKind,
// isDeclarationStatementKind, isBlockStatement), src/compiler/utilities.ts (nodeCanBeDecorated, nodeIsDecorated,
// nodeOrChildIsDecorated, childIsDecorated, classOrConstructorParameterIsDecorated,
// classElementOrClassElementParameterIsDecorated, getFirstConstructorWithBody, getEffectiveBaseTypeNode,
// parameterIsThisKeyword, moveRangePastModifiers, moveRangePos) and src/compiler/transformers/utilities.ts
// (getProperties, isInitializedProperty, isSimpleCopiableExpression, isSimpleInlineableExpression)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Modified: translated from TypeScript to C#; module D1 (the parts the TypeScript transformer's first paths use).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>The node utilities of utilities.ts, utilitiesPublic.ts and transformers/utilities.ts the transformers use.</summary>
internal static class TransformerUtilities
{
    /// <summary>utilitiesPublic.ts isStatement.</summary>
    internal static bool IsStatement(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        return IsStatementKindButNotDeclarationKind(kind)
            || IsDeclarationStatementKind(kind)
            || IsBlockStatement(store, node);
    }

    private static bool IsBlockStatement(NodeStore store, Node node)
    {
        if (store.Kind(node) != SyntaxKind.Block)
        {
            return false;
        }
        var parent = store.Parent(node);
        if (!parent.IsNone)
        {
            if (store.Kind(parent) is SyntaxKind.TryStatement or SyntaxKind.CatchClause)
            {
                return false;
            }
        }
        // !isFunctionBlock(node)
        return parent.IsNone || !Utilities.IsFunctionLike(store, parent);
    }

    internal static bool IsDeclarationStatementKind(SyntaxKind kind)
    {
        return kind is SyntaxKind.FunctionDeclaration
            or SyntaxKind.MissingDeclaration
            or SyntaxKind.ClassDeclaration
            or SyntaxKind.InterfaceDeclaration
            or SyntaxKind.TypeAliasDeclaration
            or SyntaxKind.EnumDeclaration
            or SyntaxKind.ModuleDeclaration
            or SyntaxKind.ImportDeclaration
            or SyntaxKind.ImportEqualsDeclaration
            or SyntaxKind.ExportDeclaration
            or SyntaxKind.ExportAssignment
            or SyntaxKind.NamespaceExportDeclaration;
    }

    internal static bool IsStatementKindButNotDeclarationKind(SyntaxKind kind)
    {
        return kind is SyntaxKind.BreakStatement
            or SyntaxKind.ContinueStatement
            or SyntaxKind.DebuggerStatement
            or SyntaxKind.DoStatement
            or SyntaxKind.ExpressionStatement
            or SyntaxKind.EmptyStatement
            or SyntaxKind.ForInStatement
            or SyntaxKind.ForOfStatement
            or SyntaxKind.ForStatement
            or SyntaxKind.IfStatement
            or SyntaxKind.LabeledStatement
            or SyntaxKind.ReturnStatement
            or SyntaxKind.SwitchStatement
            or SyntaxKind.ThrowStatement
            or SyntaxKind.TryStatement
            or SyntaxKind.VariableStatement
            or SyntaxKind.WhileStatement
            or SyntaxKind.WithStatement
            or SyntaxKind.NotEmittedStatement;
    }

    /// <summary>utilities.ts hasDecorators.</summary>
    internal static bool HasDecorators(NodeStore store, Node node)
    {
        foreach (var modifier in store.Nodes(store.Modifiers(node)))
        {
            if (store.Kind(modifier) == SyntaxKind.Decorator)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>utilities.ts nodeCanBeDecorated.</summary>
    internal static bool NodeCanBeDecorated(NodeStore store, bool useLegacyDecorators, Node node, Node parent, Node grandparent)
    {
        // private names cannot be used with decorators yet
        if (!store.Name(node).IsNone && store.Kind(node) is not (SyntaxKind.Parameter or SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression) && store.Kind(store.Name(node)) == SyntaxKind.PrivateIdentifier && useLegacyDecorators)
        {
            return false;
        }

        switch (store.Kind(node))
        {
            case SyntaxKind.ClassDeclaration:
                // class declarations are valid targets
                return true;

            case SyntaxKind.ClassExpression:
                // class expressions are valid targets for native decorators
                return !useLegacyDecorators;

            case SyntaxKind.PropertyDeclaration:
                // property declarations are valid if their parent is a class declaration.
                return !parent.IsNone && (useLegacyDecorators
                    ? store.Kind(parent) == SyntaxKind.ClassDeclaration
                    : Utilities.IsClassLike(store, parent) && !Utilities.HasSyntacticModifier(store, node, ModifierFlags.Abstract) && !Utilities.HasSyntacticModifier(store, node, ModifierFlags.Ambient));

            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.MethodDeclaration:
                // if this method has a body and its parent is a class declaration, this is a valid target.
                return !store.Body(node).IsNone && !parent.IsNone && (useLegacyDecorators
                    ? store.Kind(parent) == SyntaxKind.ClassDeclaration
                    : Utilities.IsClassLike(store, parent));

            case SyntaxKind.Parameter:
                // TODO(rbuckton): Parameter decorator support for ES decorators must wait until it is standardized
                if (!useLegacyDecorators)
                {
                    return false;
                }
                // if the parameter's parent has a body and its grandparent is a class declaration, this is a valid target.
                return !parent.IsNone
                    && !store.Body(parent).IsNone
                    && store.Kind(parent) is SyntaxKind.Constructor or SyntaxKind.MethodDeclaration or SyntaxKind.SetAccessor
                    && !IsThisParameterOf(store, parent, node)
                    && !grandparent.IsNone
                    && store.Kind(grandparent) == SyntaxKind.ClassDeclaration;
        }

        return false;
    }

    /// <summary>utilities.ts nodeIsDecorated.</summary>
    internal static bool NodeIsDecorated(NodeStore store, bool useLegacyDecorators, Node node, Node parent = default, Node grandparent = default)
    {
        return HasDecorators(store, node) && NodeCanBeDecorated(store, useLegacyDecorators, node, parent, grandparent);
    }

    /// <summary>utilities.ts nodeOrChildIsDecorated.</summary>
    internal static bool NodeOrChildIsDecorated(NodeStore store, bool useLegacyDecorators, Node node, Node parent = default, Node grandparent = default)
    {
        return NodeIsDecorated(store, useLegacyDecorators, node, parent, grandparent) || ChildIsDecorated(store, useLegacyDecorators, node, parent);
    }

    /// <summary>utilities.ts childIsDecorated.</summary>
    internal static bool ChildIsDecorated(NodeStore store, bool useLegacyDecorators, Node node, Node parent = default)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ClassDeclaration:
                foreach (var m in store.Nodes(store.Members(node)))
                {
                    if (NodeOrChildIsDecorated(store, useLegacyDecorators, m, node, parent))
                    {
                        return true;
                    }
                }
                return false;
            case SyntaxKind.ClassExpression:
                if (useLegacyDecorators)
                {
                    return false;
                }
                foreach (var m in store.Nodes(store.Members(node)))
                {
                    if (NodeOrChildIsDecorated(store, useLegacyDecorators, m, node, parent))
                    {
                        return true;
                    }
                }
                return false;
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.Constructor:
                foreach (var p in store.Nodes(store.Parameters(node)))
                {
                    if (NodeIsDecorated(store, useLegacyDecorators, p, node, parent))
                    {
                        return true;
                    }
                }
                return false;
            default:
                return false;
        }
    }

    /// <summary>utilities.ts classOrConstructorParameterIsDecorated.</summary>
    internal static bool ClassOrConstructorParameterIsDecorated(NodeStore store, bool useLegacyDecorators, Node node)
    {
        if (NodeIsDecorated(store, useLegacyDecorators, node))
        {
            return true;
        }
        var constructor = GetFirstConstructorWithBody(store, node);
        return !constructor.IsNone && ChildIsDecorated(store, useLegacyDecorators, constructor, node);
    }

    // utilities.ts getThisParameter(signature) === node: the first parameter of a signature is its `this` parameter.
    private static bool IsThisParameterOf(NodeStore store, Node signature, Node node)
    {
        var parameters = store.Nodes(store.Parameters(signature));
        return parameters.Length > 0 && parameters[0] == node && ParameterIsThisKeyword(store, node);
    }

    /// <summary>utilities.ts getFirstConstructorWithBody.</summary>
    internal static Node GetFirstConstructorWithBody(NodeStore store, Node node)
    {
        foreach (var member in store.Nodes(store.Members(node)))
        {
            if (store.Kind(member) == SyntaxKind.Constructor && Utilities.NodeIsPresent(store, store.Body(member)))
            {
                return member;
            }
        }
        return Node.None;
    }

    /// <summary>utilities.ts getEffectiveBaseTypeNode for a TypeScript class (getClassExtendsHeritageElement).</summary>
    internal static Node GetEffectiveBaseTypeNode(NodeStore store, Node node)
    {
        foreach (var clause in store.Nodes(store.HeritageClauses(node)))
        {
            if (store.Token(clause) == SyntaxKind.ExtendsKeyword)
            {
                var types = store.Nodes(store.Types(clause));
                return types.Length > 0 ? types[0] : Node.None;
            }
        }
        return Node.None;
    }

    /// <summary>utilities.ts parameterIsThisKeyword.</summary>
    internal static bool ParameterIsThisKeyword(NodeStore store, Node parameter)
    {
        var name = store.Name(parameter);
        return store.Kind(name) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(name), "this");
    }

    /// <summary>transformers/utilities.ts getProperties(node, requireInitializer, isStatic) is non-empty.</summary>
    internal static bool HasProperties(NodeStore store, Node node, bool requireInitializer, bool isStatic)
    {
        foreach (var member in store.Nodes(store.Members(node)))
        {
            // isInitializedOrStaticProperty
            if (store.Kind(member) == SyntaxKind.PropertyDeclaration
                && (!requireInitializer || !store.Initializer(member).IsNone)
                && Utilities.HasSyntacticModifier(store, member, ModifierFlags.Static) == isStatic)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>utilitiesPublic.ts getOriginalNode(node, nodeTest): the original node, if it passes the test.</summary>
    internal static Node GetOriginalNode(NodeStore store, Node node, Func<Node, bool>? nodeTest = null)
    {
        if (!node.IsNone)
        {
            while (!store.Original(node).IsNone)
            {
                node = store.Original(node);
            }
        }
        return node.IsNone || nodeTest is null || nodeTest(node) ? node : Node.None;
    }

    /// <summary>transformers/utilities.ts getSuperCallFromStatement: the super() call a statement contains (ignoring parentheses).</summary>
    internal static Node GetSuperCallFromStatement(NodeStore store, Node statement)
    {
        if (store.Kind(statement) != SyntaxKind.ExpressionStatement)
        {
            return Node.None;
        }

        var expression = Utilities.SkipParentheses(store, store.Expression(statement));
        // isSuperCall
        return store.Kind(expression) == SyntaxKind.CallExpression && store.Kind(store.Expression(expression)) == SyntaxKind.SuperKeyword
            ? expression
            : Node.None;
    }

    private static bool FindSuperStatementIndexPathWorker(NodeStore store, NodeList statements, int start, List<int> indices)
    {
        var length = store.Length(statements);
        for (var i = start; i < length; i += 1)
        {
            var statement = store.Element(statements, i);
            if (!GetSuperCallFromStatement(store, statement).IsNone)
            {
                indices.Insert(0, i);
                return true;
            }
            else if (store.Kind(statement) == SyntaxKind.TryStatement && FindSuperStatementIndexPathWorker(store, store.Statements(store.TryBlock(statement)), 0, indices))
            {
                indices.Insert(0, i);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// transformers/utilities.ts findSuperStatementIndexPath: a path of indices to navigate to a `super()` call,
    /// descending only through `try` statements (empty if `super()` was not found).
    /// </summary>
    internal static List<int> FindSuperStatementIndexPath(NodeStore store, NodeList statements, int start)
    {
        var indices = new List<int>();
        FindSuperStatementIndexPathWorker(store, statements, start, indices);
        return indices;
    }

    /// <summary>transformers/utilities.ts isSimpleCopiableExpression.</summary>
    internal static bool IsSimpleCopiableExpression(NodeStore store, Node expression)
    {
        var kind = store.Kind(expression);
        return kind is SyntaxKind.StringLiteral or SyntaxKind.NumericLiteral or SyntaxKind.NoSubstitutionTemplateLiteral or SyntaxKind.BigIntLiteral or SyntaxKind.RegularExpressionLiteral ||
            Utilities.IsKeyword(kind) ||
            kind == SyntaxKind.Identifier;
    }

    /// <summary>transformers/utilities.ts isSimpleInlineableExpression.</summary>
    internal static bool IsSimpleInlineableExpression(NodeStore store, Node expression)
    {
        return store.Kind(expression) != SyntaxKind.Identifier && IsSimpleCopiableExpression(store, expression);
    }
}
