// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): summarizes a dotnet-trace gc-verbose trace (EventPipe .nettrace):
// sampled allocation bytes per type (GCAllocationTick), GC count and total pause.
//
// Usage: dotnet run -c Release --project tools/NeverSharp.TraceSummary -- <trace.nettrace>

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

var byType = new Dictionary<string, (long Bytes, int Ticks)>();
long total = 0; int gcs = 0; double pauseMs = 0; var gcStart = new Dictionary<int, double>();
using (var source = new EventPipeEventSource(args[0]))
{
    var clr = new ClrTraceEventParser(source);
    clr.GCAllocationTick += (GCAllocationTickTraceData e) =>
    {
        var name = string.IsNullOrEmpty(e.TypeName) ? "?" : e.TypeName;
        byType.TryGetValue(name, out var v);
        byType[name] = (v.Bytes + e.AllocationAmount64, v.Ticks + 1);
        total += e.AllocationAmount64;
    };
    clr.GCStart += e => { gcs++; };
    clr.GCSuspendEEStart += e => gcStart[e.ProcessID] = e.TimeStampRelativeMSec;
    clr.GCRestartEEStop += e => { if (gcStart.TryGetValue(e.ProcessID, out var s)) pauseMs += e.TimeStampRelativeMSec - s; };
    source.Process();
}
Console.WriteLine($"sampled bytes {total / 1e6:F0} MB, GCs {gcs}, pause {pauseMs:F0} ms");
foreach (var (name, v) in byType.OrderByDescending(kv => kv.Value.Bytes).Take(30))
    Console.WriteLine($"{100.0 * v.Bytes / total,5:F1} %  {v.Bytes / 1e6,8:F0} MB  {name}");
