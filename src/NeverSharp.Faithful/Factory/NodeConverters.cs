// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeConverters.ts and src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

namespace NeverSharp.Faithful;

/// <summary>
/// types.ts: interface NodeConverters. TypeScript's unions (ConciseBody, BindingOrAssignmentElement,
/// BindingOrAssignmentPattern, ...) are Node here.
/// </summary>
internal interface NodeConverters
{
    Block ConvertToFunctionBlock(Node node, bool? multiLine = null);

    FunctionExpression ConvertToFunctionExpression(FunctionDeclaration node);

    ClassExpression ConvertToClassExpression(ClassDeclaration node);

    Node ConvertToArrayAssignmentElement(Node element);

    Node ConvertToObjectAssignmentElement(Node element);

    Node ConvertToAssignmentPattern(Node node);

    ObjectLiteralExpression ConvertToObjectAssignmentPattern(Node node);

    ArrayLiteralExpression ConvertToArrayAssignmentPattern(Node node);

    Node ConvertToAssignmentElementTarget(Node node);
}

/// <summary>nodeConverters.ts: nullNodeConverters (the converters of a factory created with NoNodeConverters): every function is `notImplemented`.</summary>
internal sealed class NullNodeConverters : NodeConverters
{
    internal static readonly NullNodeConverters Instance = new();

    private NullNodeConverters()
    {
    }

    public Block ConvertToFunctionBlock(Node node, bool? multiLine = null)
    {
        Core.NotImplemented();
        return null!;
    }

    public FunctionExpression ConvertToFunctionExpression(FunctionDeclaration node)
    {
        Core.NotImplemented();
        return null!;
    }

    public ClassExpression ConvertToClassExpression(ClassDeclaration node)
    {
        Core.NotImplemented();
        return null!;
    }

    public Node ConvertToArrayAssignmentElement(Node element)
    {
        Core.NotImplemented();
        return null!;
    }

    public Node ConvertToObjectAssignmentElement(Node element)
    {
        Core.NotImplemented();
        return null!;
    }

    public Node ConvertToAssignmentPattern(Node node)
    {
        Core.NotImplemented();
        return null!;
    }

    public ObjectLiteralExpression ConvertToObjectAssignmentPattern(Node node)
    {
        Core.NotImplemented();
        return null!;
    }

    public ArrayLiteralExpression ConvertToArrayAssignmentPattern(Node node)
    {
        Core.NotImplemented();
        return null!;
    }

    public Node ConvertToAssignmentElementTarget(Node node)
    {
        Core.NotImplemented();
        return null!;
    }
}
