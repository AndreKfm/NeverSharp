// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilitiesPublic.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/utilities.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// The result of a `findAncestor` callback that may also answer "quit" (TypeScript: `boolean | "quit"`).
/// </summary>
internal enum FindAncestorResult
{
    False,
    True,
    Quit,
}

/// <summary>
/// utilitiesPublic.ts (the functions the parser and the factory reach; `createTextSpan` and
/// `createTextSpanFromBounds` are in Core/UtilitiesPublic.cs). TypeScript's unions of nodes (Expression,
/// PropertyName, HasModifiers, ...) are Node here; type predicates are plain bool functions.
/// </summary>
internal static partial class UtilitiesPublic
{
    /// <summary>
    /// Iterates through the parent chain of a node and performs the callback on each parent until the callback
    /// returns a truthy value, then returns that value.
    /// If no such value is found, it applies the callback until the parent pointer is undefined or the callback returns "quit"
    /// At that point findAncestor returns undefined.
    /// </summary>
    internal static Node? FindAncestor(Node? node, Func<Node, bool> callback)
    {
        while (node != null)
        {
            var result = callback(node);
            if (result)
            {
                return node;
            }
            node = node.Parent;
        }
        return null;
    }

    /// <summary>The overload of `findAncestor` whose callback may return "quit".</summary>
    internal static Node? FindAncestor(Node? node, Func<Node, FindAncestorResult> callback)
    {
        while (node != null)
        {
            var result = callback(node);
            if (result == FindAncestorResult.Quit)
            {
                return null;
            }
            else if (result == FindAncestorResult.True)
            {
                return node;
            }
            node = node.Parent;
        }
        return null;
    }

    /// <summary>
    /// Gets a value indicating whether a node originated in the parse tree.
    /// </summary>
    internal static bool IsParseTreeNode(Node node)
    {
        return (node.Flags & NodeFlags.Synthesized) == 0;
    }

    /// <summary>
    /// Gets the original parse tree node for a node.
    /// </summary>
    /// <param name="node">The original node.</param>
    /// <param name="nodeTest">A callback used to ensure the correct type of parse tree node is returned.</param>
    /// <returns>The original parse tree node if found; otherwise, undefined.</returns>
    internal static Node? GetParseTreeNode(Node? node, Func<Node, bool>? nodeTest = null)
    {
        if (node == null || IsParseTreeNode(node))
        {
            return node;
        }

        node = node.Original;
        while (node != null)
        {
            if (IsParseTreeNode(node))
            {
                return nodeTest == null || nodeTest(node) ? node : null;
            }
            node = node.Original;
        }
        return null;
    }

    /// <summary>Add an extra underscore to identifiers that start with two underscores to avoid issues with magic names like '__proto__'</summary>
    internal static string EscapeLeadingUnderscores(string identifier)
    {
        return identifier.Length >= 2 && identifier[0] == '_' && identifier[1] == '_' ? "_" + identifier : identifier;
    }

    /// <summary>
    /// Remove extra underscore from escaped identifier text content.
    /// </summary>
    /// <param name="identifier">The escaped identifier text.</param>
    /// <returns>The unescaped identifier text.</returns>
    internal static string UnescapeLeadingUnderscores(string identifier)
    {
        var id = identifier;
        return id.Length >= 3 && id[0] == '_' && id[1] == '_' && id[2] == '_' ? id.Substring(1) : id;
    }

    /// <summary>`identifierOrPrivateName` is an Identifier or a PrivateIdentifier.</summary>
    internal static string IdText(Node identifierOrPrivateName)
    {
        var escapedText = identifierOrPrivateName is PrivateIdentifier privateIdentifier
            ? privateIdentifier.EscapedText
            : ((Identifier)identifierOrPrivateName).EscapedText;
        return UnescapeLeadingUnderscores((string)escapedText!);
    }

    /// <summary>
    /// TypeScript: `!!(node as NamedDeclaration).name` (duck-typed: any node that has a truthy `name`).
    /// </summary>
    internal static bool IsNamedDeclaration(Node node)
    {
        return NameFieldOf(node) != null; // A 'name' property should always be a DeclarationName.
    }

    /// <summary>Reads the `name` property of the node classes that have one (C# has no duck typing).</summary>
    private static Node? NameFieldOf(Node node)
    {
        switch (node)
        {
            case BindingElement n:
                return n.Name;
            case CallSignatureDeclaration n:
                return n.Name;
            case ClassDeclaration n:
                return n.Name;
            case ClassExpression n:
                return n.Name;
            case ClassStaticBlockDeclaration n:
                return n.Name;
            case ConstructSignatureDeclaration n:
                return n.Name;
            case ConstructorDeclaration n:
                return n.Name;
            case ConstructorTypeNode n:
                return n.Name;
            case EnumDeclaration n:
                return n.Name;
            case EnumMember n:
                return n.Name;
            case ExportAssignment n:
                return n.Name;
            case ExportDeclaration n:
                return n.Name;
            case ExportSpecifier n:
                return n.Name;
            case FunctionDeclaration n:
                return n.Name;
            case FunctionExpression n:
                return n.Name;
            case FunctionTypeNode n:
                return n.Name;
            case GetAccessorDeclaration n:
                return n.Name;
            case ImportAttribute n:
                return n.Name;
            case ImportClause n:
                return n.Name;
            case ImportEqualsDeclaration n:
                return n.Name;
            case ImportSpecifier n:
                return n.Name;
            case IndexSignatureDeclaration n:
                return n.Name;
            case InterfaceDeclaration n:
                return n.Name;
            case JSDocCallbackTag n:
                return n.Name;
            case JSDocFunctionType n:
                return n.Name;
            case JSDocLink n:
                return n.Name;
            case JSDocLinkCode n:
                return n.Name;
            case JSDocLinkPlain n:
                return n.Name;
            case JSDocNameReference n:
                return n.Name;
            case JSDocParameterTag n:
                return n.Name;
            case JSDocPropertyTag n:
                return n.Name;
            case JSDocSeeTag n:
                return n.Name;
            case JSDocTypedefTag n:
                return n.Name;
            case JsxAttribute n:
                return n.Name;
            case JsxNamespacedName n:
                return n.Name;
            case JsxSpreadAttribute n:
                return n.Name;
            case MetaProperty n:
                return n.Name;
            case MethodDeclaration n:
                return n.Name;
            case MethodSignature n:
                return n.Name;
            case MissingDeclaration n:
                return n.Name;
            case ModuleDeclaration n:
                return n.Name;
            case NamedTupleMember n:
                return n.Name;
            case NamespaceExport n:
                return n.Name;
            case NamespaceExportDeclaration n:
                return n.Name;
            case NamespaceImport n:
                return n.Name;
            case NotEmittedTypeElement n:
                return n.Name;
            case ParameterDeclaration n:
                return n.Name;
            case PropertyAccessExpression n:
                return n.Name;
            case PropertyAssignment n:
                return n.Name;
            case PropertyDeclaration n:
                return n.Name;
            case PropertySignature n:
                return n.Name;
            case SemicolonClassElement n:
                return n.Name;
            case SetAccessorDeclaration n:
                return n.Name;
            case ShorthandPropertyAssignment n:
                return n.Name;
            case SpreadAssignment n:
                return n.Name;
            case TypeAliasDeclaration n:
                return n.Name;
            case TypeParameterDeclaration n:
                return n.Name;
            case VariableDeclaration n:
                return n.Name;
            default:
                // ArrowFunction declares `name?: undefined`; every other node has no `name`.
                return null;
        }
    }

    /// <summary>Reads the `modifiers` property of the node classes that have one (C# has no duck typing).</summary>
    private static NodeArray? ModifiersOf(Node node)
    {
        switch (node)
        {
            case ArrowFunction n:
                return n.Modifiers;
            case ClassDeclaration n:
                return n.Modifiers;
            case ClassExpression n:
                return n.Modifiers;
            case ClassStaticBlockDeclaration n:
                return n.Modifiers;
            case ConstructorDeclaration n:
                return n.Modifiers;
            case ConstructorTypeNode n:
                return n.Modifiers;
            case EnumDeclaration n:
                return n.Modifiers;
            case ExportAssignment n:
                return n.Modifiers;
            case ExportDeclaration n:
                return n.Modifiers;
            case FunctionDeclaration n:
                return n.Modifiers;
            case FunctionExpression n:
                return n.Modifiers;
            case FunctionTypeNode n:
                return n.Modifiers;
            case GetAccessorDeclaration n:
                return n.Modifiers;
            case ImportDeclaration n:
                return n.Modifiers;
            case ImportEqualsDeclaration n:
                return n.Modifiers;
            case IndexSignatureDeclaration n:
                return n.Modifiers;
            case InterfaceDeclaration n:
                return n.Modifiers;
            case MethodDeclaration n:
                return n.Modifiers;
            case MethodSignature n:
                return n.Modifiers;
            case MissingDeclaration n:
                return n.Modifiers;
            case ModuleDeclaration n:
                return n.Modifiers;
            case NamespaceExportDeclaration n:
                return n.Modifiers;
            case ParameterDeclaration n:
                return n.Modifiers;
            case PropertyAssignment n:
                return n.Modifiers;
            case PropertyDeclaration n:
                return n.Modifiers;
            case PropertySignature n:
                return n.Modifiers;
            case SetAccessorDeclaration n:
                return n.Modifiers;
            case ShorthandPropertyAssignment n:
                return n.Modifiers;
            case TypeAliasDeclaration n:
                return n.Modifiers;
            case TypeParameterDeclaration n:
                return n.Modifiers;
            case VariableStatement n:
                return n.Modifiers;
            default:
                return null;
        }
    }

    /// <summary>Reads the `parameters` property of a SignatureDeclaration | JSDocFunctionType.</summary>
    private static NodeArray? ParametersOf(Node node)
    {
        switch (node)
        {
            case ArrowFunction n:
                return n.Parameters;
            case CallSignatureDeclaration n:
                return n.Parameters;
            case ConstructSignatureDeclaration n:
                return n.Parameters;
            case ConstructorDeclaration n:
                return n.Parameters;
            case ConstructorTypeNode n:
                return n.Parameters;
            case FunctionDeclaration n:
                return n.Parameters;
            case FunctionExpression n:
                return n.Parameters;
            case FunctionTypeNode n:
                return n.Parameters;
            case GetAccessorDeclaration n:
                return n.Parameters;
            case IndexSignatureDeclaration n:
                return n.Parameters;
            case JSDocFunctionType n:
                return n.Parameters;
            case MethodDeclaration n:
                return n.Parameters;
            case MethodSignature n:
                return n.Parameters;
            case SetAccessorDeclaration n:
                return n.Parameters;
            default:
                return null;
        }
    }

    internal static List<Node>? GetModifiers(Node node)
    {
        if (Utilities.HasSyntacticModifier(node, ModifierFlags.Modifier))
        {
            return Core.Filter<Node>(ModifiersOf(node), IsModifier);
        }
        return null;
    }

    private static List<JSDocParameterTag> GetJSDocParameterTagsWorker(ParameterDeclaration param, bool noCache = false)
    {
        if (param.Name != null)
        {
            if (NodeTests.IsIdentifier(param.Name))
            {
                var name = (string?)((Identifier)param.Name).EscapedText;
                var result = new List<JSDocParameterTag>();
                foreach (var tag in GetJSDocTagsWorker(param.Parent, noCache))
                {
                    if (NodeTests.IsJSDocParameterTag(tag))
                    {
                        var parameterTag = (JSDocParameterTag)tag;
                        if (NodeTests.IsIdentifier(parameterTag.Name) && (string?)((Identifier)parameterTag.Name).EscapedText == name)
                        {
                            result.Add(parameterTag);
                        }
                    }
                }
                return result;
            }
            else
            {
                var parameters = ParametersOf(param.Parent);
                var i = -1;
                if (parameters != null)
                {
                    for (var index = 0; index < parameters.Count; index++)
                    {
                        if (ReferenceEquals(parameters[index], param))
                        {
                            i = index;
                            break;
                        }
                    }
                }
                Debug.Assert(i > -1, "Parameters should always be in their parents' parameter list");
                var paramTags = new List<JSDocParameterTag>();
                foreach (var tag in GetJSDocTagsWorker(param.Parent, noCache))
                {
                    if (NodeTests.IsJSDocParameterTag(tag))
                    {
                        paramTags.Add((JSDocParameterTag)tag);
                    }
                }
                if (i < paramTags.Count)
                {
                    return new List<JSDocParameterTag> { paramTags[i] };
                }
            }
        }
        // return empty array for: out-of-order binding patterns and JSDoc function syntax, which has un-named parameters
        return new List<JSDocParameterTag>();
    }

    /// <summary>
    /// Gets the JSDoc parameter tags for the node if present.
    ///
    /// Returns any JSDoc param tag whose name matches the provided
    /// parameter, whether a param tag on a containing function
    /// expression, or a param tag on a variable declaration whose
    /// initializer is the containing function. The tags closest to the
    /// node are returned first, so in the previous example, the param
    /// tag on the containing function expression would be first.
    ///
    /// For binding patterns, parameter tags are matched by position.
    /// </summary>
    internal static List<JSDocParameterTag> GetJSDocParameterTags(ParameterDeclaration param)
    {
        return GetJSDocParameterTagsWorker(param, /*noCache*/ false);
    }

    internal static List<JSDocParameterTag> GetJSDocParameterTagsNoCache(ParameterDeclaration param)
    {
        return GetJSDocParameterTagsWorker(param, /*noCache*/ true);
    }

    private static List<JSDocTemplateTag> GetJSDocTypeParameterTagsWorker(TypeParameterDeclaration param, bool noCache = false)
    {
        var name = (string?)param.Name.EscapedText;
        var result = new List<JSDocTemplateTag>();
        foreach (var tag in GetJSDocTagsWorker(param.Parent, noCache))
        {
            if (NodeTests.IsJSDocTemplateTag(tag))
            {
                var templateTag = (JSDocTemplateTag)tag;
                if (Core.Some(templateTag.TypeParameters, tp => (string?)((TypeParameterDeclaration)tp).Name.EscapedText == name))
                {
                    result.Add(templateTag);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Gets the JSDoc type parameter tags for the node if present.
    ///
    /// Returns any JSDoc template tag whose names match the provided
    /// parameter, whether a template tag on a containing function
    /// expression, or a template tag on a variable declaration whose
    /// initializer is the containing function. The tags closest to the
    /// node are returned first, so in the previous example, the template
    /// tag on the containing function expression would be first.
    /// </summary>
    internal static List<JSDocTemplateTag> GetJSDocTypeParameterTags(TypeParameterDeclaration param)
    {
        return GetJSDocTypeParameterTagsWorker(param, /*noCache*/ false);
    }

    internal static List<JSDocTemplateTag> GetJSDocTypeParameterTagsNoCache(TypeParameterDeclaration param)
    {
        return GetJSDocTypeParameterTagsWorker(param, /*noCache*/ true);
    }

    internal static JSDocPublicTag? GetJSDocPublicTagNoCache(Node node)
    {
        return (JSDocPublicTag?)GetFirstJSDocTag(node, NodeTests.IsJSDocPublicTag, /*noCache*/ true);
    }

    internal static JSDocPrivateTag? GetJSDocPrivateTagNoCache(Node node)
    {
        return (JSDocPrivateTag?)GetFirstJSDocTag(node, NodeTests.IsJSDocPrivateTag, /*noCache*/ true);
    }

    internal static JSDocProtectedTag? GetJSDocProtectedTagNoCache(Node node)
    {
        return (JSDocProtectedTag?)GetFirstJSDocTag(node, NodeTests.IsJSDocProtectedTag, /*noCache*/ true);
    }

    internal static JSDocReadonlyTag? GetJSDocReadonlyTagNoCache(Node node)
    {
        return (JSDocReadonlyTag?)GetFirstJSDocTag(node, NodeTests.IsJSDocReadonlyTag, /*noCache*/ true);
    }

    internal static JSDocOverrideTag? GetJSDocOverrideTagNoCache(Node node)
    {
        return (JSDocOverrideTag?)GetFirstJSDocTag(node, NodeTests.IsJSDocOverrideTag, /*noCache*/ true);
    }

    internal static JSDocDeprecatedTag? GetJSDocDeprecatedTagNoCache(Node node)
    {
        return (JSDocDeprecatedTag?)GetFirstJSDocTag(node, NodeTests.IsJSDocDeprecatedTag, /*noCache*/ true);
    }

    /// <summary>Gets the JSDoc type tag for the node if present and valid</summary>
    internal static JSDocTypeTag? GetJSDocTypeTag(Node node)
    {
        // We should have already issued an error if there were multiple type jsdocs, so just use the first one.
        var tag = (JSDocTypeTag?)GetFirstJSDocTag(node, NodeTests.IsJSDocTypeTag);
        if (tag != null && tag.TypeExpression != null && tag.TypeExpression.Type != null)
        {
            return tag;
        }
        return null;
    }

    /// <summary>
    /// TypeScript caches the result in `node.jsDoc.jsDocCache` (`jsDoc` is `JSDoc[] & { jsDocCache? }`); the
    /// generated `JsDoc` fields are plain lists without a cache slot, so the tags are collected on every call
    /// (what TypeScript does when `noCache` is set). The returned tags are the same.
    /// </summary>
    internal static IReadOnlyList<Node> GetJSDocTagsWorker(Node node, bool noCache = false)
    {
        if (!Utilities.CanHaveJSDoc(node))
        {
            return Core.EmptyArray<Node>();
        }
        var comments = Utilities.GetJSDocCommentsAndTags(node, noCache);
        Debug.Assert(comments.Count < 2 || !ReferenceEquals(comments[0], comments[1]));
        return Core.FlatMap<Node, Node>(
            comments,
            (j, _) => NodeTests.IsJSDoc(j) ? (IReadOnlyList<Node>?)((JSDoc)j).Tags : new List<Node> { j });
    }

    /// <summary>Get the first JSDoc tag of a specified kind, or undefined if not present.</summary>
    internal static Node? GetFirstJSDocTag(Node node, Func<Node, bool> predicate, bool noCache = false)
    {
        return Core.Find(GetJSDocTagsWorker(node, noCache), (tag, _) => predicate(tag));
    }

    internal static bool IsCallChain(Node node)
    {
        return NodeTests.IsCallExpression(node) && (node.Flags & NodeFlags.OptionalChain) != 0;
    }

    internal static bool IsOptionalChain(Node node)
    {
        var kind = node.Kind;
        return (node.Flags & NodeFlags.OptionalChain) != 0 &&
            (kind == SyntaxKind.PropertyAccessExpression
                || kind == SyntaxKind.ElementAccessExpression
                || kind == SyntaxKind.CallExpression
                || kind == SyntaxKind.NonNullExpression);
    }

    internal static Node SkipPartiallyEmittedExpressions(Node node)
    {
        return FactoryUtilities.SkipOuterExpressions(node, OuterExpressionKinds.PartiallyEmittedExpressions);
    }

    internal static bool IsNonNullChain(Node node)
    {
        return NodeTests.IsNonNullExpression(node) && (node.Flags & NodeFlags.OptionalChain) != 0;
    }

    /// <summary>
    /// TypeScript tests for the `pos` and `end` properties of the array; a NodeArray is the only list that has them.
    /// </summary>
    internal static bool IsNodeArray(IReadOnlyList<Node> array)
    {
        return array is NodeArray;
    }

    // Literals

    internal static bool IsLiteralKind(SyntaxKind kind)
    {
        return SyntaxKind.FirstLiteralToken <= kind && kind <= SyntaxKind.LastLiteralToken;
    }

    // Pseudo-literals

    internal static bool IsTemplateLiteralKind(SyntaxKind kind)
    {
        return SyntaxKind.FirstTemplateToken <= kind && kind <= SyntaxKind.LastTemplateToken;
    }

    // Identifiers

    internal static bool IsGeneratedIdentifier(Node node)
    {
        return NodeTests.IsIdentifier(node) && node.EmitNode is EmitNodeData { AutoGenerate: not null };
    }

    // Keywords

    internal static bool IsModifierKind(SyntaxKind token)
    {
        switch (token)
        {
            case SyntaxKind.AbstractKeyword:
            case SyntaxKind.AccessorKeyword:
            case SyntaxKind.AsyncKeyword:
            case SyntaxKind.ConstKeyword:
            case SyntaxKind.DeclareKeyword:
            case SyntaxKind.DefaultKeyword:
            case SyntaxKind.ExportKeyword:
            case SyntaxKind.InKeyword:
            case SyntaxKind.PublicKeyword:
            case SyntaxKind.PrivateKeyword:
            case SyntaxKind.ProtectedKeyword:
            case SyntaxKind.ReadonlyKeyword:
            case SyntaxKind.StaticKeyword:
            case SyntaxKind.OutKeyword:
            case SyntaxKind.OverrideKeyword:
                return true;
        }
        return false;
    }

    internal static bool IsParameterPropertyModifier(SyntaxKind kind)
    {
        return (Utilities.ModifierToFlag(kind) & ModifierFlags.ParameterPropertyModifier) != 0;
    }

    internal static bool IsClassMemberModifier(SyntaxKind idToken)
    {
        return IsParameterPropertyModifier(idToken) ||
            idToken == SyntaxKind.StaticKeyword ||
            idToken == SyntaxKind.OverrideKeyword ||
            idToken == SyntaxKind.AccessorKeyword;
    }

    internal static bool IsModifier(Node node)
    {
        return IsModifierKind(node.Kind);
    }

    internal static bool IsPropertyName(Node node)
    {
        var kind = node.Kind;
        return kind == SyntaxKind.Identifier
            || kind == SyntaxKind.PrivateIdentifier
            || kind == SyntaxKind.StringLiteral
            || kind == SyntaxKind.NumericLiteral
            || kind == SyntaxKind.ComputedPropertyName;
    }

    internal static bool IsObjectLiteralElementLike(Node node)
    {
        var kind = node.Kind;
        return kind == SyntaxKind.PropertyAssignment
            || kind == SyntaxKind.ShorthandPropertyAssignment
            || kind == SyntaxKind.SpreadAssignment
            || kind == SyntaxKind.MethodDeclaration
            || kind == SyntaxKind.GetAccessor
            || kind == SyntaxKind.SetAccessor;
    }

    // Type

    internal static bool IsFunctionOrConstructorTypeNode(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
                return true;
        }

        return false;
    }

    // Binding patterns

    internal static bool IsBindingPattern(Node? node)
    {
        if (node != null)
        {
            var kind = node.Kind;
            return kind == SyntaxKind.ArrayBindingPattern
                || kind == SyntaxKind.ObjectBindingPattern;
        }

        return false;
    }

    internal static bool IsAssignmentPattern(Node node)
    {
        var kind = node.Kind;
        return kind == SyntaxKind.ArrayLiteralExpression
            || kind == SyntaxKind.ObjectLiteralExpression;
    }

    /// <summary>
    /// Determines whether the BindingOrAssignmentElement is a BindingElement-like declaration
    /// </summary>
    internal static bool IsDeclarationBindingElement(Node bindingElement)
    {
        switch (bindingElement.Kind)
        {
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.Parameter:
            case SyntaxKind.BindingElement:
                return true;
        }

        return false;
    }

    internal static bool IsLeftHandSideExpression(Node node)
    {
        return IsLeftHandSideExpressionKind(SkipPartiallyEmittedExpressions(node).Kind);
    }

    internal static bool IsLeftHandSideExpressionKind(SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.NewExpression:
            case SyntaxKind.CallExpression:
            case SyntaxKind.JsxElement:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxFragment:
            case SyntaxKind.TaggedTemplateExpression:
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.Identifier:
            case SyntaxKind.PrivateIdentifier: // technically this is only an Expression if it's in a `#field in expr` BinaryExpression
            case SyntaxKind.RegularExpressionLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateExpression:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.ThisKeyword:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.SuperKeyword:
            case SyntaxKind.NonNullExpression:
            case SyntaxKind.ExpressionWithTypeArguments:
            case SyntaxKind.MetaProperty:
            case SyntaxKind.ImportKeyword: // technically this is only an Expression if it's in a CallExpression
            case SyntaxKind.MissingDeclaration:
                return true;
            default:
                return false;
        }
    }

    internal static bool IsUnaryExpression(Node node)
    {
        return IsUnaryExpressionKind(SkipPartiallyEmittedExpressions(node).Kind);
    }

    internal static bool IsUnaryExpressionKind(SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
            case SyntaxKind.DeleteExpression:
            case SyntaxKind.TypeOfExpression:
            case SyntaxKind.VoidExpression:
            case SyntaxKind.AwaitExpression:
            case SyntaxKind.TypeAssertionExpression:
                return true;
            default:
                return IsLeftHandSideExpressionKind(kind);
        }
    }

    /// <summary>
    /// Determines whether a node is an expression based only on its kind.
    /// </summary>
    internal static bool IsExpression(Node node)
    {
        return IsExpressionKind(SkipPartiallyEmittedExpressions(node).Kind);
    }

    internal static bool IsExpressionKind(SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.ConditionalExpression:
            case SyntaxKind.YieldExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.BinaryExpression:
            case SyntaxKind.SpreadElement:
            case SyntaxKind.AsExpression:
            case SyntaxKind.OmittedExpression:
            case SyntaxKind.CommaListExpression:
            case SyntaxKind.PartiallyEmittedExpression:
            case SyntaxKind.SatisfiesExpression:
                return true;
            default:
                return IsUnaryExpressionKind(kind);
        }
    }

    // JSDoc

    /// <summary>
    /// True if node is of some JSDoc syntax kind.
    /// </summary>
    internal static bool IsJSDocNode(Node node)
    {
        return node.Kind >= SyntaxKind.FirstJSDocNode && node.Kind <= SyntaxKind.LastJSDocNode;
    }

    /// <summary>
    /// True if has jsdoc nodes attached to it.
    /// </summary>
    internal static bool HasJSDocNodes(Node node)
    {
        if (!Utilities.CanHaveJSDoc(node))
        {
            return false;
        }

        var jsDoc = JSDocContainers.GetJsDoc(node);
        return jsDoc != null && jsDoc.Count > 0;
    }

    /// <summary>
    /// True if has initializer node attached to it (TypeScript: `!!(node as HasInitializer).initializer`, duck-typed).
    /// </summary>
    internal static bool HasInitializer(Node node)
    {
        switch (node)
        {
            case BindingElement n:
                return n.Initializer != null;
            case EnumMember n:
                return n.Initializer != null;
            case ForInStatement n:
                return n.Initializer != null;
            case ForOfStatement n:
                return n.Initializer != null;
            case ForStatement n:
                return n.Initializer != null;
            case JsxAttribute n:
                return n.Initializer != null;
            case ParameterDeclaration n:
                return n.Initializer != null;
            case PropertyAssignment n:
                return n.Initializer != null;
            case PropertyDeclaration n:
                return n.Initializer != null;
            case PropertySignature n:
                return n.Initializer != null;
            case VariableDeclaration n:
                return n.Initializer != null;
            default:
                return false;
        }
    }

    /// <summary>
    /// TypeScript's parameter is `Node | FileReference`; only the `Node` case is reached here.
    /// </summary>
    internal static bool IsStringLiteralLike(Node node)
    {
        return node.Kind == SyntaxKind.StringLiteral || node.Kind == SyntaxKind.NoSubstitutionTemplateLiteral;
    }
}
