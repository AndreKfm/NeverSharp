// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): unit tests of module M2 (docs/modules/M2.md) for the exported
// scanner helpers that gate G1a does not reach. Compares with the results of tsc 6.0.3
// (harness/port/vectors-m2.mjs); canonical string forms as written there.

using System.Globalization;
using System.Text;
using System.Text.Json;
using NeverSharp.Generated;

namespace NeverSharp.Faithful.Tests;

public class M2Tests
{
    private static readonly Lazy<Dictionary<string, string>> Units = new(() =>
    {
        var map = new Dictionary<string, string>();
        foreach (var v in Vectors.ReadLines("M2", "units"))
        {
            using var doc = JsonDocument.Parse(v);
            map[doc.RootElement.GetProperty("id").GetString()!] = doc.RootElement.GetProperty("text").GetString()!;
        }
        return map;
    });

    private static string Text(JsonElement arg) => Units.Value[arg.GetProperty("unit").GetString()!];

    private static string? CanonRanges(IReadOnlyList<CommentRange>? ranges)
    {
        if (ranges is null)
        {
            return null;
        }
        var sb = new StringBuilder();
        for (var i = 0; i < ranges.Count; i++)
        {
            var r = ranges[i];
            if (i > 0)
            {
                sb.Append(';');
            }
            sb.Append(CultureInfo.InvariantCulture, $"{r.Pos}-{r.End}-{(int)r.Kind}-{(r.HasTrailingNewLine ? 1 : 0)}");
        }
        return sb.ToString();
    }

    private static string CanonNumbers(IReadOnlyList<int> numbers)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < numbers.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append(numbers[i].ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    [Fact]
    public void Trivia() => Vectors.Check("M2", "trivia", v =>
    {
        var text = Text(v.Args[0]);
        var pos = v.Args[1].GetInt32();
        return v.Function switch
        {
            "getLeadingCommentRanges" => CanonRanges(Scanner.GetLeadingCommentRanges(text, pos)),
            "getTrailingCommentRanges" => CanonRanges(Scanner.GetTrailingCommentRanges(text, pos)),
            "skipTrivia" => Scanner.SkipTrivia(text, pos, v.Args[2].GetBoolean(), v.Args[3].GetBoolean(), v.Args[4].GetBoolean()),
            _ => throw new InvalidOperationException(v.Function),
        };
    });

    [Fact]
    public void Lines()
    {
        var lineStarts = new Dictionary<string, IReadOnlyList<int>>();
        Vectors.Check("M2", "lines", v =>
        {
            switch (v.Function)
            {
                case "computeLineStarts":
                    var starts = Scanner.ComputeLineStarts(Text(v.Args[0]));
                    lineStarts[v.Args[0].GetProperty("unit").GetString()!] = starts;
                    return CanonNumbers(starts);
                case "getShebang":
                    return Scanner.GetShebang(Text(v.Args[0]));
                case "computeLineAndCharacterOfPosition":
                    var id = v.Args[0].GetProperty("lineStarts").GetString()!;
                    var lc = Scanner.ComputeLineAndCharacterOfPosition(lineStarts[id], v.Args[1].GetInt32());
                    return string.Create(CultureInfo.InvariantCulture, $"{lc.Line}:{lc.Character}");
                default:
                    throw new InvalidOperationException(v.Function);
            }
        });
    }

    [Fact]
    public void IdentifierText() => Vectors.Check("M2", "identifiers", v =>
        Scanner.IsIdentifierText(v.Args[0].GetString()!, (ScriptTarget)v.Args[1].GetInt32(), (LanguageVariant)v.Args[2].GetInt32()));

    [Fact]
    public void Tokens() => Vectors.Check("M2", "tokens", v => v.Function switch
    {
        "tokenToString" => Scanner.TokenToString((SyntaxKind)v.Args[0].GetInt32()),
        "stringToToken" => Scanner.StringToToken(v.Args[0].GetString()!) is { } kind ? (int)kind : null,
        "parsePseudoBigInt" => Utilities.ParsePseudoBigInt(v.Args[0].GetString()!),
        "getNameOfScriptTarget" => Utilities.GetNameOfScriptTarget((ScriptTarget)v.Args[0].GetInt32()),
        "utf16EncodeAsString" => Scanner.Utf16EncodeAsString(v.Args[0].GetInt32()),
        _ => throw new InvalidOperationException(v.Function),
    });
}
