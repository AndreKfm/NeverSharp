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

/// <summary>
/// nodeFactory.ts, createNodeFactory: JSX, clauses, property assignments, enum members, top-level nodes,
/// transformation nodes, outer expressions and the helpers at the end of createNodeFactory.
/// </summary>
internal sealed partial class NodeFactory
{
    //
    // JSX
    //

    // @api
    internal JsxElement CreateJsxElement(JsxOpeningElement openingElement, IReadOnlyList<Node> children, JsxClosingElement closingElement)
    {
        var node = (JsxElement)CreateBaseNode(SyntaxKind.JsxElement);
        node.OpeningElement = openingElement;
        node.Children = CreateNodeArray(children);
        node.ClosingElement = closingElement;
        node.TransformFlags |= PropagateChildFlags(node.OpeningElement) |
            PropagateChildrenFlags(node.Children) |
            PropagateChildFlags(node.ClosingElement) |
            TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxSelfClosingElement CreateJsxSelfClosingElement(Node tagName, IReadOnlyList<Node>? typeArguments, JsxAttributes attributes)
    {
        var node = (JsxSelfClosingElement)CreateBaseNode(SyntaxKind.JsxSelfClosingElement);
        node.TagName = tagName;
        node.TypeArguments = AsNodeArray(typeArguments);
        node.Attributes = attributes;
        node.TransformFlags |= PropagateChildFlags(node.TagName) |
            PropagateChildrenFlags(node.TypeArguments) |
            PropagateChildFlags(node.Attributes) |
            TransformFlags.ContainsJsx;
        if (node.TypeArguments != null)
        {
            node.TransformFlags |= TransformFlags.ContainsTypeScript;
        }
        return node;
    }

    // @api
    internal JsxOpeningElement CreateJsxOpeningElement(Node tagName, IReadOnlyList<Node>? typeArguments, JsxAttributes attributes)
    {
        var node = (JsxOpeningElement)CreateBaseNode(SyntaxKind.JsxOpeningElement);
        node.TagName = tagName;
        node.TypeArguments = AsNodeArray(typeArguments);
        node.Attributes = attributes;
        node.TransformFlags |= PropagateChildFlags(node.TagName) |
            PropagateChildrenFlags(node.TypeArguments) |
            PropagateChildFlags(node.Attributes) |
            TransformFlags.ContainsJsx;
        if (typeArguments != null)
        {
            node.TransformFlags |= TransformFlags.ContainsTypeScript;
        }
        return node;
    }

    // @api
    internal JsxClosingElement CreateJsxClosingElement(Node tagName)
    {
        var node = (JsxClosingElement)CreateBaseNode(SyntaxKind.JsxClosingElement);
        node.TagName = tagName;
        node.TransformFlags |= PropagateChildFlags(node.TagName) |
            TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxFragment CreateJsxFragment(JsxOpeningFragment openingFragment, IReadOnlyList<Node> children, JsxClosingFragment closingFragment)
    {
        var node = (JsxFragment)CreateBaseNode(SyntaxKind.JsxFragment);
        node.OpeningFragment = openingFragment;
        node.Children = CreateNodeArray(children);
        node.ClosingFragment = closingFragment;
        node.TransformFlags |= PropagateChildFlags(node.OpeningFragment) |
            PropagateChildrenFlags(node.Children) |
            PropagateChildFlags(node.ClosingFragment) |
            TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxText CreateJsxText(string text, bool containsOnlyTriviaWhiteSpaces = false)
    {
        var node = (JsxText)CreateBaseNode(SyntaxKind.JsxText);
        node.Text = text;
        node.ContainsOnlyTriviaWhiteSpaces = containsOnlyTriviaWhiteSpaces;
        node.TransformFlags |= TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxOpeningFragment CreateJsxOpeningFragment()
    {
        var node = (JsxOpeningFragment)CreateBaseNode(SyntaxKind.JsxOpeningFragment);
        node.TransformFlags |= TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxClosingFragment CreateJsxJsxClosingFragment()
    {
        var node = (JsxClosingFragment)CreateBaseNode(SyntaxKind.JsxClosingFragment);
        node.TransformFlags |= TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxAttribute CreateJsxAttribute(Node name, Node? initializer)
    {
        var node = (JsxAttribute)CreateBaseDeclaration(SyntaxKind.JsxAttribute);
        node.Name = name;
        node.Initializer = initializer;
        node.TransformFlags |= PropagateChildFlags(node.Name) |
            PropagateChildFlags(node.Initializer) |
            TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxAttributes CreateJsxAttributes(IReadOnlyList<Node> properties)
    {
        var node = (JsxAttributes)CreateBaseDeclaration(SyntaxKind.JsxAttributes);
        node.Properties = CreateNodeArray(properties);
        node.TransformFlags |= PropagateChildrenFlags(node.Properties) |
            TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxSpreadAttribute CreateJsxSpreadAttribute(Node expression)
    {
        var node = (JsxSpreadAttribute)CreateBaseNode(SyntaxKind.JsxSpreadAttribute);
        node.Expression = expression;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxExpression CreateJsxExpression(DotDotDotToken? dotDotDotToken, Node? expression)
    {
        var node = (JsxExpression)CreateBaseNode(SyntaxKind.JsxExpression);
        node.DotDotDotToken = dotDotDotToken;
        node.Expression = expression;
        node.TransformFlags |= PropagateChildFlags(node.DotDotDotToken) |
            PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsJsx;
        return node;
    }

    // @api
    internal JsxNamespacedName CreateJsxNamespacedName(Identifier @namespace, Identifier name)
    {
        var node = (JsxNamespacedName)CreateBaseNode(SyntaxKind.JsxNamespacedName);
        node.Namespace = @namespace;
        node.Name = name;
        node.TransformFlags |= PropagateChildFlags(node.Namespace) |
            PropagateChildFlags(node.Name) |
            TransformFlags.ContainsJsx;
        return node;
    }

    //
    // Clauses
    //

    // @api
    internal CaseClause CreateCaseClause(Node expression, IReadOnlyList<Node> statements)
    {
        var node = (CaseClause)CreateBaseNode(SyntaxKind.CaseClause);
        node.Expression = Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression);
        node.Statements = CreateNodeArray(statements);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildrenFlags(node.Statements);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal DefaultClause CreateDefaultClause(IReadOnlyList<Node> statements)
    {
        var node = (DefaultClause)CreateBaseNode(SyntaxKind.DefaultClause);
        node.Statements = CreateNodeArray(statements);
        node.TransformFlags = PropagateChildrenFlags(node.Statements);
        return node;
    }

    // @api
    internal HeritageClause CreateHeritageClause(SyntaxKind token, IReadOnlyList<Node> types)
    {
        var node = (HeritageClause)CreateBaseNode(SyntaxKind.HeritageClause);
        node.Token = token;
        node.Types = CreateNodeArray(types);
        node.TransformFlags |= PropagateChildrenFlags(node.Types);
        switch (token)
        {
            case SyntaxKind.ExtendsKeyword:
                node.TransformFlags |= TransformFlags.ContainsES2015;
                break;
            case SyntaxKind.ImplementsKeyword:
                node.TransformFlags |= TransformFlags.ContainsTypeScript;
                break;
            default:
                Debug.AssertNever(token);
                return null!;
        }
        return node;
    }

    // @api
    internal CatchClause CreateCatchClause(Node? variableDeclaration, Block block)
    {
        var node = (CatchClause)CreateBaseNode(SyntaxKind.CatchClause);
        node.VariableDeclaration = AsVariableDeclaration(variableDeclaration);
        node.Block = block;

        node.TransformFlags |= PropagateChildFlags(node.VariableDeclaration) |
            PropagateChildFlags(node.Block) |
            (variableDeclaration == null ? TransformFlags.ContainsES2019 : TransformFlags.None);

        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        return node;
    }

    //
    // Property assignments
    //

    // @api
    internal PropertyAssignment CreatePropertyAssignment(Node name, Node initializer)
    {
        var node = (PropertyAssignment)CreateBaseDeclaration(SyntaxKind.PropertyAssignment);
        node.Name = AsName(name);
        node.Initializer = Parenthesizer.ParenthesizeExpressionForDisallowedComma(initializer);
        node.TransformFlags |= PropagateNameFlags(node.Name) |
            PropagateChildFlags(node.Initializer);

        node.Modifiers = null; // initialized by parser to report grammar errors
        node.QuestionToken = null; // initialized by parser to report grammar errors
        node.ExclamationToken = null; // initialized by parser to report grammar errors
        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal ShorthandPropertyAssignment CreateShorthandPropertyAssignment(Identifier name, Node? objectAssignmentInitializer = null)
    {
        var node = (ShorthandPropertyAssignment)CreateBaseDeclaration(SyntaxKind.ShorthandPropertyAssignment);
        node.Name = AsName(name);
        node.ObjectAssignmentInitializer = objectAssignmentInitializer != null ? Parenthesizer.ParenthesizeExpressionForDisallowedComma(objectAssignmentInitializer) : null;
        node.TransformFlags |= PropagateIdentifierNameFlags(node.Name) |
            PropagateChildFlags(node.ObjectAssignmentInitializer) |
            TransformFlags.ContainsES2015;

        node.EqualsToken = null; // initialized by parser to report grammar errors
        node.Modifiers = null; // initialized by parser to report grammar errors
        node.QuestionToken = null; // initialized by parser to report grammar errors
        node.ExclamationToken = null; // initialized by parser to report grammar errors
        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal SpreadAssignment CreateSpreadAssignment(Node expression)
    {
        var node = (SpreadAssignment)CreateBaseDeclaration(SyntaxKind.SpreadAssignment);
        node.Expression = Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsES2018 |
            TransformFlags.ContainsObjectRestOrSpread;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    //
    // Enum
    //

    // @api
    internal EnumMember CreateEnumMember(Node name, Node? initializer = null)
    {
        var node = (EnumMember)CreateBaseDeclaration(SyntaxKind.EnumMember);
        node.Name = AsName(name);
        node.Initializer = initializer != null ? Parenthesizer.ParenthesizeExpressionForDisallowedComma(initializer) : null;
        node.TransformFlags |= PropagateChildFlags(node.Name) |
            PropagateChildFlags(node.Initializer) |
            TransformFlags.ContainsTypeScript;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    //
    // Top-level nodes
    //

    // @api
    internal SourceFile CreateSourceFile(IReadOnlyList<Node> statements, EndOfFileToken endOfFileToken, NodeFlags flags)
    {
        var node = (SourceFile)BaseFactory.CreateBaseSourceFileNode(SyntaxKind.SourceFile);
        node.Statements = CreateNodeArray(statements);
        node.EndOfFileToken = endOfFileToken;
        node.Flags |= flags;
        node.Text = "";
        node.FileName = "";
        node.Path = "";
        node.ResolvedPath = "";
        node.OriginalFileName = "";
        node.LanguageVersion = ScriptTarget.LatestStandard;
        node.LanguageVariant = 0;
        node.ScriptKind = 0;
        node.IsDeclarationFile = false;
        node.HasNoDefaultLib = false;

        node.TransformFlags |= PropagateChildrenFlags(node.Statements) |
            PropagateChildFlags(node.EndOfFileToken);

        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.EndFlowNode = null;

        node.NodeCount = 0;
        node.IdentifierCount = 0;
        node.SymbolCount = 0;
        node.ParseDiagnostics = null!;
        node.BindDiagnostics = null!;
        node.BindSuggestionDiagnostics = null;
        node.LineMap = null!;
        node.ExternalModuleIndicator = null;
        node.SetExternalModuleIndicator = null;
        node.Pragmas = null!;
        node.CheckJsDirective = null;
        node.ReferencedFiles = null!;
        node.TypeReferenceDirectives = null!;
        node.LibReferenceDirectives = null!;
        node.AmdDependencies = null!;
        node.CommentDirectives = null;
        node.Identifiers = null!;
        node.PackageJsonLocations = null;
        node.PackageJsonScope = null;
        node.Imports = null!;
        node.ModuleAugmentations = null!;
        node.AmbientModuleNames = null!;
        node.ClassifiableNames = null;
        node.ImpliedNodeFormat = default; // TypeScript: undefined (the generated field is a non-nullable ModuleKind)
        return node;
    }

    /// <summary>
    /// TypeScript creates the redirect with `Object.create(redirectInfo.redirectTarget)` and getters/setters for `id` and
    /// `symbol` that forward to the target (prototype delegation). C# objects have no prototype chain; the parser never
    /// creates redirected source files (RedirectInfo is set by the program), so this throws.
    /// </summary>
    internal static SourceFile CreateRedirectedSourceFile(object redirectInfo)
    {
        throw new NotSupportedException("createRedirectedSourceFile: prototype delegation (Object.create) has no C# counterpart");
    }

    private static SourceFile CloneRedirectedSourceFile(SourceFile source)
    {
        var node = CreateRedirectedSourceFile(source.RedirectInfo!);
        node.Flags |= source.Flags & ~NodeFlags.Synthesized;
        node.FileName = source.FileName;
        node.Path = source.Path;
        node.ResolvedPath = source.ResolvedPath;
        node.OriginalFileName = source.OriginalFileName;
        node.PackageJsonLocations = source.PackageJsonLocations;
        node.PackageJsonScope = source.PackageJsonScope;
        node.EmitNode = null;
        return node;
    }

    private SourceFile CloneSourceFileWorker(SourceFile source)
    {
        // TODO: This mechanism for cloning results in megamorphic property reads and writes. In future perf-related
        //       work, we should consider switching explicit property assignments instead of using `for..in`.
        var node = (SourceFile)BaseFactory.CreateBaseSourceFileNode(SyntaxKind.SourceFile);
        node.Flags |= source.Flags & ~NodeFlags.Synthesized;
        // for (const p in source) { if (hasProperty(node, p) || !hasProperty(source, p)) continue; ...
        //     (node as any)[p] = (source as any)[p]; }
        // The properties the new node already has are those the Node constructor sets (pos, end, kind, id, flags,
        // modifierFlagsCache, transformFlags, parent, original, emitNode); `emitNode` is reset to undefined. Every other
        // member is copied (a member the source does not have is the default value on both).
        CopySourceFileMembers(node, source);
        return node;
    }

    /// <summary>The copy loop of cloneSourceFileWorker over the members of SourceFile that are not Node constructor fields.</summary>
    private static void CopySourceFileMembers(SourceFile node, SourceFile source)
    {
        // Generated members (types.ts SourceFile).
        node.AmbientModuleNames = source.AmbientModuleNames;
        node.ClassifiableNames = source.ClassifiableNames;
        node.EndFlowNode = source.EndFlowNode;
        node.EndOfFileToken = source.EndOfFileToken;
        node.ExternalModuleIndicator = source.ExternalModuleIndicator;
        node.FileName = source.FileName;
        node.HasNoDefaultLib = source.HasNoDefaultLib;
        node.IdentifierCount = source.IdentifierCount;
        node.Identifiers = source.Identifiers;
        node.ImpliedNodeFormat = source.ImpliedNodeFormat;
        node.Imports = source.Imports;
        node.IsDeclarationFile = source.IsDeclarationFile;
        node.JsDocParsingMode = source.JsDocParsingMode;
        node.LanguageVariant = source.LanguageVariant;
        node.LanguageVersion = source.LanguageVersion;
        node.LineMap = source.LineMap;
        node.LocalSymbol = source.LocalSymbol;
        node.Locals = source.Locals;
        node.ModuleAugmentations = source.ModuleAugmentations;
        node.ModuleName = source.ModuleName;
        node.NextContainer = source.NextContainer;
        node.NodeCount = source.NodeCount;
        node.OriginalFileName = source.OriginalFileName;
        node.PackageJsonLocations = source.PackageJsonLocations;
        node.PackageJsonScope = source.PackageJsonScope;
        node.Path = source.Path;
        node.RedirectInfo = source.RedirectInfo;
        node.ResolvedPath = source.ResolvedPath;
        node.ScriptKind = source.ScriptKind;
        node.SetExternalModuleIndicator = source.SetExternalModuleIndicator;
        node.Statements = source.Statements;
        node.Symbol = source.Symbol;
        node.SymbolCount = source.SymbolCount;
        node.Text = source.Text;

        // Members declared by the port (Parser/Types.cs).
        node.AmdDependencies = source.AmdDependencies;
        node.ReferencedFiles = source.ReferencedFiles;
        node.TypeReferenceDirectives = source.TypeReferenceDirectives;
        node.LibReferenceDirectives = source.LibReferenceDirectives;
        node.ParseDiagnostics = source.ParseDiagnostics;
        node.BindDiagnostics = source.BindDiagnostics;
        node.BindSuggestionDiagnostics = source.BindSuggestionDiagnostics;
        node.JsDocDiagnostics = source.JsDocDiagnostics;
        node.CommentDirectives = source.CommentDirectives;
        node.CheckJsDirective = source.CheckJsDirective;
        node.Pragmas = source.Pragmas;

        // `if (p === "emitNode") { node.emitNode = undefined; continue; }` (emitNode is a Node constructor field the new
        // node already has; the assignment keeps it undefined).
        node.EmitNode = null;
    }

    private SourceFile CloneSourceFile(SourceFile source)
    {
        var node = source.RedirectInfo != null ? CloneRedirectedSourceFile(source) : CloneSourceFileWorker(source);
        setOriginal(node, source);
        return node;
    }

    private SourceFile CloneSourceFileWithChanges(
        SourceFile source,
        IReadOnlyList<Node> statements,
        bool isDeclarationFile,
        IReadOnlyList<FileReference> referencedFiles,
        IReadOnlyList<FileReference> typeReferences,
        bool hasNoDefaultLib,
        IReadOnlyList<FileReference> libReferences)
    {
        var node = CloneSourceFile(source);
        node.Statements = CreateNodeArray(statements);
        node.IsDeclarationFile = isDeclarationFile;
        node.ReferencedFiles = referencedFiles;
        node.TypeReferenceDirectives = typeReferences;
        node.HasNoDefaultLib = hasNoDefaultLib;
        node.LibReferenceDirectives = libReferences;
        node.TransformFlags = PropagateChildrenFlags(node.Statements) |
            PropagateChildFlags(node.EndOfFileToken);
        return node;
    }

    /// <summary>
    /// updateSourceFile. The TypeScript defaults (`isDeclarationFile = node.isDeclarationFile`, ...) are applied when
    /// an argument is null (JavaScript applies a default when the argument is undefined).
    /// </summary>
    // @api
    internal SourceFile UpdateSourceFile(
        SourceFile node,
        IReadOnlyList<Node> statements,
        bool? isDeclarationFile = null,
        IReadOnlyList<FileReference>? referencedFiles = null,
        IReadOnlyList<FileReference>? typeReferenceDirectives = null,
        bool hasNoDefaultLib = false,
        IReadOnlyList<FileReference>? libReferenceDirectives = null)
    {
        var isDeclarationFileValue = isDeclarationFile ?? node.IsDeclarationFile;
        var referencedFilesValue = referencedFiles ?? node.ReferencedFiles;
        var typeReferenceDirectivesValue = typeReferenceDirectives ?? node.TypeReferenceDirectives;
        var libReferenceDirectivesValue = libReferenceDirectives ?? node.LibReferenceDirectives;
        return node.Statements != statements
                || node.IsDeclarationFile != isDeclarationFileValue
                || node.ReferencedFiles != referencedFilesValue
                || node.TypeReferenceDirectives != typeReferenceDirectivesValue
                || node.LibReferenceDirectives != libReferenceDirectivesValue
            ? Update(CloneSourceFileWithChanges(node, statements, isDeclarationFileValue, referencedFilesValue, typeReferenceDirectivesValue, /*hasNoDefaultLib*/ false, libReferenceDirectivesValue), node)
            : node;
    }

    //
    // Transformation nodes
    //

    /// <summary>
    /// Creates a synthetic expression to act as a placeholder for a not-emitted expression in
    /// order to preserve comments or sourcemap positions.
    /// </summary>
    /// <param name="expression">The inner expression to emit.</param>
    /// <param name="original">The original outer expression.</param>
    // @api
    internal PartiallyEmittedExpression CreatePartiallyEmittedExpression(Node expression, Node? original = null)
    {
        var node = (PartiallyEmittedExpression)CreateBaseNode(SyntaxKind.PartiallyEmittedExpression);
        node.Expression = expression;
        node.Original = original;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsTypeScript;
        FactoryUtilitiesPublic.SetTextRange(node, original);
        return node;
    }

    // @api
    internal PartiallyEmittedExpression UpdatePartiallyEmittedExpression(PartiallyEmittedExpression node, Node expression)
    {
        return node.Expression != expression
            ? Update(CreatePartiallyEmittedExpression(expression, node.Original), node)
            : node;
    }

    private Node UpdateOuterExpression(Node outerExpression, Node expression)
    {
        switch (outerExpression.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                return UpdateParenthesizedExpression((ParenthesizedExpression)outerExpression, expression);
            case SyntaxKind.TypeAssertionExpression:
                return UpdateTypeAssertion((TypeAssertion)outerExpression, ((TypeAssertion)outerExpression).Type, expression);
            case SyntaxKind.AsExpression:
                return UpdateAsExpression((AsExpression)outerExpression, expression, ((AsExpression)outerExpression).Type);
            case SyntaxKind.SatisfiesExpression:
                return UpdateSatisfiesExpression((SatisfiesExpression)outerExpression, expression, ((SatisfiesExpression)outerExpression).Type);
            case SyntaxKind.NonNullExpression:
                return UpdateNonNullExpression((NonNullExpression)outerExpression, expression);
            case SyntaxKind.ExpressionWithTypeArguments:
                return UpdateExpressionWithTypeArguments((ExpressionWithTypeArguments)outerExpression, expression, ((ExpressionWithTypeArguments)outerExpression).TypeArguments);
            case SyntaxKind.PartiallyEmittedExpression:
                return UpdatePartiallyEmittedExpression((PartiallyEmittedExpression)outerExpression, expression);
        }
        // TypeScript's switch covers every OuterExpression kind and falls off the end otherwise.
        return null!;
    }

    /// <summary>
    /// Determines whether a node is a parenthesized expression that can be ignored when recreating outer expressions.
    ///
    /// A parenthesized expression can be ignored when all of the following are true:
    ///
    /// - It's `pos` and `end` are not -1
    /// - It does not have a custom source map range
    /// - It does not have a custom comment range
    /// - It does not have synthetic leading or trailing comments
    ///
    /// If an outermost parenthesized expression is ignored, but the containing expression requires a parentheses around
    /// the expression to maintain precedence, a new parenthesized expression should be created automatically when
    /// the containing expression is created/updated.
    /// </summary>
    private static bool IsIgnorableParen(Node node)
    {
        return NodeTests.IsParenthesizedExpression(node)
            && Utilities.NodeIsSynthesized(node)
            && Utilities.NodeIsSynthesized(FactoryEmitNode.GetSourceMapRange(node))
            && Utilities.NodeIsSynthesized(FactoryEmitNode.GetCommentRange(node))
            && !Core.Some(FactoryEmitNode.GetSyntheticLeadingComments(node))
            && !Core.Some(FactoryEmitNode.GetSyntheticTrailingComments(node));
    }

    internal Node RestoreOuterExpressions(Node? outerExpression, Node innerExpression, OuterExpressionKinds kinds = OuterExpressionKinds.All)
    {
        if (outerExpression != null && FactoryUtilities.IsOuterExpression(outerExpression, kinds) && !IsIgnorableParen(outerExpression))
        {
            return UpdateOuterExpression(
                outerExpression,
                RestoreOuterExpressions(NodeAccessors.GetExpression(outerExpression), innerExpression));
        }
        return innerExpression;
    }

    //
    // Helpers (end of createNodeFactory)
    //

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(statement))]
    private Node? AsEmbeddedStatement(Node? statement)
    {
        return statement != null && NodeTests.IsNotEmittedStatement(statement) ? FactoryUtilitiesPublic.SetTextRange(setOriginal(CreateEmptyStatement(), statement), statement) : statement;
    }

    /// <summary>
    /// asVariableDeclaration for `BindingName | VariableDeclaration | undefined` (the parser passes nodes; the
    /// `typeof variableDeclaration === "string"` case has no caller with a string and is left out).
    /// </summary>
    private VariableDeclaration? AsVariableDeclaration(Node? variableDeclaration)
    {
        if (variableDeclaration != null && !NodeTests.IsVariableDeclaration(variableDeclaration))
        {
            return CreateVariableDeclaration(
                variableDeclaration,
                /*exclamationToken*/ null,
                /*type*/ null,
                /*initializer*/ null);
        }
        return (VariableDeclaration?)variableDeclaration;
    }
}
