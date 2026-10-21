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

internal sealed partial class NodeFactory
{
    //
    // Property and element access
    //

    private PropertyAccessExpression CreateBasePropertyAccessExpression(Node expression, QuestionDotToken? questionDotToken, Node name)
    {
        var node = CreateBaseDeclaration<PropertyAccessExpression>(SyntaxKind.PropertyAccessExpression);
        node.Expression = expression;
        node.QuestionDotToken = questionDotToken;
        node.Name = name;
        node.TransformFlags = PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.QuestionDotToken) |
            (NodeTests.IsIdentifier(node.Name) ?
                PropagateIdentifierNameFlags(node.Name) :
                PropagateChildFlags(node.Name) | TransformFlags.ContainsPrivateIdentifierInExpression);

        // node.jsDoc: initialized by parser (JsDocContainer)
        // node.flowNode: initialized by binder (FlowContainer)
        return node;
    }

    // The factory object exposes createPropertyAccessExpression wrapped when
    // `flags & NodeFactoryFlags.NoIndentationOnFreshPropertyAccess` (nodeFactory.ts 635-641): the wrapper is this method,
    // the unwrapped function (used by updatePropertyAccessExpression) is CreatePropertyAccessExpressionWorker.
    internal PropertyAccessExpression CreatePropertyAccessExpression(Node expression, Node name)
    {
        var node = CreatePropertyAccessExpressionWorker(expression, name);
        if ((flags & NodeFactoryFlags.NoIndentationOnFreshPropertyAccess) != 0)
        {
            EmitNode.SetEmitFlags(node, EmitFlags.NoIndentation);
        }
        return node;
    }

    private PropertyAccessExpression CreatePropertyAccessExpressionWorker(Node expression, Node name)
    {
        var node = CreateBasePropertyAccessExpression(
            parenthesizerRules.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false),
            /*questionDotToken*/ null,
            AsName(name)!);
        if (NodeTests.IsSuperKeyword(expression))
        {
            // super method calls require a lexical 'this'
            // super method calls require 'super' hoisting in ES2017 and ES2018 async functions and async generators
            node.TransformFlags |= TransformFlags.ContainsES2017 |
                TransformFlags.ContainsES2018;
        }
        return node;
    }

    // The wrapper of nodeFactory.ts 635-641 for createPropertyAccessChain (see CreatePropertyAccessExpression).
    internal PropertyAccessExpression CreatePropertyAccessChain(Node expression, QuestionDotToken? questionDotToken, Node name)
    {
        var node = CreatePropertyAccessChainWorker(expression, questionDotToken, name);
        if ((flags & NodeFactoryFlags.NoIndentationOnFreshPropertyAccess) != 0)
        {
            EmitNode.SetEmitFlags(node, EmitFlags.NoIndentation);
        }
        return node;
    }

    private PropertyAccessExpression CreatePropertyAccessChainWorker(Node expression, QuestionDotToken? questionDotToken, Node name)
    {
        var node = CreateBasePropertyAccessExpression(
            parenthesizerRules.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ true),
            questionDotToken,
            AsName(name)!);
        node.Flags |= NodeFlags.OptionalChain;
        node.TransformFlags |= TransformFlags.ContainsES2020;
        return node;
    }

    private ElementAccessExpression CreateBaseElementAccessExpression(Node expression, QuestionDotToken? questionDotToken, Node argumentExpression)
    {
        var node = CreateBaseDeclaration<ElementAccessExpression>(SyntaxKind.ElementAccessExpression);
        node.Expression = expression;
        node.QuestionDotToken = questionDotToken;
        node.ArgumentExpression = argumentExpression;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.QuestionDotToken) |
            PropagateChildFlags(node.ArgumentExpression);

        // node.jsDoc: initialized by parser (JsDocContainer)
        // node.flowNode: initialized by binder (FlowContainer)
        return node;
    }

    // TS: `index: number | Expression` and `asExpression(index)`; the parser only passes an Expression, for which
    // asExpression is the identity (the string and number cases are not reachable from the C# signature).
    internal ElementAccessExpression CreateElementAccessExpression(Node expression, Node index)
    {
        var node = CreateBaseElementAccessExpression(
            parenthesizerRules.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false),
            /*questionDotToken*/ null,
            index);
        if (NodeTests.IsSuperKeyword(expression))
        {
            // super method calls require a lexical 'this'
            // super method calls require 'super' hoisting in ES2017 and ES2018 async functions and async generators
            node.TransformFlags |= TransformFlags.ContainsES2017 |
                TransformFlags.ContainsES2018;
        }
        return node;
    }

    internal ElementAccessExpression CreateElementAccessChain(Node expression, QuestionDotToken? questionDotToken, Node index)
    {
        var node = CreateBaseElementAccessExpression(
            parenthesizerRules.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ true),
            questionDotToken,
            index);
        node.Flags |= NodeFlags.OptionalChain;
        node.TransformFlags |= TransformFlags.ContainsES2020;
        return node;
    }

    //
    // Call and new
    //

    private CallExpression CreateBaseCallExpression(Node expression, QuestionDotToken? questionDotToken, NodeArray? typeArguments, NodeArray argumentsArray)
    {
        var node = CreateBaseDeclaration<CallExpression>(SyntaxKind.CallExpression);
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

    internal CallExpression CreateCallExpression(Node expression, IReadOnlyList<Node>? typeArguments, IReadOnlyList<Node>? argumentsArray)
    {
        var node = CreateBaseCallExpression(
            parenthesizerRules.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false),
            /*questionDotToken*/ null,
            AsNodeArray(typeArguments),
            parenthesizerRules.ParenthesizeExpressionsOfCommaDelimitedList(CreateNodeArray(argumentsArray)));
        if (NodeTests.IsImportKeyword(node.Expression))
        {
            node.TransformFlags |= TransformFlags.ContainsDynamicImport;
        }
        return node;
    }

    internal CallExpression UpdateCallExpression(CallExpression node, Node expression, IReadOnlyList<Node>? typeArguments, IReadOnlyList<Node> argumentsArray)
    {
        if (UtilitiesPublic.IsCallChain(node))
        {
            return UpdateCallChain(node, expression, node.QuestionDotToken, typeArguments, argumentsArray);
        }
        return node.Expression != expression
                || !ReferenceEquals(node.TypeArguments, typeArguments)
                || !ReferenceEquals(node.Arguments, argumentsArray)
            ? Update(CreateCallExpression(expression, typeArguments, argumentsArray), node)
            : node;
    }

    internal CallExpression CreateCallChain(Node expression, QuestionDotToken? questionDotToken, IReadOnlyList<Node>? typeArguments, IReadOnlyList<Node>? argumentsArray)
    {
        var node = CreateBaseCallExpression(
            parenthesizerRules.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ true),
            questionDotToken,
            AsNodeArray(typeArguments),
            parenthesizerRules.ParenthesizeExpressionsOfCommaDelimitedList(CreateNodeArray(argumentsArray)));
        node.Flags |= NodeFlags.OptionalChain;
        node.TransformFlags |= TransformFlags.ContainsES2020;
        return node;
    }

    internal CallExpression UpdateCallChain(CallExpression node, Node expression, QuestionDotToken? questionDotToken, IReadOnlyList<Node>? typeArguments, IReadOnlyList<Node> argumentsArray)
    {
        Debug.Assert((node.Flags & NodeFlags.OptionalChain) != 0, "Cannot update a CallExpression using updateCallChain. Use updateCall instead.");
        return node.Expression != expression
                || node.QuestionDotToken != questionDotToken
                || !ReferenceEquals(node.TypeArguments, typeArguments)
                || !ReferenceEquals(node.Arguments, argumentsArray)
            ? Update(CreateCallChain(expression, questionDotToken, typeArguments, argumentsArray), node)
            : node;
    }

    internal NewExpression CreateNewExpression(Node expression, IReadOnlyList<Node>? typeArguments, IReadOnlyList<Node>? argumentsArray)
    {
        var node = CreateBaseDeclaration<NewExpression>(SyntaxKind.NewExpression);
        node.Expression = parenthesizerRules.ParenthesizeExpressionOfNew(expression);
        node.TypeArguments = AsNodeArray(typeArguments);
        node.Arguments = argumentsArray != null ? parenthesizerRules.ParenthesizeExpressionsOfCommaDelimitedList(argumentsArray) : null;
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

    internal TaggedTemplateExpression CreateTaggedTemplateExpression(Node tag, IReadOnlyList<Node>? typeArguments, Node template)
    {
        var node = CreateBaseNode<TaggedTemplateExpression>(SyntaxKind.TaggedTemplateExpression);
        node.Tag = parenthesizerRules.ParenthesizeLeftSideOfAccess(tag, /*optionalChain*/ false);
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

    internal TypeAssertion CreateTypeAssertion(Node type, Node expression)
    {
        var node = CreateBaseNode<TypeAssertion>(SyntaxKind.TypeAssertionExpression);
        node.Expression = parenthesizerRules.ParenthesizeOperandOfPrefixUnary(expression);
        node.Type = type;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.Type) |
            TransformFlags.ContainsTypeScript;
        return node;
    }

    internal TypeAssertion UpdateTypeAssertion(TypeAssertion node, Node type, Node expression)
    {
        return node.Type != type
                || node.Expression != expression
            ? Update(CreateTypeAssertion(type, expression), node)
            : node;
    }

    internal ParenthesizedExpression CreateParenthesizedExpression(Node expression)
    {
        var node = CreateBaseNode<ParenthesizedExpression>(SyntaxKind.ParenthesizedExpression);
        node.Expression = expression;
        node.TransformFlags = PropagateChildFlags(node.Expression);

        // node.jsDoc: initialized by parser (JsDocContainer)
        return node;
    }

    internal ParenthesizedExpression UpdateParenthesizedExpression(ParenthesizedExpression node, Node expression)
    {
        return node.Expression != expression
            ? Update(CreateParenthesizedExpression(expression), node)
            : node;
    }

    //
    // Function and arrow function expressions
    //

    internal FunctionExpression CreateFunctionExpression(
        IReadOnlyList<Node>? modifiers,
        AsteriskToken? asteriskToken,
        Node? name,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node>? parameters,
        Node? type,
        Block body)
    {
        var node = CreateBaseDeclaration<FunctionExpression>(SyntaxKind.FunctionExpression);
        node.Modifiers = AsNodeArray(modifiers);
        node.AsteriskToken = asteriskToken;
        node.Name = (Identifier?)AsName(name);
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

        // node.typeArguments: used in quick info
        // node.jsDoc: initialized by parser (JsDocContainer)
        // node.locals, node.nextContainer: initialized by binder (LocalsContainer)
        // node.flowNode: initialized by binder (FlowContainer)
        // node.endFlowNode, node.returnFlowNode: undefined
        return node;
    }

    internal ArrowFunction CreateArrowFunction(
        IReadOnlyList<Node>? modifiers,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node? type,
        EqualsGreaterThanToken? equalsGreaterThanToken,
        Node body)
    {
        var node = CreateBaseDeclaration<ArrowFunction>(SyntaxKind.ArrowFunction);
        node.Modifiers = AsNodeArray(modifiers);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = CreateNodeArray(parameters);
        node.Type = type;
        node.EqualsGreaterThanToken = equalsGreaterThanToken ?? (EqualsGreaterThanToken)CreateToken(SyntaxKind.EqualsGreaterThanToken);
        node.Body = parenthesizerRules.ParenthesizeConciseBodyOfArrowFunction(body);

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

        // node.typeArguments: used in quick info
        // node.jsDoc: initialized by parser (JsDocContainer)
        // node.locals, node.nextContainer: initialized by binder (LocalsContainer)
        // node.flowNode: initialized by binder (FlowContainer)
        // node.endFlowNode, node.returnFlowNode: undefined
        return node;
    }

    //
    // Unary expressions
    //

    internal DeleteExpression CreateDeleteExpression(Node expression)
    {
        var node = CreateBaseNode<DeleteExpression>(SyntaxKind.DeleteExpression);
        node.Expression = parenthesizerRules.ParenthesizeOperandOfPrefixUnary(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression);
        return node;
    }

    internal TypeOfExpression CreateTypeOfExpression(Node expression)
    {
        var node = CreateBaseNode<TypeOfExpression>(SyntaxKind.TypeOfExpression);
        node.Expression = parenthesizerRules.ParenthesizeOperandOfPrefixUnary(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression);
        return node;
    }

    internal VoidExpression CreateVoidExpression(Node expression)
    {
        var node = CreateBaseNode<VoidExpression>(SyntaxKind.VoidExpression);
        node.Expression = parenthesizerRules.ParenthesizeOperandOfPrefixUnary(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression);
        return node;
    }

    internal AwaitExpression CreateAwaitExpression(Node expression)
    {
        var node = CreateBaseNode<AwaitExpression>(SyntaxKind.AwaitExpression);
        node.Expression = parenthesizerRules.ParenthesizeOperandOfPrefixUnary(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsES2017 |
            TransformFlags.ContainsES2018 |
            TransformFlags.ContainsAwait;
        return node;
    }

    internal PrefixUnaryExpression CreatePrefixUnaryExpression(SyntaxKind @operator, Node operand)
    {
        var node = CreateBaseNode<PrefixUnaryExpression>(SyntaxKind.PrefixUnaryExpression);
        node.Operator = @operator;
        node.Operand = parenthesizerRules.ParenthesizeOperandOfPrefixUnary(operand);
        node.TransformFlags |= PropagateChildFlags(node.Operand);
        // Only set this flag for non-generated identifiers and non-"local" names. See the
        // comment in `visitPreOrPostfixUnaryExpression` in module.ts
        if (
            (@operator == SyntaxKind.PlusPlusToken || @operator == SyntaxKind.MinusMinusToken) &&
            NodeTests.IsIdentifier(node.Operand) &&
            !UtilitiesPublic.IsGeneratedIdentifier(node.Operand) &&
            !FactoryUtilities.IsLocalName((Identifier)node.Operand))
        {
            node.TransformFlags |= TransformFlags.ContainsUpdateExpressionForIdentifier;
        }
        return node;
    }

    internal PostfixUnaryExpression CreatePostfixUnaryExpression(Node operand, SyntaxKind @operator)
    {
        var node = CreateBaseNode<PostfixUnaryExpression>(SyntaxKind.PostfixUnaryExpression);
        node.Operator = @operator;
        node.Operand = parenthesizerRules.ParenthesizeOperandOfPostfixUnary(operand);
        node.TransformFlags |= PropagateChildFlags(node.Operand);
        // Only set this flag for non-generated identifiers and non-"local" names. See the
        // comment in `visitPreOrPostfixUnaryExpression` in module.ts
        if (
            NodeTests.IsIdentifier(node.Operand) &&
            !UtilitiesPublic.IsGeneratedIdentifier(node.Operand) &&
            !FactoryUtilities.IsLocalName((Identifier)node.Operand))
        {
            node.TransformFlags |= TransformFlags.ContainsUpdateExpressionForIdentifier;
        }
        return node;
    }

    //
    // Binary and conditional expressions
    //

    // TS: `operator: BinaryOperator | BinaryOperatorToken` and `asToken(operator)`; one overload per alternative.
    internal BinaryExpression CreateBinaryExpression(Node left, Node @operator, Node right)
    {
        return CreateBinaryExpressionWorker(left, AsToken(@operator), right);
    }

    internal BinaryExpression CreateBinaryExpression(Node left, SyntaxKind @operator, Node right)
    {
        return CreateBinaryExpressionWorker(left, AsToken(@operator), right);
    }

    private BinaryExpression CreateBinaryExpressionWorker(Node left, Node operatorToken, Node right)
    {
        var node = CreateBaseDeclaration<BinaryExpression>(SyntaxKind.BinaryExpression);
        var operatorKind = operatorToken.Kind;
        node.Left = parenthesizerRules.ParenthesizeLeftSideOfBinary(operatorKind, left);
        node.OperatorToken = operatorToken;
        node.Right = parenthesizerRules.ParenthesizeRightSideOfBinary(operatorKind, node.Left, right);
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

        // node.jsDoc: initialized by parser (JsDocContainer)
        return node;
    }

    internal static TransformFlags PropagateAssignmentPatternFlags(Node node)
    {
        return FactoryUtilities.ContainsObjectRestOrSpread(node) ? TransformFlags.ContainsObjectRestOrSpread : TransformFlags.None;
    }

    internal ConditionalExpression CreateConditionalExpression(Node condition, QuestionToken? questionToken, Node whenTrue, ColonToken? colonToken, Node whenFalse)
    {
        var node = CreateBaseNode<ConditionalExpression>(SyntaxKind.ConditionalExpression);
        node.Condition = parenthesizerRules.ParenthesizeConditionOfConditionalExpression(condition);
        node.QuestionToken = questionToken ?? (QuestionToken)CreateToken(SyntaxKind.QuestionToken);
        node.WhenTrue = parenthesizerRules.ParenthesizeBranchOfConditionalExpression(whenTrue);
        node.ColonToken = colonToken ?? (ColonToken)CreateToken(SyntaxKind.ColonToken);
        node.WhenFalse = parenthesizerRules.ParenthesizeBranchOfConditionalExpression(whenFalse);
        node.TransformFlags |= PropagateChildFlags(node.Condition) |
            PropagateChildFlags(node.QuestionToken) |
            PropagateChildFlags(node.WhenTrue) |
            PropagateChildFlags(node.ColonToken) |
            PropagateChildFlags(node.WhenFalse);
        // node.flowNodeWhenFalse, node.flowNodeWhenTrue: undefined
        return node;
    }

    //
    // Template expressions and template literal-like nodes
    //

    internal TemplateExpression CreateTemplateExpression(TemplateHead head, IReadOnlyList<Node> templateSpans)
    {
        var node = CreateBaseNode<TemplateExpression>(SyntaxKind.TemplateExpression);
        node.Head = head;
        node.TemplateSpans = CreateNodeArray(templateSpans);
        node.TransformFlags |= PropagateChildFlags(node.Head) |
            PropagateChildrenFlags(node.TemplateSpans) |
            TransformFlags.ContainsES2015;
        return node;
    }

    /// <summary>
    /// nodeFactory.ts getCookedText: the cooked text of the raw text of a template literal-like token, or null
    /// (TypeScript: `invalidValueSentinel`) when the raw text is invalid. TypeScript keeps one module-level
    /// scanner (`rawTextScanner`); a fresh scanner per call keeps this thread-safe (the parser never reaches it).
    /// </summary>
    private static string? GetCookedText(SyntaxKind kind, string rawText)
    {
        var rawTextScanner = Scanner.CreateScanner(ScriptTarget.Latest, false, LanguageVariant.Standard);
        switch (kind)
        {
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                rawTextScanner.SetText("`" + rawText + "`");
                break;
            case SyntaxKind.TemplateHead:
                rawTextScanner.SetText("`" + rawText + "${");
                break;
            case SyntaxKind.TemplateMiddle:
                rawTextScanner.SetText("}" + rawText + "${");
                break;
            case SyntaxKind.TemplateTail:
                rawTextScanner.SetText("}" + rawText + "`");
                break;
        }

        var token = rawTextScanner.Scan();
        if (token == SyntaxKind.CloseBraceToken)
        {
            token = rawTextScanner.ReScanTemplateToken(false);
        }

        if (rawTextScanner.IsUnterminated())
        {
            return null;
        }

        string? tokenValue = null;
        switch (token)
        {
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateHead:
            case SyntaxKind.TemplateMiddle:
            case SyntaxKind.TemplateTail:
                tokenValue = rawTextScanner.GetTokenValue();
                break;
        }

        if (tokenValue is null || rawTextScanner.Scan() != SyntaxKind.EndOfFileToken)
        {
            return null;
        }

        return tokenValue;
    }

    private static string CheckTemplateLiteralLikeNode(SyntaxKind kind, string? text, string? rawText, TokenFlags templateFlags = TokenFlags.None)
    {
        Debug.Assert((templateFlags & ~TokenFlags.TemplateLiteralLikeFlags) == 0, "Unsupported template flags.");
        // GetCookedText yields a string, or a non-string value (TS: `invalidValueSentinel`, an object) when rawText is invalid.
        object? cooked = null;
        if (rawText != null && !string.Equals(rawText, text, StringComparison.Ordinal))
        {
            cooked = GetCookedText(kind, rawText);
            if (cooked is not string)
            {
                Debug.Fail("Invalid raw text");
            }
        }
        if (text == null)
        {
            if (cooked == null)
            {
                Debug.Fail("Arguments 'text' and 'rawText' may not both be undefined.");
            }
            text = (string)cooked!;
        }
        else if (cooked != null)
        {
            Debug.Assert(string.Equals(text, (string)cooked, StringComparison.Ordinal), "Expected argument 'text' to be the normalized (i.e. 'cooked') version of argument 'rawText'.");
        }
        return text;
    }

    private static TransformFlags GetTransformFlagsOfTemplateLiteralLike(TokenFlags templateFlags)
    {
        var transformFlags = TransformFlags.ContainsES2015;
        if (templateFlags != TokenFlags.None)
        {
            transformFlags |= TransformFlags.ContainsES2018;
        }
        return transformFlags;
    }

    // NOTE: `createTemplateLiteralLikeToken` and `createTemplateLiteralLikeDeclaration` are identical except for
    //       the underlying nodes they create. To avoid polymorphism due to two different node shapes, these
    //       functions are intentionally duplicated.
    // C#: the node classes of TemplateHead/TemplateMiddle/TemplateTail have no common base declaring text, rawText and
    // templateFlags (TS's TemplateLiteralLikeNode), so the fields are set through SetTemplateLiteralLikeFields.
    private Node CreateTemplateLiteralLikeToken(SyntaxKind kind, string text, string? rawText, TokenFlags? templateFlags)
    {
        var node = CreateBaseToken<Node>(kind);
        var nodeTemplateFlags = (templateFlags ?? TokenFlags.None) & TokenFlags.TemplateLiteralLikeFlags;
        SetTemplateLiteralLikeFields(node, text, rawText, nodeTemplateFlags);
        node.TransformFlags = GetTransformFlagsOfTemplateLiteralLike(nodeTemplateFlags);
        return node;
    }

    private NoSubstitutionTemplateLiteral CreateTemplateLiteralLikeDeclaration(SyntaxKind kind, string text, string? rawText, TokenFlags? templateFlags)
    {
        var node = CreateBaseDeclaration<NoSubstitutionTemplateLiteral>(kind);
        node.Text = text;
        node.RawText = rawText;
        node.TemplateFlags = (templateFlags ?? TokenFlags.None) & TokenFlags.TemplateLiteralLikeFlags;
        node.TransformFlags = GetTransformFlagsOfTemplateLiteralLike(node.TemplateFlags);
        return node;
    }

    private static void SetTemplateLiteralLikeFields(Node node, string text, string? rawText, TokenFlags templateFlags)
    {
        switch (node)
        {
            case TemplateHead head:
                head.Text = text;
                head.RawText = rawText;
                head.TemplateFlags = templateFlags;
                break;
            case TemplateMiddle middle:
                middle.Text = text;
                middle.RawText = rawText;
                middle.TemplateFlags = templateFlags;
                break;
            case TemplateTail tail:
                tail.Text = text;
                tail.RawText = rawText;
                tail.TemplateFlags = templateFlags;
                break;
            default:
                Debug.Fail("Unsupported template literal-like kind.");
                break;
        }
    }

    internal Node CreateTemplateLiteralLikeNode(SyntaxKind kind, string text, string? rawText, TokenFlags? templateFlags)
    {
        if (kind == SyntaxKind.NoSubstitutionTemplateLiteral)
        {
            return CreateTemplateLiteralLikeDeclaration(kind, text, rawText, templateFlags);
        }
        return CreateTemplateLiteralLikeToken(kind, text, rawText, templateFlags);
    }

    internal TemplateHead CreateTemplateHead(string? text, string? rawText = null, TokenFlags? templateFlags = null)
    {
        text = CheckTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text, rawText, templateFlags ?? TokenFlags.None);
        return (TemplateHead)CreateTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text, rawText, templateFlags);
    }

    internal TemplateMiddle CreateTemplateMiddle(string? text, string? rawText = null, TokenFlags? templateFlags = null)
    {
        // TS passes SyntaxKind.TemplateHead to the check here (as in the original).
        text = CheckTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text, rawText, templateFlags ?? TokenFlags.None);
        return (TemplateMiddle)CreateTemplateLiteralLikeNode(SyntaxKind.TemplateMiddle, text, rawText, templateFlags);
    }

    internal TemplateTail CreateTemplateTail(string? text, string? rawText = null, TokenFlags? templateFlags = null)
    {
        // TS passes SyntaxKind.TemplateHead to the check here (as in the original).
        text = CheckTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text, rawText, templateFlags ?? TokenFlags.None);
        return (TemplateTail)CreateTemplateLiteralLikeNode(SyntaxKind.TemplateTail, text, rawText, templateFlags);
    }

    internal NoSubstitutionTemplateLiteral CreateNoSubstitutionTemplateLiteral(string? text, string? rawText = null, TokenFlags? templateFlags = null)
    {
        // TS passes SyntaxKind.TemplateHead to the check here (as in the original).
        text = CheckTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text, rawText, templateFlags ?? TokenFlags.None);
        return CreateTemplateLiteralLikeDeclaration(SyntaxKind.NoSubstitutionTemplateLiteral, text, rawText, templateFlags);
    }

    //
    // Yield, spread, class expression and other expressions
    //

    internal YieldExpression CreateYieldExpression(AsteriskToken? asteriskToken, Node? expression)
    {
        Debug.Assert(asteriskToken == null || expression != null, "A `YieldExpression` with an asteriskToken must have an expression.");
        var node = CreateBaseNode<YieldExpression>(SyntaxKind.YieldExpression);
        node.Expression = expression != null ? parenthesizerRules.ParenthesizeExpressionForDisallowedComma(expression) : null;
        node.AsteriskToken = asteriskToken;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.AsteriskToken) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsES2018 |
            TransformFlags.ContainsYield;
        return node;
    }

    internal SpreadElement CreateSpreadElement(Node expression)
    {
        var node = CreateBaseNode<SpreadElement>(SyntaxKind.SpreadElement);
        node.Expression = parenthesizerRules.ParenthesizeExpressionForDisallowedComma(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsRestOrSpread;
        return node;
    }

    internal ClassExpression CreateClassExpression(
        IReadOnlyList<Node>? modifiers,
        Node? name,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node>? heritageClauses,
        IReadOnlyList<Node> members)
    {
        var node = CreateBaseDeclaration<ClassExpression>(SyntaxKind.ClassExpression);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = (Identifier?)AsName(name);
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

        // node.jsDoc: initialized by parser (JsDocContainer)
        return node;
    }

    internal OmittedExpression CreateOmittedExpression()
    {
        return CreateBaseNode<OmittedExpression>(SyntaxKind.OmittedExpression);
    }

    internal ExpressionWithTypeArguments CreateExpressionWithTypeArguments(Node expression, IReadOnlyList<Node>? typeArguments)
    {
        var node = CreateBaseNode<ExpressionWithTypeArguments>(SyntaxKind.ExpressionWithTypeArguments);
        node.Expression = parenthesizerRules.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false);
        node.TypeArguments = typeArguments != null ? parenthesizerRules.ParenthesizeTypeArguments(typeArguments) : null;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildrenFlags(node.TypeArguments) |
            TransformFlags.ContainsES2015;
        return node;
    }

    internal ExpressionWithTypeArguments UpdateExpressionWithTypeArguments(ExpressionWithTypeArguments node, Node expression, IReadOnlyList<Node>? typeArguments)
    {
        return node.Expression != expression
                || !ReferenceEquals(node.TypeArguments, typeArguments)
            ? Update(CreateExpressionWithTypeArguments(expression, typeArguments), node)
            : node;
    }

    internal AsExpression CreateAsExpression(Node expression, Node type)
    {
        var node = CreateBaseNode<AsExpression>(SyntaxKind.AsExpression);
        node.Expression = expression;
        node.Type = type;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.Type) |
            TransformFlags.ContainsTypeScript;
        return node;
    }

    internal AsExpression UpdateAsExpression(AsExpression node, Node expression, Node type)
    {
        return node.Expression != expression
                || node.Type != type
            ? Update(CreateAsExpression(expression, type), node)
            : node;
    }

    internal NonNullExpression CreateNonNullExpression(Node expression)
    {
        var node = CreateBaseNode<NonNullExpression>(SyntaxKind.NonNullExpression);
        node.Expression = parenthesizerRules.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsTypeScript;
        return node;
    }

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

    internal SatisfiesExpression CreateSatisfiesExpression(Node expression, Node type)
    {
        var node = CreateBaseNode<SatisfiesExpression>(SyntaxKind.SatisfiesExpression);
        node.Expression = expression;
        node.Type = type;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.Type) |
            TransformFlags.ContainsTypeScript;
        return node;
    }

    internal SatisfiesExpression UpdateSatisfiesExpression(SatisfiesExpression node, Node expression, Node type)
    {
        return node.Expression != expression
                || node.Type != type
            ? Update(CreateSatisfiesExpression(expression, type), node)
            : node;
    }

    internal NonNullExpression CreateNonNullChain(Node expression)
    {
        var node = CreateBaseNode<NonNullExpression>(SyntaxKind.NonNullExpression);
        node.Flags |= NodeFlags.OptionalChain;
        node.Expression = parenthesizerRules.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ true);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsTypeScript;
        return node;
    }

    internal NonNullExpression UpdateNonNullChain(NonNullExpression node, Node expression)
    {
        Debug.Assert((node.Flags & NodeFlags.OptionalChain) != 0, "Cannot update a NonNullExpression using updateNonNullChain. Use updateNonNullExpression instead.");
        return node.Expression != expression
            ? Update(CreateNonNullChain(expression), node)
            : node;
    }

    internal MetaProperty CreateMetaProperty(SyntaxKind keywordToken, Identifier name)
    {
        var node = CreateBaseNode<MetaProperty>(SyntaxKind.MetaProperty);
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
                break;
        }

        // node.flowNode: initialized by binder (FlowContainer)
        return node;
    }

    //
    // Misc
    //

    internal TemplateSpan CreateTemplateSpan(Node expression, Node literal)
    {
        var node = CreateBaseNode<TemplateSpan>(SyntaxKind.TemplateSpan);
        node.Expression = expression;
        node.Literal = literal;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.Literal) |
            TransformFlags.ContainsES2015;
        return node;
    }

    internal SemicolonClassElement CreateSemicolonClassElement()
    {
        var node = CreateBaseNode<SemicolonClassElement>(SyntaxKind.SemicolonClassElement);
        node.TransformFlags |= TransformFlags.ContainsES2015;
        return node;
    }
}
