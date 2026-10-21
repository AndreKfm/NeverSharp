// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/core/text.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

namespace NeverSharp.Faithful;

/// <summary>
/// types.ts: <c>interface TextRange { pos; end; }</c>. TypeScript's <c>ReadonlyTextRange</c> is the
/// same shape with readonly members; the helpers that take it write through a cast to
/// <c>TextRange</c> (utilities.ts <c>setTextRangePos</c>), so one interface serves both.
/// </summary>
internal interface ITextRange
{
    int Pos { get; set; }

    int End { get; set; }
}

/// <summary>
/// types.ts: <c>interface TextSpan { start; length; }</c>. A small immutable record kept as a struct,
/// as typescript-go keeps its text ranges (internal/core/text.go, <c>TextRange</c>).
/// </summary>
internal readonly record struct TextSpan(int Start, int Length);

/// <summary>Node is a TextRange (types.ts: <c>interface Node extends ReadonlyTextRange</c>).</summary>
public abstract partial class Node : ITextRange
{
    int ITextRange.Pos { get => Pos; set => Pos = value; }

    int ITextRange.End { get => End; set => End = value; }
}

/// <summary>NodeArray is a TextRange (types.ts: <c>interface NodeArray&lt;T&gt; extends ReadonlyArray&lt;T&gt;, ReadonlyTextRange</c>).</summary>
public sealed partial class NodeArray : ITextRange
{
    int ITextRange.Pos { get => Pos; set => Pos = value; }

    int ITextRange.End { get => End; set => End = value; }
}
