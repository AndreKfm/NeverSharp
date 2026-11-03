// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/program.ts (emit, emitWorker, getEmitHost)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/compiler/program.go (Emit)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; no cancellation, emitOnly, custom transformers, forceDtsEmit or build info
// (module D0).

namespace NeverSharp.Optimized;

internal sealed partial class Program
{
    /// <summary>
    /// program.ts emit(sourceFile, writeFileCallback): emitWorker. (handleNoEmitOptions: noEmit and noEmitOnError are
    /// not options Track O supports, so the emit always runs.) The emit resolver is created as TypeScript does, by
    /// checking the target file (or every file) first, so the checker's state is the same; module D1 ports the resolver
    /// itself.
    /// </summary>
    internal EmitResult Emit(SourceFile? sourceFile, WriteFileCallback? writeFileCallback)
    {
        // typeChecker.getEmitResolver(options.outFile ? undefined : sourceFile, cancellationToken, skipDiagnostics: false):
        // getDiagnostics(sourceFile) first
        var typeChecker = GetTypeChecker();
        if (sourceFile is not null)
        {
            typeChecker.GetDiagnostics(sourceFile);
        }
        else
        {
            foreach (var file in files)
            {
                typeChecker.GetDiagnostics(file);
            }
        }
        return Emitter.EmitFiles(new EmitHost(this, writeFileCallback), sourceFile);
    }
}
