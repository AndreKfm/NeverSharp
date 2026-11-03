// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts (EmitHost, EmitResult, WriteFileCallback, EmitFileNames) and src/compiler/program.ts
// (getEmitHost)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/compiler/emitHost.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; the members emitFiles reads under Gate D's options (no outDir, outFile,
// declarationDir, project references, build info or source maps).

namespace NeverSharp.Optimized;

/// <summary>types.ts WriteFileCallback (onError, sourceFiles and data are not passed: nothing in Track O reads them).</summary>
internal delegate void WriteFileCallback(string fileName, string text, bool writeByteOrderMark);

/// <summary>types.ts EmitResult (emittedFiles and sourceMaps are not collected: listEmittedFiles and source maps are off).</summary>
internal sealed class EmitResult
{
    internal EmitResult(bool emitSkipped, List<ProgramDiagnostic> diagnostics)
    {
        EmitSkipped = emitSkipped;
        Diagnostics = diagnostics;
    }

    internal bool EmitSkipped { get; }

    internal List<ProgramDiagnostic> Diagnostics { get; }
}

/// <summary>types.ts EmitFileNames (no source map, declaration map or build info paths: those options are off).</summary>
internal readonly record struct EmitFileNames(string? JsFilePath, string? DeclarationFilePath);

/// <summary>
/// program.ts getEmitHost(writeFileCallback): the program as the emitter sees it. The write callback receives every
/// output file; with none, the program's host would write it (Track O's hosts never write, so it is dropped).
/// </summary>
internal sealed class EmitHost
{
    private readonly Program program;
    private readonly WriteFileCallback? writeFileCallback;

    internal EmitHost(Program program, WriteFileCallback? writeFileCallback)
    {
        this.program = program;
        this.writeFileCallback = writeFileCallback;
    }

    internal Program Program => program;

    internal CompilerOptions GetCompilerOptions() => program.Options;

    internal string GetCurrentDirectory() => program.CurrentDirectory;

    internal IReadOnlyList<SourceFile> GetSourceFiles() => program.SourceFiles;

    internal bool IsEmitBlocked(string emitFileName) => program.IsEmitBlocked(emitFileName);

    internal void WriteFile(string fileName, string text, bool writeByteOrderMark)
    {
        writeFileCallback?.Invoke(fileName, text, writeByteOrderMark);
    }
}
