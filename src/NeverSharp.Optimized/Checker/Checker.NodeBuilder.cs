// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (createNodeBuilder: withContext, typeToTypeNodeHelper and the type node functions, typeToString, formatUnionTypes) and src/compiler/factory/parenthesizerRules.ts (type rules)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/nodebuilderimpl.go and internal/checker/printer.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C2. The nodes are built in a node store of the checker's own
// (synthesized nodes: pos and end -1, NodeFlags.Synthesized) and printed with the Track O printer; a store mark is
// taken before and the store truncated after each print. Existing nodes the builder reuses are copied into that store
// and remembered as reused (TypeScript's setTextRange/setOriginalNode: such nodes are not fully synthetic, which the
// .types baseline underlines with spaces). Left out: the per-enclosing-declaration serializedTypes cache (a cache of
// the result), symbol trackers and their error reports (typeToString passes none), the expanding/verbosity mode of
// quick info (maxExpansionDepth -1 throughout) and declaration emit's symbolTableToDeclarationStatements.

using System.Runtime.InteropServices;
using System.Text;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // checker.ts constants
    private const int DefaultMaximumTruncationLength = 160;
    private const int NoTruncationMaximumTruncationLength = 1_000_000;

    /// <summary>checker.ts NodeBuilderContext (the fields typeToString's paths read).</summary>
    private sealed class NodeBuilderContext
    {
        public NodeRef EnclosingDeclaration;
        public FakeScope? FakeScope;
        public int EnclosingFile;
        public NodeBuilderFlags Flags;
        public InternalNodeBuilderFlags InternalFlags;
        public int MaxTruncationLength;
        public int MaxExpansionDepth = -1;
        public bool EncounteredError;
        public bool SuppressReportInferenceFallback;
        public HashSet<Type>? VisitedTypes;
        public Dictionary<string, int>? SymbolDepth;
        public List<Type>? InferTypeParameters;
        public int ApproximateLength;
        public bool Truncating;
        public List<SymbolRef>? ReverseMappedStack;
        public bool MustCreateTypeParameterSymbolList = true;
        public HashSet<SymbolRef>? TypeParameterSymbolList;
        public bool MustCreateTypeParametersNamesLookups = true;
        public Dictionary<Type, Node>? TypeParameterNames;
        public HashSet<string>? TypeParameterNamesByText;
        public Dictionary<string, int>? TypeParameterNamesByTextNextNameCount;
        public readonly Dictionary<SymbolRef, Type> EnclosingSymbolTypes = [];
        public object? Mapper; // TypeMapper (C3)
        public int Depth;
        public readonly List<int> TypeStack = [];
        public bool CanIncreaseExpansionDepth;
        public bool Truncated;

        // createRecoveryBoundary: whether an error was marked inside the current boundary (null outside of any)
        public bool? BoundaryHadError;
    }

    // ---- the node store the builder writes into ----

    private NodeStore? synthStore;
    private NodeStore.Mark synthMark;
    private int synthDepth;
    private readonly HashSet<Node> reusedNodes = [];

    // NodeLinks.serializedTypes (visitAndTransformType's cache), for the current outermost print
    private readonly Dictionary<(NodeRef, Type, NodeBuilderFlags, InternalNodeBuilderFlags), (Node Node, bool Truncating, int AddedLength)> serializedTypes = [];

    private NodeStore S => synthStore!;

    // visitAndTransformType deepCloneOrReuseNode: a fresh copy of a cached node (a shallow copy sharing the children,
    // with the emit node), so that changes to the result do not reach the cached node
    private Node CloneSerializedTypeNode(Node node)
    {
        var clone = CopyWithChildrenFrom(S, node, child => child, list => list, markReused: reusedNodes.Contains(node));
        if (S.EmitNode(node) is EmitNode emitNode)
        {
            var copy = SEmitNode(clone);
            copy.Flags = emitNode.Flags;
            copy.IdentifierTypeArguments = emitNode.IdentifierTypeArguments;
        }
        return clone;
    }

    // Begins building nodes for one print (nested prints, e.g. symbolToString inside the builder, share the store).
    private void BeginSynth()
    {
        if (synthStore is null)
        {
            synthStore = new NodeStore(NativeText.FromString(""));
            synthStore.Commit();
        }
        if (synthDepth++ == 0)
        {
            synthMark = synthStore.GetMark();
            reusedNodes.Clear();
            serializedTypes.Clear();
        }
    }

    private void EndSynth()
    {
        if (--synthDepth == 0)
        {
            synthStore!.Truncate(synthMark);
            reusedNodes.Clear();
            serializedTypes.Clear();
        }
    }

    // A synthesized node (factory-created: pos and end -1, NodeFlags.Synthesized).
    private Node SNew(SyntaxKind kind)
    {
        var node = S.CreateNode(kind);
        S.Finish(node, -1, -1, NodeFlags.Synthesized);
        return node;
    }

    private NodeList SList(ReadOnlySpan<Node> nodes) => S.NewList(nodes);

    // (the unused optional parameter makes collection expressions bind to the span overload)
    private NodeList SList(List<Node>? nodes, bool fromList = true) => nodes is null || !fromList ? NodeList.None : S.NewList(CollectionsMarshal.AsSpan(nodes));

    private Node SIdentifier(string text)
    {
        var node = SNew(SyntaxKind.Identifier);
        S.SetEscapedText(node, S.Texts.Add(text.Length >= 2 && text[0] == '_' && text[1] == '_' ? "_" + text : text));
        return node;
    }

    private Node SToken(SyntaxKind kind) => SNew(kind);

    private Node SStringLiteral(string text, bool singleQuote = false)
    {
        var node = SNew(SyntaxKind.StringLiteral);
        S.SetText(node, S.Texts.Add(text));
        S.SetSingleQuote(node, singleQuote);
        return node;
    }

    private Node SNumericLiteral(double value)
    {
        var node = SNew(SyntaxKind.NumericLiteral);
        S.SetText(node, S.Texts.Add(JsNumber.ToString(value)));
        return node;
    }

    private Node SNumericLiteral(string text)
    {
        var node = SNew(SyntaxKind.NumericLiteral);
        S.SetText(node, S.Texts.Add(text));
        return node;
    }

    private EmitNode SEmitNode(Node node)
    {
        if (S.EmitNode(node) is not EmitNode emitNode)
        {
            emitNode = new EmitNode();
            S.SetEmitNode(node, emitNode);
        }
        return emitNode;
    }

    private Node SetEmitFlags(Node node, EmitFlags flags)
    {
        SEmitNode(node).Flags = flags;
        return node;
    }

    private EmitFlags GetSynthEmitFlags(Node node) => (S.EmitNode(node) as EmitNode)?.Flags ?? 0;

    private NodeList GetIdentifierTypeArguments(Node identifier) => (S.EmitNode(identifier) as EmitNode)?.IdentifierTypeArguments ?? NodeList.None;

    private void SetIdentifierTypeArguments(Node identifier, NodeList typeArguments)
    {
        if (typeArguments.IsNone && S.EmitNode(identifier) is not EmitNode)
        {
            return;
        }
        SEmitNode(identifier).IdentifierTypeArguments = typeArguments;
    }

    // checker.ts setTextRange(context, range, location): the node gets a link to the existing node it stands for, which
    // makes it not fully synthetic. A fresh synthesized node (no original yet) is cloned first, as TypeScript does, so
    // that other references to it (cached type parameter names) stay fully synthetic.
    private Node SetTextRange(NodeBuilderContext context, Node range, NodeRef location)
    {
        _ = context;
        if (location.IsNone || range.IsNone || reusedNodes.Contains(range))
        {
            return range;
        }
        return CloneSynthForReuse(range);
    }

    private bool IsFullySynthetic(Node node) => !reusedNodes.Contains(node);

    // ---- type node constructors (nodeFactory.ts with the type parenthesizer rules) ----

    private Node SKeyword(SyntaxKind kind) => SNew(kind);

    private Node STypeReference(Node typeName, NodeList typeArguments)
    {
        var node = SNew(SyntaxKind.TypeReference);
        S.SetTypeName(node, typeName);
        S.SetTypeArguments(node, ParenthesizeTypeArguments(typeArguments));
        return node;
    }

    private Node STypeReference(string name) => STypeReference(SIdentifier(name), NodeList.None);

    private Node SArrayType(Node elementType)
    {
        var node = SNew(SyntaxKind.ArrayType);
        S.SetElementType(node, ParenthesizeNonArrayTypeOfPostfixType(elementType));
        return node;
    }

    private Node STypeOperator(SyntaxKind @operator, Node type)
    {
        var node = SNew(SyntaxKind.TypeOperator);
        S.SetOperator(node, @operator);
        S.SetType(node, @operator == SyntaxKind.ReadonlyKeyword ? ParenthesizeOperandOfReadonlyTypeOperator(type) : ParenthesizeOperandOfTypeOperator(type));
        return node;
    }

    private Node SUnionType(List<Node> types)
    {
        var node = SNew(SyntaxKind.UnionType);
        var parenthesized = new Node[types.Count];
        for (var i = 0; i < types.Count; i++)
        {
            parenthesized[i] = ParenthesizeConstituentTypeOfUnionType(types[i]);
        }
        S.SetTypes(node, SList(parenthesized));
        return node;
    }

    private Node SIntersectionType(List<Node> types)
    {
        var node = SNew(SyntaxKind.IntersectionType);
        var parenthesized = new Node[types.Count];
        for (var i = 0; i < types.Count; i++)
        {
            parenthesized[i] = ParenthesizeConstituentTypeOfIntersectionType(types[i]);
        }
        S.SetTypes(node, SList(parenthesized));
        return node;
    }

    private Node SIndexedAccessType(Node objectType, Node indexType)
    {
        var node = SNew(SyntaxKind.IndexedAccessType);
        S.SetObjectType(node, ParenthesizeNonArrayTypeOfPostfixType(objectType));
        S.SetIndexType(node, indexType);
        return node;
    }

    private Node SLiteralType(Node literal)
    {
        var node = SNew(SyntaxKind.LiteralType);
        S.SetLiteral(node, literal);
        return node;
    }

    private Node STypeQuery(Node exprName, NodeList typeArguments = default)
    {
        var node = SNew(SyntaxKind.TypeQuery);
        S.SetExprName(node, exprName);
        S.SetTypeArguments(node, ParenthesizeTypeArguments(typeArguments));
        return node;
    }

    private Node SImportType(Node argument, Node attributes, Node qualifier, NodeList typeArguments, bool isTypeOf)
    {
        var node = SNew(SyntaxKind.ImportType);
        S.SetArgument(node, argument);
        S.SetAttributes(node, attributes);
        S.SetQualifier(node, qualifier);
        S.SetTypeArguments(node, ParenthesizeTypeArguments(typeArguments));
        S.SetIsTypeOf(node, isTypeOf);
        return node;
    }

    private Node SParenthesizedType(Node type)
    {
        var node = SNew(SyntaxKind.ParenthesizedType);
        S.SetType(node, type);
        return node;
    }

    private Node SConditionalType(Node checkType, Node extendsType, Node trueType, Node falseType)
    {
        var node = SNew(SyntaxKind.ConditionalType);
        S.SetCheckType(node, ParenthesizeCheckTypeOfConditionalType(checkType));
        S.SetExtendsType(node, ParenthesizeExtendsTypeOfConditionalType(extendsType));
        S.SetTrueType(node, trueType);
        S.SetFalseType(node, falseType);
        return node;
    }

    private Node SInferType(Node typeParameter)
    {
        var node = SNew(SyntaxKind.InferType);
        S.SetTypeParameter(node, typeParameter);
        return node;
    }

    private Node STupleType(List<Node> elements)
    {
        var node = SNew(SyntaxKind.TupleType);
        var parenthesized = new Node[elements.Count];
        for (var i = 0; i < elements.Count; i++)
        {
            parenthesized[i] = HasJSDocPostfixQuestion(elements[i]) ? SParenthesizedType(elements[i]) : elements[i];
        }
        S.SetElements(node, SList(parenthesized));
        return node;
    }

    private Node SNamedTupleMember(Node dotDotDotToken, Node name, Node questionToken, Node type)
    {
        var node = SNew(SyntaxKind.NamedTupleMember);
        S.SetDotDotDotToken(node, dotDotDotToken);
        S.SetName(node, name);
        S.SetQuestionToken(node, questionToken);
        S.SetType(node, type);
        return node;
    }

    private Node SOptionalType(Node type)
    {
        var node = SNew(SyntaxKind.OptionalType);
        S.SetType(node, HasJSDocPostfixQuestion(type) ? SParenthesizedType(type) : ParenthesizeNonArrayTypeOfPostfixType(type));
        return node;
    }

    private Node SRestType(Node type)
    {
        var node = SNew(SyntaxKind.RestType);
        S.SetType(node, type);
        return node;
    }

    private Node STypeLiteral(List<Node>? members)
    {
        var node = SNew(SyntaxKind.TypeLiteral);
        S.SetMembers(node, SList(members ?? []));
        return node;
    }

    private Node SMappedType(Node readonlyToken, Node typeParameter, Node nameType, Node questionToken, Node type)
    {
        var node = SNew(SyntaxKind.MappedType);
        S.SetReadonlyToken(node, readonlyToken);
        S.SetTypeParameter(node, typeParameter);
        S.SetNameType(node, nameType);
        S.SetQuestionToken(node, questionToken);
        S.SetType(node, type);
        return node;
    }

    private Node SQualifiedName(Node left, Node right)
    {
        var node = SNew(SyntaxKind.QualifiedName);
        S.SetLeft(node, left);
        S.SetRight(node, right);
        return node;
    }

    private Node STypeParameterDeclaration(NodeList modifiers, Node name, Node constraint, Node defaultType)
    {
        var node = SNew(SyntaxKind.TypeParameter);
        S.SetModifiers(node, modifiers);
        S.SetName(node, name);
        S.SetConstraint(node, constraint);
        S.SetDefault(node, defaultType);
        return node;
    }

    private Node SParameterDeclaration(NodeList modifiers, Node dotDotDotToken, Node name, Node questionToken, Node type)
    {
        var node = SNew(SyntaxKind.Parameter);
        S.SetModifiers(node, modifiers);
        S.SetDotDotDotToken(node, dotDotDotToken);
        S.SetName(node, name);
        S.SetQuestionToken(node, questionToken);
        S.SetType(node, type);
        return node;
    }

    private Node SPropertySignature(NodeList modifiers, Node name, Node questionToken, Node type)
    {
        var node = SNew(SyntaxKind.PropertySignature);
        S.SetModifiers(node, modifiers);
        S.SetName(node, name);
        S.SetQuestionToken(node, questionToken);
        S.SetType(node, type);
        return node;
    }

    private Node SSignature(SyntaxKind kind, NodeList modifiers, Node name, Node questionToken, NodeList typeParameters, NodeList parameters, Node type)
    {
        var node = SNew(kind);
        if (kind is SyntaxKind.MethodSignature or SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor or SyntaxKind.ConstructorType or SyntaxKind.IndexSignature or SyntaxKind.Constructor or SyntaxKind.FunctionDeclaration or SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction)
        {
            S.SetModifiers(node, modifiers);
        }
        if (kind is SyntaxKind.MethodSignature or SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor or SyntaxKind.FunctionDeclaration or SyntaxKind.FunctionExpression)
        {
            S.SetName(node, name);
        }
        if (kind is SyntaxKind.MethodSignature)
        {
            S.SetQuestionToken(node, questionToken);
        }
        if (kind is not (SyntaxKind.IndexSignature or SyntaxKind.Constructor or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor))
        {
            S.SetTypeParameters(node, typeParameters);
        }
        S.SetParameters(node, parameters);
        if (kind is not (SyntaxKind.Constructor or SyntaxKind.SetAccessor))
        {
            S.SetType(node, type);
        }
        return node;
    }

    private Node SPrefixUnaryExpression(SyntaxKind @operator, Node operand)
    {
        var node = SNew(SyntaxKind.PrefixUnaryExpression);
        S.SetOperator(node, @operator);
        S.SetOperand(node, operand);
        return node;
    }

    private Node SComputedPropertyName(Node expression)
    {
        var node = SNew(SyntaxKind.ComputedPropertyName);
        S.SetExpression(node, expression);
        return node;
    }

    private Node SPropertyAccessExpression(Node expression, Node name)
    {
        var node = SNew(SyntaxKind.PropertyAccessExpression);
        S.SetExpression(node, expression);
        S.SetName(node, name);
        // nodeFactory: createPropertyAccessExpression sets NoIndentation only with NoIndentationOnFreshPropertyAccess
        return node;
    }

    private Node SElementAccessExpression(Node expression, Node argumentExpression)
    {
        var node = SNew(SyntaxKind.ElementAccessExpression);
        S.SetExpression(node, expression);
        S.SetArgumentExpression(node, argumentExpression);
        return node;
    }

    // parenthesizerRules.ts: the type rules
    private Node ParenthesizeCheckTypeOfConditionalType(Node checkType) =>
        S.Kind(checkType) is SyntaxKind.FunctionType or SyntaxKind.ConstructorType or SyntaxKind.ConditionalType ? SParenthesizedType(checkType) : checkType;

    private Node ParenthesizeExtendsTypeOfConditionalType(Node extendsType) =>
        S.Kind(extendsType) == SyntaxKind.ConditionalType ? SParenthesizedType(extendsType) : extendsType;

    private Node ParenthesizeConstituentTypeOfUnionType(Node type) =>
        S.Kind(type) is SyntaxKind.UnionType or SyntaxKind.IntersectionType ? SParenthesizedType(type) : ParenthesizeCheckTypeOfConditionalType(type);

    private Node ParenthesizeConstituentTypeOfIntersectionType(Node type) =>
        S.Kind(type) is SyntaxKind.UnionType or SyntaxKind.IntersectionType ? SParenthesizedType(type) : ParenthesizeConstituentTypeOfUnionType(type);

    private Node ParenthesizeOperandOfTypeOperator(Node type) =>
        S.Kind(type) == SyntaxKind.IntersectionType ? SParenthesizedType(type) : ParenthesizeConstituentTypeOfIntersectionType(type);

    private Node ParenthesizeOperandOfReadonlyTypeOperator(Node type) =>
        S.Kind(type) == SyntaxKind.TypeOperator ? SParenthesizedType(type) : ParenthesizeOperandOfTypeOperator(type);

    private Node ParenthesizeNonArrayTypeOfPostfixType(Node type) =>
        S.Kind(type) is SyntaxKind.InferType or SyntaxKind.TypeOperator or SyntaxKind.TypeQuery ? SParenthesizedType(type) : ParenthesizeOperandOfTypeOperator(type);

    private bool HasJSDocPostfixQuestion(Node type)
    {
        switch (S.Kind(type))
        {
            case SyntaxKind.JSDocNullableType:
                return S.Postfix(type);
            case SyntaxKind.NamedTupleMember:
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.TypeOperator:
                return HasJSDocPostfixQuestion(S.Type(type));
            case SyntaxKind.ConditionalType:
                return HasJSDocPostfixQuestion(S.FalseType(type));
            case SyntaxKind.UnionType:
            case SyntaxKind.IntersectionType:
            {
                var types = S.Types(type);
                return HasJSDocPostfixQuestion(S.Element(types, S.Length(types) - 1));
            }
            case SyntaxKind.InferType:
            {
                var constraint = S.Constraint(S.TypeParameter(type));
                return !constraint.IsNone && HasJSDocPostfixQuestion(constraint);
            }
            default:
                return false;
        }
    }

    private NodeList ParenthesizeTypeArguments(NodeList typeArguments)
    {
        if (typeArguments.IsNone || S.Length(typeArguments) == 0)
        {
            return typeArguments;
        }
        var first = S.Element(typeArguments, 0);
        if (S.Kind(first) is SyntaxKind.FunctionType or SyntaxKind.ConstructorType && !S.TypeParameters(first).IsNone)
        {
            var nodes = S.Nodes(typeArguments).ToArray();
            nodes[0] = SParenthesizedType(first);
            return SList(nodes);
        }
        return typeArguments;
    }

    // ---- entry points ----

    private static NodeBuilderContext CreateNodeBuilderContext(NodeRef enclosingDeclaration, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, int maximumLength = 0)
    {
        return new NodeBuilderContext
        {
            EnclosingDeclaration = enclosingDeclaration,
            EnclosingFile = enclosingDeclaration.IsNone ? 0 : enclosingDeclaration.File,
            Flags = flags,
            InternalFlags = internalFlags,
            MaxTruncationLength = maximumLength != 0 ? maximumLength : (flags & NodeBuilderFlags.NoTruncation) != 0 ? NoTruncationMaximumTruncationLength : DefaultMaximumTruncationLength,
        };
    }

    // printer.writeNode(EmitHint.Unspecified, node, sourceFile, writer) with createTextWriter("") (single line)
    private string PrintSynthesizedNode(Node node, bool neverAsciiEscape = false, StringBuilder? underline = null)
    {
        var writer = new EmitTextWriter("");
        writer.Underline = underline;
        var printer = Printer.CreatePrinter(new PrinterOptions { RemoveComments = true, NeverAsciiEscape = neverAsciiEscape });
        if (underline is not null)
        {
            printer.IsFullySynthetic = IsFullySynthetic;
        }
        printer.WriteNode(EmitHint.Unspecified, S, node, Node.None, writer);
        return writer.GetText();
    }

    /// <summary>
    /// checker.ts typeToString(type, enclosingDeclaration, flags = AllowUniqueESSymbolType | UseAliasDefinedOutsideCurrentScope).
    /// </summary>
    internal string TypeToString(Type type, NodeRef enclosingDeclaration = default, TypeFormatFlags flags = TypeFormatFlags.AllowUniqueESSymbolType | TypeFormatFlags.UseAliasDefinedOutsideCurrentScope, int maximumLength = 0)
    {
        // (compilerOptions.noErrorTruncation is not among Track O's options yet: undefined)
        var noTruncation = (flags & TypeFormatFlags.NoTruncation) != 0;
        BeginSynth();
        try
        {
            var context = CreateNodeBuilderContext(enclosingDeclaration, ToNodeBuilderFlags(flags) | NodeBuilderFlags.IgnoreErrors | (noTruncation ? NodeBuilderFlags.NoTruncation : 0), InternalNodeBuilderFlags.None, maximumLength);
            var typeNode = TypeToTypeNodeHelper(type, context);
            var result = typeNode.IsNone ? "" : PrintSynthesizedNode(typeNode);
            var maxLength = maximumLength != 0 ? maximumLength : noTruncation ? NoTruncationMaximumTruncationLength * 2 : DefaultMaximumTruncationLength * 2;
            if (result.Length > 0 && result.Length >= maxLength)
            {
                return result[..(maxLength - 3)] + "...";
            }
            return result;
        }
        finally
        {
            EndSynth();
        }
    }

    private static NodeBuilderFlags ToNodeBuilderFlags(TypeFormatFlags flags) => (NodeBuilderFlags)(flags & TypeFormatFlags.NodeBuilderFlagsMask);

    /// <summary>
    /// typeWriter.ts: typeToTypeNode(type, enclosingDeclaration, (NoTruncation | AllowUniqueESSymbolType |
    /// GenerateNamesForShadowedTypeParams | IgnoreErrors), AllowUnresolvedNames) printed by the underlining printer: the
    /// type text and its underline (^ under fully synthesized nodes, spaces under reused ones).
    /// </summary>
    internal (string Text, string Underline) TypeToBaselineText(Type type, NodeRef enclosingDeclaration, bool inTypeAlias)
    {
        var flags = TypeFormatFlags.NoTruncation | TypeFormatFlags.AllowUniqueESSymbolType | TypeFormatFlags.GenerateNamesForShadowedTypeParams;
        if (inTypeAlias)
        {
            flags |= TypeFormatFlags.InTypeAlias;
        }
        BeginSynth();
        try
        {
            var context = CreateNodeBuilderContext(enclosingDeclaration, ToNodeBuilderFlags(flags) | NodeBuilderFlags.IgnoreErrors, inTypeAlias ? InternalNodeBuilderFlags.None : InternalNodeBuilderFlags.AllowUnresolvedNames);
            var typeNode = TypeToTypeNodeHelper(type, context);
            if (typeNode.IsNone)
            {
                return ("", "");
            }
            var underline = new StringBuilder();
            var text = PrintSynthesizedNode(typeNode, false, underline);
            return (text, underline.ToString());
        }
        finally
        {
            EndSynth();
        }
    }

    /// <summary>checker.ts symbolToString(symbol, enclosingDeclaration, meaning, flags = AllowAnyNodeKind).</summary>
    internal string SymbolToString(SymbolRef symbol, NodeRef enclosingDeclaration = default, SymbolFlags meaning = SymbolFlags.None, SymbolFormatFlags flags = SymbolFormatFlags.AllowAnyNodeKind)
    {
        var nodeFlags = NodeBuilderFlags.IgnoreErrors;
        var internalNodeFlags = InternalNodeBuilderFlags.None;
        if ((flags & SymbolFormatFlags.UseOnlyExternalAliasing) != 0)
        {
            nodeFlags |= NodeBuilderFlags.UseOnlyExternalAliasing;
        }
        if ((flags & SymbolFormatFlags.WriteTypeParametersOrArguments) != 0)
        {
            nodeFlags |= NodeBuilderFlags.WriteTypeParametersInQualifiedName;
        }
        if ((flags & SymbolFormatFlags.UseAliasDefinedOutsideCurrentScope) != 0)
        {
            nodeFlags |= NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope;
        }
        if ((flags & SymbolFormatFlags.DoNotIncludeSymbolChain) != 0)
        {
            internalNodeFlags |= InternalNodeBuilderFlags.DoNotIncludeSymbolChain;
        }
        if ((flags & SymbolFormatFlags.WriteComputedProps) != 0)
        {
            internalNodeFlags |= InternalNodeBuilderFlags.WriteComputedProps;
        }
        BeginSynth();
        try
        {
            var context = CreateNodeBuilderContext(enclosingDeclaration, nodeFlags, internalNodeFlags);
            var entity = (flags & SymbolFormatFlags.AllowAnyNodeKind) != 0 ? SymbolToNode(symbol, context, meaning) : SymbolToName(symbol, context, meaning, expectsIdentifier: false);
            // add neverAsciiEscape for GH#39027
            return PrintSynthesizedNode(entity, neverAsciiEscape: !enclosingDeclaration.IsNone && Kind(enclosingDeclaration) == SyntaxKind.SourceFile);
        }
        finally
        {
            EndSynth();
        }
    }

    /// <summary>checker.ts signatureToString(signature, enclosingDeclaration, flags, kind).</summary>
    internal string SignatureToString(Signature signature, NodeRef enclosingDeclaration = default, TypeFormatFlags flags = TypeFormatFlags.None, SignatureKind kind = SignatureKind.Call)
    {
        SyntaxKind sigOutput;
        if ((flags & TypeFormatFlags.WriteArrowStyleSignature) != 0)
        {
            sigOutput = kind == SignatureKind.Construct ? SyntaxKind.ConstructorType : SyntaxKind.FunctionType;
        }
        else
        {
            sigOutput = kind == SignatureKind.Construct ? SyntaxKind.ConstructSignature : SyntaxKind.CallSignature;
        }
        BeginSynth();
        try
        {
            var context = CreateNodeBuilderContext(enclosingDeclaration, ToNodeBuilderFlags(flags) | NodeBuilderFlags.IgnoreErrors | NodeBuilderFlags.WriteTypeParametersInQualifiedName, InternalNodeBuilderFlags.None);
            var sig = SignatureToSignatureDeclarationHelper(signature, sigOutput, context);
            var text = PrintSynthesizedNode(sig);
            // createPrinterWithRemoveCommentsOmitTrailingSemicolon with getTrailingSemicolonDeferringWriter: the final ";" is not written
            return text.EndsWith(';') ? text[..^1] : text;
        }
        finally
        {
            EndSynth();
        }
    }

    /// <summary>checker.ts typePredicateToString(typePredicate, enclosingDeclaration, flags = UseAliasDefinedOutsideCurrentScope).</summary>
    internal string TypePredicateToString(TypePredicateInfo typePredicate, NodeRef enclosingDeclaration = default, TypeFormatFlags flags = TypeFormatFlags.UseAliasDefinedOutsideCurrentScope)
    {
        BeginSynth();
        try
        {
            var context = CreateNodeBuilderContext(enclosingDeclaration, ToNodeBuilderFlags(flags) | NodeBuilderFlags.IgnoreErrors | NodeBuilderFlags.WriteTypeParametersInQualifiedName, InternalNodeBuilderFlags.None);
            return PrintSynthesizedNode(TypePredicateToTypePredicateNodeHelper(typePredicate, context));
        }
        finally
        {
            EndSynth();
        }
    }

    // ---- flags save/restore and truncation ----

    private readonly struct SavedFlags(NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, int depth)
    {
        public void Restore(NodeBuilderContext context)
        {
            context.Flags = flags;
            context.InternalFlags = internalFlags;
            context.Depth = depth;
        }
    }

    private static SavedFlags SaveRestoreFlags(NodeBuilderContext context) => new(context.Flags, context.InternalFlags, context.Depth);

    private static bool CheckTruncationLength(NodeBuilderContext context)
    {
        if (context.Truncating)
        {
            return context.Truncating;
        }
        return context.Truncating = context.ApproximateLength > context.MaxTruncationLength;
    }

    private static bool IsExpanding(NodeBuilderContext context) => context.MaxExpansionDepth != -1;

    // checker.ts canPossiblyExpandType
    private static bool CanPossiblyExpandType(Type type, NodeBuilderContext context)
    {
        for (var i = 0; i < context.TypeStack.Count - 1; i++)
        {
            if (context.TypeStack[i] == type.Id)
            {
                return false;
            }
        }
        return context.Depth < context.MaxExpansionDepth ||
            (context.Depth == context.MaxExpansionDepth && !context.CanIncreaseExpansionDepth);
    }

    // checker.ts shouldExpandType
    private bool ShouldExpandType(Type type, NodeBuilderContext context, bool isAlias = false)
    {
        if (!isAlias && IsLibType(type))
        {
            return false;
        }
        for (var i = 0; i < context.TypeStack.Count - 1; i++)
        {
            if (context.TypeStack[i] == type.Id)
            {
                return false;
            }
        }
        var result = context.Depth < context.MaxExpansionDepth;
        if (!result)
        {
            // This type would have been expanded if `maxExpansionDepth` was increased.
            context.CanIncreaseExpansionDepth = true;
        }
        return result;
    }

    // checker.ts isLibType: don't expand types like `Array` or `Promise`, instead treating them as opaque
    private bool IsLibType(Type type)
    {
        var symbol = (Types.ObjectFlagsOf(type) & ObjectFlags.Reference) != 0 ? Types.Symbol(Types.Object(type).Target) : Types.Symbol(type);
        if (IsTupleType(type))
        {
            return true;
        }
        foreach (var declaration in symbol.IsNone ? [] : DeclarationsOf(symbol) ?? [])
        {
            if (program.IsSourceFileDefaultLibrary(FileAt(declaration.File)))
            {
                return true;
            }
        }
        return false;
    }

    // ---- typeToTypeNodeHelper ----

    private Node TypeToTypeNodeHelper(Type type, NodeBuilderContext context)
    {
        var saved = SaveRestoreFlags(context);
        if (!type.IsNone)
        {
            context.TypeStack.Add(type.Id);
        }
        var typeNode = TypeToTypeNodeWorker(type, context);
        if (!type.IsNone)
        {
            context.TypeStack.RemoveAt(context.TypeStack.Count - 1);
        }
        saved.Restore(context);
        return typeNode;
    }

    private Node TypeToTypeNodeWorker(Type type, NodeBuilderContext context)
    {
        var inTypeAlias = (context.Flags & NodeBuilderFlags.InTypeAlias) != 0;
        context.Flags &= ~NodeBuilderFlags.InTypeAlias;
        var expandingEnum = false;

        if (type.IsNone)
        {
            if ((context.Flags & NodeBuilderFlags.AllowEmptyUnionOrIntersection) == 0)
            {
                context.EncounteredError = true;
                return Node.None;
            }
            context.ApproximateLength += 3;
            return SKeyword(SyntaxKind.AnyKeyword);
        }

        if ((context.Flags & NodeBuilderFlags.NoTypeReduction) == 0)
        {
            type = GetReducedType(type);
        }

        var flags = Types.Flags(type);
        ref var record = ref Types.Record(type);
        if ((flags & TypeFlags.Any) != 0)
        {
            if (!record.AliasSymbol.IsNone)
            {
                return STypeReference(SymbolToEntityNameNode(record.AliasSymbol), SList(MapToTypeNodes(Types.Elements(record.AliasTypeArguments).ToArray(), context)));
            }
            if (type == unresolvedType)
            {
                // (the synthetic /*unresolved*/ comment is not printed with removeComments)
                return SKeyword(SyntaxKind.AnyKeyword);
            }
            context.ApproximateLength += 3;
            return SKeyword(type == intrinsicMarkerType ? SyntaxKind.IntrinsicKeyword : SyntaxKind.AnyKeyword);
        }
        if ((flags & TypeFlags.Unknown) != 0)
        {
            return SKeyword(SyntaxKind.UnknownKeyword);
        }
        if ((flags & TypeFlags.String) != 0)
        {
            context.ApproximateLength += 6;
            return SKeyword(SyntaxKind.StringKeyword);
        }
        if ((flags & TypeFlags.Number) != 0)
        {
            context.ApproximateLength += 6;
            return SKeyword(SyntaxKind.NumberKeyword);
        }
        if ((flags & TypeFlags.BigInt) != 0)
        {
            context.ApproximateLength += 6;
            return SKeyword(SyntaxKind.BigIntKeyword);
        }
        if ((flags & TypeFlags.Boolean) != 0 && record.AliasSymbol.IsNone)
        {
            context.ApproximateLength += 7;
            return SKeyword(SyntaxKind.BooleanKeyword);
        }
        if ((flags & TypeFlags.EnumLike) != 0)
        {
            if ((SymbolFlagsOf(record.Symbol) & SymbolFlags.EnumMember) != 0)
            {
                var parentSymbol = GetParentOfSymbol(record.Symbol);
                var parentName = SymbolToTypeNode(parentSymbol, context, SymbolFlags.Type);
                if (GetDeclaredTypeOfSymbol(parentSymbol) == type)
                {
                    return parentName;
                }
                var memberName = SymbolName(record.Symbol);
                if (Scanner.IsIdentifierText(memberName, ScriptTarget.ESNext))
                {
                    return AppendReferenceToType(parentName, STypeReference(memberName));
                }
                if (S.Kind(parentName) == SyntaxKind.ImportType)
                {
                    S.SetIsTypeOf(parentName, true); // mutably update, node is freshly manufactured anyhow
                    return SIndexedAccessType(parentName, SLiteralType(SStringLiteral(memberName)));
                }
                if (S.Kind(parentName) == SyntaxKind.TypeReference)
                {
                    return SIndexedAccessType(STypeQuery(S.TypeName(parentName)), SLiteralType(SStringLiteral(memberName)));
                }
                throw new InvalidOperationException("Unhandled type node kind returned from `symbolToTypeNode`.");
            }
            if (!ShouldExpandType(type, context))
            {
                return SymbolToTypeNode(record.Symbol, context, SymbolFlags.Type);
            }
            expandingEnum = true;
        }
        if ((flags & TypeFlags.StringLiteral) != 0)
        {
            var value = (string)Types.Literal(type).Value!;
            context.ApproximateLength += value.Length + 2;
            return SLiteralType(SetEmitFlags(SStringLiteral(value, (context.Flags & NodeBuilderFlags.UseSingleQuotesForStringLiteralType) != 0), EmitFlags.NoAsciiEscaping));
        }
        if ((flags & TypeFlags.NumberLiteral) != 0)
        {
            var value = (double)Types.Literal(type).Value!;
            context.ApproximateLength += JsNumber.ToString(value).Length;
            return SLiteralType(value < 0 ? SPrefixUnaryExpression(SyntaxKind.MinusToken, SNumericLiteral(-value)) : SNumericLiteral(value));
        }
        if ((flags & TypeFlags.BigIntLiteral) != 0)
        {
            var text = (string)Types.Literal(type).Value!;
            context.ApproximateLength += text.Length + 1;
            var literal = SNew(SyntaxKind.BigIntLiteral);
            S.SetText(literal, S.Texts.Add(text + "n"));
            return SLiteralType(literal);
        }
        if ((flags & TypeFlags.BooleanLiteral) != 0)
        {
            var name = IntrinsicNameOf(type);
            context.ApproximateLength += name.Length;
            return SLiteralType(SToken(name == "true" ? SyntaxKind.TrueKeyword : SyntaxKind.FalseKeyword));
        }
        if ((flags & TypeFlags.UniqueESSymbol) != 0)
        {
            if ((context.Flags & NodeBuilderFlags.AllowUniqueESSymbolType) == 0)
            {
                if (IsValueSymbolAccessible(record.Symbol, context.EnclosingDeclaration, context.FakeScope))
                {
                    context.ApproximateLength += 6;
                    return SymbolToTypeNode(record.Symbol, context, SymbolFlags.Value);
                }
            }
            context.ApproximateLength += 13;
            return STypeOperator(SyntaxKind.UniqueKeyword, SKeyword(SyntaxKind.SymbolKeyword));
        }
        if ((flags & TypeFlags.Void) != 0)
        {
            context.ApproximateLength += 4;
            return SKeyword(SyntaxKind.VoidKeyword);
        }
        if ((flags & TypeFlags.Undefined) != 0)
        {
            context.ApproximateLength += 9;
            return SKeyword(SyntaxKind.UndefinedKeyword);
        }
        if ((flags & TypeFlags.Null) != 0)
        {
            context.ApproximateLength += 4;
            return SLiteralType(SToken(SyntaxKind.NullKeyword));
        }
        if ((flags & TypeFlags.Never) != 0)
        {
            context.ApproximateLength += 5;
            return SKeyword(SyntaxKind.NeverKeyword);
        }
        if ((flags & TypeFlags.ESSymbol) != 0)
        {
            context.ApproximateLength += 6;
            return SKeyword(SyntaxKind.SymbolKeyword);
        }
        if ((flags & TypeFlags.NonPrimitive) != 0)
        {
            context.ApproximateLength += 6;
            return SKeyword(SyntaxKind.ObjectKeyword);
        }
        if ((flags & TypeFlags.TypeParameter) != 0 && Types.TypeParameter(type).IsThisType)
        {
            if ((context.Flags & NodeBuilderFlags.InObjectTypeLiteral) != 0)
            {
                if (!context.EncounteredError && (context.Flags & NodeBuilderFlags.AllowThisInObjectLiteral) == 0)
                {
                    context.EncounteredError = true;
                }
            }
            context.ApproximateLength += 4;
            return SToken(SyntaxKind.ThisType);
        }

        if (!inTypeAlias && !record.AliasSymbol.IsNone && ((context.Flags & NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope) != 0 || IsTypeSymbolAccessible(record.AliasSymbol, context.EnclosingDeclaration, context.FakeScope)))
        {
            if (!ShouldExpandType(type, context, isAlias: true))
            {
                var typeArgumentNodes = MapToTypeNodes(Types.Elements(record.AliasTypeArguments).ToArray(), context);
                if (IsReservedMemberName(NameOf(record.AliasSymbol)) && (SymbolFlagsOf(record.AliasSymbol) & SymbolFlags.Class) == 0)
                {
                    return STypeReference(SIdentifier(""), SList(typeArgumentNodes));
                }
                if (typeArgumentNodes is { Count: 1 } && record.AliasSymbol == Types.Symbol(globalArrayType))
                {
                    return SArrayType(typeArgumentNodes[0]);
                }
                return SymbolToTypeNode(record.AliasSymbol, context, SymbolFlags.Type, typeArgumentNodes);
            }
            context.Depth += 1;
        }

        var objectFlags = Types.ObjectFlagsOf(type);

        if ((objectFlags & ObjectFlags.Reference) != 0)
        {
            if (ShouldExpandType(type, context))
            {
                context.Depth += 1;
                return CreateAnonymousTypeNode(type, context, forceClassExpansion: true, forceExpansion: true);
            }
            return !Types.Object(type).Node.IsNone ? VisitAndTransformType(type, context, t => TypeReferenceToTypeNode(t, context)) : TypeReferenceToTypeNode(type, context);
        }
        if ((flags & TypeFlags.TypeParameter) != 0 || (objectFlags & ObjectFlags.ClassOrInterface) != 0)
        {
            if ((flags & TypeFlags.TypeParameter) != 0 && context.InferTypeParameters is not null && context.InferTypeParameters.Contains(type))
            {
                context.ApproximateLength += SymbolName(record.Symbol).Length + 6;
                var constraintNode = Node.None;
                var constraint = GetConstraintOfTypeParameter(type);
                if (!constraint.IsNone)
                {
                    // If the infer type has a constraint that is not the same as the constraint
                    // we would have normally inferred based on context, we emit the constraint
                    // using `infer T extends ?`. We omit inferred constraints from type references
                    // as they may be elided.
                    var inferredConstraint = GetInferredTypeParameterConstraint(type, omitTypeReferences: true);
                    if (!(!inferredConstraint.IsNone && IsTypeIdenticalTo(constraint, inferredConstraint)))
                    {
                        context.ApproximateLength += 9;
                        constraintNode = TypeToTypeNodeHelper(constraint, context);
                    }
                }
                return SInferType(TypeParameterToDeclarationWithConstraint(type, context, constraintNode));
            }
            if ((context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0 && (flags & TypeFlags.TypeParameter) != 0)
            {
                var name = TypeParameterToName(type, context);
                var nameText = IdTextOf(name);
                context.ApproximateLength += nameText.Length;
                return STypeReference(SIdentifier(nameText), NodeList.None);
            }
            if ((objectFlags & ObjectFlags.ClassOrInterface) != 0 && ShouldExpandType(type, context))
            {
                context.Depth += 1;
                return CreateAnonymousTypeNode(type, context, forceClassExpansion: true, forceExpansion: true);
            }
            // Ignore constraint/default when creating a usage (as opposed to declaration) of a type parameter.
            if (!record.Symbol.IsNone)
            {
                return SymbolToTypeNode(record.Symbol, context, SymbolFlags.Type);
            }
            return STypeReference("?");
        }
        if ((flags & TypeFlags.Union) != 0 && !Types.Union(type).Origin.IsNone)
        {
            type = Types.Union(type).Origin;
            flags = Types.Flags(type);
        }
        if ((flags & (TypeFlags.Union | TypeFlags.Intersection)) != 0)
        {
            var members = Types.Elements(Types.Union(type).Types).ToArray();
            var types = (flags & TypeFlags.Union) != 0 ? FormatUnionTypes(members, expandingEnum) : [.. members];
            if (types.Count == 1)
            {
                return TypeToTypeNodeHelper(types[0], context);
            }
            var typeNodes = MapToTypeNodes([.. types], context, isBareList: true);
            if (typeNodes is { Count: > 0 })
            {
                return (flags & TypeFlags.Union) != 0 ? SUnionType(typeNodes) : SIntersectionType(typeNodes);
            }
            if (!context.EncounteredError && (context.Flags & NodeBuilderFlags.AllowEmptyUnionOrIntersection) == 0)
            {
                context.EncounteredError = true;
            }
            return Node.None;
        }
        if ((objectFlags & (ObjectFlags.Anonymous | ObjectFlags.Mapped)) != 0)
        {
            // The type is an object literal type.
            return CreateAnonymousTypeNode(type, context);
        }
        if ((flags & TypeFlags.Index) != 0)
        {
            var indexedType = GetIndexTypeOperand(type);
            context.ApproximateLength += 6;
            var indexTypeNode = TypeToTypeNodeHelper(indexedType, context);
            return STypeOperator(SyntaxKind.KeyOfKeyword, indexTypeNode);
        }
        if ((flags & TypeFlags.TemplateLiteral) != 0)
        {
            var (texts, types) = GetTemplateLiteralParts(type);
            var templateHead = SNew(SyntaxKind.TemplateHead);
            S.SetText(templateHead, S.Texts.Add(texts[0]));
            var templateSpans = new List<Node>();
            for (var i = 0; i < types.Length; i++)
            {
                var literal = SNew(i < types.Length - 1 ? SyntaxKind.TemplateMiddle : SyntaxKind.TemplateTail);
                S.SetText(literal, S.Texts.Add(texts[i + 1]));
                var span = SNew(SyntaxKind.TemplateLiteralTypeSpan);
                S.SetType(span, TypeToTypeNodeHelper(types[i], context));
                S.SetLiteral(span, literal);
                templateSpans.Add(span);
            }
            context.ApproximateLength += 2;
            var node = SNew(SyntaxKind.TemplateLiteralType);
            S.SetHead(node, templateHead);
            S.SetTemplateSpans(node, SList(templateSpans));
            return node;
        }
        if ((flags & TypeFlags.StringMapping) != 0)
        {
            var typeNode = TypeToTypeNodeHelper(GetStringMappingOperand(type), context);
            return SymbolToTypeNode(record.Symbol, context, SymbolFlags.Type, [typeNode]);
        }
        if ((flags & TypeFlags.IndexedAccess) != 0)
        {
            var (objectType, indexType) = GetIndexedAccessParts(type);
            var objectTypeNode = TypeToTypeNodeHelper(objectType, context);
            var indexTypeNode = TypeToTypeNodeHelper(indexType, context);
            context.ApproximateLength += 2;
            return SIndexedAccessType(objectTypeNode, indexTypeNode);
        }
        if ((flags & TypeFlags.Conditional) != 0)
        {
            return VisitAndTransformType(type, context, t => ConditionalTypeToTypeNode(t, context));
        }
        if ((flags & TypeFlags.Substitution) != 0)
        {
            var typeNode = TypeToTypeNodeHelper(GetSubstitutionBaseType(type), context);
            var noInferSymbol = IsNoInferType(type) ? GetGlobalTypeSymbol("NoInfer", reportErrors: false) : SymbolRef.None;
            return !noInferSymbol.IsNone ? SymbolToTypeNode(noInferSymbol, context, SymbolFlags.Type, [typeNode]) : typeNode;
        }
        throw new InvalidOperationException("Should be unreachable.");
    }

    private string IdTextOf(Node identifier) => Utilities.UnescapeLeadingUnderscores(S.Texts.GetString(S.EscapedText(identifier)) ?? "");

    // checker.ts conditionalTypeToTypeNode
    private Node ConditionalTypeToTypeNode(Type type, NodeBuilderContext context)
    {
        var parts = GetConditionalTypeParts(type);
        var checkTypeNode = TypeToTypeNodeHelper(parts.CheckType, context);
        context.ApproximateLength += 15;
        if ((context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0 && parts.IsDistributive && (Types.Flags(parts.CheckType) & TypeFlags.TypeParameter) == 0)
        {
            var newParam = Types.CreateTypeParameter(CreateSymbol(SymbolFlags.TypeParameter, "T"));
            var name = TypeParameterToName(newParam, context);
            var newTypeVariable = STypeReference(name, NodeList.None);
            context.ApproximateLength += 37; // 15 each for two added conditionals, 7 for an added infer type
            var (extendsType, trueType, falseType) = GetConditionalTypeBranchesWithNewCheckType(type, newParam);
            var saveInferTypeParameters = context.InferTypeParameters;
            context.InferTypeParameters = parts.InferTypeParameters;
            var extendsTypeNode = TypeToTypeNodeHelper(extendsType, context);
            context.InferTypeParameters = saveInferTypeParameters;
            var trueTypeNode = TypeToTypeNodeOrCircularityElision(trueType, context);
            var falseTypeNode = TypeToTypeNodeOrCircularityElision(falseType, context);
            // checkType extends infer T ? T extends checkType ? T extends extendsType<T> ? trueType<T> : falseType<T> : never : never;
            return SConditionalType(
                checkTypeNode,
                SInferType(STypeParameterDeclaration(NodeList.None, SIdentifier(IdTextOf(name)), Node.None, Node.None)),
                SConditionalType(
                    STypeReference(SIdentifier(IdTextOf(name)), NodeList.None),
                    TypeToTypeNodeHelper(parts.CheckType, context),
                    SConditionalType(newTypeVariable, extendsTypeNode, trueTypeNode, falseTypeNode),
                    SKeyword(SyntaxKind.NeverKeyword)),
                SKeyword(SyntaxKind.NeverKeyword));
        }
        var saveInfer = context.InferTypeParameters;
        context.InferTypeParameters = parts.InferTypeParameters;
        var extendsNode = TypeToTypeNodeHelper(parts.ExtendsType, context);
        context.InferTypeParameters = saveInfer;
        var trueNode = TypeToTypeNodeOrCircularityElision(GetTrueTypeFromConditionalType(type), context);
        var falseNode = TypeToTypeNodeOrCircularityElision(GetFalseTypeFromConditionalType(type), context);
        return SConditionalType(checkTypeNode, extendsNode, trueNode, falseNode);
    }

    private Node TypeToTypeNodeOrCircularityElision(Type type, NodeBuilderContext context)
    {
        if ((Types.Flags(type) & TypeFlags.Union) != 0)
        {
            if (context.VisitedTypes is not null && context.VisitedTypes.Contains(type))
            {
                if ((context.Flags & NodeBuilderFlags.AllowAnonymousIdentifier) == 0)
                {
                    context.EncounteredError = true;
                }
                return CreateElidedInformationPlaceholder(context);
            }
            return VisitAndTransformType(type, context, t => TypeToTypeNodeHelper(t, context));
        }
        return TypeToTypeNodeHelper(type, context);
    }

    // checker.ts createMappedTypeNodeFromType
    private Node CreateMappedTypeNodeFromType(Type type, NodeBuilderContext context)
    {
        var declaration = GetMappedTypeDeclaration(type);
        var declarationStore = StoreOf(declaration);
        var readonlyTokenNode = declarationStore.ReadonlyToken(declaration.Node);
        var questionTokenNode = declarationStore.QuestionToken(declaration.Node);
        var readonlyToken = readonlyTokenNode.IsNone ? Node.None : SToken(declarationStore.Kind(readonlyTokenNode));
        var questionToken = questionTokenNode.IsNone ? Node.None : SToken(declarationStore.Kind(questionTokenNode));
        Node appropriateConstraintTypeNode;
        var newTypeVariable = Node.None;
        var templateType = GetTemplateTypeFromMappedType(type);
        var typeParameter = GetTypeParameterFromMappedType(type);
        // If the mapped type isn't `keyof` constraint-declared, _but_ still has modifiers preserved, and its naive instantiation won't preserve modifiers because its constraint isn't `keyof` constrained, we have work to do
        var constraintType = GetConstraintTypeFromMappedType(type);
        var needsModifierPreservingWrapper = !IsMappedTypeWithKeyofConstraintDeclaration(type)
            && (Types.Flags(GetModifiersTypeFromMappedType(type)) & TypeFlags.Unknown) == 0
            && (context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0
            && !((Types.Flags(constraintType) & TypeFlags.TypeParameter) != 0 && !GetConstraintOfTypeParameter(constraintType).IsNone && (Types.Flags(GetConstraintOfTypeParameter(constraintType)) & TypeFlags.Index) != 0);
        if (IsMappedTypeWithKeyofConstraintDeclaration(type))
        {
            // We have a { [P in keyof T]: X }
            // We do this to ensure we retain the toplevel keyof-ness of the type which may be lost due to keyof distribution during `getConstraintTypeFromMappedType`
            if (IsHomomorphicMappedTypeWithNonHomomorphicInstantiation(type) && (context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0)
            {
                var newConstraintParam = Types.CreateTypeParameter(CreateSymbol(SymbolFlags.TypeParameter, "T"));
                var name = TypeParameterToName(newConstraintParam, context);
                newTypeVariable = STypeReference(name, NodeList.None);
                templateType = InstantiateMappedTemplateForNewConstraint(type, newConstraintParam);
            }
            appropriateConstraintTypeNode = STypeOperator(SyntaxKind.KeyOfKeyword, !newTypeVariable.IsNone ? newTypeVariable : TypeToTypeNodeHelper(GetModifiersTypeFromMappedType(type), context));
        }
        else if (needsModifierPreservingWrapper)
        {
            // So, step 1: new type variable
            var newParam = Types.CreateTypeParameter(CreateSymbol(SymbolFlags.TypeParameter, "T"));
            var name = TypeParameterToName(newParam, context);
            newTypeVariable = STypeReference(name, NodeList.None);
            // step 2: make that new type variable itself the constraint node, making the mapped type `{[K in T_1]: Template}`
            appropriateConstraintTypeNode = newTypeVariable;
        }
        else
        {
            appropriateConstraintTypeNode = TypeToTypeNodeHelper(constraintType, context);
        }
        var typeParameterNode = TypeParameterToDeclarationWithConstraint(typeParameter, context, appropriateConstraintTypeNode);

        // nameType and templateType nodes have to be in the new scope
        var declarationTypeParameter = At(declaration, declarationStore.TypeParameter(declaration.Node));
        var cleanup = EnterNewScope(context, declaration, null, [GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(declarationTypeParameter))]);
        var nameTypeNode = !declarationStore.NameType(declaration.Node).IsNone ? TypeToTypeNodeHelper(GetNameTypeFromMappedType(type), context) : Node.None;
        var templateTypeNode = TypeToTypeNodeHelper(RemoveMissingType(templateType, (GetMappedTypeModifiers(type) & MappedTypeModifiers.IncludeOptional) != 0), context);
        cleanup();

        var mappedTypeNode = SMappedType(readonlyToken, typeParameterNode, nameTypeNode, questionToken, templateTypeNode);
        context.ApproximateLength += 10;
        var result = SetEmitFlags(mappedTypeNode, EmitFlags.SingleLine);
        if (IsHomomorphicMappedTypeWithNonHomomorphicInstantiation(type) && (context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0)
        {
            // homomorphic mapped type with a non-homomorphic naive inlining
            // wrap it with a conditional like `SomeModifiersType extends infer U ? {..the mapped type...} : never` to ensure the resulting
            // type stays homomorphic
            var originalConstraint = GetOriginalConstraintOfHomomorphicMappedType(type);
            return SConditionalType(
                TypeToTypeNodeHelper(GetModifiersTypeFromMappedType(type), context),
                SInferType(STypeParameterDeclaration(NodeList.None, SIdentifier(IdTextOf(S.TypeName(newTypeVariable))), (Types.Flags(originalConstraint) & TypeFlags.Unknown) != 0 ? Node.None : TypeToTypeNodeHelper(originalConstraint, context), Node.None)),
                result,
                SKeyword(SyntaxKind.NeverKeyword));
        }
        if (needsModifierPreservingWrapper)
        {
            // and step 3: once the mapped type is reconstructed, create a `ConstraintType extends infer T_1 extends keyof ModifiersType ? {[K in T_1]: Template} : never`
            return SConditionalType(
                TypeToTypeNodeHelper(constraintType, context),
                SInferType(STypeParameterDeclaration(NodeList.None, SIdentifier(IdTextOf(S.TypeName(newTypeVariable))), STypeOperator(SyntaxKind.KeyOfKeyword, TypeToTypeNodeHelper(GetModifiersTypeFromMappedType(type), context)), Node.None)),
                result,
                SKeyword(SyntaxKind.NeverKeyword));
        }
        return result;
    }

    // checker.ts createAnonymousTypeNode
    private Node CreateAnonymousTypeNode(Type type, NodeBuilderContext context, bool forceClassExpansion = false, bool forceExpansion = false)
    {
        var symbol = Types.Symbol(type);
        if (!symbol.IsNone)
        {
            var isInstantiationExpressionType = (Types.ObjectFlagsOf(type) & ObjectFlags.InstantiationExpressionType) != 0;
            if (isInstantiationExpressionType)
            {
                var existing = GetInstantiationExpressionNode(type);
                if (!existing.IsNone && Kind(existing) == SyntaxKind.TypeQuery && GetTypeFromTypeNodeInContext(context, existing) == type)
                {
                    var typeNode = TryReuseExistingTypeNodeChecked(context, existing);
                    if (!typeNode.IsNone)
                    {
                        return typeNode;
                    }
                }
                if (context.VisitedTypes is not null && context.VisitedTypes.Contains(type))
                {
                    return CreateElidedInformationPlaceholder(context);
                }
                return VisitAndTransformType(type, context, t => CreateTypeNodeFromObjectType(t, context));
            }
            var isInstanceType = IsClassInstanceSide(type) ? SymbolFlags.Type : SymbolFlags.Value;
            var symbolFlags = SymbolFlagsOf(symbol);
            var valueDeclaration = ValueDeclarationOf(symbol);
            if (IsJSConstructor(valueDeclaration))
            {
                // Instance and static types share the same symbol; only add 'typeof' for the static side.
                return SymbolToTypeNode(symbol, context, isInstanceType);
            }
            // Always use 'typeof T' for type of class, enum, and module objects
            if (!forceExpansion &&
                (((symbolFlags & SymbolFlags.Class) != 0
                        && !forceClassExpansion
                        && GetBaseTypeVariableOfClass(symbol).IsNone
                        && !(!valueDeclaration.IsNone
                            && Utilities.IsClassLike(StoreOf(valueDeclaration), valueDeclaration.Node)
                            && (context.Flags & NodeBuilderFlags.WriteClassExpressionAsTypeLiteral) != 0
                            && (Kind(valueDeclaration) != SyntaxKind.ClassDeclaration
                                || IsSymbolAccessible(symbol, context.EnclosingDeclaration, context.FakeScope, isInstanceType) != SymbolAccessibility.Accessible)))
                    || (symbolFlags & (SymbolFlags.Enum | SymbolFlags.ValueModule)) != 0
                    || ShouldWriteTypeOfFunctionSymbol()))
            {
                if (ShouldExpandType(type, context))
                {
                    context.Depth += 1;
                }
                else
                {
                    return SymbolToTypeNode(symbol, context, isInstanceType);
                }
            }
            if (context.VisitedTypes is not null && context.VisitedTypes.Contains(type))
            {
                // If type is an anonymous type literal in a type alias declaration, use type alias name
                var typeAlias = GetTypeAliasForTypeLiteral(type);
                if (!typeAlias.IsNone)
                {
                    // The specified symbol flags need to be reinterpreted as type flags
                    return SymbolToTypeNode(typeAlias, context, SymbolFlags.Type);
                }
                return CreateElidedInformationPlaceholder(context);
            }
            return VisitAndTransformType(type, context, t => CreateTypeNodeFromObjectType(t, context));
        }
        // Anonymous types without a symbol are never circular.
        return CreateTypeNodeFromObjectType(type, context);

        bool ShouldWriteTypeOfFunctionSymbol()
        {
            var flags = SymbolFlagsOf(symbol);
            var declarations = DeclarationsOf(symbol) ?? [];
            var isStaticMethodSymbol = (flags & SymbolFlags.Method) != 0 && // typeof static method
                declarations.Exists(d => Utilities.IsStatic(StoreOf(d), d.Node) && !IsLateBindableIndexSignature(GetNameOfDeclarationRef(d)));
            var isNonLocalFunctionSymbol = (flags & SymbolFlags.Function) != 0 &&
                (!ParentOf(symbol).IsNone || // is exported function symbol
                    declarations.Exists(d => Kind(Parent(d)) is SyntaxKind.SourceFile or SyntaxKind.ModuleBlock));
            if (isStaticMethodSymbol || isNonLocalFunctionSymbol)
            {
                // typeof is allowed only for static/non local functions
                return ((context.Flags & NodeBuilderFlags.UseTypeOfFunction) != 0 || (context.VisitedTypes is not null && context.VisitedTypes.Contains(type))) && // it is type of the symbol uses itself recursively
                    ((context.Flags & NodeBuilderFlags.UseStructuralFallback) == 0 || IsValueSymbolAccessible(symbol, context.EnclosingDeclaration, context.FakeScope)); // And the build is going to succeed without visibility error or there is no structural fallback allowed
            }
            return false;
        }
    }

    // checker.ts visitAndTransformType (without the serializedTypes cache)
    private Node VisitAndTransformType(Type type, NodeBuilderContext context, Func<Type, Node> transform)
    {
        var objectFlags = Types.ObjectFlagsOf(type);
        var symbol = Types.Symbol(type);
        var isConstructorObject = (objectFlags & ObjectFlags.Anonymous) != 0 && !symbol.IsNone && (SymbolFlagsOf(symbol) & SymbolFlags.Class) != 0;
        string? id = null;
        if ((objectFlags & ObjectFlags.Reference) != 0 && !Types.Object(type).Node.IsNone)
        {
            var node = Types.Object(type).Node;
            id = "N" + node.File + ":" + node.Node.Index;
        }
        else if ((Types.Flags(type) & TypeFlags.Conditional) != 0)
        {
            var node = GetConditionalRootNode(type);
            id = "N" + node.File + ":" + node.Node.Index;
        }
        else if (!symbol.IsNone)
        {
            id = (isConstructorObject ? "+" : "") + symbol.File + ":" + symbol.Index;
        }
        // Since instantiations of the same anonymous type have the same symbol, tracking symbols instead
        // of types allows us to catch circular references to instantiations of the same anonymous type
        context.VisitedTypes ??= [];
        if (id is not null)
        {
            context.SymbolDepth ??= [];
        }
        // Don't rely on type cache if we're expand a type, because we need to compute `canIncreaseExpansionDepth`.
        // (NodeLinks.serializedTypes of the enclosing declaration; the cached nodes live in the builder's store, which is
        // reset after each outermost print, so here the cache lasts for one print.)
        var useCache = context.MaxExpansionDepth < 0 && !context.EnclosingDeclaration.IsNone;
        var cacheKey = useCache ? (context.EnclosingDeclaration, type, context.Flags, context.InternalFlags) : default;
        if (useCache && serializedTypes.TryGetValue(cacheKey, out var cached))
        {
            if (cached.Truncating)
            {
                context.Truncating = true;
            }
            context.ApproximateLength += cached.AddedLength;
            return CloneSerializedTypeNode(cached.Node);
        }
        var depth = 0;
        if (id is not null)
        {
            depth = context.SymbolDepth!.GetValueOrDefault(id);
            if (depth > 10)
            {
                return CreateElidedInformationPlaceholder(context);
            }
            context.SymbolDepth![id] = depth + 1;
        }
        context.VisitedTypes.Add(type);
        var startLength = context.ApproximateLength;
        var result = transform(type);
        if (useCache && !context.EncounteredError)
        {
            serializedTypes[cacheKey] = (result, context.Truncating, context.ApproximateLength - startLength);
        }
        context.VisitedTypes.Remove(type);
        if (id is not null)
        {
            context.SymbolDepth![id] = depth;
        }
        return result;
    }

    // checker.ts createTypeNodeFromObjectType
    private Node CreateTypeNodeFromObjectType(Type type, NodeBuilderContext context)
    {
        if (IsGenericMappedType(type) || MappedTypeContainsError(type))
        {
            return CreateMappedTypeNodeFromType(type, context);
        }
        var resolved = ResolveStructuredTypeMembers(type);
        var callSignatures = resolved.CallSignatures ?? [];
        var constructSignatures = resolved.ConstructSignatures ?? [];
        var properties = resolved.Properties ?? [];
        var indexInfos = resolved.IndexInfos ?? [];
        if (properties.Count == 0 && indexInfos.Count == 0)
        {
            if (callSignatures.Count == 0 && constructSignatures.Count == 0)
            {
                context.ApproximateLength += 2;
                return SetEmitFlags(STypeLiteral(null), EmitFlags.SingleLine);
            }
            if (callSignatures.Count == 1 && constructSignatures.Count == 0)
            {
                return SignatureToSignatureDeclarationHelper(callSignatures[0], SyntaxKind.FunctionType, context);
            }
            if (constructSignatures.Count == 1 && callSignatures.Count == 0)
            {
                return SignatureToSignatureDeclarationHelper(constructSignatures[0], SyntaxKind.ConstructorType, context);
            }
        }
        var abstractSignatures = constructSignatures.FindAll(s => (Signatures.Record(s).Flags & SignatureFlags.Abstract) != 0);
        if (abstractSignatures.Count > 0)
        {
            var types = abstractSignatures.ConvertAll(GetOrCreateTypeFromSignature);
            // count the number of type elements excluding abstract constructors
            var typeElementCount = callSignatures.Count +
                (constructSignatures.Count - abstractSignatures.Count) +
                indexInfos.Count +
                // exclude `prototype` when writing a class expression as a type literal, as per
                // the logic in `createTypeNodesFromResolvedType`.
                ((context.Flags & NodeBuilderFlags.WriteClassExpressionAsTypeLiteral) != 0 ?
                    properties.FindAll(p => (SymbolFlagsOf(p) & SymbolFlags.Prototype) == 0).Count :
                    properties.Count);
            // don't include an empty object literal if there were no other static-side
            // properties to write, i.e. `abstract class C { }` becomes `abstract new () => {}`
            // and not `(abstract new () => {}) & {}`
            if (typeElementCount != 0)
            {
                // create a copy of the object type without any abstract construct signatures.
                types.Add(GetResolvedTypeWithoutAbstractConstructSignatures(type, resolved));
            }
            return TypeToTypeNodeHelper(GetIntersectionType(CollectionsMarshal.AsSpan(types)), context);
        }

        var saved = SaveRestoreFlags(context);
        context.Flags |= NodeBuilderFlags.InObjectTypeLiteral;
        var members = CreateTypeNodesFromResolvedType(type, resolved, context);
        saved.Restore(context);
        var typeLiteralNode = STypeLiteral(members);
        context.ApproximateLength += 2;
        SetEmitFlags(typeLiteralNode, (context.Flags & NodeBuilderFlags.MultilineObjectLiterals) != 0 ? 0 : EmitFlags.SingleLine);
        return typeLiteralNode;
    }

    // checker.ts typeReferenceToTypeNode
    private Node TypeReferenceToTypeNode(Type type, NodeBuilderContext context)
    {
        var typeArguments = GetTypeArguments(type).ToArray();
        var target = Types.Object(type).Target;
        if (target == globalArrayType || target == globalReadonlyArrayType)
        {
            if ((context.Flags & NodeBuilderFlags.WriteArrayAsGenericType) != 0)
            {
                var typeArgumentNode = TypeToTypeNodeHelper(typeArguments[0], context);
                return STypeReference(SIdentifier(target == globalArrayType ? "Array" : "ReadonlyArray"), SList([typeArgumentNode]));
            }
            var elementType = TypeToTypeNodeHelper(typeArguments[0], context);
            var arrayType = SArrayType(elementType);
            return target == globalArrayType ? arrayType : STypeOperator(SyntaxKind.ReadonlyKeyword, arrayType);
        }
        if ((Types.ObjectFlagsOf(target) & ObjectFlags.Tuple) != 0)
        {
            var tuple = GetTupleTargetInfo(target);
            for (var i = 0; i < typeArguments.Length && i < tuple.ElementFlags.Length; i++)
            {
                typeArguments[i] = RemoveMissingType(typeArguments[i], (tuple.ElementFlags[i] & ElementFlags.Optional) != 0);
            }
            if (typeArguments.Length > 0)
            {
                var arity = GetTypeReferenceArity(type);
                var tupleConstituentNodes = MapToTypeNodes(typeArguments[..arity], context);
                if (tupleConstituentNodes is not null)
                {
                    for (var i = 0; i < tupleConstituentNodes.Count; i++)
                    {
                        var flags = tuple.ElementFlags[i];
                        var labeledElementDeclaration = tuple.LabeledElementDeclarations is not null && i < tuple.LabeledElementDeclarations.Length ? tuple.LabeledElementDeclarations[i] : NodeRef.None;
                        if (!labeledElementDeclaration.IsNone)
                        {
                            tupleConstituentNodes[i] = SNamedTupleMember(
                                (flags & ElementFlags.Variable) != 0 ? SToken(SyntaxKind.DotDotDotToken) : Node.None,
                                SIdentifier(Utilities.UnescapeLeadingUnderscores(GetTupleElementLabel(labeledElementDeclaration))),
                                (flags & ElementFlags.Optional) != 0 ? SToken(SyntaxKind.QuestionToken) : Node.None,
                                (flags & ElementFlags.Rest) != 0 ? SArrayType(tupleConstituentNodes[i]) : tupleConstituentNodes[i]);
                        }
                        else
                        {
                            tupleConstituentNodes[i] = (flags & ElementFlags.Variable) != 0 ? SRestType((flags & ElementFlags.Rest) != 0 ? SArrayType(tupleConstituentNodes[i]) : tupleConstituentNodes[i]) :
                                (flags & ElementFlags.Optional) != 0 ? SOptionalType(tupleConstituentNodes[i]) :
                                tupleConstituentNodes[i];
                        }
                    }
                    var tupleTypeNode = SetEmitFlags(STupleType(tupleConstituentNodes), EmitFlags.SingleLine);
                    return tuple.Readonly ? STypeOperator(SyntaxKind.ReadonlyKeyword, tupleTypeNode) : tupleTypeNode;
                }
            }
            if (context.EncounteredError || (context.Flags & NodeBuilderFlags.AllowEmptyTuple) != 0)
            {
                var tupleTypeNode = SetEmitFlags(STupleType([]), EmitFlags.SingleLine);
                return tuple.Readonly ? STypeOperator(SyntaxKind.ReadonlyKeyword, tupleTypeNode) : tupleTypeNode;
            }
            context.EncounteredError = true;
            return Node.None;
        }
        var symbol = Types.Symbol(type);
        var valueDeclaration = symbol.IsNone ? NodeRef.None : ValueDeclarationOf(symbol);
        if ((context.Flags & NodeBuilderFlags.WriteClassExpressionAsTypeLiteral) != 0 &&
            !valueDeclaration.IsNone &&
            Utilities.IsClassLike(StoreOf(valueDeclaration), valueDeclaration.Node) &&
            !IsValueSymbolAccessible(symbol, context.EnclosingDeclaration, context.FakeScope))
        {
            return CreateAnonymousTypeNode(type, context);
        }
        var targetData = Types.Object(target);
        var outerTypeParameters = Types.Elements(targetData.OuterTypeParameters).ToArray();
        var index = 0;
        var resultType = Node.None;
        if (outerTypeParameters.Length > 0)
        {
            var length = outerTypeParameters.Length;
            while (index < length)
            {
                // Find group of type arguments for type parameters with the same declaring container.
                var start = index;
                var parent = GetParentSymbolOfTypeParameter(outerTypeParameters[index]);
                do
                {
                    index++;
                }
                while (index < length && GetParentSymbolOfTypeParameter(outerTypeParameters[index]) == parent);
                // When type parameters are their own type arguments for the whole group (i.e. we have
                // the default outer type arguments), we don't show the group.
                if (!RangeEquals(outerTypeParameters, typeArguments, start, index))
                {
                    var typeArgumentSlice = MapToTypeNodes(typeArguments[start..index], context);
                    var savedFlags = SaveRestoreFlags(context);
                    context.Flags |= NodeBuilderFlags.ForbidIndexedAccessSymbolReferences;
                    var reference = SymbolToTypeNode(parent, context, SymbolFlags.Type, typeArgumentSlice);
                    savedFlags.Restore(context);
                    resultType = resultType.IsNone ? reference : AppendReferenceToType(resultType, reference);
                }
            }
        }
        List<Node>? typeArgumentNodes = null;
        if (typeArguments.Length > 0)
        {
            var typeParameterCount = 0;
            if (targetData.HasTypeParameters)
            {
                var typeParameters = Types.Elements(targetData.TypeParameters).ToArray();
                typeParameterCount = Math.Min(typeParameters.Length, typeArguments.Length);

                // Maybe we should do this for more types, but for now we only elide type arguments that are
                // identical to their associated type parameters' defaults for `Iterable`, `IterableIterator`,
                // `AsyncIterable`, and `AsyncIterableIterator` to provide backwards-compatible .d.ts emit due
                // to each now having three type parameters instead of only one.
                if (IsReferenceToGlobalIterableLikeType(type))
                {
                    var node = Types.Object(type).Node;
                    if (node.IsNone || Kind(node) != SyntaxKind.TypeReference || StoreOf(node).TypeArguments(node.Node).IsNone ||
                        StoreOf(node).Length(StoreOf(node).TypeArguments(node.Node)) < typeParameterCount)
                    {
                        while (typeParameterCount > 0)
                        {
                            var typeArgument = typeArguments[typeParameterCount - 1];
                            var typeParameter = typeParameters[typeParameterCount - 1];
                            var defaultType = GetDefaultFromTypeParameter(typeParameter);
                            if (defaultType.IsNone || !IsTypeIdenticalTo(typeArgument, defaultType))
                            {
                                break;
                            }
                            typeParameterCount--;
                        }
                    }
                }
            }
            typeArgumentNodes = index < typeParameterCount ? MapToTypeNodes(typeArguments[index..typeParameterCount], context) : null;
        }
        var saved = SaveRestoreFlags(context);
        context.Flags |= NodeBuilderFlags.ForbidIndexedAccessSymbolReferences;
        var finalRef = SymbolToTypeNode(symbol, context, SymbolFlags.Type, typeArgumentNodes);
        saved.Restore(context);
        return resultType.IsNone ? finalRef : AppendReferenceToType(resultType, finalRef);
    }

    private static bool RangeEquals(Type[] array1, Type[] array2, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (i >= array2.Length || array1[i] != array2[i])
            {
                return false;
            }
        }
        return true;
    }

    // checker.ts appendReferenceToType
    private Node AppendReferenceToType(Node root, Node reference)
    {
        if (S.Kind(root) == SyntaxKind.ImportType)
        {
            // first shift type arguments
            var typeArguments = S.TypeArguments(root);
            var qualifier = S.Qualifier(root);
            if (!qualifier.IsNone)
            {
                if (S.Kind(qualifier) == SyntaxKind.Identifier)
                {
                    if (typeArguments != GetIdentifierTypeArguments(qualifier))
                    {
                        qualifier = CloneSynthIdentifier(qualifier);
                        SetIdentifierTypeArguments(qualifier, typeArguments);
                    }
                }
                else
                {
                    var right = S.Right(qualifier);
                    if (typeArguments != GetIdentifierTypeArguments(right))
                    {
                        var newRight = CloneSynthIdentifier(right);
                        SetIdentifierTypeArguments(newRight, typeArguments);
                        qualifier = SQualifiedName(S.Left(qualifier), newRight);
                    }
                }
            }
            typeArguments = S.TypeArguments(reference);
            // then move qualifiers
            foreach (var id in GetAccessStack(reference))
            {
                qualifier = !qualifier.IsNone ? SQualifiedName(qualifier, id) : id;
            }
            return SImportType(S.Argument(root), S.Attributes(root), qualifier, typeArguments, S.IsTypeOf(root));
        }
        else
        {
            // first shift type arguments
            var typeArguments = S.TypeArguments(root);
            var typeName = S.TypeName(root);
            if (S.Kind(typeName) == SyntaxKind.Identifier)
            {
                if (typeArguments != GetIdentifierTypeArguments(typeName))
                {
                    typeName = CloneSynthIdentifier(typeName);
                    SetIdentifierTypeArguments(typeName, typeArguments);
                }
            }
            else
            {
                var right = S.Right(typeName);
                if (typeArguments != GetIdentifierTypeArguments(right))
                {
                    var newRight = CloneSynthIdentifier(right);
                    SetIdentifierTypeArguments(newRight, typeArguments);
                    typeName = SQualifiedName(S.Left(typeName), newRight);
                }
            }
            typeArguments = S.TypeArguments(reference);
            // then move qualifiers
            foreach (var id in GetAccessStack(reference))
            {
                typeName = SQualifiedName(typeName, id);
            }
            return STypeReference(typeName, typeArguments);
        }
    }

    private Node CloneSynthIdentifier(Node identifier)
    {
        var clone = SIdentifier(IdTextOf(identifier));
        var emitNode = S.EmitNode(identifier) as EmitNode;
        if (emitNode is not null)
        {
            SEmitNode(clone).Flags = emitNode.Flags;
            SEmitNode(clone).IdentifierTypeArguments = emitNode.IdentifierTypeArguments;
        }
        if (reusedNodes.Contains(identifier))
        {
            reusedNodes.Add(clone);
        }
        return clone;
    }

    private List<Node> GetAccessStack(Node reference)
    {
        var state = S.TypeName(reference);
        var ids = new List<Node>();
        while (S.Kind(state) != SyntaxKind.Identifier)
        {
            ids.Insert(0, S.Right(state));
            state = S.Left(state);
        }
        ids.Insert(0, state);
        return ids;
    }

    // checker.ts indexInfoToObjectComputedNamesOrSignatureDeclaration (index infos with components: C3)
    private List<Node> IndexInfoToObjectComputedNamesOrSignatureDeclaration(IndexInfo indexInfo, NodeBuilderContext context, Node typeNode) =>
        [IndexInfoToIndexSignatureDeclarationHelper(indexInfo, context, typeNode)];

    // checker.ts indexInfoToIndexSignatureDeclarationHelper
    private Node IndexInfoToIndexSignatureDeclarationHelper(IndexInfo indexInfo, NodeBuilderContext context, Node typeNode)
    {
        var name = GetNameFromIndexInfo(indexInfo) ?? "x";
        var indexerTypeNode = TypeToTypeNodeHelper(indexInfo.KeyType, context);
        var indexingParameter = SParameterDeclaration(NodeList.None, Node.None, SIdentifier(name), Node.None, indexerTypeNode);
        if (typeNode.IsNone)
        {
            typeNode = TypeToTypeNodeHelper(!indexInfo.Type.IsNone ? indexInfo.Type : anyType, context);
        }
        if (indexInfo.Type.IsNone && (context.Flags & NodeBuilderFlags.AllowEmptyIndexInfoType) == 0)
        {
            context.EncounteredError = true;
        }
        context.ApproximateLength += name.Length + 4;
        return SSignature(SyntaxKind.IndexSignature, indexInfo.IsReadonly ? SList([SToken(SyntaxKind.ReadonlyKeyword)]) : NodeList.None, Node.None, Node.None, NodeList.None, SList([indexingParameter]), typeNode);
    }

    // utilities.ts getNameFromIndexInfo
    private string? GetNameFromIndexInfo(IndexInfo info)
    {
        if (info.Declaration.IsNone)
        {
            return null;
        }
        var store = StoreOf(info.Declaration);
        var parameters = store.Parameters(info.Declaration.Node);
        if (parameters.IsNone || store.Length(parameters) == 0)
        {
            return null;
        }
        var name = store.Name(store.Element(parameters, 0));
        return store.Kind(name) == SyntaxKind.Identifier ? Utilities.DeclarationNameToString(store, name) : null;
    }

    // checker.ts createTypeNodesFromResolvedType
    private List<Node>? CreateTypeNodesFromResolvedType(Type type, ObjectTypeData resolvedType, NodeBuilderContext context)
    {
        if (CheckTruncationLength(context))
        {
            context.Truncated = true;
            if ((context.Flags & NodeBuilderFlags.NoTruncation) != 0)
            {
                // a NotEmittedTypeElement with the synthetic comment /*elided*/ (not printed with removeComments)
                return [SNew(SyntaxKind.NotEmittedTypeElement)];
            }
            return [SPropertySignature(NodeList.None, SIdentifier("..."), Node.None, Node.None)];
        }
        context.TypeStack.Add(-1);
        var typeElements = new List<Node>();
        foreach (var signature in resolvedType.CallSignatures ?? [])
        {
            typeElements.Add(SignatureToSignatureDeclarationHelper(signature, SyntaxKind.CallSignature, context));
        }
        foreach (var signature in resolvedType.ConstructSignatures ?? [])
        {
            if ((Signatures.Record(signature).Flags & SignatureFlags.Abstract) != 0)
            {
                continue;
            }
            typeElements.Add(SignatureToSignatureDeclarationHelper(signature, SyntaxKind.ConstructSignature, context));
        }
        foreach (var info in resolvedType.IndexInfos ?? [])
        {
            typeElements.AddRange(IndexInfoToObjectComputedNamesOrSignatureDeclaration(info, context, (Types.ObjectFlagsOf(type) & ObjectFlags.ReverseMapped) != 0 ? CreateElidedInformationPlaceholder(context) : Node.None));
        }

        var properties = resolvedType.Properties;
        if (properties is null)
        {
            context.TypeStack.RemoveAt(context.TypeStack.Count - 1);
            return typeElements;
        }

        var i = 0;
        foreach (var propertySymbol in properties)
        {
            if (IsExpanding(context) && (SymbolFlagsOf(propertySymbol) & SymbolFlags.Prototype) != 0)
            {
                continue;
            }
            i++;
            if ((context.Flags & NodeBuilderFlags.WriteClassExpressionAsTypeLiteral) != 0)
            {
                if ((SymbolFlagsOf(propertySymbol) & SymbolFlags.Prototype) != 0)
                {
                    continue;
                }
            }
            if (CheckTruncationLength(context) && (i + 2 < properties.Count - 1))
            {
                context.Truncated = true;
                if ((context.Flags & NodeBuilderFlags.NoTruncation) != 0)
                {
                    // the last element gets the synthetic comment `... N more elided ...` (not printed with removeComments)
                }
                else
                {
                    typeElements.Add(SPropertySignature(NodeList.None, SIdentifier($"... {properties.Count - i} more ..."), Node.None, Node.None));
                }
                AddPropertyToElementList(properties[^1], context, typeElements);
                break;
            }
            AddPropertyToElementList(propertySymbol, context, typeElements);
        }
        context.TypeStack.RemoveAt(context.TypeStack.Count - 1);
        return typeElements.Count > 0 ? typeElements : null;
    }

    private Node CreateElidedInformationPlaceholder(NodeBuilderContext context)
    {
        context.ApproximateLength += 3;
        if ((context.Flags & NodeBuilderFlags.NoTruncation) == 0)
        {
            return STypeReference(SIdentifier("..."), NodeList.None);
        }
        // `any` with the synthetic comment /*elided*/ (not printed with removeComments)
        return SKeyword(SyntaxKind.AnyKeyword);
    }

    // checker.ts shouldUsePlaceholderForProperty
    private bool ShouldUsePlaceholderForProperty(SymbolRef propertySymbol, NodeBuilderContext context)
    {
        // Use placeholders for reverse mapped types we've either
        // (1) already descended into, or
        // (2) are nested reverse mappings within a mapping over a non-anonymous type, or
        // (3) are deeply nested properties that originate from the same mapped type.
        const int depth = 3;
        if ((GetCheckFlags(propertySymbol) & CheckFlags.ReverseMapped) == 0)
        {
            return false;
        }
        var stack = context.ReverseMappedStack;
        if (stack is not null && stack.Contains(propertySymbol))
        {
            return true;
        }
        if (stack is { Count: > 0 } && (Types.ObjectFlagsOf(GetReverseMappedPropertyType(stack[^1])) & ObjectFlags.Anonymous) == 0)
        {
            return true;
        }
        if ((stack?.Count ?? 0) < depth)
        {
            return false;
        }
        for (var i = 0; i < depth; i++)
        {
            var prop = stack![stack.Count - 1 - i];
            if (Types.Symbol(GetReverseMappedMappedType(prop)) != Types.Symbol(GetReverseMappedMappedType(propertySymbol)))
            {
                return false;
            }
        }
        return true;
    }

    // checker.ts addPropertyToElementList
    private void AddPropertyToElementList(SymbolRef propertySymbol, NodeBuilderContext context, List<Node> typeElements)
    {
        var propertyIsReverseMapped = (GetCheckFlags(propertySymbol) & CheckFlags.ReverseMapped) != 0;
        var propertyType = ShouldUsePlaceholderForProperty(propertySymbol, context) ? anyType : GetNonMissingTypeOfSymbol(propertySymbol);
        var saveEnclosingDeclaration = context.EnclosingDeclaration;
        var saveFakeScope = context.FakeScope;
        context.EnclosingDeclaration = NodeRef.None;
        context.FakeScope = null;
        var valueDeclaration = ValueDeclarationOf(propertySymbol);
        var declarations = DeclarationsOf(propertySymbol);
        if (!valueDeclaration.IsNone)
        {
            context.EnclosingDeclaration = valueDeclaration;
        }
        else if (declarations is { Count: > 0 })
        {
            context.EnclosingDeclaration = declarations[0];
        }
        else
        {
            context.EnclosingDeclaration = saveEnclosingDeclaration;
            context.FakeScope = saveFakeScope;
        }
        var propertyName = GetPropertyNameNodeForSymbol(propertySymbol, context);
        context.EnclosingDeclaration = saveEnclosingDeclaration;
        context.FakeScope = saveFakeScope;
        context.ApproximateLength += SymbolName(propertySymbol).Length + 1;

        if ((SymbolFlagsOf(propertySymbol) & SymbolFlags.Accessor) != 0)
        {
            var writeType = GetWriteTypeOfSymbol(propertySymbol);
            if (!IsErrorType(propertyType) && !IsErrorType(writeType))
            {
                var propDeclaration = GetDeclarationOfKind(propertySymbol, SyntaxKind.PropertyDeclaration);
                if (propertyType != writeType || ((SymbolFlagsOf(ParentOf(propertySymbol)) & SymbolFlags.Class) != 0 && propDeclaration.IsNone))
                {
                    var getterDeclaration = GetDeclarationOfKind(propertySymbol, SyntaxKind.GetAccessor);
                    if (!getterDeclaration.IsNone)
                    {
                        var getterSignature = GetSignatureFromDeclaration(getterDeclaration);
                        typeElements.Add(SignatureToSignatureDeclarationHelper(InstantiateSignatureForSymbol(getterSignature, propertySymbol), SyntaxKind.GetAccessor, context, name: propertyName));
                    }
                    var setterDeclaration = GetDeclarationOfKind(propertySymbol, SyntaxKind.SetAccessor);
                    if (!setterDeclaration.IsNone)
                    {
                        var setterSignature = GetSignatureFromDeclaration(setterDeclaration);
                        typeElements.Add(SignatureToSignatureDeclarationHelper(InstantiateSignatureForSymbol(setterSignature, propertySymbol), SyntaxKind.SetAccessor, context, name: propertyName));
                    }
                    return;
                }
                if ((SymbolFlagsOf(ParentOf(propertySymbol)) & SymbolFlags.Class) != 0 && !propDeclaration.IsNone && HasAccessorModifier(propDeclaration))
                {
                    var fakeGetterSignature = CreateSignatureForBuilder(NodeRef.None, [], propertyType);
                    typeElements.Add(SignatureToSignatureDeclarationHelper(fakeGetterSignature, SyntaxKind.GetAccessor, context, name: propertyName));
                    var setterParam = CreateSymbol(SymbolFlags.FunctionScopedVariable, "arg");
                    ValueSymbolLinks.Get(setterParam).ResolvedType = writeType;
                    var fakeSetterSignature = CreateSignatureForBuilder(NodeRef.None, [setterParam], voidType);
                    typeElements.Add(SignatureToSignatureDeclarationHelper(fakeSetterSignature, SyntaxKind.SetAccessor, context, name: propertyName));
                    return;
                }
            }
        }

        var optionalToken = (SymbolFlagsOf(propertySymbol) & SymbolFlags.Optional) != 0 ? SToken(SyntaxKind.QuestionToken) : Node.None;
        if ((SymbolFlagsOf(propertySymbol) & (SymbolFlags.Function | SymbolFlags.Method)) != 0 && GetPropertiesOfObjectType(propertyType).Count == 0 && !IsReadonlySymbol(propertySymbol))
        {
            var signatures = GetSignaturesOfType(FilterType(propertyType, t => (Types.Flags(t) & TypeFlags.Undefined) == 0), SignatureKind.Call);
            foreach (var signature in signatures)
            {
                typeElements.Add(SignatureToSignatureDeclarationHelper(signature, SyntaxKind.MethodSignature, context, name: propertyName, questionToken: optionalToken));
            }
            if (signatures.Count > 0 || optionalToken.IsNone)
            {
                return;
            }
        }
        Node propertyTypeNode;
        if (ShouldUsePlaceholderForProperty(propertySymbol, context))
        {
            propertyTypeNode = CreateElidedInformationPlaceholder(context);
        }
        else
        {
            if (propertyIsReverseMapped)
            {
                (context.ReverseMappedStack ??= []).Add(propertySymbol);
            }
            propertyTypeNode = !propertyType.IsNone ? SerializeTypeForDeclaration(context, NodeRef.None, propertyType, propertySymbol) : SKeyword(SyntaxKind.AnyKeyword);
            if (propertyIsReverseMapped)
            {
                context.ReverseMappedStack!.RemoveAt(context.ReverseMappedStack.Count - 1);
            }
        }

        var modifiers = IsReadonlySymbol(propertySymbol) ? SList([SToken(SyntaxKind.ReadonlyKeyword)]) : NodeList.None;
        if (!modifiers.IsNone)
        {
            context.ApproximateLength += 9;
        }
        typeElements.Add(SPropertySignature(modifiers, propertyName, optionalToken, propertyTypeNode));
    }

    private bool HasAccessorModifier(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        foreach (var modifier in store.Nodes(store.Modifiers(declaration.Node)))
        {
            if (store.Kind(modifier) == SyntaxKind.AccessorKeyword)
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts mapToTypeNodes
    private List<Node>? MapToTypeNodes(Type[]? types, NodeBuilderContext context, bool isBareList = false)
    {
        if (types is not { Length: > 0 })
        {
            return null;
        }
        if (CheckTruncationLength(context))
        {
            context.Truncated = true;
            if (!isBareList)
            {
                return [(context.Flags & NodeBuilderFlags.NoTruncation) != 0 ? SKeyword(SyntaxKind.AnyKeyword) : STypeReference("...")];
            }
            if (types.Length > 2)
            {
                return
                [
                    TypeToTypeNodeHelper(types[0], context),
                    (context.Flags & NodeBuilderFlags.NoTruncation) != 0 ? SKeyword(SyntaxKind.AnyKeyword) : STypeReference($"... {types.Length - 2} more ..."),
                    TypeToTypeNodeHelper(types[^1], context),
                ];
            }
        }
        var mayHaveNameCollisions = (context.Flags & NodeBuilderFlags.UseFullyQualifiedType) == 0;
        // Map from type reference identifier text to [type, index in `result` where the type node is]
        var seenNames = mayHaveNameCollisions ? new Dictionary<string, List<(Type Type, int Index)>>(StringComparer.Ordinal) : null;
        var seenOrder = mayHaveNameCollisions ? new List<string>() : null;
        var result = new List<Node>();
        var i = 0;
        foreach (var type in types)
        {
            i++;
            if (CheckTruncationLength(context) && (i + 2 < types.Length - 1))
            {
                context.Truncated = true;
                result.Add((context.Flags & NodeBuilderFlags.NoTruncation) != 0 ? SKeyword(SyntaxKind.AnyKeyword) : STypeReference($"... {types.Length - i} more ..."));
                var last = TypeToTypeNodeHelper(types[^1], context);
                if (!last.IsNone)
                {
                    result.Add(last);
                }
                break;
            }
            context.ApproximateLength += 2; // Account for whitespace + separator
            var typeNode = TypeToTypeNodeHelper(type, context);
            if (!typeNode.IsNone)
            {
                result.Add(typeNode);
                if (seenNames is not null && S.Kind(typeNode) == SyntaxKind.TypeReference && S.Kind(S.TypeName(typeNode)) == SyntaxKind.Identifier)
                {
                    var key = S.Texts.GetString(S.EscapedText(S.TypeName(typeNode))) ?? "";
                    if (!seenNames.TryGetValue(key, out var list))
                    {
                        seenNames[key] = list = [];
                        seenOrder!.Add(key);
                    }
                    list.Add((type, result.Count - 1));
                }
            }
        }
        if (seenNames is not null)
        {
            // To avoid printing types like `[Foo, Foo]` or `Bar & Bar` where
            // occurrences of the same name actually come from different
            // namespaces, go through the single-identifier type reference nodes
            // we just generated, and see if any names were generated more than
            // once while referring to different types. If so, regenerate the
            // type node for each entry by that name with the
            // `UseFullyQualifiedType` flag enabled.
            var saved = SaveRestoreFlags(context);
            context.Flags |= NodeBuilderFlags.UseFullyQualifiedType;
            foreach (var key in seenOrder!)
            {
                var entries = seenNames[key];
                var homogeneous = true;
                for (var j = 1; j < entries.Count; j++)
                {
                    if (!TypesAreSameReference(entries[0].Type, entries[j].Type))
                    {
                        homogeneous = false;
                        break;
                    }
                }
                if (!homogeneous)
                {
                    foreach (var (entryType, resultIndex) in entries)
                    {
                        result[resultIndex] = TypeToTypeNodeHelper(entryType, context);
                    }
                }
            }
            saved.Restore(context);
        }
        return result;
    }

    private bool TypesAreSameReference(Type a, Type b) =>
        a == b
        || (!Types.Symbol(a).IsNone && Types.Symbol(a) == Types.Symbol(b))
        || (!Types.Record(a).AliasSymbol.IsNone && Types.Record(a).AliasSymbol == Types.Record(b).AliasSymbol);

    // checker.ts formatUnionTypes
    private List<Type> FormatUnionTypes(Type[] types, bool expandingEnum)
    {
        var result = new List<Type>();
        TypeFlags flags = 0;
        for (var i = 0; i < types.Length; i++)
        {
            var t = types[i];
            flags |= Types.Flags(t);
            if ((Types.Flags(t) & TypeFlags.Nullable) == 0)
            {
                if ((Types.Flags(t) & TypeFlags.BooleanLiteral) != 0 || (!expandingEnum && (Types.Flags(t) | TypeFlags.EnumLike) != 0))
                {
                    var baseType = (Types.Flags(t) & TypeFlags.BooleanLiteral) != 0 ? booleanType : GetBaseTypeOfEnumLikeType(t);
                    if ((Types.Flags(baseType) & TypeFlags.Union) != 0)
                    {
                        var baseTypes = Types.Elements(Types.Union(baseType).Types);
                        var count = baseTypes.Length;
                        if (i + count <= types.Length && GetRegularTypeOfLiteralType(types[i + count - 1]) == GetRegularTypeOfLiteralType(baseTypes[count - 1]))
                        {
                            result.Add(baseType);
                            i += count - 1;
                            continue;
                        }
                    }
                }
                result.Add(t);
            }
        }
        if ((flags & TypeFlags.Null) != 0)
        {
            result.Add(nullType);
        }
        if ((flags & TypeFlags.Undefined) != 0)
        {
            result.Add(undefinedType);
        }
        return result;
    }

    // checker.ts getTypeAliasForTypeLiteral
    private SymbolRef GetTypeAliasForTypeLiteral(Type type)
    {
        var symbol = Types.Symbol(type);
        var declarations = symbol.IsNone ? null : DeclarationsOf(symbol);
        if (!symbol.IsNone && (SymbolFlagsOf(symbol) & SymbolFlags.TypeLiteral) != 0 && declarations is { Count: > 0 })
        {
            var node = Parent(declarations[0]);
            while (!node.IsNone && Kind(node) == SyntaxKind.ParenthesizedType)
            {
                node = Parent(node);
            }
            if (!node.IsNone && Kind(node) == SyntaxKind.TypeAliasDeclaration)
            {
                return GetSymbolOfDeclaration(node);
            }
        }
        return SymbolRef.None;
    }

    // checker.ts isClassInstanceSide
    private bool IsClassInstanceSide(Type type)
    {
        var symbol = Types.Symbol(type);
        return !symbol.IsNone && (SymbolFlagsOf(symbol) & SymbolFlags.Class) != 0 &&
            (type == GetDeclaredTypeOfSymbol(symbol) || ((Types.Flags(type) & TypeFlags.Object) != 0 && (Types.ObjectFlagsOf(type) & ObjectFlags.IsClassInstanceClone) != 0));
    }
}
