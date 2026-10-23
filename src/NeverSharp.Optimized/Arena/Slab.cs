// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's per-thread slabs for the node data of parse results (step O9, variant C).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NeverSharp.Optimized;

/// <summary>
/// A large block of memory that holds the node data of many parse results (step O9, variant C): records, list headers,
/// list elements, or source texts and arena characters, one kind per slab. A thread fills its current slab of each kind
/// parse after parse, each parse writing one contiguous range after the ranges of the parses before it; a range, once
/// its parse is committed, is never written by another parse (results stay independent).
/// <para>
/// Memory: native memory from <see cref="SlabKind.NativeSize"/> on (a thread that parses many files; on the tuning
/// corpus native and pinned managed slabs measured the same time, native ones without the 250 MB the collector would
/// count), a pinned managed array for the small slabs a thread starts with, so that a thread that parses only a few
/// files before it ends (one thread per file, as the dump tool runs) leaves garbage the collector sees and accounts for.
/// </para>
/// <para>
/// Lifetime: the slab counts its holders, the thread while the slab is its current one and every store with a range in
/// it. <see cref="SourceFile.Release"/> drops a store's holds; the memory is freed when no holder is left. A result that
/// is dropped without that keeps its hold, and the finalizer frees the memory once neither the thread nor any store
/// references the slab (the backstop, as for the source text).
/// </para>
/// </summary>
internal sealed unsafe class Slab
{
    private nint memory;
    private byte[]? array;
    private int holders;

    /// <summary>A slab of <paramref name="size"/> bytes, held once (by its creator): native memory or a pinned array.</summary>
    internal Slab(long size, bool native)
    {
        if (native)
        {
            memory = (nint)NativeMemory.Alloc((nuint)size);
        }
        else
        {
            array = GC.AllocateUninitializedArray<byte>(checked((int)size), pinned: true);
            memory = (nint)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(array));
        }
        Size = size;
        holders = 1;
    }

    ~Slab()
    {
        Free();
    }

    /// <summary>The first byte (null once freed).</summary>
    internal byte* Memory
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => (byte*)memory;
    }

    /// <summary>The size in bytes.</summary>
    internal long Size { get; }

    /// <summary>The bytes handed out to committed ranges (the owning thread's bump offset).</summary>
    internal long Used { get; set; }

    /// <summary>Whether the owning thread is the only holder (every range in the slab was released).</summary>
    internal bool OnlyOwner => Volatile.Read(ref holders) == 1;

    internal void AddHolder()
    {
        Interlocked.Increment(ref holders);
    }

    internal void RemoveHolder()
    {
        if (Interlocked.Decrement(ref holders) == 0)
        {
            // (The finalizer still runs later and finds nothing to free.)
            Free();
        }
    }

    private void Free()
    {
        var block = Interlocked.Exchange(ref memory, 0);
        if (block != 0)
        {
            if (array is null)
            {
                NativeMemory.Free((void*)block);
            }
            array = null;
        }
    }
}

/// <summary>The kinds of slabs a thread keeps, and their sizes.</summary>
internal static class SlabKind
{
    internal const int Records = 0;
    internal const int ListHeaders = 1;
    internal const int ListElements = 2;
    internal const int Chars = 3;
    internal const int Count = 4;

    /// <summary>The size of a thread's first slab of each kind (at least; a thread's slabs double up to the full size).</summary>
    internal const long FirstSize = 64 << 10;

    /// <summary>Slabs of this size and larger are native memory (the thread has parsed a few dozen files at least).</summary>
    internal const long NativeSize = 256 << 10;

    // Full slab sizes in bytes (from the tuning corpus: per character of source text about 2.5 bytes of records, 0.4 of
    // list headers, 0.15 of list elements, and 2 of source text plus 0.7 of arena characters in the character slabs, so
    // one slab of each kind holds the data of roughly 1.5 million characters of source text).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static ReadOnlySpan<int> FullSizes => [4 << 20, 1 << 20, 512 << 10, 4 << 20];

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static long FullSize(int kind) => FullSizes[kind];
}

/// <summary>
/// The memory of one arena of a store (records, list headers, list elements or arena characters): a range of a slab
/// from <see cref="Start"/> on, <see cref="Capacity"/> bytes long. While its parse runs, the range of the parse that
/// owns the thread's slabs reaches to the end of the thread's current slab; when the parse is committed it shrinks to
/// the bytes in use, and the thread's next parse starts after it. An arena that outgrows its range moves to a new slab
/// (copied); the slabs it leaves stay held until the store is released, since spans of them may still be read.
/// </summary>
internal unsafe struct SlabRange
{
    internal byte* Start;
    internal long Capacity;
    internal Slab? Slab;

    // The slabs left by moves (each still held once by this range).
    private Slab[]? retired;
    private int retiredCount;

    /// <summary>Opens the range in the thread's current slab of the kind, or in a new one if fewer than <paramref name="estimate"/> bytes are left.</summary>
    internal byte* Open(ThreadSlabs local, int kind, long estimate)
    {
        var slab = local.Current[kind];
        if (slab is not null && slab.Used != 0 && slab.OnlyOwner)
        {
            // Every range in the slab was released: start over at its beginning.
            slab.Used = 0;
        }
        if (slab is null || slab.Size - slab.Used < estimate)
        {
            slab = local.Replace(kind, estimate * 2);
        }
        slab.AddHolder();
        Slab = slab;
        Start = slab.Memory + slab.Used;
        Capacity = slab.Size - slab.Used;
        return Start;
    }

    /// <summary>
    /// Moves the range to a slab with room for <paramref name="needed"/> bytes, copying the <paramref name="used"/> bytes
    /// in use: the thread's new current slab of the kind if <paramref name="local"/> is given (the store owns the
    /// thread's slabs), a slab of its own otherwise.
    /// </summary>
    internal byte* Grow(long used, long needed, ThreadSlabs? local, int kind)
    {
        Slab slab;
        if (local is not null)
        {
            slab = local.Replace(kind, needed * 2);
            slab.AddHolder();
        }
        else
        {
            slab = new Slab(Align(Math.Max(needed * 2, 64)), native: false);
        }
        Buffer.MemoryCopy(Start, slab.Memory, slab.Size, used);
        if (Slab is not null)
        {
            Retire(Slab);
        }
        Slab = slab;
        Start = slab.Memory;
        Capacity = slab.Size;
        return Start;
    }

    private void Retire(Slab slab)
    {
        retired ??= new Slab[2];
        if (retiredCount == retired.Length)
        {
            Array.Resize(ref retired, retiredCount * 2);
        }
        retired[retiredCount++] = slab;
    }

    /// <summary>
    /// The end of the parse: the range shrinks to the <paramref name="used"/> bytes, and if the store owns the thread's
    /// slabs (<paramref name="local"/> given), the thread's next range of the kind starts after it.
    /// </summary>
    internal void Commit(long used, ThreadSlabs? local, int kind)
    {
        Capacity = used;
        if (local is not null && Slab is not null && ReferenceEquals(local.Current[kind], Slab))
        {
            Slab.Used = Align((long)(Start - Slab.Memory) + used);
        }
    }

    /// <summary>Drops the range's holds on its slabs (the store is done); the range is empty afterwards.</summary>
    internal void Release()
    {
        Start = null;
        Capacity = 0;
        var slab = Slab;
        Slab = null;
        slab?.RemoveHolder();
        for (var i = 0; i < retiredCount; i++)
        {
            retired![i].RemoveHolder();
        }
        retired = null;
        retiredCount = 0;
    }

    // Ranges start at multiples of 16 bytes.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static long Align(long bytes) => (bytes + 15) & ~15L;
}

/// <summary>
/// The slabs of a thread: the current slab of each kind, the current object slot array (managed: object slots hold
/// references), and the store whose parse owns them (<see cref="Active"/>).
/// </summary>
internal sealed unsafe class ThreadSlabs
{
    // Object slots per slot array (references of the SourceFile node and a few other nodes, about 30 per file); a
    // thread's first array is smaller.
    internal const int RefSlabLength = 8192;
    internal const int FirstRefSlabLength = 256;

    [ThreadStatic]
    private static ThreadSlabs? local;

    internal readonly Slab?[] Current = new Slab?[SlabKind.Count];

    // The size of the thread's next slab of each kind: from SlabKind.FirstSize on, doubling up to the full size.
    private readonly long[] nextSize = new long[SlabKind.Count];

    internal object?[]? Refs;
    internal int RefsUsed;

    // The scratch stack and frames of lists under construction (NodeList.cs), lent to the active store while it parses.
    internal Node[]? ListScratch;
    internal int[]? ListFrames;

    /// <summary>The store whose parse writes to the thread's slabs (null between parses).</summary>
    internal object? Active;

    internal static ThreadSlabs Local => local ??= new ThreadSlabs();

    /// <summary>
    /// Makes <paramref name="store"/> the store that writes to the thread's slabs. A store that still owned them (a parse
    /// that was not committed) keeps its ranges, which reach to the end of the slabs, so the thread leaves those slabs.
    /// </summary>
    internal void TakeOver(object store)
    {
        if (Active is not null && !ReferenceEquals(Active, store))
        {
            for (var k = 0; k < SlabKind.Count; k++)
            {
                Current[k]?.RemoveHolder();
                Current[k] = null;
            }
            Refs = null;
            RefsUsed = 0;
        }
        Active = store;
    }

    /// <summary>
    /// A new current slab of the kind with at least <paramref name="minimum"/> bytes; the thread leaves the previous one
    /// (its committed ranges stay held by their stores). Slabs grow from <see cref="SlabKind.FirstSize"/> to the full
    /// size of the kind as the thread fills them; full-size (and larger) slabs are native memory.
    /// </summary>
    internal Slab Replace(int kind, long minimum)
    {
        var full = SlabKind.FullSize(kind);
        var size = Math.Max(nextSize[kind], SlabKind.FirstSize);
        nextSize[kind] = Math.Min(size * 2, full);
        size = SlabRange.Align(Math.Max(size, minimum));
        var slab = new Slab(size, native: size >= Math.Min(SlabKind.NativeSize, full));
        Current[kind]?.RemoveHolder();
        Current[kind] = slab;
        return slab;
    }

    /// <summary>
    /// A source text decoded into the thread's character slab (between parses: the text's range comes before the ranges
    /// of the parse that reads it); <paramref name="capacity"/> is the most characters the bytes decode to.
    /// </summary>
    internal NativeText DecodeText(ReadOnlySpan<byte> bytes, int capacity)
    {
        // One more character: a terminating NUL after the text.
        var needed = ((long)capacity + 1) * sizeof(char);
        var slab = Current[SlabKind.Chars];
        if (slab is not null && slab.Used != 0 && slab.OnlyOwner)
        {
            // Every range in the slab was released: start over at its beginning.
            slab.Used = 0;
        }
        if (slab is null || slab.Size - slab.Used < needed)
        {
            slab = Replace(SlabKind.Chars, needed);
        }
        var chars = (char*)(slab.Memory + slab.Used);
        var length = SourceText.Decode(bytes, new Span<char>(chars, capacity));
        chars[length] = '\0';
        slab.Used = SlabRange.Align(slab.Used + (((long)length + 1) * sizeof(char)));
        slab.AddHolder();
        return new NativeText(chars, length, slab);
    }

    /// <summary>The object slots for a new store: the rest of the current slot array, or a new one.</summary>
    internal void EnsureRefs()
    {
        if (Refs is null || Refs.Length - RefsUsed < 64)
        {
            Refs = new object?[Refs is null ? FirstRefSlabLength : RefSlabLength];
            RefsUsed = 0;
        }
    }
}
