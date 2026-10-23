// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's node arena (pre-registration §5.4), step O6b variant A's layout (compact records).

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
/// Memory (step O9, variant C: per-thread slabs, Slab.cs): the records, the list headers, the list elements and the
/// arena characters of a parse are written straight into large slabs of native memory that the parsing thread fills
/// parse after parse (the counterpart of typescript-go's pooled node arenas, internal/ast/ast.go, which hand out
/// nodes from large blocks). A store's records are one range of the thread's current record slab, and a node handle
/// is an offset from the range's start, so a range that outgrows its slab moves to a new slab without changing any
/// handle. When the parse is committed (<see cref="Commit"/>), the ranges shrink to what is in use and the thread's
/// next parse starts after them: no copy and no array per file. The result keeps its slabs alive (a hold on each,
/// dropped by <see cref="Release"/>; the slab's finalizer is the backstop). Nodes created after the commit move the
/// grown arena to memory of the store's own. Object slots (references) live in a managed slot array the thread fills
/// the same way.
/// </para>
/// <para>
/// Speculative parsing discards the nodes (and lists and texts) it created by truncating the arrays to a <see cref="Mark"/> taken
/// before (<see cref="GetMark"/>, <see cref="Truncate"/>).
/// </para>
/// <para>
/// Node lists are handles into the list store of the same parse (NodeList.cs, step O7a); list fields are int slots.
/// </para>
/// </summary>
internal sealed unsafe partial class NodeStore
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

    // The records: dataCapacity ints from data on (the store's range of a record slab); used of them are in use.
    private int* data;
    private int dataCapacity;
    private int used;
    private SlabRange dataRange;

    // The object slots: refCapacity slots of refArray from refBase on; refCount of them are in use.
    private object?[] refArray = [];
    private int refBase;
    private int refCapacity;
    private int refCount;

    private int count;

    // The thread whose slabs this store's parse writes to, until the parse is committed (null afterwards, and for a
    // store whose slabs another store of the thread took over, see IsActive).
    private ThreadSlabs? owner;

    // Rare fields, keyed by (node << 8) | field id; sideLog lists the keys in the order they were added (truncation).
    private Dictionary<long, SideValue>? side;
    private long[]? sideLog;
    private int sideLogCount;

    /// <summary>A store for a source text, written into the slabs of the current thread until <see cref="Commit"/>.</summary>
    public NodeStore(NativeText sourceText)
    {
        var textLength = sourceText.Length;
        var local = ThreadSlabs.Local;
        local.TakeOver(this);
        owner = local;
        Texts = new TextTable(sourceText);
        Texts.UseSlabs(local, this, textLength);
        // From the tuning corpus: 0.56 ints per character on average since node lists are handles in int slots (O7a);
        // the range reaches to the end of the slab anyway, the estimate only decides whether a new slab is started.
        data = (int*)dataRange.Open(local, SlabKind.Records, (((long)textLength / 3 * 2) + 64) * sizeof(int));
        dataCapacity = (int)(dataRange.Capacity / sizeof(int));
        OpenRefs(local);
        // Record 0 is Node.None.
        data[0] = (int)SyntaxKind.Unknown;
        data[FlagsWord] = 0;
        data[PosWord] = -1;
        data[EndWord] = -1;
        data[TransformFlagsWord] = 0;
        used = HeaderWords;
        count = 1;
        InitLists(local, textLength);
    }

    /// <summary>Whether this store's parse still writes to the thread's slabs (it owns them and is not committed).</summary>
    private bool IsActive
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => owner is not null && ReferenceEquals(owner.Active, this);
    }

    // The object slots of the store: the rest of the thread's slot array.
    private void OpenRefs(ThreadSlabs local)
    {
        local.EnsureRefs();
        refArray = local.Refs!;
        refBase = local.RefsUsed;
        refCapacity = refArray.Length - refBase;
    }

    /// <summary>The object slots of the store (indices relative to its range).</summary>
    private Span<object?> refs
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => new(refArray, refBase, refCapacity);
    }

    /// <summary>
    /// The end of the parse (Parser.CreateSourceFile): the ranges shrink to what is in use and the thread's next parse
    /// starts after them; nodes created afterwards move the arena they grow to memory of the store's own.
    /// </summary>
    internal void Commit()
    {
        Seal(used, refCount);
        SealLists();
        Texts.Seal();
        if (IsActive)
        {
            owner!.Active = null;
        }
        owner = null;
    }

    /// <summary>
    /// Drops the store's holds on its slabs and frees its source text (SourceFile.Release): the caller is done with the
    /// tree, which must not be read afterwards.
    /// </summary>
    internal void Release()
    {
        if (owner is not null)
        {
            Commit();
        }
        Texts.Release();
        data = null;
        dataCapacity = 0;
        used = 0;
        dataRange.Release();
        ReleaseLists();
        refs.Slice(0, refCount).Clear();
        refArray = [];
        refBase = 0;
        refCapacity = 0;
        refCount = 0;
    }

    /// <summary>The texts of the parse: source text, character arena and interned identifiers (Text.cs, step O5a).</summary>
    internal TextTable Texts { get; }

    /// <summary>The number of records in use (including <see cref="Node.None"/>'s).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int Count => count;

    /// <summary>A new node of the given kind: pos and end -1, flags none, every field undefined (utilities.ts: function Node).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node Create(SyntaxKind kind)
    {
        var k = (int)kind;
        int size = RecordSizes[k];
        int refSize = RefCounts[k];
        var h = used;
        if (size > dataCapacity - h)
        {
            GrowData(h + size);
        }
        var record = data + h;
        record[0] = k;
        record[FlagsWord] = 0;
        record[PosWord] = -1;
        record[EndWord] = -1;
        record[TransformFlagsWord] = 0;
        var first = HeaderWords;
        if (refSize != 0)
        {
            var r = refCount;
            if (refSize > refCapacity - r)
            {
                GrowRefs(r + refSize);
            }
            record[RefBaseWord] = r;
            first++;
            // Slots of discarded nodes (reused after a truncation) may hold old references.
            refs.Slice(r, refSize).Clear();
            refCount = r + refSize;
        }
        for (var i = first; i < size; i++)
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    /// <summary>
    /// Commits the record range (<paramref name="words"/> ints) and the object slot range (<paramref name="slots"/>) of
    /// the finished parse: the thread's next parse starts after them, and later nodes grow memory of the store's own.
    /// </summary>
    [NoAllocUnchecked("per-file result: the slab ranges the finished tree keeps")]
    private void Seal(int words, int slots)
    {
        var local = IsActive ? owner : null;
        dataRange.Commit((long)words * sizeof(int), local, SlabKind.Records);
        dataCapacity = words;
        if (local is not null && ReferenceEquals(local.Refs, refArray))
        {
            local.RefsUsed = refBase + slots;
        }
        refCapacity = slots;
    }

    [NoAllocUnchecked("arena growth: the record range moves to a larger slab")]
    private void GrowData(int needed)
    {
        data = (int*)dataRange.Grow((long)used * sizeof(int), (long)needed * sizeof(int), IsActive ? owner : null, SlabKind.Records);
        dataCapacity = (int)(dataRange.Capacity / sizeof(int));
    }

    [NoAllocUnchecked("arena growth: the object slots move to a new slot array")]
    private void GrowRefs(int needed)
    {
        var length = Math.Max(Math.Max(refCapacity * 2, needed), 16);
        object?[] grown;
        if (IsActive)
        {
            grown = new object?[Math.Max(length, ThreadSlabs.RefSlabLength)];
            owner!.Refs = grown;
            owner.RefsUsed = 0;
        }
        else
        {
            grown = new object?[length];
        }
        var old = refs.Slice(0, refCount);
        old.CopyTo(grown);
        // The old slots no longer keep the objects alive.
        old.Clear();
        refArray = grown;
        refBase = 0;
        refCapacity = grown.Length;
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public Node Parent(Node node) => data[node.Index] >= 0 ? default : new Node(GetSideInt(node.Index, ParentSide));

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void SetParent(Node node, Node value) => SetSideInt(node.Index, ParentSide, value.Index);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public ModifierFlags ModifierFlagsCache(Node node) => data[node.Index] >= 0 ? default : (ModifierFlags)GetSideInt(node.Index, ModifierFlagsCacheSide);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void SetModifierFlagsCache(Node node, ModifierFlags value) => SetSideInt(node.Index, ModifierFlagsCacheSide, (int)value);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public Node Original(Node node) => data[node.Index] >= 0 ? default : new Node(GetSideInt(node.Index, OriginalSide));

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void SetOriginal(Node node, Node value) => SetSideInt(node.Index, OriginalSide, value.Index);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public object? EmitNode(Node node) => data[node.Index] >= 0 ? null : GetSideRef(node.Index, EmitNodeSide);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void SetEmitNode(Node node, object? value) => SetSideRef(node.Index, EmitNodeSide, value);

    //// Side table.

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static long SideKey(int node, int field) => ((long)node << 8) | (uint)field;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int GetSideInt(int node, int field) => side is not null && side.TryGetValue(SideKey(node, field), out var value) ? value.Int : 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private object? GetSideRef(int node, int field) => side is not null && side.TryGetValue(SideKey(node, field), out var value) ? value.Ref : null;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAllocUnchecked("arena growth: a new side table entry (rare fields)")]
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

    [NoAllocUnchecked("error path: reading or writing a field the node's kind does not have throws")]
    private static void ThrowNoField(int kindWord, string field)
    {
        throw new InvalidOperationException($"{(SyntaxKind)(kindWord & KindMask)} has no field {field}");
    }
}
