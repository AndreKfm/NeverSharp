// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/transformers/declarations.ts (transformDeclarations)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/transformers/declarations/transform.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D5. One source file, no bundles (no outFile), without
// stripInternal and isolatedDeclarations (not options of Phase 4: their branches are left out). The inferred types come
// from the emit resolver, whose node builder writes them into the emit copy. The fake namespace of an expando
// function's properties is a node builder fake scope (Checker.FakeScope) instead of a synthesized module declaration
// with locals.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>diagnostics.ts SymbolAccessibilityDiagnostic.</summary>
internal sealed class SymbolAccessibilityDiagnostic
{
    internal required DiagnosticMessage DiagnosticMessage;
    internal Node ErrorNode;
    internal Node TypeName;
}

/// <summary>declarations.ts transformDeclarations: the closure of the transformer is the instance (it is the symbol tracker).</summary>
internal sealed partial class DeclarationsTransformer : ISymbolTracker
{
    private const NodeBuilderFlags DeclarationEmitNodeBuilderFlags = NodeBuilderFlags.MultilineObjectLiterals |
        NodeBuilderFlags.WriteClassExpressionAsTypeLiteral |
        NodeBuilderFlags.UseTypeOfFunction |
        NodeBuilderFlags.UseStructuralFallback |
        NodeBuilderFlags.AllowEmptyTuple |
        NodeBuilderFlags.GenerateNamesForShadowedTypeParams |
        NodeBuilderFlags.NoTruncation;

    private const InternalNodeBuilderFlags DeclarationEmitInternalNodeBuilderFlags = InternalNodeBuilderFlags.AllowUnresolvedNames;

    private readonly TransformationContext context;
    private readonly NodeFactory factory;
    private readonly EmitHost host;
    private readonly EmitResolver resolver;
    private readonly CompilerOptions options;

    private Func<SymbolAccessibilityResult, SymbolAccessibilityDiagnostic?> getSymbolAccessibilityDiagnostic;
    private bool needsDeclare = true;
    private bool resultHasExternalModuleIndicator;
    private bool needsScopeFixMarker;
    private bool resultHasScopeMarker;
    private Node enclosingDeclaration;
    private List<Node>? lateMarkedStatements;
    private Dictionary<Node, VisitResult> lateStatementReplacementMap = [];
    private bool suppressNewDiagnosticContexts;
    private Node errorNameNode;
    private Node errorFallbackNode;
    private Action restoreFallbackNode = static () => { };
    private Node currentSourceFile;
    private readonly Func<Node, VisitResult> visitDeclarationSubtree;
    private readonly Func<Node, VisitResult> visitDeclarationStatements;

    private DeclarationsTransformer(TransformationContext context)
    {
        this.context = context;
        factory = context.Factory;
        host = context.GetEmitHost();
        resolver = context.GetEmitResolver();
        options = context.GetCompilerOptions();
        getSymbolAccessibilityDiagnostic = ThrowDiagnostic;
        visitDeclarationSubtree = VisitDeclarationSubtree;
        visitDeclarationStatements = VisitDeclarationStatements;
    }

    private NodeStore store => context.Store;

    /// <summary>transformDeclarations(context): transformRoot.</summary>
    internal static Func<Node, Node> TransformDeclarations(TransformationContext context) => new DeclarationsTransformer(context).TransformRoot;

    private static SymbolAccessibilityDiagnostic? ThrowDiagnostic(SymbolAccessibilityResult result) => throw new InvalidOperationException("Debug Failure. Diagnostic emitted without context");

    // ---- diagnostics ----

    // utilities.ts createDiagnosticForNode for a node of the emit copy (a parse tree node of the file)
    private ProgramDiagnostic CreateDiagnosticForNode(Node node, DiagnosticMessage message, params object?[] args) =>
        resolver.CreateDiagnosticForNode(resolver.ToParseTree(node), message, args);

    private ProgramDiagnostic CreateDiagnosticForNode(NodeRef node, DiagnosticMessage message, params object?[] args) =>
        resolver.CreateDiagnosticForNode(node, message, args);

    // (reportInferenceFallback and reportExpandoFunctionErrors: isolatedDeclarations only, which is not a Phase 4 option)

    private bool HandleSymbolAccessibilityError(SymbolAccessibilityResult symbolAccessibilityResult)
    {
        if (symbolAccessibilityResult.Accessibility == SymbolAccessibility.Accessible)
        {
            // Add aliases back onto the possible imports list if they're not there so we can try them again with updated visibility info
            if (symbolAccessibilityResult.AliasesToMakeVisible is { } aliases)
            {
                if (lateMarkedStatements is null)
                {
                    lateMarkedStatements = [];
                    foreach (var alias in aliases)
                    {
                        lateMarkedStatements.Add(resolver.ToEmit(alias));
                    }
                }
                else
                {
                    foreach (var alias in aliases)
                    {
                        var node = resolver.ToEmit(alias);
                        if (!lateMarkedStatements.Contains(node))
                        {
                            lateMarkedStatements.Add(node);
                        }
                    }
                }
            }
            // TODO: Do all these accessibility checks inside/after the first pass in the checker when declarations are enabled, if possible
        }
        // The checker should issue errors on unresolvable names, skip the declaration emit error for using a private/unreachable name for those
        else if (symbolAccessibilityResult.Accessibility != SymbolAccessibility.NotResolved)
        {
            // Report error
            var errorInfo = getSymbolAccessibilityDiagnostic(symbolAccessibilityResult);
            if (errorInfo is not null)
            {
                var errorNode = !symbolAccessibilityResult.ErrorNode.IsNone ? symbolAccessibilityResult.ErrorNode : resolver.ToParseTree(errorInfo.ErrorNode);
                if (!errorInfo.TypeName.IsNone)
                {
                    context.AddDiagnostic(CreateDiagnosticForNode(errorNode, errorInfo.DiagnosticMessage, resolver.GetTextOfNode(resolver.ToParseTree(errorInfo.TypeName)), symbolAccessibilityResult.ErrorSymbolName, symbolAccessibilityResult.ErrorModuleName));
                }
                else
                {
                    context.AddDiagnostic(CreateDiagnosticForNode(errorNode, errorInfo.DiagnosticMessage, symbolAccessibilityResult.ErrorSymbolName, symbolAccessibilityResult.ErrorModuleName));
                }
                return true;
            }
        }
        return false;
    }

    // ---- ISymbolTracker ----

    public bool TrackSymbol(SymbolRef symbol, NodeRef enclosingDeclaration, Checker.FakeScope? fakeScope, SymbolFlags meaning)
    {
        if ((resolver.SymbolFlagsOf(symbol) & SymbolFlags.TypeParameter) != 0)
        {
            return false;
        }
        var issuedDiagnostic = HandleSymbolAccessibilityError(resolver.IsSymbolAccessible(symbol, enclosingDeclaration, fakeScope, meaning, shouldComputeAliasToMarkVisible: true));
        return issuedDiagnostic;
    }

    private Node ErrorNameOrFallbackNode => !errorNameNode.IsNone ? errorNameNode : errorFallbackNode;

    public void ReportPrivateInBaseOfClassExpression(string propertyName)
    {
        var errorNode = ErrorNameOrFallbackNode;
        if (!errorNode.IsNone)
        {
            var diagnostic = CreateDiagnosticForNode(errorNode, Diagnostics.Property_0_of_exported_anonymous_class_type_may_not_be_private_or_protected, propertyName);
            if (store.Kind(store.Parent(errorNode)) == SyntaxKind.VariableDeclaration)
            {
                Checker.AddRelatedInfo(diagnostic, Checker.AsRelatedInformation(CreateDiagnosticForNode(errorNode, Diagnostics.Add_a_type_annotation_to_the_variable_0, ErrorDeclarationNameWithFallback())));
            }
            context.AddDiagnostic(diagnostic);
        }
    }

    private string ErrorDeclarationNameWithFallback()
    {
        if (!errorNameNode.IsNone)
        {
            return Utilities.DeclarationNameToString(store, errorNameNode);
        }
        if (!errorFallbackNode.IsNone && !Utilities.GetNameOfDeclaration(store, errorFallbackNode).IsNone)
        {
            return Utilities.DeclarationNameToString(store, Utilities.GetNameOfDeclaration(store, errorFallbackNode));
        }
        if (!errorFallbackNode.IsNone && store.Kind(errorFallbackNode) == SyntaxKind.ExportAssignment)
        {
            return store.IsExportEquals(errorFallbackNode) ? "export=" : "default";
        }
        return "(Missing)"; // same fallback declarationNameToString uses when node is zero-width (ie, nameless)
    }

    public void ReportInaccessibleUniqueSymbolError()
    {
        var errorNode = ErrorNameOrFallbackNode;
        if (!errorNode.IsNone)
        {
            context.AddDiagnostic(CreateDiagnosticForNode(errorNode, Diagnostics.The_inferred_type_of_0_references_an_inaccessible_1_type_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback(), "unique symbol"));
        }
    }

    public void ReportCyclicStructureError()
    {
        var errorNode = ErrorNameOrFallbackNode;
        if (!errorNode.IsNone)
        {
            context.AddDiagnostic(CreateDiagnosticForNode(errorNode, Diagnostics.The_inferred_type_of_0_references_a_type_with_a_cyclic_structure_which_cannot_be_trivially_serialized_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback()));
        }
    }

    public void ReportInaccessibleThisError()
    {
        var errorNode = ErrorNameOrFallbackNode;
        if (!errorNode.IsNone)
        {
            context.AddDiagnostic(CreateDiagnosticForNode(errorNode, Diagnostics.The_inferred_type_of_0_references_an_inaccessible_1_type_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback(), "this"));
        }
    }

    public void ReportLikelyUnsafeImportRequiredError(string specifier, string? symbolName)
    {
        var errorNode = ErrorNameOrFallbackNode;
        if (!errorNode.IsNone)
        {
            if (symbolName is not null)
            {
                context.AddDiagnostic(CreateDiagnosticForNode(errorNode, Diagnostics.The_inferred_type_of_0_cannot_be_named_without_a_reference_to_2_from_1_This_is_likely_not_portable_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback(), specifier, symbolName));
            }
            else
            {
                context.AddDiagnostic(CreateDiagnosticForNode(errorNode, Diagnostics.The_inferred_type_of_0_cannot_be_named_without_a_reference_to_1_This_is_likely_not_portable_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback(), specifier));
            }
        }
    }

    public void ReportTruncationError()
    {
        var errorNode = ErrorNameOrFallbackNode;
        if (!errorNode.IsNone)
        {
            context.AddDiagnostic(CreateDiagnosticForNode(errorNode, Diagnostics.The_inferred_type_of_this_node_exceeds_the_maximum_length_the_compiler_will_serialize_An_explicit_type_annotation_is_needed));
        }
    }

    public void ReportNonlocalAugmentation(NodeRef containingFile, SymbolRef parentSymbol, SymbolRef symbol)
    {
        NodeRef primaryDeclaration = NodeRef.None;
        foreach (var d in resolver.DeclarationsOf(parentSymbol) ?? [])
        {
            if (SourceFileOf(d) == containingFile)
            {
                primaryDeclaration = d;
                break;
            }
        }
        if (primaryDeclaration.IsNone || resolver.DeclarationsOf(symbol) is not { } declarations)
        {
            return;
        }
        foreach (var augmentations in declarations)
        {
            if (SourceFileOf(augmentations) == containingFile)
            {
                continue;
            }
            context.AddDiagnostic(Checker.AddRelatedInfo(
                CreateDiagnosticForNode(augmentations, Diagnostics.Declaration_augments_declaration_in_another_file_This_cannot_be_serialized),
                Checker.AsRelatedInformation(CreateDiagnosticForNode(primaryDeclaration, Diagnostics.This_is_the_declaration_being_augmented_Consider_moving_the_augmenting_declaration_into_the_same_file))));
        }
    }

    private NodeRef SourceFileOf(NodeRef node) => new(node.File, Utilities.GetSourceFileOfNode(resolver.StoreOf(node), node.Node));

    public void ReportNonSerializableProperty(string propertyName)
    {
        var errorNode = ErrorNameOrFallbackNode;
        if (!errorNode.IsNone)
        {
            context.AddDiagnostic(CreateDiagnosticForNode(errorNode, Diagnostics.The_type_of_this_node_cannot_be_serialized_because_its_property_0_cannot_be_serialized, propertyName));
        }
    }

    // (isolatedDeclarations only)
    public void ReportInferenceFallback(NodeRef node)
    {
    }

    public void PushErrorFallbackNode(NodeRef node)
    {
        var currentFallback = errorFallbackNode;
        var currentRestore = restoreFallbackNode;
        restoreFallbackNode = () =>
        {
            restoreFallbackNode = currentRestore;
            errorFallbackNode = currentFallback;
        };
        errorFallbackNode = resolver.ToEmit(node);
    }

    public void PopErrorFallbackNode() => restoreFallbackNode();

    // ---- transformRoot ----

    private Node TransformRoot(Node node)
    {
        if (store.Kind(node) == SyntaxKind.SourceFile && store.IsDeclarationFile(node))
        {
            return node;
        }
        // (Bundles: outFile is not a Phase 4 option.)

        // Single source file
        needsDeclare = true;
        needsScopeFixMarker = false;
        resultHasScopeMarker = false;
        enclosingDeclaration = node;
        currentSourceFile = node;
        getSymbolAccessibilityDiagnostic = ThrowDiagnostic;
        resultHasExternalModuleIndicator = false;
        suppressNewDiagnosticContexts = false;
        lateMarkedStatements = null;
        lateStatementReplacementMap = [];
        var rawReferencedFiles = store.ReferencedFiles(node);
        var rawTypeReferenceDirectives = store.TypeReferenceDirectives(node);
        var rawLibReferenceDirectives = store.LibReferenceDirectives(node);
        NodeList combinedStatements;
        if (IsSourceFileJS(node))
        {
            // JavaScript units: transformDeclarationsForJS (getDeclarationStatementsForSourceFile,
            // symbolTableToDeclarationStatements) is not ported yet; the source is left as it is.
            return node;
        }
        else
        {
            var statements = Visitors.VisitNodes(factory, store.Statements(node), visitDeclarationStatements);
            combinedStatements = FactoryUtilitiesPublic.SetTextRange(store, TransformAndReplaceLatePaintedStatements(statements), store.Statements(node));
            if (Utilities.IsExternalModule(store, node) && (!resultHasExternalModuleIndicator || (needsScopeFixMarker && !resultHasScopeMarker)))
            {
                var list = new List<Node>(store.Nodes(combinedStatements).ToArray()) { CreateEmptyExports() };
                combinedStatements = FactoryUtilitiesPublic.SetTextRange(store, factory.CreateNodeArray(list), combinedStatements);
            }
        }
        return factory.UpdateSourceFile(node, combinedStatements, isDeclarationFile: true, GetReferencedFiles(rawReferencedFiles), GetPreserved(rawTypeReferenceDirectives), hasNoDefaultLib: false, GetPreserved(rawLibReferenceDirectives));
    }

    private static bool IsSourceFileJS(NodeStore store, Node file) => store.ScriptKind(file) is ScriptKind.JS or ScriptKind.JSX;

    private bool IsSourceFileJS(Node file) => IsSourceFileJS(store, file);

    // utilities.ts createEmptyExports
    private Node CreateEmptyExports() =>
        factory.CreateExportDeclaration(default, isTypeOnly: false, factory.CreateNamedExports(factory.CreateNodeArray(ReadOnlySpan<Node>.Empty)), default);

    private static FileReference CopyFileReferenceAsSynthetic(FileReference reference) => new()
    {
        Pos = -1,
        End = -1,
        FileName = reference.FileName,
        ResolutionMode = reference.ResolutionMode,
        Preserve = reference.Preserve,
    };

    // getTypeReferences / getLibReferences
    private static List<FileReference> GetPreserved(IReadOnlyList<FileReference> references)
    {
        var result = new List<FileReference>();
        foreach (var reference in references)
        {
            if (reference.Preserve == true)
            {
                result.Add(CopyFileReferenceAsSynthetic(reference));
            }
        }
        return result;
    }

    // getReferencedFiles(outputFilePath): the preserved references to files of the program, relative to the output's
    // directory (host.getSourceFileFromReference: the referenced file among the program's files)
    private List<FileReference> GetReferencedFiles(IReadOnlyList<FileReference> references)
    {
        var result = new List<FileReference>();
        var sourceFileName = store.FileName(currentSourceFile);
        foreach (var reference in references)
        {
            if (reference.Preserve != true)
            {
                continue;
            }
            var referencedFileName = TsPath.GetNormalizedAbsolutePath(TsPath.CombinePaths(GetDirectoryPath(sourceFileName), reference.FileName), host.GetCurrentDirectory());
            SourceFile? file = null;
            foreach (var candidate in host.GetSourceFiles())
            {
                if (string.Equals(candidate.Store.FileName(candidate.Root), referencedFileName, StringComparison.Ordinal))
                {
                    file = candidate;
                    break;
                }
            }
            if (file is null)
            {
                continue;
            }
            string declFileName;
            if (file.Store.IsDeclarationFile(file.Root))
            {
                // Neither decl files or js should have their refs changed
                declFileName = file.Store.FileName(file.Root);
            }
            else
            {
                var paths = Emitter.GetOutputPathsFor(file, host, forceDtsPaths: true);
                declFileName = paths.DeclarationFilePath ?? paths.JsFilePath ?? file.Store.FileName(file.Root);
            }
            var outputFilePath = GetDirectoryPath(TsPath.NormalizeSlashes(Emitter.GetOutputPathsFor(SourceFileOfCurrent(), host, forceDtsPaths: true).DeclarationFilePath!));
            var fileName = GetRelativePathToDirectory(
                TsPath.GetNormalizedAbsolutePath(outputFilePath, host.GetCurrentDirectory()),
                TsPath.GetNormalizedAbsolutePath(declFileName, host.GetCurrentDirectory()));
            var newRef = CopyFileReferenceAsSynthetic(reference);
            newRef.FileName = fileName;
            result.Add(newRef);
        }
        return result;
    }

    private SourceFile SourceFileOfCurrent() => resolver.FileAt(resolver.File);

    // path.ts getDirectoryPath for a normalized path
    private static string GetDirectoryPath(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? "" : index == 0 ? "/" : path[..index];
    }

    // path.ts getRelativePathToDirectoryOrUrl(directory, path, currentDirectory, identity, isAbsolutePathAnUrl: false)
    // for two absolute normalized paths (case-sensitive, as Gate D's host)
    private static string GetRelativePathToDirectory(string directory, string path)
    {
        var from = directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var to = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var start = 0;
        while (start < from.Length && start < to.Length && from[start] == to[start])
        {
            start++;
        }
        var parts = new List<string>();
        for (var i = start; i < from.Length; i++)
        {
            parts.Add("..");
        }
        for (var i = start; i < to.Length; i++)
        {
            parts.Add(to[i]);
        }
        return string.Join("/", parts);
    }

    private NodeList NodeArray(params Node[] nodes) => factory.CreateNodeArray(new List<Node>(nodes));

    // ---- helpers of the transformer ----

    private Node FilterBindingPatternInitializers(Node name)
    {
        if (store.Kind(name) == SyntaxKind.Identifier)
        {
            return name;
        }
        if (store.Kind(name) == SyntaxKind.ArrayBindingPattern)
        {
            return factory.UpdateArrayBindingPattern(name, Visitors.VisitNodes(factory, store.Elements(name), VisitBindingElement));
        }
        return factory.UpdateObjectBindingPattern(name, Visitors.VisitNodes(factory, store.Elements(name), VisitBindingElement));

        VisitResult VisitBindingElement(Node elem)
        {
            if (store.Kind(elem) == SyntaxKind.OmittedExpression)
            {
                return elem;
            }
            var propertyName = store.PropertyName(elem);
            if (!propertyName.IsNone && store.Kind(propertyName) == SyntaxKind.ComputedPropertyName && Utilities.IsEntityNameExpression(store, store.Expression(propertyName)))
            {
                CheckEntityNameVisibility(store.Expression(propertyName), enclosingDeclaration);
            }
            return factory.UpdateBindingElement(
                elem,
                store.DotDotDotToken(elem),
                propertyName,
                FilterBindingPatternInitializers(store.Name(elem)),
                Node.None);
        }
    }

    private Node EnsureParameter(Node p, ModifierFlags? modifierMask = null)
    {
        Func<SymbolAccessibilityResult, SymbolAccessibilityDiagnostic?>? oldDiag = null;
        if (!suppressNewDiagnosticContexts)
        {
            oldDiag = getSymbolAccessibilityDiagnostic;
            getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(p);
        }
        var newParam = factory.UpdateParameterDeclaration(
            p,
            MaskModifiers(p, modifierMask),
            store.DotDotDotToken(p),
            FilterBindingPatternInitializers(store.Name(p)),
            resolver.IsOptionalParameter(p) ? (!store.QuestionToken(p).IsNone ? store.QuestionToken(p) : factory.CreateToken(SyntaxKind.QuestionToken)) : Node.None,
            EnsureType(p, ignorePrivate: true), // Ignore private param props, since this type is going straight back into a param
            EnsureNoInitializer(p));
        if (!suppressNewDiagnosticContexts)
        {
            getSymbolAccessibilityDiagnostic = oldDiag!;
        }
        return newParam;
    }

    private bool ShouldPrintWithInitializer(Node node)
    {
        return CanHaveLiteralInitializer(node)
            && !store.Initializer(node).IsNone
            && resolver.IsLiteralConstDeclaration(UtilitiesPublic.GetParseTreeNode(store, node)); // TODO: Make safea
    }

    private Node EnsureNoInitializer(Node node)
    {
        if (ShouldPrintWithInitializer(node))
        {
            // (unwrappedInitializer / isPrimitiveLiteralValue: only for reportInferenceFallback, isolatedDeclarations)
            return resolver.CreateLiteralConstValue(UtilitiesPublic.GetParseTreeNode(store, node), this);
        }
        return Node.None;
    }

    private Node EnsureType(Node node, bool ignorePrivate = false)
    {
        if (!ignorePrivate && HasEffectiveModifier(node, ModifierFlags.Private))
        {
            // Private nodes emit no types (except private parameter properties, whose parameter types are actually visible)
            return Node.None;
        }
        if (ShouldPrintWithInitializer(node))
        {
            // Literal const declarations will have an initializer ensured rather than a type
            return Node.None;
        }
        var kind = store.Kind(node);
        // Should be removed createTypeOfDeclaration will actually now reuse the existing annotation so there is no real need to duplicate type walking
        // Left in for now to minimize diff during syntactic type node builder refactor
        if (kind != SyntaxKind.ExportAssignment
            && kind != SyntaxKind.BindingElement
            && !store.Type(node).IsNone
            && (kind != SyntaxKind.Parameter || !resolver.RequiresAddingImplicitUndefined(node, enclosingDeclaration)))
        {
            return Visitors.VisitNode(factory, store.Type(node), visitDeclarationSubtree);
        }

        var oldErrorNameNode = errorNameNode;
        errorNameNode = store.Kind(node) == SyntaxKind.ExportAssignment ? Node.None : store.Name(node);
        Func<SymbolAccessibilityResult, SymbolAccessibilityDiagnostic?>? oldDiag = null;
        if (!suppressNewDiagnosticContexts)
        {
            oldDiag = getSymbolAccessibilityDiagnostic;
            if (CanProduceDiagnostics(node))
            {
                getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(node);
            }
        }
        Node typeNode;
        if (HasInferredType(kind))
        {
            typeNode = resolver.CreateTypeOfDeclaration(node, enclosingDeclaration, DeclarationEmitNodeBuilderFlags, DeclarationEmitInternalNodeBuilderFlags, this);
        }
        else if (Utilities.IsFunctionLike(store, node))
        {
            typeNode = resolver.CreateReturnTypeOfSignatureDeclaration(node, enclosingDeclaration, DeclarationEmitNodeBuilderFlags, DeclarationEmitInternalNodeBuilderFlags, this);
        }
        else
        {
            throw new InvalidOperationException("Debug Failure. ensureType: unexpected node kind " + kind);
        }

        errorNameNode = oldErrorNameNode;
        if (!suppressNewDiagnosticContexts)
        {
            getSymbolAccessibilityDiagnostic = oldDiag!;
        }
        return !typeNode.IsNone ? typeNode : factory.CreateToken(SyntaxKind.AnyKeyword);
    }

    // utilities.ts hasInferredType
    private static bool HasInferredType(SyntaxKind kind) => kind is
        SyntaxKind.Parameter or SyntaxKind.PropertySignature or SyntaxKind.PropertyDeclaration or SyntaxKind.BindingElement or
        SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression or SyntaxKind.BinaryExpression or
        SyntaxKind.VariableDeclaration or SyntaxKind.ExportAssignment or SyntaxKind.PropertyAssignment or
        SyntaxKind.ShorthandPropertyAssignment or SyntaxKind.JSDocParameterTag or SyntaxKind.JSDocPropertyTag;

    private bool IsDeclarationAndNotVisible(Node node)
    {
        node = UtilitiesPublic.GetParseTreeNode(store, node);
        switch (store.Kind(node))
        {
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.EnumDeclaration:
                return !resolver.IsDeclarationVisible(node);
            // The following should be doing their own visibility checks based on filtering their members
            case SyntaxKind.VariableDeclaration:
                return !GetBindingNameVisible(node);
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ExportDeclaration:
            case SyntaxKind.ExportAssignment:
                return false;
            case SyntaxKind.ClassStaticBlockDeclaration:
                return true;
        }
        return false;
    }

    // If the ExpandoFunctionDeclaration have multiple overloads, then we only need to emit properties for the last one.
    private bool ShouldEmitFunctionProperties(Node input)
    {
        if (!store.Body(input).IsNone)
        {
            return true;
        }
        var declarations = resolver.DeclarationsOf(resolver.GetSymbolOfDeclaration(input));
        if (declarations is null)
        {
            return true;
        }
        var overloadSignatures = new List<NodeRef>();
        foreach (var decl in declarations)
        {
            var dstore = resolver.StoreOf(decl);
            if (dstore.Kind(decl.Node) == SyntaxKind.FunctionDeclaration && dstore.Body(decl.Node).IsNone)
            {
                overloadSignatures.Add(decl);
            }
        }
        return overloadSignatures.IndexOf(resolver.ToParseTree(input)) == overloadSignatures.Count - 1;
    }

    private bool GetBindingNameVisible(Node elem)
    {
        if (store.Kind(elem) == SyntaxKind.OmittedExpression)
        {
            return false;
        }
        var name = store.Name(elem);
        if (store.Kind(name) is SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern)
        {
            // If any child binding pattern element has been marked visible (usually by collect linked aliases), then this is visible
            foreach (var e in store.Nodes(store.Elements(name)))
            {
                if (GetBindingNameVisible(e))
                {
                    return true;
                }
            }
            return false;
        }
        return resolver.IsDeclarationVisible(elem);
    }

    private NodeList UpdateParamsList(Node node, NodeList parameters, ModifierFlags? modifierMask = null)
    {
        if (HasEffectiveModifier(node, ModifierFlags.Private))
        {
            return factory.CreateNodeArray(ReadOnlySpan<Node>.Empty);
        }
        if (parameters.IsNone)
        {
            return factory.CreateNodeArray(ReadOnlySpan<Node>.Empty);
        }
        var newParams = new List<Node>();
        foreach (var p in store.Nodes(parameters))
        {
            newParams.Add(EnsureParameter(p, modifierMask));
        }
        return factory.CreateNodeArray(newParams, store.HasTrailingComma(parameters));
    }

    private NodeList UpdateAccessorParamsList(Node input, bool isPrivate)
    {
        var newParams = new List<Node>();
        if (!isPrivate)
        {
            var thisParameter = GetThisParameter(input);
            if (!thisParameter.IsNone)
            {
                newParams.Add(EnsureParameter(thisParameter));
            }
        }
        if (store.Kind(input) == SyntaxKind.SetAccessor)
        {
            var newValueParameter = Node.None;
            if (!isPrivate)
            {
                var valueParameter = GetSetAccessorValueParameter(input);
                if (!valueParameter.IsNone)
                {
                    newValueParameter = EnsureParameter(valueParameter);
                }
            }
            if (newValueParameter.IsNone)
            {
                newValueParameter = factory.CreateParameterDeclaration(default, default, factory.CreateIdentifier("value"), default, default, default);
            }
            newParams.Add(newValueParameter);
        }
        return factory.CreateNodeArray(newParams);
    }

    // utilities.ts getThisParameter
    private Node GetThisParameter(Node signature)
    {
        var parameters = store.Nodes(store.Parameters(signature));
        if (parameters.Length > 0 && TransformerUtilities.ParameterIsThisKeyword(store, parameters[0]))
        {
            return parameters[0];
        }
        return Node.None;
    }

    // utilities.ts getSetAccessorValueParameter
    private Node GetSetAccessorValueParameter(Node accessor)
    {
        var parameters = store.Nodes(store.Parameters(accessor));
        if (parameters.Length > 0)
        {
            var hasThis = parameters.Length == 2 && TransformerUtilities.ParameterIsThisKeyword(store, parameters[0]);
            return parameters[hasThis ? 1 : 0];
        }
        return Node.None;
    }

    private NodeList EnsureTypeParams(Node node, NodeList parameters) =>
        HasEffectiveModifier(node, ModifierFlags.Private) ? NodeList.None : Visitors.VisitNodes(factory, parameters, visitDeclarationSubtree);

    private bool IsEnclosingDeclaration(Node node)
    {
        var kind = store.Kind(node);
        return kind is SyntaxKind.SourceFile or SyntaxKind.TypeAliasDeclaration or SyntaxKind.ModuleDeclaration or SyntaxKind.ClassDeclaration
            or SyntaxKind.InterfaceDeclaration or SyntaxKind.IndexSignature or SyntaxKind.MappedType
            || Utilities.IsFunctionLike(store, node);
    }

    private void CheckEntityNameVisibility(Node entityName, Node enclosingDeclaration)
    {
        var visibilityResult = resolver.IsEntityNameVisible(entityName, enclosingDeclaration);
        HandleSymbolAccessibilityError(visibilityResult);
    }

    private Node PreserveJsDoc(Node updated, Node original)
    {
        // (updated.jsDoc = original.jsDoc: the printer does not read jsDoc nodes)
        return FactoryEmitNode.SetCommentRange(store, updated, FactoryEmitNode.GetCommentRange(store, original));
    }

    private Node RewriteModuleSpecifier(Node parent, Node input)
    {
        if (input.IsNone)
        {
            return Node.None; // TODO: GH#18217
        }
        resultHasExternalModuleIndicator = resultHasExternalModuleIndicator || (store.Kind(parent) != SyntaxKind.ModuleDeclaration && store.Kind(parent) != SyntaxKind.ImportType);
        // (isBundledEmit is false: outFile is not a Phase 4 option)
        return input;
    }

    private VisitResult TransformImportEqualsDeclaration(Node decl)
    {
        if (!resolver.IsDeclarationVisible(decl))
        {
            return VisitResult.None;
        }
        var moduleReference = store.ModuleReference(decl);
        if (store.Kind(moduleReference) == SyntaxKind.ExternalModuleReference)
        {
            // Rewrite external module names if necessary
            var specifier = store.Expression(moduleReference);
            return factory.UpdateImportEqualsDeclaration(
                decl,
                store.Modifiers(decl),
                store.IsTypeOnly(decl),
                store.Name(decl),
                factory.UpdateExternalModuleReference(moduleReference, RewriteModuleSpecifier(decl, specifier)));
        }
        var oldDiag = getSymbolAccessibilityDiagnostic;
        getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(decl);
        CheckEntityNameVisibility(moduleReference, enclosingDeclaration);
        getSymbolAccessibilityDiagnostic = oldDiag;
        return decl;
    }

    private VisitResult TransformImportDeclaration(Node decl)
    {
        var importClause = store.ImportClause(decl);
        if (importClause.IsNone)
        {
            // import "mod" - possibly needed for side effects? (global interface patches, module augmentations, etc)
            return factory.UpdateImportDeclaration(
                decl,
                store.Modifiers(decl),
                importClause,
                RewriteModuleSpecifier(decl, store.ModuleSpecifier(decl)),
                TryGetResolutionModeOverride(store.Attributes(decl)));
        }
        var phase = store.PhaseModifier(importClause);
        SyntaxKind? phaseModifier = phase == SyntaxKind.DeferKeyword || phase == SyntaxKind.Unknown ? null : phase;
        // The `importClause` visibility corresponds to the default's visibility.
        var visibleDefaultBinding = !store.Name(importClause).IsNone && resolver.IsDeclarationVisible(importClause) ? store.Name(importClause) : Node.None;
        var namedBindings = store.NamedBindings(importClause);
        if (namedBindings.IsNone)
        {
            // No named bindings (either namespace or list), meaning the import is just default or should be elided
            return visibleDefaultBinding.IsNone ? VisitResult.None : factory.UpdateImportDeclaration(
                decl,
                store.Modifiers(decl),
                factory.UpdateImportClause(importClause, phaseModifier, visibleDefaultBinding, Node.None),
                RewriteModuleSpecifier(decl, store.ModuleSpecifier(decl)),
                TryGetResolutionModeOverride(store.Attributes(decl)));
        }
        if (store.Kind(namedBindings) == SyntaxKind.NamespaceImport)
        {
            // Namespace import (optionally with visible default)
            var visibleNamedBindings = resolver.IsDeclarationVisible(namedBindings) ? namedBindings : Node.None;
            return !visibleDefaultBinding.IsNone || !visibleNamedBindings.IsNone ? factory.UpdateImportDeclaration(
                decl,
                store.Modifiers(decl),
                factory.UpdateImportClause(importClause, phaseModifier, visibleDefaultBinding, visibleNamedBindings),
                RewriteModuleSpecifier(decl, store.ModuleSpecifier(decl)),
                TryGetResolutionModeOverride(store.Attributes(decl))) : VisitResult.None;
        }
        // Named imports (optionally with visible default)
        var bindingList = new List<Node>();
        foreach (var b in store.Nodes(store.Elements(namedBindings)))
        {
            if (resolver.IsDeclarationVisible(b))
            {
                bindingList.Add(b);
            }
        }
        if (bindingList.Count > 0 || !visibleDefaultBinding.IsNone)
        {
            return factory.UpdateImportDeclaration(
                decl,
                store.Modifiers(decl),
                factory.UpdateImportClause(
                    importClause,
                    phaseModifier,
                    visibleDefaultBinding,
                    bindingList.Count > 0 ? factory.UpdateNamedImports(namedBindings, factory.CreateNodeArray(bindingList)) : Node.None),
                RewriteModuleSpecifier(decl, store.ModuleSpecifier(decl)),
                TryGetResolutionModeOverride(store.Attributes(decl)));
        }
        // Augmentation of export depends on import
        if (resolver.IsImportRequiredByAugmentation(decl))
        {
            return factory.UpdateImportDeclaration(
                decl,
                store.Modifiers(decl),
                Node.None,
                RewriteModuleSpecifier(decl, store.ModuleSpecifier(decl)),
                TryGetResolutionModeOverride(store.Attributes(decl)));
        }
        // Nothing visible
        return VisitResult.None;
    }

    private Node TryGetResolutionModeOverride(Node node)
    {
        return !node.IsNone && GetResolutionModeOverride(node) is not null ? node : Node.None;
    }

    // program.ts getResolutionModeOverride (without its grammar errors)
    private ModuleKind? GetResolutionModeOverride(Node node)
    {
        var elements = store.Nodes(store.Elements(node));
        if (elements.Length != 1)
        {
            return null;
        }
        var elem = elements[0];
        var name = store.Name(elem);
        if (!UtilitiesPublic.IsStringLiteralLike(store, name) || !store.Texts.Equals(store.Text(name), "resolution-mode"))
        {
            return null;
        }
        var value = store.Value(elem);
        if (!UtilitiesPublic.IsStringLiteralLike(store, value))
        {
            return null;
        }
        return store.Texts.Equals(store.Text(value), "import") ? ModuleKind.ESNext
            : store.Texts.Equals(store.Text(value), "require") ? ModuleKind.CommonJS
            : null;
    }

    private NodeList TransformAndReplaceLatePaintedStatements(NodeList statements)
    {
        // This is a `while` loop because `handleSymbolAccessibilityError` can see additional import aliases marked as visible during
        // error handling which must now be included in the output and themselves checked for errors.
        // For example:
        // ```
        // module A {
        //   export module Q {}
        //   import B = Q;
        //   import C = B;
        //   export import D = C;
        // }
        // ```
        // In such a scenario, only Q and D are initially visible, but we don't consider imports as private names - instead we say they if they are referenced they must
        // be recorded. So while checking D's visibility we mark C as visible, then we must check C which in turn marks B, completing the chain of
        // dependent imports and allowing a valid declaration file output. Today, this dependent alias marking only happens for internal import aliases.
        while (lateMarkedStatements is { Count: > 0 })
        {
            var i = lateMarkedStatements[0];
            lateMarkedStatements.RemoveAt(0);
            if (!IsLateVisibilityPaintedStatement(i))
            {
                throw new InvalidOperationException("Debug Failure. Late replaced statement was found which is not handled by the declaration transformer!: " + store.Kind(i));
            }
            var priorNeedsDeclare = needsDeclare;
            var parent = store.Parent(i);
            needsDeclare = !parent.IsNone && store.Kind(parent) == SyntaxKind.SourceFile; // (&& !(isExternalModule(i.parent) && isBundledEmit))
            var result = TransformTopLevelDeclaration(i);
            needsDeclare = priorNeedsDeclare;
            lateStatementReplacementMap[OriginalNodeKey(i)] = result;
        }

        // And lastly, we need to get the final form of all those indetermine import declarations from before and add them to the output list
        // (and remove them from the set to examine for outter declarations)
        return Visitors.VisitNodes(factory, statements, VisitLateVisibilityMarkedStatements);

        VisitResult VisitLateVisibilityMarkedStatements(Node statement)
        {
            if (IsLateVisibilityPaintedStatement(statement))
            {
                var key = OriginalNodeKey(statement);
                if (lateStatementReplacementMap.Remove(key, out var result))
                {
                    if (!result.IsNone)
                    {
                        if (result.Nodes is { } nodes ? Core.Some(nodes, NeedsScopeMarker) : NeedsScopeMarker(result.Node))
                        {
                            // Top-level declarations in .d.ts files are always considered exported even without a modifier unless there's an export assignment or specifier
                            needsScopeFixMarker = true;
                        }
                        if (store.Kind(store.Parent(statement)) == SyntaxKind.SourceFile && (result.Nodes is { } nodes2 ? Core.Some(nodes2, IsExternalModuleIndicator) : IsExternalModuleIndicator(result.Node)))
                        {
                            resultHasExternalModuleIndicator = true;
                        }
                    }
                    return result;
                }
            }
            return statement;
        }
    }

    // getOriginalNodeId(node): the original node's handle
    private Node OriginalNodeKey(Node node) => TransformerUtilities.GetOriginalNode(store, node);

    // utilities.ts isLateVisibilityPaintedStatement
    private bool IsLateVisibilityPaintedStatement(Node node) => store.Kind(node) is
        SyntaxKind.ImportDeclaration or SyntaxKind.ImportEqualsDeclaration or SyntaxKind.VariableStatement or SyntaxKind.ClassDeclaration or
        SyntaxKind.FunctionDeclaration or SyntaxKind.ModuleDeclaration or SyntaxKind.TypeAliasDeclaration or SyntaxKind.InterfaceDeclaration or
        SyntaxKind.EnumDeclaration;

    // utilities.ts needsScopeMarker
    private bool NeedsScopeMarker(Node result)
    {
        var kind = store.Kind(result);
        return !(kind is SyntaxKind.ImportDeclaration or SyntaxKind.ImportEqualsDeclaration or SyntaxKind.ExportDeclaration) &&
            kind != SyntaxKind.ExportAssignment &&
            !Utilities.HasSyntacticModifier(store, result, ModifierFlags.Export) &&
            !Utilities.IsAmbientModule(store, result);
    }

    // utilities.ts isExternalModuleIndicator
    private bool IsExternalModuleIndicator(Node result)
    {
        return store.Kind(result) is SyntaxKind.ImportDeclaration or SyntaxKind.ImportEqualsDeclaration or SyntaxKind.ExportDeclaration or SyntaxKind.ExportAssignment
            || Utilities.HasSyntacticModifier(store, result, ModifierFlags.Export);
    }

    private bool HasEffectiveModifier(Node node, ModifierFlags flags) => (Utilities.GetEffectiveModifierFlags(store, node) & flags) != 0;
}
