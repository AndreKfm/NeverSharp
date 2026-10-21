// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go and internal/ast/subtreefacts.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

public abstract partial class Node
{
    /// <summary>
    /// TypeScript reads `child.name` of any node (`isNamedDeclaration(child)` is `!!child.name`), which `propagateChildFlags`
    /// uses. The node classes that have a `name` return it here (typescript-go: `Node.Name()`).
    /// </summary>
    internal virtual Node? NameForTransformFlags => null;
}

internal sealed partial class NodeFactory
{
    //
    // Outer expressions
    //

    internal Node UpdateOuterExpression(Node outerExpression, Node expression)
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

        // TypeScript falls off the end (returns undefined): unreachable for the kinds of an OuterExpression.
        return null!;
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
        return NodeTests.IsParenthesizedExpression(node)
            && Utilities.NodeIsSynthesized(node)
            && Utilities.NodeIsSynthesized(EmitNode.GetSourceMapRange(node))
            && Utilities.NodeIsSynthesized(EmitNode.GetCommentRange(node))
            && !Core.Some(EmitNode.GetSyntheticLeadingComments(node))
            && !Core.Some(EmitNode.GetSyntheticTrailingComments(node));
    }

    internal Node RestoreOuterExpressions(Node? outerExpression, Node innerExpression, OuterExpressionKinds kinds = OuterExpressionKinds.All)
    {
        if (outerExpression != null && FactoryUtilities.IsOuterExpression(outerExpression, kinds) && !IsIgnorableParen(outerExpression))
        {
            return UpdateOuterExpression(
                outerExpression,
                RestoreOuterExpressions(FactoryUtilities.GetExpressionOfOuterExpression(outerExpression), innerExpression));
        }
        return innerExpression;
    }

    //
    // Helpers (nodeFactory.ts, end of createNodeFactory)
    //

    internal NodeArray? AsNodeArray(IReadOnlyList<Node>? array)
    {
        return array != null ? CreateNodeArray(array) : null;
    }

    internal static Node? AsName(Node? name)
    {
        return name;
    }

    internal Node? AsName(string? name)
    {
        return name != null ? CreateIdentifier(name) : null;
    }

    internal Node? AsInitializer(Node? node)
    {
        return node != null ? parenthesizerRules.ParenthesizeExpressionForDisallowedComma(node) : null;
    }

    internal static Node AsToken(Node token)
    {
        return token;
    }

    internal Node AsToken(SyntaxKind kind)
    {
        return CreateToken(kind);
    }

    internal Node? AsEmbeddedStatement(Node? statement)
    {
        if (statement != null && NodeTests.IsNotEmittedStatement(statement))
        {
            return FactoryUtilitiesPublic.SetTextRange(SetOriginal(CreateEmptyStatement(), statement), statement);
        }
        return statement;
    }

    /// <param name="variableDeclaration">`string | BindingName | VariableDeclaration | undefined`.</param>
    internal VariableDeclaration? AsVariableDeclaration(object? variableDeclaration)
    {
        if (variableDeclaration is string name)
        {
            return CreateVariableDeclaration(
                CreateIdentifier(name),
                /*exclamationToken*/ null,
                /*type*/ null,
                /*initializer*/ null);
        }
        if (variableDeclaration is Node bindingName && !NodeTests.IsVariableDeclaration(bindingName))
        {
            return CreateVariableDeclaration(
                bindingName,
                /*exclamationToken*/ null,
                /*type*/ null,
                /*initializer*/ null);
        }
        return (VariableDeclaration?)variableDeclaration;
    }

    internal T Update<T>(T updated, T original)
        where T : Node
    {
        if (!ReferenceEquals(updated, original))
        {
            SetOriginal(updated, original);
            FactoryUtilitiesPublic.SetTextRange(updated, original);
        }
        return updated;
    }

    // TypeScript: `const setOriginal = flags & NodeFactoryFlags.NoOriginalNode ? identity : setOriginalNode;`
    internal T SetOriginal<T>(T node, Node? original)
        where T : Node
    {
        if ((flags & NodeFactoryFlags.NoOriginalNode) != 0)
        {
            return node;
        }
        return SetOriginalNode(node, original);
    }

    //
    // Utilities (file-level functions of nodeFactory.ts)
    //

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
                // TypeScript: Debug.formatSyntaxKind(kind), the name of the kind.
                Debug.Fail($"Unsupported kind: {kind}");
                return null!;
        }
    }

    internal static TransformFlags PropagateNameFlags(Node? node)
    {
        return node != null && NodeTests.IsIdentifier(node) ? PropagateIdentifierNameFlags(node) : PropagateChildFlags(node);
    }

    internal static TransformFlags PropagateIdentifierNameFlags(Node node)
    {
        // An IdentifierName is allowed to be `await`
        return PropagateChildFlags(node) & ~TransformFlags.ContainsPossibleTopLevelAwait;
    }

    internal static TransformFlags PropagatePropertyNameFlagsOfChild(Node? name, TransformFlags transformFlags)
    {
        return transformFlags | (name!.TransformFlags & TransformFlags.PropertyNamePropagatingFlags);
    }

    internal static TransformFlags PropagateChildFlags(Node? child)
    {
        if (child == null)
        {
            return TransformFlags.None;
        }
        var childFlags = child.TransformFlags & ~GetTransformFlagsSubtreeExclusions(child.Kind);
        // TypeScript: isNamedDeclaration(child) && isPropertyName(child.name)
        var name = child.NameForTransformFlags;
        return name != null && UtilitiesPublic.IsPropertyName(name) ? PropagatePropertyNameFlagsOfChild(name, childFlags) : childFlags;
    }

    internal static TransformFlags PropagateChildrenFlags(NodeArray? children)
    {
        return children != null ? children.TransformFlags : TransformFlags.None;
    }

    internal static void AggregateChildrenFlags(NodeArray children)
    {
        var subtreeFlags = TransformFlags.None;
        for (var i = 0; i < children.Count; i++)
        {
            subtreeFlags |= PropagateChildFlags(children[i]);
        }
        children.TransformFlags = subtreeFlags;
    }

    /// <summary>Gets the transform flags to exclude when unioning the transform flags of a subtree.</summary>
    internal static TransformFlags GetTransformFlagsSubtreeExclusions(SyntaxKind kind)
    {
        if (kind >= SyntaxKind.FirstTypeNode && kind <= SyntaxKind.LastTypeNode)
        {
            return TransformFlags.TypeExcludes;
        }

        switch (kind)
        {
            case SyntaxKind.CallExpression:
            case SyntaxKind.NewExpression:
            case SyntaxKind.ArrayLiteralExpression:
                return TransformFlags.ArrayLiteralOrCallOrNewExcludes;
            case SyntaxKind.ModuleDeclaration:
                return TransformFlags.ModuleExcludes;
            case SyntaxKind.Parameter:
                return TransformFlags.ParameterExcludes;
            case SyntaxKind.ArrowFunction:
                return TransformFlags.ArrowFunctionExcludes;
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionDeclaration:
                return TransformFlags.FunctionExcludes;
            case SyntaxKind.VariableDeclarationList:
                return TransformFlags.VariableDeclarationListExcludes;
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
                return TransformFlags.ClassExcludes;
            case SyntaxKind.Constructor:
                return TransformFlags.ConstructorExcludes;
            case SyntaxKind.PropertyDeclaration:
                return TransformFlags.PropertyExcludes;
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                return TransformFlags.MethodOrAccessorExcludes;
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.BigIntKeyword:
            case SyntaxKind.NeverKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.ObjectKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.TypeParameter:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.CallSignature:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
                return TransformFlags.TypeExcludes;
            case SyntaxKind.ObjectLiteralExpression:
                return TransformFlags.ObjectLiteralExcludes;
            case SyntaxKind.CatchClause:
                return TransformFlags.CatchClauseExcludes;
            case SyntaxKind.ObjectBindingPattern:
            case SyntaxKind.ArrayBindingPattern:
                return TransformFlags.BindingPatternExcludes;
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.SatisfiesExpression:
            case SyntaxKind.AsExpression:
            case SyntaxKind.PartiallyEmittedExpression:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.SuperKeyword:
                return TransformFlags.OuterExpressionExcludes;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                return TransformFlags.PropertyAccessExcludes;
            default:
                return TransformFlags.NodeExcludes;
        }
    }

    // TypeScript: the file-level `baseFactory`, `makeSynthetic`, `syntheticFactory`, the exported `factory`, `createSourceMapSource`
    // and `getCookedText` follow here; the parser has its own base factory, and the others are not reached by it.

    internal static T SetOriginalNode<T>(T node, Node? original)
        where T : Node
    {
        if (!ReferenceEquals(node.Original, original))
        {
            node.Original = original;
            if (original != null)
            {
                var emitNode = original.EmitNode;
                if (emitNode != null)
                {
                    node.EmitNode = MergeEmitNode((EmitNodeData)emitNode, (EmitNodeData?)node.EmitNode);
                }
            }
        }
        return node;
    }

    private static EmitNodeData MergeEmitNode(EmitNodeData sourceEmitNode, EmitNodeData? destEmitNode)
    {
        var emitFlags = sourceEmitNode.Flags;
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
        destEmitNode ??= new EmitNodeData();

        // NOTE: We should have one or more lines here for each property in EmitNode, even if the line
        // consists only of a comment indicating the property does not merge

        // `flags` overwrites the destination
        if (emitFlags != EmitFlags.None)
        {
            destEmitNode.Flags = emitFlags;
        }

        // `internalFlags` overwrites the destination. We do not copy over the immutability of the source.
        if (internalFlags != InternalEmitFlags.None)
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
            for (var i = 0; i < helpers.Count; i++)
            {
                destEmitNode.Helpers = Core.AppendIfUnique(destEmitNode.Helpers, helpers[i]);
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

    private static Dictionary<int, SourceMapRange?> MergeTokenSourceMapRanges(Dictionary<int, SourceMapRange?> sourceRanges, Dictionary<int, SourceMapRange?>? destRanges)
    {
        destRanges ??= new Dictionary<int, SourceMapRange?>();
        foreach (var (key, value) in sourceRanges)
        {
            destRanges[key] = value;
        }
        return destRanges;
    }
}

// The node classes that have a `name` (see Node.NameForTransformFlags).
public sealed partial class BindingElement
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class CallSignatureDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ClassDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ClassExpression
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ClassStaticBlockDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ConstructSignatureDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ConstructorDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ConstructorTypeNode
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class EnumDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class EnumMember
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ExportAssignment
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ExportDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ExportSpecifier
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class FunctionDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class FunctionExpression
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class FunctionTypeNode
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class GetAccessorDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

#pragma warning disable CA1711 // the class names are TypeScript's interface names (ImportAttribute, JsxAttribute, JsxSpreadAttribute)
public sealed partial class ImportAttribute
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ImportClause
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ImportEqualsDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ImportSpecifier
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class IndexSignatureDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class InterfaceDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class JSDocCallbackTag
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class JSDocFunctionType
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class JSDocLink
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class JSDocLinkCode
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class JSDocLinkPlain
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class JSDocNameReference
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class JSDocParameterTag
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class JSDocPropertyTag
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class JSDocSeeTag
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class JSDocTypedefTag
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class JsxAttribute
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class JsxNamespacedName
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class JsxSpreadAttribute
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class MetaProperty
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class MethodDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class MethodSignature
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class MissingDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ModuleDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class NamedTupleMember
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class NamespaceExport
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class NamespaceExportDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class NamespaceImport
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class NotEmittedTypeElement
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ParameterDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class PropertyAccessExpression
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class PropertyAssignment
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class PropertyDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class PropertySignature
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class SemicolonClassElement
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class SetAccessorDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class ShorthandPropertyAssignment
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class SpreadAssignment
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class TypeAliasDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class TypeParameterDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}

public sealed partial class VariableDeclaration
{
    internal override Node? NameForTransformFlags => Name;
}
