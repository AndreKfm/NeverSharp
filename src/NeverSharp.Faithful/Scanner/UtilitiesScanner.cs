// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts and src/compiler/commandLineParser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/jsnum/pseudobigint.go and internal/core/compileroptions.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Globalization;
using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>commandLineParser.ts: CommandLineOptionOfCustomType (only the members `getNameOfScriptTarget` needs).</summary>
internal sealed class CommandLineOptionOfCustomType
{
    public required string Name { get; init; }

    public string? ShortName { get; init; }

    /// <summary>The `type` map of the option: custom type names to values, in insertion order (a JavaScript `Map`).</summary>
    public required IReadOnlyList<KeyValuePair<string, ScriptTarget>> Type { get; init; }
}

/// <summary>commandLineParser.ts (the part the scanner reaches).</summary>
internal static partial class CommandLineParser
{
    // commandLineParser.ts: targetOptionDeclaration; the members beyond name, shortName and type (affects*, paramType, category,
    // description, ...) are not read by the scanner and not ported.
    internal static readonly CommandLineOptionOfCustomType TargetOptionDeclaration = new()
    {
        Name = "target",
        ShortName = "t",
        Type =
        [
            new("es3", ScriptTarget.ES3),
            new("es5", ScriptTarget.ES5),
            new("es6", ScriptTarget.ES2015),
            new("es2015", ScriptTarget.ES2015),
            new("es2016", ScriptTarget.ES2016),
            new("es2017", ScriptTarget.ES2017),
            new("es2018", ScriptTarget.ES2018),
            new("es2019", ScriptTarget.ES2019),
            new("es2020", ScriptTarget.ES2020),
            new("es2021", ScriptTarget.ES2021),
            new("es2022", ScriptTarget.ES2022),
            new("es2023", ScriptTarget.ES2023),
            new("es2024", ScriptTarget.ES2024),
            new("es2025", ScriptTarget.ES2025),
            new("esnext", ScriptTarget.ESNext),
        ],
    };
}

/// <summary>utilities.ts (the units of the scanner).</summary>
internal static partial class Utilities
{
    /// <summary>
    /// utilities.ts: forEachEntry. Calls the callback for each entry of the map, in insertion order, until it returns a truthy value,
    /// and returns that value.
    /// </summary>
    internal static U? ForEachEntry<K, V, U>(IEnumerable<KeyValuePair<K, V>> map, Func<V, K, U?> callback)
    {
        foreach (var (key, value) in map)
        {
            var result = callback(value, key);
            if (IsTruthyResult(result))
            {
                return result;
            }
        }
        return default;
    }

    private static bool IsTruthyResult<T>(T value)
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

    /// <summary>utilities.ts: getNameOfScriptTarget.</summary>
    internal static string? GetNameOfScriptTarget(ScriptTarget scriptTarget)
    {
        return ForEachEntry<string, ScriptTarget, string>(CommandLineParser.TargetOptionDeclaration.Type, (value, key) => value == scriptTarget ? key : null);
    }

    /// <summary>
    /// utilities.ts: parsePseudoBigInt.
    /// Converts a bigint literal string, e.g. `0x1234n`,
    /// to its decimal string representation, e.g. `4660`.
    /// </summary>
    internal static string ParsePseudoBigInt(string stringValue)
    {
        int log2Base;
        switch (ScannerCharCodeAt(stringValue, 1)) // "x" in "0x123"
        {
            case Ch.LowerB:
            case Ch.UpperB: // 0b or 0B
                log2Base = 1;
                break;
            case Ch.LowerO:
            case Ch.UpperO: // 0o or 0O
                log2Base = 3;
                break;
            case Ch.LowerX:
            case Ch.UpperX: // 0x or 0X
                log2Base = 4;
                break;
            default: // already in decimal; omit trailing "n"
                var nIndex = stringValue.Length - 1;
                // Skip leading 0s
                var nonZeroStart = 0;
                while (ScannerCharCodeAt(stringValue, nonZeroStart) == Ch.Digit0)
                {
                    nonZeroStart++;
                }
                var sliced = ScannerSlice(stringValue, nonZeroStart, nIndex);
                return sliced.Length != 0 ? sliced : "0";
        }

        // Omit leading "0b", "0o", or "0x", and trailing "n"
        const int StartIndex = 2;
        var endIndex = stringValue.Length - 1;
        var bitsNeeded = (endIndex - StartIndex) * log2Base;
        // Stores the value specified by the string as a LE array of 16-bit integers
        // using Uint16 instead of Uint32 so combining steps can use bitwise operators
        var segments = new ushort[Math.Max(0, (int)((uint)bitsNeeded >> 4) + ((bitsNeeded & 15) != 0 ? 1 : 0))];
        // Add the digits, one at a time
        for (int i = endIndex - 1, bitOffset = 0; i >= StartIndex; i--, bitOffset += log2Base)
        {
            var segment = (int)((uint)bitOffset >> 4);
            var digitChar = ScannerCharCodeAt(stringValue, i);
            // Find character range: 0-9 < A-F < a-f
            var digit = digitChar <= Ch.Digit9
                ? digitChar - Ch.Digit0
                : 10 + digitChar -
                    (digitChar <= Ch.UpperF ? Ch.UpperA : Ch.LowerA);
            var shiftedDigit = digit << (bitOffset & 15);
            // Assignments to a Uint16Array element wrap modulo 2^16 and are ignored outside the array.
            if ((uint)segment < (uint)segments.Length)
            {
                segments[segment] = (ushort)(segments[segment] | shiftedDigit);
            }
            var residual = (int)((uint)shiftedDigit >> 16);
            if (residual != 0 && (uint)(segment + 1) < (uint)segments.Length)
            {
                segments[segment + 1] = (ushort)(segments[segment + 1] | residual); // overflows segment
            }
        }
        // Repeatedly divide segments by 10 and add remainder to base10Value. The digits are collected least significant first and
        // reversed at the end: prepending each digit to a string (as TypeScript does) is quadratic in the number of digits.
        var base10Digits = new List<char>();
        var firstNonzeroSegment = segments.Length - 1;
        var segmentsRemaining = true;
        while (segmentsRemaining)
        {
            var mod10 = 0;
            segmentsRemaining = false;
            for (var segment = firstNonzeroSegment; segment >= 0; segment--)
            {
                var newSegment = (mod10 << 16) | segments[segment];
                var segmentValue = newSegment / 10;
                segments[segment] = (ushort)segmentValue;
                mod10 = newSegment - (segmentValue * 10);
                if (segmentValue != 0 && !segmentsRemaining)
                {
                    firstNonzeroSegment = segment;
                    segmentsRemaining = true;
                }
            }
            base10Digits.Add((char)('0' + mod10));
        }
        base10Digits.Reverse();
        return new string(base10Digits.ToArray());
    }

    /// <summary>`text.charCodeAt(pos)`: the UTF-16 code unit, or -1 (standing for NaN, equal to no character code) outside the string.</summary>
    private static int ScannerCharCodeAt(string text, int pos)
    {
        return (uint)pos < (uint)text.Length ? text[pos] : -1;
    }

    /// <summary>`text.slice(start, end)` for non-negative arguments.</summary>
    private static string ScannerSlice(string text, int start, int end)
    {
        start = Math.Min(start, text.Length);
        end = Math.Min(end, text.Length);
        return start < end ? text.Substring(start, end - start) : string.Empty;
    }
}
