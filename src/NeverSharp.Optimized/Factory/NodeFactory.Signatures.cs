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

/// <summary>nodeFactory.ts: names, signature elements, type elements and class elements.</summary>
internal sealed partial class NodeFactory
{
    //
    // Names
    //

    // @api
    internal QualifiedName CreateQualifiedName(Node left, object right)
    {
        var node = CreateBaseNode<QualifiedName>(SyntaxKind.QualifiedName);
        node.Left = left;
        node.Right = (Identifier)AsName(right)!;
        node.TransformFlags |= PropagateChildFlags(node.Left) |
            PropagateIdentifierNameFlags(node.Right);
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal ComputedPropertyName CreateComputedPropertyName(Node expression)
    {
        var node = CreateBaseNode<ComputedPropertyName>(SyntaxKind.ComputedPropertyName);
        node.Expression = Parenthesizer.ParenthesizeExpressionOfComputedPropertyName(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsComputedPropertyName;
        return node;
    }

    //
    // Signature elements
    //

    // @api
    internal TypeParameterDeclaration CreateTypeParameterDeclaration(IReadOnlyList<Node>? modifiers, object name, Node? constraint = null, Node? defaultType = null)
    {
        var node = CreateBaseDeclaration<TypeParameterDeclaration>(SyntaxKind.TypeParameter);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = (Identifier)AsName(name)!;
        node.Constraint = constraint;
        node.Default = defaultType;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        node.Expression = null; // initialized by parser to report grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal ParameterDeclaration CreateParameterDeclaration(
        IReadOnlyList<Node>? modifiers,
        DotDotDotToken? dotDotDotToken,
        object name,
        QuestionToken? questionToken = null,
        Node? type = null,
        Node? initializer = null)
    {
        var node = CreateBaseDeclaration<ParameterDeclaration>(SyntaxKind.Parameter);
        node.Modifiers = AsNodeArray(modifiers);
        node.DotDotDotToken = dotDotDotToken;
        node.Name = AsName(name)!;
        node.QuestionToken = questionToken;
        node.Type = type;
        node.Initializer = AsInitializer(initializer);
        if (Utilities.IsThisIdentifier(node.Name))
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            node.TransformFlags = PropagateChildrenFlags(node.Modifiers) |
                PropagateChildFlags(node.DotDotDotToken) |
                PropagateNameFlags(node.Name) |
                PropagateChildFlags(node.QuestionToken) |
                PropagateChildFlags(node.Initializer) |
                ((node.QuestionToken ?? node.Type) != null ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
                ((node.DotDotDotToken ?? node.Initializer) != null ? TransformFlags.ContainsES2015 : TransformFlags.None) |
                ((Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.ParameterPropertyModifier) != 0 ? TransformFlags.ContainsTypeScriptClassSyntax : TransformFlags.None);
        }
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Decorator CreateDecorator(Node expression)
    {
        var node = CreateBaseNode<Decorator>(SyntaxKind.Decorator);
        node.Expression = Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsTypeScript |
            TransformFlags.ContainsTypeScriptClassSyntax |
            TransformFlags.ContainsDecorators;
        return node;
    }

    //
    // Type Elements
    //

    // @api
    internal PropertySignature CreatePropertySignature(
        IReadOnlyList<Node>? modifiers,
        object name,
        QuestionToken? questionToken,
        Node? type)
    {
        var node = CreateBaseDeclaration<PropertySignature>(SyntaxKind.PropertySignature);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name)!;
        node.Type = type;
        node.QuestionToken = questionToken;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        node.Initializer = null; // initialized by parser to report grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal PropertyDeclaration CreatePropertyDeclaration(
        IReadOnlyList<Node>? modifiers,
        object name,
        Node? questionOrExclamationToken,
        Node? type,
        Node? initializer)
    {
        var node = CreateBaseDeclaration<PropertyDeclaration>(SyntaxKind.PropertyDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name)!;
        node.QuestionToken = questionOrExclamationToken != null && FactoryNodeTests.IsQuestionToken(questionOrExclamationToken) ? (QuestionToken)questionOrExclamationToken : null;
        node.ExclamationToken = questionOrExclamationToken != null && FactoryNodeTests.IsExclamationToken(questionOrExclamationToken) ? (ExclamationToken)questionOrExclamationToken : null;
        node.Type = type;
        node.Initializer = AsInitializer(initializer);
        var isAmbient = (node.Flags & NodeFlags.Ambient) != 0 || (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Ambient) != 0;
        node.TransformFlags = PropagateChildrenFlags(node.Modifiers) |
            PropagateNameFlags(node.Name) |
            PropagateChildFlags(node.Initializer) |
            (isAmbient || node.QuestionToken != null || node.ExclamationToken != null || node.Type != null ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
            (FactoryNodeTests.IsComputedPropertyName(node.Name) || (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Static) != 0 && node.Initializer != null ? TransformFlags.ContainsTypeScriptClassSyntax : TransformFlags.None) |
            TransformFlags.ContainsClassFields;
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal MethodSignature CreateMethodSignature(
        IReadOnlyList<Node>? modifiers,
        object name,
        QuestionToken? questionToken,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node? type)
    {
        var node = CreateBaseDeclaration<MethodSignature>(SyntaxKind.MethodSignature);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name)!;
        node.QuestionToken = questionToken;
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = AsNodeArray(parameters)!;
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.typeArguments = undefined; // used in quick info
        return node;
    }

    // @api
    internal MethodDeclaration CreateMethodDeclaration(
        IReadOnlyList<Node>? modifiers,
        AsteriskToken? asteriskToken,
        object name,
        QuestionToken? questionToken,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node? type,
        Block? body)
    {
        var node = CreateBaseDeclaration<MethodDeclaration>(SyntaxKind.MethodDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.AsteriskToken = asteriskToken;
        node.Name = AsName(name)!;
        node.QuestionToken = questionToken;
        node.ExclamationToken = null; // initialized by parser for grammar errors
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = CreateNodeArray(parameters);
        node.Type = type;
        node.Body = body;
        if (node.Body == null)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            var isAsync = (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Async) != 0;
            var isGenerator = node.AsteriskToken != null;
            var isAsyncGenerator = isAsync && isGenerator;
            node.TransformFlags = PropagateChildrenFlags(node.Modifiers) |
                PropagateChildFlags(node.AsteriskToken) |
                PropagateNameFlags(node.Name) |
                PropagateChildFlags(node.QuestionToken) |
                PropagateChildrenFlags(node.TypeParameters) |
                PropagateChildrenFlags(node.Parameters) |
                PropagateChildFlags(node.Type) |
                (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
                (isAsyncGenerator ? TransformFlags.ContainsES2018 :
                    isAsync ? TransformFlags.ContainsES2017 :
                    isGenerator ? TransformFlags.ContainsGenerator :
                    TransformFlags.None) |
                (node.QuestionToken != null || node.TypeParameters != null || node.Type != null ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
                TransformFlags.ContainsES2015;
        }
        // node.typeArguments = undefined; // used in quick info
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        // node.endFlowNode = undefined;
        // node.returnFlowNode = undefined;
        return node;
    }

    // @api
    internal ClassStaticBlockDeclaration CreateClassStaticBlockDeclaration(
        Block body)
    {
        var node = CreateBaseDeclaration<ClassStaticBlockDeclaration>(SyntaxKind.ClassStaticBlockDeclaration);
        node.Body = body;
        node.TransformFlags = PropagateChildFlags(body) | TransformFlags.ContainsClassFields;
        node.Modifiers = null; // initialized by parser for grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.endFlowNode = undefined;
        // node.returnFlowNode = undefined;
        return node;
    }

    // @api
    internal ConstructorDeclaration CreateConstructorDeclaration(
        IReadOnlyList<Node>? modifiers,
        IReadOnlyList<Node> parameters,
        Block? body)
    {
        var node = CreateBaseDeclaration<ConstructorDeclaration>(SyntaxKind.Constructor);
        node.Modifiers = AsNodeArray(modifiers);
        node.Parameters = CreateNodeArray(parameters);
        node.Body = body;
        if (node.Body == null)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            node.TransformFlags = PropagateChildrenFlags(node.Modifiers) |
                PropagateChildrenFlags(node.Parameters) |
                (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
                TransformFlags.ContainsES2015;
        }
        node.TypeParameters = null; // initialized by parser for grammar errors
        node.Type = null; // initialized by parser for grammar errors
        // node.typeArguments = undefined; // used in quick info
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.endFlowNode = undefined;
        // node.returnFlowNode = undefined;
        return node;
    }

    // @api
    internal GetAccessorDeclaration CreateGetAccessorDeclaration(
        IReadOnlyList<Node>? modifiers,
        object name,
        IReadOnlyList<Node> parameters,
        Node? type,
        Block? body)
    {
        var node = CreateBaseDeclaration<GetAccessorDeclaration>(SyntaxKind.GetAccessor);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name)!;
        node.Parameters = CreateNodeArray(parameters);
        node.Type = type;
        node.Body = body;
        if (node.Body == null)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            node.TransformFlags = PropagateChildrenFlags(node.Modifiers) |
                PropagateNameFlags(node.Name) |
                PropagateChildrenFlags(node.Parameters) |
                PropagateChildFlags(node.Type) |
                (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
                (node.Type != null ? TransformFlags.ContainsTypeScript : TransformFlags.None);
        }
        // node.typeArguments = undefined; // used in quick info
        node.TypeParameters = null; // initialized by parser for grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        // node.endFlowNode = undefined;
        // node.returnFlowNode = undefined;
        return node;
    }

    // @api
    internal SetAccessorDeclaration CreateSetAccessorDeclaration(
        IReadOnlyList<Node>? modifiers,
        object name,
        IReadOnlyList<Node> parameters,
        Block? body)
    {
        var node = CreateBaseDeclaration<SetAccessorDeclaration>(SyntaxKind.SetAccessor);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name)!;
        node.Parameters = CreateNodeArray(parameters);
        node.Body = body;
        if (node.Body == null)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            node.TransformFlags = PropagateChildrenFlags(node.Modifiers) |
                PropagateNameFlags(node.Name) |
                PropagateChildrenFlags(node.Parameters) |
                (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
                (node.Type != null ? TransformFlags.ContainsTypeScript : TransformFlags.None);
        }
        // node.typeArguments = undefined; // used in quick info
        node.TypeParameters = null; // initialized by parser for grammar errors
        node.Type = null; // initialized by parser for grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        // node.endFlowNode = undefined;
        // node.returnFlowNode = undefined;
        return node;
    }

    // @api
    internal CallSignatureDeclaration CreateCallSignature(
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node? type)
    {
        var node = CreateBaseDeclaration<CallSignatureDeclaration>(SyntaxKind.CallSignature);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = AsNodeArray(parameters)!;
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.typeArguments = undefined; // used in quick info
        return node;
    }

    // @api
    internal ConstructSignatureDeclaration CreateConstructSignature(
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node? type)
    {
        var node = CreateBaseDeclaration<ConstructSignatureDeclaration>(SyntaxKind.ConstructSignature);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = AsNodeArray(parameters)!;
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.typeArguments = undefined; // used in quick info
        return node;
    }

    // @api
    internal IndexSignatureDeclaration CreateIndexSignature(
        IReadOnlyList<Node>? modifiers,
        IReadOnlyList<Node> parameters,
        Node? type)
    {
        var node = CreateBaseDeclaration<IndexSignatureDeclaration>(SyntaxKind.IndexSignature);
        node.Modifiers = AsNodeArray(modifiers);
        node.Parameters = AsNodeArray(parameters)!;
        node.Type = type!; // TODO(rbuckton): We mark this as required in IndexSignatureDeclaration, but it looks like the parser allows it to be elided.
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.typeArguments = undefined; // used in quick info
        return node;
    }

    // @api
    internal TemplateLiteralTypeSpan CreateTemplateLiteralTypeSpan(Node type, Node literal)
    {
        var node = CreateBaseNode<TemplateLiteralTypeSpan>(SyntaxKind.TemplateLiteralTypeSpan);
        node.Type = type;
        node.Literal = literal;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }
}
