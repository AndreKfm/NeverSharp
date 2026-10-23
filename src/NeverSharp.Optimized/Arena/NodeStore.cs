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
/// Sizing (step O9, variant B: the result keeps the arrays it was built in): a parse starts in its thread's working
/// arrays (<see cref="ThreadArrays"/>, the counterpart of typescript-go's pooled node arenas, internal/ast/ast.go;
/// <see cref="ScratchWords"/> ints at first). A file that needs more grows them to the size its progress through the
/// text projects (<see cref="GrowthLength"/>), and the outgrown array goes back to the thread. When the parser creates
/// the SourceFile node, the last node of a parse, the result keeps the arrays as they are, without a copy; only an
/// array with much more room than the parse used (more than twice the room needed, and the spare room above
/// <see cref="TrimBytes"/>) is trimmed: its records are copied into an array of the exact size, and the working array
/// stays with the thread. So a thread's working arrays are replaced lazily: a parse that keeps them leaves the thread
/// without, and the next parse on the thread allocates new ones. The int array lives on the pinned object heap: it
/// outlives the parse like the tree it holds, and the collector neither copies it from generation to generation nor
/// scans it (it holds no references). Nodes created after the SourceFile node use the room left, or grow the arrays.
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
    // The highest refCount since the object array was taken from the thread: the slots to clear before giving it back.
    private int refMax;
    private int count;
    // Whether the parse is still building the arrays (until the SourceFile node is created, see Seal).
    private bool pooled;
    // The largest node end set so far: the parse's progress through the text (GrowthLength).
    private int maxEnd;

    // Rare fields, keyed by (node << 8) | field id; sideLog lists the keys in the order they were added (truncation).
    private Dictionary<long, SideValue>? side;
    private long[]? sideLog;
    private int sideLogCount;

    /// <summary>A store for a source text (the thread's working arrays or new ones; the result keeps them at the end).</summary>
    public NodeStore(NativeText sourceText)
    {
        Texts = new TextTable(sourceText);
        Texts.UsePool();
        var arrays = ThreadArrays.Current;
        data = ThreadArrays.Take(ref arrays.Data, ScratchWords, pinned: true);
        refs = ThreadArrays.Take(ref arrays.Refs, ScratchRefs, pinned: false);
        pooled = true;
        // Record 0 is Node.None.
        data[0] = (int)SyntaxKind.Unknown;
        data[FlagsWord] = 0;
        data[PosWord] = -1;
        data[EndWord] = -1;
        data[TransformFlagsWord] = 0;
        used = HeaderWords;
        count = 1;
        InitLists();
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
    /// The end of the parse (before the SourceFile record): the result keeps the arrays the parse built, except an array
    /// with much more room than needed, whose contents move into an exact array while the working array stays with the
    /// thread (<see cref="ThreadArrays.Fit{T}"/>).
    /// </summary>
    [NoAllocUnchecked("per-file result: the exact arrays the finished tree keeps")]
    private void Seal(int size, int refSize)
    {
        var arrays = ThreadArrays.Current;
        data = ThreadArrays.Fit(data, used, used + size, ref arrays.Data, ScratchWords, pinned: true);
        var needed = refCount + refSize;
        if (TrimAtSeal(refs.Length, needed, IntPtr.Size))
        {
            var exactRefs = new object?[needed];
            Array.Copy(refs, exactRefs, refCount);
            // The working array goes to the thread without the references this store wrote.
            refs.AsSpan(0, refMax).Clear();
            ThreadArrays.Offer(ref arrays.Refs, refs, ScratchRefs);
            refs = exactRefs;
        }
        pooled = false;
        SealLists();
    }

    [NoAllocUnchecked("arena growth: the record array doubles")]
    private void GrowData(int needed)
    {
        var grown = GC.AllocateUninitializedArray<int>(GrowthLength(data.Length, used, needed), pinned: true);
        Array.Copy(data, grown, used);
        if (pooled)
        {
            // (Nothing holds a span of the records while room is made; the thread's next parse may reuse the array.)
            ThreadArrays.Offer(ref ThreadArrays.Current.Data, data, ScratchWords);
        }
        data = grown;
    }

    [NoAllocUnchecked("arena growth: the object slot array doubles")]
    private void GrowRefs(int needed)
    {
        var grown = new object?[Math.Max(refs.Length * 2, needed)];
        Array.Copy(refs, grown, refCount);
        // (The old array is the collector's, with the references it holds.)
        refs = grown;
        refMax = refCount;
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
    public void SetEnd(Node node, int value)
    {
        data[node.Index + EndWord] = value;
        if (value > maxEnd)
        {
            maxEnd = value;
        }
    }

    /// <summary>
    /// The new length of an array that holds <paramref name="count"/> elements and needs <paramref name="needed"/>: while
    /// the parse builds the arrays (the result keeps them), the count projected to the end of the text from the parse's
    /// progress (the largest node end so far) plus a quarter, at least one and a half times the current length; after
    /// the parse, or before a sixteenth of the text, twice the current length.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int GrowthLength(int current, int count, int needed)
    {
        var textLength = Texts.Source.Length;
        long length = 2L * current;
        if (pooled && maxEnd > 0 && maxEnd > textLength / 16)
        {
            var projected = (long)count * textLength / maxEnd;
            length = Math.Max(projected + (projected >> 2) + 64, current + (current >> 1));
        }
        return (int)Math.Min(0x7FFFFFC7L, Math.Max(length, needed)); // (at most Array.MaxLength)
    }

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

    /// <summary>The spare room (bytes) up to which an array goes to the result as it is, however much larger than needed.</summary>
    internal const int TrimBytes = 4096;

    // The first lengths of a thread's working arrays (O9 variant B, measured on the tuning corpus and vscode): records
    // of about 10,000 characters of code (0.56 to 0.8 ints per character), a tenth and a fifth as many list headers and
    // list elements, and a few object slots (only the SourceFile node has object fields in the tuning corpus).
    private const int ScratchWords = 8192;
    private const int ScratchLists = (ScratchWords / 10) + 16;
    private const int ScratchElements = (ScratchWords / 5) + 16;
    private const int ScratchRefs = 64;

    /// <summary>
    /// Whether an array of <paramref name="length"/> elements is trimmed at the end of a parse that needs
    /// <paramref name="needed"/> of them: it lacks the room, or it has more than twice the room needed and the spare
    /// room exceeds <see cref="TrimBytes"/>.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool TrimAtSeal(int length, int needed, int elementSize) =>
        length < needed || (length > 2 * needed && (long)(length - needed) * elementSize > TrimBytes);

    /// <summary>
    /// The working arrays a thread keeps between parses (step O9, variant B): at most one per kind, taken by the next
    /// parse on the thread (<see cref="Take{T}"/>), and offered back by a parse that outgrew it or trimmed it
    /// (<see cref="Offer{T}"/>); the scratch arrays of lists under construction, which no result keeps, always go
    /// back. A parse takes an array out of its slot, so a parse that starts on the same thread meanwhile does not share
    /// it.
    /// </summary>
    private sealed class ThreadArrays
    {
        [ThreadStatic]
        private static ThreadArrays? current;

        public int[]? Data;
        public object?[]? Refs;
        public ListHeader[]? Lists;
        public Node[]? ListElements;
        public Node[]? ListScratch;
        public int[]? Frames;

        /// <summary>The arrays of the current thread.</summary>
        public static ThreadArrays Current => current ??= new ThreadArrays();

        /// <summary>
        /// The thread's array of the slot, or a new one of <paramref name="length"/> elements (the first length of a
        /// working array; contents undefined for arrays without references).
        /// </summary>
        public static T[] Take<T>(ref T[]? slot, int length, bool pinned)
        {
            var array = slot;
            slot = null;
            return array ?? GC.AllocateUninitializedArray<T>(length, pinned);
        }

        /// <summary>
        /// The array the result keeps: <paramref name="array"/> itself, or, if it is trimmed (<see cref="TrimAtSeal"/>),
        /// a copy of its first <paramref name="count"/> elements in an array of <paramref name="needed"/> elements, and
        /// the working array is offered to the thread.
        /// </summary>
        public static T[] Fit<T>(T[] array, int count, int needed, ref T[]? slot, int firstLength, bool pinned)
        {
            if (!TrimAtSeal(array.Length, needed, Unsafe.SizeOf<T>()))
            {
                return array;
            }
            var exact = GC.AllocateUninitializedArray<T>(needed, pinned);
            Array.Copy(array, exact, count);
            Offer(ref slot, array, firstLength);
            return exact;
        }

        /// <summary>
        /// Keeps a working array no result holds for the thread's next parse: the smaller of it and the slot's, and
        /// into an empty slot only up to four times the first length (a thread does not hold a large file's array).
        /// </summary>
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public static void Offer<T>(ref T[]? slot, T[] array, int firstLength)
        {
            if (slot is null ? array.Length <= 4 * firstLength : array.Length < slot.Length)
            {
                slot = array;
            }
        }
    }
}
