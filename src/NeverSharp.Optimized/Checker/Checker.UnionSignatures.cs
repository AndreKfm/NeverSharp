// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (module C5; the functions are listed below)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (details below).
// Functions (the signature helpers of call resolution and contextual typing: createSignature,
// cloneSignature, createUnionSignature, getOptionalCallSignature, getOrCreateTypeFromSignature,
// getResolvedTypeWithoutAbstractConstructSignatures, getSingleCallOrConstructSignature, signatureHasLiteralTypes,
// acceptsVoid, getTypeOfFirstParameterOfSignature, getRestTypeOfSignature, the union and intersection signatures of
// resolveUnionTypeMembers / resolveIntersectionTypeMembers (getUnionSignatures, combineSignaturesOfUnionMembers,
// getUnionIndexInfos, findMixins, appendSignatures); the other signature helpers: Checker.Signatures.cs, C4)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5. The resolved members of union and intersection types are kept
// in a side table (their type records have no object data).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private Signature anySignatureValue;
    private Signature unknownSignatureValue;
    private Signature resolvingSignatureValue;
    private Signature silentNeverSignatureValue;
    private readonly Dictionary<Signature, Type> isolatedSignatureTypes = [];
    private readonly Dictionary<(Signature, SignatureFlags), Signature> optionalCallSignatures = [];
    private readonly Dictionary<Type, Type> typesWithoutAbstractConstructSignatures = [];

    // checker.ts anySignature, unknownSignature, resolvingSignature, silentNeverSignature
    private Signature anySignature => anySignatureValue.IsNone ? anySignatureValue = CreateSignature(NodeRef.None, TypeList.Empty, SymbolRef.None, [], anyType, 0, SignatureFlags.None) : anySignatureValue;

    private Signature unknownSignature => unknownSignatureValue.IsNone ? unknownSignatureValue = CreateSignature(NodeRef.None, TypeList.Empty, SymbolRef.None, [], errorType, 0, SignatureFlags.None) : unknownSignatureValue;

    private Signature resolvingSignature => resolvingSignatureValue.IsNone ? resolvingSignatureValue = CreateSignature(NodeRef.None, TypeList.Empty, SymbolRef.None, [], anyType, 0, SignatureFlags.None) : resolvingSignatureValue;

    private Signature silentNeverSignature => silentNeverSignatureValue.IsNone ? silentNeverSignatureValue = CreateSignature(NodeRef.None, TypeList.Empty, SymbolRef.None, [], silentNeverType, 0, SignatureFlags.None) : silentNeverSignatureValue;

    /// <summary>checker.ts createSignature.</summary>
    internal Signature CreateSignature(NodeRef declaration, TypeList typeParameters, SymbolRef thisParameter, ReadOnlySpan<SymbolRef> parameters, Type resolvedReturnType, int minArgumentCount, SignatureFlags flags) =>
        CreateSignature(declaration, typeParameters, thisParameter, Signatures.StoreParameters(parameters), resolvedReturnType, minArgumentCount, flags);

    /// <summary>checker.ts createSignature with a stored parameter list (shared as TypeScript shares the array; module AR1).</summary>
    internal Signature CreateSignature(NodeRef declaration, TypeList typeParameters, SymbolRef thisParameter, ItemRange<SymbolRef> parameters, Type resolvedReturnType, int minArgumentCount, SignatureFlags flags)
    {
        var signature = Signatures.Create(flags, declaration, typeParameters, thisParameter, minArgumentCount);
        ref var record = ref Signatures.Record(signature);
        record.Parameters = parameters;
        record.ResolvedReturnType = resolvedReturnType;
        return signature;
    }

    /// <summary>checker.ts cloneSignature.</summary>
    internal Signature CloneSignature(Signature sig)
    {
        var source = Signatures.Record(sig);
        var result = CreateSignature(source.Declaration, source.TypeParameters, source.ThisParameter, source.Parameters, Type.None, source.MinArgumentCount, source.Flags & SignatureFlags.PropagatingFlags);
        ref var record = ref Signatures.Record(result);
        record.Target = source.Target;
        record.Mapper = source.Mapper;
        record.CompositeSignatures = source.CompositeSignatures;
        record.CompositeKind = source.CompositeKind;
        record.SyntheticFunctionTypeDeclaration = source.SyntheticFunctionTypeDeclaration;
        return result;
    }

    /// <summary>checker.ts createUnionSignature.</summary>
    internal Signature CreateUnionSignature(Signature signature, Signature[] unionSignatures)
    {
        var result = CloneSignature(signature);
        ref var record = ref Signatures.Record(result);
        record.CompositeSignatures = unionSignatures;
        record.CompositeKind = TypeFlags.Union;
        record.Target = Signature.None;
        record.Mapper = 0;
        return result;
    }

    /// <summary>checker.ts getOptionalCallSignature.</summary>
    internal Signature GetOptionalCallSignature(Signature signature, SignatureFlags callChainFlags)
    {
        if ((Signatures.Record(signature).Flags & SignatureFlags.CallChainFlags) == callChainFlags)
        {
            return signature;
        }
        if (!optionalCallSignatures.TryGetValue((signature, callChainFlags), out var result))
        {
            // createOptionalCallSignature
            result = CloneSignature(signature);
            Signatures.Record(result).Flags |= callChainFlags;
            optionalCallSignatures[(signature, callChainFlags)] = result;
        }
        return result;
    }

    // the return type of a composite signature (getReturnTypeOfSignature: the union or intersection of the return types)
    private Type GetUnionOrIntersectionTypeOfReturnTypes(Signature[] signatures, TypeFlags compositeKind)
    {
        var types = Array.ConvertAll(signatures, GetReturnTypeOfSignature);
        return compositeKind == TypeFlags.Union ? GetUnionType(types, UnionReduction.Subtype) : GetIntersectionType(types);
    }

    /// <summary>checker.ts getOrCreateTypeFromSignature.</summary>
    internal Type GetOrCreateTypeFromSignature(Signature signature)
    {
        // There are two ways to declare a construct signature, one is by declaring a class constructor
        // using the constructor keyword, and the other is declaring a bare construct signature in an
        // object type literal or interface (using the new keyword). Each way of declaring a constructor
        // will result in a different declaration kind.
        if (!isolatedSignatureTypes.TryGetValue(signature, out var type))
        {
            var declaration = Signatures.Record(signature).Declaration;
            var kind = declaration.IsNone ? SyntaxKind.Unknown : Kind(declaration);
            // If declaration is undefined, it is likely to be the signature of the default constructor.
            var isConstructor = declaration.IsNone && !Signatures.Record(signature).SyntheticFunctionTypeDeclaration || kind is SyntaxKind.Constructor or SyntaxKind.ConstructSignature or SyntaxKind.ConstructorType;
            type = CreateObjectType(ObjectFlags.Anonymous | ObjectFlags.SingleSignatureType, declaration.IsNone ? SymbolRef.None : NodeSymbol(declaration));
            SetStructuredTypeMembers(type, emptySymbols, !isConstructor ? [signature] : [], isConstructor ? [signature] : [], []);
            isolatedSignatureTypes[signature] = type;
        }
        return type;
    }

    /// <summary>checker.ts getResolvedTypeWithoutAbstractConstructSignatures.</summary>
    internal Type GetResolvedTypeWithoutAbstractConstructSignatures(Type type, ObjectTypeData resolved)
    {
        if (resolved.ConstructSignatures.Count == 0)
        {
            return type;
        }
        if (typesWithoutAbstractConstructSignatures.TryGetValue(type, out var cached))
        {
            return cached;
        }
        var constructSignatures = Types.View(resolved.ConstructSignatures).FindAll(signature => (Signatures.Record(signature).Flags & SignatureFlags.Abstract) == 0);
        if (constructSignatures.Count == resolved.ConstructSignatures.Count)
        {
            return type;
        }
        var typeCopy = CreateAnonymousType(Types.Symbol(type), resolved.Members!, Types.View(resolved.CallSignatures), Types.Keep(constructSignatures), Types.View(resolved.IndexInfos));
        typesWithoutAbstractConstructSignatures[type] = typeCopy;
        typesWithoutAbstractConstructSignatures[typeCopy] = typeCopy;
        return typeCopy;
    }

    /// <summary>checker.ts getSingleCallOrConstructSignature.</summary>
    internal Signature GetSingleCallOrConstructSignature(Type type)
    {
        var call = GetSingleSignature(type, SignatureKind.Call, allowMembers: false);
        return !call.IsNone ? call : GetSingleSignature(type, SignatureKind.Construct, allowMembers: false);
    }

    /// <summary>checker.ts signatureHasLiteralTypes.</summary>
    internal bool SignatureHasLiteralTypes(Signature signature) => (Signatures.Record(signature).Flags & SignatureFlags.HasLiteralTypes) != 0;

    // checker.ts acceptsVoid
    private bool AcceptsVoid(Type t) => (Types.Flags(t) & TypeFlags.Void) != 0;

    /// <summary>checker.ts getTypeOfFirstParameterOfSignatureWithFallback.</summary>
    internal Type GetTypeOfFirstParameterOfSignatureWithFallback(Signature signature, Type fallbackType) =>
        ParametersOf(signature).Length > 0 ? GetTypeAtPosition(signature, 0) : fallbackType;

    /// <summary>checker.ts getRestTypeOfSignature.</summary>
    internal Type GetRestTypeOfSignature(Signature signature)
    {
        var type = TryGetRestTypeOfSignature(signature);
        return type.IsNone ? anyType : type;
    }

    /// <summary>checker.ts tryGetRestTypeOfSignature.</summary>
    internal Type TryGetRestTypeOfSignature(Signature signature)
    {
        if (SignatureHasRestParameter(signature))
        {
            var sigRestType = GetTypeOfSymbol(ParametersOf(signature)[^1]);
            var restType = IsTupleType(sigRestType) ? GetRestTypeOfTupleType(sigRestType) : sigRestType;
            return restType.IsNone ? Type.None : GetIndexTypeOfType(restType, numberType);
        }
        return Type.None;
    }

    // ---- the members of union and intersection types ----

    // checker.ts resolveStructuredTypeMembers for union and intersection types
    private ObjectTypeData ResolveUnionOrIntersectionTypeMembers(Type type)
    {
        if (Types.Union(type).ResolvedMembers != 0)
        {
            return Types.ObjectData(Types.Union(type).ResolvedMembers);
        }
        // (members resolving: the empty lists are in place while the signatures are computed)
        var record = Types.NewObjectData();
        Types.Union(type).ResolvedMembers = record;
        var data = Types.ObjectData(record);
        data.Members = emptySymbols;
        data.MembersResolved = true;
        var types = Types.Elements(Types.Union(type).Types).ToArray();
        if ((Types.Flags(type) & TypeFlags.Union) != 0)
        {
            // resolveUnionTypeMembers
            var callSignatures = GetUnionSignatures(Array.ConvertAll(types, t => t == globalFunctionType ? [unknownSignature] : GetSignaturesOfType(t, SignatureKind.Call)));
            var constructSignatures = GetUnionSignatures(Array.ConvertAll(types, t => GetSignaturesOfType(t, SignatureKind.Construct)));
            var indexInfos = GetUnionIndexInfos(types);
            data.CallSignatures = Types.Store(callSignatures);
            data.ConstructSignatures = Types.Store(constructSignatures);
            data.IndexInfos = Types.Store(indexInfos);
        }
        else
        {
            // resolveIntersectionTypeMembers
            List<Signature>? callSignatures = null;
            List<Signature>? constructSignatures = null;
            List<IndexInfo>? indexInfos = null;
            var mixinFlags = FindMixins(types);
            var mixinCount = Array.FindAll(mixinFlags, b => b).Length;
            for (var i = 0; i < types.Length; i++)
            {
                var t = types[i];
                // When an intersection type contains mixin constructor types, the construct signatures from
                // those types are discarded and their return types are mixed into the return types of all
                // other construct signatures in the intersection type. For example, the intersection type
                // '{ new(...args: any[]) => A } & { new(s: string) => B }' has a single construct signature
                // 'new(s: string) => A & B'.
                if (!mixinFlags[i])
                {
                    var signatures = GetSignaturesOfType(t, SignatureKind.Construct);
                    if (signatures.Count > 0 && mixinCount > 0)
                    {
                        var index = i;
                        signatures = signatures.ConvertAll<Signature>(s =>
                        {
                            var clone = CloneSignature(s);
                            var mixinReturnType = IncludeMixinType(GetReturnTypeOfSignature(s), types, mixinFlags, index); // (before the record is written)
                            Signatures.Record(clone).ResolvedReturnType = mixinReturnType;
                            return clone;
                        });
                    }
                    constructSignatures = AppendSignatures(constructSignatures, signatures);
                }
                callSignatures = AppendSignatures(callSignatures, GetSignaturesOfType(t, SignatureKind.Call));
                foreach (var newInfo in GetIndexInfosOfType(t))
                {
                    indexInfos = AppendIndexInfo(indexInfos, newInfo, union: false);
                }
            }
            data.CallSignatures = callSignatures is null ? default : Types.Store(callSignatures);
            data.ConstructSignatures = constructSignatures is null ? default : Types.Store(constructSignatures);
            data.IndexInfos = indexInfos is null ? default : Types.Store(indexInfos);
        }
        return data;
    }

    // checker.ts findMatchingSignature
    private Signature FindMatchingSignature(List<Signature> signatureList, Signature signature, bool partialMatch, bool ignoreThisTypes, bool ignoreReturnTypes) =>
        FindMatchingSignature(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(signatureList), signature, partialMatch, ignoreThisTypes, ignoreReturnTypes);

    // checker.ts findMatchingSignature
    private Signature FindMatchingSignature(Items<Signature> signatureList, Signature signature, bool partialMatch, bool ignoreThisTypes, bool ignoreReturnTypes) =>
        FindMatchingSignature(signatureList.AsSpan(), signature, partialMatch, ignoreThisTypes, ignoreReturnTypes);

    // checker.ts findMatchingSignature
    private Signature FindMatchingSignature(ReadOnlySpan<Signature> signatureList, Signature signature, bool partialMatch, bool ignoreThisTypes, bool ignoreReturnTypes)
    {
        foreach (var s in signatureList)
        {
            if (CompareSignaturesIdentical(s, signature, partialMatch, ignoreThisTypes, ignoreReturnTypes, partialMatch ? CompareTypesSubtypeOf : CompareTypesIdentical) != Ternary.False)
            {
                return s;
            }
        }
        return Signature.None;
    }

    // checker.ts findMatchingSignatures
    private List<Signature>? FindMatchingSignatures(Items<Signature>[] signatureLists, Signature signature, int listIndex)
    {
        if (Signatures.Record(signature).TypeParameters.Count > 0)
        {
            // We require an exact match for generic signatures, so we only return signatures from the first
            // signature list and only if they have exact matches in the other signature lists.
            if (listIndex > 0)
            {
                return null;
            }
            for (var i = 1; i < signatureLists.Length; i++)
            {
                if (FindMatchingSignature(signatureLists[i], signature, partialMatch: false, ignoreThisTypes: false, ignoreReturnTypes: false).IsNone)
                {
                    return null;
                }
            }
            return [signature];
        }
        List<Signature>? result = null;
        for (var i = 0; i < signatureLists.Length; i++)
        {
            // Allow matching non-generic signatures to have excess parameters (as a fallback if exact parameter match is not found) and different return types.
            // Prefer matching this types if possible.
            var match = i == listIndex ? signature : FindMatchingSignature(signatureLists[i], signature, partialMatch: false, ignoreThisTypes: false, ignoreReturnTypes: true);
            if (match.IsNone)
            {
                match = FindMatchingSignature(signatureLists[i], signature, partialMatch: true, ignoreThisTypes: false, ignoreReturnTypes: true);
            }
            if (match.IsNone)
            {
                return null;
            }
            result ??= [];
            if (!result.Contains(match))
            {
                result.Add(match);
            }
        }
        return result;
    }

    // checker.ts getUnionSignatures
    private List<Signature> GetUnionSignatures(Items<Signature>[] signatureLists)
    {
        List<Signature>? result = null;
        int? indexWithLengthOverOne = null;
        for (var i = 0; i < signatureLists.Length; i++)
        {
            if (signatureLists[i].Count == 0)
            {
                return [];
            }
            if (signatureLists[i].Count > 1)
            {
                indexWithLengthOverOne = indexWithLengthOverOne is null ? i : -1; // -1 is a signal there are multiple overload sets
            }
            foreach (var signature in signatureLists[i])
            {
                // Only process signatures with parameter lists that aren't already in the result list
                if (result is null || FindMatchingSignature(result, signature, partialMatch: false, ignoreThisTypes: false, ignoreReturnTypes: true).IsNone)
                {
                    var unionSignatures = FindMatchingSignatures(signatureLists, signature, i);
                    if (unionSignatures is not null)
                    {
                        var s = signature;
                        // Union the result types when more than one signature matches
                        if (unionSignatures.Count > 1)
                        {
                            var thisParameter = Signatures.Record(signature).ThisParameter;
                            var firstThisParameterOfUnionSignatures = SymbolRef.None;
                            foreach (var sig in unionSignatures)
                            {
                                if (!Signatures.Record(sig).ThisParameter.IsNone)
                                {
                                    firstThisParameterOfUnionSignatures = Signatures.Record(sig).ThisParameter;
                                    break;
                                }
                            }
                            if (!firstThisParameterOfUnionSignatures.IsNone)
                            {
                                var thisTypes = new List<Type>();
                                foreach (var sig in unionSignatures)
                                {
                                    if (!Signatures.Record(sig).ThisParameter.IsNone)
                                    {
                                        thisTypes.Add(GetTypeOfSymbol(Signatures.Record(sig).ThisParameter));
                                    }
                                }
                                var thisType = GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(thisTypes));
                                thisParameter = CreateSymbolWithType(firstThisParameterOfUnionSignatures, thisType);
                            }
                            s = CreateUnionSignature(signature, [.. unionSignatures]);
                            Signatures.Record(s).ThisParameter = thisParameter;
                        }
                        (result ??= []).Add(s);
                    }
                }
            }
        }
        if ((result is null || result.Count == 0) && indexWithLengthOverOne != -1)
        {
            // No sufficiently similar signature existed to subsume all the other signatures in the union - time to see if we can make a single
            // signature that handles all over them. We only do this when there are overloads in only one constituent.
            // (Overloads are conditional in nature and having overloads in multiple constituents would necessitate making a power set of
            // signatures from the type, whose ordering would be non-obvious)
            var masterList = signatureLists[indexWithLengthOverOne ?? 0];
            List<Signature>? results = [.. masterList];
            foreach (var signatures in signatureLists)
            {
                if (!signatures.SameAs(masterList))
                {
                    var signature = signatures[0];
                    var typeParameters = Signatures.Record(signature).TypeParameters;
                    results = typeParameters.Count > 0 && results!.Exists(s => Signatures.Record(s).TypeParameters.Count > 0 && !CompareTypeParametersIdentical(typeParameters, Signatures.Record(s).TypeParameters))
                        ? null
                        : results!.ConvertAll(sig => CombineSignaturesOfUnionMembers(sig, signature));
                    if (results is null)
                    {
                        break;
                    }
                }
            }
            result = results;
        }
        return result ?? [];
    }

    // checker.ts compareTypeParametersIdentical
    private bool CompareTypeParametersIdentical(TypeList sourceParams, TypeList targetParams)
    {
        if (sourceParams.Count != targetParams.Count)
        {
            return false;
        }
        if (sourceParams.Count == 0)
        {
            return true;
        }
        var sources = Types.Elements(sourceParams).ToArray();
        var targets = Types.Elements(targetParams).ToArray();
        var mapper = CreateTypeMapper(targets, sources);
        for (var i = 0; i < sources.Length; i++)
        {
            var source = sources[i];
            var target = targets[i];
            if (source == target)
            {
                continue;
            }
            // We instantiate the target type parameter constraints into the source types so we can recognize `<T, U extends T>` as the same as `<A, B extends A>`
            var sourceConstraint = GetConstraintFromTypeParameter(source);
            var targetConstraint = GetConstraintFromTypeParameter(target);
            if (!IsTypeIdenticalTo(sourceConstraint.IsNone ? unknownType : sourceConstraint, InstantiateType(targetConstraint.IsNone ? unknownType : targetConstraint, mapper)))
            {
                return false;
            }
        }
        return true;
    }

    // checker.ts combineUnionThisParam
    private SymbolRef CombineUnionThisParam(SymbolRef left, SymbolRef right, TypeMapper mapper)
    {
        if (left.IsNone || right.IsNone)
        {
            return !left.IsNone ? left : right;
        }
        // A signature `this` type might be a read or a write position... It's very possible that it should be invariant
        // and we should refuse to merge signatures if there are `this` types and they do not match. However, so as to be
        // permissive when calling, for now, we'll intersect the `this` types just like we do for param types in union signatures.
        var thisType = GetIntersectionType([GetTypeOfSymbol(left), InstantiateType(GetTypeOfSymbol(right), mapper)]);
        return CreateSymbolWithType(left, thisType);
    }

    // checker.ts combineUnionParameters
    private SymbolRef[] CombineUnionParameters(Signature left, Signature right, TypeMapper mapper)
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
            var unionParamType = GetIntersectionType([longestParamType, shorterParamType]);
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

    // checker.ts combineSignaturesOfUnionMembers
    private Signature CombineSignaturesOfUnionMembers(Signature left, Signature right)
    {
        var leftRecord = Signatures.Record(left);
        var rightRecord = Signatures.Record(right);
        var typeParams = leftRecord.TypeParameters.Count > 0 ? leftRecord.TypeParameters : rightRecord.TypeParameters;
        var paramMapper = TypeMapper.None;
        if (leftRecord.TypeParameters.Count > 0 && rightRecord.TypeParameters.Count > 0)
        {
            paramMapper = CreateTypeMapper(Types.Elements(rightRecord.TypeParameters), Types.Elements(leftRecord.TypeParameters).ToArray());
            // We just use the type parameter defaults from the first signature
        }
        var flags = (leftRecord.Flags | rightRecord.Flags) & (SignatureFlags.PropagatingFlags & ~SignatureFlags.HasRestParameter);
        var declaration = leftRecord.Declaration;
        var parameters = CombineUnionParameters(left, right, paramMapper);
        if (parameters.Length > 0 && (GetCheckFlags(parameters[^1]) & CheckFlags.RestParameter) != 0)
        {
            flags |= SignatureFlags.HasRestParameter;
        }
        var thisParam = CombineUnionThisParam(leftRecord.ThisParameter, rightRecord.ThisParameter, paramMapper);
        var minArgCount = Math.Max(leftRecord.MinArgumentCount, rightRecord.MinArgumentCount);
        var result = CreateSignature(declaration, typeParams, thisParam, parameters, Type.None, minArgCount, flags);
        ref var record = ref Signatures.Record(result);
        record.CompositeKind = TypeFlags.Union;
        var leftComposite = leftRecord.CompositeKind != TypeFlags.Intersection ? leftRecord.CompositeSignatures : null;
        record.CompositeSignatures = [.. leftComposite ?? [left], right];
        if (!paramMapper.IsNone)
        {
            Signatures.Record(result).Mapper = MapperHandle(leftRecord.CompositeKind != TypeFlags.Intersection && leftRecord.Mapper != 0 && leftRecord.CompositeSignatures is not null
                ? CombineTypeMappers(MapperAt(leftRecord.Mapper), paramMapper)
                : paramMapper);
        }
        else if (leftRecord.CompositeKind != TypeFlags.Intersection && leftRecord.Mapper != 0 && leftRecord.CompositeSignatures is not null)
        {
            Signatures.Record(result).Mapper = leftRecord.Mapper;
        }
        return result;
    }

    // checker.ts getUnionIndexInfos
    private List<IndexInfo> GetUnionIndexInfos(Type[] types)
    {
        var sourceInfos = GetIndexInfosOfType(types[0]);
        var result = new List<IndexInfo>();
        foreach (var info in sourceInfos)
        {
            var indexType = info.KeyType;
            if (Array.TrueForAll(types, t => GetIndexInfoOfType(t, indexType) is not null))
            {
                result.Add(new IndexInfo
                {
                    KeyType = indexType,
                    Type = GetUnionType(Array.ConvertAll(types, t => GetIndexTypeOfType(t, indexType))),
                    IsReadonly = Array.Exists(types, t => GetIndexInfoOfType(t, indexType)!.IsReadonly),
                });
            }
        }
        return result;
    }

    // checker.ts findMixins
    private bool[] FindMixins(Type[] types)
    {
        var constructorTypeCount = Array.FindAll(types, t => GetSignaturesOfType(t, SignatureKind.Construct).Count > 0).Length;
        var mixinFlags = Array.ConvertAll(types, IsMixinConstructorType);
        if (constructorTypeCount > 0 && constructorTypeCount == Array.FindAll(mixinFlags, b => b).Length)
        {
            var firstMixinIndex = Array.IndexOf(mixinFlags, true);
            mixinFlags[firstMixinIndex] = false;
        }
        return mixinFlags;
    }

    // checker.ts includeMixinType
    private Type IncludeMixinType(Type type, Type[] types, bool[] mixinFlags, int index)
    {
        var mixedTypes = new List<Type>();
        for (var i = 0; i < types.Length; i++)
        {
            if (i == index)
            {
                mixedTypes.Add(type);
            }
            else if (mixinFlags[i])
            {
                mixedTypes.Add(GetReturnTypeOfSignature(GetSignaturesOfType(types[i], SignatureKind.Construct)[0]));
            }
        }
        return GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(mixedTypes));
    }

    // checker.ts appendSignatures
    private List<Signature>? AppendSignatures(List<Signature>? signatures, Items<Signature> newSignatures)
    {
        foreach (var sig in newSignatures)
        {
            if (signatures is null || signatures.TrueForAll(s => CompareSignaturesIdentical(s, sig, partialMatch: false, ignoreThisTypes: false, ignoreReturnTypes: false, CompareTypesIdentical) == Ternary.False))
            {
                (signatures ??= []).Add(sig);
            }
        }
        return signatures;
    }
}
