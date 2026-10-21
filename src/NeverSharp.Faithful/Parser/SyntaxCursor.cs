// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/parser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/parser/parser.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Runtime.CompilerServices;

namespace NeverSharp.Faithful;

/// <summary>
/// parser.ts IncrementalParser.SyntaxCursor: allows finding nodes in the source file at a certain position in an
/// efficient manner. (TypeScript: an object literal `{ currentNode(position) { ... } }`.)
/// </summary>
internal sealed class SyntaxCursor
{
    private readonly Func<int, Node?> currentNode;

    internal SyntaxCursor(Func<int, Node?> currentNode)
    {
        this.currentNode = currentNode;
    }

    internal Node? CurrentNode(int position)
    {
        return currentNode(position);
    }
}

// parser.ts lines 9936-9944: file-level functions of parser.ts that the parser (the main grammar and the
// incremental parser) call, so they are static members of Parser.
internal sealed partial class Parser
{
    // TypeScript: `const intersectingChangeSet = new WeakSet<Node | NodeArray<Node>>();`
    // TypeScript's module-level WeakSet is per-parser instance state here (no mutable static state); reference equality,
    // cleared in ClearState. Entries are only ever added by the incremental parser, which Phase 1 does not run.
    private readonly HashSet<object> intersectingChangeSet = new(ReferenceEqualityComparer.Instance);

    internal bool IntersectsIncrementalChange(Node node)
    {
        return intersectingChangeSet.Contains(node);
    }

    internal bool IntersectsIncrementalChange(NodeArray nodes)
    {
        return intersectingChangeSet.Contains(nodes);
    }

    internal void MarkAsIntersectingIncrementalChange(Node node)
    {
        intersectingChangeSet.Add(node);
    }

    internal void MarkAsIntersectingIncrementalChange(NodeArray nodes)
    {
        intersectingChangeSet.Add(nodes);
    }
}

internal static partial class IncrementalParser
{
    // `const enum InvalidPosition { Value = -1 }`
    private const int InvalidPositionValue = -1;

    internal static SyntaxCursor CreateSyntaxCursor(SourceFile sourceFile)
    {
        NodeArray? currentArray = sourceFile.Statements;
        var currentArrayIndex = 0;

        Debug.Assert(currentArrayIndex < currentArray.Count);
        Node? current = currentArray[currentArrayIndex];
        var lastQueriedPosition = InvalidPositionValue;

        return new SyntaxCursor(CurrentNode);

        Node? CurrentNode(int position)
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
                if (current is not null && current.End == position && currentArrayIndex < (currentArray!.Count - 1))
                {
                    currentArrayIndex++;
                    current = currentArray![currentArrayIndex];
                }

                // If we don't have a node, or the node we have isn't in the right position,
                // then try to find a viable node at the position requested.
                if (current is null || current.Pos != position)
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
            Debug.Assert(current is null || current.Pos == position);
            return current;
        }

        // Finds the highest element in the tree we can find that starts at the provided position.
        // The element must be a direct child of some node list in the tree.  This way after we
        // return it, we can easily return its next sibling in the list.
        void FindHighestListElementThatStartsAtPosition(int position)
        {
            // Clear out any cached state about the last node we found.
            currentArray = null;
            currentArrayIndex = InvalidPositionValue;
            current = null;

            // Recurse into the source file to find the highest node at this position.
            NodeVisitor.ForEachChild<object>(sourceFile, VisitNode, VisitArray);
            return;

            // The TypeScript callbacks return true/false (stop/continue); forEachChild stops at the first truthy
            // result, which is a non-null object here (the node or array itself).
            object? VisitNode(Node node)
            {
                if (position >= node.Pos && position < node.End)
                {
                    // Position was within this node.  Keep searching deeper to find the node.
                    NodeVisitor.ForEachChild<object>(node, VisitNode, VisitArray);

                    // don't proceed any further in the search.
                    return node;
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
                        if (child.Pos == position)
                        {
                            // Found the right node.  We're done.
                            currentArray = array;
                            currentArrayIndex = i;
                            current = child;
                            return array;
                        }
                        else
                        {
                            if (child.Pos < position && position < child.End)
                            {
                                // Position in somewhere within this child.  Search in it and
                                // stop searching in this array.
                                NodeVisitor.ForEachChild<object>(child, VisitNode, VisitArray);
                                return array;
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
