// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (checkImportDeclaration, checkImportBinding, checkImportAttributes,
// checkExternalImportOrExportDeclaration, checkAliasSymbol, checkImportEqualsDeclaration, checkExportDeclaration,
// checkExportSpecifier, checkExportAssignment, getDeclarationContainer, isTopLevelInExternalModuleAugmentation)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go (checkImportDeclaration ... checkExportAssignment)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; modules C1 and C8 (the checkSourceElement driver is in
// Checker.Statements.cs). Options Gate C does not set (isolatedModules, verbatimModuleSyntax, module preserve,
// emitDeclarations) and the emit helpers are left out where noted.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // checker.ts checkImportDeclaration
    private void CheckImportDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (CheckGrammarModuleElementContext(node, Utilities.IsInJSFile(store, n)
            ? Diagnostics.An_import_declaration_can_only_be_used_at_the_top_level_of_a_module
            : Diagnostics.An_import_declaration_can_only_be_used_at_the_top_level_of_a_namespace_or_module))
        {
            // If we hit an import declaration in an illegal context, just bail out to avoid cascading errors.
            return;
        }
        if (!CheckGrammarModifiers(node) && !store.Modifiers(n).IsNone)
        {
            GrammarErrorOnFirstToken(node, Diagnostics.An_import_declaration_cannot_have_modifiers);
        }
        if (CheckExternalImportOrExportDeclaration(node))
        {
            var importClause = store.ImportClause(n);
            var moduleSpecifier = At(node, store.ModuleSpecifier(n));
            if (!importClause.IsNone && !CheckGrammarImportClause(At(node, importClause)))
            {
                if (!store.Name(importClause).IsNone)
                {
                    CheckImportBinding(At(node, importClause));
                }
                var namedBindings = store.NamedBindings(importClause);
                if (!namedBindings.IsNone)
                {
                    if (store.Kind(namedBindings) == SyntaxKind.NamespaceImport)
                    {
                        CheckImportBinding(At(node, namedBindings));
                        // (the __importStar helper: Gate C emits ES modules)
                    }
                    else
                    {
                        var resolvedModule = ResolveExternalModuleName(node, moduleSpecifier);
                        if (!resolvedModule.IsNone)
                        {
                            foreach (var element in store.Nodes(store.Elements(namedBindings)))
                            {
                                CheckImportBinding(At(node, element));
                            }
                        }
                    }
                }
                // (JSON imports in --module node18..nodenext: Gate C uses module ES2022)
            }
            else if (noUncheckedSideEffectImports && importClause.IsNone)
            {
                ResolveExternalModuleName(node, moduleSpecifier, ignoreErrors: false, Diagnostics.Cannot_find_module_or_type_declarations_for_side_effect_import_of_0);
            }
        }
        CheckImportAttributes(node);
    }

    // checker.ts checkImportBinding
    private void CheckImportBinding(NodeRef node)
    {
        var store = StoreOf(node);
        CheckCollisionsForDeclarationName(node, At(node, store.Name(node.Node)));
        CheckAliasSymbol(node);
        if (store.Kind(node.Node) == SyntaxKind.ImportSpecifier)
        {
            CheckModuleExportName(At(node, store.PropertyName(node.Node)));
            // (the __importDefault helper: Gate C emits ES modules)
        }
    }

    // checker.ts checkImportAttributes. The relation of the attributes' object type to the global ImportAttributes type
    // ({ [key: string]: string }) is not checked: attribute values that are not string literals are reported by
    // checkExternalImportOrExportDeclaration already.
    private void CheckImportAttributes(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var node = store.Attributes(declaration.Node);
        if (node.IsNone)
        {
            return;
        }
        var attributes = At(declaration, node);
        var isImportAttributes = store.Token(node) == SyntaxKind.WithKeyword;
        // isExclusivelyTypeOnlyImportOrExport
        var isTypeOnly = store.Kind(declaration.Node) == SyntaxKind.ExportDeclaration
            ? store.IsTypeOnly(declaration.Node)
            : !store.ImportClause(declaration.Node).IsNone && store.PhaseModifier(store.ImportClause(declaration.Node)) == SyntaxKind.TypeKeyword;
        var validForTypeAttributes = isTypeOnly;
        var hasOverride = GetResolutionModeOverride(attributes, reportErrors: validForTypeAttributes);
        if (validForTypeAttributes && hasOverride)
        {
            return; // Other grammar checks do not apply to type-only imports with resolution mode assertions
        }
        // !moduleSupportsImportAttributes(moduleKind)
        if (moduleKind is not (ModuleKind.ESNext or ModuleKind.Node18 or ModuleKind.Node20 or ModuleKind.NodeNext or ModuleKind.Preserve))
        {
            GrammarErrorOnNode(attributes, isImportAttributes
                ? Diagnostics.Import_attributes_are_only_supported_when_the_module_option_is_set_to_esnext_node18_node20_nodenext_or_preserve
                : Diagnostics.Import_assertions_are_only_supported_when_the_module_option_is_set_to_esnext_node18_node20_nodenext_or_preserve);
            return;
        }
        if (moduleKind is >= ModuleKind.Node20 and <= ModuleKind.NodeNext && !isImportAttributes)
        {
            GrammarErrorOnFirstToken(attributes, Diagnostics.Import_assertions_have_been_replaced_by_import_attributes_Use_with_instead_of_assert);
            return;
        }
        if (!isImportAttributes) // (ignoreDeprecations: not an option of Gate C)
        {
            GrammarErrorOnFirstToken(attributes, Diagnostics.Import_assertions_have_been_replaced_by_import_attributes_Use_with_instead_of_assert);
        }
        // getEmitSyntaxForModuleSpecifierExpression: CommonJS in .cts files
        if (!store.ModuleSpecifier(declaration.Node).IsNone && GetImpliedNodeFormatForEmit(declaration.File) == ModuleKind.CommonJS && moduleKind != ModuleKind.Preserve)
        {
            GrammarErrorOnNode(attributes, isImportAttributes
                ? Diagnostics.Import_attributes_are_not_allowed_on_statements_that_compile_to_CommonJS_require_calls
                : Diagnostics.Import_assertions_are_not_allowed_on_statements_that_compile_to_CommonJS_require_calls);
            return;
        }
        if (isTypeOnly)
        {
            GrammarErrorOnNode(attributes, isImportAttributes
                ? Diagnostics.Import_attributes_cannot_be_used_with_type_only_imports_or_exports
                : Diagnostics.Import_assertions_cannot_be_used_with_type_only_imports_or_exports);
            return;
        }
        if (hasOverride)
        {
            GrammarErrorOnNode(attributes, Diagnostics.resolution_mode_can_only_be_set_for_type_only_imports);
        }
    }

    // checker.ts checkExternalImportOrExportDeclaration
    private bool CheckExternalImportOrExportDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var moduleName = Utilities.GetExternalModuleName(store, n);
        if (moduleName.IsNone || Utilities.NodeIsMissing(store, moduleName))
        {
            // Should be a parse error.
            return false;
        }
        if (store.Kind(moduleName) != SyntaxKind.StringLiteral)
        {
            Error(At(node, moduleName), Diagnostics.String_literal_expected);
            return false;
        }
        var parent = store.Parent(n);
        var inAmbientExternalModule = store.Kind(parent) == SyntaxKind.ModuleBlock && Utilities.IsAmbientModule(store, store.Parent(parent));
        if (store.Kind(parent) != SyntaxKind.SourceFile && !inAmbientExternalModule)
        {
            Error(At(node, moduleName), store.Kind(n) == SyntaxKind.ExportDeclaration
                ? Diagnostics.Export_declarations_are_not_permitted_in_a_namespace
                : Diagnostics.Import_declarations_in_a_namespace_cannot_reference_a_module);
            return false;
        }
        if (inAmbientExternalModule && Utilities.IsExternalModuleNameRelative(store.Texts.GetString(store.Text(moduleName))!))
        {
            // we have already reported errors on top level imports/exports in external module augmentations in checkModuleDeclaration
            // no need to do this again.
            if (!IsTopLevelInExternalModuleAugmentation(store, n))
            {
                // TypeScript 1.0 spec (April 2013): 12.1.6
                // An ExternalImportDeclaration in an AmbientExternalModuleDeclaration may reference
                // other external modules only through top - level external module names.
                // Relative external module names are not permitted.
                Error(node, Diagnostics.Import_or_export_declaration_in_an_ambient_module_declaration_cannot_reference_module_through_relative_module_name);
                return false;
            }
        }
        if (store.Kind(n) != SyntaxKind.ImportEqualsDeclaration)
        {
            var attributes = store.Attributes(n);
            if (!attributes.IsNone)
            {
                var diagnostic = store.Token(attributes) == SyntaxKind.WithKeyword ? Diagnostics.Import_attribute_values_must_be_string_literal_expressions : Diagnostics.Import_assertion_values_must_be_string_literal_expressions;
                var hasError = false;
                foreach (var attr in store.Nodes(store.Elements(attributes)))
                {
                    var value = store.Value(attr);
                    if (store.Kind(value) != SyntaxKind.StringLiteral)
                    {
                        hasError = true;
                        Error(At(node, value), diagnostic);
                    }
                }
                return !hasError;
            }
        }
        return true;
    }

    // utilities.ts isTopLevelInExternalModuleAugmentation
    private static bool IsTopLevelInExternalModuleAugmentation(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        return !parent.IsNone && store.Kind(parent) == SyntaxKind.ModuleBlock && Utilities.IsModuleAugmentationExternal(store, store.Parent(parent));
    }

    // checker.ts checkAliasSymbol
    private void CheckAliasSymbol(NodeRef node)
    {
        var symbol = GetSymbolOfDeclaration(node);
        if (symbol.IsNone)
        {
            return;
        }
        var target = ResolveAlias(symbol);
        if (target == unknownSymbol)
        {
            return;
        }
        // For external modules, `symbol` represents the local symbol for an alias.
        // This local symbol will merge any other local declarations (excluding other aliases)
        // and symbol.flags will contains combined representation for all merged declaration.
        // Based on symbol.flags we can compute a set of excluded meanings (meaning that resolved alias should not have,
        // otherwise it will conflict with some local declaration). Note that in addition to normal flags we include matching SymbolFlags.Export*
        // in order to prevent collisions with declarations that were exported from the current module (they still contribute to local names).
        var exportSymbol = ExportSymbolOf(symbol);
        symbol = GetMergedSymbol(exportSymbol.IsNone ? symbol : exportSymbol);
        // A type-only import/export will already have a grammar error in a JS file, so no need to issue more errors within
        var aliasStore = StoreOf(node);
        if (Utilities.IsInJSFile(aliasStore, node.Node) && (GetSymbolFlags(target) & SymbolFlags.Value) == 0 && !IsTypeOnlyImportOrExportDeclaration(node))
        {
            var aliasKind = aliasStore.Kind(node.Node);
            var errorNode = aliasKind is SyntaxKind.ImportSpecifier or SyntaxKind.ExportSpecifier
                ? (!aliasStore.PropertyName(node.Node).IsNone ? aliasStore.PropertyName(node.Node) : aliasStore.Name(node.Node))
                : !aliasStore.Name(node.Node).IsNone ? aliasStore.Name(node.Node) : node.Node;
            if (aliasKind == SyntaxKind.ExportSpecifier)
            {
                // (the related "automatically exported here" information names a JSDoc declaration of another file's
                // symbol: not reached in Gate C's one-file programs)
                Error(At(node, errorNode), Diagnostics.Types_cannot_appear_in_export_declarations_in_JavaScript_files);
            }
            else
            {
                var importDeclaration = node.Node;
                while (!importDeclaration.IsNone && aliasStore.Kind(importDeclaration) is not (SyntaxKind.ImportDeclaration or SyntaxKind.ImportEqualsDeclaration))
                {
                    importDeclaration = aliasStore.Parent(importDeclaration);
                }
                var specifierNode = importDeclaration.IsNone ? Node.None
                    : aliasStore.Kind(importDeclaration) == SyntaxKind.ImportDeclaration ? aliasStore.ModuleSpecifier(importDeclaration)
                    : aliasStore.Kind(aliasStore.ModuleReference(importDeclaration)) == SyntaxKind.ExternalModuleReference ? aliasStore.Expression(aliasStore.ModuleReference(importDeclaration))
                    : Node.None;
                var moduleSpecifier = !specifierNode.IsNone && Utilities.IsStringLiteralLike(aliasStore, specifierNode) ? aliasStore.Texts.GetString(aliasStore.Text(specifierNode))! : "...";
                var importedIdentifier = aliasStore.Kind(errorNode) == SyntaxKind.Identifier ? aliasStore.Texts.GetString(aliasStore.EscapedText(errorNode))! : NameOf(GetMergedSymbol(ExportSymbolOf(symbol).IsNone ? symbol : ExportSymbolOf(symbol)));
                importedIdentifier = Utilities.UnescapeLeadingUnderscores(importedIdentifier);
                Error(At(node, errorNode), Diagnostics._0_is_a_type_and_cannot_be_imported_in_JavaScript_files_Use_1_in_a_JSDoc_type_annotation,
                    importedIdentifier, "import(\"" + moduleSpecifier + "\")." + importedIdentifier);
            }
            return;
        }
        var targetFlags = GetSymbolFlags(target);
        var symbolFlags = SymbolFlagsOf(symbol);
        var excludedMeanings = ((symbolFlags & (SymbolFlags.Value | SymbolFlags.ExportValue)) != 0 ? SymbolFlags.Value : 0) |
            ((symbolFlags & SymbolFlags.Type) != 0 ? SymbolFlags.Type : 0) |
            ((symbolFlags & SymbolFlags.Namespace) != 0 ? SymbolFlags.Namespace : 0);
        if ((targetFlags & excludedMeanings) != 0)
        {
            var message = Kind(node) == SyntaxKind.ExportSpecifier
                ? Diagnostics.Export_declaration_conflicts_with_exported_declaration_of_0
                : Diagnostics.Import_declaration_conflicts_with_local_declaration_of_0;
            Error(node, message, SymbolToString(symbol));
        }
        // (isolatedModules and verbatimModuleSyntax checks: Gate C sets neither)
        if (Kind(node) == SyntaxKind.ImportSpecifier)
        {
            var targetSymbol = ResolveAliasWithDeprecationCheck(symbol, node);
            if (IsDeprecatedSymbol(targetSymbol) && DeclarationsOf(targetSymbol) is { } targetDeclarations)
            {
                AddDeprecatedSuggestion(node, targetDeclarations, NameOf(targetSymbol));
            }
        }
    }

    // checker.ts checkImportEqualsDeclaration
    private void CheckImportEqualsDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (CheckGrammarModuleElementContext(node, Utilities.IsInJSFile(store, n)
            ? Diagnostics.An_import_declaration_can_only_be_used_at_the_top_level_of_a_module
            : Diagnostics.An_import_declaration_can_only_be_used_at_the_top_level_of_a_namespace_or_module))
        {
            // If we hit an import declaration in an illegal context, just bail out to avoid cascading errors.
            return;
        }
        CheckGrammarModifiers(node);
        // (erasableSyntaxOnly: not an option of Gate C)
        if (Utilities.IsInternalModuleImportEqualsDeclaration(store, n) || CheckExternalImportOrExportDeclaration(node))
        {
            CheckImportBinding(node);
            if (store.Kind(store.ModuleReference(n)) != SyntaxKind.ExternalModuleReference)
            {
                var target = ResolveAlias(GetSymbolOfDeclaration(node));
                if (target != unknownSymbol)
                {
                    var targetFlags = GetSymbolFlags(target);
                    if ((targetFlags & SymbolFlags.Value) != 0)
                    {
                        // Target is a value symbol, check that it is not hidden by a local declaration with the same name
                        var moduleName = At(node, Utilities.GetFirstIdentifier(store, store.ModuleReference(n)));
                        var resolved = ResolveEntityName(moduleName, SymbolFlags.Value | SymbolFlags.Namespace);
                        if (!resolved.IsNone && (SymbolFlagsOf(resolved) & SymbolFlags.Namespace) == 0)
                        {
                            Error(moduleName, Diagnostics.Module_0_is_hidden_by_a_local_declaration_with_the_same_name, Utilities.DeclarationNameToString(store, moduleName.Node));
                        }
                    }
                    if ((targetFlags & SymbolFlags.Type) != 0)
                    {
                        CheckTypeNameIsReserved(At(node, store.Name(n)), Diagnostics.Import_name_cannot_be_0);
                    }
                }
                if (store.IsTypeOnly(n))
                {
                    GrammarErrorOnNode(node, Diagnostics.An_import_alias_cannot_use_import_type);
                }
            }
            else if (moduleKind >= ModuleKind.ES2015 && moduleKind <= ModuleKind.ESNext && !store.IsTypeOnly(n) && (store.Flags(n) & NodeFlags.Ambient) == 0)
            {
                // Import equals declaration cannot be emitted as ESM
                GrammarErrorOnNode(node, Diagnostics.Import_assignment_cannot_be_used_when_targeting_ECMAScript_modules_Consider_using_import_Asterisk_as_ns_from_mod_import_a_from_mod_import_d_from_mod_or_another_module_format_instead);
            }
        }
    }

    // checker.ts checkExportDeclaration
    private void CheckExportDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (CheckGrammarModuleElementContext(node, Utilities.IsInJSFile(store, n)
            ? Diagnostics.An_export_declaration_can_only_be_used_at_the_top_level_of_a_module
            : Diagnostics.An_export_declaration_can_only_be_used_at_the_top_level_of_a_namespace_or_module))
        {
            // If we hit an export in an illegal context, just bail out to avoid cascading errors.
            return;
        }
        if (!CheckGrammarModifiers(node) && Utilities.GetSyntacticModifierFlagsNoCache(store, n) != 0)
        {
            GrammarErrorOnFirstToken(node, Diagnostics.An_export_declaration_cannot_have_modifiers);
        }
        CheckGrammarExportDeclaration(node);
        var moduleSpecifier = store.ModuleSpecifier(n);
        if (moduleSpecifier.IsNone || CheckExternalImportOrExportDeclaration(node))
        {
            var exportClause = store.ExportClause(n);
            if (!exportClause.IsNone && store.Kind(exportClause) != SyntaxKind.NamespaceExport)
            {
                // export { x, y }
                // export { x, y } from "foo"
                foreach (var specifier in store.Nodes(store.Elements(exportClause)))
                {
                    CheckExportSpecifier(At(node, specifier));
                }
                var parent = store.Parent(n);
                var inAmbientExternalModule = store.Kind(parent) == SyntaxKind.ModuleBlock && Utilities.IsAmbientModule(store, store.Parent(parent));
                var inAmbientNamespaceDeclaration = !inAmbientExternalModule && store.Kind(parent) == SyntaxKind.ModuleBlock && moduleSpecifier.IsNone && (store.Flags(n) & NodeFlags.Ambient) != 0;
                if (store.Kind(parent) != SyntaxKind.SourceFile && !inAmbientExternalModule && !inAmbientNamespaceDeclaration)
                {
                    Error(node, Diagnostics.Export_declarations_are_not_permitted_in_a_namespace);
                }
            }
            else
            {
                // export * from "foo"
                // export * as ns from "foo";
                var moduleSymbol = ResolveExternalModuleName(node, At(node, moduleSpecifier));
                if (!moduleSymbol.IsNone && (ExportsOf(moduleSymbol)?.Has(InternalSymbolName.ExportEquals) ?? false))
                {
                    Error(At(node, moduleSpecifier), Diagnostics.Module_0_uses_export_and_cannot_be_used_with_export_Asterisk, SymbolToString(moduleSymbol));
                }
                else if (!exportClause.IsNone)
                {
                    CheckAliasSymbol(At(node, exportClause));
                    CheckModuleExportName(At(node, store.Name(exportClause)));
                }
                // (the __importStar and __exportStar helpers: Gate C emits ES modules)
            }
        }
        CheckImportAttributes(node);
    }

    // checker.ts checkExportSpecifier
    private void CheckExportSpecifier(NodeRef node)
    {
        CheckAliasSymbol(node);
        var store = StoreOf(node);
        var n = node.Node;
        var hasModuleSpecifier = !store.ModuleSpecifier(store.Parent(store.Parent(n))).IsNone;
        CheckModuleExportName(At(node, store.PropertyName(n)), hasModuleSpecifier);
        CheckModuleExportName(At(node, store.Name(n)));
        if (!hasModuleSpecifier)
        {
            var exportedName = !store.PropertyName(n).IsNone ? store.PropertyName(n) : store.Name(n);
            if (store.Kind(exportedName) == SyntaxKind.StringLiteral)
            {
                return; // Skip for invalid syntax like this: export { "x" }
            }
            // find immediate value referenced by exported name (SymbolFlags.Alias is set so we don't chase down aliases)
            var symbol = ResolveName(At(node, exportedName), store.Texts.GetString(store.EscapedText(exportedName))!, NodeRef.None,
                SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias, null, isUse: true);
            if (!symbol.IsNone && (symbol == undefinedSymbol || symbol == globalThisSymbol ||
                (DeclarationsOf(symbol) is { Count: > 0 } declarations && IsGlobalSourceFile(GetDeclarationContainer(declarations[0])))))
            {
                Error(At(node, exportedName), Diagnostics.Cannot_export_0_Only_local_declarations_can_be_exported_from_a_module, store.Texts.GetString(Utilities.IdText(store, exportedName)));
            }
            // (markLinkedReferences: emit only)
        }
    }

    // utilities.ts getDeclarationContainer
    private NodeRef GetDeclarationContainer(NodeRef node)
    {
        var store = StoreOf(node);
        var n = Utilities.GetRootDeclaration(store, node.Node);
        for (n = store.Parent(n); !n.IsNone; n = store.Parent(n))
        {
            if (store.Kind(n) is not (SyntaxKind.VariableDeclaration or SyntaxKind.VariableDeclarationList or SyntaxKind.ImportSpecifier
                or SyntaxKind.NamedImports or SyntaxKind.NamespaceImport or SyntaxKind.ImportClause))
            {
                return At(node, n);
            }
        }
        return NodeRef.None;
    }

    // checker.ts checkExportAssignment
    private void CheckExportAssignment(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var illegalContextMessage = store.IsExportEquals(n)
            ? Diagnostics.An_export_assignment_must_be_at_the_top_level_of_a_file_or_module_declaration
            : Diagnostics.A_default_export_must_be_at_the_top_level_of_a_file_or_module_declaration;
        if (CheckGrammarModuleElementContext(node, illegalContextMessage))
        {
            // If we hit an export assignment in an illegal context, just bail out to avoid cascading errors.
            return;
        }
        // (erasableSyntaxOnly: not an option of Gate C)
        var parent = store.Parent(n);
        var container = store.Kind(parent) == SyntaxKind.SourceFile ? parent : store.Parent(parent);
        if (store.Kind(container) == SyntaxKind.ModuleDeclaration && !Utilities.IsAmbientModule(store, container))
        {
            Error(node, store.IsExportEquals(n) ? Diagnostics.An_export_assignment_cannot_be_used_in_a_namespace : Diagnostics.A_default_export_can_only_be_used_in_an_ECMAScript_style_module);
            return;
        }
        // Grammar checking
        if (!CheckGrammarModifiers(node) && Utilities.GetSyntacticModifierFlagsNoCache(store, n) != 0)
        {
            GrammarErrorOnFirstToken(node, Diagnostics.An_export_assignment_cannot_have_modifiers);
        }
        // (getEffectiveTypeAnnotationNode: the JSDoc @type of a JavaScript export assignment, module C9)
        var expression = At(node, store.Expression(n));
        if (store.Kind(expression.Node) == SyntaxKind.Identifier)
        {
            var sym = GetExportSymbolOfValueSymbolIfExported(ResolveEntityName(expression, SymbolFlags.All, ignoreErrors: true, dontResolveAlias: true, node));
            if (!sym.IsNone)
            {
                // If not a value, we're interpreting the identifier as a type export, along the lines of (`export { Id as default }`)
                if ((GetSymbolFlags(sym) & SymbolFlags.Value) != 0)
                {
                    // However if it is a value, we need to check it's being used correctly
                    CheckExpressionCached(expression);
                }
            }
            else
            {
                CheckExpressionCached(expression); // doesn't resolve, check as expression to mark as error
            }
        }
        else
        {
            CheckExpressionCached(expression);
        }
        CheckExternalModuleExports(At(node, container));
        if ((store.Flags(n) & NodeFlags.Ambient) != 0 && !Utilities.IsEntityNameExpression(store, expression.Node))
        {
            GrammarErrorOnNode(expression, Diagnostics.The_expression_of_an_export_assignment_must_be_an_identifier_or_qualified_name_in_an_ambient_context);
        }
        if (store.IsExportEquals(n))
        {
            // Forbid export= in esm implementation files, and esm mode declaration files
            var format = GetImpliedNodeFormatForEmit(node.File);
            if (moduleKind >= ModuleKind.ES2015 && moduleKind != ModuleKind.Preserve &&
                (((store.Flags(n) & NodeFlags.Ambient) != 0 && format == ModuleKind.ESNext) ||
                    ((store.Flags(n) & NodeFlags.Ambient) == 0 && format != ModuleKind.CommonJS)))
            {
                // export assignment is not supported in es6 modules
                GrammarErrorOnNode(node, Diagnostics.Export_assignment_cannot_be_used_when_targeting_ECMAScript_modules_Consider_using_export_default_or_another_module_format_instead);
            }
            else if (moduleKind == ModuleKind.System && (store.Flags(n) & NodeFlags.Ambient) == 0)
            {
                // system modules does not support export assignment
                GrammarErrorOnNode(node, Diagnostics.Export_assignment_is_not_supported_when_module_flag_is_system);
            }
        }
    }

    // program.ts getImpliedNodeFormatForEmit for Gate C's options (module ES2022, moduleResolution bundler, no
    // package.json scopes): CommonJS for .cts/.cjs files, ESNext for .mts/.mjs files, otherwise undefined (None).
    private ModuleKind? GetImpliedNodeFormatForEmit(int file)
    {
        var fileName = FileNameOf(file);
        if (fileName.EndsWith(".cts", StringComparison.Ordinal) || fileName.EndsWith(".cjs", StringComparison.Ordinal))
        {
            return ModuleKind.CommonJS;
        }
        if (fileName.EndsWith(".mts", StringComparison.Ordinal) || fileName.EndsWith(".mjs", StringComparison.Ordinal))
        {
            return ModuleKind.ESNext;
        }
        return null;
    }

    // import("x") in type positions: the module reference
    private void CheckImportTypeModule(NodeRef node)
    {
        var store = StoreOf(node);
        var argument = store.Argument(node.Node);
        if (store.Kind(argument) == SyntaxKind.LiteralType && Utilities.IsStringLiteralLike(store, store.Literal(argument)))
        {
            ResolveExternalModuleName(node, At(node, store.Literal(argument)));
        }
    }
}
