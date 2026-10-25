// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/commandLineParser.ts (libEntries, libs, libMap) and src/compiler/program.ts
// (getLibFileNameFromLibReference)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/tsoptions/enummaps.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

namespace NeverSharp.Optimized;

/// <summary>commandLineParser.ts: the names of the library files (`lib` option, `/// &lt;reference lib="..." /&gt;`).</summary>
internal static class LibraryNames
{
    /// <summary>
    /// commandLineParser.ts libEntries: library name and file, in TypeScript's order (the order decides the priority of
    /// default library files in a program, program.ts getDefaultLibFilePriority).
    /// </summary>
    internal static readonly (string Name, string FileName)[] LibEntries =
    [
        // JavaScript only
        ("es5", "lib.es5.d.ts"),
        ("es6", "lib.es2015.d.ts"),
        ("es2015", "lib.es2015.d.ts"),
        ("es7", "lib.es2016.d.ts"),
        ("es2016", "lib.es2016.d.ts"),
        ("es2017", "lib.es2017.d.ts"),
        ("es2018", "lib.es2018.d.ts"),
        ("es2019", "lib.es2019.d.ts"),
        ("es2020", "lib.es2020.d.ts"),
        ("es2021", "lib.es2021.d.ts"),
        ("es2022", "lib.es2022.d.ts"),
        ("es2023", "lib.es2023.d.ts"),
        ("es2024", "lib.es2024.d.ts"),
        ("es2025", "lib.es2025.d.ts"),
        ("esnext", "lib.esnext.d.ts"),
        // Host only
        ("dom", "lib.dom.d.ts"),
        ("dom.iterable", "lib.dom.iterable.d.ts"),
        ("dom.asynciterable", "lib.dom.asynciterable.d.ts"),
        ("webworker", "lib.webworker.d.ts"),
        ("webworker.importscripts", "lib.webworker.importscripts.d.ts"),
        ("webworker.iterable", "lib.webworker.iterable.d.ts"),
        ("webworker.asynciterable", "lib.webworker.asynciterable.d.ts"),
        ("scripthost", "lib.scripthost.d.ts"),
        // ES2015 and later By-feature options
        ("es2015.core", "lib.es2015.core.d.ts"),
        ("es2015.collection", "lib.es2015.collection.d.ts"),
        ("es2015.generator", "lib.es2015.generator.d.ts"),
        ("es2015.iterable", "lib.es2015.iterable.d.ts"),
        ("es2015.promise", "lib.es2015.promise.d.ts"),
        ("es2015.proxy", "lib.es2015.proxy.d.ts"),
        ("es2015.reflect", "lib.es2015.reflect.d.ts"),
        ("es2015.symbol", "lib.es2015.symbol.d.ts"),
        ("es2015.symbol.wellknown", "lib.es2015.symbol.wellknown.d.ts"),
        ("es2016.array.include", "lib.es2016.array.include.d.ts"),
        ("es2016.intl", "lib.es2016.intl.d.ts"),
        ("es2017.arraybuffer", "lib.es2017.arraybuffer.d.ts"),
        ("es2017.date", "lib.es2017.date.d.ts"),
        ("es2017.object", "lib.es2017.object.d.ts"),
        ("es2017.sharedmemory", "lib.es2017.sharedmemory.d.ts"),
        ("es2017.string", "lib.es2017.string.d.ts"),
        ("es2017.intl", "lib.es2017.intl.d.ts"),
        ("es2017.typedarrays", "lib.es2017.typedarrays.d.ts"),
        ("es2018.asyncgenerator", "lib.es2018.asyncgenerator.d.ts"),
        ("es2018.asynciterable", "lib.es2018.asynciterable.d.ts"),
        ("es2018.intl", "lib.es2018.intl.d.ts"),
        ("es2018.promise", "lib.es2018.promise.d.ts"),
        ("es2018.regexp", "lib.es2018.regexp.d.ts"),
        ("es2019.array", "lib.es2019.array.d.ts"),
        ("es2019.object", "lib.es2019.object.d.ts"),
        ("es2019.string", "lib.es2019.string.d.ts"),
        ("es2019.symbol", "lib.es2019.symbol.d.ts"),
        ("es2019.intl", "lib.es2019.intl.d.ts"),
        ("es2020.bigint", "lib.es2020.bigint.d.ts"),
        ("es2020.date", "lib.es2020.date.d.ts"),
        ("es2020.promise", "lib.es2020.promise.d.ts"),
        ("es2020.sharedmemory", "lib.es2020.sharedmemory.d.ts"),
        ("es2020.string", "lib.es2020.string.d.ts"),
        ("es2020.symbol.wellknown", "lib.es2020.symbol.wellknown.d.ts"),
        ("es2020.intl", "lib.es2020.intl.d.ts"),
        ("es2020.number", "lib.es2020.number.d.ts"),
        ("es2021.promise", "lib.es2021.promise.d.ts"),
        ("es2021.string", "lib.es2021.string.d.ts"),
        ("es2021.weakref", "lib.es2021.weakref.d.ts"),
        ("es2021.intl", "lib.es2021.intl.d.ts"),
        ("es2022.array", "lib.es2022.array.d.ts"),
        ("es2022.error", "lib.es2022.error.d.ts"),
        ("es2022.intl", "lib.es2022.intl.d.ts"),
        ("es2022.object", "lib.es2022.object.d.ts"),
        ("es2022.string", "lib.es2022.string.d.ts"),
        ("es2022.regexp", "lib.es2022.regexp.d.ts"),
        ("es2023.array", "lib.es2023.array.d.ts"),
        ("es2023.collection", "lib.es2023.collection.d.ts"),
        ("es2023.intl", "lib.es2023.intl.d.ts"),
        ("es2024.arraybuffer", "lib.es2024.arraybuffer.d.ts"),
        ("es2024.collection", "lib.es2024.collection.d.ts"),
        ("es2024.object", "lib.es2024.object.d.ts"),
        ("es2024.promise", "lib.es2024.promise.d.ts"),
        ("es2024.regexp", "lib.es2024.regexp.d.ts"),
        ("es2024.sharedmemory", "lib.es2024.sharedmemory.d.ts"),
        ("es2024.string", "lib.es2024.string.d.ts"),
        ("es2025.collection", "lib.es2025.collection.d.ts"),
        ("es2025.float16", "lib.es2025.float16.d.ts"),
        ("es2025.intl", "lib.es2025.intl.d.ts"),
        ("es2025.iterator", "lib.es2025.iterator.d.ts"),
        ("es2025.promise", "lib.es2025.promise.d.ts"),
        ("es2025.regexp", "lib.es2025.regexp.d.ts"),
        // Fallback for backward compatibility
        ("esnext.asynciterable", "lib.es2018.asynciterable.d.ts"),
        ("esnext.symbol", "lib.es2019.symbol.d.ts"),
        ("esnext.bigint", "lib.es2020.bigint.d.ts"),
        ("esnext.weakref", "lib.es2021.weakref.d.ts"),
        ("esnext.object", "lib.es2024.object.d.ts"),
        ("esnext.regexp", "lib.es2024.regexp.d.ts"),
        ("esnext.string", "lib.es2024.string.d.ts"),
        ("esnext.float16", "lib.es2025.float16.d.ts"),
        ("esnext.iterator", "lib.es2025.iterator.d.ts"),
        ("esnext.promise", "lib.es2025.promise.d.ts"),
        // ESNext By-feature options
        ("esnext.array", "lib.esnext.array.d.ts"),
        ("esnext.collection", "lib.esnext.collection.d.ts"),
        ("esnext.date", "lib.esnext.date.d.ts"),
        ("esnext.decorators", "lib.esnext.decorators.d.ts"),
        ("esnext.disposable", "lib.esnext.disposable.d.ts"),
        ("esnext.error", "lib.esnext.error.d.ts"),
        ("esnext.intl", "lib.esnext.intl.d.ts"),
        ("esnext.sharedmemory", "lib.esnext.sharedmemory.d.ts"),
        ("esnext.temporal", "lib.esnext.temporal.d.ts"),
        ("esnext.typedarrays", "lib.esnext.typedarrays.d.ts"),
        // Decorators
        ("decorators", "lib.decorators.d.ts"),
        ("decorators.legacy", "lib.decorators.legacy.d.ts"),
    ];

    private static readonly Dictionary<string, int> LibIndex = CreateLibIndex();

    private static readonly Dictionary<string, string> LibMap = CreateLibMap();

    private static Dictionary<string, int> CreateLibIndex()
    {
        // commandLineParser.ts libs = libEntries.map(entry => entry[0]); libs.indexOf(name) finds the first entry.
        var index = new Dictionary<string, int>(LibEntries.Length, StringComparer.Ordinal);
        for (var i = 0; i < LibEntries.Length; i++)
        {
            index.TryAdd(LibEntries[i].Name, i);
        }
        return index;
    }

    private static Dictionary<string, string> CreateLibMap()
    {
        var map = new Dictionary<string, string>(LibEntries.Length, StringComparer.Ordinal);
        foreach (var (name, fileName) in LibEntries)
        {
            map[name] = fileName;
        }
        return map;
    }

    /// <summary>commandLineParser.ts libs.indexOf(name); -1 when the name is not a library name.</summary>
    internal static int IndexOf(string name) => LibIndex.TryGetValue(name, out var i) ? i : -1;

    /// <summary>The number of library names (commandLineParser.ts libs.length).</summary>
    internal static int Count => LibEntries.Length;

    /// <summary>program.ts getLibFileNameFromLibReference: the file of a lib reference, null when the name is unknown.</summary>
    internal static string? GetLibFileNameFromLibReference(string libReferenceFileName) =>
        LibMap.TryGetValue(ToFileNameLowerCase(libReferenceFileName), out var fileName) ? fileName : null;

    /// <summary>
    /// core.ts toFileNameLowerCase: lower case, except the dotted capital I (U+0130), the dotless small i (U+0131) and
    /// the sharp s (U+00DF), which TypeScript keeps (per character; toLowerCase of the others maps one to one here).
    /// </summary>
    private static string ToFileNameLowerCase(string x)
    {
        return string.Create(x.Length, x, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = c is (char)0x130 or (char)0x131 or (char)0xDF ? c : char.ToLowerInvariant(c);
            }
        });
    }
}
