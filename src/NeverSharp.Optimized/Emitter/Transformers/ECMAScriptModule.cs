// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/transformers/module/esnextAnd2015.ts (transformECMAScriptModule) and
// src/compiler/transformers/module/impliedNodeFormatDependent.ts (transformImpliedNodeFormatDependentModule)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/transformers/moduletransforms/esmodule.go and impliedmodule.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D3. TypeScript 6.0.3's default options only (module kind ES2022,
// no importHelpers, no rewriteRelativeImportExtensions): the paths of `--module preserve`, of node16 and later module
// kinds (the createRequire shim of import-equals declarations) and of rewritten module specifiers are not reached and
// throw. The CommonJS transformer (transformers/module/module.ts) is not ported: files whose emit module format is
// CommonJS (.cts/.cjs) are routed as tsc routes them and returned untransformed.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>esnextAnd2015.ts transformECMAScriptModule: the closure of the transformer is the instance.</summary>
internal sealed class ECMAScriptModuleTransformer
{
    private readonly TransformationContext context;
    private readonly NodeFactory factory;
    private readonly CompilerOptions compilerOptions;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> previousOnEmitNode;
    private readonly Func<EmitHint, Node, Node> previousOnSubstituteNode;
    private readonly Func<Node, VisitResult> visitor;

    // (noSubstitution, helperNameSubstitutions: filled only with importHelpers, which is not a Phase 4 option, so
    // substituteHelperName returns its node.)
    private Node currentSourceFile;

    private ECMAScriptModuleTransformer(TransformationContext context)
    {
        this.context = context;
        factory = context.Factory;
        compilerOptions = context.GetCompilerOptions();
        if (compilerOptions.RewriteRelativeImportExtensions == true)
        {
            throw new NotSupportedException("transformECMAScriptModule: rewriteRelativeImportExtensions is not a Phase 4 option.");
        }
        if (compilerOptions.GetEmitModuleKind() == ModuleKind.Preserve)
        {
            throw new NotSupportedException("transformECMAScriptModule: --module preserve is not a Phase 4 option.");
        }

        previousOnEmitNode = context.OnEmitNode;
        previousOnSubstituteNode = context.OnSubstituteNode;
        context.OnEmitNode = OnEmitNode;
        context.OnSubstituteNode = OnSubstituteNode;
        context.EnableEmitNotification(SyntaxKind.SourceFile);
        context.EnableSubstitution(SyntaxKind.Identifier);

        currentSourceFile = Node.None;
        visitor = Visitor;
    }

    private NodeStore store => context.Store;

    /// <summary>transformECMAScriptModule(context): sets its emit hooks on the context (chainBundle: source files only).</summary>
    internal static ECMAScriptModuleTransformer Create(TransformationContext context) => new(context);

    internal Node TransformSourceFile(Node node)
    {
        if (store.IsDeclarationFile(node))
        {
            return node;
        }

        if (Utilities.IsExternalModule(store, node) || compilerOptions.GetIsolatedModules())
        {
            currentSourceFile = node;
            // (importRequireStatements: only node16 and later module kinds create them; importsAndRequiresToRewriteOrShim:
            // only with rewriteRelativeImportExtensions.)
            var result = UpdateExternalModule(node);
            FactoryEmitNode.AddEmitHelpers(store, result, context.ReadEmitHelpers());
            currentSourceFile = Node.None;
            if (!Utilities.IsExternalModule(store, node) || Core.Some(store.Nodes(store.Statements(result)), IsExternalModuleIndicator))
            {
                return result;
            }
            var statements = new List<Node>(store.Nodes(store.Statements(result)).ToArray()) { CreateEmptyExports() };
            return factory.UpdateSourceFile(
                result,
                FactoryUtilitiesPublic.SetTextRange(store, factory.CreateNodeArray(statements), store.Statements(result)));
        }

        return node;
    }

    // utilities.ts createEmptyExports
    private Node CreateEmptyExports()
    {
        return factory.CreateExportDeclaration(default, isTypeOnly: false, factory.CreateNamedExports(factory.CreateNodeArray(ReadOnlySpan<Node>.Empty)), default);
    }

    // utilities.ts isExternalModuleIndicator: isAnyImportOrReExport(result) || isExportAssignment(result) ||
    // hasSyntacticModifier(result, ModifierFlags.Export)
    private bool IsExternalModuleIndicator(Node result)
    {
        return store.Kind(result) is SyntaxKind.ImportDeclaration or SyntaxKind.ImportEqualsDeclaration or SyntaxKind.ExportDeclaration or SyntaxKind.ExportAssignment
            || Utilities.HasSyntacticModifier(store, result, ModifierFlags.Export);
    }

    private Node UpdateExternalModule(Node node)
    {
        // (createExternalHelpersImportDeclarationIfNeeded: undefined without importHelpers.)
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult Visitor(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ImportEqualsDeclaration:
                // Though an error in es2020 modules, in node-flavor es2020 modules, we can helpfully transform this to a synthetic `require` call
                // To give easy access to a synchronous `require` in node-flavor esm. We do the transform even in scenarios where we error, but `import.meta.url`
                // is available, just because the output is reasonable for a node-like runtime.
                if (compilerOptions.GetEmitModuleKind() >= ModuleKind.Node16)
                {
                    throw new NotSupportedException("transformECMAScriptModule: the createRequire shim of node16 and later module kinds is not a Phase 4 option.");
                }
                return VisitResult.None;
            case SyntaxKind.ExportAssignment:
                return VisitExportAssignment(node);
            case SyntaxKind.ExportDeclaration:
                return VisitExportDeclaration(node);
            case SyntaxKind.ImportDeclaration:
                // visitImportDeclaration: the node itself without rewriteRelativeImportExtensions.
                return node;
        }

        // (CallExpression and default: importsAndRequiresToRewriteOrShim is empty.)
        return node;
    }

    private VisitResult VisitExportAssignment(Node node)
    {
        if (store.IsExportEquals(node))
        {
            // (--module preserve: not a Phase 4 option.)
            // Elide `export=` as it is not legal with --module ES6
            return VisitResult.None;
        }
        return node;
    }

    private VisitResult VisitExportDeclaration(Node node)
    {
        // (rewriteModuleSpecifier returns the module specifier itself without rewriteRelativeImportExtensions.)
        var moduleSpecifier = store.ModuleSpecifier(node);
        var exportClause = store.ExportClause(node);
        if ((compilerOptions.Module is { } module && module > ModuleKind.ES2015)
            || exportClause.IsNone || store.Kind(exportClause) != SyntaxKind.NamespaceExport || moduleSpecifier.IsNone)
        {
            // Either ill-formed or don't need to be tranformed.
            return node;
        }

        var oldIdentifier = store.Name(exportClause);
        var synthName = factory.GetGeneratedNameForNode(oldIdentifier);
        var importDecl = factory.CreateImportDeclaration(
            default,
            factory.CreateImportClause(
                phaseModifier: null,
                default,
                factory.CreateNamespaceImport(
                    synthName)),
            moduleSpecifier,
            store.Attributes(node));
        NodeFactory.SetOriginalNode(store, importDecl, exportClause);

        var exportDecl = IsExportNamespaceAsDefaultDeclaration(node) ? factory.CreateExportDefault(synthName) : factory.CreateExportDeclaration(
            default,
            isTypeOnly: false,
            factory.CreateNamedExports(factory.CreateNodeArray(new[] { factory.CreateExportSpecifier(isTypeOnly: false, synthName, oldIdentifier) })));
        NodeFactory.SetOriginalNode(store, exportDecl, node);

        return VisitResult.Many(new[] { importDecl, exportDecl });
    }

    // utilities.ts isExportNamespaceAsDefaultDeclaration (with moduleExportNameIsDefault)
    private bool IsExportNamespaceAsDefaultDeclaration(Node node)
    {
        var exportClause = store.ExportClause(node);
        if (exportClause.IsNone || store.Kind(exportClause) != SyntaxKind.NamespaceExport)
        {
            return false;
        }
        var name = store.Name(exportClause);
        var text = store.Kind(name) == SyntaxKind.StringLiteral ? store.Text(name) : store.EscapedText(name);
        return store.Texts.Equals(text, "default");
    }

    //
    // Emit Notification
    //

    /// <summary>Hook for node emit.</summary>
    private void OnEmitNode(EmitHint hint, Node node, Action<EmitHint, Node> emitCallback)
    {
        if (store.Kind(node) == SyntaxKind.SourceFile)
        {
            // (helperNameSubstitutions: only with importHelpers.)
            currentSourceFile = node;
            previousOnEmitNode(hint, node, emitCallback);
            currentSourceFile = Node.None;
        }
        else
        {
            previousOnEmitNode(hint, node, emitCallback);
        }
    }

    //
    // Substitutions
    //

    /// <summary>Hooks node substitutions.</summary>
    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        node = previousOnSubstituteNode(hint, node);
        // (noSubstitution is empty, and substituteHelperName returns the node: no external helpers module without
        // importHelpers, no helperNameSubstitutions.)
        return node;
    }
}

/// <summary>
/// impliedNodeFormatDependent.ts transformImpliedNodeFormatDependentModule: selects the ES module transformer or the
/// CommonJS transformer by the emit module format of each file. Track O (module D3): the CommonJS transformer
/// (module.ts) is not ported; a CommonJS file keeps the hooks of the transformers before it and is returned as it is.
/// </summary>
internal sealed class ImpliedNodeFormatDependentModuleTransformer
{
    private readonly TransformationContext context;
    private readonly Func<EmitHint, Node, Node> previousOnSubstituteNode;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> previousOnEmitNode;
    private readonly ECMAScriptModuleTransformer esmTransform;
    private readonly Func<EmitHint, Node, Node> esmOnSubstituteNode;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> esmOnEmitNode;
    private readonly Func<EmitHint, Node, Node> cjsOnSubstituteNode;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> cjsOnEmitNode;
    private Node currentSourceFile;

    private ImpliedNodeFormatDependentModuleTransformer(TransformationContext context)
    {
        this.context = context;
        previousOnSubstituteNode = context.OnSubstituteNode;
        previousOnEmitNode = context.OnEmitNode;

        esmTransform = ECMAScriptModuleTransformer.Create(context);

        esmOnSubstituteNode = context.OnSubstituteNode;
        esmOnEmitNode = context.OnEmitNode;

        context.OnSubstituteNode = previousOnSubstituteNode;
        context.OnEmitNode = previousOnEmitNode;

        // transformModule(context): not ported (module.ts is outside D3); its hooks are the previous ones.
        cjsOnSubstituteNode = context.OnSubstituteNode;
        cjsOnEmitNode = context.OnEmitNode;

        context.OnSubstituteNode = OnSubstituteNode;
        context.OnEmitNode = OnEmitNode;
        context.EnableSubstitution(SyntaxKind.SourceFile);
        context.EnableEmitNotification(SyntaxKind.SourceFile);

        currentSourceFile = Node.None;
    }

    private NodeStore store => context.Store;

    internal static Func<Node, Node> TransformImpliedNodeFormatDependentModule(TransformationContext context) => new ImpliedNodeFormatDependentModuleTransformer(context).TransformSourceFile;

    private ModuleKind GetEmitModuleFormatOfFile(Node file) => context.GetEmitHost().GetEmitModuleFormatOfFile(store, file);

    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        if (store.Kind(node) == SyntaxKind.SourceFile)
        {
            currentSourceFile = node;
            // Neither component transform wants substitution notifications for `SourceFile`s, and, in fact, relies on
            // the source file emit notification to setup scope variables for substitutions (so we _cannot_ call their substitute
            // functions on source files safely, as that context only gets setup in a later pipeline phase!)
            return previousOnSubstituteNode(hint, node);
        }
        else
        {
            if (currentSourceFile.IsNone)
            {
                return previousOnSubstituteNode(hint, node);
            }
            if (GetEmitModuleFormatOfFile(currentSourceFile) >= ModuleKind.ES2015)
            {
                return esmOnSubstituteNode(hint, node);
            }
            return cjsOnSubstituteNode(hint, node);
        }
    }

    private void OnEmitNode(EmitHint hint, Node node, Action<EmitHint, Node> emitCallback)
    {
        if (store.Kind(node) == SyntaxKind.SourceFile)
        {
            currentSourceFile = node;
        }
        if (currentSourceFile.IsNone)
        {
            previousOnEmitNode(hint, node, emitCallback);
            return;
        }
        if (GetEmitModuleFormatOfFile(currentSourceFile) >= ModuleKind.ES2015)
        {
            esmOnEmitNode(hint, node, emitCallback);
            return;
        }
        cjsOnEmitNode(hint, node, emitCallback);
    }

    private Node TransformSourceFile(Node node)
    {
        if (store.IsDeclarationFile(node))
        {
            return node;
        }

        currentSourceFile = node;
        // getModuleTransformForFile(node)(node): the CommonJS transformer (module.ts) is not ported, so a CommonJS file
        // is returned as it is (its .js output differs from tsc's until module.ts is ported).
        var result = GetEmitModuleFormatOfFile(node) >= ModuleKind.ES2015 ? esmTransform.TransformSourceFile(node) : node;
        currentSourceFile = Node.None;
        Debug.Assert(store.Kind(result) == SyntaxKind.SourceFile);
        return result;
    }
}
