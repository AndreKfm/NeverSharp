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
    Node CreateBaseSourceFileNode(SyntaxKind kind);

    Node CreateBaseIdentifierNode(SyntaxKind kind);

    Node CreateBasePrivateIdentifierNode(SyntaxKind kind);

    Node CreateBaseTokenNode(SyntaxKind kind);

    Node CreateBaseNode(SyntaxKind kind);
}

/// <summary>
/// JavaScript turns a plain array into a NodeArray by assigning `pos`, `end`, `hasTrailingComma` and
/// `transformFlags` to it (createNodeArray), and isNodeArray tests for the own properties `pos` and `end`.
/// The parser builds its lists as plain arrays before they become NodeArrays; a C# object cannot change its
/// class, so the list is a NodeArray object from the start and this field records whether the NodeArray
/// properties have been assigned yet.
/// </summary>
internal sealed partial class NodeArray
{
    internal bool HasNodeArrayProperties;
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

    internal BaseNodeFactory BaseFactory => baseFactory;

    internal NodeFactoryFlags Flags => flags;

    // @api
    internal static NodeArray CreateNodeArray(NodeStore store, IReadOnlyList<Node>? elements = null, bool? hasTrailingComma = null)
    {
        if (elements == null || ReferenceEquals(elements, Core.EmptyArray<Node>()))
        {
            elements = new NodeArray();
        }
        else if (UtilitiesPublic.IsNodeArray(elements))
        {
            var nodeArray = (NodeArray)elements;
            if (hasTrailingComma == null || nodeArray.HasTrailingComma == hasTrailingComma)
            {
                // Ensure the transform flags have been aggregated for this NodeArray
                // (TypeScript: `if (elements.transformFlags === undefined)`; createNodeArray assigns them to
                // every array it turns into a NodeArray, so they are defined here.)
                return nodeArray;
            }

            // This *was* a `NodeArray`, but the `hasTrailingComma` option differs. Recreate the
            // array with the same elements, text range, and transform flags but with the updated
            // value for `hasTrailingComma`
            var copy = new NodeArray();
            copy.AddRange(nodeArray);
            copy.HasNodeArrayProperties = true;
            copy.Pos = nodeArray.Pos;
            copy.End = nodeArray.End;
            copy.HasTrailingComma = hasTrailingComma.Value;
            copy.TransformFlags = nodeArray.TransformFlags;
            return copy;
        }

        // Since the element list of a node array is typically created by starting with an empty array and
        // repeatedly calling push(), the list may not have the optimal memory layout. We invoke slice() for
        // small arrays (1 to 4 elements) to give the VM a chance to allocate an optimal representation.
        // (C#: a plain array of the parser is a NodeArray object without the NodeArray properties and is
        // used as it is; the copy of slice() only concerns the JavaScript engine's memory layout. Any other
        // list is copied into a NodeArray, the class TypeScript's array becomes.)
        NodeArray array;
        if (elements is NodeArray plain)
        {
            array = plain;
        }
        else
        {
            array = new NodeArray();
            array.AddRange(elements);
        }
        array.HasNodeArrayProperties = true;
        array.Pos = -1;
        array.End = -1;
        array.HasTrailingComma = hasTrailingComma == true;
        array.TransformFlags = TransformFlags.None;
        AggregateChildrenFlags(store, array);
        return array;
    }

    private Node CreateBaseNode(SyntaxKind kind)
    {
        return baseFactory.CreateBaseNode(kind);
    }

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
    internal Node CreateNumericLiteral(string value, TokenFlags? numericLiteralFlags = null)
    {
        var flagsValue = numericLiteralFlags ?? TokenFlags.None;
        var text = value;
        Debug.Assert(text.Length == 0 || text[0] != (char)CharacterCodes.minus, "Negative numbers should be created in combination with createPrefixUnaryExpression");
        var node = CreateBaseDeclaration(SyntaxKind.NumericLiteral);
        store.SetText(node, text);
        store.SetNumericLiteralFlags(node, flagsValue);
        if ((flagsValue & TokenFlags.BinaryOrOctalSpecifier) != 0)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2015);
        }
        return node;
    }

    // @api
    internal Node CreateNumericLiteral(double value, TokenFlags? numericLiteralFlags = null)
    {
        // `typeof value === "number" ? value + "" : value`
        return CreateNumericLiteral(JsNumber.ToString(value), numericLiteralFlags);
    }

    // @api
    internal Node CreateBigIntLiteral(string value)
    {
        // TypeScript: `value: string | PseudoBigInt`; the parser passes the literal's text.
        var node = CreateBaseToken(SyntaxKind.BigIntLiteral);
        store.SetText(node, value);
        store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2020);
        return node;
    }

    private Node CreateBaseStringLiteral(string text, bool? isSingleQuote)
    {
        var node = CreateBaseDeclaration(SyntaxKind.StringLiteral);
        store.SetText(node, text);
        store.SetSingleQuote(node, isSingleQuote == true);
        return node;
    }

    // @api
    internal Node CreateStringLiteral(string text, bool? isSingleQuote = null, bool? hasExtendedUnicodeEscape = null)
    {
        var node = CreateBaseStringLiteral(text, isSingleQuote);
        store.SetHasExtendedUnicodeEscape(node, hasExtendedUnicodeEscape == true);
        if (hasExtendedUnicodeEscape == true)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2015);
        }
        return node;
    }

    // @api
    internal Node CreateRegularExpressionLiteral(string text)
    {
        var node = CreateBaseToken(SyntaxKind.RegularExpressionLiteral);
        store.SetText(node, text);
        return node;
    }

    //
    // Identifiers
    //

    private Node CreateBaseIdentifier(string escapedText)
    {
        var node = baseFactory.CreateBaseIdentifierNode(SyntaxKind.Identifier);
        store.SetEscapedText(node, escapedText);
        store.SetJsDoc(node, null); // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateIdentifier(string text, SyntaxKind? originalKeywordKind = null, bool? hasExtendedUnicodeEscape = null)
    {
        if (originalKeywordKind == null && text.Length != 0)
        {
            originalKeywordKind = Scanner.StringToToken(text);
        }
        if (originalKeywordKind == SyntaxKind.Identifier)
        {
            originalKeywordKind = null;
        }
        // (originalKeywordKind is not stored on the node; TypeScript computes it as above and does not use it further.)
        _ = originalKeywordKind;

        var node = CreateBaseIdentifier(UtilitiesPublic.EscapeLeadingUnderscores(text));
        if (hasExtendedUnicodeEscape == true)
        {
            store.SetFlags(node, store.Flags(node) | NodeFlags.IdentifierHasExtendedUnicodeEscape);
        }

        // NOTE: we do not include transform flags of typeArguments in an identifier as they do not contribute to transformations
        if ((string)store.EscapedText(node)! == "await")
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsPossibleTopLevelAwait);
        }
        if ((store.Flags(node) & NodeFlags.IdentifierHasExtendedUnicodeEscape) != 0)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2015);
        }

        return node;
    }

    private Node CreateBasePrivateIdentifier(string escapedText)
    {
        var node = baseFactory.CreateBasePrivateIdentifierNode(SyntaxKind.PrivateIdentifier);
        store.SetEscapedText(node, escapedText);
        store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsClassFields);
        return node;
    }

    // @api
    internal Node CreatePrivateIdentifier(string text)
    {
        if (!Core.StartsWith(text, "#"))
        {
            Debug.Fail("First character of private identifier must be #: " + text);
        }
        return CreateBasePrivateIdentifier(UtilitiesPublic.EscapeLeadingUnderscores(text));
    }

    //
    // Punctuation
    //

    private Node CreateBaseToken(SyntaxKind kind)
    {
        return baseFactory.CreateBaseTokenNode(kind);
    }

    // @api
    internal Node CreateToken(SyntaxKind token)
    {
        Debug.Assert(token >= SyntaxKind.FirstToken && token <= SyntaxKind.LastToken, "Invalid token");
        Debug.Assert(token <= SyntaxKind.FirstTemplateToken || token >= SyntaxKind.LastTemplateToken, "Invalid token. Use 'createTemplateLiteralLikeNode' to create template literals.");
        Debug.Assert(token <= SyntaxKind.FirstLiteralToken || token >= SyntaxKind.LastLiteralToken, "Invalid token. Use 'createLiteralLikeNode' to create literals.");
        Debug.Assert(token != SyntaxKind.Identifier, "Invalid token. Use 'createIdentifier' to create identifiers");
        var node = CreateBaseToken(token);
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
        if (transformFlags != TransformFlags.None)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | transformFlags);
        }
        return node;
    }

    //// File-level functions of nodeFactory.ts: propagation of transform flags.

    internal static TransformFlags PropagateNameFlags(NodeStore store, Node node)
    {
        return !node.IsNone && FactoryNodeTests.IsIdentifier(store, node) ? PropagateIdentifierNameFlags(store, node) : PropagateChildFlags(store, node);
    }

    internal static TransformFlags PropagateIdentifierNameFlags(NodeStore store, Node node)
    {
        // An IdentifierName is allowed to be `await`
        return PropagateChildFlags(store, node) & ~TransformFlags.ContainsPossibleTopLevelAwait;
    }

    internal static TransformFlags PropagatePropertyNameFlagsOfChild(NodeStore store, Node node, TransformFlags transformFlags)
    {
        return transformFlags | (store.TransformFlags(node) & TransformFlags.PropertyNamePropagatingFlags);
    }

    internal static TransformFlags PropagateChildFlags(NodeStore store, Node child)
    {
        if (child.IsNone)
        {
            return TransformFlags.None;
        }
        var childFlags = store.TransformFlags(child) & ~GetTransformFlagsSubtreeExclusions(store.Kind(child));
        if (UtilitiesPublic.IsNamedDeclaration(store, child))
        {
            var name = store.Name(child);
            if (UtilitiesPublic.IsPropertyName(store, name))
            {
                return PropagatePropertyNameFlagsOfChild(store, name, childFlags);
            }
        }
        return childFlags;
    }

    internal static TransformFlags PropagateChildrenFlags(NodeArray? children)
    {
        return children != null ? children.TransformFlags : TransformFlags.None;
    }

    internal static void AggregateChildrenFlags(NodeStore store, NodeArray children)
    {
        var subtreeFlags = TransformFlags.None;
        foreach (var child in children)
        {
            subtreeFlags |= PropagateChildFlags(store, child);
        }
        children.TransformFlags = subtreeFlags;
    }

    /// <summary>
    /// Gets the transform flags to exclude when unioning the transform flags of a subtree.
    /// </summary>
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
}
