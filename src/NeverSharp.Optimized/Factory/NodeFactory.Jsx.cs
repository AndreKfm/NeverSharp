// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>nodeFactory.ts: JSX, clauses and property assignments.</summary>
internal sealed partial class NodeFactory
{
    //
    // JSX
    //

    // @api
    internal Node CreateJsxElement(Node openingElement, NodeList children, Node closingElement)
    {
        var node = CreateBaseNode(SyntaxKind.JsxElement);
        store.SetOpeningElement(node, openingElement);
        store.SetChildren(node, CreateNodeArray(store, children));
        store.SetClosingElement(node, closingElement);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.OpeningElement(node)) |
            PropagateChildrenFlags(store.Children(node)) |
            PropagateChildFlags(store, store.ClosingElement(node)) |
            TransformFlags.ContainsJsx));
        return node;
    }

    // @api
    internal Node CreateJsxSelfClosingElement(Node tagName, NodeList typeArguments, Node attributes)
    {
        var node = CreateBaseNode(SyntaxKind.JsxSelfClosingElement);
        store.SetTagName(node, tagName);
        store.SetTypeArguments(node, AsNodeArray(store, typeArguments));
        store.SetAttributes(node, attributes);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.TagName(node)) |
            PropagateChildrenFlags(store.TypeArguments(node)) |
            PropagateChildFlags(store, store.Attributes(node)) |
            TransformFlags.ContainsJsx));
        if (!store.TypeArguments(node).IsNone)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsTypeScript);
        }
        return node;
    }

    // @api
    internal Node CreateJsxOpeningElement(Node tagName, NodeList typeArguments, Node attributes)
    {
        var node = CreateBaseNode(SyntaxKind.JsxOpeningElement);
        store.SetTagName(node, tagName);
        store.SetTypeArguments(node, AsNodeArray(store, typeArguments));
        store.SetAttributes(node, attributes);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.TagName(node)) |
            PropagateChildrenFlags(store.TypeArguments(node)) |
            PropagateChildFlags(store, store.Attributes(node)) |
            TransformFlags.ContainsJsx));
        if (!typeArguments.IsNone)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsTypeScript);
        }
        return node;
    }

    // @api
    internal Node CreateJsxClosingElement(Node tagName)
    {
        var node = CreateBaseNode(SyntaxKind.JsxClosingElement);
        store.SetTagName(node, tagName);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.TagName(node)) |
            TransformFlags.ContainsJsx));
        return node;
    }

    // @api
    internal Node CreateJsxFragment(Node openingFragment, NodeList children, Node closingFragment)
    {
        var node = CreateBaseNode(SyntaxKind.JsxFragment);
        store.SetOpeningFragment(node, openingFragment);
        store.SetChildren(node, CreateNodeArray(store, children));
        store.SetClosingFragment(node, closingFragment);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.OpeningFragment(node)) |
            PropagateChildrenFlags(store.Children(node)) |
            PropagateChildFlags(store, store.ClosingFragment(node)) |
            TransformFlags.ContainsJsx));
        return node;
    }

    // @api
    internal Node CreateJsxText(string text, bool? containsOnlyTriviaWhiteSpaces = null)
    {
        var node = CreateBaseNode(SyntaxKind.JsxText);
        store.SetText(node, text);
        store.SetContainsOnlyTriviaWhiteSpaces(node, containsOnlyTriviaWhiteSpaces == true);
        store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsJsx);
        return node;
    }

    // @api
    internal Node CreateJsxOpeningFragment()
    {
        var node = CreateBaseNode(SyntaxKind.JsxOpeningFragment);
        store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsJsx);
        return node;
    }

    // @api
    internal Node CreateJsxJsxClosingFragment()
    {
        var node = CreateBaseNode(SyntaxKind.JsxClosingFragment);
        store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsJsx);
        return node;
    }

    // @api
    internal Node CreateJsxAttribute(Node name, Node initializer)
    {
        var node = CreateBaseDeclaration(SyntaxKind.JsxAttribute);
        store.SetName(node, name);
        store.SetInitializer(node, initializer);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Name(node)) |
            PropagateChildFlags(store, store.Initializer(node)) |
            TransformFlags.ContainsJsx));
        return node;
    }

    // @api
    internal Node CreateJsxAttributes(NodeList properties)
    {
        var node = CreateBaseDeclaration(SyntaxKind.JsxAttributes);
        store.SetProperties(node, CreateNodeArray(store, properties));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildrenFlags(store.Properties(node)) |
            TransformFlags.ContainsJsx));
        return node;
    }

    // @api
    internal Node CreateJsxSpreadAttribute(Node expression)
    {
        var node = CreateBaseNode(SyntaxKind.JsxSpreadAttribute);
        store.SetExpression(node, expression);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            TransformFlags.ContainsJsx));
        return node;
    }

    // @api
    internal Node CreateJsxExpression(Node dotDotDotToken, Node expression)
    {
        var node = CreateBaseNode(SyntaxKind.JsxExpression);
        store.SetDotDotDotToken(node, dotDotDotToken);
        store.SetExpression(node, expression);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.DotDotDotToken(node)) |
            PropagateChildFlags(store, store.Expression(node)) |
            TransformFlags.ContainsJsx));
        return node;
    }

    // @api
    internal Node CreateJsxNamespacedName(Node @namespace, Node name)
    {
        var node = CreateBaseNode(SyntaxKind.JsxNamespacedName);
        store.SetNamespace(node, @namespace);
        store.SetName(node, name);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Namespace(node)) |
            PropagateChildFlags(store, store.Name(node)) |
            TransformFlags.ContainsJsx));
        return node;
    }

    //
    // Clauses
    //

    // @api
    internal Node CreateCaseClause(Node expression, NodeList statements)
    {
        var node = CreateBaseNode(SyntaxKind.CaseClause);
        store.SetExpression(node, Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression));
        store.SetStatements(node, CreateNodeArray(store, statements));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildrenFlags(store.Statements(node))));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateDefaultClause(NodeList statements)
    {
        var node = CreateBaseNode(SyntaxKind.DefaultClause);
        store.SetStatements(node, CreateNodeArray(store, statements));
        store.SetTransformFlags(node, PropagateChildrenFlags(store.Statements(node)));
        return node;
    }

    // @api
    internal Node CreateHeritageClause(SyntaxKind token, NodeList types)
    {
        var node = CreateBaseNode(SyntaxKind.HeritageClause);
        store.SetToken(node, token);
        store.SetTypes(node, CreateNodeArray(store, types));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildrenFlags(store.Types(node)));
        switch (token)
        {
            case SyntaxKind.ExtendsKeyword:
                store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2015);
                break;
            case SyntaxKind.ImplementsKeyword:
                store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsTypeScript);
                break;
            default:
                Debug.AssertNever(token);
                break;
        }
        return node;
    }

    /// <summary>`createCatchClause(variableDeclaration: string | BindingName | VariableDeclaration | undefined, block)`.</summary>
    // @api
    internal Node CreateCatchClause(object? variableDeclaration, Node block)
    {
        // Track O: a node handle passed as an object; None is undefined.
        variableDeclaration = variableDeclaration is Node { IsNone: true } ? null : variableDeclaration;
        var node = CreateBaseNode(SyntaxKind.CatchClause);
        store.SetVariableDeclaration(node, AsVariableDeclaration(variableDeclaration));
        store.SetBlock(node, block);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.VariableDeclaration(node)) |
            PropagateChildFlags(store, store.Block(node)) |
            (variableDeclaration == null || variableDeclaration is "" ? TransformFlags.ContainsES2019 : TransformFlags.None)));
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    //
    // Property assignments
    //

    // @api
    internal Node CreatePropertyAssignment(object name, Node initializer)
    {
        var node = CreateBaseDeclaration(SyntaxKind.PropertyAssignment);
        store.SetName(node, AsName(name));
        store.SetInitializer(node, Parenthesizer.ParenthesizeExpressionForDisallowedComma(initializer));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateNameFlags(store, store.Name(node)) |
            PropagateChildFlags(store, store.Initializer(node))));
        store.SetModifiers(node, default); // initialized by parser to report grammar errors
        store.SetQuestionToken(node, default); // initialized by parser to report grammar errors
        store.SetExclamationToken(node, default); // initialized by parser to report grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateShorthandPropertyAssignment(object name, Node objectAssignmentInitializer = default)
    {
        var node = CreateBaseDeclaration(SyntaxKind.ShorthandPropertyAssignment);
        store.SetName(node, AsName(name));
        store.SetObjectAssignmentInitializer(node, !objectAssignmentInitializer.IsNone ? Parenthesizer.ParenthesizeExpressionForDisallowedComma(objectAssignmentInitializer) : default);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateIdentifierNameFlags(store, store.Name(node)) |
            PropagateChildFlags(store, store.ObjectAssignmentInitializer(node)) |
            TransformFlags.ContainsES2015));
        store.SetEqualsToken(node, default); // initialized by parser to report grammar errors
        store.SetModifiers(node, default); // initialized by parser to report grammar errors
        store.SetQuestionToken(node, default); // initialized by parser to report grammar errors
        store.SetExclamationToken(node, default); // initialized by parser to report grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateSpreadAssignment(Node expression)
    {
        var node = CreateBaseDeclaration(SyntaxKind.SpreadAssignment);
        store.SetExpression(node, Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            TransformFlags.ContainsES2018 |
            TransformFlags.ContainsObjectRestOrSpread));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    //
    // Enum
    //

    // @api
    internal Node CreateEnumMember(object name, Node initializer = default)
    {
        var node = CreateBaseDeclaration(SyntaxKind.EnumMember);
        store.SetName(node, AsName(name));
        store.SetInitializer(node, !initializer.IsNone ? Parenthesizer.ParenthesizeExpressionForDisallowedComma(initializer) : default);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Name(node)) |
            PropagateChildFlags(store, store.Initializer(node)) |
            TransformFlags.ContainsTypeScript));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }
}
