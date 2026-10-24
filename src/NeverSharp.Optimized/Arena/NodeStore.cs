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
/// side table keyed by node and field, so they cost nothing on the nodes that lack them. The side table (step O10)
/// is an append-only log of entries (key, int value or object slot) with an open-addressing index over it, both in
/// one region of the thread's side slab (Slab.cs), opened at a file's first entry and owned by the result like the
/// records: no allocation per file, and a table that outgrows its region moves to a region twice the size.
/// Step BO1: parents written after the parse (the binder sets one on every node) live in a dense array instead, one
/// int per 4 record ints, allocated once per file in the thread's parents slab; the side table keeps only the parents
/// the parse itself writes.
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

    /// <summary>
    /// A side table entry: the key (node << 8 | field id), the value (an int field's value, or for an object field the
    /// index of the object slot that holds it), and whether the field is an object field.
    /// </summary>
    private struct SideEntry
    {
        public long Key;
        public int Value;
        public int IsRef;
    }

    // Side table entries in a new store's first region (a power of two).
    private const int FirstSideCapacity = 16;

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

    // Nodes created (step O12), not reduced by truncation.
    private int created;

    // The thread whose slabs this store's parse writes to, until the parse is committed (null afterwards, and for a
    // store whose slabs another store of the thread took over, see IsActive).
    private ThreadSlabs? owner;

    // Rare fields (step O10): sideCount entries of sideCapacity in the order they were added (truncation discards a
    // suffix), and the index, 2 * sideCapacity ints, each 0 (free) or an entry's position + 1, linear probing from the
    // key's hash (the top sideShift bits of a multiplicative hash). The current region is the last sideTop bytes in use
    // of sideRange; regions left by growth stay in the range unused. No region until the first entry.
    private SlabRange sideRange;
    private int* sideIndex;
    private SideEntry* sideEntries;
    private int sideCapacity;
    private int sideCount;
    private int sideShift;
    private long sideTop;

    // Parent pointers written after the parse (step BO1: the binder sets one for every node): dense, parentLength ints
    // from parents on, the parent of the record at handle h in parents[h >> 2] (records are at least HeaderWords = 5
    // ints long, so h >> 2 differs from record to record), 0 for none. A range of the thread's parents slab, allocated
    // in one piece at the first parent written after the parse (the parents the parse wrote move there from the side
    // table, which holds them until then: in the measured mode the parse sets few). Null until then.
    private int* parents;
    private int parentLength;
    private SlabRange parentRange;

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
        sideRange.Release();
        sideIndex = null;
        sideEntries = null;
        sideCapacity = 0;
        sideCount = 0;
        sideTop = 0;
        parentRange.Release();
        parents = null;
        parentLength = 0;
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

    /// <summary>The record ints in use: every node handle is below it (step BO1: sizes the binder's dense arrays).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int RecordWords => used;

    /// <summary>
    /// The number of nodes created so far, including those a truncation discarded since (the parser's nodeCount, which
    /// speculation does not restore; step O12: the factory creates nodes here directly).
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int CreatedCount => created;

    /// <summary>
    /// A new node as the parser's base factory creates it (`new Node(kind, /*pos*/ 0, /*end*/ 0)`, utilities.ts: function
    /// Node): pos and end 0, flags none, every field undefined, the given transform flags (step O12: one write of the
    /// record, formerly a record with pos and end -1 and two setters; counted in <see cref="CreatedCount"/>).
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Node CreateNode(SyntaxKind kind, TransformFlags transformFlags = 0)
    {
        var k = (int)kind;
        int size = RecordSizes[k];
        var h = used;
        // Room for the largest record: the zeroing below is one constant-size block (the words after this record's
        // end are free, they belong to no record).
        if (MaxRecordWords > dataCapacity - h)
        {
            GrowData(h + MaxRecordWords);
        }
        var record = data + h;
        Unsafe.InitBlockUnaligned(record, 0, MaxRecordWords * sizeof(int));
        record[0] = k;
        record[TransformFlagsWord] = (int)transformFlags;
        int refSize = RefCounts[k];
        if (refSize != 0)
        {
            InitRefs(record, refSize);
        }
        used = h + size;
        count++;
        created++;
        return new Node(h);
    }

    // The object slots of a new record (CreateNode; kinds with object slots are rare).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void InitRefs(int* record, int refSize)
    {
        var r = refCount;
        if (refSize > refCapacity - r)
        {
            GrowRefs(r + refSize);
        }
        record[RefBaseWord] = r;
        // Slots of discarded nodes (reused after a truncation) may hold old references.
        refs.Slice(r, refSize).Clear();
        refCount = r + refSize;
    }

    /// <summary>
    /// The record of a new node of <paramref name="size"/> ints, for the generated `New{Class}` methods, which write
    /// every int of it (step O12); counted like <see cref="CreateNode"/>.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int NewRecord(int size)
    {
        var h = used;
        if (size > dataCapacity - h)
        {
            GrowData(h + size);
        }
        used = h + size;
        count++;
        created++;
        return h;
    }

    /// <summary>
    /// The parser's finishNode in one go (step O12): sets pos and end and adds <paramref name="flags"/> to the node's
    /// flags.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Finish(Node node, int pos, int end, NodeFlags flags)
    {
        var record = data + node.Index;
        record[PosWord] = pos;
        record[EndWord] = end;
        if (flags != 0)
        {
            record[FlagsWord] |= (int)flags;
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Mark GetMark() => new(used, refCount, count, sideCount, listCount, listElementCount, frameCount, scratchTop, Texts.CharCount, Texts.InternedCount);

    /// <summary>Discards every node created after <paramref name="mark"/> (speculative parsing that is rolled back).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void Truncate(Mark mark)
    {
        Debug.Assert(mark.Words <= used && mark.Refs <= refCount && mark.Nodes <= count && mark.SideLog <= sideCount && mark.Lists <= listCount, "Mark is beyond the store's fill level");
        if (parents != null)
        {
            ClearParents(mark.Words, used);
        }
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
        if (sideCount > mark.SideLog)
        {
            TruncateSide(mark);
        }
    }

    /// <summary>
    /// The dense parents of the records from <paramref name="from"/> to <paramref name="to"/> (discarded): cleared, so
    /// that records created there later have none (the record before <paramref name="from"/> starts at least
    /// HeaderWords ints before it, so its entry is before from >> 2).
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void ClearParents(int from, int to)
    {
        var start = from >> 2;
        var end = Math.Min(((to - 1) >> 2) + 1, parentLength);
        if (start < end)
        {
            new Span<int>(parents + start, end - start).Clear();
        }
    }

    /// <summary>
    /// Removes the side table entries of discarded nodes. Entries added since the mark for nodes that survive (caches
    /// written on older nodes) stay, and move down in the log so that an enclosing truncation still finds them; an
    /// object slot of theirs from after the mark (truncated with the nodes' slots) moves down to a slot after
    /// <see cref="refCount"/> (slots were handed out in log order, so a move never overwrites a slot still to move).
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void TruncateSide(Mark mark)
    {
        var entries = sideEntries;
        var kept = mark.SideLog;
        for (var i = mark.SideLog; i < sideCount; i++)
        {
            var entry = entries[i];
            var slot = SideSlotOf(entry.Key, i + 1);
            if ((int)(entry.Key >> 8) >= mark.Words)
            {
                RemoveSideSlot(slot);
                continue;
            }
            if (entry.IsRef != 0 && entry.Value >= refCount)
            {
                var r = refCount++;
                var objects = refs;
                objects[r] = objects[entry.Value];
                entry.Value = r;
            }
            entries[kept] = entry;
            sideIndex[slot] = ++kept;
        }
        sideCount = kept;
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
        if (sideIndex is not null)
        {
            // The current region stays where it is, with its free entries: entries added later fill them first.
            sideRange.Commit(sideTop, local, SlabKind.Side);
        }
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
    public Node Parent(Node node)
    {
        if (parents != null)
        {
            var i = node.Index >> 2;
            return (uint)i < (uint)parentLength ? new Node(parents[i]) : default;
        }
        return data[node.Index] >= 0 ? default : new Node(GetSideInt(node.Index, ParentSide));
    }

    /// <summary>
    /// Sets the parent: during the parse in the side table (rare in the measured mode), afterwards in the dense parent
    /// array (step BO1), which the first such write creates (<see cref="AddSide"/>, the arena growth of rare fields).
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void SetParent(Node node, Node value)
    {
        var i = node.Index >> 2;
        if ((uint)i < (uint)parentLength)
        {
            parents[i] = value.Index;
            return;
        }
        if (parents == null && IsActive)
        {
            SetSideInt(node.Index, ParentSide, value.Index);
            return;
        }
        AddSide(node.Index, ParentSide);
        parents[i] = value.Index;
    }

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

    //// Side table (step O10).

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static long SideKey(int node, int field) => ((long)node << 8) | (uint)field;

    /// <summary>The key's home slot in the index: the top bits of a multiplicative (Fibonacci) hash.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int SideHome(long key) => (int)(((ulong)key * 0x9E3779B97F4A7C15UL) >> sideShift);

    /// <summary>The position of the key's entry, or -1 if the table has none.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int FindSide(long key)
    {
        var index = sideIndex;
        if (index is null)
        {
            return -1;
        }
        var mask = (sideCapacity * 2) - 1;
        var i = SideHome(key);
        while (true)
        {
            var e = index[i];
            if (e == 0 || sideEntries[e - 1].Key == key)
            {
                return e - 1;
            }
            i = (i + 1) & mask;
        }
    }

    /// <summary>The index slot that holds <paramref name="value"/> (an entry's position + 1, with the given key).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int SideSlotOf(long key, int value)
    {
        var mask = (sideCapacity * 2) - 1;
        var i = SideHome(key);
        while (sideIndex[i] != value)
        {
            i = (i + 1) & mask;
        }
        return i;
    }

    /// <summary>
    /// Frees an index slot. Later slots of the probe run move back where their home allows (backward-shift deletion),
    /// so every key stays reachable from its home slot without tombstones.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void RemoveSideSlot(int slot)
    {
        var index = sideIndex;
        var mask = (sideCapacity * 2) - 1;
        var j = slot;
        while (true)
        {
            j = (j + 1) & mask;
            var e = index[j];
            if (e == 0)
            {
                break;
            }
            var home = SideHome(sideEntries[e - 1].Key);
            if (((j - home) & mask) >= ((j - slot) & mask))
            {
                index[slot] = e;
                slot = j;
            }
        }
        index[slot] = 0;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int GetSideInt(int node, int field)
    {
        var p = FindSide(SideKey(node, field));
        return p < 0 ? 0 : sideEntries[p].Value;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private object? GetSideRef(int node, int field)
    {
        var p = FindSide(SideKey(node, field));
        return p < 0 ? null : refs[sideEntries[p].Value];
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void SetSideInt(int node, int field, int value)
    {
        // A node without the side bit has no entries.
        var p = data[node] < 0 ? FindSide(SideKey(node, field)) : -1;
        if (p < 0)
        {
            if (value == 0)
            {
                return;
            }
            p = AddSide(node, field);
        }
        sideEntries[p].Value = value;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void SetSideRef(int node, int field, object? value)
    {
        var p = data[node] < 0 ? FindSide(SideKey(node, field)) : -1;
        if (p >= 0)
        {
            refs[sideEntries[p].Value] = value;
            return;
        }
        if (value is null)
        {
            return;
        }
        // The value's object slot.
        var slot = refCount;
        if (slot == refCapacity)
        {
            GrowRefs(slot + 1);
        }
        refCount = slot + 1;
        refs[slot] = value;
        // (The position first: adding may move the table.)
        p = AddSide(node, field);
        ref var entry = ref sideEntries[p];
        entry.Value = slot;
        entry.IsRef = 1;
    }

    /// <summary>
    /// The position of a new entry (value 0, an int field's) for a key the table does not have; marks the node as
    /// having side entries.
    /// A full table first moves to a region with twice the entries (the first region: <see cref="FirstSideCapacity"/>
    /// entries): after the current region if the range has room, otherwise in the thread's next side slab (or, for a
    /// committed store, the thread's side slab if no parse runs on the thread, or a slab of its own); the entries are
    /// copied and the index is rebuilt.
    /// <para>
    /// Step BO1: a parent written after the parse (<see cref="SetParent"/>) instead creates or grows the dense parent
    /// array so that it covers the node, and the result is -1 (no entry).
    /// </para>
    /// </summary>
    [NoAllocUnchecked("arena growth: a new side table entry (rare fields)")]
    private int AddSide(int node, int field)
    {
        if (field == ParentSide && (parents != null || !IsActive))
        {
            EnsureParents(node);
            return -1;
        }
        if (sideCount == sideCapacity)
        {
            GrowSide();
        }
        var key = SideKey(node, field);
        var position = sideCount++;
        ref var entry = ref sideEntries[position];
        entry.Key = key;
        entry.Value = 0;
        entry.IsRef = 0;
        var index = sideIndex;
        var mask = (sideCapacity * 2) - 1;
        var i = SideHome(key);
        while (index[i] != 0)
        {
            i = (i + 1) & mask;
        }
        index[i] = position + 1;
        data[node] |= SideBit;
        return position;
    }

    // (Called by AddSide only.) The dense parent array covering the node: created in one piece in the thread's parents
    // slab, with the parents the side table holds, or grown for a node created afterwards.
    private void EnsureParents(int node)
    {
        var length = (Math.Max(used, node + 1) >> 2) + 1;
        if (parents == null)
        {
            parents = (int*)parentRange.OpenCommitted(ThreadSlabs.Local, SlabKind.Parents, (long)length * sizeof(int));
            new Span<int>(parents, length).Clear();
            parentLength = length;
            var entries = sideEntries;
            for (var p = 0; p < sideCount; p++)
            {
                if ((int)(entries[p].Key & 0xFF) == ParentSide)
                {
                    parents[(int)(entries[p].Key >> 8) >> 2] = entries[p].Value;
                }
            }
            return;
        }
        length = Math.Max(length, parentLength * 2);
        var bytes = (long)length * sizeof(int);
        parents = (int*)parentRange.GrowCommitted((long)parentLength * sizeof(int), bytes, bytes, SlabKind.Parents);
        new Span<int>(parents + parentLength, length - parentLength).Clear();
        parentLength = length;
    }

    // (Called by AddSide only.)
    private void GrowSide()
    {
        var capacity = sideCapacity == 0 ? FirstSideCapacity : sideCapacity * 2;
        var indexBytes = (long)capacity * 2 * sizeof(int);
        var bytes = indexBytes + ((long)capacity * sizeof(SideEntry));
        var active = IsActive ? owner : null;
        if (sideRange.Slab is null && active is not null)
        {
            // The parse's first entry: the range is the rest of the thread's current side slab.
            sideRange.Open(active, SlabKind.Side, bytes);
            sideTop = 0;
        }
        if (sideRange.Capacity - sideTop < bytes)
        {
            // The old region stays readable: the range keeps its slab held.
            if (active is not null)
            {
                sideRange.Grow(0, bytes, active, SlabKind.Side);
                sideTop = 0;
            }
            else
            {
                // After the parse (step BO1: caches the binder writes): in place after the current region, or a new
                // range in the thread's side slab.
                var start = sideRange.Start;
                var grown = sideRange.GrowCommitted(0, sideTop + bytes, bytes, SlabKind.Side);
                if (start == null || grown != start)
                {
                    sideTop = 0;
                }
            }
        }
        var region = sideRange.Start + sideTop;
        sideTop = SlabRange.Align(sideTop + bytes);
        var index = (int*)region;
        var entries = (SideEntry*)(region + indexBytes);
        new Span<int>(index, capacity * 2).Clear();
        var n = sideCount;
        if (n != 0)
        {
            Buffer.MemoryCopy(sideEntries, entries, (long)capacity * sizeof(SideEntry), (long)n * sizeof(SideEntry));
        }
        sideIndex = index;
        sideEntries = entries;
        sideCapacity = capacity;
        sideShift = 64 - System.Numerics.BitOperations.Log2((uint)(capacity * 2));
        var mask = (capacity * 2) - 1;
        for (var p = 0; p < n; p++)
        {
            var i = SideHome(entries[p].Key);
            while (index[i] != 0)
            {
                i = (i + 1) & mask;
            }
            index[i] = p + 1;
        }
    }

    [NoAllocUnchecked("error path: reading or writing a field the node's kind does not have throws")]
    private static void ThrowNoField(int kindWord, string field)
    {
        throw new InvalidOperationException($"{(SyntaxKind)(kindWord & KindMask)} has no field {field}");
    }
}
