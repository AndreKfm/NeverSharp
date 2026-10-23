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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateQualifiedName(Node left, Node right)
    {
        // (Track O, step O12, here and in the functions below: the fields and the transform flags are computed from the
        // arguments first, then the record is written in one go by the generated NodeStore.New{Class}; the flags of
        // the new node, which TypeScript's code ORs in or tests, are none at that point. No other node is created in
        // between.)
        var rightNode = AsName(right);
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return store.NewQualifiedName(
            PropagateChildFlags(store, left) |
                PropagateIdentifierNameFlags(store, rightNode),
            left: left,
            right: rightNode);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTypeParameterDeclaration(NodeList modifiers, Node name, Node constraint = default, Node defaultType = default)
    {
        // (expression: undefined, initialized by parser to report grammar errors)
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return store.NewTypeParameterDeclaration(
            TransformFlags.ContainsTypeScript,
            modifiers: AsNodeArray(store, modifiers),
            name: AsName(name),
            constraint: constraint,
            @default: defaultType);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateParameterDeclaration(
        NodeList modifiers,
        Node dotDotDotToken,
        Node name,
        Node questionToken = default,
        Node type = default,
        Node initializer = default)
    {
        var modifiersArray = AsNodeArray(store, modifiers);
        var nameNode = AsName(name);
        var initializerNode = AsInitializer(initializer);
        TransformFlags transformFlags;
        if (Utilities.IsThisIdentifier(store, nameNode))
        {
            transformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            transformFlags = PropagateChildrenFlags(modifiersArray) |
                PropagateChildFlags(store, dotDotDotToken) |
                PropagateNameFlags(store, nameNode) |
                PropagateChildFlags(store, questionToken) |
                PropagateChildFlags(store, initializerNode) |
                (!questionToken.IsNone || !type.IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
                (!dotDotDotToken.IsNone || !initializerNode.IsNone ? TransformFlags.ContainsES2015 : TransformFlags.None) |
                ((Utilities.ModifiersToFlags(store, modifiersArray) & ModifierFlags.ParameterPropertyModifier) != 0 ? TransformFlags.ContainsTypeScriptClassSyntax : TransformFlags.None);
        }
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return store.NewParameterDeclaration(
            transformFlags,
            modifiers: modifiersArray,
            dotDotDotToken: dotDotDotToken,
            name: nameNode,
            questionToken: questionToken,
            type: type,
            initializer: initializerNode);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreatePropertySignature(
        NodeList modifiers,
        Node name,
        Node questionToken,
        Node type)
    {
        // (initializer: undefined, initialized by parser to report grammar errors)
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return store.NewPropertySignature(
            TransformFlags.ContainsTypeScript,
            modifiers: AsNodeArray(store, modifiers),
            name: AsName(name),
            type: type,
            questionToken: questionToken);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreatePropertyDeclaration(
        NodeList modifiers,
        Node name,
        Node questionOrExclamationToken,
        Node type,
        Node initializer)
    {
        var modifiersArray = AsNodeArray(store, modifiers);
        var nameNode = AsName(name);
        var questionToken = !questionOrExclamationToken.IsNone && FactoryNodeTests.IsQuestionToken(store, questionOrExclamationToken) ? questionOrExclamationToken : default;
        var exclamationToken = !questionOrExclamationToken.IsNone && FactoryNodeTests.IsExclamationToken(store, questionOrExclamationToken) ? questionOrExclamationToken : default;
        var initializerNode = AsInitializer(initializer);
        // (node.flags & NodeFlags.Ambient: a new node has no flags.)
        var isAmbient = (Utilities.ModifiersToFlags(store, modifiersArray) & ModifierFlags.Ambient) != 0;
        var transformFlags = PropagateChildrenFlags(modifiersArray) |
            PropagateNameFlags(store, nameNode) |
            PropagateChildFlags(store, initializerNode) |
            (isAmbient || !questionToken.IsNone || !exclamationToken.IsNone || !type.IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
            (FactoryNodeTests.IsComputedPropertyName(store, nameNode) || (Utilities.ModifiersToFlags(store, modifiersArray) & ModifierFlags.Static) != 0 && !initializerNode.IsNone ? TransformFlags.ContainsTypeScriptClassSyntax : TransformFlags.None) |
            TransformFlags.ContainsClassFields;
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return store.NewPropertyDeclaration(
            transformFlags,
            modifiers: modifiersArray,
            name: nameNode,
            questionToken: questionToken,
            exclamationToken: exclamationToken,
            type: type,
            initializer: initializerNode);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateMethodSignature(
        NodeList modifiers,
        Node name,
        Node questionToken,
        NodeList typeParameters,
        NodeList parameters,
        Node type)
    {
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.typeArguments = undefined; // used in quick info
        return store.NewMethodSignature(
            TransformFlags.ContainsTypeScript,
            modifiers: AsNodeArray(store, modifiers),
            name: AsName(name),
            questionToken: questionToken,
            typeParameters: AsNodeArray(store, typeParameters),
            parameters: AsNodeArray(store, parameters),
            type: type);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateMethodDeclaration(
        NodeList modifiers,
        Node asteriskToken,
        Node name,
        Node questionToken,
        NodeList typeParameters,
        NodeList parameters,
        Node type,
        Node body)
    {
        var modifiersArray = AsNodeArray(store, modifiers);
        var nameNode = AsName(name);
        var typeParametersArray = AsNodeArray(store, typeParameters);
        var parametersArray = CreateNodeArray(store, parameters);
        TransformFlags transformFlags;
        if (body.IsNone)
        {
            transformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            var isAsync = (Utilities.ModifiersToFlags(store, modifiersArray) & ModifierFlags.Async) != 0;
            var isGenerator = !asteriskToken.IsNone;
            var isAsyncGenerator = isAsync && isGenerator;
            transformFlags = PropagateChildrenFlags(modifiersArray) |
                PropagateChildFlags(store, asteriskToken) |
                PropagateNameFlags(store, nameNode) |
                PropagateChildFlags(store, questionToken) |
                PropagateChildrenFlags(typeParametersArray) |
                PropagateChildrenFlags(parametersArray) |
                PropagateChildFlags(store, type) |
                (PropagateChildFlags(store, body) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
                (isAsyncGenerator ? TransformFlags.ContainsES2018 :
                    isAsync ? TransformFlags.ContainsES2017 :
                    isGenerator ? TransformFlags.ContainsGenerator :
                    TransformFlags.None) |
                (!questionToken.IsNone || !typeParametersArray.IsNone || !type.IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
                TransformFlags.ContainsES2015;
        }
        // (exclamationToken: undefined, initialized by parser for grammar errors)
        var node = store.NewMethodDeclaration(
            transformFlags,
            modifiers: modifiersArray,
            asteriskToken: asteriskToken,
            name: nameNode,
            questionToken: questionToken,
            typeParameters: typeParametersArray,
            parameters: parametersArray,
            type: type,
            body: body);
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateGetAccessorDeclaration(
        NodeList modifiers,
        Node name,
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateSetAccessorDeclaration(
        NodeList modifiers,
        Node name,
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTemplateLiteralTypeSpan(Node type, Node literal)
    {
        var node = CreateBaseNode(SyntaxKind.TemplateLiteralTypeSpan);
        store.SetType(node, type);
        store.SetLiteral(node, literal);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }
}
