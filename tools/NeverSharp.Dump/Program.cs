// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): token-stream dump of Track F for gate G1a.
//
// Reproduces harness/astdump/dump-tokens-ts.mjs (format: docs/ast-dump-format.md §8) with the
// Track F scanner: each unit of the equivalence corpus is scanned with
// Scanner.CreateScanner(ScriptTarget.Latest, skipTrivia: true, variant, text, onError) until
// EndOfFileToken, without contextual rescans. Units run in parallel; each file is written whole.
//
// Usage: NeverSharp.Dump tokens [--corpus <dir>] [--out <dir>]
//   --corpus: the equivalence corpus (manifest.json, units/); default artifacts/equivalence of this checkout
//   --out:    default <corpus>/dumps/cs-faithful-tokens

using System.Globalization;
using System.Text;
using System.Text.Json;
using NeverSharp.Dump;
using NeverSharp.Faithful;
using NeverSharp.Generated;

if (args.Length == 0 || args[0] != "tokens")
{
    Console.Error.WriteLine("usage: NeverSharp.Dump tokens [--corpus <dir>] [--out <dir>]");
    return 2;
}
string Arg(string name, string fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}
var corpus = Arg("--corpus", Path.Combine(FindRoot(), "artifacts", "equivalence"));
var outDir = Arg("--out", Path.Combine(corpus, "dumps", "cs-faithful-tokens"));
if (Directory.Exists(outDir))
{
    Directory.Delete(outDir, recursive: true);
}
Directory.CreateDirectory(outDir);

using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(corpus, "manifest.json")));
var units = new List<(string Id, string Kind)>();
foreach (var u in manifest.RootElement.GetProperty("units").EnumerateArray())
{
    units.Add((u.GetProperty("id").GetString()!, u.GetProperty("kind").GetString()!));
}

var kindNames = new Dictionary<int, string>();
foreach (var (value, name) in EnumNames.SyntaxKind)
{
    kindNames[value] = name;
}
var flagNames = new List<(int Bit, string Name)>(EnumNames.TokenFlags);
flagNames.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

var started = DateTime.UtcNow;
long tokens = 0;
var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Parallel.ForEach(units, unit =>
{
    // Node's readFileSync(..., "utf8") keeps a byte order mark as U+FEFF; Encoding.GetString does too.
    var text = utf8.GetString(File.ReadAllBytes(Path.Combine(corpus, "units", unit.Id + ".txt")));
    var (dump, count) = DumpTokens(text, unit.Kind == "TSX" || unit.Kind == "JSX" ? LanguageVariant.JSX : LanguageVariant.Standard);
    File.WriteAllText(Path.Combine(outDir, unit.Id + ".txt"), dump, utf8);
    Interlocked.Add(ref tokens, count);
});
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"cs-faithful tokens: {units.Count} units, {tokens} tokens in {(DateTime.UtcNow - started).TotalSeconds:0.0} s"));
return 0;

(string Dump, int Tokens) DumpTokens(string text, LanguageVariant variant)
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
        sb.Append(kindNames.TryGetValue((int)kind, out var kindName) ? kindName : "undefined").Append(' ')
            .Append(scanner.GetTokenFullStart().ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(scanner.GetTokenStart().ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(scanner.GetTokenEnd().ToString(CultureInfo.InvariantCulture));
        var flags = (int)scanner.GetTokenFlags();
        var first = true;
        foreach (var (bit, name) in flagNames)
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
            JsonString(sb, scanner.GetTokenValue());
        }
        sb.Append('\n');
        if (kind == SyntaxKind.EndOfFileToken)
        {
            break;
        }
    }
    return (sb.Append(diagnostics).ToString(), count);
}

static bool WithValue(SyntaxKind kind) =>
    kind is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier
    || (kind >= SyntaxKind.FirstLiteralToken && kind <= SyntaxKind.LastLiteralToken)
    || kind is SyntaxKind.NoSubstitutionTemplateLiteral or SyntaxKind.TemplateHead or SyntaxKind.TemplateMiddle or SyntaxKind.TemplateTail;

// JSON.stringify of a string (ECMA-262 QuoteJSONString, well-formed variant): lone surrogates are escaped.
static void JsonString(StringBuilder sb, string? value)
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

static string FindRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
    {
        dir = dir.Parent;
    }
    return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
}
