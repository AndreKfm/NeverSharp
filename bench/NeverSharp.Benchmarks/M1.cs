// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): ranking workload of module M1 (docs/modules/M1.md).
// For every file name of the tuning corpus: the path work of parseSourceFile
// (parser.ts:1742 normalizePath, :1804 isDeclarationFileName ->
// getDeclarationFileExtension).

using BenchmarkDotNet.Attributes;
#if TRACK_OPTIMIZED
using NeverSharp.Optimized;
#else
using NeverSharp.Faithful;
#endif

namespace NeverSharp.Benchmarks;

public class M1
{
    private static readonly string[] DeclarationExtensions = [".d.ts", ".d.cts", ".d.mts"];
    private string[] names = [];

    [GlobalSetup]
    public void Setup()
    {
        var list = Environment.GetEnvironmentVariable("NEVERSHARP_FILELIST") ?? Path.Combine(FindRoot(), "artifacts", "bench", "filelists", "tuning.txt");
        names = File.ReadAllLines(list);
    }

    [Benchmark]
    public int ParseSourceFilePathWork()
    {
        var declarationFiles = 0;
        foreach (var name in names)
        {
            var fileName = TsPath.NormalizePath(name);
            // getDeclarationFileExtension (parser.ts)
            var extension = TsPath.GetAnyExtensionFromPath(fileName, DeclarationExtensions, ignoreCase: false);
            if (extension.Length == 0 && TsPath.FileExtensionIs(fileName, ".ts"))
            {
                var baseName = TsPath.GetBaseFileName(fileName);
                if (baseName.LastIndexOf(".d.", StringComparison.Ordinal) >= 0)
                {
                    extension = ".d.";
                }
            }
            if (extension.Length != 0)
            {
                declarationFiles++;
            }
        }
        return declarationFiles;
    }

    internal static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
