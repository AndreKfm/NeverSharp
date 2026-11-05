// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// Portions Copyright 2026 Max Schwenk and the tsrs contributors
// SPDX-License-Identifier: Apache-2.0
//
// Ported from internal/compiler/checkerpool.go and internal/compiler/program.go (collectCheckerDiagnosticsFromFiles)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// (TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8 has one checker per program and no counterpart.)
// Modified: translated from Go to C#; module C10 (the check driver). No tracing, no project-system pools, no locks
// (each checker is used by one work item at a time by construction). Ported: newCheckerPoolWithTracing, createCheckers,
// forEachCheckerGroupDo, GetGlobalDiagnostics; program.go GetSemanticDiagnostics with no file, GetGlobalDiagnostics.
// Module CO4: each work item creates its own checker (no wait for the other checkers' construction), and the program's
// imports are resolved before binding (in parallel unless single-threaded), and a checker keeps only its own files'
// diagnostics. Module D7: program.go Emit on the pool (Emit), one work item per checker.
// Module SC1 (Track O / tsrs-only options, off by default): tsrs's work distribution, ported from
// crates/tsrs_compiler/src/checkerpool.rs of https://github.com/oxc-project/tsrs at tag v0.18.0
// (5f92808bc92bb97b06212153ab73d28d551c0b9b): the locality assignment (CheckerAssignment.cs) and tail stealing
// (for_each_checker_group_do_inner with allow_steal, heavy_files_first, FileQueue, queues_next,
// max_thieves_per_queue, steal_sticky; notes/perf-checker-stealing.md). Not ported: split checking of declaration
// files, checker retirement under --maxMemory, the shared graph, the TSRS_* overrides.

using System.Diagnostics;

namespace NeverSharp.Optimized;

/// <summary>How a <see cref="CheckerPool"/> assigns the program's files to its checkers (module SC1).</summary>
internal enum CheckerAssignmentKind
{
    /// <summary>typescript-go 7.0.2: file i to checker i % N (the default).</summary>
    Modulo,

    /// <summary>tsrs: directory-subtree groups placed with FENNEL over the import edges (CheckerAssignment.Locality).</summary>
    Locality,
}

/// <summary>
/// typescript-go's compiler checker pool: a fixed number of checkers on one program (4 by default, 1 single-threaded,
/// capped by the file count), file i of program.SourceFiles assigned to checker i % count. Each checker checks its own
/// files, in program order, on its own work item; checkers share the program and its files read-only. Module SC1:
/// optionally tsrs's locality assignment instead of i % count, and tsrs's tail stealing in the check pass.
/// </summary>
internal sealed class CheckerPool
{
    private readonly Program program;
    private readonly bool singleThreaded;
    private readonly bool steal;
    private readonly Checker?[] checkers;
    // The checker of each program file (by program file index): the assignment, then, after a check pass with stealing,
    // the checker that checked the file (later passes run on it, as tsrs's owners).
    private readonly int[] owners;
    // checkerpool.rs checked_file_weights (only with stealing: the queues' estimated work)
    private readonly long[]? weights;

    /// <summary>
    /// checkerpool.go newCheckerPoolWithTracing + createCheckers. program.Options().Checkers is not in Track O's
    /// options; <paramref name="requestedCount"/> stands for it (4 by default; the driver's --checkers N,
    /// pre-registration §18). <paramref name="assignment"/> and <paramref name="steal"/>: module SC1 (driver switches
    /// --assignment locality and --steal).
    /// </summary>
    internal CheckerPool(Program program, bool singleThreaded, int requestedCount = 4, CheckerAssignmentKind assignment = CheckerAssignmentKind.Modulo, bool steal = false)
    {
        this.program = program;
        this.singleThreaded = singleThreaded;
        var checkerCount = singleThreaded ? 1 : requestedCount;
        checkerCount = Math.Max(Math.Min(Math.Min(checkerCount, program.SourceFiles.Count), 256), 1);
        checkers = new Checker?[checkerCount];
        this.steal = steal && checkerCount > 1;

        // The imports of the program's files (module CO4: resolved after the program's walk, parallel unless
        // single-threaded), before the files are bound, as in the walk, and before any checker reads them.
        program.ResolveImportedModules(singleThreaded);
        // checker.NewChecker calls program.BindSourceFiles (parallel unless single-threaded) before anything else.
        program.BindSourceFiles(singleThreaded);

        var files = program.SourceFiles;
        if (assignment == CheckerAssignmentKind.Locality && checkerCount > 1)
        {
            owners = CheckerAssignment.Locality(program, checkerCount);
        }
        else
        {
            owners = new int[files.Count];
            for (var i = 0; i < files.Count; i++)
            {
                owners[i] = i % checkerCount;
            }
        }
        if (this.steal)
        {
            weights = CheckerAssignment.CheckedFileWeights(program);
        }
        Stats = new CheckerStats[checkerCount];
    }

    internal int Count => checkers.Length;

    /// <summary>Per checker: what its work items did (module SC1; the driver prints it with NEVERSHARP_POOL_STATS=1).</summary>
    internal CheckerStats[] Stats { get; }

    /// <summary>The per-checker statistics of the check pass (module SC1).</summary>
    internal struct CheckerStats
    {
        public double CreateSeconds;
        public double CheckSeconds;
        public int Files;
        public int StolenFiles;
        public long Weight;
        public int Types;
        public int TransientSymbols;
    }

    /// <summary>
    /// The checker of work item <paramref name="index"/>, created on that work item's thread when first needed
    /// (module CO4). typescript-go's createCheckers creates every checker in a work group and waits for the slowest
    /// before any file is checked; here each work item creates its own checker and goes on with its files at once (a
    /// checker's construction reads only the bound program, so it does not depend on the other checkers).
    /// </summary>
    private Checker CheckerAt(int index) => checkers[index] ??= new Checker(program, KeptFilesOf(index));

    // The files whose diagnostics checker `index` keeps (module CO4): its own; with stealing every file, since it may
    // take any other checker's not yet started files (module SC1).
    private HashSet<SourceFile>? KeptFilesOf(int index)
    {
        if (checkers.Length == 1 || steal)
        {
            return null;
        }
        var files = program.SourceFiles;
        var kept = new HashSet<SourceFile>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < files.Count; i++)
        {
            if (owners[i] == index)
            {
                kept.Add(files[i]);
            }
        }
        return kept;
    }

    /// <summary>
    /// program.go GetSemanticDiagnostics(ctx, nil): collectCheckerDiagnosticsFromFiles over program.files with
    /// forEachCheckerGroupDo (one work item per checker, each visiting its files in order); the result per file index.
    /// With stealing (module SC1), tsrs's for_each_checker_group_do with allow_steal.
    /// </summary>
    internal List<ProgramDiagnostic>[] GetSemanticDiagnostics()
    {
        var files = program.SourceFiles;
        var diagnostics = new List<ProgramDiagnostic>[files.Count];
        if (SlowFileSeconds > 0)
        {
            SlowFiles = new List<(int, int, double, int)>[checkers.Length];
            for (var c = 0; c < SlowFiles.Length; c++)
            {
                SlowFiles[c] = [];
            }
        }
        if (steal)
        {
            CheckWithStealing(diagnostics);
        }
        else
        {
            RunAll(checkers.Length, checkerIndex =>
            {
                var start = Stopwatch.GetTimestamp();
                var checker = CheckerAt(checkerIndex);
                checker.EnterThread();
                var created = Stopwatch.GetTimestamp();
                for (var i = 0; i < files.Count; i++)
                {
                    if (owners[i] == checkerIndex)
                    {
                        CheckFile(checkerIndex, checker, i, diagnostics);
                    }
                }
                RecordStats(checkerIndex, checker, start, created);
            });
        }
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

    /// <summary>
    /// When positive (module SC1, a development probe set by the driver): the files whose check takes at least this many
    /// seconds are recorded in <see cref="SlowFiles"/>.
    /// </summary>
    internal double SlowFileSeconds { get; set; }

    /// <summary>Per checker: (program file index, check order on the checker, seconds, types created) of the slow files.</summary>
    internal List<(int File, int Order, double Seconds, int Types)>[] SlowFiles { get; private set; } = [];

    private void CheckFile(int checkerIndex, Checker checker, int i, List<ProgramDiagnostic>[] diagnostics)
    {
        if (SlowFileSeconds <= 0)
        {
            diagnostics[i] = program.GetSemanticDiagnostics(program.SourceFiles[i], checker);
        }
        else
        {
            var start = Stopwatch.GetTimestamp();
            var types = checker.Types.Count;
            diagnostics[i] = program.GetSemanticDiagnostics(program.SourceFiles[i], checker);
            var seconds = (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;
            if (seconds >= SlowFileSeconds)
            {
                SlowFiles[checkerIndex].Add((i, Stats[checkerIndex].Files, seconds, checker.Types.Count - types));
            }
        }
        Stats[checkerIndex].Files++;
    }

    private void RecordStats(int checkerIndex, Checker checker, long start, long created)
    {
        var end = Stopwatch.GetTimestamp();
        Stats[checkerIndex].CreateSeconds = (created - start) / (double)Stopwatch.Frequency;
        Stats[checkerIndex].CheckSeconds = (end - created) / (double)Stopwatch.Frequency;
        Stats[checkerIndex].Types = checker.Types.Count;
        Stats[checkerIndex].TransientSymbols = checker.TransientSymbols.Count;
    }

    // tsrs-only (checkerpool.rs HEAVY_SHARE_DIVISOR): a file heavier than 1/200 of an average checker's share is heavy.
    private const long HeavyShareDivisor = 200;

    // affinity.rs MIN_CHECKERS, LARGE_MIN_CHECKERS, LARGE_PROGRAM_FILES.
    private const int AffinityMinCheckers = 16;
    private const int AffinityLargeMinCheckers = 8;
    private const int AffinityLargeProgramFiles = 24_000;

    /// <summary>affinity.rs applies: whether the module affinity and the sticky thieves apply.</summary>
    internal static bool ModuleAffinityApplies(int checkerCount, int files) =>
        checkerCount >= AffinityMinCheckers || (checkerCount >= AffinityLargeMinCheckers && files >= AffinityLargeProgramFiles);

    // checkerpool.rs max_thieves_per_queue: from 8 to 15 checkers, at most a quarter of the checkers steal from any one
    // queue at a time; otherwise no limit (0).
    private static int MaxThievesPerQueue(int checkerCount) =>
        checkerCount is >= 8 and < AffinityMinCheckers ? checkerCount / 4 : 0;

    /// <summary>
    /// checkerpool.rs for_each_checker_group_do_inner with allow_steal (tsrs's dynamic scheduling, notes/perf-checker-
    /// stealing.md): each checker runs the files assigned to it from the front of its queue (its heavy files first,
    /// heavy_files_first); a checker that runs out takes not yet started files from the back of the queue with the most
    /// estimated work left (queues_next). A stolen file's later passes (emit) run on the checker that checked it.
    /// </summary>
    private void CheckWithStealing(List<ProgramDiagnostic>[] diagnostics)
    {
        var files = program.SourceFiles;
        var n = checkers.Length;
        var w = weights!;
        var positions = new List<int>[n];
        for (var c = 0; c < n; c++)
        {
            positions[c] = [];
        }
        for (var i = 0; i < files.Count; i++)
        {
            positions[owners[i]].Add(i);
        }
        ulong Weight(int i) => (ulong)Math.Max(w[i], 0);
        var active = new List<int>();
        for (var c = 0; c < n; c++)
        {
            if (positions[c].Count > 0)
            {
                active.Add(c);
            }
        }
        ulong total = 0;
        foreach (var p in positions)
        {
            foreach (var i in p)
            {
                total += Weight(i);
            }
        }
        var threshold = total / ((ulong)Math.Max(active.Count, 1) * HeavyShareDivisor);
        var queues = new FileQueue[n];
        for (var c = 0; c < n; c++)
        {
            queues[c] = new FileQueue(HeavyFilesFirst(positions[c], threshold, Weight), Weight);
        }
        var sticky = ModuleAffinityApplies(n, files.Count);
        var maxThieves = MaxThievesPerQueue(n);
        RunAll(active.Count, k =>
        {
            var checkerIndex = active[k];
            var start = Stopwatch.GetTimestamp();
            var checker = CheckerAt(checkerIndex);
            checker.EnterThread();
            var created = Stopwatch.GetTimestamp();
            var slot = -1;
            var lastVictim = -1;
            while (QueuesNext(queues, checkerIndex, maxThieves, ref slot, sticky, ref lastVictim, out var fromOther) is var i and >= 0)
            {
                if (fromOther)
                {
                    // Later passes over this file go to the checker that checked it (read after the work group joined).
                    owners[i] = checkerIndex;
                    Stats[checkerIndex].StolenFiles++;
                }
                CheckFile(checkerIndex, checker, i, diagnostics);
                Stats[checkerIndex].Weight += w[i];
            }
            RecordStats(checkerIndex, checker, start, created);
        });
    }

    // checkerpool.rs heavy_files_first: the heavy files (weight above `threshold`) of one queue to its front, heaviest
    // first (ties in queue order); the other files keep their order.
    private static int[] HeavyFilesFirst(List<int> positions, ulong threshold, Func<int, ulong> weight)
    {
        var heavy = new List<int>();
        var light = new List<int>();
        foreach (var i in positions)
        {
            (weight(i) > threshold ? heavy : light).Add(i);
        }
        if (heavy.Count == 0)
        {
            return [.. positions];
        }
        // (Rust's sort_by_key is stable: ties keep the queue order)
        var order = new int[heavy.Count];
        for (var k = 0; k < order.Length; k++)
        {
            order[k] = k;
        }
        Array.Sort(order, (a, b) =>
        {
            var c = weight(heavy[b]).CompareTo(weight(heavy[a]));
            return c != 0 ? c : a.CompareTo(b);
        });
        var result = new int[positions.Count];
        for (var k = 0; k < order.Length; k++)
        {
            result[k] = heavy[order[k]];
        }
        light.CopyTo(result, heavy.Count);
        return result;
    }

    /// <summary>
    /// checkerpool.rs FileQueue: one checker's files, taken from the front by their owner and from the back by thieves.
    /// The front (low 32 bits) and the back (high 32 bits) of the not yet started positions are one 64-bit word, so one
    /// compare-exchange claims a position; a bit per checker (index below 64) that steals from this queue now.
    /// </summary>
    private sealed class FileQueue
    {
        private const long Low = 0xFFFF_FFFFL;
        private readonly int[] positions;
        private readonly ulong[] prefix;
        private long range;
        private long thieves;

        internal FileQueue(int[] positions, Func<int, ulong> weight)
        {
            this.positions = positions;
            prefix = new ulong[positions.Length + 1];
            for (var k = 0; k < positions.Length; k++)
            {
                prefix[k + 1] = prefix[k] + weight(positions[k]);
            }
            range = (long)positions.Length << 32;
        }

        // A heuristic read: a stale value only picks a different victim.
        internal ulong Remaining()
        {
            var r = Volatile.Read(ref range);
            var (front, back) = ((int)(r & Low), (int)(r >>> 32));
            return front < back ? prefix[back] - prefix[front] : 0;
        }

        // Whether checker `me` may take from this queue as a thief (a heuristic read; Admit decides).
        internal bool Admits(int me, int max)
        {
            if (max == 0 || me >= 64)
            {
                return true;
            }
            var bits = (ulong)Volatile.Read(ref thieves);
            return (bits & (1UL << me)) != 0 || System.Numerics.BitOperations.PopCount(bits) < max;
        }

        // Records `me` as one of this queue's thieves, unless it has `max` others already (then false).
        internal bool Admit(int me, int max)
        {
            if (max == 0 || me >= 64)
            {
                return true;
            }
            var bits = Volatile.Read(ref thieves);
            while (true)
            {
                if (((ulong)bits & (1UL << me)) != 0)
                {
                    return true;
                }
                if (System.Numerics.BitOperations.PopCount((ulong)bits) >= max)
                {
                    return false;
                }
                var current = Interlocked.CompareExchange(ref thieves, (long)((ulong)bits | (1UL << me)), bits);
                if (current == bits)
                {
                    return true;
                }
                bits = current;
            }
        }

        // Checker `me` no longer steals from this queue.
        internal void Release(int me)
        {
            if (me < 64)
            {
                Interlocked.And(ref thieves, (long)~(1UL << me));
            }
        }

        // The next position from the front (the owner) or the back (a thief); -1 when none is left.
        internal int Take(bool front)
        {
            var r = Volatile.Read(ref range);
            while (true)
            {
                if ((r & Low) >= (r >>> 32))
                {
                    return -1;
                }
                var updated = front ? r + 1 : r - (1L << 32);
                var current = Interlocked.CompareExchange(ref range, updated, r);
                if (current == r)
                {
                    return positions[front ? (int)(r & Low) : (int)(r >>> 32) - 1];
                }
                r = current;
            }
        }
    }

    // checkerpool.rs queues_next: the next position for checker `me` (-1 for none) and whether it came from another
    // checker's queue. A thief takes only from queues that admit it; `slot` is the queue it steals from now (-1 for none).
    // With `sticky` (steal_sticky, where the module affinity applies) a thief keeps taking from the queue it took from
    // last while that queue has at least half the work of the fullest one.
    private static int QueuesNext(FileQueue[] queues, int me, int maxThieves, ref int slot, bool sticky, ref int lastVictim, out bool fromOther)
    {
        fromOther = false;
        if (queues[me].Take(true) is var own and >= 0)
        {
            return own;
        }
        fromOther = true;
        while (true)
        {
            var anyLeft = false;
            foreach (var q in queues)
            {
                if (q.Remaining() != 0)
                {
                    anyLeft = true;
                    break;
                }
            }
            if (!anyLeft)
            {
                // Queues whose remaining files weigh 0 (unchecked declaration files) are still drained by their owners.
                for (var c = 0; c < queues.Length; c++)
                {
                    if (c != me && queues[c].Take(false) is var i and >= 0)
                    {
                        return i;
                    }
                }
                return -1;
            }
            // max_by_key((left, Reverse(c))): the most work left, then the lowest index
            var victim = -1;
            ulong left = 0;
            for (var c = 0; c < queues.Length; c++)
            {
                if (c == me || !queues[c].Admits(me, maxThieves))
                {
                    continue;
                }
                var remaining = queues[c].Remaining();
                if (victim < 0 || remaining > left)
                {
                    victim = c;
                    left = remaining;
                }
            }
            if (victim < 0)
            {
                return -1;
            }
            if (left == 0)
            {
                // The queues with work left have their thieves; their owners finish them.
                if (slot >= 0)
                {
                    queues[slot].Release(me);
                }
                return -1;
            }
            if (sticky)
            {
                if (lastVictim >= 0 && queues[lastVictim].Admits(me, maxThieves) && queues[lastVictim].Remaining() * 2 >= left)
                {
                    victim = lastVictim;
                }
                lastVictim = victim;
            }
            if (victim != slot)
            {
                if (!queues[victim].Admit(me, maxThieves))
                {
                    continue;
                }
                if (slot >= 0)
                {
                    queues[slot].Release(me);
                }
                slot = victim;
            }
            if (queues[victim].Take(false) is var stolen and >= 0)
            {
                return stolen;
            }
        }
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
    /// program.go Emit(ctx, EmitOptions{WriteFile}) on this pool (module D7): every program file emitted with the emit
    /// resolver of the checker it is assigned to. typescript-go queues one work item per file, each taking its file's
    /// checker (newEmitHost) and locking it for every resolver call; here, as for the diagnostics, one work item per
    /// checker emits that checker's files in program order (a checker is used by one thread at a time by construction).
    /// <paramref name="writeFile"/> receives the program file index with every output, on the thread of the file's
    /// checker. The result combines the files' results in program order (CombineEmitResults), with the emit diagnostics
    /// sorted and deduplicated as emitter.ts emitFiles does for a whole program. Module SC1: a file stolen in the check
    /// pass is emitted by the checker that checked it.
    /// </summary>
    internal EmitResult Emit(Action<int, string, string> writeFile)
    {
        var files = program.SourceFiles;
        var results = new EmitResult?[files.Count];
        RunAll(checkers.Length, checkerIndex =>
        {
            var checker = CheckerAt(checkerIndex);
            checker.EnterThread();
            for (var i = 0; i < files.Count; i++)
            {
                if (owners[i] == checkerIndex)
                {
                    var index = i;
                    results[i] = program.Emit(files[i], checker, (fileName, text, _) => writeFile(index, fileName, text));
                }
            }
        });
        var emitSkipped = false;
        var diagnostics = new List<ProgramDiagnostic>();
        foreach (var result in results)
        {
            if (result is null)
            {
                continue;
            }
            emitSkipped |= result.EmitSkipped;
            diagnostics.AddRange(result.Diagnostics);
        }
        return new EmitResult(emitSkipped, DiagnosticCollection.SortAndDeduplicate(diagnostics));
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
