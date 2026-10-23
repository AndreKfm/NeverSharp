// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/scanner.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/scanner/scanner.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// scanner.ts: createScanner. The variables of its closure are the fields of the instance, its inner
/// functions the instance methods, and the object it returns (TypeScript's `interface Scanner`) is the
/// instance itself.
/// </summary>
internal sealed partial class Scanner
{
    // Track O (step O1, variant C): the characters of the text (native memory, or a pinned string), and their owner,
    // which this field keeps alive while the scanner reads them.
    private SourceChars text;
    private NativeText textOwner = NativeText.Empty;

    // Current position (end position of text of current token)
    private int pos;

    // end of text
    private int end;

    // Start position of whitespace before current token
    private int fullStartPos;

    // Start position of text of current token
    private int tokenStart;

    private SyntaxKind token;
    // Track O (O5a): the token value as a handle into `texts`, a span of the text or a value in its character arena.
    private TextRef tokenValue;
    private TokenFlags tokenFlags;

    // Track O (O5a): the texts token values refer to: the scanner's own table (reset with each text), or the table of
    // the parse that uses the scanner (UseTexts), so that the values outlive the token in the parse's nodes.
    private readonly TextTable ownTexts = new(NativeText.Empty);
    private TextTable texts;

    private List<CommentDirective>? commentDirectives;
    private int skipJsDocLeadingAsterisks;

    private ScriptKind scriptKind = ScriptKind.Unknown;
    private JSDocParsingMode jsDocParsingMode = JSDocParsingMode.ParseAll;

    // The parameters of createScanner (closure variables; setScriptTarget, setLanguageVariant and setOnError assign them).
    private ScriptTarget languageVersion;
    private readonly bool skipTrivia;
    private LanguageVariant languageVariant;
    private ErrorCallback? onError;

    // createScanner's `start` parameter: scanClassStringDisjunctionContents assigns it (TypeScript resolves the
    // name `start` there to this parameter), which has no further effect.
    private int? start;

    private Scanner(ScriptTarget languageVersion, bool skipTrivia, LanguageVariant languageVariant, NativeText textInitial, ErrorCallback? onError, int? start, int? length)
    {
        this.languageVersion = languageVersion;
        this.skipTrivia = skipTrivia;
        this.languageVariant = languageVariant;
        this.onError = onError;
        this.start = start;
        texts = ownTexts;
        SetText(textInitial, this.start, length);
    }

    // Creates a scanner over a (possibly unspecified) range of a piece of text.
    internal static Scanner CreateScanner(
        ScriptTarget languageVersion,
        bool skipTrivia,
        LanguageVariant languageVariant = LanguageVariant.Standard,
        string? textInitial = null,
        ErrorCallback? onError = null,
        int? start = null,
        int? length = null)
    {
        return new Scanner(languageVersion, skipTrivia, languageVariant, NativeText.FromString(textInitial), onError, start, length);
    }

    /// <summary>Track O (step O1): <see cref="CreateScanner(ScriptTarget, bool, LanguageVariant, string?, ErrorCallback?, int?, int?)"/> over the text of a parse.</summary>
    internal static Scanner CreateScanner(
        ScriptTarget languageVersion,
        bool skipTrivia,
        LanguageVariant languageVariant,
        NativeText textInitial,
        ErrorCallback? onError = null,
        int? start = null,
        int? length = null)
    {
        return new Scanner(languageVersion, skipTrivia, languageVariant, textInitial, onError, start, length);
    }

    /// <summary>
    /// TypeScript defines this property (`__debugShowCurrentPositionInText`) on the scanner object only when
    /// Debug.isDebugging; a C# object cannot gain properties at run time, so it always exists.
    /// </summary>
    internal string DebugShowCurrentPositionInText
    {
        get
        {
            return string.Concat(text.AsSpan(0, GetTokenFullStart()), "║", text.AsSpan(GetTokenFullStart()));
        }
    }

    //// The members of the returned object (interface Scanner).

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int GetTokenFullStart() => fullStartPos;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int GetStartPos() => fullStartPos;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int GetTokenEnd() => pos;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int GetTextPos() => pos;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind GetToken() => token;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int GetTokenStart() => tokenStart;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int GetTokenPos() => tokenStart;

    internal string GetTokenText() => Substring(text, tokenStart, pos);

    /// <summary>Track O (O5a): <see cref="GetTokenText"/> as a span of the text.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal TextRef GetTokenTextRef() => texts.Substring(tokenStart, pos);

    /// <summary>The token value as a string (materialized from <see cref="GetTokenValueRef"/>; null before any value).</summary>
    internal string GetTokenValue() => texts.GetString(tokenValue)!;

    /// <summary>Track O (O5a): the token value as a handle into <see cref="Texts"/>.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal TextRef GetTokenValueRef() => tokenValue;

    /// <summary>Track O (O5a): the texts the token values refer to.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal TextTable Texts => texts;

    /// <summary>
    /// Track O (O5a): token values from here on go to <paramref name="table"/> (the texts of a parse, whose source text is
    /// this scanner's text) until the next <see cref="SetText"/>.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void UseTexts(TextTable table)
    {
        Debug.Assert(table.Source.SameAs(text) || (table.Source.Length == 0 && text.Length == 0), "The texts of another source text");
        texts = table;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool HasUnicodeEscape() => (tokenFlags & TokenFlags.UnicodeEscape) != 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool HasExtendedUnicodeEscape() => (tokenFlags & TokenFlags.ExtendedUnicodeEscape) != 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool HasPrecedingLineBreak() => (tokenFlags & TokenFlags.PrecedingLineBreak) != 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool HasPrecedingJSDocComment() => (tokenFlags & TokenFlags.PrecedingJSDocComment) != 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool HasPrecedingJSDocLeadingAsterisks() => (tokenFlags & TokenFlags.PrecedingJSDocLeadingAsterisks) != 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool IsIdentifier() => token == SyntaxKind.Identifier || token > SyntaxKind.LastReservedWord;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool IsReservedWord() => token >= SyntaxKind.FirstReservedWord && token <= SyntaxKind.LastReservedWord;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool IsUnterminated() => (tokenFlags & TokenFlags.Unterminated) != 0;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal List<CommentDirective>? GetCommentDirectives() => commentDirectives;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal TokenFlags GetNumericLiteralFlags() => tokenFlags & TokenFlags.NumericLiteralFlags;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal TokenFlags GetTokenFlags() => tokenFlags;

    //// The inner functions of createScanner.

    /// <summary>
    /// Returns the code point for the character at the given position within `text`. This
    /// should only be used when pos is guaranteed to be within the bounds of `text` as this
    /// function does not perform bounds checks.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int CodePointUnchecked(int pos)
    {
        return CodePointAt(text, pos);
    }

    /// <summary>
    /// Returns the code point for the character at the given position within `text`. If
    /// `pos` is outside the bounds set for `text`, `CharacterCodes.EOF` is returned instead.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int CodePointChecked(int pos)
    {
        return pos >= 0 && pos < end ? CodePointUnchecked(pos) : (int)CharacterCodes.EOF;
    }

    /// <summary>
    /// Returns the char code for the character at the given position within `text`. This
    /// should only be used when pos is guaranteed to be within the bounds of `text` as this
    /// function does not perform bounds checks.
    /// (Outside the string, `text.charCodeAt` returns NaN; see <see cref="NaN"/>.)
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int CharCodeUnchecked(int pos)
    {
        return (uint)pos < (uint)text.Length ? text[pos] : NaN;
    }

    /// <summary>
    /// Returns the char code for the character at the given position within `text`. If
    /// `pos` is outside the bounds set for `text`, `CharacterCodes.EOF` is returned instead.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int CharCodeChecked(int pos)
    {
        return pos >= 0 && pos < end ? CharCodeUnchecked(pos) : (int)CharacterCodes.EOF;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void Error(DiagnosticMessage message)
    {
        Error(message, pos, 0);
    }

    [NoAllocUnchecked("error path: reports through the error callback (the parser creates a diagnostic)")]
    private void Error(DiagnosticMessage message, int errPos, int length, object? arg0 = null)
    {
        if (onError != null)
        {
            var oldPos = pos;
            pos = errPos;
            onError(message, length, arg0);
            pos = oldPos;
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private TextRef ScanNumberFragment()
    {
        var start = pos;
        var allowSeparator = false;
        var isPreviousTokenSeparator = false;
        var result = new TextBuilder(texts); // TypeScript: `let result = ""` (Track O: a span of the text unless separators split it)
        while (true)
        {
            var ch = CharCodeUnchecked(pos);
            if (ch == (int)CharacterCodes._)
            {
                tokenFlags |= TokenFlags.ContainsSeparator;
                if (allowSeparator)
                {
                    allowSeparator = false;
                    isPreviousTokenSeparator = true;
                    result.Append(text, start, pos);
                }
                else
                {
                    tokenFlags |= TokenFlags.ContainsInvalidSeparator;
                    if (isPreviousTokenSeparator)
                    {
                        Error(Diagnostics.Multiple_consecutive_numeric_separators_are_not_permitted, pos, 1);
                    }
                    else
                    {
                        Error(Diagnostics.Numeric_separators_are_not_allowed_here, pos, 1);
                    }
                }
                pos++;
                start = pos;
                continue;
            }
            if (IsDigit(ch))
            {
                allowSeparator = true;
                isPreviousTokenSeparator = false;
                pos++;
                continue;
            }
            break;
        }
        if (CharCodeUnchecked(pos - 1) == (int)CharacterCodes._)
        {
            tokenFlags |= TokenFlags.ContainsInvalidSeparator;
            Error(Diagnostics.Numeric_separators_are_not_allowed_here, pos - 1, 1);
        }
        result.Append(text, start, pos);
        return result.Result();
    }

    // Extract from Section 12.9.3
    // NumericLiteral ::=
    //     | DecimalLiteral
    //     | DecimalBigIntegerLiteral
    //     | NonDecimalIntegerLiteral 'n'?
    //     | LegacyOctalIntegerLiteral
    // DecimalBigIntegerLiteral ::=
    //     | '0n'
    //     | [1-9] DecimalDigits? 'n'
    //     | [1-9] '_' DecimalDigits 'n'
    // DecimalLiteral ::=
    //     | DecimalIntegerLiteral '.' DecimalDigits? ExponentPart?
    //     | '.' DecimalDigits ExponentPart?
    //     | DecimalIntegerLiteral ExponentPart?
    // DecimalIntegerLiteral ::=
    //     | '0'
    //     | [1-9] '_'? DecimalDigits
    //     | NonOctalDecimalIntegerLiteral
    // LegacyOctalIntegerLiteral ::= '0' [0-7]+
    // NonOctalDecimalIntegerLiteral ::= '0' [0-7]* [89] [0-9]*
    /// <summary>
    /// Track O (O24): scanNumber for the common case, a decimal integer of at most 15 digits without leading zeros (or
    /// a single `0`) after which no '.', no identifier part (digit, letter, '$', '_': no separator, exponent, bigint
    /// suffix or identifier) and no character above ASCII follow, read as scanNumber reads (CharCodeUnchecked, so past
    /// end within the text; the text has a NUL after its last character). scanNumber would take its fragments as one
    /// span, find the value canonical (JsNumber.IsCanonicalNumericString) and keep it: tokenValue the span of the text,
    /// pos after the digits, no token flags. Returns false, changing nothing, in every other case.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool TryScanSimpleInteger()
    {
        var start = pos;
        var p = start + 1;
        while ((uint)(text[p] - '0') <= 9u)
        {
            p++;
        }
        var length = p - start;
        if (length > 15 || (length > 1 && text[start] == '0'))
        {
            return false;
        }
        int next = text[p];
        if (next >= 128 || next == (int)CharacterCodes.dot || (AsciiCharFlags[next] & (byte)CharFlags.IdentifierPart) != 0)
        {
            return false;
        }
        pos = p;
        tokenValue = TextRef.Source(start, length);
        return true;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ScanNumber()
    {
        var start = pos;
        TextRef mainFragment;
        if (CharCodeUnchecked(pos) == (int)CharacterCodes._0)
        {
            pos++;
            if (CharCodeUnchecked(pos) == (int)CharacterCodes._)
            {
                tokenFlags |= TokenFlags.ContainsSeparator | TokenFlags.ContainsInvalidSeparator;
                Error(Diagnostics.Numeric_separators_are_not_allowed_here, pos, 1);
                // treat it as a normal number literal
                pos--;
                mainFragment = ScanNumberFragment();
            }
            // Separators are not allowed in the below cases
            else if (!ScanDigits())
            {
                // NonOctalDecimalIntegerLiteral, emit error later
                // Separators in decimal and exponent parts are still allowed according to the spec
                tokenFlags |= TokenFlags.ContainsLeadingZero;
                mainFragment = NumericStringValue(tokenValue);
            }
            else if (tokenValue.Length <= 0)
            {
                // a single zero (Track O: the "0" of the text)
                mainFragment = texts.Substring(start, start + 1);
            }
            else
            {
                // LegacyOctalIntegerLiteral
                return ScanLegacyOctalLiteral(start);
            }
        }
        else
        {
            mainFragment = ScanNumberFragment();
        }
        TextRef decimalFragment = default;
        TextRef scientificFragment = default;
        if (CharCodeUnchecked(pos) == (int)CharacterCodes.dot)
        {
            pos++;
            decimalFragment = ScanNumberFragment();
        }
        // TypeScript names this local `end` (shadowing the scanner's `end`).
        var numberEnd = pos;
        if (CharCodeUnchecked(pos) == (int)CharacterCodes.E || CharCodeUnchecked(pos) == (int)CharacterCodes.e)
        {
            pos++;
            tokenFlags |= TokenFlags.Scientific;
            if (CharCodeUnchecked(pos) == (int)CharacterCodes.plus || CharCodeUnchecked(pos) == (int)CharacterCodes.minus)
            {
                pos++;
            }
            var preNumericPart = pos;
            var finalFragment = ScanNumberFragment();
            if (finalFragment.Length == 0)
            {
                Error(Diagnostics.Digit_expected);
            }
            else
            {
                scientificFragment = texts.Concat(texts.Substring(numberEnd, preNumericPart), finalFragment);
                numberEnd = pos;
            }
        }
        TextRef result;
        if ((tokenFlags & TokenFlags.ContainsSeparator) != 0)
        {
            var parts = new TextBuilder(texts);
            parts.Append(mainFragment);
            if (decimalFragment.Length > 0)
            {
                parts.Append(".");
                parts.Append(decimalFragment);
            }
            if (scientificFragment.Length > 0)
            {
                parts.Append(scientificFragment);
            }
            result = parts.Result();
        }
        else
        {
            result = texts.Substring(start, numberEnd); // No need to use all the fragments; no _ removal needed
        }

        if ((tokenFlags & TokenFlags.ContainsLeadingZero) != 0)
        {
            Error(Diagnostics.Decimals_with_leading_zeros_are_not_allowed, start, numberEnd - start);
            // if a literal has a leading zero, it must not be bigint
            tokenValue = NumericStringValue(result);
            return SyntaxKind.NumericLiteral;
        }

        if (!decimalFragment.IsNone || (tokenFlags & TokenFlags.Scientific) != 0)
        {
            CheckForIdentifierStartAfterNumericLiteral(start, decimalFragment.IsNone && (tokenFlags & TokenFlags.Scientific) != 0);
            // if value is not an integer, it can be safely coerced to a number
            tokenValue = NumericStringValue(result);
            return SyntaxKind.NumericLiteral;
        }
        else
        {
            tokenValue = result;
            var type = CheckBigIntSuffix(); // if value is an integer, check whether it is a bigint
            CheckForIdentifierStartAfterNumericLiteral(start);
            return type;
        }
    }

    // The LegacyOctalIntegerLiteral branch of scanNumber (Track O: a method of its own, as it builds strings).
    [NoAllocUnchecked("rare numeric form: a legacy octal literal (an error) converts its value through strings")]
    private SyntaxKind ScanLegacyOctalLiteral(int start)
    {
        var octalValue = JsNumber.ToString(JsNumber.ParseInt(texts.GetString(tokenValue)!, 8));
        tokenValue = texts.Add(octalValue);
        tokenFlags |= TokenFlags.Octal;
        var withMinus = token == SyntaxKind.MinusToken;
        var literal = (withMinus ? "-" : "") + "0o" + JsNumber.ToRadixString(JsNumber.FromString(octalValue), 8);
        if (withMinus)
        {
            start--;
        }
        Error(Diagnostics.Octal_literals_are_not_allowed_Use_the_syntax_0, start, pos - start, literal);
        return SyntaxKind.NumericLiteral;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void CheckForIdentifierStartAfterNumericLiteral(int numericStart, bool isScientific = false)
    {
        if (!IsIdentifierStart(CodePointUnchecked(pos), languageVersion))
        {
            return;
        }

        var identifierStart = pos;
        var length = ScanIdentifierParts().Length;

        if (length == 1 && CharCodeUnchecked(identifierStart) == 'n')
        {
            if (isScientific)
            {
                Error(Diagnostics.A_bigint_literal_cannot_use_exponential_notation, numericStart, identifierStart - numericStart + 1);
            }
            else
            {
                Error(Diagnostics.A_bigint_literal_must_be_an_integer, numericStart, identifierStart - numericStart + 1);
            }
        }
        else
        {
            Error(Diagnostics.An_identifier_or_keyword_cannot_immediately_follow_a_numeric_literal, identifierStart, length);
            pos = identifierStart;
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool ScanDigits()
    {
        var start = pos;
        var isOctal = true;
        while (IsDigit(CharCodeChecked(pos)))
        {
            if (!IsOctalDigit(CharCodeUnchecked(pos)))
            {
                isOctal = false;
            }
            pos++;
        }
        tokenValue = texts.Substring(start, pos);
        return isOctal;
    }

    /// <summary>
    /// `"" + +value` for the numeric strings the scanner builds (JsNumber.ToStringOfNumericString). Track O: a value that
    /// is already in canonical form stays the handle it is; another goes to the character arena.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private TextRef NumericStringValue(TextRef value)
    {
        var span = texts.Span(value);
        if (JsNumber.IsCanonicalNumericString(span))
        {
            return value;
        }
        return NumericStringValueSlow(value);
    }

    [NoAllocUnchecked("rare numeric form: a literal not in canonical form converts through a double and strings")]
    private TextRef NumericStringValueSlow(TextRef value)
    {
        return texts.Add(JsNumber.ToStringOfNumericString(texts.Span(value).ToString()));
    }

    /// <summary>
    /// Track O (O-NA): `"" + value` for an integer value of at most 2^53 (exact in a double) without a string: its decimal
    /// digits in the character arena.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private TextRef IntegerStringValue(ulong value)
    {
        Span<char> digits = stackalloc char[20];
        var at = digits.Length;
        do
        {
            digits[--at] = (char)('0' + (int)(value % 10));
            value /= 10;
        }
        while (value != 0);
        return texts.Add(digits[at..]);
    }

    /// <summary>
    /// Scans the given number of hexadecimal digits in the text,
    /// returning -1 if the given number is unavailable.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int ScanExactNumberOfHexDigits(int count, bool canHaveSeparators)
    {
        var digitCount = ScanHexDigits(/*minCount*/ count, /*scanAsManyAsPossible*/ false, canHaveSeparators, out var value);
        return digitCount != 0 ? HexValueToInt(value) : -1;
    }

    /// <summary>
    /// Scans as many hexadecimal digits as are available in the text,
    /// returning "" if the given number of digits was unavailable.
    /// (Track O: `parseInt(scanMinimumNumberOfHexDigits(...), 16)` of the callers, -1 for "".)
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int ScanMinimumNumberOfHexDigitsValue(int count, bool canHaveSeparators)
    {
        var digitCount = ScanHexDigits(/*minCount*/ count, /*scanAsManyAsPossible*/ true, canHaveSeparators, out var value);
        return digitCount != 0 ? HexValueToInt(value) : -1;
    }

    /// <summary>
    /// scanHexDigits without the string of digits (Track O, O-NA): returns the number of digits (0 where TypeScript
    /// returns "", i.e. fewer than <paramref name="minCount"/>) and their value, `parseInt(digits, 16)`, which saturates
    /// at <see cref="ulong.MaxValue"/>.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int ScanHexDigits(int minCount, bool scanAsManyAsPossible, bool canHaveSeparators, out ulong value)
    {
        var digitCount = 0;
        ulong digits = 0;
        var allowSeparator = false;
        var isPreviousTokenSeparator = false;
        while (digitCount < minCount || scanAsManyAsPossible)
        {
            var ch = CharCodeUnchecked(pos);
            if (canHaveSeparators && ch == (int)CharacterCodes._)
            {
                tokenFlags |= TokenFlags.ContainsSeparator;
                if (allowSeparator)
                {
                    allowSeparator = false;
                    isPreviousTokenSeparator = true;
                }
                else if (isPreviousTokenSeparator)
                {
                    Error(Diagnostics.Multiple_consecutive_numeric_separators_are_not_permitted, pos, 1);
                }
                else
                {
                    Error(Diagnostics.Numeric_separators_are_not_allowed_here, pos, 1);
                }
                pos++;
                continue;
            }
            allowSeparator = canHaveSeparators;
            int digit;
            if (ch >= (int)CharacterCodes.A && ch <= (int)CharacterCodes.F)
            {
                digit = ch - (int)CharacterCodes.A + 10; // (TypeScript standardizes hex literals to lowercase)
            }
            else if (ch >= (int)CharacterCodes.a && ch <= (int)CharacterCodes.f)
            {
                digit = ch - (int)CharacterCodes.a + 10;
            }
            else if (ch >= (int)CharacterCodes._0 && ch <= (int)CharacterCodes._9)
            {
                digit = ch - (int)CharacterCodes._0;
            }
            else
            {
                break;
            }
            digits = digits > (ulong.MaxValue >> 4) ? ulong.MaxValue : (digits << 4) | (uint)digit;
            digitCount++;
            pos++;
            isPreviousTokenSeparator = false;
        }
        if (digitCount < minCount)
        {
            digitCount = 0;
            digits = 0;
        }
        if (CharCodeUnchecked(pos - 1) == (int)CharacterCodes._)
        {
            Error(Diagnostics.Numeric_separators_are_not_allowed_here, pos - 1, 1);
        }
        value = digits;
        return digitCount;
    }

    /// <summary>
    /// The value of the hexadecimal digits text[from..to) (valid digits, at most 6 of them: the escapes `\uXXXX` and
    /// `\xXX`), as `parseInt(text.substring(from, to), 16)`.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int HexValueOfRange(int from, int to)
    {
        var value = 0;
        for (var i = from; i < to; i++)
        {
            int ch = text[i];
            value = (value << 4) | (ch <= '9' ? ch - '0' : (ch | 0x20) - 'a' + 10);
        }
        return value;
    }

    /// <summary>
    /// `parseInt(hexDigits, 16)` for the scanner's escape sequences: values beyond int.MaxValue (only possible in
    /// `\u{...}` with many digits) are kept above 0x10FFFF, which is all the scanner tests about them.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int HexValueToInt(ulong value) => value > int.MaxValue ? int.MaxValue : (int)value;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private TextRef ScanString(bool jsxAttributeString = false)
    {
        var quote = CharCodeUnchecked(pos);
        pos++;
        var result = new TextBuilder(texts); // TypeScript: `let result = ""` (Track O: a span of the text unless escapes change it)
        var start = pos;
        // Track O (O23-B): the quote is '"' or '\'' (both callers); the other one does not end the string.
        Debug.Assert(quote == (int)CharacterCodes.doubleQuote || quote == (int)CharacterCodes.singleQuote);
        while (true)
        {
            // typescript-go's scanString (internal/scanner/scanner.go) takes a fast path over the characters none of
            // the tests below handle; for each of them the loop below would only do `pos++`. (Track O, O24: the
            // characters it stops at, the quote, '\', LF and CR, found by SIMD, Scanner.Simd.cs.)
            pos = SkipStringCharacters(pos, quote);

            if (pos >= end)
            {
                result.Append(text, start, pos);
                tokenFlags |= TokenFlags.Unterminated;
                Error(Diagnostics.Unterminated_string_literal);
                break;
            }
            var ch = CharCodeUnchecked(pos);
            if (ch == quote)
            {
                result.Append(text, start, pos);
                pos++;
                break;
            }
            if (ch == (int)CharacterCodes.backslash && !jsxAttributeString)
            {
                result.Append(text, start, pos);
                ScanEscapeSequence(EscapeSequenceScanningFlags.String | EscapeSequenceScanningFlags.ReportErrors, ref result);
                start = pos;
                continue;
            }

            if ((ch == (int)CharacterCodes.lineFeed || ch == (int)CharacterCodes.carriageReturn) && !jsxAttributeString)
            {
                result.Append(text, start, pos);
                tokenFlags |= TokenFlags.Unterminated;
                Error(Diagnostics.Unterminated_string_literal);
                break;
            }
            pos++;
        }
        return result.Result();
    }

    /// <summary>
    /// Sets the current 'tokenValue' and returns a NoSubstitutionTemplateLiteral or
    /// a literal component of a TemplateExpression.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ScanTemplateAndSetTokenValue(bool shouldEmitInvalidEscapeError)
    {
        var startedWithBacktick = CharCodeUnchecked(pos) == (int)CharacterCodes.backtick;

        pos++;
        var start = pos;
        var contents = new TextBuilder(texts); // TypeScript: `let contents = ""` (Track O: a span of the text unless escapes or CRs change it)
        SyntaxKind resultingToken;

        while (true)
        {
            // typescript-go's scanTemplateAndSetTokenValue (internal/scanner/scanner.go) first skips the characters
            // that none of the tests below handle (`scanASCIIWhile(b != '`' && b != '$' && b != '\\' && b != '\r')`);
            // for each of them the loop below would only do `pos++`. (Track O, O24: by SIMD, Scanner.Simd.cs.)
            pos = SkipTemplateCharacters(pos);

            if (pos >= end)
            {
                contents.Append(text, start, pos);
                tokenFlags |= TokenFlags.Unterminated;
                Error(Diagnostics.Unterminated_template_literal);
                resultingToken = startedWithBacktick ? SyntaxKind.NoSubstitutionTemplateLiteral : SyntaxKind.TemplateTail;
                break;
            }

            var currChar = CharCodeUnchecked(pos);

            // '`'
            if (currChar == (int)CharacterCodes.backtick)
            {
                contents.Append(text, start, pos);
                pos++;
                resultingToken = startedWithBacktick ? SyntaxKind.NoSubstitutionTemplateLiteral : SyntaxKind.TemplateTail;
                break;
            }

            // '${'
            if (currChar == (int)CharacterCodes.Dollar && pos + 1 < end && CharCodeUnchecked(pos + 1) == (int)CharacterCodes.openBrace)
            {
                contents.Append(text, start, pos);
                pos += 2;
                resultingToken = startedWithBacktick ? SyntaxKind.TemplateHead : SyntaxKind.TemplateMiddle;
                break;
            }

            // Escape character
            if (currChar == (int)CharacterCodes.backslash)
            {
                contents.Append(text, start, pos);
                ScanEscapeSequence(EscapeSequenceScanningFlags.String | (shouldEmitInvalidEscapeError ? EscapeSequenceScanningFlags.ReportErrors : 0), ref contents);
                start = pos;
                continue;
            }

            // Speculated ECMAScript 6 Spec 11.8.6.1:
            // <CR><LF> and <CR> LineTerminatorSequences are normalized to <LF> for Template Values
            if (currChar == (int)CharacterCodes.carriageReturn)
            {
                contents.Append(text, start, pos);
                pos++;

                if (pos < end && CharCodeUnchecked(pos) == (int)CharacterCodes.lineFeed)
                {
                    pos++;
                    // TypeScript: `contents += "\n"`. The "\n" is the LF just consumed; appending it as that range
                    // of the text lets the builder merge it with the characters that follow.
                    contents.Append(text, pos - 1, pos);
                }
                else
                {
                    contents.Append("\n");
                }
                start = pos;
                continue;
            }

            pos++;
        }

        // TypeScript: `Debug.assert(resultingToken !== undefined);`. C#'s definite assignment proves it at
        // compile time (every exit of the loop assigns it), and the analyzers reject the always-true test (CA1508).

        tokenValue = contents.Result();
        return resultingToken;
    }

    // Extract from Section A.1
    // EscapeSequence ::=
    //     | CharacterEscapeSequence
    //     | 0 (?![0-9])
    //     | LegacyOctalEscapeSequence
    //     | NonOctalDecimalEscapeSequence
    //     | HexEscapeSequence
    //     | UnicodeEscapeSequence
    // LegacyOctalEscapeSequence ::=
    //     | '0' (?=[89])
    //     | [1-7] (?![0-7])
    //     | [0-3] [0-7] [0-7]?
    //     | [4-7] [0-7]
    // NonOctalDecimalEscapeSequence ::= [89]
    // Track O (O-NA): scanEscapeSequence appends the cooked characters to the value the caller builds (TypeScript:
    // `result += scanEscapeSequence(...)`) instead of returning a string; ScanEscapeSequenceString keeps the string
    // form for the regular expression checker.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void ScanEscapeSequence(EscapeSequenceScanningFlags flags, ref TextBuilder result)
    {
        var start = pos;
        pos++;
        if (pos >= end)
        {
            Error(Diagnostics.Unexpected_end_of_text);
            return;
        }
        var ch = CharCodeUnchecked(pos);
        pos++;
        switch (ch)
        {
            case (int)CharacterCodes._0:
                // Although '0' preceding any digit is treated as LegacyOctalEscapeSequence,
                // '\08' should separately be interpreted as '\0' + '8'.
                if (pos >= end || !IsDigit(CharCodeUnchecked(pos)))
                {
                    result.Append('\0');
                    return;
                }
                // '\01', '\011'
                goto case (int)CharacterCodes._1;
            case (int)CharacterCodes._1:
            case (int)CharacterCodes._2:
            case (int)CharacterCodes._3:
                // '\1', '\17', '\177'
                if (pos < end && IsOctalDigit(CharCodeUnchecked(pos)))
                {
                    pos++;
                }
                // '\17', '\177'
                goto case (int)CharacterCodes._4;
            case (int)CharacterCodes._4:
            case (int)CharacterCodes._5:
            case (int)CharacterCodes._6:
            case (int)CharacterCodes._7:
                // '\4', '\47' but not '\477'
                if (pos < end && IsOctalDigit(CharCodeUnchecked(pos)))
                {
                    pos++;
                }
                // '\47'
                tokenFlags |= TokenFlags.ContainsInvalidEscape;
                if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                {
                    result.Append((char)(ReportOctalEscape(flags, start, ch) & 0xFFFF));
                    return;
                }
                result.AppendSource(start, pos);
                return;
            case (int)CharacterCodes._8:
            case (int)CharacterCodes._9:
                // the invalid '\8' and '\9'
                tokenFlags |= TokenFlags.ContainsInvalidEscape;
                if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                {
                    if ((flags & EscapeSequenceScanningFlags.RegularExpression) != 0 && (flags & EscapeSequenceScanningFlags.AtomEscape) == 0)
                    {
                        Error(Diagnostics.Decimal_escape_sequences_and_backreferences_are_not_allowed_in_a_character_class, start, pos - start);
                    }
                    else
                    {
                        ReportEscapeNotAllowed(start);
                    }
                    result.Append((char)ch);
                    return;
                }
                result.AppendSource(start, pos);
                return;
            case (int)CharacterCodes.b:
                result.Append('\b');
                return;
            case (int)CharacterCodes.t:
                result.Append('\t');
                return;
            case (int)CharacterCodes.n:
                result.Append('\n');
                return;
            case (int)CharacterCodes.v:
                result.Append('\v');
                return;
            case (int)CharacterCodes.f:
                result.Append('\f');
                return;
            case (int)CharacterCodes.r:
                result.Append('\r');
                return;
            case (int)CharacterCodes.singleQuote:
                result.Append('\'');
                return;
            case (int)CharacterCodes.doubleQuote:
                result.Append('"');
                return;
            case (int)CharacterCodes.u:
                if (pos < end && CharCodeUnchecked(pos) == (int)CharacterCodes.openBrace)
                {
                    // '\u{DDDDDD}'
                    pos -= 2;
                    ScanExtendedUnicodeEscape((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0, ref result);
                    if ((flags & EscapeSequenceScanningFlags.AllowExtendedUnicodeEscape) == 0)
                    {
                        tokenFlags |= TokenFlags.ContainsInvalidEscape;
                        if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                        {
                            Error(Diagnostics.Unicode_escape_sequences_are_only_available_when_the_Unicode_u_flag_or_the_Unicode_Sets_v_flag_is_set, start, pos - start);
                        }
                    }
                    return;
                }
                // '\uDDDD'
                for (; pos < start + 6; pos++)
                {
                    if (!(pos < end && IsHexDigit(CharCodeUnchecked(pos))))
                    {
                        tokenFlags |= TokenFlags.ContainsInvalidEscape;
                        if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                        {
                            Error(Diagnostics.Hexadecimal_digit_expected);
                        }
                        result.AppendSource(start, pos);
                        return;
                    }
                }
                tokenFlags |= TokenFlags.UnicodeEscape;
                var escapedValue = HexValueOfRange(start + 2, pos);
                if (
                    (flags & EscapeSequenceScanningFlags.AnyUnicodeMode) != 0 && escapedValue >= 0xD800 && escapedValue <= 0xDBFF &&
                    pos + 6 < end && CharCodeUnchecked(pos) == (int)CharacterCodes.backslash && CharCodeUnchecked(pos + 1) == (int)CharacterCodes.u && CharCodeUnchecked(pos + 2) != (int)CharacterCodes.openBrace
                )
                {
                    // For regular expressions in any Unicode mode, \u HexLeadSurrogate \u HexTrailSurrogate is treated as a single character
                    // for the purpose of determining whether a character class range is out of order
                    // https://tc39.es/ecma262/#prod-RegExpUnicodeEscapeSequence
                    var nextStart = pos;
                    var nextPos = pos + 2;
                    for (; nextPos < nextStart + 6; nextPos++)
                    {
                        if (!IsHexDigit(CharCodeUnchecked(nextPos)))
                        {
                            // leave the error to the next call
                            result.Append((char)escapedValue);
                            return;
                        }
                    }
                    var nextEscapedValue = HexValueOfRange(nextStart + 2, nextPos);
                    if (nextEscapedValue >= 0xDC00 && nextEscapedValue <= 0xDFFF)
                    {
                        pos = nextPos;
                        result.Append((char)escapedValue);
                        result.Append((char)nextEscapedValue);
                        return;
                    }
                }
                result.Append((char)escapedValue);
                return;

            case (int)CharacterCodes.x:
                // '\xDD'
                for (; pos < start + 4; pos++)
                {
                    if (!(pos < end && IsHexDigit(CharCodeUnchecked(pos))))
                    {
                        tokenFlags |= TokenFlags.ContainsInvalidEscape;
                        if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                        {
                            Error(Diagnostics.Hexadecimal_digit_expected);
                        }
                        result.AppendSource(start, pos);
                        return;
                    }
                }
                tokenFlags |= TokenFlags.HexEscape;
                result.Append((char)HexValueOfRange(start + 2, pos));
                return;

            // when encountering a LineContinuation (i.e. a backslash and a line terminator sequence),
            // the line terminator is interpreted to be "the empty code unit sequence".
            case (int)CharacterCodes.carriageReturn:
                if (pos < end && CharCodeUnchecked(pos) == (int)CharacterCodes.lineFeed)
                {
                    pos++;
                }
                goto case (int)CharacterCodes.lineFeed;
            case (int)CharacterCodes.lineFeed:
            case (int)CharacterCodes.lineSeparator:
            case (int)CharacterCodes.paragraphSeparator:
                return;
            default:
                if (
                    (flags & EscapeSequenceScanningFlags.AnyUnicodeMode) != 0
                    || ((flags & EscapeSequenceScanningFlags.RegularExpression) != 0
                        && (flags & EscapeSequenceScanningFlags.AnnexB) == 0
                        && IsIdentifierPart(ch, languageVersion))
                )
                {
                    Error(Diagnostics.This_character_cannot_be_escaped_in_a_regular_expression, pos - 2, 2);
                }
                result.Append((char)ch);
                return;
        }
    }

    // The octal escape error of scanEscapeSequence: reports it and returns the escape's value.
    [NoAllocUnchecked("error path: an octal escape sequence is reported with its hexadecimal form as a string")]
    private int ReportOctalEscape(EscapeSequenceScanningFlags flags, int start, int ch)
    {
        var code = (int)JsNumber.ParseInt(Substring(text, start + 1, pos), 8);
        if ((flags & EscapeSequenceScanningFlags.RegularExpression) != 0 && (flags & EscapeSequenceScanningFlags.AtomEscape) == 0 && ch != (int)CharacterCodes._0)
        {
            Error(Diagnostics.Octal_escape_sequences_and_backreferences_are_not_allowed_in_a_character_class_If_this_was_intended_as_an_escape_sequence_use_the_syntax_0_instead, start, pos - start, "\\x" + code.ToString("x", CultureInfo.InvariantCulture).PadLeft(2, '0'));
        }
        else
        {
            Error(Diagnostics.Octal_escape_sequences_are_not_allowed_Use_the_syntax_0, start, pos - start, "\\x" + code.ToString("x", CultureInfo.InvariantCulture).PadLeft(2, '0'));
        }
        return code;
    }

    [NoAllocUnchecked("error path: the escape sequence is reported as a string")]
    private void ReportEscapeNotAllowed(int start)
    {
        Error(Diagnostics.Escape_sequence_0_is_not_allowed, start, pos - start, Substring(text, start, pos));
    }

    /// <summary>scanEscapeSequence as TypeScript returns it (the string), for the regular expression checker.</summary>
    [NoAllocUnchecked("regex checking: the escape's characters as a string")]
    private string ScanEscapeSequenceString(EscapeSequenceScanningFlags flags)
    {
        // The characters are built in the arena and taken out again (nothing else writes to it meanwhile).
        var mark = texts.CharCount;
        var builder = new TextBuilder(texts);
        ScanEscapeSequence(flags, ref builder);
        var value = texts.GetString(builder.Result())!;
        texts.SetCharCount(mark);
        return value;
    }

    // Track O (O-NA): appends the cooked characters (TypeScript returns them as a string).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void ScanExtendedUnicodeEscape(bool shouldEmitInvalidEscapeError, ref TextBuilder result)
    {
        var start = pos;
        pos += 3;
        var escapedStart = pos;
        var escapedValue = ScanMinimumNumberOfHexDigitsValue(1, /*canHaveSeparators*/ false);
        var isInvalidExtendedEscape = false;

        // Validate the value of the digit
        if (escapedValue < 0)
        {
            if (shouldEmitInvalidEscapeError)
            {
                Error(Diagnostics.Hexadecimal_digit_expected);
            }
            isInvalidExtendedEscape = true;
        }
        else if (escapedValue > 0x10FFFF)
        {
            if (shouldEmitInvalidEscapeError)
            {
                Error(Diagnostics.An_extended_Unicode_escape_value_must_be_between_0x0_and_0x10FFFF_inclusive, escapedStart, pos - escapedStart);
            }
            isInvalidExtendedEscape = true;
        }

        if (pos >= end)
        {
            if (shouldEmitInvalidEscapeError)
            {
                Error(Diagnostics.Unexpected_end_of_text);
            }
            isInvalidExtendedEscape = true;
        }
        else if (CharCodeUnchecked(pos) == (int)CharacterCodes.closeBrace)
        {
            // Only swallow the following character up if it's a '}'.
            pos++;
        }
        else
        {
            if (shouldEmitInvalidEscapeError)
            {
                Error(Diagnostics.Unterminated_Unicode_escape_sequence);
            }
            isInvalidExtendedEscape = true;
        }

        if (isInvalidExtendedEscape)
        {
            tokenFlags |= TokenFlags.ContainsInvalidEscape;
            result.AppendSource(start, pos);
            return;
        }

        tokenFlags |= TokenFlags.ExtendedUnicodeEscape;
        AppendCodePoint(ref result, escapedValue);
    }

    // Current character is known to be a backslash. Check for Unicode escape of the form '\uXXXX'
    // and return code point value if valid Unicode escape is found. Otherwise return -1.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int PeekUnicodeEscape()
    {
        if (pos + 5 < end && CharCodeUnchecked(pos + 1) == (int)CharacterCodes.u)
        {
            var start = pos;
            pos += 2;
            var value = ScanExactNumberOfHexDigits(4, /*canHaveSeparators*/ false);
            pos = start;
            return value;
        }
        return -1;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int PeekExtendedUnicodeEscape()
    {
        if (CodePointUnchecked(pos + 1) == (int)CharacterCodes.u && CodePointUnchecked(pos + 2) == (int)CharacterCodes.openBrace)
        {
            var start = pos;
            pos += 3;
            var escapedValue = ScanMinimumNumberOfHexDigitsValue(1, /*canHaveSeparators*/ false);
            pos = start;
            return escapedValue;
        }
        return -1;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private TextRef ScanIdentifierParts()
    {
        var result = new TextBuilder(texts); // TypeScript: `let result = ""`
        ScanIdentifierParts(ref result);
        return result.Result();
    }

    /// <summary>Track O (O5a): scanIdentifierParts appending to a value built by the caller (`value + scanIdentifierParts()`).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void ScanIdentifierParts(ref TextBuilder result)
    {
        var start = pos;
        while (pos < end)
        {
            var ch = CodePointUnchecked(pos);
            if (IsIdentifierPart(ch, languageVersion))
            {
                pos += CharSize(ch);
            }
            else if (ch == (int)CharacterCodes.backslash)
            {
                ch = PeekExtendedUnicodeEscape();
                if (ch >= 0 && IsIdentifierPart(ch, languageVersion))
                {
                    ScanExtendedUnicodeEscape(/*shouldEmitInvalidEscapeError*/ true, ref result);
                    start = pos;
                    continue;
                }
                ch = PeekUnicodeEscape();
                if (!(ch >= 0 && IsIdentifierPart(ch, languageVersion)))
                {
                    break;
                }
                tokenFlags |= TokenFlags.UnicodeEscape;
                result.Append(text, start, pos);
                AppendCodePoint(ref result, ch);
                // Valid Unicode escape is always six characters
                pos += 6;
                start = pos;
            }
            else
            {
                break;
            }
        }
        result.Append(text, start, pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind GetIdentifierToken()
    {
        // Reserved words are between 2 and 12 characters long and start with a lowercase letter
        var len = tokenValue.Length;
        if (len >= 2 && len <= 12)
        {
            var value = texts.Span(tokenValue);
            int ch = value[0];
            if (ch >= (int)CharacterCodes.a && ch <= (int)CharacterCodes.z)
            {
                if (TryGetKeyword(value, out var keyword))
                {
                    return token = keyword;
                }
            }
        }
        return token = SyntaxKind.Identifier;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private TextRef ScanBinaryOrOctalDigits(int @base)
    {
        var value = new TextBuilder(texts); // TypeScript: `let value = ""`
        // For counting number of digits; Valid binaryIntegerLiteral must have at least one binary digit following B or b.
        // Similarly valid octalIntegerLiteral must have at least one octal digit following o or O.
        var separatorAllowed = false;
        var isPreviousTokenSeparator = false;
        while (true)
        {
            var ch = CharCodeUnchecked(pos);
            // Numeric separators are allowed anywhere within a numeric literal, except not at the beginning, or following another separator
            if (ch == (int)CharacterCodes._)
            {
                tokenFlags |= TokenFlags.ContainsSeparator;
                if (separatorAllowed)
                {
                    separatorAllowed = false;
                    isPreviousTokenSeparator = true;
                }
                else if (isPreviousTokenSeparator)
                {
                    Error(Diagnostics.Multiple_consecutive_numeric_separators_are_not_permitted, pos, 1);
                }
                else
                {
                    Error(Diagnostics.Numeric_separators_are_not_allowed_here, pos, 1);
                }
                pos++;
                continue;
            }
            separatorAllowed = true;
            if (!IsDigit(ch) || ch - (int)CharacterCodes._0 >= @base)
            {
                break;
            }
            value.Append(text, pos, pos + 1); // TypeScript: `value += text[pos]`
            pos++;
            isPreviousTokenSeparator = false;
        }
        if (CharCodeUnchecked(pos - 1) == (int)CharacterCodes._)
        {
            // Literal ends with underscore - not allowed
            Error(Diagnostics.Numeric_separators_are_not_allowed_here, pos - 1, 1);
        }
        return value.Result();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind CheckBigIntSuffix()
    {
        if (CharCodeUnchecked(pos) == (int)CharacterCodes.n)
        {
            // TypeScript: `tokenValue += "n"` (Track O: the "n" of the text, which continues a value that is a span of it).
            tokenValue = texts.Concat(tokenValue, texts.Substring(pos, pos + 1));
            // Use base 10 instead of base 2 or base 8 for shorter literals
            if ((tokenFlags & TokenFlags.BinaryOrOctalSpecifier) != 0)
            {
                tokenValue = BinaryOrOctalBigIntValue();
            }
            pos++;
            return SyntaxKind.BigIntLiteral;
        }
        else
        { // not a bigint, so can convert to number in simplified form
            // Number() may not support 0b or 0o, so use parseInt() instead
            if ((tokenFlags & (TokenFlags.BinarySpecifier | TokenFlags.OctalSpecifier)) == 0)
            {
                // `"" + +tokenValue` (an already canonical integer string stays as it is).
                tokenValue = NumericStringValue(tokenValue);
                return SyntaxKind.NumericLiteral;
            }
            var radix = (tokenFlags & TokenFlags.BinarySpecifier) != 0 ? 2 : 8;
            // Track O (O-NA): values exact in a double are converted without strings.
            tokenValue = TryParseSafeInteger(texts.Span(tokenValue.Slice(2)), radix, out var value) // skip "0b" or "0o"
                ? IntegerStringValue(value)
                : BinaryOrOctalValueSlow(radix);
            return SyntaxKind.NumericLiteral;
        }
    }

    [NoAllocUnchecked("rare numeric form: a binary or octal bigint literal is converted through strings")]
    private TextRef BinaryOrOctalBigIntValue()
    {
        return texts.Add(Utilities.ParsePseudoBigInt(texts.GetString(tokenValue)!) + "n");
    }

    [NoAllocUnchecked("rare numeric form: a binary or octal literal beyond 2^53 is converted through strings")]
    private TextRef BinaryOrOctalValueSlow(int radix)
    {
        var digits = texts.GetString(tokenValue.Slice(2))!; // skip "0b" or "0o"
        return texts.Add(JsNumber.ToString(JsNumber.ParseInt(digits, radix)));
    }

    /// <summary>
    /// Track O (O-NA): `parseInt(digits, radix)` for digits of the radix whose value is at most 2^53 - 1 (exact in a
    /// double); false for other values (and for no digits, which parseInt makes NaN).
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool TryParseSafeInteger(ReadOnlySpan<char> digits, int radix, out ulong value)
    {
        const ulong MaxSafeInteger = (1UL << 53) - 1;
        value = 0;
        if (digits.IsEmpty)
        {
            return false;
        }
        foreach (var ch in digits)
        {
            var digit = (uint)(ch - '0');
            if (digit >= (uint)radix || value > (MaxSafeInteger - digit) / (uint)radix)
            {
                return false;
            }
            value = (value * (uint)radix) + digit;
        }
        return true;
    }

    /// <summary>
    /// The value of a hexadecimal literal `0x...` without a bigint suffix: TypeScript builds `"0x" + digits` and converts
    /// it (`"" + +tokenValue`); Track O converts the value scanned (exact up to 2^53 - 1) and builds the strings only for
    /// a bigint suffix or a larger value.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ScanHexLiteralValue(int digitsStart, ulong value)
    {
        if (CharCodeUnchecked(pos) == (int)CharacterCodes.n || value > (1UL << 53) - 1)
        {
            return ScanHexLiteralValueSlow(digitsStart);
        }
        tokenValue = IntegerStringValue(value);
        return SyntaxKind.NumericLiteral;
    }

    // TypeScript's way: `tokenValue = "0x" + hexDigits` (digits lowercased, separators dropped, "0" if none), then
    // checkBigIntSuffix.
    [NoAllocUnchecked("rare numeric form: a hexadecimal bigint literal or a value beyond 2^53 is built as a string")]
    private SyntaxKind ScanHexLiteralValueSlow(int digitsStart)
    {
        var hexDigits = new StringBuilder();
        for (var i = digitsStart; i < pos; i++)
        {
            var ch = text[i];
            if (ch != '_')
            {
                hexDigits.Append(ch >= 'A' && ch <= 'F' ? (char)(ch + ('a' - 'A')) : ch);
            }
        }
        tokenValue = texts.Add("0x" + (hexDigits.Length == 0 ? "0" : hexDigits.ToString()));
        return CheckBigIntSuffix();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind Scan()
    {
        // Track O (O23-B, O24): the common tokens by the ASCII table, in a small method, with the token state in locals
        // until the token is known. First the trivia before the token (skipTrivia): ASCII white space and line breaks
        // (each of them would come back to the loop of ScanToken and do `pos++`, a line break also setting
        // PrecedingLineBreak; after a line break the run of ASCII trivia by SIMD), and single-line comments (ScanToken's
        // case: the body to the first line break, the comment directive, `continue`). Then an identifier or keyword that
        // starts with an ASCII character, a punctuator that is always one character, '.', '=' or a string literal, as
        // ScanToken's cases return them. Everything else, from the first character none of these handles, goes to
        // ScanToken (the loop of scanner.ts's scan), which starts at that character as the loop would after `continue`.
        // (The text has a NUL after its last character, so text[p + 1] for p &lt; end reads what CharCodeUnchecked
        // tests: the character, or the NUL where CharCodeUnchecked returns NaN; neither matches any character tested.)
        var p = pos;
        fullStartPos = p;
        var limit = end;
        var flags = TokenFlags.None;
        var table = AsciiCharFlags;
        if (skipTrivia)
        {
            while (p < limit)
            {
                int c = text[p];
                if (c >= 128)
                {
                    break;
                }
                var f = table[c];
                if ((f & (byte)CharFlags.WhiteSpace) != 0)
                {
                    p++;
                    continue;
                }
                if ((f & (byte)CharFlags.LineBreak) != 0)
                {
                    flags |= TokenFlags.PrecedingLineBreak;
                    p = SkipAsciiWhiteSpaceOrLineBreak(p + 1);
                    continue;
                }
                if (c == (int)CharacterCodes.slash && text[p + 1] == (int)CharacterCodes.slash)
                {
                    var commentEnd = FindLineBreak(p + 2);
                    if (MayBeCommentDirective(p, commentEnd))
                    {
                        // (The directive's range ends at pos.)
                        pos = commentEnd;
                        commentDirectives = AppendIfCommentDirective(commentDirectives, p, commentEnd, /*multiLine*/ false, p);
                    }
                    p = commentEnd;
                    continue;
                }
                break;
            }
        }
        tokenFlags = flags;
        if (p < limit)
        {
            int ch = text[p];
            if (ch < 128)
            {
                var f = table[ch];
                if ((f & (byte)CharFlags.IdentifierStart) != 0)
                {
                    tokenStart = p;
                    var asciiEnd = SkipAsciiIdentifierParts(p + 1);
                    pos = asciiEnd;
                    if (asciiEnd < limit)
                    {
                        int next = text[asciiEnd];
                        if (next > (int)CharacterCodes.maxAsciiCharacter || next == (int)CharacterCodes.backslash)
                        {
                            return ScanIdentifierRest(languageVersion);
                        }
                    }
                    return GetIdentifierTokenOfRange(p, asciiEnd);
                }
                if ((f & (byte)CharFlags.SingleCharToken) != 0)
                {
                    tokenStart = p;
                    pos = p + 1;
                    return token = (SyntaxKind)AsciiSingleCharTokens[ch];
                }
                switch (ch)
                {
                    case (int)CharacterCodes.dot:
                        {
                            int next = text[p + 1];
                            if (next != (int)CharacterCodes.dot && !IsDigit(next))
                            {
                                tokenStart = p;
                                pos = p + 1;
                                return token = SyntaxKind.DotToken;
                            }
                            break;
                        }
                    case (int)CharacterCodes.equals:
                        {
                            // (A conflict marker starts with seven '='.)
                            int next = text[p + 1];
                            if (next != (int)CharacterCodes.equals && next != (int)CharacterCodes.greaterThan)
                            {
                                tokenStart = p;
                                pos = p + 1;
                                return token = SyntaxKind.EqualsToken;
                            }
                            break;
                        }
                    case (int)CharacterCodes.doubleQuote:
                    case (int)CharacterCodes.singleQuote:
                        tokenStart = p;
                        pos = p;
                        tokenValue = ScanString();
                        return token = SyntaxKind.StringLiteral;
                }
            }
        }
        pos = p;
        return ScanToken();
    }

    /// <summary>
    /// Track O (step O11 variant C, the parser's exact pre-decisions): the kind of the token <see cref="Scan"/> would
    /// return next, read without scanning and without changing any state, when the way there is plain: from
    /// <see cref="pos"/> only ASCII white space that is not a line break (trivia skipped), then an identifier or keyword
    /// that <see cref="Scan"/> finishes on its fast path (an ASCII identifier start, ASCII identifier parts, ended by
    /// the end or an ASCII character other than '\'; the kind is what <see cref="GetIdentifierTokenOfRange"/> returns),
    /// a punctuator that is always one character (<see cref="AsciiSingleCharTokens"/>), or `=`, `&lt;`, `&gt;` or `?`
    /// where <see cref="ScanToken"/> would return EqualsToken, LessThanToken, GreaterThanToken or QuestionToken
    /// (the characters after them are read as ScanToken reads them, not at a line start, so no conflict marker).
    /// The token then has no preceding line break, and no comment lies before it. <see cref="SyntaxKind.Unknown"/> in
    /// every other case.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind PeekPlainToken()
    {
        if (!skipTrivia)
        {
            return SyntaxKind.Unknown;
        }
        var flags = AsciiCharFlags;
        var p = pos;
        while (p < end)
        {
            int ch = text[p];
            if (ch >= 128)
            {
                return SyntaxKind.Unknown;
            }
            var f = flags[ch];
            if ((f & (byte)CharFlags.LineBreak) != 0)
            {
                return SyntaxKind.Unknown;
            }
            if ((f & (byte)CharFlags.WhiteSpace) != 0)
            {
                p++;
                continue;
            }
            if ((f & (byte)CharFlags.IdentifierStart) != 0)
            {
                var asciiEnd = SkipAsciiIdentifierParts(p + 1);
                if (asciiEnd < end)
                {
                    int next = text[asciiEnd];
                    if (next > (int)CharacterCodes.maxAsciiCharacter || next == (int)CharacterCodes.backslash)
                    {
                        return SyntaxKind.Unknown;
                    }
                }
                var len = asciiEnd - p;
                if (len >= 2 && len <= 12 && ch >= (int)CharacterCodes.a && ch <= (int)CharacterCodes.z)
                {
                    var keyword = MatchKeyword(p, len);
                    if (keyword != SyntaxKind.Unknown)
                    {
                        return keyword;
                    }
                }
                return SyntaxKind.Identifier;
            }
            if ((f & (byte)CharFlags.SingleCharToken) != 0)
            {
                return (SyntaxKind)AsciiSingleCharTokens[ch];
            }
            if (p + 2 >= end || IsConflictMarkerTrivia(text, p))
            {
                return SyntaxKind.Unknown;
            }
            int next1 = text[p + 1];
            switch (ch)
            {
                case (int)CharacterCodes.equals:
                    return next1 != (int)CharacterCodes.equals && next1 != (int)CharacterCodes.greaterThan ? SyntaxKind.EqualsToken : SyntaxKind.Unknown;
                case (int)CharacterCodes.lessThan:
                    return next1 != (int)CharacterCodes.lessThan && next1 != (int)CharacterCodes.equals && next1 != (int)CharacterCodes.slash ? SyntaxKind.LessThanToken : SyntaxKind.Unknown;
                case (int)CharacterCodes.greaterThan:
                    return SyntaxKind.GreaterThanToken;
                case (int)CharacterCodes.question:
                    return next1 != (int)CharacterCodes.dot && next1 != (int)CharacterCodes.question ? SyntaxKind.QuestionToken : SyntaxKind.Unknown;
                default:
                    return SyntaxKind.Unknown;
            }
        }
        return SyntaxKind.Unknown;
    }

    /// <summary>
    /// The loop of scanner.ts's scan (Track O, O23-B: entered from <see cref="Scan"/>, which has reset the token's flags
    /// and handled the common tokens).
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ScanToken()
    {
        while (true)
        {
            tokenStart = pos;
            if (pos >= end)
            {
                return token = SyntaxKind.EndOfFileToken;
            }

            var ch = CodePointUnchecked(pos);
            if (pos == 0)
            {
                // Special handling for shebang
                if (ch == (int)CharacterCodes.hash && IsShebangTrivia(text, pos))
                {
                    pos = ScanShebangTrivia(text, pos);
                    if (skipTrivia)
                    {
                        continue;
                    }
                    else
                    {
                        return token = SyntaxKind.ShebangTrivia;
                    }
                }
            }

            switch (ch)
            {
                case (int)CharacterCodes.lineFeed:
                case (int)CharacterCodes.carriageReturn:
                    tokenFlags |= TokenFlags.PrecedingLineBreak;
                    if (skipTrivia)
                    {
                        pos++;
                        // typescript-go's Scan (internal/scanner/scanner.go) consumes the run of ASCII white space and
                        // line breaks after a line break in one loop. Each of these characters would come back here or
                        // to the white space case below and do the same (pos++ and continue; the line break flag is set).
                        // (Track O, O24: the run by SIMD.)
                        pos = SkipAsciiWhiteSpaceOrLineBreak(pos);
                        continue;
                    }
                    else
                    {
                        if (ch == (int)CharacterCodes.carriageReturn && pos + 1 < end && CharCodeUnchecked(pos + 1) == (int)CharacterCodes.lineFeed)
                        {
                            // consume both CR and LF
                            pos += 2;
                        }
                        else
                        {
                            pos++;
                        }
                        return token = SyntaxKind.NewLineTrivia;
                    }
                case (int)CharacterCodes.tab:
                case (int)CharacterCodes.verticalTab:
                case (int)CharacterCodes.formFeed:
                case (int)CharacterCodes.space:
                case (int)CharacterCodes.nonBreakingSpace:
                case (int)CharacterCodes.ogham:
                case (int)CharacterCodes.enQuad:
                case (int)CharacterCodes.emQuad:
                case (int)CharacterCodes.enSpace:
                case (int)CharacterCodes.emSpace:
                case (int)CharacterCodes.threePerEmSpace:
                case (int)CharacterCodes.fourPerEmSpace:
                case (int)CharacterCodes.sixPerEmSpace:
                case (int)CharacterCodes.figureSpace:
                case (int)CharacterCodes.punctuationSpace:
                case (int)CharacterCodes.thinSpace:
                case (int)CharacterCodes.hairSpace:
                case (int)CharacterCodes.zeroWidthSpace:
                case (int)CharacterCodes.narrowNoBreakSpace:
                case (int)CharacterCodes.mathematicalSpace:
                case (int)CharacterCodes.ideographicSpace:
                case (int)CharacterCodes.byteOrderMark:
                    if (skipTrivia)
                    {
                        pos++;
                        continue;
                    }
                    else
                    {
                        while (pos < end && IsWhiteSpaceSingleLine(CharCodeUnchecked(pos)))
                        {
                            pos++;
                        }
                        return token = SyntaxKind.WhitespaceTrivia;
                    }
                case (int)CharacterCodes.exclamation:
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.equals)
                    {
                        if (CharCodeUnchecked(pos + 2) == (int)CharacterCodes.equals)
                        {
                            pos += 3;
                            return token = SyntaxKind.ExclamationEqualsEqualsToken;
                        }
                        pos += 2;
                        return token = SyntaxKind.ExclamationEqualsToken;
                    }
                    pos++;
                    return token = SyntaxKind.ExclamationToken;
                case (int)CharacterCodes.doubleQuote:
                case (int)CharacterCodes.singleQuote:
                    tokenValue = ScanString();
                    return token = SyntaxKind.StringLiteral;
                case (int)CharacterCodes.backtick:
                    return token = ScanTemplateAndSetTokenValue(/*shouldEmitInvalidEscapeError*/ false);
                case (int)CharacterCodes.percent:
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.equals)
                    {
                        pos += 2;
                        return token = SyntaxKind.PercentEqualsToken;
                    }
                    pos++;
                    return token = SyntaxKind.PercentToken;
                case (int)CharacterCodes.ampersand:
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.ampersand)
                    {
                        if (CharCodeUnchecked(pos + 2) == (int)CharacterCodes.equals)
                        {
                            pos += 3;
                            return token = SyntaxKind.AmpersandAmpersandEqualsToken;
                        }
                        pos += 2;
                        return token = SyntaxKind.AmpersandAmpersandToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.equals)
                    {
                        pos += 2;
                        return token = SyntaxKind.AmpersandEqualsToken;
                    }
                    pos++;
                    return token = SyntaxKind.AmpersandToken;
                case (int)CharacterCodes.openParen:
                    pos++;
                    return token = SyntaxKind.OpenParenToken;
                case (int)CharacterCodes.closeParen:
                    pos++;
                    return token = SyntaxKind.CloseParenToken;
                case (int)CharacterCodes.asterisk:
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.equals)
                    {
                        pos += 2;
                        return token = SyntaxKind.AsteriskEqualsToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.asterisk)
                    {
                        if (CharCodeUnchecked(pos + 2) == (int)CharacterCodes.equals)
                        {
                            pos += 3;
                            return token = SyntaxKind.AsteriskAsteriskEqualsToken;
                        }
                        pos += 2;
                        return token = SyntaxKind.AsteriskAsteriskToken;
                    }
                    pos++;
                    if (
                        skipJsDocLeadingAsterisks != 0 &&
                        (tokenFlags & TokenFlags.PrecedingJSDocLeadingAsterisks) == 0 &&
                        (tokenFlags & TokenFlags.PrecedingLineBreak) != 0
                    )
                    {
                        // decoration at the start of a JSDoc comment line
                        tokenFlags |= TokenFlags.PrecedingJSDocLeadingAsterisks;
                        continue;
                    }
                    return token = SyntaxKind.AsteriskToken;
                case (int)CharacterCodes.plus:
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.plus)
                    {
                        pos += 2;
                        return token = SyntaxKind.PlusPlusToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.equals)
                    {
                        pos += 2;
                        return token = SyntaxKind.PlusEqualsToken;
                    }
                    pos++;
                    return token = SyntaxKind.PlusToken;
                case (int)CharacterCodes.comma:
                    pos++;
                    return token = SyntaxKind.CommaToken;
                case (int)CharacterCodes.minus:
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.minus)
                    {
                        pos += 2;
                        return token = SyntaxKind.MinusMinusToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.equals)
                    {
                        pos += 2;
                        return token = SyntaxKind.MinusEqualsToken;
                    }
                    pos++;
                    return token = SyntaxKind.MinusToken;
                case (int)CharacterCodes.dot:
                    if (IsDigit(CharCodeUnchecked(pos + 1)))
                    {
                        ScanNumber();
                        return token = SyntaxKind.NumericLiteral;
                    }
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.dot && CharCodeUnchecked(pos + 2) == (int)CharacterCodes.dot)
                    {
                        pos += 3;
                        return token = SyntaxKind.DotDotDotToken;
                    }
                    pos++;
                    return token = SyntaxKind.DotToken;
                case (int)CharacterCodes.slash:
                    // Single-line comment
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.slash)
                    {
                        pos += 2;

                        // (Track O, O24: the first line break by SIMD, Scanner.Simd.cs.)
                        pos = FindLineBreak(pos);

                        // (Track O, O24: most comments are rejected by the first characters, MayBeCommentDirective.)
                        if (MayBeCommentDirective(tokenStart, pos))
                        {
                            commentDirectives = AppendIfCommentDirective(
                                commentDirectives,
                                tokenStart,
                                pos,
                                /*multiLine*/ false,
                                tokenStart);
                        }

                        if (skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            return token = SyntaxKind.SingleLineCommentTrivia;
                        }
                    }
                    // Multi-line comment
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.asterisk)
                    {
                        pos += 2;
                        var isJSDoc = CharCodeUnchecked(pos) == (int)CharacterCodes.asterisk && CharCodeUnchecked(pos + 1) != (int)CharacterCodes.slash;

                        var lastLineStart = tokenStart;
                        // Track O (O24): the loop over the comment's body (stop after "*/"; each line break sets
                        // PrecedingLineBreak and lastLineStart) by SIMD in two passes, Scanner.Simd.cs.
                        var commentClosed = ScanMultiLineCommentBody(ref lastLineStart);

                        if (isJSDoc && ShouldParseJSDoc())
                        {
                            tokenFlags |= TokenFlags.PrecedingJSDocComment;
                        }

                        commentDirectives = AppendIfCommentDirective(commentDirectives, lastLineStart, pos, /*multiLine*/ true, lastLineStart);

                        if (!commentClosed)
                        {
                            Error(Diagnostics.Asterisk_Slash_expected);
                        }

                        if (skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            if (!commentClosed)
                            {
                                tokenFlags |= TokenFlags.Unterminated;
                            }
                            return token = SyntaxKind.MultiLineCommentTrivia;
                        }
                    }

                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.equals)
                    {
                        pos += 2;
                        return token = SyntaxKind.SlashEqualsToken;
                    }

                    pos++;
                    return token = SyntaxKind.SlashToken;

                case (int)CharacterCodes._0:
                    if (pos + 2 < end && (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.X || CharCodeUnchecked(pos + 1) == (int)CharacterCodes.x))
                    {
                        pos += 2;
                        var hexDigitsStart = pos;
                        var hexDigitCount = ScanHexDigits(/*minCount*/ 1, /*scanAsManyAsPossible*/ true, /*canHaveSeparators*/ true, out var hexValue);
                        if (hexDigitCount == 0)
                        {
                            Error(Diagnostics.Hexadecimal_digit_expected);
                        }
                        tokenFlags |= TokenFlags.HexSpecifier;
                        // (TypeScript: `tokenValue = "0x" + hexDigits; return token = checkBigIntSuffix();`.)
                        return token = ScanHexLiteralValue(hexDigitsStart, hexValue);
                    }
                    else if (pos + 2 < end && (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.B || CharCodeUnchecked(pos + 1) == (int)CharacterCodes.b))
                    {
                        pos += 2;
                        tokenValue = ScanBinaryOrOctalDigits(/* base */ 2);
                        if (tokenValue.Length == 0)
                        {
                            Error(Diagnostics.Binary_digit_expected);
                            tokenValue = texts.Add("0");
                        }
                        tokenValue = texts.Concat("0b", tokenValue);
                        tokenFlags |= TokenFlags.BinarySpecifier;
                        return token = CheckBigIntSuffix();
                    }
                    else if (pos + 2 < end && (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.O || CharCodeUnchecked(pos + 1) == (int)CharacterCodes.o))
                    {
                        pos += 2;
                        tokenValue = ScanBinaryOrOctalDigits(/* base */ 8);
                        if (tokenValue.Length == 0)
                        {
                            Error(Diagnostics.Octal_digit_expected);
                            tokenValue = texts.Add("0");
                        }
                        tokenValue = texts.Concat("0o", tokenValue);
                        tokenFlags |= TokenFlags.OctalSpecifier;
                        return token = CheckBigIntSuffix();
                    }
                    goto case (int)CharacterCodes._1;
                case (int)CharacterCodes._1:
                case (int)CharacterCodes._2:
                case (int)CharacterCodes._3:
                case (int)CharacterCodes._4:
                case (int)CharacterCodes._5:
                case (int)CharacterCodes._6:
                case (int)CharacterCodes._7:
                case (int)CharacterCodes._8:
                case (int)CharacterCodes._9:
                    if (TryScanSimpleInteger())
                    {
                        return token = SyntaxKind.NumericLiteral;
                    }
                    return token = ScanNumber();
                case (int)CharacterCodes.colon:
                    pos++;
                    return token = SyntaxKind.ColonToken;
                case (int)CharacterCodes.semicolon:
                    pos++;
                    return token = SyntaxKind.SemicolonToken;
                case (int)CharacterCodes.lessThan:
                    if (IsConflictMarkerTrivia(text, pos))
                    {
                        pos = ScanConflictMarkerTrivia(text, pos, this);
                        if (skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            return token = SyntaxKind.ConflictMarkerTrivia;
                        }
                    }

                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.lessThan)
                    {
                        if (CharCodeUnchecked(pos + 2) == (int)CharacterCodes.equals)
                        {
                            pos += 3;
                            return token = SyntaxKind.LessThanLessThanEqualsToken;
                        }
                        pos += 2;
                        return token = SyntaxKind.LessThanLessThanToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.equals)
                    {
                        pos += 2;
                        return token = SyntaxKind.LessThanEqualsToken;
                    }
                    if (
                        languageVariant == LanguageVariant.JSX &&
                        CharCodeUnchecked(pos + 1) == (int)CharacterCodes.slash &&
                        CharCodeUnchecked(pos + 2) != (int)CharacterCodes.asterisk
                    )
                    {
                        pos += 2;
                        return token = SyntaxKind.LessThanSlashToken;
                    }
                    pos++;
                    return token = SyntaxKind.LessThanToken;
                case (int)CharacterCodes.equals:
                    if (IsConflictMarkerTrivia(text, pos))
                    {
                        pos = ScanConflictMarkerTrivia(text, pos, this);
                        if (skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            return token = SyntaxKind.ConflictMarkerTrivia;
                        }
                    }

                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.equals)
                    {
                        if (CharCodeUnchecked(pos + 2) == (int)CharacterCodes.equals)
                        {
                            pos += 3;
                            return token = SyntaxKind.EqualsEqualsEqualsToken;
                        }
                        pos += 2;
                        return token = SyntaxKind.EqualsEqualsToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.greaterThan)
                    {
                        pos += 2;
                        return token = SyntaxKind.EqualsGreaterThanToken;
                    }
                    pos++;
                    return token = SyntaxKind.EqualsToken;
                case (int)CharacterCodes.greaterThan:
                    if (IsConflictMarkerTrivia(text, pos))
                    {
                        pos = ScanConflictMarkerTrivia(text, pos, this);
                        if (skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            return token = SyntaxKind.ConflictMarkerTrivia;
                        }
                    }

                    pos++;
                    return token = SyntaxKind.GreaterThanToken;
                case (int)CharacterCodes.question:
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.dot && !IsDigit(CharCodeUnchecked(pos + 2)))
                    {
                        pos += 2;
                        return token = SyntaxKind.QuestionDotToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.question)
                    {
                        if (CharCodeUnchecked(pos + 2) == (int)CharacterCodes.equals)
                        {
                            pos += 3;
                            return token = SyntaxKind.QuestionQuestionEqualsToken;
                        }
                        pos += 2;
                        return token = SyntaxKind.QuestionQuestionToken;
                    }
                    pos++;
                    return token = SyntaxKind.QuestionToken;
                case (int)CharacterCodes.openBracket:
                    pos++;
                    return token = SyntaxKind.OpenBracketToken;
                case (int)CharacterCodes.closeBracket:
                    pos++;
                    return token = SyntaxKind.CloseBracketToken;
                case (int)CharacterCodes.caret:
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.equals)
                    {
                        pos += 2;
                        return token = SyntaxKind.CaretEqualsToken;
                    }
                    pos++;
                    return token = SyntaxKind.CaretToken;
                case (int)CharacterCodes.openBrace:
                    pos++;
                    return token = SyntaxKind.OpenBraceToken;
                case (int)CharacterCodes.bar:
                    if (IsConflictMarkerTrivia(text, pos))
                    {
                        pos = ScanConflictMarkerTrivia(text, pos, this);
                        if (skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            return token = SyntaxKind.ConflictMarkerTrivia;
                        }
                    }

                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.bar)
                    {
                        if (CharCodeUnchecked(pos + 2) == (int)CharacterCodes.equals)
                        {
                            pos += 3;
                            return token = SyntaxKind.BarBarEqualsToken;
                        }
                        pos += 2;
                        return token = SyntaxKind.BarBarToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.equals)
                    {
                        pos += 2;
                        return token = SyntaxKind.BarEqualsToken;
                    }
                    pos++;
                    return token = SyntaxKind.BarToken;
                case (int)CharacterCodes.closeBrace:
                    pos++;
                    return token = SyntaxKind.CloseBraceToken;
                case (int)CharacterCodes.tilde:
                    pos++;
                    return token = SyntaxKind.TildeToken;
                case (int)CharacterCodes.at:
                    pos++;
                    return token = SyntaxKind.AtToken;
                case (int)CharacterCodes.backslash:
                    {
                        var extendedCookedChar = PeekExtendedUnicodeEscape();
                        if (extendedCookedChar >= 0 && IsIdentifierStart(extendedCookedChar, languageVersion))
                        {
                            tokenValue = ConcatIdentifierParts(/*hashPrefix*/ false, -1);
                            return token = GetIdentifierToken();
                        }

                        var cookedChar = PeekUnicodeEscape();
                        if (cookedChar >= 0 && IsIdentifierStart(cookedChar, languageVersion))
                        {
                            pos += 6;
                            tokenFlags |= TokenFlags.UnicodeEscape;
                            tokenValue = ConcatIdentifierParts(/*hashPrefix*/ false, cookedChar);
                            return token = GetIdentifierToken();
                        }

                        Error(Diagnostics.Invalid_character);
                        pos++;
                        return token = SyntaxKind.Unknown;
                    }
                case (int)CharacterCodes.hash:
                    {
                        if (pos != 0 && CharCodeUnchecked(pos + 1) == '!')
                        {
                            Error(Diagnostics.can_only_be_used_at_the_start_of_a_file, pos, 2);
                            pos++;
                            return token = SyntaxKind.Unknown;
                        }

                        var charAfterHash = CodePointUnchecked(pos + 1);
                        if (charAfterHash == (int)CharacterCodes.backslash)
                        {
                            pos++;
                            var extendedCookedChar = PeekExtendedUnicodeEscape();
                            if (extendedCookedChar >= 0 && IsIdentifierStart(extendedCookedChar, languageVersion))
                            {
                                tokenValue = ConcatIdentifierParts(/*hashPrefix*/ true, -1);
                                return token = SyntaxKind.PrivateIdentifier;
                            }

                            var cookedChar = PeekUnicodeEscape();
                            if (cookedChar >= 0 && IsIdentifierStart(cookedChar, languageVersion))
                            {
                                pos += 6;
                                tokenFlags |= TokenFlags.UnicodeEscape;
                                tokenValue = ConcatIdentifierParts(/*hashPrefix*/ true, cookedChar);
                                return token = SyntaxKind.PrivateIdentifier;
                            }
                            pos--;
                        }

                        if (IsIdentifierStart(charAfterHash, languageVersion))
                        {
                            pos++;
                            // We're relying on scanIdentifier's behavior and adjusting the token kind after the fact.
                            // Notably absent from this block is the fact that calling a function named "scanIdentifier",
                            // but identifiers don't include '#', and that function doesn't deal with it at all.
                            // This works because 'scanIdentifier' tries to reuse source characters and builds up substrings;
                            // however, it starts at the 'tokenPos' which includes the '#', and will "accidentally" prepend the '#' for us.
                            ScanIdentifier(charAfterHash, languageVersion);
                        }
                        else
                        {
                            tokenValue = texts.Substring(pos, pos + 1); // TypeScript: `tokenValue = "#"` (the '#' at pos)
                            Error(Diagnostics.Invalid_character, pos++, CharSize(ch));
                        }
                        return token = SyntaxKind.PrivateIdentifier;
                    }
                case (int)CharacterCodes.replacementCharacter:
                    Error(Diagnostics.File_appears_to_be_binary, 0, 0);
                    pos = end;
                    return token = SyntaxKind.NonTextFileMarkerTrivia;
                default:
                    var identifierKind = ScanIdentifier(ch, languageVersion);
                    if (identifierKind != null)
                    {
                        return token = identifierKind.Value;
                    }
                    else if (IsWhiteSpaceSingleLine(ch))
                    {
                        pos += CharSize(ch);
                        continue;
                    }
                    else if (IsLineBreak(ch))
                    {
                        tokenFlags |= TokenFlags.PrecedingLineBreak;
                        pos += CharSize(ch);
                        continue;
                    }
                    var size = CharSize(ch);
                    Error(Diagnostics.Invalid_character, pos, size);
                    pos += size;
                    return token = SyntaxKind.Unknown;
            }
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool ShouldParseJSDoc()
    {
        switch (jsDocParsingMode)
        {
            case JSDocParsingMode.ParseAll:
                return true;
            case JSDocParsingMode.ParseNone:
                return false;
        }

        if (scriptKind != ScriptKind.TS && scriptKind != ScriptKind.TSX)
        {
            // If outside of TS, we need JSDoc to get any type info.
            return true;
        }

        if (jsDocParsingMode == JSDocParsingMode.ParseForTypeInfo)
        {
            // If we're in TS, but we don't need to produce reliable errors,
            // we don't need to parse to find @see or @link.
            return false;
        }

        // TypeScript: `jsDocSeeOrLink.test(text.slice(fullStartPos, pos))`.
        return JsDocSeeOrLinkTest(text, fullStartPos, Math.Min(pos, text.Length));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ReScanInvalidIdentifier()
    {
        Debug.Assert(token == SyntaxKind.Unknown, "'reScanInvalidIdentifier' should only be called when the current token is 'SyntaxKind.Unknown'.");
        pos = tokenStart = fullStartPos;
        tokenFlags = 0;
        var ch = CodePointUnchecked(pos);
        var identifierKind = ScanIdentifier(ch, ScriptTarget.ESNext);
        if (identifierKind != null)
        {
            return token = identifierKind.Value;
        }
        pos += CharSize(ch);
        return token; // Still `SyntaxKind.Unknown`
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind? ScanIdentifier(int startCharacter, ScriptTarget languageVersion)
    {
        var ch = startCharacter;
        if (HasAsciiFlag(ch, CharFlags.IdentifierStart))
        {
            // Track O (O23-B): isIdentifierStart of an ASCII character by the table.
            pos++;
            return ScanIdentifierAfterAsciiStart(languageVersion);
        }
        if (ch > (int)CharacterCodes.maxAsciiCharacter && IsUnicodeIdentifierStart(ch, languageVersion))
        {
            pos += CharSize(ch);
            return ScanIdentifierRest(languageVersion);
        }
        return null;
    }

    /// <summary>
    /// The rest of <see cref="ScanIdentifier"/> after an ASCII first character (pos after it). Fast path for simple ASCII
    /// identifiers, as typescript-go's scanIdentifier (internal/scanner/scanner.go): when the identifier consists of ASCII
    /// identifier characters and ends at the end or at an ASCII character other than '\', the loop of scanIdentifier
    /// would stop at the same position and take the same branch (Track O, O23-B: the run of ASCII identifier parts by the
    /// table). Otherwise scanIdentifier's loop continues where the run ends, as it would after the same characters.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ScanIdentifierAfterAsciiStart(ScriptTarget languageVersion)
    {
        var asciiEnd = SkipAsciiIdentifierParts(pos);
        pos = asciiEnd;
        if (asciiEnd < end)
        {
            int next = text[asciiEnd];
            if (next > (int)CharacterCodes.maxAsciiCharacter || next == (int)CharacterCodes.backslash)
            {
                return ScanIdentifierRest(languageVersion);
            }
        }
        return GetIdentifierTokenOfRange(tokenStart, asciiEnd);
    }

    /// <summary>The loop of scanIdentifier after the first character, and what follows it (pos after the first character).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ScanIdentifierRest(ScriptTarget languageVersion)
    {
        // (The loop leaves the first character in `ch` when it does not run; that character is never '\'.)
        var ch = 0;
        while (pos < end && IsIdentifierPart(ch = CodePointUnchecked(pos), languageVersion))
        {
            pos += CharSize(ch);
        }
        if (ch == (int)CharacterCodes.backslash)
        {
            // TypeScript: `tokenValue = text.substring(tokenStart, pos); tokenValue += scanIdentifierParts();`.
            var value = new TextBuilder(texts);
            value.AppendSource(tokenStart, pos);
            ScanIdentifierParts(ref value);
            tokenValue = value.Result();
            return GetIdentifierToken();
        }
        // TypeScript: `tokenValue = text.substring(tokenStart, pos); return getIdentifierToken();`.
        // typescript-go takes the value as a slice of the text, which copies nothing
        // (`s.tokenValue = s.text[start:s.pos]`, scanIdentifier in internal/scanner/scanner.go); here the
        // value is the span of the text (Track O, O5a) and the keyword lookup of getIdentifierToken runs on it.
        return GetIdentifierTokenOfRange(tokenStart, pos);
    }

    // The characters scanTemplateAndSetTokenValue tests for: '`', '$', '\', CR.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsTemplateSpecialCharacter(int ch)
    {
        return ch == (int)CharacterCodes.backtick || ch == (int)CharacterCodes.Dollar || ch == (int)CharacterCodes.backslash || ch == (int)CharacterCodes.carriageReturn;
    }

    // ' ', '\t', '\n', '\v', '\f', '\r' (typescript-go: `b == ' ' || (b >= '\t' && b <= '\r')`).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsAsciiWhiteSpaceOrLineBreak(int ch)
    {
        return ch == (int)CharacterCodes.space || (ch >= (int)CharacterCodes.tab && ch <= (int)CharacterCodes.carriageReturn);
    }


    // getIdentifierToken for tokenValue = text[start..end), see ScanIdentifier.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind GetIdentifierTokenOfRange(int start, int end)
    {
        // Reserved words are between 2 and 12 characters long and start with a lowercase letter
        var len = end - start;
        tokenValue = TextRef.Source(start, len);
        if (len >= 2 && len <= 12)
        {
            int ch = text[start];
            if (ch >= (int)CharacterCodes.a && ch <= (int)CharacterCodes.z)
            {
                // (Track O, O24: the keyword's characters compared by SIMD, Scanner.Simd.cs.)
                var keyword = MatchKeyword(start, len);
                if (keyword != SyntaxKind.Unknown)
                {
                    return token = keyword;
                }
            }
        }
        return token = SyntaxKind.Identifier;
    }

    /// <summary>
    /// Track O (O5a): `prefix + escaped + scanIdentifierParts()` for identifiers that start with an escape: the extended
    /// escape at pos (`scanExtendedUnicodeEscape(true)`, scanned here) when <paramref name="cookedChar"/> is -1, else
    /// `String.fromCharCode(cookedChar)` of the `\uXXXX` escape the caller skipped; the prefix is "#" or nothing.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private TextRef ConcatIdentifierParts(bool hashPrefix, int cookedChar)
    {
        var value = new TextBuilder(texts);
        if (hashPrefix)
        {
            value.Append('#');
        }
        if (cookedChar < 0)
        {
            ScanExtendedUnicodeEscape(/*shouldEmitInvalidEscapeError*/ true, ref value);
        }
        else
        {
            value.Append((char)(cookedChar & 0xFFFF));
        }
        ScanIdentifierParts(ref value);
        return value.Result();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ReScanGreaterToken()
    {
        if (token == SyntaxKind.GreaterThanToken)
        {
            if (CharCodeUnchecked(pos) == (int)CharacterCodes.greaterThan)
            {
                if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.greaterThan)
                {
                    if (CharCodeUnchecked(pos + 2) == (int)CharacterCodes.equals)
                    {
                        pos += 3;
                        return token = SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken;
                    }
                    pos += 2;
                    return token = SyntaxKind.GreaterThanGreaterThanGreaterThanToken;
                }
                if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.equals)
                {
                    pos += 2;
                    return token = SyntaxKind.GreaterThanGreaterThanEqualsToken;
                }
                pos++;
                return token = SyntaxKind.GreaterThanGreaterThanToken;
            }
            if (CharCodeUnchecked(pos) == (int)CharacterCodes.equals)
            {
                pos++;
                return token = SyntaxKind.GreaterThanEqualsToken;
            }
        }
        return token;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ReScanAsteriskEqualsToken()
    {
        Debug.Assert(token == SyntaxKind.AsteriskEqualsToken, "'reScanAsteriskEqualsToken' should only be called on a '*='");
        pos = tokenStart + 1;
        return token = SyntaxKind.EqualsToken;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ReScanSlashToken(bool reportErrors = false)
    {
        if (token == SyntaxKind.SlashToken || token == SyntaxKind.SlashEqualsToken)
        {
            // Quickly get to the end of regex such that we know the flags
            var startOfRegExpBody = tokenStart + 1;
            pos = startOfRegExpBody;
            var inEscape = false;
            var namedCaptureGroups = false;
            // Although nested character classes are allowed in Unicode Sets mode,
            // an unescaped slash is nevertheless invalid even in a character class in any Unicode mode.
            // This is indicated by Section 12.9.5 Regular Expression Literals of the specification,
            // where nested character classes are not considered at all. (A `[` RegularExpressionClassChar
            // does nothing in a RegularExpressionClass, and a `]` always closes the class.)
            // Additionally, parsing nested character classes will misinterpret regexes like `/[[]/`
            // as unterminated, consuming characters beyond the slash. (This even applies to `/[[]/v`,
            // which should be parsed as a well-terminated regex with an incomplete character class.)
            // Thus we must not handle nested character classes in the first pass.
            var inCharacterClass = false;
            while (true)
            {
                // If we reach the end of a file, or hit a newline, then this is an unterminated
                // regex.  Report error and return what we have so far.
                var ch = CharCodeChecked(pos);
                if (ch == (int)CharacterCodes.EOF || IsLineBreak(ch))
                {
                    tokenFlags |= TokenFlags.Unterminated;
                    break;
                }

                if (inEscape)
                {
                    // Parsing an escape character;
                    // reset the flag and just advance to the next char.
                    inEscape = false;
                }
                else if (ch == (int)CharacterCodes.slash && !inCharacterClass)
                {
                    // A slash within a character class is permissible,
                    // but in general it signals the end of the regexp literal.
                    break;
                }
                else if (ch == (int)CharacterCodes.openBracket)
                {
                    inCharacterClass = true;
                }
                else if (ch == (int)CharacterCodes.backslash)
                {
                    inEscape = true;
                }
                else if (ch == (int)CharacterCodes.closeBracket)
                {
                    inCharacterClass = false;
                }
                else if (
                    !inCharacterClass
                    && ch == (int)CharacterCodes.openParen
                    && CharCodeChecked(pos + 1) == (int)CharacterCodes.question
                    && CharCodeChecked(pos + 2) == (int)CharacterCodes.lessThan
                    && CharCodeChecked(pos + 3) != (int)CharacterCodes.equals
                    && CharCodeChecked(pos + 3) != (int)CharacterCodes.exclamation
                )
                {
                    namedCaptureGroups = true;
                }
                pos++;
            }
            var endOfRegExpBody = pos;
            if ((tokenFlags & TokenFlags.Unterminated) != 0)
            {
                // Search for the nearest unbalanced bracket for better recovery. Since the expression is
                // invalid anyways, we take nested square brackets into consideration for the best guess.
                pos = startOfRegExpBody;
                inEscape = false;
                var characterClassDepth = 0;
                var inDecimalQuantifier = false;
                var groupDepth = 0;
                while (pos < endOfRegExpBody)
                {
                    var ch = CharCodeUnchecked(pos);
                    if (inEscape)
                    {
                        inEscape = false;
                    }
                    else if (ch == (int)CharacterCodes.backslash)
                    {
                        inEscape = true;
                    }
                    else if (ch == (int)CharacterCodes.openBracket)
                    {
                        characterClassDepth++;
                    }
                    else if (ch == (int)CharacterCodes.closeBracket && characterClassDepth != 0)
                    {
                        characterClassDepth--;
                    }
                    else if (characterClassDepth == 0)
                    {
                        if (ch == (int)CharacterCodes.openBrace)
                        {
                            inDecimalQuantifier = true;
                        }
                        else if (ch == (int)CharacterCodes.closeBrace && inDecimalQuantifier)
                        {
                            inDecimalQuantifier = false;
                        }
                        else if (!inDecimalQuantifier)
                        {
                            if (ch == (int)CharacterCodes.openParen)
                            {
                                groupDepth++;
                            }
                            else if (ch == (int)CharacterCodes.closeParen && groupDepth != 0)
                            {
                                groupDepth--;
                            }
                            else if (ch == (int)CharacterCodes.closeParen || ch == (int)CharacterCodes.closeBracket || ch == (int)CharacterCodes.closeBrace)
                            {
                                // We encountered an unbalanced bracket outside a character class. Treat this position as the end of regex.
                                break;
                            }
                        }
                    }
                    pos++;
                }
                // Whitespaces and semicolons at the end are not likely to be part of the regex
                while (IsWhiteSpaceLike(CharCodeChecked(pos - 1)) || CharCodeChecked(pos - 1) == (int)CharacterCodes.semicolon)
                {
                    pos--;
                }
                Error(Diagnostics.Unterminated_regular_expression_literal, tokenStart, pos - tokenStart);
            }
            else
            {
                // Consume the slash character
                pos++;
                var regExpFlags = RegularExpressionFlags.None;
                while (true)
                {
                    var ch = CodePointChecked(pos);
                    if (ch == (int)CharacterCodes.EOF || !IsIdentifierPart(ch, languageVersion))
                    {
                        break;
                    }
                    var size = CharSize(ch);
                    if (reportErrors)
                    {
                        var flag = CharacterCodeToRegularExpressionFlag(ch);
                        if (flag == null)
                        {
                            Error(Diagnostics.Unknown_regular_expression_flag, pos, size);
                        }
                        else if ((regExpFlags & flag.Value) != 0)
                        {
                            Error(Diagnostics.Duplicate_regular_expression_flag, pos, size);
                        }
                        else if (((regExpFlags | flag.Value) & RegularExpressionFlags.AnyUnicodeMode) == RegularExpressionFlags.AnyUnicodeMode)
                        {
                            Error(Diagnostics.The_Unicode_u_flag_and_the_Unicode_Sets_v_flag_cannot_be_set_simultaneously, pos, size);
                        }
                        else
                        {
                            regExpFlags |= flag.Value;
                            CheckRegularExpressionFlagAvailability(flag.Value, size);
                        }
                    }
                    pos += size;
                }
                if (reportErrors)
                {
                    CheckRegularExpressionBody(startOfRegExpBody, endOfRegExpBody, regExpFlags, namedCaptureGroups);
                }
            }
            tokenValue = texts.Substring(tokenStart, pos);
            token = SyntaxKind.RegularExpressionLiteral;
        }
        return token;
    }

    // `scanRange(startOfRegExpBody, endOfRegExpBody - startOfRegExpBody, () => { scanRegularExpressionWorker(...); })`.
    [NoAllocUnchecked("regex checking: the regular expression checker builds strings and lists")]
    private void CheckRegularExpressionBody(int startOfRegExpBody, int endOfRegExpBody, RegularExpressionFlags regExpFlags, bool namedCaptureGroups)
    {
        ScanRange<object?>(startOfRegExpBody, endOfRegExpBody - startOfRegExpBody, () =>
        {
            ScanRegularExpressionWorker(regExpFlags, /*annexB*/ true, namedCaptureGroups);
            return null;
        });
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void CheckRegularExpressionFlagAvailability(RegularExpressionFlags flag, int size)
    {
        if (RegExpFlagToFirstAvailableLanguageVersion.TryGetValue(flag, out var availableFrom) && availableFrom != 0 && languageVersion < availableFrom)
        {
            ReportFlagNotAvailable(availableFrom, size);
        }
    }

    [NoAllocUnchecked("error path: the diagnostic names the script target")]
    private void ReportFlagNotAvailable(ScriptTarget availableFrom, int size)
    {
        Error(Diagnostics.This_regular_expression_flag_is_only_available_when_targeting_0_or_later, pos, size, Utilities.GetNameOfScriptTarget(availableFrom));
    }

    // TypeScript passes the comment's text (`text.slice(start, end)`) and a RegExp; this passes the range of
    // the text instead of slicing it, as typescript-go's processCommentDirective (internal/scanner/scanner.go)
    // works on positions, and the hand-written matcher of the RegExp.
    // (Track O, O-NA: the regex is one of the two matchers, chosen by a flag instead of passed as a delegate.)
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private List<CommentDirective>? AppendIfCommentDirective(
        List<CommentDirective>? commentDirectives,
        int textStart,
        int textEnd,
        bool multiLine,
        int lineStart)
    {
        // `text.trimStart()`
        textEnd = Math.Min(textEnd, text.Length);
        while (textStart < textEnd && IsRegExpWhiteSpace(text[textStart]))
        {
            textStart++;
        }
        if (!TryGetDirectiveFromComment(textStart, textEnd, multiLine, out var type))
        {
            return commentDirectives;
        }

        return AppendCommentDirective(commentDirectives, lineStart, type);
    }

    [NoAllocUnchecked("per-file result: a comment directive of the SourceFile")]
    private List<CommentDirective> AppendCommentDirective(List<CommentDirective>? commentDirectives, int lineStart, CommentDirectiveType type)
    {
        return Core.Append(
            commentDirectives,
            new CommentDirective
            {
                Range = new ObjectTextRange { Pos = lineStart, End = pos },
                Type = type,
            });
    }

    // getDirectiveFromComment (Track O: false where TypeScript returns undefined).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool TryGetDirectiveFromComment(int textStart, int textEnd, bool multiLine, out CommentDirectiveType type)
    {
        var match = multiLine ? CommentDirectiveRegExMultiLine(text, textStart, textEnd) : CommentDirectiveRegExSingleLine(text, textStart, textEnd);
        type = default;
        if (match == null)
        {
            return false;
        }

        switch (match)
        {
            case "ts-expect-error":
                type = CommentDirectiveType.ExpectError;
                return true;

            case "ts-ignore":
                type = CommentDirectiveType.Ignore;
                return true;
        }

        return false;
    }

    /// <summary>Unconditionally back up and scan a template expression portion.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ReScanTemplateToken(bool isTaggedTemplate)
    {
        pos = tokenStart;
        return token = ScanTemplateAndSetTokenValue(!isTaggedTemplate);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ReScanTemplateHeadOrNoSubstitutionTemplate()
    {
        pos = tokenStart;
        return token = ScanTemplateAndSetTokenValue(/*shouldEmitInvalidEscapeError*/ true);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ReScanJsxToken(bool allowMultilineJsxText = true)
    {
        pos = tokenStart = fullStartPos;
        return token = ScanJsxToken(allowMultilineJsxText);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ReScanLessThanToken()
    {
        if (token == SyntaxKind.LessThanLessThanToken)
        {
            pos = tokenStart + 1;
            return token = SyntaxKind.LessThanToken;
        }
        return token;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ReScanHashToken()
    {
        if (token == SyntaxKind.PrivateIdentifier)
        {
            pos = tokenStart + 1;
            return token = SyntaxKind.HashToken;
        }
        return token;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ReScanQuestionToken()
    {
        Debug.Assert(token == SyntaxKind.QuestionQuestionToken, "'reScanQuestionToken' should only be called on a '??'");
        pos = tokenStart + 1;
        return token = SyntaxKind.QuestionToken;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ScanJsxToken(bool allowMultilineJsxText = true)
    {
        fullStartPos = tokenStart = pos;

        if (pos >= end)
        {
            return token = SyntaxKind.EndOfFileToken;
        }

        // TypeScript names this local `char`.
        var chr = CharCodeUnchecked(pos);
        if (chr == (int)CharacterCodes.lessThan)
        {
            if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.slash)
            {
                pos += 2;
                return token = SyntaxKind.LessThanSlashToken;
            }
            pos++;
            return token = SyntaxKind.LessThanToken;
        }

        if (chr == (int)CharacterCodes.openBrace)
        {
            pos++;
            return token = SyntaxKind.OpenBraceToken;
        }

        // First non-whitespace character on this line.
        var firstNonWhitespace = 0;

        // These initial values are special because the first line is:
        // firstNonWhitespace = 0 to indicate that we want leading whitespace,

        while (pos < end)
        {
            chr = CharCodeUnchecked(pos);
            if (chr == (int)CharacterCodes.openBrace)
            {
                break;
            }
            if (chr == (int)CharacterCodes.lessThan)
            {
                if (IsConflictMarkerTrivia(text, pos))
                {
                    pos = ScanConflictMarkerTrivia(text, pos, this);
                    return token = SyntaxKind.ConflictMarkerTrivia;
                }
                break;
            }
            if (chr == (int)CharacterCodes.greaterThan)
            {
                Error(Diagnostics.Unexpected_token_Did_you_mean_or_gt, pos, 1);
            }
            if (chr == (int)CharacterCodes.closeBrace)
            {
                Error(Diagnostics.Unexpected_token_Did_you_mean_or_rbrace, pos, 1);
            }

            // FirstNonWhitespace is 0, then we only see whitespaces so far. If we see a linebreak, we want to ignore that whitespaces.
            // i.e (- : whitespace)
            //      <div>----
            //      </div> becomes <div></div>
            //
            //      <div>----</div> becomes <div>----</div>
            if (IsLineBreak(chr) && firstNonWhitespace == 0)
            {
                firstNonWhitespace = -1;
            }
            else if (!allowMultilineJsxText && IsLineBreak(chr) && firstNonWhitespace > 0)
            {
                // Stop JsxText on each line during formatting. This allows the formatter to
                // indent each line correctly.
                break;
            }
            else if (!IsWhiteSpaceLike(chr))
            {
                firstNonWhitespace = pos;
            }

            pos++;
        }

        tokenValue = texts.Substring(fullStartPos, pos);

        return firstNonWhitespace == -1 ? SyntaxKind.JsxTextAllWhiteSpaces : SyntaxKind.JsxText;
    }

    // Scans a JSX identifier; these differ from normal identifiers in that
    // they allow dashes
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ScanJsxIdentifier()
    {
        if (TokenIsIdentifierOrKeyword(token))
        {
            // An identifier or keyword has already been parsed - check for a `-` or a single instance of `:` and then append it and
            // everything after it to the token
            // Do note that this means that `scanJsxIdentifier` effectively _mutates_ the visible token without advancing to a new token
            // Any caller should be expecting this behavior and should only read the pos or token value after calling it.
            // (Track O: `tokenValue += ...` on a builder that starts with the value; a "-" is the one of the text.)
            var value = new TextBuilder(texts);
            value.Append(tokenValue);
            while (pos < end)
            {
                var ch = CharCodeUnchecked(pos);
                if (ch == (int)CharacterCodes.minus)
                {
                    value.AppendSource(pos, pos + 1); // TypeScript: `tokenValue += "-"`
                    pos++;
                    continue;
                }
                var oldPos = pos;
                ScanIdentifierParts(ref value); // reuse `scanIdentifierParts` so unicode escapes are handled
                if (pos == oldPos)
                {
                    break;
                }
            }
            tokenValue = value.Result();
            return GetIdentifierToken();
        }
        return token;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ScanJsxAttributeValue()
    {
        fullStartPos = pos;

        switch (CharCodeUnchecked(pos))
        {
            case (int)CharacterCodes.doubleQuote:
            case (int)CharacterCodes.singleQuote:
                tokenValue = ScanString(/*jsxAttributeString*/ true);
                return token = SyntaxKind.StringLiteral;
            default:
                // If this scans anything other than `{`, it's a parse error.
                return Scan();
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ReScanJsxAttributeValue()
    {
        pos = tokenStart = fullStartPos;
        return ScanJsxAttributeValue();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ScanJSDocCommentTextToken(bool inBackticks)
    {
        fullStartPos = tokenStart = pos;
        tokenFlags = TokenFlags.None;
        if (pos >= end)
        {
            return token = SyntaxKind.EndOfFileToken;
        }
        for (var ch = CharCodeUnchecked(pos); pos < end && (!IsLineBreak(ch) && ch != (int)CharacterCodes.backtick); ch = CodePointUnchecked(++pos))
        {
            if (!inBackticks)
            {
                if (ch == (int)CharacterCodes.openBrace)
                {
                    break;
                }
                else if (
                    ch == (int)CharacterCodes.at
                    && pos - 1 >= 0 && IsWhiteSpaceSingleLine(CharCodeUnchecked(pos - 1))
                    && !(pos + 1 < end && IsWhiteSpaceLike(CharCodeUnchecked(pos + 1)))
                )
                {
                    // @ doesn't start a new tag inside ``, and elsewhere, only after whitespace and before non-whitespace
                    break;
                }
            }
        }
        if (pos == tokenStart)
        {
            return ScanJsDocToken();
        }
        tokenValue = texts.Substring(tokenStart, pos);
        return token = SyntaxKind.JSDocCommentTextToken;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SyntaxKind ScanJsDocToken()
    {
        fullStartPos = tokenStart = pos;
        tokenFlags = TokenFlags.None;
        if (pos >= end)
        {
            return token = SyntaxKind.EndOfFileToken;
        }

        var ch = CodePointUnchecked(pos);
        pos += CharSize(ch);
        switch (ch)
        {
            case (int)CharacterCodes.tab:
            case (int)CharacterCodes.verticalTab:
            case (int)CharacterCodes.formFeed:
            case (int)CharacterCodes.space:
                while (pos < end && IsWhiteSpaceSingleLine(CharCodeUnchecked(pos)))
                {
                    pos++;
                }
                return token = SyntaxKind.WhitespaceTrivia;
            case (int)CharacterCodes.at:
                return token = SyntaxKind.AtToken;
            case (int)CharacterCodes.carriageReturn:
                if (CharCodeUnchecked(pos) == (int)CharacterCodes.lineFeed)
                {
                    pos++;
                }
                goto case (int)CharacterCodes.lineFeed;
            case (int)CharacterCodes.lineFeed:
                tokenFlags |= TokenFlags.PrecedingLineBreak;
                return token = SyntaxKind.NewLineTrivia;
            case (int)CharacterCodes.asterisk:
                return token = SyntaxKind.AsteriskToken;
            case (int)CharacterCodes.openBrace:
                return token = SyntaxKind.OpenBraceToken;
            case (int)CharacterCodes.closeBrace:
                return token = SyntaxKind.CloseBraceToken;
            case (int)CharacterCodes.openBracket:
                return token = SyntaxKind.OpenBracketToken;
            case (int)CharacterCodes.closeBracket:
                return token = SyntaxKind.CloseBracketToken;
            case (int)CharacterCodes.openParen:
                return token = SyntaxKind.OpenParenToken;
            case (int)CharacterCodes.closeParen:
                return token = SyntaxKind.CloseParenToken;
            case (int)CharacterCodes.lessThan:
                return token = SyntaxKind.LessThanToken;
            case (int)CharacterCodes.greaterThan:
                return token = SyntaxKind.GreaterThanToken;
            case (int)CharacterCodes.equals:
                return token = SyntaxKind.EqualsToken;
            case (int)CharacterCodes.comma:
                return token = SyntaxKind.CommaToken;
            case (int)CharacterCodes.dot:
                return token = SyntaxKind.DotToken;
            case (int)CharacterCodes.backtick:
                return token = SyntaxKind.BacktickToken;
            case (int)CharacterCodes.hash:
                return token = SyntaxKind.HashToken;
            case (int)CharacterCodes.backslash:
                pos--;
                var extendedCookedChar = PeekExtendedUnicodeEscape();
                if (extendedCookedChar >= 0 && IsIdentifierStart(extendedCookedChar, languageVersion))
                {
                    tokenValue = ConcatIdentifierParts(/*hashPrefix*/ false, -1);
                    return token = GetIdentifierToken();
                }

                var cookedChar = PeekUnicodeEscape();
                if (cookedChar >= 0 && IsIdentifierStart(cookedChar, languageVersion))
                {
                    pos += 6;
                    tokenFlags |= TokenFlags.UnicodeEscape;
                    tokenValue = ConcatIdentifierParts(/*hashPrefix*/ false, cookedChar);
                    return token = GetIdentifierToken();
                }
                pos++;
                return token = SyntaxKind.Unknown;
        }

        if (IsIdentifierStart(ch, languageVersion))
        {
            // TypeScript names this local `char`.
            var chr = ch;
            // Ported as it is (pre-registration §5.3): `&&` binds tighter than `||`, so a '-' as the last
            // character seen keeps the loop going after `pos < end` fails.
            while ((pos < end && IsIdentifierPart(chr = CodePointUnchecked(pos), languageVersion)) || chr == (int)CharacterCodes.minus)
            {
                pos += CharSize(chr);
            }
            if (chr == (int)CharacterCodes.backslash)
            {
                var value = new TextBuilder(texts);
                value.AppendSource(tokenStart, pos);
                ScanIdentifierParts(ref value);
                tokenValue = value.Result();
            }
            else
            {
                tokenValue = texts.Substring(tokenStart, pos);
            }
            return token = GetIdentifierToken();
        }
        else
        {
            return token = SyntaxKind.Unknown;
        }
    }

    // Track O (O-NA): the callback is a struct (IScannerCallback), not a delegate.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private T SpeculationHelper<TCallback, T>(TCallback callback, bool isLookahead)
        where TCallback : struct, IScannerCallback<T>
    {
        var savePos = pos;
        var saveStartPos = fullStartPos;
        var saveTokenPos = tokenStart;
        var saveToken = token;
        var saveTokenValue = tokenValue;
        var saveTokenFlags = tokenFlags;
        var result = callback.Invoke();

        // If our callback returned something 'falsy' or we're just looking ahead,
        // then unconditionally restore us to where we were.
        if (!Core.IsTruthyValue(result) || isLookahead)
        {
            pos = savePos;
            fullStartPos = saveStartPos;
            tokenStart = saveTokenPos;
            token = saveToken;
            tokenValue = saveTokenValue;
            tokenFlags = saveTokenFlags;
        }
        return result;
    }

    /// <summary>scanRange with a callback struct (Track O, O-NA: no delegate); see the overload below.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal T ScanRange<TCallback, T>(int start, int length, TCallback callback)
        where TCallback : struct, IScannerCallback<T>
    {
        var saveEnd = end;
        var savePos = pos;
        var saveStartPos = fullStartPos;
        var saveTokenPos = tokenStart;
        var saveToken = token;
        var saveTokenValue = tokenValue;
        var saveTokenFlags = tokenFlags;
        var saveErrorExpectations = commentDirectives;

        // (TypeScript: `setText(text, start, length)`; Track O: the same text, so the texts of the parse stay.)
        end = start + length;
        ResetTokenState(start);
        var result = callback.Invoke();

        end = saveEnd;
        pos = savePos;
        fullStartPos = saveStartPos;
        tokenStart = saveTokenPos;
        token = saveToken;
        tokenValue = saveTokenValue;
        tokenFlags = saveTokenFlags;
        commentDirectives = saveErrorExpectations;

        return result;
    }

    internal T ScanRange<T>(int start, int length, Func<T> callback)
    {
        var saveEnd = end;
        var savePos = pos;
        var saveStartPos = fullStartPos;
        var saveTokenPos = tokenStart;
        var saveToken = token;
        var saveTokenValue = tokenValue;
        var saveTokenFlags = tokenFlags;
        var saveErrorExpectations = commentDirectives;

        // (TypeScript: `setText(text, start, length)`; Track O: the same text, so the texts of the parse stay.)
        end = start + length;
        ResetTokenState(start);
        var result = callback();

        end = saveEnd;
        pos = savePos;
        fullStartPos = saveStartPos;
        tokenStart = saveTokenPos;
        token = saveToken;
        tokenValue = saveTokenValue;
        tokenFlags = saveTokenFlags;
        commentDirectives = saveErrorExpectations;

        return result;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal T LookAhead<TCallback, T>(TCallback callback)
        where TCallback : struct, IScannerCallback<T>
    {
        return SpeculationHelper<TCallback, T>(callback, /*isLookahead*/ true);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal T TryScan<TCallback, T>(TCallback callback)
        where TCallback : struct, IScannerCallback<T>
    {
        return SpeculationHelper<TCallback, T>(callback, /*isLookahead*/ false);
    }

    /// <summary>The text as a string (Track O, step O1: materialized from the native text; nothing in the parse calls it).</summary>
    internal string GetText()
    {
        return textOwner.GetString();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void ClearCommentDirectives()
    {
        commentDirectives = null;
    }

    /// <summary>setText for a string (pinned while the scanner reads it; Track O's parse passes its <see cref="NativeText"/>).</summary>
    internal void SetText(string? newText, int? start = null, int? length = null)
    {
        SetText(NativeText.FromString(newText), start, length);
    }

    /// <summary>Track O (step O1): setText for the text of a parse (native memory the parse result owns).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetText(NativeText newText, int? start = null, int? length = null)
    {
        textOwner = newText;
        text = newText.Chars;
        end = length == null ? text.Length : start!.Value + length.Value;
        // Track O (O5a): token values go to the scanner's own texts until a parse attaches its texts (UseTexts).
        texts = ownTexts;
        ownTexts.Reset(newText);
        ResetTokenState(start ?? 0);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetOnError(ErrorCallback? errorCallback)
    {
        onError = errorCallback;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetScriptTarget(ScriptTarget scriptTarget)
    {
        languageVersion = scriptTarget;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetLanguageVariant(LanguageVariant variant)
    {
        languageVariant = variant;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetScriptKind(ScriptKind kind)
    {
        scriptKind = kind;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetJSDocParsingMode(JSDocParsingMode kind)
    {
        jsDocParsingMode = kind;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void ResetTokenState(int position)
    {
        Debug.Assert(position >= 0);
        pos = position;
        fullStartPos = position;
        tokenStart = position;
        token = SyntaxKind.Unknown;
        tokenValue = default;
        tokenFlags = TokenFlags.None;
    }

    /// <summary>scanner.ts: `setTextPos: resetTokenState` (deprecated alias).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetTextPos(int textPos)
    {
        ResetTokenState(textPos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal void SetSkipJsDocLeadingAsterisks(bool skip)
    {
        skipJsDocLeadingAsterisks += skip ? 1 : -1;
    }
}
