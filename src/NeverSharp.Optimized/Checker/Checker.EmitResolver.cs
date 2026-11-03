// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (createResolver and the emitter support functions it binds; inSameLexicalScope)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/emitresolver.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D1. The resolver of one emitted file comes from the checker that
// checked it (typescript-go's GetEmitResolver); the transformers pass nodes of the emit copy of the file's store, whose
// parse tree nodes have the file's handles, so getParseTreeNode runs on the copy and the checker reads the file's own
// store. Nothing here writes into a file's store. Declaration-emit entries (createTypeOfDeclaration and friends,
// collectLinkedAliases) are module D5's; the ES2015 transformer's entries (getReferencedDeclarationWithCollidingName,
// isDeclarationWithCollidingName) are not reached under the default options.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    /// <summary>checker.ts getEmitResolver(sourceFile): the resolver for the emit of one file (its checks run before, Program.Emit).</summary>
    internal EmitResolver GetEmitResolver(SourceFile sourceFile)
    {
        return new EmitResolver(this, fileIndex[sourceFile]);
    }

    // checker.ts `var canCollectSymbolAliasAccessabilityData = !compilerOptions.verbatimModuleSyntax;`
    internal bool CanCollectAliasData => compilerOptions.VerbatimModuleSyntax != true;

    // checker.ts inSameLexicalScope
    private bool InSameLexicalScope(NodeRef node1, NodeRef node2)
    {
        var container1 = At(node1, Utilities.GetEnclosingBlockScopeContainer(StoreOf(node1), node1.Node));
        var container2 = At(node2, Utilities.GetEnclosingBlockScopeContainer(StoreOf(node2), node2.Node));
        if (IsGlobalSourceFile(container1))
        {
            return IsGlobalSourceFile(container2);
        }
        else if (IsGlobalSourceFile(container2))
        {
            return false;
        }
        else
        {
            return container1 == container2;
        }
    }

    // checker.ts isNameOfModuleOrEnumDeclaration
    private bool IsNameOfModuleOrEnumDeclaration(NodeRef node)
    {
        var parent = Parent(node);
        return Utilities.IsModuleOrEnumDeclaration(StoreOf(parent), parent.Node) && StoreOf(parent).Name(parent.Node) == node.Node;
    }

    /// <summary>
    /// checker.ts isArgumentsLocalBinding (for a parse tree identifier). Note: does not handle
    /// isShorthandPropertyAssignment (and probably a few more).
    /// </summary>
    internal bool IsArgumentsLocalBindingOf(NodeRef node)
    {
        var parent = Parent(node);
        if (parent.IsNone)
        {
            return false;
        }
        var isPropertyName = Kind(parent) is SyntaxKind.PropertyAccessExpression or SyntaxKind.PropertyAssignment
            && StoreOf(parent).Name(parent.Node) == node.Node;
        return !isPropertyName && GetReferencedValueSymbol(node) == argumentsSymbol;
    }

    /// <summary>
    /// checker.ts getReferencedExportContainer: when resolved as an expression identifier, if the given node references an
    /// exported entity, the declaration node of the exported entity's container; otherwise none.
    /// </summary>
    internal NodeRef GetReferencedExportContainerOf(NodeRef node, bool prefixLocals)
    {
        // When resolving the export container for the name of a module or enum
        // declaration, we need to start resolution at the declaration's container.
        // Otherwise, we could incorrectly resolve the export container as the
        // declaration if it contains an exported member with the same name.
        var symbol = GetReferencedValueSymbol(node, startInDeclarationContainer: IsNameOfModuleOrEnumDeclaration(node));
        if (!symbol.IsNone)
        {
            if ((SymbolFlagsOf(symbol) & SymbolFlags.ExportValue) != 0)
            {
                // If we reference an exported entity within the same module declaration, then whether
                // we prefix depends on the kind of entity. SymbolFlags.ExportHasLocal encompasses all the
                // kinds that we do NOT prefix.
                var exportSymbol = GetMergedSymbol(ExportSymbolOf(symbol));
                if (!prefixLocals && (SymbolFlagsOf(exportSymbol) & SymbolFlags.ExportHasLocal) != 0 && (SymbolFlagsOf(exportSymbol) & SymbolFlags.Variable) == 0)
                {
                    return NodeRef.None;
                }
                symbol = exportSymbol;
            }
            var parentSymbol = GetParentOfSymbol(symbol);
            if (!parentSymbol.IsNone)
            {
                var parentDeclaration = ValueDeclarationOf(parentSymbol);
                if ((SymbolFlagsOf(parentSymbol) & SymbolFlags.ValueModule) != 0 && !parentDeclaration.IsNone && Kind(parentDeclaration) == SyntaxKind.SourceFile)
                {
                    // If `node` accesses an export and that export isn't in the same file, then symbol is a namespace export, so return undefined.
                    var symbolIsUmdExport = parentDeclaration.File != node.File;
                    return symbolIsUmdExport ? NodeRef.None : parentDeclaration;
                }
                for (var n = Parent(node); !n.IsNone; n = Parent(n))
                {
                    if (Utilities.IsModuleOrEnumDeclaration(StoreOf(n), n.Node) && GetSymbolOfDeclaration(n) == parentSymbol)
                    {
                        return n;
                    }
                }
            }
        }
        return NodeRef.None;
    }

    /// <summary>
    /// checker.ts getReferencedImportDeclaration for a parse tree identifier: when resolved as an expression identifier,
    /// if the given node references an import, the declaration of that import.
    /// </summary>
    internal NodeRef GetReferencedImportDeclarationOf(NodeRef node)
    {
        var symbol = GetReferencedValueOrAliasSymbol(node);

        // We should only get the declaration of an alias if there isn't a local value
        // declaration for the symbol
        if (!symbol.IsNone && IsNonLocalAlias(SymbolFlagsOf(symbol), SymbolFlags.Value) && GetTypeOnlyAliasDeclaration(symbol, SymbolFlags.Value).IsNone)
        {
            return GetDeclarationOfAliasSymbol(symbol);
        }
        return NodeRef.None;
    }

    /// <summary>checker.ts isValueAliasDeclaration.</summary>
    internal bool IsValueAliasDeclarationOf(NodeRef node)
    {
        Debug.Assert(CanCollectAliasData);
        var store = StoreOf(node);
        switch (store.Kind(node.Node))
        {
            case SyntaxKind.ImportEqualsDeclaration:
                return IsAliasResolvedToValue(GetSymbolOfDeclaration(node));
            case SyntaxKind.ImportClause:
            case SyntaxKind.NamespaceImport:
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.ExportSpecifier:
            {
                var symbol = GetSymbolOfDeclaration(node);
                return !symbol.IsNone && IsAliasResolvedToValue(symbol, excludeTypeOnlyValues: true);
            }
            case SyntaxKind.ExportDeclaration:
            {
                var exportClause = store.ExportClause(node.Node);
                if (exportClause.IsNone)
                {
                    return false;
                }
                if (store.Kind(exportClause) == SyntaxKind.NamespaceExport)
                {
                    return true;
                }
                foreach (var element in store.Nodes(store.Elements(exportClause)))
                {
                    if (IsValueAliasDeclarationOf(At(node, element)))
                    {
                        return true;
                    }
                }
                return false;
            }
            case SyntaxKind.ExportAssignment:
            {
                var expression = store.Expression(node.Node);
                return !expression.IsNone && store.Kind(expression) == SyntaxKind.Identifier ?
                    IsAliasResolvedToValue(GetSymbolOfDeclaration(node), excludeTypeOnlyValues: true) :
                    true;
            }
        }
        return false;
    }

    /// <summary>checker.ts isTopLevelValueImportEqualsWithEntityName for a parse tree import-equals declaration.</summary>
    internal bool IsTopLevelValueImportEqualsWithEntityNameOf(NodeRef node)
    {
        var store = StoreOf(node);
        if (store.Kind(store.Parent(node.Node)) != SyntaxKind.SourceFile || !Utilities.IsInternalModuleImportEqualsDeclaration(store, node.Node))
        {
            // parent is not source file or it is not reference to internal module
            return false;
        }

        var isValue = IsAliasResolvedToValue(GetSymbolOfDeclaration(node));
        return isValue && !store.ModuleReference(node.Node).IsNone && !Utilities.NodeIsMissing(store, store.ModuleReference(node.Node));
    }

    // checker.ts isAliasResolvedToValue
    private bool IsAliasResolvedToValue(SymbolRef symbol, bool excludeTypeOnlyValues = false)
    {
        if (symbol.IsNone)
        {
            return false;
        }
        var valueDeclaration = ValueDeclarationOf(symbol);
        if (!valueDeclaration.IsNone)
        {
            var container = new NodeRef(valueDeclaration.File, Utilities.GetSourceFileOfNode(StoreOf(valueDeclaration), valueDeclaration.Node));
            var fileSymbol = container.IsNone ? SymbolRef.None : GetSymbolOfDeclaration(container);
            // Ensures cjs export assignment is setup, since this symbol may point at, and merge with, the file itself.
            // If we don't, the merge may not have yet occured, and the flags check below will be missing flags that
            // are added as a result of the merge.
            ResolveExternalModuleSymbol(fileSymbol);
        }
        var target = GetExportSymbolOfValueSymbolIfExported(ResolveAlias(symbol));
        if (target == unknownSymbol)
        {
            return !excludeTypeOnlyValues || GetTypeOnlyAliasDeclaration(symbol).IsNone;
        }
        // const enums and modules that contain only const enums are not considered values from the emit perspective
        // unless 'preserveConstEnums' option is set to true
        return (GetSymbolFlags(symbol, excludeTypeOnlyValues, excludeLocalMeanings: true) & SymbolFlags.Value) != 0 &&
            (compilerOptions.ShouldPreserveConstEnums() || !IsConstEnumOrConstEnumOnlyModule(target));
    }

    /// <summary>checker.ts isReferencedAliasDeclaration.</summary>
    internal bool IsReferencedAliasDeclarationOf(NodeRef node, bool checkChildren)
    {
        Debug.Assert(CanCollectAliasData);
        if (IsAliasSymbolDeclaration(node))
        {
            var symbol = GetSymbolOfDeclaration(node);
            if (!symbol.IsNone && AliasSymbolLinks.Get(symbol).Referenced)
            {
                return true;
            }
            var target = symbol.IsNone ? SymbolRef.None : AliasSymbolLinks.Get(symbol).AliasTarget;
            if (!target.IsNone && (Utilities.GetEffectiveModifierFlags(StoreOf(node), node.Node) & ModifierFlags.Export) != 0 &&
                (GetSymbolFlags(target) & SymbolFlags.Value) != 0 &&
                (compilerOptions.ShouldPreserveConstEnums() || !IsConstEnumOrConstEnumOnlyModule(target)))
            {
                // An `export import ... =` of a value symbol is always considered referenced
                return true;
            }
        }

        if (checkChildren)
        {
            return NodeVisitor.ForEachChild<object>(StoreOf(node), node.Node, child => IsReferencedAliasDeclarationOf(At(node, child), checkChildren) ? (object)true : null) is not null;
        }
        return false;
    }

    /// <summary>checker.ts isImplementationOfOverload.</summary>
    internal bool IsImplementationOfOverloadOf(NodeRef node)
    {
        var store = StoreOf(node);
        if (Utilities.NodeIsPresent(store, store.Body(node.Node)))
        {
            if (store.Kind(node.Node) is SyntaxKind.GetAccessor or SyntaxKind.SetAccessor)
            {
                return false; // Get or set accessors can never be overload implementations, but can have up to 2 signatures
            }
            var symbol = GetSymbolOfDeclaration(node);
            var signaturesOfSymbol = GetSignaturesOfSymbol(symbol);
            // If this function body corresponds to function with multiple signature, it is implementation of overload
            return signaturesOfSymbol.Count > 1 ||
                // If there is single signature for the symbol, it is overload if that signature isn't coming from the node
                (signaturesOfSymbol.Count == 1 && Signatures.Record(signaturesOfSymbol[0]).Declaration != node);
        }
        return false;
    }

    /// <summary>checker.ts getConstantValue (for a parse tree enum member or access expression).</summary>
    internal object? GetConstantValueOf(NodeRef node)
    {
        if (Kind(node) == SyntaxKind.EnumMember)
        {
            return GetEnumMemberValue(node);
        }

        if (!SymbolNodeLinks.Has(node) || SymbolNodeLinks.Get(node).ResolvedSymbol.IsNone)
        {
            CheckExpressionCached(node); // ensure cached resolved symbol is set
        }
        var symbol = SymbolNodeLinks.Has(node) ? SymbolNodeLinks.Get(node).ResolvedSymbol : SymbolRef.None;
        if (symbol.IsNone && Utilities.IsEntityNameExpression(StoreOf(node), node.Node))
        {
            symbol = ResolveEntityName(node, SymbolFlags.Value, ignoreErrors: true);
        }
        if (!symbol.IsNone && (SymbolFlagsOf(symbol) & SymbolFlags.EnumMember) != 0)
        {
            // inline property\index accesses only for const enums
            var member = ValueDeclarationOf(symbol);
            if (Utilities.IsEnumConst(StoreOf(member), StoreOf(member).Parent(member.Node)))
            {
                return GetEnumMemberValue(member);
            }
        }

        return null;
    }

    /// <summary>checker.ts hasGlobalName.</summary>
    internal bool HasGlobalName(string name)
    {
        return globals.Has(Utilities.EscapeLeadingUnderscores(name));
    }

    // checker.ts getReferencedValueSymbol
    private SymbolRef GetReferencedValueSymbol(NodeRef reference, bool startInDeclarationContainer = false)
    {
        var resolvedSymbol = SymbolNodeLinks.Has(reference) ? SymbolNodeLinks.Get(reference).ResolvedSymbol : SymbolRef.None;
        if (!resolvedSymbol.IsNone)
        {
            return resolvedSymbol;
        }

        var location = reference;
        if (startInDeclarationContainer)
        {
            // When resolving the name of a declaration as a value, we need to start resolution
            // at a point outside of the declaration.
            var parent = Parent(reference);
            if (Utilities.IsDeclaration(StoreOf(parent), parent.Node) && StoreOf(parent).Name(parent.Node) == reference.Node)
            {
                location = GetDeclarationContainer(parent);
            }
        }

        var store = StoreOf(reference);
        return ResolveName(location, store.Texts.GetString(store.EscapedText(reference.Node))!, reference, SymbolFlags.Value | SymbolFlags.ExportValue | SymbolFlags.Alias, nameNotFoundMessage: null, isUse: true);
    }

    // checker.ts getReferencedValueOrAliasSymbol: unlike getReferencedValueSymbol, if the cached resolved symbol is
    // the unknown symbol, resolveName is called to find a symbol.
    private SymbolRef GetReferencedValueOrAliasSymbol(NodeRef reference)
    {
        var resolvedSymbol = SymbolNodeLinks.Has(reference) ? SymbolNodeLinks.Get(reference).ResolvedSymbol : SymbolRef.None;
        if (!resolvedSymbol.IsNone && resolvedSymbol != unknownSymbol)
        {
            return resolvedSymbol;
        }

        var store = StoreOf(reference);
        return ResolveName(reference, store.Texts.GetString(store.EscapedText(reference.Node))!, reference, SymbolFlags.Value | SymbolFlags.ExportValue | SymbolFlags.Alias, nameNotFoundMessage: null, isUse: true);
    }

    /// <summary>checker.ts getReferencedValueDeclaration for a parse tree identifier.</summary>
    internal NodeRef GetReferencedValueDeclarationOf(NodeRef reference)
    {
        var symbol = GetReferencedValueSymbol(reference);
        return symbol.IsNone ? NodeRef.None : ValueDeclarationOf(GetExportSymbolOfValueSymbolIfExported(symbol));
    }

    /// <summary>checker.ts isLiteralConstDeclaration.</summary>
    internal bool IsLiteralConstDeclarationOf(NodeRef node)
    {
        if (IsDeclarationReadonly(node) || (Kind(node) == SyntaxKind.VariableDeclaration && IsVarConstLike(node)))
        {
            return IsFreshLiteralType(GetTypeOfSymbol(GetSymbolOfDeclaration(node)));
        }
        return false;
    }

    /// <summary>checker.ts hasNodeCheckFlag (for a parse tree node).</summary>
    internal bool HasNodeCheckFlagOf(NodeRef node, NodeCheckFlags flag)
    {
        // (TypeScript: `checkNodeDeferred` happens through getNodeCheckFlags' callers having checked the file; the
        // emit checks the file first, Program.Emit.)
        return NodeLinks.Has(node) && (NodeLinks.Get(node).Flags & flag) != 0;
    }

    /// <summary>checker.ts isDeclarationVisible (for the resolver).</summary>
    internal bool IsDeclarationVisibleOf(NodeRef node) => IsDeclarationVisible(node);

    /// <summary>
    /// getEnumMemberValue(member).isSyntacticallyString (module D2, emit only). Phase 3's evaluator
    /// (<see cref="EvaluateConstantExpression"/>) keeps only the value of TypeScript's EvaluatorResult; this walks the
    /// initializer as createEvaluator's evaluate does and computes the flag alone, without reporting errors. Only the
    /// TypeScript transformer reads it, for members whose value is undefined.
    /// </summary>
    internal bool IsEnumMemberValueSyntacticallyStringOf(NodeRef member)
    {
        var store = StoreOf(member);
        var initializer = store.Initializer(member.Node);
        // (computeEnumMemberValue without an initializer: evaluatorResult(undefined) or the auto value, never a
        // syntactic string.)
        return !initializer.IsNone && IsSyntacticallyStringOf(At(member, initializer), member, 0);
    }

    // createEvaluator's evaluate(expr, location).isSyntacticallyString; depth guards against a cycle TypeScript's
    // memoized getEnumMemberValue would cut.
    private bool IsSyntacticallyStringOf(NodeRef expr, NodeRef location, int depth)
    {
        if (depth > 100)
        {
            return false;
        }
        var store = StoreOf(expr);
        var node = Utilities.SkipParentheses(store, expr.Node, false);
        switch (store.Kind(node))
        {
            case SyntaxKind.BinaryExpression:
                return (IsSyntacticallyStringOf(At(expr, store.Left(node)), location, depth + 1) | IsSyntacticallyStringOf(At(expr, store.Right(node)), location, depth + 1)) &&
                    store.Kind(store.OperatorToken(node)) == SyntaxKind.PlusToken;
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateExpression:
                return true;
            case SyntaxKind.Identifier:
                return IsEntityNameSyntacticallyString(At(expr, node), location, depth);
            case SyntaxKind.PropertyAccessExpression:
                return Utilities.IsEntityNameExpression(store, node) && IsEntityNameSyntacticallyString(At(expr, node), location, depth);
            case SyntaxKind.ElementAccessExpression:
            {
                // evaluateElementAccessExpression
                var root = store.Expression(node);
                var argument = store.ArgumentExpression(node);
                if (Utilities.IsEntityNameExpression(store, root) && Utilities.IsStringLiteralLike(store, argument))
                {
                    var rootSymbol = ResolveEntityName(At(expr, root), SymbolFlags.Value, ignoreErrors: true);
                    if (!rootSymbol.IsNone && (SymbolFlagsOf(rootSymbol) & SymbolFlags.Enum) != 0)
                    {
                        var name = Utilities.EscapeLeadingUnderscores(store.Texts.GetString(store.Text(argument))!);
                        var member = ExportsOf(rootSymbol)?.Get(name) ?? SymbolRef.None;
                        if (!member.IsNone)
                        {
                            return IsEnumMemberReferenceSyntacticallyString(member, location, depth);
                        }
                    }
                }
                return false;
            }
        }
        // (PrefixUnaryExpression, NumericLiteral and everything else: false.)
        return false;
    }

    // evaluateEntityNameExpression(expr, location).isSyntacticallyString
    private bool IsEntityNameSyntacticallyString(NodeRef expr, NodeRef location, int depth)
    {
        var symbol = ResolveEntityName(expr, SymbolFlags.Value, ignoreErrors: true);
        if (symbol.IsNone)
        {
            return false;
        }
        var store = StoreOf(expr);
        if (store.Kind(expr.Node) == SyntaxKind.Identifier)
        {
            var name = store.Texts.GetString(store.EscapedText(expr.Node));
            if (name is "Infinity" or "NaN" && symbol == GetGlobalSymbol(name, SymbolFlags.Value, null))
            {
                return false;
            }
        }
        if ((SymbolFlagsOf(symbol) & SymbolFlags.EnumMember) != 0)
        {
            return IsEnumMemberReferenceSyntacticallyString(symbol, location, depth);
        }
        if ((SymbolFlagsOf(symbol) & SymbolFlags.Variable) != 0 && (GetDeclarationNodeFlagsFromSymbol(symbol) & NodeFlags.Constant) != 0)
        {
            var declaration = ValueDeclarationOf(symbol);
            if (!declaration.IsNone && Kind(declaration) == SyntaxKind.VariableDeclaration)
            {
                var dstore = StoreOf(declaration);
                var initializer = dstore.Initializer(declaration.Node);
                if (dstore.Type(declaration.Node).IsNone && !initializer.IsNone &&
                    (location.IsNone || (declaration != location && IsBlockScopedNameDeclaredBeforeUse(declaration, location))))
                {
                    if (!location.IsNone && location.File != declaration.File)
                    {
                        return false;
                    }
                    return IsSyntacticallyStringOf(At(declaration, initializer), declaration, depth + 1);
                }
            }
        }
        return false;
    }

    // evaluateEnumMember(expr, symbol, location).isSyntacticallyString (location is always an enum member or a
    // variable declaration here)
    private bool IsEnumMemberReferenceSyntacticallyString(SymbolRef symbol, NodeRef location, int depth)
    {
        var declaration = ValueDeclarationOf(symbol);
        if (declaration.IsNone || declaration == location)
        {
            return false;
        }
        if (!location.IsNone && !IsBlockScopedNameDeclaredBeforeUse(declaration, location))
        {
            return false;
        }
        // getEnumMemberValue(declaration).isSyntacticallyString
        return Kind(declaration) == SyntaxKind.EnumMember && IsEnumMemberValueSyntacticallyStringAt(declaration, depth + 1);
    }

    private bool IsEnumMemberValueSyntacticallyStringAt(NodeRef member, int depth)
    {
        var store = StoreOf(member);
        var initializer = store.Initializer(member.Node);
        return !initializer.IsNone && IsSyntacticallyStringOf(At(member, initializer), member, depth);
    }
}

/// <summary>
/// types.ts EmitResolver: checker.ts createResolver() for the emit of one file. Every entry takes a node of the
/// transformers' store (the emit copy of the file's store) and maps it to its parse tree node first, as TypeScript's
/// entries do with getParseTreeNode.
/// </summary>
internal sealed class EmitResolver
{
    private readonly Checker checker;
    private readonly int file;

    internal EmitResolver(Checker checker, int file)
    {
        this.checker = checker;
        this.file = file;
    }

    /// <summary>The store of the nodes the transformers pass (set by the emitter for each transformation).</summary>
    internal NodeStore EmitStore { get; set; } = null!;

    // getParseTreeNode(nodeIn) as a node of the file (none for a synthesized node without a parse tree node).
    private NodeRef ParseTreeNode(Node nodeIn)
    {
        var node = UtilitiesPublic.GetParseTreeNode(EmitStore, nodeIn);
        return node.IsNone ? NodeRef.None : new NodeRef(file, node);
    }

    private NodeRef ParseTreeIdentifier(Node nodeIn)
    {
        var node = ParseTreeNode(nodeIn);
        return !node.IsNone && checker.Kind(node) == SyntaxKind.Identifier ? node : NodeRef.None;
    }

    // A declaration the checker returns, as a node of the transformers' store (none if it is in another file).
    private Node InThisFile(NodeRef node) => !node.IsNone && node.File == file ? node.Node : Node.None;

    internal bool HasGlobalName(string name) => checker.HasGlobalName(name);

    internal Node GetReferencedExportContainer(Node nodeIn, bool prefixLocals = false)
    {
        var node = ParseTreeIdentifier(nodeIn);
        return node.IsNone ? Node.None : InThisFile(checker.GetReferencedExportContainerOf(node, prefixLocals));
    }

    internal NodeRef GetReferencedImportDeclaration(Node nodeIn)
    {
        var specifier = FactoryEmitNode.GetIdentifierGeneratedImportReference(EmitStore, nodeIn);
        if (!specifier.IsNone)
        {
            return new NodeRef(file, specifier);
        }
        var node = ParseTreeIdentifier(nodeIn);
        return node.IsNone ? NodeRef.None : checker.GetReferencedImportDeclarationOf(node);
    }

    internal bool IsValueAliasDeclaration(Node nodeIn)
    {
        var node = ParseTreeNode(nodeIn);
        // Synthesized nodes are always treated like values.
        return !node.IsNone && checker.CanCollectAliasData ? checker.IsValueAliasDeclarationOf(node) : true;
    }

    internal bool IsReferencedAliasDeclaration(Node nodeIn, bool checkChildren = false)
    {
        var node = ParseTreeNode(nodeIn);
        // Synthesized nodes are always treated as referenced.
        return !node.IsNone && checker.CanCollectAliasData ? checker.IsReferencedAliasDeclarationOf(node, checkChildren) : true;
    }

    internal bool HasNodeCheckFlag(Node nodeIn, NodeCheckFlags flag)
    {
        var node = ParseTreeNode(nodeIn);
        return !node.IsNone && checker.HasNodeCheckFlagOf(node, flag);
    }

    internal bool IsTopLevelValueImportEqualsWithEntityName(Node nodeIn)
    {
        var node = ParseTreeNode(nodeIn);
        return !node.IsNone && checker.Kind(node) == SyntaxKind.ImportEqualsDeclaration && checker.IsTopLevelValueImportEqualsWithEntityNameOf(node);
    }

    internal bool IsDeclarationVisible(Node nodeIn)
    {
        // (TypeScript's isDeclarationVisible takes the node as it is: the declaration transformer passes parse tree nodes.)
        var node = ParseTreeNode(nodeIn);
        return !node.IsNone && checker.IsDeclarationVisibleOf(node);
    }

    internal bool IsImplementationOfOverload(Node nodeIn)
    {
        var node = ParseTreeNode(nodeIn);
        return !node.IsNone && checker.IsImplementationOfOverloadOf(node);
    }

    internal bool IsLiteralConstDeclaration(Node nodeIn)
    {
        var node = ParseTreeNode(nodeIn);
        return !node.IsNone && checker.IsLiteralConstDeclarationOf(node);
    }

    /// <summary>getConstantValue: string, double or null.</summary>
    internal object? GetConstantValue(Node nodeIn)
    {
        var node = ParseTreeNode(nodeIn);
        // canHaveConstantValue
        return !node.IsNone && checker.Kind(node) is SyntaxKind.EnumMember or SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression
            ? checker.GetConstantValueOf(node)
            : null;
    }

    /// <summary>getEnumMemberValue(node).value: string, double or null.</summary>
    internal object? GetEnumMemberValue(Node nodeIn)
    {
        var node = ParseTreeNode(nodeIn);
        return !node.IsNone && checker.Kind(node) == SyntaxKind.EnumMember ? checker.GetConstantValueOf(node) : null;
    }

    /// <summary>markLinkedReferences (module D2): markLinkedReferences(getParseTreeNode(node), ReferenceHint.Unspecified).</summary>
    internal void MarkLinkedReferences(Node nodeIn)
    {
        var node = ParseTreeNode(nodeIn);
        if (!node.IsNone)
        {
            checker.MarkLinkedReferencesUnspecified(node);
        }
    }

    /// <summary>getEnumMemberValue(node).isSyntacticallyString (module D2).</summary>
    internal bool IsEnumMemberValueSyntacticallyString(Node nodeIn)
    {
        var node = ParseTreeNode(nodeIn);
        return !node.IsNone && checker.Kind(node) == SyntaxKind.EnumMember && checker.IsEnumMemberValueSyntacticallyStringOf(node);
    }

    internal NodeRef GetReferencedValueDeclaration(Node referenceIn)
    {
        if (UtilitiesPublic.IsGeneratedIdentifier(EmitStore, referenceIn))
        {
            return NodeRef.None;
        }
        var reference = ParseTreeIdentifier(referenceIn);
        return reference.IsNone ? NodeRef.None : checker.GetReferencedValueDeclarationOf(reference);
    }

    internal bool IsArgumentsLocalBinding(Node nodeIn)
    {
        if (UtilitiesPublic.IsGeneratedIdentifier(EmitStore, nodeIn))
        {
            return false;
        }
        var node = ParseTreeIdentifier(nodeIn);
        return !node.IsNone && checker.IsArgumentsLocalBindingOf(node);
    }

    // Declaration emit (module D5): createTypeOfDeclaration, createReturnTypeOfSignatureDeclaration,
    // createTypeOfExpression, createLiteralConstValue, collectLinkedAliases, isEntityNameVisible,
    // requiresAddingImplicitUndefined, getJsxFactoryEntity are added with the declaration transformer.
}
