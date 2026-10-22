// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/jsnum/pseudobigint.go and internal/core/core.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Globalization;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>utilities.ts (the units the scanner reaches).</summary>
internal static partial class Utilities
{
    /// <summary>
    /// Calls `callback` for each entry in the map, returning the first truthy result.
    /// Use `map.forEach` instead for normal iteration.
    /// </summary>
    internal static TResult? ForEachEntry<TKey, TValue, TResult>(IEnumerable<KeyValuePair<TKey, TValue>> map, Func<TValue, TKey, TResult?> callback)
    {
        foreach (var (key, value) in map)
        {
            var result = callback(value, key);
            if (IsTruthyValue(result))
            {
                return result;
            }
        }
        return default;
    }

    internal static string? GetNameOfScriptTarget(ScriptTarget scriptTarget)
    {
        return ForEachEntry(CommandLineParser.TargetOptionDeclaration.Type, (value, key) => value == scriptTarget ? key : null);
    }

    /// <summary>
    /// Converts a bigint literal string, e.g. `0x1234n`,
    /// to its decimal string representation, e.g. `4660`.
    /// </summary>
    internal static string ParsePseudoBigInt(string stringValue)
    {
        int log2Base;
        switch (CharCodeAtOrNaN(stringValue, 1))
        { // "x" in "0x123"
            case (int)CharacterCodes.b:
            case (int)CharacterCodes.B: // 0b or 0B
                log2Base = 1;
                break;
            case (int)CharacterCodes.o:
            case (int)CharacterCodes.O: // 0o or 0O
                log2Base = 3;
                break;
            case (int)CharacterCodes.x:
            case (int)CharacterCodes.X: // 0x or 0X
                log2Base = 4;
                break;
            default: // already in decimal; omit trailing "n"
                var nIndex = stringValue.Length - 1;
                // Skip leading 0s
                var nonZeroStart = 0;
                while (CharCodeAtOrNaN(stringValue, nonZeroStart) == (int)CharacterCodes._0)
                {
                    nonZeroStart++;
                }
                var sliced = SliceString(stringValue, nonZeroStart, nIndex);
                return sliced.Length != 0 ? sliced : "0";
        }

        // Omit leading "0b", "0o", or "0x", and trailing "n"
        int startIndex = 2, endIndex = stringValue.Length - 1;
        var bitsNeeded = (endIndex - startIndex) * log2Base;
        // Stores the value specified by the string as a LE array of 16-bit integers
        // using Uint16 instead of Uint32 so combining steps can use bitwise operators
        var segments = new ushort[(int)((uint)bitsNeeded >> 4) + ((bitsNeeded & 15) != 0 ? 1 : 0)];
        // Add the digits, one at a time
        for (int i = endIndex - 1, bitOffset = 0; i >= startIndex; i--, bitOffset += log2Base)
        {
            var segment = (int)((uint)bitOffset >> 4);
            int digitChar = stringValue[i];
            // Find character range: 0-9 < A-F < a-f
            var digit = digitChar <= (int)CharacterCodes._9
                ? digitChar - (int)CharacterCodes._0
                : 10 + digitChar -
                    (digitChar <= (int)CharacterCodes.F ? (int)CharacterCodes.A : (int)CharacterCodes.a);
            var shiftedDigit = digit << (bitOffset & 15);
            // Uint16Array stores ToUint16 of the value; writes outside the array are ignored.
            if (segment < segments.Length)
            {
                segments[segment] = (ushort)(segments[segment] | shiftedDigit);
            }
            var residual = (int)((uint)shiftedDigit >> 16);
            if (residual != 0 && segment + 1 < segments.Length)
            {
                segments[segment + 1] = (ushort)(segments[segment + 1] | residual); // overflows segment
            }
        }
        // Repeatedly divide segments by 10 and add remainder to base10Value
        var base10Value = "";
        var firstNonzeroSegment = segments.Length - 1;
        var segmentsRemaining = true;
        while (segmentsRemaining)
        {
            var mod10 = 0;
            segmentsRemaining = false;
            for (var segment = firstNonzeroSegment; segment >= 0; segment--)
            {
                var newSegment = mod10 << 16 | segments[segment];
                var segmentValue = newSegment / 10;
                segments[segment] = (ushort)segmentValue;
                mod10 = newSegment - segmentValue * 10;
                if (segmentValue != 0 && !segmentsRemaining)
                {
                    firstNonzeroSegment = segment;
                    segmentsRemaining = true;
                }
            }
            base10Value = mod10.ToString(CultureInfo.InvariantCulture) + base10Value;
        }
        return base10Value;
    }

    //// JavaScript semantics the ported functions rely on.

    /// <summary>JavaScript truthiness of a value (`if (result)`): undefined, null, false, 0, NaN and "" are falsy.</summary>
    private static bool IsTruthyValue<T>(T value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            string s => s.Length != 0,
            int i => i != 0,
            double d => d != 0 && !double.IsNaN(d),
            Node n => !n.IsNone, // Track O: a node handle; None is undefined
            _ => true,
        };
    }

    /// <summary>`s.charCodeAt(i)`; NaN (outside the string) is int.MinValue, which equals no character code.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int CharCodeAtOrNaN(ReadOnlySpan<char> s, int i)
    {
        return (uint)i < (uint)s.Length ? s[i] : int.MinValue;
    }

    /// <summary>JavaScript's String.prototype.slice(start, end): negative offsets count from the end.</summary>
    private static string SliceString(string s, int start, int end)
    {
        var length = s.Length;
        var from = start < 0 ? Math.Max(length + start, 0) : Math.Min(start, length);
        var to = end < 0 ? Math.Max(length + end, 0) : Math.Min(end, length);
        return from < to ? s.Substring(from, to - from) : "";
    }
}
