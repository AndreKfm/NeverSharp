// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (checkDecorator, checkGrammarDecorator, nodeCanBeDecorated)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (the expression roots of C1's walk were removed in module C8);
// module C9e: the decorator call signatures, the return type check and checkGrammarDecorator.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    /// <summary>checker.ts checkDecorator.</summary>
    private void CheckDecorator(NodeRef node)
    {
        // checkDecorators: the decorators of a node that cannot be decorated are not checked
        var decorated = Parent(node);
        if (!NodeCanBeDecorated(compilerOptions.ExperimentalDecorators == true, decorated))
        {
            return;
        }
        CheckGrammarDecorator(node);
        var signature = GetResolvedSignature(node);
        CheckDeprecatedSignature(signature, node);
        var returnType = GetReturnTypeOfSignature(signature);
        if ((Types.Flags(returnType) & TypeFlags.Any) != 0)
        {
            return;
        }
        // if we fail to get a signature and return type here, we will have already reported a grammar error in `checkDecorators`.
        var decoratorSignature = GetDecoratorCallSignature(node);
        if (decoratorSignature.IsNone || Signatures.Record(decoratorSignature).ResolvedReturnType.IsNone)
        {
            return;
        }
        var expectedReturnType = Signatures.Record(decoratorSignature).ResolvedReturnType;
        var headMessage = Kind(decorated) switch
        {
            SyntaxKind.PropertyDeclaration when compilerOptions.ExperimentalDecorators != true => Diagnostics.Decorator_function_return_type_0_is_not_assignable_to_type_1,
            SyntaxKind.PropertyDeclaration or SyntaxKind.Parameter => Diagnostics.Decorator_function_return_type_is_0_but_is_expected_to_be_void_or_any,
            _ => Diagnostics.Decorator_function_return_type_0_is_not_assignable_to_type_1,
        };
        CheckTypeAssignableTo(returnType, expectedReturnType, At(node, StoreOf(node).Expression(node.Node)), headMessage);
    }

    // checker.ts checkGrammarDecorator
    private bool CheckGrammarDecorator(NodeRef decorator)
    {
        if (HasParseDiagnostics(decorator.File))
        {
            return false;
        }
        var store = StoreOf(decorator);
        var node = store.Expression(decorator.Node);
        // DecoratorParenthesizedExpression :
        //   `(` Expression `)`
        if (store.Kind(node) == SyntaxKind.ParenthesizedExpression)
        {
            return false;
        }
        var canHaveCallExpression = true;
        var errorNode = Node.None;
        while (true)
        {
            var kind = store.Kind(node);
            // Allow TS syntax such as non-null assertions and instantiation expressions
            if (kind is SyntaxKind.ExpressionWithTypeArguments or SyntaxKind.NonNullExpression)
            {
                node = store.Expression(node);
                continue;
            }
            // DecoratorCallExpression :
            //   DecoratorMemberExpression Arguments
            if (kind == SyntaxKind.CallExpression)
            {
                if (!canHaveCallExpression)
                {
                    errorNode = node;
                }
                if (!store.QuestionDotToken(node).IsNone)
                {
                    // Even if we already have an error node, error at the `?.` token since it appears earlier.
                    errorNode = store.QuestionDotToken(node);
                }
                node = store.Expression(node);
                canHaveCallExpression = false;
                continue;
            }
            // DecoratorMemberExpression :
            //   IdentifierReference
            //   DecoratorMemberExpression `.` IdentifierName
            //   DecoratorMemberExpression `.` PrivateIdentifier
            if (kind == SyntaxKind.PropertyAccessExpression)
            {
                if (!store.QuestionDotToken(node).IsNone)
                {
                    // Even if we already have an error node, error at the `?.` token since it appears earlier.
                    errorNode = store.QuestionDotToken(node);
                }
                node = store.Expression(node);
                canHaveCallExpression = false;
                continue;
            }
            if (kind != SyntaxKind.Identifier)
            {
                // Even if we already have an error node, error at this node since it appears earlier.
                errorNode = node;
            }
            break;
        }
        if (!errorNode.IsNone)
        {
            var diagnostic = Error(At(decorator, store.Expression(decorator.Node)), Diagnostics.Expression_must_be_enclosed_in_parentheses_to_be_used_as_a_decorator);
            AddRelatedInfo(diagnostic, CreateRelatedForNode(At(decorator, errorNode), Diagnostics.Invalid_syntax_in_decorator));
            return true;
        }
        return false;
    }

    // utilities.ts nodeCanBeDecorated (node.parent, node.parent.parent)
    private bool NodeCanBeDecorated(bool useLegacyDecorators, NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var kind = store.Kind(n);
        // private names cannot be used with decorators yet
        if (useLegacyDecorators && kind is SyntaxKind.PropertyDeclaration or SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor &&
            store.Kind(store.Name(n)) == SyntaxKind.PrivateIdentifier)
        {
            return false;
        }
        var parent = store.Parent(n);
        switch (kind)
        {
            case SyntaxKind.ClassDeclaration:
                // class declarations are valid targets
                return true;
            case SyntaxKind.ClassExpression:
                // class expressions are valid targets for native decorators
                return !useLegacyDecorators;
            case SyntaxKind.PropertyDeclaration:
                // property declarations are valid if their parent is a class declaration.
                return !parent.IsNone && (useLegacyDecorators ? store.Kind(parent) == SyntaxKind.ClassDeclaration
                    : store.Kind(parent) is SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression &&
                        !Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Abstract) && !Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Ambient));
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.MethodDeclaration:
                // if this method has a body and its parent is a class declaration, this is a valid target.
                return !store.Body(n).IsNone && !parent.IsNone &&
                    (useLegacyDecorators ? store.Kind(parent) == SyntaxKind.ClassDeclaration : store.Kind(parent) is SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression);
            case SyntaxKind.Parameter:
                // Parameter decorator support for ES decorators must wait until it is standardized
                if (!useLegacyDecorators)
                {
                    return false;
                }
                // if the parameter's parent has a body and its grandparent is a class declaration, this is a valid target.
                return !parent.IsNone && !store.Body(parent).IsNone &&
                    store.Kind(parent) is SyntaxKind.Constructor or SyntaxKind.MethodDeclaration or SyntaxKind.SetAccessor &&
                    GetThisParameter(store, parent) != n && store.Kind(store.Parent(parent)) == SyntaxKind.ClassDeclaration;
        }
        return false;
    }
}
