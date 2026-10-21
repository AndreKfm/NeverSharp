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

namespace NeverSharp.Faithful;

/// <summary>nodeFactory.ts: JSX, clauses and property assignments.</summary>
internal sealed partial class NodeFactory
{
    //
    // JSX
    //

    // @api
    internal JsxElement CreateJsxElement(JsxOpeningElement openingElement, IReadOnlyList<Node> children, JsxClosingElement closingElement)
    {
        var node = CreateBaseNode<JsxElement>(SyntaxKind.JsxElement);
        node.OpeningElement = openingElement;
        node.Children = CreateNodeArray(children);
        node.ClosingElement = closingElement;
        node.TransformFlags |= PropagateChildFlags(node.OpeningElement) |
            PropagateChildrenFlags(node.Children) |
            PropagateChildFlags(node.ClosingElement) |
            TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxSelfClosingElement CreateJsxSelfClosingElement(Node tagName, IReadOnlyList<Node>? typeArguments, JsxAttributes attributes)
    {
        var node = CreateBaseNode<JsxSelfClosingElement>(SyntaxKind.JsxSelfClosingElement);
        node.TagName = tagName;
        node.TypeArguments = AsNodeArray(typeArguments);
        node.Attributes = attributes;
        node.TransformFlags |= PropagateChildFlags(node.TagName) |
            PropagateChildrenFlags(node.TypeArguments) |
            PropagateChildFlags(node.Attributes) |
            TransformFlags.ContainsJsx;
        if (node.TypeArguments != null)
        {
            node.TransformFlags |= TransformFlags.ContainsTypeScript;
        }
        return node;
    }

    // @api
    internal JsxOpeningElement CreateJsxOpeningElement(Node tagName, IReadOnlyList<Node>? typeArguments, JsxAttributes attributes)
    {
        var node = CreateBaseNode<JsxOpeningElement>(SyntaxKind.JsxOpeningElement);
        node.TagName = tagName;
        node.TypeArguments = AsNodeArray(typeArguments);
        node.Attributes = attributes;
        node.TransformFlags |= PropagateChildFlags(node.TagName) |
            PropagateChildrenFlags(node.TypeArguments) |
            PropagateChildFlags(node.Attributes) |
            TransformFlags.ContainsJsx;
        if (typeArguments != null)
        {
            node.TransformFlags |= TransformFlags.ContainsTypeScript;
        }
        return node;
    }

    // @api
    internal JsxClosingElement CreateJsxClosingElement(Node tagName)
    {
        var node = CreateBaseNode<JsxClosingElement>(SyntaxKind.JsxClosingElement);
        node.TagName = tagName;
        node.TransformFlags |= PropagateChildFlags(node.TagName) |
            TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxFragment CreateJsxFragment(JsxOpeningFragment openingFragment, IReadOnlyList<Node> children, JsxClosingFragment closingFragment)
    {
        var node = CreateBaseNode<JsxFragment>(SyntaxKind.JsxFragment);
        node.OpeningFragment = openingFragment;
        node.Children = CreateNodeArray(children);
        node.ClosingFragment = closingFragment;
        node.TransformFlags |= PropagateChildFlags(node.OpeningFragment) |
            PropagateChildrenFlags(node.Children) |
            PropagateChildFlags(node.ClosingFragment) |
            TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxText CreateJsxText(string text, bool? containsOnlyTriviaWhiteSpaces = null)
    {
        var node = CreateBaseNode<JsxText>(SyntaxKind.JsxText);
        node.Text = text;
        node.ContainsOnlyTriviaWhiteSpaces = containsOnlyTriviaWhiteSpaces == true;
        node.TransformFlags |= TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxOpeningFragment CreateJsxOpeningFragment()
    {
        var node = CreateBaseNode<JsxOpeningFragment>(SyntaxKind.JsxOpeningFragment);
        node.TransformFlags |= TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxClosingFragment CreateJsxJsxClosingFragment()
    {
        var node = CreateBaseNode<JsxClosingFragment>(SyntaxKind.JsxClosingFragment);
        node.TransformFlags |= TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxAttribute CreateJsxAttribute(Node name, Node? initializer)
    {
        var node = CreateBaseDeclaration<JsxAttribute>(SyntaxKind.JsxAttribute);
        node.Name = name;
        node.Initializer = initializer;
        node.TransformFlags |= PropagateChildFlags(node.Name) |
            PropagateChildFlags(node.Initializer) |
            TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxAttributes CreateJsxAttributes(IReadOnlyList<Node> properties)
    {
        var node = CreateBaseDeclaration<JsxAttributes>(SyntaxKind.JsxAttributes);
        node.Properties = CreateNodeArray(properties);
        node.TransformFlags |= PropagateChildrenFlags(node.Properties) |
            TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxSpreadAttribute CreateJsxSpreadAttribute(Node expression)
    {
        var node = CreateBaseNode<JsxSpreadAttribute>(SyntaxKind.JsxSpreadAttribute);
        node.Expression = expression;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxExpression CreateJsxExpression(DotDotDotToken? dotDotDotToken, Node? expression)
    {
        var node = CreateBaseNode<JsxExpression>(SyntaxKind.JsxExpression);
        node.DotDotDotToken = dotDotDotToken;
        node.Expression = expression;
        node.TransformFlags |= PropagateChildFlags(node.DotDotDotToken) |
            PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxNamespacedName CreateJsxNamespacedName(Identifier @namespace, Identifier name)
    {
        var node = CreateBaseNode<JsxNamespacedName>(SyntaxKind.JsxNamespacedName);
        node.Namespace = @namespace;
        node.Name = name;
        node.TransformFlags |= PropagateChildFlags(node.Namespace) |
            PropagateChildFlags(node.Name) |
            TransformFlags.ContainsJsx;
        return node;
    }

    //
    // Clauses
    //

    // @api
    internal CaseClause CreateCaseClause(Node expression, IReadOnlyList<Node> statements)
    {
        var node = CreateBaseNode<CaseClause>(SyntaxKind.CaseClause);
        node.Expression = Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression);
        node.Statements = CreateNodeArray(statements);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildrenFlags(node.Statements);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal DefaultClause CreateDefaultClause(IReadOnlyList<Node> statements)
    {
        var node = CreateBaseNode<DefaultClause>(SyntaxKind.DefaultClause);
        node.Statements = CreateNodeArray(statements);
        node.TransformFlags = PropagateChildrenFlags(node.Statements);
        return node;
    }

    // @api
    internal HeritageClause CreateHeritageClause(SyntaxKind token, IReadOnlyList<Node> types)
    {
        var node = CreateBaseNode<HeritageClause>(SyntaxKind.HeritageClause);
        node.Token = token;
        node.Types = CreateNodeArray(types);
        node.TransformFlags |= PropagateChildrenFlags(node.Types);
        switch (token)
        {
            case SyntaxKind.ExtendsKeyword:
                node.TransformFlags |= TransformFlags.ContainsES2015;
                break;
            case SyntaxKind.ImplementsKeyword:
                node.TransformFlags |= TransformFlags.ContainsTypeScript;
                break;
            default:
                Debug.AssertNever(token);
                break;
        }
        return node;
    }

    /// <summary>`createCatchClause(variableDeclaration: string | BindingName | VariableDeclaration | undefined, block)`.</summary>
    // @api
    internal CatchClause CreateCatchClause(object? variableDeclaration, Block block)
    {
        var node = CreateBaseNode<CatchClause>(SyntaxKind.CatchClause);
        node.VariableDeclaration = AsVariableDeclaration(variableDeclaration);
        node.Block = block;
        node.TransformFlags |= PropagateChildFlags(node.VariableDeclaration) |
            PropagateChildFlags(node.Block) |
            (variableDeclaration == null || variableDeclaration is "" ? TransformFlags.ContainsES2019 : TransformFlags.None);
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    //
    // Property assignments
    //

    // @api
    internal PropertyAssignment CreatePropertyAssignment(object name, Node initializer)
    {
        var node = CreateBaseDeclaration<PropertyAssignment>(SyntaxKind.PropertyAssignment);
        node.Name = AsName(name)!;
        node.Initializer = Parenthesizer.ParenthesizeExpressionForDisallowedComma(initializer);
        node.TransformFlags |= PropagateNameFlags(node.Name) |
            PropagateChildFlags(node.Initializer);
        node.Modifiers = null; // initialized by parser to report grammar errors
        node.QuestionToken = null; // initialized by parser to report grammar errors
        node.ExclamationToken = null; // initialized by parser to report grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal ShorthandPropertyAssignment CreateShorthandPropertyAssignment(object name, Node? objectAssignmentInitializer = null)
    {
        var node = CreateBaseDeclaration<ShorthandPropertyAssignment>(SyntaxKind.ShorthandPropertyAssignment);
        node.Name = (Identifier)AsName(name)!;
        node.ObjectAssignmentInitializer = objectAssignmentInitializer != null ? Parenthesizer.ParenthesizeExpressionForDisallowedComma(objectAssignmentInitializer) : null;
        node.TransformFlags |= PropagateIdentifierNameFlags(node.Name) |
            PropagateChildFlags(node.ObjectAssignmentInitializer) |
            TransformFlags.ContainsES2015;
        node.EqualsToken = null; // initialized by parser to report grammar errors
        node.Modifiers = null; // initialized by parser to report grammar errors
        node.QuestionToken = null; // initialized by parser to report grammar errors
        node.ExclamationToken = null; // initialized by parser to report grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal SpreadAssignment CreateSpreadAssignment(Node expression)
    {
        var node = CreateBaseDeclaration<SpreadAssignment>(SyntaxKind.SpreadAssignment);
        node.Expression = Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsES2018 |
            TransformFlags.ContainsObjectRestOrSpread;
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    //
    // Enum
    //

    // @api
    internal EnumMember CreateEnumMember(object name, Node? initializer = null)
    {
        var node = CreateBaseDeclaration<EnumMember>(SyntaxKind.EnumMember);
        node.Name = AsName(name)!;
        node.Initializer = initializer != null ? Parenthesizer.ParenthesizeExpressionForDisallowedComma(initializer) : null;
        node.TransformFlags |= PropagateChildFlags(node.Name) |
            PropagateChildFlags(node.Initializer) |
            TransformFlags.ContainsTypeScript;
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }
}
