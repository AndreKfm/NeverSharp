// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/parenthesizerRules.ts, src/compiler/factory/nodeConverters.ts and src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>types.ts: ParenthesizerRules.</summary>
internal interface ParenthesizerRules
{
    Func<Node, Node> GetParenthesizeLeftSideOfBinaryForOperator(SyntaxKind binaryOperator);

    Func<Node, Node> GetParenthesizeRightSideOfBinaryForOperator(SyntaxKind binaryOperator);

    Node ParenthesizeLeftSideOfBinary(SyntaxKind binaryOperator, Node leftSide);

    Node ParenthesizeRightSideOfBinary(SyntaxKind binaryOperator, Node leftSide, Node rightSide);

    Node ParenthesizeExpressionOfComputedPropertyName(Node expression);

    Node ParenthesizeConditionOfConditionalExpression(Node condition);

    Node ParenthesizeBranchOfConditionalExpression(Node branch);

    Node ParenthesizeExpressionOfExportDefault(Node expression);

    Node ParenthesizeExpressionOfNew(Node expression);

    Node ParenthesizeLeftSideOfAccess(Node expression, bool? optionalChain = null);

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

/// <summary>types.ts: NodeConverters.</summary>
internal interface NodeConverters
{
    Node ConvertToFunctionBlock(Node node, bool? multiLine = null);

    Node ConvertToFunctionExpression(Node node);

    Node ConvertToClassExpression(Node node);

    Node ConvertToArrayAssignmentElement(Node element);

    Node ConvertToObjectAssignmentElement(Node element);

    Node ConvertToAssignmentPattern(Node node);

    Node ConvertToObjectAssignmentPattern(Node node);

    Node ConvertToArrayAssignmentPattern(Node node);

    Node ConvertToAssignmentElementTarget(Node node);
}

/// <summary>factory/parenthesizerRules.ts (nullParenthesizerRules; createParenthesizerRules is not reached by the parser's factory).</summary>
internal static class FactoryParenthesizerRules
{
    internal static readonly ParenthesizerRules NullParenthesizerRules = new NullParenthesizerRulesObject();

    /// <summary>The object literal `nullParenthesizerRules`.</summary>
    private sealed class NullParenthesizerRulesObject : ParenthesizerRules
    {
        public Func<Node, Node> GetParenthesizeLeftSideOfBinaryForOperator(SyntaxKind binaryOperator) => Core.Identity;

        public Func<Node, Node> GetParenthesizeRightSideOfBinaryForOperator(SyntaxKind binaryOperator) => Core.Identity;

        public Node ParenthesizeLeftSideOfBinary(SyntaxKind binaryOperator, Node leftSide) => leftSide;

        public Node ParenthesizeRightSideOfBinary(SyntaxKind binaryOperator, Node leftSide, Node rightSide) => rightSide;

        public Node ParenthesizeExpressionOfComputedPropertyName(Node expression) => expression;

        public Node ParenthesizeConditionOfConditionalExpression(Node condition) => condition;

        public Node ParenthesizeBranchOfConditionalExpression(Node branch) => branch;

        public Node ParenthesizeExpressionOfExportDefault(Node expression) => expression;

        // `cast(expression, isLeftHandSideExpression)` and friends: the assertion of the cast needs the node store, which

        // these interface members do not get (Track O, O6a); the value is returned as cast returns it when the test holds.
        public Node ParenthesizeExpressionOfNew(Node expression) => expression;

        public Node ParenthesizeLeftSideOfAccess(Node expression, bool? optionalChain = null) => expression;

        public Node ParenthesizeOperandOfPostfixUnary(Node operand) => operand;

        public Node ParenthesizeOperandOfPrefixUnary(Node operand) => operand;

        public NodeArray ParenthesizeExpressionsOfCommaDelimitedList(IReadOnlyList<Node> elements) => CastNodeArray(elements);

        public Node ParenthesizeExpressionForDisallowedComma(Node expression) => expression;

        public Node ParenthesizeExpressionOfExpressionStatement(Node expression) => expression;

        public Node ParenthesizeConciseBodyOfArrowFunction(Node body) => body;

        public Node ParenthesizeCheckTypeOfConditionalType(Node type) => type;

        public Node ParenthesizeExtendsTypeOfConditionalType(Node type) => type;

        public Node ParenthesizeOperandOfTypeOperator(Node type) => type;

        public Node ParenthesizeOperandOfReadonlyTypeOperator(Node type) => type;

        public Node ParenthesizeNonArrayTypeOfPostfixType(Node type) => type;

        public NodeArray ParenthesizeElementTypesOfTupleType(IReadOnlyList<Node> types) => CastNodeArray(types);

        public Node ParenthesizeElementTypeOfTupleType(Node type) => type;

        public Node ParenthesizeTypeOfOptionalType(Node type) => type;

        public Node ParenthesizeConstituentTypeOfUnionType(Node type) => type;

        public NodeArray ParenthesizeConstituentTypesOfUnionType(IReadOnlyList<Node> constituents) => CastNodeArray(constituents);

        public Node ParenthesizeConstituentTypeOfIntersectionType(Node type) => type;

        public NodeArray ParenthesizeConstituentTypesOfIntersectionType(IReadOnlyList<Node> constituents) => CastNodeArray(constituents);

        public Node ParenthesizeLeadingTypeArgument(Node typeNode) => typeNode;

        public NodeArray? ParenthesizeTypeArguments(IReadOnlyList<Node>? typeParameters) => typeParameters != null ? CastNodeArray(typeParameters) : null;

        /// <summary>`nodes => cast(nodes, isNodeArray)`.</summary>
        private static NodeArray CastNodeArray(IReadOnlyList<Node> nodes) => Core.Cast<NodeArray, IReadOnlyList<Node>>(nodes, n => UtilitiesPublic.IsNodeArray(n));
    }
}

/// <summary>factory/nodeConverters.ts (nullNodeConverters; createNodeConverters is not reached by the parser's factory).</summary>
internal static class FactoryNodeConverters
{
    internal static readonly NodeConverters NullNodeConverters = new NullNodeConvertersObject();

    /// <summary>The object literal `nullNodeConverters`: every member is `notImplemented`.</summary>
    private sealed class NullNodeConvertersObject : NodeConverters
    {
        public Node ConvertToFunctionBlock(Node node, bool? multiLine = null) => throw NotImplemented();

        public Node ConvertToFunctionExpression(Node node) => throw NotImplemented();

        public Node ConvertToClassExpression(Node node) => throw NotImplemented();

        public Node ConvertToArrayAssignmentElement(Node element) => throw NotImplemented();

        public Node ConvertToObjectAssignmentElement(Node element) => throw NotImplemented();

        public Node ConvertToAssignmentPattern(Node node) => throw NotImplemented();

        public Node ConvertToObjectAssignmentPattern(Node node) => throw NotImplemented();

        public Node ConvertToArrayAssignmentPattern(Node node) => throw NotImplemented();

        public Node ConvertToAssignmentElementTarget(Node node) => throw NotImplemented();

        // core.ts notImplemented: `throw new Error("Not implemented")`.
        private static NotImplementedException NotImplemented() => new("Not implemented");
    }
}
