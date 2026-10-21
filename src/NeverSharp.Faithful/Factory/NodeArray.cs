// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

namespace NeverSharp.Faithful;

/// <summary>
/// The constructors of the generated NodeArray: TypeScript's createNodeArray copies the elements into a
/// fresh array (slice) or adopts the caller's array; a C# NodeArray is a List of its own, so it copies.
/// </summary>
public sealed partial class NodeArray
{
    public NodeArray()
    {
    }

    public NodeArray(int capacity)
        : base(capacity)
    {
    }

    public NodeArray(IEnumerable<Node> collection)
        : base(collection)
    {
    }
}
