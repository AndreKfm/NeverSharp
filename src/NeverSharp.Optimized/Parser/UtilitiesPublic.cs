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

namespace NeverSharp.Optimized;

/// <summary>
/// types.ts: JSDocArray (`interface JSDocArray extends Array&lt;JSDoc&gt; { jsDocCache?: readonly JSDocTag[] | undefined; }`).
/// TypeScript adds the property jsDocCache to the node's jsDoc array; a C# list cannot gain a field, so the cache lives
/// in this subclass, and getJSDocTagsWorker replaces a node's plain list by a JSDocArray with the same elements when it
/// first caches tags on it.
/// </summary>
internal sealed class JSDocArray : List<Node>
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
    internal static Node FindAncestor(NodeStore store, Node node, Func<Node, bool> callback)
    {
        while (!node.IsNone)
        {
            var result = callback(node);
            if (result)
            {
                return node;
            }
            node = store.Parent(node);
        }
        return default;
    }

    /// <summary>
    /// Gets a value indicating whether a node originated in the parse tree.
    /// </summary>
    /// <param name="node">The node to test.</param>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsParseTreeNode(NodeStore store, Node node)
    {
        return (store.Flags(node) & NodeFlags.Synthesized) == 0;
    }

    /// <summary>
    /// Gets the original parse tree node for a node.
    /// </summary>
    /// <param name="node">The original node.</param>
    /// <param name="nodeTest">A callback used to ensure the correct type of parse tree node is returned.</param>
    /// <returns>The original parse tree node if found; otherwise, undefined.</returns>
    internal static Node GetParseTreeNode(NodeStore store, Node node, Func<Node, bool>? nodeTest = null)
    {
        if (node.IsNone || IsParseTreeNode(store, node))
        {
            return node;
        }

        node = store.Original(node);
        while (!node.IsNone)
        {
            if (IsParseTreeNode(store, node))
            {
                return nodeTest == null || nodeTest(node) ? node : default;
            }
            node = store.Original(node);
        }
        return default;
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

    /// <summary>Track O (O5a): <see cref="EscapeLeadingUnderscores(string)"/> on a text value (an escaped value goes to the character arena).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TextRef EscapeLeadingUnderscores(TextTable texts, TextRef identifier)
    {
        var span = texts.Span(identifier);
        return span.Length >= 2 && span[0] == (char)CharacterCodes._ && span[1] == (char)CharacterCodes._ ? texts.Concat("_", identifier) : identifier;
    }

    /// <summary>Track O (O5a): <see cref="UnescapeLeadingUnderscores(string)"/> on a text value (a slice of it).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TextRef UnescapeLeadingUnderscores(TextTable texts, TextRef identifier)
    {
        var id = texts.Span(identifier);
        return id.Length >= 3 && id[0] == (char)CharacterCodes._ && id[1] == (char)CharacterCodes._ && id[2] == (char)CharacterCodes._ ? identifier.Slice(1) : identifier;
    }

    /// <summary>TypeScript: `idText(identifierOrPrivateName: Identifier | PrivateIdentifier)`.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TextRef IdText(NodeStore store, Node identifierOrPrivateName)
    {
        var escapedText = (identifierOrPrivateName is var identifier && store.IsIdentifier(identifier)) ? store.EscapedText(identifier) : store.EscapedText(identifierOrPrivateName);
        return UnescapeLeadingUnderscores(store.Texts, escapedText);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNamedDeclaration(NodeStore store, Node node)
    {
        return !store.Name(node).IsNone; // A 'name' property should always be a DeclarationName.
    }

    internal static IReadOnlyList<Node>? GetModifiers(NodeStore store, Node node)
    {
        if (Utilities.HasSyntacticModifier(store, node, ModifierFlags.Modifier))
        {
            var modifiers = store.Modifiers(node);
            return modifiers.IsNone ? null : Core.Filter((IReadOnlyList<Node>)store.Nodes(modifiers).ToArray(), ((a0) => IsModifier(store, a0)));
        }
        return null;
    }

    /// <summary>`(param.parent as SignatureDeclaration).parameters`.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static NodeList GetParametersOfParent(NodeStore store, Node parent)
    {
        return parent switch
        {
            var n when store.IsArrowFunction(n) => store.Parameters(n),
            var n when store.IsCallSignatureDeclaration(n) => store.Parameters(n),
            var n when store.IsConstructSignatureDeclaration(n) => store.Parameters(n),
            var n when store.IsConstructorDeclaration(n) => store.Parameters(n),
            var n when store.IsConstructorTypeNode(n) => store.Parameters(n),
            var n when store.IsFunctionDeclaration(n) => store.Parameters(n),
            var n when store.IsFunctionExpression(n) => store.Parameters(n),
            var n when store.IsFunctionTypeNode(n) => store.Parameters(n),
            var n when store.IsGetAccessorDeclaration(n) => store.Parameters(n),
            var n when store.IsIndexSignatureDeclaration(n) => store.Parameters(n),
            var n when store.IsJSDocFunctionType(n) => store.Parameters(n),
            var n when store.IsMethodDeclaration(n) => store.Parameters(n),
            var n when store.IsMethodSignature(n) => store.Parameters(n),
            var n when store.IsSetAccessorDeclaration(n) => store.Parameters(n),
            _ => throw Debug.InvalidCast(store.Kind(parent)),
        };
    }

    private static IReadOnlyList<Node> GetJSDocParameterTagsWorker(NodeStore store, Node param, bool? noCache = null)
    {
        if (!store.Name(param).IsNone)
        {
            if (FactoryNodeTests.IsIdentifier(store, store.Name(param)))
            {
                var name = store.EscapedText(store.Name(param));
                var result = new List<Node>();
                foreach (var tag in GetJSDocTagsWorker(store, store.Parent(param), noCache))
                {
                    if (FactoryNodeTests.IsJSDocParameterTag(store, tag) && FactoryNodeTests.IsIdentifier(store, store.Name(tag)) && store.Texts.Equals(store.EscapedText(store.Name(tag)), name))
                    {
                        result.Add(tag);
                    }
                }
                return result;
            }
            else
            {
                var i = store.Nodes(GetParametersOfParent(store, store.Parent(param))).IndexOf(param);
                Debug.Assert(i > -1, "Parameters should always be in their parents' parameter list");
                var paramTags = new List<Node>();
                foreach (var tag in GetJSDocTagsWorker(store, store.Parent(param), noCache))
                {
                    if (FactoryNodeTests.IsJSDocParameterTag(store, tag))
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
    internal static IReadOnlyList<Node> GetJSDocParameterTags(NodeStore store, Node param)
    {
        return GetJSDocParameterTagsWorker(store, param, /*noCache*/ false);
    }

    internal static IReadOnlyList<Node> GetJSDocParameterTagsNoCache(NodeStore store, Node param)
    {
        return GetJSDocParameterTagsWorker(store, param, /*noCache*/ true);
    }

    private static List<Node> GetJSDocTypeParameterTagsWorker(NodeStore store, Node param, bool? noCache = null)
    {
        var name = store.EscapedText(store.Name(param));
        var result = new List<Node>();
        foreach (var tag in GetJSDocTagsWorker(store, store.Parent(param), noCache))
        {
            if (FactoryNodeTests.IsJSDocTemplateTag(store, tag) && Core.Some(store.Nodes(store.TypeParameters(tag)), tp => store.Texts.Equals(store.EscapedText(store.Name(tp)), name)))
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
    internal static IReadOnlyList<Node> GetJSDocTypeParameterTags(NodeStore store, Node param)
    {
        return GetJSDocTypeParameterTagsWorker(store, param, /*noCache*/ false);
    }

    internal static IReadOnlyList<Node> GetJSDocTypeParameterTagsNoCache(NodeStore store, Node param)
    {
        return GetJSDocTypeParameterTagsWorker(store, param, /*noCache*/ true);
    }

    internal static Node GetJSDocPublicTagNoCache(NodeStore store, Node node)
    {
        return GetFirstJSDocTag(store, node, ((a0) => FactoryNodeTests.IsJSDocPublicTag(store, a0)), /*noCache*/ true);
    }

    internal static Node GetJSDocPrivateTagNoCache(NodeStore store, Node node)
    {
        return GetFirstJSDocTag(store, node, ((a0) => FactoryNodeTests.IsJSDocPrivateTag(store, a0)), /*noCache*/ true);
    }

    internal static Node GetJSDocProtectedTagNoCache(NodeStore store, Node node)
    {
        return GetFirstJSDocTag(store, node, ((a0) => FactoryNodeTests.IsJSDocProtectedTag(store, a0)), /*noCache*/ true);
    }

    internal static Node GetJSDocReadonlyTagNoCache(NodeStore store, Node node)
    {
        return GetFirstJSDocTag(store, node, ((a0) => FactoryNodeTests.IsJSDocReadonlyTag(store, a0)), /*noCache*/ true);
    }

    internal static Node GetJSDocOverrideTagNoCache(NodeStore store, Node node)
    {
        return GetFirstJSDocTag(store, node, ((a0) => FactoryNodeTests.IsJSDocOverrideTag(store, a0)), /*noCache*/ true);
    }

    internal static Node GetJSDocDeprecatedTagNoCache(NodeStore store, Node node)
    {
        return GetFirstJSDocTag(store, node, ((a0) => FactoryNodeTests.IsJSDocDeprecatedTag(store, a0)), /*noCache*/ true);
    }

    [NoAllocUnchecked("rare path: the JSDoc tags of a node in a JavaScript file are looked up (and cached) through lists")]
    internal static Node GetJSDocTypeTag(NodeStore store, Node node)
    {
        // We should have already issued an error if there were multiple type jsdocs, so just use the first one.
        var tag = GetFirstJSDocTag(store, node, ((a0) => FactoryNodeTests.IsJSDocTypeTag(store, a0)));
        if (!tag.IsNone && !store.TypeExpression(tag).IsNone && !store.Type(store.TypeExpression(tag)).IsNone)
        {
            return tag;
        }
        return default;
    }

    /// <summary>
    /// The nodes without comments that getJSDocTagsWorker gave an empty jsDoc list (`node.jsDoc ??= []`) while the
    /// checker that owns this set runs on the thread (null outside a checker: the list is then written to the store, as
    /// during binding, which writes its file alone).
    /// </summary>
    [ThreadStatic]
    internal static HashSet<(NodeStore Store, int Node)>? CheckerEmptyJsDocLists;

    /// <summary>`!!node.jsDoc`: the node's own list, or the empty list getJSDocTagsWorker gave it.</summary>
    internal static bool HasJsDocList(NodeStore store, Node node) =>
        store.JsDoc(node) is not null || (CheckerEmptyJsDocLists?.Contains((store, node.Index)) ?? false);

    private static IReadOnlyList<Node> GetJSDocTagsWorker(NodeStore store, Node node, bool? noCache = null)
    {
        if (!Utilities.CanHaveJSDoc(store, node))
        {
            return Core.EmptyArray<Node>();
        }
        var jsDoc = store.JsDoc(node);
        var tags = (jsDoc as JSDocArray)?.JsDocCache;
        // If cache is 'null', that means we did the work of searching for JSDoc tags and came up with nothing.
        if (tags == null || noCache == true)
        {
            var comments = Utilities.GetJSDocCommentsAndTags(store, node, noCache);
            Debug.Assert(comments.Count < 2 || comments[0] != comments[1]);
            // flatMap(comments, j => isJSDoc(j) ? j.tags : j)
            List<Node>? flat = null;
            foreach (var j in comments)
            {
                if (FactoryNodeTests.IsJSDoc(store, j))
                {
                    var jTags = store.Tags(j);
                    if (!jTags.IsNone)
                    {
                        flat ??= [];
                        flat.AddRange(store.Nodes(jTags));
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
                if (array != null)
                {
                    array.JsDocCache = tags;
                }
                else if (CheckerEmptyJsDocLists is { } marks)
                {
                    // A checker runs on this thread: the files' stores are shared by the checkers of a program and read
                    // from several threads, so they are not written (a new jsDoc entry can move a store's object slots
                    // under a concurrent reader). The empty list that `node.jsDoc ??= []` gives a node without comments
                    // is observable (`decl.jsDoc` in getSignaturesOfSymbol): the checker records it; a node with
                    // comments keeps its list and its tags are computed again.
                    if (jsDoc == null)
                    {
                        marks.Add((store, node.Index));
                    }
                }
                else
                {
                    array = new JSDocArray();
                    if (jsDoc != null)
                    {
                        array.AddRange(jsDoc);
                    }
                    store.SetJsDoc(node, array);
                    array.JsDocCache = tags;
                }
            }
        }
        return tags;
    }

    /// <summary>utilitiesPublic.ts getJSDocTags (used by the checker's JavaScript and JSDoc typing, module C9d).</summary>
    internal static IReadOnlyList<Node> GetJSDocTags(NodeStore store, Node node) => GetJSDocTagsWorker(store, node);

    /// <summary>utilitiesPublic.ts getJSDocDeprecatedTag.</summary>
    internal static Node GetJSDocDeprecatedTag(NodeStore store, Node node) => GetFirstJSDocTag(store, node, tag => store.Kind(tag) == SyntaxKind.JSDocDeprecatedTag);

    /// <summary>Get the first JSDoc tag of a specified kind, or undefined if not present.</summary>
    private static Node GetFirstJSDocTag(NodeStore store, Node node, Func<Node, bool> predicate, bool? noCache = null)
    {
        return Core.Find(GetJSDocTagsWorker(store, node, noCache), (tag, _) => predicate(tag));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsCallChain(NodeStore store, Node node)
    {
        return FactoryNodeTests.IsCallExpression(store, node) && (store.Flags(node) & NodeFlags.OptionalChain) != 0;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsOptionalChain(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        return (store.Flags(node) & NodeFlags.OptionalChain) != 0 &&
            (kind == SyntaxKind.PropertyAccessExpression
                || kind == SyntaxKind.ElementAccessExpression
                || kind == SyntaxKind.CallExpression
                || kind == SyntaxKind.NonNullExpression);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node SkipPartiallyEmittedExpressions(NodeStore store, Node node)
    {
        return FactoryUtilities.SkipOuterExpressions(store, node, OuterExpressionKinds.PartiallyEmittedExpressions);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNonNullChain(NodeStore store, Node node)
    {
        return FactoryNodeTests.IsNonNullExpression(store, node) && (store.Flags(node) & NodeFlags.OptionalChain) != 0;
    }

    /// <summary>
    /// TypeScript: `hasProperty(array, "pos") && hasProperty(array, "end")`, i.e. whether createNodeArray has turned the
    /// array into a NodeArray (Track O: a list handle; a plain array is a list under construction or a span).
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNodeArray(object? array)
    {
        return array is NodeList;
    }

    // Literals

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsLiteralKind(SyntaxKind kind)
    {
        return SyntaxKind.FirstLiteralToken <= kind && kind <= SyntaxKind.LastLiteralToken;
    }

    // Pseudo-literals

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTemplateLiteralKind(SyntaxKind kind)
    {
        return SyntaxKind.FirstTemplateToken <= kind && kind <= SyntaxKind.LastTemplateToken;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsGeneratedIdentifier(NodeStore store, Node node)
    {
        return FactoryNodeTests.IsIdentifier(store, node) && ((EmitNode?)store.EmitNode(node))?.AutoGenerate != null;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsParameterPropertyModifier(SyntaxKind kind)
    {
        return (Utilities.ModifierToFlag(kind) & ModifierFlags.ParameterPropertyModifier) != 0;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsClassMemberModifier(SyntaxKind idToken)
    {
        return IsParameterPropertyModifier(idToken) ||
            idToken == SyntaxKind.StaticKeyword ||
            idToken == SyntaxKind.OverrideKeyword ||
            idToken == SyntaxKind.AccessorKeyword;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsModifier(NodeStore store, Node node)
    {
        return IsModifierKind(store.Kind(node));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPropertyName(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        return kind == SyntaxKind.Identifier
            || kind == SyntaxKind.PrivateIdentifier
            || kind == SyntaxKind.StringLiteral
            || kind == SyntaxKind.NumericLiteral
            || kind == SyntaxKind.ComputedPropertyName;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsObjectLiteralElementLike(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        return kind == SyntaxKind.PropertyAssignment
            || kind == SyntaxKind.ShorthandPropertyAssignment
            || kind == SyntaxKind.SpreadAssignment
            || kind == SyntaxKind.MethodDeclaration
            || kind == SyntaxKind.GetAccessor
            || kind == SyntaxKind.SetAccessor;
    }

    // Type

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsFunctionOrConstructorTypeNode(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
                return true;
        }

        return false;
    }

    // Binding patterns

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsBindingPattern(NodeStore store, Node node)
    {
        if (!node.IsNone)
        {
            var kind = store.Kind(node);
            return kind == SyntaxKind.ArrayBindingPattern
                || kind == SyntaxKind.ObjectBindingPattern;
        }

        return false;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAssignmentPattern(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        return kind == SyntaxKind.ArrayLiteralExpression
            || kind == SyntaxKind.ObjectLiteralExpression;
    }

    /// <summary>
    /// Determines whether the BindingOrAssignmentElement is a BindingElement-like declaration
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsDeclarationBindingElement(NodeStore store, Node bindingElement)
    {
        switch (store.Kind(bindingElement))
        {
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.Parameter:
            case SyntaxKind.BindingElement:
                return true;
        }

        return false;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsLeftHandSideExpression(NodeStore store, Node node)
    {
        return IsLeftHandSideExpressionKind(store.Kind(SkipPartiallyEmittedExpressions(store, node)));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsUnaryExpression(NodeStore store, Node node)
    {
        return IsUnaryExpressionKind(store.Kind(SkipPartiallyEmittedExpressions(store, node)));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsExpression(NodeStore store, Node node)
    {
        return IsExpressionKind(store.Kind(SkipPartiallyEmittedExpressions(store, node)));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocNode(NodeStore store, Node node)
    {
        return store.Kind(node) >= SyntaxKind.FirstJSDocNode && store.Kind(node) <= SyntaxKind.LastJSDocNode;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool HasJSDocNodes(NodeStore store, Node node)
    {
        if (!Utilities.CanHaveJSDoc(store, node))
        {
            return false;
        }

        var jsDoc = store.JsDoc(node);
        return jsDoc != null && jsDoc.Count > 0;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool HasInitializer(NodeStore store, Node node)
    {
        return !store.Initializer(node).IsNone;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsStringLiteralLike(NodeStore store, Node node)
    {
        return store.Kind(node) == SyntaxKind.StringLiteral || store.Kind(node) == SyntaxKind.NoSubstitutionTemplateLiteral;
    }
}
