// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (the relations, their caches and entry points, isTypeRelatedTo, variances)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/relater.go (Relation, Relater)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C4. checkTypeRelatedTo's closure state is the Relater object of
// Checker.Relater.cs (typescript-go's Relater). Relation keys are strings as in TypeScript (type ids are the handles).
// Also: isSimpleTypeRelatedTo, isEnumTypeRelatedTo, getNormalizedType, compareSignaturesRelated,
// compareSignaturesIdentical, compareProperties, getVariances, getRelationKey, isDeeplyNestedType, isWeakType.

using System.Globalization;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>types.ts Ternary (as int constants so that the results combine with &amp; as in TypeScript).</summary>
internal static class Ternary
{
    internal const int False = 0;
    internal const int Unknown = 1;
    internal const int Maybe = 3;
    internal const int True = -1;
}

/// <summary>types.ts RelationComparisonResult.</summary>
[Flags]
internal enum RelationComparisonResult
{
    None = 0,
    Succeeded = 1 << 0,
    Failed = 1 << 1,
    ReportsUnmeasurable = 1 << 3,
    ReportsUnreliable = 1 << 4,
    ReportsMask = ReportsUnmeasurable | ReportsUnreliable,
    ComplexityOverflow = 1 << 5,
    StackDepthOverflow = 1 << 6,
    Overflow = ComplexityOverflow | StackDepthOverflow,
}

/// <summary>checker.ts IntersectionState.</summary>
[Flags]
internal enum IntersectionState
{
    None = 0,
    Source = 1 << 0,
    Target = 1 << 1,
}

/// <summary>checker.ts RecursionFlags.</summary>
[Flags]
internal enum RecursionFlags
{
    None = 0,
    Source = 1 << 0,
    Target = 1 << 1,
    Both = Source | Target,
}

/// <summary>checker.ts ExpandingFlags.</summary>
[Flags]
internal enum ExpandingFlags
{
    None = 0,
    Source = 1,
    Target = 1 << 1,
    Both = Source | Target,
}

/// <summary>checker.ts SignatureCheckMode.</summary>
[Flags]
internal enum SignatureCheckMode
{
    None = 0,
    BivariantCallback = 1 << 0,
    StrictCallback = 1 << 1,
    IgnoreReturnTypes = 1 << 2,
    StrictArity = 1 << 3,
    StrictTopSignature = 1 << 4,
    Callback = BivariantCallback | StrictCallback,
}

/// <summary>A relation and its cache (checker.ts: Map&lt;string, RelationComparisonResult&gt;; relater.go Relation).</summary>
internal sealed class Relation(string name)
{
    internal readonly string Name = name;
    internal readonly Dictionary<string, RelationComparisonResult> Results = new(StringComparer.Ordinal);

    public override string ToString() => Name;
}

/// <summary>checker.ts ErrorOutputContainer.</summary>
internal sealed class ErrorOutputContainer
{
    internal List<ProgramDiagnostic>? Errors;
    internal bool SkipLogging;
}

internal sealed partial class Checker
{
    private readonly Relation subtypeRelation = new("subtype");
    private readonly Relation strictSubtypeRelation = new("strictSubtype");
    private readonly Relation assignableRelation = new("assignable");
    private readonly Relation comparableRelation = new("comparable");
    private readonly Relation identityRelation = new("identity");
    private readonly Dictionary<string, RelationComparisonResult> enumRelation = new(StringComparer.Ordinal);

    // createTypeChecker's marker types (created in CreateIntrinsicTypes in TypeScript's order)
    private Type markerSuperType;
    private Type markerSubType;
    private Type markerOtherType;
    private Type markerSuperTypeForCheck;
    private Type markerSubTypeForCheck;

    private readonly HashSet<Type> markerTypes = [];
    private Action<bool>? outofbandVarianceMarkerHandler;
    private bool inVarianceComputation;
    private int resolutionStart;
    private readonly Dictionary<SymbolRef, VarianceFlags[]> variancesOfSymbols = [];
    private static readonly VarianceFlags[] EmptyVariances = [];
    private static readonly VarianceFlags[] ArrayVariances = [VarianceFlags.Covariant];

    private TypeMapper? reportUnreliableMapperCache;
    private TypeMapper? reportUnmeasurableMapperCache;
    private TypeMapper? uniqueLiteralMapperCache;

    // createTypeChecker: reportUnreliableMapper
    private TypeMapper ReportUnreliableMapper => reportUnreliableMapperCache ??= MakeFunctionTypeMapper(t =>
    {
        if (outofbandVarianceMarkerHandler is not null && (t == markerSuperType || t == markerSubType || t == markerOtherType))
        {
            outofbandVarianceMarkerHandler(true);
        }
        return t;
    });

    // createTypeChecker: reportUnmeasurableMapper
    private TypeMapper ReportUnmeasurableMapper => reportUnmeasurableMapperCache ??= MakeFunctionTypeMapper(t =>
    {
        if (outofbandVarianceMarkerHandler is not null && (t == markerSuperType || t == markerSubType || t == markerOtherType))
        {
            outofbandVarianceMarkerHandler(false);
        }
        return t;
    });

    // createTypeChecker: uniqueLiteralMapper
    private TypeMapper UniqueLiteralMapper => uniqueLiteralMapperCache ??= MakeFunctionTypeMapper(t => (Types.Flags(t) & TypeFlags.TypeParameter) != 0 ? uniqueLiteralType : t);

    // ---- entry points ----

    /// <summary>checker.ts isTypeIdenticalTo.</summary>
    internal bool IsTypeIdenticalTo(Type source, Type target) => IsTypeRelatedTo(source, target, identityRelation);

    internal int CompareTypesIdentical(Type source, Type target) => IsTypeRelatedTo(source, target, identityRelation) ? Ternary.True : Ternary.False;

    /// <summary>checker.ts compareTypesAssignable.</summary>
    internal int CompareTypesAssignable(Type source, Type target) => IsTypeRelatedTo(source, target, assignableRelation) ? Ternary.True : Ternary.False;

    internal int CompareTypesSubtypeOf(Type source, Type target) => IsTypeRelatedTo(source, target, subtypeRelation) ? Ternary.True : Ternary.False;

    /// <summary>checker.ts isTypeSubtypeOf.</summary>
    internal bool IsTypeSubtypeOf(Type source, Type target) => IsTypeRelatedTo(source, target, subtypeRelation);

    /// <summary>checker.ts isTypeStrictSubtypeOf.</summary>
    internal bool IsTypeStrictSubtypeOf(Type source, Type target) => IsTypeRelatedTo(source, target, strictSubtypeRelation);

    /// <summary>checker.ts isTypeAssignableTo.</summary>
    internal bool IsTypeAssignableTo(Type source, Type target) => IsTypeRelatedTo(source, target, assignableRelation);

    /// <summary>
    /// checker.ts isTypeDerivedFrom: whether an object type S is derived from an object type T (ignoring type
    /// parameters and considering only the inheritance hierarchy).
    /// </summary>
    internal bool IsTypeDerivedFrom(Type source, Type target)
    {
        var s = Types.Flags(source);
        if ((s & TypeFlags.Union) != 0)
        {
            return EveryType(source, t => IsTypeDerivedFrom(t, target));
        }
        if ((Types.Flags(target) & TypeFlags.Union) != 0)
        {
            return SomeType(target, t => IsTypeDerivedFrom(source, t));
        }
        if ((s & TypeFlags.Intersection) != 0)
        {
            return ExistsType(Types.Elements(Types.Union(source).Types), t => IsTypeDerivedFrom(t, target));
        }
        if ((s & TypeFlags.InstantiableNonPrimitive) != 0)
        {
            var constraint = GetBaseConstraintOfType(source);
            return IsTypeDerivedFrom(constraint.IsNone ? unknownType : constraint, target);
        }
        if (IsEmptyAnonymousObjectType(target))
        {
            return (s & (TypeFlags.Object | TypeFlags.NonPrimitive)) != 0;
        }
        if (target == globalObjectType)
        {
            return (s & (TypeFlags.Object | TypeFlags.NonPrimitive)) != 0 && !IsEmptyAnonymousObjectType(source);
        }
        if (target == globalFunctionType)
        {
            return (s & TypeFlags.Object) != 0 && IsFunctionObjectType(source);
        }
        return HasBaseType(source, GetTargetType(target)) || (IsArrayType(target) && !IsReadonlyArrayType(target) && IsTypeDerivedFrom(source, globalReadonlyArrayType));
    }

    /// <summary>
    /// checker.ts isTypeComparableTo: whether some (but not necessarily all) of the possible values of S are also
    /// possible values of T. Not bi-directional.
    /// </summary>
    internal bool IsTypeComparableTo(Type source, Type target) => IsTypeRelatedTo(source, target, comparableRelation);

    /// <summary>checker.ts areTypesComparable.</summary>
    internal bool AreTypesComparable(Type type1, Type type2) => IsTypeComparableTo(type1, type2) || IsTypeComparableTo(type2, type1);

    /// <summary>checker.ts checkTypeAssignableTo.</summary>
    internal bool CheckTypeAssignableTo(Type source, Type target, NodeRef errorNode, DiagnosticMessage? headMessage = null, Func<DiagnosticMessageChain?>? containingMessageChain = null, ErrorOutputContainer? errorOutputObject = null) =>
        CheckTypeRelatedTo(source, target, assignableRelation, errorNode, headMessage, containingMessageChain, errorOutputObject);

    /// <summary>
    /// checker.ts checkTypeAssignableToAndOptionallyElaborate: like checkTypeAssignableTo, but if it would issue an
    /// error, instead performs structural comparisons of the types using the given expression node to attempt to issue
    /// more specific errors on, for example, specific object literal properties or tuple members.
    /// </summary>
    internal bool CheckTypeAssignableToAndOptionallyElaborate(Type source, Type target, NodeRef errorNode, NodeRef expr, DiagnosticMessage? headMessage = null, Func<DiagnosticMessageChain?>? containingMessageChain = null) =>
        CheckTypeRelatedToAndOptionallyElaborate(source, target, assignableRelation, errorNode, expr, headMessage, containingMessageChain, null);

    // checker.ts checkTypeRelatedToAndOptionallyElaborate
    internal bool CheckTypeRelatedToAndOptionallyElaborate(Type source, Type target, Relation relation, NodeRef errorNode, NodeRef expr, DiagnosticMessage? headMessage, Func<DiagnosticMessageChain?>? containingMessageChain, ErrorOutputContainer? errorOutputContainer)
    {
        if (IsTypeRelatedTo(source, target, relation))
        {
            return true;
        }
        if (errorNode.IsNone || !ElaborateError(expr, source, target, relation, headMessage, containingMessageChain, errorOutputContainer))
        {
            return CheckTypeRelatedTo(source, target, relation, errorNode, headMessage, containingMessageChain, errorOutputContainer);
        }
        return false;
    }

    /// <summary>checker.ts checkTypeComparableTo. Not bi-directional.</summary>
    internal bool CheckTypeComparableTo(Type source, Type target, NodeRef errorNode, DiagnosticMessage? headMessage = null, Func<DiagnosticMessageChain?>? containingMessageChain = null) =>
        CheckTypeRelatedTo(source, target, comparableRelation, errorNode, headMessage, containingMessageChain);

    /// <summary>checker.ts isSignatureAssignableTo.</summary>
    internal bool IsSignatureAssignableTo(Signature source, Signature target, bool ignoreReturnTypes) =>
        CompareSignaturesRelated(source, target, ignoreReturnTypes ? SignatureCheckMode.IgnoreReturnTypes : SignatureCheckMode.None, false, null, null,
            (s, t, _) => CompareTypesAssignable(s, t), null) != Ternary.False;

    // checker.ts isTopSignature: `(...args: A) => R` where A is any, any[], never, or never[], and R is any or unknown.
    private bool IsTopSignature(Signature s)
    {
        ref var record = ref Signatures.Record(s);
        var parameters = record.Parameters ?? [];
        if (record.TypeParameters.Count == 0 && (record.ThisParameter.IsNone || (Types.Flags(GetTypeOfParameter(record.ThisParameter)) & TypeFlags.Any) != 0) &&
            parameters.Length == 1 && SignatureHasRestParameter(s))
        {
            var paramType = GetTypeOfParameter(parameters[0]);
            var restType = IsArrayType(paramType) ? GetTypeArguments(paramType)[0] : paramType;
            return (Types.Flags(restType) & (TypeFlags.Any | TypeFlags.Never)) != 0 && (Types.Flags(GetReturnTypeOfSignature(s)) & TypeFlags.AnyOrUnknown) != 0;
        }
        return false;
    }

    /// <summary>checker.ts compareSignaturesRelated (see signatureRelatedTo, compareSignaturesIdentical).</summary>
    internal int CompareSignaturesRelated(Signature source, Signature target, SignatureCheckMode checkMode, bool reportErrors, Action<DiagnosticMessage, object?[]>? errorReporter,
        Action<Type, Type>? incompatibleErrorReporter, Func<Type, Type, bool, int> compareTypes, TypeMapper? reportUnreliableMarkers)
    {
        if (source == target)
        {
            return Ternary.True;
        }
        if (!((checkMode & SignatureCheckMode.StrictTopSignature) != 0 && IsTopSignature(source)) && IsTopSignature(target))
        {
            return Ternary.True;
        }
        if ((checkMode & SignatureCheckMode.StrictTopSignature) != 0 && IsTopSignature(source) && !IsTopSignature(target))
        {
            return Ternary.False;
        }
        var targetCount = GetParameterCount(target);
        var sourceHasMoreParameters = !HasEffectiveRestParameter(target) &&
            ((checkMode & SignatureCheckMode.StrictArity) != 0 ? HasEffectiveRestParameter(source) || GetParameterCount(source) > targetCount : GetMinArgumentCount(source) > targetCount);
        if (sourceHasMoreParameters)
        {
            if (reportErrors && (checkMode & SignatureCheckMode.StrictArity) == 0)
            {
                // the second condition should be redundant, because there is no error reporting when comparing signatures by strict arity
                // since it is only done for subtype reduction
                errorReporter!(Diagnostics.Target_signature_provides_too_few_arguments_Expected_0_or_more_but_got_1, [GetMinArgumentCount(source), targetCount]);
            }
            return Ternary.False;
        }
        var sourceTypeParameters = Signatures.Record(source).TypeParameters;
        if (sourceTypeParameters.Count > 0 && sourceTypeParameters != Signatures.Record(target).TypeParameters)
        {
            target = GetCanonicalSignature(target);
            source = InstantiateSignatureInContextOf(source, target, null, (s, t) => compareTypes(s, t, false) != Ternary.False);
        }
        var sourceCount = GetParameterCount(source);
        var sourceRestType = GetNonArrayRestType(source);
        var targetRestType = GetNonArrayRestType(target);
        if (!sourceRestType.IsNone || !targetRestType.IsNone)
        {
            InstantiateType(!sourceRestType.IsNone ? sourceRestType : targetRestType, reportUnreliableMarkers);
        }
        var targetDeclaration = Signatures.Record(target).Declaration;
        var kind = targetDeclaration.IsNone ? SyntaxKind.Unknown : Kind(targetDeclaration);
        var strictVariance = (checkMode & SignatureCheckMode.Callback) == 0 && StrictFunctionTypes && kind != SyntaxKind.MethodDeclaration &&
            kind != SyntaxKind.MethodSignature && kind != SyntaxKind.Constructor;
        var result = Ternary.True;
        var sourceThisType = GetThisTypeOfSignature(source);
        if (!sourceThisType.IsNone && sourceThisType != voidType)
        {
            var targetThisType = GetThisTypeOfSignature(target);
            if (!targetThisType.IsNone)
            {
                // void sources are assignable to anything.
                var related = !strictVariance ? compareTypes(sourceThisType, targetThisType, false) : Ternary.False;
                if (related == Ternary.False)
                {
                    related = compareTypes(targetThisType, sourceThisType, reportErrors);
                }
                if (related == Ternary.False)
                {
                    if (reportErrors)
                    {
                        errorReporter!(Diagnostics.The_this_types_of_each_signature_are_incompatible, []);
                    }
                    return Ternary.False;
                }
                result &= related;
            }
        }
        var paramCount = !sourceRestType.IsNone || !targetRestType.IsNone ? Math.Min(sourceCount, targetCount) : Math.Max(sourceCount, targetCount);
        var restIndex = !sourceRestType.IsNone || !targetRestType.IsNone ? paramCount - 1 : -1;
        for (var i = 0; i < paramCount; i++)
        {
            var sourceType = i == restIndex ? GetRestOrAnyTypeAtPosition(source, i) : TryGetTypeAtPosition(source, i);
            var targetType = i == restIndex ? GetRestOrAnyTypeAtPosition(target, i) : TryGetTypeAtPosition(target, i);
            if (!sourceType.IsNone && !targetType.IsNone && (sourceType != targetType || (checkMode & SignatureCheckMode.StrictArity) != 0))
            {
                // In order to ensure that any generic type Foo<T> is at least co-variant with respect to T no matter
                // how Foo uses T, we need to relate parameters bi-variantly (given that parameters are input positions,
                // they naturally relate only contra-variantly). However, if the source and target parameters both have
                // function types with a single call signature, we know we are relating two callback parameters. In
                // that case it is sufficient to only relate the parameters of the signatures co-variantly because,
                // similar to return values, callback parameters are output positions. This means that a Promise<T>,
                // where T is used only in callback parameter positions, will be co-variant (as opposed to bi-variant)
                // with respect to T.
                var sourceSig = (checkMode & SignatureCheckMode.Callback) != 0 || IsInstantiatedGenericParameter(source, i) ? Signature.None : GetSingleCallSignature(GetNonNullableType(sourceType));
                var targetSig = (checkMode & SignatureCheckMode.Callback) != 0 || IsInstantiatedGenericParameter(target, i) ? Signature.None : GetSingleCallSignature(GetNonNullableType(targetType));
                var callbacks = !sourceSig.IsNone && !targetSig.IsNone && GetTypePredicateOfSignature(sourceSig) is null && GetTypePredicateOfSignature(targetSig) is null &&
                    GetTypeFacts(sourceType, TypeFacts.IsUndefinedOrNull) == GetTypeFacts(targetType, TypeFacts.IsUndefinedOrNull);
                int related;
                if (callbacks)
                {
                    related = CompareSignaturesRelated(targetSig, sourceSig, (checkMode & SignatureCheckMode.StrictArity) | (strictVariance ? SignatureCheckMode.StrictCallback : SignatureCheckMode.BivariantCallback),
                        reportErrors, errorReporter, incompatibleErrorReporter, compareTypes, reportUnreliableMarkers);
                }
                else
                {
                    related = (checkMode & SignatureCheckMode.Callback) == 0 && !strictVariance ? compareTypes(sourceType, targetType, false) : Ternary.False;
                    if (related == Ternary.False)
                    {
                        related = compareTypes(targetType, sourceType, reportErrors);
                    }
                }
                // With strict arity, (x: number | undefined) => void is a subtype of (x?: number | undefined) => void
                if (related != Ternary.False && (checkMode & SignatureCheckMode.StrictArity) != 0 && i >= GetMinArgumentCount(source) && i < GetMinArgumentCount(target) &&
                    compareTypes(sourceType, targetType, false) != Ternary.False)
                {
                    related = Ternary.False;
                }
                if (related == Ternary.False)
                {
                    if (reportErrors)
                    {
                        errorReporter!(Diagnostics.Types_of_parameters_0_and_1_are_incompatible,
                            [Utilities.UnescapeLeadingUnderscores(GetParameterNameAtPosition(source, i)), Utilities.UnescapeLeadingUnderscores(GetParameterNameAtPosition(target, i))]);
                    }
                    return Ternary.False;
                }
                result &= related;
            }
        }
        if ((checkMode & SignatureCheckMode.IgnoreReturnTypes) == 0)
        {
            // If a signature resolution is already in-flight, skip issuing a circularity error
            // here and just use the `any` type directly
            var targetReturnType = IsResolvingReturnTypeOfSignature(target) ? anyType
                : !targetDeclaration.IsNone && IsJSConstructor(targetDeclaration) ? GetDeclaredTypeOfClassOrInterface(GetMergedSymbol(NodeSymbol(targetDeclaration)))
                : GetReturnTypeOfSignature(target);
            if (targetReturnType == voidType || targetReturnType == anyType)
            {
                return result;
            }
            var sourceDeclaration = Signatures.Record(source).Declaration;
            var sourceReturnType = IsResolvingReturnTypeOfSignature(source) ? anyType
                : !sourceDeclaration.IsNone && IsJSConstructor(sourceDeclaration) ? GetDeclaredTypeOfClassOrInterface(GetMergedSymbol(NodeSymbol(sourceDeclaration)))
                : GetReturnTypeOfSignature(source);
            // The following block preserves behavior forbidding boolean returning functions from being assignable to type guard returning functions
            var targetTypePredicate = GetTypePredicateOfSignature(target);
            if (targetTypePredicate is not null)
            {
                var sourceTypePredicate = GetTypePredicateOfSignature(source);
                if (sourceTypePredicate is not null)
                {
                    result &= CompareTypePredicateRelatedTo(sourceTypePredicate, targetTypePredicate, reportErrors, errorReporter, compareTypes);
                }
                else if (targetTypePredicate.Kind is TypePredicateKind.Identifier or TypePredicateKind.This)
                {
                    if (reportErrors)
                    {
                        errorReporter!(Diagnostics.Signature_0_must_be_a_type_predicate, [SignatureToString(source)]);
                    }
                    return Ternary.False;
                }
            }
            else
            {
                // When relating callback signatures, we still need to relate return types bi-variantly as otherwise
                // the containing type wouldn't be co-variant. For example, interface Foo<T> { add(cb: () => T): void }
                // wouldn't be co-variant for T without this rule.
                var related = (checkMode & SignatureCheckMode.BivariantCallback) != 0 ? compareTypes(targetReturnType, sourceReturnType, false) : Ternary.False;
                if (related == Ternary.False)
                {
                    related = compareTypes(sourceReturnType, targetReturnType, reportErrors);
                }
                result &= related;
                if (result == Ternary.False && reportErrors && incompatibleErrorReporter is not null)
                {
                    incompatibleErrorReporter(sourceReturnType, targetReturnType);
                }
            }
        }
        return result;
    }

    // checker.ts compareTypePredicateRelatedTo
    private int CompareTypePredicateRelatedTo(TypePredicateInfo source, TypePredicateInfo target, bool reportErrors, Action<DiagnosticMessage, object?[]>? errorReporter, Func<Type, Type, bool, int> compareTypes)
    {
        if (source.Kind != target.Kind)
        {
            if (reportErrors)
            {
                errorReporter!(Diagnostics.A_this_based_type_guard_is_not_compatible_with_a_parameter_based_type_guard, []);
                errorReporter!(Diagnostics.Type_predicate_0_is_not_assignable_to_1, [TypePredicateToString(source), TypePredicateToString(target)]);
            }
            return Ternary.False;
        }
        if (source.Kind is TypePredicateKind.Identifier or TypePredicateKind.AssertsIdentifier)
        {
            if (source.ParameterIndex != target.ParameterIndex)
            {
                if (reportErrors)
                {
                    errorReporter!(Diagnostics.Parameter_0_is_not_in_the_same_position_as_parameter_1, [source.ParameterName, target.ParameterName]);
                    errorReporter!(Diagnostics.Type_predicate_0_is_not_assignable_to_1, [TypePredicateToString(source), TypePredicateToString(target)]);
                }
                return Ternary.False;
            }
        }
        var related = source.Type == target.Type ? Ternary.True :
            !source.Type.IsNone && !target.Type.IsNone ? compareTypes(source.Type, target.Type, reportErrors) :
            Ternary.False;
        if (related == Ternary.False && reportErrors)
        {
            errorReporter!(Diagnostics.Type_predicate_0_is_not_assignable_to_1, [TypePredicateToString(source), TypePredicateToString(target)]);
        }
        return related;
    }

    /// <summary>checker.ts isImplementationCompatibleWithOverload.</summary>
    internal bool IsImplementationCompatibleWithOverload(Signature implementation, Signature overload)
    {
        var erasedSource = GetErasedSignature(implementation);
        var erasedTarget = GetErasedSignature(overload);
        // First see if the return types are compatible in either direction.
        var sourceReturnType = GetReturnTypeOfSignature(erasedSource);
        var targetReturnType = GetReturnTypeOfSignature(erasedTarget);
        if (targetReturnType == voidType ||
            IsTypeRelatedTo(targetReturnType, sourceReturnType, assignableRelation) ||
            IsTypeRelatedTo(sourceReturnType, targetReturnType, assignableRelation))
        {
            return IsSignatureAssignableTo(erasedSource, erasedTarget, ignoreReturnTypes: true);
        }
        return false;
    }

    // checker.ts isEmptyResolvedType
    private bool IsEmptyResolvedTypeOf(Type type, ObjectTypeData data) => type != anyFunctionType && IsEmptyResolvedType(data);

    /// <summary>checker.ts isEmptyObjectType.</summary>
    internal bool IsEmptyObjectType(Type type)
    {
        var flags = Types.Flags(type);
        return (flags & TypeFlags.Object) != 0 ? !IsGenericMappedType(type) && IsEmptyResolvedTypeOf(type, ResolveStructuredTypeMembers(type)) :
            (flags & TypeFlags.NonPrimitive) != 0 ? true :
            (flags & TypeFlags.Union) != 0 ? ExistsType(Types.Elements(Types.Union(type).Types), IsEmptyObjectType) :
            (flags & TypeFlags.Intersection) != 0 && AllTypes(Types.Elements(Types.Union(type).Types), IsEmptyObjectType);
    }

    /// <summary>checker.ts isUnknownLikeUnionType.</summary>
    internal bool IsUnknownLikeUnionType(Type type)
    {
        if (strictNullChecks && (Types.Flags(type) & TypeFlags.Union) != 0)
        {
            if ((Types.ObjectFlagsOf(type) & ObjectFlags.IsUnknownLikeUnionComputed) == 0)
            {
                var types = Types.Elements(Types.Union(type).Types).ToArray();
                var isUnknownLike = types.Length >= 3 && (Types.Flags(types[0]) & TypeFlags.Undefined) != 0 &&
                    (Types.Flags(types[1]) & TypeFlags.Null) != 0 && Array.Exists(types, IsEmptyAnonymousObjectType);
                Types.Record(type).ObjectFlags |= ObjectFlags.IsUnknownLikeUnionComputed | (isUnknownLike ? ObjectFlags.IsUnknownLikeUnion : 0);
            }
            return (Types.ObjectFlagsOf(type) & ObjectFlags.IsUnknownLikeUnion) != 0;
        }
        return false;
    }

    /// <summary>checker.ts containsUndefinedType.</summary>
    internal bool ContainsUndefinedType(Type type) =>
        (Types.Flags((Types.Flags(type) & TypeFlags.Union) != 0 ? Types.Elements(Types.Union(type).Types)[0] : type) & TypeFlags.Undefined) != 0;

    private static bool AllTypes(ReadOnlySpan<Type> types, Func<Type, bool> f)
    {
        foreach (var t in types.ToArray())
        {
            if (!f(t))
            {
                return false;
            }
        }
        return true;
    }

    private static string SymbolKey(SymbolRef symbol) =>
        symbol.File.ToString(CultureInfo.InvariantCulture) + ":" + symbol.Index.ToString(CultureInfo.InvariantCulture);

    // checker.ts isEnumTypeRelatedTo
    private bool IsEnumTypeRelatedTo(SymbolRef source, SymbolRef target, Action<DiagnosticMessage, object?[]>? errorReporter)
    {
        var sourceSymbol = (SymbolFlagsOf(source) & SymbolFlags.EnumMember) != 0 ? GetParentOfSymbol(source) : source;
        var targetSymbol = (SymbolFlagsOf(target) & SymbolFlags.EnumMember) != 0 ? GetParentOfSymbol(target) : target;
        if (sourceSymbol == targetSymbol)
        {
            return true;
        }
        if (NameOf(sourceSymbol) != NameOf(targetSymbol) || (SymbolFlagsOf(sourceSymbol) & SymbolFlags.RegularEnum) == 0 || (SymbolFlagsOf(targetSymbol) & SymbolFlags.RegularEnum) == 0)
        {
            return false;
        }
        var id = SymbolKey(sourceSymbol) + "," + SymbolKey(targetSymbol);
        if (enumRelation.TryGetValue(id, out var entry) && !((entry & RelationComparisonResult.Failed) != 0 && errorReporter is not null))
        {
            return (entry & RelationComparisonResult.Succeeded) != 0;
        }
        var targetEnumType = GetTypeOfSymbol(targetSymbol);
        foreach (var sourceProperty in GetPropertiesOfType(GetTypeOfSymbol(sourceSymbol)))
        {
            if ((SymbolFlagsOf(sourceProperty) & SymbolFlags.EnumMember) != 0)
            {
                var targetProperty = GetPropertyOfType(targetEnumType, NameOf(sourceProperty));
                if (targetProperty.IsNone || (SymbolFlagsOf(targetProperty) & SymbolFlags.EnumMember) == 0)
                {
                    errorReporter?.Invoke(Diagnostics.Property_0_is_missing_in_type_1, [SymbolName(sourceProperty), TypeToString(GetDeclaredTypeOfSymbol(targetSymbol), default, TypeFormatFlags.UseFullyQualifiedType)]);
                    enumRelation[id] = RelationComparisonResult.Failed;
                    return false;
                }
                var sourceValue = GetEnumMemberValueOfDeclaration(GetDeclarationOfKind(sourceProperty, SyntaxKind.EnumMember));
                var targetValue = GetEnumMemberValueOfDeclaration(GetDeclarationOfKind(targetProperty, SyntaxKind.EnumMember));
                if (!Equals(sourceValue, targetValue))
                {
                    var sourceIsString = sourceValue is string;
                    var targetIsString = targetValue is string;
                    // If we have 2 enums with *known* values that differ, they are incompatible.
                    if (sourceValue is not null && targetValue is not null)
                    {
                        if (errorReporter is not null)
                        {
                            var escapedSource = sourceIsString ? "\"" + PrinterUtilities.EscapeString((string)sourceValue) + "\"" : JsNumber.ToString((double)sourceValue);
                            var escapedTarget = targetIsString ? "\"" + PrinterUtilities.EscapeString((string)targetValue) + "\"" : JsNumber.ToString((double)targetValue);
                            errorReporter(Diagnostics.Each_declaration_of_0_1_differs_in_its_value_where_2_was_expected_but_3_was_given, [SymbolName(targetSymbol), SymbolName(targetProperty), escapedTarget, escapedSource]);
                        }
                        enumRelation[id] = RelationComparisonResult.Failed;
                        return false;
                    }
                    // At this point we know that at least one of the values is 'undefined'.
                    // This may mean that we have an opaque member from an ambient enum declaration,
                    // or that we were not able to calculate it (which is basically an error).
                    //
                    // Either way, we can assume that it's numeric.
                    // If the other is a string, we have a mismatch in types.
                    if (sourceIsString || targetIsString)
                    {
                        if (errorReporter is not null)
                        {
                            var knownStringValue = (string)(sourceValue ?? targetValue)!;
                            var escapedValue = "\"" + PrinterUtilities.EscapeString(knownStringValue) + "\"";
                            errorReporter(Diagnostics.One_value_of_0_1_is_the_string_2_and_the_other_is_assumed_to_be_an_unknown_numeric_value, [SymbolName(targetSymbol), SymbolName(targetProperty), escapedValue]);
                        }
                        enumRelation[id] = RelationComparisonResult.Failed;
                        return false;
                    }
                }
            }
        }
        enumRelation[id] = RelationComparisonResult.Succeeded;
        return true;
    }

    /// <summary>checker.ts isSimpleTypeRelatedTo.</summary>
    internal bool IsSimpleTypeRelatedTo(Type source, Type target, Relation relation, Action<DiagnosticMessage, object?[]>? errorReporter = null)
    {
        var s = Types.Flags(source);
        var t = Types.Flags(target);
        if ((t & TypeFlags.Any) != 0 || (s & TypeFlags.Never) != 0 || source == wildcardType)
        {
            return true;
        }
        if ((t & TypeFlags.Unknown) != 0 && !(relation == strictSubtypeRelation && (s & TypeFlags.Any) != 0))
        {
            return true;
        }
        if ((t & TypeFlags.Never) != 0)
        {
            return false;
        }
        if ((s & TypeFlags.StringLike) != 0 && (t & TypeFlags.String) != 0)
        {
            return true;
        }
        if ((s & TypeFlags.StringLiteral) != 0 && (s & TypeFlags.EnumLiteral) != 0 &&
            (t & TypeFlags.StringLiteral) != 0 && (t & TypeFlags.EnumLiteral) == 0 &&
            Equals(Types.Literal(source).Value, Types.Literal(target).Value))
        {
            return true;
        }
        if ((s & TypeFlags.NumberLike) != 0 && (t & TypeFlags.Number) != 0)
        {
            return true;
        }
        if ((s & TypeFlags.NumberLiteral) != 0 && (s & TypeFlags.EnumLiteral) != 0 &&
            (t & TypeFlags.NumberLiteral) != 0 && (t & TypeFlags.EnumLiteral) == 0 &&
            Equals(Types.Literal(source).Value, Types.Literal(target).Value))
        {
            return true;
        }
        if ((s & TypeFlags.BigIntLike) != 0 && (t & TypeFlags.BigInt) != 0)
        {
            return true;
        }
        if ((s & TypeFlags.BooleanLike) != 0 && (t & TypeFlags.Boolean) != 0)
        {
            return true;
        }
        if ((s & TypeFlags.ESSymbolLike) != 0 && (t & TypeFlags.ESSymbol) != 0)
        {
            return true;
        }
        if ((s & TypeFlags.Enum) != 0 && (t & TypeFlags.Enum) != 0 && NameOf(Types.Symbol(source)) == NameOf(Types.Symbol(target)) &&
            IsEnumTypeRelatedTo(Types.Symbol(source), Types.Symbol(target), errorReporter))
        {
            return true;
        }
        if ((s & TypeFlags.EnumLiteral) != 0 && (t & TypeFlags.EnumLiteral) != 0)
        {
            if ((s & TypeFlags.Union) != 0 && (t & TypeFlags.Union) != 0 && IsEnumTypeRelatedTo(Types.Symbol(source), Types.Symbol(target), errorReporter))
            {
                return true;
            }
            if ((s & TypeFlags.Literal) != 0 && (t & TypeFlags.Literal) != 0 && Equals(Types.Literal(source).Value, Types.Literal(target).Value) &&
                IsEnumTypeRelatedTo(Types.Symbol(source), Types.Symbol(target), errorReporter))
            {
                return true;
            }
        }
        // In non-strictNullChecks mode, `undefined` and `null` are assignable to anything except `never`.
        // Since unions and intersections may reduce to `never`, we exclude them here.
        if ((s & TypeFlags.Undefined) != 0 && ((!strictNullChecks && (t & TypeFlags.UnionOrIntersection) == 0) || (t & (TypeFlags.Undefined | TypeFlags.Void)) != 0))
        {
            return true;
        }
        if ((s & TypeFlags.Null) != 0 && ((!strictNullChecks && (t & TypeFlags.UnionOrIntersection) == 0) || (t & TypeFlags.Null) != 0))
        {
            return true;
        }
        if ((s & TypeFlags.Object) != 0 && (t & TypeFlags.NonPrimitive) != 0 &&
            !(relation == strictSubtypeRelation && IsEmptyAnonymousObjectType(source) && (Types.ObjectFlagsOf(source) & ObjectFlags.FreshLiteral) == 0))
        {
            return true;
        }
        if (relation == assignableRelation || relation == comparableRelation)
        {
            if ((s & TypeFlags.Any) != 0)
            {
                return true;
            }
            // Type number is assignable to any computed numeric enum type or any numeric enum literal type, and
            // a numeric literal type is assignable any computed numeric enum type or any numeric enum literal type
            // with a matching value. These rules exist such that enums can be used for bit-flag purposes.
            if ((s & TypeFlags.Number) != 0 && ((t & TypeFlags.Enum) != 0 || ((t & TypeFlags.NumberLiteral) != 0 && (t & TypeFlags.EnumLiteral) != 0)))
            {
                return true;
            }
            if ((s & TypeFlags.NumberLiteral) != 0 && (s & TypeFlags.EnumLiteral) == 0 && ((t & TypeFlags.Enum) != 0 ||
                ((t & TypeFlags.NumberLiteral) != 0 && (t & TypeFlags.EnumLiteral) != 0 && Equals(Types.Literal(source).Value, Types.Literal(target).Value))))
            {
                return true;
            }
            // Anything is assignable to a union containing undefined, null, and {}
            if (IsUnknownLikeUnionType(target))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>checker.ts isTypeRelatedTo.</summary>
    internal bool IsTypeRelatedTo(Type source, Type target, Relation relation)
    {
        if (IsFreshLiteralType(source))
        {
            source = GetRegularTypeOfLiteralType(source);
        }
        if (IsFreshLiteralType(target))
        {
            target = GetRegularTypeOfLiteralType(target);
        }
        if (source == target)
        {
            return true;
        }
        if (relation != identityRelation)
        {
            if ((relation == comparableRelation && (Types.Flags(target) & TypeFlags.Never) == 0 && IsSimpleTypeRelatedTo(target, source, relation)) || IsSimpleTypeRelatedTo(source, target, relation))
            {
                return true;
            }
        }
        else if (((Types.Flags(source) | Types.Flags(target)) & (TypeFlags.UnionOrIntersection | TypeFlags.IndexedAccess | TypeFlags.Conditional | TypeFlags.Substitution)) == 0)
        {
            // We have excluded types that may simplify to other forms, so types must have identical flags
            if (Types.Flags(source) != Types.Flags(target))
            {
                return false;
            }
            if ((Types.Flags(source) & TypeFlags.Singleton) != 0)
            {
                return true;
            }
        }
        if ((Types.Flags(source) & TypeFlags.Object) != 0 && (Types.Flags(target) & TypeFlags.Object) != 0)
        {
            if (relation.Results.TryGetValue(GetRelationKey(source, target, IntersectionState.None, relation, ignoreConstraints: false), out var related))
            {
                return (related & RelationComparisonResult.Succeeded) != 0;
            }
        }
        if ((Types.Flags(source) & TypeFlags.StructuredOrInstantiable) != 0 || (Types.Flags(target) & TypeFlags.StructuredOrInstantiable) != 0)
        {
            return CheckTypeRelatedTo(source, target, relation, NodeRef.None);
        }
        return false;
    }

    // checker.ts isIgnoredJsxProperty
    private bool IsIgnoredJsxProperty(Type source, SymbolRef sourceProp) =>
        (Types.ObjectFlagsOf(source) & ObjectFlags.JsxAttributes) != 0 && IsHyphenatedJsxName(NameOf(sourceProp));

    // utilities.ts isHyphenatedJsxName
    private static bool IsHyphenatedJsxName(string name) => name.Contains('-', StringComparison.Ordinal);

    // checker.ts getNormalizedType
    private Type GetNormalizedType(Type type, bool writing)
    {
        while (true)
        {
            Type t;
            if (IsFreshLiteralType(type))
            {
                t = GetRegularTypeOfLiteralType(type);
            }
            else if (IsGenericTupleType(type))
            {
                t = GetNormalizedTupleType(type, writing);
            }
            else if ((Types.ObjectFlagsOf(type) & ObjectFlags.Reference) != 0)
            {
                if (!Types.Object(type).Node.IsNone)
                {
                    t = CreateTypeReference(Types.Object(type).Target, GetTypeArguments(type));
                }
                else
                {
                    var single = GetSingleBaseForNonAugmentingSubtype(type);
                    t = single.IsNone ? type : single;
                }
            }
            else if ((Types.Flags(type) & TypeFlags.UnionOrIntersection) != 0)
            {
                t = GetNormalizedUnionOrIntersectionType(type, writing);
            }
            else if ((Types.Flags(type) & TypeFlags.Substitution) != 0)
            {
                t = writing ? Types.Data<SubstitutionData>(type).BaseType : GetSubstitutionIntersection(type);
            }
            else if ((Types.Flags(type) & TypeFlags.Simplifiable) != 0)
            {
                t = GetSimplifiedType(type, writing);
            }
            else
            {
                t = type;
            }
            if (t == type)
            {
                return t;
            }
            type = t;
        }
    }

    // checker.ts getNormalizedUnionOrIntersectionType
    private Type GetNormalizedUnionOrIntersectionType(Type type, bool writing)
    {
        var reduced = GetReducedType(type);
        if (reduced != type)
        {
            return reduced;
        }
        if ((Types.Flags(type) & TypeFlags.Intersection) != 0 && ShouldNormalizeIntersection(type))
        {
            // Normalization handles cases like
            // Partial<T>[K] & ({} | null) ==>
            // Partial<T>[K] & {} | Partial<T>[K] & null ==>
            // (T[K] | undefined) & {} | (T[K] | undefined) & null ==>
            // T[K] & {} | undefined & {} | T[K] & null | undefined & null ==>
            // T[K] & {} | T[K] & null
            var types = Types.Elements(Types.Union(type).Types).ToArray();
            var normalizedTypes = new Type[types.Length];
            var changed = false;
            for (var i = 0; i < types.Length; i++)
            {
                normalizedTypes[i] = GetNormalizedType(types[i], writing);
                changed |= normalizedTypes[i] != types[i];
            }
            if (changed)
            {
                return GetIntersectionType(normalizedTypes);
            }
        }
        return type;
    }

    // checker.ts shouldNormalizeIntersection
    private bool ShouldNormalizeIntersection(Type type)
    {
        var hasInstantiable = false;
        var hasNullableOrEmpty = false;
        foreach (var t in Types.Elements(Types.Union(type).Types).ToArray())
        {
            hasInstantiable |= (Types.Flags(t) & TypeFlags.Instantiable) != 0;
            hasNullableOrEmpty |= (Types.Flags(t) & TypeFlags.Nullable) != 0 || IsEmptyAnonymousObjectType(t);
            if (hasInstantiable && hasNullableOrEmpty)
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts getNormalizedTupleType
    private Type GetNormalizedTupleType(Type type, bool writing)
    {
        var elements = GetElementTypes(type);
        var normalizedElements = new Type[elements.Length];
        var changed = false;
        for (var i = 0; i < elements.Length; i++)
        {
            var t = elements[i];
            normalizedElements[i] = (Types.Flags(t) & TypeFlags.Simplifiable) != 0 ? GetSimplifiedType(t, writing) : t;
            changed |= normalizedElements[i] != t;
        }
        return changed ? CreateNormalizedTupleType(Types.Object(type).Target, normalizedElements) : type;
    }

    /// <summary>checker.ts getSingleBaseForNonAugmentingSubtype.</summary>
    internal Type GetSingleBaseForNonAugmentingSubtype(Type type)
    {
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.Reference) == 0 || (Types.ObjectFlagsOf(Types.Object(type).Target) & ObjectFlags.ClassOrInterface) == 0)
        {
            return Type.None;
        }
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.IdenticalBaseTypeCalculated) != 0)
        {
            return (Types.ObjectFlagsOf(type) & ObjectFlags.IdenticalBaseTypeExists) != 0 ? cachedEquivalentBaseTypes[type] : Type.None;
        }
        Types.Record(type).ObjectFlags |= ObjectFlags.IdenticalBaseTypeCalculated;
        var target = Types.Object(type).Target;
        if ((Types.ObjectFlagsOf(target) & ObjectFlags.Class) != 0)
        {
            var declaration = ValueDeclarationOf(Types.Symbol(target));
            var baseTypeNode = declaration.IsNone ? NodeRef.None : GetBaseTypeNodeOfClass(declaration);
            // A base type expression may circularly reference the class itself (e.g. as an argument to function call), so we only
            // check for base types specified as simple qualified names.
            if (!baseTypeNode.IsNone)
            {
                var expressionKind = StoreOf(baseTypeNode).Kind(StoreOf(baseTypeNode).Expression(baseTypeNode.Node));
                if (expressionKind != SyntaxKind.Identifier && expressionKind != SyntaxKind.PropertyAccessExpression)
                {
                    return Type.None;
                }
            }
        }
        var bases = GetBaseTypes(target);
        if (bases.Count != 1)
        {
            return Type.None;
        }
        if (GetMembersOfSymbol(Types.Symbol(type)).Count > 0)
        {
            return Type.None; // If the interface has any members, they may subtype members in the base, so we should do a full structural comparison
        }
        var typeParameters = Types.Object(target).TypeParameters;
        var typeArguments = GetTypeArguments(type).ToArray();
        var instantiatedBase = typeParameters.Count == 0 ? bases[0]
            : InstantiateType(bases[0], CreateTypeMapper(Types.Elements(typeParameters).ToArray(), typeArguments[..typeParameters.Count]));
        if (typeArguments.Length > typeParameters.Count)
        {
            instantiatedBase = GetTypeWithThisArgument(instantiatedBase, typeArguments[^1]);
        }
        Types.Record(type).ObjectFlags |= ObjectFlags.IdenticalBaseTypeExists;
        cachedEquivalentBaseTypes[type] = instantiatedBase;
        return instantiatedBase;
    }

    // TypeReference.cachedEquivalentBaseType
    private readonly Dictionary<Type, Type> cachedEquivalentBaseTypes = [];

    // checker.ts typeCouldHaveTopLevelSingletonTypes
    private bool TypeCouldHaveTopLevelSingletonTypes(Type type)
    {
        // Okay, yes, 'boolean' is a union of 'true | false', but that's not useful
        // in error reporting scenarios. If you need to use this function but that detail matters,
        // feel free to add a flag.
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Boolean) != 0)
        {
            return false;
        }
        if ((flags & TypeFlags.UnionOrIntersection) != 0)
        {
            return ExistsType(Types.Elements(Types.Union(type).Types), TypeCouldHaveTopLevelSingletonTypes);
        }
        if ((flags & TypeFlags.Instantiable) != 0)
        {
            var constraint = GetConstraintOfType(type);
            if (!constraint.IsNone && constraint != type)
            {
                return TypeCouldHaveTopLevelSingletonTypes(constraint);
            }
        }
        return (flags & TypeFlags.Unit) != 0 || (flags & TypeFlags.TemplateLiteral) != 0 || (flags & TypeFlags.StringMapping) != 0;
    }

    // checker.ts getExactOptionalUnassignableProperties
    private List<SymbolRef> GetExactOptionalUnassignableProperties(Type source, Type target)
    {
        if (IsTupleType(source) && IsTupleType(target))
        {
            return [];
        }
        return GetPropertiesOfType(target).FindAll(targetProp => IsExactOptionalPropertyMismatch(GetTypeOfPropertyOfType(source, NameOf(targetProp)), GetTypeOfSymbol(targetProp)));
    }

    // checker.ts isExactOptionalPropertyMismatch
    private bool IsExactOptionalPropertyMismatch(Type source, Type target) =>
        !source.IsNone && !target.IsNone && MaybeTypeOfKind(source, TypeFlags.Undefined) && ContainsMissingType(target);

    /// <summary>checker.ts getTypeOfPropertyOfType (None when the type has no such property).</summary>
    internal Type GetTypeOfPropertyOfType(Type type, string name)
    {
        var prop = GetPropertyOfType(type, name);
        return prop.IsNone ? Type.None : GetTypeOfSymbol(prop);
    }

    /// <summary>
    /// checker.ts getTypeOfPropertyOrIndexSignatureOfType: the type of the matching property or index signature (with
    /// optionality added to index signature types); None if there is none.
    /// </summary>
    internal Type GetTypeOfPropertyOrIndexSignatureOfType(Type type, string name)
    {
        var propType = GetTypeOfPropertyOfType(type, name);
        if (!propType.IsNone)
        {
            return propType;
        }
        var indexInfo = GetApplicableIndexInfoForName(type, name);
        return indexInfo is null ? Type.None : AddOptionality(indexInfo.Type, isProperty: true, isOptional: true);
    }

    /// <summary>
    /// checker.ts isWeakType: an object type with at least one optional property and no required properties,
    /// call/construct signatures or index signatures.
    /// </summary>
    internal bool IsWeakType(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers(type);
            return resolved.CallSignatures!.Count == 0 && resolved.ConstructSignatures!.Count == 0 && resolved.IndexInfos!.Count == 0 &&
                resolved.Properties!.Count > 0 && resolved.Properties.TrueForAll(p => (SymbolFlagsOf(p) & SymbolFlags.Optional) != 0);
        }
        if ((flags & TypeFlags.Substitution) != 0)
        {
            return IsWeakType(Types.Data<SubstitutionData>(type).BaseType);
        }
        if ((flags & TypeFlags.Intersection) != 0)
        {
            return AllTypes(Types.Elements(Types.Union(type).Types), IsWeakType);
        }
        return false;
    }

    // checker.ts hasCommonProperties
    private bool HasCommonProperties(Type source, Type target, bool isComparingJsxAttributes)
    {
        foreach (var prop in GetPropertiesOfType(source))
        {
            if (IsKnownProperty(target, NameOf(prop), isComparingJsxAttributes))
            {
                return true;
            }
        }
        return false;
    }

    // ---- variances ----

    /// <summary>checker.ts getVariances: arrays and tuples are known to be covariant.</summary>
    internal VarianceFlags[] GetVariances(Type type) =>
        type == globalArrayType || type == globalReadonlyArrayType || (Types.ObjectFlagsOf(type) & ObjectFlags.Tuple) != 0
            ? ArrayVariances
            : GetVariancesWorker(Types.Symbol(type), Types.Elements(Types.Object(type).TypeParameters).ToArray());

    // checker.ts getAliasVariances
    private VarianceFlags[] GetAliasVariances(SymbolRef symbol) => GetVariancesWorker(symbol, GetTypeAliasTypeParameters(symbol) ?? []);

    // checker.ts getVariancesWorker: an array containing the variance of each type parameter. The variance is
    // effectively a digest of the type comparisons that occur for each type argument when instantiations of the
    // generic type are structurally compared. We infer the variance information by comparing instantiations of the
    // generic type for type arguments with known relations. Returns the EmptyVariances singleton when invoked
    // recursively for the given generic type.
    private VarianceFlags[] GetVariancesWorker(SymbolRef symbol, Type[] typeParameters)
    {
        if (!variancesOfSymbols.TryGetValue(symbol, out var links))
        {
            var oldVarianceComputation = inVarianceComputation;
            var saveResolutionStart = resolutionStart;
            if (!inVarianceComputation)
            {
                inVarianceComputation = true;
                resolutionStart = resolutionTargets.Count;
            }
            variancesOfSymbols[symbol] = EmptyVariances;
            var variances = new List<VarianceFlags>();
            foreach (var tp in typeParameters)
            {
                var modifiers = GetTypeParameterModifiers(tp);
                VarianceFlags? variance = (modifiers & ModifierFlags.Out) != 0
                    ? (modifiers & ModifierFlags.In) != 0 ? VarianceFlags.Invariant : VarianceFlags.Covariant
                    : (modifiers & ModifierFlags.In) != 0 ? VarianceFlags.Contravariant : null;
                if (variance is null)
                {
                    var unmeasurable = false;
                    var unreliable = false;
                    var oldHandler = outofbandVarianceMarkerHandler;
                    outofbandVarianceMarkerHandler = onlyUnreliable =>
                    {
                        if (onlyUnreliable)
                        {
                            unreliable = true;
                        }
                        else
                        {
                            unmeasurable = true;
                        }
                    };
                    // We first compare instantiations where the type parameter is replaced with
                    // marker types that have a known subtype relationship. From this we can infer
                    // invariance, covariance, contravariance or bivariance.
                    var typeWithSuper = CreateMarkerType(symbol, tp, markerSuperType);
                    var typeWithSub = CreateMarkerType(symbol, tp, markerSubType);
                    var v = (IsTypeAssignableTo(typeWithSub, typeWithSuper) ? VarianceFlags.Covariant : 0) |
                        (IsTypeAssignableTo(typeWithSuper, typeWithSub) ? VarianceFlags.Contravariant : 0);
                    // If the instantiations appear to be related bivariantly it may be because the
                    // type parameter is independent (i.e. it isn't witnessed anywhere in the generic
                    // type). To determine this we compare instantiations where the type parameter is
                    // replaced with marker types that are known to be unrelated.
                    if (v == VarianceFlags.Bivariant && IsTypeAssignableTo(CreateMarkerType(symbol, tp, markerOtherType), typeWithSuper))
                    {
                        v = VarianceFlags.Independent;
                    }
                    outofbandVarianceMarkerHandler = oldHandler;
                    if (unmeasurable)
                    {
                        v |= VarianceFlags.Unmeasurable;
                    }
                    if (unreliable)
                    {
                        v |= VarianceFlags.Unreliable;
                    }
                    variance = v;
                }
                variances.Add(variance.Value);
            }
            if (!oldVarianceComputation)
            {
                inVarianceComputation = false;
                resolutionStart = saveResolutionStart;
            }
            links = [.. variances];
            variancesOfSymbols[symbol] = links;
        }
        return links;
    }

    // checker.ts createMarkerType
    private Type CreateMarkerType(SymbolRef symbol, Type source, Type target)
    {
        var mapper = MakeUnaryTypeMapper(source, target);
        var type = GetDeclaredTypeOfSymbol(symbol);
        if (IsErrorType(type))
        {
            return type;
        }
        Type result;
        if ((SymbolFlagsOf(symbol) & SymbolFlags.TypeAlias) != 0)
        {
            result = GetTypeAliasInstantiation(symbol, InstantiateTypes(GetTypeAliasTypeParameters(symbol)!, mapper, out _));
        }
        else
        {
            result = CreateTypeReference(type, InstantiateTypes(Types.Elements(Types.Object(type).TypeParameters).ToArray(), mapper, out _));
        }
        markerTypes.Add(result);
        return result;
    }

    // checker.ts isMarkerType
    private bool IsMarkerType(Type type) => markerTypes.Contains(type);

    // checker.ts hasCovariantVoidArgument: whether the given type reference has a 'void' type argument for a
    // covariant type parameter.
    private bool HasCovariantVoidArgument(ReadOnlySpan<Type> typeArguments, VarianceFlags[] variances)
    {
        for (var i = 0; i < variances.Length; i++)
        {
            if ((variances[i] & VarianceFlags.VarianceMask) == VarianceFlags.Covariant && (Types.Flags(typeArguments[i]) & TypeFlags.Void) != 0)
            {
                return true;
            }
        }
        return false;
    }

    // ---- relation keys ----

    // checker.ts isUnconstrainedTypeParameter
    private bool IsUnconstrainedTypeParameter(Type type) => (Types.Flags(type) & TypeFlags.TypeParameter) != 0 && GetConstraintOfTypeParameter(type).IsNone;

    // checker.ts isNonDeferredTypeReference
    private bool IsNonDeferredTypeReference(Type type) => (Types.ObjectFlagsOf(type) & ObjectFlags.Reference) != 0 && Types.Object(type).Node.IsNone;

    // checker.ts isTypeReferenceWithGenericArguments
    private bool IsTypeReferenceWithGenericArguments(Type type)
    {
        if (!IsNonDeferredTypeReference(type))
        {
            return false;
        }
        foreach (var t in GetTypeArguments(type).ToArray())
        {
            if ((Types.Flags(t) & TypeFlags.TypeParameter) != 0 || IsTypeReferenceWithGenericArguments(t))
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts getGenericTypeReferenceRelationKey
    private string GetGenericTypeReferenceRelationKey(Type source, Type target, string postFix, bool ignoreConstraints)
    {
        var typeParameters = new List<Type>();
        var constraintMarker = "";
        var sourceId = GetTypeReferenceId(source, 0);
        var targetId = GetTypeReferenceId(target, 0);
        return constraintMarker + sourceId + "," + targetId + postFix;

        // getTypeReferenceId(A<T, number, U>) returns "111=0-12=1" where A.id=111 and number.id=12
        string GetTypeReferenceId(Type type, int depth)
        {
            var result = new System.Text.StringBuilder(Types.Object(type).Target.Id.ToString(CultureInfo.InvariantCulture));
            foreach (var t in GetTypeArguments(type).ToArray())
            {
                if ((Types.Flags(t) & TypeFlags.TypeParameter) != 0)
                {
                    if (ignoreConstraints || IsUnconstrainedTypeParameter(t))
                    {
                        var index = typeParameters.IndexOf(t);
                        if (index < 0)
                        {
                            index = typeParameters.Count;
                            typeParameters.Add(t);
                        }
                        result.Append('=').Append(index.ToString(CultureInfo.InvariantCulture));
                        continue;
                    }
                    // We mark type references that reference constrained type parameters such that we know to obtain
                    // and look for a "broadest equivalent key" in the cache.
                    constraintMarker = "*";
                }
                else if (depth < 4 && IsTypeReferenceWithGenericArguments(t))
                {
                    result.Append('<').Append(GetTypeReferenceId(t, depth + 1)).Append('>');
                    continue;
                }
                result.Append('-').Append(t.Id.ToString(CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }
    }

    /// <summary>
    /// checker.ts getRelationKey: to improve caching, the relation key for two generic types uses the target's id plus
    /// ids of the type parameters; for other cases, the types ids are used.
    /// </summary>
    private string GetRelationKey(Type source, Type target, IntersectionState intersectionState, Relation relation, bool ignoreConstraints)
    {
        if (relation == identityRelation && source.Id > target.Id)
        {
            (source, target) = (target, source);
        }
        var postFix = intersectionState != 0 ? ":" + ((int)intersectionState).ToString(CultureInfo.InvariantCulture) : "";
        return IsTypeReferenceWithGenericArguments(source) && IsTypeReferenceWithGenericArguments(target)
            ? GetGenericTypeReferenceRelationKey(source, target, postFix, ignoreConstraints)
            : source.Id.ToString(CultureInfo.InvariantCulture) + "," + target.Id.ToString(CultureInfo.InvariantCulture) + postFix;
    }

    // ---- properties of classes ----

    // checker.ts forEachProperty: invoke the callback for each underlying property symbol of the given symbol and
    // return the first value that isn't false.
    private bool ForEachProperty(SymbolRef prop, Func<SymbolRef, bool> callback)
    {
        if ((GetCheckFlags(prop) & CheckFlags.Synthetic) != 0)
        {
            var containingType = ValueSymbolLinks.Get(prop).ContainingType;
            foreach (var t in Types.Elements(Types.Union(containingType).Types).ToArray())
            {
                var p = GetPropertyOfType(t, NameOf(prop));
                if (!p.IsNone && ForEachProperty(p, callback))
                {
                    return true;
                }
            }
            return false;
        }
        return callback(prop);
    }

    /// <summary>checker.ts getDeclaringClass: the declaring class type of a property (None if not declared in a class).</summary>
    internal Type GetDeclaringClass(SymbolRef prop)
    {
        var parent = ParentOf(prop);
        return !parent.IsNone && (SymbolFlagsOf(parent) & SymbolFlags.Class) != 0 ? GetDeclaredTypeOfSymbol(GetParentOfSymbol(prop)) : Type.None;
    }

    /// <summary>checker.ts getTypeOfPropertyInBaseClass.</summary>
    internal Type GetTypeOfPropertyInBaseClass(SymbolRef property)
    {
        var classType = GetDeclaringClass(property);
        if (classType.IsNone)
        {
            return Type.None;
        }
        var baseTypes = GetBaseTypes(classType);
        return baseTypes.Count > 0 ? GetTypeOfPropertyOfType(baseTypes[0], NameOf(property)) : Type.None;
    }

    // checker.ts isPropertyInClassDerivedFrom: whether some underlying source property is declared in a class that
    // derives from the given base class.
    private bool IsPropertyInClassDerivedFrom(SymbolRef prop, Type baseClass) =>
        ForEachProperty(prop, sp =>
        {
            var sourceClass = GetDeclaringClass(sp);
            return !sourceClass.IsNone && HasBaseType(sourceClass, baseClass);
        });

    // checker.ts isValidOverrideOf: whether the source property is a valid override of protected parts of the target property.
    private bool IsValidOverrideOf(SymbolRef sourceProp, SymbolRef targetProp) =>
        !ForEachProperty(targetProp, tp =>
            (GetDeclarationModifierFlagsFromSymbol(tp) & ModifierFlags.Protected) != 0 && !IsPropertyInClassDerivedFrom(sourceProp, GetDeclaringClass(tp)));

    // ---- deeply nested types ----

    /// <summary>
    /// checker.ts isDeeplyNestedType: whether the stack contains maxDepth or more occurrences of types with the same
    /// recursion identity as the given type (only occurrences with increasing type ids count).
    /// </summary>
    internal bool IsDeeplyNestedType(Type type, List<Type> stack, int depth, int maxDepth = 3)
    {
        if (depth >= maxDepth)
        {
            if ((Types.ObjectFlagsOf(type) & ObjectFlags.InstantiatedMapped) == ObjectFlags.InstantiatedMapped)
            {
                type = GetMappedTargetWithSymbol(type);
            }
            if ((Types.Flags(type) & TypeFlags.Intersection) != 0)
            {
                foreach (var t in Types.Elements(Types.Union(type).Types).ToArray())
                {
                    if (IsDeeplyNestedType(t, stack, depth, maxDepth))
                    {
                        return true;
                    }
                }
                return false;
            }
            var identity = GetRecursionIdentity(type);
            var count = 0;
            var lastTypeId = 0;
            for (var i = 0; i < depth; i++)
            {
                var t = stack[i];
                if (HasMatchingRecursionIdentity(t, identity))
                {
                    // We only count occurrences with a higher type id than the previous occurrence, since higher
                    // type ids are an indicator of newer instantiations caused by recursion.
                    if (t.Id >= lastTypeId)
                    {
                        count++;
                        if (count >= maxDepth)
                        {
                            return true;
                        }
                    }
                    lastTypeId = t.Id;
                }
            }
        }
        return false;
    }

    // checker.ts getMappedTargetWithSymbol: unwrap nested homomorphic mapped types and return the deepest target type
    // that has a symbol.
    private Type GetMappedTargetWithSymbol(Type type)
    {
        Type target;
        while ((Types.ObjectFlagsOf(type) & ObjectFlags.InstantiatedMapped) == ObjectFlags.InstantiatedMapped &&
            !(target = GetModifiersTypeFromMappedType(type)).IsNone &&
            (!Types.Symbol(target).IsNone || ((Types.Flags(target) & TypeFlags.Intersection) != 0 && ExistsType(Types.Elements(Types.Union(target).Types), t => !Types.Symbol(t).IsNone))))
        {
            type = target;
        }
        return type;
    }

    // checker.ts hasMatchingRecursionIdentity
    private bool HasMatchingRecursionIdentity(Type type, object identity)
    {
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.InstantiatedMapped) == ObjectFlags.InstantiatedMapped)
        {
            type = GetMappedTargetWithSymbol(type);
        }
        if ((Types.Flags(type) & TypeFlags.Intersection) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(type).Types).ToArray())
            {
                if (HasMatchingRecursionIdentity(t, identity))
                {
                    return true;
                }
            }
            return false;
        }
        return GetRecursionIdentity(type).Equals(identity);
    }

    // ---- identity of properties and signatures ----

    /// <summary>checker.ts isPropertyIdenticalTo.</summary>
    internal bool IsPropertyIdenticalTo(SymbolRef sourceProp, SymbolRef targetProp) => CompareProperties(sourceProp, targetProp, CompareTypesIdentical) != Ternary.False;

    /// <summary>
    /// checker.ts compareProperties: two members are identical when they are public properties with identical names,
    /// optionality, and types, or private or protected properties originating in the same declaration with identical types.
    /// </summary>
    internal int CompareProperties(SymbolRef sourceProp, SymbolRef targetProp, Func<Type, Type, int> compareTypes)
    {
        if (sourceProp == targetProp)
        {
            return Ternary.True;
        }
        var sourcePropAccessibility = GetDeclarationModifierFlagsFromSymbol(sourceProp) & ModifierFlags.NonPublicAccessibilityModifier;
        var targetPropAccessibility = GetDeclarationModifierFlagsFromSymbol(targetProp) & ModifierFlags.NonPublicAccessibilityModifier;
        if (sourcePropAccessibility != targetPropAccessibility)
        {
            return Ternary.False;
        }
        if (sourcePropAccessibility != 0)
        {
            if (GetTargetSymbol(sourceProp) != GetTargetSymbol(targetProp))
            {
                return Ternary.False;
            }
        }
        else if ((SymbolFlagsOf(sourceProp) & SymbolFlags.Optional) != (SymbolFlagsOf(targetProp) & SymbolFlags.Optional))
        {
            return Ternary.False;
        }
        if (IsReadonlySymbol(sourceProp) != IsReadonlySymbol(targetProp))
        {
            return Ternary.False;
        }
        return compareTypes(GetNonMissingTypeOfSymbol(sourceProp), GetNonMissingTypeOfSymbol(targetProp));
    }

    // checker.ts isMatchingSignature
    private bool IsMatchingSignature(Signature source, Signature target, bool partialMatch)
    {
        var sourceParameterCount = GetParameterCount(source);
        var targetParameterCount = GetParameterCount(target);
        var sourceMinArgumentCount = GetMinArgumentCount(source);
        var targetMinArgumentCount = GetMinArgumentCount(target);
        var sourceHasRestParameter = HasEffectiveRestParameter(source);
        var targetHasRestParameter = HasEffectiveRestParameter(target);
        // A source signature matches a target signature if the two signatures have the same number of required,
        // optional, and rest parameters.
        if (sourceParameterCount == targetParameterCount && sourceMinArgumentCount == targetMinArgumentCount && sourceHasRestParameter == targetHasRestParameter)
        {
            return true;
        }
        // A source signature partially matches a target signature if the target signature has no fewer required
        // parameters
        return partialMatch && sourceMinArgumentCount <= targetMinArgumentCount;
    }

    /// <summary>checker.ts compareSignaturesIdentical (see signatureRelatedTo, compareSignaturesRelated).</summary>
    internal int CompareSignaturesIdentical(Signature source, Signature target, bool partialMatch, bool ignoreThisTypes, bool ignoreReturnTypes, Func<Type, Type, int> compareTypes)
    {
        if (source == target)
        {
            return Ternary.True;
        }
        if (!IsMatchingSignature(source, target, partialMatch))
        {
            return Ternary.False;
        }
        // Check that the two signatures have the same number of type parameters.
        var sourceTypeParameters = Types.Elements(Signatures.Record(source).TypeParameters).ToArray();
        var targetTypeParameters = Types.Elements(Signatures.Record(target).TypeParameters).ToArray();
        if (sourceTypeParameters.Length != targetTypeParameters.Length)
        {
            return Ternary.False;
        }
        // Check that type parameter constraints and defaults match. If they do, instantiate the source
        // signature with the type parameters of the target signature and continue the comparison.
        if (targetTypeParameters.Length > 0)
        {
            var mapper = CreateTypeMapper(sourceTypeParameters, targetTypeParameters);
            for (var i = 0; i < targetTypeParameters.Length; i++)
            {
                var s = sourceTypeParameters[i];
                var t = targetTypeParameters[i];
                if (!(s == t ||
                    (compareTypes(OrUnknown(InstantiateType(GetConstraintFromTypeParameter(s), mapper)), OrUnknown(GetConstraintFromTypeParameter(t))) != Ternary.False &&
                     compareTypes(OrUnknown(InstantiateType(GetDefaultFromTypeParameter(s), mapper)), OrUnknown(GetDefaultFromTypeParameter(t))) != Ternary.False)))
                {
                    return Ternary.False;
                }
            }
            source = InstantiateSignature(source, mapper, eraseTypeParameters: true);
        }
        var result = Ternary.True;
        if (!ignoreThisTypes)
        {
            var sourceThisType = GetThisTypeOfSignature(source);
            if (!sourceThisType.IsNone)
            {
                var targetThisType = GetThisTypeOfSignature(target);
                if (!targetThisType.IsNone)
                {
                    var related = compareTypes(sourceThisType, targetThisType);
                    if (related == Ternary.False)
                    {
                        return Ternary.False;
                    }
                    result &= related;
                }
            }
        }
        var targetLen = GetParameterCount(target);
        for (var i = 0; i < targetLen; i++)
        {
            var s = GetTypeAtPosition(source, i);
            var t = GetTypeAtPosition(target, i);
            var related = compareTypes(t, s);
            if (related == Ternary.False)
            {
                return Ternary.False;
            }
            result &= related;
        }
        if (!ignoreReturnTypes)
        {
            var sourceTypePredicate = GetTypePredicateOfSignature(source);
            var targetTypePredicate = GetTypePredicateOfSignature(target);
            result &= sourceTypePredicate is not null || targetTypePredicate is not null
                ? CompareTypePredicatesIdentical(sourceTypePredicate, targetTypePredicate, compareTypes)
                : compareTypes(GetReturnTypeOfSignature(source), GetReturnTypeOfSignature(target));
        }
        return result;
    }

    private Type OrUnknown(Type type) => type.IsNone ? unknownType : type;

    // checker.ts compareTypePredicatesIdentical
    private static int CompareTypePredicatesIdentical(TypePredicateInfo? source, TypePredicateInfo? target, Func<Type, Type, int> compareTypes) =>
        !(source is not null && target is not null && TypePredicateKindsMatch(source, target)) ? Ternary.False :
        source.Type == target.Type ? Ternary.True :
        !source.Type.IsNone && !target.Type.IsNone ? compareTypes(source.Type, target.Type) :
        Ternary.False;

    // checker.ts typePredicateKindsMatch
    private static bool TypePredicateKindsMatch(TypePredicateInfo a, TypePredicateInfo b) =>
        a.Kind == b.Kind && a.ParameterIndex == b.ParameterIndex;
}
