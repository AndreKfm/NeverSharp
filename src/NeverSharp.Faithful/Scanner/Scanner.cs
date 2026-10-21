// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/scanner.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/scanner/scanner.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Runtime.CompilerServices;
using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>scanner.ts: ErrorCallback.</summary>
internal delegate void ErrorCallback(DiagnosticMessage message, int length, object? arg0);

/// <summary>
/// scanner.ts. The file-level functions are the static members; an instance is the object that createScanner
/// returns (TypeScript's `interface Scanner`), see Scanner.Instance.cs.
/// </summary>
internal sealed partial class Scanner
{
    // The value of `text.charCodeAt(pos)` and `text.codePointAt(pos)` outside the string (NaN and undefined in
    // TypeScript). Like NaN it is unequal to every character code, it is not EOF (-1), it fails every `>`/`>=` test
    // and every range test the scanner makes, and charSize gives 1 for it, as for undefined.
    private const int CharCodeUndefined = int.MinValue;

    // The small character tests and accessors (isLineBreak, isDigit, isIdentifierPart, charCodeUnchecked, codePointAt,
    // charSize, ...) carry MethodImplOptions.AggressiveInlining. Their counterparts in typescript-go
    // (internal/stringutil/util.go IsLineBreak, IsDigit, ...; internal/scanner/scanner.go char, charAt) are leaf functions
    // that the Go compiler inlines; NativeAOT does not inline them into the very large scan method on its own.

    internal static bool TokenIsIdentifierOrKeyword(SyntaxKind token)
    {
        return token >= SyntaxKind.Identifier;
    }

    internal static bool TokenIsIdentifierOrKeywordOrGreaterThan(SyntaxKind token)
    {
        return token == SyntaxKind.GreaterThanToken || TokenIsIdentifierOrKeyword(token);
    }

    // TypeScript: `const textToKeywordObj: MapLike<KeywordSyntaxKind>` (an object literal; its key order is kept).
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

    // TypeScript: `new Map(Object.entries(textToKeywordObj))`; only looked up, so its order does not matter.
    private static readonly Dictionary<string, SyntaxKind> TextToKeyword = new(TextToKeywordObj, StringComparer.Ordinal);

    private static readonly OrderedDictionary<string, SyntaxKind> TextToToken = MakeTextToToken();

    private static OrderedDictionary<string, SyntaxKind> MakeTextToToken()
    {
        // TypeScript: `new Map(Object.entries({ ...textToKeywordObj, "{": SyntaxKind.OpenBraceToken, ... }))`.
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

    private static readonly Dictionary<int, RegularExpressionFlags> CharCodeToRegExpFlag = new()
    {
        [(int)CharacterCodes.d] = RegularExpressionFlags.HasIndices,
        [(int)CharacterCodes.g] = RegularExpressionFlags.Global,
        [(int)CharacterCodes.i] = RegularExpressionFlags.IgnoreCase,
        [(int)CharacterCodes.m] = RegularExpressionFlags.Multiline,
        [(int)CharacterCodes.s] = RegularExpressionFlags.DotAll,
        [(int)CharacterCodes.u] = RegularExpressionFlags.Unicode,
        [(int)CharacterCodes.v] = RegularExpressionFlags.UnicodeSets,
        [(int)CharacterCodes.y] = RegularExpressionFlags.Sticky,
    };

    private static readonly Dictionary<RegularExpressionFlags, ScriptTarget> RegExpFlagToFirstAvailableLanguageVersion = new()
    {
        [RegularExpressionFlags.HasIndices] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsHasIndices,
        [RegularExpressionFlags.DotAll] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsDotAll,
        [RegularExpressionFlags.Unicode] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsUnicode,
        [RegularExpressionFlags.UnicodeSets] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsUnicodeSets,
        [RegularExpressionFlags.Sticky] = Types.LanguageFeatureMinimumTarget.RegularExpressionFlagsSticky,
    };

    // The regular expressions of scanner.ts are banned in Track F (System.Text.RegularExpressions); each one is the
    // hand-written matcher below, with exactly the expression's semantics. A matcher works on text[start, end) in place
    // of the string slice TypeScript matches (typescript-go matches comment directives in place as well:
    // internal/scanner/scanner.go, processCommentDirective).

    // TypeScript: `(text: string) => RegExpExecArray | null` reduced to what getDirectiveFromComment reads: match[1].
    private delegate string? CommentDirectiveRegEx(string text, int start, int end);

    /// <summary>
    /// Test for whether a single line comment with leading whitespace trimmed's text contains a directive.
    /// TypeScript: <c>/^\/\/\/?\s*@(ts-expect-error|ts-ignore)/</c>.
    /// </summary>
    private static readonly CommentDirectiveRegEx CommentDirectiveRegExSingleLine = (text, start, end) =>
    {
        var p = start;
        if (!(p + 1 < end && text[p] == '/' && text[p + 1] == '/'))
        {
            return null;
        }
        p += 2;
        // `\/?` is greedy; giving the slash back cannot help, since `\s*@` does not match a slash.
        if (p < end && text[p] == '/')
        {
            p++;
        }
        while (p < end && IsRegExpWhiteSpace(text[p]))
        {
            p++;
        }
        return MatchCommentDirectiveName(text, p, end);
    };

    /// <summary>
    /// Test for whether a multi-line comment with leading whitespace trimmed's last line contains a directive.
    /// TypeScript: <c>/^(?:\/|\*)*\s*@(ts-expect-error|ts-ignore)/</c>.
    /// </summary>
    private static readonly CommentDirectiveRegEx CommentDirectiveRegExMultiLine = (text, start, end) =>
    {
        var p = start;
        // `(?:\/|\*)*` is greedy; giving characters back cannot help, since `\s*@` matches neither '/' nor '*'.
        while (p < end && (text[p] == '/' || text[p] == '*'))
        {
            p++;
        }
        while (p < end && IsRegExpWhiteSpace(text[p]))
        {
            p++;
        }
        return MatchCommentDirectiveName(text, p, end);
    };

    // `@(ts-expect-error|ts-ignore)` at p; the captured alternative or null.
    private static string? MatchCommentDirectiveName(string text, int p, int end)
    {
        if (!(p < end && text[p] == '@'))
        {
            return null;
        }
        p++;
        if (MatchesAt(text, p, end, "ts-expect-error"))
        {
            return "ts-expect-error";
        }
        if (MatchesAt(text, p, end, "ts-ignore"))
        {
            return "ts-ignore";
        }
        return null;
    }

    private static bool MatchesAt(string text, int p, int end, string literal)
    {
        return end - p >= literal.Length && string.CompareOrdinal(text, p, literal, 0, literal.Length) == 0;
    }

    /// <summary>TypeScript: <c>/@(?:see|link)/i</c>, tested (anywhere) on text[start, end).</summary>
    private static bool JsDocSeeOrLinkTest(string text, int start, int end)
    {
        // Without the u flag, /i canonicalizes with toUpperCase but never maps a non-ASCII character to an ASCII one,
        // so only the ASCII letters match case-insensitively.
        for (var i = start; i < end; i++)
        {
            if (text[i] != '@')
            {
                continue;
            }
            if (end - (i + 1) >= 3 && IsAsciiLetterIgnoreCase(text[i + 1], 's') && IsAsciiLetterIgnoreCase(text[i + 2], 'e') && IsAsciiLetterIgnoreCase(text[i + 3], 'e'))
            {
                return true;
            }
            if (end - (i + 1) >= 4 && IsAsciiLetterIgnoreCase(text[i + 1], 'l') && IsAsciiLetterIgnoreCase(text[i + 2], 'i') && IsAsciiLetterIgnoreCase(text[i + 3], 'n') && IsAsciiLetterIgnoreCase(text[i + 4], 'k'))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsAsciiLetterIgnoreCase(char ch, char lowerCaseLetter)
    {
        return ch == lowerCaseLetter || ch == lowerCaseLetter - ('a' - 'A');
    }

    // `\s` of a JavaScript regular expression and the characters String.prototype.trimStart removes: WhiteSpace
    // (TAB, VT, FF, ZWNBSP and every Zs character) and LineTerminator (LF, CR, LS, PS).
    private static bool IsRegExpWhiteSpace(int ch)
    {
        return ch == (int)CharacterCodes.tab ||
            ch == (int)CharacterCodes.lineFeed ||
            ch == (int)CharacterCodes.verticalTab ||
            ch == (int)CharacterCodes.formFeed ||
            ch == (int)CharacterCodes.carriageReturn ||
            ch == (int)CharacterCodes.space ||
            ch == (int)CharacterCodes.nonBreakingSpace ||
            ch == (int)CharacterCodes.ogham ||
            ch >= (int)CharacterCodes.enQuad && ch <= (int)CharacterCodes.hairSpace ||
            ch == (int)CharacterCodes.lineSeparator ||
            ch == (int)CharacterCodes.paragraphSeparator ||
            ch == (int)CharacterCodes.narrowNoBreakSpace ||
            ch == (int)CharacterCodes.mathematicalSpace ||
            ch == (int)CharacterCodes.ideographicSpace ||
            ch == (int)CharacterCodes.byteOrderMark;
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
            // TypeScript divides without truncation; lo is always even, so subtracting `mid % 2` below gives the
            // same even index as the truncating division here.
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

    internal static bool IsUnicodeIdentifierStart(int code, ScriptTarget languageVersion)
    {
        return languageVersion >= ScriptTarget.ES2015 ?
            LookupInUnicodeMap(code, UnicodeESNextIdentifierStart) :
            LookupInUnicodeMap(code, UnicodeES5IdentifierStart);
    }

    private static bool IsUnicodeIdentifierPart(int code, ScriptTarget languageVersion)
    {
        return languageVersion >= ScriptTarget.ES2015 ?
            LookupInUnicodeMap(code, UnicodeESNextIdentifierPart) :
            LookupInUnicodeMap(code, UnicodeES5IdentifierPart);
    }

    // TypeScript: `makeReverseMap<T>(source: Map<T, number>): T[]`, an array with holes (undefined) where no entry maps.
    private static string?[] MakeReverseMap(OrderedDictionary<string, SyntaxKind> source)
    {
        var length = 0;
        foreach (var (_, value) in source)
        {
            length = Math.Max(length, (int)value + 1);
        }
        var result = new string?[length];
        foreach (var (name, value) in source)
        {
            result[(int)value] = name;
        }
        return result;
    }

    private static readonly string?[] TokenStrings = MakeReverseMap(TextToToken);

    internal static string? TokenToString(SyntaxKind t)
    {
        return (uint)t < (uint)TokenStrings.Length ? TokenStrings[(int)t] : null;
    }

    internal static SyntaxKind? StringToToken(string s)
    {
        return TextToToken.TryGetValue(s, out var kind) ? kind : null;
    }

    internal static RegularExpressionFlags? CharacterCodeToRegularExpressionFlag(int ch)
    {
        return CharCodeToRegExpFlag.TryGetValue(ch, out var flag) ? flag : null;
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

    internal static IReadOnlyList<int> GetLineStarts(SourceFileLike sourceFile)
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
        var lineNumber = Core.BinarySearch(lineStarts, position, Core.Identity<int>, (a, b) => Core.CompareValues(a, b), lowerBound);
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

    internal static LineAndCharacter GetLineAndCharacterOfPosition(SourceFileLike sourceFile, int position)
    {
        return ComputeLineAndCharacterOfPosition(GetLineStarts(sourceFile), position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsWhiteSpaceLike(int ch)
    {
        return IsWhiteSpaceSingleLine(ch) || IsLineBreak(ch);
    }

    /// <summary>Does not include line breaks. For that, see isWhiteSpaceLike.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            ch >= (int)CharacterCodes.enQuad && ch <= (int)CharacterCodes.zeroWidthSpace ||
            ch == (int)CharacterCodes.narrowNoBreakSpace ||
            ch == (int)CharacterCodes.mathematicalSpace ||
            ch == (int)CharacterCodes.ideographicSpace ||
            ch == (int)CharacterCodes.byteOrderMark;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsLineBreak(int ch)
    {
        // ES5 7.3:
        // The ECMAScript line terminator characters are listed in Table 3.
        //     Table 3: Line Terminator Characters
        //     Code Unit Value     Name                    Formal Name
        //     \u000A              Line Feed               <LF>
        //     \u000D              Carriage Return         <CR>
        //     U+2028 (\ u2028)    Line separator          <LS>
        //     U+2029 (\ u2029)    Paragraph separator     <PS>
        // Only the characters in Table 3 are treated as line terminators. Other new line or line
        // breaking characters are treated as white space but not as line terminators.

        return ch == (int)CharacterCodes.lineFeed ||
            ch == (int)CharacterCodes.carriageReturn ||
            ch == (int)CharacterCodes.lineSeparator ||
            ch == (int)CharacterCodes.paragraphSeparator;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDigit(int ch)
    {
        return ch >= (int)CharacterCodes._0 && ch <= (int)CharacterCodes._9;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsHexDigit(int ch)
    {
        return IsDigit(ch) || ch >= (int)CharacterCodes.A && ch <= (int)CharacterCodes.F || ch >= (int)CharacterCodes.a && ch <= (int)CharacterCodes.f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsASCIILetter(int ch)
    {
        return ch >= (int)CharacterCodes.A && ch <= (int)CharacterCodes.Z || ch >= (int)CharacterCodes.a && ch <= (int)CharacterCodes.z;
    }

    // Section 6.1.4
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsWordCharacter(int ch)
    {
        return IsASCIILetter(ch) || IsDigit(ch) || ch == (int)CharacterCodes._;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    /// TypeScript: <c>const shebangTriviaRegex = /^#!.*/</c>, executed on the whole text: the length of the match, or
    /// -1 for no match. `.` matches every UTF-16 code unit except the line terminators LF, CR, LS and PS.
    /// </summary>
    private static int ShebangTriviaRegexExec(string text)
    {
        if (!(text.Length >= 2 && text[0] == '#' && text[1] == '!'))
        {
            return -1;
        }
        var p = 2;
        while (p < text.Length && !IsLineBreak(text[p]))
        {
            p++;
        }
        return p;
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
        Debug.Assert(shebangLength >= 0); // TypeScript: `shebangTriviaRegex.exec(text)![0]`
        pos = pos + shebangLength;
        return pos;
    }

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
    private static TResult? IterateCommentRanges<TState, TResult>(bool reduce, string text, int pos, bool trailing, Func<int, int, SyntaxKind, bool, TState, TResult?, TResult?> cb, TState state, TResult? initial = default)
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
                        goto EndScan;
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
                    goto EndScan;
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
                    goto EndScan;
            }
        }
    EndScan: // TypeScript: the label `scan:` of the loop, left with `break scan`

        if (hasPendingCommentRange)
        {
            accumulator = cb(pendingPos, pendingEnd, pendingKind, pendingHasTrailingNewLine, state, accumulator);
        }

        return accumulator;
    }

    internal static TResult? ReduceEachLeadingCommentRange<TState, TResult>(string text, int pos, Func<int, int, SyntaxKind, bool, TState, TResult?, TResult?> cb, TState state, TResult? initial)
    {
        return IterateCommentRanges(/*reduce*/ true, text, pos, /*trailing*/ false, cb, state, initial);
    }

    internal static TResult? ReduceEachTrailingCommentRange<TState, TResult>(string text, int pos, Func<int, int, SyntaxKind, bool, TState, TResult?, TResult?> cb, TState state, TResult? initial)
    {
        return IterateCommentRanges(/*reduce*/ true, text, pos, /*trailing*/ true, cb, state, initial);
    }

    private static List<CommentRange>? AppendCommentRange(int pos, int end, SyntaxKind kind, bool hasTrailingNewLine, object? state, List<CommentRange>? comments)
    {
        _ = state; // TypeScript: `_state: any`, unused
        comments ??= [];
        comments.Add(new CommentRange { Kind = kind, Pos = pos, End = end, HasTrailingNewLine = hasTrailingNewLine });
        return comments;
    }

    // The callback as a cached delegate (TypeScript passes the function itself).
    private static readonly Func<int, int, SyntaxKind, bool, object?, List<CommentRange>?, List<CommentRange>?> AppendCommentRangeCallback = AppendCommentRange;

    internal static List<CommentRange>? GetLeadingCommentRanges(string text, int pos)
    {
        return ReduceEachLeadingCommentRange(text, pos, AppendCommentRangeCallback, /*state*/ null, /*initial*/ null);
    }

    internal static List<CommentRange>? GetTrailingCommentRanges(string text, int pos)
    {
        return ReduceEachTrailingCommentRange(text, pos, AppendCommentRangeCallback, /*state*/ null, /*initial*/ null);
    }

    /// <summary>Optionally, get the shebang.</summary>
    internal static string? GetShebang(string text)
    {
        var match = ShebangTriviaRegexExec(text);
        if (match >= 0)
        {
            return text[..match];
        }
        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsIdentifierStart(int ch, ScriptTarget languageVersion)
    {
        return IsASCIILetter(ch) || ch == (int)CharacterCodes.Dollar || ch == (int)CharacterCodes._ ||
            ch > (int)CharacterCodes.maxAsciiCharacter && IsUnicodeIdentifierStart(ch, languageVersion);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsIdentifierPart(int ch, ScriptTarget languageVersion, LanguageVariant identifierVariant = LanguageVariant.Standard)
    {
        return IsWordCharacter(ch) || ch == (int)CharacterCodes.Dollar ||
            // "-" and ":" are valid in JSX Identifiers
            (identifierVariant == LanguageVariant.JSX ? (ch == (int)CharacterCodes.minus || ch == (int)CharacterCodes.colon) : false) ||
            ch > (int)CharacterCodes.maxAsciiCharacter && IsUnicodeIdentifierPart(ch, languageVersion);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CodePointAt(string s, int i)
    {
        // TODO(jakebailey): this is wrong and should have ?? 0; but all users are okay with it
        // (String.prototype.codePointAt: undefined outside the string, a code point for a surrogate pair, else the code unit.)
        if ((uint)i >= (uint)s.Length)
        {
            return CharCodeUndefined;
        }
        int first = s[i];
        if (first >= 0xD800 && first <= 0xDBFF && i + 1 < s.Length)
        {
            int second = s[i + 1];
            if (second >= 0xDC00 && second <= 0xDFFF)
            {
                return (first - 0xD800) * 0x400 + second - 0xDC00 + 0x10000;
            }
        }
        return first;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    // TypeScript: `String.fromCodePoint ? codePoint => String.fromCodePoint(codePoint) : utf16EncodeAsStringFallback`;
    // String.fromCodePoint exists in every runtime TypeScript 6.0 supports, so the worker is fromCodePoint: it encodes
    // like the fallback and throws a RangeError for a value that is not a code point.
    private static string Utf16EncodeAsStringWorker(int codePoint)
    {
        if (codePoint < 0 || codePoint > 0x10FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(codePoint), codePoint, "Invalid code point " + codePoint.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return Utf16EncodeAsStringFallback(codePoint);
    }

    internal static string Utf16EncodeAsString(int codePoint)
    {
        return Utf16EncodeAsStringWorker(codePoint);
    }

    // String.fromCharCode(code): one UTF-16 code unit, the number taken modulo 2^16 (ToUint16).
    private static string FromCharCode(int code)
    {
        return ((char)(ushort)code).ToString();
    }

    // String.prototype.charCodeAt for the static functions: NaN (CharCodeUndefined) outside the string.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CharCodeAt(string text, int index)
    {
        return (uint)index < (uint)text.Length ? text[index] : CharCodeUndefined;
    }

    // JavaScript truthiness of a generic value (`if (result)`).
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
    private static readonly JsStringSet BinaryUnicodeProperties = new(["ASCII", "ASCII_Hex_Digit", "AHex", "Alphabetic", "Alpha", "Any", "Assigned", "Bidi_Control", "Bidi_C", "Bidi_Mirrored", "Bidi_M", "Case_Ignorable", "CI", "Cased", "Changes_When_Casefolded", "CWCF", "Changes_When_Casemapped", "CWCM", "Changes_When_Lowercased", "CWL", "Changes_When_NFKC_Casefolded", "CWKCF", "Changes_When_Titlecased", "CWT", "Changes_When_Uppercased", "CWU", "Dash", "Default_Ignorable_Code_Point", "DI", "Deprecated", "Dep", "Diacritic", "Dia", "Emoji", "Emoji_Component", "EComp", "Emoji_Modifier", "EMod", "Emoji_Modifier_Base", "EBase", "Emoji_Presentation", "EPres", "Extended_Pictographic", "ExtPict", "Extender", "Ext", "Grapheme_Base", "Gr_Base", "Grapheme_Extend", "Gr_Ext", "Hex_Digit", "Hex", "IDS_Binary_Operator", "IDSB", "IDS_Trinary_Operator", "IDST", "ID_Continue", "IDC", "ID_Start", "IDS", "Ideographic", "Ideo", "Join_Control", "Join_C", "Logical_Order_Exception", "LOE", "Lowercase", "Lower", "Math", "Noncharacter_Code_Point", "NChar", "Pattern_Syntax", "Pat_Syn", "Pattern_White_Space", "Pat_WS", "Quotation_Mark", "QMark", "Radical", "Regional_Indicator", "RI", "Sentence_Terminal", "STerm", "Soft_Dotted", "SD", "Terminal_Punctuation", "Term", "Unified_Ideograph", "UIdeo", "Uppercase", "Upper", "Variation_Selector", "VS", "White_Space", "space", "XID_Continue", "XIDC", "XID_Start", "XIDS"]);

    // Table 68: Binary Unicode properties of strings
    // https://tc39.es/ecma262/#table-binary-unicode-properties-of-strings
    private static readonly JsStringSet BinaryUnicodePropertiesOfStrings = new(["Basic_Emoji", "Emoji_Keycap_Sequence", "RGI_Emoji_Modifier_Sequence", "RGI_Emoji_Flag_Sequence", "RGI_Emoji_Tag_Sequence", "RGI_Emoji_ZWJ_Sequence", "RGI_Emoji"]);

    // Unicode 15.1
    private static readonly JsStringSet GeneralCategoryValues = new(["C", "Other", "Cc", "Control", "cntrl", "Cf", "Format", "Cn", "Unassigned", "Co", "Private_Use", "Cs", "Surrogate", "L", "Letter", "LC", "Cased_Letter", "Ll", "Lowercase_Letter", "Lm", "Modifier_Letter", "Lo", "Other_Letter", "Lt", "Titlecase_Letter", "Lu", "Uppercase_Letter", "M", "Mark", "Combining_Mark", "Mc", "Spacing_Mark", "Me", "Enclosing_Mark", "Mn", "Nonspacing_Mark", "N", "Number", "Nd", "Decimal_Number", "digit", "Nl", "Letter_Number", "No", "Other_Number", "P", "Punctuation", "punct", "Pc", "Connector_Punctuation", "Pd", "Dash_Punctuation", "Pe", "Close_Punctuation", "Pf", "Final_Punctuation", "Pi", "Initial_Punctuation", "Po", "Other_Punctuation", "Ps", "Open_Punctuation", "S", "Symbol", "Sc", "Currency_Symbol", "Sk", "Modifier_Symbol", "Sm", "Math_Symbol", "So", "Other_Symbol", "Z", "Separator", "Zl", "Line_Separator", "Zp", "Paragraph_Separator", "Zs", "Space_Separator"]);

    private static readonly JsStringSet ScriptValues = new(["Adlm", "Adlam", "Aghb", "Caucasian_Albanian", "Ahom", "Arab", "Arabic", "Armi", "Imperial_Aramaic", "Armn", "Armenian", "Avst", "Avestan", "Bali", "Balinese", "Bamu", "Bamum", "Bass", "Bassa_Vah", "Batk", "Batak", "Beng", "Bengali", "Bhks", "Bhaiksuki", "Bopo", "Bopomofo", "Brah", "Brahmi", "Brai", "Braille", "Bugi", "Buginese", "Buhd", "Buhid", "Cakm", "Chakma", "Cans", "Canadian_Aboriginal", "Cari", "Carian", "Cham", "Cher", "Cherokee", "Chrs", "Chorasmian", "Copt", "Coptic", "Qaac", "Cpmn", "Cypro_Minoan", "Cprt", "Cypriot", "Cyrl", "Cyrillic", "Deva", "Devanagari", "Diak", "Dives_Akuru", "Dogr", "Dogra", "Dsrt", "Deseret", "Dupl", "Duployan", "Egyp", "Egyptian_Hieroglyphs", "Elba", "Elbasan", "Elym", "Elymaic", "Ethi", "Ethiopic", "Geor", "Georgian", "Glag", "Glagolitic", "Gong", "Gunjala_Gondi", "Gonm", "Masaram_Gondi", "Goth", "Gothic", "Gran", "Grantha", "Grek", "Greek", "Gujr", "Gujarati", "Guru", "Gurmukhi", "Hang", "Hangul", "Hani", "Han", "Hano", "Hanunoo", "Hatr", "Hatran", "Hebr", "Hebrew", "Hira", "Hiragana", "Hluw", "Anatolian_Hieroglyphs", "Hmng", "Pahawh_Hmong", "Hmnp", "Nyiakeng_Puachue_Hmong", "Hrkt", "Katakana_Or_Hiragana", "Hung", "Old_Hungarian", "Ital", "Old_Italic", "Java", "Javanese", "Kali", "Kayah_Li", "Kana", "Katakana", "Kawi", "Khar", "Kharoshthi", "Khmr", "Khmer", "Khoj", "Khojki", "Kits", "Khitan_Small_Script", "Knda", "Kannada", "Kthi", "Kaithi", "Lana", "Tai_Tham", "Laoo", "Lao", "Latn", "Latin", "Lepc", "Lepcha", "Limb", "Limbu", "Lina", "Linear_A", "Linb", "Linear_B", "Lisu", "Lyci", "Lycian", "Lydi", "Lydian", "Mahj", "Mahajani", "Maka", "Makasar", "Mand", "Mandaic", "Mani", "Manichaean", "Marc", "Marchen", "Medf", "Medefaidrin", "Mend", "Mende_Kikakui", "Merc", "Meroitic_Cursive", "Mero", "Meroitic_Hieroglyphs", "Mlym", "Malayalam", "Modi", "Mong", "Mongolian", "Mroo", "Mro", "Mtei", "Meetei_Mayek", "Mult", "Multani", "Mymr", "Myanmar", "Nagm", "Nag_Mundari", "Nand", "Nandinagari", "Narb", "Old_North_Arabian", "Nbat", "Nabataean", "Newa", "Nkoo", "Nko", "Nshu", "Nushu", "Ogam", "Ogham", "Olck", "Ol_Chiki", "Orkh", "Old_Turkic", "Orya", "Oriya", "Osge", "Osage", "Osma", "Osmanya", "Ougr", "Old_Uyghur", "Palm", "Palmyrene", "Pauc", "Pau_Cin_Hau", "Perm", "Old_Permic", "Phag", "Phags_Pa", "Phli", "Inscriptional_Pahlavi", "Phlp", "Psalter_Pahlavi", "Phnx", "Phoenician", "Plrd", "Miao", "Prti", "Inscriptional_Parthian", "Rjng", "Rejang", "Rohg", "Hanifi_Rohingya", "Runr", "Runic", "Samr", "Samaritan", "Sarb", "Old_South_Arabian", "Saur", "Saurashtra", "Sgnw", "SignWriting", "Shaw", "Shavian", "Shrd", "Sharada", "Sidd", "Siddham", "Sind", "Khudawadi", "Sinh", "Sinhala", "Sogd", "Sogdian", "Sogo", "Old_Sogdian", "Sora", "Sora_Sompeng", "Soyo", "Soyombo", "Sund", "Sundanese", "Sylo", "Syloti_Nagri", "Syrc", "Syriac", "Tagb", "Tagbanwa", "Takr", "Takri", "Tale", "Tai_Le", "Talu", "New_Tai_Lue", "Taml", "Tamil", "Tang", "Tangut", "Tavt", "Tai_Viet", "Telu", "Telugu", "Tfng", "Tifinagh", "Tglg", "Tagalog", "Thaa", "Thaana", "Thai", "Tibt", "Tibetan", "Tirh", "Tirhuta", "Tnsa", "Tangsa", "Toto", "Ugar", "Ugaritic", "Vaii", "Vai", "Vith", "Vithkuqi", "Wara", "Warang_Citi", "Wcho", "Wancho", "Xpeo", "Old_Persian", "Xsux", "Cuneiform", "Yezi", "Yezidi", "Yiii", "Yi", "Zanb", "Zanabazar_Square", "Zinh", "Inherited", "Qaai", "Zyyy", "Common", "Zzzz", "Unknown"]);

    // TypeScript: `const valuesOfNonBinaryUnicodeProperties = { General_Category, Script, Script_Extensions }`.
    // The Script_Extensions property of a character contains one or more Script values. See https://www.unicode.org/reports/tr24/#Script_Extensions
    // Here since each Unicode property value expression only allows a single value, its values can be considered the same as those of the Script property.
    private static readonly Dictionary<string, JsStringSet> ValuesOfNonBinaryUnicodeProperties = new(StringComparer.Ordinal)
    {
        ["General_Category"] = GeneralCategoryValues,
        ["Script"] = ScriptValues,
        ["Script_Extensions"] = ScriptValues,
    };

    /// <summary>A JavaScript Set of strings: membership plus iteration in insertion order.</summary>
    private sealed class JsStringSet : IEnumerable<string>
    {
        private readonly List<string> values = [];
        private readonly HashSet<string> set = new(StringComparer.Ordinal);

        public JsStringSet()
        {
        }

        public JsStringSet(string[] values)
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
                values.Add(value);
            }
        }

        public IEnumerator<string> GetEnumerator() => values.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
