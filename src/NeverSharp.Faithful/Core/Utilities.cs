// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/core/text.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

namespace NeverSharp.Faithful;

/// <summary>
/// types.ts: <c>interface TextRange</c> (<c>pos</c> and <c>end</c>). A <c>Node</c> and a <c>NodeArray</c> are text ranges;
/// TypeScript's <c>ReadonlyTextRange</c> differs from it only in the compile-time <c>readonly</c>, which the setters below bypass.
/// </summary>
internal interface ITextRange
{
    int Pos { get; set; }

    int End { get; set; }
}

// The node classes are generated (Generated/Nodes.g.cs); a partial declaration adds the TextRange view.
public abstract partial class Node : ITextRange
{
    int ITextRange.Pos
    {
        get => Pos;
        set => Pos = value;
    }

    int ITextRange.End
    {
        get => End;
        set => End = value;
    }
}

public sealed partial class NodeArray : ITextRange
{
    int ITextRange.Pos
    {
        get => Pos;
        set => Pos = value;
    }

    int ITextRange.End
    {
        get => End;
        set => End = value;
    }
}

/// <summary>The text-range helpers of utilities.ts.</summary>
internal static partial class Utilities
{
    // utilities.ts:10265
    internal static bool PositionIsSynthesized(int pos)
    {
        // This is a fast way of testing the following conditions:
        //  pos === undefined || pos === null || isNaN(pos) || pos < 0;
        return !(pos >= 0);
    }

    /// <summary>Bypasses immutability and directly sets the `pos` property of a `TextRange` or `Node`.</summary>
    // utilities.ts:10644
    internal static T SetTextRangePos<T>(T range, int pos)
        where T : ITextRange
    {
        range.Pos = pos;
        return range;
    }

    /// <summary>Bypasses immutability and directly sets the `end` property of a `TextRange` or `Node`.</summary>
    // utilities.ts:10654
    internal static T SetTextRangeEnd<T>(T range, int end)
        where T : ITextRange
    {
        range.End = end;
        return range;
    }

    /// <summary>Bypasses immutability and directly sets the `pos` and `end` properties of a `TextRange` or `Node`.</summary>
    // utilities.ts:10664
    internal static T SetTextRangePosEnd<T>(T range, int pos, int end)
        where T : ITextRange
    {
        return SetTextRangeEnd(SetTextRangePos(range, pos), end);
    }

    /// <summary>
    /// Bypasses immutability and directly sets the `pos` and `end` properties of a `TextRange` or `Node` from the
    /// provided position and width.
    /// </summary>
    // utilities.ts:10674
    internal static T SetTextRangePosWidth<T>(T range, int pos, int width)
        where T : ITextRange
    {
        return SetTextRangePosEnd(range, pos, pos + width);
    }
}
