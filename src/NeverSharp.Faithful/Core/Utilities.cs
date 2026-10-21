// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/utilities.go and internal/core/text.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

namespace NeverSharp.Faithful;

/// <summary>utilities.ts.</summary>
internal static partial class Utilities
{
    internal static bool PositionIsSynthesized(int pos)
    {
        // This is a fast way of testing the following conditions:
        //  pos === undefined || pos === null || isNaN(pos) || pos < 0;
        return !(pos >= 0);
    }

    /// <summary>Bypasses immutability and directly sets the `pos` property of a `TextRange` or `Node`.</summary>
    internal static T SetTextRangePos<T>(T range, int pos)
        where T : TextRange
    {
        range.Pos = pos;
        return range;
    }

    /// <summary>Bypasses immutability and directly sets the `end` property of a `TextRange` or `Node`.</summary>
    internal static T SetTextRangeEnd<T>(T range, int end)
        where T : TextRange
    {
        range.End = end;
        return range;
    }

    /// <summary>Bypasses immutability and directly sets the `pos` and `end` properties of a `TextRange` or `Node`.</summary>
    internal static T SetTextRangePosEnd<T>(T range, int pos, int end)
        where T : TextRange
    {
        return SetTextRangeEnd(SetTextRangePos(range, pos), end);
    }

    /// <summary>
    /// Bypasses immutability and directly sets the `pos` and `end` properties of a `TextRange` or `Node` from the
    /// provided position and width.
    /// </summary>
    internal static T SetTextRangePosWidth<T>(T range, int pos, int width)
        where T : TextRange
    {
        return SetTextRangePosEnd(range, pos, pos + width);
    }
}
