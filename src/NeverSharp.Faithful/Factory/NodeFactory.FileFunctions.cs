// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go (NodeFactory)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>The file-level functions of nodeFactory.ts after createNodeFactory (static members).</summary>
internal sealed partial class NodeFactory
{
    private static string GetDefaultTagNameForKind(SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.JSDocTypeTag:
                return "type";
            case SyntaxKind.JSDocReturnTag:
                return "returns";
            case SyntaxKind.JSDocThisTag:
                return "this";
            case SyntaxKind.JSDocEnumTag:
                return "enum";
            case SyntaxKind.JSDocAuthorTag:
                return "author";
            case SyntaxKind.JSDocClassTag:
                return "class";
            case SyntaxKind.JSDocPublicTag:
                return "public";
            case SyntaxKind.JSDocPrivateTag:
                return "private";
            case SyntaxKind.JSDocProtectedTag:
                return "protected";
            case SyntaxKind.JSDocReadonlyTag:
                return "readonly";
            case SyntaxKind.JSDocOverrideTag:
                return "override";
            case SyntaxKind.JSDocTemplateTag:
                return "template";
            case SyntaxKind.JSDocTypedefTag:
                return "typedef";
            case SyntaxKind.JSDocParameterTag:
                return "param";
            case SyntaxKind.JSDocPropertyTag:
                return "prop";
            case SyntaxKind.JSDocCallbackTag:
                return "callback";
            case SyntaxKind.JSDocOverloadTag:
                return "overload";
            case SyntaxKind.JSDocAugmentsTag:
                return "augments";
            case SyntaxKind.JSDocImplementsTag:
                return "implements";
            case SyntaxKind.JSDocImportTag:
                return "import";
            default:
                // Debug.formatSyntaxKind(kind): the enum member name.
                Debug.Fail("Unsupported kind: " + kind.ToString());
                return null!;
        }
    }

    // `let rawTextScanner: Scanner | undefined;` TypeScript keeps one scanner in a module variable and creates it on
    // first use. That is shared mutable state, which the thread-safety requirement rules out, so GetCookedText creates
    // its own scanner on each call (it is only reached through createTemplateHead/Middle/Tail and
    // createNoSubstitutionTemplateLiteral with a raw text, which the parser never calls).
    private static readonly object InvalidValueSentinel = new();

    /// <summary>Returns the cooked text (a string) or the invalid-value sentinel object.</summary>
    private static object GetCookedText(SyntaxKind kind, string rawText)
    {
        // if (!rawTextScanner) { rawTextScanner = createScanner(ScriptTarget.Latest, /*skipTrivia*/ false, LanguageVariant.Standard); }
        var rawTextScanner = Scanner.CreateScanner(ScriptTarget.Latest, /*skipTrivia*/ false, LanguageVariant.Standard);
        switch (kind)
        {
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                rawTextScanner.SetText("`" + rawText + "`");
                break;
            case SyntaxKind.TemplateHead:
                rawTextScanner.SetText("`" + rawText + "${");
                break;
            case SyntaxKind.TemplateMiddle:
                rawTextScanner.SetText("}" + rawText + "${");
                break;
            case SyntaxKind.TemplateTail:
                rawTextScanner.SetText("}" + rawText + "`");
                break;
        }

        var token = rawTextScanner.Scan();
        if (token == SyntaxKind.CloseBraceToken)
        {
            token = rawTextScanner.ReScanTemplateToken(/*isTaggedTemplate*/ false);
        }

        if (rawTextScanner.IsUnterminated())
        {
            rawTextScanner.SetText(null);
            return InvalidValueSentinel;
        }

        string? tokenValue = null;
        switch (token)
        {
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateHead:
            case SyntaxKind.TemplateMiddle:
            case SyntaxKind.TemplateTail:
                tokenValue = rawTextScanner.GetTokenValue();
                break;
        }

        if (tokenValue == null || rawTextScanner.Scan() != SyntaxKind.EndOfFileToken)
        {
            rawTextScanner.SetText(null);
            return InvalidValueSentinel;
        }

        rawTextScanner.SetText(null);
        return tokenValue;
    }

    //
    // Utilities
    //

    internal static Node SetOriginalNode(Node node, Node? original)
    {
        if (node.Original != original)
        {
            node.Original = original;
            if (original != null)
            {
                var emitNode = (EmitNode?)original.EmitNode;
                if (emitNode != null)
                {
                    node.EmitNode = MergeEmitNode(emitNode, (EmitNode?)node.EmitNode);
                }
            }
        }
        return node;
    }

    private static EmitNode MergeEmitNode(EmitNode sourceEmitNode, EmitNode? destEmitNode)
    {
        var flags = sourceEmitNode.Flags;
        var internalFlags = sourceEmitNode.InternalFlags;
        var leadingComments = sourceEmitNode.LeadingComments;
        var trailingComments = sourceEmitNode.TrailingComments;
        var commentRange = sourceEmitNode.CommentRange;
        var sourceMapRange = sourceEmitNode.SourceMapRange;
        var tokenSourceMapRanges = sourceEmitNode.TokenSourceMapRanges;
        var constantValue = sourceEmitNode.ConstantValue;
        var helpers = sourceEmitNode.Helpers;
        var startsOnNewLine = sourceEmitNode.StartsOnNewLine;
        var snippetElement = sourceEmitNode.SnippetElement;
        var classThis = sourceEmitNode.ClassThis;
        var assignedName = sourceEmitNode.AssignedName;
        destEmitNode ??= new EmitNode();

        // NOTE: We should have one or more lines here for each property in EmitNode, even if the line
        // consists only of a comment indicating the property does not merge

        // `flags` overwrites the destination
        if (flags != 0)
        {
            destEmitNode.Flags = flags;
        }

        // `internalFlags` overwrites the destination. We do not copy over the immutability of the source.
        if (internalFlags != 0)
        {
            destEmitNode.InternalFlags = internalFlags & ~InternalEmitFlags.Immutable;
        }

        // `annotatedNodes` are not merged as they should only present on the parse tree node of a `SourceFile`.

        // `leadingComments` are concatenated with any existing leading comments on the destination
        if (leadingComments != null)
        {
            // We use `.slice()` in case `destEmitNode.leadingComments` is pushed to later
            destEmitNode.LeadingComments = Core.AddRange(new List<object>(leadingComments), destEmitNode.LeadingComments);
        }

        // `trailingComments` are concatenated with any existing trailing comments on the destination
        if (trailingComments != null)
        {
            // We use `.slice()` in case `destEmitNode.trailingComments` is pushed to later
            destEmitNode.TrailingComments = Core.AddRange(new List<object>(trailingComments), destEmitNode.TrailingComments);
        }

        // `commentRange` overwrites the destination
        if (commentRange != null)
        {
            destEmitNode.CommentRange = commentRange;
        }

        // `sourceMapRange` overwrites the destination
        if (sourceMapRange != null)
        {
            destEmitNode.SourceMapRange = sourceMapRange;
        }

        // `tokenSourceMapRanges` are merged with the destination
        if (tokenSourceMapRanges != null)
        {
            destEmitNode.TokenSourceMapRanges = MergeTokenSourceMapRanges(tokenSourceMapRanges, destEmitNode.TokenSourceMapRanges!);
        }

        // `constantValue` overwrites the destination
        if (constantValue != null)
        {
            destEmitNode.ConstantValue = constantValue;
        }

        // `externalHelpersModuleName` is not merged
        // `externalHelpers` is not merged

        // `helpers` are merged into the destination
        if (helpers != null)
        {
            foreach (var helper in helpers)
            {
                destEmitNode.Helpers = Core.AppendIfUnique(destEmitNode.Helpers, helper);
            }
        }

        // `startsOnNewLine` overwrites the destination
        if (startsOnNewLine != null)
        {
            destEmitNode.StartsOnNewLine = startsOnNewLine;
        }

        // `snippetElement` overwrites the destination
        if (snippetElement != null)
        {
            destEmitNode.SnippetElement = snippetElement;
        }

        // `typeNode` is not merged as it only applies to comment emit for a variable declaration.
        // TODO: `typeNode` should overwrite the destination

        // `classThis` overwrites the destination
        if (classThis != null)
        {
            destEmitNode.ClassThis = classThis;
        }

        // `assignedName` overwrites the destination
        if (assignedName != null)
        {
            destEmitNode.AssignedName = assignedName;
        }

        // `identifierTypeArguments` are not merged as they only apply to an Identifier in quick info
        // `autoGenerate` is not merged as it only applies to a specific generated Identifier/PrivateIdentifier
        // `generatedImportReference` is not merged as it only applies to an Identifier

        return destEmitNode;
    }

    /// <summary>
    /// mergeTokenSourceMapRanges: `for (const key in sourceRanges) destRanges[key] = sourceRanges[key];` over a sparse
    /// array indexed by SyntaxKind (a dictionary from index to range here).
    /// </summary>
    private static Dictionary<int, TextRange?> MergeTokenSourceMapRanges(Dictionary<int, TextRange?> sourceRanges, Dictionary<int, TextRange?>? destRanges)
    {
        destRanges ??= [];
        foreach (var entry in sourceRanges)
        {
            destRanges[entry.Key] = entry.Value;
        }
        return destRanges;
    }
}
