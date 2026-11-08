// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// Portions Copyright 2026 Max Schwenk and the tsrs contributors
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts (Type, IntrinsicType, LiteralType, Signature, TransientSymbol) and
// src/compiler/checker.ts (createType, createIntrinsicType, createLiteralType, createSignature, createSymbol)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/types.go (Type with flags, objectFlags, id, symbol, alias and kind-specific
// data; Signature)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: records in per-checker arrays referenced by int handles (docs/phase3-checker-layout.md); module C0 defines
// the common part and the storage pattern, the checker modules add the kind-specific records they need.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// The types of one checker (types.go: Type; checker.ts creates types with <c>createType(flags)</c>): one common record
/// per type in creation order (the handle is the type id), kind-specific data in arrays of their own referenced by
/// <see cref="TypeRecord.Data"/>, and an append-only arena of type lists. Nothing is shared between checkers; one
/// thread uses a checker at a time.
/// </summary>
internal sealed class TypeStore
{
    /// <summary>types.go Type: the members every type has.</summary>
    internal struct TypeRecord
    {
        public TypeFlags Flags;
        public ObjectFlags ObjectFlags;
        public SymbolRef Symbol;
        public SymbolRef AliasSymbol;        // types.go TypeAlias.symbol (types.ts aliasSymbol)
        public TypeList AliasTypeArguments;  // types.go TypeAlias.typeArguments (types.ts aliasTypeArguments)
        public int Data;                     // index into the kind's data array (0: none)
    }

    /// <summary>types.go IntrinsicType.</summary>
    internal struct IntrinsicData
    {
        public string IntrinsicName;
        public string? DebugIntrinsicName; // checker.ts debugIntrinsicName (distinguishes e.g. the "any" variants)
    }

    /// <summary>types.go LiteralType (value: string, number, bigint as PseudoBigInt, boolean, or null for a computed enum).</summary>
    internal struct LiteralData
    {
        public object? Value;
        public Type FreshType;
        public Type RegularType;
    }

    /// <summary>types.go UnionOrIntersectionType (types, origin; the propagating object flags are in the common record).</summary>
    internal struct UnionData
    {
        public TypeList Types;
        public Type Origin;
        public SymTable PropertyCache;                                       // types.ts propertyCache (IsNone: undefined)
        public SymTable PropertyCacheWithoutObjectFunctionPropertyAugment;  // types.ts propertyCacheWithoutObjectFunctionPropertyAugment
        public ItemRange<SymbolRef> ResolvedProperties;                     // types.ts resolvedProperties (module CO3: a stored list)
        public bool HasResolvedProperties;                                  // resolvedProperties !== undefined
        public int ResolvedMembers;                                         // the object data record of the resolved members (module CO3; 0: not resolved)
    }

    private TypeRecord[] types = new TypeRecord[256];
    private int typeCount; // types[0] is the undefined handle
    private readonly Chunks<ObjectTypeRecord> objects = new(); // [0] unused (module CO3: records, not objects)
    private int objectCount;
    private readonly Chunks<UnionData> unions = new();
    private int unionCount;
    private readonly Chunks<TypeParameterData> typeParameters = new(); // [0] unused (module CO2: records, not objects)
    private int typeParameterCount;

    /// <summary>A new object type (createObjectType) with its data record.</summary>
    internal Type CreateObject(ObjectFlags objectFlags, SymbolRef symbol) =>
        Create(TypeFlags.Object, objectFlags, symbol, NewObjectRecord());

    /// <summary>The data of an object type.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ObjectTypeData Object(Type type) => new(objects, types[type.Id].Data);

    /// <summary>The object data record <paramref name="index"/> (<see cref="NewObjectData"/>).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ObjectTypeData ObjectData(int index) => new(objects, index);

    /// <summary>
    /// An object data record of no type (the resolved members of a union or intersection type, which TypeScript keeps
    /// on the type object): its index.
    /// </summary>
    internal int NewObjectData() => NewObjectRecord();

    private int NewObjectRecord()
    {
        objectCount++;
        objects.EnsureIndex(objectCount);
        return objectCount;
    }

    // The stored member lists (module CO3): properties, signatures, index infos as ranges of per-kind arenas.
    private readonly ItemArena<SymbolRef> symbolLists = new();
    private readonly ItemArena<Signature> signatureLists = new();
    private readonly ItemArena<IndexInfo> indexInfoLists = new();

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Items<SymbolRef> View(ItemRange<SymbolRef> list) => symbolLists.View(list);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Items<Signature> View(ItemRange<Signature> list) => signatureLists.View(list);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Items<IndexInfo> View(ItemRange<IndexInfo> list) => indexInfoLists.View(list);

    internal ItemRange<SymbolRef> Store(Items<SymbolRef> list) => symbolLists.Store(list);

    internal ItemRange<SymbolRef> Store(List<SymbolRef> list) => symbolLists.Store(list);

    internal ItemRange<SymbolRef> Store(ReadOnlySpan<SymbolRef> list) => symbolLists.Store(list);

    internal ItemRange<Signature> Store(Items<Signature> list) => signatureLists.Store(list);

    internal ItemRange<Signature> Store(List<Signature> list) => signatureLists.Store(list);

    internal ItemRange<IndexInfo> Store(Items<IndexInfo> list) => indexInfoLists.Store(list);

    internal ItemRange<IndexInfo> Store(List<IndexInfo> list) => indexInfoLists.Store(list);

    /// <summary>A stored copy of a list the checker built (the view of <see cref="Store(List{SymbolRef})"/>).</summary>
    internal Items<SymbolRef> Keep(List<SymbolRef> list) => symbolLists.Copy(list);

    internal Items<Signature> Keep(List<Signature> list) => signatureLists.Copy(list);

    internal Items<IndexInfo> Keep(List<IndexInfo> list) => indexInfoLists.Copy(list);

    // Module MM1 (Track O / tsrs-only; tsrs types.rs InterfaceType.resolved_base_types, an arena slice): the base type
    // lists of class and interface types, stored lists instead of a List per type.
    private readonly ItemArena<Type> baseTypeLists = new();

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Items<Type> ViewBaseTypes(ItemRange<Type> list) => baseTypeLists.View(list);

    internal ItemRange<Type> StoreBaseTypes(ReadOnlySpan<Type> list) => baseTypeLists.Store(list);

    /// <summary>A new union or intersection type (or an origin type) with its member list.</summary>
    internal Type CreateUnionOrIntersection(TypeFlags flags, ObjectFlags objectFlags, TypeList members, Type origin)
    {
        unionCount++;
        unions.EnsureIndex(unionCount);
        unions[unionCount] = new UnionData { Types = members, Origin = origin };
        return Create(flags, objectFlags, default, unionCount);
    }

    /// <summary>The data of a union or intersection type.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ref UnionData Union(Type type) => ref unions[types[type.Id].Data];

    /// <summary>A new type parameter (createTypeParameter).</summary>
    internal Type CreateTypeParameter(SymbolRef symbol)
    {
        typeParameterCount++;
        typeParameters.EnsureIndex(typeParameterCount);
        typeParameters[typeParameterCount] = default;
        return Create(TypeFlags.TypeParameter, ObjectFlags.None, symbol, typeParameterCount);
    }

    /// <summary>The data of a type parameter (a reference to its record, valid while the store grows).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ref TypeParameterData TypeParameter(Type type) => ref typeParameters[types[type.Id].Data];

    // The data of the instantiable kinds module C3 adds (indexed access, index, conditional, substitution, template
    // literal and string mapping types), one record class per kind.
    private readonly List<object> instantiables = [null!];

    /// <summary>A new type of an instantiable kind with its data record.</summary>
    internal Type CreateWithData(TypeFlags flags, object data, SymbolRef symbol = default)
    {
        instantiables.Add(data);
        return Create(flags, ObjectFlags.None, symbol, instantiables.Count - 1);
    }

    /// <summary>The data record of a type created by <see cref="CreateWithData"/>.</summary>
    internal T Data<T>(Type type) where T : class => (T)instantiables[types[type.Id].Data];

    private IntrinsicData[] intrinsics = new IntrinsicData[32];
    private int intrinsicCount;
    private LiteralData[] literals = new LiteralData[64];
    private int literalCount;
    private Type[] lists = new Type[256];
    private int listsUsed;

    /// <summary>checker.ts typeCount: the number of types created.</summary>
    internal int Count => typeCount;

    /// <summary>checker.ts createType(flags): a new type with the next id.</summary>
    internal Type Create(TypeFlags flags, ObjectFlags objectFlags = ObjectFlags.None, SymbolRef symbol = default, int data = 0)
    {
        if (typeCount + 1 == types.Length)
        {
            Array.Resize(ref types, types.Length * 2);
        }
        typeCount++;
        types[typeCount] = new TypeRecord { Flags = flags, ObjectFlags = objectFlags, Symbol = symbol, Data = data };
        return new Type(typeCount);
    }

    /// <summary>checker.ts createIntrinsicType(kind, intrinsicName, objectFlags).</summary>
    internal Type CreateIntrinsic(TypeFlags kind, string intrinsicName, ObjectFlags objectFlags = ObjectFlags.None, string? debugIntrinsicName = null)
    {
        if (intrinsicCount + 1 == intrinsics.Length)
        {
            Array.Resize(ref intrinsics, intrinsics.Length * 2);
        }
        intrinsicCount++;
        intrinsics[intrinsicCount] = new IntrinsicData { IntrinsicName = intrinsicName, DebugIntrinsicName = debugIntrinsicName };
        return Create(kind, objectFlags, default, intrinsicCount);
    }

    /// <summary>checker.ts createLiteralType(flags, value, symbol, regularType).</summary>
    internal Type CreateLiteral(TypeFlags flags, object? value, SymbolRef symbol, Type regularType)
    {
        if (literalCount + 1 == literals.Length)
        {
            Array.Resize(ref literals, literals.Length * 2);
        }
        literalCount++;
        var type = Create(flags, ObjectFlags.None, symbol, literalCount);
        literals[literalCount] = new LiteralData { Value = value, RegularType = regularType.IsNone ? type : regularType };
        return type;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ref TypeRecord Record(Type type) => ref types[type.Id];

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal TypeFlags Flags(Type type) => types[type.Id].Flags;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ObjectFlags ObjectFlagsOf(Type type) => types[type.Id].ObjectFlags;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SymbolRef Symbol(Type type) => types[type.Id].Symbol;

    /// <summary>types.go IntrinsicType.intrinsicName (the type must be intrinsic).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal string IntrinsicName(Type type) => intrinsics[types[type.Id].Data].IntrinsicName;

    /// <summary>types.go LiteralType (the type must be a literal type).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ref LiteralData Literal(Type type) => ref literals[types[type.Id].Data];

    /// <summary>A new type list with these elements (copied into the arena).</summary>
    internal TypeList CreateList(ReadOnlySpan<Type> elements)
    {
        if (elements.IsEmpty)
        {
            return TypeList.Empty;
        }
        if (listsUsed + elements.Length > lists.Length)
        {
            Array.Resize(ref lists, Math.Max(lists.Length * 2, listsUsed + elements.Length));
        }
        elements.CopyTo(lists.AsSpan(listsUsed));
        var list = new TypeList(listsUsed, elements.Length);
        listsUsed += elements.Length;
        return list;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ReadOnlySpan<Type> Elements(TypeList list) => list.Count == 0 ? default : lists.AsSpan(list.Start, list.Count);

    /// <summary>
    /// Element <paramref name="index"/> of a list, read from the arena as it is now (unlike a span from
    /// <see cref="Elements"/>, valid while the loop that reads it creates types and lists).
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Type Element(TypeList list, int index) => lists[list.Start + index];
}

/// <summary>
/// The data of an object type (module CO3): a reference to its <see cref="ObjectTypeRecord"/> in the store's chunks,
/// read and written through the members below like the class it replaces (copying it does not copy the record). Lives
/// on the stack; the type record keeps the record's index.
/// </summary>
internal readonly struct ObjectTypeData
{
    private readonly Chunks<ObjectTypeRecord> records;
    private readonly int index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ObjectTypeData(Chunks<ObjectTypeRecord> records, int index)
    {
        this.records = records;
        this.index = index;
    }

    /// <summary>The record (valid while the store grows: chunks never move).</summary>
    internal ref ObjectTypeRecord Record
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => ref records[index];
    }

    public ref bool MembersResolved => ref Record.MembersResolved;
    public ref SymTable Members => ref Record.Members;
    public ref ItemRange<SymbolRef> Properties => ref Record.Properties;
    public ref ItemRange<Signature> CallSignatures => ref Record.CallSignatures;
    public ref ItemRange<Signature> ConstructSignatures => ref Record.ConstructSignatures;
    public ref ItemRange<IndexInfo> IndexInfos => ref Record.IndexInfos;
    public ref TypeList TypeParameters => ref Record.TypeParameters;
    public ref TypeList OuterTypeParameters => ref Record.OuterTypeParameters;
    public ref TypeList LocalTypeParameters => ref Record.LocalTypeParameters;
    public ref bool HasTypeParameters => ref Record.HasTypeParameters;
    public ref Type ThisType => ref Record.ThisType;
    public ref bool DeclaredMembersResolved => ref Record.DeclaredMembersResolved;
    public ref ItemRange<SymbolRef> DeclaredProperties => ref Record.DeclaredProperties;
    public ref ItemRange<Signature> DeclaredCallSignatures => ref Record.DeclaredCallSignatures;
    public ref ItemRange<Signature> DeclaredConstructSignatures => ref Record.DeclaredConstructSignatures;
    public ref ItemRange<IndexInfo> DeclaredIndexInfos => ref Record.DeclaredIndexInfos;
    public ref ItemRange<Type> ResolvedBaseTypes => ref Record.ResolvedBaseTypes;
    public ref bool ResolvedBaseTypesIsResolvingEmpty => ref Record.ResolvedBaseTypesIsResolvingEmpty;
    public ref bool BaseTypesResolved => ref Record.BaseTypesResolved;
    public ref Type ResolvedBaseConstructorType => ref Record.ResolvedBaseConstructorType;
    public ref Type Target => ref Record.Target;
    public ref TypeList ResolvedTypeArguments => ref Record.ResolvedTypeArguments;
    public ref bool HasResolvedTypeArguments => ref Record.HasResolvedTypeArguments;
    public ref bool HasInstantiations => ref Record.HasInstantiations;
    public ref NodeRef Node => ref Record.Node;
    public ref TypeMapper Mapper => ref Record.Mapper;
    public ref ElementFlags[]? ElementFlags => ref Record.ElementFlags;
    public ref int MinLength => ref Record.MinLength;
    public ref int FixedLength => ref Record.FixedLength;
    public ref ElementFlags CombinedFlags => ref Record.CombinedFlags;
    public ref bool Readonly => ref Record.Readonly;
    public ref NodeRef[]? LabeledElementDeclarations => ref Record.LabeledElementDeclarations;
    public ref NodeRef Declaration => ref Record.Declaration;
    public ref Type MappedTypeParameter => ref Record.MappedTypeParameter;
    public ref Type ConstraintType => ref Record.ConstraintType;
    public ref Type NameType => ref Record.NameType;
    public ref Type TemplateType => ref Record.TemplateType;
    public ref Type ModifiersType => ref Record.ModifiersType;
    public ref Type ResolvedApparentType => ref Record.ResolvedApparentType;
    public ref bool ContainsError => ref Record.ContainsError;
}

/// <summary>
/// types.ts ObjectType and its kinds (InterfaceType, TypeReference, ResolvedType members), typescript-go's
/// ObjectType/InterfaceType/TypeReference data. Module C1 kept one record class for all object kinds; module CO3: a
/// struct record in the store's chunks (<see cref="ObjectTypeData"/> refers to it), the member lists as ranges of the
/// store's arenas (read through <see cref="TypeStore.View(ItemRange{Signature})"/>).
/// </summary>
internal struct ObjectTypeRecord
{
    // ResolvedType (resolveStructuredTypeMembers)
    public bool MembersResolved;
    public SymTable Members; // (set when the members are resolved)
    public ItemRange<SymbolRef> Properties;
    public ItemRange<Signature> CallSignatures;
    public ItemRange<Signature> ConstructSignatures;
    public ItemRange<IndexInfo> IndexInfos;

    // InterfaceType
    public TypeList TypeParameters;
    public TypeList OuterTypeParameters;
    public TypeList LocalTypeParameters;
    public bool HasTypeParameters; // typeParameters !== undefined
    public Type ThisType;
    public bool DeclaredMembersResolved;
    public ItemRange<SymbolRef> DeclaredProperties;
    public ItemRange<Signature> DeclaredCallSignatures;
    public ItemRange<Signature> DeclaredConstructSignatures;
    public ItemRange<IndexInfo> DeclaredIndexInfos;
    // resolvedBaseTypes (module MM1, Track O / tsrs-only: a stored list of the store's base type arena instead of a List,
    // as tsrs keeps type lists as arena slices, crates/tsrs_checker/src/types.rs InterfaceType.resolved_base_types;
    // default: undefined or empty, which getBaseTypes treats alike; ResolvedBaseTypesIsResolvingEmpty: the list is
    // checker.ts resolvingEmptyArray)
    public ItemRange<Type> ResolvedBaseTypes;
    public bool ResolvedBaseTypesIsResolvingEmpty;
    public bool BaseTypesResolved; // baseTypesResolved
    public Type ResolvedBaseConstructorType;

    // TypeReference / GenericType
    public Type Target;
    public TypeList ResolvedTypeArguments;
    public bool HasResolvedTypeArguments;
    public bool HasInstantiations; // instantiations !== undefined (the entries: Checker.typeInstantiations, module CO2)
    public NodeRef Node; // deferred type reference node (C3)

    // AnonymousType / DeferredTypeReference / MappedType (C3): the instantiation's mapper (Target: the instantiated type)
    public TypeMapper Mapper;

    // TupleType (C3)
    public ElementFlags[]? ElementFlags;
    public int MinLength;
    public int FixedLength;
    public ElementFlags CombinedFlags;
    public bool Readonly;
    public NodeRef[]? LabeledElementDeclarations;

    // MappedType (C3)
    public NodeRef Declaration;
    public Type MappedTypeParameter;
    public Type ConstraintType;
    public Type NameType;
    public Type TemplateType;
    public Type ModifiersType;
    public Type ResolvedApparentType;
    public bool ContainsError;
}

/// <summary>types.ts TypeParameter (module CO2: a record in the store's chunks).</summary>
internal struct TypeParameterData
{
    public Type Constraint;
    public Type Default;
    public bool IsThisType;
    public Type Target;          // the type parameter this one is an instantiation of (C3)
    public TypeMapper Mapper;    // the mapper of that instantiation (C3)
}

/// <summary>types.ts IndexedAccessType (C3).</summary>
internal sealed class IndexedAccessData
{
    public Type ObjectType;
    public Type IndexType;
    public AccessFlags AccessFlags;
}

/// <summary>types.ts IndexType (C3).</summary>
internal sealed class IndexData
{
    public Type Type;
    public IndexFlags IndexFlags;
}

/// <summary>types.ts TemplateLiteralType (C3).</summary>
internal sealed class TemplateLiteralData
{
    public string[] Texts = [];
    public Type[] Types = [];
}

/// <summary>types.ts StringMappingType (C3; the symbol is the common record's).</summary>
internal sealed class StringMappingData
{
    public Type Type;
}

/// <summary>types.ts SubstitutionType (C3).</summary>
internal sealed class SubstitutionData
{
    public Type BaseType;
    public Type Constraint;
}

/// <summary>types.ts ConditionalRoot (C3).</summary>
internal sealed class ConditionalRoot
{
    public NodeRef Node;
    public Type CheckType;
    public Type ExtendsType;
    public bool IsDistributive;
    public Type[]? InferTypeParameters;
    public Type[]? OuterTypeParameters;
    public int Id; // the root's number in its checker (the key of its instantiations, Checker.conditionalInstantiations; module CO2)
    public SymbolRef AliasSymbol;
    public TypeList AliasTypeArguments;
}

/// <summary>types.ts ConditionalType (C3).</summary>
internal sealed class ConditionalData
{
    public ConditionalRoot Root = null!;
    public Type CheckType;
    public Type ExtendsType;
    public TypeMapper Mapper;
    public TypeMapper CombinedMapper;
    public Type ResolvedTrueType;
    public Type ResolvedFalseType;
    public Type ResolvedInferredTrueType;
    public Type ResolvedDefaultConstraint;
    public Type ResolvedConstraintOfDistributive; // None: not computed; noConstraintType: false
}

/// <summary>
/// types.ts IndexInfo (module CO3: a value, stored in the index info lists of the store's arena, not an object). Every
/// <c>new IndexInfo { ... }</c> gets a new <see cref="Id"/> and copies keep it, so <c>==</c> is TypeScript's identity
/// of index info objects (the ids are only compared, never ordered).
/// </summary>
internal struct IndexInfo : IEquatable<IndexInfo>
{
    private static int lastId;

    public readonly int Id;
    public Type KeyType;
    public Type Type;
    public bool IsReadonly;
    public NodeRef Declaration;
    public List<NodeRef>? Components; // the declarations with computed property names the index info is derived from

    public IndexInfo()
    {
        Id = Interlocked.Increment(ref lastId);
    }

    public static bool operator ==(IndexInfo left, IndexInfo right) => left.Id == right.Id;

    public static bool operator !=(IndexInfo left, IndexInfo right) => left.Id != right.Id;

    public readonly bool Equals(IndexInfo other) => Id == other.Id;

    public override readonly bool Equals(object? obj) => obj is IndexInfo other && Id == other.Id;

    public override readonly int GetHashCode() => Id;
}

/// <summary>
/// The IndexInfo members the checker reads on an index info it has checked for undefined (module CO3: the info is a
/// nullable struct, which flow analysis does not unwrap; on undefined they throw, as the class members did).
/// </summary>
internal static class IndexInfoExtensions
{
    extension(IndexInfo? info)
    {
        internal Type KeyType => info!.Value.KeyType;

        internal Type Type => info!.Value.Type;

        internal bool IsReadonly => info!.Value.IsReadonly;

        internal NodeRef Declaration => info!.Value.Declaration;

        internal List<NodeRef>? Components => info!.Value.Components;
    }
}

/// <summary>
/// The signatures of one checker (types.go: Signature): records in creation order, the handle is the signature id.
/// </summary>
internal sealed class SignatureStore
{
    /// <summary>types.go Signature, the members C0 fixes; the checker modules add the resolved members they need.</summary>
    internal struct SignatureRecord
    {
        public SignatureFlags Flags;
        public int MinArgumentCount;
        public int ResolvedMinArgumentCount;
        public NodeRef Declaration;
        public TypeList TypeParameters;
        // the parameter symbols (C1: an array per signature; module AR1: a stored list of the store's parameter arena,
        // read with SignatureStore.ParametersOf; default: none)
        public ItemRange<SymbolRef> Parameters;
        public SymbolRef ThisParameter;
        public Type ResolvedReturnType;
        public Signature Target;
        public int Mapper;              // type mapper handle (C3)
        public Signature[]? CompositeSignatures; // union and intersection signatures (C5)
        public TypeFlags CompositeKind;
        // checker.ts createCallSignature: the declaration is a synthetic FunctionTypeNode (no node here; Declaration
        // stays None and this marks it, copied wherever the declaration is copied) (C9e)
        public bool SyntheticFunctionTypeDeclaration;
    }

    private SignatureRecord[] signatures = new SignatureRecord[64];
    private int count;

    internal int Count => count;

    /// <summary>checker.ts createSignature (the common members).</summary>
    internal Signature Create(SignatureFlags flags, NodeRef declaration, TypeList typeParameters, SymbolRef thisParameter, int minArgumentCount)
    {
        if (count + 1 == signatures.Length)
        {
            Array.Resize(ref signatures, signatures.Length * 2);
        }
        count++;
        signatures[count] = new SignatureRecord
        {
            Flags = flags,
            Declaration = declaration,
            TypeParameters = typeParameters,
            ThisParameter = thisParameter,
            MinArgumentCount = minArgumentCount,
            ResolvedMinArgumentCount = -1,
        };
        return new Signature(count);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ref SignatureRecord Record(Signature signature) => ref signatures[signature.Id];

    // Module AR1 (Track O / tsrs-only; tsrs keeps a signature's parameters as an arena slice, crates/tsrs_checker
    // types.rs Signature.parameters): the parameter lists of all signatures in one append-only arena of chunks that never
    // move, instead of one array per signature. Sharing a list (TypeScript assigns the same array) copies the range.
    private readonly ItemArena<SymbolRef> parameterLists = new();

    /// <summary>A stored parameter list with these symbols (a copy).</summary>
    internal ItemRange<SymbolRef> StoreParameters(ReadOnlySpan<SymbolRef> parameters) => parameterLists.Store(parameters);

    /// <summary>The parameter symbols of a stored list (valid for good: the arena's chunks never move).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ReadOnlySpan<SymbolRef> ParametersOf(ItemRange<SymbolRef> parameters) => parameterLists.View(parameters).AsSpan();

    /// <summary>signature.parameters.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ReadOnlySpan<SymbolRef> ParametersOf(Signature signature) => parameterLists.View(signatures[signature.Id].Parameters).AsSpan();
}

/// <summary>
/// The transient symbols of one checker (checker.ts createSymbol: SymbolFlags.Transient, with CheckFlags), records in
/// creation order referenced by <see cref="SymbolRef"/> with File 0. Merged symbols (mergeSymbol's clones), instantiated
/// and synthetic properties and the checker's own symbols (globalThis, undefined, arguments, ...) live here.
/// </summary>
internal sealed class TransientSymbolStore
{
    /// <summary>The symbol members of a transient symbol (types.ts Symbol + TransientSymbol.links.checkFlags).</summary>
    internal struct TransientSymbolRecord
    {
        public SymbolFlags Flags;
        public CheckFlags CheckFlags;
        public int Name;            // escapedName as an id of the checker's NameTable (module CO2)
        public DeclRef Declarations; // (module CO2: a binder symbol's chain or a list of the checker's DeclarationStore)
        public NodeRef ValueDeclaration;
        public SymbolRef Parent;
        public SymTable Members;    // checker-owned symbol tables (C1: SymTable, an ordered map like TypeScript's Map; CO3: IsNone = undefined)
        public SymTable Exports;
        public bool ConstEnumOnlyModule;
        public SymbolRef ExportSymbol;
    }

    private readonly Chunks<TransientSymbolRecord> symbols = new();
    private readonly NameTable names;
    private int count;

    internal TransientSymbolStore(NameTable names)
    {
        this.names = names;
    }

    internal int Count => count;

    /// <summary>checker.ts createSymbol(flags, name, checkFlags).</summary>
    internal SymbolRef Create(SymbolFlags flags, string escapedName, CheckFlags checkFlags = CheckFlags.None) =>
        Create(flags, names.Intern(escapedName), checkFlags);

    /// <summary>checker.ts createSymbol(flags, name, checkFlags) with the name as a <see cref="NameTable"/> id.</summary>
    internal SymbolRef Create(SymbolFlags flags, int name, CheckFlags checkFlags = CheckFlags.None)
    {
        count++;
        symbols.EnsureIndex(count);
        symbols[count] = new TransientSymbolRecord { Flags = flags | SymbolFlags.Transient, CheckFlags = checkFlags, Name = name };
        return new SymbolRef(0, count);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ref TransientSymbolRecord Record(SymbolRef symbol) => ref symbols[symbol.Index];
}

/// <summary>
/// Records of one kind in fixed-size chunks that never move (module CO1): growing adds a chunk instead of copying the
/// records into a larger array (records with references would be copied with write barriers and the whole new array
/// scanned by the next collection), and a <c>ref</c> to a record stays valid while the store grows.
/// </summary>
internal sealed class Chunks<T>
{
    private const int Shift = 12;
    private const int Size = 1 << Shift;
    private const int Mask = Size - 1;

    private T[][] chunks = [new T[Size]];

    internal ref T this[int index]
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => ref chunks[index >> Shift][index & Mask];
    }

    /// <summary>Makes <paramref name="index"/> addressable.</summary>
    internal void EnsureIndex(int index)
    {
        var chunk = index >> Shift;
        if (chunk >= chunks.Length)
        {
            Array.Resize(ref chunks, Math.Max(chunks.Length * 2, chunk + 1));
        }
        chunks[chunk] ??= new T[Size];
    }
}



/// <summary>
/// symbol.declarations of a transient symbol as a reference (module CO2): the declarations array of binder symbol
/// (<see cref="File"/>, <see cref="Index"/>) when it is shared with it (TypeScript assigns
/// <c>result.declarations = symbol.declarations</c>), else a list of the checker's <see cref="DeclarationStore"/>.
/// Copying the reference shares the array, as in TypeScript; nothing here is traced by the collector.
/// </summary>
internal readonly struct DeclRef
{
    internal readonly int File;  // > 0: the binder symbol's declarations; 0: a DeclarationStore list (Index 0: undefined)
    internal readonly int Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal DeclRef(int file, int index)
    {
        File = file;
        Index = index;
    }

    internal bool IsUndefined
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => File == 0 && Index == 0;
    }
}

/// <summary>
/// The declaration arrays the checker creates (module CO2): per list (start, count, capacity) in one append-only arena of
/// node references; appending to a full list moves it to the arena's end (its old range stays unused).
/// </summary>
internal sealed class DeclarationStore
{
    private struct ListRecord
    {
        public int Start;
        public int Count;
        public int Capacity;
    }

    private ListRecord[] lists = new ListRecord[256]; // [0] unused
    private int listCount;
    private NodeRef[] nodes = new NodeRef[1024];
    private int nodesUsed;

    /// <summary>A new list with these declarations (a copy).</summary>
    internal DeclRef Create(ReadOnlySpan<NodeRef> items)
    {
        if (listCount + 1 == lists.Length)
        {
            Array.Resize(ref lists, lists.Length * 2);
        }
        var start = Reserve(items.Length);
        items.CopyTo(nodes.AsSpan(start));
        listCount++;
        lists[listCount] = new ListRecord { Start = start, Count = items.Length, Capacity = items.Length };
        return new DeclRef(0, listCount);
    }

    /// <summary>A new list with the declarations of a view (a copy).</summary>
    internal DeclRef CreateCopy(Decls items)
    {
        if (listCount + 1 == lists.Length)
        {
            Array.Resize(ref lists, lists.Length * 2);
        }
        var start = Reserve(items.Count);
        var i = start;
        foreach (var node in items)
        {
            nodes[i++] = node;
        }
        listCount++;
        lists[listCount] = new ListRecord { Start = start, Count = items.Count, Capacity = items.Count };
        return new DeclRef(0, listCount);
    }

    /// <summary>declarations.push(node) on a list of this store.</summary>
    internal void Add(DeclRef list, NodeRef node)
    {
        if (list.File != 0 || list.Index == 0)
        {
            throw new InvalidOperationException("only the checker's own declaration lists are appended to");
        }
        ref var r = ref lists[list.Index];
        if (r.Count == r.Capacity)
        {
            var capacity = Math.Max(4, r.Capacity * 2);
            if (r.Start + r.Capacity == nodesUsed)
            {
                // the last range of the arena grows in place
                Reserve(capacity - r.Capacity);
            }
            else
            {
                var start = Reserve(capacity);
                nodes.AsSpan(r.Start, r.Count).CopyTo(nodes.AsSpan(start));
                r.Start = start;
            }
            r.Capacity = capacity;
        }
        nodes[r.Start + r.Count] = node;
        r.Count++;
    }

    /// <summary>The declarations of a list of this store as they are now.</summary>
    internal Decls View(DeclRef list)
    {
        ref var r = ref lists[list.Index];
        return new Decls(nodes, r.Start, r.Count);
    }

    private int Reserve(int length)
    {
        if (nodesUsed + length > nodes.Length)
        {
            Array.Resize(ref nodes, Math.Max(nodes.Length * 2, nodesUsed + length));
        }
        var start = nodesUsed;
        nodesUsed += length;
        return start;
    }
}

/// <summary>
/// A view of a declarations array (module CO2): a binder symbol's chain or a range of the checker's
/// <see cref="DeclarationStore"/> (or of an array of its own), read like the <c>List</c> it replaces. Views live on
/// the stack; a view taken before the list is appended to does not see the new declaration.
/// </summary>
[System.Runtime.CompilerServices.CollectionBuilder(typeof(Decls), nameof(Create))]
internal readonly struct Decls : IReadOnlyList<NodeRef>
{
    private readonly BinderStore.NodeChain chain; // isChain: the binder's declarations of a symbol of file
    private readonly int file;
    private readonly NodeRef[]? array;
    private readonly int start;
    private readonly int count;
    private readonly bool isChain;

    internal Decls(NodeRef[] array, int start, int count)
    {
        this.array = array;
        this.start = start;
        this.count = count;
    }

    internal Decls(BinderStore.NodeChain chain, int file)
    {
        this.chain = chain;
        this.file = file;
        isChain = true;
    }

    /// <summary>A view of these declarations (a copy; collection expressions).</summary>
    internal static Decls Create(ReadOnlySpan<NodeRef> items) => items.IsEmpty ? default : new Decls(items.ToArray(), 0, items.Length);

    // (a chain is counted when asked: most readers take the first declaration or enumerate)
    public int Count
    {
        get
        {
            if (!isChain)
            {
                return count;
            }
            var n = 0;
            foreach (var _ in chain)
            {
                n++;
            }
            return n;
        }
    }

    public NodeRef this[int index]
    {
        get
        {
            if (!isChain)
            {
                if ((uint)index >= (uint)count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
                return array![start + index];
            }
            foreach (var node in chain)
            {
                if (index-- == 0)
                {
                    return new NodeRef(file, node);
                }
            }
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<NodeRef> IEnumerable<NodeRef>.GetEnumerator() => GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>List.Find (NodeRef.None when no declaration matches).</summary>
    internal NodeRef Find(Predicate<NodeRef> match)
    {
        foreach (var node in this)
        {
            if (match(node))
            {
                return node;
            }
        }
        return NodeRef.None;
    }

    internal bool Exists(Predicate<NodeRef> match) => FindIndex(match) >= 0;

    internal bool TrueForAll(Predicate<NodeRef> match)
    {
        foreach (var node in this)
        {
            if (!match(node))
            {
                return false;
            }
        }
        return true;
    }

    internal int FindIndex(Predicate<NodeRef> match)
    {
        var i = 0;
        foreach (var node in this)
        {
            if (match(node))
            {
                return i;
            }
            i++;
        }
        return -1;
    }

    internal bool Contains(NodeRef value) => IndexOf(value) >= 0;

    /// <summary>List.FindAll (a new list).</summary>
    internal List<NodeRef> FindAll(Predicate<NodeRef> match)
    {
        var result = new List<NodeRef>();
        foreach (var node in this)
        {
            if (match(node))
            {
                result.Add(node);
            }
        }
        return result;
    }

    internal int IndexOf(NodeRef value)
    {
        var i = 0;
        foreach (var node in this)
        {
            if (node == value)
            {
                return i;
            }
            i++;
        }
        return -1;
    }

    internal struct Enumerator : IEnumerator<NodeRef>
    {
        private readonly Decls decls;
        private BinderStore.NodeChain.Enumerator chainEnumerator;
        private int index;
        private NodeRef current;

        internal Enumerator(Decls decls)
        {
            this.decls = decls;
            chainEnumerator = decls.isChain ? decls.chain.GetEnumerator() : default;
            index = -1;
        }

        public readonly NodeRef Current => current;

        readonly object System.Collections.IEnumerator.Current => current;

        public bool MoveNext()
        {
            if (decls.isChain)
            {
                if (chainEnumerator.MoveNext())
                {
                    current = new NodeRef(decls.file, chainEnumerator.Current);
                    return true;
                }
                return false;
            }
            if (++index < decls.count)
            {
                current = decls.array![decls.start + index];
                return true;
            }
            return false;
        }

        public void Reset() => throw new NotSupportedException();

        public readonly void Dispose()
        {
        }
    }
}

/// <summary>
/// The List members the checker reads on a declarations view it has checked for undefined (module CO2: the view is a
/// nullable struct, which flow analysis does not unwrap; on undefined they throw, as the List members did).
/// </summary>
internal static class DeclsExtensions
{
    extension(Decls? declarations)
    {
        internal int Count => declarations!.Value.Count;

        internal NodeRef Find(Predicate<NodeRef> match) => declarations!.Value.Find(match);

        internal bool Exists(Predicate<NodeRef> match) => declarations!.Value.Exists(match);

        internal bool TrueForAll(Predicate<NodeRef> match) => declarations!.Value.TrueForAll(match);

        internal bool Contains(NodeRef value) => declarations!.Value.Contains(value);

        internal int IndexOf(NodeRef value) => declarations!.Value.IndexOf(value);
    }
}

/// <summary>
/// A map from a sequence of ints to a value (module CO2): the counterpart of checker.ts's caches keyed by strings built
/// from ids (getTypeListId, getAliasId and the prefixes around them). The callers encode each part of the string as
/// ints (lists with their length, a tag per prefix), one to one, so a key hits exactly when TypeScript's string would.
/// Keys live in an int arena, the index is open addressing (linear probing, load at most 1/2); no per-entry objects
/// and no references. Never iterated.
/// </summary>
internal sealed class KeyMap<TValue>
    where TValue : struct
{
    private struct Slot
    {
        public int Start;  // key offset + 1 (0: free)
        public int Length;
        public int Hash;
        public TValue Value;
    }

    private int[] keys = new int[256];
    private int keysUsed;
    private Slot[] slots = new Slot[64];
    private int count;

    internal int Count => count;

    /// <summary>Map.clear (the arrays are kept for reuse).</summary>
    internal void Clear()
    {
        if (count > 0)
        {
            Array.Clear(slots);
            count = 0;
            keysUsed = 0;
        }
    }

    /// <summary>
    /// <see cref="Clear"/> for a scratch map that one large use may have grown (Track O; tsrs checker_11.rs
    /// <c>pop_active_mapper</c> and types.rs <c>clear_scratch</c>, notes/cpu-checker.md changes 1 and 4): clearing
    /// touches every slot, so a table whose capacity is more than 4 times its entries and more than 64 (hashbrown's
    /// capacity: half of the slots here) is replaced by a new small one. The map is empty afterwards either way.
    /// </summary>
    internal void ClearScratch()
    {
        if (count > 0)
        {
            if (slots.Length > 128 && slots.Length > 8 * count)
            {
                slots = new Slot[64];
            }
            else
            {
                Array.Clear(slots);
            }
            count = 0;
            keysUsed = 0;
        }
    }

    /// <summary>Map.get: whether the key is present, and its value.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool TryGetValue(ReadOnlySpan<int> key, out TValue value)
    {
        var hash = Hash(key);
        var s = slots;
        var mask = s.Length - 1;
        for (var i = hash & mask; ; i = (i + 1) & mask)
        {
            ref var slot = ref s[i];
            if (slot.Start == 0)
            {
                value = default;
                return false;
            }
            if (slot.Hash == hash && slot.Length == key.Length && keys.AsSpan(slot.Start - 1, slot.Length).SequenceEqual(key))
            {
                value = slot.Value;
                return true;
            }
        }
    }

    /// <summary>Map.set (a present key keeps its slot and gets the new value).</summary>
    internal void Set(ReadOnlySpan<int> key, TValue value)
    {
        if ((count + 1) * 2 > slots.Length)
        {
            Grow();
        }
        var hash = Hash(key);
        var s = slots;
        var mask = s.Length - 1;
        for (var i = hash & mask; ; i = (i + 1) & mask)
        {
            ref var slot = ref s[i];
            if (slot.Start == 0)
            {
                if (keysUsed + key.Length > keys.Length)
                {
                    Array.Resize(ref keys, Math.Max(keys.Length * 2, keysUsed + key.Length));
                }
                key.CopyTo(keys.AsSpan(keysUsed));
                slot.Start = keysUsed + 1;
                slot.Length = key.Length;
                slot.Hash = hash;
                slot.Value = value;
                keysUsed += key.Length;
                count++;
                return;
            }
            if (slot.Hash == hash && slot.Length == key.Length && keys.AsSpan(slot.Start - 1, slot.Length).SequenceEqual(key))
            {
                slot.Value = value;
                return;
            }
        }
    }

    private void Grow()
    {
        var old = slots;
        var grown = new Slot[old.Length * 2];
        var mask = grown.Length - 1;
        foreach (var slot in old)
        {
            if (slot.Start == 0)
            {
                continue;
            }
            var i = slot.Hash & mask;
            while (grown[i].Start != 0)
            {
                i = (i + 1) & mask;
            }
            grown[i] = slot;
        }
        slots = grown;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int Hash(ReadOnlySpan<int> key)
    {
        var h = (uint)key.Length * 0x9E3779B1u;
        foreach (var k in key)
        {
            h = (System.Numerics.BitOperations.RotateLeft(h, 5) ^ (uint)k) * 0x27D4EB2Fu;
        }
        h ^= h >> 15;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        return (int)(h & 0x7FFFFFFF);
    }
}

/// <summary>
/// The scratch stack the checker builds <see cref="KeyMap{TValue}"/> keys on (module CO2): a key is the ints pushed since
/// its mark; nested key building (while a type is created on a cache miss) pushes above it, so the key's span stays valid
/// until the mark is released (a span into an array the stack outgrew still reads the same ints).
/// </summary>
internal sealed class KeyScratch
{
    private int[] ints = new int[256];
    private int top;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int Mark() => top;

    internal void Add(int value)
    {
        if (top == ints.Length)
        {
            Array.Resize(ref ints, ints.Length * 2);
        }
        ints[top++] = value;
    }

    /// <summary>A list: its length, then its type ids.</summary>
    internal void AddTypes(ReadOnlySpan<Type> types)
    {
        if (top + types.Length + 1 > ints.Length)
        {
            Array.Resize(ref ints, Math.Max(ints.Length * 2, top + types.Length + 1));
        }
        ints[top++] = types.Length;
        foreach (var type in types)
        {
            ints[top++] = type.Id;
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ReadOnlySpan<int> Since(int mark) => ints.AsSpan(mark, top - mark);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void Release(int mark) => top = mark;
}

/// <summary>
/// The escaped names of one checker as dense ids (module CO2): one string per distinct name, symbol tables and transient
/// symbols store the id. Strings are read back only where text is produced or compared as text.
/// </summary>
internal sealed class NameTable
{
    // Module MM1 (Track O / tsrs-only; tsrs keeps its name tables as handle-keyed tables, notes/mem-flat-symbol-tables.md
    // and notes/mem-checker-heap.md): the index from name to id is an open-addressing table of (hash, id + 1) slots
    // without references (linear probing, load at most 1/2, never deleted) instead of a Dictionary whose entries hold
    // the strings; the strings are only in the id-ordered array. Ids are given in the same order as before.
    private struct Slot
    {
        public int Hash;
        public int Id; // id + 1 (0: free)
    }

    private Slot[] index = new Slot[2048];
    private string[] names = new string[1024];
    private int count;
    private SymTableStore? tables;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int HashOf(ReadOnlySpan<char> name)
    {
        var h = (uint)name.Length * 0x9E3779B1u;
        foreach (var ch in name)
        {
            h = (System.Numerics.BitOperations.RotateLeft(h, 5) ^ ch) * 0x27D4EB2Fu;
        }
        h ^= h >> 15;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        return (int)(h & 0x7FFFFFFF);
    }

    // the slot of the name: its id + 1 when present, else 0 with the free slot it would take
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int Lookup(ReadOnlySpan<char> name, int hash, out int free)
    {
        var s = index;
        var mask = s.Length - 1;
        for (var i = hash & mask; ; i = (i + 1) & mask)
        {
            ref var slot = ref s[i];
            if (slot.Id == 0)
            {
                free = i;
                return 0;
            }
            if (slot.Hash == hash && name.SequenceEqual(names[slot.Id - 1]))
            {
                free = -1;
                return slot.Id;
            }
        }
    }

    private int Insert(string name, int hash, int free)
    {
        var id = Add(name);
        index[free] = new Slot { Hash = hash, Id = id + 1 };
        if (count * 2 > index.Length)
        {
            var grown = new Slot[index.Length * 2];
            var mask = grown.Length - 1;
            foreach (var slot in index)
            {
                if (slot.Id == 0)
                {
                    continue;
                }
                var i = slot.Hash & mask;
                while (grown[i].Id != 0)
                {
                    i = (i + 1) & mask;
                }
                grown[i] = slot;
            }
            index = grown;
        }
        return id;
    }

    internal int Count => count;

    /// <summary>The symbol tables of the checker these names belong to (module CO3).</summary>
    internal SymTableStore Tables => tables ??= new SymTableStore(this);

    /// <summary>The name of an id.</summary>
    internal string this[int id]
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => names[id];
    }

    /// <summary>The id of a name, added if new.</summary>
    internal int Intern(string name)
    {
        var hash = HashOf(name);
        var found = Lookup(name, hash, out var free);
        return found != 0 ? found - 1 : Insert(name, hash, free);
    }

    /// <summary>The id of a name given by its characters, added if new.</summary>
    internal int Intern(ReadOnlySpan<char> name)
    {
        var hash = HashOf(name);
        var found = Lookup(name, hash, out var free);
        return found != 0 ? found - 1 : Insert(name.ToString(), hash, free);
    }

    /// <summary>The id of a name, -1 when no table of this checker has it.</summary>
    internal int Find(string name) => Find(name.AsSpan());

    /// <summary>The id of a name given by its characters, -1 when no table of this checker has it (nothing is added; module AR2).</summary>
    internal int Find(ReadOnlySpan<char> name) => Lookup(name, HashOf(name), out _) - 1;

    private int Add(string name)
    {
        if (count == names.Length)
        {
            Array.Resize(ref names, count * 2);
        }
        names[count] = name;
        return count++;
    }
}
