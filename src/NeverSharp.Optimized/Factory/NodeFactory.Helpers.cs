// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Diagnostics.CodeAnalysis;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>nodeFactory.ts: the factory's lazily created helpers, the `as*` conversions, `update`, and the file-level utilities.</summary>
internal sealed partial class NodeFactory
{
    // Lazily load the parenthesizer, node converters, and some factory methods until they are used.
    // (TypeScript: `memoize(() => flags & NodeFactoryFlags.NoParenthesizerRules ? nullParenthesizerRules : createParenthesizerRules(factory))`.)
    // (Track O, step O12: the rules can only be nullParenthesizerRules, since createParenthesizerRules is not ported; the
    // getter returns that sealed class, so the factory's calls to it are direct and its identity members inline.)
    private FactoryParenthesizerRules.NullParenthesizerRulesObject? parenthesizerRulesValue;
    private NodeConverters? convertersValue;

    /// <summary>The factory's `parenthesizer` getter (TypeScript's memoized `parenthesizerRules()`).</summary>
    /// <remarks>
    /// Track O (step O29): the memoized value is returned in the caller, its creation is out of line
    /// (<see cref="CreateParenthesizer"/>); NativeAOT called the getter in the create functions of calls, property
    /// accesses and binary expressions.
    /// </remarks>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal FactoryParenthesizerRules.NullParenthesizerRulesObject Parenthesizer
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        get => parenthesizerRulesValue ?? CreateParenthesizer();
    }

    // The first call of the parenthesizer getter: memoizes the rules.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private FactoryParenthesizerRules.NullParenthesizerRulesObject CreateParenthesizer()
    {
        // (Track O, module D1: a factory without NoParenthesizerRules, the transformers', gets createParenthesizerRules'
        // object behind the sealed rules class when it is created, CreateTransformFactory; this getter's first call
        // memoizes the null rules of the parser's factory.)
        Debug.Assert((flags & NodeFactoryFlags.NoParenthesizerRules) != 0);
        parenthesizerRulesValue = FactoryParenthesizerRules.NullParenthesizerRules;
        return parenthesizerRulesValue;
    }

    /// <summary>The factory's `converters` getter (TypeScript's memoized `converters()`).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal NodeConverters Converters
    {
        get
        {
            if (convertersValue == null)
            {
                // (Track O, module D1: the transformers' factory gets createNodeConverters' object when it is created,
                // CreateTransformFactory.)
                Debug.Assert((flags & NodeFactoryFlags.NoNodeConverters) != 0);
                convertersValue = FactoryNodeConverters.NullNodeConverters;
            }
            return convertersValue;
        }
    }

    /// <summary>`const setOriginal = flags & NodeFactoryFlags.NoOriginalNode ? identity : setOriginalNode;`</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node SetOriginal(Node node, Node original)
    {
        return (flags & NodeFactoryFlags.NoOriginalNode) != 0 ? node : SetOriginalNode(store, node, original);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static NodeList AsNodeArray(NodeStore store, NodeList array)
    {
        return !array.IsNone ? CreateNodeArray(store, array) : default;
    }

    /// <summary>`asName(name: string | T)` for a node (Track O, O-NA: the factory's callers pass nodes, unboxed).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static Node AsName(Node name)
    {
        return name;
    }

    /// <summary>`asExpression(value: string | number | boolean | T)` for a node.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static Node AsExpression(Node value)
    {
        return value;
    }

    /// <summary>`asToken(value: TKind | Token&lt;TKind&gt;)` for a token node.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static Node AsToken(Node value)
    {
        return value;
    }

    /// <summary>`asVariableDeclaration(variableDeclaration)` for a node (None: undefined).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node AsVariableDeclaration(Node variableDeclaration)
    {
        if (!variableDeclaration.IsNone && !FactoryNodeTests.IsVariableDeclaration(store, variableDeclaration))
        {
            return CreateVariableDeclaration(
                variableDeclaration,
                /*exclamationToken*/ default,
                /*type*/ default,
                /*initializer*/ default);
        }
        return variableDeclaration;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node AsInitializer(Node node)
    {
        return !node.IsNone ? Parenthesizer.ParenthesizeExpressionForDisallowedComma(node) : default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node AsEmbeddedStatement(Node statement)
    {
        return !statement.IsNone && FactoryNodeTests.IsNotEmittedStatement(store, statement) ? FactoryUtilitiesPublic.SetTextRange(store, SetOriginal(CreateEmptyStatement(), statement), statement) : statement;
    }

    [DoesNotReturn]
    [NoAllocUnchecked("error path: a failed assertion throws")]
    private static void FailUnsupportedKind(SyntaxKind kind)
    {
        Debug.Fail($"Unsupported kind: {kind}");
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node Update(Node updated, Node original)
    {
        if (updated != original)
        {
            SetOriginal(updated, original);
            FactoryUtilitiesPublic.SetTextRange(store, updated, original);
        }
        return updated;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node UpdateOuterExpression(Node outerExpression, Node expression)
    {
        switch (store.Kind(outerExpression))
        {
            case SyntaxKind.ParenthesizedExpression:
                return UpdateParenthesizedExpression(outerExpression, expression);
            case SyntaxKind.TypeAssertionExpression:
                return UpdateTypeAssertion(outerExpression, store.Type(outerExpression), expression);
            case SyntaxKind.AsExpression:
                return UpdateAsExpression(outerExpression, expression, store.Type(outerExpression));
            case SyntaxKind.SatisfiesExpression:
                return UpdateSatisfiesExpression(outerExpression, expression, store.Type(outerExpression));
            case SyntaxKind.NonNullExpression:
                return UpdateNonNullExpression(outerExpression, expression);
            case SyntaxKind.ExpressionWithTypeArguments:
                return UpdateExpressionWithTypeArguments(outerExpression, expression, store.TypeArguments(outerExpression));
            case SyntaxKind.PartiallyEmittedExpression:
                return UpdatePartiallyEmittedExpression(outerExpression, expression);
        }
        return default;
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
    private static bool IsIgnorableParen(NodeStore store, Node node)
    {
        return FactoryNodeTests.IsParenthesizedExpression(store, node)
            && Utilities.NodeIsSynthesized(store, node)
            && Utilities.NodeIsSynthesized(FactoryEmitNode.GetSourceMapRange(store, node))
            && Utilities.NodeIsSynthesized(FactoryEmitNode.GetCommentRange(store, node))
            && !Core.Some(FactoryEmitNode.GetSyntheticLeadingComments(store, node))
            && !Core.Some(FactoryEmitNode.GetSyntheticTrailingComments(store, node));
    }

    internal Node RestoreOuterExpressions(Node outerExpression, Node innerExpression, OuterExpressionKinds kinds = OuterExpressionKinds.All)
    {
        if (!outerExpression.IsNone && FactoryUtilities.IsOuterExpression(store, outerExpression, kinds) && !IsIgnorableParen(store, outerExpression))
        {
            // (Every outer expression kind has an `expression`; NodeAccessors has no getter for it, so it is read per kind.)
            return UpdateOuterExpression(
                outerExpression,
                RestoreOuterExpressions(GetOuterExpressionExpression(store, outerExpression), innerExpression));
        }
        return innerExpression;
    }

    /// <summary>`outerExpression.expression` for the kinds of OuterExpression.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static Node GetOuterExpressionExpression(NodeStore store, Node outerExpression)
    {
        return store.Kind(outerExpression) switch
        {
            SyntaxKind.ParenthesizedExpression => store.Expression(outerExpression),
            SyntaxKind.TypeAssertionExpression => store.Expression(outerExpression),
            SyntaxKind.AsExpression => store.Expression(outerExpression),
            SyntaxKind.SatisfiesExpression => store.Expression(outerExpression),
            SyntaxKind.NonNullExpression => store.Expression(outerExpression),
            SyntaxKind.ExpressionWithTypeArguments => store.Expression(outerExpression),
            SyntaxKind.PartiallyEmittedExpression => store.Expression(outerExpression),
            _ => throw Debug.UnexpectedKind(store.Kind(outerExpression), "an outer expression"),
        };
    }

    //// File-level functions of nodeFactory.ts.

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
                FailUnsupportedKind(kind);
                return null!;
        }
    }

    [NoAllocUnchecked("rare path: not reached by the parser's factory, which sets no original nodes (NoOriginalNode)")]
    internal static Node SetOriginalNode(NodeStore store, Node node, Node original)
    {
        if (store.Original(node) != original)
        {
            store.SetOriginal(node, original);
            if (!original.IsNone)
            {
                var emitNode = (EmitNode?)store.EmitNode(original);
                if (emitNode != null)
                {
                    store.SetEmitNode(node, MergeEmitNode(store, emitNode, (EmitNode?)store.EmitNode(node)));
                }
            }
        }
        return node;
    }

    private static EmitNode MergeEmitNode(NodeStore store, EmitNode sourceEmitNode, EmitNode? destEmitNode)
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
        if (!classThis.IsNone)
        {
            destEmitNode.ClassThis = classThis;
        }

        // `assignedName` overwrites the destination
        if (!assignedName.IsNone)
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
