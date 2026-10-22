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
    private string? tokenValue;
    private TokenFlags tokenFlags;

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

    private Scanner(ScriptTarget languageVersion, bool skipTrivia, LanguageVariant languageVariant, string? textInitial, ErrorCallback? onError, int? start, int? length)
    {
        this.languageVersion = languageVersion;
        this.skipTrivia = skipTrivia;
        this.languageVariant = languageVariant;
        this.onError = onError;
        this.start = start;
        text = textInitial!;
        SetText(text, this.start, length);
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
            var text = GetText();
            return string.Concat(text.AsSpan(0, GetTokenFullStart()), "║", text.AsSpan(GetTokenFullStart()));
        }
    }

    //// The members of the returned object (interface Scanner).

    internal int GetTokenFullStart() => fullStartPos;

    internal int GetStartPos() => fullStartPos;

    internal int GetTokenEnd() => pos;

    internal int GetTextPos() => pos;

    internal SyntaxKind GetToken() => token;

    internal int GetTokenStart() => tokenStart;

    internal int GetTokenPos() => tokenStart;

    internal string GetTokenText() => Substring(text, tokenStart, pos);

    internal string GetTokenValue() => tokenValue!;

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

    //// The inner functions of createScanner.

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
        return pos >= 0 && pos < end ? CodePointUnchecked(pos) : (int)CharacterCodes.EOF;
    }

    /// <summary>
    /// Returns the char code for the character at the given position within `text`. This
    /// should only be used when pos is guaranteed to be within the bounds of `text` as this
    /// function does not perform bounds checks.
    /// (Outside the string, `text.charCodeAt` returns NaN; see <see cref="NaN"/>.)
    /// </summary>
    private int CharCodeUnchecked(int pos)
    {
        return (uint)pos < (uint)text.Length ? text[pos] : NaN;
    }

    /// <summary>
    /// Returns the char code for the character at the given position within `text`. If
    /// `pos` is outside the bounds set for `text`, `CharacterCodes.EOF` is returned instead.
    /// </summary>
    private int CharCodeChecked(int pos)
    {
        return pos >= 0 && pos < end ? CharCodeUnchecked(pos) : (int)CharacterCodes.EOF;
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
        var result = new StringParts(); // TypeScript: `let result = ""` (see StringParts)
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
    private SyntaxKind ScanNumber()
    {
        var start = pos;
        string mainFragment;
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
                mainFragment = JsNumber.ToStringOfNumericString(tokenValue!);
            }
            else if (string.IsNullOrEmpty(tokenValue))
            {
                // a single zero
                mainFragment = "0";
            }
            else
            {
                // LegacyOctalIntegerLiteral
                tokenValue = JsNumber.ToString(JsNumber.ParseInt(tokenValue, 8));
                tokenFlags |= TokenFlags.Octal;
                var withMinus = token == SyntaxKind.MinusToken;
                var literal = (withMinus ? "-" : "") + "0o" + JsNumber.ToRadixString(JsNumber.FromString(tokenValue), 8);
                if (withMinus)
                {
                    start--;
                }
                Error(Diagnostics.Octal_literals_are_not_allowed_Use_the_syntax_0, start, pos - start, literal);
                return SyntaxKind.NumericLiteral;
            }
        }
        else
        {
            mainFragment = ScanNumberFragment();
        }
        string? decimalFragment = null;
        string? scientificFragment = null;
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
            Error(Diagnostics.Decimals_with_leading_zeros_are_not_allowed, start, numberEnd - start);
            // if a literal has a leading zero, it must not be bigint
            tokenValue = JsNumber.ToStringOfNumericString(result);
            return SyntaxKind.NumericLiteral;
        }

        if (decimalFragment != null || (tokenFlags & TokenFlags.Scientific) != 0)
        {
            CheckForIdentifierStartAfterNumericLiteral(start, decimalFragment == null && (tokenFlags & TokenFlags.Scientific) != 0);
            // if value is not an integer, it can be safely coerced to a number
            tokenValue = JsNumber.ToStringOfNumericString(result);
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
        return valueString.Length != 0 ? ParseHexValue(valueString) : -1;
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
        var valueChars = new StringBuilder();
        var allowSeparator = false;
        var isPreviousTokenSeparator = false;
        while (valueChars.Length < minCount || scanAsManyAsPossible)
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
            if (ch >= (int)CharacterCodes.A && ch <= (int)CharacterCodes.F)
            {
                ch += CharacterCodes.a - CharacterCodes.A; // standardize hex literals to lowercase
            }
            else if (
                !((ch >= (int)CharacterCodes._0 && ch <= (int)CharacterCodes._9) ||
                    (ch >= (int)CharacterCodes.a && ch <= (int)CharacterCodes.f))
            )
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
        if (CharCodeUnchecked(pos - 1) == (int)CharacterCodes._)
        {
            Error(Diagnostics.Numeric_separators_are_not_allowed_here, pos - 1, 1);
        }
        return valueChars.ToString();
    }

    private string ScanString(bool jsxAttributeString = false)
    {
        var quote = CharCodeUnchecked(pos);
        pos++;
        var result = new StringParts(); // TypeScript: `let result = ""` (see StringParts)
        var start = pos;
        while (true)
        {
            // typescript-go's scanString (internal/scanner/scanner.go) takes a fast path over the characters none of
            // the tests below handle; for each of them the loop below would only do `pos++`.
            while (pos < end && !IsStringSpecialCharacter(text[pos], quote))
            {
                pos++;
            }

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
                result.Append(ScanEscapeSequence(EscapeSequenceScanningFlags.String | EscapeSequenceScanningFlags.ReportErrors));
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
    private SyntaxKind ScanTemplateAndSetTokenValue(bool shouldEmitInvalidEscapeError)
    {
        var startedWithBacktick = CharCodeUnchecked(pos) == (int)CharacterCodes.backtick;

        pos++;
        var start = pos;
        var contents = new StringParts(); // TypeScript: `let contents = ""` (see StringParts)
        SyntaxKind resultingToken;

        while (true)
        {
            // typescript-go's scanTemplateAndSetTokenValue (internal/scanner/scanner.go) first skips the characters
            // that none of the tests below handle (`scanASCIIWhile(b != '`' && b != '$' && b != '\\' && b != '\r')`);
            // for each of them the loop below would only do `pos++`.
            while (pos < end && !IsTemplateSpecialCharacter(text[pos]))
            {
                pos++;
            }

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
                contents.Append(ScanEscapeSequence(EscapeSequenceScanningFlags.String | (shouldEmitInvalidEscapeError ? EscapeSequenceScanningFlags.ReportErrors : 0)));
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
                    // of the text lets StringParts merge it with the characters that follow (one part per line).
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
    private string ScanEscapeSequence(EscapeSequenceScanningFlags flags)
    {
        var start = pos;
        pos++;
        if (pos >= end)
        {
            Error(Diagnostics.Unexpected_end_of_text);
            return "";
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
                    return "\0";
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
                    var code = (int)JsNumber.ParseInt(Substring(text, start + 1, pos), 8);
                    if ((flags & EscapeSequenceScanningFlags.RegularExpression) != 0 && (flags & EscapeSequenceScanningFlags.AtomEscape) == 0 && ch != (int)CharacterCodes._0)
                    {
                        Error(Diagnostics.Octal_escape_sequences_and_backreferences_are_not_allowed_in_a_character_class_If_this_was_intended_as_an_escape_sequence_use_the_syntax_0_instead, start, pos - start, "\\x" + code.ToString("x", CultureInfo.InvariantCulture).PadLeft(2, '0'));
                    }
                    else
                    {
                        Error(Diagnostics.Octal_escape_sequences_are_not_allowed_Use_the_syntax_0, start, pos - start, "\\x" + code.ToString("x", CultureInfo.InvariantCulture).PadLeft(2, '0'));
                    }
                    return FromCharCode(code);
                }
                return Substring(text, start, pos);
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
                        Error(Diagnostics.Escape_sequence_0_is_not_allowed, start, pos - start, Substring(text, start, pos));
                    }
                    return FromCharCode(ch);
                }
                return Substring(text, start, pos);
            case (int)CharacterCodes.b:
                return "\b";
            case (int)CharacterCodes.t:
                return "\t";
            case (int)CharacterCodes.n:
                return "\n";
            case (int)CharacterCodes.v:
                return "\v";
            case (int)CharacterCodes.f:
                return "\f";
            case (int)CharacterCodes.r:
                return "\r";
            case (int)CharacterCodes.singleQuote:
                return "'";
            case (int)CharacterCodes.doubleQuote:
                return "\"";
            case (int)CharacterCodes.u:
                if (pos < end && CharCodeUnchecked(pos) == (int)CharacterCodes.openBrace)
                {
                    // '\u{DDDDDD}'
                    pos -= 2;
                    var result = ScanExtendedUnicodeEscape((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0);
                    if ((flags & EscapeSequenceScanningFlags.AllowExtendedUnicodeEscape) == 0)
                    {
                        tokenFlags |= TokenFlags.ContainsInvalidEscape;
                        if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                        {
                            Error(Diagnostics.Unicode_escape_sequences_are_only_available_when_the_Unicode_u_flag_or_the_Unicode_Sets_v_flag_is_set, start, pos - start);
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
                            Error(Diagnostics.Hexadecimal_digit_expected);
                        }
                        return Substring(text, start, pos);
                    }
                }
                tokenFlags |= TokenFlags.UnicodeEscape;
                var escapedValue = ParseHexValue(Substring(text, start + 2, pos));
                var escapedValueString = FromCharCode(escapedValue);
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
                            return escapedValueString;
                        }
                    }
                    var nextEscapedValue = ParseHexValue(Substring(text, nextStart + 2, nextPos));
                    if (nextEscapedValue >= 0xDC00 && nextEscapedValue <= 0xDFFF)
                    {
                        pos = nextPos;
                        return escapedValueString + FromCharCode(nextEscapedValue);
                    }
                }
                return escapedValueString;

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
                        return Substring(text, start, pos);
                    }
                }
                tokenFlags |= TokenFlags.HexEscape;
                return FromCharCode(ParseHexValue(Substring(text, start + 2, pos)));

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
                return "";
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
                return FromCharCode(ch);
        }
    }

    private string ScanExtendedUnicodeEscape(bool shouldEmitInvalidEscapeError)
    {
        var start = pos;
        pos += 3;
        var escapedStart = pos;
        var escapedValueString = ScanMinimumNumberOfHexDigits(1, /*canHaveSeparators*/ false);
        var escapedValue = escapedValueString.Length != 0 ? ParseHexValue(escapedValueString) : -1;
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
            return Substring(text, start, pos);
        }

        tokenFlags |= TokenFlags.ExtendedUnicodeEscape;
        return Utf16EncodeAsString(escapedValue);
    }

    // Current character is known to be a backslash. Check for Unicode escape of the form '\uXXXX'
    // and return code point value if valid Unicode escape is found. Otherwise return -1.
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

    private int PeekExtendedUnicodeEscape()
    {
        if (CodePointUnchecked(pos + 1) == (int)CharacterCodes.u && CodePointUnchecked(pos + 2) == (int)CharacterCodes.openBrace)
        {
            var start = pos;
            pos += 3;
            var escapedValueString = ScanMinimumNumberOfHexDigits(1, /*canHaveSeparators*/ false);
            var escapedValue = escapedValueString.Length != 0 ? ParseHexValue(escapedValueString) : -1;
            pos = start;
            return escapedValue;
        }
        return -1;
    }

    private string ScanIdentifierParts()
    {
        var result = new StringParts(); // TypeScript: `let result = ""` (see StringParts)
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
                    result.Append(ScanExtendedUnicodeEscape(/*shouldEmitInvalidEscapeError*/ true));
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
                result.Append(Utf16EncodeAsString(ch));
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
        return result.Result();
    }

    private SyntaxKind GetIdentifierToken()
    {
        // Reserved words are between 2 and 12 characters long and start with a lowercase letter
        var len = tokenValue!.Length;
        if (len >= 2 && len <= 12)
        {
            int ch = tokenValue[0];
            if (ch >= (int)CharacterCodes.a && ch <= (int)CharacterCodes.z)
            {
                if (TextToKeyword.TryGetValue(tokenValue, out var keyword))
                {
                    return token = keyword;
                }
            }
        }
        return token = SyntaxKind.Identifier;
    }

    private string ScanBinaryOrOctalDigits(int @base)
    {
        var value = new StringParts(); // TypeScript: `let value = ""` (see StringParts)
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

    private SyntaxKind CheckBigIntSuffix()
    {
        if (CharCodeUnchecked(pos) == (int)CharacterCodes.n)
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
            if ((tokenFlags & (TokenFlags.BinarySpecifier | TokenFlags.OctalSpecifier)) == 0)
            {
                // `"" + +tokenValue` (JsNumber.ToStringOfNumericString keeps an already canonical integer string).
                tokenValue = JsNumber.ToStringOfNumericString(tokenValue!);
                return SyntaxKind.NumericLiteral;
            }
            var numericValue = (tokenFlags & TokenFlags.BinarySpecifier) != 0
                ? JsNumber.ParseInt(tokenValue!.Substring(2), 2) // skip "0b"
                : JsNumber.ParseInt(tokenValue!.Substring(2), 8); // skip "0o"
            tokenValue = JsNumber.ToString(numericValue);
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
                        while (pos < end && IsAsciiWhiteSpaceOrLineBreak(CharCodeUnchecked(pos)))
                        {
                            pos++;
                        }
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
                            CommentDirectiveRegExSingleLine,
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
                    if (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.asterisk)
                    {
                        pos += 2;
                        var isJSDoc = CharCodeUnchecked(pos) == (int)CharacterCodes.asterisk && CharCodeUnchecked(pos + 1) != (int)CharacterCodes.slash;

                        var commentClosed = false;
                        var lastLineStart = tokenStart;
                        while (pos < end)
                        {
                            var ch2 = CharCodeUnchecked(pos);

                            if (ch2 == (int)CharacterCodes.asterisk && CharCodeUnchecked(pos + 1) == (int)CharacterCodes.slash)
                            {
                                pos += 2;
                                commentClosed = true;
                                break;
                            }

                            pos++;

                            if (IsLineBreak(ch2))
                            {
                                lastLineStart = pos;
                                tokenFlags |= TokenFlags.PrecedingLineBreak;
                            }
                        }

                        if (isJSDoc && ShouldParseJSDoc())
                        {
                            tokenFlags |= TokenFlags.PrecedingJSDocComment;
                        }

                        commentDirectives = AppendIfCommentDirective(commentDirectives, lastLineStart, pos, CommentDirectiveRegExMultiLine, lastLineStart);

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
                        tokenValue = ScanMinimumNumberOfHexDigits(1, /*canHaveSeparators*/ true);
                        if (tokenValue.Length == 0)
                        {
                            Error(Diagnostics.Hexadecimal_digit_expected);
                            tokenValue = "0";
                        }
                        tokenValue = "0x" + tokenValue;
                        tokenFlags |= TokenFlags.HexSpecifier;
                        return token = CheckBigIntSuffix();
                    }
                    else if (pos + 2 < end && (CharCodeUnchecked(pos + 1) == (int)CharacterCodes.B || CharCodeUnchecked(pos + 1) == (int)CharacterCodes.b))
                    {
                        pos += 2;
                        tokenValue = ScanBinaryOrOctalDigits(/* base */ 2);
                        if (tokenValue.Length == 0)
                        {
                            Error(Diagnostics.Binary_digit_expected);
                            tokenValue = "0";
                        }
                        tokenValue = "0b" + tokenValue;
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
                            tokenValue = "0";
                        }
                        tokenValue = "0o" + tokenValue;
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
                        pos = ScanConflictMarkerTrivia(text, pos, (message, errPos, length) => Error(message, errPos, length));
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
                        pos = ScanConflictMarkerTrivia(text, pos, (message, errPos, length) => Error(message, errPos, length));
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
                        pos = ScanConflictMarkerTrivia(text, pos, (message, errPos, length) => Error(message, errPos, length));
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
                        pos = ScanConflictMarkerTrivia(text, pos, (message, errPos, length) => Error(message, errPos, length));
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
                            tokenValue = ScanExtendedUnicodeEscape(/*shouldEmitInvalidEscapeError*/ true) + ScanIdentifierParts();
                            return token = GetIdentifierToken();
                        }

                        var cookedChar = PeekUnicodeEscape();
                        if (cookedChar >= 0 && IsIdentifierStart(cookedChar, languageVersion))
                        {
                            pos += 6;
                            tokenFlags |= TokenFlags.UnicodeEscape;
                            tokenValue = FromCharCode(cookedChar) + ScanIdentifierParts();
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
                                tokenValue = "#" + ScanExtendedUnicodeEscape(/*shouldEmitInvalidEscapeError*/ true) + ScanIdentifierParts();
                                return token = SyntaxKind.PrivateIdentifier;
                            }

                            var cookedChar = PeekUnicodeEscape();
                            if (cookedChar >= 0 && IsIdentifierStart(cookedChar, languageVersion))
                            {
                                pos += 6;
                                tokenFlags |= TokenFlags.UnicodeEscape;
                                tokenValue = "#" + FromCharCode(cookedChar) + ScanIdentifierParts();
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
            // Fast path for simple ASCII identifiers, as typescript-go's scanIdentifier (internal/scanner/scanner.go):
            // when the identifier consists of ASCII identifier characters and ends at the end or at an ASCII
            // character other than '\', the loop below would stop at the same position and take the same branch.
            if (ch <= (int)CharacterCodes.maxAsciiCharacter)
            {
                var asciiEnd = pos;
                while (asciiEnd < end && IsAsciiIdentifierPart(text[asciiEnd]))
                {
                    asciiEnd++;
                }
                if (asciiEnd >= end || (text[asciiEnd] <= (int)CharacterCodes.maxAsciiCharacter && text[asciiEnd] != (int)CharacterCodes.backslash))
                {
                    pos = asciiEnd;
                    return GetIdentifierTokenOfRange(tokenStart, pos);
                }
            }
            while (pos < end && IsIdentifierPart(ch = CodePointUnchecked(pos), languageVersion))
            {
                pos += CharSize(ch);
            }
            if (ch == (int)CharacterCodes.backslash)
            {
                tokenValue = Substring(text, tokenStart, pos);
                tokenValue += ScanIdentifierParts();
                return GetIdentifierToken();
            }
            // TypeScript: `tokenValue = text.substring(tokenStart, pos); return getIdentifierToken();`.
            // typescript-go takes the value as a slice of the text, which copies nothing
            // (`s.tokenValue = s.text[start:s.pos]`, scanIdentifier in internal/scanner/scanner.go); here the
            // keyword lookup of getIdentifierToken runs on the range, and a keyword's value is the keyword table's
            // string (the same text), so that only identifiers allocate their value.
            return GetIdentifierTokenOfRange(tokenStart, pos);
        }
        return null;
    }

    // The characters scanString tests for: the quote, '\', LF, CR.
    private static bool IsStringSpecialCharacter(int ch, int quote)
    {
        return ch == quote || ch == (int)CharacterCodes.backslash || ch == (int)CharacterCodes.lineFeed || ch == (int)CharacterCodes.carriageReturn;
    }

    // The characters scanTemplateAndSetTokenValue tests for: '`', '$', '\', CR.
    private static bool IsTemplateSpecialCharacter(int ch)
    {
        return ch == (int)CharacterCodes.backtick || ch == (int)CharacterCodes.Dollar || ch == (int)CharacterCodes.backslash || ch == (int)CharacterCodes.carriageReturn;
    }

    // ' ', '\t', '\n', '\v', '\f', '\r' (typescript-go: `b == ' ' || (b >= '\t' && b <= '\r')`).
    private static bool IsAsciiWhiteSpaceOrLineBreak(int ch)
    {
        return ch == (int)CharacterCodes.space || (ch >= (int)CharacterCodes.tab && ch <= (int)CharacterCodes.carriageReturn);
    }

    // isIdentifierPart for an ASCII character (any language version and variant other than JSX): [A-Za-z0-9_$].
    private static bool IsAsciiIdentifierPart(int ch)
    {
        return IsWordCharacter(ch) || ch == (int)CharacterCodes.Dollar;
    }

    // getIdentifierToken for tokenValue = text[start..end), see ScanIdentifier.
    private SyntaxKind GetIdentifierTokenOfRange(int start, int end)
    {
        // Reserved words are between 2 and 12 characters long and start with a lowercase letter
        var len = end - start;
        if (len >= 2 && len <= 12)
        {
            int ch = text[start];
            if (ch >= (int)CharacterCodes.a && ch <= (int)CharacterCodes.z)
            {
                if (TextToKeywordByRange.TryGetValue(text.AsSpan(start, len), out var keywordText, out var keyword))
                {
                    tokenValue = keywordText;
                    return token = keyword;
                }
            }
        }
        tokenValue = Substring(text, start, end);
        return token = SyntaxKind.Identifier;
    }

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
                    ScanRange<object?>(startOfRegExpBody, endOfRegExpBody - startOfRegExpBody, () =>
                    {
                        ScanRegularExpressionWorker(regExpFlags, /*annexB*/ true, namedCaptureGroups);
                        return null;
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
        if (RegExpFlagToFirstAvailableLanguageVersion.TryGetValue(flag, out var availableFrom) && availableFrom != 0 && languageVersion < availableFrom)
        {
            Error(Diagnostics.This_regular_expression_flag_is_only_available_when_targeting_0_or_later, pos, size, Utilities.GetNameOfScriptTarget(availableFrom));
        }
    }

    // TypeScript passes the comment's text (`text.slice(start, end)`) and a RegExp; this passes the range of
    // the text instead of slicing it, as typescript-go's processCommentDirective (internal/scanner/scanner.go)
    // works on positions, and the hand-written matcher of the RegExp.
    private List<CommentDirective>? AppendIfCommentDirective(
        List<CommentDirective>? commentDirectives,
        int textStart,
        int textEnd,
        Func<string, int, int, string?> commentDirectiveRegEx,
        int lineStart)
    {
        // `text.trimStart()`
        textEnd = Math.Min(textEnd, text.Length);
        while (textStart < textEnd && IsRegExpWhiteSpace(text[textStart]))
        {
            textStart++;
        }
        var type = GetDirectiveFromComment(textStart, textEnd, commentDirectiveRegEx);
        if (type == null)
        {
            return commentDirectives;
        }

        return Core.Append(
            commentDirectives,
            new CommentDirective
            {
                Range = new ObjectTextRange { Pos = lineStart, End = pos },
                Type = type.Value,
            });
    }

    private CommentDirectiveType? GetDirectiveFromComment(int textStart, int textEnd, Func<string, int, int, string?> commentDirectiveRegEx)
    {
        var match = commentDirectiveRegEx(text, textStart, textEnd);
        if (match == null)
        {
            return null;
        }

        switch (match)
        {
            case "ts-expect-error":
                return CommentDirectiveType.ExpectError;

            case "ts-ignore":
                return CommentDirectiveType.Ignore;
        }

        return null;
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
                    pos = ScanConflictMarkerTrivia(text, pos, (message, errPos, length) => Error(message, errPos, length));
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
                if (ch == (int)CharacterCodes.minus)
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
            case (int)CharacterCodes.doubleQuote:
            case (int)CharacterCodes.singleQuote:
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
                    tokenValue = ScanExtendedUnicodeEscape(/*shouldEmitInvalidEscapeError*/ true) + ScanIdentifierParts();
                    return token = GetIdentifierToken();
                }

                var cookedChar = PeekUnicodeEscape();
                if (cookedChar >= 0 && IsIdentifierStart(cookedChar, languageVersion))
                {
                    pos += 6;
                    tokenFlags |= TokenFlags.UnicodeEscape;
                    tokenValue = FromCharCode(cookedChar) + ScanIdentifierParts();
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
            tokenValue = Substring(text, tokenStart, pos);
            if (chr == (int)CharacterCodes.backslash)
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

    internal T LookAhead<T>(Func<T> callback)
    {
        return SpeculationHelper(callback, /*isLookahead*/ true);
    }

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

    internal void SetText(string? newText, int? start = null, int? length = null)
    {
        text = !string.IsNullOrEmpty(newText) ? newText : "";
        end = length == null ? text.Length : start!.Value + length.Value;
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

    /// <summary>scanner.ts: `setTextPos: resetTokenState` (deprecated alias).</summary>
    internal void SetTextPos(int textPos)
    {
        ResetTokenState(textPos);
    }

    internal void SetSkipJsDocLeadingAsterisks(bool skip)
    {
        skipJsDocLeadingAsterisks += skip ? 1 : -1;
    }

    /// <summary>
    /// `parseInt(hexDigits, 16)` for the scanner's escape sequences: the digits are valid hexadecimal digits;
    /// values beyond int.MaxValue (only possible in `\u{...}` with many digits) are kept above 0x10FFFF, which
    /// is all the scanner tests about them.
    /// </summary>
    private static int ParseHexValue(string hexDigits)
    {
        var value = JsNumber.ParseInt(hexDigits, 16);
        return value > int.MaxValue ? int.MaxValue : (int)value;
    }
}
