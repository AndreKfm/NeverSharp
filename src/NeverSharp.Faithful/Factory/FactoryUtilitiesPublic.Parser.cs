// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/utilitiesPublic.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/utilities.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>factory/utilitiesPublic.ts (the functions the parser reaches; `setTextRange` is in Core/FactoryUtilitiesPublic.cs).</summary>
internal static partial class FactoryUtilitiesPublic
{
    internal static bool CanHaveModifiers(Node node)
    {
        var kind = node.Kind;
        return kind == SyntaxKind.TypeParameter
            || kind == SyntaxKind.Parameter
            || kind == SyntaxKind.PropertySignature
            || kind == SyntaxKind.PropertyDeclaration
            || kind == SyntaxKind.MethodSignature
            || kind == SyntaxKind.MethodDeclaration
            || kind == SyntaxKind.Constructor
            || kind == SyntaxKind.GetAccessor
            || kind == SyntaxKind.SetAccessor
            || kind == SyntaxKind.IndexSignature
            || kind == SyntaxKind.ConstructorType
            || kind == SyntaxKind.FunctionExpression
            || kind == SyntaxKind.ArrowFunction
            || kind == SyntaxKind.ClassExpression
            || kind == SyntaxKind.VariableStatement
            || kind == SyntaxKind.FunctionDeclaration
            || kind == SyntaxKind.ClassDeclaration
            || kind == SyntaxKind.InterfaceDeclaration
            || kind == SyntaxKind.TypeAliasDeclaration
            || kind == SyntaxKind.EnumDeclaration
            || kind == SyntaxKind.ModuleDeclaration
            || kind == SyntaxKind.ImportEqualsDeclaration
            || kind == SyntaxKind.ImportDeclaration
            || kind == SyntaxKind.ExportAssignment
            || kind == SyntaxKind.ExportDeclaration;
    }
}
