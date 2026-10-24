// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/binder.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/binder/binder.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module B0 provides only the entry point and the storage of the
// binder output (BinderStore), modules B1-B3 port the binder itself.

namespace NeverSharp.Optimized;

/// <summary>binder.ts: the binder (bindSourceFile).</summary>
internal static class Binder
{
    /// <summary>
    /// binder.ts: bindSourceFile(file, options). Binds the file once (TypeScript: `if (!file.locals)`): the output is
    /// stored in a <see cref="BinderStore"/> owned by the result (<see cref="SourceFile.Binding"/>).
    /// </summary>
    internal static void BindSourceFile(SourceFile file, CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(options);
        if (file.Binding is not null)
        {
            return;
        }
        file.Binding = new BinderStore(file.Store);
        // Modules B1-B3: bind(file), file.symbolCount, delayedBindJSDocTypedefTag, bindJSDocImports.
    }
}

/// <summary>
/// types.ts: CompilerOptions. Module B0 needs only the type; the binder modules add the options binder.ts reads
/// (target, alwaysStrict / strict, noFallthroughCasesInSwitch, allowUnreachableCode, allowUnusedLabels,
/// preserveConstEnums, isolatedModules). The oracle binds with `{}`, all options undefined.
/// </summary>
internal sealed class CompilerOptions
{
}
