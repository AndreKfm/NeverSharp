// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (checkDecorator, nodeCanBeDecorated)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (the expression roots of C1's walk were removed in module C8).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    /// <summary>checker.ts checkDecorator (TODO(C9e): the decorator call signatures and the return type check).</summary>
    private void CheckDecorator(NodeRef node)
    {
        // checkDecorators: the decorators of a node that cannot be decorated are not checked
        var decorated = Parent(node);
        if (!NodeCanBeDecorated(compilerOptions.ExperimentalDecorators == true, decorated))
        {
            return;
        }
        // resolveDecorator: checkExpression(node.expression)
        CheckExpression(At(node, StoreOf(node).Expression(node.Node)));
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
