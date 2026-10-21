// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>nodeFactory.ts: literal-like nodes and template literal tokens.</summary>
internal sealed partial class NodeFactory
{
    // @api
    internal Node CreateLiteralLikeNode(SyntaxKind kind, string text)
    {
        switch (kind)
        {
            case SyntaxKind.NumericLiteral:
                return CreateNumericLiteral(text, /*numericLiteralFlags*/ 0);
            case SyntaxKind.BigIntLiteral:
                return CreateBigIntLiteral(text);
            case SyntaxKind.StringLiteral:
                return CreateStringLiteral(text, /*isSingleQuote*/ null);
            case SyntaxKind.JsxText:
                return CreateJsxText(text, /*containsOnlyTriviaWhiteSpaces*/ false);
            case SyntaxKind.JsxTextAllWhiteSpaces:
                return CreateJsxText(text, /*containsOnlyTriviaWhiteSpaces*/ true);
            case SyntaxKind.RegularExpressionLiteral:
                return CreateRegularExpressionLiteral(text);
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                return CreateTemplateLiteralLikeNode(kind, text, /*rawText*/ null, /*templateFlags*/ 0);
        }
        // (TypeScript's switch covers every kind of its parameter type and returns undefined otherwise.)
        return null!;
    }

    private static TransformFlags GetTransformFlagsOfTemplateLiteralLike(TokenFlags templateFlags)
    {
        var transformFlags = TransformFlags.ContainsES2015;
        if (templateFlags != 0)
        {
            transformFlags |= TransformFlags.ContainsES2018;
        }
        return transformFlags;
    }

    // NOTE: `createTemplateLiteralLikeToken` and `createTemplateLiteralLikeDeclaration` are identical except for
    //       the underlying nodes they create. To avoid polymorphism due to two different node shapes, these
    //       functions are intentionally duplicated.
    private Node CreateTemplateLiteralLikeToken(SyntaxKind kind, string text, string? rawText, TokenFlags? templateFlags)
    {
        var node = CreateBaseToken<Node>(kind);
        // `templateFlags! & TokenFlags.TemplateLiteralLikeFlags` (undefined & x is 0)
        var flagsValue = (templateFlags ?? 0) & TokenFlags.TemplateLiteralLikeFlags;
        switch (node)
        {
            case TemplateHead head:
                head.Text = text;
                head.RawText = rawText;
                head.TemplateFlags = flagsValue;
                break;
            case TemplateMiddle middle:
                middle.Text = text;
                middle.RawText = rawText;
                middle.TemplateFlags = flagsValue;
                break;
            case TemplateTail tail:
                tail.Text = text;
                tail.RawText = rawText;
                tail.TemplateFlags = flagsValue;
                break;
            default:
                throw new InvalidOperationException($"{node.Kind} is not a template literal token");
        }
        node.TransformFlags = GetTransformFlagsOfTemplateLiteralLike(flagsValue);
        return node;
    }

    private NoSubstitutionTemplateLiteral CreateTemplateLiteralLikeDeclaration(SyntaxKind kind, string text, string? rawText, TokenFlags? templateFlags)
    {
        var node = CreateBaseDeclaration<NoSubstitutionTemplateLiteral>(kind);
        node.Text = text;
        node.RawText = rawText;
        node.TemplateFlags = (templateFlags ?? 0) & TokenFlags.TemplateLiteralLikeFlags;
        node.TransformFlags = GetTransformFlagsOfTemplateLiteralLike(node.TemplateFlags);
        return node;
    }

    // @api
    internal Node CreateTemplateLiteralLikeNode(SyntaxKind kind, string text, string? rawText, TokenFlags? templateFlags)
    {
        if (kind == SyntaxKind.NoSubstitutionTemplateLiteral)
        {
            return CreateTemplateLiteralLikeDeclaration(kind, text, rawText, templateFlags);
        }
        return CreateTemplateLiteralLikeToken(kind, text, rawText, templateFlags);
    }
}
