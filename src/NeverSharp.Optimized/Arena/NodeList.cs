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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static NodeList None => default;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool IsNone => Index == 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator ==(NodeList left, NodeList right) => left.Index == right.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static bool operator !=(NodeList left, NodeList right) => left.Index != right.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public bool Equals(NodeList other) => Index == other.Index;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
/// Memory and lifetime as for the node records (NodeStore.cs, step O9 variant C): the headers and the elements are
/// ranges of the thread's slabs of list headers and list elements, committed with the records; the scratch stack and
/// its frames are the thread's, lent to the store while it parses. Speculative parsing truncates the list store with
/// the node store (<see cref="Mark"/>).
/// </para>
/// </summary>
internal sealed unsafe partial class NodeStore
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

    // The list headers (listCapacity of them from lists on) and the list elements (elementCapacity from listElements
    // on): ranges of the thread's slabs.
    private ListHeader* lists;
    private int listCapacity;
    private int listCount;
    private SlabRange listRange;
    private Node* listElements;
    private int elementCapacity;
    private int listElementCount;
    private SlabRange elementRange;

    // Lists under construction: scratch[0..scratchTop) holds the elements of the open frames; frames holds (start,
    // count) per frame, count -1 for a frame finished out of order (popped when the frames above it are).
    private Node[]? scratch;
    private int scratchTop;
    private int[]? frames;
    private int frameCount;

    private void InitLists(ThreadSlabs local, int textLength)
    {
        // From the tuning corpus: 0.016 lists and 0.027 list elements per character on average (estimates only: the
        // ranges reach to the end of the slabs).
        lists = (ListHeader*)listRange.Open(local, SlabKind.ListHeaders, (((long)textLength / 64) + 16) * sizeof(ListHeader));
        listCapacity = (int)(listRange.Capacity / sizeof(ListHeader));
        listElements = (Node*)elementRange.Open(local, SlabKind.ListElements, (((long)textLength / 32) + 16) * sizeof(Node));
        elementCapacity = (int)(elementRange.Capacity / sizeof(Node));
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
        return new ReadOnlySpan<Node>(listElements + h.Start, h.Count);
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public NodeList NewList(ReadOnlySpan<Node> elements, bool hasTrailingComma = false)
    {
        var start = listElementCount;
        if (elements.Length > elementCapacity - start)
        {
            GrowListElements(start + elements.Length);
        }
        // The source may be the elements of an existing list (the old range stays intact when the arena moves).
        elements.CopyTo(new Span<Node>(listElements + start, elements.Length));
        listElementCount = start + elements.Length;
        return AddHeader(start, elements.Length, hasTrailingComma);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private NodeList AddHeader(int start, int count, bool hasTrailingComma)
    {
        var index = listCount;
        if (index == listCapacity)
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

    [NoAllocUnchecked("arena growth: the list header range moves to a larger slab")]
    private void GrowLists()
    {
        lists = (ListHeader*)listRange.Grow((long)listCount * sizeof(ListHeader), ((long)listCount + 1) * sizeof(ListHeader), IsActive ? owner : null, SlabKind.ListHeaders);
        listCapacity = (int)(listRange.Capacity / sizeof(ListHeader));
    }

    [NoAllocUnchecked("arena growth: the list element range moves to a larger slab")]
    private void GrowListElements(int needed)
    {
        // Spans handed out by Nodes may still read the old range: the store keeps holding the slab it leaves.
        listElements = (Node*)elementRange.Grow((long)listElementCount * sizeof(Node), (long)needed * sizeof(Node), IsActive ? owner : null, SlabKind.ListElements);
        elementCapacity = (int)(elementRange.Capacity / sizeof(Node));
    }

    //// Lists under construction.

    /// <summary>Opens a list under construction (TypeScript: `const list = [];`).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public ListBuilder BeginList()
    {
        if (frames is null)
        {
            RentScratch();
        }
        var f = frameCount;
        if (2 * f + 2 > frames!.Length)
        {
            GrowFrames();
        }
        frames[2 * f] = scratchTop;
        frames[(2 * f) + 1] = 0;
        frameCount = f + 1;
        return new ListBuilder(f);
    }

    /// <summary>Appends an element to a list under construction (TypeScript: `list.push(node)`).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
            GrowScratch(needed, top);
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

    [NoAllocUnchecked("arena growth: the frame array of lists under construction doubles")]
    private void GrowFrames()
    {
        Array.Resize(ref frames, frames!.Length * 2);
    }

    [NoAllocUnchecked("arena growth: the scratch stack of lists under construction doubles")]
    private void GrowScratch(int needed, int top)
    {
        var grown = new Node[Math.Max(scratch!.Length * 2, needed)];
        Array.Copy(scratch, grown, top);
        scratch = grown;
    }

    /// <summary>The number of elements of a list under construction (TypeScript: `list.length`).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public int Length(ListBuilder list) => frames![(2 * list.Frame) + 1];

    /// <summary>The elements of a list under construction; valid until the next change of the scratch stack.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public ReadOnlySpan<Node> Nodes(ListBuilder list)
    {
        var i = 2 * list.Frame;
        return new ReadOnlySpan<Node>(scratch, frames![i], frames[i + 1]);
    }

    /// <summary>
    /// Turns a list under construction into a list (the copy into the arena of createNodeArray): range -1, transform
    /// flags none (the factory aggregates them). The builder must not be used afterwards.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public NodeList FinishList(ListBuilder list, bool hasTrailingComma = false)
    {
        var fr = frames!;
        var i = 2 * list.Frame;
        var start = fr[i];
        var count = fr[i + 1];
        Debug.Assert(count >= 0, "List finished twice");
        var at = listElementCount;
        if (count > elementCapacity - at)
        {
            GrowListElements(at + count);
        }
        var target = listElements + at;
        if (count <= 8)
        {
            // Most lists are short (step O12): a plain loop instead of the call to the block copy.
            var source = scratch!;
            for (var k = 0; k < count; k++)
            {
                target[k] = source[start + k];
            }
        }
        else
        {
            new ReadOnlySpan<Node>(scratch, start, count).CopyTo(new Span<Node>(target, count));
        }
        listElementCount = at + count;
        PopFrame(list.Frame, start, count);
        return AddHeader(at, count, hasTrailingComma);
    }

    /// <summary>
    /// createNodeArray for a list under construction in one go (Track O, step O28): the elements are copied into the
    /// arena while their transform flags are aggregated (nodeFactory.ts aggregateChildrenFlags, the union of
    /// propagateChildFlags over the elements; <see cref="ChildFlags"/>), and the header is written once with its range
    /// (<paramref name="pos"/>, <paramref name="end"/>), the transform flags and `hasTrailingComma`. The same values as
    /// <see cref="FinishList"/> followed by NodeFactory.AggregateChildrenFlags and setTextRangePosEnd: reading the
    /// elements' records does not depend on the list, and nothing else is written in between.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public NodeList FinishNodeArray(ListBuilder list, bool hasTrailingComma, int pos, int end)
    {
        var fr = frames!;
        var i = 2 * list.Frame;
        var start = fr[i];
        var count = fr[i + 1];
        Debug.Assert(count >= 0, "List finished twice");
        var at = listElementCount;
        if (count > elementCapacity - at)
        {
            GrowListElements(at + count);
        }
        var target = listElements + at;
        // (One range check for the span; none per element.)
        var source = new ReadOnlySpan<Node>(scratch, start, count);
        var transformFlags = 0;
        for (var k = 0; k < source.Length; k++)
        {
            var node = source[k];
            target[k] = node;
            transformFlags |= ChildFlags(node);
        }
        listElementCount = at + count;
        PopFrame(list.Frame, start, count);
        var index = listCount;
        if (index == listCapacity)
        {
            GrowLists();
        }
        ref var h = ref lists[index];
        h.Start = at;
        h.Count = count;
        h.Pos = pos;
        h.End = end;
        h.TransformFlags = transformFlags;
        h.Bits = hasTrailingComma ? TrailingCommaBit : 0;
        listCount = index + 1;
        return new NodeList(index);
    }

    /// <summary>
    /// nodeFactory.ts propagateChildFlags of an element (NodeFactory.PropagateChildFlags, step O12's form): the child's
    /// transform flags without its kind's subtree exclusions, plus the property-name propagating flags of its name when
    /// it has one that is a property name.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ChildFlags(Node child)
    {
        if (child.IsNone)
        {
            return 0;
        }
        var flags = data[child.Index + TransformFlagsWord] & ~(int)TransformFlagsSubtreeExclusions(Kind(child));
        var name = Name(child);
        // (UtilitiesPublic.IsPropertyName: the kinds of a property name.)
        SyntaxKind nameKind;
        if (!name.IsNone &&
            ((nameKind = Kind(name)) == SyntaxKind.Identifier || nameKind == SyntaxKind.PrivateIdentifier ||
                nameKind == SyntaxKind.StringLiteral || nameKind == SyntaxKind.NumericLiteral || nameKind == SyntaxKind.ComputedPropertyName))
        {
            flags |= data[name.Index + TransformFlagsWord] & (int)Generated.TransformFlags.PropertyNamePropagatingFlags;
        }
        return flags;
    }

    /// <summary>Closes a list under construction without creating a list (TypeScript: the plain array is dropped).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public void DiscardList(ListBuilder list)
    {
        var i = 2 * list.Frame;
        PopFrame(list.Frame, frames![i], frames[i + 1]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAllocUnchecked("arena growth: the scratch arrays of lists under construction, lent by the thread")]
    private void RentScratch()
    {
        var local = IsActive ? owner! : null;
        if (local is not null && local.ListScratch is not null)
        {
            scratch = local.ListScratch;
            frames = local.ListFrames;
            local.ListScratch = null;
            local.ListFrames = null;
        }
        else
        {
            scratch = new Node[local is not null ? 1024 : 64];
            frames = new int[local is not null ? 256 : 32];
        }
        scratchTop = 0;
        frameCount = 0;
    }

    /// <summary>Commits the list header and element ranges and gives the scratch arrays back to the thread (with <see cref="Seal"/>).</summary>
    [NoAllocUnchecked("per-file result: the slab ranges of the lists the finished tree keeps")]
    private void SealLists()
    {
        var local = IsActive ? owner : null;
        listRange.Commit((long)listCount * sizeof(ListHeader), local, SlabKind.ListHeaders);
        listCapacity = listCount;
        elementRange.Commit((long)listElementCount * sizeof(Node), local, SlabKind.ListElements);
        elementCapacity = listElementCount;
        if (frames is not null && frameCount == 0)
        {
            if (local is not null && local.ListScratch is null)
            {
                local.ListScratch = scratch;
                local.ListFrames = frames;
            }
            scratch = null;
            frames = null;
        }
        // (Open frames at the seal keep their scratch arrays.)
    }

    /// <summary>Drops the holds on the list slabs (<see cref="Release"/>).</summary>
    private void ReleaseLists()
    {
        lists = null;
        listCapacity = 0;
        listCount = 0;
        listRange.Release();
        listElements = null;
        elementCapacity = 0;
        listElementCount = 0;
        elementRange.Release();
    }
}
