// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/utilitiesPublic.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/core/text.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

namespace NeverSharp.Faithful;

/// <summary>factory/utilitiesPublic.ts.</summary>
internal static partial class FactoryUtilitiesPublic
{
    internal static T SetTextRange<T>(T range, ITextRange? location)
        where T : ITextRange
    {
        return location is not null ? Utilities.SetTextRangePosEnd(range, location.Pos, location.End) : range;
    }
}
