// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's text values (pre-registration §5.4: identifier and literal text referenced as
// (pos, length) into the source text, values that differ from the source text in a character arena, identifiers
// interned in a table keyed by span), step O5a.

using System.Runtime.CompilerServices;

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
/// Sizing and lifetime: the table of a parse (<see cref="UsePool"/>) takes its arena and hash table arrays from the pool
/// the node store uses (NodeStore.Scratch, shared by all threads) while the parse runs. <see cref="Seal"/>, at the end
/// of the parse, moves the arena's characters and the interned handles (TypeScript's `identifiers`) into arrays of the
/// exact size and gives the pooled arrays back; the hash table is rebuilt only if something interns afterwards.
/// </para>
/// </summary>
internal sealed class TextTable
{
    private struct Entry
    {
        public int Hash;
        public int Next; // the next (older) entry of the bucket, 0 for none
        public TextRef Text;
    }

    private char[] source;
    private char[] chars = [];
    private int charCount;

    // Interning: buckets hold entry numbers (1-based, 0 = empty); entries[0] is unused.
    private int[]? buckets;
    private Entry[]? entries;
    private int entryCount;

    // The interned handles in the order they were added, after Seal (the hash table is gone then).
    private TextRef[]? sealedInterned;

    // Whether the arrays come from the pool (a parse's table until Seal).
    private bool pooled;

    public TextTable(char[] source)
    {
        this.source = source;
    }

    /// <summary>The source text of the parse (step O1: an array of the exact length, see <see cref="SourceText"/>).</summary>
    internal char[] Source
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => source;
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

    /// <summary>Takes the arrays from the pool until <see cref="Seal"/> (the table of a parse).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void UsePool() => pooled = true;

    /// <summary>Starts over with another source text (a scanner's own table, reused from text to text).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void Reset(char[] newSource)
    {
        source = newSource;
        charCount = 0;
        if (entryCount != 0)
        {
            Array.Clear(buckets!);
            entryCount = 0;
        }
    }

    /// <summary>
    /// The end of the parse: the arena's characters and the interned handles move into arrays of the exact size, and the
    /// pooled arrays go back to the pool.
    /// </summary>
    [NoAllocUnchecked("per-file result: the exact arrays of characters and interned texts the finished tree keeps")]
    internal void Seal()
    {
        if (!pooled)
        {
            return;
        }
        pooled = false;
        var oldChars = chars;
        chars = charCount == 0 ? [] : oldChars.AsSpan(0, charCount).ToArray();
        if (oldChars.Length != 0)
        {
            NodeStore.Scratch<char>.Return(oldChars);
        }
        if (entries is not null)
        {
            var interned = new TextRef[entryCount];
            for (var i = 0; i < interned.Length; i++)
            {
                interned[i] = entries[i + 1].Text;
            }
            sealedInterned = interned;
            NodeStore.Scratch<int>.Return(buckets!);
            NodeStore.Scratch<Entry>.Return(entries);
            buckets = null;
            entries = null;
            // (entryCount stays: the number of interned texts, now in sealedInterned.)
        }
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
        return text.Start >= 0 ? source.AsSpan(text.Start, text.Size - 1) : new ReadOnlySpan<char>(chars, ~text.Start, text.Size - 1);
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
        value.CopyTo(chars.AsSpan(at));
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
        if (count > chars.Length - at)
        {
            GrowChars(at + count);
        }
        return at;
    }

    [NoAllocUnchecked("arena growth: the character arena doubles")]
    private void GrowChars(int needed)
    {
        var length = Math.Max(Math.Max(chars.Length * 2, 256), needed);
        var grown = pooled ? NodeStore.Scratch<char>.Rent(length) : new char[length];
        chars.AsSpan(0, charCount).CopyTo(grown);
        // (Nothing holds a span of the arena while room is made: Add's value is never one, and the builder copies arena
        // characters by index. So a pooled array can go back to the pool at once.)
        if (pooled && chars.Length != 0)
        {
            NodeStore.Scratch<char>.Return(chars);
        }
        chars = grown;
    }

    /// <summary>The arena's characters from <paramref name="at"/> (after <see cref="Reserve"/>), for writing.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Span<char> ArenaSpan(int at, int count) => chars.AsSpan(at, count);

    /// <summary>Copies characters of the arena to a place after them (after <see cref="Reserve"/>).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void CopyWithin(int from, int to, int count) => Array.Copy(chars, from, chars, to, count);

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
            if (entry.Hash == hash && entry.Text.Size == text.Size && Span(entry.Text).SequenceEqual(span))
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
            return sealedInterned ?? [];
        }
        var interned = new TextRef[entryCount];
        for (var i = 0; i < interned.Length; i++)
        {
            interned[i] = entries[i + 1].Text;
        }
        return interned;
    }

    // The hash table: pooled for a parse (power-of-two lengths from 4096 on), rebuilt from the sealed handles if
    // something interns after Seal.
    [NoAllocUnchecked("arena growth: the interning table's first arrays, from the pool")]
    private void CreateHashTable()
    {
        var previous = sealedInterned;
        var size = 256;
        while (previous is not null && previous.Length > size - (size >> 2))
        {
            size *= 2;
        }
        buckets = pooled ? NodeStore.Scratch<int>.Rent(size) : new int[size];
        Array.Clear(buckets);
        entries = pooled ? NodeStore.Scratch<Entry>.Rent(buckets.Length) : new Entry[buckets.Length];
        entryCount = 0;
        sealedInterned = null;
        if (previous is not null)
        {
            foreach (var text in previous)
            {
                AddEntry(Hash(Span(text)), text);
            }
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

    [NoAllocUnchecked("arena growth: the interning table's entry array doubles")]
    private void GrowEntries(int index)
    {
        var grown = pooled ? NodeStore.Scratch<Entry>.Rent(entries!.Length * 2) : new Entry[entries!.Length * 2];
        entries.AsSpan(0, index).CopyTo(grown);
        if (pooled)
        {
            NodeStore.Scratch<Entry>.Return(entries);
        }
        entries = grown;
    }

    // Rebuilds the chains of the entries before the one being added, oldest first, so each chain still runs from
    // the newest entry to the oldest.
    [NoAllocUnchecked("arena growth: the interning table's bucket array doubles")]
    private void Rehash(int size)
    {
        var grown = pooled ? NodeStore.Scratch<int>.Rent(size) : new int[size];
        Array.Clear(grown);
        var e = entries!;
        for (var i = 1; i < entryCount; i++)
        {
            ref var bucket = ref grown[e[i].Hash & (grown.Length - 1)];
            e[i].Next = bucket;
            bucket = i;
        }
        if (pooled)
        {
            NodeStore.Scratch<int>.Return(buckets!);
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

    // FNV-1a over the UTF-16 code units.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int Hash(ReadOnlySpan<char> span)
    {
        var h = 2166136261u;
        foreach (var c in span)
        {
            h = (h ^ c) * 16777619u;
        }
        return (int)(h ^ (h >> 15));
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
    public void Append(char[] text, int from, int to)
    {
        Debug.Assert(ReferenceEquals(text, table.Source), "A range of another string than the source text");
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
