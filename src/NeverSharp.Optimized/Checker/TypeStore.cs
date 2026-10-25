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
    }

    private TypeRecord[] types = new TypeRecord[256];
    private int typeCount; // types[0] is the undefined handle
    private readonly List<ObjectTypeData> objects = [null!];
    private UnionData[] unions = new UnionData[64];
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
        if (unionCount + 1 == unions.Length)
        {
            Array.Resize(ref unions, unions.Length * 2);
        }
        unionCount++;
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
    internal ReadOnlySpan<Type> Elements(TypeList list) => lists.AsSpan(list.Start, list.Count);
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
    public bool BaseTypesResolving;
    public Type ResolvedBaseConstructorType;

    // TypeReference / GenericType
    public Type Target;
    public TypeList ResolvedTypeArguments;
    public bool HasResolvedTypeArguments;
    public Dictionary<string, Type>? Instantiations;
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
    public Dictionary<string, Type>? Instantiations;
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
        public string EscapedName;
        public List<NodeRef>? Declarations;
        public NodeRef ValueDeclaration;
        public SymbolRef Parent;
        public SymTable? Members;   // checker-owned symbol tables (C1: SymTable, an ordered map like TypeScript's Map)
        public SymTable? Exports;
        public bool ConstEnumOnlyModule;
        public SymbolRef ExportSymbol;
    }

    private TransientSymbolRecord[] symbols = new TransientSymbolRecord[64];
    private int count;

    internal int Count => count;

    /// <summary>checker.ts createSymbol(flags, name, checkFlags).</summary>
    internal SymbolRef Create(SymbolFlags flags, string escapedName, CheckFlags checkFlags = CheckFlags.None)
    {
        if (count + 1 == symbols.Length)
        {
            Array.Resize(ref symbols, symbols.Length * 2);
        }
        count++;
        symbols[count] = new TransientSymbolRecord { Flags = flags | SymbolFlags.Transient, CheckFlags = checkFlags, EscapedName = escapedName };
        return new SymbolRef(0, count);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ref TransientSymbolRecord Record(SymbolRef symbol) => ref symbols[symbol.Index];
}

/// <summary>
/// A symbol table the checker owns (types.ts SymbolTable: a Map from escaped name to symbol, iterated in insertion
/// order; Map.set on an existing key keeps its position). Binder tables are read through a cached copy of this kind
/// (<see cref="Checker.TableOf"/>), so the checker sees one table type for binder and transient symbols.
/// </summary>
internal sealed class SymTable
{
    private readonly Dictionary<string, int> index;
    private readonly List<string> names;
    private readonly List<SymbolRef> values;

    internal SymTable(int capacity = 0)
    {
        index = new Dictionary<string, int>(capacity, StringComparer.Ordinal);
        names = new List<string>(capacity);
        values = new List<SymbolRef>(capacity);
    }

    /// <summary>new Map(other): a copy in the same order.</summary>
    internal SymTable(SymTable other)
    {
        index = new Dictionary<string, int>(other.index, StringComparer.Ordinal);
        names = new List<string>(other.names);
        values = new List<SymbolRef>(other.values);
    }

    internal int Count => names.Count;

    /// <summary>Map.get (None when absent).</summary>
    internal SymbolRef Get(string name) => index.TryGetValue(name, out var i) ? values[i] : SymbolRef.None;

    internal bool Has(string name) => index.ContainsKey(name);

    /// <summary>Map.set.</summary>
    internal void Set(string name, SymbolRef symbol)
    {
        if (index.TryGetValue(name, out var i))
        {
            values[i] = symbol;
            return;
        }
        index.Add(name, names.Count);
        names.Add(name);
        values.Add(symbol);
    }

    internal string NameAt(int i) => names[i];

    internal SymbolRef ValueAt(int i) => values[i];
}
