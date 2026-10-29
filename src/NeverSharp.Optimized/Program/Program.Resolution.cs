// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/program.ts (module name resolution of the program) and src/compiler/utilities.ts helpers
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/compiler/fileloader.go and internal/compiler/program.go (GetResolvedModule)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C10. The imports and module augmentations of a file are kept by the
// program (TypeScript stores them in the source file; library files are shared read-only here). With noResolve no
// resolved file is added; no importHelpers (not in the options C10 supports); the JSX runtime import is recorded by text.
//
// program.ts: processImportedModules, collectExternalModuleReferences, getModuleNames, resolveModuleNamesWorker,
// getModeForUsageLocation(Worker), getEmitSyntaxForUsageLocationWorker, getResolutionModeOverride,
// isExclusivelyTypeOnlyImportOrExport, shouldTransformImportCallWorker, getEmitModuleFormatOfFileWorker,
// getImpliedNodeFormatForEmitWorker, getDefaultResolutionModeForFileWorker, getResolutionDiagnostic; utilities.ts:
// forEachDynamicImportOrRequireCall, getNodeAtPosition, importSyntaxAffectsModuleResolution.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Program
{
    private readonly ModuleResolutionCache moduleResolutionCache;

    // program.ts resolvedModules: per file, the resolution of each module name and mode (null: not resolved).
    private readonly Dictionary<SourceFile, Dictionary<(string Name, ModuleKind? Mode), ResolvedModuleFull?>> resolvedModules = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// program.getResolvedModule(file, moduleName, mode)?.resolvedModule: the resolution recorded while the program was
    /// built, null when the name did not resolve or was not resolved for this file and mode.
    /// </summary>
    internal ResolvedModuleFull? GetResolvedModule(SourceFile file, string moduleName, ModuleKind? mode) =>
        resolvedModules.TryGetValue(file, out var resolutions) && resolutions.TryGetValue((moduleName, mode), out var resolved) ? resolved : null;

    /// <summary>program.ts processImportedModules (noResolve: the resolved files are recorded, never added).</summary>
    private void ProcessImportedModules(SourceFile file)
    {
        var (imports, moduleAugmentations) = CollectExternalModuleReferences(file);
        var syntheticJsxImport = GetSyntheticJsxImportText(file, options);
        if (imports.Count == 0 && moduleAugmentations.Count == 0 && syntheticJsxImport is null)
        {
            return;
        }
        // Because global augmentation doesn't have string literal name, we can check for global augmentation as such.
        var moduleNames = GetModuleNames(file.Store, imports, moduleAugmentations);
        var store = file.Store;
        // resolveModuleNamesWorker: the containing file's name made absolute
        var containingFileName = TsPath.GetNormalizedAbsolutePath(store.FileName(file.Root), currentDirectory);
        var resolutionsInFile = new Dictionary<(string Name, ModuleKind? Mode), ResolvedModuleFull?>();
        resolvedModules[file] = resolutionsInFile;
        if (syntheticJsxImport is not null)
        {
            // the synthesized `import "<base>/jsx-runtime"` (createSyntheticImport, the first import): its specifier's mode
            // is getModeForUsageLocation of a string literal whose parent is an import declaration of the file
            var mode = ImportSyntaxAffectsModuleResolution(options) ? GetEmitSyntaxOfFile(file, options) : null;
            syntheticJsxImports[file] = (syntheticJsxImport, mode);
            resolutionsInFile[(syntheticJsxImport, mode)] = ModuleNameResolver.ResolveModuleName(syntheticJsxImport, containingFileName, options, host, moduleResolutionCache, mode);
        }
        foreach (var moduleNameNode in moduleNames)
        {
            var moduleName = store.Texts.GetString(store.Text(moduleNameNode))!;
            var mode = GetModeForUsageLocationWorker(file, moduleNameNode, options);
            // resolveModuleNamesReusingOldState (no old program) -> actualResolveModuleNamesWorker: resolveModuleName
            // with the program's cache, for each name (a name repeated in the file is resolved again from the cache)
            var resolution = ModuleNameResolver.ResolveModuleName(moduleName, containingFileName, options, host, moduleResolutionCache, mode);
            resolutionsInFile[(moduleName, mode)] = resolution;
            // (addResolutionDiagnosticsFromResolutionOrCache: the ported resolution produces no resolution diagnostics.)
            // (noResolve: no resolved file is added to the program.)
        }
    }

    // the files with a synthesized JSX runtime import: its text and the mode it was resolved with
    private readonly Dictionary<SourceFile, (string Text, ModuleKind? Mode)> syntheticJsxImports = new(ReferenceEqualityComparer.Instance);

    /// <summary>The synthesized JSX runtime import of <paramref name="file"/> (checker.ts getJSXRuntimeImportSpecifier), if any.</summary>
    internal (string Text, ModuleKind? Mode)? GetSyntheticJsxImport(SourceFile file) => syntheticJsxImports.TryGetValue(file, out var s) ? s : null;

    /// <summary>
    /// collectExternalModuleReferences' synthesized JSX import: getJSXRuntimeImport(getJSXImplicitImportBase(options, file))
    /// for JavaScript files and for non-declaration files that are modules or under isolatedModules (importHelpers: not in
    /// Track O's options).
    /// </summary>
    private static string? GetSyntheticJsxImportText(SourceFile file, CompilerOptions options)
    {
        var store = file.Store;
        var root = file.Root;
        if (!(Utilities.IsInJSFile(store, root) || (!store.IsDeclarationFile(root) && (options.GetIsolatedModules() || Utilities.IsExternalModule(store, root)))))
        {
            return null;
        }
        // utilities.ts getJSXImplicitImportBase (the jsxImportSource option: not in Track O's options)
        var pragmas = store.Pragmas(root);
        var jsxImportSourcePragma = Checker.LastPragmaFactory(pragmas, "jsximportsource", out var hasImportSource);
        var jsxRuntimeFactory = Checker.LastPragmaFactory(pragmas, "jsxruntime", out _);
        if (jsxRuntimeFactory == "classic")
        {
            return null;
        }
        var importBase = options.Jsx is JsxEmit.ReactJSX or JsxEmit.ReactJSXDev || hasImportSource || jsxRuntimeFactory == "automatic"
            ? (!string.IsNullOrEmpty(jsxImportSourcePragma) ? jsxImportSourcePragma : "react")
            : null;
        // utilities.ts getJSXRuntimeImport
        return importBase is null ? null : importBase + "/" + (options.Jsx == JsxEmit.ReactJSXDev ? "jsx-dev-runtime" : "jsx-runtime");
    }

    // getEmitSyntaxForUsageLocationWorker for a usage in an import declaration of the file (no import-equals, require or
    // import call): the file's emit module format
    private static ModuleKind? GetEmitSyntaxOfFile(SourceFile file, CompilerOptions options)
    {
        var fileEmitMode = GetEmitModuleFormatOfFileWorker(file, options);
        return fileEmitMode == ModuleKind.CommonJS ? ModuleKind.CommonJS
            : EmitModuleKindIsNonNodeESM(fileEmitMode) || fileEmitMode == ModuleKind.Preserve ? ModuleKind.ESNext
            : null;
    }

    // program.ts getModuleNames
    private static List<Node> GetModuleNames(NodeStore store, List<Node> imports, List<Node> moduleAugmentations)
    {
        var result = new List<Node>(imports);
        foreach (var augmentation in moduleAugmentations)
        {
            if (store.Kind(augmentation) == SyntaxKind.StringLiteral)
            {
                result.Add(augmentation);
            }
            // Do nothing if it's an Identifier; we don't need to do module resolution for `declare global`.
        }
        return result;
    }

    /// <summary>
    /// program.ts collectExternalModuleReferences: the file's imports (string literals of import and export declarations,
    /// import-equals declarations, dynamic imports, import types, require calls and JSDoc imports in JavaScript files)
    /// and module augmentation names. (ambientModuleNames: read only for program reuse.)
    /// </summary>
    private static (List<Node> Imports, List<Node> ModuleAugmentations) CollectExternalModuleReferences(SourceFile file)
    {
        var store = file.Store;
        var root = file.Root;
        var isJavaScriptFile = Utilities.IsInJSFile(store, root);
        var isExternalModuleFile = Utilities.IsExternalModule(store, root);
        var isDeclarationFile = store.IsDeclarationFile(root);
        // Library files are shared and already bound (their parents are set); TypeScript's setParentRecursive writes the
        // same parents.
        var setParents = file.Binding is null;
        var imports = new List<Node>();
        var moduleAugmentations = new List<Node>();

        // (options.importHelpers and the JSX runtime import: not in the options C10 supports.)

        foreach (var node in store.Nodes(store.Statements(root)))
        {
            CollectModuleReferences(node, inAmbientModule: false);
        }

        if ((store.Flags(root) & NodeFlags.PossiblyContainsDynamicImport) != 0 || isJavaScriptFile)
        {
            ForEachDynamicImportOrRequireCall(store, root, isJavaScriptFile, (node, moduleSpecifier) =>
            {
                if (setParents)
                {
                    Utilities.SetParentRecursive(store, node, incremental: false); // we need parent data on imports before the program is fully bound, so we ensure it's set here
                }
                imports.Add(moduleSpecifier);
            });
        }
        return (imports, moduleAugmentations);

        void CollectModuleReferences(Node node, bool inAmbientModule)
        {
            var kind = store.Kind(node);
            if (kind is SyntaxKind.ImportDeclaration or SyntaxKind.ImportEqualsDeclaration or SyntaxKind.ExportDeclaration)
            {
                var moduleNameExpr = Utilities.GetExternalModuleName(store, node);
                // TypeScript 1.0 spec (April 2014): 12.1.6
                // An ExternalImportDeclaration in an AmbientExternalModuleDeclaration may reference other external modules
                // only through top - level external module names. Relative external module names are not permitted.
                if (!moduleNameExpr.IsNone && store.Kind(moduleNameExpr) == SyntaxKind.StringLiteral)
                {
                    var text = store.Texts.GetString(store.Text(moduleNameExpr))!;
                    if (text.Length > 0 && (!inAmbientModule || !Utilities.IsExternalModuleNameRelative(text)))
                    {
                        if (setParents)
                        {
                            Utilities.SetParentRecursive(store, node, incremental: false); // we need parent data on imports before the program is fully bound, so we ensure it's set here
                        }
                        imports.Add(moduleNameExpr);
                        // (usesUriStyleNodeCoreModules: read only by the language service.)
                    }
                }
            }
            else if (kind == SyntaxKind.ModuleDeclaration)
            {
                if (Utilities.IsAmbientModule(store, node) && (inAmbientModule || Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Ambient) || isDeclarationFile))
                {
                    var name = store.Name(node);
                    if (setParents)
                    {
                        Utilities.SetParent(store, name, node);
                    }
                    var nameText = store.Texts.GetString(Utilities.GetTextOfIdentifierOrLiteral(store, name))!;
                    // Ambient module declarations can be interpreted as augmentations for some existing external modules.
                    // This will happen in two cases:
                    // - if current file is external module then module augmentation is a ambient module declaration defined in the top level scope
                    // - if current file is not external module then module augmentation is an ambient module declaration with non-relative module name
                    //   immediately nested in top level ambient module declaration .
                    if (isExternalModuleFile || (inAmbientModule && !Utilities.IsExternalModuleNameRelative(nameText)))
                    {
                        moduleAugmentations.Add(name);
                    }
                    else if (!inAmbientModule)
                    {
                        // An AmbientExternalModuleDeclaration declares an external module.
                        // This type of declaration is permitted only in the global module.
                        // The StringLiteral must specify a top - level external module name.
                        // Relative external module names are not permitted

                        // NOTE: body of ambient module is always a module block, if it exists
                        var body = store.Body(node);
                        if (!body.IsNone)
                        {
                            foreach (var statement in store.Nodes(store.Statements(body)))
                            {
                                CollectModuleReferences(statement, inAmbientModule: true);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// utilities.ts forEachDynamicImportOrRequireCall(file, includeTypeSpaceImports: true,
    /// requireStringLiteralLikeArgument: true, cb): every match of /import|require/g in the text, at the deepest node
    /// containing the end of the match.
    /// </summary>
    private static void ForEachDynamicImportOrRequireCall(NodeStore store, Node file, bool isJavaScriptFile, Action<Node, Node> cb)
    {
        var text = store.SourceFileText(file).Chars.AsSpan();
        var nextImport = text.IndexOf("import", StringComparison.Ordinal);
        var nextRequire = text.IndexOf("require", StringComparison.Ordinal);
        while (nextImport >= 0 || nextRequire >= 0)
        {
            // the regular expression's next match: the earlier one (at the same index "import" is tried first)
            int lastIndex;
            if (nextRequire < 0 || (nextImport >= 0 && nextImport <= nextRequire))
            {
                lastIndex = nextImport + "import".Length;
            }
            else
            {
                lastIndex = nextRequire + "require".Length;
            }
            if (nextImport >= 0 && nextImport < lastIndex)
            {
                nextImport = IndexFrom(text, "import", lastIndex);
            }
            if (nextRequire >= 0 && nextRequire < lastIndex)
            {
                nextRequire = IndexFrom(text, "require", lastIndex);
            }

            var node = GetNodeAtPosition(store, file, lastIndex, includeJSDoc: true, isJavaScriptFile);
            var kind = store.Kind(node);
            if (isJavaScriptFile && IsRequireCall(store, node))
            {
                cb(node, store.Nodes(store.Arguments(node))[0]);
            }
            else if (Utilities.IsImportCall(store, node) && FirstArgument(store, node) is var first && !first.IsNone && UtilitiesPublic.IsStringLiteralLike(store, first))
            {
                cb(node, first);
            }
            else if (kind == SyntaxKind.ImportType && store.Argument(node) is var argument && !argument.IsNone
                && store.Kind(argument) == SyntaxKind.LiteralType && store.Kind(store.Literal(argument)) == SyntaxKind.StringLiteral)
            {
                // isLiteralImportTypeNode(node)
                cb(node, store.Literal(argument));
            }
            else if (kind == SyntaxKind.JSDocImportTag)
            {
                var moduleNameExpr = Utilities.GetExternalModuleName(store, node);
                if (!moduleNameExpr.IsNone && store.Kind(moduleNameExpr) == SyntaxKind.StringLiteral && store.Texts.GetString(store.Text(moduleNameExpr))!.Length > 0)
                {
                    cb(node, moduleNameExpr);
                }
            }
        }

        static Node FirstArgument(NodeStore store, Node call)
        {
            var arguments = store.Nodes(store.Arguments(call));
            return arguments.Length >= 1 ? arguments[0] : default;
        }

        static int IndexFrom(ReadOnlySpan<char> text, string value, int start)
        {
            var i = text[start..].IndexOf(value, StringComparison.Ordinal);
            return i < 0 ? -1 : start + i;
        }
    }

    // utilities.ts isRequireCall(callExpression, requireStringLiteralLikeArgument: true)
    private static bool IsRequireCall(NodeStore store, Node node)
    {
        if (store.Kind(node) != SyntaxKind.CallExpression)
        {
            return false;
        }
        var expression = store.Expression(node);
        if (store.Kind(expression) != SyntaxKind.Identifier || !store.Texts.Equals(store.EscapedText(expression), "require"))
        {
            return false;
        }
        var arguments = store.Nodes(store.Arguments(node));
        return arguments.Length == 1 && UtilitiesPublic.IsStringLiteralLike(store, arguments[0]);
    }

    /// <summary>utilities.ts getNodeAtPosition: a token if position is in [start-of-leading-trivia, end), includes JSDoc only in JS files.</summary>
    private static Node GetNodeAtPosition(NodeStore store, Node sourceFile, int position, bool includeJSDoc, bool isJavaScriptFile)
    {
        var current = sourceFile;
        while (true)
        {
            var child = Node.None;
            if (isJavaScriptFile && includeJSDoc && store.JsDoc(current) is { Count: > 0 } jsDoc)
            {
                foreach (var doc in jsDoc)
                {
                    if (Contains(store, doc, position))
                    {
                        child = doc;
                        break;
                    }
                }
            }
            if (child.IsNone)
            {
                child = NodeVisitor.ForEachChild<ContainingChildVisitor, Node>(store, current, new ContainingChildVisitor(store, position));
            }
            if (child.IsNone || store.Kind(child) == SyntaxKind.MetaProperty)
            {
                return current;
            }
            current = child;
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool Contains(NodeStore store, Node child, int position) =>
        store.Pos(child) <= position && (position < store.End(child) || (position == store.End(child) && store.Kind(child) == SyntaxKind.EndOfFileToken));

    // getNodeAtPosition's getContainingChild for forEachChild (lists element by element, as without cbNodes)
    private readonly struct ContainingChildVisitor : INodeVisitor<Node>
    {
        private readonly NodeStore store;
        private readonly int position;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        internal ContainingChildVisitor(NodeStore store, int position)
        {
            this.store = store;
            this.position = position;
        }

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node VisitNode(Node node) => Contains(store, node, position) ? node : default;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node VisitNodes(NodeList nodes)
        {
            foreach (var node in store.Nodes(nodes))
            {
                if (Contains(store, node, position))
                {
                    return node;
                }
            }
            return default;
        }
    }

    /// <summary>program.getModeForUsageLocation(file, usage): the resolution mode of a module name.</summary>
    internal ModuleKind? GetModeForUsageLocation(SourceFile file, Node usage) => GetModeForUsageLocationWorker(file, usage, options);

    /// <summary>program.getDefaultResolutionModeForFile(file) (getDefaultResolutionModeForFileWorker).</summary>
    internal ModuleKind? GetDefaultResolutionModeForFile(SourceFile file) =>
        ImportSyntaxAffectsModuleResolution(options) ? GetImpliedNodeFormatForEmitWorker(file, options) : null;

    private static ModuleKind? GetModeForUsageLocationWorker(SourceFile file, Node usage, CompilerOptions compilerOptions)
    {
        var store = file.Store;
        var parent = store.Parent(usage);
        if (!parent.IsNone && store.Kind(parent) is SyntaxKind.ImportDeclaration or SyntaxKind.ExportDeclaration or SyntaxKind.JSDocImportTag)
        {
            var isTypeOnly = IsExclusivelyTypeOnlyImportOrExport(store, parent);
            if (isTypeOnly)
            {
                var @override = GetResolutionModeOverride(store, store.Attributes(parent));
                if (@override is not null)
                {
                    return @override;
                }
            }
        }
        var grandParent = parent.IsNone ? Node.None : store.Parent(parent);
        if (!grandParent.IsNone && store.Kind(grandParent) == SyntaxKind.ImportType)
        {
            var @override = GetResolutionModeOverride(store, store.Attributes(grandParent));
            if (@override is not null)
            {
                return @override;
            }
        }
        if (ImportSyntaxAffectsModuleResolution(compilerOptions))
        {
            return GetEmitSyntaxForUsageLocationWorker(file, usage, compilerOptions);
        }
        return null;
    }

    private static ModuleKind? GetEmitSyntaxForUsageLocationWorker(SourceFile file, Node usage, CompilerOptions compilerOptions)
    {
        var store = file.Store;
        var parent = store.Parent(usage);
        var walked = WalkUpParenthesizedExpressions(store, parent);
        var exprParentParent = walked.IsNone ? Node.None : store.Parent(walked);
        if ((!exprParentParent.IsNone && store.Kind(exprParentParent) == SyntaxKind.ImportEqualsDeclaration) || IsRequireCallAnyArgument(store, parent))
        {
            return ModuleKind.CommonJS;
        }
        if (Utilities.IsImportCall(store, walked))
        {
            return ShouldTransformImportCallWorker(file, compilerOptions) ? ModuleKind.CommonJS : ModuleKind.ESNext;
        }
        // If we're in --module preserve on an input file, we know that an import
        // is an import. But if this is a declaration file, we'd prefer to use the
        // impliedNodeFormat. Since we want things to be consistent between the two,
        // we need to issue errors when the user writes ESM syntax in a definitely-CJS
        // file, until/unless declaration emit can indicate a true ESM import. On the
        // other hand, writing CJS syntax in a definitely-ESM file is fine, since declaration
        // emit preserves the CJS syntax.
        var fileEmitMode = GetEmitModuleFormatOfFileWorker(file, compilerOptions);
        return fileEmitMode == ModuleKind.CommonJS ? ModuleKind.CommonJS
            : EmitModuleKindIsNonNodeESM(fileEmitMode) || fileEmitMode == ModuleKind.Preserve ? ModuleKind.ESNext
            : null;
    }

    // utilities.ts walkUpParenthesizedExpressions
    private static Node WalkUpParenthesizedExpressions(NodeStore store, Node node)
    {
        while (!node.IsNone && store.Kind(node) == SyntaxKind.ParenthesizedExpression)
        {
            node = store.Parent(node);
        }
        return node;
    }

    // utilities.ts isRequireCall(callExpression, requireStringLiteralLikeArgument: false)
    private static bool IsRequireCallAnyArgument(NodeStore store, Node node)
    {
        if (node.IsNone || store.Kind(node) != SyntaxKind.CallExpression)
        {
            return false;
        }
        var expression = store.Expression(node);
        return store.Kind(expression) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(expression), "require")
            && store.Nodes(store.Arguments(node)).Length == 1;
    }

    // program.ts isExclusivelyTypeOnlyImportOrExport
    private static bool IsExclusivelyTypeOnlyImportOrExport(NodeStore store, Node decl)
    {
        if (store.Kind(decl) == SyntaxKind.ExportDeclaration)
        {
            return store.IsTypeOnly(decl);
        }
        var importClause = store.ImportClause(decl);
        return !importClause.IsNone && store.IsTypeOnly(importClause);
    }

    /// <summary>program.ts getResolutionModeOverride (without the grammar errors, which the checker reports).</summary>
    private static ModuleKind? GetResolutionModeOverride(NodeStore store, Node node)
    {
        if (node.IsNone)
        {
            return null;
        }
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

    // utilities.ts importSyntaxAffectsModuleResolution
    private static bool ImportSyntaxAffectsModuleResolution(CompilerOptions options)
    {
        var moduleResolution = options.GetEmitModuleResolutionKind();
        return (ModuleResolutionKind.Node16 <= moduleResolution && moduleResolution <= ModuleResolutionKind.NodeNext)
            || options.GetResolvePackageJsonExports()
            || options.GetResolvePackageJsonImports();
    }

    // program.ts shouldTransformImportCallWorker
    private static bool ShouldTransformImportCallWorker(SourceFile file, CompilerOptions options)
    {
        var moduleKind = options.GetEmitModuleKind();
        if ((ModuleKind.Node16 <= moduleKind && moduleKind <= ModuleKind.NodeNext) || moduleKind == ModuleKind.Preserve)
        {
            return false;
        }
        return GetEmitModuleFormatOfFileWorker(file, options) < ModuleKind.ES2015;
    }

    // program.ts getEmitModuleFormatOfFileWorker
    private static ModuleKind GetEmitModuleFormatOfFileWorker(SourceFile file, CompilerOptions options) =>
        GetImpliedNodeFormatForEmitWorker(file, options) ?? options.GetEmitModuleKind();

    /// <summary>
    /// program.ts getImpliedNodeFormatForEmitWorker. The program's files carry the implied node format of
    /// getImpliedNodeFormatForFile, which for the options C10 supports (no node16/nodenext resolution, no files in
    /// node_modules) depends on the extension only: ESNext for .mts/.mjs (and .d.mts), CommonJS for .cts/.cjs (and .d.cts),
    /// otherwise none; there is no package.json scope.
    /// </summary>
    private static ModuleKind? GetImpliedNodeFormatForEmitWorker(SourceFile file, CompilerOptions options)
    {
        var moduleKind = options.GetEmitModuleKind();
        if (ModuleKind.Node16 <= moduleKind && moduleKind <= ModuleKind.NodeNext)
        {
            throw new NotSupportedException("Program (module C10): node16/nodenext module formats are not ported");
        }
        var fileName = file.Store.FileName(file.Root);
        if (FileExtensionIsOneOf(fileName, ".cjs", ".cts"))
        {
            return ModuleKind.CommonJS;
        }
        if (FileExtensionIsOneOf(fileName, ".mjs", ".mts"))
        {
            return ModuleKind.ESNext;
        }
        return null;
    }

    // utilities.ts emitModuleKindIsNonNodeESM
    private static bool EmitModuleKindIsNonNodeESM(ModuleKind moduleKind) => moduleKind >= ModuleKind.ES2015 && moduleKind <= ModuleKind.ESNext;

    /// <summary>program.ts getResolutionDiagnostic.</summary>
    internal static DiagnosticMessage? GetResolutionDiagnostic(CompilerOptions options, ResolvedModuleFull resolvedModule, bool isDeclarationFile)
    {
        switch (resolvedModule.Extension)
        {
            case Extension.Ts:
            case Extension.Dts:
            case Extension.Mts:
            case Extension.Dmts:
            case Extension.Cts:
            case Extension.Dcts:
                // These are always allowed.
                return null;
            case Extension.Tsx:
                return NeedJsx();
            case Extension.Jsx:
                return NeedJsx() ?? NeedAllowJs();
            case Extension.Js:
            case Extension.Mjs:
            case Extension.Cjs:
                return NeedAllowJs();
            case Extension.Json:
                return NeedResolveJsonModule();
            default:
                return NeedAllowArbitraryExtensions();
        }

        DiagnosticMessage? NeedJsx() => options.Jsx is not null and not JsxEmit.None ? null : Diagnostics.Module_0_was_resolved_to_1_but_jsx_is_not_set;

        DiagnosticMessage? NeedAllowJs() => options.GetAllowJSCompilerOption() || !options.GetStrictOptionValue(options.NoImplicitAny) ? null : Diagnostics.Could_not_find_a_declaration_file_for_module_0_1_implicitly_has_an_any_type;

        DiagnosticMessage? NeedResolveJsonModule() => options.GetResolveJsonModule() ? null : Diagnostics.Module_0_was_resolved_to_1_but_resolveJsonModule_is_not_used;

        // But don't report the allowArbitraryExtensions error from declaration files (no reason to report it, since the import doesn't have a runtime component)
        DiagnosticMessage? NeedAllowArbitraryExtensions() => isDeclarationFile || options.AllowArbitraryExtensions == true ? null : Diagnostics.Module_0_was_resolved_to_1_but_allowArbitraryExtensions_is_not_set;
    }
}
