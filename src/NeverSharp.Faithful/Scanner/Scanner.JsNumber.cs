// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/scanner.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/jsnum/string.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Globalization;
using System.Numerics;

namespace NeverSharp.Faithful;

/// <summary>
/// The JavaScript number conversions scanner.ts writes as operators and built-ins: `+s` (StringToNumber),
/// `"" + n` (Number::toString), `n.toString(radix)` and `parseInt(s, radix)`, for the strings the scanner
/// passes to them. typescript-go has the same conversions in internal/jsnum (Number.String, FromString).
/// </summary>
internal sealed partial class Scanner
{
    /// <summary>
    /// `+s` (StringToNumber) for the strings scanner.ts converts: decimal literals without separators (digits, an
    /// optional '.', optional digits, an optional exponent), the empty string, and "0x"/"0b"/"0o" integers.
    /// </summary>
    private static double StringToNumber(string s)
    {
        if (s.Length == 0)
        {
            return 0;
        }
        if (s.Length > 2 && s[0] == '0')
        {
            switch (s[1])
            {
                case 'x':
                case 'X':
                    return ParseInt(s[2..], 16, wholeStringOnly: true);
                case 'b':
                case 'B':
                    return ParseInt(s[2..], 2, wholeStringOnly: true);
                case 'o':
                case 'O':
                    return ParseInt(s[2..], 8, wholeStringOnly: true);
            }
        }
        // .NET parses decimal strings correctly rounded (IEEE 754), as StringToNumber does; overflow gives Infinity.
        return double.TryParse(s, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var value)
            ? value
            : double.NaN;
    }

    /// <summary>
    /// `parseInt(s, radix)` for radix 2, 8, 10 and 16: the longest prefix of digits of the radix (no sign or
    /// whitespace occurs in the scanner's strings), NaN without one. Powers of two are converted exactly rounded, as
    /// V8 and the specification do; radix 10 through the correctly rounded decimal conversion.
    /// </summary>
    private static double ParseInt(string s, int radix, bool wholeStringOnly = false)
    {
        var length = 0;
        while (length < s.Length && DigitValue(s[length]) < radix)
        {
            length++;
        }
        if (length == 0 || (wholeStringOnly && length != s.Length))
        {
            return double.NaN;
        }
        if (radix == 10)
        {
            return double.Parse(s.AsSpan(0, length), NumberStyles.None, CultureInfo.InvariantCulture);
        }
        var value = BigInteger.Zero;
        for (var i = 0; i < length; i++)
        {
            value = value * radix + DigitValue(s[i]);
        }
        return BigIntegerToDouble(value);
    }

    private static int DigitValue(char ch)
    {
        return ch switch
        {
            >= '0' and <= '9' => ch - '0',
            >= 'a' and <= 'z' => ch - 'a' + 10,
            >= 'A' and <= 'Z' => ch - 'A' + 10,
            _ => int.MaxValue,
        };
    }

    // A non-negative integer as the nearest double (ties to even), Infinity when out of range.
    private static double BigIntegerToDouble(BigInteger value)
    {
        var bitLength = (int)value.GetBitLength();
        if (bitLength <= 53)
        {
            return (long)value;
        }
        var shift = bitLength - 53;
        var mantissa = value >> shift;
        var remainder = value - (mantissa << shift);
        var half = BigInteger.One << (shift - 1);
        if (remainder > half || (remainder == half && !mantissa.IsEven))
        {
            mantissa += 1;
            if (mantissa.GetBitLength() > 53)
            {
                mantissa >>= 1;
                shift++;
            }
        }
        return Math.ScaleB((long)mantissa, shift);
    }

    /// <summary>`"" + n`: Number::toString(n) with radix 10 (ECMA-262 6.1.6.1.20).</summary>
    private static string NumberToString(double n)
    {
        if (double.IsNaN(n))
        {
            return "NaN";
        }
        if (n == 0)
        {
            return "0";
        }
        if (double.IsInfinity(n))
        {
            return n > 0 ? "Infinity" : "-Infinity";
        }
        if (n < 0)
        {
            return "-" + NumberToString(-n);
        }
        // Fast path: safe integers (internal/jsnum/string.go, Number.String).
        if (n <= 9007199254740991 && Math.Floor(n) == n)
        {
            return ((long)n).ToString(CultureInfo.InvariantCulture);
        }

        // The shortest digits s (k of them) and the exponent e with n = 0.s * 10^e. .NET's round-trip format
        // gives the shortest string that round-trips (and the nearest of those), as Number::toString requires.
        var roundTrip = n.ToString("R", CultureInfo.InvariantCulture);
        var exponentIndex = roundTrip.IndexOfAny(['E', 'e']);
        var mantissa = exponentIndex >= 0 ? roundTrip[..exponentIndex] : roundTrip;
        var exponent = exponentIndex >= 0 ? int.Parse(roundTrip.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : 0;
        var dot = mantissa.IndexOf('.', StringComparison.Ordinal);
        var integerDigits = dot >= 0 ? dot : mantissa.Length;
        var allDigits = dot >= 0 ? string.Concat(mantissa.AsSpan(0, dot), mantissa.AsSpan(dot + 1)) : mantissa;
        var leadingZeros = 0;
        while (leadingZeros < allDigits.Length - 1 && allDigits[leadingZeros] == '0')
        {
            leadingZeros++;
        }
        var digits = allDigits[leadingZeros..].TrimEnd('0');
        var k = digits.Length;
        var e = integerDigits - leadingZeros + exponent;

        if (k <= e && e <= 21)
        {
            return digits + new string('0', e - k);
        }
        if (0 < e && e <= 21)
        {
            return string.Concat(digits.AsSpan(0, e), ".", digits.AsSpan(e));
        }
        if (-6 < e && e <= 0)
        {
            return "0." + new string('0', -e) + digits;
        }
        var exponentText = (e - 1 < 0 ? "-" : "+") + Math.Abs(e - 1).ToString(CultureInfo.InvariantCulture);
        if (k == 1)
        {
            return digits + "e" + exponentText;
        }
        return string.Concat(digits.AsSpan(0, 1), ".", digits.AsSpan(1)) + "e" + exponentText;
    }

    /// <summary>`n.toString(radix)` for the non-negative integers scanner.ts formats (octal literal suggestions).</summary>
    private static string NumberToStringRadix(double n, int radix)
    {
        if (double.IsNaN(n))
        {
            return "NaN";
        }
        if (double.IsInfinity(n))
        {
            return n > 0 ? "Infinity" : "-Infinity";
        }
        Debug.Assert(n >= 0 && Math.Floor(n) == n);
        var value = new BigInteger(n);
        if (value.IsZero)
        {
            return "0";
        }
        var digits = new System.Text.StringBuilder();
        while (!value.IsZero)
        {
            value = BigInteger.DivRem(value, radix, out var remainder);
            digits.Insert(0, "0123456789abcdefghijklmnopqrstuvwxyz"[(int)remainder]);
        }
        return digits.ToString();
    }

    // A double as an int code point for the identifier tests (a value above int.MaxValue is no identifier character
    // either way; the TypeScript functions take any number).
    private static int ToCodePointSaturating(double value)
    {
        return value > int.MaxValue ? int.MaxValue : (int)value;
    }
}
