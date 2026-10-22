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

namespace NeverSharp.Optimized;

/// <summary>
/// parser.ts: IncrementalParser.SyntaxCursor. Allows finding nodes in the source file at a certain position in an
/// efficient manner. (TypeScript: an object with the method currentNode; here a field holding the function.)
/// </summary>
internal sealed class SyntaxCursor
{
    public required Func<int, Node> CurrentNode;
}

/// <summary>
/// parser.ts: Parser.reparseTopLevelAwait, the incremental-change marks it uses, and IncrementalParser.createSyntaxCursor,
/// which it needs to reuse the statements that contain no `await`.
/// </summary>
internal sealed partial class Parser
{
    // TypeScript: `const intersectingChangeSet = new WeakSet<Node | NodeArray<Node>>()`, a module-level set. Only
    // reparseTopLevelAwait marks nodes, and only the parser's currentNode asks, while that reparse runs with its syntax
    // cursor; a set per parser instance, emptied when the reparse ends, answers every query as the module-level set
    // does (nodes of other parses are never asked about), without keeping nodes alive or sharing state across threads.
    // (Track O: only nodes are marked; a node handle is its identity within the parse.)
    private readonly HashSet<Node> intersectingChangeSet = [];

    // A non-null result for NodeVisitor.ForEachChild: `return true` from a visitor ends the iteration.
    private static readonly object StopVisiting = new();

    private bool IntersectsIncrementalChange(Node node)
    {
        return intersectingChangeSet.Contains(node);
    }

    private void MarkAsIntersectingIncrementalChange(Node node)
    {
        intersectingChangeSet.Add(node);
    }

    private Node ReparseTopLevelAwait(Node sourceFile)
    {
        var savedSyntaxCursor = syntaxCursor;
        var baseSyntaxCursor = CreateSyntaxCursor(store, sourceFile);
        syntaxCursor = new SyntaxCursor { CurrentNode = ReparseCurrentNode };

        var statements = store.BeginList();
        var savedParseDiagnostics = parseDiagnostics;

        parseDiagnostics = [];

        var pos = 0;
        var start = FindNextStatementWithAwait(store.Statements(sourceFile), 0);
        while (start != -1)
        {
            // append all statements between pos and start
            var prevStatement = store.Element(store.Statements(sourceFile), pos);
            var nextStatement = store.Element(store.Statements(sourceFile), start);
            foreach (var statement in store.Nodes(store.Statements(sourceFile))[pos..start])
            {
                store.Push(statements, statement);
            }
            pos = FindNextStatementWithoutAwait(store.Statements(sourceFile), start);

            // append all diagnostics associated with the copied range
            var diagnosticStart = Core.FindIndex(savedParseDiagnostics, (diagnostic, _) => diagnostic.Start >= store.Pos(prevStatement));
            var diagnosticEnd = diagnosticStart >= 0 ? Core.FindIndex(savedParseDiagnostics, (diagnostic, _) => diagnostic.Start >= store.Pos(nextStatement), diagnosticStart) : -1;
            if (diagnosticStart >= 0)
            {
                Core.AddRange(parseDiagnostics, savedParseDiagnostics, diagnosticStart, diagnosticEnd >= 0 ? diagnosticEnd : null);
            }

            // reparse all statements between start and pos. We skip existing diagnostics for the same range and allow the parser to generate new ones.
            SpeculationHelper(p =>
            {
                var savedContextFlags = contextFlags;
                contextFlags |= NodeFlags.AwaitContext;
                scanner.ResetTokenState(store.Pos(nextStatement));
                NextToken();

                while (Token() != SyntaxKind.EndOfFileToken)
                {
                    var startPos = scanner.GetTokenFullStart();
                    var statement = ParseListElement(ParsingContext.SourceElements, static p => p.ParseStatement());
                    store.Push(statements, statement);
                    if (startPos == scanner.GetTokenFullStart())
                    {
                        NextToken();
                    }

                    if (pos >= 0)
                    {
                        var nonAwaitStatement = store.Element(store.Statements(sourceFile), pos);
                        if (store.End(statement) == store.Pos(nonAwaitStatement))
                        {
                            // done reparsing this section
                            break;
                        }
                        if (store.End(statement) > store.Pos(nonAwaitStatement))
                        {
                            // we ate into the next statement, so we must reparse it.
                            pos = FindNextStatementWithoutAwait(store.Statements(sourceFile), pos + 1);
                        }
                    }
                }

                contextFlags = savedContextFlags;
                // (TypeScript's callback returns undefined.)
                return false;
            }, SpeculationKind.Reparse);

            // find the next statement containing an `await`
            start = pos >= 0 ? FindNextStatementWithAwait(store.Statements(sourceFile), pos) : -1;
        }

        // append all statements between pos and the end of the list
        if (pos >= 0)
        {
            var prevStatement = store.Element(store.Statements(sourceFile), pos);
            foreach (var statement in store.Nodes(store.Statements(sourceFile))[pos..])
            {
                store.Push(statements, statement);
            }

            // append all diagnostics associated with the copied range
            var diagnosticStart = Core.FindIndex(savedParseDiagnostics, (diagnostic, _) => diagnostic.Start >= store.Pos(prevStatement));
            if (diagnosticStart >= 0)
            {
                Core.AddRange(parseDiagnostics, savedParseDiagnostics, diagnosticStart);
            }
        }

        syntaxCursor = savedSyntaxCursor;
        intersectingChangeSet.Clear();
        return factory.UpdateSourceFile(sourceFile, FactoryUtilitiesPublic.SetTextRange(store, NodeFactory.CreateNodeArray(store, statements), store.Statements(sourceFile)));

        bool ContainsPossibleTopLevelAwait(Node node)
        {
            return (store.Flags(node) & NodeFlags.AwaitContext) == 0
                && (store.TransformFlags(node) & TransformFlags.ContainsPossibleTopLevelAwait) != 0;
        }

        int FindNextStatementWithAwait(NodeList statements, int start)
        {
            for (var i = start; i < store.Length(statements); i++)
            {
                if (ContainsPossibleTopLevelAwait(store.Element(statements, i)))
                {
                    return i;
                }
            }
            return -1;
        }

        int FindNextStatementWithoutAwait(NodeList statements, int start)
        {
            for (var i = start; i < store.Length(statements); i++)
            {
                if (!ContainsPossibleTopLevelAwait(store.Element(statements, i)))
                {
                    return i;
                }
            }
            return -1;
        }

        // TypeScript: the inner function currentNode, the syntax cursor of the reparse.
        Node ReparseCurrentNode(int position)
        {
            var node = baseSyntaxCursor.CurrentNode(position);
            if (topLevel && !node.IsNone && ContainsPossibleTopLevelAwait(node))
            {
                MarkAsIntersectingIncrementalChange(node);
            }
            return node;
        }
    }

    /// <summary>
    /// parser.ts: IncrementalParser.createSyntaxCursor. Not otherwise part of this module (no incremental parsing in
    /// Phase 1), but reparseTopLevelAwait reuses the unchanged statements through it.
    /// </summary>
    internal static SyntaxCursor CreateSyntaxCursor(NodeStore store, Node sourceFile)
    {
        NodeList currentArray = store.Statements(sourceFile);
        var currentArrayIndex = 0;

        Debug.Assert(currentArrayIndex < store.Length(currentArray));
        Node current = store.Element(currentArray, currentArrayIndex);
        var lastQueriedPosition = (int)InvalidPosition.Value;

        return new SyntaxCursor
        {
            CurrentNode = position =>
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
                    if (!current.IsNone && store.End(current) == position && currentArrayIndex < (store.Length(currentArray) - 1))
                    {
                        currentArrayIndex++;
                        current = store.Element(currentArray, currentArrayIndex);
                    }

                    // If we don't have a node, or the node we have isn't in the right position,
                    // then try to find a viable node at the position requested.
                    if (current.IsNone || store.Pos(current) != position)
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
                Debug.Assert(current.IsNone || store.Pos(current) == position);
                return current;
            },
        };

        // Finds the highest element in the tree we can find that starts at the provided position.
        // The element must be a direct child of some node list in the tree.  This way after we
        // return it, we can easily return its next sibling in the list.
        void FindHighestListElementThatStartsAtPosition(int position)
        {
            // Clear out any cached state about the last node we found.
            currentArray = default;
            currentArrayIndex = (int)InvalidPosition.Value;
            current = default;

            // Recurse into the source file to find the highest node at this position.
            NodeVisitor.ForEachChild<object>(store, sourceFile, VisitNode, VisitArray);
            return;

            object? VisitNode(Node node)
            {
                if (position >= store.Pos(node) && position < store.End(node))
                {
                    // Position was within this node.  Keep searching deeper to find the node.
                    NodeVisitor.ForEachChild<object>(store, node, VisitNode, VisitArray);

                    // don't proceed any further in the search.
                    return StopVisiting;
                }

                // position wasn't in this node, have to keep searching.
                return null;
            }

            object? VisitArray(NodeList array)
            {
                if (position >= store.Pos(array) && position < store.End(array))
                {
                    // position was in this array.  Search through this array to see if we find a
                    // viable element.
                    for (var i = 0; i < store.Length(array); i++)
                    {
                        var child = store.Element(array, i);
                        if (!child.IsNone)
                        {
                            if (store.Pos(child) == position)
                            {
                                // Found the right node.  We're done.
                                currentArray = array;
                                currentArrayIndex = i;
                                current = child;
                                return StopVisiting;
                            }
                            else
                            {
                                if (store.Pos(child) < position && position < store.End(child))
                                {
                                    // Position in somewhere within this child.  Search in it and
                                    // stop searching in this array.
                                    NodeVisitor.ForEachChild<object>(store, child, VisitNode, VisitArray);
                                    return StopVisiting;
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

    /// <summary>parser.ts: IncrementalParser.InvalidPosition.</summary>
    private enum InvalidPosition
    {
        Value = -1,
    }
}
