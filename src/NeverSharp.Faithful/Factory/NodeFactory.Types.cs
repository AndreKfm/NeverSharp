// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go (NodeFactory)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// nodeFactory.ts, createNodeFactory: names, signature elements, type elements, types and binding patterns.
/// </summary>
internal sealed partial class NodeFactory
{
    //
    // Names
    //

    // @api
    internal QualifiedName CreateQualifiedName(Node left, Identifier right)
    {
        var node = (QualifiedName)CreateBaseNode(SyntaxKind.QualifiedName);
        node.Left = left;
        node.Right = AsName(right);
        node.TransformFlags |= PropagateChildFlags(node.Left) |
            PropagateIdentifierNameFlags(node.Right);

        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal ComputedPropertyName CreateComputedPropertyName(Node expression)
    {
        var node = (ComputedPropertyName)CreateBaseNode(SyntaxKind.ComputedPropertyName);
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
    internal TypeParameterDeclaration CreateTypeParameterDeclaration(IReadOnlyList<Node>? modifiers, Identifier name, Node? constraint = null, Node? defaultType = null)
    {
        var node = (TypeParameterDeclaration)CreateBaseDeclaration(SyntaxKind.TypeParameter);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.Constraint = constraint;
        node.Default = defaultType;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        node.Expression = null; // initialized by parser to report grammar errors
        node.JsDoc = null; // initialized by parser (JsDocContainer)
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
        var node = (ParameterDeclaration)CreateBaseDeclaration(SyntaxKind.Parameter);
        node.Modifiers = AsNodeArray(modifiers);
        node.DotDotDotToken = dotDotDotToken;
        node.Name = AsName(name);
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

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Decorator CreateDecorator(Node expression)
    {
        var node = (Decorator)CreateBaseNode(SyntaxKind.Decorator);
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
        Node name,
        QuestionToken? questionToken,
        Node? type)
    {
        var node = (PropertySignature)CreateBaseDeclaration(SyntaxKind.PropertySignature);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.Type = type;
        node.QuestionToken = questionToken;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        node.Initializer = null; // initialized by parser to report grammar errors
        node.JsDoc = null; // initialized by parser (JsDocContainer)
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
        var node = (PropertyDeclaration)CreateBaseDeclaration(SyntaxKind.PropertyDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.QuestionToken = questionOrExclamationToken != null && NodeTests.IsQuestionToken(questionOrExclamationToken) ? (QuestionToken)questionOrExclamationToken : null;
        node.ExclamationToken = questionOrExclamationToken != null && NodeTests.IsExclamationToken(questionOrExclamationToken) ? (ExclamationToken)questionOrExclamationToken : null;
        node.Type = type;
        node.Initializer = AsInitializer(initializer);

        var isAmbient = (node.Flags & NodeFlags.Ambient) != 0 || (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Ambient) != 0;

        node.TransformFlags = PropagateChildrenFlags(node.Modifiers) |
            PropagateNameFlags(node.Name) |
            PropagateChildFlags(node.Initializer) |
            (isAmbient || node.QuestionToken != null || node.ExclamationToken != null || node.Type != null ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
            (NodeTests.IsComputedPropertyName(node.Name) || (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Static) != 0 && node.Initializer != null ? TransformFlags.ContainsTypeScriptClassSyntax : TransformFlags.None) |
            TransformFlags.ContainsClassFields;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
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
        var node = (MethodSignature)CreateBaseDeclaration(SyntaxKind.MethodSignature);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.QuestionToken = questionToken;
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = AsNodeArray(parameters);
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.TypeArguments = null; // used in quick info
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
        var node = (MethodDeclaration)CreateBaseDeclaration(SyntaxKind.MethodDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.AsteriskToken = asteriskToken;
        node.Name = AsName(name);
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

        node.TypeArguments = null; // used in quick info
        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        node.EndFlowNode = null;
        node.ReturnFlowNode = null;
        return node;
    }

    // @api
    internal ClassStaticBlockDeclaration CreateClassStaticBlockDeclaration(Block body)
    {
        var node = (ClassStaticBlockDeclaration)CreateBaseDeclaration(SyntaxKind.ClassStaticBlockDeclaration);
        node.Body = body;
        node.TransformFlags = PropagateChildFlags(body) | TransformFlags.ContainsClassFields;

        node.Modifiers = null; // initialized by parser for grammar errors
        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.EndFlowNode = null;
        node.ReturnFlowNode = null;
        return node;
    }

    // @api
    internal ConstructorDeclaration CreateConstructorDeclaration(
        IReadOnlyList<Node>? modifiers,
        IReadOnlyList<Node> parameters,
        Block? body)
    {
        var node = (ConstructorDeclaration)CreateBaseDeclaration(SyntaxKind.Constructor);
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
        node.TypeArguments = null; // used in quick info
        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.EndFlowNode = null;
        node.ReturnFlowNode = null;
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
        var node = (GetAccessorDeclaration)CreateBaseDeclaration(SyntaxKind.GetAccessor);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
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

        node.TypeArguments = null; // used in quick info
        node.TypeParameters = null; // initialized by parser for grammar errors
        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        node.EndFlowNode = null;
        node.ReturnFlowNode = null;
        return node;
    }

    // @api
    internal SetAccessorDeclaration CreateSetAccessorDeclaration(
        IReadOnlyList<Node>? modifiers,
        Node name,
        IReadOnlyList<Node> parameters,
        Block? body)
    {
        var node = (SetAccessorDeclaration)CreateBaseDeclaration(SyntaxKind.SetAccessor);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
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

        node.TypeArguments = null; // used in quick info
        node.TypeParameters = null; // initialized by parser for grammar errors
        node.Type = null; // initialized by parser for grammar errors
        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        node.EndFlowNode = null;
        node.ReturnFlowNode = null;
        return node;
    }

    // @api
    internal CallSignatureDeclaration CreateCallSignature(
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node? type)
    {
        var node = (CallSignatureDeclaration)CreateBaseDeclaration(SyntaxKind.CallSignature);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = AsNodeArray(parameters);
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.TypeArguments = null; // used in quick info
        return node;
    }

    // @api
    internal ConstructSignatureDeclaration CreateConstructSignature(
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node? type)
    {
        var node = (ConstructSignatureDeclaration)CreateBaseDeclaration(SyntaxKind.ConstructSignature);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = AsNodeArray(parameters);
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.TypeArguments = null; // used in quick info
        return node;
    }

    // @api
    internal IndexSignatureDeclaration CreateIndexSignature(
        IReadOnlyList<Node>? modifiers,
        IReadOnlyList<Node> parameters,
        Node? type)
    {
        var node = (IndexSignatureDeclaration)CreateBaseDeclaration(SyntaxKind.IndexSignature);
        node.Modifiers = AsNodeArray(modifiers);
        node.Parameters = AsNodeArray(parameters);
        node.Type = type!; // TODO(rbuckton): We mark this as required in IndexSignatureDeclaration, but it looks like the parser allows it to be elided.
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.TypeArguments = null; // used in quick info
        return node;
    }

    // @api
    internal TemplateLiteralTypeSpan CreateTemplateLiteralTypeSpan(Node type, Node literal)
    {
        var node = (TemplateLiteralTypeSpan)CreateBaseNode(SyntaxKind.TemplateLiteralTypeSpan);
        node.Type = type;
        node.Literal = literal;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    //
    // Types
    //

    // @api
    internal Node CreateKeywordTypeNode(SyntaxKind kind)
    {
        return CreateToken(kind);
    }

    // @api
    internal TypePredicateNode CreateTypePredicateNode(AssertsKeyword? assertsModifier, Node parameterName, Node? type)
    {
        var node = (TypePredicateNode)CreateBaseNode(SyntaxKind.TypePredicate);
        node.AssertsModifier = assertsModifier;
        node.ParameterName = AsName(parameterName);
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TypeReferenceNode CreateTypeReferenceNode(Node typeName, IReadOnlyList<Node>? typeArguments = null)
    {
        var node = (TypeReferenceNode)CreateBaseNode(SyntaxKind.TypeReference);
        node.TypeName = AsName(typeName);
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
        var node = (FunctionTypeNode)CreateBaseDeclaration(SyntaxKind.FunctionType);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = AsNodeArray(parameters);
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        node.Modifiers = null; // initialized by parser for grammar errors
        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.TypeArguments = null; // used in quick info
        return node;
    }

    /// <summary>
    /// createConstructorTypeNode(...args): `args.length === 4 ? createConstructorTypeNode1(...args)`. The dispatch on the
    /// argument count is C# overload resolution (the `Debug.fail` for other counts cannot be reached).
    /// </summary>
    // @api
    internal ConstructorTypeNode CreateConstructorTypeNode(
        IReadOnlyList<Node>? modifiers,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node type)
    {
        return CreateConstructorTypeNode1(modifiers, typeParameters, parameters, type);
    }

    /// <summary>createConstructorTypeNode(...args): `args.length === 3 ? createConstructorTypeNode2(...args)`.</summary>
    // @api
    internal ConstructorTypeNode CreateConstructorTypeNode(
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node type)
    {
        return CreateConstructorTypeNode2(typeParameters, parameters, type);
    }

    private ConstructorTypeNode CreateConstructorTypeNode1(
        IReadOnlyList<Node>? modifiers,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node type)
    {
        var node = (ConstructorTypeNode)CreateBaseDeclaration(SyntaxKind.ConstructorType);
        node.Modifiers = AsNodeArray(modifiers);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = AsNodeArray(parameters);
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.TypeArguments = null; // used in quick info
        return node;
    }

    /// <summary>@deprecated</summary>
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
        var node = (TypeQueryNode)CreateBaseNode(SyntaxKind.TypeQuery);
        node.ExprName = exprName;
        node.TypeArguments = typeArguments != null ? Parenthesizer.ParenthesizeTypeArguments(typeArguments) : null;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TypeLiteralNode CreateTypeLiteralNode(IReadOnlyList<Node>? members)
    {
        var node = (TypeLiteralNode)CreateBaseDeclaration(SyntaxKind.TypeLiteral);
        node.Members = CreateNodeArray(members);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal ArrayTypeNode CreateArrayTypeNode(Node elementType)
    {
        var node = (ArrayTypeNode)CreateBaseNode(SyntaxKind.ArrayType);
        node.ElementType = Parenthesizer.ParenthesizeNonArrayTypeOfPostfixType(elementType);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TupleTypeNode CreateTupleTypeNode(IReadOnlyList<Node> elements)
    {
        var node = (TupleTypeNode)CreateBaseNode(SyntaxKind.TupleType);
        node.Elements = CreateNodeArray(Parenthesizer.ParenthesizeElementTypesOfTupleType(elements));
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal NamedTupleMember CreateNamedTupleMember(DotDotDotToken? dotDotDotToken, Identifier name, QuestionToken? questionToken, Node type)
    {
        var node = (NamedTupleMember)CreateBaseDeclaration(SyntaxKind.NamedTupleMember);
        node.DotDotDotToken = dotDotDotToken;
        node.Name = name;
        node.QuestionToken = questionToken;
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal OptionalTypeNode CreateOptionalTypeNode(Node type)
    {
        var node = (OptionalTypeNode)CreateBaseNode(SyntaxKind.OptionalType);
        node.Type = Parenthesizer.ParenthesizeTypeOfOptionalType(type);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal RestTypeNode CreateRestTypeNode(Node type)
    {
        var node = (RestTypeNode)CreateBaseNode(SyntaxKind.RestType);
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    private Node CreateUnionOrIntersectionTypeNode(SyntaxKind kind, IReadOnlyList<Node> types, Func<IReadOnlyList<Node>, NodeArray> parenthesize)
    {
        var node = CreateBaseNode(kind);
        // node.types = factory.createNodeArray(parenthesize(types)) (UnionTypeNode | IntersectionTypeNode)
        var nodeTypes = CreateNodeArray(parenthesize(types));
        if (node is UnionTypeNode union)
        {
            union.Types = nodeTypes;
        }
        else
        {
            ((IntersectionTypeNode)node).Types = nodeTypes;
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
        var node = (ConditionalTypeNode)CreateBaseNode(SyntaxKind.ConditionalType);
        node.CheckType = Parenthesizer.ParenthesizeCheckTypeOfConditionalType(checkType);
        node.ExtendsType = Parenthesizer.ParenthesizeExtendsTypeOfConditionalType(extendsType);
        node.TrueType = trueType;
        node.FalseType = falseType;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    internal InferTypeNode CreateInferTypeNode(TypeParameterDeclaration typeParameter)
    {
        var node = (InferTypeNode)CreateBaseNode(SyntaxKind.InferType);
        node.TypeParameter = typeParameter;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TemplateLiteralTypeNode CreateTemplateLiteralType(TemplateHead head, IReadOnlyList<Node> templateSpans)
    {
        var node = (TemplateLiteralTypeNode)CreateBaseNode(SyntaxKind.TemplateLiteralType);
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
        var node = (ImportTypeNode)CreateBaseNode(SyntaxKind.ImportType);
        node.Argument = argument;
        node.Attributes = attributes;
        // TypeScript: `if (node.assertions && node.assertions.assertClause && node.attributes) {
        //     (node.assertions as Mutable<ImportTypeAssertionContainer>).assertClause = node.attributes; }`
        // The generated ImportTypeNode has no `assertions` member; on the node just created it is undefined, so the
        // branch is never taken.
        node.Qualifier = qualifier;
        node.TypeArguments = typeArguments != null ? Parenthesizer.ParenthesizeTypeArguments(typeArguments) : null;
        node.IsTypeOf = isTypeOf;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal ParenthesizedTypeNode CreateParenthesizedType(Node type)
    {
        var node = (ParenthesizedTypeNode)CreateBaseNode(SyntaxKind.ParenthesizedType);
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal ThisTypeNode CreateThisTypeNode()
    {
        var node = (ThisTypeNode)CreateBaseNode(SyntaxKind.ThisType);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TypeOperatorNode CreateTypeOperatorNode(SyntaxKind @operator, Node type)
    {
        var node = (TypeOperatorNode)CreateBaseNode(SyntaxKind.TypeOperator);
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
        var node = (IndexedAccessTypeNode)CreateBaseNode(SyntaxKind.IndexedAccessType);
        node.ObjectType = Parenthesizer.ParenthesizeNonArrayTypeOfPostfixType(objectType);
        node.IndexType = indexType;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal MappedTypeNode CreateMappedTypeNode(Node? readonlyToken, TypeParameterDeclaration typeParameter, Node? nameType, Node? questionToken, Node? type, IReadOnlyList<Node>? members)
    {
        var node = (MappedTypeNode)CreateBaseDeclaration(SyntaxKind.MappedType);
        node.ReadonlyToken = readonlyToken;
        node.TypeParameter = typeParameter;
        node.NameType = nameType;
        node.QuestionToken = questionToken;
        node.Type = type;
        node.Members = members != null ? CreateNodeArray(members) : null;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    internal LiteralTypeNode CreateLiteralTypeNode(Node literal)
    {
        var node = (LiteralTypeNode)CreateBaseNode(SyntaxKind.LiteralType);
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
        var node = (ObjectBindingPattern)CreateBaseNode(SyntaxKind.ObjectBindingPattern);
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
        var node = (ArrayBindingPattern)CreateBaseNode(SyntaxKind.ArrayBindingPattern);
        node.Elements = CreateNodeArray(elements);
        node.TransformFlags |= PropagateChildrenFlags(node.Elements) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsBindingPattern;
        return node;
    }

    // @api
    internal BindingElement CreateBindingElement(DotDotDotToken? dotDotDotToken, Node? propertyName, Node name, Node? initializer = null)
    {
        var node = (BindingElement)CreateBaseDeclaration(SyntaxKind.BindingElement);
        node.DotDotDotToken = dotDotDotToken;
        node.PropertyName = AsName(propertyName);
        node.Name = AsName(name);
        node.Initializer = AsInitializer(initializer);
        node.TransformFlags |= PropagateChildFlags(node.DotDotDotToken) |
            PropagateNameFlags(node.PropertyName) |
            PropagateNameFlags(node.Name) |
            PropagateChildFlags(node.Initializer) |
            (node.DotDotDotToken != null ? TransformFlags.ContainsRestOrSpread : TransformFlags.None) |
            TransformFlags.ContainsES2015;

        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }
}
