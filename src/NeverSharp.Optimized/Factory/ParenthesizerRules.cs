// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/parenthesizerRules.ts, src/compiler/factory/nodeConverters.ts and src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D1 adds createParenthesizerRules and createNodeConverters for the
// transformers' factory, module D4 the assignment-pattern converters.

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

/// <summary>factory/parenthesizerRules.ts: nullParenthesizerRules and createParenthesizerRules.</summary>
internal static class FactoryParenthesizerRules
{
    // (Track O, step O12: typed as the sealed class, so that calls through NodeFactory.Parenthesizer are direct.)
    internal static readonly NullParenthesizerRulesObject NullParenthesizerRules = new NullParenthesizerRulesObject(null);

    /// <summary>
    /// The object literal `nullParenthesizerRules` (<see cref="NullParenthesizerRules"/>), and the factory's rules
    /// object. Track O (module D1): the factory calls its rules through this sealed class (step O12); a factory without
    /// NoParenthesizerRules gets an instance that forwards every rule to createParenthesizerRules' object
    /// (<see cref="ParenthesizerRulesObject"/>), the parser's factory the shared instance without one.
    /// </summary>
    internal sealed class NullParenthesizerRulesObject : ParenthesizerRules
    {
        private readonly ParenthesizerRules? rules;

        internal NullParenthesizerRulesObject(ParenthesizerRules? rules)
        {
            this.rules = rules;
        }

        public Func<Node, Node> GetParenthesizeLeftSideOfBinaryForOperator(SyntaxKind binaryOperator) => rules is null ? Core.Identity : rules.GetParenthesizeLeftSideOfBinaryForOperator(binaryOperator);

        public Func<Node, Node> GetParenthesizeRightSideOfBinaryForOperator(SyntaxKind binaryOperator) => rules is null ? Core.Identity : rules.GetParenthesizeRightSideOfBinaryForOperator(binaryOperator);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeLeftSideOfBinary(SyntaxKind binaryOperator, Node leftSide) => rules is null ? leftSide : rules.ParenthesizeLeftSideOfBinary(binaryOperator, leftSide);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeRightSideOfBinary(SyntaxKind binaryOperator, Node leftSide, Node rightSide) => rules is null ? rightSide : rules.ParenthesizeRightSideOfBinary(binaryOperator, leftSide, rightSide);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeExpressionOfComputedPropertyName(Node expression) => rules is null ? expression : rules.ParenthesizeExpressionOfComputedPropertyName(expression);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeConditionOfConditionalExpression(Node condition) => rules is null ? condition : rules.ParenthesizeConditionOfConditionalExpression(condition);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeBranchOfConditionalExpression(Node branch) => rules is null ? branch : rules.ParenthesizeBranchOfConditionalExpression(branch);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeExpressionOfExportDefault(Node expression) => rules is null ? expression : rules.ParenthesizeExpressionOfExportDefault(expression);

        // `cast(expression, isLeftHandSideExpression)` and friends: the assertion of the cast needs the node store, which
        // these interface members do not get (Track O, O6a); the value is returned as cast returns it when the test holds.
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeExpressionOfNew(Node expression) => rules is null ? expression : rules.ParenthesizeExpressionOfNew(expression);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeLeftSideOfAccess(Node expression, bool? optionalChain = null) => rules is null ? expression : rules.ParenthesizeLeftSideOfAccess(expression, optionalChain);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeOperandOfPostfixUnary(Node operand) => rules is null ? operand : rules.ParenthesizeOperandOfPostfixUnary(operand);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeOperandOfPrefixUnary(Node operand) => rules is null ? operand : rules.ParenthesizeOperandOfPrefixUnary(operand);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public NodeList ParenthesizeExpressionsOfCommaDelimitedList(NodeList elements) => rules is null ? CastNodeArray(elements) : rules.ParenthesizeExpressionsOfCommaDelimitedList(elements);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeExpressionForDisallowedComma(Node expression) => rules is null ? expression : rules.ParenthesizeExpressionForDisallowedComma(expression);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeExpressionOfExpressionStatement(Node expression) => rules is null ? expression : rules.ParenthesizeExpressionOfExpressionStatement(expression);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeConciseBodyOfArrowFunction(Node body) => rules is null ? body : rules.ParenthesizeConciseBodyOfArrowFunction(body);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeCheckTypeOfConditionalType(Node type) => rules is null ? type : rules.ParenthesizeCheckTypeOfConditionalType(type);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeExtendsTypeOfConditionalType(Node type) => rules is null ? type : rules.ParenthesizeExtendsTypeOfConditionalType(type);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeOperandOfTypeOperator(Node type) => rules is null ? type : rules.ParenthesizeOperandOfTypeOperator(type);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeOperandOfReadonlyTypeOperator(Node type) => rules is null ? type : rules.ParenthesizeOperandOfReadonlyTypeOperator(type);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeNonArrayTypeOfPostfixType(Node type) => rules is null ? type : rules.ParenthesizeNonArrayTypeOfPostfixType(type);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public NodeList ParenthesizeElementTypesOfTupleType(NodeList types) => rules is null ? CastNodeArray(types) : rules.ParenthesizeElementTypesOfTupleType(types);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeElementTypeOfTupleType(Node type) => rules is null ? type : rules.ParenthesizeElementTypeOfTupleType(type);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeTypeOfOptionalType(Node type) => rules is null ? type : rules.ParenthesizeTypeOfOptionalType(type);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeConstituentTypeOfUnionType(Node type) => rules is null ? type : rules.ParenthesizeConstituentTypeOfUnionType(type);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public NodeList ParenthesizeConstituentTypesOfUnionType(NodeList constituents) => rules is null ? CastNodeArray(constituents) : rules.ParenthesizeConstituentTypesOfUnionType(constituents);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeConstituentTypeOfIntersectionType(Node type) => rules is null ? type : rules.ParenthesizeConstituentTypeOfIntersectionType(type);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public NodeList ParenthesizeConstituentTypesOfIntersectionType(NodeList constituents) => rules is null ? CastNodeArray(constituents) : rules.ParenthesizeConstituentTypesOfIntersectionType(constituents);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node ParenthesizeLeadingTypeArgument(Node typeNode) => rules is null ? typeNode : rules.ParenthesizeLeadingTypeArgument(typeNode);

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public NodeList ParenthesizeTypeArguments(NodeList typeParameters) => rules is not null ? rules.ParenthesizeTypeArguments(typeParameters) : !typeParameters.IsNone ? CastNodeArray(typeParameters) : default;

        /// <summary>`nodes => cast(nodes, isNodeArray)`: a NodeList is always a NodeArray (Track O, O7a: plain arrays are list builders), so the cast holds.</summary>
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        private static NodeList CastNodeArray(NodeList nodes) => nodes;
    }

    /// <summary>createParenthesizerRules(factory) (module D1).</summary>
    [NoAllocUnchecked("the transformers' factory: created once per emit, not on the parser's path")]
    internal static ParenthesizerRules CreateParenthesizerRules(NodeFactory factory) => new ParenthesizerRulesObject(factory);

    /// <summary>
    /// The object createParenthesizerRules returns; its closure's functions are the instance methods. (Track O: the
    /// rules keep the factory's NoAlloc contract: new lists are built with the store's list builder, and sameMap's
    /// function argument is a rule selector instead of a delegate.)
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private sealed class ParenthesizerRulesObject : ParenthesizerRules
    {
        private readonly NodeFactory factory;
        private Dictionary<SyntaxKind, Func<Node, Node>>? binaryLeftOperandParenthesizerCache;
        private Dictionary<SyntaxKind, Func<Node, Node>>? binaryRightOperandParenthesizerCache;

        internal ParenthesizerRulesObject(NodeFactory factory)
        {
            this.factory = factory;
        }

        private NodeStore store => factory.store;

        [NoAllocUnchecked("a memoized rule per operator (the printer's binary operands)")]
        public Func<Node, Node> GetParenthesizeLeftSideOfBinaryForOperator(SyntaxKind operatorKind)
        {
            binaryLeftOperandParenthesizerCache ??= [];
            if (!binaryLeftOperandParenthesizerCache.TryGetValue(operatorKind, out var parenthesizerRule))
            {
                parenthesizerRule = node => ParenthesizeLeftSideOfBinary(operatorKind, node);
                binaryLeftOperandParenthesizerCache[operatorKind] = parenthesizerRule;
            }
            return parenthesizerRule;
        }

        [NoAllocUnchecked("a memoized rule per operator (the printer's binary operands)")]
        public Func<Node, Node> GetParenthesizeRightSideOfBinaryForOperator(SyntaxKind operatorKind)
        {
            binaryRightOperandParenthesizerCache ??= [];
            if (!binaryRightOperandParenthesizerCache.TryGetValue(operatorKind, out var parenthesizerRule))
            {
                parenthesizerRule = node => ParenthesizeRightSideOfBinary(operatorKind, /*leftSide*/ default, node);
                binaryRightOperandParenthesizerCache[operatorKind] = parenthesizerRule;
            }
            return parenthesizerRule;
        }

        private static bool MixingBinaryOperatorsRequiresParentheses(SyntaxKind a, SyntaxKind b)
        {
            if (a == SyntaxKind.QuestionQuestionToken)
            {
                return b == SyntaxKind.AmpersandAmpersandToken || b == SyntaxKind.BarBarToken;
            }
            if (b == SyntaxKind.QuestionQuestionToken)
            {
                return a == SyntaxKind.AmpersandAmpersandToken || a == SyntaxKind.BarBarToken;
            }
            return false;
        }

        /// <summary>Determines whether the operand to a BinaryExpression needs to be parenthesized.</summary>
        private bool BinaryOperandNeedsParentheses(SyntaxKind binaryOperator, Node operand, bool isLeftSideOfBinary, Node leftOperand)
        {
            var emittedOperand = UtilitiesPublic.SkipPartiallyEmittedExpressions(store, operand);
            if (store.Kind(emittedOperand) == SyntaxKind.BinaryExpression && MixingBinaryOperatorsRequiresParentheses(binaryOperator, store.Kind(store.OperatorToken(emittedOperand))))
            {
                return true;
            }
            // If the operand has lower precedence, then it needs to be parenthesized to preserve the
            // intent of the expression. If the operand has higher precedence, then it does not need to be
            // parenthesized. If the operand has the same precedence, then we need to check the associativity of
            // the operator based on whether this is the left or right operand of the expression.
            var binaryOperatorPrecedence = Utilities.GetOperatorPrecedence(SyntaxKind.BinaryExpression, binaryOperator);
            var binaryOperatorAssociativity = Utilities.GetOperatorAssociativity(SyntaxKind.BinaryExpression, binaryOperator);
            if (!isLeftSideOfBinary && store.Kind(operand) == SyntaxKind.ArrowFunction && binaryOperatorPrecedence > OperatorPrecedence.Assignment)
            {
                // We need to parenthesize arrow functions on the right side to avoid it being
                // parsed as parenthesized expression: `a && (() => {})`
                return true;
            }
            var operandPrecedence = Utilities.GetExpressionPrecedence(store, emittedOperand);
            var comparison = operandPrecedence < binaryOperatorPrecedence ? -1 : operandPrecedence > binaryOperatorPrecedence ? 1 : 0;
            if (comparison < 0)
            {
                // If the operand is the right side of a right-associative binary operation
                // and is a yield expression, then we do not need parentheses.
                if (!isLeftSideOfBinary
                    && binaryOperatorAssociativity == Associativity.Right
                    && store.Kind(operand) == SyntaxKind.YieldExpression)
                {
                    return false;
                }
                return true;
            }
            if (comparison > 0)
            {
                return false;
            }
            if (isLeftSideOfBinary)
            {
                // No need to parenthesize the left operand when the binary operator is
                // left associative; parentheses are needed when it is right associative.
                return binaryOperatorAssociativity == Associativity.Right;
            }
            if (store.Kind(emittedOperand) == SyntaxKind.BinaryExpression
                && store.Kind(store.OperatorToken(emittedOperand)) == binaryOperator)
            {
                // No need to parenthesize the right operand when the binary operator and
                // operand are the same and one of the following:
                //  x*(a*b)     => x*a*b
                //  x|(a|b)     => x|a|b
                //  x&(a&b)     => x&a&b
                //  x^(a^b)     => x^a^b
                if (OperatorHasAssociativeProperty(binaryOperator))
                {
                    return false;
                }

                // No need to parenthesize the right operand when the binary operator
                // is plus (+) if both the left and right operands consist solely of either
                // literals of the same kind or binary plus (+) expressions for literals of
                // the same kind (recursively).
                if (binaryOperator == SyntaxKind.PlusToken)
                {
                    var leftKind = !leftOperand.IsNone ? GetLiteralKindOfBinaryPlusOperand(leftOperand) : SyntaxKind.Unknown;
                    if (UtilitiesPublic.IsLiteralKind(leftKind) && leftKind == GetLiteralKindOfBinaryPlusOperand(emittedOperand))
                    {
                        return false;
                    }
                }
            }

            // No need to parenthesize the right operand when the operand is right
            // associative; parentheses are needed when it is left associative.
            var operandAssociativity = Utilities.GetExpressionAssociativity(store, emittedOperand);
            return operandAssociativity == Associativity.Left;
        }

        /// <summary>Determines whether a binary operator is mathematically associative.</summary>
        private static bool OperatorHasAssociativeProperty(SyntaxKind binaryOperator)
        {
            // While addition is associative in mathematics, JavaScript's `+` is not
            // guaranteed to be associative as it is overloaded with string concatenation.
            return binaryOperator == SyntaxKind.AsteriskToken
                || binaryOperator == SyntaxKind.BarToken
                || binaryOperator == SyntaxKind.AmpersandToken
                || binaryOperator == SyntaxKind.CaretToken
                || binaryOperator == SyntaxKind.CommaToken;
        }

        /// <summary>
        /// Whether an expression consists of a homogeneous set of literal expressions or binary plus expressions that
        /// all share the same literal kind. (TypeScript caches the result on the node, `cachedLiteralKind`; the value
        /// does not change, so Track O recomputes it.)
        /// </summary>
        private SyntaxKind GetLiteralKindOfBinaryPlusOperand(Node node)
        {
            node = UtilitiesPublic.SkipPartiallyEmittedExpressions(store, node);

            if (UtilitiesPublic.IsLiteralKind(store.Kind(node)))
            {
                return store.Kind(node);
            }

            if (store.Kind(node) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(node)) == SyntaxKind.PlusToken)
            {
                var leftKind = GetLiteralKindOfBinaryPlusOperand(store.Left(node));
                return UtilitiesPublic.IsLiteralKind(leftKind) && leftKind == GetLiteralKindOfBinaryPlusOperand(store.Right(node))
                    ? leftKind
                    : SyntaxKind.Unknown;
            }

            return SyntaxKind.Unknown;
        }

        /// <summary>
        /// Wraps the operand to a BinaryExpression in parentheses if they are needed to preserve the intended
        /// order of operations.
        /// </summary>
        private Node ParenthesizeBinaryOperand(SyntaxKind binaryOperator, Node operand, bool isLeftSideOfBinary, Node leftOperand = default)
        {
            var skipped = UtilitiesPublic.SkipPartiallyEmittedExpressions(store, operand);

            // If the resulting expression is already parenthesized, we do not need to do any further processing.
            if (store.Kind(skipped) == SyntaxKind.ParenthesizedExpression)
            {
                return operand;
            }

            return BinaryOperandNeedsParentheses(binaryOperator, operand, isLeftSideOfBinary, leftOperand)
                ? factory.CreateParenthesizedExpression(operand)
                : operand;
        }

        public Node ParenthesizeLeftSideOfBinary(SyntaxKind binaryOperator, Node leftSide)
        {
            return ParenthesizeBinaryOperand(binaryOperator, leftSide, isLeftSideOfBinary: true);
        }

        public Node ParenthesizeRightSideOfBinary(SyntaxKind binaryOperator, Node leftSide, Node rightSide)
        {
            return ParenthesizeBinaryOperand(binaryOperator, rightSide, isLeftSideOfBinary: false, leftSide);
        }

        public Node ParenthesizeExpressionOfComputedPropertyName(Node expression)
        {
            return FactoryUtilities.IsCommaSequence(store, expression) ? factory.CreateParenthesizedExpression(expression) : expression;
        }

        public Node ParenthesizeConditionOfConditionalExpression(Node condition)
        {
            var conditionalPrecedence = Utilities.GetOperatorPrecedence(SyntaxKind.ConditionalExpression, SyntaxKind.QuestionToken);
            var emittedCondition = UtilitiesPublic.SkipPartiallyEmittedExpressions(store, condition);
            var conditionPrecedence = Utilities.GetExpressionPrecedence(store, emittedCondition);
            if ((int)conditionPrecedence <= (int)conditionalPrecedence)
            {
                return factory.CreateParenthesizedExpression(condition);
            }
            return condition;
        }

        public Node ParenthesizeBranchOfConditionalExpression(Node branch)
        {
            // per ES grammar both 'whenTrue' and 'whenFalse' parts of conditional expression are assignment expressions
            // so in case when comma expression is introduced as a part of previous transformations
            // if should be wrapped in parens since comma operator has the lowest precedence
            var emittedExpression = UtilitiesPublic.SkipPartiallyEmittedExpressions(store, branch);
            return FactoryUtilities.IsCommaSequence(store, emittedExpression)
                ? factory.CreateParenthesizedExpression(branch)
                : branch;
        }

        /// <summary>
        /// `export default` accepts _AssigmentExpression_ but has a lookahead restriction for `function`,
        /// `async function`, and `class`.
        /// </summary>
        public Node ParenthesizeExpressionOfExportDefault(Node expression)
        {
            var check = UtilitiesPublic.SkipPartiallyEmittedExpressions(store, expression);
            var needsParens = FactoryUtilities.IsCommaSequence(store, check);
            if (!needsParens)
            {
                switch (store.Kind(Utilities.GetLeftmostExpression(store, check, stopAtCallExpressions: false)))
                {
                    case SyntaxKind.ClassExpression:
                    case SyntaxKind.FunctionExpression:
                        needsParens = true;
                        break;
                }
            }
            return needsParens ? factory.CreateParenthesizedExpression(expression) : expression;
        }

        /// <summary>
        /// Wraps an expression in parentheses if it is needed in order to use the expression
        /// as the expression of a `NewExpression` node.
        /// </summary>
        public Node ParenthesizeExpressionOfNew(Node expression)
        {
            var leftmostExpr = Utilities.GetLeftmostExpression(store, expression, stopAtCallExpressions: true);
            switch (store.Kind(leftmostExpr))
            {
                case SyntaxKind.CallExpression:
                    return factory.CreateParenthesizedExpression(expression);

                case SyntaxKind.NewExpression:
                    return store.Arguments(leftmostExpr).IsNone
                        ? factory.CreateParenthesizedExpression(expression)
                        : expression;
            }

            return ParenthesizeLeftSideOfAccess(expression);
        }

        /// <summary>
        /// Wraps an expression in parentheses if it is needed in order to use the expression for
        /// property or element access.
        /// </summary>
        public Node ParenthesizeLeftSideOfAccess(Node expression, bool? optionalChain = null)
        {
            // isLeftHandSideExpression is almost the correct criterion for when it is not necessary
            // to parenthesize the expression before a dot. The known exception is:
            //
            //    NewExpression:
            //       new C.x        -> not the same as (new C).x
            //
            var emittedExpression = UtilitiesPublic.SkipPartiallyEmittedExpressions(store, expression);
            if (UtilitiesPublic.IsLeftHandSideExpression(store, emittedExpression)
                && (store.Kind(emittedExpression) != SyntaxKind.NewExpression || !store.Arguments(emittedExpression).IsNone)
                && (optionalChain == true || !UtilitiesPublic.IsOptionalChain(store, emittedExpression)))
            {
                return expression;
            }

            return FactoryUtilitiesPublic.SetTextRange(store, factory.CreateParenthesizedExpression(expression), expression);
        }

        public Node ParenthesizeOperandOfPostfixUnary(Node operand)
        {
            return UtilitiesPublic.IsLeftHandSideExpression(store, operand) ? operand : FactoryUtilitiesPublic.SetTextRange(store, factory.CreateParenthesizedExpression(operand), operand);
        }

        public Node ParenthesizeOperandOfPrefixUnary(Node operand)
        {
            return UtilitiesPublic.IsUnaryExpression(store, operand) ? operand : FactoryUtilitiesPublic.SetTextRange(store, factory.CreateParenthesizedExpression(operand), operand);
        }

        public NodeList ParenthesizeExpressionsOfCommaDelimitedList(NodeList elements)
        {
            var result = SameMap(elements, ListRule.DisallowedComma, store.HasTrailingComma(elements));
            var list = result.IsNone ? NodeFactory.CreateNodeArray(store, elements, store.HasTrailingComma(elements)) : result;
            return FactoryUtilitiesPublic.SetTextRange(store, list, elements);
        }

        public Node ParenthesizeExpressionForDisallowedComma(Node expression)
        {
            var emittedExpression = UtilitiesPublic.SkipPartiallyEmittedExpressions(store, expression);
            var expressionPrecedence = Utilities.GetExpressionPrecedence(store, emittedExpression);
            var commaPrecedence = Utilities.GetOperatorPrecedence(SyntaxKind.BinaryExpression, SyntaxKind.CommaToken);
            return expressionPrecedence > commaPrecedence ? expression : FactoryUtilitiesPublic.SetTextRange(store, factory.CreateParenthesizedExpression(expression), expression);
        }

        public Node ParenthesizeExpressionOfExpressionStatement(Node expression)
        {
            var emittedExpression = UtilitiesPublic.SkipPartiallyEmittedExpressions(store, expression);
            if (store.Kind(emittedExpression) == SyntaxKind.CallExpression)
            {
                var callee = store.Expression(emittedExpression);
                var kind = store.Kind(UtilitiesPublic.SkipPartiallyEmittedExpressions(store, callee));
                if (kind == SyntaxKind.FunctionExpression || kind == SyntaxKind.ArrowFunction)
                {
                    var updated = factory.UpdateCallExpression(
                        emittedExpression,
                        FactoryUtilitiesPublic.SetTextRange(store, factory.CreateParenthesizedExpression(callee), callee),
                        store.TypeArguments(emittedExpression),
                        store.Arguments(emittedExpression));
                    return RestorePartiallyEmittedExpressions(expression, updated);
                }
            }

            var leftmostExpressionKind = store.Kind(Utilities.GetLeftmostExpression(store, emittedExpression, stopAtCallExpressions: false));
            if (leftmostExpressionKind == SyntaxKind.ObjectLiteralExpression || leftmostExpressionKind == SyntaxKind.FunctionExpression)
            {
                return FactoryUtilitiesPublic.SetTextRange(store, factory.CreateParenthesizedExpression(expression), expression);
            }

            return expression;
        }

        public Node ParenthesizeConciseBodyOfArrowFunction(Node body)
        {
            if (store.Kind(body) != SyntaxKind.Block && (FactoryUtilities.IsCommaSequence(store, body) || store.Kind(Utilities.GetLeftmostExpression(store, body, stopAtCallExpressions: false)) == SyntaxKind.ObjectLiteralExpression))
            {
                return FactoryUtilitiesPublic.SetTextRange(store, factory.CreateParenthesizedExpression(body), body);
            }

            return body;
        }

        // - The check type (the `UnionType`, above) does not allow function, constructor, or conditional types (they must be parenthesized)
        // - The extends type (the first `Type`, above) does not allow conditional types (they must be parenthesized). Function and constructor types are fine.
        // - The true and false branch types (the second and third `Type` non-terminals, above) allow any type
        public Node ParenthesizeCheckTypeOfConditionalType(Node checkType)
        {
            switch (store.Kind(checkType))
            {
                case SyntaxKind.FunctionType:
                case SyntaxKind.ConstructorType:
                case SyntaxKind.ConditionalType:
                    return factory.CreateParenthesizedType(checkType);
            }
            return checkType;
        }

        public Node ParenthesizeExtendsTypeOfConditionalType(Node extendsType)
        {
            switch (store.Kind(extendsType))
            {
                case SyntaxKind.ConditionalType:
                    return factory.CreateParenthesizedType(extendsType);
            }
            return extendsType;
        }

        // - A union type constituent has the same precedence as the check type of a conditional type
        public Node ParenthesizeConstituentTypeOfUnionType(Node type)
        {
            switch (store.Kind(type))
            {
                case SyntaxKind.UnionType: // Not strictly necessary, but a union containing a union should have been flattened
                case SyntaxKind.IntersectionType: // Not strictly necessary, but makes generated output more readable and avoids breaks in DT tests
                    return factory.CreateParenthesizedType(type);
            }
            return ParenthesizeCheckTypeOfConditionalType(type);
        }

        public NodeList ParenthesizeConstituentTypesOfUnionType(NodeList members)
        {
            return NodeFactory.CreateNodeArray(store, OrSelf(SameMap(members, ListRule.UnionConstituent, hasTrailingComma: false), members));
        }

        // - An intersection type constituent does not allow function, constructor, conditional, or union types (they must be parenthesized)
        public Node ParenthesizeConstituentTypeOfIntersectionType(Node type)
        {
            switch (store.Kind(type))
            {
                case SyntaxKind.UnionType:
                case SyntaxKind.IntersectionType: // Not strictly necessary, but an intersection containing an intersection should have been flattened
                    return factory.CreateParenthesizedType(type);
            }
            return ParenthesizeConstituentTypeOfUnionType(type);
        }

        public NodeList ParenthesizeConstituentTypesOfIntersectionType(NodeList members)
        {
            return NodeFactory.CreateNodeArray(store, OrSelf(SameMap(members, ListRule.IntersectionConstituent, hasTrailingComma: false), members));
        }

        public Node ParenthesizeOperandOfTypeOperator(Node type)
        {
            switch (store.Kind(type))
            {
                case SyntaxKind.IntersectionType:
                    return factory.CreateParenthesizedType(type);
            }
            return ParenthesizeConstituentTypeOfIntersectionType(type);
        }

        public Node ParenthesizeOperandOfReadonlyTypeOperator(Node type)
        {
            switch (store.Kind(type))
            {
                case SyntaxKind.TypeOperator:
                    return factory.CreateParenthesizedType(type);
            }
            return ParenthesizeOperandOfTypeOperator(type);
        }

        public Node ParenthesizeNonArrayTypeOfPostfixType(Node type)
        {
            switch (store.Kind(type))
            {
                case SyntaxKind.InferType:
                case SyntaxKind.TypeOperator:
                case SyntaxKind.TypeQuery: // Not strictly necessary, but makes generated output more readable and avoids breaks in DT tests
                    return factory.CreateParenthesizedType(type);
            }
            return ParenthesizeOperandOfTypeOperator(type);
        }

        public NodeList ParenthesizeElementTypesOfTupleType(NodeList types)
        {
            return NodeFactory.CreateNodeArray(store, OrSelf(SameMap(types, ListRule.TupleElement, hasTrailingComma: false), types));
        }

        public Node ParenthesizeElementTypeOfTupleType(Node type)
        {
            if (HasJSDocPostfixQuestion(type))
            {
                return factory.CreateParenthesizedType(type);
            }
            return type;
        }

        private bool HasJSDocPostfixQuestion(Node type)
        {
            switch (store.Kind(type))
            {
                case SyntaxKind.JSDocNullableType:
                    return store.Postfix(type);
                case SyntaxKind.NamedTupleMember:
                case SyntaxKind.FunctionType:
                case SyntaxKind.ConstructorType:
                case SyntaxKind.TypeOperator:
                    return HasJSDocPostfixQuestion(store.Type(type));
                case SyntaxKind.ConditionalType:
                    return HasJSDocPostfixQuestion(store.FalseType(type));
                case SyntaxKind.UnionType:
                case SyntaxKind.IntersectionType:
                {
                    var types = store.Nodes(store.Types(type));
                    return HasJSDocPostfixQuestion(types[^1]);
                }
                case SyntaxKind.InferType:
                {
                    var constraint = store.Constraint(store.TypeParameter(type));
                    return !constraint.IsNone && HasJSDocPostfixQuestion(constraint);
                }
            }
            return false;
        }

        public Node ParenthesizeTypeOfOptionalType(Node type)
        {
            if (HasJSDocPostfixQuestion(type))
            {
                return factory.CreateParenthesizedType(type);
            }
            return ParenthesizeNonArrayTypeOfPostfixType(type);
        }

        public Node ParenthesizeLeadingTypeArgument(Node node)
        {
            return (store.Kind(node) == SyntaxKind.FunctionType || store.Kind(node) == SyntaxKind.ConstructorType) && !store.TypeParameters(node).IsNone ? factory.CreateParenthesizedType(node) : node;
        }

        public NodeList ParenthesizeTypeArguments(NodeList typeArguments)
        {
            if (!typeArguments.IsNone && store.Length(typeArguments) > 0)
            {
                return NodeFactory.CreateNodeArray(store, OrSelf(SameMap(typeArguments, ListRule.OrdinalTypeArgument, hasTrailingComma: false), typeArguments));
            }
            return default;
        }

        // The functions sameMap applies (Track O: a selector instead of a delegate).
        private enum ListRule
        {
            DisallowedComma,
            UnionConstituent,
            IntersectionConstituent,
            TupleElement,
            OrdinalTypeArgument,
        }

        private Node Apply(ListRule rule, Node node, int index)
        {
            return rule switch
            {
                ListRule.DisallowedComma => ParenthesizeExpressionForDisallowedComma(node),
                ListRule.UnionConstituent => ParenthesizeConstituentTypeOfUnionType(node),
                ListRule.IntersectionConstituent => ParenthesizeConstituentTypeOfIntersectionType(node),
                ListRule.TupleElement => ParenthesizeElementTypeOfTupleType(node),
                // parenthesizeOrdinalTypeArgument
                _ => index == 0 ? ParenthesizeLeadingTypeArgument(node) : node,
            };
        }

        // core.ts sameMap: none when the rule maps every element to itself (the caller keeps the list), otherwise a new
        // list of the mapped elements.
        private NodeList SameMap(NodeList list, ListRule rule, bool hasTrailingComma)
        {
            var length = store.Length(list);
            for (var i = 0; i < length; i++)
            {
                var item = store.Element(list, i);
                var mapped = Apply(rule, item, i);
                if (mapped != item)
                {
                    var builder = store.BeginList();
                    for (var j = 0; j < i; j++)
                    {
                        store.Push(builder, store.Element(list, j));
                    }
                    store.Push(builder, mapped);
                    for (var j = i + 1; j < length; j++)
                    {
                        store.Push(builder, Apply(rule, store.Element(list, j), j));
                    }
                    return NodeFactory.CreateNodeArray(store, builder, hasTrailingComma);
                }
            }
            return NodeList.None;
        }

        // factory.restoreOuterExpressions(outerExpression, innerExpression, OuterExpressionKinds.PartiallyEmittedExpressions)
        // (with only partially emitted expressions as outer kinds, isIgnorableParen never applies)
        private Node RestorePartiallyEmittedExpressions(Node outerExpression, Node innerExpression)
        {
            if (!outerExpression.IsNone && store.Kind(outerExpression) == SyntaxKind.PartiallyEmittedExpression)
            {
                return factory.UpdatePartiallyEmittedExpression(
                    outerExpression,
                    RestorePartiallyEmittedExpressions(store.Expression(outerExpression), innerExpression));
            }
            return innerExpression;
        }

        private static NodeList OrSelf(NodeList mapped, NodeList list) => mapped.IsNone ? list : mapped;
    }
}

/// <summary>factory/nodeConverters.ts: nullNodeConverters and createNodeConverters.</summary>
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
    }

    // core.ts notImplemented: `throw new Error("Not implemented")`.
    private static NotImplementedException NotImplemented() => new("Not implemented");

    /// <summary>createNodeConverters(factory) (module D1).</summary>
    [NoAllocUnchecked("the transformers' factory: created once per emit, not on the parser's path")]
    internal static NodeConverters CreateNodeConverters(NodeFactory factory) => new NodeConvertersObject(factory);

    /// <summary>
    /// The object createNodeConverters returns. Ported: convertToFunctionBlock, convertToFunctionExpression and
    /// convertToClassExpression (module D1); the assignment-pattern converters (module D4).
    /// </summary>
    private sealed class NodeConvertersObject : NodeConverters
    {
        private readonly NodeFactory factory;

        internal NodeConvertersObject(NodeFactory factory)
        {
            this.factory = factory;
        }

        private NodeStore store => factory.store;

        public Node ConvertToFunctionBlock(Node node, bool? multiLine = null)
        {
            if (store.Kind(node) == SyntaxKind.Block)
            {
                return node;
            }
            var returnStatement = factory.CreateReturnStatement(node);
            FactoryUtilitiesPublic.SetTextRange(store, returnStatement, node);
            var body = factory.CreateBlock(NodeFactory.CreateNodeArray(store, [returnStatement]), multiLine);
            FactoryUtilitiesPublic.SetTextRange(store, body, node);
            return body;
        }

        public Node ConvertToFunctionExpression(Node node)
        {
            if (store.Body(node).IsNone)
            {
                Debug.Fail("Cannot convert a FunctionDeclaration without a body");
            }
            var updated = factory.CreateFunctionExpression(
                NodeFactory.GetModifierList(store, node),
                store.AsteriskToken(node),
                store.Name(node),
                store.TypeParameters(node),
                store.Parameters(node),
                store.Type(node),
                store.Body(node));
            NodeFactory.SetOriginalNode(store, updated, node);
            FactoryUtilitiesPublic.SetTextRange(store, updated, node);
            if (FactoryEmitNode.GetStartsOnNewLine(store, node) == true)
            {
                FactoryEmitNode.SetStartsOnNewLine(store, updated, newLine: true);
            }
            return updated;
        }

        public Node ConvertToClassExpression(Node node)
        {
            // node.modifiers?.filter(modifier => !isExportModifier(modifier) && !isDefaultModifier(modifier)): a new array
            var modifiers = store.Modifiers(node);
            var filtered = NodeList.None;
            if (!modifiers.IsNone)
            {
                var kept = new List<Node>();
                foreach (var modifier in store.Nodes(modifiers))
                {
                    if (store.Kind(modifier) is not (SyntaxKind.ExportKeyword or SyntaxKind.DefaultKeyword))
                    {
                        kept.Add(modifier);
                    }
                }
                filtered = factory.CreateNodeArray(kept);
            }
            var updated = factory.CreateClassExpression(
                filtered,
                store.Name(node),
                store.TypeParameters(node),
                store.HeritageClauses(node),
                store.Members(node));
            NodeFactory.SetOriginalNode(store, updated, node);
            FactoryUtilitiesPublic.SetTextRange(store, updated, node);
            if (FactoryEmitNode.GetStartsOnNewLine(store, node) == true)
            {
                FactoryEmitNode.SetStartsOnNewLine(store, updated, newLine: true);
            }
            return updated;
        }

        // (module D4: the assignment-pattern converters)
        public Node ConvertToArrayAssignmentElement(Node element)
        {
            if (store.Kind(element) == SyntaxKind.BindingElement)
            {
                if (!store.DotDotDotToken(element).IsNone)
                {
                    Debug.AssertNode(store.Name(element), n => store.Kind(n) == SyntaxKind.Identifier);
                    return NodeFactory.SetOriginalNode(store, FactoryUtilitiesPublic.SetTextRange(store, factory.CreateSpreadElement(store.Name(element)), element), element);
                }
                var expression = ConvertToAssignmentElementTarget(store.Name(element));
                return !store.Initializer(element).IsNone
                    ? NodeFactory.SetOriginalNode(
                        store,
                        FactoryUtilitiesPublic.SetTextRange(
                            store,
                            factory.CreateAssignment(expression, store.Initializer(element)),
                            element),
                        element)
                    : expression;
            }
            Debug.Assert(UtilitiesPublic.IsExpression(store, element));
            return element;
        }

        public Node ConvertToObjectAssignmentElement(Node element)
        {
            if (store.Kind(element) == SyntaxKind.BindingElement)
            {
                if (!store.DotDotDotToken(element).IsNone)
                {
                    Debug.AssertNode(store.Name(element), n => store.Kind(n) == SyntaxKind.Identifier);
                    return NodeFactory.SetOriginalNode(store, FactoryUtilitiesPublic.SetTextRange(store, factory.CreateSpreadAssignment(store.Name(element)), element), element);
                }
                if (!store.PropertyName(element).IsNone)
                {
                    var expression = ConvertToAssignmentElementTarget(store.Name(element));
                    return NodeFactory.SetOriginalNode(store, FactoryUtilitiesPublic.SetTextRange(store, factory.CreatePropertyAssignment(store.PropertyName(element), !store.Initializer(element).IsNone ? factory.CreateAssignment(expression, store.Initializer(element)) : expression), element), element);
                }
                Debug.AssertNode(store.Name(element), n => store.Kind(n) == SyntaxKind.Identifier);
                return NodeFactory.SetOriginalNode(store, FactoryUtilitiesPublic.SetTextRange(store, factory.CreateShorthandPropertyAssignment(store.Name(element), store.Initializer(element)), element), element);
            }

            Debug.Assert(UtilitiesPublic.IsObjectLiteralElementLike(store, element));
            return element;
        }

        public Node ConvertToAssignmentPattern(Node node)
        {
            switch (store.Kind(node))
            {
                case SyntaxKind.ArrayBindingPattern:
                case SyntaxKind.ArrayLiteralExpression:
                    return ConvertToArrayAssignmentPattern(node);

                case SyntaxKind.ObjectBindingPattern:
                case SyntaxKind.ObjectLiteralExpression:
                    return ConvertToObjectAssignmentPattern(node);
            }
            throw Debug.UnexpectedKind(store.Kind(node), "a binding or assignment pattern");
        }

        public Node ConvertToObjectAssignmentPattern(Node node)
        {
            if (store.Kind(node) == SyntaxKind.ObjectBindingPattern)
            {
                var elements = new List<Node>();
                foreach (var element in store.Nodes(store.Elements(node)))
                {
                    elements.Add(ConvertToObjectAssignmentElement(element));
                }
                return NodeFactory.SetOriginalNode(
                    store,
                    FactoryUtilitiesPublic.SetTextRange(
                        store,
                        factory.CreateObjectLiteralExpression(factory.CreateNodeArray(elements)),
                        node),
                    node);
            }
            Debug.Assert(store.Kind(node) == SyntaxKind.ObjectLiteralExpression);
            return node;
        }

        public Node ConvertToArrayAssignmentPattern(Node node)
        {
            if (store.Kind(node) == SyntaxKind.ArrayBindingPattern)
            {
                var elements = new List<Node>();
                foreach (var element in store.Nodes(store.Elements(node)))
                {
                    elements.Add(ConvertToArrayAssignmentElement(element));
                }
                return NodeFactory.SetOriginalNode(
                    store,
                    FactoryUtilitiesPublic.SetTextRange(
                        store,
                        factory.CreateArrayLiteralExpression(factory.CreateNodeArray(elements)),
                        node),
                    node);
            }
            Debug.Assert(store.Kind(node) == SyntaxKind.ArrayLiteralExpression);
            return node;
        }

        public Node ConvertToAssignmentElementTarget(Node node)
        {
            if (store.Kind(node) is SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern)
            {
                return ConvertToAssignmentPattern(node);
            }

            Debug.Assert(UtilitiesPublic.IsExpression(store, node));
            return node;
        }
    }
}
