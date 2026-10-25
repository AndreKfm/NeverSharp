// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts (forEachReturnStatement)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Modified: translated from TypeScript to C#. Module C2 replaced module C1's minimal type printing with the node
// builder (Checker.NodeBuilder*.cs) and the symbol accessibility functions (Checker.SymbolAccessibility.cs).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // utilities.ts forEachReturnStatement (the body's return statements, not those of nested functions)
    private static void ForEachReturnStatementInBody(NodeStore store, Node body, Action<Node> visitor)
    {
        Traverse(body);

        void Traverse(Node node)
        {
            switch (store.Kind(node))
            {
                case SyntaxKind.ReturnStatement:
                    visitor(node);
                    return;
                case SyntaxKind.CaseBlock:
                case SyntaxKind.Block:
                case SyntaxKind.IfStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                case SyntaxKind.WithStatement:
                case SyntaxKind.SwitchStatement:
                case SyntaxKind.CaseClause:
                case SyntaxKind.DefaultClause:
                case SyntaxKind.LabeledStatement:
                case SyntaxKind.TryStatement:
                case SyntaxKind.CatchClause:
                    NodeVisitor.ForEachChild<object>(store, node, child =>
                    {
                        Traverse(child);
                        return null;
                    });
                    return;
            }
        }
    }
}
