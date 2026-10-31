// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's stored member lists of the checker (module CO3, docs/phase3-checker-layout.md).

using System.Runtime.CompilerServices;

namespace NeverSharp.Optimized;

/// <summary>
/// A list the checker stores (module CO3: resolved and declared properties, signatures, index infos): a range of one of
/// the checker's <see cref="ItemArena{T}"/>s. No references for the collector to trace; read through
/// <see cref="ItemArena{T}.View"/>. <c>default</c> is the empty list.
/// </summary>
internal readonly struct ItemRange<T>
{
    internal readonly int Start; // chunk << ItemArena.Shift | offset
    internal readonly int Count;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ItemRange(int start, int count)
    {
        Start = start;
        Count = count;
    }
}

/// <summary>
/// The append-only arena of one kind of stored list of a checker (module CO3): elements in chunks that never move, a
/// list never crosses a chunk (a list longer than a chunk gets a chunk of its own), so a view of a stored list is
/// (chunk, offset, count) and stays valid for good. Stored lists are never written again.
/// </summary>
internal sealed class ItemArena<T>
{
    internal const int Shift = 16;
    private const int Size = 1 << Shift;

    private T[][] chunks = [new T[1024]]; // (the first chunk small: Gate C's checkers check one small file each)
    private int chunk;   // the chunk being filled
    private int used;    // elements used in it

    /// <summary>The list a range stands for.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Items<T> View(ItemRange<T> range) =>
        range.Count == 0 ? default : new Items<T>(chunks[range.Start >> Shift], range.Start & (Size - 1), range.Count, range.Start);

    /// <summary>
    /// The stored form of a list: the list's own range when it is a view of this arena (TypeScript shares the array),
    /// else a copy.
    /// </summary>
    internal ItemRange<T> Store(Items<T> items)
    {
        if (items.Count == 0)
        {
            return default;
        }
        return items.ArenaStart >= 0 ? new ItemRange<T>(items.ArenaStart, items.Count) : Store(items.AsSpan());
    }

    /// <summary>A copy of these elements as a stored list.</summary>
    internal ItemRange<T> Store(List<T> items) => Store(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(items));

    /// <summary>A copy of these elements as a stored list.</summary>
    internal ItemRange<T> Store(ReadOnlySpan<T> items)
    {
        if (items.IsEmpty)
        {
            return default;
        }
        if (items.Length > chunks[chunk].Length - used)
        {
            // (an oversized list gets a chunk of its own, which it fills)
            NewChunk(Math.Max(Size, items.Length));
        }
        items.CopyTo(chunks[chunk].AsSpan(used));
        var range = new ItemRange<T>(chunk << Shift | used, items.Length);
        used += items.Length;
        return range;
    }

    /// <summary>A view of these elements stored as a list (a copy, see <see cref="Store(ReadOnlySpan{T})"/>).</summary>
    internal Items<T> Copy(ReadOnlySpan<T> items) => View(Store(items));

    /// <summary>A view of these elements stored as a list (a copy).</summary>
    internal Items<T> Copy(List<T> items) => View(Store(items));

    private void NewChunk(int length)
    {
        chunk++;
        if (chunk == chunks.Length)
        {
            Array.Resize(ref chunks, chunks.Length * 2);
        }
        chunks[chunk] = new T[length];
        used = 0;
    }
}

/// <summary>
/// A read-only view of a list (module CO3): a stored list of an <see cref="ItemArena{T}"/> or an array of its own (a
/// list the checker built and has not stored, or a copy of a <c>List</c>). Read like the <c>List</c> it replaces; views
/// live on the stack and in short-lived scratch, never in the checker's long-lived data (that keeps
/// <see cref="ItemRange{T}"/>s). <see cref="SameAs"/> is TypeScript's array identity.
/// </summary>
[CollectionBuilder(typeof(ItemsBuilder), nameof(ItemsBuilder.Create))]
internal readonly struct Items<T> : IReadOnlyList<T>
{
    private readonly T[]? array;
    private readonly int start;
    private readonly int count;
    private readonly int arenaStart; // the stored range's start, -1 for a view of an own array; (meaningful when count > 0)

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Items(T[] array, int start, int count, int arenaStart)
    {
        this.array = array;
        this.start = start;
        this.count = count;
        this.arenaStart = arenaStart;
    }

    /// <summary>A view of an array of its own (not copied; the array must not change afterwards).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Items(T[] array)
    {
        this.array = array;
        count = array.Length;
        arenaStart = -1;
    }

    /// <summary>A view of a copy of a list's elements.</summary>
    public static implicit operator Items<T>(List<T> list) => list.Count == 0 ? default : new Items<T>(list.ToArray());

    /// <summary>A view of an array (not copied).</summary>
    public static implicit operator Items<T>(T[] array) => array.Length == 0 ? default : new Items<T>(array);

    internal int ArenaStart
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => count == 0 ? -1 : arenaStart;
    }

    public int Count
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => count;
    }

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)count)
            {
                ThrowIndex();
            }
            return array![start + index];
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ReadOnlySpan<T> AsSpan() => count == 0 ? default : new ReadOnlySpan<T>(array, start, count);

    /// <summary>TypeScript's <c>a === b</c> on arrays: the same stored list or the same array (any two empty lists are the same).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool SameAs(Items<T> other) =>
        count == other.count && (count == 0 || (ReferenceEquals(array, other.array) && start == other.start));

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    internal T[] ToArray() => AsSpan().ToArray();

    internal List<T> ToList()
    {
        var list = new List<T>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(array![start + i]);
        }
        return list;
    }

    /// <summary>List.Find (default when nothing matches).</summary>
    internal T? Find(Predicate<T> match)
    {
        for (var i = 0; i < count; i++)
        {
            var item = array![start + i];
            if (match(item))
            {
                return item;
            }
        }
        return default;
    }

    internal int FindIndex(Predicate<T> match)
    {
        for (var i = 0; i < count; i++)
        {
            if (match(array![start + i]))
            {
                return i;
            }
        }
        return -1;
    }

    internal bool Exists(Predicate<T> match) => FindIndex(match) >= 0;

    internal bool TrueForAll(Predicate<T> match)
    {
        for (var i = 0; i < count; i++)
        {
            if (!match(array![start + i]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>List.FindAll (a new list).</summary>
    internal List<T> FindAll(Predicate<T> match)
    {
        var result = new List<T>();
        for (var i = 0; i < count; i++)
        {
            var item = array![start + i];
            if (match(item))
            {
                result.Add(item);
            }
        }
        return result;
    }

    internal List<TOutput> ConvertAll<TOutput>(Converter<T, TOutput> converter)
    {
        var result = new List<TOutput>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(converter(array![start + i]));
        }
        return result;
    }

    internal int IndexOf(T value)
    {
        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < count; i++)
        {
            if (comparer.Equals(array![start + i], value))
            {
                return i;
            }
        }
        return -1;
    }

    internal bool Contains(T value) => IndexOf(value) >= 0;

    private static void ThrowIndex() => throw new ArgumentOutOfRangeException("index");

    internal struct Enumerator : IEnumerator<T>
    {
        private readonly T[]? array;
        private readonly int end;
        private int index;

        internal Enumerator(Items<T> items)
        {
            array = items.array;
            index = items.start - 1;
            end = items.start + items.count;
        }

        public readonly T Current => array![index];

        readonly object? System.Collections.IEnumerator.Current => Current;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public bool MoveNext() => ++index < end;

        public void Reset() => throw new NotSupportedException();

        public readonly void Dispose()
        {
        }
    }
}

/// <summary>The collection-expression builder of <see cref="Items{T}"/> (a view of a copy).</summary>
internal static class ItemsBuilder
{
    internal static Items<T> Create<T>(ReadOnlySpan<T> items) => items.IsEmpty ? default : new Items<T>(items.ToArray());

    /// <summary>core.ts concatenate: the other list when one is empty, else a new list with both.</summary>
    internal static Items<T> Concatenate<T>(Items<T> first, Items<T> second)
    {
        if (second.Count == 0)
        {
            return first;
        }
        if (first.Count == 0)
        {
            return second;
        }
        var result = new T[first.Count + second.Count];
        first.AsSpan().CopyTo(result);
        second.AsSpan().CopyTo(result.AsSpan(first.Count));
        return new Items<T>(result);
    }
}
