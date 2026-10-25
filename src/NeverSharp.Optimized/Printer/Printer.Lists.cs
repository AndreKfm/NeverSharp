// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/emitter.ts (createPrinter: emitList, emitNodeList, emitNodeListItems, writers, line
// terminator counts) and src/compiler/utilities.ts (range and line helpers)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/printer/printer.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; without comments, source maps and preserveSourceNewlines (their branches
// are left out); a writer callback is a WriteKind.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>The writer functions emitter.ts passes around (writePunctuation, writeKeyword, ...).</summary>
internal enum WriteKind
{
    Write,
    Literal,
    StringLiteral,
    Punctuation,
    Keyword,
    Operator,
    Parameter,
    Property,
    Space,
    TrailingSemicolon,
    Comment,
}

internal sealed partial class Printer
{
    /// <summary>The children of a list as emitList receives them: a NodeArray, a plain array, or undefined.</summary>
    private readonly ref struct ListItems
    {
        internal readonly ReadOnlySpan<Node> Nodes;
        internal readonly bool IsUndefined;
        internal readonly bool HasTrailingComma;
        internal readonly int Pos;
        internal readonly int End;

        internal ListItems(ReadOnlySpan<Node> nodes, bool isUndefined, bool hasTrailingComma, int pos, int end)
        {
            Nodes = nodes;
            IsUndefined = isUndefined;
            HasTrailingComma = hasTrailingComma;
            Pos = pos;
            End = end;
        }
    }

    private ListItems Items(NodeList list)
    {
        return list.IsNone ? new ListItems(default, isUndefined: true, hasTrailingComma: false, -1, -1)
            : new ListItems(store.Nodes(list), isUndefined: false, store.HasTrailingComma(list), store.Pos(list), store.End(list));
    }

    // factory.createNodeArray(array) of a plain array (pos and end -1); undefined for null.
    private static ListItems Items(Node[]? array)
    {
        return array is null ? new ListItems(default, isUndefined: true, hasTrailingComma: false, -1, -1)
            : new ListItems(array, isUndefined: false, hasTrailingComma: false, -1, -1);
    }

    private void EmitList(Node parentNode, NodeList children, ListFormat format, int start = 0, int count = -1)
    {
        EmitList(parentNode, Items(children), format, start, count);
    }

    private void EmitList(Node parentNode, ListItems children, ListFormat format, int start = 0, int count = -1)
    {
        EmitNodeList(
            expressionList: false,
            parentNode,
            children,
            format | (!parentNode.IsNone && (GetEmitFlags(parentNode) & EmitFlags.MultiLine) != 0 ? ListFormat.PreferNewLine : 0),
            start,
            count);
    }

    private void EmitExpressionList(Node parentNode, NodeList children, ListFormat format, int start = 0, int count = -1)
    {
        EmitNodeList(expressionList: true, parentNode, Items(children), format, start, count);
    }

    // emitNodeList; count -1 is TypeScript's default `children ? children.length - start : 0`. (The parenthesizer rule
    // argument is left out: without substitution the printer never applies it.)
    private void EmitNodeList(bool expressionList, Node parentNode, ListItems children, ListFormat format, int start, int count)
    {
        if (count < 0)
        {
            count = children.IsUndefined ? 0 : children.Nodes.Length - start;
        }
        var isUndefined = children.IsUndefined;
        if (isUndefined && (format & ListFormat.OptionalIfUndefined) != 0)
        {
            return;
        }

        var isEmpty = children.IsUndefined || start >= children.Nodes.Length || count == 0;
        if (isEmpty && (format & ListFormat.OptionalIfEmpty) != 0)
        {
            return;
        }

        if ((format & ListFormat.BracketsMask) != 0)
        {
            WritePunctuation(GetOpeningBracket(format));
        }

        if (isEmpty)
        {
            // Write a line terminator if the parent node was multi-line
            if ((format & ListFormat.MultiLine) != 0)
            {
                WriteLine();
            }
            else if ((format & ListFormat.SpaceBetweenBraces) != 0 && (format & ListFormat.NoSpaceIfEmpty) == 0)
            {
                WriteSpace();
            }
        }
        else
        {
            EmitNodeListItems(expressionList, parentNode, children.Nodes, format, start, count, children.HasTrailingComma);
        }

        if ((format & ListFormat.BracketsMask) != 0)
        {
            WritePunctuation(GetClosingBracket(format));
        }
    }

    /// <summary>
    /// Emits a list without brackets or raising events.
    ///
    /// NOTE: You probably don't want to call this directly and should be using `emitList` or `emitExpressionList` instead.
    /// </summary>
    private void EmitNodeListItems(bool expressionList, Node parentNode, ReadOnlySpan<Node> children, ListFormat format, int start, int count, bool hasTrailingComma)
    {
        // Write the opening line terminator or leading whitespace.
        var leadingLineTerminatorCount = GetLeadingLineTerminatorCount(parentNode, children[start], format);
        if (leadingLineTerminatorCount != 0)
        {
            WriteLine(leadingLineTerminatorCount);
        }
        else if ((format & ListFormat.SpaceBetweenBraces) != 0)
        {
            WriteSpace();
        }

        // Increase the indent, if requested.
        if ((format & ListFormat.Indented) != 0)
        {
            IncreaseIndent();
        }

        // Emit each child.
        var previousSibling = Node.None;
        var shouldDecreaseIndentAfterEmit = false;
        for (var i = 0; i < count; i++)
        {
            var child = children[start + i];

            // Write the delimiter if this is not the first node.
            if ((format & ListFormat.AsteriskDelimited) != 0)
            {
                // always write JSDoc in the format "\n *"
                WriteLine();
                WriteDelimiter(format);
            }
            else if (!previousSibling.IsNone)
            {
                WriteDelimiter(format);

                // Write either a line terminator or whitespace to separate the elements.
                var separatingLineTerminatorCount = GetSeparatingLineTerminatorCount(previousSibling, child, format);
                if (separatingLineTerminatorCount > 0)
                {
                    // If a synthesized node in a single-line list starts on a new
                    // line, we should increase the indent.
                    if ((format & (ListFormat.LinesMask | ListFormat.Indented)) == ListFormat.SingleLine)
                    {
                        IncreaseIndent();
                        shouldDecreaseIndentAfterEmit = true;
                    }

                    WriteLine(separatingLineTerminatorCount);
                }
                else if ((format & ListFormat.SpaceBetweenSiblings) != 0)
                {
                    WriteSpace();
                }
            }

            // Emit this child.
            nextListElementPos = store.Pos(child);
            if (expressionList)
            {
                EmitExpression(child);
            }
            else
            {
                Emit(child);
            }

            if (shouldDecreaseIndentAfterEmit)
            {
                DecreaseIndent();
                shouldDecreaseIndentAfterEmit = false;
            }

            previousSibling = child;
        }

        // Write a trailing comma, if requested.
        var emitTrailingComma = hasTrailingComma && (format & ListFormat.AllowTrailingComma) != 0 && (format & ListFormat.CommaDelimited) != 0;
        if (emitTrailingComma)
        {
            // (comments are disabled: TypeScript writes the comma with writePunctuation)
            WritePunctuation(",");
        }

        // Decrease the indent, if requested.
        if ((format & ListFormat.Indented) != 0)
        {
            DecreaseIndent();
        }

        // Write the closing line terminator or closing whitespace.
        var closingLineTerminatorCount = GetClosingLineTerminatorCount(parentNode, children[start + count - 1], format);
        if (closingLineTerminatorCount != 0)
        {
            WriteLine(closingLineTerminatorCount);
        }
        else if ((format & (ListFormat.SpaceAfterList | ListFormat.SpaceBetweenBraces)) != 0)
        {
            WriteSpace();
        }
    }

    private void WriteDelimiter(ListFormat format)
    {
        switch (format & ListFormat.DelimitersMask)
        {
            case ListFormat.None:
                break;
            case ListFormat.CommaDelimited:
                WritePunctuation(",");
                break;
            case ListFormat.BarDelimited:
                WriteSpace();
                WritePunctuation("|");
                break;
            case ListFormat.AsteriskDelimited:
                WriteSpace();
                WritePunctuation("*");
                WriteSpace();
                break;
            case ListFormat.AmpersandDelimited:
                WriteSpace();
                WritePunctuation("&");
                break;
        }
    }

    // createBracketsMap, getOpeningBracket, getClosingBracket
    private static string GetOpeningBracket(ListFormat format) => (format & ListFormat.BracketsMask) switch
    {
        ListFormat.Braces => "{",
        ListFormat.Parenthesis => "(",
        ListFormat.AngleBrackets => "<",
        _ => "[",
    };

    private static string GetClosingBracket(ListFormat format) => (format & ListFormat.BracketsMask) switch
    {
        ListFormat.Braces => "}",
        ListFormat.Parenthesis => ")",
        ListFormat.AngleBrackets => ">",
        _ => "]",
    };

    //
    // Writers
    //

    private void WriteLiteral(string s) => writer.WriteLiteral(s);

    private void WriteStringLiteral(string s) => writer.WriteStringLiteral(s);

    private void Write(string s) => writer.Write(s);

    private void WritePunctuation(string s) => writer.WritePunctuation(s);

    private void WriteTrailingSemicolon() => writer.WriteTrailingSemicolon(";");

    private void WriteKeyword(string s) => writer.WriteKeyword(s);

    private void WriteOperator(string s) => writer.WriteOperator(s);

    private void WriteComment(string s) => writer.WriteComment(s);

    private void WriteSpace() => writer.WriteSpace(" ");

    private void WriteAs(string s, WriteKind kind)
    {
        switch (kind)
        {
            case WriteKind.Literal:
                writer.WriteLiteral(s);
                break;
            case WriteKind.StringLiteral:
                writer.WriteStringLiteral(s);
                break;
            case WriteKind.Punctuation:
                writer.WritePunctuation(s);
                break;
            case WriteKind.Keyword:
                writer.WriteKeyword(s);
                break;
            case WriteKind.Operator:
                writer.WriteOperator(s);
                break;
            case WriteKind.Parameter:
                writer.WriteParameter(s);
                break;
            case WriteKind.Property:
                writer.WriteProperty(s);
                break;
            case WriteKind.Space:
                writer.WriteSpace(s);
                break;
            case WriteKind.TrailingSemicolon:
                writer.WriteTrailingSemicolon(s);
                break;
            case WriteKind.Comment:
                writer.WriteComment(s);
                break;
            default:
                writer.Write(s);
                break;
        }
    }

    private void WriteLine(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            writer.WriteLine(i > 0);
        }
    }

    private void IncreaseIndent() => writer.IncreaseIndent();

    private void DecreaseIndent() => writer.DecreaseIndent();

    // writeToken: without source maps, writeTokenText.
    private int WriteToken(SyntaxKind token, int pos, WriteKind kind) => WriteTokenText(token, kind, pos);

    private void WriteTokenNode(Node node, WriteKind kind)
    {
        WriteAs(Scanner.TokenToString(store.Kind(node))!, kind);
    }

    private void WriteTokenText(SyntaxKind token, WriteKind kind)
    {
        WriteAs(Scanner.TokenToString(token)!, kind);
    }

    private int WriteTokenText(SyntaxKind token, WriteKind kind, int pos)
    {
        var tokenString = Scanner.TokenToString(token)!;
        WriteAs(tokenString, kind);
        return pos < 0 ? pos : pos + tokenString.Length;
    }

    private void WriteLineOrSpace(Node parentNode)
    {
        if ((GetEmitFlags(parentNode) & EmitFlags.SingleLine) != 0)
        {
            WriteSpace();
        }
        else
        {
            WriteLine();
        }
    }

    private void WriteLinesAndIndent(int lineCount, bool writeSpaceIfNotIndenting)
    {
        if (lineCount != 0)
        {
            IncreaseIndent();
            WriteLine(lineCount);
        }
        else if (writeSpaceIfNotIndenting)
        {
            WriteSpace();
        }
    }

    // Helper function to decrease the indent if we previously indented.  Allows multiple
    // previous indent values to be considered at a time.  This also allows caller to just
    // call this once, passing in all their appropriate indent values, instead of needing
    // to call this helper function multiple times.
    private void DecreaseIndentIf(int value1, int value2 = 0)
    {
        if (value1 != 0)
        {
            DecreaseIndent();
        }
        if (value2 != 0)
        {
            DecreaseIndent();
        }
    }

    //
    // Line terminators
    //

    private int GetLeadingLineTerminatorCount(Node parentNode, Node firstChild, ListFormat format)
    {
        if ((format & ListFormat.PreserveLines) != 0)
        {
            if ((format & ListFormat.PreferNewLine) != 0)
            {
                return 1;
            }

            if (firstChild.IsNone)
            {
                return parentNode.IsNone || (!currentSourceFile.IsNone && RangeIsOnSingleLine(parentNode)) ? 0 : 1;
            }
            if (store.Pos(firstChild) == nextListElementPos)
            {
                // If this child starts at the beginning of a list item in a parent list, its leading
                // line terminators have already been written as the separating line terminators of the
                // parent list.
                return 0;
            }
            if (store.Kind(firstChild) == SyntaxKind.JsxText)
            {
                // JsxText will be written with its leading whitespace, so don't add more manually.
                return 0;
            }
            if (
                !currentSourceFile.IsNone && !parentNode.IsNone &&
                !PositionIsSynthesized(store.Pos(parentNode)) &&
                !NodeIsSynthesized(firstChild) &&
                (store.Parent(firstChild).IsNone || GetOriginalNode(store.Parent(firstChild)) == GetOriginalNode(parentNode)))
            {
                return RangeStartPositionsAreOnSameLine(parentNode, firstChild) ? 0 : 1;
            }
            if (SynthesizedNodeStartsOnNewLine(firstChild, format))
            {
                return 1;
            }
        }
        return (format & ListFormat.MultiLine) != 0 ? 1 : 0;
    }

    private int GetSeparatingLineTerminatorCount(Node previousNode, Node nextNode, ListFormat format)
    {
        if ((format & ListFormat.PreserveLines) != 0)
        {
            if (previousNode.IsNone || nextNode.IsNone)
            {
                return 0;
            }
            if (store.Kind(nextNode) == SyntaxKind.JsxText)
            {
                // JsxText will be written with its leading whitespace, so don't add more manually.
                return 0;
            }
            else if (!currentSourceFile.IsNone && !NodeIsSynthesized(previousNode) && !NodeIsSynthesized(nextNode))
            {
                // If `preserveSourceNewlines` is `false` we do not intend to preserve the effective lines between the
                // previous and next node. Instead we naively check whether nodes are on separate lines within the
                // same node parent. If so, we intend to preserve a single line terminator.
                if (OriginalNodesHaveSameParent(previousNode, nextNode))
                {
                    return RangeEndIsOnSameLineAsRangeStart(store.End(previousNode), store.Pos(nextNode)) ? 0 : 1;
                }
                // If the two nodes are not comparable, add a line terminator based on the format that can indicate
                // whether new lines are preferred or not.
                return (format & ListFormat.PreferNewLine) != 0 ? 1 : 0;
            }
            else if (SynthesizedNodeStartsOnNewLine(previousNode, format) || SynthesizedNodeStartsOnNewLine(nextNode, format))
            {
                return 1;
            }
        }
        else if (GetStartsOnNewLine(nextNode) == true)
        {
            return 1;
        }
        return (format & ListFormat.MultiLine) != 0 ? 1 : 0;
    }

    private int GetClosingLineTerminatorCount(Node parentNode, Node lastChild, ListFormat format)
    {
        if ((format & ListFormat.PreserveLines) != 0)
        {
            if ((format & ListFormat.PreferNewLine) != 0)
            {
                return 1;
            }

            if (lastChild.IsNone)
            {
                return parentNode.IsNone || (!currentSourceFile.IsNone && RangeIsOnSingleLine(parentNode)) ? 0 : 1;
            }
            if (!currentSourceFile.IsNone && !parentNode.IsNone && !PositionIsSynthesized(store.Pos(parentNode)) && !NodeIsSynthesized(lastChild) &&
                (store.Parent(lastChild).IsNone || store.Parent(lastChild) == parentNode))
            {
                return PositionsAreOnSameLine(store.End(parentNode), store.End(lastChild)) ? 0 : 1;
            }
            if (SynthesizedNodeStartsOnNewLine(lastChild, format))
            {
                return 1;
            }
        }
        if ((format & ListFormat.MultiLine) != 0 && (format & ListFormat.NoTrailingNewLine) == 0)
        {
            return 1;
        }
        return 0;
    }

    private bool SynthesizedNodeStartsOnNewLine(Node node, ListFormat format)
    {
        if (NodeIsSynthesized(node))
        {
            var startsOnNewLine = GetStartsOnNewLine(node);
            if (startsOnNewLine is null)
            {
                return (format & ListFormat.PreferNewLine) != 0;
            }

            return startsOnNewLine.Value;
        }

        return (format & ListFormat.PreferNewLine) != 0;
    }

    private int GetLinesBetweenNodes(Node parent, Node node1, Node node2)
    {
        node1 = SkipSynthesizedParentheses(node1);
        node2 = SkipSynthesizedParentheses(node2);
        return GetLinesBetweenRanges(parent, node2, store.End(node1), store.Pos(node2), NodeIsSynthesized(node1));
    }

    /// <summary>
    /// getLinesBetweenNodes for (node1, node2) given as node1's end, node2's pos and node2 itself (none for a token the
    /// printer creates with a text range, such as the dot of a property access).
    /// </summary>
    private int GetLinesBetweenRanges(Node parent, Node node2, int end1, int pos2, bool node1IsSynthesized)
    {
        if ((GetEmitFlags(parent) & EmitFlags.NoIndentation) != 0)
        {
            return 0;
        }

        parent = SkipSynthesizedParentheses(parent);

        // Always use a newline for synthesized code if the synthesizer desires it.
        if (!node2.IsNone && GetStartsOnNewLine(node2) == true)
        {
            return 1;
        }

        if (!currentSourceFile.IsNone && !NodeIsSynthesized(parent) && !node1IsSynthesized && !PositionIsSynthesized(pos2))
        {
            return RangeEndIsOnSameLineAsRangeStart(end1, pos2) ? 0 : 1;
        }

        return 0;
    }

    private bool IsEmptyBlock(Node block)
    {
        return store.Length(store.Statements(block)) == 0
            && (currentSourceFile.IsNone || RangeEndIsOnSameLineAsRangeStart(store.End(block), store.Pos(block)));
    }

    private Node SkipSynthesizedParentheses(Node node)
    {
        while (store.Kind(node) == SyntaxKind.ParenthesizedExpression && NodeIsSynthesized(node))
        {
            node = store.Expression(node);
        }

        return node;
    }

    private bool OriginalNodesHaveSameParent(Node nodeA, Node nodeB)
    {
        nodeA = GetOriginalNode(nodeA);
        // For performance, do not call `getOriginalNode` for `nodeB` if `nodeA` doesn't even
        // have a parent node.
        return !store.Parent(nodeA).IsNone && store.Parent(nodeA) == store.Parent(GetOriginalNode(nodeB));
    }

    //
    // utilities.ts: ranges and lines of the current source file
    //

    private bool RangeIsOnSingleLine(Node range)
    {
        return PositionsAreOnSameLine(GetStartPositionOfRange(store.Pos(range)), store.End(range));
    }

    private bool RangeStartPositionsAreOnSameLine(Node range1, Node range2)
    {
        return PositionsAreOnSameLine(GetStartPositionOfRange(store.Pos(range1)), GetStartPositionOfRange(store.Pos(range2)));
    }

    // rangeEndIsOnSameLineAsRangeStart(range1, range2) with range1's end and range2's pos.
    private bool RangeEndIsOnSameLineAsRangeStart(int end1, int pos2)
    {
        return PositionsAreOnSameLine(end1, GetStartPositionOfRange(pos2));
    }

    private bool PositionsAreOnSameLine(int pos1, int pos2)
    {
        return GetLinesBetweenPositions(pos1, pos2) == 0;
    }

    // getStartPositionOfRange(range, sourceFile, includeComments: false)
    private int GetStartPositionOfRange(int pos)
    {
        return PositionIsSynthesized(pos) ? -1 : Scanner.SkipTrivia(CurrentText, pos, stopAfterLineBreak: false, stopAtComments: false);
    }

    private int GetLinesBetweenPositions(int pos1, int pos2)
    {
        if (pos1 == pos2)
        {
            return 0;
        }
        var lineStarts = GetCurrentLineMap();
        var lower = Math.Min(pos1, pos2);
        var isNegative = lower == pos2;
        var upper = isNegative ? pos1 : pos2;
        var lowerLine = Scanner.ComputeLineOfPosition(lineStarts, lower);
        var upperLine = Scanner.ComputeLineOfPosition(lineStarts, upper, lowerLine);
        return isNegative ? lowerLine - upperLine : upperLine - lowerLine;
    }
}
