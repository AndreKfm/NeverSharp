// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/scanner.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/scanner/scanner.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>scanner.ts: ErrorCallback.</summary>
internal delegate void ErrorCallback(DiagnosticMessage message, int length, object? arg0);

/// <summary>
/// scanner.ts. Instances are the objects `createScanner` returns (TypeScript's `interface Scanner`); the
/// file-level functions and constants of scanner.ts are the static members.
/// </summary>
internal sealed partial class Scanner
{
    /// <summary>
    /// The value of `text.charCodeAt(i)` and `text.codePointAt(i)` outside the string (NaN, respectively
    /// undefined, in JavaScript). It equals no character code and no CharacterCodes member (in particular not
    /// CharacterCodes.EOF), and every range test on character codes rejects it, as they reject NaN.
    /// </summary>
    private const int NaN = int.MinValue;

    internal static bool TokenIsIdentifierOrKeyword(SyntaxKind token)
    {
        return token >= SyntaxKind.Identifier;
    }

    internal static bool TokenIsIdentifierOrKeywordOrGreaterThan(SyntaxKind token)
    {
        return token == SyntaxKind.GreaterThanToken || TokenIsIdentifierOrKeyword(token);
    }

    // scanner.ts: `textToKeywordObj: MapLike<KeywordSyntaxKind>`; an object literal keeps insertion order.
    internal static readonly OrderedDictionary<string, SyntaxKind> TextToKeywordObj = new(StringComparer.Ordinal)
    {
        ["abstract"] = SyntaxKind.AbstractKeyword,
        ["accessor"] = SyntaxKind.AccessorKeyword,
        ["any"] = SyntaxKind.AnyKeyword,
        ["as"] = SyntaxKind.AsKeyword,
        ["asserts"] = SyntaxKind.AssertsKeyword,
        ["assert"] = SyntaxKind.AssertKeyword,
        ["bigint"] = SyntaxKind.BigIntKeyword,
        ["boolean"] = SyntaxKind.BooleanKeyword,
        ["break"] = SyntaxKind.BreakKeyword,
        ["case"] = SyntaxKind.CaseKeyword,
        ["catch"] = SyntaxKind.CatchKeyword,
        ["class"] = SyntaxKind.ClassKeyword,
        ["continue"] = SyntaxKind.ContinueKeyword,
        ["const"] = SyntaxKind.ConstKeyword,
        ["" + "constructor"] = SyntaxKind.ConstructorKeyword,
        ["debugger"] = SyntaxKind.DebuggerKeyword,
        ["declare"] = SyntaxKind.DeclareKeyword,
        ["default"] = SyntaxKind.DefaultKeyword,
        ["defer"] = SyntaxKind.DeferKeyword,
        ["delete"] = SyntaxKind.DeleteKeyword,
        ["do"] = SyntaxKind.DoKeyword,
        ["else"] = SyntaxKind.ElseKeyword,
        ["enum"] = SyntaxKind.EnumKeyword,
        ["export"] = SyntaxKind.ExportKeyword,
        ["extends"] = SyntaxKind.ExtendsKeyword,
        ["false"] = SyntaxKind.FalseKeyword,
        ["finally"] = SyntaxKind.FinallyKeyword,
        ["for"] = SyntaxKind.ForKeyword,
        ["from"] = SyntaxKind.FromKeyword,
        ["function"] = SyntaxKind.FunctionKeyword,
        ["get"] = SyntaxKind.GetKeyword,
        ["if"] = SyntaxKind.IfKeyword,
        ["implements"] = SyntaxKind.ImplementsKeyword,
        ["import"] = SyntaxKind.ImportKeyword,
        ["in"] = SyntaxKind.InKeyword,
        ["infer"] = SyntaxKind.InferKeyword,
        ["instanceof"] = SyntaxKind.InstanceOfKeyword,
        ["interface"] = SyntaxKind.InterfaceKeyword,
        ["intrinsic"] = SyntaxKind.IntrinsicKeyword,
        ["is"] = SyntaxKind.IsKeyword,
        ["keyof"] = SyntaxKind.KeyOfKeyword,
        ["let"] = SyntaxKind.LetKeyword,
        ["module"] = SyntaxKind.ModuleKeyword,
        ["namespace"] = SyntaxKind.NamespaceKeyword,
        ["never"] = SyntaxKind.NeverKeyword,
        ["new"] = SyntaxKind.NewKeyword,
        ["null"] = SyntaxKind.NullKeyword,
        ["number"] = SyntaxKind.NumberKeyword,
        ["object"] = SyntaxKind.ObjectKeyword,
        ["package"] = SyntaxKind.PackageKeyword,
        ["private"] = SyntaxKind.PrivateKeyword,
        ["protected"] = SyntaxKind.ProtectedKeyword,
        ["public"] = SyntaxKind.PublicKeyword,
        ["override"] = SyntaxKind.OverrideKeyword,
        ["out"] = SyntaxKind.OutKeyword,
        ["readonly"] = SyntaxKind.ReadonlyKeyword,
        ["require"] = SyntaxKind.RequireKeyword,
        ["global"] = SyntaxKind.GlobalKeyword,
        ["return"] = SyntaxKind.ReturnKeyword,
        ["satisfies"] = SyntaxKind.SatisfiesKeyword,
        ["set"] = SyntaxKind.SetKeyword,
        ["static"] = SyntaxKind.StaticKeyword,
        ["string"] = SyntaxKind.StringKeyword,
        ["super"] = SyntaxKind.SuperKeyword,
        ["switch"] = SyntaxKind.SwitchKeyword,
        ["symbol"] = SyntaxKind.SymbolKeyword,
        ["this"] = SyntaxKind.ThisKeyword,
        ["throw"] = SyntaxKind.ThrowKeyword,
        ["true"] = SyntaxKind.TrueKeyword,
        ["try"] = SyntaxKind.TryKeyword,
        ["type"] = SyntaxKind.TypeKeyword,
        ["typeof"] = SyntaxKind.TypeOfKeyword,
        ["undefined"] = SyntaxKind.UndefinedKeyword,
        ["unique"] = SyntaxKind.UniqueKeyword,
        ["unknown"] = SyntaxKind.UnknownKeyword,
        ["using"] = SyntaxKind.UsingKeyword,
        ["var"] = SyntaxKind.VarKeyword,
        ["void"] = SyntaxKind.VoidKeyword,
        ["while"] = SyntaxKind.WhileKeyword,
        ["with"] = SyntaxKind.WithKeyword,
        ["yield"] = SyntaxKind.YieldKeyword,
        ["async"] = SyntaxKind.AsyncKeyword,
        ["await"] = SyntaxKind.AwaitKeyword,
        ["of"] = SyntaxKind.OfKeyword,
    };

    // scanner.ts: `new Map(Object.entries(textToKeywordObj))`.
    private static readonly Dictionary<string, SyntaxKind> TextToKeyword = new(TextToKeywordObj, StringComparer.Ordinal);

    // textToKeyword looked up by a range of the text (see ScanIdentifier).
    private static readonly Dictionary<string, SyntaxKind>.AlternateLookup<ReadOnlySpan<char>> TextToKeywordByRange = TextToKeyword.GetAlternateLookup<ReadOnlySpan<char>>();

    // scanner.ts: `new Map(Object.entries({ ...textToKeywordObj, "{": ..., ... }))`.
    private static readonly OrderedDictionary<string, SyntaxKind> TextToToken = CreateTextToToken();

    private static OrderedDictionary<string, SyntaxKind> CreateTextToToken()
    {
        var result = new OrderedDictionary<string, SyntaxKind>(TextToKeywordObj, StringComparer.Ordinal)
        {
            ["{"] = SyntaxKind.OpenBraceToken,
            ["}"] = SyntaxKind.CloseBraceToken,
            ["("] = SyntaxKind.OpenParenToken,
            [")"] = SyntaxKind.CloseParenToken,
            ["["] = SyntaxKind.OpenBracketToken,
            ["]"] = SyntaxKind.CloseBracketToken,
            ["."] = SyntaxKind.DotToken,
            ["..."] = SyntaxKind.DotDotDotToken,
            [";"] = SyntaxKind.SemicolonToken,
            [","] = SyntaxKind.CommaToken,
            ["<"] = SyntaxKind.LessThanToken,
            [">"] = SyntaxKind.GreaterThanToken,
            ["<="] = SyntaxKind.LessThanEqualsToken,
            [">="] = SyntaxKind.GreaterThanEqualsToken,
            ["=="] = SyntaxKind.EqualsEqualsToken,
            ["!="] = SyntaxKind.ExclamationEqualsToken,
            ["==="] = SyntaxKind.EqualsEqualsEqualsToken,
            ["!=="] = SyntaxKind.ExclamationEqualsEqualsToken,
            ["=>"] = SyntaxKind.EqualsGreaterThanToken,
            ["+"] = SyntaxKind.PlusToken,
            ["-"] = SyntaxKind.MinusToken,
            ["**"] = SyntaxKind.AsteriskAsteriskToken,
            ["*"] = SyntaxKind.AsteriskToken,
            ["/"] = SyntaxKind.SlashToken,
            ["%"] = SyntaxKind.PercentToken,
            ["++"] = SyntaxKind.PlusPlusToken,
            ["--"] = SyntaxKind.MinusMinusToken,
            ["<<"] = SyntaxKind.LessThanLessThanToken,
            ["</"] = SyntaxKind.LessThanSlashToken,
            [">>"] = SyntaxKind.GreaterThanGreaterThanToken,
            [">>>"] = SyntaxKind.GreaterThanGreaterThanGreaterThanToken,
            ["&"] = SyntaxKind.AmpersandToken,
            ["|"] = SyntaxKind.BarToken,
            ["^"] = SyntaxKind.CaretToken,
            ["!"] = SyntaxKind.ExclamationToken,
            ["~"] = SyntaxKind.TildeToken,
            ["&&"] = SyntaxKind.AmpersandAmpersandToken,
            ["||"] = SyntaxKind.BarBarToken,
            ["?"] = SyntaxKind.QuestionToken,
            ["??"] = SyntaxKind.QuestionQuestionToken,
            ["?."] = SyntaxKind.QuestionDotToken,
            [":"] = SyntaxKind.ColonToken,
            ["="] = SyntaxKind.EqualsToken,
            ["+="] = SyntaxKind.PlusEqualsToken,
            ["-="] = SyntaxKind.MinusEqualsToken,
            ["*="] = SyntaxKind.AsteriskEqualsToken,
            ["**="] = SyntaxKind.AsteriskAsteriskEqualsToken,
            ["/="] = SyntaxKind.SlashEqualsToken,
            ["%="] = SyntaxKind.PercentEqualsToken,
            ["<<="] = SyntaxKind.LessThanLessThanEqualsToken,
            [">>="] = SyntaxKind.GreaterThanGreaterThanEqualsToken,
            [">>>="] = SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken,
            ["&="] = SyntaxKind.AmpersandEqualsToken,
            ["|="] = SyntaxKind.BarEqualsToken,
            ["^="] = SyntaxKind.CaretEqualsToken,
            ["||="] = SyntaxKind.BarBarEqualsToken,
            ["&&="] = SyntaxKind.AmpersandAmpersandEqualsToken,
            ["??="] = SyntaxKind.QuestionQuestionEqualsToken,
            ["@"] = SyntaxKind.AtToken,
            ["#"] = SyntaxKind.HashToken,
            ["`"] = SyntaxKind.BacktickToken,
        };
        return result;
    }

    private static readonly OrderedDictionary<CharacterCodes, RegularExpressionFlags> CharCodeToRegExpFlag = new()
    {
        [CharacterCodes.d] = RegularExpressionFlags.HasIndices,
        [CharacterCodes.g] = RegularExpressionFlags.Global,
        [CharacterCodes.i] = RegularExpressionFlags.IgnoreCase,
        [CharacterCodes.m] = RegularExpressionFlags.Multiline,
        [CharacterCodes.s] = RegularExpressionFlags.DotAll,
        [CharacterCodes.u] = RegularExpressionFlags.Unicode,
        [CharacterCodes.v] = RegularExpressionFlags.UnicodeSets,
        [CharacterCodes.y] = RegularExpressionFlags.Sticky,
    };

    private static readonly Dictionary<RegularExpressionFlags, ScriptTarget> RegExpFlagToFirstAvailableLanguageVersion = new()
    {
        [RegularExpressionFlags.HasIndices] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsHasIndices,
        [RegularExpressionFlags.DotAll] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsDotAll,
        [RegularExpressionFlags.Unicode] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsUnicode,
        [RegularExpressionFlags.UnicodeSets] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsUnicodeSets,
        [RegularExpressionFlags.Sticky] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsSticky,
    };

    //// The Unicode identifier tables (unicodeES5IdentifierStart, ...) are in Scanner.UnicodeTables.cs.

    /// <summary>
    /// Test for whether a single line comment with leading whitespace trimmed's text contains a directive.
    /// scanner.ts: <c>const commentDirectiveRegExSingleLine = /^\/\/\/?\s*@(ts-expect-error|ts-ignore)/;</c>
    /// Regular expressions are banned in Track F: this is a hand-written matcher with the regex's semantics
    /// over `text[start..end)`, returning capture group 1 (undefined when the regex does not match).
    /// </summary>
    private static string? CommentDirectiveRegExSingleLine(string text, int start, int end)
    {
        var p = start;
        // ^\/\/
        if (!(p + 1 < end && text[p] == '/' && text[p + 1] == '/'))
        {
            return null;
        }
        p += 2;
        // \/? (greedy; giving the slash back cannot help, since \s* and @ do not match '/')
        if (p < end && text[p] == '/')
        {
            p++;
        }
        return MatchDirectiveAfterSlashes(text, p, end);
    }

    /// <summary>
    /// Test for whether a multi-line comment with leading whitespace trimmed's last line contains a directive.
    /// scanner.ts: <c>const commentDirectiveRegExMultiLine = /^(?:\/|\*)*\s*@(ts-expect-error|ts-ignore)/;</c>
    /// (hand-written matcher, see CommentDirectiveRegExSingleLine).
    /// </summary>
    private static string? CommentDirectiveRegExMultiLine(string text, int start, int end)
    {
        var p = start;
        // ^(?:\/|\*)* (greedy; backtracking into it cannot help, since \s* and @ match neither '/' nor '*')
        while (p < end && (text[p] == '/' || text[p] == '*'))
        {
            p++;
        }
        return MatchDirectiveAfterSlashes(text, p, end);
    }

    // \s*@(ts-expect-error|ts-ignore): the tail the two directive regexes share.
    private static string? MatchDirectiveAfterSlashes(string text, int p, int end)
    {
        while (p < end && IsRegExpWhiteSpace(text[p]))
        {
            p++;
        }
        if (!(p < end && text[p] == '@'))
        {
            return null;
        }
        p++;
        if (string.CompareOrdinal(text, p, "ts-expect-error", 0, 15) == 0 && p + 15 <= end)
        {
            return "ts-expect-error";
        }
        if (string.CompareOrdinal(text, p, "ts-ignore", 0, 9) == 0 && p + 9 <= end)
        {
            return "ts-ignore";
        }
        return null;
    }

    /// <summary>
    /// scanner.ts: <c>const jsDocSeeOrLink = /@(?:see|link)/i;</c> as `jsDocSeeOrLink.test(text[start..end))`
    /// (hand-written matcher, see CommentDirectiveRegExSingleLine). Without the `u` flag, `i` canonicalizes by
    /// toUpperCase but never maps a non-ASCII character to an ASCII one, so only ASCII letters fold.
    /// </summary>
    private static bool JsDocSeeOrLinkTest(string text, int start, int end)
    {
        for (var p = start; p < end; p++)
        {
            if (text[p] != '@')
            {
                continue;
            }
            if (MatchesAsciiIgnoreCase(text, p + 1, end, "see") || MatchesAsciiIgnoreCase(text, p + 1, end, "link"))
            {
                return true;
            }
        }
        return false;
    }

    private static bool MatchesAsciiIgnoreCase(string text, int p, int end, string lowerWord)
    {
        if (p + lowerWord.Length > end)
        {
            return false;
        }
        for (var i = 0; i < lowerWord.Length; i++)
        {
            int ch = text[p + i];
            if (ch >= 'A' && ch <= 'Z')
            {
                ch += 'a' - 'A';
            }
            if (ch != lowerWord[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The characters JavaScript's `\s` (and String.prototype.trimStart) matches: WhiteSpace and LineTerminator
    /// (ECMA-262 12.2, 12.3), with the Space_Separator (Zs) characters of Unicode 16.
    /// </summary>
    private static bool IsRegExpWhiteSpace(int ch)
    {
        switch (ch)
        {
            case 0x0009:
            case 0x000A:
            case 0x000B:
            case 0x000C:
            case 0x000D:
            case 0x0020:
            case 0x00A0:
            case 0x1680:
            case 0x2028:
            case 0x2029:
            case 0x202F:
            case 0x205F:
            case 0x3000:
            case 0xFEFF:
                return true;
            default:
                return ch >= 0x2000 && ch <= 0x200A;
        }
    }

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
            // TypeScript: `mid = lo + (hi - lo) / 2; mid -= mid % 2;` in floating point; for the even map lengths
            // and the even lo of this search, (hi - lo) / 2 is an integer or ends in .5, which `mid % 2` removes
            // together with the odd part, as integer division and `mid -= mid % 2` do.
            mid = lo + (hi - lo) / 2;
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

    internal static bool IsUnicodeIdentifierStart(int code, ScriptTarget? languageVersion)
    {
        return languageVersion >= ScriptTarget.ES2015 ?
            LookupInUnicodeMap(code, UnicodeESNextIdentifierStart) :
            LookupInUnicodeMap(code, UnicodeES5IdentifierStart);
    }

    private static bool IsUnicodeIdentifierPart(int code, ScriptTarget? languageVersion)
    {
        return languageVersion >= ScriptTarget.ES2015 ?
            LookupInUnicodeMap(code, UnicodeESNextIdentifierPart) :
            LookupInUnicodeMap(code, UnicodeES5IdentifierPart);
    }

    // scanner.ts: makeReverseMap. The result is a sparse JavaScript array indexed by the map's values.
    private static TKey?[] MakeReverseMap<TKey>(IEnumerable<KeyValuePair<TKey, int>> source)
    {
        var result = new List<TKey?>();
        foreach (var (name, value) in source)
        {
            while (result.Count <= value)
            {
                result.Add(default);
            }
            result[value] = name;
        }
        return [.. result];
    }

    private static readonly string?[] TokenStrings = MakeReverseMap(ToIntValues(TextToToken));

    private static IEnumerable<KeyValuePair<string, int>> ToIntValues(OrderedDictionary<string, SyntaxKind> map)
    {
        foreach (var (key, value) in map)
        {
            yield return new KeyValuePair<string, int>(key, (int)value);
        }
    }

    internal static string? TokenToString(SyntaxKind t)
    {
        return (uint)t < (uint)TokenStrings.Length ? TokenStrings[(int)t] : null;
    }

    internal static SyntaxKind? StringToToken(string s)
    {
        return TextToToken.TryGetValue(s, out var kind) ? kind : null;
    }

    private static readonly CharacterCodes?[] RegExpFlagCharCodes = CreateRegExpFlagCharCodes();

    private static CharacterCodes?[] CreateRegExpFlagCharCodes()
    {
        var source = new List<KeyValuePair<CharacterCodes, int>>();
        foreach (var (ch, flag) in CharCodeToRegExpFlag)
        {
            source.Add(new KeyValuePair<CharacterCodes, int>(ch, (int)flag));
        }
        var reversed = MakeReverseMapOfValues(source);
        return reversed;
    }

    // makeReverseMap for a map whose keys are value types (the absent entries stay undefined).
    private static TKey?[] MakeReverseMapOfValues<TKey>(List<KeyValuePair<TKey, int>> source)
        where TKey : struct
    {
        var result = new List<TKey?>();
        foreach (var (name, value) in source)
        {
            while (result.Count <= value)
            {
                result.Add(null);
            }
            result[value] = name;
        }
        return [.. result];
    }

    internal static CharacterCodes? RegularExpressionFlagToCharacterCode(RegularExpressionFlags f)
    {
        return (uint)f < (uint)RegExpFlagCharCodes.Length ? RegExpFlagCharCodes[(int)f] : null;
    }

    internal static RegularExpressionFlags? CharacterCodeToRegularExpressionFlag(int ch)
    {
        return CharCodeToRegExpFlag.TryGetValue((CharacterCodes)ch, out var flag) ? flag : null;
    }

    internal static List<int> ComputeLineStarts(string text)
    {
        var result = new List<int>();
        var pos = 0;
        var lineStart = 0;
        while (pos < text.Length)
        {
            int ch = text[pos];
            pos++;
            switch (ch)
            {
                case (int)CharacterCodes.carriageReturn:
                    if (CharCodeAt(text, pos) == (int)CharacterCodes.lineFeed)
                    {
                        pos++;
                    }
                    goto case (int)CharacterCodes.lineFeed;
                case (int)CharacterCodes.lineFeed:
                    result.Add(lineStart);
                    lineStart = pos;
                    break;
                default:
                    if (ch > (int)CharacterCodes.maxAsciiCharacter && IsLineBreak(ch))
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

    /// <summary>
    /// scanner.ts: getLineStarts(sourceFile). The SourceFileLike interface (text, lineMap) arrives with the
    /// parser's SourceFile; until then the function takes the two members it uses.
    /// </summary>
    internal static IReadOnlyList<int> GetLineStarts(ISourceFileLike sourceFile)
    {
        return sourceFile.LineMap ??= ComputeLineStarts(sourceFile.Text);
    }

    internal static LineAndCharacter ComputeLineAndCharacterOfPosition(IReadOnlyList<int> lineStarts, int position)
    {
        var lineNumber = ComputeLineOfPosition(lineStarts, position);
        return new LineAndCharacter
        {
            Line = lineNumber,
            Character = position - lineStarts[lineNumber],
        };
    }

    /// <summary>We assume the first line starts at position 0 and 'position' is non-negative.</summary>
    internal static int ComputeLineOfPosition(IReadOnlyList<int> lineStarts, int position, int? lowerBound = null)
    {
        var lineNumber = Core.BinarySearch(lineStarts, position, Core.Identity, (a, b) => Core.CompareValues(a, b), lowerBound);
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

    internal static LineAndCharacter GetLineAndCharacterOfPosition(ISourceFileLike sourceFile, int position)
    {
        return ComputeLineAndCharacterOfPosition(GetLineStarts(sourceFile), position);
    }

    internal static bool IsWhiteSpaceLike(int ch)
    {
        return IsWhiteSpaceSingleLine(ch) || IsLineBreak(ch);
    }

    /// <summary>Does not include line breaks. For that, see isWhiteSpaceLike.</summary>
    internal static bool IsWhiteSpaceSingleLine(int ch)
    {
        // Note: nextLine is in the Zs space, and should be considered to be a whitespace.
        // It is explicitly not a line-break as it isn't in the exact set specified by EcmaScript.
        return ch == (int)CharacterCodes.space ||
            ch == (int)CharacterCodes.tab ||
            ch == (int)CharacterCodes.verticalTab ||
            ch == (int)CharacterCodes.formFeed ||
            ch == (int)CharacterCodes.nonBreakingSpace ||
            ch == (int)CharacterCodes.nextLine ||
            ch == (int)CharacterCodes.ogham ||
            (ch >= (int)CharacterCodes.enQuad && ch <= (int)CharacterCodes.zeroWidthSpace) ||
            ch == (int)CharacterCodes.narrowNoBreakSpace ||
            ch == (int)CharacterCodes.mathematicalSpace ||
            ch == (int)CharacterCodes.ideographicSpace ||
            ch == (int)CharacterCodes.byteOrderMark;
    }

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

        return ch == (int)CharacterCodes.lineFeed ||
            ch == (int)CharacterCodes.carriageReturn ||
            ch == (int)CharacterCodes.lineSeparator ||
            ch == (int)CharacterCodes.paragraphSeparator;
    }

    private static bool IsDigit(int ch)
    {
        return ch >= (int)CharacterCodes._0 && ch <= (int)CharacterCodes._9;
    }

    private static bool IsHexDigit(int ch)
    {
        return IsDigit(ch) || (ch >= (int)CharacterCodes.A && ch <= (int)CharacterCodes.F) || (ch >= (int)CharacterCodes.a && ch <= (int)CharacterCodes.f);
    }

    private static bool IsASCIILetter(int ch)
    {
        return (ch >= (int)CharacterCodes.A && ch <= (int)CharacterCodes.Z) || (ch >= (int)CharacterCodes.a && ch <= (int)CharacterCodes.z);
    }

    // Section 6.1.4
    private static bool IsWordCharacter(int ch)
    {
        return IsASCIILetter(ch) || IsDigit(ch) || ch == (int)CharacterCodes._;
    }

    private static bool IsOctalDigit(int ch)
    {
        return ch >= (int)CharacterCodes._0 && ch <= (int)CharacterCodes._7;
    }

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
                case (int)CharacterCodes.carriageReturn:
                    if (CharCodeAt(text, pos + 1) == (int)CharacterCodes.lineFeed)
                    {
                        pos++;
                    }
                    goto case (int)CharacterCodes.lineFeed;
                case (int)CharacterCodes.lineFeed:
                    pos++;
                    if (stopAfterLineBreak)
                    {
                        return pos;
                    }
                    canConsumeStar = inJSDoc;
                    continue;
                case (int)CharacterCodes.tab:
                case (int)CharacterCodes.verticalTab:
                case (int)CharacterCodes.formFeed:
                case (int)CharacterCodes.space:
                    pos++;
                    continue;
                case (int)CharacterCodes.slash:
                    if (stopAtComments)
                    {
                        break;
                    }
                    if (CharCodeAt(text, pos + 1) == (int)CharacterCodes.slash)
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
                    if (CharCodeAt(text, pos + 1) == (int)CharacterCodes.asterisk)
                    {
                        pos += 2;
                        while (pos < text.Length)
                        {
                            if (text[pos] == (int)CharacterCodes.asterisk && CharCodeAt(text, pos + 1) == (int)CharacterCodes.slash)
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

                case (int)CharacterCodes.lessThan:
                case (int)CharacterCodes.bar:
                case (int)CharacterCodes.equals:
                case (int)CharacterCodes.greaterThan:
                    if (IsConflictMarkerTrivia(text, pos))
                    {
                        pos = ScanConflictMarkerTrivia(text, pos);
                        canConsumeStar = false;
                        continue;
                    }
                    break;

                case (int)CharacterCodes.hash:
                    if (pos == 0 && IsShebangTrivia(text, pos))
                    {
                        pos = ScanShebangTrivia(text, pos);
                        canConsumeStar = false;
                        continue;
                    }
                    break;

                case (int)CharacterCodes.asterisk:
                    if (canConsumeStar)
                    {
                        pos++;
                        canConsumeStar = false;
                        continue;
                    }
                    break;

                default:
                    if (ch > (int)CharacterCodes.maxAsciiCharacter && IsWhiteSpaceLike(ch))
                    {
                        pos++;
                        continue;
                    }
                    break;
            }
            return pos;
        }
    }

    // All conflict markers consist of the same character repeated seven times.  If it is
    // a <<<<<<< or >>>>>>> marker then it is also followed by a space.
    private static readonly int MergeConflictMarkerLength = "<<<<<<<".Length;

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

                return ch == (int)CharacterCodes.equals ||
                    CharCodeAt(text, pos + MergeConflictMarkerLength) == (int)CharacterCodes.space;
            }
        }

        return false;
    }

    private static int ScanConflictMarkerTrivia(string text, int pos, Action<DiagnosticMessage, int, int>? error = null)
    {
        error?.Invoke(Diagnostics.Merge_conflict_marker_encountered, pos, MergeConflictMarkerLength);

        var ch = CharCodeAt(text, pos);
        var len = text.Length;

        if (ch == (int)CharacterCodes.lessThan || ch == (int)CharacterCodes.greaterThan)
        {
            while (pos < len && !IsLineBreak(text[pos]))
            {
                pos++;
            }
        }
        else
        {
            Debug.Assert(ch == (int)CharacterCodes.bar || ch == (int)CharacterCodes.equals);
            // Consume everything from the start of a ||||||| or ======= marker to the start
            // of the next ======= or >>>>>>> marker.
            while (pos < len)
            {
                int currentChar = text[pos];
                if ((currentChar == (int)CharacterCodes.equals || currentChar == (int)CharacterCodes.greaterThan) && currentChar != ch && IsConflictMarkerTrivia(text, pos))
                {
                    break;
                }

                pos++;
            }
        }

        return pos;
    }

    /// <summary>
    /// scanner.ts: <c>const shebangTriviaRegex = /^#!.*/;</c> as `shebangTriviaRegex.exec(text)`. Regular
    /// expressions are banned in Track F: this returns the length of the match ("#!" and every following
    /// character up to a line terminator, which `.` does not match), or -1 when it does not match.
    /// </summary>
    private static int ShebangTriviaRegexExec(string text)
    {
        if (!(text.Length >= 2 && text[0] == '#' && text[1] == '!'))
        {
            return -1;
        }
        var end = 2;
        while (end < text.Length && !IsLineBreak(text[end]))
        {
            end++;
        }
        return end;
    }

    private static bool IsShebangTrivia(string text, int pos)
    {
        // Shebangs check must only be done at the start of the file
        Debug.Assert(pos == 0);
        return ShebangTriviaRegexExec(text) >= 0;
    }

    private static int ScanShebangTrivia(string text, int pos)
    {
        var shebangLength = ShebangTriviaRegexExec(text);
        pos = pos + shebangLength;
        return pos;
    }

    /// <summary>The callback of iterateCommentRanges: (pos, end, kind, hasTrailingNewLine, state, memo) => U.</summary>
    internal delegate TResult CommentRangeCallback<in TState, TResult>(int pos, int end, SyntaxKind kind, bool hasTrailingNewLine, TState state, TResult? memo);

    /// <summary>
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
    private static TResult? IterateCommentRanges<TState, TResult>(bool reduce, string text, int pos, bool trailing, CommentRangeCallback<TState, TResult> cb, TState state, TResult? initial = default)
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
            var shebang = GetShebang(text);
            if (!string.IsNullOrEmpty(shebang))
            {
                pos = shebang.Length;
            }
        }
        while (pos >= 0 && pos < text.Length)
        {
            int ch = text[pos];
            switch (ch)
            {
                case (int)CharacterCodes.carriageReturn:
                    if (CharCodeAt(text, pos + 1) == (int)CharacterCodes.lineFeed)
                    {
                        pos++;
                    }
                    goto case (int)CharacterCodes.lineFeed;
                case (int)CharacterCodes.lineFeed:
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
                case (int)CharacterCodes.tab:
                case (int)CharacterCodes.verticalTab:
                case (int)CharacterCodes.formFeed:
                case (int)CharacterCodes.space:
                    pos++;
                    continue;
                case (int)CharacterCodes.slash:
                    var nextChar = CharCodeAt(text, pos + 1);
                    var hasTrailingNewLine = false;
                    if (nextChar == (int)CharacterCodes.slash || nextChar == (int)CharacterCodes.asterisk)
                    {
                        var kind = nextChar == (int)CharacterCodes.slash ? SyntaxKind.SingleLineCommentTrivia : SyntaxKind.MultiLineCommentTrivia;
                        var startPos = pos;
                        pos += 2;
                        if (nextChar == (int)CharacterCodes.slash)
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
                                if (text[pos] == (int)CharacterCodes.asterisk && CharCodeAt(text, pos + 1) == (int)CharacterCodes.slash)
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
                    if (ch > (int)CharacterCodes.maxAsciiCharacter && IsWhiteSpaceLike(ch))
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

    internal static TResult? ForEachLeadingCommentRange<TState, TResult>(string text, int pos, CommentRangeCallback<TState, TResult> cb, TState state)
    {
        return IterateCommentRanges(/*reduce*/ false, text, pos, /*trailing*/ false, cb, state);
    }

    internal static TResult? ForEachTrailingCommentRange<TState, TResult>(string text, int pos, CommentRangeCallback<TState, TResult> cb, TState state)
    {
        return IterateCommentRanges(/*reduce*/ false, text, pos, /*trailing*/ true, cb, state);
    }

    internal static TResult? ReduceEachLeadingCommentRange<TState, TResult>(string text, int pos, CommentRangeCallback<TState, TResult> cb, TState state, TResult? initial)
    {
        return IterateCommentRanges(/*reduce*/ true, text, pos, /*trailing*/ false, cb, state, initial);
    }

    internal static TResult? ReduceEachTrailingCommentRange<TState, TResult>(string text, int pos, CommentRangeCallback<TState, TResult> cb, TState state, TResult? initial)
    {
        return IterateCommentRanges(/*reduce*/ true, text, pos, /*trailing*/ true, cb, state, initial);
    }

    private static List<CommentRange> AppendCommentRange(int pos, int end, SyntaxKind kind, bool hasTrailingNewLine, object? state, List<CommentRange>? comments)
    {
        comments ??= [];
        comments.Add(new CommentRange { Kind = kind, Pos = pos, End = end, HasTrailingNewLine = hasTrailingNewLine });
        return comments;
    }

    internal static List<CommentRange>? GetLeadingCommentRanges(string text, int pos)
    {
        return ReduceEachLeadingCommentRange<object?, List<CommentRange>>(text, pos, AppendCommentRange, /*state*/ null, /*initial*/ null);
    }

    internal static List<CommentRange>? GetTrailingCommentRanges(string text, int pos)
    {
        return ReduceEachTrailingCommentRange<object?, List<CommentRange>>(text, pos, AppendCommentRange, /*state*/ null, /*initial*/ null);
    }

    /// <summary>Optionally, get the shebang</summary>
    internal static string? GetShebang(string text)
    {
        var match = ShebangTriviaRegexExec(text);
        if (match >= 0)
        {
            return text.Substring(0, match);
        }
        return null;
    }

    internal static bool IsIdentifierStart(int ch, ScriptTarget? languageVersion)
    {
        return IsASCIILetter(ch) || ch == (int)CharacterCodes.Dollar || ch == (int)CharacterCodes._ ||
            (ch > (int)CharacterCodes.maxAsciiCharacter && IsUnicodeIdentifierStart(ch, languageVersion));
    }

    internal static bool IsIdentifierPart(int ch, ScriptTarget? languageVersion, LanguageVariant? identifierVariant = null)
    {
        return IsWordCharacter(ch) || ch == (int)CharacterCodes.Dollar ||
            // "-" and ":" are valid in JSX Identifiers
            (identifierVariant == LanguageVariant.JSX ? (ch == (int)CharacterCodes.minus || ch == (int)CharacterCodes.colon) : false) ||
            (ch > (int)CharacterCodes.maxAsciiCharacter && IsUnicodeIdentifierPart(ch, languageVersion));
    }

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

    private enum ClassSetExpressionType
    {
        Unknown,
        ClassUnion,
        ClassIntersection,
        ClassSubtraction,
    }

    private static int CodePointAt(string s, int i)
    {
        // TODO(jakebailey): this is wrong and should have ?? 0; but all users are okay with it
        // (String.prototype.codePointAt: undefined outside the string, a lone surrogate as itself.)
        if ((uint)i >= (uint)s.Length)
        {
            return NaN;
        }
        int first = s[i];
        if (first >= 0xD800 && first <= 0xDBFF && i + 1 < s.Length)
        {
            int second = s[i + 1];
            if (second >= 0xDC00 && second <= 0xDFFF)
            {
                return ((first - 0xD800) * 0x400) + (second - 0xDC00) + 0x10000;
            }
        }
        return first;
    }

    private static int CharSize(int ch)
    {
        if (ch >= 0x10000)
        {
            return 2;
        }
        if (ch == (int)CharacterCodes.EOF)
        {
            return 0;
        }
        return 1;
    }

    // Derived from the 10.1.1 UTF16Encoding of the ES6 Spec.
    private static string Utf16EncodeAsStringFallback(int codePoint)
    {
        Debug.Assert(0x0 <= codePoint && codePoint <= 0x10FFFF);

        if (codePoint <= 65535)
        {
            return FromCharCode(codePoint);
        }

        var codeUnit1 = (int)Math.Floor((codePoint - 65536) / 1024.0) + 0xD800;
        var codeUnit2 = ((codePoint - 65536) % 1024) + 0xDC00;

        return string.Concat(FromCharCode(codeUnit1), FromCharCode(codeUnit2));
    }

    // scanner.ts: `String.fromCodePoint ? codePoint => String.fromCodePoint(codePoint) : utf16EncodeAsStringFallback`.
    // .NET has no String.fromCodePoint (char.ConvertFromUtf32 rejects surrogate code points, which
    // String.fromCodePoint accepts), so the worker is the fallback, which computes the same string.
    private static readonly Func<int, string> Utf16EncodeAsStringWorker = Utf16EncodeAsStringFallback;

    internal static string Utf16EncodeAsString(int codePoint)
    {
        return Utf16EncodeAsStringWorker(codePoint);
    }

    // Table 66: Non-binary Unicode property aliases and their canonical property names
    // https://tc39.es/ecma262/#table-nonbinary-unicode-properties
    private static readonly OrderedDictionary<string, string> NonBinaryUnicodeProperties = new(StringComparer.Ordinal)
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
    private static readonly OrderedStringSet BinaryUnicodeProperties = new(["ASCII", "ASCII_Hex_Digit", "AHex", "Alphabetic", "Alpha", "Any", "Assigned", "Bidi_Control", "Bidi_C", "Bidi_Mirrored", "Bidi_M", "Case_Ignorable", "CI", "Cased", "Changes_When_Casefolded", "CWCF", "Changes_When_Casemapped", "CWCM", "Changes_When_Lowercased", "CWL", "Changes_When_NFKC_Casefolded", "CWKCF", "Changes_When_Titlecased", "CWT", "Changes_When_Uppercased", "CWU", "Dash", "Default_Ignorable_Code_Point", "DI", "Deprecated", "Dep", "Diacritic", "Dia", "Emoji", "Emoji_Component", "EComp", "Emoji_Modifier", "EMod", "Emoji_Modifier_Base", "EBase", "Emoji_Presentation", "EPres", "Extended_Pictographic", "ExtPict", "Extender", "Ext", "Grapheme_Base", "Gr_Base", "Grapheme_Extend", "Gr_Ext", "Hex_Digit", "Hex", "IDS_Binary_Operator", "IDSB", "IDS_Trinary_Operator", "IDST", "ID_Continue", "IDC", "ID_Start", "IDS", "Ideographic", "Ideo", "Join_Control", "Join_C", "Logical_Order_Exception", "LOE", "Lowercase", "Lower", "Math", "Noncharacter_Code_Point", "NChar", "Pattern_Syntax", "Pat_Syn", "Pattern_White_Space", "Pat_WS", "Quotation_Mark", "QMark", "Radical", "Regional_Indicator", "RI", "Sentence_Terminal", "STerm", "Soft_Dotted", "SD", "Terminal_Punctuation", "Term", "Unified_Ideograph", "UIdeo", "Uppercase", "Upper", "Variation_Selector", "VS", "White_Space", "space", "XID_Continue", "XIDC", "XID_Start", "XIDS"]);

    // Table 68: Binary Unicode properties of strings
    // https://tc39.es/ecma262/#table-binary-unicode-properties-of-strings
    private static readonly OrderedStringSet BinaryUnicodePropertiesOfStrings = new(["Basic_Emoji", "Emoji_Keycap_Sequence", "RGI_Emoji_Modifier_Sequence", "RGI_Emoji_Flag_Sequence", "RGI_Emoji_Tag_Sequence", "RGI_Emoji_ZWJ_Sequence", "RGI_Emoji"]);

    // Unicode 15.1
    private static readonly ValuesOfNonBinaryUnicodePropertiesType ValuesOfNonBinaryUnicodeProperties = CreateValuesOfNonBinaryUnicodeProperties();

    private sealed class ValuesOfNonBinaryUnicodePropertiesType
    {
        public required OrderedStringSet General_Category { get; init; }

        public required OrderedStringSet Script { get; init; }

        public OrderedStringSet Script_Extensions { get; set; } = null!;

        // `valuesOfNonBinaryUnicodeProperties[propertyName]` for a canonical property name.
        public OrderedStringSet this[string propertyName] => propertyName switch
        {
            "General_Category" => General_Category,
            "Script" => Script,
            _ => Script_Extensions,
        };
    }

    private static ValuesOfNonBinaryUnicodePropertiesType CreateValuesOfNonBinaryUnicodeProperties()
    {
        var values = new ValuesOfNonBinaryUnicodePropertiesType
        {
            General_Category = new(["C", "Other", "Cc", "Control", "cntrl", "Cf", "Format", "Cn", "Unassigned", "Co", "Private_Use", "Cs", "Surrogate", "L", "Letter", "LC", "Cased_Letter", "Ll", "Lowercase_Letter", "Lm", "Modifier_Letter", "Lo", "Other_Letter", "Lt", "Titlecase_Letter", "Lu", "Uppercase_Letter", "M", "Mark", "Combining_Mark", "Mc", "Spacing_Mark", "Me", "Enclosing_Mark", "Mn", "Nonspacing_Mark", "N", "Number", "Nd", "Decimal_Number", "digit", "Nl", "Letter_Number", "No", "Other_Number", "P", "Punctuation", "punct", "Pc", "Connector_Punctuation", "Pd", "Dash_Punctuation", "Pe", "Close_Punctuation", "Pf", "Final_Punctuation", "Pi", "Initial_Punctuation", "Po", "Other_Punctuation", "Ps", "Open_Punctuation", "S", "Symbol", "Sc", "Currency_Symbol", "Sk", "Modifier_Symbol", "Sm", "Math_Symbol", "So", "Other_Symbol", "Z", "Separator", "Zl", "Line_Separator", "Zp", "Paragraph_Separator", "Zs", "Space_Separator"]),
            Script = new(["Adlm", "Adlam", "Aghb", "Caucasian_Albanian", "Ahom", "Arab", "Arabic", "Armi", "Imperial_Aramaic", "Armn", "Armenian", "Avst", "Avestan", "Bali", "Balinese", "Bamu", "Bamum", "Bass", "Bassa_Vah", "Batk", "Batak", "Beng", "Bengali", "Bhks", "Bhaiksuki", "Bopo", "Bopomofo", "Brah", "Brahmi", "Brai", "Braille", "Bugi", "Buginese", "Buhd", "Buhid", "Cakm", "Chakma", "Cans", "Canadian_Aboriginal", "Cari", "Carian", "Cham", "Cher", "Cherokee", "Chrs", "Chorasmian", "Copt", "Coptic", "Qaac", "Cpmn", "Cypro_Minoan", "Cprt", "Cypriot", "Cyrl", "Cyrillic", "Deva", "Devanagari", "Diak", "Dives_Akuru", "Dogr", "Dogra", "Dsrt", "Deseret", "Dupl", "Duployan", "Egyp", "Egyptian_Hieroglyphs", "Elba", "Elbasan", "Elym", "Elymaic", "Ethi", "Ethiopic", "Geor", "Georgian", "Glag", "Glagolitic", "Gong", "Gunjala_Gondi", "Gonm", "Masaram_Gondi", "Goth", "Gothic", "Gran", "Grantha", "Grek", "Greek", "Gujr", "Gujarati", "Guru", "Gurmukhi", "Hang", "Hangul", "Hani", "Han", "Hano", "Hanunoo", "Hatr", "Hatran", "Hebr", "Hebrew", "Hira", "Hiragana", "Hluw", "Anatolian_Hieroglyphs", "Hmng", "Pahawh_Hmong", "Hmnp", "Nyiakeng_Puachue_Hmong", "Hrkt", "Katakana_Or_Hiragana", "Hung", "Old_Hungarian", "Ital", "Old_Italic", "Java", "Javanese", "Kali", "Kayah_Li", "Kana", "Katakana", "Kawi", "Khar", "Kharoshthi", "Khmr", "Khmer", "Khoj", "Khojki", "Kits", "Khitan_Small_Script", "Knda", "Kannada", "Kthi", "Kaithi", "Lana", "Tai_Tham", "Laoo", "Lao", "Latn", "Latin", "Lepc", "Lepcha", "Limb", "Limbu", "Lina", "Linear_A", "Linb", "Linear_B", "Lisu", "Lyci", "Lycian", "Lydi", "Lydian", "Mahj", "Mahajani", "Maka", "Makasar", "Mand", "Mandaic", "Mani", "Manichaean", "Marc", "Marchen", "Medf", "Medefaidrin", "Mend", "Mende_Kikakui", "Merc", "Meroitic_Cursive", "Mero", "Meroitic_Hieroglyphs", "Mlym", "Malayalam", "Modi", "Mong", "Mongolian", "Mroo", "Mro", "Mtei", "Meetei_Mayek", "Mult", "Multani", "Mymr", "Myanmar", "Nagm", "Nag_Mundari", "Nand", "Nandinagari", "Narb", "Old_North_Arabian", "Nbat", "Nabataean", "Newa", "Nkoo", "Nko", "Nshu", "Nushu", "Ogam", "Ogham", "Olck", "Ol_Chiki", "Orkh", "Old_Turkic", "Orya", "Oriya", "Osge", "Osage", "Osma", "Osmanya", "Ougr", "Old_Uyghur", "Palm", "Palmyrene", "Pauc", "Pau_Cin_Hau", "Perm", "Old_Permic", "Phag", "Phags_Pa", "Phli", "Inscriptional_Pahlavi", "Phlp", "Psalter_Pahlavi", "Phnx", "Phoenician", "Plrd", "Miao", "Prti", "Inscriptional_Parthian", "Rjng", "Rejang", "Rohg", "Hanifi_Rohingya", "Runr", "Runic", "Samr", "Samaritan", "Sarb", "Old_South_Arabian", "Saur", "Saurashtra", "Sgnw", "SignWriting", "Shaw", "Shavian", "Shrd", "Sharada", "Sidd", "Siddham", "Sind", "Khudawadi", "Sinh", "Sinhala", "Sogd", "Sogdian", "Sogo", "Old_Sogdian", "Sora", "Sora_Sompeng", "Soyo", "Soyombo", "Sund", "Sundanese", "Sylo", "Syloti_Nagri", "Syrc", "Syriac", "Tagb", "Tagbanwa", "Takr", "Takri", "Tale", "Tai_Le", "Talu", "New_Tai_Lue", "Taml", "Tamil", "Tang", "Tangut", "Tavt", "Tai_Viet", "Telu", "Telugu", "Tfng", "Tifinagh", "Tglg", "Tagalog", "Thaa", "Thaana", "Thai", "Tibt", "Tibetan", "Tirh", "Tirhuta", "Tnsa", "Tangsa", "Toto", "Ugar", "Ugaritic", "Vaii", "Vai", "Vith", "Vithkuqi", "Wara", "Warang_Citi", "Wcho", "Wancho", "Xpeo", "Old_Persian", "Xsux", "Cuneiform", "Yezi", "Yezidi", "Yiii", "Yi", "Zanb", "Zanabazar_Square", "Zinh", "Inherited", "Qaai", "Zyyy", "Common", "Zzzz", "Unknown"]),
        };
        // The Script_Extensions property of a character contains one or more Script values. See https://www.unicode.org/reports/tr24/#Script_Extensions
        // Here since each Unicode property value expression only allows a single value, its values can be considered the same as those of the Script property.
        values.Script_Extensions = values.Script;
        return values;
    }

    //// JavaScript semantics the ported functions rely on.

    /// <summary>
    /// A JavaScript Set&lt;string&gt;: membership plus iteration in insertion order (getSpellingSuggestion
    /// returns the first best candidate, so the order is observable).
    /// </summary>
    private sealed class OrderedStringSet : IEnumerable<string>
    {
        private readonly List<string> items = [];
        private readonly HashSet<string> set = new(StringComparer.Ordinal);

        public OrderedStringSet()
        {
        }

        public OrderedStringSet(IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                Add(value);
            }
        }

        public bool Has(string value) => set.Contains(value);

        public void Add(string value)
        {
            if (set.Add(value))
            {
                items.Add(value);
            }
        }

        public IEnumerator<string> GetEnumerator() => items.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// A string that TypeScript builds piece by piece with `+=` (`result += text.substring(start, pos)`).
    /// JavaScript engines concatenate in constant time (ropes); a .NET string concatenation copies, which makes
    /// such loops quadratic (e.g. a template literal with many CRLF lines). typescript-go collects the parts and
    /// joins them once (`parts = append(parts, ...)`, `strings.Join(parts, "")` in scanTemplateAndSetTokenValue)
    /// or uses a strings.Builder (scanString, scanNumberFragment, scanIdentifierParts, scanBinaryOrOctalDigits),
    /// internal/scanner/scanner.go; this is the counterpart: the parts are ranges of strings, joined into a
    /// string of the exact length at the end. Contiguous ranges of one text are merged, so a value that is a
    /// single range of the source becomes one Substring, as TypeScript's `"" + text.substring(start, pos)` does.
    /// Used as a local variable (a mutable struct, so that a value of at most one part allocates no list).
    /// </summary>
    private struct StringParts
    {
        private List<Part>? parts; // the earlier parts
        private string? source; // the last part: source[start..end)
        private int start;
        private int end;
        private int length; // the length of the earlier parts

        /// <summary>`result += text.substring(from, to)`.</summary>
        public void Append(string text, int from, int to)
        {
            // String.prototype.substring: clamp to the string, swap reversed bounds.
            from = Math.Clamp(from, 0, text.Length);
            to = Math.Clamp(to, 0, text.Length);
            if (from > to)
            {
                (from, to) = (to, from);
            }
            if (from == to)
            {
                return;
            }
            if (source != null)
            {
                if (ReferenceEquals(source, text) && from == end)
                {
                    end = to;
                    return;
                }
                parts ??= [];
                parts.Add(new Part(source, start, end - start));
                length += end - start;
            }
            source = text;
            start = from;
            end = to;
        }

        /// <summary>`result += value`.</summary>
        public void Append(string value)
        {
            Append(value, 0, value.Length);
        }

        /// <summary>The accumulated string.</summary>
        public readonly string Result()
        {
            if (source == null)
            {
                return "";
            }
            if (parts == null)
            {
                return start == 0 && end == source.Length ? source : source.Substring(start, end - start);
            }
            return string.Create(length + (end - start), this, static (span, self) =>
            {
                var at = 0;
                foreach (var part in self.parts!)
                {
                    part.Source.AsSpan(part.Start, part.Length).CopyTo(span.Slice(at));
                    at += part.Length;
                }
                self.source.AsSpan(self.start, self.end - self.start).CopyTo(span.Slice(at));
            });
        }

        private readonly record struct Part(string Source, int Start, int Length);
    }

    /// <summary>`text.charCodeAt(pos)`: NaN outside the string.</summary>
    private static int CharCodeAt(string text, int pos)
    {
        return (uint)pos < (uint)text.Length ? text[pos] : NaN;
    }

    /// <summary>`String.fromCharCode(code)`: the code unit ToUint16(code) (NaN gives "\0").</summary>
    private static string FromCharCode(int code)
    {
        return ((char)(code & 0xFFFF)).ToString();
    }

    /// <summary>JavaScript's String.prototype.substring(start, end): clamps to the string and swaps reversed bounds.</summary>
    private static string Substring(string text, int start, int end)
    {
        var length = text.Length;
        start = Math.Clamp(start, 0, length);
        end = Math.Clamp(end, 0, length);
        if (start > end)
        {
            (start, end) = (end, start);
        }
        return text.Substring(start, end - start);
    }

    /// <summary>JavaScript's String.prototype.slice(start, end) for non-negative bounds: clamps; reversed bounds give "".</summary>
    private static string Slice(string text, int start, int end)
    {
        var length = text.Length;
        start = Math.Min(start, length);
        end = Math.Min(end, length);
        return start < end ? text.Substring(start, end - start) : "";
    }

    /// <summary>JavaScript truthiness of a value (`if (value)`): undefined, null, false, 0, NaN and "" are falsy.</summary>
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
}

/// <summary>
/// types.ts: SourceFileLike (the members getLineStarts uses: `readonly text: string; lineMap?: readonly number[]`).
/// </summary>
internal interface ISourceFileLike
{
    string Text { get; }

    IReadOnlyList<int>? LineMap { get; set; }
}
