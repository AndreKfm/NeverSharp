// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (type mappers, instantiateType and its workers, instantiateSignature,
// instantiateSymbol, getObjectTypeInstantiation, resolveTypeReferenceMembers, getTypeAliasInstantiation, type
// parameter defaults and constraints, fillMissingTypeArguments, getSignatureInstantiation)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/mapper.go (TypeMapper kinds) and internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C3. Module CO6: mappers are handles to records of the checker's
// MapperStore (typescript-go: TypeMapper interface with one struct per kind); records hold the handle's id.

using System.Globalization;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>types.ts ElementFlags (tuple element kinds).</summary>
[Flags]
internal enum ElementFlags
{
    None = 0,
    Required = 1 << 0,  // T
    Optional = 1 << 1,  // T?
    Rest = 1 << 2,      // ...T[]
    Variadic = 1 << 3,  // ...T
    Fixed = Required | Optional,
    Variable = Rest | Variadic,
    NonRequired = Optional | Rest | Variadic,
    NonRest = Required | Optional | Variadic,
}

/// <summary>
/// checker.ts TypeMapper (mapper.go TypeMapper) as a handle (module CO6): the id of a record of the checker's
/// <see cref="MapperStore"/>, 0 for none (undefined). Every make* call creates a new record, so <c>==</c> is
/// TypeScript's identity of mapper objects (<c>mapper === other</c>, the active mappers' caches).
/// </summary>
internal readonly struct TypeMapper : IEquatable<TypeMapper>
{
    internal readonly int Id;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal TypeMapper(int id)
    {
        Id = id;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static TypeMapper None => default;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsNone => Id == 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(TypeMapper left, TypeMapper right) => left.Id == right.Id;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(TypeMapper left, TypeMapper right) => left.Id != right.Id;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(TypeMapper other) => Id == other.Id;

    public override bool Equals(object? obj) => obj is TypeMapper other && Id == other.Id;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override int GetHashCode() => Id;
}

/// <summary>
/// checker.ts TypeMapKind, with the deferred mappers of inference contexts (makeFixingMapperForContext,
/// makeNonFixingMapperForContext) as kinds of their own: their targets are computed from the context, not from closures.
/// </summary>
internal enum TypeMapKind : byte
{
    None,
    Simple,     // A: source, B: target (0: any)
    Array,      // A: types chunk, B: offset of the sources (the targets follow them), C: count
    ArrayToAny, // A: types chunk, B: offset of the sources, C: count; every source maps to any
    Deferred,   // A: object index (DeferredTargets)
    Fixing,     // A: object index (the InferenceContext)
    NonFixing,  // A: object index (the InferenceContext)
    Function,   // A: object index (Func<Type, Type>)
    Composite,  // A: mapper1, B: mapper2
    Merged,     // A: mapper1, B: mapper2
}

/// <summary>A type mapper of a <see cref="MapperStore"/> (module CO6): no references.</summary>
internal struct MapperRecord
{
    public TypeMapKind Kind;
    public int A;
    public int B;
    public int C;
}

/// <summary>makeDeferredTypeMapper's sources and target functions (the deferred mappers that are not an inference context's).</summary>
internal sealed class DeferredTargets(Type[] sources, Func<Type>[] targets)
{
    internal readonly Type[] Sources = sources;
    internal readonly Func<Type>[] Targets = targets;
}

/// <summary>
/// The type mappers of one checker (module CO6): records in creation order in chunks, the array kinds' sources and
/// targets in chunks of types, and the objects of the function and deferred kinds (delegates, inference contexts) in a
/// table. The collector traces only that table, not the records.
/// </summary>
internal sealed class MapperStore
{
    private const int Shift = 12;
    private const int Size = 1 << Shift;
    private const int Mask = Size - 1;
    private const int TypeChunkSize = 1 << 14;

    private MapperRecord[][] records = [new MapperRecord[Size]];
    private int count = 1; // 0: none
    private Type[][] typeChunks = [new Type[TypeChunkSize]];
    private int typeChunk;
    private int typeUsed;
    private readonly List<object> objects = [];

    /// <summary>The record of a mapper (valid while the store grows: chunks never move).</summary>
    internal ref MapperRecord this[TypeMapper mapper]
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => ref records[mapper.Id >> Shift][mapper.Id & Mask];
    }

    /// <summary>A chunk of the array kinds' types.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Type[] TypeChunk(int chunk) => typeChunks[chunk];

    /// <summary>An object of the function and deferred kinds.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal object ObjectAt(int index) => objects[index];

    internal TypeMapper Add(TypeMapKind kind, int a, int b, int c = 0)
    {
        var id = count;
        if ((id & Mask) == 0)
        {
            AddChunk(id >> Shift);
        }
        ref var record = ref records[id >> Shift][id & Mask];
        record.Kind = kind;
        record.A = a;
        record.B = b;
        record.C = c;
        count = id + 1;
        return new TypeMapper(id);
    }

    private void AddChunk(int chunk)
    {
        if (chunk == records.Length)
        {
            Array.Resize(ref records, records.Length * 2);
        }
        records[chunk] = new MapperRecord[Size];
    }

    internal int AddObject(object value)
    {
        objects.Add(value);
        return objects.Count - 1;
    }

    /// <summary>An array kind: the sources, then the targets (none: every source maps to any).</summary>
    internal TypeMapper AddArray(ReadOnlySpan<Type> sources, ReadOnlySpan<Type> targets, bool toAny)
    {
        var length = toAny ? sources.Length : sources.Length * 2;
        if (typeUsed + length > typeChunks[typeChunk].Length)
        {
            typeChunk++;
            if (typeChunk == typeChunks.Length)
            {
                Array.Resize(ref typeChunks, typeChunks.Length * 2);
            }
            typeChunks[typeChunk] = new Type[Math.Max(TypeChunkSize, length)];
            typeUsed = 0;
        }
        var offset = typeUsed;
        var types = typeChunks[typeChunk];
        sources.CopyTo(types.AsSpan(offset));
        if (!toAny)
        {
            // createTypeMapper reads targets[i] for each source (undefined beyond the end of a shorter array: None)
            var n = Math.Min(targets.Length, sources.Length);
            targets[..n].CopyTo(types.AsSpan(offset + sources.Length));
        }
        typeUsed = offset + length;
        return Add(toAny ? TypeMapKind.ArrayToAny : TypeMapKind.Array, typeChunk, offset, sources.Length);
    }
}

internal sealed partial class Checker
{
    /// <summary>The checker's type mappers (module CO6).</summary>
    internal readonly MapperStore Mappers = new();

    // ---- mapper handles in records (signature records and symbol links hold the id) ----

    /// <summary>The handle of a mapper as stored in a record (0 for none).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static int MapperHandle(TypeMapper mapper) => mapper.Id;

    /// <summary>The mapper of a stored handle (none for 0).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TypeMapper MapperAt(int handle) => new(handle);

    // ---- mapper construction ----

    internal TypeMapper MakeUnaryTypeMapper(Type source, Type target) => Mappers.Add(TypeMapKind.Simple, source.Id, target.Id);

    internal TypeMapper MakeArrayTypeMapper(ReadOnlySpan<Type> sources, Type[]? targets) => Mappers.AddArray(sources, targets, targets is null);

    internal TypeMapper MakeFunctionTypeMapper(Func<Type, Type> func) => Mappers.Add(TypeMapKind.Function, Mappers.AddObject(func), 0);

    internal TypeMapper MakeDeferredTypeMapper(Type[] sources, Func<Type>[] targets) =>
        Mappers.Add(TypeMapKind.Deferred, Mappers.AddObject(new DeferredTargets(sources, targets)), 0);

    /// <summary>checker.ts createTypeMapper (targets null: map to any).</summary>
    internal TypeMapper CreateTypeMapper(ReadOnlySpan<Type> sources, Type[]? targets) =>
        sources.Length == 1 ? MakeUnaryTypeMapper(sources[0], targets is not null ? targets[0] : default) : MakeArrayTypeMapper(sources, targets);

    // (createTypeMapper's unary case maps to anyType when targets is undefined; the mapper is created with None and
    // getMappedType substitutes anyType, see GetMappedType)

    internal TypeMapper CombineTypeMappers(TypeMapper mapper1, TypeMapper mapper2) =>
        !mapper1.IsNone ? Mappers.Add(TypeMapKind.Composite, mapper1.Id, mapper2.Id) : mapper2;

    internal TypeMapper MergeTypeMappers(TypeMapper mapper1, TypeMapper mapper2) =>
        !mapper1.IsNone ? Mappers.Add(TypeMapKind.Merged, mapper1.Id, mapper2.Id) : mapper2;

    internal TypeMapper PrependTypeMapping(Type source, Type target, TypeMapper mapper) =>
        mapper.IsNone ? MakeUnaryTypeMapper(source, target) : Mappers.Add(TypeMapKind.Merged, MakeUnaryTypeMapper(source, target).Id, mapper.Id);

    internal TypeMapper AppendTypeMapping(TypeMapper mapper, Type source, Type target) =>
        mapper.IsNone ? MakeUnaryTypeMapper(source, target) : Mappers.Add(TypeMapKind.Merged, mapper.Id, MakeUnaryTypeMapper(source, target).Id);

    internal TypeMapper CreateTypeEraser(ReadOnlySpan<Type> sources) => CreateTypeMapper(sources, null);

    /// <summary>checker.ts getMappedType.</summary>
    internal Type GetMappedType(Type type, TypeMapper mapper)
    {
        ref var record = ref Mappers[mapper];
        switch (record.Kind)
        {
            case TypeMapKind.Simple:
                return type.Id == record.A ? (record.B == 0 ? anyType : new Type(record.B)) : type;
            case TypeMapKind.Array:
            case TypeMapKind.ArrayToAny:
            {
                var types = Mappers.TypeChunk(record.A);
                var offset = record.B;
                var count = record.C;
                for (var i = 0; i < count; i++)
                {
                    if (types[offset + i] == type)
                    {
                        return record.Kind == TypeMapKind.Array ? types[offset + count + i] : anyType;
                    }
                }
                return type;
            }
            case TypeMapKind.Fixing:
            case TypeMapKind.NonFixing:
            {
                var context = (InferenceContext)Mappers.ObjectAt(record.A);
                var fixing = record.Kind == TypeMapKind.Fixing;
                var inferences = context.Inferences;
                for (var i = 0; i < inferences.Count; i++)
                {
                    if (inferences[i].TypeParameter == type)
                    {
                        return fixing ? GetFixedInferredType(context, i) : GetInferredType(context, i);
                    }
                }
                return type;
            }
            case TypeMapKind.Deferred:
            {
                var deferred = (DeferredTargets)Mappers.ObjectAt(record.A);
                var sources = deferred.Sources;
                for (var i = 0; i < sources.Length; i++)
                {
                    if (type == sources[i])
                    {
                        return deferred.Targets[i]();
                    }
                }
                return type;
            }
            case TypeMapKind.Function:
                return ((Func<Type, Type>)Mappers.ObjectAt(record.A))(type);
            case TypeMapKind.Composite:
            case TypeMapKind.Merged:
            {
                var merged = record.Kind == TypeMapKind.Merged;
                var mapper2 = new TypeMapper(record.B);
                var t1 = GetMappedType(type, new TypeMapper(record.A));
                return t1 != type && !merged ? InstantiateType(t1, mapper2) : GetMappedType(t1, mapper2);
            }
        }
        return type;
    }

    private TypeMapper permissiveMapperCache;
    private TypeMapper restrictiveMapperCache;

    // createTypeChecker: permissiveMapper, restrictiveMapper
    private TypeMapper PermissiveMapper => !permissiveMapperCache.IsNone ? permissiveMapperCache
        : permissiveMapperCache = MakeFunctionTypeMapper(t => (Types.Flags(t) & TypeFlags.TypeParameter) != 0 ? wildcardType : t);

    private TypeMapper RestrictiveMapper => !restrictiveMapperCache.IsNone ? restrictiveMapperCache
        : restrictiveMapperCache = MakeFunctionTypeMapper(t => (Types.Flags(t) & TypeFlags.TypeParameter) != 0 ? GetRestrictiveTypeParameter(t) : t);

    private readonly Dictionary<Type, Type> restrictiveTypeParameters = [];

    // checker.ts getRestrictiveTypeParameter
    private Type GetRestrictiveTypeParameter(Type tp)
    {
        ref var data = ref Types.TypeParameter(tp);
        if ((data.Constraint.IsNone && GetConstraintDeclaration(tp).IsNone) || data.Constraint == noConstraintType)
        {
            return tp;
        }
        if (!restrictiveTypeParameters.TryGetValue(tp, out var result))
        {
            result = Types.CreateTypeParameter(Types.Symbol(tp));
            Types.TypeParameter(result).Constraint = noConstraintType;
            restrictiveTypeParameters[tp] = result;
        }
        return result;
    }

    /// <summary>checker.ts cloneTypeParameter.</summary>
    internal Type CloneTypeParameter(Type typeParameter)
    {
        var result = Types.CreateTypeParameter(Types.Symbol(typeParameter));
        Types.TypeParameter(result).Target = typeParameter;
        return result;
    }

    // ---- instantiation ----

    private int instantiationDepth;
    private int instantiationCount;
    private NodeRef currentNode;
    private readonly List<TypeMapper> activeTypeMappers = [];
    private readonly List<KeyMap<Type>> activeTypeMappersCaches = []; // (module CO2: keys `${type.id}${getAliasId(...)}` as ints)
    private int activeTypeMappersCount;

    private void PushActiveMapper(TypeMapper mapper)
    {
        if (activeTypeMappersCount == activeTypeMappers.Count)
        {
            activeTypeMappers.Add(mapper);
            activeTypeMappersCaches.Add(new KeyMap<Type>());
        }
        else
        {
            activeTypeMappers[activeTypeMappersCount] = mapper;
        }
        activeTypeMappersCount++;
    }

    private void PopActiveMapper()
    {
        activeTypeMappersCount--;
        activeTypeMappers[activeTypeMappersCount] = default;
        // (Go clears the map and keeps it for reuse; a table one large instantiation grew is replaced instead: tsrs
        // checker_11.rs pop_active_mapper, Track O)
        activeTypeMappersCaches[activeTypeMappersCount].ClearScratch();
    }

    private int FindActiveMapper(TypeMapper mapper)
    {
        for (var i = activeTypeMappersCount - 1; i >= 0; i--)
        {
            if (mapper == activeTypeMappers[i])
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>checker.ts clearActiveMapperCaches.</summary>
    internal void ClearActiveMapperCaches()
    {
        for (var i = activeTypeMappersCount - 1; i >= 0; i--)
        {
            activeTypeMappersCaches[i].Clear();
        }
    }

    /// <summary>checker.ts instantiateType (None stays None; a null mapper returns the type).</summary>
    internal Type InstantiateType(Type type, TypeMapper mapper) =>
        !type.IsNone && !mapper.IsNone ? InstantiateTypeWithAlias(type, mapper, SymbolRef.None, default) : type;

    /// <summary>checker.ts instantiateTypeWithAlias.</summary>
    internal Type InstantiateTypeWithAlias(Type type, TypeMapper mapper, SymbolRef aliasSymbol, TypeList aliasTypeArguments)
    {        if (!CouldContainTypeVariables(type))
        {
            return type;
        }
        if (instantiationDepth == 100 || instantiationCount >= 5000000)
        {
            // We have reached 100 recursive type instantiations, or 5M type instantiations caused by the same statement
            // or expression. There is a very high likelyhood we're dealing with a combination of infinite generic types
            // that perpetually generate new type identities, so we stop the recursion here by yielding the error type.
            Error(currentNode, Diagnostics.Type_instantiation_is_excessively_deep_and_possibly_infinite);
            totalInstantiationCount++; // (module RS2: the flow memo keeps no frame that reached this)
            return errorType;
        }
        var index = FindActiveMapper(mapper);
        if (index == -1)
        {
            PushActiveMapper(mapper);
        }
        var mark = keyScratch.Mark();
        keyScratch.Add(type.Id);
        AddAliasKey(aliasSymbol, aliasTypeArguments);
        var key = keyScratch.Since(mark);
        var mapperCache = activeTypeMappersCaches[index != -1 ? index : activeTypeMappersCount - 1];
        if (mapperCache.TryGetValue(key, out var cached))
        {
            keyScratch.Release(mark);
            return cached;
        }
        instantiationCount++;
        totalInstantiationCount++; // (module RS2, the flow memo)
        instantiationDepth++;
        var result = InstantiateTypeWorker(type, mapper, aliasSymbol, aliasTypeArguments);
        if (index == -1)
        {
            PopActiveMapper();
        }
        else
        {
            mapperCache.Set(key, result);
        }
        keyScratch.Release(mark);
        instantiationDepth--;
        return result;
    }

    private Type InstantiateTypeWorker(Type type, TypeMapper mapper, SymbolRef aliasSymbol, TypeList aliasTypeArguments)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.TypeParameter) != 0)
        {
            return GetMappedType(type, mapper);
        }
        if ((flags & TypeFlags.Object) != 0)
        {
            var objectFlags = Types.ObjectFlagsOf(type);
            if ((objectFlags & (ObjectFlags.Reference | ObjectFlags.Anonymous | ObjectFlags.Mapped)) != 0)
            {
                if ((objectFlags & ObjectFlags.Reference) != 0 && Types.Object(type).Node.IsNone)
                {
                    var resolvedTypeArguments = Types.Object(type).ResolvedTypeArguments;
                    var newTypeArguments = InstantiateTypesChanged(Types.Elements(resolvedTypeArguments), mapper);
                    return newTypeArguments is not null ? CreateNormalizedTypeReference(Types.Object(type).Target, newTypeArguments) : type;
                }
                if ((objectFlags & ObjectFlags.ReverseMapped) != 0)
                {
                    return InstantiateReverseMappedType(type, mapper);
                }
                return GetObjectTypeInstantiation(type, mapper, aliasSymbol, aliasTypeArguments);
            }
            return type;
        }
        if ((flags & TypeFlags.UnionOrIntersection) != 0)
        {
            var origin = (flags & TypeFlags.Union) != 0 ? Types.Union(type).Origin : Type.None;
            var types = !origin.IsNone && (Types.Flags(origin) & TypeFlags.UnionOrIntersection) != 0 ? Types.Union(origin).Types : Types.Union(type).Types;
            // (tsrs checker_11.rs instantiate_type_worker: the stored members, a pooled buffer only when one changed)
            var changedTypes = InstantiateTypesChangedPooled(Types.Elements(types), mapper);
            ref var record = ref Types.Record(type);
            if (changedTypes is null && aliasSymbol == record.AliasSymbol)
            {
                return type;
            }
            var newAliasSymbol = !aliasSymbol.IsNone ? aliasSymbol : Types.Record(type).AliasSymbol;
            var newAliasTypeArguments = !aliasSymbol.IsNone ? aliasTypeArguments : InstantiateTypeList(Types.Record(type).AliasTypeArguments, mapper);
            var newTypes = changedTypes is not null ? System.Runtime.InteropServices.CollectionsMarshal.AsSpan(changedTypes) : Types.Elements(types);
            var result = (flags & TypeFlags.Intersection) != 0 || (!origin.IsNone && (Types.Flags(origin) & TypeFlags.Intersection) != 0)
                ? GetIntersectionType(newTypes, newAliasSymbol, newAliasTypeArguments)
                : GetUnionType(newTypes, UnionReduction.Literal, newAliasSymbol, newAliasTypeArguments);
            if (changedTypes is not null)
            {
                FreeTypeList(changedTypes);
            }
            return result;
        }
        if ((flags & TypeFlags.Index) != 0)
        {
            return GetIndexType(InstantiateType(Types.Data<IndexData>(type).Type, mapper));
        }
        if ((flags & TypeFlags.TemplateLiteral) != 0)
        {
            var data = Types.Data<TemplateLiteralData>(type);
            return GetTemplateLiteralType(data.Texts, InstantiateTypes(data.Types, mapper, out _));
        }
        if ((flags & TypeFlags.StringMapping) != 0)
        {
            return GetStringMappingType(Types.Symbol(type), InstantiateType(Types.Data<StringMappingData>(type).Type, mapper));
        }
        if ((flags & TypeFlags.IndexedAccess) != 0)
        {
            var data = Types.Data<IndexedAccessData>(type);
            var newAliasSymbol = !aliasSymbol.IsNone ? aliasSymbol : Types.Record(type).AliasSymbol;
            var newAliasTypeArguments = !aliasSymbol.IsNone ? aliasTypeArguments : InstantiateTypeList(Types.Record(type).AliasTypeArguments, mapper);
            return GetIndexedAccessType(InstantiateType(data.ObjectType, mapper), InstantiateType(data.IndexType, mapper), data.AccessFlags, NodeRef.None, newAliasSymbol, newAliasTypeArguments);
        }
        if ((flags & TypeFlags.Conditional) != 0)
        {
            var data = Types.Data<ConditionalData>(type);
            return GetConditionalTypeInstantiation(type, CombineTypeMappers(data.Mapper, mapper), forConstraint: false, aliasSymbol, aliasTypeArguments);
        }
        if ((flags & TypeFlags.Substitution) != 0)
        {
            var data = Types.Data<SubstitutionData>(type);
            var newBaseType = InstantiateType(data.BaseType, mapper);
            if (IsNoInferType(type))
            {
                return GetNoInferType(newBaseType);
            }
            var newConstraint = InstantiateType(data.Constraint, mapper);
            // A substitution type originates in the true branch of a conditional type and can be resolved
            // to just the base type in the same cases as the conditional type resolves to its true branch
            // (because the base type is then known to satisfy the constraint).
            if ((Types.Flags(newBaseType) & TypeFlags.TypeVariable) != 0 && IsGenericType(newConstraint))
            {
                return GetSubstitutionType(newBaseType, newConstraint);
            }
            if ((Types.Flags(newConstraint) & TypeFlags.AnyOrUnknown) != 0 || IsTypeAssignableTo(GetRestrictiveInstantiation(newBaseType), GetRestrictiveInstantiation(newConstraint)))
            {
                return newBaseType;
            }
            return (Types.Flags(newBaseType) & TypeFlags.TypeVariable) != 0 ? GetSubstitutionType(newBaseType, newConstraint) : GetIntersectionType([newConstraint, newBaseType]);
        }
        return type;
    }

    /// <summary>checker.ts instantiateTypes (changed: whether some element changed; the input array otherwise).</summary>
    internal Type[] InstantiateTypes(Type[] types, TypeMapper mapper, out bool changed)
    {
        var result = InstantiateTypesChanged(types, mapper);
        changed = result is not null;
        return result ?? types;
    }

    /// <summary>
    /// instantiateTypes over a stored list, for callers that only read the result (Track O; tsrs checker_11.rs
    /// <c>instantiate_types_changed</c>, notes/cpu-checker.md changes 7 and 8: the stored slice instead of a copy).
    /// Null when no element changed (where TypeScript's instantiateList returns its input), else a new array. Instantiates
    /// the same elements in the same order. The span may be one of the type store's lists: the arena is append-only, so
    /// the elements read stay what they were while instantiation creates new lists.
    /// </summary>
    internal Type[]? InstantiateTypesChanged(ReadOnlySpan<Type> types, TypeMapper mapper)
    {
        for (var i = 0; i < types.Length; i++)
        {
            var item = types[i];
            var mapped = InstantiateType(item, mapper);
            if (item != mapped)
            {
                var result = new Type[types.Length];
                types[..i].CopyTo(result);
                result[i] = mapped;
                for (var j = i + 1; j < types.Length; j++)
                {
                    result[j] = InstantiateType(types[j], mapper);
                }
                return result;
            }
        }
        return null;
    }

    /// <summary>
    /// <see cref="InstantiateTypesChanged"/> into a pooled buffer (tsrs checker.rs <c>free_type_lists</c>, Track O):
    /// null when no element changed, else a buffer to give back with <see cref="FreeTypeList"/> once the result is read.
    /// </summary>
    private List<Type>? InstantiateTypesChangedPooled(ReadOnlySpan<Type> types, TypeMapper mapper)
    {
        for (var i = 0; i < types.Length; i++)
        {
            var item = types[i];
            var mapped = InstantiateType(item, mapper);
            if (item != mapped)
            {
                var result = freeTypeLists.Count > 0 ? freeTypeLists.Pop() : new List<Type>(types.Length);
                result.AddRange(types[..i]);
                result.Add(mapped);
                for (var j = i + 1; j < types.Length; j++)
                {
                    result.Add(InstantiateType(types[j], mapper));
                }
                return result;
            }
        }
        return null;
    }

    // tsrs checker.rs free_type_lists: empty buffers for InstantiateTypesChangedPooled (Track O)
    private readonly Stack<List<Type>> freeTypeLists = new();

    // tsrs checker_11.rs free_type_list
    private void FreeTypeList(List<Type> list)
    {
        if (list.Capacity <= 256)
        {
            list.Clear();
            freeTypeLists.Push(list);
        }
    }

    /// <summary>instantiateTypes for a stored type list (the same list when nothing changed).</summary>
    internal TypeList InstantiateTypeList(TypeList list, TypeMapper mapper)
    {
        if (list.Count == 0 || mapper.IsNone)
        {
            return list;
        }
        var result = InstantiateTypesChangedPooled(Types.Elements(list), mapper);
        if (result is null)
        {
            return list;
        }
        var created = Types.CreateList(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(result));
        FreeTypeList(result);
        return created;
    }

    /// <summary>checker.ts instantiateSignatures.</summary>
    internal Items<Signature> InstantiateSignatures(Items<Signature> signatures, TypeMapper mapper)
    {
        Signature[]? result = null;
        for (var i = 0; i < signatures.Count; i++)
        {
            var mapped = InstantiateSignature(signatures[i], mapper);
            if (mapped != signatures[i])
            {
                result ??= signatures.ToArray();
                result[i] = mapped;
            }
        }
        return result is null ? signatures : new Items<Signature>(result);
    }

    /// <summary>checker.ts instantiateIndexInfos.</summary>
    internal Items<IndexInfo> InstantiateIndexInfos(Items<IndexInfo> infos, TypeMapper mapper)
    {
        IndexInfo[]? result = null;
        for (var i = 0; i < infos.Count; i++)
        {
            var mapped = InstantiateIndexInfo(infos[i], mapper);
            if (mapped != infos[i])
            {
                result ??= infos.ToArray();
                result[i] = mapped;
            }
        }
        return result is null ? infos : new Items<IndexInfo>(result);
    }

    /// <summary>checker.ts instantiateIndexInfo.</summary>
    internal IndexInfo InstantiateIndexInfo(IndexInfo info, TypeMapper mapper) =>
        new() { KeyType = info.KeyType, Type = InstantiateType(info.Type, mapper), IsReadonly = info.IsReadonly, Declaration = info.Declaration, Components = info.Components };

    /// <summary>checker.ts instantiateSignature.</summary>
    internal Signature InstantiateSignature(Signature signature, TypeMapper mapper, bool eraseTypeParameters = false)
    {
        var source = Signatures.Record(signature);
        var freshTypeParameters = TypeList.Empty;
        if (source.TypeParameters.Count > 0 && !eraseTypeParameters)
        {
            // First create a fresh set of type parameters, then include a mapping from the old to the
            // new type parameters in the mapper function. Finally store this mapper in the new type
            // parameters such that we can use it when instantiating constraints.
            var typeParameters = Types.Elements(source.TypeParameters).ToArray();
            var fresh = new Type[typeParameters.Length];
            for (var i = 0; i < fresh.Length; i++)
            {
                fresh[i] = CloneTypeParameter(typeParameters[i]);
            }
            mapper = CombineTypeMappers(CreateTypeMapper(typeParameters, fresh), mapper);
            foreach (var tp in fresh)
            {
                Types.TypeParameter(tp).Mapper = mapper;
            }
            freshTypeParameters = Types.CreateList(fresh);
        }
        // Don't compute resolvedReturnType and resolvedTypePredicate now,
        // because using `mapper` now could trigger inferences to become fixed. (See `createInferenceContext`.)
        // See GH#17600.
        var thisParameter = source.ThisParameter.IsNone ? SymbolRef.None : InstantiateSymbol(source.ThisParameter, mapper);
        var parameters = source.Parameters ?? [];
        var newParameters = new SymbolRef[parameters.Length];
        var changed = false;
        for (var i = 0; i < parameters.Length; i++)
        {
            newParameters[i] = InstantiateSymbol(parameters[i], mapper);
            changed |= newParameters[i] != parameters[i];
        }
        var result = Signatures.Create(source.Flags & SignatureFlags.PropagatingFlags, source.Declaration, freshTypeParameters, thisParameter, source.MinArgumentCount);
        ref var record = ref Signatures.Record(result);
        record.Parameters = changed ? newParameters : parameters;
        record.SyntheticFunctionTypeDeclaration = source.SyntheticFunctionTypeDeclaration;
        record.Target = signature;
        record.Mapper = MapperHandle(mapper);
        return result;
    }

    /// <summary>checker.ts instantiateSymbol.</summary>
    internal SymbolRef InstantiateSymbol(SymbolRef symbol, TypeMapper mapper)
    {
        // If the type of the symbol is already resolved, and if that type could not possibly
        // be affected by instantiation, simply return the symbol itself.
        if (ValueSymbolLinks.Has(symbol))
        {
            var links = ValueSymbolLinks.Get(symbol);
            if (!links.ResolvedType.IsNone && !CouldContainTypeVariables(links.ResolvedType))
            {
                if ((SymbolFlagsOf(symbol) & SymbolFlags.SetAccessor) == 0)
                {
                    return symbol;
                }
                // If we're a setter, check writeType.
                if (!links.WriteType.IsNone && !CouldContainTypeVariables(links.WriteType))
                {
                    return symbol;
                }
            }
        }
        var checkFlags = GetCheckFlags(symbol);
        if ((checkFlags & CheckFlags.Instantiated) != 0)
        {
            // If symbol being instantiated is itself a instantiation, fetch the original target and combine the
            // type mappers. This ensures that original type identities are properly preserved and that aliases
            // always reference a non-aliases.
            var links = ValueSymbolLinks.Get(symbol);
            symbol = links.Target;
            mapper = CombineTypeMappers(MapperAt(links.Mapper), mapper);
            checkFlags = GetCheckFlags(symbol);
        }
        // Keep the flags from the symbol we're instantiating.  Mark that is instantiated, and
        // also transient so that we can just store data on it directly.
        var result = CreateSymbol(SymbolFlagsOf(symbol), NameIdOf(symbol),
            CheckFlags.Instantiated | (checkFlags & (CheckFlags.Readonly | CheckFlags.Late | CheckFlags.OptionalParameter | CheckFlags.RestParameter)));
        ref var record = ref TransientSymbols.Record(result);
        record.Declarations = DeclRefOf(symbol);
        record.Parent = ParentOf(symbol);
        record.ValueDeclaration = ValueDeclarationOf(symbol);
        var nameType = ValueSymbolLinks.Has(symbol) ? ValueSymbolLinks.Get(symbol).NameType : Type.None;
        ref var resultLinks = ref ValueSymbolLinks.Get(result);
        resultLinks.Target = symbol;
        resultLinks.Mapper = MapperHandle(mapper);
        resultLinks.NameType = nameType;
        return result;
    }

    /// <summary>symbol.links.checkFlags (getCheckFlags).</summary>
    internal CheckFlags GetCheckFlags(SymbolRef symbol) => symbol.IsTransient ? TransientSymbols.Record(symbol).CheckFlags : CheckFlags.None;

    /// <summary>checker.ts getTypeOfInstantiatedSymbol.</summary>
    private Type GetTypeOfInstantiatedSymbol(SymbolRef symbol)
    {
        var links = ValueSymbolLinks.Get(symbol);
        if (!links.ResolvedType.IsNone)
        {
            return links.ResolvedType;
        }
        var type = InstantiateType(GetTypeOfSymbol(links.Target), MapperAt(links.Mapper));
        ref var after = ref ValueSymbolLinks.Get(symbol);
        if (after.ResolvedType.IsNone)
        {
            after.ResolvedType = type;
        }
        return after.ResolvedType;
    }

    /// <summary>checker.ts getWriteTypeOfInstantiatedSymbol.</summary>
    private Type GetWriteTypeOfInstantiatedSymbol(SymbolRef symbol)
    {
        var links = ValueSymbolLinks.Get(symbol);
        if (!links.WriteType.IsNone)
        {
            return links.WriteType;
        }
        var type = InstantiateType(GetWriteTypeOfSymbol(links.Target), MapperAt(links.Mapper));
        ValueSymbolLinks.Get(symbol).WriteType = type;
        return type;
    }

    // checker.ts getWriteTypeOfSymbol (instantiated accessors; getWriteTypeOfAccessors: the setter's annotation, the
    // rest of it: C8)
    private Type GetWriteTypeOfSymbol(SymbolRef symbol)
    {
        var checkFlags = GetCheckFlags(symbol);
        if ((checkFlags & CheckFlags.SyntheticProperty) != 0)
        {
            // synthetic union and intersection properties (module C4)
            if ((checkFlags & CheckFlags.DeferredType) != 0)
            {
                var deferredWriteType = GetWriteTypeOfSymbolWithDeferredType(symbol);
                return deferredWriteType.IsNone ? GetTypeOfSymbolWithDeferredType(symbol) : deferredWriteType;
            }
            var links = ValueSymbolLinks.Get(symbol);
            return links.WriteType.IsNone ? links.ResolvedType : links.WriteType;
        }
        if ((SymbolFlagsOf(symbol) & SymbolFlags.Property) != 0)
        {
            return RemoveMissingType(GetTypeOfSymbol(symbol), (SymbolFlagsOf(symbol) & SymbolFlags.Optional) != 0);
        }
        if ((SymbolFlagsOf(symbol) & SymbolFlags.Accessor) != 0)
        {
            if ((GetCheckFlags(symbol) & CheckFlags.Instantiated) != 0)
            {
                return GetWriteTypeOfInstantiatedSymbol(symbol);
            }
            return GetWriteTypeOfAccessors(symbol);
        }
        return GetTypeOfSymbol(symbol);
    }

    private const int WriteTypeProperty = 7;

    // checker.ts getWriteTypeOfAccessors
    private Type GetWriteTypeOfAccessors(SymbolRef symbol)
    {
        if (ValueSymbolLinks.Get(symbol).WriteType.IsNone)
        {
            if (!PushTypeResolution(symbol, WriteTypeProperty))
            {
                return errorType;
            }
            var setter = GetDeclarationOfKind(symbol, SyntaxKind.SetAccessor);
            if (setter.IsNone)
            {
                var property = GetDeclarationOfKind(symbol, SyntaxKind.PropertyDeclaration);
                setter = !property.IsNone && Utilities.IsAutoAccessorPropertyDeclaration(StoreOf(property), property.Node) ? property : NodeRef.None;
            }
            var writeType = GetAnnotatedAccessorType(setter);
            if (!PopTypeResolution())
            {
                if (!GetAnnotatedAccessorTypeNode(setter).IsNone)
                {
                    Error(setter, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, SymbolToString(symbol));
                }
                writeType = errorType;
            }
            // Absent an explicit setter type annotation we use the read type of the accessor.
            if (ValueSymbolLinks.Get(symbol).WriteType.IsNone)
            {
                var type = !writeType.IsNone ? writeType : GetTypeOfAccessors(symbol);
                ValueSymbolLinks.Get(symbol).WriteType = type;
            }
        }
        return ValueSymbolLinks.Get(symbol).WriteType;
    }

    private readonly Dictionary<NodeRef, Type[]> outerTypeParametersOfDeclarations = [];

    /// <summary>checker.ts getObjectTypeInstantiation.</summary>
    private Type GetObjectTypeInstantiation(Type type, TypeMapper mapper, SymbolRef aliasSymbol, TypeList aliasTypeArguments)
    {
        var typeData = Types.Object(type);
        var objectFlags = Types.ObjectFlagsOf(type);
        var declaration = (objectFlags & ObjectFlags.Reference) != 0 ? typeData.Node
            : (objectFlags & ObjectFlags.InstantiationExpressionType) != 0 ? typeData.Node
            : DeclarationsOf(Types.Symbol(type))!.Value[0];
        var target = (objectFlags & ObjectFlags.Reference) != 0 ? GetCachedNodeType(declaration)
            : (objectFlags & ObjectFlags.Instantiated) != 0 ? typeData.Target
            : type;
        if (!outerTypeParametersOfDeclarations.TryGetValue(declaration, out var typeParameters))
        {
            // The first time an anonymous type is instantiated we compute and store a list of the type
            // parameters that are in scope (and therefore potentially referenced). For type literals that
            // aren't the right hand side of a generic type alias declaration we optimize by reducing the
            // set of type parameters to those that are possibly referenced in the literal.
            var outerTypeParameters = GetOuterTypeParameters(declaration, includeThisTypes: true);
            // (JavaScript constructor functions' template tag parameters: C9)
            typeParameters = outerTypeParameters is null ? [] : [.. outerTypeParameters];
            var targetFlags = Types.ObjectFlagsOf(target);
            var targetSymbol = Types.Symbol(target);
            if (((targetFlags & (ObjectFlags.Reference | ObjectFlags.InstantiationExpressionType)) != 0 ||
                (!targetSymbol.IsNone && (SymbolFlagsOf(targetSymbol) & (SymbolFlags.Method | SymbolFlags.TypeLiteral)) != 0)) &&
                !Types.Record(target).AliasTypeArguments.IsPresent)
            {
                var allDeclarations = (objectFlags & (ObjectFlags.Reference | ObjectFlags.InstantiationExpressionType)) != 0 ? [declaration] : DeclarationsOf(Types.Symbol(type))!.Value;
                typeParameters = Array.FindAll(typeParameters, tp => allDeclarations.Exists(d => IsTypeParameterPossiblyReferenced(tp, d)));
            }
            outerTypeParametersOfDeclarations[declaration] = typeParameters;
        }
        if (typeParameters.Length > 0)
        {
            // We are instantiating an anonymous type that has one or more type parameters in scope. Apply the
            // mapper to the type parameters to produce the effective list of type arguments, and compute the
            // instantiation cache key from the type IDs of the type arguments.
            var combinedMapper = CombineTypeMappers(typeData.Mapper, mapper);
            var typeArguments = new Type[typeParameters.Length];
            for (var i = 0; i < typeArguments.Length; i++)
            {
                typeArguments[i] = GetMappedType(typeParameters[i], combinedMapper);
            }
            var newAliasSymbol = !aliasSymbol.IsNone ? aliasSymbol : Types.Record(type).AliasSymbol;
            var newAliasTypeArguments = !aliasSymbol.IsNone ? aliasTypeArguments : InstantiateTypeList(Types.Record(type).AliasTypeArguments, mapper);
            // (the key getTypeListId(typeArguments) + getAliasId(...) in target.instantiations, as ints after target's id)
            var mark = keyScratch.Mark();
            keyScratch.Add(target.Id);
            keyScratch.AddTypes(typeArguments);
            AddAliasKey(newAliasSymbol, newAliasTypeArguments);
            var id = keyScratch.Since(mark);
            if (!Types.Object(target).HasInstantiations)
            {
                Types.Object(target).HasInstantiations = true;
                var initial = keyScratch.Mark();
                keyScratch.Add(target.Id);
                keyScratch.AddTypes(typeParameters);
                AddAliasKey(Types.Record(target).AliasSymbol, Types.Record(target).AliasTypeArguments);
                typeInstantiations.Set(keyScratch.Since(initial), target);
                keyScratch.Release(initial);
            }
            if (!typeInstantiations.TryGetValue(id, out var result))
            {
                var newMapper = CreateTypeMapper(typeParameters, typeArguments);
                var targetFlags = Types.ObjectFlagsOf(target);
                if ((targetFlags & ObjectFlags.SingleSignatureType) != 0)
                {
                    newMapper = CombineTypeMappers(newMapper, mapper);
                }
                result = (targetFlags & ObjectFlags.Reference) != 0 ? CreateDeferredTypeReference(typeData.Target, typeData.Node, newMapper, newAliasSymbol, newAliasTypeArguments)
                    : (targetFlags & ObjectFlags.Mapped) != 0 ? InstantiateMappedType(target, newMapper, newAliasSymbol, newAliasTypeArguments)
                    : InstantiateAnonymousType(target, newMapper, newAliasSymbol, newAliasTypeArguments);
                typeInstantiations.Set(id, result); // Set cached result early in case we recursively invoke instantiation while eagerly computing type variable visibility below
                var resultObjectFlags = Types.ObjectFlagsOf(result);
                if ((Types.Flags(result) & TypeFlags.ObjectFlagsType) != 0 && (resultObjectFlags & ObjectFlags.CouldContainTypeVariablesComputed) == 0)
                {
                    var resultCouldContainTypeVariables = ExistsType(typeArguments, new CouldContainTypeVariablesPredicate(this)); // one of the input type arguments might be or contain the result
                    if ((Types.ObjectFlagsOf(result) & ObjectFlags.CouldContainTypeVariablesComputed) == 0)
                    {
                        // if `result` is one of the object types we tried to make (it may not be, due to how `instantiateMappedType` works), we can carry forward the type variable containment check from the input type arguments
                        if ((resultObjectFlags & (ObjectFlags.Mapped | ObjectFlags.Anonymous | ObjectFlags.Reference)) != 0)
                        {
                            Types.Record(result).ObjectFlags |= ObjectFlags.CouldContainTypeVariablesComputed | (resultCouldContainTypeVariables ? ObjectFlags.CouldContainTypeVariables : 0);
                        }
                        // If none of the type arguments for the outer type parameters contain type variables, it follows
                        // that the instantiated type doesn't reference type variables.
                        else
                        {
                            Types.Record(result).ObjectFlags |= !resultCouldContainTypeVariables ? ObjectFlags.CouldContainTypeVariablesComputed : 0;
                        }
                    }
                }
            }
            keyScratch.Release(mark);
            return result;
        }
        return type;
    }

    // checker.ts maybeTypeParameterReference
    private static bool MaybeTypeParameterReference(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        return !((store.Kind(parent) == SyntaxKind.TypeReference && !store.TypeArguments(parent).IsNone && node == store.TypeName(parent)) ||
            (store.Kind(parent) == SyntaxKind.ImportType && !store.TypeArguments(parent).IsNone && node == store.Qualifier(parent)));
    }

    /// <summary>checker.ts isTypeParameterPossiblyReferenced.</summary>
    private bool IsTypeParameterPossiblyReferenced(Type tp, NodeRef node)
    {
        // If the type parameter doesn't have exactly one declaration, if there are intervening statement blocks
        // between the node and the type parameter declaration, if the node contains actual references to the
        // type parameter, or if the node contains type queries that we can't prove couldn't contain references to the type parameter,
        // we consider the type parameter possibly referenced.
        var symbol = Types.Symbol(tp);
        var declarations = symbol.IsNone ? null : DeclarationsOf(symbol);
        if (declarations is not { Count: 1 } tpDeclarations)
        {
            return true;
        }
        var store = StoreOf(node);
        var tpIsThisType = Types.TypeParameter(tp).IsThisType;
        var tpDeclaration = tpDeclarations[0];
        var container = Parent(tpDeclaration);
        for (var n = node; n != container; n = Parent(n))
        {
            if (n.IsNone || Kind(n) == SyntaxKind.Block || (Kind(n) == SyntaxKind.ConditionalType && ForEachChildRef(At(n, StoreOf(n).ExtendsType(n.Node)), ContainsReference)))
            {
                return true;
            }
        }
        return ContainsReference(node);

        bool ContainsReference(NodeRef current)
        {
            var s = StoreOf(current);
            var c = current.Node;
            switch (s.Kind(c))
            {
                case SyntaxKind.ThisType:
                    return tpIsThisType;
                case SyntaxKind.Identifier:
                    return !tpIsThisType && Utilities.IsPartOfTypeNode(s, c) && MaybeTypeParameterReference(s, c) && GetTypeFromTypeNodeWorker(current) == tp; // use worker because we're looking for === equality
                case SyntaxKind.TypeQuery:
                {
                    var entityName = s.ExprName(c);
                    var firstIdentifier = Utilities.GetFirstIdentifier(s, entityName);
                    if (!Utilities.IsThisIdentifier(s, firstIdentifier))
                    {
                        // Don't attempt to analyze typeof this.xxx
                        var firstIdentifierSymbol = GetResolvedSymbol(At(current, firstIdentifier));
                        var tpScope = Kind(tpDeclaration) == SyntaxKind.TypeParameter ? Parent(tpDeclaration) // Type parameter is a regular type parameter, e.g. foo<T>
                            : tpIsThisType ? tpDeclaration // Type parameter is the this type, and its declaration is the class declaration.
                            : NodeRef.None; // Type parameter's declaration was unrecognized, e.g. comes from JSDoc annotation.
                        var idDeclarations = DeclarationsOf(firstIdentifierSymbol);
                        if (idDeclarations is not null && !tpScope.IsNone)
                        {
                            if (idDeclarations.Exists(d => d.File == tpScope.File && IsNodeDescendantOf(StoreOf(d), d.Node, tpScope.Node)))
                            {
                                return true;
                            }
                            var typeArguments = s.TypeArguments(c);
                            if (!typeArguments.IsNone)
                            {
                                foreach (var argument in s.Nodes(typeArguments))
                                {
                                    if (ContainsReference(At(current, argument)))
                                    {
                                        return true;
                                    }
                                }
                            }
                            return false;
                        }
                    }
                    return true;
                }
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                {
                    if (s.Type(c).IsNone && !s.Body(c).IsNone)
                    {
                        return true;
                    }
                    var typeParameters = s.TypeParameters(c);
                    if (!typeParameters.IsNone)
                    {
                        foreach (var p in s.Nodes(typeParameters))
                        {
                            if (ContainsReference(At(current, p)))
                            {
                                return true;
                            }
                        }
                    }
                    var parameters = s.Parameters(c);
                    if (!parameters.IsNone)
                    {
                        foreach (var p in s.Nodes(parameters))
                        {
                            if (ContainsReference(At(current, p)))
                            {
                                return true;
                            }
                        }
                    }
                    return !s.Type(c).IsNone && ContainsReference(At(current, s.Type(c)));
                }
            }
            return ForEachChildRef(current, ContainsReference);
        }
    }

    // forEachChild(node, callback) returning whether the callback returned true for some child.
    private bool ForEachChildRef(NodeRef node, Func<NodeRef, bool> callback)
    {
        if (node.IsNone)
        {
            return false;
        }
        var store = StoreOf(node);
        return NodeVisitor.ForEachChild<object>(store, node.Node, child => callback(At(node, child)) ? (object)true : null) is not null;
    }

    /// <summary>checker.ts instantiateAnonymousType.</summary>
    private Type InstantiateAnonymousType(Type type, TypeMapper mapper, SymbolRef aliasSymbol, TypeList aliasTypeArguments)
    {
        var objectFlags = Types.ObjectFlagsOf(type);
        var result = CreateObjectType((objectFlags & ~(ObjectFlags.CouldContainTypeVariablesComputed | ObjectFlags.CouldContainTypeVariables)) | ObjectFlags.Instantiated, Types.Symbol(type));
        var resultData = Types.Object(result);
        var typeData = Types.Object(type);
        if ((objectFlags & ObjectFlags.Mapped) != 0)
        {
            resultData.Declaration = typeData.Declaration;
            // C.f. instantiateSignature
            var origTypeParameter = GetTypeParameterFromMappedType(type);
            var freshTypeParameter = CloneTypeParameter(origTypeParameter);
            resultData.MappedTypeParameter = freshTypeParameter;
            mapper = CombineTypeMappers(MakeUnaryTypeMapper(origTypeParameter, freshTypeParameter), mapper);
            Types.TypeParameter(freshTypeParameter).Mapper = mapper;
        }
        if ((objectFlags & ObjectFlags.InstantiationExpressionType) != 0)
        {
            resultData.Node = typeData.Node;
        }
        resultData.Target = type;
        resultData.Mapper = mapper;
        Types.Record(result).AliasSymbol = !aliasSymbol.IsNone ? aliasSymbol : Types.Record(type).AliasSymbol;
        // (computed before the record is written: the instantiation can grow the type store)
        var resultAliasTypeArguments = !aliasSymbol.IsNone ? aliasTypeArguments : InstantiateTypeList(Types.Record(type).AliasTypeArguments, mapper);
        Types.Record(result).AliasTypeArguments = resultAliasTypeArguments;
        var aliasArguments = Types.Record(result).AliasTypeArguments;
        Types.Record(result).ObjectFlags |= aliasArguments.Count > 0 ? GetPropagatingFlagsOfTypes(Types.Elements(aliasArguments)) : 0;
        return result;
    }

    private readonly Dictionary<Type, Type> permissiveInstantiations = [];
    private readonly Dictionary<Type, Type> restrictiveInstantiations = [];

    /// <summary>checker.ts getPermissiveInstantiation.</summary>
    internal Type GetPermissiveInstantiation(Type type)
    {
        if ((Types.Flags(type) & (TypeFlags.Primitive | TypeFlags.AnyOrUnknown | TypeFlags.Never)) != 0)
        {
            return type;
        }
        if (!permissiveInstantiations.TryGetValue(type, out var result))
        {
            permissiveInstantiations[type] = result = InstantiateType(type, PermissiveMapper);
        }
        return result;
    }

    /// <summary>checker.ts getRestrictiveInstantiation.</summary>
    internal Type GetRestrictiveInstantiation(Type type)
    {
        if ((Types.Flags(type) & (TypeFlags.Primitive | TypeFlags.AnyOrUnknown | TypeFlags.Never)) != 0)
        {
            return type;
        }
        if (restrictiveInstantiations.TryGetValue(type, out var result))
        {
            return result;
        }
        result = InstantiateType(type, RestrictiveMapper);
        restrictiveInstantiations[type] = result;
        // We set the following so we don't attempt to set the restrictive instance of a restrictive instance
        // which is redundant - we'll produce new type identities, but all type params have already been mapped.
        restrictiveInstantiations[result] = result;
        return result;
    }

    /// <summary>Whether the type is its own restrictive instantiation (type.restrictiveInstantiation === type).</summary>
    private bool IsOwnRestrictiveInstantiation(Type type) => restrictiveInstantiations.TryGetValue(type, out var r) && r == type;

    /// <summary>checker.ts couldContainTypeVariables.</summary>
    internal bool CouldContainTypeVariables(Type type)
    {
        var objectFlags = Types.ObjectFlagsOf(type);
        if ((objectFlags & ObjectFlags.CouldContainTypeVariablesComputed) != 0)
        {
            return (objectFlags & ObjectFlags.CouldContainTypeVariables) != 0;
        }
        var flags = Types.Flags(type);
        bool result;
        if ((flags & TypeFlags.Instantiable) != 0)
        {
            result = true;
        }
        else if ((flags & TypeFlags.Object) != 0 && !IsNonGenericTopLevelType(type))
        {
            if ((objectFlags & ObjectFlags.Reference) != 0 && (!Types.Object(type).Node.IsNone || ExistsType(GetTypeArguments(type), new CouldContainTypeVariablesPredicate(this))))
            {
                result = true;
            }
            else
            {
                var symbol = Types.Symbol(type);
                result = ((objectFlags & ObjectFlags.Anonymous) != 0 && !symbol.IsNone &&
                        (SymbolFlagsOf(symbol) & (SymbolFlags.Function | SymbolFlags.Method | SymbolFlags.Class | SymbolFlags.TypeLiteral | SymbolFlags.ObjectLiteral)) != 0 && DeclarationsOf(symbol) is not null) ||
                    (objectFlags & (ObjectFlags.Mapped | ObjectFlags.ReverseMapped | ObjectFlags.ObjectRestType | ObjectFlags.InstantiationExpressionType)) != 0;
            }
        }
        else
        {
            result = (flags & TypeFlags.UnionOrIntersection) != 0 && (flags & TypeFlags.EnumLiteral) == 0 && !IsNonGenericTopLevelType(type) &&
                ExistsType(Types.Elements(Types.Union(type).Types), new CouldContainTypeVariablesPredicate(this));
        }
        if ((flags & TypeFlags.ObjectFlagsType) != 0)
        {
            Types.Record(type).ObjectFlags |= ObjectFlags.CouldContainTypeVariablesComputed | (result ? ObjectFlags.CouldContainTypeVariables : 0);
        }
        return result;
    }

    // couldContainTypeVariables as a struct callback (no delegate per call; Track O)
    private readonly struct CouldContainTypeVariablesPredicate(Checker c) : ITypePredicate
    {
        public bool Invoke(Type t) => c.CouldContainTypeVariables(t);
    }

    private static bool ExistsType(ReadOnlySpan<Type> types, Func<Type, bool> predicate)
    {
        // (the stored list in place: the arena is append-only; Track O)
        foreach (var t in types)
        {
            if (predicate(t))
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts isNonGenericTopLevelType
    private bool IsNonGenericTopLevelType(Type type)
    {
        ref var record = ref Types.Record(type);
        if (!record.AliasSymbol.IsNone && !record.AliasTypeArguments.IsPresent)
        {
            var declaration = GetDeclarationOfKind(record.AliasSymbol, SyntaxKind.TypeAliasDeclaration);
            if (declaration.IsNone)
            {
                return false;
            }
            for (var n = Parent(declaration); !n.IsNone; n = Parent(n))
            {
                var kind = Kind(n);
                if (kind == SyntaxKind.SourceFile)
                {
                    return true;
                }
                if (kind != SyntaxKind.ModuleDeclaration)
                {
                    return false; // findAncestor's "quit"
                }
            }
        }
        return false;
    }

    // ---- type references ----

    /// <summary>checker.ts getTypeWithThisArgument.</summary>
    internal Type GetTypeWithThisArgument(Type type, Type thisArgument = default, bool needApparentType = false)
    {
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.Reference) != 0)
        {
            var target = Types.Object(type).Target;
            var typeArguments = GetTypeArguments(type).ToArray();
            var targetData = Types.Object(target);
            if (targetData.TypeParameters.Count == typeArguments.Length)
            {
                return CreateTypeReference(target, [.. typeArguments, thisArgument.IsNone ? targetData.ThisType : thisArgument]);
            }
            return type;
        }
        if ((Types.Flags(type) & TypeFlags.Intersection) != 0)
        {
            var types = Types.Elements(Types.Union(type).Types).ToArray();
            var mapped = new Type[types.Length];
            var changed = false;
            for (var i = 0; i < types.Length; i++)
            {
                mapped[i] = GetTypeWithThisArgument(types[i], thisArgument, needApparentType);
                changed |= mapped[i] != types[i];
            }
            return changed ? GetIntersectionType(mapped) : type;
        }
        return needApparentType ? GetApparentType(type) : type;
    }

    // checker.ts resolveTypeReferenceMembers
    private void ResolveTypeReferenceMembers(Type type)
    {
        var target = Types.Object(type).Target;
        var source = ResolveDeclaredMembers(target);
        var sourceTypeParameters = Types.Elements(source.TypeParameters).ToArray();
        var typeParameters = new Type[sourceTypeParameters.Length + 1];
        sourceTypeParameters.CopyTo(typeParameters, 0);
        typeParameters[^1] = source.ThisType;
        var typeArguments = GetTypeArguments(type).ToArray();
        var paddedTypeArguments = typeArguments.Length == typeParameters.Length ? typeArguments : [.. typeArguments, type];
        ResolveObjectTypeMembers(type, target, source, typeParameters, paddedTypeArguments);
    }

    // checker.ts resolveObjectTypeMembers
    private void ResolveObjectTypeMembers(Type type, Type sourceType, ObjectTypeData source, Type[] typeParameters, Type[] typeArguments)
    {
        var mapper = TypeMapper.None;
        SymTable members;
        Items<Signature> callSignatures;
        Items<Signature> constructSignatures;
        Items<IndexInfo> indexInfos;
        var sourceSymbol = Types.Symbol(sourceType);
        if (RangeEquals(typeParameters, typeArguments, typeParameters.Length))
        {
            members = !sourceSymbol.IsNone ? GetMembersOfSymbol(sourceSymbol) : CreateSymbolTable(Types.View(source.DeclaredProperties));
            callSignatures = Types.View(source.DeclaredCallSignatures);
            constructSignatures = Types.View(source.DeclaredConstructSignatures);
            indexInfos = Types.View(source.DeclaredIndexInfos);
        }
        else
        {
            mapper = CreateTypeMapper(typeParameters, typeArguments);
            members = CreateInstantiatedSymbolTable(Types.View(source.DeclaredProperties), mapper, mappingThisOnly: typeParameters.Length == 1);
            callSignatures = InstantiateSignatures(Types.View(source.DeclaredCallSignatures), mapper);
            constructSignatures = InstantiateSignatures(Types.View(source.DeclaredConstructSignatures), mapper);
            indexInfos = InstantiateIndexInfos(Types.View(source.DeclaredIndexInfos), mapper);
        }
        var baseTypes = GetBaseTypes(sourceType);
        if (baseTypes.Count > 0)
        {
            if (!sourceSymbol.IsNone && members == GetMembersOfSymbol(sourceSymbol))
            {
                var symbolTable = CreateSymbolTable(Types.View(source.DeclaredProperties));
                // copy index signature symbol as well (for quickinfo)
                var sourceIndex = GetMembersOfSymbol(sourceSymbol).Get(InternalSymbolName.Index);
                if (!sourceIndex.IsNone)
                {
                    symbolTable.Set(InternalSymbolName.Index, sourceIndex);
                }
                members = symbolTable;
            }
            SetStructuredTypeMembers(type, members, callSignatures, constructSignatures, indexInfos);
            var thisArgument = typeArguments.Length > 0 ? typeArguments[^1] : Type.None;
            foreach (var baseType in baseTypes)
            {
                var instantiatedBaseType = !thisArgument.IsNone ? GetTypeWithThisArgument(InstantiateType(baseType, mapper), thisArgument) : baseType;
                AddInheritedMembers(members, GetPropertiesOfType(instantiatedBaseType));
                callSignatures = ItemsBuilder.Concatenate(callSignatures, GetSignaturesOfType(instantiatedBaseType, SignatureKind.Call));
                constructSignatures = ItemsBuilder.Concatenate(constructSignatures, GetSignaturesOfType(instantiatedBaseType, SignatureKind.Construct));
                var inheritedIndexInfos = instantiatedBaseType != anyType ? GetIndexInfosOfType(instantiatedBaseType) : [new IndexInfo { KeyType = stringType, Type = anyType }];
                List<IndexInfo>? added = null;
                foreach (var info in inheritedIndexInfos)
                {
                    if (!indexInfos.Exists(i => i.KeyType == info.KeyType))
                    {
                        (added ??= []).Add(info);
                    }
                }
                if (added is not null)
                {
                    indexInfos = ItemsBuilder.Concatenate(indexInfos, added);
                }
            }
        }
        SetStructuredTypeMembers(type, members, callSignatures, constructSignatures, indexInfos);
    }

    private static bool RangeEquals(Type[] a, Type[] b, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }

    // checker.ts createSymbolTable(symbols)
    private SymTable CreateSymbolTable(Items<SymbolRef> symbols)
    {
        var result = new SymTable(Names, symbols.Count);
        foreach (var s in symbols)
        {
            result.Set(NameIdOf(s), s);
        }
        return result;
    }

    // checker.ts createInstantiatedSymbolTable. The mappingThisOnly flag indicates that the only type parameter being
    // mapped is "this". When the flag is true, we check symbols to see if we can quickly conclude they are free of
    // "this" references, thus needing no instantiation.
    private SymTable CreateInstantiatedSymbolTable(Items<SymbolRef> symbols, TypeMapper mapper, bool mappingThisOnly)
    {
        var result = new SymTable(Names, symbols.Count);
        foreach (var symbol in symbols)
        {
            result.Set(NameIdOf(symbol), mappingThisOnly && IsThisless(symbol) ? symbol : InstantiateSymbol(symbol, mapper));
        }
        return result;
    }

    // checker.ts isThisless
    private bool IsThisless(SymbolRef symbol)
    {
        var declarations = DeclarationsOf(symbol);
        if (declarations is { Count: 1 } single)
        {
            var declaration = single[0];
            var store = StoreOf(declaration);
            switch (store.Kind(declaration.Node))
            {
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.PropertySignature:
                    return IsThislessVariableLikeDeclaration(declaration);
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.Constructor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    return IsThislessFunctionLikeDeclaration(declaration);
            }
        }
        return false;
    }

    // checker.ts isThislessType
    private static bool IsThislessType(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.UnknownKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.BigIntKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.ObjectKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.UndefinedKeyword:
            case SyntaxKind.NeverKeyword:
            case SyntaxKind.LiteralType:
                return true;
            case SyntaxKind.ArrayType:
                return IsThislessType(store, store.ElementType(node));
            case SyntaxKind.TypeReference:
            {
                var typeArguments = store.TypeArguments(node);
                if (typeArguments.IsNone)
                {
                    return true;
                }
                foreach (var argument in store.Nodes(typeArguments))
                {
                    if (!IsThislessType(store, argument))
                    {
                        return false;
                    }
                }
                return true;
            }
        }
        return false;
    }

    // checker.ts isThislessTypeParameter
    private static bool IsThislessTypeParameter(NodeStore store, Node node)
    {
        var constraint = store.Constraint(node);
        return constraint.IsNone || IsThislessType(store, constraint);
    }

    // checker.ts isThislessVariableLikeDeclaration
    private bool IsThislessVariableLikeDeclaration(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var typeNode = GetEffectiveTypeAnnotationNode(declaration);
        return !typeNode.IsNone ? IsThislessType(store, typeNode.Node) : store.Initializer(declaration.Node).IsNone;
    }

    // checker.ts isThislessFunctionLikeDeclaration
    private bool IsThislessFunctionLikeDeclaration(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var node = declaration.Node;
        var returnType = GetEffectiveReturnTypeNode(store, node);
        if (!(store.Kind(node) == SyntaxKind.Constructor || (!returnType.IsNone && IsThislessType(store, returnType))))
        {
            return false;
        }
        var parameters = store.Parameters(node);
        if (!parameters.IsNone)
        {
            foreach (var p in store.Nodes(parameters))
            {
                if (!IsThislessVariableLikeDeclaration(At(declaration, p)))
                {
                    return false;
                }
            }
        }
        foreach (var tp in GetEffectiveTypeParameterDeclarations(store, node))
        {
            if (!IsThislessTypeParameter(store, tp))
            {
                return false;
            }
        }
        return true;
    }

    // ---- type alias instantiation ----

    // getSymbolLinks(symbol).typeParameters / instantiations of generic type aliases
    // (module CO2: the instantiations of all aliases in one map, keyed by the alias symbol and the string's parts)
    private readonly Dictionary<SymbolRef, Type[]> aliasInstantiations = [];
    private readonly KeyMap<Type> aliasInstantiationCache = new();

    /// <summary>The type parameters of a generic type alias (links.typeParameters; null when not generic).</summary>
    internal Type[]? GetTypeAliasTypeParameters(SymbolRef symbol)
    {
        GetDeclaredTypeOfSymbol(symbol);
        return aliasInstantiations.TryGetValue(symbol, out var entry) ? entry : null;
    }

    // called from getDeclaredTypeOfTypeAlias when the resolution did not circle
    private void InitializeTypeAliasInstantiations(SymbolRef symbol, Type type)
    {
        var typeParameters = GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol);
        if (typeParameters is not null)
        {
            // Initialize the instantiation cache for generic type aliases. The declared type corresponds to
            // an instantiation of the type alias with the type parameters supplied as type arguments.
            var array = typeParameters.ToArray();
            aliasInstantiations[symbol] = array;
            var mark = keyScratch.Mark();
            keyScratch.Add(symbol.File);
            keyScratch.Add(symbol.Index);
            keyScratch.AddTypes(array);
            aliasInstantiationCache.Set(keyScratch.Since(mark), type);
            keyScratch.Release(mark);
        }
    }

    /// <summary>checker.ts getTypeAliasInstantiation.</summary>
    internal Type GetTypeAliasInstantiation(SymbolRef symbol, Type[] typeArguments, SymbolRef aliasSymbol = default, TypeList aliasTypeArguments = default)
    {
        var type = GetDeclaredTypeOfSymbol(symbol);
        if (type == intrinsicMarkerType)
        {
            var typeKind = GetIntrinsicTypeKind(NameOf(symbol));
            if (typeKind != IntrinsicTypeKind.None && typeArguments.Length == 1)
            {
                return typeKind == IntrinsicTypeKind.NoInfer ? GetNoInferType(typeArguments[0]) : GetStringMappingType(symbol, typeArguments[0]);
            }
        }
        if (!aliasInstantiations.TryGetValue(symbol, out var typeParameters))
        {
            return type;
        }
        var mark = keyScratch.Mark();
        keyScratch.Add(symbol.File);
        keyScratch.Add(symbol.Index);
        keyScratch.AddTypes(typeArguments);
        AddAliasKey(aliasSymbol, aliasTypeArguments);
        var id = keyScratch.Since(mark);
        if (!aliasInstantiationCache.TryGetValue(id, out var instantiation))
        {
            var valueDeclaration = ValueDeclarationOf(symbol);
            var isJs = !valueDeclaration.IsNone && Utilities.IsInJSFile(StoreOf(valueDeclaration), valueDeclaration.Node);
            instantiation = InstantiateTypeWithAlias(type, CreateTypeMapper(typeParameters, FillMissingTypeArguments(typeArguments, typeParameters, GetMinTypeArgumentCount(typeParameters), isJs)), aliasSymbol, aliasTypeArguments);
            aliasInstantiationCache.Set(id, instantiation);
        }
        keyScratch.Release(mark);
        return instantiation;
    }

    /// <summary>checker.ts intrinsicTypeKinds.</summary>
    internal enum IntrinsicTypeKind
    {
        None,
        Uppercase,
        Lowercase,
        Capitalize,
        Uncapitalize,
        NoInfer,
    }

    internal static IntrinsicTypeKind GetIntrinsicTypeKind(string name) => name switch
    {
        "Uppercase" => IntrinsicTypeKind.Uppercase,
        "Lowercase" => IntrinsicTypeKind.Lowercase,
        "Capitalize" => IntrinsicTypeKind.Capitalize,
        "Uncapitalize" => IntrinsicTypeKind.Uncapitalize,
        "NoInfer" => IntrinsicTypeKind.NoInfer,
        _ => IntrinsicTypeKind.None,
    };

    // checker.ts isLocalTypeAlias
    private bool IsLocalTypeAlias(SymbolRef symbol)
    {
        foreach (var declaration in DeclarationsOf(symbol) ?? [])
        {
            if (Kind(declaration) is SyntaxKind.TypeAliasDeclaration or SyntaxKind.JSDocTypedefTag or SyntaxKind.JSDocCallbackTag or SyntaxKind.JSDocEnumTag)
            {
                return !Utilities.GetContainingFunctionOrNone(StoreOf(declaration), declaration.Node).IsNone;
            }
        }
        return false;
    }

    // ---- type arguments ----

    /// <summary>checker.ts fillMissingTypeArguments.</summary>
    internal Type[] FillMissingTypeArguments(Type[] typeArguments, Type[] typeParameters, int minTypeArgumentCount, bool isJavaScriptImplicitAny)
    {
        var numTypeParameters = typeParameters.Length;
        if (numTypeParameters == 0)
        {
            return [];
        }
        var numTypeArguments = typeArguments.Length;
        if (isJavaScriptImplicitAny || (numTypeArguments >= minTypeArgumentCount && numTypeArguments <= numTypeParameters))
        {
            var result = new Type[numTypeParameters];
            Array.Copy(typeArguments, result, Math.Min(numTypeArguments, numTypeParameters));
            // Map invalid forward references in default types to the error type
            for (var i = numTypeArguments; i < numTypeParameters; i++)
            {
                result[i] = errorType;
            }
            var baseDefaultType = isJavaScriptImplicitAny ? anyType : unknownType;
            for (var i = numTypeArguments; i < numTypeParameters; i++)
            {
                var defaultType = GetDefaultFromTypeParameter(typeParameters[i]);
                if (isJavaScriptImplicitAny && !defaultType.IsNone && (defaultType == unknownType || defaultType == emptyObjectType))
                {
                    defaultType = anyType;
                }
                result[i] = !defaultType.IsNone ? InstantiateType(defaultType, CreateTypeMapper(typeParameters, (Type[])result.Clone())) : baseDefaultType;
            }
            return result;
        }
        return (Type[])typeArguments.Clone();
    }

    private int GetMinTypeArgumentCount(Type[] typeParameters)
    {
        var minTypeArgumentCount = 0;
        for (var i = 0; i < typeParameters.Length; i++)
        {
            if (!HasTypeParameterDefault(typeParameters[i]))
            {
                minTypeArgumentCount = i + 1;
            }
        }
        return minTypeArgumentCount;
    }

    /// <summary>checker.ts getResolvedTypeParameterDefault.</summary>
    private Type GetResolvedTypeParameterDefault(Type typeParameter)
    {
        ref var data = ref Types.TypeParameter(typeParameter);
        if (data.Default.IsNone)
        {
            if (!data.Target.IsNone)
            {
                var targetDefault = GetResolvedTypeParameterDefault(data.Target);
                data.Default = !targetDefault.IsNone ? InstantiateType(targetDefault, data.Mapper) : noConstraintType;
            }
            else
            {
                // To block recursion, set the initial value to the resolvingDefaultType.
                data.Default = resolvingDefaultType;
                var defaultType = noConstraintType;
                foreach (var declaration in DeclarationsOf(Types.Symbol(typeParameter)) ?? [])
                {
                    if (Kind(declaration) == SyntaxKind.TypeParameter)
                    {
                        var defaultNode = StoreOf(declaration).Default(declaration.Node);
                        if (!defaultNode.IsNone)
                        {
                            defaultType = GetTypeFromTypeNode(At(declaration, defaultNode));
                            break;
                        }
                    }
                }
                if (data.Default == resolvingDefaultType)
                {
                    // If we have not been called recursively, set the correct default type.
                    data.Default = defaultType;
                }
            }
        }
        else if (data.Default == resolvingDefaultType)
        {
            // If we are called recursively for this type parameter, mark the default as circular.
            data.Default = circularConstraintType;
        }
        return data.Default;
    }

    /// <summary>checker.ts getDefaultFromTypeParameter (None when there is no default or it is circular).</summary>
    internal Type GetDefaultFromTypeParameter(Type typeParameter)
    {
        var defaultType = GetResolvedTypeParameterDefault(typeParameter);
        return defaultType != noConstraintType && defaultType != circularConstraintType ? defaultType : Type.None;
    }

    // checker.ts getConstraintDeclaration
    private NodeRef GetConstraintDeclaration(Type type)
    {
        var symbol = Types.Symbol(type);
        foreach (var declaration in symbol.IsNone ? [] : DeclarationsOf(symbol) ?? [])
        {
            if (Kind(declaration) == SyntaxKind.TypeParameter)
            {
                var constraint = GetEffectiveConstraintOfTypeParameter(StoreOf(declaration), declaration.Node);
                if (!constraint.IsNone)
                {
                    return At(declaration, constraint);
                }
            }
        }
        return NodeRef.None;
    }

    /// <summary>checker.ts getConstraintFromTypeParameter (the worker; use GetConstraintOfTypeParameter).</summary>
    internal Type GetConstraintFromTypeParameter(Type typeParameter)
    {
        ref var data = ref Types.TypeParameter(typeParameter);
        if (data.Constraint.IsNone)
        {
            if (!data.Target.IsNone)
            {
                var targetConstraint = GetConstraintOfTypeParameter(data.Target);
                data.Constraint = !targetConstraint.IsNone ? InstantiateType(targetConstraint, data.Mapper) : noConstraintType;
            }
            else
            {
                var constraintDeclaration = GetConstraintDeclaration(typeParameter);
                if (constraintDeclaration.IsNone)
                {
                    var inferred = GetInferredTypeParameterConstraint(typeParameter);
                    data.Constraint = inferred.IsNone ? noConstraintType : inferred;
                }
                else
                {
                    var type = GetTypeFromTypeNode(constraintDeclaration);
                    if ((Types.Flags(type) & TypeFlags.Any) != 0 && !IsErrorType(type))
                    {
                        // Allow errorType to propegate to keep downstream errors suppressed
                        // use stringNumberSymbolType as the base constraint for mapped type key constraints (unknown isn;t assignable to that, but `any` was),
                        // use unknown otherwise
                        type = Kind(Parent(Parent(constraintDeclaration))) == SyntaxKind.MappedType ? stringNumberSymbolType : unknownType;
                    }
                    data.Constraint = type;
                }
            }
        }
        return data.Constraint == noConstraintType ? Type.None : data.Constraint;
    }

    /// <summary>checker.ts getConstraintOfTypeParameter.</summary>
    internal Type GetConstraintOfTypeParameter(Type typeParameter) =>
        HasNonCircularBaseConstraint(typeParameter) ? GetConstraintFromTypeParameter(typeParameter) : Type.None;

    // checker.ts getInferredTypeParameterConstraint (the infer type parameters of conditional types)
    private Type GetInferredTypeParameterConstraint(Type typeParameter, bool omitTypeReferences = false)
    {
        List<Type>? inferences = null;
        var symbol = Types.Symbol(typeParameter);
        foreach (var declaration in symbol.IsNone ? [] : DeclarationsOf(symbol) ?? [])
        {
            var parent = Parent(declaration);
            if (Kind(parent) != SyntaxKind.InferType)
            {
                continue;
            }
            // When an 'infer T' declaration is immediately contained in a type reference node
            // (such as 'Foo<infer T>'), T's constraint is inferred from the constraint of the
            // corresponding type parameter in 'Foo'. When multiple 'infer T' declarations are
            // present, we form an intersection of the inferred constraint types.
            var child = parent;
            var grandParent = Parent(parent);
            while (Kind(grandParent) == SyntaxKind.ParenthesizedType)
            {
                child = grandParent;
                grandParent = Parent(grandParent);
            }
            var store = StoreOf(grandParent);
            if (Kind(grandParent) == SyntaxKind.TypeReference && !omitTypeReferences)
            {
                var typeReference = grandParent;
                var typeParameters = GetTypeParametersForTypeReferenceOrImport(typeReference);
                if (typeParameters is not null)
                {
                    var typeArgumentNodes = store.TypeArguments(typeReference.Node);
                    var index = -1;
                    for (var i = 0; i < store.Length(typeArgumentNodes); i++)
                    {
                        if (store.Element(typeArgumentNodes, i) == child.Node)
                        {
                            index = i;
                            break;
                        }
                    }
                    if (index >= 0 && index < typeParameters.Length)
                    {
                        var declaredConstraint = GetConstraintOfTypeParameter(typeParameters[index]);
                        if (!declaredConstraint.IsNone)
                        {
                            // Type parameter constraints can reference other type parameters so
                            // constraints need to be instantiated. If instantiation produces the
                            // type parameter itself, we discard that inference.
                            var targets = new Func<Type>[typeParameters.Length];
                            for (var i = 0; i < targets.Length; i++)
                            {
                                var j = i;
                                targets[i] = () => GetEffectiveTypeArgumentAtIndex(typeReference, typeParameters, j);
                            }
                            var constraint = InstantiateType(declaredConstraint, MakeDeferredTypeMapper(typeParameters, targets));
                            if (constraint != typeParameter)
                            {
                                (inferences ??= []).Add(constraint);
                            }
                        }
                    }
                }
            }
            // When an 'infer T' declaration is immediately contained in a rest parameter declaration, a rest type
            // or a named rest tuple element, we infer an 'unknown[]' constraint.
            else if ((Kind(grandParent) == SyntaxKind.Parameter && !store.DotDotDotToken(grandParent.Node).IsNone) ||
                Kind(grandParent) == SyntaxKind.RestType ||
                (Kind(grandParent) == SyntaxKind.NamedTupleMember && !store.DotDotDotToken(grandParent.Node).IsNone))
            {
                (inferences ??= []).Add(CreateArrayType(unknownType));
            }
            // When an 'infer T' declaration is immediately contained in a string template type, we infer a 'string'
            // constraint.
            else if (Kind(grandParent) == SyntaxKind.TemplateLiteralTypeSpan)
            {
                (inferences ??= []).Add(stringType);
            }
            // When an 'infer T' declaration is in the constraint position of a mapped type, we infer a 'keyof any'
            // constraint.
            else if (Kind(grandParent) == SyntaxKind.TypeParameter && Kind(Parent(grandParent)) == SyntaxKind.MappedType)
            {
                (inferences ??= []).Add(stringNumberSymbolType);
            }
            // When an 'infer T' declaration is the template of a mapped type, and that mapped type is the extends
            // clause of a conditional whose check type is also a mapped type, give it a constraint equal to the template
            // of the check type's mapped type
            else if (Kind(grandParent) == SyntaxKind.MappedType && store.Type(grandParent.Node) == parent.Node &&
                Kind(Parent(grandParent)) == SyntaxKind.ConditionalType && store.ExtendsType(store.Parent(grandParent.Node)) == grandParent.Node &&
                store.Kind(store.CheckType(store.Parent(grandParent.Node))) == SyntaxKind.MappedType &&
                !store.Type(store.CheckType(store.Parent(grandParent.Node))).IsNone)
            {
                var checkMappedType = At(grandParent, store.CheckType(store.Parent(grandParent.Node)));
                var nodeType = GetTypeFromTypeNode(At(grandParent, store.Type(checkMappedType.Node)));
                var mappedTypeParameter = At(grandParent, store.TypeParameter(checkMappedType.Node));
                var mappedConstraint = store.Constraint(mappedTypeParameter.Node);
                inferences ??= [];
                inferences.Add(InstantiateType(nodeType, MakeUnaryTypeMapper(GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(mappedTypeParameter)),
                    !mappedConstraint.IsNone ? GetTypeFromTypeNode(At(grandParent, mappedConstraint)) : stringNumberSymbolType)));
            }
        }
        return inferences is null ? Type.None : GetIntersectionType(inferences.ToArray());
    }

    // checker.ts getTypeParametersForTypeReferenceOrImport (type references)
    private Type[]? GetTypeParametersForTypeReferenceOrImport(NodeRef node)
    {
        var type = GetTypeFromTypeNode(node);
        if (!IsErrorType(type))
        {
            var symbol = SymbolNodeLinks.Has(node) ? SymbolNodeLinks.Get(node).ResolvedSymbol : SymbolRef.None;
            if (!symbol.IsNone && symbol != unknownSymbol)
            {
                var flags = SymbolFlagsOf(symbol);
                if ((flags & SymbolFlags.TypeAlias) != 0)
                {
                    return GetTypeAliasTypeParameters(symbol);
                }
                if ((flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
                {
                    var declared = GetDeclaredTypeOfSymbol(symbol);
                    return (Types.Flags(declared) & TypeFlags.Object) != 0 && Types.Object(declared).LocalTypeParameters.Count > 0
                        ? Types.Elements(Types.Object(declared).LocalTypeParameters).ToArray()
                        : null;
                }
            }
        }
        return null;
    }

    // checker.ts getEffectiveTypeArgumentAtIndex
    private Type GetEffectiveTypeArgumentAtIndex(NodeRef node, Type[] typeParameters, int index)
    {
        var store = StoreOf(node);
        var typeArgumentNodes = store.TypeArguments(node.Node);
        if (!typeArgumentNodes.IsNone && index < store.Length(typeArgumentNodes))
        {
            return GetTypeFromTypeNode(At(node, store.Element(typeArgumentNodes, index)));
        }
        return GetEffectiveTypeArguments(node, typeParameters)[index];
    }

    // checker.ts getEffectiveTypeArguments
    private Type[] GetEffectiveTypeArguments(NodeRef node, Type[] typeParameters) =>
        FillMissingTypeArguments(TypeArgumentsFromTypeReferenceNode(node), typeParameters, GetMinTypeArgumentCount(typeParameters), Utilities.IsInJSFile(StoreOf(node), node.Node));

    // ---- signatures ----

    // signature.instantiations (module CO2: one map, the signature's id first in each key)
    private readonly KeyMap<Signature> signatureInstantiations = new();

    /// <summary>checker.ts getSignatureInstantiation.</summary>
    internal Signature GetSignatureInstantiation(Signature signature, Type[] typeArguments, bool isJavascript, List<Type>? inferredTypeParameters = null)
    {
        var typeParameters = Types.Elements(Signatures.Record(signature).TypeParameters).ToArray();
        var instantiatedSignature = GetSignatureInstantiationWithoutFillingInTypeArguments(signature, FillMissingTypeArguments(typeArguments, typeParameters, GetMinTypeArgumentCount(typeParameters), isJavascript));
        if (inferredTypeParameters is not null)
        {
            var returnSignature = GetSingleCallOrConstructSignature(GetReturnTypeOfSignature(instantiatedSignature));
            if (!returnSignature.IsNone)
            {
                var newReturnSignature = CloneSignature(returnSignature);
                Signatures.Record(newReturnSignature).TypeParameters = Types.CreateList(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(inferredTypeParameters));
                var newReturnType = GetOrCreateTypeFromSignature(newReturnSignature);
                Types.Object(newReturnType).Mapper = MapperAt(Signatures.Record(instantiatedSignature).Mapper);
                var newInstantiatedSignature = CloneSignature(instantiatedSignature);
                Signatures.Record(newInstantiatedSignature).ResolvedReturnType = newReturnType;
                return newInstantiatedSignature;
            }
        }
        return instantiatedSignature;
    }

    private Signature GetSignatureInstantiationWithoutFillingInTypeArguments(Signature signature, Type[] typeArguments)
    {
        var mark = keyScratch.Mark();
        keyScratch.Add(signature.Id);
        keyScratch.AddTypes(typeArguments);
        var id = keyScratch.Since(mark);
        if (!signatureInstantiations.TryGetValue(id, out var instantiation))
        {
            instantiation = CreateSignatureInstantiation(signature, typeArguments);
            signatureInstantiations.Set(id, instantiation);
        }
        keyScratch.Release(mark);
        return instantiation;
    }

    private readonly Dictionary<(NodeRef, Type), Type> instantiationExpressionTypes = [];

    /// <summary>
    /// checker.ts getInstantiationExpressionType: the type of `f&lt;T&gt;` (an expression or type query with type arguments).
    /// </summary>
    internal Type GetInstantiationExpressionType(Type exprType, NodeRef node)
    {
        var store = StoreOf(node);
        var typeArgumentNodes = store.TypeArguments(node.Node);
        if (exprType == silentNeverType || IsErrorType(exprType) || typeArgumentNodes.IsNone || store.Length(typeArgumentNodes) == 0)
        {
            return exprType;
        }
        if (instantiationExpressionTypes.TryGetValue((node, exprType), out var cached))
        {
            return cached;
        }
        var hasSomeApplicableSignature = false;
        var nonApplicableType = Type.None;
        var typeArgumentCount = store.Length(typeArgumentNodes);
        var result = GetInstantiatedType(exprType);
        instantiationExpressionTypes[(node, exprType)] = result;
        var errorTypeOfList = hasSomeApplicableSignature ? nonApplicableType : exprType;
        if (!errorTypeOfList.IsNone)
        {
            var file = FileAt(node.File);
            var start = Scanner.SkipTrivia(store.SourceFileText(file.Root).Chars, store.Pos(typeArgumentNodes));
            diagnostics.Add(ProgramDiagnostic.FromFile(file, Utilities.CreateFileDiagnostic(store, file.Root, start, store.End(typeArgumentNodes) - start,
                Diagnostics.Type_0_has_no_signatures_for_which_the_type_argument_list_is_applicable, TypeToString(errorTypeOfList))));
        }
        return result;

        Type GetInstantiatedType(Type type)
        {
            var hasSignatures = false;
            var hasApplicableSignature = false;
            var part = GetInstantiatedTypePart(type);
            hasSomeApplicableSignature |= hasApplicableSignature;
            if (hasSignatures && !hasApplicableSignature && nonApplicableType.IsNone)
            {
                nonApplicableType = type;
            }
            return part;

            Type GetInstantiatedTypePart(Type t)
            {
                var flags = Types.Flags(t);
                if ((flags & TypeFlags.Object) != 0)
                {
                    var resolved = ResolveStructuredTypeMembers(t);
                    var resolvedCallSignatures = Types.View(resolved.CallSignatures);
                    var resolvedConstructSignatures = Types.View(resolved.ConstructSignatures);
                    var callSignatures = GetInstantiatedSignatures(resolvedCallSignatures);
                    var constructSignatures = GetInstantiatedSignatures(resolvedConstructSignatures);
                    hasSignatures |= resolvedCallSignatures.Count != 0 || resolvedConstructSignatures.Count != 0;
                    hasApplicableSignature |= callSignatures.Count != 0 || constructSignatures.Count != 0;
                    if (!callSignatures.SameAs(resolvedCallSignatures) || !constructSignatures.SameAs(resolvedConstructSignatures))
                    {
                        var instantiated = CreateAnonymousType(CreateSymbol(SymbolFlags.None, InternalSymbolName.InstantiationExpression), resolved.Members!, callSignatures, constructSignatures, Types.View(resolved.IndexInfos));
                        Types.Record(instantiated).ObjectFlags |= ObjectFlags.InstantiationExpressionType;
                        Types.Object(instantiated).Node = node;
                        return instantiated;
                    }
                }
                else if ((flags & TypeFlags.InstantiableNonPrimitive) != 0)
                {
                    var constraint = GetBaseConstraintOfType(t);
                    if (!constraint.IsNone)
                    {
                        var instantiated = GetInstantiatedTypePart(constraint);
                        if (instantiated != constraint)
                        {
                            return instantiated;
                        }
                    }
                }
                else if ((flags & TypeFlags.Union) != 0)
                {
                    return MapType(t, GetInstantiatedType);
                }
                else if ((flags & TypeFlags.Intersection) != 0)
                {
                    var types = Types.Elements(Types.Union(t).Types).ToArray();
                    var mapped = Array.ConvertAll(types, GetInstantiatedTypePart);
                    return RangeEquals(mapped, types, types.Length) ? t : GetIntersectionType(mapped);
                }
                return t;
            }
        }

        Items<Signature> GetInstantiatedSignatures(Items<Signature> signatures)
        {
            var applicable = signatures.FindAll(sig =>
            {
                var typeParameters = Types.Elements(Signatures.Record(sig).TypeParameters).ToArray();
                return typeParameters.Length > 0 && typeArgumentCount >= GetMinTypeArgumentCount(typeParameters) && typeArgumentCount <= typeParameters.Length;
            });
            if (applicable.Count == signatures.Count && applicable.Count == 0)
            {
                return signatures;
            }
            var result = new List<Signature>(applicable.Count);
            foreach (var sig in applicable)
            {
                var declaration = Signatures.Record(sig).Declaration;
                var isJs = !declaration.IsNone && Utilities.IsInJSFile(StoreOf(declaration), declaration.Node);
                // checkTypeArguments (module C4)
                var typeArgumentTypes = CheckTypeArguments(sig, node, reportErrors: true);
                result.Add(typeArgumentTypes is not null ? GetSignatureInstantiation(sig, typeArgumentTypes, isJs) : sig);
            }
            return result;
        }
    }

    // checker.ts createSignatureInstantiation / createSignatureTypeMapper
    private Signature CreateSignatureInstantiation(Signature signature, Type[] typeArguments) =>
        // createSignatureTypeMapper: the type parameters for the mapper (getTypeParametersForMapper)
        InstantiateSignature(signature, CreateTypeMapper(GetTypeParametersForMapper(signature), typeArguments), eraseTypeParameters: true);
}
