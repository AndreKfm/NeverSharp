// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/scanner.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/scanner/regexp.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>scanner.ts: createScanner's scanRegularExpressionWorker (the regular expression validator).</summary>
internal sealed partial class Scanner
{
    /// <summary>A reference to a named capturing group: `TextRange &amp; { name: string; }`.</summary>
    private sealed class GroupNameReference : TextRange
    {
        public int Pos { get; set; }

        public int End { get; set; }

        public required string Name { get; init; }
    }

    /// <summary>A numeric backreference: `TextRange &amp; { value: number; }`.</summary>
    private sealed class DecimalEscape : TextRange
    {
        public int Pos { get; set; }

        public int End { get; set; }

        public double Value { get; init; }
    }

    // The nested functions of scanRegularExpressionWorker are local functions over its variables, as in TypeScript.
    private void ScanRegularExpressionWorker(RegularExpressionFlags regExpFlags, bool annexB, bool namedCaptureGroups)
    {
        /** Grammar parameter */
        var unicodeSetsMode = (regExpFlags & RegularExpressionFlags.UnicodeSets) != 0;
        /** Grammar parameter */
        var anyUnicodeMode = (regExpFlags & RegularExpressionFlags.AnyUnicodeMode) != 0;

        // Regular expressions are checked more strictly when either in 'u' or 'v' mode, or
        // when not using the looser interpretation of the syntax from ECMA-262 Annex B.
        var anyUnicodeModeOrNonAnnexB = anyUnicodeMode || !annexB;

        /** @see {scanClassSetExpression} */
        var mayContainStrings = false;

        /** The number of all (named and unnamed) capturing groups defined in the regex. */
        var numberOfCapturingGroups = 0;
        /** All named capturing groups defined in the regex. */
        OrderedStringSet? groupSpecifiers = null;
        /** All references to named capturing groups in the regex. */
        List<GroupNameReference>? groupNameReferences = null;
        /** All numeric backreferences within the regex. */
        List<DecimalEscape>? decimalEscapes = null;
        /** A stack of scopes for named capturing groups. @see {scanGroupName} */
        var namedCapturingGroupsScopeStack = new List<OrderedStringSet?>();
        OrderedStringSet? topNamedCapturingGroupsScope = null;

        // Disjunction ::= Alternative ('|' Alternative)*
        void ScanDisjunction(bool isInGroup)
        {
            while (true)
            {
                namedCapturingGroupsScopeStack.Add(topNamedCapturingGroupsScope);
                topNamedCapturingGroupsScope = null;
                ScanAlternative(isInGroup);
                topNamedCapturingGroupsScope = Pop(namedCapturingGroupsScopeStack);
                if (CharCodeChecked(pos) != (int)CharacterCodes.bar)
                {
                    return;
                }
                pos++;
            }
        }

        // Alternative ::= Term*
        // Term ::=
        //     | Assertion
        //     | Atom Quantifier?
        // Assertion ::=
        //     | '^'
        //     | '$'
        //     | '\b'
        //     | '\B'
        //     | '(?=' Disjunction ')'
        //     | '(?!' Disjunction ')'
        //     | '(?<=' Disjunction ')'
        //     | '(?<!' Disjunction ')'
        // Quantifier ::= QuantifierPrefix '?'?
        // QuantifierPrefix ::=
        //     | '*'
        //     | '+'
        //     | '?'
        //     | '{' DecimalDigits (',' DecimalDigits?)? '}'
        // Atom ::=
        //     | PatternCharacter
        //     | '.'
        //     | '\' AtomEscape
        //     | CharacterClass
        //     | '(?<' RegExpIdentifierName '>' Disjunction ')'
        //     | '(?' RegularExpressionFlags ('-' RegularExpressionFlags)? ':' Disjunction ')'
        // CharacterClass ::= unicodeMode
        //     ? '[' ClassRanges ']'
        //     : '[' ClassSetExpression ']'
        void ScanAlternative(bool isInGroup)
        {
            var isPreviousTermQuantifiable = false;
            while (true)
            {
                var start = pos;
                var ch = CharCodeChecked(pos);
                switch (ch)
                {
                    case (int)CharacterCodes.EOF:
                        return;
                    case (int)CharacterCodes.caret:
                    case (int)CharacterCodes.Dollar:
                        pos++;
                        isPreviousTermQuantifiable = false;
                        break;
                    case (int)CharacterCodes.backslash:
                        pos++;
                        switch (CharCodeChecked(pos))
                        {
                            case (int)CharacterCodes.b:
                            case (int)CharacterCodes.B:
                                pos++;
                                isPreviousTermQuantifiable = false;
                                break;
                            default:
                                ScanAtomEscape();
                                isPreviousTermQuantifiable = true;
                                break;
                        }
                        break;
                    case (int)CharacterCodes.openParen:
                        pos++;
                        if (CharCodeChecked(pos) == (int)CharacterCodes.question)
                        {
                            pos++;
                            switch (CharCodeChecked(pos))
                            {
                                case (int)CharacterCodes.equals:
                                case (int)CharacterCodes.exclamation:
                                    pos++;
                                    // In Annex B, `(?=Disjunction)` and `(?!Disjunction)` are quantifiable
                                    isPreviousTermQuantifiable = !anyUnicodeModeOrNonAnnexB;
                                    break;
                                case (int)CharacterCodes.lessThan:
                                    var groupNameStart = pos;
                                    pos++;
                                    switch (CharCodeChecked(pos))
                                    {
                                        case (int)CharacterCodes.equals:
                                        case (int)CharacterCodes.exclamation:
                                            pos++;
                                            isPreviousTermQuantifiable = false;
                                            break;
                                        default:
                                            ScanGroupName(/*isReference*/ false);
                                            ScanExpectedChar((int)CharacterCodes.greaterThan);
                                            if (languageVersion < ScriptTarget.ES2018)
                                            {
                                                Error(Diagnostics.Named_capturing_groups_are_only_available_when_targeting_ES2018_or_later, groupNameStart, pos - groupNameStart);
                                            }
                                            numberOfCapturingGroups++;
                                            isPreviousTermQuantifiable = true;
                                            break;
                                    }
                                    break;
                                default:
                                    // TypeScript declares another `start` in this block (shadowing the term's start).
                                    var modifiersStart = pos;
                                    var setFlags = ScanPatternModifiers(RegularExpressionFlags.None);
                                    if (CharCodeChecked(pos) == (int)CharacterCodes.minus)
                                    {
                                        pos++;
                                        ScanPatternModifiers(setFlags);
                                        if (pos == modifiersStart + 1)
                                        {
                                            Error(Diagnostics.Subpattern_flags_must_be_present_when_there_is_a_minus_sign, modifiersStart, pos - modifiersStart);
                                        }
                                    }
                                    ScanExpectedChar((int)CharacterCodes.colon);
                                    isPreviousTermQuantifiable = true;
                                    break;
                            }
                        }
                        else
                        {
                            numberOfCapturingGroups++;
                            isPreviousTermQuantifiable = true;
                        }
                        ScanDisjunction(/*isInGroup*/ true);
                        ScanExpectedChar((int)CharacterCodes.closeParen);
                        break;
                    case (int)CharacterCodes.openBrace:
                        {
                            pos++;
                            var digitsStart = pos;
                            ScanDigits();
                            var min = GetTokenValue();
                            if (!anyUnicodeModeOrNonAnnexB && min.Length == 0)
                            {
                                isPreviousTermQuantifiable = true;
                                break;
                            }
                            if (CharCodeChecked(pos) == (int)CharacterCodes.comma)
                            {
                                pos++;
                                ScanDigits();
                                var max = GetTokenValue();
                                if (min.Length == 0)
                                {
                                    if (max.Length != 0 || CharCodeChecked(pos) == (int)CharacterCodes.closeBrace)
                                    {
                                        Error(Diagnostics.Incomplete_quantifier_Digit_expected, digitsStart, 0);
                                    }
                                    else
                                    {
                                        Error(Diagnostics.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, start, 1, FromCharCode(ch));
                                        isPreviousTermQuantifiable = true;
                                        break;
                                    }
                                }
                                else if (max.Length != 0 && JsNumber.ParseInt(min, 10) > JsNumber.ParseInt(max, 10) && (anyUnicodeModeOrNonAnnexB || CharCodeChecked(pos) == (int)CharacterCodes.closeBrace))
                                {
                                    Error(Diagnostics.Numbers_out_of_order_in_quantifier, digitsStart, pos - digitsStart);
                                }
                            }
                            else if (min.Length == 0)
                            {
                                if (anyUnicodeModeOrNonAnnexB)
                                {
                                    Error(Diagnostics.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, start, 1, FromCharCode(ch));
                                }
                                isPreviousTermQuantifiable = true;
                                break;
                            }
                            if (CharCodeChecked(pos) != (int)CharacterCodes.closeBrace)
                            {
                                if (anyUnicodeModeOrNonAnnexB)
                                {
                                    Error(Diagnostics._0_expected, pos, 0, FromCharCode((int)CharacterCodes.closeBrace));
                                    pos--;
                                }
                                else
                                {
                                    isPreviousTermQuantifiable = true;
                                    break;
                                }
                            }
                            goto case (int)CharacterCodes.asterisk;
                        }
                    case (int)CharacterCodes.asterisk:
                    case (int)CharacterCodes.plus:
                    case (int)CharacterCodes.question:
                        pos++;
                        if (CharCodeChecked(pos) == (int)CharacterCodes.question)
                        {
                            // Non-greedy
                            pos++;
                        }
                        if (!isPreviousTermQuantifiable)
                        {
                            Error(Diagnostics.There_is_nothing_available_for_repetition, start, pos - start);
                        }
                        isPreviousTermQuantifiable = false;
                        break;
                    case (int)CharacterCodes.dot:
                        pos++;
                        isPreviousTermQuantifiable = true;
                        break;
                    case (int)CharacterCodes.openBracket:
                        pos++;
                        if (unicodeSetsMode)
                        {
                            ScanClassSetExpression();
                        }
                        else
                        {
                            ScanClassRanges();
                        }
                        ScanExpectedChar((int)CharacterCodes.closeBracket);
                        isPreviousTermQuantifiable = true;
                        break;
                    case (int)CharacterCodes.closeParen:
                        if (isInGroup)
                        {
                            return;
                        }
                        goto case (int)CharacterCodes.closeBracket;
                    case (int)CharacterCodes.closeBracket:
                    case (int)CharacterCodes.closeBrace:
                        if (anyUnicodeModeOrNonAnnexB || ch == (int)CharacterCodes.closeParen)
                        {
                            Error(Diagnostics.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, pos, 1, FromCharCode(ch));
                        }
                        pos++;
                        isPreviousTermQuantifiable = true;
                        break;
                    case (int)CharacterCodes.slash:
                    case (int)CharacterCodes.bar:
                        return;
                    default:
                        ScanSourceCharacter();
                        isPreviousTermQuantifiable = true;
                        break;
                }
            }
        }

        RegularExpressionFlags ScanPatternModifiers(RegularExpressionFlags currFlags)
        {
            while (true)
            {
                var ch = CodePointChecked(pos);
                if (ch == (int)CharacterCodes.EOF || !IsIdentifierPart(ch, languageVersion))
                {
                    break;
                }
                var size = CharSize(ch);
                var flag = CharacterCodeToRegularExpressionFlag(ch);
                if (flag == null)
                {
                    Error(Diagnostics.Unknown_regular_expression_flag, pos, size);
                }
                else if ((currFlags & flag.Value) != 0)
                {
                    Error(Diagnostics.Duplicate_regular_expression_flag, pos, size);
                }
                else if ((flag.Value & RegularExpressionFlags.Modifiers) == 0)
                {
                    Error(Diagnostics.This_regular_expression_flag_cannot_be_toggled_within_a_subpattern, pos, size);
                }
                else
                {
                    currFlags |= flag.Value;
                    CheckRegularExpressionFlagAvailability(flag.Value, size);
                }
                pos += size;
            }
            return currFlags;
        }

        // AtomEscape ::=
        //     | DecimalEscape
        //     | CharacterClassEscape
        //     | CharacterEscape
        //     | 'k<' RegExpIdentifierName '>'
        void ScanAtomEscape()
        {
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), (int)CharacterCodes.backslash);
            switch (CharCodeChecked(pos))
            {
                case (int)CharacterCodes.k:
                    pos++;
                    if (CharCodeChecked(pos) == (int)CharacterCodes.lessThan)
                    {
                        pos++;
                        ScanGroupName(/*isReference*/ true);
                        ScanExpectedChar((int)CharacterCodes.greaterThan);
                    }
                    else if (anyUnicodeModeOrNonAnnexB || namedCaptureGroups)
                    {
                        Error(Diagnostics.k_must_be_followed_by_a_capturing_group_name_enclosed_in_angle_brackets, pos - 2, 2);
                    }
                    break;
                case (int)CharacterCodes.q:
                    if (unicodeSetsMode)
                    {
                        pos++;
                        Error(Diagnostics.q_is_only_available_inside_character_class, pos - 2, 2);
                        break;
                    }
                    goto default;
                default:
                    // The scanEscapeSequence call in scanCharacterEscape must return non-empty strings
                    // since there must not be line breaks in a regex literal
                    Debug.Assert(ScanCharacterClassEscape() || ScanDecimalEscape() || ScanCharacterEscape(/*atomEscape*/ true).Length != 0);
                    break;
            }
        }

        // DecimalEscape ::= [1-9] [0-9]*
        bool ScanDecimalEscape()
        {
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), (int)CharacterCodes.backslash);
            var ch = CharCodeChecked(pos);
            if (ch >= (int)CharacterCodes._1 && ch <= (int)CharacterCodes._9)
            {
                var start = pos;
                ScanDigits();
                decimalEscapes = Core.Append(decimalEscapes, new DecimalEscape { Pos = start, End = pos, Value = JsNumber.FromString(GetTokenValue()) });
                return true;
            }
            return false;
        }

        // CharacterEscape ::=
        //     | `c` ControlLetter
        //     | IdentityEscape
        //     | (Other sequences handled by `scanEscapeSequence`)
        // IdentityEscape ::=
        //     | '^' | '$' | '/' | '\' | '.' | '*' | '+' | '?' | '(' | ')' | '[' | ']' | '{' | '}' | '|'
        //     | [~UnicodeMode] (any other non-identifier characters)
        string ScanCharacterEscape(bool atomEscape)
        {
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), (int)CharacterCodes.backslash);
            var ch = CharCodeChecked(pos);
            switch (ch)
            {
                case (int)CharacterCodes.EOF:
                    Error(Diagnostics.Undetermined_character_escape, pos - 1, 1);
                    return "\\";
                case (int)CharacterCodes.c:
                    pos++;
                    ch = CharCodeChecked(pos);
                    if (IsASCIILetter(ch))
                    {
                        pos++;
                        return FromCharCode(ch & 0x1f);
                    }
                    if (anyUnicodeModeOrNonAnnexB)
                    {
                        Error(Diagnostics.c_must_be_followed_by_an_ASCII_letter, pos - 2, 2);
                    }
                    else if (atomEscape)
                    {
                        // Annex B treats
                        //
                        //  ExtendedAtom : `\` [lookahead = `c`]
                        //
                        // as the single character `\` when `c` isn't followed by a valid control character
                        pos--;
                        return "\\";
                    }
                    return FromCharCode(ch);
                case (int)CharacterCodes.caret:
                case (int)CharacterCodes.Dollar:
                case (int)CharacterCodes.slash:
                case (int)CharacterCodes.backslash:
                case (int)CharacterCodes.dot:
                case (int)CharacterCodes.asterisk:
                case (int)CharacterCodes.plus:
                case (int)CharacterCodes.question:
                case (int)CharacterCodes.openParen:
                case (int)CharacterCodes.closeParen:
                case (int)CharacterCodes.openBracket:
                case (int)CharacterCodes.closeBracket:
                case (int)CharacterCodes.openBrace:
                case (int)CharacterCodes.closeBrace:
                case (int)CharacterCodes.bar:
                    pos++;
                    return FromCharCode(ch);
                default:
                    pos--;
                    return ScanEscapeSequenceString(
                        EscapeSequenceScanningFlags.RegularExpression
                            | (annexB ? EscapeSequenceScanningFlags.AnnexB : 0)
                            | (anyUnicodeMode ? EscapeSequenceScanningFlags.AnyUnicodeMode : 0)
                            | (atomEscape ? EscapeSequenceScanningFlags.AtomEscape : 0));
            }
        }

        void ScanGroupName(bool isReference)
        {
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), (int)CharacterCodes.lessThan);
            tokenStart = pos;
            ScanIdentifier(CodePointChecked(pos), languageVersion);
            if (pos == tokenStart)
            {
                Error(Diagnostics.Expected_a_capturing_group_name);
            }
            else if (isReference)
            {
                groupNameReferences = Core.Append(groupNameReferences, new GroupNameReference { Pos = tokenStart, End = pos, Name = GetTokenValue() });
            }
            else if (topNamedCapturingGroupsScope?.Has(GetTokenValue()) == true || SomeScopeHas(namedCapturingGroupsScopeStack, GetTokenValue()))
            {
                Error(Diagnostics.Named_capturing_groups_with_the_same_name_must_be_mutually_exclusive_to_each_other, tokenStart, pos - tokenStart);
            }
            else
            {
                topNamedCapturingGroupsScope ??= new OrderedStringSet();
                topNamedCapturingGroupsScope.Add(GetTokenValue());
                groupSpecifiers ??= new OrderedStringSet();
                groupSpecifiers.Add(GetTokenValue());
            }
        }

        bool IsClassContentExit(int ch)
        {
            return ch == (int)CharacterCodes.closeBracket || ch == (int)CharacterCodes.EOF || pos >= end;
        }

        // ClassRanges ::= '^'? (ClassAtom ('-' ClassAtom)?)*
        void ScanClassRanges()
        {
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), (int)CharacterCodes.openBracket);
            if (CharCodeChecked(pos) == (int)CharacterCodes.caret)
            {
                // character complement
                pos++;
            }
            while (true)
            {
                var ch = CharCodeChecked(pos);
                if (IsClassContentExit(ch))
                {
                    return;
                }
                var minStart = pos;
                var minCharacter = ScanClassAtom();
                if (CharCodeChecked(pos) == (int)CharacterCodes.minus)
                {
                    pos++;
                    var ch2 = CharCodeChecked(pos);
                    if (IsClassContentExit(ch2))
                    {
                        return;
                    }
                    if (minCharacter.Length == 0 && anyUnicodeModeOrNonAnnexB)
                    {
                        Error(Diagnostics.A_character_class_range_must_not_be_bounded_by_another_character_class, minStart, pos - 1 - minStart);
                    }
                    var maxStart = pos;
                    var maxCharacter = ScanClassAtom();
                    if (maxCharacter.Length == 0 && anyUnicodeModeOrNonAnnexB)
                    {
                        Error(Diagnostics.A_character_class_range_must_not_be_bounded_by_another_character_class, maxStart, pos - maxStart);
                        continue;
                    }
                    if (minCharacter.Length == 0)
                    {
                        continue;
                    }
                    var minCharacterValue = CodePointAt(minCharacter, 0);
                    var maxCharacterValue = CodePointAt(maxCharacter, 0);
                    if (
                        minCharacter.Length == CharSize(minCharacterValue) &&
                        maxCharacter.Length == CharSize(maxCharacterValue) &&
                        minCharacterValue > maxCharacterValue
                    )
                    {
                        Error(Diagnostics.Range_out_of_order_in_character_class, minStart, pos - minStart);
                    }
                }
            }
        }

        // Static Semantics: MayContainStrings
        //     ClassUnion: ClassSetOperands.some(ClassSetOperand => ClassSetOperand.MayContainStrings)
        //     ClassIntersection: ClassSetOperands.every(ClassSetOperand => ClassSetOperand.MayContainStrings)
        //     ClassSubtraction: ClassSetOperands[0].MayContainStrings
        //     ClassSetOperand:
        //         || ClassStringDisjunctionContents.MayContainStrings
        //         || CharacterClassEscape.UnicodePropertyValueExpression.LoneUnicodePropertyNameOrValue.MayContainStrings
        //     ClassStringDisjunctionContents: ClassStrings.some(ClassString => ClassString.ClassSetCharacters.length !== 1)
        //     LoneUnicodePropertyNameOrValue: isBinaryUnicodePropertyOfStrings(LoneUnicodePropertyNameOrValue)

        // ClassSetExpression ::= '^'? (ClassUnion | ClassIntersection | ClassSubtraction)
        // ClassUnion ::= (ClassSetRange | ClassSetOperand)*
        // ClassIntersection ::= ClassSetOperand ('&&' ClassSetOperand)+
        // ClassSubtraction ::= ClassSetOperand ('--' ClassSetOperand)+
        // ClassSetRange ::= ClassSetCharacter '-' ClassSetCharacter
        void ScanClassSetExpression()
        {
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), (int)CharacterCodes.openBracket);
            var isCharacterComplement = false;
            if (CharCodeChecked(pos) == (int)CharacterCodes.caret)
            {
                pos++;
                isCharacterComplement = true;
            }
            var expressionMayContainStrings = false;
            var ch = CharCodeChecked(pos);
            if (IsClassContentExit(ch))
            {
                return;
            }
            var start = pos;
            string? operand = null;
            // TypeScript: `switch (text.slice(pos, pos + 2))` with the cases "--" and "&&".
            if (IsDoublePunctuator(pos, '-') || IsDoublePunctuator(pos, '&'))
            {
                Error(Diagnostics.Expected_a_class_set_operand);
                mayContainStrings = false;
            }
            else
            {
                operand = ScanClassSetOperand();
            }
            switch (CharCodeChecked(pos))
            {
                case (int)CharacterCodes.minus:
                    if (CharCodeChecked(pos + 1) == (int)CharacterCodes.minus)
                    {
                        if (isCharacterComplement && mayContainStrings)
                        {
                            Error(Diagnostics.Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, start, pos - start);
                        }
                        expressionMayContainStrings = mayContainStrings;
                        ScanClassSetSubExpression(ClassSetExpressionType.ClassSubtraction);
                        mayContainStrings = !isCharacterComplement && expressionMayContainStrings;
                        return;
                    }
                    break;
                case (int)CharacterCodes.ampersand:
                    if (CharCodeChecked(pos + 1) == (int)CharacterCodes.ampersand)
                    {
                        ScanClassSetSubExpression(ClassSetExpressionType.ClassIntersection);
                        if (isCharacterComplement && mayContainStrings)
                        {
                            Error(Diagnostics.Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, start, pos - start);
                        }
                        expressionMayContainStrings = mayContainStrings;
                        mayContainStrings = !isCharacterComplement && expressionMayContainStrings;
                        return;
                    }
                    else
                    {
                        Error(Diagnostics.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, pos, 1, FromCharCode(ch));
                    }
                    break;
                default:
                    if (isCharacterComplement && mayContainStrings)
                    {
                        Error(Diagnostics.Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, start, pos - start);
                    }
                    expressionMayContainStrings = mayContainStrings;
                    break;
            }
            while (true)
            {
                ch = CharCodeChecked(pos);
                if (ch == (int)CharacterCodes.EOF)
                {
                    break;
                }
                switch (ch)
                {
                    case (int)CharacterCodes.minus:
                        pos++;
                        ch = CharCodeChecked(pos);
                        if (IsClassContentExit(ch))
                        {
                            mayContainStrings = !isCharacterComplement && expressionMayContainStrings;
                            return;
                        }
                        if (ch == (int)CharacterCodes.minus)
                        {
                            pos++;
                            Error(Diagnostics.Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, pos - 2, 2);
                            start = pos - 2;
                            operand = Slice(text, start, pos);
                            continue;
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(operand))
                            {
                                Error(Diagnostics.A_character_class_range_must_not_be_bounded_by_another_character_class, start, pos - 1 - start);
                            }
                            var secondStart = pos;
                            var secondOperand = ScanClassSetOperand();
                            if (isCharacterComplement && mayContainStrings)
                            {
                                Error(Diagnostics.Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, secondStart, pos - secondStart);
                            }
                            expressionMayContainStrings |= mayContainStrings;
                            if (secondOperand.Length == 0)
                            {
                                Error(Diagnostics.A_character_class_range_must_not_be_bounded_by_another_character_class, secondStart, pos - secondStart);
                                break;
                            }
                            if (string.IsNullOrEmpty(operand))
                            {
                                break;
                            }
                            var minCharacterValue = CodePointAt(operand, 0);
                            var maxCharacterValue = CodePointAt(secondOperand, 0);
                            if (
                                operand.Length == CharSize(minCharacterValue) &&
                                secondOperand.Length == CharSize(maxCharacterValue) &&
                                minCharacterValue > maxCharacterValue
                            )
                            {
                                Error(Diagnostics.Range_out_of_order_in_character_class, start, pos - start);
                            }
                        }
                        break;
                    case (int)CharacterCodes.ampersand:
                        start = pos;
                        pos++;
                        if (CharCodeChecked(pos) == (int)CharacterCodes.ampersand)
                        {
                            pos++;
                            Error(Diagnostics.Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, pos - 2, 2);
                            if (CharCodeChecked(pos) == (int)CharacterCodes.ampersand)
                            {
                                Error(Diagnostics.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, pos, 1, FromCharCode(ch));
                                pos++;
                            }
                        }
                        else
                        {
                            Error(Diagnostics.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, pos - 1, 1, FromCharCode(ch));
                        }
                        operand = Slice(text, start, pos);
                        continue;
                }
                if (IsClassContentExit(CharCodeChecked(pos)))
                {
                    break;
                }
                start = pos;
                // TypeScript: `switch (text.slice(pos, pos + 2))` with the cases "--" and "&&".
                if (IsDoublePunctuator(pos, '-') || IsDoublePunctuator(pos, '&'))
                {
                    Error(Diagnostics.Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, pos, 2);
                    pos += 2;
                    operand = Slice(text, start, pos);
                }
                else
                {
                    operand = ScanClassSetOperand();
                }
            }
            mayContainStrings = !isCharacterComplement && expressionMayContainStrings;
        }

        void ScanClassSetSubExpression(ClassSetExpressionType expressionType)
        {
            var expressionMayContainStrings = mayContainStrings;
            while (true)
            {
                var ch = CharCodeChecked(pos);
                if (IsClassContentExit(ch))
                {
                    break;
                }
                // Provide user-friendly diagnostic messages
                switch (ch)
                {
                    case (int)CharacterCodes.minus:
                        pos++;
                        if (CharCodeChecked(pos) == (int)CharacterCodes.minus)
                        {
                            pos++;
                            if (expressionType != ClassSetExpressionType.ClassSubtraction)
                            {
                                Error(Diagnostics.Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, pos - 2, 2);
                            }
                        }
                        else
                        {
                            Error(Diagnostics.Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, pos - 1, 1);
                        }
                        break;
                    case (int)CharacterCodes.ampersand:
                        pos++;
                        if (CharCodeChecked(pos) == (int)CharacterCodes.ampersand)
                        {
                            pos++;
                            if (expressionType != ClassSetExpressionType.ClassIntersection)
                            {
                                Error(Diagnostics.Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, pos - 2, 2);
                            }
                            if (CharCodeChecked(pos) == (int)CharacterCodes.ampersand)
                            {
                                Error(Diagnostics.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, pos, 1, FromCharCode(ch));
                                pos++;
                            }
                        }
                        else
                        {
                            Error(Diagnostics.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, pos - 1, 1, FromCharCode(ch));
                        }
                        break;
                    default:
                        switch (expressionType)
                        {
                            case ClassSetExpressionType.ClassSubtraction:
                                Error(Diagnostics._0_expected, pos, 0, "--");
                                break;
                            case ClassSetExpressionType.ClassIntersection:
                                Error(Diagnostics._0_expected, pos, 0, "&&");
                                break;
                            default:
                                break;
                        }
                        break;
                }
                ch = CharCodeChecked(pos);
                if (IsClassContentExit(ch))
                {
                    Error(Diagnostics.Expected_a_class_set_operand);
                    break;
                }
                ScanClassSetOperand();
                // Used only if expressionType is Intersection
                expressionMayContainStrings &= mayContainStrings;
            }
            mayContainStrings = expressionMayContainStrings;
        }

        // ClassSetOperand ::=
        //     | '[' ClassSetExpression ']'
        //     | '\' CharacterClassEscape
        //     | '\q{' ClassStringDisjunctionContents '}'
        //     | ClassSetCharacter
        string ScanClassSetOperand()
        {
            mayContainStrings = false;
            switch (CharCodeChecked(pos))
            {
                case (int)CharacterCodes.EOF:
                    return "";
                case (int)CharacterCodes.openBracket:
                    pos++;
                    ScanClassSetExpression();
                    ScanExpectedChar((int)CharacterCodes.closeBracket);
                    return "";
                case (int)CharacterCodes.backslash:
                    pos++;
                    if (ScanCharacterClassEscape())
                    {
                        return "";
                    }
                    else if (CharCodeChecked(pos) == (int)CharacterCodes.q)
                    {
                        pos++;
                        if (CharCodeChecked(pos) == (int)CharacterCodes.openBrace)
                        {
                            pos++;
                            ScanClassStringDisjunctionContents();
                            ScanExpectedChar((int)CharacterCodes.closeBrace);
                            return "";
                        }
                        else
                        {
                            Error(Diagnostics.q_must_be_followed_by_string_alternatives_enclosed_in_braces, pos - 2, 2);
                            return "q";
                        }
                    }
                    pos--;
                    goto default;
                default:
                    return ScanClassSetCharacter();
            }
        }

        // ClassStringDisjunctionContents ::= ClassSetCharacter* ('|' ClassSetCharacter*)*
        void ScanClassStringDisjunctionContents()
        {
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), (int)CharacterCodes.openBrace);
            var characterCount = 0;
            while (true)
            {
                var ch = CharCodeChecked(pos);
                switch (ch)
                {
                    case (int)CharacterCodes.EOF:
                        return;
                    case (int)CharacterCodes.closeBrace:
                        if (characterCount != 1)
                        {
                            mayContainStrings = true;
                        }
                        return;
                    case (int)CharacterCodes.bar:
                        if (characterCount != 1)
                        {
                            mayContainStrings = true;
                        }
                        pos++;
                        // TypeScript resolves this `start` to createScanner's parameter (see the field).
                        start = pos;
                        characterCount = 0;
                        break;
                    default:
                        ScanClassSetCharacter();
                        characterCount++;
                        break;
                }
            }
        }

        // ClassSetCharacter ::=
        //     | SourceCharacter -- ClassSetSyntaxCharacter -- ClassSetReservedDoublePunctuator
        //     | '\' (CharacterEscape | ClassSetReservedPunctuator | 'b')
        string ScanClassSetCharacter()
        {
            var ch = CharCodeChecked(pos);
            if (ch == (int)CharacterCodes.EOF)
            {
                // no need to report an error, the initial scan will already have reported that the RegExp is unterminated.
                return "";
            }
            if (ch == (int)CharacterCodes.backslash)
            {
                pos++;
                var ch2 = CharCodeChecked(pos);
                switch (ch2)
                {
                    case (int)CharacterCodes.b:
                        pos++;
                        return "\b";
                    case (int)CharacterCodes.ampersand:
                    case (int)CharacterCodes.minus:
                    case (int)CharacterCodes.exclamation:
                    case (int)CharacterCodes.hash:
                    case (int)CharacterCodes.percent:
                    case (int)CharacterCodes.comma:
                    case (int)CharacterCodes.colon:
                    case (int)CharacterCodes.semicolon:
                    case (int)CharacterCodes.lessThan:
                    case (int)CharacterCodes.equals:
                    case (int)CharacterCodes.greaterThan:
                    case (int)CharacterCodes.at:
                    case (int)CharacterCodes.backtick:
                    case (int)CharacterCodes.tilde:
                        pos++;
                        return FromCharCode(ch2);
                    default:
                        return ScanCharacterEscape(/*atomEscape*/ false);
                }
            }
            else if (ch == CharCodeChecked(pos + 1))
            {
                switch (ch)
                {
                    case (int)CharacterCodes.ampersand:
                    case (int)CharacterCodes.exclamation:
                    case (int)CharacterCodes.hash:
                    case (int)CharacterCodes.percent:
                    case (int)CharacterCodes.asterisk:
                    case (int)CharacterCodes.plus:
                    case (int)CharacterCodes.comma:
                    case (int)CharacterCodes.dot:
                    case (int)CharacterCodes.colon:
                    case (int)CharacterCodes.semicolon:
                    case (int)CharacterCodes.lessThan:
                    case (int)CharacterCodes.equals:
                    case (int)CharacterCodes.greaterThan:
                    case (int)CharacterCodes.question:
                    case (int)CharacterCodes.at:
                    case (int)CharacterCodes.backtick:
                    case (int)CharacterCodes.tilde:
                        Error(Diagnostics.A_character_class_must_not_contain_a_reserved_double_punctuator_Did_you_mean_to_escape_it_with_backslash, pos, 2);
                        pos += 2;
                        return Substring(text, pos - 2, pos);
                }
            }
            switch (ch)
            {
                case (int)CharacterCodes.slash:
                case (int)CharacterCodes.openParen:
                case (int)CharacterCodes.closeParen:
                case (int)CharacterCodes.openBracket:
                case (int)CharacterCodes.closeBracket:
                case (int)CharacterCodes.openBrace:
                case (int)CharacterCodes.closeBrace:
                case (int)CharacterCodes.minus:
                case (int)CharacterCodes.bar:
                    Error(Diagnostics.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, pos, 1, FromCharCode(ch));
                    pos++;
                    return FromCharCode(ch);
            }
            return ScanSourceCharacter();
        }

        // ClassAtom ::=
        //     | SourceCharacter but not one of '\' or ']'
        //     | '\' ClassEscape
        // ClassEscape ::=
        //     | 'b'
        //     | '-'
        //     | CharacterClassEscape
        //     | CharacterEscape
        string ScanClassAtom()
        {
            if (CharCodeChecked(pos) == (int)CharacterCodes.backslash)
            {
                pos++;
                var ch = CharCodeChecked(pos);
                switch (ch)
                {
                    case (int)CharacterCodes.b:
                        pos++;
                        return "\b";
                    case (int)CharacterCodes.minus:
                        pos++;
                        return FromCharCode(ch);
                    default:
                        if (ScanCharacterClassEscape())
                        {
                            return "";
                        }
                        return ScanCharacterEscape(/*atomEscape*/ false);
                }
            }
            else
            {
                return ScanSourceCharacter();
            }
        }

        // CharacterClassEscape ::=
        //     | 'd' | 'D' | 's' | 'S' | 'w' | 'W'
        //     | [+UnicodeMode] ('P' | 'p') '{' UnicodePropertyValueExpression '}'
        bool ScanCharacterClassEscape()
        {
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), (int)CharacterCodes.backslash);
            var isCharacterComplement = false;
            var start = pos - 1;
            var ch = CharCodeChecked(pos);
            switch (ch)
            {
                case (int)CharacterCodes.d:
                case (int)CharacterCodes.D:
                case (int)CharacterCodes.s:
                case (int)CharacterCodes.S:
                case (int)CharacterCodes.w:
                case (int)CharacterCodes.W:
                    pos++;
                    return true;
                case (int)CharacterCodes.P:
                    isCharacterComplement = true;
                    goto case (int)CharacterCodes.p;
                case (int)CharacterCodes.p:
                    pos++;
                    if (CharCodeChecked(pos) == (int)CharacterCodes.openBrace)
                    {
                        pos++;
                        var propertyNameOrValueStart = pos;
                        var propertyNameOrValue = ScanWordCharacters();
                        if (CharCodeChecked(pos) == (int)CharacterCodes.equals)
                        {
                            var propertyName = NonBinaryUnicodeProperties.TryGetValue(propertyNameOrValue, out var canonicalName) ? canonicalName : null;
                            if (pos == propertyNameOrValueStart)
                            {
                                Error(Diagnostics.Expected_a_Unicode_property_name);
                            }
                            else if (propertyName == null)
                            {
                                Error(Diagnostics.Unknown_Unicode_property_name, propertyNameOrValueStart, pos - propertyNameOrValueStart);
                                var suggestion = Core.GetSpellingSuggestion(propertyNameOrValue, NonBinaryUnicodeProperties.Keys, Core.Identity<string>);
                                if (!string.IsNullOrEmpty(suggestion))
                                {
                                    Error(Diagnostics.Did_you_mean_0, propertyNameOrValueStart, pos - propertyNameOrValueStart, suggestion);
                                }
                            }
                            pos++;
                            var propertyValueStart = pos;
                            var propertyValue = ScanWordCharacters();
                            if (pos == propertyValueStart)
                            {
                                Error(Diagnostics.Expected_a_Unicode_property_value);
                            }
                            else if (propertyName != null && !ValuesOfNonBinaryUnicodeProperties[propertyName].Has(propertyValue))
                            {
                                Error(Diagnostics.Unknown_Unicode_property_value, propertyValueStart, pos - propertyValueStart);
                                var suggestion = Core.GetSpellingSuggestion(propertyValue, ValuesOfNonBinaryUnicodeProperties[propertyName], Core.Identity<string>);
                                if (!string.IsNullOrEmpty(suggestion))
                                {
                                    Error(Diagnostics.Did_you_mean_0, propertyValueStart, pos - propertyValueStart, suggestion);
                                }
                            }
                        }
                        else
                        {
                            if (pos == propertyNameOrValueStart)
                            {
                                Error(Diagnostics.Expected_a_Unicode_property_name_or_value);
                            }
                            else if (BinaryUnicodePropertiesOfStrings.Has(propertyNameOrValue))
                            {
                                if (!unicodeSetsMode)
                                {
                                    Error(Diagnostics.Any_Unicode_property_that_would_possibly_match_more_than_a_single_character_is_only_available_when_the_Unicode_Sets_v_flag_is_set, propertyNameOrValueStart, pos - propertyNameOrValueStart);
                                }
                                else if (isCharacterComplement)
                                {
                                    Error(Diagnostics.Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, propertyNameOrValueStart, pos - propertyNameOrValueStart);
                                }
                                else
                                {
                                    mayContainStrings = true;
                                }
                            }
                            else if (!ValuesOfNonBinaryUnicodeProperties.General_Category.Has(propertyNameOrValue) && !BinaryUnicodeProperties.Has(propertyNameOrValue))
                            {
                                Error(Diagnostics.Unknown_Unicode_property_name_or_value, propertyNameOrValueStart, pos - propertyNameOrValueStart);
                                var candidates = new List<string>();
                                candidates.AddRange(ValuesOfNonBinaryUnicodeProperties.General_Category);
                                candidates.AddRange(BinaryUnicodeProperties);
                                candidates.AddRange(BinaryUnicodePropertiesOfStrings);
                                var suggestion = Core.GetSpellingSuggestion(propertyNameOrValue, candidates, Core.Identity<string>);
                                if (!string.IsNullOrEmpty(suggestion))
                                {
                                    Error(Diagnostics.Did_you_mean_0, propertyNameOrValueStart, pos - propertyNameOrValueStart, suggestion);
                                }
                            }
                        }
                        ScanExpectedChar((int)CharacterCodes.closeBrace);
                        if (!anyUnicodeMode)
                        {
                            Error(Diagnostics.Unicode_property_value_expressions_are_only_available_when_the_Unicode_u_flag_or_the_Unicode_Sets_v_flag_is_set, start, pos - start);
                        }
                    }
                    else if (anyUnicodeModeOrNonAnnexB)
                    {
                        Error(Diagnostics._0_must_be_followed_by_a_Unicode_property_value_expression_enclosed_in_braces, pos - 2, 2, FromCharCode(ch));
                    }
                    else
                    {
                        pos--;
                        return false;
                    }
                    return true;
            }
            return false;
        }

        string ScanWordCharacters()
        {
            var value = new StringParts(); // TypeScript: `let value = ""` (see StringParts)
            while (true)
            {
                var ch = CharCodeChecked(pos);
                if (ch == (int)CharacterCodes.EOF || !IsWordCharacter(ch))
                {
                    break;
                }
                value.Append(text, pos, pos + 1); // TypeScript: `value += String.fromCharCode(ch)`, ch being text[pos]
                pos++;
            }
            return value.Result();
        }

        string ScanSourceCharacter()
        {
            var size = anyUnicodeMode ? CharSize(CodePointChecked(pos)) : 1;
            pos += size;
            return size > 0 ? Substring(text, pos - size, pos) : "";
        }

        void ScanExpectedChar(int ch)
        {
            if (CharCodeChecked(pos) == ch)
            {
                pos++;
            }
            else
            {
                Error(Diagnostics._0_expected, pos, 0, FromCharCode(ch));
            }
        }

        ScanDisjunction(/*isInGroup*/ false);

        // TypeScript: forEach(groupNameReferences, reference => { ... }).
        if (groupNameReferences != null)
        {
            foreach (var reference in groupNameReferences)
            {
                if (groupSpecifiers?.Has(reference.Name) != true)
                {
                    Error(Diagnostics.There_is_no_capturing_group_named_0_in_this_regular_expression, reference.Pos, reference.End - reference.Pos, reference.Name);
                    if (groupSpecifiers != null)
                    {
                        var suggestion = Core.GetSpellingSuggestion(reference.Name, groupSpecifiers, Core.Identity<string>);
                        if (!string.IsNullOrEmpty(suggestion))
                        {
                            Error(Diagnostics.Did_you_mean_0, reference.Pos, reference.End - reference.Pos, suggestion);
                        }
                    }
                }
            }
        }
        // TypeScript: forEach(decimalEscapes, escape => { ... }).
        if (decimalEscapes != null)
        {
            foreach (var escape in decimalEscapes)
            {
                // Although a DecimalEscape with a value greater than the number of capturing groups
                // is treated as either a LegacyOctalEscapeSequence or an IdentityEscape in Annex B,
                // an error is nevertheless reported since it's most likely a mistake.
                if (escape.Value > numberOfCapturingGroups)
                {
                    if (numberOfCapturingGroups != 0)
                    {
                        Error(Diagnostics.This_backreference_refers_to_a_group_that_does_not_exist_There_are_only_0_capturing_groups_in_this_regular_expression, escape.Pos, escape.End - escape.Pos, numberOfCapturingGroups);
                    }
                    else
                    {
                        Error(Diagnostics.This_backreference_refers_to_a_group_that_does_not_exist_There_are_no_capturing_groups_in_this_regular_expression, escape.Pos, escape.End - escape.Pos);
                    }
                }
            }
        }
    }

    // `text.slice(pos, pos + 2) === "--"` (or "&&"): both characters exist and equal `ch`.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsDoublePunctuator(int at, char ch)
    {
        return at >= 0 && at + 1 < text.Length && text[at] == ch && text[at + 1] == ch;
    }

    // `namedCapturingGroupsScopeStack.some(group => group?.has(tokenValue))`.
    private static bool SomeScopeHas(List<OrderedStringSet?> scopes, string name)
    {
        foreach (var group in scopes)
        {
            if (group?.Has(name) == true)
            {
                return true;
            }
        }
        return false;
    }

    // `array.pop()`: removes and returns the last element (undefined for an empty array).
    private static T? Pop<T>(List<T> array)
    {
        if (array.Count == 0)
        {
            return default;
        }
        var last = array[^1];
        array.RemoveAt(array.Count - 1);
        return last;
    }
}
