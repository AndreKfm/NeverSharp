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
/// types.ts: JSDocArray (`interface JSDocArray extends Array&lt;JSDoc&gt; { jsDocCache?: readonly JSDocTag[] | undefined; }`).
/// TypeScript adds the property jsDocCache to the node's jsDoc array; a C# list cannot gain a field, so the cache lives
/// in this subclass, and getJSDocTagsWorker replaces a node's plain list by a JSDocArray with the same elements when it
/// first caches tags on it.
/// </summary>
internal sealed class JSDocArray : List<JSDoc>
{
    public IReadOnlyList<Node>? JsDocCache;
}

/// <summary>utilitiesPublic.ts (the units the parser reaches; the text spans are in Core/UtilitiesPublic.cs).</summary>
internal static partial class UtilitiesPublic
{
    /// <summary>
    /// Iterates through the parent chain of a node and performs the callback on each parent until the callback
    /// returns a truthy value, then returns that value. (TypeScript's callback may also return "quit", which ends
    /// the search with undefined; no caller in the parser's units passes such a callback.)
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

    /// <summary>
    /// Gets a value indicating whether a node originated in the parse tree.
    /// </summary>
    /// <param name="node">The node to test.</param>
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
        return identifier.Length >= 2 && identifier[0] == (char)CharacterCodes._ && identifier[1] == (char)CharacterCodes._ ? "_" + identifier : identifier;
    }

    /// <summary>
    /// Remove extra underscore from escaped identifier text content.
    /// </summary>
    /// <param name="identifier">The escaped identifier text.</param>
    /// <returns>The unescaped identifier text.</returns>
    internal static string UnescapeLeadingUnderscores(string identifier)
    {
        var id = identifier;
        return id.Length >= 3 && id[0] == (char)CharacterCodes._ && id[1] == (char)CharacterCodes._ && id[2] == (char)CharacterCodes._ ? id[1..] : id;
    }

    /// <summary>TypeScript: `idText(identifierOrPrivateName: Identifier | PrivateIdentifier)`.</summary>
    internal static string IdText(Node identifierOrPrivateName)
    {
        var escapedText = identifierOrPrivateName is Identifier identifier ? identifier.EscapedText : ((PrivateIdentifier)identifierOrPrivateName).EscapedText;
        return UnescapeLeadingUnderscores((string)escapedText!);
    }

    internal static bool IsNamedDeclaration(Node node)
    {
        return NodeAccessors.GetName(node) != null; // A 'name' property should always be a DeclarationName.
    }

    internal static IReadOnlyList<Node>? GetModifiers(Node node)
    {
        if (Utilities.HasSyntacticModifier(node, ModifierFlags.Modifier))
        {
            return Core.Filter((IReadOnlyList<Node>?)NodeAccessors.GetModifiers(node), IsModifier);
        }
        return null;
    }

    /// <summary>`(param.parent as SignatureDeclaration).parameters`.</summary>
    private static NodeArray GetParametersOfParent(Node parent)
    {
        return parent switch
        {
            ArrowFunction n => n.Parameters,
            CallSignatureDeclaration n => n.Parameters,
            ConstructSignatureDeclaration n => n.Parameters,
            ConstructorDeclaration n => n.Parameters,
            ConstructorTypeNode n => n.Parameters,
            FunctionDeclaration n => n.Parameters,
            FunctionExpression n => n.Parameters,
            FunctionTypeNode n => n.Parameters,
            GetAccessorDeclaration n => n.Parameters,
            IndexSignatureDeclaration n => n.Parameters,
            JSDocFunctionType n => n.Parameters,
            MethodDeclaration n => n.Parameters,
            MethodSignature n => n.Parameters,
            SetAccessorDeclaration n => n.Parameters,
            _ => throw new InvalidCastException(parent.Kind.ToString()),
        };
    }

    private static IReadOnlyList<Node> GetJSDocParameterTagsWorker(ParameterDeclaration param, bool? noCache = null)
    {
        if (param.Name != null)
        {
            if (FactoryNodeTests.IsIdentifier(param.Name))
            {
                var name = (string)((Identifier)param.Name).EscapedText!;
                var result = new List<Node>();
                foreach (var tag in GetJSDocTagsWorker(param.Parent, noCache))
                {
                    if (FactoryNodeTests.IsJSDocParameterTag(tag) && FactoryNodeTests.IsIdentifier(((JSDocParameterTag)tag).Name) && (string)((Identifier)((JSDocParameterTag)tag).Name).EscapedText! == name)
                    {
                        result.Add(tag);
                    }
                }
                return result;
            }
            else
            {
                var i = GetParametersOfParent(param.Parent).IndexOf(param);
                Debug.Assert(i > -1, "Parameters should always be in their parents' parameter list");
                var paramTags = new List<Node>();
                foreach (var tag in GetJSDocTagsWorker(param.Parent, noCache))
                {
                    if (FactoryNodeTests.IsJSDocParameterTag(tag))
                    {
                        paramTags.Add(tag);
                    }
                }
                if (i < paramTags.Count)
                {
                    return [paramTags[i]];
                }
            }
        }
        // return empty array for: out-of-order binding patterns and JSDoc function syntax, which has un-named parameters
        return Core.EmptyArray<Node>();
    }

    /// <summary>
    /// Gets the JSDoc parameter tags for the node if present.
    /// </summary>
    /// <remarks>
    /// Returns any JSDoc param tag whose name matches the provided
    /// parameter, whether a param tag on a containing function
    /// expression, or a param tag on a variable declaration whose
    /// initializer is the containing function. The tags closest to the
    /// node are returned first, so in the previous example, the param
    /// tag on the containing function expression would be first.
    ///
    /// For binding patterns, parameter tags are matched by position.
    /// </remarks>
    internal static IReadOnlyList<Node> GetJSDocParameterTags(ParameterDeclaration param)
    {
        return GetJSDocParameterTagsWorker(param, /*noCache*/ false);
    }

    internal static IReadOnlyList<Node> GetJSDocParameterTagsNoCache(ParameterDeclaration param)
    {
        return GetJSDocParameterTagsWorker(param, /*noCache*/ true);
    }

    private static List<Node> GetJSDocTypeParameterTagsWorker(TypeParameterDeclaration param, bool? noCache = null)
    {
        var name = (string)param.Name.EscapedText!;
        var result = new List<Node>();
        foreach (var tag in GetJSDocTagsWorker(param.Parent, noCache))
        {
            if (FactoryNodeTests.IsJSDocTemplateTag(tag) && Core.Some(((JSDocTemplateTag)tag).TypeParameters, tp => (string)((TypeParameterDeclaration)tp).Name.EscapedText! == name))
            {
                result.Add(tag);
            }
        }
        return result;
    }

    /// <summary>
    /// Gets the JSDoc type parameter tags for the node if present.
    /// </summary>
    /// <remarks>
    /// Returns any JSDoc template tag whose names match the provided
    /// parameter, whether a template tag on a containing function
    /// expression, or a template tag on a variable declaration whose
    /// initializer is the containing function. The tags closest to the
    /// node are returned first, so in the previous example, the template
    /// tag on the containing function expression would be first.
    /// </remarks>
    internal static IReadOnlyList<Node> GetJSDocTypeParameterTags(TypeParameterDeclaration param)
    {
        return GetJSDocTypeParameterTagsWorker(param, /*noCache*/ false);
    }

    internal static IReadOnlyList<Node> GetJSDocTypeParameterTagsNoCache(TypeParameterDeclaration param)
    {
        return GetJSDocTypeParameterTagsWorker(param, /*noCache*/ true);
    }

    internal static JSDocPublicTag? GetJSDocPublicTagNoCache(Node node)
    {
        return (JSDocPublicTag?)GetFirstJSDocTag(node, FactoryNodeTests.IsJSDocPublicTag, /*noCache*/ true);
    }

    internal static JSDocPrivateTag? GetJSDocPrivateTagNoCache(Node node)
    {
        return (JSDocPrivateTag?)GetFirstJSDocTag(node, FactoryNodeTests.IsJSDocPrivateTag, /*noCache*/ true);
    }

    internal static JSDocProtectedTag? GetJSDocProtectedTagNoCache(Node node)
    {
        return (JSDocProtectedTag?)GetFirstJSDocTag(node, FactoryNodeTests.IsJSDocProtectedTag, /*noCache*/ true);
    }

    internal static JSDocReadonlyTag? GetJSDocReadonlyTagNoCache(Node node)
    {
        return (JSDocReadonlyTag?)GetFirstJSDocTag(node, FactoryNodeTests.IsJSDocReadonlyTag, /*noCache*/ true);
    }

    internal static JSDocOverrideTag? GetJSDocOverrideTagNoCache(Node node)
    {
        return (JSDocOverrideTag?)GetFirstJSDocTag(node, FactoryNodeTests.IsJSDocOverrideTag, /*noCache*/ true);
    }

    internal static JSDocDeprecatedTag? GetJSDocDeprecatedTagNoCache(Node node)
    {
        return (JSDocDeprecatedTag?)GetFirstJSDocTag(node, FactoryNodeTests.IsJSDocDeprecatedTag, /*noCache*/ true);
    }

    internal static JSDocTypeTag? GetJSDocTypeTag(Node node)
    {
        // We should have already issued an error if there were multiple type jsdocs, so just use the first one.
        var tag = (JSDocTypeTag?)GetFirstJSDocTag(node, FactoryNodeTests.IsJSDocTypeTag);
        if (tag != null && tag.TypeExpression != null && tag.TypeExpression.Type != null)
        {
            return tag;
        }
        return null;
    }

    private static IReadOnlyList<Node> GetJSDocTagsWorker(Node node, bool? noCache = null)
    {
        if (!Utilities.CanHaveJSDoc(node))
        {
            return Core.EmptyArray<Node>();
        }
        var jsDoc = JSDocContainers.GetJsDoc(node);
        var tags = (jsDoc as JSDocArray)?.JsDocCache;
        // If cache is 'null', that means we did the work of searching for JSDoc tags and came up with nothing.
        if (tags == null || noCache == true)
        {
            var comments = Utilities.GetJSDocCommentsAndTags(node, noCache);
            Debug.Assert(comments.Count < 2 || comments[0] != comments[1]);
            // flatMap(comments, j => isJSDoc(j) ? j.tags : j)
            List<Node>? flat = null;
            foreach (var j in comments)
            {
                if (FactoryNodeTests.IsJSDoc(j))
                {
                    var jTags = ((JSDoc)j).Tags;
                    if (jTags != null)
                    {
                        flat ??= [];
                        flat.AddRange(jTags);
                    }
                }
                else
                {
                    flat ??= [];
                    flat.Add(j);
                }
            }
            tags = flat ?? (IReadOnlyList<Node>)Core.EmptyArray<Node>();
            if (noCache != true)
            {
                // node.jsDoc ??= []; node.jsDoc.jsDocCache = tags;
                var array = jsDoc as JSDocArray;
                if (array == null)
                {
                    array = new JSDocArray();
                    if (jsDoc != null)
                    {
                        array.AddRange(jsDoc);
                    }
                    JSDocContainers.SetJsDoc(node, array);
                }
                array.JsDocCache = tags;
            }
        }
        return tags;
    }

    /// <summary>Get the first JSDoc tag of a specified kind, or undefined if not present.</summary>
    private static Node? GetFirstJSDocTag(Node node, Func<Node, bool> predicate, bool? noCache = null)
    {
        return Core.Find(GetJSDocTagsWorker(node, noCache), (tag, _) => predicate(tag));
    }

    internal static bool IsCallChain(Node node)
    {
        return FactoryNodeTests.IsCallExpression(node) && (node.Flags & NodeFlags.OptionalChain) != 0;
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
        return FactoryNodeTests.IsNonNullExpression(node) && (node.Flags & NodeFlags.OptionalChain) != 0;
    }

    /// <summary>
    /// TypeScript: `hasProperty(array, "pos") && hasProperty(array, "end")`, i.e. whether createNodeArray has turned the
    /// array into a NodeArray (see NodeArray.HasNodeArrayProperties).
    /// </summary>
    internal static bool IsNodeArray(object? array)
    {
        return array is NodeArray { HasNodeArrayProperties: true };
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

    internal static bool IsGeneratedIdentifier(Node node)
    {
        return FactoryNodeTests.IsIdentifier(node) && ((EmitNode?)node.EmitNode)?.AutoGenerate != null;
    }

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

    private static bool IsLeftHandSideExpressionKind(SyntaxKind kind)
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

    private static bool IsUnaryExpressionKind(SyntaxKind kind)
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

    private static bool IsExpressionKind(SyntaxKind kind)
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

    internal static bool IsJSDocNode(Node node)
    {
        return node.Kind >= SyntaxKind.FirstJSDocNode && node.Kind <= SyntaxKind.LastJSDocNode;
    }

    internal static bool HasJSDocNodes(Node node)
    {
        if (!Utilities.CanHaveJSDoc(node))
        {
            return false;
        }

        var jsDoc = JSDocContainers.GetJsDoc(node);
        return jsDoc != null && jsDoc.Count > 0;
    }

    internal static bool HasInitializer(Node node)
    {
        return NodeAccessors.GetInitializer(node) != null;
    }

    internal static bool IsStringLiteralLike(Node node)
    {
        return node.Kind == SyntaxKind.StringLiteral || node.Kind == SyntaxKind.NoSubstitutionTemplateLiteral;
    }
}
