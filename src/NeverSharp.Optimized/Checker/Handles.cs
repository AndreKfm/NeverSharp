// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's handles for checker data (module C0, docs/phase3-checker-layout.md).

using System.Globalization;

namespace NeverSharp.Optimized;

/// <summary>
/// A type (types.ts: Type; typescript-go: *checker.Type): a handle into the <see cref="TypeStore"/> of one checker.
/// The handle is also the type's id (checker.ts: <c>type.id = ++typeCount</c> per checker, in creation order), so
/// orderings by type id (union and intersection members) are orderings by handle. <see cref="None"/> is undefined.
/// </summary>
internal readonly struct Type : IEquatable<Type>
{
    internal readonly int Id;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Type(int id)
    {
        Id = id;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static Type None => default;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsNone => Id == 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(Type left, Type right) => left.Id == right.Id;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(Type left, Type right) => left.Id != right.Id;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(Type other) => Id == other.Id;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override bool Equals(object? obj) => obj is Type other && Id == other.Id;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override int GetHashCode() => Id;

    public override string ToString() => IsNone ? "None" : "Type#" + Id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// A signature (types.ts: Signature; typescript-go: *checker.Signature): a handle into the <see cref="SignatureStore"/>
/// of one checker; also its id. <see cref="None"/> is undefined.
/// </summary>
internal readonly struct Signature : IEquatable<Signature>
{
    internal readonly int Id;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Signature(int id)
    {
        Id = id;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static Signature None => default;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsNone => Id == 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(Signature left, Signature right) => left.Id == right.Id;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(Signature left, Signature right) => left.Id != right.Id;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(Signature other) => Id == other.Id;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override bool Equals(object? obj) => obj is Signature other && Id == other.Id;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override int GetHashCode() => Id;

    public override string ToString() => IsNone ? "None" : "Signature#" + Id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// A symbol as the checker sees it (types.ts: Symbol, binder-made or transient): <see cref="File"/> 1..n is the binder
/// symbol <see cref="Index"/> of the program's n-th source file (<see cref="Program.SourceFiles"/>, 1-based; the
/// handle of that file's <see cref="BinderStore"/>), <see cref="File"/> 0 a transient symbol the checker created
/// (checker.ts createSymbol with SymbolFlags.Transient: merged, instantiated, synthetic symbols) in its own
/// <see cref="TransientSymbolStore"/>. Binder symbols of shared library files are never written by a checker; what a
/// checker computes about any symbol lives in its link stores. <see cref="None"/> (both 0) is undefined.
/// </summary>
internal readonly struct SymbolRef : IEquatable<SymbolRef>
{
    internal readonly int File;
    internal readonly int Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SymbolRef(int file, int index)
    {
        File = file;
        Index = index;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static SymbolRef None => default;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsNone => Index == 0;

    /// <summary>Whether the checker created the symbol (SymbolFlags.Transient).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsTransient => File == 0 && Index != 0;

    /// <summary>The binder symbol (valid when not transient).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public Symbol BinderSymbol => new(Index);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(SymbolRef left, SymbolRef right) => left.File == right.File && left.Index == right.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(SymbolRef left, SymbolRef right) => !(left == right);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(SymbolRef other) => this == other;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override bool Equals(object? obj) => obj is SymbolRef other && this == other;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override int GetHashCode() => (File * 0x3FFFFFF) ^ Index;

    public override string ToString() => IsNone ? "None" : IsTransient
        ? "Transient#" + Index.ToString(CultureInfo.InvariantCulture)
        : "Symbol#" + File.ToString(CultureInfo.InvariantCulture) + ":" + Index.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// A node of a program (Track O's <see cref="Node"/> handles are per file): <see cref="File"/> 1..n is the program's
/// n-th source file (1-based), <see cref="Node"/> the handle in that file's node store. <see cref="None"/> is undefined.
/// </summary>
internal readonly struct NodeRef : IEquatable<NodeRef>
{
    internal readonly int File;
    internal readonly Node Node;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal NodeRef(int file, Node node)
    {
        File = file;
        Node = node;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static NodeRef None => default;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsNone => Node.IsNone;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(NodeRef left, NodeRef right) => left.File == right.File && left.Node == right.Node;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(NodeRef left, NodeRef right) => !(left == right);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(NodeRef other) => this == other;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override bool Equals(object? obj) => obj is NodeRef other && this == other;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override int GetHashCode() => (File * 0x3FFFFFF) ^ Node.Index;

    public override string ToString() => IsNone ? "None" : "Node#" + File.ToString(CultureInfo.InvariantCulture) + ":" + Node.Index.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// A list of types (TypeScript: <c>Type[]</c>; typescript-go: <c>[]*Type</c>): a range of the <see cref="TypeStore"/>'s
/// list arena, immutable once created. <see cref="Empty"/> has no elements.
/// </summary>
internal readonly struct TypeList : IEquatable<TypeList>
{
    internal readonly int Start;
    internal readonly int Count;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal TypeList(int start, int count)
    {
        Start = start;
        Count = count;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static TypeList Empty => default;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(TypeList left, TypeList right) => left.Start == right.Start && left.Count == right.Count;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(TypeList left, TypeList right) => !(left == right);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(TypeList other) => this == other;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override bool Equals(object? obj) => obj is TypeList other && this == other;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override int GetHashCode() => (Start * 31) ^ Count;
}
