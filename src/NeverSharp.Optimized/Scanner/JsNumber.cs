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
using System.Text;

namespace NeverSharp.Optimized;

/// <summary>
/// The JavaScript number conversions that scanner.ts writes as expressions: `"" + +value` (StringToNumber,
/// then Number::toString), `parseInt(digits, radix)`, `number.toString(radix)`. typescript-go implements the
/// same conversions in internal/jsnum (Number.String, FromString); this follows its approach of normalizing
/// the input and letting the platform's correctly rounded conversion do the arithmetic.
/// </summary>
internal static class JsNumber
{
    /// <summary>Number::toString(x) with radix 10 (ECMA-262 6.1.6.1.20).</summary>
    internal static string ToString(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }
        if (value == 0)
        {
            return "0"; // also -0
        }
        if (double.IsInfinity(value))
        {
            return value < 0 ? "-Infinity" : "Infinity";
        }
        // Fast path for safe integers (as jsnum's Number.String).
        if (Math.Abs(value) <= 9007199254740991d && value == Math.Floor(value))
        {
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        }

        // The shortest round-trip digits ("R" is the shortest representation since .NET Core 3.0).
        var r = value.ToString("R", CultureInfo.InvariantCulture);
        var negative = r[0] == '-';
        if (negative)
        {
            r = r.Substring(1);
        }
        var exponent = 0;
        var e = r.IndexOf('E', StringComparison.Ordinal);
        if (e >= 0)
        {
            exponent = int.Parse(r.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            r = r.Substring(0, e);
        }
        // r is now d+ or d+.d+; collect the digits and the position of the decimal point.
        var digits = new StringBuilder();
        var pointIndex = -1;
        foreach (var c in r)
        {
            if (c == '.')
            {
                pointIndex = digits.Length;
            }
            else
            {
                digits.Append(c);
            }
        }
        if (pointIndex < 0)
        {
            pointIndex = digits.Length;
        }
        // Strip leading zeros (moving the point) and trailing zeros.
        var leading = 0;
        while (leading < digits.Length - 1 && digits[leading] == '0')
        {
            leading++;
        }
        var s = digits.ToString(leading, digits.Length - leading);
        pointIndex -= leading;
        s = s.TrimEnd('0');
        // value = 0.s * 10^n
        var k = s.Length;
        var n = pointIndex + exponent;

        var sb = new StringBuilder();
        if (negative)
        {
            sb.Append('-');
        }
        if (k <= n && n <= 21)
        {
            sb.Append(s).Append('0', n - k);
        }
        else if (0 < n && n <= 21)
        {
            sb.Append(s, 0, n).Append('.').Append(s, n, k - n);
        }
        else if (-6 < n && n <= 0)
        {
            sb.Append("0.").Append('0', -n).Append(s);
        }
        else
        {
            var exp = n - 1;
            sb.Append(s[0]);
            if (k > 1)
            {
                sb.Append('.').Append(s, 1, k - 1);
            }
            sb.Append('e').Append(exp < 0 ? '-' : '+').Append(Math.Abs(exp).ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// `"" + +value`: the canonical string of the number a numeric string denotes. A decimal integer of at most
    /// 15 digits without leading zeros is its own canonical string and is returned as it is, without converting
    /// it twice (typescript-go's scanBigIntSuffix likewise keeps the scanned string when the conversion gives
    /// the same text, and caches conversions; internal/scanner/scanner.go).
    /// </summary>
    internal static string ToStringOfNumericString(string value)
    {
        if (value.Length != 0 && value.Length <= 15 && (value[0] != '0' || value.Length == 1) && IsAllDigits(value))
        {
            return value;
        }
        return ToString(FromString(value));
    }

    /// <summary>
    /// Track O (O5a): whether <see cref="ToStringOfNumericString"/> returns the value's own characters, decided without
    /// converting, for the common forms (false only means "convert"):
    /// - a decimal integer of at most 15 digits without leading zeros (as above);
    /// - `I.F` with I `0` or without leading zeros, F not ending in `0`, at most 15 significant digits, and, for I `0`,
    ///   at most 5 zeros before F's first nonzero digit. At most 15 significant digits round-trip through a double, so
    ///   the shortest digits of the number are the literal's own (without trailing zeros), and Number::toString writes
    ///   a value from 1e-6 to below 1e21 with those digits in positional notation.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsCanonicalNumericString(ReadOnlySpan<char> value)
    {
        var length = value.Length;
        if (length == 0 || length > 24)
        {
            return false;
        }
        var i = 0;
        while (i < length && (uint)(value[i] - '0') <= 9)
        {
            i++;
        }
        if (i == 0 || (value[0] == '0' && i > 1))
        {
            return false;
        }
        if (i == length)
        {
            return length <= 15;
        }
        if (value[i] != '.' || i == length - 1 || value[length - 1] == '0')
        {
            return false;
        }
        var integerZero = i == 1 && value[0] == '0';
        var significant = integerZero ? 0 : i;
        var leadingZeros = 0;
        for (var j = i + 1; j < length; j++)
        {
            var ch = value[j];
            if ((uint)(ch - '0') > 9)
            {
                return false;
            }
            if (integerZero && significant == 0 && ch == '0')
            {
                leadingZeros++;
            }
            else
            {
                significant++;
            }
        }
        return significant <= 15 && leadingZeros <= 5;
    }

    /// <summary>
    /// StringToNumber (`+value`) for the strings the scanner produces: decimal literals without separators
    /// (`123`, `1.5`, `.5`, `1.`, `1e10`, `1.e-5`, possibly with leading zeros), `0x`/`0b`/`0o` integer
    /// literals, and the empty string (0).
    /// </summary>
    internal static double FromString(string value)
    {
        switch (value)
        {
            case "":
                return 0;
            case "Infinity" or "+Infinity":
                return double.PositiveInfinity;
            case "-Infinity":
                return double.NegativeInfinity;
        }
        // Fast path for integers (as jsnum's tryParseInt): up to 15 decimal digits are exact in a double.
        if (value.Length <= 15 && IsAllDigits(value))
        {
            long integer = 0;
            foreach (var ch in value)
            {
                integer = integer * 10 + (ch - '0');
            }
            return integer;
        }
        if (value.Length > 2 && value[0] == '0')
        {
            switch (value[1])
            {
                case 'x':
                case 'X':
                    return ParseInt(value.Substring(2), 16);
                case 'b':
                case 'B':
                    return ParseInt(value.Substring(2), 2);
                case 'o':
                case 'O':
                    return ParseInt(value.Substring(2), 8);
            }
        }

        // A sign (jsnum: cut off first so that -0 stays -0); a signed hexadecimal, binary or octal literal is NaN.
        if (value[0] is '-' or '+')
        {
            var unsigned = value.Substring(1);
            if (unsigned.Length == 0 || !(char.IsAsciiDigit(unsigned[0]) || unsigned[0] == '.') ||
                (unsigned.Length > 2 && unsigned[0] == '0' && unsigned[1] is 'x' or 'X' or 'b' or 'B' or 'o' or 'O'))
            {
                return double.NaN;
            }
            var magnitude = FromString(unsigned);
            return value[0] == '-' ? -magnitude : magnitude;
        }

        // <a>.<b>e<c>, each part optional (as jsnum's parseFloatString).
        string a, b = "", c = "";
        bool hasDot, hasExp;
        var dot = value.IndexOf('.', StringComparison.Ordinal);
        hasDot = dot >= 0;
        var rest = hasDot ? value.Substring(dot + 1) : value;
        var expIndex = rest.AsSpan().IndexOfAny('e', 'E');
        hasExp = expIndex >= 0;
        if (hasDot)
        {
            a = value.Substring(0, dot);
            b = hasExp ? rest.Substring(0, expIndex) : rest;
        }
        else
        {
            a = hasExp ? value.Substring(0, expIndex) : value;
        }
        if (hasExp)
        {
            c = rest.Substring(expIndex + 1);
        }
        if (!IsAllDigits(a) || !IsAllDigits(b))
        {
            return double.NaN;
        }
        if ((a.Length == 0 && b.Length == 0) || (hasExp && !IsSignedDigits(c)))
        {
            return double.NaN;
        }
        var sb = new StringBuilder();
        sb.Append(a.Length == 0 ? "0" : a);
        if (b.Length != 0)
        {
            sb.Append('.').Append(b);
        }
        if (hasExp)
        {
            sb.Append('e').Append(c);
        }
        return double.Parse(sb.ToString(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// `parseInt(digits, radix)` for a string of valid digits of the radix (the scanner never passes anything
    /// else): the correctly rounded value, as V8 computes it for these radices.
    /// </summary>
    internal static double ParseInt(string digits, int radix)
    {
        if (digits.Length == 0)
        {
            return double.NaN;
        }
        ulong small = 0;
        var i = 0;
        // Accumulate exactly while the value has at most 53 significant bits.
        for (; i < digits.Length; i++)
        {
            var d = DigitValue(digits[i]);
            if (d < 0 || d >= radix)
            {
                break;
            }
            if (small > (((1UL << 53) - 1) - (ulong)d) / (ulong)radix)
            {
                return ParseIntBig(digits, radix);
            }
            small = small * (ulong)radix + (ulong)d;
        }
        if (i == 0)
        {
            return double.NaN;
        }
        return small;
    }

    private static double ParseIntBig(string digits, int radix)
    {
        if (BitOperations.IsPow2(radix))
        {
            return ParseIntPowerOfTwo(digits, BitOperations.Log2((uint)radix));
        }
        var big = BigInteger.Zero;
        foreach (var ch in digits)
        {
            var d = DigitValue(ch);
            if (d < 0 || d >= radix)
            {
                break;
            }
            big = big * radix + d;
        }
        // The decimal string of the exact integer parses with correct rounding (round half to even).
        return double.Parse(big.ToString(CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Track O (O-NA): ParseIntBig for a radix 2^<paramref name="bitsPerDigit"/> (2, 8, 16: every radix the scanner
    /// passes) without BigInteger: the digits are bits, so the double is the leading 64 bits rounded to 53, half to
    /// even, with the bits below as a sticky bit, scaled by the number of bits left out (the decimal round trip of the
    /// BigInteger form gives the same correctly rounded value).
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static double ParseIntPowerOfTwo(string digits, int bitsPerDigit)
    {
        ulong mantissa = 0;
        var shift = 0; // bits left out below mantissa
        var sticky = false;
        foreach (var ch in digits)
        {
            var d = DigitValue(ch);
            if (d < 0 || d >= 1 << bitsPerDigit)
            {
                break;
            }
            if (BitOperations.LeadingZeroCount(mantissa) >= bitsPerDigit)
            {
                mantissa = (mantissa << bitsPerDigit) | (uint)d;
            }
            else
            {
                shift += bitsPerDigit;
                sticky |= d != 0;
            }
        }
        var length = 64 - BitOperations.LeadingZeroCount(mantissa);
        var excess = length - 53;
        if (excess > 0)
        {
            var roundBit = (mantissa >> (excess - 1)) & 1;
            var below = (mantissa & ((1UL << (excess - 1)) - 1)) != 0 || sticky;
            mantissa >>= excess;
            if (roundBit != 0 && (below || (mantissa & 1) != 0))
            {
                mantissa++;
            }
            shift += excess;
        }
        return Math.ScaleB(mantissa, shift);
    }

    /// <summary>`number.toString(radix)` for an integral number (the only kind the scanner formats with a radix).</summary>
    internal static string ToRadixString(double value, int radix)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }
        if (double.IsInfinity(value))
        {
            return value < 0 ? "-Infinity" : "Infinity";
        }
        var big = new BigInteger(Math.Abs(value));
        if (big.IsZero)
        {
            return "0";
        }
        var sb = new StringBuilder();
        while (!big.IsZero)
        {
            var digit = (int)(big % radix);
            sb.Insert(0, (char)(digit < 10 ? '0' + digit : 'a' + digit - 10));
            big /= radix;
        }
        if (value < 0)
        {
            sb.Insert(0, '-');
        }
        return sb.ToString();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int DigitValue(char ch)
    {
        if (ch >= '0' && ch <= '9')
        {
            return ch - '0';
        }
        if (ch >= 'a' && ch <= 'z')
        {
            return ch - 'a' + 10;
        }
        if (ch >= 'A' && ch <= 'Z')
        {
            return ch - 'A' + 10;
        }
        return -1;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsAllDigits(string s)
    {
        foreach (var ch in s)
        {
            if (ch < '0' || ch > '9')
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsSignedDigits(string s)
    {
        var start = s.Length > 0 && (s[0] == '+' || s[0] == '-') ? 1 : 0;
        return s.Length > start && IsAllDigits(s.Substring(start));
    }
}
