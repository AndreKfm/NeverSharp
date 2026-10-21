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

/// <summary>
/// nodeFactory.ts: createNodeFactory. An instance is the `NodeFactory` object createNodeFactory returns; the
/// variables of its closure are the instance's fields and its inner functions the instance methods. The
/// file-level functions of nodeFactory.ts are static members.
/// </summary>
internal sealed partial class NodeFactory
{
    // nodeFactory.ts: `const nodeFactoryPatchers: ((factory: NodeFactory) => void)[] = [];` (addNodeFactoryPatcher
    // is not in the parser's slice; the list stays empty, so applying it is a no-op).
    private static readonly List<Action<NodeFactory>> NodeFactoryPatchers = [];

    private readonly Func<Node, Node?, Node> setOriginal;
    private ParenthesizerRules? parenthesizerRules;
    private NodeConverters? converters;

    private NodeFactory(NodeFactoryFlags flags, BaseNodeFactory baseFactory)
    {
        Flags = flags;
        BaseFactory = baseFactory;
        setOriginal = (flags & NodeFactoryFlags.NoOriginalNode) != 0 ? static (node, _) => node : static (node, original) => SetOriginalNode(node, original);
    }

    internal NodeFactoryFlags Flags { get; }

    internal BaseNodeFactory BaseFactory { get; }

    /// <summary>`get parenthesizer()`: the memoized parenthesizer rules (`parenthesizerRules()` inside the factory).</summary>
    internal ParenthesizerRules Parenthesizer
    {
        get
        {
            // memoize(() => flags & NodeFactoryFlags.NoParenthesizerRules ? nullParenthesizerRules : createParenthesizerRules(factory))
            if (parenthesizerRules == null)
            {
                parenthesizerRules = (Flags & NodeFactoryFlags.NoParenthesizerRules) != 0
                    ? ParenthesizerRulesNull.NullParenthesizerRules
                    : throw new NotImplementedException("createParenthesizerRules is not in the parser's slice");
            }
            return parenthesizerRules;
        }
    }

    /// <summary>`get converters()`: the memoized node converters.</summary>
    internal NodeConverters Converters
    {
        get
        {
            // memoize(() => flags & NodeFactoryFlags.NoNodeConverters ? nullNodeConverters : createNodeConverters(factory))
            if (converters == null)
            {
                converters = (Flags & NodeFactoryFlags.NoNodeConverters) != 0
                    ? NodeConvertersNull.NullNodeConverters
                    : throw new NotImplementedException("createNodeConverters is not in the parser's slice");
            }
            return converters;
        }
    }

    /// <summary>
    /// Creates a `NodeFactory` that can be used to create and update a syntax tree.
    /// </summary>
    /// <param name="flags">Flags that control factory behavior.</param>
    /// <param name="baseFactory">A `BaseNodeFactory` used to create the base `Node` objects.</param>
    internal static NodeFactory CreateNodeFactory(NodeFactoryFlags flags, BaseNodeFactory baseFactory)
    {
        var factory = new NodeFactory(flags, baseFactory);
        foreach (var fn in NodeFactoryPatchers)
        {
            fn(factory);
        }
        return factory;
    }

    // @api
    internal NodeArray CreateNodeArray(IReadOnlyList<Node>? elements, bool? hasTrailingComma = null)
    {
        if (elements == null)
        {
            // `elements === undefined || elements === emptyArray`: a fresh empty array (emptyArray is not a NodeArray
            // and takes the path below with the same result).
            elements = [];
        }
        else if (elements is NodeArray nodeArray)
        {
            if (hasTrailingComma == null || nodeArray.HasTrailingComma == hasTrailingComma)
            {
                // Ensure the transform flags have been aggregated for this NodeArray
                // (TypeScript: `if (elements.transformFlags === undefined)`; every NodeArray is created here, which
                // aggregates them, so the flags are always defined.)
                return nodeArray;
            }

            // This *was* a `NodeArray`, but the `hasTrailingComma` option differs. Recreate the
            // array with the same elements, text range, and transform flags but with the updated
            // value for `hasTrailingComma`
            var copy = new NodeArray();
            copy.AddRange(nodeArray);
            copy.Pos = nodeArray.Pos;
            copy.End = nodeArray.End;
            copy.HasTrailingComma = hasTrailingComma.Value;
            copy.TransformFlags = nodeArray.TransformFlags;
            return copy;
        }

        // Since the element list of a node array is typically created by starting with an empty array and
        // repeatedly calling push(), the list may not have the optimal memory layout. We invoke slice() for
        // small arrays (1 to 4 elements) to give the VM a chance to allocate an optimal representation.
        // (C#: a NodeArray is its own List type, so the elements are always copied into a new NodeArray.)
        var array = new NodeArray();
        array.AddRange(elements);
        array.Pos = -1;
        array.End = -1;
        array.HasTrailingComma = hasTrailingComma == true;
        array.TransformFlags = TransformFlags.None;
        AggregateChildrenFlags(array);
        return array;
    }

    private Node CreateBaseNode(SyntaxKind kind)
    {
        return BaseFactory.CreateBaseNode(kind);
    }

    private Node CreateBaseDeclaration(SyntaxKind kind)
    {
        var node = CreateBaseNode(kind);
        // node.symbol = undefined!; // initialized by binder
        // node.localSymbol = undefined; // initialized by binder
        return node;
    }

    //
    // Literals
    //

    // @api
    internal NumericLiteral CreateNumericLiteral(string value, TokenFlags numericLiteralFlags = TokenFlags.None)
    {
        var text = value;
        Debug.Assert(text.Length == 0 || text[0] != (char)CharacterCodes.minus, "Negative numbers should be created in combination with createPrefixUnaryExpression");
        var node = (NumericLiteral)CreateBaseDeclaration(SyntaxKind.NumericLiteral);
        node.Text = text;
        node.NumericLiteralFlags = numericLiteralFlags;
        if ((numericLiteralFlags & TokenFlags.BinaryOrOctalSpecifier) != 0)
        {
            node.TransformFlags |= TransformFlags.ContainsES2015;
        }
        return node;
    }

    // @api
    internal NumericLiteral CreateNumericLiteral(double value, TokenFlags numericLiteralFlags = TokenFlags.None)
    {
        // `typeof value === "number" ? value + "" : value`
        return CreateNumericLiteral(JsNumber.ToString(value), numericLiteralFlags);
    }

    // @api
    internal BigIntLiteral CreateBigIntLiteral(string value)
    {
        // TypeScript: `value: string | PseudoBigInt`; the parser passes strings.
        var node = (BigIntLiteral)CreateBaseToken(SyntaxKind.BigIntLiteral);
        node.Text = value;
        node.TransformFlags |= TransformFlags.ContainsES2020;
        return node;
    }

    private StringLiteral CreateBaseStringLiteral(string text, bool? isSingleQuote)
    {
        var node = (StringLiteral)CreateBaseDeclaration(SyntaxKind.StringLiteral);
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
    internal RegularExpressionLiteral CreateRegularExpressionLiteral(string text)
    {
        var node = (RegularExpressionLiteral)CreateBaseToken(SyntaxKind.RegularExpressionLiteral);
        node.Text = text;
        return node;
    }

    // @api
    internal Node CreateLiteralLikeNode(SyntaxKind kind, string text)
    {
        switch (kind)
        {
            case SyntaxKind.NumericLiteral:
                return CreateNumericLiteral(text, /*numericLiteralFlags*/ 0);
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
                return CreateTemplateLiteralLikeNode(kind, text, /*rawText*/ null, /*templateFlags*/ 0);
        }
        // TypeScript's switch covers every member of its parameter type and falls off the end otherwise.
        return null!;
    }

    //
    // Identifiers
    //

    private Identifier CreateBaseIdentifier(string escapedText)
    {
        var node = (Identifier)BaseFactory.CreateBaseIdentifierNode(SyntaxKind.Identifier);
        node.EscapedText = escapedText;
        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        node.Symbol = null; // initialized by checker
        return node;
    }

    // @api
    internal Identifier CreateIdentifier(string text, SyntaxKind? originalKeywordKind = null, bool? hasExtendedUnicodeEscape = null)
    {
        if (originalKeywordKind == null && !string.IsNullOrEmpty(text))
        {
            originalKeywordKind = Scanner.StringToToken(text);
        }
        if (originalKeywordKind == SyntaxKind.Identifier)
        {
            // (TypeScript computes originalKeywordKind and no longer stores it on the node.)
            originalKeywordKind = null;
        }

        var node = CreateBaseIdentifier(UtilitiesPublic.EscapeLeadingUnderscores(text));
        if (hasExtendedUnicodeEscape == true)
        {
            node.Flags |= NodeFlags.IdentifierHasExtendedUnicodeEscape;
        }

        // NOTE: we do not include transform flags of typeArguments in an identifier as they do not contribute to transformations
        if (node.EscapedText is "await")
        {
            node.TransformFlags |= TransformFlags.ContainsPossibleTopLevelAwait;
        }
        if ((node.Flags & NodeFlags.IdentifierHasExtendedUnicodeEscape) != 0)
        {
            node.TransformFlags |= TransformFlags.ContainsES2015;
        }

        return node;
    }

    private PrivateIdentifier CreateBasePrivateIdentifier(string escapedText)
    {
        var node = (PrivateIdentifier)BaseFactory.CreateBasePrivateIdentifierNode(SyntaxKind.PrivateIdentifier);
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

    private Node CreateBaseToken(SyntaxKind kind)
    {
        return BaseFactory.CreateBaseTokenNode(kind);
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
                ((SuperExpression)node).FlowNode = null; // initialized by binder (FlowContainer)
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
                ((ThisExpression)node).FlowNode = null; // initialized by binder (FlowContainer)
                break;
        }
        if (transformFlags != 0)
        {
            node.TransformFlags |= transformFlags;
        }
        return node;
    }

    //
    // Reserved words
    //

    // @api
    internal SuperExpression CreateSuper()
    {
        return (SuperExpression)CreateToken(SyntaxKind.SuperKeyword);
    }

    // @api
    internal ThisExpression CreateThis()
    {
        return (ThisExpression)CreateToken(SyntaxKind.ThisKeyword);
    }

    // @api
    internal NullLiteral CreateNull()
    {
        return (NullLiteral)CreateToken(SyntaxKind.NullKeyword);
    }

    // @api
    internal TrueLiteral CreateTrue()
    {
        return (TrueLiteral)CreateToken(SyntaxKind.TrueKeyword);
    }

    // @api
    internal FalseLiteral CreateFalse()
    {
        return (FalseLiteral)CreateToken(SyntaxKind.FalseKeyword);
    }

    //
    // Modifiers
    //

    // @api
    internal Node CreateModifier(SyntaxKind kind)
    {
        return CreateToken(kind);
    }

    //
    // Helpers (end of createNodeFactory)
    //

    // (TypeScript overloads asNodeArray so that a defined array gives a defined NodeArray.)
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(array))]
    private NodeArray? AsNodeArray(IReadOnlyList<Node>? array)
    {
        return array != null ? CreateNodeArray(array) : null;
    }

    /// <summary>
    /// asName: `typeof name === "string" ? createIdentifier(name) : name`. The parser passes nodes; the string case is
    /// the overload below.
    /// </summary>
    private static T AsName<T>(T name)
        where T : Node?
    {
        return name;
    }

    private Identifier AsName(string name)
    {
        return CreateIdentifier(name);
    }

    /// <summary>asExpression for a node (`string | number | boolean | T`; the parser passes nodes).</summary>
    private static T AsExpression<T>(T value)
        where T : Node?
    {
        return value;
    }

    private Node? AsInitializer(Node? node)
    {
        return node != null ? Parenthesizer.ParenthesizeExpressionForDisallowedComma(node) : null;
    }

    /// <summary>asToken: `typeof value === "number" ? createToken(value) : value`.</summary>
    private static Node AsToken(Node value)
    {
        return value;
    }

    private Node AsToken(SyntaxKind value)
    {
        return CreateToken(value);
    }

    private T Update<T>(T updated, T original)
        where T : Node
    {
        if (updated != original)
        {
            setOriginal(updated, original);
            FactoryUtilitiesPublic.SetTextRange(updated, original);
        }
        return updated;
    }

    //
    // File-level functions of nodeFactory.ts
    //

    private static TransformFlags PropagateNameFlags(Node? node)
    {
        return node != null && NodeTests.IsIdentifier(node) ? PropagateIdentifierNameFlags(node) : PropagateChildFlags(node);
    }

    private static TransformFlags PropagateIdentifierNameFlags(Node node)
    {
        // An IdentifierName is allowed to be `await`
        return PropagateChildFlags(node) & ~TransformFlags.ContainsPossibleTopLevelAwait;
    }

    private static TransformFlags PropagatePropertyNameFlagsOfChild(Node node, TransformFlags transformFlags)
    {
        return transformFlags | (node.TransformFlags & TransformFlags.PropertyNamePropagatingFlags);
    }

    private static TransformFlags PropagateChildFlags(Node? child)
    {
        if (child == null)
        {
            return TransformFlags.None;
        }
        var childFlags = child.TransformFlags & ~GetTransformFlagsSubtreeExclusions(child.Kind);
        return UtilitiesPublic.IsNamedDeclaration(child) && UtilitiesPublic.IsPropertyName(NodeAccessors.GetName(child)!) ? PropagatePropertyNameFlagsOfChild(NodeAccessors.GetName(child)!, childFlags) : childFlags;
    }

    private static TransformFlags PropagateChildrenFlags(NodeArray? children)
    {
        return children != null ? children.TransformFlags : TransformFlags.None;
    }

    private static void AggregateChildrenFlags(NodeArray children)
    {
        var subtreeFlags = TransformFlags.None;
        foreach (var child in children)
        {
            subtreeFlags |= PropagateChildFlags(child);
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
