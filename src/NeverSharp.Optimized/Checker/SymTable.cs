// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's symbol tables of the checker (modules C1, CO1, CO2, CO3;
// docs/phase3-checker-layout.md).

namespace NeverSharp.Optimized;

/// <summary>
/// A symbol table the checker owns (types.ts SymbolTable: a Map from escaped name to symbol, iterated in insertion
/// order; Map.set on an existing key keeps its position). Binder tables are read through a cached copy of this kind
/// (<see cref="Checker.TableOf"/>), so the checker sees one table type for binder and transient symbols.
/// Layout (module CO1): the entries (name, symbol) in insertion order; up to <see cref="SymTableStore.LinearLimit"/>
/// entries are found by a scan, larger tables by an open-addressing index (linear probing, load at most 1/2, entry
/// number + 1 per slot, 0 free). Module CO2: names are ids of the checker's <see cref="NameTable"/>. Module CO3: a table
/// is a handle to a record of its checker's <see cref="SymTableStore"/>, its entries and index ranges of the store's
/// arenas, so a table is no object of its own; copying the handle does not copy the table (as a reference to a Map),
/// <c>==</c> is the identity of the Map.
/// </summary>
internal readonly struct SymTable : IEquatable<SymTable>
{
    private readonly SymTableStore store;
    private readonly int id;

    /// <summary>new Map(): a new table of the checker the name table belongs to.</summary>
    internal SymTable(NameTable names, int capacity = 0)
    {
        store = names.Tables;
        id = store.Create(capacity);
    }

    /// <summary>new Map(other): a copy in the same order.</summary>
    internal SymTable(SymTable other)
    {
        store = other.store;
        id = store.Copy(other.id);
    }

    /// <summary>new Map(other) of a table checked for undefined.</summary>
    internal SymTable(SymTable? other)
        : this(other!.Value)
    {
    }

    /// <summary>
    /// <c>table ??= new Map()</c> on a table field of a record (IsNone there is undefined): the table, created if none.
    /// </summary>
    internal static SymTable OrNew(ref SymTable table, NameTable names)
    {
        if (table.IsNone)
        {
            table = new SymTable(names);
        }
        return table;
    }

    /// <summary>The table as TypeScript's <c>table | undefined</c> (null when IsNone).</summary>
    internal SymTable? OrNull
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => store is null ? null : this;
    }

    /// <summary>Whether this is no table (<c>default</c>: undefined in a record, or an object type's members before they are resolved).</summary>
    internal bool IsNone
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => store is null;
    }

    internal int Count
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => store.Count(id);
    }

    /// <summary>
    /// Module EO2 (Track O / tsrs-only): a number that changes whenever the contents change (Set, Delete, Clear), so an
    /// index of the table can tell that it is out of date (<see cref="Checker"/>'s export table index).
    /// </summary>
    internal int Version
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => store.Version(id);
    }

    /// <summary>Module EO2 (Track O / tsrs-only): from now on a change of the contents counts in SymTableStore.WatchedChanges.</summary>
    internal void Watch() => store.Watch(id);

    /// <summary>Map.get (None when absent).</summary>
    internal SymbolRef Get(string name)
    {
        var nameId = store.Names.Find(name);
        return nameId < 0 ? SymbolRef.None : store.Get(id, nameId);
    }

    /// <summary>Map.get by name id (None when absent).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SymbolRef Get(int name) => store.Get(id, name);

    internal bool Has(string name)
    {
        var nameId = store.Names.Find(name);
        return nameId >= 0 && store.Find(id, nameId) >= 0;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool Has(int name) => store.Find(id, name) >= 0;

    /// <summary>Module EO2: the position of the entry named <paramref name="name"/> (a name id) in table order, -1 when absent.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int IndexOf(int name) => store.Find(id, name);

    /// <summary>Map.set.</summary>
    internal void Set(string name, SymbolRef symbol) => store.Set(id, store.Names.Intern(name), symbol);

    /// <summary>Map.set by name id.</summary>
    internal void Set(int name, SymbolRef symbol) => store.Set(id, name, symbol);

    /// <summary>Makes room for <paramref name="capacity"/> entries (no change to the contents).</summary>
    internal void EnsureCapacity(int capacity) => store.EnsureCapacity(id, capacity);

    /// <summary>Map.clear (the ranges are kept for reuse).</summary>
    internal void Clear() => store.Clear(id);

    /// <summary>Map.delete: true when the name was present (the entries after it keep their order).</summary>
    internal bool Delete(string name)
    {
        var nameId = store.Names.Find(name);
        return nameId >= 0 && store.Delete(id, nameId);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal string NameAt(int i) => store.Names[store.NameIdAt(id, i)];

    /// <summary>The name id of entry <paramref name="i"/>.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int NameIdAt(int i) => store.NameIdAt(id, i);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SymbolRef ValueAt(int i) => store.ValueAt(id, i);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(SymTable left, SymTable right) => left.id == right.id && ReferenceEquals(left.store, right.store);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(SymTable left, SymTable right) => !(left == right);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(SymTable other) => this == other;

    public override bool Equals(object? obj) => obj is SymTable other && this == other;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override int GetHashCode() => id;
}

/// <summary>
/// The SymTable members the checker reads on a table it has checked for undefined (module CO3: the table is a nullable
/// struct, which flow analysis does not unwrap; on undefined they throw, as the class members did).
/// </summary>
internal static class SymTableExtensions
{
    extension(SymTable? table)
    {
        internal int Count => table!.Value.Count;

        internal SymbolRef Get(string name) => table!.Value.Get(name);

        internal SymbolRef Get(int name) => table!.Value.Get(name);

        internal bool Has(string name) => table!.Value.Has(name);

        internal bool Has(int name) => table!.Value.Has(name);

        internal void Set(string name, SymbolRef symbol) => table!.Value.Set(name, symbol);

        internal void Set(int name, SymbolRef symbol) => table!.Value.Set(name, symbol);

        internal void EnsureCapacity(int capacity) => table!.Value.EnsureCapacity(capacity);

        internal void Clear() => table!.Value.Clear();

        internal bool Delete(string name) => table!.Value.Delete(name);

        internal string NameAt(int i) => table!.Value.NameAt(i);

        internal int NameIdAt(int i) => table!.Value.NameIdAt(i);

        internal SymbolRef ValueAt(int i) => table!.Value.ValueAt(i);
    }
}

/// <summary>
/// The symbol tables of one checker (module CO3): per table a record (entries range, count, capacity, index range),
/// entries (name id, symbol) and index slots in chunked arenas that hold no references. A range never crosses a chunk
/// (a range longer than a chunk gets a chunk of its own); a table that outgrows its range moves to a new one, and freed
/// ranges of a power-of-two capacity are reused by later tables.
/// </summary>
internal sealed class SymTableStore
{
    internal const int LinearLimit = 8;

    private struct Record
    {
        public int Entries;   // start of the entry range (chunk << Shift | offset)
        public int Count;
        public int Capacity;
        public int Buckets;   // start of the index range; meaningful when BucketCount > 0
        public int BucketCount; // 0: no index, at most LinearLimit entries, found by a scan
        public int Version;   // module EO2: counts the changes of the contents (Set, Delete, Clear)
        public bool Watched;  // module EO2: a change also counts in WatchedChanges
    }

    /// <summary>
    /// Module EO2 (Track O / tsrs-only): the number of changes of the contents of watched tables (<see cref="Watch"/>), so
    /// an index that reads many tables can tell with one number that one of them changed (Checker.ExportIndex.cs).
    /// </summary>
    internal int WatchedChanges { get; private set; }

    internal void Watch(int id) => RecordOf(id).Watched = true;

    private void Changed(ref Record r)
    {
        r.Version++;
        if (r.Watched)
        {
            WatchedChanges++;
        }
    }

    internal struct Entry
    {
        public int Name;
        public SymbolRef Value;
    }

    // [0] unused; one array (no references inside: growing copies plain data). No method holds a ref to a record across
    // the creation of another table.
    private Record[] records = new Record[64];
    private int recordCount;
    private readonly RangeArena<Entry> entries = new();
    private readonly RangeArena<int> buckets = new();

    internal SymTableStore(NameTable names)
    {
        Names = names;
    }

    internal NameTable Names { get; }

    internal int Create(int capacity)
    {
        recordCount++;
        if (recordCount == records.Length)
        {
            Array.Resize(ref records, records.Length * 2);
        }
        ref var r = ref records[recordCount];
        r = default;
        if (capacity > 0)
        {
            r.Capacity = RangeArena<Entry>.RoundCapacity(capacity);
            r.Entries = entries.Allocate(r.Capacity, clear: false);
        }
        return recordCount;
    }

    internal int Copy(int other)
    {
        var o = records[other];
        var id = Create(o.Count);
        ref var r = ref RecordOf(id);
        if (o.Count > 0)
        {
            entries.Span(o.Entries, o.Count).CopyTo(entries.Span(r.Entries, o.Count));
        }
        r.Count = o.Count;
        if (o.BucketCount > 0)
        {
            r.BucketCount = o.BucketCount;
            r.Buckets = buckets.Allocate(o.BucketCount, clear: false);
            buckets.Span(o.Buckets, o.BucketCount).CopyTo(buckets.Span(r.Buckets, o.BucketCount));
        }
        return id;
    }

    // (ids come from Create only, so they are in range: no bounds check on the hot paths)
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private ref Record RecordOf(int id) =>
        ref System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(new Span<Record>(records)), id);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int Count(int id) => RecordOf(id).Count;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int Version(int id) => RecordOf(id).Version;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SymbolRef Get(int id, int name)
    {
        ref var r = ref RecordOf(id);
        var i = Find(ref r, name);
        return i >= 0 ? entries.At(r.Entries, i).Value : SymbolRef.None;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int Find(int id, int name) => Find(ref RecordOf(id), name);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int NameIdAt(int id, int i) => EntryAt(id, i).Name;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SymbolRef ValueAt(int id, int i) => EntryAt(id, i).Value;

    // entry i of a table (bounds-checked by its chunk, as the array-backed table was by its capacity)
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private ref Entry EntryAt(int id, int i) => ref entries.At(RecordOf(id).Entries, i);

    internal void Set(int id, int name, SymbolRef symbol)
    {
        ref var r = ref RecordOf(id);
        Changed(ref r);
        var i = Find(ref r, name);
        if (i >= 0)
        {
            entries.At(r.Entries, i).Value = symbol;
            return;
        }
        if (r.Count == r.Capacity)
        {
            Grow(ref r, Math.Max(4, r.Count * 2));
        }
        entries.At(r.Entries, r.Count) = new Entry { Name = name, Value = symbol };
        r.Count++;
        if (r.BucketCount > 0)
        {
            if (r.Count * 2 > r.BucketCount)
            {
                Rehash(ref r, r.BucketCount * 2);
            }
            else
            {
                Insert(ref r, r.Count - 1);
            }
        }
        else if (r.Count > LinearLimit)
        {
            Rehash(ref r, BucketCountFor(r.Capacity));
        }
    }

    internal void EnsureCapacity(int id, int capacity)
    {
        ref var r = ref RecordOf(id);
        if (capacity > r.Capacity)
        {
            Grow(ref r, capacity);
            if (r.BucketCount > 0 && capacity * 2 > r.BucketCount)
            {
                Rehash(ref r, BucketCountFor(capacity));
            }
        }
    }

    internal void Clear(int id)
    {
        ref var r = ref RecordOf(id);
        Changed(ref r);
        if (r.BucketCount > 0)
        {
            var b = buckets.Span(r.Buckets, r.BucketCount);
            if (r.Count * 8 >= r.BucketCount)
            {
                b.Clear();
            }
            else
            {
                // A large index of a table reused with few entries: free just their slots, the last entry first (with
                // linear probing and no deletions, the probe path of an entry only crosses slots of earlier entries).
                var mask = r.BucketCount - 1;
                for (var entry = r.Count - 1; entry >= 0; entry--)
                {
                    var slot = Hash(entries.At(r.Entries, entry).Name) & mask;
                    while (b[slot] != entry + 1)
                    {
                        slot = (slot + 1) & mask;
                    }
                    b[slot] = 0;
                }
            }
        }
        r.Count = 0;
    }

    internal bool Delete(int id, int name)
    {
        ref var r = ref RecordOf(id);
        var i = Find(ref r, name);
        if (i < 0)
        {
            return false;
        }
        var e = entries.Span(r.Entries, r.Count);
        e[(i + 1)..].CopyTo(e[i..]);
        r.Count--;
        Changed(ref r);
        if (r.BucketCount > 0)
        {
            if (r.Count <= LinearLimit)
            {
                buckets.Free(r.Buckets, r.BucketCount);
                r.BucketCount = 0;
            }
            else
            {
                Rehash(ref r, r.BucketCount);
            }
        }
        return true;
    }

    // The entry number of the name, -1 when absent.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int Find(ref Record r, int name)
    {
        // (the ranges hold Count entries and BucketCount slots: no bounds checks inside them)
        var count = r.Count;
        if (count == 0)
        {
            return -1;
        }
        ref var e = ref entries.Ref(r.Entries);
        if (r.BucketCount == 0)
        {
            for (var i = 0; i < count; i++)
            {
                if (System.Runtime.CompilerServices.Unsafe.Add(ref e, i).Name == name)
                {
                    return i;
                }
            }
            return -1;
        }
        ref var b = ref buckets.Ref(r.Buckets);
        var mask = r.BucketCount - 1;
        for (var slot = Hash(name) & mask; ; slot = (slot + 1) & mask)
        {
            var entry = System.Runtime.CompilerServices.Unsafe.Add(ref b, slot);
            if (entry == 0)
            {
                return -1;
            }
            if (System.Runtime.CompilerServices.Unsafe.Add(ref e, entry - 1).Name == name)
            {
                return entry - 1;
            }
        }
    }

    private void Grow(ref Record r, int capacity)
    {
        capacity = RangeArena<Entry>.RoundCapacity(capacity);
        var start = entries.Allocate(capacity, clear: false);
        if (r.Count > 0)
        {
            entries.Span(r.Entries, r.Count).CopyTo(entries.Span(start, r.Count));
        }
        if (r.Capacity > 0)
        {
            entries.Free(r.Entries, r.Capacity);
        }
        r.Entries = start;
        r.Capacity = capacity;
    }

    private void Rehash(ref Record r, int bucketCount)
    {
        if (r.BucketCount != bucketCount)
        {
            if (r.BucketCount > 0)
            {
                buckets.Free(r.Buckets, r.BucketCount);
            }
            r.Buckets = buckets.Allocate(bucketCount, clear: true);
            r.BucketCount = bucketCount;
        }
        else
        {
            buckets.Span(r.Buckets, r.BucketCount).Clear();
        }
        for (var i = 0; i < r.Count; i++)
        {
            Insert(ref r, i);
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void Insert(ref Record r, int entry)
    {
        var b = buckets.Span(r.Buckets, r.BucketCount);
        var mask = b.Length - 1;
        var slot = Hash(entries.At(r.Entries, entry).Name) & mask;
        while (b[slot] != 0)
        {
            slot = (slot + 1) & mask;
        }
        b[slot] = entry + 1;
    }

    // a power of two with load at most 1/2
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int BucketCountFor(int capacity) => (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(capacity * 2, 32));

    // a mix of the name id for the low bits the index uses
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int Hash(int name)
    {
        var h = (uint)name * 0x9E3779B1u;
        h ^= h >> 15;
        return (int)(h & 0x7FFFFFFF);
    }
}

/// <summary>
/// A chunked arena of mutable ranges (module CO3, for <see cref="SymTableStore"/>): ranges of power-of-two lengths in
/// chunks that never move; a range never crosses a chunk (a range longer than a chunk gets a chunk of its own). Freed
/// ranges are kept in one free list per length and reused first. No per-range objects.
/// </summary>
internal sealed class RangeArena<T>
    where T : struct
{
    private const int Shift = 16;
    private const int Size = 1 << Shift;
    private const int FreeClasses = 17; // lengths 1 .. Size

    private T[][] chunks = [new T[256]]; // (the first chunk small: Gate C's checkers check one small file each)
    private int chunk;
    private int used;
    private readonly List<int>[] free = new List<int>[FreeClasses];

    /// <summary>The length a range for <paramref name="capacity"/> elements gets: a power of two.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static int RoundCapacity(int capacity) => (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(capacity, 1));

    /// <summary>
    /// A range of <paramref name="length"/> elements (a power of two): its start. Cleared when <paramref name="clear"/>,
    /// else a reused range keeps its old contents.
    /// </summary>
    internal int Allocate(int length, bool clear)
    {
        var cls = System.Numerics.BitOperations.Log2((uint)length);
        if (cls < FreeClasses && free[cls] is { Count: > 0 } list)
        {
            var start = list[^1];
            list.RemoveAt(list.Count - 1);
            if (clear)
            {
                Span(start, length).Clear();
            }
            return start;
        }
        if (length > chunks[chunk].Length - used)
        {
            chunk++;
            if (chunk == chunks.Length)
            {
                Array.Resize(ref chunks, chunks.Length * 2);
            }
            chunks[chunk] = new T[Math.Max(length, chunk == 1 ? 4096 : Size)];
            used = 0;
        }
        var result = chunk << Shift | used;
        used += length;
        return result;
    }

    /// <summary>Gives a range back for reuse.</summary>
    internal void Free(int start, int length)
    {
        var cls = System.Numerics.BitOperations.Log2((uint)length);
        if (cls < FreeClasses)
        {
            (free[cls] ??= []).Add(start);
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Span<T> Span(int start, int length) => chunks[start >> Shift].AsSpan(start & (Size - 1), length);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ref T At(int start, int index) => ref chunks[start >> Shift][(start & (Size - 1)) + index];

    /// <summary>The first element of a range (the caller stays inside the range's length).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ref T Ref(int start) =>
        ref System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(new Span<T>(chunks[start >> Shift])), start & (Size - 1));
}
