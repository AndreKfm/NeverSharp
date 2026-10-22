// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's node handle (pre-registration §5.4: nodes referenced by int index).

namespace NeverSharp.Optimized;

/// <summary>
/// A node: a handle into the <see cref="NodeStore"/> of one parse. Kind, positions, flags and every field of the
/// node are read and written through the store. <see cref="None"/> (the default value) is TypeScript's
/// `undefined` where a node is expected.
/// </summary>
internal readonly struct Node : IEquatable<Node>
{
    internal readonly int Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node(int index)
    {
        Index = index;
    }

    /// <summary>No node (TypeScript: undefined).</summary>
    public static Node None => default;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsNone => Index == 0;

    /// <summary>A node held as an object (TypeScript's union-typed parameters such as string | Node); null is <see cref="None"/>.</summary>
    internal static Node FromObject(object? value) => value is Node node ? node : default;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(Node left, Node right) => left.Index == right.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(Node left, Node right) => left.Index != right.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(Node other) => Index == other.Index;

    public override bool Equals(object? obj) => obj is Node other && Index == other.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override int GetHashCode() => Index;

    public override string ToString() => IsNone ? "None" : "Node#" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
