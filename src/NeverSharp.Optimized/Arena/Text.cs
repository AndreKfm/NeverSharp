// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's text values (pre-registration §5.4: identifier and literal text referenced as
// (pos, length) into the source text, values that differ from the source text in a character arena, identifiers
// interned in a table keyed by span), step O5a.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NeverSharp.Optimized;

/// <summary>
/// A text value (TypeScript: a string the scanner, the parser or the factory produced): a span of the source text of a
/// parse, or a span of the parse's character arena for values that differ from the source text (escape sequences,
/// normalized numbers, cooked template text, joined JSDoc comment text, escaped leading underscores). The characters
/// are read through the parse's <see cref="TextTable"/>. <see cref="None"/> (the default value) is TypeScript's
/// `undefined` where a string is expected; an empty string is a span of length 0.
/// <para>
/// Two handles with equal fields denote equal strings; equal strings may have different handles (use
/// <see cref="TextTable.Equals(TextRef, TextRef)"/>), except identifiers interned by the same table.
/// </para>
/// </summary>
internal readonly struct TextRef : IEquatable<TextRef>
{
    /// <summary>The offset in the source text (0 or more), or the complement of the offset in the character arena (negative).</summary>
    internal readonly int Start;

    /// <summary>The length plus one; 0 for <see cref="None"/>.</summary>
    internal readonly int Size;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal TextRef(int start, int size)
    {
        Start = start;
        Size = size;
    }

    /// <summary>No text (TypeScript: undefined).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static TextRef None => default;

    /// <summary>The empty string.</summary>
    public static TextRef Empty
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => new(0, 1);
    }

    /// <summary>A span of the source text.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TextRef Source(int start, int length) => new(start, length + 1);

    /// <summary>A span of the character arena.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TextRef Arena(int offset, int length) => new(~offset, length + 1);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsNone => Size == 0;

    /// <summary>The number of characters (-1 for <see cref="None"/>).</summary>
    public int Length
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => Size - 1;
    }

    /// <summary>Whether the characters are a span of the source text (or the value is empty or none).</summary>
    public bool IsSource
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => Start >= 0;
    }

    /// <summary>The characters from <paramref name="start"/> on, <paramref name="length"/> of them (the caller keeps the bounds).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public TextRef Slice(int start, int length) => length == 0 ? Empty : new(Start >= 0 ? Start + start : Start - start, length + 1);

    /// <summary>The characters from <paramref name="start"/> on.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public TextRef Slice(int start) => Slice(start, Length - start);

    /// <summary>JavaScript's String.prototype.substring(start, end): clamps to the text and swaps reversed bounds.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public TextRef Substring(int start, int end)
    {
        var length = Length;
        var from = Math.Min(Math.Max(start, 0), length);
        var to = Math.Min(Math.Max(end, 0), length);
        if (from > to)
        {
            (from, to) = (to, from);
        }
        return Slice(from, to - from);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(TextRef left, TextRef right) => left.Start == right.Start && left.Size == right.Size;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(TextRef left, TextRef right) => !(left == right);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(TextRef other) => this == other;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override bool Equals(object? obj) => obj is TextRef other && this == other;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override int GetHashCode() => (Start * 31) + Size;

    public override string ToString() => IsNone ? "None" : (Start >= 0 ? "Text@" : "Arena@") + (Start >= 0 ? Start : ~Start).ToString(System.Globalization.CultureInfo.InvariantCulture) + "+" + Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// TypeScript's `string | NodeArray&lt;JSDocComment&gt; | undefined` (the `comment` of JSDoc nodes): a text value, a node
/// list, or neither (the default value). Stored in two int slots.
/// </summary>
internal readonly struct TextOrList : IEquatable<TextOrList>
{
    // Size of a text (> 0), 0 for undefined, ListMarker for a list (A is then the list's index).
    private const int ListMarker = -1;

    internal readonly int A;
    internal readonly int B;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal TextOrList(int a, int b)
    {
        A = a;
        B = b;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static TextOrList None => default;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static implicit operator TextOrList(TextRef text) => new(text.Start, text.Size);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static implicit operator TextOrList(NodeList list) => list.IsNone ? default : new(list.Index, ListMarker);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsNone => B == 0;

    /// <summary>`typeof comment === "string"`.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsText => B > 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsList => B == ListMarker;

    /// <summary>The text (<see cref="TextRef.None"/> unless <see cref="IsText"/>).</summary>
    public TextRef Text
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => B > 0 ? new TextRef(A, B) : default;
    }

    /// <summary>The list (<see cref="NodeList.None"/> unless <see cref="IsList"/>).</summary>
    public NodeList List
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => B == ListMarker ? new NodeList(A) : default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(TextOrList left, TextOrList right) => left.A == right.A && left.B == right.B;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(TextOrList left, TextOrList right) => !(left == right);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(TextOrList other) => this == other;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override bool Equals(object? obj) => obj is TextOrList other && this == other;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override int GetHashCode() => (A * 31) + B;
}

/// <summary>
/// The texts of one parse: the source text, the character arena for values that differ from it, and the identifier
/// interning table (TypeScript: the parser's `identifiers` map and `internIdentifier`).
/// <para>
/// Character arena: values are appended and never change; a value is a <see cref="TextRef"/> into it. Speculative
/// parsing that is rolled back truncates the arena (and the interning table) to a mark taken before, with the node
/// store (<see cref="NodeStore.Truncate"/>).
/// </para>
/// <para>
/// Interning table: a hash table keyed by the characters of a text (the counterpart of a dictionary with span alternate
/// lookup, without a string per key). Its entries are the first handle interned for each distinct text; interning a
/// text returns that handle, so equal identifiers of a parse have equal handles. Entries are kept in the order they were
/// added and each bucket's chain runs from the newest entry to the oldest, so truncating removes the newest entries from
/// the heads of their chains.
/// </para>
/// <para>
/// Memory and lifetime (step O9, variant C): the arena of a parse's table (<see cref="UseSlabs"/>) is a range of the
/// thread's character slab, as the node records are ranges of its record slab (NodeStore.cs); the hash table is the
/// thread's, lent to the table while the parse runs. <see cref="Seal"/>, at the end of the parse, writes the interned
/// handles (TypeScript's `identifiers`) after the arena's characters, commits the range, clears the hash table's used
/// buckets (not the whole table) and gives it back to the thread; a hash table is rebuilt only if something interns
/// afterwards. Other tables (a scanner's own) keep their arena and hash table in memory of their own.
/// </para>
/// </summary>
internal sealed unsafe class TextTable
{
    private struct Entry
    {
        public int Hash;
        public int Next; // the next (older) entry of the bucket, 0 for none
        public TextRef Text;
    }

    // A hash table of the thread (the arrays a parse's table uses until Seal): buckets all zero while it is not lent.
    private sealed class InternTable
    {
        public int[] Buckets = new int[1024];
        public Entry[] Entries = new Entry[1024];
    }

    [ThreadStatic]
    private static InternTable? threadTable;

    // The source text (step O1, variant C: native memory the parse result owns) and its characters.
    private NativeText owner;
    private SourceChars source;

    // The arena: charCapacity characters from chars on (a range of a character slab, or memory of the table's own).
    private char* chars;
    private int charCapacity;
    private int charCount;
    private SlabRange charRange;

    // A parse's table: the thread whose slabs it writes to, and the store whose parse owns them (ThreadSlabs.Active).
    private ThreadSlabs? slabs;
    private object? slabsOwner;

    // Interning: buckets hold entry numbers (1-based, 0 = empty); entries[0] is unused. lent: the thread's table the
    // arrays belong to (until Seal).
    private int[]? buckets;
    private Entry[]? entries;
    private int entryCount;
    private InternTable? lent;

    // The interned handles in the order they were added, after Seal (the hash table is gone then): entryCount of them,
    // after the arena's characters.
    private TextRef* sealedInterned;

    // Whether this is a parse's table that is not sealed yet.
    private bool pooled;

    public TextTable(NativeText source)
    {
        owner = source;
        this.source = source.Chars;
    }

    /// <summary>The characters of the source text of the parse.</summary>
    internal SourceChars Source
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => source;
    }

    /// <summary>The source text of the parse (its owner: the parse result releases it).</summary>
    internal NativeText Owner
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => owner;
    }

    /// <summary>The fill level of the character arena (for <see cref="NodeStore.Mark"/>).</summary>
    internal int CharCount
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => charCount;
    }

    /// <summary>The number of interned texts (for <see cref="NodeStore.Mark"/>).</summary>
    internal int InternedCount
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => entryCount;
    }

    /// <summary>
    /// The table of a parse: the arena is a range of the thread's character slab and the hash table the thread's, until
    /// <see cref="Seal"/>; <paramref name="store"/> is the store whose parse owns the thread's slabs.
    /// </summary>
    internal void UseSlabs(ThreadSlabs local, object store, int textLength)
    {
        pooled = true;
        slabs = local;
        slabsOwner = store;
        chars = (char*)charRange.Open(local, SlabKind.Chars, (((long)textLength / 8) + 256) * sizeof(char));
        charCapacity = (int)(charRange.Capacity / sizeof(char));
    }

    // The thread whose slabs the arena grows into: the table's parse still owns them.
    private ThreadSlabs? ActiveSlabs
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => pooled && slabs is not null && ReferenceEquals(slabs.Active, slabsOwner) ? slabs : null;
    }

    /// <summary>Drops the hold on the character slab and frees the source text (the result is released).</summary>
    internal void Release()
    {
        owner.Release();
        chars = null;
        charCapacity = 0;
        charCount = 0;
        sealedInterned = null;
        entryCount = 0;
        buckets = null;
        entries = null;
        charRange.Release();
    }

    /// <summary>Starts over with another source text (a scanner's own table, reused from text to text).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void Reset(NativeText newSource)
    {
        owner = newSource;
        source = newSource.Chars;
        charCount = 0;
        if (entryCount != 0)
        {
            Array.Clear(buckets!);
            entryCount = 0;
        }
    }

    /// <summary>
    /// The end of the parse: the interned handles are written after the arena's characters, the arena's range is
    /// committed, and the hash table goes back to the thread with its used buckets cleared.
    /// </summary>
    [NoAllocUnchecked("per-file result: the slab range of characters and interned texts the finished tree keeps")]
    internal void Seal()
    {
        if (!pooled)
        {
            return;
        }
        var committed = charCount;
        if (entries is not null)
        {
            // The interned handles, at a multiple of 4 characters (8 bytes) after the characters.
            var at = (charCount + 3) & ~3;
            committed = at + (entryCount * 4);
            if (committed > charCapacity)
            {
                GrowChars(committed);
            }
            var interned = (TextRef*)(chars + at);
            var e = entries;
            var b = buckets!;
            var mask = b.Length - 1;
            for (var i = 1; i <= entryCount; i++)
            {
                interned[i - 1] = e[i].Text;
                b[e[i].Hash & mask] = 0;
            }
            sealedInterned = interned;
            if (lent is not null)
            {
                lent.Buckets = b;
                lent.Entries = e;
                threadTable ??= lent;
                lent = null;
            }
            buckets = null;
            entries = null;
            // (entryCount stays: the number of interned texts, now at sealedInterned.)
        }
        charRange.Commit((long)committed * sizeof(char), ActiveSlabs, SlabKind.Chars);
        // Characters added later move the arena to memory of the table's own (the interned handles stay where they are).
        charCapacity = charCount;
        pooled = false;
    }

    /// <summary>The characters of a text (empty for <see cref="TextRef.None"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public ReadOnlySpan<char> Span(TextRef text)
    {
        if (text.Size <= 1)
        {
            return default;
        }
        return text.Start >= 0 ? source.AsSpan(text.Start, text.Size - 1) : new ReadOnlySpan<char>(chars + ~text.Start, text.Size - 1);
    }

    /// <summary>The text as a string (null for <see cref="TextRef.None"/>): where TypeScript's code needs a string object.</summary>
    public string? GetString(TextRef text)
    {
        if (text.IsNone)
        {
            return null;
        }
        if (text.Size == 1)
        {
            return "";
        }
        if (text.Start == 0 && text.Size - 1 == source.Length)
        {
            return owner.GetString();
        }
        return Span(text).ToString();
    }

    /// <summary>`text === value`.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(TextRef text, string value) => !text.IsNone && Span(text).SequenceEqual(value);

    /// <summary>`a === b` for two texts (undefined equals undefined).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(TextRef a, TextRef b) => a == b || (!a.IsNone && !b.IsNone && a.Size == b.Size && Span(a).SequenceEqual(Span(b)));

    /// <summary>`a === b` for two comments: texts by value, lists by identity.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(TextOrList a, TextOrList b) => a.IsText && b.IsText ? Equals(a.Text, b.Text) : a == b;

    /// <summary>`text.substring(start, end)` of the source text (clamped, reversed bounds swapped), as a span.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public TextRef Substring(int start, int end)
    {
        var length = source.Length;
        start = Math.Clamp(start, 0, length);
        end = Math.Clamp(end, 0, length);
        if (start > end)
        {
            (start, end) = (end, start);
        }
        return TextRef.Source(start, end - start);
    }

    /// <summary>
    /// A value with the given characters in the arena (the empty string as <see cref="TextRef.Empty"/>); the characters
    /// must not be a span of the arena (it may move).
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public TextRef Add(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            return TextRef.Empty;
        }
        var at = Reserve(value.Length);
        value.CopyTo(new Span<char>(chars + at, value.Length));
        charCount = at + value.Length;
        return TextRef.Arena(at, value.Length);
    }

    /// <summary>`a + b`.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public TextRef Concat(TextRef a, TextRef b)
    {
        var builder = new TextBuilder(this);
        builder.Append(a);
        builder.Append(b);
        return builder.Result();
    }

    /// <summary>`a + b` for a constant <paramref name="a"/>.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public TextRef Concat(string a, TextRef b)
    {
        var builder = new TextBuilder(this);
        builder.Append(a);
        builder.Append(b);
        return builder.Result();
    }

    /// <summary>`a + b` for a constant <paramref name="b"/>.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public TextRef Concat(TextRef a, string b)
    {
        var builder = new TextBuilder(this);
        builder.Append(a);
        builder.Append(b);
        return builder.Result();
    }

    /// <summary>Makes room for <paramref name="count"/> characters at the end of the arena; returns where they go.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int Reserve(int count)
    {
        var at = charCount;
        if (count > charCapacity - at)
        {
            GrowChars(at + count);
        }
        return at;
    }

    [NoAllocUnchecked("arena growth: the character range moves to a larger slab")]
    private void GrowChars(int needed)
    {
        // A parse's table grows into the thread's next character slab while its parse owns the thread's slabs, other
        // tables (and a parse's table after Seal) into memory of their own. The range it leaves stays held.
        var needs = Math.Max(Math.Max((long)charCapacity * 2, 256), needed);
        chars = (char*)charRange.Grow((long)charCount * sizeof(char), needs * sizeof(char), ActiveSlabs, SlabKind.Chars);
        charCapacity = (int)Math.Min(charRange.Capacity / sizeof(char), int.MaxValue);
    }

    /// <summary>The arena's characters from <paramref name="at"/> (after <see cref="Reserve"/>), for writing.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Span<char> ArenaSpan(int at, int count) => new(chars + at, count);

    /// <summary>Copies characters of the arena to a place after them (after <see cref="Reserve"/>).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void CopyWithin(int from, int to, int count) => new ReadOnlySpan<char>(chars + from, count).CopyTo(new Span<char>(chars + to, count));

    /// <summary>Sets the arena's fill level (after writing reserved characters).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetCharCount(int count) => charCount = count;

    /// <summary>
    /// parser.ts internIdentifier: the handle of the first text interned with the same characters, or
    /// <paramref name="text"/> itself, which becomes that handle.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public TextRef Intern(TextRef text)
    {
        var span = Span(text);
        var hash = Hash(span);
        if (buckets is null)
        {
            CreateHashTable();
        }
        var e = entries!;
        var b = buckets!;
        for (var i = b[hash & (b.Length - 1)]; i != 0; i = e[i].Next)
        {
            ref var entry = ref e[i];
            if (entry.Hash == hash && entry.Text.Size == text.Size && SameChars(Span(entry.Text), span))
            {
                return entry.Text;
            }
        }
        AddEntry(hash, text);
        return text;
    }

    /// <summary>The interned texts as TypeScript's `identifiers` map (materialized on demand; nothing in the parse reads it).</summary>
    public Dictionary<string, string> ToDictionary()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var interned in InternedTexts())
        {
            var text = GetString(interned)!;
            map[text] = text;
        }
        return map;
    }

    private TextRef[] InternedTexts()
    {
        if (entries is null)
        {
            return sealedInterned == null ? [] : new ReadOnlySpan<TextRef>(sealedInterned, entryCount).ToArray();
        }
        var interned = new TextRef[entryCount];
        for (var i = 0; i < interned.Length; i++)
        {
            interned[i] = entries[i + 1].Text;
        }
        return interned;
    }

    // The hash table: the thread's for a parse (lent until Seal, its buckets all zero), rebuilt in arrays of the
    // table's own from the sealed handles if something interns after Seal.
    [NoAllocUnchecked("arena growth: the interning table's first arrays, lent by the thread or new")]
    private void CreateHashTable()
    {
        if (pooled)
        {
            var table = threadTable ?? new InternTable();
            threadTable = null;
            lent = table;
            buckets = table.Buckets;
            entries = table.Entries;
            entryCount = 0;
            return;
        }
        var previous = sealedInterned;
        var previousCount = previous == null ? 0 : entryCount;
        var size = 256;
        while (previousCount > size - (size >> 2))
        {
            size *= 2;
        }
        buckets = new int[size];
        entries = new Entry[size];
        entryCount = 0;
        sealedInterned = null;
        for (var i = 0; i < previousCount; i++)
        {
            var text = previous[i];
            AddEntry(Hash(Span(text)), text);
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void AddEntry(int hash, TextRef text)
    {
        var index = entryCount + 1;
        if (index >= entries!.Length)
        {
            GrowEntries(index);
        }
        entryCount = index;
        if (index > buckets!.Length - (buckets.Length >> 2))
        {
            Rehash(buckets.Length * 2);
        }
        ref var entry = ref entries[index];
        entry.Hash = hash;
        entry.Text = text;
        ref var bucket = ref buckets[hash & (buckets.Length - 1)];
        entry.Next = bucket;
        bucket = index;
    }

    // (A lent table keeps the grown arrays: they go back to the thread with Seal.)
    [NoAllocUnchecked("arena growth: the interning table's entry array doubles")]
    private void GrowEntries(int index)
    {
        var grown = new Entry[entries!.Length * 2];
        entries.AsSpan(0, index).CopyTo(grown);
        entries = grown;
    }

    // Rebuilds the chains of the entries before the one being added, oldest first, so each chain still runs from
    // the newest entry to the oldest.
    [NoAllocUnchecked("arena growth: the interning table's bucket array doubles")]
    private void Rehash(int size)
    {
        var grown = new int[size];
        var e = entries!;
        for (var i = 1; i < entryCount; i++)
        {
            ref var bucket = ref grown[e[i].Hash & (grown.Length - 1)];
            e[i].Next = bucket;
            bucket = i;
        }
        buckets = grown;
    }

    /// <summary>Discards the characters and interned texts added after a mark (<see cref="NodeStore.Truncate"/>).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void Truncate(int markChars, int markInterned)
    {
        charCount = markChars;
        if (entryCount > markInterned)
        {
            if (entries is null)
            {
                CreateHashTable(); // (sealed: the hash table is rebuilt first)
            }
            var e = entries!;
            var b = buckets!;
            for (var i = entryCount; i > markInterned; i--)
            {
                // The newest entry of its bucket: the head of the chain.
                b[e[i].Hash & (b.Length - 1)] = e[i].Next;
            }
            entryCount = markInterned;
        }
    }

    // Step O5b: a hash over the UTF-16 code units read four at a time (words of 8 bytes, every read within the text),
    // each word folded into the state by xor and a multiplication by an odd constant (a bijection of the state for each
    // word), then the state's halves folded and multiplied once more so that every character reaches the low bits the
    // buckets use. The words: 1 to 3 characters form one word of the first, middle and last character (the length is in
    // the seed); 4 to 8 characters the first and the last four (overlapping); 9 to 16 characters the words at 0, 4,
    // length - 8 and length - 4; longer texts the words at 0, 4, 8, ... before length - 4 and the last four (the
    // characters are counted, the reads are 8 bytes each). A dependency chain of one
    // multiplication per word instead of one per character (FNV-1a before), and no loop up to 16 characters.
    // Which hash is used does not change any result: entries, their order and the handles returned do not depend on it.
    private const ulong HashSeed = 0x9E3779B97F4A7C15ul;
    private const ulong HashMultiplier = 0xBF58476D1CE4E5B9ul;
    private const ulong HashFinal = 0x94D049BB133111EBul;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int Hash(ReadOnlySpan<char> span)
    {
        ref var c = ref MemoryMarshal.GetReference(span);
        ref var first = ref Unsafe.As<char, byte>(ref c);
        var length = span.Length;
        var h = HashSeed ^ (ulong)length;
        if (length >= 4)
        {
            var last = (nint)(length - 4) * sizeof(char);
            h = (h ^ Unsafe.ReadUnaligned<ulong>(ref first)) * HashMultiplier;
            if (length > 8)
            {
                if (length <= 16)
                {
                    h = (h ^ Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref first, sizeof(ulong)))) * HashMultiplier;
                    h = (h ^ Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref first, last - sizeof(ulong)))) * HashMultiplier;
                }
                else
                {
                    for (nint i = sizeof(ulong); i < last; i += sizeof(ulong))
                    {
                        h = (h ^ Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref first, i))) * HashMultiplier;
                    }
                }
            }
            h = (h ^ Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref first, last))) * HashMultiplier;
        }
        else if (length > 0)
        {
            var word = (ulong)c | ((ulong)Unsafe.Add(ref c, length >> 1) << 16) | ((ulong)Unsafe.Add(ref c, length - 1) << 32);
            h = (h ^ word) * HashMultiplier;
        }
        h = (h ^ (h >> 32)) * HashFinal;
        return (int)(h >> 32);
    }

    // Step O5b: `a.SequenceEqual(b)` for two texts of the same length, without a loop or a call up to 16 characters
    // (the same words as Hash reads, which together cover every character), by SequenceEqual for longer ones.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool SameChars(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var length = b.Length;
        ref var cx = ref MemoryMarshal.GetReference(a);
        ref var cy = ref MemoryMarshal.GetReference(b);
        if (length >= 4)
        {
            if (length > 16)
            {
                return a.SequenceEqual(b);
            }
            ref var x = ref Unsafe.As<char, byte>(ref cx);
            ref var y = ref Unsafe.As<char, byte>(ref cy);
            var last = (nint)(length - 4) * sizeof(char);
            var difference = (Unsafe.ReadUnaligned<ulong>(ref x) ^ Unsafe.ReadUnaligned<ulong>(ref y)) |
                (Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref x, last)) ^ Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref y, last)));
            if (length > 8)
            {
                difference |= (Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref x, sizeof(ulong))) ^ Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref y, sizeof(ulong)))) |
                    (Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref x, last - sizeof(ulong))) ^ Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref y, last - sizeof(ulong))));
            }
            return difference == 0;
        }
        if (length == 0)
        {
            return true;
        }
        return ((cx ^ cy) | (Unsafe.Add(ref cx, length >> 1) ^ Unsafe.Add(ref cy, length >> 1)) | (Unsafe.Add(ref cx, length - 1) ^ Unsafe.Add(ref cy, length - 1))) == 0;
    }
}

/// <summary>
/// A text built piece by piece (TypeScript: `result += ...`): while the pieces are one contiguous span of the source
/// text, the result is that span; otherwise the pieces are copied to the end of the character arena as they come, and
/// the result is that range of the arena. Only one builder may append to a table's arena at a time (the scanner's and
/// the JSDoc parser's builders are local variables used in sequence); a builder that finds the arena changed under it
/// copies its characters to the end again. A mutable struct, used as a local variable.
/// </summary>
internal struct TextBuilder
{
    private const int EmptyMode = 0;
    private const int SourceMode = 1;
    private const int ArenaMode = 2;

    private readonly TextTable table;
    private int mode;
    private int start; // source mode: offset in the source text; arena mode: offset in the arena
    private int length;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public TextBuilder(TextTable table)
    {
        this.table = table;
    }

    /// <summary>The number of characters so far.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public readonly int Length => length;

    /// <summary>`result += text.substring(from, to)` of the source text (clamped, reversed bounds swapped).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void AppendSource(int from, int to)
    {
        var sourceLength = table.Source.Length;
        from = Math.Clamp(from, 0, sourceLength);
        to = Math.Clamp(to, 0, sourceLength);
        if (from > to)
        {
            (from, to) = (to, from);
        }
        if (from == to)
        {
            return;
        }
        switch (mode)
        {
            case EmptyMode:
                mode = SourceMode;
                start = from;
                length = to - from;
                return;
            case SourceMode when start + length == from:
                length += to - from;
                return;
            default:
                AppendChars(table.Source.AsSpan(from, to - from));
                return;
        }
    }

    /// <summary>`result += text.substring(from, to)`, <paramref name="text"/> being the table's source text.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void Append(SourceChars text, int from, int to)
    {
        Debug.Assert(text.SameAs(table.Source), "A range of another string than the source text");
        AppendSource(from, to);
    }

    /// <summary>`result += value`.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void Append(TextRef value)
    {
        if (value.Size <= 1)
        {
            return;
        }
        if (value.Start >= 0)
        {
            AppendSource(value.Start, value.Start + value.Size - 1);
            return;
        }
        var offset = ~value.Start;
        if (mode == EmptyMode && offset + value.Size - 1 == table.CharCount)
        {
            // The last value of the arena: extended in place.
            mode = ArenaMode;
            start = offset;
            length = value.Size - 1;
            return;
        }
        // (Copied within the arena by index: the arena may move while room is made.)
        var count = value.Size - 1;
        table.CopyWithin(offset, Prepare(count), count);
    }

    /// <summary>`result += value` for a string.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void Append(string value)
    {
        if (value.Length != 0)
        {
            AppendChars(value);
        }
    }

    /// <summary>`result += String.fromCharCode(c)`.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void Append(char value)
    {
        AppendChars(new ReadOnlySpan<char>(in value));
    }

    // Appends characters that are not in the arena (a string or a range of the source text).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void AppendChars(ReadOnlySpan<char> value)
    {
        value.CopyTo(table.ArenaSpan(Prepare(value.Length), value.Length));
    }

    /// <summary>
    /// Makes room for <paramref name="count"/> more characters at the end of the builder's range of the arena, first moving
    /// what there is to the end of the arena if it is not there (a span of the source text, or arena characters another
    /// writer overtook); returns where the characters go.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int Prepare(int count)
    {
        if (mode != ArenaMode || start + length != table.CharCount)
        {
            var at = table.Reserve(length + count);
            if (mode == SourceMode)
            {
                table.Source.AsSpan(start, length).CopyTo(table.ArenaSpan(at, length));
            }
            else if (mode == ArenaMode)
            {
                table.CopyWithin(start, at, length);
            }
            table.SetCharCount(at + length);
            mode = ArenaMode;
            start = at;
        }
        var to = table.Reserve(count);
        table.SetCharCount(to + count);
        length += count;
        return to;
    }

    /// <summary>The text built (the empty string if nothing was appended).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public readonly TextRef Result() => mode switch
    {
        SourceMode => TextRef.Source(start, length),
        ArenaMode => TextRef.Arena(start, length),
        _ => TextRef.Empty,
    };
}
