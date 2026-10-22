// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/tspath/extension.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// utilities.ts: the extension helpers on paths. They live in TsPath (not Utilities) as in typescript-go,
/// which places TryGetExtensionFromPath and extensionsToRemove in package tspath (extension.go); the C# API
/// of module M1 (docs/modules/M1.md) calls TsPath.TryGetExtensionFromPath.
/// </summary>
internal static partial class TsPath
{
    private static readonly string[] ExtensionsToRemove = [Extension.Dts, Extension.Dmts, Extension.Dcts, Extension.Mjs, Extension.Mts, Extension.Cjs, Extension.Cts, Extension.Ts, Extension.Js, Extension.Tsx, Extension.Jsx, Extension.Json];

    internal static string? TryGetExtensionFromPath(string path)
    {
        return Core.Find(ExtensionsToRemove, (e, _) => FileExtensionIs(path, e));
    }
}
