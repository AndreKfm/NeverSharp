// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (grammarErrorOnNode, grammarErrorOnFirstToken, grammarErrorAtPos,
// checkGrammarModifiers, reportObviousModifierErrors, findFirstModifierExcept, findFirstIllegalModifier,
// reportObviousDecoratorErrors, checkGrammarAsyncModifier, checkGrammarForDisallowedTrailingComma,
// checkGrammarTypeParameterList, checkGrammarParameterList, checkGrammarForUseStrictSimpleParameterList,
// checkGrammarFunctionLikeDeclaration, checkGrammarClassLikeDeclaration, checkGrammarArrowFunction,
// checkGrammarIndexSignatureParameters, checkGrammarIndexSignature, checkGrammarForAtLeastOneTypeArgument,
// checkGrammarTypeArguments, checkGrammarHeritageClause, checkGrammarExpressionWithTypeArguments,
// checkGrammarClassDeclarationHeritageClauses, checkGrammarInterfaceDeclaration, checkGrammarComputedPropertyName,
// checkGrammarForGenerator, checkGrammarForInvalidQuestionMark, checkGrammarForInvalidExclamationToken,
// checkGrammarForInOrForOfStatement, checkGrammarAccessor, checkGrammarTypeOperatorNode, checkGrammarForInvalidDynamicName,
// checkGrammarMethod, checkGrammarBreakOrContinueStatement, checkGrammarBindingElement, checkAmbientInitializer,
// checkGrammarVariableDeclaration, checkESModuleMarker, checkGrammarNameInLetOrConstDeclarations,
// checkGrammarVariableDeclarationList, checkGrammarForDisallowedBlockScopedVariableStatement,
// checkGrammarConstructorTypeParameters, checkGrammarConstructorTypeAnnotation, checkGrammarProperty,
// checkGrammarTopLevelElementForRequiredDeclareModifier, checkGrammarSourceFile, checkGrammarStatementInAmbientContext,
// checkGrammarNumericLiteral, checkGrammarImportClause, checkGrammarNamedImportsOrExports, checkGrammarExportDeclaration,
// checkGrammarModuleElementContext, checkModuleExportName, checkAwaitGrammar, checkYieldExpression's grammar)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/grammarchecks.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C8. JSDoc template tags as type parameter hosts are module C9's.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private readonly HashSet<NodeRef> reportedStatementInAmbientContext = [];

    // ---- reporting ----

    private bool HasParseDiagnostics(int file) => FileAt(file).ParseDiagnostics.Count > 0;

    // sourceFile.text
    private SourceChars FileTextOf(int file) => FileAt(file).Store.SourceFileText(FileAt(file).Root).Chars;

    // checker.ts grammarErrorOnNode: reported only when the file has no parse errors
    private bool GrammarErrorOnNode(NodeRef node, DiagnosticMessage message, params object?[] args)
    {
        if (!HasParseDiagnostics(node.File))
        {
            Error(node, message, args);
            return true;
        }
        return false;
    }

    // checker.ts grammarErrorOnFirstToken: reported only when the file has no parse errors
    private bool GrammarErrorOnFirstToken(NodeRef node, DiagnosticMessage message, params object?[] args)
    {
        var file = FileAt(node.File);
        if (file.ParseDiagnostics.Count == 0)
        {
            var span = Utilities.GetSpanOfTokenAtPosition(file.Store, file.Root, file.Store.Pos(node.Node));
            diagnostics.Add(CreateFileDiagnosticAt(node.File, span.Start, span.Length, message, args));
            return true;
        }
        return false;
    }

    // checker.ts grammarErrorAtPos
    private bool GrammarErrorAtPos(NodeRef nodeForSourceFile, int start, int length, DiagnosticMessage message, params object?[] args)
    {
        if (!HasParseDiagnostics(nodeForSourceFile.File))
        {
            diagnostics.Add(CreateFileDiagnosticAt(nodeForSourceFile.File, start, length, message, args));
            return true;
        }
        return false;
    }

    // utilities.ts createFileDiagnostic
    private ProgramDiagnostic CreateFileDiagnosticAt(int fileNumber, int start, int length, DiagnosticMessage message, params object?[] args)
    {
        var file = FileAt(fileNumber);
        return ProgramDiagnostic.FromFile(file, Utilities.CreateFileDiagnostic(file.Store, file.Root, start, length, message, args));
    }

    // createFileDiagnostic used as related information
    private ProgramRelatedInformation CreateRelatedForSpan(int fileNumber, int start, int length, DiagnosticMessage message, params object?[] args)
    {
        var d = CreateFileDiagnosticAt(fileNumber, start, length, message, args);
        return new ProgramRelatedInformation
        {
            SourceFile = d.SourceFile,
            File = d.File,
            StartOrUndefined = d.StartOrUndefined,
            LengthOrUndefined = d.LengthOrUndefined,
            Code = d.Code,
            Category = d.Category,
            MessageText = d.MessageText,
        };
    }

    // the binder's fallthrough flow node of a case or default clause
    private FlowRef FallthroughFlowNodeOf(NodeRef clause) => new(clause.File, BindingOf(clause.File).FallthroughFlowNodeOf(clause.Node));

    // ---- modifiers ----

    // factory/utilitiesPublic.ts canHaveDecorators
    private static bool CanHaveDecorators(NodeStore store, Node node) =>
        store.Kind(node) is SyntaxKind.Parameter or SyntaxKind.PropertyDeclaration or SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor
            or SyntaxKind.SetAccessor or SyntaxKind.ClassExpression or SyntaxKind.ClassDeclaration;

    // factory/utilities.ts canHaveIllegalDecorators
    private static bool CanHaveIllegalDecorators(NodeStore store, Node node) =>
        store.Kind(node) is SyntaxKind.PropertyAssignment or SyntaxKind.ShorthandPropertyAssignment or SyntaxKind.FunctionDeclaration or SyntaxKind.Constructor
            or SyntaxKind.IndexSignature or SyntaxKind.ClassStaticBlockDeclaration or SyntaxKind.MissingDeclaration or SyntaxKind.VariableStatement
            or SyntaxKind.InterfaceDeclaration or SyntaxKind.TypeAliasDeclaration or SyntaxKind.EnumDeclaration or SyntaxKind.ModuleDeclaration
            or SyntaxKind.ImportEqualsDeclaration or SyntaxKind.ImportDeclaration or SyntaxKind.NamespaceExportDeclaration or SyntaxKind.ExportDeclaration
            or SyntaxKind.ExportAssignment;

    private static bool IsAccessorKind(NodeStore store, Node node) => store.Kind(node) is SyntaxKind.GetAccessor or SyntaxKind.SetAccessor;

    /// <summary>checker.ts checkGrammarModifiers: true when an error was reported.</summary>
    private bool CheckGrammarModifiers(NodeRef nodeRef)
    {
        var store = StoreOf(nodeRef);
        var node = nodeRef.Node;
        var quickResult = ReportObviousDecoratorErrors(nodeRef) ?? ReportObviousModifierErrors(nodeRef);
        if (quickResult is not null)
        {
            return quickResult.Value;
        }
        var kind = store.Kind(node);
        var parent = store.Parent(node);
        if (kind == SyntaxKind.Parameter && Utilities.IsThisIdentifier(store, store.Name(node)))
        {
            return GrammarErrorOnFirstToken(nodeRef, Diagnostics.Neither_decorators_nor_modifiers_may_be_applied_to_this_parameters);
        }
        var blockScopeKind = kind == SyntaxKind.VariableStatement ? store.Flags(store.DeclarationList(node)) & NodeFlags.BlockScoped : NodeFlags.None;
        Node lastStatic = Node.None, lastDeclare = Node.None, lastAsync = Node.None, lastOverride = Node.None, firstDecorator = Node.None;
        var flags = ModifierFlags.None;
        var sawExportBeforeDecorators = false;
        // We parse decorators and modifiers in four contiguous chunks:
        // [...leadingDecorators, ...leadingModifiers, ...trailingDecorators, ...trailingModifiers]. It is an error to
        // have both leading and trailing decorators.
        var hasLeadingDecorators = false;
        foreach (var modifierNode in store.Nodes(store.Modifiers(node)))
        {
            var modifier = At(nodeRef, modifierNode);
            var modifierKind = store.Kind(modifierNode);
            if (modifierKind == SyntaxKind.Decorator)
            {
                if (!NodeCanBeDecorated(compilerOptions.ExperimentalDecorators == true, nodeRef))
                {
                    if (kind == SyntaxKind.MethodDeclaration && Utilities.NodeIsMissing(store, store.Body(node)))
                    {
                        return GrammarErrorOnFirstToken(nodeRef, Diagnostics.A_decorator_can_only_decorate_a_method_implementation_not_an_overload);
                    }
                    return GrammarErrorOnFirstToken(nodeRef, Diagnostics.Decorators_are_not_valid_here);
                }
                if (compilerOptions.ExperimentalDecorators == true && kind is SyntaxKind.GetAccessor or SyntaxKind.SetAccessor)
                {
                    // getAllAccessorDeclarations: the first accessor has decorators and this is the second
                    var symbol = GetSymbolOfDeclaration(nodeRef);
                    var accessors = new List<NodeRef>();
                    foreach (var d in DeclarationsOf(symbol) ?? [])
                    {
                        if (Kind(d) is SyntaxKind.GetAccessor or SyntaxKind.SetAccessor && Utilities.IsStatic(StoreOf(d), d.Node) == Utilities.IsStatic(store, node))
                        {
                            accessors.Add(d);
                        }
                    }
                    if (accessors.Count >= 2 && accessors[1] == nodeRef && HasDecorators(accessors[0]))
                    {
                        return GrammarErrorOnFirstToken(nodeRef, Diagnostics.Decorators_cannot_be_applied_to_multiple_get_Slashset_accessors_of_the_same_name);
                    }
                }
                // if we've seen any modifiers aside from `export`, `default`, or another decorator, then this is an invalid position
                if ((flags & ~(ModifierFlags.ExportDefault | ModifierFlags.Decorator)) != 0)
                {
                    return GrammarErrorOnNode(modifier, Diagnostics.Decorators_are_not_valid_here);
                }
                // if we've already seen leading decorators and leading modifiers, then trailing decorators are an invalid position
                if (hasLeadingDecorators && (flags & ModifierFlags.Modifier) != 0)
                {
                    if (!HasParseDiagnostics(nodeRef.File))
                    {
                        AddRelatedInfo(Error(modifier, Diagnostics.Decorators_may_not_appear_after_export_or_export_default_if_they_also_appear_before_export),
                            CreateRelatedForNode(At(nodeRef, firstDecorator), Diagnostics.Decorator_used_before_export_here));
                        return true;
                    }
                    return false;
                }
                flags |= ModifierFlags.Decorator;
                // if we have not yet seen a modifier, then these are leading decorators
                if ((flags & ModifierFlags.Modifier) == 0)
                {
                    hasLeadingDecorators = true;
                }
                else if ((flags & ModifierFlags.Export) != 0)
                {
                    sawExportBeforeDecorators = true;
                }
                if (firstDecorator.IsNone)
                {
                    firstDecorator = modifierNode;
                }
                continue;
            }
            var tokenText = Scanner.TokenToString(modifierKind);
            if (modifierKind != SyntaxKind.ReadonlyKeyword)
            {
                if (kind is SyntaxKind.PropertySignature or SyntaxKind.MethodSignature)
                {
                    return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_type_member, tokenText);
                }
                if (kind == SyntaxKind.IndexSignature && (modifierKind != SyntaxKind.StaticKeyword || !Utilities.IsClassLike(store, parent)))
                {
                    return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_an_index_signature, tokenText);
                }
            }
            if (modifierKind is not (SyntaxKind.InKeyword or SyntaxKind.OutKeyword or SyntaxKind.ConstKeyword))
            {
                if (kind == SyntaxKind.TypeParameter)
                {
                    return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_type_parameter, tokenText);
                }
            }
            switch (modifierKind)
            {
                case SyntaxKind.ConstKeyword:
                {
                    if (kind is not (SyntaxKind.EnumDeclaration or SyntaxKind.TypeParameter))
                    {
                        return GrammarErrorOnNode(nodeRef, Diagnostics.A_class_member_cannot_have_the_0_keyword, Scanner.TokenToString(SyntaxKind.ConstKeyword));
                    }
                    // (JSDoc template tags: TODO(C9))
                    if (kind == SyntaxKind.TypeParameter && !(Utilities.IsFunctionLikeDeclaration(store, parent) || Utilities.IsClassLike(store, parent) ||
                        store.Kind(parent) is SyntaxKind.FunctionType or SyntaxKind.ConstructorType or SyntaxKind.CallSignature or SyntaxKind.ConstructSignature or SyntaxKind.MethodSignature))
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_can_only_appear_on_a_type_parameter_of_a_function_method_or_class, tokenText);
                    }
                    break;
                }
                case SyntaxKind.OverrideKeyword:
                    // If node.kind === SyntaxKind.Parameter, checkParameter reports an error if it's not a parameter property.
                    if ((flags & ModifierFlags.Override) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "override");
                    }
                    else if ((flags & ModifierFlags.Ambient) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "override", "declare");
                    }
                    else if ((flags & ModifierFlags.Readonly) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "override", "readonly");
                    }
                    else if ((flags & ModifierFlags.Accessor) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "override", "accessor");
                    }
                    else if ((flags & ModifierFlags.Async) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "override", "async");
                    }
                    flags |= ModifierFlags.Override;
                    lastOverride = modifierNode;
                    break;
                case SyntaxKind.PublicKeyword:
                case SyntaxKind.ProtectedKeyword:
                case SyntaxKind.PrivateKeyword:
                {
                    var text = modifierKind switch
                    {
                        SyntaxKind.PublicKeyword => "public",
                        SyntaxKind.ProtectedKeyword => "protected",
                        _ => "private",
                    };
                    if ((flags & ModifierFlags.AccessibilityModifier) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics.Accessibility_modifier_already_seen);
                    }
                    else if ((flags & ModifierFlags.Override) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, text, "override");
                    }
                    else if ((flags & ModifierFlags.Static) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, text, "static");
                    }
                    else if ((flags & ModifierFlags.Accessor) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, text, "accessor");
                    }
                    else if ((flags & ModifierFlags.Readonly) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, text, "readonly");
                    }
                    else if ((flags & ModifierFlags.Async) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, text, "async");
                    }
                    else if (store.Kind(parent) is SyntaxKind.ModuleBlock or SyntaxKind.SourceFile)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_module_or_namespace_element, text);
                    }
                    else if ((flags & ModifierFlags.Abstract) != 0)
                    {
                        if (modifierKind == SyntaxKind.PrivateKeyword)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, text, "abstract");
                        }
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, text, "abstract");
                    }
                    else if (IsPrivateIdentifierClassElementDeclaration(store, node))
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics.An_accessibility_modifier_cannot_be_used_with_a_private_identifier);
                    }
                    flags |= Utilities.ModifierToFlag(modifierKind);
                    break;
                }
                case SyntaxKind.StaticKeyword:
                    if ((flags & ModifierFlags.Static) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "static");
                    }
                    else if ((flags & ModifierFlags.Readonly) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "static", "readonly");
                    }
                    else if ((flags & ModifierFlags.Async) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "static", "async");
                    }
                    else if ((flags & ModifierFlags.Accessor) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "static", "accessor");
                    }
                    else if (store.Kind(parent) is SyntaxKind.ModuleBlock or SyntaxKind.SourceFile)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_module_or_namespace_element, "static");
                    }
                    else if (kind == SyntaxKind.Parameter)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_parameter, "static");
                    }
                    else if ((flags & ModifierFlags.Abstract) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "static", "abstract");
                    }
                    else if ((flags & ModifierFlags.Override) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "static", "override");
                    }
                    flags |= ModifierFlags.Static;
                    lastStatic = modifierNode;
                    break;
                case SyntaxKind.AccessorKeyword:
                    if ((flags & ModifierFlags.Accessor) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "accessor");
                    }
                    else if ((flags & ModifierFlags.Readonly) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "accessor", "readonly");
                    }
                    else if ((flags & ModifierFlags.Ambient) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "accessor", "declare");
                    }
                    else if (kind != SyntaxKind.PropertyDeclaration)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics.accessor_modifier_can_only_appear_on_a_property_declaration);
                    }
                    flags |= ModifierFlags.Accessor;
                    break;
                case SyntaxKind.ReadonlyKeyword:
                    if ((flags & ModifierFlags.Readonly) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "readonly");
                    }
                    else if (kind is not (SyntaxKind.PropertyDeclaration or SyntaxKind.PropertySignature or SyntaxKind.IndexSignature or SyntaxKind.Parameter))
                    {
                        // If node.kind === SyntaxKind.Parameter, checkParameter reports an error if it's not a parameter property.
                        return GrammarErrorOnNode(modifier, Diagnostics.readonly_modifier_can_only_appear_on_a_property_declaration_or_index_signature);
                    }
                    else if ((flags & ModifierFlags.Accessor) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "readonly", "accessor");
                    }
                    flags |= ModifierFlags.Readonly;
                    break;
                case SyntaxKind.ExportKeyword:
                    // (verbatimModuleSyntax: Gate C does not set it)
                    if ((flags & ModifierFlags.Export) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "export");
                    }
                    else if ((flags & ModifierFlags.Ambient) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "export", "declare");
                    }
                    else if ((flags & ModifierFlags.Abstract) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "export", "abstract");
                    }
                    else if ((flags & ModifierFlags.Async) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "export", "async");
                    }
                    else if (Utilities.IsClassLike(store, parent))
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_class_elements_of_this_kind, "export");
                    }
                    else if (kind == SyntaxKind.Parameter)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_parameter, "export");
                    }
                    else if (blockScopeKind == NodeFlags.Using)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_using_declaration, "export");
                    }
                    else if (blockScopeKind == NodeFlags.AwaitUsing)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_an_await_using_declaration, "export");
                    }
                    flags |= ModifierFlags.Export;
                    break;
                case SyntaxKind.DefaultKeyword:
                {
                    var container = store.Kind(parent) == SyntaxKind.SourceFile ? parent : store.Parent(parent);
                    if (store.Kind(container) == SyntaxKind.ModuleDeclaration && !Utilities.IsAmbientModule(store, container))
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics.A_default_export_can_only_be_used_in_an_ECMAScript_style_module);
                    }
                    else if (blockScopeKind == NodeFlags.Using)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_using_declaration, "default");
                    }
                    else if (blockScopeKind == NodeFlags.AwaitUsing)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_an_await_using_declaration, "default");
                    }
                    else if ((flags & ModifierFlags.Export) == 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "export", "default");
                    }
                    else if (sawExportBeforeDecorators)
                    {
                        return GrammarErrorOnNode(At(nodeRef, firstDecorator), Diagnostics.Decorators_are_not_valid_here);
                    }
                    flags |= ModifierFlags.Default;
                    break;
                }
                case SyntaxKind.DeclareKeyword:
                    if ((flags & ModifierFlags.Ambient) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "declare");
                    }
                    else if ((flags & ModifierFlags.Async) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_in_an_ambient_context, "async");
                    }
                    else if ((flags & ModifierFlags.Override) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_in_an_ambient_context, "override");
                    }
                    else if (Utilities.IsClassLike(store, parent) && kind != SyntaxKind.PropertyDeclaration)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_class_elements_of_this_kind, "declare");
                    }
                    else if (kind == SyntaxKind.Parameter)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_parameter, "declare");
                    }
                    else if (blockScopeKind == NodeFlags.Using)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_using_declaration, "declare");
                    }
                    else if (blockScopeKind == NodeFlags.AwaitUsing)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_an_await_using_declaration, "declare");
                    }
                    else if ((store.Flags(parent) & NodeFlags.Ambient) != 0 && store.Kind(parent) == SyntaxKind.ModuleBlock)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics.A_declare_modifier_cannot_be_used_in_an_already_ambient_context);
                    }
                    else if (IsPrivateIdentifierClassElementDeclaration(store, node))
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_a_private_identifier, "declare");
                    }
                    else if ((flags & ModifierFlags.Accessor) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "declare", "accessor");
                    }
                    flags |= ModifierFlags.Ambient;
                    lastDeclare = modifierNode;
                    break;
                case SyntaxKind.AbstractKeyword:
                    if ((flags & ModifierFlags.Abstract) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "abstract");
                    }
                    if (kind is not (SyntaxKind.ClassDeclaration or SyntaxKind.ConstructorType))
                    {
                        if (kind is not (SyntaxKind.MethodDeclaration or SyntaxKind.PropertyDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor))
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics.abstract_modifier_can_only_appear_on_a_class_method_or_property_declaration);
                        }
                        if (!(store.Kind(parent) == SyntaxKind.ClassDeclaration && Utilities.HasSyntacticModifierUncached(store, parent, ModifierFlags.Abstract)))
                        {
                            var message = kind == SyntaxKind.PropertyDeclaration
                                ? Diagnostics.Abstract_properties_can_only_appear_within_an_abstract_class
                                : Diagnostics.Abstract_methods_can_only_appear_within_an_abstract_class;
                            return GrammarErrorOnNode(modifier, message);
                        }
                        if ((flags & ModifierFlags.Static) != 0)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "static", "abstract");
                        }
                        if ((flags & ModifierFlags.Private) != 0)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "private", "abstract");
                        }
                        if ((flags & ModifierFlags.Async) != 0 && !lastAsync.IsNone)
                        {
                            return GrammarErrorOnNode(At(nodeRef, lastAsync), Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "async", "abstract");
                        }
                        if ((flags & ModifierFlags.Override) != 0)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "abstract", "override");
                        }
                        if ((flags & ModifierFlags.Accessor) != 0)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "abstract", "accessor");
                        }
                    }
                    if (!store.Name(node).IsNone && store.Kind(store.Name(node)) == SyntaxKind.PrivateIdentifier)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_a_private_identifier, "abstract");
                    }
                    flags |= ModifierFlags.Abstract;
                    break;
                case SyntaxKind.AsyncKeyword:
                    if ((flags & ModifierFlags.Async) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "async");
                    }
                    else if ((flags & ModifierFlags.Ambient) != 0 || (store.Flags(parent) & NodeFlags.Ambient) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_in_an_ambient_context, "async");
                    }
                    else if (kind == SyntaxKind.Parameter)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_parameter, "async");
                    }
                    if ((flags & ModifierFlags.Abstract) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "async", "abstract");
                    }
                    flags |= ModifierFlags.Async;
                    lastAsync = modifierNode;
                    break;
                case SyntaxKind.InKeyword:
                case SyntaxKind.OutKeyword:
                {
                    var inOutFlag = modifierKind == SyntaxKind.InKeyword ? ModifierFlags.In : ModifierFlags.Out;
                    var inOutText = modifierKind == SyntaxKind.InKeyword ? "in" : "out";
                    // (JSDoc template tags: TODO(C9))
                    if (kind != SyntaxKind.TypeParameter || !parent.IsNone && !(store.Kind(parent) is SyntaxKind.InterfaceDeclaration or SyntaxKind.TypeAliasDeclaration || Utilities.IsClassLike(store, parent)))
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_can_only_appear_on_a_type_parameter_of_a_class_interface_or_type_alias, inOutText);
                    }
                    if ((flags & inOutFlag) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, inOutText);
                    }
                    if ((inOutFlag & ModifierFlags.In) != 0 && (flags & ModifierFlags.Out) != 0)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "in", "out");
                    }
                    flags |= inOutFlag;
                    break;
                }
            }
        }
        if (kind == SyntaxKind.Constructor)
        {
            if ((flags & ModifierFlags.Static) != 0)
            {
                return GrammarErrorOnNode(At(nodeRef, lastStatic), Diagnostics._0_modifier_cannot_appear_on_a_constructor_declaration, "static");
            }
            if ((flags & ModifierFlags.Override) != 0)
            {
                return GrammarErrorOnNode(At(nodeRef, lastOverride), Diagnostics._0_modifier_cannot_appear_on_a_constructor_declaration, "override");
            }
            if ((flags & ModifierFlags.Async) != 0)
            {
                return GrammarErrorOnNode(At(nodeRef, lastAsync), Diagnostics._0_modifier_cannot_appear_on_a_constructor_declaration, "async");
            }
            return false;
        }
        else if (kind is SyntaxKind.ImportDeclaration or SyntaxKind.ImportEqualsDeclaration && (flags & ModifierFlags.Ambient) != 0)
        {
            return GrammarErrorOnNode(At(nodeRef, lastDeclare), Diagnostics.A_0_modifier_cannot_be_used_with_an_import_declaration, "declare");
        }
        else if (kind == SyntaxKind.Parameter && (flags & ModifierFlags.ParameterPropertyModifier) != 0 && Utilities.IsBindingPattern(store, store.Name(node)))
        {
            return GrammarErrorOnNode(nodeRef, Diagnostics.A_parameter_property_may_not_be_declared_using_a_binding_pattern);
        }
        else if (kind == SyntaxKind.Parameter && (flags & ModifierFlags.ParameterPropertyModifier) != 0 && !store.DotDotDotToken(node).IsNone)
        {
            return GrammarErrorOnNode(nodeRef, Diagnostics.A_parameter_property_cannot_be_declared_using_a_rest_parameter);
        }
        if ((flags & ModifierFlags.Async) != 0)
        {
            // checkGrammarAsyncModifier
            if (kind is SyntaxKind.MethodDeclaration or SyntaxKind.FunctionDeclaration or SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction)
            {
                return false;
            }
            return GrammarErrorOnNode(At(nodeRef, lastAsync), Diagnostics._0_modifier_cannot_be_used_here, "async");
        }
        return false;
    }

    private bool HasDecorators(NodeRef node)
    {
        var store = StoreOf(node);
        var modifiers = store.Modifiers(node.Node);
        if (modifiers.IsNone)
        {
            return false;
        }
        foreach (var modifier in store.Nodes(modifiers))
        {
            if (store.Kind(modifier) == SyntaxKind.Decorator)
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts reportObviousModifierErrors: true | false: early return this value from checkGrammarModifiers; null: need
    // to do full checking on the modifiers
    private bool? ReportObviousModifierErrors(NodeRef node)
    {
        var store = StoreOf(node);
        if (store.Modifiers(node.Node).IsNone)
        {
            return false;
        }
        var modifier = FindFirstIllegalModifier(store, node.Node);
        return modifier.IsNone ? null : GrammarErrorOnFirstToken(At(node, modifier), Diagnostics.Modifiers_cannot_appear_here);
    }

    // checker.ts findFirstModifierExcept
    private static Node FindFirstModifierExcept(NodeStore store, Node node, SyntaxKind allowedModifier)
    {
        var modifier = FindFirstModifier(store, node);
        return !modifier.IsNone && store.Kind(modifier) != allowedModifier ? modifier : Node.None;
    }

    // find(node.modifiers, isModifier)
    private static Node FindFirstModifier(NodeStore store, Node node)
    {
        var modifiers = store.Modifiers(node);
        if (modifiers.IsNone)
        {
            return Node.None;
        }
        foreach (var modifier in store.Nodes(modifiers))
        {
            if (UtilitiesPublic.IsModifier(store, modifier))
            {
                return modifier;
            }
        }
        return Node.None;
    }

    // checker.ts findFirstIllegalModifier
    private static Node FindFirstIllegalModifier(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.Constructor:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.ExportDeclaration:
            case SyntaxKind.ExportAssignment:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.Parameter:
            case SyntaxKind.TypeParameter:
                return Node.None;
            case SyntaxKind.ClassStaticBlockDeclaration:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.ShorthandPropertyAssignment:
            case SyntaxKind.NamespaceExportDeclaration:
            case SyntaxKind.MissingDeclaration:
                return FindFirstModifier(store, node);
            default:
                if (store.Kind(store.Parent(node)) is SyntaxKind.ModuleBlock or SyntaxKind.SourceFile)
                {
                    return Node.None;
                }
                switch (store.Kind(node))
                {
                    case SyntaxKind.FunctionDeclaration:
                        return FindFirstModifierExcept(store, node, SyntaxKind.AsyncKeyword);
                    case SyntaxKind.ClassDeclaration:
                    case SyntaxKind.ConstructorType:
                        return FindFirstModifierExcept(store, node, SyntaxKind.AbstractKeyword);
                    case SyntaxKind.ClassExpression:
                    case SyntaxKind.InterfaceDeclaration:
                    case SyntaxKind.TypeAliasDeclaration:
                        return FindFirstModifier(store, node);
                    case SyntaxKind.VariableStatement:
                        return (store.Flags(store.DeclarationList(node)) & NodeFlags.Using) != 0
                            ? FindFirstModifierExcept(store, node, SyntaxKind.AwaitKeyword)
                            : FindFirstModifier(store, node);
                    case SyntaxKind.EnumDeclaration:
                        return FindFirstModifierExcept(store, node, SyntaxKind.ConstKeyword);
                    default:
                        return Node.None;
                }
        }
    }

    // checker.ts reportObviousDecoratorErrors / findFirstIllegalDecorator
    private bool? ReportObviousDecoratorErrors(NodeRef node)
    {
        var store = StoreOf(node);
        if (!CanHaveIllegalDecorators(store, node.Node) || store.Modifiers(node.Node).IsNone)
        {
            return null;
        }
        foreach (var modifier in store.Nodes(store.Modifiers(node.Node)))
        {
            if (store.Kind(modifier) == SyntaxKind.Decorator)
            {
                return GrammarErrorOnFirstToken(At(node, modifier), Diagnostics.Decorators_are_not_valid_here);
            }
        }
        return null;
    }

    // ---- parameter lists and signatures ----

    // checker.ts checkGrammarForDisallowedTrailingComma
    private bool CheckGrammarForDisallowedTrailingComma(NodeRef context, NodeList list, DiagnosticMessage? diag = null)
    {
        var store = StoreOf(context);
        if (!list.IsNone && store.HasTrailingComma(list))
        {
            return GrammarErrorAtPos(context, store.End(list) - 1, 1, diag ?? Diagnostics.Trailing_comma_not_allowed);
        }
        return false;
    }

    // checker.ts checkGrammarTypeParameterList
    private bool CheckGrammarTypeParameterList(NodeRef context, NodeList typeParameters)
    {
        var store = StoreOf(context);
        if (!typeParameters.IsNone && store.Length(typeParameters) == 0)
        {
            var file = FileAt(context.File);
            var start = store.Pos(typeParameters) - 1;
            var end = Scanner.SkipTrivia(FileTextOf(context.File), store.End(typeParameters)) + 1;
            return GrammarErrorAtPos(context, start, end - start, Diagnostics.Type_parameter_list_cannot_be_empty);
        }
        return false;
    }

    // checker.ts checkGrammarParameterList
    private bool CheckGrammarParameterList(NodeRef context, NodeList parameters)
    {
        var store = StoreOf(context);
        var seenOptionalParameter = false;
        var nodes = store.Nodes(parameters);
        var parameterCount = nodes.Length;
        for (var i = 0; i < parameterCount; i++)
        {
            var parameter = nodes[i];
            if (!store.DotDotDotToken(parameter).IsNone)
            {
                if (i != parameterCount - 1)
                {
                    return GrammarErrorOnNode(At(context, store.DotDotDotToken(parameter)), Diagnostics.A_rest_parameter_must_be_last_in_a_parameter_list);
                }
                if ((store.Flags(parameter) & NodeFlags.Ambient) == 0)
                {
                    // Allow `...foo,` in ambient declarations; see GH#23070
                    CheckGrammarForDisallowedTrailingComma(context, parameters, Diagnostics.A_rest_parameter_or_binding_pattern_may_not_have_a_trailing_comma);
                }
                if (!store.QuestionToken(parameter).IsNone)
                {
                    return GrammarErrorOnNode(At(context, store.QuestionToken(parameter)), Diagnostics.A_rest_parameter_cannot_be_optional);
                }
                if (!store.Initializer(parameter).IsNone)
                {
                    return GrammarErrorOnNode(At(context, store.Name(parameter)), Diagnostics.A_rest_parameter_cannot_have_an_initializer);
                }
            }
            else if (!store.QuestionToken(parameter).IsNone) // (hasEffectiveQuestionToken: JSDoc optionality in JavaScript, module C9)
            {
                seenOptionalParameter = true;
                if (!store.Initializer(parameter).IsNone)
                {
                    return GrammarErrorOnNode(At(context, store.Name(parameter)), Diagnostics.Parameter_cannot_have_question_mark_and_initializer);
                }
            }
            else if (seenOptionalParameter && store.Initializer(parameter).IsNone)
            {
                return GrammarErrorOnNode(At(context, store.Name(parameter)), Diagnostics.A_required_parameter_cannot_follow_an_optional_parameter);
            }
        }
        return false;
    }

    // checker.ts checkGrammarForUseStrictSimpleParameterList
    private bool CheckGrammarForUseStrictSimpleParameterList(NodeRef node)
    {
        var store = StoreOf(node);
        var body = store.Body(node.Node);
        if (languageVersion < ScriptTarget.ES2016 || body.IsNone || store.Kind(body) != SyntaxKind.Block)
        {
            return false;
        }
        var useStrictDirective = Node.None;
        foreach (var statement in store.Nodes(store.Statements(body)))
        {
            // findUseStrictPrologue
            if (!Utilities.IsPrologueDirective(store, statement))
            {
                break;
            }
            // isUseStrictPrologueDirective: the expression's source text is "use strict" (quoted, without escapes)
            var expressionText = GetTextOfNode(At(node, store.Expression(statement)));
            if (expressionText is "\"use strict\"" or "'use strict'")
            {
                useStrictDirective = statement;
                break;
            }
        }
        if (useStrictDirective.IsNone)
        {
            return false;
        }
        var nonSimpleParameters = new List<Node>();
        foreach (var p in store.Nodes(store.Parameters(node.Node)))
        {
            if (!store.Initializer(p).IsNone || Utilities.IsBindingPattern(store, store.Name(p)) || !store.DotDotDotToken(p).IsNone)
            {
                nonSimpleParameters.Add(p);
            }
        }
        if (nonSimpleParameters.Count == 0)
        {
            return false;
        }
        foreach (var parameter in nonSimpleParameters)
        {
            AddRelatedInfo(Error(At(node, parameter), Diagnostics.This_parameter_is_not_allowed_with_use_strict_directive),
                CreateRelatedForNode(At(node, useStrictDirective), Diagnostics.use_strict_directive_used_here));
        }
        var related = new ProgramRelatedInformation[nonSimpleParameters.Count];
        for (var index = 0; index < related.Length; index++)
        {
            related[index] = CreateRelatedForNode(At(node, nonSimpleParameters[index]), index == 0 ? Diagnostics.Non_simple_parameter_declared_here : Diagnostics.and_here);
        }
        AddRelatedInfo(Error(At(node, useStrictDirective), Diagnostics.use_strict_directive_cannot_be_used_with_non_simple_parameter_list), related);
        return true;
    }

    // checker.ts checkGrammarFunctionLikeDeclaration
    private bool CheckGrammarFunctionLikeDeclaration(NodeRef node)
    {
        // Prevent cascading error by short-circuit
        var store = StoreOf(node);
        return CheckGrammarModifiers(node) ||
            CheckGrammarTypeParameterList(node, store.TypeParameters(node.Node)) ||
            CheckGrammarParameterList(node, store.Parameters(node.Node)) ||
            CheckGrammarArrowFunction(node) ||
            (Utilities.IsFunctionLikeDeclaration(store, node.Node) && CheckGrammarForUseStrictSimpleParameterList(node));
    }

    // checker.ts checkGrammarClassLikeDeclaration
    private bool CheckGrammarClassLikeDeclaration(NodeRef node) =>
        CheckGrammarClassDeclarationHeritageClauses(node) || CheckGrammarTypeParameterList(node, StoreOf(node).TypeParameters(node.Node));

    // checker.ts checkGrammarArrowFunction
    private bool CheckGrammarArrowFunction(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (store.Kind(n) != SyntaxKind.ArrowFunction)
        {
            return false;
        }
        var typeParameters = store.TypeParameters(n);
        if (!typeParameters.IsNone && store.Length(typeParameters) > 0 &&
            !(store.Length(typeParameters) > 1 || store.HasTrailingComma(typeParameters) || !store.Constraint(store.Nodes(typeParameters)[0]).IsNone))
        {
            var fileName = FileNameOf(node.File);
            if (fileName.EndsWith(".mts", StringComparison.Ordinal) || fileName.EndsWith(".cts", StringComparison.Ordinal))
            {
                GrammarErrorOnNode(At(node, store.Nodes(typeParameters)[0]), Diagnostics.This_syntax_is_reserved_in_files_with_the_mts_or_cts_extension_Add_a_trailing_comma_or_explicit_constraint);
            }
        }
        var equalsGreaterThanToken = store.EqualsGreaterThanToken(n);
        var fileLike = new SourceFileNodeLike(store, FileAt(node.File).Root);
        var startLine = Scanner.GetLineAndCharacterOfPosition(fileLike, store.Pos(equalsGreaterThanToken)).Line;
        var endLine = Scanner.GetLineAndCharacterOfPosition(fileLike, store.End(equalsGreaterThanToken)).Line;
        return startLine != endLine && GrammarErrorOnNode(At(node, equalsGreaterThanToken), Diagnostics.Line_terminator_not_permitted_before_arrow);
    }

    // checker.ts checkGrammarIndexSignature / checkGrammarIndexSignatureParameters
    private bool CheckGrammarIndexSignature(NodeRef node)
    {
        // Prevent cascading error by short-circuit
        if (CheckGrammarModifiers(node))
        {
            return true;
        }
        var store = StoreOf(node);
        var n = node.Node;
        var parameters = store.Nodes(store.Parameters(n));
        if (parameters.Length != 1)
        {
            if (parameters.Length > 0)
            {
                return GrammarErrorOnNode(At(node, store.Name(parameters[0])), Diagnostics.An_index_signature_must_have_exactly_one_parameter);
            }
            return GrammarErrorOnNode(node, Diagnostics.An_index_signature_must_have_exactly_one_parameter);
        }
        var parameter = parameters[0];
        CheckGrammarForDisallowedTrailingComma(node, store.Parameters(n), Diagnostics.An_index_signature_cannot_have_a_trailing_comma);
        if (!store.DotDotDotToken(parameter).IsNone)
        {
            return GrammarErrorOnNode(At(node, store.DotDotDotToken(parameter)), Diagnostics.An_index_signature_cannot_have_a_rest_parameter);
        }
        if (Utilities.GetSyntacticModifierFlagsNoCache(store, parameter) != 0)
        {
            return GrammarErrorOnNode(At(node, store.Name(parameter)), Diagnostics.An_index_signature_parameter_cannot_have_an_accessibility_modifier);
        }
        if (!store.QuestionToken(parameter).IsNone)
        {
            return GrammarErrorOnNode(At(node, store.QuestionToken(parameter)), Diagnostics.An_index_signature_parameter_cannot_have_a_question_mark);
        }
        if (!store.Initializer(parameter).IsNone)
        {
            return GrammarErrorOnNode(At(node, store.Name(parameter)), Diagnostics.An_index_signature_parameter_cannot_have_an_initializer);
        }
        if (store.Type(parameter).IsNone)
        {
            return GrammarErrorOnNode(At(node, store.Name(parameter)), Diagnostics.An_index_signature_parameter_must_have_a_type_annotation);
        }
        var type = GetTypeFromTypeNode(At(node, store.Type(parameter)));
        if (SomeType(type, t => (Types.Flags(t) & TypeFlags.StringOrNumberLiteralOrUnique) != 0) || IsGenericType(type))
        {
            return GrammarErrorOnNode(At(node, store.Name(parameter)), Diagnostics.An_index_signature_parameter_type_cannot_be_a_literal_type_or_generic_type_Consider_using_a_mapped_object_type_instead);
        }
        if (!EveryType(type, IsValidIndexKeyType))
        {
            return GrammarErrorOnNode(At(node, store.Name(parameter)), Diagnostics.An_index_signature_parameter_type_must_be_string_number_symbol_or_a_template_literal_type);
        }
        if (store.Type(n).IsNone)
        {
            return GrammarErrorOnNode(node, Diagnostics.An_index_signature_must_have_a_type_annotation);
        }
        return false;
    }

    // checker.ts checkGrammarForAtLeastOneTypeArgument / checkGrammarTypeArguments
    private bool CheckGrammarTypeArguments(NodeRef node, NodeList typeArguments)
    {
        if (CheckGrammarForDisallowedTrailingComma(node, typeArguments))
        {
            return true;
        }
        var store = StoreOf(node);
        if (!typeArguments.IsNone && store.Length(typeArguments) == 0)
        {
            var start = store.Pos(typeArguments) - 1;
            var end = Scanner.SkipTrivia(FileTextOf(node.File), store.End(typeArguments)) + 1;
            return GrammarErrorAtPos(node, start, end - start, Diagnostics.Type_argument_list_cannot_be_empty);
        }
        return false;
    }

    // checker.ts checkGrammarHeritageClause
    private bool CheckGrammarHeritageClause(NodeRef node)
    {
        var store = StoreOf(node);
        var types = store.Types(node.Node);
        if (CheckGrammarForDisallowedTrailingComma(node, types))
        {
            return true;
        }
        if (!types.IsNone && store.Length(types) == 0)
        {
            var listType = Scanner.TokenToString(store.Token(node.Node));
            return GrammarErrorAtPos(node, store.Pos(types), 0, Diagnostics._0_list_cannot_be_empty, listType);
        }
        foreach (var type in store.Nodes(types))
        {
            // checkGrammarExpressionWithTypeArguments
            var typeRef = At(node, type);
            if (store.Kind(store.Expression(type)) == SyntaxKind.ImportKeyword && !store.TypeArguments(type).IsNone)
            {
                if (GrammarErrorOnNode(typeRef, Diagnostics.This_use_of_import_is_invalid_import_calls_can_be_written_but_they_must_have_parentheses_and_cannot_have_type_arguments))
                {
                    return true;
                }
                continue;
            }
            if (CheckGrammarTypeArguments(typeRef, store.TypeArguments(type)))
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts checkGrammarClassDeclarationHeritageClauses
    private bool CheckGrammarClassDeclarationHeritageClauses(NodeRef node)
    {
        var store = StoreOf(node);
        var seenExtendsClause = false;
        var seenImplementsClause = false;
        var clauses = store.HeritageClauses(node.Node);
        if (!CheckGrammarModifiers(node) && !clauses.IsNone)
        {
            foreach (var heritageClause in store.Nodes(clauses))
            {
                var clause = At(node, heritageClause);
                if (store.Token(heritageClause) == SyntaxKind.ExtendsKeyword)
                {
                    if (seenExtendsClause)
                    {
                        return GrammarErrorOnFirstToken(clause, Diagnostics.extends_clause_already_seen);
                    }
                    if (seenImplementsClause)
                    {
                        return GrammarErrorOnFirstToken(clause, Diagnostics.extends_clause_must_precede_implements_clause);
                    }
                    var types = store.Nodes(store.Types(heritageClause));
                    if (types.Length > 1)
                    {
                        return GrammarErrorOnFirstToken(At(node, types[1]), Diagnostics.Classes_can_only_extend_a_single_class);
                    }
                    seenExtendsClause = true;
                }
                else
                {
                    if (seenImplementsClause)
                    {
                        return GrammarErrorOnFirstToken(clause, Diagnostics.implements_clause_already_seen);
                    }
                    seenImplementsClause = true;
                }
                // Grammar checking heritageClause inside class declaration
                CheckGrammarHeritageClause(clause);
            }
        }
        return false;
    }

    // checker.ts checkGrammarInterfaceDeclaration
    private bool CheckGrammarInterfaceDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var seenExtendsClause = false;
        var clauses = store.HeritageClauses(node.Node);
        if (!clauses.IsNone)
        {
            foreach (var heritageClause in store.Nodes(clauses))
            {
                var clause = At(node, heritageClause);
                if (store.Token(heritageClause) == SyntaxKind.ExtendsKeyword)
                {
                    if (seenExtendsClause)
                    {
                        return GrammarErrorOnFirstToken(clause, Diagnostics.extends_clause_already_seen);
                    }
                    seenExtendsClause = true;
                }
                else
                {
                    return GrammarErrorOnFirstToken(clause, Diagnostics.Interface_declaration_cannot_have_implements_clause);
                }
                // Grammar checking heritageClause inside class declaration
                CheckGrammarHeritageClause(clause);
            }
        }
        return false;
    }

    // checker.ts checkGrammarComputedPropertyName
    private bool CheckGrammarComputedPropertyName(NodeRef node)
    {
        // If node is not a computedPropertyName, just skip the grammar checking
        if (node.IsNone || Kind(node) != SyntaxKind.ComputedPropertyName)
        {
            return false;
        }
        var store = StoreOf(node);
        var expression = store.Expression(node.Node);
        if (store.Kind(expression) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(expression)) == SyntaxKind.CommaToken)
        {
            return GrammarErrorOnNode(At(node, expression), Diagnostics.A_comma_expression_is_not_allowed_in_a_computed_property_name);
        }
        return false;
    }

    // checker.ts checkGrammarForGenerator
    private bool CheckGrammarForGenerator(NodeRef node)
    {
        var store = StoreOf(node);
        var asteriskToken = store.AsteriskToken(node.Node);
        if (!asteriskToken.IsNone)
        {
            if ((store.Flags(node.Node) & NodeFlags.Ambient) != 0)
            {
                return GrammarErrorOnNode(At(node, asteriskToken), Diagnostics.Generators_are_not_allowed_in_an_ambient_context);
            }
            if (store.Body(node.Node).IsNone)
            {
                return GrammarErrorOnNode(At(node, asteriskToken), Diagnostics.An_overload_signature_cannot_be_declared_as_a_generator);
            }
        }
        return false;
    }

    // checker.ts checkGrammarForInvalidQuestionMark / checkGrammarForInvalidExclamationToken
    private bool CheckGrammarForInvalidToken(NodeRef context, Node token, DiagnosticMessage message) =>
        !token.IsNone && GrammarErrorOnNode(At(context, token), message);

    // checker.ts checkGrammarAccessor
    private bool CheckGrammarAccessor(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var parent = store.Parent(n);
        var name = At(node, store.Name(n));
        var body = store.Body(n);
        if ((store.Flags(n) & NodeFlags.Ambient) == 0 && store.Kind(parent) is not (SyntaxKind.TypeLiteral or SyntaxKind.InterfaceDeclaration))
        {
            if (languageVersion < ScriptTarget.ES2015 && store.Kind(name.Node) == SyntaxKind.PrivateIdentifier)
            {
                return GrammarErrorOnNode(name, Diagnostics.Private_identifiers_are_only_available_when_targeting_ECMAScript_2015_and_higher);
            }
            if (body.IsNone && !Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Abstract))
            {
                return GrammarErrorAtPos(node, store.End(n) - 1, 1, Diagnostics._0_expected, "{");
            }
        }
        if (!body.IsNone)
        {
            if (Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Abstract))
            {
                return GrammarErrorOnNode(node, Diagnostics.An_abstract_accessor_cannot_have_an_implementation);
            }
            if (store.Kind(parent) is SyntaxKind.TypeLiteral or SyntaxKind.InterfaceDeclaration)
            {
                return GrammarErrorOnNode(At(node, body), Diagnostics.An_implementation_cannot_be_declared_in_ambient_contexts);
            }
        }
        if (!store.TypeParameters(n).IsNone)
        {
            return GrammarErrorOnNode(name, Diagnostics.An_accessor_cannot_have_type_parameters);
        }
        var parameters = store.Nodes(store.Parameters(n));
        var isGet = store.Kind(n) == SyntaxKind.GetAccessor;
        // doesAccessorHaveCorrectParameterCount
        var hasThis = parameters.Length == (isGet ? 1 : 2) && !GetThisParameter(store, n).IsNone;
        if (!hasThis && parameters.Length != (isGet ? 0 : 1))
        {
            return GrammarErrorOnNode(name, isGet ? Diagnostics.A_get_accessor_cannot_have_parameters : Diagnostics.A_set_accessor_must_have_exactly_one_parameter);
        }
        if (!isGet)
        {
            if (!store.Type(n).IsNone)
            {
                return GrammarErrorOnNode(name, Diagnostics.A_set_accessor_cannot_have_a_return_type_annotation);
            }
            // getSetAccessorValueParameter
            var parameter = parameters.Length == 2 && Utilities.IsThisIdentifier(store, store.Name(parameters[0])) ? parameters[1] : parameters.Length > 0 ? parameters[0] : Node.None;
            if (!parameter.IsNone)
            {
                if (!store.DotDotDotToken(parameter).IsNone)
                {
                    return GrammarErrorOnNode(At(node, store.DotDotDotToken(parameter)), Diagnostics.A_set_accessor_cannot_have_rest_parameter);
                }
                if (!store.QuestionToken(parameter).IsNone)
                {
                    return GrammarErrorOnNode(At(node, store.QuestionToken(parameter)), Diagnostics.A_set_accessor_cannot_have_an_optional_parameter);
                }
                if (!store.Initializer(parameter).IsNone)
                {
                    return GrammarErrorOnNode(name, Diagnostics.A_set_accessor_parameter_cannot_have_an_initializer);
                }
            }
        }
        return false;
    }

    // checker.ts checkGrammarTypeOperatorNode
    private bool CheckGrammarTypeOperatorNode(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var type = store.Type(n);
        if (store.Operator(n) == SyntaxKind.UniqueKeyword)
        {
            if (store.Kind(type) != SyntaxKind.SymbolKeyword)
            {
                return GrammarErrorOnNode(At(node, type), Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.SymbolKeyword));
            }
            var parent = store.Parent(n);
            while (store.Kind(parent) == SyntaxKind.ParenthesizedType)
            {
                parent = store.Parent(parent);
            }
            // (JSDoc type expressions of JavaScript files: TODO(C9))
            switch (store.Kind(parent))
            {
                case SyntaxKind.VariableDeclaration:
                    if (store.Kind(store.Name(parent)) != SyntaxKind.Identifier)
                    {
                        return GrammarErrorOnNode(node, Diagnostics.unique_symbol_types_may_not_be_used_on_a_variable_declaration_with_a_binding_name);
                    }
                    if (!IsVariableDeclarationInVariableStatement(store, parent))
                    {
                        return GrammarErrorOnNode(node, Diagnostics.unique_symbol_types_are_only_allowed_on_variables_in_a_variable_statement);
                    }
                    if ((store.Flags(store.Parent(parent)) & NodeFlags.Const) == 0)
                    {
                        return GrammarErrorOnNode(At(node, store.Name(parent)), Diagnostics.A_variable_whose_type_is_a_unique_symbol_type_must_be_const);
                    }
                    break;
                case SyntaxKind.PropertyDeclaration:
                    if (!Utilities.IsStatic(store, parent) || !Utilities.HasSyntacticModifierUncached(store, parent, ModifierFlags.Readonly))
                    {
                        return GrammarErrorOnNode(At(node, store.Name(parent)), Diagnostics.A_property_of_a_class_whose_type_is_a_unique_symbol_type_must_be_both_static_and_readonly);
                    }
                    break;
                case SyntaxKind.PropertySignature:
                    if (!Utilities.HasSyntacticModifierUncached(store, parent, ModifierFlags.Readonly))
                    {
                        return GrammarErrorOnNode(At(node, store.Name(parent)), Diagnostics.A_property_of_an_interface_or_type_literal_whose_type_is_a_unique_symbol_type_must_be_readonly);
                    }
                    break;
                default:
                    return GrammarErrorOnNode(node, Diagnostics.unique_symbol_types_are_not_allowed_here);
            }
        }
        else if (store.Operator(n) == SyntaxKind.ReadonlyKeyword)
        {
            if (store.Kind(type) is not (SyntaxKind.ArrayType or SyntaxKind.TupleType))
            {
                return GrammarErrorOnFirstToken(node, Diagnostics.readonly_type_modifier_is_only_permitted_on_array_and_tuple_literal_types, Scanner.TokenToString(SyntaxKind.SymbolKeyword));
            }
        }
        return false;
    }

    // checker.ts checkGrammarForInvalidDynamicName
    private bool CheckGrammarForInvalidDynamicName(NodeRef node, DiagnosticMessage message)
    {
        // Even non-bindable names are allowed as late-bound implied index signatures so long as the name is a simple `a.b.c` type name expression
        var store = StoreOf(node);
        if (IsNonBindableDynamicName(node))
        {
            var expression = store.Kind(node.Node) == SyntaxKind.ElementAccessExpression
                ? Utilities.SkipParentheses(store, store.ArgumentExpression(node.Node))
                : store.Expression(node.Node);
            if (!Utilities.IsEntityNameExpression(store, expression))
            {
                return GrammarErrorOnNode(node, message);
            }
        }
        return false;
    }

    // checker.ts checkGrammarMethod
    private bool CheckGrammarMethod(NodeRef node)
    {
        if (CheckGrammarFunctionLikeDeclaration(node))
        {
            return true;
        }
        var store = StoreOf(node);
        var n = node.Node;
        var parent = store.Parent(n);
        var name = At(node, store.Name(n));
        if (store.Kind(n) == SyntaxKind.MethodDeclaration)
        {
            if (store.Kind(parent) == SyntaxKind.ObjectLiteralExpression)
            {
                // We only disallow modifier on a method declaration if it is a property of object-literal-expression
                var modifiers = store.Modifiers(n);
                if (!modifiers.IsNone && !(store.Length(modifiers) == 1 && store.Kind(store.Nodes(modifiers)[0]) == SyntaxKind.AsyncKeyword))
                {
                    return GrammarErrorOnFirstToken(node, Diagnostics.Modifiers_cannot_appear_here);
                }
                else if (CheckGrammarForInvalidToken(node, store.QuestionToken(n), Diagnostics.An_object_member_cannot_be_declared_optional))
                {
                    return true;
                }
                else if (CheckGrammarForInvalidToken(node, store.ExclamationToken(n), Diagnostics.A_definite_assignment_assertion_is_not_permitted_in_this_context))
                {
                    return true;
                }
                else if (store.Body(n).IsNone)
                {
                    return GrammarErrorAtPos(node, store.End(n) - 1, 1, Diagnostics._0_expected, "{");
                }
            }
            if (CheckGrammarForGenerator(node))
            {
                return true;
            }
        }
        if (Utilities.IsClassLike(store, parent))
        {
            if (languageVersion < ScriptTarget.ES2015 && store.Kind(name.Node) == SyntaxKind.PrivateIdentifier)
            {
                return GrammarErrorOnNode(name, Diagnostics.Private_identifiers_are_only_available_when_targeting_ECMAScript_2015_and_higher);
            }
            // Technically, computed properties in ambient contexts is disallowed
            // for property declarations and accessors too, not just methods.
            // However, property declarations disallow computed names in general,
            // and accessors are not allowed in ambient contexts in general,
            // so this error only really matters for methods.
            if ((store.Flags(n) & NodeFlags.Ambient) != 0)
            {
                return CheckGrammarForInvalidDynamicName(name, Diagnostics.A_computed_property_name_in_an_ambient_context_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type);
            }
            else if (store.Kind(n) == SyntaxKind.MethodDeclaration && store.Body(n).IsNone)
            {
                return CheckGrammarForInvalidDynamicName(name, Diagnostics.A_computed_property_name_in_a_method_overload_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type);
            }
        }
        else if (store.Kind(parent) == SyntaxKind.InterfaceDeclaration)
        {
            return CheckGrammarForInvalidDynamicName(name, Diagnostics.A_computed_property_name_in_an_interface_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type);
        }
        else if (store.Kind(parent) == SyntaxKind.TypeLiteral)
        {
            return CheckGrammarForInvalidDynamicName(name, Diagnostics.A_computed_property_name_in_a_type_literal_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type);
        }
        return false;
    }

    // ---- statements ----

    // checker.ts checkGrammarBreakOrContinueStatement
    private bool CheckGrammarBreakOrContinueStatement(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var label = store.Label(n);
        var isBreak = store.Kind(n) == SyntaxKind.BreakStatement;
        for (var current = n; !current.IsNone; current = store.Parent(current))
        {
            if (Utilities.IsFunctionLikeOrClassStaticBlockDeclaration(store, current))
            {
                return GrammarErrorOnNode(node, Diagnostics.Jump_target_cannot_cross_function_boundary);
            }
            switch (store.Kind(current))
            {
                case SyntaxKind.LabeledStatement:
                    if (!label.IsNone && store.Texts.Equals(store.EscapedText(store.Label(current)), store.Texts.GetString(store.EscapedText(label))!))
                    {
                        // found matching label - verify that label usage is correct
                        // continue can only target labels that are on iteration statements
                        var isMisplacedContinueLabel = !isBreak && !IsIterationStatement(store, store.Statement(current), lookInLabeledStatements: true);
                        if (isMisplacedContinueLabel)
                        {
                            return GrammarErrorOnNode(node, Diagnostics.A_continue_statement_can_only_jump_to_a_label_of_an_enclosing_iteration_statement);
                        }
                        return false;
                    }
                    break;
                case SyntaxKind.SwitchStatement:
                    if (isBreak && label.IsNone)
                    {
                        // unlabeled break within switch statement - ok
                        return false;
                    }
                    break;
                default:
                    if (IsIterationStatement(store, current, lookInLabeledStatements: false) && label.IsNone)
                    {
                        // unlabeled break or continue within iteration statement - ok
                        return false;
                    }
                    break;
            }
        }
        if (!label.IsNone)
        {
            return GrammarErrorOnNode(node, isBreak
                ? Diagnostics.A_break_statement_can_only_jump_to_a_label_of_an_enclosing_statement
                : Diagnostics.A_continue_statement_can_only_jump_to_a_label_of_an_enclosing_iteration_statement);
        }
        return GrammarErrorOnNode(node, isBreak
            ? Diagnostics.A_break_statement_can_only_be_used_within_an_enclosing_iteration_or_switch_statement
            : Diagnostics.A_continue_statement_can_only_be_used_within_an_enclosing_iteration_statement);
    }

    // utilities.ts isIterationStatement
    private static bool IsIterationStatement(NodeStore store, Node node, bool lookInLabeledStatements)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
            case SyntaxKind.DoStatement:
            case SyntaxKind.WhileStatement:
                return true;
            case SyntaxKind.LabeledStatement:
                return lookInLabeledStatements && IsIterationStatement(store, store.Statement(node), lookInLabeledStatements);
        }
        return false;
    }

    // checker.ts checkGrammarBindingElement
    private bool CheckGrammarBindingElement(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (!store.DotDotDotToken(n).IsNone)
        {
            var elementsList = store.Elements(store.Parent(n));
            var elements = store.Nodes(elementsList);
            if (n != elements[^1])
            {
                return GrammarErrorOnNode(node, Diagnostics.A_rest_element_must_be_last_in_a_destructuring_pattern);
            }
            CheckGrammarForDisallowedTrailingComma(node, elementsList, Diagnostics.A_rest_parameter_or_binding_pattern_may_not_have_a_trailing_comma);
            if (!store.PropertyName(n).IsNone)
            {
                return GrammarErrorOnNode(At(node, store.Name(n)), Diagnostics.A_rest_element_cannot_have_a_property_name);
            }
        }
        if (!store.DotDotDotToken(n).IsNone && !store.Initializer(n).IsNone)
        {
            // Error on equals token which immediately precedes the initializer
            return GrammarErrorAtPos(node, store.Pos(store.Initializer(n)) - 1, 1, Diagnostics.A_rest_element_cannot_have_an_initializer);
        }
        return false;
    }

    private static bool IsStringOrNumberLiteralExpression(NodeStore store, Node expr) =>
        Utilities.IsStringOrNumericLiteralLike(store, expr) ||
        (store.Kind(expr) == SyntaxKind.PrefixUnaryExpression && store.Operator(expr) == SyntaxKind.MinusToken && store.Kind(store.Operand(expr)) == SyntaxKind.NumericLiteral);

    private static bool IsBigIntLiteralExpression(NodeStore store, Node expr) =>
        store.Kind(expr) == SyntaxKind.BigIntLiteral ||
        (store.Kind(expr) == SyntaxKind.PrefixUnaryExpression && store.Operator(expr) == SyntaxKind.MinusToken && store.Kind(store.Operand(expr)) == SyntaxKind.BigIntLiteral);

    // checker.ts isSimpleLiteralEnumReference
    private bool IsSimpleLiteralEnumReference(NodeRef expr)
    {
        var store = StoreOf(expr);
        var n = expr.Node;
        if ((store.Kind(n) == SyntaxKind.PropertyAccessExpression ||
                (store.Kind(n) == SyntaxKind.ElementAccessExpression && IsStringOrNumberLiteralExpression(store, store.ArgumentExpression(n)))) &&
            Utilities.IsEntityNameExpression(store, store.Expression(n)))
        {
            return (Types.Flags(CheckExpressionCached(expr)) & TypeFlags.EnumLike) != 0;
        }
        return false;
    }

    // checker.ts checkAmbientInitializer
    private bool CheckAmbientInitializer(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var initializer = store.Initializer(n);
        if (initializer.IsNone)
        {
            return false;
        }
        var initializerRef = At(node, initializer);
        var isInvalidInitializer = !(IsStringOrNumberLiteralExpression(store, initializer) || IsSimpleLiteralEnumReference(initializerRef) ||
            store.Kind(initializer) is SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword || IsBigIntLiteralExpression(store, initializer));
        var isConstOrReadonly = IsDeclarationReadonly(store, n) || (store.Kind(n) == SyntaxKind.VariableDeclaration && IsVarConstLike(store, n));
        if (isConstOrReadonly && store.Type(n).IsNone)
        {
            if (isInvalidInitializer)
            {
                return GrammarErrorOnNode(initializerRef, Diagnostics.A_const_initializer_in_an_ambient_context_must_be_a_string_or_numeric_literal_or_literal_enum_reference);
            }
        }
        else
        {
            return GrammarErrorOnNode(initializerRef, Diagnostics.Initializers_are_not_allowed_in_ambient_contexts);
        }
        return false;
    }

    // checker.ts checkGrammarVariableDeclaration
    private bool CheckGrammarVariableDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var nodeFlags = Utilities.GetCombinedNodeFlags(store, n);
        var blockScopeKind = nodeFlags & NodeFlags.BlockScoped;
        var name = store.Name(n);
        if (Utilities.IsBindingPattern(store, name))
        {
            switch (blockScopeKind)
            {
                case NodeFlags.AwaitUsing:
                    return GrammarErrorOnNode(node, Diagnostics._0_declarations_may_not_have_binding_patterns, "await using");
                case NodeFlags.Using:
                    return GrammarErrorOnNode(node, Diagnostics._0_declarations_may_not_have_binding_patterns, "using");
            }
        }
        var grandParent = store.Parent(store.Parent(n));
        if (store.Kind(grandParent) is not (SyntaxKind.ForInStatement or SyntaxKind.ForOfStatement))
        {
            if ((nodeFlags & NodeFlags.Ambient) != 0)
            {
                CheckAmbientInitializer(node);
            }
            else if (store.Initializer(n).IsNone)
            {
                if (Utilities.IsBindingPattern(store, name) && !Utilities.IsBindingPattern(store, store.Parent(n)))
                {
                    return GrammarErrorOnNode(node, Diagnostics.A_destructuring_declaration_must_have_an_initializer);
                }
                switch (blockScopeKind)
                {
                    case NodeFlags.AwaitUsing:
                        return GrammarErrorOnNode(node, Diagnostics._0_declarations_must_be_initialized, "await using");
                    case NodeFlags.Using:
                        return GrammarErrorOnNode(node, Diagnostics._0_declarations_must_be_initialized, "using");
                    case NodeFlags.Const:
                        return GrammarErrorOnNode(node, Diagnostics._0_declarations_must_be_initialized, "const");
                }
            }
        }
        var exclamationToken = store.ExclamationToken(n);
        if (!exclamationToken.IsNone && (store.Kind(grandParent) != SyntaxKind.VariableStatement || store.Type(n).IsNone || !store.Initializer(n).IsNone || (nodeFlags & NodeFlags.Ambient) != 0))
        {
            var message = !store.Initializer(n).IsNone
                ? Diagnostics.Declarations_with_initializers_cannot_also_have_definite_assignment_assertions
                : store.Type(n).IsNone
                    ? Diagnostics.Declarations_with_definite_assignment_assertions_must_also_have_type_annotations
                    : Diagnostics.A_definite_assignment_assertion_is_not_permitted_in_this_context;
            return GrammarErrorOnNode(At(node, exclamationToken), message);
        }
        if (GetEmitModuleFormatOfFile(node.File) < ModuleKind.System && (store.Flags(grandParent) & NodeFlags.Ambient) == 0 &&
            Utilities.HasSyntacticModifierUncached(store, grandParent, ModifierFlags.Export))
        {
            CheckESModuleMarker(At(node, name));
        }
        // 1. LexicalDeclaration : LetOrConst BindingList ;
        // It is a Syntax Error if the BoundNames of BindingList contains "let".
        // 2. ForDeclaration: ForDeclaration : LetOrConst ForBinding
        // It is a Syntax Error if the BoundNames of ForDeclaration contains "let".

        // It is a SyntaxError if a VariableDeclaration or VariableDeclarationNoIn occurs within strict code
        // and its Identifier is eval or arguments
        return blockScopeKind != 0 && CheckGrammarNameInLetOrConstDeclarations(At(node, name));
    }

    // checker.ts checkESModuleMarker
    private bool CheckESModuleMarker(NodeRef name)
    {
        var store = StoreOf(name);
        if (store.Kind(name.Node) == SyntaxKind.Identifier)
        {
            if (store.Texts.Equals(Utilities.IdText(store, name.Node), "__esModule"))
            {
                return GrammarErrorOnNode(name, Diagnostics.Identifier_expected_esModule_is_reserved_as_an_exported_marker_when_transforming_ECMAScript_modules);
            }
        }
        else
        {
            foreach (var element in store.Nodes(store.Elements(name.Node)))
            {
                if (store.Kind(element) != SyntaxKind.OmittedExpression)
                {
                    return CheckESModuleMarker(At(name, store.Name(element)));
                }
            }
        }
        return false;
    }

    // checker.ts checkGrammarNameInLetOrConstDeclarations
    private bool CheckGrammarNameInLetOrConstDeclarations(NodeRef name)
    {
        var store = StoreOf(name);
        if (store.Kind(name.Node) == SyntaxKind.Identifier)
        {
            if (store.Texts.Equals(store.EscapedText(name.Node), "let"))
            {
                return GrammarErrorOnNode(name, Diagnostics.let_is_not_allowed_to_be_used_as_a_name_in_let_or_const_declarations);
            }
        }
        else
        {
            foreach (var element in store.Nodes(store.Elements(name.Node)))
            {
                if (store.Kind(element) != SyntaxKind.OmittedExpression)
                {
                    CheckGrammarNameInLetOrConstDeclarations(At(name, store.Name(element)));
                }
            }
        }
        return false;
    }

    // checker.ts checkGrammarVariableDeclarationList
    private bool CheckGrammarVariableDeclarationList(NodeRef declarationList)
    {
        var store = StoreOf(declarationList);
        var n = declarationList.Node;
        var declarations = store.Declarations(n);
        if (CheckGrammarForDisallowedTrailingComma(declarationList, declarations))
        {
            return true;
        }
        if (store.Length(declarations) == 0)
        {
            return GrammarErrorAtPos(declarationList, store.Pos(declarations), store.End(declarations) - store.Pos(declarations), Diagnostics.Variable_declaration_list_cannot_be_empty);
        }
        var blockScopeFlags = store.Flags(n) & NodeFlags.BlockScoped;
        if (blockScopeFlags is NodeFlags.Using or NodeFlags.AwaitUsing)
        {
            var parent = store.Parent(n);
            if (store.Kind(parent) == SyntaxKind.ForInStatement)
            {
                return GrammarErrorOnNode(declarationList, blockScopeFlags == NodeFlags.Using
                    ? Diagnostics.The_left_hand_side_of_a_for_in_statement_cannot_be_a_using_declaration
                    : Diagnostics.The_left_hand_side_of_a_for_in_statement_cannot_be_an_await_using_declaration);
            }
            else if (store.Kind(parent) == SyntaxKind.VariableStatement && store.Kind(store.Parent(parent)) is SyntaxKind.CaseClause or SyntaxKind.DefaultClause)
            {
                return GrammarErrorOnNode(declarationList, blockScopeFlags == NodeFlags.Using
                    ? Diagnostics.using_declarations_are_not_allowed_in_case_or_default_clauses_unless_contained_within_a_block
                    : Diagnostics.await_using_declarations_are_not_allowed_in_case_or_default_clauses_unless_contained_within_a_block);
            }
            if ((store.Flags(n) & NodeFlags.Ambient) != 0)
            {
                return GrammarErrorOnNode(declarationList, blockScopeFlags == NodeFlags.Using
                    ? Diagnostics.using_declarations_are_not_allowed_in_ambient_contexts
                    : Diagnostics.await_using_declarations_are_not_allowed_in_ambient_contexts);
            }
            if (blockScopeFlags == NodeFlags.AwaitUsing)
            {
                return CheckAwaitGrammar(declarationList);
            }
        }
        return false;
    }

    // checker.ts checkGrammarForDisallowedBlockScopedVariableStatement
    private void CheckGrammarForDisallowedBlockScopedVariableStatement(NodeRef node)
    {
        var store = StoreOf(node);
        if (!AllowBlockDeclarations(store, store.Parent(node.Node)))
        {
            var blockScopeKind = Utilities.GetCombinedNodeFlags(store, store.DeclarationList(node.Node)) & NodeFlags.BlockScoped;
            if (blockScopeKind != 0)
            {
                var keyword = blockScopeKind switch
                {
                    NodeFlags.Let => "let",
                    NodeFlags.Const => "const",
                    NodeFlags.Using => "using",
                    _ => "await using",
                };
                Error(node, Diagnostics._0_declarations_can_only_be_declared_inside_a_block, keyword);
            }
        }
    }

    // checker.ts checkGrammarForInOrForOfStatement
    private bool CheckGrammarForInOrForOfStatement(NodeRef node)
    {
        if (CheckGrammarStatementInAmbientContext(node))
        {
            return true;
        }
        var store = StoreOf(node);
        var n = node.Node;
        var awaitModifier = store.AwaitModifier(n);
        if (store.Kind(n) == SyntaxKind.ForOfStatement && !awaitModifier.IsNone)
        {
            if ((store.Flags(n) & NodeFlags.AwaitContext) == 0)
            {
                var awaitRef = At(node, awaitModifier);
                if (IsInTopLevelContext(store, n))
                {
                    if (!HasParseDiagnostics(node.File))
                    {
                        if (!IsEffectiveExternalModule(node.File))
                        {
                            diagnostics.Add(CreateDiagnosticForNode(awaitRef, Diagnostics.for_await_loops_are_only_allowed_at_the_top_level_of_a_file_when_that_file_is_a_module_but_this_file_has_no_imports_or_exports_Consider_adding_an_empty_export_to_make_this_file_a_module));
                        }
                        if (!TopLevelAwaitAllowed(node.File))
                        {
                            diagnostics.Add(CreateDiagnosticForNode(awaitRef, Diagnostics.Top_level_for_await_loops_are_only_allowed_when_the_module_option_is_set_to_es2022_esnext_system_node16_node18_node20_nodenext_or_preserve_and_the_target_option_is_set_to_es2017_or_higher));
                        }
                    }
                }
                else
                {
                    // use of 'for-await-of' in non-async function
                    if (!HasParseDiagnostics(node.File))
                    {
                        var diagnostic = CreateDiagnosticForNode(awaitRef, Diagnostics.for_await_loops_are_only_allowed_within_async_functions_and_at_the_top_levels_of_modules);
                        var func = Utilities.GetContainingFunctionOrNone(store, n);
                        if (!func.IsNone && store.Kind(func) != SyntaxKind.Constructor)
                        {
                            AddRelatedInfo(diagnostic, CreateRelatedForNode(At(node, func), Diagnostics.Did_you_mean_to_mark_this_function_as_async));
                        }
                        diagnostics.Add(diagnostic);
                        return true;
                    }
                }
            }
        }
        var initializer = store.Initializer(n);
        if (store.Kind(n) == SyntaxKind.ForOfStatement && (store.Flags(n) & NodeFlags.AwaitContext) == 0 &&
            store.Kind(initializer) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(initializer), "async"))
        {
            GrammarErrorOnNode(At(node, initializer), Diagnostics.The_left_hand_side_of_a_for_of_statement_may_not_be_async);
            return false;
        }
        if (store.Kind(initializer) == SyntaxKind.VariableDeclarationList)
        {
            var variableList = At(node, initializer);
            if (!CheckGrammarVariableDeclarationList(variableList))
            {
                var declarations = store.Nodes(store.Declarations(initializer));
                // declarations.length can be zero if there is an error in variable declaration in for-of or for-in
                // See http://www.ecma-international.org/ecma-262/6.0/#sec-for-in-and-for-of-statements for details
                // For example:
                //      var let = 10;
                //      for (let of [1,2,3]) {} // this is invalid ES6 syntax
                //      for (let in [1,2,3]) {} // this is invalid ES6 syntax
                // We will then want to skip on grammar checking on variableList declaration
                if (declarations.Length == 0)
                {
                    return false;
                }
                var isForIn = store.Kind(n) == SyntaxKind.ForInStatement;
                if (declarations.Length > 1)
                {
                    return GrammarErrorOnFirstToken(At(node, declarations[1]), isForIn
                        ? Diagnostics.Only_a_single_variable_declaration_is_allowed_in_a_for_in_statement
                        : Diagnostics.Only_a_single_variable_declaration_is_allowed_in_a_for_of_statement);
                }
                var firstDeclaration = declarations[0];
                if (!store.Initializer(firstDeclaration).IsNone)
                {
                    return GrammarErrorOnNode(At(node, store.Name(firstDeclaration)), isForIn
                        ? Diagnostics.The_variable_declaration_of_a_for_in_statement_cannot_have_an_initializer
                        : Diagnostics.The_variable_declaration_of_a_for_of_statement_cannot_have_an_initializer);
                }
                if (!store.Type(firstDeclaration).IsNone)
                {
                    return GrammarErrorOnNode(At(node, firstDeclaration), isForIn
                        ? Diagnostics.The_left_hand_side_of_a_for_in_statement_cannot_use_a_type_annotation
                        : Diagnostics.The_left_hand_side_of_a_for_of_statement_cannot_use_a_type_annotation);
                }
            }
        }
        return false;
    }

    // utilities.ts isInTopLevelContext
    private static bool IsInTopLevelContext(NodeStore store, Node node)
    {
        // The name of a class or function declaration is a BindingIdentifier in its surrounding scope.
        var parent = store.Parent(node);
        if (store.Kind(node) == SyntaxKind.Identifier && store.Kind(parent) is SyntaxKind.ClassDeclaration or SyntaxKind.FunctionDeclaration && store.Name(parent) == node)
        {
            node = parent;
        }
        var container = Utilities.GetThisContainer(store, node, includeArrowFunctions: true, includeClassComputedPropertyName: false);
        return store.Kind(container) == SyntaxKind.SourceFile;
    }

    // utilities.ts isEffectiveExternalModule (Gate C's module kinds do not contain CommonJS modules of JavaScript)
    private bool IsEffectiveExternalModule(int file)
    {
        var f = FileAt(file);
        return f.Store.ExternalModuleIndicator(f.Root) is not null ||
            (moduleKind is ModuleKind.CommonJS or ModuleKind.Node16 or ModuleKind.Node18 or ModuleKind.Node20 or ModuleKind.NodeNext && !BindingOf(file).CommonJsModuleIndicator.IsNone);
    }

    // the module/target switch of checkAwaitGrammar / checkGrammarForInOrForOfStatement: whether top-level await is allowed
    private bool TopLevelAwaitAllowed(int file)
    {
        switch (moduleKind)
        {
            case ModuleKind.Node16:
            case ModuleKind.Node18:
            case ModuleKind.Node20:
            case ModuleKind.NodeNext:
            case ModuleKind.ES2022:
            case ModuleKind.ESNext:
            case ModuleKind.Preserve:
            case ModuleKind.System:
                return languageVersion >= ScriptTarget.ES2017;
            default:
                return false;
        }
    }

    // checker.ts checkAwaitGrammar
    internal bool CheckAwaitGrammar(NodeRef node)
    {
        // Grammar checking
        var store = StoreOf(node);
        var n = node.Node;
        var isAwaitExpression = store.Kind(n) == SyntaxKind.AwaitExpression;
        var hasError = false;
        var container = GetContainingFunctionOrClassStaticBlock(store, n);
        if (!container.IsNone && store.Kind(container) == SyntaxKind.ClassStaticBlockDeclaration)
        {
            // NOTE: We report this regardless as to whether there are parse diagnostics.
            Error(node, isAwaitExpression ? Diagnostics.await_expression_cannot_be_used_inside_a_class_static_block : Diagnostics.await_using_statements_cannot_be_used_inside_a_class_static_block);
            hasError = true;
        }
        else if ((store.Flags(n) & NodeFlags.AwaitContext) == 0)
        {
            var file = FileAt(node.File);
            if (IsInTopLevelContext(store, n))
            {
                if (!HasParseDiagnostics(node.File))
                {
                    var span = Utilities.GetSpanOfTokenAtPosition(file.Store, file.Root, store.Pos(n));
                    if (!IsEffectiveExternalModule(node.File))
                    {
                        diagnostics.Add(CreateFileDiagnosticAt(node.File, span.Start, span.Length, isAwaitExpression
                            ? Diagnostics.await_expressions_are_only_allowed_at_the_top_level_of_a_file_when_that_file_is_a_module_but_this_file_has_no_imports_or_exports_Consider_adding_an_empty_export_to_make_this_file_a_module
                            : Diagnostics.await_using_statements_are_only_allowed_at_the_top_level_of_a_file_when_that_file_is_a_module_but_this_file_has_no_imports_or_exports_Consider_adding_an_empty_export_to_make_this_file_a_module));
                        hasError = true;
                    }
                    if (!TopLevelAwaitAllowed(node.File))
                    {
                        diagnostics.Add(CreateFileDiagnosticAt(node.File, span.Start, span.Length, isAwaitExpression
                            ? Diagnostics.Top_level_await_expressions_are_only_allowed_when_the_module_option_is_set_to_es2022_esnext_system_node16_node18_node20_nodenext_or_preserve_and_the_target_option_is_set_to_es2017_or_higher
                            : Diagnostics.Top_level_await_using_statements_are_only_allowed_when_the_module_option_is_set_to_es2022_esnext_system_node16_node18_node20_nodenext_or_preserve_and_the_target_option_is_set_to_es2017_or_higher));
                        hasError = true;
                    }
                }
            }
            else
            {
                // use of 'await' in non-async function
                if (!HasParseDiagnostics(node.File))
                {
                    var span = Utilities.GetSpanOfTokenAtPosition(file.Store, file.Root, store.Pos(n));
                    var diagnostic = CreateFileDiagnosticAt(node.File, span.Start, span.Length, isAwaitExpression
                        ? Diagnostics.await_expressions_are_only_allowed_within_async_functions_and_at_the_top_levels_of_modules
                        : Diagnostics.await_using_statements_are_only_allowed_within_async_functions_and_at_the_top_levels_of_modules);
                    if (!container.IsNone && store.Kind(container) != SyntaxKind.Constructor && (GetFunctionFlagsOf(store, container) & FunctionFlags.Async) == 0)
                    {
                        AddRelatedInfo(diagnostic, CreateRelatedForNode(At(node, container), Diagnostics.Did_you_mean_to_mark_this_function_as_async));
                    }
                    diagnostics.Add(diagnostic);
                    hasError = true;
                }
            }
        }
        if (isAwaitExpression && IsInParameterInitializerBeforeContainingFunction(node))
        {
            // NOTE: We report this regardless as to whether there are parse diagnostics.
            Error(node, Diagnostics.await_expressions_cannot_be_used_in_a_parameter_initializer);
            hasError = true;
        }
        return hasError;
    }

    // checker.ts checkYieldExpression's checkYieldExpressionGrammar
    internal void CheckYieldExpressionGrammar(NodeRef node)
    {
        if ((StoreOf(node).Flags(node.Node) & NodeFlags.YieldContext) == 0)
        {
            GrammarErrorOnFirstToken(node, Diagnostics.A_yield_expression_is_only_allowed_in_a_generator_body);
        }
        if (IsInParameterInitializerBeforeContainingFunction(node))
        {
            Error(node, Diagnostics.yield_expressions_cannot_be_used_in_a_parameter_initializer);
        }
    }

    // checker.ts checkGrammarConstructorTypeParameters
    private bool CheckGrammarConstructorTypeParameters(NodeRef node)
    {
        var store = StoreOf(node);
        var range = store.TypeParameters(node.Node);
        if (range.IsNone)
        {
            return false;
        }
        var pos = store.Pos(range) == store.End(range) ? store.Pos(range) : Scanner.SkipTrivia(FileTextOf(node.File), store.Pos(range));
        return GrammarErrorAtPos(node, pos, store.End(range) - pos, Diagnostics.Type_parameters_cannot_appear_on_a_constructor_declaration);
    }

    // checker.ts checkGrammarConstructorTypeAnnotation
    private bool CheckGrammarConstructorTypeAnnotation(NodeRef node)
    {
        var type = GetEffectiveReturnTypeNode(StoreOf(node), node.Node);
        return !type.IsNone && GrammarErrorOnNode(At(node, type), Diagnostics.Type_annotation_cannot_appear_on_a_constructor_declaration);
    }

    // checker.ts checkGrammarProperty
    private bool CheckGrammarProperty(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var name = At(node, store.Name(n));
        var parent = store.Parent(n);
        if (store.Kind(name.Node) == SyntaxKind.ComputedPropertyName && store.Kind(store.Expression(name.Node)) == SyntaxKind.BinaryExpression &&
            store.Kind(store.OperatorToken(store.Expression(name.Node))) == SyntaxKind.InKeyword)
        {
            return GrammarErrorOnNode(At(node, store.Nodes(store.Members(parent))[0]), Diagnostics.A_mapped_type_may_not_declare_properties_or_methods);
        }
        if (Utilities.IsClassLike(store, parent))
        {
            if (store.Kind(name.Node) == SyntaxKind.StringLiteral && store.Texts.Equals(store.Text(name.Node), "constructor"))
            {
                return GrammarErrorOnNode(name, Diagnostics.Classes_may_not_have_a_field_named_constructor);
            }
            if (CheckGrammarForInvalidDynamicName(name, Diagnostics.A_computed_property_name_in_a_class_property_declaration_must_have_a_simple_literal_type_or_a_unique_symbol_type))
            {
                return true;
            }
            if (languageVersion < ScriptTarget.ES2015 && store.Kind(name.Node) == SyntaxKind.PrivateIdentifier)
            {
                return GrammarErrorOnNode(name, Diagnostics.Private_identifiers_are_only_available_when_targeting_ECMAScript_2015_and_higher);
            }
            var isAutoAccessor = store.Kind(n) == SyntaxKind.PropertyDeclaration && Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Accessor);
            if (languageVersion < ScriptTarget.ES2015 && isAutoAccessor && (store.Flags(n) & NodeFlags.Ambient) == 0)
            {
                return GrammarErrorOnNode(name, Diagnostics.Properties_with_the_accessor_modifier_are_only_available_when_targeting_ECMAScript_2015_and_higher);
            }
            if (isAutoAccessor && CheckGrammarForInvalidToken(node, store.QuestionToken(n), Diagnostics.An_accessor_property_cannot_be_declared_optional))
            {
                return true;
            }
        }
        else if (store.Kind(parent) == SyntaxKind.InterfaceDeclaration)
        {
            if (CheckGrammarForInvalidDynamicName(name, Diagnostics.A_computed_property_name_in_an_interface_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type))
            {
                return true;
            }
            // Interfaces cannot contain property declarations
            if (!store.Initializer(n).IsNone)
            {
                return GrammarErrorOnNode(At(node, store.Initializer(n)), Diagnostics.An_interface_property_cannot_have_an_initializer);
            }
        }
        else if (store.Kind(parent) == SyntaxKind.TypeLiteral)
        {
            if (CheckGrammarForInvalidDynamicName(name, Diagnostics.A_computed_property_name_in_a_type_literal_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type))
            {
                return true;
            }
            // Type literals cannot contain property declarations
            if (!store.Initializer(n).IsNone)
            {
                return GrammarErrorOnNode(At(node, store.Initializer(n)), Diagnostics.A_type_literal_property_cannot_have_an_initializer);
            }
        }
        if ((store.Flags(n) & NodeFlags.Ambient) != 0)
        {
            CheckAmbientInitializer(node);
        }
        var exclamationToken = store.ExclamationToken(n);
        if (store.Kind(n) == SyntaxKind.PropertyDeclaration && !exclamationToken.IsNone &&
            (!Utilities.IsClassLike(store, parent) || store.Type(n).IsNone || !store.Initializer(n).IsNone || (store.Flags(n) & NodeFlags.Ambient) != 0 ||
                Utilities.IsStatic(store, n) || Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Abstract)))
        {
            var message = !store.Initializer(n).IsNone
                ? Diagnostics.Declarations_with_initializers_cannot_also_have_definite_assignment_assertions
                : store.Type(n).IsNone
                    ? Diagnostics.Declarations_with_definite_assignment_assertions_must_also_have_type_annotations
                    : Diagnostics.A_definite_assignment_assertion_is_not_permitted_in_this_context;
            return GrammarErrorOnNode(At(node, exclamationToken), message);
        }
        return false;
    }

    // checker.ts checkGrammarSourceFile / checkGrammarTopLevelElementsForRequiredDeclareModifier
    private bool CheckGrammarSourceFile(NodeRef file)
    {
        var store = StoreOf(file);
        if ((store.Flags(file.Node) & NodeFlags.Ambient) == 0)
        {
            return false;
        }
        foreach (var decl in store.Nodes(store.Statements(file.Node)))
        {
            if (Utilities.IsDeclaration(store, decl) || store.Kind(decl) == SyntaxKind.VariableStatement)
            {
                // checkGrammarTopLevelElementForRequiredDeclareModifier
                // A declare modifier is required for any top level .d.ts declaration except export=, export default, export as namespace
                // interfaces and imports categories:
                //
                //  DeclarationElement:
                //     ExportAssignment
                //     export_opt   InterfaceDeclaration
                //     export_opt   TypeAliasDeclaration
                //     export_opt   ImportDeclaration
                //     export_opt   ExternalImportDeclaration
                //     export_opt   AmbientDeclaration
                //
                // TODO: The spec needs to be amended to reflect this grammar.
                if (store.Kind(decl) is SyntaxKind.InterfaceDeclaration or SyntaxKind.TypeAliasDeclaration or SyntaxKind.ImportDeclaration or SyntaxKind.ImportEqualsDeclaration
                        or SyntaxKind.ExportDeclaration or SyntaxKind.ExportAssignment or SyntaxKind.NamespaceExportDeclaration ||
                    Utilities.HasSyntacticModifierUncached(store, decl, ModifierFlags.Ambient | ModifierFlags.Export | ModifierFlags.Default))
                {
                    continue;
                }
                if (GrammarErrorOnFirstToken(At(file, decl), Diagnostics.Top_level_declarations_in_d_ts_files_must_start_with_either_a_declare_or_export_modifier))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // checker.ts checkGrammarStatementInAmbientContext
    private bool CheckGrammarStatementInAmbientContext(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if ((store.Flags(n) & NodeFlags.Ambient) == 0)
        {
            return false;
        }
        var parent = store.Parent(n);
        // Find containing block which is either Block, ModuleBlock, SourceFile
        if (!reportedStatementInAmbientContext.Contains(node) && (Utilities.IsFunctionLike(store, parent) || IsAccessorKind(store, parent)))
        {
            var reported = GrammarErrorOnFirstToken(node, Diagnostics.An_implementation_cannot_be_declared_in_ambient_contexts);
            if (reported)
            {
                reportedStatementInAmbientContext.Add(node);
            }
            return reported;
        }
        // We are either parented by another statement, or some sort of block.
        // If we're in a block, we only want to really report an error once
        // to prevent noisiness.  So use a bit on the block to indicate if
        // this has already been reported, and don't report if it has.
        //
        if (store.Kind(parent) is SyntaxKind.Block or SyntaxKind.ModuleBlock or SyntaxKind.SourceFile)
        {
            var parentRef = At(node, parent);
            // Check if the containing block ever report this error
            if (!reportedStatementInAmbientContext.Contains(parentRef))
            {
                var reported = GrammarErrorOnFirstToken(node, Diagnostics.Statements_are_not_allowed_in_ambient_contexts);
                if (reported)
                {
                    reportedStatementInAmbientContext.Add(parentRef);
                }
                return reported;
            }
        }
        // We must be parented by a statement.  If so, there's no need
        // to report the error as our parent will have already done it.
        return false;
    }

    // checker.ts checkGrammarNumericLiteral
    internal void CheckGrammarNumericLiteral(NodeRef node)
    {
        // Realism (size) checking
        // We should test against `getTextOfNode(node)` rather than `node.text`, because `node.text` for large numeric literals can contain "."
        // e.g. `node.text` for numeric literal `1100000000000000000000` is `1.1e21`.
        var store = StoreOf(node);
        var isFractional = GetTextOfNode(node).Contains('.', StringComparison.Ordinal);
        var isScientific = (store.NumericLiteralFlags(node.Node) & TokenFlags.Scientific) != 0;
        // Scientific notation (e.g. 2e54 and 1e00000000010) can't be converted to bigint
        // Fractional numbers (e.g. 9000000000000000.001) are inherently imprecise anyway
        if (isFractional || isScientific)
        {
            return;
        }
        // Here `node` is guaranteed to be a numeric literal representing an integer.
        // We need to judge whether the integer `node` represents is <= 2 ** 53 - 1, which can be accomplished by comparing to `value` defined below because:
        // 1) when `node` represents an integer <= 2 ** 53 - 1, `node.text` is its exact string representation and thus `value` precisely represents the integer.
        // 2) otherwise, although `node.text` may be imprecise string representation, its mathematical value and consequently `value` cannot be less than 2 ** 53,
        //    thus the result of the predicate won't be affected.
        var value = JsNumber.FromString(store.Texts.GetString(store.Text(node.Node))!);
        if (value <= 9007199254740991.0)
        {
            return;
        }
        ErrorOrSuggestion(isError: false, node, Diagnostics.Numeric_literals_with_absolute_values_equal_to_2_53_or_greater_are_too_large_to_be_represented_accurately_as_integers);
    }

    private readonly HashSet<NodeRef> checkedRegularExpressions = [];

    // checker.ts checkRegularExpressionLiteral / checkGrammarRegularExpressionLiteral: the regular expression is rescanned
    // with error reporting (once per node, a lazy diagnostic)
    internal void CheckRegularExpressionLiteral(NodeRef node)
    {
        if (!checkedRegularExpressions.Add(node))
        {
            return;
        }
        var store = StoreOf(node);
        if (HasParseDiagnostics(node.File) || store.IsUnterminated(node.Node))
        {
            return;
        }
        var root = FileAt(node.File).Root;
        ProgramDiagnostic? lastError = null;
        Scanner? scanner = null;
        scanner = Scanner.CreateScanner(store.LanguageVersion(root), skipTrivia: true, store.LanguageVariant(root), store.SourceFileText(root), (message, length, arg0) =>
        {
            // For providing spelling suggestions
            var start = scanner!.GetTokenEnd();
            if (message.Category == DiagnosticCategory.Message && lastError is not null && start == lastError.StartOrUndefined && length == lastError.LengthOrUndefined)
            {
                AddRelatedInfo(lastError, CreateRelatedForSpan(node.File, start, length, message, arg0));
            }
            else if (lastError is null || start != lastError.StartOrUndefined)
            {
                lastError = CreateFileDiagnosticAt(node.File, start, length, message, arg0);
                diagnostics.Add(lastError);
            }
        }, store.Pos(node.Node), store.End(node.Node) - store.Pos(node.Node));
        scanner.Scan();
        scanner.ReScanSlashToken(reportErrors: true);
    }

    // ---- imports and exports ----

    // checker.ts checkGrammarModuleElementContext
    private bool CheckGrammarModuleElementContext(NodeRef node, DiagnosticMessage errorMessage)
    {
        var store = StoreOf(node);
        var isInAppropriateContext = store.Kind(store.Parent(node.Node)) is SyntaxKind.SourceFile or SyntaxKind.ModuleBlock or SyntaxKind.ModuleDeclaration;
        if (!isInAppropriateContext)
        {
            GrammarErrorOnFirstToken(node, errorMessage);
        }
        return !isInAppropriateContext;
    }

    // checker.ts checkGrammarImportClause
    private bool CheckGrammarImportClause(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var namedBindings = store.NamedBindings(n);
        if (store.PhaseModifier(n) == SyntaxKind.TypeKeyword)
        {
            if (!store.Name(n).IsNone && !namedBindings.IsNone)
            {
                return GrammarErrorOnNode(node, Diagnostics.A_type_only_import_can_specify_a_default_import_or_named_bindings_but_not_both);
            }
            if (!namedBindings.IsNone && store.Kind(namedBindings) == SyntaxKind.NamedImports)
            {
                return CheckGrammarNamedImportsOrExports(At(node, namedBindings));
            }
        }
        else if (store.PhaseModifier(n) == SyntaxKind.DeferKeyword)
        {
            if (!store.Name(n).IsNone)
            {
                return GrammarErrorOnNode(node, Diagnostics.Default_imports_are_not_allowed_in_a_deferred_import);
            }
            if (!namedBindings.IsNone && store.Kind(namedBindings) == SyntaxKind.NamedImports)
            {
                return GrammarErrorOnNode(node, Diagnostics.Named_imports_are_not_allowed_in_a_deferred_import);
            }
            if (moduleKind is not (ModuleKind.ESNext or ModuleKind.Preserve))
            {
                return GrammarErrorOnNode(node, Diagnostics.Deferred_imports_are_only_supported_when_the_module_flag_is_set_to_esnext_or_preserve);
            }
        }
        return false;
    }

    // checker.ts checkGrammarNamedImportsOrExports
    private bool CheckGrammarNamedImportsOrExports(NodeRef namedBindings)
    {
        var store = StoreOf(namedBindings);
        foreach (var specifier in store.Nodes(store.Elements(namedBindings.Node)))
        {
            if (store.IsTypeOnly(specifier))
            {
                if (GrammarErrorOnFirstToken(At(namedBindings, specifier), store.Kind(specifier) == SyntaxKind.ImportSpecifier
                    ? Diagnostics.The_type_modifier_cannot_be_used_on_a_named_import_when_import_type_is_used_on_its_import_statement
                    : Diagnostics.The_type_modifier_cannot_be_used_on_a_named_export_when_export_type_is_used_on_its_export_statement))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // checker.ts checkGrammarExportDeclaration
    private bool CheckGrammarExportDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var exportClause = store.ExportClause(node.Node);
        if (store.IsTypeOnly(node.Node) && !exportClause.IsNone && store.Kind(exportClause) == SyntaxKind.NamedExports)
        {
            return CheckGrammarNamedImportsOrExports(At(node, exportClause));
        }
        return false;
    }

    // checker.ts checkModuleExportName
    private void CheckModuleExportName(NodeRef name, bool allowStringLiteral = true)
    {
        if (name.IsNone || Kind(name) != SyntaxKind.StringLiteral)
        {
            return;
        }
        if (!allowStringLiteral)
        {
            GrammarErrorOnNode(name, Diagnostics.Identifier_expected);
        }
        else if (moduleKind is ModuleKind.ES2015 or ModuleKind.ES2020)
        {
            GrammarErrorOnNode(name, Diagnostics.String_literal_import_and_export_names_are_not_supported_when_the_module_flag_is_set_to_es2015_or_es2020);
        }
    }
}
