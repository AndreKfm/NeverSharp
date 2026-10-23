// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O, step O23 (variant B), character classification of ASCII characters by
// lookup tables for the scanner (Scanner.cs, Scanner.Instance.cs).
//
// No counterpart in scanner.ts; typescript-go's scanner tests ASCII bytes with comparisons. Characters
// above ASCII still go through the tests of scanner.ts (isWhiteSpaceSingleLine, isLineBreak and the
// Unicode identifier tables of the language version).

using System.Runtime.CompilerServices;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Scanner
{
    /// <summary>The classes of an ASCII character (<see cref="AsciiCharFlags"/>).</summary>
    [Flags]
    private enum CharFlags : byte
    {
        None = 0,

        /// <summary>isIdentifierStart for ASCII: [A-Za-z$_].</summary>
        IdentifierStart = 1 << 0,

        /// <summary>isIdentifierPart for ASCII outside JSX: [A-Za-z0-9$_].</summary>
        IdentifierPart = 1 << 1,

        /// <summary>isWhiteSpaceSingleLine for ASCII: space, tab, vertical tab, form feed.</summary>
        WhiteSpace = 1 << 2,

        /// <summary>isLineBreak for ASCII: line feed, carriage return.</summary>
        LineBreak = 1 << 3,

        /// <summary>isDigit: [0-9].</summary>
        Digit = 1 << 4,

        /// <summary>
        /// A character that <see cref="Scan"/> always returns as a token of one character, whatever follows:
        /// ( ) , : ; @ [ ] { } ~ (the token kind is in <see cref="AsciiSingleCharTokens"/>).
        /// </summary>
        SingleCharToken = 1 << 5,

        /// <summary>The characters a multi-line comment's body stops at: '*', line feed, carriage return.</summary>
        BlockCommentSpecial = 1 << 6,

        /// <summary>The characters scanString stops at, besides the other quote: '"', '\'', '\\', line feed, carriage return.</summary>
        StringSpecial = 1 << 7,
    }

    // Shorthands for the table below.
    private const byte IS = (byte)(CharFlags.IdentifierStart | CharFlags.IdentifierPart);
    private const byte DG = (byte)(CharFlags.Digit | CharFlags.IdentifierPart);
    private const byte WS = (byte)CharFlags.WhiteSpace;
    private const byte LB = (byte)(CharFlags.LineBreak | CharFlags.BlockCommentSpecial | CharFlags.StringSpecial);
    private const byte ST = (byte)CharFlags.SingleCharToken;
    private const byte AS = (byte)CharFlags.BlockCommentSpecial;
    private const byte SS = (byte)CharFlags.StringSpecial;

    /// <summary>The <see cref="CharFlags"/> of each ASCII character (index &lt; 128).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static ReadOnlySpan<byte> AsciiCharFlags =>
    [
        // 0x00-0x0F: NUL .. SI (tab, line feed, vertical tab, form feed, carriage return)
        0, 0, 0, 0, 0, 0, 0, 0, 0, WS, LB, WS, WS, LB, 0, 0,
        // 0x10-0x1F
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        // 0x20-0x2F:  space ! " # $ % & ' ( ) * + , - . /
        WS, 0, SS, 0, IS, 0, 0, SS, ST, ST, AS, 0, ST, 0, 0, 0,
        // 0x30-0x3F: 0-9 : ; < = > ?
        DG, DG, DG, DG, DG, DG, DG, DG, DG, DG, ST, ST, 0, 0, 0, 0,
        // 0x40-0x4F: @ A-O
        ST, IS, IS, IS, IS, IS, IS, IS, IS, IS, IS, IS, IS, IS, IS, IS,
        // 0x50-0x5F: P-Z [ \ ] ^ _
        IS, IS, IS, IS, IS, IS, IS, IS, IS, IS, IS, ST, SS, ST, 0, IS,
        // 0x60-0x6F: ` a-o
        0, IS, IS, IS, IS, IS, IS, IS, IS, IS, IS, IS, IS, IS, IS, IS,
        // 0x70-0x7F: p-z { | } ~ DEL
        IS, IS, IS, IS, IS, IS, IS, IS, IS, IS, IS, ST, 0, ST, ST, 0,
    ];

    /// <summary>The token kind of each <see cref="CharFlags.SingleCharToken"/> character (0 for the others).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static ReadOnlySpan<byte> AsciiSingleCharTokens =>
    [
        // 0x00-0x1F
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        // 0x20-0x2F: ( ) ,
        0, 0, 0, 0, 0, 0, 0, 0, (byte)SyntaxKind.OpenParenToken, (byte)SyntaxKind.CloseParenToken, 0, 0, (byte)SyntaxKind.CommaToken, 0, 0, 0,
        // 0x30-0x3F: : ;
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)SyntaxKind.ColonToken, (byte)SyntaxKind.SemicolonToken, 0, 0, 0, 0,
        // 0x40-0x4F: @
        (byte)SyntaxKind.AtToken, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        // 0x50-0x5F: [ ]
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)SyntaxKind.OpenBracketToken, 0, (byte)SyntaxKind.CloseBracketToken, 0, 0,
        // 0x60-0x6F
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        // 0x70-0x7F: { } ~
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)SyntaxKind.OpenBraceToken, 0, (byte)SyntaxKind.CloseBraceToken, (byte)SyntaxKind.TildeToken, 0,
    ];

    /// <summary>Whether <paramref name="ch"/> is an ASCII character with one of <paramref name="flags"/> (false for any other value).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool HasAsciiFlag(int ch, CharFlags flags)
    {
        return (uint)ch < 128u && (AsciiCharFlags[ch] & (byte)flags) != 0;
    }

    /// <summary>isLineBreak by the table for ASCII; above ASCII only U+2028 and U+2029 are line breaks.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsLineBreakFast(int ch)
    {
        return (uint)ch < 128u ? (AsciiCharFlags[ch] & (byte)CharFlags.LineBreak) != 0 : (ch | 1) == (int)CharacterCodes.paragraphSeparator;
    }

    /// <summary>
    /// The end of the comment body of a single-line comment from <paramref name="pos"/>: the position of the first line
    /// break before <paramref name="end"/>, else <paramref name="end"/>.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int SkipToLineBreak(ReadOnlySpan<char> text, int pos, int end)
    {
        while (pos < end && !IsLineBreakFast(text[pos]))
        {
            pos++;
        }
        return pos;
    }

    /// <summary>
    /// Track O (O24): false when the single-line comment text[<paramref name="start"/>..<paramref name="commentEnd"/>)
    /// (from its "//"; commentEnd &lt;= the text's length) cannot match commentDirectiveRegExSingleLine
    /// (`^\/\/\/?\s*@(ts-expect-error|ts-ignore)`), so that appendIfCommentDirective would return the directives
    /// unchanged: after "//", an optional '/' and spaces or tabs, the comment ends, or the next character is an ASCII
    /// character that is neither '@' nor other white space ('\t' to '\r'). True otherwise (the full test decides).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool MayBeCommentDirective(int start, int commentEnd)
    {
        var p = start + 2;
        if (p < commentEnd && text[p] == '/')
        {
            p++;
        }
        while (p < commentEnd && (text[p] == ' ' || text[p] == '\t'))
        {
            p++;
        }
        if (p >= commentEnd)
        {
            return false;
        }
        int ch = text[p];
        return ch >= 128 || ch == '@' || (uint)(ch - '\t') <= '\r' - '\t';
    }

    /// <summary>
    /// textToKeyword looked up by a range of the text (Track O, O23-B: instead of the dictionary's hash and buckets, a
    /// table indexed by a hash of the first two characters, the last one and the length, which is collision-free for the
    /// keywords; a hit is confirmed by comparing the characters). <paramref name="value"/> has 2 to 12 characters.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool TryGetKeyword(ReadOnlySpan<char> value, out SyntaxKind keyword)
    {
        var slot = KeywordTable.Slots[KeywordTable.Hash(value[0], value[1], value[^1], value.Length)];
        if (slot != 0 && value.SequenceEqual(KeywordTable.Words[slot - 1]))
        {
            keyword = KeywordTable.Kinds[slot - 1];
            return true;
        }
        keyword = SyntaxKind.Unknown;
        return false;
    }

    /// <summary>
    /// The perfect hash table of the keywords (<see cref="TryGetKeyword"/>, <see cref="MatchKeyword"/>), built from
    /// textToKeywordObj.
    /// </summary>
    private static class KeywordTable
    {
        private const int Size = 512;

        /// <summary>The characters of a keyword in <see cref="Chars"/>, zero-padded (Track O, O24).</summary>
        internal const int Width = 16;

        /// <summary>The keywords in the order of textToKeywordObj.</summary>
        internal static readonly string[] Words = [.. TextToKeywordObj.Keys];

        /// <summary>The token kind of each keyword in <see cref="Words"/>.</summary>
        internal static readonly SyntaxKind[] Kinds = [.. TextToKeywordObj.Values];

        /// <summary>Per hash value: 1 + the keyword's index in <see cref="Words"/>, or 0.</summary>
        internal static readonly byte[] Slots = CreateSlots();

        /// <summary>
        /// Per slot value (1 + index): the keyword's characters at slot * <see cref="Width"/>, zero-padded; slot 0 is
        /// empty (Track O, O24).
        /// </summary>
        internal static readonly ushort[] Chars = CreateChars();

        /// <summary>Per slot value: the keyword's length, 0 for slot 0 (Track O, O24).</summary>
        internal static readonly byte[] Lengths = CreateLengths();

        /// <summary>Per slot value: the keyword's kind, Unknown for slot 0 (Track O, O24).</summary>
        internal static readonly SyntaxKind[] SlotKinds = [SyntaxKind.Unknown, .. Kinds];

        private static ushort[] CreateChars()
        {
            var chars = new ushort[(Words.Length + 1) * Width];
            for (var i = 0; i < Words.Length; i++)
            {
                for (var j = 0; j < Words[i].Length; j++)
                {
                    chars[((i + 1) * Width) + j] = Words[i][j];
                }
            }
            return chars;
        }

        private static byte[] CreateLengths()
        {
            var lengths = new byte[Words.Length + 1];
            for (var i = 0; i < Words.Length; i++)
            {
                lengths[i + 1] = (byte)Words[i].Length;
            }
            return lengths;
        }

        private static byte[] CreateSlots()
        {
            var slots = new byte[Size];
            for (var i = 0; i < Words.Length; i++)
            {
                var word = Words[i];
                var h = Hash(word[0], word[1], word[^1], word.Length);
                if (slots[h] != 0)
                {
                    throw new InvalidOperationException($"keyword hash collision: {word} and {Words[slots[h] - 1]}");
                }
                slots[h] = (byte)(i + 1);
            }
            return slots;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        internal static int Hash(int first, int second, int last, int length)
        {
            return ((first * 2) + (second * 2) + (last * 47) + length) & (Size - 1);
        }
    }
}
