// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's node arena (pre-registration §5.4), step O6a's layout.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// The nodes of one parse. A <see cref="Node"/> is an index into this store; everything about a node is read and
/// written through it, so the layout below can change without touching the parser or the factory (step O6b).
/// <para>
/// O6a layout: one record per node in a growable array (kind, flags, positions, parent, transform and modifier
/// flags, original node, and where the node's fields start); the fields of a node live in two side arrays, a
/// block of int slots (child handles and scalar fields) and a block of object slots (strings, node lists, other
/// objects), sized per kind and laid out per node class by the generated part (Generated/NodeStore.g.cs).
/// Record 0 is <see cref="Node.None"/>: its fields read as undefined.
/// </para>
/// <para>
/// Speculative parsing discards the nodes it created by truncating the arrays to a <see cref="Mark"/> taken before
/// (<see cref="GetMark"/>, <see cref="Truncate"/>).
/// </para>
/// </summary>
internal sealed partial class NodeStore
{
    private struct NodeRecord
    {
        public SyntaxKind Kind;
        public NodeFlags Flags;
        public int Pos;
        public int End;
        public int Parent;
        public int Slots;
        public int Refs;
        public TransformFlags TransformFlags;
        public ModifierFlags ModifierFlagsCache;
        public int Original;
    }

    /// <summary>The fill levels of the store's arrays; truncating to it discards everything created after it.</summary>
    internal readonly struct Mark
    {
        internal readonly int Nodes;
        internal readonly int Slots;
        internal readonly int Refs;

        internal Mark(int nodes, int slots, int refs)
        {
            Nodes = nodes;
            Slots = slots;
            Refs = refs;
        }
    }

    private NodeRecord[] records;
    private int count;
    private int[] slots;
    private int slotCount;
    private object?[] refs;
    private int refCount;

    // TypeScript's node.emitNode (factory data the parser does not create); keyed by node index, rarely used.
    private Dictionary<int, object>? emitNodes;

    /// <summary>A store sized for a source text of the given length (the arrays grow as needed).</summary>
    public NodeStore(int textLength)
    {
        // From the tuning corpus: about 11 characters, 1.5 int slots and 1.65 object slots per node; a generous first size
        // (a node per 8 characters) allocated the fewest bytes in total, growth included.
        var nodes = 32 + (textLength / 8);
        records = new NodeRecord[nodes];
        slots = new int[nodes * 3 / 2];
        refs = new object?[nodes * 7 / 4];
        // Record 0 is Node.None.
        records[0].Kind = SyntaxKind.Unknown;
        records[0].Pos = -1;
        records[0].End = -1;
        count = 1;
    }

    /// <summary>The number of records in use (including <see cref="Node.None"/>'s).</summary>
    internal int Count => count;

    /// <summary>A new node of the given kind: pos and end -1, flags none, every field undefined (utilities.ts: function Node).</summary>
    internal Node Create(SyntaxKind kind)
    {
        var slotSize = SlotCounts[(int)kind];
        var refSize = RefCounts[(int)kind];
        if (count == records.Length)
        {
            Array.Resize(ref records, records.Length * 2);
        }
        if (slotCount + slotSize > slots.Length)
        {
            Array.Resize(ref slots, Math.Max(slots.Length * 2, slotCount + slotSize));
        }
        if (refCount + refSize > refs.Length)
        {
            Array.Resize(ref refs, Math.Max(refs.Length * 2, refCount + refSize));
        }
        var index = count++;
        ref var r = ref records[index];
        r.Kind = kind;
        r.Flags = default;
        r.Pos = -1;
        r.End = -1;
        r.Parent = 0;
        r.Slots = slotCount;
        r.Refs = refCount;
        r.TransformFlags = default;
        r.ModifierFlagsCache = default;
        r.Original = 0;
        // Slots of discarded nodes are reused after a truncation: clear them.
        Array.Clear(slots, slotCount, slotSize);
        Array.Clear(refs, refCount, refSize);
        slotCount += slotSize;
        refCount += refSize;
        return new Node(index);
    }

    internal Mark GetMark() => new(count, slotCount, refCount);

    /// <summary>Discards every node created after <paramref name="mark"/> (speculative parsing that is rolled back).</summary>
    internal void Truncate(Mark mark)
    {
        Debug.Assert(mark.Nodes <= count && mark.Slots <= slotCount && mark.Refs <= refCount, "Mark is beyond the store's fill level");
        Array.Clear(refs, mark.Refs, refCount - mark.Refs);
        if (emitNodes is not null)
        {
            for (var i = mark.Nodes; i < count; i++)
            {
                emitNodes.Remove(i);
            }
        }
        count = mark.Nodes;
        slotCount = mark.Slots;
        refCount = mark.Refs;
    }

    //// Base fields (utilities.ts: function Node).

    public SyntaxKind Kind(Node node) => records[node.Index].Kind;

    public int Pos(Node node) => records[node.Index].Pos;

    public void SetPos(Node node, int value) => records[node.Index].Pos = value;

    public int End(Node node) => records[node.Index].End;

    public void SetEnd(Node node, int value) => records[node.Index].End = value;

    public NodeFlags Flags(Node node) => records[node.Index].Flags;

    public void SetFlags(Node node, NodeFlags value) => records[node.Index].Flags = value;

    public Node Parent(Node node) => new(records[node.Index].Parent);

    public void SetParent(Node node, Node value) => records[node.Index].Parent = value.Index;

    public TransformFlags TransformFlags(Node node) => records[node.Index].TransformFlags;

    public void SetTransformFlags(Node node, TransformFlags value) => records[node.Index].TransformFlags = value;

    public ModifierFlags ModifierFlagsCache(Node node) => records[node.Index].ModifierFlagsCache;

    public void SetModifierFlagsCache(Node node, ModifierFlags value) => records[node.Index].ModifierFlagsCache = value;

    public Node Original(Node node) => new(records[node.Index].Original);

    public void SetOriginal(Node node, Node value) => records[node.Index].Original = value.Index;

    public object? EmitNode(Node node) => emitNodes is not null && emitNodes.TryGetValue(node.Index, out var value) ? value : null;

    public void SetEmitNode(Node node, object? value)
    {
        if (value is null)
        {
            emitNodes?.Remove(node.Index);
            return;
        }
        (emitNodes ??= [])[node.Index] = value;
    }

    private static void ThrowNoField(SyntaxKind kind, string field)
    {
        throw new InvalidOperationException($"{kind} has no field {field}");
    }
}
