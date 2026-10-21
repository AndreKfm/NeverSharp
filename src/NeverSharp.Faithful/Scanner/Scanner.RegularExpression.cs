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

namespace NeverSharp.Faithful;

/// <summary>
/// scanner.ts: createScanner's scanRegularExpressionWorker. Its variables are fields (set at its start), its inner
/// functions methods, like the variables and functions of createScanner itself; the worker is not re-entrant in
/// TypeScript either.
/// </summary>
internal sealed partial class Scanner
{
    // Worker parameters used by the inner functions.
    private bool annexB;
    private bool namedCaptureGroups;

    /// <summary>Grammar parameter.</summary>
    private bool unicodeSetsMode;

    /// <summary>Grammar parameter.</summary>
    private bool anyUnicodeMode;

    // Regular expressions are checked more strictly when either in 'u' or 'v' mode, or
    // when not using the looser interpretation of the syntax from ECMA-262 Annex B.
    private bool anyUnicodeModeOrNonAnnexB;

    /// <summary>@see {scanClassSetExpression}.</summary>
    private bool mayContainStrings;

    /// <summary>The number of all (named and unnamed) capturing groups defined in the regex.</summary>
    private int numberOfCapturingGroups;

    /// <summary>All named capturing groups defined in the regex.</summary>
    private JsStringSet? groupSpecifiers;

    /// <summary>All references to named capturing groups in the regex.</summary>
    private List<GroupNameReference>? groupNameReferences;

    /// <summary>All numeric backreferences within the regex.</summary>
    private List<DecimalEscapeReference>? decimalEscapes;

    /// <summary>A stack of scopes for named capturing groups. @see {scanGroupName}.</summary>
    private List<JsStringSet?> namedCapturingGroupsScopeStack = [];

    private JsStringSet? topNamedCapturingGroupsScope;

    /// <summary>TypeScript: `TextRange &amp; { name: string; }`.</summary>
    private sealed class GroupNameReference : TextRange
    {
        public int Pos { get; set; }

        public int End { get; set; }

        public string Name { get; set; } = null!;
    }

    /// <summary>TypeScript: `TextRange &amp; { value: number; }`.</summary>
    private sealed class DecimalEscapeReference : TextRange
    {
        public int Pos { get; set; }

        public int End { get; set; }

        public double Value { get; set; }
    }

    private void ScanRegularExpressionWorker(RegularExpressionFlags regExpFlags, bool annexB, bool namedCaptureGroups)
    {
        this.annexB = annexB;
        this.namedCaptureGroups = namedCaptureGroups;
        unicodeSetsMode = (regExpFlags & RegularExpressionFlags.UnicodeSets) != 0;
        anyUnicodeMode = (regExpFlags & RegularExpressionFlags.AnyUnicodeMode) != 0;
        anyUnicodeModeOrNonAnnexB = anyUnicodeMode || !annexB;
        mayContainStrings = false;
        numberOfCapturingGroups = 0;
        groupSpecifiers = null;
        groupNameReferences = null;
        decimalEscapes = null;
        namedCapturingGroupsScopeStack = [];
        topNamedCapturingGroupsScope = null;

        ScanDisjunction(/*isInGroup*/ false);

        // TypeScript: `forEach(groupNameReferences, reference => { ... })`; the callback returns undefined, so every
        // reference is visited.
        if (groupNameReferences != null)
        {
            foreach (var reference in groupNameReferences)
            {
                if (!(groupSpecifiers?.Has(reference.Name) ?? false))
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

    // Disjunction ::= Alternative ('|' Alternative)*
    private void ScanDisjunction(bool isInGroup)
    {
        while (true)
        {
            namedCapturingGroupsScopeStack.Add(topNamedCapturingGroupsScope);
            topNamedCapturingGroupsScope = null;
            ScanAlternative(isInGroup);
            topNamedCapturingGroupsScope = namedCapturingGroupsScopeStack[^1];
            namedCapturingGroupsScopeStack.RemoveAt(namedCapturingGroupsScopeStack.Count - 1);
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
    private void ScanAlternative(bool isInGroup)
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
                                // TypeScript: `const start = pos;` in the inner block (it shadows the outer `start`).
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
                    pos++;
                    var digitsStart = pos;
                    ScanDigits();
                    var min = tokenValue;
                    if (!anyUnicodeModeOrNonAnnexB && string.IsNullOrEmpty(min))
                    {
                        isPreviousTermQuantifiable = true;
                        break;
                    }
                    if (CharCodeChecked(pos) == (int)CharacterCodes.comma)
                    {
                        pos++;
                        ScanDigits();
                        var max = tokenValue;
                        if (string.IsNullOrEmpty(min))
                        {
                            if (!string.IsNullOrEmpty(max) || CharCodeChecked(pos) == (int)CharacterCodes.closeBrace)
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
                        else if (!string.IsNullOrEmpty(max) && ParseInt(min, 10) > ParseInt(max, 10) && (anyUnicodeModeOrNonAnnexB || CharCodeChecked(pos) == (int)CharacterCodes.closeBrace))
                        {
                            Error(Diagnostics.Numbers_out_of_order_in_quantifier, digitsStart, pos - digitsStart);
                        }
                    }
                    else if (string.IsNullOrEmpty(min))
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

    private RegularExpressionFlags ScanPatternModifiers(RegularExpressionFlags currFlags)
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
    private void ScanAtomEscape()
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
    private bool ScanDecimalEscape()
    {
        Debug.AssertEqual(CharCodeUnchecked(pos - 1), (int)CharacterCodes.backslash);
        var ch = CharCodeChecked(pos);
        if (ch >= (int)CharacterCodes._1 && ch <= (int)CharacterCodes._9)
        {
            var start = pos;
            ScanDigits();
            decimalEscapes = Core.Append(decimalEscapes, new DecimalEscapeReference { Pos = start, End = pos, Value = StringToNumber(tokenValue!) });
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
    private string ScanCharacterEscape(bool atomEscape)
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
                return ScanEscapeSequence(
                    EscapeSequenceScanningFlags.RegularExpression
                        | (annexB ? EscapeSequenceScanningFlags.AnnexB : 0)
                        | (anyUnicodeMode ? EscapeSequenceScanningFlags.AnyUnicodeMode : 0)
                        | (atomEscape ? EscapeSequenceScanningFlags.AtomEscape : 0));
        }
    }

    private void ScanGroupName(bool isReference)
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
            groupNameReferences = Core.Append(groupNameReferences, new GroupNameReference { Pos = tokenStart, End = pos, Name = tokenValue! });
        }
        else if ((topNamedCapturingGroupsScope?.Has(tokenValue!) ?? false) || NamedCapturingGroupsScopeStackHas(tokenValue!))
        {
            Error(Diagnostics.Named_capturing_groups_with_the_same_name_must_be_mutually_exclusive_to_each_other, tokenStart, pos - tokenStart);
        }
        else
        {
            topNamedCapturingGroupsScope ??= new JsStringSet();
            topNamedCapturingGroupsScope.Add(tokenValue!);
            groupSpecifiers ??= new JsStringSet();
            groupSpecifiers.Add(tokenValue!);
        }
    }

    // TypeScript: `namedCapturingGroupsScopeStack.some(group => group?.has(tokenValue))`.
    private bool NamedCapturingGroupsScopeStackHas(string name)
    {
        foreach (var group in namedCapturingGroupsScopeStack)
        {
            if (group?.Has(name) ?? false)
            {
                return true;
            }
        }
        return false;
    }

    private bool IsClassContentExit(int ch)
    {
        return ch == (int)CharacterCodes.closeBracket || ch == (int)CharacterCodes.EOF || pos >= end;
    }

    // ClassRanges ::= '^'? (ClassAtom ('-' ClassAtom)?)*
    private void ScanClassRanges()
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
                // TypeScript: `const ch` in the inner block (it shadows the outer `ch`).
                var chAfterMinus = CharCodeChecked(pos);
                if (IsClassContentExit(chAfterMinus))
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
    private void ScanClassSetExpression()
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
        // TypeScript: `let operand!: string;` (undefined when the expression starts with an operator).
        string? operand = null;
        switch (Slice2(pos)) // TODO: don't use slice
        {
            case "--":
            case "&&":
                Error(Diagnostics.Expected_a_class_set_operand);
                mayContainStrings = false;
                break;
            default:
                operand = ScanClassSetOperand();
                break;
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
                        operand = Slice(start, pos);
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
                    operand = Slice(start, pos);
                    continue;
            }
            if (IsClassContentExit(CharCodeChecked(pos)))
            {
                break;
            }
            start = pos;
            switch (Slice2(pos)) // TODO: don't use slice
            {
                case "--":
                case "&&":
                    Error(Diagnostics.Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, pos, 2);
                    pos += 2;
                    operand = Slice(start, pos);
                    break;
                default:
                    operand = ScanClassSetOperand();
                    break;
            }
        }
        mayContainStrings = !isCharacterComplement && expressionMayContainStrings;
    }

    // TypeScript: `text.slice(pos, pos + 2)`, compared with "--" and "&&" only.
    private string? Slice2(int start)
    {
        if (start >= 0 && start + 1 < text.Length)
        {
            var first = text[start];
            if (first == text[start + 1] && (first == '-' || first == '&'))
            {
                return first == '-' ? "--" : "&&";
            }
        }
        return null;
    }

    // TypeScript: `text.slice(start, end)` for 0 <= start <= end.
    private string Slice(int start, int end)
    {
        return Substring(text, start, end);
    }

    private void ScanClassSetSubExpression(ClassSetExpressionType expressionType)
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
    private string ScanClassSetOperand()
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
    private void ScanClassStringDisjunctionContents()
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
                    // TypeScript: `start = pos;` assigns createScanner's parameter `start`, which nothing reads after
                    // the initial setText; the assignment has no effect and is not ported.
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
    private string ScanClassSetCharacter()
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
            // TypeScript: `const ch` in the inner block (it shadows the outer `ch`).
            var escapedChar = CharCodeChecked(pos);
            switch (escapedChar)
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
                    return FromCharCode(escapedChar);
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
    private string ScanClassAtom()
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
    private bool ScanCharacterClassEscape()
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
                        else if (!ValuesOfNonBinaryUnicodeProperties["General_Category"].Has(propertyNameOrValue) && !BinaryUnicodeProperties.Has(propertyNameOrValue))
                        {
                            Error(Diagnostics.Unknown_Unicode_property_name_or_value, propertyNameOrValueStart, pos - propertyNameOrValueStart);
                            // TypeScript: `[...General_Category, ...binaryUnicodeProperties, ...binaryUnicodePropertiesOfStrings]`.
                            var candidates = new List<string>(ValuesOfNonBinaryUnicodeProperties["General_Category"]);
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

    private string ScanWordCharacters()
    {
        var value = new StringConcatenation();
        while (true)
        {
            var ch = CharCodeChecked(pos);
            if (ch == (int)CharacterCodes.EOF || !IsWordCharacter(ch))
            {
                break;
            }
            value.Append(FromCharCode(ch));
            pos++;
        }
        return value.ToString();
    }

    private string ScanSourceCharacter()
    {
        var size = anyUnicodeMode ? CharSize(CodePointChecked(pos)) : 1;
        pos += size;
        return size > 0 ? Substring(text, pos - size, pos) : "";
    }

    private void ScanExpectedChar(int ch)
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
}
