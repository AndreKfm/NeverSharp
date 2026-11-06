// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (symbolTableToDeclarationStatements and its helpers, the resolver's
// getDeclarationStatementsForSourceFile, getNonInheritedProperties, getImplementsTypes)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/nodebuilderimpl.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D5 (declaration emit of JavaScript files). The statements are made
// with the transformers' factory of the emit copy; the expanding mode of quick info (maxExpansionDepth) is left out.
// The fake namespace of serializeAsNamespaceDeclaration is a node builder fake scope over the root enclosing declaration.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    /// <summary>
    /// The resolver's getDeclarationStatementsForSourceFile (a JavaScript source file): the statements in the emit copy, or
    /// null when the node builder encountered an error (TypeScript's undefined).
    /// </summary>
    internal List<Node>? GetDeclarationStatementsForSourceFile(NodeStore emitStore, NodeFactory factory, NodeRef node, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker tracker)
    {
        var sym = GetSymbolOfDeclaration(node);
        SymTable? table;
        if (sym.IsNone)
        {
            table = LocalsOf(node);
        }
        else
        {
            ResolveExternalModuleSymbol(sym); // ensures cjs export assignment is setup
            table = ExportsOf(sym);
        }
        if (table is null)
        {
            return [];
        }
        List<Node>? result = null;
        WithEmitContext(emitStore, node, null, flags, internalFlags, tracker, context =>
        {
            result = new DeclarationSerializer(this, factory, context).Run(table.Value);
            return Node.None;
        });
        return result;
    }

    // utilities.ts isPartOfParameterDeclaration
    private bool IsPartOfParameterDeclarationOf(NodeRef node) => Utilities.IsPartOfParameterDeclaration(StoreOf(node), node.Node);

    /// <summary>checker.ts symbolTableToDeclarationStatements: the closure is the instance.</summary>
    private sealed class DeclarationSerializer
    {
        private readonly Checker c;
        private readonly NodeFactory f;
        private NodeBuilderContext context;
        private readonly NodeBuilderContext oldcontext;
        private readonly NodeRef enclosingDeclaration;
        private List<Node> results = [];
        private readonly HashSet<SymbolRef> visitedSymbols = [];
        private readonly List<OrderedSymbols> deferredPrivatesStack = [];
        private bool addingDeclare;

        // Map<SymbolId, Symbol> in insertion order (forEach visits entries added while iterating)
        private sealed class OrderedSymbols
        {
            internal readonly List<SymbolRef> List = [];
            private readonly HashSet<SymbolRef> set = [];

            internal void Set(SymbolRef symbol)
            {
                if (set.Add(symbol))
                {
                    List.Add(symbol);
                }
            }
        }

        internal DeclarationSerializer(Checker checker, NodeFactory factory, NodeBuilderContext context)
        {
            c = checker;
            f = factory;
            // We save the enclosing declaration off here so it's not adjusted by well-meaning declaration
            // emit codepaths which want to apply more specific contexts (so we can still refer to the root real declaration
            // we're trying to emit from later on)
            enclosingDeclaration = context.EnclosingDeclaration;
            oldcontext = context;
            this.context = context.Clone();
            this.context.UsedSymbolNames = oldcontext.UsedSymbolNames is null ? [] : [.. oldcontext.UsedSymbolNames];
            this.context.RemappedSymbolNames = [];
            this.context.RemappedSymbolReferences = oldcontext.RemappedSymbolReferences is null ? [] : new(oldcontext.RemappedSymbolReferences);
            this.context.Tracker = new SymbolTrackerImpl(c, this.context, new SerializerTracker(this));
        }

        private NodeStore S => c.S;

        // the serializer's tracker: ...oldcontext.tracker.inner with its own trackSymbol
        private sealed class SerializerTracker(DeclarationSerializer s) : ISymbolTracker
        {
            private ISymbolTracker? Inner => s.oldcontext.Tracker.Inner;

            public bool TrackSymbol(SymbolRef sym, NodeRef decl, FakeScope? fakeScope, SymbolFlags meaning)
            {
                var c = s.c;
                if (s.context.RemappedSymbolNames?.ContainsKey(sym) == true)
                {
                    return false; // If the context has a remapped name for the symbol, it *should* mean it's been made visible
                }
                var accessibleResult = c.IsSymbolAccessibleFull(sym, decl, fakeScope, meaning, shouldComputeAliasesToMakeVisible: false);
                if (accessibleResult.Accessibility == SymbolAccessibility.Accessible)
                {
                    // Lookup the root symbol of the chain of refs we'll use to access it and serialize it
                    var chain = c.LookupSymbolChainWorker(sym, s.context, meaning, false);
                    if ((c.SymbolFlagsOf(sym) & SymbolFlags.Property) == 0)
                    {
                        // Only include referenced privates in the same file. Weird JS aliases may expose privates
                        // from other files - assume JS transforms will make those available via expected means
                        var root = chain[0];
                        var contextFile = s.oldcontext.EnclosingDeclaration.File;
                        if ((c.DeclarationsOf(root) ?? []).Exists(d => d.File == contextFile))
                        {
                            s.IncludePrivateSymbol(root);
                        }
                    }
                }
                else if (Inner is not null)
                {
                    return Inner.TrackSymbol(sym, decl, fakeScope, meaning);
                }
                return false;
            }

            public void ReportInaccessibleThisError() => Inner?.ReportInaccessibleThisError();
            public void ReportPrivateInBaseOfClassExpression(string propertyName) => Inner?.ReportPrivateInBaseOfClassExpression(propertyName);
            public void ReportInaccessibleUniqueSymbolError() => Inner?.ReportInaccessibleUniqueSymbolError();
            public void ReportCyclicStructureError() => Inner?.ReportCyclicStructureError();
            public void ReportLikelyUnsafeImportRequiredError(string specifier, string? symbolName) => Inner?.ReportLikelyUnsafeImportRequiredError(specifier, symbolName);
            public void ReportTruncationError() => Inner?.ReportTruncationError();
            public void ReportNonlocalAugmentation(NodeRef containingFile, SymbolRef parentSymbol, SymbolRef augmentingSymbol) => Inner?.ReportNonlocalAugmentation(containingFile, parentSymbol, augmentingSymbol);
            public void ReportNonSerializableProperty(string propertyName) => Inner?.ReportNonSerializableProperty(propertyName);
            public void ReportInferenceFallback(NodeRef node) => Inner?.ReportInferenceFallback(node);
            public void PushErrorFallbackNode(NodeRef node) => Inner?.PushErrorFallbackNode(node);
            public void PopErrorFallbackNode() => Inner?.PopErrorFallbackNode();
        }

        internal List<Node> Run(SymTable symbolTable)
        {
            for (var i = 0; i < symbolTable.Count; i++)
            {
                var baseName = Utilities.UnescapeLeadingUnderscores(symbolTable.NameAt(i));
                _ = GetInternalSymbolName(symbolTable.ValueAt(i), baseName); // Called to cache values into `usedSymbolNames` and `remappedSymbolNames`
            }
            addingDeclare = true; // (!context.bundled: no outFile)
            var symbols = new List<SymbolRef>();
            var exportEquals = symbolTable.Get(InternalSymbolName.ExportEquals);
            if (!exportEquals.IsNone && symbolTable.Count > 1 && (c.SymbolFlagsOf(exportEquals) & (SymbolFlags.Alias | SymbolFlags.Module)) != 0)
            {
                // Remove extraneous elements from root symbol table (they'll be mixed back in when the target of the `export=` is looked up)
                symbols.Add(exportEquals);
            }
            else
            {
                for (var i = 0; i < symbolTable.Count; i++)
                {
                    symbols.Add(symbolTable.ValueAt(i));
                }
            }

            VisitSymbolTable(symbols);
            return MergeRedundantStatements(results);
        }

        // ---- final passes ----

        private List<Node> GetNamesOfDeclaration(Node statement)
        {
            var result = new List<Node>();
            if (S.Kind(statement) == SyntaxKind.VariableStatement)
            {
                foreach (var d in S.Nodes(S.Declarations(S.DeclarationList(statement))))
                {
                    var name = Utilities.GetNameOfDeclaration(S, d);
                    if (!name.IsNone && S.Kind(name) == SyntaxKind.Identifier)
                    {
                        result.Add(name);
                    }
                }
                return result;
            }
            var n = Utilities.GetNameOfDeclaration(S, statement);
            if (!n.IsNone && S.Kind(n) == SyntaxKind.Identifier)
            {
                result.Add(n);
            }
            return result;
        }

        private string IdText(Node identifier) => S.Texts.GetString(Utilities.IdText(S, identifier))!;

        private List<Node> FlattenExportAssignedNamespace(List<Node> statements)
        {
            var exportAssignment = statements.Find(s => S.Kind(s) == SyntaxKind.ExportAssignment);
            var nsIndex = statements.FindIndex(s => S.Kind(s) == SyntaxKind.ModuleDeclaration);
            var ns = nsIndex != -1 ? statements[nsIndex] : Node.None;
            if (!ns.IsNone && !exportAssignment.IsNone && S.IsExportEquals(exportAssignment) &&
                S.Kind(S.Expression(exportAssignment)) == SyntaxKind.Identifier && S.Kind(S.Name(ns)) == SyntaxKind.Identifier &&
                IdText(S.Name(ns)) == IdText(S.Expression(exportAssignment)) &&
                !S.Body(ns).IsNone && S.Kind(S.Body(ns)) == SyntaxKind.ModuleBlock)
            {
                // Pass 0: Correct situations where a module has both an `export = ns` and multiple top-level exports by stripping the export modifiers from
                //  the top-level exports and exporting them in the targeted ns, as can occur when a js file has both typedefs and `module.export` assignments
                var excessExports = statements.FindAll(s => (Utilities.GetEffectiveModifierFlags(S, s) & ModifierFlags.Export) != 0);
                var name = S.Name(ns);
                var body = S.Body(ns);
                if (excessExports.Count > 0)
                {
                    var specifiers = new List<Node>();
                    foreach (var e in excessExports)
                    {
                        foreach (var id in GetNamesOfDeclaration(e))
                        {
                            specifiers.Add(f.CreateExportSpecifier(isTypeOnly: false, Node.None, id));
                        }
                    }
                    var newStatements = new List<Node>(S.Nodes(S.Statements(body)).ToArray())
                    {
                        f.CreateExportDeclaration(default, isTypeOnly: false, f.CreateNamedExports(f.CreateNodeArray(specifiers)), Node.None),
                    };
                    body = f.UpdateModuleBlock(body, f.CreateNodeArray(newStatements));
                    ns = f.UpdateModuleDeclaration(ns, S.Modifiers(ns), S.Name(ns), body);
                    statements = [.. statements[..nsIndex], ns, .. statements[(nsIndex + 1)..]];
                }

                // Pass 1: Flatten `export namespace _exports {} export = _exports;` so long as the `export=` only points at a single namespace declaration
                var nsNode = ns;
                if (!statements.Exists(s => s != nsNode && NodeHasName(s, name)))
                {
                    results = [];
                    // If the namespace contains no export assignments or declarations, and no declarations flagged with `export`, then _everything_ is exported -
                    // to respect this as the top level, we need to add an `export` modifier to everything
                    var mixinExportFlag = !Core.Some(S.Nodes(S.Statements(body)).ToArray(), s => Utilities.HasSyntacticModifier(S, s, ModifierFlags.Export) || S.Kind(s) is SyntaxKind.ExportAssignment or SyntaxKind.ExportDeclaration);
                    foreach (var s in S.Nodes(S.Statements(body)))
                    {
                        AddResult(s, mixinExportFlag ? ModifierFlags.Export : ModifierFlags.None); // Recalculates the ambient (and export, if applicable from above) flag
                    }
                    var kept = statements.FindAll(s => s != nsNode && s != exportAssignment);
                    kept.AddRange(results);
                    statements = kept;
                }
            }
            return statements;
        }

        // utilities.ts nodeHasName
        private bool NodeHasName(Node statement, Node name)
        {
            // isNamedDeclaration(statement): a `name` property (none for the statements without one)
            var declName = S.Kind(statement) is SyntaxKind.VariableStatement or SyntaxKind.ExportAssignment or SyntaxKind.ExportDeclaration or SyntaxKind.ImportDeclaration
                or SyntaxKind.ExpressionStatement or SyntaxKind.EmptyStatement
                ? Node.None
                : S.Name(statement);
            if (!declName.IsNone && S.Kind(declName) == SyntaxKind.Identifier && IdText(declName) == IdText(name))
            {
                return true;
            }
            if (S.Kind(statement) == SyntaxKind.VariableStatement)
            {
                foreach (var d in S.Nodes(S.Declarations(S.DeclarationList(statement))))
                {
                    if (NodeHasName(d, name))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsNamedExportDeclaration(Node d, bool withSpecifier) =>
            S.Kind(d) == SyntaxKind.ExportDeclaration && (withSpecifier ? !S.ModuleSpecifier(d).IsNone : S.ModuleSpecifier(d).IsNone) &&
            !S.ExportClause(d).IsNone && S.Kind(S.ExportClause(d)) == SyntaxKind.NamedExports;

        private List<Node> MergeExportDeclarations(List<Node> statements)
        {
            // Pass 2: Combine all `export {}` declarations
            var exports = statements.FindAll(d => IsNamedExportDeclaration(d, withSpecifier: false));
            if (exports.Count > 1)
            {
                var nonExports = statements.FindAll(d => S.Kind(d) != SyntaxKind.ExportDeclaration || !S.ModuleSpecifier(d).IsNone || S.ExportClause(d).IsNone);
                var elements = new List<Node>();
                foreach (var e in exports)
                {
                    elements.AddRange(S.Nodes(S.Elements(S.ExportClause(e))).ToArray());
                }
                nonExports.Add(f.CreateExportDeclaration(default, isTypeOnly: false, f.CreateNamedExports(f.CreateNodeArray(elements)), Node.None));
                statements = nonExports;
            }
            // Pass 2b: Also combine all `export {} from "..."` declarations as needed
            var reexports = statements.FindAll(d => IsNamedExportDeclaration(d, withSpecifier: true));
            if (reexports.Count > 1)
            {
                var groups = new List<List<Node>>();
                var keys = new List<string>();
                foreach (var decl in reexports)
                {
                    var spec = S.ModuleSpecifier(decl);
                    var key = S.Kind(spec) == SyntaxKind.StringLiteral ? ">" + S.Texts.GetString(S.Text(spec)) : ">";
                    var index = keys.IndexOf(key);
                    if (index < 0)
                    {
                        keys.Add(key);
                        groups.Add([decl]);
                    }
                    else
                    {
                        groups[index].Add(decl);
                    }
                }
                if (groups.Count != reexports.Count)
                {
                    foreach (var group in groups)
                    {
                        if (group.Count > 1)
                        {
                            // remove group members from statements and then merge group members and add back to statements
                            var elements = new List<Node>();
                            foreach (var e in group)
                            {
                                elements.AddRange(S.Nodes(S.Elements(S.ExportClause(e))).ToArray());
                            }
                            var rest = statements.FindAll(s => !group.Contains(s));
                            rest.Add(f.CreateExportDeclaration(default, isTypeOnly: false, f.CreateNamedExports(f.CreateNodeArray(elements)), S.ModuleSpecifier(group[0])));
                            statements = rest;
                        }
                    }
                }
            }
            return statements;
        }

        private List<Node> InlineExportModifiers(List<Node> statements)
        {
            // Pass 3: Move all `export {}`'s to `export` modifiers where possible
            var index = statements.FindIndex(d => IsNamedExportDeclaration(d, withSpecifier: false) && S.Attributes(d).IsNone);
            if (index >= 0)
            {
                var exportDecl = statements[index];
                var replacements = new List<Node>();
                foreach (var e in S.Nodes(S.Elements(S.ExportClause(exportDecl))))
                {
                    if (S.PropertyName(e).IsNone && S.Kind(S.Name(e)) != SyntaxKind.StringLiteral)
                    {
                        // export {name} - look thru `statements` for `name`, and if all results can take an `export` modifier, do so and filter it
                        var name = S.Name(e);
                        var associatedIndices = new List<int>();
                        for (var i = 0; i < statements.Count; i++)
                        {
                            if (NodeHasName(statements[i], name))
                            {
                                associatedIndices.Add(i);
                            }
                        }
                        if (associatedIndices.Count > 0 && associatedIndices.TrueForAll(i => CanHaveExportModifier(statements[i])))
                        {
                            foreach (var i in associatedIndices)
                            {
                                statements[i] = AddExportModifier(statements[i]);
                            }
                            continue;
                        }
                    }
                    replacements.Add(e);
                }
                if (replacements.Count == 0)
                {
                    // all clauses removed, remove the export declaration
                    statements.RemoveAt(index);
                }
                else
                {
                    // some items filtered, others not - update the export declaration
                    statements[index] = f.UpdateExportDeclaration(
                        exportDecl,
                        S.Modifiers(exportDecl),
                        S.IsTypeOnly(exportDecl),
                        f.UpdateNamedExports(S.ExportClause(exportDecl), f.CreateNodeArray(replacements)),
                        S.ModuleSpecifier(exportDecl),
                        S.Attributes(exportDecl));
                }
            }
            return statements;
        }

        // utilities.ts canHaveExportModifier
        private bool CanHaveExportModifier(Node node)
        {
            var kind = S.Kind(node);
            return kind is SyntaxKind.EnumDeclaration or SyntaxKind.VariableStatement or SyntaxKind.FunctionDeclaration or SyntaxKind.ClassDeclaration
                    or SyntaxKind.InterfaceDeclaration or SyntaxKind.TypeAliasDeclaration
                || (kind == SyntaxKind.ModuleDeclaration && !IsExternalModuleAugmentation(S, node) && !Utilities.IsGlobalScopeAugmentation(S, node))
                || (kind == SyntaxKind.ImportEqualsDeclaration);
        }

        private List<Node> MergeRedundantStatements(List<Node> statements)
        {
            statements = FlattenExportAssignedNamespace(statements);
            statements = MergeExportDeclarations(statements);
            statements = InlineExportModifiers(statements);

            // Not a cleanup, but as a final step: If there is a mix of `export` and non-`export` declarations, but no `export =` or `export {}` add a `export {};` so
            // declaration privacy is respected.
            if (!enclosingDeclaration.IsNone &&
                ((c.Kind(enclosingDeclaration) == SyntaxKind.SourceFile && Utilities.IsExternalOrCommonJsModule(c.StoreOf(enclosingDeclaration), enclosingDeclaration.Node, c.BindingOf(enclosingDeclaration.File))) ||
                    c.Kind(enclosingDeclaration) == SyntaxKind.ModuleDeclaration) &&
                (!statements.Exists(IsExternalModuleIndicator) || (!statements.Exists(s => S.Kind(s) is SyntaxKind.ExportAssignment or SyntaxKind.ExportDeclaration) && statements.Exists(NeedsScopeMarker))))
            {
                statements.Add(f.CreateExportDeclaration(default, isTypeOnly: false, f.CreateNamedExports(f.CreateNodeArray(ReadOnlySpan<Node>.Empty)), Node.None));
            }
            return statements;
        }

        private bool IsExternalModuleIndicator(Node result) =>
            S.Kind(result) is SyntaxKind.ImportDeclaration or SyntaxKind.ImportEqualsDeclaration or SyntaxKind.ExportDeclaration or SyntaxKind.ExportAssignment ||
            Utilities.HasSyntacticModifier(S, result, ModifierFlags.Export);

        private bool NeedsScopeMarker(Node result) =>
            !(S.Kind(result) is SyntaxKind.ImportDeclaration or SyntaxKind.ImportEqualsDeclaration or SyntaxKind.ExportDeclaration or SyntaxKind.ExportAssignment) &&
            !Utilities.HasSyntacticModifier(S, result, ModifierFlags.Export) && !Utilities.IsAmbientModule(S, result);

        private Node ReplaceModifiers(Node node, ModifierFlags flags) => f.ReplaceModifiers(node, f.CreateModifiersFromModifierFlags(flags));

        private Node AddExportModifier(Node node)
        {
            var flags = (Utilities.GetEffectiveModifierFlags(S, node) | ModifierFlags.Export) & ~ModifierFlags.Ambient;
            return ReplaceModifiers(node, flags);
        }

        private Node RemoveExportModifier(Node node)
        {
            var flags = Utilities.GetEffectiveModifierFlags(S, node) & ~ModifierFlags.Export;
            return ReplaceModifiers(node, flags);
        }

        // ---- visiting symbols ----

        private void VisitSymbolTable(List<SymbolRef> symbols, bool suppressNewPrivateContext = false, bool propertyAsAlias = false)
        {
            if (!suppressNewPrivateContext)
            {
                deferredPrivatesStack.Add(new OrderedSymbols());
            }
            foreach (var symbol in symbols)
            {
                // (checkTruncationLengthIfExpanding: not expanding)
                SerializeSymbol(symbol, isPrivate: false, propertyAsAlias);
            }
            if (!suppressNewPrivateContext)
            {
                // deferredPrivates will be filled up by visiting the symbol table
                // And will continue to iterate as elements are added while visited `deferredPrivates`
                // (As that's how a map iterator is defined to work)
                var deferred = deferredPrivatesStack[^1];
                for (var i = 0; i < deferred.List.Count; i++)
                {
                    SerializeSymbol(deferred.List[i], isPrivate: true, propertyAsAlias);
                }
                deferredPrivatesStack.RemoveAt(deferredPrivatesStack.Count - 1);
            }
        }

        private void SerializeSymbol(SymbolRef symbol, bool isPrivate, bool propertyAsAlias)
        {
            _ = c.GetPropertiesOfType(c.GetTypeOfSymbol(symbol)); // resolve symbol's type and properties, which should trigger any required merges
            // cache visited list based on merged symbol, since we want to use the unmerged top-level symbol, but
            // still skip reserializing it if we encounter the merged product later on
            var visitedSym = c.GetMergedSymbol(symbol);
            if (!visitedSymbols.Add(visitedSym))
            {
                return; // Already printed
            }
            // Only actually serialize symbols within the correct enclosing declaration, otherwise do nothing with the out-of-context symbol
            var skipMembershipCheck = !isPrivate; // We only call this on exported symbols when we know they're in the correct scope
            var declarations = c.DeclarationsOf(symbol);
            if (skipMembershipCheck || (declarations ?? []).Exists(d => IsInside(d, enclosingDeclaration)))
            {
                var scopeCleanup = CloneNodeBuilderContext(context);
                var fallback = NodeRef.None;
                foreach (var d in declarations ?? [])
                {
                    if (d.File == context.EnclosingFile)
                    {
                        fallback = d;
                        break;
                    }
                }
                context.Tracker.PushErrorFallbackNode(fallback);
                SerializeSymbolWorker(symbol, isPrivate, propertyAsAlias, c.NameOf(symbol));
                context.Tracker.PopErrorFallbackNode();
                scopeCleanup();
            }
        }

        // findAncestor(d, n => n === ancestor)
        private bool IsInside(NodeRef d, NodeRef ancestor)
        {
            if (ancestor.IsNone || d.File != ancestor.File)
            {
                return false;
            }
            var store = c.StoreOf(d);
            for (var n = d.Node; !n.IsNone; n = store.Parent(n))
            {
                if (n == ancestor.Node)
                {
                    return true;
                }
            }
            return false;
        }

        // Synthesize declarations for a symbol - might be an Interface, a Class, a Namespace, a Type, a Variable (const, let, or var), an Alias
        // or a merge of some number of those.
        private void SerializeSymbolWorker(SymbolRef symbol, bool isPrivate, bool propertyAsAlias, string escapedSymbolName)
        {
            var symbolName = Utilities.UnescapeLeadingUnderscores(escapedSymbolName);
            var isDefault = escapedSymbolName == InternalSymbolName.Default;
            if (isPrivate && (context.Flags & NodeBuilderFlags.AllowAnonymousIdentifier) == 0 && IsStringANonContextualKeyword(symbolName) && !isDefault)
            {
                // Oh no. We cannot use this symbol's name as it's name... It's likely some jsdoc had an invalid name like `export` or `default` :(
                context.EncounteredError = true;
                // TODO: Issue error via symbol tracker?
                return; // If we need to emit a private with a keyword name, we're done for, since something else will try to refer to it by that name
            }
            var flags = c.SymbolFlagsOf(symbol);
            var needsPostExportDefault = isDefault && ((flags & SymbolFlags.ExportDoesNotSupportDefaultModifier) != 0
                || ((flags & SymbolFlags.Function) != 0 && c.GetPropertiesOfType(c.GetTypeOfSymbol(symbol)).Count > 0)) && (flags & SymbolFlags.Alias) == 0; // An alias symbol should preclude needing to make an alias ourselves
            var needsExportDeclaration = !needsPostExportDefault && !isPrivate && IsStringANonContextualKeyword(symbolName) && !isDefault;
            // `serializeVariableOrProperty` will handle adding the export declaration if it is run (since `getInternalSymbolName` will create the name mapping), so we need to ensuer we unset `needsExportDeclaration` if it is
            if (needsPostExportDefault || needsExportDeclaration)
            {
                isPrivate = true;
            }
            var modifierFlags = (!isPrivate ? ModifierFlags.Export : 0) | (isDefault && !needsPostExportDefault ? ModifierFlags.Default : 0);
            var isConstMergedWithNS = (flags & SymbolFlags.Module) != 0 &&
                (flags & (SymbolFlags.BlockScopedVariable | SymbolFlags.FunctionScopedVariable | SymbolFlags.Property)) != 0 &&
                escapedSymbolName != InternalSymbolName.ExportEquals;
            var isConstMergedWithNSPrintableAsSignatureMerge = isConstMergedWithNS && IsTypeRepresentableAsFunctionNamespaceMerge(c.GetTypeOfSymbol(symbol), symbol);
            if ((flags & (SymbolFlags.Function | SymbolFlags.Method)) != 0 || isConstMergedWithNSPrintableAsSignatureMerge)
            {
                SerializeAsFunctionNamespaceMerge(c.GetTypeOfSymbol(symbol), symbol, GetInternalSymbolName(symbol, symbolName), modifierFlags);
            }
            if ((flags & SymbolFlags.TypeAlias) != 0)
            {
                SerializeTypeAlias(symbol, symbolName, modifierFlags);
            }
            // Need to skip over export= symbols below - json source files get a single `Property` flagged
            // symbol of name `export=` which needs to be handled like an alias. It's not great, but it is what it is.
            if ((flags & (SymbolFlags.BlockScopedVariable | SymbolFlags.FunctionScopedVariable | SymbolFlags.Property | SymbolFlags.Accessor)) != 0
                && escapedSymbolName != InternalSymbolName.ExportEquals
                && (flags & SymbolFlags.Prototype) == 0
                && (flags & SymbolFlags.Class) == 0
                && (flags & SymbolFlags.Method) == 0
                && !isConstMergedWithNSPrintableAsSignatureMerge)
            {
                if (propertyAsAlias)
                {
                    var createdExport = SerializeMaybeAliasAssignment(symbol);
                    if (createdExport)
                    {
                        needsExportDeclaration = false;
                        needsPostExportDefault = false;
                    }
                }
                else
                {
                    var type = c.GetTypeOfSymbol(symbol);
                    var localName = GetInternalSymbolName(symbol, symbolName);
                    var typeSymbol = c.Types.Record(type).Symbol;
                    if (!typeSymbol.IsNone && typeSymbol != symbol && (c.SymbolFlagsOf(typeSymbol) & SymbolFlags.Function) != 0 &&
                        (c.DeclarationsOf(typeSymbol) ?? []).Exists(d => c.Kind(d) is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction) &&
                        ((c.MembersOf(typeSymbol)?.Count ?? 0) > 0 || (c.ExportsOf(typeSymbol)?.Count ?? 0) > 0))
                    {
                        // assignment of a anonymous expando/class-like function, the func/ns/merge branch below won't trigger,
                        // and the assignment form has to reference the unreachable anonymous type so will error.
                        // Instead, serialize the type's symbol, but with the current symbol's name, rather than the anonymous one.
                        context.RemappedSymbolReferences ??= [];
                        context.RemappedSymbolReferences[typeSymbol] = symbol; // save name remapping as local name for target symbol
                        SerializeSymbolWorker(typeSymbol, isPrivate, propertyAsAlias, escapedSymbolName);
                        context.RemappedSymbolReferences.Remove(typeSymbol);
                    }
                    else if ((flags & SymbolFlags.Function) == 0 && IsTypeRepresentableAsFunctionNamespaceMerge(type, symbol))
                    {
                        // If the type looks like a function declaration + ns could represent it, and it's type is sourced locally, rewrite it into a function declaration + ns
                        SerializeAsFunctionNamespaceMerge(type, symbol, localName, modifierFlags);
                    }
                    else
                    {
                        // A Class + Property merge is made for a `module.exports.Member = class {}`, and it doesn't serialize well as either a class _or_ a property symbol - in fact, _it behaves like an alias!_
                        // `var` is `FunctionScopedVariable`, `const` and `let` are `BlockScopedVariable`, and `module.exports.thing =` is `Property`
                        var parentValueDeclaration = c.ParentOf(symbol).IsNone ? NodeRef.None : c.ValueDeclarationOf(c.ParentOf(symbol));
                        NodeFlags? declFlags = (flags & SymbolFlags.BlockScopedVariable) == 0
                            ? !parentValueDeclaration.IsNone && c.Kind(parentValueDeclaration) == SyntaxKind.SourceFile
                                ? NodeFlags.Const // exports are immutable in es6, which is what we emulate and check; so it's safe to mark all exports as `const` (there's no difference to consumers, but it allows unique symbol type declarations)
                                : null
                            : c.IsConstantVariable(symbol)
                            ? NodeFlags.Const
                            : NodeFlags.Let;
                        var name = (needsPostExportDefault || (flags & SymbolFlags.Property) == 0) ? localName : GetUnusedName(localName, symbol);
                        var declarations = c.DeclarationsOf(symbol);
                        var textRange = NodeRef.None;
                        foreach (var d in declarations ?? [])
                        {
                            if (c.Kind(d) == SyntaxKind.VariableDeclaration)
                            {
                                textRange = d;
                                break;
                            }
                        }
                        if (!textRange.IsNone)
                        {
                            var list = c.Parent(textRange);
                            if (!list.IsNone && c.Kind(list) == SyntaxKind.VariableDeclarationList && c.StoreOf(list).Length(c.StoreOf(list).Declarations(list.Node)) == 1)
                            {
                                textRange = c.Parent(list);
                            }
                        }
                        var propertyAccessRequire = NodeRef.None;
                        foreach (var d in declarations ?? [])
                        {
                            if (c.Kind(d) == SyntaxKind.PropertyAccessExpression)
                            {
                                propertyAccessRequire = d;
                                break;
                            }
                        }
                        var requireParent = propertyAccessRequire.IsNone ? NodeRef.None : c.Parent(propertyAccessRequire);
                        var typeSymbolValueDeclaration = typeSymbol.IsNone ? NodeRef.None : c.ValueDeclarationOf(typeSymbol);
                        if (!requireParent.IsNone && c.Kind(requireParent) == SyntaxKind.BinaryExpression && c.Kind(At(requireParent, c.StoreOf(requireParent).Right(requireParent.Node))) == SyntaxKind.Identifier
                            && !typeSymbolValueDeclaration.IsNone && c.Kind(typeSymbolValueDeclaration) == SyntaxKind.SourceFile)
                        {
                            var rstore = c.StoreOf(requireParent);
                            var right = rstore.Right(requireParent.Node);
                            var rightText = rstore.Texts.GetString(rstore.EscapedText(right))!;
                            var alias = localName == rightText ? Node.None : f.CreateIdentifier(rightText);
                            AddResult(
                                f.CreateExportDeclaration(default, isTypeOnly: false,
                                    f.CreateNamedExports(NodeArray(f.CreateExportSpecifier(isTypeOnly: false, alias, f.CreateIdentifier(localName)))), Node.None),
                                ModifierFlags.None);
                            context.Tracker.TrackSymbol(typeSymbol, context.EnclosingDeclaration, context.FakeScope, SymbolFlags.Value);
                        }
                        else
                        {
                            var statement = SetTextRange(
                                f.CreateVariableStatement(
                                    default,
                                    f.CreateVariableDeclarationList(
                                        NodeArray(f.CreateVariableDeclaration(f.CreateIdentifier(name), Node.None, c.SerializeTypeForDeclaration(context, NodeRef.None, type, symbol), Node.None)),
                                        declFlags ?? NodeFlags.None)),
                                textRange);
                            AddResult(statement, name != localName ? modifierFlags & ~ModifierFlags.Export : modifierFlags);
                            if (name != localName && !isPrivate)
                            {
                                // We rename the variable declaration we generate for Property symbols since they may have a name which
                                // conflicts with a local declaration.
                                AddResult(
                                    f.CreateExportDeclaration(default, isTypeOnly: false,
                                        f.CreateNamedExports(NodeArray(f.CreateExportSpecifier(isTypeOnly: false, f.CreateIdentifier(name), f.CreateIdentifier(localName)))), Node.None),
                                    ModifierFlags.None);
                                needsExportDeclaration = false;
                                needsPostExportDefault = false;
                            }
                        }
                    }
                }
            }
            if ((flags & SymbolFlags.Enum) != 0)
            {
                SerializeEnum(symbol, symbolName, modifierFlags);
            }
            if ((flags & SymbolFlags.Class) != 0)
            {
                var valueDeclaration = c.ValueDeclarationOf(symbol);
                var vdParent = valueDeclaration.IsNone ? NodeRef.None : c.Parent(valueDeclaration);
                if ((flags & SymbolFlags.Property) != 0
                    && !vdParent.IsNone
                    && c.Kind(vdParent) == SyntaxKind.BinaryExpression
                    && c.Kind(At(vdParent, c.StoreOf(vdParent).Right(vdParent.Node))) == SyntaxKind.ClassExpression)
                {
                    // Looks like a `module.exports.Sub = class {}` - if we serialize `symbol` as a class, the result will have no members,
                    // since the classiness is actually from the target of the effective alias the symbol is. yes. A BlockScopedVariable|Class|Property
                    // _really_ acts like an Alias, and none of a BlockScopedVariable, Class, or Property. This is the travesty of JS binding today.
                    SerializeAsAlias(symbol, GetInternalSymbolName(symbol, symbolName), modifierFlags);
                }
                else
                {
                    SerializeAsClass(symbol, GetInternalSymbolName(symbol, symbolName), modifierFlags);
                }
            }
            if (((flags & (SymbolFlags.ValueModule | SymbolFlags.NamespaceModule)) != 0 && (!isConstMergedWithNS || IsTypeOnlyNamespace(symbol))) || isConstMergedWithNSPrintableAsSignatureMerge)
            {
                SerializeModule(symbol, symbolName, modifierFlags);
            }
            // The class meaning serialization should handle serializing all interface members
            if ((flags & SymbolFlags.Interface) != 0 && (flags & SymbolFlags.Class) == 0)
            {
                SerializeInterface(symbol, symbolName, modifierFlags);
            }
            if ((flags & SymbolFlags.Alias) != 0)
            {
                SerializeAsAlias(symbol, GetInternalSymbolName(symbol, symbolName), modifierFlags);
            }
            if ((flags & SymbolFlags.Property) != 0 && c.NameOf(symbol) == InternalSymbolName.ExportEquals)
            {
                SerializeMaybeAliasAssignment(symbol);
            }
            if ((flags & SymbolFlags.ExportStar) != 0)
            {
                // synthesize export * from "moduleReference"
                // Straightforward - only one thing to do - make an export declaration
                foreach (var node in c.DeclarationsOf(symbol) ?? [])
                {
                    var nstore = c.StoreOf(node);
                    var resolvedModule = c.ResolveExternalModuleName(node, At(node, nstore.ModuleSpecifier(node.Node)));
                    if (resolvedModule.IsNone)
                    {
                        continue;
                    }
                    var isTypeOnly = nstore.IsTypeOnly(node.Node);
                    var specifier = c.GetSpecifierForModuleSymbol(resolvedModule, context);
                    AddResult(f.CreateExportDeclaration(default, isTypeOnly, Node.None, StringLiteral(specifier)), ModifierFlags.None);
                }
            }
            if (needsPostExportDefault)
            {
                var internalSymbolName = GetInternalSymbolName(symbol, symbolName);
                AddResult(f.CreateExportAssignment(default, isExportEquals: false, f.CreateIdentifier(internalSymbolName)), ModifierFlags.None);
            }
            else if (needsExportDeclaration)
            {
                var internalSymbolName = GetInternalSymbolName(symbol, symbolName);
                AddResult(
                    f.CreateExportDeclaration(default, isTypeOnly: false,
                        f.CreateNamedExports(NodeArray(f.CreateExportSpecifier(isTypeOnly: false, f.CreateIdentifier(internalSymbolName), f.CreateIdentifier(symbolName)))), Node.None),
                    ModifierFlags.None);
            }
        }

        private static NodeRef At(NodeRef context, Node node) => Checker.At(context, node);

        private Node StringLiteral(string text) => f.CreateStringLiteral(S.Texts.Add(text));

        private NodeList NodeArray(params Node[] nodes) => f.CreateNodeArray(new List<Node>(nodes));

        // utilities.ts isStringANonContextualKeyword
        private static bool IsStringANonContextualKeyword(string name)
        {
            var token = Scanner.StringToToken(name);
            return token is { } t && Utilities.IsKeyword(t) && !(t >= SyntaxKind.FirstContextualKeyword && t <= SyntaxKind.LastContextualKeyword);
        }

        // checker.ts setTextRange(context, node, location) for a node made here: the positions within the enclosing file
        private Node SetTextRange(Node node, NodeRef location)
        {
            if (!location.IsNone && context.EnclosingFile != 0 && location.File == context.EnclosingFile)
            {
                var lstore = c.StoreOf(location);
                Utilities.SetTextRangePosEnd(S, node, lstore.Pos(location.Node), lstore.End(location.Node));
            }
            return node;
        }

        internal void IncludePrivateSymbol(SymbolRef symbol)
        {
            var declarations = c.DeclarationsOf(symbol) ?? [];
            if (declarations.Exists(c.IsPartOfParameterDeclarationOf))
            {
                return;
            }
            Debug.Assert(deferredPrivatesStack.Count > 0);
            _ = GetUnusedName(Utilities.UnescapeLeadingUnderscores(c.NameOf(symbol)), symbol); // Call to cache unique name for symbol
            // Blanket moving (import) aliases into the root private context should work, since imports are not valid within namespaces
            // (so they must have been in the root to begin with if they were real imports) cjs `require` aliases (an upcoming feature)
            // will throw a wrench in this, since those may have been nested, but we'll need to synthesize them in the outer scope
            // anyway, as that's the only place the import they translate to is valid. In such a case, we might need to use a unique name
            // for the moved import; which hopefully the above `getUnusedName` call should produce.
            var isExternalImportAlias = (c.SymbolFlagsOf(symbol) & SymbolFlags.Alias) != 0 && !declarations.Exists(d =>
            {
                var store = c.StoreOf(d);
                for (var n = d.Node; !n.IsNone; n = store.Parent(n))
                {
                    if (store.Kind(n) == SyntaxKind.ExportDeclaration)
                    {
                        return true;
                    }
                }
                return store.Kind(d.Node) == SyntaxKind.NamespaceExport ||
                    (store.Kind(d.Node) == SyntaxKind.ImportEqualsDeclaration && store.Kind(store.ModuleReference(d.Node)) != SyntaxKind.ExternalModuleReference);
            });
            deferredPrivatesStack[isExternalImportAlias ? 0 : deferredPrivatesStack.Count - 1].Set(symbol);
        }

        // the context's enclosing declaration is a fake namespace of serializeAsNamespaceDeclaration (not, e.g., the JSDoc
        // type alias serializeTypeAlias sets)
        private static bool IsFakeNamespace(NodeBuilderContext context) => context.FakeScope is { Kind: "namespace" };

        private bool IsExportingScope(NodeRef enclosing)
        {
            var store = c.StoreOf(enclosing);
            return (store.Kind(enclosing.Node) == SyntaxKind.SourceFile && (Utilities.IsExternalOrCommonJsModule(store, enclosing.Node, c.BindingOf(enclosing.File)) || store.ScriptKind(enclosing.Node) == ScriptKind.JSON)) ||
                (Utilities.IsAmbientModule(store, enclosing.Node) && !Utilities.IsGlobalScopeAugmentation(store, enclosing.Node));
        }

        // Prepends a `declare` and/or `export` modifier if the context requires it, and then adds `node` to `result` and returns `node`
        private void AddResult(Node node, ModifierFlags additionalModifierFlags)
        {
            if (FactoryUtilitiesPublic.CanHaveModifiers(S, node))
            {
                var oldModifierFlags = Utilities.GetEffectiveModifierFlags(S, node);
                var newModifierFlags = ModifierFlags.None;
                var enclosing = context.EnclosingDeclaration;
                if (!enclosing.IsNone && c.Kind(enclosing) is SyntaxKind.JSDocTypedefTag or SyntaxKind.JSDocCallbackTag or SyntaxKind.JSDocEnumTag)
                {
                    enclosing = new NodeRef(enclosing.File, Utilities.GetSourceFileOfNode(c.StoreOf(enclosing), enclosing.Node));
                }
                var fakeNamespace = IsFakeNamespace(context);
                var enclosingIsModule = fakeNamespace || (!enclosing.IsNone && c.Kind(enclosing) == SyntaxKind.ModuleDeclaration);
                if ((additionalModifierFlags & ModifierFlags.Export) != 0 &&
                    !enclosing.IsNone && ((!fakeNamespace && IsExportingScope(enclosing)) || enclosingIsModule) &&
                    CanHaveExportModifier(node))
                {
                    // Classes, namespaces, variables, functions, interfaces, and types should all be `export`ed in a module context if not private
                    newModifierFlags |= ModifierFlags.Export;
                }
                if (addingDeclare && (newModifierFlags & ModifierFlags.Export) == 0 &&
                    (enclosing.IsNone || fakeNamespace || (c.StoreOf(enclosing).Flags(enclosing.Node) & NodeFlags.Ambient) == 0) &&
                    S.Kind(node) is SyntaxKind.EnumDeclaration or SyntaxKind.VariableStatement or SyntaxKind.FunctionDeclaration or SyntaxKind.ClassDeclaration or SyntaxKind.ModuleDeclaration)
                {
                    // Classes, namespaces, variables, enums, and functions all need `declare` modifiers to be valid in a declaration file top-level scope
                    newModifierFlags |= ModifierFlags.Ambient;
                }
                if ((additionalModifierFlags & ModifierFlags.Default) != 0 && S.Kind(node) is SyntaxKind.ClassDeclaration or SyntaxKind.InterfaceDeclaration or SyntaxKind.FunctionDeclaration)
                {
                    newModifierFlags |= ModifierFlags.Default;
                }
                if (newModifierFlags != 0)
                {
                    node = ReplaceModifiers(node, newModifierFlags | oldModifierFlags);
                }
            }
            results.Add(node);
        }

        private void SerializeTypeAlias(SymbolRef symbol, string symbolName, ModifierFlags modifierFlags)
        {
            var aliasType = c.GetDeclaredTypeOfTypeAlias(symbol);
            var typeParams = c.GetTypeAliasTypeParameters(symbol);
            List<Node>? typeParamDecls = null;
            if (typeParams is not null)
            {
                typeParamDecls = [];
                foreach (var p in typeParams)
                {
                    typeParamDecls.Add(c.TypeParameterToDeclaration(p, context));
                }
            }
            var jsdocAliasDecl = NodeRef.None;
            foreach (var d in c.DeclarationsOf(symbol) ?? [])
            {
                if (c.Kind(d) is SyntaxKind.JSDocTypedefTag or SyntaxKind.JSDocCallbackTag or SyntaxKind.JSDocEnumTag)
                {
                    jsdocAliasDecl = d;
                    break;
                }
            }
            string? commentText = null;
            if (!jsdocAliasDecl.IsNone)
            {
                var jstore = c.StoreOf(jsdocAliasDecl);
                var comment = jstore.Comment(jsdocAliasDecl.Node);
                if (comment.IsNone)
                {
                    comment = jstore.Comment(jstore.Parent(jsdocAliasDecl.Node));
                }
                commentText = PrinterUtilities.GetTextOfJSDocComment(jstore, comment);
            }
            var saved = SaveRestoreFlags(context);
            context.Flags |= NodeBuilderFlags.InTypeAlias;
            var oldEnclosingDecl = context.EnclosingDeclaration;
            var oldFakeScope = context.FakeScope;
            context.EnclosingDeclaration = jsdocAliasDecl;
            context.FakeScope = null;
            var typeNode = Node.None;
            if (!jsdocAliasDecl.IsNone)
            {
                var jstore = c.StoreOf(jsdocAliasDecl);
                var typeExpression = jstore.TypeExpression(jsdocAliasDecl.Node);
                if (!typeExpression.IsNone && jstore.Kind(typeExpression) == SyntaxKind.JSDocTypeExpression)
                {
                    typeNode = c.TryReuseExistingTypeNodeChecked(context, At(jsdocAliasDecl, jstore.Type(typeExpression)));
                }
            }
            if (typeNode.IsNone)
            {
                typeNode = c.TypeToTypeNodeHelper(aliasType, context);
            }
            var internalSymbolName = GetInternalSymbolName(symbol, symbolName);
            var alias = f.CreateTypeAliasDeclaration(default, f.CreateIdentifier(internalSymbolName), typeParamDecls is null ? NodeList.None : f.CreateNodeArray(typeParamDecls), typeNode);
            FactoryEmitNode.SetSyntheticLeadingComments(S, alias, string.IsNullOrEmpty(commentText) ? [] :
                [new SynthesizedComment { Kind = SyntaxKind.MultiLineCommentTrivia, Text = "*\n * " + commentText.Replace("\n", "\n * ") + "\n ", HasTrailingNewLine = true }]);
            AddResult(alias, modifierFlags);
            saved.Restore(context);
            context.EnclosingDeclaration = oldEnclosingDecl;
            context.FakeScope = oldFakeScope;
        }

        private void SerializeInterface(SymbolRef symbol, string symbolName, ModifierFlags modifierFlags)
        {
            var internalSymbolName = GetInternalSymbolName(symbol, symbolName);
            var interfaceType = c.GetDeclaredTypeOfClassOrInterface(symbol);
            var localParams = c.GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol);
            var typeParamDecls = TypeParamDecls(localParams);
            var baseTypes = c.GetBaseTypes(interfaceType);
            var baseType = baseTypes.Count > 0 ? c.GetIntersectionType(baseTypes.AsSpan()) : Type.None;
            var members = SerializePropertySymbolsForClassOrInterface([.. c.GetPropertiesOfType(interfaceType)], isClass: false, baseType, isStatic: false);
            var callSignatures = SerializeSignatures(SignatureKind.Call, interfaceType, baseType, SyntaxKind.CallSignature);
            var constructSignatures = SerializeSignatures(SignatureKind.Construct, interfaceType, baseType, SyntaxKind.ConstructSignature);
            var indexSignatures = SerializeIndexSignatures(interfaceType, baseType);

            var heritageClauses = NodeList.None;
            if (baseTypes.Count > 0)
            {
                var types = new List<Node>();
                foreach (var b in baseTypes)
                {
                    var r = TrySerializeAsTypeReference(b, SymbolFlags.Value);
                    if (!r.IsNone)
                    {
                        types.Add(r);
                    }
                }
                heritageClauses = NodeArray(f.CreateHeritageClause(SyntaxKind.ExtendsKeyword, f.CreateNodeArray(types)));
            }
            var allMembers = new List<Node>();
            allMembers.AddRange(indexSignatures);
            allMembers.AddRange(constructSignatures);
            allMembers.AddRange(callSignatures);
            allMembers.AddRange(members);
            AddResult(f.CreateInterfaceDeclaration(default, f.CreateIdentifier(internalSymbolName), typeParamDecls, heritageClauses, f.CreateNodeArray(allMembers)), modifierFlags);
        }

        private NodeList TypeParamDecls(List<Type>? localParams)
        {
            if (localParams is null)
            {
                return NodeList.None;
            }
            var decls = new List<Node>();
            foreach (var p in localParams)
            {
                decls.Add(c.TypeParameterToDeclaration(p, context));
            }
            return f.CreateNodeArray(decls);
        }

        private List<Node> SerializePropertySymbolsForClassOrInterface(List<SymbolRef> props, bool isClass, Type baseType, bool isStatic)
        {
            var elements = new List<Node>();
            foreach (var prop in props)
            {
                // (checkTruncationLengthIfExpanding: not expanding)
                context.ApproximateLength += 1; // separator
                elements.AddRange(SerializePropertySymbol(prop, isStatic, baseType, isClass));
            }
            return elements;
        }

        private List<SymbolRef> GetNamespaceMembersForSerialization(SymbolRef symbol)
        {
            var exportsTable = c.GetExportsOfSymbol(symbol);
            var exports = new List<SymbolRef>();
            for (var i = 0; i < exportsTable.Count; i++)
            {
                exports.Add(exportsTable.ValueAt(i));
            }
            var merged = c.GetMergedSymbol(symbol);
            if (merged != symbol)
            {
                var membersSet = new HashSet<SymbolRef>(exports);
                var mergedExports = c.GetExportsOfSymbol(merged);
                for (var i = 0; i < mergedExports.Count; i++)
                {
                    var exported = mergedExports.ValueAt(i);
                    if ((c.GetSymbolFlags(c.ResolveSymbol(exported)) & SymbolFlags.Value) == 0 && membersSet.Add(exported))
                    {
                        exports.Add(exported);
                    }
                }
            }
            return exports.FindAll(m => IsNamespaceMember(m) && Scanner.IsIdentifierText(c.NameOf(m), ScriptTarget.ESNext));
        }

        private bool IsTypeOnlyNamespace(SymbolRef symbol) =>
            GetNamespaceMembersForSerialization(symbol).TrueForAll(m => (c.GetSymbolFlags(c.ResolveSymbol(m)) & SymbolFlags.Value) == 0);

        private void SerializeModule(SymbolRef symbol, string symbolName, ModifierFlags modifierFlags)
        {
            var members = GetNamespaceMembersForSerialization(symbol);
            // Split NS members up by declaration - members whose parent symbol is the ns symbol vs those whose is not (but were added in later via merging)
            var realMembers = members.FindAll(m => !c.ParentOf(m).IsNone && c.ParentOf(m) == symbol);
            var mergedMembers = members.FindAll(m => !(!c.ParentOf(m).IsNone && c.ParentOf(m) == symbol));
            // TODO: `suppressNewPrivateContext` is questionable -we need to simply be emitting privates in whatever scope they were declared in, rather
            // than whatever scope we traverse to them in.
            if (realMembers.Count > 0)
            {
                var localText = GetInternalSymbolName(symbol, symbolName);
                var localName = f.CreateIdentifier(localText);
                context.ApproximateLength += localText.Length;
                SerializeAsNamespaceDeclaration(realMembers, localName, modifierFlags, (c.SymbolFlagsOf(symbol) & (SymbolFlags.Function | SymbolFlags.Assignment)) != 0);
            }
            if (mergedMembers.Count > 0)
            {
                var containingFile = context.EnclosingDeclaration.IsNone ? 0 : context.EnclosingDeclaration.File;
                var localName = GetInternalSymbolName(symbol, symbolName);
                var specifiers = new List<Node>();
                foreach (var s in mergedMembers)
                {
                    if (c.NameOf(s) == InternalSymbolName.ExportEquals)
                    {
                        continue;
                    }
                    var name = Utilities.UnescapeLeadingUnderscores(c.NameOf(s));
                    var sLocalName = GetInternalSymbolName(s, name);
                    var sDeclarations = c.DeclarationsOf(s);
                    var aliasDecl = sDeclarations is not null ? c.GetDeclarationOfAliasSymbol(s) : NodeRef.None;
                    if (containingFile != 0 && (!aliasDecl.IsNone ? containingFile != aliasDecl.File : !(sDeclarations ?? []).Exists(d => d.File == containingFile)))
                    {
                        context.Tracker.ReportNonlocalAugmentation(new NodeRef(containingFile, c.FileAt(containingFile).Root), symbol, s);
                        continue;
                    }
                    var target = aliasDecl.IsNone ? SymbolRef.None : c.GetTargetOfAliasDeclaration(aliasDecl, dontRecursivelyResolve: true);
                    IncludePrivateSymbol(!target.IsNone ? target : s);
                    var targetName = !target.IsNone ? GetInternalSymbolName(target, Utilities.UnescapeLeadingUnderscores(c.NameOf(target))) : sLocalName;
                    specifiers.Add(f.CreateExportSpecifier(isTypeOnly: false, name == targetName ? Node.None : f.CreateIdentifier(targetName), f.CreateIdentifier(name)));
                }
                var nsBody = f.CreateModuleBlock(NodeArray(f.CreateExportDeclaration(default, isTypeOnly: false, f.CreateNamedExports(f.CreateNodeArray(specifiers)), Node.None)));
                AddResult(f.CreateModuleDeclaration(default, f.CreateIdentifier(localName), nsBody, NodeFlags.Namespace), ModifierFlags.None);
            }
        }

        private void SerializeEnum(SymbolRef symbol, string symbolName, ModifierFlags modifierFlags)
        {
            var internalSymbolName = GetInternalSymbolName(symbol, symbolName);
            var members = new List<Node>();
            foreach (var p in c.GetPropertiesOfType(c.GetTypeOfSymbol(symbol)))
            {
                if ((c.SymbolFlagsOf(p) & SymbolFlags.EnumMember) == 0)
                {
                    continue;
                }
                // TODO: Handle computed names
                var declarations = c.DeclarationsOf(p);
                var memberDecl = declarations is { Count: > 0 } d && c.Kind(d[0]) == SyntaxKind.EnumMember ? d[0] : NodeRef.None;
                var initializedValue = memberDecl.IsNone ? null : c.GetConstantValueOf(memberDecl);
                var initializer = initializedValue switch
                {
                    string s => StringLiteral(s),
                    double n => f.CreateNumericLiteral(n),
                    _ => Node.None,
                };
                var memberName = Utilities.UnescapeLeadingUnderscores(c.NameOf(p));
                members.Add(f.CreateEnumMember(MemberName(memberName), initializer));
            }
            AddResult(
                f.CreateEnumDeclaration(
                    f.CreateModifiersFromModifierFlags((c.SymbolFlagsOf(symbol) & SymbolFlags.ConstEnum) != 0 ? ModifierFlags.Const : 0),
                    f.CreateIdentifier(internalSymbolName),
                    f.CreateNodeArray(members)),
                modifierFlags);
        }

        // factory.createEnumMember(name: string): asName (an identifier)
        private Node MemberName(string name) => f.CreateIdentifier(name);

        private void SerializeAsFunctionNamespaceMerge(Type type, SymbolRef symbol, string localName, ModifierFlags modifierFlags)
        {
            var signatures = c.GetSignaturesOfType(type, SignatureKind.Call);
            foreach (var sig in signatures)
            {
                context.ApproximateLength += 1; // ;
                // Each overload becomes a separate function declaration, in order
                var decl = c.SignatureToSignatureDeclarationHelper(sig, SyntaxKind.FunctionDeclaration, context, name: f.CreateIdentifier(localName));
                AddResult(SetTextRange(decl, GetSignatureTextRangeLocation(sig)), modifierFlags);
            }
            // Module symbol emit will take care of module-y members, provided it has exports
            if (!((c.SymbolFlagsOf(symbol) & (SymbolFlags.ValueModule | SymbolFlags.NamespaceModule)) != 0 && (c.ExportsOf(symbol)?.Count ?? 0) > 0))
            {
                var props = new List<SymbolRef>();
                foreach (var p in c.GetPropertiesOfType(type))
                {
                    if (IsNamespaceMember(p))
                    {
                        props.Add(p);
                    }
                }
                context.ApproximateLength += localName.Length;
                SerializeAsNamespaceDeclaration(props, f.CreateIdentifier(localName), modifierFlags, suppressNewPrivateContext: true);
            }
        }

        private NodeRef GetSignatureTextRangeLocation(Signature signature)
        {
            var declaration = c.Signatures.Record(signature).Declaration;
            if (!declaration.IsNone)
            {
                var parent = c.Parent(declaration);
                if (!parent.IsNone)
                {
                    var pstore = c.StoreOf(parent);
                    if (pstore.Kind(parent.Node) == SyntaxKind.BinaryExpression && Utilities.GetAssignmentDeclarationKind(pstore, parent.Node) == AssignmentDeclarationKind.Property)
                    {
                        return parent;
                    }
                    // for expressions assigned to `var`s, use the `var` as the text range
                    if (pstore.Kind(parent.Node) == SyntaxKind.VariableDeclaration && !pstore.Parent(parent.Node).IsNone)
                    {
                        return c.Parent(parent);
                    }
                }
            }
            return declaration;
        }

        private void SerializeAsNamespaceDeclaration(List<SymbolRef> props, Node localName, ModifierFlags modifierFlags, bool suppressNewPrivateContext)
        {
            var nodeFlags = S.Kind(localName) == SyntaxKind.Identifier ? NodeFlags.Namespace : NodeFlags.None;
            if (props.Count == 0)
            {
                return; // (else if (expanding): not expanding)
            }
            context.ApproximateLength += 14; // "namespace { }"
            var enclosingFile = context.EnclosingDeclaration.IsNone ? 0 : context.EnclosingDeclaration.File;
            var localProps = props.FindAll(p => (c.DeclarationsOf(p) ?? []).Count == 0 || (c.DeclarationsOf(p) ?? []).Exists(d => d.File == enclosingFile));
            // handle remote props first - we need to make an `import` declaration that points at the module containing each remote
            // prop in the outermost scope (TODO: a namespace within a namespace would need to be appropriately handled by this)

            // Add a namespace
            // Create namespace as non-synthetic so it is usable as an enclosing declaration (Track O: a fake scope with the
            // properties as locals over the root enclosing declaration)
            var fakeScope = c.CreateFakeNamespaceScope(props);
            var oldResults = results;
            results = [];
            var oldAddingDeclare = addingDeclare;
            addingDeclare = false;
            var oldContext = context;
            var subcontext = context.Clone();
            subcontext.EnclosingDeclaration = enclosingDeclaration;
            subcontext.FakeScope = fakeScope;
            context = subcontext;
            // TODO: implement handling for the localVsRemoteMap.get("remote") - should be difficult to trigger (see comment above), as only interesting cross-file js merges should make this possible
            VisitSymbolTable(localProps, suppressNewPrivateContext, propertyAsAlias: true);
            context = oldContext;
            addingDeclare = oldAddingDeclare;
            var declarations = results;
            results = oldResults;
            // replace namespace with synthetic version
            var defaultReplaced = new List<Node>();
            foreach (var d in declarations)
            {
                if (S.Kind(d) == SyntaxKind.ExportAssignment && !S.IsExportEquals(d) && S.Kind(S.Expression(d)) == SyntaxKind.Identifier)
                {
                    defaultReplaced.Add(f.CreateExportDeclaration(default, isTypeOnly: false,
                        f.CreateNamedExports(NodeArray(f.CreateExportSpecifier(isTypeOnly: false, S.Expression(d), f.CreateIdentifier(InternalSymbolName.Default)))), Node.None));
                }
                else
                {
                    defaultReplaced.Add(d);
                }
            }
            var exportModifierStripped = defaultReplaced.TrueForAll(d => Utilities.HasSyntacticModifier(S, d, ModifierFlags.Export))
                ? defaultReplaced.ConvertAll(RemoveExportModifier)
                : defaultReplaced;
            var fakespace = f.CreateModuleDeclaration(default, localName, f.CreateModuleBlock(f.CreateNodeArray(exportModifierStripped)), nodeFlags);
            AddResult(fakespace, modifierFlags); // namespaces can never be default exported
        }

        private bool IsNamespaceMember(SymbolRef p)
        {
            var flags = c.SymbolFlagsOf(p);
            if ((flags & (SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias)) != 0)
            {
                return true;
            }
            var valueDeclaration = c.ValueDeclarationOf(p);
            return !((flags & SymbolFlags.Prototype) != 0 || c.NameOf(p) == "prototype" ||
                (!valueDeclaration.IsNone && Utilities.IsStatic(c.StoreOf(valueDeclaration), valueDeclaration.Node) && Utilities.IsClassLike(c.StoreOf(valueDeclaration), c.StoreOf(valueDeclaration).Parent(valueDeclaration.Node))));
        }

        private List<Node>? SanitizeJSDocImplements(List<NodeRef> clauses)
        {
            var result = new List<Node>();
            foreach (var e in clauses)
            {
                var oldEnclosing = context.EnclosingDeclaration;
                var oldFakeScope = context.FakeScope;
                context.EnclosingDeclaration = e;
                context.FakeScope = null;
                var estore = c.StoreOf(e);
                var expr = estore.Expression(e.Node);
                Node exprNode;
                var ok = true;
                if (Utilities.IsEntityNameExpression(estore, expr))
                {
                    if (estore.Kind(expr) == SyntaxKind.Identifier && string.IsNullOrEmpty(estore.Texts.GetString(estore.EscapedText(expr))))
                    {
                        ok = false; // Empty heritage clause, should be an error, but prefer emitting no heritage clauses to reemitting the empty one
                        exprNode = Node.None;
                    }
                    else
                    {
                        var (introducesError, node) = c.TrackExistingEntityName(context, At(e, expr));
                        exprNode = node;
                        if (introducesError)
                        {
                            ok = false;
                        }
                    }
                }
                else
                {
                    exprNode = c.CopyExistingNode(At(e, expr));
                }
                if (ok)
                {
                    var typeArguments = NodeList.None;
                    if (!estore.TypeArguments(e.Node).IsNone)
                    {
                        var args = new List<Node>();
                        foreach (var a in estore.Nodes(estore.TypeArguments(e.Node)))
                        {
                            var reused = c.TryReuseExistingTypeNodeChecked(context, At(e, a));
                            args.Add(!reused.IsNone ? reused : c.TypeToTypeNodeHelper(c.GetTypeFromTypeNodeInContext(context, At(e, a)), context));
                        }
                        typeArguments = f.CreateNodeArray(args);
                    }
                    result.Add(f.CreateExpressionWithTypeArguments(exprNode, typeArguments));
                }
                context.EnclosingDeclaration = oldEnclosing;
                context.FakeScope = oldFakeScope;
            }
            if (result.Count == clauses.Count)
            {
                return result;
            }
            return null;
        }

        private void SerializeAsClass(SymbolRef symbol, string localName, ModifierFlags modifierFlags)
        {
            context.ApproximateLength += 9 + localName.Length; // `class localName { }`
            var originalDecl = NodeRef.None;
            foreach (var d in c.DeclarationsOf(symbol) ?? [])
            {
                if (Utilities.IsClassLike(c.StoreOf(d), d.Node))
                {
                    originalDecl = d;
                    break;
                }
            }
            var oldEnclosing = context.EnclosingDeclaration;
            var oldFakeScope = context.FakeScope;
            if (!originalDecl.IsNone)
            {
                context.EnclosingDeclaration = originalDecl;
                context.FakeScope = null;
            }
            var localParams = c.GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol);
            var typeParamDecls = TypeParamDecls(localParams);
            var classType = c.GetTypeWithThisArgument(c.GetDeclaredTypeOfClassOrInterface(symbol));
            var baseTypes = c.GetBaseTypes(classType);
            List<Node>? implementsExpressions = null;
            if (!originalDecl.IsNone)
            {
                var originalImplements = GetImplementsTypeNodes(c.StoreOf(originalDecl), originalDecl.Node).ConvertAll(n => At(originalDecl, n));
                implementsExpressions = SanitizeJSDocImplements(originalImplements);
            }
            if (implementsExpressions is null)
            {
                implementsExpressions = [];
                foreach (var t in GetImplementsTypes(classType))
                {
                    var r = SerializeImplementedType(t);
                    if (!r.IsNone)
                    {
                        implementsExpressions.Add(r);
                    }
                }
            }
            var staticType = c.GetTypeOfSymbol(symbol);
            var staticSymbol = c.Types.Record(staticType).Symbol;
            var staticValueDeclaration = staticSymbol.IsNone ? NodeRef.None : c.ValueDeclarationOf(staticSymbol);
            var isClass = !staticValueDeclaration.IsNone && Utilities.IsClassLike(c.StoreOf(staticValueDeclaration), staticValueDeclaration.Node);
            var staticBaseType = isClass ? c.GetBaseConstructorTypeOfClass(staticType) : c.anyType;
            var heritageClauses = new List<Node>();
            if (baseTypes.Count > 0)
            {
                var types = new List<Node>();
                foreach (var b in baseTypes)
                {
                    types.Add(SerializeBaseType(b, staticBaseType, localName));
                }
                heritageClauses.Add(f.CreateHeritageClause(SyntaxKind.ExtendsKeyword, f.CreateNodeArray(types)));
            }
            if (implementsExpressions.Count > 0)
            {
                heritageClauses.Add(f.CreateHeritageClause(SyntaxKind.ImplementsKeyword, f.CreateNodeArray(implementsExpressions)));
            }
            var symbolProps = GetNonInheritedProperties(classType, baseTypes, [.. c.GetPropertiesOfType(classType)]);            var publicSymbolProps = symbolProps.FindAll(s => !IsHashPrivate(s));
            var hasPrivateIdentifier = symbolProps.Exists(IsHashPrivate);
            // Boil down all private properties into a single one.
            var privateProperties = hasPrivateIdentifier
                ? [f.CreatePropertyDeclaration(NodeList.None, f.CreatePrivateIdentifier(S.Texts.Add("#private")), Node.None, Node.None, Node.None)]
                : new List<Node>();
            var baseType0 = baseTypes.Count > 0 ? baseTypes[0] : Type.None;
            var publicProperties = SerializePropertySymbolsForClassOrInterface(publicSymbolProps, isClass: true, baseType0, isStatic: false);
            // Consider static members empty if symbol also has function or module meaning - function namespacey emit will handle statics
            var staticProps = new List<SymbolRef>();
            foreach (var p in c.GetPropertiesOfType(staticType))
            {
                if ((c.SymbolFlagsOf(p) & SymbolFlags.Prototype) == 0 && c.NameOf(p) != "prototype" && !IsNamespaceMember(p))
                {
                    staticProps.Add(p);
                }
            }
            var staticMembers = SerializePropertySymbolsForClassOrInterface(staticProps, isClass: true, staticBaseType, isStatic: true);
            // When we encounter an `X.prototype.y` assignment in a JS file, we bind `X` as a class regardless as to whether
            // the value is ever initialized with a class or function-like value. For cases where `X` could never be
            // created via `new`, we will inject a `private constructor()` declaration to indicate it is not createable.
            var symbolValueDeclaration = c.ValueDeclarationOf(symbol);
            var isNonConstructableClassLikeInJsFile = !isClass &&
                !symbolValueDeclaration.IsNone &&
                Utilities.IsInJSFile(c.StoreOf(symbolValueDeclaration), symbolValueDeclaration.Node) &&
                c.GetSignaturesOfType(staticType, SignatureKind.Construct).Count == 0;
            var constructors = isNonConstructableClassLikeInJsFile
                ? [f.CreateConstructorDeclaration(f.CreateModifiersFromModifierFlags(ModifierFlags.Private), f.CreateNodeArray(ReadOnlySpan<Node>.Empty), Node.None)]
                : SerializeSignatures(SignatureKind.Construct, staticType, staticBaseType, SyntaxKind.Constructor);
            var indexSignatures = SerializeIndexSignatures(classType, baseType0);
            context.EnclosingDeclaration = oldEnclosing;
            context.FakeScope = oldFakeScope;
            var members = new List<Node>();
            members.AddRange(indexSignatures);
            members.AddRange(staticMembers);
            members.AddRange(constructors);
            members.AddRange(publicProperties);
            members.AddRange(privateProperties);
            var classTextRange = NodeRef.None;
            foreach (var d in c.DeclarationsOf(symbol) ?? [])
            {
                if (c.Kind(d) is SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression)
                {
                    classTextRange = d;
                    break;
                }
            }
            AddResult(
                SetTextRange(
                    f.CreateClassDeclaration(default, f.CreateIdentifier(localName), typeParamDecls, f.CreateNodeArray(heritageClauses), f.CreateNodeArray(members)),
                    classTextRange),
                modifierFlags);
        }

        // checker.ts getImplementsTypes
        private List<Type> GetImplementsTypes(Type type)
        {
            var resolvedImplementsTypes = new List<Type>();
            var symbol = c.Types.Record(type).Symbol;
            foreach (var declaration in c.DeclarationsOf(symbol) ?? [])
            {
                foreach (var node in GetImplementsTypeNodes(c.StoreOf(declaration), declaration.Node))
                {
                    var implementsType = c.GetTypeFromTypeNode(At(declaration, node));
                    if (!c.IsErrorType(implementsType))
                    {
                        resolvedImplementsTypes.Add(implementsType);
                    }
                }
            }
            return resolvedImplementsTypes;
        }

        // checker.ts getNonInheritedProperties
        private List<SymbolRef> GetNonInheritedProperties(Type type, Items<Type> baseTypes, List<SymbolRef> properties)
        {
            if (baseTypes.Count == 0)
            {
                return properties;
            }
            var order = new List<string>();
            var seen = new Dictionary<string, SymbolRef>(StringComparer.Ordinal);
            foreach (var s in properties)
            {
                var name = c.NameOf(s);
                if (!seen.ContainsKey(name))
                {
                    order.Add(name);
                }
                seen[name] = s;
            }
            foreach (var b in baseTypes)
            {
                foreach (var prop in c.GetPropertiesOfType(c.GetTypeWithThisArgument(b, c.Types.Object(type).ThisType)))
                {
                    var name = c.NameOf(prop);
                    if (seen.TryGetValue(name, out var existing) && c.ParentOf(prop) == c.ParentOf(existing))
                    {
                        seen.Remove(name);
                    }
                }
            }
            var result = new List<SymbolRef>();
            foreach (var name in order)
            {
                if (seen.TryGetValue(name, out var s))
                {
                    result.Add(s);
                }
            }
            return result;
        }

        private bool IsHashPrivate(SymbolRef s)
        {
            var valueDeclaration = c.ValueDeclarationOf(s);
            if (valueDeclaration.IsNone)
            {
                return false;
            }
            var store = c.StoreOf(valueDeclaration);
            var name = Utilities.GetNameOfDeclaration(store, valueDeclaration.Node);
            return !name.IsNone && store.Kind(name) == SyntaxKind.PrivateIdentifier;
        }

        private string? GetSomeTargetNameFromDeclarations(Decls? declarations)
        {
            foreach (var d in declarations ?? [])
            {
                var store = c.StoreOf(d);
                var kind = store.Kind(d.Node);
                if (kind is SyntaxKind.ImportSpecifier or SyntaxKind.ExportSpecifier)
                {
                    var n = !store.PropertyName(d.Node).IsNone ? store.PropertyName(d.Node) : store.Name(d.Node);
                    return store.Kind(n) == SyntaxKind.StringLiteral ? store.Texts.GetString(store.Text(n)) : Utilities.UnescapeLeadingUnderscores(store.Texts.GetString(store.EscapedText(n))!);
                }
                if (kind is SyntaxKind.BinaryExpression or SyntaxKind.ExportAssignment)
                {
                    var expression = kind == SyntaxKind.ExportAssignment ? store.Expression(d.Node) : store.Right(d.Node);
                    if (store.Kind(expression) == SyntaxKind.PropertyAccessExpression)
                    {
                        return store.Texts.GetString(Utilities.IdText(store, store.Name(expression)));
                    }
                }
                if (c.IsAliasSymbolDeclaration(d))
                {
                    // This is... heuristic, at best. But it's probably better than always printing the name of the shorthand ambient module.
                    var name = Utilities.GetNameOfDeclaration(store, d.Node);
                    if (!name.IsNone && store.Kind(name) == SyntaxKind.Identifier)
                    {
                        return store.Texts.GetString(Utilities.IdText(store, name));
                    }
                }
            }
            return null;
        }

        private void SerializeAsAlias(SymbolRef symbol, string localName, ModifierFlags modifierFlags)
        {
            // synthesize an alias, eg `export { symbolName as Name }`
            // need to mark the alias `symbol` points at
            // as something we need to serialize as a private declaration as well
            var node = c.GetDeclarationOfAliasSymbol(symbol);
            if (node.IsNone)
            {
                throw new InvalidOperationException("Debug Failure.");
            }
            var target = c.GetMergedSymbol(c.GetTargetOfAliasDeclaration(node, dontRecursivelyResolve: true));
            if (target.IsNone)
            {
                return;
            }
            // If `target` refers to a shorthand module symbol, the name we're trying to pull out isn;t recoverable from the target symbol
            // In such a scenario, we must fall back to looking for an alias declaration on `symbol` and pulling the target name from that
            var verbatimTargetName = (c.IsShorthandAmbientModuleSymbol(target) ? GetSomeTargetNameFromDeclarations(c.DeclarationsOf(symbol)) : null) ?? Utilities.UnescapeLeadingUnderscores(c.NameOf(target));
            if (verbatimTargetName == InternalSymbolName.ExportEquals && c.compilerOptions.GetAllowSyntheticDefaultImports())
            {
                // target refers to an `export=` symbol that was hoisted into a synthetic default - rename here to match
                verbatimTargetName = InternalSymbolName.Default;
            }
            var targetName = GetInternalSymbolName(target, verbatimTargetName);
            IncludePrivateSymbol(target); // the target may be within the same scope - attempt to serialize it first
            var store = c.StoreOf(node);
            var n = node.Node;
            switch (store.Kind(n))
            {
                case SyntaxKind.BindingElement:
                {
                    var p = store.Parent(n);
                    var pp = p.IsNone ? Node.None : store.Parent(p);
                    if (!pp.IsNone && store.Kind(pp) == SyntaxKind.VariableDeclaration)
                    {
                        // const { SomeClass } = require('./lib');
                        var specifier = c.GetSpecifierForModuleSymbol(!c.ParentOf(target).IsNone ? c.ParentOf(target) : target, context); // './lib'
                        var propertyName = store.PropertyName(n);
                        var propertyNameText = !propertyName.IsNone && store.Kind(propertyName) == SyntaxKind.Identifier ? store.Texts.GetString(Utilities.IdText(store, propertyName)) : null;
                        AddResult(
                            f.CreateImportDeclaration(
                                default,
                                f.CreateImportClause(null, Node.None,
                                    f.CreateNamedImports(NodeArray(f.CreateImportSpecifier(isTypeOnly: false,
                                        propertyNameText is not null ? f.CreateIdentifier(propertyNameText) : Node.None,
                                        f.CreateIdentifier(localName))))),
                                StringLiteral(specifier),
                                Node.None),
                            ModifierFlags.None);
                        break;
                    }
                    // We don't know how to serialize this (nested?) binding element
                    throw new InvalidOperationException("Debug Failure. Unhandled binding element grandparent kind in declaration serialization");
                }
                case SyntaxKind.ShorthandPropertyAssignment:
                {
                    var pp = store.Parent(store.Parent(n));
                    if (!pp.IsNone && store.Kind(pp) == SyntaxKind.BinaryExpression)
                    {
                        // module.exports = { SomeClass }
                        SerializeExportSpecifier(Utilities.UnescapeLeadingUnderscores(c.NameOf(symbol)), targetName);
                    }
                    break;
                }
                case SyntaxKind.VariableDeclaration when store.Kind(store.Initializer(n)) == SyntaxKind.PropertyAccessExpression:
                {
                    // commonjs require: const x = require('y').z
                    var initializer = store.Initializer(n); // require('y').z
                    var uniqueName = f.CreateUniqueName(localName); // _x
                    var specifier = c.GetSpecifierForModuleSymbol(!c.ParentOf(target).IsNone ? c.ParentOf(target) : target, context); // 'y'
                    // import _x = require('y');
                    AddResult(f.CreateImportEqualsDeclaration(default, isTypeOnly: false, uniqueName, f.CreateExternalModuleReference(StringLiteral(specifier))), ModifierFlags.None);
                    // import x = _x.z
                    var initializerName = store.Texts.GetString(store.EscapedText(store.Name(initializer)))!;
                    AddResult(f.CreateImportEqualsDeclaration(default, isTypeOnly: false, f.CreateIdentifier(localName), f.CreateQualifiedName(uniqueName, f.CreateIdentifier(initializerName))), modifierFlags);
                    break;
                }
                case SyntaxKind.VariableDeclaration:
                case SyntaxKind.ImportEqualsDeclaration:
                {
                    // This _specifically_ only exists to handle json declarations - where we make aliases, but since
                    // we emit no declarations for the json document, must not refer to it in the declarations
                    if (c.NameOf(target) == InternalSymbolName.ExportEquals && (c.DeclarationsOf(target) ?? []).Exists(d => c.Kind(d) == SyntaxKind.SourceFile && c.StoreOf(d).ScriptKind(d.Node) == ScriptKind.JSON))
                    {
                        SerializeMaybeAliasAssignment(symbol);
                        break;
                    }
                    // Could be a local `import localName = ns.member` or
                    // an external `import localName = require("whatever")`
                    var isLocalImport = (c.SymbolFlagsOf(target) & SymbolFlags.ValueModule) == 0 && store.Kind(n) != SyntaxKind.VariableDeclaration;
                    AddResult(
                        f.CreateImportEqualsDeclaration(
                            default,
                            isTypeOnly: false,
                            f.CreateIdentifier(localName),
                            isLocalImport
                                ? c.SymbolToName(target, context, SymbolFlags.All, expectsIdentifier: false)
                                : f.CreateExternalModuleReference(StringLiteral(c.GetSpecifierForModuleSymbol(target, context)))),
                        isLocalImport ? modifierFlags : ModifierFlags.None);
                    break;
                }
                case SyntaxKind.NamespaceExportDeclaration:
                    // export as namespace foo
                    // TODO: Not part of a file's local or export symbol tables
                    // Is bound into file.symbol.globalExports instead, which we don't currently traverse
                    AddResult(f.CreateNamespaceExportDeclaration(f.CreateIdentifier(store.Texts.GetString(Utilities.IdText(store, store.Name(n)))!)), ModifierFlags.None);
                    break;
                case SyntaxKind.ImportClause:
                {
                    _ = c.GetSpecifierForModuleSymbol(!c.ParentOf(target).IsNone ? c.ParentOf(target) : target, context); // generate specifier (even though we're reusing and existing one) for ambient module reference include side effects
                    var importDecl = store.Parent(n);
                    var specifier = CopyLiteral(At(node, store.ModuleSpecifier(importDecl)));
                    var attributes = store.Kind(importDecl) == SyntaxKind.ImportDeclaration ? CopyAttributes(At(node, store.Attributes(importDecl))) : Node.None;
                    var isTypeOnly = store.Kind(importDecl) == SyntaxKind.JSDocImportTag;
                    AddResult(
                        f.CreateImportDeclaration(default,
                            f.CreateImportClause(isTypeOnly ? SyntaxKind.TypeKeyword : null, f.CreateIdentifier(localName), Node.None),
                            specifier,
                            attributes),
                        ModifierFlags.None);
                    break;
                }
                case SyntaxKind.NamespaceImport:
                {
                    _ = c.GetSpecifierForModuleSymbol(!c.ParentOf(target).IsNone ? c.ParentOf(target) : target, context);
                    var importDecl = store.Parent(store.Parent(n));
                    var specifier = CopyLiteral(At(node, store.ModuleSpecifier(importDecl)));
                    var isTypeOnly = store.Kind(importDecl) == SyntaxKind.JSDocImportTag;
                    AddResult(
                        f.CreateImportDeclaration(default,
                            f.CreateImportClause(isTypeOnly ? SyntaxKind.TypeKeyword : null, Node.None, f.CreateNamespaceImport(f.CreateIdentifier(localName))),
                            specifier,
                            CopyAttributes(At(node, store.Attributes(store.Parent(n))))),
                        ModifierFlags.None);
                    break;
                }
                case SyntaxKind.NamespaceExport:
                    AddResult(
                        f.CreateExportDeclaration(default, isTypeOnly: false, f.CreateNamespaceExport(f.CreateIdentifier(localName)), StringLiteral(c.GetSpecifierForModuleSymbol(target, context))),
                        ModifierFlags.None);
                    break;
                case SyntaxKind.ImportSpecifier:
                {
                    _ = c.GetSpecifierForModuleSymbol(!c.ParentOf(target).IsNone ? c.ParentOf(target) : target, context);
                    var importDecl = store.Parent(store.Parent(store.Parent(n)));
                    var specifier = CopyLiteral(At(node, store.ModuleSpecifier(importDecl)));
                    var isTypeOnly = store.Kind(importDecl) == SyntaxKind.JSDocImportTag;
                    AddResult(
                        f.CreateImportDeclaration(default,
                            f.CreateImportClause(isTypeOnly ? SyntaxKind.TypeKeyword : null, Node.None,
                                f.CreateNamedImports(NodeArray(f.CreateImportSpecifier(isTypeOnly: false,
                                    localName != verbatimTargetName ? f.CreateIdentifier(verbatimTargetName) : Node.None,
                                    f.CreateIdentifier(localName))))),
                            specifier,
                            CopyAttributes(At(node, store.Attributes(importDecl)))),
                        ModifierFlags.None);
                    break;
                }
                case SyntaxKind.ExportSpecifier:
                {
                    // does not use localName because the symbol name in this case refers to the name in the exports table,
                    // which we must exactly preserve
                    var specifier = store.ModuleSpecifier(store.Parent(store.Parent(n)));
                    if (!specifier.IsNone)
                    {
                        var propertyName = store.PropertyName(n);
                        if (!propertyName.IsNone && ModuleExportNameIsDefault(store, propertyName))
                        {
                            verbatimTargetName = InternalSymbolName.Default;
                        }
                    }
                    // targetName is only used when the target is local, as otherwise the target is an alias that points at
                    // another file
                    SerializeExportSpecifier(
                        Utilities.UnescapeLeadingUnderscores(c.NameOf(symbol)),
                        !specifier.IsNone ? verbatimTargetName : targetName,
                        !specifier.IsNone && UtilitiesPublic.IsStringLiteralLike(store, specifier) ? StringLiteral(store.Texts.GetString(store.Text(specifier))!) : Node.None);
                    break;
                }
                case SyntaxKind.ExportAssignment:
                    SerializeMaybeAliasAssignment(symbol);
                    break;
                case SyntaxKind.BinaryExpression:
                case SyntaxKind.PropertyAccessExpression:
                case SyntaxKind.ElementAccessExpression:
                    // Could be best encoded as though an export specifier or as though an export assignment
                    // If name is default or export=, do an export assignment
                    // Otherwise do an export specifier
                    if (c.NameOf(symbol) == InternalSymbolName.Default || c.NameOf(symbol) == InternalSymbolName.ExportEquals)
                    {
                        SerializeMaybeAliasAssignment(symbol);
                    }
                    else
                    {
                        SerializeExportSpecifier(localName, targetName);
                    }
                    break;
                default:
                    throw new InvalidOperationException("Debug Failure. Unhandled alias declaration kind in symbol serializer!");
            }
        }

        // utilities.ts moduleExportNameIsDefault
        private static bool ModuleExportNameIsDefault(NodeStore store, Node node) =>
            (store.Kind(node) == SyntaxKind.StringLiteral ? store.Texts.GetString(store.Text(node)) : store.Texts.GetString(store.EscapedText(node))) == InternalSymbolName.Default;

        // A parse tree literal of the file reused in the output (TypeScript reuses the node itself; the emit copy has it
        // under the same handle when the file is the emitted one)
        private Node CopyLiteral(NodeRef literal)
        {
            if (literal.IsNone)
            {
                return Node.None;
            }
            if (literal.File == context.EnclosingFile)
            {
                return literal.Node;
            }
            return c.CopyExistingNode(literal);
        }

        private Node CopyAttributes(NodeRef attributes) => attributes.IsNone ? Node.None : attributes.File == context.EnclosingFile ? attributes.Node : Node.None;

        private void SerializeExportSpecifier(string localName, string targetName, Node specifier = default)
        {
            AddResult(
                f.CreateExportDeclaration(default, isTypeOnly: false,
                    f.CreateNamedExports(NodeArray(f.CreateExportSpecifier(isTypeOnly: false, localName != targetName ? f.CreateIdentifier(targetName) : Node.None, f.CreateIdentifier(localName)))),
                    specifier),
                ModifierFlags.None);
        }

        /// <summary>Returns `true` if an export assignment or declaration was produced for the symbol</summary>
        private bool SerializeMaybeAliasAssignment(SymbolRef symbol)
        {
            if ((c.SymbolFlagsOf(symbol) & SymbolFlags.Prototype) != 0)
            {
                return false;
            }
            var name = Utilities.UnescapeLeadingUnderscores(c.NameOf(symbol));
            var isExportEquals = name == InternalSymbolName.ExportEquals;
            var isDefault = name == InternalSymbolName.Default;
            var isExportAssignmentCompatibleSymbolName = isExportEquals || isDefault;
            // synthesize export = ref
            // ref should refer to either be a locally scoped symbol which we need to emit, or
            // a reference to another namespace/module which we may need to emit an `import` statement for
            var aliasDecl = c.DeclarationsOf(symbol) is not null ? c.GetDeclarationOfAliasSymbol(symbol) : NodeRef.None;
            // serialize what the alias points to, preserve the declaration's initializer
            var target = aliasDecl.IsNone ? SymbolRef.None : c.GetTargetOfAliasDeclaration(aliasDecl, dontRecursivelyResolve: true);
            // If the target resolves and resolves to a thing defined in this file, emit as an alias, otherwise emit as a const
            if (!target.IsNone && (c.DeclarationsOf(target) ?? []).Exists(d => d.File == enclosingDeclaration.File))
            {
                // In case `target` refers to a namespace member, look at the declaration and serialize the leftmost symbol in it
                // eg, `namespace A { export class B {} }; exports = A.B;`
                // Technically, this is all that's required in the case where the assignment is an entity name expression
                var astore = c.StoreOf(aliasDecl);
                var akind = astore.Kind(aliasDecl.Node);
                var expr = akind is SyntaxKind.ExportAssignment or SyntaxKind.BinaryExpression
                    ? (akind == SyntaxKind.ExportAssignment ? astore.Expression(aliasDecl.Node) : astore.Right(aliasDecl.Node))
                    : akind == SyntaxKind.ShorthandPropertyAssignment ? astore.Name(aliasDecl.Node)
                    : akind == SyntaxKind.PropertyAssignment ? astore.Initializer(aliasDecl.Node)
                    : astore.Right(astore.Parent(aliasDecl.Node));
                var first = !expr.IsNone && Utilities.IsEntityNameExpression(astore, expr) ? GetFirstNonModuleExportsIdentifier(astore, expr) : Node.None;
                var referenced = !first.IsNone ? c.ResolveEntityName(At(aliasDecl, first), SymbolFlags.All, ignoreErrors: true, dontResolveAlias: true, enclosingDeclaration) : SymbolRef.None;
                if (!referenced.IsNone || !target.IsNone)
                {
                    IncludePrivateSymbol(!referenced.IsNone ? referenced : target);
                }

                // We disable the context's symbol tracker for the duration of this name serialization
                // as, by virtue of being here, the name is required to print something, and we don't want to
                // issue a visibility error on it.
                var prevDisableTrackSymbol = context.Tracker.DisableTrackSymbol;
                context.Tracker.DisableTrackSymbol = true;
                if (isExportAssignmentCompatibleSymbolName)
                {
                    results.Add(f.CreateExportAssignment(default, isExportEquals, c.SymbolToExpression(target, context, SymbolFlags.All)));
                }
                else
                {
                    if (first == expr && !first.IsNone)
                    {
                        // serialize as `export {target as name}`
                        SerializeExportSpecifier(name, astore.Texts.GetString(Utilities.IdText(astore, first))!);
                    }
                    else if (!expr.IsNone && astore.Kind(expr) == SyntaxKind.ClassExpression)
                    {
                        SerializeExportSpecifier(name, GetInternalSymbolName(target, c.SymbolName(target)));
                    }
                    else
                    {
                        // serialize as `import _Ref = t.arg.et; export { _Ref as name }`
                        var varName = GetUnusedName(name, symbol);
                        AddResult(
                            f.CreateImportEqualsDeclaration(default, isTypeOnly: false, f.CreateIdentifier(varName), c.SymbolToName(target, context, SymbolFlags.All, expectsIdentifier: false)),
                            ModifierFlags.None);
                        SerializeExportSpecifier(name, varName);
                    }
                }
                context.Tracker.DisableTrackSymbol = prevDisableTrackSymbol;
                return true;
            }
            else
            {
                // serialize as an anonymous property declaration
                var varName = GetUnusedName(name, symbol);
                // We have to use `getWidenedType` here since the object within a json file is unwidened within the file
                // (Unwidened types can only exist in expression contexts and should never be serialized)
                var typeToSerialize = c.GetWidenedType(c.GetTypeOfSymbol(c.GetMergedSymbol(symbol)));
                if (IsTypeRepresentableAsFunctionNamespaceMerge(typeToSerialize, symbol))
                {
                    // If there are no index signatures and `typeToSerialize` is an object type, emit as a namespace instead of a const
                    SerializeAsFunctionNamespaceMerge(typeToSerialize, symbol, varName, isExportAssignmentCompatibleSymbolName ? ModifierFlags.None : ModifierFlags.Export);
                }
                else
                {
                    var symbolFlags = c.SymbolFlagsOf(symbol);
                    var enclosingIsModule = IsFakeNamespace(context) || (!context.EnclosingDeclaration.IsNone && c.Kind(context.EnclosingDeclaration) == SyntaxKind.ModuleDeclaration);
                    var flags = enclosingIsModule && ((symbolFlags & SymbolFlags.Accessor) == 0 || (symbolFlags & SymbolFlags.SetAccessor) != 0) ? NodeFlags.Let : NodeFlags.Const;
                    var statement = f.CreateVariableStatement(
                        default,
                        f.CreateVariableDeclarationList(
                            NodeArray(f.CreateVariableDeclaration(f.CreateIdentifier(varName), Node.None, c.SerializeTypeForDeclaration(context, NodeRef.None, typeToSerialize, symbol), Node.None)),
                            flags));
                    // Inlined JSON types exported with [module.]exports= will already emit an export=, so should use `declare`.
                    // Otherwise, the type itself should be exported.
                    AddResult(
                        statement,
                        !target.IsNone && (c.SymbolFlagsOf(target) & SymbolFlags.Property) != 0 && c.NameOf(target) == InternalSymbolName.ExportEquals ? ModifierFlags.Ambient
                            : name == varName ? ModifierFlags.Export
                            : ModifierFlags.None);
                }
                if (isExportAssignmentCompatibleSymbolName)
                {
                    results.Add(f.CreateExportAssignment(default, isExportEquals, f.CreateIdentifier(varName)));
                    return true;
                }
                else if (name != varName)
                {
                    SerializeExportSpecifier(name, varName);
                    return true;
                }
                return false;
            }
        }

        // checker.ts getFirstNonModuleExportsIdentifier
        private static Node GetFirstNonModuleExportsIdentifier(NodeStore store, Node node)
        {
            switch (store.Kind(node))
            {
                case SyntaxKind.Identifier:
                    return node;
                case SyntaxKind.QualifiedName:
                    do
                    {
                        node = store.Left(node);
                    }
                    while (store.Kind(node) != SyntaxKind.Identifier);
                    return node;
                case SyntaxKind.PropertyAccessExpression:
                    do
                    {
                        if (Utilities.IsModuleExportsAccessExpression(store, store.Expression(node)) && store.Kind(store.Name(node)) != SyntaxKind.PrivateIdentifier)
                        {
                            return store.Name(node);
                        }
                        node = store.Expression(node);
                    }
                    while (store.Kind(node) != SyntaxKind.Identifier);
                    return node;
            }
            return Node.None;
        }

        private bool IsTypeRepresentableAsFunctionNamespaceMerge(Type typeToSerialize, SymbolRef hostSymbol)
        {
            // Only object types which are not constructable, or indexable, whose members all come from the
            // context source file, and whose property names are all valid identifiers and not late-bound, _and_
            // whose input is not type annotated (if the input symbol has an annotation we can reuse, we should prefer it)
            var ctxSrc = context.EnclosingDeclaration.IsNone ? 0 : context.EnclosingDeclaration.File;
            if ((c.Types.ObjectFlagsOf(typeToSerialize) & (ObjectFlags.Anonymous | ObjectFlags.Mapped)) == 0)
            {
                return false;
            }
            var typeSymbol = c.Types.Record(typeToSerialize).Symbol;
            var typeSymbolDeclarations = typeSymbol.IsNone ? null : c.DeclarationsOf(typeSymbol);
            if ((typeSymbolDeclarations ?? []).Exists(d => IsTypeNodeKind(c.Kind(d))))
            {
                return false; // If the type comes straight from a type node, we shouldn't try to break it up
            }
            if (c.GetIndexInfosOfType(typeToSerialize).Count > 0)
            {
                return false;
            }
            if (c.IsClassInstanceSide(typeToSerialize))
            {
                return false; // While a class instance is potentially representable as a NS, prefer printing a reference to the instance type and serializing the class
            }
            var properties = new List<SymbolRef>(c.GetPropertiesOfType(typeToSerialize));
            if (!(properties.Exists(IsNamespaceMember) || c.GetSignaturesOfType(typeToSerialize, SignatureKind.Call).Count > 0))
            {
                return false;
            }
            if (c.GetSignaturesOfType(typeToSerialize, SignatureKind.Construct).Count > 0)
            {
                return false; // TODO: could probably serialize as function + ns + class, now that that's OK
            }
            if (HasDeclarationWithTypeAnnotationIn(hostSymbol, enclosingDeclaration))
            {
                return false;
            }
            if ((typeSymbolDeclarations ?? []).Exists(d => d.File != ctxSrc))
            {
                return false;
            }
            if (properties.Exists(p => IsLateBoundName(c.NameOf(p))))
            {
                return false;
            }
            if (properties.Exists(p => (c.DeclarationsOf(p) ?? []).Exists(d => d.File != ctxSrc)))
            {
                return false;
            }
            return properties.TrueForAll(p =>
            {
                if (!Scanner.IsIdentifierText(c.SymbolName(p), c.languageVersion))
                {
                    return false;
                }
                if ((c.SymbolFlagsOf(p) & SymbolFlags.Accessor) == 0)
                {
                    return true;
                }
                return c.GetNonMissingTypeOfSymbol(p) == c.GetWriteTypeOfSymbol(p);
            });
        }

        // checker.ts getDeclarationWithTypeAnnotation(symbol, enclosingDeclaration) is defined
        private bool HasDeclarationWithTypeAnnotationIn(SymbolRef symbol, NodeRef enclosing)
        {
            foreach (var d in c.DeclarationsOf(symbol) ?? [])
            {
                if (!c.GetNonlocalEffectiveTypeAnnotationNode(d).IsNone && (enclosing.IsNone || IsInside(d, enclosing)))
                {
                    return true;
                }
            }
            return false;
        }

        // makeSerializePropertySymbol(createProperty, methodKind, useAccessors)(p, isStatic, baseType): the class form
        // (property declarations, method declarations, accessors) or the interface form (property and method signatures)
        private List<Node> SerializePropertySymbol(SymbolRef p, bool isStatic, Type baseType, bool isClass)
        {
            var useAccessors = isClass;
            var methodKind = isClass ? SyntaxKind.MethodDeclaration : SyntaxKind.MethodSignature;
            var modifierFlags = c.GetDeclarationModifierFlagsFromSymbol(p);
            var omitType = (modifierFlags & ModifierFlags.Private) != 0; // (&& !isExpanding(context))
            var pflags = c.SymbolFlagsOf(p);
            if (isStatic && (pflags & (SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias)) != 0)
            {
                // Only value-only-meaning symbols can be correctly encoded as class statics, type/namespace/alias meaning symbols
                // need to be merged namespace members
                return [];
            }
            if ((pflags & SymbolFlags.Prototype) != 0 || c.NameOf(p) == "constructor")
            {
                return [];
            }
            if (!baseType.IsNone)
            {
                var baseProp = c.GetPropertyOfType(baseType, c.NameOf(p));
                if (!baseProp.IsNone
                    && c.IsReadonlySymbol(baseProp) == c.IsReadonlySymbol(p)
                    && (pflags & SymbolFlags.Optional) == (c.SymbolFlagsOf(baseProp) & SymbolFlags.Optional)
                    && c.IsTypeIdenticalTo(c.GetTypeOfSymbol(p), c.GetTypeOfPropertyOfType(baseType, c.NameOf(p))))
                {
                    return [];
                }
            }
            var flag = (modifierFlags & ~ModifierFlags.Async) | (isStatic ? ModifierFlags.Static : 0);
            var name = c.GetPropertyNameNodeForSymbol(p, context);
            var declarations = c.DeclarationsOf(p);
            var firstPropertyLikeDecl = NodeRef.None;
            foreach (var d in declarations ?? [])
            {
                if (c.Kind(d) is SyntaxKind.PropertyDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor or SyntaxKind.VariableDeclaration or SyntaxKind.PropertySignature or SyntaxKind.BinaryExpression or SyntaxKind.PropertyAccessExpression)
                {
                    firstPropertyLikeDecl = d;
                    break;
                }
            }
            NodeRef FindDeclaration(Func<SyntaxKind, bool> test)
            {
                foreach (var d in declarations ?? [])
                {
                    if (test(c.Kind(d)))
                    {
                        return d;
                    }
                }
                return NodeRef.None;
            }
            if ((pflags & SymbolFlags.Accessor) != 0 && useAccessors)
            {
                var result = new List<Node>();
                if ((pflags & SymbolFlags.SetAccessor) != 0)
                {
                    var setter = NodeRef.None;
                    foreach (var d in declarations ?? [])
                    {
                        if (c.Kind(d) == SyntaxKind.SetAccessor)
                        {
                            setter = d;
                            break;
                        }
                        var dstore = c.StoreOf(d);
                        if (dstore.Kind(d.Node) == SyntaxKind.CallExpression && Utilities.IsBindableObjectDefinePropertyCall(dstore, d.Node))
                        {
                            var descriptor = dstore.Element(dstore.Arguments(d.Node), 2);
                            foreach (var propDecl in dstore.Nodes(dstore.Properties(descriptor)))
                            {
                                var id = Utilities.GetNameOfDeclaration(dstore, propDecl);
                                if (!id.IsNone && dstore.Kind(id) == SyntaxKind.Identifier && dstore.Texts.GetString(Utilities.IdText(dstore, id)) == "set")
                                {
                                    setter = At(d, propDecl);
                                    break;
                                }
                            }
                            if (!setter.IsNone)
                            {
                                break;
                            }
                        }
                    }
                    Debug.Assert(!setter.IsNone);
                    var paramSymbol = Utilities.IsFunctionLikeDeclaration(c.StoreOf(setter), setter.Node) ? FirstParameter(c.GetSignatureFromDeclaration(setter)) : SymbolRef.None;
                    var setterDeclaration = FindDeclaration(k => k == SyntaxKind.SetAccessor);
                    var parameter = f.CreateParameterDeclaration(
                        default,
                        Node.None,
                        !paramSymbol.IsNone ? c.ParameterToParameterDeclarationName(paramSymbol, c.GetEffectiveParameterDeclaration(paramSymbol), context) : f.CreateIdentifier("value"),
                        Node.None,
                        omitType ? Node.None : c.SerializeTypeForDeclaration(context, setterDeclaration, c.GetWriteTypeOfSymbol(p), p),
                        Node.None);
                    result.Add(SetTextRangeBuilder(
                        f.CreateSetAccessorDeclaration(f.CreateModifiersFromModifierFlags(flag), name, NodeArray(parameter), Node.None),
                        !setterDeclaration.IsNone ? setterDeclaration : firstPropertyLikeDecl));
                }
                if ((pflags & SymbolFlags.GetAccessor) != 0)
                {
                    var getterDeclaration = FindDeclaration(k => k == SyntaxKind.GetAccessor);
                    result.Add(SetTextRangeBuilder(
                        f.CreateGetAccessorDeclaration(
                            f.CreateModifiersFromModifierFlags(flag),
                            name,
                            f.CreateNodeArray(ReadOnlySpan<Node>.Empty),
                            omitType ? Node.None : c.SerializeTypeForDeclaration(context, getterDeclaration, c.GetTypeOfSymbol(p), p),
                            Node.None),
                        !getterDeclaration.IsNone ? getterDeclaration : firstPropertyLikeDecl));
                }
                return result;
            }
            // This is an else/if as accessors and properties can't merge in TS, but might in JS
            // If this happens, we assume the accessor takes priority, as it imposes more constraints
            if ((pflags & (SymbolFlags.Property | SymbolFlags.Variable | SymbolFlags.Accessor)) != 0)
            {
                var propertyModifierFlags = (c.IsReadonlySymbol(p) ? ModifierFlags.Readonly : 0) | flag;
                var type = omitType ? Node.None : c.SerializeTypeForDeclaration(context, FindDeclaration(k => k == SyntaxKind.SetAccessor), c.GetWriteTypeOfSymbol(p), p);
                var location = FindDeclaration(k => k is SyntaxKind.PropertyDeclaration or SyntaxKind.VariableDeclaration);
                return [SetTextRangeBuilder(
                    CreateProperty(isClass, f.CreateModifiersFromModifierFlags(propertyModifierFlags), name, (pflags & SymbolFlags.Optional) != 0 ? f.CreateToken(SyntaxKind.QuestionToken) : Node.None, type),
                    !location.IsNone ? location : firstPropertyLikeDecl)];
            }
            if ((pflags & (SymbolFlags.Method | SymbolFlags.Function)) != 0)
            {
                var type = c.GetTypeOfSymbol(p);
                var signatures = c.GetSignaturesOfType(type, SignatureKind.Call);
                if (omitType)
                {
                    var propertyModifierFlags = (c.IsReadonlySymbol(p) ? ModifierFlags.Readonly : 0) | flag;
                    var location = FindDeclaration(k => k is SyntaxKind.FunctionDeclaration or SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor or SyntaxKind.Constructor or SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction);
                    if (location.IsNone && signatures.Count > 0)
                    {
                        location = c.Signatures.Record(signatures[0]).Declaration;
                    }
                    if (location.IsNone && declarations is { Count: > 0 } ds)
                    {
                        location = ds[0];
                    }
                    return [SetTextRangeBuilder(
                        CreateProperty(isClass, f.CreateModifiersFromModifierFlags(propertyModifierFlags), name, (pflags & SymbolFlags.Optional) != 0 ? f.CreateToken(SyntaxKind.QuestionToken) : Node.None, Node.None),
                        location)];
                }
                var results = new List<Node>();
                foreach (var sig in signatures)
                {
                    context.ApproximateLength += 1; // ;
                    // Each overload becomes a separate method declaration, in order
                    var decl = c.SignatureToSignatureDeclarationHelper(
                        sig,
                        methodKind,
                        context,
                        name: name,
                        questionToken: (pflags & SymbolFlags.Optional) != 0 ? f.CreateToken(SyntaxKind.QuestionToken) : Node.None,
                        modifiers: flag != 0 ? f.CreateModifiersFromModifierFlags(flag) : NodeList.None);
                    var sigDeclaration = c.Signatures.Record(sig).Declaration;
                    var location = !sigDeclaration.IsNone && !c.Parent(sigDeclaration).IsNone && IsPrototypePropertyAssignment(c.StoreOf(sigDeclaration), c.StoreOf(sigDeclaration).Parent(sigDeclaration.Node)) ? c.Parent(sigDeclaration) : sigDeclaration;
                    results.Add(SetTextRangeBuilder(decl, location));
                }
                return results;
            }
            // The `Constructor`'s symbol isn't in the class's properties lists, obviously, since it's a signature on the static
            throw new InvalidOperationException("Debug Failure. Unhandled class member kind! " + pflags);
        }

        private Node CreateProperty(bool isClass, NodeList modifiers, Node name, Node questionToken, Node type) =>
            isClass ? f.CreatePropertyDeclaration(modifiers, name, questionToken, type, Node.None) : f.CreatePropertySignature(modifiers, name, questionToken, type);

        private SymbolRef FirstParameter(Signature signature)
        {
            var parameters = c.Signatures.ParametersOf(signature);
            return parameters.Length > 0 ? parameters[0] : SymbolRef.None;
        }

        // the builder's setTextRange for members: a fresh node, so the clone TypeScript makes is not needed; the positions
        // within the enclosing file
        private Node SetTextRangeBuilder(Node node, NodeRef location) => SetTextRange(node, location);

        private List<Node> SerializeSignatures(SignatureKind kind, Type input, Type baseType, SyntaxKind outputKind)
        {
            var signatures = c.GetSignaturesOfType(input, kind);
            if (kind == SignatureKind.Construct)
            {
                var allEmpty = true;
                foreach (var s in signatures)
                {
                    if (c.Signatures.Record(s).Parameters.Count != 0)
                    {
                        allEmpty = false;
                    }
                }
                if (baseType.IsNone && allEmpty)
                {
                    return []; // No base type, every constructor is empty - elide the extraneous `constructor()`
                }
                if (!baseType.IsNone)
                {
                    // If there is a base type, if every signature in the class is identical to a signature in the baseType, elide all the declarations
                    var baseSigs = c.GetSignaturesOfType(baseType, SignatureKind.Construct);
                    if (baseSigs.Count == 0 && allEmpty)
                    {
                        return []; // Base had no explicit signatures, if all our signatures are also implicit, return an empty list
                    }
                    if (baseSigs.Count == signatures.Count)
                    {
                        var failed = false;
                        for (var i = 0; i < baseSigs.Count; i++)
                        {
                            if (c.CompareSignaturesIdentical(signatures[i], baseSigs[i], partialMatch: false, ignoreThisTypes: false, ignoreReturnTypes: true, c.CompareTypesIdentical) == 0)
                            {
                                failed = true;
                                break;
                            }
                        }
                        if (!failed)
                        {
                            return []; // Every signature was identical - elide constructor list as it is inherited
                        }
                    }
                }
                var privateProtected = ModifierFlags.None;
                foreach (var s in signatures)
                {
                    var declaration = c.Signatures.Record(s).Declaration;
                    if (!declaration.IsNone)
                    {
                        privateProtected |= Utilities.GetEffectiveModifierFlags(c.StoreOf(declaration), declaration.Node) & (ModifierFlags.Private | ModifierFlags.Protected);
                    }
                }
                if (privateProtected != 0)
                {
                    return [SetTextRangeBuilder(
                        f.CreateConstructorDeclaration(f.CreateModifiersFromModifierFlags(privateProtected), f.CreateNodeArray(ReadOnlySpan<Node>.Empty), Node.None),
                        c.Signatures.Record(signatures[0]).Declaration)];
                }
            }

            var results = new List<Node>();
            foreach (var sig in signatures)
            {
                context.ApproximateLength += 1; // ;
                // Each overload becomes a separate constructor declaration, in order
                var decl = c.SignatureToSignatureDeclarationHelper(sig, outputKind, context);
                results.Add(SetTextRangeBuilder(decl, c.Signatures.Record(sig).Declaration));
            }
            return results;
        }

        private List<Node> SerializeIndexSignatures(Type input, Type baseType)
        {
            var results = new List<Node>();
            foreach (var info in c.GetIndexInfosOfType(input))
            {
                if (!baseType.IsNone)
                {
                    var baseInfo = c.GetIndexInfoOfType(baseType, info.KeyType);
                    if (baseInfo is { } b && c.IsTypeIdenticalTo(info.Type, b.Type))
                    {
                        continue; // elide identical index signatures
                    }
                }
                results.Add(c.IndexInfoToIndexSignatureDeclarationHelper(info, context, Node.None));
            }
            return results;
        }

        private Node SerializeBaseType(Type t, Type staticType, string rootName)
        {
            var reference = TrySerializeAsTypeReference(t, SymbolFlags.Value);
            if (!reference.IsNone)
            {
                return reference;
            }
            var tempName = GetUnusedName(rootName + "_base");
            var statement = f.CreateVariableStatement(
                default,
                f.CreateVariableDeclarationList(NodeArray(f.CreateVariableDeclaration(f.CreateIdentifier(tempName), Node.None, c.TypeToTypeNodeHelper(staticType, context), Node.None)), NodeFlags.Const));
            AddResult(statement, ModifierFlags.None);
            return f.CreateExpressionWithTypeArguments(f.CreateIdentifier(tempName), NodeList.None);
        }

        private Node TrySerializeAsTypeReference(Type t, SymbolFlags flags)
        {
            List<Node>? typeArgs = null;
            var reference = Node.None;

            // We don't use `isValueSymbolAccessible` below. since that considers alternative containers (like modules)
            // which we can't write out in a syntactically valid way as an expression
            var isReference = (c.Types.ObjectFlagsOf(t) & ObjectFlags.Reference) != 0;
            var target = isReference ? c.Types.Object(t).Target : Type.None;
            if (!target.IsNone && IsSymbolAccessibleByFlags(c.Types.Record(target).Symbol, enclosingDeclaration, flags))
            {
                typeArgs = [];
                foreach (var a in c.GetTypeArguments(t))
                {
                    typeArgs.Add(c.TypeToTypeNodeHelper(a, context));
                }
                reference = c.SymbolToExpression(c.Types.Record(target).Symbol, context, SymbolFlags.Type);
            }
            else if (!c.Types.Record(t).Symbol.IsNone && IsSymbolAccessibleByFlags(c.Types.Record(t).Symbol, enclosingDeclaration, flags))
            {
                reference = c.SymbolToExpression(c.Types.Record(t).Symbol, context, SymbolFlags.Type);
            }
            if (!reference.IsNone)
            {
                return f.CreateExpressionWithTypeArguments(reference, typeArgs is null ? NodeList.None : f.CreateNodeArray(typeArgs));
            }
            return Node.None;
        }

        // checker.ts isSymbolAccessibleByFlags (allowModules false)
        private bool IsSymbolAccessibleByFlags(SymbolRef typeSymbol, NodeRef enclosing, SymbolFlags flags) =>
            c.IsSymbolAccessibleWorker(typeSymbol, enclosing, null, flags, allowModules: false) == SymbolAccessibility.Accessible;

        private Node SerializeImplementedType(Type t)
        {
            var reference = TrySerializeAsTypeReference(t, SymbolFlags.Type);
            if (!reference.IsNone)
            {
                return reference;
            }
            var symbol = c.Types.Record(t).Symbol;
            if (!symbol.IsNone)
            {
                return f.CreateExpressionWithTypeArguments(c.SymbolToExpression(symbol, context, SymbolFlags.Type), NodeList.None);
            }
            return Node.None;
        }

        private string GetUnusedName(string input, SymbolRef symbol = default)
        {
            if (!symbol.IsNone && context.RemappedSymbolNames!.TryGetValue(symbol, out var remapped))
            {
                return remapped;
            }
            if (!symbol.IsNone)
            {
                input = GetNameCandidateWorker(symbol, input);
            }
            var i = 0;
            var original = input;
            while (context.UsedSymbolNames?.Contains(input) == true)
            {
                i++;
                input = original + "_" + i;
            }
            context.UsedSymbolNames?.Add(input);
            if (!symbol.IsNone)
            {
                context.RemappedSymbolNames![symbol] = input;
            }
            return input;
        }

        private string GetNameCandidateWorker(SymbolRef symbol, string localName)
        {
            if (localName is InternalSymbolName.Default or InternalSymbolName.Class or InternalSymbolName.Function)
            {
                var saved = SaveRestoreFlags(context);
                context.Flags |= NodeBuilderFlags.InInitialEntityName;
                var nameCandidate = c.GetNameOfSymbolAsWritten(symbol, context);
                saved.Restore(context);
                localName = nameCandidate.Length > 0 && (nameCandidate[0] == '"' || nameCandidate[0] == '\'') ? StripQuotes(nameCandidate) : nameCandidate;
            }
            if (localName == InternalSymbolName.Default)
            {
                localName = "_default";
            }
            else if (localName == InternalSymbolName.ExportEquals)
            {
                localName = "_exports";
            }
            localName = Scanner.IsIdentifierText(localName, c.languageVersion) && !IsStringANonContextualKeyword(localName)
                ? localName
                : "_" + System.Text.RegularExpressions.Regex.Replace(localName, "[^a-zA-Z0-9]", "_");
            return localName;
        }

        private string GetInternalSymbolName(SymbolRef symbol, string localName)
        {
            if (context.RemappedSymbolNames!.TryGetValue(symbol, out var remapped))
            {
                return remapped;
            }
            localName = GetNameCandidateWorker(symbol, localName);
            // The result of this is going to be used as the symbol's name - lock it in, so `getUnusedName` will also pick it up
            context.RemappedSymbolNames[symbol] = localName;
            return localName;
        }
    }
}
