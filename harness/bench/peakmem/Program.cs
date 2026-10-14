// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Runs a command and reports the child's peak memory as one JSON line on stderr:
//   peakWorkingSetBytes  - Windows equivalent of peak RSS (PeakWorkingSetSize)
//   peakCommitBytes      - peak private committed memory (PeakPagefileUsage)
//   wallMs, exitCode
// Counters are read with GetProcessMemoryInfo on the still-open process handle
// after the child has exited, so the measurement does not sample or poll.
// The child's stdout/stderr are passed through unchanged.
//
// Usage: peakmem <command> [args...]

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("peakmem: Windows only (use /usr/bin/time -v elsewhere)");
    return 2;
}
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: peakmem <command> [args...]");
    return 2;
}

var start = new ProcessStartInfo(args[0]) { UseShellExecute = false };
foreach (var arg in args.Skip(1))
{
    start.ArgumentList.Add(arg);
}

var stopwatch = Stopwatch.StartNew();
using var process = Process.Start(start) ?? throw new InvalidOperationException("process did not start");
process.WaitForExit();
stopwatch.Stop();

var counters = new ProcessMemoryCounters { cb = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
if (!Native.GetProcessMemoryInfo(process.SafeHandle.DangerousGetHandle(), ref counters, counters.cb))
{
    throw new InvalidOperationException($"GetProcessMemoryInfo failed: {Marshal.GetLastWin32Error()}");
}

Console.Error.WriteLine(JsonSerializer.Serialize(new PeakMemoryResult(
    (long)counters.PeakWorkingSetSize,
    (long)counters.PeakPagefileUsage,
    stopwatch.Elapsed.TotalMilliseconds,
    process.ExitCode), PeakMemoryJson.Default.PeakMemoryResult));
return process.ExitCode;

[StructLayout(LayoutKind.Sequential)]
struct ProcessMemoryCounters
{
    public uint cb;
    public uint PageFaultCount;
    public nuint PeakWorkingSetSize;
    public nuint WorkingSetSize;
    public nuint QuotaPeakPagedPoolUsage;
    public nuint QuotaPagedPoolUsage;
    public nuint QuotaPeakNonPagedPoolUsage;
    public nuint QuotaNonPagedPoolUsage;
    public nuint PagefileUsage;
    public nuint PeakPagefileUsage;
}

static partial class Native
{
    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCounters counters, uint size);
}

record PeakMemoryResult(long peakWorkingSetBytes, long peakCommitBytes, double wallMs, int exitCode);

[System.Text.Json.Serialization.JsonSerializable(typeof(PeakMemoryResult))]
partial class PeakMemoryJson : System.Text.Json.Serialization.JsonSerializerContext;
