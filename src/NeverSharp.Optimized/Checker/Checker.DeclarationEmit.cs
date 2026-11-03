// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (isSymbolAccessible, hasVisibleDeclarations, isEntityNameVisible with aliases to
// make visible, collectLinkedAliases, SymbolTrackerImpl, withContext and the emit resolver's declaration-emit entries)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/emitresolver.go and internal/checker/nodebuilderimpl.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D5. The declaration transformer's symbol tracker is an
// ISymbolTracker; the node builder writes the nodes of a resolver entry directly into the emit copy of the file's store
// (EnterEmitBuilder/ExitEmitBuilder swap the builder's store, its serializedTypes cache and its reused-node marks).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>types.ts SymbolAccessibilityResult (with SymbolVisibilityResult's aliasesToMakeVisible).</summary>
internal sealed class SymbolAccessibilityResult
{
    internal SymbolAccessibility Accessibility;
    internal List<NodeRef>? AliasesToMakeVisible;
    internal string? ErrorSymbolName;
    internal string? ErrorModuleName;
    internal NodeRef ErrorNode;

    internal static SymbolAccessibilityResult Accessible() => new() { Accessibility = SymbolAccessibility.Accessible };
}

/// <summary>
/// types.ts SymbolTracker as the declaration transformer implements it. The enclosing declaration of trackSymbol may be a
/// node builder fake scope (TypeScript's synthesized Block with locals).
/// </summary>
internal interface ISymbolTracker
{
    bool TrackSymbol(SymbolRef symbol, NodeRef enclosingDeclaration, Checker.FakeScope? fakeScope, SymbolFlags meaning);
    void ReportInaccessibleThisError();
    void ReportPrivateInBaseOfClassExpression(string propertyName);
    void ReportInaccessibleUniqueSymbolError();
    void ReportCyclicStructureError();
    void ReportLikelyUnsafeImportRequiredError(string specifier, string? symbolName);
    void ReportTruncationError();
    void ReportNonlocalAugmentation(NodeRef containingFile, SymbolRef parentSymbol, SymbolRef augmentingSymbol);
    void ReportNonSerializableProperty(string propertyName);
    void ReportInferenceFallback(NodeRef node);
    void PushErrorFallbackNode(NodeRef node);
    void PopErrorFallbackNode();
}

internal sealed partial class Checker
{
    /// <summary>checker.ts TrackedSymbol: [symbol, enclosingDeclaration, meaning].</summary>
    internal readonly record struct TrackedSymbol(SymbolRef Symbol, NodeRef EnclosingDeclaration, FakeScope? FakeScope, SymbolFlags Meaning);

    /// <summary>checker.ts SymbolTrackerImpl.</summary>
    private sealed class SymbolTrackerImpl
    {
        private readonly Checker checker;
        private readonly NodeBuilderContext context;
        internal readonly ISymbolTracker? Inner;
        internal bool DisableTrackSymbol { get; set; }

        internal SymbolTrackerImpl(Checker checker, NodeBuilderContext context, ISymbolTracker? tracker)
        {
            this.checker = checker;
            Inner = tracker;
            this.context = context;
        }

        internal bool CanTrackSymbol => Inner is not null;

        internal bool TrackSymbol(SymbolRef symbol, NodeRef enclosingDeclaration, FakeScope? fakeScope, SymbolFlags meaning)
        {
            if (Inner is not null && !DisableTrackSymbol)
            {
                if (Inner.TrackSymbol(symbol, enclosingDeclaration, fakeScope, meaning))
                {
                    OnDiagnosticReported();
                    return true;
                }
                // Skip recording type parameters as they dont contribute to late painted statements
                if ((checker.SymbolFlagsOf(symbol) & SymbolFlags.TypeParameter) == 0)
                {
                    (context.TrackedSymbols ??= []).Add(new TrackedSymbol(symbol, enclosingDeclaration, fakeScope, meaning));
                }
            }
            return false;
        }

        internal void ReportInaccessibleThisError()
        {
            if (Inner is not null)
            {
                OnDiagnosticReported();
                Inner.ReportInaccessibleThisError();
            }
        }

        internal void ReportPrivateInBaseOfClassExpression(string propertyName)
        {
            if (Inner is not null)
            {
                OnDiagnosticReported();
                Inner.ReportPrivateInBaseOfClassExpression(propertyName);
            }
        }

        internal void ReportInaccessibleUniqueSymbolError()
        {
            if (Inner is not null)
            {
                OnDiagnosticReported();
                Inner.ReportInaccessibleUniqueSymbolError();
            }
        }

        internal void ReportCyclicStructureError()
        {
            if (Inner is not null)
            {
                OnDiagnosticReported();
                Inner.ReportCyclicStructureError();
            }
        }

        internal void ReportLikelyUnsafeImportRequiredError(string specifier, string? symbolName)
        {
            if (Inner is not null)
            {
                OnDiagnosticReported();
                Inner.ReportLikelyUnsafeImportRequiredError(specifier, symbolName);
            }
        }

        internal void ReportTruncationError()
        {
            if (Inner is not null)
            {
                OnDiagnosticReported();
                Inner.ReportTruncationError();
            }
        }

        internal void ReportNonlocalAugmentation(NodeRef containingFile, SymbolRef parentSymbol, SymbolRef augmentingSymbol)
        {
            if (Inner is not null)
            {
                OnDiagnosticReported();
                Inner.ReportNonlocalAugmentation(containingFile, parentSymbol, augmentingSymbol);
            }
        }

        internal void ReportNonSerializableProperty(string propertyName)
        {
            if (Inner is not null)
            {
                OnDiagnosticReported();
                Inner.ReportNonSerializableProperty(propertyName);
            }
        }

        private void OnDiagnosticReported() => context.ReportedDiagnostic = true;

        internal void ReportInferenceFallback(NodeRef node)
        {
            if (Inner is not null && !context.SuppressReportInferenceFallback)
            {
                OnDiagnosticReported();
                Inner.ReportInferenceFallback(node);
            }
        }

        internal void PushErrorFallbackNode(NodeRef node) => Inner?.PushErrorFallbackNode(node);

        internal void PopErrorFallbackNode() => Inner?.PopErrorFallbackNode();
    }

    /// <summary>
    /// createRecoveryBoundary's tracker: tracked symbols are kept until the boundary is finalized and the reports are
    /// deferred as unreported errors (each marks the boundary's error).
    /// </summary>
    private sealed class RecoveryBoundaryTracker(NodeBuilderContext context, SymbolTrackerImpl oldTracker) : ISymbolTracker
    {
        internal readonly List<TrackedSymbol> TrackedSymbols = [];
        internal readonly List<Action> UnreportedErrors = [];

        private void MarkError(Action unreportedError)
        {
            Checker.MarkError(context);
            UnreportedErrors.Add(unreportedError);
        }

        public bool TrackSymbol(SymbolRef symbol, NodeRef enclosingDeclaration, FakeScope? fakeScope, SymbolFlags meaning)
        {
            TrackedSymbols.Add(new TrackedSymbol(symbol, enclosingDeclaration, fakeScope, meaning));
            return false;
        }

        public void ReportInaccessibleThisError() => MarkError(oldTracker.ReportInaccessibleThisError);
        public void ReportPrivateInBaseOfClassExpression(string propertyName) => MarkError(() => oldTracker.ReportPrivateInBaseOfClassExpression(propertyName));
        public void ReportInaccessibleUniqueSymbolError() => MarkError(oldTracker.ReportInaccessibleUniqueSymbolError);
        public void ReportCyclicStructureError() => MarkError(oldTracker.ReportCyclicStructureError);
        public void ReportLikelyUnsafeImportRequiredError(string specifier, string? symbolName) => MarkError(() => oldTracker.ReportLikelyUnsafeImportRequiredError(specifier, symbolName));
        public void ReportNonSerializableProperty(string propertyName) => MarkError(() => oldTracker.ReportNonSerializableProperty(propertyName));

        // (...oldTracker.inner: the rest go to the inner tracker directly)
        public void ReportTruncationError() => oldTracker.Inner?.ReportTruncationError();
        public void ReportNonlocalAugmentation(NodeRef containingFile, SymbolRef parentSymbol, SymbolRef augmentingSymbol) => oldTracker.Inner?.ReportNonlocalAugmentation(containingFile, parentSymbol, augmentingSymbol);
        public void ReportInferenceFallback(NodeRef node) => oldTracker.Inner?.ReportInferenceFallback(node);
        public void PushErrorFallbackNode(NodeRef node) => oldTracker.Inner?.PushErrorFallbackNode(node);
        public void PopErrorFallbackNode() => oldTracker.Inner?.PopErrorFallbackNode();
    }

    // ---- the node builder for the emit resolver (nodes written into the emit copy of the file's store) ----

    // NodeLinks.serializedTypes for the emit copy the builder writes into (its nodes live in that store)
    private Dictionary<(NodeRef, FakeScope?, Type, NodeBuilderFlags, InternalNodeBuilderFlags), (Node Node, bool Truncating, int AddedLength, List<TrackedSymbol>? TrackedSymbols)> emitSerializedTypes = [];
    private NodeStore? emitSerializedTypesStore;

    private readonly record struct SavedSynthState(
        NodeStore? Store, NodeStore.Mark Mark, int Depth, int CacheCount, int ReusedCount,
        Dictionary<(NodeRef, FakeScope?, Type, NodeBuilderFlags, InternalNodeBuilderFlags), (Node Node, bool Truncating, int AddedLength, List<TrackedSymbol>? TrackedSymbols)> SerializedTypes,
        HashSet<Node> ReusedNodes, List<Node> ReusedNodesInOrder, NodeStore? BuilderEnclosingStore, bool EmitBuilderActive);

    // Makes the builder write into the emit copy: nothing written there is truncated away (depth stays above zero).
    private SavedSynthState EnterEmitBuilder(NodeStore emitStore)
    {
        var saved = new SavedSynthState(synthStore, synthMark, synthDepth, synthCacheCount, synthReusedCount, serializedTypes, reusedNodes, reusedNodesInOrder, builderEnclosingStore, emitBuilderActive);
        emitBuilderActive = true;
        if (!ReferenceEquals(emitSerializedTypesStore, emitStore))
        {
            emitSerializedTypes = [];
            emitSerializedTypesStore = emitStore;
        }
        synthStore = emitStore;
        synthDepth = 1;
        serializedTypes = emitSerializedTypes;
        reusedNodes = [];
        reusedNodesInOrder = [];
        return saved;
    }

    private void ExitEmitBuilder(SavedSynthState saved)
    {
        synthStore = saved.Store;
        synthMark = saved.Mark;
        synthDepth = saved.Depth;
        synthCacheCount = saved.CacheCount;
        synthReusedCount = saved.ReusedCount;
        serializedTypes = saved.SerializedTypes;
        reusedNodes = saved.ReusedNodes;
        reusedNodesInOrder = saved.ReusedNodesInOrder;
        builderEnclosingStore = saved.BuilderEnclosingStore;
        emitBuilderActive = saved.EmitBuilderActive;
    }

    // checker.ts withContext for the emit resolver's node builder calls; none when the context encountered an error
    private Node WithEmitContext(NodeStore emitStore, NodeRef enclosingDeclaration, FakeScope? fakeScope, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker? tracker, Func<NodeBuilderContext, Node> cb)
    {
        var saved = EnterEmitBuilder(emitStore);
        try
        {
            BeginSynth();
            try
            {
                var context = CreateNodeBuilderContext(enclosingDeclaration, flags, internalFlags);
                context.FakeScope = fakeScope;
                context.Tracker = new SymbolTrackerImpl(this, context, tracker);
                var resultingNode = cb(context);
                if (context.Truncating && (context.Flags & NodeBuilderFlags.NoTruncation) != 0)
                {
                    context.Tracker.ReportTruncationError();
                }
                return context.EncounteredError ? Node.None : resultingNode;
            }
            finally
            {
                EndSynth();
            }
        }
        finally
        {
            ExitEmitBuilder(saved);
        }
    }

    /// <summary>checker.ts createTypeOfDeclaration (declaration: a parse tree node with an inferred type); none: any.</summary>
    internal Node CreateTypeOfDeclaration(NodeStore emitStore, NodeRef declaration, NodeRef enclosingDeclaration, FakeScope? fakeScope, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker tracker)
    {
        // Get type of the symbol if this is the valid symbol otherwise get type at location
        var symbol = GetSymbolOfDeclaration(declaration);
        return WithEmitContext(emitStore, enclosingDeclaration, fakeScope, flags | NodeBuilderFlags.MultilineObjectLiterals, internalFlags, tracker,
            context => SerializeTypeOfDeclarationSyntactic(declaration, symbol, context));
    }

    /// <summary>checker.ts createReturnTypeOfSignatureDeclaration (a parse tree signature declaration).</summary>
    internal Node CreateReturnTypeOfSignatureDeclaration(NodeStore emitStore, NodeRef signatureDeclaration, NodeRef enclosingDeclaration, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker tracker)
    {
        return WithEmitContext(emitStore, enclosingDeclaration, null, flags | NodeBuilderFlags.MultilineObjectLiterals, internalFlags, tracker,
            context => SerializeReturnTypeForSignatureSyntactic(signatureDeclaration, GetSymbolOfDeclaration(signatureDeclaration), context));
    }

    /// <summary>checker.ts createTypeOfExpression (a parse tree expression).</summary>
    internal Node CreateTypeOfExpression(NodeStore emitStore, NodeRef expr, NodeRef enclosingDeclaration, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker tracker)
    {
        return WithEmitContext(emitStore, enclosingDeclaration, null, flags | NodeBuilderFlags.MultilineObjectLiterals, internalFlags, tracker,
            context => SerializeTypeOfExpression(expr, context));
    }

    /// <summary>
    /// checker.ts createLiteralConstValue: literalTypeToNode(getTypeOfSymbol(getSymbolOfDeclaration(node)), node, tracker),
    /// written into the emit copy.
    /// </summary>
    internal Node CreateLiteralConstValue(NodeStore emitStore, NodeRef node, ISymbolTracker tracker)
    {
        var type = GetTypeOfSymbol(GetSymbolOfDeclaration(node));
        if ((Types.Flags(type) & TypeFlags.EnumLike) != 0)
        {
            var enumResult = WithEmitContext(emitStore, node, null, NodeBuilderFlags.None, InternalNodeBuilderFlags.None, tracker,
                context => SymbolToExpression(Types.Record(type).Symbol, context, SymbolFlags.Value));
            if (!enumResult.IsNone)
            {
                return enumResult;
            }
        }
        var saved = EnterEmitBuilder(emitStore);
        try
        {
            if (type == trueType)
            {
                return SToken(SyntaxKind.TrueKeyword);
            }
            if (type == falseType)
            {
                return SToken(SyntaxKind.FalseKeyword);
            }
            var literalValue = Types.Literal(type).Value;
            switch (literalValue)
            {
                case string s when (Types.Flags(type) & TypeFlags.BigIntLiteral) != 0:
                {
                    var literal = SNew(SyntaxKind.BigIntLiteral);
                    S.SetText(literal, S.Texts.Add(s + "n"));
                    return literal;
                }
                case string s:
                    return SStringLiteral(s);
                case double d:
                    return d < 0 ? SPrefixUnaryExpression(SyntaxKind.MinusToken, SNumericLiteral(-d)) : SNumericLiteral(d);
                default:
                    throw new InvalidOperationException("createLiteralConstValue: unexpected literal value");
            }
        }
        finally
        {
            ExitEmitBuilder(saved);
        }
    }

    /// <summary>checker.ts isExpandoFunctionDeclaration (a parse tree function or variable declaration).</summary>
    internal bool IsExpandoFunctionDeclaration(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        SymbolRef symbol;
        if (store.Kind(declaration.Node) == SyntaxKind.VariableDeclaration)
        {
            if (!store.Type(declaration.Node).IsNone || (!Utilities.IsInJSFile(store, declaration.Node) && !IsVarConstLike(declaration)))
            {
                return false;
            }
            var initializer = GetDeclaredExpandoInitializer(declaration);
            if (initializer.IsNone || !CanHaveSymbolKind(Kind(initializer)))
            {
                return false;
            }
            symbol = GetSymbolOfDeclaration(initializer);
        }
        else if (store.Kind(declaration.Node) == SyntaxKind.FunctionDeclaration)
        {
            symbol = GetSymbolOfDeclaration(declaration);
        }
        else
        {
            return false;
        }
        // (`symbol.flags & SymbolFlags.Function | SymbolFlags.Variable` is always truthy)
        if (symbol.IsNone)
        {
            return false;
        }
        var exports = GetExportsOfSymbol(symbol);
        for (var i = 0; i < exports.Count; i++)
        {
            var p = exports.ValueAt(i);
            if ((SymbolFlagsOf(p) & SymbolFlags.Value) != 0 && IsExpandoPropertyDeclaration(ValueDeclarationOf(p)))
            {
                return true;
            }
        }
        return false;
    }

    // utilities.ts isExpandoPropertyDeclaration
    internal bool IsExpandoPropertyDeclaration(NodeRef declaration) =>
        !declaration.IsNone && Kind(declaration) is SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression or SyntaxKind.BinaryExpression;

    // utilities.ts canHaveSymbol (the kinds of an expando initializer)
    private static bool CanHaveSymbolKind(SyntaxKind kind) => kind is
        SyntaxKind.ArrowFunction or SyntaxKind.BinaryExpression or SyntaxKind.BindingElement or SyntaxKind.CallExpression or
        SyntaxKind.CallSignature or SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression or SyntaxKind.ClassStaticBlockDeclaration or
        SyntaxKind.Constructor or SyntaxKind.ConstructorType or SyntaxKind.ConstructSignature or SyntaxKind.ElementAccessExpression or
        SyntaxKind.EnumDeclaration or SyntaxKind.EnumMember or SyntaxKind.ExportAssignment or SyntaxKind.ExportDeclaration or
        SyntaxKind.ExportSpecifier or SyntaxKind.FunctionDeclaration or SyntaxKind.FunctionExpression or SyntaxKind.FunctionType or
        SyntaxKind.GetAccessor or SyntaxKind.Identifier or SyntaxKind.ImportClause or SyntaxKind.ImportEqualsDeclaration or
        SyntaxKind.ImportSpecifier or SyntaxKind.IndexSignature or SyntaxKind.InterfaceDeclaration or SyntaxKind.JSDocCallbackTag or
        SyntaxKind.JSDocEnumTag or SyntaxKind.JSDocFunctionType or SyntaxKind.JSDocParameterTag or SyntaxKind.JSDocPropertyTag or
        SyntaxKind.JSDocSignature or SyntaxKind.JSDocTypedefTag or SyntaxKind.JSDocTypeLiteral or SyntaxKind.JsxAttribute or
        SyntaxKind.JsxAttributes or SyntaxKind.JsxSpreadAttribute or SyntaxKind.MappedType or SyntaxKind.MethodDeclaration or
        SyntaxKind.MethodSignature or SyntaxKind.ModuleDeclaration or SyntaxKind.NamedTupleMember or SyntaxKind.NamespaceExport or
        SyntaxKind.NamespaceExportDeclaration or SyntaxKind.NamespaceImport or SyntaxKind.NewExpression or
        SyntaxKind.NoSubstitutionTemplateLiteral or SyntaxKind.NumericLiteral or SyntaxKind.ObjectLiteralExpression or
        SyntaxKind.Parameter or SyntaxKind.PropertyAccessExpression or SyntaxKind.PropertyAssignment or SyntaxKind.PropertyDeclaration or
        SyntaxKind.PropertySignature or SyntaxKind.SetAccessor or SyntaxKind.ShorthandPropertyAssignment or SyntaxKind.SourceFile or
        SyntaxKind.SpreadAssignment or SyntaxKind.StringLiteral or SyntaxKind.TypeAliasDeclaration or SyntaxKind.TypeLiteral or
        SyntaxKind.TypeParameter or SyntaxKind.VariableDeclaration;

    /// <summary>checker.ts getPropertiesOfContainerFunction (a parse tree function declaration).</summary>
    internal List<SymbolRef> GetPropertiesOfContainerFunction(NodeRef declaration)
    {
        if (declaration.IsNone || Kind(declaration) != SyntaxKind.FunctionDeclaration)
        {
            return [];
        }
        var symbol = GetSymbolOfDeclaration(declaration);
        return symbol.IsNone ? [] : [.. GetPropertiesOfType(GetTypeOfSymbol(symbol))];
    }

    /// <summary>The resolver's isLateBound (a parse tree declaration).</summary>
    internal bool IsLateBoundDeclaration(NodeRef node)
    {
        var symbol = GetSymbolOfDeclaration(node);
        return !symbol.IsNone && (GetCheckFlags(symbol) & CheckFlags.Late) != 0;
    }

    /// <summary>The resolver's isImportRequiredByAugmentation (a parse tree import declaration).</summary>
    internal bool IsImportRequiredByAugmentation(NodeRef node)
    {
        var file = new NodeRef(node.File, Utilities.GetSourceFileOfNode(StoreOf(node), node.Node));
        var fileSymbol = GetSymbolOfDeclaration(file);
        if (fileSymbol.IsNone)
        {
            return false;
        }
        var importTarget = GetExternalModuleFileFromDeclaration(node);
        if (importTarget.IsNone)
        {
            return false;
        }
        if (importTarget == file)
        {
            return false;
        }
        var exports = GetExportsOfModule(fileSymbol);
        for (var i = 0; i < exports.Count; i++)
        {
            var s = exports.ValueAt(i);
            if (HasMergeId(s))
            {
                var merged = GetMergedSymbol(s);
                foreach (var d in DeclarationsOf(merged) ?? [])
                {
                    var declFile = new NodeRef(d.File, Utilities.GetSourceFileOfNode(StoreOf(d), d.Node));
                    if (declFile == importTarget)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>
    /// The resolver's createLateBoundIndexSignatures (cls: a parse tree class declaration): the index signatures of the
    /// class without declarations (from late-bound members), written into the emit copy; null when there are none.
    /// </summary>
    internal List<Node>? CreateLateBoundIndexSignatures(NodeStore emitStore, NodeRef cls, NodeRef enclosing, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker tracker)
    {
        var sym = GetSymbolOfDeclaration(cls);
        var staticInfos = new List<IndexInfo>();
        foreach (var info in GetIndexInfosOfType(GetTypeOfSymbol(sym)))
        {
            staticInfos.Add(info);
        }
        var instanceInfos = GetIndexInfosOfSymbol(sym);
        List<Node>? result = null;
        foreach (var (infoList, isStatic) in new[] { (staticInfos, true), (instanceInfos, false) })
        {
            if (infoList.Count == 0)
            {
                continue;
            }
            result ??= [];
            foreach (var info in infoList)
            {
                if (!info.Declaration.IsNone)
                {
                    continue;
                }
                if (info.Components is null && info.KeyType == stringType && info.Type == anyType && !info.IsReadonly)
                {
                    continue; // inherited, but looks like a late-bound signature because it has no declarations (anyBaseTypeIndexInfo)
                }
                if (info.Components is { } components)
                {
                    var allComponentComputedNamesSerializable = components.TrueForAll(e =>
                    {
                        var estore = StoreOf(e);
                        var name = estore.Name(e.Node);
                        return !name.IsNone && estore.Kind(name) == SyntaxKind.ComputedPropertyName && Utilities.IsEntityNameExpression(estore, estore.Expression(name)) &&
                            !enclosing.IsNone && IsEntityNameVisibleFull(At(e, estore.Expression(name)), enclosing, false).Accessibility == SymbolAccessibility.Accessible;
                    });
                    if (allComponentComputedNamesSerializable)
                    {
                        foreach (var e in components)
                        {
                            // skip late bound props that contribute to the index signature - they'll be preserved via other means
                            if (HasLateBindableName(e))
                            {
                                continue;
                            }
                            var estore = StoreOf(e);
                            var name = estore.Name(e.Node);
                            var property = WithEmitContext(emitStore, enclosing, null, flags, internalFlags, tracker, context =>
                            {
                                TrackComputedName(At(e, estore.Expression(name)), enclosing, context);
                                var modifiers = new List<Node>();
                                if (isStatic)
                                {
                                    modifiers.Add(SToken(SyntaxKind.StaticKeyword));
                                }
                                if (info.IsReadonly)
                                {
                                    modifiers.Add(SToken(SyntaxKind.ReadonlyKeyword));
                                }
                                var hasQuestion = estore.Kind(e.Node) is SyntaxKind.PropertySignature or SyntaxKind.PropertyDeclaration or SyntaxKind.MethodSignature or SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor &&
                                    !estore.QuestionToken(e.Node).IsNone;
                                var type = TypeToTypeNodeHelper(GetTypeOfSymbol(GetSymbolOfDeclaration(e)), context);
                                var node = SNew(SyntaxKind.PropertyDeclaration);
                                S.SetModifiers(node, modifiers.Count == 0 ? NodeList.None : SList(modifiers));
                                S.SetName(node, CopyExistingNode(At(e, name), markReused: false));
                                S.SetQuestionToken(node, hasQuestion ? SToken(SyntaxKind.QuestionToken) : Node.None);
                                S.SetType(node, type);
                                return node;
                            });
                            if (!property.IsNone)
                            {
                                result.Add(property);
                            }
                        }
                        continue;
                    }
                }
                var signature = WithEmitContext(emitStore, enclosing, null, flags, internalFlags, tracker, context => IndexInfoToIndexSignatureDeclarationHelper(info, context, Node.None));
                if (!signature.IsNone)
                {
                    if (isStatic)
                    {
                        var saved = EnterEmitBuilder(emitStore);
                        try
                        {
                            var modifiers = new List<Node> { SToken(SyntaxKind.StaticKeyword) };
                            foreach (var m in S.Nodes(S.Modifiers(signature)))
                            {
                                modifiers.Add(m);
                            }
                            S.SetModifiers(signature, SList(modifiers));
                        }
                        finally
                        {
                            ExitEmitBuilder(saved);
                        }
                    }
                    result.Add(signature);
                }
            }
        }
        return result;
    }

    internal bool RequiresAddingImplicitUndefinedOf(NodeRef parameter, NodeRef enclosingDeclaration) => RequiresAddingImplicitUndefined(parameter, enclosingDeclaration);

    internal bool IsOptionalParameterOf(NodeRef node) => IsOptionalParameter(node);

    /// <summary>
    /// declarations.ts's fakespace (a namespace declaration with the properties of an expando function as its locals,
    /// whose parent is the enclosing declaration) as a node builder fake scope.
    /// </summary>
    internal FakeScope CreateFakeNamespaceScope(List<SymbolRef> props)
    {
        var locals = new SymTable(Names);
        foreach (var p in props)
        {
            locals.Set(NameOf(p), p);
        }
        return new FakeScope { Kind = "namespace", Locals = locals };
    }

    // symbol.mergeId: set on the source symbol of recordMergedSymbol
    private bool HasMergeId(SymbolRef symbol) => mergedSymbols.ContainsKey(symbol);

    // checker.ts getExternalModuleFileFromDeclaration
    private NodeRef GetExternalModuleFileFromDeclaration(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        Node specifier;
        if (store.Kind(declaration.Node) == SyntaxKind.ModuleDeclaration)
        {
            var name = store.Name(declaration.Node);
            specifier = store.Kind(name) == SyntaxKind.StringLiteral ? name : Node.None;
        }
        else
        {
            specifier = Utilities.GetExternalModuleName(store, declaration.Node);
        }
        if (specifier.IsNone)
        {
            return NodeRef.None;
        }
        var moduleSymbol = ResolveExternalModuleNameWorker(At(declaration, specifier), At(declaration, specifier), null);
        if (moduleSymbol.IsNone)
        {
            return NodeRef.None;
        }
        return GetDeclarationOfKind(moduleSymbol, SyntaxKind.SourceFile);
    }

    // checker.ts isPrivateIdentifierSymbol
    private bool IsPrivateIdentifierSymbol(SymbolRef symbol)
    {
        var valueDeclaration = ValueDeclarationOf(symbol);
        return !valueDeclaration.IsNone && IsPrivateIdentifierClassElementDeclaration(valueDeclaration);
    }

    // checker.ts trackComputedName (in the node builder)
    private void TrackComputedName(NodeRef accessExpression, NodeRef enclosingDeclaration, NodeBuilderContext context)
    {
        if (!context.Tracker.CanTrackSymbol)
        {
            return;
        }
        // get symbol of the first identifier of the entityName
        var store = StoreOf(accessExpression);
        var firstIdentifier = At(accessExpression, Utilities.GetFirstIdentifier(store, accessExpression.Node));
        var text = store.Texts.GetString(store.EscapedText(firstIdentifier.Node))!;
        var name = ResolveName(enclosingDeclaration, text, NodeRef.None, SymbolFlags.Value | SymbolFlags.ExportValue, null, isUse: true);
        if (!name.IsNone)
        {
            context.Tracker.TrackSymbol(name, enclosingDeclaration, null, SymbolFlags.Value);
        }
        else
        {
            // Name does not resolve at target location, track symbol at dest location (should be inaccessible)
            var fallback = ResolveName(firstIdentifier, text, NodeRef.None, SymbolFlags.Value | SymbolFlags.ExportValue, null, isUse: true);
            if (!fallback.IsNone)
            {
                context.Tracker.TrackSymbol(fallback, enclosingDeclaration, null, SymbolFlags.Value);
            }
        }
    }

    // ---- symbol accessibility with aliases to make visible ----

    /// <summary>checker.ts isSymbolAccessible(symbol, enclosingDeclaration, meaning, shouldComputeAliasesToMakeVisible).</summary>
    internal SymbolAccessibilityResult IsSymbolAccessibleFull(SymbolRef symbol, NodeRef enclosingDeclaration, FakeScope? fakeScope, SymbolFlags meaning, bool shouldComputeAliasesToMakeVisible)
    {
        if (!symbol.IsNone && (!enclosingDeclaration.IsNone || fakeScope is not null))
        {
            var result = IsAnySymbolAccessibleFull([symbol], enclosingDeclaration, fakeScope, symbol, meaning, shouldComputeAliasesToMakeVisible, allowModules: true);
            if (result is not null)
            {
                return result;
            }

            // This could be a symbol that is not exported in the external module
            // or it could be a symbol from different external module that is not aliased and hence cannot be named
            var symbolExternalModule = SymbolRef.None;
            foreach (var d in DeclarationsOf(symbol) ?? [])
            {
                symbolExternalModule = GetExternalModuleContainer(d);
                if (!symbolExternalModule.IsNone)
                {
                    break;
                }
            }
            if (!symbolExternalModule.IsNone)
            {
                var enclosingExternalModule = GetExternalModuleContainer(enclosingDeclaration);
                if (symbolExternalModule != enclosingExternalModule)
                {
                    // name from different external module that is not visible
                    return new SymbolAccessibilityResult
                    {
                        Accessibility = SymbolAccessibility.CannotBeNamed,
                        ErrorSymbolName = SymbolToString(symbol, enclosingDeclaration, meaning),
                        ErrorModuleName = SymbolToString(symbolExternalModule),
                        ErrorNode = !enclosingDeclaration.IsNone && Utilities.IsInJSFile(StoreOf(enclosingDeclaration), enclosingDeclaration.Node) ? enclosingDeclaration : NodeRef.None,
                    };
                }
            }

            // Just a local name that is not accessible
            return new SymbolAccessibilityResult
            {
                Accessibility = SymbolAccessibility.NotAccessible,
                ErrorSymbolName = SymbolToString(symbol, enclosingDeclaration, meaning),
            };
        }
        return SymbolAccessibilityResult.Accessible();
    }

    // checker.ts isAnySymbolAccessible
    private SymbolAccessibilityResult? IsAnySymbolAccessibleFull(List<SymbolRef>? symbols, NodeRef enclosingDeclaration, FakeScope? fakeScope, SymbolRef initialSymbol, SymbolFlags meaning, bool shouldComputeAliasesToMakeVisible, bool allowModules)
    {
        if (symbols is not { Count: > 0 })
        {
            return null;
        }
        var hadAccessibleChain = SymbolRef.None;
        var earlyModuleBail = false;
        foreach (var symbol in symbols)
        {
            // Symbol is accessible if it by itself is accessible
            var accessibleSymbolChain = GetAccessibleSymbolChain(symbol, enclosingDeclaration, fakeScope, meaning, false);
            if (accessibleSymbolChain is not null)
            {
                hadAccessibleChain = symbol;
                var hasAccessibleDeclarations = HasVisibleDeclarationsFull(accessibleSymbolChain[0], shouldComputeAliasesToMakeVisible);
                if (hasAccessibleDeclarations is not null)
                {
                    return hasAccessibleDeclarations;
                }
            }
            if (allowModules)
            {
                if ((DeclarationsOf(symbol) ?? []).Exists(HasNonGlobalAugmentationExternalModuleSymbol))
                {
                    if (shouldComputeAliasesToMakeVisible)
                    {
                        earlyModuleBail = true;
                        // Generally speaking, we want to use the aliases that already exist to refer to a module, if present
                        // In order to do so, we need to find those aliases in order to retain them in declaration emit; so
                        // if we are in declaration emit, we cannot use the fast path for module visibility until we've exhausted
                        // all other visibility options (in order to capture the possible aliases used to reference the module)
                        continue;
                    }
                    // Any meaning of a module symbol is always accessible via an `import` type
                    return SymbolAccessibilityResult.Accessible();
                }
            }

            // If we haven't got the accessible symbol, it doesn't mean the symbol is actually inaccessible.
            // It could be a qualified symbol and hence verify the path
            var containers = GetContainersOfSymbol(symbol, enclosingDeclaration, fakeScope, meaning);
            var parentResult = IsAnySymbolAccessibleFull(containers, enclosingDeclaration, fakeScope, initialSymbol, initialSymbol == symbol ? GetQualifiedLeftMeaning(meaning) : meaning, shouldComputeAliasesToMakeVisible, allowModules);
            if (parentResult is not null)
            {
                return parentResult;
            }
        }

        if (earlyModuleBail)
        {
            return SymbolAccessibilityResult.Accessible();
        }

        if (!hadAccessibleChain.IsNone)
        {
            return new SymbolAccessibilityResult
            {
                Accessibility = SymbolAccessibility.NotAccessible,
                ErrorSymbolName = SymbolToString(initialSymbol, enclosingDeclaration, meaning),
                ErrorModuleName = hadAccessibleChain != initialSymbol ? SymbolToString(hadAccessibleChain, enclosingDeclaration, SymbolFlags.Namespace) : null,
            };
        }
        return null;
    }

    // checker.ts hasVisibleDeclarations(symbol, shouldComputeAliasToMakeVisible): undefined (null) when not visible
    private SymbolAccessibilityResult? HasVisibleDeclarationsFull(SymbolRef symbol, bool shouldComputeAliasToMakeVisible)
    {
        List<NodeRef>? aliasesToMakeVisible = null;
        foreach (var declaration in DeclarationsOf(symbol) ?? [])
        {
            if (Kind(declaration) != SyntaxKind.Identifier && !GetIsDeclarationVisible(declaration))
            {
                return null;
            }
        }
        return new SymbolAccessibilityResult { Accessibility = SymbolAccessibility.Accessible, AliasesToMakeVisible = aliasesToMakeVisible };

        bool GetIsDeclarationVisible(NodeRef declaration)
        {
            if (IsDeclarationVisible(declaration))
            {
                return true;
            }
            // Mark the unexported alias as visible if its parent is visible
            // because these kind of aliases can be used to name types in declaration file
            var store = StoreOf(declaration);
            var n = declaration.Node;
            var anyImportSyntax = GetAnyImportSyntax(store, n);
            if (!anyImportSyntax.IsNone &&
                !Utilities.HasSyntacticModifierUncached(store, anyImportSyntax, ModifierFlags.Export) && // import clause without export
                IsDeclarationVisible(At(declaration, store.Parent(anyImportSyntax))))
            {
                return AddVisibleAlias(declaration, At(declaration, anyImportSyntax));
            }
            var kind = store.Kind(n);
            if (kind == SyntaxKind.VariableDeclaration && store.Kind(store.Parent(store.Parent(n))) == SyntaxKind.VariableStatement &&
                !Utilities.HasSyntacticModifierUncached(store, store.Parent(store.Parent(n)), ModifierFlags.Export) && // unexported variable statement
                IsDeclarationVisible(At(declaration, store.Parent(store.Parent(store.Parent(n))))))
            {
                return AddVisibleAlias(declaration, At(declaration, store.Parent(store.Parent(n))));
            }
            if (IsLateVisibilityPaintedStatement(kind) // unexported top-level statement
                && !Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Export)
                && IsDeclarationVisible(At(declaration, store.Parent(n))))
            {
                return AddVisibleAlias(declaration, declaration);
            }
            if (kind == SyntaxKind.BindingElement)
            {
                if ((SymbolFlagsOf(symbol) & SymbolFlags.Alias) != 0 && Utilities.IsInJSFile(store, n))
                {
                    // exported import-like top-level JS require statement
                    var p = store.Parent(n);
                    var vd = p.IsNone ? Node.None : store.Parent(p);
                    if (!vd.IsNone && store.Kind(vd) == SyntaxKind.VariableDeclaration)
                    {
                        var vdl = store.Parent(vd);
                        var vs = vdl.IsNone ? Node.None : store.Parent(vdl);
                        if (!vs.IsNone && store.Kind(vs) == SyntaxKind.VariableStatement && !Utilities.HasSyntacticModifierUncached(store, vs, ModifierFlags.Export) &&
                            !store.Parent(vs).IsNone && IsDeclarationVisible(At(declaration, store.Parent(vs))))
                        {
                            return AddVisibleAlias(declaration, At(declaration, vs));
                        }
                    }
                }
                else if ((SymbolFlagsOf(symbol) & SymbolFlags.BlockScopedVariable) != 0)
                {
                    var rootDeclaration = Utilities.WalkUpBindingElementsAndPatterns(store, n);
                    if (store.Kind(rootDeclaration) == SyntaxKind.Parameter)
                    {
                        return false;
                    }
                    var variableStatement = store.Parent(store.Parent(rootDeclaration));
                    if (store.Kind(variableStatement) != SyntaxKind.VariableStatement)
                    {
                        return false;
                    }
                    if (Utilities.HasSyntacticModifierUncached(store, variableStatement, ModifierFlags.Export))
                    {
                        return true;
                    }
                    if (!IsDeclarationVisible(At(declaration, store.Parent(variableStatement))))
                    {
                        return false;
                    }
                    return AddVisibleAlias(declaration, At(declaration, variableStatement));
                }
            }
            // Declaration is not visible
            return false;
        }

        bool AddVisibleAlias(NodeRef declaration, NodeRef aliasingStatement)
        {
            // In function "buildTypeDisplay" where we decide whether to write type-alias or serialize types,
            // we want to just check if type- alias is accessible or not but we don't care about emitting those alias at that time
            // since we will do the emitting later in trackSymbol.
            if (shouldComputeAliasToMakeVisible)
            {
                isVisibleLinks[declaration] = true;
                aliasesToMakeVisible ??= [];
                if (!aliasesToMakeVisible.Contains(aliasingStatement))
                {
                    aliasesToMakeVisible.Add(aliasingStatement);
                }
            }
            return true;
        }
    }

    /// <summary>checker.ts isEntityNameVisible(entityName, enclosingDeclaration, shouldComputeAliasToMakeVisible).</summary>
    internal SymbolAccessibilityResult IsEntityNameVisibleFull(NodeRef entityName, NodeRef enclosingDeclaration, bool shouldComputeAliasToMakeVisible = true)
    {
        var store = StoreOf(entityName);
        var meaning = GetMeaningOfEntityNameReference(store, entityName.Node);
        var firstIdentifier = At(entityName, Utilities.GetFirstIdentifier(store, entityName.Node));
        var symbol = ResolveName(enclosingDeclaration, store.Texts.GetString(store.EscapedText(firstIdentifier.Node)) ?? "", NodeRef.None, meaning, null, isUse: false);
        if (!symbol.IsNone && (SymbolFlagsOf(symbol) & SymbolFlags.TypeParameter) != 0 && (meaning & SymbolFlags.Type) != 0)
        {
            return SymbolAccessibilityResult.Accessible();
        }
        if (symbol.IsNone && Utilities.IsThisIdentifier(store, firstIdentifier.Node) &&
            IsSymbolAccessibleFull(GetSymbolOfDeclaration(At(entityName, Utilities.GetThisContainer(store, firstIdentifier.Node, false, false))), firstIdentifier, null, meaning, false).Accessibility == SymbolAccessibility.Accessible)
        {
            return SymbolAccessibilityResult.Accessible();
        }

        if (symbol.IsNone)
        {
            return new SymbolAccessibilityResult
            {
                Accessibility = SymbolAccessibility.NotResolved,
                ErrorSymbolName = GetTextOfNode(firstIdentifier),
                ErrorNode = firstIdentifier,
            };
        }
        // Verify if the symbol is accessible
        return HasVisibleDeclarationsFull(symbol, shouldComputeAliasToMakeVisible) ?? new SymbolAccessibilityResult
        {
            Accessibility = SymbolAccessibility.NotAccessible,
            ErrorSymbolName = GetTextOfNode(firstIdentifier),
            ErrorNode = firstIdentifier,
        };
    }

    /// <summary>
    /// checker.ts collectLinkedAliases(node, setVisibility): the declarations an export assignment's identifier or an export
    /// specifier's name links to (painted visible with setVisibility, else returned).
    /// </summary>
    internal List<NodeRef>? CollectLinkedAliases(NodeRef node, bool setVisibility)
    {
        var store = StoreOf(node);
        var exportSymbol = SymbolRef.None;
        var parent = store.Parent(node.Node);
        if (store.Kind(node.Node) != SyntaxKind.StringLiteral && !parent.IsNone && store.Kind(parent) == SyntaxKind.ExportAssignment)
        {
            exportSymbol = ResolveName(node, store.Texts.GetString(store.EscapedText(node.Node))!, node, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias, null, isUse: false);
        }
        else if (store.Kind(parent) == SyntaxKind.ExportSpecifier)
        {
            exportSymbol = GetTargetOfExportSpecifier(At(node, parent), SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias, dontResolveAlias: false);
        }
        List<NodeRef>? result = null;
        HashSet<SymbolRef>? visited = null;
        if (!exportSymbol.IsNone)
        {
            visited = [exportSymbol];
            BuildVisibleNodeList(DeclarationsOf(exportSymbol));
        }
        return result;

        void BuildVisibleNodeList(Decls? declarations)
        {
            if (declarations is null)
            {
                return;
            }
            foreach (var declaration in declarations)
            {
                var dstore = StoreOf(declaration);
                var anyImport = GetAnyImportSyntax(dstore, declaration.Node);
                var resultNode = anyImport.IsNone ? declaration : At(declaration, anyImport);
                if (setVisibility)
                {
                    isVisibleLinks[declaration] = true;
                }
                else
                {
                    result ??= [];
                    if (!result.Contains(resultNode))
                    {
                        result.Add(resultNode);
                    }
                }

                if (Utilities.IsInternalModuleImportEqualsDeclaration(dstore, declaration.Node))
                {
                    // Add the referenced top container visible
                    var internalModuleReference = dstore.ModuleReference(declaration.Node);
                    var firstIdentifier = Utilities.GetFirstIdentifier(dstore, internalModuleReference);
                    var importSymbol = ResolveName(declaration, dstore.Texts.GetString(dstore.EscapedText(firstIdentifier))!, NodeRef.None, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, null, isUse: false);
                    if (!importSymbol.IsNone && visited is not null)
                    {
                        if (visited.Add(importSymbol))
                        {
                            BuildVisibleNodeList(DeclarationsOf(importSymbol));
                        }
                    }
                }
            }
        }
    }
}
