// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's node lists (pre-registration §5.4: node lists are (start, count) ranges in an
// index arena), step O7a.

using System.Runtime.CompilerServices;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// A node list (TypeScript's NodeArray): a handle into the list store of the <see cref="NodeStore"/> of one parse.
/// Elements, text range, `hasTrailingComma`, transform flags and the missing-list mark are read and written through
/// the store. <see cref="None"/> (the default value) is TypeScript's `undefined` where a NodeArray is expected; an empty
/// list is a list with a header of its own. Two handles are equal when they denote the same NodeArray object.
/// </summary>
internal readonly struct NodeList : IEquatable<NodeList>
{
    internal readonly int Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal NodeList(int index)
    {
        Index = index;
    }

    /// <summary>No list (TypeScript: undefined).</summary>
    public static NodeList None => default;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsNone => Index == 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(NodeList left, NodeList right) => left.Index == right.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(NodeList left, NodeList right) => left.Index != right.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(NodeList other) => Index == other.Index;

    public override bool Equals(object? obj) => obj is NodeList other && Index == other.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public override int GetHashCode() => Index;

    public override string ToString() => IsNone ? "None" : "NodeList#" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// A list under construction (TypeScript: the plain array the parser pushes elements to before createNodeArray turns
/// it into a NodeArray): a frame of the store's scratch stack, from <see cref="NodeStore.BeginList"/> until
/// <see cref="NodeStore.FinishList"/> copies its elements into the list arena (or <see cref="NodeStore.DiscardList"/>).
/// </summary>
internal readonly struct ListBuilder
{
    internal readonly int Frame;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ListBuilder(int frame)
    {
        Frame = frame;
    }
}

/// <summary>
/// The list store of a parse (step O7a layout: ranges in one index arena).
/// <para>
/// Finished lists: every list is a header (start and count of its elements in the element arena, pos, end, transform
/// flags, and the bits `hasTrailingComma` and missing list), and a <see cref="NodeList"/> is the index of its header.
/// The elements of all lists lie in one array of node handles, each list's elements contiguous. Header 0 is
/// <see cref="NodeList.None"/>. A list is immutable once finished (TypeScript's NodeArray is a ReadonlyArray), except
/// for its range and transform flags, which createNodeArray's callers set.
/// </para>
/// <para>
/// Lists under construction: a reusable scratch stack of node handles with one frame per open list. Nested lists
/// (an element parser that parses lists itself) open frames above the enclosing one and finish before the enclosing
/// list grows again, so a frame normally grows at the top of the stack and is popped when it is finished. A frame
/// that grows while it is not at the top (TypeScript code that pushes to two plain arrays alternately, such as the
/// JSDoc parser's comment parts and tags) is first moved to the top; the space it leaves is reclaimed when the frames
/// below it are finished.
/// </para>
/// <para>
/// Sizing and lifetime as for the node records (NodeStore.cs): the header, element, scratch and frame arrays are
/// rented from the shared pool while the parse runs; when the SourceFile node is created, the headers and elements
/// move into arrays of the exact size (pinned; they hold no references) and the scratch arrays go back to the pool.
/// Speculative parsing truncates the list store with the node store (<see cref="Mark"/>).
/// </para>
/// </summary>
internal sealed partial class NodeStore
{
    private const int TrailingCommaBit = 1;
    private const int MissingListBit = 2;

    private struct ListHeader
    {
        public int Start;
        public int Count;
        public int Pos;
        public int End;
        public int TransformFlags;
        public int Bits;
    }

    private ListHeader[] lists = null!;
    private int listCount;
    private Node[] listElements = null!;
    private int listElementCount;

    // Lists under construction: scratch[0..scratchTop) holds the elements of the open frames; frames holds (start,
    // count) per frame, count -1 for a frame finished out of order (popped when the frames above it are).
    private Node[]? scratch;
    private int scratchTop;
    private int[]? frames;
    private int frameCount;

    // Whether the list arrays are scratch arrays of the pool (until the SourceFile node is created).
    private bool listsPooled;

    private void InitLists(int textLength)
    {
        // From the tuning corpus: 0.016 lists and 0.027 list elements per character on average (rounded up by the pool;
        // fewer than 10 of 24,511 files grow these arrays).
        lists = Scratch<ListHeader>.Rent((textLength / 64) + 16);
        listElements = Scratch<Node>.Rent((textLength / 32) + 16);
        listsPooled = true;
        // Header 0 is NodeList.None: no elements, range -1.
        lists[0] = new ListHeader { Pos = -1, End = -1 };
        listCount = 1;
        listElementCount = 0;
    }

    //// Finished lists.

    /// <summary>The elements of a list (empty for <see cref="NodeList.None"/>). The span stays valid while lists are added.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<Node> Nodes(NodeList list)
    {
        ref var h = ref lists[list.Index];
        return new ReadOnlySpan<Node>(listElements, h.Start, h.Count);
    }

    /// <summary>The number of elements of a list (TypeScript: `list.length`).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Length(NodeList list) => lists[list.Index].Count;

    /// <summary>The element at an index (TypeScript: `list[index]`).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public Node Element(NodeList list, int index) => Nodes(list)[index];

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public int Pos(NodeList list) => lists[list.Index].Pos;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void SetPos(NodeList list, int value) => lists[list.Index].Pos = value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public int End(NodeList list) => lists[list.Index].End;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void SetEnd(NodeList list, int value) => lists[list.Index].End = value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool HasTrailingComma(NodeList list) => (lists[list.Index].Bits & TrailingCommaBit) != 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public TransformFlags TransformFlags(NodeList list) => (TransformFlags)lists[list.Index].TransformFlags;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void SetTransformFlags(NodeList list, TransformFlags value) => lists[list.Index].TransformFlags = (int)value;

    /// <summary>parser.ts: `isMissingList(arr)`, the property createMissingList assigns.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsMissingList(NodeList list) => (lists[list.Index].Bits & MissingListBit) != 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void SetIsMissingList(NodeList list) => lists[list.Index].Bits |= MissingListBit;

    /// <summary>A new list with a copy of the given elements, range -1, transform flags none (the factory aggregates them).</summary>
    public NodeList NewList(ReadOnlySpan<Node> elements, bool hasTrailingComma = false)
    {
        var start = listElementCount;
        if (elements.Length > listElements.Length - start)
        {
            GrowListElements(start + elements.Length);
        }
        // The source may be the elements of an existing list (the old array stays intact when the arena grows).
        elements.CopyTo(listElements.AsSpan(start));
        listElementCount = start + elements.Length;
        return AddHeader(start, elements.Length, hasTrailingComma);
    }

    private NodeList AddHeader(int start, int count, bool hasTrailingComma)
    {
        var index = listCount;
        if (index == lists.Length)
        {
            GrowLists();
        }
        ref var h = ref lists[index];
        h.Start = start;
        h.Count = count;
        h.Pos = -1;
        h.End = -1;
        h.TransformFlags = 0;
        h.Bits = hasTrailingComma ? TrailingCommaBit : 0;
        listCount = index + 1;
        return new NodeList(index);
    }

    private void GrowLists()
    {
        var length = lists.Length * 2;
        var grown = listsPooled ? Scratch<ListHeader>.Rent(length) : GC.AllocateUninitializedArray<ListHeader>(length);
        Array.Copy(lists, grown, listCount);
        if (listsPooled)
        {
            Scratch<ListHeader>.Return(lists);
        }
        lists = grown;
    }

    private void GrowListElements(int needed)
    {
        var length = Math.Max(listElements.Length * 2, needed);
        var grown = listsPooled ? Scratch<Node>.Rent(length) : GC.AllocateUninitializedArray<Node>(length);
        Array.Copy(listElements, grown, listElementCount);
        // Spans handed out by Elements may still read the old array, so it is not given back to the pool (another
        // parse could overwrite it); the collector takes it.
        listElements = grown;
    }

    //// Lists under construction.

    /// <summary>Opens a list under construction (TypeScript: `const list = [];`).</summary>
    public ListBuilder BeginList()
    {
        if (frames is null)
        {
            RentScratch();
        }
        var f = frameCount;
        if (2 * f + 2 > frames!.Length)
        {
            Array.Resize(ref frames, frames.Length * 2);
        }
        frames[2 * f] = scratchTop;
        frames[(2 * f) + 1] = 0;
        frameCount = f + 1;
        return new ListBuilder(f);
    }

    /// <summary>Appends an element to a list under construction (TypeScript: `list.push(node)`).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Push(ListBuilder list, Node node)
    {
        var fr = frames!;
        var i = 2 * list.Frame;
        var count = fr[i + 1];
        var top = scratchTop;
        if (fr[i] + count != top || top == scratch!.Length)
        {
            top = MakeRoom(i);
        }
        scratch![top] = node;
        scratchTop = top + 1;
        fr[i + 1] = count + 1;
    }

    /// <summary>Moves the frame to the top of the scratch stack if it is not there, and makes room for one element.</summary>
    private int MakeRoom(int i)
    {
        var fr = frames!;
        var start = fr[i];
        var count = fr[i + 1];
        var top = scratchTop;
        var atTop = start + count == top;
        var needed = top + (atTop ? 0 : count) + 1;
        if (needed > scratch!.Length)
        {
            var grown = new Node[Math.Max(scratch.Length * 2, needed)];
            Array.Copy(scratch, grown, top);
            scratch = grown;
        }
        if (!atTop)
        {
            Array.Copy(scratch, start, scratch, top, count);
            fr[i] = top;
            top += count;
            scratchTop = top;
        }
        return top;
    }

    /// <summary>The number of elements of a list under construction (TypeScript: `list.length`).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public int Length(ListBuilder list) => frames![(2 * list.Frame) + 1];

    /// <summary>The elements of a list under construction; valid until the next change of the scratch stack.</summary>
    public ReadOnlySpan<Node> Nodes(ListBuilder list)
    {
        var i = 2 * list.Frame;
        return new ReadOnlySpan<Node>(scratch, frames![i], frames[i + 1]);
    }

    /// <summary>
    /// Turns a list under construction into a list (the copy into the arena of createNodeArray): range -1, transform
    /// flags none (the factory aggregates them). The builder must not be used afterwards.
    /// </summary>
    public NodeList FinishList(ListBuilder list, bool hasTrailingComma = false)
    {
        var fr = frames!;
        var i = 2 * list.Frame;
        var start = fr[i];
        var count = fr[i + 1];
        Debug.Assert(count >= 0, "List finished twice");
        var at = listElementCount;
        if (count > listElements.Length - at)
        {
            GrowListElements(at + count);
        }
        Array.Copy(scratch!, start, listElements, at, count);
        listElementCount = at + count;
        PopFrame(list.Frame, start, count);
        return AddHeader(at, count, hasTrailingComma);
    }

    /// <summary>Closes a list under construction without creating a list (TypeScript: the plain array is dropped).</summary>
    public void DiscardList(ListBuilder list)
    {
        var i = 2 * list.Frame;
        PopFrame(list.Frame, frames![i], frames[i + 1]);
    }

    private void PopFrame(int frame, int start, int count)
    {
        var fr = frames!;
        if (start + count == scratchTop)
        {
            // The topmost region of the scratch stack: every other open frame lies below it.
            scratchTop = start;
        }
        fr[(2 * frame) + 1] = -1;
        var f = frameCount;
        while (f > 0 && fr[(2 * f) - 1] < 0)
        {
            f--;
        }
        frameCount = f;
        if (f == 0)
        {
            scratchTop = 0;
        }
    }

    private void RentScratch()
    {
        if (listsPooled)
        {
            scratch = Scratch<Node>.Rent(1024);
            frames = Scratch<int>.Rent(256);
        }
        else
        {
            scratch = new Node[64];
            frames = new int[32];
        }
        scratchTop = 0;
        frameCount = 0;
    }

    /// <summary>Moves the lists into arrays of the exact size and returns the scratch arrays (with <see cref="Seal"/>).</summary>
    private void SealLists()
    {
        var exact = GC.AllocateUninitializedArray<ListHeader>(listCount, pinned: true);
        Array.Copy(lists, exact, listCount);
        Scratch<ListHeader>.Return(lists);
        lists = exact;
        var exactElements = GC.AllocateUninitializedArray<Node>(listElementCount, pinned: true);
        Array.Copy(listElements, exactElements, listElementCount);
        Scratch<Node>.Return(listElements);
        listElements = exactElements;
        if (frames is not null && frameCount == 0)
        {
            Scratch<Node>.Return(scratch!);
            Scratch<int>.Return(frames);
            scratch = null;
            frames = null;
        }
        // (Open frames at the seal keep their scratch arrays; they are not returned to the pool.)
        listsPooled = false;
    }
}
