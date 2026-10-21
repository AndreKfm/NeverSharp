// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts and src/compiler/factory/baseNodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>nodeFactory.ts: NodeFactoryFlags.</summary>
[Flags]
internal enum NodeFactoryFlags
{
    None = 0,

    /// <summary>Disables the parenthesizer rules for the factory.</summary>
    NoParenthesizerRules = 1 << 0,

    /// <summary>Disables the node converters for the factory.</summary>
    NoNodeConverters = 1 << 1,

    /// <summary>Ensures new `PropertyAccessExpression` nodes are created with the `NoIndentation` emit flag set.</summary>
    NoIndentationOnFreshPropertyAccess = 1 << 2,

    /// <summary>Do not set an `original` pointer when updating a node.</summary>
    NoOriginalNode = 1 << 3,
}

/// <summary>baseNodeFactory.ts: BaseNodeFactory (the factory's source of fresh node objects).</summary>
internal interface BaseNodeFactory
{
    Node CreateBaseSourceFileNode(SyntaxKind kind);

    Node CreateBaseIdentifierNode(SyntaxKind kind);

    Node CreateBasePrivateIdentifierNode(SyntaxKind kind);

    Node CreateBaseTokenNode(SyntaxKind kind);

    Node CreateBaseNode(SyntaxKind kind);
}

/// <summary>
/// nodeFactory.ts: createNodeFactory. The instance is what `createNodeFactory` returns (its closure state is the
/// fields below; the functions of the returned `factory` object are the members of this class).
/// </summary>
internal sealed partial class NodeFactory
{
    // nodeFactoryPatchers (`addNodeFactoryPatcher`) is a list of functions run on every new factory; nothing registers
    // a patcher in the parser's slice, so it is not ported.

    internal readonly NodeFactoryFlags flags;

    internal readonly BaseNodeFactory baseFactory;

    internal NodeFactory(NodeFactoryFlags flags, BaseNodeFactory baseFactory)
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
        return new NodeFactory(flags, baseFactory);
    }

    // TypeScript: `const parenthesizerRules = memoize(() => flags & NoParenthesizerRules ? nullParenthesizerRules : createParenthesizerRules(factory))`
    // (lazy: createParenthesizerRules is only evaluated when the rules are used; it is not part of M34).
#pragma warning disable CA1859 // typed as the interface, like TypeScript's ParenthesizerRules and NodeConverters
    private ParenthesizerRules parenthesizerRules
    {
        get
        {
            if ((flags & NodeFactoryFlags.NoParenthesizerRules) != 0)
            {
                return NullParenthesizerRules.Instance;
            }
            throw new NotImplementedException("createParenthesizerRules is not part of M34");
        }
    }

    // TypeScript: `const converters = memoize(() => flags & NoNodeConverters ? nullNodeConverters : createNodeConverters(factory))`.
    private NodeConverters converters
    {
        get
        {
            if ((flags & NodeFactoryFlags.NoNodeConverters) != 0)
            {
                return NullNodeConverters.Instance;
            }
            throw new NotImplementedException("createNodeConverters is not part of M34");
        }
    }

#pragma warning restore CA1859

    /// <summary>The `parenthesizer` property of the factory object.</summary>
    internal ParenthesizerRules Parenthesizer => parenthesizerRules;

    /// <summary>The `converters` property of the factory object.</summary>
    internal NodeConverters Converters => converters;

    // @api
#pragma warning disable CA1822 // faithfulness: createNodeArray is a function of the factory object (TypeScript closure; the parser calls factory.CreateNodeArray)
    internal NodeArray CreateNodeArray(IReadOnlyList<Node>? elements, bool? hasTrailingComma = null)
    {
        // TypeScript: `if (elements === undefined || elements === emptyArray) elements = []`: both continue below as an
        // empty array (an empty list takes the same path as the freshly created `[]`).
        if (elements == null)
        {
            elements = new List<Node>();
        }
        else if (elements is NodeArray nodeArray)
        {
            if (hasTrailingComma == null || nodeArray.HasTrailingComma == hasTrailingComma.Value)
            {
                // Ensure the transform flags have been aggregated for this NodeArray
                // (a NodeArray's `transformFlags` is never undefined in C#: None stands for "not aggregated yet").
                if (nodeArray.TransformFlags == TransformFlags.None)
                {
                    AggregateChildrenFlags(nodeArray);
                }
                return nodeArray;
            }

            // This *was* a `NodeArray`, but the `hasTrailingComma` option differs. Recreate the
            // array with the same elements, text range, and transform flags but with the updated
            // value for `hasTrailingComma`
            var copy = new NodeArray(nodeArray);
            copy.Pos = nodeArray.Pos;
            copy.End = nodeArray.End;
            copy.HasTrailingComma = hasTrailingComma.Value;
            copy.TransformFlags = nodeArray.TransformFlags;
            return copy;
        }

        // TypeScript adopts the caller's array (slice() for 1 to 4 elements); a C# NodeArray is a list of its own.
        var array = new NodeArray(elements);
        array.Pos = -1;
        array.End = -1;
        array.HasTrailingComma = hasTrailingComma == true;
        array.TransformFlags = TransformFlags.None;
        AggregateChildrenFlags(array);
        return array;
    }
#pragma warning restore CA1822

    private T CreateBaseNode<T>(SyntaxKind kind)
        where T : Node
    {
        return (T)baseFactory.CreateBaseNode(kind);
    }

    private T CreateBaseDeclaration<T>(SyntaxKind kind)
        where T : Node
    {
        // TypeScript also sets `symbol` and `localSymbol` to undefined (initialized by the binder): the fields default to null.
        return CreateBaseNode<T>(kind);
    }

    //
    // Literals
    //

    // @api
    internal NumericLiteral CreateNumericLiteral(string value, TokenFlags numericLiteralFlags = TokenFlags.None)
    {
        var text = value;
        Debug.Assert(text.Length == 0 || text[0] != '-', "Negative numbers should be created in combination with createPrefixUnaryExpression");
        var node = CreateBaseDeclaration<NumericLiteral>(SyntaxKind.NumericLiteral);
        node.Text = text;
        node.NumericLiteralFlags = numericLiteralFlags;
        if ((numericLiteralFlags & TokenFlags.BinaryOrOctalSpecifier) != 0)
        {
            node.TransformFlags |= TransformFlags.ContainsES2015;
        }
        return node;
    }

    // @api
    internal BigIntLiteral CreateBigIntLiteral(string value)
    {
        var node = CreateBaseToken<BigIntLiteral>(SyntaxKind.BigIntLiteral);
        node.Text = value;
        node.TransformFlags |= TransformFlags.ContainsES2020;
        return node;
    }

    private StringLiteral CreateBaseStringLiteral(string text, bool? isSingleQuote)
    {
        var node = CreateBaseDeclaration<StringLiteral>(SyntaxKind.StringLiteral);
        node.Text = text;
        node.SingleQuote = isSingleQuote == true;
        return node;
    }

    // @api
    internal StringLiteral CreateStringLiteral(string text, bool? isSingleQuote = null, bool? hasExtendedUnicodeEscape = null)
    {
        var node = CreateBaseStringLiteral(text, isSingleQuote);
        node.HasExtendedUnicodeEscape = hasExtendedUnicodeEscape == true;
        if (hasExtendedUnicodeEscape == true)
        {
            node.TransformFlags |= TransformFlags.ContainsES2015;
        }
        return node;
    }

    // @api
    internal StringLiteral CreateStringLiteralFromNode(Node sourceNode)
    {
        var node = CreateBaseStringLiteral(Utilities.GetTextOfIdentifierOrLiteral(sourceNode), /*isSingleQuote*/ null);
        node.TextSourceNode = sourceNode;
        return node;
    }

    // @api
    internal RegularExpressionLiteral CreateRegularExpressionLiteral(string text)
    {
        var node = CreateBaseToken<RegularExpressionLiteral>(SyntaxKind.RegularExpressionLiteral);
        node.Text = text;
        return node;
    }

    // @api
    internal Node CreateLiteralLikeNode(SyntaxKind kind, string text)
    {
        switch (kind)
        {
            case SyntaxKind.NumericLiteral:
                return CreateNumericLiteral(text, /*numericLiteralFlags*/ TokenFlags.None);
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
                return CreateTemplateLiteralLikeNode(kind, text, /*rawText*/ null, /*templateFlags*/ TokenFlags.None);
        }

        // TypeScript falls off the end (returns undefined): unreachable for the kinds of a LiteralToken.
        return null!;
    }

    //
    // Identifiers
    //

    private Identifier CreateBaseIdentifier(string escapedText)
    {
        var node = (Identifier)baseFactory.CreateBaseIdentifierNode(SyntaxKind.Identifier);
        node.EscapedText = escapedText;
        // jsDoc, flowNode and symbol are initialized by the parser, binder and checker: they default to null.
        return node;
    }

    // @api
    internal Identifier CreateIdentifier(string text, SyntaxKind? originalKeywordKind = null, bool? hasExtendedUnicodeEscape = null)
    {
        // TypeScript computes `originalKeywordKind` here (stringToToken(text), undefined for SyntaxKind.Identifier) and then
        // no longer uses it: the node does not store it (the parameter is kept for the API).
        var escapedText = UtilitiesPublic.EscapeLeadingUnderscores(text);
        var node = CreateBaseIdentifier(escapedText);
        if (hasExtendedUnicodeEscape == true)
        {
            node.Flags |= NodeFlags.IdentifierHasExtendedUnicodeEscape;
        }

        // NOTE: we do not include transform flags of typeArguments in an identifier as they do not contribute to transformations
        if (string.Equals(escapedText, "await", StringComparison.Ordinal))
        {
            node.TransformFlags |= TransformFlags.ContainsPossibleTopLevelAwait;
        }
        if ((node.Flags & NodeFlags.IdentifierHasExtendedUnicodeEscape) != 0)
        {
            node.TransformFlags |= TransformFlags.ContainsES2015;
        }

        return node;
    }

    // createTempVariable, createLoopVariable, createUniqueName, getGeneratedNameForNode and the generated private
    // names (createBaseGeneratedIdentifier, createBaseGeneratedPrivateIdentifier, ...) are for the transformers: not ported.

    private PrivateIdentifier CreateBasePrivateIdentifier(string escapedText)
    {
        var node = (PrivateIdentifier)baseFactory.CreateBasePrivateIdentifierNode(SyntaxKind.PrivateIdentifier);
        node.EscapedText = escapedText;
        node.TransformFlags |= TransformFlags.ContainsClassFields;
        return node;
    }

    // @api
    internal PrivateIdentifier CreatePrivateIdentifier(string text)
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

    private T CreateBaseToken<T>(SyntaxKind kind)
        where T : Node
    {
        return (T)baseFactory.CreateBaseTokenNode(kind);
    }

    // @api
    internal Node CreateToken(SyntaxKind token)
    {
        Debug.Assert(token >= SyntaxKind.FirstToken && token <= SyntaxKind.LastToken, "Invalid token");
        Debug.Assert(token <= SyntaxKind.FirstTemplateToken || token >= SyntaxKind.LastTemplateToken, "Invalid token. Use 'createTemplateLiteralLikeNode' to create template literals.");
        Debug.Assert(token <= SyntaxKind.FirstLiteralToken || token >= SyntaxKind.LastLiteralToken, "Invalid token. Use 'createLiteralLikeNode' to create literals.");
        Debug.Assert(token != SyntaxKind.Identifier, "Invalid token. Use 'createIdentifier' to create identifiers");
        var node = CreateBaseToken<Node>(token);
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
                // flowNode is initialized by the binder (FlowContainer): it defaults to null.
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
                // flowNode is initialized by the binder (FlowContainer): it defaults to null.
                break;
        }
        if (transformFlags != TransformFlags.None)
        {
            node.TransformFlags |= transformFlags;
        }
        return node;
    }

    /// <summary>`createToken` for a token whose node class is known (the TypeScript overloads return the narrower token types).</summary>
    internal T CreateToken<T>(SyntaxKind token)
        where T : Node
    {
        return (T)CreateToken(token);
    }

    //
    // Reserved words
    //

    // @api
    internal SuperExpression CreateSuper()
    {
        return CreateToken<SuperExpression>(SyntaxKind.SuperKeyword);
    }

    // @api
    internal ThisExpression CreateThis()
    {
        return CreateToken<ThisExpression>(SyntaxKind.ThisKeyword);
    }

    // @api
    internal NullLiteral CreateNull()
    {
        return CreateToken<NullLiteral>(SyntaxKind.NullKeyword);
    }

    // @api
    internal TrueLiteral CreateTrue()
    {
        return CreateToken<TrueLiteral>(SyntaxKind.TrueKeyword);
    }

    // @api
    internal FalseLiteral CreateFalse()
    {
        return CreateToken<FalseLiteral>(SyntaxKind.FalseKeyword);
    }

    //
    // Modifiers
    //

    // @api
    internal Node CreateModifier(SyntaxKind kind)
    {
        return CreateToken(kind);
    }
}
