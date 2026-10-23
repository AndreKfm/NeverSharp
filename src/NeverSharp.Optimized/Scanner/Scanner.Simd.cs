// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's SIMD scans of the scanner's character runs and keyword match (step O23, variant C).
//
// The runs the scanner steps over character by character (ASCII identifier characters,
// ASCII white space after a line break, the bodies of comments, string literals and template literals) found with
// SIMD over blocks of 8 characters, and the keyword lookup of identifiers as a perfect hash with a vector compare.
// Each helper returns the position the scalar loop it replaces would stop at and does the same bookkeeping (line
// breaks in multi-line comments). A block is only loaded when all its characters are before `end`; the rest of a run
// goes through the scalar loop. Vector128 only: the NativeAOT baseline (x86-64-v2) has no 256-bit vectors.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
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
    /// The end of the run of ASCII identifier characters ([A-Za-z0-9_$], <see cref="IsAsciiIdentifierPart"/>) from
    /// <paramref name="p"/>: the first position in [p, end) whose character is not one, or end.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int SkipAsciiIdentifierParts(int p)
    {
        var limit = end;
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
        while (p < limit && IsAsciiIdentifierPart(text[p]))
        {
            p++;
        }
        return p;
    }

    /// <summary>
    /// The end of the run of ASCII white space and line breaks (<see cref="IsAsciiWhiteSpaceOrLineBreak"/>) from
    /// <paramref name="p"/>: the first position in [p, end) whose character is not one, or end.
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
        while (p < limit && !IsLineBreak(text[p]))
        {
            p++;
        }
        return p;
    }

    /// <summary>
    /// The body of a multi-line comment from pos (after "/*"), as scan's loop does it: stops after the first "*/" whose
    /// '*' is before end (pos after the '/'; returns true), or at end (returns false); for each line break before that
    /// it sets <see cref="TokenFlags.PrecedingLineBreak"/> and <paramref name="lastLineStart"/> to the position after it.
    /// The vectors find the candidates of a block ('*' and line breaks); each is then handled as the scalar loop would.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool ScanMultiLineCommentBody(ref int lastLineStart)
    {
        var p = pos;
        var limit = end;
        if (Vector128.IsHardwareAccelerated)
        {
            ref var b = ref TextBase();
            while (p <= limit - Vector128<ushort>.Count)
            {
                var v = Vector128.LoadUnsafe(ref b, (nuint)p);
                var bits = LaneBits(Vector128.Equals(v, Vector128.Create((ushort)'*')) | LineBreaks(v));
                while (bits != 0)
                {
                    var q = p + FirstLane(bits);
                    if (text[q] == '*')
                    {
                        if (CharCodeUnchecked(q + 1) == (int)CharacterCodes.slash)
                        {
                            pos = q + 2;
                            return true;
                        }
                    }
                    else
                    {
                        lastLineStart = q + 1;
                        tokenFlags |= TokenFlags.PrecedingLineBreak;
                    }
                    bits &= bits - 1;
                    bits &= bits - 1; // both bits of the lane
                }
                p += Vector128<ushort>.Count;
            }
        }
        while (p < limit)
        {
            var ch = text[p];
            if (ch == '*' && CharCodeUnchecked(p + 1) == (int)CharacterCodes.slash)
            {
                pos = p + 2;
                return true;
            }
            p++;
            if (IsLineBreak(ch))
            {
                lastLineStart = p;
                tokenFlags |= TokenFlags.PrecedingLineBreak;
            }
        }
        pos = p;
        return false;
    }

    /// <summary>
    /// The end of the run of string literal characters from <paramref name="p"/> that scanString's loop only steps over
    /// (<see cref="IsStringSpecialCharacter"/>: not the quote, '\', LF or CR): the first position in [p, end) whose
    /// character is one of them, or end.
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
        while (p < limit && !IsStringSpecialCharacter(text[p], quote))
        {
            p++;
        }
        return p;
    }

    /// <summary>
    /// The end of the run of template characters from <paramref name="p"/> that scanTemplateAndSetTokenValue's loop
    /// only steps over (<see cref="IsTemplateSpecialCharacter"/>: not '`', '$', '\' or CR): the first position in
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
    /// keyword's kind, or <see cref="SyntaxKind.Unknown"/> when the text is no keyword. A perfect hash of the first,
    /// second and last character and the length selects the only keyword the text can be; a vector compare of the
    /// text with that keyword decides.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind MatchKeyword(int start, int len)
    {
        var entry = KeywordTable.Slots[KeywordTable.Hash(text[start], text[start + 1], text[start + len - 1], len)];
        if (KeywordTable.Lengths[entry] != len)
        {
            return SyntaxKind.Unknown; // also entry 0 (no keyword; length 0)
        }
        if (Vector128.IsHardwareAccelerated && start <= text.Length - KeywordTable.Width)
        {
            ref var b = ref TextBase();
            ref var k = ref KeywordTable.Chars[entry * KeywordTable.Width];
            // Two bits per character that matches; the first len characters must.
            var equal = LaneBits(Vector128.Equals(Vector128.LoadUnsafe(ref b, (nuint)start), Vector128.LoadUnsafe(ref k))) |
                (LaneBits(Vector128.Equals(Vector128.LoadUnsafe(ref b, (nuint)(start + 8)), Vector128.LoadUnsafe(ref k, 8))) << 16);
            var need = (1u << (2 * len)) - 1;
            return (equal & need) == need ? KeywordTable.Kinds[entry] : SyntaxKind.Unknown;
        }
        return text.AsSpan(start, len).SequenceEqual(KeywordTable.Texts[entry]) ? KeywordTable.Kinds[entry] : SyntaxKind.Unknown;
    }
}

/// <summary>
/// Track O (O23): textToKeyword as a perfect hash table for <see cref="Scanner"/>'s keyword match. <see cref="Slots"/>
/// maps the hash to an entry (0: none); entry i has its characters zero-padded to <see cref="Width"/> in
/// <see cref="Chars"/>, its length in <see cref="Lengths"/> (0 for entry 0), its kind in <see cref="Kinds"/> and its
/// text in <see cref="Texts"/>. Built once from <see cref="Scanner.TextToKeywordObj"/>; building fails if the hash is
/// not perfect for it.
/// </summary>
internal static class KeywordTable
{
    internal const int Width = 16;
    private const int SlotCount = 512;

    internal static readonly byte[] Slots = new byte[SlotCount];
    internal static readonly ushort[] Chars = new ushort[(Scanner.TextToKeywordObj.Count + 1) * Width];
    internal static readonly byte[] Lengths = new byte[Scanner.TextToKeywordObj.Count + 1];
    internal static readonly SyntaxKind[] Kinds = new SyntaxKind[Scanner.TextToKeywordObj.Count + 1];
    internal static readonly string[] Texts = Build();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static int Hash(int first, int second, int last, int length) => (first + second * 16 + last * 7 + length * 31) & (SlotCount - 1);

    private static string[] Build()
    {
        var texts = new string[Kinds.Length];
        texts[0] = "";
        var entry = 1;
        foreach (var (text, kind) in Scanner.TextToKeywordObj)
        {
            var hash = Hash(text[0], text[1], text[^1], text.Length);
            if (Slots[hash] != 0 || text.Length > 12)
            {
                throw new InvalidOperationException("KeywordTable: the hash is not perfect for the keywords");
            }
            Slots[hash] = (byte)entry;
            for (var i = 0; i < text.Length; i++)
            {
                Chars[entry * Width + i] = text[i];
            }
            Lengths[entry] = (byte)text.Length;
            Kinds[entry] = kind;
            texts[entry] = text;
            entry++;
        }
        return texts;
    }
}
