// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's handles for binder output (docs/phase2-binder-layout.md), like Node for nodes.

namespace NeverSharp.Optimized;

/// <summary>
/// A symbol (types.ts: Symbol): a handle into the <see cref="BinderStore"/> of one bound file. <see cref="None"/> (the
/// default value) is TypeScript's `undefined` where a symbol is expected.
/// </summary>
internal readonly struct Symbol : IEquatable<Symbol>
{
    internal readonly int Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Symbol(int index)
    {
        Index = index;
    }

    /// <summary>No symbol (TypeScript: undefined).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static Symbol None => default;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsNone => Index == 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(Symbol left, Symbol right) => left.Index == right.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(Symbol left, Symbol right) => left.Index != right.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(Symbol other) => Index == other.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override bool Equals(object? obj) => obj is Symbol other && Index == other.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override int GetHashCode() => Index;

    public override string ToString() => IsNone ? "None" : "Symbol#" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// A symbol table (types.ts: SymbolTable, a Map from escaped name to symbol in insertion order): a handle into the
/// <see cref="BinderStore"/>. <see cref="None"/> is `undefined`; an empty table is a handle with no entries.
/// </summary>
internal readonly struct SymbolTable : IEquatable<SymbolTable>
{
    internal readonly int Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SymbolTable(int index)
    {
        Index = index;
    }

    /// <summary>No table (TypeScript: undefined).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static SymbolTable None => default;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsNone => Index == 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(SymbolTable left, SymbolTable right) => left.Index == right.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(SymbolTable left, SymbolTable right) => left.Index != right.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(SymbolTable other) => Index == other.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override bool Equals(object? obj) => obj is SymbolTable other && Index == other.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override int GetHashCode() => Index;

    public override string ToString() => IsNone ? "None" : "SymbolTable#" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// A control-flow graph node (types.ts: FlowNode and its kinds, FlowLabel, FlowAssignment, ...): a handle into the
/// <see cref="BinderStore"/>. <see cref="None"/> is `undefined`.
/// </summary>
internal readonly struct FlowNode : IEquatable<FlowNode>
{
    internal readonly int Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FlowNode(int index)
    {
        Index = index;
    }

    /// <summary>No flow node (TypeScript: undefined).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static FlowNode None => default;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsNone => Index == 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(FlowNode left, FlowNode right) => left.Index == right.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(FlowNode left, FlowNode right) => left.Index != right.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(FlowNode other) => Index == other.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override bool Equals(object? obj) => obj is FlowNode other && Index == other.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override int GetHashCode() => Index;

    public override string ToString() => IsNone ? "None" : "FlowNode#" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
