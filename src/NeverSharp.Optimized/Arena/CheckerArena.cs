// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O / tsrs-only (module AR1), native memory per checker for the checker's
// reference-free stores, after tsrs's per-thread checker arenas (tsrs crates/tsrs_core/src/arena.rs; external/tsrs
// docs/RUST.md and docs/CHECKER.md, the arena and handle model; notes/mem-checker-gc.md: tsrs does not collect checker data; notes/mem-checker-scratch.md:
// free lists per size class). TypeScript 6.0.3 has no counterpart (its checker data lives on the JavaScript heap).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NeverSharp.Optimized;

/// <summary>
/// The native memory of one checker (module AR1): blocks for the checker's stores whose records hold no references
/// (link slot pages, node link tables, record chunks), outside the collector's reach, so that a collection neither
/// copies nor scans them. Small blocks (up to <see cref="SmallMax"/> bytes, power-of-two classes) are carved from 64 KB
/// slabs and reused through a free list per class; larger blocks are allocated one by one. Every block is zeroed when
/// handed out.
/// <para>
/// Ownership and lifetime: the arena owns every block it hands out. A store keeps a reference to its checker's arena, so
/// the arena lives as long as any store that can read its blocks; when the checker and its stores are unreachable the
/// finalizer frees all blocks in one piece (tsrs frees a checker thread's arenas in one piece). A block given back with
/// <see cref="Free"/> is not read again by its store (the store has moved to a new block). One thread uses a checker at a
/// time, so the arena takes no locks.
/// </para>
/// </summary>
internal sealed unsafe class CheckerArena
{
    /// <summary>Blocks up to this size come from the slabs of the size classes.</summary>
    internal const int SmallMax = 2048;

    private const int SlabSize = 64 << 10;
    private const int MinShift = 4; // the smallest class: 16 bytes
    private const int Classes = 8;  // 16 .. 2048 bytes

    private readonly nint[] freeLists = new nint[Classes];
    private readonly HashSet<nint> large = [];
    private nint[] slabs = new nint[8];
    private int slabCount;
    private byte* bump;
    private nint bumpLeft;

    ~CheckerArena()
    {
        // (finalizer only: nothing else frees the blocks; the HashSet and the slab array are still valid here)
        for (var i = 0; i < slabCount; i++)
        {
            NativeMemory.Free((void*)slabs[i]);
        }
        foreach (var block in large)
        {
            NativeMemory.Free((void*)block);
        }
    }

    /// <summary>A zeroed block of at least <paramref name="bytes"/> bytes (small blocks: the class size).</summary>
    internal void* Allocate(nint bytes)
    {
        if (bytes > SmallMax)
        {
            var block = NativeMemory.AllocZeroed((nuint)bytes);
            large.Add((nint)block);
            return block;
        }
        var c = ClassOf(bytes);
        var size = (nint)1 << (c + MinShift);
        var head = freeLists[c];
        if (head != 0)
        {
            freeLists[c] = *(nint*)head;
            NativeMemory.Clear((void*)head, (nuint)size);
            return (void*)head;
        }
        if (bumpLeft < size)
        {
            NewSlab();
        }
        var result = bump;
        bump += size;
        bumpLeft -= size;
        return result;
    }

    /// <summary>Gives back a block of <paramref name="bytes"/> bytes (the size it was allocated with); its store no longer reads it.</summary>
    internal void Free(void* block, nint bytes)
    {
        if (block == null)
        {
            return;
        }
        if (bytes > SmallMax)
        {
            if (large.Remove((nint)block))
            {
                NativeMemory.Free(block);
            }
            return;
        }
        var c = ClassOf(bytes);
        *(nint*)block = freeLists[c];
        freeLists[c] = (nint)block;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ClassOf(nint bytes) =>
        bytes <= 1 << MinShift ? 0 : System.Numerics.BitOperations.Log2((uint)(bytes - 1)) + 1 - MinShift;

    private void NewSlab()
    {
        // (the rest of the old slab stays unused)
        var slab = (byte*)NativeMemory.AllocZeroed(SlabSize);
        if (slabCount == slabs.Length)
        {
            Array.Resize(ref slabs, slabs.Length * 2);
        }
        slabs[slabCount++] = (nint)slab;
        bump = slab;
        bumpLeft = SlabSize;
    }
}

/// <summary>
/// Records of one reference-free kind in fixed-size native chunks of a <see cref="CheckerArena"/> (module AR1; the
/// counterpart of <see cref="Chunks{T}"/> outside the collector's reach): growing adds a chunk, a <c>ref</c> to a record
/// stays valid while the store grows, records start zeroed. Track O / tsrs-only (tsrs keeps records in arena chunks).
/// </summary>
internal sealed unsafe class NativeChunks<T>
    where T : unmanaged
{
    private const int Shift = 12;
    private const int Size = 1 << Shift;
    private const int Mask = Size - 1;

    private readonly CheckerArena arena;
    private nint[] chunks = new nint[4];

    internal NativeChunks(CheckerArena arena)
    {
        this.arena = arena;
    }

    internal ref T this[int index]
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => ref ((T*)chunks[index >> Shift])[index & Mask];
    }

    /// <summary>Makes <paramref name="index"/> addressable.</summary>
    internal void EnsureIndex(int index)
    {
        var chunk = index >> Shift;
        if (chunk >= chunks.Length)
        {
            Array.Resize(ref chunks, Math.Max(chunks.Length * 2, chunk + 1));
        }
        if (chunks[chunk] == 0)
        {
            chunks[chunk] = (nint)arena.Allocate((nint)Size * sizeof(T));
        }
    }
}
