// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O, step O24 (from step O23, variant C), SIMD scans of the scanner's character runs.
//
// The runs the scanner steps over character by character, found with SIMD over blocks of 8 characters
// (Vector128 only: the NativeAOT baseline, x86-64-v2, has no 256-bit vectors). Each helper returns the position the
// scalar loop it replaces would stop at. A block is only loaded when all its characters are before `end`; the rest of a
// run goes through the scalar loop on the ASCII tables (Scanner.CharTables.cs). No counterpart in scanner.ts;
// typescript-go's scanner steps over the same runs byte by byte.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Scanner
{
    /// <summary>The first character of the text as a reference for vector loads (text is native or pinned memory).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private ref ushort TextBase() => ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(text.AsSpan()));

    // The bits of LaneBits when every lane is set.
    private const uint AllLanes = 0xFFFF;

    /// <summary>
    /// The lanes of a comparison result as bits, two per lane (the byte mask: one instruction, where the mask of the
    /// 16-bit lanes is a longer sequence on the NativeAOT baseline).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static uint LaneBits(Vector128<ushort> mask) => mask.AsByte().ExtractMostSignificantBits();

    /// <summary>The first lane set in <paramref name="bits"/> (from <see cref="LaneBits"/>, not 0).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int FirstLane(uint bits) => BitOperations.TrailingZeroCount(bits) >> 1;

    /// <summary>
    /// The end of the run of ASCII identifier parts ([A-Za-z0-9$_]) from <paramref name="p"/>: the first position in
    /// [p, end) whose character is not one, or end.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int SkipAsciiIdentifierParts(int p)
    {
        var limit = end;
        if (Sse2.IsSupported)
        {
            // 16 characters per block, narrowed to bytes with saturation (characters above 0xFF become 0xFF or 0, which
            // are no identifier parts), so that most identifiers take one block.
            ref var b = ref TextBase();
            while (p <= limit - (2 * Vector128<ushort>.Count))
            {
                var bytes = Sse2.PackUnsignedSaturate(Vector128.LoadUnsafe(ref b, (nuint)p).AsInt16(), Vector128.LoadUnsafe(ref b, (nuint)(p + 8)).AsInt16());
                var letter = Vector128.LessThan((bytes | Vector128.Create((byte)0x20)) - Vector128.Create((byte)'a'), Vector128.Create((byte)26));
                var digit = Vector128.LessThan(bytes - Vector128.Create((byte)'0'), Vector128.Create((byte)10));
                var other = Vector128.Equals(bytes, Vector128.Create((byte)'_')) | Vector128.Equals(bytes, Vector128.Create((byte)'$'));
                var stop = ~(letter | digit | other).ExtractMostSignificantBits() & 0xFFFF;
                if (stop != 0)
                {
                    return p + BitOperations.TrailingZeroCount(stop);
                }
                p += 2 * Vector128<ushort>.Count;
            }
        }
        if (Vector128.IsHardwareAccelerated)
        {
            ref var b = ref TextBase();
            while (p <= limit - Vector128<ushort>.Count)
            {
                var v = Vector128.LoadUnsafe(ref b, (nuint)p);
                var letter = Vector128.LessThan((v | Vector128.Create((ushort)0x20)) - Vector128.Create((ushort)'a'), Vector128.Create((ushort)26));
                var digit = Vector128.LessThan(v - Vector128.Create((ushort)'0'), Vector128.Create((ushort)10));
                var other = Vector128.Equals(v, Vector128.Create((ushort)'_')) | Vector128.Equals(v, Vector128.Create((ushort)'$'));
                var stop = ~LaneBits(letter | digit | other) & AllLanes;
                if (stop != 0)
                {
                    return p + FirstLane(stop);
                }
                p += Vector128<ushort>.Count;
            }
        }
        var flags = AsciiCharFlags;
        while (p < limit)
        {
            int ch = text[p];
            if (ch >= 128 || (flags[ch] & (byte)CharFlags.IdentifierPart) == 0)
            {
                break;
            }
            p++;
        }
        return p;
    }

    /// <summary>
    /// The end of the run of ASCII white space and line breaks (' ', '\t', '\n', '\v', '\f', '\r',
    /// <see cref="IsAsciiWhiteSpaceOrLineBreak"/>) from <paramref name="p"/>: the first position in [p, end) whose
    /// character is not one, or end.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int SkipAsciiWhiteSpaceOrLineBreak(int p)
    {
        var limit = end;
        if (Vector128.IsHardwareAccelerated)
        {
            ref var b = ref TextBase();
            while (p <= limit - Vector128<ushort>.Count)
            {
                var v = Vector128.LoadUnsafe(ref b, (nuint)p);
                var space = Vector128.Equals(v, Vector128.Create((ushort)' ')) |
                    Vector128.LessThan(v - Vector128.Create((ushort)'\t'), Vector128.Create((ushort)('\r' - '\t' + 1)));
                var stop = ~LaneBits(space) & AllLanes;
                if (stop != 0)
                {
                    return p + FirstLane(stop);
                }
                p += Vector128<ushort>.Count;
            }
        }
        while (p < limit && IsAsciiWhiteSpaceOrLineBreak(text[p]))
        {
            p++;
        }
        return p;
    }

    /// <summary>Lanes of <paramref name="v"/> that are line breaks (<see cref="IsLineBreak"/>: LF, CR, U+2028, U+2029).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static Vector128<ushort> LineBreaks(Vector128<ushort> v)
    {
        return Vector128.Equals(v, Vector128.Create((ushort)'\n')) | Vector128.Equals(v, Vector128.Create((ushort)'\r')) |
            Vector128.Equals(v & Vector128.Create((ushort)0xFFFE), Vector128.Create((ushort)0x2028));
    }

    /// <summary>
    /// The first line break (<see cref="IsLineBreak"/>) in [<paramref name="p"/>, end), or end: the end of a
    /// single-line comment's body.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int FindLineBreak(int p)
    {
        var limit = end;
        if (Vector128.IsHardwareAccelerated)
        {
            ref var b = ref TextBase();
            while (p <= limit - Vector128<ushort>.Count)
            {
                var lineBreak = LaneBits(LineBreaks(Vector128.LoadUnsafe(ref b, (nuint)p)));
                if (lineBreak != 0)
                {
                    return p + FirstLane(lineBreak);
                }
                p += Vector128<ushort>.Count;
            }
        }
        while (p < limit && !IsLineBreakFast(text[p]))
        {
            p++;
        }
        return p;
    }

    /// <summary>
    /// The body of a multi-line comment from pos (after "/*"), as scan's loop does it: stops after the first "*/" whose
    /// '*' is before end (pos after the '/'; returns true), or at end (returns false); for each line break before that
    /// it sets <see cref="TokenFlags.PrecedingLineBreak"/> and <paramref name="lastLineStart"/> to the position after it.
    /// In two passes: the vectors find the "*/" (a '*' lane whose next character is '/'), then the last line break
    /// before it, searching backwards; the scalar loop steps over the same characters and records each line break, so
    /// only the last one and whether there is one matter.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool ScanMultiLineCommentBody(ref int lastLineStart)
    {
        var start = pos;
        var p = start;
        var limit = end;
        var closed = false;
        if (Vector128.IsHardwareAccelerated)
        {
            ref var b = ref TextBase();
            // The pairs (p + i, p + i + 1), i < 8, of a block: all characters before end.
            while (p < limit - Vector128<ushort>.Count)
            {
                var star = Vector128.Equals(Vector128.LoadUnsafe(ref b, (nuint)p), Vector128.Create((ushort)'*'));
                var slash = Vector128.Equals(Vector128.LoadUnsafe(ref b, (nuint)(p + 1)), Vector128.Create((ushort)'/'));
                var bits = LaneBits(star & slash);
                if (bits != 0)
                {
                    p += FirstLane(bits);
                    closed = true;
                    break;
                }
                p += Vector128<ushort>.Count;
            }
        }
        if (!closed)
        {
            // The rest as the scalar loop tests it (the '/' may be at end when end is before the end of the text).
            while (p < limit)
            {
                if (text[p] == '*' && CharCodeUnchecked(p + 1) == (int)CharacterCodes.slash)
                {
                    closed = true;
                    break;
                }
                p++;
            }
        }
        // The characters the scalar loop steps over are [start, p): its last line break decides.
        var lineBreak = LastLineBreak(start, p);
        if (lineBreak >= 0)
        {
            lastLineStart = lineBreak + 1;
            tokenFlags |= TokenFlags.PrecedingLineBreak;
        }
        pos = closed ? p + 2 : p;
        return closed;
    }

    /// <summary>
    /// The end of the run of string literal characters from <paramref name="p"/> that scanString's loop only steps over
    /// (all but <paramref name="quote"/>, '\', LF and CR): the first position in [p, end) whose character is one of them,
    /// or end.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int SkipStringCharacters(int p, int quote)
    {
        var limit = end;
        if (Vector128.IsHardwareAccelerated)
        {
            ref var b = ref TextBase();
            var q = Vector128.Create((ushort)quote);
            while (p <= limit - Vector128<ushort>.Count)
            {
                var v = Vector128.LoadUnsafe(ref b, (nuint)p);
                var special = LaneBits(Vector128.Equals(v, q) | Vector128.Equals(v, Vector128.Create((ushort)'\\')) |
                    Vector128.Equals(v, Vector128.Create((ushort)'\n')) | Vector128.Equals(v, Vector128.Create((ushort)'\r')));
                if (special != 0)
                {
                    return p + FirstLane(special);
                }
                p += Vector128<ushort>.Count;
            }
        }
        var otherQuote = quote ^ ((int)CharacterCodes.doubleQuote ^ (int)CharacterCodes.singleQuote);
        var flags = AsciiCharFlags;
        while (p < limit)
        {
            int c = text[p];
            if ((uint)c < 128u && (flags[c] & (byte)CharFlags.StringSpecial) != 0 && c != otherQuote)
            {
                break;
            }
            p++;
        }
        return p;
    }

    /// <summary>
    /// The end of the run of template characters from <paramref name="p"/> that scanTemplateAndSetTokenValue's loop
    /// only steps over (all but '`', '$', '\' and CR, <see cref="IsTemplateSpecialCharacter"/>): the first position in
    /// [p, end) whose character is one of them, or end.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int SkipTemplateCharacters(int p)
    {
        var limit = end;
        if (Vector128.IsHardwareAccelerated)
        {
            ref var b = ref TextBase();
            while (p <= limit - Vector128<ushort>.Count)
            {
                var v = Vector128.LoadUnsafe(ref b, (nuint)p);
                var special = LaneBits(Vector128.Equals(v, Vector128.Create((ushort)'`')) | Vector128.Equals(v, Vector128.Create((ushort)'$')) |
                    Vector128.Equals(v, Vector128.Create((ushort)'\\')) | Vector128.Equals(v, Vector128.Create((ushort)'\r')));
                if (special != 0)
                {
                    return p + FirstLane(special);
                }
                p += Vector128<ushort>.Count;
            }
        }
        while (p < limit && !IsTemplateSpecialCharacter(text[p]))
        {
            p++;
        }
        return p;
    }

    /// <summary>
    /// textToKeyword for text[start..start+len) (2 &lt;= len &lt;= 12, the first character a lowercase letter): the
    /// keyword's kind, or <see cref="SyntaxKind.Unknown"/> when the text is no keyword. The perfect hash of
    /// <see cref="TryGetKeyword"/> selects the only keyword the text can be; a vector compare of the text with that
    /// keyword's padded characters decides.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind MatchKeyword(int start, int len)
    {
        int slot = KeywordTable.Slots[KeywordTable.Hash(text[start], text[start + 1], text[start + len - 1], len)];
        if (KeywordTable.Lengths[slot] != len)
        {
            return SyntaxKind.Unknown; // also slot 0 (no keyword; length 0)
        }
        if (Vector128.IsHardwareAccelerated && start <= text.Length - KeywordTable.Width)
        {
            ref var b = ref TextBase();
            ref var k = ref KeywordTable.Chars[slot * KeywordTable.Width];
            // Two bits per character that matches; the first len characters must.
            var equal = LaneBits(Vector128.Equals(Vector128.LoadUnsafe(ref b, (nuint)start), Vector128.LoadUnsafe(ref k))) |
                (LaneBits(Vector128.Equals(Vector128.LoadUnsafe(ref b, (nuint)(start + 8)), Vector128.LoadUnsafe(ref k, 8))) << 16);
            var need = (1u << (2 * len)) - 1;
            return (equal & need) == need ? KeywordTable.SlotKinds[slot] : SyntaxKind.Unknown;
        }
        return text.AsSpan(start, len).SequenceEqual(KeywordTable.Words[slot - 1]) ? KeywordTable.SlotKinds[slot] : SyntaxKind.Unknown;
    }

    /// <summary>The position of the last line break (<see cref="IsLineBreak"/>) in [<paramref name="low"/>, <paramref name="high"/>), or -1.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int LastLineBreak(int low, int high)
    {
        var p = high;
        if (Vector128.IsHardwareAccelerated)
        {
            ref var b = ref TextBase();
            while (p - Vector128<ushort>.Count >= low)
            {
                p -= Vector128<ushort>.Count;
                var lineBreak = LaneBits(LineBreaks(Vector128.LoadUnsafe(ref b, (nuint)p)));
                if (lineBreak != 0)
                {
                    return p + (BitOperations.Log2(lineBreak) >> 1);
                }
            }
        }
        while (p > low)
        {
            p--;
            if (IsLineBreakFast(text[p]))
            {
                return p;
            }
        }
        return -1;
    }
}
