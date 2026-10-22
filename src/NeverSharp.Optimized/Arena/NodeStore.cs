// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's node arena (pre-registration §5.4), step O6b variant B: structure of arrays.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// The nodes of one parse. A <see cref="Node"/> is an index into this store; everything about a node is read and
/// written through it, so the layout below can change without touching the parser or the factory.
/// <para>
/// Layout (structure of arrays): one column per base field, indexed by the node: kind (16 bits), pos, end, flags,
/// transform flags, and where the node's fields start in the two field columns. The fields of a node are a block
/// of int slots (child handles and scalar fields) and a block of object slots (strings, node lists, other objects),
/// sized per kind and laid out per node class by the generated part (Generated/NodeStore.g.cs). Fields set on few
/// nodes (parent, modifier-flags cache, original node, jsDoc) are sparse node-indexed arrays of the store, allocated
/// on first use; emit nodes are a dictionary. Node 0 is <see cref="Node.None"/>: its fields read as undefined.
/// </para>
/// <para>
/// Memory: the columns without references (everything but the object slots) are large segments shared by successive
/// parses (<see cref="Arena"/>, from a pool: the counterpart of typescript-go's node arenas, internal/ast/ast.go, and
/// its parser pool). A store fills the free tail of an arena's segments from its base index on; when its parse is
/// complete (the parser stores the parse diagnostics on the source file, the last step of parser.ts
/// parseSourceFileWorker and parseJsonText), it keeps exactly the room it used and returns the arena with the rest
/// (<see cref="Seal"/>). The object slots are filled in the arena's reusable buffer and copied into an array of the
/// exact size then. A store that outgrows its room moves to new segments; nothing else is allocated per parse.
/// </para>
/// <para>
/// Invariant: columns are zero beyond a store's fill level (kinds, positions, ends and field starts excepted: they
/// are written for every node created). Creating a node therefore writes only those; speculative parsing discards
/// the nodes it created by truncating to a <see cref="Mark"/> taken before (<see cref="GetMark"/>,
/// <see cref="Truncate"/>), which clears what those nodes wrote.
/// </para>
/// </summary>
internal sealed partial class NodeStore
{
    /// <summary>The fill levels of the store (relative to its bases); truncating to it discards everything created after it.</summary>
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

    /// <summary>Segments that parses fill one after another (how far they are used), and a buffer for object slots.</summary>
    private sealed class Arena
    {
        public ushort[] Kinds = [];
        public int[] Positions = [];
        public int[] Ends = [];
        public NodeFlags[] Flags = [];
        public TransformFlags[] TransformFlags = [];
        public int[] SlotStarts = [];
        public int[] RefStarts = [];
        public int[] Slots = [];
        public int NodeTop;
        public int SlotTop;

        // Zero while the arena is in the pool.
        public object?[] Refs = [];

        public void Allocate(int nodes, int slots)
        {
            Kinds = new ushort[nodes];
            Positions = new int[nodes];
            Ends = new int[nodes];
            Flags = new NodeFlags[nodes];
            TransformFlags = new TransformFlags[nodes];
            SlotStarts = new int[nodes];
            RefStarts = new int[nodes];
            Slots = new int[slots];
            NodeTop = 0;
            SlotTop = 0;
        }
    }

    // Nodes per segment (int columns of 128 KB, allocated outside the young generation); larger parses get
    // segments of twice their size.
    private const int SegmentNodes = 1 << 15;

    // Arenas not in use (about one per parse that ran at the same time).
    private static readonly Stack<Arena> Pool = new();

    // The arena whose free tail this store is filling (null when sealed).
    private Arena? owner;

    // Node i of this store is element nodeBase + i of the node columns; its fields start at absolute indices of the
    // slot column and at indices of the object slots (slotStarts, refStarts). Limits: where the room of this store
    // ends in the node and slot columns.
    private int nodeBase;
    private int count;
    private int nodeLimit;
    private ushort[] kinds;
    private int[] positions;
    private int[] ends;
    private NodeFlags[] flags;
    private TransformFlags[] transformFlags;
    private int[] slotStarts;
    private int[] refStarts;
    private int[] slots;
    private int slotBase;
    private int slotCount;
    private int slotLimit;
    private object?[] refs;
    private int refCount;

    // Sparse node-indexed arrays (index: the node's handle).
    private int[]? parents;
    private ModifierFlags[]? modifierFlagsCaches;
    private int[]? originals;
    private List<Node>?[]? jsDocs;

    // TypeScript's node.emitNode (factory data the parser does not create); keyed by node index, rarely used.
    private Dictionary<int, object>? emitNodes;

    /// <summary>A store sized for a source text of the given length (it grows as needed).</summary>
    public NodeStore(int textLength)
    {
        // About 11.4 characters, 1.5 int slots and 1.0 object slots per node in the tuning corpus; a generous estimate
        // keeps moves rare, and what a parse does not use is left to the next one.
        var estimate = 32 + (textLength / 8);
        Arena? a;
        lock (Pool)
        {
            Pool.TryPop(out a);
        }
        a ??= new Arena();
        if (a.Kinds.Length - a.NodeTop < estimate || a.Slots.Length - a.SlotTop < estimate * 3 / 2)
        {
            var nodes = Math.Max(SegmentNodes, estimate * 2);
            a.Allocate(nodes, nodes * 3 / 2);
        }
        if (a.Refs.Length < estimate * 3 / 2)
        {
            a.Refs = new object?[Math.Max(1024, estimate * 2)];
        }
        owner = a;
        kinds = a.Kinds;
        positions = a.Positions;
        ends = a.Ends;
        flags = a.Flags;
        transformFlags = a.TransformFlags;
        slotStarts = a.SlotStarts;
        refStarts = a.RefStarts;
        slots = a.Slots;
        refs = a.Refs;
        nodeBase = a.NodeTop;
        slotBase = a.SlotTop;
        nodeLimit = kinds.Length;
        slotLimit = slots.Length;
        // Node 0 is Node.None (kind Unknown, pos and end -1, no fields).
        kinds[nodeBase] = 0;
        positions[nodeBase] = -1;
        ends[nodeBase] = -1;
        slotStarts[nodeBase] = slotBase;
        refStarts[nodeBase] = 0;
        count = 1;
    }

    /// <summary>The number of nodes in use (including <see cref="Node.None"/>).</summary>
    internal int Count => count;

    /// <summary>Element <paramref name="index"/> of <paramref name="array"/> without a bounds check (handles are below the fill level).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref T At<T>(T[] array, int index) => ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), (nint)(uint)index);

    /// <summary>Entry <paramref name="index"/> of a constant table without a bounds check (the tables have an entry per kind).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T Entry<T>(ReadOnlySpan<T> table, int index) => Unsafe.Add(ref MemoryMarshal.GetReference(table), (nint)(uint)index);

    /// <summary>A new node of the given kind: pos and end -1, flags none, every field undefined (utilities.ts: function Node).</summary>
    internal Node Create(SyntaxKind kind)
    {
        var slotSize = Entry(SlotCounts, (int)kind);
        var refSize = Entry(RefCounts, (int)kind);
        if (nodeBase + count == nodeLimit || slotBase + slotCount + slotSize > slotLimit || refCount + refSize > refs.Length)
        {
            Move(slotSize, refSize);
        }
        var index = count;
        var i = nodeBase + index;
        At(kinds, i) = (ushort)kind;
        At(positions, i) = -1;
        At(ends, i) = -1;
        At(slotStarts, i) = slotBase + slotCount;
        At(refStarts, i) = refCount;
        slotCount += slotSize;
        refCount += refSize;
        count = index + 1;
        return new Node(index);
    }

    /// <summary>
    /// Moves the store to new columns with room for one more node and the given fields: new segments (and a larger
    /// buffer) of its arena while it fills one, columns of its own once it is sealed.
    /// </summary>
    private void Move(int slotSize, int refSize)
    {
        var a = owner;
        var n = count;
        var nodeSize = Math.Max(16, n * 2);
        var slotNeed = Math.Max(16, (slotCount + slotSize) * 2);
        if (a is not null)
        {
            nodeSize = Math.Max(SegmentNodes, nodeSize);
            slotNeed = Math.Max(SegmentNodes * 3 / 2, slotNeed);
        }
        if (refCount + refSize > refs.Length)
        {
            Array.Resize(ref refs, Math.Max(16, (refCount + refSize) * 2));
        }
        if (nodeBase + n == nodeLimit || slotBase + slotCount + slotSize > slotLimit)
        {
            var newKinds = new ushort[nodeSize];
            var newPositions = new int[nodeSize];
            var newEnds = new int[nodeSize];
            var newFlags = new NodeFlags[nodeSize];
            var newTransformFlags = new TransformFlags[nodeSize];
            var newSlotStarts = new int[nodeSize];
            var newRefStarts = new int[nodeSize];
            var newSlots = new int[slotNeed];
            Array.Copy(kinds, nodeBase, newKinds, 0, n);
            Array.Copy(positions, nodeBase, newPositions, 0, n);
            Array.Copy(ends, nodeBase, newEnds, 0, n);
            Array.Copy(flags, nodeBase, newFlags, 0, n);
            Array.Copy(transformFlags, nodeBase, newTransformFlags, 0, n);
            Array.Copy(refStarts, nodeBase, newRefStarts, 0, n);
            for (var k = 0; k < n; k++)
            {
                newSlotStarts[k] = slotStarts[nodeBase + k] - slotBase;
            }
            Array.Copy(slots, slotBase, newSlots, 0, slotCount);
            kinds = newKinds;
            positions = newPositions;
            ends = newEnds;
            flags = newFlags;
            transformFlags = newTransformFlags;
            slotStarts = newSlotStarts;
            refStarts = newRefStarts;
            slots = newSlots;
            nodeBase = 0;
            slotBase = 0;
            nodeLimit = nodeSize;
            slotLimit = slotNeed;
            if (a is not null)
            {
                // The old segments stay with the stores in them; the arena continues on the new ones.
                a.Kinds = newKinds;
                a.Positions = newPositions;
                a.Ends = newEnds;
                a.Flags = newFlags;
                a.TransformFlags = newTransformFlags;
                a.SlotStarts = newSlotStarts;
                a.RefStarts = newRefStarts;
                a.Slots = newSlots;
                a.NodeTop = 0;
                a.SlotTop = 0;
            }
        }
    }

    /// <summary>
    /// The parse is complete: the store keeps the room it used, copies its object slots into an array of their exact
    /// size, and returns its arena to the pool. Later changes work as before; a new node moves the store to columns
    /// of its own.
    /// </summary>
    private void Seal()
    {
        var a = owner;
        if (a is null)
        {
            return;
        }
        owner = null;
        nodeLimit = nodeBase + count;
        slotLimit = slotBase + slotCount;
        a.NodeTop = nodeLimit;
        a.SlotTop = slotLimit;
        var exactRefs = new object?[refCount];
        Array.Copy(refs, exactRefs, refCount);
        new Span<object?>(refs, 0, refCount).Clear();
        a.Refs = refs;
        refs = exactRefs;
        lock (Pool)
        {
            Pool.Push(a);
        }
        SealSparse(ref parents);
        SealSparse(ref modifierFlagsCaches);
        SealSparse(ref originals);
        SealSparse(ref jsDocs);
    }

    internal Mark GetMark() => new(count, slotCount, refCount);

    /// <summary>Discards every node created after <paramref name="mark"/> (speculative parsing that is rolled back).</summary>
    internal void Truncate(Mark mark)
    {
        var nodes = count - mark.Nodes;
        if (nodes == 0)
        {
            return;
        }
        Debug.Assert(nodes > 0 && mark.Slots <= slotCount && mark.Refs <= refCount, "Mark is beyond the store's fill level");
        // Restore the invariant: everything beyond the fill level is zero.
        new Span<NodeFlags>(flags, nodeBase + mark.Nodes, nodes).Clear();
        new Span<TransformFlags>(transformFlags, nodeBase + mark.Nodes, nodes).Clear();
        TruncateSparse(parents, mark.Nodes);
        TruncateSparse(modifierFlagsCaches, mark.Nodes);
        TruncateSparse(originals, mark.Nodes);
        TruncateSparse(jsDocs, mark.Nodes);
        new Span<int>(slots, slotBase + mark.Slots, slotCount - mark.Slots).Clear();
        new Span<object?>(refs, mark.Refs, refCount - mark.Refs).Clear();
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

    public SyntaxKind Kind(Node node) => (SyntaxKind)At(kinds, nodeBase + node.Index);

    public int Pos(Node node) => At(positions, nodeBase + node.Index);

    public void SetPos(Node node, int value) => At(positions, nodeBase + node.Index) = value;

    public int End(Node node) => At(ends, nodeBase + node.Index);

    public void SetEnd(Node node, int value) => At(ends, nodeBase + node.Index) = value;

    public NodeFlags Flags(Node node) => At(flags, nodeBase + node.Index);

    public void SetFlags(Node node, NodeFlags value) => At(flags, nodeBase + node.Index) = value;

    public Node Parent(Node node) => new(Sparse(parents, node.Index));

    public void SetParent(Node node, Node value) => SetSparse(ref parents, node.Index, value.Index);

    public TransformFlags TransformFlags(Node node) => At(transformFlags, nodeBase + node.Index);

    public void SetTransformFlags(Node node, TransformFlags value) => At(transformFlags, nodeBase + node.Index) = value;

    public ModifierFlags ModifierFlagsCache(Node node) => Sparse(modifierFlagsCaches, node.Index);

    public void SetModifierFlagsCache(Node node, ModifierFlags value) => SetSparse(ref modifierFlagsCaches, node.Index, value);

    public Node Original(Node node) => new(Sparse(originals, node.Index));

    public void SetOriginal(Node node, Node value) => SetSparse(ref originals, node.Index, value.Index);

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

    //// Sparse node-indexed arrays of rarely set fields: allocated on the first value that is not the default, as long as needed.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T? Sparse<T>(T[]? array, int index) => array is not null && (uint)index < (uint)array.Length ? array[index] : default;

    private void SetSparse<T>(ref T[]? array, int index, T value)
    {
        if (array is null || (uint)index >= (uint)array.Length)
        {
            if (EqualityComparer<T>.Default.Equals(value, default))
            {
                return;
            }
            Array.Resize(ref array, Math.Max(Math.Max(count + 16, index + 1), (array?.Length ?? 0) * 2));
        }
        At(array, index) = value;
    }

    private void TruncateSparse<T>(T[]? array, int from)
    {
        if (array is not null && from < array.Length)
        {
            new Span<T>(array, from, Math.Min(count, array.Length) - from).Clear();
        }
    }

    private void SealSparse<T>(ref T[]? array)
    {
        if (array is not null && array.Length > count)
        {
            Array.Resize(ref array, count);
        }
    }

    private static void ThrowNoField(SyntaxKind kind, string field)
    {
        throw new InvalidOperationException($"{kind} has no field {field}");
    }
}
