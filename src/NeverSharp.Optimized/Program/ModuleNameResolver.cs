// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// Portions Copyright 2026 Max Schwenk and the tsrs contributors
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/moduleNameResolver.ts (bundler resolution) and the utilities.ts/path.ts helpers it uses
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/module/resolver.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C10, what TypeScript 6.0.3's defaults reach (details below).
//
// Ported: resolveModuleName, bundlerModuleNameResolver, nodeModuleNameResolverWorker, getConditions,
// getNodeResolutionFeatures, nodeLoadModuleByRelativeName, loadModuleFromFile, tryAddingExtensions,
// loadNodeModuleFromDirectory(Worker), getPackageJsonInfo, getPackageScopeForPath, loadModuleFromSelfNameReference,
// loadModuleFromExports, loadModuleFromImports, loadModuleFromExportsOrImports, loadModuleFromTargetExportOrImport,
// comparePatternKeys, the per-directory and package.json caches: relative names, `#` imports and self-name references
// through package.json, directories with package.json types/typings/main and index files. Not ported, each because the
// options or the benchmark programs never reach it (the parts throw NotSupportedException where reaching them is possible at all): the other resolution kinds; paths,
// baseUrl, rootDirs, typeRoots, moduleSuffixes, customConditions, noDtsResolution, outDir/declarationDir input-file
// mapping (tryLoadInputFileForPath), project reference redirects; node_modules lookups (the repositories' dependencies
// are not installed: an existing node_modules directory throws); typesVersions and `types@` conditions (no package.json
// of the benchmark repositories has them); tracing, failed lookup and affecting locations (watch mode only), peer
// dependencies of package ids and realpath (node_modules only).

using System.Text.Json;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>types.ts PackageId (the fields the checker can read).</summary>
internal sealed record PackageId(string Name, string SubModuleName, string Version);

/// <summary>types.ts ResolvedModuleFull.</summary>
internal sealed class ResolvedModuleFull
{
    internal ResolvedModuleFull(string resolvedFileName, string extension, bool isExternalLibraryImport, PackageId? packageId, bool resolvedUsingTsExtension)
    {
        ResolvedFileName = resolvedFileName;
        Extension = extension;
        IsExternalLibraryImport = isExternalLibraryImport;
        PackageId = packageId;
        ResolvedUsingTsExtension = resolvedUsingTsExtension;
    }

    internal string ResolvedFileName { get; }

    internal string Extension { get; }

    internal bool IsExternalLibraryImport { get; }

    internal PackageId? PackageId { get; }

    internal bool ResolvedUsingTsExtension { get; }
}

/// <summary>
/// moduleNameResolver.ts createModuleResolutionCache: the per-directory resolution cache and the package.json info cache
/// of one program (createProgram creates one when the host has no resolveModuleNames). Safe for concurrent use (module
/// CO4: the program resolves its files' imports in parallel): an entry is the same whichever file computes it first, so a
/// race only computes an entry twice.
/// </summary>
internal sealed class ModuleResolutionCache
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Directory, string Name, ModuleKind? Mode), ResolvedModuleFull?> directoryCache = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<PackageJsonInfoCacheEntry>> packageJsonInfoCache = new(StringComparer.Ordinal);
    private readonly string currentDirectory;
    private readonly Func<string, string> getCanonicalFileName;

    internal ModuleResolutionCache(string currentDirectory, Func<string, string> getCanonicalFileName)
    {
        this.currentDirectory = currentDirectory;
        this.getCanonicalFileName = getCanonicalFileName;
    }

    private string ToPath(string fileName) => getCanonicalFileName(TsPath.GetNormalizedAbsolutePath(fileName, currentDirectory));

    /// <summary>The key of a directory in the per-directory cache (its canonical absolute path).</summary>
    internal string DirectoryKey(string directoryName) => ToPath(directoryName);

    internal bool TryGetFromDirectoryCache(string name, ModuleKind? mode, string directoryKey, out ResolvedModuleFull? result) =>
        directoryCache.TryGetValue((directoryKey, name, mode), out result);

    internal void SetInDirectoryCache(string name, ModuleKind? mode, string directoryKey, ResolvedModuleFull? result) =>
        directoryCache[(directoryKey, name, mode)] = result;

    /// <summary>
    /// The cached entry of <paramref name="packageJsonPath"/>, computed by <paramref name="compute"/> if there is none.
    /// Module FE1 (Track O): computed once even when several threads ask at the same time (before, each thread that
    /// missed read and parsed the file: vscode's parallel resolution read package.json files 589 times instead of 66);
    /// every computation gives the same entry, so the result is the same.
    /// </summary>
    internal PackageJsonInfoCacheEntry GetOrAddPackageJsonInfo(string packageJsonPath, Func<PackageJsonInfoCacheEntry> compute)
    {
        var key = ToPath(packageJsonPath);
        if (packageJsonInfoCache.TryGetValue(key, out var lazy))
        {
            return lazy.Value;
        }
        return packageJsonInfoCache.GetOrAdd(key, _ => new Lazy<PackageJsonInfoCacheEntry>(compute, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    // Module FE1 (Track O): the directories from which loadModuleFromNearestNodeModulesDirectory walked up and found
    // no node_modules folder.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> nodeModulesWalked = new(StringComparer.Ordinal);

    internal bool NodeModulesWalked(string directory) => nodeModulesWalked.ContainsKey(directory);

    internal void SetNodeModulesWalked(string directory) => nodeModulesWalked.TryAdd(directory, true);

    // Module FE1 (Track O): getPackageScopeForPath per directory.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PackageJsonInfo?> packageScopes = new(StringComparer.Ordinal);

    internal PackageJsonInfo? GetOrAddPackageScope(string directory, Func<string, PackageJsonInfo?> compute) =>
        packageScopes.TryGetValue(directory, out var scope) ? scope : packageScopes.GetOrAdd(directory, compute);
}

/// <summary>moduleNameResolver.ts PackageJsonInfoCacheEntry: a package.json found (<see cref="PackageJsonInfo"/>) or missing.</summary>
internal class PackageJsonInfoCacheEntry
{
    internal PackageJsonInfoCacheEntry(string packageDirectory)
    {
        PackageDirectory = packageDirectory;
    }

    internal string PackageDirectory { get; }
}

/// <summary>moduleNameResolver.ts MissingPackageJsonInfo.</summary>
internal sealed class MissingPackageJsonInfo : PackageJsonInfoCacheEntry
{
    internal MissingPackageJsonInfo(string packageDirectory, bool directoryExists)
        : base(packageDirectory)
    {
        DirectoryExists = directoryExists;
    }

    internal bool DirectoryExists { get; }
}

/// <summary>moduleNameResolver.ts PackageJsonInfo: the directory and the parsed contents (an object, or empty).</summary>
internal sealed class PackageJsonInfo : PackageJsonInfoCacheEntry
{
    internal PackageJsonInfo(string packageDirectory, JsonElement content)
        : base(packageDirectory)
    {
        Content = content;
    }

    /// <summary>packageJsonContent: the parsed JSON (ValueKind Undefined when the file is not a JSON object: readJson's <c>{}</c>).</summary>
    internal JsonElement Content { get; }
}

/// <summary>moduleNameResolver.ts: module name resolution for TypeScript 6.0.3's default options (see the file header).</summary>
internal static class ModuleNameResolver
{
    // moduleNameResolver.ts Extensions
    [Flags]
    private enum Extensions
    {
        None = 0,
        TypeScript = 1 << 0, // '.ts', '.tsx', '.mts', '.cts'
        JavaScript = 1 << 1, // '.js', '.jsx', '.mjs', '.cjs'
        Declaration = 1 << 2, // '.d.ts', etc.
        Json = 1 << 3, // '.json'
        ImplementationFiles = TypeScript | JavaScript,
    }

    // moduleNameResolver.ts NodeResolutionFeatures
    [Flags]
    private enum NodeResolutionFeatures
    {
        None = 0,
        Imports = 1 << 1,
        SelfName = 1 << 2,
        Exports = 1 << 3,
        ExportsPatternTrailers = 1 << 4,
        ImportsPatternRoot = 1 << 6,
        BundlerDefault = Imports | SelfName | Exports | ExportsPatternTrailers | ImportsPatternRoot,
        EsmMode = 1 << 5,
    }

    /// <summary>Result of trying to resolve a module (moduleNameResolver.ts Resolved).</summary>
    private sealed record Resolved(string Path, string Extension, PackageId? PackageId, bool? ResolvedUsingTsExtension);

    /// <summary>Result of trying to resolve a module at a file; needs a packageId added later (PathAndExtension).</summary>
    private sealed record PathAndExtension(string Path, string Ext, bool? ResolvedUsingTsExtension);

    /// <summary>
    /// SearchResult&lt;T&gt;: <c>undefined</c> (null here) means "continue searching"; a result with a null value means "stop,
    /// nothing found".
    /// </summary>
    private sealed record SearchResult<T>(T? Value) where T : class;

    private static SearchResult<T>? ToSearchResult<T>(T? value) where T : class => value is not null ? new SearchResult<T>(value) : null;

    private sealed class ModuleResolutionState
    {
        internal required ICompilerHost Host { get; init; }

        internal required CompilerOptions CompilerOptions { get; init; }

        internal required ModuleResolutionCache? PackageJsonInfoCache { get; init; }

        internal required NodeResolutionFeatures Features { get; set; }

        internal required List<string> Conditions { get; init; }

        internal required string? RequestContainingDirectory { get; init; }

        internal bool CandidateIsFromPackageJsonField { get; set; }

        internal bool ResolvedPackageDirectory { get; set; }
    }

    /// <summary>moduleNameResolver.ts resolveModuleName (no redirected reference, no tracing).</summary>
    internal static ResolvedModuleFull? ResolveModuleName(string moduleName, string containingFile, CompilerOptions compilerOptions, ICompilerHost host, ModuleResolutionCache? cache, ModuleKind? resolutionMode)
    {
        var containingDirectory = GetDirectoryPath(containingFile);
        return ResolveModuleName(moduleName, containingDirectory, cache?.DirectoryKey(containingDirectory), compilerOptions, host, cache, resolutionMode);
    }

    /// <summary>
    /// resolveModuleName for a containing file in <paramref name="containingDirectory"/> (getDirectoryPath of the
    /// containing file) whose cache key <paramref name="directoryKey"/> (<see cref="ModuleResolutionCache.DirectoryKey"/>,
    /// null without a cache) the caller computed once for all names of the file (module FE1, Track O: the same lookups).
    /// </summary>
    internal static ResolvedModuleFull? ResolveModuleName(string moduleName, string containingDirectory, string? directoryKey, CompilerOptions compilerOptions, ICompilerHost host, ModuleResolutionCache? cache, ModuleKind? resolutionMode)
    {
        if (cache is not null && cache.TryGetFromDirectoryCache(moduleName, resolutionMode, directoryKey!, out var cached))
        {
            return cached;
        }
        var moduleResolution = compilerOptions.ModuleResolution ?? compilerOptions.GetEmitModuleResolutionKind();
        var result = moduleResolution switch
        {
            ModuleResolutionKind.Bundler => BundlerModuleNameResolver(moduleName, containingDirectory, compilerOptions, host, cache, resolutionMode is { } mode ? GetConditions(compilerOptions, mode) : null),
            _ => throw new NotSupportedException($"ModuleNameResolver (module C10): moduleResolution {moduleResolution} is not ported"),
        };
        // (the per-module-name cache of non-relative names only serves node_modules lookups, which are not ported)
        cache?.SetInDirectoryCache(moduleName, resolutionMode, directoryKey!, result);
        return result;
    }

    // Module FE1 (Track O): the lists of getConditions, made once per (moduleResolution, mode); nothing writes to a
    // returned list.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(ModuleResolutionKind, ModuleKind?), List<string>> ConditionLists = new();

    /// <summary>moduleNameResolver.ts getConditions.</summary>
    private static List<string> GetConditions(CompilerOptions options, ModuleKind? resolutionMode) =>
        ConditionLists.GetOrAdd((options.GetEmitModuleResolutionKind(), resolutionMode), key => GetConditions(key.Item1, key.Item2));

    private static List<string> GetConditions(ModuleResolutionKind moduleResolution, ModuleKind? resolutionMode)
    {
        if (resolutionMode is null)
        {
            if (moduleResolution == ModuleResolutionKind.Bundler)
            {
                // bundler always uses `import` unless explicitly overridden
                resolutionMode = ModuleKind.ESNext;
            }
            else if (moduleResolution == ModuleResolutionKind.Node10)
            {
                return [];
            }
        }
        // conditions are only used by the node16/nodenext/bundler resolvers - there's no priority order in the list,
        // it's essentially a set (priority is determined by object insertion order in the object we look at).
        List<string> conditions = resolutionMode == ModuleKind.ESNext ? ["import"] : ["require"];
        // (!options.noDtsResolution: the option is not in Track O's options)
        conditions.Add("types");
        if (moduleResolution != ModuleResolutionKind.Bundler)
        {
            conditions.Add("node");
        }
        // (options.customConditions: not in Track O's options)
        return conditions;
    }

    /// <summary>moduleNameResolver.ts getNodeResolutionFeatures.</summary>
    private static NodeResolutionFeatures GetNodeResolutionFeatures(CompilerOptions options)
    {
        var features = options.GetEmitModuleResolutionKind() switch
        {
            ModuleResolutionKind.Bundler => NodeResolutionFeatures.BundlerDefault,
            _ => throw new NotSupportedException("ModuleNameResolver (module C10): only bundler resolution is ported"),
        };
        if (options.ResolvePackageJsonExports == true)
        {
            features |= NodeResolutionFeatures.Exports;
        }
        else if (options.ResolvePackageJsonExports == false)
        {
            features &= ~NodeResolutionFeatures.Exports;
        }
        if (options.ResolvePackageJsonImports == true)
        {
            features |= NodeResolutionFeatures.Imports;
        }
        else if (options.ResolvePackageJsonImports == false)
        {
            features &= ~NodeResolutionFeatures.Imports;
        }
        return features;
    }

    /// <summary>moduleNameResolver.ts bundlerModuleNameResolver.</summary>
    /// <param name="containingDirectory">getDirectoryPath(containingFile), computed by the caller (module FE1)</param>
    private static ResolvedModuleFull? BundlerModuleNameResolver(string moduleName, string containingDirectory, CompilerOptions compilerOptions, ICompilerHost host, ModuleResolutionCache? cache, List<string>? conditions)
    {
        var extensions = Extensions.TypeScript | Extensions.JavaScript | Extensions.Declaration; // (noDtsResolution: not in Track O's options)
        if (compilerOptions.GetResolveJsonModule())
        {
            extensions |= Extensions.Json;
        }
        return NodeModuleNameResolverWorker(GetNodeResolutionFeatures(compilerOptions), moduleName, containingDirectory, compilerOptions, host, cache, extensions, conditions);
    }

    /// <summary>moduleNameResolver.ts nodeModuleNameResolverWorker (isConfigLookup false; bundler, so no Node10 passes).</summary>
    private static ResolvedModuleFull? NodeModuleNameResolverWorker(NodeResolutionFeatures features, string moduleName, string containingDirectory, CompilerOptions compilerOptions, ICompilerHost host, ModuleResolutionCache? cache, Extensions extensions, List<string>? conditions)
    {
        var moduleResolution = compilerOptions.GetEmitModuleResolutionKind();
        conditions ??= GetConditions(compilerOptions,
            moduleResolution is ModuleResolutionKind.Bundler or ModuleResolutionKind.Node10
                ? null
                : (features & NodeResolutionFeatures.EsmMode) != 0 ? ModuleKind.ESNext : ModuleKind.CommonJS);
        var state = new ModuleResolutionState
        {
            Host = host,
            CompilerOptions = compilerOptions,
            PackageJsonInfoCache = cache,
            Features = features,
            Conditions = conditions,
            RequestContainingDirectory = containingDirectory,
        };
        var result = TryResolve(extensions, state);
        // (alternateResult: computed only after a node_modules package directory was found, which is not ported.)
        // createResolvedModuleWithFailedLookupLocationsHandlingSymlink: realpath only for node_modules results.
        if (result?.Value is not { } value)
        {
            return null;
        }
        return new ResolvedModuleFull(value.Resolved.Path, value.Resolved.Extension, value.IsExternalLibraryImport, value.Resolved.PackageId, value.Resolved.ResolvedUsingTsExtension == true);

        SearchResult<ResolvedAndKind>? TryResolve(Extensions extensions, ModuleResolutionState state)
        {
            // (tryLoadModuleUsingOptionalResolutionSettings: paths, baseUrl and rootDirs are not in Track O's options.)
            if (!Utilities.IsExternalModuleNameRelative(moduleName))
            {
                if ((features & NodeResolutionFeatures.Imports) != 0 && moduleName.StartsWith('#'))
                {
                    var resolved = LoadModuleFromImports(extensions, moduleName, containingDirectory, state, cache);
                    if (resolved is not null)
                    {
                        return resolved.Value is null ? null : new SearchResult<ResolvedAndKind>(new ResolvedAndKind(resolved.Value, IsExternalLibraryImport: false));
                    }
                }
                if ((features & NodeResolutionFeatures.SelfName) != 0)
                {
                    var resolved = LoadModuleFromSelfNameReference(extensions, moduleName, containingDirectory, state, cache);
                    if (resolved is not null)
                    {
                        return resolved.Value is null ? null : new SearchResult<ResolvedAndKind>(new ResolvedAndKind(resolved.Value, IsExternalLibraryImport: false));
                    }
                }
                if (moduleName.Contains(':', StringComparison.Ordinal))
                {
                    return null;
                }
                LoadModuleFromNearestNodeModulesDirectory(moduleName, containingDirectory, state);
                // (resolveFromTypeRoot: typeRoots is not in Track O's options.)
                return null;
            }
            else
            {
                var (candidate, hasNodeModulesPart) = NormalizePathForCJSResolution(containingDirectory, moduleName);
                var resolved = NodeLoadModuleByRelativeName(extensions, candidate, onlyRecordFailures: false, state, considerPackageJson: true);
                // Treat explicit "node_modules" import as an external library import.
                return resolved is null ? null : new SearchResult<ResolvedAndKind>(new ResolvedAndKind(resolved, hasNodeModulesPart));
            }
        }
    }

    private sealed record ResolvedAndKind(Resolved Resolved, bool IsExternalLibraryImport);

    /// <summary>
    /// moduleNameResolver.ts loadModuleFromNearestNodeModulesDirectory: for each ancestor directory (not itself named
    /// node_modules) the lookup starts by checking whether its node_modules folder exists. The repositories' dependencies
    /// are not installed, so none does; a lookup that would find one is not ported.
    /// </summary>
    private static void LoadModuleFromNearestNodeModulesDirectory(string moduleName, string directory, ModuleResolutionState state)
    {
        // Module FE1 (Track O): the walk's outcome depends only on the directory (every ancestor's existence check is
        // answered from the host's cache), and a walk that returns found no node_modules folder; so a directory walked
        // once is not walked again (sentry: about 600,000 existence checks for 2,500 directories).
        var cache = state.PackageJsonInfoCache;
        if (cache is not null && cache.NodeModulesWalked(directory))
        {
            return;
        }
        WalkAncestors(moduleName, directory, state);
        cache?.SetNodeModulesWalked(directory);
    }

    private static void WalkAncestors(string moduleName, string directory, ModuleResolutionState state)
    {
        ForEachAncestorDirectory(TsPath.NormalizeSlashes(directory), ancestorDirectory =>
        {
            if (TsPath.GetBaseFileName(ancestorDirectory) != "node_modules")
            {
                var nodeModulesFolder = TsPath.CombinePaths(ancestorDirectory, "node_modules");
                if (DirectoryProbablyExists(nodeModulesFolder, state.Host))
                {
                    throw new NotSupportedException($"ModuleNameResolver (module C10): node_modules lookups are not ported ('{moduleName}' from '{directory}' finds {nodeModulesFolder})");
                }
            }
            return (object?)null;
        });
    }

    // If you import from "." inside a containing directory "/foo", the result of `normalizePath`
    // would be "/foo", but this loses the information that `foo` is a directory and we intended
    // to look inside of it. The Node CommonJS resolution algorithm doesn't call this out
    // (https://nodejs.org/api/modules.html#all-together), but it seems that module paths ending
    // in `.` are actually normalized to `./` before proceeding with the resolution algorithm.
    // Module FE1 (Track O): the path components of getPathComponents are not built; only the two facts the callers read
    // are computed from them (the last component is "." or "..", some component is "node_modules"), the same values
    // (tsrs-only shape: tsrs's #146, normalizePathForCJSResolution tests the last component without the list,
    // notes/perf-front-end-fixed-costs.md).
    private static (string Path, bool HasNodeModulesPart) NormalizePathForCJSResolution(string containingDirectory, string moduleName)
    {
        var combined = TsPath.CombinePaths(containingDirectory, moduleName);
        var (lastIsDot, hasNodeModules) = ScanPathComponents(combined);
        var path = lastIsDot ? TsPath.EnsureTrailingDirectorySeparator(TsPath.NormalizePath(combined)) : TsPath.NormalizePath(combined);
        return (path, hasNodeModules);
    }

    /// <summary>
    /// Of <see cref="GetPathComponents"/>(path): whether its last element is "." or ".." and whether some element is
    /// "node_modules" (the root element is neither: it is empty or ends in a separator or ':').
    /// </summary>
    private static (bool LastIsDot, bool HasNodeModules) ScanPathComponents(string path)
    {
        var rootLength = TsPath.GetRootLength(path);
        var rest = path.AsSpan(rootLength);
        // the elements of rest.Split('/'), without the last one when it is empty
        var hasNodeModules = false;
        var last = ReadOnlySpan<char>.Empty;
        var count = 0;
        while (true)
        {
            var slash = rest.IndexOf('/');
            var element = slash < 0 ? rest : rest[..slash];
            if (slash < 0 && element.Length == 0 && count > 0)
            {
                break; // the trailing empty element is dropped
            }
            if (slash < 0 && element.Length == 0)
            {
                // rest is empty: Split gives [""], which is dropped, so only the root remains
                return (false, false);
            }
            count++;
            last = element;
            hasNodeModules |= element.SequenceEqual("node_modules");
            if (slash < 0)
            {
                break;
            }
            rest = rest[(slash + 1)..];
        }
        return (last.SequenceEqual(".") || last.SequenceEqual(".."), hasNodeModules);
    }

    private static Resolved? NodeLoadModuleByRelativeName(Extensions extensions, string candidate, bool onlyRecordFailures, ModuleResolutionState state, bool considerPackageJson)
    {
        if (!TsPath.HasTrailingDirectorySeparator(candidate))
        {
            if (!onlyRecordFailures)
            {
                var parentOfCandidate = DirectoryPathSpan(candidate);
                if (!state.Host.DirectoryExists(parentOfCandidate))
                {
                    onlyRecordFailures = true;
                }
            }
            var resolvedFromFile = LoadModuleFromFile(extensions, candidate, onlyRecordFailures, state);
            if (resolvedFromFile is not null)
            {
                var packageDirectory = considerPackageJson ? ParseNodeModuleFromPath(resolvedFromFile.Path) : null;
                var packageInfo = packageDirectory is not null ? GetPackageJsonInfo(packageDirectory, onlyRecordFailures: false, state) : null;
                return WithPackageId(packageInfo, resolvedFromFile);
            }
        }
        if (!onlyRecordFailures)
        {
            var candidateExists = DirectoryProbablyExists(candidate, state.Host);
            if (!candidateExists)
            {
                onlyRecordFailures = true;
            }
        }
        // esm mode relative imports shouldn't do any directory lookups (either inside `package.json`
        // files or implicit `index.js`es). This is a notable departure from cjs norms, where `./foo/pkg`
        // could have been redirected by `./foo/pkg/package.json` to an arbitrary location!
        if ((state.Features & NodeResolutionFeatures.EsmMode) == 0)
        {
            return LoadNodeModuleFromDirectory(extensions, candidate, onlyRecordFailures, state, considerPackageJson);
        }
        return null;
    }

    private const string NodeModulesPathPart = "/node_modules/";

    /// <summary>moduleNameResolver.ts parseNodeModuleFromPath.</summary>
    private static string? ParseNodeModuleFromPath(string resolved, bool isFolder = false)
    {
        var path = TsPath.NormalizePath(resolved);
        var idx = path.LastIndexOf(NodeModulesPathPart, StringComparison.Ordinal);
        if (idx == -1)
        {
            return null;
        }
        var indexAfterNodeModules = idx + NodeModulesPathPart.Length;
        var indexAfterPackageName = MoveToNextDirectorySeparatorIfAvailable(path, indexAfterNodeModules, isFolder);
        if (indexAfterNodeModules < path.Length && path[indexAfterNodeModules] == '@')
        {
            indexAfterPackageName = MoveToNextDirectorySeparatorIfAvailable(path, indexAfterPackageName, isFolder);
        }
        return path[..indexAfterPackageName];
    }

    private static int MoveToNextDirectorySeparatorIfAvailable(string path, int prevSeparatorIndex, bool isFolder)
    {
        var nextSeparatorIndex = prevSeparatorIndex + 1 <= path.Length ? path.IndexOf('/', prevSeparatorIndex + 1) : -1;
        return nextSeparatorIndex == -1 ? isFolder ? path.Length : prevSeparatorIndex : nextSeparatorIndex;
    }

    private static Resolved? WithPackageId(PackageJsonInfo? packageInfo, PathAndExtension? r)
    {
        if (r is null)
        {
            return null;
        }
        PackageId? packageId = null;
        if (packageInfo is not null
            && TryGetString(packageInfo.Content, "name", out var name)
            && TryGetString(packageInfo.Content, "version", out var version))
        {
            // (peerDependencies: read for node_modules packages only, not ported)
            packageId = new PackageId(name, r.Path[(packageInfo.PackageDirectory.Length + 1)..], version);
        }
        return new Resolved(r.Path, r.Ext, packageId, r.ResolvedUsingTsExtension);
    }

    private static Resolved? NoPackageId(PathAndExtension? r) => WithPackageId(null, r);

    private static PathAndExtension? RemoveIgnoredPackageId(Resolved? r) => r is null ? null : new PathAndExtension(r.Path, r.Extension, r.ResolvedUsingTsExtension);

    /// <param name="onlyRecordFailures">if true then function won't try to actually load files but instead record all attempts as failures.</param>
    private static PathAndExtension? LoadModuleFromFile(Extensions extensions, string candidate, bool onlyRecordFailures, ModuleResolutionState state)
    {
        // ./foo.js -> ./foo.ts
        var resolvedByReplacingExtension = LoadModuleFromFileNoImplicitExtensions(extensions, candidate, onlyRecordFailures, state);
        if (resolvedByReplacingExtension is not null)
        {
            return resolvedByReplacingExtension;
        }
        // ./foo -> ./foo.ts
        if ((state.Features & NodeResolutionFeatures.EsmMode) == 0)
        {
            // First, try adding an extension. An import of "foo" could be matched by a file "foo.ts", or "foo.js" by "foo.js.ts"
            var resolvedByAddingExtension = TryAddingExtensions(candidate, extensions, "", onlyRecordFailures, state);
            if (resolvedByAddingExtension is not null)
            {
                return resolvedByAddingExtension;
            }
        }
        return null;
    }

    private static PathAndExtension? LoadModuleFromFileNoImplicitExtensions(Extensions extensions, string candidate, bool onlyRecordFailures, ModuleResolutionState state)
    {
        var filename = TsPath.GetBaseFileName(candidate);
        if (!filename.Contains('.', StringComparison.Ordinal))
        {
            return null; // extensionless import, no lookups performed, since we don't support extensionless files
        }
        var extensionless = RemoveFileExtension(candidate);
        if (extensionless == candidate)
        {
            // Once TS native extensions are handled, handle arbitrary extensions for declaration file mapping
            extensionless = candidate[..candidate.LastIndexOf('.')];
        }
        var extension = candidate[extensionless.Length..];
        return TryAddingExtensions(extensionless, extensions, extension, onlyRecordFailures, state);
    }

    /// <summary>
    /// This function is only ever called with paths written in package.json files - never
    /// module specifiers written in source files - and so it always allows the
    /// candidate to end with a TS extension (but will also try substituting a JS extension for a TS extension).
    /// </summary>
    private static PathAndExtension? LoadFileNameFromPackageJsonField(Extensions extensions, string candidate, string? packageJsonValue, bool onlyRecordFailures, ModuleResolutionState state)
    {
        if (((extensions & Extensions.TypeScript) != 0 && FileExtensionIsOneOf(candidate, SupportedTSImplementationExtensions))
            || ((extensions & Extensions.Declaration) != 0 && FileExtensionIsOneOf(candidate, Utilities.SupportedDeclarationExtensions)))
        {
            var result = TryFile(candidate, onlyRecordFailures, state);
            var ext = TryExtractTSExtension(candidate)!;
            return result is not null ? new PathAndExtension(candidate, ext, packageJsonValue is not null ? !packageJsonValue.EndsWith(ext, StringComparison.Ordinal) : null) : null;
        }
        // (isConfigLookup: never)
        return LoadModuleFromFileNoImplicitExtensions(extensions, candidate, onlyRecordFailures, state);
    }

    /// <summary>Try to return an existing file that adds one of the `extensions` to `candidate`.</summary>
    private static PathAndExtension? TryAddingExtensions(string candidate, Extensions extensions, string originalExtension, bool onlyRecordFailures, ModuleResolutionState state)
    {
        if (!onlyRecordFailures)
        {
            // check if containing folder exists - if it doesn't then just record failures for all supported extensions without disk probing
            var directory = DirectoryPathSpan(candidate);
            if (directory.Length > 0)
            {
                onlyRecordFailures = !state.Host.DirectoryExists(directory);
            }
        }
        switch (originalExtension)
        {
            case Extension.Mjs:
            case Extension.Mts:
            case Extension.Dmts:
                return ((extensions & Extensions.TypeScript) != 0 ? TryExtension(Extension.Mts, originalExtension is Extension.Mts or Extension.Dmts) : null)
                    ?? ((extensions & Extensions.Declaration) != 0 ? TryExtension(Extension.Dmts, originalExtension is Extension.Mts or Extension.Dmts) : null)
                    ?? ((extensions & Extensions.JavaScript) != 0 ? TryExtension(Extension.Mjs) : null);
            case Extension.Cjs:
            case Extension.Cts:
            case Extension.Dcts:
                return ((extensions & Extensions.TypeScript) != 0 ? TryExtension(Extension.Cts, originalExtension is Extension.Cts or Extension.Dcts) : null)
                    ?? ((extensions & Extensions.Declaration) != 0 ? TryExtension(Extension.Dcts, originalExtension is Extension.Cts or Extension.Dcts) : null)
                    ?? ((extensions & Extensions.JavaScript) != 0 ? TryExtension(Extension.Cjs) : null);
            case Extension.Json:
                return ((extensions & Extensions.Declaration) != 0 ? TryExtension(".d.json.ts") : null)
                    ?? ((extensions & Extensions.Json) != 0 ? TryExtension(Extension.Json) : null);
            case Extension.Tsx:
            case Extension.Jsx:
                // basically idendical to the ts/js case below, but prefers matching tsx and jsx files exactly before falling back to the ts or js file path
                // (historically, we disallow having both a a.ts and a.tsx file in the same compilation, since their outputs clash)
                return ((extensions & Extensions.TypeScript) != 0 ? TryExtension(Extension.Tsx, originalExtension == Extension.Tsx) ?? TryExtension(Extension.Ts, originalExtension == Extension.Tsx) : null)
                    ?? ((extensions & Extensions.Declaration) != 0 ? TryExtension(Extension.Dts, originalExtension == Extension.Tsx) : null)
                    ?? ((extensions & Extensions.JavaScript) != 0 ? TryExtension(Extension.Jsx) ?? TryExtension(Extension.Js) : null);
            case Extension.Ts:
            case Extension.Dts:
            case Extension.Js:
            case "":
                return ((extensions & Extensions.TypeScript) != 0 ? TryExtension(Extension.Ts, originalExtension is Extension.Ts or Extension.Dts) ?? TryExtension(Extension.Tsx, originalExtension is Extension.Ts or Extension.Dts) : null)
                    ?? ((extensions & Extensions.Declaration) != 0 ? TryExtension(Extension.Dts, originalExtension is Extension.Ts or Extension.Dts) : null)
                    ?? ((extensions & Extensions.JavaScript) != 0 ? TryExtension(Extension.Js) ?? TryExtension(Extension.Jsx) : null);
            // (state.isConfigLookup && tryExtension(".json"): never)
            default:
                return (extensions & Extensions.Declaration) != 0 && !Parser.IsDeclarationFileName(candidate + originalExtension) ? TryExtension(".d" + originalExtension + ".ts") : null;
        }

        PathAndExtension? TryExtension(string ext, bool resolvedUsingTsExtension = false)
        {
            var path = TryFile(candidate, ext, onlyRecordFailures, state);
            return path is null ? null : new PathAndExtension(path, ext, !state.CandidateIsFromPackageJsonField && resolvedUsingTsExtension);
        }
    }

    [ThreadStatic]
    private static char[]? pathBuffer;

    /// <summary>
    /// <see cref="TryFile(string, bool, ModuleResolutionState)"/> of <c>candidate + ext</c>. Module FE1 (Track O): the
    /// name is built in a per-thread buffer and is not made a string when the file is known (tsrs-only shape:
    /// tsrs_module/src/resolver.rs try_extension with scratch.rs with_path_buffer, notes/perf-resolver-allocations.md).
    /// </summary>
    private static string? TryFile(string candidate, string ext, bool onlyRecordFailures, ModuleResolutionState state)
    {
        if (onlyRecordFailures)
        {
            return null;
        }
        var length = candidate.Length + ext.Length;
        var buffer = pathBuffer;
        if (buffer is null || buffer.Length < length)
        {
            pathBuffer = buffer = new char[Math.Max(length, 512)];
        }
        candidate.CopyTo(buffer);
        ext.CopyTo(buffer.AsSpan(candidate.Length));
        return state.Host.ExistingFileName(buffer.AsSpan(0, length));
    }

    /// <summary>Return the file if it exists (moduleSuffixes is not in Track O's options: tryFileLookup).</summary>
    private static string? TryFile(string fileName, bool onlyRecordFailures, ModuleResolutionState state)
    {
        if (!onlyRecordFailures && state.Host.FileExists(fileName))
        {
            return fileName;
        }
        // (failedLookupLocations: watch mode only)
        return null;
    }

    private static Resolved? LoadNodeModuleFromDirectory(Extensions extensions, string candidate, bool onlyRecordFailures, ModuleResolutionState state, bool considerPackageJson = true)
    {
        var packageInfo = considerPackageJson ? GetPackageJsonInfo(candidate, onlyRecordFailures, state) : null;
        return WithPackageId(packageInfo, LoadNodeModuleFromDirectoryWorker(extensions, candidate, onlyRecordFailures, state, packageInfo));
    }

    /// <summary>moduleNameResolver.ts getPackageScopeForPath.</summary>
    private static PackageJsonInfo? GetPackageScopeForPath(string directory, ModuleResolutionState state)
    {
        // (forEachAncestorDirectoryStoppingAtGlobalCache: the host has no global typings cache)
        // Module FE1 (Track O): the scope of a directory is a function of the directory (every package.json lookup of
        // the walk is answered from the cache, and the same directory names give the same objects), so it is computed
        // once per directory (sentry: every non-relative name walked up to the repository's package.json).
        if (state.PackageJsonInfoCache is { } cache)
        {
            return cache.GetOrAddPackageScope(directory, d => ForEachAncestorDirectory(d, dir => GetPackageJsonInfo(dir, onlyRecordFailures: false, state)));
        }
        return ForEachAncestorDirectory(directory, dir => GetPackageJsonInfo(dir, onlyRecordFailures: false, state));
    }

    private static PackageJsonInfo? GetPackageJsonInfo(string packageDirectory, bool onlyRecordFailures, ModuleResolutionState state)
    {
        var host = state.Host;
        var packageJsonPath = TsPath.CombinePaths(packageDirectory, "package.json");
        if (onlyRecordFailures)
        {
            return null;
        }
        // (module FE1: the cache's entry is computed once, ModuleResolutionCache.GetOrAddPackageJsonInfo; a computed
        // PackageJsonInfo has this packageDirectory, so the first caller gets it as before)
        var entry = state.PackageJsonInfoCache is { } cache ? cache.GetOrAddPackageJsonInfo(packageJsonPath, Compute) : Compute();
        if (entry is PackageJsonInfo info)
        {
            return info.PackageDirectory == packageDirectory ? info : new PackageJsonInfo(packageDirectory, info.Content);
        }
        return null;

        PackageJsonInfoCacheEntry Compute()
        {
            var directoryExists = DirectoryProbablyExists(packageDirectory, host);
            if (directoryExists && host.FileExists(packageJsonPath))
            {
                return new PackageJsonInfo(packageDirectory, ReadJson(packageJsonPath, host));
            }
            // record package json as one of failed lookup locations - in the future if this file will appear it will invalidate resolution results
            return new MissingPackageJsonInfo(packageDirectory, directoryExists);
        }
    }

    private static PathAndExtension? LoadNodeModuleFromDirectoryWorker(Extensions extensions, string candidate, bool onlyRecordFailures, ModuleResolutionState state, PackageJsonInfo? packageJson)
    {
        if (packageJson is not null && HasProperty(packageJson.Content, "typesVersions"))
        {
            // getVersionPathsOfPackageJsonInfo (VersionRange matching): no package.json of the benchmark repositories has one.
            throw new NotSupportedException($"ModuleNameResolver (module C10): typesVersions is not ported ({packageJson.PackageDirectory}/package.json)");
        }
        string? packageFile = null;
        if (packageJson is not null && ArePathsEqual(packageJson.PackageDirectory, candidate, state.Host))
        {
            // (isConfigLookup: never)
            packageFile = ((extensions & Extensions.Declaration) != 0 ? ReadPackageJsonTypesFields(packageJson.Content, packageJson.PackageDirectory) : null)
                ?? ((extensions & (Extensions.ImplementationFiles | Extensions.Declaration)) != 0 ? ReadPackageJsonPathField(packageJson.Content, "main", packageJson.PackageDirectory) : null);
        }

        var onlyRecordFailuresForPackageFile = packageFile is not null && !DirectoryProbablyExists(GetDirectoryPath(packageFile), state.Host);
        var onlyRecordFailuresForIndex = onlyRecordFailures || !DirectoryProbablyExists(candidate, state.Host);
        var indexPath = TsPath.CombinePaths(candidate, "index");

        // (versionPaths: see above)
        // It won't have a `packageId` set, because we disabled `considerPackageJson`.
        var packageFileResult = packageFile is not null ? RemoveIgnoredPackageId(Loader(extensions, packageFile, onlyRecordFailuresForPackageFile, state)) : null;
        if (packageFileResult is not null)
        {
            return packageFileResult;
        }
        // esm mode resolutions don't do package `index` lookups
        if ((state.Features & NodeResolutionFeatures.EsmMode) == 0)
        {
            return LoadModuleFromFile(extensions, indexPath, onlyRecordFailuresForIndex, state);
        }
        return null;

        Resolved? Loader(Extensions extensions, string candidate, bool onlyRecordFailures, ModuleResolutionState state)
        {
            var fromFile = LoadFileNameFromPackageJsonField(extensions, candidate, packageJsonValue: null, onlyRecordFailures, state);
            if (fromFile is not null)
            {
                return NoPackageId(fromFile);
            }
            // Even if extensions is DtsOnly, we can still look up a .ts file as a result of package.json "types"
            var expandedExtensions = extensions == Extensions.Declaration ? Extensions.TypeScript | Extensions.Declaration : extensions;
            // Don't do package.json lookup recursively, because Node.js' package lookup doesn't.

            // Disable `EsmMode` for the resolution of the package path for cjs-mode packages (so the `main` field can omit extensions)
            // (technically it only emits a deprecation warning in esm packages right now, but that's probably
            // enough to mean we don't need to support it)
            var features = state.Features;
            var candidateIsFromPackageJsonField = state.CandidateIsFromPackageJsonField;
            state.CandidateIsFromPackageJsonField = true;
            if (!(TryGetString(packageJson?.Content ?? default, "type", out var type) && type == "module"))
            {
                state.Features &= ~NodeResolutionFeatures.EsmMode;
            }
            var result = NodeLoadModuleByRelativeName(expandedExtensions, candidate, onlyRecordFailures, state, considerPackageJson: false);
            state.Features = features;
            state.CandidateIsFromPackageJsonField = candidateIsFromPackageJsonField;
            return result;
        }
    }

    /// <summary>True if `extension` is one of the supported `extensions`.</summary>
    private static bool ExtensionIsOk(Extensions extensions, string extension)
    {
        return ((extensions & Extensions.JavaScript) != 0 && extension is Extension.Js or Extension.Jsx or Extension.Mjs or Extension.Cjs)
            || ((extensions & Extensions.TypeScript) != 0 && extension is Extension.Ts or Extension.Tsx or Extension.Mts or Extension.Cts)
            || ((extensions & Extensions.Declaration) != 0 && extension is Extension.Dts or Extension.Dmts or Extension.Dcts)
            || ((extensions & Extensions.Json) != 0 && extension == Extension.Json);
    }

    private static SearchResult<Resolved>? LoadModuleFromSelfNameReference(Extensions extensions, string moduleName, string directory, ModuleResolutionState state, ModuleResolutionCache? cache)
    {
        var directoryPath = TsPath.GetNormalizedAbsolutePath(directory, state.Host.GetCurrentDirectory());
        var scope = GetPackageScopeForPath(directoryPath, state);
        if (scope is null || !IsTruthy(GetProperty(scope.Content, "exports")))
        {
            return null;
        }
        if (!TryGetString(scope.Content, "name", out var name))
        {
            return null;
        }
        var parts = GetPathComponents(moduleName); // unrooted paths should have `""` as their 0th entry
        var nameParts = GetPathComponents(name);
        for (var i = 0; i < nameParts.Count; i++)
        {
            if (i >= parts.Count || parts[i] != nameParts[i])
            {
                return null;
            }
        }
        var trailingParts = parts.GetRange(nameParts.Count, parts.Count - nameParts.Count);
        var subpath = trailingParts.Count == 0 ? "." : "./" + string.Join('/', trailingParts);
        // (allowJs with a self-name outside node_modules: one pass with all extensions, for tryLoadInputFileForPath)
        if (state.CompilerOptions.GetAllowJSCompilerOption() && !directory.Contains(NodeModulesPathPart, StringComparison.Ordinal))
        {
            return LoadModuleFromExports(scope, extensions, subpath, state, cache);
        }
        var priorityExtensions = extensions & (Extensions.TypeScript | Extensions.Declaration);
        var secondaryExtensions = extensions & ~(Extensions.TypeScript | Extensions.Declaration);
        return LoadModuleFromExports(scope, priorityExtensions, subpath, state, cache)
            ?? LoadModuleFromExports(scope, secondaryExtensions, subpath, state, cache);
    }

    private static SearchResult<Resolved>? LoadModuleFromExports(PackageJsonInfo scope, Extensions extensions, string subpath, ModuleResolutionState state, ModuleResolutionCache? cache)
    {
        var exports = GetProperty(scope.Content, "exports");
        if (!IsTruthy(exports))
        {
            return null;
        }
        if (subpath == ".")
        {
            JsonElement mainExport = default;
            if (exports.ValueKind is JsonValueKind.String or JsonValueKind.Array || (exports.ValueKind == JsonValueKind.Object && NoKeyStartsWithDot(exports)))
            {
                mainExport = exports;
            }
            else if (exports.ValueKind == JsonValueKind.Object && exports.TryGetProperty(".", out var dot))
            {
                mainExport = dot;
            }
            if (IsTruthy(mainExport))
            {
                return LoadModuleFromTargetExportOrImport(extensions, state, cache, subpath, scope, isImports: false, mainExport, "", pattern: false, ".");
            }
        }
        else if (AllKeysStartWithDot(exports))
        {
            if (exports.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                return ToSearchResult<Resolved>(null);
            }
            var result = LoadModuleFromExportsOrImports(extensions, state, cache, subpath, exports, scope, isImports: false);
            if (result is not null)
            {
                return result;
            }
        }
        return ToSearchResult<Resolved>(null);
    }

    private static SearchResult<Resolved>? LoadModuleFromImports(Extensions extensions, string moduleName, string directory, ModuleResolutionState state, ModuleResolutionCache? cache)
    {
        if (moduleName == "#" || (moduleName.StartsWith("#/", StringComparison.Ordinal) && (state.Features & NodeResolutionFeatures.ImportsPatternRoot) == 0))
        {
            return ToSearchResult<Resolved>(null);
        }
        var directoryPath = TsPath.GetNormalizedAbsolutePath(directory, state.Host.GetCurrentDirectory());
        var scope = GetPackageScopeForPath(directoryPath, state);
        if (scope is null)
        {
            return ToSearchResult<Resolved>(null);
        }
        var imports = GetProperty(scope.Content, "imports");
        if (!IsTruthy(imports))
        {
            return ToSearchResult<Resolved>(null);
        }
        var result = LoadModuleFromExportsOrImports(extensions, state, cache, moduleName, imports, scope, isImports: true);
        if (result is not null)
        {
            return result;
        }
        return ToSearchResult<Resolved>(null);
    }

    /// <summary>
    /// From https://github.com/nodejs/node/blob/8f39f51cbbd3b2de14b9ee896e26421cc5b20121/lib/internal/modules/esm/resolve.js#L722 -
    /// "longest" has some nuance as to what "longest" means in the presence of pattern trailers
    /// </summary>
    private static int ComparePatternKeys(string a, string b)
    {
        var aPatternIndex = a.IndexOf('*', StringComparison.Ordinal);
        var bPatternIndex = b.IndexOf('*', StringComparison.Ordinal);
        var baseLenA = aPatternIndex == -1 ? a.Length : aPatternIndex + 1;
        var baseLenB = bPatternIndex == -1 ? b.Length : bPatternIndex + 1;
        if (baseLenA > baseLenB)
        {
            return -1;
        }
        if (baseLenB > baseLenA)
        {
            return 1;
        }
        if (aPatternIndex == -1)
        {
            return 1;
        }
        if (bPatternIndex == -1)
        {
            return -1;
        }
        if (a.Length > b.Length)
        {
            return -1;
        }
        if (b.Length > a.Length)
        {
            return 1;
        }
        return 0;
    }

    private static SearchResult<Resolved>? LoadModuleFromExportsOrImports(Extensions extensions, ModuleResolutionState state, ModuleResolutionCache? cache, string moduleName, JsonElement lookupTable, PackageJsonInfo scope, bool isImports)
    {
        if (!moduleName.EndsWith('/') && !moduleName.Contains('*', StringComparison.Ordinal) && HasProperty(lookupTable, moduleName))
        {
            var target = lookupTable.GetProperty(moduleName);
            return LoadModuleFromTargetExportOrImport(extensions, state, cache, moduleName, scope, isImports, target, subpath: "", pattern: false, moduleName);
        }
        // toSorted(filter(getOwnKeys(lookupTable), k => hasOneAsterisk(k) || endsWith(k, "/")), comparePatternKeys): a stable sort
        var keys = new List<string>();
        if (lookupTable.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in lookupTable.EnumerateObject())
            {
                if (HasOneAsterisk(property.Name) || property.Name.EndsWith('/'))
                {
                    keys.Add(property.Name);
                }
            }
        }
        var order = new int[keys.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }
        Array.Sort(order, (x, y) => ComparePatternKeys(keys[x], keys[y]) is var c && c != 0 ? c : x.CompareTo(y));
        foreach (var index in order)
        {
            var potentialTarget = keys[index];
            if ((state.Features & NodeResolutionFeatures.ExportsPatternTrailers) != 0 && MatchesPatternWithTrailer(potentialTarget, moduleName))
            {
                var target = lookupTable.GetProperty(potentialTarget);
                var starPos = potentialTarget.IndexOf('*', StringComparison.Ordinal);
                var subpath = moduleName.Substring(starPos, moduleName.Length - (potentialTarget.Length - 1 - starPos) - starPos);
                return LoadModuleFromTargetExportOrImport(extensions, state, cache, moduleName, scope, isImports, target, subpath, pattern: true, potentialTarget);
            }
            else if (potentialTarget.EndsWith('*') && moduleName.StartsWith(potentialTarget[..^1], StringComparison.Ordinal))
            {
                var target = lookupTable.GetProperty(potentialTarget);
                var subpath = moduleName[(potentialTarget.Length - 1)..];
                return LoadModuleFromTargetExportOrImport(extensions, state, cache, moduleName, scope, isImports, target, subpath, pattern: true, potentialTarget);
            }
            else if (moduleName.StartsWith(potentialTarget, StringComparison.Ordinal))
            {
                var target = lookupTable.GetProperty(potentialTarget);
                var subpath = moduleName[potentialTarget.Length..];
                return LoadModuleFromTargetExportOrImport(extensions, state, cache, moduleName, scope, isImports, target, subpath, pattern: false, potentialTarget);
            }
        }
        return null;

        static bool MatchesPatternWithTrailer(string target, string name)
        {
            if (target.EndsWith('*'))
            {
                return false; // handled by next case in loop
            }
            var starPos = target.IndexOf('*', StringComparison.Ordinal);
            if (starPos == -1)
            {
                return false; // handled by last case in loop
            }
            return name.StartsWith(target[..starPos], StringComparison.Ordinal) && name.EndsWith(target[(starPos + 1)..], StringComparison.Ordinal);
        }
    }

    private static bool HasOneAsterisk(string patternKey)
    {
        var firstStar = patternKey.IndexOf('*', StringComparison.Ordinal);
        return firstStar != -1 && firstStar == patternKey.LastIndexOf('*');
    }

    /// <summary>moduleNameResolver.ts getLoadModuleFromTargetExportOrImport / loadModuleFromTargetExportOrImport.</summary>
    private static SearchResult<Resolved>? LoadModuleFromTargetExportOrImport(Extensions extensions, ModuleResolutionState state, ModuleResolutionCache? cache, string moduleName, PackageJsonInfo scope, bool isImports, JsonElement target, string subpath, bool pattern, string key)
    {
        if (target.ValueKind == JsonValueKind.String)
        {
            var targetText = target.GetString()!;
            if (!pattern && subpath.Length > 0 && !targetText.EndsWith('/'))
            {
                return ToSearchResult<Resolved>(null);
            }
            if (!targetText.StartsWith("./", StringComparison.Ordinal))
            {
                if (isImports && !targetText.StartsWith("../", StringComparison.Ordinal) && !targetText.StartsWith('/') && TsPath.GetRootLength(targetText) == 0)
                {
                    var combinedLookup = pattern ? targetText.Replace("*", subpath, StringComparison.Ordinal) : targetText + subpath;
                    var result = NodeModuleNameResolverWorker(state.Features, combinedLookup, scope.PackageDirectory + "/", state.CompilerOptions, state.Host, cache, extensions, state.Conditions);
                    return ToSearchResult(result is null ? null : new Resolved(result.ResolvedFileName, result.Extension, result.PackageId, result.ResolvedUsingTsExtension));
                }
                return ToSearchResult<Resolved>(null);
            }
            var parts = PathIsRelative(targetText) ? GetPathComponents(targetText).GetRange(1, GetPathComponents(targetText).Count - 1) : GetPathComponents(targetText);
            var partsAfterFirst = parts.Count > 1 ? parts.GetRange(1, parts.Count - 1) : [];
            if (partsAfterFirst.Contains("..") || partsAfterFirst.Contains(".") || partsAfterFirst.Contains("node_modules"))
            {
                return ToSearchResult<Resolved>(null);
            }
            var resolvedTarget = TsPath.CombinePaths(scope.PackageDirectory, targetText);
            // TODO: Assert that `resolvedTarget` is actually within the package directory? That's what the spec says.... but I'm not sure we need
            // to be in the business of validating everyone's import and export map correctness.
            var subpathParts = GetPathComponents(subpath);
            if (subpathParts.Contains("..") || subpathParts.Contains(".") || subpathParts.Contains("node_modules"))
            {
                return ToSearchResult<Resolved>(null);
            }
            var finalPath = TsPath.GetNormalizedAbsolutePath(pattern ? resolvedTarget.Replace("*", subpath, StringComparison.Ordinal) : resolvedTarget + subpath, state.Host.GetCurrentDirectory());
            // (tryLoadInputFileForPath: needs declarationDir or outDir, not in Track O's options)
            return ToSearchResult(WithPackageId(scope, LoadFileNameFromPackageJsonField(extensions, finalPath, targetText, onlyRecordFailures: false, state)));
        }
        else if (target.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            if (target.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in target.EnumerateObject())
                {
                    var condition = property.Name;
                    if (condition == "default" || state.Conditions.Contains(condition) || IsApplicableVersionedTypesKey(state.Conditions, condition))
                    {
                        var result = LoadModuleFromTargetExportOrImport(extensions, state, cache, moduleName, scope, isImports, property.Value, subpath, pattern, key);
                        if (result is not null)
                        {
                            return result;
                        }
                    }
                }
                return null;
            }
            if (target.GetArrayLength() == 0)
            {
                return ToSearchResult<Resolved>(null);
            }
            foreach (var elem in target.EnumerateArray())
            {
                var result = LoadModuleFromTargetExportOrImport(extensions, state, cache, moduleName, scope, isImports, elem, subpath, pattern, key);
                if (result is not null)
                {
                    return result;
                }
            }
        }
        else if (target.ValueKind == JsonValueKind.Null)
        {
            return new SearchResult<Resolved>(null);
        }
        return ToSearchResult<Resolved>(null);
    }

    /// <summary>moduleNameResolver.ts isApplicableVersionedTypesKey (`types@&lt;range&gt;` keys need VersionRange, not ported).</summary>
    private static bool IsApplicableVersionedTypesKey(List<string> conditions, string key)
    {
        if (!conditions.Contains("types"))
        {
            return false; // only apply versioned types conditions if the types condition is applied
        }
        if (!key.StartsWith("types@", StringComparison.Ordinal))
        {
            return false;
        }
        throw new NotSupportedException($"ModuleNameResolver (module C10): versioned types conditions are not ported ('{key}')");
    }

    // ---- package.json reading (moduleNameResolver.ts readPackageJsonField and friends, utilities.ts readJson) ----

    /// <summary>
    /// utilities.ts readJson: JSON.parse first, then the loose parser (comments, trailing commas); a file that does not
    /// parse is <c>{}</c>. A root that is not an object has no fields for the resolver either.
    /// </summary>
    private static JsonElement ReadJson(string path, ICompilerHost host)
    {
        var text = host.ReadFile(path);
        if (string.IsNullOrEmpty(text))
        {
            return default;
        }
        foreach (var options in JsonOptions)
        {
            try
            {
                using var document = JsonDocument.Parse(text, options);
                return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : default;
            }
            catch (JsonException)
            {
            }
        }
        return default;
    }

    private static readonly JsonDocumentOptions[] JsonOptions =
    [
        new JsonDocumentOptions { MaxDepth = 1024 },
        new JsonDocumentOptions { MaxDepth = 1024, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip },
    ];

    private static JsonElement GetProperty(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value) ? value : default;

    private static bool HasProperty(JsonElement json, string name) => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out _);

    // readPackageJsonField(json, name, "string"): the value if it is a string.
    private static bool TryGetString(JsonElement json, string name, out string value)
    {
        var property = GetProperty(json, name);
        if (property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString()!;
            return true;
        }
        value = "";
        return false;
    }

    // JavaScript truthiness of a JSON value (undefined: ValueKind Undefined).
    private static bool IsTruthy(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.False => false,
        JsonValueKind.String => value.GetString()!.Length > 0,
        JsonValueKind.Number => value.GetDouble() != 0,
        _ => true,
    };

    // allKeysStartWithDot(obj): every(getOwnKeys(obj), k => startsWith(k, ".")) (a string's or an array's keys are its indices)
    private static bool AllKeysStartWithDot(JsonElement obj)
    {
        switch (obj.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in obj.EnumerateObject())
                {
                    if (!property.Name.StartsWith('.'))
                    {
                        return false;
                    }
                }
                return true;
            case JsonValueKind.String:
                return obj.GetString()!.Length == 0;
            case JsonValueKind.Array:
                return obj.GetArrayLength() == 0;
            default:
                return true;
        }
    }

    private static bool NoKeyStartsWithDot(JsonElement obj)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (property.Name.StartsWith('.'))
            {
                return false;
            }
        }
        return true;
    }

    private static string? ReadPackageJsonPathField(JsonElement jsonContent, string fieldName, string baseDirectory)
    {
        if (!TryGetString(jsonContent, fieldName, out var fileName) || fileName.Length == 0)
        {
            return null;
        }
        return TsPath.NormalizePath(TsPath.CombinePaths(baseDirectory, fileName));
    }

    private static string? ReadPackageJsonTypesFields(JsonElement jsonContent, string baseDirectory)
    {
        return ReadPackageJsonPathField(jsonContent, "typings", baseDirectory) ?? ReadPackageJsonPathField(jsonContent, "types", baseDirectory);
    }

    // ---- utilities.ts and path.ts helpers ----

    private static readonly string[] SupportedTSImplementationExtensions = [Extension.Ts, Extension.Cts, Extension.Mts, Extension.Tsx];

    private static readonly string[] SupportedTSExtensionsForExtractExtension = [Extension.Dts, Extension.Dcts, Extension.Dmts, Extension.Cts, Extension.Mts, Extension.Ts, Extension.Tsx];

    private static readonly string[] ExtensionsToRemove = [Extension.Dts, Extension.Dmts, Extension.Dcts, Extension.Mjs, Extension.Mts, Extension.Cjs, Extension.Cts, Extension.Ts, Extension.Js, Extension.Tsx, Extension.Jsx, Extension.Json];

    /// <summary>utilities.ts tryExtractTSExtension.</summary>
    internal static string? TryExtractTSExtension(string fileName)
    {
        foreach (var extension in SupportedTSExtensionsForExtractExtension)
        {
            if (TsPath.FileExtensionIs(fileName, extension))
            {
                return extension;
            }
        }
        return null;
    }

    /// <summary>utilities.ts extensionIsTS.</summary>
    internal static bool ExtensionIsTS(string ext) =>
        ext is Extension.Ts or Extension.Tsx or Extension.Dts or Extension.Cts or Extension.Mts or Extension.Dmts or Extension.Dcts
        || (ext.StartsWith(".d.", StringComparison.Ordinal) && ext.EndsWith(".ts", StringComparison.Ordinal));

    /// <summary>utilities.ts resolutionExtensionIsTSOrJson.</summary>
    internal static bool ResolutionExtensionIsTSOrJson(string ext) => ExtensionIsTS(ext) || ext == Extension.Json;

    // utilities.ts removeFileExtension (extensionsToRemove)
    private static string RemoveFileExtension(string path)
    {
        foreach (var extension in ExtensionsToRemove)
        {
            if (TsPath.FileExtensionIs(path, extension))
            {
                return path[..^extension.Length];
            }
        }
        return path;
    }

    private static bool FileExtensionIsOneOf(string path, string[] extensions)
    {
        foreach (var extension in extensions)
        {
            if (TsPath.FileExtensionIs(path, extension))
            {
                return true;
            }
        }
        return false;
    }

    // utilities.ts directoryProbablyExists (every Track O host has directoryExists)
    private static bool DirectoryProbablyExists(string directoryName, ICompilerHost host) => host.DirectoryExists(directoryName);

    // moduleNameResolver.ts arePathsEqual
    private static bool ArePathsEqual(string path1, string path2, ICompilerHost host) =>
        string.Equals(path1, path2, host.UseCaseSensitiveFileNames ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>path.ts pathIsRelative: /^\.\.?(?:$|[\\/])/.</summary>
    internal static bool PathIsRelative(string path)
    {
        if (path.Length == 0 || path[0] != '.')
        {
            return false;
        }
        var i = path.Length > 1 && path[1] == '.' ? 2 : 1;
        return i == path.Length || path[i] is '/' or '\\';
    }

    /// <summary>path.ts getDirectoryPath.</summary>
    internal static string GetDirectoryPath(string path)
    {
        path = TsPath.NormalizeSlashes(path);
        // If the path provided is itself the root, then return it.
        var rootLength = TsPath.GetRootLength(path);
        if (rootLength == path.Length)
        {
            return path;
        }
        // return the leading portion of the path up to the last (non-terminal) directory separator
        // but not including any trailing directory separator.
        path = TsPath.RemoveTrailingDirectorySeparator(path);
        return path[..Math.Max(rootLength, path.LastIndexOf('/'))];
    }

    /// <summary>
    /// <see cref="GetDirectoryPath"/> as characters of <paramref name="path"/> (module FE1, Track O: no substring for
    /// a directory whose existence is only asked); the same characters. A path with backslashes takes the string way.
    /// </summary>
    private static ReadOnlySpan<char> DirectoryPathSpan(string path)
    {
        if (path.Contains('\\', StringComparison.Ordinal))
        {
            return GetDirectoryPath(path);
        }
        var rootLength = TsPath.GetRootLength(path);
        if (rootLength == path.Length)
        {
            return path;
        }
        var span = TsPath.HasTrailingDirectorySeparator(path) ? path.AsSpan(0, path.Length - 1) : path.AsSpan();
        return span[..Math.Max(rootLength, span.LastIndexOf('/'))];
    }

    /// <summary>path.ts getPathComponents (no current directory) and pathComponents.</summary>
    internal static List<string> GetPathComponents(string path)
    {
        var rootLength = TsPath.GetRootLength(path);
        var result = new List<string> { path[..rootLength] };
        var rest = path[rootLength..].Split('/');
        var count = rest.Length > 0 && rest[^1].Length == 0 ? rest.Length - 1 : rest.Length;
        for (var i = 0; i < count; i++)
        {
            result.Add(rest[i]);
        }
        return result;
    }

    /// <summary>path.ts forEachAncestorDirectory.</summary>
    internal static T? ForEachAncestorDirectory<T>(string directory, Func<string, T?> callback) where T : class
    {
        while (true)
        {
            var result = callback(directory);
            if (result is not null)
            {
                return result;
            }
            var parentPath = GetDirectoryPath(directory);
            if (parentPath == directory)
            {
                return null;
            }
            directory = parentPath;
        }
    }
}
