// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): printer dump of Track O for the printer gate (Phase 3, module P1).
//
// Reproduces harness/astdump/dump-ts-printer.mjs: each unit is parsed with Parser.CreateSourceFile(fileName, text,
// { languageVersion: Latest, jsDocParsingMode }, setParentNodes: false, scriptKind) and printed with Track O's printer,
// Printer.CreatePrinter({ removeComments: true, newLine: LineFeed }).PrintFile(...); the dump of a unit is the printed
// text, written as UTF-8. Units that do not finish within the timeout are recorded as timeouts, as by the tsc dumper.

using System.Globalization;
using System.Text.Json;
using NeverSharp.Generated;
using NeverSharp.Optimized;

namespace NeverSharp.Dump;

internal static class PrinterDump
{
    private const int StackSize = 256 * 1024 * 1024;

    public static int Run(Options options)
    {
        var corpus = options.Corpus;
        var jsdocMode = options.Get("--jsdoc-mode", "types");
        var mode = jsdocMode switch
        {
            "all" => JSDocParsingMode.ParseAll,
            "none" => JSDocParsingMode.ParseNone,
            "types" => JSDocParsingMode.ParseForTypeErrors,
            _ => throw new InvalidOperationException("--jsdoc-mode must be all, none or types"),
        };
        var dumpName = $"cs-{Track.Name}-printer-{jsdocMode}";
        var outDir = Path.GetFullPath(options.Get("--out", Path.Combine(corpus, "dumps", dumpName)));
        var timeoutMs = int.Parse(options.Get("--timeout-ms", "10000"), CultureInfo.InvariantCulture);
        var only = options.Get("--units", "");
        Common.ResetDirectory(outDir);
        var units = Common.ReadUnits(corpus);
        if (only.Length > 0)
        {
            var wanted = new HashSet<string>(only.Split(','), StringComparer.Ordinal);
            units = units.FindAll(u => wanted.Contains(u.Id));
        }
        var ok = new List<string>();
        var timeout = new List<string>();
        var error = new List<(string Id, string Error)>();
        var started = DateTime.UtcNow;
        Parallel.ForEach(units, unit =>
        {
            var text = Common.ReadUnitText(corpus, unit);
            // As AstDump.Run: each unit on its own thread with a large stack (parser and printer recurse as deeply as
            // the source nests); a unit that never finishes keeps its thread, the process exits at the end regardless.
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
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{dumpName}: ok {ok.Count}, timeout {timeout.Count}, error {error.Count} in {(DateTime.UtcNow - started).TotalSeconds:0.0} s"));
        for (var i = 0; i < Math.Min(5, error.Count); i++)
        {
            Console.WriteLine($"error {error[i].Id}: {error[i].Error}");
        }
        Environment.Exit(0); // threads of timed-out units may still run
        return 0;
    }

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
        var file = Parser.CreateSourceFile(unit.FileName, text,
            new CreateSourceFileOptions { LanguageVersion = ScriptTarget.Latest, JsDocParsingMode = mode },
            setParentNodes: false, scriptKind);
        var printer = Printer.CreatePrinter(new PrinterOptions { RemoveComments = true, NewLine = NewLineKind.LineFeed });
        return printer.PrintFile(file.Store, file.Root);
    }
}
