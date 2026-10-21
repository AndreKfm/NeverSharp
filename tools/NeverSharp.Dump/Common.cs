// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): options, corpus manifest and file helpers of the dump writers.

using System.Text;
using System.Text.Json;

namespace NeverSharp.Dump;

internal sealed class Options(string[] args)
{
    public string Get(string name, string fallback)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    public string Corpus => Get("--corpus", Path.Combine(Common.FindRoot(), "artifacts", "equivalence"));
}

internal sealed record Unit(string Id, string Kind, string FileName);

internal static class Common
{
    /// <summary>UTF-8 without byte order mark on output; on input, GetString keeps a BOM as U+FEFF, as Node's readFileSync(..., "utf8") does.</summary>
    public static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static List<Unit> ReadUnits(string corpus)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(corpus, "manifest.json")));
        var units = new List<Unit>();
        foreach (var u in manifest.RootElement.GetProperty("units").EnumerateArray())
        {
            units.Add(new Unit(u.GetProperty("id").GetString()!, u.GetProperty("kind").GetString()!, u.GetProperty("fileName").GetString()!));
        }
        return units;
    }

    public static string ReadUnitText(string corpus, Unit unit) => Utf8.GetString(File.ReadAllBytes(Path.Combine(corpus, "units", unit.Id + ".txt")));

    public static void ResetDirectory(string dir)
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
        Directory.CreateDirectory(dir);
    }

    public static Dictionary<int, string> Names((int Value, string Name)[] table)
    {
        var names = new Dictionary<int, string>();
        foreach (var (value, name) in table)
        {
            names[value] = name;
        }
        return names;
    }

    public static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
