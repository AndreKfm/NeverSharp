// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): reader check of Track O (step O1).
//
// For every file of artifacts/bench/filelists/*.txt (benchmark repositories, tuning corpus) and every unit of the
// equivalence corpus, compares Track O's SourceText.Read(path).Chars with the reference decoding of ts.sys.readFile
// (sys.ts: UTF-16 BE/LE and UTF-8 byte order marks decide the encoding, UTF-8 otherwise; the same code as the
// parse-only drivers' ReadFile). Exit code 0 only if every file is identical, character by character.
//
// Usage: NeverSharp.ReaderCheck [--main <checkout with artifacts/>]

using System.Text;
using NeverSharp.Optimized;

var main = args.Length >= 2 && args[0] == "--main" ? args[1] : FindRoot();
var files = new List<string>();
foreach (var list in Directory.GetFiles(Path.Combine(main, "artifacts", "bench", "filelists"), "*.txt"))
{
    foreach (var line in File.ReadAllLines(list))
    {
        if (line.Length > 0)
        {
            files.Add(line);
        }
    }
}
files.AddRange(Directory.GetFiles(Path.Combine(main, "artifacts", "equivalence", "units"), "*.txt"));

var differing = new List<string>();
var checkedFiles = 0;
long chars = 0;
Parallel.ForEach(files, file =>
{
    var expected = ReferenceRead(file);
    var actual = SourceText.Read(file).Chars;
    if (!actual.SequenceEqual(expected))
    {
        lock (differing)
        {
            differing.Add($"{file}: {actual.Length} vs {expected.Length} chars");
        }
    }
    Interlocked.Increment(ref checkedFiles);
    Interlocked.Add(ref chars, expected.Length);
});
Console.WriteLine($"reader check: {checkedFiles} files, {chars} chars, {differing.Count} differing");
foreach (var d in differing.Take(20))
{
    Console.WriteLine("  " + d);
}
return differing.Count == 0 ? 0 : 1;

static string ReferenceRead(string path)
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

static string FindRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
    {
        dir = dir.Parent;
    }
    return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
}
