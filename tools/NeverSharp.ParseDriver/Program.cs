// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Parse-only driver for the C# tracks, following the driver contract
// (docs/benchmark-harness.md §4.1):
//   parse-cs-<track>-<runtime> [--single] <file list>
// - NEVERSHARP_PRIORITY=high: raise this process to the high priority class first.
// - Read every file with the semantics of ts.sys.readFile (sys.ts): UTF-16 BE/LE and
//   UTF-8 byte order marks detected, UTF-8 otherwise.
// - Parallel by default: one work item per file (read + parse) on the thread pool,
//   the counterpart of typescript-go's core.WorkGroup (internal/core/workgroup.go);
//   --single: sequential.
// - Parse each file as tsc's compiler host does (harness/bench/parse-ts.mjs): file name with
//   forward slashes, script kind from the extension, languageVersion Latest,
//   JSDocParsingMode.ParseForTypeErrors (executeCommandLine.ts), setParentNodes false.
// - Keep every result alive until the end; print one JSON line.
// - --bind (Track O only; Phase 2, pre-registration §15): parse+bind. Each work item parses the file as above and then
//   binds it with Binder.BindSourceFile(sourceFile, new CompilerOptions()) (binder.ts bindSourceFile with options {},
//   as the Gate B oracle binds); the binder output stays alive with its result. The driver name carries "-bind" and
//   the JSON line adds "symbols" (sum of the files' symbolCount).
// - --check (Track O only; Phase 3, pre-registration §17, module C10): parse+bind+check of the file list as one program.
//   The work items parse every file as above (languageVersion: the program's emit target, as tsc's host parses it); one
//   Program is built from them with TypeScript 6.0.3's default options plus noResolve (Program.Create: the default
//   library from the lib folder next to the executable, module names resolved between the program's files); then
//   typescript-go's compiler checker pool (CheckerPool: Program.BindSourceFiles, 4 checkers capped by the file count,
//   file i to checker i % 4, each checker on its own thread) computes the semantic diagnostics of every program file
//   and the global diagnostics. --single: everything sequential, one checker. The driver name carries "-check" and the
//   JSON line adds the diagnostic counts ("diagnostics" and per category). --dump-diagnostics <file> writes the
//   canonical diagnostics for harness/bench/check-equivalence.mjs (never in timed runs). --checkers N (pre-registration
//   §18): N checkers instead of 4, file i to checker i % N, as typescript-go's CompilerOptions.Checkers; the driver
//   name ends in "-nN".
// - --check --emit (Track O only; Phase 4, draft pre-registration §19, module D7): the check above with Gate D's options
//   (TypeScript 6.0.3's defaults with noResolve, plus declaration: true and newLine: lf), then every program file emitted
//   to memory (.js and .d.ts outputs kept in memory, never written to disk) through the same checkers (CheckerPool.Emit:
//   each file with the emit resolver of its own checker, one work item per checker). The driver name carries "-emit"
//   instead of "-check" and the JSON line adds "outputFiles", "outputBytes" (UTF-8), "emitDiagnostics" and
//   "emitSkipped". --dump-emit <file> writes the outputs and emit diagnostics canonically (JSON lines) for
//   harness/bench/emit-equivalence.mjs (never in timed runs).
//   Usage: parse-cs-optimized-<runtime> [--bind | --check [--emit [--dump-emit <file>]] [--checkers N] [--dump-diagnostics <file>]] [--single] <file list>

using System.Diagnostics;
using System.Globalization;
using System.Text;
#if TRACK_OPTIMIZED
using NeverSharp.Optimized;
#else
using NeverSharp.Faithful;
#endif
using NeverSharp.Generated;

var single = false;
var bind = false;
var check = false;
var emit = false;
string? dumpDiagnostics = null;
string? dumpEmit = null;
var checkerCount = 0;
var argIndex = 0;
for (; argIndex < args.Length && args[argIndex].StartsWith("--", StringComparison.Ordinal); argIndex++)
{
    switch (args[argIndex])
    {
        case "--single":
            single = true;
            break;
        case "--bind":
            bind = true;
            break;
        case "--check":
            check = true;
            break;
        case "--emit":
            emit = true;
            break;
        case "--dump-diagnostics":
            dumpDiagnostics = args[++argIndex];
            break;
        case "--dump-emit":
            dumpEmit = args[++argIndex];
            break;
        case "--checkers":
            if (argIndex + 1 >= args.Length || !int.TryParse(args[++argIndex], NumberStyles.None, CultureInfo.InvariantCulture, out checkerCount) || checkerCount < 1)
            {
                Console.Error.WriteLine("--checkers needs a positive count");
                return 2;
            }
            break;
        default:
            Console.Error.WriteLine($"unknown option {args[argIndex]}");
            return 2;
    }
}
#if !TRACK_OPTIMIZED
if (bind || check)
{
    Console.Error.WriteLine("--bind/--check: Track F has no binder or checker (pre-registration §15, §17)");
    return 2;
}
#endif
if ((bind && check) || (dumpDiagnostics is not null && !check))
{
    Console.Error.WriteLine("--bind and --check exclude each other; --dump-diagnostics needs --check");
    return 2;
}
if (checkerCount > 0 && (!check || single))
{
    Console.Error.WriteLine("--checkers needs --check and excludes --single");
    return 2;
}
if ((emit && !check) || (dumpEmit is not null && !emit))
{
    Console.Error.WriteLine("--emit needs --check; --dump-emit needs --emit");
    return 2;
}
var listPath = args[argIndex];

var priority = "normal";
if (Environment.GetEnvironmentVariable("NEVERSHARP_PRIORITY") == "high")
{
    using var self = Process.GetCurrentProcess();
    self.PriorityClass = ProcessPriorityClass.High;
}
using (var self = Process.GetCurrentProcess())
{
    priority = self.PriorityClass switch
    {
        ProcessPriorityClass.High => "high",
        ProcessPriorityClass.Normal => "normal",
        _ => "other",
    };
}

var files = File.ReadAllLines(listPath).Where(l => l.Length > 0).ToArray();
var texts = new string[files.Length];
var sourceFiles = new SourceFile[files.Length];
// NEVERSHARP_JSDOC=all (development probe only, never set by bench.mjs): JSDocParsingMode.ParseAll, every JSDoc
// comment parsed as typescript-go 7.0.2 does eagerly.
var parseAllJsDoc = Environment.GetEnvironmentVariable("NEVERSHARP_JSDOC") == "all";
var options = new CreateSourceFileOptions { LanguageVersion = ScriptTarget.Latest, JsDocParsingMode = parseAllJsDoc ? JSDocParsingMode.ParseAll : JSDocParsingMode.ParseForTypeErrors };

#if TRACK_OPTIMIZED
if (check)
{
    return CheckMode.Run(files, single, checkerCount, priority, dumpDiagnostics, emit, dumpEmit);
}
#endif

#if TRACK_OPTIMIZED
// NEVERSHARP_PROBE=read (development probe only, never set by bench.mjs): read and decode every file as ParseFile
// does, without parsing; the I/O and decoding floor of the measured work.
var readOnly = Environment.GetEnvironmentVariable("NEVERSHARP_PROBE") == "read";
long readChars = 0;
#endif

void ParseOne(int i)
{
#if TRACK_OPTIMIZED
    if (readOnly)
    {
        var text = SourceText.ReadNative(files[i]);
        Interlocked.Add(ref readChars, text.Length);
        text.Release();
        return;
    }
    // Track O reads files itself (step O1: SourceText.Read, sys.ts semantics, checked by tools/NeverSharp.ReaderCheck).
    sourceFiles[i] = Parser.ParseFile(files[i], files[i].Replace('\\', '/'), options, setParentNodes: false, KindOf(files[i]));
    if (bind)
    {
        // Same work item: bind right after the parse (typescript-go: binder.BindSourceFile per file on its work group).
        Binder.BindSourceFile(sourceFiles[i], new CompilerOptions());
    }
#else
    texts[i] = ReadFile(files[i]);
    sourceFiles[i] = Parser.CreateSourceFile(files[i].Replace('\\', '/'), texts[i], options, setParentNodes: false, KindOf(files[i]));
#endif
}

if (single)
{
    for (var i = 0; i < files.Length; i++)
    {
        ParseOne(i);
    }
}
else
{
    var tasks = new Task[files.Length];
    for (var i = 0; i < files.Length; i++)
    {
        var index = i;
        tasks[i] = Task.Run(() => ParseOne(index));
    }
    Task.WaitAll(tasks);
}

#if TRACK_OPTIMIZED
if (readOnly)
{
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{{\"driver\":\"cs-optimized-read-probe-{(single ? "single" : "parallel")}\",\"files\":{files.Length},\"chars\":{readChars},\"nodes\":0,\"priority\":\"{priority}\"}}"));
    return 0;
}
#endif

long chars = 0, nodes = 0, symbols = 0;
for (var i = 0; i < files.Length; i++)
{
#if TRACK_OPTIMIZED
    chars += sourceFiles[i].TextLength;
    if (bind)
    {
        symbols += (sourceFiles[i].Binding ?? throw new InvalidOperationException($"{files[i]}: not bound")).SymbolCount;
    }
#else
    chars += texts[i].Length;
#endif
    nodes += sourceFiles[i].NodeCount;
}

#if TRACK_OPTIMIZED
const string track = "optimized";
#else
const string track = "faithful";
#endif
var runtime = $"{(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "jit" : "aot")}-{(System.Runtime.GCSettings.IsServerGC ? "srv" : "ws")}";
var mode = single ? "single" : "parallel";
var inv = CultureInfo.InvariantCulture;
var jsDocField = parseAllJsDoc ? "\"jsdoc\":\"all\"," : "";
var bindName = bind ? "bind-" : "";
var symbolsField = bind ? string.Create(inv, $"\"symbols\":{symbols},") : "";
Console.WriteLine(string.Create(inv,
    $"{{\"driver\":\"cs-{track}-{runtime}-{bindName}{mode}\",\"files\":{files.Length},\"chars\":{chars},\"nodes\":{nodes},{symbolsField}\"priority\":\"{priority}\",{jsDocField}" +
    $"\"allocatedBytes\":{GC.GetTotalAllocatedBytes(true)},\"gen0\":{GC.CollectionCount(0)},\"gen1\":{GC.CollectionCount(1)},\"gen2\":{GC.CollectionCount(2)}," +
    $"\"gcPauseMs\":{GC.GetTotalPauseDuration().TotalMilliseconds.ToString("0.###", inv)}}}"));
GC.KeepAlive(texts);
GC.KeepAlive(sourceFiles);
return 0;

// parse-ts.mjs: the same mapping as typescript-go's core.GetScriptKindFromFileName.
static ScriptKind KindOf(string name) => Path.GetExtension(name).ToLowerInvariant() switch
{
    ".ts" or ".mts" or ".cts" => ScriptKind.TS,
    ".tsx" => ScriptKind.TSX,
    ".js" or ".mjs" or ".cjs" => ScriptKind.JS,
    ".jsx" => ScriptKind.JSX,
    ".json" => ScriptKind.JSON,
    _ => ScriptKind.Unknown,
};

// sys.ts readFile: byte order marks decide the encoding, UTF-8 otherwise (Track F; Track O reads through SourceText).
#if !TRACK_OPTIMIZED
static string ReadFile(string path)
{
    var bytes = File.ReadAllBytes(path);
    if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
    {
        return Encoding.BigEndianUnicode.GetString(bytes, 2, (bytes.Length - 2) & ~1);
    }
    if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
    {
        return Encoding.Unicode.GetString(bytes, 2, (bytes.Length - 2) & ~1);
    }
    if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
    {
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }
    return Encoding.UTF8.GetString(bytes);
}
#endif
