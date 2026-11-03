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

internal static class CheckMode
{
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
    internal static int Run(string[] files, bool single, int checkerCount, string priority, string? dumpDiagnostics, bool emit, string? dumpEmit)
    {
        int exitCode = 0;
        if (single)
        {
            // Everything sequential on one thread whose stack is the checker threads' (Go stacks grow on demand).
            var thread = new Thread(() => exitCode = RunCore(files, single, checkerCount, priority, dumpDiagnostics, emit, dumpEmit), CheckerPool.StackSize);
            thread.Start();
            thread.Join();
            return exitCode;
        }
        return RunCore(files, single, checkerCount, priority, dumpDiagnostics, emit, dumpEmit);
    }

    private static int RunCore(string[] files, bool single, int checkerCount, string priority, string? dumpDiagnostics, bool emit, string? dumpEmit)
    {
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
            var program = TsProgram.Create(names, programOptions, host);
            var pool = checkerCount > 0 ? new CheckerPool(program, single, checkerCount) : new CheckerPool(program, single);
            semantic = pool.GetSemanticDiagnostics();
            global = pool.GetGlobalDiagnostics();
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
        var inv = CultureInfo.InvariantCulture;
        var emitFields = emit
            ? string.Create(inv, $"\"outputFiles\":{outputFiles},\"outputBytes\":{outputBytes},\"emitDiagnostics\":{emitResult?.Diagnostics.Count ?? 0},\"emitSkipped\":{(emitResult?.EmitSkipped == true ? "true" : "false")},")
            : "";
        Console.WriteLine(string.Create(inv,
            $"{{\"driver\":\"cs-optimized-{runtime}-{(emit ? "emit" : "check")}-{mode}\",\"files\":{files.Length},\"chars\":{chars},\"nodes\":{nodes},\"programFiles\":{programFiles},\"checkers\":{checkers}," +
            $"\"diagnostics\":{total},\"errors\":{errors},\"warnings\":{warnings},\"suggestions\":{suggestions},\"messages\":{messages},{emitFields}\"priority\":\"{priority}\"," +
            $"\"allocatedBytes\":{GC.GetTotalAllocatedBytes(true)},\"gen0\":{GC.CollectionCount(0)},\"gen1\":{GC.CollectionCount(1)},\"gen2\":{GC.CollectionCount(2)}," +
            $"\"gcPauseMs\":{GC.GetTotalPauseDuration().TotalMilliseconds.ToString("0.###", inv)}}}"));
        GC.KeepAlive(sourceFiles);
        GC.KeepAlive(outputs);
        return 0;
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
