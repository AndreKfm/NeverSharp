// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): token-stream dump of Track F for gate G1a.
//
// Reproduces harness/astdump/dump-tokens-ts.mjs (format: docs/ast-dump-format.md §8) with the
// Track F scanner: each unit of the equivalence corpus is scanned with
// Scanner.CreateScanner(ScriptTarget.Latest, skipTrivia: true, variant, text, onError) until
// EndOfFileToken, without contextual rescans. Units run in parallel; each file is written whole.
// Default output: <corpus>/dumps/cs-faithful-tokens.

using System.Globalization;
using System.Text;
using NeverSharp.Faithful;
using NeverSharp.Generated;

namespace NeverSharp.Dump;

internal static class TokenDump
{
    private static readonly Dictionary<int, string> KindNames = Common.Names(EnumNames.SyntaxKind);
    private static readonly List<(int Bit, string Name)> FlagNames = SortedByName(EnumNames.TokenFlags);

    private static List<(int Bit, string Name)> SortedByName((int, string)[] table)
    {
        var list = new List<(int Bit, string Name)>(table);
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    public static int Run(Options options)
    {
        var corpus = options.Corpus;
        var outDir = options.Get("--out", Path.Combine(corpus, "dumps", "cs-faithful-tokens"));
        Common.ResetDirectory(outDir);
        var units = Common.ReadUnits(corpus);
        var started = DateTime.UtcNow;
        long tokens = 0;
        Parallel.ForEach(units, unit =>
        {
            var text = Common.ReadUnitText(corpus, unit);
            var (dump, count) = DumpTokens(text, unit.Kind == "TSX" || unit.Kind == "JSX" ? LanguageVariant.JSX : LanguageVariant.Standard);
            File.WriteAllText(Path.Combine(outDir, unit.Id + ".txt"), dump, Common.Utf8);
            Interlocked.Add(ref tokens, count);
        });
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"cs-faithful tokens: {units.Count} units, {tokens} tokens in {(DateTime.UtcNow - started).TotalSeconds:0.0} s"));
        return 0;
    }

    private static (string Dump, int Tokens) DumpTokens(string text, LanguageVariant variant)
    {
        var diagnostics = new StringBuilder();
        Scanner? scanner = null;
        scanner = Scanner.CreateScanner(ScriptTarget.Latest, skipTrivia: true, variant, text,
            (message, length, _) => diagnostics.Append(CultureInfo.InvariantCulture, $"!scan {message.Code} {scanner!.GetTokenEnd()} {length}\n"));
        var sb = new StringBuilder();
        var count = 0;
        for (var guard = 0; guard <= text.Length + 1; guard++)
        {
            var kind = scanner.Scan();
            count++;
            sb.Append(KindNames.TryGetValue((int)kind, out var kindName) ? kindName : "undefined").Append(' ')
                .Append(scanner.GetTokenFullStart().ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(scanner.GetTokenStart().ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(scanner.GetTokenEnd().ToString(CultureInfo.InvariantCulture));
            var flags = (int)scanner.GetTokenFlags();
            var first = true;
            foreach (var (bit, name) in FlagNames)
            {
                if ((flags & bit) != 0)
                {
                    sb.Append(first ? ' ' : '|').Append(name);
                    first = false;
                }
            }
            if (WithValue(kind))
            {
                sb.Append(" =");
                Json.AppendString(sb, scanner.GetTokenValue());
            }
            sb.Append('\n');
            if (kind == SyntaxKind.EndOfFileToken)
            {
                break;
            }
        }
        return (sb.Append(diagnostics).ToString(), count);
    }

    private static bool WithValue(SyntaxKind kind) =>
        kind is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier
        || (kind >= SyntaxKind.FirstLiteralToken && kind <= SyntaxKind.LastLiteralToken)
        || kind is SyntaxKind.NoSubstitutionTemplateLiteral or SyntaxKind.TemplateHead or SyntaxKind.TemplateMiddle or SyntaxKind.TemplateTail;
}

internal static class Json
{
    // JSON.stringify of a string (ECMA-262 QuoteJSONString, well-formed variant): lone surrogates are escaped.
    public static void AppendString(StringBuilder sb, string? value)
    {
        if (value is null)
        {
            sb.Append("undefined");
            return;
        }
        sb.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"': sb.Append(@"\"""); break;
                case '\\': sb.Append(@"\\"); break;
                case '\b': sb.Append(@"\b"); break;
                case '\f': sb.Append(@"\f"); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append(@"\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        sb.Append(c).Append(value[++i]);
                    }
                    else if (char.IsSurrogate(c))
                    {
                        sb.Append(@"\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
    }
}
