// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// --check mode of the parse driver (Track O; Phase 3, pre-registration §17, module C10): parse+bind+check of the file
// list as one program, the counterpart of harness/bench/go/parse.go --check (typescript-go's NewProgram,
// GetSemanticDiagnostics(ctx, nil) and GetGlobalDiagnostics on its compiler checker pool). With --emit (Phase 4, draft
// pre-registration §19, module D7) the program is then emitted to memory, as parse.go --check --emit runs
// typescript-go's Program.Emit. See Program.cs for the contract.

#if TRACK_OPTIMIZED
using System.Globalization;
using System.Text;
using NeverSharp.Generated;
using NeverSharp.Optimized;
using TsProgram = NeverSharp.Optimized.Program;

/// <summary>The driver's pool switches of module SC1 (--assignment, --steal, --pool tsrs).</summary>
internal sealed class PoolSettings
{
    public CheckerAssignmentKind Assignment = CheckerAssignmentKind.Modulo;
    public bool Steal;
    public bool Tsrs;
    public bool Custom;
}

/// <summary>
/// NEVERSHARP_PHASES=1 (module FE1, development probe, never set by bench.mjs): the wall-time phases of a --check run
/// from the driver's start to its last output, one JSON line on stderr after the result line. Each mark records the
/// Stopwatch time and the process's GC pause so far; the line also carries the Unix times (ms) of the driver's start and
/// of its last output, so that a caller can measure process start and exit from outside.
/// </summary>
internal static class Phases
{
    internal static readonly bool On = Environment.GetEnvironmentVariable("NEVERSHARP_PHASES") == "1";
    private static readonly List<(string Name, long Ticks, double PauseMs, long Allocated, int Gen0, int Gen1, int Gen2)> Marks = [];
    private static double mainUnixMs;

    private static double UnixMs() => (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;

    internal static void Begin()
    {
        if (On)
        {
            mainUnixMs = UnixMs();
            Mark("start");
        }
    }

    internal static void Mark(string name)
    {
        if (On)
        {
            Marks.Add((name, System.Diagnostics.Stopwatch.GetTimestamp(), GC.GetTotalPauseDuration().TotalMilliseconds, GC.GetTotalAllocatedBytes(false),
                GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)));
        }
    }

    /// <summary>The phase line: each mark's seconds since the previous one and the GC pause in between, then <paramref name="extra"/> (JSON members).</summary>
    internal static void Write(string extra)
    {
        if (!On)
        {
            return;
        }
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("{\"phases\":[");
        for (var i = 1; i < Marks.Count; i++)
        {
            var (m, p) = (Marks[i], Marks[i - 1]);
            sb.Append(inv, $"{(i > 1 ? "," : "")}{{\"name\":\"{m.Name}\",\"s\":{(m.Ticks - p.Ticks) / (double)System.Diagnostics.Stopwatch.Frequency:0.0000},\"pauseS\":{(m.PauseMs - p.PauseMs) / 1000:0.0000}," +
                $"\"allocMB\":{(m.Allocated - p.Allocated) >> 20},\"gcs\":\"{m.Gen0 - p.Gen0}/{m.Gen1 - p.Gen1}/{m.Gen2 - p.Gen2}\"}}");
        }
        using (var self = System.Diagnostics.Process.GetCurrentProcess())
        {
            sb.Append(inv, $"],{extra}\"privateMB\":{self.PrivateMemorySize64 >> 20},\"workingSetMB\":{self.WorkingSet64 >> 20},\"peakWorkingSetMB\":{self.PeakWorkingSet64 >> 20},\"threads\":{self.Threads.Count},");
        }
        sb.Append(inv, $"\"mainUnixMs\":{mainUnixMs:0.000},\"endUnixMs\":");
        var text = sb.ToString();
        Console.Error.WriteLine(text + UnixMs().ToString("0.000", inv) + "}");
    }
}

internal static class CheckMode
{
    /// <summary>
    /// --pool tsrs: the checker count without --checkers N (module SC1): tsrs's default on this machine (16) with Server
    /// GC; 12 with Workstation GC, whose pause grows with the checker count so that 16 checkers are slower than 12
    /// (SC1's checker-count curve on vscode and sentry).
    /// </summary>
    internal static int TsrsPoolCheckers => System.Runtime.GCSettings.IsServerGC ? 16 : 12;

    /// <summary>
    /// The options of the checked program: TypeScript 6.0.3's defaults with noResolve (Gate C's options; the table of the
    /// computed values the Go driver passes explicitly is in docs/benchmark-harness.md §4.1).
    /// </summary>
    private static CompilerOptions ProgramOptions() => new() { NoResolve = true };

    /// <summary>
    /// The options of the emitted program (--emit, draft pre-registration §19): Gate D's options, Gate C's plus
    /// declaration: true and newLine: lf (the Go and tsrs drivers pass the same values explicitly).
    /// </summary>
    private static CompilerOptions EmitProgramOptions() => new() { NoResolve = true, Declaration = true, NewLine = NewLineKind.LineFeed };

    /// <param name="checkerCount">--checkers N (pre-registration §18), 0: the pool's default of 4.</param>
    /// <param name="pool">--assignment, --steal, --pool tsrs (module SC1).</param>
    internal static int Run(string[] files, bool single, int checkerCount, PoolSettings pool, string priority, string? dumpDiagnostics, bool emit, string? dumpEmit)
    {
        if (pool.Tsrs)
        {
            pool.Assignment = CheckerAssignmentKind.Locality;
            pool.Steal = true;
            if (checkerCount == 0)
            {
                checkerCount = TsrsPoolCheckers;
            }
        }
        int exitCode = 0;
        if (single)
        {
            // Everything sequential on one thread whose stack is the checker threads' (Go stacks grow on demand).
            var thread = new Thread(() => exitCode = RunCore(files, single, checkerCount, pool, priority, dumpDiagnostics, emit, dumpEmit), CheckerPool.StackSize);
            thread.Start();
            thread.Join();
            return exitCode;
        }
        return RunCore(files, single, checkerCount, pool, priority, dumpDiagnostics, emit, dumpEmit);
    }

    private static int RunCore(string[] files, bool single, int checkerCount, PoolSettings poolSettings, string priority, string? dumpDiagnostics, bool emit, string? dumpEmit)
    {
        Phases.Mark("list");
        var programOptions = emit ? EmitProgramOptions() : ProgramOptions();
        // tsc's host parses with the program's createSourceFile options: languageVersion = the emit target, jsDocParsingMode
        // = the host's (executeCommandLine.ts: ParseForTypeErrors).
        var parseOptions = new CreateSourceFileOptions { LanguageVersion = programOptions.GetEmitScriptTarget(), JsDocParsingMode = JSDocParsingMode.ParseForTypeErrors };
        var sourceFiles = new SourceFile[files.Length];
        var names = new string[files.Length];

        void ParseOne(int i)
        {
            names[i] = files[i].Replace('\\', '/');
            var file = Parser.ParseFile(files[i], names[i], parseOptions, setParentNodes: false, DiskCompilerHost.ScriptKindFromExtension(files[i]));
            // The line map is computed lazily on first use; checkers on several threads read it, so it is computed here
            // (as DefaultLibrary does for the shared library files).
            Scanner.GetLineStarts(new SourceFileNodeLike(file.Store, file.Root));
            sourceFiles[i] = file;
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
        Phases.Mark("parse");

        // The default library next to the executable (build-cs.mjs copies TypeScript 6.0.3's lib folder there), named by
        // its real path, as tsc's host names it.
        var libDirectory = Path.Combine(AppContext.BaseDirectory, "lib");
        var libFolder = TsPath.NormalizeSlashes(Path.GetFullPath(libDirectory)).TrimEnd('/');
        var library = DefaultLibrary.Get(libDirectory, libFolder, programOptions, JSDocParsingMode.ParseForTypeErrors);
        var currentDirectory = TsPath.NormalizeSlashes(Environment.CurrentDirectory);
        var host = new DiskCompilerHost(sourceFiles, library, libFolder, currentDirectory, JSDocParsingMode.ParseForTypeErrors);

        long nodes = 0, chars = 0;
        foreach (var file in sourceFiles)
        {
            nodes += file.NodeCount;
            chars += file.TextLength;
        }

        List<ProgramDiagnostic>[] semantic;
        List<ProgramDiagnostic> global;
        int programFiles, checkers;
        // --emit: the outputs of each program file (name, text), in write order, kept in memory (an in-memory WriteFile)
        List<(string FileName, string Text)>?[] outputs = [];
        EmitResult? emitResult = null;
        var phaseExtra = "";
        if (files.Length == 0)
        {
            // createProgram with no root files: no default library, no checker work, nothing to emit.
            semantic = [];
            global = [];
            programFiles = 0;
            checkers = 0;
        }
        else
        {
            Phases.Mark("host");
            var program = TsProgram.Create(names, programOptions, host);
            Phases.Mark("program");
            if (Phases.On)
            {
                // (the probe only: the pool's constructor resolves and binds the same way; its calls then do nothing)
                program.ResolveImportedModules(single);
                Phases.Mark("resolve");
                program.BindSourceFiles(single);
                Phases.Mark("bind");
            }
            var pool = !poolSettings.Custom
                ? (checkerCount > 0 ? new CheckerPool(program, single, checkerCount) : new CheckerPool(program, single))
                : new CheckerPool(program, single, checkerCount > 0 ? checkerCount : 4, poolSettings.Assignment, poolSettings.Steal);
            Phases.Mark("pool");
            if (Environment.GetEnvironmentVariable("NEVERSHARP_POOL_SLOW") is { } slow)
            {
                pool.SlowFileSeconds = double.Parse(slow, CultureInfo.InvariantCulture);
            }
            semantic = pool.GetSemanticDiagnostics();
            Phases.Mark("check");
            global = pool.GetGlobalDiagnostics();
            Phases.Mark("global");
            if (Phases.On)
            {
                phaseExtra = PhaseExtra(pool);
            }
            if (Environment.GetEnvironmentVariable("NEVERSHARP_POOL_STATS") == "1")
            {
                WritePoolStats(pool);
            }
            for (var c = 0; c < pool.SlowFiles.Length; c++)
            {
                foreach (var (file, order, seconds, types) in pool.SlowFiles[c])
                {
                    Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{{\"slowFile\":{Json(program.SourceFiles[file].Store.FileName(program.SourceFiles[file].Root))},\"checker\":{c},\"order\":{order},\"seconds\":{seconds:0.000},\"types\":{types}}}"));
                }
            }
            programFiles = program.SourceFiles.Count;
            checkers = pool.Count;
            if (emit)
            {
                var perFile = new List<(string FileName, string Text)>?[programFiles];
                string? lastOutput = null;
                try
                {
                    // Each slot is written only by the thread of its file's checker.
                    emitResult = pool.Emit((index, fileName, text) =>
                    {
                        (perFile[index] ??= []).Add((fileName, text));
                        lastOutput = fileName;
                    });
                }
                catch (Exception) when (single)
                {
                    // (single-threaded: the failing file is the one emitted after the last output, or that output's file)
                    Console.Error.WriteLine($"emit failed; last output written: {lastOutput ?? "none"}");
                    throw;
                }
                outputs = perFile;
            }
            if (dumpDiagnostics is not null)
            {
                WriteDiagnostics(dumpDiagnostics, program, semantic, global, libFolder);
            }
            if (dumpEmit is not null)
            {
                WriteEmit(dumpEmit, outputs, emitResult!, libFolder);
            }
            GC.KeepAlive(program);
        }
        Phases.Mark("emit+dumps");

        long outputFiles = 0, outputBytes = 0;
        foreach (var list in outputs)
        {
            foreach (var (_, text) in list ?? [])
            {
                outputFiles++;
                outputBytes += Encoding.UTF8.GetByteCount(text);
            }
        }

        long total = 0, errors = 0, warnings = 0, suggestions = 0, messages = 0;
        foreach (var list in semantic.Append(global))
        {
            foreach (var d in list ?? [])
            {
                total++;
                switch (d.Category)
                {
                    case DiagnosticCategory.Error:
                        errors++;
                        break;
                    case DiagnosticCategory.Warning:
                        warnings++;
                        break;
                    case DiagnosticCategory.Suggestion:
                        suggestions++;
                        break;
                    default:
                        messages++;
                        break;
                }
            }
        }

        var runtime = $"{(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "jit" : "aot")}-{(System.Runtime.GCSettings.IsServerGC ? "srv" : "ws")}";
        var mode = single ? "single" : checkerCount > 0 ? $"parallel-n{checkerCount.ToString(CultureInfo.InvariantCulture)}" : "parallel";
        if (poolSettings.Assignment == CheckerAssignmentKind.Locality)
        {
            mode += "-loc";
        }
        if (poolSettings.Steal)
        {
            mode += "-steal";
        }
        var inv = CultureInfo.InvariantCulture;
        var emitFields = emit
            ? string.Create(inv, $"\"outputFiles\":{outputFiles},\"outputBytes\":{outputBytes},\"emitDiagnostics\":{emitResult?.Diagnostics.Count ?? 0},\"emitSkipped\":{(emitResult?.EmitSkipped == true ? "true" : "false")},")
            : "";
        Console.WriteLine(string.Create(inv,
            $"{{\"driver\":\"cs-optimized-{runtime}-{(emit ? "emit" : "check")}-{mode}\",\"files\":{files.Length},\"chars\":{chars},\"nodes\":{nodes},\"programFiles\":{programFiles},\"checkers\":{checkers}," +
            $"\"diagnostics\":{total},\"errors\":{errors},\"warnings\":{warnings},\"suggestions\":{suggestions},\"messages\":{messages},{emitFields}\"priority\":\"{priority}\"," +
            $"\"allocatedBytes\":{GC.GetTotalAllocatedBytes(true)},\"gen0\":{GC.CollectionCount(0)},\"gen1\":{GC.CollectionCount(1)},\"gen2\":{GC.CollectionCount(2)}," +
            $"\"gcPauseMs\":{GC.GetTotalPauseDuration().TotalMilliseconds.ToString("0.###", inv)}}}"));
        Phases.Mark("output");
        Phases.Write(phaseExtra);
        GC.KeepAlive(sourceFiles);
        GC.KeepAlive(outputs);
        return 0;
    }

    // The phase probe's members of the pool (module FE1): the constructor's phases, the deduplication, the library
    // load, and per checker the maximum and mean of creation, globals merge and duplicate-declaration report.
    private static string PhaseExtra(CheckerPool pool)
    {
        var inv = CultureInfo.InvariantCulture;
        double maxCreate = 0, sumCreate = 0, maxMerge = 0, sumMerge = 0, maxDup = 0, sumDup = 0, maxTotal = 0;
        foreach (var s in pool.Stats)
        {
            maxCreate = Math.Max(maxCreate, s.CreateSeconds);
            sumCreate += s.CreateSeconds;
            maxMerge = Math.Max(maxMerge, s.MergeSeconds);
            sumMerge += s.MergeSeconds;
            maxDup = Math.Max(maxDup, s.DuplicatesSeconds);
            sumDup += s.DuplicatesSeconds;
            maxTotal = Math.Max(maxTotal, s.CreateSeconds + s.CheckSeconds);
        }
        var n = Math.Max(pool.Stats.Length, 1);
        return string.Create(inv,
            $"\"resolve\":{pool.ResolveSeconds:0.0000},\"bind\":{pool.BindSeconds:0.0000},\"assign\":{pool.AssignSeconds:0.0000},\"dedupe\":{pool.DedupeSeconds:0.0000}," +
            $"\"libLoad\":{DefaultLibrary.LoadTicks / (double)System.Diagnostics.Stopwatch.Frequency:0.0000}," +
            $"\"createMax\":{maxCreate:0.0000},\"createMean\":{sumCreate / n:0.0000},\"mergeMax\":{maxMerge:0.0000},\"mergeMean\":{sumMerge / n:0.0000}," +
            $"\"dupMax\":{maxDup:0.0000},\"dupMean\":{sumDup / n:0.0000},\"slowestChecker\":{maxTotal:0.0000},");
    }

    // NEVERSHARP_POOL_STATS=1 (module SC1, development probe, never set by bench.mjs): one JSON line per checker of the
    // check pass on stderr.
    private static void WritePoolStats(CheckerPool pool)
    {
        var inv = CultureInfo.InvariantCulture;
        for (var i = 0; i < pool.Stats.Length; i++)
        {
            var s = pool.Stats[i];
            Console.Error.WriteLine(string.Create(inv,
                $"{{\"checker\":{i},\"createSeconds\":{s.CreateSeconds:0.000},\"mergeSeconds\":{s.MergeSeconds:0.000},\"duplicatesSeconds\":{s.DuplicatesSeconds:0.000},\"checkSeconds\":{s.CheckSeconds:0.000},\"files\":{s.Files},\"stolen\":{s.StolenFiles},\"weight\":{s.Weight},\"types\":{s.Types},\"transientSymbols\":{s.TransientSymbols}}}"));
        }
    }

    /// <summary>
    /// The canonical emit dump (emit-equivalence.mjs writes tsc's the same way), one JSON object per line: every output in
    /// program file order and, per file, in write order, as <c>{"output":name,"text":text}</c>; then the emit diagnostics
    /// as <c>{"diagnostic":line}</c> in the canonical form of <see cref="WriteDiagnostics"/> (tag "emit"); last
    /// <c>{"emitSkipped":bool}</c>.
    /// </summary>
    private static void WriteEmit(string path, List<(string FileName, string Text)>?[] outputs, EmitResult result, string libFolder)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        var sb = new StringBuilder();
        foreach (var list in outputs)
        {
            foreach (var (fileName, text) in list ?? [])
            {
                writer.Write("{\"output\":");
                writer.Write(Json(fileName));
                writer.Write(",\"text\":");
                writer.Write(Json(text));
                writer.Write("}\n");
            }
        }
        foreach (var d in result.Diagnostics)
        {
            sb.Clear();
            Write(sb, "emit", d, libFolder);
            writer.Write("{\"diagnostic\":");
            writer.Write(Json(sb.ToString(0, sb.Length - 1)));
            writer.Write("}\n");
        }
        writer.Write(result.EmitSkipped ? "{\"emitSkipped\":true}\n" : "{\"emitSkipped\":false}\n");
    }

    /// <summary>
    /// The canonical diagnostics (check-equivalence.mjs writes tsc's the same way): one line per diagnostic, semantic
    /// diagnostics in program file order, then the global ones: <c>semantic|global, file (library files by base name, "-"
    /// for none), start, length, code, category, flattened message as a JSON string</c>, tab-separated.
    /// </summary>
    private static void WriteDiagnostics(string path, TsProgram program, List<ProgramDiagnostic>[] semantic, List<ProgramDiagnostic> global, string libFolder)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < semantic.Length; i++)
        {
            foreach (var d in semantic[i] ?? [])
            {
                Write(sb, "semantic", d, libFolder);
            }
        }
        foreach (var d in global)
        {
            Write(sb, "global", d, libFolder);
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        _ = program;
    }

    // One canonical diagnostic line (with its "\n").
    private static void Write(StringBuilder sb, string tag, ProgramDiagnostic d, string libFolder)
    {
        var fileName = d.SourceFile?.Store.FileName(d.SourceFile.Root);
        var fileText = fileName is null ? "-" : fileName.StartsWith(libFolder + "/", StringComparison.Ordinal) ? fileName[(libFolder.Length + 1)..] : fileName;
        sb.Append(tag).Append('\t').Append(fileText).Append('\t')
            .Append(d.StartOrUndefined is { } start ? start.ToString(CultureInfo.InvariantCulture) : "-").Append('\t')
            .Append(d.LengthOrUndefined is { } length ? length.ToString(CultureInfo.InvariantCulture) : "-").Append('\t')
            .Append(d.Code.ToString(CultureInfo.InvariantCulture)).Append('\t')
            .Append(d.Category switch
            {
                DiagnosticCategory.Warning => "Warning",
                DiagnosticCategory.Error => "Error",
                DiagnosticCategory.Suggestion => "Suggestion",
                _ => "Message",
            }).Append('\t')
            .Append(Json(ProgramDiagnostics.FlattenDiagnosticMessageText(d, "\n"))).Append('\n');
    }

    // JSON.stringify of a string (the oracle's escaping).
    private static string Json(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < 0x20 || (char.IsSurrogate(c) && !(char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                        && !(char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(value[i - 1]))))
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
#endif
