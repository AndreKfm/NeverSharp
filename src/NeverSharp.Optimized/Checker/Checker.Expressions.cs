// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (checkExpression for identifiers, literals, `this`, parenthesized and asserted
// expressions and property access of resolved members: checkIdentifier, checkThisExpression, tryGetThisTypeAt,
// checkPropertyAccessExpression, checkQualifiedName, checkExpressionCached, getTypeOfExpression)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C1. TODO(C5): the rest of the checkExpression family (operators,
// calls with overload resolution, object and array literals with contextual typing, element access, JSX in C9), the
// diagnostics of property access (missing properties, accessibility), contextual typing. TODO(C7): narrowing (the
// declared types of references are used). The few expression kinds C1 approximates for the walks are marked.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private readonly Dictionary<NodeRef, Type> expressionTypes = [];

    /// <summary>checker.ts checkExpressionCached.</summary>
    internal Type CheckExpressionCached(NodeRef node)
    {
        if (expressionTypes.TryGetValue(node, out var type))
        {
            return type;
        }
        type = CheckExpression(node);
        expressionTypes[node] = type;
        return type;
    }

    /// <summary>checker.ts getTypeOfExpression (C1: the cached checkExpression).</summary>
    internal Type GetTypeOfExpression(NodeRef node) => CheckExpressionCached(node);

    /// <summary>checker.ts checkExpression / checkExpressionWorker (the C1 subset, see the file header).</summary>
    internal Type CheckExpression(NodeRef node)
    {
        if (expressionTypes.TryGetValue(node, out var cached))
        {
            return cached;
        }
        var type = CheckExpressionWorker(node);
        expressionTypes[node] = type;
        return type;
    }

    private Type CheckExpressionWorker(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.Identifier:
                return CheckIdentifier(node);
            case SyntaxKind.ThisKeyword:
                return CheckThisExpression(node);
            case SyntaxKind.SuperKeyword:
                return CheckSuperExpressionForMembers(node);
            case SyntaxKind.NullKeyword:
                return nullWideningType;
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.StringLiteral:
                return GetFreshTypeOfLiteralType(GetStringLiteralType(store.Texts.GetString(store.Text(n))!));
            case SyntaxKind.NumericLiteral:
                return GetFreshTypeOfLiteralType(GetNumberLiteralType(JsNumber.FromString(store.Texts.GetString(store.Text(n))!)));
            case SyntaxKind.BigIntLiteral:
                return GetFreshTypeOfLiteralType(GetBigIntLiteralType(Utilities.ParsePseudoBigInt(store.Texts.GetString(store.Text(n))!)));
            case SyntaxKind.TrueKeyword:
                return trueType;
            case SyntaxKind.FalseKeyword:
                return falseType;
            case SyntaxKind.TemplateExpression:
                // (template literal types in const contexts: C5)
                foreach (var span in store.Nodes(store.TemplateSpans(n)))
                {
                    CheckExpression(At(node, store.Expression(span)));
                }
                return stringType;
            case SyntaxKind.RegularExpressionLiteral:
                return globalRegExpType;
            case SyntaxKind.ParenthesizedExpression:
                return CheckExpression(At(node, store.Expression(n)));
            case SyntaxKind.AsExpression:
            case SyntaxKind.TypeAssertionExpression:
            {
                var exprType = CheckExpression(At(node, store.Expression(n)));
                var typeNode = store.Type(n);
                if (store.Kind(typeNode) == SyntaxKind.TypeReference && store.Kind(store.TypeName(typeNode)) == SyntaxKind.Identifier &&
                    store.Texts.Equals(store.EscapedText(store.TypeName(typeNode)), "const") && store.TypeArguments(typeNode).IsNone)
                {
                    return GetRegularTypeOfLiteralType(exprType);
                }
                return GetTypeFromTypeNode(At(node, typeNode));
            }
            case SyntaxKind.SatisfiesExpression:
                GetTypeFromTypeNode(At(node, store.Type(n)));
                return CheckExpression(At(node, store.Expression(n)));
            case SyntaxKind.NonNullExpression:
                return GetNonNullableType(CheckExpression(At(node, store.Expression(n))));
            case SyntaxKind.PropertyAccessExpression:
                return CheckPropertyAccessExpression(node);
            case SyntaxKind.QualifiedName:
                return CheckQualifiedName(node);
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.ClassExpression:
            {
                var symbol = GetSymbolOfDeclaration(node);
                return symbol.IsNone ? anyFunctionType : GetTypeOfSymbol(symbol);
            }
            case SyntaxKind.ObjectLiteralExpression:
            {
                // TODO(C5): checkObjectLiteral (property types, spreads, contextual types, freshness)
                var symbol = NodeSymbol(node);
                var type = CreateObjectType(ObjectFlags.Anonymous | ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral, symbol);
                return type;
            }
            case SyntaxKind.ArrayLiteralExpression:
                return CheckArrayLiteral(node);
            case SyntaxKind.CallExpression:
            case SyntaxKind.NewExpression:
                return CheckCallExpression(node);
            case SyntaxKind.VoidExpression:
                CheckExpression(At(node, store.Expression(n)));
                return undefinedWideningType;
            case SyntaxKind.DeleteExpression:
                CheckExpression(At(node, store.Expression(n)));
                return booleanType;
            case SyntaxKind.PrefixUnaryExpression:
            {
                var operand = At(node, store.Operand(n));
                var operandType = CheckExpression(operand);
                switch (store.Operator(n))
                {
                    case SyntaxKind.MinusToken:
                        if (store.Kind(operand.Node) == SyntaxKind.NumericLiteral)
                        {
                            return GetFreshTypeOfLiteralType(GetNumberLiteralType(-JsNumber.FromString(store.Texts.GetString(store.Text(operand.Node))!)));
                        }
                        if (store.Kind(operand.Node) == SyntaxKind.BigIntLiteral)
                        {
                            var value = Utilities.ParsePseudoBigInt(store.Texts.GetString(store.Text(operand.Node))!);
                            return GetFreshTypeOfLiteralType(GetBigIntLiteralType(value == "0" ? value : "-" + value));
                        }
                        return (Types.Flags(operandType) & TypeFlags.BigIntLike) != 0 ? bigintType : numberType;
                    case SyntaxKind.PlusToken:
                        if (store.Kind(operand.Node) == SyntaxKind.NumericLiteral)
                        {
                            return GetFreshTypeOfLiteralType(GetNumberLiteralType(JsNumber.FromString(store.Texts.GetString(store.Text(operand.Node))!)));
                        }
                        return numberType;
                    case SyntaxKind.ExclamationToken:
                        return booleanType;
                    default:
                        return (Types.Flags(operandType) & TypeFlags.BigIntLike) != 0 ? bigintType : numberType;
                }
            }
            case SyntaxKind.PostfixUnaryExpression:
            {
                var operandType = CheckExpression(At(node, store.Operand(n)));
                return (Types.Flags(operandType) & TypeFlags.BigIntLike) != 0 ? bigintType : numberType;
            }
            case SyntaxKind.BinaryExpression:
                return CheckBinaryExpression(node);
            case SyntaxKind.ConditionalExpression:
            {
                CheckExpression(At(node, store.Condition(n)));
                var t1 = CheckExpression(At(node, store.WhenTrue(n)));
                var t2 = CheckExpression(At(node, store.WhenFalse(n)));
                return GetUnionType([t1, t2], UnionReduction.Subtype);
            }
            case SyntaxKind.TypeOfExpression:
                CheckExpression(At(node, store.Expression(n)));
                return stringType; // TODO(C5): the union of the typeof result literals
            case SyntaxKind.AwaitExpression:
                CheckExpression(At(node, store.Expression(n)));
                return anyType; // TODO(C5): awaited types
            case SyntaxKind.SpreadElement:
                return CheckExpression(At(node, store.Expression(n)));
            case SyntaxKind.ElementAccessExpression:
                return CheckElementAccessExpression(node);
            case SyntaxKind.OmittedExpression:
                return undefinedWideningType;
            case SyntaxKind.YieldExpression:
                return CheckYieldExpression(node);
            default:
                // TODO(C5): super, yield, tagged templates, meta properties, JSX (C9), synthetic expressions
                return errorType;
        }
    }

    /// <summary>checker.ts getNonNullableType (strictNullChecks: removes null and undefined).</summary>
    internal Type GetNonNullableType(Type type)
    {
        if (!strictNullChecks)
        {
            return type;
        }
        if ((Types.Flags(type) & TypeFlags.Union) != 0)
        {
            return MapType(type, t => (Types.Flags(t) & TypeFlags.Nullable) != 0 ? Type.None : t);
        }
        return (Types.Flags(type) & TypeFlags.Nullable) != 0 ? neverType : type;
    }

    // checker.ts checkArrayLiteral (TODO(C5): contextual tuple types, spreads, const contexts)
    private Type CheckArrayLiteral(NodeRef node)
    {
        var store = StoreOf(node);
        var elementTypes = new List<Type>();
        foreach (var e in store.Nodes(store.Elements(node.Node)))
        {
            var t = CheckExpressionForMutableLocation(At(node, e));
            elementTypes.Add(t);
        }
        var elementType = elementTypes.Count > 0 ? GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(elementTypes), UnionReduction.Subtype) : neverType;
        var arrayType = CreateArrayType(elementTypes.Count > 0 ? elementType : strictNullChecks ? implicitNeverType : undefinedWideningType);
        return CreateArrayLiteralType(arrayType);
    }

    // checker.ts createArrayLiteralType: a reference with the ArrayLiteral flags (cached per reference)
    private readonly Dictionary<Type, Type> arrayLiteralTypes = [];

    private Type CreateArrayLiteralType(Type type)
    {
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.Reference) == 0)
        {
            return type;
        }
        if (!arrayLiteralTypes.TryGetValue(type, out var literalType))
        {
            literalType = CreateObjectType(ObjectFlags.Reference | ObjectFlags.ArrayLiteral | ObjectFlags.ContainsObjectOrArrayLiteral, Types.Symbol(type));
            var data = Types.Object(literalType);
            data.Target = Types.Object(type).Target;
            data.ResolvedTypeArguments = Types.CreateList(GetTypeArguments(type));
            data.HasResolvedTypeArguments = true;
            arrayLiteralTypes[type] = literalType;
        }
        return literalType;
    }

    /// <summary>checker.ts checkExpressionForMutableLocation (literal widening without a contextual literal type).</summary>
    internal Type CheckExpressionForMutableLocation(NodeRef node)
    {
        var type = CheckExpression(node);
        return IsConstContext(node) ? GetRegularTypeOfLiteralType(type) : GetWidenedLiteralType(type);
    }

    // checker.ts isConstContext (the `as const` part; const type parameters: C6)
    private bool IsConstContext(NodeRef node)
    {
        var store = StoreOf(node);
        var parent = store.Parent(node.Node);
        while (true)
        {
            switch (store.Kind(parent))
            {
                case SyntaxKind.AsExpression:
                case SyntaxKind.TypeAssertionExpression:
                    return Utilities.IsConstAssertion(store, parent);
                case SyntaxKind.ParenthesizedExpression:
                case SyntaxKind.ArrayLiteralExpression:
                case SyntaxKind.SpreadElement:
                case SyntaxKind.PropertyAssignment:
                case SyntaxKind.ShorthandPropertyAssignment:
                case SyntaxKind.ObjectLiteralExpression:
                    parent = store.Parent(parent);
                    continue;
                case SyntaxKind.TemplateSpan:
                    parent = store.Parent(store.Parent(parent));
                    continue;
                default:
                    return false;
            }
        }
    }

    /// <summary>checker.ts checkIdentifier (narrowing and the assignment checks: C7 / C5).</summary>
    internal Type CheckIdentifier(NodeRef node)
    {
        var store = StoreOf(node);
        if (IsThisInTypeQuery(store, node.Node))
        {
            return CheckThisExpression(node);
        }
        var symbol = GetResolvedSymbol(node);
        if (symbol == unknownSymbol)
        {
            return errorType;
        }
        var localOrExportSymbol = GetExportSymbolOfValueSymbolIfExported(symbol);
        if (localOrExportSymbol == argumentsSymbol)
        {
            return GetTypeOfSymbol(argumentsSymbol);
        }
        var type = GetTypeOfSymbol(localOrExportSymbol);
        if (uncertainSymbols.Contains(localOrExportSymbol))
        {
            uncertainExpressions.Add(node);
        }
        // TODO(C7): getFlowTypeOfReference (autoType and autoArrayType are evolved by control flow analysis)
        if (type == autoType || type == autoArrayType)
        {
            return type == autoType ? anyType : anyArrayType;
        }
        return type;
    }

    /// <summary>checker.ts checkThisExpression.</summary>
    internal Type CheckThisExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var isNodeInTypeQuery = Utilities.IsInTypeQuery(store, node.Node);
        // Stop at the first arrow function so that we can
        // tell whether 'this' needs to be captured.
        var container = Utilities.GetThisContainer(store, node.Node, includeArrowFunctions: true, includeClassComputedPropertyName: true);
        var capturedByArrowFunction = false;
        var thisInComputedPropertyName = false;
        while (true)
        {
            // Now skip arrow functions to get the "real" owner of 'this'.
            if (store.Kind(container) == SyntaxKind.ArrowFunction)
            {
                container = Utilities.GetThisContainer(store, container, includeArrowFunctions: false, includeClassComputedPropertyName: !thisInComputedPropertyName);
                capturedByArrowFunction = true;
            }
            if (store.Kind(container) == SyntaxKind.ComputedPropertyName)
            {
                container = Utilities.GetThisContainer(store, container, includeArrowFunctions: !capturedByArrowFunction, includeClassComputedPropertyName: false);
                thisInComputedPropertyName = true;
                continue;
            }
            break;
        }
        if (thisInComputedPropertyName)
        {
            Error(node, Diagnostics.this_cannot_be_referenced_in_a_computed_property_name);
        }
        else
        {
            switch (store.Kind(container))
            {
                case SyntaxKind.ModuleDeclaration:
                    Error(node, Diagnostics.this_cannot_be_referenced_in_a_module_or_namespace_body);
                    break;
                case SyntaxKind.EnumDeclaration:
                    Error(node, Diagnostics.this_cannot_be_referenced_in_current_location);
                    break;
            }
        }
        _ = isNodeInTypeQuery;
        var type = TryGetThisTypeAt(node, includeGlobalThis: true, At(node, container));
        if (compilerOptions.GetStrictOptionValue(compilerOptions.NoImplicitThis))
        {
            var globalThisType = GetTypeOfSymbol(globalThisSymbol);
            if (type == globalThisType && capturedByArrowFunction)
            {
                Error(node, Diagnostics.The_containing_arrow_function_captures_the_global_value_of_this);
            }
            else if (type.IsNone)
            {
                // With noImplicitThis, functions may not reference 'this' if it has type 'any'
                var diag = Error(node, Diagnostics.this_implicitly_has_type_any_because_it_does_not_have_a_type_annotation);
                if (store.Kind(container) != SyntaxKind.SourceFile)
                {
                    var outsideThis = TryGetThisTypeAt(At(node, container), includeGlobalThis: true, At(node, Utilities.GetThisContainer(store, container, false, false)));
                    if (!outsideThis.IsNone && outsideThis != globalThisType)
                    {
                        (diag.RelatedInformation ??= []).Add(CreateRelatedForNode(At(node, container), Diagnostics.An_outer_value_of_this_is_shadowed_by_this_container));
                    }
                }
            }
        }
        return type.IsNone ? anyType : type;
    }

    // checker.ts tryGetThisTypeAt (contextual this types of object literal methods: C5)
    private Type TryGetThisTypeAt(NodeRef node, bool includeGlobalThis, NodeRef container)
    {
        var store = StoreOf(node);
        var c = container.Node;
        if (!c.IsNone && Utilities.IsFunctionLike(store, c))
        {
            var thisParameter = Signatures.Record(GetSignatureFromDeclaration(container)).ThisParameter;
            if (!thisParameter.IsNone)
            {
                return GetTypeOfSymbol(thisParameter);
            }
            var contextual = GetContextualThisParameterType(container);
            if (!contextual.IsNone)
            {
                return contextual;
            }
        }
        var parent = c.IsNone ? Node.None : store.Parent(c);
        if (!parent.IsNone && Utilities.IsClassLike(store, parent))
        {
            var symbol = GetSymbolOfDeclaration(At(node, parent));
            return Utilities.IsStatic(store, c) ? GetTypeOfSymbol(symbol) : ThisTypeOfClassOrInterface(GetDeclaredTypeOfSymbol(symbol));
        }
        if (!c.IsNone && store.Kind(c) == SyntaxKind.SourceFile)
        {
            // look up in the source file's locals or exports
            if (!BindingOf(container.File).CommonJsModuleIndicator.IsNone)
            {
                var fileSymbol = GetSymbolOfDeclaration(container);
                return fileSymbol.IsNone ? Type.None : GetTypeOfSymbol(fileSymbol);
            }
            if (Utilities.IsExternalModule(store, c))
            {
                return undefinedType;
            }
            if (includeGlobalThis)
            {
                return GetTypeOfSymbol(globalThisSymbol);
            }
        }
        return Type.None;
    }

    // checker.ts getContextualThisParameterType (TODO(C5): object literal methods with a contextual type; C1: the
    // methods of an object literal see the literal's type)
    private Type GetContextualThisParameterType(NodeRef func)
    {
        var store = StoreOf(func);
        if (store.Kind(func.Node) == SyntaxKind.ArrowFunction)
        {
            return Type.None;
        }
        if (IsContextSensitiveFunctionOrObjectLiteralMethod(store, func.Node))
        {
            var parent = store.Parent(func.Node);
            var literal = store.Kind(func.Node) == SyntaxKind.MethodDeclaration ? parent
                : store.Kind(parent) == SyntaxKind.PropertyAssignment ? store.Parent(parent) : Node.None;
            if (!literal.IsNone && store.Kind(literal) == SyntaxKind.ObjectLiteralExpression)
            {
                return CheckExpressionCached(At(func, literal));
            }
            if (HasContextualType(At(func, func.Node)))
            {
                return anyType; // TODO(C5): the this parameter type of the contextual signature
            }
        }
        return Type.None;
    }

    private static bool IsContextSensitiveFunctionOrObjectLiteralMethod(NodeStore store, Node func) =>
        store.Kind(func) is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction
        || (store.Kind(func) == SyntaxKind.MethodDeclaration && store.Kind(store.Parent(func)) == SyntaxKind.ObjectLiteralExpression);

    /// <summary>
    /// checker.ts getContextuallyTypedParameterType. TODO(C5/C6): the parameter type of the contextual signature;
    /// C1 returns <c>any</c> for parameters of function expressions that have a contextual signature (so they are not
    /// reported as implicitly any) and None otherwise.
    /// </summary>
    private Type GetContextuallyTypedParameterType(NodeRef parameter)
    {
        var func = Parent(parameter);
        var store = StoreOf(func);
        if (!IsContextSensitiveFunctionOrObjectLiteralMethod(store, func.Node))
        {
            return Type.None;
        }
        var iife = Utilities.GetImmediatelyInvokedFunctionExpression(store, func.Node);
        if (!iife.IsNone && !store.Arguments(iife).IsNone)
        {
            // the parameters of an immediately invoked function have the types of the arguments (spread arguments: C5)
            var args = store.Nodes(store.Arguments(iife));
            var indexOfParameter = store.Nodes(store.Parameters(func.Node)).IndexOf(parameter.Node);
            if (!store.DotDotDotToken(parameter.Node).IsNone)
            {
                return anyArrayType;
            }
            return indexOfParameter < args.Length ? GetWidenedLiteralType(CheckExpressionCached(At(func, args[indexOfParameter])))
                : !store.Initializer(parameter.Node).IsNone ? Type.None : undefinedWideningType;
        }
        return HasContextualType(func) ? anyType : Type.None;
    }

    // Whether a function expression or object literal method has a contextual signature (C1 approximation of
    // getContextualSignature, TODO(C5)): its contextual type is unknown but present, or has call signatures.
    private bool HasContextualType(NodeRef func)
    {
        var contextualType = GetApproximateContextualType(func, 0);
        return contextualType == unknownContextualType || (!contextualType.IsNone && HasSignatures(contextualType));
    }

    // A marker for a contextual type that exists but C1 does not compute (contextual signatures' return types, generic
    // parameters, tuples).
    private Type unknownContextualType => anyFunctionType;

    // checker.ts getContextualType, approximated (None: no contextual type)
    private Type GetApproximateContextualType(NodeRef node, int depth)
    {
        if (depth > 16)
        {
            return unknownContextualType;
        }
        var store = StoreOf(node);
        var n = node.Node;
        var parent = store.Parent(n);
        switch (store.Kind(parent))
        {
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.Parameter:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            {
                var typeNode = store.Type(parent);
                return store.Initializer(parent) == n && !typeNode.IsNone ? GetTypeFromTypeNode(At(node, typeNode)) : Type.None;
            }
            case SyntaxKind.ReturnStatement:
            case SyntaxKind.YieldExpression:
            {
                var func = Utilities.GetContainingFunctionOrNone(store, parent);
                return ContextualReturnType(At(node, func), depth);
            }
            case SyntaxKind.ArrowFunction:
                return store.Body(parent) == n ? ContextualReturnType(At(node, parent), depth) : Type.None;
            case SyntaxKind.ObjectLiteralExpression:
            {
                // a method or accessor of an object literal
                var objectContextualType = GetApproximateContextualType(At(node, parent), depth + 1);
                return ContextualPropertyType(objectContextualType, At(node, store.Name(n)));
            }
            case SyntaxKind.ParenthesizedExpression:
                return GetApproximateContextualType(At(node, parent), depth + 1);
            case SyntaxKind.ConditionalExpression:
                return store.Condition(parent) != n ? GetApproximateContextualType(At(node, parent), depth + 1) : Type.None;
            case SyntaxKind.BinaryExpression:
            {
                var op = store.Kind(store.OperatorToken(parent));
                if (op == SyntaxKind.EqualsToken)
                {
                    return store.Right(parent) == n ? CheckExpressionCached(At(node, store.Left(parent))) : Type.None;
                }
                if (op is SyntaxKind.BarBarToken or SyntaxKind.QuestionQuestionToken or SyntaxKind.AmpersandAmpersandToken)
                {
                    return GetApproximateContextualType(At(node, parent), depth + 1);
                }
                if (op == SyntaxKind.CommaToken)
                {
                    return store.Right(parent) == n ? GetApproximateContextualType(At(node, parent), depth + 1) : Type.None;
                }
                return Type.None;
            }
            case SyntaxKind.PropertyAssignment:
            {
                if (store.Initializer(parent) != n)
                {
                    return Type.None;
                }
                var objectContextualType = GetApproximateContextualType(At(node, store.Parent(parent)), depth + 1);
                return ContextualPropertyType(objectContextualType, At(node, store.Name(parent)));
            }
            case SyntaxKind.ArrayLiteralExpression:
            {
                var arrayContextualType = GetApproximateContextualType(At(node, parent), depth + 1);
                if (arrayContextualType.IsNone || arrayContextualType == unknownContextualType)
                {
                    return arrayContextualType;
                }
                if ((Types.ObjectFlagsOf(arrayContextualType) & ObjectFlags.Reference) != 0 &&
                    Types.Object(arrayContextualType).Target is var target && (target == globalArrayType || target == globalReadonlyArrayType))
                {
                    return GetTypeArguments(arrayContextualType)[0];
                }
                return (Types.Flags(arrayContextualType) & TypeFlags.Any) != 0 ? Type.None : unknownContextualType;
            }
            case SyntaxKind.SpreadElement:
                return GetApproximateContextualType(At(node, parent), depth + 1) is var spread && !spread.IsNone ? unknownContextualType : Type.None;
            case SyntaxKind.AsExpression:
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.SatisfiesExpression:
                return Utilities.IsConstAssertion(store, parent) ? Type.None : GetTypeFromTypeNode(At(node, store.Type(parent)));
            case SyntaxKind.CallExpression:
            case SyntaxKind.NewExpression:
            {
                var arguments = store.Arguments(parent);
                var argIndex = arguments.IsNone ? -1 : store.Nodes(arguments).IndexOf(n);
                if (argIndex < 0)
                {
                    return Type.None;
                }
                var calleeType = CheckExpressionCached(At(node, store.Expression(parent)));
                if ((Types.Flags(calleeType) & TypeFlags.Any) != 0)
                {
                    return Type.None;
                }
                var signatures = GetSignaturesOfType(calleeType, store.Kind(parent) == SyntaxKind.NewExpression ? SignatureKind.Construct : SignatureKind.Call);
                if (signatures.Count == 0)
                {
                    return Type.None;
                }
                if (signatures.Count > 1 || Signatures.Record(signatures[0]).TypeParameters.Count > 0)
                {
                    return unknownContextualType; // (overloads and inference: C5/C6)
                }
                var parameters = Signatures.Record(signatures[0]).Parameters ?? [];
                if (argIndex < parameters.Length)
                {
                    var parameterDeclaration = ValueDeclarationOf(parameters[argIndex]);
                    if (!parameterDeclaration.IsNone && Kind(parameterDeclaration) == SyntaxKind.Parameter && !StoreOf(parameterDeclaration).DotDotDotToken(parameterDeclaration.Node).IsNone)
                    {
                        return unknownContextualType;
                    }
                    return GetTypeOfSymbol(parameters[argIndex]);
                }
                if (parameters.Length > 0 && (Signatures.Record(signatures[0]).Flags & SignatureFlags.HasRestParameter) != 0)
                {
                    return unknownContextualType;
                }
                return Type.None;
            }
            case SyntaxKind.Decorator:
                return unknownContextualType;
            case SyntaxKind.JsxExpression:
            case SyntaxKind.JsxAttribute:
                return HasJsxContext(node) ? unknownContextualType : Type.None;
            default:
                return Type.None;
        }
    }

    // the contextual return type of a function: its annotation, or (unknown) when the function is contextually typed
    private Type ContextualReturnType(NodeRef func, int depth)
    {
        if (func.IsNone)
        {
            return Type.None;
        }
        var store = StoreOf(func);
        var typeNode = store.Type(func.Node);
        if (!typeNode.IsNone)
        {
            return unknownContextualType; // (the awaited or iterated return type: C5)
        }
        if (IsContextSensitiveFunctionOrObjectLiteralMethod(store, func.Node))
        {
            var contextualType = GetApproximateContextualType(func, depth + 1);
            return contextualType == unknownContextualType || (!contextualType.IsNone && HasSignatures(contextualType)) ? unknownContextualType : Type.None;
        }
        return Type.None;
    }

    // getTypeOfPropertyOfContextualType: the property's type (or a string index signature's)
    private Type ContextualPropertyType(Type objectContextualType, NodeRef name)
    {
        if (objectContextualType.IsNone || objectContextualType == unknownContextualType)
        {
            return objectContextualType;
        }
        if ((Types.Flags(objectContextualType) & TypeFlags.Any) != 0)
        {
            return Type.None;
        }
        if ((Types.Flags(objectContextualType) & (TypeFlags.Union | TypeFlags.Intersection | TypeFlags.Instantiable)) != 0 || IsUnsupportedType(objectContextualType))
        {
            return unknownContextualType;
        }
        if (name.IsNone)
        {
            return Type.None;
        }
        var store = StoreOf(name);
        if (store.Kind(name.Node) is not (SyntaxKind.Identifier or SyntaxKind.StringLiteral or SyntaxKind.NumericLiteral or SyntaxKind.PrivateIdentifier))
        {
            return unknownContextualType;
        }
        var text = store.Kind(name.Node) == SyntaxKind.Identifier || store.Kind(name.Node) == SyntaxKind.PrivateIdentifier
            ? store.Texts.GetString(store.EscapedText(name.Node))!
            : Utilities.EscapeLeadingUnderscores(store.Texts.GetString(store.Text(name.Node))!);
        var prop = GetPropertyOfType(objectContextualType, text);
        if (!prop.IsNone)
        {
            return GetTypeOfSymbol(prop);
        }
        foreach (var info in GetIndexInfosOfType(objectContextualType))
        {
            if ((Types.Flags(info.KeyType) & TypeFlags.String) != 0)
            {
                return info.Type;
            }
        }
        return Type.None;
    }

    private bool HasSignatures(Type type, SignatureKind kind = SignatureKind.Call)
    {
        if ((Types.Flags(type) & TypeFlags.Any) != 0)
        {
            return false;
        }
        if ((Types.Flags(type) & TypeFlags.Union) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(type).Types).ToArray())
            {
                if (HasSignatures(t, kind))
                {
                    return true;
                }
            }
            return false;
        }
        return (Types.Flags(type) & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.TypeParameter)) != 0 && GetSignaturesOfType(type, kind).Count > 0;
    }

    /// <summary>checker.ts checkPropertyAccessExpression (the resolved-member part; errors: TODO(C5)).</summary>
    internal Type CheckPropertyAccessExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var left = At(node, store.Expression(node.Node));
        var leftType = store.Kind(left.Node) == SyntaxKind.SuperKeyword ? CheckSuperExpressionForMembers(left) : CheckExpressionCached(left);
        return CheckPropertyAccessOnType(node, leftType, At(node, store.Name(node.Node)));
    }

    // checker.ts checkPropertyAccessExpressionOrQualifiedName (TODO(C5): diagnostics, private names, index signatures,
    // optional chains, flow types)
    private Type CheckPropertyAccessOnType(NodeRef node, Type leftType, NodeRef right)
    {
        var store = StoreOf(right);
        string name;
        if (store.Kind(right.Node) == SyntaxKind.PrivateIdentifier)
        {
            // lookupSymbolForPrivateIdentifierDeclaration: the member of a containing class declared with this name
            var lexicallyScopedSymbol = LookupSymbolForPrivateIdentifierDeclaration(right);
            if (lexicallyScopedSymbol.IsNone)
            {
                return errorType;
            }
            name = NameOf(lexicallyScopedSymbol);
        }
        else if (store.Kind(right.Node) == SyntaxKind.Identifier)
        {
            name = store.Texts.GetString(store.EscapedText(right.Node))!;
        }
        else
        {
            return errorType;
        }
        if (IsErrorType(leftType))
        {
            // isAnyLike: the error type itself, not the unresolved alias the left side was printed with
            return errorType;
        }
        if ((Types.Flags(leftType) & TypeFlags.Any) != 0)
        {
            return leftType;
        }
        var leftNode = StoreOf(node).Kind(node.Node) == SyntaxKind.PropertyAccessExpression ? StoreOf(node).Expression(node.Node) : StoreOf(node).Left(node.Node);
        var leftUncertain = IsUncertainExpression(At(node, leftNode));
        if (leftUncertain)
        {
            uncertainExpressions.Add(node);
        }
        var apparentType = GetApparentType(GetNonNullableType(leftType));
        var prop = GetPropertyOfType(apparentType, name);
        if (prop.IsNone)
        {
            if (Types.Symbol(leftType) == globalThisSymbol && store.Kind(right.Node) == SyntaxKind.Identifier && !HasApplicableIndexInfoForName(apparentType, name))
            {
                var globalThisMember = globals.Get(name);
                if (!globalThisMember.IsNone && (SymbolFlagsOf(globalThisMember) & SymbolFlags.BlockScoped) != 0)
                {
                    Error(right, Diagnostics.Property_0_does_not_exist_on_type_1, name, TypeToString(leftType));
                }
                else if (noImplicitAny)
                {
                    Error(right, Diagnostics.Element_implicitly_has_an_any_type_because_type_0_has_no_index_signature, TypeToString(leftType));
                }
                return anyType;
            }
            if (!leftUncertain && store.Kind(right.Node) == SyntaxKind.Identifier && name.Length > 0 && !IsUnsupportedType(apparentType) && !HasApplicableIndexInfoForName(apparentType, name) && !CheckAndReportErrorForExtendingInterface(node))
            {
                var isThisTypeParameter = (Types.Flags(leftType) & TypeFlags.TypeParameter) != 0 && Types.TypeParameter(leftType).IsThisType;
                ReportNonexistentProperty(right, isThisTypeParameter ? apparentType : leftType);
            }
            return errorType;
        }
        SymbolNodeLinks.Get(node).ResolvedSymbol = prop;
        if (store.Kind(node.Node) == SyntaxKind.PropertyAccessExpression || store.Kind(node.Node) == SyntaxKind.QualifiedName)
        {
            SymbolNodeLinks.Get(right).ResolvedSymbol = prop;
        }
        // markPropertyAsReferenced(prop, node, isSelfTypeAccess(left, parentSymbol))
        var left = store.Kind(node.Node) == SyntaxKind.PropertyAccessExpression ? store.Expression(node.Node) : store.Left(node.Node);
        var isSelfTypeAccess = store.Kind(left) == SyntaxKind.ThisKeyword ||
            (store.Kind(left) == SyntaxKind.Identifier && SymbolNodeLinks.Has(At(node, left)) && SymbolNodeLinks.Get(At(node, left)).ResolvedSymbol == GetParentOfSymbol(prop));
        MarkPropertyAsReferenced(prop, node, isSelfTypeAccess);
        return GetTypeOfSymbol(prop);
    }

    // TODO(C5): checkElementAccessExpression / getIndexedAccessType. C1: literal property names (the property's type),
    // array element types and index signatures of non-generic types; anything else is any and marked uncertain. No
    // diagnostics are reported.
    private Type CheckElementAccessExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var objectNode = At(node, store.Expression(n));
        var objectType = CheckExpression(objectNode);
        var indexType = CheckExpression(At(node, store.ArgumentExpression(n)));
        if ((Types.Flags(objectType) & TypeFlags.Any) != 0)
        {
            return IsErrorType(objectType) ? errorType : anyType;
        }
        if (IsUncertainExpression(objectNode) || IsUnsupportedType(objectType))
        {
            uncertainExpressions.Add(node);
            return anyType;
        }
        var apparentType = GetApparentType(GetNonNullableType(objectType));
        var indexFlags = Types.Flags(indexType);
        string? name = (indexFlags & TypeFlags.StringLiteral) != 0 ? (string)Types.Literal(indexType).Value!
            : (indexFlags & TypeFlags.NumberLiteral) != 0 ? JsNumber.ToString((double)Types.Literal(indexType).Value!)
            : null;
        if (name is not null)
        {
            var prop = GetPropertyOfType(apparentType, name);
            if (!prop.IsNone)
            {
                return GetTypeOfSymbol(prop);
            }
        }
        var isNumericIndex = (indexFlags & TypeFlags.NumberLike) != 0;
        if ((Types.Record(apparentType).ObjectFlags & ObjectFlags.Reference) != 0 && !Types.Object(apparentType).Target.IsNone)
        {
            var target = Types.Object(apparentType).Target;
            if ((target == globalArrayType || target == globalReadonlyArrayType) && isNumericIndex)
            {
                var typeArguments = GetTypeArguments(apparentType);
                if (typeArguments.Length > 0)
                {
                    return typeArguments[0];
                }
            }
            if (Types.Object(target).LocalTypeParameters.Count > 0 || Types.Object(target).OuterTypeParameters.Count > 0)
            {
                // the members of generic references are not instantiated before C3
                uncertainExpressions.Add(node);
                return anyType;
            }
        }
        if ((Types.Flags(apparentType) & TypeFlags.Object) != 0 && (indexFlags & (TypeFlags.StringLike | TypeFlags.NumberLike)) != 0)
        {
            IndexInfo? stringInfo = null;
            foreach (var info in GetIndexInfosOfType(apparentType))
            {
                var keyFlags = Types.Flags(info.KeyType);
                if ((keyFlags & TypeFlags.Number) != 0 && (isNumericIndex || (name is not null && IsNumericLiteralName(name))))
                {
                    return info.Type;
                }
                if ((keyFlags & TypeFlags.String) != 0)
                {
                    stringInfo = info;
                }
            }
            if (stringInfo is not null)
            {
                return stringInfo.Type;
            }
        }
        uncertainExpressions.Add(node);
        return anyType;
    }

    // checker.ts getApplicableIndexInfoForName: an index signature for string keys (or numbers for numeric names)
    private bool HasApplicableIndexInfoForName(Type type, string name)
    {
        foreach (var info in GetIndexInfosOfType(type))
        {
            var keyFlags = Types.Flags(info.KeyType);
            if ((keyFlags & (TypeFlags.String | TypeFlags.Any)) != 0 || ((keyFlags & TypeFlags.Number) != 0 && IsNumericLiteralName(name)) ||
                (keyFlags & (TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0)
            {
                return true;
            }
        }
        return false;
    }

    private readonly HashSet<(NodeRef, Type)> nonExistentPropertyReports = [];
    // checker.ts reportNonexistentProperty (C1: object and primitive types; unions are left to the narrowing of C7, the
    // lib suggestions and unchecked JavaScript to C9)
    private void ReportNonexistentProperty(NodeRef propNode, Type containingType)
    {
        if ((Types.Flags(containingType) & (TypeFlags.Union | TypeFlags.Intersection | TypeFlags.Instantiable)) != 0)
        {
            return;
        }
        if (!nonExistentPropertyReports.Add((propNode, containingType)))
        {
            return;
        }
        var store = StoreOf(propNode);
        var propName = Utilities.DeclarationNameToString(store, propNode.Node);
        var escapedName = store.Texts.GetString(store.EscapedText(propNode.Node))!;
        DiagnosticMessageChain chain;
        ProgramRelatedInformation? relatedInfo = null;
        if (TypeHasStaticProperty(escapedName, containingType))
        {
            var typeName = TypeToString(containingType);
            chain = ProgramDiagnostics.ChainDiagnosticMessages(null, Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_to_access_the_static_member_2_instead, propName, typeName, typeName + "." + propName);
        }
        else
        {
            var promisedType = GetPromisedTypeOfPromise(containingType);
            if (!promisedType.IsNone && !GetPropertyOfType(promisedType, escapedName).IsNone)
            {
                chain = ProgramDiagnostics.ChainDiagnosticMessages(null, Diagnostics.Property_0_does_not_exist_on_type_1, propName, TypeToString(containingType));
                relatedInfo = CreateRelatedForNode(propNode, Diagnostics.Did_you_forget_to_use_await);
            }
            else
            {
                var container = TypeToString(containingType);
                var suggestion = GetSuggestedSymbolForNonexistentProperty(escapedName, containingType);
                if (!suggestion.IsNone)
                {
                    var suggestedName = SymbolName(suggestion);
                    chain = ProgramDiagnostics.ChainDiagnosticMessages(null, Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_2, propName, container, suggestedName);
                    var suggestionValueDeclaration = ValueDeclarationOf(suggestion);
                    if (!suggestionValueDeclaration.IsNone)
                    {
                        relatedInfo = CreateRelatedForNode(suggestionValueDeclaration, Diagnostics._0_is_declared_here, suggestedName);
                    }
                }
                else
                {
                    chain = ProgramDiagnostics.ChainDiagnosticMessages(null, Diagnostics.Property_0_does_not_exist_on_type_1, propName, container);
                }
            }
        }
        var diagnostic = CreateDiagnosticForNode(propNode, Diagnostics.Property_0_does_not_exist_on_type_1, propName, "");
        diagnostic.Code = chain.Code;
        diagnostic.Category = chain.Category;
        diagnostic.MessageText = chain.MessageText;
        if (relatedInfo is not null)
        {
            diagnostic.RelatedInformation = [relatedInfo];
        }
        diagnostics.Add(diagnostic);
    }

    // checker.ts typeHasStaticProperty
    private bool TypeHasStaticProperty(string propName, Type containingType)
    {
        var symbol = Types.Symbol(containingType);
        if (symbol.IsNone)
        {
            return false;
        }
        var prop = GetPropertyOfType(GetTypeOfSymbol(symbol), propName);
        if (prop.IsNone)
        {
            return false;
        }
        var valueDeclaration = ValueDeclarationOf(prop);
        return !valueDeclaration.IsNone && Utilities.IsStatic(StoreOf(valueDeclaration), valueDeclaration.Node);
    }

    // checker.ts getPromisedTypeOfPromise (a reference to the global Promise type: its type argument)
    private Type GetPromisedTypeOfPromise(Type type)
    {
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.Reference) == 0 || deferredGlobalPromiseType.IsNone)
        {
            return Type.None;
        }
        var target = Types.Object(type).Target;
        return target == deferredGlobalPromiseType ? GetTypeArguments(type)[0] : Type.None;
    }

    // checker.ts getSuggestedSymbolForNonexistentProperty (isValidPropertyAccessForCompletions: accessibility, C8)
    private SymbolRef GetSuggestedSymbolForNonexistentProperty(string name, Type containingType) =>
        GetSpellingSuggestionForName(Utilities.UnescapeLeadingUnderscores(name), GetPropertiesOfType(containingType), SymbolFlags.Value);

    // checker.ts lookupSymbolForPrivateIdentifierDeclaration: the containing classes from the inside out; the member whose
    // declaration name is this private identifier (the binder names it after the class's symbol id)
    private SymbolRef LookupSymbolForPrivateIdentifierDeclaration(NodeRef propName)
    {
        var store = StoreOf(propName);
        var text = store.Texts.Span(store.EscapedText(propName.Node)).ToString();
        for (var containingClass = Utilities.GetContainingClass(store, propName.Node); !containingClass.IsNone; containingClass = Utilities.GetContainingClass(store, containingClass))
        {
            var classSymbol = GetSymbolOfDeclaration(At(propName, containingClass));
            foreach (var table in (SymTable?[])[MembersOf(classSymbol), ExportsOf(classSymbol)])
            {
                if (table is null)
                {
                    continue;
                }
                for (var i = 0; i < table.Count; i++)
                {
                    var member = table.ValueAt(i);
                    var declaration = ValueDeclarationOf(member);
                    if (declaration.IsNone)
                    {
                        continue;
                    }
                    var ds = StoreOf(declaration);
                    var memberName = Utilities.DeclarationNameField(ds, declaration.Node);
                    if (!memberName.IsNone && ds.Kind(memberName) == SyntaxKind.PrivateIdentifier && ds.Texts.Equals(ds.EscapedText(memberName), text))
                    {
                        return member;
                    }
                }
            }
        }
        return SymbolRef.None;
    }

    // checker.ts checkSuperExpression (C1: the types; the diagnostics of invalid super uses: TODO(C5)): in a call and in
    // static members the base constructor type, elsewhere the base class instance type
    private Type CheckSuperExpressionForMembers(NodeRef node)
    {
        var store = StoreOf(node);
        var parent = store.Parent(node.Node);
        var isCallExpression = store.Kind(parent) == SyntaxKind.CallExpression && store.Expression(parent) == node.Node;
        var container = Utilities.GetThisContainer(store, node.Node, includeArrowFunctions: true, includeClassComputedPropertyName: false);
        // skip arrow functions
        while (!container.IsNone && store.Kind(container) == SyntaxKind.ArrowFunction)
        {
            container = Utilities.GetThisContainer(store, container, includeArrowFunctions: true, includeClassComputedPropertyName: false);
        }
        var classDecl = Utilities.GetContainingClass(store, node.Node);
        if (classDecl.IsNone)
        {
            return errorType;
        }
        var classType = GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration(At(node, classDecl)));
        var baseTypes = GetBaseTypes(classType);
        if (baseTypes.Count == 0)
        {
            return errorType;
        }
        return isCallExpression || (!container.IsNone && Utilities.IsStatic(store, container)) ? GetBaseConstructorTypeOfClass(classType) : baseTypes[0];
    }

    /// <summary>checker.ts checkQualifiedName (TODO(C5): diagnostics).</summary>
    internal Type CheckQualifiedName(NodeRef node)
    {
        var store = StoreOf(node);
        var left = At(node, store.Left(node.Node));
        var leftType = Utilities.IsPartOfTypeQuery(store, node.Node) && Utilities.IsThisIdentifier(store, left.Node) ? CheckThisExpression(left) : CheckEntityNameExpressionForTypeQuery(left);
        return CheckPropertyAccessOnType(node, leftType, At(node, store.Right(node.Node)));
    }

    // TODO(C5): resolveCall / overload resolution. C1: the return type of the first signature of the callee (calls to
    // non-generic declarations with a declared return type), any for untyped calls.
    private Type CheckCallExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var isNew = store.Kind(node.Node) == SyntaxKind.NewExpression;
        var callee = At(node, store.Expression(node.Node));
        Type funcType;
        if (store.Kind(callee.Node) is SyntaxKind.SuperKeyword or SyntaxKind.ImportKeyword)
        {
            funcType = errorType;
        }
        else
        {
            funcType = CheckExpressionCached(callee);
        }
        var arguments = store.Arguments(node.Node);
        if (!arguments.IsNone)
        {
            foreach (var argument in store.Nodes(arguments))
            {
                CheckExpressionCached(At(node, argument));
            }
        }
        if (store.Kind(callee.Node) == SyntaxKind.SuperKeyword)
        {
            // checkCallExpression: a super call is of type void (TODO(C5): resolveCallExpression's super checks)
            return voidType;
        }
        if (store.Kind(callee.Node) == SyntaxKind.ImportKeyword)
        {
            // checkImportCallExpression: the module reference (TODO(C5): the Promise of the module type)
            if (!arguments.IsNone && store.Length(arguments) > 0 && Utilities.IsStringLiteralLike(store, store.Element(arguments, 0)))
            {
                ResolveExternalModuleName(node, At(node, store.Element(arguments, 0)));
            }
            return anyType;
        }
        if (IsErrorType(funcType))
        {
            return errorType;
        }
        if ((Types.Flags(funcType) & TypeFlags.Any) != 0)
        {
            return anyType;
        }
        var signatures = GetSignaturesOfType(funcType, isNew ? SignatureKind.Construct : SignatureKind.Call);
        if (signatures.Count == 0)
        {
            return errorType;
        }
        var typeArgumentNodes = store.TypeArguments(node.Node);
        if (!typeArgumentNodes.IsNone && store.Length(typeArgumentNodes) > 0 && !IsUncertainExpression(callee))
        {
            // explicit type arguments: the first signature of that arity, its return type instantiated where C1 can
            var typeArguments = new List<Type>();
            foreach (var argumentNode in store.Nodes(typeArgumentNodes))
            {
                typeArguments.Add(GetTypeFromTypeNode(At(node, argumentNode)));
            }
            var candidates = signatures.FindAll(s => Signatures.Record(s).TypeParameters.Count == typeArguments.Count);
            if (candidates.Count > 0)
            {
                var candidateTypeParameters = Types.Elements(Signatures.Record(candidates[0]).TypeParameters).ToArray();
                var instantiated = InstantiateSimple(GetReturnTypeOfSignature(candidates[0]), candidateTypeParameters, typeArguments, 0);
                if (!instantiated.IsNone)
                {
                    if (candidates.Count > 1)
                    {
                        uncertainExpressions.Add(node);
                    }
                    return instantiated;
                }
            }
        }
        var signature = signatures[0];
        if (signatures.Count > 1 || Signatures.Record(signature).TypeParameters.Count > 0 || IsUncertainExpression(callee))
        {
            // TODO(C5/C6): overload resolution and the inference of type arguments. C1 takes the first signature's
            // return type (its members name the symbols an instantiation would) and marks the result as uncertain, so
            // no diagnostics are derived from it.
            uncertainExpressions.Add(node);
        }
        return GetReturnTypeOfSignature(signature);
    }

    // A minimal instantiation of a type for explicit type arguments (TODO(C3): instantiateType). None when the type has
    // a shape C1 cannot instantiate (anonymous and other structured types).
    private Type InstantiateSimple(Type type, Type[] typeParameters, List<Type> typeArguments, int depth)
    {
        if (depth > 8)
        {
            return Type.None;
        }
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.TypeParameter) != 0)
        {
            var index = Array.IndexOf(typeParameters, type);
            return index >= 0 ? typeArguments[index] : Types.TypeParameter(type).IsThisType ? type : Type.None;
        }
        if ((flags & TypeFlags.Union) != 0)
        {
            var members = Types.Elements(Types.Union(type).Types).ToArray();
            var mapped = new Type[members.Length];
            for (var i = 0; i < members.Length; i++)
            {
                mapped[i] = InstantiateSimple(members[i], typeParameters, typeArguments, depth + 1);
                if (mapped[i].IsNone)
                {
                    return Type.None;
                }
            }
            return GetUnionType(mapped);
        }
        if ((flags & TypeFlags.Object) != 0)
        {
            var objectFlags = Types.Record(type).ObjectFlags;
            if ((objectFlags & ObjectFlags.Reference) != 0 && !Types.Object(type).Target.IsNone)
            {
                var target = Types.Object(type).Target;
                if (target == type)
                {
                    return type;
                }
                var arguments = GetTypeArguments(type).ToArray();
                var mapped = new Type[arguments.Length];
                for (var i = 0; i < arguments.Length; i++)
                {
                    mapped[i] = InstantiateSimple(arguments[i], typeParameters, typeArguments, depth + 1);
                    if (mapped[i].IsNone)
                    {
                        return Type.None;
                    }
                }
                return CreateTypeReference(target, mapped);
            }
            if ((objectFlags & ObjectFlags.ClassOrInterface) != 0 && Types.Object(type).LocalTypeParameters.Count == 0 && Types.Object(type).OuterTypeParameters.Count == 0)
            {
                return type;
            }
            return Type.None;
        }
        if ((flags & (TypeFlags.Intersection | TypeFlags.Instantiable)) != 0)
        {
            return Type.None;
        }
        return type;
    }

    // Expressions whose C1 type is an approximation (calls without overload resolution and inference, and what is
    // derived from them): their members resolve symbols, but no diagnostics are reported on them.
    private readonly HashSet<NodeRef> uncertainExpressions = [];
    private readonly HashSet<SymbolRef> uncertainSymbols = [];

    private bool IsUncertainExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        while (store.Kind(n) is SyntaxKind.ParenthesizedExpression or SyntaxKind.NonNullExpression)
        {
            n = store.Expression(n);
        }
        return uncertainExpressions.Contains(At(node, n));
    }

    // TODO(C5): checkBinaryLikeExpression. C1: the result types of the operators that do not depend on contextual or
    // narrowed types.
    private Type CheckBinaryExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var op = store.Kind(store.OperatorToken(node.Node));
        var leftType = CheckExpression(At(node, store.Left(node.Node)));
        var rightType = CheckExpression(At(node, store.Right(node.Node)));
        switch (op)
        {
            case SyntaxKind.CommaToken:
            case SyntaxKind.EqualsToken:
                return rightType;
            case SyntaxKind.LessThanToken:
            case SyntaxKind.GreaterThanToken:
            case SyntaxKind.LessThanEqualsToken:
            case SyntaxKind.GreaterThanEqualsToken:
            case SyntaxKind.EqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsToken:
            case SyntaxKind.EqualsEqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsEqualsToken:
            case SyntaxKind.InstanceOfKeyword:
            case SyntaxKind.InKeyword:
                return booleanType;
            case SyntaxKind.PlusToken:
            case SyntaxKind.PlusEqualsToken:
                if ((Types.Flags(leftType) & TypeFlags.StringLike) != 0 || (Types.Flags(rightType) & TypeFlags.StringLike) != 0)
                {
                    return stringType;
                }
                if ((Types.Flags(leftType) & TypeFlags.NumberLike) != 0 && (Types.Flags(rightType) & TypeFlags.NumberLike) != 0)
                {
                    return numberType;
                }
                if ((Types.Flags(leftType) & TypeFlags.BigIntLike) != 0 && (Types.Flags(rightType) & TypeFlags.BigIntLike) != 0)
                {
                    return bigintType;
                }
                return (Types.Flags(leftType) & TypeFlags.Any) != 0 || (Types.Flags(rightType) & TypeFlags.Any) != 0 ? anyType : errorType;
            case SyntaxKind.MinusToken:
            case SyntaxKind.AsteriskToken:
            case SyntaxKind.AsteriskAsteriskToken:
            case SyntaxKind.SlashToken:
            case SyntaxKind.PercentToken:
            case SyntaxKind.LessThanLessThanToken:
            case SyntaxKind.GreaterThanGreaterThanToken:
            case SyntaxKind.GreaterThanGreaterThanGreaterThanToken:
            case SyntaxKind.AmpersandToken:
            case SyntaxKind.BarToken:
            case SyntaxKind.CaretToken:
            case SyntaxKind.MinusEqualsToken:
            case SyntaxKind.AsteriskEqualsToken:
            case SyntaxKind.AsteriskAsteriskEqualsToken:
            case SyntaxKind.SlashEqualsToken:
            case SyntaxKind.PercentEqualsToken:
            case SyntaxKind.LessThanLessThanEqualsToken:
            case SyntaxKind.GreaterThanGreaterThanEqualsToken:
            case SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken:
            case SyntaxKind.AmpersandEqualsToken:
            case SyntaxKind.BarEqualsToken:
            case SyntaxKind.CaretEqualsToken:
                return (Types.Flags(leftType) & TypeFlags.BigIntLike) != 0 && (Types.Flags(rightType) & TypeFlags.BigIntLike) != 0 ? bigintType : numberType;
            default:
                // (&&, ||, ??, and their assignments: C5/C7)
                return GetUnionType([leftType, rightType], UnionReduction.Subtype);
        }
    }

    // TODO(C5): getReturnTypeFromBody (async and generator functions, contextual return types, never/void inference).
    // C1: void without returned expressions, else the union of the widened types of the returned expressions.
    private Type GetReturnTypeFromBody(NodeRef func)
    {
        var store = StoreOf(func);
        var body = store.Body(func.Node);
        if (body.IsNone)
        {
            return errorType;
        }
        var isAsync = Utilities.HasSyntacticModifierUncached(store, func.Node, ModifierFlags.Async);
        if (!store.AsteriskToken(func.Node).IsNone)
        {
            return GetGeneratorReturnTypeFromBody(func, isAsync);
        }
        Type returnType;
        if (store.Kind(body) != SyntaxKind.Block)
        {
            returnType = CheckExpressionCached(At(func, body));
            if (IsConstContext(At(func, body)))
            {
                returnType = GetRegularTypeOfLiteralType(returnType);
            }
        }
        else
        {
            // checkAndAggregateReturnExpressionTypes (functionHasImplicitReturn: the binder's end flow node; reachability: C7)
            var types = new List<Type>();
            var hasReturnWithNoExpression = !BindingOf(func.File).EndFlowNodeOf(func.Node).IsNone;
            var hasReturnOfTypeNever = false;
            ForEachReturnStatement(store, body, statement =>
            {
                var expression = store.Expression(statement);
                if (expression.IsNone)
                {
                    hasReturnWithNoExpression = true;
                    return;
                }
                var type = CheckExpressionCached(At(func, expression));
                if ((Types.Flags(type) & TypeFlags.Never) != 0)
                {
                    hasReturnOfTypeNever = true;
                }
                if (!types.Contains(type))
                {
                    types.Add(type);
                }
            });
            if (types.Count == 0 && !hasReturnWithNoExpression && (hasReturnOfTypeNever || MayReturnNever(store, func.Node)))
            {
                return isAsync ? CreatePromiseType(neverType) : neverType;
            }
            if (types.Count == 0)
            {
                return isAsync ? CreatePromiseType(voidType) : voidType;
            }
            if (strictNullChecks && hasReturnWithNoExpression && !types.Contains(undefinedType))
            {
                types.Add(undefinedType);
            }
            returnType = GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(types), UnionReduction.Subtype);
        }
        if (IsUnitType(returnType))
        {
            // getWidenedLiteralLikeTypeForContextualReturnTypeIfNeeded (contextual literal return types: C5)
            returnType = GetRegularTypeOfLiteralType(GetWidenedLiteralType(returnType));
        }
        returnType = GetWidenedType(returnType);
        return isAsync ? CreatePromiseType(returnType) : returnType;
    }

    // getReturnTypeFromBody for generators (C1: the yielded operand types; yield* delegation, contextual iteration types
    // and async awaiting are TODO(C5/C6), and functions using yield* are typed any)
    private Type GetGeneratorReturnTypeFromBody(NodeRef func, bool isAsync)
    {
        var store = StoreOf(func);
        var body = store.Body(func.Node);
        if (store.Kind(body) != SyntaxKind.Block || HasContextualType(func))
        {
            return anyType;
        }
        // checkAndAggregateReturnExpressionTypes
        var returnTypes = new List<Type>();
        var hasReturnWithNoExpression = !BindingOf(func.File).EndFlowNodeOf(func.Node).IsNone;
        var hasReturnOfTypeNever = false;
        ForEachReturnStatement(store, body, statement =>
        {
            var expression = store.Expression(statement);
            if (expression.IsNone)
            {
                hasReturnWithNoExpression = true;
                return;
            }
            var type = CheckExpressionCached(At(func, expression));
            if ((Types.Flags(type) & TypeFlags.Never) != 0)
            {
                hasReturnOfTypeNever = true;
            }
            if (!returnTypes.Contains(type))
            {
                returnTypes.Add(type);
            }
        });
        var fallbackReturnType = voidType;
        Type returnType = Type.None;
        if (returnTypes.Count == 0 && !hasReturnWithNoExpression && (hasReturnOfTypeNever || MayReturnNever(store, func.Node)))
        {
            fallbackReturnType = neverType;
        }
        else if (returnTypes.Count > 0)
        {
            if (strictNullChecks && hasReturnWithNoExpression && !returnTypes.Contains(undefinedType))
            {
                returnTypes.Add(undefinedType);
            }
            returnType = GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(returnTypes), UnionReduction.Subtype);
        }
        // checkAndAggregateYieldOperandTypes
        var yieldTypes = new List<Type>();
        var delegates = false;
        ForEachYieldExpression(store, body, yield =>
        {
            if (!store.AsteriskToken(yield).IsNone)
            {
                delegates = true;
                return;
            }
            var operand = store.Expression(yield);
            var type = operand.IsNone ? undefinedWideningType : CheckExpressionCached(At(func, operand));
            if (!yieldTypes.Contains(type))
            {
                yieldTypes.Add(type);
            }
        });
        if (delegates || (isAsync && yieldTypes.Exists(t => (Types.Flags(t) & TypeFlags.Object) != 0)))
        {
            return anyType;
        }
        var yieldType = yieldTypes.Count > 0 ? GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(yieldTypes), UnionReduction.Subtype) : Type.None;
        if (!yieldType.IsNone && IsUnitType(yieldType))
        {
            yieldType = GetRegularTypeOfLiteralType(GetWidenedLiteralType(yieldType));
        }
        if (!returnType.IsNone && IsUnitType(returnType))
        {
            returnType = GetRegularTypeOfLiteralType(GetWidenedLiteralType(returnType));
        }
        if (!yieldType.IsNone)
        {
            yieldType = GetWidenedType(yieldType);
        }
        if (!returnType.IsNone)
        {
            returnType = GetWidenedType(returnType);
        }
        return CreateGeneratorType(yieldType.IsNone ? neverType : yieldType, returnType.IsNone ? fallbackReturnType : returnType, unknownType, isAsync);
    }

    // checker.ts createGeneratorType
    private Type CreateGeneratorType(Type yieldType, Type returnType, Type nextType, bool isAsync)
    {
        var generator = GetGlobalType(isAsync ? "AsyncGenerator" : "Generator", 3, reportErrors: false);
        if (generator.IsNone || generator == emptyGenericType)
        {
            var iterableIterator = GetGlobalType(isAsync ? "AsyncIterableIterator" : "IterableIterator", 3, reportErrors: false);
            if (!iterableIterator.IsNone && iterableIterator != emptyGenericType)
            {
                return CreateTypeReference(iterableIterator, [yieldType, returnType, nextType]);
            }
            GetGlobalType(isAsync ? "AsyncIterableIterator" : "IterableIterator", 3, reportErrors: true);
            return emptyObjectType;
        }
        return CreateTypeReference(generator, [yieldType, returnType, nextType]);
    }

    // utilities.ts forEachYieldExpression
    private static void ForEachYieldExpression(NodeStore store, Node body, Action<Node> visitor)
    {
        Traverse(body);

        void Traverse(Node node)
        {
            switch (store.Kind(node))
            {
                case SyntaxKind.YieldExpression:
                    visitor(node);
                    var operand = store.Expression(node);
                    if (!operand.IsNone)
                    {
                        Traverse(operand);
                    }
                    return;
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.TypeAliasDeclaration:
                    return;
            }
            if (Utilities.IsFunctionLike(store, node))
            {
                var name = store.Kind(node) is SyntaxKind.ArrowFunction or SyntaxKind.Constructor ? Node.None : store.Name(node);
                if (!name.IsNone && store.Kind(name) == SyntaxKind.ComputedPropertyName)
                {
                    Traverse(store.Expression(name));
                }
                return;
            }
            if (!Utilities.IsPartOfTypeNode(store, node))
            {
                NodeVisitor.ForEachChild<object>(store, node, child =>
                {
                    Traverse(child);
                    return null;
                });
            }
        }
    }

    // checker.ts checkYieldExpression (C1: the next type of generators without a return type annotation and contextual
    // type, TODO(C5/C6): annotated and contextually typed generators, yield* and the assignability checks)
    private Type CheckYieldExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var operand = store.Expression(n);
        if (!operand.IsNone)
        {
            CheckExpression(At(node, operand));
        }
        var func = Utilities.GetContainingFunctionOrNone(store, n);
        if (func.IsNone || store.AsteriskToken(func).IsNone)
        {
            return anyType;
        }
        if (!store.AsteriskToken(n).IsNone || !store.Type(func).IsNone || HasContextualType(At(node, func)))
        {
            uncertainExpressions.Add(node);
            return anyType;
        }
        if (noImplicitAny && !ExpressionResultIsUnused(store, n) && GetApproximateContextualType(node, 0).IsNone)
        {
            Error(node, Diagnostics.yield_expression_implicitly_results_in_an_any_type_because_its_containing_generator_lacks_a_return_type_annotation);
        }
        return anyType;
    }

    // utilities.ts expressionResultIsUnused
    private static bool ExpressionResultIsUnused(NodeStore store, Node node)
    {
        while (true)
        {
            var parent = store.Parent(node);
            switch (store.Kind(parent))
            {
                case SyntaxKind.ParenthesizedExpression:
                    node = parent;
                    continue;
                case SyntaxKind.ExpressionStatement:
                case SyntaxKind.VoidExpression:
                    return true;
                case SyntaxKind.ForStatement:
                    return store.Initializer(parent) == node || store.Incrementor(parent) == node;
                case SyntaxKind.BinaryExpression when store.Kind(store.OperatorToken(parent)) == SyntaxKind.CommaToken:
                    if (store.Left(parent) == node)
                    {
                        return true;
                    }
                    node = parent;
                    continue;
                default:
                    return false;
            }
        }
    }

    // checker.ts mayReturnNever
    private static bool MayReturnNever(NodeStore store, Node func) => store.Kind(func) switch
    {
        SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction => true,
        SyntaxKind.MethodDeclaration => store.Kind(store.Parent(func)) == SyntaxKind.ObjectLiteralExpression,
        _ => false,
    };

    // checker.ts isUnitType
    private bool IsUnitType(Type type) => (Types.Flags(type) & TypeFlags.Unit) != 0;

    private Type deferredGlobalPromiseType;

    // checker.ts createPromiseType (TODO(C5): getAwaitedType of the promised type)
    private Type CreatePromiseType(Type promisedType)
    {
        if (deferredGlobalPromiseType.IsNone)
        {
            var t = GetGlobalType("Promise", 1, reportErrors: true);
            deferredGlobalPromiseType = t.IsNone ? emptyGenericType : t;
        }
        return deferredGlobalPromiseType != emptyGenericType ? CreateTypeReference(deferredGlobalPromiseType, [promisedType]) : unknownType;
    }

    // utilities.ts forEachReturnStatement
    private static void ForEachReturnStatement(NodeStore store, Node body, Action<Node> visitor)
    {
        Traverse(body);

        void Traverse(Node node)
        {
            switch (store.Kind(node))
            {
                case SyntaxKind.ReturnStatement:
                    visitor(node);
                    return;
                case SyntaxKind.CaseBlock:
                case SyntaxKind.Block:
                case SyntaxKind.IfStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                case SyntaxKind.WithStatement:
                case SyntaxKind.SwitchStatement:
                case SyntaxKind.CaseClause:
                case SyntaxKind.DefaultClause:
                case SyntaxKind.LabeledStatement:
                case SyntaxKind.TryStatement:
                case SyntaxKind.CatchClause:
                    NodeVisitor.ForEachChild<object>(store, node, child =>
                    {
                        Traverse(child);
                        return null;
                    });
                    return;
            }
        }
    }
}
