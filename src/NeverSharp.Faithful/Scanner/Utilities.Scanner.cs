// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/jsnum/pseudobigint.go and internal/core/core.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

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
            if (IsTruthyEntryResult(result))
            {
                return result;
            }
        }
        return default;
    }

    internal static string? GetNameOfScriptTarget(ScriptTarget scriptTarget)
    {
        return ForEachEntry(CommandLineParser.TargetOptionDeclaration.Type, (value, key) => value == (int)scriptTarget ? key : null);
    }

    /// <summary>
    /// Converts a bigint literal string, e.g. `0x1234n`,
    /// to its decimal string representation, e.g. `4660`.
    /// </summary>
    internal static string ParsePseudoBigInt(string stringValue)
    {
        int log2Base;
        switch (CharCodeAt(stringValue, 1))
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
                while (CharCodeAt(stringValue, nonZeroStart) == (int)CharacterCodes._0)
                {
                    nonZeroStart++;
                }
                var sliced = Slice(stringValue, nonZeroStart, nIndex);
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
            // A write to a Uint16Array keeps the low 16 bits (ToUint16).
            segments[segment] = (ushort)(segments[segment] | shiftedDigit);
            var residual = (int)((uint)shiftedDigit >> 16);
            if (residual != 0 && segment + 1 < segments.Length)
            {
                // overflows segment (a write past the end of a typed array is ignored, hence the bounds check)
                segments[segment + 1] = (ushort)(segments[segment + 1] | residual);
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
            base10Value = (char)('0' + mod10) + base10Value;
        }
        return base10Value;
    }

    // String.prototype.charCodeAt: NaN outside the string (all comparisons with NaN are false; -1 is never compared here
    // except for equality with character codes, which it cannot match).
    private static int CharCodeAt(string text, int index)
    {
        return index >= 0 && index < text.Length ? text[index] : -1;
    }

    // String.prototype.slice(start, end) with a possibly negative end.
    private static string Slice(string text, int start, int end)
    {
        var length = text.Length;
        var from = start < 0 ? Math.Max(length + start, 0) : Math.Min(start, length);
        var to = end < 0 ? Math.Max(length + end, 0) : Math.Min(end, length);
        return from < to ? text[from..to] : "";
    }

    // JavaScript truthiness of the callback results forEachEntry receives.
    private static bool IsTruthyEntryResult<T>(T value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            string s => s.Length != 0,
            int i => i != 0,
            double d => d != 0 && !double.IsNaN(d),
            _ => true,
        };
    }
}
