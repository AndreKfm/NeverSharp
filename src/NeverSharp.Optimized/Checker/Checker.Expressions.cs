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
                CheckExpression(At(node, store.Expression(n)));
                CheckExpression(At(node, store.ArgumentExpression(n)));
                return anyType; // TODO(C5): checkIndexedAccess
            case SyntaxKind.OmittedExpression:
                return undefinedWideningType;
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
        return HasContextualType(func) ? anyType : Type.None;
    }

    // Whether an expression has a contextual type that provides a signature (C1 heuristic for getContextualSignature).
    private bool HasContextualType(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var parent = store.Parent(n);
        switch (store.Kind(parent))
        {
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.Parameter:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
                return store.Initializer(parent) == n && !store.Type(parent).IsNone;
            case SyntaxKind.BindingElement:
                return false;
            case SyntaxKind.ReturnStatement:
            case SyntaxKind.YieldExpression:
            {
                var func = Utilities.GetContainingFunctionOrNone(store, parent);
                return !func.IsNone && (!store.Type(func).IsNone || (IsContextSensitiveFunctionOrObjectLiteralMethod(store, func) && HasContextualType(At(node, func))));
            }
            case SyntaxKind.ArrowFunction:
                return store.Body(parent) == n && (!store.Type(parent).IsNone || HasContextualType(At(node, parent)));
            case SyntaxKind.ObjectLiteralExpression:
                // a method of an object literal
                return HasContextualType(At(node, parent));
            case SyntaxKind.ParenthesizedExpression:
                return HasContextualType(At(node, parent));
            case SyntaxKind.ConditionalExpression:
                return store.Condition(parent) != n && HasContextualType(At(node, parent));
            case SyntaxKind.BinaryExpression:
            {
                var op = store.Kind(store.OperatorToken(parent));
                if (op == SyntaxKind.EqualsToken && store.Right(parent) == n)
                {
                    var leftType = CheckExpressionCached(At(node, store.Left(parent)));
                    return HasSignatures(leftType);
                }
                if (op is SyntaxKind.BarBarToken or SyntaxKind.QuestionQuestionToken or SyntaxKind.AmpersandAmpersandToken or SyntaxKind.CommaToken)
                {
                    return (op == SyntaxKind.CommaToken ? store.Right(parent) == n : true) && HasContextualType(At(node, parent));
                }
                return false;
            }
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.ShorthandPropertyAssignment:
                return HasContextualType(At(node, store.Parent(parent)));
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.SpreadElement:
                return HasContextualType(At(node, parent));
            case SyntaxKind.AsExpression:
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.SatisfiesExpression:
                return !Utilities.IsConstAssertion(store, parent);
            case SyntaxKind.CallExpression:
            case SyntaxKind.NewExpression:
            {
                var arguments = store.Arguments(parent);
                if (arguments.IsNone || !store.Nodes(arguments).Contains(n))
                {
                    return false;
                }
                var calleeType = CheckExpressionCached(At(node, store.Expression(parent)));
                return HasSignatures(calleeType, store.Kind(parent) == SyntaxKind.NewExpression ? SignatureKind.Construct : SignatureKind.Call);
            }
            case SyntaxKind.Decorator:
            case SyntaxKind.JsxExpression:
            case SyntaxKind.JsxAttribute:
                return true;
            case SyntaxKind.ExportAssignment:
                return false;
            default:
                return false;
        }
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
        if (store.Kind(right.Node) != SyntaxKind.Identifier)
        {
            return errorType;
        }
        var name = store.Texts.GetString(store.EscapedText(right.Node))!;
        if (IsErrorType(leftType) || (Types.Flags(leftType) & TypeFlags.Any) != 0)
        {
            return leftType;
        }
        var apparentType = GetApparentType(GetNonNullableType(leftType));
        var prop = GetPropertyOfType(apparentType, name);
        if (prop.IsNone)
        {
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

    // `super.x`: the base class instance type or constructor type (C1 subset of checkSuperExpression)
    private Type CheckSuperExpressionForMembers(NodeRef node)
    {
        var store = StoreOf(node);
        var container = Utilities.GetThisContainer(store, node.Node, includeArrowFunctions: false, includeClassComputedPropertyName: false);
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
        return !container.IsNone && Utilities.IsStatic(store, container) ? GetBaseConstructorTypeOfClass(classType) : baseTypes[0];
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
        var signature = signatures[0];
        if (Signatures.Record(signature).TypeParameters.Count > 0)
        {
            // TODO(C6): inference of type arguments
            var returnType = GetReturnTypeOfSignature(signature);
            return (Types.Flags(returnType) & TypeFlags.TypeParameter) != 0 ? unknownType : returnType;
        }
        return GetReturnTypeOfSignature(signature);
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
            return anyType; // TODO(C5): generator return types
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
