// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/binder.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/binder/binder.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; the strict-mode and reserved-word checks of the binder (the JavaScript
// special assignments and the delayed JSDoc typedef binding of module B3 are in Binder.JavaScript.cs).

using System.Collections.Frozen;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Binder
{
    // utilitiesPublic.ts identifierToKeywordKind: textToKeyword looked up by the identifier's characters.
    private static readonly FrozenDictionary<string, SyntaxKind> Keywords = Scanner.TextToKeywordObj.ToFrozenDictionary(StringComparer.Ordinal);
    private static readonly FrozenDictionary<string, SyntaxKind>.AlternateLookup<ReadOnlySpan<char>> KeywordsBySpan = Keywords.GetAlternateLookup<ReadOnlySpan<char>>();

    // ---- strict mode and reserved words ----

    // The binder visits every node in the syntax tree so it is a convenient place to perform a single localized
    // check for reserved words used as identifiers in strict mode code, as well as `yield` or `await` in
    // [Yield] or [Await] contexts, respectively.
    private void CheckContextualIdentifier(Node node)
    {
        // Report error only if there are no parse errors in file
        var flags = store.Flags(node);
        if (
            store.ParseDiagnostics(file).Count == 0 &&
            (flags & NodeFlags.Ambient) == 0 &&
            (flags & NodeFlags.JSDoc) == 0 &&
            !IsIdentifierName(node))
        {
            // strict mode identifiers
            var originalKeywordKind = IdentifierToKeywordKind(node);
            if (originalKeywordKind == SyntaxKind.Unknown)
            {
                return;
            }

            if (
                inStrictMode &&
                originalKeywordKind >= SyntaxKind.FirstFutureReservedWord &&
                originalKeywordKind <= SyntaxKind.LastFutureReservedWord)
            {
                b.BindDiagnostics.Add(CreateDiagnosticForNode(node, GetStrictModeIdentifierMessage(node), Utilities.DeclarationNameToString(store, node)));
            }
            else if (originalKeywordKind == SyntaxKind.AwaitKeyword)
            {
                if (Utilities.IsExternalModule(store, file) && IsInTopLevelContext(node))
                {
                    b.BindDiagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.Identifier_expected_0_is_a_reserved_word_at_the_top_level_of_a_module, Utilities.DeclarationNameToString(store, node)));
                }
                else if ((flags & NodeFlags.AwaitContext) != 0)
                {
                    b.BindDiagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here, Utilities.DeclarationNameToString(store, node)));
                }
            }
            else if (originalKeywordKind == SyntaxKind.YieldKeyword && (flags & NodeFlags.YieldContext) != 0)
            {
                b.BindDiagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here, Utilities.DeclarationNameToString(store, node)));
            }
        }
    }

    // utilitiesPublic.ts identifierToKeywordKind (Unknown for undefined); `this` has no escapedText.
    private SyntaxKind IdentifierToKeywordKind(Node node)
    {
        if (store.Kind(node) != SyntaxKind.Identifier)
        {
            return SyntaxKind.Unknown;
        }
        var text = texts.Span(store.EscapedText(node));
        return text.Length >= 2 && KeywordsBySpan.TryGetValue(text, out var kind) && Utilities.IsKeyword(kind) ? kind : SyntaxKind.Unknown;
    }

    // utilities.ts isIdentifierName.
    private bool IsIdentifierName(Node node)
    {
        var nodeParent = store.Parent(node);
        switch (store.Kind(nodeParent))
        {
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.EnumMember:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.PropertyAccessExpression:
                // Name in member declaration or property name in property access
                return store.Name(nodeParent) == node;
            case SyntaxKind.QualifiedName:
                // Name on right hand side of dot in a type query or type reference
                return store.Right(nodeParent) == node;
            case SyntaxKind.BindingElement:
            case SyntaxKind.ImportSpecifier:
                // Property name in binding element or import specifier
                return store.PropertyName(nodeParent) == node;
            case SyntaxKind.ExportSpecifier:
            case SyntaxKind.JsxAttribute:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxOpeningElement:
            case SyntaxKind.JsxClosingElement:
                // Any name in an export specifier or JSX Attribute or Jsx Element
                return true;
        }
        return false;
    }

    // utilities.ts isInTopLevelContext.
    private bool IsInTopLevelContext(Node node)
    {
        // The name of a class or function declaration is a BindingIdentifier in its surrounding scope.
        var nodeParent = store.Parent(node);
        if (store.Kind(node) == SyntaxKind.Identifier && (store.Kind(nodeParent) == SyntaxKind.ClassDeclaration || store.Kind(nodeParent) == SyntaxKind.FunctionDeclaration) && store.Name(nodeParent) == node)
        {
            node = nodeParent;
        }
        var thisContainer = GetThisContainer(node, /*includeArrowFunctions*/ true, /*includeClassComputedPropertyName*/ false);
        return store.Kind(thisContainer) == SyntaxKind.SourceFile;
    }

    // utilities.ts getThisContainer.
    private Node GetThisContainer(Node node, bool includeArrowFunctions, bool includeClassComputedPropertyName)
    {
        Debug.Assert(store.Kind(node) != SyntaxKind.SourceFile);
        while (true)
        {
            node = store.Parent(node);
            if (node.IsNone)
            {
                Debug.Fail(); // If we never pass in a SourceFile, this should be unreachable, since we'll stop when we reach that.
                return default;
            }
            switch (store.Kind(node))
            {
                case SyntaxKind.ComputedPropertyName:
                    // If the grandparent node is an object literal (as opposed to a class),
                    // then the computed property is not a 'this' container.
                    // A computed property name in a class needs to be a this container
                    // so that we can error on it.
                    if (includeClassComputedPropertyName && Utilities.IsClassLike(store, store.Parent(store.Parent(node))))
                    {
                        return node;
                    }
                    // If this is a computed property, then the parent should not
                    // make it a this container. The parent might be a property
                    // in an object literal, like a method or accessor. But in order for
                    // such a parent to be a this container, the reference must be in
                    // the *body* of the container.
                    node = store.Parent(store.Parent(node));
                    break;
                case SyntaxKind.Decorator:
                    // Decorators are always applied outside of the body of a class or method.
                    var decoratorParent = store.Parent(node);
                    if (store.Kind(decoratorParent) == SyntaxKind.Parameter && Utilities.IsClassElement(store, store.Parent(decoratorParent)))
                    {
                        // If the decorator's parent is a Parameter, we resolve the this container from
                        // the grandparent class declaration.
                        node = store.Parent(decoratorParent);
                    }
                    else if (Utilities.IsClassElement(store, decoratorParent))
                    {
                        // If the decorator's parent is a class element, we resolve the 'this' container
                        // from the parent class declaration.
                        node = decoratorParent;
                    }
                    break;
                case SyntaxKind.ArrowFunction:
                    if (!includeArrowFunctions)
                    {
                        continue;
                    }
                    return node;
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.ClassStaticBlockDeclaration:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.Constructor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.CallSignature:
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.SourceFile:
                    return node;
            }
        }
    }

    private DiagnosticMessage GetStrictModeIdentifierMessage(Node node)
    {
        // Provide specialized messages to help the user understand why we think they're in
        // strict mode.
        if (!Utilities.GetContainingClass(store, node).IsNone)
        {
            return Diagnostics.Identifier_expected_0_is_a_reserved_word_in_strict_mode_Class_definitions_are_automatically_in_strict_mode;
        }

        if (store.ExternalModuleIndicator(file) is not null)
        {
            return Diagnostics.Identifier_expected_0_is_a_reserved_word_in_strict_mode_Modules_are_automatically_in_strict_mode;
        }

        return Diagnostics.Identifier_expected_0_is_a_reserved_word_in_strict_mode;
    }

    // The binder visits every node, so this is a good place to check for
    // the reserved private name (there is only one)
    private void CheckPrivateIdentifier(Node node)
    {
        if (texts.Equals(store.EscapedText(node), "#constructor"))
        {
            // Report error only if there are no parse errors in file
            if (store.ParseDiagnostics(file).Count == 0)
            {
                b.BindDiagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.constructor_is_a_reserved_word, Utilities.DeclarationNameToString(store, node)));
            }
        }
    }

    private void CheckStrictModeBinaryExpression(Node node)
    {
        if (inStrictMode && UtilitiesPublic.IsLeftHandSideExpression(store, store.Left(node)) && Utilities.IsAssignmentOperator(store.Kind(store.OperatorToken(node))))
        {
            // ECMA 262 (Annex C) The identifier eval or arguments may not appear as the LeftHandSideExpression of an
            // Assignment operator(11.13) or of a PostfixExpression(11.3)
            CheckStrictModeEvalOrArguments(node, store.Left(node));
        }
    }

    private void CheckStrictModeCatchClause(Node node)
    {
        // It is a SyntaxError if a TryStatement with a Catch occurs within strict code and the Identifier of the
        // Catch production is eval or arguments
        var variableDeclaration = store.VariableDeclaration(node);
        if (inStrictMode && !variableDeclaration.IsNone)
        {
            CheckStrictModeEvalOrArguments(node, store.Name(variableDeclaration));
        }
    }

    private void CheckStrictModeDeleteExpression(Node node)
    {
        // Grammar checking
        if (inStrictMode && store.Kind(store.Expression(node)) == SyntaxKind.Identifier)
        {
            // When a delete operator occurs within strict mode code, a SyntaxError is thrown if its
            // UnaryExpression is a direct reference to a variable, function argument, or function name
            var span = Utilities.GetErrorSpanForNode(store, file, store.Expression(node));
            b.BindDiagnostics.Add(Utilities.CreateFileDiagnostic(store, file, span.Start, span.Length, Diagnostics.delete_cannot_be_called_on_an_identifier_in_strict_mode));
        }
    }

    private bool IsEvalOrArgumentsIdentifier(Node node)
    {
        return store.Kind(node) == SyntaxKind.Identifier && (texts.Equals(store.EscapedText(node), "eval") || texts.Equals(store.EscapedText(node), "arguments"));
    }

    private void CheckStrictModeEvalOrArguments(Node contextNode, Node name)
    {
        if (!name.IsNone && store.Kind(name) == SyntaxKind.Identifier)
        {
            if (IsEvalOrArgumentsIdentifier(name))
            {
                // We check first if the name is inside class declaration or class expression; if so give explicit message
                // otherwise report generic error message.
                var span = Utilities.GetErrorSpanForNode(store, file, name);
                b.BindDiagnostics.Add(Utilities.CreateFileDiagnostic(store, file, span.Start, span.Length, GetStrictModeEvalOrArgumentsMessage(contextNode), texts.GetString(UtilitiesPublic.IdText(store, name))));
            }
        }
    }

    private DiagnosticMessage GetStrictModeEvalOrArgumentsMessage(Node node)
    {
        // Provide specialized messages to help the user understand why we think they're in
        // strict mode.
        if (!Utilities.GetContainingClass(store, node).IsNone)
        {
            return Diagnostics.Code_contained_in_a_class_is_evaluated_in_JavaScript_s_strict_mode_which_does_not_allow_this_use_of_0_For_more_information_see_https_Colon_Slash_Slashdeveloper_mozilla_org_Slashen_US_Slashdocs_SlashWeb_SlashJavaScript_SlashReference_SlashStrict_mode;
        }

        if (store.ExternalModuleIndicator(file) is not null)
        {
            return Diagnostics.Invalid_use_of_0_Modules_are_automatically_in_strict_mode;
        }

        return Diagnostics.Invalid_use_of_0_in_strict_mode;
    }

    private void CheckStrictModeFunctionName(Node node)
    {
        if (inStrictMode && (store.Flags(node) & NodeFlags.Ambient) == 0)
        {
            // It is a SyntaxError if the identifier eval or arguments appears within a FormalParameterList of a strict mode FunctionDeclaration or FunctionExpression (13.1))
            CheckStrictModeEvalOrArguments(node, store.Name(node));
        }
    }

    private DiagnosticMessage GetStrictModeBlockScopeFunctionDeclarationMessage(Node node)
    {
        // Provide specialized messages to help the user understand why we think they're in
        // strict mode.
        if (!Utilities.GetContainingClass(store, node).IsNone)
        {
            return Diagnostics.Function_declarations_are_not_allowed_inside_blocks_in_strict_mode_when_targeting_ES5_Class_definitions_are_automatically_in_strict_mode;
        }

        if (store.ExternalModuleIndicator(file) is not null)
        {
            return Diagnostics.Function_declarations_are_not_allowed_inside_blocks_in_strict_mode_when_targeting_ES5_Modules_are_automatically_in_strict_mode;
        }

        return Diagnostics.Function_declarations_are_not_allowed_inside_blocks_in_strict_mode_when_targeting_ES5;
    }

    private void CheckStrictModeFunctionDeclaration(Node node)
    {
        if (languageVersion < ScriptTarget.ES2015)
        {
            // Report error if function is not top level function declaration
            if (
                store.Kind(blockScopeContainer) != SyntaxKind.SourceFile &&
                store.Kind(blockScopeContainer) != SyntaxKind.ModuleDeclaration &&
                !Utilities.IsFunctionLikeOrClassStaticBlockDeclaration(store, blockScopeContainer))
            {
                // We check first if the name is inside class declaration or class expression; if so give explicit message
                // otherwise report generic error message.
                var errorSpan = Utilities.GetErrorSpanForNode(store, file, node);
                b.BindDiagnostics.Add(Utilities.CreateFileDiagnostic(store, file, errorSpan.Start, errorSpan.Length, GetStrictModeBlockScopeFunctionDeclarationMessage(node)));
            }
        }
    }

    private void CheckStrictModePostfixUnaryExpression(Node node)
    {
        // Grammar checking
        // The identifier eval or arguments may not appear as the LeftHandSideExpression of an
        // Assignment operator(11.13) or of a PostfixExpression(11.3) or as the UnaryExpression
        // operated upon by a Prefix Increment(11.4.4) or a Prefix Decrement(11.4.5) operator.
        if (inStrictMode)
        {
            CheckStrictModeEvalOrArguments(node, store.Operand(node));
        }
    }

    private void CheckStrictModePrefixUnaryExpression(Node node)
    {
        // Grammar checking
        if (inStrictMode)
        {
            if (store.Operator(node) == SyntaxKind.PlusPlusToken || store.Operator(node) == SyntaxKind.MinusMinusToken)
            {
                CheckStrictModeEvalOrArguments(node, store.Operand(node));
            }
        }
    }

    private void CheckStrictModeWithStatement(Node node)
    {
        // Grammar checking for withStatement
        if (inStrictMode)
        {
            ErrorOnFirstToken(node, Diagnostics.with_statements_are_not_allowed_in_strict_mode);
        }
    }

    private void CheckStrictModeLabeledStatement(Node node)
    {
        // Grammar checking for labeledStatement
        if (inStrictMode && languageVersion >= ScriptTarget.ES2015)
        {
            var statement = store.Statement(node);
            if (IsDeclarationStatementKind(store.Kind(statement)) || store.Kind(statement) == SyntaxKind.VariableStatement)
            {
                ErrorOnFirstToken(store.Label(node), Diagnostics.A_label_is_not_allowed_here);
            }
        }
    }

    // utilitiesPublic.ts isDeclarationStatementKind.
    private static bool IsDeclarationStatementKind(SyntaxKind kind)
    {
        return kind == SyntaxKind.FunctionDeclaration
            || kind == SyntaxKind.MissingDeclaration
            || kind == SyntaxKind.ClassDeclaration
            || kind == SyntaxKind.InterfaceDeclaration
            || kind == SyntaxKind.TypeAliasDeclaration
            || kind == SyntaxKind.EnumDeclaration
            || kind == SyntaxKind.ModuleDeclaration
            || kind == SyntaxKind.ImportDeclaration
            || kind == SyntaxKind.ImportEqualsDeclaration
            || kind == SyntaxKind.ExportDeclaration
            || kind == SyntaxKind.ExportAssignment
            || kind == SyntaxKind.NamespaceExportDeclaration;
    }
}
