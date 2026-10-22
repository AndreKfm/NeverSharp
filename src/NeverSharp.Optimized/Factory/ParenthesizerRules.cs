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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeLeftSideOfBinary(SyntaxKind binaryOperator, Node leftSide);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeRightSideOfBinary(SyntaxKind binaryOperator, Node leftSide, Node rightSide);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeExpressionOfComputedPropertyName(Node expression);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeConditionOfConditionalExpression(Node condition);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeBranchOfConditionalExpression(Node branch);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeExpressionOfExportDefault(Node expression);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeExpressionOfNew(Node expression);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeLeftSideOfAccess(Node expression, bool? optionalChain = null);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeOperandOfPostfixUnary(Node operand);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeOperandOfPrefixUnary(Node operand);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    NodeList ParenthesizeExpressionsOfCommaDelimitedList(NodeList elements);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeExpressionForDisallowedComma(Node expression);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeExpressionOfExpressionStatement(Node expression);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeConciseBodyOfArrowFunction(Node body);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeCheckTypeOfConditionalType(Node type);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeExtendsTypeOfConditionalType(Node type);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeOperandOfTypeOperator(Node type);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeOperandOfReadonlyTypeOperator(Node type);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeNonArrayTypeOfPostfixType(Node type);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    NodeList ParenthesizeElementTypesOfTupleType(NodeList types);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeElementTypeOfTupleType(Node type);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeTypeOfOptionalType(Node type);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeConstituentTypeOfUnionType(Node type);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    NodeList ParenthesizeConstituentTypesOfUnionType(NodeList constituents);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeConstituentTypeOfIntersectionType(Node type);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    NodeList ParenthesizeConstituentTypesOfIntersectionType(NodeList constituents);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node ParenthesizeLeadingTypeArgument(Node typeNode);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    NodeList ParenthesizeTypeArguments(NodeList typeParameters);
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

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeLeftSideOfBinary(SyntaxKind binaryOperator, Node leftSide) => leftSide;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeRightSideOfBinary(SyntaxKind binaryOperator, Node leftSide, Node rightSide) => rightSide;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeExpressionOfComputedPropertyName(Node expression) => expression;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeConditionOfConditionalExpression(Node condition) => condition;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeBranchOfConditionalExpression(Node branch) => branch;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeExpressionOfExportDefault(Node expression) => expression;

        // `cast(expression, isLeftHandSideExpression)` and friends: the assertion of the cast needs the node store, which

        // these interface members do not get (Track O, O6a); the value is returned as cast returns it when the test holds.
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeExpressionOfNew(Node expression) => expression;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeLeftSideOfAccess(Node expression, bool? optionalChain = null) => expression;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeOperandOfPostfixUnary(Node operand) => operand;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeOperandOfPrefixUnary(Node operand) => operand;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public NodeList ParenthesizeExpressionsOfCommaDelimitedList(NodeList elements) => CastNodeArray(elements);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeExpressionForDisallowedComma(Node expression) => expression;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeExpressionOfExpressionStatement(Node expression) => expression;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeConciseBodyOfArrowFunction(Node body) => body;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeCheckTypeOfConditionalType(Node type) => type;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeExtendsTypeOfConditionalType(Node type) => type;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeOperandOfTypeOperator(Node type) => type;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeOperandOfReadonlyTypeOperator(Node type) => type;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeNonArrayTypeOfPostfixType(Node type) => type;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public NodeList ParenthesizeElementTypesOfTupleType(NodeList types) => CastNodeArray(types);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeElementTypeOfTupleType(Node type) => type;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeTypeOfOptionalType(Node type) => type;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeConstituentTypeOfUnionType(Node type) => type;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public NodeList ParenthesizeConstituentTypesOfUnionType(NodeList constituents) => CastNodeArray(constituents);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeConstituentTypeOfIntersectionType(Node type) => type;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public NodeList ParenthesizeConstituentTypesOfIntersectionType(NodeList constituents) => CastNodeArray(constituents);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeLeadingTypeArgument(Node typeNode) => typeNode;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public NodeList ParenthesizeTypeArguments(NodeList typeParameters) => !typeParameters.IsNone ? CastNodeArray(typeParameters) : default;

        /// <summary>`nodes => cast(nodes, isNodeArray)`: a NodeList is always a NodeArray (Track O, O7a: plain arrays are list builders), so the cast holds.</summary>
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        private static NodeList CastNodeArray(NodeList nodes) => nodes;
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
