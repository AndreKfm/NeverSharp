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

/// <summary>nodeFactory.ts: the factory's lazily created helpers, the `as*` conversions, `update`, and the file-level utilities.</summary>
internal sealed partial class NodeFactory
{
    // Lazily load the parenthesizer, node converters, and some factory methods until they are used.
    // (TypeScript: `memoize(() => flags & NodeFactoryFlags.NoParenthesizerRules ? nullParenthesizerRules : createParenthesizerRules(factory))`.)
    private ParenthesizerRules? parenthesizerRulesValue;
    private NodeConverters? convertersValue;

    /// <summary>The factory's `parenthesizer` getter (TypeScript's memoized `parenthesizerRules()`).</summary>
    internal ParenthesizerRules Parenthesizer
    {
        get
        {
            if (parenthesizerRulesValue == null)
            {
                if ((flags & NodeFactoryFlags.NoParenthesizerRules) == 0)
                {
                    // createParenthesizerRules (factory/parenthesizerRules.ts) is not ported: the parser's factory
                    // is created with NoParenthesizerRules.
                    Debug.Fail("createParenthesizerRules is not ported");
                }
                parenthesizerRulesValue = FactoryParenthesizerRules.NullParenthesizerRules;
            }
            return parenthesizerRulesValue;
        }
    }

    /// <summary>The factory's `converters` getter (TypeScript's memoized `converters()`).</summary>
    internal NodeConverters Converters
    {
        get
        {
            if (convertersValue == null)
            {
                if ((flags & NodeFactoryFlags.NoNodeConverters) == 0)
                {
                    // createNodeConverters (factory/nodeConverters.ts) is not ported: the parser's factory is
                    // created with NoNodeConverters.
                    Debug.Fail("createNodeConverters is not ported");
                }
                convertersValue = FactoryNodeConverters.NullNodeConverters;
            }
            return convertersValue;
        }
    }

    /// <summary>`const setOriginal = flags & NodeFactoryFlags.NoOriginalNode ? identity : setOriginalNode;`</summary>
    private T SetOriginal<T>(T node, Node? original)
        where T : Node
    {
        return (flags & NodeFactoryFlags.NoOriginalNode) != 0 ? node : SetOriginalNode(node, original);
    }

    private static NodeArray? AsNodeArray(IReadOnlyList<Node>? array)
    {
        return array != null ? CreateNodeArray(array) : null;
    }

    /// <summary>`asName(name: string | T)`: a string becomes an Identifier.</summary>
    private Node? AsName(object? name)
    {
        return name is string s ? CreateIdentifier(s) :
            (Node?)name;
    }

    /// <summary>`asExpression(value: string | number | boolean | T)`.</summary>
    private Node? AsExpression(object? value)
    {
        return value switch
        {
            string s => CreateStringLiteral(s),
            int i => CreateNumericLiteral(i),
            double d => CreateNumericLiteral(d),
            bool b => b ? CreateToken(SyntaxKind.TrueKeyword) : CreateToken(SyntaxKind.FalseKeyword),
            _ => (Node?)value,
        };
    }

    private Node? AsInitializer(Node? node)
    {
        return node != null ? Parenthesizer.ParenthesizeExpressionForDisallowedComma(node) : null;
    }

    /// <summary>`asToken(value: TKind | Token&lt;TKind&gt;)`: a kind becomes a token.</summary>
    private Node AsToken(object value)
    {
        return value is SyntaxKind kind ? CreateToken(kind) : (Node)value;
    }

    private Node? AsEmbeddedStatement(Node? statement)
    {
        return statement != null && FactoryNodeTests.IsNotEmittedStatement(statement) ? FactoryUtilitiesPublic.SetTextRange(SetOriginal(CreateEmptyStatement(), statement), statement) : statement;
    }

    /// <summary>`asVariableDeclaration(variableDeclaration: string | BindingName | VariableDeclaration | undefined)`.</summary>
    private VariableDeclaration? AsVariableDeclaration(object? variableDeclaration)
    {
        if (variableDeclaration is string || variableDeclaration != null && !FactoryNodeTests.IsVariableDeclaration((Node)variableDeclaration))
        {
            return CreateVariableDeclaration(
                variableDeclaration,
                /*exclamationToken*/ null,
                /*type*/ null,
                /*initializer*/ null);
        }
        return (VariableDeclaration?)variableDeclaration;
    }

    private T Update<T>(T updated, T original)
        where T : Node
    {
        if (updated != original)
        {
            SetOriginal(updated, original);
            FactoryUtilitiesPublic.SetTextRange(updated, original);
        }
        return updated;
    }

    private Node? UpdateOuterExpression(Node outerExpression, Node expression)
    {
        switch (outerExpression.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                return UpdateParenthesizedExpression((ParenthesizedExpression)outerExpression, expression);
            case SyntaxKind.TypeAssertionExpression:
                return UpdateTypeAssertion((TypeAssertion)outerExpression, ((TypeAssertion)outerExpression).Type, expression);
            case SyntaxKind.AsExpression:
                return UpdateAsExpression((AsExpression)outerExpression, expression, ((AsExpression)outerExpression).Type);
            case SyntaxKind.SatisfiesExpression:
                return UpdateSatisfiesExpression((SatisfiesExpression)outerExpression, expression, ((SatisfiesExpression)outerExpression).Type);
            case SyntaxKind.NonNullExpression:
                return UpdateNonNullExpression((NonNullExpression)outerExpression, expression);
            case SyntaxKind.ExpressionWithTypeArguments:
                return UpdateExpressionWithTypeArguments((ExpressionWithTypeArguments)outerExpression, expression, ((ExpressionWithTypeArguments)outerExpression).TypeArguments);
            case SyntaxKind.PartiallyEmittedExpression:
                return UpdatePartiallyEmittedExpression((PartiallyEmittedExpression)outerExpression, expression);
        }
        return null;
    }

    /// <summary>
    /// Determines whether a node is a parenthesized expression that can be ignored when recreating outer expressions.
    ///
    /// A parenthesized expression can be ignored when all of the following are true:
    ///
    /// - It's `pos` and `end` are not -1
    /// - It does not have a custom source map range
    /// - It does not have a custom comment range
    /// - It does not have synthetic leading or trailing comments
    ///
    /// If an outermost parenthesized expression is ignored, but the containing expression requires a parentheses around
    /// the expression to maintain precedence, a new parenthesized expression should be created automatically when
    /// the containing expression is created/updated.
    /// </summary>
    private static bool IsIgnorableParen(Node node)
    {
        return FactoryNodeTests.IsParenthesizedExpression(node)
            && Utilities.NodeIsSynthesized(node)
            && Utilities.NodeIsSynthesized(FactoryEmitNode.GetSourceMapRange(node))
            && Utilities.NodeIsSynthesized(FactoryEmitNode.GetCommentRange(node))
            && !Core.Some(FactoryEmitNode.GetSyntheticLeadingComments(node))
            && !Core.Some(FactoryEmitNode.GetSyntheticTrailingComments(node));
    }

    internal Node RestoreOuterExpressions(Node? outerExpression, Node innerExpression, OuterExpressionKinds kinds = OuterExpressionKinds.All)
    {
        if (outerExpression != null && FactoryUtilities.IsOuterExpression(outerExpression, kinds) && !IsIgnorableParen(outerExpression))
        {
            // (Every outer expression kind has an `expression`; NodeAccessors has no getter for it, so it is read per kind.)
            return UpdateOuterExpression(
                outerExpression,
                RestoreOuterExpressions(GetOuterExpressionExpression(outerExpression), innerExpression))!;
        }
        return innerExpression;
    }

    /// <summary>`outerExpression.expression` for the kinds of OuterExpression.</summary>
    private static Node GetOuterExpressionExpression(Node outerExpression)
    {
        return outerExpression.Kind switch
        {
            SyntaxKind.ParenthesizedExpression => ((ParenthesizedExpression)outerExpression).Expression,
            SyntaxKind.TypeAssertionExpression => ((TypeAssertion)outerExpression).Expression,
            SyntaxKind.AsExpression => ((AsExpression)outerExpression).Expression,
            SyntaxKind.SatisfiesExpression => ((SatisfiesExpression)outerExpression).Expression,
            SyntaxKind.NonNullExpression => ((NonNullExpression)outerExpression).Expression,
            SyntaxKind.ExpressionWithTypeArguments => ((ExpressionWithTypeArguments)outerExpression).Expression,
            SyntaxKind.PartiallyEmittedExpression => ((PartiallyEmittedExpression)outerExpression).Expression,
            _ => throw new InvalidOperationException($"{outerExpression.Kind} is not an outer expression"),
        };
    }

    //// File-level functions of nodeFactory.ts.

    internal static string GetDefaultTagNameForKind(SyntaxKind kind)
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
                // TypeScript: Debug.formatSyntaxKind(kind) (the enum member's name).
                Debug.Fail($"Unsupported kind: {kind}");
                return null!;
        }
    }

    internal static T SetOriginalNode<T>(T node, Node? original)
        where T : Node
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
            destEmitNode.LeadingComments = Core.AddRange(new List<SynthesizedComment>(leadingComments), destEmitNode.LeadingComments);
        }

        // `trailingComments` are concatenated with any existing trailing comments on the destination
        if (trailingComments != null)
        {
            // We use `.slice()` in case `destEmitNode.trailingComments` is pushed to later
            destEmitNode.TrailingComments = Core.AddRange(new List<SynthesizedComment>(trailingComments), destEmitNode.TrailingComments);
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
            destEmitNode.TokenSourceMapRanges = MergeTokenSourceMapRanges(tokenSourceMapRanges, destEmitNode.TokenSourceMapRanges);
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
    /// TypeScript iterates the (sparse) source array with `for (const key in sourceRanges)`; a C# list has no holes,
    /// so every index of the source list is copied.
    /// </summary>
    private static List<TextRange?> MergeTokenSourceMapRanges(List<TextRange?> sourceRanges, List<TextRange?>? destRanges)
    {
        destRanges ??= [];
        for (var key = 0; key < sourceRanges.Count; key++)
        {
            while (destRanges.Count <= key)
            {
                destRanges.Add(null);
            }
            destRanges[key] = sourceRanges[key];
        }
        return destRanges;
    }
}
