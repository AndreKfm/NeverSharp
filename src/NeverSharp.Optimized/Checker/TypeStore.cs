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

    private TypeRecord[] types = new TypeRecord[256];
    private int typeCount; // types[0] is the undefined handle
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
    internal Type CreateIntrinsic(TypeFlags kind, string intrinsicName, ObjectFlags objectFlags = ObjectFlags.None)
    {
        if (intrinsicCount + 1 == intrinsics.Length)
        {
            Array.Resize(ref intrinsics, intrinsics.Length * 2);
        }
        intrinsicCount++;
        intrinsics[intrinsicCount] = new IntrinsicData { IntrinsicName = intrinsicName };
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
        public int Parameters;          // index of the parameter symbol list (C5 decides the list arena)
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
        public int Members;   // checker-owned symbol table handle (C1 decides the table layout)
        public int Exports;
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
