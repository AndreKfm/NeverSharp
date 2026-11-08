// Copyright 2026 Andre Kaufmann
// Portions Copyright 2026 Max Schwenk and the tsrs contributors
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's shared default library (module C0, docs/phase3-checker-layout.md).

using System.Collections.Concurrent;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// TypeScript 6.0.3's library files (<c>lib.*.d.ts</c>) of one folder, parsed and bound at most once per process and
/// then shared read-only by every program and thread, as TypeScript's test harness and language service share their
/// library source files between programs (the oracle does the same per worker). A file is parsed and bound on first
/// request (thread-safe, once); after that nothing writes to it: its line map is computed before it is handed out, and
/// checkers keep everything they compute about library symbols and nodes in their own storage
/// (docs/phase3-checker-layout.md).
/// <para>
/// The files are read from a real folder (Gate C: the pinned typescript 6.0.3 package,
/// <c>harness/astdump/node_modules/typescript/lib</c>, the folder the oracle reads) and named under a virtual folder
/// (<see cref="MemoryCompilerHost.LibFolder"/>). Parse and bind depend on the emit target, the JSDoc parsing mode and
/// the binder's options (alwaysStrict, noFallthroughCasesInSwitch), so one instance exists per combination.
/// </para>
/// </summary>
internal sealed class DefaultLibrary
{
    private static readonly ConcurrentDictionary<(string Directory, string VirtualFolder, ScriptTarget Target, JSDocParsingMode? Mode, bool AlwaysStrict, bool? NoFallthrough), DefaultLibrary> Instances = new();

    private readonly string directory;
    private readonly string virtualFolder;
    private readonly CompilerOptions bindOptions;
    private readonly CreateSourceFileOptions parseOptions;
    private readonly Dictionary<string, Lazy<SourceFile>> files = new(StringComparer.Ordinal);

    private DefaultLibrary(string directory, string virtualFolder, CompilerOptions options, JSDocParsingMode? jsDocParsingMode)
    {
        this.directory = directory;
        this.virtualFolder = virtualFolder;
        bindOptions = new CompilerOptions
        {
            Target = options.Target,
            AlwaysStrict = options.AlwaysStrict,
            NoFallthroughCasesInSwitch = options.NoFallthroughCasesInSwitch,
        };
        parseOptions = new CreateSourceFileOptions { LanguageVersion = options.GetEmitScriptTarget(), JsDocParsingMode = jsDocParsingMode };
        foreach (var path in Directory.EnumerateFiles(directory, "lib.*.d.ts"))
        {
            var name = virtualFolder + "/" + Path.GetFileName(path);
            files[name] = new Lazy<SourceFile>(() => Load(path, name), LazyThreadSafetyMode.ExecutionAndPublication);
        }
    }

    /// <summary>The shared instance for these files and options.</summary>
    internal static DefaultLibrary Get(string directory, string virtualFolder, CompilerOptions options, JSDocParsingMode? jsDocParsingMode)
    {
        ArgumentNullException.ThrowIfNull(options);
        var key = (Path.GetFullPath(directory), virtualFolder, options.GetEmitScriptTarget(), jsDocParsingMode, options.GetAlwaysStrict(), options.NoFallthroughCasesInSwitch);
        return Instances.GetOrAdd(key, k => new DefaultLibrary(k.Directory, k.VirtualFolder, options, k.Mode));
    }

    /// <summary>The real folder the files are read from.</summary>
    internal string LibDirectory => directory;

    /// <summary>Whether <paramref name="fileName"/> (a name under the virtual folder) is a library file.</summary>
    internal bool Exists(string fileName) => files.ContainsKey(fileName);

    /// <summary>The library file, parsed and bound (shared, read-only); null when it does not exist.</summary>
    internal SourceFile? GetFile(string fileName) => files.TryGetValue(fileName, out var file) ? file.Value : null;

    // the files Preload has started (module FE1)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> preloaded = new(StringComparer.Ordinal);

    /// <summary>
    /// Module FE1 (Track O / tsrs-only): starts parsing and binding <paramref name="fileName"/> and every library file
    /// its <c>/// &lt;reference lib="..." /&gt;</c> directives reach, each file on a thread-pool work item, and returns
    /// at once (tsrs parses the lib chain in its loader rounds in parallel with the program's files,
    /// notes/perf-frontend-64.md). A file is loaded once either way (<see cref="GetFile"/> waits for a load in
    /// progress), so the files are the same; only the time of their loading changes.
    /// </summary>
    internal void Preload(string fileName)
    {
        if (!files.TryGetValue(fileName, out var lazy) || !preloaded.TryAdd(fileName, true))
        {
            return;
        }
        Task.Run(() =>
        {
            var file = lazy.Value;
            var references = file.Store.LibReferenceDirectives(file.Root);
            for (var i = 0; i < references.Count; i++)
            {
                if (LibraryNames.GetLibFileNameFromLibReference(references[i].FileName) is { } libFileName)
                {
                    Preload(virtualFolder + "/" + libFileName);
                }
            }
        });
    }

    /// <summary>Whether <paramref name="file"/> is one of this library's shared files.</summary>
    internal bool Owns(SourceFile file)
    {
        var name = file.Store.FileName(file.Root);
        return files.TryGetValue(name, out var lazy) && lazy.IsValueCreated && ReferenceEquals(lazy.Value, file);
    }

    /// <summary>
    /// Stopwatch ticks spent in <see cref="Load"/> in this process, summed over the loads (module FE1's phase probe;
    /// the loads of one program run one after another).
    /// </summary>
    internal static long LoadTicks;

    private SourceFile Load(string path, string name)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        // tsc's host: sys.readFile (Track O's reader has its semantics, step O1), then createSourceFile.
        var file = Parser.ParseFile(path, name, parseOptions, setParentNodes: false, ScriptKind.TS);
        // Written once here, read by every program afterwards: the line map (otherwise computed lazily on first use)
        // and the binder output (checker.ts initializeTypeChecker binds every file that is not bound yet).
        Scanner.GetLineStarts(new SourceFileNodeLike(file.Store, file.Root));
        Binder.BindSourceFile(file, bindOptions);
        Interlocked.Add(ref LoadTicks, System.Diagnostics.Stopwatch.GetTimestamp() - start);
        return file;
    }

    public override string ToString() => $"DefaultLibrary({directory} as {virtualFolder})";
}
