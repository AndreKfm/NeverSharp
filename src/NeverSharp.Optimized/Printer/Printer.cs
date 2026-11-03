// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/emitter.ts (createPrinter: public API, pipeline, emit dispatch, lists, writers, line
// terminators, text of nodes)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/printer/printer.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; reduced to a printer without handlers, source maps,
// preserveSourceNewlines, emit helpers, snippets and name generation (see the class comment). Module D0 adds comment
// emission (Printer.Comments.cs).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// emitter.ts: createPrinter. The variables of its closure are the fields of the instance, its inner functions the
/// instance methods. Nodes are read through the node store they live in, which each print call passes in.
/// <para>
/// Supported: <c>createPrinter({ removeComments, onlyPrintJsDocStyle, ... }, {})</c> with printNode, printList and
/// printFile (and the write* variants taking a writer), comments included (module D0). Left out, because a printer
/// without handlers never reaches them for parsed trees, and the type printer (node builder output) does not need them:
/// print handlers (so no substitution, and therefore the parenthesizer rules the printer passes along are never applied,
/// as in TypeScript), source maps, preserveSourceNewlines, omitTrailingSemicolon, bundles, emit helpers, snippet
/// elements, and generated names (auto-generated identifiers fail with an exception).
/// </para>
/// </summary>
internal sealed partial class Printer
{
    private readonly PrinterOptions printerOptions;
    private readonly string newLine;

    private NodeStore store = null!;
    private Node currentSourceFile;
    private IReadOnlyList<int>? currentLineMap;
    private int? nextListElementPos; // See comment in `getLeadingLineTerminatorCount`.

    private EmitTextWriter writer = null!;
    private EmitTextWriter? ownWriter; // Reusable `EmitTextWriter` for basic printing.

    private Printer(PrinterOptions printerOptions)
    {
        this.printerOptions = printerOptions;
        commentsDisabled = printerOptions.RemoveComments;
        newLine = EmitTextWriter.GetNewLineCharacter(printerOptions);
    }

    /// <summary>createPrinter(printerOptions) (no print handlers).</summary>
    internal static Printer CreatePrinter(PrinterOptions? printerOptions = null)
    {
        return new Printer(printerOptions ?? new PrinterOptions());
    }

    internal string PrintNode(EmitHint hint, NodeStore nodeStore, Node node, Node sourceFile)
    {
        switch (hint)
        {
            case EmitHint.SourceFile:
                Debug.Assert(nodeStore.Kind(node) == SyntaxKind.SourceFile, "Expected a SourceFile node.");
                break;
            case EmitHint.IdentifierName:
                Debug.Assert(nodeStore.Kind(node) == SyntaxKind.Identifier, "Expected an Identifier node.");
                break;
            case EmitHint.Expression:
                Debug.Assert(UtilitiesPublic.IsExpression(nodeStore, node), "Expected an Expression node.");
                break;
        }
        switch (nodeStore.Kind(node))
        {
            case SyntaxKind.SourceFile:
                return PrintFile(nodeStore, node);
            case SyntaxKind.Bundle:
                throw new NotSupportedException("Bundles are not supported by the Track O printer.");
        }
        WriteNode(hint, nodeStore, node, sourceFile, BeginPrint());
        return EndPrint();
    }

    internal string PrintList(ListFormat format, NodeStore nodeStore, NodeList nodes, Node sourceFile)
    {
        WriteList(format, nodeStore, nodes, sourceFile, BeginPrint());
        return EndPrint();
    }

    internal string PrintFile(NodeStore nodeStore, Node sourceFile)
    {
        WriteFile(nodeStore, sourceFile, BeginPrint());
        return EndPrint();
    }

    /// <summary>If <paramref name="sourceFile"/> is none, <paramref name="node"/> must be a synthesized node.</summary>
    internal void WriteNode(EmitHint hint, NodeStore nodeStore, Node node, Node sourceFile, EmitTextWriter output)
    {
        var previousWriter = writer;
        var previousStore = store;
        store = nodeStore;
        SetWriter(output);
        Print(hint, node, sourceFile);
        Reset();
        writer = previousWriter;
        store = previousStore;
    }

    internal void WriteList(ListFormat format, NodeStore nodeStore, NodeList nodes, Node sourceFile, EmitTextWriter output)
    {
        var previousWriter = writer;
        var previousStore = store;
        store = nodeStore;
        SetWriter(output);
        if (!sourceFile.IsNone)
        {
            SetSourceFile(sourceFile);
        }
        EmitList(Node.None, nodes, format);
        Reset();
        writer = previousWriter;
        store = previousStore;
    }

    internal void WriteFile(NodeStore nodeStore, Node sourceFile, EmitTextWriter output)
    {
        var previousWriter = writer;
        var previousStore = store;
        store = nodeStore;
        SetWriter(output);
        EmitShebangIfNeeded(sourceFile);
        EmitPrologueDirectivesIfNeeded(sourceFile);
        Print(EmitHint.SourceFile, sourceFile, sourceFile);
        Reset();
        writer = previousWriter;
        store = previousStore;
    }

    private EmitTextWriter BeginPrint()
    {
        return ownWriter ??= new EmitTextWriter(newLine);
    }

    private string EndPrint()
    {
        var text = ownWriter!.GetText();
        ownWriter.Clear();
        return text;
    }

    private void Print(EmitHint hint, Node node, Node sourceFile)
    {
        if (!sourceFile.IsNone)
        {
            SetSourceFile(sourceFile);
        }

        PipelineEmit(hint, node);
    }

    private void SetSourceFile(Node sourceFile)
    {
        currentSourceFile = sourceFile;
        currentLineMap = null;
        detachedCommentsInfo = null;
    }

    private void SetWriter(EmitTextWriter output)
    {
        writer = output;
    }

    private void Reset()
    {
        ResetNameGeneration();
        currentSourceFile = Node.None;
        currentLineMap = null;
        detachedCommentsInfo = null;
    }

    private ReadOnlySpan<char> CurrentText => store.SourceFileText(currentSourceFile).Chars;

    private IReadOnlyList<int> GetCurrentLineMap()
    {
        Debug.Assert(!currentSourceFile.IsNone);
        return currentLineMap ??= Scanner.ComputeLineStarts(CurrentText);
    }

    private void Emit(Node node)
    {
        if (node.IsNone)
        {
            return;
        }
        PipelineEmit(EmitHint.Unspecified, node);
    }

    private void EmitIdentifierName(Node node)
    {
        if (node.IsNone)
        {
            return;
        }
        PipelineEmit(EmitHint.IdentifierName, node);
    }

    private void EmitExpression(Node node)
    {
        if (node.IsNone)
        {
            return;
        }
        PipelineEmit(EmitHint.Expression, node);
    }

    private void EmitJsxAttributeValue(Node node)
    {
        PipelineEmit(store.Kind(node) == SyntaxKind.StringLiteral ? EmitHint.JsxAttributeValue : EmitHint.Unspecified, node);
    }

    // pipelineEmit: without substitution and source maps, the phases are notification (the handler below), comments
    // (pipelineEmitWithComments when shouldEmitComments) and pipelineEmitWithHint, which (without
    // onBeforeEmitNode/onAfterEmitNode and preserveSourceNewlines) is the worker.
    private void PipelineEmit(EmitHint emitHint, Node node, Func<Node, Node>? parenthesizerRule = null)
    {
        // Track O (module C2): the onEmitNode handler of typeWriter.ts' underlining printer, which toggles the writer's
        // underlining around every node whose fully-synthetic state differs from the current one.
        if (IsFullySynthetic is not null && IsFullySynthetic(node) != writer.Underlining)
        {
            writer.Underlining = !writer.Underlining;
            PipelineEmitAfterNotification(emitHint, node);
            writer.Underlining = !writer.Underlining;
            return;
        }
        if (handlers is null)
        {
            PipelineEmitAfterNotification(emitHint, node);
            return;
        }
        // Module D1: the print handlers' notification and substitution phases (getPipelinePhase).
        currentParenthesizerRule = parenthesizerRule;
        if (WantsNotification(node))
        {
            PipelineEmitWithNotification(emitHint, node);
        }
        else
        {
            PipelineEmitFromSubstitution(emitHint, node);
        }
        currentParenthesizerRule = null;
    }

    // getPipelinePhase from PipelinePhase.Substitution on (no substitution, no source maps).
    private void PipelineEmitAfterNotification(EmitHint emitHint, Node node)
    {
        if (ShouldEmitComments(node))
        {
            PipelineEmitWithComments(emitHint, node);
            return;
        }
        PipelineEmitWithHintWorker(emitHint, node);
    }

    /// <summary>
    /// Track O (module C2): typeWriter.ts' onEmitNode handler (nodeIsSynthesized(node) and no parse tree node behind
    /// it); null for printers without that handler.
    /// </summary>
    internal Func<Node, bool>? IsFullySynthetic { get; set; }

    private void PipelineEmitWithHintWorker(EmitHint hint, Node node)
    {
        if (FactoryEmitNodeSnippet(node) is not null)
        {
            throw new NotSupportedException("Snippet elements are not supported by the Track O printer.");
        }
        switch (hint)
        {
            case EmitHint.SourceFile:
                EmitSourceFile(Cast(node, SyntaxKind.SourceFile));
                return;
            case EmitHint.IdentifierName:
                EmitIdentifier(Cast(node, SyntaxKind.Identifier));
                return;
            case EmitHint.JsxAttributeValue:
                EmitLiteral(Cast(node, SyntaxKind.StringLiteral), jsxAttributeEscape: true);
                return;
            case EmitHint.MappedTypeParameter:
                EmitMappedTypeParameter(Cast(node, SyntaxKind.TypeParameter));
                return;
            case EmitHint.ImportTypeNodeAttributes:
                EmitImportTypeNodeAttributes(Cast(node, SyntaxKind.ImportAttributes));
                return;
            case EmitHint.EmbeddedStatement:
                Debug.Assert(store.Kind(node) == SyntaxKind.EmptyStatement);
                EmitEmptyStatement(isEmbeddedStatement: true);
                return;
        }
        if (hint == EmitHint.Unspecified)
        {
            if (EmitUnspecified(node))
            {
                return;
            }
            if (UtilitiesPublic.IsExpression(store, node))
            {
                hint = EmitHint.Expression;
            }
        }
        if (hint == EmitHint.Expression && EmitExpressionWorker(node))
        {
            return;
        }
        var kind = store.Kind(node);
        if (Utilities.IsKeyword(kind))
        {
            WriteTokenNode(node, WriteKind.Keyword);
            return;
        }
        if (IsTokenKind(kind))
        {
            WriteTokenNode(node, WriteKind.Punctuation);
            return;
        }
        Debug.Fail("Unhandled SyntaxKind: " + kind.ToString() + ".");
    }

    private object? FactoryEmitNodeSnippet(Node node) => (store.EmitNode(node) as EmitNode)?.SnippetElement;

    private Node Cast(Node node, SyntaxKind kind)
    {
        if (store.Kind(node) != kind)
        {
            throw Debug.InvalidCast(store.Kind(node));
        }
        return node;
    }

    private static bool IsTokenKind(SyntaxKind kind) => kind >= SyntaxKind.FirstToken && kind <= SyntaxKind.LastToken;

    /// <summary>The cases of pipelineEmitWithHintWorker for EmitHint.Unspecified; false when the node is not one of them.</summary>
    private bool EmitUnspecified(Node node)
    {
        switch (store.Kind(node))
        {
            // Pseudo-literals
            case SyntaxKind.TemplateHead:
            case SyntaxKind.TemplateMiddle:
            case SyntaxKind.TemplateTail:
                EmitLiteral(node, jsxAttributeEscape: false);
                return true;

            // Identifiers
            case SyntaxKind.Identifier:
                EmitIdentifier(node);
                return true;

            // PrivateIdentifiers
            case SyntaxKind.PrivateIdentifier:
                EmitPrivateIdentifier(node);
                return true;

            // Parse tree nodes
            // Names
            case SyntaxKind.QualifiedName:
                EmitQualifiedName(node);
                return true;
            case SyntaxKind.ComputedPropertyName:
                EmitComputedPropertyName(node);
                return true;

            // Signature elements
            case SyntaxKind.TypeParameter:
                EmitTypeParameter(node);
                return true;
            case SyntaxKind.Parameter:
                EmitParameter(node);
                return true;
            case SyntaxKind.Decorator:
                EmitDecorator(node);
                return true;

            // Type members
            case SyntaxKind.PropertySignature:
                EmitPropertySignature(node);
                return true;
            case SyntaxKind.PropertyDeclaration:
                EmitPropertyDeclaration(node);
                return true;
            case SyntaxKind.MethodSignature:
                EmitMethodSignature(node);
                return true;
            case SyntaxKind.MethodDeclaration:
                EmitMethodDeclaration(node);
                return true;
            case SyntaxKind.ClassStaticBlockDeclaration:
                EmitClassStaticBlockDeclaration(node);
                return true;
            case SyntaxKind.Constructor:
                EmitConstructor(node);
                return true;
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                EmitAccessorDeclaration(node);
                return true;
            case SyntaxKind.CallSignature:
                EmitCallSignature(node);
                return true;
            case SyntaxKind.ConstructSignature:
                EmitConstructSignature(node);
                return true;
            case SyntaxKind.IndexSignature:
                EmitIndexSignature(node);
                return true;

            // Types
            case SyntaxKind.TypePredicate:
                EmitTypePredicate(node);
                return true;
            case SyntaxKind.TypeReference:
                EmitTypeReference(node);
                return true;
            case SyntaxKind.FunctionType:
                EmitFunctionType(node);
                return true;
            case SyntaxKind.ConstructorType:
                EmitConstructorType(node);
                return true;
            case SyntaxKind.TypeQuery:
                EmitTypeQuery(node);
                return true;
            case SyntaxKind.TypeLiteral:
                EmitTypeLiteral(node);
                return true;
            case SyntaxKind.ArrayType:
                EmitArrayType(node);
                return true;
            case SyntaxKind.TupleType:
                EmitTupleType(node);
                return true;
            case SyntaxKind.OptionalType:
                EmitOptionalType(node);
                return true;
            // SyntaxKind.RestType is handled below
            case SyntaxKind.UnionType:
                EmitUnionType(node);
                return true;
            case SyntaxKind.IntersectionType:
                EmitIntersectionType(node);
                return true;
            case SyntaxKind.ConditionalType:
                EmitConditionalType(node);
                return true;
            case SyntaxKind.InferType:
                EmitInferType(node);
                return true;
            case SyntaxKind.ParenthesizedType:
                EmitParenthesizedType(node);
                return true;
            case SyntaxKind.ExpressionWithTypeArguments:
                EmitExpressionWithTypeArguments(node);
                return true;
            case SyntaxKind.ThisType:
                EmitThisType();
                return true;
            case SyntaxKind.TypeOperator:
                EmitTypeOperator(node);
                return true;
            case SyntaxKind.IndexedAccessType:
                EmitIndexedAccessType(node);
                return true;
            case SyntaxKind.MappedType:
                EmitMappedType(node);
                return true;
            case SyntaxKind.LiteralType:
                EmitLiteralType(node);
                return true;
            case SyntaxKind.NamedTupleMember:
                EmitNamedTupleMember(node);
                return true;
            case SyntaxKind.TemplateLiteralType:
                EmitTemplateType(node);
                return true;
            case SyntaxKind.TemplateLiteralTypeSpan:
                EmitTemplateTypeSpan(node);
                return true;
            case SyntaxKind.ImportType:
                EmitImportTypeNode(node);
                return true;

            // Binding patterns
            case SyntaxKind.ObjectBindingPattern:
                EmitObjectBindingPattern(node);
                return true;
            case SyntaxKind.ArrayBindingPattern:
                EmitArrayBindingPattern(node);
                return true;
            case SyntaxKind.BindingElement:
                EmitBindingElement(node);
                return true;

            // Misc
            case SyntaxKind.TemplateSpan:
                EmitTemplateSpan(node);
                return true;
            case SyntaxKind.SemicolonClassElement:
                EmitSemicolonClassElement();
                return true;

            // Statements
            case SyntaxKind.Block:
                EmitBlock(node);
                return true;
            case SyntaxKind.VariableStatement:
                EmitVariableStatement(node);
                return true;
            case SyntaxKind.EmptyStatement:
                EmitEmptyStatement(isEmbeddedStatement: false);
                return true;
            case SyntaxKind.ExpressionStatement:
                EmitExpressionStatement(node);
                return true;
            case SyntaxKind.IfStatement:
                EmitIfStatement(node);
                return true;
            case SyntaxKind.DoStatement:
                EmitDoStatement(node);
                return true;
            case SyntaxKind.WhileStatement:
                EmitWhileStatement(node);
                return true;
            case SyntaxKind.ForStatement:
                EmitForStatement(node);
                return true;
            case SyntaxKind.ForInStatement:
                EmitForInStatement(node);
                return true;
            case SyntaxKind.ForOfStatement:
                EmitForOfStatement(node);
                return true;
            case SyntaxKind.ContinueStatement:
                EmitContinueStatement(node);
                return true;
            case SyntaxKind.BreakStatement:
                EmitBreakStatement(node);
                return true;
            case SyntaxKind.ReturnStatement:
                EmitReturnStatement(node);
                return true;
            case SyntaxKind.WithStatement:
                EmitWithStatement(node);
                return true;
            case SyntaxKind.SwitchStatement:
                EmitSwitchStatement(node);
                return true;
            case SyntaxKind.LabeledStatement:
                EmitLabeledStatement(node);
                return true;
            case SyntaxKind.ThrowStatement:
                EmitThrowStatement(node);
                return true;
            case SyntaxKind.TryStatement:
                EmitTryStatement(node);
                return true;
            case SyntaxKind.DebuggerStatement:
                EmitDebuggerStatement(node);
                return true;

            // Declarations
            case SyntaxKind.VariableDeclaration:
                EmitVariableDeclaration(node);
                return true;
            case SyntaxKind.VariableDeclarationList:
                EmitVariableDeclarationList(node);
                return true;
            case SyntaxKind.FunctionDeclaration:
                EmitFunctionDeclaration(node);
                return true;
            case SyntaxKind.ClassDeclaration:
                EmitClassDeclaration(node);
                return true;
            case SyntaxKind.InterfaceDeclaration:
                EmitInterfaceDeclaration(node);
                return true;
            case SyntaxKind.TypeAliasDeclaration:
                EmitTypeAliasDeclaration(node);
                return true;
            case SyntaxKind.EnumDeclaration:
                EmitEnumDeclaration(node);
                return true;
            case SyntaxKind.ModuleDeclaration:
                EmitModuleDeclaration(node);
                return true;
            case SyntaxKind.ModuleBlock:
                EmitModuleBlock(node);
                return true;
            case SyntaxKind.CaseBlock:
                EmitCaseBlock(node);
                return true;
            case SyntaxKind.NamespaceExportDeclaration:
                EmitNamespaceExportDeclaration(node);
                return true;
            case SyntaxKind.ImportEqualsDeclaration:
                EmitImportEqualsDeclaration(node);
                return true;
            case SyntaxKind.ImportDeclaration:
                EmitImportDeclaration(node);
                return true;
            case SyntaxKind.ImportClause:
                EmitImportClause(node);
                return true;
            case SyntaxKind.NamespaceImport:
                EmitNamespaceImport(node);
                return true;
            case SyntaxKind.NamespaceExport:
                EmitNamespaceExport(node);
                return true;
            case SyntaxKind.NamedImports:
                EmitNamedImports(node);
                return true;
            case SyntaxKind.ImportSpecifier:
                EmitImportSpecifier(node);
                return true;
            case SyntaxKind.ExportAssignment:
                EmitExportAssignment(node);
                return true;
            case SyntaxKind.ExportDeclaration:
                EmitExportDeclaration(node);
                return true;
            case SyntaxKind.NamedExports:
                EmitNamedExports(node);
                return true;
            case SyntaxKind.ExportSpecifier:
                EmitExportSpecifier(node);
                return true;
            case SyntaxKind.ImportAttributes:
                EmitImportAttributes(node);
                return true;
            case SyntaxKind.ImportAttribute:
                EmitImportAttribute(node);
                return true;
            case SyntaxKind.MissingDeclaration:
                return true;

            // Module references
            case SyntaxKind.ExternalModuleReference:
                EmitExternalModuleReference(node);
                return true;

            // JSX (non-expression)
            case SyntaxKind.JsxText:
                EmitJsxText(node);
                return true;
            case SyntaxKind.JsxOpeningElement:
            case SyntaxKind.JsxOpeningFragment:
                EmitJsxOpeningElementOrFragment(node);
                return true;
            case SyntaxKind.JsxClosingElement:
            case SyntaxKind.JsxClosingFragment:
                EmitJsxClosingElementOrFragment(node);
                return true;
            case SyntaxKind.JsxAttribute:
                EmitJsxAttribute(node);
                return true;
            case SyntaxKind.JsxAttributes:
                EmitJsxAttributes(node);
                return true;
            case SyntaxKind.JsxSpreadAttribute:
                EmitJsxSpreadAttribute(node);
                return true;
            case SyntaxKind.JsxExpression:
                EmitJsxExpression(node);
                return true;
            case SyntaxKind.JsxNamespacedName:
                EmitJsxNamespacedName(node);
                return true;

            // Clauses
            case SyntaxKind.CaseClause:
                EmitCaseClause(node);
                return true;
            case SyntaxKind.DefaultClause:
                EmitDefaultClause(node);
                return true;
            case SyntaxKind.HeritageClause:
                EmitHeritageClause(node);
                return true;
            case SyntaxKind.CatchClause:
                EmitCatchClause(node);
                return true;

            // Property assignments
            case SyntaxKind.PropertyAssignment:
                EmitPropertyAssignment(node);
                return true;
            case SyntaxKind.ShorthandPropertyAssignment:
                EmitShorthandPropertyAssignment(node);
                return true;
            case SyntaxKind.SpreadAssignment:
                EmitSpreadAssignment(node);
                return true;

            // Enum
            case SyntaxKind.EnumMember:
                EmitEnumMember(node);
                return true;

            // Top-level nodes
            case SyntaxKind.SourceFile:
                EmitSourceFile(node);
                return true;
            case SyntaxKind.Bundle:
                Debug.Fail("Bundles should be printed using printBundle");
                return true;

            // JSDoc nodes (only used in codefixes currently)
            case SyntaxKind.JSDocTypeExpression:
                EmitJSDocTypeExpressionNode(node);
                return true;
            case SyntaxKind.JSDocNameReference:
                EmitJSDocNameReference(node);
                return true;
            case SyntaxKind.JSDocAllType:
                WritePunctuation("*");
                return true;
            case SyntaxKind.JSDocUnknownType:
                WritePunctuation("?");
                return true;
            case SyntaxKind.JSDocNullableType:
                EmitJSDocNullableType(node);
                return true;
            case SyntaxKind.JSDocNonNullableType:
                EmitJSDocNonNullableType(node);
                return true;
            case SyntaxKind.JSDocOptionalType:
                EmitJSDocOptionalType(node);
                return true;
            case SyntaxKind.JSDocFunctionType:
                EmitJSDocFunctionType(node);
                return true;
            case SyntaxKind.RestType:
            case SyntaxKind.JSDocVariadicType:
                EmitRestOrJSDocVariadicType(node);
                return true;
            case SyntaxKind.JSDocNamepathType:
                return true;
            case SyntaxKind.JSDoc:
                EmitJSDoc(node);
                return true;
            case SyntaxKind.JSDocTypeLiteral:
                EmitJSDocTypeLiteral(node);
                return true;
            case SyntaxKind.JSDocSignature:
                EmitJSDocSignature(node);
                return true;
            case SyntaxKind.JSDocTag:
            case SyntaxKind.JSDocClassTag:
            case SyntaxKind.JSDocOverrideTag:
                EmitJSDocSimpleTag(node);
                return true;
            case SyntaxKind.JSDocAugmentsTag:
            case SyntaxKind.JSDocImplementsTag:
                EmitJSDocHeritageTag(node);
                return true;
            case SyntaxKind.JSDocAuthorTag:
            case SyntaxKind.JSDocDeprecatedTag:
                return true;
            // SyntaxKind.JSDocClassTag (see JSDocTag, above)
            case SyntaxKind.JSDocPublicTag:
            case SyntaxKind.JSDocPrivateTag:
            case SyntaxKind.JSDocProtectedTag:
            case SyntaxKind.JSDocReadonlyTag:
                return true;
            case SyntaxKind.JSDocCallbackTag:
                EmitJSDocCallbackTag(node);
                return true;
            case SyntaxKind.JSDocOverloadTag:
                EmitJSDocOverloadTag(node);
                return true;
            // SyntaxKind.JSDocEnumTag (see below)
            case SyntaxKind.JSDocParameterTag:
            case SyntaxKind.JSDocPropertyTag:
                EmitJSDocPropertyLikeTag(node);
                return true;
            case SyntaxKind.JSDocEnumTag:
            case SyntaxKind.JSDocReturnTag:
            case SyntaxKind.JSDocThisTag:
            case SyntaxKind.JSDocTypeTag:
            case SyntaxKind.JSDocThrowsTag:
            case SyntaxKind.JSDocSatisfiesTag:
                EmitJSDocSimpleTypedTag(node);
                return true;
            case SyntaxKind.JSDocTemplateTag:
                EmitJSDocTemplateTag(node);
                return true;
            case SyntaxKind.JSDocTypedefTag:
                EmitJSDocTypedefTag(node);
                return true;
            case SyntaxKind.JSDocSeeTag:
                EmitJSDocSeeTag(node);
                return true;
            case SyntaxKind.JSDocImportTag:
                EmitJSDocImportTag(node);
                return true;
            // SyntaxKind.JSDocPropertyTag (see JSDocParameterTag, above)

            // Transformation nodes
            case SyntaxKind.NotEmittedStatement:
            case SyntaxKind.NotEmittedTypeElement:
                return true;
        }
        return false;
    }

    /// <summary>The cases of pipelineEmitWithHintWorker for EmitHint.Expression; false when the node is not one of them.</summary>
    private bool EmitExpressionWorker(Node node)
    {
        switch (store.Kind(node))
        {
            // Literals
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
                EmitNumericOrBigIntLiteral(node);
                return true;

            case SyntaxKind.StringLiteral:
            case SyntaxKind.RegularExpressionLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                EmitLiteral(node, jsxAttributeEscape: false);
                return true;

            // Identifiers
            case SyntaxKind.Identifier:
                EmitIdentifier(node);
                return true;
            case SyntaxKind.PrivateIdentifier:
                EmitPrivateIdentifier(node);
                return true;

            // Expressions
            case SyntaxKind.ArrayLiteralExpression:
                EmitArrayLiteralExpression(node);
                return true;
            case SyntaxKind.ObjectLiteralExpression:
                EmitObjectLiteralExpression(node);
                return true;
            case SyntaxKind.PropertyAccessExpression:
                EmitPropertyAccessExpression(node);
                return true;
            case SyntaxKind.ElementAccessExpression:
                EmitElementAccessExpression(node);
                return true;
            case SyntaxKind.CallExpression:
                EmitCallExpression(node);
                return true;
            case SyntaxKind.NewExpression:
                EmitNewExpression(node);
                return true;
            case SyntaxKind.TaggedTemplateExpression:
                EmitTaggedTemplateExpression(node);
                return true;
            case SyntaxKind.TypeAssertionExpression:
                EmitTypeAssertionExpression(node);
                return true;
            case SyntaxKind.ParenthesizedExpression:
                EmitParenthesizedExpression(node);
                return true;
            case SyntaxKind.FunctionExpression:
                EmitFunctionExpression(node);
                return true;
            case SyntaxKind.ArrowFunction:
                EmitArrowFunction(node);
                return true;
            case SyntaxKind.DeleteExpression:
                EmitDeleteExpression(node);
                return true;
            case SyntaxKind.TypeOfExpression:
                EmitTypeOfExpression(node);
                return true;
            case SyntaxKind.VoidExpression:
                EmitVoidExpression(node);
                return true;
            case SyntaxKind.AwaitExpression:
                EmitAwaitExpression(node);
                return true;
            case SyntaxKind.PrefixUnaryExpression:
                EmitPrefixUnaryExpression(node);
                return true;
            case SyntaxKind.PostfixUnaryExpression:
                EmitPostfixUnaryExpression(node);
                return true;
            case SyntaxKind.BinaryExpression:
                EmitBinaryExpression(node);
                return true;
            case SyntaxKind.ConditionalExpression:
                EmitConditionalExpression(node);
                return true;
            case SyntaxKind.TemplateExpression:
                EmitTemplateExpression(node);
                return true;
            case SyntaxKind.YieldExpression:
                EmitYieldExpression(node);
                return true;
            case SyntaxKind.SpreadElement:
                EmitSpreadElement(node);
                return true;
            case SyntaxKind.ClassExpression:
                EmitClassExpression(node);
                return true;
            case SyntaxKind.OmittedExpression:
                return true;
            case SyntaxKind.AsExpression:
                EmitAsExpression(node);
                return true;
            case SyntaxKind.NonNullExpression:
                EmitNonNullExpression(node);
                return true;
            case SyntaxKind.ExpressionWithTypeArguments:
                EmitExpressionWithTypeArguments(node);
                return true;
            case SyntaxKind.SatisfiesExpression:
                EmitSatisfiesExpression(node);
                return true;
            case SyntaxKind.MetaProperty:
                EmitMetaProperty(node);
                return true;
            case SyntaxKind.SyntheticExpression:
                Debug.Fail("SyntheticExpression should never be printed.");
                return true;
            case SyntaxKind.MissingDeclaration:
                return true;

            // JSX
            case SyntaxKind.JsxElement:
                EmitJsxElement(node);
                return true;
            case SyntaxKind.JsxSelfClosingElement:
                EmitJsxSelfClosingElement(node);
                return true;
            case SyntaxKind.JsxFragment:
                EmitJsxFragment(node);
                return true;

            // Synthesized list
            case SyntaxKind.SyntaxList:
                Debug.Fail("SyntaxList should not be printed");
                return true;

            // Transformation nodes
            case SyntaxKind.NotEmittedStatement:
                return true;
            case SyntaxKind.PartiallyEmittedExpression:
                EmitPartiallyEmittedExpression(node);
                return true;
            case SyntaxKind.CommaListExpression:
                EmitCommaList(node);
                return true;
            case SyntaxKind.SyntheticReferenceExpression:
                Debug.Fail("SyntheticReferenceExpression should not be printed");
                return true;
        }
        return false;
    }

    //
    // Text of nodes
    //

    private string GetTextOfNode(Node node, bool includeTrivia = false)
    {
        var kind = store.Kind(node);
        if (UtilitiesPublic.IsGeneratedIdentifier(store, node) || (kind == SyntaxKind.PrivateIdentifier && IsGeneratedPrivateIdentifier(node)))
        {
            return GenerateName(node);
        }
        if (kind == SyntaxKind.StringLiteral && !store.TextSourceNode(node).IsNone)
        {
            return GetTextOfNode(store.TextSourceNode(node), includeTrivia);
        }
        var sourceFile = currentSourceFile; // const needed for control flow
        var canUseSourceFile = !sourceFile.IsNone && !store.Parent(node).IsNone && !Utilities.NodeIsSynthesized(store, node);
        if (kind is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier)
        {
            if (!canUseSourceFile || Utilities.GetSourceFileOfNode(store, node) != GetOriginalNode(sourceFile))
            {
                return PrinterUtilities.IdText(store, node);
            }
        }
        else if (kind == SyntaxKind.JsxNamespacedName)
        {
            if (!canUseSourceFile || Utilities.GetSourceFileOfNode(store, node) != GetOriginalNode(sourceFile))
            {
                return PrinterUtilities.GetTextOfJsxNamespacedName(store, node);
            }
        }
        else
        {
            if (!canUseSourceFile)
            {
                return PrinterUtilities.TextOf(store, node);
            }
        }
        return Utilities.GetSourceTextOfNodeFromSourceFile(store, sourceFile, node, includeTrivia);
    }

    private bool IsGeneratedPrivateIdentifier(Node node)
    {
        var emitNode = store.EmitNode(node) as EmitNode;
        return emitNode?.AutoGenerate is not null;
    }

    private string GetLiteralTextOfNode(Node node, Node sourceFile, bool neverAsciiEscape, bool jsxAttributeEscape)
    {
        if (store.Kind(node) == SyntaxKind.StringLiteral && !store.TextSourceNode(node).IsNone)
        {
            var textSourceNode = store.TextSourceNode(node);
            var sourceKind = store.Kind(textSourceNode);
            if (sourceKind is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier or SyntaxKind.NumericLiteral or SyntaxKind.JsxNamespacedName)
            {
                var text = sourceKind == SyntaxKind.NumericLiteral ? PrinterUtilities.TextOf(store, textSourceNode) : GetTextOfNode(textSourceNode);
                return jsxAttributeEscape ? "\"" + PrinterUtilities.EscapeJsxAttributeString(text) + "\"" :
                    neverAsciiEscape || (Utilities.GetEmitFlags(store, node) & EmitFlags.NoAsciiEscaping) != 0 ? "\"" + PrinterUtilities.EscapeString(text) + "\"" :
                    "\"" + PrinterUtilities.EscapeNonAsciiString(text) + "\"";
            }
            else
            {
                return GetLiteralTextOfNode(textSourceNode, Utilities.GetSourceFileOfNode(store, textSourceNode), neverAsciiEscape, jsxAttributeEscape);
            }
        }

        var flags = (neverAsciiEscape ? GetLiteralTextFlags.NeverAsciiEscape : 0)
            | (jsxAttributeEscape ? GetLiteralTextFlags.JsxAttributeEscape : 0)
            | (printerOptions.TerminateUnterminatedLiterals ? GetLiteralTextFlags.TerminateUnterminatedLiterals : 0)
            | (printerOptions.Target is { } target && target >= ScriptTarget.ES2021 ? GetLiteralTextFlags.AllowNumericSeparator : 0);

        return PrinterUtilities.GetLiteralText(store, node, sourceFile, flags);
    }

    private Node GetOriginalNode(Node node)
    {
        if (!node.IsNone)
        {
            while (!store.Original(node).IsNone)
            {
                node = store.Original(node);
            }
        }
        return node;
    }

    private bool NodeIsSynthesized(Node node) => Utilities.NodeIsSynthesized(store, node);

    private static bool PositionIsSynthesized(int pos) => Utilities.PositionIsSynthesized(pos);

    private EmitFlags GetEmitFlags(Node node) => Utilities.GetEmitFlags(store, node);

    private InternalEmitFlags GetInternalEmitFlags(Node node) => (store.EmitNode(node) as EmitNode)?.InternalFlags ?? 0;

    private bool? GetStartsOnNewLine(Node node) => FactoryEmitNode.GetStartsOnNewLine(store, node);

    private bool IsJsonSourceFile(Node sourceFile) => store.ScriptKind(sourceFile) == ScriptKind.JSON;
}
