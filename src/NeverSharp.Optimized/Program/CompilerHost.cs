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
}
