// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/parenthesizerRules.ts and src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/precedence.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// types.ts: interface ParenthesizerRules. TypeScript's expression/type node unions (Expression, TypeNode,
/// LeftHandSideExpression, UnaryExpression, ConciseBody, ...) are Node here.
/// </summary>
internal interface ParenthesizerRules
{
    Func<Node, Node> GetParenthesizeLeftSideOfBinaryForOperator(SyntaxKind binaryOperator);

    Func<Node, Node> GetParenthesizeRightSideOfBinaryForOperator(SyntaxKind binaryOperator);

    Node ParenthesizeLeftSideOfBinary(SyntaxKind binaryOperator, Node leftSide);

    Node ParenthesizeRightSideOfBinary(SyntaxKind binaryOperator, Node? leftSide, Node rightSide);

    Node ParenthesizeExpressionOfComputedPropertyName(Node expression);

    Node ParenthesizeConditionOfConditionalExpression(Node condition);

    Node ParenthesizeBranchOfConditionalExpression(Node branch);

    Node ParenthesizeExpressionOfExportDefault(Node expression);

    Node ParenthesizeExpressionOfNew(Node expression);

    Node ParenthesizeLeftSideOfAccess(Node expression, bool optionalChain = false);

    Node ParenthesizeOperandOfPostfixUnary(Node operand);

    Node ParenthesizeOperandOfPrefixUnary(Node operand);

    NodeArray ParenthesizeExpressionsOfCommaDelimitedList(IReadOnlyList<Node> elements);

    Node ParenthesizeExpressionForDisallowedComma(Node expression);

    Node ParenthesizeExpressionOfExpressionStatement(Node expression);

    Node ParenthesizeConciseBodyOfArrowFunction(Node body);

    Node ParenthesizeCheckTypeOfConditionalType(Node type);

    Node ParenthesizeExtendsTypeOfConditionalType(Node type);

    Node ParenthesizeOperandOfTypeOperator(Node type);

    Node ParenthesizeOperandOfReadonlyTypeOperator(Node type);

    Node ParenthesizeNonArrayTypeOfPostfixType(Node type);

    NodeArray ParenthesizeElementTypesOfTupleType(IReadOnlyList<Node> types);

    Node ParenthesizeElementTypeOfTupleType(Node type);

    Node ParenthesizeTypeOfOptionalType(Node type);

    Node ParenthesizeConstituentTypeOfUnionType(Node type);

    NodeArray ParenthesizeConstituentTypesOfUnionType(IReadOnlyList<Node> constituents);

    Node ParenthesizeConstituentTypeOfIntersectionType(Node type);

    NodeArray ParenthesizeConstituentTypesOfIntersectionType(IReadOnlyList<Node> constituents);

    Node ParenthesizeLeadingTypeArgument(Node typeNode);

    NodeArray? ParenthesizeTypeArguments(IReadOnlyList<Node>? typeParameters);
}

/// <summary>parenthesizerRules.ts: nullParenthesizerRules (the rules of a factory created with NoParenthesizerRules).</summary>
internal sealed class NullParenthesizerRules : ParenthesizerRules
{
    internal static readonly NullParenthesizerRules Instance = new();

    private NullParenthesizerRules()
    {
    }

    public Func<Node, Node> GetParenthesizeLeftSideOfBinaryForOperator(SyntaxKind binaryOperator)
    {
        return Core.Identity;
    }

    public Func<Node, Node> GetParenthesizeRightSideOfBinaryForOperator(SyntaxKind binaryOperator)
    {
        return Core.Identity;
    }

    public Node ParenthesizeLeftSideOfBinary(SyntaxKind binaryOperator, Node leftSide)
    {
        return leftSide;
    }

    public Node ParenthesizeRightSideOfBinary(SyntaxKind binaryOperator, Node? leftSide, Node rightSide)
    {
        return rightSide;
    }

    public Node ParenthesizeExpressionOfComputedPropertyName(Node expression)
    {
        return expression;
    }

    public Node ParenthesizeConditionOfConditionalExpression(Node condition)
    {
        return condition;
    }

    public Node ParenthesizeBranchOfConditionalExpression(Node branch)
    {
        return branch;
    }

    public Node ParenthesizeExpressionOfExportDefault(Node expression)
    {
        return expression;
    }

    public Node ParenthesizeExpressionOfNew(Node expression)
    {
        return Core.Cast<Node, Node>(expression, UtilitiesPublic.IsLeftHandSideExpression);
    }

    public Node ParenthesizeLeftSideOfAccess(Node expression, bool optionalChain = false)
    {
        return Core.Cast<Node, Node>(expression, UtilitiesPublic.IsLeftHandSideExpression);
    }

    public Node ParenthesizeOperandOfPostfixUnary(Node operand)
    {
        return Core.Cast<Node, Node>(operand, UtilitiesPublic.IsLeftHandSideExpression);
    }

    public Node ParenthesizeOperandOfPrefixUnary(Node operand)
    {
        return Core.Cast<Node, Node>(operand, UtilitiesPublic.IsUnaryExpression);
    }

    public NodeArray ParenthesizeExpressionsOfCommaDelimitedList(IReadOnlyList<Node> elements)
    {
        return CastToNodeArray(elements);
    }

    public Node ParenthesizeExpressionForDisallowedComma(Node expression)
    {
        return expression;
    }

    public Node ParenthesizeExpressionOfExpressionStatement(Node expression)
    {
        return expression;
    }

    public Node ParenthesizeConciseBodyOfArrowFunction(Node body)
    {
        return body;
    }

    public Node ParenthesizeCheckTypeOfConditionalType(Node type)
    {
        return type;
    }

    public Node ParenthesizeExtendsTypeOfConditionalType(Node type)
    {
        return type;
    }

    public Node ParenthesizeOperandOfTypeOperator(Node type)
    {
        return type;
    }

    public Node ParenthesizeOperandOfReadonlyTypeOperator(Node type)
    {
        return type;
    }

    public Node ParenthesizeNonArrayTypeOfPostfixType(Node type)
    {
        return type;
    }

    public NodeArray ParenthesizeElementTypesOfTupleType(IReadOnlyList<Node> types)
    {
        return CastToNodeArray(types);
    }

    public Node ParenthesizeElementTypeOfTupleType(Node type)
    {
        return type;
    }

    public Node ParenthesizeTypeOfOptionalType(Node type)
    {
        return type;
    }

    public Node ParenthesizeConstituentTypeOfUnionType(Node type)
    {
        return type;
    }

    public NodeArray ParenthesizeConstituentTypesOfUnionType(IReadOnlyList<Node> constituents)
    {
        return CastToNodeArray(constituents);
    }

    public Node ParenthesizeConstituentTypeOfIntersectionType(Node type)
    {
        return type;
    }

    public NodeArray ParenthesizeConstituentTypesOfIntersectionType(IReadOnlyList<Node> constituents)
    {
        return CastToNodeArray(constituents);
    }

    public Node ParenthesizeLeadingTypeArgument(Node typeNode)
    {
        return typeNode;
    }

    public NodeArray? ParenthesizeTypeArguments(IReadOnlyList<Node>? typeParameters)
    {
        // TypeScript: `nodes => nodes && cast(nodes, isNodeArray)`.
        return typeParameters != null ? CastToNodeArray(typeParameters) : null;
    }

    /// <summary>TypeScript: `cast(nodes, isNodeArray)`.</summary>
    private static NodeArray CastToNodeArray(IReadOnlyList<Node> nodes)
    {
        return Core.Cast<NodeArray, IReadOnlyList<Node>>(nodes, static value => value is NodeArray);
    }
}
