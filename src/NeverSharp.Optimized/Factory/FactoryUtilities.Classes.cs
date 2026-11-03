// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/utilities.ts (createMemberAccessForPropertyName,
// expandPreOrPostfixIncrementOrDecrementExpression, startOnNewLine, createAccessorPropertyBackingField,
// createAccessorPropertyGetRedirector, createAccessorPropertySetRedirector, findComputedPropertyNameCacheAssignment,
// flattenCommaList) and src/compiler/factory/emitHelpers.ts (isCallToHelper)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/utilities.go and internal/transformers/utilities.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D4 (the units the decorator and class-fields transformers use).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>factory/utilities.ts: the units of the decorator and class-fields transformers (module D4).</summary>
internal static partial class FactoryUtilities
{
    /// <summary>createMemberAccessForPropertyName.</summary>
    internal static Node CreateMemberAccessForPropertyName(NodeFactory factory, Node target, Node memberName, Node location = default)
    {
        var store = factory.store;
        if (store.Kind(memberName) == SyntaxKind.ComputedPropertyName)
        {
            var access = factory.CreateElementAccessExpression(target, store.Expression(memberName));
            return location.IsNone ? access : FactoryUtilitiesPublic.SetTextRange(store, access, location);
        }
        else
        {
            var expression = FactoryUtilitiesPublic.SetTextRange(
                store,
                store.Kind(memberName) is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier
                    ? factory.CreatePropertyAccessExpression(target, memberName)
                    : factory.CreateElementAccessExpression(target, memberName),
                memberName);
            FactoryEmitNode.AddEmitFlags(store, expression, EmitFlags.NoNestedSourceMaps);
            return expression;
        }
    }

    /// <summary>
    /// expandPreOrPostfixIncrementOrDecrementExpression: expands `x++` / `++x` with a temp so that `++` and `--` work
    /// uniformly with numbers and bigints (the result variable is optional).
    /// </summary>
    internal static Node ExpandPreOrPostfixIncrementOrDecrementExpression(NodeFactory factory, Node node, Node expression, Action<Node> recordTempVariable, Node resultVariable)
    {
        var store = factory.store;
        var @operator = store.Operator(node);
        Debug.Assert(@operator is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken, "Expected 'node' to be a pre- or post-increment or pre- or post-decrement expression");

        var temp = factory.CreateTempVariable(recordTempVariable);
        expression = factory.CreateAssignment(temp, expression);
        FactoryUtilitiesPublic.SetTextRange(store, expression, store.Operand(node));

        var operation = store.Kind(node) == SyntaxKind.PrefixUnaryExpression ?
            factory.CreatePrefixUnaryExpression(@operator, temp) :
            factory.CreatePostfixUnaryExpression(temp, @operator);
        FactoryUtilitiesPublic.SetTextRange(store, operation, node);

        if (!resultVariable.IsNone)
        {
            operation = factory.CreateAssignment(resultVariable, operation);
            FactoryUtilitiesPublic.SetTextRange(store, operation, node);
        }

        expression = factory.CreateComma(expression, operation);
        FactoryUtilitiesPublic.SetTextRange(store, expression, node);

        if (store.Kind(node) == SyntaxKind.PostfixUnaryExpression)
        {
            expression = factory.CreateComma(expression, temp);
            FactoryUtilitiesPublic.SetTextRange(store, expression, node);
        }

        return expression;
    }

    /// <summary>startOnNewLine.</summary>
    internal static Node StartOnNewLine(NodeStore store, Node node) => FactoryEmitNode.SetStartsOnNewLine(store, node, newLine: true);

    /// <summary>Creates a private backing field for an `accessor` PropertyDeclaration.</summary>
    internal static Node CreateAccessorPropertyBackingField(NodeFactory factory, Node node, NodeList modifiers, Node initializer)
    {
        return factory.UpdatePropertyDeclaration(
            node,
            modifiers,
            factory.GetGeneratedPrivateNameForNode(factory.store.Name(node), prefix: null, "_accessor_storage"),
            default,
            default,
            initializer);
    }

    /// <summary>Creates a GetAccessorDeclaration that reads from a private backing field.</summary>
    internal static Node CreateAccessorPropertyGetRedirector(NodeFactory factory, Node node, NodeList modifiers, Node name, Node receiver = default)
    {
        if (receiver.IsNone)
        {
            receiver = factory.CreateToken(SyntaxKind.ThisKeyword);
        }
        return factory.CreateGetAccessorDeclaration(
            modifiers,
            name,
            factory.CreateNodeArray(ReadOnlySpan<Node>.Empty),
            default,
            factory.CreateBlock(factory.CreateNodeArray(new[]
            {
                factory.CreateReturnStatement(
                    factory.CreatePropertyAccessExpression(
                        receiver,
                        factory.GetGeneratedPrivateNameForNode(factory.store.Name(node), prefix: null, "_accessor_storage"))),
            })));
    }

    /// <summary>Creates a SetAccessorDeclaration that writes to a private backing field.</summary>
    internal static Node CreateAccessorPropertySetRedirector(NodeFactory factory, Node node, NodeList modifiers, Node name, Node receiver = default)
    {
        if (receiver.IsNone)
        {
            receiver = factory.CreateToken(SyntaxKind.ThisKeyword);
        }
        return factory.CreateSetAccessorDeclaration(
            modifiers,
            name,
            factory.CreateNodeArray(new[] { factory.CreateParameterDeclaration(default, default, factory.CreateIdentifier("value")) }),
            factory.CreateBlock(factory.CreateNodeArray(new[]
            {
                factory.CreateExpressionStatement(
                    factory.CreateAssignment(
                        factory.CreatePropertyAccessExpression(
                            receiver,
                            factory.GetGeneratedPrivateNameForNode(factory.store.Name(node), prefix: null, "_accessor_storage")),
                        factory.CreateIdentifier("value"))),
            })));
    }

    /// <summary>findComputedPropertyNameCacheAssignment: the `_a = expr` assignment of a generated name a computed name caches.</summary>
    internal static Node FindComputedPropertyNameCacheAssignment(NodeStore store, Node name)
    {
        var node = store.Expression(name);
        while (true)
        {
            node = SkipOuterExpressions(store, node);
            if (store.Kind(node) == SyntaxKind.CommaListExpression)
            {
                var elements = store.Elements(node);
                node = store.Element(elements, store.Length(elements) - 1);
                continue;
            }

            if (IsCommaExpression(store, node))
            {
                node = store.Right(node);
                continue;
            }

            if (Utilities.IsAssignmentExpression(store, node, excludeCompoundAssignment: true) && UtilitiesPublic.IsGeneratedIdentifier(store, store.Left(node)))
            {
                return node;
            }

            break;
        }
        return Node.None;
    }

    private static bool IsSyntheticParenthesizedExpression(NodeStore store, Node node)
    {
        return store.Kind(node) == SyntaxKind.ParenthesizedExpression
            && Utilities.NodeIsSynthesized(store, node)
            && store.EmitNode(node) is null;
    }

    private static void FlattenCommaListWorker(NodeStore store, Node node, List<Node> expressions)
    {
        if (IsSyntheticParenthesizedExpression(store, node))
        {
            FlattenCommaListWorker(store, store.Expression(node), expressions);
        }
        else if (IsCommaExpression(store, node))
        {
            FlattenCommaListWorker(store, store.Left(node), expressions);
            FlattenCommaListWorker(store, store.Right(node), expressions);
        }
        else if (store.Kind(node) == SyntaxKind.CommaListExpression)
        {
            foreach (var child in store.Nodes(store.Elements(node)))
            {
                FlattenCommaListWorker(store, child, expressions);
            }
        }
        else
        {
            expressions.Add(node);
        }
    }

    /// <summary>
    /// Flatten a CommaExpression or CommaListExpression into an array of one or more expressions, unwrapping any nested
    /// comma expressions and synthetic parens.
    /// </summary>
    internal static List<Node> FlattenCommaList(NodeStore store, Node node)
    {
        var expressions = new List<Node>();
        FlattenCommaListWorker(store, node, expressions);
        return expressions;
    }

    /// <summary>emitHelpers.ts isCallToHelper.</summary>
    internal static bool IsCallToHelper(NodeStore store, Node firstSegment, string helperName)
    {
        if (store.Kind(firstSegment) != SyntaxKind.CallExpression)
        {
            return false;
        }
        var expression = store.Expression(firstSegment);
        return store.Kind(expression) == SyntaxKind.Identifier
            && (Utilities.GetEmitFlags(store, expression) & EmitFlags.HelperName) != 0
            && store.Texts.Equals(store.EscapedText(expression), helperName);
    }
}
