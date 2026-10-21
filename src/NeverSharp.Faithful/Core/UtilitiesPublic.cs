// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilitiesPublic.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/core/text.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

namespace NeverSharp.Faithful;

/// <summary>utilitiesPublic.ts.</summary>
internal static partial class UtilitiesPublic
{
    internal static TextSpan CreateTextSpan(int start, int length)
    {
        // TypeScript throws `new Error(...)`; the exception carries exactly its message.
        if (start < 0)
        {
            throw new InvalidOperationException("start < 0");
        }
        if (length < 0)
        {
            throw new InvalidOperationException("length < 0");
        }

        return new TextSpan { Start = start, Length = length };
    }

    internal static TextSpan CreateTextSpanFromBounds(int start, int end)
    {
        return CreateTextSpan(start, end - start);
    }
}
