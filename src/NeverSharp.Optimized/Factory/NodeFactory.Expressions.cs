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

/// <summary>nodeFactory.ts: expressions.</summary>
internal sealed partial class NodeFactory
{
    //
    // Expression
    //

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateArrayLiteralExpression(NodeList elements = default, bool? multiLine = null)
    {
        // (Track O, step O12, here and in the functions below: the fields and the transform flags are computed from the
        // arguments first, then the record is written in one go by the generated NodeStore.New{Class}; the flags of
        // the new node, which TypeScript's code ORs in, are none at that point. No other node is created in between.)
        // Ensure we add a trailing comma for something like `[NumericLiteral(1), NumericLiteral(2), OmittedExpresion]` so that
        // we end up with `[1, 2, ,]` instead of `[1, 2, ]` otherwise the `OmittedExpression` will just end up being treated like
        // a trailing comma.
        var lastElement = !elements.IsNone ? Core.LastOrUndefined(store.Nodes(elements)) : default;
        var elementsArray = CreateNodeArray(store, elements, !lastElement.IsNone && FactoryNodeTests.IsOmittedExpression(store, lastElement) ? true : null);
        var parenthesized = Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(elementsArray);
        return store.NewArrayLiteralExpression(
            PropagateChildrenFlags(parenthesized),
            elements: parenthesized,
            multiLine: multiLine == true);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateObjectLiteralExpression(NodeList properties = default, bool? multiLine = null)
    {
        var propertiesArray = CreateNodeArray(store, properties);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return store.NewObjectLiteralExpression(
            PropagateChildrenFlags(propertiesArray),
            properties: propertiesArray,
            multiLine: multiLine == true);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateBasePropertyAccessExpression(Node expression, Node questionDotToken, Node name)
    {
        var transformFlags = PropagateChildFlags(store, expression) |
            PropagateChildFlags(store, questionDotToken) |
            (FactoryNodeTests.IsIdentifier(store, name) ?
                PropagateIdentifierNameFlags(store, name) :
                PropagateChildFlags(store, name) | TransformFlags.ContainsPrivateIdentifierInExpression);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return store.NewPropertyAccessExpression(transformFlags, expression: expression, questionDotToken: questionDotToken, name: name);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreatePropertyAccessExpression(Node expression, Node name)
    {
        var node = CreateBasePropertyAccessExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false),
            /*questionDotToken*/ default,
            AsName(name));
        if (FactoryNodeTests.IsSuperKeyword(store, expression))
        {
            // super method calls require a lexical 'this'
            // super method calls require 'super' hoisting in ES2017 and ES2018 async functions and async generators
            store.SetTransformFlags(node, store.TransformFlags(node) | (TransformFlags.ContainsES2017 |
                TransformFlags.ContainsES2018));
        }
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreatePropertyAccessChain(Node expression, Node questionDotToken, Node name)
    {
        var node = CreateBasePropertyAccessExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ true),
            questionDotToken,
            AsName(name));
        store.SetFlags(node, store.Flags(node) | NodeFlags.OptionalChain);
        store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2020);
        return node;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateBaseElementAccessExpression(Node expression, Node questionDotToken, Node argumentExpression)
    {
        var transformFlags = PropagateChildFlags(store, expression) |
            PropagateChildFlags(store, questionDotToken) |
            PropagateChildFlags(store, argumentExpression);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return store.NewElementAccessExpression(transformFlags, expression: expression, questionDotToken: questionDotToken, argumentExpression: argumentExpression);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateElementAccessExpression(Node expression, Node index)
    {
        var node = CreateBaseElementAccessExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false),
            /*questionDotToken*/ default,
            AsExpression(index));
        if (FactoryNodeTests.IsSuperKeyword(store, expression))
        {
            // super method calls require a lexical 'this'
            // super method calls require 'super' hoisting in ES2017 and ES2018 async functions and async generators
            store.SetTransformFlags(node, store.TransformFlags(node) | (TransformFlags.ContainsES2017 |
                TransformFlags.ContainsES2018));
        }
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateElementAccessChain(Node expression, Node questionDotToken, Node index)
    {
        var node = CreateBaseElementAccessExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ true),
            questionDotToken,
            AsExpression(index));
        store.SetFlags(node, store.Flags(node) | NodeFlags.OptionalChain);
        store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2020);
        return node;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateBaseCallExpression(Node expression, Node questionDotToken, NodeList typeArguments, NodeList argumentsArray)
    {
        var transformFlags = PropagateChildFlags(store, expression) |
            PropagateChildFlags(store, questionDotToken) |
            PropagateChildrenFlags(typeArguments) |
            PropagateChildrenFlags(argumentsArray);
        if (!typeArguments.IsNone)
        {
            transformFlags |= TransformFlags.ContainsTypeScript;
        }
        if (Utilities.IsSuperProperty(store, expression))
        {
            transformFlags |= TransformFlags.ContainsLexicalThis;
        }
        return store.NewCallExpression(transformFlags, expression: expression, questionDotToken: questionDotToken, typeArguments: typeArguments, arguments: argumentsArray);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateCallExpression(Node expression, NodeList typeArguments, NodeList argumentsArray)
    {
        var callee = Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false);
        var node = CreateBaseCallExpression(
            callee,
            /*questionDotToken*/ default,
            AsNodeArray(store, typeArguments),
            Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(CreateNodeArray(store, argumentsArray)));
        // (node.expression is callee.)
        if (FactoryNodeTests.IsImportKeyword(store, callee))
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsDynamicImport);
        }
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node UpdateCallExpression(Node node, Node expression, NodeList typeArguments, NodeList argumentsArray)
    {
        if (UtilitiesPublic.IsCallChain(store, node))
        {
            return UpdateCallChain(node, expression, store.QuestionDotToken(node), typeArguments, argumentsArray);
        }
        return store.Expression(node) != expression
                || store.TypeArguments(node) != typeArguments
                || store.Arguments(node) != argumentsArray
            ? Update(CreateCallExpression(expression, typeArguments, argumentsArray), node)
            : node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateCallChain(Node expression, Node questionDotToken, NodeList typeArguments, NodeList argumentsArray)
    {
        var node = CreateBaseCallExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ true),
            questionDotToken,
            AsNodeArray(store, typeArguments),
            Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(CreateNodeArray(store, argumentsArray)));
        store.SetFlags(node, store.Flags(node) | NodeFlags.OptionalChain);
        store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2020);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node UpdateCallChain(Node node, Node expression, Node questionDotToken, NodeList typeArguments, NodeList argumentsArray)
    {
        Debug.Assert((store.Flags(node) & NodeFlags.OptionalChain) != 0, "Cannot update a CallExpression using updateCallChain. Use updateCall instead.");
        return store.Expression(node) != expression
                || store.QuestionDotToken(node) != questionDotToken
                || store.TypeArguments(node) != typeArguments
                || store.Arguments(node) != argumentsArray
            ? Update(CreateCallChain(expression, questionDotToken, typeArguments, argumentsArray), node)
            : node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateNewExpression(Node expression, NodeList typeArguments, NodeList argumentsArray)
    {
        var callee = Parenthesizer.ParenthesizeExpressionOfNew(expression);
        var typeArgumentsArray = AsNodeArray(store, typeArguments);
        var args = !argumentsArray.IsNone ? Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(argumentsArray) : default;
        var transformFlags = PropagateChildFlags(store, callee) |
            PropagateChildrenFlags(typeArgumentsArray) |
            PropagateChildrenFlags(args) |
            TransformFlags.ContainsES2020;
        if (!typeArgumentsArray.IsNone)
        {
            transformFlags |= TransformFlags.ContainsTypeScript;
        }
        return store.NewNewExpression(transformFlags, expression: callee, typeArguments: typeArgumentsArray, arguments: args);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTaggedTemplateExpression(Node tag, NodeList typeArguments, Node template)
    {
        var node = CreateBaseNode(SyntaxKind.TaggedTemplateExpression);
        store.SetTag(node, Parenthesizer.ParenthesizeLeftSideOfAccess(tag, /*optionalChain*/ false));
        store.SetTypeArguments(node, AsNodeArray(store, typeArguments));
        store.SetTemplate(node, template);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Tag(node)) |
            PropagateChildrenFlags(store.TypeArguments(node)) |
            PropagateChildFlags(store, store.Template(node)) |
            TransformFlags.ContainsES2015));
        if (!store.TypeArguments(node).IsNone)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsTypeScript);
        }
        if (Utilities.HasInvalidEscape(store, store.Template(node)))
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2018);
        }
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTypeAssertion(Node type, Node expression)
    {
        var node = CreateBaseNode(SyntaxKind.TypeAssertionExpression);
        store.SetExpression(node, Parenthesizer.ParenthesizeOperandOfPrefixUnary(expression));
        store.SetType(node, type);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.Type(node)) |
            TransformFlags.ContainsTypeScript));
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node UpdateTypeAssertion(Node node, Node type, Node expression)
    {
        return store.Type(node) != type
                || store.Expression(node) != expression
            ? Update(CreateTypeAssertion(type, expression), node)
            : node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateParenthesizedExpression(Node expression)
    {
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return store.NewParenthesizedExpression(PropagateChildFlags(store, expression), expression: expression);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node UpdateParenthesizedExpression(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateParenthesizedExpression(expression), node)
            : node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateFunctionExpression(
        NodeList modifiers,
        Node asteriskToken,
        Node name,
        NodeList typeParameters,
        NodeList parameters,
        Node type,
        Node body)
    {
        var modifiersArray = AsNodeArray(store, modifiers);
        var nameNode = AsName(name);
        var typeParametersArray = AsNodeArray(store, typeParameters);
        var parametersArray = CreateNodeArray(store, parameters);
        var isAsync = (Utilities.ModifiersToFlags(store, modifiersArray) & ModifierFlags.Async) != 0;
        var isGenerator = !asteriskToken.IsNone;
        var isAsyncGenerator = isAsync && isGenerator;
        var transformFlags = PropagateChildrenFlags(modifiersArray) |
            PropagateChildFlags(store, asteriskToken) |
            PropagateNameFlags(store, nameNode) |
            PropagateChildrenFlags(typeParametersArray) |
            PropagateChildrenFlags(parametersArray) |
            PropagateChildFlags(store, type) |
            (PropagateChildFlags(store, body) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
            (isAsyncGenerator ? TransformFlags.ContainsES2018 :
                isAsync ? TransformFlags.ContainsES2017 :
                isGenerator ? TransformFlags.ContainsGenerator :
                TransformFlags.None) |
            (!typeParametersArray.IsNone || !type.IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
            TransformFlags.ContainsHoistedDeclarationOrCompletion;
        var node = store.NewFunctionExpression(
            transformFlags,
            modifiers: modifiersArray,
            asteriskToken: asteriskToken,
            name: nameNode,
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
    internal Node CreateArrowFunction(
        NodeList modifiers,
        NodeList typeParameters,
        NodeList parameters,
        Node type,
        Node equalsGreaterThanToken,
        Node body)
    {
        var modifiersArray = AsNodeArray(store, modifiers);
        var typeParametersArray = AsNodeArray(store, typeParameters);
        var parametersArray = CreateNodeArray(store, parameters);
        var conciseBody = Parenthesizer.ParenthesizeConciseBodyOfArrowFunction(body);
        // (The transform flags are written after the record: without the token, the node is created before it.)
        var node = store.NewArrowFunction(
            TransformFlags.None,
            modifiers: modifiersArray,
            typeParameters: typeParametersArray,
            parameters: parametersArray,
            type: type,
            equalsGreaterThanToken: equalsGreaterThanToken,
            body: conciseBody);
        if (equalsGreaterThanToken.IsNone)
        {
            equalsGreaterThanToken = CreateToken(SyntaxKind.EqualsGreaterThanToken);
            store.SetEqualsGreaterThanToken(node, equalsGreaterThanToken);
        }
        var isAsync = (Utilities.ModifiersToFlags(store, modifiersArray) & ModifierFlags.Async) != 0;
        store.SetTransformFlags(node, PropagateChildrenFlags(modifiersArray) |
            PropagateChildrenFlags(typeParametersArray) |
            PropagateChildrenFlags(parametersArray) |
            PropagateChildFlags(store, type) |
            PropagateChildFlags(store, equalsGreaterThanToken) |
            (PropagateChildFlags(store, conciseBody) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
            (!typeParametersArray.IsNone || !type.IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
            (isAsync ? TransformFlags.ContainsES2017 | TransformFlags.ContainsLexicalThis : TransformFlags.None) |
            TransformFlags.ContainsES2015);
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
    internal Node CreateDeleteExpression(Node expression)
    {
        var node = CreateBaseNode(SyntaxKind.DeleteExpression);
        store.SetExpression(node, Parenthesizer.ParenthesizeOperandOfPrefixUnary(expression));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildFlags(store, store.Expression(node)));
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTypeOfExpression(Node expression)
    {
        var operand = Parenthesizer.ParenthesizeOperandOfPrefixUnary(expression);
        return store.NewTypeOfExpression(PropagateChildFlags(store, operand), expression: operand);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateVoidExpression(Node expression)
    {
        var node = CreateBaseNode(SyntaxKind.VoidExpression);
        store.SetExpression(node, Parenthesizer.ParenthesizeOperandOfPrefixUnary(expression));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildFlags(store, store.Expression(node)));
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateAwaitExpression(Node expression)
    {
        var operand = Parenthesizer.ParenthesizeOperandOfPrefixUnary(expression);
        return store.NewAwaitExpression(
            PropagateChildFlags(store, operand) |
                TransformFlags.ContainsES2017 |
                TransformFlags.ContainsES2018 |
                TransformFlags.ContainsAwait,
            expression: operand);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreatePrefixUnaryExpression(SyntaxKind @operator, Node operand)
    {
        var operandNode = Parenthesizer.ParenthesizeOperandOfPrefixUnary(operand);
        var transformFlags = PropagateChildFlags(store, operandNode);
        // Only set this flag for non-generated identifiers and non-"local" names. See the
        // comment in `visitPreOrPostfixUnaryExpression` in module.ts
        if (
            (@operator == SyntaxKind.PlusPlusToken || @operator == SyntaxKind.MinusMinusToken) &&
            FactoryNodeTests.IsIdentifier(store, operandNode) &&
            !UtilitiesPublic.IsGeneratedIdentifier(store, operandNode) &&
            !FactoryUtilities.IsLocalName(store, operandNode))
        {
            transformFlags |= TransformFlags.ContainsUpdateExpressionForIdentifier;
        }
        return store.NewPrefixUnaryExpression(transformFlags, @operator: @operator, operand: operandNode);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreatePostfixUnaryExpression(Node operand, SyntaxKind @operator)
    {
        var operandNode = Parenthesizer.ParenthesizeOperandOfPostfixUnary(operand);
        var transformFlags = PropagateChildFlags(store, operandNode);
        // Only set this flag for non-generated identifiers and non-"local" names. See the
        // comment in `visitPreOrPostfixUnaryExpression` in module.ts
        if (
            FactoryNodeTests.IsIdentifier(store, operandNode) &&
            !UtilitiesPublic.IsGeneratedIdentifier(store, operandNode) &&
            !FactoryUtilities.IsLocalName(store, operandNode))
        {
            transformFlags |= TransformFlags.ContainsUpdateExpressionForIdentifier;
        }
        return store.NewPostfixUnaryExpression(transformFlags, @operator: @operator, operand: operandNode);
    }

    /// <summary>`createBinaryExpression(left, operator: BinaryOperator | BinaryOperatorToken, right)`.</summary>
    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateBinaryExpression(Node left, Node @operator, Node right)
    {
        var operatorToken = AsToken(@operator);
        var operatorKind = store.Kind(operatorToken);
        var leftNode = Parenthesizer.ParenthesizeLeftSideOfBinary(operatorKind, left);
        var rightNode = Parenthesizer.ParenthesizeRightSideOfBinary(operatorKind, leftNode, right);
        var transformFlags = PropagateChildFlags(store, leftNode) |
            PropagateChildFlags(store, operatorToken) |
            PropagateChildFlags(store, rightNode);
        if (operatorKind == SyntaxKind.QuestionQuestionToken)
        {
            transformFlags |= TransformFlags.ContainsES2020;
        }
        else if (operatorKind == SyntaxKind.EqualsToken)
        {
            if (FactoryNodeTests.IsObjectLiteralExpression(store, leftNode))
            {
                transformFlags |= TransformFlags.ContainsES2015 |
                    TransformFlags.ContainsES2018 |
                    TransformFlags.ContainsDestructuringAssignment |
                    PropagateAssignmentPatternFlags(store, leftNode);
            }
            else if (FactoryNodeTests.IsArrayLiteralExpression(store, leftNode))
            {
                transformFlags |= TransformFlags.ContainsES2015 |
                    TransformFlags.ContainsDestructuringAssignment |
                    PropagateAssignmentPatternFlags(store, leftNode);
            }
        }
        else if (operatorKind == SyntaxKind.AsteriskAsteriskToken || operatorKind == SyntaxKind.AsteriskAsteriskEqualsToken)
        {
            transformFlags |= TransformFlags.ContainsES2016;
        }
        else if (Utilities.IsLogicalOrCoalescingAssignmentOperator(operatorKind))
        {
            transformFlags |= TransformFlags.ContainsES2021;
        }
        if (operatorKind == SyntaxKind.InKeyword && FactoryNodeTests.IsPrivateIdentifier(store, leftNode))
        {
            transformFlags |= TransformFlags.ContainsPrivateIdentifierInExpression;
        }
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return store.NewBinaryExpression(transformFlags, left: leftNode, operatorToken: operatorToken, right: rightNode);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static TransformFlags PropagateAssignmentPatternFlags(NodeStore store, Node node)
    {
        return FactoryUtilities.ContainsObjectRestOrSpread(store, node) ? TransformFlags.ContainsObjectRestOrSpread : TransformFlags.None;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateConditionalExpression(Node condition, Node questionToken, Node whenTrue, Node colonToken, Node whenFalse)
    {
        var conditionNode = Parenthesizer.ParenthesizeConditionOfConditionalExpression(condition);
        var whenTrueNode = Parenthesizer.ParenthesizeBranchOfConditionalExpression(whenTrue);
        var whenFalseNode = Parenthesizer.ParenthesizeBranchOfConditionalExpression(whenFalse);
        // (The transform flags are written after the record: missing tokens are created after the node.)
        var node = store.NewConditionalExpression(
            TransformFlags.None,
            condition: conditionNode,
            questionToken: questionToken,
            whenTrue: whenTrueNode,
            colonToken: colonToken,
            whenFalse: whenFalseNode);
        if (questionToken.IsNone)
        {
            questionToken = CreateToken(SyntaxKind.QuestionToken);
            store.SetQuestionToken(node, questionToken);
        }
        if (colonToken.IsNone)
        {
            colonToken = CreateToken(SyntaxKind.ColonToken);
            store.SetColonToken(node, colonToken);
        }
        store.SetTransformFlags(node, PropagateChildFlags(store, conditionNode) |
            PropagateChildFlags(store, questionToken) |
            PropagateChildFlags(store, whenTrueNode) |
            PropagateChildFlags(store, colonToken) |
            PropagateChildFlags(store, whenFalseNode));
        // node.flowNodeWhenFalse = undefined;
        // node.flowNodeWhenTrue = undefined;
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTemplateExpression(Node head, NodeList templateSpans)
    {
        var spans = CreateNodeArray(store, templateSpans);
        return store.NewTemplateExpression(
            PropagateChildFlags(store, head) |
                PropagateChildrenFlags(spans) |
                TransformFlags.ContainsES2015,
            head: head,
            templateSpans: spans);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateYieldExpression(Node asteriskToken, Node expression)
    {
        Debug.Assert(asteriskToken.IsNone || !expression.IsNone, "A `YieldExpression` with an asteriskToken must have an expression.");
        var node = CreateBaseNode(SyntaxKind.YieldExpression);
        store.SetExpression(node, !expression.IsNone ? Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression) : default);
        store.SetAsteriskToken(node, asteriskToken);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.AsteriskToken(node)) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsES2018 |
            TransformFlags.ContainsYield));
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateSpreadElement(Node expression)
    {
        var operand = Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression);
        return store.NewSpreadElement(
            PropagateChildFlags(store, operand) |
                TransformFlags.ContainsES2015 |
                TransformFlags.ContainsRestOrSpread,
            expression: operand);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateClassExpression(
        NodeList modifiers,
        Node name,
        NodeList typeParameters,
        NodeList heritageClauses,
        NodeList members)
    {
        var node = CreateBaseDeclaration(SyntaxKind.ClassExpression);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetName(node, AsName(name));
        store.SetTypeParameters(node, AsNodeArray(store, typeParameters));
        store.SetHeritageClauses(node, AsNodeArray(store, heritageClauses));
        store.SetMembers(node, CreateNodeArray(store, members));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildrenFlags(store.Modifiers(node)) |
            PropagateNameFlags(store, store.Name(node)) |
            PropagateChildrenFlags(store.TypeParameters(node)) |
            PropagateChildrenFlags(store.HeritageClauses(node)) |
            PropagateChildrenFlags(store.Members(node)) |
            (!store.TypeParameters(node).IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
            TransformFlags.ContainsES2015));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateOmittedExpression()
    {
        return CreateBaseNode(SyntaxKind.OmittedExpression);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateExpressionWithTypeArguments(Node expression, NodeList typeArguments)
    {
        var operand = Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false);
        var typeArgumentsArray = !typeArguments.IsNone ? Parenthesizer.ParenthesizeTypeArguments(typeArguments) : default;
        return store.NewExpressionWithTypeArguments(
            PropagateChildFlags(store, operand) |
                PropagateChildrenFlags(typeArgumentsArray) |
                TransformFlags.ContainsES2015,
            expression: operand,
            typeArguments: typeArgumentsArray);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node UpdateExpressionWithTypeArguments(Node node, Node expression, NodeList typeArguments)
    {
        return store.Expression(node) != expression
                || store.TypeArguments(node) != typeArguments
            ? Update(CreateExpressionWithTypeArguments(expression, typeArguments), node)
            : node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateAsExpression(Node expression, Node type)
    {
        return store.NewAsExpression(
            PropagateChildFlags(store, expression) |
                PropagateChildFlags(store, type) |
                TransformFlags.ContainsTypeScript,
            expression: expression,
            type: type);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node UpdateAsExpression(Node node, Node expression, Node type)
    {
        return store.Expression(node) != expression
                || store.Type(node) != type
            ? Update(CreateAsExpression(expression, type), node)
            : node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateNonNullExpression(Node expression)
    {
        var operand = Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false);
        return store.NewNonNullExpression(
            PropagateChildFlags(store, operand) |
                TransformFlags.ContainsTypeScript,
            expression: operand);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node UpdateNonNullExpression(Node node, Node expression)
    {
        if (UtilitiesPublic.IsNonNullChain(store, node))
        {
            return UpdateNonNullChain(node, expression);
        }
        return store.Expression(node) != expression
            ? Update(CreateNonNullExpression(expression), node)
            : node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateSatisfiesExpression(Node expression, Node type)
    {
        var node = CreateBaseNode(SyntaxKind.SatisfiesExpression);
        store.SetExpression(node, expression);
        store.SetType(node, type);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.Type(node)) |
            TransformFlags.ContainsTypeScript));
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node UpdateSatisfiesExpression(Node node, Node expression, Node type)
    {
        return store.Expression(node) != expression
                || store.Type(node) != type
            ? Update(CreateSatisfiesExpression(expression, type), node)
            : node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateNonNullChain(Node expression)
    {
        var node = CreateBaseNode(SyntaxKind.NonNullExpression);
        store.SetFlags(node, store.Flags(node) | NodeFlags.OptionalChain);
        store.SetExpression(node, Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ true));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            TransformFlags.ContainsTypeScript));
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node UpdateNonNullChain(Node node, Node expression)
    {
        Debug.Assert((store.Flags(node) & NodeFlags.OptionalChain) != 0, "Cannot update a NonNullExpression using updateNonNullChain. Use updateNonNullExpression instead.");
        return store.Expression(node) != expression
            ? Update(CreateNonNullChain(expression), node)
            : node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateMetaProperty(SyntaxKind keywordToken, Node name)
    {
        var node = CreateBaseNode(SyntaxKind.MetaProperty);
        store.SetKeywordToken(node, keywordToken);
        store.SetName(node, name);
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildFlags(store, store.Name(node)));
        switch (keywordToken)
        {
            case SyntaxKind.NewKeyword:
                store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2015);
                break;
            case SyntaxKind.ImportKeyword:
                store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2020);
                break;
            default:
                Debug.AssertNever(keywordToken);
                break;
        }
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    //
    // Misc
    //

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTemplateSpan(Node expression, Node literal)
    {
        return store.NewTemplateSpan(
            PropagateChildFlags(store, expression) |
                PropagateChildFlags(store, literal) |
                TransformFlags.ContainsES2015,
            expression: expression,
            literal: literal);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateSemicolonClassElement()
    {
        var node = CreateBaseNode(SyntaxKind.SemicolonClassElement);
        store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2015);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreatePartiallyEmittedExpression(Node expression, Node original = default)
    {
        var node = CreateBaseNode(SyntaxKind.PartiallyEmittedExpression);
        store.SetExpression(node, expression);
        store.SetOriginal(node, original);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            TransformFlags.ContainsTypeScript));
        FactoryUtilitiesPublic.SetTextRange(store, node, original);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node UpdatePartiallyEmittedExpression(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreatePartiallyEmittedExpression(expression, store.Original(node)), node)
            : node;
    }
}
