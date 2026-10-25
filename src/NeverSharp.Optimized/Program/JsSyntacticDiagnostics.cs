// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/program.ts (getJSSyntacticDiagnosticsForFile) and src/compiler/factory/utilities.ts /
// utilitiesPublic.ts (canHaveIllegalDecorators, canHaveDecorators)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Modified: translated from TypeScript to C#; the walk's closures are instance methods of a per-file walker.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// program.ts getJSSyntacticDiagnosticsForFile: the TypeScript-only constructs of a JavaScript file (type annotations,
/// TypeScript-only declarations and modifiers, type parameters and arguments, non-null assertions, `as`/`satisfies`,
/// illegal decorators), reported as syntactic diagnostics.
/// </summary>
internal sealed class JsSyntacticDiagnostics
{
    private static readonly object Skip = "skip";

    private readonly NodeStore store;
    private readonly Node sourceFile;
    private readonly CompilerOptions options;
    private readonly List<Diagnostic> diagnostics = [];

    private JsSyntacticDiagnostics(SourceFile file, CompilerOptions options)
    {
        store = file.Store;
        sourceFile = file.Root;
        this.options = options;
    }

    internal static List<Diagnostic> GetForFile(SourceFile file, CompilerOptions options)
    {
        var walker = new JsSyntacticDiagnostics(file, options);
        walker.Walk(file.Root, file.Root);
        Parser.ForEachChildRecursively<object>(file.Store, file.Root, walker.Walk, walker.WalkArray);
        return walker.diagnostics;
    }

    // Return directly from the case if the given node doesnt want to visit each child
    // Otherwise break to visit each child
    private object? Walk(Node node, Node parent)
    {
        switch (store.Kind(parent))
        {
            case SyntaxKind.Parameter:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.MethodDeclaration:
                if (store.QuestionToken(parent) == node)
                {
                    diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.The_0_modifier_can_only_be_used_in_TypeScript_files, "?"));
                    return Skip;
                }
                goto case SyntaxKind.MethodSignature;
            case SyntaxKind.MethodSignature:
            case SyntaxKind.Constructor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.VariableDeclaration:
                // type annotation
                if (store.Type(parent) == node)
                {
                    diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.Type_annotations_can_only_be_used_in_TypeScript_files));
                    return Skip;
                }
                break;
        }

        switch (store.Kind(node))
        {
            case SyntaxKind.ImportClause:
                if (store.IsTypeOnly(node))
                {
                    diagnostics.Add(CreateDiagnosticForNode(parent, Diagnostics._0_declarations_can_only_be_used_in_TypeScript_files, "import type"));
                    return Skip;
                }
                break;
            case SyntaxKind.ExportDeclaration:
                if (store.IsTypeOnly(node))
                {
                    diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics._0_declarations_can_only_be_used_in_TypeScript_files, "export type"));
                    return Skip;
                }
                break;
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.ExportSpecifier:
                if (store.IsTypeOnly(node))
                {
                    diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics._0_declarations_can_only_be_used_in_TypeScript_files, FactoryNodeTests.IsImportSpecifier(store, node) ? "import...type" : "export...type"));
                    return Skip;
                }
                break;
            case SyntaxKind.ImportEqualsDeclaration:
                diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.import_can_only_be_used_in_TypeScript_files));
                return Skip;
            case SyntaxKind.ExportAssignment:
                if (store.IsExportEquals(node))
                {
                    diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.export_can_only_be_used_in_TypeScript_files));
                    return Skip;
                }
                break;
            case SyntaxKind.HeritageClause:
                if (store.Token(node) == SyntaxKind.ImplementsKeyword)
                {
                    diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.implements_clauses_can_only_be_used_in_TypeScript_files));
                    return Skip;
                }
                break;
            case SyntaxKind.InterfaceDeclaration:
                diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics._0_declarations_can_only_be_used_in_TypeScript_files, Scanner.TokenToString(SyntaxKind.InterfaceKeyword)));
                return Skip;
            case SyntaxKind.ModuleDeclaration:
                var moduleKeyword = (store.Flags(node) & NodeFlags.Namespace) != 0 ? Scanner.TokenToString(SyntaxKind.NamespaceKeyword) : Scanner.TokenToString(SyntaxKind.ModuleKeyword);
                diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics._0_declarations_can_only_be_used_in_TypeScript_files, moduleKeyword));
                return Skip;
            case SyntaxKind.TypeAliasDeclaration:
                diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.Type_aliases_can_only_be_used_in_TypeScript_files));
                return Skip;
            case SyntaxKind.Constructor:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.FunctionDeclaration:
                if (store.Body(node).IsNone)
                {
                    diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.Signature_declarations_can_only_be_used_in_TypeScript_files));
                    return Skip;
                }
                return null;
            case SyntaxKind.EnumDeclaration:
                diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics._0_declarations_can_only_be_used_in_TypeScript_files, Scanner.TokenToString(SyntaxKind.EnumKeyword)));
                return Skip;
            case SyntaxKind.NonNullExpression:
                diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.Non_null_assertions_can_only_be_used_in_TypeScript_files));
                return Skip;
            case SyntaxKind.AsExpression:
                diagnostics.Add(CreateDiagnosticForNode(store.Type(node), Diagnostics.Type_assertion_expressions_can_only_be_used_in_TypeScript_files));
                return Skip;
            case SyntaxKind.SatisfiesExpression:
                diagnostics.Add(CreateDiagnosticForNode(store.Type(node), Diagnostics.Type_satisfaction_expressions_can_only_be_used_in_TypeScript_files));
                return Skip;
            case SyntaxKind.TypeAssertionExpression:
                throw new InvalidOperationException("Debug Failure."); // Won't parse these in a JS file anyway, as they are interpreted as JSX.
        }
        return null;
    }

    private object? WalkArray(NodeList nodes, Node parent)
    {
        var modifiers = store.Modifiers(parent);
        if (CanHaveIllegalDecorators(store.Kind(parent)))
        {
            var decoratorIndex = FindIndex(modifiers, SyntaxKind.Decorator, 0);
            if (decoratorIndex >= 0)
            {
                // report illegal decorator
                diagnostics.Add(CreateDiagnosticForNode(store.Element(modifiers, decoratorIndex), Diagnostics.Decorators_are_not_valid_here));
            }
        }
        else if (CanHaveDecorators(store.Kind(parent)) && !modifiers.IsNone)
        {
            var decoratorIndex = FindIndex(modifiers, SyntaxKind.Decorator, 0);
            if (decoratorIndex >= 0)
            {
                if (store.Kind(parent) == SyntaxKind.Parameter && options.ExperimentalDecorators != true)
                {
                    // report illegall decorator on parameter
                    diagnostics.Add(CreateDiagnosticForNode(store.Element(modifiers, decoratorIndex), Diagnostics.Decorators_are_not_valid_here));
                }
                else if (store.Kind(parent) == SyntaxKind.ClassDeclaration)
                {
                    var exportIndex = FindIndex(modifiers, SyntaxKind.ExportKeyword, 0);
                    if (exportIndex >= 0)
                    {
                        var defaultIndex = FindIndex(modifiers, SyntaxKind.DefaultKeyword, 0);
                        if (decoratorIndex > exportIndex && defaultIndex >= 0 && decoratorIndex < defaultIndex)
                        {
                            // report illegal decorator between `export` and `default`
                            diagnostics.Add(CreateDiagnosticForNode(store.Element(modifiers, decoratorIndex), Diagnostics.Decorators_are_not_valid_here));
                        }
                        else if (exportIndex >= 0 && decoratorIndex < exportIndex)
                        {
                            var trailingDecoratorIndex = FindIndex(modifiers, SyntaxKind.Decorator, exportIndex);
                            if (trailingDecoratorIndex >= 0)
                            {
                                diagnostics.Add(Utilities.AddRelatedInfo(
                                    CreateDiagnosticForNode(store.Element(modifiers, trailingDecoratorIndex), Diagnostics.Decorators_may_not_appear_after_export_or_export_default_if_they_also_appear_before_export),
                                    CreateDiagnosticForNode(store.Element(modifiers, decoratorIndex), Diagnostics.Decorator_used_before_export_here)));
                            }
                        }
                    }
                }
            }
        }

        switch (store.Kind(parent))
        {
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.Constructor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.ArrowFunction:
                // Check type parameters
                if (nodes == store.TypeParameters(parent))
                {
                    diagnostics.Add(CreateDiagnosticForNodeArray(nodes, Diagnostics.Type_parameter_declarations_can_only_be_used_in_TypeScript_files));
                    return Skip;
                }
                goto case SyntaxKind.VariableStatement;
            case SyntaxKind.VariableStatement:
                // Check modifiers
                if (nodes == modifiers)
                {
                    CheckModifiers(modifiers, store.Kind(parent) == SyntaxKind.VariableStatement);
                    return Skip;
                }
                break;
            case SyntaxKind.PropertyDeclaration:
                // Check modifiers of property declaration
                if (nodes == modifiers)
                {
                    for (var i = 0; i < store.Length(nodes); i++)
                    {
                        var modifier = store.Element(nodes, i);
                        var kind = store.Kind(modifier);
                        if (UtilitiesPublic.IsModifierKind(kind) && kind != SyntaxKind.StaticKeyword && kind != SyntaxKind.AccessorKeyword)
                        {
                            diagnostics.Add(CreateDiagnosticForNode(modifier, Diagnostics.The_0_modifier_can_only_be_used_in_TypeScript_files, Scanner.TokenToString(kind)));
                        }
                    }
                    return Skip;
                }
                break;
            case SyntaxKind.Parameter:
                // Check modifiers of parameter declaration
                if (nodes == modifiers && SomeModifier(nodes))
                {
                    diagnostics.Add(CreateDiagnosticForNodeArray(nodes, Diagnostics.Parameter_modifiers_can_only_be_used_in_TypeScript_files));
                    return Skip;
                }
                break;
            case SyntaxKind.CallExpression:
            case SyntaxKind.NewExpression:
            case SyntaxKind.ExpressionWithTypeArguments:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxOpeningElement:
            case SyntaxKind.TaggedTemplateExpression:
                // Check type arguments
                if (nodes == store.TypeArguments(parent))
                {
                    diagnostics.Add(CreateDiagnosticForNodeArray(nodes, Diagnostics.Type_arguments_can_only_be_used_in_TypeScript_files));
                    return Skip;
                }
                break;
        }
        return null;
    }

    private void CheckModifiers(NodeList modifiers, bool isConstValid)
    {
        for (var i = 0; i < store.Length(modifiers); i++)
        {
            var modifier = store.Element(modifiers, i);
            var kind = store.Kind(modifier);
            switch (kind)
            {
                case SyntaxKind.ConstKeyword:
                    if (isConstValid)
                    {
                        continue;
                    }
                    // to report error,
                    goto case SyntaxKind.PublicKeyword;
                case SyntaxKind.PublicKeyword:
                case SyntaxKind.PrivateKeyword:
                case SyntaxKind.ProtectedKeyword:
                case SyntaxKind.ReadonlyKeyword:
                case SyntaxKind.DeclareKeyword:
                case SyntaxKind.AbstractKeyword:
                case SyntaxKind.OverrideKeyword:
                case SyntaxKind.InKeyword:
                case SyntaxKind.OutKeyword:
                    diagnostics.Add(CreateDiagnosticForNode(modifier, Diagnostics.The_0_modifier_can_only_be_used_in_TypeScript_files, Scanner.TokenToString(kind)));
                    break;

                    // These are all legal modifiers: static, export, default, accessor.
            }
        }
    }

    private bool SomeModifier(NodeList nodes)
    {
        for (var i = 0; i < store.Length(nodes); i++)
        {
            if (UtilitiesPublic.IsModifier(store, store.Element(nodes, i)))
            {
                return true;
            }
        }
        return false;
    }

    // findIndex(list, node => node.kind === kind, startIndex); -1 for an absent list.
    private int FindIndex(NodeList list, SyntaxKind kind, int startIndex)
    {
        if (list.IsNone)
        {
            return -1;
        }
        for (var i = startIndex; i < store.Length(list); i++)
        {
            if (store.Kind(store.Element(list, i)) == kind)
            {
                return i;
            }
        }
        return -1;
    }

    private DiagnosticWithLocation CreateDiagnosticForNodeArray(NodeList nodes, DiagnosticMessage message, params object?[] args)
    {
        var start = store.Pos(nodes);
        return Utilities.CreateFileDiagnostic(store, sourceFile, start, store.End(nodes) - start, message, args);
    }

    // Since these are syntactic diagnostics, parent might not have been set
    // this means the sourceFile cannot be infered from the node
    private DiagnosticWithLocation CreateDiagnosticForNode(Node node, DiagnosticMessage message, params object?[] args)
    {
        return Utilities.CreateDiagnosticForNodeInSourceFile(store, sourceFile, node, message, args);
    }

    // factory/utilities.ts canHaveIllegalDecorators
    private static bool CanHaveIllegalDecorators(SyntaxKind kind)
    {
        return kind is SyntaxKind.PropertyAssignment
            or SyntaxKind.ShorthandPropertyAssignment
            or SyntaxKind.FunctionDeclaration
            or SyntaxKind.Constructor
            or SyntaxKind.IndexSignature
            or SyntaxKind.ClassStaticBlockDeclaration
            or SyntaxKind.MissingDeclaration
            or SyntaxKind.VariableStatement
            or SyntaxKind.InterfaceDeclaration
            or SyntaxKind.TypeAliasDeclaration
            or SyntaxKind.EnumDeclaration
            or SyntaxKind.ModuleDeclaration
            or SyntaxKind.ImportEqualsDeclaration
            or SyntaxKind.ImportDeclaration
            or SyntaxKind.NamespaceExportDeclaration
            or SyntaxKind.ExportDeclaration
            or SyntaxKind.ExportAssignment;
    }

    // factory/utilitiesPublic.ts canHaveDecorators
    private static bool CanHaveDecorators(SyntaxKind kind)
    {
        return kind is SyntaxKind.Parameter
            or SyntaxKind.PropertyDeclaration
            or SyntaxKind.MethodDeclaration
            or SyntaxKind.GetAccessor
            or SyntaxKind.SetAccessor
            or SyntaxKind.ClassExpression
            or SyntaxKind.ClassDeclaration;
    }
}
