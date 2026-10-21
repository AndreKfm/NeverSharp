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

// nodeFactory.ts lines 1570-2877: names, signature elements, type elements, types, binding patterns,
// array and object literal expressions.
//
// Notes on the port:
//  - TypeScript's `node.x = undefined; // initialized by parser/binder` statements only shape the object (they
//    make the property exist); C# fields start as null, so those statements are not repeated.
//  - The update*/finishUpdate* functions of this range are not called by the parser and are not ported
//    (a pure update* that nothing ported calls is not needed).
//  - Parameters of TypeScript type `string | X` take `X` (the parser never passes strings).
internal sealed partial class NodeFactory
{
    //
    // Names
    //

    // @api
    internal QualifiedName CreateQualifiedName(Node left, Identifier right)
    {
        QualifiedName node = CreateBaseNode<QualifiedName>(SyntaxKind.QualifiedName);
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
        ComputedPropertyName node = CreateBaseNode<ComputedPropertyName>(SyntaxKind.ComputedPropertyName);
        node.Expression = parenthesizerRules.ParenthesizeExpressionOfComputedPropertyName(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsComputedPropertyName;
        return node;
    }

    //
    // Signature elements
    //

    // @api
    internal TypeParameterDeclaration CreateTypeParameterDeclaration(IReadOnlyList<Node>? modifiers, Identifier name, Node? constraint = null, Node? defaultType = null)
    {
        TypeParameterDeclaration node = CreateBaseDeclaration<TypeParameterDeclaration>(SyntaxKind.TypeParameter);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = (Identifier)AsName(name)!;
        node.Constraint = constraint;
        node.Default = defaultType;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        // node.expression = undefined; // initialized by parser to report grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal ParameterDeclaration CreateParameterDeclaration(
        IReadOnlyList<Node>? modifiers,
        DotDotDotToken? dotDotDotToken,
        Node name,
        QuestionToken? questionToken = null,
        Node? type = null,
        Node? initializer = null)
    {
        ParameterDeclaration node = CreateBaseDeclaration<ParameterDeclaration>(SyntaxKind.Parameter);
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
                ((node.QuestionToken != null || node.Type != null) ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
                ((node.DotDotDotToken != null || node.Initializer != null) ? TransformFlags.ContainsES2015 : TransformFlags.None) |
                ((Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.ParameterPropertyModifier) != 0 ? TransformFlags.ContainsTypeScriptClassSyntax : TransformFlags.None);
        }

        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Decorator CreateDecorator(Node expression)
    {
        Decorator node = CreateBaseNode<Decorator>(SyntaxKind.Decorator);
        node.Expression = parenthesizerRules.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false);
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
        Node name,
        QuestionToken? questionToken,
        Node? type)
    {
        PropertySignature node = CreateBaseDeclaration<PropertySignature>(SyntaxKind.PropertySignature);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name)!;
        node.Type = type;
        node.QuestionToken = questionToken;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        // node.initializer = undefined; // initialized by parser to report grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal PropertyDeclaration CreatePropertyDeclaration(
        IReadOnlyList<Node>? modifiers,
        Node name,
        Node? questionOrExclamationToken,
        Node? type,
        Node? initializer)
    {
        PropertyDeclaration node = CreateBaseDeclaration<PropertyDeclaration>(SyntaxKind.PropertyDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name)!;
        node.QuestionToken = questionOrExclamationToken != null && NodeTests.IsQuestionToken(questionOrExclamationToken) ? (QuestionToken)questionOrExclamationToken : null;
        node.ExclamationToken = questionOrExclamationToken != null && NodeTests.IsExclamationToken(questionOrExclamationToken) ? (ExclamationToken)questionOrExclamationToken : null;
        node.Type = type;
        node.Initializer = AsInitializer(initializer);

        bool isAmbient = (node.Flags & NodeFlags.Ambient) != 0 || (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Ambient) != 0;

        node.TransformFlags = PropagateChildrenFlags(node.Modifiers) |
            PropagateNameFlags(node.Name) |
            PropagateChildFlags(node.Initializer) |
            ((isAmbient || node.QuestionToken != null || node.ExclamationToken != null || node.Type != null) ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
            ((NodeTests.IsComputedPropertyName(node.Name) || ((Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Static) != 0 && node.Initializer != null)) ? TransformFlags.ContainsTypeScriptClassSyntax : TransformFlags.None) |
            TransformFlags.ContainsClassFields;

        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal MethodSignature CreateMethodSignature(
        IReadOnlyList<Node>? modifiers,
        Node name,
        QuestionToken? questionToken,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node? type)
    {
        MethodSignature node = CreateBaseDeclaration<MethodSignature>(SyntaxKind.MethodSignature);
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
        Node name,
        QuestionToken? questionToken,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node? type,
        Block? body)
    {
        MethodDeclaration node = CreateBaseDeclaration<MethodDeclaration>(SyntaxKind.MethodDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.AsteriskToken = asteriskToken;
        node.Name = AsName(name)!;
        node.QuestionToken = questionToken;
        // node.exclamationToken = undefined; // initialized by parser for grammar errors
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
            bool isAsync = (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Async) != 0;
            bool isGenerator = node.AsteriskToken != null;
            bool isAsyncGenerator = isAsync && isGenerator;

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
                ((node.QuestionToken != null || node.TypeParameters != null || node.Type != null) ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
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
    internal ClassStaticBlockDeclaration CreateClassStaticBlockDeclaration(Block body)
    {
        ClassStaticBlockDeclaration node = CreateBaseDeclaration<ClassStaticBlockDeclaration>(SyntaxKind.ClassStaticBlockDeclaration);
        node.Body = body;
        node.TransformFlags = PropagateChildFlags(body) | TransformFlags.ContainsClassFields;

        // node.modifiers = undefined; // initialized by parser for grammar errors
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
        ConstructorDeclaration node = CreateBaseDeclaration<ConstructorDeclaration>(SyntaxKind.Constructor);
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

        // node.typeParameters = undefined; // initialized by parser for grammar errors
        // node.type = undefined; // initialized by parser for grammar errors
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
        Node name,
        IReadOnlyList<Node> parameters,
        Node? type,
        Block? body)
    {
        GetAccessorDeclaration node = CreateBaseDeclaration<GetAccessorDeclaration>(SyntaxKind.GetAccessor);
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
        // node.typeParameters = undefined; // initialized by parser for grammar errors
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
        Node name,
        IReadOnlyList<Node> parameters,
        Block? body)
    {
        SetAccessorDeclaration node = CreateBaseDeclaration<SetAccessorDeclaration>(SyntaxKind.SetAccessor);
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
            // node.type is still undefined here (it is only assigned below, as in TypeScript), so the last term is None.
            node.TransformFlags = PropagateChildrenFlags(node.Modifiers) |
                PropagateNameFlags(node.Name) |
                PropagateChildrenFlags(node.Parameters) |
                (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
                (node.Type != null ? TransformFlags.ContainsTypeScript : TransformFlags.None);
        }

        // node.typeArguments = undefined; // used in quick info
        // node.typeParameters = undefined; // initialized by parser for grammar errors
        // node.type = undefined; // initialized by parser for grammar errors
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
        CallSignatureDeclaration node = CreateBaseDeclaration<CallSignatureDeclaration>(SyntaxKind.CallSignature);
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
        ConstructSignatureDeclaration node = CreateBaseDeclaration<ConstructSignatureDeclaration>(SyntaxKind.ConstructSignature);
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
    // TypeScript declares `type: TypeNode` publicly and `TypeNode | undefined` in the @internal overload; the parser may elide it.
    internal IndexSignatureDeclaration CreateIndexSignature(
        IReadOnlyList<Node>? modifiers,
        IReadOnlyList<Node> parameters,
        Node? type)
    {
        IndexSignatureDeclaration node = CreateBaseDeclaration<IndexSignatureDeclaration>(SyntaxKind.IndexSignature);
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
        TemplateLiteralTypeSpan node = CreateBaseNode<TemplateLiteralTypeSpan>(SyntaxKind.TemplateLiteralTypeSpan);
        node.Type = type;
        node.Literal = literal;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    //
    // Types
    //

    // @api
    internal TypePredicateNode CreateTypePredicateNode(AssertsKeyword? assertsModifier, Node parameterName, Node? type)
    {
        TypePredicateNode node = CreateBaseNode<TypePredicateNode>(SyntaxKind.TypePredicate);
        node.AssertsModifier = assertsModifier;
        node.ParameterName = AsName(parameterName)!;
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TypeReferenceNode CreateTypeReferenceNode(Node typeName, IReadOnlyList<Node>? typeArguments = null)
    {
        TypeReferenceNode node = CreateBaseNode<TypeReferenceNode>(SyntaxKind.TypeReference);
        node.TypeName = AsName(typeName)!;
        node.TypeArguments = typeArguments != null ? parenthesizerRules.ParenthesizeTypeArguments(CreateNodeArray(typeArguments)) : null;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal FunctionTypeNode CreateFunctionTypeNode(
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node type)
    {
        FunctionTypeNode node = CreateBaseDeclaration<FunctionTypeNode>(SyntaxKind.FunctionType);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = AsNodeArray(parameters)!;
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        // node.modifiers = undefined; // initialized by parser for grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.typeArguments = undefined; // used in quick info
        return node;
    }

    // @api
    // TypeScript: createConstructorTypeNode(...args) dispatches on args.length (4 -> createConstructorTypeNode1,
    // 3 -> createConstructorTypeNode2); in C# the two argument counts are the two overloads.
    internal ConstructorTypeNode CreateConstructorTypeNode(
        IReadOnlyList<Node>? modifiers,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node type)
    {
        return CreateConstructorTypeNode1(modifiers, typeParameters, parameters, type);
    }

    // TypeScript: the three-argument form (deprecated).
    internal ConstructorTypeNode CreateConstructorTypeNode(
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node type)
    {
        return CreateConstructorTypeNode2(typeParameters, parameters, type);
    }

    internal ConstructorTypeNode CreateConstructorTypeNode1(
        IReadOnlyList<Node>? modifiers,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node type)
    {
        ConstructorTypeNode node = CreateBaseDeclaration<ConstructorTypeNode>(SyntaxKind.ConstructorType);
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

    // TypeScript: @deprecated.
    internal ConstructorTypeNode CreateConstructorTypeNode2(
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node type)
    {
        return CreateConstructorTypeNode1(/*modifiers*/ null, typeParameters, parameters, type);
    }

    // @api
    internal TypeQueryNode CreateTypeQueryNode(Node exprName, IReadOnlyList<Node>? typeArguments = null)
    {
        TypeQueryNode node = CreateBaseNode<TypeQueryNode>(SyntaxKind.TypeQuery);
        node.ExprName = exprName;
        node.TypeArguments = typeArguments != null ? parenthesizerRules.ParenthesizeTypeArguments(typeArguments) : null;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TypeLiteralNode CreateTypeLiteralNode(IReadOnlyList<Node>? members)
    {
        TypeLiteralNode node = CreateBaseDeclaration<TypeLiteralNode>(SyntaxKind.TypeLiteral);
        node.Members = CreateNodeArray(members);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal ArrayTypeNode CreateArrayTypeNode(Node elementType)
    {
        ArrayTypeNode node = CreateBaseNode<ArrayTypeNode>(SyntaxKind.ArrayType);
        node.ElementType = parenthesizerRules.ParenthesizeNonArrayTypeOfPostfixType(elementType);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TupleTypeNode CreateTupleTypeNode(IReadOnlyList<Node> elements)
    {
        TupleTypeNode node = CreateBaseNode<TupleTypeNode>(SyntaxKind.TupleType);
        node.Elements = CreateNodeArray(parenthesizerRules.ParenthesizeElementTypesOfTupleType(elements));
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal NamedTupleMember CreateNamedTupleMember(DotDotDotToken? dotDotDotToken, Identifier name, QuestionToken? questionToken, Node type)
    {
        NamedTupleMember node = CreateBaseDeclaration<NamedTupleMember>(SyntaxKind.NamedTupleMember);
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
        OptionalTypeNode node = CreateBaseNode<OptionalTypeNode>(SyntaxKind.OptionalType);
        node.Type = parenthesizerRules.ParenthesizeTypeOfOptionalType(type);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal RestTypeNode CreateRestTypeNode(Node type)
    {
        RestTypeNode node = CreateBaseNode<RestTypeNode>(SyntaxKind.RestType);
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // TypeScript returns UnionTypeNode | IntersectionTypeNode; both classes have a `Types` field and no common base,
    // so the node is returned as Node and the field is assigned by kind.
    private Node CreateUnionOrIntersectionTypeNode(SyntaxKind kind, IReadOnlyList<Node> types, Func<IReadOnlyList<Node>, IReadOnlyList<Node>> parenthesize)
    {
        Node node = CreateBaseNode<Node>(kind);
        NodeArray typesArray = CreateNodeArray(parenthesize(types));
        if (node is UnionTypeNode unionType)
        {
            unionType.Types = typesArray;
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
        return (UnionTypeNode)CreateUnionOrIntersectionTypeNode(SyntaxKind.UnionType, types, parenthesizerRules.ParenthesizeConstituentTypesOfUnionType);
    }

    // @api
    internal IntersectionTypeNode CreateIntersectionTypeNode(IReadOnlyList<Node> types)
    {
        return (IntersectionTypeNode)CreateUnionOrIntersectionTypeNode(SyntaxKind.IntersectionType, types, parenthesizerRules.ParenthesizeConstituentTypesOfIntersectionType);
    }

    // @api
    internal ConditionalTypeNode CreateConditionalTypeNode(Node checkType, Node extendsType, Node trueType, Node falseType)
    {
        ConditionalTypeNode node = CreateBaseNode<ConditionalTypeNode>(SyntaxKind.ConditionalType);
        node.CheckType = parenthesizerRules.ParenthesizeCheckTypeOfConditionalType(checkType);
        node.ExtendsType = parenthesizerRules.ParenthesizeExtendsTypeOfConditionalType(extendsType);
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
        InferTypeNode node = CreateBaseNode<InferTypeNode>(SyntaxKind.InferType);
        node.TypeParameter = typeParameter;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TemplateLiteralTypeNode CreateTemplateLiteralType(TemplateHead head, IReadOnlyList<Node> templateSpans)
    {
        TemplateLiteralTypeNode node = CreateBaseNode<TemplateLiteralTypeNode>(SyntaxKind.TemplateLiteralType);
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
        ImportTypeNode node = CreateBaseNode<ImportTypeNode>(SyntaxKind.ImportType);
        node.Argument = argument;
        node.Attributes = attributes;
        // TypeScript: `if (node.assertions && node.assertions.assertClause && node.attributes) { ... }`.
        // `node.assertions` is never assigned on a freshly created node, so this branch is dead (and
        // ImportTypeNode has no Assertions field in the generated class).
        node.Qualifier = qualifier;
        node.TypeArguments = typeArguments != null ? parenthesizerRules.ParenthesizeTypeArguments(typeArguments) : null;
        node.IsTypeOf = isTypeOf;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal ParenthesizedTypeNode CreateParenthesizedType(Node type)
    {
        ParenthesizedTypeNode node = CreateBaseNode<ParenthesizedTypeNode>(SyntaxKind.ParenthesizedType);
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal ThisTypeNode CreateThisTypeNode()
    {
        ThisTypeNode node = CreateBaseNode<ThisTypeNode>(SyntaxKind.ThisType);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TypeOperatorNode CreateTypeOperatorNode(SyntaxKind @operator, Node type)
    {
        TypeOperatorNode node = CreateBaseNode<TypeOperatorNode>(SyntaxKind.TypeOperator);
        node.Operator = @operator;
        node.Type = @operator == SyntaxKind.ReadonlyKeyword ?
            parenthesizerRules.ParenthesizeOperandOfReadonlyTypeOperator(type) :
            parenthesizerRules.ParenthesizeOperandOfTypeOperator(type);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal IndexedAccessTypeNode CreateIndexedAccessTypeNode(Node objectType, Node indexType)
    {
        IndexedAccessTypeNode node = CreateBaseNode<IndexedAccessTypeNode>(SyntaxKind.IndexedAccessType);
        node.ObjectType = parenthesizerRules.ParenthesizeNonArrayTypeOfPostfixType(objectType);
        node.IndexType = indexType;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal MappedTypeNode CreateMappedTypeNode(
        Node? readonlyToken,
        TypeParameterDeclaration typeParameter,
        Node? nameType,
        Node? questionToken,
        Node? type,
        IReadOnlyList<Node>? members)
    {
        MappedTypeNode node = CreateBaseDeclaration<MappedTypeNode>(SyntaxKind.MappedType);
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
        LiteralTypeNode node = CreateBaseNode<LiteralTypeNode>(SyntaxKind.LiteralType);
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
        ObjectBindingPattern node = CreateBaseNode<ObjectBindingPattern>(SyntaxKind.ObjectBindingPattern);
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
        ArrayBindingPattern node = CreateBaseNode<ArrayBindingPattern>(SyntaxKind.ArrayBindingPattern);
        node.Elements = CreateNodeArray(elements);
        node.TransformFlags |= PropagateChildrenFlags(node.Elements) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsBindingPattern;
        return node;
    }

    // @api
    internal BindingElement CreateBindingElement(DotDotDotToken? dotDotDotToken, Node? propertyName, Node name, Node? initializer = null)
    {
        BindingElement node = CreateBaseDeclaration<BindingElement>(SyntaxKind.BindingElement);
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

    //
    // Expression
    //

    // @api
    internal ArrayLiteralExpression CreateArrayLiteralExpression(IReadOnlyList<Node>? elements = null, bool? multiLine = null)
    {
        ArrayLiteralExpression node = CreateBaseNode<ArrayLiteralExpression>(SyntaxKind.ArrayLiteralExpression);
        // Ensure we add a trailing comma for something like `[NumericLiteral(1), NumericLiteral(2), OmittedExpresion]` so that
        // we end up with `[1, 2, ,]` instead of `[1, 2, ]` otherwise the `OmittedExpression` will just end up being treated like
        // a trailing comma.
        Node? lastElement = elements != null ? Core.LastOrUndefined(elements) : null;
        NodeArray elementsArray = CreateNodeArray(elements, lastElement != null && NodeTests.IsOmittedExpression(lastElement) ? (bool?)true : null);
        node.Elements = parenthesizerRules.ParenthesizeExpressionsOfCommaDelimitedList(elementsArray);
        node.MultiLine = multiLine ?? false;
        node.TransformFlags |= PropagateChildrenFlags(node.Elements);
        return node;
    }

    // @api
    internal ObjectLiteralExpression CreateObjectLiteralExpression(IReadOnlyList<Node>? properties = null, bool? multiLine = null)
    {
        ObjectLiteralExpression node = CreateBaseDeclaration<ObjectLiteralExpression>(SyntaxKind.ObjectLiteralExpression);
        node.Properties = CreateNodeArray(properties);
        node.MultiLine = multiLine ?? false;
        node.TransformFlags |= PropagateChildrenFlags(node.Properties);

        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }
}
