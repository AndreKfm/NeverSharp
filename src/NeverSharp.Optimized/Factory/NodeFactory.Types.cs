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

/// <summary>nodeFactory.ts: types and binding patterns.</summary>
internal sealed partial class NodeFactory
{
    //
    // Types
    //

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTypePredicateNode(Node assertsModifier, Node parameterName, Node type)
    {
        var node = CreateBaseNode(SyntaxKind.TypePredicate);
        store.SetAssertsModifier(node, assertsModifier);
        store.SetParameterName(node, AsName(parameterName));
        store.SetType(node, type);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTypeReferenceNode(Node typeName, NodeList typeArguments)
    {
        var node = CreateBaseNode(SyntaxKind.TypeReference);
        store.SetTypeName(node, AsName(typeName));
        store.SetTypeArguments(node, !typeArguments.IsNone ? Parenthesizer.ParenthesizeTypeArguments(CreateNodeArray(store, typeArguments)) : default);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateFunctionTypeNode(
        NodeList typeParameters,
        NodeList parameters,
        Node type)
    {
        var node = CreateBaseDeclaration(SyntaxKind.FunctionType);
        store.SetTypeParameters(node, AsNodeArray(store, typeParameters));
        store.SetParameters(node, AsNodeArray(store, parameters));
        store.SetType(node, type);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        store.SetModifiers(node, default); // initialized by parser for grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.typeArguments = undefined; // used in quick info
        return node;
    }

    /// <summary>
    /// TypeScript: `createConstructorTypeNode(...args)` dispatches on the number of arguments: 4 to
    /// createConstructorTypeNode1, 3 to createConstructorTypeNode2. The two C# overloads are that dispatch.
    /// </summary>
    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateConstructorTypeNode(NodeList modifiers, NodeList typeParameters, NodeList parameters, Node type)
    {
        return CreateConstructorTypeNode1(modifiers, typeParameters, parameters, type);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateConstructorTypeNode(NodeList typeParameters, NodeList parameters, Node type)
    {
        return CreateConstructorTypeNode2(typeParameters, parameters, type);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateConstructorTypeNode1(
        NodeList modifiers,
        NodeList typeParameters,
        NodeList parameters,
        Node type)
    {
        var node = CreateBaseDeclaration(SyntaxKind.ConstructorType);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
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

    /// <summary>Deprecated.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateConstructorTypeNode2(
        NodeList typeParameters,
        NodeList parameters,
        Node type)
    {
        return CreateConstructorTypeNode1(/*modifiers*/ default, typeParameters, parameters, type);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTypeQueryNode(Node exprName, NodeList typeArguments = default)
    {
        var node = CreateBaseNode(SyntaxKind.TypeQuery);
        store.SetExprName(node, exprName);
        store.SetTypeArguments(node, !typeArguments.IsNone ? Parenthesizer.ParenthesizeTypeArguments(typeArguments) : default);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTypeLiteralNode(NodeList members)
    {
        var node = CreateBaseDeclaration(SyntaxKind.TypeLiteral);
        store.SetMembers(node, CreateNodeArray(store, members));
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateArrayTypeNode(Node elementType)
    {
        var node = CreateBaseNode(SyntaxKind.ArrayType);
        store.SetElementType(node, Parenthesizer.ParenthesizeNonArrayTypeOfPostfixType(elementType));
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTupleTypeNode(NodeList elements)
    {
        var node = CreateBaseNode(SyntaxKind.TupleType);
        store.SetElements(node, CreateNodeArray(store, Parenthesizer.ParenthesizeElementTypesOfTupleType(elements)));
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateNamedTupleMember(Node dotDotDotToken, Node name, Node questionToken, Node type)
    {
        var node = CreateBaseDeclaration(SyntaxKind.NamedTupleMember);
        store.SetDotDotDotToken(node, dotDotDotToken);
        store.SetName(node, name);
        store.SetQuestionToken(node, questionToken);
        store.SetType(node, type);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateOptionalTypeNode(Node type)
    {
        var node = CreateBaseNode(SyntaxKind.OptionalType);
        store.SetType(node, Parenthesizer.ParenthesizeTypeOfOptionalType(type));
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateRestTypeNode(Node type)
    {
        var node = CreateBaseNode(SyntaxKind.RestType);
        store.SetType(node, type);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // Track O (O-NA): `parenthesize` (parenthesizerRules().parenthesizeConstituentTypesOfUnionType or ...OfIntersectionType)
    // is chosen by the kind instead of passed as a delegate.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateUnionOrIntersectionTypeNode(SyntaxKind kind, NodeList types)
    {
        var node = CreateBaseNode(kind);
        var typesArray = CreateNodeArray(store, kind == SyntaxKind.UnionType ? Parenthesizer.ParenthesizeConstituentTypesOfUnionType(types) : Parenthesizer.ParenthesizeConstituentTypesOfIntersectionType(types));
        if ((node is var union && store.IsUnionTypeNode(union)))
        {
            store.SetTypes(union, typesArray);
        }
        else
        {
            store.SetTypes(node, typesArray);
        }
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateUnionTypeNode(NodeList types)
    {
        return CreateUnionOrIntersectionTypeNode(SyntaxKind.UnionType, types);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateIntersectionTypeNode(NodeList types)
    {
        return CreateUnionOrIntersectionTypeNode(SyntaxKind.IntersectionType, types);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateConditionalTypeNode(Node checkType, Node extendsType, Node trueType, Node falseType)
    {
        var node = CreateBaseNode(SyntaxKind.ConditionalType);
        store.SetCheckType(node, Parenthesizer.ParenthesizeCheckTypeOfConditionalType(checkType));
        store.SetExtendsType(node, Parenthesizer.ParenthesizeExtendsTypeOfConditionalType(extendsType));
        store.SetTrueType(node, trueType);
        store.SetFalseType(node, falseType);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateInferTypeNode(Node typeParameter)
    {
        var node = CreateBaseNode(SyntaxKind.InferType);
        store.SetTypeParameter(node, typeParameter);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTemplateLiteralType(Node head, NodeList templateSpans)
    {
        var node = CreateBaseNode(SyntaxKind.TemplateLiteralType);
        store.SetHead(node, head);
        store.SetTemplateSpans(node, CreateNodeArray(store, templateSpans));
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateImportTypeNode(
        Node argument,
        Node attributes = default,
        Node qualifier = default,
        NodeList typeArguments = default,
        bool isTypeOf = false)
    {
        var node = CreateBaseNode(SyntaxKind.ImportType);
        store.SetArgument(node, argument);
        store.SetAttributes(node, attributes);
        // TypeScript: `if (node.assertions && node.assertions.assertClause && node.attributes) { ... }`; a new node has
        // no `assertions` (the deprecated property is not declared on the generated class), so the test is false.
        store.SetQualifier(node, qualifier);
        store.SetTypeArguments(node, !typeArguments.IsNone ? Parenthesizer.ParenthesizeTypeArguments(typeArguments) : default);
        store.SetIsTypeOf(node, isTypeOf);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateParenthesizedType(Node type)
    {
        var node = CreateBaseNode(SyntaxKind.ParenthesizedType);
        store.SetType(node, type);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateThisTypeNode()
    {
        var node = CreateBaseNode(SyntaxKind.ThisType);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTypeOperatorNode(SyntaxKind @operator, Node type)
    {
        var node = CreateBaseNode(SyntaxKind.TypeOperator);
        store.SetOperator(node, @operator);
        store.SetType(node, @operator == SyntaxKind.ReadonlyKeyword ?
            Parenthesizer.ParenthesizeOperandOfReadonlyTypeOperator(type) :
            Parenthesizer.ParenthesizeOperandOfTypeOperator(type));
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateIndexedAccessTypeNode(Node objectType, Node indexType)
    {
        var node = CreateBaseNode(SyntaxKind.IndexedAccessType);
        store.SetObjectType(node, Parenthesizer.ParenthesizeNonArrayTypeOfPostfixType(objectType));
        store.SetIndexType(node, indexType);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateMappedTypeNode(Node readonlyToken, Node typeParameter, Node nameType, Node questionToken, Node type, NodeList members)
    {
        var node = CreateBaseDeclaration(SyntaxKind.MappedType);
        store.SetReadonlyToken(node, readonlyToken);
        store.SetTypeParameter(node, typeParameter);
        store.SetNameType(node, nameType);
        store.SetQuestionToken(node, questionToken);
        store.SetType(node, type);
        store.SetMembers(node, !members.IsNone ? CreateNodeArray(store, members) : default);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateLiteralTypeNode(Node literal)
    {
        var node = CreateBaseNode(SyntaxKind.LiteralType);
        store.SetLiteral(node, literal);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        return node;
    }

    //
    // Binding Patterns
    //

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateObjectBindingPattern(NodeList elements)
    {
        var node = CreateBaseNode(SyntaxKind.ObjectBindingPattern);
        store.SetElements(node, CreateNodeArray(store, elements));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildrenFlags(store.Elements(node)) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsBindingPattern));
        if ((store.TransformFlags(node) & TransformFlags.ContainsRestOrSpread) != 0)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | (TransformFlags.ContainsES2018 |
                TransformFlags.ContainsObjectRestOrSpread));
        }
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateArrayBindingPattern(NodeList elements)
    {
        var node = CreateBaseNode(SyntaxKind.ArrayBindingPattern);
        store.SetElements(node, CreateNodeArray(store, elements));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildrenFlags(store.Elements(node)) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsBindingPattern));
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateBindingElement(Node dotDotDotToken, Node propertyName, Node name, Node initializer = default)
    {
        var node = CreateBaseDeclaration(SyntaxKind.BindingElement);
        store.SetDotDotDotToken(node, dotDotDotToken);
        store.SetPropertyName(node, AsName(propertyName));
        store.SetName(node, AsName(name));
        store.SetInitializer(node, AsInitializer(initializer));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.DotDotDotToken(node)) |
            PropagateNameFlags(store, store.PropertyName(node)) |
            PropagateNameFlags(store, store.Name(node)) |
            PropagateChildFlags(store, store.Initializer(node)) |
            (!store.DotDotDotToken(node).IsNone ? TransformFlags.ContainsRestOrSpread : TransformFlags.None) |
            TransformFlags.ContainsES2015));
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }
}
