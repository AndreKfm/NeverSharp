// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Ported from src/compiler/factory/parenthesizerRules.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/printer/factory.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>types.ts: ParenthesizerRules.</summary>
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

/// <summary>factory/parenthesizerRules.ts: nullParenthesizerRules (createParenthesizerRules is not in the parser's slice).</summary>
internal static class ParenthesizerRulesNull
{
    // (Typed as the implementing class so that CA1859 has nothing to upgrade; it converts to ParenthesizerRules.)
    internal static readonly NullRules NullParenthesizerRules = new();

    /// <summary>The object literal `nullParenthesizerRules`: identity functions and casts.</summary>
    internal sealed class NullRules : ParenthesizerRules
    {
        // getParenthesizeLeftSideOfBinaryForOperator: _ => identity
        public Func<Node, Node> GetParenthesizeLeftSideOfBinaryForOperator(SyntaxKind binaryOperator) => Core.Identity;

        // getParenthesizeRightSideOfBinaryForOperator: _ => identity
        public Func<Node, Node> GetParenthesizeRightSideOfBinaryForOperator(SyntaxKind binaryOperator) => Core.Identity;

        // parenthesizeLeftSideOfBinary: (_binaryOperator, leftSide) => leftSide
        public Node ParenthesizeLeftSideOfBinary(SyntaxKind binaryOperator, Node leftSide) => leftSide;

        // parenthesizeRightSideOfBinary: (_binaryOperator, _leftSide, rightSide) => rightSide
        public Node ParenthesizeRightSideOfBinary(SyntaxKind binaryOperator, Node? leftSide, Node rightSide) => rightSide;

        // parenthesizeExpressionOfComputedPropertyName: identity
        public Node ParenthesizeExpressionOfComputedPropertyName(Node expression) => Core.Identity(expression);

        // parenthesizeConditionOfConditionalExpression: identity
        public Node ParenthesizeConditionOfConditionalExpression(Node condition) => Core.Identity(condition);

        // parenthesizeBranchOfConditionalExpression: identity
        public Node ParenthesizeBranchOfConditionalExpression(Node branch) => Core.Identity(branch);

        // parenthesizeExpressionOfExportDefault: identity
        public Node ParenthesizeExpressionOfExportDefault(Node expression) => Core.Identity(expression);

        // parenthesizeExpressionOfNew: expression => cast(expression, isLeftHandSideExpression)
        public Node ParenthesizeExpressionOfNew(Node expression) => Core.Cast<Node, Node>(expression, UtilitiesPublic.IsLeftHandSideExpression);

        // parenthesizeLeftSideOfAccess: expression => cast(expression, isLeftHandSideExpression)
        public Node ParenthesizeLeftSideOfAccess(Node expression, bool optionalChain = false) => Core.Cast<Node, Node>(expression, UtilitiesPublic.IsLeftHandSideExpression);

        // parenthesizeOperandOfPostfixUnary: operand => cast(operand, isLeftHandSideExpression)
        public Node ParenthesizeOperandOfPostfixUnary(Node operand) => Core.Cast<Node, Node>(operand, UtilitiesPublic.IsLeftHandSideExpression);

        // parenthesizeOperandOfPrefixUnary: operand => cast(operand, isUnaryExpression)
        public Node ParenthesizeOperandOfPrefixUnary(Node operand) => Core.Cast<Node, Node>(operand, UtilitiesPublic.IsUnaryExpression);

        // parenthesizeExpressionsOfCommaDelimitedList: nodes => cast(nodes, isNodeArray)
        public NodeArray ParenthesizeExpressionsOfCommaDelimitedList(IReadOnlyList<Node> elements) => Core.Cast<NodeArray, IReadOnlyList<Node>>(elements, UtilitiesPublic.IsNodeArray);

        // parenthesizeExpressionForDisallowedComma: identity
        public Node ParenthesizeExpressionForDisallowedComma(Node expression) => Core.Identity(expression);

        // parenthesizeExpressionOfExpressionStatement: identity
        public Node ParenthesizeExpressionOfExpressionStatement(Node expression) => Core.Identity(expression);

        // parenthesizeConciseBodyOfArrowFunction: identity
        public Node ParenthesizeConciseBodyOfArrowFunction(Node body) => Core.Identity(body);

        // parenthesizeCheckTypeOfConditionalType: identity
        public Node ParenthesizeCheckTypeOfConditionalType(Node type) => Core.Identity(type);

        // parenthesizeExtendsTypeOfConditionalType: identity
        public Node ParenthesizeExtendsTypeOfConditionalType(Node type) => Core.Identity(type);

        // parenthesizeConstituentTypesOfUnionType: nodes => cast(nodes, isNodeArray)
        public NodeArray ParenthesizeConstituentTypesOfUnionType(IReadOnlyList<Node> constituents) => Core.Cast<NodeArray, IReadOnlyList<Node>>(constituents, UtilitiesPublic.IsNodeArray);

        // parenthesizeConstituentTypeOfUnionType: identity
        public Node ParenthesizeConstituentTypeOfUnionType(Node type) => Core.Identity(type);

        // parenthesizeConstituentTypesOfIntersectionType: nodes => cast(nodes, isNodeArray)
        public NodeArray ParenthesizeConstituentTypesOfIntersectionType(IReadOnlyList<Node> constituents) => Core.Cast<NodeArray, IReadOnlyList<Node>>(constituents, UtilitiesPublic.IsNodeArray);

        // parenthesizeConstituentTypeOfIntersectionType: identity
        public Node ParenthesizeConstituentTypeOfIntersectionType(Node type) => Core.Identity(type);

        // parenthesizeOperandOfTypeOperator: identity
        public Node ParenthesizeOperandOfTypeOperator(Node type) => Core.Identity(type);

        // parenthesizeOperandOfReadonlyTypeOperator: identity
        public Node ParenthesizeOperandOfReadonlyTypeOperator(Node type) => Core.Identity(type);

        // parenthesizeNonArrayTypeOfPostfixType: identity
        public Node ParenthesizeNonArrayTypeOfPostfixType(Node type) => Core.Identity(type);

        // parenthesizeElementTypesOfTupleType: nodes => cast(nodes, isNodeArray)
        public NodeArray ParenthesizeElementTypesOfTupleType(IReadOnlyList<Node> types) => Core.Cast<NodeArray, IReadOnlyList<Node>>(types, UtilitiesPublic.IsNodeArray);

        // parenthesizeElementTypeOfTupleType: identity
        public Node ParenthesizeElementTypeOfTupleType(Node type) => Core.Identity(type);

        // parenthesizeTypeOfOptionalType: identity
        public Node ParenthesizeTypeOfOptionalType(Node type) => Core.Identity(type);

        // parenthesizeTypeArguments: nodes => nodes && cast(nodes, isNodeArray)
        public NodeArray? ParenthesizeTypeArguments(IReadOnlyList<Node>? typeParameters) => typeParameters != null ? Core.Cast<NodeArray, IReadOnlyList<Node>>(typeParameters, UtilitiesPublic.IsNodeArray) : null;

        // parenthesizeLeadingTypeArgument: identity
        public Node ParenthesizeLeadingTypeArgument(Node typeNode) => Core.Identity(typeNode);
    }
}
