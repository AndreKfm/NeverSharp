// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (module C5; the functions are listed below)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (details below).
// Functions (contextual typing: getContextualType and its cases, getApparentTypeOfContextualType,
// instantiateContextualType, instantiateInstantiableTypes, getContextualSignature, getContextualCallSignature,
// isAritySmaller, getIntersectedSignatures, combineSignaturesOfIntersectionMembers, getContextuallyTypedParameterType,
// getContextualTypeForVariableLikeDeclaration, getContextualTypeForInitializerExpression,
// getContextualTypeForReturnExpression, getContextualTypeForAwaitOperand, getContextualTypeForYieldOperand,
// getContextualReturnType, getContextualIterationType, getContextualTypeForArgument, getContextualTypeForBinaryOperand,
// getContextualTypeForAssignmentDeclaration, getSymbolForExpression, getTypeOfPropertyOfContextualType,
// getContextualTypeForObjectLiteralElement, getContextualTypeForElementExpression, discriminateContextualTypeByObjectMembers,
// discriminateTypeByDiscriminableItems, getMatchingUnionConstituentForObjectLiteral, isPossiblyDiscriminantValue,
// getWidenedLiteralLikeTypeForContextualType)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (replaces C1's approximate contextual typing); module C9b: the
// contextual types of JSX attributes and children (Checker.Jsx.cs). Module C9d: JavaScript assignment declarations,
// JSDoc type and satisfies tags, type tag signatures as contextual signatures.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private readonly Dictionary<(NodeRef, Type), Type> discriminatedContextualTypes = [];

    /// <summary>checker.ts getContextualType.</summary>
    internal Type GetContextualType(NodeRef node, ContextFlags contextFlags)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if ((store.Flags(n) & NodeFlags.InWithStatement) != 0)
        {
            // We cannot answer semantic questions within a with block, do not proceed any further
            return Type.None;
        }
        // Cached contextual types are obtained with no ContextFlags, so we can only consult them for
        // requests with no ContextFlags.
        var index = FindContextualNode(node, includeCaches: contextFlags == ContextFlags.None);
        if (index >= 0)
        {
            return contextualTypes[index];
        }
        var parent = store.Parent(n);
        switch (store.Kind(parent))
        {
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.Parameter:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.BindingElement:
                return GetContextualTypeForInitializerExpression(node, contextFlags);
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.ReturnStatement:
                return GetContextualTypeForReturnExpression(node, contextFlags);
            case SyntaxKind.YieldExpression:
                return GetContextualTypeForYieldOperand(At(node, parent), contextFlags);
            case SyntaxKind.AwaitExpression:
                return GetContextualTypeForAwaitOperand(At(node, parent), contextFlags);
            case SyntaxKind.CallExpression:
            case SyntaxKind.NewExpression:
                return GetContextualTypeForArgument(At(node, parent), node);
            case SyntaxKind.Decorator:
                return GetContextualTypeForDecorator(At(node, parent));
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.AsExpression:
                return IsConstTypeReference(store, store.Type(parent)) ? GetContextualType(At(node, parent), contextFlags) : GetTypeFromTypeNode(At(node, store.Type(parent)));
            case SyntaxKind.BinaryExpression:
                return GetContextualTypeForBinaryOperand(node, contextFlags);
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.ShorthandPropertyAssignment:
                return GetContextualTypeForObjectLiteralElement(At(node, parent), contextFlags);
            case SyntaxKind.SpreadAssignment:
                return GetContextualType(At(node, store.Parent(parent)), contextFlags);
            case SyntaxKind.ArrayLiteralExpression:
            {
                var arrayLiteral = At(node, parent);
                var type = GetApparentTypeOfContextualType(arrayLiteral, contextFlags);
                var elementIndex = store.Nodes(store.Elements(parent)).IndexOf(n);
                var (first, last) = GetSpreadIndices(arrayLiteral);
                return GetContextualTypeForElementExpression(type, elementIndex, store.Length(store.Elements(parent)), first, last);
            }
            case SyntaxKind.ConditionalExpression:
                return GetContextualTypeForConditionalOperand(node, contextFlags);
            case SyntaxKind.TemplateSpan:
                return GetContextualTypeForSubstitutionExpression(At(node, store.Parent(parent)), node);
            case SyntaxKind.ParenthesizedExpression:
                if (Utilities.IsInJSFile(store, parent))
                {
                    if (IsJSDocSatisfiesExpression(store, parent))
                    {
                        return GetTypeFromTypeNode(GetJSDocSatisfiesExpressionType(At(node, parent)));
                    }
                    var typeTag = UtilitiesPublic.GetJSDocTypeTag(store, parent);
                    if (!typeTag.IsNone && !IsConstTypeReference(store, store.Type(store.TypeExpression(typeTag))))
                    {
                        return GetTypeFromTypeNode(At(node, store.Type(store.TypeExpression(typeTag))));
                    }
                }
                return GetContextualType(At(node, parent), contextFlags);
            case SyntaxKind.NonNullExpression:
                return GetContextualType(At(node, parent), contextFlags);
            case SyntaxKind.SatisfiesExpression:
                return GetTypeFromTypeNode(At(node, store.Type(parent)));
            case SyntaxKind.ExportAssignment:
                return TryGetTypeFromEffectiveTypeNode(At(node, parent));
            case SyntaxKind.JsxExpression:
                return GetContextualTypeForJsxExpression(At(node, parent), contextFlags);
            case SyntaxKind.JsxAttribute:
            case SyntaxKind.JsxSpreadAttribute:
                return GetContextualTypeForJsxAttribute(At(node, parent), contextFlags);
            case SyntaxKind.JsxOpeningElement:
            case SyntaxKind.JsxSelfClosingElement:
                return GetContextualJsxElementAttributesType(At(node, parent), contextFlags);
            case SyntaxKind.ImportAttribute:
                // (getContextualImportAttributeType: C8)
                return Type.None;
        }
        return Type.None;
    }

    // checker.ts getSpreadIndices (cached in the array literal's node links)
    private (int First, int Last) GetSpreadIndices(NodeRef arrayLiteral)
    {
        ref var links = ref ExpressionLinks.Get(arrayLiteral);
        if (!links.SpreadIndicesComputed)
        {
            var store = StoreOf(arrayLiteral);
            int first = -1, last = -1;
            var elements = store.Nodes(store.Elements(arrayLiteral.Node));
            for (var i = 0; i < elements.Length; i++)
            {
                if (store.Kind(elements[i]) == SyntaxKind.SpreadElement)
                {
                    if (first < 0)
                    {
                        first = i;
                    }
                    last = i;
                }
            }
            ref var after = ref ExpressionLinks.Get(arrayLiteral);
            after.SpreadIndicesComputed = true;
            after.FirstSpreadIndex = first;
            after.LastSpreadIndex = last;
        }
        var result = ExpressionLinks.Get(arrayLiteral);
        return (result.FirstSpreadIndex, result.LastSpreadIndex);
    }

    /// <summary>checker.ts getApparentTypeOfContextualType.</summary>
    internal Type GetApparentTypeOfContextualType(NodeRef node, ContextFlags contextFlags)
    {
        var contextualType = Utilities.IsObjectLiteralMethod(StoreOf(node), node.Node)
            ? GetContextualTypeForObjectLiteralMethod(node, contextFlags)
            : GetContextualType(node, contextFlags);
        var instantiatedType = InstantiateContextualType(contextualType, node, contextFlags);
        if (!instantiatedType.IsNone && !((contextFlags & ContextFlags.NoConstraints) != 0 && (Types.Flags(instantiatedType) & TypeFlags.TypeVariable) != 0))
        {
            // When obtaining apparent type of *contextual* type we don't want to get apparent type of mapped types.
            // That would evaluate mapped types with array or tuple type constraints too eagerly
            // and thus it would prevent `getTypeOfPropertyOfContextualType` from obtaining per-position contextual type for elements of array literal expressions.
            // Apparent type of other mapped types is already the mapped type itself so we can just avoid calling `getApparentType` here for all mapped types.
            var apparentType = MapType(instantiatedType, t => (Types.ObjectFlagsOf(t) & ObjectFlags.Mapped) != 0 ? t : GetApparentType(t), noReductions: true);
            var store = StoreOf(node);
            if ((Types.Flags(apparentType) & TypeFlags.Union) != 0 && store.Kind(node.Node) == SyntaxKind.ObjectLiteralExpression)
            {
                return DiscriminateContextualTypeByObjectMembers(node, apparentType);
            }
            if ((Types.Flags(apparentType) & TypeFlags.Union) != 0 && store.Kind(node.Node) == SyntaxKind.JsxAttributes)
            {
                return DiscriminateContextualTypeByJSXAttributes(node, apparentType);
            }
            return apparentType;
        }
        return Type.None;
    }

    /// <summary>checker.ts instantiateContextualType.</summary>
    internal Type InstantiateContextualType(Type contextualType, NodeRef node, ContextFlags contextFlags)
    {
        if (!contextualType.IsNone && MaybeTypeOfKind(contextualType, TypeFlags.Instantiable))
        {
            var inferenceContext = GetInferenceContext(node);
            // If no inferences have been made, and none of the type parameters for which we are inferring
            // specify default types, nothing is gained from instantiating as type parameters would just be
            // replaced with their constraints similar to the apparent type.
            if (inferenceContext is not null && (contextFlags & ContextFlags.Signature) != 0 && inferenceContext.Inferences.Exists(HasInferenceCandidatesOrDefault))
            {
                // For contextual signatures we incorporate all inferences made so far, e.g. from return
                // types as well as arguments to the left in a function call.
                var type = InstantiateInstantiableTypes(contextualType, inferenceContext.NonFixingMapper);
                if ((Types.Flags(type) & TypeFlags.AnyOrUnknown) == 0)
                {
                    return type;
                }
            }
            if (inferenceContext?.ReturnMapper is not null)
            {
                // For other purposes (e.g. determining whether to produce literal types) we only
                // incorporate inferences made from the return type in a function call. We remove
                // the 'boolean' type from the contextual type such that contextually typed boolean
                // literals actually end up widening to 'boolean' (see #48363).
                var type = InstantiateInstantiableTypes(contextualType, inferenceContext.ReturnMapper);
                if ((Types.Flags(type) & TypeFlags.AnyOrUnknown) == 0)
                {
                    if ((Types.Flags(type) & TypeFlags.Union) != 0)
                    {
                        var members = Types.Elements(Types.Union(type).Types);
                        if (members.Contains(regularFalseType) && members.Contains(regularTrueType))
                        {
                            return FilterType(type, t => t != regularFalseType && t != regularTrueType);
                        }
                    }
                    return type;
                }
            }
        }
        return contextualType;
    }

    // checker.ts instantiateInstantiableTypes
    private Type InstantiateInstantiableTypes(Type type, TypeMapper mapper)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Instantiable) != 0)
        {
            return InstantiateType(type, mapper);
        }
        if ((flags & TypeFlags.Union) != 0)
        {
            return GetUnionType(Array.ConvertAll(Types.Elements(Types.Union(type).Types).ToArray(), t => InstantiateInstantiableTypes(t, mapper)), UnionReduction.None);
        }
        if ((flags & TypeFlags.Intersection) != 0)
        {
            return GetIntersectionType(Array.ConvertAll(Types.Elements(Types.Union(type).Types).ToArray(), t => InstantiateInstantiableTypes(t, mapper)));
        }
        return type;
    }

    // ---- declarations and returns ----

    /// <summary>checker.ts getContextuallyTypedParameterType.</summary>
    internal Type GetContextuallyTypedParameterType(NodeRef parameter)
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
            var iifeRef = At(func, iife);
            var args = GetEffectiveCallArguments(iifeRef);
            var indexOfParameter = store.Nodes(store.Parameters(func.Node)).IndexOf(parameter.Node);
            if (!store.DotDotDotToken(parameter.Node).IsNone)
            {
                return GetSpreadArgumentType(args, indexOfParameter, args.Count, anyType, null, CheckMode.Normal);
            }
            ref var links = ref SignatureLinks.Get(iifeRef);
            var cached = links.ResolvedSignature;
            links.ResolvedSignature = anySignature;
            var type = indexOfParameter < args.Count ? GetWidenedLiteralType(CheckArgumentExpression(args[indexOfParameter], CheckMode.Normal))
                : !store.Initializer(parameter.Node).IsNone ? Type.None : undefinedWideningType;
            SignatureLinks.Get(iifeRef).ResolvedSignature = cached;
            return type;
        }
        var contextualSignature = GetContextualSignature(func);
        if (!contextualSignature.IsNone)
        {
            var index = store.Nodes(store.Parameters(func.Node)).IndexOf(parameter.Node) - (!GetThisParameter(store, func.Node).IsNone ? 1 : 0);
            var parameters = store.Nodes(store.Parameters(func.Node));
            return !store.DotDotDotToken(parameter.Node).IsNone && parameters[^1] == parameter.Node
                ? GetRestTypeAtPosition(contextualSignature, index)
                : TryGetTypeAtPosition(contextualSignature, index);
        }
        return Type.None;
    }

    // checker.ts getContextualTypeForVariableLikeDeclaration (JSDoc satisfies tags: C9)
    private Type GetContextualTypeForVariableLikeDeclaration(NodeRef declaration, ContextFlags contextFlags)
    {
        var typeNode = GetEffectiveTypeAnnotationNode(declaration);
        if (typeNode.IsNone && Utilities.IsInJSFile(StoreOf(declaration), declaration.Node))
        {
            typeNode = TryGetJSDocSatisfiesTypeNode(declaration);
        }
        if (!typeNode.IsNone)
        {
            return GetTypeFromTypeNode(typeNode);
        }
        switch (Kind(declaration))
        {
            case SyntaxKind.Parameter:
                return GetContextuallyTypedParameterType(declaration);
            case SyntaxKind.BindingElement:
                return GetContextualTypeForBindingElement(declaration, contextFlags);
            case SyntaxKind.PropertyDeclaration:
                if (Utilities.IsStatic(StoreOf(declaration), declaration.Node))
                {
                    return GetContextualTypeForStaticPropertyDeclaration(declaration, contextFlags);
                }
                break;
        }
        // By default, do nothing and return undefined - only the above cases have context implied by a parent
        return Type.None;
    }

    // checker.ts getContextualTypeForBindingElement
    private Type GetContextualTypeForBindingElement(NodeRef declaration, ContextFlags contextFlags)
    {
        var store = StoreOf(declaration);
        var parent = At(declaration, store.Parent(store.Parent(declaration.Node)));
        var propertyName = store.PropertyName(declaration.Node);
        var name = propertyName.IsNone ? store.Name(declaration.Node) : propertyName;
        var parentType = GetContextualTypeForVariableLikeDeclaration(parent, contextFlags);
        if (parentType.IsNone && store.Kind(parent.Node) != SyntaxKind.BindingElement && !store.Initializer(parent.Node).IsNone)
        {
            parentType = CheckDeclarationInitializer(parent, !store.DotDotDotToken(declaration.Node).IsNone ? CheckMode.RestBindingElement : CheckMode.Normal);
        }
        if (parentType.IsNone || Utilities.IsBindingPattern(store, name) || Utilities.IsComputedNonLiteralName(store, name))
        {
            return Type.None;
        }
        if (store.Kind(store.Name(parent.Node)) == SyntaxKind.ArrayBindingPattern)
        {
            var index = store.Nodes(store.Elements(store.Parent(declaration.Node))).IndexOf(declaration.Node);
            if (index < 0)
            {
                return Type.None;
            }
            return GetContextualTypeForElementExpression(parentType, index);
        }
        var nameType = GetLiteralTypeFromPropertyName(At(declaration, name));
        if (IsTypeUsableAsPropertyName(nameType))
        {
            var text = GetPropertyNameFromType(nameType);
            return GetTypeOfPropertyOfType(parentType, text);
        }
        return Type.None;
    }

    // checker.ts getContextualTypeForStaticPropertyDeclaration
    private Type GetContextualTypeForStaticPropertyDeclaration(NodeRef declaration, ContextFlags contextFlags)
    {
        var parent = Parent(declaration);
        var parentType = UtilitiesPublic.IsExpression(StoreOf(parent), parent.Node) ? GetContextualType(parent, contextFlags) : Type.None;
        if (parentType.IsNone)
        {
            return Type.None;
        }
        return GetTypeOfPropertyOfContextualType(parentType, NameOf(GetSymbolOfDeclaration(declaration)));
    }

    // checker.ts getContextualTypeForInitializerExpression
    private Type GetContextualTypeForInitializerExpression(NodeRef node, ContextFlags contextFlags)
    {
        var store = StoreOf(node);
        var declaration = At(node, store.Parent(node.Node));
        if (UtilitiesPublic.HasInitializer(store, declaration.Node) && store.Initializer(declaration.Node) == node.Node)
        {
            var result = GetContextualTypeForVariableLikeDeclaration(declaration, contextFlags);
            if (!result.IsNone)
            {
                return result;
            }
            var name = store.Name(declaration.Node);
            if ((contextFlags & ContextFlags.SkipBindingPatterns) == 0 && Utilities.IsBindingPattern(store, name) && store.Length(store.Elements(name)) > 0)
            {
                return GetTypeFromBindingPattern(At(node, name), includePatternInType: true, reportErrors: false);
            }
        }
        return Type.None;
    }

    // checker.ts getContextualTypeForReturnExpression
    private Type GetContextualTypeForReturnExpression(NodeRef node, ContextFlags contextFlags)
    {
        var store = StoreOf(node);
        var func = Utilities.GetContainingFunctionOrNone(store, node.Node);
        if (func.IsNone)
        {
            return Type.None;
        }
        var funcRef = At(node, func);
        var contextualReturnType = GetContextualReturnType(funcRef, contextFlags);
        if (contextualReturnType.IsNone)
        {
            return Type.None;
        }
        var functionFlags = GetFunctionFlagsOf(store, func);
        if ((functionFlags & FunctionFlags.Generator) != 0)
        {
            // Generator or AsyncGenerator function
            var isAsyncGenerator = (functionFlags & FunctionFlags.Async) != 0;
            if ((Types.Flags(contextualReturnType) & TypeFlags.Union) != 0)
            {
                contextualReturnType = FilterType(contextualReturnType, type => !GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, type, isAsyncGenerator).IsNone);
            }
            var iterationReturnType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, contextualReturnType, (functionFlags & FunctionFlags.Async) != 0);
            if (iterationReturnType.IsNone)
            {
                return Type.None;
            }
            contextualReturnType = iterationReturnType;
            // falls through to unwrap Promise for AsyncGenerators
        }
        if ((functionFlags & FunctionFlags.Async) != 0)
        {
            // Async function or AsyncGenerator function
            // Get the awaited type without the `Awaited<T>` alias
            var contextualAwaitedType = MapType(contextualReturnType, GetAwaitedTypeNoAlias);
            return contextualAwaitedType.IsNone ? Type.None : GetUnionType([contextualAwaitedType, CreatePromiseLikeType(contextualAwaitedType)]);
        }
        return contextualReturnType; // Regular function or Generator function
    }

    // checker.ts getContextualTypeForAwaitOperand
    private Type GetContextualTypeForAwaitOperand(NodeRef node, ContextFlags contextFlags)
    {
        var contextualType = GetContextualType(node, contextFlags);
        if (!contextualType.IsNone)
        {
            var contextualAwaitedType = GetAwaitedTypeNoAlias(contextualType);
            return contextualAwaitedType.IsNone ? Type.None : GetUnionType([contextualAwaitedType, CreatePromiseLikeType(contextualAwaitedType)]);
        }
        return Type.None;
    }

    // checker.ts getContextualTypeForYieldOperand
    private Type GetContextualTypeForYieldOperand(NodeRef node, ContextFlags contextFlags)
    {
        var store = StoreOf(node);
        var func = Utilities.GetContainingFunctionOrNone(store, node.Node);
        if (func.IsNone)
        {
            return Type.None;
        }
        var functionFlags = GetFunctionFlagsOf(store, func);
        var contextualReturnType = GetContextualReturnType(At(node, func), contextFlags);
        if (contextualReturnType.IsNone)
        {
            return Type.None;
        }
        var isAsyncGenerator = (functionFlags & FunctionFlags.Async) != 0;
        var hasAsterisk = !store.AsteriskToken(node.Node).IsNone;
        if (!hasAsterisk && (Types.Flags(contextualReturnType) & TypeFlags.Union) != 0)
        {
            contextualReturnType = FilterType(contextualReturnType, type => !GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, type, isAsyncGenerator).IsNone);
        }
        if (hasAsterisk)
        {
            var iterationTypes = GetIterationTypesOfGeneratorFunctionReturnType(contextualReturnType, isAsyncGenerator);
            var yieldType = iterationTypes is not null && !iterationTypes.YieldType.IsNone ? iterationTypes.YieldType : silentNeverType;
            var returnType = GetContextualType(node, contextFlags);
            if (returnType.IsNone)
            {
                returnType = silentNeverType;
            }
            var nextType = iterationTypes is not null && !iterationTypes.NextType.IsNone ? iterationTypes.NextType : unknownType;
            var generatorType = CreateGeneratorType(yieldType, returnType, nextType, isAsyncGenerator: false);
            if (isAsyncGenerator)
            {
                var asyncGeneratorType = CreateGeneratorType(yieldType, returnType, nextType, isAsyncGenerator: true);
                return GetUnionType([generatorType, asyncGeneratorType]);
            }
            return generatorType;
        }
        return GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Yield, contextualReturnType, isAsyncGenerator);
    }

    /// <summary>checker.ts getContextualIterationType.</summary>
    internal Type GetContextualIterationType(IterationTypeKind kind, NodeRef functionDecl)
    {
        var isAsync = (GetFunctionFlagsOf(StoreOf(functionDecl), functionDecl.Node) & FunctionFlags.Async) != 0;
        var contextualReturnType = GetContextualReturnType(functionDecl, ContextFlags.None);
        if (!contextualReturnType.IsNone)
        {
            return GetIterationTypeOfGeneratorFunctionReturnType(kind, contextualReturnType, isAsync);
        }
        return Type.None;
    }

    /// <summary>checker.ts getContextualReturnType.</summary>
    internal Type GetContextualReturnType(NodeRef functionDecl, ContextFlags contextFlags)
    {
        // If the containing function has a return type annotation, is a constructor, or is a get accessor whose
        // corresponding set accessor has a type annotation, return statements in the function are contextually typed
        var returnType = GetReturnTypeFromAnnotation(functionDecl);
        if (!returnType.IsNone)
        {
            return returnType;
        }
        // Otherwise, if the containing function is contextually typed by a function type with exactly one call signature
        // and that call signature is non-generic, return statements are contextually typed by the return type of the signature
        var signature = GetContextualSignatureForFunctionLikeDeclaration(functionDecl);
        if (!signature.IsNone && !IsResolvingReturnTypeOfSignature(signature))
        {
            var signatureReturnType = GetReturnTypeOfSignature(signature);
            var functionFlags = GetFunctionFlagsOf(StoreOf(functionDecl), functionDecl.Node);
            if ((functionFlags & FunctionFlags.Generator) != 0)
            {
                return FilterType(signatureReturnType, t =>
                    (Types.Flags(t) & (TypeFlags.AnyOrUnknown | TypeFlags.Void | TypeFlags.InstantiableNonPrimitive)) != 0 ||
                    CheckGeneratorInstantiationAssignabilityToReturnType(t, functionFlags, NodeRef.None));
            }
            if ((functionFlags & FunctionFlags.Async) != 0)
            {
                return FilterType(signatureReturnType, t =>
                    (Types.Flags(t) & (TypeFlags.AnyOrUnknown | TypeFlags.Void | TypeFlags.InstantiableNonPrimitive)) != 0 || !GetAwaitedTypeOfPromise(t).IsNone);
            }
            return signatureReturnType;
        }
        var store = StoreOf(functionDecl);
        var iife = Utilities.GetImmediatelyInvokedFunctionExpression(store, functionDecl.Node);
        if (!iife.IsNone)
        {
            return GetContextualType(At(functionDecl, iife), contextFlags);
        }
        return Type.None;
    }

    // ---- arguments and operands ----

    /// <summary>checker.ts getContextualTypeForArgument.</summary>
    internal Type GetContextualTypeForArgument(NodeRef callTarget, NodeRef arg)
    {
        var args = GetEffectiveCallArguments(callTarget);
        var argIndex = args.FindIndex(a => a.Node == arg); // -1 for e.g. the expression of a CallExpression, or the tag of a TaggedTemplateExpression
        return argIndex == -1 ? Type.None : GetContextualTypeForArgumentAtIndex(callTarget, argIndex);
    }

    /// <summary>checker.ts getContextualTypeForArgumentAtIndex.</summary>
    internal Type GetContextualTypeForArgumentAtIndex(NodeRef callTarget, int argIndex)
    {
        var store = StoreOf(callTarget);
        if (Utilities.IsImportCall(store, callTarget.Node))
        {
            return argIndex == 0 ? stringType
                : argIndex == 1 ? GetGlobalImportCallOptionsType(reportErrors: false)
                : anyType;
        }
        // If we're already in the process of resolving the given signature, don't resolve again as
        // that could cause infinite recursion. Instead, return anySignature.
        var signature = SignatureLinks.Has(callTarget) && SignatureLinks.Get(callTarget).ResolvedSignature == resolvingSignature ? resolvingSignature : GetResolvedSignature(callTarget);
        if (IsJsxOpeningLikeElement(store, callTarget.Node) && argIndex == 0)
        {
            return GetEffectiveFirstArgumentForJsxSignature(signature, callTarget);
        }
        var parameters = ParametersOf(signature);
        var restIndex = parameters.Length - 1;
        return SignatureHasRestParameter(signature) && argIndex >= restIndex
            ? GetIndexedAccessType(GetTypeOfSymbol(parameters[restIndex]), GetNumberLiteralType(argIndex - restIndex), AccessFlags.Contextual)
            : GetTypeAtPosition(signature, argIndex);
    }

    // checker.ts getContextualTypeForDecorator
    private Type GetContextualTypeForDecorator(NodeRef decorator)
    {
        var signature = GetDecoratorCallSignature(decorator);
        return signature.IsNone ? Type.None : GetOrCreateTypeFromSignature(signature);
    }

    // checker.ts getContextualTypeForSubstitutionExpression
    private Type GetContextualTypeForSubstitutionExpression(NodeRef template, NodeRef substitutionExpression)
    {
        var store = StoreOf(template);
        if (store.Kind(store.Parent(template.Node)) == SyntaxKind.TaggedTemplateExpression)
        {
            return GetContextualTypeForArgument(At(template, store.Parent(template.Node)), substitutionExpression);
        }
        return Type.None;
    }

    // checker.ts getContextualTypeForBinaryOperand
    private Type GetContextualTypeForBinaryOperand(NodeRef node, ContextFlags contextFlags)
    {
        var store = StoreOf(node);
        var binaryExpression = At(node, store.Parent(node.Node));
        var left = store.Left(binaryExpression.Node);
        var right = store.Right(binaryExpression.Node);
        switch (store.Kind(store.OperatorToken(binaryExpression.Node)))
        {
            case SyntaxKind.EqualsToken:
            case SyntaxKind.AmpersandAmpersandEqualsToken:
            case SyntaxKind.BarBarEqualsToken:
            case SyntaxKind.QuestionQuestionEqualsToken:
                return node.Node == right ? GetContextualTypeForAssignmentDeclaration(binaryExpression) : Type.None;
            case SyntaxKind.BarBarToken:
            case SyntaxKind.QuestionQuestionToken:
            {
                // When an || expression has a contextual type, the operands are contextually typed by that type, except
                // when that type originates in a binding pattern, the right operand is contextually typed by the type of
                // the left operand. When an || expression has no contextual type, the right operand is contextually typed
                // by the type of the left operand, except for the special case of Javascript declarations of the form
                // `namespace.prop = namespace.prop || {}`.
                var type = GetContextualType(binaryExpression, contextFlags);
                return node.Node == right && ((!type.IsNone && IsTypeWithPattern(type)) || (type.IsNone && !IsDefaultedExpandoInitializer(binaryExpression)))
                    ? GetTypeOfExpression(At(node, left))
                    : type;
            }
            case SyntaxKind.AmpersandAmpersandToken:
            case SyntaxKind.CommaToken:
                return node.Node == right ? GetContextualType(binaryExpression, contextFlags) : Type.None;
            default:
                return Type.None;
        }
    }

    // utilities.ts isDefaultedExpandoInitializer
    private bool IsDefaultedExpandoInitializer(NodeRef node)
    {
        var store = StoreOf(node);
        var parent = store.Parent(node.Node);
        var name = store.Kind(parent) == SyntaxKind.VariableDeclaration ? store.Name(parent)
            : store.Kind(parent) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(parent)) == SyntaxKind.EqualsToken ? store.Left(parent)
            : Node.None;
        return !name.IsNone && !Utilities.GetExpandoInitializer(store, store.Right(node.Node), Utilities.IsPrototypeAccess(store, name)).IsNone &&
            Utilities.IsEntityNameExpression(store, name) && Utilities.IsSameEntityName(store, name, store.Left(node.Node));
    }

    /// <summary>checker.ts getSymbolForExpression.</summary>
    private SymbolRef GetSymbolForExpression(NodeRef e)
    {
        var store = StoreOf(e);
        if (Utilities.CanHaveSymbol(store, e.Node))
        {
            var nodeSymbol = NodeSymbol(e);
            if (!nodeSymbol.IsNone)
            {
                return nodeSymbol;
            }
        }
        switch (store.Kind(e.Node))
        {
            case SyntaxKind.Identifier:
                return GetResolvedSymbol(e);
            case SyntaxKind.PropertyAccessExpression:
            {
                var lhsType = GetTypeOfExpression(At(e, store.Expression(e.Node)));
                var name = At(e, store.Name(e.Node));
                if (store.Kind(name.Node) == SyntaxKind.PrivateIdentifier)
                {
                    var lexicallyScopedSymbol = LookupSymbolForPrivateIdentifierDeclaration(name);
                    return lexicallyScopedSymbol.IsNone ? SymbolRef.None : GetPropertyOfType(lhsType, NameOf(lexicallyScopedSymbol));
                }
                return GetPropertyOfType(lhsType, store.Texts.GetString(store.EscapedText(name.Node))!);
            }
            case SyntaxKind.ElementAccessExpression:
            {
                var propType = CheckExpressionCached(At(e, store.ArgumentExpression(e.Node)));
                if (!IsTypeUsableAsPropertyName(propType))
                {
                    return SymbolRef.None;
                }
                var lhsType = GetTypeOfExpression(At(e, store.Expression(e.Node)));
                return GetPropertyOfType(lhsType, GetPropertyNameFromType(propType));
            }
        }
        return SymbolRef.None;
    }

    // checker.ts getContextualTypeForAssignmentDeclaration (the JavaScript assignment declarations: C9)
    private Type GetContextualTypeForAssignmentDeclaration(NodeRef binaryExpression)
    {
        var store = StoreOf(binaryExpression);
        var kind = Utilities.GetAssignmentDeclarationKind(store, binaryExpression.Node);
        var left = At(binaryExpression, store.Left(binaryExpression.Node));
        switch (kind)
        {
            case AssignmentDeclarationKind.None:
            case AssignmentDeclarationKind.ThisProperty:
            {
                var lhsSymbol = GetSymbolForExpression(left);
                var decl = lhsSymbol.IsNone ? NodeRef.None : ValueDeclarationOf(lhsSymbol);
                // Unannotated, uninitialized property declarations have a type implied by their usage in the constructor.
                // We avoid calling back into `getTypeOfExpression` and reentering contextual typing to avoid a bogus circularity error in that case.
                if (!decl.IsNone && Kind(decl) is SyntaxKind.PropertyDeclaration or SyntaxKind.PropertySignature)
                {
                    var overallAnnotation = GetEffectiveTypeAnnotationNode(decl);
                    if (!overallAnnotation.IsNone)
                    {
                        var mapper = ValueSymbolLinks.Has(lhsSymbol) ? MapperAt(ValueSymbolLinks.Get(lhsSymbol).Mapper) : null;
                        return InstantiateType(GetTypeFromTypeNode(overallAnnotation), mapper);
                    }
                    return Kind(decl) == SyntaxKind.PropertyDeclaration && !StoreOf(decl).Initializer(decl.Node).IsNone ? GetTypeOfExpression(left) : Type.None;
                }
                if (kind == AssignmentDeclarationKind.None)
                {
                    return GetTypeOfExpression(left);
                }
                return GetContextualTypeForThisPropertyAssignment(binaryExpression);
            }
            case AssignmentDeclarationKind.Property:
            {
                if (IsPossiblyAliasedThisProperty(binaryExpression, kind))
                {
                    return GetContextualTypeForThisPropertyAssignment(binaryExpression);
                }
                // If `binaryExpression.left` was assigned a symbol, then this is a new declaration; otherwise it is an assignment to an existing declaration.
                // See `bindStaticPropertyAssignment` in `binder.ts`.
                var leftSymbol = Utilities.CanHaveSymbol(store, left.Node) ? NodeSymbol(left) : SymbolRef.None;
                if (leftSymbol.IsNone)
                {
                    return GetTypeOfExpression(left);
                }
                var decl = ValueDeclarationOf(leftSymbol);
                if (decl.IsNone)
                {
                    return Type.None;
                }
                var overallAnnotation = GetEffectiveTypeAnnotationNode(decl);
                if (!overallAnnotation.IsNone)
                {
                    return GetTypeFromTypeNode(overallAnnotation);
                }
                // the property of an annotated parent (module C7, needed once flow types no longer suppress the check)
                var leftStore = StoreOf(left);
                if (Utilities.IsAccessExpression(leftStore, left.Node) && leftStore.Kind(leftStore.Expression(left.Node)) == SyntaxKind.Identifier)
                {
                    var id = At(left, leftStore.Expression(left.Node));
                    var parentSymbol = ResolveName(id, leftStore.Texts.GetString(leftStore.EscapedText(id.Node))!, NodeRef.None, SymbolFlags.Value, null, isUse: true);
                    if (!parentSymbol.IsNone)
                    {
                        var parentDeclaration = ValueDeclarationOf(parentSymbol);
                        var annotated = parentDeclaration.IsNone ? NodeRef.None : GetEffectiveTypeAnnotationNode(parentDeclaration);
                        if (!annotated.IsNone && leftStore.Kind(left.Node) == SyntaxKind.PropertyAccessExpression)
                        {
                            return GetTypeOfPropertyOfContextualType(GetTypeFromTypeNode(annotated), leftStore.Texts.GetString(leftStore.EscapedText(leftStore.Name(left.Node)))!);
                        }
                        return Type.None;
                    }
                }
                return Utilities.IsInJSFile(StoreOf(decl), decl.Node) || decl == left ? Type.None : GetTypeOfExpression(left);
            }
            case AssignmentDeclarationKind.ExportsProperty:
            case AssignmentDeclarationKind.Prototype:
            case AssignmentDeclarationKind.PrototypeProperty:
            case AssignmentDeclarationKind.ModuleExports:
            {
                var valueDeclaration = NodeRef.None;
                if (kind != AssignmentDeclarationKind.ModuleExports)
                {
                    var leftSymbol = Utilities.CanHaveSymbol(store, left.Node) ? NodeSymbol(left) : SymbolRef.None;
                    valueDeclaration = leftSymbol.IsNone ? NodeRef.None : ValueDeclarationOf(leftSymbol);
                }
                if (valueDeclaration.IsNone)
                {
                    var binarySymbol = NodeSymbol(binaryExpression);
                    valueDeclaration = binarySymbol.IsNone ? NodeRef.None : ValueDeclarationOf(binarySymbol);
                }
                var annotated = valueDeclaration.IsNone ? NodeRef.None : GetEffectiveTypeAnnotationNode(valueDeclaration);
                return annotated.IsNone ? Type.None : GetTypeFromTypeNode(annotated);
            }
            default:
                return Type.None;
        }
    }

    // checker.ts getContextualTypeForThisPropertyAssignment
    private Type GetContextualTypeForThisPropertyAssignment(NodeRef binaryExpression)
    {
        var symbol = NodeSymbol(binaryExpression);
        var store = StoreOf(binaryExpression);
        if (symbol.IsNone)
        {
            return GetTypeOfExpression(At(binaryExpression, store.Left(binaryExpression.Node)));
        }
        var valueDeclaration = ValueDeclarationOf(symbol);
        if (!valueDeclaration.IsNone)
        {
            var annotated = GetEffectiveTypeAnnotationNode(valueDeclaration);
            if (!annotated.IsNone)
            {
                var type = GetTypeFromTypeNode(annotated);
                if (!type.IsNone)
                {
                    return type;
                }
            }
        }
        var thisAccess = store.Left(binaryExpression.Node);
        var container = Utilities.GetThisContainer(store, store.Expression(thisAccess), includeArrowFunctions: false, includeClassComputedPropertyName: false);
        if (!Utilities.IsObjectLiteralMethod(store, container))
        {
            return Type.None;
        }
        var thisType = CheckThisExpression(At(binaryExpression, store.Expression(thisAccess)));
        var nameText = Utilities.GetElementOrPropertyAccessName(store, thisAccess);
        if (nameText.IsNone)
        {
            return Type.None;
        }
        var propType = GetTypeOfPropertyOfContextualType(thisType, store.Texts.GetString(nameText)!);
        return propType;
    }

    // ---- object literal members and elements ----

    // checker.ts isCircularMappedProperty
    private bool IsCircularMappedProperty(SymbolRef symbol) =>
        (GetCheckFlags(symbol) & CheckFlags.Mapped) != 0 && ValueSymbolLinks.Get(symbol).ResolvedType.IsNone && FindResolutionCycleStartIndex(symbol, TypeProperty) >= 0;

    // checker.ts isExcludedMappedPropertyName
    private bool IsExcludedMappedPropertyName(Type constraint, Type propertyNameType)
    {
        if ((Types.Flags(constraint) & TypeFlags.Conditional) != 0)
        {
            var data = Types.Data<ConditionalData>(constraint);
            return (Types.Flags(GetReducedType(GetTrueTypeFromConditionalType(constraint))) & TypeFlags.Never) != 0 &&
                GetActualTypeVariable(GetFalseTypeFromConditionalType(constraint)) == GetActualTypeVariable(data.CheckType) &&
                IsTypeAssignableTo(propertyNameType, data.ExtendsType);
        }
        if ((Types.Flags(constraint) & TypeFlags.Intersection) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(constraint).Types).ToArray())
            {
                if (IsExcludedMappedPropertyName(t, propertyNameType))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>checker.ts getTypeOfPropertyOfContextualType.</summary>
    internal Type GetTypeOfPropertyOfContextualType(Type type, string name, Type nameType = default)
    {
        return MapType(type, t =>
        {
            if ((Types.Flags(t) & TypeFlags.Intersection) != 0)
            {
                List<Type>? types = null;
                List<Type>? indexInfoCandidates = null;
                var ignoreIndexInfos = false;
                foreach (var constituentType in Types.Elements(Types.Union(t).Types).ToArray())
                {
                    if ((Types.Flags(constituentType) & TypeFlags.Object) == 0)
                    {
                        continue;
                    }
                    if (IsGenericMappedType(constituentType) && GetMappedTypeNameTypeKind(constituentType) != MappedTypeNameTypeKind.Remapping)
                    {
                        var substitutedType = GetIndexedMappedTypeSubstitutedTypeOfContextualType(constituentType, name, nameType);
                        types = AppendContextualPropertyTypeConstituent(types, substitutedType);
                        continue;
                    }
                    var propertyType = GetTypeOfConcretePropertyOfContextualType(constituentType, name);
                    if (propertyType.IsNone)
                    {
                        if (!ignoreIndexInfos)
                        {
                            (indexInfoCandidates ??= []).Add(constituentType);
                        }
                        continue;
                    }
                    ignoreIndexInfos = true;
                    indexInfoCandidates = null;
                    types = AppendContextualPropertyTypeConstituent(types, propertyType);
                }
                if (indexInfoCandidates is not null)
                {
                    foreach (var candidate in indexInfoCandidates)
                    {
                        var indexInfoType = GetTypeFromIndexInfosOfContextualType(candidate, name, nameType);
                        types = AppendContextualPropertyTypeConstituent(types, indexInfoType);
                    }
                }
                if (types is null)
                {
                    return Type.None;
                }
                if (types.Count == 1)
                {
                    return types[0];
                }
                return GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(types));
            }
            if ((Types.Flags(t) & TypeFlags.Object) == 0)
            {
                return Type.None;
            }
            if (IsGenericMappedType(t) && GetMappedTypeNameTypeKind(t) != MappedTypeNameTypeKind.Remapping)
            {
                return GetIndexedMappedTypeSubstitutedTypeOfContextualType(t, name, nameType);
            }
            var concrete = GetTypeOfConcretePropertyOfContextualType(t, name);
            return !concrete.IsNone ? concrete : GetTypeFromIndexInfosOfContextualType(t, name, nameType);
        }, noReductions: true);
    }

    // checker.ts appendContextualPropertyTypeConstituent
    private List<Type>? AppendContextualPropertyTypeConstituent(List<Type>? types, Type type)
    {
        // any doesn't provide any contextual information but could spoil the overall result by nullifying contextual information provided by other intersection constituents
        // so it gets replaced with `unknown` as `T & unknown` is just `T` and all types computed based on the contextual information provided by other constituens are still assignable to any
        if (type.IsNone)
        {
            return types;
        }
        (types ??= []).Add((Types.Flags(type) & TypeFlags.Any) != 0 ? unknownType : type);
        return types;
    }

    // checker.ts getIndexedMappedTypeSubstitutedTypeOfContextualType
    private Type GetIndexedMappedTypeSubstitutedTypeOfContextualType(Type type, string name, Type nameType)
    {
        var propertyNameType = !nameType.IsNone ? nameType : GetStringLiteralType(Utilities.UnescapeLeadingUnderscores(name));
        var constraint = GetConstraintTypeFromMappedType(type);
        // special case for conditional types pretending to be negated types
        var mappedNameType = Types.Object(type).NameType;
        if ((!mappedNameType.IsNone && IsExcludedMappedPropertyName(mappedNameType, propertyNameType)) || IsExcludedMappedPropertyName(constraint, propertyNameType))
        {
            return Type.None;
        }
        var constraintOfConstraint = GetBaseConstraintOfType(constraint);
        if (!IsTypeAssignableTo(propertyNameType, constraintOfConstraint.IsNone ? constraint : constraintOfConstraint))
        {
            return Type.None;
        }
        return SubstituteIndexedMappedType(type, propertyNameType);
    }

    // checker.ts getTypeOfConcretePropertyOfContextualType
    private Type GetTypeOfConcretePropertyOfContextualType(Type type, string name)
    {
        var prop = GetPropertyOfType(type, name);
        if (prop.IsNone || IsCircularMappedProperty(prop))
        {
            return Type.None;
        }
        return RemoveMissingType(GetTypeOfSymbol(prop), (SymbolFlagsOf(prop) & SymbolFlags.Optional) != 0);
    }

    // checker.ts getTypeFromIndexInfosOfContextualType
    private Type GetTypeFromIndexInfosOfContextualType(Type type, string name, Type nameType)
    {
        if (IsTupleType(type) && IsNumericLiteralName(name) && JsNumber.FromString(name) >= 0)
        {
            var restType = GetElementTypeOfSliceOfTupleType(type, TupleTarget(type).FixedLength, 0, writing: false, noReductions: true);
            if (!restType.IsNone)
            {
                return restType;
            }
        }
        var info = GetApplicableIndexInfoOf(GetIndexInfosOfType(type), !nameType.IsNone ? nameType : GetStringLiteralType(Utilities.UnescapeLeadingUnderscores(name)));
        return info?.Type ?? Type.None;
    }

    // checker.ts getContextualTypeForObjectLiteralMethod
    private Type GetContextualTypeForObjectLiteralMethod(NodeRef node, ContextFlags contextFlags)
    {
        if ((StoreOf(node).Flags(node.Node) & NodeFlags.InWithStatement) != 0)
        {
            // We cannot answer semantic questions within a with block, do not proceed any further
            return Type.None;
        }
        return GetContextualTypeForObjectLiteralElement(node, contextFlags);
    }

    // checker.ts getContextualTypeForObjectLiteralElement
    private Type GetContextualTypeForObjectLiteralElement(NodeRef element, ContextFlags contextFlags)
    {
        var store = StoreOf(element);
        var objectLiteral = At(element, store.Parent(element.Node));
        if (store.Kind(element.Node) == SyntaxKind.PropertyAssignment)
        {
            var propertyAssignmentType = GetContextualTypeForVariableLikeDeclaration(element, contextFlags);
            if (!propertyAssignmentType.IsNone)
            {
                return propertyAssignmentType;
            }
        }
        var type = GetApparentTypeOfContextualType(objectLiteral, contextFlags);
        if (type.IsNone)
        {
            return Type.None;
        }
        if (HasBindableName(element))
        {
            // For a (non-symbol) computed property, there is no reason to look up the name
            // in the type. It will just be "__computed", which does not appear in any
            // SymbolTable.
            var symbol = GetSymbolOfDeclaration(element);
            return GetTypeOfPropertyOfContextualType(type, NameOf(symbol), GetNameTypeOfSymbol(symbol));
        }
        var name = Utilities.GetNameOfDeclaration(store, element.Node);
        if (Utilities.HasDynamicName(store, element.Node))
        {
            if (!name.IsNone && store.Kind(name) == SyntaxKind.ComputedPropertyName)
            {
                var exprType = CheckExpression(At(element, store.Expression(name)));
                var propType = IsTypeUsableAsPropertyName(exprType) ? GetTypeOfPropertyOfContextualType(type, GetPropertyNameFromType(exprType)) : Type.None;
                if (!propType.IsNone)
                {
                    return propType;
                }
            }
        }
        var elementName = store.Name(element.Node);
        if (!elementName.IsNone)
        {
            var nameType = GetLiteralTypeFromPropertyName(At(element, elementName));
            // We avoid calling getApplicableIndexInfo here because it performs potentially expensive intersection reduction.
            return MapType(type, t => GetApplicableIndexInfoOf(GetIndexInfosOfStructuredType(t), nameType)?.Type ?? Type.None, noReductions: true);
        }
        return Type.None;
    }

    // checker.ts getIndexInfosOfStructuredType
    private List<IndexInfo> GetIndexInfosOfStructuredType(Type type)
    {
        if ((Types.Flags(type) & TypeFlags.Object) != 0)
        {
            return ResolveStructuredTypeMembers(type).IndexInfos!;
        }
        if ((Types.Flags(type) & TypeFlags.UnionOrIntersection) != 0)
        {
            return ResolveUnionOrIntersectionTypeMembers(type).IndexInfos!;
        }
        return [];
    }

    /// <summary>checker.ts getContextualTypeForElementExpression.</summary>
    internal Type GetContextualTypeForElementExpression(Type type, int index, int? length = null, int firstSpreadIndex = -1, int lastSpreadIndex = -1)
    {
        if (type.IsNone)
        {
            return Type.None;
        }
        return MapType(type, t =>
        {
            if (IsTupleType(t))
            {
                var target = TupleTarget(t);
                // If index is before any spread element and within the fixed part of the contextual tuple type, return
                // the type of the contextual tuple element.
                if ((firstSpreadIndex < 0 || index < firstSpreadIndex) && index < target.FixedLength)
                {
                    return RemoveMissingType(GetTypeArguments(t)[index], target.ElementFlags is { } elementFlags && elementFlags[index] != ElementFlags.None && true);
                }
                // When the length is known and the index is after all spread elements we compute the offset from the element
                // to the end and the number of ending fixed elements in the contextual tuple type.
                var offset = length is { } len && (lastSpreadIndex < 0 || index > lastSpreadIndex) ? len - index : 0;
                var fixedEndLength = offset > 0 && (target.CombinedFlags & ElementFlags.Variable) != 0 ? GetEndElementCount(target, ElementFlags.Fixed) : 0;
                // If the offset is within the ending fixed part of the contextual tuple type, return the type of the contextual
                // tuple element.
                if (offset > 0 && offset <= fixedEndLength)
                {
                    return GetTypeArguments(t)[GetTypeReferenceArity(t) - offset];
                }
                // Return a union of the possible contextual element types with no subtype reduction.
                return GetElementTypeOfSliceOfTupleType(t,
                    firstSpreadIndex < 0 ? target.FixedLength : Math.Min(target.FixedLength, firstSpreadIndex),
                    length is null || lastSpreadIndex < 0 ? fixedEndLength : Math.Min(fixedEndLength, length.Value - lastSpreadIndex),
                    writing: false, noReductions: true);
            }
            // If element index is known and a contextual property with that name exists, return it. Otherwise return the
            // iterated or element type of the contextual type.
            // (`!firstSpreadIndex` in TypeScript: no spread, or the first element is a spread)
            if (firstSpreadIndex <= 0 || index < firstSpreadIndex)
            {
                var propertyType = GetTypeOfPropertyOfContextualType(t, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (!propertyType.IsNone)
                {
                    return propertyType;
                }
            }
            return GetIteratedTypeOrElementType(IterationUse.Element, t, undefinedType, NodeRef.None, checkAssignability: false);
        }, noReductions: true);
    }

    // checker.ts getContextualTypeForConditionalOperand
    private Type GetContextualTypeForConditionalOperand(NodeRef node, ContextFlags contextFlags)
    {
        var store = StoreOf(node);
        var conditional = store.Parent(node.Node);
        return node.Node == store.WhenTrue(conditional) || node.Node == store.WhenFalse(conditional) ? GetContextualType(At(node, conditional), contextFlags) : Type.None;
    }

    // checker.ts isPossiblyDiscriminantValue
    private static bool IsPossiblyDiscriminantValue(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateExpression:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.Identifier:
            case SyntaxKind.UndefinedKeyword:
                return true;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ParenthesizedExpression:
                return IsPossiblyDiscriminantValue(store, store.Expression(node));
            case SyntaxKind.JsxExpression:
                return store.Expression(node).IsNone || IsPossiblyDiscriminantValue(store, store.Expression(node));
        }
        return false;
    }

    // checker.ts discriminateContextualTypeByObjectMembers
    private Type DiscriminateContextualTypeByObjectMembers(NodeRef node, Type contextualType)
    {
        if (discriminatedContextualTypes.TryGetValue((node, contextualType), out var cached))
        {
            return cached;
        }
        var result = GetMatchingUnionConstituentForObjectLiteral(contextualType, node);
        if (result.IsNone)
        {
            var store = StoreOf(node);
            var discriminators = new List<(Func<Type> GetType, string Name)>();
            foreach (var p in store.Nodes(store.Properties(node.Node)))
            {
                var pRef = At(node, p);
                var symbol = NodeSymbol(pRef);
                if (symbol.IsNone)
                {
                    continue;
                }
                var name = NameOf(symbol);
                if (store.Kind(p) == SyntaxKind.PropertyAssignment)
                {
                    if (IsPossiblyDiscriminantValue(store, store.Initializer(p)) && IsDiscriminantProperty(contextualType, name))
                    {
                        discriminators.Add((() => GetContextFreeTypeOfExpression(At(node, store.Initializer(p))), name));
                    }
                }
                else if (store.Kind(p) == SyntaxKind.ShorthandPropertyAssignment)
                {
                    if (IsDiscriminantProperty(contextualType, name))
                    {
                        discriminators.Add((() => GetContextFreeTypeOfExpression(At(node, store.Name(p))), name));
                    }
                }
            }
            var literalSymbol = NodeSymbol(node);
            var members = literalSymbol.IsNone ? null : MembersOf(literalSymbol);
            foreach (var s in GetPropertiesOfType(contextualType))
            {
                if ((SymbolFlagsOf(s) & SymbolFlags.Optional) != 0 && members is not null && !members.Has(NameOf(s)) && IsDiscriminantProperty(contextualType, NameOf(s)))
                {
                    discriminators.Add((() => undefinedType, NameOf(s)));
                }
            }
            result = DiscriminateTypeByDiscriminableItems(contextualType, discriminators, IsTypeAssignableTo);
        }
        discriminatedContextualTypes[(node, contextualType)] = result;
        return result;
    }

    // (discriminateTypeByDiscriminableItems, getKeyPropertyName, getConstituentTypeForKeyType: Checker.RelationHelpers.cs, C4)

    // checker.ts getMatchingUnionConstituentForObjectLiteral
    private Type GetMatchingUnionConstituentForObjectLiteral(Type unionType, NodeRef node)
    {
        var keyPropertyName = GetKeyPropertyName(unionType);
        if (keyPropertyName is null)
        {
            return Type.None;
        }
        var store = StoreOf(node);
        foreach (var p in store.Nodes(store.Properties(node.Node)))
        {
            var symbol = NodeSymbol(At(node, p));
            if (!symbol.IsNone && store.Kind(p) == SyntaxKind.PropertyAssignment && NameOf(symbol) == keyPropertyName && IsPossiblyDiscriminantValue(store, store.Initializer(p)))
            {
                var propType = GetContextFreeTypeOfExpression(At(node, store.Initializer(p)));
                return GetConstituentTypeForKeyType(unionType, propType);
            }
        }
        return Type.None;
    }

    // ---- contextual signatures ----

    // checker.ts getIntersectedSignatures
    private Signature GetIntersectedSignatures(List<Signature> signatures)
    {
        if (!noImplicitAny || signatures.Count == 0)
        {
            return Signature.None;
        }
        var left = signatures[0];
        for (var i = 1; i < signatures.Count && !left.IsNone; i++)
        {
            var right = signatures[i];
            left = left == right ? left
                : CompareTypeParametersIdentical(Signatures.Record(left).TypeParameters, Signatures.Record(right).TypeParameters) ? CombineSignaturesOfIntersectionMembers(left, right)
                : Signature.None;
        }
        return left;
    }

    // checker.ts combineIntersectionThisParam
    private SymbolRef CombineIntersectionThisParam(SymbolRef left, SymbolRef right, TypeMapper? mapper)
    {
        if (left.IsNone || right.IsNone)
        {
            return !left.IsNone ? left : right;
        }
        // A signature `this` type might be a read or a write position... It's very possible that it should be invariant
        // and we should refuse to merge signatures if there are `this` types and they do not match. However, so as to be
        // pessimistic when contextual typing, for now, we'll union the `this` types.
        var thisType = GetUnionType([GetTypeOfSymbol(left), InstantiateType(GetTypeOfSymbol(right), mapper)]);
        return CreateSymbolWithType(left, thisType);
    }

    // checker.ts combineIntersectionParameters
    private SymbolRef[] CombineIntersectionParameters(Signature left, Signature right, TypeMapper? mapper)
    {
        var leftCount = GetParameterCount(left);
        var rightCount = GetParameterCount(right);
        var longest = leftCount >= rightCount ? left : right;
        var shorter = longest == left ? right : left;
        var longestCount = longest == left ? leftCount : rightCount;
        var eitherHasEffectiveRest = HasEffectiveRestParameter(left) || HasEffectiveRestParameter(right);
        var needsExtraRestElement = eitherHasEffectiveRest && !HasEffectiveRestParameter(longest);
        var parameters = new SymbolRef[longestCount + (needsExtraRestElement ? 1 : 0)];
        for (var i = 0; i < longestCount; i++)
        {
            var longestParamType = TryGetTypeAtPosition(longest, i);
            if (longest == right)
            {
                longestParamType = InstantiateType(longestParamType, mapper);
            }
            var shorterParamType = TryGetTypeAtPosition(shorter, i);
            if (shorterParamType.IsNone)
            {
                shorterParamType = unknownType;
            }
            if (shorter == right)
            {
                shorterParamType = InstantiateType(shorterParamType, mapper);
            }
            var unionParamType = GetUnionType([longestParamType, shorterParamType]);
            var isRestParam = eitherHasEffectiveRest && !needsExtraRestElement && i == longestCount - 1;
            var isOptional = i >= GetMinArgumentCount(longest) && i >= GetMinArgumentCount(shorter);
            var leftName = i >= leftCount ? null : GetParameterNameAtPosition(left, i);
            var rightName = i >= rightCount ? null : GetParameterNameAtPosition(right, i);
            var paramName = leftName == rightName ? leftName : leftName is null ? rightName : rightName is null ? leftName : null;
            var paramSymbol = CreateSymbol(SymbolFlags.FunctionScopedVariable | (isOptional && !isRestParam ? SymbolFlags.Optional : SymbolFlags.None),
                paramName ?? "arg" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                isRestParam ? CheckFlags.RestParameter : isOptional ? CheckFlags.OptionalParameter : CheckFlags.None);
            var paramType = isRestParam ? CreateArrayType(unionParamType) : unionParamType; // (before the links are written)
            ValueSymbolLinks.Get(paramSymbol).ResolvedType = paramType;
            parameters[i] = paramSymbol;
        }
        if (needsExtraRestElement)
        {
            var restParamSymbol = CreateSymbol(SymbolFlags.FunctionScopedVariable, "args", CheckFlags.RestParameter);
            var restType = CreateArrayType(GetTypeAtPosition(shorter, longestCount));
            if (shorter == right)
            {
                restType = InstantiateType(restType, mapper);
            }
            ValueSymbolLinks.Get(restParamSymbol).ResolvedType = restType;
            parameters[longestCount] = restParamSymbol;
        }
        return parameters;
    }

    // checker.ts combineSignaturesOfIntersectionMembers
    private Signature CombineSignaturesOfIntersectionMembers(Signature left, Signature right)
    {
        var leftRecord = Signatures.Record(left);
        var rightRecord = Signatures.Record(right);
        var typeParams = leftRecord.TypeParameters.Count > 0 ? leftRecord.TypeParameters : rightRecord.TypeParameters;
        TypeMapper? paramMapper = null;
        if (leftRecord.TypeParameters.Count > 0 && rightRecord.TypeParameters.Count > 0)
        {
            paramMapper = CreateTypeMapper(Types.Elements(rightRecord.TypeParameters).ToArray(), Types.Elements(leftRecord.TypeParameters).ToArray());
            // We just use the type parameter defaults from the first signature
        }
        var flags = (leftRecord.Flags | rightRecord.Flags) & (SignatureFlags.PropagatingFlags & ~SignatureFlags.HasRestParameter);
        var declaration = leftRecord.Declaration;
        var parameters = CombineIntersectionParameters(left, right, paramMapper);
        if (parameters.Length > 0 && (GetCheckFlags(parameters[^1]) & CheckFlags.RestParameter) != 0)
        {
            flags |= SignatureFlags.HasRestParameter;
        }
        var thisParam = CombineIntersectionThisParam(leftRecord.ThisParameter, rightRecord.ThisParameter, paramMapper);
        var minArgCount = Math.Max(leftRecord.MinArgumentCount, rightRecord.MinArgumentCount);
        var result = CreateSignature(declaration, typeParams, thisParam, parameters, Type.None, minArgCount, flags);
        ref var record = ref Signatures.Record(result);
        record.CompositeKind = TypeFlags.Intersection;
        var leftComposite = leftRecord.CompositeKind == TypeFlags.Intersection ? leftRecord.CompositeSignatures : null;
        record.CompositeSignatures = [.. leftComposite ?? [left], right];
        if (paramMapper is not null)
        {
            Signatures.Record(result).Mapper = MapperHandle(leftRecord.CompositeKind == TypeFlags.Intersection && leftRecord.Mapper != 0 && leftRecord.CompositeSignatures is not null
                ? CombineTypeMappers(MapperAt(leftRecord.Mapper), paramMapper)
                : paramMapper);
        }
        return result;
    }

    // checker.ts getContextualCallSignature
    private Signature GetContextualCallSignature(Type type, NodeRef node)
    {
        var signatures = GetSignaturesOfType(type, SignatureKind.Call);
        var applicableByArity = signatures.FindAll(s => !IsAritySmaller(s, node));
        return applicableByArity.Count == 1 ? applicableByArity[0] : GetIntersectedSignatures(applicableByArity);
    }

    // checker.ts isAritySmaller (JSDoc optional parameters: C9)
    private bool IsAritySmaller(Signature signature, NodeRef target)
    {
        var store = StoreOf(target);
        var parameters = store.Nodes(store.Parameters(target.Node));
        var targetParameterCount = 0;
        for (; targetParameterCount < parameters.Length; targetParameterCount++)
        {
            var param = parameters[targetParameterCount];
            if (!store.Initializer(param).IsNone || !store.QuestionToken(param).IsNone || !store.DotDotDotToken(param).IsNone)
            {
                break;
            }
        }
        if (parameters.Length > 0 && ParameterIsThisKeyword(store, parameters[0]))
        {
            targetParameterCount--;
        }
        return !HasEffectiveRestParameter(signature) && GetParameterCount(signature) < targetParameterCount;
    }

    // checker.ts getContextualSignatureForFunctionLikeDeclaration
    private Signature GetContextualSignatureForFunctionLikeDeclaration(NodeRef node)
    {
        // Only function expressions, arrow functions, and object literal methods are contextually typed.
        var store = StoreOf(node);
        return IsFunctionExpressionOrArrowFunction(store, node.Node) || Utilities.IsObjectLiteralMethod(store, node.Node) ? GetContextualSignature(node) : Signature.None;
    }

    /// <summary>checker.ts getContextualSignature (JSDoc type tags: C9).</summary>
    internal Signature GetContextualSignature(NodeRef node)
    {
        var typeTagSignature = GetSignatureOfTypeTag(node);
        if (!typeTagSignature.IsNone)
        {
            return typeTagSignature;
        }
        var type = GetApparentTypeOfContextualType(node, ContextFlags.Signature);
        if (type.IsNone)
        {
            return Signature.None;
        }
        if ((Types.Flags(type) & TypeFlags.Union) == 0)
        {
            return GetContextualCallSignature(type, node);
        }
        List<Signature>? signatureList = null;
        foreach (var current in Types.Elements(Types.Union(type).Types).ToArray())
        {
            var signature = GetContextualCallSignature(current, node);
            if (!signature.IsNone)
            {
                if (signatureList is null)
                {
                    // This signature will contribute to contextual union signature
                    signatureList = [signature];
                }
                else if (CompareSignaturesIdentical(signatureList[0], signature, partialMatch: false, ignoreThisTypes: true, ignoreReturnTypes: true, CompareTypesIdentical) == Ternary.False)
                {
                    // Signatures aren't identical, do not use
                    return Signature.None;
                }
                else
                {
                    // Use this signature for contextual union signature
                    signatureList.Add(signature);
                }
            }
        }
        // Result is union of signatures collected (return type is union of return types of this signature set)
        if (signatureList is not null)
        {
            return signatureList.Count == 1 ? signatureList[0] : CreateUnionSignature(signatureList[0], [.. signatureList]);
        }
        return Signature.None;
    }

    // ---- literal widening in contexts ----

    // checker.ts getWidenedLiteralLikeTypeForContextualType
    private Type GetWidenedLiteralLikeTypeForContextualType(Type type, Type contextualType)
    {
        if (!IsLiteralOfContextualType(type, contextualType))
        {
            type = GetWidenedUniqueESSymbolType(GetWidenedLiteralType(type));
        }
        return GetRegularTypeOfLiteralType(type);
    }

    // checker.ts getWidenedUniqueESSymbolType
    private Type GetWidenedUniqueESSymbolType(Type type)
    {
        var flags = Types.Flags(type);
        return (flags & TypeFlags.UniqueESSymbol) != 0 ? esSymbolType
            : (flags & TypeFlags.Union) != 0 ? MapType(type, GetWidenedUniqueESSymbolType)
            : type;
    }

    // checker.ts getWidenedLiteralLikeTypeForContextualReturnTypeIfNeeded
    private Type GetWidenedLiteralLikeTypeForContextualReturnTypeIfNeeded(Type type, Type contextualSignatureReturnType, bool isAsync)
    {
        if (!type.IsNone && IsUnitType(type))
        {
            var contextualType = contextualSignatureReturnType.IsNone ? Type.None
                : isAsync ? GetPromisedTypeOfPromise(contextualSignatureReturnType)
                : contextualSignatureReturnType;
            type = GetWidenedLiteralLikeTypeForContextualType(type, contextualType);
        }
        return type;
    }

    // checker.ts getWidenedLiteralLikeTypeForContextualIterationTypeIfNeeded
    private Type GetWidenedLiteralLikeTypeForContextualIterationTypeIfNeeded(Type type, Type contextualSignatureReturnType, IterationTypeKind kind, bool isAsyncGenerator)
    {
        if (!type.IsNone && IsUnitType(type))
        {
            var contextualType = contextualSignatureReturnType.IsNone ? Type.None : GetIterationTypeOfGeneratorFunctionReturnType(kind, contextualSignatureReturnType, isAsyncGenerator);
            type = GetWidenedLiteralLikeTypeForContextualType(type, contextualType);
        }
        return type;
    }

    // a contextual type originating in a binding pattern (types.ts Type.pattern)
    private bool IsTypeWithPattern(Type type) => typePatterns.ContainsKey(type);

    // checker.ts Type.pattern: the binding pattern or object literal a type was implied by
    private readonly Dictionary<Type, NodeRef> typePatterns = [];
}
