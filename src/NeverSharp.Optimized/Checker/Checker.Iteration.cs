// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (module C5; the functions are listed below)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (details below).
// Functions (iteration and awaited types: checkRightHandSideOfForOf,
// checkIteratedTypeOrElementType, getIteratedTypeOrElementType, getIterationTypeOfIterable, createIterationTypes,
// combineIterationTypes, getIterationTypesOfIterable(Worker/Cached/Fast/Slow), getAsyncFromSyncIterationTypes,
// reportTypeNotIterableError, getIterationTypesOfIterator(Worker/Cached/Fast/Slow), isIteratorResult,
// getIterationTypesOfIteratorResult, getIterationTypesOfMethod, getIterationTypeOfGeneratorFunctionReturnType,
// getIterationTypesOfGeneratorFunctionReturnType, unwrapReturnType, the global iterable and promise types,
// getAwaitedTypeOfPromise, getPromisedTypeOfPromise, checkAwaitedType, isThenableType, isAwaitedTypeInstantiation,
// unwrapAwaitedType, isAwaitedTypeNeeded, tryCreateAwaitedType, createAwaitedTypeIfNeeded, getAwaitedType,
// getAwaitedTypeNoAlias, createPromiseType, createPromiseLikeType, createPromiseReturnType, createGeneratorType,
// errorAndMaybeSuggestAwait, includeUndefinedInIndexSignature, isReferenceToType)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5. The per-type caches of checker.ts (IterableOrIteratorType,
// PromiseOrAwaitableType) are side tables.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>checker.ts IterationUse.</summary>
[Flags]
internal enum IterationUse
{
    None = 0,
    AllowsSyncIterablesFlag = 1 << 0,
    AllowsAsyncIterablesFlag = 1 << 1,
    AllowsStringInputFlag = 1 << 2,
    ForOfFlag = 1 << 3,
    YieldStarFlag = 1 << 4,
    SpreadFlag = 1 << 5,
    DestructuringFlag = 1 << 6,
    PossiblyOutOfBounds = 1 << 7,

    Element = AllowsSyncIterablesFlag,
    Spread = AllowsSyncIterablesFlag | SpreadFlag,
    Destructuring = AllowsSyncIterablesFlag | DestructuringFlag,
    ForOf = AllowsSyncIterablesFlag | AllowsStringInputFlag | ForOfFlag,
    ForAwaitOf = AllowsSyncIterablesFlag | AllowsAsyncIterablesFlag | AllowsStringInputFlag | ForOfFlag,
    YieldStar = AllowsSyncIterablesFlag | YieldStarFlag,
    AsyncYieldStar = AllowsSyncIterablesFlag | AllowsAsyncIterablesFlag | YieldStarFlag,
    GeneratorReturnType = AllowsSyncIterablesFlag,
    AsyncGeneratorReturnType = AllowsAsyncIterablesFlag,
}

/// <summary>checker.ts IterationTypeKind.</summary>
internal enum IterationTypeKind
{
    Yield,
    Return,
    Next,
}

/// <summary>checker.ts IterationTypes.</summary>
internal sealed class IterationTypes(Type yieldType, Type returnType, Type nextType)
{
    public Type YieldType { get; } = yieldType;
    public Type ReturnType { get; } = returnType;
    public Type NextType { get; } = nextType;

    public Type Get(IterationTypeKind kind) => kind switch
    {
        IterationTypeKind.Yield => YieldType,
        IterationTypeKind.Return => ReturnType,
        _ => NextType,
    };
}

internal sealed partial class Checker
{
    // the cache keys of checker.ts IterableOrIteratorType
    private const int IterationTypesOfIterableKey = 0;
    private const int IterationTypesOfAsyncIterableKey = 1;
    private const int IterationTypesOfIteratorKey = 2;
    private const int IterationTypesOfAsyncIteratorKey = 3;
    private const int IterationTypesOfIteratorResultKey = 4;

    private readonly Dictionary<(Type, int), IterationTypes> iterationTypesCacheByType = [];
    private readonly Dictionary<string, IterationTypes> iterationTypesCache = [];
    private readonly IterationTypes noIterationTypes = new(Type.None, Type.None, Type.None);
    private IterationTypes? anyIterationTypesValue;

    private IterationTypes AnyIterationTypes => anyIterationTypesValue ??= CreateIterationTypes(anyType, anyType, anyType);

    // checker.ts IterationTypesResolver
    private sealed class IterationTypesResolver
    {
        public int IterableCacheKey;
        public int IteratorCacheKey;
        public string IteratorSymbolName = "";
        public Func<bool, Type> GetGlobalIteratorType = null!;
        public Func<bool, Type> GetGlobalIterableType = null!;
        public Func<bool, Type> GetGlobalIterableIteratorType = null!;
        public Func<bool, Type> GetGlobalIteratorObjectType = null!;
        public Func<bool, Type> GetGlobalGeneratorType = null!;
        public Func<Type[]> GetGlobalBuiltinIteratorTypes = null!;
        public Func<Type, NodeRef, Type> ResolveIterationType = null!;
        public DiagnosticMessage MustHaveANextMethodDiagnostic = null!;
        public DiagnosticMessage MustBeAMethodDiagnostic = null!;
        public DiagnosticMessage MustHaveAValueDiagnostic = null!;
    }

    private IterationTypesResolver? asyncIterationTypesResolverValue;
    private IterationTypesResolver? syncIterationTypesResolverValue;

    private IterationTypesResolver AsyncIterationTypesResolver => asyncIterationTypesResolverValue ??= new IterationTypesResolver
    {
        IterableCacheKey = IterationTypesOfAsyncIterableKey,
        IteratorCacheKey = IterationTypesOfAsyncIteratorKey,
        IteratorSymbolName = "asyncIterator",
        GetGlobalIteratorType = r => GetGlobalTypeCached(ref deferredGlobalAsyncIteratorType, "AsyncIterator", 3, r),
        GetGlobalIterableType = GetGlobalAsyncIterableType,
        GetGlobalIterableIteratorType = r => GetGlobalTypeCached(ref deferredGlobalAsyncIterableIteratorType, "AsyncIterableIterator", 3, r),
        GetGlobalIteratorObjectType = r => GetGlobalTypeCached(ref deferredGlobalAsyncIteratorObjectType, "AsyncIteratorObject", 3, r),
        GetGlobalGeneratorType = r => GetGlobalTypeCached(ref deferredGlobalAsyncGeneratorType, "AsyncGenerator", 3, r),
        GetGlobalBuiltinIteratorTypes = () => deferredGlobalBuiltinAsyncIteratorTypes ??= GetGlobalBuiltinTypes(["ReadableStreamAsyncIterator"], 1),
        ResolveIterationType = (type, errorNode) => GetAwaitedType(type, errorNode, Diagnostics.Type_of_await_operand_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member),
        MustHaveANextMethodDiagnostic = Diagnostics.An_async_iterator_must_have_a_next_method,
        MustBeAMethodDiagnostic = Diagnostics.The_0_property_of_an_async_iterator_must_be_a_method,
        MustHaveAValueDiagnostic = Diagnostics.The_type_returned_by_the_0_method_of_an_async_iterator_must_be_a_promise_for_a_type_with_a_value_property,
    };

    private IterationTypesResolver SyncIterationTypesResolver => syncIterationTypesResolverValue ??= new IterationTypesResolver
    {
        IterableCacheKey = IterationTypesOfIterableKey,
        IteratorCacheKey = IterationTypesOfIteratorKey,
        IteratorSymbolName = "iterator",
        GetGlobalIteratorType = r => GetGlobalTypeCached(ref deferredGlobalIteratorType, "Iterator", 3, r),
        GetGlobalIterableType = GetGlobalIterableType,
        GetGlobalIterableIteratorType = r => GetGlobalTypeCached(ref deferredGlobalIterableIteratorType, "IterableIterator", 3, r),
        GetGlobalIteratorObjectType = r => GetGlobalTypeCached(ref deferredGlobalIteratorObjectType, "IteratorObject", 3, r),
        GetGlobalGeneratorType = r => GetGlobalTypeCached(ref deferredGlobalGeneratorType, "Generator", 3, r),
        GetGlobalBuiltinIteratorTypes = () => deferredGlobalBuiltinIteratorTypes ??= GetGlobalBuiltinTypes(["ArrayIterator", "MapIterator", "SetIterator", "StringIterator"], 1),
        ResolveIterationType = (type, _) => type,
        MustHaveANextMethodDiagnostic = Diagnostics.An_iterator_must_have_a_next_method,
        MustBeAMethodDiagnostic = Diagnostics.The_0_property_of_an_iterator_must_be_a_method,
        MustHaveAValueDiagnostic = Diagnostics.The_type_returned_by_the_0_method_of_an_iterator_must_have_a_value_property,
    };

    // ---- global types ----

    private Type deferredGlobalAsyncIterableType;
    private Type deferredGlobalAsyncIteratorType;
    private Type deferredGlobalAsyncIterableIteratorType;
    private Type deferredGlobalAsyncIteratorObjectType;
    private Type deferredGlobalAsyncGeneratorType;
    private Type deferredGlobalIterableType;
    private Type deferredGlobalIteratorType;
    private Type deferredGlobalIterableIteratorType;
    private Type deferredGlobalIteratorObjectType;
    private Type deferredGlobalGeneratorType;
    private Type deferredGlobalIteratorYieldResultType;
    private Type deferredGlobalIteratorReturnResultType;
    private Type deferredGlobalPromiseType;
    private Type deferredGlobalPromiseLikeType;
    private SymbolRef deferredGlobalPromiseConstructorSymbol;
    private SymbolRef deferredGlobalAwaitedSymbol;
    private Type[]? deferredGlobalBuiltinIteratorTypes;
    private Type[]? deferredGlobalBuiltinAsyncIteratorTypes;

    // (deferredGlobalXType ||= getGlobalType(name, arity, reportErrors)) || emptyGenericType
    private Type GetGlobalTypeCached(ref Type cache, string name, int arity, bool reportErrors)
    {
        if (cache.IsNone)
        {
            cache = GetGlobalType(name, arity, reportErrors);
        }
        return cache.IsNone ? emptyGenericType : cache;
    }

    // checker.ts getGlobalBuiltinTypes
    private Type[] GetGlobalBuiltinTypes(string[] typeNames, int arity)
    {
        var types = new List<Type>();
        foreach (var typeName in typeNames)
        {
            var t = GetGlobalType(typeName, arity, reportErrors: false);
            if (!t.IsNone)
            {
                types.Add(t);
            }
        }
        return [.. types];
    }

    internal Type GetGlobalAsyncIterableType(bool reportErrors) => GetGlobalTypeCached(ref deferredGlobalAsyncIterableType, "AsyncIterable", 3, reportErrors);

    internal Type GetGlobalIterableType(bool reportErrors) => GetGlobalTypeCached(ref deferredGlobalIterableType, "Iterable", 3, reportErrors);

    private Type GetGlobalIteratorYieldResultType(bool reportErrors) => GetGlobalTypeCached(ref deferredGlobalIteratorYieldResultType, "IteratorYieldResult", 1, reportErrors);

    private Type GetGlobalIteratorReturnResultType(bool reportErrors) => GetGlobalTypeCached(ref deferredGlobalIteratorReturnResultType, "IteratorReturnResult", 1, reportErrors);

    internal Type GetGlobalPromiseType(bool reportErrors) => GetGlobalTypeCached(ref deferredGlobalPromiseType, "Promise", 1, reportErrors);

    internal Type GetGlobalPromiseLikeType(bool reportErrors) => GetGlobalTypeCached(ref deferredGlobalPromiseLikeType, "PromiseLike", 1, reportErrors);

    // checker.ts getGlobalPromiseConstructorSymbol (getGlobalValueSymbol)
    internal SymbolRef GetGlobalPromiseConstructorSymbol(bool reportErrors)
    {
        if (deferredGlobalPromiseConstructorSymbol.IsNone)
        {
            deferredGlobalPromiseConstructorSymbol = GetGlobalSymbol("Promise", SymbolFlags.Value, reportErrors ? Diagnostics.Cannot_find_global_value_0 : null);
        }
        return deferredGlobalPromiseConstructorSymbol;
    }

    // checker.ts getGlobalAwaitedSymbol (getGlobalTypeAliasSymbol("Awaited", 1, reportErrors))
    private SymbolRef GetGlobalAwaitedSymbol(bool reportErrors)
    {
        if (deferredGlobalAwaitedSymbol.IsNone)
        {
            var symbol = GetGlobalSymbol("Awaited", SymbolFlags.Type, reportErrors ? Diagnostics.Cannot_find_global_type_0 : null);
            if (!symbol.IsNone)
            {
                GetDeclaredTypeOfSymbol(symbol);
                if ((GetTypeAliasTypeParameters(symbol)?.Length ?? 0) != 1)
                {
                    var declaration = DeclarationsOf(symbol)?.Find(d => Kind(d) == SyntaxKind.TypeAliasDeclaration) ?? NodeRef.None;
                    Error(declaration, Diagnostics.Global_type_0_must_have_1_type_parameter_s, SymbolName(symbol), 1);
                    symbol = SymbolRef.None;
                }
            }
            // Only cache `unknownSymbol` if we are reporting errors so that we don't report the error more than once.
            deferredGlobalAwaitedSymbol = !symbol.IsNone ? symbol : reportErrors ? unknownSymbol : SymbolRef.None;
        }
        return deferredGlobalAwaitedSymbol == unknownSymbol ? SymbolRef.None : deferredGlobalAwaitedSymbol;
    }

    // checker.ts isReferenceToType
    private bool IsReferenceToType(Type type, Type target) =>
        !type.IsNone && !target.IsNone && (Types.ObjectFlagsOf(type) & ObjectFlags.Reference) != 0 && Types.Object(type).Target == target;

    // checker.ts isReferenceToSomeType
    private bool IsReferenceToSomeType(Type type, Type[] targets)
    {
        if (type.IsNone || (Types.ObjectFlagsOf(type) & ObjectFlags.Reference) == 0)
        {
            return false;
        }
        var target = Types.Object(type).Target;
        return Array.IndexOf(targets, target) >= 0;
    }

    // checker.ts isTypeAny
    private bool IsTypeAny(Type type) => !type.IsNone && (Types.Flags(type) & TypeFlags.Any) != 0;

    // checker.ts errorAndMaybeSuggestAwait
    private ProgramDiagnostic ErrorAndMaybeSuggestAwait(NodeRef location, bool maybeMissingAwait, DiagnosticMessage message, params object?[] args)
    {
        var diagnostic = Error(location, message, args);
        if (maybeMissingAwait)
        {
            AddRelatedInfo(diagnostic, CreateRelatedForNode(location, Diagnostics.Did_you_forget_to_use_await));
        }
        return diagnostic;
    }

    // checker.ts includeUndefinedInIndexSignature
    private Type IncludeUndefinedInIndexSignature(Type type) =>
        type.IsNone ? type : compilerOptions.NoUncheckedIndexedAccess == true ? GetUnionType([type, missingType]) : type;

    // ---- iteration ----

    /// <summary>checker.ts checkRightHandSideOfForOf.</summary>
    internal Type CheckRightHandSideOfForOf(NodeRef statement)
    {
        var store = StoreOf(statement);
        var use = !store.AwaitModifier(statement.Node).IsNone ? IterationUse.ForAwaitOf : IterationUse.ForOf;
        var expression = At(statement, store.Expression(statement.Node));
        return CheckIteratedTypeOrElementType(use, CheckNonNullExpression(expression), undefinedType, expression);
    }

    /// <summary>checker.ts checkIteratedTypeOrElementType.</summary>
    internal Type CheckIteratedTypeOrElementType(IterationUse use, Type inputType, Type sentType, NodeRef errorNode)
    {
        if (IsTypeAny(inputType))
        {
            return inputType;
        }
        var t = GetIteratedTypeOrElementType(use, inputType, sentType, errorNode, checkAssignability: true);
        return t.IsNone ? anyType : t;
    }

    /// <summary>checker.ts getIteratedTypeOrElementType.</summary>
    internal Type GetIteratedTypeOrElementType(IterationUse use, Type inputType, Type sentType, NodeRef errorNode, bool checkAssignability)
    {
        var allowAsyncIterables = (use & IterationUse.AllowsAsyncIterablesFlag) != 0;
        if (inputType == neverType)
        {
            if (!errorNode.IsNone)
            {
                ReportTypeNotIterableError(errorNode, inputType, allowAsyncIterables);
            }
            return Type.None;
        }
        var iterableExists = GetGlobalIterableType(reportErrors: false) != emptyGenericType;
        var uplevelIteration = languageVersion >= ScriptTarget.ES2015 && iterableExists;
        var downlevelIteration = false; // (!uplevelIteration && compilerOptions.downlevelIteration: the option is not modeled)
        var possibleOutOfBounds = compilerOptions.NoUncheckedIndexedAccess == true && (use & IterationUse.PossiblyOutOfBounds) != 0;
        // Get the iterated type of an `Iterable<T>` or `IterableIterator<T>` only in ES2015
        // or higher, when inside of an async generator or for-await-if, or when
        // downlevelIteration is requested.
        if (uplevelIteration || downlevelIteration || allowAsyncIterables)
        {
            // We only report errors for an invalid iterable type in ES2015 or higher.
            var iterationTypes = GetIterationTypesOfIterable(inputType, use, uplevelIteration ? errorNode : NodeRef.None);
            if (checkAssignability && iterationTypes is not null)
            {
                var diagnostic = (use & IterationUse.ForOfFlag) != 0 ? Diagnostics.Cannot_iterate_value_because_the_next_method_of_its_iterator_expects_type_1_but_for_of_will_always_send_0
                    : (use & IterationUse.SpreadFlag) != 0 ? Diagnostics.Cannot_iterate_value_because_the_next_method_of_its_iterator_expects_type_1_but_array_spread_will_always_send_0
                    : (use & IterationUse.DestructuringFlag) != 0 ? Diagnostics.Cannot_iterate_value_because_the_next_method_of_its_iterator_expects_type_1_but_array_destructuring_will_always_send_0
                    : (use & IterationUse.YieldStarFlag) != 0 ? Diagnostics.Cannot_delegate_iteration_to_value_because_the_next_method_of_its_iterator_expects_type_1_but_the_containing_generator_will_always_send_0
                    : null;
                if (diagnostic is not null)
                {
                    CheckTypeAssignableTo(sentType, iterationTypes.NextType, errorNode, diagnostic);
                }
            }
            if (iterationTypes is not null || uplevelIteration)
            {
                var yieldType = iterationTypes?.YieldType ?? Type.None;
                return possibleOutOfBounds ? IncludeUndefinedInIndexSignature(yieldType) : yieldType;
            }
        }
        var arrayType = inputType;
        var hasStringConstituent = false;
        // If strings are permitted, remove any string-like constituents from the array type.
        // This allows us to find other non-string element types from an array unioned with
        // a string.
        if ((use & IterationUse.AllowsStringInputFlag) != 0)
        {
            if ((Types.Flags(arrayType) & TypeFlags.Union) != 0)
            {
                // After we remove all types that are StringLike, we will know if there was a string constituent
                // based on whether the result of filter is a new array.
                var arrayTypes = Types.Elements(Types.Union(inputType).Types).ToArray();
                var filteredTypes = Array.FindAll(arrayTypes, t => (Types.Flags(t) & TypeFlags.StringLike) == 0);
                if (filteredTypes.Length != arrayTypes.Length)
                {
                    arrayType = GetUnionType(filteredTypes, UnionReduction.Subtype);
                }
            }
            else if ((Types.Flags(arrayType) & TypeFlags.StringLike) != 0)
            {
                arrayType = neverType;
            }
            hasStringConstituent = arrayType != inputType;
            if (hasStringConstituent)
            {
                // Now that we've removed all the StringLike types, if no constituents remain, then the entire
                // arrayOrStringType was a string.
                if ((Types.Flags(arrayType) & TypeFlags.Never) != 0)
                {
                    return possibleOutOfBounds ? IncludeUndefinedInIndexSignature(stringType) : stringType;
                }
            }
        }
        if (!IsArrayLikeType(arrayType))
        {
            if (!errorNode.IsNone)
            {
                // Which error we report depends on whether we allow strings or if there was a
                // string constituent. For example, if the input type is number | string, we
                // want to say that number is not an array type. But if the input was just
                // number and string input is allowed, we want to say that number is not an
                // array type or a string type.
                var allowsStrings = (use & IterationUse.AllowsStringInputFlag) != 0 && !hasStringConstituent;
                var (defaultDiagnostic, maybeMissingAwait) = GetIterationDiagnosticDetails(allowsStrings);
                ErrorAndMaybeSuggestAwait(errorNode, maybeMissingAwait && !GetAwaitedTypeOfPromise(arrayType).IsNone, defaultDiagnostic, TypeToString(arrayType));
            }
            return hasStringConstituent ? possibleOutOfBounds ? IncludeUndefinedInIndexSignature(stringType) : stringType : Type.None;
        }
        var arrayElementType = GetIndexTypeOfType(arrayType, numberType);
        if (hasStringConstituent && !arrayElementType.IsNone)
        {
            // This is just an optimization for the case where arrayOrStringType is string | string[]
            if ((Types.Flags(arrayElementType) & TypeFlags.StringLike) != 0 && compilerOptions.NoUncheckedIndexedAccess != true)
            {
                return stringType;
            }
            return GetUnionType(possibleOutOfBounds ? [arrayElementType, stringType, undefinedType] : [arrayElementType, stringType], UnionReduction.Subtype);
        }
        return (use & IterationUse.PossiblyOutOfBounds) != 0 ? IncludeUndefinedInIndexSignature(arrayElementType) : arrayElementType;

        (DiagnosticMessage, bool) GetIterationDiagnosticDetails(bool allowsStrings)
        {
            if (downlevelIteration)
            {
                return allowsStrings
                    ? (Diagnostics.Type_0_is_not_an_array_type_or_a_string_type_or_does_not_have_a_Symbol_iterator_method_that_returns_an_iterator, true)
                    : (Diagnostics.Type_0_is_not_an_array_type_or_does_not_have_a_Symbol_iterator_method_that_returns_an_iterator, true);
            }
            var yieldType = GetIterationTypeOfIterable(use, IterationTypeKind.Yield, inputType, NodeRef.None);
            if (!yieldType.IsNone)
            {
                return (Diagnostics.Type_0_can_only_be_iterated_through_when_using_the_downlevelIteration_flag_or_with_a_target_of_es2015_or_higher, false);
            }
            var symbol = Types.Symbol(inputType);
            if (!symbol.IsNone && IsES2015OrLaterIterable(NameOf(symbol)))
            {
                return (Diagnostics.Type_0_can_only_be_iterated_through_when_using_the_downlevelIteration_flag_or_with_a_target_of_es2015_or_higher, true);
            }
            return allowsStrings ? (Diagnostics.Type_0_is_not_an_array_type_or_a_string_type, true) : (Diagnostics.Type_0_is_not_an_array_type, true);
        }
    }

    // checker.ts isES2015OrLaterIterable
    private static bool IsES2015OrLaterIterable(string n) => n is "Float32Array" or "Float64Array" or "Int16Array" or "Int32Array" or "Int8Array" or "NodeList"
        or "Uint16Array" or "Uint32Array" or "Uint8Array" or "Uint8ClampedArray";

    // checker.ts getIterationTypeOfIterable
    private Type GetIterationTypeOfIterable(IterationUse use, IterationTypeKind typeKind, Type inputType, NodeRef errorNode)
    {
        if (IsTypeAny(inputType))
        {
            return Type.None;
        }
        var iterationTypes = GetIterationTypesOfIterable(inputType, use, errorNode);
        return iterationTypes?.Get(typeKind) ?? Type.None;
    }

    // checker.ts createIterationTypes (None: the defaults)
    private IterationTypes CreateIterationTypes(Type yieldType = default, Type returnType = default, Type nextType = default)
    {
        if (yieldType.IsNone)
        {
            yieldType = neverType;
        }
        if (returnType.IsNone)
        {
            returnType = neverType;
        }
        if (nextType.IsNone)
        {
            nextType = unknownType;
        }
        // Use the cache only for intrinsic types to keep it small as they are likely to be
        // more frequently created (i.e. `Iterator<number, void, unknown>`).
        const TypeFlags simple = TypeFlags.Any | TypeFlags.Never | TypeFlags.Unknown | TypeFlags.Void | TypeFlags.Undefined;
        if ((Types.Flags(yieldType) & TypeFlags.Intrinsic) != 0 && (Types.Flags(returnType) & simple) != 0 && (Types.Flags(nextType) & simple) != 0)
        {
            var id = GetTypeListId([yieldType, returnType, nextType]);
            if (!iterationTypesCache.TryGetValue(id, out var cached))
            {
                cached = new IterationTypes(yieldType, returnType, nextType);
                iterationTypesCache[id] = cached;
            }
            return cached;
        }
        return new IterationTypes(yieldType, returnType, nextType);
    }

    // checker.ts combineIterationTypes
    private IterationTypes CombineIterationTypes(IEnumerable<IterationTypes?> array)
    {
        List<Type>? yieldTypes = null;
        List<Type>? returnTypes = null;
        List<Type>? nextTypes = null;
        foreach (var iterationTypes in array)
        {
            if (iterationTypes is null || iterationTypes == noIterationTypes)
            {
                continue;
            }
            if (iterationTypes == AnyIterationTypes)
            {
                return AnyIterationTypes;
            }
            (yieldTypes ??= []).Add(iterationTypes.YieldType);
            (returnTypes ??= []).Add(iterationTypes.ReturnType);
            (nextTypes ??= []).Add(iterationTypes.NextType);
        }
        if (yieldTypes is not null || returnTypes is not null || nextTypes is not null)
        {
            return CreateIterationTypes(
                yieldTypes is null ? Type.None : GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(yieldTypes)),
                returnTypes is null ? Type.None : GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(returnTypes)),
                nextTypes is null ? Type.None : GetIntersectionType(nextTypes.ToArray()));
        }
        return noIterationTypes;
    }

    private IterationTypes? GetCachedIterationTypes(Type type, int cacheKey) =>
        iterationTypesCacheByType.TryGetValue((type, cacheKey), out var cached) ? cached : null;

    private IterationTypes SetCachedIterationTypes(Type type, int cacheKey, IterationTypes cachedTypes) =>
        iterationTypesCacheByType[(type, cacheKey)] = cachedTypes;

    /// <summary>checker.ts getIterationTypesOfIterable.</summary>
    internal IterationTypes? GetIterationTypesOfIterable(Type type, IterationUse use, NodeRef errorNode)
    {
        type = GetReducedType(type);
        if (IsTypeAny(type))
        {
            return AnyIterationTypes;
        }
        if ((Types.Flags(type) & TypeFlags.Union) == 0)
        {
            var errorOutputContainer = !errorNode.IsNone ? new ErrorOutputContainer { SkipLogging = true } : null;
            var iterationTypes = GetIterationTypesOfIterableWorker(type, use, errorNode, errorOutputContainer);
            if (iterationTypes == noIterationTypes)
            {
                if (!errorNode.IsNone)
                {
                    var rootDiag = ReportTypeNotIterableError(errorNode, type, (use & IterationUse.AllowsAsyncIterablesFlag) != 0);
                    if (errorOutputContainer?.Errors is { } errors)
                    {
                        AddRelatedInfo(rootDiag, errors.ConvertAll(AsRelatedInformation).ToArray());
                    }
                }
                return null;
            }
            if (errorOutputContainer?.Errors is { Count: > 0 } errs)
            {
                foreach (var diag in errs)
                {
                    diagnostics.Add(diag);
                }
            }
            return iterationTypes;
        }
        var cacheKey = (use & IterationUse.AllowsAsyncIterablesFlag) != 0 ? IterationTypesOfAsyncIterableKey : IterationTypesOfIterableKey;
        var cachedTypes = GetCachedIterationTypes(type, cacheKey);
        if (cachedTypes is not null)
        {
            return cachedTypes == noIterationTypes ? null : cachedTypes;
        }
        List<IterationTypes?>? allIterationTypes = null;
        foreach (var constituent in Types.Elements(Types.Union(type).Types).ToArray())
        {
            var errorOutputContainer = !errorNode.IsNone ? new ErrorOutputContainer() : null;
            var iterationTypes = GetIterationTypesOfIterableWorker(constituent, use, errorNode, errorOutputContainer);
            if (iterationTypes == noIterationTypes)
            {
                if (!errorNode.IsNone)
                {
                    var rootDiag = ReportTypeNotIterableError(errorNode, type, (use & IterationUse.AllowsAsyncIterablesFlag) != 0);
                    if (errorOutputContainer?.Errors is { } errors)
                    {
                        AddRelatedInfo(rootDiag, errors.ConvertAll(AsRelatedInformation).ToArray());
                    }
                }
                SetCachedIterationTypes(type, cacheKey, noIterationTypes);
                return null;
            }
            if (errorOutputContainer?.Errors is { Count: > 0 } errs)
            {
                foreach (var diag in errs)
                {
                    diagnostics.Add(diag);
                }
            }
            (allIterationTypes ??= []).Add(iterationTypes);
        }
        var combined = allIterationTypes is not null ? CombineIterationTypes(allIterationTypes) : noIterationTypes;
        SetCachedIterationTypes(type, cacheKey, combined);
        return combined == noIterationTypes ? null : combined;
    }

    // checker.ts getAsyncFromSyncIterationTypes
    private IterationTypes GetAsyncFromSyncIterationTypes(IterationTypes iterationTypes, NodeRef errorNode)
    {
        if (iterationTypes == noIterationTypes)
        {
            return noIterationTypes;
        }
        if (iterationTypes == AnyIterationTypes)
        {
            return AnyIterationTypes;
        }
        // if we're requesting diagnostics, report errors for a missing `Awaited<T>`.
        if (!errorNode.IsNone)
        {
            GetGlobalAwaitedSymbol(reportErrors: true);
        }
        var yieldType = GetAwaitedType(iterationTypes.YieldType, errorNode);
        var returnType = GetAwaitedType(iterationTypes.ReturnType, errorNode);
        return CreateIterationTypes(yieldType.IsNone ? anyType : yieldType, returnType.IsNone ? anyType : returnType, iterationTypes.NextType);
    }

    // checker.ts getIterationTypesOfIterableWorker
    private IterationTypes GetIterationTypesOfIterableWorker(Type type, IterationUse use, NodeRef errorNode, ErrorOutputContainer? errorOutputContainer)
    {
        if (IsTypeAny(type))
        {
            return AnyIterationTypes;
        }
        // If we are reporting errors and encounter a cached `noIterationTypes`, we should ignore the cached value and continue as if nothing was cached.
        // In addition, we should not cache any new results for this call.
        var noCache = false;
        if ((use & IterationUse.AllowsAsyncIterablesFlag) != 0)
        {
            var iterationTypes = GetCachedIterationTypes(type, AsyncIterationTypesResolver.IterableCacheKey) ?? GetIterationTypesOfIterableFast(type, AsyncIterationTypesResolver);
            if (iterationTypes is not null)
            {
                if (iterationTypes == noIterationTypes && !errorNode.IsNone)
                {
                    // ignore the cached value
                    noCache = true;
                }
                else
                {
                    return (use & IterationUse.ForOfFlag) != 0 ? GetAsyncFromSyncIterationTypes(iterationTypes, errorNode) : iterationTypes;
                }
            }
            iterationTypes = GetIterationTypesOfIterableSlow(type, AsyncIterationTypesResolver, errorNode, errorOutputContainer, noCache);
            if (iterationTypes != noIterationTypes)
            {
                return iterationTypes;
            }
        }
        if ((use & IterationUse.AllowsSyncIterablesFlag) != 0)
        {
            var iterationTypes = GetCachedIterationTypes(type, SyncIterationTypesResolver.IterableCacheKey) ?? GetIterationTypesOfIterableFast(type, SyncIterationTypesResolver);
            if (iterationTypes is not null)
            {
                if (iterationTypes == noIterationTypes && !errorNode.IsNone)
                {
                    // ignore the cached value
                    noCache = true;
                }
                else
                {
                    if ((use & IterationUse.AllowsAsyncIterablesFlag) != 0)
                    {
                        // for a sync iterable in an async context, only use the cached types if they are valid.
                        if (iterationTypes != noIterationTypes)
                        {
                            iterationTypes = GetAsyncFromSyncIterationTypes(iterationTypes, errorNode);
                            return noCache ? iterationTypes : SetCachedIterationTypes(type, IterationTypesOfAsyncIterableKey, iterationTypes);
                        }
                    }
                    else
                    {
                        return iterationTypes;
                    }
                }
            }
            iterationTypes = GetIterationTypesOfIterableSlow(type, SyncIterationTypesResolver, errorNode, errorOutputContainer, noCache);
            if (iterationTypes != noIterationTypes)
            {
                if ((use & IterationUse.AllowsAsyncIterablesFlag) != 0)
                {
                    iterationTypes = GetAsyncFromSyncIterationTypes(iterationTypes, errorNode);
                    return noCache ? iterationTypes : SetCachedIterationTypes(type, IterationTypesOfAsyncIterableKey, iterationTypes);
                }
                return iterationTypes;
            }
        }
        return noIterationTypes;
    }

    // checker.ts getIterationTypesOfIterableFast
    private IterationTypes? GetIterationTypesOfIterableFast(Type type, IterationTypesResolver resolver)
    {
        // As an optimization, if the type is an instantiation of the following global type, then
        // just grab its related type arguments:
        // - `Iterable<T, TReturn, TNext>` or `AsyncIterable<T, TReturn, TNext>`
        // - `IteratorObject<T, TReturn, TNext>` or `AsyncIteratorObject<T, TReturn, TNext>`
        // - `IterableIterator<T, TReturn, TNext>` or `AsyncIterableIterator<T, TReturn, TNext>`
        // - `Generator<T, TReturn, TNext>` or `AsyncGenerator<T, TReturn, TNext>`
        if (IsReferenceToType(type, resolver.GetGlobalIterableType(false)) ||
            IsReferenceToType(type, resolver.GetGlobalIteratorObjectType(false)) ||
            IsReferenceToType(type, resolver.GetGlobalIterableIteratorType(false)) ||
            IsReferenceToType(type, resolver.GetGlobalGeneratorType(false)))
        {
            var typeArguments = GetTypeArguments(type);
            var yieldType = typeArguments[0];
            var returnType = typeArguments[1];
            var nextType = typeArguments[2];
            var resolvedYield = resolver.ResolveIterationType(yieldType, NodeRef.None);
            var resolvedReturn = resolver.ResolveIterationType(returnType, NodeRef.None);
            return SetCachedIterationTypes(type, resolver.IterableCacheKey,
                CreateIterationTypes(resolvedYield.IsNone ? yieldType : resolvedYield, resolvedReturn.IsNone ? returnType : resolvedReturn, nextType));
        }
        // As an optimization, if the type is an instantiation of one of the following global types, then
        // just grab the related type argument:
        // - `ArrayIterator<T>`
        // - `MapIterator<T>`
        // - `SetIterator<T>`
        // - `StringIterator<T>`
        // - `ReadableStreamAsyncIterator<T>`
        if (IsReferenceToSomeType(type, resolver.GetGlobalBuiltinIteratorTypes()))
        {
            var yieldType = GetTypeArguments(type)[0];
            var returnType = GetBuiltinIteratorReturnType();
            var nextType = unknownType;
            var resolvedYield = resolver.ResolveIterationType(yieldType, NodeRef.None);
            var resolvedReturn = resolver.ResolveIterationType(returnType, NodeRef.None);
            return SetCachedIterationTypes(type, resolver.IterableCacheKey,
                CreateIterationTypes(resolvedYield.IsNone ? yieldType : resolvedYield, resolvedReturn.IsNone ? returnType : resolvedReturn, nextType));
        }
        return null;
    }

    // checker.ts getIterationTypesOfIterableSlow
    private IterationTypes GetIterationTypesOfIterableSlow(Type type, IterationTypesResolver resolver, NodeRef errorNode, ErrorOutputContainer? errorOutputContainer, bool noCache)
    {
        var method = GetPropertyOfType(type, GetPropertyNameForKnownSymbolName(resolver.IteratorSymbolName));
        var methodType = !method.IsNone && (SymbolFlagsOf(method) & SymbolFlags.Optional) == 0 ? GetTypeOfSymbol(method) : Type.None;
        if (IsTypeAny(methodType))
        {
            return noCache ? AnyIterationTypes : SetCachedIterationTypes(type, resolver.IterableCacheKey, AnyIterationTypes);
        }
        var allSignatures = !methodType.IsNone ? GetSignaturesOfType(methodType, SignatureKind.Call) : default;
        var validSignatures = allSignatures.FindAll(sig => GetMinArgumentCount(sig) == 0);
        if (validSignatures is not { Count: > 0 })
        {
            if (!errorNode.IsNone && allSignatures is { Count: > 0 })
            {
                CheckTypeAssignableTo(type, resolver.GetGlobalIterableType(true), errorNode, null, null, errorOutputContainer);
            }
            return noCache ? noIterationTypes : SetCachedIterationTypes(type, resolver.IterableCacheKey, noIterationTypes);
        }
        var iteratorType = GetIntersectionType(validSignatures.ConvertAll(GetReturnTypeOfSignature).ToArray());
        var iterationTypes = GetIterationTypesOfIteratorWorker(iteratorType, resolver, errorNode, errorOutputContainer, noCache) ?? noIterationTypes;
        return noCache ? iterationTypes : SetCachedIterationTypes(type, resolver.IterableCacheKey, iterationTypes);
    }

    // checker.ts reportTypeNotIterableError
    private ProgramDiagnostic ReportTypeNotIterableError(NodeRef errorNode, Type type, bool allowAsyncIterables)
    {
        var message = allowAsyncIterables
            ? Diagnostics.Type_0_must_have_a_Symbol_asyncIterator_method_that_returns_an_async_iterator
            : Diagnostics.Type_0_must_have_a_Symbol_iterator_method_that_returns_an_iterator;
        var store = StoreOf(errorNode);
        var parent = store.Parent(errorNode.Node);
        var suggestAwait =
            // for (const x of Promise<...>) or [...Promise<...>]
            !GetAwaitedTypeOfPromise(type).IsNone
            // for (const x of AsyncIterable<...>)
            || (!allowAsyncIterables &&
                store.Kind(parent) == SyntaxKind.ForOfStatement &&
                store.Expression(parent) == errorNode.Node &&
                GetGlobalAsyncIterableType(reportErrors: false) != emptyGenericType &&
                IsTypeAssignableTo(type, CreateTypeFromGenericGlobalType(GetGlobalAsyncIterableType(reportErrors: false), [anyType, anyType, anyType])));
        return ErrorAndMaybeSuggestAwait(errorNode, suggestAwait, message, TypeToString(type));
    }

    // checker.ts getIterationTypesOfIterator
    private IterationTypes? GetIterationTypesOfIterator(Type type, IterationTypesResolver resolver, NodeRef errorNode, ErrorOutputContainer? errorOutputContainer) =>
        GetIterationTypesOfIteratorWorker(type, resolver, errorNode, errorOutputContainer, noCache: false);

    // checker.ts getIterationTypesOfIteratorWorker
    private IterationTypes? GetIterationTypesOfIteratorWorker(Type type, IterationTypesResolver resolver, NodeRef errorNode, ErrorOutputContainer? errorOutputContainer, bool noCache)
    {
        if (IsTypeAny(type))
        {
            return AnyIterationTypes;
        }
        var iterationTypes = GetCachedIterationTypes(type, resolver.IteratorCacheKey) ?? GetIterationTypesOfIteratorFast(type, resolver);
        if (iterationTypes == noIterationTypes && !errorNode.IsNone)
        {
            iterationTypes = null;
            noCache = true;
        }
        iterationTypes ??= GetIterationTypesOfIteratorSlow(type, resolver, errorNode, errorOutputContainer, noCache);
        return iterationTypes == noIterationTypes ? null : iterationTypes;
    }

    // checker.ts getIterationTypesOfIteratorFast
    private IterationTypes? GetIterationTypesOfIteratorFast(Type type, IterationTypesResolver resolver)
    {
        if (IsReferenceToType(type, resolver.GetGlobalIterableIteratorType(false)) ||
            IsReferenceToType(type, resolver.GetGlobalIteratorType(false)) ||
            IsReferenceToType(type, resolver.GetGlobalIteratorObjectType(false)) ||
            IsReferenceToType(type, resolver.GetGlobalGeneratorType(false)))
        {
            var typeArguments = GetTypeArguments(type);
            return SetCachedIterationTypes(type, resolver.IteratorCacheKey, CreateIterationTypes(typeArguments[0], typeArguments[1], typeArguments[2]));
        }
        if (IsReferenceToSomeType(type, resolver.GetGlobalBuiltinIteratorTypes()))
        {
            var yieldType = GetTypeArguments(type)[0];
            return SetCachedIterationTypes(type, resolver.IteratorCacheKey, CreateIterationTypes(yieldType, GetBuiltinIteratorReturnType(), unknownType));
        }
        return null;
    }

    // checker.ts isIteratorResult
    private bool IsIteratorResult(Type type, IterationTypeKind kind)
    {
        // From https://tc39.github.io/ecma262/#sec-iteratorresult-interface:
        // > [done] is the result status of an iterator `next` method call. If the end of the iterator was reached `done` is `true`.
        // > If the end was not reached `done` is `false` and a value is available.
        // > If a `done` property (either own or inherited) does not exist, it is consider to have the value `false`.
        var doneType = GetTypeOfPropertyOfType(type, "done");
        if (doneType.IsNone)
        {
            doneType = falseType;
        }
        return IsTypeAssignableTo(kind == IterationTypeKind.Yield ? falseType : trueType, doneType);
    }

    // checker.ts getIterationTypesOfIteratorResult
    private IterationTypes GetIterationTypesOfIteratorResult(Type type)
    {
        if (IsTypeAny(type))
        {
            return AnyIterationTypes;
        }
        var cachedTypes = GetCachedIterationTypes(type, IterationTypesOfIteratorResultKey);
        if (cachedTypes is not null)
        {
            return cachedTypes;
        }
        // As an optimization, if the type is an instantiation of one of the global `IteratorYieldResult<T>`
        // or `IteratorReturnResult<TReturn>` types, then just grab its type argument.
        if (IsReferenceToType(type, GetGlobalIteratorYieldResultType(reportErrors: false)))
        {
            var yieldType = GetTypeArguments(type)[0];
            return SetCachedIterationTypes(type, IterationTypesOfIteratorResultKey, CreateIterationTypes(yieldType));
        }
        if (IsReferenceToType(type, GetGlobalIteratorReturnResultType(reportErrors: false)))
        {
            var returnType = GetTypeArguments(type)[0];
            return SetCachedIterationTypes(type, IterationTypesOfIteratorResultKey, CreateIterationTypes(Type.None, returnType));
        }
        // Choose any constituents that can produce the requested iteration type.
        var yieldIteratorResult = FilterType(type, t => IsIteratorResult(t, IterationTypeKind.Yield));
        var yieldTypeOfResult = yieldIteratorResult != neverType ? GetTypeOfPropertyOfType(yieldIteratorResult, "value") : Type.None;
        var returnIteratorResult = FilterType(type, t => IsIteratorResult(t, IterationTypeKind.Return));
        var returnTypeOfResult = returnIteratorResult != neverType ? GetTypeOfPropertyOfType(returnIteratorResult, "value") : Type.None;
        if (yieldTypeOfResult.IsNone && returnTypeOfResult.IsNone)
        {
            return SetCachedIterationTypes(type, IterationTypesOfIteratorResultKey, noIterationTypes);
        }
        // From https://tc39.github.io/ecma262/#sec-iteratorresult-interface
        // > ... If the iterator does not have a return value, `value` is `undefined`. In that case, the
        // > `value` property may be absent from the conforming object if it does not inherit an explicit
        // > `value` property.
        return SetCachedIterationTypes(type, IterationTypesOfIteratorResultKey, CreateIterationTypes(yieldTypeOfResult, returnTypeOfResult.IsNone ? voidType : returnTypeOfResult));
    }

    // checker.ts getIterationTypesOfMethod
    private IterationTypes? GetIterationTypesOfMethod(Type type, IterationTypesResolver resolver, string methodName, NodeRef errorNode, ErrorOutputContainer? errorOutputContainer)
    {
        var method = GetPropertyOfType(type, methodName);
        // Ignore 'return' or 'throw' if they are missing.
        if (method.IsNone && methodName != "next")
        {
            return null;
        }
        var methodType = !method.IsNone && !(methodName == "next" && (SymbolFlagsOf(method) & SymbolFlags.Optional) != 0)
            ? methodName == "next" ? GetTypeOfSymbol(method) : GetTypeWithFacts(GetTypeOfSymbol(method), TypeFacts.NEUndefinedOrNull)
            : Type.None;
        if (IsTypeAny(methodType))
        {
            return AnyIterationTypes;
        }
        // Both async and non-async iterators *must* have a `next` method.
        var methodSignatures = !methodType.IsNone ? GetSignaturesOfType(methodType, SignatureKind.Call) : [];
        if (methodSignatures.Count == 0)
        {
            if (!errorNode.IsNone)
            {
                var diagnostic = methodName == "next" ? resolver.MustHaveANextMethodDiagnostic : resolver.MustBeAMethodDiagnostic;
                if (errorOutputContainer is not null)
                {
                    (errorOutputContainer.Errors ??= []).Add(CreateDiagnosticForNode(errorNode, diagnostic, methodName));
                }
                else
                {
                    Error(errorNode, diagnostic, methodName);
                }
            }
            return methodName == "next" ? noIterationTypes : null;
        }
        // If the method signature comes exclusively from the global iterator or generator type,
        // create iteration types from its type arguments like `getIterationTypesOfIteratorFast`
        // does (so as to remove `undefined` from the next and return types).
        if (!Types.Symbol(methodType).IsNone && methodSignatures.Count == 1)
        {
            var globalGeneratorType = resolver.GetGlobalGeneratorType(false);
            var globalIteratorType = resolver.GetGlobalIteratorType(false);
            var generatorSymbol = Types.Symbol(globalGeneratorType);
            var iteratorSymbol = Types.Symbol(globalIteratorType);
            var methodSymbol = Types.Symbol(methodType);
            var isGeneratorMethod = !generatorSymbol.IsNone && MembersOf(generatorSymbol) is { } generatorMembers && generatorMembers.Get(methodName) == methodSymbol;
            var isIteratorMethod = !isGeneratorMethod && !iteratorSymbol.IsNone && MembersOf(iteratorSymbol) is { } iteratorMembers && iteratorMembers.Get(methodName) == methodSymbol;
            if (isGeneratorMethod || isIteratorMethod)
            {
                var globalType = isGeneratorMethod ? globalGeneratorType : globalIteratorType;
                var mapper = Types.Object(methodType).Mapper;
                var typeParameters = Types.Elements(Types.Object(globalType).TypeParameters).ToArray();
                return CreateIterationTypes(
                    GetMappedType(typeParameters[0], mapper),
                    GetMappedType(typeParameters[1], mapper),
                    methodName == "next" ? GetMappedType(typeParameters[2], mapper) : Type.None);
            }
        }
        // Extract the first parameter and return type of each signature.
        List<Type>? methodParameterTypes = null;
        List<Type>? methodReturnTypes = null;
        foreach (var signature in methodSignatures)
        {
            if (methodName != "throw" && ParametersOf(signature).Length > 0)
            {
                (methodParameterTypes ??= []).Add(GetTypeAtPosition(signature, 0));
            }
            (methodReturnTypes ??= []).Add(GetReturnTypeOfSignature(signature));
        }
        // Resolve the *next* or *return* type from the first parameter of a `next()` or
        // `return()` method, respectively.
        List<Type>? returnTypes = null;
        var nextType = Type.None;
        if (methodName != "throw")
        {
            var methodParameterType = methodParameterTypes is not null ? GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(methodParameterTypes)) : unknownType;
            if (methodName == "next")
            {
                // The value of `next(value)` is *not* awaited by async generators
                nextType = methodParameterType;
            }
            else if (methodName == "return")
            {
                // The value of `return(value)` *is* awaited by async generators
                var resolvedMethodParameterType = resolver.ResolveIterationType(methodParameterType, errorNode);
                (returnTypes ??= []).Add(resolvedMethodParameterType.IsNone ? anyType : resolvedMethodParameterType);
            }
        }
        // Resolve the *yield* and *return* types from the return type of the method (i.e. `IteratorResult`)
        Type yieldType;
        var methodReturnType = methodReturnTypes is not null ? GetIntersectionType(methodReturnTypes.ToArray()) : neverType;
        var resolvedMethodReturnType = resolver.ResolveIterationType(methodReturnType, errorNode);
        if (resolvedMethodReturnType.IsNone)
        {
            resolvedMethodReturnType = anyType;
        }
        var iterationTypes = GetIterationTypesOfIteratorResult(resolvedMethodReturnType);
        if (iterationTypes == noIterationTypes)
        {
            if (!errorNode.IsNone)
            {
                if (errorOutputContainer is not null)
                {
                    (errorOutputContainer.Errors ??= []).Add(CreateDiagnosticForNode(errorNode, resolver.MustHaveAValueDiagnostic, methodName));
                }
                else
                {
                    Error(errorNode, resolver.MustHaveAValueDiagnostic, methodName);
                }
            }
            yieldType = anyType;
            (returnTypes ??= []).Add(anyType);
        }
        else
        {
            yieldType = iterationTypes.YieldType;
            (returnTypes ??= []).Add(iterationTypes.ReturnType);
        }
        return CreateIterationTypes(yieldType, GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(returnTypes)), nextType);
    }

    // checker.ts getIterationTypesOfIteratorSlow
    private IterationTypes GetIterationTypesOfIteratorSlow(Type type, IterationTypesResolver resolver, NodeRef errorNode, ErrorOutputContainer? errorOutputContainer, bool noCache)
    {
        var iterationTypes = CombineIterationTypes([
            GetIterationTypesOfMethod(type, resolver, "next", errorNode, errorOutputContainer),
            GetIterationTypesOfMethod(type, resolver, "return", errorNode, errorOutputContainer),
            GetIterationTypesOfMethod(type, resolver, "throw", errorNode, errorOutputContainer),
        ]);
        return noCache ? iterationTypes : SetCachedIterationTypes(type, resolver.IteratorCacheKey, iterationTypes);
    }

    /// <summary>checker.ts getIterationTypeOfGeneratorFunctionReturnType.</summary>
    internal Type GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind kind, Type returnType, bool isAsyncGenerator)
    {
        if (IsTypeAny(returnType))
        {
            return Type.None;
        }
        var iterationTypes = GetIterationTypesOfGeneratorFunctionReturnType(returnType, isAsyncGenerator);
        return iterationTypes?.Get(kind) ?? Type.None;
    }

    /// <summary>checker.ts getIterationTypesOfGeneratorFunctionReturnType.</summary>
    internal IterationTypes? GetIterationTypesOfGeneratorFunctionReturnType(Type type, bool isAsyncGenerator)
    {
        if (IsTypeAny(type))
        {
            return AnyIterationTypes;
        }
        var use = isAsyncGenerator ? IterationUse.AsyncGeneratorReturnType : IterationUse.GeneratorReturnType;
        var resolver = isAsyncGenerator ? AsyncIterationTypesResolver : SyncIterationTypesResolver;
        return GetIterationTypesOfIterable(type, use, NodeRef.None) ?? GetIterationTypesOfIterator(type, resolver, NodeRef.None, null);
    }

    /// <summary>checker.ts unwrapReturnType.</summary>
    internal Type UnwrapReturnType(Type returnType, FunctionFlags functionFlags)
    {
        var isGenerator = (functionFlags & FunctionFlags.Generator) != 0;
        var isAsync = (functionFlags & FunctionFlags.Async) != 0;
        if (isGenerator)
        {
            var returnIterationType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, returnType, isAsync);
            if (returnIterationType.IsNone)
            {
                return errorType;
            }
            return isAsync ? GetAwaitedTypeNoAlias(UnwrapAwaitedType(returnIterationType)) : returnIterationType;
        }
        if (isAsync)
        {
            var awaited = GetAwaitedTypeNoAlias(returnType);
            return awaited.IsNone ? errorType : awaited;
        }
        return returnType;
    }

    // ---- awaited types ----

    private readonly Dictionary<Type, Type> promisedTypeOfPromise = [];
    private readonly Dictionary<Type, Type> awaitedTypeOfType = [];
    private readonly List<int> awaitedTypeStack = [];

    /// <summary>checker.ts getAwaitedTypeOfPromise.</summary>
    internal Type GetAwaitedTypeOfPromise(Type type, NodeRef errorNode = default, DiagnosticMessage? diagnosticMessage = null, params object?[] args)
    {
        var promisedType = GetPromisedTypeOfPromise(type, errorNode);
        return promisedType.IsNone ? Type.None : GetAwaitedType(promisedType, errorNode, diagnosticMessage, args);
    }

    /// <summary>checker.ts getPromisedTypeOfPromise.</summary>
    internal Type GetPromisedTypeOfPromise(Type type, NodeRef errorNode = default) => GetPromisedTypeOfPromise(type, errorNode, out _);

    private Type GetPromisedTypeOfPromise(Type type, NodeRef errorNode, out Type thisTypeForErrorOut)
    {
        thisTypeForErrorOut = Type.None;
        if (IsTypeAny(type))
        {
            return Type.None;
        }
        if (promisedTypeOfPromise.TryGetValue(type, out var cached))
        {
            return cached;
        }
        if (IsReferenceToType(type, GetGlobalPromiseType(reportErrors: false)))
        {
            return promisedTypeOfPromise[type] = GetTypeArguments(type)[0];
        }
        // primitives with a `{ then() }` won't be unwrapped/adopted.
        if (AllTypesAssignableToKind(GetBaseConstraintOrType(type), TypeFlags.Primitive | TypeFlags.Never))
        {
            return Type.None;
        }
        var thenFunction = GetTypeOfPropertyOfType(type, "then");
        if (IsTypeAny(thenFunction))
        {
            return Type.None;
        }
        var thenSignatures = !thenFunction.IsNone ? GetSignaturesOfType(thenFunction, SignatureKind.Call) : [];
        if (thenSignatures.Count == 0)
        {
            if (!errorNode.IsNone)
            {
                Error(errorNode, Diagnostics.A_promise_must_have_a_then_method);
            }
            return Type.None;
        }
        var thisTypeForError = Type.None;
        List<Signature>? candidates = null;
        foreach (var thenSignature in thenSignatures)
        {
            var thisType = GetThisTypeOfSignature(thenSignature);
            if (!thisType.IsNone && thisType != voidType && !IsTypeRelatedTo(type, thisType, subtypeRelation))
            {
                thisTypeForError = thisType;
            }
            else
            {
                (candidates ??= []).Add(thenSignature);
            }
        }
        if (candidates is null)
        {
            thisTypeForErrorOut = thisTypeForError;
            if (!errorNode.IsNone)
            {
                Error(errorNode, Diagnostics.The_this_context_of_type_0_is_not_assignable_to_method_s_this_of_type_1, TypeToString(type), TypeToString(thisTypeForError));
            }
            return Type.None;
        }
        var onfulfilledParameterType = GetTypeWithFacts(GetUnionType(candidates.ConvertAll(GetTypeOfFirstParameterOfSignature).ToArray()), TypeFacts.NEUndefinedOrNull);
        if (IsTypeAny(onfulfilledParameterType))
        {
            return Type.None;
        }
        var onfulfilledParameterSignatures = GetSignaturesOfType(onfulfilledParameterType, SignatureKind.Call);
        if (onfulfilledParameterSignatures.Count == 0)
        {
            if (!errorNode.IsNone)
            {
                Error(errorNode, Diagnostics.The_first_parameter_of_the_then_method_of_a_promise_must_be_a_callback);
            }
            return Type.None;
        }
        return promisedTypeOfPromise[type] = GetUnionType(onfulfilledParameterSignatures.ConvertAll(GetTypeOfFirstParameterOfSignature).ToArray(), UnionReduction.Subtype);
    }

    // checker.ts getTypeOfFirstParameterOfSignature
    private Type GetTypeOfFirstParameterOfSignature(Signature signature) => GetTypeOfFirstParameterOfSignatureWithFallback(signature, neverType);

    /// <summary>checker.ts checkAwaitedType.</summary>
    internal Type CheckAwaitedType(Type type, bool withAlias, NodeRef errorNode, DiagnosticMessage diagnosticMessage, params object?[] args)
    {
        var awaitedType = withAlias ? GetAwaitedType(type, errorNode, diagnosticMessage, args) : GetAwaitedTypeNoAlias(type, errorNode, diagnosticMessage, args);
        return awaitedType.IsNone ? errorType : awaitedType;
    }

    // checker.ts isThenableType
    private bool IsThenableType(Type type)
    {
        if (AllTypesAssignableToKind(GetBaseConstraintOrType(type), TypeFlags.Primitive | TypeFlags.Never))
        {
            // primitive types cannot be considered "thenable" since they are not objects.
            return false;
        }
        var thenFunction = GetTypeOfPropertyOfType(type, "then");
        return !thenFunction.IsNone && GetSignaturesOfType(GetTypeWithFacts(thenFunction, TypeFacts.NEUndefinedOrNull), SignatureKind.Call).Count > 0;
    }

    // checker.ts isAwaitedTypeInstantiation
    private bool IsAwaitedTypeInstantiation(Type type)
    {
        if ((Types.Flags(type) & TypeFlags.Conditional) != 0)
        {
            var awaitedSymbol = GetGlobalAwaitedSymbol(reportErrors: false);
            return !awaitedSymbol.IsNone && Types.Record(type).AliasSymbol == awaitedSymbol && Types.Record(type).AliasTypeArguments.Count == 1;
        }
        return false;
    }

    /// <summary>checker.ts unwrapAwaitedType.</summary>
    internal Type UnwrapAwaitedType(Type type) =>
        (Types.Flags(type) & TypeFlags.Union) != 0 ? MapType(type, UnwrapAwaitedType)
        : IsAwaitedTypeInstantiation(type) ? Types.Elements(Types.Record(type).AliasTypeArguments)[0]
        : type;

    // checker.ts isAwaitedTypeNeeded
    private bool IsAwaitedTypeNeeded(Type type)
    {
        // If this is already an `Awaited<T>`, we shouldn't wrap it. This helps to avoid `Awaited<Awaited<T>>` in higher-order.
        if (IsTypeAny(type) || IsAwaitedTypeInstantiation(type))
        {
            return false;
        }
        // We only need `Awaited<T>` if `T` contains possibly non-primitive types.
        if (IsGenericObjectType(type))
        {
            var baseConstraint = GetBaseConstraintOfType(type);
            // We only need `Awaited<T>` if `T` is a type variable that has no base constraint, or the base constraint of `T` is `any`, `unknown`, `{}`, `object`,
            // or is promise-like.
            if (!baseConstraint.IsNone
                ? (Types.Flags(baseConstraint) & TypeFlags.AnyOrUnknown) != 0 || IsEmptyObjectType(baseConstraint) || SomeType(baseConstraint, IsThenableType)
                : MaybeTypeOfKind(type, TypeFlags.TypeVariable))
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts tryCreateAwaitedType
    private Type TryCreateAwaitedType(Type type)
    {
        // Nothing to do if `Awaited<T>` doesn't exist
        var awaitedSymbol = GetGlobalAwaitedSymbol(reportErrors: true);
        if (!awaitedSymbol.IsNone)
        {
            // Unwrap unions that may contain `Awaited<T>`, otherwise its possible to manufacture an `Awaited<Awaited<T> | U>` where
            // an `Awaited<T | U>` would suffice.
            return GetTypeAliasInstantiation(awaitedSymbol, [UnwrapAwaitedType(type)]);
        }
        return Type.None;
    }

    // checker.ts createAwaitedTypeIfNeeded
    private Type CreateAwaitedTypeIfNeeded(Type type)
    {
        if (IsAwaitedTypeNeeded(type))
        {
            var awaited = TryCreateAwaitedType(type);
            return awaited.IsNone ? type : awaited;
        }
        return type;
    }

    /// <summary>checker.ts getAwaitedType.</summary>
    internal Type GetAwaitedType(Type type, NodeRef errorNode = default, DiagnosticMessage? diagnosticMessage = null, params object?[] args)
    {
        var awaitedType = GetAwaitedTypeNoAlias(type, errorNode, diagnosticMessage, args);
        return awaitedType.IsNone ? Type.None : CreateAwaitedTypeIfNeeded(awaitedType);
    }

    /// <summary>checker.ts getAwaitedTypeNoAlias.</summary>
    internal Type GetAwaitedTypeNoAlias(Type type) => GetAwaitedTypeNoAlias(type, NodeRef.None, null);

    internal Type GetAwaitedTypeNoAlias(Type type, NodeRef errorNode, DiagnosticMessage? diagnosticMessage, params object?[] args)
    {
        if (IsTypeAny(type))
        {
            return type;
        }
        // If this is already an `Awaited<T>`, just return it. This avoids `Awaited<Awaited<T>>` in higher-order
        if (IsAwaitedTypeInstantiation(type))
        {
            return type;
        }
        // If we've already cached an awaited type, return a possible `Awaited<T>` for it.
        if (awaitedTypeOfType.TryGetValue(type, out var cached))
        {
            return cached;
        }
        // For a union, get a union of the awaited types of each constituent.
        if ((Types.Flags(type) & TypeFlags.Union) != 0)
        {
            if (awaitedTypeStack.LastIndexOf(type.Id) >= 0)
            {
                if (!errorNode.IsNone)
                {
                    Error(errorNode, Diagnostics.Type_is_referenced_directly_or_indirectly_in_the_fulfillment_callback_of_its_own_then_method);
                }
                return Type.None;
            }
            awaitedTypeStack.Add(type.Id);
            var mapped = MapType(type, constituentType => GetAwaitedTypeNoAlias(constituentType, errorNode, diagnosticMessage, args));
            awaitedTypeStack.RemoveAt(awaitedTypeStack.Count - 1);
            return awaitedTypeOfType[type] = mapped;
        }
        // If `type` is generic and should be wrapped in `Awaited<T>`, return it.
        if (IsAwaitedTypeNeeded(type))
        {
            return awaitedTypeOfType[type] = type;
        }
        var promisedType = GetPromisedTypeOfPromise(type, NodeRef.None, out var thisTypeForErrorOut);
        if (!promisedType.IsNone)
        {
            if (type.Id == promisedType.Id || awaitedTypeStack.LastIndexOf(promisedType.Id) >= 0)
            {
                // Verify that we don't have a bad actor in the form of a promise whose
                // promised type is the same as the promise type, or a mutually recursive
                // promise. If so, we return undefined as we cannot guess the shape. If this
                // were the actual case in the JavaScript, this Promise would never resolve.
                if (!errorNode.IsNone)
                {
                    Error(errorNode, Diagnostics.Type_is_referenced_directly_or_indirectly_in_the_fulfillment_callback_of_its_own_then_method);
                }
                return Type.None;
            }
            // Keep track of the type we're about to unwrap to avoid bad recursive promise types.
            // See the comments above for more information.
            awaitedTypeStack.Add(type.Id);
            var awaitedType = GetAwaitedTypeNoAlias(promisedType, errorNode, diagnosticMessage, args);
            awaitedTypeStack.RemoveAt(awaitedTypeStack.Count - 1);
            if (awaitedType.IsNone)
            {
                return Type.None;
            }
            return awaitedTypeOfType[type] = awaitedType;
        }
        // The type was not a promise, so it could not be unwrapped any further.
        // As long as the type does not have a callable "then" property, it is
        // safe to return the type; otherwise, an error is reported and we return
        // undefined.
        if (IsThenableType(type))
        {
            if (!errorNode.IsNone)
            {
                DiagnosticMessageChain? chain = null;
                if (!thisTypeForErrorOut.IsNone)
                {
                    chain = ChainDiagnosticMessages(chain, Diagnostics.The_this_context_of_type_0_is_not_assignable_to_method_s_this_of_type_1, TypeToString(type), TypeToString(thisTypeForErrorOut));
                }
                chain = ChainDiagnosticMessages(chain, diagnosticMessage!, args);
                diagnostics.Add(CreateDiagnosticForNodeFromMessageChain(errorNode, chain));
            }
            return Type.None;
        }
        return awaitedTypeOfType[type] = type;
    }

    // ---- promise and generator types ----

    /// <summary>checker.ts createPromiseType.</summary>
    internal Type CreatePromiseType(Type promisedType)
    {
        // creates a `Promise<T>` type where `T` is the promisedType argument
        var globalPromiseType = GetGlobalPromiseType(reportErrors: true);
        if (globalPromiseType != emptyGenericType)
        {
            // if the promised type is itself a promise, get the underlying type; otherwise, fallback to the promised type
            // Unwrap an `Awaited<T>` to `T` to improve inference.
            var awaited = GetAwaitedTypeNoAlias(UnwrapAwaitedType(promisedType));
            return CreateTypeReference(globalPromiseType, [awaited.IsNone ? unknownType : awaited]);
        }
        return unknownType;
    }

    /// <summary>checker.ts createPromiseLikeType.</summary>
    internal Type CreatePromiseLikeType(Type promisedType)
    {
        var globalPromiseLikeType = GetGlobalPromiseLikeType(reportErrors: true);
        if (globalPromiseLikeType != emptyGenericType)
        {
            var awaited = GetAwaitedTypeNoAlias(UnwrapAwaitedType(promisedType));
            return CreateTypeReference(globalPromiseLikeType, [awaited.IsNone ? unknownType : awaited]);
        }
        return unknownType;
    }

    /// <summary>checker.ts createPromiseReturnType.</summary>
    internal Type CreatePromiseReturnType(NodeRef func, Type promisedType)
    {
        var promiseType = CreatePromiseType(promisedType);
        var isImportCall = Kind(func) == SyntaxKind.CallExpression;
        if (promiseType == unknownType)
        {
            Error(func, isImportCall
                ? Diagnostics.A_dynamic_import_call_returns_a_Promise_Make_sure_you_have_a_declaration_for_Promise_or_include_ES2015_in_your_lib_option
                : Diagnostics.An_async_function_or_method_must_return_a_Promise_Make_sure_you_have_a_declaration_for_Promise_or_include_ES2015_in_your_lib_option);
            return errorType;
        }
        if (GetGlobalPromiseConstructorSymbol(reportErrors: true).IsNone)
        {
            Error(func, isImportCall
                ? Diagnostics.A_dynamic_import_call_in_ES5_requires_the_Promise_constructor_Make_sure_you_have_a_declaration_for_the_Promise_constructor_or_include_ES2015_in_your_lib_option
                : Diagnostics.An_async_function_or_method_in_ES5_requires_the_Promise_constructor_Make_sure_you_have_a_declaration_for_the_Promise_constructor_or_include_ES2015_in_your_lib_option);
        }
        return promiseType;
    }

    /// <summary>checker.ts createGeneratorType.</summary>
    internal Type CreateGeneratorType(Type yieldType, Type returnType, Type nextType, bool isAsyncGenerator)
    {
        var resolver = isAsyncGenerator ? AsyncIterationTypesResolver : SyncIterationTypesResolver;
        var globalGeneratorType = resolver.GetGlobalGeneratorType(false);
        yieldType = resolver.ResolveIterationType(yieldType, NodeRef.None) is var y && !y.IsNone ? y : unknownType;
        returnType = resolver.ResolveIterationType(returnType, NodeRef.None) is var r && !r.IsNone ? r : unknownType;
        if (globalGeneratorType == emptyGenericType)
        {
            // Fall back to the global IterableIterator type.
            var globalIterableIteratorType = resolver.GetGlobalIterableIteratorType(false);
            if (globalIterableIteratorType != emptyGenericType)
            {
                return CreateTypeFromGenericGlobalType(globalIterableIteratorType, [yieldType, returnType, nextType]);
            }
            // The global Generator type doesn't exist, so report an error
            resolver.GetGlobalIterableIteratorType(true);
            return emptyObjectType;
        }
        return CreateTypeFromGenericGlobalType(globalGeneratorType, [yieldType, returnType, nextType]);
    }
}
