// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): ranking workload of modules M3 + M4 (docs/modules/M34.md).
// Parses every file of the tuning corpus with Parser.CreateSourceFile, as the parse-only driver's
// host does (harness/bench/parse-ts.mjs: languageVersion Latest, setParentNodes false, script kind
// from the extension, normalized file name), single-threaded, keeping every SourceFile alive until
// the end of the iteration. JSDocParsingMode.ParseNone: the JSDoc parser is module M5.

using BenchmarkDotNet.Attributes;
#if TRACK_OPTIMIZED
using NeverSharp.Optimized;
#else
using NeverSharp.Faithful;
#endif
using NeverSharp.Generated;

namespace NeverSharp.Benchmarks;

public class M34
{
    private (string FileName, string Text, ScriptKind Kind)[] files = [];
    private CreateSourceFileOptions options = null!;

    /// <summary>ParseNone for M3 + M4; M5 measures with JSDoc parsing as tsc's host does it.</summary>
    protected virtual JSDocParsingMode Mode => JSDocParsingMode.ParseNone;

    [GlobalSetup]
    public void Setup()
    {
        var list = Environment.GetEnvironmentVariable("NEVERSHARP_FILELIST") ?? Path.Combine(M1.FindRoot(), "artifacts", "bench", "filelists", "tuning.txt");
        options = new CreateSourceFileOptions { LanguageVersion = ScriptTarget.Latest, JsDocParsingMode = Mode };
        var names = File.ReadAllLines(list);
        files = new (string, string, ScriptKind)[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            files[i] = (name.Replace('\\', '/'), TextFile.Read(name), KindOf(name));
        }
    }

    // parse-ts.mjs: the same mapping as typescript-go's core.GetScriptKindFromFileName.
    private static ScriptKind KindOf(string name)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return extension switch
        {
            ".ts" or ".mts" or ".cts" => ScriptKind.TS,
            ".tsx" => ScriptKind.TSX,
            ".js" or ".mjs" or ".cjs" => ScriptKind.JS,
            ".jsx" => ScriptKind.JSX,
            ".json" => ScriptKind.JSON,
            _ => ScriptKind.Unknown,
        };
    }

    // The parser recurses as deeply as the source nests; one thread with a large stack per iteration
    // (its start is negligible against an iteration of the whole corpus).
    private const int StackSize = 256 * 1024 * 1024;

    [Benchmark]
    public long ParseTuningCorpus()
    {
        long nodes = 0;
        var thread = new Thread(() =>
        {
            var sourceFiles = new SourceFile[files.Length];
            for (var i = 0; i < files.Length; i++)
            {
                var (fileName, text, kind) = files[i];
                sourceFiles[i] = Parser.CreateSourceFile(fileName, text, options, setParentNodes: false, kind);
                nodes += sourceFiles[i].NodeCount;
            }
            GC.KeepAlive(sourceFiles);
        }, StackSize);
        thread.Start();
        thread.Join();
        return nodes;
    }
}

/// <summary>
/// Ranking workload of M5 (docs/modules/M5.md): the M34 workload with JSDocParsingMode.ParseForTypeErrors,
/// the mode tsc's command line sets (executeCommandLine.ts) and the parse-only drivers use.
/// </summary>
public class M5 : M34
{
    protected override JSDocParsingMode Mode => JSDocParsingMode.ParseForTypeErrors;
}
