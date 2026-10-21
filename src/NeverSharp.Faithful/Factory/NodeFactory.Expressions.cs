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

/// <summary>nodeFactory.ts, createNodeFactory: expressions, template literals and misc.</summary>
internal sealed partial class NodeFactory
{
    //
    // Expression
    //

    // @api
    internal ArrayLiteralExpression CreateArrayLiteralExpression(IReadOnlyList<Node>? elements = null, bool multiLine = false)
    {
        var node = (ArrayLiteralExpression)CreateBaseNode(SyntaxKind.ArrayLiteralExpression);
        // Ensure we add a trailing comma for something like `[NumericLiteral(1), NumericLiteral(2), OmittedExpresion]` so that
        // we end up with `[1, 2, ,]` instead of `[1, 2, ]` otherwise the `OmittedExpression` will just end up being treated like
        // a trailing comma.
        var lastElement = elements != null ? Core.LastOrUndefined(elements) : null;
        var elementsArray = CreateNodeArray(elements, lastElement != null && NodeTests.IsOmittedExpression(lastElement) ? true : null);
        node.Elements = Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(elementsArray);
        node.MultiLine = multiLine;
        node.TransformFlags |= PropagateChildrenFlags(node.Elements);
        return node;
    }

    // @api
    internal ObjectLiteralExpression CreateObjectLiteralExpression(IReadOnlyList<Node>? properties = null, bool multiLine = false)
    {
        var node = (ObjectLiteralExpression)CreateBaseDeclaration(SyntaxKind.ObjectLiteralExpression);
        node.Properties = CreateNodeArray(properties);
        node.MultiLine = multiLine;
        node.TransformFlags |= PropagateChildrenFlags(node.Properties);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    private PropertyAccessExpression CreateBasePropertyAccessExpression(Node expression, QuestionDotToken? questionDotToken, Node name)
    {
        var node = (PropertyAccessExpression)CreateBaseDeclaration(SyntaxKind.PropertyAccessExpression);
        node.Expression = expression;
        node.QuestionDotToken = questionDotToken;
        node.Name = name;
        node.TransformFlags = PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.QuestionDotToken) |
            (NodeTests.IsIdentifier(node.Name) ?
                PropagateIdentifierNameFlags(node.Name) :
                PropagateChildFlags(node.Name) | TransformFlags.ContainsPrivateIdentifierInExpression);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    /// <summary>
    /// The factory member `createPropertyAccessExpression`: `flags &amp; NodeFactoryFlags.NoIndentationOnFreshPropertyAccess ?
    /// (expression, name) => setEmitFlags(createPropertyAccessExpression(expression, name), EmitFlags.NoIndentation) :
    /// createPropertyAccessExpression`.
    /// </summary>
    // @api
    internal PropertyAccessExpression CreatePropertyAccessExpression(Node expression, Node name)
    {
        return (Flags & NodeFactoryFlags.NoIndentationOnFreshPropertyAccess) != 0 ?
            FactoryEmitNode.SetEmitFlags(CreatePropertyAccessExpressionWorker(expression, name), EmitFlags.NoIndentation) :
            CreatePropertyAccessExpressionWorker(expression, name);
    }

    /// <summary>The inner function `createPropertyAccessExpression` of createNodeFactory.</summary>
    private PropertyAccessExpression CreatePropertyAccessExpressionWorker(Node expression, Node name)
    {
        var node = CreateBasePropertyAccessExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false),
            /*questionDotToken*/ null,
            AsName(name));
        if (NodeTests.IsSuperKeyword(expression))
        {
            // super method calls require a lexical 'this'
            // super method calls require 'super' hoisting in ES2017 and ES2018 async functions and async generators
            node.TransformFlags |= TransformFlags.ContainsES2017 |
                TransformFlags.ContainsES2018;
        }
        return node;
    }

    /// <summary>
    /// The factory member `createPropertyAccessChain`: `flags &amp; NodeFactoryFlags.NoIndentationOnFreshPropertyAccess ?
    /// (expression, questionDotToken, name) => setEmitFlags(createPropertyAccessChain(expression, questionDotToken, name),
    /// EmitFlags.NoIndentation) : createPropertyAccessChain`.
    /// </summary>
    // @api
    internal PropertyAccessExpression CreatePropertyAccessChain(Node expression, QuestionDotToken? questionDotToken, Node name)
    {
        return (Flags & NodeFactoryFlags.NoIndentationOnFreshPropertyAccess) != 0 ?
            FactoryEmitNode.SetEmitFlags(CreatePropertyAccessChainWorker(expression, questionDotToken, name), EmitFlags.NoIndentation) :
            CreatePropertyAccessChainWorker(expression, questionDotToken, name);
    }

    /// <summary>The inner function `createPropertyAccessChain` of createNodeFactory.</summary>
    private PropertyAccessExpression CreatePropertyAccessChainWorker(Node expression, QuestionDotToken? questionDotToken, Node name)
    {
        var node = CreateBasePropertyAccessExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ true),
            questionDotToken,
            AsName(name));
        node.Flags |= NodeFlags.OptionalChain;
        node.TransformFlags |= TransformFlags.ContainsES2020;
        return node;
    }

    private ElementAccessExpression CreateBaseElementAccessExpression(Node expression, QuestionDotToken? questionDotToken, Node argumentExpression)
    {
        var node = (ElementAccessExpression)CreateBaseDeclaration(SyntaxKind.ElementAccessExpression);
        node.Expression = expression;
        node.QuestionDotToken = questionDotToken;
        node.ArgumentExpression = argumentExpression;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.QuestionDotToken) |
            PropagateChildFlags(node.ArgumentExpression);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal ElementAccessExpression CreateElementAccessExpression(Node expression, Node index)
    {
        var node = CreateBaseElementAccessExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false),
            /*questionDotToken*/ null,
            AsExpression(index));
        if (NodeTests.IsSuperKeyword(expression))
        {
            // super method calls require a lexical 'this'
            // super method calls require 'super' hoisting in ES2017 and ES2018 async functions and async generators
            node.TransformFlags |= TransformFlags.ContainsES2017 |
                TransformFlags.ContainsES2018;
        }
        return node;
    }

    // @api
    internal ElementAccessExpression CreateElementAccessChain(Node expression, QuestionDotToken? questionDotToken, Node index)
    {
        var node = CreateBaseElementAccessExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ true),
            questionDotToken,
            AsExpression(index));
        node.Flags |= NodeFlags.OptionalChain;
        node.TransformFlags |= TransformFlags.ContainsES2020;
        return node;
    }

    private CallExpression CreateBaseCallExpression(Node expression, QuestionDotToken? questionDotToken, NodeArray? typeArguments, NodeArray argumentsArray)
    {
        var node = (CallExpression)CreateBaseDeclaration(SyntaxKind.CallExpression);
        node.Expression = expression;
        node.QuestionDotToken = questionDotToken;
        node.TypeArguments = typeArguments;
        node.Arguments = argumentsArray;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.QuestionDotToken) |
            PropagateChildrenFlags(node.TypeArguments) |
            PropagateChildrenFlags(node.Arguments);
        if (node.TypeArguments != null)
        {
            node.TransformFlags |= TransformFlags.ContainsTypeScript;
        }
        if (Utilities.IsSuperProperty(node.Expression))
        {
            node.TransformFlags |= TransformFlags.ContainsLexicalThis;
        }
        return node;
    }

    // @api
    internal CallExpression CreateCallExpression(Node expression, IReadOnlyList<Node>? typeArguments, IReadOnlyList<Node>? argumentsArray)
    {
        var node = CreateBaseCallExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false),
            /*questionDotToken*/ null,
            AsNodeArray(typeArguments),
            Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(CreateNodeArray(argumentsArray)));
        if (NodeTests.IsImportKeyword(node.Expression))
        {
            node.TransformFlags |= TransformFlags.ContainsDynamicImport;
        }
        return node;
    }

    // @api
    internal CallExpression UpdateCallExpression(CallExpression node, Node expression, IReadOnlyList<Node>? typeArguments, IReadOnlyList<Node> argumentsArray)
    {
        if (UtilitiesPublic.IsCallChain(node))
        {
            return UpdateCallChain(node, expression, node.QuestionDotToken, typeArguments, argumentsArray);
        }
        return node.Expression != expression
                || node.TypeArguments != typeArguments
                || node.Arguments != argumentsArray
            ? Update(CreateCallExpression(expression, typeArguments, argumentsArray), node)
            : node;
    }

    // @api
    internal CallExpression CreateCallChain(Node expression, QuestionDotToken? questionDotToken, IReadOnlyList<Node>? typeArguments, IReadOnlyList<Node>? argumentsArray)
    {
        var node = CreateBaseCallExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ true),
            questionDotToken,
            AsNodeArray(typeArguments),
            Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(CreateNodeArray(argumentsArray)));
        node.Flags |= NodeFlags.OptionalChain;
        node.TransformFlags |= TransformFlags.ContainsES2020;
        return node;
    }

    // @api
    internal CallExpression UpdateCallChain(CallExpression node, Node expression, QuestionDotToken? questionDotToken, IReadOnlyList<Node>? typeArguments, IReadOnlyList<Node> argumentsArray)
    {
        Debug.Assert((node.Flags & NodeFlags.OptionalChain) != 0, "Cannot update a CallExpression using updateCallChain. Use updateCall instead.");
        return node.Expression != expression
                || node.QuestionDotToken != questionDotToken
                || node.TypeArguments != typeArguments
                || node.Arguments != argumentsArray
            ? Update(CreateCallChain(expression, questionDotToken, typeArguments, argumentsArray), node)
            : node;
    }

    // @api
    internal NewExpression CreateNewExpression(Node expression, IReadOnlyList<Node>? typeArguments, IReadOnlyList<Node>? argumentsArray)
    {
        var node = (NewExpression)CreateBaseDeclaration(SyntaxKind.NewExpression);
        node.Expression = Parenthesizer.ParenthesizeExpressionOfNew(expression);
        node.TypeArguments = AsNodeArray(typeArguments);
        node.Arguments = argumentsArray != null ? Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(argumentsArray) : null;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildrenFlags(node.TypeArguments) |
            PropagateChildrenFlags(node.Arguments) |
            TransformFlags.ContainsES2020;
        if (node.TypeArguments != null)
        {
            node.TransformFlags |= TransformFlags.ContainsTypeScript;
        }
        return node;
    }

    // @api
    internal TaggedTemplateExpression CreateTaggedTemplateExpression(Node tag, IReadOnlyList<Node>? typeArguments, Node template)
    {
        var node = (TaggedTemplateExpression)CreateBaseNode(SyntaxKind.TaggedTemplateExpression);
        node.Tag = Parenthesizer.ParenthesizeLeftSideOfAccess(tag, /*optionalChain*/ false);
        node.TypeArguments = AsNodeArray(typeArguments);
        node.Template = template;
        node.TransformFlags |= PropagateChildFlags(node.Tag) |
            PropagateChildrenFlags(node.TypeArguments) |
            PropagateChildFlags(node.Template) |
            TransformFlags.ContainsES2015;
        if (node.TypeArguments != null)
        {
            node.TransformFlags |= TransformFlags.ContainsTypeScript;
        }
        if (Utilities.HasInvalidEscape(node.Template))
        {
            node.TransformFlags |= TransformFlags.ContainsES2018;
        }
        return node;
    }

    // @api
    internal TypeAssertion CreateTypeAssertion(Node type, Node expression)
    {
        var node = (TypeAssertion)CreateBaseNode(SyntaxKind.TypeAssertionExpression);
        node.Expression = Parenthesizer.ParenthesizeOperandOfPrefixUnary(expression);
        node.Type = type;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.Type) |
            TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal TypeAssertion UpdateTypeAssertion(TypeAssertion node, Node type, Node expression)
    {
        return node.Type != type
                || node.Expression != expression
            ? Update(CreateTypeAssertion(type, expression), node)
            : node;
    }

    // @api
    internal ParenthesizedExpression CreateParenthesizedExpression(Node expression)
    {
        var node = (ParenthesizedExpression)CreateBaseNode(SyntaxKind.ParenthesizedExpression);
        node.Expression = expression;
        node.TransformFlags = PropagateChildFlags(node.Expression);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal ParenthesizedExpression UpdateParenthesizedExpression(ParenthesizedExpression node, Node expression)
    {
        return node.Expression != expression
            ? Update(CreateParenthesizedExpression(expression), node)
            : node;
    }

    // @api
    internal FunctionExpression CreateFunctionExpression(
        IReadOnlyList<Node>? modifiers,
        AsteriskToken? asteriskToken,
        Identifier? name,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node>? parameters,
        Node? type,
        Block body)
    {
        var node = (FunctionExpression)CreateBaseDeclaration(SyntaxKind.FunctionExpression);
        node.Modifiers = AsNodeArray(modifiers);
        node.AsteriskToken = asteriskToken;
        node.Name = AsName(name);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = CreateNodeArray(parameters);
        node.Type = type;
        node.Body = body;

        var isAsync = (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Async) != 0;
        var isGenerator = node.AsteriskToken != null;
        var isAsyncGenerator = isAsync && isGenerator;

        node.TransformFlags = PropagateChildrenFlags(node.Modifiers) |
            PropagateChildFlags(node.AsteriskToken) |
            PropagateNameFlags(node.Name) |
            PropagateChildrenFlags(node.TypeParameters) |
            PropagateChildrenFlags(node.Parameters) |
            PropagateChildFlags(node.Type) |
            (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
            (isAsyncGenerator ? TransformFlags.ContainsES2018 :
                isAsync ? TransformFlags.ContainsES2017 :
                isGenerator ? TransformFlags.ContainsGenerator :
                TransformFlags.None) |
            (node.TypeParameters != null || node.Type != null ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
            TransformFlags.ContainsHoistedDeclarationOrCompletion;

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
    internal ArrowFunction CreateArrowFunction(
        IReadOnlyList<Node>? modifiers,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node? type,
        EqualsGreaterThanToken? equalsGreaterThanToken,
        Node body)
    {
        var node = (ArrowFunction)CreateBaseDeclaration(SyntaxKind.ArrowFunction);
        node.Modifiers = AsNodeArray(modifiers);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = CreateNodeArray(parameters);
        node.Type = type;
        node.EqualsGreaterThanToken = equalsGreaterThanToken ?? (EqualsGreaterThanToken)CreateToken(SyntaxKind.EqualsGreaterThanToken);
        node.Body = Parenthesizer.ParenthesizeConciseBodyOfArrowFunction(body);

        var isAsync = (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Async) != 0;

        node.TransformFlags = PropagateChildrenFlags(node.Modifiers) |
            PropagateChildrenFlags(node.TypeParameters) |
            PropagateChildrenFlags(node.Parameters) |
            PropagateChildFlags(node.Type) |
            PropagateChildFlags(node.EqualsGreaterThanToken) |
            (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
            (node.TypeParameters != null || node.Type != null ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
            (isAsync ? TransformFlags.ContainsES2017 | TransformFlags.ContainsLexicalThis : TransformFlags.None) |
            TransformFlags.ContainsES2015;

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
    internal DeleteExpression CreateDeleteExpression(Node expression)
    {
        var node = (DeleteExpression)CreateBaseNode(SyntaxKind.DeleteExpression);
        node.Expression = Parenthesizer.ParenthesizeOperandOfPrefixUnary(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression);
        return node;
    }

    // @api
    internal TypeOfExpression CreateTypeOfExpression(Node expression)
    {
        var node = (TypeOfExpression)CreateBaseNode(SyntaxKind.TypeOfExpression);
        node.Expression = Parenthesizer.ParenthesizeOperandOfPrefixUnary(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression);
        return node;
    }

    // @api
    internal VoidExpression CreateVoidExpression(Node expression)
    {
        var node = (VoidExpression)CreateBaseNode(SyntaxKind.VoidExpression);
        node.Expression = Parenthesizer.ParenthesizeOperandOfPrefixUnary(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression);
        return node;
    }

    // @api
    internal AwaitExpression CreateAwaitExpression(Node expression)
    {
        var node = (AwaitExpression)CreateBaseNode(SyntaxKind.AwaitExpression);
        node.Expression = Parenthesizer.ParenthesizeOperandOfPrefixUnary(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsES2017 |
            TransformFlags.ContainsES2018 |
            TransformFlags.ContainsAwait;
        return node;
    }

    // @api
    internal PrefixUnaryExpression CreatePrefixUnaryExpression(SyntaxKind @operator, Node operand)
    {
        var node = (PrefixUnaryExpression)CreateBaseNode(SyntaxKind.PrefixUnaryExpression);
        node.Operator = @operator;
        node.Operand = Parenthesizer.ParenthesizeOperandOfPrefixUnary(operand);
        node.TransformFlags |= PropagateChildFlags(node.Operand);
        // Only set this flag for non-generated identifiers and non-"local" names. See the
        // comment in `visitPreOrPostfixUnaryExpression` in module.ts
        if (
            (@operator == SyntaxKind.PlusPlusToken || @operator == SyntaxKind.MinusMinusToken) &&
            NodeTests.IsIdentifier(node.Operand) &&
            !UtilitiesPublic.IsGeneratedIdentifier(node.Operand) &&
            !FactoryUtilities.IsLocalName(node.Operand))
        {
            node.TransformFlags |= TransformFlags.ContainsUpdateExpressionForIdentifier;
        }
        return node;
    }

    // @api
    internal PostfixUnaryExpression CreatePostfixUnaryExpression(Node operand, SyntaxKind @operator)
    {
        var node = (PostfixUnaryExpression)CreateBaseNode(SyntaxKind.PostfixUnaryExpression);
        node.Operator = @operator;
        node.Operand = Parenthesizer.ParenthesizeOperandOfPostfixUnary(operand);
        node.TransformFlags |= PropagateChildFlags(node.Operand);
        // Only set this flag for non-generated identifiers and non-"local" names. See the
        // comment in `visitPreOrPostfixUnaryExpression` in module.ts
        if (
            NodeTests.IsIdentifier(node.Operand) &&
            !UtilitiesPublic.IsGeneratedIdentifier(node.Operand) &&
            !FactoryUtilities.IsLocalName(node.Operand))
        {
            node.TransformFlags |= TransformFlags.ContainsUpdateExpressionForIdentifier;
        }
        return node;
    }

    /// <summary>createBinaryExpression with an operator token (the parser's form of `BinaryOperator | BinaryOperatorToken`).</summary>
    // @api
    internal BinaryExpression CreateBinaryExpression(Node left, Node @operator, Node right)
    {
        var node = (BinaryExpression)CreateBaseDeclaration(SyntaxKind.BinaryExpression);
        var operatorToken = AsToken(@operator);
        var operatorKind = operatorToken.Kind;
        node.Left = Parenthesizer.ParenthesizeLeftSideOfBinary(operatorKind, left);
        node.OperatorToken = operatorToken;
        node.Right = Parenthesizer.ParenthesizeRightSideOfBinary(operatorKind, node.Left, right);
        node.TransformFlags |= PropagateChildFlags(node.Left) |
            PropagateChildFlags(node.OperatorToken) |
            PropagateChildFlags(node.Right);
        if (operatorKind == SyntaxKind.QuestionQuestionToken)
        {
            node.TransformFlags |= TransformFlags.ContainsES2020;
        }
        else if (operatorKind == SyntaxKind.EqualsToken)
        {
            if (NodeTests.IsObjectLiteralExpression(node.Left))
            {
                node.TransformFlags |= TransformFlags.ContainsES2015 |
                    TransformFlags.ContainsES2018 |
                    TransformFlags.ContainsDestructuringAssignment |
                    PropagateAssignmentPatternFlags(node.Left);
            }
            else if (NodeTests.IsArrayLiteralExpression(node.Left))
            {
                node.TransformFlags |= TransformFlags.ContainsES2015 |
                    TransformFlags.ContainsDestructuringAssignment |
                    PropagateAssignmentPatternFlags(node.Left);
            }
        }
        else if (operatorKind == SyntaxKind.AsteriskAsteriskToken || operatorKind == SyntaxKind.AsteriskAsteriskEqualsToken)
        {
            node.TransformFlags |= TransformFlags.ContainsES2016;
        }
        else if (Utilities.IsLogicalOrCoalescingAssignmentOperator(operatorKind))
        {
            node.TransformFlags |= TransformFlags.ContainsES2021;
        }
        if (operatorKind == SyntaxKind.InKeyword && NodeTests.IsPrivateIdentifier(node.Left))
        {
            node.TransformFlags |= TransformFlags.ContainsPrivateIdentifierInExpression;
        }

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    private static TransformFlags PropagateAssignmentPatternFlags(Node node)
    {
        return FactoryUtilities.ContainsObjectRestOrSpread(node) ? TransformFlags.ContainsObjectRestOrSpread : TransformFlags.None;
    }

    // @api
    internal ConditionalExpression CreateConditionalExpression(Node condition, QuestionToken? questionToken, Node whenTrue, ColonToken? colonToken, Node whenFalse)
    {
        var node = (ConditionalExpression)CreateBaseNode(SyntaxKind.ConditionalExpression);
        node.Condition = Parenthesizer.ParenthesizeConditionOfConditionalExpression(condition);
        node.QuestionToken = questionToken ?? (QuestionToken)CreateToken(SyntaxKind.QuestionToken);
        node.WhenTrue = Parenthesizer.ParenthesizeBranchOfConditionalExpression(whenTrue);
        node.ColonToken = colonToken ?? (ColonToken)CreateToken(SyntaxKind.ColonToken);
        node.WhenFalse = Parenthesizer.ParenthesizeBranchOfConditionalExpression(whenFalse);
        node.TransformFlags |= PropagateChildFlags(node.Condition) |
            PropagateChildFlags(node.QuestionToken) |
            PropagateChildFlags(node.WhenTrue) |
            PropagateChildFlags(node.ColonToken) |
            PropagateChildFlags(node.WhenFalse);
        node.FlowNodeWhenFalse = null;
        node.FlowNodeWhenTrue = null;
        return node;
    }

    // @api
    internal TemplateExpression CreateTemplateExpression(TemplateHead head, IReadOnlyList<Node> templateSpans)
    {
        var node = (TemplateExpression)CreateBaseNode(SyntaxKind.TemplateExpression);
        node.Head = head;
        node.TemplateSpans = CreateNodeArray(templateSpans);
        node.TransformFlags |= PropagateChildFlags(node.Head) |
            PropagateChildrenFlags(node.TemplateSpans) |
            TransformFlags.ContainsES2015;
        return node;
    }

    private static string CheckTemplateLiteralLikeNode(SyntaxKind kind, string? text, string? rawText, TokenFlags templateFlags = TokenFlags.None)
    {
        Debug.Assert((templateFlags & ~TokenFlags.TemplateLiteralLikeFlags) == 0, "Unsupported template flags.");
        // NOTE: without the assignment to `undefined`, we don't narrow the initial type of `cooked`.
        object? cooked = null;
        if (rawText != null && rawText != text)
        {
            cooked = GetCookedText(kind, rawText);
            if (cooked is not string)
            {
                // typeof cooked === "object": the invalid-value sentinel.
                Debug.Fail("Invalid raw text");
            }
        }
        if (text == null)
        {
            if (cooked == null)
            {
                Debug.Fail("Arguments 'text' and 'rawText' may not both be undefined.");
            }
            text = (string)cooked;
        }
        else if (cooked != null)
        {
            Debug.Assert(text == (string)cooked, "Expected argument 'text' to be the normalized (i.e. 'cooked') version of argument 'rawText'.");
        }
        return text;
    }

    private static TransformFlags GetTransformFlagsOfTemplateLiteralLike(TokenFlags templateFlags)
    {
        var transformFlags = TransformFlags.ContainsES2015;
        if (templateFlags != 0)
        {
            transformFlags |= TransformFlags.ContainsES2018;
        }
        return transformFlags;
    }

    // NOTE: `createTemplateLiteralLikeToken` and `createTemplateLiteralLikeDeclaration` are identical except for
    //       the underlying nodes they create. To avoid polymorphism due to two different node shapes, these
    //       functions are intentionally duplicated.
    private Node CreateTemplateLiteralLikeToken(SyntaxKind kind, string text, string? rawText, TokenFlags? templateFlags)
    {
        var node = CreateBaseToken(kind);
        // `templateFlags! & TokenFlags.TemplateLiteralLikeFlags`: `undefined & x` is 0.
        var nodeTemplateFlags = (templateFlags ?? TokenFlags.None) & TokenFlags.TemplateLiteralLikeFlags;
        switch (node)
        {
            case TemplateHead head:
                head.Text = text;
                head.RawText = rawText;
                head.TemplateFlags = nodeTemplateFlags;
                break;
            case TemplateMiddle middle:
                middle.Text = text;
                middle.RawText = rawText;
                middle.TemplateFlags = nodeTemplateFlags;
                break;
            case TemplateTail tail:
                tail.Text = text;
                tail.RawText = rawText;
                tail.TemplateFlags = nodeTemplateFlags;
                break;
            default:
                // (TemplateLiteralLikeNode: the kinds above, or NoSubstitutionTemplateLiteral when called directly.)
                var literal = (NoSubstitutionTemplateLiteral)node;
                literal.Text = text;
                literal.RawText = rawText;
                literal.TemplateFlags = nodeTemplateFlags;
                break;
        }
        node.TransformFlags = GetTransformFlagsOfTemplateLiteralLike(nodeTemplateFlags);
        return node;
    }

    private NoSubstitutionTemplateLiteral CreateTemplateLiteralLikeDeclaration(SyntaxKind kind, string text, string? rawText, TokenFlags? templateFlags)
    {
        var node = (NoSubstitutionTemplateLiteral)CreateBaseDeclaration(kind);
        node.Text = text;
        node.RawText = rawText;
        node.TemplateFlags = (templateFlags ?? TokenFlags.None) & TokenFlags.TemplateLiteralLikeFlags;
        node.TransformFlags = GetTransformFlagsOfTemplateLiteralLike(node.TemplateFlags);
        return node;
    }

    // @api
    internal Node CreateTemplateLiteralLikeNode(SyntaxKind kind, string text, string? rawText, TokenFlags? templateFlags)
    {
        if (kind == SyntaxKind.NoSubstitutionTemplateLiteral)
        {
            return CreateTemplateLiteralLikeDeclaration(kind, text, rawText, templateFlags);
        }
        return CreateTemplateLiteralLikeToken(kind, text, rawText, templateFlags);
    }

    // @api
    internal TemplateHead CreateTemplateHead(string? text, string? rawText = null, TokenFlags? templateFlags = null)
    {
        text = CheckTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text, rawText, templateFlags ?? TokenFlags.None);
        return (TemplateHead)CreateTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text, rawText, templateFlags);
    }

    // @api
    internal TemplateMiddle CreateTemplateMiddle(string? text, string? rawText = null, TokenFlags? templateFlags = null)
    {
        text = CheckTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text, rawText, templateFlags ?? TokenFlags.None);
        return (TemplateMiddle)CreateTemplateLiteralLikeNode(SyntaxKind.TemplateMiddle, text, rawText, templateFlags);
    }

    // @api
    internal TemplateTail CreateTemplateTail(string? text, string? rawText = null, TokenFlags? templateFlags = null)
    {
        text = CheckTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text, rawText, templateFlags ?? TokenFlags.None);
        return (TemplateTail)CreateTemplateLiteralLikeNode(SyntaxKind.TemplateTail, text, rawText, templateFlags);
    }

    // @api
    internal NoSubstitutionTemplateLiteral CreateNoSubstitutionTemplateLiteral(string? text, string? rawText = null, TokenFlags? templateFlags = null)
    {
        text = CheckTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text, rawText, templateFlags ?? TokenFlags.None);
        return CreateTemplateLiteralLikeDeclaration(SyntaxKind.NoSubstitutionTemplateLiteral, text, rawText, templateFlags);
    }

    // @api
    internal YieldExpression CreateYieldExpression(AsteriskToken? asteriskToken, Node? expression)
    {
        Debug.Assert(asteriskToken == null || expression != null, "A `YieldExpression` with an asteriskToken must have an expression.");
        var node = (YieldExpression)CreateBaseNode(SyntaxKind.YieldExpression);
        node.Expression = expression != null ? Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression) : null;
        node.AsteriskToken = asteriskToken;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.AsteriskToken) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsES2018 |
            TransformFlags.ContainsYield;
        return node;
    }

    // @api
    internal SpreadElement CreateSpreadElement(Node expression)
    {
        var node = (SpreadElement)CreateBaseNode(SyntaxKind.SpreadElement);
        node.Expression = Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsRestOrSpread;
        return node;
    }

    // @api
    internal ClassExpression CreateClassExpression(
        IReadOnlyList<Node>? modifiers,
        Identifier? name,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node>? heritageClauses,
        IReadOnlyList<Node> members)
    {
        var node = (ClassExpression)CreateBaseDeclaration(SyntaxKind.ClassExpression);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.HeritageClauses = AsNodeArray(heritageClauses);
        node.Members = CreateNodeArray(members);
        node.TransformFlags |= PropagateChildrenFlags(node.Modifiers) |
            PropagateNameFlags(node.Name) |
            PropagateChildrenFlags(node.TypeParameters) |
            PropagateChildrenFlags(node.HeritageClauses) |
            PropagateChildrenFlags(node.Members) |
            (node.TypeParameters != null ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
            TransformFlags.ContainsES2015;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal OmittedExpression CreateOmittedExpression()
    {
        return (OmittedExpression)CreateBaseNode(SyntaxKind.OmittedExpression);
    }

    // @api
    internal ExpressionWithTypeArguments CreateExpressionWithTypeArguments(Node expression, IReadOnlyList<Node>? typeArguments)
    {
        var node = (ExpressionWithTypeArguments)CreateBaseNode(SyntaxKind.ExpressionWithTypeArguments);
        node.Expression = Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false);
        node.TypeArguments = typeArguments != null ? Parenthesizer.ParenthesizeTypeArguments(typeArguments) : null;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildrenFlags(node.TypeArguments) |
            TransformFlags.ContainsES2015;
        return node;
    }

    // @api
    internal ExpressionWithTypeArguments UpdateExpressionWithTypeArguments(ExpressionWithTypeArguments node, Node expression, IReadOnlyList<Node>? typeArguments)
    {
        return node.Expression != expression
                || node.TypeArguments != typeArguments
            ? Update(CreateExpressionWithTypeArguments(expression, typeArguments), node)
            : node;
    }

    // @api
    internal AsExpression CreateAsExpression(Node expression, Node type)
    {
        var node = (AsExpression)CreateBaseNode(SyntaxKind.AsExpression);
        node.Expression = expression;
        node.Type = type;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.Type) |
            TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal AsExpression UpdateAsExpression(AsExpression node, Node expression, Node type)
    {
        return node.Expression != expression
                || node.Type != type
            ? Update(CreateAsExpression(expression, type), node)
            : node;
    }

    // @api
    internal NonNullExpression CreateNonNullExpression(Node expression)
    {
        var node = (NonNullExpression)CreateBaseNode(SyntaxKind.NonNullExpression);
        node.Expression = Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal NonNullExpression UpdateNonNullExpression(NonNullExpression node, Node expression)
    {
        if (UtilitiesPublic.IsNonNullChain(node))
        {
            return UpdateNonNullChain(node, expression);
        }
        return node.Expression != expression
            ? Update(CreateNonNullExpression(expression), node)
            : node;
    }

    // @api
    internal SatisfiesExpression CreateSatisfiesExpression(Node expression, Node type)
    {
        var node = (SatisfiesExpression)CreateBaseNode(SyntaxKind.SatisfiesExpression);
        node.Expression = expression;
        node.Type = type;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.Type) |
            TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal SatisfiesExpression UpdateSatisfiesExpression(SatisfiesExpression node, Node expression, Node type)
    {
        return node.Expression != expression
                || node.Type != type
            ? Update(CreateSatisfiesExpression(expression, type), node)
            : node;
    }

    // @api
    internal NonNullExpression CreateNonNullChain(Node expression)
    {
        var node = (NonNullExpression)CreateBaseNode(SyntaxKind.NonNullExpression);
        node.Flags |= NodeFlags.OptionalChain;
        node.Expression = Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ true);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsTypeScript;
        return node;
    }

    // @api
    internal NonNullExpression UpdateNonNullChain(NonNullExpression node, Node expression)
    {
        Debug.Assert((node.Flags & NodeFlags.OptionalChain) != 0, "Cannot update a NonNullExpression using updateNonNullChain. Use updateNonNullExpression instead.");
        return node.Expression != expression
            ? Update(CreateNonNullChain(expression), node)
            : node;
    }

    // @api
    internal MetaProperty CreateMetaProperty(SyntaxKind keywordToken, Identifier name)
    {
        var node = (MetaProperty)CreateBaseNode(SyntaxKind.MetaProperty);
        node.KeywordToken = keywordToken;
        node.Name = name;
        node.TransformFlags |= PropagateChildFlags(node.Name);
        switch (keywordToken)
        {
            case SyntaxKind.NewKeyword:
                node.TransformFlags |= TransformFlags.ContainsES2015;
                break;
            case SyntaxKind.ImportKeyword:
                node.TransformFlags |= TransformFlags.ContainsES2020;
                break;
            default:
                Debug.AssertNever(keywordToken);
                return null!;
        }

        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    //
    // Misc
    //

    // @api
    internal TemplateSpan CreateTemplateSpan(Node expression, Node literal)
    {
        var node = (TemplateSpan)CreateBaseNode(SyntaxKind.TemplateSpan);
        node.Expression = expression;
        node.Literal = literal;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.Literal) |
            TransformFlags.ContainsES2015;
        return node;
    }

    // @api
    internal SemicolonClassElement CreateSemicolonClassElement()
    {
        var node = (SemicolonClassElement)CreateBaseNode(SyntaxKind.SemicolonClassElement);
        node.TransformFlags |= TransformFlags.ContainsES2015;
        return node;
    }
}
