// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): AST dump of Track F for Gate 1.
//
// Reproduces harness/astdump/dump-ts.mjs + format.mjs (docs/ast-dump-format.md, format version 1):
// each unit is parsed with Parser.CreateSourceFile(fileName, text, { languageVersion: Latest,
// jsDocParsingMode: ParseAll, or ParseNone with --jsdoc-mode none }, setParentNodes: false, scriptKind); one line per node in pre-order
// (kind, pos, end, sorted NodeFlags names, "=HasJSDoc" when node.jsDoc is non-empty); after a
// node's line its jsDoc comments as "@" subtrees, then its children (forEachChild); then the parse
// diagnostics and the JSDoc diagnostics. Units that do not finish within the timeout are
// recorded as timeouts (v6.0.3's scanJsDocToken defect can loop forever), like the tsc dumper.
//
// The renderer works on plain lines, so "ast-replay" can check it without a parser: it parses
// tsc's own dump lines back into kinds, positions, flag bits and diagnostics and renders them again.

using System.Globalization;
using System.Text;
using System.Text.Json;
#if !NO_PARSER && TRACK_OPTIMIZED
using NeverSharp.Optimized;
#elif !NO_PARSER
using NeverSharp.Faithful;
#endif
using NeverSharp.Generated;

namespace NeverSharp.Dump;

internal readonly record struct DumpLine(int Depth, string Kind, int Pos, int End, List<string> Flags, bool JsDoc);

internal readonly record struct DumpDiagnostic(int Code, int Start, int Length);

internal static class AstDump
{
    private static readonly Dictionary<int, string> KindNames = Common.Names(EnumNames.SyntaxKind);
    private static readonly (int Bit, string Name)[] FlagBits = EnumNames.NodeFlags; // single-bit members, by value

    /// <summary>dump-ts.mjs flagNames: NodeFlags names in bit order; two bits mean something else on identifiers.</summary>
    public static List<string> FlagNames(SyntaxKind kind, int flags, bool hasJsDoc)
    {
        var names = new List<string>();
        foreach (var (bit, name) in FlagBits)
        {
            if ((flags & bit) == 0)
            {
                continue;
            }
            if (kind == SyntaxKind.Identifier && bit == (int)NodeFlags.ContainsThis)
            {
                names.Add("IdentifierHasExtendedUnicodeEscape");
            }
            else if (kind == SyntaxKind.Identifier && bit == (int)NodeFlags.HasAsyncFunctions)
            {
                names.Add("IdentifierIsInJSDocNamespace");
            }
            else
            {
                names.Add(name);
            }
        }
        if (hasJsDoc)
        {
            names.Add("=HasJSDoc");
        }
        return names;
    }

    /// <summary>format.mjs renderDump.</summary>
    public static string Render(List<DumpLine> lines, List<DumpDiagnostic> diag, List<DumpDiagnostic> jsdocdiag)
    {
        var sb = new StringBuilder();
        foreach (var n in lines)
        {
            sb.Append(' ', 2 * n.Depth);
            if (n.JsDoc)
            {
                sb.Append('@');
            }
            sb.Append(n.Kind).Append(' ').Append(n.Pos.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(n.End.ToString(CultureInfo.InvariantCulture));
            if (n.Flags.Count > 0)
            {
                var sorted = new List<string>(n.Flags);
                sorted.Sort(string.CompareOrdinal); // JavaScript's default sort: UTF-16 code units
                sb.Append(' ').Append(string.Join('|', sorted));
            }
            sb.Append('\n');
        }
        foreach (var d in diag)
        {
            sb.Append(CultureInfo.InvariantCulture, $"!diag {d.Code} {d.Start} {d.Length}\n");
        }
        foreach (var d in jsdocdiag)
        {
            sb.Append(CultureInfo.InvariantCulture, $"!jsdocdiag {d.Code} {d.Start} {d.Length}\n");
        }
        return sb.ToString();
    }

    public static int Run(Options options)
    {
#if NO_PARSER
        Console.Error.WriteLine("built without the parser (NO_PARSER)");
        return 2;
#else
        var corpus = options.Corpus;
        var jsdocMode = options.Get("--jsdoc-mode", "all");
        var mode = jsdocMode switch
        {
            "all" => JSDocParsingMode.ParseAll,
            "none" => JSDocParsingMode.ParseNone,
            _ => throw new InvalidOperationException("--jsdoc-mode must be all or none"),
        };
        var outDir = options.Get("--out", Path.Combine(corpus, "dumps", mode == JSDocParsingMode.ParseAll ? $"cs-{Track.Name}" : $"cs-{Track.Name}-parsenone"));
        var timeoutMs = int.Parse(options.Get("--timeout-ms", "10000"), CultureInfo.InvariantCulture);
        Common.ResetDirectory(outDir);
        var units = Common.ReadUnits(corpus);
        var ok = new List<string>();
        var timeout = new List<string>();
        var error = new List<(string Id, string Error)>();
        var started = DateTime.UtcNow;
        Parallel.ForEach(units, unit =>
        {
            var text = Common.ReadUnitText(corpus, unit);
            // Each unit on its own thread with a large stack: the parser recurses as deeply as the source
            // nests (tsc runs it in a worker thread). A unit that never finishes keeps its thread; the
            // process exits at the end regardless.
            string? result = null;
            string? outcome = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = DumpUnit(text, unit, mode);
                }
                catch (Exception e)
                {
                    outcome = e.ToString();
                }
            }, StackSize)
            {
                IsBackground = true,
            };
            thread.Start();
            if (!thread.Join(timeoutMs))
            {
                outcome = "timeout";
            }
            lock (ok)
            {
                if (outcome is null)
                {
                    ok.Add(unit.Id);
                }
                else if (outcome == "timeout")
                {
                    timeout.Add(unit.Id);
                }
                else
                {
                    error.Add((unit.Id, outcome));
                }
            }
            if (outcome is null)
            {
                File.WriteAllText(Path.Combine(outDir, unit.Id + ".txt"), result!, Common.Utf8);
            }
        });
        ok.Sort(string.CompareOrdinal);
        timeout.Sort(string.CompareOrdinal);
        error.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        var outcomes = new
        {
            timeoutMs,
            ok,
            timeout,
            error = error.ConvertAll(e => new { id = e.Id, error = e.Error }),
        };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(outDir)!, Path.GetFileName(outDir) + ".outcomes.json"), JsonSerializer.Serialize(outcomes), Common.Utf8);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"cs-{Track.Name}: ok {ok.Count}, timeout {timeout.Count}, error {error.Count} in {(DateTime.UtcNow - started).TotalSeconds:0.0} s"));
        for (var i = 0; i < Math.Min(5, error.Count); i++)
        {
            Console.WriteLine($"error {error[i].Id}: {error[i].Error}");
        }
        Environment.Exit(0); // threads of timed-out units may still run
        return 0;
#endif
    }

#if !NO_PARSER
    private const int StackSize = 256 * 1024 * 1024;

    private static string DumpUnit(string text, Unit unit, JSDocParsingMode mode)
    {
        var scriptKind = unit.Kind switch
        {
            "TS" => ScriptKind.TS,
            "TSX" => ScriptKind.TSX,
            "JS" => ScriptKind.JS,
            "JSX" => ScriptKind.JSX,
            "JSON" => ScriptKind.JSON,
            _ => throw new InvalidOperationException(unit.Kind),
        };
        var sourceFile = Parser.CreateSourceFile(unit.FileName, text,
            new CreateSourceFileOptions { LanguageVersion = ScriptTarget.Latest, JsDocParsingMode = mode },
            setParentNodes: false, scriptKind);
        var lines = new List<DumpLine>();
        void Visit(Node node, int depth, bool jsdoc)
        {
            var jsDoc = JSDocContainers.GetJsDoc(node);
            lines.Add(new DumpLine(depth, KindNames.TryGetValue((int)node.Kind, out var name) ? name : "undefined", node.Pos, node.End,
                FlagNames(node.Kind, (int)node.Flags, jsDoc is { Count: > 0 }), jsdoc));
            if (jsDoc is not null)
            {
                foreach (var comment in jsDoc)
                {
                    Visit(comment, depth + 1, true);
                }
            }
            NodeVisitor.ForEachChild<object>(node, child =>
            {
                Visit(child, depth + 1, jsdoc);
                return null;
            });
        }
        Visit(sourceFile, 0, false);
        var diag = new List<DumpDiagnostic>();
        foreach (var d in sourceFile.ParseDiagnostics)
        {
            diag.Add(new DumpDiagnostic(d.Code, d.Start, d.Length));
        }
        var jsdocdiag = new List<DumpDiagnostic>();
        foreach (var d in sourceFile.JsDocDiagnostics ?? [])
        {
            jsdocdiag.Add(new DumpDiagnostic(d.Code, d.Start, d.Length));
        }
        return Render(lines, diag, jsdocdiag);
    }
#endif

    /// <summary>Re-renders every tsc dump from its parsed lines; all must come out byte-identical.</summary>
    public static int Replay(Options options)
    {
        var corpus = options.Corpus;
        var kindValues = new Dictionary<string, int>();
        foreach (var (value, name) in EnumNames.SyntaxKind)
        {
            kindValues[name] = value;
        }
        var bitValues = new Dictionary<string, int>();
        foreach (var (bit, name) in FlagBits)
        {
            bitValues[name] = bit;
        }
        bitValues["IdentifierHasExtendedUnicodeEscape"] = (int)NodeFlags.ContainsThis;
        bitValues["IdentifierIsInJSDocNamespace"] = (int)NodeFlags.HasAsyncFunctions;
        var units = Common.ReadUnits(corpus);
        var identical = 0;
        var differing = new List<string>();
        Parallel.ForEach(units, unit =>
        {
            var expected = File.ReadAllText(Path.Combine(corpus, "dumps", "ts", unit.Id + ".txt"), Common.Utf8);
            var lines = new List<DumpLine>();
            var diag = new List<DumpDiagnostic>();
            var jsdocdiag = new List<DumpDiagnostic>();
            foreach (var line in expected.Split('\n'))
            {
                if (line.Length == 0)
                {
                    continue;
                }
                if (line[0] == '!')
                {
                    var p = line.Split(' ');
                    var d = new DumpDiagnostic(int.Parse(p[1], CultureInfo.InvariantCulture), int.Parse(p[2], CultureInfo.InvariantCulture), int.Parse(p[3], CultureInfo.InvariantCulture));
                    (p[0] == "!diag" ? diag : p[0] == "!jsdocdiag" ? jsdocdiag : throw new InvalidOperationException(line)).Add(d);
                    continue;
                }
                var indent = 0;
                while (line[indent] == ' ')
                {
                    indent++;
                }
                var rest = line[indent..];
                var jsdoc = rest[0] == '@';
                var parts = (jsdoc ? rest[1..] : rest).Split(' ');
                var kind = (SyntaxKind)kindValues[parts[0]];
                var flags = 0;
                var hasJsDoc = false;
                if (parts.Length > 3)
                {
                    foreach (var f in parts[3].Split('|'))
                    {
                        if (f == "=HasJSDoc")
                        {
                            hasJsDoc = true;
                        }
                        else
                        {
                            flags |= bitValues[f];
                        }
                    }
                }
                lines.Add(new DumpLine(indent / 2, parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[2], CultureInfo.InvariantCulture),
                    FlagNames(kind, flags, hasJsDoc), jsdoc));
            }
            if (Render(lines, diag, jsdocdiag) == expected)
            {
                Interlocked.Increment(ref identical);
            }
            else
            {
                lock (differing)
                {
                    differing.Add(unit.Id);
                }
            }
        });
        differing.Sort(string.CompareOrdinal);
        Console.WriteLine($"ast-replay: {identical} of {units.Count} identical; differing: {string.Join(' ', differing.GetRange(0, Math.Min(20, differing.Count)))}");
        return differing.Count == 0 ? 0 : 1;
    }
}
