// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/parser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/parser/parser.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

// The file-level (static) functions of parser.ts that lie outside `namespace Parser`: visitNode, visitNodes,
// isJSDocLikeText, the external-module indicator functions, forEachChildRecursively, setExternalModuleIndicator and
// isExternalModule.
internal sealed partial class Parser
{
    // parser.ts visitNode (the generated NodeVisitor has its own private copy used by the generated ForEachChild).
    internal static T? VisitNode<T>(Func<Node, T?> cbNode, Node? node)
        where T : class
    {
        return node is null ? null : cbNode(node);
    }

    // parser.ts visitNodes
    internal static T? VisitNodes<T>(Func<Node, T?> cbNode, Func<NodeArray, T?>? cbNodes, NodeArray? nodes)
        where T : class
    {
        if (nodes is not null)
        {
            if (cbNodes is not null)
            {
                return cbNodes(nodes);
            }
            foreach (var node in nodes)
            {
                var result = cbNode(node);
                if (result is not null)
                {
                    return result;
                }
            }
        }
        return null;
    }

    // `text.charCodeAt(i)`: NaN (represented by -1, which equals no character code) when `i` is out of range.
    private static int CharCodeAtOrNaN(string text, int i)
    {
        return i >= 0 && i < text.Length ? text[i] : -1;
    }

    internal static bool IsJSDocLikeText(string text, int start)
    {
        return CharCodeAtOrNaN(text, start + 1) == (int)CharacterCodes.asterisk &&
            CharCodeAtOrNaN(text, start + 2) == (int)CharacterCodes.asterisk &&
            CharCodeAtOrNaN(text, start + 3) != (int)CharacterCodes.slash;
    }

    internal static Node? IsFileProbablyExternalModule(SourceFile sourceFile)
    {
        // Try to use the first top-level import/export when available, then
        // fall back to looking for an 'import.meta' somewhere in the tree if necessary.
        // (TypeScript: `forEach(sourceFile.statements, isAnExternalModuleIndicatorNode) || getImportMetaIfNecessary(sourceFile)`.)
        var statements = sourceFile.Statements;
        for (var i = 0; i < statements.Count; i++)
        {
            var result = IsAnExternalModuleIndicatorNode(statements[i]);
            if (result is not null)
            {
                return result;
            }
        }
        return GetImportMetaIfNecessary(sourceFile);
    }

    private static Node? IsAnExternalModuleIndicatorNode(Node node)
    {
        return (FactoryUtilitiesPublic.CanHaveModifiers(node) && HasModifierOfKind(node, SyntaxKind.ExportKeyword))
            || (NodeTests.IsImportEqualsDeclaration(node) && NodeTests.IsExternalModuleReference(((ImportEqualsDeclaration)node).ModuleReference))
            || NodeTests.IsImportDeclaration(node)
            || NodeTests.IsExportAssignment(node)
            || NodeTests.IsExportDeclaration(node) ? node : null;
    }

    private static Node? GetImportMetaIfNecessary(SourceFile sourceFile)
    {
        return (sourceFile.Flags & NodeFlags.PossiblyContainsImportMeta) != 0 ?
            WalkTreeForImportMeta(sourceFile) :
            null;
    }

    private static Node? WalkTreeForImportMeta(Node node)
    {
        return IsImportMeta(node) ? node : NodeVisitor.ForEachChild<Node>(node, WalkTreeForImportMeta);
    }

    /// <summary>Do not use hasModifier inside the parser; it relies on parent pointers. Use this instead.</summary>
    private static bool HasModifierOfKind(Node node, SyntaxKind kind)
    {
        // TypeScript: `some(node.modifiers, m => m.kind === kind)`; `node` is a HasModifiers (see CanHaveModifiers).
        var modifiers = GetModifiersOfHasModifiers(node);
        if (modifiers is not null)
        {
            for (var i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].Kind == kind)
                {
                    return true;
                }
            }
        }
        return false;
    }

    // `node.modifiers` of a HasModifiers node (the union of the 25 kinds of CanHaveModifiers): the classes of the
    // generated nodes declare the field separately, so it is read by kind.
    private static NodeArray? GetModifiersOfHasModifiers(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.TypeParameter:
                return ((TypeParameterDeclaration)node).Modifiers;
            case SyntaxKind.Parameter:
                return ((ParameterDeclaration)node).Modifiers;
            case SyntaxKind.PropertySignature:
                return ((PropertySignature)node).Modifiers;
            case SyntaxKind.PropertyDeclaration:
                return ((PropertyDeclaration)node).Modifiers;
            case SyntaxKind.MethodSignature:
                return ((MethodSignature)node).Modifiers;
            case SyntaxKind.MethodDeclaration:
                return ((MethodDeclaration)node).Modifiers;
            case SyntaxKind.Constructor:
                return ((ConstructorDeclaration)node).Modifiers;
            case SyntaxKind.GetAccessor:
                return ((GetAccessorDeclaration)node).Modifiers;
            case SyntaxKind.SetAccessor:
                return ((SetAccessorDeclaration)node).Modifiers;
            case SyntaxKind.IndexSignature:
                return ((IndexSignatureDeclaration)node).Modifiers;
            case SyntaxKind.ConstructorType:
                return ((ConstructorTypeNode)node).Modifiers;
            case SyntaxKind.FunctionExpression:
                return ((FunctionExpression)node).Modifiers;
            case SyntaxKind.ArrowFunction:
                return ((ArrowFunction)node).Modifiers;
            case SyntaxKind.ClassExpression:
                return ((ClassExpression)node).Modifiers;
            case SyntaxKind.VariableStatement:
                return ((VariableStatement)node).Modifiers;
            case SyntaxKind.FunctionDeclaration:
                return ((FunctionDeclaration)node).Modifiers;
            case SyntaxKind.ClassDeclaration:
                return ((ClassDeclaration)node).Modifiers;
            case SyntaxKind.InterfaceDeclaration:
                return ((InterfaceDeclaration)node).Modifiers;
            case SyntaxKind.TypeAliasDeclaration:
                return ((TypeAliasDeclaration)node).Modifiers;
            case SyntaxKind.EnumDeclaration:
                return ((EnumDeclaration)node).Modifiers;
            case SyntaxKind.ModuleDeclaration:
                return ((ModuleDeclaration)node).Modifiers;
            case SyntaxKind.ImportEqualsDeclaration:
                return ((ImportEqualsDeclaration)node).Modifiers;
            case SyntaxKind.ImportDeclaration:
                return ((ImportDeclaration)node).Modifiers;
            case SyntaxKind.ExportAssignment:
                return ((ExportAssignment)node).Modifiers;
            case SyntaxKind.ExportDeclaration:
                return ((ExportDeclaration)node).Modifiers;
            default:
                Debug.Fail("Unexpected node kind in HasModifiers: " + node.Kind);
                return null;
        }
    }

    private static bool IsImportMeta(Node node)
    {
        return NodeTests.IsMetaProperty(node)
            && ((MetaProperty)node).KeywordToken == SyntaxKind.ImportKeyword
            && ((MetaProperty)node).Name.EscapedText is string escapedText
            && escapedText == "meta";
    }

    /// <summary>
    /// Invokes a callback for each child of the given node. The 'cbNode' callback is invoked for all child nodes
    /// stored in properties. If a callback returns a truthy value, iteration stops and that value is returned.
    /// Otherwise, returns undefined.
    /// Unlike `forEachChild`, `forEachChildRecursively` handles recursively invoking the traversal on each child node found,
    /// and while doing so, handles traversing the structure without relying on the callstack to encode the tree structure.
    /// </summary>
    /// <remarks>
    /// The callbacks return null (TypeScript: undefined, continue), the string "skip" (do not descend into the node or
    /// array), or a result, which must be a <typeparamref name="T"/>; it is returned at once.
    /// </remarks>
    internal static T? ForEachChildRecursively<T>(Node rootNode, Func<Node, Node, object?> cbNode, Func<NodeArray, Node, object?>? cbNodes = null)
        where T : class
    {
        // The queue holds Node or NodeArray items.
        var queue = GatherPossibleChildren(rootNode);
        var parents = new List<Node>(); // tracks parent references for elements in queue
        while (parents.Count < queue.Count)
        {
            parents.Add(rootNode);
        }
        while (queue.Count != 0)
        {
            var current = queue[queue.Count - 1];
            queue.RemoveAt(queue.Count - 1);
            var parent = parents[parents.Count - 1];
            parents.RemoveAt(parents.Count - 1);
            if (current is NodeArray currentArray)
            {
                if (cbNodes is not null)
                {
                    var res = cbNodes(currentArray, parent);
                    if (res is not null)
                    {
                        if (res is "skip")
                        {
                            continue;
                        }
                        return (T)res;
                    }
                }
                for (var i = currentArray.Count - 1; i >= 0; --i)
                {
                    queue.Add(currentArray[i]);
                    parents.Add(parent);
                }
            }
            else
            {
                var currentNode = (Node)current;
                var res = cbNode(currentNode, parent);
                if (res is not null)
                {
                    if (res is "skip")
                    {
                        continue;
                    }
                    return (T)res;
                }
                if (currentNode.Kind >= SyntaxKind.FirstNode)
                {
                    // add children in reverse order to the queue, so popping gives the first child
                    foreach (var child in GatherPossibleChildren(currentNode))
                    {
                        queue.Add(child);
                        parents.Add(currentNode);
                    }
                }
            }
        }
        return null;
    }

    // Returns the children (Node or NodeArray items).
    private static List<object> GatherPossibleChildren(Node node)
    {
        var children = new List<object>();
        NodeVisitor.ForEachChild<object>(
            node,
            n =>
            {
                children.Insert(0, n);
                return null;
            },
            n =>
            {
                children.Insert(0, n);
                return null;
            }); // By using a stack above and `unshift` here, we emulate a depth-first preorder traversal
        return children;
    }

    internal static void SetExternalModuleIndicator(SourceFile sourceFile)
    {
        sourceFile.ExternalModuleIndicator = IsFileProbablyExternalModule(sourceFile);
    }

    // See also `isExternalOrCommonJsModule` in utilities.ts
    internal static bool IsExternalModule(SourceFile file)
    {
        return file.ExternalModuleIndicator is not null;
    }
}
