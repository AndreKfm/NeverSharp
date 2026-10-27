// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (module C5; the functions are listed below)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (details below).
// Functions (operators: checkPrefixUnaryExpression, checkPostfixUnaryExpression,
// getUnaryResultType, checkArithmeticOperandType, checkReferenceExpression, checkInstanceOfExpression,
// hasEmptyObjectIntersection, checkInExpression, checkObjectLiteralAssignment,
// checkObjectLiteralDestructuringPropertyAssignment, checkArrayLiteralAssignment,
// checkArrayLiteralDestructuringElementAssignment, checkDestructuringAssignment, checkReferenceAssignment, isSideEffectFree,
// isTypeEqualityComparableTo, createCheckBinaryExpression, checkNullishCoalesceOperands, checkNullishCoalesceOperandLeft,
// getSyntacticNullishnessSemantics, checkBinaryLikeExpression, checkBinaryLikeExpressionWorker, getBaseTypesIfUnrelated,
// getBaseTypeOfLiteralTypeForComparison, isExactOptionalPropertyMismatch, checkConditionalExpression,
// checkTestingKnownTruthyCallableOrAwaitableOrEnumMemberType, isSymbolUsedInConditionBody,
// isSymbolUsedInBinaryExpressionChain, checkTruthinessOfType, getSyntacticTruthySemantics, checkTruthinessExpression,
// evaluateEntityNameExpression) and src/compiler/utilities.ts (createEvaluator, isLiteralExpressionOfObject)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (replaces C1's binary operators). The binary expression
// trampoline is a loop over the left spine. Module C9d: JavaScript assignment declarations
// (isAssignmentDeclaration in the = case).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // checker.ts PredicateSemantics
    [Flags]
    private enum PredicateSemantics
    {
        None = 0,
        Always = 1 << 0,
        Never = 1 << 1,
        Sometimes = Always | Never,
    }

    // ---- unary operators ----

    /// <summary>checker.ts checkPrefixUnaryExpression.</summary>
    private Type CheckPrefixUnaryExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var operand = At(node, store.Operand(n));
        var op = store.Operator(n);
        var operandType = CheckExpression(operand);
        if (operandType == silentNeverType)
        {
            return silentNeverType;
        }
        switch (store.Kind(operand.Node))
        {
            case SyntaxKind.NumericLiteral:
                switch (op)
                {
                    case SyntaxKind.MinusToken:
                        return GetFreshTypeOfLiteralType(GetNumberLiteralType(-JsNumber.FromString(store.Texts.GetString(store.Text(operand.Node))!)));
                    case SyntaxKind.PlusToken:
                        return GetFreshTypeOfLiteralType(GetNumberLiteralType(+JsNumber.FromString(store.Texts.GetString(store.Text(operand.Node))!)));
                }
                break;
            case SyntaxKind.BigIntLiteral:
                if (op == SyntaxKind.MinusToken)
                {
                    var base10Value = Utilities.ParsePseudoBigInt(store.Texts.GetString(store.Text(operand.Node))!);
                    return GetFreshTypeOfLiteralType(GetBigIntLiteralType(base10Value != "0" ? "-" + base10Value : base10Value));
                }
                break;
        }
        switch (op)
        {
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.TildeToken:
                CheckNonNullType(operandType, operand);
                if (MaybeTypeOfKindConsideringBaseConstraint(operandType, TypeFlags.ESSymbolLike))
                {
                    Error(operand, Diagnostics.The_0_operator_cannot_be_applied_to_type_symbol, TokenText(op));
                }
                if (op == SyntaxKind.PlusToken)
                {
                    if (MaybeTypeOfKindConsideringBaseConstraint(operandType, TypeFlags.BigIntLike))
                    {
                        Error(operand, Diagnostics.Operator_0_cannot_be_applied_to_type_1, TokenText(op), TypeToString(GetBaseTypeOfLiteralType(operandType)));
                    }
                    return numberType;
                }
                return GetUnaryResultType(operandType);
            case SyntaxKind.ExclamationToken:
            {
                CheckTruthinessOfType(operandType, operand);
                var facts = GetTypeFacts(operandType, TypeFacts.Truthy | TypeFacts.Falsy);
                return facts == TypeFacts.Truthy ? falseType : facts == TypeFacts.Falsy ? trueType : booleanType;
            }
            case SyntaxKind.PlusPlusToken:
            case SyntaxKind.MinusMinusToken:
            {
                var ok = CheckArithmeticOperandType(operand, CheckNonNullType(operandType, operand), Diagnostics.An_arithmetic_operand_must_be_of_type_any_number_bigint_or_an_enum_type);
                if (ok)
                {
                    // run check only if former checks succeeded to avoid reporting cascading errors
                    CheckReferenceExpression(operand,
                        Diagnostics.The_operand_of_an_increment_or_decrement_operator_must_be_a_variable_or_a_property_access,
                        Diagnostics.The_operand_of_an_increment_or_decrement_operator_may_not_be_an_optional_property_access);
                }
                return GetUnaryResultType(operandType);
            }
        }
        return errorType;
    }

    /// <summary>checker.ts checkPostfixUnaryExpression.</summary>
    private Type CheckPostfixUnaryExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var operand = At(node, store.Operand(node.Node));
        var operandType = CheckExpression(operand);
        if (operandType == silentNeverType)
        {
            return silentNeverType;
        }
        var ok = CheckArithmeticOperandType(operand, CheckNonNullType(operandType, operand), Diagnostics.An_arithmetic_operand_must_be_of_type_any_number_bigint_or_an_enum_type);
        if (ok)
        {
            // run check only if former checks succeeded to avoid reporting cascading errors
            CheckReferenceExpression(operand,
                Diagnostics.The_operand_of_an_increment_or_decrement_operator_must_be_a_variable_or_a_property_access,
                Diagnostics.The_operand_of_an_increment_or_decrement_operator_may_not_be_an_optional_property_access);
        }
        return GetUnaryResultType(operandType);
    }

    // checker.ts getUnaryResultType
    private Type GetUnaryResultType(Type operandType)
    {
        if (MaybeTypeOfKind(operandType, TypeFlags.BigIntLike))
        {
            return IsTypeAssignableToKind(operandType, TypeFlags.AnyOrUnknown) || MaybeTypeOfKind(operandType, TypeFlags.NumberLike) ? numberOrBigIntType : bigintType;
        }
        // If it's not a bigint type, implicit coercion will result in a number
        return numberType;
    }

    // checker.ts checkArithmeticOperandType
    private bool CheckArithmeticOperandType(NodeRef operand, Type type, DiagnosticMessage diagnostic, bool isAwaitValid = false)
    {
        if (!IsTypeAssignableTo(type, numberOrBigIntType))
        {
            var awaitedType = isAwaitValid ? GetAwaitedTypeOfPromise(type) : Type.None;
            ErrorAndMaybeSuggestAwait(operand, !awaitedType.IsNone && IsTypeAssignableTo(awaitedType, numberOrBigIntType), diagnostic);
            return false;
        }
        return true;
    }

    /// <summary>checker.ts checkReferenceExpression.</summary>
    private bool CheckReferenceExpression(NodeRef expr, DiagnosticMessage invalidReferenceMessage, DiagnosticMessage invalidOptionalChainMessage)
    {
        // References are combinations of identifiers, parentheses, and property accesses.
        var store = StoreOf(expr);
        var node = SkipOuterExpressions(store, expr.Node, OuterExpressionKinds.Assertions | OuterExpressionKinds.Parentheses);
        if (store.Kind(node) != SyntaxKind.Identifier && !Utilities.IsAccessExpression(store, node))
        {
            Error(expr, invalidReferenceMessage);
            return false;
        }
        if ((store.Flags(node) & NodeFlags.OptionalChain) != 0)
        {
            Error(expr, invalidOptionalChainMessage);
            return false;
        }
        return true;
    }

    // ---- instanceof and in ----

    // checker.ts checkInstanceOfExpression
    private Type CheckInstanceOfExpression(NodeRef left, NodeRef right, Type leftType, Type rightType, CheckMode checkMode)
    {
        if (leftType == silentNeverType || rightType == silentNeverType)
        {
            return silentNeverType;
        }
        // TypeScript 1.0 spec (April 2014): 4.15.4
        // The instanceof operator requires the left operand to be of type Any, an object type, or a type parameter type,
        // and the right operand to be of type Any, a subtype of the 'Function' interface type, or have a call or construct signature.
        // The result is always of the Boolean primitive type.
        // NOTE: do not raise error if leftType is unknown as related error was already reported
        if (!IsTypeAny(leftType) && AllTypesAssignableToKind(leftType, TypeFlags.Primitive))
        {
            Error(left, Diagnostics.The_left_hand_side_of_an_instanceof_expression_must_be_of_type_any_an_object_type_or_a_type_parameter);
        }
        var signature = GetResolvedSignature(Parent(left), null, checkMode);
        if (signature == resolvingSignature)
        {
            // CheckMode.SkipGenericFunctions is enabled and this is a call to a generic function that
            // returns a function type. We defer checking and return silentNeverType.
            return silentNeverType;
        }
        // If rightType has a `[Symbol.hasInstance]` method that is not `(value: unknown) => boolean`, we
        // must check the expression as if it were a call to `right[Symbol.hasInstance](left)`. The call to
        // `getResolvedSignature`, below, will check that leftType is assignable to the type of the first
        // parameter.
        var returnType = GetReturnTypeOfSignature(signature);
        // We also verify that the return type of the `[Symbol.hasInstance]` method is assignable to
        // `boolean`. According to the spec, the runtime will actually perform `ToBoolean` on the result,
        // but this is more type-safe.
        CheckTypeAssignableTo(returnType, booleanType, right, Diagnostics.An_object_s_Symbol_hasInstance_method_must_return_a_boolean_value_for_it_to_be_used_on_the_right_hand_side_of_an_instanceof_expression);
        return booleanType;
    }

    // checker.ts hasEmptyObjectIntersection
    private bool HasEmptyObjectIntersection(Type type) =>
        SomeType(type, t => t == unknownEmptyObjectType || ((Types.Flags(t) & TypeFlags.Intersection) != 0 && IsEmptyAnonymousObjectType(GetBaseConstraintOrType(t))));

    // checker.ts checkInExpression
    private Type CheckInExpression(NodeRef left, NodeRef right, Type leftType, Type rightType)
    {
        if (leftType == silentNeverType || rightType == silentNeverType)
        {
            return silentNeverType;
        }
        var store = StoreOf(left);
        if (store.Kind(left.Node) == SyntaxKind.PrivateIdentifier)
        {
            // Unlike in 'checkPrivateIdentifierExpression' we now have access to the RHS type
            // which provides us with the opportunity to emit more detailed errors
            var resolved = SymbolNodeLinks.Has(left) ? SymbolNodeLinks.Get(left).ResolvedSymbol : SymbolRef.None;
            if (resolved.IsNone && !Utilities.GetContainingClass(store, left.Node).IsNone)
            {
                ReportNonexistentProperty(left, rightType, IsUncheckedJSSuggestion(left, Types.Symbol(rightType), excludeClasses: true));
            }
        }
        else
        {
            // The type of the lef operand must be assignable to string, number, or symbol.
            var nonNullLeft = CheckNonNullType(leftType, left);
            CheckTypeAssignableTo(nonNullLeft, stringNumberSymbolType, left);
        }
        // The type of the right operand must be assignable to 'object'.
        var nonNullRight = CheckNonNullType(rightType, right);
        if (CheckTypeAssignableTo(nonNullRight, nonPrimitiveType, right))
        {
            // The {} type is assignable to the object type, yet {} might represent a primitive type. Here we
            // detect and error on {} that results from narrowing the unknown type, as well as intersections
            // that include {} (we know that the other types in such intersections are assignable to object
            // since we already checked for that).
            if (HasEmptyObjectIntersection(rightType))
            {
                Error(right, Diagnostics.Type_0_may_represent_a_primitive_value_which_is_not_permitted_as_the_right_operand_of_the_in_operator, TypeToString(rightType));
            }
        }
        // The result is always of the Boolean primitive type.
        return booleanType;
    }

    // ---- destructuring assignments ----

    // checker.ts checkObjectLiteralAssignment
    private Type CheckObjectLiteralAssignment(NodeRef node, Type sourceType, bool rightIsThis)
    {
        var store = StoreOf(node);
        var properties = store.Nodes(store.Properties(node.Node));
        if (strictNullChecks && properties.Length == 0)
        {
            return CheckNonNullType(sourceType, node);
        }
        for (var i = 0; i < properties.Length; i++)
        {
            CheckObjectLiteralDestructuringPropertyAssignment(node, sourceType, i, allProperties: true, rightIsThis);
        }
        return sourceType;
    }

    // checker.ts checkObjectLiteralDestructuringPropertyAssignment
    private Type CheckObjectLiteralDestructuringPropertyAssignment(NodeRef node, Type objectLiteralType, int propertyIndex, bool allProperties, bool rightIsThis = false)
    {
        var store = StoreOf(node);
        var properties = store.Nodes(store.Properties(node.Node));
        var property = At(node, properties[propertyIndex]);
        var propertyKind = store.Kind(property.Node);
        if (propertyKind is SyntaxKind.PropertyAssignment or SyntaxKind.ShorthandPropertyAssignment)
        {
            var name = At(node, store.Name(property.Node));
            var exprType = GetLiteralTypeFromPropertyName(name);
            if (IsTypeUsableAsPropertyName(exprType))
            {
                var text = GetPropertyNameFromType(exprType);
                var prop = GetPropertyOfType(objectLiteralType, text);
                if (!prop.IsNone)
                {
                    MarkPropertyAsReferenced(prop, property, rightIsThis);
                    CheckPropertyAccessibility(property, isSuper: false, writing: true, objectLiteralType, prop);
                }
            }
            var elementType = GetIndexedAccessType(objectLiteralType, exprType, AccessFlags.ExpressionPosition | (HasDefaultValue(store, property.Node) ? AccessFlags.AllowMissing : AccessFlags.None), name);
            var type = GetFlowTypeOfDestructuring(property, elementType);
            return CheckDestructuringAssignment(propertyKind == SyntaxKind.ShorthandPropertyAssignment ? property : At(node, store.Initializer(property.Node)), type);
        }
        if (propertyKind == SyntaxKind.SpreadAssignment)
        {
            if (propertyIndex < properties.Length - 1)
            {
                Error(property, Diagnostics.A_rest_element_must_be_last_in_a_destructuring_pattern);
            }
            else
            {
                var nonRestNames = new List<NodeRef>();
                if (allProperties)
                {
                    foreach (var otherProperty in properties)
                    {
                        if (store.Kind(otherProperty) != SyntaxKind.SpreadAssignment)
                        {
                            nonRestNames.Add(At(node, store.Name(otherProperty)));
                        }
                    }
                }
                var type = GetRestType(objectLiteralType, nonRestNames, Types.Symbol(objectLiteralType));
                // (checkGrammarForDisallowedTrailingComma: C8)
                return CheckDestructuringAssignment(At(node, store.Expression(property.Node)), type);
            }
        }
        else
        {
            Error(property, Diagnostics.Property_assignment_expected);
        }
        return Type.None;
    }

    // checker.ts checkArrayLiteralAssignment
    private Type CheckArrayLiteralAssignment(NodeRef node, Type sourceType, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var elements = store.Nodes(store.Elements(node.Node));
        // This elementType will be used if the specific property corresponding to this index is not
        // present (aka the tuple element property). This call also checks that the parentType is in
        // fact an iterable or array (depending on target language).
        var possiblyOutOfBoundsType = CheckIteratedTypeOrElementType(IterationUse.Destructuring | IterationUse.PossiblyOutOfBounds, sourceType, undefinedType, node);
        if (possiblyOutOfBoundsType.IsNone)
        {
            possiblyOutOfBoundsType = errorType;
        }
        var inBoundsType = compilerOptions.NoUncheckedIndexedAccess == true ? Type.None : possiblyOutOfBoundsType;
        for (var i = 0; i < elements.Length; i++)
        {
            var type = possiblyOutOfBoundsType;
            if (store.Kind(elements[i]) == SyntaxKind.SpreadElement)
            {
                if (inBoundsType.IsNone)
                {
                    inBoundsType = CheckIteratedTypeOrElementType(IterationUse.Destructuring, sourceType, undefinedType, node);
                    if (inBoundsType.IsNone)
                    {
                        inBoundsType = errorType;
                    }
                }
                type = inBoundsType;
            }
            CheckArrayLiteralDestructuringElementAssignment(node, sourceType, i, type, checkMode);
        }
        return sourceType;
    }

    // checker.ts checkArrayLiteralDestructuringElementAssignment
    private Type CheckArrayLiteralDestructuringElementAssignment(NodeRef node, Type sourceType, int elementIndex, Type elementType, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var elements = store.Nodes(store.Elements(node.Node));
        var element = At(node, elements[elementIndex]);
        var kind = store.Kind(element.Node);
        if (kind == SyntaxKind.OmittedExpression)
        {
            return Type.None;
        }
        if (kind != SyntaxKind.SpreadElement)
        {
            var indexType = GetNumberLiteralType(elementIndex);
            if (IsArrayLikeType(sourceType))
            {
                // We create a synthetic expression so that getIndexedAccessType doesn't get confused
                // when the element is a SyntaxKind.ElementAccessExpression.
                var accessFlags = AccessFlags.ExpressionPosition | (HasDefaultValue(store, element.Node) ? AccessFlags.AllowMissing : AccessFlags.None);
                var accessNode = kind == SyntaxKind.ElementAccessExpression ? NodeRef.None : element;
                var indexedType = GetIndexedAccessTypeOrUndefined(sourceType, indexType, accessFlags, accessNode);
                if (indexedType.IsNone)
                {
                    indexedType = errorType;
                }
                var assignedType = HasDefaultValue(store, element.Node) ? GetTypeWithFacts(indexedType, TypeFacts.NEUndefined) : indexedType;
                var type = GetFlowTypeOfDestructuring(element, assignedType);
                return CheckDestructuringAssignment(element, type, checkMode);
            }
            return CheckDestructuringAssignment(element, elementType, checkMode);
        }
        if (elementIndex < elements.Length - 1)
        {
            Error(element, Diagnostics.A_rest_element_must_be_last_in_a_destructuring_pattern);
        }
        else
        {
            var restExpression = store.Expression(element.Node);
            if (store.Kind(restExpression) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(restExpression)) == SyntaxKind.EqualsToken)
            {
                Error(At(node, store.OperatorToken(restExpression)), Diagnostics.A_rest_element_cannot_have_an_initializer);
            }
            else
            {
                // (checkGrammarForDisallowedTrailingComma: C8)
                var type = EveryType(sourceType, IsTupleType)
                    ? MapType(sourceType, t => SliceTupleType(t, elementIndex))
                    : CreateArrayType(elementType);
                return CheckDestructuringAssignment(At(node, restExpression), type, checkMode);
            }
        }
        return Type.None;
    }

    // checker.ts checkDestructuringAssignment
    private Type CheckDestructuringAssignment(NodeRef exprOrAssignment, Type sourceType, CheckMode checkMode = CheckMode.Normal, bool rightIsThis = false)
    {
        var store = StoreOf(exprOrAssignment);
        NodeRef target;
        if (store.Kind(exprOrAssignment.Node) == SyntaxKind.ShorthandPropertyAssignment)
        {
            var objectAssignmentInitializer = store.ObjectAssignmentInitializer(exprOrAssignment.Node);
            if (!objectAssignmentInitializer.IsNone)
            {
                // In strict null checking mode, if a default value of a non-undefined type is specified, remove
                // undefined from the final type.
                if (strictNullChecks && !HasTypeFacts(CheckExpression(At(exprOrAssignment, objectAssignmentInitializer)), TypeFacts.IsUndefined))
                {
                    sourceType = GetTypeWithFacts(sourceType, TypeFacts.NEUndefined);
                }
                CheckBinaryLikeExpression(At(exprOrAssignment, store.Name(exprOrAssignment.Node)), At(exprOrAssignment, store.EqualsToken(exprOrAssignment.Node)), SyntaxKind.EqualsToken,
                    At(exprOrAssignment, objectAssignmentInitializer), checkMode, NodeRef.None);
            }
            target = At(exprOrAssignment, store.Name(exprOrAssignment.Node));
        }
        else
        {
            target = exprOrAssignment;
        }
        if (store.Kind(target.Node) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(target.Node)) == SyntaxKind.EqualsToken)
        {
            CheckBinaryExpression(target, checkMode);
            target = At(target, store.Left(target.Node));
            // A default value is specified, so remove undefined from the final type.
            if (strictNullChecks)
            {
                sourceType = GetTypeWithFacts(sourceType, TypeFacts.NEUndefined);
            }
        }
        if (store.Kind(target.Node) == SyntaxKind.ObjectLiteralExpression)
        {
            return CheckObjectLiteralAssignment(target, sourceType, rightIsThis);
        }
        if (store.Kind(target.Node) == SyntaxKind.ArrayLiteralExpression)
        {
            return CheckArrayLiteralAssignment(target, sourceType, checkMode);
        }
        return CheckReferenceAssignment(target, sourceType, checkMode);
    }

    // checker.ts checkReferenceAssignment
    private Type CheckReferenceAssignment(NodeRef target, Type sourceType, CheckMode checkMode)
    {
        var targetType = CheckExpression(target, checkMode);
        var isRest = Kind(Parent(target)) == SyntaxKind.SpreadAssignment;
        var error = isRest
            ? Diagnostics.The_target_of_an_object_rest_assignment_must_be_a_variable_or_a_property_access
            : Diagnostics.The_left_hand_side_of_an_assignment_expression_must_be_a_variable_or_a_property_access;
        var optionalError = isRest
            ? Diagnostics.The_target_of_an_object_rest_assignment_may_not_be_an_optional_property_access
            : Diagnostics.The_left_hand_side_of_an_assignment_expression_may_not_be_an_optional_property_access;
        if (CheckReferenceExpression(target, error, optionalError))
        {
            CheckTypeAssignableToAndOptionallyElaborate(sourceType, targetType, target, target);
        }
        return sourceType;
    }

    // checker.ts isSideEffectFree
    private static bool IsSideEffectFree(NodeStore store, Node node)
    {
        node = Utilities.SkipParentheses(store, node, false);
        switch (store.Kind(node))
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.RegularExpressionLiteral:
            case SyntaxKind.TaggedTemplateExpression:
            case SyntaxKind.TemplateExpression:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.UndefinedKeyword:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.TypeOfExpression:
            case SyntaxKind.NonNullExpression:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxElement:
                return true;
            case SyntaxKind.ConditionalExpression:
                return IsSideEffectFree(store, store.WhenTrue(node)) && IsSideEffectFree(store, store.WhenFalse(node));
            case SyntaxKind.BinaryExpression:
                if (Utilities.IsAssignmentOperator(store.Kind(store.OperatorToken(node))))
                {
                    return false;
                }
                return IsSideEffectFree(store, store.Left(node)) && IsSideEffectFree(store, store.Right(node));
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
                // Unary operators ~, !, +, and - have no side effects.
                // The rest do.
                return store.Operator(node) is SyntaxKind.ExclamationToken or SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.TildeToken;
            default:
                return false;
        }
    }

    // checker.ts isTypeEqualityComparableTo
    private bool IsTypeEqualityComparableTo(Type source, Type target) =>
        (Types.Flags(target) & TypeFlags.Nullable) != 0 || IsTypeComparableTo(source, target);

    // ---- binary expressions ----

    /// <summary>checker.ts checkBinaryExpression (createCheckBinaryExpression's trampoline as a loop over the left spine).</summary>
    private Type CheckBinaryExpression(NodeRef node, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var spine = new List<Node>();
        var current = node.Node;
        Type lastResult;
        while (true)
        {
            // onEnter
            CheckNullishCoalesceOperands(At(node, current));
            var op = store.Kind(store.OperatorToken(current));
            var currentLeft = store.Left(current);
            if (op == SyntaxKind.EqualsToken && store.Kind(currentLeft) is SyntaxKind.ObjectLiteralExpression or SyntaxKind.ArrayLiteralExpression)
            {
                var currentRight = store.Right(current);
                lastResult = CheckDestructuringAssignment(At(node, currentLeft), CheckExpression(At(node, currentRight), checkMode), checkMode, store.Kind(currentRight) == SyntaxKind.ThisKeyword);
                break;
            }
            spine.Add(current);
            // onLeft
            if (store.Kind(currentLeft) == SyntaxKind.BinaryExpression)
            {
                current = currentLeft;
                continue;
            }
            lastResult = CheckExpression(At(node, currentLeft), checkMode);
            break;
        }
        for (var i = spine.Count - 1; i >= 0; i--)
        {
            var b = At(node, spine[i]);
            var leftType = lastResult;
            // onOperator
            CheckBinaryOperatorLeft(b, leftType);
            // onRight
            var right = At(node, store.Right(b.Node));
            var rightType = store.Kind(right.Node) == SyntaxKind.BinaryExpression ? CheckBinaryExpression(right, checkMode) : CheckExpression(right, checkMode);
            // onExit
            lastResult = CheckBinaryLikeExpressionWorker(At(node, store.Left(b.Node)), At(node, store.OperatorToken(b.Node)), right, leftType, rightType, checkMode, b);
        }
        return lastResult;
    }

    // the onOperator step of createCheckBinaryExpression
    private void CheckBinaryOperatorLeft(NodeRef node, Type leftType)
    {
        var store = StoreOf(node);
        var op = store.Kind(store.OperatorToken(node.Node));
        if (op is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.BarBarToken or SyntaxKind.QuestionQuestionToken)
        {
            var parent = store.Parent(node.Node);
            while (store.Kind(parent) == SyntaxKind.ParenthesizedExpression || IsLogicalOrCoalescingBinaryExpression(store, parent))
            {
                parent = store.Parent(parent);
            }
            var left = At(node, store.Left(node.Node));
            if (op == SyntaxKind.AmpersandAmpersandToken || store.Kind(parent) == SyntaxKind.IfStatement)
            {
                CheckTestingKnownTruthyCallableOrAwaitableOrEnumMemberType(left, leftType, store.Kind(parent) == SyntaxKind.IfStatement ? At(node, store.ThenStatement(parent)) : NodeRef.None);
            }
            if (IsBinaryLogicalOperator(op))
            {
                CheckTruthinessOfType(leftType, left);
            }
        }
    }

    // utilities.ts isLogicalOrCoalescingBinaryExpression
    private static bool IsLogicalOrCoalescingBinaryExpression(NodeStore store, Node node) =>
        store.Kind(node) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(node)) is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.BarBarToken or SyntaxKind.QuestionQuestionToken;

    // checker.ts checkNullishCoalesceOperands
    private void CheckNullishCoalesceOperands(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (store.Kind(store.OperatorToken(n)) != SyntaxKind.QuestionQuestionToken)
        {
            return;
        }
        var parent = store.Parent(n);
        if (store.Kind(parent) == SyntaxKind.BinaryExpression)
        {
            var left = store.Left(parent);
            var operatorKind = store.Kind(store.OperatorToken(parent));
            if (store.Kind(left) == SyntaxKind.BinaryExpression && operatorKind == SyntaxKind.BarBarToken)
            {
                GrammarErrorOnNode(At(node, left), Diagnostics._0_and_1_operations_cannot_be_mixed_without_parentheses, TokenText(SyntaxKind.QuestionQuestionToken), TokenText(operatorKind));
            }
        }
        else if (store.Kind(store.Left(n)) == SyntaxKind.BinaryExpression)
        {
            var operatorKind = store.Kind(store.OperatorToken(store.Left(n)));
            if (operatorKind is SyntaxKind.BarBarToken or SyntaxKind.AmpersandAmpersandToken)
            {
                GrammarErrorOnNode(At(node, store.Left(n)), Diagnostics._0_and_1_operations_cannot_be_mixed_without_parentheses, TokenText(operatorKind), TokenText(SyntaxKind.QuestionQuestionToken));
            }
        }
        else if (store.Kind(store.Right(n)) == SyntaxKind.BinaryExpression)
        {
            var operatorKind = store.Kind(store.OperatorToken(store.Right(n)));
            if (operatorKind == SyntaxKind.AmpersandAmpersandToken)
            {
                GrammarErrorOnNode(At(node, store.Right(n)), Diagnostics._0_and_1_operations_cannot_be_mixed_without_parentheses, TokenText(SyntaxKind.QuestionQuestionToken), TokenText(operatorKind));
            }
        }
        CheckNullishCoalesceOperandLeft(node);
    }

    // checker.ts checkNullishCoalesceOperandLeft
    private void CheckNullishCoalesceOperandLeft(NodeRef node)
    {
        var store = StoreOf(node);
        var leftTarget = At(node, SkipOuterExpressions(store, store.Left(node.Node), OuterExpressionKinds.All));
        var nullishSemantics = GetSyntacticNullishnessSemantics(leftTarget);
        if (nullishSemantics != PredicateSemantics.Sometimes)
        {
            Error(leftTarget, nullishSemantics == PredicateSemantics.Always
                ? Diagnostics.This_expression_is_always_nullish
                : Diagnostics.Right_operand_of_is_unreachable_because_the_left_operand_is_never_nullish);
        }
    }

    // checker.ts getSyntacticNullishnessSemantics
    private PredicateSemantics GetSyntacticNullishnessSemantics(NodeRef nodeRef)
    {
        var store = StoreOf(nodeRef);
        var node = SkipOuterExpressions(store, nodeRef.Node, OuterExpressionKinds.All);
        switch (store.Kind(node))
        {
            case SyntaxKind.AwaitExpression:
            case SyntaxKind.CallExpression:
            case SyntaxKind.TaggedTemplateExpression:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.MetaProperty:
            case SyntaxKind.NewExpression:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.YieldExpression:
            case SyntaxKind.ThisKeyword:
                return PredicateSemantics.Sometimes;
            case SyntaxKind.BinaryExpression:
                // List of operators that can produce null/undefined:
                // = ??= ?? || ||= && &&=
                switch (store.Kind(store.OperatorToken(node)))
                {
                    case SyntaxKind.BarBarToken:
                    case SyntaxKind.BarBarEqualsToken:
                    case SyntaxKind.AmpersandAmpersandToken:
                    case SyntaxKind.AmpersandAmpersandEqualsToken:
                        return PredicateSemantics.Sometimes;
                    // For these operator kinds, the right operand is effectively controlling
                    case SyntaxKind.CommaToken:
                    case SyntaxKind.EqualsToken:
                    case SyntaxKind.QuestionQuestionToken:
                    case SyntaxKind.QuestionQuestionEqualsToken:
                        return GetSyntacticNullishnessSemantics(At(nodeRef, store.Right(node)));
                }
                return PredicateSemantics.Never;
            case SyntaxKind.ConditionalExpression:
                return GetSyntacticNullishnessSemantics(At(nodeRef, store.WhenTrue(node))) | GetSyntacticNullishnessSemantics(At(nodeRef, store.WhenFalse(node)));
            case SyntaxKind.NullKeyword:
                return PredicateSemantics.Always;
            case SyntaxKind.Identifier:
                if (GetResolvedSymbol(At(nodeRef, node)) == undefinedSymbol)
                {
                    return PredicateSemantics.Always;
                }
                return PredicateSemantics.Sometimes;
        }
        return PredicateSemantics.Never;
    }

    // checker.ts checkBinaryLikeExpression
    private Type CheckBinaryLikeExpression(NodeRef left, NodeRef operatorToken, SyntaxKind op, NodeRef right, CheckMode checkMode, NodeRef errorNode)
    {
        var store = StoreOf(left);
        if (op == SyntaxKind.EqualsToken && store.Kind(left.Node) is SyntaxKind.ObjectLiteralExpression or SyntaxKind.ArrayLiteralExpression)
        {
            return CheckDestructuringAssignment(left, CheckExpression(right, checkMode), checkMode, Kind(right) == SyntaxKind.ThisKeyword);
        }
        var leftType = IsBinaryLogicalOperator(op) ? CheckTruthinessExpression(left, checkMode) : CheckExpression(left, checkMode);
        var rightType = CheckExpression(right, checkMode);
        return CheckBinaryLikeExpressionWorker(left, operatorToken, right, leftType, rightType, checkMode, errorNode, op);
    }

    // checker.ts checkBinaryLikeExpressionWorker
    private Type CheckBinaryLikeExpressionWorker(NodeRef left, NodeRef operatorToken, NodeRef right, Type leftType, Type rightType, CheckMode checkMode, NodeRef errorNode, SyntaxKind op = SyntaxKind.Unknown)
    {
        var store = StoreOf(left);
        var @operator = op != SyntaxKind.Unknown ? op : store.Kind(operatorToken.Node);
        var errNodeOrToken = errorNode.IsNone ? operatorToken : errorNode;
        switch (@operator)
        {
            case SyntaxKind.AsteriskToken:
            case SyntaxKind.AsteriskAsteriskToken:
            case SyntaxKind.AsteriskEqualsToken:
            case SyntaxKind.AsteriskAsteriskEqualsToken:
            case SyntaxKind.SlashToken:
            case SyntaxKind.SlashEqualsToken:
            case SyntaxKind.PercentToken:
            case SyntaxKind.PercentEqualsToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.MinusEqualsToken:
            case SyntaxKind.LessThanLessThanToken:
            case SyntaxKind.LessThanLessThanEqualsToken:
            case SyntaxKind.GreaterThanGreaterThanToken:
            case SyntaxKind.GreaterThanGreaterThanEqualsToken:
            case SyntaxKind.GreaterThanGreaterThanGreaterThanToken:
            case SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken:
            case SyntaxKind.BarToken:
            case SyntaxKind.BarEqualsToken:
            case SyntaxKind.CaretToken:
            case SyntaxKind.CaretEqualsToken:
            case SyntaxKind.AmpersandToken:
            case SyntaxKind.AmpersandEqualsToken:
            {
                if (leftType == silentNeverType || rightType == silentNeverType)
                {
                    return silentNeverType;
                }
                leftType = CheckNonNullType(leftType, left);
                rightType = CheckNonNullType(rightType, right);
                // if a user tries to apply a bitwise operator to 2 boolean operands
                // try and return them a helpful suggestion
                var suggestedOperator = GetSuggestedBooleanOperator(@operator);
                if ((Types.Flags(leftType) & TypeFlags.BooleanLike) != 0 && (Types.Flags(rightType) & TypeFlags.BooleanLike) != 0 && suggestedOperator != SyntaxKind.Unknown)
                {
                    Error(errNodeOrToken, Diagnostics.The_0_operator_is_not_allowed_for_boolean_types_Consider_using_1_instead, TokenText(@operator), TokenText(suggestedOperator));
                    return numberType;
                }
                // otherwise just check each operand separately and report errors as normal
                var leftOk = CheckArithmeticOperandType(left, leftType, Diagnostics.The_left_hand_side_of_an_arithmetic_operation_must_be_of_type_any_number_bigint_or_an_enum_type, isAwaitValid: true);
                var rightOk = CheckArithmeticOperandType(right, rightType, Diagnostics.The_right_hand_side_of_an_arithmetic_operation_must_be_of_type_any_number_bigint_or_an_enum_type, isAwaitValid: true);
                Type resultType;
                // If both are any or unknown, allow operation; assume it will resolve to number
                if ((IsTypeAssignableToKind(leftType, TypeFlags.AnyOrUnknown) && IsTypeAssignableToKind(rightType, TypeFlags.AnyOrUnknown)) ||
                    // Or, if neither could be bigint, implicit coercion results in a number result
                    !(MaybeTypeOfKind(leftType, TypeFlags.BigIntLike) || MaybeTypeOfKind(rightType, TypeFlags.BigIntLike)))
                {
                    resultType = numberType;
                }
                // At least one is assignable to bigint, so check that both are
                else if (BothAreBigIntLike(leftType, rightType))
                {
                    switch (@operator)
                    {
                        case SyntaxKind.GreaterThanGreaterThanGreaterThanToken:
                        case SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken:
                            ReportOperatorError();
                            break;
                        case SyntaxKind.AsteriskAsteriskToken:
                        case SyntaxKind.AsteriskAsteriskEqualsToken:
                            if (languageVersion < ScriptTarget.ES2016)
                            {
                                Error(errorNode, Diagnostics.Exponentiation_cannot_be_performed_on_bigint_values_unless_the_target_option_is_set_to_es2016_or_later);
                            }
                            break;
                    }
                    resultType = bigintType;
                }
                // Exactly one of leftType/rightType is assignable to bigint
                else
                {
                    ReportOperatorError(BothAreBigIntLike);
                    resultType = errorType;
                }
                if (leftOk && rightOk)
                {
                    CheckAssignmentOperator(resultType);
                    switch (@operator)
                    {
                        case SyntaxKind.LessThanLessThanToken:
                        case SyntaxKind.LessThanLessThanEqualsToken:
                        case SyntaxKind.GreaterThanGreaterThanToken:
                        case SyntaxKind.GreaterThanGreaterThanEqualsToken:
                        case SyntaxKind.GreaterThanGreaterThanGreaterThanToken:
                        case SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken:
                            var rhsEval = EvaluateConstantExpression(right);
                            if (rhsEval is double d && Math.Abs(d) >= 32)
                            {
                                var enumMemberCandidate = WalkUpParenthesizedExpressions(store, store.Parent(store.Parent(right.Node)));
                                ErrorOrSuggestion(store.Kind(enumMemberCandidate) == SyntaxKind.EnumMember, errNodeOrToken,
                                    Diagnostics.This_operation_can_be_simplified_This_shift_is_identical_to_0_1_2, GetTextOfNode(left), TokenText(@operator), JsNumber.ToString(d % 32));
                            }
                            break;
                    }
                }
                return resultType;
            }
            case SyntaxKind.PlusToken:
            case SyntaxKind.PlusEqualsToken:
            {
                if (leftType == silentNeverType || rightType == silentNeverType)
                {
                    return silentNeverType;
                }
                if (!IsTypeAssignableToKind(leftType, TypeFlags.StringLike) && !IsTypeAssignableToKind(rightType, TypeFlags.StringLike))
                {
                    leftType = CheckNonNullType(leftType, left);
                    rightType = CheckNonNullType(rightType, right);
                }
                var resultType = Type.None;
                if (IsTypeAssignableToKind(leftType, TypeFlags.NumberLike, strict: true) && IsTypeAssignableToKind(rightType, TypeFlags.NumberLike, strict: true))
                {
                    // Operands of an enum type are treated as having the primitive type Number.
                    // If both operands are of the Number primitive type, the result is of the Number primitive type.
                    resultType = numberType;
                }
                else if (IsTypeAssignableToKind(leftType, TypeFlags.BigIntLike, strict: true) && IsTypeAssignableToKind(rightType, TypeFlags.BigIntLike, strict: true))
                {
                    // If both operands are of the BigInt primitive type, the result is of the BigInt primitive type.
                    resultType = bigintType;
                }
                else if (IsTypeAssignableToKind(leftType, TypeFlags.StringLike, strict: true) || IsTypeAssignableToKind(rightType, TypeFlags.StringLike, strict: true))
                {
                    // If one or both operands are of the String primitive type, the result is of the String primitive type.
                    resultType = stringType;
                }
                else if (IsTypeAny(leftType) || IsTypeAny(rightType))
                {
                    // Otherwise, the result is of type Any.
                    // NOTE: unknown type here denotes error type. Old compiler treated this case as any type so do we.
                    resultType = IsErrorType(leftType) || IsErrorType(rightType) ? errorType : anyType;
                }
                // Symbols are not allowed at all in arithmetic expressions
                if (!resultType.IsNone && !CheckForDisallowedESSymbolOperand(@operator))
                {
                    return resultType;
                }
                if (resultType.IsNone)
                {
                    // Types that have a reasonably good chance of being a valid operand type.
                    // If both types have an awaited type of one of these, we'll assume the user
                    // might be missing an await without doing an exhaustive check that inserting
                    // await(s) will actually be a completely valid binary expression.
                    const TypeFlags closeEnoughKind = TypeFlags.NumberLike | TypeFlags.BigIntLike | TypeFlags.StringLike | TypeFlags.AnyOrUnknown;
                    ReportOperatorError((l, r) => IsTypeAssignableToKind(l, closeEnoughKind) && IsTypeAssignableToKind(r, closeEnoughKind));
                    return anyType;
                }
                if (@operator == SyntaxKind.PlusEqualsToken)
                {
                    CheckAssignmentOperator(resultType);
                }
                return resultType;
            }
            case SyntaxKind.LessThanToken:
            case SyntaxKind.GreaterThanToken:
            case SyntaxKind.LessThanEqualsToken:
            case SyntaxKind.GreaterThanEqualsToken:
                if (CheckForDisallowedESSymbolOperand(@operator))
                {
                    leftType = GetBaseTypeOfLiteralTypeForComparison(CheckNonNullType(leftType, left));
                    rightType = GetBaseTypeOfLiteralTypeForComparison(CheckNonNullType(rightType, right));
                    ReportOperatorErrorUnless((l, r) =>
                    {
                        if (IsTypeAny(l) || IsTypeAny(r))
                        {
                            return true;
                        }
                        var leftAssignableToNumber = IsTypeAssignableTo(l, numberOrBigIntType);
                        var rightAssignableToNumber = IsTypeAssignableTo(r, numberOrBigIntType);
                        return (leftAssignableToNumber && rightAssignableToNumber) ||
                            (!leftAssignableToNumber && !rightAssignableToNumber && AreTypesComparable(l, r));
                    });
                }
                return booleanType;
            case SyntaxKind.EqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsToken:
            case SyntaxKind.EqualsEqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsEqualsToken:
                // We suppress errors in CheckMode.TypeOnly (meaning the invocation came from getTypeOfExpression). During
                // control flow analysis it is possible for operands to temporarily have narrower types, and those narrower
                // types may cause the operands to not be comparable. We don't want such errors reported (see #46475).
                if ((checkMode & CheckMode.TypeOnly) == 0)
                {
                    if ((IsLiteralExpressionOfObject(store, left.Node) || IsLiteralExpressionOfObject(store, right.Node)) &&
                        // only report for === and !== in JS, not == or !=
                        (!Utilities.IsInJSFile(store, left.Node) || @operator is SyntaxKind.EqualsEqualsEqualsToken or SyntaxKind.ExclamationEqualsEqualsToken))
                    {
                        var eqType = @operator is SyntaxKind.EqualsEqualsToken or SyntaxKind.EqualsEqualsEqualsToken;
                        Error(errorNode, Diagnostics.This_condition_will_always_return_0_since_JavaScript_compares_objects_by_reference_not_value, eqType ? "false" : "true");
                    }
                    CheckNaNEquality(errorNode, @operator, left, right);
                    ReportOperatorErrorUnless((l, r) => IsTypeEqualityComparableTo(l, r) || IsTypeEqualityComparableTo(r, l));
                }
                return booleanType;
            case SyntaxKind.InstanceOfKeyword:
                return CheckInstanceOfExpression(left, right, leftType, rightType, checkMode);
            case SyntaxKind.InKeyword:
                return CheckInExpression(left, right, leftType, rightType);
            case SyntaxKind.AmpersandAmpersandToken:
            case SyntaxKind.AmpersandAmpersandEqualsToken:
            {
                var resultType = HasTypeFacts(leftType, TypeFacts.Truthy)
                    ? GetUnionType([ExtractDefinitelyFalsyTypes(strictNullChecks ? leftType : GetBaseTypeOfLiteralType(rightType)), rightType])
                    : leftType;
                if (@operator == SyntaxKind.AmpersandAmpersandEqualsToken)
                {
                    CheckAssignmentOperator(rightType);
                }
                return resultType;
            }
            case SyntaxKind.BarBarToken:
            case SyntaxKind.BarBarEqualsToken:
            {
                var resultType = HasTypeFacts(leftType, TypeFacts.Falsy)
                    ? GetUnionType([GetNonNullableType(RemoveDefinitelyFalsyTypes(leftType)), rightType], UnionReduction.Subtype)
                    : leftType;
                if (@operator == SyntaxKind.BarBarEqualsToken)
                {
                    CheckAssignmentOperator(rightType);
                }
                return resultType;
            }
            case SyntaxKind.QuestionQuestionToken:
            case SyntaxKind.QuestionQuestionEqualsToken:
            {
                var resultType = HasTypeFacts(leftType, TypeFacts.EQUndefinedOrNull)
                    ? GetUnionType([GetNonNullableType(leftType), rightType], UnionReduction.Subtype)
                    : leftType;
                if (@operator == SyntaxKind.QuestionQuestionEqualsToken)
                {
                    CheckAssignmentOperator(rightType);
                }
                return resultType;
            }
            case SyntaxKind.EqualsToken:
            {
                var leftParent = store.Parent(left.Node);
                var declKind = store.Kind(leftParent) == SyntaxKind.BinaryExpression ? Utilities.GetAssignmentDeclarationKind(store, leftParent) : AssignmentDeclarationKind.None;
                // (checkAssignmentDeclaration: duplicate typedef/class exports across declarations, not reached in Gate C)
                if (IsAssignmentDeclarationKind(declKind))
                {
                    if ((Types.Flags(rightType) & TypeFlags.Object) == 0 ||
                        (declKind != AssignmentDeclarationKind.ModuleExports && declKind != AssignmentDeclarationKind.Prototype &&
                            !IsEmptyObjectType(rightType) && !IsFunctionObjectType(rightType) && (Types.ObjectFlagsOf(rightType) & ObjectFlags.Class) == 0))
                    {
                        // don't check assignability of module.exports=, C.prototype=, or expando types because they will necessarily be incomplete
                        CheckAssignmentOperator(rightType);
                    }
                    return leftType;
                }
                CheckAssignmentOperator(rightType);
                return rightType;
            }
            case SyntaxKind.CommaToken:
                if (compilerOptions.AllowUnreachableCode != true && IsSideEffectFree(store, left.Node) && !IsIndirectCall(store, store.Parent(left.Node)))
                {
                    var file = FileAt(left.File);
                    var start = Scanner.SkipTrivia(store.SourceFileText(Utilities.RootOf(store, left.Node)).Chars, store.Pos(left.Node));
                    var isInDiag2657 = false;
                    foreach (var diag in file.ParseDiagnostics)
                    {
                        if (diag.Code == Diagnostics.JSX_expressions_must_have_one_parent_element.Code && diag.Start <= start && start < diag.Start + diag.Length)
                        {
                            isInDiag2657 = true;
                            break;
                        }
                    }
                    if (!isInDiag2657)
                    {
                        Error(left, Diagnostics.Left_side_of_comma_operator_is_unused_and_has_no_side_effects);
                    }
                }
                return rightType;
            default:
                return errorType;
        }

        bool IsAssignmentDeclarationKind(AssignmentDeclarationKind kind)
        {
            switch (kind)
            {
                case AssignmentDeclarationKind.ModuleExports:
                    return true;
                case AssignmentDeclarationKind.ExportsProperty:
                case AssignmentDeclarationKind.Property:
                case AssignmentDeclarationKind.Prototype:
                case AssignmentDeclarationKind.PrototypeProperty:
                case AssignmentDeclarationKind.ThisProperty:
                {
                    var symbol = GetSymbolOfNode(left);
                    var init = Utilities.GetAssignedExpandoInitializer(store, right.Node);
                    return !init.IsNone && store.Kind(init) == SyntaxKind.ObjectLiteralExpression && !symbol.IsNone && ExportsOf(symbol) is { Count: > 0 };
                }
                default:
                    return false;
            }
        }

        bool BothAreBigIntLike(Type l, Type r) => IsTypeAssignableToKind(l, TypeFlags.BigIntLike) && IsTypeAssignableToKind(r, TypeFlags.BigIntLike);

        // Return true if there was no error, false if there was an error.
        bool CheckForDisallowedESSymbolOperand(SyntaxKind operatorKind)
        {
            var offendingSymbolOperand = MaybeTypeOfKindConsideringBaseConstraint(leftType, TypeFlags.ESSymbolLike) ? left
                : MaybeTypeOfKindConsideringBaseConstraint(rightType, TypeFlags.ESSymbolLike) ? right
                : NodeRef.None;
            if (!offendingSymbolOperand.IsNone)
            {
                Error(offendingSymbolOperand, Diagnostics.The_0_operator_cannot_be_applied_to_type_symbol, TokenText(operatorKind));
                return false;
            }
            return true;
        }

        void CheckAssignmentOperator(Type valueType)
        {
            if (!Utilities.IsAssignmentOperator(@operator))
            {
                return;
            }
            var assigneeType = leftType;
            // getters can be a subtype of setters, so to check for assignability we use the setter's type instead
            if (IsCompoundAssignment(store.Kind(operatorToken.Node)) && store.Kind(left.Node) == SyntaxKind.PropertyAccessExpression)
            {
                assigneeType = CheckPropertyAccessExpression(left, CheckMode.Normal, writeOnly: true);
            }
            // TypeScript 1.0 spec (April 2014): 4.17
            // An assignment of the form
            //    VarExpr = ValueExpr
            // requires VarExpr to be classified as a reference
            // A compound assignment furthermore requires VarExpr to be classified as a reference (section 4.1)
            // and the type of the non-compound operation to be assignable to the type of VarExpr.
            if (CheckReferenceExpression(left, Diagnostics.The_left_hand_side_of_an_assignment_expression_must_be_a_variable_or_a_property_access,
                Diagnostics.The_left_hand_side_of_an_assignment_expression_may_not_be_an_optional_property_access))
            {
                DiagnosticMessage? headMessage = null;
                if (compilerOptions.ExactOptionalPropertyTypes == true && store.Kind(left.Node) == SyntaxKind.PropertyAccessExpression && MaybeTypeOfKind(valueType, TypeFlags.Undefined))
                {
                    var target = GetTypeOfPropertyOfType(GetTypeOfExpression(At(left, store.Expression(left.Node))), store.Texts.GetString(store.EscapedText(store.Name(left.Node)))!);
                    if (IsExactOptionalPropertyMismatch(valueType, target))
                    {
                        headMessage = Diagnostics.Type_0_is_not_assignable_to_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_type_of_the_target;
                    }
                }
                // to avoid cascading errors check assignability only if 'isReference' check succeeded and no errors were reported
                CheckTypeAssignableToAndOptionallyElaborate(valueType, assigneeType, left, right, headMessage);
            }
        }

        // Returns true if an error is reported
        bool ReportOperatorErrorUnless(Func<Type, Type, bool> typesAreCompatible)
        {
            if (!typesAreCompatible(leftType, rightType))
            {
                ReportOperatorError(typesAreCompatible);
                return true;
            }
            return false;
        }

        void ReportOperatorError(Func<Type, Type, bool>? isRelated = null)
        {
            var wouldWorkWithAwait = false;
            var errNode = errNodeOrToken;
            if (isRelated is not null)
            {
                var awaitedLeftType = GetAwaitedTypeNoAlias(leftType);
                var awaitedRightType = GetAwaitedTypeNoAlias(rightType);
                wouldWorkWithAwait = !(awaitedLeftType == leftType && awaitedRightType == rightType)
                    && !awaitedLeftType.IsNone && !awaitedRightType.IsNone
                    && isRelated(awaitedLeftType, awaitedRightType);
            }
            var effectiveLeft = leftType;
            var effectiveRight = rightType;
            if (!wouldWorkWithAwait && isRelated is not null)
            {
                (effectiveLeft, effectiveRight) = GetBaseTypesIfUnrelated(leftType, rightType, isRelated);
            }
            var (leftStr, rightStr) = GetTypeNamesForErrorDisplay(effectiveLeft, effectiveRight);
            switch (store.Kind(operatorToken.Node))
            {
                case SyntaxKind.EqualsEqualsEqualsToken:
                case SyntaxKind.EqualsEqualsToken:
                case SyntaxKind.ExclamationEqualsEqualsToken:
                case SyntaxKind.ExclamationEqualsToken:
                    ErrorAndMaybeSuggestAwait(errNode, wouldWorkWithAwait, Diagnostics.This_comparison_appears_to_be_unintentional_because_the_types_0_and_1_have_no_overlap, leftStr, rightStr);
                    return;
            }
            ErrorAndMaybeSuggestAwait(errNode, wouldWorkWithAwait, Diagnostics.Operator_0_cannot_be_applied_to_types_1_and_2, TokenText(store.Kind(operatorToken.Node)), leftStr, rightStr);
        }
    }

    // checker.ts getSuggestedBooleanOperator (Unknown: none)
    private static SyntaxKind GetSuggestedBooleanOperator(SyntaxKind operatorKind) => operatorKind switch
    {
        SyntaxKind.BarToken or SyntaxKind.BarEqualsToken => SyntaxKind.BarBarToken,
        SyntaxKind.CaretToken or SyntaxKind.CaretEqualsToken => SyntaxKind.ExclamationEqualsEqualsToken,
        SyntaxKind.AmpersandToken or SyntaxKind.AmpersandEqualsToken => SyntaxKind.AmpersandAmpersandToken,
        _ => SyntaxKind.Unknown,
    };

    // Return true for "indirect calls", (i.e. `(0, x.f)(...)` or `(0, eval)(...)`), which prevents passing `this`.
    private static bool IsIndirectCall(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        if (store.Kind(parent) != SyntaxKind.ParenthesizedExpression)
        {
            return false;
        }
        var left = store.Left(node);
        if (store.Kind(left) != SyntaxKind.NumericLiteral || store.Texts.GetString(store.Text(left)) != "0")
        {
            return false;
        }
        var grandparent = store.Parent(parent);
        if (!((store.Kind(grandparent) == SyntaxKind.CallExpression && store.Expression(grandparent) == parent) || store.Kind(grandparent) == SyntaxKind.TaggedTemplateExpression))
        {
            return false;
        }
        var right = store.Right(node);
        // special-case for "eval" because it's the only non-access case where an indirect call actually affects behavior.
        return Utilities.IsAccessExpression(store, right) || (store.Kind(right) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(right), "eval"));
    }

    // checker.ts checkNaNEquality
    private void CheckNaNEquality(NodeRef errorNode, SyntaxKind operatorKind, NodeRef left, NodeRef right)
    {
        var store = StoreOf(left);
        var isLeftNaN = IsGlobalNaN(At(left, Utilities.SkipParentheses(store, left.Node, false)));
        var isRightNaN = IsGlobalNaN(At(right, Utilities.SkipParentheses(store, right.Node, false)));
        if (isLeftNaN || isRightNaN)
        {
            var err = Error(errorNode, Diagnostics.This_condition_will_always_return_0,
                TokenText(operatorKind is SyntaxKind.EqualsEqualsEqualsToken or SyntaxKind.EqualsEqualsToken ? SyntaxKind.FalseKeyword : SyntaxKind.TrueKeyword));
            if (isLeftNaN && isRightNaN)
            {
                return;
            }
            var operatorString = operatorKind is SyntaxKind.ExclamationEqualsEqualsToken or SyntaxKind.ExclamationEqualsToken ? TokenText(SyntaxKind.ExclamationToken) : "";
            var location = isLeftNaN ? right : left;
            var expression = Utilities.SkipParentheses(store, location.Node, false);
            var text = Utilities.IsEntityNameExpression(store, expression) ? GetTextOfNode(At(location, expression)) : "...";
            AddRelatedInfo(err, CreateRelatedForNode(location, Diagnostics.Did_you_mean_0, operatorString + "Number.isNaN(" + text + ")"));
        }
    }

    private SymbolRef deferredGlobalNaNSymbol;

    // checker.ts isGlobalNaN
    private bool IsGlobalNaN(NodeRef expr)
    {
        var store = StoreOf(expr);
        if (store.Kind(expr.Node) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(expr.Node), "NaN"))
        {
            if (deferredGlobalNaNSymbol.IsNone)
            {
                var symbol = GetGlobalSymbol("NaN", SymbolFlags.Value, null);
                deferredGlobalNaNSymbol = symbol.IsNone ? unknownSymbol : symbol;
            }
            return deferredGlobalNaNSymbol != unknownSymbol && deferredGlobalNaNSymbol == GetResolvedSymbol(expr);
        }
        return false;
    }

    // utilities.ts isLiteralExpressionOfObject
    private static bool IsLiteralExpressionOfObject(NodeStore store, Node node) => store.Kind(node) is SyntaxKind.ObjectLiteralExpression or SyntaxKind.ArrayLiteralExpression
        or SyntaxKind.RegularExpressionLiteral or SyntaxKind.FunctionExpression or SyntaxKind.ClassExpression;

    // checker.ts getBaseTypesIfUnrelated
    private (Type, Type) GetBaseTypesIfUnrelated(Type leftType, Type rightType, Func<Type, Type, bool> isRelated)
    {
        var leftBase = GetBaseTypeOfLiteralType(leftType);
        var rightBase = GetBaseTypeOfLiteralType(rightType);
        return !isRelated(leftBase, rightBase) ? (leftBase, rightBase) : (leftType, rightType);
    }

    // checker.ts getBaseTypeOfLiteralTypeForComparison
    private Type GetBaseTypeOfLiteralTypeForComparison(Type type)
    {
        var flags = Types.Flags(type);
        return (flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 ? stringType
            : (flags & (TypeFlags.NumberLiteral | TypeFlags.Enum)) != 0 ? numberType
            : (flags & TypeFlags.BigIntLiteral) != 0 ? bigintType
            : (flags & TypeFlags.BooleanLiteral) != 0 ? booleanType
            : (flags & TypeFlags.Union) != 0 ? MapType(type, GetBaseTypeOfLiteralTypeForComparison)
            : type;
    }

    // ---- conditional expressions and truthiness ----

    /// <summary>checker.ts checkConditionalExpression.</summary>
    private Type CheckConditionalExpression(NodeRef node, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var condition = At(node, store.Condition(node.Node));
        var type = CheckTruthinessExpression(condition, checkMode);
        CheckTestingKnownTruthyCallableOrAwaitableOrEnumMemberType(condition, type, At(node, store.WhenTrue(node.Node)));
        var type1 = CheckExpression(At(node, store.WhenTrue(node.Node)), checkMode);
        var type2 = CheckExpression(At(node, store.WhenFalse(node.Node)), checkMode);
        return GetUnionType([type1, type2], UnionReduction.Subtype);
    }

    /// <summary>checker.ts checkTestingKnownTruthyCallableOrAwaitableOrEnumMemberType.</summary>
    internal void CheckTestingKnownTruthyCallableOrAwaitableOrEnumMemberType(NodeRef condExpr, Type condType, NodeRef body)
    {
        if (!strictNullChecks)
        {
            return;
        }
        BothHelper(condExpr, body);

        void BothHelper(NodeRef cond, NodeRef b)
        {
            var store = StoreOf(cond);
            var expr = Utilities.SkipParentheses(store, cond.Node, false);
            Helper(At(cond, expr), b);
            while (store.Kind(expr) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(expr)) is SyntaxKind.BarBarToken or SyntaxKind.QuestionQuestionToken)
            {
                expr = Utilities.SkipParentheses(store, store.Left(expr), false);
                Helper(At(cond, expr), b);
            }
        }

        void Helper(NodeRef cond, NodeRef b)
        {
            var store = StoreOf(cond);
            var location = IsLogicalOrCoalescingBinaryExpression(store, cond.Node) ? At(cond, Utilities.SkipParentheses(store, store.Right(cond.Node), false)) : cond;
            if (IsModuleExportsAccessExpression(store, location.Node))
            {
                return;
            }
            if (IsLogicalOrCoalescingBinaryExpression(store, location.Node))
            {
                BothHelper(location, b);
                return;
            }
            var type = location == cond ? condType : CheckExpression(location);
            if ((Types.Flags(type) & TypeFlags.EnumLiteral) != 0 && store.Kind(location.Node) == SyntaxKind.PropertyAccessExpression)
            {
                var expressionSymbol = SymbolNodeLinks.Has(At(location, store.Expression(location.Node))) ? SymbolNodeLinks.Get(At(location, store.Expression(location.Node))).ResolvedSymbol : SymbolRef.None;
                if (((expressionSymbol.IsNone ? SymbolFlags.None : SymbolFlagsOf(expressionSymbol)) & SymbolFlags.Enum) != 0)
                {
                    // EnumLiteral type at condition with known value is always truthy or always falsy, likely an error
                    var value = Types.Literal(type).Value;
                    var truthy = value switch
                    {
                        double d => d != 0 && !double.IsNaN(d),
                        string s => s.Length > 0,
                        _ => value is not null,
                    };
                    Error(location, Diagnostics.This_condition_will_always_return_0, truthy ? "true" : "false");
                    return;
                }
            }
            var isPropertyExpressionCast = store.Kind(location.Node) == SyntaxKind.PropertyAccessExpression && IsTypeAssertion(store, store.Expression(location.Node));
            if (!HasTypeFacts(type, TypeFacts.Truthy) || isPropertyExpressionCast)
            {
                return;
            }
            // While it technically should be invalid for any known-truthy value
            // to be tested, we de-scope to functions and Promises unreferenced in
            // the block as a heuristic to identify the most common bugs. There
            // are too many false positives for values sourced from type
            // definitions without strictNullChecks otherwise.
            var callSignatures = GetSignaturesOfType(type, SignatureKind.Call);
            var isPromise = !GetAwaitedTypeOfPromise(type).IsNone;
            if (callSignatures.Count == 0 && !isPromise)
            {
                return;
            }
            var testedNode = store.Kind(location.Node) == SyntaxKind.Identifier ? location
                : store.Kind(location.Node) == SyntaxKind.PropertyAccessExpression ? At(location, store.Name(location.Node))
                : NodeRef.None;
            var testedSymbol = testedNode.IsNone ? SymbolRef.None : GetSymbolAtLocation(testedNode);
            if (testedSymbol.IsNone && !isPromise)
            {
                return;
            }
            var condParent = store.Parent(cond.Node);
            var isUsed = (!testedSymbol.IsNone && store.Kind(condParent) == SyntaxKind.BinaryExpression && IsSymbolUsedInBinaryExpressionChain(At(cond, condParent), testedSymbol))
                || (!testedSymbol.IsNone && !b.IsNone && IsSymbolUsedInConditionBody(cond, b, testedNode, testedSymbol));
            if (!isUsed)
            {
                if (isPromise)
                {
                    ErrorAndMaybeSuggestAwait(location, maybeMissingAwait: true, Diagnostics.This_condition_will_always_return_true_since_this_0_is_always_defined,
                        TypeToString(type, NodeRef.None, TypeFormatFlags.UseFullyQualifiedType));
                }
                else
                {
                    Error(location, Diagnostics.This_condition_will_always_return_true_since_this_function_is_always_defined_Did_you_mean_to_call_it_instead);
                }
            }
        }
    }

    // utilities.ts isModuleExportsAccessExpression
    private static bool IsModuleExportsAccessExpression(NodeStore store, Node node)
    {
        if (store.Kind(node) is not (SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression))
        {
            return false;
        }
        var expression = store.Expression(node);
        if (store.Kind(expression) != SyntaxKind.Identifier || !store.Texts.Equals(store.EscapedText(expression), "module"))
        {
            return false;
        }
        if (store.Kind(node) == SyntaxKind.PropertyAccessExpression)
        {
            return store.Texts.Equals(store.EscapedText(store.Name(node)), "exports");
        }
        var argument = store.ArgumentExpression(node);
        return Utilities.IsStringLiteralLike(store, argument) && store.Texts.GetString(store.Text(argument)) == "exports";
    }

    // checker.ts isSymbolUsedInConditionBody
    private bool IsSymbolUsedInConditionBody(NodeRef expr, NodeRef body, NodeRef testedNode, SymbolRef testedSymbol)
    {
        var store = StoreOf(body);
        return ForEachChildAny(store, body.Node, Check);

        bool Check(Node childNode)
        {
            if (store.Kind(childNode) == SyntaxKind.Identifier)
            {
                var childSymbol = GetSymbolAtLocation(At(body, childNode));
                if (!childSymbol.IsNone && childSymbol == testedSymbol)
                {
                    // If the test was a simple identifier, the above check is sufficient
                    if (store.Kind(expr.Node) == SyntaxKind.Identifier || (store.Kind(testedNode.Node) == SyntaxKind.Identifier && store.Kind(store.Parent(testedNode.Node)) == SyntaxKind.BinaryExpression))
                    {
                        return true;
                    }
                    // Otherwise we need to ensure the symbol is called on the same target
                    var testedExpression = store.Parent(testedNode.Node);
                    var childExpression = store.Parent(childNode);
                    while (!testedExpression.IsNone && !childExpression.IsNone)
                    {
                        if ((store.Kind(testedExpression) == SyntaxKind.Identifier && store.Kind(childExpression) == SyntaxKind.Identifier) ||
                            (store.Kind(testedExpression) == SyntaxKind.ThisKeyword && store.Kind(childExpression) == SyntaxKind.ThisKeyword))
                        {
                            return GetSymbolAtLocation(At(body, testedExpression)) == GetSymbolAtLocation(At(body, childExpression));
                        }
                        if (store.Kind(testedExpression) == SyntaxKind.PropertyAccessExpression && store.Kind(childExpression) == SyntaxKind.PropertyAccessExpression)
                        {
                            if (GetSymbolAtLocation(At(body, store.Name(testedExpression))) != GetSymbolAtLocation(At(body, store.Name(childExpression))))
                            {
                                return false;
                            }
                            childExpression = store.Expression(childExpression);
                            testedExpression = store.Expression(testedExpression);
                        }
                        else if (store.Kind(testedExpression) == SyntaxKind.CallExpression && store.Kind(childExpression) == SyntaxKind.CallExpression)
                        {
                            childExpression = store.Expression(childExpression);
                            testedExpression = store.Expression(testedExpression);
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
            }
            return ForEachChildAny(store, childNode, Check);
        }
    }

    // checker.ts isSymbolUsedInBinaryExpressionChain
    private bool IsSymbolUsedInBinaryExpressionChain(NodeRef nodeRef, SymbolRef testedSymbol)
    {
        var store = StoreOf(nodeRef);
        var node = nodeRef.Node;
        while (store.Kind(node) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(node)) == SyntaxKind.AmpersandAmpersandToken)
        {
            var isUsed = ForEachChildAny(store, store.Right(node), Visit);
            if (isUsed)
            {
                return true;
            }
            node = store.Parent(node);
        }
        return false;

        bool Visit(Node child)
        {
            if (store.Kind(child) == SyntaxKind.Identifier)
            {
                var symbol = GetSymbolAtLocation(At(nodeRef, child));
                if (!symbol.IsNone && symbol == testedSymbol)
                {
                    return true;
                }
            }
            return ForEachChildAny(store, child, Visit);
        }
    }

    // forEachChild with a predicate: true when the predicate is true for a child
    private static bool ForEachChildAny(NodeStore store, Node node, Func<Node, bool> predicate)
    {
        var found = false;
        NodeVisitor.ForEachChild<object>(store, node, child =>
        {
            if (!found && predicate(child))
            {
                found = true;
            }
            return found ? true : null;
        });
        return found;
    }

    /// <summary>checker.ts checkTruthinessOfType.</summary>
    internal Type CheckTruthinessOfType(Type type, NodeRef node)
    {
        if ((Types.Flags(type) & TypeFlags.Void) != 0)
        {
            Error(node, Diagnostics.An_expression_of_type_void_cannot_be_tested_for_truthiness);
        }
        else
        {
            var semantics = GetSyntacticTruthySemantics(node);
            if (semantics != PredicateSemantics.Sometimes)
            {
                Error(node, semantics == PredicateSemantics.Always ? Diagnostics.This_kind_of_expression_is_always_truthy : Diagnostics.This_kind_of_expression_is_always_falsy);
            }
        }
        return type;
    }

    // checker.ts getSyntacticTruthySemantics
    private PredicateSemantics GetSyntacticTruthySemantics(NodeRef nodeRef)
    {
        var store = StoreOf(nodeRef);
        var node = SkipOuterExpressions(store, nodeRef.Node, OuterExpressionKinds.All);
        switch (store.Kind(node))
        {
            case SyntaxKind.NumericLiteral:
            {
                // Allow `while(0)` or `while(1)`
                var text = store.Texts.GetString(store.Text(node));
                return text is "0" or "1" ? PredicateSemantics.Sometimes : PredicateSemantics.Always;
            }
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.JsxElement:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.RegularExpressionLiteral:
                return PredicateSemantics.Always;
            case SyntaxKind.VoidExpression:
            case SyntaxKind.NullKeyword:
                return PredicateSemantics.Never;
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.StringLiteral:
                return !string.IsNullOrEmpty(store.Texts.GetString(store.Text(node))) ? PredicateSemantics.Always : PredicateSemantics.Never;
            case SyntaxKind.ConditionalExpression:
                return GetSyntacticTruthySemantics(At(nodeRef, store.WhenTrue(node))) | GetSyntacticTruthySemantics(At(nodeRef, store.WhenFalse(node)));
            case SyntaxKind.Identifier:
                if (GetResolvedSymbol(At(nodeRef, node)) == undefinedSymbol)
                {
                    return PredicateSemantics.Never;
                }
                return PredicateSemantics.Sometimes;
        }
        return PredicateSemantics.Sometimes;
    }

    /// <summary>checker.ts checkTruthinessExpression.</summary>
    internal Type CheckTruthinessExpression(NodeRef node, CheckMode checkMode = CheckMode.Normal) =>
        CheckTruthinessOfType(CheckExpression(node, checkMode), node);

    // ---- constant evaluation ----

    /// <summary>checker.ts evaluate(expr).value (createEvaluator): a double, a string or null.</summary>
    internal object? EvaluateConstantExpression(NodeRef expr, NodeRef location = default)
    {
        var store = StoreOf(expr);
        var node = Utilities.SkipParentheses(store, expr.Node, false);
        switch (store.Kind(node))
        {
            case SyntaxKind.PrefixUnaryExpression:
            {
                var value = EvaluateConstantExpression(At(expr, store.Operand(node)), location);
                if (value is double d)
                {
                    switch (store.Operator(node))
                    {
                        case SyntaxKind.PlusToken:
                            return d;
                        case SyntaxKind.MinusToken:
                            return -d;
                        case SyntaxKind.TildeToken:
                            return (double)~ToInt32(d);
                    }
                }
                return null;
            }
            case SyntaxKind.BinaryExpression:
            {
                var left = EvaluateConstantExpression(At(expr, store.Left(node)), location);
                var right = EvaluateConstantExpression(At(expr, store.Right(node)), location);
                var op = store.Kind(store.OperatorToken(node));
                if (left is double l && right is double r)
                {
                    return op switch
                    {
                        SyntaxKind.BarToken => (double)(ToInt32(l) | ToInt32(r)),
                        SyntaxKind.AmpersandToken => (double)(ToInt32(l) & ToInt32(r)),
                        SyntaxKind.GreaterThanGreaterThanToken => (double)(ToInt32(l) >> (int)(ToUint32(r) & 31)),
                        SyntaxKind.GreaterThanGreaterThanGreaterThanToken => (double)(ToUint32(l) >> (int)(ToUint32(r) & 31)),
                        SyntaxKind.LessThanLessThanToken => (double)(ToInt32(l) << (int)(ToUint32(r) & 31)),
                        SyntaxKind.CaretToken => (double)(ToInt32(l) ^ ToInt32(r)),
                        SyntaxKind.AsteriskToken => l * r,
                        SyntaxKind.SlashToken => l / r,
                        SyntaxKind.PlusToken => l + r,
                        SyntaxKind.MinusToken => l - r,
                        SyntaxKind.PercentToken => l % r,
                        SyntaxKind.AsteriskAsteriskToken => Math.Pow(l, r),
                        _ => null,
                    };
                }
                if (left is string or double && right is string or double && op == SyntaxKind.PlusToken)
                {
                    return ToJsString(left!) + ToJsString(right!);
                }
                return null;
            }
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                return store.Texts.GetString(store.Text(node));
            case SyntaxKind.TemplateExpression:
            {
                var result = new System.Text.StringBuilder(store.Texts.GetString(store.Text(store.Head(node))));
                foreach (var span in store.Nodes(store.TemplateSpans(node)))
                {
                    var value = EvaluateConstantExpression(At(expr, store.Expression(span)), location);
                    if (value is null)
                    {
                        return null;
                    }
                    result.Append(ToJsString(value));
                    result.Append(store.Texts.GetString(store.Text(store.Literal(span))));
                }
                return result.ToString();
            }
            case SyntaxKind.NumericLiteral:
                return JsNumber.FromString(store.Texts.GetString(store.Text(node))!);
            case SyntaxKind.Identifier:
                return EvaluateEntityNameExpression(At(expr, node), location);
            case SyntaxKind.PropertyAccessExpression:
                if (Utilities.IsEntityNameExpression(store, node))
                {
                    return EvaluateEntityNameExpression(At(expr, node), location);
                }
                return null;
            case SyntaxKind.ElementAccessExpression:
                return EvaluateElementAccessExpression(At(expr, node), location);
        }
        return null;

        static string ToJsString(object value) => value is double d ? JsNumber.ToString(d) : (string)value;
    }

    // ECMAScript ToUint32
    private static uint ToUint32(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }
        var truncated = Math.Truncate(value);
        var modulo = truncated % 4294967296.0;
        if (modulo < 0)
        {
            modulo += 4294967296.0;
        }
        return (uint)modulo;
    }

    // ECMAScript ToInt32
    private static int ToInt32(double value) => unchecked((int)ToUint32(value));

    // checker.ts evaluateEntityNameExpression
    private object? EvaluateEntityNameExpression(NodeRef expr, NodeRef location)
    {
        var symbol = ResolveEntityName(expr, SymbolFlags.Value, ignoreErrors: true);
        if (symbol.IsNone)
        {
            return null;
        }
        var store = StoreOf(expr);
        if (store.Kind(expr.Node) == SyntaxKind.Identifier)
        {
            var name = store.Texts.GetString(store.EscapedText(expr.Node));
            if (name is "Infinity" or "NaN" && symbol == GetGlobalSymbol(name, SymbolFlags.Value, null))
            {
                return name == "Infinity" ? double.PositiveInfinity : double.NaN;
            }
        }
        if ((SymbolFlagsOf(symbol) & SymbolFlags.EnumMember) != 0)
        {
            return !location.IsNone ? EvaluateEnumMember(expr, symbol, location) : GetEnumMemberValue(ValueDeclarationOf(symbol));
        }
        if ((SymbolFlagsOf(symbol) & SymbolFlags.Variable) != 0 && (GetDeclarationNodeFlagsFromSymbol(symbol) & NodeFlags.Constant) != 0)
        {
            var declaration = ValueDeclarationOf(symbol);
            if (!declaration.IsNone && Kind(declaration) == SyntaxKind.VariableDeclaration)
            {
                var dstore = StoreOf(declaration);
                var initializer = dstore.Initializer(declaration.Node);
                if (dstore.Type(declaration.Node).IsNone && !initializer.IsNone &&
                    (location.IsNone || (declaration != location && IsBlockScopedNameDeclaredBeforeUse(declaration, location))))
                {
                    return EvaluateConstantExpression(At(declaration, initializer), declaration);
                }
            }
        }
        return null;
    }
}
