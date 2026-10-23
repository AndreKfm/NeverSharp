// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Ported from src/compiler/factory/baseNodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Diagnostics.CodeAnalysis;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>nodeFactory.ts: NodeFactoryFlags.</summary>
[Flags]
internal enum NodeFactoryFlags
{
    None = 0,

    // Disables the parenthesizer rules for the factory.
    NoParenthesizerRules = 1 << 0,

    // Disables the node converters for the factory.
    NoNodeConverters = 1 << 1,

    // Ensures new `PropertyAccessExpression` nodes are created with the `NoIndentation` emit flag set.
    NoIndentationOnFreshPropertyAccess = 1 << 2,

    // Do not set an `original` pointer when updating a node.
    NoOriginalNode = 1 << 3,
}

/// <summary>baseNodeFactory.ts: BaseNodeFactory (creates the base Node objects; the parser's counts them).</summary>
internal interface BaseNodeFactory
{
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node CreateBaseSourceFileNode(SyntaxKind kind);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node CreateBaseIdentifierNode(SyntaxKind kind);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node CreateBasePrivateIdentifierNode(SyntaxKind kind);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node CreateBaseTokenNode(SyntaxKind kind);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    Node CreateBaseNode(SyntaxKind kind);
}

/// <summary>
/// nodeFactory.ts: createNodeFactory. The variables of its closure are the fields of the instance, its inner
/// functions the instance methods, and the NodeFactory object it returns is the instance itself.
/// </summary>
internal sealed partial class NodeFactory
{
    private readonly NodeFactoryFlags flags;
    private readonly BaseNodeFactory baseFactory;

    // Track O: the node store the base factory creates nodes in (set by the parser for each parse).
    internal NodeStore store = null!;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private NodeFactory(NodeFactoryFlags flags, BaseNodeFactory baseFactory)
    {
        this.flags = flags;
        this.baseFactory = baseFactory;
    }

    /// <summary>
    /// Creates a `NodeFactory` that can be used to create and update a syntax tree.
    /// </summary>
    /// <param name="flags">Flags that control factory behavior.</param>
    /// <param name="baseFactory">A `BaseNodeFactory` used to create the base `Node` objects.</param>
    internal static NodeFactory CreateNodeFactory(NodeFactoryFlags flags, BaseNodeFactory baseFactory)
    {
        var factory = new NodeFactory(flags, baseFactory);
        // TypeScript: `forEach(nodeFactoryPatchers, fn => fn(factory))`; nodeFactoryPatchers is empty unless
        // addNodeFactoryPatcher (deprecatedCompat, not reached by the parser) adds to it.
        return factory;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal BaseNodeFactory BaseFactory => baseFactory;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal NodeFactoryFlags Flags => flags;

    // @api
    // TypeScript's createNodeArray takes a plain array or a NodeArray (`elements?: readonly T[]`). Track O (step O7a): a
    // NodeArray is a list handle (NodeList), a plain array of the parser a list under construction (ListBuilder), and
    // any other array a span of node handles; JavaScript turns a plain array into a NodeArray by assigning `pos`,
    // `end`, `hasTrailingComma` and `transformFlags` to it, the list store copies it into its arena.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static NodeList CreateNodeArray(NodeStore store)
    {
        return CreateNodeArray(store, ReadOnlySpan<Node>.Empty);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static NodeList CreateNodeArray(NodeStore store, NodeList elements, bool? hasTrailingComma = null)
    {
        if (elements.IsNone)
        {
            return CreateNodeArray(store, ReadOnlySpan<Node>.Empty, hasTrailingComma);
        }

        if (hasTrailingComma == null || store.HasTrailingComma(elements) == hasTrailingComma)
        {
            // Ensure the transform flags have been aggregated for this NodeArray
            // (TypeScript: `if (elements.transformFlags === undefined)`; createNodeArray assigns them to
            // every array it turns into a NodeArray, so they are defined here.)
            return elements;
        }

        // This *was* a `NodeArray`, but the `hasTrailingComma` option differs. Recreate the
        // array with the same elements, text range, and transform flags but with the updated
        // value for `hasTrailingComma`
        var copy = store.NewList(store.Nodes(elements), hasTrailingComma.Value);
        store.SetPos(copy, store.Pos(elements));
        store.SetEnd(copy, store.End(elements));
        store.SetTransformFlags(copy, store.TransformFlags(elements));
        return copy;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static NodeList CreateNodeArray(NodeStore store, ListBuilder elements, bool? hasTrailingComma = null)
    {
        // Since the element list of a node array is typically created by starting with an empty array and
        // repeatedly calling push(), the list may not have the optimal memory layout. We invoke slice() for
        // small arrays (1 to 4 elements) to give the VM a chance to allocate an optimal representation.
        // (C#: the list store copies the plain array into its arena, with pos and end -1; Track O, step O28: with the
        // transform flags aggregated during the copy, NodeStore.FinishNodeArray.)
        return store.FinishNodeArray(elements, hasTrailingComma == true, -1, -1);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static NodeList CreateNodeArray(NodeStore store, ReadOnlySpan<Node> elements, bool? hasTrailingComma = null)
    {
        var array = store.NewList(elements, hasTrailingComma == true);
        AggregateChildrenFlags(store, array);
        return array;
    }

    // Track O (step O12): the base nodes are created in the store directly (NodeStore.CreateNode, as the parser's base
    // factory creates them; the store counts them for the parser's nodeCount), without the interface call. The hot
    // factory functions write their whole record with the generated `NodeStore.New{Class}` instead, the transform flags
    // computed beforehand from the children they are given.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateBaseNode(SyntaxKind kind)
    {
        return store.CreateNode(kind);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateBaseDeclaration(SyntaxKind kind)
    {
        var node = CreateBaseNode(kind);
        // node.symbol = undefined!; // initialized by binder
        // node.localSymbol = undefined; // initialized by binder
        // (The fields are typed per class in C# and are null after construction, which is what TypeScript assigns.)
        return node;
    }

    //
    // Literals
    //

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateNumericLiteral(TextRef value, TokenFlags? numericLiteralFlags = null)
    {
        var flagsValue = numericLiteralFlags ?? TokenFlags.None;
        var text = value;
        Debug.Assert(text.Length <= 0 || store.Texts.Span(text)[0] != (char)CharacterCodes.minus, "Negative numbers should be created in combination with createPrefixUnaryExpression");
        // (Track O, step O12: the record in one go, NodeStore.NewNumericLiteral.)
        return store.NewNumericLiteral(
            (flagsValue & TokenFlags.BinaryOrOctalSpecifier) != 0 ? TransformFlags.ContainsES2015 : TransformFlags.None,
            text: text,
            numericLiteralFlags: flagsValue);
    }

    // @api
    internal Node CreateNumericLiteral(double value, TokenFlags? numericLiteralFlags = null)
    {
        // `typeof value === "number" ? value + "" : value`
        return CreateNumericLiteral(store.Texts.Add(JsNumber.ToString(value)), numericLiteralFlags);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateBigIntLiteral(TextRef value)
    {
        // TypeScript: `value: string | PseudoBigInt`; the parser passes the literal's text.
        var node = store.CreateNode(SyntaxKind.BigIntLiteral, TransformFlags.ContainsES2020);
        store.SetText(node, value);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateStringLiteral(TextRef text, bool? isSingleQuote = null, bool? hasExtendedUnicodeEscape = null)
    {
        // (Track O, step O12: createBaseStringLiteral and the rest in one record write, NodeStore.NewStringLiteral.)
        return store.NewStringLiteral(
            hasExtendedUnicodeEscape == true ? TransformFlags.ContainsES2015 : TransformFlags.None,
            text: text,
            singleQuote: isSingleQuote == true,
            hasExtendedUnicodeEscape: hasExtendedUnicodeEscape == true);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateRegularExpressionLiteral(TextRef text)
    {
        var node = CreateBaseToken(SyntaxKind.RegularExpressionLiteral);
        store.SetText(node, text);
        return node;
    }

    //
    // Identifiers
    //

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateIdentifier(TextRef text, SyntaxKind? originalKeywordKind = null, bool? hasExtendedUnicodeEscape = null)
    {
        // (Track O: originalKeywordKind is computed only to be dropped below; the lookup of stringToToken is left out.)
        // (originalKeywordKind is not stored on the node; TypeScript computes it from the text and does not use it further.)
        _ = originalKeywordKind;

        // Track O (step O12): createBaseIdentifier (escapedText; jsDoc undefined, as on every new node) and the transform
        // flags in one record write (NodeStore.NewIdentifier); the flags of a new node are none, so the identifier has
        // IdentifierHasExtendedUnicodeEscape exactly when hasExtendedUnicodeEscape is true.
        var escapedText = UtilitiesPublic.EscapeLeadingUnderscores(store.Texts, text);
        var transformFlags = TransformFlags.None;

        // NOTE: we do not include transform flags of typeArguments in an identifier as they do not contribute to transformations
        // ("await" has 5 characters: TextRef.Size 6.)
        if (escapedText.Size == 6 && store.Texts.Equals(escapedText, "await"))
        {
            transformFlags |= TransformFlags.ContainsPossibleTopLevelAwait;
        }
        if (hasExtendedUnicodeEscape == true)
        {
            transformFlags |= TransformFlags.ContainsES2015;
        }

        var node = store.NewIdentifier(transformFlags, escapedText: escapedText);
        if (hasExtendedUnicodeEscape == true)
        {
            store.SetFlags(node, store.Flags(node) | NodeFlags.IdentifierHasExtendedUnicodeEscape);
        }
        return node;
    }

    /// <summary>Track O (O5a): createIdentifier with a constant text (the factory's default tag names), put in the character arena.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateIdentifier(string text)
    {
        return CreateIdentifier(store.Texts.Add(text));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateBasePrivateIdentifier(TextRef escapedText)
    {
        var node = store.CreateNode(SyntaxKind.PrivateIdentifier, TransformFlags.ContainsClassFields);
        store.SetEscapedText(node, escapedText);
        return node;
    }

    [DoesNotReturn]
    [NoAllocUnchecked("error path: a failed assertion throws")]
    private void FailPrivateIdentifier(TextRef text)
    {
        Debug.Fail("First character of private identifier must be #: " + store.Texts.GetString(text));
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreatePrivateIdentifier(TextRef text)
    {
        if (!store.Texts.Span(text).StartsWith("#"))
        {
            FailPrivateIdentifier(text);
        }
        return CreateBasePrivateIdentifier(UtilitiesPublic.EscapeLeadingUnderscores(store.Texts, text));
    }

    //
    // Punctuation
    //

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateBaseToken(SyntaxKind kind)
    {
        return store.CreateNode(kind);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateToken(SyntaxKind token)
    {
        Debug.Assert(token >= SyntaxKind.FirstToken && token <= SyntaxKind.LastToken, "Invalid token");
        Debug.Assert(token <= SyntaxKind.FirstTemplateToken || token >= SyntaxKind.LastTemplateToken, "Invalid token. Use 'createTemplateLiteralLikeNode' to create template literals.");
        Debug.Assert(token <= SyntaxKind.FirstLiteralToken || token >= SyntaxKind.LastLiteralToken, "Invalid token. Use 'createLiteralLikeNode' to create literals.");
        Debug.Assert(token != SyntaxKind.Identifier, "Invalid token. Use 'createIdentifier' to create identifiers");
        // (Track O, step O12: the transform flags first, then the node with them in one record write.)
        var transformFlags = TransformFlags.None;
        switch (token)
        {
            case SyntaxKind.AsyncKeyword:
                // 'async' modifier is ES2017 (async functions) or ES2018 (async generators)
                transformFlags = TransformFlags.ContainsES2017 |
                    TransformFlags.ContainsES2018;
                break;

            case SyntaxKind.UsingKeyword:
                transformFlags = TransformFlags.ContainsESNext;
                break;

            case SyntaxKind.PublicKeyword:
            case SyntaxKind.PrivateKeyword:
            case SyntaxKind.ProtectedKeyword:
            case SyntaxKind.ReadonlyKeyword:
            case SyntaxKind.AbstractKeyword:
            case SyntaxKind.DeclareKeyword:
            case SyntaxKind.ConstKeyword:
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.BigIntKeyword:
            case SyntaxKind.NeverKeyword:
            case SyntaxKind.ObjectKeyword:
            case SyntaxKind.InKeyword:
            case SyntaxKind.OutKeyword:
            case SyntaxKind.OverrideKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.UnknownKeyword:
            case SyntaxKind.UndefinedKeyword: // `undefined` is an Identifier in the expression case.
                transformFlags = TransformFlags.ContainsTypeScript;
                break;
            case SyntaxKind.SuperKeyword:
                transformFlags = TransformFlags.ContainsES2015 | TransformFlags.ContainsLexicalSuper;
                break;
            case SyntaxKind.StaticKeyword:
                transformFlags = TransformFlags.ContainsES2015;
                break;
            case SyntaxKind.AccessorKeyword:
                transformFlags = TransformFlags.ContainsClassFields;
                break;
            case SyntaxKind.ThisKeyword:
                // 'this' indicates a lexical 'this'
                transformFlags = TransformFlags.ContainsLexicalThis;
                break;
        }
        return store.CreateNode(token, transformFlags);
    }

    //// File-level functions of nodeFactory.ts: propagation of transform flags.

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TransformFlags PropagateNameFlags(NodeStore store, Node node)
    {
        return !node.IsNone && FactoryNodeTests.IsIdentifier(store, node) ? PropagateIdentifierNameFlags(store, node) : PropagateChildFlags(store, node);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TransformFlags PropagateIdentifierNameFlags(NodeStore store, Node node)
    {
        // An IdentifierName is allowed to be `await`
        return PropagateChildFlags(store, node) & ~TransformFlags.ContainsPossibleTopLevelAwait;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TransformFlags PropagatePropertyNameFlagsOfChild(NodeStore store, Node node, TransformFlags transformFlags)
    {
        return transformFlags | (store.TransformFlags(node) & TransformFlags.PropertyNamePropagatingFlags);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TransformFlags PropagateChildFlags(NodeStore store, Node child)
    {
        if (child.IsNone)
        {
            return TransformFlags.None;
        }
        var childFlags = store.TransformFlags(child) & ~GetTransformFlagsSubtreeExclusions(store.Kind(child));
        // isNamedDeclaration(child) && isPropertyName(child.name) (Track O, step O12: the name read once).
        var name = store.Name(child);
        if (!name.IsNone && UtilitiesPublic.IsPropertyName(store, name))
        {
            return PropagatePropertyNameFlagsOfChild(store, name, childFlags);
        }
        return childFlags;
    }

    // (The store of the factory; header 0, an undefined list, has no transform flags.)
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal TransformFlags PropagateChildrenFlags(NodeList children)
    {
        return !children.IsNone ? store.TransformFlags(children) : TransformFlags.None;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static void AggregateChildrenFlags(NodeStore store, NodeList children)
    {
        var subtreeFlags = TransformFlags.None;
        foreach (var child in store.Nodes(children))
        {
            subtreeFlags |= PropagateChildFlags(store, child);
        }
        store.SetTransformFlags(children, subtreeFlags);
    }

    /// <summary>
    /// Gets the transform flags to exclude when unioning the transform flags of a subtree.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TransformFlags GetTransformFlagsSubtreeExclusions(SyntaxKind kind)
    {
        // Track O (step O12): a table lookup instead of TypeScript's range test and switch (`kind >= FirstTypeNode &&
        // kind <= LastTypeNode` gives TypeExcludes, then one case list per exclusion, NodeExcludes by default); the
        // generator evaluates TypeScript's function on every kind (NodeStore.g.cs, emit-optimized.mjs).
        return NodeStore.TransformFlagsSubtreeExclusions(kind);
    }
}
