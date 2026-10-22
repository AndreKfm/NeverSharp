// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): ranking workload of module M2 (docs/modules/M2.md).
// Scans every file of the tuning corpus to EndOfFileToken with one scanner, driven as
// the parser drives it (parser.ts:1444 createScanner(Latest, skipTrivia: true);
// :1772-1775 setText, setOnError, setLanguageVariant per file).

using BenchmarkDotNet.Attributes;
#if TRACK_OPTIMIZED
using NeverSharp.Optimized;
#else
using NeverSharp.Faithful;
#endif
using NeverSharp.Generated;

namespace NeverSharp.Benchmarks;

public class M2
{
    private (string Text, LanguageVariant Variant)[] files = [];
    private Scanner scanner = null!;
    private ErrorCallback onError = null!;
    private int errors;

    [GlobalSetup]
    public void Setup()
    {
        var list = Environment.GetEnvironmentVariable("NEVERSHARP_FILELIST") ?? Path.Combine(M1.FindRoot(), "artifacts", "bench", "filelists", "tuning.txt");
        var names = File.ReadAllLines(list);
        files = new (string, LanguageVariant)[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            var jsx = names[i].EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) || names[i].EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);
            files[i] = (TextFile.Read(names[i]), jsx ? LanguageVariant.JSX : LanguageVariant.Standard);
        }
        scanner = Scanner.CreateScanner(ScriptTarget.Latest, skipTrivia: true);
        onError = (_, _, _) => errors++; // one delegate, as the parser passes its scanError function
    }

    [Benchmark]
    public long ScanTuningCorpus()
    {
        long tokens = 0;
        foreach (var (text, variant) in files)
        {
            scanner.SetText(text);
            scanner.SetOnError(onError);
            scanner.SetLanguageVariant(variant);
            while (scanner.Scan() != SyntaxKind.EndOfFileToken)
            {
                tokens++;
            }
        }
        scanner.SetText("");
        scanner.SetOnError(null);
        return tokens;
    }
}

/// <summary>sys.ts readFile semantics, as the parse-only drivers read files (benchmark-harness.md §4.1).</summary>
internal static class TextFile
{
    public static string Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, (bytes.Length - 2) & ~1);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return System.Text.Encoding.Unicode.GetString(bytes, 2, (bytes.Length - 2) & ~1);
        }
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
