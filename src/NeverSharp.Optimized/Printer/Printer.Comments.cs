// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/emitter.ts (createPrinter: comment state and the "Comments" section, emitTokenWithComment,
// willEmitLeadingNewLine, parenthesizeExpressionForNoAsi) and src/compiler/utilities.ts (emitDetachedComments, ...)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/printer/printer.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; the comment callbacks are an enum (no closures); no source maps (emitPos
// is a no-op); parenthesizeExpressionForNoAsi rebuilds no nodes (see there). Also ported here: isRecognizedTripleSlashComment,
// isJSDocLikeText and emitNewLineBeforeLeadingCommentOfPosition (utilities.ts).

using System.Text.RegularExpressions;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Printer
{
    // Comments
    private int containerPos = -1;
    private int containerEnd = -1;
    private int declarationListContainerEnd = -1;
    private List<(int NodePos, int DetachedCommentEndPos)>? detachedCommentsInfo;
    private bool hasWrittenComment;
    private bool commentsDisabled;

    // The callbacks emitter.ts passes to forEachLeadingCommentToEmit / forEachTrailingCommentToEmit.
    private enum CommentCallback
    {
        LeadingComment,
        TripleSlashLeadingComment,
        NonTripleSlashLeadingComment,
        TrailingComment,
        TrailingCommentOfPosition,
        TrailingCommentOfPositionNoNewline,
    }

    private bool ShouldEmitComments(Node node) => !commentsDisabled && store.Kind(node) != SyntaxKind.SourceFile;

    // getCommentRange without allocating
    private (int Pos, int End) CommentRangeOf(Node node)
    {
        return (store.EmitNode(node) as EmitNode)?.CommentRange is { } range ? (range.Pos, range.End) : (store.Pos(node), store.End(node));
    }

    private void PipelineEmitWithComments(EmitHint hint, Node node)
    {
        var savedContainerPos = containerPos;
        var savedContainerEnd = containerEnd;
        var savedDeclarationListContainerEnd = declarationListContainerEnd;
        EmitCommentsBeforeNode(node);
        PipelineEmitWithHintWorker(hint, node);
        EmitCommentsAfterNode(node, savedContainerPos, savedContainerEnd, savedDeclarationListContainerEnd);
    }

    private void EmitCommentsBeforeNode(Node node)
    {
        var emitFlags = GetEmitFlags(node);
        var commentRange = CommentRangeOf(node);

        // Emit leading comments
        EmitLeadingCommentsOfNode(node, emitFlags, commentRange.Pos, commentRange.End);
        if ((emitFlags & EmitFlags.NoNestedComments) != 0)
        {
            commentsDisabled = true;
        }
    }

    private void EmitCommentsAfterNode(Node node, int savedContainerPos, int savedContainerEnd, int savedDeclarationListContainerEnd)
    {
        var emitFlags = GetEmitFlags(node);
        var commentRange = CommentRangeOf(node);

        // Emit trailing comments
        if ((emitFlags & EmitFlags.NoNestedComments) != 0)
        {
            commentsDisabled = false;
        }
        EmitTrailingCommentsOfNode(node, emitFlags, commentRange.Pos, commentRange.End, savedContainerPos, savedContainerEnd, savedDeclarationListContainerEnd);
        var typeNode = (store.EmitNode(node) as EmitNode)?.TypeNode ?? Node.None;
        if (!typeNode.IsNone)
        {
            EmitTrailingCommentsOfNode(node, emitFlags, store.Pos(typeNode), store.End(typeNode), savedContainerPos, savedContainerEnd, savedDeclarationListContainerEnd);
        }
    }

    private void EmitLeadingCommentsOfNode(Node node, EmitFlags emitFlags, int pos, int end)
    {
        hasWrittenComment = false;

        // We have to explicitly check that the node is JsxText because if the compilerOptions.jsx is "preserve" we will not do any transformation.
        // It is expensive to walk entire tree just to set one kind of node to have no comments.
        var kind = store.Kind(node);
        var skipLeadingComments = pos < 0 || (emitFlags & EmitFlags.NoLeadingComments) != 0 || kind == SyntaxKind.JsxText;
        var skipTrailingComments = end < 0 || (emitFlags & EmitFlags.NoTrailingComments) != 0 || kind == SyntaxKind.JsxText;

        // Save current container state on the stack.
        if ((pos > 0 || end > 0) && pos != end)
        {
            // Emit leading comments if the position is not synthesized and the node
            // has not opted out from emitting leading comments.
            if (!skipLeadingComments)
            {
                EmitLeadingComments(pos, isEmittedNode: kind != SyntaxKind.NotEmittedStatement);
            }

            if (!skipLeadingComments || (pos >= 0 && (emitFlags & EmitFlags.NoLeadingComments) != 0))
            {
                // Advance the container position if comments get emitted or if they've been disabled explicitly using NoLeadingComments.
                containerPos = pos;
            }

            if (!skipTrailingComments || (end >= 0 && (emitFlags & EmitFlags.NoTrailingComments) != 0))
            {
                // As above.
                containerEnd = end;

                // To avoid invalid comment emit in a down-level binding pattern, we
                // keep track of the last declaration list container's end
                if (kind == SyntaxKind.VariableDeclarationList)
                {
                    declarationListContainerEnd = end;
                }
            }
        }
        if (FactoryEmitNode.GetSyntheticLeadingComments(store, node) is { } comments)
        {
            foreach (var comment in comments)
            {
                EmitLeadingSynthesizedComment(comment);
            }
        }
    }

    private void EmitTrailingCommentsOfNode(Node node, EmitFlags emitFlags, int pos, int end, int savedContainerPos, int savedContainerEnd, int savedDeclarationListContainerEnd)
    {
        var kind = store.Kind(node);
        var skipTrailingComments = end < 0 || (emitFlags & EmitFlags.NoTrailingComments) != 0 || kind == SyntaxKind.JsxText;
        if (FactoryEmitNode.GetSyntheticTrailingComments(store, node) is { } comments)
        {
            foreach (var comment in comments)
            {
                EmitTrailingSynthesizedComment(comment);
            }
        }
        if ((pos > 0 || end > 0) && pos != end)
        {
            // Restore previous container state.
            containerPos = savedContainerPos;
            containerEnd = savedContainerEnd;
            declarationListContainerEnd = savedDeclarationListContainerEnd;

            // Emit trailing comments if the position is not synthesized and the node
            // has not opted out from emitting leading comments and is an emitted node.
            if (!skipTrailingComments && kind != SyntaxKind.NotEmittedStatement)
            {
                EmitTrailingComments(end);
            }
        }
    }

    private void EmitLeadingSynthesizedComment(SynthesizedComment comment)
    {
        if (comment.HasLeadingNewline == true || comment.Kind == SyntaxKind.SingleLineCommentTrivia)
        {
            writer.WriteLine();
        }
        WriteSynthesizedComment(comment);
        if (comment.HasTrailingNewLine == true || comment.Kind == SyntaxKind.SingleLineCommentTrivia)
        {
            writer.WriteLine();
        }
        else
        {
            writer.WriteSpace(" ");
        }
    }

    private void EmitTrailingSynthesizedComment(SynthesizedComment comment)
    {
        if (!writer.IsAtStartOfLine())
        {
            writer.WriteSpace(" ");
        }
        WriteSynthesizedComment(comment);
        if (comment.HasTrailingNewLine == true)
        {
            writer.WriteLine();
        }
    }

    private void WriteSynthesizedComment(SynthesizedComment comment)
    {
        var text = FormatSynthesizedComment(comment);
        var lineMap = comment.Kind == SyntaxKind.MultiLineCommentTrivia ? Scanner.ComputeLineStarts(text) : [];
        PrinterUtilities.WriteCommentRange(text, lineMap, writer, 0, text.Length, newLine);
    }

    private static string FormatSynthesizedComment(SynthesizedComment comment)
    {
        return comment.Kind == SyntaxKind.MultiLineCommentTrivia
            ? "/*" + comment.Text + "*/"
            : "//" + comment.Text;
    }

    private void EmitBodyWithDetachedComments(Node node, int detachedRangePos, int detachedRangeEnd, Action emitCallback)
    {
        var emitFlags = GetEmitFlags(node);
        var skipLeadingComments = detachedRangePos < 0 || (emitFlags & EmitFlags.NoLeadingComments) != 0;
        var skipTrailingComments = commentsDisabled || detachedRangeEnd < 0 || (emitFlags & EmitFlags.NoTrailingComments) != 0;
        if (!skipLeadingComments)
        {
            EmitDetachedCommentsAndUpdateCommentsInfo(detachedRangePos);
        }

        if ((emitFlags & EmitFlags.NoNestedComments) != 0 && !commentsDisabled)
        {
            commentsDisabled = true;
            emitCallback();
            commentsDisabled = false;
        }
        else
        {
            emitCallback();
        }

        if (!skipTrailingComments)
        {
            EmitLeadingComments(detachedRangeEnd, isEmittedNode: true);
            if (hasWrittenComment && !writer.IsAtStartOfLine())
            {
                writer.WriteLine();
            }
        }
    }

    private void EmitLeadingComments(int pos, bool isEmittedNode)
    {
        hasWrittenComment = false;

        if (isEmittedNode)
        {
            if (pos == 0 && !currentSourceFile.IsNone && store.IsDeclarationFile(currentSourceFile))
            {
                ForEachLeadingCommentToEmit(pos, CommentCallback.NonTripleSlashLeadingComment);
            }
            else
            {
                ForEachLeadingCommentToEmit(pos, CommentCallback.LeadingComment);
            }
        }
        else if (pos == 0)
        {
            // If the node will not be emitted in JS, remove all the comments(normal, pinned and ///) associated with the node,
            // unless it is a triple slash comment at the top of the file.
            // For Example:
            //      /// <reference-path ...>
            //      declare var x;
            //      /// <reference-path ...>
            //      interface F {}
            //  The first /// will NOT be removed while the second one will be removed even though both node will not be emitted
            ForEachLeadingCommentToEmit(pos, CommentCallback.TripleSlashLeadingComment);
        }
    }

    private bool ShouldWriteComment(ReadOnlySpan<char> text, int pos)
    {
        if (printerOptions.OnlyPrintJsDocStyle)
        {
            return IsJSDocLikeText(text, pos) || PrinterUtilities.IsPinnedComment(text, pos);
        }
        return true;
    }

    // utilities.ts isJSDocLikeText
    private static bool IsJSDocLikeText(ReadOnlySpan<char> text, int start)
    {
        return start + 2 < text.Length && text[start + 1] == '*' && text[start + 2] == '*'
            && (start + 3 >= text.Length || text[start + 3] != '/');
    }

    private void EmitLeadingComment(int commentPos, int commentEnd, SyntaxKind kind, bool hasTrailingNewLine, int rangePos)
    {
        if (currentSourceFile.IsNone || !ShouldWriteComment(CurrentText, commentPos))
        {
            return;
        }
        if (!hasWrittenComment)
        {
            EmitNewLineBeforeLeadingCommentOfPosition(rangePos, commentPos);
            hasWrittenComment = true;
        }

        // Leading comments are emitted at /*leading comment1 */space/*leading comment*/space
        PrinterUtilities.WriteCommentRange(CurrentText, GetCurrentLineMap(), writer, commentPos, commentEnd, newLine);

        if (hasTrailingNewLine)
        {
            writer.WriteLine();
        }
        else if (kind == SyntaxKind.MultiLineCommentTrivia)
        {
            writer.WriteSpace(" ");
        }
    }

    // utilities.ts emitNewLineBeforeLeadingCommentOfPosition
    private void EmitNewLineBeforeLeadingCommentOfPosition(int pos, int commentPos)
    {
        // If the leading comments start on different line than the start of node, write new line
        var lineMap = GetCurrentLineMap();
        if (pos != commentPos && Scanner.ComputeLineOfPosition(lineMap, pos) != Scanner.ComputeLineOfPosition(lineMap, commentPos))
        {
            writer.WriteLine();
        }
    }

    private void EmitLeadingCommentsOfPosition(int pos)
    {
        if (commentsDisabled || pos == -1)
        {
            return;
        }

        EmitLeadingComments(pos, isEmittedNode: true);
    }

    private void EmitTrailingComments(int pos)
    {
        ForEachTrailingCommentToEmit(pos, CommentCallback.TrailingComment);
    }

    private void EmitTrailingComment(int commentPos, int commentEnd, bool hasTrailingNewLine)
    {
        if (currentSourceFile.IsNone || !ShouldWriteComment(CurrentText, commentPos))
        {
            return;
        }
        // trailing comments are emitted at space/*trailing comment1 */space/*trailing comment2*/
        if (!writer.IsAtStartOfLine())
        {
            writer.WriteSpace(" ");
        }

        PrinterUtilities.WriteCommentRange(CurrentText, GetCurrentLineMap(), writer, commentPos, commentEnd, newLine);

        if (hasTrailingNewLine)
        {
            writer.WriteLine();
        }
    }

    private void EmitTrailingCommentsOfPosition(int pos, bool prefixSpace = false, bool forceNoNewline = false)
    {
        if (commentsDisabled)
        {
            return;
        }
        ForEachTrailingCommentToEmit(pos, prefixSpace ? CommentCallback.TrailingComment
            : forceNoNewline ? CommentCallback.TrailingCommentOfPositionNoNewline
            : CommentCallback.TrailingCommentOfPosition);
    }

    private void EmitTrailingCommentOfPositionNoNewline(int commentPos, int commentEnd, SyntaxKind kind)
    {
        if (currentSourceFile.IsNone)
        {
            return;
        }
        // trailing comments of a position are emitted at /*trailing comment1 */space/*trailing comment*/space

        PrinterUtilities.WriteCommentRange(CurrentText, GetCurrentLineMap(), writer, commentPos, commentEnd, newLine);

        if (kind == SyntaxKind.SingleLineCommentTrivia)
        {
            writer.WriteLine(); // still write a newline for single-line comments, so closing tokens aren't written on the same line
        }
    }

    private void EmitTrailingCommentOfPosition(int commentPos, int commentEnd, bool hasTrailingNewLine)
    {
        if (currentSourceFile.IsNone)
        {
            return;
        }
        // trailing comments of a position are emitted at /*trailing comment1 */space/*trailing comment*/space

        PrinterUtilities.WriteCommentRange(CurrentText, GetCurrentLineMap(), writer, commentPos, commentEnd, newLine);

        if (hasTrailingNewLine)
        {
            writer.WriteLine();
        }
        else
        {
            writer.WriteSpace(" ");
        }
    }

    private void InvokeCommentCallback(CommentCallback cb, CommentRange range, int rangePos)
    {
        switch (cb)
        {
            case CommentCallback.LeadingComment:
                EmitLeadingComment(range.Pos, range.End, range.Kind, range.HasTrailingNewLine, rangePos);
                break;
            case CommentCallback.TripleSlashLeadingComment:
                if (IsTripleSlashComment(range.Pos, range.End))
                {
                    EmitLeadingComment(range.Pos, range.End, range.Kind, range.HasTrailingNewLine, rangePos);
                }
                break;
            case CommentCallback.NonTripleSlashLeadingComment:
                if (!IsTripleSlashComment(range.Pos, range.End))
                {
                    EmitLeadingComment(range.Pos, range.End, range.Kind, range.HasTrailingNewLine, rangePos);
                }
                break;
            case CommentCallback.TrailingComment:
                EmitTrailingComment(range.Pos, range.End, range.HasTrailingNewLine);
                break;
            case CommentCallback.TrailingCommentOfPosition:
                EmitTrailingCommentOfPosition(range.Pos, range.End, range.HasTrailingNewLine);
                break;
            default:
                EmitTrailingCommentOfPositionNoNewline(range.Pos, range.End, range.Kind);
                break;
        }
    }

    // forEachLeadingCommentRange(text, pos, cb, state: pos) / forEachTrailingCommentRange: the callbacks never return a
    // truthy value, so every range is visited (the callbacks only write to the writer).
    private void ForEachCommentRange(int pos, bool trailing, CommentCallback cb, int rangePos)
    {
        var text = CurrentText;
        var iterator = new Scanner.CommentRangeIterator(text, pos, trailing);
        while (iterator.MoveNext(out var range))
        {
            InvokeCommentCallback(cb, range, rangePos);
        }
    }

    private void ForEachLeadingCommentToEmit(int pos, CommentCallback cb)
    {
        // Emit the leading comments only if the container's pos doesn't match because the container should take care of emitting these comments
        if (!currentSourceFile.IsNone && (containerPos == -1 || pos != containerPos))
        {
            if (HasDetachedComments(pos))
            {
                ForEachLeadingCommentWithoutDetachedComments(cb);
            }
            else
            {
                ForEachCommentRange(pos, trailing: false, cb, rangePos: pos);
            }
        }
    }

    private void ForEachTrailingCommentToEmit(int end, CommentCallback cb)
    {
        // Emit the trailing comments only if the container's end doesn't match because the container should take care of emitting these comments
        if (!currentSourceFile.IsNone && (containerEnd == -1 || (end != containerEnd && end != declarationListContainerEnd)))
        {
            ForEachCommentRange(end, trailing: true, cb, rangePos: end);
        }
    }

    private bool HasDetachedComments(int pos)
    {
        return detachedCommentsInfo is not null && detachedCommentsInfo[^1].NodePos == pos;
    }

    private void ForEachLeadingCommentWithoutDetachedComments(CommentCallback cb)
    {
        if (currentSourceFile.IsNone)
        {
            return;
        }
        // get the leading comments from detachedPos
        var pos = detachedCommentsInfo![^1].DetachedCommentEndPos;
        if (detachedCommentsInfo.Count - 1 != 0)
        {
            detachedCommentsInfo.RemoveAt(detachedCommentsInfo.Count - 1);
        }
        else
        {
            detachedCommentsInfo = null;
        }

        ForEachCommentRange(pos, trailing: false, cb, rangePos: pos);
    }

    private void EmitDetachedCommentsAndUpdateCommentsInfo(int rangePos)
    {
        if (currentSourceFile.IsNone)
        {
            return;
        }
        var currentDetachedCommentInfo = EmitDetachedComments(rangePos);
        if (currentDetachedCommentInfo is { } info)
        {
            (detachedCommentsInfo ??= []).Add(info);
        }
    }

    /// <summary>
    /// utilities.ts emitDetachedComments(text, lineMap, writer, emitComment, range, newLine, commentsDisabled): a detached
    /// comment is a comment at the top of file or function body that is separated from the next statement by space.
    /// </summary>
    private (int NodePos, int DetachedCommentEndPos)? EmitDetachedComments(int nodePos)
    {
        var text = CurrentText;
        var lineMap = GetCurrentLineMap();
        List<CommentRange>? leadingComments = null;
        if (commentsDisabled)
        {
            // removeComments is true, only reserve pinned comment at the top of file
            // For example:
            //      /*! Pinned Comment */
            //
            //      var x = 10;
            if (nodePos == 0)
            {
                if (Scanner.GetLeadingCommentRanges(text, nodePos) is { } all)
                {
                    leadingComments = [];
                    foreach (var c in all)
                    {
                        if (PrinterUtilities.IsPinnedComment(text, c.Pos))
                        {
                            leadingComments.Add(c);
                        }
                    }
                }
            }
        }
        else
        {
            // removeComments is false, just get detached as normal and bypass the process to filter comment
            leadingComments = Scanner.GetLeadingCommentRanges(text, nodePos);
        }

        if (leadingComments is not null)
        {
            var detachedComments = new List<CommentRange>();
            CommentRange? lastComment = null;

            foreach (var comment in leadingComments)
            {
                if (lastComment is { } last)
                {
                    var lastCommentLine = Scanner.ComputeLineOfPosition(lineMap, last.End);
                    var commentLine = Scanner.ComputeLineOfPosition(lineMap, comment.Pos);

                    if (commentLine >= lastCommentLine + 2)
                    {
                        // There was a blank line between the last comment and this comment.  This
                        // comment is not part of the copyright comments.  Return what we have so
                        // far.
                        break;
                    }
                }

                detachedComments.Add(comment);
                lastComment = comment;
            }

            if (detachedComments.Count > 0)
            {
                // All comments look like they could have been part of the copyright header.  Make
                // sure there is at least one blank line between it and the node.  If not, it's not
                // a copyright header.
                var lastCommentLine = Scanner.ComputeLineOfPosition(lineMap, detachedComments[^1].End);
                var nodeLine = Scanner.ComputeLineOfPosition(lineMap, Scanner.SkipTrivia(text, nodePos));
                if (nodeLine >= lastCommentLine + 2)
                {
                    // Valid detachedComments
                    // (emitNewLineBeforeLeadingComments)
                    if (leadingComments.Count > 0 && nodePos != leadingComments[0].Pos &&
                        Scanner.ComputeLineOfPosition(lineMap, nodePos) != Scanner.ComputeLineOfPosition(lineMap, leadingComments[0].Pos))
                    {
                        writer.WriteLine();
                    }
                    // emitComments(..., leadingSeparator: false, trailingSeparator: true, newLine, emitComment)
                    var emitInterveningSeparator = false;
                    foreach (var comment in detachedComments)
                    {
                        if (emitInterveningSeparator)
                        {
                            writer.WriteSpace(" ");
                            emitInterveningSeparator = false;
                        }

                        EmitComment(comment.Pos, comment.End);
                        if (comment.HasTrailingNewLine)
                        {
                            writer.WriteLine();
                        }
                        else
                        {
                            emitInterveningSeparator = true;
                        }
                    }
                    if (emitInterveningSeparator)
                    {
                        writer.WriteSpace(" ");
                    }
                    return (nodePos, detachedComments[^1].End);
                }
            }
        }

        return null;
    }

    private void EmitComment(int commentPos, int commentEnd)
    {
        if (currentSourceFile.IsNone || !ShouldWriteComment(CurrentText, commentPos))
        {
            return;
        }
        PrinterUtilities.WriteCommentRange(CurrentText, GetCurrentLineMap(), writer, commentPos, commentEnd, newLine);
    }

    /// <summary>Determine if the given comment is a triple-slash.</summary>
    private bool IsTripleSlashComment(int commentPos, int commentEnd)
    {
        return !currentSourceFile.IsNone && IsRecognizedTripleSlashComment(CurrentText, commentPos, commentEnd);
    }

    // utilities.ts fullTripleSlash*RegEx and defaultLibReferenceRegEx, with JavaScript's \s, \S and . spelled out.
    private const string JsWs = @"[\t\n\v\f\r    -     　﻿]";
    private const string JsNonWs = @"[^\t\n\v\f\r    -     　﻿]";
    private const string JsDot = @"[^\n\r  ]";
    private static readonly Regex[] TripleSlashRegexes =
    [
        new(@"^///" + JsWs + "*<reference" + JsWs + "+path" + JsWs + "*=" + JsWs + "*(?:'[^']*'|\"[^\"]*\")" + JsDot + "*?/>", RegexOptions.CultureInvariant),
        new(@"^///" + JsWs + "*<amd-dependency" + JsWs + "+path" + JsWs + "*=" + JsWs + "*(?:'[^']*'|\"[^\"]*\")" + JsDot + "*?/>", RegexOptions.CultureInvariant),
        new(@"^///" + JsWs + "*<amd-module" + JsWs + "+(?:" + JsNonWs + JsDot + "*?)??/>", RegexOptions.CultureInvariant),
        new(@"^///" + JsWs + "*<reference" + JsWs + "+types" + JsWs + "*=" + JsWs + "*(?:'[^']*'|\"[^\"]*\")" + JsDot + "*?/>", RegexOptions.CultureInvariant),
        new(@"^///" + JsWs + "*<reference" + JsWs + "+lib" + JsWs + "*=" + JsWs + "*(?:'[^']*'|\"[^\"]*\")" + JsDot + "*?/>", RegexOptions.CultureInvariant),
        new(@"^///" + JsWs + "*<reference" + JsWs + "+no-default-lib" + JsWs + "*=" + JsWs + "*(?:'[^']*'|\"[^\"]*\")" + JsWs + "*/>", RegexOptions.CultureInvariant),
    ];

    // utilities.ts isRecognizedTripleSlashComment
    private static bool IsRecognizedTripleSlashComment(ReadOnlySpan<char> text, int commentPos, int commentEnd)
    {
        // Verify this is /// comment, but do the regexp match only when we first can find /// in the comment text
        // so that we don't end up computing comment string and doing match for all // comments
        if (commentPos + 1 < text.Length && text[commentPos + 1] == '/' &&
            commentPos + 2 < commentEnd &&
            text[commentPos + 2] == '/')
        {
            var textSubStr = text[commentPos..commentEnd].ToString();
            foreach (var regex in TripleSlashRegexes)
            {
                if (regex.IsMatch(textSubStr))
                {
                    return true;
                }
            }
        }
        return false;
    }

    //
    // Tokens with comments
    //

    private int EmitTokenWithComment(SyntaxKind token, int pos, WriteKind kind, Node contextNode, bool indentLeading = false)
    {
        var node = UtilitiesPublic.GetParseTreeNode(store, contextNode);
        var isSimilarNode = !node.IsNone && store.Kind(node) == store.Kind(contextNode);
        var startPos = pos;
        if (isSimilarNode && !currentSourceFile.IsNone)
        {
            pos = Scanner.SkipTrivia(CurrentText, pos);
        }
        if (isSimilarNode && store.Pos(contextNode) != startPos)
        {
            var needsIndent = indentLeading && !currentSourceFile.IsNone && !PositionsAreOnSameLine(startPos, pos);
            if (needsIndent)
            {
                IncreaseIndent();
            }
            EmitLeadingCommentsOfPosition(startPos);
            if (needsIndent)
            {
                DecreaseIndent();
            }
        }

        // (writeToken and writeTokenText write the same without source maps)
        pos = WriteTokenText(token, kind, pos);

        if (isSimilarNode && store.End(contextNode) != pos)
        {
            var isJsxExprContext = store.Kind(contextNode) == SyntaxKind.JsxExpression;
            EmitTrailingCommentsOfPosition(pos, prefixSpace: !isJsxExprContext, forceNoNewline: isJsxExprContext);
        }
        return pos;
    }

    private static bool CommentWillEmitNewLine(SyntaxKind kind, bool? hasTrailingNewLine)
    {
        return kind == SyntaxKind.SingleLineCommentTrivia || hasTrailingNewLine == true;
    }

    private bool WillEmitLeadingNewLine(Node node)
    {
        if (currentSourceFile.IsNone)
        {
            return false;
        }
        var text = CurrentText;
        var leadingCommentRanges = Scanner.GetLeadingCommentRanges(text, store.Pos(node));
        if (leadingCommentRanges is not null)
        {
            var parseNode = UtilitiesPublic.GetParseTreeNode(store, node);
            if (!parseNode.IsNone && !store.Parent(parseNode).IsNone && store.Kind(store.Parent(parseNode)) == SyntaxKind.ParenthesizedExpression)
            {
                return true;
            }
            foreach (var range in leadingCommentRanges)
            {
                if (CommentWillEmitNewLine(range.Kind, range.HasTrailingNewLine))
                {
                    return true;
                }
            }
        }
        if (FactoryEmitNode.GetSyntheticLeadingComments(store, node) is { } synthetic)
        {
            foreach (var comment in synthetic)
            {
                if (CommentWillEmitNewLine(comment.Kind, comment.HasTrailingNewLine))
                {
                    return true;
                }
            }
        }
        if (store.Kind(node) == SyntaxKind.PartiallyEmittedExpression)
        {
            var expression = store.Expression(node);
            if (store.Pos(node) != store.Pos(expression))
            {
                if (Scanner.GetTrailingCommentRanges(text, store.Pos(expression)) is { } trailing)
                {
                    foreach (var range in trailing)
                    {
                        if (CommentWillEmitNewLine(range.Kind, range.HasTrailingNewLine))
                        {
                            return true;
                        }
                    }
                }
            }
            return WillEmitLeadingNewLine(expression);
        }
        return false;
    }

    /// <summary>
    /// Wraps an expression in parens if we would emit a leading comment that would introduce a line separator
    /// between the node and its parent.
    /// <para>
    /// Track O: TypeScript rebuilds the left spine of the expression with factory.update* calls, which return the node
    /// itself unless a part changed; a part changes only below a partially emitted expression that would emit a leading
    /// new line, which then is parenthesized. Only transformers create partially emitted expressions; the printer has no
    /// node factory, so that case throws until the transformation pipeline (D1/D2) provides one.
    /// </para>
    /// </summary>
    private Node ParenthesizeExpressionForNoAsi(Node node)
    {
        if (commentsDisabled || node.IsNone)
        {
            return node;
        }
        var current = node;
        while (true)
        {
            switch (store.Kind(current))
            {
                case SyntaxKind.PartiallyEmittedExpression:
                    if (WillEmitLeadingNewLine(current))
                    {
                        throw new NotSupportedException("parenthesizeExpressionForNoAsi of a partially emitted expression with a leading new line needs the node factory (module D1).");
                    }
                    current = store.Expression(current);
                    continue;
                case SyntaxKind.PropertyAccessExpression:
                case SyntaxKind.ElementAccessExpression:
                case SyntaxKind.CallExpression:
                case SyntaxKind.AsExpression:
                case SyntaxKind.SatisfiesExpression:
                case SyntaxKind.NonNullExpression:
                    current = store.Expression(current);
                    continue;
                case SyntaxKind.TaggedTemplateExpression:
                    current = store.Tag(current);
                    continue;
                case SyntaxKind.PostfixUnaryExpression:
                    current = store.Operand(current);
                    continue;
                case SyntaxKind.BinaryExpression:
                    current = store.Left(current);
                    continue;
                case SyntaxKind.ConditionalExpression:
                    current = store.Condition(current);
                    continue;
            }
            return node;
        }
    }
}
