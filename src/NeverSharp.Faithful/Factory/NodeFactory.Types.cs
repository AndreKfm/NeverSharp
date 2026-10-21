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

/// <summary>nodeFactory.ts: types and binding patterns.</summary>
internal sealed partial class NodeFactory
{
    //
    // Types
    //

    // @api
    internal TypePredicateNode CreateTypePredicateNode(AssertsKeyword? assertsModifier, object parameterName, Node? type)
    {
        var node = CreateBaseNode<TypePredicateNode>(SyntaxKind.TypePredicate);
        node.AssertsModifier = assertsModifier;
        node.ParameterName = AsName(parameterName)!;
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TypeReferenceNode CreateTypeReferenceNode(object typeName, IReadOnlyList<Node>? typeArguments)
    {
        var node = CreateBaseNode<TypeReferenceNode>(SyntaxKind.TypeReference);
        node.TypeName = AsName(typeName)!;
        node.TypeArguments = typeArguments != null ? Parenthesizer.ParenthesizeTypeArguments(CreateNodeArray(typeArguments)) : null;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal FunctionTypeNode CreateFunctionTypeNode(
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node type)
    {
        var node = CreateBaseDeclaration<FunctionTypeNode>(SyntaxKind.FunctionType);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = AsNodeArray(parameters)!;
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        node.Modifiers = null; // initialized by parser for grammar errors
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
    internal ConstructorTypeNode CreateConstructorTypeNode(IReadOnlyList<Node>? modifiers, IReadOnlyList<Node>? typeParameters, IReadOnlyList<Node> parameters, Node type)
    {
        return CreateConstructorTypeNode1(modifiers, typeParameters, parameters, type);
    }

    // @api
    internal ConstructorTypeNode CreateConstructorTypeNode(IReadOnlyList<Node>? typeParameters, IReadOnlyList<Node> parameters, Node type)
    {
        return CreateConstructorTypeNode2(typeParameters, parameters, type);
    }

    private ConstructorTypeNode CreateConstructorTypeNode1(
        IReadOnlyList<Node>? modifiers,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node type)
    {
        var node = CreateBaseDeclaration<ConstructorTypeNode>(SyntaxKind.ConstructorType);
        node.Modifiers = AsNodeArray(modifiers);
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

    /// <summary>Deprecated.</summary>
    private ConstructorTypeNode CreateConstructorTypeNode2(
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node type)
    {
        return CreateConstructorTypeNode1(/*modifiers*/ null, typeParameters, parameters, type);
    }

    // @api
    internal TypeQueryNode CreateTypeQueryNode(Node exprName, IReadOnlyList<Node>? typeArguments = null)
    {
        var node = CreateBaseNode<TypeQueryNode>(SyntaxKind.TypeQuery);
        node.ExprName = exprName;
        node.TypeArguments = typeArguments != null ? Parenthesizer.ParenthesizeTypeArguments(typeArguments) : null;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TypeLiteralNode CreateTypeLiteralNode(IReadOnlyList<Node>? members)
    {
        var node = CreateBaseDeclaration<TypeLiteralNode>(SyntaxKind.TypeLiteral);
        node.Members = CreateNodeArray(members);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal ArrayTypeNode CreateArrayTypeNode(Node elementType)
    {
        var node = CreateBaseNode<ArrayTypeNode>(SyntaxKind.ArrayType);
        node.ElementType = Parenthesizer.ParenthesizeNonArrayTypeOfPostfixType(elementType);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TupleTypeNode CreateTupleTypeNode(IReadOnlyList<Node> elements)
    {
        var node = CreateBaseNode<TupleTypeNode>(SyntaxKind.TupleType);
        node.Elements = CreateNodeArray(Parenthesizer.ParenthesizeElementTypesOfTupleType(elements));
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal NamedTupleMember CreateNamedTupleMember(DotDotDotToken? dotDotDotToken, Identifier name, QuestionToken? questionToken, Node type)
    {
        var node = CreateBaseDeclaration<NamedTupleMember>(SyntaxKind.NamedTupleMember);
        node.DotDotDotToken = dotDotDotToken;
        node.Name = name;
        node.QuestionToken = questionToken;
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal OptionalTypeNode CreateOptionalTypeNode(Node type)
    {
        var node = CreateBaseNode<OptionalTypeNode>(SyntaxKind.OptionalType);
        node.Type = Parenthesizer.ParenthesizeTypeOfOptionalType(type);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal RestTypeNode CreateRestTypeNode(Node type)
    {
        var node = CreateBaseNode<RestTypeNode>(SyntaxKind.RestType);
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    private Node CreateUnionOrIntersectionTypeNode(SyntaxKind kind, IReadOnlyList<Node> types, Func<IReadOnlyList<Node>, IReadOnlyList<Node>> parenthesize)
    {
        var node = CreateBaseNode<Node>(kind);
        var typesArray = CreateNodeArray(parenthesize(types));
        if (node is UnionTypeNode union)
        {
            union.Types = typesArray;
        }
        else
        {
            ((IntersectionTypeNode)node).Types = typesArray;
        }
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal UnionTypeNode CreateUnionTypeNode(IReadOnlyList<Node> types)
    {
        return (UnionTypeNode)CreateUnionOrIntersectionTypeNode(SyntaxKind.UnionType, types, Parenthesizer.ParenthesizeConstituentTypesOfUnionType);
    }

    // @api
    internal IntersectionTypeNode CreateIntersectionTypeNode(IReadOnlyList<Node> types)
    {
        return (IntersectionTypeNode)CreateUnionOrIntersectionTypeNode(SyntaxKind.IntersectionType, types, Parenthesizer.ParenthesizeConstituentTypesOfIntersectionType);
    }

    // @api
    internal ConditionalTypeNode CreateConditionalTypeNode(Node checkType, Node extendsType, Node trueType, Node falseType)
    {
        var node = CreateBaseNode<ConditionalTypeNode>(SyntaxKind.ConditionalType);
        node.CheckType = Parenthesizer.ParenthesizeCheckTypeOfConditionalType(checkType);
        node.ExtendsType = Parenthesizer.ParenthesizeExtendsTypeOfConditionalType(extendsType);
        node.TrueType = trueType;
        node.FalseType = falseType;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    internal InferTypeNode CreateInferTypeNode(TypeParameterDeclaration typeParameter)
    {
        var node = CreateBaseNode<InferTypeNode>(SyntaxKind.InferType);
        node.TypeParameter = typeParameter;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TemplateLiteralTypeNode CreateTemplateLiteralType(TemplateHead head, IReadOnlyList<Node> templateSpans)
    {
        var node = CreateBaseNode<TemplateLiteralTypeNode>(SyntaxKind.TemplateLiteralType);
        node.Head = head;
        node.TemplateSpans = CreateNodeArray(templateSpans);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal ImportTypeNode CreateImportTypeNode(
        Node argument,
        ImportAttributes? attributes = null,
        Node? qualifier = null,
        IReadOnlyList<Node>? typeArguments = null,
        bool isTypeOf = false)
    {
        var node = CreateBaseNode<ImportTypeNode>(SyntaxKind.ImportType);
        node.Argument = argument;
        node.Attributes = attributes;
        // TypeScript: `if (node.assertions && node.assertions.assertClause && node.attributes) { ... }`; a new node has
        // no `assertions` (the deprecated property is not declared on the generated class), so the test is false.
        node.Qualifier = qualifier;
        node.TypeArguments = typeArguments != null ? Parenthesizer.ParenthesizeTypeArguments(typeArguments) : null;
        node.IsTypeOf = isTypeOf;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal ParenthesizedTypeNode CreateParenthesizedType(Node type)
    {
        var node = CreateBaseNode<ParenthesizedTypeNode>(SyntaxKind.ParenthesizedType);
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal ThisTypeNode CreateThisTypeNode()
    {
        var node = CreateBaseNode<ThisTypeNode>(SyntaxKind.ThisType);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TypeOperatorNode CreateTypeOperatorNode(SyntaxKind @operator, Node type)
    {
        var node = CreateBaseNode<TypeOperatorNode>(SyntaxKind.TypeOperator);
        node.Operator = @operator;
        node.Type = @operator == SyntaxKind.ReadonlyKeyword ?
            Parenthesizer.ParenthesizeOperandOfReadonlyTypeOperator(type) :
            Parenthesizer.ParenthesizeOperandOfTypeOperator(type);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal IndexedAccessTypeNode CreateIndexedAccessTypeNode(Node objectType, Node indexType)
    {
        var node = CreateBaseNode<IndexedAccessTypeNode>(SyntaxKind.IndexedAccessType);
        node.ObjectType = Parenthesizer.ParenthesizeNonArrayTypeOfPostfixType(objectType);
        node.IndexType = indexType;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal MappedTypeNode CreateMappedTypeNode(Node? readonlyToken, TypeParameterDeclaration typeParameter, Node? nameType, Node? questionToken, Node? type, IReadOnlyList<Node>? members)
    {
        var node = CreateBaseDeclaration<MappedTypeNode>(SyntaxKind.MappedType);
        node.ReadonlyToken = readonlyToken;
        node.TypeParameter = typeParameter;
        node.NameType = nameType;
        node.QuestionToken = questionToken;
        node.Type = type;
        node.Members = members != null ? CreateNodeArray(members) : null;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    internal LiteralTypeNode CreateLiteralTypeNode(Node literal)
    {
        var node = CreateBaseNode<LiteralTypeNode>(SyntaxKind.LiteralType);
        node.Literal = literal;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    //
    // Binding Patterns
    //

    // @api
    internal ObjectBindingPattern CreateObjectBindingPattern(IReadOnlyList<Node> elements)
    {
        var node = CreateBaseNode<ObjectBindingPattern>(SyntaxKind.ObjectBindingPattern);
        node.Elements = CreateNodeArray(elements);
        node.TransformFlags |= PropagateChildrenFlags(node.Elements) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsBindingPattern;
        if ((node.TransformFlags & TransformFlags.ContainsRestOrSpread) != 0)
        {
            node.TransformFlags |= TransformFlags.ContainsES2018 |
                TransformFlags.ContainsObjectRestOrSpread;
        }
        return node;
    }

    // @api
    internal ArrayBindingPattern CreateArrayBindingPattern(IReadOnlyList<Node> elements)
    {
        var node = CreateBaseNode<ArrayBindingPattern>(SyntaxKind.ArrayBindingPattern);
        node.Elements = CreateNodeArray(elements);
        node.TransformFlags |= PropagateChildrenFlags(node.Elements) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsBindingPattern;
        return node;
    }

    // @api
    internal BindingElement CreateBindingElement(DotDotDotToken? dotDotDotToken, object? propertyName, object name, Node? initializer = null)
    {
        var node = CreateBaseDeclaration<BindingElement>(SyntaxKind.BindingElement);
        node.DotDotDotToken = dotDotDotToken;
        node.PropertyName = AsName(propertyName);
        node.Name = AsName(name)!;
        node.Initializer = AsInitializer(initializer);
        node.TransformFlags |= PropagateChildFlags(node.DotDotDotToken) |
            PropagateNameFlags(node.PropertyName) |
            PropagateNameFlags(node.Name) |
            PropagateChildFlags(node.Initializer) |
            (node.DotDotDotToken != null ? TransformFlags.ContainsRestOrSpread : TransformFlags.None) |
            TransformFlags.ContainsES2015;
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }
}
