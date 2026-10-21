// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): reads the test vectors written by harness/port/vectors-*.mjs.

using System.Text;
using System.Text.Json;

namespace NeverSharp.Faithful.Tests;

/// <summary>One call of a TypeScript function with the result tsc 6.0.3 returned.</summary>
internal sealed record Vector(int Line, string Function, JsonElement[] Args, JsonElement Result);

internal static class Vectors
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("repository root (global.json) not found");
    }

    /// <summary>The raw lines of a vector file (e.g. the unit texts of M2).</summary>
    public static IEnumerable<string> ReadLines(string module, string name)
    {
        var file = Path.Combine(Root, "artifacts", "vectors", module, name + ".jsonl");
        if (!File.Exists(file))
        {
            throw new InvalidOperationException($"{file} missing: run node harness/port/vectors-{module.ToLowerInvariant()}.mjs");
        }
        foreach (var text in File.ReadLines(file, Encoding.UTF8))
        {
            if (text.Length > 0)
            {
                yield return text;
            }
        }
    }

    public static IEnumerable<Vector> Read(string module, string name)
    {
        var file = Path.Combine(Root, "artifacts", "vectors", module, name + ".jsonl");
        if (!File.Exists(file))
        {
            throw new InvalidOperationException($"{file} missing: run node harness/port/vectors-{module.ToLowerInvariant()}.mjs");
        }
        var line = 0;
        foreach (var text in File.ReadLines(file, Encoding.UTF8))
        {
            line++;
            if (text.Length == 0)
            {
                continue;
            }
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var args = new List<JsonElement>();
            foreach (var a in root.GetProperty("args").EnumerateArray())
            {
                args.Add(a.Clone());
            }
            yield return new Vector(line, root.GetProperty("f").GetString()!, [.. args], root.GetProperty("result").Clone());
        }
    }

    private sealed record Thrown(string Message);

    private static bool Same(object? actual, JsonElement expected) => expected.ValueKind switch
    {
        JsonValueKind.Null => actual is null,
        JsonValueKind.String => actual is string s && s == expected.GetString(),
        JsonValueKind.True => actual is true,
        JsonValueKind.False => actual is false,
        JsonValueKind.Number => actual switch
        {
            int i => expected.TryGetInt32(out var e) && e == i,
            double d => expected.GetDouble() == d,
            _ => false,
        },
        _ => false,
    };

    private static string Show(object? value) => value switch
    {
        null => "null",
        string s => JsonSerializer.Serialize(s),
        bool b => b ? "true" : "false",
        Thrown t => "<threw " + t.Message + ">",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "?",
    };

    public static string? Str(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetString();

    /// <summary>Runs every vector; collects mismatches and fails with the first 20 and their count.</summary>
    public static void Check(string module, string name, Func<Vector, object?> call)
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var v in Read(module, name))
        {
            count++;
            object? actual;
            try
            {
                actual = call(v);
            }
            catch (Exception ex)
            {
                actual = new Thrown(ex.GetType().Name + ": " + ex.Message);
            }
            if (!Same(actual, v.Result))
            {
                failures.Add($"line {v.Line}: {v.Function}({string.Join(", ", Array.ConvertAll(v.Args, a => a.GetRawText()))}) = {Show(actual)}, tsc: {v.Result.GetRawText()}");
            }
        }
        Assert.True(count > 0, $"{name}: no vectors");
        Assert.True(failures.Count == 0, $"{name}: {failures.Count} of {count} calls differ from tsc 6.0.3\n" + string.Join("\n", failures.GetRange(0, Math.Min(20, failures.Count))));
    }
}
