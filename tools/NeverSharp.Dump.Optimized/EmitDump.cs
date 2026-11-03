// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): emit dump of Track O for Gate D (module D0).
//
// Reproduces harness/astdump/dump-ts-emit.mjs (emit dump format version 1, described there): each unit becomes a
// one-file program with Gate C's options plus declaration: true and newLine: lf (MemoryCompilerHost, the default library
// shared as in CheckerDump.cs), emitted with Program.Emit(unitFile, writeFile) to memory. Written: the outputs in write
// order (name relative to the unit's folder, text split at "\n" with each piece prefixed by "|"), the emit diagnostics
// and emitSkipped.

using System.Globalization;
using System.Text;
using System.Text.Json;
using NeverSharp.Generated;
using NeverSharp.Optimized;
using TsProgram = NeverSharp.Optimized.Program;

namespace NeverSharp.Dump;

internal static class EmitDump
{
    private const int StackSize = 256 * 1024 * 1024;

    // dump-ts-emit.mjs optionsFor, in its key order
    private const string OptionsJson = "{\"noResolve\":true,\"declaration\":true,\"newLine\":1}";
    private const string OptionsJsonJs = "{\"noResolve\":true,\"allowJs\":true,\"declaration\":true,\"newLine\":1}";

    private static bool IsJavaScript(Unit unit) => unit.Kind is "JS" or "JSX";

    private static CompilerOptions GateOptions(Unit unit) => new()
    {
        NoResolve = true,
        AllowJs = IsJavaScript(unit) ? true : null,
        Declaration = true,
        NewLine = NewLineKind.LineFeed,
    };

    public static int Run(Options options)
    {
        var corpus = options.Corpus;
        const JSDocParsingMode mode = JSDocParsingMode.ParseForTypeErrors;
        var dumpName = $"cs-{Track.Name}-emit-types";
        var outDir = Path.GetFullPath(options.Get("--out", Path.Combine(corpus, "dumps", dumpName)));
        var timeoutMs = int.Parse(options.Get("--timeout-ms", "120000"), CultureInfo.InvariantCulture);
        var libDir = Path.GetFullPath(options.Get("--lib-dir", Path.Combine(Common.FindRoot(), "harness", "astdump", "node_modules", "typescript", "lib")));
        Common.ResetDirectory(outDir);
        var units = Common.ReadUnits(corpus);
        var unitsFile = options.Get("--units", "");
        if (unitsFile.Length > 0)
        {
            var ids = new HashSet<string>(File.ReadAllLines(unitsFile), StringComparer.Ordinal);
            units = units.FindAll(u => ids.Contains(u.Id));
        }
        var tsOptions = new CompilerOptions { NoResolve = true, Declaration = true, NewLine = NewLineKind.LineFeed };
        var library = DefaultLibrary.Get(libDir, MemoryCompilerHost.LibFolder, tsOptions, mode);

        var started = DateTime.UtcNow;
        // Before the first unit, as the oracle's warm-up: the library files parsed and bound (not timed per unit).
        var warmUp = TsProgram.Create(["/__empty__.ts"], tsOptions, new MemoryCompilerHost("/__empty__.ts", "", ScriptKind.TS, library, mode));
        warmUp.GetTypeChecker();

        var ok = new List<string>();
        var timeout = new List<string>();
        var error = new List<(string Id, string Error)>();
        Parallel.ForEach(units, unit =>
        {
            var text = Common.ReadUnitText(corpus, unit);
            string? result = null;
            string? outcome = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = DumpUnit(text, unit, mode, library);
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
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{dumpName}: ok {ok.Count}, timeout {timeout.Count}, error {error.Count} in {(DateTime.UtcNow - started).TotalSeconds:0.0} s"));
        for (var i = 0; i < Math.Min(5, error.Count); i++)
        {
            Console.WriteLine($"error {error[i].Id}: {error[i].Error}");
        }
        Environment.Exit(0); // threads of timed-out units may still run
        return 0;
    }

    private static ScriptKind ScriptKindOf(Unit unit) => unit.Kind switch
    {
        "TS" => ScriptKind.TS,
        "TSX" => ScriptKind.TSX,
        "JS" => ScriptKind.JS,
        "JSX" => ScriptKind.JSX,
        "JSON" => ScriptKind.JSON,
        _ => throw new InvalidOperationException(unit.Kind),
    };

    internal static string DumpUnit(string text, Unit unit, JSDocParsingMode mode, DefaultLibrary library)
    {
        var host = new MemoryCompilerHost(unit.FileName, text, ScriptKindOf(unit), library, mode);
        var program = TsProgram.Create([unit.FileName], GateOptions(unit), host);
        var sourceFile = program.GetSourceFile(unit.FileName);
        var outputs = new List<(string FileName, string Text)>();
        // The unit file only (a missing unit file emits nothing, as in the oracle).
        var result = sourceFile is null ? new EmitResult(false, []) : program.Emit(sourceFile, (fileName, outputText, _) => outputs.Add((fileName, outputText)));

        var unitDir = DirName(unit.FileName);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"emit-dump 1\nfile {unit.FileName} {unit.Kind}\n");
        sb.Append("options ").Append(IsJavaScript(unit) ? OptionsJsonJs : OptionsJson).Append('\n');
        sb.Append("== outputs\n");
        foreach (var (fileName, outputText) in outputs)
        {
            if (DirName(fileName) != unitDir)
            {
                throw new InvalidOperationException("An output outside the unit's folder: " + fileName);
            }
            sb.Append("output ").Append(fileName[(fileName.LastIndexOf('/') + 1)..]).Append('\n');
            foreach (var line in outputText.Split('\n'))
            {
                sb.Append('|').Append(line).Append('\n');
            }
        }
        sb.Append("== diagnostics\n");
        CheckerDump.WriteDiagnostics(sb, "emit", result.Diagnostics, unit.FileName);
        sb.Append("== emitSkipped\n").Append(result.EmitSkipped ? "true" : "false").Append('\n');
        return sb.ToString();
    }

    private static string DirName(string fileName)
    {
        var slash = fileName.LastIndexOf('/');
        return slash <= 0 ? "/" : fileName[..slash];
    }
}
