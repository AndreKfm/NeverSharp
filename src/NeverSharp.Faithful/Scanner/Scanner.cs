// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/scanner.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/scanner/scanner.go and internal/scanner/utilities.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

// The root namespace `Diagnostics` of some referenced assemblies (the benchmark project compiles these sources into its own
// assembly, together with BenchmarkDotNet) would hide the generated class `Diagnostics`, TypeScript's table of messages: alias it.
using DiagnosticMessages = NeverSharp.Generated.Diagnostics;

namespace NeverSharp.Faithful;

/// <summary>
/// scanner.ts. The instances are the objects `createScanner` returns (TypeScript's `interface Scanner`);
/// the file-level functions of scanner.ts are the static members.
/// </summary>
internal sealed partial class Scanner
{
    /// <summary>
    /// What JavaScript's `charCodeAt`/`codePointAt` return for a position outside the string (NaN/undefined):
    /// unequal to every character code and outside every range comparison.
    /// </summary>
    private const int Nan = int.MinValue;

    // scanner.ts: textToKeywordObj (a MapLike; the order of the entries is the order of its keys).
    internal static readonly (string Text, SyntaxKind Kind)[] TextToKeywordObj =
    [
        ("abstract", SyntaxKind.AbstractKeyword),
        ("accessor", SyntaxKind.AccessorKeyword),
        ("any", SyntaxKind.AnyKeyword),
        ("as", SyntaxKind.AsKeyword),
        ("asserts", SyntaxKind.AssertsKeyword),
        ("assert", SyntaxKind.AssertKeyword),
        ("bigint", SyntaxKind.BigIntKeyword),
        ("boolean", SyntaxKind.BooleanKeyword),
        ("break", SyntaxKind.BreakKeyword),
        ("case", SyntaxKind.CaseKeyword),
        ("catch", SyntaxKind.CatchKeyword),
        ("class", SyntaxKind.ClassKeyword),
        ("continue", SyntaxKind.ContinueKeyword),
        ("const", SyntaxKind.ConstKeyword),
        ("constructor", SyntaxKind.ConstructorKeyword),
        ("debugger", SyntaxKind.DebuggerKeyword),
        ("declare", SyntaxKind.DeclareKeyword),
        ("default", SyntaxKind.DefaultKeyword),
        ("defer", SyntaxKind.DeferKeyword),
        ("delete", SyntaxKind.DeleteKeyword),
        ("do", SyntaxKind.DoKeyword),
        ("else", SyntaxKind.ElseKeyword),
        ("enum", SyntaxKind.EnumKeyword),
        ("export", SyntaxKind.ExportKeyword),
        ("extends", SyntaxKind.ExtendsKeyword),
        ("false", SyntaxKind.FalseKeyword),
        ("finally", SyntaxKind.FinallyKeyword),
        ("for", SyntaxKind.ForKeyword),
        ("from", SyntaxKind.FromKeyword),
        ("function", SyntaxKind.FunctionKeyword),
        ("get", SyntaxKind.GetKeyword),
        ("if", SyntaxKind.IfKeyword),
        ("implements", SyntaxKind.ImplementsKeyword),
        ("import", SyntaxKind.ImportKeyword),
        ("in", SyntaxKind.InKeyword),
        ("infer", SyntaxKind.InferKeyword),
        ("instanceof", SyntaxKind.InstanceOfKeyword),
        ("interface", SyntaxKind.InterfaceKeyword),
        ("intrinsic", SyntaxKind.IntrinsicKeyword),
        ("is", SyntaxKind.IsKeyword),
        ("keyof", SyntaxKind.KeyOfKeyword),
        ("let", SyntaxKind.LetKeyword),
        ("module", SyntaxKind.ModuleKeyword),
        ("namespace", SyntaxKind.NamespaceKeyword),
        ("never", SyntaxKind.NeverKeyword),
        ("new", SyntaxKind.NewKeyword),
        ("null", SyntaxKind.NullKeyword),
        ("number", SyntaxKind.NumberKeyword),
        ("object", SyntaxKind.ObjectKeyword),
        ("package", SyntaxKind.PackageKeyword),
        ("private", SyntaxKind.PrivateKeyword),
        ("protected", SyntaxKind.ProtectedKeyword),
        ("public", SyntaxKind.PublicKeyword),
        ("override", SyntaxKind.OverrideKeyword),
        ("out", SyntaxKind.OutKeyword),
        ("readonly", SyntaxKind.ReadonlyKeyword),
        ("require", SyntaxKind.RequireKeyword),
        ("global", SyntaxKind.GlobalKeyword),
        ("return", SyntaxKind.ReturnKeyword),
        ("satisfies", SyntaxKind.SatisfiesKeyword),
        ("set", SyntaxKind.SetKeyword),
        ("static", SyntaxKind.StaticKeyword),
        ("string", SyntaxKind.StringKeyword),
        ("super", SyntaxKind.SuperKeyword),
        ("switch", SyntaxKind.SwitchKeyword),
        ("symbol", SyntaxKind.SymbolKeyword),
        ("this", SyntaxKind.ThisKeyword),
        ("throw", SyntaxKind.ThrowKeyword),
        ("true", SyntaxKind.TrueKeyword),
        ("try", SyntaxKind.TryKeyword),
        ("type", SyntaxKind.TypeKeyword),
        ("typeof", SyntaxKind.TypeOfKeyword),
        ("undefined", SyntaxKind.UndefinedKeyword),
        ("unique", SyntaxKind.UniqueKeyword),
        ("unknown", SyntaxKind.UnknownKeyword),
        ("using", SyntaxKind.UsingKeyword),
        ("var", SyntaxKind.VarKeyword),
        ("void", SyntaxKind.VoidKeyword),
        ("while", SyntaxKind.WhileKeyword),
        ("with", SyntaxKind.WithKeyword),
        ("yield", SyntaxKind.YieldKeyword),
        ("async", SyntaxKind.AsyncKeyword),
        ("await", SyntaxKind.AwaitKeyword),
        ("of", SyntaxKind.OfKeyword),
    ];

    // scanner.ts: the punctuation entries of textToToken, after the spread of textToKeywordObj.
    private static readonly (string Text, SyntaxKind Kind)[] PunctuationTokens =
    [
        ("{", SyntaxKind.OpenBraceToken),
        ("}", SyntaxKind.CloseBraceToken),
        ("(", SyntaxKind.OpenParenToken),
        (")", SyntaxKind.CloseParenToken),
        ("[", SyntaxKind.OpenBracketToken),
        ("]", SyntaxKind.CloseBracketToken),
        (".", SyntaxKind.DotToken),
        ("...", SyntaxKind.DotDotDotToken),
        (";", SyntaxKind.SemicolonToken),
        (",", SyntaxKind.CommaToken),
        ("<", SyntaxKind.LessThanToken),
        (">", SyntaxKind.GreaterThanToken),
        ("<=", SyntaxKind.LessThanEqualsToken),
        (">=", SyntaxKind.GreaterThanEqualsToken),
        ("==", SyntaxKind.EqualsEqualsToken),
        ("!=", SyntaxKind.ExclamationEqualsToken),
        ("===", SyntaxKind.EqualsEqualsEqualsToken),
        ("!==", SyntaxKind.ExclamationEqualsEqualsToken),
        ("=>", SyntaxKind.EqualsGreaterThanToken),
        ("+", SyntaxKind.PlusToken),
        ("-", SyntaxKind.MinusToken),
        ("**", SyntaxKind.AsteriskAsteriskToken),
        ("*", SyntaxKind.AsteriskToken),
        ("/", SyntaxKind.SlashToken),
        ("%", SyntaxKind.PercentToken),
        ("++", SyntaxKind.PlusPlusToken),
        ("--", SyntaxKind.MinusMinusToken),
        ("<<", SyntaxKind.LessThanLessThanToken),
        ("</", SyntaxKind.LessThanSlashToken),
        (">>", SyntaxKind.GreaterThanGreaterThanToken),
        (">>>", SyntaxKind.GreaterThanGreaterThanGreaterThanToken),
        ("&", SyntaxKind.AmpersandToken),
        ("|", SyntaxKind.BarToken),
        ("^", SyntaxKind.CaretToken),
        ("!", SyntaxKind.ExclamationToken),
        ("~", SyntaxKind.TildeToken),
        ("&&", SyntaxKind.AmpersandAmpersandToken),
        ("||", SyntaxKind.BarBarToken),
        ("?", SyntaxKind.QuestionToken),
        ("??", SyntaxKind.QuestionQuestionToken),
        ("?.", SyntaxKind.QuestionDotToken),
        (":", SyntaxKind.ColonToken),
        ("=", SyntaxKind.EqualsToken),
        ("+=", SyntaxKind.PlusEqualsToken),
        ("-=", SyntaxKind.MinusEqualsToken),
        ("*=", SyntaxKind.AsteriskEqualsToken),
        ("**=", SyntaxKind.AsteriskAsteriskEqualsToken),
        ("/=", SyntaxKind.SlashEqualsToken),
        ("%=", SyntaxKind.PercentEqualsToken),
        ("<<=", SyntaxKind.LessThanLessThanEqualsToken),
        (">>=", SyntaxKind.GreaterThanGreaterThanEqualsToken),
        (">>>=", SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken),
        ("&=", SyntaxKind.AmpersandEqualsToken),
        ("|=", SyntaxKind.BarEqualsToken),
        ("^=", SyntaxKind.CaretEqualsToken),
        ("||=", SyntaxKind.BarBarEqualsToken),
        ("&&=", SyntaxKind.AmpersandAmpersandEqualsToken),
        ("??=", SyntaxKind.QuestionQuestionEqualsToken),
        ("@", SyntaxKind.AtToken),
        ("#", SyntaxKind.HashToken),
        ("`", SyntaxKind.BacktickToken),
    ];

    // scanner.ts: textToKeyword (a Map built from textToKeywordObj).
    private static readonly Dictionary<string, SyntaxKind> TextToKeyword = BuildTextToKeyword();

    // scanner.ts: textToToken.
    private static readonly Dictionary<string, SyntaxKind> TextToToken = BuildTextToToken();

    // scanner.ts: tokenStrings = makeReverseMap(textToToken).
    private static readonly string?[] TokenStrings = BuildTokenStrings();

    // scanner.ts: charCodeToRegExpFlag.
    private static readonly Dictionary<int, RegularExpressionFlags> CharCodeToRegExpFlag = new()
    {
        [Ch.LowerD] = RegularExpressionFlags.HasIndices,
        [Ch.LowerG] = RegularExpressionFlags.Global,
        [Ch.LowerI] = RegularExpressionFlags.IgnoreCase,
        [Ch.LowerM] = RegularExpressionFlags.Multiline,
        [Ch.LowerS] = RegularExpressionFlags.DotAll,
        [Ch.LowerU] = RegularExpressionFlags.Unicode,
        [Ch.LowerV] = RegularExpressionFlags.UnicodeSets,
        [Ch.LowerY] = RegularExpressionFlags.Sticky,
    };

    // scanner.ts: regExpFlagToFirstAvailableLanguageVersion.
    private static readonly Dictionary<RegularExpressionFlags, ScriptTarget> RegExpFlagToFirstAvailableLanguageVersion = new()
    {
        [RegularExpressionFlags.HasIndices] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsHasIndices,
        [RegularExpressionFlags.DotAll] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsDotAll,
        [RegularExpressionFlags.Unicode] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsUnicode,
        [RegularExpressionFlags.UnicodeSets] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsUnicodeSets,
        [RegularExpressionFlags.Sticky] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsSticky,
    };

    // scanner.ts: mergeConflictMarkerLength = "<<<<<<<".length.
    private const int MergeConflictMarkerLength = 7;

    private static Dictionary<string, SyntaxKind> BuildTextToKeyword()
    {
        var result = new Dictionary<string, SyntaxKind>(TextToKeywordObj.Length, StringComparer.Ordinal);
        foreach (var (text, kind) in TextToKeywordObj)
        {
            result[text] = kind;
        }
        return result;
    }

    private static Dictionary<string, SyntaxKind> BuildTextToToken()
    {
        var result = new Dictionary<string, SyntaxKind>(TextToKeywordObj.Length + PunctuationTokens.Length, StringComparer.Ordinal);
        foreach (var (text, kind) in TextToKeywordObj)
        {
            result[text] = kind;
        }
        foreach (var (text, kind) in PunctuationTokens)
        {
            result[text] = kind;
        }
        return result;
    }

    // scanner.ts: makeReverseMap(textToToken), specialised to the token table (its only use that the parser reaches). The array is
    // indexed by SyntaxKind; entries that no text maps to stay undefined (null). The entries are visited in the insertion order
    // of textToToken (keywords, then punctuation), so a later text wins over an earlier one for the same kind, as in TypeScript.
    private static string?[] BuildTokenStrings()
    {
        var length = 0;
        foreach (var (_, kind) in TextToKeywordObj)
        {
            length = Math.Max(length, (int)kind + 1);
        }
        foreach (var (_, kind) in PunctuationTokens)
        {
            length = Math.Max(length, (int)kind + 1);
        }
        var result = new string?[length];
        foreach (var (text, kind) in TextToKeywordObj)
        {
            result[(int)kind] = text;
        }
        foreach (var (text, kind) in PunctuationTokens)
        {
            result[(int)kind] = text;
        }
        return result;
    }

    /// <summary>scanner.ts: tokenIsIdentifierOrKeyword.</summary>
    internal static bool TokenIsIdentifierOrKeyword(SyntaxKind token)
    {
        return token >= SyntaxKind.Identifier;
    }

    /// <summary>scanner.ts: tokenIsIdentifierOrKeywordOrGreaterThan.</summary>
    internal static bool TokenIsIdentifierOrKeywordOrGreaterThan(SyntaxKind token)
    {
        return token == SyntaxKind.GreaterThanToken || TokenIsIdentifierOrKeyword(token);
    }

    /// <summary>scanner.ts: lookupInUnicodeMap.</summary>
    private static bool LookupInUnicodeMap(int code, int[] map)
    {
        // Bail out quickly if it couldn't possibly be in the map.
        if (code < map[0])
        {
            return false;
        }

        // Perform binary search in one of the Unicode range maps
        var lo = 0;
        var hi = map.Length;
        int mid;

        while (lo + 1 < hi)
        {
            mid = lo + ((hi - lo) / 2);
            // mid has to be even to catch a range's beginning
            mid -= mid % 2;
            if (map[mid] <= code && code <= map[mid + 1])
            {
                return true;
            }

            if (code < map[mid])
            {
                hi = mid;
            }
            else
            {
                lo = mid + 2;
            }
        }

        return false;
    }

    /// <summary>scanner.ts: isUnicodeIdentifierStart.</summary>
    internal static bool IsUnicodeIdentifierStart(int code, ScriptTarget? languageVersion)
    {
        return languageVersion >= ScriptTarget.ES2015 ?
            LookupInUnicodeMap(code, UnicodeESNextIdentifierStart) :
            LookupInUnicodeMap(code, UnicodeES5IdentifierStart);
    }

    /// <summary>scanner.ts: isUnicodeIdentifierPart.</summary>
    private static bool IsUnicodeIdentifierPart(int code, ScriptTarget? languageVersion)
    {
        return languageVersion >= ScriptTarget.ES2015 ?
            LookupInUnicodeMap(code, UnicodeESNextIdentifierPart) :
            LookupInUnicodeMap(code, UnicodeES5IdentifierPart);
    }

    /// <summary>scanner.ts: tokenToString (`tokenStrings[t]`).</summary>
    internal static string? TokenToString(SyntaxKind t)
    {
        var index = (int)t;
        return (uint)index < (uint)TokenStrings.Length ? TokenStrings[index] : null;
    }

    /// <summary>scanner.ts: stringToToken.</summary>
    internal static SyntaxKind? StringToToken(string s)
    {
        return TextToToken.TryGetValue(s, out var kind) ? kind : null;
    }

    /// <summary>scanner.ts: characterCodeToRegularExpressionFlag.</summary>
    internal static RegularExpressionFlags? CharacterCodeToRegularExpressionFlag(int ch)
    {
        return CharCodeToRegExpFlag.TryGetValue(ch, out var flag) ? flag : null;
    }

    /// <summary>scanner.ts: computeLineStarts.</summary>
    internal static List<int> ComputeLineStarts(string text)
    {
        var result = new List<int>();
        var pos = 0;
        var lineStart = 0;
        while (pos < text.Length)
        {
            var ch = text[pos];
            pos++;
            switch (ch)
            {
                case (char)Ch.CarriageReturn:
                    if (CharCodeAt(text, pos) == Ch.LineFeed)
                    {
                        pos++;
                    }
                    result.Add(lineStart);
                    lineStart = pos;
                    break;
                case (char)Ch.LineFeed:
                    result.Add(lineStart);
                    lineStart = pos;
                    break;
                default:
                    if (ch > Ch.MaxAsciiCharacter && IsLineBreak(ch))
                    {
                        result.Add(lineStart);
                        lineStart = pos;
                    }
                    break;
            }
        }
        result.Add(lineStart);
        return result;
    }

    /// <summary>scanner.ts: getLineStarts.</summary>
    internal static IReadOnlyList<int> GetLineStarts(SourceFileLike sourceFile)
    {
        return sourceFile.LineMap ?? (sourceFile.LineMap = ComputeLineStarts(sourceFile.Text));
    }

    /// <summary>scanner.ts: computeLineAndCharacterOfPosition.</summary>
    internal static LineAndCharacter ComputeLineAndCharacterOfPosition(IReadOnlyList<int> lineStarts, int position)
    {
        var lineNumber = ComputeLineOfPosition(lineStarts, position);
        return new LineAndCharacter(lineNumber, position - lineStarts[lineNumber]);
    }

    /// <summary>
    /// scanner.ts: computeLineOfPosition.
    /// We assume the first line starts at position 0 and 'position' is non-negative.
    /// </summary>
    internal static int ComputeLineOfPosition(IReadOnlyList<int> lineStarts, int position, int? lowerBound = null)
    {
        var lineNumber = Core.BinarySearch<int, int>(lineStarts, position, static x => x, static (a, b) => Core.CompareValues(a, b), lowerBound);
        if (lineNumber < 0)
        {
            // If the actual position was not found,
            // the binary search returns the 2's-complement of the next line start
            // e.g. if the line starts at [5, 10, 23, 80] and the position requested was 20
            // then the search will return -2.
            //
            // We want the index of the previous line start, so we subtract 1.
            // Review 2's-complement if this is confusing.
            lineNumber = ~lineNumber - 1;
            Debug.Assert(lineNumber != -1, "position cannot precede the beginning of the file");
        }
        return lineNumber;
    }

    /// <summary>scanner.ts: getLineAndCharacterOfPosition.</summary>
    internal static LineAndCharacter GetLineAndCharacterOfPosition(SourceFileLike sourceFile, int position)
    {
        return ComputeLineAndCharacterOfPosition(GetLineStarts(sourceFile), position);
    }

    /// <summary>scanner.ts: isWhiteSpaceLike.</summary>
    internal static bool IsWhiteSpaceLike(int ch)
    {
        return IsWhiteSpaceSingleLine(ch) || IsLineBreak(ch);
    }

    /// <summary>scanner.ts: isWhiteSpaceSingleLine. Does not include line breaks. For that, see isWhiteSpaceLike.</summary>
    internal static bool IsWhiteSpaceSingleLine(int ch)
    {
        // Note: nextLine is in the Zs space, and should be considered to be a whitespace.
        // It is explicitly not a line-break as it isn't in the exact set specified by EcmaScript.
        return ch == Ch.Space ||
            ch == Ch.Tab ||
            ch == Ch.VerticalTab ||
            ch == Ch.FormFeed ||
            ch == Ch.NonBreakingSpace ||
            ch == Ch.NextLine ||
            ch == Ch.Ogham ||
            (ch >= Ch.EnQuad && ch <= Ch.ZeroWidthSpace) ||
            ch == Ch.NarrowNoBreakSpace ||
            ch == Ch.MathematicalSpace ||
            ch == Ch.IdeographicSpace ||
            ch == Ch.ByteOrderMark;
    }

    /// <summary>scanner.ts: isLineBreak.</summary>
    internal static bool IsLineBreak(int ch)
    {
        // ES5 7.3:
        // The ECMAScript line terminator characters are listed in Table 3.
        //     Table 3: Line Terminator Characters
        //     Code Unit Value     Name                    Formal Name
        //     \u000A              Line Feed               <LF>
        //     \u000D              Carriage Return         <CR>
        //     U+2028              Line separator          <LS>
        //     U+2029              Paragraph separator     <PS>
        // Only the characters in Table 3 are treated as line terminators. Other new line or line
        // breaking characters are treated as white space but not as line terminators.

        return ch == Ch.LineFeed ||
            ch == Ch.CarriageReturn ||
            ch == Ch.LineSeparator ||
            ch == Ch.ParagraphSeparator;
    }

    /// <summary>scanner.ts: isDigit.</summary>
    private static bool IsDigit(int ch)
    {
        return ch >= Ch.Digit0 && ch <= Ch.Digit9;
    }

    /// <summary>scanner.ts: isHexDigit.</summary>
    private static bool IsHexDigit(int ch)
    {
        return IsDigit(ch) || (ch >= Ch.UpperA && ch <= Ch.UpperF) || (ch >= Ch.LowerA && ch <= Ch.LowerF);
    }

    /// <summary>scanner.ts: isASCIILetter.</summary>
    private static bool IsASCIILetter(int ch)
    {
        return (ch >= Ch.UpperA && ch <= Ch.UpperZ) || (ch >= Ch.LowerA && ch <= Ch.LowerZ);
    }

    /// <summary>scanner.ts: isWordCharacter (Section 6.1.4).</summary>
    private static bool IsWordCharacter(int ch)
    {
        return IsASCIILetter(ch) || IsDigit(ch) || ch == Ch.Underscore;
    }

    /// <summary>scanner.ts: isOctalDigit.</summary>
    private static bool IsOctalDigit(int ch)
    {
        return ch >= Ch.Digit0 && ch <= Ch.Digit7;
    }

    /// <summary>`text.charCodeAt(pos)`: the UTF-16 code unit, or <see cref="Nan"/> outside the string.</summary>
    private static int CharCodeAt(string text, int pos)
    {
        return (uint)pos < (uint)text.Length ? text[pos] : Nan;
    }

    /// <summary>`text.substring(start, end)`: arguments below 0 count as 0, above the length as the length, and are swapped if start is greater than end.</summary>
    private static string Substring(string text, int start, int end)
    {
        start = Math.Min(Math.Max(start, 0), text.Length);
        end = Math.Min(Math.Max(end, 0), text.Length);
        return start <= end ? text.Substring(start, end - start) : text.Substring(end, start - end);
    }

    /// <summary>`text.slice(start, end)` for non-negative arguments.</summary>
    private static string Slice(string text, int start, int end)
    {
        start = Math.Min(start, text.Length);
        end = Math.Min(end, text.Length);
        return start < end ? text.Substring(start, end - start) : string.Empty;
    }

    /// <summary>The truthiness of a JavaScript value (the callbacks of the speculation helpers return any type).</summary>
    private static bool IsTruthy<T>(T value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            string s => s.Length != 0,
            int i => i != 0,
            double d => d != 0 && !double.IsNaN(d),
            _ => true,
        };
    }

    /// <summary>scanner.ts: couldStartTrivia.</summary>
    internal static bool CouldStartTrivia(string text, int pos)
    {
        // Keep in sync with skipTrivia
        var ch = CharCodeAt(text, pos);
        switch (ch)
        {
            case Ch.CarriageReturn:
            case Ch.LineFeed:
            case Ch.Tab:
            case Ch.VerticalTab:
            case Ch.FormFeed:
            case Ch.Space:
            case Ch.Slash:
            // starts of normal trivia
            case Ch.LessThan:
            case Ch.Bar:
            case Ch.EqualsSign:
            case Ch.GreaterThan:
                // Starts of conflict marker trivia
                return true;
            case Ch.Hash:
                // Only if its the beginning can we have #! trivia
                return pos == 0;
            default:
                return ch > Ch.MaxAsciiCharacter;
        }
    }

    /// <summary>scanner.ts: skipTrivia.</summary>
    internal static int SkipTrivia(string text, int pos, bool stopAfterLineBreak = false, bool stopAtComments = false, bool inJSDoc = false)
    {
        if (Utilities.PositionIsSynthesized(pos))
        {
            return pos;
        }

        var canConsumeStar = false;
        // Keep in sync with couldStartTrivia
        while (true)
        {
            var ch = CharCodeAt(text, pos);
            switch (ch)
            {
                case Ch.CarriageReturn:
                    if (CharCodeAt(text, pos + 1) == Ch.LineFeed)
                    {
                        pos++;
                    }
                    goto case Ch.LineFeed;
                case Ch.LineFeed:
                    pos++;
                    if (stopAfterLineBreak)
                    {
                        return pos;
                    }
                    canConsumeStar = inJSDoc;
                    continue;
                case Ch.Tab:
                case Ch.VerticalTab:
                case Ch.FormFeed:
                case Ch.Space:
                    pos++;
                    continue;
                case Ch.Slash:
                    if (stopAtComments)
                    {
                        break;
                    }
                    if (CharCodeAt(text, pos + 1) == Ch.Slash)
                    {
                        pos += 2;
                        while (pos < text.Length)
                        {
                            if (IsLineBreak(text[pos]))
                            {
                                break;
                            }
                            pos++;
                        }
                        canConsumeStar = false;
                        continue;
                    }
                    if (CharCodeAt(text, pos + 1) == Ch.Asterisk)
                    {
                        pos += 2;
                        while (pos < text.Length)
                        {
                            if (text[pos] == Ch.Asterisk && CharCodeAt(text, pos + 1) == Ch.Slash)
                            {
                                pos += 2;
                                break;
                            }
                            pos++;
                        }
                        canConsumeStar = false;
                        continue;
                    }
                    break;

                case Ch.LessThan:
                case Ch.Bar:
                case Ch.EqualsSign:
                case Ch.GreaterThan:
                    if (IsConflictMarkerTrivia(text, pos))
                    {
                        pos = ScanConflictMarkerTrivia(text, pos);
                        canConsumeStar = false;
                        continue;
                    }
                    break;

                case Ch.Hash:
                    if (pos == 0 && IsShebangTrivia(text, pos))
                    {
                        pos = ScanShebangTrivia(text, pos);
                        canConsumeStar = false;
                        continue;
                    }
                    break;

                case Ch.Asterisk:
                    if (canConsumeStar)
                    {
                        pos++;
                        canConsumeStar = false;
                        continue;
                    }
                    break;

                default:
                    if (ch > Ch.MaxAsciiCharacter && IsWhiteSpaceLike(ch))
                    {
                        pos++;
                        continue;
                    }
                    break;
            }
            return pos;
        }
    }

    /// <summary>scanner.ts: isConflictMarkerTrivia.</summary>
    private static bool IsConflictMarkerTrivia(string text, int pos)
    {
        Debug.Assert(pos >= 0);

        // Conflict markers must be at the start of a line.
        if (pos == 0 || IsLineBreak(CharCodeAt(text, pos - 1)))
        {
            var ch = CharCodeAt(text, pos);

            if ((pos + MergeConflictMarkerLength) < text.Length)
            {
                for (var i = 0; i < MergeConflictMarkerLength; i++)
                {
                    if (CharCodeAt(text, pos + i) != ch)
                    {
                        return false;
                    }
                }

                return ch == Ch.EqualsSign ||
                    CharCodeAt(text, pos + MergeConflictMarkerLength) == Ch.Space;
            }
        }

        return false;
    }

    /// <summary>scanner.ts: scanConflictMarkerTrivia. `error` reports (message, pos, length) as the optional callback of TypeScript does.</summary>
    private static int ScanConflictMarkerTrivia(string text, int pos, Action<DiagnosticMessage, int, int>? error = null)
    {
        error?.Invoke(DiagnosticMessages.Merge_conflict_marker_encountered, pos, MergeConflictMarkerLength);

        var ch = CharCodeAt(text, pos);
        var len = text.Length;

        if (ch == Ch.LessThan || ch == Ch.GreaterThan)
        {
            while (pos < len && !IsLineBreak(text[pos]))
            {
                pos++;
            }
        }
        else
        {
            Debug.Assert(ch == Ch.Bar || ch == Ch.EqualsSign);
            // Consume everything from the start of a ||||||| or ======= marker to the start
            // of the next ======= or >>>>>>> marker.
            while (pos < len)
            {
                int currentChar = text[pos];
                if ((currentChar == Ch.EqualsSign || currentChar == Ch.GreaterThan) && currentChar != ch && IsConflictMarkerTrivia(text, pos))
                {
                    break;
                }

                pos++;
            }
        }

        return pos;
    }

    // scanner.ts: shebangTriviaRegex = /^#!.*/ (`.` does not match line terminators).
    private static int ShebangLength(string text)
    {
        if (text.Length < 2 || text[0] != '#' || text[1] != '!')
        {
            return -1;
        }
        var length = 2;
        while (length < text.Length && !IsLineBreak(text[length]))
        {
            length++;
        }
        return length;
    }

    /// <summary>scanner.ts: isShebangTrivia.</summary>
    private static bool IsShebangTrivia(string text, int pos)
    {
        // Shebangs check must only be done at the start of the file
        Debug.Assert(pos == 0);
        return ShebangLength(text) >= 0;
    }

    /// <summary>scanner.ts: scanShebangTrivia.</summary>
    private static int ScanShebangTrivia(string text, int pos)
    {
        var shebangLength = ShebangLength(text);
        pos += shebangLength;
        return pos;
    }

    /// <summary>
    /// scanner.ts: iterateCommentRanges.
    /// Invokes a callback for each comment range following the provided position.
    ///
    /// Single-line comment ranges include the leading double-slash characters but not the ending
    /// line break. Multi-line comment ranges include the leading slash-asterisk and trailing
    /// asterisk-slash characters.
    /// </summary>
    /// <param name="reduce">If true, accumulates the result of calling the callback in a fashion similar
    ///      to reduceLeft. If false, iteration stops when the callback returns a truthy value.</param>
    /// <param name="text">The source text to scan.</param>
    /// <param name="pos">The position at which to start scanning.</param>
    /// <param name="trailing">If false, whitespace is skipped until the first line break and comments
    ///      between that location and the next token are returned. If true, comments occurring
    ///      between the given position and the next line break are returned.</param>
    /// <param name="cb">The callback to execute as each comment range is encountered.</param>
    /// <param name="state">A state value to pass to each iteration of the callback.</param>
    /// <param name="initial">An initial value to pass when accumulating results (when "reduce" is true).</param>
    /// <returns>If "reduce" is true, the accumulated value. If "reduce" is false, the first truthy
    ///      return value of the callback.</returns>
    private static U? IterateCommentRanges<T, U>(bool reduce, string text, int pos, bool trailing, Func<int, int, SyntaxKind, bool, T, U?, U?> cb, T state, U? initial = default)
    {
        var pendingPos = 0;
        var pendingEnd = 0;
        var pendingKind = SyntaxKind.Unknown;
        var pendingHasTrailingNewLine = false;
        var hasPendingCommentRange = false;
        var collecting = trailing;
        var accumulator = initial;
        if (pos == 0)
        {
            collecting = true;
            var shebangLength = ShebangLength(text);
            if (shebangLength > 0)
            {
                pos = shebangLength;
            }
        }
        while (pos >= 0 && pos < text.Length)
        {
            var ch = text[pos];
            switch (ch)
            {
                case (char)Ch.CarriageReturn:
                    if (CharCodeAt(text, pos + 1) == Ch.LineFeed)
                    {
                        pos++;
                    }
                    goto case (char)Ch.LineFeed;
                case (char)Ch.LineFeed:
                    pos++;
                    if (trailing)
                    {
                        goto scanEnd;
                    }

                    collecting = true;
                    if (hasPendingCommentRange)
                    {
                        pendingHasTrailingNewLine = true;
                    }

                    continue;
                case (char)Ch.Tab:
                case (char)Ch.VerticalTab:
                case (char)Ch.FormFeed:
                case (char)Ch.Space:
                    pos++;
                    continue;
                case (char)Ch.Slash:
                    var nextChar = CharCodeAt(text, pos + 1);
                    var hasTrailingNewLine = false;
                    if (nextChar == Ch.Slash || nextChar == Ch.Asterisk)
                    {
                        var kind = nextChar == Ch.Slash ? SyntaxKind.SingleLineCommentTrivia : SyntaxKind.MultiLineCommentTrivia;
                        var startPos = pos;
                        pos += 2;
                        if (nextChar == Ch.Slash)
                        {
                            while (pos < text.Length)
                            {
                                if (IsLineBreak(text[pos]))
                                {
                                    hasTrailingNewLine = true;
                                    break;
                                }
                                pos++;
                            }
                        }
                        else
                        {
                            while (pos < text.Length)
                            {
                                if (text[pos] == Ch.Asterisk && CharCodeAt(text, pos + 1) == Ch.Slash)
                                {
                                    pos += 2;
                                    break;
                                }
                                pos++;
                            }
                        }

                        if (collecting)
                        {
                            if (hasPendingCommentRange)
                            {
                                accumulator = cb(pendingPos, pendingEnd, pendingKind, pendingHasTrailingNewLine, state, accumulator);
                                if (!reduce && IsTruthy(accumulator))
                                {
                                    // If we are not reducing and we have a truthy result, return it.
                                    return accumulator;
                                }
                            }

                            pendingPos = startPos;
                            pendingEnd = pos;
                            pendingKind = kind;
                            pendingHasTrailingNewLine = hasTrailingNewLine;
                            hasPendingCommentRange = true;
                        }

                        continue;
                    }
                    goto scanEnd;
                default:
                    if (ch > Ch.MaxAsciiCharacter && IsWhiteSpaceLike(ch))
                    {
                        if (hasPendingCommentRange && IsLineBreak(ch))
                        {
                            pendingHasTrailingNewLine = true;
                        }
                        pos++;
                        continue;
                    }
                    goto scanEnd;
            }
        }

    scanEnd:
        if (hasPendingCommentRange)
        {
            accumulator = cb(pendingPos, pendingEnd, pendingKind, pendingHasTrailingNewLine, state, accumulator);
        }

        return accumulator;
    }

    /// <summary>scanner.ts: reduceEachLeadingCommentRange.</summary>
    internal static U? ReduceEachLeadingCommentRange<T, U>(string text, int pos, Func<int, int, SyntaxKind, bool, T, U?, U?> cb, T state, U? initial)
    {
        return IterateCommentRanges(/*reduce*/ true, text, pos, /*trailing*/ false, cb, state, initial);
    }

    /// <summary>scanner.ts: reduceEachTrailingCommentRange.</summary>
    internal static U? ReduceEachTrailingCommentRange<T, U>(string text, int pos, Func<int, int, SyntaxKind, bool, T, U?, U?> cb, T state, U? initial)
    {
        return IterateCommentRanges(/*reduce*/ true, text, pos, /*trailing*/ true, cb, state, initial);
    }

    /// <summary>scanner.ts: appendCommentRange.</summary>
    private static List<CommentRange> AppendCommentRange(int pos, int end, SyntaxKind kind, bool hasTrailingNewLine, object? state, List<CommentRange>? comments)
    {
        comments ??= [];
        comments.Add(new CommentRange { Kind = kind, Pos = pos, End = end, HasTrailingNewLine = hasTrailingNewLine });
        return comments;
    }

    /// <summary>scanner.ts: getLeadingCommentRanges.</summary>
    internal static List<CommentRange>? GetLeadingCommentRanges(string text, int pos)
    {
        return ReduceEachLeadingCommentRange<object?, List<CommentRange>>(text, pos, AppendCommentRange, /*state*/ null, /*initial*/ null);
    }

    /// <summary>scanner.ts: getTrailingCommentRanges.</summary>
    internal static List<CommentRange>? GetTrailingCommentRanges(string text, int pos)
    {
        return ReduceEachTrailingCommentRange<object?, List<CommentRange>>(text, pos, AppendCommentRange, /*state*/ null, /*initial*/ null);
    }

    /// <summary>scanner.ts: getShebang. Optionally, get the shebang.</summary>
    internal static string? GetShebang(string text)
    {
        var length = ShebangLength(text);
        if (length >= 0)
        {
            return text.Substring(0, length);
        }
        return null;
    }

    /// <summary>scanner.ts: isIdentifierStart.</summary>
    internal static bool IsIdentifierStart(int ch, ScriptTarget? languageVersion)
    {
        return IsASCIILetter(ch) || ch == Ch.Dollar || ch == Ch.Underscore ||
            (ch > Ch.MaxAsciiCharacter && IsUnicodeIdentifierStart(ch, languageVersion));
    }

    /// <summary>scanner.ts: isIdentifierPart.</summary>
    internal static bool IsIdentifierPart(int ch, ScriptTarget? languageVersion, LanguageVariant identifierVariant = LanguageVariant.Standard)
    {
        return IsWordCharacter(ch) || ch == Ch.Dollar ||
            // "-" and ":" are valid in JSX Identifiers
            (identifierVariant == LanguageVariant.JSX && (ch == Ch.Minus || ch == Ch.Colon)) ||
            (ch > Ch.MaxAsciiCharacter && IsUnicodeIdentifierPart(ch, languageVersion));
    }

    /// <summary>scanner.ts: isIdentifierText.</summary>
    internal static bool IsIdentifierText(string name, ScriptTarget languageVersion, LanguageVariant identifierVariant = LanguageVariant.Standard)
    {
        var ch = CodePointAt(name, 0);
        if (!IsIdentifierStart(ch, languageVersion))
        {
            return false;
        }

        for (var i = CharSize(ch); i < name.Length; i += CharSize(ch))
        {
            if (!IsIdentifierPart(ch = CodePointAt(name, i), languageVersion, identifierVariant))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>scanner.ts: codePointAt (`s.codePointAt(i)`; <see cref="Nan"/> for undefined).</summary>
    private static int CodePointAt(string s, int i)
    {
        if ((uint)i >= (uint)s.Length)
        {
            return Nan;
        }
        int first = s[i];
        if (first is >= 0xD800 and <= 0xDBFF && i + 1 < s.Length)
        {
            int second = s[i + 1];
            if (second is >= 0xDC00 and <= 0xDFFF)
            {
                return ((first - 0xD800) * 0x400) + (second - 0xDC00) + 0x10000;
            }
        }
        return first;
    }

    /// <summary>scanner.ts: charSize.</summary>
    private static int CharSize(int ch)
    {
        if (ch >= 0x10000)
        {
            return 2;
        }
        if (ch == Ch.EOF)
        {
            return 0;
        }
        return 1;
    }

    /// <summary>scanner.ts: utf16EncodeAsStringFallback (the worker `String.fromCodePoint` accepts the same range).</summary>
    private static string Utf16EncodeAsStringFallback(int codePoint)
    {
        Debug.Assert(0x0 <= codePoint && codePoint <= 0x10FFFF);

        if (codePoint <= 65535)
        {
            return ((char)codePoint).ToString();
        }

        var codeUnit1 = (char)(((codePoint - 65536) / 1024) + 0xD800);
        var codeUnit2 = (char)(((codePoint - 65536) % 1024) + 0xDC00);

        return string.Concat(codeUnit1.ToString(), codeUnit2.ToString());
    }

    /// <summary>scanner.ts: utf16EncodeAsStringWorker.</summary>
    private static string Utf16EncodeAsStringWorker(int codePoint)
    {
        return Utf16EncodeAsStringFallback(codePoint);
    }

    /// <summary>scanner.ts: utf16EncodeAsString.</summary>
    internal static string Utf16EncodeAsString(int codePoint)
    {
        return Utf16EncodeAsStringWorker(codePoint);
    }
}
