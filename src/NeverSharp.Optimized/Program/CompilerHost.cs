// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts (CompilerHost, the members createProgram calls)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/compiler/host.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; the host returns parsed source files, library files come from the shared
// DefaultLibrary (parsed and bound once per process); MemoryCompilerHost is original code (the Gate C host).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// types.ts: CompilerHost, the members <see cref="Program.Create"/> calls. A program never reads the file system
/// itself; everything it sees comes through the host.
/// </summary>
internal interface ICompilerHost
{
    /// <summary>
    /// CompilerHost.getSourceFile: the parsed file, or null when it does not exist. The program passes the options
    /// TypeScript's program passes (languageVersion = the emit target, jsDocParsingMode = the host's); the result is
    /// not bound yet, or is a bound file the host shares read-only (library files).
    /// </summary>
    SourceFile? GetSourceFile(string fileName, CreateSourceFileOptions options);

    /// <summary>CompilerHost.getDefaultLibFileName: the full name of the default library file for these options.</summary>
    string GetDefaultLibFileName(CompilerOptions options);

    /// <summary>CompilerHost.getDefaultLibLocation: the folder of the library files.</summary>
    string GetDefaultLibLocation();

    string GetCurrentDirectory();

    string GetCanonicalFileName(string fileName);

    bool UseCaseSensitiveFileNames { get; }

    bool FileExists(string fileName);

    /// <summary>
    /// <see cref="FileExists(string)"/> of a name given as characters (module FE1, Track O: the resolver probes names
    /// built in a per-thread buffer): the name as a string when the file exists (the same characters), otherwise null.
    /// </summary>
    string? ExistingFileName(ReadOnlySpan<char> fileName)
    {
        var name = fileName.ToString();
        return FileExists(name) ? name : null;
    }

    /// <summary>ModuleResolutionHost.directoryExists (module resolution, C10).</summary>
    bool DirectoryExists(string directoryName);

    /// <summary><see cref="DirectoryExists(string)"/> of a name given as characters (module FE1); the same answer.</summary>
    bool DirectoryExists(ReadOnlySpan<char> directoryName) => DirectoryExists(directoryName.ToString());

    /// <summary>ModuleResolutionHost.readFile (module resolution reads package.json files, C10).</summary>
    string? ReadFile(string fileName);

    /// <summary>CompilerHost.jsDocParsingMode (null: ParseAll).</summary>
    JSDocParsingMode? JsDocParsingMode { get; }
}

/// <summary>
/// The host of Gate C (phase3-plan.md), the counterpart of harness/astdump/dump-ts-checker.mjs' host: one file served
/// from memory under its unit name with its script kind, the library files from a <see cref="DefaultLibrary"/> under the
/// virtual folder <see cref="LibFolder"/>; nothing else exists (no directories, no @types). Case-sensitive file names,
/// current directory "/".
/// </summary>
internal sealed class MemoryCompilerHost : ICompilerHost
{
    /// <summary>The virtual folder of the library files (the folder TypeScript's own test harness uses for them).</summary>
    internal const string LibFolder = "/.ts";

    private readonly string fileName;
    private readonly string text;
    private readonly ScriptKind scriptKind;
    private readonly DefaultLibrary library;

    internal MemoryCompilerHost(string fileName, string text, ScriptKind scriptKind, DefaultLibrary library, JSDocParsingMode? jsDocParsingMode)
    {
        this.fileName = fileName;
        this.text = text;
        this.scriptKind = scriptKind;
        this.library = library;
        JsDocParsingMode = jsDocParsingMode;
    }

    public JSDocParsingMode? JsDocParsingMode { get; }

    public bool UseCaseSensitiveFileNames => true;

    public SourceFile? GetSourceFile(string fileName, CreateSourceFileOptions options)
    {
        if (fileName == this.fileName)
        {
            return Parser.CreateSourceFile(fileName, text, options, setParentNodes: false, scriptKind);
        }
        return library.GetFile(fileName);
    }

    public string GetDefaultLibFileName(CompilerOptions options) => LibFolder + "/" + options.GetDefaultLibFileName();

    public string GetDefaultLibLocation() => LibFolder;

    public string GetCurrentDirectory() => "/";

    public string GetCanonicalFileName(string fileName) => fileName;

    public bool FileExists(string fileName) => fileName == this.fileName || library.Exists(fileName);

    // The oracle's host: directoryExists: () => false, readFile: the unit only.
    public bool DirectoryExists(string directoryName) => false;

    public string? ReadFile(string fileName) => fileName == this.fileName ? text : null;
}

/// <summary>
/// The host of the check driver (module C10), the counterpart of tsc's ts.createCompilerHost(options) on the real file
/// system: the program's root files are parsed by the driver beforehand (its parallel work items) and handed out by
/// name; other files are parsed on request; the library files come from a <see cref="DefaultLibrary"/> named under the
/// real library folder. File and directory existence is cached (the file system does not change during a run; the
/// results are those of the uncached calls). Case-insensitive file names on Windows (ts.sys.useCaseSensitiveFileNames).
/// Thread-safe for the reads module resolution makes; the program itself is built on one thread.
/// </summary>
internal sealed class DiskCompilerHost : ICompilerHost
{
    private readonly Dictionary<string, SourceFile> parsedFiles;
    private readonly DefaultLibrary library;
    private readonly string libFolder;
    private readonly string currentDirectory;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> fileExists = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> directoryExists = new(StringComparer.Ordinal);

    /// <param name="parsedFiles">the files parsed beforehand, by file name as the program will ask for them (forward slashes)</param>
    /// <param name="library">the library, whose virtual folder is <paramref name="libFolder"/> (the real folder with forward slashes)</param>
    internal DiskCompilerHost(IEnumerable<SourceFile> parsedFiles, DefaultLibrary library, string libFolder, string currentDirectory, JSDocParsingMode? jsDocParsingMode)
    {
        UseCaseSensitiveFileNames = !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS();
        this.parsedFiles = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
        foreach (var file in parsedFiles)
        {
            this.parsedFiles[GetCanonicalFileName(file.Store.FileName(file.Root))] = file;
        }
        this.library = library;
        this.libFolder = libFolder;
        this.currentDirectory = currentDirectory;
        JsDocParsingMode = jsDocParsingMode;
    }

    public JSDocParsingMode? JsDocParsingMode { get; }

    public bool UseCaseSensitiveFileNames { get; }

    public SourceFile? GetSourceFile(string fileName, CreateSourceFileOptions options)
    {
        if (parsedFiles.TryGetValue(GetCanonicalFileName(fileName), out var file))
        {
            return file;
        }
        if (library.GetFile(fileName) is { } libFile)
        {
            return libFile;
        }
        if (!File.Exists(fileName))
        {
            return null;
        }
        return Parser.ParseFile(fileName, fileName, options, setParentNodes: false, ScriptKindFromExtension(fileName));
    }

    public string GetDefaultLibFileName(CompilerOptions options) => libFolder + "/" + options.GetDefaultLibFileName();

    public string GetDefaultLibLocation() => libFolder;

    public string GetCurrentDirectory() => currentDirectory;

    // sys.ts toFileNameLowerCase on a case-insensitive file system
    public string GetCanonicalFileName(string fileName) => UseCaseSensitiveFileNames ? fileName : fileName.ToLowerInvariant();

    public bool FileExists(string fileName) => fileExists.GetOrAdd(fileName, static f => File.Exists(f));

    public bool DirectoryExists(string directoryName) => directoryExists.GetOrAdd(directoryName, static d => Directory.Exists(d));

    // Module FE1 (Track O): a cached answer is found without making a string of the name (tsrs-only shape: the cached
    // vfs probed by &str, notes/perf-frontend-64.md); otherwise the string overload asks the file system once.
    public string? ExistingFileName(ReadOnlySpan<char> fileName)
    {
        if (fileExists.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(fileName, out var name, out var exists))
        {
            return exists ? name : null;
        }
        name = fileName.ToString();
        return FileExists(name) ? name : null;
    }

    public bool DirectoryExists(ReadOnlySpan<char> directoryName) =>
        directoryExists.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(directoryName, out var exists) ? exists : DirectoryExists(directoryName.ToString());

    public string? ReadFile(string fileName)
    {
        try
        {
            var text = SourceText.ReadNative(fileName);
            var result = text.GetString();
            text.Release();
            return result;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // core.GetScriptKindFromFileName / utilities.ts ensureScriptKind (the extension decides)
    internal static ScriptKind ScriptKindFromExtension(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".ts" or ".mts" or ".cts" => ScriptKind.TS,
        ".tsx" => ScriptKind.TSX,
        ".js" or ".mjs" or ".cjs" => ScriptKind.JS,
        ".jsx" => ScriptKind.JSX,
        ".json" => ScriptKind.JSON,
        _ => ScriptKind.Unknown,
    };
}
