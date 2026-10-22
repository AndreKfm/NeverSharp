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
    internal Node CreateQualifiedName(Node left, object right)
    {
        var node = CreateBaseNode(SyntaxKind.QualifiedName);
        store.SetLeft(node, left);
        store.SetRight(node, AsName(right));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Left(node)) |
            PropagateIdentifierNameFlags(store, store.Right(node))));
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateComputedPropertyName(Node expression)
    {
        var node = CreateBaseNode(SyntaxKind.ComputedPropertyName);
        store.SetExpression(node, Parenthesizer.ParenthesizeExpressionOfComputedPropertyName(expression));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsComputedPropertyName));
        return node;
    }

    //
    // Signature elements
    //

    // @api
    internal Node CreateTypeParameterDeclaration(NodeList modifiers, object name, Node constraint = default, Node defaultType = default)
    {
        var node = CreateBaseDeclaration(SyntaxKind.TypeParameter);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetName(node, AsName(name));
        store.SetConstraint(node, constraint);
        store.SetDefault(node, defaultType);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        store.SetExpression(node, default); // initialized by parser to report grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateParameterDeclaration(
        NodeList modifiers,
        Node dotDotDotToken,
        object name,
        Node questionToken = default,
        Node type = default,
        Node initializer = default)
    {
        var node = CreateBaseDeclaration(SyntaxKind.Parameter);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetDotDotDotToken(node, dotDotDotToken);
        store.SetName(node, AsName(name));
        store.SetQuestionToken(node, questionToken);
        store.SetType(node, type);
        store.SetInitializer(node, AsInitializer(initializer));
        if (Utilities.IsThisIdentifier(store, store.Name(node)))
        {
            store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        }
        else
        {
            store.SetTransformFlags(node, PropagateChildrenFlags(store.Modifiers(node)) |
                PropagateChildFlags(store, store.DotDotDotToken(node)) |
                PropagateNameFlags(store, store.Name(node)) |
                PropagateChildFlags(store, store.QuestionToken(node)) |
                PropagateChildFlags(store, store.Initializer(node)) |
                (!((store.QuestionToken(node) is { IsNone: false } __t1 ? __t1 : store.Type(node))).IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
                (!((store.DotDotDotToken(node) is { IsNone: false } __t2 ? __t2 : store.Initializer(node))).IsNone ? TransformFlags.ContainsES2015 : TransformFlags.None) |
                ((Utilities.ModifiersToFlags(store, store.Modifiers(node)) & ModifierFlags.ParameterPropertyModifier) != 0 ? TransformFlags.ContainsTypeScriptClassSyntax : TransformFlags.None));
        }
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateDecorator(Node expression)
    {
        var node = CreateBaseNode(SyntaxKind.Decorator);
        store.SetExpression(node, Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            TransformFlags.ContainsTypeScript |
            TransformFlags.ContainsTypeScriptClassSyntax |
            TransformFlags.ContainsDecorators));
        return node;
    }

    //
    // Type Elements
    //

    // @api
    internal Node CreatePropertySignature(
        NodeList modifiers,
        object name,
        Node questionToken,
        Node type)
    {
        var node = CreateBaseDeclaration(SyntaxKind.PropertySignature);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetName(node, AsName(name));
        store.SetType(node, type);
        store.SetQuestionToken(node, questionToken);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        store.SetInitializer(node, default); // initialized by parser to report grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreatePropertyDeclaration(
        NodeList modifiers,
        object name,
        Node questionOrExclamationToken,
        Node type,
        Node initializer)
    {
        var node = CreateBaseDeclaration(SyntaxKind.PropertyDeclaration);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetName(node, AsName(name));
        store.SetQuestionToken(node, !questionOrExclamationToken.IsNone && FactoryNodeTests.IsQuestionToken(store, questionOrExclamationToken) ? questionOrExclamationToken : default);
        store.SetExclamationToken(node, !questionOrExclamationToken.IsNone && FactoryNodeTests.IsExclamationToken(store, questionOrExclamationToken) ? questionOrExclamationToken : default);
        store.SetType(node, type);
        store.SetInitializer(node, AsInitializer(initializer));
        var isAmbient = (store.Flags(node) & NodeFlags.Ambient) != 0 || (Utilities.ModifiersToFlags(store, store.Modifiers(node)) & ModifierFlags.Ambient) != 0;
        store.SetTransformFlags(node, PropagateChildrenFlags(store.Modifiers(node)) |
            PropagateNameFlags(store, store.Name(node)) |
            PropagateChildFlags(store, store.Initializer(node)) |
            (isAmbient || !store.QuestionToken(node).IsNone || !store.ExclamationToken(node).IsNone || !store.Type(node).IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
            (FactoryNodeTests.IsComputedPropertyName(store, store.Name(node)) || (Utilities.ModifiersToFlags(store, store.Modifiers(node)) & ModifierFlags.Static) != 0 && !store.Initializer(node).IsNone ? TransformFlags.ContainsTypeScriptClassSyntax : TransformFlags.None) |
            TransformFlags.ContainsClassFields);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateMethodSignature(
        NodeList modifiers,
        object name,
        Node questionToken,
        NodeList typeParameters,
        NodeList parameters,
        Node type)
    {
        var node = CreateBaseDeclaration(SyntaxKind.MethodSignature);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetName(node, AsName(name));
        store.SetQuestionToken(node, questionToken);
        store.SetTypeParameters(node, AsNodeArray(store, typeParameters));
        store.SetParameters(node, AsNodeArray(store, parameters));
        store.SetType(node, type);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.typeArguments = undefined; // used in quick info
        return node;
    }

    // @api
    internal Node CreateMethodDeclaration(
        NodeList modifiers,
        Node asteriskToken,
        object name,
        Node questionToken,
        NodeList typeParameters,
        NodeList parameters,
        Node type,
        Node body)
    {
        var node = CreateBaseDeclaration(SyntaxKind.MethodDeclaration);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetAsteriskToken(node, asteriskToken);
        store.SetName(node, AsName(name));
        store.SetQuestionToken(node, questionToken);
        store.SetExclamationToken(node, default); // initialized by parser for grammar errors
        store.SetTypeParameters(node, AsNodeArray(store, typeParameters));
        store.SetParameters(node, CreateNodeArray(store, parameters));
        store.SetType(node, type);
        store.SetBody(node, body);
        if (store.Body(node).IsNone)
        {
            store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        }
        else
        {
            var isAsync = (Utilities.ModifiersToFlags(store, store.Modifiers(node)) & ModifierFlags.Async) != 0;
            var isGenerator = !store.AsteriskToken(node).IsNone;
            var isAsyncGenerator = isAsync && isGenerator;
            store.SetTransformFlags(node, PropagateChildrenFlags(store.Modifiers(node)) |
                PropagateChildFlags(store, store.AsteriskToken(node)) |
                PropagateNameFlags(store, store.Name(node)) |
                PropagateChildFlags(store, store.QuestionToken(node)) |
                PropagateChildrenFlags(store.TypeParameters(node)) |
                PropagateChildrenFlags(store.Parameters(node)) |
                PropagateChildFlags(store, store.Type(node)) |
                (PropagateChildFlags(store, store.Body(node)) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
                (isAsyncGenerator ? TransformFlags.ContainsES2018 :
                    isAsync ? TransformFlags.ContainsES2017 :
                    isGenerator ? TransformFlags.ContainsGenerator :
                    TransformFlags.None) |
                (!store.QuestionToken(node).IsNone || !store.TypeParameters(node).IsNone || !store.Type(node).IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
                TransformFlags.ContainsES2015);
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
    internal Node CreateClassStaticBlockDeclaration(
        Node body)
    {
        var node = CreateBaseDeclaration(SyntaxKind.ClassStaticBlockDeclaration);
        store.SetBody(node, body);
        store.SetTransformFlags(node, PropagateChildFlags(store, body) | TransformFlags.ContainsClassFields);
        store.SetModifiers(node, default); // initialized by parser for grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.endFlowNode = undefined;
        // node.returnFlowNode = undefined;
        return node;
    }

    // @api
    internal Node CreateConstructorDeclaration(
        NodeList modifiers,
        NodeList parameters,
        Node body)
    {
        var node = CreateBaseDeclaration(SyntaxKind.Constructor);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetParameters(node, CreateNodeArray(store, parameters));
        store.SetBody(node, body);
        if (store.Body(node).IsNone)
        {
            store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        }
        else
        {
            store.SetTransformFlags(node, PropagateChildrenFlags(store.Modifiers(node)) |
                PropagateChildrenFlags(store.Parameters(node)) |
                (PropagateChildFlags(store, store.Body(node)) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
                TransformFlags.ContainsES2015);
        }
        store.SetTypeParameters(node, default); // initialized by parser for grammar errors
        store.SetType(node, default); // initialized by parser for grammar errors
        // node.typeArguments = undefined; // used in quick info
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.endFlowNode = undefined;
        // node.returnFlowNode = undefined;
        return node;
    }

    // @api
    internal Node CreateGetAccessorDeclaration(
        NodeList modifiers,
        object name,
        NodeList parameters,
        Node type,
        Node body)
    {
        var node = CreateBaseDeclaration(SyntaxKind.GetAccessor);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetName(node, AsName(name));
        store.SetParameters(node, CreateNodeArray(store, parameters));
        store.SetType(node, type);
        store.SetBody(node, body);
        if (store.Body(node).IsNone)
        {
            store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        }
        else
        {
            store.SetTransformFlags(node, PropagateChildrenFlags(store.Modifiers(node)) |
                PropagateNameFlags(store, store.Name(node)) |
                PropagateChildrenFlags(store.Parameters(node)) |
                PropagateChildFlags(store, store.Type(node)) |
                (PropagateChildFlags(store, store.Body(node)) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
                (!store.Type(node).IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None));
        }
        // node.typeArguments = undefined; // used in quick info
        store.SetTypeParameters(node, default); // initialized by parser for grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        // node.endFlowNode = undefined;
        // node.returnFlowNode = undefined;
        return node;
    }

    // @api
    internal Node CreateSetAccessorDeclaration(
        NodeList modifiers,
        object name,
        NodeList parameters,
        Node body)
    {
        var node = CreateBaseDeclaration(SyntaxKind.SetAccessor);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetName(node, AsName(name));
        store.SetParameters(node, CreateNodeArray(store, parameters));
        store.SetBody(node, body);
        if (store.Body(node).IsNone)
        {
            store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        }
        else
        {
            store.SetTransformFlags(node, PropagateChildrenFlags(store.Modifiers(node)) |
                PropagateNameFlags(store, store.Name(node)) |
                PropagateChildrenFlags(store.Parameters(node)) |
                (PropagateChildFlags(store, store.Body(node)) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
                (!store.Type(node).IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None));
        }
        // node.typeArguments = undefined; // used in quick info
        store.SetTypeParameters(node, default); // initialized by parser for grammar errors
        store.SetType(node, default); // initialized by parser for grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        // node.endFlowNode = undefined;
        // node.returnFlowNode = undefined;
        return node;
    }

    // @api
    internal Node CreateCallSignature(
        NodeList typeParameters,
        NodeList parameters,
        Node type)
    {
        var node = CreateBaseDeclaration(SyntaxKind.CallSignature);
        store.SetTypeParameters(node, AsNodeArray(store, typeParameters));
        store.SetParameters(node, AsNodeArray(store, parameters));
        store.SetType(node, type);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.typeArguments = undefined; // used in quick info
        return node;
    }

    // @api
    internal Node CreateConstructSignature(
        NodeList typeParameters,
        NodeList parameters,
        Node type)
    {
        var node = CreateBaseDeclaration(SyntaxKind.ConstructSignature);
        store.SetTypeParameters(node, AsNodeArray(store, typeParameters));
        store.SetParameters(node, AsNodeArray(store, parameters));
        store.SetType(node, type);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.typeArguments = undefined; // used in quick info
        return node;
    }

    // @api
    internal Node CreateIndexSignature(
        NodeList modifiers,
        NodeList parameters,
        Node type)
    {
        var node = CreateBaseDeclaration(SyntaxKind.IndexSignature);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetParameters(node, AsNodeArray(store, parameters));
        store.SetType(node, type); // TODO(rbuckton): We mark this as required in IndexSignatureDeclaration, but it looks like the parser allows it to be elided.
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.typeArguments = undefined; // used in quick info
        return node;
    }

    // @api
    internal Node CreateTemplateLiteralTypeSpan(Node type, Node literal)
    {
        var node = CreateBaseNode(SyntaxKind.TemplateLiteralTypeSpan);
        store.SetType(node, type);
        store.SetLiteral(node, literal);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }
}
