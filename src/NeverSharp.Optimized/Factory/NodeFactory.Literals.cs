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

namespace NeverSharp.Optimized;

/// <summary>nodeFactory.ts: literal-like nodes and template literal tokens.</summary>
internal sealed partial class NodeFactory
{
    // @api
    internal Node CreateLiteralLikeNode(SyntaxKind kind, TextRef text)
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
                return CreateTemplateLiteralLikeNode(kind, text, /*rawText*/ default, /*templateFlags*/ 0);
        }
        // (TypeScript's switch covers every kind of its parameter type and returns undefined otherwise.)
        return default;
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
    private Node CreateTemplateLiteralLikeToken(SyntaxKind kind, TextRef text, TextRef rawText, TokenFlags? templateFlags)
    {
        var node = CreateBaseToken(kind);
        // `templateFlags! & TokenFlags.TemplateLiteralLikeFlags` (undefined & x is 0)
        var flagsValue = (templateFlags ?? 0) & TokenFlags.TemplateLiteralLikeFlags;
        switch (node)
        {
            case var head when store.IsTemplateHead(head):
                store.SetText(head, text);
                store.SetRawText(head, rawText);
                store.SetTemplateFlags(head, flagsValue);
                break;
            case var middle when store.IsTemplateMiddle(middle):
                store.SetText(middle, text);
                store.SetRawText(middle, rawText);
                store.SetTemplateFlags(middle, flagsValue);
                break;
            case var tail when store.IsTemplateTail(tail):
                store.SetText(tail, text);
                store.SetRawText(tail, rawText);
                store.SetTemplateFlags(tail, flagsValue);
                break;
            default:
                throw new InvalidOperationException($"{store.Kind(node)} is not a template literal token");
        }
        store.SetTransformFlags(node, GetTransformFlagsOfTemplateLiteralLike(flagsValue));
        return node;
    }

    private Node CreateTemplateLiteralLikeDeclaration(SyntaxKind kind, TextRef text, TextRef rawText, TokenFlags? templateFlags)
    {
        var node = CreateBaseDeclaration(kind);
        store.SetText(node, text);
        store.SetRawText(node, rawText);
        store.SetTemplateFlags(node, (templateFlags ?? 0) & TokenFlags.TemplateLiteralLikeFlags);
        store.SetTransformFlags(node, GetTransformFlagsOfTemplateLiteralLike(store.TemplateFlags(node)));
        return node;
    }

    // @api
    internal Node CreateTemplateLiteralLikeNode(SyntaxKind kind, TextRef text, TextRef rawText, TokenFlags? templateFlags)
    {
        if (kind == SyntaxKind.NoSubstitutionTemplateLiteral)
        {
            return CreateTemplateLiteralLikeDeclaration(kind, text, rawText, templateFlags);
        }
        return CreateTemplateLiteralLikeToken(kind, text, rawText, templateFlags);
    }
}
