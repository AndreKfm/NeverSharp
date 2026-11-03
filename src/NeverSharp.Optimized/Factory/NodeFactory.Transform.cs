// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts (generated identifiers, cloneNode, synthesized nodes, the compound
// nodes and utilities the transformers use) and src/compiler/factory/utilities.ts (startOnNewLine,
// findUseStrictPrologue, formatGeneratedName) and src/compiler/utilities.ts (isCustomPrologue, isHoistedFunction,
// isHoistedVariableStatement)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go and internal/printer/factory.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D1. The transformers' factory writes into the emit copy of a
// file's store (NodeStore.CreateEmitCopy), whose new nodes are synthesized (TypeScript's syntheticFactory).

using System.Runtime.InteropServices;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>nodeFactory.ts: the parts of the factory the transformers use beyond the parser's.</summary>
internal sealed partial class NodeFactory
{
    // nodeFactory.ts `let nextAutoGenerateId = 0;` (module level, shared by every factory: ids only need to differ).
    private static int nextAutoGenerateId;

    /// <summary>
    /// The transformers' factory (TypeScript's `factory`, createNodeFactory(NodeFactoryFlags.None, syntheticFactory)) for
    /// the emit copy of a file's store.
    /// </summary>
    internal static NodeFactory CreateTransformFactory(NodeStore store)
    {
        var factory = CreateNodeFactory(NodeFactoryFlags.None, new SyntheticBaseNodeFactory(store));
        factory.store = store;
        // The memoized parenthesizer rules and node converters (TypeScript creates them on first use).
        factory.parenthesizerRulesValue = new FactoryParenthesizerRules.NullParenthesizerRulesObject(FactoryParenthesizerRules.CreateParenthesizerRules(factory));
        factory.convertersValue = FactoryNodeConverters.CreateNodeConverters(factory);
        return factory;
    }

    /// <summary>baseNodeFactory.ts createBaseNodeFactory as `syntheticFactory` uses it: the store's new nodes are synthesized.</summary>
    private sealed class SyntheticBaseNodeFactory(NodeStore store) : BaseNodeFactory
    {
        public Node CreateBaseSourceFileNode(SyntaxKind kind) => store.CreateNode(kind);

        public Node CreateBaseIdentifierNode(SyntaxKind kind) => store.CreateNode(kind);

        public Node CreateBasePrivateIdentifierNode(SyntaxKind kind) => store.CreateNode(kind);

        public Node CreateBaseTokenNode(SyntaxKind kind) => store.CreateNode(kind);

        public Node CreateBaseNode(SyntaxKind kind) => store.CreateNode(kind);
    }

    /// <summary>`createNodeArray(elements)` for a list the caller built.</summary>
    internal NodeList CreateNodeArray(List<Node> elements, bool? hasTrailingComma = null)
    {
        return CreateNodeArray(store, CollectionsMarshal.AsSpan(elements), hasTrailingComma);
    }

    /// <summary>`createNodeArray(elements)` for the nodes of a span.</summary>
    internal NodeList CreateNodeArray(ReadOnlySpan<Node> elements, bool? hasTrailingComma = null)
    {
        return CreateNodeArray(store, elements, hasTrailingComma);
    }

    /// <summary>utilities.ts getModifiers(node) as a list: the list itself when every element is a modifier (filter returns the array), else the modifiers.</summary>
    internal static NodeList GetModifierList(NodeStore store, Node node)
    {
        var modifiers = store.Modifiers(node);
        if (modifiers.IsNone)
        {
            return modifiers;
        }
        var nodes = store.Nodes(modifiers);
        var all = true;
        foreach (var modifier in nodes)
        {
            if (!UtilitiesPublic.IsModifier(store, modifier))
            {
                all = false;
                break;
            }
        }
        if (all)
        {
            return modifiers;
        }
        var result = new List<Node>();
        foreach (var modifier in store.Nodes(modifiers))
        {
            if (UtilitiesPublic.IsModifier(store, modifier))
            {
                result.Add(modifier);
            }
        }
        return CreateNodeArray(store, CollectionsMarshal.AsSpan(result));
    }

    //// Generated identifiers

    private Node CreateBaseGeneratedIdentifier(string text, GeneratedIdentifierFlags autoGenerateFlags, string? prefix, string? suffix, GeneratedNamePart? prefixPart = null)
    {
        var node = CreateIdentifierForGeneratedName(text);
        FactoryEmitNode.SetIdentifierAutoGenerate(store, node, new AutoGenerateInfo
        {
            Flags = autoGenerateFlags,
            Id = Interlocked.Increment(ref nextAutoGenerateId),
            Prefix = prefix ?? "",
            PrefixPart = prefixPart,
            Suffix = suffix ?? "",
        });
        return node;
    }

    // createBaseGeneratedPrivateIdentifier (module D4)
    private Node CreateBaseGeneratedPrivateIdentifier(string text, GeneratedIdentifierFlags autoGenerateFlags, string? prefix, string? suffix)
    {
        var node = CreateBasePrivateIdentifier(store.Texts.Add(UtilitiesPublic.EscapeLeadingUnderscores(text)));
        FactoryEmitNode.SetIdentifierAutoGenerate(store, node, new AutoGenerateInfo
        {
            Flags = autoGenerateFlags,
            Id = Interlocked.Increment(ref nextAutoGenerateId),
            Prefix = prefix ?? "",
            Suffix = suffix ?? "",
        });
        return node;
    }

    /// <summary>Create a unique private name based on the supplied text (module D4).</summary>
    // @api
    internal Node CreateUniquePrivateName(string? text = null, string? prefix = null, string? suffix = null)
    {
        if (!string.IsNullOrEmpty(text) && text[0] != '#')
        {
            Debug.Fail("First character of private identifier must be #: " + text);
        }
        var autoGenerateFlags = GeneratedIdentifierFlags.ReservedInNestedScopes |
            (!string.IsNullOrEmpty(text) ? GeneratedIdentifierFlags.Unique : GeneratedIdentifierFlags.Auto);
        return CreateBaseGeneratedPrivateIdentifier(text ?? "", autoGenerateFlags, prefix, suffix);
    }

    /// <summary>Create a unique private name generated for a node (module D4).</summary>
    // @api
    internal Node GetGeneratedPrivateNameForNode(Node node, string? prefix = null, string? suffix = null)
    {
        var text = IsMemberName(store, node) ? FormatGeneratedName(privateName: true, prefix, PrinterUtilities.IdText(store, node), suffix) :
            "#generated@" + node.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var flags = !string.IsNullOrEmpty(prefix) || !string.IsNullOrEmpty(suffix) ? GeneratedIdentifierFlags.Optimistic : GeneratedIdentifierFlags.None;
        var name = CreateBaseGeneratedPrivateIdentifier(text, GeneratedIdentifierFlags.Node | flags, prefix, suffix);
        store.SetOriginal(name, node);
        return name;
    }

    /// <summary>`createUniqueName(text, flags, prefix)` with a GeneratedNamePart prefix (module D4).</summary>
    internal Node CreateUniqueName(string text, GeneratedIdentifierFlags flags, GeneratedNamePart prefix, string? suffix)
    {
        Debug.Assert((flags & GeneratedIdentifierFlags.KindMask) == 0, "Argument out of range: flags");
        return CreateBaseGeneratedIdentifier(text, GeneratedIdentifierFlags.Unique | flags, prefix: null, suffix, prefix);
    }

    /// <summary>`getGeneratedNameForNode(node, flags, prefix, suffix)` with a GeneratedNamePart prefix (module D4).</summary>
    internal Node GetGeneratedNameForNode(Node node, GeneratedIdentifierFlags flags, GeneratedNamePart prefix, string? suffix)
    {
        Debug.Assert((flags & GeneratedIdentifierFlags.KindMask) == 0, "Argument out of range: flags");
        var text = node.IsNone ? "" :
            IsMemberName(store, node) ? FormatGeneratedName(privateName: false, prefix.Prefix + PrinterUtilities.IdText(store, prefix.Node).TrimStart('#') + prefix.Suffix, PrinterUtilities.IdText(store, node), suffix) :
            "generated@" + node.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        flags |= GeneratedIdentifierFlags.Optimistic;
        var name = CreateBaseGeneratedIdentifier(text, GeneratedIdentifierFlags.Node | flags, prefix: null, suffix, prefix);
        store.SetOriginal(name, node);
        return name;
    }

    /// <summary>`createTempVariable(undefined, reservedInNestedScopes, prefix, suffix)` with a GeneratedNamePart prefix (module D4).</summary>
    internal Node CreateTempVariable(bool reservedInNestedScopes, GeneratedNamePart prefix, string? suffix)
    {
        var flags = GeneratedIdentifierFlags.Auto;
        if (reservedInNestedScopes)
        {
            flags |= GeneratedIdentifierFlags.ReservedInNestedScopes;
        }
        return CreateBaseGeneratedIdentifier("", flags, prefix: null, suffix, prefix);
    }

    // createBaseIdentifier(escapeLeadingUnderscores(text)): no transform flags.
    private Node CreateIdentifierForGeneratedName(string text)
    {
        return store.NewIdentifier(TransformFlags.None, escapedText: store.Texts.Add(UtilitiesPublic.EscapeLeadingUnderscores(text)));
    }

    // @api
    internal Node CreateTempVariable(Action<Node>? recordTempVariable, bool reservedInNestedScopes = false, string? prefix = null, string? suffix = null)
    {
        var flags = GeneratedIdentifierFlags.Auto;
        if (reservedInNestedScopes)
        {
            flags |= GeneratedIdentifierFlags.ReservedInNestedScopes;
        }
        var name = CreateBaseGeneratedIdentifier("", flags, prefix, suffix);
        recordTempVariable?.Invoke(name);
        return name;
    }

    /// <summary>Create a unique temporary variable for use in a loop.</summary>
    // @api
    internal Node CreateLoopVariable(bool reservedInNestedScopes = false)
    {
        var flags = GeneratedIdentifierFlags.Loop;
        if (reservedInNestedScopes)
        {
            flags |= GeneratedIdentifierFlags.ReservedInNestedScopes;
        }
        return CreateBaseGeneratedIdentifier("", flags, prefix: null, suffix: null);
    }

    /// <summary>Create a unique name based on the supplied text.</summary>
    // @api
    internal Node CreateUniqueName(string text, GeneratedIdentifierFlags flags = GeneratedIdentifierFlags.None, string? prefix = null, string? suffix = null)
    {
        Debug.Assert((flags & GeneratedIdentifierFlags.KindMask) == 0, "Argument out of range: flags");
        Debug.Assert((flags & (GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel)) != GeneratedIdentifierFlags.FileLevel, "GeneratedIdentifierFlags.FileLevel cannot be set without also setting GeneratedIdentifierFlags.Optimistic");
        return CreateBaseGeneratedIdentifier(text, GeneratedIdentifierFlags.Unique | flags, prefix, suffix);
    }

    /// <summary>Create a unique name generated for a node.</summary>
    // @api
    internal Node GetGeneratedNameForNode(Node node, GeneratedIdentifierFlags flags = GeneratedIdentifierFlags.None, string? prefix = null, string? suffix = null)
    {
        Debug.Assert((flags & GeneratedIdentifierFlags.KindMask) == 0, "Argument out of range: flags");
        // (TypeScript: `generated@${getNodeId(node)}` for other nodes; the text of a node-generated name is not read.)
        var text = node.IsNone ? "" :
            IsMemberName(store, node) ? FormatGeneratedName(privateName: false, prefix, PrinterUtilities.IdText(store, node), suffix) :
            "generated@" + node.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(prefix) || !string.IsNullOrEmpty(suffix))
        {
            flags |= GeneratedIdentifierFlags.Optimistic;
        }
        var name = CreateBaseGeneratedIdentifier(text, GeneratedIdentifierFlags.Node | flags, prefix, suffix);
        store.SetOriginal(name, node);
        return name;
    }

    /// <summary>utilitiesPublic.ts isPropertyAccessChain.</summary>
    internal static bool IsPropertyAccessChain(NodeStore store, Node node) =>
        store.Kind(node) == SyntaxKind.PropertyAccessExpression && (store.Flags(node) & NodeFlags.OptionalChain) != 0;

    /// <summary>utilitiesPublic.ts isElementAccessChain.</summary>
    internal static bool IsElementAccessChain(NodeStore store, Node node) =>
        store.Kind(node) == SyntaxKind.ElementAccessExpression && (store.Flags(node) & NodeFlags.OptionalChain) != 0;

    /// <summary>utilities.ts isMemberName: an identifier or a private identifier.</summary>
    private static bool IsMemberName(NodeStore store, Node node) => store.Kind(node) is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier;

    /// <summary>factory/utilities.ts formatGeneratedName for string parts (a GeneratedNamePart is not ported yet).</summary>
    internal static string FormatGeneratedName(bool privateName, string? prefix, string baseName, string? suffix)
    {
        prefix = FormatGeneratedNamePart(prefix);
        suffix = FormatGeneratedNamePart(suffix);
        if (baseName.Length > 0 && baseName[0] == '#')
        {
            // formatIdentifierWorker: a private identifier's text without the `#`
            baseName = baseName[1..];
        }
        return (privateName ? "#" : "") + prefix + baseName + suffix;
    }

    /// <summary>factory/utilities.ts formatGeneratedNamePart for a string part.</summary>
    internal static string FormatGeneratedNamePart(string? part)
    {
        return part is null ? "" : part.Length > 0 && part[0] == '#' ? part[1..] : part;
    }

    //// cloneNode

    // @api
    internal Node CloneNode(Node node)
    {
        // We don't use "clone" from core.ts here, as we need to preserve the prototype chain of
        // the original node. We also need to exclude specific properties and only include own-
        // properties (to skip members already defined on the shared prototype).
        if (node.IsNone)
        {
            return node;
        }
        if (store.Kind(node) == SyntaxKind.SourceFile)
        {
            return CloneSourceFile(node);
        }
        // (cloneGeneratedIdentifier, cloneIdentifier, cloneGeneratedPrivateIdentifier, clonePrivateIdentifier and the
        // generic clone copy the fields the node has, minus the constructor's; Track O copies the record, see
        // NodeStore.CloneRecord. The binder's flowNode and symbol of an identifier are not in the store.)
        var clone = store.CloneRecord(node);
        SetOriginal(clone, node);
        var autoGenerate = FactoryEmitNode.GetIdentifierAutoGenerate(store, node);
        if (autoGenerate is not null && store.Kind(node) is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier)
        {
            FactoryEmitNode.SetIdentifierAutoGenerate(store, clone, new AutoGenerateInfo { Flags = autoGenerate.Flags, Id = autoGenerate.Id, Prefix = autoGenerate.Prefix, PrefixPart = autoGenerate.PrefixPart, Suffix = autoGenerate.Suffix });
        }
        else if (store.Kind(node) == SyntaxKind.Identifier && store.EmitNode(node) is EmitNode { IdentifierTypeArguments.IsNone: false } emitNode)
        {
            // clone type arguments for emitter/typeWriter
            FactoryEmitNode.GetOrCreateEmitNode(store, clone).IdentifierTypeArguments = emitNode.IdentifierTypeArguments;
        }
        return clone;
    }

    //// Synthesized nodes

    /// <summary>Creates a synthetic statement to act as a placeholder for a not-emitted statement in order to preserve comments.</summary>
    // @api
    internal Node CreateNotEmittedStatement(Node original)
    {
        var node = CreateBaseNode(SyntaxKind.NotEmittedStatement);
        store.SetOriginal(node, original);
        FactoryUtilitiesPublic.SetTextRange(store, node, original);
        return node;
    }

    // @api
    internal Node CreateNotEmittedTypeElement()
    {
        return CreateBaseNode(SyntaxKind.NotEmittedTypeElement);
    }

    private void FlattenCommaElements(Node node, List<Node> result)
    {
        if (Utilities.NodeIsSynthesized(store, node) && !UtilitiesPublic.IsParseTreeNode(store, node) && store.Original(node).IsNone && store.EmitNode(node) is null)
        {
            if (store.Kind(node) == SyntaxKind.CommaListExpression)
            {
                result.AddRange(store.Nodes(store.Elements(node)));
                return;
            }
            if (store.Kind(node) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(node)) == SyntaxKind.CommaToken)
            {
                result.Add(store.Left(node));
                result.Add(store.Right(node));
                return;
            }
        }
        result.Add(node);
    }

    // @api
    internal Node CreateCommaListExpression(ReadOnlySpan<Node> elements)
    {
        var node = CreateBaseNode(SyntaxKind.CommaListExpression);
        var flattened = new List<Node>(elements.Length);
        foreach (var element in elements)
        {
            FlattenCommaElements(element, flattened);
        }
        store.SetElements(node, CreateNodeArray(flattened));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildrenFlags(store.Elements(node)));
        return node;
    }

    // @api
    internal Node CreateStringLiteralFromNode(Node sourceNode)
    {
        // getTextOfIdentifierOrLiteral(sourceNode)
        var text = IsMemberName(store, sourceNode) ? UtilitiesPublic.IdText(store, sourceNode) : store.Text(sourceNode);
        return store.NewStringLiteral(TransformFlags.None, text: text, textSourceNode: sourceNode);
    }

    //// Compound nodes

    internal Node CreateVoidZero()
    {
        return CreateVoidExpression(CreateNumericLiteral(store.Texts.Add("0")));
    }

    internal Node CreateExportDefault(Node expression)
    {
        return CreateExportAssignment(default, isExportEquals: false, expression);
    }

    internal Node CreateExternalModuleExport(Node exportName)
    {
        return CreateExportDeclaration(
            default,
            isTypeOnly: false,
            CreateNamedExports(CreateNodeArray(new[] { CreateExportSpecifier(isTypeOnly: false, propertyName: default, exportName) })));
    }

    /// <summary>`createTypeCheck(value, tag)`.</summary>
    internal Node CreateTypeCheck(Node value, string tag)
    {
        return tag == "null" ? CreateStrictEquality(value, CreateToken(SyntaxKind.NullKeyword)) :
            tag == "undefined" ? CreateStrictEquality(value, CreateVoidZero()) :
            CreateStrictEquality(CreateTypeOfExpression(value), CreateStringLiteral(store.Texts.Add(tag)));
    }

    // The binary operator shortcuts (TypeScript's createAssignment, createComma, createStrictEquality, ...).
    private Node CreateBinary(Node left, SyntaxKind operatorKind, Node right) => CreateBinaryExpression(left, CreateToken(operatorKind), right);

    internal Node CreateComma(Node left, Node right) => CreateBinary(left, SyntaxKind.CommaToken, right);

    internal Node CreateAssignment(Node left, Node right) => CreateBinary(left, SyntaxKind.EqualsToken, right);

    internal Node CreateLogicalOr(Node left, Node right) => CreateBinary(left, SyntaxKind.BarBarToken, right);

    internal Node CreateLogicalAnd(Node left, Node right) => CreateBinary(left, SyntaxKind.AmpersandAmpersandToken, right);

    internal Node CreateStrictEquality(Node left, Node right) => CreateBinary(left, SyntaxKind.EqualsEqualsEqualsToken, right);

    internal Node CreateStrictInequality(Node left, Node right) => CreateBinary(left, SyntaxKind.ExclamationEqualsEqualsToken, right);

    /// <summary>inlineExpressions: a comma list for more than 10 expressions, else nested comma expressions.</summary>
    internal Node InlineExpressions(List<Node> expressions)
    {
        // Avoid deeply nested comma expressions as traversing them during emit can result in "Maximum call
        // stack size exceeded" errors.
        if (expressions.Count > 10)
        {
            return CreateCommaListExpression(CollectionsMarshal.AsSpan(expressions));
        }
        var result = expressions[0];
        for (var i = 1; i < expressions.Count; i++)
        {
            result = CreateComma(result, expressions[i]);
        }
        return result;
    }

    //// Names of declarations

    private Node GetName(Node node, bool allowComments, bool allowSourceMaps, EmitFlags emitFlags = 0, bool ignoreAssignedName = false)
    {
        var nodeName = ignoreAssignedName
            ? (node.IsNone ? Node.None : Utilities.GetNonAssignedNameOfDeclaration(store, node))
            : (node.IsNone ? Node.None : Utilities.GetNameOfDeclaration(store, node));
        if (!nodeName.IsNone && store.Kind(nodeName) == SyntaxKind.Identifier && !UtilitiesPublic.IsGeneratedIdentifier(store, nodeName))
        {
            // TODO(rbuckton): Does this need to be parented?
            var name = FactoryUtilitiesPublic.SetTextRange(store, CloneNode(nodeName), nodeName);
            store.SetParent(name, store.Parent(nodeName));
            emitFlags |= Utilities.GetEmitFlags(store, nodeName);
            if (!allowSourceMaps)
            {
                emitFlags |= EmitFlags.NoSourceMap;
            }
            if (!allowComments)
            {
                emitFlags |= EmitFlags.NoComments;
            }
            if (emitFlags != 0)
            {
                FactoryEmitNode.SetEmitFlags(store, name, emitFlags);
            }
            return name;
        }
        return GetGeneratedNameForNode(node);
    }

    /// <summary>Gets the internal name of a declaration (never prefixed with an export modifier, never renamed).</summary>
    internal Node GetInternalName(Node node, bool allowComments = false, bool allowSourceMaps = false)
    {
        return GetName(node, allowComments, allowSourceMaps, EmitFlags.LocalName | EmitFlags.InternalName);
    }

    /// <summary>Gets the local name of a declaration (never prefixed with an export modifier).</summary>
    internal Node GetLocalName(Node node, bool allowComments = false, bool allowSourceMaps = false, bool ignoreAssignedName = false)
    {
        return GetName(node, allowComments, allowSourceMaps, EmitFlags.LocalName, ignoreAssignedName);
    }

    /// <summary>Gets the export name of a declaration (always prefixed with an export modifier if exported).</summary>
    internal Node GetExportName(Node node, bool allowComments = false, bool allowSourceMaps = false)
    {
        return GetName(node, allowComments, allowSourceMaps, EmitFlags.ExportName);
    }

    /// <summary>Gets the name of a declaration for use in declarations.</summary>
    internal Node GetDeclarationName(Node node, bool allowComments = false, bool allowSourceMaps = false)
    {
        return GetName(node, allowComments, allowSourceMaps);
    }

    /// <summary>Gets a namespace-qualified name for use in expressions.</summary>
    internal Node GetNamespaceMemberName(Node ns, Node name, bool allowComments = false, bool allowSourceMaps = false)
    {
        var qualifiedName = CreatePropertyAccessExpression(ns, Utilities.NodeIsSynthesized(store, name) ? name : CloneNode(name));
        FactoryUtilitiesPublic.SetTextRange(store, qualifiedName, name);
        EmitFlags emitFlags = 0;
        if (!allowSourceMaps)
        {
            emitFlags |= EmitFlags.NoSourceMap;
        }
        if (!allowComments)
        {
            emitFlags |= EmitFlags.NoComments;
        }
        if (emitFlags != 0)
        {
            FactoryEmitNode.SetEmitFlags(store, qualifiedName, emitFlags);
        }
        return qualifiedName;
    }

    /// <summary>Gets the exported name of a declaration for use in expressions.</summary>
    internal Node GetExternalModuleOrNamespaceExportName(Node ns, Node node, bool allowComments = false, bool allowSourceMaps = false)
    {
        if (!ns.IsNone && Utilities.HasSyntacticModifier(store, node, ModifierFlags.Export))
        {
            return GetNamespaceMemberName(ns, GetName(node, allowComments: false, allowSourceMaps: false), allowComments, allowSourceMaps);
        }
        return GetExportName(node, allowComments, allowSourceMaps);
    }

    //// Prologues and lexical environments

    internal bool IsUseStrictPrologue(Node node)
    {
        var expression = store.Expression(node);
        return store.Kind(expression) == SyntaxKind.StringLiteral && store.Texts.Equals(store.Text(expression), "use strict");
    }

    internal Node CreateUseStrictPrologue()
    {
        return FactoryEmitNode.SetStartsOnNewLine(store, CreateExpressionStatement(CreateStringLiteral(store.Texts.Add("use strict"))), newLine: true);
    }

    /// <summary>Copies any necessary standard and custom prologue-directives into the target list.</summary>
    internal int CopyPrologue(ReadOnlySpan<Node> source, List<Node> target, bool ensureUseStrict = false, Func<Node, VisitResult>? visitor = null)
    {
        var offset = CopyStandardPrologue(source, target, 0, ensureUseStrict);
        return CopyCustomPrologue(source, target, offset, visitor);
    }

    /// <summary>Copies only the standard (string-expression) prologue-directives into the target list.</summary>
    internal int CopyStandardPrologue(ReadOnlySpan<Node> source, List<Node> target, int statementOffset = 0, bool ensureUseStrict = false)
    {
        Debug.Assert(target.Count == 0, "Prologue directives should be at the first statement in the target statements array");
        var foundUseStrict = false;
        var numStatements = source.Length;
        while (statementOffset < numStatements)
        {
            var statement = source[statementOffset];
            if (Utilities.IsPrologueDirective(store, statement))
            {
                if (IsUseStrictPrologue(statement))
                {
                    foundUseStrict = true;
                }
                target.Add(statement);
            }
            else
            {
                break;
            }
            statementOffset++;
        }
        if (ensureUseStrict && !foundUseStrict)
        {
            target.Add(CreateUseStrictPrologue());
        }
        return statementOffset;
    }

    /// <summary>Copies only the custom prologue-directives into the target list.</summary>
    internal int CopyCustomPrologue(ReadOnlySpan<Node> source, List<Node> target, int statementOffset, Func<Node, VisitResult>? visitor = null, Func<Node, bool>? filter = null)
    {
        var numStatements = source.Length;
        while (statementOffset < numStatements)
        {
            var statement = source[statementOffset];
            if ((Utilities.GetEmitFlags(store, statement) & EmitFlags.CustomPrologue) != 0 && (filter is null || filter(statement)))
            {
                var visited = visitor is not null ? Visitors.VisitNode(this, statement, visitor) : statement;
                if (!visited.IsNone)
                {
                    target.Add(visited);
                }
            }
            else
            {
                break;
            }
            statementOffset++;
        }
        return statementOffset;
    }

    /// <summary>factory/utilities.ts findUseStrictPrologue.</summary>
    internal Node FindUseStrictPrologue(ReadOnlySpan<Node> statements)
    {
        foreach (var statement in statements)
        {
            if (Utilities.IsPrologueDirective(store, statement))
            {
                if (IsUseStrictPrologue(statement))
                {
                    return statement;
                }
            }
            else
            {
                break;
            }
        }
        return Node.None;
    }

    /// <summary>Ensures "use strict" directive is added.</summary>
    internal NodeList EnsureUseStrict(NodeList statements)
    {
        var foundUseStrict = FindUseStrictPrologue(store.Nodes(statements));

        if (foundUseStrict.IsNone)
        {
            var list = new List<Node>(store.Length(statements) + 1) { CreateUseStrictPrologue() };
            list.AddRange(store.Nodes(statements));
            return FactoryUtilitiesPublic.SetTextRange(store, CreateNodeArray(list), statements);
        }

        return statements;
    }

    /// <summary>Lifts a list containing only Statement nodes to a block.</summary>
    internal Node LiftToBlock(IReadOnlyList<Node> nodes)
    {
        if (nodes.Count == 1)
        {
            return nodes[0];
        }
        var statements = new List<Node>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            statements.Add(nodes[i]);
        }
        return CreateBlock(CreateNodeArray(statements));
    }

    /// <summary>utilities.ts isCustomPrologue.</summary>
    internal static bool IsCustomPrologue(NodeStore store, Node node) => (Utilities.GetEmitFlags(store, node) & EmitFlags.CustomPrologue) != 0;

    /// <summary>utilities.ts isHoistedFunction.</summary>
    internal static bool IsHoistedFunction(NodeStore store, Node node) => IsCustomPrologue(store, node) && store.Kind(node) == SyntaxKind.FunctionDeclaration;

    /// <summary>utilities.ts isHoistedVariableStatement.</summary>
    internal static bool IsHoistedVariableStatement(NodeStore store, Node node)
    {
        if (!IsCustomPrologue(store, node) || store.Kind(node) != SyntaxKind.VariableStatement)
        {
            return false;
        }
        foreach (var declaration in store.Nodes(store.Declarations(store.DeclarationList(node))))
        {
            // isHoistedVariable
            if (store.Kind(store.Name(declaration)) != SyntaxKind.Identifier || !store.Initializer(declaration).IsNone)
            {
                return false;
            }
        }
        return true;
    }

    private static int FindSpanEnd(NodeStore store, ReadOnlySpan<Node> array, Func<NodeStore, Node, bool> test, int start)
    {
        var i = start;
        while (i < array.Length && test(store, array[i]))
        {
            i++;
        }
        return i;
    }

    /// <summary>mergeLexicalEnvironment for a node list.</summary>
    internal NodeList MergeLexicalEnvironment(NodeList statements, List<Node>? declarations)
    {
        if (!Core.Some(declarations))
        {
            return statements;
        }
        var left = new List<Node>(store.Nodes(statements).Length + declarations.Count);
        left.AddRange(store.Nodes(statements));
        MergeLexicalEnvironmentWorker(left, declarations);
        return FactoryUtilitiesPublic.SetTextRange(store, CreateNodeArray(left, store.HasTrailingComma(statements)), statements);
    }

    /// <summary>mergeLexicalEnvironment for a plain array (the array is changed in place and returned).</summary>
    internal List<Node> MergeLexicalEnvironment(List<Node> statements, List<Node>? declarations)
    {
        if (!Core.Some(declarations))
        {
            return statements;
        }
        MergeLexicalEnvironmentWorker(statements, declarations);
        return statements;
    }

    private void MergeLexicalEnvironmentWorker(List<Node> left, List<Node> declarations)
    {
        // When we merge new lexical statements into an existing statement list, we merge them in the following manner:
        // standard prologues (right, then left), hoisted functions (right, left), hoisted variables (right, left),
        // lexical init statements (right, left), other statements (left).
        //
        // NOTE: It is expected that new lexical init statements must be evaluated before existing lexical init statements,
        // as the prior transformation may depend on the evaluation of the lexical init statements to be in the correct state.
        var statements = left.ToArray();
        var right = CollectionsMarshal.AsSpan(declarations);

        // find standard prologues on left in the following order: standard directives, hoisted functions, hoisted variables, other custom
        var leftStandardPrologueEnd = FindSpanEnd(store, statements, Utilities.IsPrologueDirective, 0);
        var leftHoistedFunctionsEnd = FindSpanEnd(store, statements, IsHoistedFunction, leftStandardPrologueEnd);
        var leftHoistedVariablesEnd = FindSpanEnd(store, statements, IsHoistedVariableStatement, leftHoistedFunctionsEnd);

        // find standard prologues on right in the following order: standard directives, hoisted functions, hoisted variables, other custom
        var rightStandardPrologueEnd = FindSpanEnd(store, right, Utilities.IsPrologueDirective, 0);
        var rightHoistedFunctionsEnd = FindSpanEnd(store, right, IsHoistedFunction, rightStandardPrologueEnd);
        var rightHoistedVariablesEnd = FindSpanEnd(store, right, IsHoistedVariableStatement, rightHoistedFunctionsEnd);
        var rightCustomPrologueEnd = FindSpanEnd(store, right, IsCustomPrologue, rightHoistedVariablesEnd);
        Debug.Assert(rightCustomPrologueEnd == right.Length, "Expected declarations to be valid standard or custom prologues");

        // splice prologues from the right into the left. We do this in reverse order
        // so that we don't need to recompute the index on the left when we insert items.

        // splice other custom prologues from right into left
        if (rightCustomPrologueEnd > rightHoistedVariablesEnd)
        {
            left.InsertRange(leftHoistedVariablesEnd, right[rightHoistedVariablesEnd..rightCustomPrologueEnd].ToArray());
        }

        // splice hoisted variables from right into left
        if (rightHoistedVariablesEnd > rightHoistedFunctionsEnd)
        {
            left.InsertRange(leftHoistedFunctionsEnd, right[rightHoistedFunctionsEnd..rightHoistedVariablesEnd].ToArray());
        }

        // splice hoisted functions from right into left
        if (rightHoistedFunctionsEnd > rightStandardPrologueEnd)
        {
            left.InsertRange(leftStandardPrologueEnd, right[rightStandardPrologueEnd..rightHoistedFunctionsEnd].ToArray());
        }

        // splice standard prologues from right into left (that are not already in left)
        if (rightStandardPrologueEnd > 0)
        {
            if (leftStandardPrologueEnd == 0)
            {
                left.InsertRange(0, right[..rightStandardPrologueEnd].ToArray());
            }
            else
            {
                var leftPrologues = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < leftStandardPrologueEnd; i++)
                {
                    leftPrologues.Add(store.Texts.GetString(store.Text(store.Expression(statements[i])))!);
                }
                for (var i = rightStandardPrologueEnd - 1; i >= 0; i--)
                {
                    var rightPrologue = right[i];
                    if (!leftPrologues.Contains(store.Texts.GetString(store.Text(store.Expression(rightPrologue)))!))
                    {
                        left.Insert(0, rightPrologue);
                    }
                }
            }
        }
    }

    /// <summary>replaceModifiers(node, modifiers) for a modifier list.</summary>
    internal Node ReplaceModifiers(Node node, NodeList modifierArray)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.TypeParameter:
                return UpdateTypeParameterDeclaration(node, modifierArray, store.Name(node), store.Constraint(node), store.Default(node));
            case SyntaxKind.Parameter:
                return UpdateParameterDeclaration(node, modifierArray, store.DotDotDotToken(node), store.Name(node), store.QuestionToken(node), store.Type(node), store.Initializer(node));
            case SyntaxKind.ConstructorType:
                return UpdateConstructorTypeNode(node, modifierArray, store.TypeParameters(node), store.Parameters(node), store.Type(node));
            case SyntaxKind.PropertySignature:
                return UpdatePropertySignature(node, modifierArray, store.Name(node), store.QuestionToken(node), store.Type(node));
            case SyntaxKind.PropertyDeclaration:
                return UpdatePropertyDeclaration(node, modifierArray, store.Name(node), store.QuestionToken(node).IsNone ? store.ExclamationToken(node) : store.QuestionToken(node), store.Type(node), store.Initializer(node));
            case SyntaxKind.MethodSignature:
                return UpdateMethodSignature(node, modifierArray, store.Name(node), store.QuestionToken(node), store.TypeParameters(node), store.Parameters(node), store.Type(node));
            case SyntaxKind.MethodDeclaration:
                return UpdateMethodDeclaration(node, modifierArray, store.AsteriskToken(node), store.Name(node), store.QuestionToken(node), store.TypeParameters(node), store.Parameters(node), store.Type(node), store.Body(node));
            case SyntaxKind.Constructor:
                return UpdateConstructorDeclaration(node, modifierArray, store.Parameters(node), store.Body(node));
            case SyntaxKind.GetAccessor:
                return UpdateGetAccessorDeclaration(node, modifierArray, store.Name(node), store.Parameters(node), store.Type(node), store.Body(node));
            case SyntaxKind.SetAccessor:
                return UpdateSetAccessorDeclaration(node, modifierArray, store.Name(node), store.Parameters(node), store.Body(node));
            case SyntaxKind.IndexSignature:
                return UpdateIndexSignature(node, modifierArray, store.Parameters(node), store.Type(node));
            case SyntaxKind.FunctionExpression:
                return UpdateFunctionExpression(node, modifierArray, store.AsteriskToken(node), store.Name(node), store.TypeParameters(node), store.Parameters(node), store.Type(node), store.Body(node));
            case SyntaxKind.ArrowFunction:
                return UpdateArrowFunction(node, modifierArray, store.TypeParameters(node), store.Parameters(node), store.Type(node), store.EqualsGreaterThanToken(node), store.Body(node));
            case SyntaxKind.ClassExpression:
                return UpdateClassExpression(node, modifierArray, store.Name(node), store.TypeParameters(node), store.HeritageClauses(node), store.Members(node));
            case SyntaxKind.VariableStatement:
                return UpdateVariableStatement(node, modifierArray, store.DeclarationList(node));
            case SyntaxKind.FunctionDeclaration:
                return UpdateFunctionDeclaration(node, modifierArray, store.AsteriskToken(node), store.Name(node), store.TypeParameters(node), store.Parameters(node), store.Type(node), store.Body(node));
            case SyntaxKind.ClassDeclaration:
                return UpdateClassDeclaration(node, modifierArray, store.Name(node), store.TypeParameters(node), store.HeritageClauses(node), store.Members(node));
            case SyntaxKind.InterfaceDeclaration:
                return UpdateInterfaceDeclaration(node, modifierArray, store.Name(node), store.TypeParameters(node), store.HeritageClauses(node), store.Members(node));
            case SyntaxKind.TypeAliasDeclaration:
                return UpdateTypeAliasDeclaration(node, modifierArray, store.Name(node), store.TypeParameters(node), store.Type(node));
            case SyntaxKind.EnumDeclaration:
                return UpdateEnumDeclaration(node, modifierArray, store.Name(node), store.Members(node));
            case SyntaxKind.ModuleDeclaration:
                return UpdateModuleDeclaration(node, modifierArray, store.Name(node), store.Body(node));
            case SyntaxKind.ImportEqualsDeclaration:
                return UpdateImportEqualsDeclaration(node, modifierArray, store.IsTypeOnly(node), store.Name(node), store.ModuleReference(node));
            case SyntaxKind.ImportDeclaration:
                return UpdateImportDeclaration(node, modifierArray, store.ImportClause(node), store.ModuleSpecifier(node), store.Attributes(node));
            case SyntaxKind.ExportAssignment:
                return UpdateExportAssignment(node, modifierArray, store.Expression(node));
            case SyntaxKind.ExportDeclaration:
                return UpdateExportDeclaration(node, modifierArray, store.IsTypeOnly(node), store.ExportClause(node), store.ModuleSpecifier(node), store.Attributes(node));
        }
        throw Debug.UnexpectedKind(store.Kind(node), "a node with modifiers");
    }

    //// Module D4: the utilities of nodeFactory.ts the decorator and class-fields transformers use.

    /// <summary>createModifiersFromModifierFlags (none when no flag is set).</summary>
    internal NodeList CreateModifiersFromModifierFlags(ModifierFlags flags)
    {
        var result = new List<Node>();
        if ((flags & ModifierFlags.Export) != 0) result.Add(CreateToken(SyntaxKind.ExportKeyword));
        if ((flags & ModifierFlags.Ambient) != 0) result.Add(CreateToken(SyntaxKind.DeclareKeyword));
        if ((flags & ModifierFlags.Default) != 0) result.Add(CreateToken(SyntaxKind.DefaultKeyword));
        if ((flags & ModifierFlags.Const) != 0) result.Add(CreateToken(SyntaxKind.ConstKeyword));
        if ((flags & ModifierFlags.Public) != 0) result.Add(CreateToken(SyntaxKind.PublicKeyword));
        if ((flags & ModifierFlags.Private) != 0) result.Add(CreateToken(SyntaxKind.PrivateKeyword));
        if ((flags & ModifierFlags.Protected) != 0) result.Add(CreateToken(SyntaxKind.ProtectedKeyword));
        if ((flags & ModifierFlags.Abstract) != 0) result.Add(CreateToken(SyntaxKind.AbstractKeyword));
        if ((flags & ModifierFlags.Static) != 0) result.Add(CreateToken(SyntaxKind.StaticKeyword));
        if ((flags & ModifierFlags.Override) != 0) result.Add(CreateToken(SyntaxKind.OverrideKeyword));
        if ((flags & ModifierFlags.Readonly) != 0) result.Add(CreateToken(SyntaxKind.ReadonlyKeyword));
        if ((flags & ModifierFlags.Accessor) != 0) result.Add(CreateToken(SyntaxKind.AccessorKeyword));
        if ((flags & ModifierFlags.Async) != 0) result.Add(CreateToken(SyntaxKind.AsyncKeyword));
        if ((flags & ModifierFlags.In) != 0) result.Add(CreateToken(SyntaxKind.InKeyword));
        if ((flags & ModifierFlags.Out) != 0) result.Add(CreateToken(SyntaxKind.OutKeyword));
        return result.Count > 0 ? CreateNodeArray(result) : NodeList.None;
    }

    internal Node CreateImmediatelyInvokedArrowFunction(List<Node> statements)
    {
        return CreateCallExpression(
            CreateArrowFunction(
                default,
                default,
                CreateNodeArray(ReadOnlySpan<Node>.Empty),
                default,
                default,
                CreateBlock(CreateNodeArray(statements), multiLine: true)),
            default,
            CreateNodeArray(ReadOnlySpan<Node>.Empty));
    }

    internal Node CreateMethodCall(Node @object, string methodName, ReadOnlySpan<Node> argumentsList)
    {
        // Preserve the optionality of `object`.
        if (UtilitiesPublic.IsCallChain(store, @object))
        {
            return CreateCallChain(
                CreatePropertyAccessChain(@object, default, CreateIdentifier(methodName)),
                default,
                default,
                CreateNodeArray(argumentsList));
        }
        return CreateCallExpression(
            CreatePropertyAccessExpression(@object, CreateIdentifier(methodName)),
            default,
            CreateNodeArray(argumentsList));
    }

    private static Node[] Prepend(Node first, ReadOnlySpan<Node> rest)
    {
        var all = new Node[rest.Length + 1];
        all[0] = first;
        rest.CopyTo(all.AsSpan(1));
        return all;
    }

    internal Node CreateFunctionBindCall(Node target, Node thisArg, ReadOnlySpan<Node> argumentsList) =>
        CreateMethodCall(target, "bind", Prepend(thisArg, argumentsList));

    internal Node CreateFunctionCallCall(Node target, Node thisArg, ReadOnlySpan<Node> argumentsList) =>
        CreateMethodCall(target, "call", Prepend(thisArg, argumentsList));

    internal Node CreateGlobalMethodCall(string globalObjectName, string methodName, ReadOnlySpan<Node> argumentsList) =>
        CreateMethodCall(CreateIdentifier(globalObjectName), methodName, argumentsList);

    internal Node CreateObjectDefinePropertyCall(Node target, Node propertyName, Node attributes) =>
        CreateGlobalMethodCall("Object", "defineProperty", [target, propertyName, attributes]);

    internal Node CreateReflectGetCall(Node target, Node propertyKey, Node receiver = default) =>
        CreateGlobalMethodCall("Reflect", "get", !receiver.IsNone ? [target, propertyKey, receiver] : [target, propertyKey]);

    internal Node CreateReflectSetCall(Node target, Node propertyKey, Node value, Node receiver = default) =>
        CreateGlobalMethodCall("Reflect", "set", !receiver.IsNone ? [target, propertyKey, value, receiver] : [target, propertyKey, value]);

    /// <summary>types.ts PropertyDescriptorAttributes (booleans as nullable; `value`, `get`, `set` as nodes).</summary>
    internal struct PropertyDescriptorAttributes
    {
        public bool? Enumerable;
        public bool? Configurable;
        public bool? Writable;
        public Node Value;
        public Node Get;
        public Node Set;
    }

    private bool TryAddPropertyAssignment(List<Node> properties, string propertyName, Node expression)
    {
        if (!expression.IsNone)
        {
            properties.Add(CreatePropertyAssignment(CreateIdentifier(propertyName), expression));
            return true;
        }
        return false;
    }

    // asExpression(boolean)
    private Node AsExpression(bool? value) => value is null ? Node.None : CreateToken(value.Value ? SyntaxKind.TrueKeyword : SyntaxKind.FalseKeyword);

    internal Node CreatePropertyDescriptor(PropertyDescriptorAttributes attributes, bool singleLine = false)
    {
        var properties = new List<Node>();
        TryAddPropertyAssignment(properties, "enumerable", AsExpression(attributes.Enumerable));
        TryAddPropertyAssignment(properties, "configurable", AsExpression(attributes.Configurable));

        var isData = TryAddPropertyAssignment(properties, "writable", AsExpression(attributes.Writable));
        isData = TryAddPropertyAssignment(properties, "value", attributes.Value) || isData;

        var isAccessor = TryAddPropertyAssignment(properties, "get", attributes.Get);
        isAccessor = TryAddPropertyAssignment(properties, "set", attributes.Set) || isAccessor;

        Debug.Assert(!(isData && isAccessor), "A PropertyDescriptor may not be both an accessor descriptor and a data descriptor.");
        return CreateObjectLiteralExpression(CreateNodeArray(properties), !singleLine);
    }

    private static bool ShouldBeCapturedInTempVariable(NodeStore store, Node node, bool cacheIdentifiers)
    {
        var target = Utilities.SkipParentheses(store, node);
        switch (store.Kind(target))
        {
            case SyntaxKind.Identifier:
                return cacheIdentifiers;
            case SyntaxKind.ThisKeyword:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.StringLiteral:
                return false;
            case SyntaxKind.ArrayLiteralExpression:
                return store.Length(store.Elements(target)) != 0;
            case SyntaxKind.ObjectLiteralExpression:
                return store.Length(store.Properties(target)) > 0;
            default:
                return true;
        }
    }

    /// <summary>createCallBinding: the target of a call and its `this` argument.</summary>
    internal (Node Target, Node ThisArg) CreateCallBinding(Node expression, Action<Node>? recordTempVariable, ScriptTarget? languageVersion = null, bool cacheIdentifiers = false)
    {
        var callee = FactoryUtilities.SkipOuterExpressions(store, expression, OuterExpressionKinds.All);
        Node thisArg;
        Node target;
        if (Utilities.IsSuperProperty(store, callee))
        {
            thisArg = CreateToken(SyntaxKind.ThisKeyword);
            target = callee;
        }
        else if (store.Kind(callee) == SyntaxKind.SuperKeyword)
        {
            thisArg = CreateToken(SyntaxKind.ThisKeyword);
            target = languageVersion is not null && languageVersion < ScriptTarget.ES2015
                ? FactoryUtilitiesPublic.SetTextRange(store, CreateIdentifier("_super"), callee)
                : callee;
        }
        else if ((Utilities.GetEmitFlags(store, callee) & EmitFlags.HelperName) != 0)
        {
            thisArg = CreateVoidZero();
            target = Parenthesizer.ParenthesizeLeftSideOfAccess(callee, optionalChain: false);
        }
        else if (store.Kind(callee) == SyntaxKind.PropertyAccessExpression)
        {
            if (ShouldBeCapturedInTempVariable(store, store.Expression(callee), cacheIdentifiers))
            {
                // for `a.b()` target is `(_a = a).b` and thisArg is `_a`
                thisArg = CreateTempVariable(recordTempVariable);
                target = CreatePropertyAccessExpression(
                    FactoryUtilitiesPublic.SetTextRange(store, CreateAssignment(thisArg, store.Expression(callee)), store.Expression(callee)),
                    store.Name(callee));
                FactoryUtilitiesPublic.SetTextRange(store, target, callee);
            }
            else
            {
                thisArg = store.Expression(callee);
                target = callee;
            }
        }
        else if (store.Kind(callee) == SyntaxKind.ElementAccessExpression)
        {
            if (ShouldBeCapturedInTempVariable(store, store.Expression(callee), cacheIdentifiers))
            {
                // for `a[b]()` target is `(_a = a)[b]` and thisArg is `_a`
                thisArg = CreateTempVariable(recordTempVariable);
                target = CreateElementAccessExpression(
                    FactoryUtilitiesPublic.SetTextRange(store, CreateAssignment(thisArg, store.Expression(callee)), store.Expression(callee)),
                    store.ArgumentExpression(callee));
                FactoryUtilitiesPublic.SetTextRange(store, target, callee);
            }
            else
            {
                thisArg = store.Expression(callee);
                target = callee;
            }
        }
        else
        {
            // for `a()` target is `a` and thisArg is `void 0`
            thisArg = CreateVoidZero();
            target = Parenthesizer.ParenthesizeLeftSideOfAccess(expression, optionalChain: false);
        }

        return (target, thisArg);
    }

    internal Node CreateAssignmentTargetWrapper(Node paramName, Node expression)
    {
        return CreatePropertyAccessExpression(
            // Explicit parens required because of v8 regression (https://bugs.chromium.org/p/v8/issues/detail?id=9560)
            CreateParenthesizedExpression(
                CreateObjectLiteralExpression(CreateNodeArray(new[]
                {
                    CreateSetAccessorDeclaration(
                        default,
                        CreateIdentifier("value"),
                        CreateNodeArray(new[] { CreateParameterDeclaration(default, default, paramName) }),
                        CreateBlock(CreateNodeArray(new[] { CreateExpressionStatement(expression) }))),
                }))),
            CreateIdentifier("value"));
    }
}
