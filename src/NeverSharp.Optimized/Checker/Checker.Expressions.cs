// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (module C5; the functions are listed below)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (details below).
// Functions (checkExpression, checkExpressionWorker, checkExpressionCached,
// getTypeOfExpression, getQuickTypeOfExpression, getContextFreeTypeOfExpression, checkExpressionWithContextualType,
// checkExpressionForMutableLocation, isConstContext, instantiateTypeWithSingleGenericCallSignature, checkConstEnumAccess,
// the literal, template, parenthesized, assertion, satisfies, non-null, typeof, void, delete, meta property, spread
// and synthetic expressions, checkIdentifier, checkThisExpression, tryGetThisTypeAt, checkSuperExpression,
// checkPrivateIdentifierExpression)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; modules C1 (subset) and C5. TODO(C7): the flow types of references
// (Checker.FlowSeam.cs). Module C9b: JSX expressions (Checker.Jsx.cs). Module C9d: JSDoc assertions and satisfies tags,
// this in JavaScript constructor functions and prototype methods.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>The node links the expression checker keeps (checker.ts NodeLinks: resolvedType, contextFreeType, ...).</summary>
internal struct ExpressionNodeLinks
{
    public Type ResolvedType;            // checkExpressionCached
    public Type ContextFreeType;         // getContextFreeTypeOfExpression, context free function types
    public Type AssertionExpressionType; // checkAssertionDeferred
    public bool SpreadIndicesComputed;
    public int FirstSpreadIndex;         // -1: none
    public int LastSpreadIndex;
    public HashSet<string>? NonExistentPropCheckCache;
    public bool TypeChecked;             // NodeCheckFlags.TypeChecked of regular expression literals
}

internal sealed partial class Checker
{
    private NodeLinkStore<ExpressionNodeLinks>? expressionLinks;

    private NodeLinkStore<ExpressionNodeLinks> ExpressionLinks => expressionLinks ??= new NodeLinkStore<ExpressionNodeLinks>(program.SourceFiles.Count);

    // checker.ts contextualTypeNodes / contextualTypes / contextualIsCache
    private NodeRef[] contextualTypeNodes = new NodeRef[16];
    private Type[] contextualTypes = new Type[16];
    private bool[] contextualIsCache = new bool[16];
    private int contextualTypeCount;

    // checker.ts inferenceContextNodes / inferenceContexts
    private NodeRef[] inferenceContextNodes = new NodeRef[16];
    private InferenceContext?[] inferenceContexts = new InferenceContext?[16];
    private int inferenceContextCount;

    // checker.ts contextualBindingPatterns
    private readonly List<NodeRef> contextualBindingPatterns = [];

    /// <summary>checker.ts checkExpressionCached.</summary>
    internal Type CheckExpressionCached(NodeRef node, CheckMode checkMode = CheckMode.Normal)
    {
        if (checkMode != CheckMode.Normal)
        {
            return CheckExpression(node, checkMode);
        }
        if (ExpressionLinks.Has(node) && !ExpressionLinks.Get(node).ResolvedType.IsNone)
        {
            return ExpressionLinks.Get(node).ResolvedType;
        }
        // When computing a type that we're going to cache, we need to ignore any ongoing control flow
        // analysis because variables may have transient types in indeterminable states. Moving flowLoopStart
        // to the top of the stack ensures all transient types are computed from a known point.
        var saveFlowLoopStart = flowLoopStart;
        var saveFlowTypeCache = flowTypeCache;
        flowLoopStart = flowLoopCount;
        flowTypeCache = null;
        var type = CheckExpression(node, checkMode);
        flowTypeCache = saveFlowTypeCache;
        flowLoopStart = saveFlowLoopStart;
        ref var links = ref ExpressionLinks.Get(node);
        if (links.ResolvedType.IsNone)
        {
            links.ResolvedType = type;
        }
        return links.ResolvedType;
    }

    /// <summary>checker.ts getTypeOfExpression.</summary>
    internal Type GetTypeOfExpression(NodeRef node)
    {
        // Don't bother caching types that require no flow analysis and are quick to compute.
        var quickType = GetQuickTypeOfExpression(node);
        if (!quickType.IsNone)
        {
            return quickType;
        }
        // If a type has been cached for the node, return it.
        if (flowTypeCache is not null && flowTypeCache.TryGetValue(node, out var cachedType))
        {
            return cachedType;
        }
        var startInvocationCount = flowInvocationCount;
        var type = CheckExpression(node, CheckMode.TypeOnly);
        // If control flow analysis was required to determine the type, it is worth caching.
        if (flowInvocationCount != startInvocationCount)
        {
            (flowTypeCache ??= [])[node] = type;
        }
        return type;
    }

    // checker.ts getQuickTypeOfExpression
    private Type GetQuickTypeOfExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var expr = At(node, Utilities.SkipParentheses(store, node.Node, true));
        if (FactoryUtilities.IsJSDocTypeAssertion(store, expr.Node))
        {
            var assertionType = GetJSDocTypeAssertionType(expr);
            if (!IsConstTypeReference(store, assertionType.Node))
            {
                return GetTypeFromTypeNode(assertionType);
            }
        }
        expr = At(node, Utilities.SkipParentheses(store, node.Node));
        var kind = store.Kind(expr.Node);
        if (kind == SyntaxKind.AwaitExpression)
        {
            var type = GetQuickTypeOfExpression(At(node, store.Expression(expr.Node)));
            return type.IsNone ? Type.None : GetAwaitedType(type);
        }
        // Optimize for the common case of a call to a function with a single non-generic call
        // signature where we can just fetch the return type without checking the arguments.
        if (kind == SyntaxKind.CallExpression && store.Kind(store.Expression(expr.Node)) != SyntaxKind.SuperKeyword && !IsRequireCall(expr, requireStringLiteralLikeArgument: true) &&
            !IsSymbolOrSymbolForCall(expr) && !Utilities.IsImportCall(store, expr.Node))
        {
            return UtilitiesPublic.IsCallChain(store, expr.Node) ? GetReturnTypeOfSingleNonGenericSignatureOfCallChain(expr)
                : GetReturnTypeOfSingleNonGenericCallSignature(CheckNonNullExpression(At(node, store.Expression(expr.Node))));
        }
        if (kind is SyntaxKind.AsExpression or SyntaxKind.TypeAssertionExpression && !IsConstTypeReference(store, store.Type(expr.Node)))
        {
            return GetTypeFromTypeNode(At(node, store.Type(expr.Node)));
        }
        var nodeKind = store.Kind(node.Node);
        if (UtilitiesPublic.IsLiteralKind(nodeKind) || nodeKind is SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword)
        {
            return CheckExpression(node);
        }
        return Type.None;
    }

    // checker.ts getReturnTypeOfSingleNonGenericCallSignature
    private Type GetReturnTypeOfSingleNonGenericCallSignature(Type funcType)
    {
        var signature = GetSingleCallSignature(funcType);
        return !signature.IsNone && Signatures.Record(signature).TypeParameters.Count == 0 ? GetReturnTypeOfSignature(signature) : Type.None;
    }

    // checker.ts getReturnTypeOfSingleNonGenericSignatureOfCallChain
    private Type GetReturnTypeOfSingleNonGenericSignatureOfCallChain(NodeRef expr)
    {
        var funcType = CheckExpression(At(expr, StoreOf(expr).Expression(expr.Node)));
        var nonOptionalType = GetOptionalExpressionType(funcType, At(expr, StoreOf(expr).Expression(expr.Node)));
        var returnType = GetReturnTypeOfSingleNonGenericCallSignature(funcType);
        return returnType.IsNone ? Type.None : PropagateOptionalTypeMarker(returnType, expr, nonOptionalType != funcType);
    }

    /// <summary>checker.ts getContextFreeTypeOfExpression.</summary>
    internal Type GetContextFreeTypeOfExpression(NodeRef node)
    {
        if (ExpressionLinks.Has(node) && !ExpressionLinks.Get(node).ContextFreeType.IsNone)
        {
            return ExpressionLinks.Get(node).ContextFreeType;
        }
        PushContextualType(node, anyType, isCache: false);
        var type = CheckExpression(node, CheckMode.SkipContextSensitive);
        ExpressionLinks.Get(node).ContextFreeType = type;
        PopContextualType();
        return type;
    }

    /// <summary>checker.ts checkExpression.</summary>
    internal Type CheckExpression(NodeRef node, CheckMode checkMode = CheckMode.Normal, bool forceTuple = false)
    {
        var saveCurrentNode = currentNode;
        currentNode = node;
        instantiationCount = 0;
        var uninstantiatedType = CheckExpressionWorker(node, checkMode, forceTuple);
        var type = InstantiateTypeWithSingleGenericCallSignature(node, uninstantiatedType, checkMode);
        if (IsConstEnumObjectType(type))
        {
            CheckConstEnumAccess(node, type);
        }
        currentNode = saveCurrentNode;
        return type;
    }

    // checker.ts checkConstEnumAccess (isolatedModules and verbatimModuleSyntax are not set in Gate C)
    private void CheckConstEnumAccess(NodeRef node, Type type)
    {
        _ = type;
        // enum object type for const enums are only permitted in:
        // - 'left' in property access
        // - 'object' in indexed access
        // - target in rhs of import statement
        var store = StoreOf(node);
        var parent = store.Parent(node.Node);
        var ok = (store.Kind(parent) == SyntaxKind.PropertyAccessExpression && store.Expression(parent) == node.Node) ||
            (store.Kind(parent) == SyntaxKind.ElementAccessExpression && store.Expression(parent) == node.Node) ||
            (store.Kind(node.Node) is SyntaxKind.Identifier or SyntaxKind.QualifiedName && IsInRightSideOfImportOrExportAssignment(store, node.Node)) ||
            (store.Kind(parent) == SyntaxKind.TypeQuery && store.ExprName(parent) == node.Node) ||
            store.Kind(parent) == SyntaxKind.ExportSpecifier; // We allow reexporting const enums
        if (!ok)
        {
            Error(node, Diagnostics.const_enums_can_only_be_used_in_property_or_index_access_expressions_or_the_right_hand_side_of_an_import_declaration_or_export_assignment_or_type_query);
        }
    }

    // checker.ts isConstEnumObjectType
    private bool IsConstEnumObjectType(Type type) =>
        (Types.ObjectFlagsOf(type) & ObjectFlags.Anonymous) != 0 && !Types.Symbol(type).IsNone && (SymbolFlagsOf(Types.Symbol(type)) & SymbolFlags.ConstEnum) != 0;

    private Type CheckExpressionWorker(NodeRef node, CheckMode checkMode, bool forceTuple)
    {
        var store = StoreOf(node);
        var n = node.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.Identifier:
                return CheckIdentifier(node, checkMode);
            case SyntaxKind.PrivateIdentifier:
                return CheckPrivateIdentifierExpression(node);
            case SyntaxKind.ThisKeyword:
                return CheckThisExpression(node);
            case SyntaxKind.SuperKeyword:
                return CheckSuperExpression(node);
            case SyntaxKind.NullKeyword:
                return nullWideningType;
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.StringLiteral:
                // (hasSkipDirectInferenceFlag: set by the language service only)
                return GetFreshTypeOfLiteralType(GetStringLiteralType(store.Texts.GetString(store.Text(n))!));
            case SyntaxKind.NumericLiteral:
                CheckGrammarNumericLiteral(node);
                return GetFreshTypeOfLiteralType(GetNumberLiteralType(JsNumber.FromString(store.Texts.GetString(store.Text(n))!)));
            case SyntaxKind.BigIntLiteral:
                return GetFreshTypeOfLiteralType(GetBigIntLiteralType(Utilities.ParsePseudoBigInt(store.Texts.GetString(store.Text(n))!)));
            case SyntaxKind.TrueKeyword:
                return trueType;
            case SyntaxKind.FalseKeyword:
                return falseType;
            case SyntaxKind.TemplateExpression:
                return CheckTemplateExpression(node);
            case SyntaxKind.RegularExpressionLiteral:
                CheckRegularExpressionLiteral(node); // checkRegularExpressionLiteral (module C8)
                return globalRegExpType;
            case SyntaxKind.ArrayLiteralExpression:
                return CheckArrayLiteral(node, checkMode, forceTuple);
            case SyntaxKind.ObjectLiteralExpression:
                return CheckObjectLiteral(node, checkMode);
            case SyntaxKind.PropertyAccessExpression:
                return CheckPropertyAccessExpression(node, checkMode);
            case SyntaxKind.QualifiedName:
                return CheckQualifiedName(node, checkMode);
            case SyntaxKind.ElementAccessExpression:
                return CheckIndexedAccess(node, checkMode);
            case SyntaxKind.CallExpression:
                if (Utilities.IsImportCall(store, n))
                {
                    return CheckImportCallExpression(node);
                }
                return CheckCallExpression(node, checkMode);
            case SyntaxKind.NewExpression:
                return CheckCallExpression(node, checkMode);
            case SyntaxKind.TaggedTemplateExpression:
                return CheckTaggedTemplateExpression(node);
            case SyntaxKind.ParenthesizedExpression:
                return CheckParenthesizedExpression(node, checkMode);
            case SyntaxKind.ClassExpression:
                return CheckClassExpression(node);
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
                return CheckFunctionExpressionOrObjectLiteralMethod(node, checkMode);
            case SyntaxKind.TypeOfExpression:
                CheckExpression(At(node, store.Expression(n)));
                return typeofType;
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.AsExpression:
                return CheckAssertion(node, checkMode);
            case SyntaxKind.NonNullExpression:
                return CheckNonNullAssertion(node);
            case SyntaxKind.ExpressionWithTypeArguments:
                return CheckExpressionWithTypeArguments(node);
            case SyntaxKind.SatisfiesExpression:
                return CheckSatisfiesExpression(node);
            case SyntaxKind.MetaProperty:
                return CheckMetaProperty(node);
            case SyntaxKind.DeleteExpression:
                return CheckDeleteExpression(node);
            case SyntaxKind.VoidExpression:
                // checkVoidExpression: checkNodeDeferred(node) checks the operand
                CheckNodeDeferred(node);
                return undefinedWideningType;
            case SyntaxKind.AwaitExpression:
                return CheckAwaitExpression(node);
            case SyntaxKind.PrefixUnaryExpression:
                return CheckPrefixUnaryExpression(node);
            case SyntaxKind.PostfixUnaryExpression:
                return CheckPostfixUnaryExpression(node);
            case SyntaxKind.BinaryExpression:
                return CheckBinaryExpression(node, checkMode);
            case SyntaxKind.ConditionalExpression:
                return CheckConditionalExpression(node, checkMode);
            case SyntaxKind.SpreadElement:
                return CheckSpreadExpression(node, checkMode);
            case SyntaxKind.OmittedExpression:
                return undefinedWideningType;
            case SyntaxKind.YieldExpression:
                return CheckYieldExpression(node);
            case SyntaxKind.JsxExpression:
                return CheckJsxExpression(node, checkMode);
            case SyntaxKind.JsxElement:
            case SyntaxKind.JsxSelfClosingElement:
                return CheckJsxElement(node);
            case SyntaxKind.JsxFragment:
                return CheckJsxFragment(node);
            case SyntaxKind.JsxAttributes:
                return CheckJsxAttributes(node, checkMode);
            default:
                return errorType;
        }
    }

    /// <summary>checker.ts checkExpressionWithContextualType.</summary>
    internal Type CheckExpressionWithContextualType(NodeRef node, Type contextualType, InferenceContext? inferenceContext, CheckMode checkMode)
    {
        var contextNode = GetContextNode(node);
        PushContextualType(contextNode, contextualType, isCache: false);
        PushInferenceContext(contextNode, inferenceContext);
        var type = CheckExpression(node, checkMode | CheckMode.Contextual | (inferenceContext is not null ? CheckMode.Inferential : CheckMode.Normal));
        // In CheckMode.Inferential we collect intra-expression inference sites to process before fixing any type
        // parameters. This information is no longer needed after the call to checkExpression.
        if (inferenceContext is not null && inferenceContext.IntraExpressionInferenceSites is not null)
        {
            inferenceContext.IntraExpressionInferenceSites = null;
        }
        // We strip literal freshness when an appropriate contextual type is present such that contextually typed
        // literals always preserve their literal types (otherwise they might widen during type inference). An alternative
        // here would be to not mark contextually typed literals as fresh in the first place.
        var result = MaybeTypeOfKind(type, TypeFlags.Literal) && IsLiteralOfContextualType(type, InstantiateContextualType(contextualType, node, ContextFlags.None))
            ? GetRegularTypeOfLiteralType(type)
            : type;
        PopInferenceContext();
        PopContextualType();
        return result;
    }

    // checker.ts getContextNode (JSX attributes: C9)
    private NodeRef GetContextNode(NodeRef node)
    {
        var store = StoreOf(node);
        if (store.Kind(node.Node) == SyntaxKind.JsxAttributes && store.Kind(store.Parent(node.Node)) != SyntaxKind.JsxSelfClosingElement)
        {
            return At(node, store.Parent(store.Parent(node.Node))); // Needs to be the root JsxElement, so it encompasses the attributes _and_ the children (which are essentially part of the attributes)
        }
        return node;
    }

    // checker.ts isTypeAssertion (JSDoc type assertions: C9)
    private static bool IsTypeAssertion(NodeStore store, Node node)
    {
        node = Utilities.SkipParentheses(store, node, true);
        return store.Kind(node) is SyntaxKind.TypeAssertionExpression or SyntaxKind.AsExpression || FactoryUtilities.IsJSDocTypeAssertion(store, node);
    }

    /// <summary>checker.ts checkDeclarationInitializer (JSDoc satisfies: C9).</summary>
    internal Type CheckDeclarationInitializer(NodeRef declaration, CheckMode checkMode, Type contextualType = default)
    {
        var store = StoreOf(declaration);
        var initializer = At(declaration, GetEffectiveInitializer(store, declaration.Node));
        if (Utilities.IsInJSFile(store, declaration.Node))
        {
            var satisfiesTypeNode = TryGetJSDocSatisfiesTypeNode(declaration);
            if (!satisfiesTypeNode.IsNone)
            {
                return CheckSatisfiesExpressionWorker(initializer, satisfiesTypeNode, checkMode);
            }
        }
        var quick = GetQuickTypeOfExpression(initializer);
        var type = !quick.IsNone ? quick
            : !contextualType.IsNone ? CheckExpressionWithContextualType(initializer, contextualType, null, checkMode)
            : CheckExpressionCached(initializer, checkMode);
        var root = store.Kind(declaration.Node) == SyntaxKind.BindingElement ? WalkUpBindingElementsAndPatterns(declaration) : declaration;
        if (Kind(root) == SyntaxKind.Parameter)
        {
            var name = store.Name(declaration.Node);
            if (store.Kind(name) == SyntaxKind.ObjectBindingPattern && IsObjectLiteralType(type))
            {
                return PadObjectLiteralType(type, At(declaration, name));
            }
            if (store.Kind(name) == SyntaxKind.ArrayBindingPattern && IsTupleType(type))
            {
                return PadTupleType(type, At(declaration, name));
            }
        }
        return type;
    }

    // utilities.ts walkUpBindingElementsAndPatterns
    private NodeRef WalkUpBindingElementsAndPatterns(NodeRef binding)
    {
        var store = StoreOf(binding);
        var node = store.Parent(binding.Node);
        while (store.Kind(store.Parent(node)) == SyntaxKind.BindingElement)
        {
            node = store.Parent(store.Parent(node));
        }
        return At(binding, store.Parent(node));
    }

    // checker.ts padObjectLiteralType
    private Type PadObjectLiteralType(Type type, NodeRef pattern)
    {
        var store = StoreOf(pattern);
        List<Node>? missingElements = null;
        foreach (var e in store.Nodes(store.Elements(pattern.Node)))
        {
            if (!store.Initializer(e).IsNone)
            {
                var name = GetPropertyNameFromBindingElement(At(pattern, e));
                if (name is not null && GetPropertyOfType(type, name).IsNone)
                {
                    (missingElements ??= []).Add(e);
                }
            }
        }
        if (missingElements is null)
        {
            return type;
        }
        var members = new SymTable();
        foreach (var prop in GetPropertiesOfObjectType(type))
        {
            members.Set(NameOf(prop), prop);
        }
        foreach (var e in missingElements)
        {
            var symbol = CreateSymbol(SymbolFlags.Property | SymbolFlags.Optional, GetPropertyNameFromBindingElement(At(pattern, e))!);
            var elementType = GetTypeFromBindingElement(At(pattern, e), includePatternInType: false, reportErrors: false); // (before the links are written: the call can grow them)
            ValueSymbolLinks.Get(symbol).ResolvedType = elementType;
            members.Set(NameOf(symbol), symbol);
        }
        var result = CreateAnonymousType(Types.Symbol(type), members, [], [], GetIndexInfosOfType(type));
        Types.Record(result).ObjectFlags = Types.ObjectFlagsOf(type);
        return result;
    }

    // checker.ts getPropertyNameFromBindingElement
    private string? GetPropertyNameFromBindingElement(NodeRef e)
    {
        var store = StoreOf(e);
        var propertyName = store.PropertyName(e.Node);
        var exprType = GetLiteralTypeFromPropertyName(At(e, propertyName.IsNone ? store.Name(e.Node) : propertyName));
        return IsTypeUsableAsPropertyName(exprType) ? GetPropertyNameFromType(exprType) : null;
    }

    // checker.ts padTupleType
    private Type PadTupleType(Type type, NodeRef pattern)
    {
        var target = TupleTarget(type);
        if ((target.CombinedFlags & ElementFlags.Variable) != 0 || GetTypeReferenceArity(type) >= StoreOf(pattern).Length(StoreOf(pattern).Elements(pattern.Node)))
        {
            return type;
        }
        var store = StoreOf(pattern);
        var patternElements = store.Nodes(store.Elements(pattern.Node));
        var elementTypes = new List<Type>(GetElementTypes(type));
        var elementFlags = new List<ElementFlags>(target.ElementFlags ?? []);
        for (var i = GetTypeReferenceArity(type); i < patternElements.Length; i++)
        {
            var e = patternElements[i];
            if (i < patternElements.Length - 1 || !(store.Kind(e) == SyntaxKind.BindingElement && !store.DotDotDotToken(e).IsNone))
            {
                var hasDefault = store.Kind(e) != SyntaxKind.OmittedExpression && HasDefaultValue(store, e);
                elementTypes.Add(hasDefault ? GetTypeFromBindingElement(At(pattern, e), includePatternInType: false, reportErrors: false) : anyType);
                elementFlags.Add(ElementFlags.Optional);
                if (store.Kind(e) != SyntaxKind.OmittedExpression && !hasDefault)
                {
                    ReportImplicitAny(At(pattern, e), anyType);
                }
            }
        }
        return CreateTupleType([.. elementTypes], [.. elementFlags], target.Readonly);
    }

    // checker.ts getWidenedLiteralTypeForInitializer
    private Type GetWidenedLiteralTypeForInitializer(NodeRef declaration, Type type)
    {
        var store = StoreOf(declaration);
        return (Utilities.GetCombinedNodeFlags(store, declaration.Node) & NodeFlags.Constant) != 0 || IsDeclarationReadonly(store, declaration.Node) ? type : GetWidenedLiteralType(type);
    }

    /// <summary>checker.ts isLiteralOfContextualType.</summary>
    internal bool IsLiteralOfContextualType(Type candidateType, Type contextualType)
    {
        if (contextualType.IsNone)
        {
            return false;
        }
        var flags = Types.Flags(contextualType);
        if ((flags & TypeFlags.UnionOrIntersection) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(contextualType).Types).ToArray())
            {
                if (IsLiteralOfContextualType(candidateType, t))
                {
                    return true;
                }
            }
            return false;
        }
        if ((flags & TypeFlags.InstantiableNonPrimitive) != 0)
        {
            // If the contextual type is a type variable constrained to a primitive type, consider
            // this a literal context for literals of that primitive type. For example, given a
            // type parameter 'T extends string', infer string literal types for T.
            var c = GetBaseConstraintOfType(contextualType);
            var constraint = c.IsNone ? unknownType : c;
            return (MaybeTypeOfKind(constraint, TypeFlags.String) && MaybeTypeOfKind(candidateType, TypeFlags.StringLiteral)) ||
                (MaybeTypeOfKind(constraint, TypeFlags.Number) && MaybeTypeOfKind(candidateType, TypeFlags.NumberLiteral)) ||
                (MaybeTypeOfKind(constraint, TypeFlags.BigInt) && MaybeTypeOfKind(candidateType, TypeFlags.BigIntLiteral)) ||
                (MaybeTypeOfKind(constraint, TypeFlags.ESSymbol) && MaybeTypeOfKind(candidateType, TypeFlags.UniqueESSymbol)) ||
                IsLiteralOfContextualType(candidateType, constraint);
        }
        // If the contextual type is a literal of a particular primitive type, we consider this a
        // literal context for all literals of that primitive type.
        return ((flags & (TypeFlags.StringLiteral | TypeFlags.Index | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.StringLiteral)) ||
            ((flags & TypeFlags.NumberLiteral) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.NumberLiteral)) ||
            ((flags & TypeFlags.BigIntLiteral) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.BigIntLiteral)) ||
            ((flags & TypeFlags.BooleanLiteral) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.BooleanLiteral)) ||
            ((flags & TypeFlags.UniqueESSymbol) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.UniqueESSymbol));
    }

    /// <summary>checker.ts isConstContext.</summary>
    internal bool IsConstContext(NodeRef node)
    {
        var store = StoreOf(node);
        var parent = store.Parent(node.Node);
        var parentKind = store.Kind(parent);
        return (parentKind is SyntaxKind.AsExpression or SyntaxKind.TypeAssertionExpression && IsConstTypeReference(store, store.Type(parent))) ||
            (IsValidConstAssertionArgument(node) && IsConstTypeVariable(GetContextualType(node, ContextFlags.None))) ||
            (parentKind is SyntaxKind.ParenthesizedExpression or SyntaxKind.ArrayLiteralExpression or SyntaxKind.SpreadElement && IsConstContext(At(node, parent))) ||
            (parentKind is SyntaxKind.PropertyAssignment or SyntaxKind.ShorthandPropertyAssignment or SyntaxKind.TemplateSpan && IsConstContext(At(node, store.Parent(parent))));
    }

    // checker.ts isConstTypeVariable
    private bool IsConstTypeVariable(Type type, int depth = 0)
    {
        if (type.IsNone || depth >= 5)
        {
            return false;
        }
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.TypeParameter) != 0)
        {
            var symbol = Types.Symbol(type);
            if (!symbol.IsNone)
            {
                foreach (var d in DeclarationsOf(symbol) ?? [])
                {
                    if ((Utilities.GetCombinedModifierFlags(StoreOf(d), d.Node) & ModifierFlags.Const) != 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        if ((flags & TypeFlags.UnionOrIntersection) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(type).Types).ToArray())
            {
                if (IsConstTypeVariable(t, depth))
                {
                    return true;
                }
            }
            return false;
        }
        if ((flags & TypeFlags.IndexedAccess) != 0)
        {
            return IsConstTypeVariable(Types.Data<IndexedAccessData>(type).ObjectType, depth + 1);
        }
        if ((flags & TypeFlags.Conditional) != 0)
        {
            return IsConstTypeVariable(GetConstraintOfConditionalType(type), depth + 1);
        }
        if ((flags & TypeFlags.Substitution) != 0)
        {
            return IsConstTypeVariable(Types.Data<SubstitutionData>(type).BaseType, depth);
        }
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.Mapped) != 0)
        {
            return IsConstMappedType(type, depth);
        }
        if (IsGenericTupleType(type))
        {
            var elementTypes = GetElementTypes(type);
            var elementFlags = TupleTarget(type).ElementFlags ?? [];
            for (var i = 0; i < elementTypes.Length; i++)
            {
                if ((elementFlags[i] & ElementFlags.Variadic) != 0 && IsConstTypeVariable(elementTypes[i], depth))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // checker.ts isConstMappedType
    private bool IsConstMappedType(Type type, int depth)
    {
        var typeVariable = GetHomomorphicTypeVariable(type);
        return !typeVariable.IsNone && IsConstTypeVariable(typeVariable, depth);
    }

    // checker.ts isValidConstAssertionArgument
    private bool IsValidConstAssertionArgument(NodeRef node)
    {
        var store = StoreOf(node);
        switch (store.Kind(node.Node))
        {
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.TemplateExpression:
                return true;
            case SyntaxKind.ParenthesizedExpression:
                return IsValidConstAssertionArgument(At(node, store.Expression(node.Node)));
            case SyntaxKind.PrefixUnaryExpression:
            {
                var op = store.Operator(node.Node);
                var arg = store.Kind(store.Operand(node.Node));
                return (op == SyntaxKind.MinusToken && arg is SyntaxKind.NumericLiteral or SyntaxKind.BigIntLiteral) ||
                    (op == SyntaxKind.PlusToken && arg == SyntaxKind.NumericLiteral);
            }
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            {
                var expr = Utilities.SkipParentheses(store, store.Expression(node.Node));
                var symbol = Utilities.IsEntityNameExpression(store, expr) ? ResolveEntityName(At(node, expr), SymbolFlags.Value, ignoreErrors: true) : SymbolRef.None;
                return !symbol.IsNone && (SymbolFlagsOf(symbol) & SymbolFlags.Enum) != 0;
            }
        }
        return false;
    }

    /// <summary>checker.ts checkExpressionForMutableLocation.</summary>
    internal Type CheckExpressionForMutableLocation(NodeRef node, CheckMode checkMode = CheckMode.Normal, bool forceTuple = false)
    {
        var type = CheckExpression(node, checkMode, forceTuple);
        return IsConstContext(node) ? GetRegularTypeOfLiteralType(type)
            : IsTypeAssertion(StoreOf(node), node.Node) ? type
            : GetWidenedLiteralLikeTypeForContextualType(type, InstantiateContextualType(GetContextualType(node, ContextFlags.None), node, ContextFlags.None));
    }

    // checker.ts instantiateTypeWithSingleGenericCallSignature
    private Type InstantiateTypeWithSingleGenericCallSignature(NodeRef node, Type type, CheckMode checkMode)
    {
        if ((checkMode & (CheckMode.Inferential | CheckMode.SkipGenericFunctions)) != 0)
        {
            var callSignature = GetSingleSignature(type, SignatureKind.Call, allowMembers: true);
            var constructSignature = GetSingleSignature(type, SignatureKind.Construct, allowMembers: true);
            var signature = !callSignature.IsNone ? callSignature : constructSignature;
            if (!signature.IsNone && Signatures.Record(signature).TypeParameters.Count > 0)
            {
                var contextualType = GetApparentTypeOfContextualType(node, ContextFlags.NoConstraints);
                if (!contextualType.IsNone)
                {
                    var contextualSignature = GetSingleSignature(GetNonNullableType(contextualType), !callSignature.IsNone ? SignatureKind.Call : SignatureKind.Construct, allowMembers: false);
                    if (!contextualSignature.IsNone && Signatures.Record(contextualSignature).TypeParameters.Count == 0)
                    {
                        if ((checkMode & CheckMode.SkipGenericFunctions) != 0)
                        {
                            SkippedGenericFunction(node, checkMode);
                            return anyFunctionType;
                        }
                        var context = GetInferenceContext(node)!;
                        // We have an expression that is an argument of a generic function for which we are performing
                        // type argument inference. The expression is of a function type with a single generic call
                        // signature and a contextual function type with a single non-generic call signature. Now check
                        // if the outer function returns a function type with a single non-generic call signature and
                        // if some of the outer function type parameters have no inferences so far. If so, we can
                        // potentially add inferred type parameters to the outer function return type.
                        var returnType = !context.Signature.IsNone ? GetReturnTypeOfSignature(context.Signature) : Type.None;
                        var returnSignature = !returnType.IsNone ? GetSingleCallOrConstructSignature(returnType) : Signature.None;
                        if (!returnSignature.IsNone && Signatures.Record(returnSignature).TypeParameters.Count == 0 && !context.Inferences.TrueForAll(HasInferenceCandidates))
                        {
                            // Instantiate the signature with its own type parameters as type arguments, possibly
                            // renaming the type parameters to ensure they have unique names.
                            var uniqueTypeParameters = GetUniqueTypeParameters(context, Types.Elements(Signatures.Record(signature).TypeParameters).ToArray());
                            var instantiatedSignature = GetSignatureInstantiationWithoutFillingInTypeArguments(signature, uniqueTypeParameters);
                            // Infer from the parameters of the instantiated signature to the parameters of the
                            // contextual signature starting with an empty set of inference candidates.
                            var inferences = context.Inferences.ConvertAll(info => CreateInferenceInfo(info.TypeParameter));
                            ApplyToParameterTypes(instantiatedSignature, contextualSignature, (source, target) => InferTypes(inferences, source, target, InferencePriority.None, contravariant: true));
                            if (inferences.Exists(HasInferenceCandidates))
                            {
                                // We have inference candidates, indicating that one or more type parameters are referenced
                                // in the parameter types of the contextual signature. Now also infer from the return type.
                                ApplyToReturnTypes(instantiatedSignature, contextualSignature, (source, target) => InferTypes(inferences, source, target));
                                // If the type parameters for which we produced candidates do not have any inferences yet,
                                // we adopt the new inference candidates and add the type parameters of the expression type
                                // to the set of inferred type parameters for the outer function return type.
                                if (!HasOverlappingInferences(context.Inferences, inferences))
                                {
                                    MergeInferences(context.Inferences, inferences);
                                    (context.InferredTypeParameters ??= []).AddRange(uniqueTypeParameters);
                                    return GetOrCreateTypeFromSignature(instantiatedSignature);
                                }
                            }
                        }
                        return GetOrCreateTypeFromSignature(InstantiateSignatureInContextOf(signature, contextualSignature, context));
                    }
                }
            }
        }
        return type;
    }

    // checker.ts skippedGenericFunction
    private void SkippedGenericFunction(NodeRef node, CheckMode checkMode)
    {
        if ((checkMode & CheckMode.Inferential) != 0)
        {
            // We have skipped a generic function during inferential typing. Obtain the inference context and
            // indicate this has occurred such that we know a second pass of inference is be needed.
            var context = GetInferenceContext(node)!;
            context.Flags |= InferenceFlags.SkippedGenericFunction;
        }
    }

    // ---- the contextual type and inference context stacks ----

    private void PushCachedContextualType(NodeRef node) => PushContextualType(node, GetContextualType(node, ContextFlags.None), isCache: true);

    private void PushContextualType(NodeRef node, Type type, bool isCache)
    {
        if (contextualTypeCount == contextualTypeNodes.Length)
        {
            Array.Resize(ref contextualTypeNodes, contextualTypeCount * 2);
            Array.Resize(ref contextualTypes, contextualTypeCount * 2);
            Array.Resize(ref contextualIsCache, contextualTypeCount * 2);
        }
        contextualTypeNodes[contextualTypeCount] = node;
        contextualTypes[contextualTypeCount] = type;
        contextualIsCache[contextualTypeCount] = isCache;
        contextualTypeCount++;
    }

    private void PopContextualType()
    {
        contextualTypeCount--;
        contextualTypeNodes[contextualTypeCount] = NodeRef.None;
        contextualTypes[contextualTypeCount] = Type.None;
        contextualIsCache[contextualTypeCount] = false;
    }

    private int FindContextualNode(NodeRef node, bool includeCaches)
    {
        for (var i = contextualTypeCount - 1; i >= 0; i--)
        {
            if (node == contextualTypeNodes[i] && (includeCaches || !contextualIsCache[i]))
            {
                return i;
            }
        }
        return -1;
    }

    private void PushInferenceContext(NodeRef node, InferenceContext? inferenceContext)
    {
        if (inferenceContextCount == inferenceContextNodes.Length)
        {
            Array.Resize(ref inferenceContextNodes, inferenceContextCount * 2);
            Array.Resize(ref inferenceContexts, inferenceContextCount * 2);
        }
        inferenceContextNodes[inferenceContextCount] = node;
        inferenceContexts[inferenceContextCount] = inferenceContext;
        inferenceContextCount++;
    }

    private void PopInferenceContext()
    {
        inferenceContextCount--;
        inferenceContextNodes[inferenceContextCount] = NodeRef.None;
        inferenceContexts[inferenceContextCount] = null;
    }

    /// <summary>checker.ts getInferenceContext.</summary>
    internal InferenceContext? GetInferenceContext(NodeRef node)
    {
        for (var i = inferenceContextCount - 1; i >= 0; i--)
        {
            if (IsNodeDescendantOf(node, inferenceContextNodes[i]))
            {
                return inferenceContexts[i];
            }
        }
        return null;
    }

    // utilities.ts isNodeDescendantOf
    private bool IsNodeDescendantOf(NodeRef node, NodeRef ancestor)
    {
        if (node.File != ancestor.File || ancestor.IsNone)
        {
            return false;
        }
        var store = StoreOf(node);
        var n = node.Node;
        while (!n.IsNone)
        {
            if (n == ancestor.Node)
            {
                return true;
            }
            n = store.Parent(n);
        }
        return false;
    }

    // ---- simple expressions ----

    // checker.ts checkTemplateExpression
    private Type CheckTemplateExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var texts = new List<string> { store.Texts.GetString(store.Text(store.Head(n)))! };
        var types = new List<Type>();
        foreach (var span in store.Nodes(store.TemplateSpans(n)))
        {
            var expression = At(node, store.Expression(span));
            var type = CheckExpression(expression);
            if (MaybeTypeOfKindConsideringBaseConstraint(type, TypeFlags.ESSymbolLike))
            {
                Error(expression, Diagnostics.Implicit_conversion_of_a_symbol_to_a_string_will_fail_at_runtime_Consider_wrapping_this_expression_in_String);
            }
            texts.Add(store.Texts.GetString(store.Text(store.Literal(span)))!);
            types.Add(IsTypeAssignableTo(type, templateConstraintType) ? type : stringType);
        }
        var evaluated = store.Kind(store.Parent(n)) != SyntaxKind.TaggedTemplateExpression ? EvaluateTemplateExpression(node) : null;
        if (evaluated is not null)
        {
            return GetFreshTypeOfLiteralType(GetStringLiteralType(evaluated));
        }
        var contextual = GetContextualType(node, ContextFlags.None);
        if (IsConstContext(node) || IsTemplateLiteralContext(node) || SomeType(contextual.IsNone ? unknownType : contextual, IsTemplateLiteralContextualType))
        {
            return GetTemplateLiteralType([.. texts], [.. types]);
        }
        return stringType;
    }

    // evaluate(node).value for a template expression: the string of the evaluated spans when all are constant
    private string? EvaluateTemplateExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var result = new System.Text.StringBuilder(store.Texts.GetString(store.Text(store.Head(n))));
        foreach (var span in store.Nodes(store.TemplateSpans(n)))
        {
            var value = EvaluateConstantExpression(At(node, store.Expression(span)));
            if (value is null)
            {
                return null;
            }
            result.Append(value is double d ? JsNumber.ToString(d) : (string)value);
            result.Append(store.Texts.GetString(store.Text(store.Literal(span))));
        }
        return result.ToString();
    }

    // checker.ts isTemplateLiteralContext
    private bool IsTemplateLiteralContext(NodeRef node)
    {
        var store = StoreOf(node);
        var parent = store.Parent(node.Node);
        return (store.Kind(parent) == SyntaxKind.ParenthesizedExpression && IsTemplateLiteralContext(At(node, parent))) ||
            (store.Kind(parent) == SyntaxKind.ElementAccessExpression && store.ArgumentExpression(parent) == node.Node);
    }

    // checker.ts isTemplateLiteralContextualType
    private bool IsTemplateLiteralContextualType(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral)) != 0)
        {
            return true;
        }
        if ((flags & TypeFlags.InstantiableNonPrimitive) != 0)
        {
            var constraint = GetBaseConstraintOfType(type);
            return MaybeTypeOfKind(constraint.IsNone ? unknownType : constraint, TypeFlags.StringLike);
        }
        return false;
    }

    // checker.ts maybeTypeOfKindConsideringBaseConstraint
    internal bool MaybeTypeOfKindConsideringBaseConstraint(Type type, TypeFlags kind)
    {
        if (MaybeTypeOfKind(type, kind))
        {
            return true;
        }
        var baseConstraint = GetBaseConstraintOrType(type);
        return !baseConstraint.IsNone && MaybeTypeOfKind(baseConstraint, kind);
    }

    // checker.ts checkAssertion / checkAssertionWorker (the .mts/.cts and erasableSyntaxOnly checks are not reached in
    // Gate C)
    private Type CheckAssertion(NodeRef node, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var typeNode = GetAssertionTypeNode(node);
        var expression = At(node, store.Expression(node.Node));
        var exprType = CheckExpression(expression, checkMode);
        if (IsConstTypeReference(store, typeNode.Node))
        {
            if (!IsValidConstAssertionArgument(expression))
            {
                Error(expression, Diagnostics.A_const_assertion_can_only_be_applied_to_references_to_enum_members_or_string_number_boolean_array_or_object_literals);
            }
            return GetRegularTypeOfLiteralType(exprType);
        }
        ExpressionLinks.Get(node).AssertionExpressionType = exprType;
        CheckSourceElement(typeNode);
        CheckNodeDeferred(node);
        return GetTypeFromTypeNode(typeNode);
    }

    // checker.ts getAssertionTypeAndExpression (the type)
    private NodeRef GetAssertionTypeNode(NodeRef node)
    {
        var store = StoreOf(node);
        return store.Kind(node.Node) == SyntaxKind.ParenthesizedExpression ? GetJSDocTypeAssertionType(node) : At(node, store.Type(node.Node));
    }

    // utilities.ts getJSDocTypeAssertionType
    private NodeRef GetJSDocTypeAssertionType(NodeRef node)
    {
        var store = StoreOf(node);
        var tag = UtilitiesPublic.GetJSDocTypeTag(store, node.Node);
        return tag.IsNone ? NodeRef.None : At(node, store.Type(store.TypeExpression(tag)));
    }

    // utilities.ts isJSDocSatisfiesExpression
    private static bool IsJSDocSatisfiesExpression(NodeStore store, Node node) =>
        Utilities.IsInJSFile(store, node) && store.Kind(node) == SyntaxKind.ParenthesizedExpression && UtilitiesPublic.HasJSDocNodes(store, node) &&
        !GetJSDocSatisfiesTag(store, node).IsNone;

    // utilities.ts getJSDocSatisfiesTag (a tag with a type expression)
    private static Node GetJSDocSatisfiesTag(NodeStore store, Node node) => GetFirstJSDocTagOfKind(store, node, SyntaxKind.JSDocSatisfiesTag);

    // utilities.ts getJSDocSatisfiesExpressionType
    private NodeRef GetJSDocSatisfiesExpressionType(NodeRef node)
    {
        var store = StoreOf(node);
        var tag = GetJSDocSatisfiesTag(store, node.Node);
        return At(node, store.Type(store.TypeExpression(tag)));
    }

    // checker.ts checkParenthesizedExpression
    private Type CheckParenthesizedExpression(NodeRef node, CheckMode checkMode)
    {
        var store = StoreOf(node);
        if (UtilitiesPublic.HasJSDocNodes(store, node.Node))
        {
            if (IsJSDocSatisfiesExpression(store, node.Node))
            {
                return CheckSatisfiesExpressionWorker(At(node, store.Expression(node.Node)), GetJSDocSatisfiesExpressionType(node), checkMode);
            }
            if (FactoryUtilities.IsJSDocTypeAssertion(store, node.Node))
            {
                return CheckAssertion(node, checkMode);
            }
        }
        return CheckExpression(At(node, store.Expression(node.Node)), checkMode);
    }

    // checker.ts checkAssertionDeferred
    private void CheckAssertionDeferred(NodeRef node)
    {
        var store = StoreOf(node);
        var typeNode = GetAssertionTypeNode(node);
        var errNode = store.Kind(node.Node) == SyntaxKind.ParenthesizedExpression ? typeNode : node;
        if (!ExpressionLinks.Has(node) || ExpressionLinks.Get(node).AssertionExpressionType.IsNone)
        {
            return;
        }
        var exprType = GetRegularTypeOfObjectLiteral(GetBaseTypeOfLiteralType(ExpressionLinks.Get(node).AssertionExpressionType));
        var targetType = GetTypeFromTypeNode(typeNode);
        if (!IsErrorType(targetType))
        {
            AddLazyDiagnostic(() =>
            {
                var widenedType = GetWidenedType(exprType);
                if (!IsTypeComparableTo(targetType, widenedType))
                {
                    CheckTypeComparableTo(exprType, targetType, errNode, Diagnostics.Conversion_of_type_0_to_type_1_may_be_a_mistake_because_neither_type_sufficiently_overlaps_with_the_other_If_this_was_intentional_convert_the_expression_to_unknown_first);
                }
            });
        }
    }

    // checker.ts checkNonNullAssertion / checkNonNullChain
    private Type CheckNonNullAssertion(NodeRef node)
    {
        var store = StoreOf(node);
        var expression = At(node, store.Expression(node.Node));
        if ((store.Flags(node.Node) & NodeFlags.OptionalChain) != 0)
        {
            var leftType = CheckExpression(expression);
            var nonOptionalType = GetOptionalExpressionType(leftType, expression);
            return PropagateOptionalTypeMarker(GetNonNullableType(nonOptionalType), node, nonOptionalType != leftType);
        }
        return GetNonNullableType(CheckExpression(expression));
    }

    // checker.ts checkExpressionWithTypeArguments (expressions with type arguments and type queries)
    private Type CheckExpressionWithTypeArguments(NodeRef node)
    {
        var store = StoreOf(node);
        // checkGrammarExpressionWithTypeArguments
        if (store.Kind(node.Node) == SyntaxKind.ExpressionWithTypeArguments && store.Kind(store.Expression(node.Node)) == SyntaxKind.ImportKeyword && !store.TypeArguments(node.Node).IsNone)
        {
            GrammarErrorOnNode(node, Diagnostics.This_use_of_import_is_invalid_import_calls_can_be_written_but_they_must_have_parentheses_and_cannot_have_type_arguments);
        }
        else
        {
            CheckGrammarTypeArguments(node, store.TypeArguments(node.Node));
        }
        CheckSourceElements(node, store.TypeArguments(node.Node));
        Type exprType;
        if (store.Kind(node.Node) == SyntaxKind.ExpressionWithTypeArguments)
        {
            var parent = WalkUpParenthesizedExpressions(store, store.Parent(node.Node));
            if (store.Kind(parent) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(parent)) == SyntaxKind.InstanceOfKeyword && IsNodeDescendantOf(node, At(node, store.Right(parent))))
            {
                Error(node, Diagnostics.The_right_hand_side_of_an_instanceof_expression_must_not_be_an_instantiation_expression);
            }
            exprType = CheckExpression(At(node, store.Expression(node.Node)));
        }
        else
        {
            var exprName = At(node, store.ExprName(node.Node));
            exprType = Utilities.IsThisIdentifier(store, exprName.Node) ? CheckThisExpression(exprName) : CheckExpression(exprName);
        }
        return GetInstantiationExpressionType(exprType, node);
    }

    // checker.ts checkSatisfiesExpression / checkSatisfiesExpressionWorker
    private Type CheckSatisfiesExpression(NodeRef node)
    {
        var store = StoreOf(node);
        CheckSourceElement(At(node, store.Type(node.Node)));
        return CheckSatisfiesExpressionWorker(At(node, store.Expression(node.Node)), At(node, store.Type(node.Node)), CheckMode.Normal);
    }

    private Type CheckSatisfiesExpressionWorker(NodeRef expression, NodeRef typeNode, CheckMode checkMode)
    {
        var typeStore = StoreOf(typeNode);
        var errorNode = typeStore.Parent(typeNode.Node);
        while (!errorNode.IsNone && typeStore.Kind(errorNode) is not (SyntaxKind.SatisfiesExpression or SyntaxKind.JSDocSatisfiesTag))
        {
            errorNode = typeStore.Parent(errorNode);
        }
        var node = At(typeNode, errorNode);
        var exprType = CheckExpression(expression, checkMode);
        var targetType = GetTypeFromTypeNode(typeNode);
        if (IsErrorType(targetType))
        {
            return targetType;
        }
        CheckTypeAssignableToAndOptionallyElaborate(exprType, targetType, node, expression, Diagnostics.Type_0_does_not_satisfy_the_expected_type_1);
        return exprType;
    }

    // checker.ts checkMetaProperty / checkNewTargetMetaProperty / checkImportMetaProperty
    private Type CheckMetaProperty(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        CheckGrammarMetaProperty(node);
        if (store.KeywordToken(n) == SyntaxKind.NewKeyword)
        {
            return CheckNewTargetMetaProperty(node);
        }
        if (store.Texts.Equals(store.EscapedText(store.Name(n)), "defer"))
        {
            return errorType;
        }
        // checkImportMetaProperty (the module kind is ES2022 in Gate C, so 1343/1470 are not reported)
        if (moduleKind is >= ModuleKind.Node16 and <= ModuleKind.NodeNext)
        {
            // (impliedNodeFormat of the file: ESNext under the bundler resolution of Gate C)
        }
        else if (moduleKind < ModuleKind.ES2020 && moduleKind != ModuleKind.System)
        {
            Error(node, Diagnostics.The_import_meta_meta_property_is_only_allowed_when_the_module_option_is_es2020_es2022_esnext_system_node16_node18_node20_or_nodenext);
        }
        if (store.Texts.Equals(store.EscapedText(store.Name(n)), "meta"))
        {
            var importMeta = GetGlobalType("ImportMeta", 0, reportErrors: true);
            return importMeta.IsNone ? errorType : importMeta;
        }
        return errorType;
    }

    // checker.ts checkGrammarMetaProperty
    private void CheckGrammarMetaProperty(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var nameText = store.Texts.GetString(store.EscapedText(store.Name(n)))!;
        switch (store.KeywordToken(n))
        {
            case SyntaxKind.NewKeyword:
                if (nameText != "target")
                {
                    GrammarErrorOnNode(At(node, store.Name(n)), Diagnostics._0_is_not_a_valid_meta_property_for_keyword_1_Did_you_mean_2, nameText, TokenText(store.KeywordToken(n)), "target");
                }
                break;
            case SyntaxKind.ImportKeyword:
                if (nameText != "meta")
                {
                    var isCallee = store.Kind(store.Parent(n)) == SyntaxKind.CallExpression && store.Expression(store.Parent(n)) == n;
                    if (nameText == "defer")
                    {
                        if (!isCallee)
                        {
                            GrammarErrorAtPos(node, store.End(n), 0, Diagnostics._0_expected, "(");
                        }
                    }
                    else
                    {
                        if (isCallee)
                        {
                            GrammarErrorOnNode(At(node, store.Name(n)), Diagnostics._0_is_not_a_valid_meta_property_for_keyword_import_Did_you_mean_meta_or_defer, nameText);
                        }
                        else
                        {
                            GrammarErrorOnNode(At(node, store.Name(n)), Diagnostics._0_is_not_a_valid_meta_property_for_keyword_1_Did_you_mean_2, nameText, TokenText(store.KeywordToken(n)), "meta");
                        }
                    }
                }
                break;
        }
    }

    private Type CheckNewTargetMetaProperty(NodeRef node)
    {
        var container = GetNewTargetContainer(node);
        if (container.IsNone)
        {
            Error(node, Diagnostics.Meta_property_0_is_only_allowed_in_the_body_of_a_function_declaration_function_expression_or_constructor, "new.target");
            return errorType;
        }
        if (Kind(container) == SyntaxKind.Constructor)
        {
            return GetTypeOfSymbol(GetSymbolOfDeclaration(Parent(container)));
        }
        return GetTypeOfSymbol(GetSymbolOfDeclaration(container));
    }

    // utilities.ts getNewTargetContainer
    private NodeRef GetNewTargetContainer(NodeRef node)
    {
        var store = StoreOf(node);
        var container = Utilities.GetThisContainer(store, node.Node, includeArrowFunctions: false, includeClassComputedPropertyName: false);
        if (!container.IsNone)
        {
            switch (store.Kind(container))
            {
                case SyntaxKind.Constructor:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.FunctionExpression:
                    return At(node, container);
            }
        }
        return NodeRef.None;
    }

    // checker.ts checkDeleteExpression
    private Type CheckDeleteExpression(NodeRef node)
    {
        var store = StoreOf(node);
        CheckExpression(At(node, store.Expression(node.Node)));
        var expr = At(node, Utilities.SkipParentheses(store, store.Expression(node.Node)));
        if (!Utilities.IsAccessExpression(store, expr.Node))
        {
            Error(expr, Diagnostics.The_operand_of_a_delete_operator_must_be_a_property_reference);
            return booleanType;
        }
        if (store.Kind(expr.Node) == SyntaxKind.PropertyAccessExpression && store.Kind(store.Name(expr.Node)) == SyntaxKind.PrivateIdentifier)
        {
            Error(expr, Diagnostics.The_operand_of_a_delete_operator_cannot_be_a_private_identifier);
        }
        var resolved = SymbolNodeLinks.Has(expr) ? SymbolNodeLinks.Get(expr).ResolvedSymbol : SymbolRef.None;
        var symbol = resolved.IsNone ? SymbolRef.None : GetExportSymbolOfValueSymbolIfExported(resolved);
        if (!symbol.IsNone)
        {
            if (IsReadonlySymbol(symbol))
            {
                Error(expr, Diagnostics.The_operand_of_a_delete_operator_cannot_be_a_read_only_property);
            }
            else
            {
                CheckDeleteExpressionMustBeOptional(expr, symbol);
            }
        }
        return booleanType;
    }

    // checker.ts checkDeleteExpressionMustBeOptional
    private void CheckDeleteExpressionMustBeOptional(NodeRef expr, SymbolRef symbol)
    {
        var type = GetTypeOfSymbol(symbol);
        if (strictNullChecks && (Types.Flags(type) & (TypeFlags.AnyOrUnknown | TypeFlags.Never)) == 0 &&
            !(exactOptionalPropertyTypes ? (SymbolFlagsOf(symbol) & SymbolFlags.Optional) != 0 : HasTypeFacts(type, TypeFacts.IsUndefined)))
        {
            Error(expr, Diagnostics.The_operand_of_a_delete_operator_must_be_optional);
        }
    }

    // checker.ts checkSpreadExpression
    private Type CheckSpreadExpression(NodeRef node, CheckMode checkMode)
    {
        var expression = At(node, StoreOf(node).Expression(node.Node));
        var arrayOrIterableType = CheckExpression(expression, checkMode);
        return CheckIteratedTypeOrElementType(IterationUse.Spread, arrayOrIterableType, undefinedType, expression);
    }

    // ---- identifiers ----

    /// <summary>checker.ts checkIdentifier (the flow part and getNarrowedTypeOfSymbol: Checker.FlowCheck.cs).</summary>
    internal Type CheckIdentifier(NodeRef node, CheckMode checkMode = CheckMode.Normal)
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
        CheckIdentifierCalculateNodeCheckFlags(node, symbol);
        if (symbol == argumentsSymbol)
        {
            if (IsInPropertyInitializerOrClassStaticBlock(node, ignoreArrowFunctions: true))
            {
                return errorType;
            }
            return GetTypeOfSymbol(symbol);
        }
        if (ShouldMarkIdentifierAliasReferenced(store, node.Node))
        {
            MarkIdentifierAliasReferenced(node);
        }
        var localOrExportSymbol = GetExportSymbolOfValueSymbolIfExported(symbol);
        var targetSymbol = ResolveAliasWithDeprecationCheck(localOrExportSymbol, node); // (module C8)
        if (IsDeprecatedSymbol(targetSymbol) && IsUncalledFunctionReference(node, targetSymbol) && DeclarationsOf(targetSymbol) is { } targetDeclarations)
        {
            var nodeStore = StoreOf(node);
            AddDeprecatedSuggestion(node, targetDeclarations, nodeStore.Texts.GetString(nodeStore.EscapedText(node.Node))!);
        }
        var declaration = ValueDeclarationOf(localOrExportSymbol);
        // If the identifier is declared in a binding pattern for which we're currently computing the implied type and the
        // reference occurs with the same binding pattern, return the non-inferrable any type. This for example occurs in
        // 'const [a, b = a + 1] = [2]' when we're computing the contextual type for the array literal '[2]'.
        if (!declaration.IsNone && Kind(declaration) == SyntaxKind.BindingElement && contextualBindingPatterns.Contains(Parent(declaration)) &&
            IsNodeDescendantOf(node, Parent(declaration)))
        {
            return nonInferrableAnyType;
        }
        var type = GetNarrowedTypeOfSymbol(localOrExportSymbol, node);
        var assignmentKind = GetAssignmentTargetKind(node);
        var flags = SymbolFlagsOf(localOrExportSymbol);
        if (assignmentKind != AssignmentKind.None)
        {
            if ((flags & SymbolFlags.Variable) == 0 && !(Utilities.IsInJSFile(store, node.Node) && (flags & SymbolFlags.ValueModule) != 0))
            {
                var assignmentError = (flags & SymbolFlags.Enum) != 0 ? Diagnostics.Cannot_assign_to_0_because_it_is_an_enum
                    : (flags & SymbolFlags.Class) != 0 ? Diagnostics.Cannot_assign_to_0_because_it_is_a_class
                    : (flags & SymbolFlags.Module) != 0 ? Diagnostics.Cannot_assign_to_0_because_it_is_a_namespace
                    : (flags & SymbolFlags.Function) != 0 ? Diagnostics.Cannot_assign_to_0_because_it_is_a_function
                    : (flags & SymbolFlags.Alias) != 0 ? Diagnostics.Cannot_assign_to_0_because_it_is_an_import
                    : Diagnostics.Cannot_assign_to_0_because_it_is_not_a_variable;
                Error(node, assignmentError, SymbolToString(symbol));
                return errorType;
            }
            if (IsReadonlySymbol(localOrExportSymbol))
            {
                Error(node, (flags & SymbolFlags.Variable) != 0 ? Diagnostics.Cannot_assign_to_0_because_it_is_a_constant : Diagnostics.Cannot_assign_to_0_because_it_is_a_read_only_property, SymbolToString(symbol));
                return errorType;
            }
        }
        var isAlias = (flags & SymbolFlags.Alias) != 0;
        // We only narrow variables and parameters occurring in a non-assignment position. For all other
        // entities we simply return the declared type.
        if ((flags & SymbolFlags.Variable) != 0)
        {
            if (assignmentKind == AssignmentKind.Definite)
            {
                return IsInCompoundLikeAssignment(store, node.Node) ? GetBaseTypeOfLiteralType(type) : type;
            }
        }
        else if (isAlias)
        {
            declaration = GetDeclarationOfAliasSymbol(symbol);
        }
        else
        {
            return type;
        }
        if (declaration.IsNone)
        {
            return type;
        }
        // module C7: the declaration and flow containers, the initial type and the flow type (Checker.FlowCheck.cs)
        return CheckIdentifierFlowType(node, symbol, localOrExportSymbol, declaration, type, assignmentKind, isAlias, checkMode);
    }

    // checker.ts checkIdentifierCalculateNodeCheckFlags (the downlevel captures do not apply to the ES2025 target)
    private void CheckIdentifierCalculateNodeCheckFlags(NodeRef node, SymbolRef symbol)
    {
        if (IsThisInTypeQuery(StoreOf(node), node.Node))
        {
            return;
        }
        // As noted in ECMAScript 6 language spec, arrow functions never have an arguments objects.
        // To avoid that we will give an error to users if they use arguments objects in arrow function so that they
        // can explicitly bound arguments objects
        if (symbol == argumentsSymbol)
        {
            if (IsInPropertyInitializerOrClassStaticBlock(node, ignoreArrowFunctions: true))
            {
                Error(node, Diagnostics.arguments_cannot_be_referenced_in_property_initializers_or_class_static_initialization_blocks);
            }
        }
        // (deprecation suggestions: C8; the class reference and block-scoped binding flags are emit details)
    }

    // checker.ts isInPropertyInitializerOrClassStaticBlock
    private bool IsInPropertyInitializerOrClassStaticBlock(NodeRef node, bool ignoreArrowFunctions = false)
    {
        var store = StoreOf(node);
        for (var n = node.Node; !n.IsNone; n = store.Parent(n))
        {
            switch (store.Kind(n))
            {
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.ClassStaticBlockDeclaration:
                    return true;
                case SyntaxKind.TypeQuery:
                case SyntaxKind.JsxClosingElement:
                    return false;
                case SyntaxKind.ArrowFunction:
                    if (!ignoreArrowFunctions)
                    {
                        return false;
                    }
                    break;
                case SyntaxKind.Block:
                    if (Utilities.IsFunctionLikeDeclaration(store, store.Parent(n)) && store.Kind(store.Parent(n)) != SyntaxKind.ArrowFunction)
                    {
                        return false;
                    }
                    break;
            }
        }
        return false;
    }

    // checker.ts checkPrivateIdentifierExpression / checkGrammarPrivateIdentifierExpression
    private Type CheckPrivateIdentifierExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (Utilities.GetContainingClass(store, n).IsNone)
        {
            GrammarErrorOnNode(node, Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
        }
        else if (store.Kind(store.Parent(n)) != SyntaxKind.ForInStatement)
        {
            if (!Utilities.IsExpressionNode(store, n))
            {
                GrammarErrorOnNode(node, Diagnostics.Private_identifiers_are_only_allowed_in_class_bodies_and_may_only_be_used_as_part_of_a_class_member_declaration_property_access_or_on_the_left_hand_side_of_an_in_expression);
            }
            else
            {
                var isInOperation = store.Kind(store.Parent(n)) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(store.Parent(n))) == SyntaxKind.InKeyword;
                if (GetSymbolForPrivateIdentifierExpression(node).IsNone && !isInOperation)
                {
                    GrammarErrorOnNode(node, Diagnostics.Cannot_find_name_0, store.Texts.GetString(store.EscapedText(n)));
                }
            }
        }
        var symbol = GetSymbolForPrivateIdentifierExpression(node);
        if (!symbol.IsNone)
        {
            MarkPropertyAsReferenced(symbol, NodeRef.None, isSelfTypeAccess: false);
        }
        return anyType;
    }

    // checker.ts getSymbolForPrivateIdentifierExpression
    private SymbolRef GetSymbolForPrivateIdentifierExpression(NodeRef privId)
    {
        if (!Utilities.IsExpressionNode(StoreOf(privId), privId.Node))
        {
            return SymbolRef.None;
        }
        ref var links = ref SymbolNodeLinks.Get(privId);
        if (links.ResolvedSymbol.IsNone)
        {
            var symbol = LookupSymbolForPrivateIdentifierDeclaration(privId);
            SymbolNodeLinks.Get(privId).ResolvedSymbol = symbol;
            return symbol;
        }
        return links.ResolvedSymbol;
    }

    // ---- this and super ----

    /// <summary>checker.ts checkThisExpression.</summary>
    internal Type CheckThisExpression(NodeRef node)
    {
        var store = StoreOf(node);
        // Stop at the first arrow function so that we can
        // tell whether 'this' needs to be captured.
        var container = Utilities.GetThisContainer(store, node.Node, includeArrowFunctions: true, includeClassComputedPropertyName: true);
        var capturedByArrowFunction = false;
        var thisInComputedPropertyName = false;
        if (store.Kind(container) == SyntaxKind.Constructor)
        {
            CheckThisBeforeSuper(node, At(node, container), Diagnostics.super_must_be_called_before_accessing_this_in_the_constructor_of_a_derived_class);
        }
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
        // (checkThisInStaticClassFieldInitializerInDecoratedClass: experimentalDecorators is not set in Gate C)
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

    // checker.ts checkThisBeforeSuper
    private void CheckThisBeforeSuper(NodeRef node, NodeRef container, DiagnosticMessage diagnosticMessage)
    {
        var containingClassDecl = Parent(container);
        var baseTypeNode = GetBaseTypeNodeOfClass(containingClassDecl);
        // If a containing class does not have extends clause or the class extends null
        // skip checking whether super statement is called before "this" accessing.
        if (!baseTypeNode.IsNone && !ClassDeclarationExtendsNull(containingClassDecl))
        {
            var flow = BindingOf(node.File).FlowNodeOf(node.Node);
            if (!flow.IsNone && !IsPostSuperFlowNode(node.File, flow, null))
            {
                Error(node, diagnosticMessage);
            }
        }
    }

    // checker.ts classDeclarationExtendsNull
    private bool ClassDeclarationExtendsNull(NodeRef classDecl)
    {
        var classSymbol = GetSymbolOfDeclaration(classDecl);
        var classInstanceType = GetDeclaredTypeOfSymbol(classSymbol);
        return GetBaseConstructorTypeOfClass(classInstanceType) == nullWideningType;
    }

    // checker.ts isPostSuperFlowNode (the antecedents of reduce labels are substituted through a map instead of being
    // written into the flow graph)
    private readonly Dictionary<(int, int), bool> flowNodePostSuper = [];

    private bool IsPostSuperFlowNode(int file, FlowNode flow, Dictionary<int, BinderStore.FlowChain>? reduced)
    {
        var binding = BindingOf(file);
        var store = StoreOf(file);
        while (true)
        {
            var flags = binding.Flags(flow);
            if ((flags & (FlowFlags.Assignment | FlowFlags.Condition | FlowFlags.ArrayMutation | FlowFlags.SwitchClause)) != 0)
            {
                flow = binding.Antecedent(flow);
            }
            else if ((flags & FlowFlags.Call) != 0)
            {
                if (store.Kind(store.Expression(binding.NodeOf(flow))) == SyntaxKind.SuperKeyword)
                {
                    return true;
                }
                flow = binding.Antecedent(flow);
            }
            else if ((flags & FlowFlags.BranchLabel) != 0)
            {
                // (flowNodePostSuper: the result of a branch label is cached, outside of reduce labels)
                if (reduced is null && flowNodePostSuper.TryGetValue((file, flow.Index), out var cachedPostSuper))
                {
                    return cachedPostSuper;
                }
                // A branching point is post-super if every branch is post-super.
                var antecedents = reduced is not null && reduced.TryGetValue(flow.Index, out var replacement) ? replacement : binding.Antecedents(flow);
                var result = true;
                foreach (var f in antecedents)
                {
                    if (!IsPostSuperFlowNode(file, f, reduced))
                    {
                        result = false;
                        break;
                    }
                }
                if (reduced is null)
                {
                    flowNodePostSuper[(file, flow.Index)] = result;
                }
                return result;
            }
            else if ((flags & FlowFlags.LoopLabel) != 0)
            {
                // A loop is post-super if the control flow path that leads to the top is post-super.
                var first = FlowNode.None;
                foreach (var f in binding.Antecedents(flow))
                {
                    first = f;
                    break;
                }
                flow = first;
                if (flow.IsNone)
                {
                    return false;
                }
            }
            else if ((flags & FlowFlags.ReduceLabel) != 0)
            {
                // the reduce label's target sees only the given antecedents while its antecedent is evaluated
                var map = reduced is null ? new Dictionary<int, BinderStore.FlowChain>() : new Dictionary<int, BinderStore.FlowChain>(reduced);
                map[binding.ReduceTarget(flow).Index] = binding.ReduceAntecedents(flow);
                return IsPostSuperFlowNode(file, binding.Antecedent(flow), map);
            }
            else
            {
                // Unreachable nodes are considered post-super to silence errors
                return (flags & FlowFlags.Unreachable) != 0;
            }
        }
    }

    // checker.ts getTypeForThisExpressionFromJSDoc
    private Type GetTypeForThisExpressionFromJSDoc(NodeRef node)
    {
        var store = StoreOf(node);
        var thisTag = GetFirstJSDocTagOfKind(store, node.Node, SyntaxKind.JSDocThisTag);
        if (!thisTag.IsNone && !store.TypeExpression(thisTag).IsNone)
        {
            return GetTypeFromTypeNode(At(node, store.TypeExpression(thisTag)));
        }
        var signature = GetSignatureOfTypeTag(node);
        return signature.IsNone ? Type.None : GetThisTypeOfSignature(signature);
    }

    // checker.ts getClassNameFromPrototypeMethod
    private static Node GetClassNameFromPrototypeMethod(NodeStore store, Node container)
    {
        var parent = store.Parent(container);
        var kind = store.Kind(container);
        // Check if it's the RHS of a x.prototype.y = function [name]() { .... }
        if (kind == SyntaxKind.FunctionExpression && store.Kind(parent) == SyntaxKind.BinaryExpression &&
            Utilities.GetAssignmentDeclarationKind(store, parent) == AssignmentDeclarationKind.PrototypeProperty)
        {
            // Get the 'x' of 'x.prototype.y = container'
            return store.Expression(store.Expression(store.Left(parent)));
        }
        // x.prototype = { method() { } }
        if (kind == SyntaxKind.MethodDeclaration && store.Kind(parent) == SyntaxKind.ObjectLiteralExpression &&
            store.Kind(store.Parent(parent)) == SyntaxKind.BinaryExpression && Utilities.GetAssignmentDeclarationKind(store, store.Parent(parent)) == AssignmentDeclarationKind.Prototype)
        {
            return store.Expression(store.Left(store.Parent(parent)));
        }
        // x.prototype = { method: function() { } }
        if (kind == SyntaxKind.FunctionExpression && store.Kind(parent) == SyntaxKind.PropertyAssignment && store.Kind(store.Parent(parent)) == SyntaxKind.ObjectLiteralExpression)
        {
            var binary = store.Parent(store.Parent(parent));
            if (store.Kind(binary) == SyntaxKind.BinaryExpression && Utilities.GetAssignmentDeclarationKind(store, binary) == AssignmentDeclarationKind.Prototype)
            {
                return store.Expression(store.Left(binary));
            }
        }
        // Object.defineProperty(x, "method", { value: function() { } }) and the get/set forms
        if (kind == SyntaxKind.FunctionExpression && store.Kind(parent) == SyntaxKind.PropertyAssignment && IsValueGetOrSetName(store, store.Name(parent)) &&
            store.Kind(store.Parent(parent)) == SyntaxKind.ObjectLiteralExpression)
        {
            var call = store.Parent(store.Parent(parent));
            if (store.Kind(call) == SyntaxKind.CallExpression && Utilities.GetAssignmentDeclarationKind(store, call) == AssignmentDeclarationKind.ObjectDefinePrototypeProperty &&
                store.Element(store.Arguments(call), 2) == store.Parent(parent))
            {
                return store.Expression(store.Element(store.Arguments(call), 0));
            }
        }
        // Object.defineProperty(x, "method", { value() { } }) and the get/set forms
        if (kind == SyntaxKind.MethodDeclaration && IsValueGetOrSetName(store, store.Name(container)) && store.Kind(parent) == SyntaxKind.ObjectLiteralExpression)
        {
            var call = store.Parent(parent);
            if (store.Kind(call) == SyntaxKind.CallExpression && Utilities.GetAssignmentDeclarationKind(store, call) == AssignmentDeclarationKind.ObjectDefinePrototypeProperty &&
                store.Element(store.Arguments(call), 2) == parent)
            {
                return store.Expression(store.Element(store.Arguments(call), 0));
            }
        }
        return Node.None;

        static bool IsValueGetOrSetName(NodeStore store, Node name) => store.Kind(name) == SyntaxKind.Identifier &&
            (store.Texts.Equals(store.EscapedText(name), "value") || store.Texts.Equals(store.EscapedText(name), "get") || store.Texts.Equals(store.EscapedText(name), "set"));
    }

    // checker.ts tryGetThisTypeAt
    private Type TryGetThisTypeAt(NodeRef node, bool includeGlobalThis, NodeRef container)
    {
        var store = StoreOf(node);
        var c = container.Node;
        if (!c.IsNone && Utilities.IsFunctionLike(store, c) && (!IsInParameterInitializerBeforeContainingFunction(node) || !GetThisParameter(store, c).IsNone))
        {
            var isInJS = Utilities.IsInJSFile(store, node.Node);
            var thisType = GetThisTypeOfDeclaration(container);
            if (thisType.IsNone && isInJS)
            {
                thisType = GetTypeForThisExpressionFromJSDoc(container);
            }
            // Note: a parameter initializer should refer to class-this unless function-this is explicitly annotated.
            // If this is a function in a JS file, it might be a class method.
            if (thisType.IsNone)
            {
                var className = GetClassNameFromPrototypeMethod(store, c);
                if (isInJS && !className.IsNone)
                {
                    var classSymbol = Types.Symbol(CheckExpression(At(node, className)));
                    if (!classSymbol.IsNone && MembersOf(classSymbol) is not null && (SymbolFlagsOf(classSymbol) & SymbolFlags.Function) != 0)
                    {
                        thisType = ThisTypeOfClassOrInterface(GetDeclaredTypeOfSymbol(classSymbol));
                    }
                }
                else if (IsJSConstructor(container))
                {
                    thisType = ThisTypeOfClassOrInterface(GetDeclaredTypeOfSymbol(GetMergedSymbol(NodeSymbol(container))));
                }
                if (thisType.IsNone)
                {
                    thisType = GetContextualThisParameterType(container);
                }
            }
            if (!thisType.IsNone)
            {
                return GetFlowTypeOfReference(node, thisType);
            }
        }
        var parent = c.IsNone ? Node.None : store.Parent(c);
        if (!parent.IsNone && Utilities.IsClassLike(store, parent))
        {
            var symbol = GetSymbolOfDeclaration(At(node, parent));
            var type = Utilities.IsStatic(store, c) ? GetTypeOfSymbol(symbol) : ThisTypeOfClassOrInterface(GetDeclaredTypeOfSymbol(symbol));
            return GetFlowTypeOfReference(node, type);
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
                // TODO: Maybe issue a better error than 'object is possibly undefined'
                return undefinedType;
            }
            if (includeGlobalThis)
            {
                return GetTypeOfSymbol(globalThisSymbol);
            }
        }
        return Type.None;
    }

    // checker.ts getThisTypeOfDeclaration
    private Type GetThisTypeOfDeclaration(NodeRef declaration) => GetThisTypeOfSignature(GetSignatureFromDeclaration(declaration));

    // checker.ts isInParameterInitializerBeforeContainingFunction
    private bool IsInParameterInitializerBeforeContainingFunction(NodeRef node)
    {
        var store = StoreOf(node);
        var inBindingInitializer = false;
        var n = node.Node;
        while (!store.Parent(n).IsNone && !Utilities.IsFunctionLike(store, store.Parent(n)))
        {
            var parent = store.Parent(n);
            if (store.Kind(parent) == SyntaxKind.Parameter && (inBindingInitializer || store.Initializer(parent) == n))
            {
                return true;
            }
            if (store.Kind(parent) == SyntaxKind.BindingElement && store.Initializer(parent) == n)
            {
                inBindingInitializer = true;
            }
            n = parent;
        }
        return false;
    }

    // checker.ts getContainingObjectLiteral
    private static Node GetContainingObjectLiteral(NodeStore store, Node func) =>
        store.Kind(func) is SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor && store.Kind(store.Parent(func)) == SyntaxKind.ObjectLiteralExpression ? store.Parent(func)
        : store.Kind(func) == SyntaxKind.FunctionExpression && store.Kind(store.Parent(func)) == SyntaxKind.PropertyAssignment ? store.Parent(store.Parent(func))
        : Node.None;

    // checker.ts getThisTypeArgument
    private Type GetThisTypeArgument(Type type) =>
        (Types.ObjectFlagsOf(type) & ObjectFlags.Reference) != 0 && !globalThisType.IsNone && Types.Object(type).Target == globalThisType ? GetTypeArguments(type)[0] : Type.None;

    // checker.ts getThisTypeFromContextualType
    private Type GetThisTypeFromContextualType(Type type) => MapType(type, t =>
    {
        if ((Types.Flags(t) & TypeFlags.Intersection) != 0)
        {
            foreach (var member in Types.Elements(Types.Union(t).Types).ToArray())
            {
                var argument = GetThisTypeArgument(member);
                if (!argument.IsNone)
                {
                    return argument;
                }
            }
            return Type.None;
        }
        return GetThisTypeArgument(t);
    });

    // checker.ts getThisTypeOfObjectLiteralFromContextualType
    private Type GetThisTypeOfObjectLiteralFromContextualType(NodeRef containingLiteral, Type contextualType)
    {
        var literal = containingLiteral;
        var type = contextualType;
        var store = StoreOf(containingLiteral);
        while (!type.IsNone)
        {
            var thisType = GetThisTypeFromContextualType(type);
            if (!thisType.IsNone)
            {
                return thisType;
            }
            if (store.Kind(store.Parent(literal.Node)) != SyntaxKind.PropertyAssignment)
            {
                break;
            }
            literal = At(literal, store.Parent(store.Parent(literal.Node)));
            type = GetApparentTypeOfContextualType(literal, ContextFlags.None);
        }
        return Type.None;
    }

    /// <summary>checker.ts getContextualThisParameterType.</summary>
    internal Type GetContextualThisParameterType(NodeRef func)
    {
        var store = StoreOf(func);
        if (store.Kind(func.Node) == SyntaxKind.ArrowFunction)
        {
            return Type.None;
        }
        if (IsContextSensitiveFunctionOrObjectLiteralMethod(store, func.Node))
        {
            var contextualSignature = GetContextualSignature(func);
            if (!contextualSignature.IsNone)
            {
                var thisParameter = Signatures.Record(contextualSignature).ThisParameter;
                if (!thisParameter.IsNone)
                {
                    return GetTypeOfSymbol(thisParameter);
                }
            }
        }
        var inJs = Utilities.IsInJSFile(store, func.Node);
        if (compilerOptions.GetStrictOptionValue(compilerOptions.NoImplicitThis) || inJs)
        {
            var containingLiteral = GetContainingObjectLiteral(store, func.Node);
            if (!containingLiteral.IsNone)
            {
                // We have an object literal method. Check if the containing object literal has a contextual type
                // that includes a ThisType<T>. If so, T is the contextual type for 'this'. We continue looking in
                // any directly enclosing object literals.
                var literal = At(func, containingLiteral);
                var contextualType = GetApparentTypeOfContextualType(literal, ContextFlags.None);
                var thisType = GetThisTypeOfObjectLiteralFromContextualType(literal, contextualType);
                if (!thisType.IsNone)
                {
                    return InstantiateType(thisType, GetMapperFromContext(GetInferenceContext(literal)));
                }
                // There was no contextual ThisType<T> for the containing object literal, so the contextual type
                // for 'this' is the non-null form of the contextual type for the containing object literal or
                // the type of the object literal itself.
                return GetWidenedType(!contextualType.IsNone ? GetNonNullableType(contextualType) : CheckExpressionCached(literal));
            }
            // In an assignment of the form 'obj.xxx = function(...)' or 'obj[xxx] = function(...)', the
            // contextual type for 'this' is 'obj'.
            var parent = WalkUpParenthesizedExpressions(store, store.Parent(func.Node));
            if (Utilities.IsAssignmentExpression(store, parent))
            {
                var target = store.Left(parent);
                if (Utilities.IsAccessExpression(store, target))
                {
                    var expression = store.Expression(target);
                    // Don't contextually type `this` as `exports` in `exports.Point = function(x, y) { this.x = x; this.y = y; }`
                    if (inJs && store.Kind(expression) == SyntaxKind.Identifier)
                    {
                        var binding = BindingOf(func.File);
                        if (!binding.CommonJsModuleIndicator.IsNone && GetResolvedSymbol(At(func, expression)) == GetSymbolOfDeclaration(At(func, Utilities.RootOf(store, func.Node))))
                        {
                            return Type.None;
                        }
                    }
                    return GetWidenedType(CheckExpressionCached(At(func, expression)));
                }
            }
        }
        return Type.None;
    }

    /// <summary>checker.ts isContextSensitiveFunctionOrObjectLiteralMethod.</summary>
    internal static bool IsContextSensitiveFunctionOrObjectLiteralMethod(NodeStore store, Node func) =>
        (IsFunctionExpressionOrArrowFunction(store, func) || Utilities.IsObjectLiteralMethod(store, func)) && IsContextSensitiveFunctionLikeDeclaration(store, func);

    // checker.ts isContextSensitiveFunctionLikeDeclaration
    private static bool IsContextSensitiveFunctionLikeDeclaration(NodeStore store, Node node) =>
        HasContextSensitiveParameters(store, node) || HasContextSensitiveReturnExpression(store, node) || HasContextSensitiveYieldExpression(store, node);

    // (hasContextSensitiveParameters: Checker.RelationHelpers.cs, C4)

    // checker.ts hasContextSensitiveReturnExpression
    private static bool HasContextSensitiveReturnExpression(NodeStore store, Node node)
    {
        if (!store.TypeParameters(node).IsNone || !GetEffectiveReturnTypeNode(store, node).IsNone || store.Body(node).IsNone)
        {
            return false;
        }
        var body = store.Body(node);
        if (store.Kind(body) != SyntaxKind.Block)
        {
            return IsContextSensitive(store, body);
        }
        return ForEachReturnStatementAny(store, body, statement => !store.Expression(statement).IsNone && IsContextSensitive(store, store.Expression(statement)));
    }

    // checker.ts hasContextSensitiveYieldExpression
    private static bool HasContextSensitiveYieldExpression(NodeStore store, Node node)
    {
        if ((GetFunctionFlagsOf(store, node) & FunctionFlags.Generator) == 0 || store.Body(node).IsNone)
        {
            return false;
        }
        var found = false;
        ForEachYieldExpression(store, store.Body(node), y =>
        {
            if (!found && IsContextSensitive(store, y))
            {
                found = true;
            }
        });
        return found;
    }

    /// <summary>checker.ts isContextSensitive.</summary>
    internal static bool IsContextSensitive(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.FunctionDeclaration: // Function declarations can have context when annotated with a jsdoc @type
                return IsContextSensitiveFunctionLikeDeclaration(store, node);
            case SyntaxKind.ObjectLiteralExpression:
                foreach (var p in store.Nodes(store.Properties(node)))
                {
                    if (IsContextSensitive(store, p))
                    {
                        return true;
                    }
                }
                return false;
            case SyntaxKind.ArrayLiteralExpression:
                foreach (var e in store.Nodes(store.Elements(node)))
                {
                    if (IsContextSensitive(store, e))
                    {
                        return true;
                    }
                }
                return false;
            case SyntaxKind.ConditionalExpression:
                return IsContextSensitive(store, store.WhenTrue(node)) || IsContextSensitive(store, store.WhenFalse(node));
            case SyntaxKind.BinaryExpression:
                return store.Kind(store.OperatorToken(node)) is SyntaxKind.BarBarToken or SyntaxKind.QuestionQuestionToken &&
                    (IsContextSensitive(store, store.Left(node)) || IsContextSensitive(store, store.Right(node)));
            case SyntaxKind.PropertyAssignment:
                return IsContextSensitive(store, store.Initializer(node));
            case SyntaxKind.ParenthesizedExpression:
                return IsContextSensitive(store, store.Expression(node));
            case SyntaxKind.JsxAttributes:
            {
                foreach (var p in store.Nodes(store.Properties(node)))
                {
                    if (IsContextSensitive(store, p))
                    {
                        return true;
                    }
                }
                if (store.Kind(store.Parent(node)) == SyntaxKind.JsxOpeningElement)
                {
                    foreach (var child in store.Nodes(store.Children(store.Parent(store.Parent(node)))))
                    {
                        if (IsContextSensitive(store, child))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            case SyntaxKind.JsxAttribute:
            {
                // If there is no initializer, JSX attribute has a boolean value of true which is not context sensitive.
                var initializer = store.Initializer(node);
                return !initializer.IsNone && IsContextSensitive(store, initializer);
            }
            case SyntaxKind.JsxExpression:
            case SyntaxKind.YieldExpression:
            {
                // It is possible to that node.expression is undefined (e.g <div x={} />)
                var expression = store.Expression(node);
                return !expression.IsNone && IsContextSensitive(store, expression);
            }
        }
        return false;
    }

    /// <summary>checker.ts checkSuperExpression.</summary>
    private Type CheckSuperExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var isCallExpression = store.Kind(store.Parent(n)) == SyntaxKind.CallExpression && store.Expression(store.Parent(n)) == n;
        var immediateContainer = GetSuperContainer(store, n, stopOnFunctions: true);
        var container = immediateContainer;
        // adjust the container reference in case if super is used inside arrow functions with arbitrarily deep nesting
        if (!isCallExpression)
        {
            while (!container.IsNone && store.Kind(container) == SyntaxKind.ArrowFunction)
            {
                container = GetSuperContainer(store, container, stopOnFunctions: true);
            }
        }
        if (container.IsNone || !IsLegalUsageOfSuperExpression(store, container, isCallExpression))
        {
            // issue more specific error if super is used in computed property name
            // class A { foo() { return "1" }}
            // class B {
            //     [super.foo()]() {}
            // }
            var current = Node.None;
            for (var a = n; !a.IsNone; a = store.Parent(a))
            {
                if (a == container)
                {
                    break;
                }
                if (store.Kind(a) == SyntaxKind.ComputedPropertyName)
                {
                    current = a;
                    break;
                }
            }
            if (!current.IsNone)
            {
                Error(node, Diagnostics.super_cannot_be_referenced_in_a_computed_property_name);
            }
            else if (isCallExpression)
            {
                Error(node, Diagnostics.Super_calls_are_not_permitted_outside_constructors_or_in_nested_functions_inside_constructors);
            }
            else if (container.IsNone || store.Parent(container).IsNone || !(Utilities.IsClassLike(store, store.Parent(container)) || store.Kind(store.Parent(container)) == SyntaxKind.ObjectLiteralExpression))
            {
                Error(node, Diagnostics.super_can_only_be_referenced_in_members_of_derived_classes_or_object_literal_expressions);
            }
            else
            {
                Error(node, Diagnostics.super_property_access_is_permitted_only_in_a_constructor_member_function_or_member_accessor_of_a_derived_class);
            }
            return errorType;
        }
        if (!isCallExpression && store.Kind(immediateContainer) == SyntaxKind.Constructor)
        {
            CheckThisBeforeSuper(node, At(node, container), Diagnostics.super_must_be_called_before_accessing_a_property_of_super_in_the_constructor_of_a_derived_class);
        }
        var isSuperStatic = Utilities.IsStatic(store, container) || isCallExpression;
        NodeLinks.Get(node).Flags |= isSuperStatic ? NodeCheckFlags.SuperStatic : NodeCheckFlags.SuperInstance;
        if (store.Kind(store.Parent(container)) == SyntaxKind.ObjectLiteralExpression)
        {
            if (languageVersion < ScriptTarget.ES2015)
            {
                Error(node, Diagnostics.super_is_only_allowed_in_members_of_object_literal_expressions_when_option_target_is_ES2015_or_higher);
                return errorType;
            }
            // for object literal assume that type of 'super' is 'any'
            return anyType;
        }
        // at this point the only legal case for parent is ClassLikeDeclaration
        var classLikeDeclaration = At(node, store.Parent(container));
        if (GetBaseTypeNodeOfClass(classLikeDeclaration).IsNone)
        {
            Error(node, Diagnostics.super_can_only_be_referenced_in_a_derived_class);
            return errorType;
        }
        if (ClassDeclarationExtendsNull(classLikeDeclaration))
        {
            return isCallExpression ? errorType : nullWideningType;
        }
        var classType = GetDeclaredTypeOfSymbol(GetSymbolOfDeclaration(classLikeDeclaration));
        var baseTypes = GetBaseTypes(classType);
        if (baseTypes.Count == 0)
        {
            return errorType;
        }
        var baseClassType = baseTypes[0];
        if (store.Kind(container) == SyntaxKind.Constructor && IsInConstructorArgumentInitializer(store, n, container))
        {
            // issue custom error message for super property access in constructor arguments (to be aligned with old compiler)
            Error(node, Diagnostics.super_cannot_be_referenced_in_constructor_arguments);
            return errorType;
        }
        return isSuperStatic ? GetBaseConstructorTypeOfClass(classType) : GetTypeWithThisArgument(baseClassType, ThisTypeOfClassOrInterface(classType));
    }

    // checker.ts checkSuperExpression's isLegalUsageOfSuperExpression
    private static bool IsLegalUsageOfSuperExpression(NodeStore store, Node container, bool isCallExpression)
    {
        if (isCallExpression)
        {
            // TS 1.0 SPEC (April 2014): 4.8.1
            // Super calls are only permitted in constructors of derived classes
            return store.Kind(container) == SyntaxKind.Constructor;
        }
        // TS 1.0 SPEC (April 2014)
        // 'super' property access is allowed
        // - In a constructor, instance member function, instance member accessor, or instance member variable initializer where this references a derived class instance
        // - In a static member function or static member accessor
        // topmost container must be something that is directly nested in the class declaration\object literal expression
        var parent = store.Parent(container);
        if (Utilities.IsClassLike(store, parent) || store.Kind(parent) == SyntaxKind.ObjectLiteralExpression)
        {
            if (Utilities.IsStatic(store, container))
            {
                return store.Kind(container) is SyntaxKind.MethodDeclaration or SyntaxKind.MethodSignature or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor
                    or SyntaxKind.PropertyDeclaration or SyntaxKind.ClassStaticBlockDeclaration;
            }
            return store.Kind(container) is SyntaxKind.MethodDeclaration or SyntaxKind.MethodSignature or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor
                or SyntaxKind.PropertyDeclaration or SyntaxKind.PropertySignature or SyntaxKind.Constructor;
        }
        return false;
    }

    // utilities.ts getSuperContainer
    private static Node GetSuperContainer(NodeStore store, Node node, bool stopOnFunctions)
    {
        while (true)
        {
            node = store.Parent(node);
            if (node.IsNone)
            {
                return Node.None;
            }
            switch (store.Kind(node))
            {
                case SyntaxKind.ComputedPropertyName:
                    node = store.Parent(node);
                    break;
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ArrowFunction:
                    if (!stopOnFunctions)
                    {
                        continue;
                    }
                    return node;
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.Constructor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.ClassStaticBlockDeclaration:
                    return node;
                case SyntaxKind.Decorator:
                    // Decorators are always applied outside of the body of a class or method.
                    if (store.Kind(store.Parent(node)) == SyntaxKind.Parameter && Utilities.IsClassElement(store, store.Parent(store.Parent(node))))
                    {
                        // If the decorator's parent is a Parameter, we resolve the this container from
                        // the grandparent class declaration.
                        node = store.Parent(store.Parent(node));
                    }
                    else if (Utilities.IsClassElement(store, store.Parent(node)))
                    {
                        // If the decorator's parent is a class element, we resolve the 'this' container
                        // from the parent class declaration.
                        node = store.Parent(node);
                    }
                    break;
            }
        }
    }

    // checker.ts isInConstructorArgumentInitializer
    private static bool IsInConstructorArgumentInitializer(NodeStore store, Node node, Node constructorDecl)
    {
        for (var n = node; !n.IsNone; n = store.Parent(n))
        {
            if (Utilities.IsFunctionLikeDeclaration(store, n))
            {
                return false;
            }
            if (store.Kind(n) == SyntaxKind.Parameter && store.Parent(n) == constructorDecl)
            {
                return true;
            }
        }
        return false;
    }
}
