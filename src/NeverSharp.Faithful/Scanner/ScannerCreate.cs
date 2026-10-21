// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/scanner.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/scanner/scanner.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Text;
using NeverSharp.Generated;

// See Scanner.cs: the alias keeps the generated `Diagnostics` class reachable where a root namespace `Diagnostics` exists.
using DiagnosticMessages = NeverSharp.Generated.Diagnostics;

namespace NeverSharp.Faithful;

// scanner.ts: createScanner. The closure state of createScanner is the fields below, its inner functions are the
// instance methods; the object it returns (TypeScript's `interface Scanner`) is this instance.
internal sealed partial class Scanner
{
    // scanner.ts: const enum EscapeSequenceScanningFlags.
    [Flags]
    private enum EscapeSequenceScanningFlags
    {
        None = 0,
        String = 1 << 0,
        ReportErrors = 1 << 1,

        RegularExpression = 1 << 2,
        AnnexB = 1 << 3,
        AnyUnicodeMode = 1 << 4,
        AtomEscape = 1 << 5,

        ReportInvalidEscapeErrors = RegularExpression | ReportErrors,
        AllowExtendedUnicodeEscape = String | AnyUnicodeMode,
    }

    // scanner.ts: const enum ClassSetExpressionType.
    private enum ClassSetExpressionType
    {
        Unknown,
        ClassUnion,
        ClassIntersection,
        ClassSubtraction,
    }

    private ScriptTarget languageVersion;
    private readonly bool skipTrivia;
    private LanguageVariant languageVariant;
    private ErrorCallback? onError;

    private string text;

    // Current position (end position of text of current token)
    private int pos;

    // end of text
    private int end;

    // Start position of whitespace before current token
    private int fullStartPos;

    // Start position of text of current token
    private int tokenStart;

    private SyntaxKind token;
    private string tokenValue = null!;
    private TokenFlags tokenFlags;

    private List<CommentDirective>? commentDirectives;
    private int skipJsDocLeadingAsterisks;

    private ScriptKind scriptKind = ScriptKind.Unknown;
    private JSDocParsingMode jsDocParsingMode = JSDocParsingMode.ParseAll;

    // Accumulators for `result += piece` loops (scanString, scanTemplateAndSetTokenValue, scanHexDigits). JavaScript engines make
    // repeated string concatenation cheap; C# strings do not, so the pieces are collected in a builder, as internal/scanner/scanner.go
    // does (strings.Builder in scanString, scanNumberFragment and the hexadecimal digit scanner; a list of parts joined at the end
    // in scanTemplateAndSetTokenValue).
    private readonly StringBuilder pieceScratch = new();
    private readonly StringBuilder hexScratch = new();

    /// <summary>
    /// `result += piece` over the pieces of a literal. A value made of a single piece (a string or template without
    /// escape sequences) is that piece's substring; only a second piece makes the shared builder collect them.
    /// </summary>
    private struct PieceAccumulator(Scanner scanner)
    {
        private string? single;
        private StringBuilder? builder;

        /// <summary>`result += text.substring(start, stop)`.</summary>
        internal void AppendText(int start, int stop)
        {
            if (builder == null)
            {
                if (single == null)
                {
                    single = Substring(scanner.text, start, stop);
                    return;
                }
                builder = scanner.pieceScratch.Clear().Append(single);
            }
            var length = scanner.text.Length;
            start = Math.Min(Math.Max(start, 0), length);
            stop = Math.Min(Math.Max(stop, 0), length);
            if (start <= stop)
            {
                builder.Append(scanner.text, start, stop - start);
            }
            else
            {
                builder.Append(scanner.text, stop, start - stop);
            }
        }

        /// <summary>`result += piece`.</summary>
        internal void Append(string piece)
        {
            if (builder == null)
            {
                if (single == null)
                {
                    single = piece;
                    return;
                }
                builder = scanner.pieceScratch.Clear().Append(single);
            }
            builder.Append(piece);
        }

        public override readonly string ToString()
        {
            return builder != null ? builder.ToString() : single ?? string.Empty;
        }
    }

    /// <summary>scanner.ts: createScanner. Creates a scanner over a (possibly unspecified) range of a piece of text.</summary>
    internal static Scanner CreateScanner(
        ScriptTarget languageVersion,
        bool skipTrivia,
        LanguageVariant languageVariant = LanguageVariant.Standard,
        string? textInitial = null,
        ErrorCallback? onError = null,
        int? start = null,
        int? length = null)
    {
        return new Scanner(languageVersion, skipTrivia, languageVariant, textInitial, onError, start, length);
    }

    private Scanner(ScriptTarget languageVersion, bool skipTrivia, LanguageVariant languageVariant, string? textInitial, ErrorCallback? onError, int? start, int? length)
    {
        this.languageVersion = languageVersion;
        this.skipTrivia = skipTrivia;
        this.languageVariant = languageVariant;
        this.onError = onError;
        text = textInitial ?? string.Empty;
        SetText(text, start, length);
    }

    internal int GetTokenFullStart() => fullStartPos;

    /// <summary>Deprecated: use <see cref="GetTokenFullStart"/>.</summary>
    internal int GetStartPos() => fullStartPos;

    internal int GetTokenEnd() => pos;

    /// <summary>Deprecated: use <see cref="GetTokenEnd"/>.</summary>
    internal int GetTextPos() => pos;

    internal SyntaxKind GetToken() => token;

    internal int GetTokenStart() => tokenStart;

    /// <summary>Deprecated: use <see cref="GetTokenStart"/>.</summary>
    internal int GetTokenPos() => tokenStart;

    internal string GetTokenText() => Substring(text, tokenStart, pos);

    internal string GetTokenValue() => tokenValue;

    internal bool HasUnicodeEscape() => (tokenFlags & TokenFlags.UnicodeEscape) != 0;

    internal bool HasExtendedUnicodeEscape() => (tokenFlags & TokenFlags.ExtendedUnicodeEscape) != 0;

    internal bool HasPrecedingLineBreak() => (tokenFlags & TokenFlags.PrecedingLineBreak) != 0;

    internal bool HasPrecedingJSDocComment() => (tokenFlags & TokenFlags.PrecedingJSDocComment) != 0;

    internal bool HasPrecedingJSDocLeadingAsterisks() => (tokenFlags & TokenFlags.PrecedingJSDocLeadingAsterisks) != 0;

    internal bool IsIdentifier() => token == SyntaxKind.Identifier || token > SyntaxKind.LastReservedWord;

    internal bool IsReservedWord() => token >= SyntaxKind.FirstReservedWord && token <= SyntaxKind.LastReservedWord;

    internal bool IsUnterminated() => (tokenFlags & TokenFlags.Unterminated) != 0;

    internal List<CommentDirective>? GetCommentDirectives() => commentDirectives;

    internal TokenFlags GetNumericLiteralFlags() => tokenFlags & TokenFlags.NumericLiteralFlags;

    internal TokenFlags GetTokenFlags() => tokenFlags;

    /// <summary>
    /// Returns the code point for the character at the given position within `text`. This
    /// should only be used when pos is guaranteed to be within the bounds of `text` as this
    /// function does not perform bounds checks.
    /// </summary>
    private int CodePointUnchecked(int pos)
    {
        return CodePointAt(text, pos);
    }

    /// <summary>
    /// Returns the code point for the character at the given position within `text`. If
    /// `pos` is outside the bounds set for `text`, `CharacterCodes.EOF` is returned instead.
    /// </summary>
    private int CodePointChecked(int pos)
    {
        return pos >= 0 && pos < end ? CodePointUnchecked(pos) : Ch.EOF;
    }

    /// <summary>
    /// Returns the char code for the character at the given position within `text`. This
    /// should only be used when pos is guaranteed to be within the bounds of `text` as this
    /// function does not perform bounds checks.
    /// </summary>
    private int CharCodeUnchecked(int pos)
    {
        return CharCodeAt(text, pos);
    }

    /// <summary>
    /// Returns the char code for the character at the given position within `text`. If
    /// `pos` is outside the bounds set for `text`, `CharacterCodes.EOF` is returned instead.
    /// </summary>
    private int CharCodeChecked(int pos)
    {
        return pos >= 0 && pos < end ? CharCodeUnchecked(pos) : Ch.EOF;
    }

    private void Error(DiagnosticMessage message)
    {
        Error(message, pos, 0);
    }

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

    private string ScanNumberFragment()
    {
        var start = pos;
        var allowSeparator = false;
        var isPreviousTokenSeparator = false;
        var result = string.Empty;
        while (true)
        {
            var ch = CharCodeUnchecked(pos);
            if (ch == Ch.Underscore)
            {
                tokenFlags |= TokenFlags.ContainsSeparator;
                if (allowSeparator)
                {
                    allowSeparator = false;
                    isPreviousTokenSeparator = true;
                    result += Substring(text, start, pos);
                }
                else
                {
                    tokenFlags |= TokenFlags.ContainsInvalidSeparator;
                    if (isPreviousTokenSeparator)
                    {
                        Error(DiagnosticMessages.Multiple_consecutive_numeric_separators_are_not_permitted, pos, 1);
                    }
                    else
                    {
                        Error(DiagnosticMessages.Numeric_separators_are_not_allowed_here, pos, 1);
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
        if (CharCodeUnchecked(pos - 1) == Ch.Underscore)
        {
            tokenFlags |= TokenFlags.ContainsInvalidSeparator;
            Error(DiagnosticMessages.Numeric_separators_are_not_allowed_here, pos - 1, 1);
        }
        return result + Substring(text, start, pos);
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
    private SyntaxKind ScanNumber()
    {
        var start = pos;
        string mainFragment;
        if (CharCodeUnchecked(pos) == Ch.Digit0)
        {
            pos++;
            if (CharCodeUnchecked(pos) == Ch.Underscore)
            {
                tokenFlags |= TokenFlags.ContainsSeparator | TokenFlags.ContainsInvalidSeparator;
                Error(DiagnosticMessages.Numeric_separators_are_not_allowed_here, pos, 1);
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
                mainFragment = JsNumber.NumberToString(JsNumber.StringToNumber(tokenValue));
            }
            else if (tokenValue.Length == 0)
            {
                // a single zero
                mainFragment = "0";
            }
            else
            {
                // LegacyOctalIntegerLiteral
                tokenValue = JsNumber.NumberToString(JsNumber.ParseInt(tokenValue, 8));
                tokenFlags |= TokenFlags.Octal;
                var withMinus = token == SyntaxKind.MinusToken;
                var literal = (withMinus ? "-" : string.Empty) + "0o" + JsNumber.IntegerToOctalString(JsNumber.StringToNumber(tokenValue));
                if (withMinus)
                {
                    start--;
                }
                Error(DiagnosticMessages.Octal_literals_are_not_allowed_Use_the_syntax_0, start, pos - start, literal);
                return SyntaxKind.NumericLiteral;
            }
        }
        else
        {
            mainFragment = ScanNumberFragment();
        }
        string? decimalFragment = null;
        string? scientificFragment = null;
        if (CharCodeUnchecked(pos) == Ch.Dot)
        {
            pos++;
            decimalFragment = ScanNumberFragment();
        }
        var numberEnd = pos;
        if (CharCodeUnchecked(pos) == Ch.UpperE || CharCodeUnchecked(pos) == Ch.LowerE)
        {
            pos++;
            tokenFlags |= TokenFlags.Scientific;
            if (CharCodeUnchecked(pos) == Ch.Plus || CharCodeUnchecked(pos) == Ch.Minus)
            {
                pos++;
            }
            var preNumericPart = pos;
            var finalFragment = ScanNumberFragment();
            if (finalFragment.Length == 0)
            {
                Error(DiagnosticMessages.Digit_expected);
            }
            else
            {
                scientificFragment = Substring(text, numberEnd, preNumericPart) + finalFragment;
                numberEnd = pos;
            }
        }
        string result;
        if ((tokenFlags & TokenFlags.ContainsSeparator) != 0)
        {
            result = mainFragment;
            if (!string.IsNullOrEmpty(decimalFragment))
            {
                result += "." + decimalFragment;
            }
            if (!string.IsNullOrEmpty(scientificFragment))
            {
                result += scientificFragment;
            }
        }
        else
        {
            result = Substring(text, start, numberEnd); // No need to use all the fragments; no _ removal needed
        }

        if ((tokenFlags & TokenFlags.ContainsLeadingZero) != 0)
        {
            Error(DiagnosticMessages.Decimals_with_leading_zeros_are_not_allowed, start, numberEnd - start);
            // if a literal has a leading zero, it must not be bigint
            tokenValue = JsNumber.NumberToString(JsNumber.StringToNumber(result));
            return SyntaxKind.NumericLiteral;
        }

        if (decimalFragment != null || (tokenFlags & TokenFlags.Scientific) != 0)
        {
            CheckForIdentifierStartAfterNumericLiteral(start, decimalFragment == null && (tokenFlags & TokenFlags.Scientific) != 0);
            // if value is not an integer, it can be safely coerced to a number
            tokenValue = JsNumber.NumberToString(JsNumber.StringToNumber(result));
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

    private void CheckForIdentifierStartAfterNumericLiteral(int numericStart, bool isScientific = false)
    {
        if (!IsIdentifierStart(CodePointUnchecked(pos), languageVersion))
        {
            return;
        }

        var identifierStart = pos;
        var length = ScanIdentifierParts().Length;

        if (length == 1 && text[identifierStart] == 'n')
        {
            if (isScientific)
            {
                Error(DiagnosticMessages.A_bigint_literal_cannot_use_exponential_notation, numericStart, identifierStart - numericStart + 1);
            }
            else
            {
                Error(DiagnosticMessages.A_bigint_literal_must_be_an_integer, numericStart, identifierStart - numericStart + 1);
            }
        }
        else
        {
            Error(DiagnosticMessages.An_identifier_or_keyword_cannot_immediately_follow_a_numeric_literal, identifierStart, length);
            pos = identifierStart;
        }
    }

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
        tokenValue = Substring(text, start, pos);
        return isOctal;
    }

    /// <summary>
    /// Scans the given number of hexadecimal digits in the text,
    /// returning -1 if the given number is unavailable.
    /// </summary>
    private int ScanExactNumberOfHexDigits(int count, bool canHaveSeparators)
    {
        var valueString = ScanHexDigits(/*minCount*/ count, /*scanAsManyAsPossible*/ false, canHaveSeparators);
        return valueString.Length != 0 ? JsNumber.ParseHexDigitsSaturated(valueString) : -1;
    }

    /// <summary>
    /// Scans as many hexadecimal digits as are available in the text,
    /// returning "" if the given number of digits was unavailable.
    /// </summary>
    private string ScanMinimumNumberOfHexDigits(int count, bool canHaveSeparators)
    {
        return ScanHexDigits(/*minCount*/ count, /*scanAsManyAsPossible*/ true, canHaveSeparators);
    }

    private string ScanHexDigits(int minCount, bool scanAsManyAsPossible, bool canHaveSeparators)
    {
        var valueChars = hexScratch.Clear();
        var allowSeparator = false;
        var isPreviousTokenSeparator = false;
        while (valueChars.Length < minCount || scanAsManyAsPossible)
        {
            var ch = CharCodeUnchecked(pos);
            if (canHaveSeparators && ch == Ch.Underscore)
            {
                tokenFlags |= TokenFlags.ContainsSeparator;
                if (allowSeparator)
                {
                    allowSeparator = false;
                    isPreviousTokenSeparator = true;
                }
                else if (isPreviousTokenSeparator)
                {
                    Error(DiagnosticMessages.Multiple_consecutive_numeric_separators_are_not_permitted, pos, 1);
                }
                else
                {
                    Error(DiagnosticMessages.Numeric_separators_are_not_allowed_here, pos, 1);
                }
                pos++;
                continue;
            }
            allowSeparator = canHaveSeparators;
            if (ch >= Ch.UpperA && ch <= Ch.UpperF)
            {
                ch += Ch.LowerA - Ch.UpperA; // standardize hex literals to lowercase
            }
            else if (
                !((ch >= Ch.Digit0 && ch <= Ch.Digit9) ||
                    (ch >= Ch.LowerA && ch <= Ch.LowerF)))
            {
                break;
            }
            valueChars.Append((char)ch);
            pos++;
            isPreviousTokenSeparator = false;
        }
        if (valueChars.Length < minCount)
        {
            valueChars.Clear();
        }
        if (CharCodeUnchecked(pos - 1) == Ch.Underscore)
        {
            Error(DiagnosticMessages.Numeric_separators_are_not_allowed_here, pos - 1, 1);
        }
        return valueChars.ToString();
    }

    private string ScanString(bool jsxAttributeString = false)
    {
        var quote = CharCodeUnchecked(pos);
        pos++;
        var result = new PieceAccumulator(this);
        var start = pos;
        while (true)
        {
            if (pos >= end)
            {
                result.AppendText(start, pos);
                tokenFlags |= TokenFlags.Unterminated;
                Error(DiagnosticMessages.Unterminated_string_literal);
                break;
            }
            var ch = CharCodeUnchecked(pos);
            if (ch == quote)
            {
                result.AppendText(start, pos);
                pos++;
                break;
            }
            if (ch == Ch.Backslash && !jsxAttributeString)
            {
                result.AppendText(start, pos);
                result.Append(ScanEscapeSequence(EscapeSequenceScanningFlags.String | EscapeSequenceScanningFlags.ReportErrors));
                start = pos;
                continue;
            }

            if ((ch == Ch.LineFeed || ch == Ch.CarriageReturn) && !jsxAttributeString)
            {
                result.AppendText(start, pos);
                tokenFlags |= TokenFlags.Unterminated;
                Error(DiagnosticMessages.Unterminated_string_literal);
                break;
            }
            pos++;
        }
        return result.ToString();
    }

    /// <summary>
    /// Sets the current 'tokenValue' and returns a NoSubstitutionTemplateLiteral or
    /// a literal component of a TemplateExpression.
    /// </summary>
    private SyntaxKind ScanTemplateAndSetTokenValue(bool shouldEmitInvalidEscapeError)
    {
        var startedWithBacktick = CharCodeUnchecked(pos) == Ch.Backtick;

        pos++;
        var start = pos;
        var contents = new PieceAccumulator(this);
        SyntaxKind resultingToken;

        while (true)
        {
            if (pos >= end)
            {
                contents.AppendText(start, pos);
                tokenFlags |= TokenFlags.Unterminated;
                Error(DiagnosticMessages.Unterminated_template_literal);
                resultingToken = startedWithBacktick ? SyntaxKind.NoSubstitutionTemplateLiteral : SyntaxKind.TemplateTail;
                break;
            }

            var currChar = CharCodeUnchecked(pos);

            // '`'
            if (currChar == Ch.Backtick)
            {
                contents.AppendText(start, pos);
                pos++;
                resultingToken = startedWithBacktick ? SyntaxKind.NoSubstitutionTemplateLiteral : SyntaxKind.TemplateTail;
                break;
            }

            // '${'
            if (currChar == Ch.Dollar && pos + 1 < end && CharCodeUnchecked(pos + 1) == Ch.OpenBrace)
            {
                contents.AppendText(start, pos);
                pos += 2;
                resultingToken = startedWithBacktick ? SyntaxKind.TemplateHead : SyntaxKind.TemplateMiddle;
                break;
            }

            // Escape character
            if (currChar == Ch.Backslash)
            {
                contents.AppendText(start, pos);
                contents.Append(ScanEscapeSequence(EscapeSequenceScanningFlags.String | (shouldEmitInvalidEscapeError ? EscapeSequenceScanningFlags.ReportErrors : 0)));
                start = pos;
                continue;
            }

            // Speculated ECMAScript 6 Spec 11.8.6.1:
            // <CR><LF> and <CR> LineTerminatorSequences are normalized to <LF> for Template Values
            if (currChar == Ch.CarriageReturn)
            {
                contents.AppendText(start, pos);
                pos++;

                if (pos < end && CharCodeUnchecked(pos) == Ch.LineFeed)
                {
                    pos++;
                }

                contents.Append("\n");
                start = pos;
                continue;
            }

            pos++;
        }

        tokenValue = contents.ToString();
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
    private string ScanEscapeSequence(EscapeSequenceScanningFlags flags)
    {
        var start = pos;
        pos++;
        if (pos >= end)
        {
            Error(DiagnosticMessages.Unexpected_end_of_text);
            return string.Empty;
        }
        var ch = CharCodeUnchecked(pos);
        pos++;
        switch (ch)
        {
            case Ch.Digit0:
                // Although '0' preceding any digit is treated as LegacyOctalEscapeSequence,
                // '\08' should separately be interpreted as '\0' + '8'.
                if (pos >= end || !IsDigit(CharCodeUnchecked(pos)))
                {
                    return "\0";
                }
                // '\01', '\011'
                goto case Ch.Digit1;
            case Ch.Digit1:
            case Ch.Digit2:
            case Ch.Digit3:
                // '\1', '\17', '\177'
                if (pos < end && IsOctalDigit(CharCodeUnchecked(pos)))
                {
                    pos++;
                }
                // '\17', '\177'
                goto case Ch.Digit4;
            case Ch.Digit4:
            case Ch.Digit5:
            case Ch.Digit6:
            case Ch.Digit7:
                // '\4', '\47' but not '\477'
                if (pos < end && IsOctalDigit(CharCodeUnchecked(pos)))
                {
                    pos++;
                }
                // '\47'
                tokenFlags |= TokenFlags.ContainsInvalidEscape;
                if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                {
                    var code = (int)JsNumber.ParseInt(Substring(text, start + 1, pos), 8);
                    if ((flags & EscapeSequenceScanningFlags.RegularExpression) != 0 && (flags & EscapeSequenceScanningFlags.AtomEscape) == 0 && ch != Ch.Digit0)
                    {
                        Error(DiagnosticMessages.Octal_escape_sequences_and_backreferences_are_not_allowed_in_a_character_class_If_this_was_intended_as_an_escape_sequence_use_the_syntax_0_instead, start, pos - start, "\\x" + HexPadStart2(code));
                    }
                    else
                    {
                        Error(DiagnosticMessages.Octal_escape_sequences_are_not_allowed_Use_the_syntax_0, start, pos - start, "\\x" + HexPadStart2(code));
                    }
                    return ((char)code).ToString();
                }
                return Substring(text, start, pos);
            case Ch.Digit8:
            case Ch.Digit9:
                // the invalid '\8' and '\9'
                tokenFlags |= TokenFlags.ContainsInvalidEscape;
                if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                {
                    if ((flags & EscapeSequenceScanningFlags.RegularExpression) != 0 && (flags & EscapeSequenceScanningFlags.AtomEscape) == 0)
                    {
                        Error(DiagnosticMessages.Decimal_escape_sequences_and_backreferences_are_not_allowed_in_a_character_class, start, pos - start);
                    }
                    else
                    {
                        Error(DiagnosticMessages.Escape_sequence_0_is_not_allowed, start, pos - start, Substring(text, start, pos));
                    }
                    return ((char)ch).ToString();
                }
                return Substring(text, start, pos);
            case Ch.LowerB:
                return "\b";
            case Ch.LowerT:
                return "\t";
            case Ch.LowerN:
                return "\n";
            case Ch.LowerV:
                return "\v";
            case Ch.LowerF:
                return "\f";
            case Ch.LowerR:
                return "\r";
            case Ch.SingleQuote:
                return "'";
            case Ch.DoubleQuote:
                return "\"";
            case Ch.LowerU:
                if (pos < end && CharCodeUnchecked(pos) == Ch.OpenBrace)
                {
                    // '\u{DDDDDD}'
                    pos -= 2;
                    var result = ScanExtendedUnicodeEscape((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0);
                    if ((flags & EscapeSequenceScanningFlags.AllowExtendedUnicodeEscape) == 0)
                    {
                        tokenFlags |= TokenFlags.ContainsInvalidEscape;
                        if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                        {
                            Error(DiagnosticMessages.Unicode_escape_sequences_are_only_available_when_the_Unicode_u_flag_or_the_Unicode_Sets_v_flag_is_set, start, pos - start);
                        }
                    }
                    return result;
                }
                // '\uDDDD'
                for (; pos < start + 6; pos++)
                {
                    if (!(pos < end && IsHexDigit(CharCodeUnchecked(pos))))
                    {
                        tokenFlags |= TokenFlags.ContainsInvalidEscape;
                        if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                        {
                            Error(DiagnosticMessages.Hexadecimal_digit_expected);
                        }
                        return Substring(text, start, pos);
                    }
                }
                tokenFlags |= TokenFlags.UnicodeEscape;
                var escapedValue = (int)JsNumber.ParseInt(Substring(text, start + 2, pos), 16);
                var escapedValueString = ((char)escapedValue).ToString();
                if (
                    (flags & EscapeSequenceScanningFlags.AnyUnicodeMode) != 0 && escapedValue >= 0xD800 && escapedValue <= 0xDBFF &&
                    pos + 6 < end && CharCodeUnchecked(pos) == Ch.Backslash && CharCodeUnchecked(pos + 1) == Ch.LowerU && CharCodeUnchecked(pos + 2) != Ch.OpenBrace)
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
                            return escapedValueString;
                        }
                    }
                    var nextEscapedValue = (int)JsNumber.ParseInt(Substring(text, nextStart + 2, nextPos), 16);
                    if (nextEscapedValue >= 0xDC00 && nextEscapedValue <= 0xDFFF)
                    {
                        pos = nextPos;
                        return escapedValueString + ((char)nextEscapedValue).ToString();
                    }
                }
                return escapedValueString;

            case Ch.LowerX:
                // '\xDD'
                for (; pos < start + 4; pos++)
                {
                    if (!(pos < end && IsHexDigit(CharCodeUnchecked(pos))))
                    {
                        tokenFlags |= TokenFlags.ContainsInvalidEscape;
                        if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                        {
                            Error(DiagnosticMessages.Hexadecimal_digit_expected);
                        }
                        return Substring(text, start, pos);
                    }
                }
                tokenFlags |= TokenFlags.HexEscape;
                return ((char)(int)JsNumber.ParseInt(Substring(text, start + 2, pos), 16)).ToString();

            // when encountering a LineContinuation (i.e. a backslash and a line terminator sequence),
            // the line terminator is interpreted to be "the empty code unit sequence".
            case Ch.CarriageReturn:
                if (pos < end && CharCodeUnchecked(pos) == Ch.LineFeed)
                {
                    pos++;
                }
                return string.Empty;
            case Ch.LineFeed:
            case Ch.LineSeparator:
            case Ch.ParagraphSeparator:
                return string.Empty;
            default:
                if (
                    (flags & EscapeSequenceScanningFlags.AnyUnicodeMode) != 0
                    || ((flags & EscapeSequenceScanningFlags.RegularExpression) != 0
                        && (flags & EscapeSequenceScanningFlags.AnnexB) == 0
                        && IsIdentifierPart(ch, languageVersion)))
                {
                    Error(DiagnosticMessages.This_character_cannot_be_escaped_in_a_regular_expression, pos - 2, 2);
                }
                return ((char)ch).ToString();
        }
    }

    /// <summary>`code.toString(16).padStart(2, "0")`.</summary>
    private static string HexPadStart2(int code)
    {
        return code.ToString("x", System.Globalization.CultureInfo.InvariantCulture).PadLeft(2, '0');
    }

    private string ScanExtendedUnicodeEscape(bool shouldEmitInvalidEscapeError)
    {
        var start = pos;
        pos += 3;
        var escapedStart = pos;
        var escapedValueString = ScanMinimumNumberOfHexDigits(1, /*canHaveSeparators*/ false);
        var escapedValue = escapedValueString.Length != 0 ? JsNumber.ParseHexDigitsSaturated(escapedValueString) : -1;
        var isInvalidExtendedEscape = false;

        // Validate the value of the digit
        if (escapedValue < 0)
        {
            if (shouldEmitInvalidEscapeError)
            {
                Error(DiagnosticMessages.Hexadecimal_digit_expected);
            }
            isInvalidExtendedEscape = true;
        }
        else if (escapedValue > 0x10FFFF)
        {
            if (shouldEmitInvalidEscapeError)
            {
                Error(DiagnosticMessages.An_extended_Unicode_escape_value_must_be_between_0x0_and_0x10FFFF_inclusive, escapedStart, pos - escapedStart);
            }
            isInvalidExtendedEscape = true;
        }

        if (pos >= end)
        {
            if (shouldEmitInvalidEscapeError)
            {
                Error(DiagnosticMessages.Unexpected_end_of_text);
            }
            isInvalidExtendedEscape = true;
        }
        else if (CharCodeUnchecked(pos) == Ch.CloseBrace)
        {
            // Only swallow the following character up if it's a '}'.
            pos++;
        }
        else
        {
            if (shouldEmitInvalidEscapeError)
            {
                Error(DiagnosticMessages.Unterminated_Unicode_escape_sequence);
            }
            isInvalidExtendedEscape = true;
        }

        if (isInvalidExtendedEscape)
        {
            tokenFlags |= TokenFlags.ContainsInvalidEscape;
            return Substring(text, start, pos);
        }

        tokenFlags |= TokenFlags.ExtendedUnicodeEscape;
        return Utf16EncodeAsString(escapedValue);
    }

    // Current character is known to be a backslash. Check for Unicode escape of the form '\uXXXX'
    // and return code point value if valid Unicode escape is found. Otherwise return -1.
    private int PeekUnicodeEscape()
    {
        if (pos + 5 < end && CharCodeUnchecked(pos + 1) == Ch.LowerU)
        {
            var start = pos;
            pos += 2;
            var value = ScanExactNumberOfHexDigits(4, /*canHaveSeparators*/ false);
            pos = start;
            return value;
        }
        return -1;
    }

    private int PeekExtendedUnicodeEscape()
    {
        if (CodePointUnchecked(pos + 1) == Ch.LowerU && CodePointUnchecked(pos + 2) == Ch.OpenBrace)
        {
            var start = pos;
            pos += 3;
            var escapedValueString = ScanMinimumNumberOfHexDigits(1, /*canHaveSeparators*/ false);
            var escapedValue = escapedValueString.Length != 0 ? JsNumber.ParseHexDigitsSaturated(escapedValueString) : -1;
            pos = start;
            return escapedValue;
        }
        return -1;
    }

    private string ScanIdentifierParts()
    {
        var result = string.Empty;
        var start = pos;
        while (pos < end)
        {
            var ch = CodePointUnchecked(pos);
            if (IsIdentifierPart(ch, languageVersion))
            {
                pos += CharSize(ch);
            }
            else if (ch == Ch.Backslash)
            {
                ch = PeekExtendedUnicodeEscape();
                if (ch >= 0 && IsIdentifierPart(ch, languageVersion))
                {
                    result += ScanExtendedUnicodeEscape(/*shouldEmitInvalidEscapeError*/ true);
                    start = pos;
                    continue;
                }
                ch = PeekUnicodeEscape();
                if (!(ch >= 0 && IsIdentifierPart(ch, languageVersion)))
                {
                    break;
                }
                tokenFlags |= TokenFlags.UnicodeEscape;
                result += Substring(text, start, pos);
                result += Utf16EncodeAsString(ch);
                // Valid Unicode escape is always six characters
                pos += 6;
                start = pos;
            }
            else
            {
                break;
            }
        }
        result += Substring(text, start, pos);
        return result;
    }

    private SyntaxKind GetIdentifierToken()
    {
        // Reserved words are between 2 and 12 characters long and start with a lowercase letter
        var len = tokenValue.Length;
        if (len >= 2 && len <= 12)
        {
            var ch = tokenValue[0];
            if (ch >= Ch.LowerA && ch <= Ch.LowerZ)
            {
                if (TextToKeyword.TryGetValue(tokenValue, out var keyword))
                {
                    return token = keyword;
                }
            }
        }
        return token = SyntaxKind.Identifier;
    }

    private string ScanBinaryOrOctalDigits(int radixBase)
    {
        var value = hexScratch.Clear();
        // For counting number of digits; Valid binaryIntegerLiteral must have at least one binary digit following B or b.
        // Similarly valid octalIntegerLiteral must have at least one octal digit following o or O.
        var separatorAllowed = false;
        var isPreviousTokenSeparator = false;
        while (true)
        {
            var ch = CharCodeUnchecked(pos);
            // Numeric separators are allowed anywhere within a numeric literal, except not at the beginning, or following another separator
            if (ch == Ch.Underscore)
            {
                tokenFlags |= TokenFlags.ContainsSeparator;
                if (separatorAllowed)
                {
                    separatorAllowed = false;
                    isPreviousTokenSeparator = true;
                }
                else if (isPreviousTokenSeparator)
                {
                    Error(DiagnosticMessages.Multiple_consecutive_numeric_separators_are_not_permitted, pos, 1);
                }
                else
                {
                    Error(DiagnosticMessages.Numeric_separators_are_not_allowed_here, pos, 1);
                }
                pos++;
                continue;
            }
            separatorAllowed = true;
            if (!IsDigit(ch) || ch - Ch.Digit0 >= radixBase)
            {
                break;
            }
            value.Append(text[pos]);
            pos++;
            isPreviousTokenSeparator = false;
        }
        if (CharCodeUnchecked(pos - 1) == Ch.Underscore)
        {
            // Literal ends with underscore - not allowed
            Error(DiagnosticMessages.Numeric_separators_are_not_allowed_here, pos - 1, 1);
        }
        return value.ToString();
    }

    private SyntaxKind CheckBigIntSuffix()
    {
        if (CharCodeUnchecked(pos) == Ch.LowerN)
        {
            tokenValue += "n";
            // Use base 10 instead of base 2 or base 8 for shorter literals
            if ((tokenFlags & TokenFlags.BinaryOrOctalSpecifier) != 0)
            {
                tokenValue = Utilities.ParsePseudoBigInt(tokenValue) + "n";
            }
            pos++;
            return SyntaxKind.BigIntLiteral;
        }
        else
        { // not a bigint, so can convert to number in simplified form
            // Number() may not support 0b or 0o, so use parseInt() instead
            var numericValue = (tokenFlags & TokenFlags.BinarySpecifier) != 0
                ? JsNumber.ParseInt(tokenValue.Substring(2), 2) // skip "0b"
                : (tokenFlags & TokenFlags.OctalSpecifier) != 0
                ? JsNumber.ParseInt(tokenValue.Substring(2), 8) // skip "0o"
                : JsNumber.StringToNumber(tokenValue);
            tokenValue = JsNumber.NumberToString(numericValue);
            return SyntaxKind.NumericLiteral;
        }
    }

    internal SyntaxKind Scan()
    {
        fullStartPos = pos;
        tokenFlags = TokenFlags.None;
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
                if (ch == Ch.Hash && IsShebangTrivia(text, pos))
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
                case Ch.LineFeed:
                case Ch.CarriageReturn:
                    tokenFlags |= TokenFlags.PrecedingLineBreak;
                    if (skipTrivia)
                    {
                        pos++;
                        continue;
                    }
                    else
                    {
                        if (ch == Ch.CarriageReturn && pos + 1 < end && CharCodeUnchecked(pos + 1) == Ch.LineFeed)
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
                case Ch.Tab:
                case Ch.VerticalTab:
                case Ch.FormFeed:
                case Ch.Space:
                case Ch.NonBreakingSpace:
                case Ch.Ogham:
                case Ch.EnQuad:
                case Ch.EmQuad:
                case Ch.EnSpace:
                case Ch.EmSpace:
                case Ch.ThreePerEmSpace:
                case Ch.FourPerEmSpace:
                case Ch.SixPerEmSpace:
                case Ch.FigureSpace:
                case Ch.PunctuationSpace:
                case Ch.ThinSpace:
                case Ch.HairSpace:
                case Ch.ZeroWidthSpace:
                case Ch.NarrowNoBreakSpace:
                case Ch.MathematicalSpace:
                case Ch.IdeographicSpace:
                case Ch.ByteOrderMark:
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
                case Ch.Exclamation:
                    if (CharCodeUnchecked(pos + 1) == Ch.EqualsSign)
                    {
                        if (CharCodeUnchecked(pos + 2) == Ch.EqualsSign)
                        {
                            pos += 3;
                            return token = SyntaxKind.ExclamationEqualsEqualsToken;
                        }
                        pos += 2;
                        return token = SyntaxKind.ExclamationEqualsToken;
                    }
                    pos++;
                    return token = SyntaxKind.ExclamationToken;
                case Ch.DoubleQuote:
                case Ch.SingleQuote:
                    tokenValue = ScanString();
                    return token = SyntaxKind.StringLiteral;
                case Ch.Backtick:
                    return token = ScanTemplateAndSetTokenValue(/*shouldEmitInvalidEscapeError*/ false);
                case Ch.Percent:
                    if (CharCodeUnchecked(pos + 1) == Ch.EqualsSign)
                    {
                        pos += 2;
                        return token = SyntaxKind.PercentEqualsToken;
                    }
                    pos++;
                    return token = SyntaxKind.PercentToken;
                case Ch.Ampersand:
                    if (CharCodeUnchecked(pos + 1) == Ch.Ampersand)
                    {
                        if (CharCodeUnchecked(pos + 2) == Ch.EqualsSign)
                        {
                            pos += 3;
                            return token = SyntaxKind.AmpersandAmpersandEqualsToken;
                        }
                        pos += 2;
                        return token = SyntaxKind.AmpersandAmpersandToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == Ch.EqualsSign)
                    {
                        pos += 2;
                        return token = SyntaxKind.AmpersandEqualsToken;
                    }
                    pos++;
                    return token = SyntaxKind.AmpersandToken;
                case Ch.OpenParen:
                    pos++;
                    return token = SyntaxKind.OpenParenToken;
                case Ch.CloseParen:
                    pos++;
                    return token = SyntaxKind.CloseParenToken;
                case Ch.Asterisk:
                    if (CharCodeUnchecked(pos + 1) == Ch.EqualsSign)
                    {
                        pos += 2;
                        return token = SyntaxKind.AsteriskEqualsToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == Ch.Asterisk)
                    {
                        if (CharCodeUnchecked(pos + 2) == Ch.EqualsSign)
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
                        (tokenFlags & TokenFlags.PrecedingLineBreak) != 0)
                    {
                        // decoration at the start of a JSDoc comment line
                        tokenFlags |= TokenFlags.PrecedingJSDocLeadingAsterisks;
                        continue;
                    }
                    return token = SyntaxKind.AsteriskToken;
                case Ch.Plus:
                    if (CharCodeUnchecked(pos + 1) == Ch.Plus)
                    {
                        pos += 2;
                        return token = SyntaxKind.PlusPlusToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == Ch.EqualsSign)
                    {
                        pos += 2;
                        return token = SyntaxKind.PlusEqualsToken;
                    }
                    pos++;
                    return token = SyntaxKind.PlusToken;
                case Ch.Comma:
                    pos++;
                    return token = SyntaxKind.CommaToken;
                case Ch.Minus:
                    if (CharCodeUnchecked(pos + 1) == Ch.Minus)
                    {
                        pos += 2;
                        return token = SyntaxKind.MinusMinusToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == Ch.EqualsSign)
                    {
                        pos += 2;
                        return token = SyntaxKind.MinusEqualsToken;
                    }
                    pos++;
                    return token = SyntaxKind.MinusToken;
                case Ch.Dot:
                    if (IsDigit(CharCodeUnchecked(pos + 1)))
                    {
                        ScanNumber();
                        return token = SyntaxKind.NumericLiteral;
                    }
                    if (CharCodeUnchecked(pos + 1) == Ch.Dot && CharCodeUnchecked(pos + 2) == Ch.Dot)
                    {
                        pos += 3;
                        return token = SyntaxKind.DotDotDotToken;
                    }
                    pos++;
                    return token = SyntaxKind.DotToken;
                case Ch.Slash:
                    // Single-line comment
                    if (CharCodeUnchecked(pos + 1) == Ch.Slash)
                    {
                        pos += 2;

                        while (pos < end)
                        {
                            if (IsLineBreak(CharCodeUnchecked(pos)))
                            {
                                break;
                            }
                            pos++;
                        }

                        commentDirectives = AppendIfCommentDirective(
                            commentDirectives,
                            tokenStart,
                            pos,
                            /*multiLine*/ false,
                            tokenStart);

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
                    if (CharCodeUnchecked(pos + 1) == Ch.Asterisk)
                    {
                        pos += 2;
                        var isJSDoc = CharCodeUnchecked(pos) == Ch.Asterisk && CharCodeUnchecked(pos + 1) != Ch.Slash;

                        var commentClosed = false;
                        var lastLineStart = tokenStart;
                        while (pos < end)
                        {
                            var commentCh = CharCodeUnchecked(pos);

                            if (commentCh == Ch.Asterisk && CharCodeUnchecked(pos + 1) == Ch.Slash)
                            {
                                pos += 2;
                                commentClosed = true;
                                break;
                            }

                            pos++;

                            if (IsLineBreak(commentCh))
                            {
                                lastLineStart = pos;
                                tokenFlags |= TokenFlags.PrecedingLineBreak;
                            }
                        }

                        if (isJSDoc && ShouldParseJSDoc())
                        {
                            tokenFlags |= TokenFlags.PrecedingJSDocComment;
                        }

                        commentDirectives = AppendIfCommentDirective(commentDirectives, lastLineStart, pos, /*multiLine*/ true, lastLineStart);

                        if (!commentClosed)
                        {
                            Error(DiagnosticMessages.Asterisk_Slash_expected);
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

                    if (CharCodeUnchecked(pos + 1) == Ch.EqualsSign)
                    {
                        pos += 2;
                        return token = SyntaxKind.SlashEqualsToken;
                    }

                    pos++;
                    return token = SyntaxKind.SlashToken;

                case Ch.Digit0:
                    if (pos + 2 < end && (CharCodeUnchecked(pos + 1) == Ch.UpperX || CharCodeUnchecked(pos + 1) == Ch.LowerX))
                    {
                        pos += 2;
                        tokenValue = ScanMinimumNumberOfHexDigits(1, /*canHaveSeparators*/ true);
                        if (tokenValue.Length == 0)
                        {
                            Error(DiagnosticMessages.Hexadecimal_digit_expected);
                            tokenValue = "0";
                        }
                        tokenValue = "0x" + tokenValue;
                        tokenFlags |= TokenFlags.HexSpecifier;
                        return token = CheckBigIntSuffix();
                    }
                    else if (pos + 2 < end && (CharCodeUnchecked(pos + 1) == Ch.UpperB || CharCodeUnchecked(pos + 1) == Ch.LowerB))
                    {
                        pos += 2;
                        tokenValue = ScanBinaryOrOctalDigits(/* base */ 2);
                        if (tokenValue.Length == 0)
                        {
                            Error(DiagnosticMessages.Binary_digit_expected);
                            tokenValue = "0";
                        }
                        tokenValue = "0b" + tokenValue;
                        tokenFlags |= TokenFlags.BinarySpecifier;
                        return token = CheckBigIntSuffix();
                    }
                    else if (pos + 2 < end && (CharCodeUnchecked(pos + 1) == Ch.UpperO || CharCodeUnchecked(pos + 1) == Ch.LowerO))
                    {
                        pos += 2;
                        tokenValue = ScanBinaryOrOctalDigits(/* base */ 8);
                        if (tokenValue.Length == 0)
                        {
                            Error(DiagnosticMessages.Octal_digit_expected);
                            tokenValue = "0";
                        }
                        tokenValue = "0o" + tokenValue;
                        tokenFlags |= TokenFlags.OctalSpecifier;
                        return token = CheckBigIntSuffix();
                    }
                    goto case Ch.Digit1;
                case Ch.Digit1:
                case Ch.Digit2:
                case Ch.Digit3:
                case Ch.Digit4:
                case Ch.Digit5:
                case Ch.Digit6:
                case Ch.Digit7:
                case Ch.Digit8:
                case Ch.Digit9:
                    return token = ScanNumber();
                case Ch.Colon:
                    pos++;
                    return token = SyntaxKind.ColonToken;
                case Ch.Semicolon:
                    pos++;
                    return token = SyntaxKind.SemicolonToken;
                case Ch.LessThan:
                    if (IsConflictMarkerTrivia(text, pos))
                    {
                        pos = ScanConflictMarkerTrivia(text, pos, ErrorAt);
                        if (skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            return token = SyntaxKind.ConflictMarkerTrivia;
                        }
                    }

                    if (CharCodeUnchecked(pos + 1) == Ch.LessThan)
                    {
                        if (CharCodeUnchecked(pos + 2) == Ch.EqualsSign)
                        {
                            pos += 3;
                            return token = SyntaxKind.LessThanLessThanEqualsToken;
                        }
                        pos += 2;
                        return token = SyntaxKind.LessThanLessThanToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == Ch.EqualsSign)
                    {
                        pos += 2;
                        return token = SyntaxKind.LessThanEqualsToken;
                    }
                    if (
                        languageVariant == LanguageVariant.JSX &&
                        CharCodeUnchecked(pos + 1) == Ch.Slash &&
                        CharCodeUnchecked(pos + 2) != Ch.Asterisk)
                    {
                        pos += 2;
                        return token = SyntaxKind.LessThanSlashToken;
                    }
                    pos++;
                    return token = SyntaxKind.LessThanToken;
                case Ch.EqualsSign:
                    if (IsConflictMarkerTrivia(text, pos))
                    {
                        pos = ScanConflictMarkerTrivia(text, pos, ErrorAt);
                        if (skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            return token = SyntaxKind.ConflictMarkerTrivia;
                        }
                    }

                    if (CharCodeUnchecked(pos + 1) == Ch.EqualsSign)
                    {
                        if (CharCodeUnchecked(pos + 2) == Ch.EqualsSign)
                        {
                            pos += 3;
                            return token = SyntaxKind.EqualsEqualsEqualsToken;
                        }
                        pos += 2;
                        return token = SyntaxKind.EqualsEqualsToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == Ch.GreaterThan)
                    {
                        pos += 2;
                        return token = SyntaxKind.EqualsGreaterThanToken;
                    }
                    pos++;
                    return token = SyntaxKind.EqualsToken;
                case Ch.GreaterThan:
                    if (IsConflictMarkerTrivia(text, pos))
                    {
                        pos = ScanConflictMarkerTrivia(text, pos, ErrorAt);
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
                case Ch.Question:
                    if (CharCodeUnchecked(pos + 1) == Ch.Dot && !IsDigit(CharCodeUnchecked(pos + 2)))
                    {
                        pos += 2;
                        return token = SyntaxKind.QuestionDotToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == Ch.Question)
                    {
                        if (CharCodeUnchecked(pos + 2) == Ch.EqualsSign)
                        {
                            pos += 3;
                            return token = SyntaxKind.QuestionQuestionEqualsToken;
                        }
                        pos += 2;
                        return token = SyntaxKind.QuestionQuestionToken;
                    }
                    pos++;
                    return token = SyntaxKind.QuestionToken;
                case Ch.OpenBracket:
                    pos++;
                    return token = SyntaxKind.OpenBracketToken;
                case Ch.CloseBracket:
                    pos++;
                    return token = SyntaxKind.CloseBracketToken;
                case Ch.Caret:
                    if (CharCodeUnchecked(pos + 1) == Ch.EqualsSign)
                    {
                        pos += 2;
                        return token = SyntaxKind.CaretEqualsToken;
                    }
                    pos++;
                    return token = SyntaxKind.CaretToken;
                case Ch.OpenBrace:
                    pos++;
                    return token = SyntaxKind.OpenBraceToken;
                case Ch.Bar:
                    if (IsConflictMarkerTrivia(text, pos))
                    {
                        pos = ScanConflictMarkerTrivia(text, pos, ErrorAt);
                        if (skipTrivia)
                        {
                            continue;
                        }
                        else
                        {
                            return token = SyntaxKind.ConflictMarkerTrivia;
                        }
                    }

                    if (CharCodeUnchecked(pos + 1) == Ch.Bar)
                    {
                        if (CharCodeUnchecked(pos + 2) == Ch.EqualsSign)
                        {
                            pos += 3;
                            return token = SyntaxKind.BarBarEqualsToken;
                        }
                        pos += 2;
                        return token = SyntaxKind.BarBarToken;
                    }
                    if (CharCodeUnchecked(pos + 1) == Ch.EqualsSign)
                    {
                        pos += 2;
                        return token = SyntaxKind.BarEqualsToken;
                    }
                    pos++;
                    return token = SyntaxKind.BarToken;
                case Ch.CloseBrace:
                    pos++;
                    return token = SyntaxKind.CloseBraceToken;
                case Ch.Tilde:
                    pos++;
                    return token = SyntaxKind.TildeToken;
                case Ch.At:
                    pos++;
                    return token = SyntaxKind.AtToken;
                case Ch.Backslash:
                    var extendedCookedChar = PeekExtendedUnicodeEscape();
                    if (extendedCookedChar >= 0 && IsIdentifierStart(extendedCookedChar, languageVersion))
                    {
                        tokenValue = ScanExtendedUnicodeEscape(/*shouldEmitInvalidEscapeError*/ true) + ScanIdentifierParts();
                        return token = GetIdentifierToken();
                    }

                    var cookedChar = PeekUnicodeEscape();
                    if (cookedChar >= 0 && IsIdentifierStart(cookedChar, languageVersion))
                    {
                        pos += 6;
                        tokenFlags |= TokenFlags.UnicodeEscape;
                        tokenValue = ((char)cookedChar).ToString() + ScanIdentifierParts();
                        return token = GetIdentifierToken();
                    }

                    Error(DiagnosticMessages.Invalid_character);
                    pos++;
                    return token = SyntaxKind.Unknown;
                case Ch.Hash:
                    if (pos != 0 && CharCodeUnchecked(pos + 1) == Ch.Exclamation)
                    {
                        Error(DiagnosticMessages.can_only_be_used_at_the_start_of_a_file, pos, 2);
                        pos++;
                        return token = SyntaxKind.Unknown;
                    }

                    var charAfterHash = CodePointUnchecked(pos + 1);
                    if (charAfterHash == Ch.Backslash)
                    {
                        pos++;
                        var hashExtendedCookedChar = PeekExtendedUnicodeEscape();
                        if (hashExtendedCookedChar >= 0 && IsIdentifierStart(hashExtendedCookedChar, languageVersion))
                        {
                            tokenValue = "#" + ScanExtendedUnicodeEscape(/*shouldEmitInvalidEscapeError*/ true) + ScanIdentifierParts();
                            return token = SyntaxKind.PrivateIdentifier;
                        }

                        var hashCookedChar = PeekUnicodeEscape();
                        if (hashCookedChar >= 0 && IsIdentifierStart(hashCookedChar, languageVersion))
                        {
                            pos += 6;
                            tokenFlags |= TokenFlags.UnicodeEscape;
                            tokenValue = "#" + ((char)hashCookedChar).ToString() + ScanIdentifierParts();
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
                        tokenValue = "#";
                        Error(DiagnosticMessages.Invalid_character, pos++, CharSize(ch));
                    }
                    return token = SyntaxKind.PrivateIdentifier;
                case Ch.ReplacementCharacter:
                    Error(DiagnosticMessages.File_appears_to_be_binary, 0, 0);
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
                    Error(DiagnosticMessages.Invalid_character, pos, size);
                    pos += size;
                    return token = SyntaxKind.Unknown;
            }
        }
    }

    /// <summary>The callback `scanConflictMarkerTrivia` takes for its `error` parameter: the scanner's `error(message, errPos, length)`.</summary>
    private void ErrorAt(DiagnosticMessage message, int errPos, int length)
    {
        Error(message, errPos, length);
    }

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

        return ContainsJsDocSeeOrLink(fullStartPos, pos);
    }

    // scanner.ts: jsDocSeeOrLink = /@(?:see|link)/i tested on text.slice(from, to). The `i` flag of a regular expression
    // without the `u` flag only folds ASCII letters here (no non-ASCII character canonicalizes to an ASCII one).
    private bool ContainsJsDocSeeOrLink(int from, int to)
    {
        from = Math.Min(from, text.Length);
        to = Math.Min(to, text.Length);
        for (var i = from; i < to; i++)
        {
            if (text[i] == '@')
            {
                if (MatchesAsciiIgnoreCase(i + 1, to, "see") || MatchesAsciiIgnoreCase(i + 1, to, "link"))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private bool MatchesAsciiIgnoreCase(int from, int to, string lowerCaseWord)
    {
        if (from + lowerCaseWord.Length > to)
        {
            return false;
        }
        for (var i = 0; i < lowerCaseWord.Length; i++)
        {
            var c = text[from + i];
            if (c >= 'A' && c <= 'Z')
            {
                c = (char)(c + ('a' - 'A'));
            }
            if (c != lowerCaseWord[i])
            {
                return false;
            }
        }
        return true;
    }

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

    private SyntaxKind? ScanIdentifier(int startCharacter, ScriptTarget languageVersion)
    {
        var ch = startCharacter;
        if (IsIdentifierStart(ch, languageVersion))
        {
            pos += CharSize(ch);
            while (pos < end && IsIdentifierPart(ch = CodePointUnchecked(pos), languageVersion))
            {
                pos += CharSize(ch);
            }
            tokenValue = Substring(text, tokenStart, pos);
            if (ch == Ch.Backslash)
            {
                tokenValue += ScanIdentifierParts();
            }
            return GetIdentifierToken();
        }
        return null;
    }

    internal SyntaxKind ReScanGreaterToken()
    {
        if (token == SyntaxKind.GreaterThanToken)
        {
            if (CharCodeUnchecked(pos) == Ch.GreaterThan)
            {
                if (CharCodeUnchecked(pos + 1) == Ch.GreaterThan)
                {
                    if (CharCodeUnchecked(pos + 2) == Ch.EqualsSign)
                    {
                        pos += 3;
                        return token = SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken;
                    }
                    pos += 2;
                    return token = SyntaxKind.GreaterThanGreaterThanGreaterThanToken;
                }
                if (CharCodeUnchecked(pos + 1) == Ch.EqualsSign)
                {
                    pos += 2;
                    return token = SyntaxKind.GreaterThanGreaterThanEqualsToken;
                }
                pos++;
                return token = SyntaxKind.GreaterThanGreaterThanToken;
            }
            if (CharCodeUnchecked(pos) == Ch.EqualsSign)
            {
                pos++;
                return token = SyntaxKind.GreaterThanEqualsToken;
            }
        }
        return token;
    }

    internal SyntaxKind ReScanAsteriskEqualsToken()
    {
        Debug.Assert(token == SyntaxKind.AsteriskEqualsToken, "'reScanAsteriskEqualsToken' should only be called on a '*='");
        pos = tokenStart + 1;
        return token = SyntaxKind.EqualsToken;
    }

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
                if (ch == Ch.EOF || IsLineBreak(ch))
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
                else if (ch == Ch.Slash && !inCharacterClass)
                {
                    // A slash within a character class is permissible,
                    // but in general it signals the end of the regexp literal.
                    break;
                }
                else if (ch == Ch.OpenBracket)
                {
                    inCharacterClass = true;
                }
                else if (ch == Ch.Backslash)
                {
                    inEscape = true;
                }
                else if (ch == Ch.CloseBracket)
                {
                    inCharacterClass = false;
                }
                else if (
                    !inCharacterClass
                    && ch == Ch.OpenParen
                    && CharCodeChecked(pos + 1) == Ch.Question
                    && CharCodeChecked(pos + 2) == Ch.LessThan
                    && CharCodeChecked(pos + 3) != Ch.EqualsSign
                    && CharCodeChecked(pos + 3) != Ch.Exclamation)
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
                    else if (ch == Ch.Backslash)
                    {
                        inEscape = true;
                    }
                    else if (ch == Ch.OpenBracket)
                    {
                        characterClassDepth++;
                    }
                    else if (ch == Ch.CloseBracket && characterClassDepth != 0)
                    {
                        characterClassDepth--;
                    }
                    else if (characterClassDepth == 0)
                    {
                        if (ch == Ch.OpenBrace)
                        {
                            inDecimalQuantifier = true;
                        }
                        else if (ch == Ch.CloseBrace && inDecimalQuantifier)
                        {
                            inDecimalQuantifier = false;
                        }
                        else if (!inDecimalQuantifier)
                        {
                            if (ch == Ch.OpenParen)
                            {
                                groupDepth++;
                            }
                            else if (ch == Ch.CloseParen && groupDepth != 0)
                            {
                                groupDepth--;
                            }
                            else if (ch == Ch.CloseParen || ch == Ch.CloseBracket || ch == Ch.CloseBrace)
                            {
                                // We encountered an unbalanced bracket outside a character class. Treat this position as the end of regex.
                                break;
                            }
                        }
                    }
                    pos++;
                }
                // Whitespaces and semicolons at the end are not likely to be part of the regex
                while (IsWhiteSpaceLike(CharCodeChecked(pos - 1)) || CharCodeChecked(pos - 1) == Ch.Semicolon)
                {
                    pos--;
                }
                Error(DiagnosticMessages.Unterminated_regular_expression_literal, tokenStart, pos - tokenStart);
            }
            else
            {
                // Consume the slash character
                pos++;
                var regExpFlags = RegularExpressionFlags.None;
                while (true)
                {
                    var ch = CodePointChecked(pos);
                    if (ch == Ch.EOF || !IsIdentifierPart(ch, languageVersion))
                    {
                        break;
                    }
                    var size = CharSize(ch);
                    if (reportErrors)
                    {
                        var flag = CharacterCodeToRegularExpressionFlag(ch);
                        if (flag == null)
                        {
                            Error(DiagnosticMessages.Unknown_regular_expression_flag, pos, size);
                        }
                        else if ((regExpFlags & flag.Value) != 0)
                        {
                            Error(DiagnosticMessages.Duplicate_regular_expression_flag, pos, size);
                        }
                        else if (((regExpFlags | flag.Value) & RegularExpressionFlags.AnyUnicodeMode) == RegularExpressionFlags.AnyUnicodeMode)
                        {
                            Error(DiagnosticMessages.The_Unicode_u_flag_and_the_Unicode_Sets_v_flag_cannot_be_set_simultaneously, pos, size);
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
                    ScanRange<bool>(startOfRegExpBody, endOfRegExpBody - startOfRegExpBody, () =>
                    {
                        ScanRegularExpressionWorker(regExpFlags, /*annexB*/ true, namedCaptureGroups);
                        return true;
                    });
                }
            }
            tokenValue = Substring(text, tokenStart, pos);
            token = SyntaxKind.RegularExpressionLiteral;
        }
        return token;
    }

    private void CheckRegularExpressionFlagAvailability(RegularExpressionFlags flag, int size)
    {
        if (RegExpFlagToFirstAvailableLanguageVersion.TryGetValue(flag, out var availableFrom) && languageVersion < availableFrom)
        {
            Error(DiagnosticMessages.This_regular_expression_flag_is_only_available_when_targeting_0_or_later, pos, size, Utilities.GetNameOfScriptTarget(availableFrom));
        }
    }

    // scanner.ts: appendIfCommentDirective. `from` and `to` delimit the comment text (TypeScript passes the substring
    // and tests it with commentDirectiveRegExSingleLine/MultiLine; this matches in place, as processCommentDirective
    // does in internal/scanner/scanner.go, with the semantics of the two regular expressions).
    private List<CommentDirective>? AppendIfCommentDirective(
        List<CommentDirective>? commentDirectives,
        int from,
        int to,
        bool multiLine,
        int lineStart)
    {
        var type = GetDirectiveFromComment(from, to, multiLine);
        if (type == null)
        {
            return commentDirectives;
        }

        return Core.Append(
            commentDirectives,
            new CommentDirective
            {
                Range = new SimpleTextRange { Pos = lineStart, End = pos },
                Type = type.Value,
            });
    }

    // scanner.ts: getDirectiveFromComment with
    //   commentDirectiveRegExSingleLine = /^\/\/\/?\s*@(ts-expect-error|ts-ignore)/
    //   commentDirectiveRegExMultiLine  = /^(?:\/|\*)*\s*@(ts-expect-error|ts-ignore)/
    // applied to the text trimmed at the start.
    private CommentDirectiveType? GetDirectiveFromComment(int from, int to, bool multiLine)
    {
        to = Math.Min(to, text.Length);
        var i = from;
        // text.trimStart()
        while (i < to && JsNumber.IsStrWhiteSpace(text[i]))
        {
            i++;
        }
        if (multiLine)
        {
            while (i < to && (text[i] == '/' || text[i] == '*'))
            {
                i++;
            }
        }
        else
        {
            if (!(i + 1 < to && text[i] == '/' && text[i + 1] == '/'))
            {
                return null;
            }
            i += 2;
            if (i < to && text[i] == '/')
            {
                i++;
            }
        }
        while (i < to && JsNumber.IsStrWhiteSpace(text[i]))
        {
            i++;
        }
        if (!(i < to && text[i] == '@'))
        {
            return null;
        }
        i++;

        if (HasPrefix(i, to, "ts-expect-error"))
        {
            return CommentDirectiveType.ExpectError;
        }
        if (HasPrefix(i, to, "ts-ignore"))
        {
            return CommentDirectiveType.Ignore;
        }
        return null;
    }

    private bool HasPrefix(int from, int to, string prefix)
    {
        if (from + prefix.Length > to)
        {
            return false;
        }
        return string.CompareOrdinal(text, from, prefix, 0, prefix.Length) == 0;
    }

    /// <summary>Unconditionally back up and scan a template expression portion.</summary>
    internal SyntaxKind ReScanTemplateToken(bool isTaggedTemplate)
    {
        pos = tokenStart;
        return token = ScanTemplateAndSetTokenValue(!isTaggedTemplate);
    }

    internal SyntaxKind ReScanTemplateHeadOrNoSubstitutionTemplate()
    {
        pos = tokenStart;
        return token = ScanTemplateAndSetTokenValue(/*shouldEmitInvalidEscapeError*/ true);
    }

    internal SyntaxKind ReScanJsxToken(bool allowMultilineJsxText = true)
    {
        pos = tokenStart = fullStartPos;
        return token = ScanJsxToken(allowMultilineJsxText);
    }

    internal SyntaxKind ReScanLessThanToken()
    {
        if (token == SyntaxKind.LessThanLessThanToken)
        {
            pos = tokenStart + 1;
            return token = SyntaxKind.LessThanToken;
        }
        return token;
    }

    internal SyntaxKind ReScanHashToken()
    {
        if (token == SyntaxKind.PrivateIdentifier)
        {
            pos = tokenStart + 1;
            return token = SyntaxKind.HashToken;
        }
        return token;
    }

    internal SyntaxKind ReScanQuestionToken()
    {
        Debug.Assert(token == SyntaxKind.QuestionQuestionToken, "'reScanQuestionToken' should only be called on a '??'");
        pos = tokenStart + 1;
        return token = SyntaxKind.QuestionToken;
    }

    internal SyntaxKind ScanJsxToken(bool allowMultilineJsxText = true)
    {
        fullStartPos = tokenStart = pos;

        if (pos >= end)
        {
            return token = SyntaxKind.EndOfFileToken;
        }

        var chr = CharCodeUnchecked(pos);
        if (chr == Ch.LessThan)
        {
            if (CharCodeUnchecked(pos + 1) == Ch.Slash)
            {
                pos += 2;
                return token = SyntaxKind.LessThanSlashToken;
            }
            pos++;
            return token = SyntaxKind.LessThanToken;
        }

        if (chr == Ch.OpenBrace)
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
            if (chr == Ch.OpenBrace)
            {
                break;
            }
            if (chr == Ch.LessThan)
            {
                if (IsConflictMarkerTrivia(text, pos))
                {
                    pos = ScanConflictMarkerTrivia(text, pos, ErrorAt);
                    return token = SyntaxKind.ConflictMarkerTrivia;
                }
                break;
            }
            if (chr == Ch.GreaterThan)
            {
                Error(DiagnosticMessages.Unexpected_token_Did_you_mean_or_gt, pos, 1);
            }
            if (chr == Ch.CloseBrace)
            {
                Error(DiagnosticMessages.Unexpected_token_Did_you_mean_or_rbrace, pos, 1);
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

        tokenValue = Substring(text, fullStartPos, pos);

        return firstNonWhitespace == -1 ? SyntaxKind.JsxTextAllWhiteSpaces : SyntaxKind.JsxText;
    }

    // Scans a JSX identifier; these differ from normal identifiers in that
    // they allow dashes
    internal SyntaxKind ScanJsxIdentifier()
    {
        if (TokenIsIdentifierOrKeyword(token))
        {
            // An identifier or keyword has already been parsed - check for a `-` or a single instance of `:` and then append it and
            // everything after it to the token
            // Do note that this means that `scanJsxIdentifier` effectively _mutates_ the visible token without advancing to a new token
            // Any caller should be expecting this behavior and should only read the pos or token value after calling it.
            while (pos < end)
            {
                var ch = CharCodeUnchecked(pos);
                if (ch == Ch.Minus)
                {
                    tokenValue += "-";
                    pos++;
                    continue;
                }
                var oldPos = pos;
                tokenValue += ScanIdentifierParts(); // reuse `scanIdentifierParts` so unicode escapes are handled
                if (pos == oldPos)
                {
                    break;
                }
            }
            return GetIdentifierToken();
        }
        return token;
    }

    internal SyntaxKind ScanJsxAttributeValue()
    {
        fullStartPos = pos;

        switch (CharCodeUnchecked(pos))
        {
            case Ch.DoubleQuote:
            case Ch.SingleQuote:
                tokenValue = ScanString(/*jsxAttributeString*/ true);
                return token = SyntaxKind.StringLiteral;
            default:
                // If this scans anything other than `{`, it's a parse error.
                return Scan();
        }
    }

    internal SyntaxKind ReScanJsxAttributeValue()
    {
        pos = tokenStart = fullStartPos;
        return ScanJsxAttributeValue();
    }

    internal SyntaxKind ScanJSDocCommentTextToken(bool inBackticks)
    {
        fullStartPos = tokenStart = pos;
        tokenFlags = TokenFlags.None;
        if (pos >= end)
        {
            return token = SyntaxKind.EndOfFileToken;
        }
        for (var ch = CharCodeUnchecked(pos); pos < end && (!IsLineBreak(ch) && ch != Ch.Backtick); ch = CodePointUnchecked(++pos))
        {
            if (!inBackticks)
            {
                if (ch == Ch.OpenBrace)
                {
                    break;
                }
                else if (
                    ch == Ch.At
                    && pos - 1 >= 0 && IsWhiteSpaceSingleLine(CharCodeUnchecked(pos - 1))
                    && !(pos + 1 < end && IsWhiteSpaceLike(CharCodeUnchecked(pos + 1))))
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
        tokenValue = Substring(text, tokenStart, pos);
        return token = SyntaxKind.JSDocCommentTextToken;
    }

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
            case Ch.Tab:
            case Ch.VerticalTab:
            case Ch.FormFeed:
            case Ch.Space:
                while (pos < end && IsWhiteSpaceSingleLine(CharCodeUnchecked(pos)))
                {
                    pos++;
                }
                return token = SyntaxKind.WhitespaceTrivia;
            case Ch.At:
                return token = SyntaxKind.AtToken;
            case Ch.CarriageReturn:
                if (CharCodeUnchecked(pos) == Ch.LineFeed)
                {
                    pos++;
                }
                goto case Ch.LineFeed;
            case Ch.LineFeed:
                tokenFlags |= TokenFlags.PrecedingLineBreak;
                return token = SyntaxKind.NewLineTrivia;
            case Ch.Asterisk:
                return token = SyntaxKind.AsteriskToken;
            case Ch.OpenBrace:
                return token = SyntaxKind.OpenBraceToken;
            case Ch.CloseBrace:
                return token = SyntaxKind.CloseBraceToken;
            case Ch.OpenBracket:
                return token = SyntaxKind.OpenBracketToken;
            case Ch.CloseBracket:
                return token = SyntaxKind.CloseBracketToken;
            case Ch.OpenParen:
                return token = SyntaxKind.OpenParenToken;
            case Ch.CloseParen:
                return token = SyntaxKind.CloseParenToken;
            case Ch.LessThan:
                return token = SyntaxKind.LessThanToken;
            case Ch.GreaterThan:
                return token = SyntaxKind.GreaterThanToken;
            case Ch.EqualsSign:
                return token = SyntaxKind.EqualsToken;
            case Ch.Comma:
                return token = SyntaxKind.CommaToken;
            case Ch.Dot:
                return token = SyntaxKind.DotToken;
            case Ch.Backtick:
                return token = SyntaxKind.BacktickToken;
            case Ch.Hash:
                return token = SyntaxKind.HashToken;
            case Ch.Backslash:
                pos--;
                var extendedCookedChar = PeekExtendedUnicodeEscape();
                if (extendedCookedChar >= 0 && IsIdentifierStart(extendedCookedChar, languageVersion))
                {
                    tokenValue = ScanExtendedUnicodeEscape(/*shouldEmitInvalidEscapeError*/ true) + ScanIdentifierParts();
                    return token = GetIdentifierToken();
                }

                var cookedChar = PeekUnicodeEscape();
                if (cookedChar >= 0 && IsIdentifierStart(cookedChar, languageVersion))
                {
                    pos += 6;
                    tokenFlags |= TokenFlags.UnicodeEscape;
                    tokenValue = ((char)cookedChar).ToString() + ScanIdentifierParts();
                    return token = GetIdentifierToken();
                }
                pos++;
                return token = SyntaxKind.Unknown;
        }

        if (IsIdentifierStart(ch, languageVersion))
        {
            var chr = ch;
            // TypeScript 6.0.3 parses this condition as `(pos < end && isIdentifierPart(...)) || chr === minus` (see section 5.3 of the
            // pre-registration: the defect is ported as it is): once pos >= end, the stale value of `chr` decides.
            while ((pos < end && IsIdentifierPart(chr = CodePointUnchecked(pos), languageVersion)) || chr == Ch.Minus)
            {
                pos += CharSize(chr);
            }
            tokenValue = Substring(text, tokenStart, pos);
            if (chr == Ch.Backslash)
            {
                tokenValue += ScanIdentifierParts();
            }
            return token = GetIdentifierToken();
        }
        else
        {
            return token = SyntaxKind.Unknown;
        }
    }

    private T SpeculationHelper<T>(Func<T> callback, bool isLookahead)
    {
        var savePos = pos;
        var saveStartPos = fullStartPos;
        var saveTokenPos = tokenStart;
        var saveToken = token;
        var saveTokenValue = tokenValue;
        var saveTokenFlags = tokenFlags;
        var result = callback();

        // If our callback returned something 'falsy' or we're just looking ahead,
        // then unconditionally restore us to where we were.
        if (!IsTruthy(result) || isLookahead)
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

    /// <summary>
    /// Invokes the callback with the scanner set to scan the specified range. When the callback
    /// returns, the scanner is restored to the state it was in before scanRange was called.
    /// </summary>
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

        SetText(text, start, length);
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

    /// <summary>
    /// Invokes the provided callback then unconditionally restores the scanner to the state it
    /// was in immediately prior to invoking the callback.  The result of invoking the callback
    /// is returned from this function.
    /// </summary>
    internal T LookAhead<T>(Func<T> callback)
    {
        return SpeculationHelper(callback, /*isLookahead*/ true);
    }

    /// <summary>
    /// Invokes the provided callback.  If the callback returns something falsy, then it restores
    /// the scanner to the state it was in immediately prior to invoking the callback.  If the
    /// callback returns something truthy, then the scanner state is not rolled back.  The result
    /// of invoking the callback is returned from this function.
    /// </summary>
    internal T TryScan<T>(Func<T> callback)
    {
        return SpeculationHelper(callback, /*isLookahead*/ false);
    }

    internal string GetText()
    {
        return text;
    }

    internal void ClearCommentDirectives()
    {
        commentDirectives = null;
    }

    /// <summary>
    /// Sets the text for the scanner to scan.  An optional subrange starting point and length
    /// can be provided to have the scanner only scan a portion of the text.
    /// </summary>
    internal void SetText(string? newText, int? start = null, int? length = null)
    {
        text = !string.IsNullOrEmpty(newText) ? newText : string.Empty;
        end = length == null ? text.Length : (start ?? 0) + length.Value;
        ResetTokenState(start ?? 0);
    }

    internal void SetOnError(ErrorCallback? errorCallback)
    {
        onError = errorCallback;
    }

    internal void SetScriptTarget(ScriptTarget scriptTarget)
    {
        languageVersion = scriptTarget;
    }

    internal void SetLanguageVariant(LanguageVariant variant)
    {
        languageVariant = variant;
    }

    internal void SetScriptKind(ScriptKind kind)
    {
        scriptKind = kind;
    }

    internal void SetJSDocParsingMode(JSDocParsingMode kind)
    {
        jsDocParsingMode = kind;
    }

    /// <summary>Deprecated: use <see cref="ResetTokenState"/>.</summary>
    internal void SetTextPos(int textPos)
    {
        ResetTokenState(textPos);
    }

    internal void ResetTokenState(int position)
    {
        Debug.Assert(position >= 0);
        pos = position;
        fullStartPos = position;
        tokenStart = position;
        token = SyntaxKind.Unknown;
        tokenValue = null!;
        tokenFlags = TokenFlags.None;
    }

    internal void SetSkipJsDocLeadingAsterisks(bool skip)
    {
        skipJsDocLeadingAsterisks += skip ? 1 : -1;
    }
}
