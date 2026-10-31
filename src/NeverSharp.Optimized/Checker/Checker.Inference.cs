// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts (InferenceContext, InferenceInfo, InferenceFlags, InferencePriority) and
// src/compiler/checker.ts (type inference: the inference context and its mappers, inferTypes, getInferredType(s),
// reverse mapped types, getCommonSupertype/getCommonSubtype, getBaseSignature, getUniqueTypeParameters; listed below)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/inference.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C6 (replaces C5's inference seam). inferTypes' closure state is an
// InferenceState object. isFromInferenceBlockedSource and hasSkipDirectInferenceFlag are language-service only (false).
// Functions: createInferenceContext(Worker), cloneInferenceContext, make(Non)FixingMapperForContext,
// clearCachedInferences, addIntraExpressionInferenceSite, inferFromIntraExpressionSites, create/cloneInferenceInfo,
// cloneInferredPartOfContext, getMapperFromContext, createBackreferenceMapper, createOuterReturnMapper,
// isTypeParameterAtTopLevel(InReturnType), createEmptyObjectTypeFromStringLiteral, inferTypeForHomomorphicMappedType,
// isPartiallyInferableType, createReverseMappedType, getTypeOfReverseMappedSymbol, inferReverseMappedType(Worker),
// resolveReverseMappedTypeMembers, getLimitedConstraint, replaceIndexedAccess, instantiateReverseMappedType,
// getTypeFromInference, inferTypes and its nested functions, isTypeOrBaseIdenticalTo, isTypeCloselyMatchedBy,
// hasPrimitiveConstraint, unionObjectAndArrayLiteralCandidates, getContravariantInference, getCovariantInference,
// getInferredType, getDefaultTypeArgumentType, getInferredTypes, literalTypesWithSameBaseType, getCombinedTypeFlags,
// getCommonSupertype, getSingleCommonSupertype, getCommonSubtype, isTupleTypeStructureMatching, getBaseSignature,
// hasInferenceCandidates(OrDefault), hasOverlappingInferences, mergeInferences, getUniqueTypeParameters,
// hasTypeParameterByName, getUniqueTypeParameterName.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>types.ts InferenceFlags.</summary>
[Flags]
internal enum InferenceFlags
{
    None = 0,                   // No special inference behaviors
    NoDefault = 1 << 0,         // Infer silentNeverType for no inferences (otherwise anyType or unknownType)
    AnyDefault = 1 << 1,        // Infer anyType (in JS files) for no inferences (otherwise unknownType)
    SkippedGenericFunction = 1 << 2, // A generic function was skipped during inference
}

/// <summary>types.ts InferencePriority.</summary>
internal enum InferencePriority
{
    None = 0,
    NakedTypeVariable = 1 << 0,            // Naked type variable in union or intersection type
    SpeculativeTuple = 1 << 1,             // Speculative tuple inference
    SubstituteSource = 1 << 2,             // Source of inference originated within a substitution type's substitute
    HomomorphicMappedType = 1 << 3,        // Reverse inference for homomorphic mapped type
    PartialHomomorphicMappedType = 1 << 4, // Partial reverse inference for homomorphic mapped type
    MappedTypeConstraint = 1 << 5,         // Reverse inference for mapped type
    ContravariantConditional = 1 << 6,     // Conditional type in contravariant position
    ReturnType = 1 << 7,                   // Inference made from return type of generic function
    LiteralKeyof = 1 << 8,                 // Inference made from a string literal to a keyof T
    NoConstraints = 1 << 9,                // Don't infer from constraints of instantiable types
    AlwaysStrict = 1 << 10,                // Always use strict rules for contravariant inferences
    MaxValue = 1 << 11,                    // Seed for inference priority tracking

    PriorityImpliesCombination = ReturnType | MappedTypeConstraint | LiteralKeyof, // These priorities imply that the resulting type should be a combination of all candidates
    Circularity = -1,                      // Inference circularity (value less than all other priorities)
}

/// <summary>types.ts InferenceInfo.</summary>
internal sealed class InferenceInfo
{
    public Type TypeParameter;
    public List<Type>? Candidates;
    public List<Type>? ContraCandidates;
    public Type InferredType;            // None: not computed
    public InferencePriority? Priority;  // null: undefined
    public bool TopLevel;
    public bool IsFixed;
    public int? ImpliedArity;
}

/// <summary>types.ts InferenceContext.</summary>
internal sealed class InferenceContext
{
    public List<InferenceInfo> Inferences = [];
    public Signature Signature;          // None: undefined
    public InferenceFlags Flags;
    public Func<Type, Type, bool> CompareTypes = null!;
    public TypeMapper Mapper = null!;
    public TypeMapper NonFixingMapper = null!;
    public TypeMapper? ReturnMapper;
    public TypeMapper? OuterReturnMapper;
    public List<Type>? InferredTypeParameters;
    public List<(NodeRef Node, Type Type)>? IntraExpressionInferenceSites;
}

internal sealed partial class Checker
{
    private Func<Type, Type, bool>? compareTypesAssignableFunc;

    // checker.ts compareTypesAssignable as a TypeComparer
    private Func<Type, Type, bool> CompareTypesAssignableFunc => compareTypesAssignableFunc ??= (s, t) => CompareTypesAssignable(s, t) != Ternary.False;

    /// <summary>checker.ts createInferenceContext.</summary>
    internal InferenceContext CreateInferenceContext(ReadOnlySpan<Type> typeParameters, Signature signature, InferenceFlags flags, Func<Type, Type, bool>? compareTypes = null)
    {
        var inferences = new List<InferenceInfo>(typeParameters.Length);
        foreach (var tp in typeParameters)
        {
            inferences.Add(CreateInferenceInfo(tp));
        }
        return CreateInferenceContextWorker(inferences, signature, flags, compareTypes ?? CompareTypesAssignableFunc);
    }

    /// <summary>checker.ts cloneInferenceContext.</summary>
    internal InferenceContext? CloneInferenceContext(InferenceContext? context, InferenceFlags extraFlags = InferenceFlags.None) =>
        context is null ? null : CreateInferenceContextWorker(context.Inferences.ConvertAll(CloneInferenceInfo), context.Signature, context.Flags | extraFlags, context.CompareTypes);

    // checker.ts createInferenceContextWorker
    private InferenceContext CreateInferenceContextWorker(List<InferenceInfo> inferences, Signature signature, InferenceFlags flags, Func<Type, Type, bool> compareTypes)
    {
        var context = new InferenceContext { Inferences = inferences, Signature = signature, Flags = flags, CompareTypes = compareTypes };
        context.Mapper = MakeFixingMapperForContext(context);
        context.NonFixingMapper = MakeNonFixingMapperForContext(context);
        return context;
    }

    // checker.ts makeFixingMapperForContext
    private TypeMapper MakeFixingMapperForContext(InferenceContext context)
    {
        var count = context.Inferences.Count;
        var sources = new Type[count];
        var targets = new Func<Type>[count];
        for (var i = 0; i < count; i++)
        {
            var inference = context.Inferences[i];
            var index = i;
            sources[i] = inference.TypeParameter;
            targets[i] = () =>
            {
                if (!inference.IsFixed)
                {
                    // Before we commit to a particular inference (and thus lock out any further inferences),
                    // we infer from any intra-expression inference sites we have collected.
                    InferFromIntraExpressionSites(context);
                    ClearCachedInferences(context.Inferences);
                    inference.IsFixed = true;
                }
                return GetInferredType(context, index);
            };
        }
        return MakeDeferredTypeMapper(sources, targets);
    }

    // checker.ts makeNonFixingMapperForContext
    private TypeMapper MakeNonFixingMapperForContext(InferenceContext context)
    {
        var count = context.Inferences.Count;
        var sources = new Type[count];
        var targets = new Func<Type>[count];
        for (var i = 0; i < count; i++)
        {
            var index = i;
            sources[i] = context.Inferences[i].TypeParameter;
            targets[i] = () => GetInferredType(context, index);
        }
        return MakeDeferredTypeMapper(sources, targets);
    }

    // checker.ts clearCachedInferences
    private static void ClearCachedInferences(List<InferenceInfo> inferences)
    {
        foreach (var inference in inferences)
        {
            if (!inference.IsFixed)
            {
                inference.InferredType = Type.None;
            }
        }
    }

    /// <summary>checker.ts addIntraExpressionInferenceSite.</summary>
    internal static void AddIntraExpressionInferenceSite(InferenceContext context, NodeRef node, Type type) =>
        (context.IntraExpressionInferenceSites ??= []).Add((node, type));

    // checker.ts inferFromIntraExpressionSites
    private void InferFromIntraExpressionSites(InferenceContext context)
    {
        if (context.IntraExpressionInferenceSites is { } sites)
        {
            foreach (var (node, type) in sites)
            {
                var contextualType = Kind(node) == SyntaxKind.MethodDeclaration
                    ? GetContextualTypeForObjectLiteralMethod(node, ContextFlags.NoConstraints)
                    : GetContextualType(node, ContextFlags.NoConstraints);
                if (!contextualType.IsNone)
                {
                    InferTypes(context.Inferences, type, contextualType);
                }
            }
            context.IntraExpressionInferenceSites = null;
        }
    }

    // checker.ts createInferenceInfo
    private static InferenceInfo CreateInferenceInfo(Type typeParameter) => new() { TypeParameter = typeParameter, TopLevel = true };

    // checker.ts cloneInferenceInfo
    private static InferenceInfo CloneInferenceInfo(InferenceInfo inference) => new()
    {
        TypeParameter = inference.TypeParameter,
        Candidates = inference.Candidates is null ? null : [.. inference.Candidates],
        ContraCandidates = inference.ContraCandidates is null ? null : [.. inference.ContraCandidates],
        InferredType = inference.InferredType,
        Priority = inference.Priority,
        TopLevel = inference.TopLevel,
        IsFixed = inference.IsFixed,
        ImpliedArity = inference.ImpliedArity,
    };

    /// <summary>checker.ts cloneInferredPartOfContext.</summary>
    internal InferenceContext? CloneInferredPartOfContext(InferenceContext context)
    {
        var inferences = context.Inferences.FindAll(HasInferenceCandidates);
        return inferences.Count > 0 ? CreateInferenceContextWorker(inferences.ConvertAll(CloneInferenceInfo), context.Signature, context.Flags, context.CompareTypes) : null;
    }

    /// <summary>checker.ts getMapperFromContext.</summary>
    internal static TypeMapper? GetMapperFromContext(InferenceContext? context) => context?.Mapper;

    // checker.ts createBackreferenceMapper
    private TypeMapper CreateBackreferenceMapper(InferenceContext context, int index)
    {
        var count = context.Inferences.Count - index;
        var sources = new Type[count];
        var targets = new Type[count];
        for (var i = 0; i < count; i++)
        {
            sources[i] = context.Inferences[index + i].TypeParameter;
            targets[i] = unknownType;
        }
        return CreateTypeMapper(sources, targets);
    }

    /// <summary>checker.ts createOuterReturnMapper.</summary>
    internal TypeMapper CreateOuterReturnMapper(InferenceContext context) =>
        context.OuterReturnMapper ??= MergeTypeMappers(context.ReturnMapper, CloneInferenceContext(context)!.Mapper);

    /// <summary>checker.ts isTypeParameterAtTopLevel.</summary>
    internal bool IsTypeParameterAtTopLevel(Type type, Type tp, int depth = 0)
    {
        if (type == tp)
        {
            return true;
        }
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.UnionOrIntersection) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(type).Types).ToArray())
            {
                if (IsTypeParameterAtTopLevel(t, tp, depth))
                {
                    return true;
                }
            }
            return false;
        }
        return depth < 3 && (flags & TypeFlags.Conditional) != 0 &&
            (IsTypeParameterAtTopLevel(GetTrueTypeFromConditionalType(type), tp, depth + 1) || IsTypeParameterAtTopLevel(GetFalseTypeFromConditionalType(type), tp, depth + 1));
    }

    // checker.ts isTypeParameterAtTopLevelInReturnType
    private bool IsTypeParameterAtTopLevelInReturnType(Signature signature, Type typeParameter)
    {
        var typePredicate = GetTypePredicateOfSignature(signature);
        return typePredicate is not null ? !typePredicate.Type.IsNone && IsTypeParameterAtTopLevel(typePredicate.Type, typeParameter)
            : IsTypeParameterAtTopLevel(GetReturnTypeOfSignature(signature), typeParameter);
    }

    // checker.ts createEmptyObjectTypeFromStringLiteral: an object with properties named in the string literal type,
    // every property of type any
    private Type CreateEmptyObjectTypeFromStringLiteral(Type type)
    {
        var members = new SymTable(Names);
        ForEachType(type, t =>
        {
            if ((Types.Flags(t) & TypeFlags.StringLiteral) == 0)
            {
                return;
            }
            var name = GetPropertyNameFromType(t);
            var literalProp = CreateSymbol(SymbolFlags.Property, name);
            ValueSymbolLinks.Get(literalProp).ResolvedType = anyType;
            var symbol = Types.Symbol(t);
            if (!symbol.IsNone)
            {
                ref var record = ref TransientSymbols.Record(literalProp);
                record.Declarations = DeclRefOf(symbol);
                record.ValueDeclaration = ValueDeclarationOf(symbol);
            }
            members.Set(name, literalProp);
        });
        var indexInfos = (Types.Flags(type) & TypeFlags.String) != 0 ? new List<IndexInfo> { new() { KeyType = stringType, Type = emptyObjectType, IsReadonly = false } } : [];
        return CreateAnonymousType(SymbolRef.None, members, [], [], indexInfos);
    }

    // ---- reverse mapped types ----

    private readonly Dictionary<(Type, Type, Type), Type> reverseHomomorphicMappedCache = [];
    private readonly Dictionary<(Type, Type, Type), Type> reverseMappedCache = [];
    private readonly List<Type> reverseMappedSourceStack = [];
    private readonly List<Type> reverseMappedTargetStack = [];
    private ExpandingFlags reverseExpandingFlags;

    // ReverseMappedType's source, mappedType and constraintType
    private readonly Dictionary<Type, (Type Source, Type MappedType, Type ConstraintType)> reverseMappedTypes = [];

    // ReverseMappedSymbol's links: propertyType, mappedType, constraintType
    private readonly Dictionary<SymbolRef, (Type PropertyType, Type MappedType, Type ConstraintType)> reverseMappedSymbolLinks = [];

    // checker.ts inferTypeForHomomorphicMappedType: a suitable input type for a homomorphic mapped type
    // { [P in keyof T]: X } (None: undefined)
    private Type InferTypeForHomomorphicMappedType(Type source, Type target, Type constraint)
    {
        var key = (source, target, constraint);
        if (reverseHomomorphicMappedCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var type = CreateReverseMappedType(source, target, constraint);
        reverseHomomorphicMappedCache[key] = type;
        return type;
    }

    // checker.ts isPartiallyInferableType
    private bool IsPartiallyInferableType(Type type)
    {
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.NonInferrableType) == 0)
        {
            return true;
        }
        if (IsObjectLiteralType(type))
        {
            foreach (var prop in GetPropertiesOfType(type))
            {
                if (IsPartiallyInferableType(GetTypeOfSymbol(prop)))
                {
                    return true;
                }
            }
        }
        if (IsTupleType(type))
        {
            foreach (var t in GetElementTypes(type))
            {
                if (IsPartiallyInferableType(t))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // checker.ts createReverseMappedType
    private Type CreateReverseMappedType(Type source, Type target, Type constraint)
    {
        // We consider a source type reverse mappable if it has a string index signature or if
        // it has one or more properties and is of a partially inferable type.
        if (!(GetIndexInfoOfType(source, stringType) is not null || (GetPropertiesOfType(source).Count != 0 && IsPartiallyInferableType(source))))
        {
            return Type.None;
        }
        // For arrays and tuples we infer new arrays and tuples where the reverse mapping has been
        // applied to the element type(s).
        if (IsArrayType(source))
        {
            var elementType = InferReverseMappedType(GetTypeArguments(source)[0], target, constraint);
            if (elementType.IsNone)
            {
                return Type.None;
            }
            return CreateArrayType(elementType, IsReadonlyArrayType(source));
        }
        if (IsTupleType(source))
        {
            var sourceElements = GetElementTypes(source);
            var elementTypes = new Type[sourceElements.Length];
            for (var i = 0; i < sourceElements.Length; i++)
            {
                elementTypes[i] = InferReverseMappedType(sourceElements[i], target, constraint);
                if (elementTypes[i].IsNone)
                {
                    return Type.None;
                }
            }
            var tupleTarget = TupleTarget(source);
            var elementFlags = (GetMappedTypeModifiers(target) & MappedTypeModifiers.IncludeOptional) != 0
                ? Array.ConvertAll(tupleTarget.ElementFlags!, f => (f & ElementFlags.Optional) != 0 ? ElementFlags.Required : f)
                : tupleTarget.ElementFlags;
            return CreateTupleType(elementTypes, elementFlags, tupleTarget.Readonly, tupleTarget.LabeledElementDeclarations);
        }
        // For all other object types we infer a new object type where the reverse mapping has been
        // applied to the type of each property.
        var reversed = CreateObjectType(ObjectFlags.ReverseMapped | ObjectFlags.Anonymous, SymbolRef.None);
        reverseMappedTypes[reversed] = (source, target, constraint);
        return reversed;
    }

    // checker.ts getTypeOfReverseMappedSymbol
    private Type GetTypeOfReverseMappedSymbol(SymbolRef symbol)
    {
        ref var links = ref ValueSymbolLinks.Get(symbol);
        if (links.ResolvedType.IsNone)
        {
            var (propertyType, mappedType, constraintType) = reverseMappedSymbolLinks[symbol];
            var type = InferReverseMappedType(propertyType, mappedType, constraintType);
            ValueSymbolLinks.Get(symbol).ResolvedType = type.IsNone ? unknownType : type;
        }
        return ValueSymbolLinks.Get(symbol).ResolvedType;
    }

    // checker.ts inferReverseMappedTypeWorker
    private Type InferReverseMappedTypeWorker(Type sourceType, Type target, Type constraint)
    {
        var typeParameter = GetIndexedAccessType(Types.Data<IndexData>(constraint).Type, GetTypeParameterFromMappedType(target));
        var templateType = GetTemplateTypeFromMappedType(target);
        var inference = CreateInferenceInfo(typeParameter);
        InferTypes([inference], sourceType, templateType);
        var inferred = GetTypeFromInference(inference);
        return GetWidenedType(inferred.IsNone ? unknownType : inferred);
    }

    // checker.ts inferReverseMappedType (None: undefined)
    private Type InferReverseMappedType(Type source, Type target, Type constraint)
    {
        var key = (source, target, constraint);
        if (reverseMappedCache.TryGetValue(key, out var cached))
        {
            return cached.IsNone ? unknownType : cached;
        }
        reverseMappedSourceStack.Add(source);
        reverseMappedTargetStack.Add(target);
        var saveExpandingFlags = reverseExpandingFlags;
        if (IsDeeplyNestedType(source, reverseMappedSourceStack, reverseMappedSourceStack.Count, 2))
        {
            reverseExpandingFlags |= ExpandingFlags.Source;
        }
        if (IsDeeplyNestedType(target, reverseMappedTargetStack, reverseMappedTargetStack.Count, 2))
        {
            reverseExpandingFlags |= ExpandingFlags.Target;
        }
        var type = Type.None;
        if (reverseExpandingFlags != ExpandingFlags.Both)
        {
            type = InferReverseMappedTypeWorker(source, target, constraint);
        }
        reverseMappedSourceStack.RemoveAt(reverseMappedSourceStack.Count - 1);
        reverseMappedTargetStack.RemoveAt(reverseMappedTargetStack.Count - 1);
        reverseExpandingFlags = saveExpandingFlags;
        reverseMappedCache[key] = type;
        return type;
    }

    // checker.ts resolveReverseMappedTypeMembers
    private void ResolveReverseMappedTypeMembers(Type type)
    {
        var (source, mappedType, constraintType) = reverseMappedTypes[type];
        var indexInfo = GetIndexInfoOfType(source, stringType);
        var modifiers = GetMappedTypeModifiers(mappedType);
        var readonlyMask = (modifiers & MappedTypeModifiers.IncludeReadonly) == 0;
        var optionalMask = (modifiers & MappedTypeModifiers.IncludeOptional) != 0 ? SymbolFlags.None : SymbolFlags.Optional;
        List<IndexInfo> indexInfos = [];
        if (indexInfo is not null)
        {
            var inferred = InferReverseMappedType(indexInfo.Type, mappedType, constraintType);
            indexInfos.Add(new IndexInfo { KeyType = stringType, Type = inferred.IsNone ? unknownType : inferred, IsReadonly = readonlyMask && indexInfo.IsReadonly });
        }
        var members = new SymTable(Names);
        var limitedConstraint = GetLimitedConstraint(mappedType, constraintType);
        foreach (var prop in GetPropertiesOfType(source))
        {
            // In case of a reverse mapped type with an intersection constraint, if we were able to
            // extract the filtering type literals we skip those properties that are not assignable to them,
            // because the extra properties wouldn't get through the application of the mapped type anyway
            if (!limitedConstraint.IsNone)
            {
                var propertyNameType = GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique);
                if (!IsTypeAssignableTo(propertyNameType, limitedConstraint))
                {
                    continue;
                }
            }
            var checkFlags = CheckFlags.ReverseMapped | (readonlyMask && IsReadonlySymbol(prop) ? CheckFlags.Readonly : CheckFlags.None);
            var inferredProp = CreateSymbol(SymbolFlags.Property | (SymbolFlagsOf(prop) & optionalMask), NameIdOf(prop), checkFlags);
            TransientSymbols.Record(inferredProp).Declarations = DeclRefOf(prop);
            var nameType = ValueSymbolLinks.Has(prop) ? ValueSymbolLinks.Get(prop).NameType : Type.None;
            ValueSymbolLinks.Get(inferredProp).NameType = nameType;
            var propertyType = GetTypeOfSymbol(prop);
            var constraintTarget = Types.Data<IndexData>(constraintType).Type;
            if ((Types.Flags(constraintTarget) & TypeFlags.IndexedAccess) != 0 &&
                (Types.Flags(Types.Data<IndexedAccessData>(constraintTarget).ObjectType) & TypeFlags.TypeParameter) != 0 &&
                (Types.Flags(Types.Data<IndexedAccessData>(constraintTarget).IndexType) & TypeFlags.TypeParameter) != 0)
            {
                // A reverse mapping of `{[K in keyof T[K_1]]: T[K_1]}` is the same as that of `{[K in keyof T]: T}`, since all we care about is
                // inferring to the "type parameter" (or indexed access) shared by the constraint and template. So, to reduce the number of
                // type identities produced, we simplify such indexed access occurences
                var newTypeParam = Types.Data<IndexedAccessData>(constraintTarget).ObjectType;
                var newMappedType = ReplaceIndexedAccess(mappedType, constraintTarget, newTypeParam);
                reverseMappedSymbolLinks[inferredProp] = (propertyType, newMappedType, GetIndexType(newTypeParam));
            }
            else
            {
                reverseMappedSymbolLinks[inferredProp] = (propertyType, mappedType, constraintType);
            }
            members.Set(NameOf(prop), inferredProp);
        }
        SetStructuredTypeMembers(type, members, [], [], indexInfos);
    }

    // checker.ts replaceIndexedAccess
    private Type ReplaceIndexedAccess(Type instantiable, Type type, Type replacement)
    {
        // map type.indexType to 0
        // map type.objectType to `[TReplacement]`
        // thus making the indexed access `[TReplacement][0]` or `TReplacement`
        var data = Types.Data<IndexedAccessData>(type);
        return InstantiateType(instantiable, CreateTypeMapper([data.IndexType, data.ObjectType], [GetNumberLiteralType(0), CreateTupleType([replacement])]));
    }

    // checker.ts getLimitedConstraint (None: undefined)
    private Type GetLimitedConstraint(Type mappedType, Type constraintType)
    {
        var constraint = GetConstraintTypeFromMappedType(mappedType);
        var flags = Types.Flags(constraint);
        if ((flags & TypeFlags.UnionOrIntersection) == 0)
        {
            return Type.None;
        }
        var origin = (flags & TypeFlags.Union) != 0 ? Types.Union(constraint).Origin : constraint;
        if (origin.IsNone || (Types.Flags(origin) & TypeFlags.Intersection) == 0)
        {
            return Type.None;
        }
        var parts = new List<Type>();
        foreach (var t in Types.Elements(Types.Union(origin).Types))
        {
            if (t != constraintType)
            {
                parts.Add(t);
            }
        }
        var limitedConstraint = GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(parts));
        return limitedConstraint != neverType ? limitedConstraint : Type.None;
    }

    // checker.ts instantiateReverseMappedType
    private Type InstantiateReverseMappedType(Type type, TypeMapper mapper)
    {
        var (source, mappedType, constraintType) = reverseMappedTypes[type];
        var innerMappedType = InstantiateType(mappedType, mapper);
        if ((Types.ObjectFlagsOf(innerMappedType) & ObjectFlags.Mapped) == 0)
        {
            return type;
        }
        var innerIndexType = InstantiateType(constraintType, mapper);
        if ((Types.Flags(innerIndexType) & TypeFlags.Index) == 0)
        {
            return type;
        }
        var instantiated = InferTypeForHomomorphicMappedType(InstantiateType(source, mapper), innerMappedType, innerIndexType);
        return !instantiated.IsNone ? instantiated : type;
    }

    /// <summary>The source of a reverse mapped type (isObjectTypeWithInferableIndex, the node builder).</summary>
    internal Type ReverseMappedSource(Type type) => reverseMappedTypes[type].Source;

    // ReverseMappedSymbol.links.propertyType / mappedType (the node builder)
    private Type GetReverseMappedPropertyType(SymbolRef symbol) => reverseMappedSymbolLinks[symbol].PropertyType;

    private Type GetReverseMappedMappedType(SymbolRef symbol) => reverseMappedSymbolLinks[symbol].MappedType;

    // checker.ts getTypeFromInference (None: undefined)
    private Type GetTypeFromInference(InferenceInfo inference) =>
        inference.Candidates is not null ? GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(inference.Candidates), UnionReduction.Subtype)
        : inference.ContraCandidates is not null ? GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(inference.ContraCandidates))
        : Type.None;

    /// <summary>checker.ts inferTypes.</summary>
    internal void InferTypes(List<InferenceInfo> inferences, Type originalSource, Type originalTarget, InferencePriority priority = InferencePriority.None, bool contravariant = false) =>
        new InferenceState(this, inferences, originalTarget, priority, contravariant).InferFromTypes(originalSource, originalTarget);

    // checker.ts inferTypes: the state its nested functions share
    private sealed class InferenceState(Checker c, List<InferenceInfo> inferences, Type originalTarget, InferencePriority priority, bool contravariant)
    {
        private bool bivariant;
        private Type propagationType;
        private int inferencePriority = (int)InferencePriority.MaxValue;
        private Dictionary<(Type, Type), int>? visited;
        private List<Type>? sourceStack;
        private List<Type>? targetStack;
        private ExpandingFlags expandingFlags;
        private InferencePriority priority = priority;
        private bool contravariant = contravariant;

        private TypeStore Types => c.Types;

        private Type[] Members(Type type) => Types.Elements(Types.Union(type).Types).ToArray();

        internal void InferFromTypes(Type source, Type target)
        {
            if (!c.CouldContainTypeVariables(target) || c.IsNoInferType(target))
            {
                return;
            }
            if (source == c.wildcardType || source == c.blockedStringType)
            {
                // We are inferring from an 'any' type. We want to infer this type for every type parameter
                // referenced in the target type, so we record it as the propagation type and infer from the
                // target to itself. Then, as we find candidates we substitute the propagation type.
                var savePropagationType = propagationType;
                propagationType = source;
                InferFromTypes(target, target);
                propagationType = savePropagationType;
                return;
            }
            var sourceAlias = Types.Record(source).AliasSymbol;
            if (!sourceAlias.IsNone && sourceAlias == Types.Record(target).AliasSymbol)
            {
                var sourceAliasArguments = Types.Record(source).AliasTypeArguments;
                if (sourceAliasArguments.IsPresent)
                {
                    // Source and target are types originating in the same generic type alias declaration.
                    // Simply infer from source type arguments to target type arguments, with defaults applied.
                    var parameters = c.GetTypeAliasTypeParameters(sourceAlias) ?? [];
                    var minParams = c.GetMinTypeArgumentCount(parameters);
                    var aliasDeclaration = c.ValueDeclarationOf(sourceAlias);
                    var isJs = !aliasDeclaration.IsNone && Utilities.IsInJSFile(c.StoreOf(aliasDeclaration), aliasDeclaration.Node);
                    var sourceTypes = c.FillMissingTypeArguments(Types.Elements(sourceAliasArguments).ToArray(), parameters, minParams, isJs);
                    var targetTypes = c.FillMissingTypeArguments(Types.Elements(Types.Record(target).AliasTypeArguments).ToArray(), parameters, minParams, isJs);
                    InferFromTypeArguments(sourceTypes, targetTypes, c.GetAliasVariances(sourceAlias));
                }
                // And if there weren't any type arguments, there's no reason to run inference as the types must be the same.
                return;
            }
            if (source == target && (Types.Flags(source) & TypeFlags.UnionOrIntersection) != 0)
            {
                // When source and target are the same union or intersection type, just relate each constituent
                // type to itself.
                foreach (var t in Members(source))
                {
                    InferFromTypes(t, t);
                }
                return;
            }
            if ((Types.Flags(target) & TypeFlags.Union) != 0)
            {
                // First, infer between identically matching source and target constituents and remove the
                // matching types.
                var (tempSources, tempTargets) = InferFromMatchingTypes((Types.Flags(source) & TypeFlags.Union) != 0 ? Members(source) : [source], Members(target), c.IsTypeOrBaseIdenticalTo);
                // Next, infer between closely matching source and target constituents and remove
                // the matching types. Types closely match when they are instantiations of the same
                // object type or instantiations of the same type alias.
                var (sources, targets) = InferFromMatchingTypes(tempSources, tempTargets, c.IsTypeCloselyMatchedBy);
                if (targets.Length == 0)
                {
                    return;
                }
                target = c.GetUnionType(targets);
                if (sources.Length == 0)
                {
                    // All source constituents have been matched and there is nothing further to infer from.
                    // However, simply making no inferences is undesirable because it could ultimately mean
                    // inferring a type parameter constraint. Instead, make a lower priority inference from
                    // the full source to whatever remains in the target. For example, when inferring from
                    // string to 'string | T', make a lower priority inference of string for T.
                    InferWithPriority(source, target, InferencePriority.NakedTypeVariable);
                    return;
                }
                source = c.GetUnionType(sources);
            }
            else if ((Types.Flags(target) & TypeFlags.Intersection) != 0 && !Array.TrueForAll(Members(target), c.IsNonGenericObjectType))
            {
                // We reduce intersection types unless they're simple combinations of object types. For example,
                // when inferring from 'string[] & { extra: any }' to 'string[] & T' we want to remove string[] and
                // infer { extra: any } for T. But when inferring to 'string[] & Iterable<T>' we want to keep the
                // string[] on the source side and infer string for T.
                if ((Types.Flags(source) & TypeFlags.Union) == 0)
                {
                    // Infer between identically matching source and target constituents and remove the matching types.
                    var (sources, targets) = InferFromMatchingTypes((Types.Flags(source) & TypeFlags.Intersection) != 0 ? Members(source) : [source], Members(target), c.IsTypeIdenticalTo);
                    if (sources.Length == 0 || targets.Length == 0)
                    {
                        return;
                    }
                    source = c.GetIntersectionType(sources);
                    target = c.GetIntersectionType(targets);
                }
            }
            if ((Types.Flags(target) & (TypeFlags.IndexedAccess | TypeFlags.Substitution)) != 0)
            {
                if (c.IsNoInferType(target))
                {
                    return;
                }
                target = c.GetActualTypeVariable(target);
            }
            if ((Types.Flags(target) & TypeFlags.TypeVariable) != 0)
            {
                // (isFromInferenceBlockedSource: language service only)
                var inference = GetInferenceInfoForType(target);
                if (inference is not null)
                {
                    // If target is a type parameter, make an inference, unless the source type contains
                    // a "non-inferrable" type. Types with this flag set are markers used to prevent inference.
                    if ((Types.ObjectFlagsOf(source) & ObjectFlags.NonInferrableType) != 0 || source == c.nonInferrableAnyType)
                    {
                        return;
                    }
                    if (!inference.IsFixed)
                    {
                        var candidate = !propagationType.IsNone ? propagationType : source;
                        if (candidate == c.blockedStringType)
                        {
                            return;
                        }
                        if (inference.Priority is null || priority < inference.Priority.Value)
                        {
                            inference.Candidates = null;
                            inference.ContraCandidates = null;
                            inference.TopLevel = true;
                            inference.Priority = priority;
                        }
                        if (priority == inference.Priority)
                        {
                            // We make contravariant inferences only if we are in a pure contravariant position,
                            // i.e. only if we have not descended into a bivariant position.
                            if (contravariant && !bivariant)
                            {
                                if (inference.ContraCandidates is null || !inference.ContraCandidates.Contains(candidate))
                                {
                                    (inference.ContraCandidates ??= []).Add(candidate);
                                    ClearCachedInferences(inferences);
                                }
                            }
                            else if (inference.Candidates is null || !inference.Candidates.Contains(candidate))
                            {
                                (inference.Candidates ??= []).Add(candidate);
                                ClearCachedInferences(inferences);
                            }
                        }
                        if ((priority & InferencePriority.ReturnType) == 0 && (Types.Flags(target) & TypeFlags.TypeParameter) != 0 && inference.TopLevel &&
                            !c.IsTypeParameterAtTopLevel(originalTarget, target))
                        {
                            inference.TopLevel = false;
                            ClearCachedInferences(inferences);
                        }
                    }
                    inferencePriority = Math.Min(inferencePriority, (int)priority);
                    return;
                }
                // Infer to the simplified version of an indexed access, if possible, to (hopefully) expose more bare type parameters to the inference engine
                var simplified = c.GetSimplifiedType(target, false);
                if (simplified != target)
                {
                    InferFromTypes(source, simplified);
                }
                else if ((Types.Flags(target) & TypeFlags.IndexedAccess) != 0)
                {
                    var data = Types.Data<IndexedAccessData>(target);
                    var indexType = c.GetSimplifiedType(data.IndexType, false);
                    // Generally simplifications of instantiable indexes are avoided to keep relationship checking correct, however if our target is an access, we can consider
                    // that key of that access to be "instantiated", since we're looking to find the infernce goal in any way we can.
                    if ((Types.Flags(indexType) & TypeFlags.Instantiable) != 0)
                    {
                        var distributed = c.DistributeIndexOverObjectType(c.GetSimplifiedType(data.ObjectType, false), indexType, false);
                        if (!distributed.IsNone && distributed != target)
                        {
                            InferFromTypes(source, distributed);
                        }
                    }
                }
            }
            if (IsSameReference(source, target) && !(!Types.Object(source).Node.IsNone && !Types.Object(target).Node.IsNone))
            {
                // If source and target are references to the same generic type, infer from type arguments
                InferFromTypeArguments(c.GetTypeArguments(source).ToArray(), c.GetTypeArguments(target).ToArray(), c.GetVariances(Types.Object(source).Target));
            }
            else if ((Types.Flags(source) & TypeFlags.Index) != 0 && (Types.Flags(target) & TypeFlags.Index) != 0)
            {
                InferFromContravariantTypes(Types.Data<IndexData>(source).Type, Types.Data<IndexData>(target).Type);
            }
            else if ((c.IsLiteralType(source) || (Types.Flags(source) & TypeFlags.String) != 0) && (Types.Flags(target) & TypeFlags.Index) != 0)
            {
                var empty = c.CreateEmptyObjectTypeFromStringLiteral(source);
                InferFromContravariantTypesWithPriority(empty, Types.Data<IndexData>(target).Type, InferencePriority.LiteralKeyof);
            }
            else if ((Types.Flags(source) & TypeFlags.IndexedAccess) != 0 && (Types.Flags(target) & TypeFlags.IndexedAccess) != 0)
            {
                InferFromTypes(Types.Data<IndexedAccessData>(source).ObjectType, Types.Data<IndexedAccessData>(target).ObjectType);
                InferFromTypes(Types.Data<IndexedAccessData>(source).IndexType, Types.Data<IndexedAccessData>(target).IndexType);
            }
            else if ((Types.Flags(source) & TypeFlags.StringMapping) != 0 && (Types.Flags(target) & TypeFlags.StringMapping) != 0)
            {
                if (Types.Symbol(source) == Types.Symbol(target))
                {
                    InferFromTypes(Types.Data<StringMappingData>(source).Type, Types.Data<StringMappingData>(target).Type);
                }
            }
            else if ((Types.Flags(source) & TypeFlags.Substitution) != 0)
            {
                InferFromTypes(Types.Data<SubstitutionData>(source).BaseType, target);
                InferWithPriority(c.GetSubstitutionIntersection(source), target, InferencePriority.SubstituteSource); // Make substitute inference at a lower priority
            }
            else if ((Types.Flags(target) & TypeFlags.Conditional) != 0)
            {
                InvokeOnce(source, target, InferToConditionalType);
            }
            else if ((Types.Flags(target) & TypeFlags.UnionOrIntersection) != 0)
            {
                InferToMultipleTypes(source, Members(target), Types.Flags(target));
            }
            else if ((Types.Flags(source) & TypeFlags.Union) != 0)
            {
                // Source is a union or intersection type, infer from each constituent type
                foreach (var sourceType in Members(source))
                {
                    InferFromTypes(sourceType, target);
                }
            }
            else if ((Types.Flags(target) & TypeFlags.TemplateLiteral) != 0)
            {
                InferToTemplateLiteralType(source, target);
            }
            else
            {
                source = c.GetReducedType(source);
                if (c.IsGenericMappedType(source) && c.IsGenericMappedType(target))
                {
                    InvokeOnce(source, target, InferFromGenericMappedTypes);
                }
                if (!((priority & InferencePriority.NoConstraints) != 0 && (Types.Flags(source) & (TypeFlags.Intersection | TypeFlags.Instantiable)) != 0))
                {
                    var apparentSource = c.GetApparentType(source);
                    // getApparentType can return _any_ type, since an indexed access or conditional may simplify to any other type.
                    // If that occurs and it doesn't simplify to an object or intersection, we'll need to restart `inferFromTypes`
                    // with the simplified source.
                    if (apparentSource != source && (Types.Flags(apparentSource) & (TypeFlags.Object | TypeFlags.Intersection)) == 0)
                    {
                        InferFromTypes(apparentSource, target);
                        return;
                    }
                    source = apparentSource;
                }
                if ((Types.Flags(source) & (TypeFlags.Object | TypeFlags.Intersection)) != 0)
                {
                    InvokeOnce(source, target, InferFromObjectTypes);
                }
            }
        }

        // getObjectFlags(source) & Reference && getObjectFlags(target) & Reference && (same target || both arrays)
        private bool IsSameReference(Type source, Type target) =>
            (Types.ObjectFlagsOf(source) & ObjectFlags.Reference) != 0 && (Types.ObjectFlagsOf(target) & ObjectFlags.Reference) != 0 &&
            (Types.Object(source).Target == Types.Object(target).Target || (c.IsArrayType(source) && c.IsArrayType(target)));

        private void InferWithPriority(Type source, Type target, InferencePriority newPriority)
        {
            var savePriority = priority;
            priority |= newPriority;
            InferFromTypes(source, target);
            priority = savePriority;
        }

        private void InferFromContravariantTypesWithPriority(Type source, Type target, InferencePriority newPriority)
        {
            var savePriority = priority;
            priority |= newPriority;
            InferFromContravariantTypes(source, target);
            priority = savePriority;
        }

        private void InferToMultipleTypesWithPriority(Type source, Type[] targets, TypeFlags targetFlags, InferencePriority newPriority)
        {
            var savePriority = priority;
            priority |= newPriority;
            InferToMultipleTypes(source, targets, targetFlags);
            priority = savePriority;
        }

        // Ensure an inference action is performed only once for the given source and target types.
        // This includes two things:
        // Avoiding inferring between the same pair of source and target types,
        // and avoiding circularly inferring between source and target types.
        private void InvokeOnce(Type source, Type target, Action<Type, Type> action)
        {
            var key = (source, target);
            if (visited is not null && visited.TryGetValue(key, out var status))
            {
                inferencePriority = Math.Min(inferencePriority, status);
                return;
            }
            (visited ??= [])[key] = (int)InferencePriority.Circularity;
            var saveInferencePriority = inferencePriority;
            inferencePriority = (int)InferencePriority.MaxValue;
            // We stop inferring and report a circularity if we encounter duplicate recursion identities on both
            // the source side and the target side.
            var saveExpandingFlags = expandingFlags;
            (sourceStack ??= []).Add(source);
            (targetStack ??= []).Add(target);
            if (c.IsDeeplyNestedType(source, sourceStack, sourceStack.Count, 2))
            {
                expandingFlags |= ExpandingFlags.Source;
            }
            if (c.IsDeeplyNestedType(target, targetStack, targetStack.Count, 2))
            {
                expandingFlags |= ExpandingFlags.Target;
            }
            if (expandingFlags != ExpandingFlags.Both)
            {
                action(source, target);
            }
            else
            {
                inferencePriority = (int)InferencePriority.Circularity;
            }
            targetStack.RemoveAt(targetStack.Count - 1);
            sourceStack.RemoveAt(sourceStack.Count - 1);
            expandingFlags = saveExpandingFlags;
            visited[key] = inferencePriority;
            inferencePriority = Math.Min(inferencePriority, saveInferencePriority);
        }

        private (Type[] Sources, Type[] Targets) InferFromMatchingTypes(Type[] sources, Type[] targets, Func<Type, Type, bool> matches)
        {
            List<Type>? matchedSources = null;
            List<Type>? matchedTargets = null;
            foreach (var t in targets)
            {
                foreach (var s in sources)
                {
                    if (matches(s, t))
                    {
                        InferFromTypes(s, t);
                        matchedSources ??= [];
                        if (!matchedSources.Contains(s))
                        {
                            matchedSources.Add(s);
                        }
                        matchedTargets ??= [];
                        if (!matchedTargets.Contains(t))
                        {
                            matchedTargets.Add(t);
                        }
                    }
                }
            }
            return (matchedSources is not null ? Array.FindAll(sources, t => !matchedSources.Contains(t)) : sources,
                matchedTargets is not null ? Array.FindAll(targets, t => !matchedTargets.Contains(t)) : targets);
        }

        private void InferFromTypeArguments(ReadOnlySpan<Type> sourceTypes, ReadOnlySpan<Type> targetTypes, VarianceFlags[] variances)
        {
            var count = sourceTypes.Length < targetTypes.Length ? sourceTypes.Length : targetTypes.Length;
            for (var i = 0; i < count; i++)
            {
                if (i < variances.Length && (variances[i] & VarianceFlags.VarianceMask) == VarianceFlags.Contravariant)
                {
                    InferFromContravariantTypes(sourceTypes[i], targetTypes[i]);
                }
                else
                {
                    InferFromTypes(sourceTypes[i], targetTypes[i]);
                }
            }
        }

        private void InferFromContravariantTypes(Type source, Type target)
        {
            contravariant = !contravariant;
            InferFromTypes(source, target);
            contravariant = !contravariant;
        }

        private void InferFromContravariantTypesIfStrictFunctionTypes(Type source, Type target)
        {
            if (c.StrictFunctionTypes || (priority & InferencePriority.AlwaysStrict) != 0)
            {
                InferFromContravariantTypes(source, target);
            }
            else
            {
                InferFromTypes(source, target);
            }
        }

        private InferenceInfo? GetInferenceInfoForType(Type type)
        {
            if ((Types.Flags(type) & TypeFlags.TypeVariable) != 0)
            {
                foreach (var inference in inferences)
                {
                    if (type == inference.TypeParameter)
                    {
                        return inference;
                    }
                }
            }
            return null;
        }

        private Type GetSingleTypeVariableFromIntersectionTypes(Type[] types)
        {
            var typeVariable = Type.None;
            foreach (var type in types)
            {
                var t = Type.None;
                if ((Types.Flags(type) & TypeFlags.Intersection) != 0)
                {
                    foreach (var member in Members(type))
                    {
                        if (GetInferenceInfoForType(member) is not null)
                        {
                            t = member;
                            break;
                        }
                    }
                }
                if (t.IsNone || (!typeVariable.IsNone && t != typeVariable))
                {
                    return Type.None;
                }
                typeVariable = t;
            }
            return typeVariable;
        }

        private void InferToMultipleTypes(Type source, Type[] targets, TypeFlags targetFlags)
        {
            var typeVariableCount = 0;
            if ((targetFlags & TypeFlags.Union) != 0)
            {
                var nakedTypeVariable = Type.None;
                var sources = (Types.Flags(source) & TypeFlags.Union) != 0 ? Members(source) : [source];
                var matched = new bool[sources.Length];
                var inferenceCircularity = false;
                // First infer to types that are not naked type variables. For each source type we
                // track whether inferences were made from that particular type to some target with
                // equal priority (i.e. of equal quality) to what we would infer for a naked type
                // parameter.
                foreach (var t in targets)
                {
                    if (GetInferenceInfoForType(t) is not null)
                    {
                        nakedTypeVariable = t;
                        typeVariableCount++;
                    }
                    else
                    {
                        for (var i = 0; i < sources.Length; i++)
                        {
                            var saveInferencePriority = inferencePriority;
                            inferencePriority = (int)InferencePriority.MaxValue;
                            InferFromTypes(sources[i], t);
                            if (inferencePriority == (int)priority)
                            {
                                matched[i] = true;
                            }
                            inferenceCircularity = inferenceCircularity || inferencePriority == (int)InferencePriority.Circularity;
                            inferencePriority = Math.Min(inferencePriority, saveInferencePriority);
                        }
                    }
                }
                if (typeVariableCount == 0)
                {
                    // If every target is an intersection of types containing a single naked type variable,
                    // make a lower priority inference to that type variable. This handles inferring from
                    // 'A | B' to 'T & (X | Y)' where we want to infer 'A | B' for T.
                    var intersectionTypeVariable = GetSingleTypeVariableFromIntersectionTypes(targets);
                    if (!intersectionTypeVariable.IsNone)
                    {
                        InferWithPriority(source, intersectionTypeVariable, InferencePriority.NakedTypeVariable);
                    }
                    return;
                }
                // If the target has a single naked type variable and no inference circularities were
                // encountered above (meaning we explored the types fully), create a union of the source
                // types from which no inferences have been made so far and infer from that union to the
                // naked type variable.
                if (typeVariableCount == 1 && !inferenceCircularity)
                {
                    var unmatched = new List<Type>();
                    for (var i = 0; i < sources.Length; i++)
                    {
                        if (!matched[i])
                        {
                            unmatched.Add(sources[i]);
                        }
                    }
                    if (unmatched.Count > 0)
                    {
                        InferFromTypes(c.GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(unmatched)), nakedTypeVariable);
                        return;
                    }
                }
            }
            else
            {
                // We infer from types that are not naked type variables first so that inferences we
                // make from nested naked type variables and given slightly higher priority by virtue
                // of being first in the candidates array.
                foreach (var t in targets)
                {
                    if (GetInferenceInfoForType(t) is not null)
                    {
                        typeVariableCount++;
                    }
                    else
                    {
                        InferFromTypes(source, t);
                    }
                }
            }
            // Inferences directly to naked type variables are given lower priority as they are
            // less specific. For example, when inferring from Promise<string> to T | Promise<T>,
            // we want to infer string for T, not Promise<string> | string. For intersection types
            // we only infer to single naked type variables.
            if ((targetFlags & TypeFlags.Intersection) != 0 ? typeVariableCount == 1 : typeVariableCount > 0)
            {
                foreach (var t in targets)
                {
                    if (GetInferenceInfoForType(t) is not null)
                    {
                        InferWithPriority(source, t, InferencePriority.NakedTypeVariable);
                    }
                }
            }
        }

        private bool InferToMappedType(Type source, Type target, Type constraintType)
        {
            var constraintFlags = Types.Flags(constraintType);
            if ((constraintFlags & TypeFlags.UnionOrIntersection) != 0)
            {
                var result = false;
                foreach (var type in Members(constraintType))
                {
                    result = InferToMappedType(source, target, type) || result;
                }
                return result;
            }
            if ((constraintFlags & TypeFlags.Index) != 0)
            {
                // We're inferring from some source type S to a homomorphic mapped type { [P in keyof T]: X },
                // where T is a type variable. Use inferTypeForHomomorphicMappedType to infer a suitable source
                // type and then make a secondary inference from that type to T. We make a secondary inference
                // such that direct inferences to T get priority over inferences to Partial<T>, for example.
                var inference = GetInferenceInfoForType(Types.Data<IndexData>(constraintType).Type);
                if (inference is not null && !inference.IsFixed)
                {
                    var inferredType = c.InferTypeForHomomorphicMappedType(source, target, constraintType);
                    if (!inferredType.IsNone)
                    {
                        // We assign a lower priority to inferences made from types containing non-inferrable
                        // types because we may only have a partial result (i.e. we may have failed to make
                        // reverse inferences for some properties).
                        InferWithPriority(inferredType, inference.TypeParameter,
                            (Types.ObjectFlagsOf(source) & ObjectFlags.NonInferrableType) != 0 ? InferencePriority.PartialHomomorphicMappedType : InferencePriority.HomomorphicMappedType);
                    }
                }
                return true;
            }
            if ((constraintFlags & TypeFlags.TypeParameter) != 0)
            {
                // We're inferring from some source type S to a mapped type { [P in K]: X }, where K is a type
                // parameter. First infer from 'keyof S' to K.
                InferWithPriority(c.GetIndexType(source, c.typePatterns.ContainsKey(source) ? IndexFlags.NoIndexSignatures : IndexFlags.None), constraintType, InferencePriority.MappedTypeConstraint);
                // If K is constrained to a type C, also infer to C. Thus, for a mapped type { [P in K]: X },
                // where K extends keyof T, we make the same inferences as for a homomorphic mapped type
                // { [P in keyof T]: X }. This enables us to make meaningful inferences when the target is a
                // Pick<T, K>.
                var extendedConstraint = c.GetConstraintOfType(constraintType);
                if (!extendedConstraint.IsNone && InferToMappedType(source, target, extendedConstraint))
                {
                    return true;
                }
                // If no inferences can be made to K's constraint, infer from a union of the property types
                // in the source to the template type X.
                var types = new List<Type>();
                foreach (var prop in c.GetPropertiesOfType(source))
                {
                    types.Add(c.GetTypeOfSymbol(prop));
                }
                foreach (var info in c.GetIndexInfosOfType(source))
                {
                    types.Add(info != c.enumNumberIndexInfo ? info.Type : c.neverType);
                }
                InferFromTypes(c.GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(types)), c.GetTemplateTypeFromMappedType(target));
                return true;
            }
            return false;
        }

        private void InferToConditionalType(Type source, Type target)
        {
            if ((Types.Flags(source) & TypeFlags.Conditional) != 0)
            {
                InferFromTypes(Types.Data<ConditionalData>(source).CheckType, Types.Data<ConditionalData>(target).CheckType);
                InferFromTypes(Types.Data<ConditionalData>(source).ExtendsType, Types.Data<ConditionalData>(target).ExtendsType);
                InferFromTypes(c.GetTrueTypeFromConditionalType(source), c.GetTrueTypeFromConditionalType(target));
                InferFromTypes(c.GetFalseTypeFromConditionalType(source), c.GetFalseTypeFromConditionalType(target));
            }
            else
            {
                Type[] targetTypes = [c.GetTrueTypeFromConditionalType(target), c.GetFalseTypeFromConditionalType(target)];
                InferToMultipleTypesWithPriority(source, targetTypes, Types.Flags(target), contravariant ? InferencePriority.ContravariantConditional : InferencePriority.None);
            }
        }

        private void InferToTemplateLiteralType(Type source, Type target)
        {
            var matches = c.InferTypesFromTemplateLiteralType(source, target);
            var data = Types.Data<TemplateLiteralData>(target);
            var types = data.Types;
            // When the target template literal contains only placeholders (meaning that inference is intended to extract
            // single characters and remainder strings) and inference fails to produce matches, we want to infer 'never' for
            // each placeholder such that instantiation with the inferred value(s) produces 'never', a type for which an
            // assignment check will fail. If we make no inferences, we'll likely end up with the constraint 'string' which,
            // upon instantiation, would collapse all the placeholders to just 'string', and an assignment check might
            // succeed. That would be a pointless and confusing outcome.
            if (matches is null && !Array.TrueForAll(data.Texts, s => s.Length == 0))
            {
                return;
            }
            for (var i = 0; i < types.Length; i++)
            {
                var matchSource = matches is not null ? matches[i] : c.neverType;
                var matchTarget = types[i];
                // If we are inferring from a string literal type to a type variable whose constraint includes one of the
                // allowed template literal placeholder types, infer from a literal type corresponding to the constraint.
                if ((Types.Flags(matchSource) & TypeFlags.StringLiteral) != 0 && (Types.Flags(matchTarget) & TypeFlags.TypeVariable) != 0)
                {
                    var inferenceContext = GetInferenceInfoForType(matchTarget);
                    var constraint = inferenceContext is not null ? c.GetBaseConstraintOfType(inferenceContext.TypeParameter) : Type.None;
                    if (!constraint.IsNone && !c.IsTypeAny(constraint))
                    {
                        var constraintTypes = (Types.Flags(constraint) & TypeFlags.Union) != 0 ? Members(constraint) : [constraint];
                        TypeFlags allTypeFlags = 0;
                        foreach (var t in constraintTypes)
                        {
                            allTypeFlags |= Types.Flags(t);
                        }
                        // If the constraint contains `string`, we don't need to look for a more preferred type
                        if ((allTypeFlags & TypeFlags.String) == 0)
                        {
                            var str = (string)Types.Literal(matchSource).Value!;
                            // If the type contains `number` or a number literal and the string isn't a valid number, exclude numbers
                            if ((allTypeFlags & TypeFlags.NumberLike) != 0 && !IsValidNumberString(str, true))
                            {
                                allTypeFlags &= ~TypeFlags.NumberLike;
                            }
                            // If the type contains `bigint` or a bigint literal and the string isn't a valid bigint, exclude bigints
                            if ((allTypeFlags & TypeFlags.BigIntLike) != 0 && !IsValidBigIntString(str, true))
                            {
                                allTypeFlags &= ~TypeFlags.BigIntLike;
                            }
                            // for each type in the constraint, find the highest priority matching type
                            var matchingType = c.neverType;
                            foreach (var right in constraintTypes)
                            {
                                matchingType = MatchingTypeOf(matchingType, right, matchSource, str, allTypeFlags);
                            }
                            if ((Types.Flags(matchingType) & TypeFlags.Never) == 0)
                            {
                                InferFromTypes(matchingType, matchTarget);
                                continue;
                            }
                        }
                    }
                }
                InferFromTypes(matchSource, matchTarget);
            }
        }

        // the reducer of inferToTemplateLiteralType's matching type
        private Type MatchingTypeOf(Type left, Type right, Type source, string str, TypeFlags allTypeFlags)
        {
            var lf = Types.Flags(left);
            var rf = Types.Flags(right);
            if ((rf & allTypeFlags) == 0)
            {
                return left;
            }
            if ((lf & TypeFlags.String) != 0) return left;
            if ((rf & TypeFlags.String) != 0) return source;
            if ((lf & TypeFlags.TemplateLiteral) != 0) return left;
            if ((rf & TypeFlags.TemplateLiteral) != 0 && c.IsTypeMatchedByTemplateLiteralType(source, right)) return source;
            if ((lf & TypeFlags.StringMapping) != 0) return left;
            if ((rf & TypeFlags.StringMapping) != 0 && str == c.ApplyStringMapping(Types.Symbol(right), str)) return source;
            if ((lf & TypeFlags.StringLiteral) != 0) return left;
            if ((rf & TypeFlags.StringLiteral) != 0 && (string)Types.Literal(right).Value! == str) return right;
            if ((lf & TypeFlags.Number) != 0) return left;
            if ((rf & TypeFlags.Number) != 0) return c.GetNumberLiteralType(JsNumber.FromString(str));
            if ((lf & TypeFlags.Enum) != 0) return left;
            if ((rf & TypeFlags.Enum) != 0) return c.GetNumberLiteralType(JsNumber.FromString(str));
            if ((lf & TypeFlags.NumberLiteral) != 0) return left;
            if ((rf & TypeFlags.NumberLiteral) != 0 && (double)Types.Literal(right).Value! == JsNumber.FromString(str)) return right;
            if ((lf & TypeFlags.BigInt) != 0) return left;
            if ((rf & TypeFlags.BigInt) != 0) return c.GetBigIntLiteralType(str);
            if ((lf & TypeFlags.BigIntLiteral) != 0) return left;
            if ((rf & TypeFlags.BigIntLiteral) != 0 && (string)Types.Literal(right).Value! == str) return right;
            if ((lf & TypeFlags.Boolean) != 0) return left;
            if ((rf & TypeFlags.Boolean) != 0) return str == "true" ? c.trueType : str == "false" ? c.falseType : c.booleanType;
            if ((lf & TypeFlags.BooleanLiteral) != 0) return left;
            if ((rf & TypeFlags.BooleanLiteral) != 0 && Types.IntrinsicName(right) == str) return right;
            if ((lf & TypeFlags.Undefined) != 0) return left;
            if ((rf & TypeFlags.Undefined) != 0 && Types.IntrinsicName(right) == str) return right;
            if ((lf & TypeFlags.Null) != 0) return left;
            if ((rf & TypeFlags.Null) != 0 && Types.IntrinsicName(right) == str) return right;
            return left;
        }

        private void InferFromGenericMappedTypes(Type source, Type target)
        {
            // The source and target types are generic types { [P in S]: X } and { [P in T]: Y }, so we infer
            // from S to T and from X to Y.
            InferFromTypes(c.GetConstraintTypeFromMappedType(source), c.GetConstraintTypeFromMappedType(target));
            InferFromTypes(c.GetTemplateTypeFromMappedType(source), c.GetTemplateTypeFromMappedType(target));
            var sourceNameType = c.GetNameTypeFromMappedType(source);
            var targetNameType = c.GetNameTypeFromMappedType(target);
            if (!sourceNameType.IsNone && !targetNameType.IsNone)
            {
                InferFromTypes(sourceNameType, targetNameType);
            }
        }

        private void InferFromObjectTypes(Type source, Type target)
        {
            if (IsSameReference(source, target))
            {
                // If source and target are references to the same generic type, infer from type arguments
                InferFromTypeArguments(c.GetTypeArguments(source).ToArray(), c.GetTypeArguments(target).ToArray(), c.GetVariances(Types.Object(source).Target));
                return;
            }
            if (c.IsGenericMappedType(source) && c.IsGenericMappedType(target))
            {
                InferFromGenericMappedTypes(source, target);
            }
            if ((Types.ObjectFlagsOf(target) & ObjectFlags.Mapped) != 0 && c.StoreOf(Types.Object(target).Declaration).NameType(Types.Object(target).Declaration.Node).IsNone)
            {
                var constraintType = c.GetConstraintTypeFromMappedType(target);
                if (InferToMappedType(source, target, constraintType))
                {
                    return;
                }
            }
            // Infer from the members of source and target only if the two types are possibly related
            if (c.TypesDefinitelyUnrelated(source, target))
            {
                return;
            }
            if (c.IsArrayOrTupleType(source))
            {
                if (c.IsTupleType(target))
                {
                    InferToTupleType(source, target);
                    return;
                }
                if (c.IsArrayType(target))
                {
                    InferFromIndexTypes(source, target);
                    return;
                }
            }
            InferFromProperties(source, target);
            InferFromSignatures(source, target, SignatureKind.Call);
            InferFromSignatures(source, target, SignatureKind.Construct);
            InferFromIndexTypes(source, target);
        }

        // inferFromObjectTypes: an array or tuple source and a tuple target
        private void InferToTupleType(Type source, Type target)
        {
            var sourceArity = c.GetTypeReferenceArity(source);
            var targetArity = c.GetTypeReferenceArity(target);
            var elementTypes = c.GetTypeArguments(target).ToArray();
            var targetTarget = c.TupleTarget(target);
            var elementFlags = targetTarget.ElementFlags!;
            var sourceIsTuple = c.IsTupleType(source);
            var sourceArguments = c.GetTypeArguments(source).ToArray();
            // When source and target are tuple types with the same structure (fixed, variadic, and rest are matched
            // to the same kind in each position), simply infer between the element types.
            if (sourceIsTuple && c.IsTupleTypeStructureMatching(source, target))
            {
                for (var i = 0; i < targetArity; i++)
                {
                    InferFromTypes(sourceArguments[i], elementTypes[i]);
                }
                return;
            }
            var sourceTarget = sourceIsTuple ? c.TupleTarget(source) : default;
            var startLength = sourceIsTuple ? Math.Min(sourceTarget.FixedLength, targetTarget.FixedLength) : 0;
            var endLength = Math.Min(sourceIsTuple ? GetEndElementCount(sourceTarget, ElementFlags.Fixed) : 0,
                (targetTarget.CombinedFlags & ElementFlags.Variable) != 0 ? GetEndElementCount(targetTarget, ElementFlags.Fixed) : 0);
            // Infer between starting fixed elements.
            for (var i = 0; i < startLength; i++)
            {
                InferFromTypes(sourceArguments[i], elementTypes[i]);
            }
            if (!sourceIsTuple || (sourceArity - startLength - endLength == 1 && (sourceTarget.ElementFlags![startLength] & ElementFlags.Rest) != 0))
            {
                // Single rest element remains in source, infer from that to every element in target
                var restType = sourceArguments[startLength];
                for (var i = startLength; i < targetArity - endLength; i++)
                {
                    InferFromTypes((elementFlags[i] & ElementFlags.Variadic) != 0 ? c.CreateArrayType(restType) : restType, elementTypes[i]);
                }
            }
            else
            {
                var middleLength = targetArity - startLength - endLength;
                if (middleLength == 2)
                {
                    if ((elementFlags[startLength] & elementFlags[startLength + 1] & ElementFlags.Variadic) != 0)
                    {
                        // Middle of target is [...T, ...U] and source is tuple type
                        var targetInfo = GetInferenceInfoForType(elementTypes[startLength]);
                        if (targetInfo is not null && targetInfo.ImpliedArity is { } impliedArity)
                        {
                            // Infer slices from source based on implied arity of T.
                            InferFromTypes(c.SliceTupleType(source, startLength, endLength + sourceArity - impliedArity), elementTypes[startLength]);
                            InferFromTypes(c.SliceTupleType(source, startLength + impliedArity, endLength), elementTypes[startLength + 1]);
                        }
                    }
                    else if ((elementFlags[startLength] & ElementFlags.Variadic) != 0 && (elementFlags[startLength + 1] & ElementFlags.Rest) != 0)
                    {
                        // Middle of target is [...T, ...rest] and source is tuple type
                        // if T is constrained by a fixed-size tuple we might be able to use its arity to infer T
                        var param = GetInferenceInfoForType(elementTypes[startLength])?.TypeParameter ?? Type.None;
                        var constraint = !param.IsNone ? c.GetBaseConstraintOfType(param) : Type.None;
                        if (!constraint.IsNone && c.IsTupleType(constraint) && (c.TupleTarget(constraint).CombinedFlags & ElementFlags.Variable) == 0)
                        {
                            var impliedArity = c.TupleTarget(constraint).FixedLength;
                            InferFromTypes(c.SliceTupleType(source, startLength, sourceArity - (startLength + impliedArity)), elementTypes[startLength]);
                            InferFromTypes(c.GetElementTypeOfSliceOfTupleType(source, startLength + impliedArity, endLength), elementTypes[startLength + 1]);
                        }
                    }
                    else if ((elementFlags[startLength] & ElementFlags.Rest) != 0 && (elementFlags[startLength + 1] & ElementFlags.Variadic) != 0)
                    {
                        // Middle of target is [...rest, ...T] and source is tuple type
                        // if T is constrained by a fixed-size tuple we might be able to use its arity to infer T
                        var param = GetInferenceInfoForType(elementTypes[startLength + 1])?.TypeParameter ?? Type.None;
                        var constraint = !param.IsNone ? c.GetBaseConstraintOfType(param) : Type.None;
                        if (!constraint.IsNone && c.IsTupleType(constraint) && (c.TupleTarget(constraint).CombinedFlags & ElementFlags.Variable) == 0)
                        {
                            var impliedArity = c.TupleTarget(constraint).FixedLength;
                            var endIndex = sourceArity - GetEndElementCount(targetTarget, ElementFlags.Fixed);
                            var startIndex = endIndex - impliedArity;
                            var trailingSlice = c.CreateTupleType(sourceArguments[startIndex..endIndex], sourceTarget!.ElementFlags![startIndex..endIndex], false,
                                sourceTarget.LabeledElementDeclarations?[startIndex..endIndex]);
                            InferFromTypes(c.GetElementTypeOfSliceOfTupleType(source, startLength, endLength + impliedArity), elementTypes[startLength]);
                            InferFromTypes(trailingSlice, elementTypes[startLength + 1]);
                        }
                    }
                }
                else if (middleLength == 1 && (elementFlags[startLength] & ElementFlags.Variadic) != 0)
                {
                    // Middle of target is exactly one variadic element. Infer the slice between the fixed parts in the source.
                    // If target ends in optional element(s), make a lower priority a speculative inference.
                    var endsInOptional = (targetTarget.ElementFlags![targetArity - 1] & ElementFlags.Optional) != 0;
                    var sourceSlice = c.SliceTupleType(source, startLength, endLength);
                    InferWithPriority(sourceSlice, elementTypes[startLength], endsInOptional ? InferencePriority.SpeculativeTuple : InferencePriority.None);
                }
                else if (middleLength == 1 && (elementFlags[startLength] & ElementFlags.Rest) != 0)
                {
                    // Middle of target is exactly one rest element. If middle of source is not empty, infer union of middle element types.
                    var restType = c.GetElementTypeOfSliceOfTupleType(source, startLength, endLength);
                    if (!restType.IsNone)
                    {
                        InferFromTypes(restType, elementTypes[startLength]);
                    }
                }
            }
            // Infer between ending fixed elements
            for (var i = 0; i < endLength; i++)
            {
                InferFromTypes(sourceArguments[sourceArity - i - 1], elementTypes[targetArity - i - 1]);
            }
        }

        private void InferFromProperties(Type source, Type target)
        {
            var properties = c.GetPropertiesOfObjectType(target);
            foreach (var targetProp in properties.ToArray())
            {
                var sourceProp = c.GetPropertyOfType(source, c.NameIdOf(targetProp));
                if (!sourceProp.IsNone)
                {
                    InferFromTypes(c.RemoveMissingType(c.GetTypeOfSymbol(sourceProp), (c.SymbolFlagsOf(sourceProp) & SymbolFlags.Optional) != 0),
                        c.RemoveMissingType(c.GetTypeOfSymbol(targetProp), (c.SymbolFlagsOf(targetProp) & SymbolFlags.Optional) != 0));
                }
            }
        }

        private void InferFromSignatures(Type source, Type target, SignatureKind kind)
        {
            var sourceSignatures = c.GetSignaturesOfType(source, kind);
            var sourceLen = sourceSignatures.Count;
            if (sourceLen > 0)
            {
                // We match source and target signatures from the bottom up, and if the source has fewer signatures
                // than the target, we infer from the first source signature to the excess target signatures.
                var targetSignatures = c.GetSignaturesOfType(target, kind);
                var targetLen = targetSignatures.Count;
                for (var i = 0; i < targetLen; i++)
                {
                    var sourceIndex = Math.Max(sourceLen - targetLen + i, 0);                    InferFromSignature(c.GetBaseSignature(sourceSignatures[sourceIndex]), c.GetErasedSignature(targetSignatures[i]));
                }
            }
        }

        private void InferFromSignature(Signature source, Signature target)
        {
            if ((c.Signatures.Record(source).Flags & SignatureFlags.IsNonInferrable) == 0)
            {
                var saveBivariant = bivariant;
                var declaration = c.Signatures.Record(target).Declaration;
                var kind = declaration.IsNone ? SyntaxKind.Unknown : c.Kind(declaration);
                // Once we descend into a bivariant signature we remain bivariant for all nested inferences
                bivariant = bivariant || kind is SyntaxKind.MethodDeclaration or SyntaxKind.MethodSignature or SyntaxKind.Constructor;
                c.ApplyToParameterTypes(source, target, InferFromContravariantTypesIfStrictFunctionTypes);
                bivariant = saveBivariant;
            }
            c.ApplyToReturnTypes(source, target, InferFromTypes);
        }

        private void InferFromIndexTypes(Type source, Type target)
        {
            // Inferences across mapped type index signatures are pretty much the same a inferences to homomorphic variables
            var indexPriority = (Types.ObjectFlagsOf(source) & Types.ObjectFlagsOf(target) & ObjectFlags.Mapped) != 0 ? InferencePriority.HomomorphicMappedType : InferencePriority.None;
            var indexInfos = c.GetIndexInfosOfType(target).ToArray();
            if (c.IsObjectTypeWithInferableIndex(source))
            {
                foreach (var targetInfo in indexInfos)
                {
                    var propTypes = new List<Type>();
                    foreach (var prop in c.GetPropertiesOfType(source))
                    {
                        if (c.IsApplicableIndexType(c.GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique), targetInfo.KeyType))
                        {
                            var propType = c.GetTypeOfSymbol(prop);
                            propTypes.Add((c.SymbolFlagsOf(prop) & SymbolFlags.Optional) != 0 ? c.RemoveMissingOrUndefinedType(propType) : propType);
                        }
                    }
                    foreach (var info in c.GetIndexInfosOfType(source))
                    {
                        if (c.IsApplicableIndexType(info.KeyType, targetInfo.KeyType))
                        {
                            propTypes.Add(info.Type);
                        }
                    }
                    if (propTypes.Count > 0)
                    {
                        InferWithPriority(c.GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(propTypes)), targetInfo.Type, indexPriority);
                    }
                }
            }
            foreach (var targetInfo in indexInfos)
            {
                var sourceInfo = c.GetApplicableIndexInfo(source, targetInfo.KeyType);
                if (sourceInfo is not null)
                {
                    InferWithPriority(sourceInfo.Type, targetInfo.Type, indexPriority);
                }
            }
        }
    }

    // checker.ts isTypeOrBaseIdenticalTo
    private bool IsTypeOrBaseIdenticalTo(Type s, Type t) =>
        t == missingType ? s == t
        : IsTypeIdenticalTo(s, t) || ((Types.Flags(t) & TypeFlags.String) != 0 && (Types.Flags(s) & TypeFlags.StringLiteral) != 0) ||
            ((Types.Flags(t) & TypeFlags.Number) != 0 && (Types.Flags(s) & TypeFlags.NumberLiteral) != 0);

    // checker.ts isTypeCloselyMatchedBy
    private bool IsTypeCloselyMatchedBy(Type s, Type t)
    {
        if ((Types.Flags(s) & TypeFlags.Object) != 0 && (Types.Flags(t) & TypeFlags.Object) != 0 && !Types.Symbol(s).IsNone && Types.Symbol(s) == Types.Symbol(t))
        {
            return true;
        }
        var sourceAlias = Types.Record(s).AliasSymbol;
        return !sourceAlias.IsNone && Types.Record(s).AliasTypeArguments.IsPresent && sourceAlias == Types.Record(t).AliasSymbol;
    }

    // checker.ts hasPrimitiveConstraint
    private bool HasPrimitiveConstraint(Type type)
    {
        var constraint = GetConstraintOfTypeParameter(type);
        return !constraint.IsNone && MaybeTypeOfKind((Types.Flags(constraint) & TypeFlags.Conditional) != 0 ? GetDefaultConstraintOfConditionalType(constraint) : constraint,
            TypeFlags.Primitive | TypeFlags.Index | TypeFlags.TemplateLiteral | TypeFlags.StringMapping);
    }

    // checker.ts unionObjectAndArrayLiteralCandidates
    private List<Type> UnionObjectAndArrayLiteralCandidates(List<Type> candidates)
    {
        if (candidates.Count > 1)
        {
            var objectLiterals = candidates.FindAll(IsObjectOrArrayLiteralType);
            if (objectLiterals.Count > 0)
            {
                var literalsType = GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(objectLiterals), UnionReduction.Subtype);
                var result = candidates.FindAll(t => !IsObjectOrArrayLiteralType(t));
                result.Add(literalsType);
                return result;
            }
        }
        return candidates;
    }

    // checker.ts getContravariantInference
    private Type GetContravariantInference(InferenceInfo inference) =>
        (inference.Priority!.Value & InferencePriority.PriorityImpliesCombination) != 0
            ? GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(inference.ContraCandidates!))
            : GetCommonSubtype(inference.ContraCandidates!);

    // checker.ts getCovariantInference
    private Type GetCovariantInference(InferenceInfo inference, Signature signature)
    {
        // Extract all object and array literal types and replace them with a single widened and normalized type.
        var candidates = UnionObjectAndArrayLiteralCandidates(inference.Candidates!);
        // We widen inferred literal types if
        // all inferences were made to top-level occurrences of the type parameter, and
        // the type parameter has no constraint or its constraint includes no primitive or literal types, and
        // the type parameter was fixed during inference or does not occur at top-level in the return type.
        var primitiveConstraint = HasPrimitiveConstraint(inference.TypeParameter) || IsConstTypeVariable(inference.TypeParameter);
        var widenLiteralTypes = !primitiveConstraint && inference.TopLevel &&
            (inference.IsFixed || !IsTypeParameterAtTopLevelInReturnType(signature, inference.TypeParameter));
        var baseCandidates = primitiveConstraint ? candidates.ConvertAll(GetRegularTypeOfLiteralType)
            : widenLiteralTypes ? candidates.ConvertAll(GetWidenedLiteralType)
            : candidates;
        // If all inferences were made from a position that implies a combined result, infer a union type.
        // Otherwise, infer a common supertype.
        var unwidenedType = (inference.Priority!.Value & InferencePriority.PriorityImpliesCombination) != 0
            ? GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(baseCandidates), UnionReduction.Subtype)
            : GetCommonSupertype(baseCandidates);
        return GetWidenedType(unwidenedType);
    }

    /// <summary>checker.ts getInferredType.</summary>
    internal Type GetInferredType(InferenceContext context, int index)
    {
        var inference = context.Inferences[index];
        if (inference.InferredType.IsNone)
        {
            var inferredType = Type.None;
            var fallbackType = Type.None;
            if (!context.Signature.IsNone)
            {
                var inferredCovariantType = inference.Candidates is not null ? GetCovariantInference(inference, context.Signature) : Type.None;
                var inferredContravariantType = inference.ContraCandidates is not null ? GetContravariantInference(inference) : Type.None;
                if (!inferredCovariantType.IsNone || !inferredContravariantType.IsNone)
                {
                    // If we have both co- and contra-variant inferences, we prefer the co-variant inference if it is not 'never',
                    // all co-variant inferences are assignable to it (i.e. it isn't one of a conflicting set of candidates), it is
                    // assignable to some contra-variant inference, and no other type parameter is constrained to this type parameter
                    // and has inferences that would conflict. Otherwise, we prefer the contra-variant inference.
                    // Similarly ignore co-variant `any` inference when both are available as almost everything is assignable to it
                    // and it would spoil the overall inference.
                    var preferCovariantType = !inferredCovariantType.IsNone && (inferredContravariantType.IsNone ||
                        ((Types.Flags(inferredCovariantType) & (TypeFlags.Never | TypeFlags.Any)) == 0 &&
                            inference.ContraCandidates!.Exists(t => IsTypeAssignableTo(inferredCovariantType, t)) &&
                            context.Inferences.TrueForAll(other =>
                                (other != inference && GetConstraintOfTypeParameter(other.TypeParameter) != inference.TypeParameter) ||
                                other.Candidates is null || other.Candidates.TrueForAll(t => IsTypeAssignableTo(t, inferredCovariantType)))));
                    inferredType = preferCovariantType ? inferredCovariantType : inferredContravariantType;
                    fallbackType = preferCovariantType ? inferredContravariantType : inferredCovariantType;
                }
                else if ((context.Flags & InferenceFlags.NoDefault) != 0)
                {
                    // We use silentNeverType as the wildcard that signals no inferences.
                    inferredType = silentNeverType;
                }
                else
                {
                    // Infer either the default or the empty object type when no inferences were
                    // made. It is important to remember that in this case, inference still
                    // succeeds, meaning there is no error for not having inference candidates. An
                    // inference error only occurs when there are *conflicting* candidates, i.e.
                    // candidates with no common supertype.
                    var defaultType = GetDefaultFromTypeParameter(inference.TypeParameter);
                    if (!defaultType.IsNone)
                    {
                        // Instantiate the default type. Any forward reference to a type
                        // parameter should be instantiated to the empty object type.
                        inferredType = InstantiateType(defaultType, MergeTypeMappers(CreateBackreferenceMapper(context, index), context.NonFixingMapper));
                    }
                }
            }
            else
            {
                inferredType = GetTypeFromInference(inference);
            }
            inference.InferredType = !inferredType.IsNone ? inferredType : GetDefaultTypeArgumentType((context.Flags & InferenceFlags.AnyDefault) != 0);
            var constraint = GetConstraintOfTypeParameter(inference.TypeParameter);
            if (!constraint.IsNone)
            {
                var instantiatedConstraint = InstantiateType(constraint, context.NonFixingMapper);
                if (!inferredType.IsNone)
                {
                    var constraintWithThis = GetTypeWithThisArgument(instantiatedConstraint, inferredType);
                    if (!context.CompareTypes(inferredType, constraintWithThis))
                    {
                        // If we have a pure return type inference, we may succeed by removing constituents of the inferred type
                        // that aren't assignable to the constraint type (pure return type inferences are speculation anyway).
                        var filteredByConstraint = inference.Priority == InferencePriority.ReturnType ? FilterType(inferredType, t => context.CompareTypes(t, constraintWithThis)) : neverType;
                        inferredType = (Types.Flags(filteredByConstraint) & TypeFlags.Never) == 0 ? filteredByConstraint : Type.None;
                    }
                }
                if (inferredType.IsNone)
                {
                    // If the fallback type satisfies the constraint, we pick it. Otherwise, we pick the constraint.
                    inferredType = !fallbackType.IsNone && context.CompareTypes(fallbackType, GetTypeWithThisArgument(instantiatedConstraint, fallbackType)) ? fallbackType : instantiatedConstraint;
                }
                inference.InferredType = inferredType;
            }
            ClearActiveMapperCaches();
        }
        return inference.InferredType;
    }

    // checker.ts getDefaultTypeArgumentType
    private Type GetDefaultTypeArgumentType(bool isInJavaScriptFile) => isInJavaScriptFile ? anyType : unknownType;

    /// <summary>checker.ts getInferredTypes.</summary>
    internal Type[] GetInferredTypes(InferenceContext context)
    {
        var result = new Type[context.Inferences.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = GetInferredType(context, i);
        }
        return result;
    }

    // checker.ts literalTypesWithSameBaseType
    private bool LiteralTypesWithSameBaseType(List<Type> types)
    {
        var commonBaseType = Type.None;
        foreach (var t in types)
        {
            if ((Types.Flags(t) & TypeFlags.Never) == 0)
            {
                var baseType = GetBaseTypeOfLiteralType(t);
                if (commonBaseType.IsNone)
                {
                    commonBaseType = baseType;
                }
                if (baseType == t || baseType != commonBaseType)
                {
                    return false;
                }
            }
        }
        return true;
    }

    // checker.ts getCombinedTypeFlags
    private TypeFlags GetCombinedTypeFlags(IEnumerable<Type> types)
    {
        TypeFlags flags = 0;
        foreach (var t in types)
        {
            flags |= (Types.Flags(t) & TypeFlags.Union) != 0 ? GetCombinedTypeFlags(Types.Elements(Types.Union(t).Types).ToArray()) : Types.Flags(t);
        }
        return flags;
    }

    // checker.ts getCommonSupertype
    private Type GetCommonSupertype(List<Type> types)
    {
        if (types.Count == 1)
        {
            return types[0];
        }
        // Remove nullable types from each of the candidates.
        var primaryTypes = types;
        if (strictNullChecks)
        {
            List<Type>? mapped = null;
            for (var i = 0; i < types.Count; i++)
            {
                var filtered = FilterType(types[i], u => (Types.Flags(u) & TypeFlags.Nullable) == 0);
                if (filtered != types[i] && mapped is null)
                {
                    mapped = types.GetRange(0, i);
                }
                mapped?.Add(filtered);
            }
            primaryTypes = mapped ?? types;
        }
        // When the candidate types are all literal types with the same base type, return a union
        // of those literal types. Otherwise, return the leftmost type for which no type to the
        // right is a supertype.
        var superTypeOrUnion = LiteralTypesWithSameBaseType(primaryTypes)
            ? GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(primaryTypes))
            : GetSingleCommonSupertype(primaryTypes);
        // Add any nullable types that occurred in the candidates back to the result.
        return primaryTypes == types ? superTypeOrUnion : GetNullableType(superTypeOrUnion, GetCombinedTypeFlags(types) & TypeFlags.Nullable);
    }

    // checker.ts getSingleCommonSupertype
    private Type GetSingleCommonSupertype(List<Type> types)
    {
        // First, find the leftmost type for which no type to the right is a strict supertype, and if that
        // type is a strict supertype of all other candidates, return it. Otherwise, return the leftmost type
        // for which no type to the right is a (regular) supertype.
        var candidate = types[0];
        for (var i = 1; i < types.Count; i++)
        {
            if (IsTypeStrictSubtypeOf(candidate, types[i]))
            {
                candidate = types[i];
            }
        }
        if (types.TrueForAll(t => t == candidate || IsTypeStrictSubtypeOf(t, candidate)))
        {
            return candidate;
        }
        var result = types[0];
        for (var i = 1; i < types.Count; i++)
        {
            if (IsTypeSubtypeOf(result, types[i]))
            {
                result = types[i];
            }
        }
        return result;
    }

    // checker.ts getCommonSubtype: the leftmost type for which no type to the right is a subtype
    private Type GetCommonSubtype(List<Type> types)
    {
        var result = types[0];
        for (var i = 1; i < types.Count; i++)
        {
            if (IsTypeSubtypeOf(types[i], result))
            {
                result = types[i];
            }
        }
        return result;
    }

    // checker.ts isTupleTypeStructureMatching
    private bool IsTupleTypeStructureMatching(Type t1, Type t2)
    {
        if (GetTypeReferenceArity(t1) != GetTypeReferenceArity(t2))
        {
            return false;
        }
        var flags1 = TupleTarget(t1).ElementFlags!;
        var flags2 = TupleTarget(t2).ElementFlags!;
        for (var i = 0; i < flags1.Length; i++)
        {
            if ((flags1[i] & ElementFlags.Variable) != (flags2[i] & ElementFlags.Variable))
            {
                return false;
            }
        }
        return true;
    }

    private readonly Dictionary<Signature, Signature> baseSignatureCache = [];

    // checker.ts getBaseSignature
    private Signature GetBaseSignature(Signature signature)
    {
        var typeParameters = Types.Elements(Signatures.Record(signature).TypeParameters).ToArray();
        if (typeParameters.Length == 0)
        {
            return signature;
        }
        if (baseSignatureCache.TryGetValue(signature, out var cached))
        {
            return cached;
        }
        var typeEraser = CreateTypeEraser(typeParameters);
        var baseConstraintMapper = CreateTypeMapper(typeParameters, Array.ConvertAll(typeParameters, tp => GetConstraintOfTypeParameter(tp) is var c && !c.IsNone ? c : unknownType));
        var baseConstraints = Array.ConvertAll(typeParameters, tp => InstantiateType(tp, baseConstraintMapper) is var t && !t.IsNone ? t : unknownType);
        // Run N type params thru the immediate constraint mapper up to N times
        // This way any noncircular interdependent type parameters are definitely resolved to their external dependencies
        for (var i = 0; i < typeParameters.Length - 1; i++)
        {
            baseConstraints = InstantiateTypes(baseConstraints, baseConstraintMapper, out _);
        }
        // and then apply a type eraser to remove any remaining circularly dependent type parameters
        baseConstraints = InstantiateTypes(baseConstraints, typeEraser, out _);
        var result = InstantiateSignature(signature, CreateTypeMapper(typeParameters, baseConstraints), eraseTypeParameters: true);
        baseSignatureCache[signature] = result;
        return result;
    }

    /// <summary>checker.ts hasInferenceCandidates.</summary>
    internal static bool HasInferenceCandidates(InferenceInfo info) => info.Candidates is not null || info.ContraCandidates is not null;

    /// <summary>checker.ts hasInferenceCandidatesOrDefault.</summary>
    internal bool HasInferenceCandidatesOrDefault(InferenceInfo info) => info.Candidates is not null || info.ContraCandidates is not null || HasTypeParameterDefault(info.TypeParameter);

    // checker.ts hasOverlappingInferences
    private static bool HasOverlappingInferences(List<InferenceInfo> a, List<InferenceInfo> b)
    {
        for (var i = 0; i < a.Count; i++)
        {
            if (HasInferenceCandidates(a[i]) && HasInferenceCandidates(b[i]))
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts mergeInferences
    private static void MergeInferences(List<InferenceInfo> target, List<InferenceInfo> source)
    {
        for (var i = 0; i < target.Count; i++)
        {
            if (!HasInferenceCandidates(target[i]) && HasInferenceCandidates(source[i]))
            {
                target[i] = source[i];
            }
        }
    }

    // checker.ts getUniqueTypeParameters
    private Type[] GetUniqueTypeParameters(InferenceContext context, Type[] typeParameters)
    {
        var result = new List<Type>();
        List<Type>? oldTypeParameters = null;
        List<Type>? newTypeParameters = null;
        foreach (var tp in typeParameters)
        {
            var name = NameOf(Types.Symbol(tp));
            if (HasTypeParameterByName(context.InferredTypeParameters, name) || HasTypeParameterByName(result, name))
            {
                var all = new List<Type>();
                if (context.InferredTypeParameters is not null)
                {
                    all.AddRange(context.InferredTypeParameters);
                }
                all.AddRange(result);
                var newName = GetUniqueTypeParameterName(all, name);
                var symbol = CreateSymbol(SymbolFlags.TypeParameter, newName);
                var newTypeParameter = Types.CreateTypeParameter(symbol);
                Types.TypeParameter(newTypeParameter).Target = tp;
                (oldTypeParameters ??= []).Add(tp);
                (newTypeParameters ??= []).Add(newTypeParameter);
                result.Add(newTypeParameter);
            }
            else
            {
                result.Add(tp);
            }
        }
        if (newTypeParameters is not null)
        {
            var mapper = CreateTypeMapper([.. oldTypeParameters!], [.. newTypeParameters]);
            foreach (var tp in newTypeParameters)
            {
                Types.TypeParameter(tp).Mapper = mapper;
            }
        }
        return [.. result];
    }

    // checker.ts hasTypeParameterByName
    private bool HasTypeParameterByName(List<Type>? typeParameters, string name) => typeParameters is not null && typeParameters.Exists(tp => NameOf(Types.Symbol(tp)) == name);

    // checker.ts getUniqueTypeParameterName
    private string GetUniqueTypeParameterName(List<Type> typeParameters, string baseName)
    {
        var len = baseName.Length;
        while (len > 1 && baseName[len - 1] >= '0' && baseName[len - 1] <= '9')
        {
            len--;
        }
        var s = baseName[..len];
        for (var index = 1; ; index++)
        {
            var augmentedName = s + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!HasTypeParameterByName(typeParameters, augmentedName))
            {
                return augmentedName;
            }
        }
    }
}
