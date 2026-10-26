// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (module C5; the functions are listed below)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (details below).
// Functions (function and class expressions: checkFunctionExpressionOrObjectLiteralMethod,
// contextuallyCheckFunctionExpressionOrObjectLiteralMethod, inferFromAnnotatedParametersAndReturn,
// assignContextualParameterTypes, assignNonContextualParameterTypes, assignParameterType, assignBindingElementTypes,
// checkFunctionExpressionOrObjectLiteralMethodDeferred, checkClassExpression, checkClassExpressionDeferred,
// getReturnTypeFromBody, checkAndAggregateYieldOperandTypes, getYieldedTypeOfYieldExpression,
// checkAndAggregateReturnExpressionTypes, mayReturnNever, checkYieldExpression, checkAwaitExpression,
// checkGeneratorInstantiationAssignabilityToReturnType, isUnitType) and src/compiler/utilities.ts
// (forEachReturnStatement, forEachYieldExpression, expressionResultIsUnused)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (replaces C1's getReturnTypeFromBody). TODO(C6): the
// inferences of inferFromAnnotatedParametersAndReturn; parameters contextually typed by a signature instantiated with an
// inference context are marked with C1's "uncertain" mechanism. TODO(C7): functionHasImplicitReturn (reachability) and
// isSymbolAssigned. TODO(C8): the grammar checks of await and yield expressions and checkReturnExpression.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private readonly HashSet<NodeRef> checkedClassExpressions = [];

    // the implicit any errors of parameters whose contextual type depends on inferences are not reported (TODO(C6))
    private int suppressImplicitAnyDepth;

    /// <summary>checker.ts checkFunctionExpressionOrObjectLiteralMethod.</summary>
    internal Type CheckFunctionExpressionOrObjectLiteralMethod(NodeRef node, CheckMode checkMode)
    {
        CheckNodeDeferred(node);
        // (checkCollisionsForDeclarationName: C8)
        // The identityMapper object is used to indicate that function expressions are wildcards
        if ((checkMode & CheckMode.SkipContextSensitive) != 0 && IsContextSensitive(node))
        {
            var store = StoreOf(node);
            // Skip parameters, return signature with return type that retains noncontextual parts so inferences can still be drawn in an early stage
            if (GetEffectiveReturnTypeNode(store, node.Node).IsNone && !HasContextSensitiveParameters(store, node.Node))
            {
                // Return plain anyFunctionType if there is no possibility we'll make inferences from the return type
                var contextualSignature = GetContextualSignature(node);
                if (!contextualSignature.IsNone && CouldContainTypeVariables(GetReturnTypeOfSignature(contextualSignature)))
                {
                    ref var links = ref ExpressionLinks.Get(node);
                    if (!links.ContextFreeType.IsNone)
                    {
                        return links.ContextFreeType;
                    }
                    var returnType = GetReturnTypeFromBody(node, checkMode);
                    var returnOnlySignature = CreateSignature(NodeRef.None, TypeList.Empty, SymbolRef.None, [], returnType, 0, SignatureFlags.IsNonInferrable);
                    var returnOnlyType = CreateAnonymousType(GetSymbolOfDeclaration(node), emptySymbols, [returnOnlySignature], [], []);
                    Types.Record(returnOnlyType).ObjectFlags |= ObjectFlags.NonInferrableType;
                    return ExpressionLinks.Get(node).ContextFreeType = returnOnlyType;
                }
            }
            return anyFunctionType;
        }
        // (checkGrammarFunctionLikeDeclaration, checkGrammarForGenerator: C8)
        ContextuallyCheckFunctionExpressionOrObjectLiteralMethod(node, checkMode);
        return GetTypeOfSymbol(GetSymbolOfDeclaration(node));
    }

    // checker.ts contextuallyCheckFunctionExpressionOrObjectLiteralMethod
    private void ContextuallyCheckFunctionExpressionOrObjectLiteralMethod(NodeRef node, CheckMode checkMode)
    {
        // Check if function expression is contextually typed and assign parameter types if so.
        if ((NodeLinks.Get(node).Flags & NodeCheckFlags.ContextChecked) != 0)
        {
            return;
        }
        var contextualSignature = GetContextualSignature(node);
        // If a type check is started at a function expression that is an argument of a function call, obtaining the
        // contextual type may recursively get back to here during overload resolution of the call. If so, we will have
        // already assigned contextual types.
        if ((NodeLinks.Get(node).Flags & NodeCheckFlags.ContextChecked) != 0)
        {
            return;
        }
        NodeLinks.Get(node).Flags |= NodeCheckFlags.ContextChecked;
        var signatures = GetSignaturesOfType(GetTypeOfSymbol(GetSymbolOfDeclaration(node)), SignatureKind.Call);
        if (signatures.Count == 0)
        {
            return;
        }
        var signature = signatures[0];
        var store = StoreOf(node);
        if (IsContextSensitive(node))
        {
            if (!contextualSignature.IsNone)
            {
                var inferenceContext = GetInferenceContext(node);
                var instantiatedContextualSignature = Signature.None;
                if ((checkMode & CheckMode.Inferential) != 0)
                {
                    InferFromAnnotatedParametersAndReturn(signature, contextualSignature, inferenceContext!);
                    var restType = GetEffectiveRestType(contextualSignature);
                    if (!restType.IsNone && (Types.Flags(restType) & TypeFlags.TypeParameter) != 0)
                    {
                        instantiatedContextualSignature = InstantiateSignature(contextualSignature, inferenceContext!.NonFixingMapper);
                    }
                }
                if (instantiatedContextualSignature.IsNone)
                {
                    instantiatedContextualSignature = inferenceContext is not null ? InstantiateSignature(contextualSignature, inferenceContext.Mapper) : contextualSignature;
                }
                if (inferenceContext is not null || IsUncertainSignature(contextualSignature) || IsInUncertainContext(node))
                {
                    // TODO(C6): the contextual signature is instantiated with inferences that are not made yet
                    MarkUncertainParameters(node);
                }
                AssignContextualParameterTypes(signature, instantiatedContextualSignature);
            }
            else
            {
                // Force resolution of all parameter types such that the absence of a contextual type is consistently reflected.
                if (IsInUncertainContext(node, inferentialArgument: true))
                {
                    // TODO(C6): the contextual type depends on inferences that are not made yet; the implicit any
                    // errors are not reported
                    MarkUncertainParameters(node);
                    suppressImplicitAnyDepth++;
                    AssignNonContextualParameterTypes(signature);
                    suppressImplicitAnyDepth--;
                }
                else
                {
                    AssignNonContextualParameterTypes(signature);
                }
            }
        }
        else if (!contextualSignature.IsNone && store.TypeParameters(node.Node).IsNone && ParametersOf(contextualSignature).Length > store.Length(store.Parameters(node.Node)))
        {
            var inferenceContext = GetInferenceContext(node);
            if ((checkMode & CheckMode.Inferential) != 0)
            {
                InferFromAnnotatedParametersAndReturn(signature, contextualSignature, inferenceContext!);
            }
        }
        if (!contextualSignature.IsNone && GetReturnTypeFromAnnotation(node).IsNone && Signatures.Record(signature).ResolvedReturnType.IsNone)
        {
            var returnType = GetReturnTypeFromBody(node, checkMode);
            ref var record = ref Signatures.Record(signature);
            if (record.ResolvedReturnType.IsNone)
            {
                record.ResolvedReturnType = returnType;
            }
        }
        // checkSignatureDeclaration
        CheckSignatureLikeDeclaration(node, deferBody: true);
    }

    // a contextual signature whose declaration's types are approximations (C1's "uncertain" mechanism)
    private bool IsUncertainSignature(Signature signature)
    {
        var declaration = Signatures.Record(signature).Declaration;
        return !declaration.IsNone && IsExpressionKind(Kind(declaration)) && IsUncertainExpression(declaration);
    }

    private static bool IsExpressionKind(SyntaxKind kind) => kind is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction;

    // C1's "uncertain" mechanism: the contextual type of the expression comes from a call or a function whose types are
    // approximations (TODO(C6))
    private bool IsInUncertainContext(NodeRef node, bool inferentialArgument = false)
    {
        var store = StoreOf(node);
        var n = node.Node;
        while (true)
        {
            var parent = store.Parent(n);
            switch (store.Kind(parent))
            {
                case SyntaxKind.ParenthesizedExpression:
                case SyntaxKind.ConditionalExpression:
                case SyntaxKind.ArrayLiteralExpression:
                case SyntaxKind.SpreadElement:
                case SyntaxKind.ObjectLiteralExpression:
                case SyntaxKind.PropertyAssignment:
                case SyntaxKind.ShorthandPropertyAssignment:
                case SyntaxKind.BinaryExpression:
                case SyntaxKind.ReturnStatement:
                    n = parent;
                    continue;
                case SyntaxKind.CallExpression:
                case SyntaxKind.NewExpression:
                case SyntaxKind.TaggedTemplateExpression:
                    if (store.Expression(parent) == n)
                    {
                        return false;
                    }
                    if (uncertainExpressions.Contains(At(node, parent)))
                    {
                        return true;
                    }
                    if (inferentialArgument)
                    {
                        // the argument is checked in an inference context (the contextual type depends on inferences)
                        var argument = At(node, n);
                        for (var i = inferenceContextCount - 1; i >= 0; i--)
                        {
                            if (inferenceContextNodes[i] == argument && inferenceContexts[i] is not null)
                            {
                                return true;
                            }
                        }
                    }
                    return false;
                case SyntaxKind.ArrowFunction:
                case SyntaxKind.FunctionExpression:
                    return uncertainExpressions.Contains(At(node, parent));
                case SyntaxKind.Block:
                {
                    var func = store.Parent(parent);
                    return store.Kind(func) is SyntaxKind.ArrowFunction or SyntaxKind.FunctionExpression or SyntaxKind.MethodDeclaration && uncertainExpressions.Contains(At(node, func));
                }
                default:
                    return false;
            }
        }
    }

    // C1's "uncertain" mechanism: the types of the function's parameters are approximations (TODO(C6))
    private void MarkUncertainParameters(NodeRef func)
    {
        var store = StoreOf(func);
        uncertainExpressions.Add(func);
        foreach (var parameter in store.Nodes(store.Parameters(func.Node)))
        {
            if (!store.Type(parameter).IsNone)
            {
                continue;
            }
            MarkUncertainBindingName(At(func, parameter));
        }
    }

    private void MarkUncertainBindingName(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var name = store.Name(declaration.Node);
        if (Utilities.IsBindingPattern(store, name))
        {
            foreach (var element in store.Nodes(store.Elements(name)))
            {
                if (store.Kind(element) == SyntaxKind.BindingElement)
                {
                    MarkUncertainBindingName(At(declaration, element));
                }
            }
            return;
        }
        var symbol = GetSymbolOfDeclaration(declaration);
        if (!symbol.IsNone)
        {
            uncertainSymbols.Add(symbol);
        }
    }

    // checker.ts inferFromAnnotatedParametersAndReturn
    private void InferFromAnnotatedParametersAndReturn(Signature signature, Signature context, InferenceContext inferenceContext)
    {
        var parameters = ParametersOf(signature);
        var len = parameters.Length - (SignatureHasRestParameter(signature) ? 1 : 0);
        for (var i = 0; i < len; i++)
        {
            var declaration = ValueDeclarationOf(parameters[i]);
            var typeNode = declaration.IsNone ? NodeRef.None : GetEffectiveTypeAnnotationNode(declaration);
            if (!typeNode.IsNone)
            {
                var source = AddOptionality(GetTypeFromTypeNode(typeNode), isProperty: false, IsOptionalDeclaration(declaration));
                var target = GetTypeAtPosition(context, i);
                InferTypes(inferenceContext.Inferences, source, target);
            }
        }
        var signatureDeclaration = Signatures.Record(signature).Declaration;
        var returnTypeNode = signatureDeclaration.IsNone ? Node.None : GetEffectiveReturnTypeNode(StoreOf(signatureDeclaration), signatureDeclaration.Node);
        if (!returnTypeNode.IsNone)
        {
            var source = GetTypeFromTypeNode(At(signatureDeclaration, returnTypeNode));
            var target = GetReturnTypeOfSignature(context);
            InferTypes(inferenceContext.Inferences, source, target);
        }
    }

    // checker.ts assignContextualParameterTypes
    private void AssignContextualParameterTypes(Signature signature, Signature context)
    {
        ref var record = ref Signatures.Record(signature);
        var contextRecord = Signatures.Record(context);
        if (contextRecord.TypeParameters.Count > 0)
        {
            if (record.TypeParameters.Count == 0)
            {
                record.TypeParameters = contextRecord.TypeParameters;
            }
            else
            {
                return; // This signature has already has a contextual inference performed and cached on it!
            }
        }
        if (!contextRecord.ThisParameter.IsNone)
        {
            var parameter = record.ThisParameter;
            var parameterDeclaration = parameter.IsNone ? NodeRef.None : ValueDeclarationOf(parameter);
            if (parameter.IsNone || (!parameterDeclaration.IsNone && StoreOf(parameterDeclaration).Type(parameterDeclaration.Node).IsNone))
            {
                if (parameter.IsNone)
                {
                    Signatures.Record(signature).ThisParameter = CreateSymbolWithType(contextRecord.ThisParameter, Type.None);
                }
                AssignParameterType(Signatures.Record(signature).ThisParameter, GetTypeOfSymbol(contextRecord.ThisParameter));
            }
        }
        var parameters = ParametersOf(signature);
        var len = parameters.Length - (SignatureHasRestParameter(signature) ? 1 : 0);
        for (var i = 0; i < len; i++)
        {
            var parameter = parameters[i];
            var declaration = ValueDeclarationOf(parameter);
            if (GetEffectiveTypeAnnotationNode(declaration).IsNone)
            {
                var type = TryGetTypeAtPosition(context, i);
                if (!type.IsNone && !StoreOf(declaration).Initializer(declaration.Node).IsNone)
                {
                    var initializerType = CheckDeclarationInitializer(declaration, CheckMode.Normal);
                    if (!IsTypeAssignableTo(initializerType, type))
                    {
                        initializerType = WidenTypeInferredFromInitializer(declaration, initializerType);
                        if (IsTypeAssignableTo(type, initializerType))
                        {
                            type = initializerType;
                        }
                    }
                }
                AssignParameterType(parameter, type);
            }
        }
        if (SignatureHasRestParameter(signature))
        {
            // parameter might be a transient symbol generated by use of `arguments` in the function body.
            var parameter = parameters[^1];
            var declaration = ValueDeclarationOf(parameter);
            if (!declaration.IsNone
                ? GetEffectiveTypeAnnotationNode(declaration).IsNone
                : (GetCheckFlags(parameter) & CheckFlags.DeferredType) != 0)
            {
                var contextualParameterType = GetRestTypeAtPosition(context, len);
                AssignParameterType(parameter, contextualParameterType);
            }
        }
    }

    // checker.ts assignNonContextualParameterTypes
    private void AssignNonContextualParameterTypes(Signature signature)
    {
        var thisParameter = Signatures.Record(signature).ThisParameter;
        if (!thisParameter.IsNone)
        {
            AssignParameterType(thisParameter);
        }
        foreach (var parameter in ParametersOf(signature))
        {
            AssignParameterType(parameter);
        }
    }

    // checker.ts assignParameterType
    private void AssignParameterType(SymbolRef parameter, Type contextualType = default)
    {
        if (!ValueSymbolLinks.Get(parameter).ResolvedType.IsNone)
        {
            return;
        }
        var declaration = ValueDeclarationOf(parameter);
        var type = !contextualType.IsNone ? contextualType
            : !declaration.IsNone ? GetWidenedTypeForVariableLikeDeclaration(declaration, reportErrors: suppressImplicitAnyDepth == 0)
            : GetTypeOfSymbol(parameter);
        var isOptional = !declaration.IsNone && StoreOf(declaration).Initializer(declaration.Node).IsNone && IsOptionalDeclaration(declaration);
        type = AddOptionality(type, isProperty: false, isOptional);
        ref var links = ref ValueSymbolLinks.Get(parameter);
        if (links.ResolvedType.IsNone)
        {
            links.ResolvedType = type;
        }
        if (!declaration.IsNone)
        {
            var store = StoreOf(declaration);
            var name = store.Name(declaration.Node);
            if (store.Kind(name) != SyntaxKind.Identifier)
            {
                // if inference didn't come up with anything but unknown, fall back to the binding pattern if present.
                if (ValueSymbolLinks.Get(parameter).ResolvedType == unknownType)
                {
                    ValueSymbolLinks.Get(parameter).ResolvedType = GetTypeFromBindingPattern(At(declaration, name));
                }
                AssignBindingElementTypes(At(declaration, name), ValueSymbolLinks.Get(parameter).ResolvedType);
            }
        }
    }

    // checker.ts assignBindingElementTypes
    private void AssignBindingElementTypes(NodeRef pattern, Type parentType)
    {
        var store = StoreOf(pattern);
        foreach (var element in store.Nodes(store.Elements(pattern.Node)))
        {
            if (store.Kind(element) == SyntaxKind.OmittedExpression)
            {
                continue;
            }
            var elementRef = At(pattern, element);
            var type = GetBindingElementTypeFromParentType(elementRef, parentType, noTupleBoundsCheck: false);
            var name = store.Name(element);
            if (store.Kind(name) == SyntaxKind.Identifier)
            {
                var symbol = GetSymbolOfDeclaration(elementRef);
                if (!symbol.IsNone)
                {
                    ValueSymbolLinks.Get(symbol).ResolvedType = type;
                }
            }
            else
            {
                AssignBindingElementTypes(At(pattern, name), type);
            }
        }
    }

    /// <summary>checker.ts checkFunctionExpressionOrObjectLiteralMethodDeferred (checkAllCodePathsInNonVoidFunctionReturnOrThrow:
    /// C7; checkReturnExpression: C8).</summary>
    private void CheckFunctionExpressionOrObjectLiteralMethodDeferred(NodeRef node)
    {
        var store = StoreOf(node);
        var body = store.Body(node.Node);
        if (body.IsNone)
        {
            return;
        }
        if (GetEffectiveReturnTypeNode(store, node.Node).IsNone)
        {
            // There are some checks that are only performed in getReturnTypeFromBody, that may produce errors
            // we need. An example is the noImplicitAny errors resulting from widening the return expression
            // of a function. Because checking of function expression bodies is deferred, there was never an
            // appropriate time to do this during the main walk of the file (see the comment at the top of
            // checkFunctionExpressionBodies). So it must be done now.
            GetReturnTypeOfSignature(GetSignatureFromDeclaration(node));
        }
        if (store.Kind(body) == SyntaxKind.Block)
        {
            CheckNodeRecursive(At(node, body));
        }
        else
        {
            CheckExpression(At(node, body));
        }
    }

    /// <summary>checker.ts checkClassExpression (checkClassLikeDeclaration: the class walk of Checker.Check.cs).</summary>
    internal Type CheckClassExpression(NodeRef node)
    {
        if (checkedClassExpressions.Add(node))
        {
            var store = StoreOf(node);
            VisitChildren(node, except: store.Members(node.Node));
            CheckClassLikeDeclarationRelations(node); // module C4
            CheckMembersForOverrideModifier(node);
            CheckPropertyInitialization(node);
        }
        CheckNodeDeferred(node);
        return GetTypeOfSymbol(GetSymbolOfDeclaration(node));
    }

    // ---- return types ----

    /// <summary>checker.ts getReturnTypeFromBody.</summary>
    internal Type GetReturnTypeFromBody(NodeRef func, CheckMode checkMode = CheckMode.Normal)
    {
        var store = StoreOf(func);
        var body = store.Body(func.Node);
        if (body.IsNone)
        {
            return errorType;
        }
        var functionFlags = GetFunctionFlagsOf(store, func.Node);
        var isAsync = (functionFlags & FunctionFlags.Async) != 0;
        var isGenerator = (functionFlags & FunctionFlags.Generator) != 0;
        var returnType = Type.None;
        var yieldType = Type.None;
        var nextType = Type.None;
        var fallbackReturnType = voidType;
        var innerCheckMode = checkMode & ~CheckMode.SkipGenericFunctions;
        if (store.Kind(body) != SyntaxKind.Block)
        {
            // Async or normal arrow function
            var bodyRef = At(func, body);
            returnType = CheckExpressionCached(bodyRef, innerCheckMode);
            MarkUncertainReturn(func, bodyRef, returnType);
            if (IsConstContext(bodyRef))
            {
                returnType = GetRegularTypeOfLiteralType(returnType);
            }
            if (isAsync)
            {
                // From within an async function you can return either a non-promise value or a promise. Any
                // Promise/A+ compatible implementation will always assimilate any foreign promise, so the
                // return type of the body should be unwrapped to its awaited type, which we will wrap in
                // the native Promise<T> type later in this function.
                returnType = UnwrapAwaitedType(CheckAwaitedType(returnType, withAlias: false, func, Diagnostics.The_return_type_of_an_async_function_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member));
            }
        }
        else if (isGenerator)
        {
            // Generator or AsyncGenerator function
            var returnTypes = CheckAndAggregateReturnExpressionTypes(func, innerCheckMode);
            if (returnTypes is null)
            {
                fallbackReturnType = neverType;
            }
            else if (returnTypes.Count > 0)
            {
                returnType = GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(returnTypes), UnionReduction.Subtype);
            }
            var (yieldTypes, nextTypes) = CheckAndAggregateYieldOperandTypes(func, innerCheckMode);
            yieldType = yieldTypes.Count > 0 ? GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(yieldTypes), UnionReduction.Subtype) : Type.None;
            nextType = nextTypes.Count > 0 ? GetIntersectionType(nextTypes.ToArray()) : Type.None;
        }
        else
        {
            // Async or normal function
            var types = CheckAndAggregateReturnExpressionTypes(func, innerCheckMode);
            if (types is null)
            {
                // For an async function, the return type will not be never, but rather a Promise for never.
                return isAsync ? CreatePromiseReturnType(func, neverType) : neverType;
            }
            if (types.Count == 0)
            {
                // For an async function, the return type will not be void/undefined, but rather a Promise for void/undefined.
                var contextualReturnType = GetContextualReturnType(func, ContextFlags.None);
                Type voidOrUndefined;
                if (!contextualReturnType.IsNone)
                {
                    var unwrapped = UnwrapReturnType(contextualReturnType, functionFlags);
                    voidOrUndefined = SomeType(unwrapped.IsNone ? voidType : unwrapped, t => (Types.Flags(t) & TypeFlags.Undefined) != 0) ? undefinedType : voidType;
                }
                else
                {
                    voidOrUndefined = voidType;
                }
                return isAsync ? CreatePromiseReturnType(func, voidOrUndefined) : voidOrUndefined;
            }
            // Return a union of the return expression types.
            returnType = GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(types), UnionReduction.Subtype);
        }
        if (!returnType.IsNone || !yieldType.IsNone || !nextType.IsNone)
        {
            if (!yieldType.IsNone)
            {
                ReportErrorsFromWidening(func, yieldType, WideningKind.GeneratorYield);
            }
            if (!returnType.IsNone)
            {
                ReportErrorsFromWidening(func, returnType, WideningKind.FunctionReturn);
            }
            if (!nextType.IsNone)
            {
                ReportErrorsFromWidening(func, nextType, WideningKind.GeneratorNext);
            }
            if ((!returnType.IsNone && IsUnitType(returnType)) || (!yieldType.IsNone && IsUnitType(yieldType)) || (!nextType.IsNone && IsUnitType(nextType)))
            {
                var contextualSignature = GetContextualSignatureForFunctionLikeDeclaration(func);
                var contextualType = contextualSignature.IsNone ? Type.None
                    : contextualSignature == GetSignatureFromDeclaration(func) ? isGenerator ? Type.None : returnType
                    : InstantiateContextualType(GetReturnTypeOfSignature(contextualSignature), func, ContextFlags.None);
                if (isGenerator)
                {
                    yieldType = GetWidenedLiteralLikeTypeForContextualIterationTypeIfNeeded(yieldType, contextualType, IterationTypeKind.Yield, isAsync);
                    returnType = GetWidenedLiteralLikeTypeForContextualIterationTypeIfNeeded(returnType, contextualType, IterationTypeKind.Return, isAsync);
                    nextType = GetWidenedLiteralLikeTypeForContextualIterationTypeIfNeeded(nextType, contextualType, IterationTypeKind.Next, isAsync);
                }
                else
                {
                    returnType = GetWidenedLiteralLikeTypeForContextualReturnTypeIfNeeded(returnType, contextualType, isAsync);
                }
            }
            if (!yieldType.IsNone)
            {
                yieldType = GetWidenedType(yieldType);
            }
            if (!returnType.IsNone)
            {
                returnType = GetWidenedType(returnType);
            }
            if (!nextType.IsNone)
            {
                nextType = GetWidenedType(nextType);
            }
        }
        if (isGenerator)
        {
            if (nextType.IsNone)
            {
                nextType = GetContextualIterationType(IterationTypeKind.Next, func);
            }
            return CreateGeneratorType(yieldType.IsNone ? neverType : yieldType, returnType.IsNone ? fallbackReturnType : returnType, nextType.IsNone ? unknownType : nextType, isAsync);
        }
        // From within an async function you can return either a non-promise value or a promise. Any
        // Promise/A+ compatible implementation will always assimilate any foreign promise, so the
        // return type of the body is awaited type of the body, wrapped in a native Promise<T> type.
        return isAsync ? CreatePromiseType(returnType.IsNone ? fallbackReturnType : returnType) : returnType.IsNone ? fallbackReturnType : returnType;
    }

    // checker.ts checkAndAggregateYieldOperandTypes
    private (List<Type> YieldTypes, List<Type> NextTypes) CheckAndAggregateYieldOperandTypes(NodeRef func, CheckMode checkMode)
    {
        var yieldTypes = new List<Type>();
        var nextTypes = new List<Type>();
        var store = StoreOf(func);
        var isAsync = (GetFunctionFlagsOf(store, func.Node) & FunctionFlags.Async) != 0;
        ForEachYieldExpression(store, store.Body(func.Node), y =>
        {
            var yieldExpression = At(func, y);
            var operand = store.Expression(y);
            var yieldExpressionType = !operand.IsNone ? CheckExpression(At(func, operand), checkMode) : undefinedWideningType;
            if (!operand.IsNone && IsConstContext(At(func, operand)))
            {
                yieldExpressionType = GetRegularTypeOfLiteralType(yieldExpressionType);
            }
            var yielded = GetYieldedTypeOfYieldExpression(yieldExpression, yieldExpressionType, anyType, isAsync);
            if (!yielded.IsNone && !yieldTypes.Contains(yielded))
            {
                yieldTypes.Add(yielded);
            }
            Type nextType;
            if (!store.AsteriskToken(y).IsNone)
            {
                var iterationTypes = GetIterationTypesOfIterable(yieldExpressionType, isAsync ? IterationUse.AsyncYieldStar : IterationUse.YieldStar, operand.IsNone ? NodeRef.None : At(func, operand));
                nextType = iterationTypes?.NextType ?? Type.None;
            }
            else
            {
                nextType = GetContextualType(yieldExpression, ContextFlags.None);
            }
            if (!nextType.IsNone && !nextTypes.Contains(nextType))
            {
                nextTypes.Add(nextType);
            }
        });
        return (yieldTypes, nextTypes);
    }

    // checker.ts getYieldedTypeOfYieldExpression
    private Type GetYieldedTypeOfYieldExpression(NodeRef node, Type expressionType, Type sentType, bool isAsync)
    {
        var store = StoreOf(node);
        var operand = store.Expression(node.Node);
        var errorNode = operand.IsNone ? node : At(node, operand);
        var hasAsterisk = !store.AsteriskToken(node.Node).IsNone;
        // A `yield*` expression effectively yields everything that its operand yields
        var yieldedType = hasAsterisk ? CheckIteratedTypeOrElementType(isAsync ? IterationUse.AsyncYieldStar : IterationUse.YieldStar, expressionType, sentType, errorNode) : expressionType;
        return !isAsync ? yieldedType : GetAwaitedType(yieldedType, errorNode, hasAsterisk
            ? Diagnostics.Type_of_iterated_elements_of_a_yield_Asterisk_operand_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member
            : Diagnostics.Type_of_yield_operand_in_an_async_generator_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member);
    }

    // checker.ts checkAndAggregateReturnExpressionTypes (null: the function returns never)
    private List<Type>? CheckAndAggregateReturnExpressionTypes(NodeRef func, CheckMode checkMode)
    {
        var store = StoreOf(func);
        var functionFlags = GetFunctionFlagsOf(store, func.Node);
        var aggregatedTypes = new List<Type>();
        var hasReturnWithNoExpression = FunctionHasImplicitReturn(func);
        var hasReturnOfTypeNever = false;
        ForEachReturnStatement(store, store.Body(func.Node), returnStatement =>
        {
            var expr = store.Expression(returnStatement);
            if (expr.IsNone)
            {
                hasReturnWithNoExpression = true;
                return;
            }
            expr = Utilities.SkipParentheses(store, expr, true);
            // Bare calls to this same function don't contribute to inference
            // and `return await` is also safe to unwrap here
            if ((functionFlags & FunctionFlags.Async) != 0 && store.Kind(expr) == SyntaxKind.AwaitExpression)
            {
                expr = Utilities.SkipParentheses(store, store.Expression(expr), true);
            }
            if (store.Kind(expr) == SyntaxKind.CallExpression && store.Kind(store.Expression(expr)) == SyntaxKind.Identifier)
            {
                var callee = At(func, store.Expression(expr));
                var funcSymbol = GetMergedSymbol(NodeSymbol(func));
                if (!funcSymbol.IsNone && Types.Symbol(CheckExpressionCached(callee)) == funcSymbol)
                {
                    var valueDeclaration = ValueDeclarationOf(funcSymbol);
                    if (valueDeclaration.IsNone || !IsFunctionExpressionOrArrowFunction(StoreOf(valueDeclaration), valueDeclaration.Node) || IsConstantReferenceForBareCall(callee))
                    {
                        hasReturnOfTypeNever = true;
                        return;
                    }
                }
            }
            var exprRef = At(func, expr);
            var type = CheckExpressionCached(exprRef, checkMode);
            MarkUncertainReturn(func, exprRef, type);
            if ((functionFlags & FunctionFlags.Async) != 0)
            {
                // From within an async function you can return either a non-promise value or a promise. Any
                // Promise/A+ compatible implementation will always assimilate any foreign promise, so the
                // return type of the body should be unwrapped to its awaited type, which should be wrapped in
                // the native Promise<T> type by the caller.
                type = UnwrapAwaitedType(CheckAwaitedType(type, withAlias: false, func, Diagnostics.The_return_type_of_an_async_function_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member));
            }
            if ((Types.Flags(type) & TypeFlags.Never) != 0)
            {
                hasReturnOfTypeNever = true;
            }
            var aggregated = IsConstContext(exprRef) ? GetRegularTypeOfLiteralType(type) : type;
            if (!aggregatedTypes.Contains(aggregated))
            {
                aggregatedTypes.Add(aggregated);
            }
        });
        if (aggregatedTypes.Count == 0 && !hasReturnWithNoExpression && (hasReturnOfTypeNever || MayReturnNever(store, func.Node)))
        {
            return null;
        }
        if (strictNullChecks && aggregatedTypes.Count > 0 && hasReturnWithNoExpression &&
            !(IsJSConstructor(func) && aggregatedTypes.Exists(t => Types.Symbol(t) == NodeSymbol(func))))
        {
            // Javascript "callable constructors", containing eg `if (!(this instanceof A)) return new A()` should not add undefined
            if (!aggregatedTypes.Contains(undefinedType))
            {
                aggregatedTypes.Add(undefinedType);
            }
        }
        return aggregatedTypes;
    }

    // checker.ts isConstantReference for the callee identifier of a bare recursive call (TODO(C7): isSymbolAssigned)
    private bool IsConstantReferenceForBareCall(NodeRef identifier)
    {
        var symbol = GetResolvedSymbol(identifier);
        if (symbol.IsNone)
        {
            return false;
        }
        var valueDeclaration = ValueDeclarationOf(symbol);
        if (!valueDeclaration.IsNone && Kind(valueDeclaration) == SyntaxKind.FunctionExpression)
        {
            return true;
        }
        // isConstantVariable || isParameterOrMutableLocalVariable && !isSymbolAssigned (TODO(C7): isSymbolAssigned; the
        // seam looks for assignments to the name in the file)
        if ((SymbolFlagsOf(symbol) & SymbolFlags.Variable) == 0)
        {
            return false;
        }
        var declaration = ValueDeclarationOf(symbol);
        if (declaration.IsNone)
        {
            return false;
        }
        var store = StoreOf(declaration);
        if ((Utilities.GetCombinedNodeFlags(store, declaration.Node) & NodeFlags.Constant) != 0)
        {
            return true;
        }
        return !IsNameAssigned(At(declaration, Utilities.RootOf(store, declaration.Node)), NameOf(symbol));
    }

    // whether the file assigns to an identifier with the name (isSymbolAssigned approximated by name)
    // (the assigned names of a file are collected once, like markNodeAssignments marks a container once)
    private bool IsNameAssigned(NodeRef file, string name)
    {
        if (!assignedNamesOfFiles.TryGetValue(file.File, out var names))
        {
            names = CollectAssignedNames(file);
            assignedNamesOfFiles[file.File] = names;
        }
        return names.Contains(name);
    }

    private readonly Dictionary<int, HashSet<string>> assignedNamesOfFiles = [];

    private HashSet<string> CollectAssignedNames(NodeRef file)
    {
        var store = StoreOf(file);
        var names = new HashSet<string>(StringComparer.Ordinal);
        Visit(file.Node);
        return names;

        void Visit(Node n)
        {
            switch (store.Kind(n))
            {
                case SyntaxKind.BinaryExpression when Utilities.IsAssignmentOperator(store.Kind(store.OperatorToken(n))):
                {
                    var left = Utilities.SkipParentheses(store, store.Left(n), false);
                    if (store.Kind(left) == SyntaxKind.Identifier)
                    {
                        names.Add(store.Texts.GetString(store.EscapedText(left))!);
                    }
                    break;
                }
                case SyntaxKind.PrefixUnaryExpression or SyntaxKind.PostfixUnaryExpression when store.Operator(n) is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken:
                {
                    var operand = Utilities.SkipParentheses(store, store.Operand(n), false);
                    if (store.Kind(operand) == SyntaxKind.Identifier)
                    {
                        names.Add(store.Texts.GetString(store.EscapedText(operand))!);
                    }
                    break;
                }
            }
            NodeVisitor.ForEachChild<object>(store, n, child =>
            {
                Visit(child);
                return null;
            });
        }
    }

    // C1's "uncertain" mechanism: a return type inferred from an approximate return expression type (TODO(C6)) or
    // from a reference whose narrowing is pending (TODO(C7)) is an approximation
    private void MarkUncertainReturn(NodeRef func, NodeRef expr, Type type)
    {
        if (SuppressDerivedDiagnostics(expr, type))
        {
            uncertainExpressions.Add(func);
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

    // ---- yield and await ----

    /// <summary>checker.ts checkYieldExpression (checkYieldExpressionGrammar: C8).</summary>
    private Type CheckYieldExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var funcNode = Utilities.GetContainingFunctionOrNone(store, n);
        if (funcNode.IsNone)
        {
            return anyType;
        }
        var func = At(node, funcNode);
        var functionFlags = GetFunctionFlagsOf(store, funcNode);
        if ((functionFlags & FunctionFlags.Generator) == 0)
        {
            // If the user's code is syntactically correct, the func should always have a star. After all, we are in a yield context.
            return anyType;
        }
        var isAsync = (functionFlags & FunctionFlags.Async) != 0;
        // There is no point in doing an assignability check if the function
        // has no explicit return type because the return type is directly computed
        // from the yield expressions.
        var returnType = GetReturnTypeFromAnnotation(func);
        if (!returnType.IsNone && (Types.Flags(returnType) & TypeFlags.Union) != 0)
        {
            returnType = FilterType(returnType, t => CheckGeneratorInstantiationAssignabilityToReturnType(t, functionFlags, NodeRef.None));
        }
        var iterationTypes = !returnType.IsNone ? GetIterationTypesOfGeneratorFunctionReturnType(returnType, isAsync) : null;
        var signatureYieldType = iterationTypes is not null && !iterationTypes.YieldType.IsNone ? iterationTypes.YieldType : anyType;
        var signatureNextType = iterationTypes is not null && !iterationTypes.NextType.IsNone ? iterationTypes.NextType : anyType;
        var operand = store.Expression(n);
        var yieldExpressionType = !operand.IsNone ? CheckExpression(At(node, operand)) : undefinedWideningType;
        var yieldedType = GetYieldedTypeOfYieldExpression(node, yieldExpressionType, signatureNextType, isAsync);
        if (!returnType.IsNone && !yieldedType.IsNone && !(operand.IsNone ? false : SuppressDerivedDiagnostics(At(node, operand), yieldExpressionType)))
        {
            CheckTypeAssignableToAndOptionallyElaborate(yieldedType, signatureYieldType, operand.IsNone ? node : At(node, operand), operand.IsNone ? NodeRef.None : At(node, operand));
        }
        if (!store.AsteriskToken(n).IsNone)
        {
            var use = isAsync ? IterationUse.AsyncYieldStar : IterationUse.YieldStar;
            var t = GetIterationTypeOfIterable(use, IterationTypeKind.Return, yieldExpressionType, operand.IsNone ? NodeRef.None : At(node, operand));
            return t.IsNone ? anyType : t;
        }
        if (!returnType.IsNone)
        {
            var t = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Next, returnType, isAsync);
            return t.IsNone ? anyType : t;
        }
        var type = GetContextualIterationType(IterationTypeKind.Next, func);
        if (type.IsNone)
        {
            type = anyType;
            if (noImplicitAny && !ExpressionResultIsUnused(store, n))
            {
                var contextualType = GetContextualType(node, ContextFlags.None);
                if (contextualType.IsNone || IsTypeAny(contextualType))
                {
                    Error(node, Diagnostics.yield_expression_implicitly_results_in_an_any_type_because_its_containing_generator_lacks_a_return_type_annotation);
                }
            }
        }
        return type;
    }

    // checker.ts checkGeneratorInstantiationAssignabilityToReturnType
    private bool CheckGeneratorInstantiationAssignabilityToReturnType(Type returnType, FunctionFlags functionFlags, NodeRef errorNode)
    {
        var isAsync = (functionFlags & FunctionFlags.Async) != 0;
        var generatorYieldType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Yield, returnType, isAsync);
        if (generatorYieldType.IsNone)
        {
            generatorYieldType = anyType;
        }
        var generatorReturnType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, returnType, isAsync);
        if (generatorReturnType.IsNone)
        {
            generatorReturnType = generatorYieldType;
        }
        var generatorNextType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Next, returnType, isAsync);
        if (generatorNextType.IsNone)
        {
            generatorNextType = unknownType;
        }
        var generatorInstantiation = CreateGeneratorType(generatorYieldType, generatorReturnType, generatorNextType, isAsync);
        return CheckTypeAssignableTo(generatorInstantiation, returnType, errorNode);
    }

    /// <summary>checker.ts checkAwaitExpression (checkAwaitGrammar: C8; the "await has no effect" suggestion is not a
    /// diagnostic of the dump).</summary>
    private Type CheckAwaitExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var operandType = CheckExpression(At(node, store.Expression(node.Node)));
        return CheckAwaitedType(operandType, withAlias: true, node, Diagnostics.Type_of_await_operand_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member);
    }

    // ---- utilities ----

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

    // utilities.ts forEachReturnStatement
    private static void ForEachReturnStatement(NodeStore store, Node body, Action<Node> visitor) =>
        ForEachReturnStatementAny(store, body, statement =>
        {
            visitor(statement);
            return false;
        });

    private static bool ForEachReturnStatementAny(NodeStore store, Node body, Func<Node, bool> visitor)
    {
        return Traverse(body);

        bool Traverse(Node node)
        {
            switch (store.Kind(node))
            {
                case SyntaxKind.ReturnStatement:
                    return visitor(node);
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
                {
                    var found = false;
                    NodeVisitor.ForEachChild<object>(store, node, child =>
                    {
                        if (!found && Traverse(child))
                        {
                            found = true;
                        }
                        return null;
                    });
                    return found;
                }
            }
            return false;
        }
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
}
