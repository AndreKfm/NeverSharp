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

/// <summary>utilities.ts (the text-range helpers).</summary>
internal static partial class Utilities
{
    internal static bool PositionIsSynthesized(int pos)
    {
        // This is a fast way of testing the following conditions:
        //  pos === undefined || pos === null || isNaN(pos) || pos < 0;
        return !(pos >= 0);
    }

    /// <summary>Bypasses immutability and directly sets the <c>pos</c> property of a <c>TextRange</c> or <c>Node</c>.</summary>
    internal static T SetTextRangePos<T>(T range, int pos)
        where T : ITextRange
    {
        range.Pos = pos;
        return range;
    }

    /// <summary>Bypasses immutability and directly sets the <c>end</c> property of a <c>TextRange</c> or <c>Node</c>.</summary>
    internal static T SetTextRangeEnd<T>(T range, int end)
        where T : ITextRange
    {
        range.End = end;
        return range;
    }

    /// <summary>Bypasses immutability and directly sets the <c>pos</c> and <c>end</c> properties of a <c>TextRange</c> or <c>Node</c>.</summary>
    internal static T SetTextRangePosEnd<T>(T range, int pos, int end)
        where T : ITextRange
    {
        return SetTextRangeEnd(SetTextRangePos(range, pos), end);
    }

    /// <summary>
    /// Bypasses immutability and directly sets the <c>pos</c> and <c>end</c> properties of a <c>TextRange</c> or <c>Node</c> from the
    /// provided position and width.
    /// </summary>
    internal static T SetTextRangePosWidth<T>(T range, int pos, int width)
        where T : ITextRange
    {
        return SetTextRangePosEnd(range, pos, pos + width);
    }
}
