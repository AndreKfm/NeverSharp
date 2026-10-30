// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
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
        public SymTable? PropertyCache;                                     // types.ts propertyCache
        public SymTable? PropertyCacheWithoutObjectFunctionPropertyAugment; // types.ts propertyCacheWithoutObjectFunctionPropertyAugment
        public List<SymbolRef>? ResolvedProperties;                         // types.ts resolvedProperties
    }

    private TypeRecord[] types = new TypeRecord[256];
    private int typeCount; // types[0] is the undefined handle
    private readonly List<ObjectTypeData> objects = [null!];
    private readonly Chunks<UnionData> unions = new();
    private int unionCount;
    private readonly List<TypeParameterData> typeParameters = [null!];

    /// <summary>A new object type (createObjectType) with its data record.</summary>
    internal Type CreateObject(ObjectFlags objectFlags, SymbolRef symbol)
    {
        objects.Add(new ObjectTypeData());
        return Create(TypeFlags.Object, objectFlags, symbol, objects.Count - 1);
    }

    /// <summary>The data of an object type.</summary>
    internal ObjectTypeData Object(Type type) => objects[types[type.Id].Data];

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
        typeParameters.Add(new TypeParameterData());
        return Create(TypeFlags.TypeParameter, ObjectFlags.None, symbol, typeParameters.Count - 1);
    }

    /// <summary>The data of a type parameter.</summary>
    internal TypeParameterData TypeParameter(Type type) => typeParameters[types[type.Id].Data];

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
/// types.ts ObjectType and its kinds (InterfaceType, TypeReference, ResolvedType members), typescript-go's
/// ObjectType/InterfaceType/TypeReference data. Module C1 keeps one record class for all object kinds (the resolved
/// members are lists the member resolution builds once); later optimization may split it into per-kind arrays.
/// </summary>
internal sealed class ObjectTypeData
{
    // ResolvedType (resolveStructuredTypeMembers)
    public bool MembersResolved;
    public SymTable? Members;
    public List<SymbolRef>? Properties;
    public List<Signature>? CallSignatures;
    public List<Signature>? ConstructSignatures;
    public List<IndexInfo>? IndexInfos;

    // InterfaceType
    public TypeList TypeParameters;
    public TypeList OuterTypeParameters;
    public TypeList LocalTypeParameters;
    public bool HasTypeParameters; // typeParameters !== undefined
    public Type ThisType;
    public bool DeclaredMembersResolved;
    public List<SymbolRef>? DeclaredProperties;
    public List<Signature>? DeclaredCallSignatures;
    public List<Signature>? DeclaredConstructSignatures;
    public List<IndexInfo>? DeclaredIndexInfos;
    public List<Type>? ResolvedBaseTypes;
    public bool BaseTypesResolved; // baseTypesResolved
    public Type ResolvedBaseConstructorType;

    // TypeReference / GenericType
    public Type Target;
    public TypeList ResolvedTypeArguments;
    public bool HasResolvedTypeArguments;
    public bool HasInstantiations; // instantiations !== undefined (the entries: Checker.typeInstantiations, module CO2)
    public NodeRef Node; // deferred type reference node (C3)

    // AnonymousType / DeferredTypeReference / MappedType (C3): the instantiation's mapper (Target: the instantiated type)
    public TypeMapper? Mapper;

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

/// <summary>types.ts TypeParameter.</summary>
internal sealed class TypeParameterData
{
    public Type Constraint;
    public Type Default;
    public bool IsThisType;
    public Type Target;          // the type parameter this one is an instantiation of (C3)
    public TypeMapper? Mapper;   // the mapper of that instantiation (C3)
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
    public TypeMapper? Mapper;
    public TypeMapper? CombinedMapper;
    public Type ResolvedTrueType;
    public Type ResolvedFalseType;
    public Type ResolvedInferredTrueType;
    public Type ResolvedDefaultConstraint;
    public Type ResolvedConstraintOfDistributive; // None: not computed; noConstraintType: false
}

/// <summary>types.ts IndexInfo.</summary>
internal sealed class IndexInfo
{
    public Type KeyType;
    public Type Type;
    public bool IsReadonly;
    public NodeRef Declaration;
    public List<NodeRef>? Components; // the declarations with computed property names the index info is derived from
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
        public SymbolRef[] Parameters;  // the parameter symbols (C1: an array per signature)
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
        public SymTable? Members;   // checker-owned symbol tables (C1: SymTable, an ordered map like TypeScript's Map)
        public SymTable? Exports;
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
/// A symbol table the checker owns (types.ts SymbolTable: a Map from escaped name to symbol, iterated in insertion
/// order; Map.set on an existing key keeps its position). Binder tables are read through a cached copy of this kind
/// (<see cref="Checker.TableOf"/>), so the checker sees one table type for binder and transient symbols.
/// Layout (module CO1): the entries (name, symbol) in insertion order in one array; up to <see cref="LinearLimit"/>
/// entries are found by a scan, larger tables by an open-addressing index (linear probing, load at most 1/2, entry
/// number + 1 per slot, 0 free). No per-entry objects. Module CO2: names are ids of the checker's <see cref="NameTable"/>,
/// so the entries hold no references for the collector to trace.
/// </summary>
internal sealed class SymTable
{
    private const int LinearLimit = 8;

    private struct Entry
    {
        public int Name;
        public SymbolRef Value;
    }

    private readonly NameTable names;
    private Entry[] entries;
    private int[]? buckets; // null: at most LinearLimit entries, found by a scan
    private int count;

    internal SymTable(NameTable names, int capacity = 0)
    {
        this.names = names;
        entries = capacity == 0 ? [] : new Entry[capacity];
    }

    /// <summary>new Map(other): a copy in the same order.</summary>
    internal SymTable(SymTable other)
    {
        names = other.names;
        count = other.count;
        entries = other.entries.AsSpan(0, count).ToArray();
        buckets = other.buckets is null ? null : (int[])other.buckets.Clone();
    }

    internal int Count
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => count;
    }

    /// <summary>Map.get (None when absent).</summary>
    internal SymbolRef Get(string name)
    {
        var id = names.Find(name);
        return id < 0 ? SymbolRef.None : Get(id);
    }

    /// <summary>Map.get by name id (None when absent).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SymbolRef Get(int name)
    {
        var i = Find(name);
        return i >= 0 ? entries[i].Value : SymbolRef.None;
    }

    internal bool Has(string name)
    {
        var id = names.Find(name);
        return id >= 0 && Find(id) >= 0;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool Has(int name) => Find(name) >= 0;

    /// <summary>Map.set.</summary>
    internal void Set(string name, SymbolRef symbol) => Set(names.Intern(name), symbol);

    /// <summary>Map.set by name id.</summary>
    internal void Set(int name, SymbolRef symbol)
    {
        var i = Find(name);
        if (i >= 0)
        {
            entries[i].Value = symbol;
            return;
        }
        if (count == entries.Length)
        {
            Resize(Math.Max(4, count * 2));
        }
        entries[count] = new Entry { Name = name, Value = symbol };
        count++;
        if (buckets is not null)
        {
            if (count * 2 > buckets.Length)
            {
                Rehash(buckets.Length * 2);
            }
            else
            {
                Insert(buckets, count - 1);
            }
        }
        else if (count > LinearLimit)
        {
            Rehash(BucketCountFor(entries.Length));
        }
    }

    /// <summary>Makes room for <paramref name="capacity"/> entries (no change to the contents).</summary>
    internal void EnsureCapacity(int capacity)
    {
        if (capacity > entries.Length)
        {
            Resize(capacity);
            if (buckets is not null && capacity * 2 > buckets.Length)
            {
                Rehash(BucketCountFor(capacity));
            }
        }
    }

    /// <summary>Map.clear (the arrays are kept for reuse).</summary>
    internal void Clear()
    {
        if (buckets is not null)
        {
            if (count * 8 >= buckets.Length)
            {
                Array.Clear(buckets);
            }
            else
            {
                // A large index of a table reused with few entries: free just their slots, the last entry first (with
                // linear probing and no deletions, the probe path of an entry only crosses slots of earlier entries).
                var mask = buckets.Length - 1;
                for (var entry = count - 1; entry >= 0; entry--)
                {
                    var slot = Hash(entries[entry].Name) & mask;
                    while (buckets[slot] != entry + 1)
                    {
                        slot = (slot + 1) & mask;
                    }
                    buckets[slot] = 0;
                }
            }
        }
        Array.Clear(entries, 0, count);
        count = 0;
    }

    /// <summary>Map.delete: true when the name was present (the entries after it keep their order).</summary>
    internal bool Delete(string name)
    {
        var id = names.Find(name);
        var i = id < 0 ? -1 : Find(id);
        if (i < 0)
        {
            return false;
        }
        Array.Copy(entries, i + 1, entries, i, count - i - 1);
        count--;
        entries[count] = default;
        if (buckets is not null)
        {
            if (count <= LinearLimit)
            {
                buckets = null;
            }
            else
            {
                Rehash(buckets.Length);
            }
        }
        return true;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal string NameAt(int i) => names[entries[i].Name];

    /// <summary>The name id of entry <paramref name="i"/>.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int NameIdAt(int i) => entries[i].Name;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SymbolRef ValueAt(int i) => entries[i].Value;

    // The entry number of the name, -1 when absent.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int Find(int name)
    {
        var b = buckets;
        var e = entries;
        if (b is null)
        {
            for (var i = 0; i < count; i++)
            {
                if (e[i].Name == name)
                {
                    return i;
                }
            }
            return -1;
        }
        var mask = b.Length - 1;
        for (var slot = Hash(name) & mask; ; slot = (slot + 1) & mask)
        {
            var entry = b[slot];
            if (entry == 0)
            {
                return -1;
            }
            if (e[entry - 1].Name == name)
            {
                return entry - 1;
            }
        }
    }

    private void Resize(int capacity) => Array.Resize(ref entries, capacity);

    private void Rehash(int bucketCount)
    {
        var b = new int[bucketCount];
        for (var i = 0; i < count; i++)
        {
            Insert(b, i);
        }
        buckets = b;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void Insert(int[] b, int entry)
    {
        var mask = b.Length - 1;
        var slot = Hash(entries[entry].Name) & mask;
        while (b[slot] != 0)
        {
            slot = (slot + 1) & mask;
        }
        b[slot] = entry + 1;
    }

    // a power of two with load at most 1/2
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int BucketCountFor(int capacity) => (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(capacity * 2, 32));

    // a mix of the name id for the low bits the index uses
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int Hash(int name)
    {
        var h = (uint)name * 0x9E3779B1u;
        h ^= h >> 15;
        return (int)(h & 0x7FFFFFFF);
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
    private readonly Dictionary<string, int> ids = new(StringComparer.Ordinal);
    private string[] names = new string[1024];
    private int count;

    internal int Count => count;

    /// <summary>The name of an id.</summary>
    internal string this[int id]
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => names[id];
    }

    /// <summary>The id of a name, added if new.</summary>
    internal int Intern(string name)
    {
        ref var id = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(ids, name, out var exists);
        if (!exists)
        {
            id = Add(name);
        }
        return id;
    }

    /// <summary>The id of a name given by its characters, added if new.</summary>
    internal int Intern(ReadOnlySpan<char> name)
    {
        var lookup = ids.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(name, out var id))
        {
            return id;
        }
        var text = name.ToString();
        id = Add(text);
        ids.Add(text, id);
        return id;
    }

    /// <summary>The id of a name, -1 when no table of this checker has it.</summary>
    internal int Find(string name) => ids.TryGetValue(name, out var id) ? id : -1;

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
