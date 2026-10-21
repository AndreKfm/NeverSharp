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
// - Keep every result alive until the end; print one JSON line.
//
// The parse call is added when the track's parser exists (Phase 1 M4); until then
// the driver reads the files and reports 0 nodes.

using System.Diagnostics;
using System.Globalization;
using System.Text;

var single = args.Length > 0 && args[0] == "--single";
var listPath = args[single ? 1 : 0];

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
var nodeCounts = new int[files.Length];

void ParseOne(int i)
{
    texts[i] = ReadFile(files[i]);
    nodeCounts[i] = 0; // parser call added in Phase 1 M4; the result will be kept in an array like texts
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

long chars = 0, nodes = 0;
for (var i = 0; i < files.Length; i++)
{
    chars += texts[i].Length;
    nodes += nodeCounts[i];
}

#if TRACK_OPTIMIZED
const string track = "optimized";
#else
const string track = "faithful";
#endif
var runtime = $"{(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "jit" : "aot")}-{(System.Runtime.GCSettings.IsServerGC ? "srv" : "ws")}";
var mode = single ? "single" : "parallel";
var inv = CultureInfo.InvariantCulture;
Console.WriteLine(string.Create(inv,
    $"{{\"driver\":\"cs-{track}-{runtime}-{mode}\",\"files\":{files.Length},\"chars\":{chars},\"nodes\":{nodes},\"priority\":\"{priority}\"," +
    $"\"allocatedBytes\":{GC.GetTotalAllocatedBytes(true)},\"gen0\":{GC.CollectionCount(0)},\"gen1\":{GC.CollectionCount(1)},\"gen2\":{GC.CollectionCount(2)}," +
    $"\"gcPauseMs\":{GC.GetTotalPauseDuration().TotalMilliseconds.ToString("0.###", inv)}}}"));
GC.KeepAlive(texts);
return 0;

// sys.ts readFile: byte order marks decide the encoding, UTF-8 otherwise.
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
