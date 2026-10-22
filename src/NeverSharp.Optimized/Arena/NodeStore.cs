// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's node arena (pre-registration §5.4), step O6b variant A's layout (compact records).

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// The nodes of one parse. A <see cref="Node"/> is an index into this store; everything about a node is read and
/// written through it, so the layout below can change without touching the parser or the factory.
/// <para>
/// O6b variant A layout (compact records): every node is a variable-length record in one int array, and its handle
/// is the record's offset. A record holds the kind word (kind in bits 0..15, the node's boolean fields in bits
/// 16..30, bit 31 set when the node has side table entries), flags, pos, end and transform flags, then, for kinds
/// with object fields, the index of the node's first object slot, then one int per child handle or scalar field.
/// Other objects live in a second array of object slots; node lists and texts are handles in int slots. The record size and the place of
/// each field depend on the kind (Generated/NodeStore.g.cs). The record at offset 0 is <see cref="Node.None"/>:
/// its fields read as undefined.
/// </para>
/// <para>
/// Rare fields (parent, original, the modifier flags cache, the emit node, and the members the parser sets on few
/// nodes of a class, such as JSDoc, optional tokens and rarely present lists; the generator lists them) live in a
/// side table keyed by node and field, so they cost nothing on the nodes that lack them.
/// </para>
/// <para>
/// Sizing: while the parse runs, the store fills two scratch arrays rented from a pool shared by all threads (the
/// counterpart of typescript-go's pooled node arenas, internal/ast/ast.go), sized from the text length and grown by
/// doubling when a file needs more. When the parser creates the SourceFile node, the last node of a parse, the store
/// copies its records into arrays of the exact size and returns the scratch arrays to the pool; nodes created after
/// that grow the exact arrays as needed. So a parse allocates its final arrays once, without spare capacity or
/// growth garbage. The final int array lives on the pinned object heap: it outlives the parse like the tree it
/// holds, and the collector neither copies it from generation to generation nor scans it (it holds no references).
/// </para>
/// <para>
/// Speculative parsing discards the nodes (and lists and texts) it created by truncating the arrays to a <see cref="Mark"/> taken
/// before (<see cref="GetMark"/>, <see cref="Truncate"/>).
/// </para>
/// <para>
/// Node lists are handles into the list store of the same parse (NodeList.cs, step O7a); list fields are int slots.
/// </para>
/// </summary>
internal sealed partial class NodeStore
{
    // Kind word: kind in bits 0..15, boolean fields in bits 16..30 (generated), side table entries in bit 31.
    private const int KindMask = 0xFFFF;
    private const int SideBit = int.MinValue;

    // Header words after the kind word (HeaderWords and RefBaseWord are generated).
    private const int FlagsWord = 1;
    private const int PosWord = 2;
    private const int EndWord = 3;
    private const int TransformFlagsWord = 4;

    // Side table ids of the base fields; the generated side fields follow from 4 on.
    private const int ParentSide = 0;
    private const int OriginalSide = 1;
    private const int ModifierFlagsCacheSide = 2;
    private const int EmitNodeSide = 3;

    /// <summary>The fill levels of the store's arrays; truncating to it discards everything created after it.</summary>
    internal readonly struct Mark
    {
        internal readonly int Words;
        internal readonly int Refs;
        internal readonly int Nodes;
        internal readonly int SideLog;
        internal readonly int Lists;
        internal readonly int ListElements;
        internal readonly int Frames;
        internal readonly int ScratchTop;
        internal readonly int Chars;
        internal readonly int Interned;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        internal Mark(int words, int refs, int nodes, int sideLog, int lists, int listElements, int frames, int scratchTop, int chars, int interned)
        {
            Words = words;
            Refs = refs;
            Nodes = nodes;
            SideLog = sideLog;
            Lists = lists;
            ListElements = listElements;
            Frames = frames;
            ScratchTop = scratchTop;
            Chars = chars;
            Interned = interned;
        }
    }

    private struct SideValue
    {
        public int Int;
        public object? Ref;
    }

    private int[] data;
    private int used;
    private object?[] refs;
    private int refCount;
    // The highest refCount since the object array was taken from the pool: the slots to clear before giving it back.
    private int refMax;
    private int count;
    // Whether data and refs are scratch arrays of the pool (until the SourceFile node is created).
    private bool pooled;

    // Rare fields, keyed by (node << 8) | field id; sideLog lists the keys in the order they were added (truncation).
    private Dictionary<long, SideValue>? side;
    private long[]? sideLog;
    private int sideLogCount;

    /// <summary>A store for a source text (scratch arrays from the pool; exact arrays when the parse ends).</summary>
    public NodeStore(string sourceText)
    {
        var textLength = sourceText.Length;
        Texts = new TextTable(sourceText);
        Texts.UsePool();
        // From the tuning corpus: 0.56 ints and 0.036 object slots per character on average since node lists are
        // handles in int slots (O7a) (the pool rounds up to a power of two; a file that needs more grows its scratch
        // arrays).
        data = Scratch<int>.Rent((textLength / 3 * 2) + 64);
        refs = Scratch<object?>.Rent((textLength / 20) + 16);
        pooled = true;
        // Record 0 is Node.None.
        data[0] = (int)SyntaxKind.Unknown;
        data[FlagsWord] = 0;
        data[PosWord] = -1;
        data[EndWord] = -1;
        data[TransformFlagsWord] = 0;
        used = HeaderWords;
        count = 1;
        InitLists(textLength);
    }

    /// <summary>The texts of the parse: source text, character arena and interned identifiers (Text.cs, step O5a).</summary>
    internal TextTable Texts { get; }

    /// <summary>The number of records in use (including <see cref="Node.None"/>'s).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int Count => count;

    /// <summary>A new node of the given kind: pos and end -1, flags none, every field undefined (utilities.ts: function Node).</summary>
    internal Node Create(SyntaxKind kind)
    {
        var k = (int)kind;
        int size = RecordSizes[k];
        int refSize = RefCounts[k];
        if (kind == SyntaxKind.SourceFile && pooled)
        {
            Seal(size, refSize);
        }
        var h = used;
        if (size > data.Length - h)
        {
            GrowData(h + size);
        }
        var record = data.AsSpan(h, size);
        record[0] = k;
        record[FlagsWord] = 0;
        record[PosWord] = -1;
        record[EndWord] = -1;
        record[TransformFlagsWord] = 0;
        var first = HeaderWords;
        if (refSize != 0)
        {
            var r = refCount;
            if (refSize > refs.Length - r)
            {
                GrowRefs(r + refSize);
            }
            record[RefBaseWord] = r;
            first++;
            // Pooled arrays and slots of discarded nodes (reused after a truncation) may hold old references.
            refs.AsSpan(r, refSize).Clear();
            refCount = r + refSize;
            if (refCount > refMax)
            {
                refMax = refCount;
            }
        }
        for (var i = first; i < record.Length; i++)
        {
            record[i] = 0;
        }
        used = h + size;
        count++;
        return new Node(h);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Mark GetMark() => new(used, refCount, count, sideLogCount, listCount, listElementCount, frameCount, scratchTop, Texts.CharCount, Texts.InternedCount);

    /// <summary>Discards every node created after <paramref name="mark"/> (speculative parsing that is rolled back).</summary>
    internal void Truncate(Mark mark)
    {
        Debug.Assert(mark.Words <= used && mark.Refs <= refCount && mark.Nodes <= count && mark.SideLog <= sideLogCount && mark.Lists <= listCount, "Mark is beyond the store's fill level");
        used = mark.Words;
        refCount = mark.Refs;
        count = mark.Nodes;
        // Lists finished or opened since the mark belong to the discarded parse; the frames open at the mark are as
        // they were (nested lists finish before the enclosing list grows again).
        listCount = mark.Lists;
        listElementCount = mark.ListElements;
        frameCount = mark.Frames;
        scratchTop = mark.ScratchTop;
        // Texts: characters and interned identifiers added since the mark belong to the discarded parse.
        Texts.Truncate(mark.Chars, mark.Interned);
        if (sideLogCount > mark.SideLog)
        {
            TruncateSide(mark);
        }
    }

    /// <summary>
    /// Removes the side table entries of discarded nodes. Entries added since the mark for nodes that survive (caches
    /// written on older nodes) stay, and move down in the log so that an enclosing truncation still finds them.
    /// </summary>
    private void TruncateSide(Mark mark)
    {
        var log = sideLog!;
        var kept = mark.SideLog;
        for (var i = mark.SideLog; i < sideLogCount; i++)
        {
            var key = log[i];
            if ((int)(key >> 8) >= mark.Words)
            {
                side!.Remove(key);
            }
            else
            {
                log[kept++] = key;
            }
        }
        sideLogCount = kept;
    }

    /// <summary>Moves the records into arrays of the exact size (plus the SourceFile record) and returns the scratch arrays.</summary>
    private void Seal(int size, int refSize)
    {
        var exact = GC.AllocateUninitializedArray<int>(used + size, pinned: true);
        Array.Copy(data, exact, used);
        var exactRefs = new object?[refCount + refSize];
        Array.Copy(refs, exactRefs, refCount);
        ReturnScratch();
        data = exact;
        refs = exactRefs;
        pooled = false;
        SealLists();
    }

    /// <summary>Gives the scratch arrays back to the pool, without the references this store wrote.</summary>
    private void ReturnScratch()
    {
        refs.AsSpan(0, refMax).Clear();
        Scratch<object?>.Return(refs);
        Scratch<int>.Return(data);
    }

    private void GrowData(int needed)
    {
        var length = Math.Max(data.Length * 2, needed);
        var grown = pooled ? Scratch<int>.Rent(length) : GC.AllocateUninitializedArray<int>(length);
        Array.Copy(data, grown, used);
        if (pooled)
        {
            Scratch<int>.Return(data);
        }
        data = grown;
    }

    private void GrowRefs(int needed)
    {
        var length = Math.Max(refs.Length * 2, needed);
        var grown = pooled ? Scratch<object?>.Rent(length) : new object?[length];
        Array.Copy(refs, grown, refCount);
        if (pooled)
        {
            refs.AsSpan(0, refMax).Clear();
            Scratch<object?>.Return(refs);
            // The new array may hold old references beyond refCount; Create clears the slots it hands out.
            refMax = refCount;
        }
        refs = grown;
    }

    //// Base fields (utilities.ts: function Node).

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public SyntaxKind Kind(Node node) => (SyntaxKind)(data[node.Index] & KindMask);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public int Pos(Node node) => data[node.Index + PosWord];

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void SetPos(Node node, int value) => data[node.Index + PosWord] = value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public int End(Node node) => data[node.Index + EndWord];

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void SetEnd(Node node, int value) => data[node.Index + EndWord] = value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public NodeFlags Flags(Node node) => (NodeFlags)data[node.Index + FlagsWord];

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void SetFlags(Node node, NodeFlags value) => data[node.Index + FlagsWord] = (int)value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public TransformFlags TransformFlags(Node node) => (TransformFlags)data[node.Index + TransformFlagsWord];

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void SetTransformFlags(Node node, TransformFlags value) => data[node.Index + TransformFlagsWord] = (int)value;

    public Node Parent(Node node) => data[node.Index] >= 0 ? default : new Node(GetSideInt(node.Index, ParentSide));

    public void SetParent(Node node, Node value) => SetSideInt(node.Index, ParentSide, value.Index);

    public ModifierFlags ModifierFlagsCache(Node node) => data[node.Index] >= 0 ? default : (ModifierFlags)GetSideInt(node.Index, ModifierFlagsCacheSide);

    public void SetModifierFlagsCache(Node node, ModifierFlags value) => SetSideInt(node.Index, ModifierFlagsCacheSide, (int)value);

    public Node Original(Node node) => data[node.Index] >= 0 ? default : new Node(GetSideInt(node.Index, OriginalSide));

    public void SetOriginal(Node node, Node value) => SetSideInt(node.Index, OriginalSide, value.Index);

    public object? EmitNode(Node node) => data[node.Index] >= 0 ? null : GetSideRef(node.Index, EmitNodeSide);

    public void SetEmitNode(Node node, object? value) => SetSideRef(node.Index, EmitNodeSide, value);

    //// Side table.

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static long SideKey(int node, int field) => ((long)node << 8) | (uint)field;

    private int GetSideInt(int node, int field) => side is not null && side.TryGetValue(SideKey(node, field), out var value) ? value.Int : 0;

    private object? GetSideRef(int node, int field) => side is not null && side.TryGetValue(SideKey(node, field), out var value) ? value.Ref : null;

    private void SetSideInt(int node, int field, int value)
    {
        if (value == 0)
        {
            if (data[node] < 0)
            {
                ref var existing = ref CollectionsMarshal.GetValueRefOrNullRef(side!, SideKey(node, field));
                if (!Unsafe.IsNullRef(ref existing))
                {
                    existing.Int = 0;
                }
            }
            return;
        }
        AddSide(node, field).Int = value;
    }

    private void SetSideRef(int node, int field, object? value)
    {
        if (value is null)
        {
            if (data[node] < 0)
            {
                ref var existing = ref CollectionsMarshal.GetValueRefOrNullRef(side!, SideKey(node, field));
                if (!Unsafe.IsNullRef(ref existing))
                {
                    existing.Ref = null;
                }
            }
            return;
        }
        AddSide(node, field).Ref = value;
    }

    private ref SideValue AddSide(int node, int field)
    {
        side ??= [];
        var key = SideKey(node, field);
        ref var value = ref CollectionsMarshal.GetValueRefOrAddDefault(side, key, out var exists);
        if (!exists)
        {
            sideLog ??= new long[16];
            if (sideLogCount == sideLog.Length)
            {
                Array.Resize(ref sideLog, sideLog.Length * 2);
            }
            sideLog[sideLogCount++] = key;
            data[node] |= SideBit;
        }
        return ref value;
    }

    private static void ThrowNoField(int kindWord, string field)
    {
        throw new InvalidOperationException($"{(SyntaxKind)(kindWord & KindMask)} has no field {field}");
    }

    /// <summary>
    /// The pool of scratch arrays, shared by all threads: power-of-two lengths from 2^12 on, a few lock-free slots
    /// per length. Unlike a per-thread cache it keeps no more arrays than parses ran at the same time, and a thread
    /// that once parsed a large file does not hold a large array for itself.
    /// </summary>
    internal static class Scratch<T>
    {
        private const int MinShift = 12;
        private const int Classes = 31 - MinShift;
        private const int SlotsPerClass = 64;

        private static readonly T[]?[] Slots = new T[]?[Classes * SlotsPerClass];

        /// <summary>An array of at least <paramref name="minimumLength"/> elements (contents undefined).</summary>
        public static T[] Rent(int minimumLength)
        {
            var c = ClassOf(minimumLength);
            if (c >= Classes)
            {
                return new T[minimumLength];
            }
            for (var i = c * SlotsPerClass; i < (c + 1) * SlotsPerClass; i++)
            {
                if (Volatile.Read(ref Slots[i]) is not null && Interlocked.Exchange(ref Slots[i], null) is { } array)
                {
                    return array;
                }
            }
            return GC.AllocateUninitializedArray<T>(1 << (c + MinShift));
        }

        /// <summary>Keeps an array of <see cref="Rent"/> for later (the caller cleared the references it wrote).</summary>
        public static void Return(T[] array)
        {
            var c = ClassOf(array.Length);
            if (c >= Classes || array.Length != 1 << (c + MinShift))
            {
                return;
            }
            for (var i = c * SlotsPerClass; i < (c + 1) * SlotsPerClass; i++)
            {
                if (Volatile.Read(ref Slots[i]) is null && Interlocked.CompareExchange(ref Slots[i], array, null) is null)
                {
                    return;
                }
            }
        }

        private static int ClassOf(int length) => length <= 1 << MinShift ? 0 : 32 - BitOperations.LeadingZeroCount((uint)length - 1) - MinShift;
    }
}
