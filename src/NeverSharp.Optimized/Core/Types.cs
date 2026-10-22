// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/core/text.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

namespace NeverSharp.Optimized;

/// <summary>
/// types.ts: TextRange (and ReadonlyTextRange, which differs only in the readonly modifiers that
/// setTextRangePos and friends bypass). A node's or a node list's range is read and written through its
/// NodeStore (Track O, steps O6a and O7a).
/// </summary>
internal interface TextRange
{
    int Pos { get; set; }

    int End { get; set; }
}

/// <summary>types.ts: TextSpan.</summary>
internal sealed class TextSpan
{
    public int Start;
    public int Length;
}
