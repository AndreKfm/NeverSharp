// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from internal/compiler/checkerpool.go and internal/compiler/program.go (collectCheckerDiagnosticsFromFiles)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// (TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8 has one checker per program and no counterpart.)
// Modified: translated from Go to C#; module C10 (the check driver). No tracing, no project-system pools, no locks
// (each checker is used by one work item at a time by construction). Ported: newCheckerPoolWithTracing, createCheckers,
// forEachCheckerGroupDo, GetGlobalDiagnostics; program.go GetSemanticDiagnostics with no file, GetGlobalDiagnostics.
// Module CO4: each work item creates its own checker (no wait for the other checkers' construction).

namespace NeverSharp.Optimized;

/// <summary>
/// typescript-go's compiler checker pool: a fixed number of checkers on one program (4 by default, 1 single-threaded,
/// capped by the file count), file i of program.SourceFiles assigned to checker i % count. Each checker checks its own
/// files, in program order, on its own work item; checkers share the program and its files read-only.
/// </summary>
internal sealed class CheckerPool
{
    private readonly Program program;
    private readonly bool singleThreaded;
    private readonly Checker?[] checkers;
    private readonly Dictionary<SourceFile, int> fileAssociations;

    /// <summary>checkerpool.go newCheckerPoolWithTracing + createCheckers (program.Options().Checkers is not in Track O's options).</summary>
    internal CheckerPool(Program program, bool singleThreaded)
    {
        this.program = program;
        this.singleThreaded = singleThreaded;
        var checkerCount = singleThreaded ? 1 : 4;
        checkerCount = Math.Max(Math.Min(Math.Min(checkerCount, program.SourceFiles.Count), 256), 1);
        checkers = new Checker?[checkerCount];

        // checker.NewChecker calls program.BindSourceFiles (parallel unless single-threaded) before anything else.
        program.BindSourceFiles(singleThreaded);

        fileAssociations = new Dictionary<SourceFile, int>(program.SourceFiles.Count, ReferenceEqualityComparer.Instance);
        var files = program.SourceFiles;
        for (var i = 0; i < files.Count; i++)
        {
            fileAssociations[files[i]] = i % checkerCount;
        }
    }

    internal int Count => checkers.Length;

    /// <summary>
    /// The checker of work item <paramref name="index"/>, created on that work item's thread when first needed
    /// (module CO4). typescript-go's createCheckers creates every checker in a work group and waits for the slowest
    /// before any file is checked; here each work item creates its own checker and goes on with its files at once (a
    /// checker's construction reads only the bound program, so it does not depend on the other checkers).
    /// </summary>
    private Checker CheckerAt(int index) => checkers[index] ??= new Checker(program);

    /// <summary>
    /// program.go GetSemanticDiagnostics(ctx, nil): collectCheckerDiagnosticsFromFiles over program.files with
    /// forEachCheckerGroupDo (one work item per checker, each visiting its files in order); the result per file index.
    /// </summary>
    internal List<ProgramDiagnostic>[] GetSemanticDiagnostics()
    {
        var files = program.SourceFiles;
        var diagnostics = new List<ProgramDiagnostic>[files.Count];
        RunAll(checkers.Length, checkerIndex =>
        {
            var checker = CheckerAt(checkerIndex);
            checker.EnterThread();
            for (var i = 0; i < files.Count; i++)
            {
                if (fileAssociations[files[i]] == checkerIndex)
                {
                    diagnostics[i] = program.GetSemanticDiagnostics(files[i], checker);
                }
            }
        });
        if (checkers.Length > 1)
        {
            // GetSemanticDiagnostics(ctx, nil) sorts and deduplicates the concatenation of all files' diagnostics. Each
            // checker adds the global diagnostics it produced to the result of its first file (getDiagnosticsWorker),
            // so every checker reports the same file-less diagnostics once: keep the first occurrence in file order.
            var seen = new List<ProgramDiagnostic>();
            foreach (var list in diagnostics)
            {
                list?.RemoveAll(d =>
                {
                    if (d.SourceFile is not null)
                    {
                        return false;
                    }
                    if (seen.Exists(s => DiagnosticCollection.EqualityComparer(s, d)))
                    {
                        return true;
                    }
                    seen.Add(d);
                    return false;
                });
            }
        }
        return diagnostics;
    }

    /// <summary>checkerpool.go GetGlobalDiagnostics: every checker's global diagnostics, sorted and deduplicated.</summary>
    internal List<ProgramDiagnostic> GetGlobalDiagnostics()
    {
        var perChecker = new List<ProgramDiagnostic>[checkers.Length];
        RunAll(checkers.Length, i =>
        {
            var checker = CheckerAt(i);
            checker.EnterThread();
            perChecker[i] = checker.GetGlobalDiagnostics();
        });
        var all = new List<ProgramDiagnostic>();
        foreach (var list in perChecker)
        {
            all.AddRange(list);
        }
        return DiagnosticCollection.SortAndDeduplicate(all);
    }

    /// <summary>
    /// The stack of each checker thread: Go's goroutine stacks grow on demand (up to 1 GB); a .NET thread's stack is
    /// fixed when it starts, so checker threads reserve as much as the Gate C dump's (committed on demand).
    /// </summary>
    internal const int StackSize = 256 * 1024 * 1024;

    // core.NewWorkGroup(singleThreaded): Queue + RunAndWait. Single-threaded: in order on the calling thread (the driver
    // runs it on a thread with StackSize); otherwise one thread per work item.
    private void RunAll(int count, Action<int> work)
    {
        if (singleThreaded || count == 1)
        {
            for (var i = 0; i < count; i++)
            {
                work(i);
            }
            return;
        }
        // (an exception on a checker thread ends the process, as a panic in a work group ends tsgo)
        var threads = new Thread[count];
        for (var i = 0; i < count; i++)
        {
            var index = i;
            threads[i] = new Thread(() => work(index), StackSize);
            threads[i].Start();
        }
        foreach (var thread in threads)
        {
            thread.Join();
        }
    }
}
