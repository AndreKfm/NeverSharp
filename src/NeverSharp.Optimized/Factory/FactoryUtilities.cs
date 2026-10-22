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

namespace NeverSharp.Optimized;

/// <summary>factory/utilities.ts (the units the parser reaches).</summary>
internal static partial class FactoryUtilities
{
    /// <summary>
    /// Gets whether an identifier should only be referred to by its local name.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsLocalName(NodeStore store, Node node)
    {
        return (Utilities.GetEmitFlags(store, node) & EmitFlags.LocalName) != 0;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsCommaExpression(NodeStore store, Node node)
    {
        return store.Kind(node) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(node)) == SyntaxKind.CommaToken;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsCommaSequence(NodeStore store, Node node)
    {
        return IsCommaExpression(store, node) || FactoryNodeTests.IsCommaListExpression(store, node);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocTypeAssertion(NodeStore store, Node node)
    {
        return FactoryNodeTests.IsParenthesizedExpression(store, node)
            && Utilities.IsInJSFile(store, node)
            && !UtilitiesPublic.GetJSDocTypeTag(store, node).IsNone;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsOuterExpression(NodeStore store, Node node, OuterExpressionKinds kinds = OuterExpressionKinds.All)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ParenthesizedExpression:
                if ((kinds & OuterExpressionKinds.ExcludeJSDocTypeAssertion) != 0 && IsJSDocTypeAssertion(store, node))
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node SkipOuterExpressions(NodeStore store, Node node, OuterExpressionKinds kinds = OuterExpressionKinds.All)
    {
        while (IsOuterExpression(store, node, kinds))
        {
            // `node = node.expression` (every OuterExpression kind declares `expression`)
            node = store.Kind(node) switch
            {
                SyntaxKind.ParenthesizedExpression => store.Expression(node),
                SyntaxKind.TypeAssertionExpression => store.Expression(node),
                SyntaxKind.AsExpression => store.Expression(node),
                SyntaxKind.SatisfiesExpression => store.Expression(node),
                SyntaxKind.ExpressionWithTypeArguments => store.Expression(node),
                SyntaxKind.NonNullExpression => store.Expression(node),
                _ => store.Expression(node),
            };
        }
        return node;
    }

    /// <summary>
    /// Gets the name of an BindingOrAssignmentElement.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetTargetOfBindingOrAssignmentElement(NodeStore store, Node bindingElement)
    {
        if (UtilitiesPublic.IsDeclarationBindingElement(store, bindingElement))
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
            return store.Name(bindingElement);
        }

        if (UtilitiesPublic.IsObjectLiteralElementLike(store, bindingElement))
        {
            switch (store.Kind(bindingElement))
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
                    return GetTargetOfBindingOrAssignmentElement(store, store.Initializer(bindingElement));

                case SyntaxKind.ShorthandPropertyAssignment:
                    // `a` in `({ a } = ...)`
                    // `a` in `({ a = 1 } = ...)`
                    return store.Name(bindingElement);

                case SyntaxKind.SpreadAssignment:
                    // `a` in `({ ...a } = ...)`
                    return GetTargetOfBindingOrAssignmentElement(store, store.Expression(bindingElement));
            }

            // no target
            return default;
        }

        if (Utilities.IsAssignmentExpression(store, bindingElement, /*excludeCompoundAssignment*/ true))
        {
            // `a` in `[a = 1] = ...`
            // `{a}` in `[{a} = 1] = ...`
            // `[a]` in `[[a] = 1] = ...`
            // `a.b` in `[a.b = 1] = ...`
            // `a[0]` in `[a[0] = 1] = ...`
            return GetTargetOfBindingOrAssignmentElement(store, store.Left(bindingElement));
        }

        if (FactoryNodeTests.IsSpreadElement(store, bindingElement))
        {
            // `a` in `[...a] = ...`
            return GetTargetOfBindingOrAssignmentElement(store, store.Expression(bindingElement));
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static NodeList GetElementsOfBindingOrAssignmentPattern(NodeStore store, Node name)
    {
        switch (store.Kind(name))
        {
            case SyntaxKind.ObjectBindingPattern:
                // `a` in `{a}`
                return store.Elements(name);
            case SyntaxKind.ArrayBindingPattern:
                // `a` in `[a]`
                return store.Elements(name);
            case SyntaxKind.ArrayLiteralExpression:
                return store.Elements(name);

            case SyntaxKind.ObjectLiteralExpression:
                // `a` in `{a}`
                return store.Properties(name);
        }
        // (TypeScript's switch is exhaustive over BindingOrAssignmentPattern and returns undefined otherwise.)
        return default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetJSDocTypeAliasName(NodeStore store, Node fullName)
    {
        if (!fullName.IsNone)
        {
            var rightNode = fullName;
            while (true)
            {
                if (FactoryNodeTests.IsIdentifier(store, rightNode) || store.Body(rightNode).IsNone)
                {
                    return FactoryNodeTests.IsIdentifier(store, rightNode) ? rightNode : store.Name(rightNode);
                }
                rightNode = store.Body(rightNode);
            }
        }
        return default;
    }

    /// <summary>
    /// Walk an AssignmentPattern to determine if it contains object rest (`...`) syntax. We cannot rely on
    /// propagation of `TransformFlags.ContainsObjectRestOrSpread` since it isn't propagated by default in
    /// ObjectLiteralExpression and ArrayLiteralExpression since we do not know whether they belong to an
    /// AssignmentPattern at the time the nodes are parsed.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool ContainsObjectRestOrSpread(NodeStore store, Node node)
    {
        if ((store.TransformFlags(node) & TransformFlags.ContainsObjectRestOrSpread) != 0)
        {
            return true;
        }
        if ((store.TransformFlags(node) & TransformFlags.ContainsES2018) != 0)
        {
            // check for nested spread assignments, otherwise '{ x: { a, ...b } = foo } = c'
            // will not be correctly interpreted by the ES2018 transformer
            foreach (var element in store.Nodes(GetElementsOfBindingOrAssignmentPattern(store, node)))
            {
                var target = GetTargetOfBindingOrAssignmentElement(store, element);
                if (!target.IsNone && UtilitiesPublic.IsAssignmentPattern(store, target))
                {
                    if ((store.TransformFlags(target) & TransformFlags.ContainsObjectRestOrSpread) != 0)
                    {
                        return true;
                    }
                    if ((store.TransformFlags(target) & TransformFlags.ContainsES2018) != 0)
                    {
                        if (ContainsObjectRestOrSpread(store, target))
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
