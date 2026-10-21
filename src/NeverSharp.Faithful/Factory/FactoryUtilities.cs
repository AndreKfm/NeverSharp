// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/utilities.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>factory/utilities.ts. TypeScript's unions of nodes (Expression, BindingOrAssignmentElement, ...) are Node here.</summary>
internal static partial class FactoryUtilities
{
    /// <summary>
    /// Gets whether an identifier should only be referred to by its local name.
    /// </summary>
    internal static bool IsLocalName(Node node)
    {
        return (Utilities.GetEmitFlags(node) & EmitFlags.LocalName) != 0;
    }

    internal static bool IsCommaExpression(Node node)
    {
        return node.Kind == SyntaxKind.BinaryExpression && ((BinaryExpression)node).OperatorToken.Kind == SyntaxKind.CommaToken;
    }

    internal static bool IsCommaSequence(Node node)
    {
        return IsCommaExpression(node) || NodeTests.IsCommaListExpression(node);
    }

    internal static bool IsJSDocTypeAssertion(Node node)
    {
        return NodeTests.IsParenthesizedExpression(node)
            && Utilities.IsInJSFile(node)
            && UtilitiesPublic.GetJSDocTypeTag(node) != null;
    }

    internal static bool IsOuterExpression(Node node, OuterExpressionKinds kinds = OuterExpressionKinds.All)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                if ((kinds & OuterExpressionKinds.ExcludeJSDocTypeAssertion) != 0 && IsJSDocTypeAssertion(node))
                {
                    return false;
                }
                return (kinds & OuterExpressionKinds.Parentheses) != 0;
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.AsExpression:
                return (kinds & OuterExpressionKinds.TypeAssertions) != 0;
            case SyntaxKind.SatisfiesExpression:
                return (kinds & (OuterExpressionKinds.TypeAssertions | OuterExpressionKinds.Satisfies)) != 0;
            case SyntaxKind.ExpressionWithTypeArguments:
                return (kinds & OuterExpressionKinds.ExpressionsWithTypeArguments) != 0;
            case SyntaxKind.NonNullExpression:
                return (kinds & OuterExpressionKinds.NonNullAssertions) != 0;
            case SyntaxKind.PartiallyEmittedExpression:
                return (kinds & OuterExpressionKinds.PartiallyEmittedExpressions) != 0;
        }
        return false;
    }

    /// <summary>
    /// TypeScript reads `node.expression` of a node that passed `isOuterExpression`
    /// (ParenthesizedExpression | TypeAssertion | AsExpression | SatisfiesExpression | NonNullExpression |
    /// ExpressionWithTypeArguments | PartiallyEmittedExpression).
    /// </summary>
    internal static Node GetExpressionOfOuterExpression(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                return ((ParenthesizedExpression)node).Expression;
            case SyntaxKind.TypeAssertionExpression:
                return ((TypeAssertion)node).Expression;
            case SyntaxKind.AsExpression:
                return ((AsExpression)node).Expression;
            case SyntaxKind.SatisfiesExpression:
                return ((SatisfiesExpression)node).Expression;
            case SyntaxKind.ExpressionWithTypeArguments:
                return ((ExpressionWithTypeArguments)node).Expression;
            case SyntaxKind.NonNullExpression:
                return ((NonNullExpression)node).Expression;
            case SyntaxKind.PartiallyEmittedExpression:
                return ((PartiallyEmittedExpression)node).Expression;
        }

        // TypeScript reads `undefined`: unreachable for a node that passed isOuterExpression.
        return null!;
    }

    internal static Node SkipOuterExpressions(Node node, OuterExpressionKinds kinds = OuterExpressionKinds.All)
    {
        while (IsOuterExpression(node, kinds))
        {
            node = GetExpressionOfOuterExpression(node);
        }
        return node;
    }

    /// <summary>
    /// Gets the name of an BindingOrAssignmentElement.
    /// </summary>
    internal static Node? GetTargetOfBindingOrAssignmentElement(Node bindingElement)
    {
        if (UtilitiesPublic.IsDeclarationBindingElement(bindingElement))
        {
            // `a` in `let { a } = ...`
            // `a` in `let { a = 1 } = ...`
            // `b` in `let { a: b } = ...`
            // `b` in `let { a: b = 1 } = ...`
            // `a` in `let { ...a } = ...`
            // `{b}` in `let { a: {b} } = ...`
            // `{b}` in `let { a: {b} = 1 } = ...`
            // `[b]` in `let { a: [b] } = ...`
            // `[b]` in `let { a: [b] = 1 } = ...`
            // `a` in `let [a] = ...`
            // `a` in `let [a = 1] = ...`
            // `a` in `let [...a] = ...`
            // `{a}` in `let [{a}] = ...`
            // `{a}` in `let [{a} = 1] = ...`
            // `[a]` in `let [[a]] = ...`
            // `[a]` in `let [[a] = 1] = ...`
            // (VariableDeclaration | ParameterDeclaration | BindingElement)
            switch (bindingElement.Kind)
            {
                case SyntaxKind.VariableDeclaration:
                    return ((VariableDeclaration)bindingElement).Name;
                case SyntaxKind.Parameter:
                    return ((ParameterDeclaration)bindingElement).Name;
                default:
                    return ((BindingElement)bindingElement).Name;
            }
        }

        if (UtilitiesPublic.IsObjectLiteralElementLike(bindingElement))
        {
            switch (bindingElement.Kind)
            {
                case SyntaxKind.PropertyAssignment:
                    // `b` in `({ a: b } = ...)`
                    // `b` in `({ a: b = 1 } = ...)`
                    // `{b}` in `({ a: {b} } = ...)`
                    // `{b}` in `({ a: {b} = 1 } = ...)`
                    // `[b]` in `({ a: [b] } = ...)`
                    // `[b]` in `({ a: [b] = 1 } = ...)`
                    // `b.c` in `({ a: b.c } = ...)`
                    // `b.c` in `({ a: b.c = 1 } = ...)`
                    // `b[0]` in `({ a: b[0] } = ...)`
                    // `b[0]` in `({ a: b[0] = 1 } = ...)`
                    return GetTargetOfBindingOrAssignmentElement(((PropertyAssignment)bindingElement).Initializer);

                case SyntaxKind.ShorthandPropertyAssignment:
                    // `a` in `({ a } = ...)`
                    // `a` in `({ a = 1 } = ...)`
                    return ((ShorthandPropertyAssignment)bindingElement).Name;

                case SyntaxKind.SpreadAssignment:
                    // `a` in `({ ...a } = ...)`
                    return GetTargetOfBindingOrAssignmentElement(((SpreadAssignment)bindingElement).Expression);
            }

            // no target
            return null;
        }

        if (Utilities.IsAssignmentExpression(bindingElement, /*excludeCompoundAssignment*/ true))
        {
            // `a` in `[a = 1] = ...`
            // `{a}` in `[{a} = 1] = ...`
            // `[a]` in `[[a] = 1] = ...`
            // `a.b` in `[a.b = 1] = ...`
            // `a[0]` in `[a[0] = 1] = ...`
            return GetTargetOfBindingOrAssignmentElement(((BinaryExpression)bindingElement).Left);
        }

        if (NodeTests.IsSpreadElement(bindingElement))
        {
            // `a` in `[...a] = ...`
            return GetTargetOfBindingOrAssignmentElement(((SpreadElement)bindingElement).Expression);
        }

        // `a` in `[a] = ...`
        // `{a}` in `[{a}] = ...`
        // `[a]` in `[[a]] = ...`
        // `a.b` in `[a.b] = ...`
        // `a[0]` in `[a[0]] = ...`
        return bindingElement;
    }

    /// <summary>
    /// Gets the elements of a BindingOrAssignmentPattern
    /// </summary>
    internal static NodeArray GetElementsOfBindingOrAssignmentPattern(Node name)
    {
        switch (name.Kind)
        {
            case SyntaxKind.ObjectBindingPattern:
                // `a` in `{a}`
                return ((ObjectBindingPattern)name).Elements;
            case SyntaxKind.ArrayBindingPattern:
                // `a` in `[a]`
                return ((ArrayBindingPattern)name).Elements;
            case SyntaxKind.ArrayLiteralExpression:
                // `a` in `[a]`
                return ((ArrayLiteralExpression)name).Elements;

            case SyntaxKind.ObjectLiteralExpression:
                // `a` in `{a}`
                return ((ObjectLiteralExpression)name).Properties;
        }

        // TypeScript falls off the end (returns undefined): unreachable for a BindingOrAssignmentPattern.
        return null!;
    }

    internal static Identifier? GetJSDocTypeAliasName(Node? fullName)
    {
        if (fullName != null)
        {
            var rightNode = fullName;
            while (true)
            {
                if (NodeTests.IsIdentifier(rightNode) || ((ModuleDeclaration)rightNode).Body == null)
                {
                    return NodeTests.IsIdentifier(rightNode) ? (Identifier)rightNode : (Identifier)((ModuleDeclaration)rightNode).Name;
                }
                rightNode = ((ModuleDeclaration)rightNode).Body!;
            }
        }
        return null;
    }

    /// <summary>
    /// Walk an AssignmentPattern to determine if it contains object rest (`...`) syntax. We cannot rely on
    /// propagation of `TransformFlags.ContainsObjectRestOrSpread` since it isn't propagated by default in
    /// ObjectLiteralExpression and ArrayLiteralExpression since we do not know whether they belong to an
    /// AssignmentPattern at the time the nodes are parsed.
    /// </summary>
    internal static bool ContainsObjectRestOrSpread(Node node)
    {
        if ((node.TransformFlags & TransformFlags.ContainsObjectRestOrSpread) != 0)
        {
            return true;
        }
        if ((node.TransformFlags & TransformFlags.ContainsES2018) != 0)
        {
            // check for nested spread assignments, otherwise '{ x: { a, ...b } = foo } = c'
            // will not be correctly interpreted by the ES2018 transformer
            var elements = GetElementsOfBindingOrAssignmentPattern(node);
            for (var i = 0; i < elements.Count; i++)
            {
                var target = GetTargetOfBindingOrAssignmentElement(elements[i]);
                if (target != null && UtilitiesPublic.IsAssignmentPattern(target))
                {
                    if ((target.TransformFlags & TransformFlags.ContainsObjectRestOrSpread) != 0)
                    {
                        return true;
                    }
                    if ((target.TransformFlags & TransformFlags.ContainsES2018) != 0)
                    {
                        if (ContainsObjectRestOrSpread(target))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }
}
