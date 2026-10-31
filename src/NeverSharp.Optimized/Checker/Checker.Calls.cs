// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (module C5; the functions are listed below)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (details below).
// Functions (call resolution: getEffectiveCallArguments, getResolvedSignature, resolveSignature,
// resolveCallExpression, resolveNewExpression, resolveTaggedTemplateExpression, resolveInstanceofExpression,
// resolveUntypedCall, resolveErrorCall, reorderCandidates, hasCorrectArity, hasCorrectTypeArgumentArity,
// getSignatureApplicabilityError, getThisArgumentOfCall, getThisArgumentType, inferTypeArguments, getSpreadArgumentType,
// getMutableArrayOrTupleType, checkTypeArguments, resolveCall, chooseOverload, getArgumentArityError,
// getTypeArgumentArityError, getCandidateForOverloadFailure, createUnionOfSignaturesForOverloadFailure,
// pickLongestCandidateSignature, getLongestCandidateIndex, isUntypedFunctionCall, invocationError(Details),
// isConstructorAccessible, typeHasProtectedAccessibleBase, checkCallExpression, checkImportCallExpression,
// checkTaggedTemplateExpression, getDiagnosticForCallNode, getErrorNodeForCallNode, isPromiseResolveArityError,
// instantiateSignatureInContextOf, applyToParameterTypes, applyToReturnTypes, isSymbolOrSymbolForCall, isRequireCall)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (replaces C1's first-signature calls). SyntheticExpression nodes
// are represented by CallArg values; module C6: inferTypeArguments with the inferences, getSignatureInstantiation with
// inferred type parameters, instantiateSignatureInContextOf, applyToReturnTypes with type predicates; module C9b: JSX
// opening-like elements and fragments as calls (Checker.Jsx.cs). Module C9d: JavaScript require calls
// (isCommonJsRequire, resolveExternalModuleTypeByLiteral) and expando assignments of calls. Module C9e: decorators as
// calls (resolveDecorator, isPotentiallyUncalledDecorator, getDiagnosticHeadMessageForDecoratorResolution,
// getDecoratorCallSignature, getESDecoratorCallSignature, getLegacyDecoratorCallSignature, the decorator context types
// createClassDecoratorContextType and relatives, getClassMemberDecoratorContextOverrideType,
// createClassMemberDecoratorContextTypeForNode, createClassFieldDecoratorInitializerMutatorType,
// createESDecoratorCallSignature, createCallSignature, createFunctionType, createGetterFunctionType,
// createSetterFunctionType, createParameter, createProperty, tryCreateTypeReference, createTypedPropertyDescriptorType,
// getParentTypeOfClassElement, getClassElementPropertyKeyType); createCallSignature's synthetic function type node is
// the SyntheticFunctionTypeDeclaration mark of the signature record.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// An effective argument of a call-like expression: an argument expression, or a synthetic expression (checker.ts
/// SyntheticExpression: a type, a spread flag and a tuple name source) whose text range is that of <see cref="Node"/>
/// (or Pos..End).
/// </summary>
internal readonly record struct CallArg(NodeRef Node, Type SyntheticType, bool IsSpread, NodeRef TupleNameSource, int Pos, int End)
{
    public bool IsSynthetic => !SyntheticType.IsNone;
}

internal sealed partial class Checker
{
    // checker.ts isInferencePartiallyBlocked (language service only: always false)
    private const bool IsInferencePartiallyBlocked = false;

    // checker.ts createSyntheticExpression
    private CallArg CreateSyntheticArg(NodeRef parent, Type type, bool isSpread = false, NodeRef tupleNameSource = default)
    {
        var store = StoreOf(parent);
        return new CallArg(parent, type, isSpread, tupleNameSource, store.Pos(parent.Node), store.End(parent.Node));
    }

    private static CallArg RealArg(NodeRef node) => new(node, Type.None, false, NodeRef.None, 0, 0);

    private bool IsOmittedArg(CallArg arg) => !arg.IsSynthetic && Kind(arg.Node) == SyntaxKind.OmittedExpression;

    // checker.ts isSpreadArgument
    private bool IsSpreadArgument(CallArg arg) => arg.IsSynthetic ? arg.IsSpread : Kind(arg.Node) == SyntaxKind.SpreadElement;

    // checker.ts getSpreadArgumentIndex
    private int GetSpreadArgumentIndex(List<CallArg> args, int start = 0)
    {
        for (var i = start; i < args.Count; i++)
        {
            if (IsSpreadArgument(args[i]))
            {
                return i;
            }
        }
        return -1;
    }

    // checkExpression of an argument (checkSyntheticExpression for synthetic ones)
    private Type CheckArgumentExpression(CallArg arg, CheckMode checkMode) =>
        arg.IsSynthetic ? arg.IsSpread ? GetIndexedAccessType(arg.SyntheticType, numberType) : arg.SyntheticType : CheckExpression(arg.Node, checkMode);

    // checkExpressionWithContextualType of an argument
    private Type CheckArgumentWithContextualType(CallArg arg, Type contextualType, InferenceContext? inferenceContext, CheckMode checkMode)
    {
        if (!arg.IsSynthetic)
        {
            return CheckExpressionWithContextualType(arg.Node, contextualType, inferenceContext, checkMode);
        }
        var type = CheckArgumentExpression(arg, checkMode);
        return MaybeTypeOfKind(type, TypeFlags.Literal) && IsLiteralOfContextualType(type, contextualType) ? GetRegularTypeOfLiteralType(type) : type;
    }

    /// <summary>checker.ts getEffectiveCallArguments.</summary>
    internal List<CallArg> GetEffectiveCallArguments(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var result = new List<CallArg>();
        switch (store.Kind(n))
        {
            case SyntaxKind.JsxOpeningFragment:
                // This attributes Type does not include a children property yet, the same way a fragment created with <React.Fragment> does not at this stage
                result.Add(CreateSyntheticArg(node, emptyFreshJsxObjectType));
                return result;
            case SyntaxKind.JsxOpeningElement:
            case SyntaxKind.JsxSelfClosingElement:
            {
                var attributes = store.Attributes(n);
                if (store.Length(store.Properties(attributes)) > 0 ||
                    (store.Kind(n) == SyntaxKind.JsxOpeningElement && store.Length(store.Children(store.Parent(n))) > 0))
                {
                    result.Add(RealArg(At(node, attributes)));
                }
                return result;
            }
            case SyntaxKind.TaggedTemplateExpression:
            {
                var template = At(node, store.Template(n));
                result.Add(CreateSyntheticArg(template, GetGlobalTemplateStringsArrayType()));
                if (store.Kind(template.Node) == SyntaxKind.TemplateExpression)
                {
                    foreach (var span in store.Nodes(store.TemplateSpans(template.Node)))
                    {
                        result.Add(RealArg(At(node, store.Expression(span))));
                    }
                }
                return result;
            }
            case SyntaxKind.Decorator:
                return GetEffectiveDecoratorArguments(node);
            case SyntaxKind.BinaryExpression:
                result.Add(RealArg(At(node, store.Left(n))));
                return result;
        }
        var arguments = store.Arguments(n);
        if (arguments.IsNone)
        {
            return result;
        }
        var args = store.Nodes(arguments);
        var spreadIndex = -1;
        for (var i = 0; i < args.Length; i++)
        {
            if (store.Kind(args[i]) == SyntaxKind.SpreadElement)
            {
                spreadIndex = i;
                break;
            }
        }
        if (spreadIndex >= 0)
        {
            // Create synthetic arguments from spreads of tuple types.
            for (var i = 0; i < spreadIndex; i++)
            {
                result.Add(RealArg(At(node, args[i])));
            }
            for (var i = spreadIndex; i < args.Length; i++)
            {
                var arg = At(node, args[i]);
                // We can call checkExpressionCached because spread expressions never have a contextual type.
                var spreadType = store.Kind(args[i]) != SyntaxKind.SpreadElement ? Type.None
                    : flowLoopCount != 0 ? CheckExpression(At(node, store.Expression(args[i]))) // (module C7)
                    : CheckExpressionCached(At(node, store.Expression(args[i])));
                if (!spreadType.IsNone && IsTupleType(spreadType))
                {
                    var elementTypes = GetElementTypes(spreadType);
                    var target = TupleTarget(spreadType);
                    for (var j = 0; j < elementTypes.Length; j++)
                    {
                        var flags = target.ElementFlags![j];
                        var t = elementTypes[j];
                        result.Add(CreateSyntheticArg(arg, (flags & ElementFlags.Rest) != 0 ? CreateArrayType(t) : t, (flags & ElementFlags.Variable) != 0,
                            target.LabeledElementDeclarations is { } labels && j < labels.Length ? labels[j] : NodeRef.None));
                    }
                }
                else
                {
                    result.Add(RealArg(arg));
                }
            }
            return result;
        }
        foreach (var a in args)
        {
            result.Add(RealArg(At(node, a)));
        }
        return result;
    }

    // checker.ts getEffectiveDecoratorArguments
    private List<CallArg> GetEffectiveDecoratorArguments(NodeRef node)
    {
        var expr = At(node, StoreOf(node).Expression(node.Node));
        var signature = GetDecoratorCallSignature(node);
        var args = new List<CallArg>();
        if (!signature.IsNone)
        {
            foreach (var param in ParametersOf(signature))
            {
                args.Add(CreateSyntheticArg(expr, GetTypeOfSymbol(param)));
            }
        }
        return args;
    }

    /// <summary>checker.ts getDecoratorCallSignature.</summary>
    internal Signature GetDecoratorCallSignature(NodeRef decorator) =>
        compilerOptions.ExperimentalDecorators == true ? GetLegacyDecoratorCallSignature(decorator) : GetESDecoratorCallSignature(decorator);

    // checker.ts createParameter
    private SymbolRef CreateParameter(string name, Type type)
    {
        var symbol = CreateSymbol(SymbolFlags.FunctionScopedVariable, name);
        ValueSymbolLinks.Get(symbol).ResolvedType = type;
        return symbol;
    }

    // checker.ts createProperty
    private SymbolRef CreateProperty(string name, Type type)
    {
        var symbol = CreateSymbol(SymbolFlags.Property, name);
        ValueSymbolLinks.Get(symbol).ResolvedType = type;
        return symbol;
    }

    // checker.ts createCallSignature (the declaration is a synthetic function type node)
    private Signature CreateCallSignature(SymbolRef[] parameters, Type returnType, int minArgumentCount = -1)
    {
        var signature = CreateSignature(NodeRef.None, TypeList.Empty, SymbolRef.None, parameters, returnType, minArgumentCount < 0 ? parameters.Length : minArgumentCount, SignatureFlags.None);
        Signatures.Record(signature).SyntheticFunctionTypeDeclaration = true;
        return signature;
    }

    // checker.ts createFunctionType
    private Type CreateFunctionType(SymbolRef[] parameters, Type returnType, int minArgumentCount = -1, SymbolRef thisParameter = default)
    {
        var signature = CreateCallSignature(parameters, returnType, minArgumentCount);
        Signatures.Record(signature).ThisParameter = thisParameter;
        return GetOrCreateTypeFromSignature(signature);
    }

    // checker.ts createGetterFunctionType, createSetterFunctionType
    private Type CreateGetterFunctionType(Type type) => CreateFunctionType([], type);

    private Type CreateSetterFunctionType(Type type) => CreateFunctionType([CreateParameter("value", type)], voidType);

    // checker.ts tryCreateTypeReference
    private Type TryCreateTypeReference(Type target, ReadOnlySpan<Type> typeArguments) =>
        typeArguments.Length > 0 && target == emptyGenericType ? unknownType : CreateTypeReference(target, typeArguments);

    // checker.ts getGlobalClassDecoratorContextType and relatives, getGlobalTypedPropertyDescriptorType
    private Type deferredGlobalClassDecoratorContextType;
    private Type deferredGlobalClassMethodDecoratorContextType;
    private Type deferredGlobalClassGetterDecoratorContextType;
    private Type deferredGlobalClassSetterDecoratorContextType;
    private Type deferredGlobalClassAccessorDecoratorContextType;
    private Type deferredGlobalClassAccessorDecoratorTargetType;
    private Type deferredGlobalClassAccessorDecoratorResultType;
    private Type deferredGlobalClassFieldDecoratorContextType;
    private Type deferredGlobalTypedPropertyDescriptorType;

    private Type GetGlobalDecoratorType(ref Type cache, string name, int arity)
    {
        if (cache.IsNone)
        {
            cache = GetGlobalType(name, arity, reportErrors: true);
        }
        return cache.IsNone ? emptyGenericType : cache;
    }

    private Type GetGlobalTypedPropertyDescriptorType()
    {
        if (deferredGlobalTypedPropertyDescriptorType.IsNone)
        {
            var t = GetGlobalType("TypedPropertyDescriptor", 1, reportErrors: true);
            deferredGlobalTypedPropertyDescriptorType = t.IsNone ? emptyGenericType : t;
        }
        return deferredGlobalTypedPropertyDescriptorType;
    }

    // checker.ts createTypedPropertyDescriptorType
    private Type CreateTypedPropertyDescriptorType(Type propertyType) => CreateTypeFromGenericGlobalType(GetGlobalTypedPropertyDescriptorType(), [propertyType]);

    // checker.ts createClassDecoratorContextType and relatives
    private Type CreateClassDecoratorContextType(Type classType) =>
        TryCreateTypeReference(GetGlobalDecoratorType(ref deferredGlobalClassDecoratorContextType, "ClassDecoratorContext", 1), [classType]);

    private Type CreateClassMemberDecoratorType(ref Type cache, string name, Type thisType, Type valueType) =>
        TryCreateTypeReference(GetGlobalDecoratorType(ref cache, name, 2), [thisType, valueType]);

    // checker.ts getClassMemberDecoratorContextOverrideType
    private readonly Dictionary<(bool IsPrivate, bool IsStatic, Type NameType), Type> decoratorContextOverrideTypeCache = [];

    private Type GetClassMemberDecoratorContextOverrideType(Type nameType, bool isPrivate, bool isStatic)
    {
        if (!decoratorContextOverrideTypeCache.TryGetValue((isPrivate, isStatic, nameType), out var overrideType))
        {
            var members = new SymTable(Names);
            members.Set("name", CreateProperty("name", nameType));
            members.Set("private", CreateProperty("private", isPrivate ? trueType : falseType));
            members.Set("static", CreateProperty("static", isStatic ? trueType : falseType));
            overrideType = CreateAnonymousType(SymbolRef.None, members, [], [], []);
            decoratorContextOverrideTypeCache[(isPrivate, isStatic, nameType)] = overrideType;
        }
        return overrideType;
    }

    // checker.ts createClassMemberDecoratorContextTypeForNode
    private Type CreateClassMemberDecoratorContextTypeForNode(NodeRef node, Type thisType, Type valueType)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var isStatic = Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Static);
        var name = store.Name(n);
        var isPrivate = store.Kind(name) == SyntaxKind.PrivateIdentifier;
        var nameType = isPrivate ? GetStringLiteralType(store.Texts.GetString(UtilitiesPublic.IdText(store, name)) ?? "") : GetLiteralTypeFromPropertyName(At(node, name));
        var contextType = store.Kind(n) switch
        {
            SyntaxKind.MethodDeclaration => CreateClassMemberDecoratorType(ref deferredGlobalClassMethodDecoratorContextType, "ClassMethodDecoratorContext", thisType, valueType),
            SyntaxKind.GetAccessor => CreateClassMemberDecoratorType(ref deferredGlobalClassGetterDecoratorContextType, "ClassGetterDecoratorContext", thisType, valueType),
            SyntaxKind.SetAccessor => CreateClassMemberDecoratorType(ref deferredGlobalClassSetterDecoratorContextType, "ClassSetterDecoratorContext", thisType, valueType),
            _ => Utilities.IsAutoAccessorPropertyDeclaration(store, n)
                ? CreateClassMemberDecoratorType(ref deferredGlobalClassAccessorDecoratorContextType, "ClassAccessorDecoratorContext", thisType, valueType)
                : CreateClassMemberDecoratorType(ref deferredGlobalClassFieldDecoratorContextType, "ClassFieldDecoratorContext", thisType, valueType),
        };
        var overrideType = GetClassMemberDecoratorContextOverrideType(nameType, isPrivate, isStatic);
        return GetIntersectionType([contextType, overrideType]);
    }

    // checker.ts createClassFieldDecoratorInitializerMutatorType
    private Type CreateClassFieldDecoratorInitializerMutatorType(Type thisType, Type valueType)
    {
        var thisParam = CreateParameter("this", thisType);
        var valueParam = CreateParameter("value", valueType);
        return CreateFunctionType([valueParam], valueType, 1, thisParam);
    }

    // checker.ts createESDecoratorCallSignature
    private Signature CreateESDecoratorCallSignature(Type targetType, Type contextType, Type nonOptionalReturnType)
    {
        var targetParam = CreateParameter("target", targetType);
        var contextParam = CreateParameter("context", contextType);
        var returnType = GetUnionType([nonOptionalReturnType, voidType]);
        return CreateCallSignature([targetParam, contextParam], returnType);
    }

    // the "this" type of a class member decorator: the class constructor type or the instance type
    private Type GetDecoratedMemberThisType(NodeRef node)
    {
        var store = StoreOf(node);
        var classSymbol = GetSymbolOfDeclaration(At(node, store.Parent(node.Node)));
        return Utilities.HasSyntacticModifierUncached(store, node.Node, ModifierFlags.Static) ? GetTypeOfSymbol(classSymbol) : GetDeclaredTypeOfClassOrInterface(classSymbol);
    }

    // checker.ts getESDecoratorCallSignature
    private Signature GetESDecoratorCallSignature(NodeRef decorator)
    {
        var parent = Parent(decorator);
        ref var links = ref SignatureLinks.Get(parent);
        if (links.DecoratorSignature.IsNone)
        {
            links.DecoratorSignature = anySignature;
            var store = StoreOf(parent);
            var n = parent.Node;
            Signature result = anySignature;
            switch (store.Kind(n))
            {
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                {
                    // Class decorators have a `context` of `ClassDecoratorContext<Class>`, where the `Class` type
                    // argument will be the "final type" of the class after all decorators are applied.
                    var targetType = GetTypeOfSymbol(GetSymbolOfDeclaration(parent));
                    var contextType = CreateClassDecoratorContextType(targetType);
                    result = CreateESDecoratorCallSignature(targetType, contextType, targetType);
                    break;
                }
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                {
                    if (!Utilities.IsClassLike(store, store.Parent(n)))
                    {
                        break;
                    }
                    var kind = store.Kind(n);
                    var valueType = kind == SyntaxKind.MethodDeclaration ? GetOrCreateTypeFromSignature(GetSignatureFromDeclaration(parent)) : GetTypeOfNode(parent);
                    var thisType = GetDecoratedMemberThisType(parent);
                    // We wrap the "input type", if necessary, to match the decoration target. For getters this is
                    // something like `() => inputType`, for setters it's `(value: inputType) => void` and for
                    // methods it is just the input type.
                    var targetType = kind == SyntaxKind.GetAccessor ? CreateGetterFunctionType(valueType)
                        : kind == SyntaxKind.SetAccessor ? CreateSetterFunctionType(valueType)
                        : valueType;
                    var contextType = CreateClassMemberDecoratorContextTypeForNode(parent, thisType, valueType);
                    // We also wrap the "output type", as needed.
                    var returnType = kind == SyntaxKind.GetAccessor ? CreateGetterFunctionType(valueType)
                        : kind == SyntaxKind.SetAccessor ? CreateSetterFunctionType(valueType)
                        : valueType;
                    result = CreateESDecoratorCallSignature(targetType, contextType, returnType);
                    break;
                }
                case SyntaxKind.PropertyDeclaration:
                {
                    if (!Utilities.IsClassLike(store, store.Parent(n)))
                    {
                        break;
                    }
                    var valueType = GetTypeOfNode(parent);
                    var thisType = GetDecoratedMemberThisType(parent);
                    var hasAccessor = HasAccessorModifier(parent);
                    // The `target` of an auto-accessor decorator is a `{ get, set }` object; the `target` of a regular
                    // field decorator is always `undefined`.
                    var targetType = hasAccessor
                        ? CreateClassMemberDecoratorType(ref deferredGlobalClassAccessorDecoratorTargetType, "ClassAccessorDecoratorTarget", thisType, valueType)
                        : undefinedType;
                    var contextType = CreateClassMemberDecoratorContextTypeForNode(parent, thisType, valueType);
                    var returnType = hasAccessor
                        ? CreateClassMemberDecoratorType(ref deferredGlobalClassAccessorDecoratorResultType, "ClassAccessorDecoratorResult", thisType, valueType)
                        : CreateClassFieldDecoratorInitializerMutatorType(thisType, valueType);
                    result = CreateESDecoratorCallSignature(targetType, contextType, returnType);
                    break;
                }
            }
            SignatureLinks.Get(parent).DecoratorSignature = result;
        }
        var signature = SignatureLinks.Get(parent).DecoratorSignature;
        return signature == anySignature ? Signature.None : signature;
    }

    // checker.ts getLegacyDecoratorCallSignature
    private Signature GetLegacyDecoratorCallSignature(NodeRef decorator)
    {
        var parent = Parent(decorator);
        ref var links = ref SignatureLinks.Get(parent);
        if (links.DecoratorSignature.IsNone)
        {
            links.DecoratorSignature = anySignature;
            var store = StoreOf(parent);
            var n = parent.Node;
            Signature result = anySignature;
            switch (store.Kind(n))
            {
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                {
                    // For a class decorator, the `target` is the type of the class (e.g. the
                    // "static" or "constructor" side of the class).
                    var targetType = GetTypeOfSymbol(GetSymbolOfDeclaration(parent));
                    var targetParam = CreateParameter("target", targetType);
                    result = CreateCallSignature([targetParam], GetUnionType([targetType, voidType]));
                    break;
                }
                case SyntaxKind.Parameter:
                {
                    var func = store.Parent(n);
                    var funcKind = store.Kind(func);
                    if (funcKind != SyntaxKind.Constructor &&
                        !(funcKind == SyntaxKind.MethodDeclaration || funcKind == SyntaxKind.SetAccessor && Utilities.IsClassLike(store, store.Parent(func))))
                    {
                        break;
                    }
                    var thisParameter = GetThisParameter(store, func);
                    if (thisParameter == n)
                    {
                        break;
                    }
                    var index = Array.IndexOf(store.Nodes(store.Parameters(func)).ToArray(), n);
                    if (!thisParameter.IsNone)
                    {
                        index--;
                    }
                    // A parameter declaration decorator will have three arguments (see `ParameterDecorator` in
                    // core.d.ts).
                    var targetType = funcKind == SyntaxKind.Constructor ? GetTypeOfSymbol(GetSymbolOfDeclaration(At(parent, store.Parent(func))))
                        : GetParentTypeOfClassElement(At(parent, func));
                    var keyType = funcKind == SyntaxKind.Constructor ? undefinedType : GetClassElementPropertyKeyType(At(parent, func));
                    var indexType = GetNumberLiteralType(index);
                    result = CreateCallSignature([CreateParameter("target", targetType), CreateParameter("propertyKey", keyType), CreateParameter("parameterIndex", indexType)], voidType);
                    break;
                }
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.PropertyDeclaration:
                {
                    if (!Utilities.IsClassLike(store, store.Parent(n)))
                    {
                        break;
                    }
                    // A method or accessor declaration decorator will have either two or three arguments (see
                    // `PropertyDecorator` and `MethodDecorator` in core.d.ts).
                    var targetParam = CreateParameter("target", GetParentTypeOfClassElement(parent));
                    var keyParam = CreateParameter("propertyKey", GetClassElementPropertyKeyType(parent));
                    var isProperty = store.Kind(n) == SyntaxKind.PropertyDeclaration;
                    var returnType = isProperty ? voidType : CreateTypedPropertyDescriptorType(GetTypeOfNode(parent));
                    var hasPropDesc = !isProperty || HasAccessorModifier(parent);
                    if (hasPropDesc)
                    {
                        var descriptorParam = CreateParameter("descriptor", CreateTypedPropertyDescriptorType(GetTypeOfNode(parent)));
                        result = CreateCallSignature([targetParam, keyParam, descriptorParam], GetUnionType([returnType, voidType]));
                    }
                    else
                    {
                        result = CreateCallSignature([targetParam, keyParam], GetUnionType([returnType, voidType]));
                    }
                    break;
                }
            }
            SignatureLinks.Get(parent).DecoratorSignature = result;
        }
        var signature = SignatureLinks.Get(parent).DecoratorSignature;
        return signature == anySignature ? Signature.None : signature;
    }

    // checker.ts getParentTypeOfClassElement
    private Type GetParentTypeOfClassElement(NodeRef node)
    {
        var classSymbol = GetSymbolOfNode(At(node, StoreOf(node).Parent(node.Node)));
        return Utilities.IsStatic(StoreOf(node), node.Node) ? GetTypeOfSymbol(classSymbol) : GetDeclaredTypeOfSymbol(classSymbol);
    }

    // checker.ts getClassElementPropertyKeyType
    private Type GetClassElementPropertyKeyType(NodeRef element)
    {
        var store = StoreOf(element);
        var name = store.Name(element.Node);
        switch (store.Kind(name))
        {
            case SyntaxKind.Identifier:
                return GetStringLiteralType(store.Texts.GetString(UtilitiesPublic.IdText(store, name)) ?? "");
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.StringLiteral:
                return GetStringLiteralType(store.Texts.GetString(store.Text(name)) ?? "");
            case SyntaxKind.ComputedPropertyName:
                var nameType = CheckComputedPropertyName(At(element, name));
                return IsTypeAssignableToKind(nameType, TypeFlags.ESSymbolLike) ? nameType : stringType;
            default:
                return errorType; // Debug.fail("Unsupported property name.")
        }
    }

    // checker.ts getGlobalTemplateStringsArrayType
    private Type deferredGlobalTemplateStringsArrayType;

    private Type GetGlobalTemplateStringsArrayType()
    {
        if (deferredGlobalTemplateStringsArrayType.IsNone)
        {
            var t = GetGlobalType("TemplateStringsArray", 0, reportErrors: true);
            deferredGlobalTemplateStringsArrayType = t.IsNone ? emptyObjectType : t;
        }
        return deferredGlobalTemplateStringsArrayType;
    }

    // checker.ts getGlobalImportCallOptionsType
    private Type deferredGlobalImportCallOptionsType;

    private Type GetGlobalImportCallOptionsType(bool reportErrors)
    {
        if (deferredGlobalImportCallOptionsType.IsNone)
        {
            var t = GetGlobalType("ImportCallOptions", 0, reportErrors);
            if (t.IsNone)
            {
                return emptyObjectType;
            }
            deferredGlobalImportCallOptionsType = t;
        }
        return deferredGlobalImportCallOptionsType;
    }

    // ---- signature resolution ----

    /// <summary>checker.ts getResolvedSignature.</summary>
    internal Signature GetResolvedSignature(NodeRef node, List<Signature>? candidatesOutArray = null, CheckMode checkMode = CheckMode.Normal)
    {
        var cached = SignatureLinks.Has(node) ? SignatureLinks.Get(node).ResolvedSignature : Signature.None;
        // If getResolvedSignature has already been called, we will have cached the resolvedSignature.
        // However, it is possible that either candidatesOutArray was not passed in the first time,
        // or that a different candidatesOutArray was passed in. Therefore, we need to redo the work
        // to correctly fill the candidatesOutArray.
        if (!cached.IsNone && cached != resolvingSignature && candidatesOutArray is null)
        {
            return cached;
        }
        var saveResolutionStart = resolutionStart;
        if (cached.IsNone)
        {
            // If we haven't already done so, temporarily reset the resolution stack. This allows us to
            // handle "inverted" situations where, for example, an API client asks for the type of a symbol
            // containined in a function call argument whose contextual type depends on the symbol itself
            // through resolution of the containing function call. By resetting the resolution stack we'll
            // retry the symbol type resolution with the resolvingSignature marker in place to suppress
            // the contextual type circularity.
            resolutionStart = resolutionTargets.Count;
        }
        SignatureLinks.Get(node).ResolvedSignature = resolvingSignature;
        var result = ResolveSignature(node, candidatesOutArray, checkMode);
        resolutionStart = saveResolutionStart;
        // When CheckMode.SkipGenericFunctions is set we use resolvingSignature to indicate that call
        // resolution should be deferred.
        if (result != resolvingSignature)
        {
            // if signature resolution originated on a node that itself depends on the contextual type
            // then it's possible that the resolved signature might not be the same as the one that would be computed in source order
            // since resolving such signature leads to resolving the potential outer signature, its arguments and thus the very same signature
            // it's possible that this inner resolution sets the resolvedSignature first.
            // In such a case we ignore the local result and reuse the correct one that was cached.
            var current = SignatureLinks.Get(node).ResolvedSignature;
            if (current != resolvingSignature)
            {
                result = current;
            }
            // If signature resolution originated in control flow type analysis (for example to compute the
            // assigned type in a flow assignment) we don't cache the result as it may be based on temporary
            // types from the control flow analysis. (module C7)
            SignatureLinks.Get(node).ResolvedSignature = flowLoopStart == flowLoopCount ? result : cached;
        }
        return result;
    }

    // checker.ts getDiagnosticHeadMessageForDecoratorResolution
    private DiagnosticMessage GetDiagnosticHeadMessageForDecoratorResolution(NodeRef node) => Kind(Parent(node)) switch
    {
        SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression => Diagnostics.Unable_to_resolve_signature_of_class_decorator_when_called_as_an_expression,
        SyntaxKind.Parameter => Diagnostics.Unable_to_resolve_signature_of_parameter_decorator_when_called_as_an_expression,
        SyntaxKind.PropertyDeclaration => Diagnostics.Unable_to_resolve_signature_of_property_decorator_when_called_as_an_expression,
        _ => Diagnostics.Unable_to_resolve_signature_of_method_decorator_when_called_as_an_expression,
    };

    // checker.ts resolveDecorator
    private Signature ResolveDecorator(NodeRef node, List<Signature>? candidatesOutArray, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var expression = At(node, store.Expression(node.Node));
        var funcType = CheckExpression(expression);
        var apparentType = GetApparentType(funcType);
        if (IsErrorType(apparentType))
        {
            return ResolveErrorCall(node);
        }
        var callSignatures = GetSignaturesOfType(apparentType, SignatureKind.Call);
        var numConstructSignatures = GetSignaturesOfType(apparentType, SignatureKind.Construct).Count;
        if (IsUntypedFunctionCall(funcType, apparentType, callSignatures.Count, numConstructSignatures))
        {
            return ResolveUntypedCall(node);
        }
        if (IsPotentiallyUncalledDecorator(node, callSignatures) && store.Kind(expression.Node) != SyntaxKind.ParenthesizedExpression)
        {
            var nodeStr = GetTextOfNode(expression);
            Error(node, Diagnostics._0_accepts_too_few_arguments_to_be_used_as_a_decorator_here_Did_you_mean_to_call_it_first_and_write_0, nodeStr);
            return ResolveErrorCall(node);
        }
        var headMessage = GetDiagnosticHeadMessageForDecoratorResolution(node);
        if (callSignatures.Count == 0)
        {
            var (errorChain, relatedMessage) = InvocationErrorDetails(expression, apparentType, SignatureKind.Call);
            var messageChain = ChainDiagnosticMessages(errorChain, headMessage);
            var diag = CreateDiagnosticForNodeFromMessageChain(expression, messageChain);
            if (relatedMessage is not null)
            {
                AddRelatedInfo(diag, CreateRelatedForNode(expression, relatedMessage));
            }
            diagnostics.Add(diag);
            InvocationErrorRecovery(apparentType, SignatureKind.Call, diag);
            return ResolveErrorCall(node);
        }
        return ResolveCall(node, callSignatures, candidatesOutArray, checkMode, SignatureFlags.None, headMessage);
    }

    // checker.ts isPotentiallyUncalledDecorator
    private bool IsPotentiallyUncalledDecorator(NodeRef decorator, Items<Signature> signatures) =>
        signatures.Count > 0 && signatures.TrueForAll(signature =>
            Signatures.Record(signature).MinArgumentCount == 0 &&
            !SignatureHasRestParameter(signature) &&
            ParametersOf(signature).Length < GetDecoratorArgumentCount(decorator, signature));

    // checker.ts resolveSignature
    private Signature ResolveSignature(NodeRef node, List<Signature>? candidatesOutArray, CheckMode checkMode) => Kind(node) switch
    {
        SyntaxKind.CallExpression => ResolveCallExpression(node, candidatesOutArray, checkMode),
        SyntaxKind.NewExpression => ResolveNewExpression(node, candidatesOutArray, checkMode),
        SyntaxKind.TaggedTemplateExpression => ResolveTaggedTemplateExpression(node, candidatesOutArray, checkMode),
        SyntaxKind.Decorator => ResolveDecorator(node, candidatesOutArray, checkMode),
        SyntaxKind.BinaryExpression => ResolveInstanceofExpression(node, candidatesOutArray, checkMode),
        SyntaxKind.JsxOpeningFragment or SyntaxKind.JsxOpeningElement or SyntaxKind.JsxSelfClosingElement => ResolveJsxOpeningLikeElement(node, candidatesOutArray, checkMode),
        _ => ResolveUntypedCall(node),
    };

    // checker.ts resolveUntypedCall
    private Signature ResolveUntypedCall(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (store.Kind(n) is SyntaxKind.CallExpression or SyntaxKind.NewExpression or SyntaxKind.TaggedTemplateExpression or SyntaxKind.JsxOpeningElement or SyntaxKind.JsxSelfClosingElement)
        {
            // Check type arguments even though we will give an error that untyped calls may not accept type arguments.
            // This gets us diagnostics for the type arguments and marks them as referenced.
            CheckSourceElements(node, store.TypeArguments(n));
        }
        if (store.Kind(n) == SyntaxKind.TaggedTemplateExpression)
        {
            CheckExpression(At(node, store.Template(n)));
        }
        else if (IsJsxOpeningLikeElement(store, n))
        {
            CheckExpression(At(node, store.Attributes(n)));
        }
        else if (store.Kind(n) == SyntaxKind.BinaryExpression)
        {
            CheckExpression(At(node, store.Left(n)));
        }
        else if (store.Kind(n) is SyntaxKind.CallExpression or SyntaxKind.NewExpression)
        {
            var arguments = store.Arguments(n);
            if (!arguments.IsNone)
            {
                foreach (var argument in store.Nodes(arguments))
                {
                    CheckExpression(At(node, argument));
                }
            }
        }
        return anySignature;
    }

    // checker.ts resolveErrorCall
    private Signature ResolveErrorCall(NodeRef node)
    {
        ResolveUntypedCall(node);
        return unknownSignature;
    }

    // checker.ts resolveCallExpression
    private Signature ResolveCallExpression(NodeRef node, List<Signature>? candidatesOutArray, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var expression = At(node, store.Expression(n));
        if (store.Kind(expression.Node) == SyntaxKind.SuperKeyword)
        {
            var superType = CheckSuperExpression(expression);
            if ((Types.Flags(superType) & TypeFlags.Any) != 0)
            {
                var arguments = store.Arguments(n);
                foreach (var arg in store.Nodes(arguments))
                {
                    CheckExpression(At(node, arg)); // Still visit arguments so they get marked for visibility, etc
                }
                return anySignature;
            }
            if (!IsErrorType(superType))
            {
                // In super call, the candidate signatures are the matching arity signatures of the base constructor function instantiated
                // with the type arguments specified in the extends clause.
                var baseTypeNode = GetBaseTypeNodeOfClass(At(node, Utilities.GetContainingClass(store, n)));
                if (!baseTypeNode.IsNone)
                {
                    var baseConstructors = GetInstantiatedConstructorsForTypeArguments(superType, baseTypeNode);
                    return ResolveCall(node, baseConstructors, candidatesOutArray, checkMode, SignatureFlags.None);
                }
            }
            return ResolveUntypedCall(node);
        }
        SignatureFlags callChainFlags;
        var funcType = CheckExpression(expression);
        if (UtilitiesPublic.IsCallChain(store, n))
        {
            var nonOptionalType = GetOptionalExpressionType(funcType, expression);
            callChainFlags = nonOptionalType == funcType ? SignatureFlags.None
                : Utilities.IsOutermostOptionalChain(store, n) ? SignatureFlags.IsOuterCallChain
                : SignatureFlags.IsInnerCallChain;
            funcType = nonOptionalType;
        }
        else
        {
            callChainFlags = SignatureFlags.None;
        }
        funcType = CheckNonNullTypeWithReporter(funcType, expression, ReportCannotInvokePossiblyNullOrUndefinedError);
        if (funcType == silentNeverType)
        {
            return silentNeverSignature;
        }
        var apparentType = GetApparentType(funcType);
        if (IsErrorType(apparentType))
        {
            // Another error has already been reported
            return ResolveErrorCall(node);
        }
        // Technically, this signatures list may be incomplete. We are taking the apparent type,
        // but we are not including call signatures that may have been added to the Object or
        // Function interface, since they have none by default. This is a bit of a leap of faith
        // that the user will not add any.
        var callSignatures = GetSignaturesOfType(apparentType, SignatureKind.Call);
        var numConstructSignatures = GetSignaturesOfType(apparentType, SignatureKind.Construct).Count;
        // TS 1.0 Spec: 4.12
        // In an untyped function call no TypeArgs are permitted, Args can be any argument list, no contextual
        // types are provided for the argument expressions, and the result is always of type Any.
        if (IsUntypedFunctionCall(funcType, apparentType, callSignatures.Count, numConstructSignatures))
        {
            // The unknownType indicates that an error already occurred (and was reported).  No
            // need to report another error in this case.
            if (!IsErrorType(funcType) && !store.TypeArguments(n).IsNone)
            {
                Error(node, Diagnostics.Untyped_function_calls_may_not_accept_type_arguments);
            }
            return ResolveUntypedCall(node);
        }
        // If FuncExpr's apparent type(section 3.8.1) is a function type, the call is a typed function call.
        // TypeScript employs overload resolution in typed function calls in order to support functions
        // with multiple call signatures.
        if (callSignatures.Count == 0)
        {
            if (numConstructSignatures > 0)
            {
                Error(node, Diagnostics.Value_of_type_0_is_not_callable_Did_you_mean_to_include_new, TypeToString(funcType));
            }
            else
            {
                ProgramRelatedInformation? relatedInformation = null;
                if (store.Length(store.Arguments(n)) == 1)
                {
                    var text = store.SourceFileText(Utilities.RootOf(store, n)).Chars;
                    var pos = Scanner.SkipTrivia(text, store.End(expression.Node), stopAfterLineBreak: true) - 1;
                    if (pos >= 0 && pos < text.Length && Scanner.IsLineBreak(text[pos]))
                    {
                        relatedInformation = CreateRelatedForNode(expression, Diagnostics.Are_you_missing_a_semicolon);
                    }
                }
                InvocationError(expression, apparentType, SignatureKind.Call, relatedInformation);
            }
            return ResolveErrorCall(node);
        }
        // When a call to a generic function is an argument to an outer call to a generic function for which
        // inference is in process, we have a choice to make. If the inner call relies on inferences made from
        // its contextual type to its return type, deferring the inner call processing allows the best possible
        // contextual type to accumulate. But if the outer call relies on inferences made from the return type of
        // the inner call, the inner call should be processed early. There's no sure way to know which choice is
        // right (only a full unification algorithm can determine that), so we resort to the following heuristic:
        // If no type arguments are specified in the inner call and at least one call signature is generic and
        // returns a function type, we choose to defer processing. This narrowly permits function composition
        // operators to flow inferences through return types, but otherwise processes calls right away. We
        // use the resolvingSignature singleton to indicate that we deferred processing. This result will be
        // propagated out and eventually turned into silentNeverType (a type that is assignable to anything and
        // from which we never make inferences).
        if ((checkMode & CheckMode.SkipGenericFunctions) != 0 && store.TypeArguments(n).IsNone && callSignatures.Exists(IsGenericFunctionReturningFunction))
        {
            SkippedGenericFunction(node, checkMode);
            return resolvingSignature;
        }
        // If the function is explicitly marked with `@class`, then it must be constructed.
        if (callSignatures.Exists(sig =>
            {
                var declaration = Signatures.Record(sig).Declaration;
                return !declaration.IsNone && Utilities.IsInJSFile(StoreOf(declaration), declaration.Node) &&
                    !GetFirstJSDocTagOfKind(StoreOf(declaration), declaration.Node, SyntaxKind.JSDocClassTag).IsNone;
            }))
        {
            Error(node, Diagnostics.Value_of_type_0_is_not_callable_Did_you_mean_to_include_new, TypeToString(funcType));
            return ResolveErrorCall(node);
        }
        return ResolveCall(node, callSignatures, candidatesOutArray, checkMode, callChainFlags);
    }

    // checker.ts isGenericFunctionReturningFunction
    private bool IsGenericFunctionReturningFunction(Signature signature) =>
        Signatures.Record(signature).TypeParameters.Count > 0 && IsFunctionType(GetReturnTypeOfSignature(signature));

    // checker.ts isFunctionType
    private bool IsFunctionType(Type type) =>
        (Types.Flags(type) & TypeFlags.Object) != 0 && GetSignaturesOfType(type, SignatureKind.Call).Count > 0;

    // checker.ts isUntypedFunctionCall
    private bool IsUntypedFunctionCall(Type funcType, Type apparentFuncType, int numCallSignatures, int numConstructSignatures)
    {
        // We exclude union types because we may have a union of function types that happen to have no common signatures.
        return (Types.Flags(funcType) & TypeFlags.Any) != 0 ||
            ((Types.Flags(apparentFuncType) & TypeFlags.Any) != 0 && (Types.Flags(funcType) & TypeFlags.TypeParameter) != 0) ||
            (numCallSignatures == 0 && numConstructSignatures == 0 && (Types.Flags(apparentFuncType) & TypeFlags.Union) == 0 &&
             (Types.Flags(GetReducedType(apparentFuncType)) & TypeFlags.Never) == 0 && IsTypeAssignableTo(funcType, globalFunctionType));
    }

    // checker.ts resolveNewExpression
    private Signature ResolveNewExpression(NodeRef node, List<Signature>? candidatesOutArray, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var expression = At(node, store.Expression(n));
        var expressionType = CheckNonNullExpression(expression);
        if (expressionType == silentNeverType)
        {
            return silentNeverSignature;
        }
        // If expressionType's apparent type(section 3.8.1) is an object type with one or
        // more construct signatures, the expression is processed in the same manner as a
        // function call, but using the construct signatures as the initial set of candidate
        // signatures for overload resolution. The result type of the function call becomes
        // the result type of the operation.
        expressionType = GetApparentType(expressionType);
        if (IsErrorType(expressionType))
        {
            // Another error has already been reported
            return ResolveErrorCall(node);
        }
        // TS 1.0 spec: 4.11
        // If expressionType is of type Any, Args can be any argument
        // list and the result of the operation is of type Any.
        if ((Types.Flags(expressionType) & TypeFlags.Any) != 0)
        {
            if (!store.TypeArguments(n).IsNone)
            {
                Error(node, Diagnostics.Untyped_function_calls_may_not_accept_type_arguments);
            }
            return ResolveUntypedCall(node);
        }
        // Technically, this signatures list may be incomplete. We are taking the apparent type,
        // but we are not including construct signatures that may have been added to the Object or
        // Function interface, since they have none by default. This is a bit of a leap of faith
        // that the user will not add any.
        var constructSignatures = GetSignaturesOfType(expressionType, SignatureKind.Construct);
        if (constructSignatures.Count > 0)
        {
            if (!IsConstructorAccessible(node, constructSignatures[0]))
            {
                return ResolveErrorCall(node);
            }
            // If the expression is a class of abstract type, or an abstract construct signature,
            // then it cannot be instantiated.
            // In the case of a merged class-module or class-interface declaration,
            // only the class declaration node will have the Abstract flag set.
            if (SomeSignature(constructSignatures, signature => (Signatures.Record(signature).Flags & SignatureFlags.Abstract) != 0))
            {
                Error(node, Diagnostics.Cannot_create_an_instance_of_an_abstract_class);
                return ResolveErrorCall(node);
            }
            var valueDecl = Types.Symbol(expressionType).IsNone ? NodeRef.None : GetClassLikeDeclarationOfSymbol(Types.Symbol(expressionType));
            if (!valueDecl.IsNone && Utilities.HasSyntacticModifierUncached(StoreOf(valueDecl), valueDecl.Node, ModifierFlags.Abstract))
            {
                Error(node, Diagnostics.Cannot_create_an_instance_of_an_abstract_class);
                return ResolveErrorCall(node);
            }
            return ResolveCall(node, constructSignatures, candidatesOutArray, checkMode, SignatureFlags.None);
        }
        // If expressionType's apparent type is an object type with no construct signatures but
        // one or more call signatures, the expression is processed as a function call. A compile-time
        // error occurs if the result of the function call is not Void. The type of the result of the
        // operation is Any. It is an error to have a Void this type.
        var callSignatures = GetSignaturesOfType(expressionType, SignatureKind.Call);
        if (callSignatures.Count > 0)
        {
            var signature = ResolveCall(node, callSignatures, candidatesOutArray, checkMode, SignatureFlags.None);
            if (!noImplicitAny)
            {
                var declaration = Signatures.Record(signature).Declaration;
                if (!declaration.IsNone && !IsJSConstructor(declaration) && GetReturnTypeOfSignature(signature) != voidType)
                {
                    Error(node, Diagnostics.Only_a_void_function_can_be_called_with_the_new_keyword);
                }
                if (GetThisTypeOfSignature(signature) == voidType)
                {
                    Error(node, Diagnostics.A_function_that_is_called_with_the_new_keyword_cannot_have_a_this_type_that_is_void);
                }
            }
            return signature;
        }
        InvocationError(expression, expressionType, SignatureKind.Construct);
        return ResolveErrorCall(node);
    }

    // checker.ts someSignature
    private bool SomeSignature(Items<Signature> signatures, Func<Signature, bool> f)
    {
        foreach (var signature in signatures)
        {
            var record = Signatures.Record(signature);
            if (record.CompositeKind == TypeFlags.Union && record.CompositeSignatures is not null ? Array.Exists(record.CompositeSignatures, s => f(s)) : f(signature))
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts typeHasProtectedAccessibleBase
    private bool TypeHasProtectedAccessibleBase(SymbolRef target, Type type)
    {
        var baseTypes = GetBaseTypes(type);
        if (baseTypes.Count == 0)
        {
            return false;
        }
        var firstBase = baseTypes[0];
        if ((Types.Flags(firstBase) & TypeFlags.Intersection) != 0)
        {
            var types = Types.Elements(Types.Union(firstBase).Types).ToArray();
            var mixinFlags = FindMixins(types);
            for (var i = 0; i < types.Length; i++)
            {
                var intersectionMember = types[i];
                // We want to ignore mixin ctors
                if (!mixinFlags[i] && (Types.ObjectFlagsOf(intersectionMember) & (ObjectFlags.Class | ObjectFlags.Interface)) != 0)
                {
                    if (Types.Symbol(intersectionMember) == target)
                    {
                        return true;
                    }
                    if (TypeHasProtectedAccessibleBase(target, intersectionMember))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        if (Types.Symbol(firstBase) == target)
        {
            return true;
        }
        return TypeHasProtectedAccessibleBase(target, firstBase);
    }

    // checker.ts isConstructorAccessible
    private bool IsConstructorAccessible(NodeRef node, Signature signature)
    {
        var declaration = Signatures.Record(signature).Declaration;
        if (declaration.IsNone)
        {
            return true;
        }
        var dstore = StoreOf(declaration);
        var modifiers = GetCombinedEffectiveModifierFlags(dstore, declaration.Node) & ModifierFlags.NonPublicAccessibilityModifier;
        // (1) Public constructors and (2) constructor functions are always accessible.
        if (modifiers == ModifierFlags.None || dstore.Kind(declaration.Node) != SyntaxKind.Constructor)
        {
            return true;
        }
        var classSymbol = NodeSymbol(Parent(declaration));
        var declaringClassDeclaration = GetClassLikeDeclarationOfSymbol(classSymbol);
        var declaringClass = GetDeclaredTypeOfSymbol(classSymbol);
        // A private or protected constructor can only be instantiated within its own class (or a subclass, for protected)
        if (!IsNodeWithinClass(node, declaringClassDeclaration))
        {
            var containingClass = Utilities.GetContainingClass(StoreOf(node), node.Node);
            if (!containingClass.IsNone && (modifiers & ModifierFlags.Protected) != 0)
            {
                var containingType = GetTypeOfNode(At(node, containingClass));
                if (TypeHasProtectedAccessibleBase(classSymbol, containingType))
                {
                    return true;
                }
            }
            if ((modifiers & ModifierFlags.Private) != 0)
            {
                Error(node, Diagnostics.Constructor_of_class_0_is_private_and_only_accessible_within_the_class_declaration, TypeToString(declaringClass));
            }
            if ((modifiers & ModifierFlags.Protected) != 0)
            {
                Error(node, Diagnostics.Constructor_of_class_0_is_protected_and_only_accessible_within_the_class_declaration, TypeToString(declaringClass));
            }
            return false;
        }
        return true;
    }

    // checker.ts invocationErrorDetails
    private (DiagnosticMessageChain MessageChain, DiagnosticMessage? RelatedMessage) InvocationErrorDetails(NodeRef errorTarget, Type apparentType, SignatureKind kind)
    {
        DiagnosticMessageChain? errorInfo = null;
        var isCall = kind == SignatureKind.Call;
        var awaitedType = GetAwaitedType(apparentType);
        var maybeMissingAwait = !awaitedType.IsNone && GetSignaturesOfType(awaitedType, kind).Count > 0;
        if ((Types.Flags(apparentType) & TypeFlags.Union) != 0)
        {
            var types = Types.Elements(Types.Union(apparentType).Types).ToArray();
            var hasSignatures = false;
            foreach (var constituent in types)
            {
                var signatures = GetSignaturesOfType(constituent, kind);
                if (signatures.Count != 0)
                {
                    hasSignatures = true;
                    if (errorInfo is not null)
                    {
                        // Bail early if we already have an error, no chance of "No constituent of type is callable"
                        break;
                    }
                }
                else
                {
                    // Error on the first non callable constituent only
                    if (errorInfo is null)
                    {
                        errorInfo = ChainDiagnosticMessages(errorInfo, isCall ? Diagnostics.Type_0_has_no_call_signatures : Diagnostics.Type_0_has_no_construct_signatures, TypeToString(constituent));
                        errorInfo = ChainDiagnosticMessages(errorInfo, isCall ? Diagnostics.Not_all_constituents_of_type_0_are_callable : Diagnostics.Not_all_constituents_of_type_0_are_constructable, TypeToString(apparentType));
                    }
                    if (hasSignatures)
                    {
                        // Bail early if we already found a siganture, no chance of "No constituent of type is callable"
                        break;
                    }
                }
            }
            if (!hasSignatures)
            {
                errorInfo = ChainDiagnosticMessages(null, isCall ? Diagnostics.No_constituent_of_type_0_is_callable : Diagnostics.No_constituent_of_type_0_is_constructable, TypeToString(apparentType));
            }
            errorInfo ??= ChainDiagnosticMessages(errorInfo, isCall
                ? Diagnostics.Each_member_of_the_union_type_0_has_signatures_but_none_of_those_signatures_are_compatible_with_each_other
                : Diagnostics.Each_member_of_the_union_type_0_has_construct_signatures_but_none_of_those_signatures_are_compatible_with_each_other, TypeToString(apparentType));
        }
        else
        {
            errorInfo = ChainDiagnosticMessages(errorInfo, isCall ? Diagnostics.Type_0_has_no_call_signatures : Diagnostics.Type_0_has_no_construct_signatures, TypeToString(apparentType));
        }
        var headMessage = isCall ? Diagnostics.This_expression_is_not_callable : Diagnostics.This_expression_is_not_constructable;
        // Diagnose get accessors incorrectly called as functions
        var store = StoreOf(errorTarget);
        var parent = store.Parent(errorTarget.Node);
        if (store.Kind(parent) == SyntaxKind.CallExpression && store.Length(store.Arguments(parent)) == 0)
        {
            var resolvedSymbol = SymbolNodeLinks.Has(errorTarget) ? SymbolNodeLinks.Get(errorTarget).ResolvedSymbol : SymbolRef.None;
            if (!resolvedSymbol.IsNone && (SymbolFlagsOf(resolvedSymbol) & SymbolFlags.GetAccessor) != 0)
            {
                headMessage = Diagnostics.This_expression_is_not_callable_because_it_is_a_get_accessor_Did_you_mean_to_use_it_without;
            }
        }
        return (ChainDiagnosticMessages(errorInfo, headMessage), maybeMissingAwait ? Diagnostics.Did_you_forget_to_use_await : null);
    }

    // checker.ts invocationErrorRecovery
    private void InvocationErrorRecovery(Type apparentType, SignatureKind kind, ProgramDiagnostic diagnostic)
    {
        var symbol = Types.Symbol(apparentType);
        if (symbol.IsNone || !ExportTypeLinks.Has(symbol))
        {
            return;
        }
        var importNode = ExportTypeLinks.Get(symbol).OriginatingImport;
        // Create a diagnostic on the originating import if possible onto which we can attach a quickfix
        //  An import call expression cannot be rewritten into another form to correct the error - the only solution is to use `.default` at the use-site
        if (!importNode.IsNone && !Utilities.IsImportCall(StoreOf(importNode), importNode.Node))
        {
            var sigs = GetSignaturesOfType(GetTypeOfSymbol(ExportTypeLinks.Get(symbol).Target), kind);
            if (sigs.Count == 0)
            {
                return;
            }
            AddRelatedInfo(diagnostic, CreateRelatedForNode(importNode, Diagnostics.Type_originates_at_this_import_A_namespace_style_import_cannot_be_called_or_constructed_and_will_cause_a_failure_at_runtime_Consider_using_a_default_import_or_import_require_here_instead));
        }
    }

    // checker.ts invocationError
    private void InvocationError(NodeRef errorTarget, Type apparentType, SignatureKind kind, ProgramRelatedInformation? relatedInformation = null)
    {
        var (messageChain, relatedInfo) = InvocationErrorDetails(errorTarget, apparentType, kind);
        var diagnostic = CreateDiagnosticForNodeFromMessageChain(errorTarget, messageChain);
        if (relatedInfo is not null)
        {
            AddRelatedInfo(diagnostic, CreateRelatedForNode(errorTarget, relatedInfo));
        }
        var store = StoreOf(errorTarget);
        var parent = store.Parent(errorTarget.Node);
        if (store.Kind(parent) == SyntaxKind.CallExpression)
        {
            var (start, length) = GetDiagnosticSpanForCallNode(At(errorTarget, parent));
            diagnostic.StartOrUndefined = start;
            diagnostic.LengthOrUndefined = length;
        }
        diagnostics.Add(diagnostic);
        if (relatedInformation is not null)
        {
            AddRelatedInfo(diagnostic, relatedInformation);
        }
        InvocationErrorRecovery(apparentType, kind, diagnostic);
    }

    // checker.ts getDiagnosticSpanForCallNode
    private (int Start, int Length) GetDiagnosticSpanForCallNode(NodeRef node)
    {
        var store = StoreOf(node);
        var expression = store.Expression(node.Node);
        var target = store.Kind(expression) == SyntaxKind.PropertyAccessExpression ? store.Name(expression) : expression;
        var span = Utilities.GetErrorSpanForNode(store, Utilities.RootOf(store, node.Node), target);
        return (span.Start, span.Length);
    }

    // checker.ts getDiagnosticForCallNode
    private ProgramDiagnostic GetDiagnosticForCallNode(NodeRef node, DiagnosticMessageChain chain)
    {
        if (Kind(node) == SyntaxKind.CallExpression)
        {
            var (start, length) = GetDiagnosticSpanForCallNode(node);
            return CreateFileDiagnosticFromMessageChain(node.File, start, length, chain);
        }
        return CreateDiagnosticForNodeFromMessageChain(node, chain);
    }

    // checker.ts getErrorNodeForCallNode
    private NodeRef GetErrorNodeForCallNode(NodeRef callLike)
    {
        var store = StoreOf(callLike);
        switch (store.Kind(callLike.Node))
        {
            case SyntaxKind.CallExpression:
            case SyntaxKind.NewExpression:
            {
                var expression = store.Expression(callLike.Node);
                return At(callLike, store.Kind(expression) == SyntaxKind.PropertyAccessExpression ? store.Name(expression) : expression);
            }
            case SyntaxKind.TaggedTemplateExpression:
            {
                var tag = store.Tag(callLike.Node);
                return At(callLike, store.Kind(tag) == SyntaxKind.PropertyAccessExpression ? store.Name(tag) : tag);
            }
            case SyntaxKind.JsxOpeningElement:
            case SyntaxKind.JsxSelfClosingElement:
                return At(callLike, store.TagName(callLike.Node));
        }
        return callLike;
    }

    // checker.ts resolveTaggedTemplateExpression
    private Signature ResolveTaggedTemplateExpression(NodeRef node, List<Signature>? candidatesOutArray, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var tag = At(node, store.Tag(node.Node));
        var tagType = CheckExpression(tag);
        var apparentType = GetApparentType(tagType);
        if (IsErrorType(apparentType))
        {
            // Another error has already been reported
            return ResolveErrorCall(node);
        }
        var callSignatures = GetSignaturesOfType(apparentType, SignatureKind.Call);
        var numConstructSignatures = GetSignaturesOfType(apparentType, SignatureKind.Construct).Count;
        if (IsUntypedFunctionCall(tagType, apparentType, callSignatures.Count, numConstructSignatures))
        {
            return ResolveUntypedCall(node);
        }
        if (callSignatures.Count == 0)
        {
            if (store.Kind(store.Parent(node.Node)) == SyntaxKind.ArrayLiteralExpression)
            {
                diagnostics.Add(CreateDiagnosticForNode(tag, Diagnostics.It_is_likely_that_you_are_missing_a_comma_to_separate_these_two_template_expressions_They_form_a_tagged_template_expression_which_cannot_be_invoked));
                return ResolveErrorCall(node);
            }
            InvocationError(tag, apparentType, SignatureKind.Call);
            return ResolveErrorCall(node);
        }
        return ResolveCall(node, callSignatures, candidatesOutArray, checkMode, SignatureFlags.None);
    }

    // checker.ts resolveInstanceofExpression
    private Signature ResolveInstanceofExpression(NodeRef node, List<Signature>? candidatesOutArray, CheckMode checkMode)
    {
        // if rightType is an object type with a custom `[Symbol.hasInstance]` method, then it is potentially
        // valid on the right-hand side of the `instanceof` operator. This allows normal `object` types to
        // participate in `instanceof`, as per Step 2 of https://tc39.es/ecma262/#sec-instanceofoperator.
        var store = StoreOf(node);
        var right = At(node, store.Right(node.Node));
        var rightType = CheckExpression(right);
        if ((Types.Flags(rightType) & TypeFlags.Any) == 0)
        {
            var hasInstanceMethodType = GetSymbolHasInstanceMethodOfObjectType(rightType);
            if (!hasInstanceMethodType.IsNone)
            {
                var apparentType = GetApparentType(hasInstanceMethodType);
                if (IsErrorType(apparentType))
                {
                    return ResolveErrorCall(node);
                }
                var callSignatures = GetSignaturesOfType(apparentType, SignatureKind.Call);
                var constructSignatures = GetSignaturesOfType(apparentType, SignatureKind.Construct);
                if (IsUntypedFunctionCall(hasInstanceMethodType, apparentType, callSignatures.Count, constructSignatures.Count))
                {
                    return ResolveUntypedCall(node);
                }
                if (callSignatures.Count > 0)
                {
                    return ResolveCall(node, callSignatures, candidatesOutArray, checkMode, SignatureFlags.None);
                }
            }
            // NOTE: do not raise error if right is unknown as related error was already reported
            else if (!(TypeHasCallOrConstructSignatures(rightType) || IsTypeSubtypeOf(rightType, globalFunctionType)))
            {
                Error(right, Diagnostics.The_right_hand_side_of_an_instanceof_expression_must_be_either_of_type_any_a_class_function_or_other_type_assignable_to_the_Function_interface_type_or_an_object_type_with_a_Symbol_hasInstance_method);
                return ResolveErrorCall(node);
            }
        }
        // fall back to a default signature
        return anySignature;
    }

    // checker.ts getSymbolHasInstanceMethodOfObjectType
    private Type GetSymbolHasInstanceMethodOfObjectType(Type type)
    {
        var hasInstancePropertyName = GetPropertyNameForKnownSymbolName("hasInstance");
        if (AllTypesAssignableToKind(type, TypeFlags.NonPrimitive))
        {
            var hasInstanceProperty = GetPropertyOfType(type, hasInstancePropertyName);
            if (!hasInstanceProperty.IsNone)
            {
                var hasInstancePropertyType = GetTypeOfSymbol(hasInstanceProperty);
                if (!hasInstancePropertyType.IsNone && GetSignaturesOfType(hasInstancePropertyType, SignatureKind.Call).Count != 0)
                {
                    return hasInstancePropertyType;
                }
            }
        }
        return Type.None;
    }

    // checker.ts getPropertyNameForKnownSymbolName
    private string GetPropertyNameForKnownSymbolName(string symbolName)
    {
        var ctorType = GetGlobalESSymbolConstructorSymbol();
        var uniqueType = ctorType.IsNone ? Type.None : GetTypeOfPropertyOfType(GetTypeOfSymbol(ctorType), Utilities.EscapeLeadingUnderscores(symbolName));
        return !uniqueType.IsNone && IsTypeUsableAsPropertyName(uniqueType) ? GetPropertyNameFromType(uniqueType) : "__@" + symbolName;
    }

    // checker.ts getGlobalESSymbolConstructorSymbol (getGlobalValueSymbol("Symbol", false))
    private SymbolRef deferredGlobalESSymbolConstructorSymbol;

    private SymbolRef GetGlobalESSymbolConstructorSymbol()
    {
        if (deferredGlobalESSymbolConstructorSymbol.IsNone)
        {
            var symbol = GetGlobalSymbol("Symbol", SymbolFlags.Value, null);
            deferredGlobalESSymbolConstructorSymbol = symbol.IsNone ? unknownSymbol : symbol;
        }
        return deferredGlobalESSymbolConstructorSymbol == unknownSymbol ? SymbolRef.None : deferredGlobalESSymbolConstructorSymbol;
    }

    // checker.ts getGlobalESSymbolConstructorTypeSymbol
    private SymbolRef GetGlobalESSymbolConstructorTypeSymbol() => GetGlobalTypeSymbol("SymbolConstructor", reportErrors: false);

    /// <summary>checker.ts allTypesAssignableToKind.</summary>
    internal bool AllTypesAssignableToKind(Type source, TypeFlags kind, bool strict = false) =>
        (Types.Flags(source) & TypeFlags.Union) != 0
            ? Array.TrueForAll(Types.Elements(Types.Union(source).Types).ToArray(), t => AllTypesAssignableToKind(t, kind, strict))
            : IsTypeAssignableToKind(source, kind, strict);

    // ---- arity ----

    // checker.ts reorderCandidates
    private void ReorderCandidates(Items<Signature> signatures, List<Signature> result, SignatureFlags callChainFlags)
    {
        NodeRef lastParent = NodeRef.None;
        var lastSymbol = SymbolRef.None;
        var cutoffIndex = 0;
        var index = 0;
        var specializedIndex = -1;
        foreach (var signature in signatures)
        {
            var declaration = Signatures.Record(signature).Declaration;
            var symbol = declaration.IsNone ? SymbolRef.None : GetSymbolOfDeclaration(declaration);
            var parent = declaration.IsNone ? NodeRef.None : Parent(declaration);
            if (lastSymbol.IsNone || symbol == lastSymbol)
            {
                if (!lastParent.IsNone && parent == lastParent)
                {
                    index++;
                }
                else
                {
                    lastParent = parent;
                    index = cutoffIndex;
                }
            }
            else
            {
                // current declaration belongs to a different symbol
                // set cutoffIndex so re-orderings in the future won't change result set from 0 to cutoffIndex
                index = cutoffIndex = result.Count;
                lastParent = parent;
            }
            lastSymbol = symbol;
            // specialized signatures always need to be placed before non-specialized signatures regardless
            // of the cutoff position; see GH#1133
            int spliceIndex;
            if (SignatureHasLiteralTypes(signature))
            {
                specializedIndex++;
                spliceIndex = specializedIndex;
                // The cutoff index always needs to be greater than or equal to the specialized signature index
                // in order to prevent non-specialized signatures from being added before a specialized
                // signature.
                cutoffIndex++;
            }
            else
            {
                spliceIndex = index;
            }
            result.Insert(Math.Min(spliceIndex, result.Count), callChainFlags != SignatureFlags.None ? GetOptionalCallSignature(signature, callChainFlags) : signature);
        }
    }

    // checker.ts hasCorrectArity
    private bool HasCorrectArity(NodeRef node, List<CallArg> args, Signature signature, bool signatureHelpTrailingComma = false)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (store.Kind(n) == SyntaxKind.JsxOpeningFragment)
        {
            return true;
        }
        int argCount;
        var callIsIncomplete = false; // In incomplete call we want to be lenient when we have too few arguments
        var effectiveParameterCount = GetParameterCount(signature);
        var effectiveMinimumArguments = GetMinArgumentCount(signature);
        if (store.Kind(n) == SyntaxKind.TaggedTemplateExpression)
        {
            argCount = args.Count;
            var template = store.Template(n);
            if (store.Kind(template) == SyntaxKind.TemplateExpression)
            {
                // If a tagged template expression lacks a tail literal, the call is incomplete.
                // Specifically, a template only can end in a TemplateTail or a Missing literal.
                var spans = store.Nodes(store.TemplateSpans(template));
                var lastSpan = spans[^1]; // we should always have at least one span.
                var literal = store.Literal(lastSpan);
                callIsIncomplete = Utilities.NodeIsMissing(store, literal) || store.IsUnterminated(literal);
            }
            else
            {
                // If the template didn't end in a backtick, or its beginning occurred right prior to EOF,
                // then this might actually turn out to be a TemplateHead in the future;
                // so we consider the call to be incomplete.
                callIsIncomplete = store.IsUnterminated(template);
            }
        }
        else if (store.Kind(n) == SyntaxKind.Decorator)
        {
            argCount = GetDecoratorArgumentCount(node, signature);
        }
        else if (store.Kind(n) == SyntaxKind.BinaryExpression)
        {
            argCount = 1;
        }
        else if (IsJsxOpeningLikeElement(store, n))
        {
            callIsIncomplete = store.End(store.Attributes(n)) == store.End(n);
            if (callIsIncomplete)
            {
                return true;
            }
            argCount = effectiveMinimumArguments == 0 ? args.Count : 1;
            effectiveParameterCount = args.Count == 0 ? effectiveParameterCount : 1; // class may have argumentless ctor functions - still resolve ctor and compare vs props member type
            effectiveMinimumArguments = Math.Min(effectiveMinimumArguments, 1); // sfc may specify context argument - handled by framework and not typechecked
        }
        else if (store.Arguments(n).IsNone)
        {
            // This only happens when we have something of the form: 'new C'
            return GetMinArgumentCount(signature) == 0;
        }
        else
        {
            argCount = signatureHelpTrailingComma ? args.Count + 1 : args.Count;
            // If we are missing the close parenthesis, the call is incomplete.
            callIsIncomplete = store.End(store.Arguments(n)) == store.End(n);
            // If a spread argument is present, check that it corresponds to a rest parameter or at least that it's in the valid range.
            var spreadArgIndex = GetSpreadArgumentIndex(args);
            if (spreadArgIndex >= 0)
            {
                return spreadArgIndex >= GetMinArgumentCount(signature) && (HasEffectiveRestParameter(signature) || spreadArgIndex < GetParameterCount(signature));
            }
        }
        // Too many arguments implies incorrect arity.
        if (!HasEffectiveRestParameter(signature) && argCount > effectiveParameterCount)
        {
            return false;
        }
        // If the call is incomplete, we should skip the lower bound check.
        // JSX signatures can have extra parameters provided by the library which we don't check
        if (callIsIncomplete || argCount >= effectiveMinimumArguments)
        {
            return true;
        }
        for (var i = argCount; i < effectiveMinimumArguments; i++)
        {
            var type = GetTypeAtPosition(signature, i);
            if ((Types.Flags(FilterType(type, Utilities.IsInJSFile(store, n) && !strictNullChecks ? AcceptsVoidUndefinedUnknownOrAny : AcceptsVoid)) & TypeFlags.Never) != 0)
            {
                return false;
            }
        }
        return true;
    }

    // checker.ts acceptsVoidUndefinedUnknownOrAny
    private bool AcceptsVoidUndefinedUnknownOrAny(Type t) => (Types.Flags(t) & (TypeFlags.Void | TypeFlags.Undefined | TypeFlags.Unknown | TypeFlags.Any)) != 0;

    // checker.ts getDecoratorArgumentCount
    private int GetDecoratorArgumentCount(NodeRef node, Signature signature)
    {
        if (compilerOptions.ExperimentalDecorators == true)
        {
            switch (Kind(Parent(node)))
            {
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                    return 1;
                case SyntaxKind.PropertyDeclaration:
                    return HasAccessorModifier(Parent(node)) ? 3 : 2;
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    // For decorators with only two parameters we supply only two arguments
                    return ParametersOf(signature).Length <= 2 ? 2 : 3;
                default:
                    return 3;
            }
        }
        // Allow the runtime to oversupply arguments to an ES decorator as long as there's at least one parameter.
        return Math.Min(Math.Max(GetParameterCount(signature), 1), 2);
    }

    // checker.ts hasCorrectTypeArgumentArity
    private bool HasCorrectTypeArgumentArity(Signature signature, NodeRef node, NodeList typeArguments)
    {
        // If the user supplied type arguments, but the number of type arguments does not match
        // the declared number of type parameters, the call has an incorrect arity.
        var typeParameters = Signatures.Record(signature).TypeParameters;
        var numTypeParameters = typeParameters.Count;
        var minTypeArgumentCount = GetMinTypeArgumentCount(typeParameters);
        if (typeArguments.IsNone)
        {
            return true;
        }
        var count = StoreOf(node).Length(typeArguments);
        return count == 0 || (count >= minTypeArgumentCount && count <= numTypeParameters);
    }

    // ---- applicability ----

    // checker.ts getEffectiveCheckNode
    private CallArg GetEffectiveCheckNode(CallArg argument)
    {
        if (argument.IsSynthetic)
        {
            return argument;
        }
        var store = StoreOf(argument.Node);
        return RealArg(At(argument.Node, SkipOuterExpressions(store, argument.Node.Node, OuterExpressionKinds.Parentheses | OuterExpressionKinds.Satisfies)));
    }

    // the diagnostics of an error reported on a synthetic argument: the synthetic expression's text range
    private void FixSyntheticErrorSpans(CallArg errorArg, ErrorOutputContainer container, int countBefore)
    {
        if (!errorArg.IsSynthetic || container.Errors is null)
        {
            return;
        }
        var store = StoreOf(errorArg.Node);
        var start = Scanner.SkipTrivia(store.SourceFileText(Utilities.RootOf(store, errorArg.Node.Node)).Chars, errorArg.Pos);
        for (var i = countBefore; i < container.Errors.Count; i++)
        {
            container.Errors[i].StartOrUndefined = start;
            container.Errors[i].LengthOrUndefined = errorArg.End - start;
        }
    }

    // checker.ts getSignatureApplicabilityError
    private List<ProgramDiagnostic>? GetSignatureApplicabilityError(NodeRef node, List<CallArg> args, Signature signature, Relation relation, CheckMode checkMode, bool reportErrors,
        Func<DiagnosticMessageChain?>? containingMessageChain)
    {
        var errorOutputContainer = new ErrorOutputContainer { SkipLogging = true };
        var store = StoreOf(node);
        if (IsJsxCallLike(store, node.Node))
        {
            if (!CheckApplicableSignatureForJsxCallLikeElement(node, signature, relation, checkMode, reportErrors, containingMessageChain, errorOutputContainer))
            {
                return errorOutputContainer.Errors ?? [];
            }
            return null;
        }
        var thisType = GetThisTypeOfSignature(signature);
        if (!thisType.IsNone && thisType != voidType &&
            !(store.Kind(node.Node) == SyntaxKind.NewExpression || (store.Kind(node.Node) == SyntaxKind.CallExpression && Utilities.IsSuperProperty(store, store.Expression(node.Node)))))
        {
            // If the called expression is not of the form `x.f` or `x["f"]`, then sourceType = voidType
            // If the signature's 'this' type is voidType, then the check is skipped -- anything is compatible.
            // If the expression is a new expression or super call expression, then the check is skipped.
            var thisArgumentNode = GetThisArgumentOfCall(node);
            var thisArgumentType = GetThisArgumentType(thisArgumentNode);
            var errorNode = reportErrors ? (!thisArgumentNode.IsNone ? thisArgumentNode : node) : NodeRef.None;
            var headMessage = Diagnostics.The_this_context_of_type_0_is_not_assignable_to_method_s_this_of_type_1;
            if (!CheckTypeRelatedTo(thisArgumentType, thisType, relation, errorNode, headMessage, containingMessageChain, errorOutputContainer))
            {
                return errorOutputContainer.Errors ?? [];
            }
        }
        var argHeadMessage = Diagnostics.Argument_of_type_0_is_not_assignable_to_parameter_of_type_1;
        var restType = GetNonArrayRestType(signature);
        var argCount = !restType.IsNone ? Math.Min(GetParameterCount(signature) - 1, args.Count) : args.Count;
        for (var i = 0; i < argCount; i++)
        {
            var arg = args[i];
            if (!IsOmittedArg(arg))
            {
                var paramType = GetTypeAtPosition(signature, i);
                var argType = CheckArgumentWithContextualType(arg, paramType, null, checkMode);
                // If one or more arguments are still excluded (as indicated by CheckMode.SkipContextSensitive),
                // we obtain the regular type of any object literal arguments because we may not have inferred complete
                // parameter types yet and therefore excess property checks may yield false positives (see #17041).
                var checkArgType = (checkMode & CheckMode.SkipContextSensitive) != 0 ? GetRegularTypeOfObjectLiteral(argType) : argType;
                var effectiveCheckArgumentNode = GetEffectiveCheckNode(arg);
                var countBefore = errorOutputContainer.Errors?.Count ?? 0;
                var errorNode = reportErrors ? effectiveCheckArgumentNode.Node : NodeRef.None;
                if (!CheckTypeRelatedToAndOptionallyElaborate(checkArgType, paramType, relation, errorNode, effectiveCheckArgumentNode.IsSynthetic ? NodeRef.None : effectiveCheckArgumentNode.Node,
                    argHeadMessage, containingMessageChain, errorOutputContainer))
                {
                    FixSyntheticErrorSpans(effectiveCheckArgumentNode, errorOutputContainer, countBefore);
                    MaybeAddMissingAwaitInfo(errorNode, checkArgType, paramType);
                    return errorOutputContainer.Errors ?? [];
                }
            }
        }
        if (!restType.IsNone)
        {
            var spreadType = GetSpreadArgumentType(args, argCount, args.Count, restType, null, checkMode);
            var restArgCount = args.Count - argCount;
            CallArg errorArg = default;
            var errorNode = NodeRef.None;
            if (reportErrors)
            {
                if (restArgCount == 0)
                {
                    errorNode = node;
                }
                else if (restArgCount == 1)
                {
                    errorArg = GetEffectiveCheckNode(args[argCount]);
                    errorNode = errorArg.Node;
                }
                else
                {
                    errorArg = new CallArg(args[argCount].Node, spreadType, false, NodeRef.None, ArgPos(args[argCount]), ArgEnd(args[^1]));
                    errorNode = args[argCount].Node;
                }
            }
            var countBefore = errorOutputContainer.Errors?.Count ?? 0;
            if (!CheckTypeRelatedTo(spreadType, restType, relation, errorNode, argHeadMessage, null, errorOutputContainer))
            {
                FixSyntheticErrorSpans(errorArg, errorOutputContainer, countBefore);
                MaybeAddMissingAwaitInfo(errorNode, spreadType, restType);
                return errorOutputContainer.Errors ?? [];
            }
        }
        return null;

        void MaybeAddMissingAwaitInfo(NodeRef errorNode, Type source, Type target)
        {
            if (!errorNode.IsNone && reportErrors && errorOutputContainer.Errors is { Count: > 0 } errors)
            {
                // Bail if target is Promise-like---something else is wrong
                if (!GetAwaitedTypeOfPromise(target).IsNone)
                {
                    return;
                }
                var awaitedTypeOfSource = GetAwaitedTypeOfPromise(source);
                if (!awaitedTypeOfSource.IsNone && IsTypeRelatedTo(awaitedTypeOfSource, target, relation))
                {
                    AddRelatedInfo(errors[0], CreateRelatedForNode(errorNode, Diagnostics.Did_you_forget_to_use_await));
                }
            }
        }
    }

    private int ArgPos(CallArg arg) => arg.IsSynthetic ? arg.Pos : StoreOf(arg.Node).Pos(arg.Node.Node);

    private int ArgEnd(CallArg arg) => arg.IsSynthetic ? arg.End : StoreOf(arg.Node).End(arg.Node.Node);

    // checker.ts getThisArgumentOfCall
    private NodeRef GetThisArgumentOfCall(NodeRef node)
    {
        var store = StoreOf(node);
        if (store.Kind(node.Node) == SyntaxKind.BinaryExpression)
        {
            return At(node, store.Right(node.Node));
        }
        var expression = store.Kind(node.Node) switch
        {
            SyntaxKind.CallExpression => store.Expression(node.Node),
            SyntaxKind.TaggedTemplateExpression => store.Tag(node.Node),
            SyntaxKind.Decorator when compilerOptions.ExperimentalDecorators != true => store.Expression(node.Node),
            _ => Node.None,
        };
        if (!expression.IsNone)
        {
            var callee = SkipOuterExpressions(store, expression);
            if (Utilities.IsAccessExpression(store, callee))
            {
                return At(node, store.Expression(callee));
            }
        }
        return NodeRef.None;
    }

    // checker.ts getThisArgumentType
    private Type GetThisArgumentType(NodeRef thisArgumentNode)
    {
        if (thisArgumentNode.IsNone)
        {
            return voidType;
        }
        var thisArgumentType = CheckExpression(thisArgumentNode);
        var store = StoreOf(thisArgumentNode);
        var parent = store.Parent(thisArgumentNode.Node);
        return IsRightSideOfInstanceofExpression(store, thisArgumentNode.Node) ? thisArgumentType
            : Utilities.IsOptionalChainRoot(store, parent) ? GetNonNullableType(thisArgumentType)
            : UtilitiesPublic.IsOptionalChain(store, parent) ? RemoveOptionalTypeMarker(thisArgumentType)
            : thisArgumentType;
    }

    // utilities.ts isRightSideOfInstanceofExpression
    private static bool IsRightSideOfInstanceofExpression(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        return store.Kind(parent) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(parent)) == SyntaxKind.InstanceOfKeyword && store.Right(parent) == node;
    }

    // checker.ts inferTypeArguments
    private Type[] InferTypeArguments(NodeRef node, Signature signature, List<CallArg> args, CheckMode checkMode, InferenceContext context)
    {
        var store = StoreOf(node);
        if (IsJsxOpeningLikeElement(store, node.Node))
        {
            return InferJsxTypeArguments(node, signature, checkMode, context);
        }
        var typeParameters = Types.Elements(Signatures.Record(signature).TypeParameters).ToArray();
        // If a contextual type is available, infer from that type to the return type of the call expression. For
        // example, given a 'function wrap<T, U>(cb: (x: T) => U): (x: T) => U' and a call expression
        // 'let f: (x: string) => number = wrap(s => s.length)', we infer from the declared type of 'f' to the
        // return type of 'wrap'.
        if (store.Kind(node.Node) is not (SyntaxKind.Decorator or SyntaxKind.BinaryExpression))
        {
            var skipBindingPatterns = Array.TrueForAll(typeParameters, p => !GetDefaultFromTypeParameter(p).IsNone);
            var contextualType = GetContextualType(node, skipBindingPatterns ? ContextFlags.SkipBindingPatterns : ContextFlags.None);
            if (!contextualType.IsNone)
            {
                var inferenceTargetType = GetReturnTypeOfSignature(signature);
                if (CouldContainTypeVariables(inferenceTargetType))
                {
                    var outerContext = GetInferenceContext(node);
                    var isFromBindingPattern = !skipBindingPatterns && GetContextualType(node, ContextFlags.SkipBindingPatterns) != contextualType;
                    // A return type inference from a binding pattern can be used in instantiating the contextual
                    // type of an argument later in inference, but cannot stand on its own as the final return type.
                    if (!isFromBindingPattern)
                    {
                        // We clone the inference context to avoid disturbing a resolution in progress for an
                        // outer call expression. Effectively we just want a snapshot of whatever has been
                        // inferred for any outer call expression so far.
                        var outerMapper = GetMapperFromContext(CloneInferenceContext(outerContext, InferenceFlags.NoDefault));
                        var instantiatedType = InstantiateType(contextualType, outerMapper);
                        // If the contextual type is a generic function type with a single call signature, we
                        // instantiate the type with its own type parameters and type arguments. This ensures that
                        // the type parameters are not erased to type any during type inference such that they can
                        // be inferred as actual types from the contextual type.
                        var contextualSignature = GetSingleCallSignature(instantiatedType);
                        var inferenceSourceType = !contextualSignature.IsNone && Signatures.Record(contextualSignature).TypeParameters.Count > 0
                            ? GetOrCreateTypeFromSignature(GetSignatureInstantiationWithoutFillingInTypeArguments(contextualSignature, Types.Elements(Signatures.Record(contextualSignature).TypeParameters).ToArray()))
                            : instantiatedType;
                        // Inferences made from return types have lower priority than all other inferences.
                        InferTypes(context.Inferences, inferenceSourceType, inferenceTargetType, InferencePriority.ReturnType);
                    }
                    // Create a type mapper for instantiating generic contextual types using the inferences made
                    // from the return type. We need a separate inference pass here because (a) instantiation of
                    // the source type uses the outer context's return mapper (which excludes inferences made from
                    // outer arguments), and (b) we don't want any further inferences going into this context.
                    var returnContext = CreateInferenceContext(typeParameters, signature, context.Flags);
                    var returnSourceType = InstantiateType(contextualType, outerContext is null ? null : CreateOuterReturnMapper(outerContext));
                    InferTypes(returnContext.Inferences, returnSourceType, inferenceTargetType);
                    context.ReturnMapper = returnContext.Inferences.Exists(HasInferenceCandidates) ? GetMapperFromContext(CloneInferredPartOfContext(returnContext)) : null;
                }
            }
        }
        var restType = GetNonArrayRestType(signature);
        var argCount = !restType.IsNone ? Math.Min(GetParameterCount(signature) - 1, args.Count) : args.Count;
        if (!restType.IsNone && (Types.Flags(restType) & TypeFlags.TypeParameter) != 0)
        {
            var info = context.Inferences.Find(i => i.TypeParameter == restType);
            if (info is not null)
            {
                info.ImpliedArity = GetSpreadArgumentIndex(args, argCount) < 0 ? args.Count - argCount : null;
            }
        }
        var thisType = GetThisTypeOfSignature(signature);
        if (!thisType.IsNone && CouldContainTypeVariables(thisType))
        {
            var thisArgumentNode = GetThisArgumentOfCall(node);
            InferTypes(context.Inferences, GetThisArgumentType(thisArgumentNode), thisType);
        }
        for (var i = 0; i < argCount; i++)
        {
            var arg = args[i];
            if (!IsOmittedArg(arg))
            {
                var paramType = GetTypeAtPosition(signature, i);
                if (CouldContainTypeVariables(paramType))
                {
                    var argType = CheckArgumentWithContextualType(arg, paramType, context, checkMode);
                    InferTypes(context.Inferences, argType, paramType);
                }
            }
        }
        if (!restType.IsNone && CouldContainTypeVariables(restType))
        {
            var spreadType = GetSpreadArgumentType(args, argCount, args.Count, restType, context, checkMode);
            InferTypes(context.Inferences, spreadType, restType);
        }
        return GetInferredTypes(context);
    }

    // checker.ts getMutableArrayOrTupleType
    private Type GetMutableArrayOrTupleType(Type type)
    {
        if ((Types.Flags(type) & TypeFlags.Union) != 0)
        {
            return MapType(type, GetMutableArrayOrTupleType);
        }
        if ((Types.Flags(type) & TypeFlags.Any) != 0 || IsMutableArrayOrTuple(GetBaseConstraintOrType(type)))
        {
            return type;
        }
        if (IsTupleType(type))
        {
            return CreateTupleType(GetElementTypes(type), TupleTarget(type).ElementFlags, false, TupleTarget(type).LabeledElementDeclarations);
        }
        return CreateTupleType([type], [ElementFlags.Variadic]);
    }

    // checker.ts getSpreadArgumentType
    private Type GetSpreadArgumentType(List<CallArg> args, int index, int argCount, Type restType, InferenceContext? context, CheckMode checkMode)
    {
        var inConstContext = IsConstTypeVariable(restType);
        if (index >= argCount - 1 && argCount > 0)
        {
            var arg = args[argCount - 1];
            if (IsSpreadArgument(arg))
            {
                // We are inferring from a spread expression in the last argument position, i.e. both the parameter
                // and the argument are ...x forms.
                var spreadType = arg.IsSynthetic ? arg.SyntheticType
                    : CheckExpressionWithContextualType(At(arg.Node, StoreOf(arg.Node).Expression(arg.Node.Node)), restType, context, checkMode);
                if (IsArrayLikeType(spreadType))
                {
                    return GetMutableArrayOrTupleType(spreadType);
                }
                return CreateArrayType(CheckIteratedTypeOrElementType(IterationUse.Spread, spreadType, undefinedType,
                    arg.IsSynthetic ? arg.Node : At(arg.Node, StoreOf(arg.Node).Expression(arg.Node.Node))), inConstContext);
            }
        }
        var types = new List<Type>();
        var flags = new List<ElementFlags>();
        var names = new List<NodeRef>();
        for (var i = index; i < argCount; i++)
        {
            var arg = args[i];
            if (IsSpreadArgument(arg))
            {
                var spreadType = arg.IsSynthetic ? arg.SyntheticType : CheckExpression(At(arg.Node, StoreOf(arg.Node).Expression(arg.Node.Node)));
                if (IsArrayLikeType(spreadType))
                {
                    types.Add(spreadType);
                    flags.Add(ElementFlags.Variadic);
                }
                else
                {
                    types.Add(CheckIteratedTypeOrElementType(IterationUse.Spread, spreadType, undefinedType,
                        arg.IsSynthetic ? arg.Node : At(arg.Node, StoreOf(arg.Node).Expression(arg.Node.Node))));
                    flags.Add(ElementFlags.Rest);
                }
            }
            else
            {
                var contextualType = IsTupleType(restType)
                    ? GetContextualTypeForElementExpression(restType, i - index, argCount - index) is var c && !c.IsNone ? c : unknownType
                    : GetIndexedAccessType(restType, GetNumberLiteralType(i - index), AccessFlags.Contextual);
                var argType = CheckArgumentWithContextualType(arg, contextualType, context, checkMode);
                var hasPrimitiveContextualType = inConstContext || MaybeTypeOfKind(contextualType, TypeFlags.Primitive | TypeFlags.Index | TypeFlags.TemplateLiteral | TypeFlags.StringMapping);
                types.Add(hasPrimitiveContextualType ? GetRegularTypeOfLiteralType(argType) : GetWidenedLiteralType(argType));
                flags.Add(ElementFlags.Required);
            }
            names.Add(arg.IsSynthetic && !arg.TupleNameSource.IsNone ? arg.TupleNameSource : NodeRef.None);
        }
        return CreateTupleType([.. types], [.. flags], inConstContext && !SomeType(restType, IsMutableArrayLikeType), names.TrueForAll(n => n.IsNone) ? null : [.. names]);
    }

    // checker.ts checkTypeArguments
    private Type[]? CheckTypeArguments(Signature signature, NodeRef node, NodeList typeArgumentNodes, bool reportErrors, DiagnosticMessage? headMessage = null)
    {
        var declaration = Signatures.Record(signature).Declaration;
        var isJavascript = !declaration.IsNone && Utilities.IsInJSFile(StoreOf(declaration), declaration.Node);
        var typeParameters = Types.Elements(Signatures.Record(signature).TypeParameters).ToArray();
        var store = StoreOf(node);
        var typeArgumentList = store.Nodes(typeArgumentNodes).ToArray();
        var typeArgumentTypes = FillMissingTypeArguments(Array.ConvertAll(typeArgumentList, t => GetTypeFromTypeNode(At(node, t))), typeParameters, GetMinTypeArgumentCount(typeParameters), isJavascript);
        TypeMapper? mapper = null;
        for (var i = 0; i < typeArgumentList.Length; i++)
        {
            var constraint = GetConstraintOfTypeParameter(typeParameters[i]);
            if (!constraint.IsNone)
            {
                Func<DiagnosticMessageChain?>? errorInfo = reportErrors && headMessage is not null ? () => ChainDiagnosticMessages(null, Diagnostics.Type_0_does_not_satisfy_the_constraint_1) : null;
                var typeArgumentHeadMessage = headMessage ?? Diagnostics.Type_0_does_not_satisfy_the_constraint_1;
                mapper ??= CreateTypeMapper(typeParameters, typeArgumentTypes);
                var typeArgument = typeArgumentTypes[i];
                if (!CheckTypeAssignableTo(typeArgument, GetTypeWithThisArgument(InstantiateType(constraint, mapper), typeArgument),
                    reportErrors ? At(node, typeArgumentList[i]) : NodeRef.None, typeArgumentHeadMessage, errorInfo))
                {
                    return null;
                }
            }
        }
        return typeArgumentTypes;
    }

    // ---- resolveCall ----

    // checker.ts resolveCall
    private Signature ResolveCall(NodeRef node, Items<Signature> signatures, List<Signature>? candidatesOutArray, CheckMode checkMode, SignatureFlags callChainFlags, DiagnosticMessage? headMessage = null)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var isTaggedTemplate = store.Kind(n) == SyntaxKind.TaggedTemplateExpression;
        var isDecorator = store.Kind(n) == SyntaxKind.Decorator;
        var isInstanceof = store.Kind(n) == SyntaxKind.BinaryExpression;
        var reportErrors = !IsInferencePartiallyBlocked && candidatesOutArray is null;
        // The following variables are captured and modified by calls to chooseOverload.
        // If overload resolution or type argument inference fails, we want to report the
        // best error possible. The best error is one which says that an argument was not
        // assignable to a parameter. This implies that everything else about the overload
        // was fine. So if there is any overload that is only incorrect because of an
        // argument, we will report an error on that one.
        List<Signature>? candidatesForArgumentError = null;
        var candidateForArgumentArityError = Signature.None;
        var candidateForTypeArgumentError = Signature.None;
        var result = Signature.None;
        var argCheckMode = CheckMode.Normal;
        var typeArguments = NodeList.None;
        var isJsxOpeningOrSelfClosingElement = IsJsxOpeningLikeElement(store, n);
        var isJsxOpeningFragment = store.Kind(n) == SyntaxKind.JsxOpeningFragment;
        if (!isDecorator && !isInstanceof && !isJsxOpeningFragment && !IsSuperCall(store, n))
        {
            typeArguments = store.TypeArguments(n);
            // We already perform checking on the type arguments on the class declaration itself.
            if (isTaggedTemplate || isJsxOpeningOrSelfClosingElement || store.Kind(store.Expression(n)) != SyntaxKind.SuperKeyword)
            {
                CheckSourceElements(node, typeArguments);
            }
        }
        var candidates = candidatesOutArray ?? [];
        // reorderCandidates fills up the candidates array directly
        ReorderCandidates(signatures, candidates, callChainFlags);
        if (candidates.Count == 0)
        {
            if (reportErrors)
            {
                diagnostics.Add(GetDiagnosticForCallNode(node, ChainDiagnosticMessages(null, Diagnostics.Call_target_does_not_contain_any_signatures)));
            }
            return ResolveErrorCall(node);
        }
        var args = GetEffectiveCallArguments(node);
        // The excludeArgument array contains true for each context sensitive argument (an argument
        // is context sensitive it is susceptible to a one-time permanent contextual typing).
        //
        // The idea is that we will perform type argument inference & assignability checking once
        // without using the susceptible parameters that are functions, and once more for those
        // parameters, contextually typing each as we go along.
        //
        // For a tagged template, then the first argument be 'undefined' if necessary because it
        // represents a TemplateStringsArray.
        //
        // For a decorator, no arguments are susceptible to contextual typing due to the fact
        // decorators are applied to a declaration by the emitter, and not to an expression.
        var isSingleNonGenericCandidate = candidates.Count == 1 && Signatures.Record(candidates[0]).TypeParameters.Count == 0;
        if (!isDecorator && !isSingleNonGenericCandidate && args.Exists(a => !a.IsSynthetic && IsContextSensitive(a.Node)))
        {
            argCheckMode = CheckMode.SkipContextSensitive;
        }
        // If we are in signature help, a trailing comma indicates that we intend to provide another argument,
        // so we will only accept overloads with arity at least 1 higher than the current number of provided arguments.
        var signatureHelpTrailingComma = (checkMode & CheckMode.IsForSignatureHelp) != 0 && store.Kind(n) == SyntaxKind.CallExpression && store.HasTrailingComma(store.Arguments(n));
        // Section 4.12.1:
        // if the candidate list contains one or more signatures for which the type of each argument
        // expression is a subtype of each corresponding parameter type, the return type of the first
        // of those signatures becomes the return type of the function call.
        // Otherwise, the return type of the first signature in the candidate list becomes the return
        // type of the function call.
        //
        // Whether the call is an error is determined by assignability of the arguments. The subtype pass
        // is just important for choosing the best signature. So in the case where there is only one
        // signature, the subtype pass is useless. So skipping it is an optimization.
        if (candidates.Count > 1)
        {
            result = ChooseOverload(candidates, subtypeRelation, isSingleNonGenericCandidate, signatureHelpTrailingComma);
        }
        if (result.IsNone)
        {
            result = ChooseOverload(candidates, assignableRelation, isSingleNonGenericCandidate, signatureHelpTrailingComma);
        }
        var linksSignature = SignatureLinks.Has(node) ? SignatureLinks.Get(node).ResolvedSignature : Signature.None;
        if (linksSignature != resolvingSignature && candidatesOutArray is null && !linksSignature.IsNone)
        {
            // There are 2 situations in which it's good to preemptively return the cached result here:
            //
            // 1. if the signature resolution originated on a node that itself depends on the contextual type
            // then it's possible that the resolved signature might not be the same as the one that would be computed in source order
            // since resolving such signature leads to resolving the potential outer signature, its arguments and thus the very same signature
            // it's possible that this inner resolution sets the resolvedSignature first.
            // In such a case we ignore the local result and reuse the correct one that was cached.
            //
            // 2. In certain circular-like situations it's possible that the compiler reentries this function for the same node.
            // It's possible to resolve the inner call against preemptively set empty members (for example in `resolveAnonymousTypeMembers`) of some type.
            return linksSignature;
        }
        if (!result.IsNone)
        {
            return result;
        }
        result = GetCandidateForOverloadFailure(node, candidates, args, candidatesOutArray is not null, checkMode);
        // Preemptively cache the result; getResolvedSignature will do this after we return, but
        // we need to ensure that the result is present for the error checks below so that if
        // this signature is encountered again, we handle the circularity (rather than producing a
        // different result which may produce no errors and assert). Callers of getResolvedSignature
        // don't hit this issue because they only observe this result after it's had a chance to
        // be cached, but the error reporting code below executes before getResolvedSignature sets
        // resolvedSignature.
        SignatureLinks.Get(node).ResolvedSignature = result;
        // No signatures were applicable. Now report errors based on the last applicable signature with
        // no arguments excluded from assignability checks.
        // If candidate is undefined, it means that no candidates had a suitable arity. In that case,
        // skip the checkApplicableSignature check.
        if (reportErrors)
        {
            // If the call expression is a synthetic call to a `[Symbol.hasInstance]` method then we will produce a head
            // message when reporting diagnostics that explains how we got to `right[Symbol.hasInstance](left)` from
            // `left instanceof right`, as it pertains to "Argument" related messages reported for the call.
            if (headMessage is null && isInstanceof)
            {
                headMessage = Diagnostics.The_left_hand_side_of_an_instanceof_expression_must_be_assignable_to_the_first_argument_of_the_right_hand_side_s_Symbol_hasInstance_method;
            }
            if (candidatesForArgumentError is not null)
            {
                if (candidatesForArgumentError.Count == 1 || candidatesForArgumentError.Count > 3)
                {
                    var last = candidatesForArgumentError[^1];
                    DiagnosticMessageChain? chain = null;
                    if (candidatesForArgumentError.Count > 3)
                    {
                        chain = ChainDiagnosticMessages(chain, Diagnostics.The_last_overload_gave_the_following_error);
                        chain = ChainDiagnosticMessages(chain, Diagnostics.No_overload_matches_this_call);
                    }
                    if (headMessage is not null)
                    {
                        chain = ChainDiagnosticMessages(chain, headMessage);
                    }
                    var finalChain = chain;
                    var diags = GetSignatureApplicabilityError(node, args, last, assignableRelation, CheckMode.Normal, reportErrors: true, () => finalChain);
                    if (diags is not null)
                    {
                        foreach (var d in diags)
                        {
                            var lastDeclaration = Signatures.Record(last).Declaration;
                            if (!lastDeclaration.IsNone && candidatesForArgumentError.Count > 3)
                            {
                                AddRelatedInfo(d, CreateRelatedForNode(lastDeclaration, Diagnostics.The_last_overload_is_declared_here));
                            }
                            AddImplementationSuccessElaboration(last, d);
                            diagnostics.Add(d);
                        }
                    }
                }
                else
                {
                    var allDiagnostics = new List<List<ProgramDiagnostic>>();
                    var max = 0;
                    var min = int.MaxValue;
                    var minIndex = 0;
                    var i = 0;
                    foreach (var c in candidatesForArgumentError)
                    {
                        var index = i;
                        var candidate = c;
                        var diags = GetSignatureApplicabilityError(node, args, c, assignableRelation, CheckMode.Normal, reportErrors: true,
                            () => ChainDiagnosticMessages(null, Diagnostics.Overload_0_of_1_2_gave_the_following_error, index + 1, candidates.Count, SignatureToString(candidate)));
                        if (diags is not null)
                        {
                            if (diags.Count <= min)
                            {
                                min = diags.Count;
                                minIndex = i;
                            }
                            max = Math.Max(max, diags.Count);
                            allDiagnostics.Add(diags);
                        }
                        i++;
                    }
                    List<ProgramDiagnostic> flattened;
                    if (max > 1)
                    {
                        flattened = allDiagnostics[minIndex];
                    }
                    else
                    {
                        flattened = [];
                        foreach (var d in allDiagnostics)
                        {
                            flattened.AddRange(d);
                        }
                    }
                    if (flattened.Count > 0)
                    {
                        var details = flattened.ConvertAll(CreateDiagnosticMessageChainFromDiagnostic);
                        var chain = ProgramDiagnostics.ChainDiagnosticMessages(details, Diagnostics.No_overload_matches_this_call);
                        if (headMessage is not null)
                        {
                            chain = ChainDiagnosticMessages(chain, headMessage);
                        }
                        var related = new List<DiagnosticRelatedInformation>();
                        foreach (var d in flattened)
                        {
                            if (d.RelatedInformation is not null)
                            {
                                related.AddRange(d.RelatedInformation);
                            }
                        }
                        ProgramDiagnostic diag;
                        var first = flattened[0];
                        if (flattened.TrueForAll(d => d.StartOrUndefined == first.StartOrUndefined && d.LengthOrUndefined == first.LengthOrUndefined && d.SourceFile == first.SourceFile))
                        {
                            diag = new ProgramDiagnostic
                            {
                                SourceFile = first.SourceFile,
                                File = first.File,
                                StartOrUndefined = first.StartOrUndefined,
                                LengthOrUndefined = first.LengthOrUndefined,
                                Code = chain.Code,
                                Category = chain.Category,
                                MessageText = chain.MessageText,
                                Chain = chain,
                                RelatedInformation = related,
                            };
                        }
                        else
                        {
                            diag = CreateDiagnosticForNodeFromMessageChain(GetErrorNodeForCallNode(node), chain, related);
                        }
                        AddImplementationSuccessElaboration(candidatesForArgumentError[0], diag);
                        diagnostics.Add(diag);
                    }
                }
            }
            else if (!candidateForArgumentArityError.IsNone)
            {
                diagnostics.Add(GetArgumentArityError(node, [candidateForArgumentArityError], args, headMessage));
            }
            else if (!candidateForTypeArgumentError.IsNone)
            {
                CheckTypeArguments(candidateForTypeArgumentError, node, store.TypeArguments(n), reportErrors: true, headMessage);
            }
            else if (!isJsxOpeningFragment)
            {
                var signaturesWithCorrectTypeArgumentArity = signatures.FindAll(s => HasCorrectTypeArgumentArity(s, node, typeArguments));
                if (signaturesWithCorrectTypeArgumentArity.Count == 0)
                {
                    diagnostics.Add(GetTypeArgumentArityError(node, signatures, typeArguments, headMessage));
                }
                else
                {
                    diagnostics.Add(GetArgumentArityError(node, signaturesWithCorrectTypeArgumentArity, args, headMessage));
                }
            }
        }
        return result;

        void AddImplementationSuccessElaboration(Signature failed, ProgramDiagnostic diagnostic)
        {
            var oldCandidatesForArgumentError = candidatesForArgumentError;
            var oldCandidateForArgumentArityError = candidateForArgumentArityError;
            var oldCandidateForTypeArgumentError = candidateForTypeArgumentError;
            var failedDeclaration = Signatures.Record(failed).Declaration;
            var failedSymbol = failedDeclaration.IsNone ? SymbolRef.None : NodeSymbol(failedDeclaration);
            var failedSignatureDeclarations = failedSymbol.IsNone ? null : DeclarationsOf(failedSymbol);
            var isOverload = failedSignatureDeclarations is { Count: > 1 };
            var implDecl = isOverload ? failedSignatureDeclarations!.Find(d => Utilities.IsFunctionLikeDeclaration(StoreOf(d), d.Node) && Utilities.NodeIsPresent(StoreOf(d), StoreOf(d).Body(d.Node))) : NodeRef.None;
            if (!implDecl.IsNone)
            {
                var candidate = GetSignatureFromDeclaration(implDecl);
                var isSingleNonGeneric = Signatures.Record(candidate).TypeParameters.Count == 0;
                if (!ChooseOverload([candidate], assignableRelation, isSingleNonGeneric).IsNone)
                {
                    AddRelatedInfo(diagnostic, CreateRelatedForNode(implDecl, Diagnostics.The_call_would_have_succeeded_against_this_implementation_but_implementation_signatures_of_overloads_are_not_externally_visible));
                }
            }
            candidatesForArgumentError = oldCandidatesForArgumentError;
            candidateForArgumentArityError = oldCandidateForArgumentArityError;
            candidateForTypeArgumentError = oldCandidateForTypeArgumentError;
        }

        Signature ChooseOverload(List<Signature> candidateList, Relation relation, bool isSingleNonGeneric, bool trailingComma = false)
        {
            candidatesForArgumentError = null;
            candidateForArgumentArityError = Signature.None;
            candidateForTypeArgumentError = Signature.None;
            var hasTypeArguments = !typeArguments.IsNone && store.Length(typeArguments) > 0;
            if (isSingleNonGeneric)
            {
                var candidate = candidateList[0];
                if (hasTypeArguments || !HasCorrectArity(node, args, candidate, trailingComma))
                {
                    return Signature.None;
                }
                if (GetSignatureApplicabilityError(node, args, candidate, relation, CheckMode.Normal, reportErrors: false, null) is not null)
                {
                    candidatesForArgumentError = [candidate];
                    return Signature.None;
                }
                return candidate;
            }
            for (var candidateIndex = 0; candidateIndex < candidateList.Count; candidateIndex++)
            {
                var candidate = candidateList[candidateIndex];
                if (!HasCorrectTypeArgumentArity(candidate, node, typeArguments) || !HasCorrectArity(node, args, candidate, trailingComma))
                {
                    continue;
                }
                Signature checkCandidate;
                InferenceContext? inferenceContext = null;
                var candidateTypeParameters = Signatures.Record(candidate).TypeParameters;
                var candidateDeclaration = Signatures.Record(candidate).Declaration;
                var isJavascript = !candidateDeclaration.IsNone && Utilities.IsInJSFile(StoreOf(candidateDeclaration), candidateDeclaration.Node);
                if (candidateTypeParameters.Count > 0)
                {
                    Type[]? typeArgumentTypes;
                    if (hasTypeArguments)
                    {
                        typeArgumentTypes = CheckTypeArguments(candidate, node, typeArguments, reportErrors: false);
                        if (typeArgumentTypes is null)
                        {
                            candidateForTypeArgumentError = candidate;
                            continue;
                        }
                    }
                    else
                    {
                        inferenceContext = CreateInferenceContext(Types.Elements(candidateTypeParameters), candidate, Utilities.IsInJSFile(store, n) ? InferenceFlags.AnyDefault : InferenceFlags.None);
                        typeArgumentTypes = InferTypeArguments(node, candidate, args, argCheckMode | CheckMode.SkipGenericFunctions, inferenceContext);
                        argCheckMode |= (inferenceContext.Flags & InferenceFlags.SkippedGenericFunction) != 0 ? CheckMode.SkipGenericFunctions : CheckMode.Normal;
                    }
                    checkCandidate = GetSignatureInstantiation(candidate, typeArgumentTypes, isJavascript, inferenceContext?.InferredTypeParameters);
                    // If the original signature has a generic rest type, instantiation may produce a
                    // signature with different arity and we need to perform another arity check.
                    if (!GetNonArrayRestType(candidate).IsNone && !HasCorrectArity(node, args, checkCandidate, trailingComma))
                    {
                        candidateForArgumentArityError = checkCandidate;
                        continue;
                    }
                }
                else
                {
                    checkCandidate = candidate;
                }
                if (GetSignatureApplicabilityError(node, args, checkCandidate, relation, argCheckMode, reportErrors: false, null) is not null)
                {
                    // Give preference to error candidates that have no rest parameters (as they are more specific)
                    (candidatesForArgumentError ??= []).Add(checkCandidate);
                    continue;
                }
                if (argCheckMode != CheckMode.Normal)
                {
                    // If one or more context sensitive arguments were excluded, we start including
                    // them now (and keeping do so for any subsequent candidates) and perform a second
                    // round of type inference and applicability checking for this particular candidate.
                    argCheckMode = CheckMode.Normal;
                    if (inferenceContext is not null)
                    {
                        var typeArgumentTypes = InferTypeArguments(node, candidate, args, argCheckMode, inferenceContext);
                        checkCandidate = GetSignatureInstantiation(candidate, typeArgumentTypes, isJavascript, inferenceContext.InferredTypeParameters);
                        // If the original signature has a generic rest type, instantiation may produce a
                        // signature with different arity and we need to perform another arity check.
                        if (!GetNonArrayRestType(candidate).IsNone && !HasCorrectArity(node, args, checkCandidate, trailingComma))
                        {
                            candidateForArgumentArityError = checkCandidate;
                            continue;
                        }
                    }
                    if (GetSignatureApplicabilityError(node, args, checkCandidate, relation, argCheckMode, reportErrors: false, null) is not null)
                    {
                        // Give preference to error candidates that have no rest parameters (as they are more specific)
                        (candidatesForArgumentError ??= []).Add(checkCandidate);
                        continue;
                    }
                }
                candidateList[candidateIndex] = checkCandidate;
                return checkCandidate;
            }
            return Signature.None;
        }
    }

    // utilities.ts createDiagnosticMessageChainFromDiagnostic
    private static DiagnosticMessageChain CreateDiagnosticMessageChainFromDiagnostic(ProgramDiagnostic diagnostic) =>
        diagnostic.Chain ?? new DiagnosticMessageChain { Code = diagnostic.Code, Category = diagnostic.Category, MessageText = diagnostic.MessageText };

    // checker.ts getArgumentArityError
    private ProgramDiagnostic GetArgumentArityError(NodeRef node, List<Signature> signatures, List<CallArg> args, DiagnosticMessage? headMessage)
    {        var spreadIndex = GetSpreadArgumentIndex(args);
        if (spreadIndex > -1)
        {
            return CreateDiagnosticAtArg(args[spreadIndex], Diagnostics.A_spread_argument_must_either_have_a_tuple_type_or_be_passed_to_a_rest_parameter);
        }
        var min = int.MaxValue; // smallest parameter count
        var max = int.MinValue; // largest parameter count
        var maxBelow = int.MinValue; // largest parameter count that is smaller than the number of arguments
        var minAbove = int.MaxValue; // smallest parameter count that is larger than the number of arguments
        var closestSignature = Signature.None;
        foreach (var sig in signatures)
        {
            var minParameter = GetMinArgumentCount(sig);
            var maxParameter = GetParameterCount(sig);
            // smallest/largest parameter counts
            if (minParameter < min)
            {
                min = minParameter;
                closestSignature = sig;
            }
            max = Math.Max(max, maxParameter);
            // shortest parameter count *longer than the call*/longest parameter count *shorter than the call*
            if (minParameter < args.Count && minParameter > maxBelow)
            {
                maxBelow = minParameter;
            }
            if (args.Count < maxParameter && maxParameter < minAbove)
            {
                minAbove = maxParameter;
            }
        }
        var hasRestParameter = signatures.Exists(HasEffectiveRestParameter);
        var parameterRange = hasRestParameter ? min.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : min < max ? min.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + max.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : min.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var isVoidPromiseError = !hasRestParameter && parameterRange == "1" && args.Count == 0 && IsPromiseResolveArityError(node);
        var isDecoratorNode = Kind(node) == SyntaxKind.Decorator;
        var error = isDecoratorNode
            ? hasRestParameter ? Diagnostics.The_runtime_will_invoke_the_decorator_with_1_arguments_but_the_decorator_expects_at_least_0 : Diagnostics.The_runtime_will_invoke_the_decorator_with_1_arguments_but_the_decorator_expects_0
            : hasRestParameter ? Diagnostics.Expected_at_least_0_arguments_but_got_1
            : isVoidPromiseError ? Diagnostics.Expected_0_arguments_but_got_1_Did_you_forget_to_include_void_in_your_type_argument_to_Promise
            : Diagnostics.Expected_0_arguments_but_got_1;
        if (min < args.Count && args.Count < max)
        {
            // between min and max, but with no matching overload
            var chain = ChainDiagnosticMessages(null, Diagnostics.No_overload_expects_0_arguments_but_overloads_do_exist_that_expect_either_1_or_2_arguments, args.Count, maxBelow, minAbove);
            if (headMessage is not null)
            {
                chain = ChainDiagnosticMessages(chain, headMessage);
            }
            return GetDiagnosticForCallNode(node, chain);
        }
        if (args.Count < min)
        {
            // too short: put the error span on the call expression, not any of the args
            var chain = ChainDiagnosticMessages(null, error, parameterRange, args.Count);
            if (headMessage is not null)
            {
                chain = ChainDiagnosticMessages(chain, headMessage);
            }
            var diagnostic = GetDiagnosticForCallNode(node, chain);
            var closestDeclaration = closestSignature.IsNone ? NodeRef.None : Signatures.Record(closestSignature).Declaration;
            if (!closestDeclaration.IsNone)
            {
                var dstore = StoreOf(closestDeclaration);
                var parameters = dstore.Parameters(closestDeclaration.Node);
                var parameterIndex = !Signatures.Record(closestSignature).ThisParameter.IsNone ? args.Count + 1 : args.Count;
                if (!parameters.IsNone && parameterIndex < dstore.Length(parameters))
                {
                    var parameter = At(closestDeclaration, dstore.Element(parameters, parameterIndex));
                    var name = dstore.Name(parameter.Node);
                    ProgramRelatedInformation parameterError;
                    if (Utilities.IsBindingPattern(dstore, name))
                    {
                        parameterError = CreateRelatedForNode(parameter, Diagnostics.An_argument_matching_this_binding_pattern_was_not_provided);
                    }
                    else if (!dstore.DotDotDotToken(parameter.Node).IsNone)
                    {
                        parameterError = CreateRelatedForNode(parameter, Diagnostics.Arguments_for_the_rest_parameter_0_were_not_provided, dstore.Texts.GetString(Utilities.IdText(dstore, Utilities.GetFirstIdentifier(dstore, name))));
                    }
                    else
                    {
                        parameterError = CreateRelatedForNode(parameter, Diagnostics.An_argument_for_0_was_not_provided,
                            name.IsNone ? args.Count : dstore.Texts.GetString(Utilities.IdText(dstore, Utilities.GetFirstIdentifier(dstore, name))));
                    }
                    AddRelatedInfo(diagnostic, parameterError);
                    return diagnostic;
                }
            }
            return diagnostic;
        }
        // too long; error goes on the excess parameters
        var pos = ArgPos(args[max]);
        var end = ArgEnd(args[^1]);
        if (end == pos)
        {
            end++;
        }
        var excessChain = ChainDiagnosticMessages(null, error, parameterRange, args.Count);
        if (headMessage is not null)
        {
            excessChain = ChainDiagnosticMessages(excessChain, headMessage);
        }
        var store = StoreOf(node);
        var start = Scanner.SkipTrivia(store.SourceFileText(Utilities.RootOf(store, node.Node)).Chars, pos);
        return CreateFileDiagnosticFromMessageChain(node.File, start, end - start, excessChain);
    }

    // a diagnostic on an argument (a synthetic one: its text range)
    private ProgramDiagnostic CreateDiagnosticAtArg(CallArg arg, DiagnosticMessage message, params object?[] args)
    {
        if (!arg.IsSynthetic)
        {
            return CreateDiagnosticForNode(arg.Node, message, args);
        }
        var store = StoreOf(arg.Node);
        var start = Scanner.SkipTrivia(store.SourceFileText(Utilities.RootOf(store, arg.Node.Node)).Chars, arg.Pos);
        return CreateFileDiagnostic(arg.Node.File, start, arg.End - start, message, args);
    }

    // checker.ts getTypeArgumentArityError
    private ProgramDiagnostic GetTypeArgumentArityError(NodeRef node, Items<Signature> signatures, NodeList typeArguments, DiagnosticMessage? headMessage)
    {
        var argCount = StoreOf(node).Length(typeArguments);
        // No overloads exist
        if (signatures.Count == 1)
        {
            var sig = signatures[0];
            var min = GetMinTypeArgumentCount(Signatures.Record(sig).TypeParameters);
            var max = Signatures.Record(sig).TypeParameters.Count;
            var range = min < max ? min.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + max.ToString(System.Globalization.CultureInfo.InvariantCulture) : min.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var chain = ChainDiagnosticMessages(null, Diagnostics.Expected_0_type_arguments_but_got_1, range, argCount);
            if (headMessage is not null)
            {
                chain = ChainDiagnosticMessages(chain, headMessage);
            }
            return CreateDiagnosticForNodeArrayFromMessageChain(node, typeArguments, chain);
        }
        // Overloads exist
        var belowArgCount = int.MinValue;
        var aboveArgCount = int.MaxValue;
        foreach (var sig in signatures)
        {
            var min = GetMinTypeArgumentCount(Signatures.Record(sig).TypeParameters);
            var max = Signatures.Record(sig).TypeParameters.Count;
            if (min > argCount)
            {
                aboveArgCount = Math.Min(aboveArgCount, min);
            }
            else if (max < argCount)
            {
                belowArgCount = Math.Max(belowArgCount, max);
            }
        }
        if (belowArgCount != int.MinValue && aboveArgCount != int.MaxValue)
        {
            var chain = ChainDiagnosticMessages(null, Diagnostics.No_overload_expects_0_type_arguments_but_overloads_do_exist_that_expect_either_1_or_2_type_arguments, argCount, belowArgCount, aboveArgCount);
            if (headMessage is not null)
            {
                chain = ChainDiagnosticMessages(chain, headMessage);
            }
            return CreateDiagnosticForNodeArrayFromMessageChain(node, typeArguments, chain);
        }
        var expected = ChainDiagnosticMessages(null, Diagnostics.Expected_0_type_arguments_but_got_1, belowArgCount == int.MinValue ? aboveArgCount : belowArgCount, argCount);
        if (headMessage is not null)
        {
            expected = ChainDiagnosticMessages(expected, headMessage);
        }
        return CreateDiagnosticForNodeArrayFromMessageChain(node, typeArguments, expected);
    }

    // checker.ts isPromiseResolveArityError
    private bool IsPromiseResolveArityError(NodeRef node)
    {
        var store = StoreOf(node);
        if (store.Kind(node.Node) != SyntaxKind.CallExpression || store.Kind(store.Expression(node.Node)) != SyntaxKind.Identifier)
        {
            return false;
        }
        var expression = At(node, store.Expression(node.Node));
        var symbol = ResolveName(expression, store.Texts.GetString(store.EscapedText(expression.Node))!, NodeRef.None, SymbolFlags.Value, null, isUse: false);
        var decl = symbol.IsNone ? NodeRef.None : ValueDeclarationOf(symbol);
        if (decl.IsNone || Kind(decl) != SyntaxKind.Parameter)
        {
            return false;
        }
        var func = Parent(decl);
        if (!IsFunctionExpressionOrArrowFunction(StoreOf(func), func.Node) || Kind(Parent(func)) != SyntaxKind.NewExpression || Kind(At(func, StoreOf(func).Expression(Parent(func).Node))) != SyntaxKind.Identifier)
        {
            return false;
        }
        var globalPromiseSymbol = GetGlobalPromiseConstructorSymbol(reportErrors: false);
        if (globalPromiseSymbol.IsNone)
        {
            return false;
        }
        var constructorSymbol = GetSymbolAtLocation(At(func, StoreOf(func).Expression(Parent(func).Node)));
        return constructorSymbol == globalPromiseSymbol;
    }

    // checker.ts getCandidateForOverloadFailure
    private Signature GetCandidateForOverloadFailure(NodeRef node, List<Signature> candidates, List<CallArg> args, bool hasCandidatesOutArray, CheckMode checkMode)
    {
        CheckNodeDeferred(node);
        // Normally we will combine overloads. Skip this if they have type parameters since that's hard to combine.
        // Don't do this if there is a `candidatesOutArray`,
        // because then we want the chosen best candidate to be one of the overloads, not a combination.
        return hasCandidatesOutArray || candidates.Count == 1 || candidates.Exists(c => Signatures.Record(c).TypeParameters.Count > 0)
            ? PickLongestCandidateSignature(node, candidates, args, checkMode)
            : CreateUnionOfSignaturesForOverloadFailure(candidates);
    }

    // checker.ts createUnionOfSignaturesForOverloadFailure
    private Signature CreateUnionOfSignaturesForOverloadFailure(List<Signature> candidates)
    {
        var thisParameters = new List<SymbolRef>();
        foreach (var c in candidates)
        {
            if (!Signatures.Record(c).ThisParameter.IsNone)
            {
                thisParameters.Add(Signatures.Record(c).ThisParameter);
            }
        }
        var thisParameter = SymbolRef.None;
        if (thisParameters.Count > 0)
        {
            thisParameter = CreateSymbolWithType(thisParameters[0], GetUnionType(thisParameters.ConvertAll(GetTypeOfParameter).ToArray(), UnionReduction.Subtype));
        }
        var minArgumentCount = int.MaxValue;
        var maxNonRestParam = int.MinValue;
        foreach (var c in candidates)
        {
            var count = GetNumNonRestParameters(c);
            minArgumentCount = Math.Min(minArgumentCount, count);
            maxNonRestParam = Math.Max(maxNonRestParam, count);
        }
        var parameters = new List<SymbolRef>();
        for (var i = 0; i < maxNonRestParam; i++)
        {
            var symbols = new List<SymbolRef>();
            var types = new List<Type>();
            foreach (var s in candidates)
            {
                var sParams = ParametersOf(s);
                var symbol = SignatureHasRestParameter(s) ? i < sParams.Length - 1 ? sParams[i] : sParams[^1] : i < sParams.Length ? sParams[i] : SymbolRef.None;
                if (!symbol.IsNone)
                {
                    symbols.Add(symbol);
                }
                var t = TryGetTypeAtPosition(s, i);
                if (!t.IsNone)
                {
                    types.Add(t);
                }
            }
            parameters.Add(CreateSymbolWithType(symbols[0], GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(types), UnionReduction.Subtype)));
        }
        var restParameterSymbols = new List<SymbolRef>();
        foreach (var c in candidates)
        {
            if (SignatureHasRestParameter(c))
            {
                restParameterSymbols.Add(ParametersOf(c)[^1]);
            }
        }
        var flags = SignatureFlags.IsSignatureCandidateForOverloadFailure;
        if (restParameterSymbols.Count != 0)
        {
            var restTypes = new List<Type>();
            foreach (var c in candidates)
            {
                var t = TryGetRestTypeOfSignature(c);
                if (!t.IsNone)
                {
                    restTypes.Add(t);
                }
            }
            var type = CreateArrayType(GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(restTypes), UnionReduction.Subtype));
            parameters.Add(CreateSymbolWithType(restParameterSymbols[0], type));
            flags |= SignatureFlags.HasRestParameter;
        }
        if (candidates.Exists(SignatureHasLiteralTypes))
        {
            flags |= SignatureFlags.HasLiteralTypes;
        }
        var result = CreateSignature(Signatures.Record(candidates[0]).Declaration, TypeList.Empty, thisParameter, [.. parameters],
            GetIntersectionType(candidates.ConvertAll(GetReturnTypeOfSignature).ToArray()), minArgumentCount, flags);
        Signatures.Record(result).SyntheticFunctionTypeDeclaration = Signatures.Record(candidates[0]).SyntheticFunctionTypeDeclaration;
        return result;
    }

    // checker.ts getNumNonRestParameters
    private int GetNumNonRestParameters(Signature signature)
    {
        var numParams = ParametersOf(signature).Length;
        return SignatureHasRestParameter(signature) ? numParams - 1 : numParams;
    }

    // checker.ts pickLongestCandidateSignature
    private Signature PickLongestCandidateSignature(NodeRef node, List<Signature> candidates, List<CallArg> args, CheckMode checkMode)
    {
        // Pick the longest signature. This way we can get a contextual type for cases like:
        //     declare function f(a: { xa: number; xb: number; }, b: number);
        //     f({ |
        // Also, use explicitly-supplied type arguments if they are provided, so we can get a contextual signature in cases like:
        //     declare function f<T>(k: keyof T);
        //     f<Foo>("
        // (apparentArgumentCount: signature help only)
        var bestIndex = GetLongestCandidateIndex(candidates, args.Count);
        var candidate = candidates[bestIndex];
        var typeParameters = Types.Elements(Signatures.Record(candidate).TypeParameters).ToArray();
        if (typeParameters.Length == 0)
        {
            return candidate;
        }
        var store = StoreOf(node);
        var typeArgumentNodes = store.Kind(node.Node) is SyntaxKind.CallExpression or SyntaxKind.NewExpression or SyntaxKind.TaggedTemplateExpression ? store.TypeArguments(node.Node) : NodeList.None;
        Signature instantiated;
        if (!typeArgumentNodes.IsNone)
        {
            instantiated = CreateSignatureInstantiation(candidate, GetTypeArgumentsFromNodes(node, typeArgumentNodes, typeParameters, Utilities.IsInJSFile(store, node.Node)));
        }
        else
        {
            // inferSignatureInstantiationForOverloadFailure
            var inferenceContext = CreateInferenceContext(typeParameters, candidate, Utilities.IsInJSFile(store, node.Node) ? InferenceFlags.AnyDefault : InferenceFlags.None);
            var typeArgumentTypes = InferTypeArguments(node, candidate, args, checkMode | CheckMode.SkipContextSensitive | CheckMode.SkipGenericFunctions, inferenceContext);
            instantiated = CreateSignatureInstantiation(candidate, typeArgumentTypes);
        }
        candidates[bestIndex] = instantiated;
        return instantiated;
    }

    // checker.ts getTypeArgumentsFromNodes
    private Type[] GetTypeArgumentsFromNodes(NodeRef node, NodeList typeArgumentNodes, Type[] typeParameters, bool isJs)
    {
        var store = StoreOf(node);
        var typeArguments = new List<Type>();
        foreach (var t in store.Nodes(typeArgumentNodes))
        {
            typeArguments.Add(GetTypeOfNode(At(node, t)));
        }
        while (typeArguments.Count > typeParameters.Length)
        {
            typeArguments.RemoveAt(typeArguments.Count - 1);
        }
        while (typeArguments.Count < typeParameters.Length)
        {
            var tp = typeParameters[typeArguments.Count];
            var defaultType = GetDefaultFromTypeParameter(tp);
            var constraint = GetConstraintOfTypeParameter(tp);
            typeArguments.Add(!defaultType.IsNone ? defaultType : !constraint.IsNone ? constraint : isJs ? anyType : unknownType);
        }
        return [.. typeArguments];
    }

    // checker.ts getLongestCandidateIndex
    private int GetLongestCandidateIndex(List<Signature> candidates, int argsCount)
    {
        var maxParamsIndex = -1;
        var maxParams = -1;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var paramCount = GetParameterCount(candidate);
            if (HasEffectiveRestParameter(candidate) || paramCount >= argsCount)
            {
                return i;
            }
            if (paramCount > maxParams)
            {
                maxParams = paramCount;
                maxParamsIndex = i;
            }
        }
        return maxParamsIndex;
    }

    /// <summary>checker.ts instantiateSignatureInContextOf.</summary>
    internal Signature InstantiateSignatureInContextOf(Signature signature, Signature contextualSignature, InferenceContext? inferenceContext, Func<Type, Type, bool>? compareTypes = null)
    {
        var context = CreateInferenceContext(GetTypeParametersForMapper(signature), signature, InferenceFlags.None, compareTypes);
        // We clone the inferenceContext to avoid fixing. For example, when the source signature is <T>(x: T) => T[] and
        // the contextual signature is (...args: A) => B, we want to infer the element type of A's constraint (say 'any')
        // for T but leave it possible to later infer '[any]' back to A.
        var restType = GetEffectiveRestType(contextualSignature);
        var mapper = inferenceContext is null ? null : !restType.IsNone && (Types.Flags(restType) & TypeFlags.TypeParameter) != 0 ? inferenceContext.NonFixingMapper : inferenceContext.Mapper;
        var sourceSignature = mapper is not null ? InstantiateSignature(contextualSignature, mapper) : contextualSignature;
        ApplyToParameterTypes(sourceSignature, signature, (source, target) =>
        {
            // Type parameters from outer context referenced by source type are fixed by instantiation of the source type
            InferTypes(context.Inferences, source, target);
        });
        if (inferenceContext is null)
        {
            ApplyToReturnTypes(contextualSignature, signature, (source, target) => InferTypes(context.Inferences, source, target, InferencePriority.ReturnType));
        }
        var declaration = Signatures.Record(contextualSignature).Declaration;
        return GetSignatureInstantiation(signature, GetInferredTypes(context), !declaration.IsNone && Utilities.IsInJSFile(StoreOf(declaration), declaration.Node));
    }

    // checker.ts getTypeParametersForMapper
    private Type[] GetTypeParametersForMapper(Signature signature) =>
        Array.ConvertAll(Types.Elements(Signatures.Record(signature).TypeParameters).ToArray(), tp =>
            Types.TypeParameter(tp).Mapper is { } tpMapper ? InstantiateType(tp, tpMapper) : tp);

    // checker.ts applyToParameterTypes
    private void ApplyToParameterTypes(Signature source, Signature target, Action<Type, Type> callback)
    {
        var sourceCount = GetParameterCount(source);
        var targetCount = GetParameterCount(target);
        var sourceRestType = GetEffectiveRestType(source);
        var targetRestType = GetEffectiveRestType(target);
        var targetNonRestCount = !targetRestType.IsNone ? targetCount - 1 : targetCount;
        var paramCount = !sourceRestType.IsNone ? targetNonRestCount : Math.Min(sourceCount, targetNonRestCount);
        var sourceThisType = GetThisTypeOfSignature(source);
        if (!sourceThisType.IsNone)
        {
            var targetThisType = GetThisTypeOfSignature(target);
            if (!targetThisType.IsNone)
            {
                callback(sourceThisType, targetThisType);
            }
        }
        for (var i = 0; i < paramCount; i++)
        {
            callback(GetTypeAtPosition(source, i), GetTypeAtPosition(target, i));
        }
        if (!targetRestType.IsNone)
        {
            callback(GetRestTypeAtPosition(source, paramCount, IsConstTypeVariable(targetRestType) && !SomeType(targetRestType, IsMutableArrayLikeType)), targetRestType);
        }
    }

    // checker.ts applyToReturnTypes
    private void ApplyToReturnTypes(Signature source, Signature target, Action<Type, Type> callback)
    {
        var targetTypePredicate = GetTypePredicateOfSignature(target);
        if (targetTypePredicate is not null)
        {
            var sourceTypePredicate = GetTypePredicateOfSignature(source);
            if (sourceTypePredicate is not null && TypePredicateKindsMatch(sourceTypePredicate, targetTypePredicate) && !sourceTypePredicate.Type.IsNone && !targetTypePredicate.Type.IsNone)
            {
                callback(sourceTypePredicate.Type, targetTypePredicate.Type);
                return;
            }
        }
        var targetReturnType = GetReturnTypeOfSignature(target);
        if (CouldContainTypeVariables(targetReturnType))
        {
            callback(GetReturnTypeOfSignature(source), targetReturnType);
        }
    }

    // ---- call expressions ----

    // checker.ts checkCallExpression
    private Type CheckCallExpression(NodeRef node, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var n = node.Node;
        CheckGrammarTypeArguments(node, store.TypeArguments(n));
        var signature = GetResolvedSignature(node, null, checkMode);
        if (signature == resolvingSignature)
        {
            // CheckMode.SkipGenericFunctions is enabled and this is a call to a generic function that
            // returns a function type. We defer checking and return silentNeverType.
            return silentNeverType;
        }
        CheckDeprecatedSignature(signature, node);
        if (store.Kind(store.Expression(n)) == SyntaxKind.SuperKeyword)
        {
            return voidType;
        }
        if (store.Kind(n) == SyntaxKind.NewExpression)
        {
            var declaration = Signatures.Record(signature).Declaration;
            if (!declaration.IsNone && Kind(declaration) is not (SyntaxKind.Constructor or SyntaxKind.ConstructSignature or SyntaxKind.ConstructorType) &&
                !IsJSDocSignatureOfConstructor(declaration) &&
                !Utilities.IsJSDocConstructSignature(StoreOf(declaration), declaration.Node) && !IsJSConstructor(declaration))
            {
                // When resolved signature is a call signature (and not a construct signature) the result type is any
                if (noImplicitAny)
                {
                    Error(node, Diagnostics.new_expression_whose_target_lacks_a_construct_signature_implicitly_has_an_any_type);
                }
                return anyType;
            }
        }
        // In JavaScript files, calls to any identifier 'require' are treated as external module imports
        if (Utilities.IsInJSFile(store, n) && IsCommonJsRequire(node))
        {
            return ResolveExternalModuleTypeByLiteral(At(node, store.Element(store.Arguments(n), 0)));
        }
        var returnType = GetReturnTypeOfSignature(signature);
        // Treat any call to the global 'Symbol' function that is part of a const variable or readonly property
        // as a fresh unique symbol literal type.
        if ((Types.Flags(returnType) & TypeFlags.ESSymbolLike) != 0 && IsSymbolOrSymbolForCall(node))
        {
            return GetESSymbolLikeTypeForNode(At(node, WalkUpParenthesizedExpressions(store, store.Parent(n))));
        }
        // assertion signatures in expression statements (module C7; JavaScript expando symbols: C9)
        if (store.Kind(n) == SyntaxKind.CallExpression && store.QuestionDotToken(n).IsNone && store.Kind(store.Parent(n)) == SyntaxKind.ExpressionStatement &&
            (Types.Flags(returnType) & TypeFlags.Void) != 0 && GetTypePredicateOfSignature(signature) is not null)
        {
            var callee = At(node, store.Expression(n));
            if (!Utilities.IsDottedName(store, callee.Node))
            {
                Error(callee, Diagnostics.Assertions_require_the_call_target_to_be_an_identifier_or_qualified_name);
            }
            else if (GetEffectsSignature(node).IsNone)
            {
                var diagnostic = Error(callee, Diagnostics.Assertions_require_every_name_in_the_call_target_to_be_declared_with_an_explicit_type_annotation);
                GetTypeOfDottedName(callee, diagnostic);
            }
        }
        if (Utilities.IsInJSFile(store, n))
        {
            var jsSymbol = GetSymbolOfExpando(node, allowDeclaration: false);
            var jsExports = jsSymbol.IsNone ? null : ExportsOf(jsSymbol);
            if (jsExports is { Count: > 0 } exportsTable)
            {
                var jsAssignmentType = CreateAnonymousType(jsSymbol, exportsTable);
                Types.Record(jsAssignmentType).ObjectFlags |= ObjectFlags.JSLiteral;
                return GetIntersectionType([returnType, jsAssignmentType]);
            }
        }
        return returnType;
    }

    // isJSDocSignature(declaration) && getJSDocRoot(declaration)?.parent?.kind === SyntaxKind.Constructor
    private bool IsJSDocSignatureOfConstructor(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        if (store.Kind(declaration.Node) != SyntaxKind.JSDocSignature)
        {
            return false;
        }
        var root = GetJSDocRoot(store, declaration.Node);
        return !root.IsNone && store.Kind(store.Parent(root)) == SyntaxKind.Constructor;
    }

    // checker.ts isCommonJsRequire
    private bool IsCommonJsRequire(NodeRef node)
    {
        if (!IsRequireCall(node, requireStringLiteralLikeArgument: true))
        {
            return false;
        }
        // Make sure require is not a local function
        var store = StoreOf(node);
        var expression = store.Expression(node.Node);
        var resolvedRequire = ResolveName(At(node, expression), store.Texts.GetString(store.EscapedText(expression))!, NodeRef.None, SymbolFlags.Value, null, isUse: true);
        if (resolvedRequire == requireSymbol)
        {
            return true;
        }
        if (resolvedRequire.IsNone)
        {
            return false;
        }
        // project includes symbol named 'require' - make sure that it is ambient and local non-alias
        var flags = SymbolFlagsOf(resolvedRequire);
        if ((flags & SymbolFlags.Alias) != 0)
        {
            return false;
        }
        var targetDeclarationKind = (flags & SymbolFlags.Function) != 0 ? SyntaxKind.FunctionDeclaration
            : (flags & SymbolFlags.Variable) != 0 ? SyntaxKind.VariableDeclaration
            : SyntaxKind.Unknown;
        if (targetDeclarationKind != SyntaxKind.Unknown)
        {
            var decl = GetDeclarationOfKind(resolvedRequire, targetDeclarationKind);
            // function/variable declaration should be ambient
            return !decl.IsNone && (StoreOf(decl).Flags(decl.Node) & NodeFlags.Ambient) != 0;
        }
        return false;
    }

    // checker.ts resolveExternalModuleTypeByLiteral
    private Type ResolveExternalModuleTypeByLiteral(NodeRef name)
    {
        var moduleSym = ResolveExternalModuleName(name, name);
        if (!moduleSym.IsNone)
        {
            var resolvedModuleSymbol = ResolveExternalModuleSymbol(moduleSym);
            if (!resolvedModuleSymbol.IsNone)
            {
                return GetTypeOfSymbol(resolvedModuleSymbol);
            }
        }
        return anyType;
    }

    // checker.ts isSymbolOrSymbolForCall
    private bool IsSymbolOrSymbolForCall(NodeRef node)
    {
        var store = StoreOf(node);
        if (store.Kind(node.Node) != SyntaxKind.CallExpression)
        {
            return false;
        }
        var left = store.Expression(node.Node);
        if (store.Kind(left) == SyntaxKind.PropertyAccessExpression && store.Texts.Equals(store.EscapedText(store.Name(left)), "for"))
        {
            left = store.Expression(left);
        }
        if (store.Kind(left) != SyntaxKind.Identifier || !store.Texts.Equals(store.EscapedText(left), "Symbol"))
        {
            return false;
        }
        // make sure `Symbol` is the global symbol
        var globalESSymbol = GetGlobalSymbol("Symbol", SymbolFlags.Value, null);
        return !globalESSymbol.IsNone && globalESSymbol == ResolveName(At(node, left), "Symbol", NodeRef.None, SymbolFlags.Value, null, isUse: false);
    }

    // utilities.ts isRequireCall
    private bool IsRequireCall(NodeRef node, bool requireStringLiteralLikeArgument)
    {
        var store = StoreOf(node);
        if (store.Kind(node.Node) != SyntaxKind.CallExpression)
        {
            return false;
        }
        var expression = store.Expression(node.Node);
        var arguments = store.Arguments(node.Node);
        if (store.Kind(expression) != SyntaxKind.Identifier || !store.Texts.Equals(store.EscapedText(expression), "require") || arguments.IsNone || store.Length(arguments) != 1)
        {
            return false;
        }
        var arg = store.Element(arguments, 0);
        return !requireStringLiteralLikeArgument || Utilities.IsStringLiteralLike(store, arg);
    }

    // checker.ts checkImportCallExpression (the module's ES module symbol and synthetic default imports under noResolve:
    // only ambient modules resolve)
    private Type CheckImportCallExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var arguments = store.Nodes(store.Arguments(node.Node));
        CheckGrammarImportCallExpression(node);
        if (arguments.Length == 0)
        {
            return CreatePromiseReturnType(node, anyType);
        }
        var specifier = At(node, arguments[0]);
        var specifierType = CheckExpressionCached(specifier);
        var optionsType = arguments.Length > 1 ? CheckExpressionCached(At(node, arguments[1])) : Type.None;
        // Even though multiple arguments is grammatically incorrect, type-check extra arguments for completion
        for (var i = 2; i < arguments.Length; ++i)
        {
            CheckExpressionCached(At(node, arguments[i]));
        }
        if ((Types.Flags(specifierType) & TypeFlags.Undefined) != 0 || (Types.Flags(specifierType) & TypeFlags.Null) != 0 || !IsTypeAssignableTo(specifierType, stringType))
        {
            Error(specifier, Diagnostics.Dynamic_import_s_specifier_must_be_of_type_string_but_here_has_type_0, TypeToString(specifierType));
        }
        if (!optionsType.IsNone)
        {
            var importCallOptionsType = GetGlobalImportCallOptionsType(reportErrors: true);
            if (importCallOptionsType != emptyObjectType)
            {
                CheckTypeAssignableTo(optionsType, GetNullableType(importCallOptionsType, TypeFlags.Undefined), At(node, arguments[1]));
            }
            if (store.Kind(arguments[1]) == SyntaxKind.ObjectLiteralExpression)
            {
                foreach (var prop in store.Nodes(store.Properties(arguments[1])))
                {
                    if (store.Kind(prop) == SyntaxKind.PropertyAssignment && store.Kind(store.Name(prop)) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(store.Name(prop)), "assert"))
                    {
                        GrammarErrorOnNode(At(node, store.Name(prop)), Diagnostics.Import_assertions_have_been_replaced_by_import_attributes_Use_with_instead_of_assert);
                        break;
                    }
                }
            }
        }
        // resolveExternalModuleName will return undefined if the moduleReferenceExpression is not a string literal
        var moduleSymbol = ResolveExternalModuleName(node, specifier);
        if (!moduleSymbol.IsNone)
        {
            var esModuleSymbol = ResolveESModuleSymbol(moduleSymbol, specifier, dontResolveAlias: true, suppressInteropError: false);
            if (!esModuleSymbol.IsNone)
            {
                var syntheticType = GetTypeWithSyntheticDefaultOnly(GetTypeOfSymbol(esModuleSymbol), esModuleSymbol, moduleSymbol, specifier);
                if (syntheticType.IsNone)
                {
                    syntheticType = GetTypeWithSyntheticDefaultImportType(GetTypeOfSymbol(esModuleSymbol), esModuleSymbol, moduleSymbol, specifier);
                }
                return CreatePromiseReturnType(node, syntheticType);
            }
        }
        return CreatePromiseReturnType(node, anyType);
    }

    // checker.ts createDefaultPropertyWrapperForModule
    private Type CreateDefaultPropertyWrapperForModule(SymbolRef symbol, SymbolRef originalSymbol, SymbolRef anonymousSymbol = default)
    {
        var memberTable = new SymTable(Names);
        var newSymbol = CreateSymbol(SymbolFlags.Alias, InternalSymbolName.Default);
        TransientSymbols.Record(newSymbol).Parent = originalSymbol;
        var nameType = GetStringLiteralType("default");
        ValueSymbolLinks.Get(newSymbol).NameType = nameType;
        var aliasTarget = ResolveSymbol(symbol, dontResolveAlias: false);
        AliasSymbolLinks.Get(newSymbol).AliasTarget = aliasTarget;
        memberTable.Set(InternalSymbolName.Default, newSymbol);
        return CreateAnonymousType(anonymousSymbol, memberTable, [], [], []);
    }

    // SyntheticDefaultModuleType.defaultOnlyType and .syntheticType (typescript-go: cachedTypes by type)
    private readonly Dictionary<Type, Type> defaultOnlyTypes = [];
    private readonly Dictionary<Type, Type> syntheticDefaultTypes = [];

    // checker.ts getTypeWithSyntheticDefaultOnly
    private Type GetTypeWithSyntheticDefaultOnly(Type type, SymbolRef symbol, SymbolRef originalSymbol, NodeRef moduleSpecifier)
    {
        var hasDefaultOnly = IsOnlyImportableAsDefault(moduleSpecifier);
        if (hasDefaultOnly && !type.IsNone && !IsErrorType(type))
        {
            if (!defaultOnlyTypes.TryGetValue(type, out var result))
            {
                result = CreateDefaultPropertyWrapperForModule(symbol, originalSymbol);
                defaultOnlyTypes[type] = result;
            }
            return result;
        }
        return Type.None;
    }

    // checker.ts getTypeWithSyntheticDefaultImportType
    private Type GetTypeWithSyntheticDefaultImportType(Type type, SymbolRef symbol, SymbolRef originalSymbol, NodeRef moduleSpecifier)
    {
        if (compilerOptions.GetAllowSyntheticDefaultImports() && !type.IsNone && !IsErrorType(type))
        {
            if (!syntheticDefaultTypes.TryGetValue(type, out var result))
            {
                var file = SourceFileDeclarationOf(originalSymbol);
                var hasSyntheticDefault = CanHaveSyntheticDefault(file, originalSymbol, dontResolveAlias: false, moduleSpecifier);
                if (hasSyntheticDefault)
                {
                    var anonymousSymbol = CreateSymbol(SymbolFlags.TypeLiteral, InternalSymbolName.Type);
                    var defaultContainingObject = CreateDefaultPropertyWrapperForModule(symbol, originalSymbol, anonymousSymbol);
                    ValueSymbolLinks.Get(anonymousSymbol).ResolvedType = defaultContainingObject;
                    result = IsValidSpreadType(type) ? GetSpreadType(type, defaultContainingObject, anonymousSymbol, objectFlags: 0, isReadonly: false) : defaultContainingObject;
                }
                else
                {
                    result = type;
                }
                syntheticDefaultTypes[type] = result;
            }
            return result;
        }
        return type;
    }

    // checker.ts getNullableType
    internal Type GetNullableType(Type type, TypeFlags flags)
    {
        var missing = (flags & ~Types.Flags(type)) & (TypeFlags.Undefined | TypeFlags.Null);
        return missing == 0 ? type
            : missing == TypeFlags.Undefined ? GetUnionType([type, undefinedType])
            : missing == TypeFlags.Null ? GetUnionType([type, nullType])
            : GetUnionType([type, undefinedType, nullType]);
    }

    // checker.ts checkTaggedTemplateExpression
    private Type CheckTaggedTemplateExpression(NodeRef node)
    {
        var store = StoreOf(node);
        // checkGrammarTaggedTemplateChain
        if (!store.QuestionDotToken(node.Node).IsNone || (store.Flags(node.Node) & NodeFlags.OptionalChain) != 0)
        {
            GrammarErrorOnNode(At(node, store.Template(node.Node)), Diagnostics.Tagged_template_expressions_are_not_permitted_in_an_optional_chain);
        }
        else
        {
            CheckGrammarTypeArguments(node, store.TypeArguments(node.Node));
        }
        var signature = GetResolvedSignature(node);
        CheckDeprecatedSignature(signature, node);
        return GetReturnTypeOfSignature(signature);
    }

    // checker.ts checkGrammarImportCallExpression (module C8; verbatimModuleSyntax is not an option of Gate C)
    private bool CheckGrammarImportCallExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (store.Kind(store.Expression(n)) == SyntaxKind.MetaProperty)
        {
            if (moduleKind is not (ModuleKind.ESNext or ModuleKind.Preserve))
            {
                return GrammarErrorOnNode(node, Diagnostics.Deferred_imports_are_only_supported_when_the_module_flag_is_set_to_esnext_or_preserve);
            }
        }
        else if (moduleKind == ModuleKind.ES2015)
        {
            return GrammarErrorOnNode(node, Diagnostics.Dynamic_imports_are_only_supported_when_the_module_flag_is_set_to_es2020_es2022_esnext_commonjs_amd_system_umd_node16_node18_node20_or_nodenext);
        }
        if (!store.TypeArguments(n).IsNone)
        {
            return GrammarErrorOnNode(node, Diagnostics.This_use_of_import_is_invalid_import_calls_can_be_written_but_they_must_have_parentheses_and_cannot_have_type_arguments);
        }
        var argumentList = store.Arguments(n);
        var nodeArguments = store.Nodes(argumentList);
        if (!(moduleKind is >= ModuleKind.Node16 and <= ModuleKind.NodeNext) && moduleKind is not (ModuleKind.ESNext or ModuleKind.Preserve))
        {
            // We are allowed trailing comma after proposal-import-assertions.
            CheckGrammarForDisallowedTrailingComma(node, argumentList);
            if (nodeArguments.Length > 1)
            {
                return GrammarErrorOnNode(At(node, nodeArguments[1]), Diagnostics.Dynamic_imports_only_support_a_second_argument_when_the_module_option_is_set_to_esnext_node16_node18_node20_nodenext_or_preserve);
            }
        }
        if (nodeArguments.Length is 0 or > 2)
        {
            return GrammarErrorOnNode(node, Diagnostics.Dynamic_imports_can_only_accept_a_module_specifier_and_an_optional_set_of_attributes_as_arguments);
        }
        // see: parseArgumentOrArrayLiteralElement...we use this function which parse arguments of callExpression to parse specifier for dynamic import.
        // parseArgumentOrArrayLiteralElement allows spread element to be in an argument list which is not allowed as specifier in dynamic import.
        foreach (var argument in nodeArguments)
        {
            if (store.Kind(argument) == SyntaxKind.SpreadElement)
            {
                return GrammarErrorOnNode(At(node, argument), Diagnostics.Argument_of_dynamic_import_cannot_be_spread_element);
            }
        }
        return false;
    }

    // checker.ts checkDeprecatedSignature (module C8)
    private void CheckDeprecatedSignature(Signature signature, NodeRef node)
    {
        ref var record = ref Signatures.Record(signature);
        if ((record.Flags & SignatureFlags.IsSignatureCandidateForOverloadFailure) != 0)
        {
            return;
        }
        var declaration = record.Declaration;
        if (!declaration.IsNone && (StoreOf(declaration).Flags(declaration.Node) & NodeFlags.Deprecated) != 0)
        {
            var suggestionNode = GetDeprecatedSuggestionNode(node);
            var name = TryGetPropertyAccessOrIdentifierToString(GetInvokedExpressionOf(node));
            AddDeprecatedSuggestionWithSignature(suggestionNode, declaration, name, SignatureToString(signature));
        }
    }

    // utilities.ts getInvokedExpression
    private NodeRef GetInvokedExpressionOf(NodeRef node)
    {
        var store = StoreOf(node);
        return store.Kind(node.Node) switch
        {
            SyntaxKind.TaggedTemplateExpression => At(node, store.Tag(node.Node)),
            SyntaxKind.JsxOpeningElement or SyntaxKind.JsxSelfClosingElement => At(node, store.TagName(node.Node)),
            SyntaxKind.BinaryExpression => At(node, store.Right(node.Node)),
            SyntaxKind.JsxOpeningFragment => node,
            _ => At(node, store.Expression(node.Node)),
        };
    }
}
