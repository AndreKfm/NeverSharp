// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Modified: translated from TypeScript to C#; a list of node handles (step O6a keeps node lists as lists;
// ranges in an index arena are step O7).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>TypeScript's NodeArray: an array of nodes with a text range (types.ts).</summary>
internal sealed partial class NodeArray : List<Node>
{
    public int Pos = -1;
    public int End = -1;
    public bool HasTrailingComma;
    public TransformFlags TransformFlags;
}
