// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/scanner.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/scanner/regexp.go and internal/scanner/unicodeproperties.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

// See Scanner.cs: the alias keeps the generated `Diagnostics` class reachable where a root namespace `Diagnostics` exists.
using DiagnosticMessages = NeverSharp.Generated.Diagnostics;

namespace NeverSharp.Faithful;

// scanner.ts: scanRegularExpressionWorker, the regular-expression validator of createScanner.
internal sealed partial class Scanner
{
    /// <summary>An insertion-ordered set of strings (a JavaScript `Set`; the order matters for spelling suggestions).</summary>
    private sealed class OrderedStringSet(params string[] items)
    {
        internal string[] Items { get; } = items;

        private readonly HashSet<string> lookup = new(items, StringComparer.Ordinal);

        internal bool Has(string value) => lookup.Contains(value);
    }

    // Table 66: Non-binary Unicode property aliases and their canonical property names
    // https://tc39.es/ecma262/#table-nonbinary-unicode-properties
    private static readonly string[] NonBinaryUnicodePropertyKeys = ["General_Category", "gc", "Script", "sc", "Script_Extensions", "scx"];

    private static readonly Dictionary<string, string> NonBinaryUnicodeProperties = new(StringComparer.Ordinal)
    {
        ["General_Category"] = "General_Category",
        ["gc"] = "General_Category",
        ["Script"] = "Script",
        ["sc"] = "Script",
        ["Script_Extensions"] = "Script_Extensions",
        ["scx"] = "Script_Extensions",
    };

    // Table 67: Binary Unicode property aliases and their canonical property names
    // https://tc39.es/ecma262/#table-binary-unicode-properties
    private static readonly OrderedStringSet BinaryUnicodeProperties = new("ASCII", "ASCII_Hex_Digit", "AHex", "Alphabetic", "Alpha", "Any", "Assigned", "Bidi_Control", "Bidi_C", "Bidi_Mirrored", "Bidi_M", "Case_Ignorable", "CI", "Cased", "Changes_When_Casefolded", "CWCF", "Changes_When_Casemapped", "CWCM", "Changes_When_Lowercased", "CWL", "Changes_When_NFKC_Casefolded", "CWKCF", "Changes_When_Titlecased", "CWT", "Changes_When_Uppercased", "CWU", "Dash", "Default_Ignorable_Code_Point", "DI", "Deprecated", "Dep", "Diacritic", "Dia", "Emoji", "Emoji_Component", "EComp", "Emoji_Modifier", "EMod", "Emoji_Modifier_Base", "EBase", "Emoji_Presentation", "EPres", "Extended_Pictographic", "ExtPict", "Extender", "Ext", "Grapheme_Base", "Gr_Base", "Grapheme_Extend", "Gr_Ext", "Hex_Digit", "Hex", "IDS_Binary_Operator", "IDSB", "IDS_Trinary_Operator", "IDST", "ID_Continue", "IDC", "ID_Start", "IDS", "Ideographic", "Ideo", "Join_Control", "Join_C", "Logical_Order_Exception", "LOE", "Lowercase", "Lower", "Math", "Noncharacter_Code_Point", "NChar", "Pattern_Syntax", "Pat_Syn", "Pattern_White_Space", "Pat_WS", "Quotation_Mark", "QMark", "Radical", "Regional_Indicator", "RI", "Sentence_Terminal", "STerm", "Soft_Dotted", "SD", "Terminal_Punctuation", "Term", "Unified_Ideograph", "UIdeo", "Uppercase", "Upper", "Variation_Selector", "VS", "White_Space", "space", "XID_Continue", "XIDC", "XID_Start", "XIDS");

    // Table 68: Binary Unicode properties of strings
    // https://tc39.es/ecma262/#table-binary-unicode-properties-of-strings
    private static readonly OrderedStringSet BinaryUnicodePropertiesOfStrings = new("Basic_Emoji", "Emoji_Keycap_Sequence", "RGI_Emoji_Modifier_Sequence", "RGI_Emoji_Flag_Sequence", "RGI_Emoji_Tag_Sequence", "RGI_Emoji_ZWJ_Sequence", "RGI_Emoji");

    // Unicode 15.1
    private static readonly OrderedStringSet ValuesOfGeneralCategory = new("C", "Other", "Cc", "Control", "cntrl", "Cf", "Format", "Cn", "Unassigned", "Co", "Private_Use", "Cs", "Surrogate", "L", "Letter", "LC", "Cased_Letter", "Ll", "Lowercase_Letter", "Lm", "Modifier_Letter", "Lo", "Other_Letter", "Lt", "Titlecase_Letter", "Lu", "Uppercase_Letter", "M", "Mark", "Combining_Mark", "Mc", "Spacing_Mark", "Me", "Enclosing_Mark", "Mn", "Nonspacing_Mark", "N", "Number", "Nd", "Decimal_Number", "digit", "Nl", "Letter_Number", "No", "Other_Number", "P", "Punctuation", "punct", "Pc", "Connector_Punctuation", "Pd", "Dash_Punctuation", "Pe", "Close_Punctuation", "Pf", "Final_Punctuation", "Pi", "Initial_Punctuation", "Po", "Other_Punctuation", "Ps", "Open_Punctuation", "S", "Symbol", "Sc", "Currency_Symbol", "Sk", "Modifier_Symbol", "Sm", "Math_Symbol", "So", "Other_Symbol", "Z", "Separator", "Zl", "Line_Separator", "Zp", "Paragraph_Separator", "Zs", "Space_Separator");

    private static readonly OrderedStringSet ValuesOfScript = new("Adlm", "Adlam", "Aghb", "Caucasian_Albanian", "Ahom", "Arab", "Arabic", "Armi", "Imperial_Aramaic", "Armn", "Armenian", "Avst", "Avestan", "Bali", "Balinese", "Bamu", "Bamum", "Bass", "Bassa_Vah", "Batk", "Batak", "Beng", "Bengali", "Bhks", "Bhaiksuki", "Bopo", "Bopomofo", "Brah", "Brahmi", "Brai", "Braille", "Bugi", "Buginese", "Buhd", "Buhid", "Cakm", "Chakma", "Cans", "Canadian_Aboriginal", "Cari", "Carian", "Cham", "Cher", "Cherokee", "Chrs", "Chorasmian", "Copt", "Coptic", "Qaac", "Cpmn", "Cypro_Minoan", "Cprt", "Cypriot", "Cyrl", "Cyrillic", "Deva", "Devanagari", "Diak", "Dives_Akuru", "Dogr", "Dogra", "Dsrt", "Deseret", "Dupl", "Duployan", "Egyp", "Egyptian_Hieroglyphs", "Elba", "Elbasan", "Elym", "Elymaic", "Ethi", "Ethiopic", "Geor", "Georgian", "Glag", "Glagolitic", "Gong", "Gunjala_Gondi", "Gonm", "Masaram_Gondi", "Goth", "Gothic", "Gran", "Grantha", "Grek", "Greek", "Gujr", "Gujarati", "Guru", "Gurmukhi", "Hang", "Hangul", "Hani", "Han", "Hano", "Hanunoo", "Hatr", "Hatran", "Hebr", "Hebrew", "Hira", "Hiragana", "Hluw", "Anatolian_Hieroglyphs", "Hmng", "Pahawh_Hmong", "Hmnp", "Nyiakeng_Puachue_Hmong", "Hrkt", "Katakana_Or_Hiragana", "Hung", "Old_Hungarian", "Ital", "Old_Italic", "Java", "Javanese", "Kali", "Kayah_Li", "Kana", "Katakana", "Kawi", "Khar", "Kharoshthi", "Khmr", "Khmer", "Khoj", "Khojki", "Kits", "Khitan_Small_Script", "Knda", "Kannada", "Kthi", "Kaithi", "Lana", "Tai_Tham", "Laoo", "Lao", "Latn", "Latin", "Lepc", "Lepcha", "Limb", "Limbu", "Lina", "Linear_A", "Linb", "Linear_B", "Lisu", "Lyci", "Lycian", "Lydi", "Lydian", "Mahj", "Mahajani", "Maka", "Makasar", "Mand", "Mandaic", "Mani", "Manichaean", "Marc", "Marchen", "Medf", "Medefaidrin", "Mend", "Mende_Kikakui", "Merc", "Meroitic_Cursive", "Mero", "Meroitic_Hieroglyphs", "Mlym", "Malayalam", "Modi", "Mong", "Mongolian", "Mroo", "Mro", "Mtei", "Meetei_Mayek", "Mult", "Multani", "Mymr", "Myanmar", "Nagm", "Nag_Mundari", "Nand", "Nandinagari", "Narb", "Old_North_Arabian", "Nbat", "Nabataean", "Newa", "Nkoo", "Nko", "Nshu", "Nushu", "Ogam", "Ogham", "Olck", "Ol_Chiki", "Orkh", "Old_Turkic", "Orya", "Oriya", "Osge", "Osage", "Osma", "Osmanya", "Ougr", "Old_Uyghur", "Palm", "Palmyrene", "Pauc", "Pau_Cin_Hau", "Perm", "Old_Permic", "Phag", "Phags_Pa", "Phli", "Inscriptional_Pahlavi", "Phlp", "Psalter_Pahlavi", "Phnx", "Phoenician", "Plrd", "Miao", "Prti", "Inscriptional_Parthian", "Rjng", "Rejang", "Rohg", "Hanifi_Rohingya", "Runr", "Runic", "Samr", "Samaritan", "Sarb", "Old_South_Arabian", "Saur", "Saurashtra", "Sgnw", "SignWriting", "Shaw", "Shavian", "Shrd", "Sharada", "Sidd", "Siddham", "Sind", "Khudawadi", "Sinh", "Sinhala", "Sogd", "Sogdian", "Sogo", "Old_Sogdian", "Sora", "Sora_Sompeng", "Soyo", "Soyombo", "Sund", "Sundanese", "Sylo", "Syloti_Nagri", "Syrc", "Syriac", "Tagb", "Tagbanwa", "Takr", "Takri", "Tale", "Tai_Le", "Talu", "New_Tai_Lue", "Taml", "Tamil", "Tang", "Tangut", "Tavt", "Tai_Viet", "Telu", "Telugu", "Tfng", "Tifinagh", "Tglg", "Tagalog", "Thaa", "Thaana", "Thai", "Tibt", "Tibetan", "Tirh", "Tirhuta", "Tnsa", "Tangsa", "Toto", "Ugar", "Ugaritic", "Vaii", "Vai", "Vith", "Vithkuqi", "Wara", "Warang_Citi", "Wcho", "Wancho", "Xpeo", "Old_Persian", "Xsux", "Cuneiform", "Yezi", "Yezidi", "Yiii", "Yi", "Zanb", "Zanabazar_Square", "Zinh", "Inherited", "Qaai", "Zyyy", "Common", "Zzzz", "Unknown");

    // scanner.ts: valuesOfNonBinaryUnicodeProperties[propertyName]. The Script_Extensions property of a character contains one or more
    // Script values. See https://www.unicode.org/reports/tr24/#Script_Extensions
    // Here since each Unicode property value expression only allows a single value, its values can be considered the same as those of the Script property.
    private static OrderedStringSet ValuesOfNonBinaryUnicodeProperty(string propertyName)
    {
        return propertyName == "General_Category" ? ValuesOfGeneralCategory : ValuesOfScript;
    }

    /// <summary>A named group reference in a regular expression: `TextRange &amp; { name }`.</summary>
    private readonly record struct GroupNameReference(int Pos, int End, string Name);

    /// <summary>A decimal escape (backreference) in a regular expression: `TextRange &amp; { value }`.</summary>
    private readonly record struct DecimalEscape(int Pos, int End, double Value);

    private void ScanRegularExpressionWorker(RegularExpressionFlags regExpFlags, bool annexB, bool namedCaptureGroups)
    {
        // Grammar parameter
        var unicodeSetsMode = (regExpFlags & RegularExpressionFlags.UnicodeSets) != 0;
        // Grammar parameter
        var anyUnicodeMode = (regExpFlags & RegularExpressionFlags.AnyUnicodeMode) != 0;

        // Regular expressions are checked more strictly when either in 'u' or 'v' mode, or
        // when not using the looser interpretation of the syntax from ECMA-262 Annex B.
        var anyUnicodeModeOrNonAnnexB = anyUnicodeMode || !annexB;

        // @see {scanClassSetExpression}
        var mayContainStrings = false;

        // The number of all (named and unnamed) capturing groups defined in the regex.
        var numberOfCapturingGroups = 0;
        // All named capturing groups defined in the regex.
        List<string>? groupSpecifiers = null;
        // All references to named capturing groups in the regex.
        List<GroupNameReference>? groupNameReferences = null;
        // All numeric backreferences within the regex.
        List<DecimalEscape>? decimalEscapes = null;
        // A stack of scopes for named capturing groups. @see {scanGroupName}
        var namedCapturingGroupsScopeStack = new List<HashSet<string>?>();
        HashSet<string>? topNamedCapturingGroupsScope = null;

        ScanDisjunction(/*isInGroup*/ false);

        if (groupNameReferences != null)
        {
            foreach (var reference in groupNameReferences)
            {
                if (!(groupSpecifiers != null && groupSpecifiers.Contains(reference.Name)))
                {
                    Error(DiagnosticMessages.There_is_no_capturing_group_named_0_in_this_regular_expression, reference.Pos, reference.End - reference.Pos, reference.Name);
                    if (groupSpecifiers != null)
                    {
                        var suggestion = Core.GetSpellingSuggestion(reference.Name, groupSpecifiers, static x => x);
                        if (!string.IsNullOrEmpty(suggestion))
                        {
                            Error(DiagnosticMessages.Did_you_mean_0, reference.Pos, reference.End - reference.Pos, suggestion);
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
                        Error(DiagnosticMessages.This_backreference_refers_to_a_group_that_does_not_exist_There_are_only_0_capturing_groups_in_this_regular_expression, escape.Pos, escape.End - escape.Pos, numberOfCapturingGroups);
                    }
                    else
                    {
                        Error(DiagnosticMessages.This_backreference_refers_to_a_group_that_does_not_exist_There_are_no_capturing_groups_in_this_regular_expression, escape.Pos, escape.End - escape.Pos);
                    }
                }
            }
        }

        // Disjunction ::= Alternative ('|' Alternative)*
        void ScanDisjunction(bool isInGroup)
        {
            while (true)
            {
                namedCapturingGroupsScopeStack.Add(topNamedCapturingGroupsScope);
                topNamedCapturingGroupsScope = null;
                ScanAlternative(isInGroup);
                topNamedCapturingGroupsScope = namedCapturingGroupsScopeStack[^1];
                namedCapturingGroupsScopeStack.RemoveAt(namedCapturingGroupsScopeStack.Count - 1);
                if (CharCodeChecked(pos) != Ch.Bar)
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
                    case Ch.EOF:
                        return;
                    case Ch.Caret:
                    case Ch.Dollar:
                        pos++;
                        isPreviousTermQuantifiable = false;
                        break;
                    case Ch.Backslash:
                        pos++;
                        switch (CharCodeChecked(pos))
                        {
                            case Ch.LowerB:
                            case Ch.UpperB:
                                pos++;
                                isPreviousTermQuantifiable = false;
                                break;
                            default:
                                ScanAtomEscape();
                                isPreviousTermQuantifiable = true;
                                break;
                        }
                        break;
                    case Ch.OpenParen:
                        pos++;
                        if (CharCodeChecked(pos) == Ch.Question)
                        {
                            pos++;
                            switch (CharCodeChecked(pos))
                            {
                                case Ch.EqualsSign:
                                case Ch.Exclamation:
                                    pos++;
                                    // In Annex B, `(?=Disjunction)` and `(?!Disjunction)` are quantifiable
                                    isPreviousTermQuantifiable = !anyUnicodeModeOrNonAnnexB;
                                    break;
                                case Ch.LessThan:
                                    var groupNameStart = pos;
                                    pos++;
                                    switch (CharCodeChecked(pos))
                                    {
                                        case Ch.EqualsSign:
                                        case Ch.Exclamation:
                                            pos++;
                                            isPreviousTermQuantifiable = false;
                                            break;
                                        default:
                                            ScanGroupName(/*isReference*/ false);
                                            ScanExpectedChar(Ch.GreaterThan);
                                            if (languageVersion < ScriptTarget.ES2018)
                                            {
                                                Error(DiagnosticMessages.Named_capturing_groups_are_only_available_when_targeting_ES2018_or_later, groupNameStart, pos - groupNameStart);
                                            }
                                            numberOfCapturingGroups++;
                                            isPreviousTermQuantifiable = true;
                                            break;
                                    }
                                    break;
                                default:
                                    var modifiersStart = pos;
                                    var setFlags = ScanPatternModifiers(RegularExpressionFlags.None);
                                    if (CharCodeChecked(pos) == Ch.Minus)
                                    {
                                        pos++;
                                        ScanPatternModifiers(setFlags);
                                        if (pos == modifiersStart + 1)
                                        {
                                            Error(DiagnosticMessages.Subpattern_flags_must_be_present_when_there_is_a_minus_sign, modifiersStart, pos - modifiersStart);
                                        }
                                    }
                                    ScanExpectedChar(Ch.Colon);
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
                        ScanExpectedChar(Ch.CloseParen);
                        break;
                    case Ch.OpenBrace:
                        pos++;
                        var digitsStart = pos;
                        ScanDigits();
                        var min = tokenValue;
                        if (!anyUnicodeModeOrNonAnnexB && min.Length == 0)
                        {
                            isPreviousTermQuantifiable = true;
                            break;
                        }
                        if (CharCodeChecked(pos) == Ch.Comma)
                        {
                            pos++;
                            ScanDigits();
                            var max = tokenValue;
                            if (min.Length == 0)
                            {
                                if (max.Length != 0 || CharCodeChecked(pos) == Ch.CloseBrace)
                                {
                                    Error(DiagnosticMessages.Incomplete_quantifier_Digit_expected, digitsStart, 0);
                                }
                                else
                                {
                                    Error(DiagnosticMessages.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, start, 1, ((char)ch).ToString());
                                    isPreviousTermQuantifiable = true;
                                    break;
                                }
                            }
                            else if (max.Length != 0 && JsNumber.ParseInt(min, 10) > JsNumber.ParseInt(max, 10) && (anyUnicodeModeOrNonAnnexB || CharCodeChecked(pos) == Ch.CloseBrace))
                            {
                                Error(DiagnosticMessages.Numbers_out_of_order_in_quantifier, digitsStart, pos - digitsStart);
                            }
                        }
                        else if (min.Length == 0)
                        {
                            if (anyUnicodeModeOrNonAnnexB)
                            {
                                Error(DiagnosticMessages.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, start, 1, ((char)ch).ToString());
                            }
                            isPreviousTermQuantifiable = true;
                            break;
                        }
                        if (CharCodeChecked(pos) != Ch.CloseBrace)
                        {
                            if (anyUnicodeModeOrNonAnnexB)
                            {
                                Error(DiagnosticMessages._0_expected, pos, 0, ((char)Ch.CloseBrace).ToString());
                                pos--;
                            }
                            else
                            {
                                isPreviousTermQuantifiable = true;
                                break;
                            }
                        }
                        goto case Ch.Asterisk;
                    case Ch.Asterisk:
                    case Ch.Plus:
                    case Ch.Question:
                        pos++;
                        if (CharCodeChecked(pos) == Ch.Question)
                        {
                            // Non-greedy
                            pos++;
                        }
                        if (!isPreviousTermQuantifiable)
                        {
                            Error(DiagnosticMessages.There_is_nothing_available_for_repetition, start, pos - start);
                        }
                        isPreviousTermQuantifiable = false;
                        break;
                    case Ch.Dot:
                        pos++;
                        isPreviousTermQuantifiable = true;
                        break;
                    case Ch.OpenBracket:
                        pos++;
                        if (unicodeSetsMode)
                        {
                            ScanClassSetExpression();
                        }
                        else
                        {
                            ScanClassRanges();
                        }
                        ScanExpectedChar(Ch.CloseBracket);
                        isPreviousTermQuantifiable = true;
                        break;
                    case Ch.CloseParen:
                        if (isInGroup)
                        {
                            return;
                        }
                        goto case Ch.CloseBracket;
                    case Ch.CloseBracket:
                    case Ch.CloseBrace:
                        if (anyUnicodeModeOrNonAnnexB || ch == Ch.CloseParen)
                        {
                            Error(DiagnosticMessages.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, pos, 1, ((char)ch).ToString());
                        }
                        pos++;
                        isPreviousTermQuantifiable = true;
                        break;
                    case Ch.Slash:
                    case Ch.Bar:
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
                if (ch == Ch.EOF || !IsIdentifierPart(ch, languageVersion))
                {
                    break;
                }
                var size = CharSize(ch);
                var flag = CharacterCodeToRegularExpressionFlag(ch);
                if (flag == null)
                {
                    Error(DiagnosticMessages.Unknown_regular_expression_flag, pos, size);
                }
                else if ((currFlags & flag.Value) != 0)
                {
                    Error(DiagnosticMessages.Duplicate_regular_expression_flag, pos, size);
                }
                else if ((flag.Value & RegularExpressionFlags.Modifiers) == 0)
                {
                    Error(DiagnosticMessages.This_regular_expression_flag_cannot_be_toggled_within_a_subpattern, pos, size);
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
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), Ch.Backslash);
            switch (CharCodeChecked(pos))
            {
                case Ch.LowerK:
                    pos++;
                    if (CharCodeChecked(pos) == Ch.LessThan)
                    {
                        pos++;
                        ScanGroupName(/*isReference*/ true);
                        ScanExpectedChar(Ch.GreaterThan);
                    }
                    else if (anyUnicodeModeOrNonAnnexB || namedCaptureGroups)
                    {
                        Error(DiagnosticMessages.k_must_be_followed_by_a_capturing_group_name_enclosed_in_angle_brackets, pos - 2, 2);
                    }
                    break;
                case Ch.LowerQ:
                    if (unicodeSetsMode)
                    {
                        pos++;
                        Error(DiagnosticMessages.q_is_only_available_inside_character_class, pos - 2, 2);
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
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), Ch.Backslash);
            var ch = CharCodeChecked(pos);
            if (ch >= Ch.Digit1 && ch <= Ch.Digit9)
            {
                var start = pos;
                ScanDigits();
                decimalEscapes ??= [];
                decimalEscapes.Add(new DecimalEscape(start, pos, JsNumber.StringToNumber(tokenValue)));
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
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), Ch.Backslash);
            var ch = CharCodeChecked(pos);
            switch (ch)
            {
                case Ch.EOF:
                    Error(DiagnosticMessages.Undetermined_character_escape, pos - 1, 1);
                    return "\\";
                case Ch.LowerC:
                    pos++;
                    ch = CharCodeChecked(pos);
                    if (IsASCIILetter(ch))
                    {
                        pos++;
                        return ((char)(ch & 0x1f)).ToString();
                    }
                    if (anyUnicodeModeOrNonAnnexB)
                    {
                        Error(DiagnosticMessages.c_must_be_followed_by_an_ASCII_letter, pos - 2, 2);
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
                    return ((char)ch).ToString();
                case Ch.Caret:
                case Ch.Dollar:
                case Ch.Slash:
                case Ch.Backslash:
                case Ch.Dot:
                case Ch.Asterisk:
                case Ch.Plus:
                case Ch.Question:
                case Ch.OpenParen:
                case Ch.CloseParen:
                case Ch.OpenBracket:
                case Ch.CloseBracket:
                case Ch.OpenBrace:
                case Ch.CloseBrace:
                case Ch.Bar:
                    pos++;
                    return ((char)ch).ToString();
                default:
                    pos--;
                    return ScanEscapeSequence(
                        EscapeSequenceScanningFlags.RegularExpression
                            | (annexB ? EscapeSequenceScanningFlags.AnnexB : 0)
                            | (anyUnicodeMode ? EscapeSequenceScanningFlags.AnyUnicodeMode : 0)
                            | (atomEscape ? EscapeSequenceScanningFlags.AtomEscape : 0));
            }
        }

        void ScanGroupName(bool isReference)
        {
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), Ch.LessThan);
            tokenStart = pos;
            ScanIdentifier(CodePointChecked(pos), languageVersion);
            if (pos == tokenStart)
            {
                Error(DiagnosticMessages.Expected_a_capturing_group_name);
            }
            else if (isReference)
            {
                groupNameReferences ??= [];
                groupNameReferences.Add(new GroupNameReference(tokenStart, pos, tokenValue));
            }
            else if ((topNamedCapturingGroupsScope != null && topNamedCapturingGroupsScope.Contains(tokenValue)) || NamedCapturingGroupsScopeStackSome(tokenValue))
            {
                Error(DiagnosticMessages.Named_capturing_groups_with_the_same_name_must_be_mutually_exclusive_to_each_other, tokenStart, pos - tokenStart);
            }
            else
            {
                topNamedCapturingGroupsScope ??= new HashSet<string>(StringComparer.Ordinal);
                topNamedCapturingGroupsScope.Add(tokenValue);
                groupSpecifiers ??= [];
                if (!groupSpecifiers.Contains(tokenValue))
                {
                    groupSpecifiers.Add(tokenValue);
                }
            }
        }

        // namedCapturingGroupsScopeStack.some(group => group?.has(name))
        bool NamedCapturingGroupsScopeStackSome(string name)
        {
            foreach (var group in namedCapturingGroupsScopeStack)
            {
                if (group != null && group.Contains(name))
                {
                    return true;
                }
            }
            return false;
        }

        bool IsClassContentExit(int ch)
        {
            return ch == Ch.CloseBracket || ch == Ch.EOF || pos >= end;
        }

        // ClassRanges ::= '^'? (ClassAtom ('-' ClassAtom)?)*
        void ScanClassRanges()
        {
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), Ch.OpenBracket);
            if (CharCodeChecked(pos) == Ch.Caret)
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
                if (CharCodeChecked(pos) == Ch.Minus)
                {
                    pos++;
                    var maxCh = CharCodeChecked(pos);
                    if (IsClassContentExit(maxCh))
                    {
                        return;
                    }
                    if (minCharacter.Length == 0 && anyUnicodeModeOrNonAnnexB)
                    {
                        Error(DiagnosticMessages.A_character_class_range_must_not_be_bounded_by_another_character_class, minStart, pos - 1 - minStart);
                    }
                    var maxStart = pos;
                    var maxCharacter = ScanClassAtom();
                    if (maxCharacter.Length == 0 && anyUnicodeModeOrNonAnnexB)
                    {
                        Error(DiagnosticMessages.A_character_class_range_must_not_be_bounded_by_another_character_class, maxStart, pos - maxStart);
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
                        minCharacterValue > maxCharacterValue)
                    {
                        Error(DiagnosticMessages.Range_out_of_order_in_character_class, minStart, pos - minStart);
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
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), Ch.OpenBracket);
            var isCharacterComplement = false;
            if (CharCodeChecked(pos) == Ch.Caret)
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
            if (IsDoubleCharacter(pos, Ch.Minus) || IsDoubleCharacter(pos, Ch.Ampersand)) // text.slice(pos, pos + 2): "--" or "&&"
            {
                Error(DiagnosticMessages.Expected_a_class_set_operand);
                mayContainStrings = false;
            }
            else
            {
                operand = ScanClassSetOperand();
            }
            switch (CharCodeChecked(pos))
            {
                case Ch.Minus:
                    if (CharCodeChecked(pos + 1) == Ch.Minus)
                    {
                        if (isCharacterComplement && mayContainStrings)
                        {
                            Error(DiagnosticMessages.Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, start, pos - start);
                        }
                        expressionMayContainStrings = mayContainStrings;
                        ScanClassSetSubExpression(ClassSetExpressionType.ClassSubtraction);
                        mayContainStrings = !isCharacterComplement && expressionMayContainStrings;
                        return;
                    }
                    break;
                case Ch.Ampersand:
                    if (CharCodeChecked(pos + 1) == Ch.Ampersand)
                    {
                        ScanClassSetSubExpression(ClassSetExpressionType.ClassIntersection);
                        if (isCharacterComplement && mayContainStrings)
                        {
                            Error(DiagnosticMessages.Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, start, pos - start);
                        }
                        expressionMayContainStrings = mayContainStrings;
                        mayContainStrings = !isCharacterComplement && expressionMayContainStrings;
                        return;
                    }
                    else
                    {
                        Error(DiagnosticMessages.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, pos, 1, ((char)ch).ToString());
                    }
                    break;
                default:
                    if (isCharacterComplement && mayContainStrings)
                    {
                        Error(DiagnosticMessages.Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, start, pos - start);
                    }
                    expressionMayContainStrings = mayContainStrings;
                    break;
            }
            while (true)
            {
                ch = CharCodeChecked(pos);
                if (ch == Ch.EOF)
                {
                    break;
                }
                switch (ch)
                {
                    case Ch.Minus:
                        pos++;
                        ch = CharCodeChecked(pos);
                        if (IsClassContentExit(ch))
                        {
                            mayContainStrings = !isCharacterComplement && expressionMayContainStrings;
                            return;
                        }
                        if (ch == Ch.Minus)
                        {
                            pos++;
                            Error(DiagnosticMessages.Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, pos - 2, 2);
                            start = pos - 2;
                            operand = Slice(text, start, pos);
                            continue;
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(operand))
                            {
                                Error(DiagnosticMessages.A_character_class_range_must_not_be_bounded_by_another_character_class, start, pos - 1 - start);
                            }
                            var secondStart = pos;
                            var secondOperand = ScanClassSetOperand();
                            if (isCharacterComplement && mayContainStrings)
                            {
                                Error(DiagnosticMessages.Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, secondStart, pos - secondStart);
                            }
                            expressionMayContainStrings = expressionMayContainStrings || mayContainStrings;
                            if (secondOperand.Length == 0)
                            {
                                Error(DiagnosticMessages.A_character_class_range_must_not_be_bounded_by_another_character_class, secondStart, pos - secondStart);
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
                                minCharacterValue > maxCharacterValue)
                            {
                                Error(DiagnosticMessages.Range_out_of_order_in_character_class, start, pos - start);
                            }
                        }
                        break;
                    case Ch.Ampersand:
                        start = pos;
                        pos++;
                        if (CharCodeChecked(pos) == Ch.Ampersand)
                        {
                            pos++;
                            Error(DiagnosticMessages.Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, pos - 2, 2);
                            if (CharCodeChecked(pos) == Ch.Ampersand)
                            {
                                Error(DiagnosticMessages.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, pos, 1, ((char)ch).ToString());
                                pos++;
                            }
                        }
                        else
                        {
                            Error(DiagnosticMessages.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, pos - 1, 1, ((char)ch).ToString());
                        }
                        operand = Slice(text, start, pos);
                        continue;
                }
                if (IsClassContentExit(CharCodeChecked(pos)))
                {
                    break;
                }
                start = pos;
                if (IsDoubleCharacter(pos, Ch.Minus) || IsDoubleCharacter(pos, Ch.Ampersand)) // text.slice(pos, pos + 2): "--" or "&&"
                {
                    Error(DiagnosticMessages.Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, pos, 2);
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
                    case Ch.Minus:
                        pos++;
                        if (CharCodeChecked(pos) == Ch.Minus)
                        {
                            pos++;
                            if (expressionType != ClassSetExpressionType.ClassSubtraction)
                            {
                                Error(DiagnosticMessages.Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, pos - 2, 2);
                            }
                        }
                        else
                        {
                            Error(DiagnosticMessages.Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, pos - 1, 1);
                        }
                        break;
                    case Ch.Ampersand:
                        pos++;
                        if (CharCodeChecked(pos) == Ch.Ampersand)
                        {
                            pos++;
                            if (expressionType != ClassSetExpressionType.ClassIntersection)
                            {
                                Error(DiagnosticMessages.Operators_must_not_be_mixed_within_a_character_class_Wrap_it_in_a_nested_class_instead, pos - 2, 2);
                            }
                            if (CharCodeChecked(pos) == Ch.Ampersand)
                            {
                                Error(DiagnosticMessages.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, pos, 1, ((char)ch).ToString());
                                pos++;
                            }
                        }
                        else
                        {
                            Error(DiagnosticMessages.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, pos - 1, 1, ((char)ch).ToString());
                        }
                        break;
                    default:
                        switch (expressionType)
                        {
                            case ClassSetExpressionType.ClassSubtraction:
                                Error(DiagnosticMessages._0_expected, pos, 0, "--");
                                break;
                            case ClassSetExpressionType.ClassIntersection:
                                Error(DiagnosticMessages._0_expected, pos, 0, "&&");
                                break;
                            default:
                                break;
                        }
                        break;
                }
                ch = CharCodeChecked(pos);
                if (IsClassContentExit(ch))
                {
                    Error(DiagnosticMessages.Expected_a_class_set_operand);
                    break;
                }
                ScanClassSetOperand();
                // Used only if expressionType is Intersection
                expressionMayContainStrings = expressionMayContainStrings && mayContainStrings;
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
                case Ch.EOF:
                    return string.Empty;
                case Ch.OpenBracket:
                    pos++;
                    ScanClassSetExpression();
                    ScanExpectedChar(Ch.CloseBracket);
                    return string.Empty;
                case Ch.Backslash:
                    pos++;
                    if (ScanCharacterClassEscape())
                    {
                        return string.Empty;
                    }
                    else if (CharCodeChecked(pos) == Ch.LowerQ)
                    {
                        pos++;
                        if (CharCodeChecked(pos) == Ch.OpenBrace)
                        {
                            pos++;
                            ScanClassStringDisjunctionContents();
                            ScanExpectedChar(Ch.CloseBrace);
                            return string.Empty;
                        }
                        else
                        {
                            Error(DiagnosticMessages.q_must_be_followed_by_string_alternatives_enclosed_in_braces, pos - 2, 2);
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
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), Ch.OpenBrace);
            var characterCount = 0;
            while (true)
            {
                var ch = CharCodeChecked(pos);
                switch (ch)
                {
                    case Ch.EOF:
                        return;
                    case Ch.CloseBrace:
                        if (characterCount != 1)
                        {
                            mayContainStrings = true;
                        }
                        return;
                    case Ch.Bar:
                        if (characterCount != 1)
                        {
                            mayContainStrings = true;
                        }
                        pos++;
                        // TypeScript assigns `start = pos` here; `start` is the (otherwise unused) `start` parameter of createScanner,
                        // so the assignment has no effect.
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
            if (ch == Ch.EOF)
            {
                // no need to report an error, the initial scan will already have reported that the RegExp is unterminated.
                return string.Empty;
            }
            if (ch == Ch.Backslash)
            {
                pos++;
                var escapedCh = CharCodeChecked(pos);
                switch (escapedCh)
                {
                    case Ch.LowerB:
                        pos++;
                        return "\b";
                    case Ch.Ampersand:
                    case Ch.Minus:
                    case Ch.Exclamation:
                    case Ch.Hash:
                    case Ch.Percent:
                    case Ch.Comma:
                    case Ch.Colon:
                    case Ch.Semicolon:
                    case Ch.LessThan:
                    case Ch.EqualsSign:
                    case Ch.GreaterThan:
                    case Ch.At:
                    case Ch.Backtick:
                    case Ch.Tilde:
                        pos++;
                        return ((char)escapedCh).ToString();
                    default:
                        return ScanCharacterEscape(/*atomEscape*/ false);
                }
            }
            else if (ch == CharCodeChecked(pos + 1))
            {
                switch (ch)
                {
                    case Ch.Ampersand:
                    case Ch.Exclamation:
                    case Ch.Hash:
                    case Ch.Percent:
                    case Ch.Asterisk:
                    case Ch.Plus:
                    case Ch.Comma:
                    case Ch.Dot:
                    case Ch.Colon:
                    case Ch.Semicolon:
                    case Ch.LessThan:
                    case Ch.EqualsSign:
                    case Ch.GreaterThan:
                    case Ch.Question:
                    case Ch.At:
                    case Ch.Backtick:
                    case Ch.Tilde:
                        Error(DiagnosticMessages.A_character_class_must_not_contain_a_reserved_double_punctuator_Did_you_mean_to_escape_it_with_backslash, pos, 2);
                        pos += 2;
                        return Substring(text, pos - 2, pos);
                }
            }
            switch (ch)
            {
                case Ch.Slash:
                case Ch.OpenParen:
                case Ch.CloseParen:
                case Ch.OpenBracket:
                case Ch.CloseBracket:
                case Ch.OpenBrace:
                case Ch.CloseBrace:
                case Ch.Minus:
                case Ch.Bar:
                    Error(DiagnosticMessages.Unexpected_0_Did_you_mean_to_escape_it_with_backslash, pos, 1, ((char)ch).ToString());
                    pos++;
                    return ((char)ch).ToString();
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
            if (CharCodeChecked(pos) == Ch.Backslash)
            {
                pos++;
                var ch = CharCodeChecked(pos);
                switch (ch)
                {
                    case Ch.LowerB:
                        pos++;
                        return "\b";
                    case Ch.Minus:
                        pos++;
                        return ((char)ch).ToString();
                    default:
                        if (ScanCharacterClassEscape())
                        {
                            return string.Empty;
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
            Debug.AssertEqual(CharCodeUnchecked(pos - 1), Ch.Backslash);
            var isCharacterComplement = false;
            var start = pos - 1;
            var ch = CharCodeChecked(pos);
            switch (ch)
            {
                case Ch.LowerD:
                case Ch.UpperD:
                case Ch.LowerS:
                case Ch.UpperS:
                case Ch.LowerW:
                case Ch.UpperW:
                    pos++;
                    return true;
                case Ch.UpperP:
                    isCharacterComplement = true;
                    goto case Ch.LowerP;
                case Ch.LowerP:
                    pos++;
                    if (CharCodeChecked(pos) == Ch.OpenBrace)
                    {
                        pos++;
                        var propertyNameOrValueStart = pos;
                        var propertyNameOrValue = ScanWordCharacters();
                        if (CharCodeChecked(pos) == Ch.EqualsSign)
                        {
                            NonBinaryUnicodeProperties.TryGetValue(propertyNameOrValue, out var propertyName);
                            if (pos == propertyNameOrValueStart)
                            {
                                Error(DiagnosticMessages.Expected_a_Unicode_property_name);
                            }
                            else if (propertyName == null)
                            {
                                Error(DiagnosticMessages.Unknown_Unicode_property_name, propertyNameOrValueStart, pos - propertyNameOrValueStart);
                                var suggestion = Core.GetSpellingSuggestion(propertyNameOrValue, NonBinaryUnicodePropertyKeys, static x => x);
                                if (!string.IsNullOrEmpty(suggestion))
                                {
                                    Error(DiagnosticMessages.Did_you_mean_0, propertyNameOrValueStart, pos - propertyNameOrValueStart, suggestion);
                                }
                            }
                            pos++;
                            var propertyValueStart = pos;
                            var propertyValue = ScanWordCharacters();
                            if (pos == propertyValueStart)
                            {
                                Error(DiagnosticMessages.Expected_a_Unicode_property_value);
                            }
                            else if (propertyName != null && !ValuesOfNonBinaryUnicodeProperty(propertyName).Has(propertyValue))
                            {
                                Error(DiagnosticMessages.Unknown_Unicode_property_value, propertyValueStart, pos - propertyValueStart);
                                var suggestion = Core.GetSpellingSuggestion(propertyValue, ValuesOfNonBinaryUnicodeProperty(propertyName).Items, static x => x);
                                if (!string.IsNullOrEmpty(suggestion))
                                {
                                    Error(DiagnosticMessages.Did_you_mean_0, propertyValueStart, pos - propertyValueStart, suggestion);
                                }
                            }
                        }
                        else
                        {
                            if (pos == propertyNameOrValueStart)
                            {
                                Error(DiagnosticMessages.Expected_a_Unicode_property_name_or_value);
                            }
                            else if (BinaryUnicodePropertiesOfStrings.Has(propertyNameOrValue))
                            {
                                if (!unicodeSetsMode)
                                {
                                    Error(DiagnosticMessages.Any_Unicode_property_that_would_possibly_match_more_than_a_single_character_is_only_available_when_the_Unicode_Sets_v_flag_is_set, propertyNameOrValueStart, pos - propertyNameOrValueStart);
                                }
                                else if (isCharacterComplement)
                                {
                                    Error(DiagnosticMessages.Anything_that_would_possibly_match_more_than_a_single_character_is_invalid_inside_a_negated_character_class, propertyNameOrValueStart, pos - propertyNameOrValueStart);
                                }
                                else
                                {
                                    mayContainStrings = true;
                                }
                            }
                            else if (!ValuesOfGeneralCategory.Has(propertyNameOrValue) && !BinaryUnicodeProperties.Has(propertyNameOrValue))
                            {
                                Error(DiagnosticMessages.Unknown_Unicode_property_name_or_value, propertyNameOrValueStart, pos - propertyNameOrValueStart);
                                var candidates = new List<string>(ValuesOfGeneralCategory.Items);
                                candidates.AddRange(BinaryUnicodeProperties.Items);
                                candidates.AddRange(BinaryUnicodePropertiesOfStrings.Items);
                                var suggestion = Core.GetSpellingSuggestion(propertyNameOrValue, candidates, static x => x);
                                if (!string.IsNullOrEmpty(suggestion))
                                {
                                    Error(DiagnosticMessages.Did_you_mean_0, propertyNameOrValueStart, pos - propertyNameOrValueStart, suggestion);
                                }
                            }
                        }
                        ScanExpectedChar(Ch.CloseBrace);
                        if (!anyUnicodeMode)
                        {
                            Error(DiagnosticMessages.Unicode_property_value_expressions_are_only_available_when_the_Unicode_u_flag_or_the_Unicode_Sets_v_flag_is_set, start, pos - start);
                        }
                    }
                    else if (anyUnicodeModeOrNonAnnexB)
                    {
                        Error(DiagnosticMessages._0_must_be_followed_by_a_Unicode_property_value_expression_enclosed_in_braces, pos - 2, 2, ((char)ch).ToString());
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
            var value = string.Empty;
            while (true)
            {
                var ch = CharCodeChecked(pos);
                if (ch == Ch.EOF || !IsWordCharacter(ch))
                {
                    break;
                }
                value += ((char)ch).ToString();
                pos++;
            }
            return value;
        }

        string ScanSourceCharacter()
        {
            var size = anyUnicodeMode ? CharSize(CodePointChecked(pos)) : 1;
            pos += size;
            return size > 0 ? Substring(text, pos - size, pos) : string.Empty;
        }

        void ScanExpectedChar(int ch)
        {
            if (CharCodeChecked(pos) == ch)
            {
                pos++;
            }
            else
            {
                Error(DiagnosticMessages._0_expected, pos, 0, ((char)ch).ToString());
            }
        }

        // text.slice(position, position + 2) is the two-character string made of `ch` twice ("--" or "&&").
        bool IsDoubleCharacter(int position, int ch)
        {
            return CharCodeUnchecked(position) == ch && CharCodeUnchecked(position + 1) == ch;
        }
    }
}
