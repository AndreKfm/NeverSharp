// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): gate of module M1 (phase1-plan.md §4). Calls the C# API
// fixed in docs/modules/M1.md and compares with the results of tsc 6.0.3.

namespace NeverSharp.Faithful.Tests;

public class M1Tests
{
    [Fact]
    public void PathsSingleArgument() => Vectors.Check("M1", "paths-single", v =>
    {
        var p = Vectors.Str(v.Args[0])!;
        return v.Function switch
        {
            "normalizePath" => TsPath.NormalizePath(p),
            "normalizeSlashes" => TsPath.NormalizeSlashes(p),
            "getRootLength" => TsPath.GetRootLength(p),
            "getBaseFileName" => TsPath.GetBaseFileName(p),
            "getAnyExtensionFromPath" => TsPath.GetAnyExtensionFromPath(p),
            "hasTrailingDirectorySeparator" => TsPath.HasTrailingDirectorySeparator(p),
            "removeTrailingDirectorySeparator" => TsPath.RemoveTrailingDirectorySeparator(p),
            "ensureTrailingDirectorySeparator" => TsPath.EnsureTrailingDirectorySeparator(p),
            "tryGetExtensionFromPath" => TsPath.TryGetExtensionFromPath(p),
            _ => throw new InvalidOperationException(v.Function),
        };
    });

    [Fact]
    public void PathsAbsolute() => Vectors.Check("M1", "paths-absolute", v =>
        TsPath.GetNormalizedAbsolutePath(Vectors.Str(v.Args[0])!, v.Args.Length > 1 ? Vectors.Str(v.Args[1]) : null));

    [Fact]
    public void PathsCombine() => Vectors.Check("M1", "paths-combine", v =>
    {
        var rest = new string?[v.Args.Length - 1];
        for (var i = 1; i < v.Args.Length; i++)
        {
            rest[i - 1] = Vectors.Str(v.Args[i]);
        }
        return TsPath.CombinePaths(Vectors.Str(v.Args[0])!, rest);
    });

    [Fact]
    public void PathsExtension() => Vectors.Check("M1", "paths-extension", v =>
        TsPath.FileExtensionIs(Vectors.Str(v.Args[0])!, Vectors.Str(v.Args[1])!));

    [Fact]
    public void PathsAnyExtension() => Vectors.Check("M1", "paths-any-extension", v =>
    {
        var p = Vectors.Str(v.Args[0])!;
        var ignoreCase = v.Args[2].GetBoolean();
        if (v.Args[1].ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return TsPath.GetAnyExtensionFromPath(p, new[] { v.Args[1].GetString()! }, ignoreCase);
        }
        var extensions = new List<string>();
        foreach (var e in v.Args[1].EnumerateArray())
        {
            extensions.Add(e.GetString()!);
        }
        return TsPath.GetAnyExtensionFromPath(p, extensions, ignoreCase);
    });

    [Fact]
    public void SpellingSuggestion() => Vectors.Check("M1", "spelling", v =>
    {
        var candidates = new List<string>();
        foreach (var c in v.Args[1].EnumerateArray())
        {
            candidates.Add(c.GetString()!);
        }
        return Core.GetSpellingSuggestion(Vectors.Str(v.Args[0])!, candidates, c => c);
    });

    [Fact]
    public void DebugFailures()
    {
        Debug.Assert(true);
        Assert.Equal("Debug Failure. False expression.", Assert.ThrowsAny<Exception>(() => Debug.Assert(false)).Message);
        Assert.Equal("Debug Failure. False expression: m", Assert.ThrowsAny<Exception>(() => Debug.Assert(false, "m")).Message);
        Assert.Equal("Debug Failure. x", Assert.ThrowsAny<Exception>(() => Debug.Fail("x")).Message);
        Assert.Equal("Debug Failure.", Assert.ThrowsAny<Exception>(() => Debug.Fail()).Message);
    }

    [Fact]
    public void TextSpans()
    {
        var span = UtilitiesPublic.CreateTextSpan(5, 3);
        Assert.Equal((5, 3), (span.Start, span.Length));
        span = UtilitiesPublic.CreateTextSpanFromBounds(2, 9);
        Assert.Equal((2, 7), (span.Start, span.Length));
        Assert.Equal("start < 0", Assert.ThrowsAny<Exception>(() => UtilitiesPublic.CreateTextSpan(-1, 1)).Message);
        Assert.Equal("length < 0", Assert.ThrowsAny<Exception>(() => UtilitiesPublic.CreateTextSpanFromBounds(4, 3)).Message);
    }
}
