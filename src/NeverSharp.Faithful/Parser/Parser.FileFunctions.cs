// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/parser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/parser/parser.go and internal/parser/utilities.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>parser.ts: IncrementalParser.SyntaxCursor.</summary>
internal interface SyntaxCursor
{
    Node? CurrentNode(int position);
}

/// <summary>
/// parser.ts: the file-level functions outside namespace Parser (the parser's slice of them) and
/// IncrementalParser.createSyntaxCursor, which reparseTopLevelAwait uses.
/// </summary>
internal sealed partial class Parser
{
    /// <summary>
    /// TypeScript: `const intersectingChangeSet = new WeakSet&lt;Node&gt;();` at module level. Only the parser marks
    /// and tests nodes (reparseTopLevelAwait; incremental parsing is not ported), and the marks only matter during
    /// the parse that sets them, so the set lives in the parser instance (no mutable static state) and is cleared
    /// with the parser's state.
    /// </summary>
    private readonly HashSet<Node> intersectingChangeSet = new(ReferenceEqualityComparer.Instance);

    private bool IntersectsIncrementalChange(Node node)
    {
        return intersectingChangeSet.Contains(node);
    }

    private void MarkAsIntersectingIncrementalChange(Node node)
    {
        intersectingChangeSet.Add(node);
    }

    private static T? VisitNode<T>(Func<Node, T?> cbNode, Node? node)
    {
        return node != null ? cbNode(node) : default;
    }

    private static T? VisitNodes<T>(Func<Node, T?> cbNode, Func<NodeArray, T?>? cbNodes, NodeArray? nodes)
    {
        if (nodes != null)
        {
            if (cbNodes != null)
            {
                return cbNodes(nodes);
            }
            foreach (var node in nodes)
            {
                var result = cbNode(node);
                if (IsTruthy(result))
                {
                    return result;
                }
            }
        }
        return default;
    }

    internal static bool IsJSDocLikeText(string text, int start)
    {
        return CharCodeAt(text, start + 1) == (int)CharacterCodes.asterisk &&
            CharCodeAt(text, start + 2) == (int)CharacterCodes.asterisk &&
            CharCodeAt(text, start + 3) != (int)CharacterCodes.slash;
    }

    /// <summary>`text.charCodeAt(i)`; NaN (outside the string) is -1, which equals no character code.</summary>
    private static int CharCodeAt(string text, int index)
    {
        return (uint)index < (uint)text.Length ? text[index] : -1;
    }

    internal static Node? IsFileProbablyExternalModule(SourceFile sourceFile)
    {
        // Try to use the first top-level import/export when available, then
        // fall back to looking for an 'import.meta' somewhere in the tree if necessary.
        return Core.ForEach(sourceFile.Statements, static (node, _) => IsAnExternalModuleIndicatorNode(node)) ??
            GetImportMetaIfNecessary(sourceFile);
    }

    private static Node? IsAnExternalModuleIndicatorNode(Node node)
    {
        return FactoryUtilitiesPublic.CanHaveModifiers(node) && HasModifierOfKind(node, SyntaxKind.ExportKeyword)
                || NodeTests.IsImportEqualsDeclaration(node) && NodeTests.IsExternalModuleReference(((ImportEqualsDeclaration)node).ModuleReference)
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
        return IsImportMeta(node) ? node : NodeVisitor.ForEachChild(node, WalkTreeForImportMeta);
    }

    /// <summary>Do not use hasModifier inside the parser; it relies on parent pointers. Use this instead.</summary>
    private static bool HasModifierOfKind(Node node, SyntaxKind kind)
    {
        return Core.Some(NodeAccessors.GetModifiers(node), m => m.Kind == kind);
    }

    private static bool IsImportMeta(Node node)
    {
        return NodeTests.IsMetaProperty(node) && ((MetaProperty)node).KeywordToken == SyntaxKind.ImportKeyword && ((MetaProperty)node).Name.EscapedText is "meta";
    }

    /// <summary>The `"skip"` result of a forEachChildRecursively callback.</summary>
    internal const string ForEachChildRecursivelySkip = "skip";

    /// <summary>
    /// Invokes a callback for each child of the given node. The 'cbNode' callback is invoked for all child nodes
    /// stored in properties. If a callback returns a truthy value, iteration stops and that value is returned.
    /// (TypeScript: callbacks return `T | "skip" | undefined`; here an object, the string "skip" or null.)
    /// </summary>
    internal static object? ForEachChildRecursively(Node rootNode, Func<Node, Node, object?> cbNode, Func<NodeArray, Node, object?>? cbNodes = null)
    {
        var queue = GatherPossibleChildren(rootNode);
        var parents = new List<Node>(); // tracks parent references for elements in queue
        while (parents.Count < queue.Count)
        {
            parents.Add(rootNode);
        }
        while (queue.Count != 0)
        {
            var current = queue[^1];
            queue.RemoveAt(queue.Count - 1);
            var parent = parents[^1];
            parents.RemoveAt(parents.Count - 1);
            if (current is NodeArray currentArray)
            {
                if (cbNodes != null)
                {
                    var res = cbNodes(currentArray, parent);
                    if (IsTruthyObject(res))
                    {
                        if (res is ForEachChildRecursivelySkip)
                        {
                            continue;
                        }
                        return res;
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
                if (IsTruthyObject(res))
                {
                    if (res is ForEachChildRecursivelySkip)
                    {
                        continue;
                    }
                    return res;
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

    /// <summary>JavaScript truthiness of a callback result: undefined, false, 0 and "" are falsy.</summary>
    private static bool IsTruthyObject(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            string s => s.Length != 0,
            int i => i != 0,
            _ => true,
        };
    }

    private static List<object> GatherPossibleChildren(Node node)
    {
        var children = new List<object>();
        NodeVisitor.ForEachChild<object>(node, AddWorkItem, AddWorkItem); // By using a stack above and `unshift` here, we emulate a depth-first preorder traversal
        return children;

        object? AddWorkItem(object n)
        {
            children.Insert(0, n);
            return null;
        }
    }

    internal static bool IsDeclarationFileName(string fileName)
    {
        return GetDeclarationFileExtension(fileName) != null;
    }

    internal static string? GetDeclarationFileExtension(string fileName)
    {
        var standardExtension = TsPath.GetAnyExtensionFromPath(fileName, Utilities.SupportedDeclarationExtensions, /*ignoreCase*/ false);
        if (!string.IsNullOrEmpty(standardExtension))
        {
            return standardExtension;
        }
        if (TsPath.FileExtensionIs(fileName, ".ts"))
        {
            var baseName = TsPath.GetBaseFileName(fileName);
            var index = baseName.LastIndexOf(".d.", StringComparison.Ordinal);
            if (index >= 0)
            {
                return baseName[index..];
            }
        }
        return null;
    }

    internal static bool TagNamesAreEquivalent(Node lhs, Node rhs)
    {
        if (lhs.Kind != rhs.Kind)
        {
            return false;
        }

        if (lhs.Kind == SyntaxKind.Identifier)
        {
            return (string?)((Identifier)lhs).EscapedText == (string?)((Identifier)rhs).EscapedText;
        }

        if (lhs.Kind == SyntaxKind.ThisKeyword)
        {
            return true;
        }

        if (lhs.Kind == SyntaxKind.JsxNamespacedName)
        {
            return (string?)((JsxNamespacedName)lhs).Namespace.EscapedText == (string?)((JsxNamespacedName)rhs).Namespace.EscapedText &&
                (string?)((JsxNamespacedName)lhs).Name.EscapedText == (string?)((JsxNamespacedName)rhs).Name.EscapedText;
        }

        // If we are at this statement then we must have PropertyAccessExpression and because tag name in Jsx element can only
        // take forms of JsxTagNameExpression which includes an identifier, "this" expression, or another propertyAccessExpression
        // it is safe to case the expression property as such. See parseJsxElementName for how we parse tag name in Jsx element
        return (string?)GetEscapedTextOfMemberName(((PropertyAccessExpression)lhs).Name) == (string?)GetEscapedTextOfMemberName(((PropertyAccessExpression)rhs).Name) &&
            TagNamesAreEquivalent(((PropertyAccessExpression)lhs).Expression, ((PropertyAccessExpression)rhs).Expression);
    }

    /// <summary>`name.escapedText` of a MemberName (Identifier | PrivateIdentifier).</summary>
    private static object? GetEscapedTextOfMemberName(Node name)
    {
        return name switch
        {
            Identifier n => n.EscapedText,
            PrivateIdentifier n => n.EscapedText,
            _ => null,
        };
    }

    /// <summary>parser.ts: namespace IncrementalParser (only createSyntaxCursor, which reparseTopLevelAwait uses).</summary>
    private static class IncrementalParser
    {
        private const int InvalidPositionValue = -1;

        internal static SyntaxCursor CreateSyntaxCursor(SourceFile sourceFile)
        {
            return new IncrementalSyntaxCursor(sourceFile);
        }

        // Allows finding nodes in the source file at a certain position in an efficient manner.
        // The implementation takes advantage of the calling pattern it knows the parser will
        // make in order to optimize finding nodes as quickly as possible.
        private sealed class IncrementalSyntaxCursor : SyntaxCursor
        {
            private readonly SourceFile sourceFile;
            private NodeArray currentArray;
            private int currentArrayIndex;
            private Node? current;
            private int lastQueriedPosition = InvalidPositionValue;

            public IncrementalSyntaxCursor(SourceFile sourceFile)
            {
                this.sourceFile = sourceFile;
                currentArray = sourceFile.Statements;
                currentArrayIndex = 0;

                Debug.Assert(currentArrayIndex < currentArray.Count);
                current = currentArray[currentArrayIndex];
            }

            public Node? CurrentNode(int position)
            {
                // Only compute the current node if the position is different than the last time
                // we were asked.  The parser commonly asks for the node at the same position
                // twice.  Once to know if can read an appropriate list element at a certain point,
                // and then to actually read and consume the node.
                if (position != lastQueriedPosition)
                {
                    // Much of the time the parser will need the very next node in the array that
                    // we just returned a node from.So just simply check for that case and move
                    // forward in the array instead of searching for the node again.
                    if (current != null && current.End == position && currentArrayIndex < (currentArray.Count - 1))
                    {
                        currentArrayIndex++;
                        current = currentArray[currentArrayIndex];
                    }

                    // If we don't have a node, or the node we have isn't in the right position,
                    // then try to find a viable node at the position requested.
                    if (current == null || current.Pos != position)
                    {
                        FindHighestListElementThatStartsAtPosition(position);
                    }
                }

                // Cache this query so that we don't do any extra work if the parser calls back
                // into us.  Note: this is very common as the parser will make pairs of calls like
                // 'isListElement -> parseListElement'.  If we were unable to find a node when
                // called with 'isListElement', we don't want to redo the work when parseListElement
                // is called immediately after.
                lastQueriedPosition = position;

                // Either we don'd have a node, or we have a node at the position being asked for.
                Debug.Assert(current == null || current.Pos == position);
                return current;
            }

            // Finds the highest element in the tree we can find that starts at the provided position.
            // The element must be a direct child of some node list in the tree.  This way after we
            // return it, we can easily return its next sibling in the list.
            private void FindHighestListElementThatStartsAtPosition(int position)
            {
                // Clear out any cached state about the last node we found.
                currentArray = null!;
                currentArrayIndex = InvalidPositionValue;
                current = null;

                // Recurse into the source file to find the highest node at this position.
                NodeVisitor.ForEachChild<object>(sourceFile, VisitNode, VisitArray);
                return;

                object? VisitNode(Node node)
                {
                    if (position >= node.Pos && position < node.End)
                    {
                        // Position was within this node.  Keep searching deeper to find the node.
                        NodeVisitor.ForEachChild<object>(node, VisitNode, VisitArray);

                        // don't proceed any further in the search.
                        return true;
                    }

                    // position wasn't in this node, have to keep searching.
                    return null;
                }

                object? VisitArray(NodeArray array)
                {
                    if (position >= array.Pos && position < array.End)
                    {
                        // position was in this array.  Search through this array to see if we find a
                        // viable element.
                        for (var i = 0; i < array.Count; i++)
                        {
                            var child = array[i];
                            if (child != null)
                            {
                                if (child.Pos == position)
                                {
                                    // Found the right node.  We're done.
                                    currentArray = array;
                                    currentArrayIndex = i;
                                    current = child;
                                    return true;
                                }
                                else
                                {
                                    if (child.Pos < position && position < child.End)
                                    {
                                        // Position in somewhere within this child.  Search in it and
                                        // stop searching in this array.
                                        NodeVisitor.ForEachChild<object>(child, VisitNode, VisitArray);
                                        return true;
                                    }
                                }
                            }
                        }
                    }

                    // position wasn't in this array, have to keep searching.
                    return null;
                }
            }
        }
    }
}
