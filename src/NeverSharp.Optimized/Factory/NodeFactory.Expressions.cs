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
        var node = CreateBaseNode(SyntaxKind.ArrayLiteralExpression);
        // Ensure we add a trailing comma for something like `[NumericLiteral(1), NumericLiteral(2), OmittedExpresion]` so that
        // we end up with `[1, 2, ,]` instead of `[1, 2, ]` otherwise the `OmittedExpression` will just end up being treated like
        // a trailing comma.
        var lastElement = !elements.IsNone ? Core.LastOrUndefined(store.Nodes(elements)) : default;
        var elementsArray = CreateNodeArray(store, elements, !lastElement.IsNone && FactoryNodeTests.IsOmittedExpression(store, lastElement) ? true : null);
        store.SetElements(node, Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(elementsArray));
        store.SetMultiLine(node, multiLine == true);
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildrenFlags(store.Elements(node)));
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateObjectLiteralExpression(NodeList properties = default, bool? multiLine = null)
    {
        var node = CreateBaseDeclaration(SyntaxKind.ObjectLiteralExpression);
        store.SetProperties(node, CreateNodeArray(store, properties));
        store.SetMultiLine(node, multiLine == true);
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildrenFlags(store.Properties(node)));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateBasePropertyAccessExpression(Node expression, Node questionDotToken, Node name)
    {
        var node = CreateBaseDeclaration(SyntaxKind.PropertyAccessExpression);
        store.SetExpression(node, expression);
        store.SetQuestionDotToken(node, questionDotToken);
        store.SetName(node, name);
        store.SetTransformFlags(node, PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.QuestionDotToken(node)) |
            (FactoryNodeTests.IsIdentifier(store, store.Name(node)) ?
                PropagateIdentifierNameFlags(store, store.Name(node)) :
                PropagateChildFlags(store, store.Name(node)) | TransformFlags.ContainsPrivateIdentifierInExpression));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
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
        var node = CreateBaseDeclaration(SyntaxKind.ElementAccessExpression);
        store.SetExpression(node, expression);
        store.SetQuestionDotToken(node, questionDotToken);
        store.SetArgumentExpression(node, argumentExpression);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.QuestionDotToken(node)) |
            PropagateChildFlags(store, store.ArgumentExpression(node))));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
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
        var node = CreateBaseDeclaration(SyntaxKind.CallExpression);
        store.SetExpression(node, expression);
        store.SetQuestionDotToken(node, questionDotToken);
        store.SetTypeArguments(node, typeArguments);
        store.SetArguments(node, argumentsArray);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.QuestionDotToken(node)) |
            PropagateChildrenFlags(store.TypeArguments(node)) |
            PropagateChildrenFlags(store.Arguments(node))));
        if (!store.TypeArguments(node).IsNone)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsTypeScript);
        }
        if (Utilities.IsSuperProperty(store, store.Expression(node)))
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsLexicalThis);
        }
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateCallExpression(Node expression, NodeList typeArguments, NodeList argumentsArray)
    {
        var node = CreateBaseCallExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false),
            /*questionDotToken*/ default,
            AsNodeArray(store, typeArguments),
            Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(CreateNodeArray(store, argumentsArray)));
        if (FactoryNodeTests.IsImportKeyword(store, store.Expression(node)))
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
        var node = CreateBaseDeclaration(SyntaxKind.NewExpression);
        store.SetExpression(node, Parenthesizer.ParenthesizeExpressionOfNew(expression));
        store.SetTypeArguments(node, AsNodeArray(store, typeArguments));
        store.SetArguments(node, !argumentsArray.IsNone ? Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(argumentsArray) : default);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildrenFlags(store.TypeArguments(node)) |
            PropagateChildrenFlags(store.Arguments(node)) |
            TransformFlags.ContainsES2020));
        if (!store.TypeArguments(node).IsNone)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsTypeScript);
        }
        return node;
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
        var node = CreateBaseNode(SyntaxKind.ParenthesizedExpression);
        store.SetExpression(node, expression);
        store.SetTransformFlags(node, PropagateChildFlags(store, store.Expression(node)));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
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
        var node = CreateBaseDeclaration(SyntaxKind.FunctionExpression);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetAsteriskToken(node, asteriskToken);
        store.SetName(node, AsName(name));
        store.SetTypeParameters(node, AsNodeArray(store, typeParameters));
        store.SetParameters(node, CreateNodeArray(store, parameters));
        store.SetType(node, type);
        store.SetBody(node, body);
        var isAsync = (Utilities.ModifiersToFlags(store, store.Modifiers(node)) & ModifierFlags.Async) != 0;
        var isGenerator = !store.AsteriskToken(node).IsNone;
        var isAsyncGenerator = isAsync && isGenerator;
        store.SetTransformFlags(node, PropagateChildrenFlags(store.Modifiers(node)) |
            PropagateChildFlags(store, store.AsteriskToken(node)) |
            PropagateNameFlags(store, store.Name(node)) |
            PropagateChildrenFlags(store.TypeParameters(node)) |
            PropagateChildrenFlags(store.Parameters(node)) |
            PropagateChildFlags(store, store.Type(node)) |
            (PropagateChildFlags(store, store.Body(node)) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
            (isAsyncGenerator ? TransformFlags.ContainsES2018 :
                isAsync ? TransformFlags.ContainsES2017 :
                isGenerator ? TransformFlags.ContainsGenerator :
                TransformFlags.None) |
            (!store.TypeParameters(node).IsNone || !store.Type(node).IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
            TransformFlags.ContainsHoistedDeclarationOrCompletion);
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
        var node = CreateBaseDeclaration(SyntaxKind.ArrowFunction);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetTypeParameters(node, AsNodeArray(store, typeParameters));
        store.SetParameters(node, CreateNodeArray(store, parameters));
        store.SetType(node, type);
        store.SetEqualsGreaterThanToken(node, equalsGreaterThanToken.IsNone ? CreateToken(SyntaxKind.EqualsGreaterThanToken) : equalsGreaterThanToken);
        store.SetBody(node, Parenthesizer.ParenthesizeConciseBodyOfArrowFunction(body));
        var isAsync = (Utilities.ModifiersToFlags(store, store.Modifiers(node)) & ModifierFlags.Async) != 0;
        store.SetTransformFlags(node, PropagateChildrenFlags(store.Modifiers(node)) |
            PropagateChildrenFlags(store.TypeParameters(node)) |
            PropagateChildrenFlags(store.Parameters(node)) |
            PropagateChildFlags(store, store.Type(node)) |
            PropagateChildFlags(store, store.EqualsGreaterThanToken(node)) |
            (PropagateChildFlags(store, store.Body(node)) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
            (!store.TypeParameters(node).IsNone || !store.Type(node).IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
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
        var node = CreateBaseNode(SyntaxKind.TypeOfExpression);
        store.SetExpression(node, Parenthesizer.ParenthesizeOperandOfPrefixUnary(expression));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildFlags(store, store.Expression(node)));
        return node;
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
        var node = CreateBaseNode(SyntaxKind.AwaitExpression);
        store.SetExpression(node, Parenthesizer.ParenthesizeOperandOfPrefixUnary(expression));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            TransformFlags.ContainsES2017 |
            TransformFlags.ContainsES2018 |
            TransformFlags.ContainsAwait));
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreatePrefixUnaryExpression(SyntaxKind @operator, Node operand)
    {
        var node = CreateBaseNode(SyntaxKind.PrefixUnaryExpression);
        store.SetOperator(node, @operator);
        store.SetOperand(node, Parenthesizer.ParenthesizeOperandOfPrefixUnary(operand));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildFlags(store, store.Operand(node)));
        // Only set this flag for non-generated identifiers and non-"local" names. See the
        // comment in `visitPreOrPostfixUnaryExpression` in module.ts
        if (
            (@operator == SyntaxKind.PlusPlusToken || @operator == SyntaxKind.MinusMinusToken) &&
            FactoryNodeTests.IsIdentifier(store, store.Operand(node)) &&
            !UtilitiesPublic.IsGeneratedIdentifier(store, store.Operand(node)) &&
            !FactoryUtilities.IsLocalName(store, store.Operand(node)))
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsUpdateExpressionForIdentifier);
        }
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreatePostfixUnaryExpression(Node operand, SyntaxKind @operator)
    {
        var node = CreateBaseNode(SyntaxKind.PostfixUnaryExpression);
        store.SetOperator(node, @operator);
        store.SetOperand(node, Parenthesizer.ParenthesizeOperandOfPostfixUnary(operand));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildFlags(store, store.Operand(node)));
        // Only set this flag for non-generated identifiers and non-"local" names. See the
        // comment in `visitPreOrPostfixUnaryExpression` in module.ts
        if (
            FactoryNodeTests.IsIdentifier(store, store.Operand(node)) &&
            !UtilitiesPublic.IsGeneratedIdentifier(store, store.Operand(node)) &&
            !FactoryUtilities.IsLocalName(store, store.Operand(node)))
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsUpdateExpressionForIdentifier);
        }
        return node;
    }

    /// <summary>`createBinaryExpression(left, operator: BinaryOperator | BinaryOperatorToken, right)`.</summary>
    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateBinaryExpression(Node left, Node @operator, Node right)
    {
        var node = CreateBaseDeclaration(SyntaxKind.BinaryExpression);
        var operatorToken = AsToken(@operator);
        var operatorKind = store.Kind(operatorToken);
        store.SetLeft(node, Parenthesizer.ParenthesizeLeftSideOfBinary(operatorKind, left));
        store.SetOperatorToken(node, operatorToken);
        store.SetRight(node, Parenthesizer.ParenthesizeRightSideOfBinary(operatorKind, store.Left(node), right));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Left(node)) |
            PropagateChildFlags(store, store.OperatorToken(node)) |
            PropagateChildFlags(store, store.Right(node))));
        if (operatorKind == SyntaxKind.QuestionQuestionToken)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2020);
        }
        else if (operatorKind == SyntaxKind.EqualsToken)
        {
            if (FactoryNodeTests.IsObjectLiteralExpression(store, store.Left(node)))
            {
                store.SetTransformFlags(node, store.TransformFlags(node) | (TransformFlags.ContainsES2015 |
                    TransformFlags.ContainsES2018 |
                    TransformFlags.ContainsDestructuringAssignment |
                    PropagateAssignmentPatternFlags(store, store.Left(node))));
            }
            else if (FactoryNodeTests.IsArrayLiteralExpression(store, store.Left(node)))
            {
                store.SetTransformFlags(node, store.TransformFlags(node) | (TransformFlags.ContainsES2015 |
                    TransformFlags.ContainsDestructuringAssignment |
                    PropagateAssignmentPatternFlags(store, store.Left(node))));
            }
        }
        else if (operatorKind == SyntaxKind.AsteriskAsteriskToken || operatorKind == SyntaxKind.AsteriskAsteriskEqualsToken)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2016);
        }
        else if (Utilities.IsLogicalOrCoalescingAssignmentOperator(operatorKind))
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2021);
        }
        if (operatorKind == SyntaxKind.InKeyword && FactoryNodeTests.IsPrivateIdentifier(store, store.Left(node)))
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsPrivateIdentifierInExpression);
        }
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
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
        var node = CreateBaseNode(SyntaxKind.ConditionalExpression);
        store.SetCondition(node, Parenthesizer.ParenthesizeConditionOfConditionalExpression(condition));
        store.SetQuestionToken(node, questionToken.IsNone ? CreateToken(SyntaxKind.QuestionToken) : questionToken);
        store.SetWhenTrue(node, Parenthesizer.ParenthesizeBranchOfConditionalExpression(whenTrue));
        store.SetColonToken(node, colonToken.IsNone ? CreateToken(SyntaxKind.ColonToken) : colonToken);
        store.SetWhenFalse(node, Parenthesizer.ParenthesizeBranchOfConditionalExpression(whenFalse));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Condition(node)) |
            PropagateChildFlags(store, store.QuestionToken(node)) |
            PropagateChildFlags(store, store.WhenTrue(node)) |
            PropagateChildFlags(store, store.ColonToken(node)) |
            PropagateChildFlags(store, store.WhenFalse(node))));
        // node.flowNodeWhenFalse = undefined;
        // node.flowNodeWhenTrue = undefined;
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTemplateExpression(Node head, NodeList templateSpans)
    {
        var node = CreateBaseNode(SyntaxKind.TemplateExpression);
        store.SetHead(node, head);
        store.SetTemplateSpans(node, CreateNodeArray(store, templateSpans));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Head(node)) |
            PropagateChildrenFlags(store.TemplateSpans(node)) |
            TransformFlags.ContainsES2015));
        return node;
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
        var node = CreateBaseNode(SyntaxKind.SpreadElement);
        store.SetExpression(node, Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            TransformFlags.ContainsES2015 |
            TransformFlags.ContainsRestOrSpread));
        return node;
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
        var node = CreateBaseNode(SyntaxKind.ExpressionWithTypeArguments);
        store.SetExpression(node, Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false));
        store.SetTypeArguments(node, !typeArguments.IsNone ? Parenthesizer.ParenthesizeTypeArguments(typeArguments) : default);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildrenFlags(store.TypeArguments(node)) |
            TransformFlags.ContainsES2015));
        return node;
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
        var node = CreateBaseNode(SyntaxKind.AsExpression);
        store.SetExpression(node, expression);
        store.SetType(node, type);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.Type(node)) |
            TransformFlags.ContainsTypeScript));
        return node;
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
        var node = CreateBaseNode(SyntaxKind.NonNullExpression);
        store.SetExpression(node, Parenthesizer.ParenthesizeLeftSideOfAccess(expression, /*optionalChain*/ false));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            TransformFlags.ContainsTypeScript));
        return node;
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
        var node = CreateBaseNode(SyntaxKind.TemplateSpan);
        store.SetExpression(node, expression);
        store.SetLiteral(node, literal);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.Literal(node)) |
            TransformFlags.ContainsES2015));
        return node;
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
