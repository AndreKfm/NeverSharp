// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Ported from src/compiler/factory/nodeConverters.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/printer/factory.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

namespace NeverSharp.Faithful;

/// <summary>types.ts: NodeConverters.</summary>
internal interface NodeConverters
{
    Block ConvertToFunctionBlock(Node node, bool multiLine = false);

    FunctionExpression ConvertToFunctionExpression(FunctionDeclaration node);

    ClassExpression ConvertToClassExpression(ClassDeclaration node);

    Node ConvertToArrayAssignmentElement(Node element);

    Node ConvertToObjectAssignmentElement(Node element);

    Node ConvertToAssignmentPattern(Node node);

    ObjectLiteralExpression ConvertToObjectAssignmentPattern(Node node);

    ArrayLiteralExpression ConvertToArrayAssignmentPattern(Node node);

    Node ConvertToAssignmentElementTarget(Node node);
}

/// <summary>factory/nodeConverters.ts: nullNodeConverters (createNodeConverters is not in the parser's slice).</summary>
internal static class NodeConvertersNull
{
    // (Typed as the implementing class so that CA1859 has nothing to upgrade; it converts to NodeConverters.)
    internal static readonly NullConverters NullNodeConverters = new();

    /// <summary>The object literal `nullNodeConverters`: every member is `notImplemented`.</summary>
    internal sealed class NullConverters : NodeConverters
    {
        // convertToFunctionBlock: notImplemented
        public Block ConvertToFunctionBlock(Node node, bool multiLine = false)
        {
            Core.NotImplemented();
            return null!;
        }

        // convertToFunctionExpression: notImplemented
        public FunctionExpression ConvertToFunctionExpression(FunctionDeclaration node)
        {
            Core.NotImplemented();
            return null!;
        }

        // convertToClassExpression: notImplemented
        public ClassExpression ConvertToClassExpression(ClassDeclaration node)
        {
            Core.NotImplemented();
            return null!;
        }

        // convertToArrayAssignmentElement: notImplemented
        public Node ConvertToArrayAssignmentElement(Node element)
        {
            Core.NotImplemented();
            return null!;
        }

        // convertToObjectAssignmentElement: notImplemented
        public Node ConvertToObjectAssignmentElement(Node element)
        {
            Core.NotImplemented();
            return null!;
        }

        // convertToAssignmentPattern: notImplemented
        public Node ConvertToAssignmentPattern(Node node)
        {
            Core.NotImplemented();
            return null!;
        }

        // convertToObjectAssignmentPattern: notImplemented
        public ObjectLiteralExpression ConvertToObjectAssignmentPattern(Node node)
        {
            Core.NotImplemented();
            return null!;
        }

        // convertToArrayAssignmentPattern: notImplemented
        public ArrayLiteralExpression ConvertToArrayAssignmentPattern(Node node)
        {
            Core.NotImplemented();
            return null!;
        }

        // convertToAssignmentElementTarget: notImplemented
        public Node ConvertToAssignmentElementTarget(Node node)
        {
            Core.NotImplemented();
            return null!;
        }
    }
}
