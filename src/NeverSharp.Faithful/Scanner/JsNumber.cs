// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): the ECMAScript number conversions that scanner.ts takes from the JavaScript
// runtime (`+string`, `"" + number`, `parseInt`, `Number.prototype.toString(8)`); structure after
// internal/jsnum/string.go of typescript-go.

using System.Globalization;
using System.Numerics;
using System.Text;

namespace NeverSharp.Faithful;

/// <summary>ECMAScript's Number conversions (StringToNumber, Number::toString, parseInt) for the scanner.</summary>
internal static class JsNumber
{
    private const double MaxSafeIntegerPlusOne = 9007199254740992.0;

    /// <summary>ECMAScript whitespace and line terminators (the characters `trim()` and `\s` match).</summary>
    internal static bool IsStrWhiteSpace(int ch)
    {
        switch (ch)
        {
            case 0x09:
            case 0x0A:
            case 0x0B:
            case 0x0C:
            case 0x0D:
            case 0x20:
            case 0xA0:
            case 0x1680:
            case 0x2028:
            case 0x2029:
            case 0x202F:
            case 0x205F:
            case 0x3000:
            case 0xFEFF:
                return true;
            default:
                return ch >= 0x2000 && ch <= 0x200A;
        }
    }

    /// <summary>String.prototype.trimStart().</summary>
    internal static string TrimStart(string s)
    {
        var i = 0;
        while (i < s.Length && IsStrWhiteSpace(s[i]))
        {
            i++;
        }
        return i == 0 ? s : s.Substring(i);
    }

    /// <summary>The numeric value of a digit character in any radix up to 36, or 99 for a non-digit.</summary>
    private static int DigitValue(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return c - '0';
        }
        if (c >= 'a' && c <= 'z')
        {
            return c - 'a' + 10;
        }
        if (c >= 'A' && c <= 'Z')
        {
            return c - 'A' + 10;
        }
        return 99;
    }

    /// <summary>ToNumber applied to a string (`+text`).</summary>
    internal static double StringToNumber(string text)
    {
        // Fast path for the common case, a short run of decimal digits (jsnum.FromString trims and parses in the same spirit,
        // internal/jsnum/string.go).
        if (text.Length is > 0 and <= 15)
        {
            long digitsValue = 0;
            var digitIndex = 0;
            while (digitIndex < text.Length && text[digitIndex] >= '0' && text[digitIndex] <= '9')
            {
                digitsValue = (digitsValue * 10) + (text[digitIndex] - '0');
                digitIndex++;
            }
            if (digitIndex == text.Length)
            {
                return digitsValue;
            }
        }

        // A text made of digits, '.', 'e', 'E' and signs that .NET parses is a StringNumericLiteral with the same, correctly rounded
        // value (what .NET accepts beyond that, e.g. "Infinity" symbols, white space or currency, is not tried here).
        if (IsPlainDecimalText(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        var s = text.AsSpan();
        var from = 0;
        var to = s.Length;
        while (from < to && IsStrWhiteSpace(s[from]))
        {
            from++;
        }
        while (to > from && IsStrWhiteSpace(s[to - 1]))
        {
            to--;
        }
        s = s.Slice(from, to - from);
        if (s.IsEmpty)
        {
            return 0;
        }
        if (s.Length > 2 && s[0] == '0')
        {
            var radix = s[1] switch
            {
                'x' or 'X' => 16,
                'o' or 'O' => 8,
                'b' or 'B' => 2,
                _ => 0,
            };
            if (radix != 0)
            {
                var digits = s.Slice(2);
                foreach (var c in digits)
                {
                    if (DigitValue(c) >= radix)
                    {
                        return double.NaN;
                    }
                }
                return PowerOfTwoDigitsToDouble(digits, radix);
            }
        }

        var i = 0;
        var negative = false;
        if (s[0] == '+' || s[0] == '-')
        {
            negative = s[0] == '-';
            i++;
        }
        var rest = s.Slice(i);
        if (rest.SequenceEqual("Infinity"))
        {
            return negative ? double.NegativeInfinity : double.PositiveInfinity;
        }

        var intStart = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9')
        {
            i++;
        }
        var integerDigits = s.Slice(intStart, i - intStart);
        ReadOnlySpan<char> fractionDigits = default;
        if (i < s.Length && s[i] == '.')
        {
            i++;
            var fractionStart = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                i++;
            }
            fractionDigits = s.Slice(fractionStart, i - fractionStart);
        }
        if (integerDigits.IsEmpty && fractionDigits.IsEmpty)
        {
            return double.NaN;
        }
        var exponent = 0;
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            var exponentNegative = false;
            if (i < s.Length && (s[i] == '+' || s[i] == '-'))
            {
                exponentNegative = s[i] == '-';
                i++;
            }
            var exponentStart = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                i++;
            }
            if (i == exponentStart)
            {
                return double.NaN;
            }
            // Clamp absurdly long exponents: the result is 0 or Infinity either way.
            for (var k = exponentStart; k < i; k++)
            {
                exponent = Math.Min(exponent * 10 + (s[k] - '0'), 99999999);
            }
            if (exponentNegative)
            {
                exponent = -exponent;
            }
        }
        if (i != s.Length)
        {
            return double.NaN;
        }

        var normalized = new StringBuilder();
        normalized.Append(integerDigits.IsEmpty ? "0" : integerDigits.ToString());
        if (!fractionDigits.IsEmpty)
        {
            normalized.Append('.').Append(fractionDigits);
        }
        normalized.Append('E').Append(exponent.ToString(CultureInfo.InvariantCulture));
        var value = double.Parse(normalized.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }

    private static bool IsPlainDecimalText(string text)
    {
        foreach (var c in text)
        {
            if (!((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-'))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The value of binary, octal or hexadecimal digits, correctly rounded (parseInt is exact for these radixes).</summary>
    private static double PowerOfTwoDigitsToDouble(ReadOnlySpan<char> digits, int radix)
    {
        var bits = radix == 2 ? 1 : radix == 8 ? 3 : 4;
        var firstSignificant = 0;
        while (firstSignificant < digits.Length && digits[firstSignificant] == '0')
        {
            firstSignificant++;
        }
        digits = digits.Slice(firstSignificant);
        if (digits.Length * bits <= 64)
        {
            ulong small = 0;
            foreach (var c in digits)
            {
                small = (small << bits) | (uint)DigitValue(c);
            }
            return small;
        }

        var big = BigInteger.Zero;
        foreach (var c in digits)
        {
            big = (big << bits) | (uint)DigitValue(c);
        }
        var bitLength = (int)big.GetBitLength();
        var shift = bitLength - 64;
        var top = (ulong)(big >> shift);
        if (!(big & ((BigInteger.One << shift) - BigInteger.One)).IsZero)
        {
            top |= 1; // sticky bit: the 11 bits that double drops include it
        }
        return Math.ScaleB((double)top, shift);
    }

    /// <summary>The global parseInt(string, radix) for the radixes the scanner uses (2, 8, 10, 16).</summary>
    internal static double ParseInt(string text, int radix)
    {
        var i = 0;
        while (i < text.Length && IsStrWhiteSpace(text[i]))
        {
            i++;
        }
        var negative = false;
        if (i < text.Length && (text[i] == '+' || text[i] == '-'))
        {
            negative = text[i] == '-';
            i++;
        }
        if (radix == 16 && i + 1 < text.Length && text[i] == '0' && (text[i + 1] == 'x' || text[i + 1] == 'X'))
        {
            i += 2;
        }
        var start = i;
        while (i < text.Length && DigitValue(text[i]) < radix)
        {
            i++;
        }
        if (i == start)
        {
            return double.NaN;
        }
        var digits = text.AsSpan(start, i - start);
        double value;
        if (radix == 10)
        {
            value = double.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        }
        else
        {
            value = PowerOfTwoDigitsToDouble(digits, radix);
        }
        return negative ? -value : value;
    }

    /// <summary>parseInt(hexDigits, 16) saturated to int.MaxValue (the callers only compare the value with 0x10FFFF).</summary>
    internal static int ParseHexDigitsSaturated(string hexDigits)
    {
        var value = ParseInt(hexDigits, 16);
        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }

    /// <summary>Number::toString(x) (radix 10).</summary>
    internal static string NumberToString(double value)
    {
        // Fast path: for safe integers, directly convert to string (Number.String in internal/jsnum/string.go).
        if (value >= -MaxSafeIntegerPlusOne && value <= MaxSafeIntegerPlusOne)
        {
            var integer = (long)value;
            if (integer == value)
            {
                return integer.ToString(CultureInfo.InvariantCulture);
            }
        }

        if (double.IsNaN(value))
        {
            return "NaN";
        }
        if (value == 0)
        {
            return "0";
        }
        if (double.IsInfinity(value))
        {
            return value < 0 ? "-Infinity" : "Infinity";
        }
        if (value < 0)
        {
            return "-" + NumberToString(-value);
        }

        // The shortest digits that round-trip, as .NET writes them ("123.45", "1E+21", "1.5E-07").
        var r = value.ToString("R", CultureInfo.InvariantCulture);
        var exponent = 0;
        var e = r.IndexOf('E', StringComparison.Ordinal);
        if (e < 0)
        {
            // .NET writes plain digits for exponents 14 down to -5, a subset of the range in which ECMAScript writes them too.
            return r;
        }
        if (e >= 0)
        {
            exponent = int.Parse(r.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            r = r.Substring(0, e);
        }
        var dot = r.IndexOf('.', StringComparison.Ordinal);
        var pointPosition = dot >= 0 ? dot : r.Length;
        var digits = dot >= 0 ? r.Remove(dot, 1) : r;
        // value = 0.digits * 10^n
        var n = pointPosition + exponent;
        var leadingZeros = 0;
        while (leadingZeros < digits.Length - 1 && digits[leadingZeros] == '0')
        {
            leadingZeros++;
        }
        digits = digits.Substring(leadingZeros);
        n -= leadingZeros;
        digits = digits.TrimEnd('0');
        var k = digits.Length;

        if (k <= n && n <= 21)
        {
            return digits + new string('0', n - k);
        }
        if (0 < n && n <= 21)
        {
            return digits.Insert(n, ".");
        }
        if (-6 < n && n <= 0)
        {
            return "0." + new string('0', -n) + digits;
        }
        var exp = n - 1;
        var expText = (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString(CultureInfo.InvariantCulture);
        if (k == 1)
        {
            return digits + "e" + expText;
        }
        return digits.Insert(1, ".") + "e" + expText;
    }

    /// <summary>Number.prototype.toString(8) for a non-negative integer value.</summary>
    internal static string IntegerToOctalString(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }
        if (double.IsInfinity(value))
        {
            return "Infinity";
        }
        if (value < MaxSafeIntegerPlusOne)
        {
            return Convert.ToString((long)value, 8);
        }
        // The exact digits of the integer value of the double.
        var remaining = new BigInteger(value);
        var digits = new List<char>();
        while (!remaining.IsZero)
        {
            digits.Add((char)('0' + (int)(remaining & 7)));
            remaining >>= 3;
        }
        digits.Reverse();
        return new string(digits.ToArray());
    }
}
