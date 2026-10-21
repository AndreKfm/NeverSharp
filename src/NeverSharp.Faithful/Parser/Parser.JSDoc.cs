// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/parser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/parser/jsdoc.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// parser.ts: `namespace JSDocParser` (entry points, the comment loop, comment text and links). The tags are in
/// Parser.JSDocTags.cs. TypeScript keeps the state of parseJSDocCommentWorker in closure variables; here the fields
/// below hold it, saved and restored around each call, so that a nested call would see its own state as with closures.
/// </summary>
internal sealed partial class Parser
{
    private enum JSDocState
    {
        BeginningOfLine,
        SawAsterisk,
        SavingComments,
        SavingBackticks, // NOTE: Only used when parsing tag comments
    }

    [Flags]
    private enum PropertyLikeParse
    {
        Property = 1 << 0,
        Parameter = 1 << 1,
        CallbackParameter = 1 << 2,
    }

    // State of parseJSDocCommentWorker (closure variables in TypeScript).
    private NodeArray? jsDocTags;
    private int jsDocTagsPos;
    private int jsDocTagsEnd;
    private int? jsDocLinkEnd;
    private int? jsDocCommentsPos;
    private List<string> jsDocComments = [];
    private NodeArray jsDocParts = [];

    // Parses out a JSDoc type expression.
    private JSDocTypeExpression ParseJSDocTypeExpression(bool mayOmitBraces = false)
    {
        var pos = GetNodePos();
        var hasBrace = mayOmitBraces ? ParseOptional(SyntaxKind.OpenBraceToken) : ParseExpected(SyntaxKind.OpenBraceToken);
        var type = DoInsideOfContext(NodeFlags.JSDoc, static p => p.ParseJSDocType());
        if (!mayOmitBraces || hasBrace)
        {
            ParseExpectedJSDoc(SyntaxKind.CloseBraceToken);
        }

        var result = factory.CreateJSDocTypeExpression(type);
        FixupParentReferences(result);
        return FinishNode(result, pos);
    }

    private JSDocNameReference ParseJSDocNameReference()
    {
        var pos = GetNodePos();
        var hasBrace = ParseOptional(SyntaxKind.OpenBraceToken);
        var p2 = GetNodePos();
        var entityName = ParseEntityName(/*allowReservedWords*/ false);
        while (Token() == SyntaxKind.PrivateIdentifier)
        {
            ReScanHashToken(); // rescan #id as # id
            NextTokenJSDoc(); // then skip the #
            entityName = FinishNode(factory.CreateJSDocMemberName(entityName, ParseIdentifier()), p2);
        }
        if (hasBrace)
        {
            ParseExpectedJSDoc(SyntaxKind.CloseBraceToken);
        }

        var result = factory.CreateJSDocNameReference(entityName);
        FixupParentReferences(result);
        return FinishNode(result, pos);
    }

    private JSDoc? JSDocParserParseJSDocComment(Node parent, int start, int length)
    {
        var saveToken = currentToken;
        var saveParseDiagnosticsLength = parseDiagnostics.Count;
        var saveParseErrorBeforeNextFinishedNode = parseErrorBeforeNextFinishedNode;

        var comment = DoInsideOfContext(NodeFlags.JSDoc, p => p.ParseJSDocCommentWorker(start, length));
        Utilities.SetParent(comment, parent);

        if ((contextFlags & NodeFlags.JavaScriptFile) != 0)
        {
            jsDocDiagnostics ??= [];
            Core.AddRange(jsDocDiagnostics, parseDiagnostics, saveParseDiagnosticsLength);
        }
        currentToken = saveToken;
        parseDiagnostics.RemoveRange(saveParseDiagnosticsLength, parseDiagnostics.Count - saveParseDiagnosticsLength);
        parseErrorBeforeNextFinishedNode = saveParseErrorBeforeNextFinishedNode;
        return comment;
    }

    private JSDoc? ParseJSDocCommentWorker(int start = 0, int? length = null)
    {
        var content = sourceText;
        var end = length == null ? content.Length : start + length.Value;
        var len = end - start;

        Debug.Assert(start >= 0);
        Debug.Assert(start <= end);
        Debug.Assert(end <= content.Length);

        // Check for /** (JSDoc opening part)
        if (!IsJSDocLikeText(content, start))
        {
            return null;
        }

        var saveTags = jsDocTags;
        var saveTagsPos = jsDocTagsPos;
        var saveTagsEnd = jsDocTagsEnd;
        var saveLinkEnd = jsDocLinkEnd;
        var saveCommentsPos = jsDocCommentsPos;
        var saveComments = jsDocComments;
        var saveParts = jsDocParts;

        jsDocTags = null;
        jsDocTagsPos = 0;
        jsDocTagsEnd = 0;
        jsDocLinkEnd = null;
        jsDocCommentsPos = null;
        jsDocComments = [];
        jsDocParts = [];

        var saveParsingContext = parsingContext;
        parsingContext |= 1 << (int)ParsingContext.JSDocComment;

        // + 3 for leading /**, - 5 in total for /** */
        var result = scanner.ScanRange(start + 3, len - 5, () => DoJSDocScan(start, end, content));
        parsingContext = saveParsingContext;

        jsDocTags = saveTags;
        jsDocTagsPos = saveTagsPos;
        jsDocTagsEnd = saveTagsEnd;
        jsDocLinkEnd = saveLinkEnd;
        jsDocCommentsPos = saveCommentsPos;
        jsDocComments = saveComments;
        jsDocParts = saveParts;
        return result;
    }

    private JSDoc DoJSDocScan(int start, int end, string content)
    {
        // Initially we can parse out a tag.  We also have seen a starting asterisk.
        // This is so that /** * @type */ doesn't parse.
        var state = JSDocState.SawAsterisk;
        int? margin = null;
        // + 4 for leading '/** '
        // + 1 because the last index of \n is always one index before the first character in the line and coincidentally, if there is no \n before start, it is -1, which is also one index before the first character
        var indent = start - (content.LastIndexOf('\n', start) + 1) + 4;

        NextTokenJSDoc();
        while (ParseOptionalJsdoc(SyntaxKind.WhitespaceTrivia))
        {
        }
        if (ParseOptionalJsdoc(SyntaxKind.NewLineTrivia))
        {
            state = JSDocState.BeginningOfLine;
            indent = 0;
        }
        while (true)
        {
            switch (Token())
            {
                case SyntaxKind.AtToken:
                    RemoveTrailingWhitespace(jsDocComments);
                    if (jsDocCommentsPos is null or 0)
                    {
                        jsDocCommentsPos = GetNodePos();
                    }
                    AddTag(ParseTag(indent));
                    // NOTE: According to usejsdoc.org, a tag goes to end of line, except the last tag.
                    // Real-world comments may break this rule, so "BeginningOfLine" will not be a real line beginning
                    // for malformed examples like `/** @param {string} x @returns {number} the length */`
                    state = JSDocState.BeginningOfLine;
                    margin = null;
                    break;
                case SyntaxKind.NewLineTrivia:
                    jsDocComments.Add(scanner.GetTokenText());
                    state = JSDocState.BeginningOfLine;
                    indent = 0;
                    break;
                case SyntaxKind.AsteriskToken:
                    {
                        var asterisk = scanner.GetTokenText();
                        if (state == JSDocState.SawAsterisk)
                        {
                            // If we've already seen an asterisk, then we can no longer parse a tag on this line
                            state = JSDocState.SavingComments;
                            PushComment(asterisk);
                        }
                        else
                        {
                            Debug.Assert(state == JSDocState.BeginningOfLine);
                            // Ignore the first asterisk on a line
                            state = JSDocState.SawAsterisk;
                            indent += asterisk.Length;
                        }
                        break;
                    }
                case SyntaxKind.WhitespaceTrivia:
                    {
                        Debug.Assert(state != JSDocState.SavingComments, "whitespace shouldn't come from the scanner while saving top-level comment text");
                        // only collect whitespace if we're already saving comments or have just crossed the comment indent margin
                        var whitespace = scanner.GetTokenText();
                        if (margin != null && indent + whitespace.Length > margin.Value)
                        {
                            jsDocComments.Add(JsSlice(whitespace, margin.Value - indent));
                        }
                        indent += whitespace.Length;
                        break;
                    }
                case SyntaxKind.EndOfFileToken:
                    goto endLoop;
                case SyntaxKind.JSDocCommentTextToken:
                    state = JSDocState.SavingComments;
                    PushComment(scanner.GetTokenValue());
                    break;
                case SyntaxKind.OpenBraceToken:
                    {
                        state = JSDocState.SavingComments;
                        var commentEnd = scanner.GetTokenFullStart();
                        var linkStart = scanner.GetTokenEnd() - 1;
                        var link = ParseJSDocLink(linkStart);
                        if (link != null)
                        {
                            if (jsDocLinkEnd is null or 0)
                            {
                                RemoveLeadingNewlines(jsDocComments);
                            }
                            jsDocParts.Add(FinishNode(factory.CreateJSDocText(string.Concat(jsDocComments)), jsDocLinkEnd ?? start, commentEnd));
                            jsDocParts.Add(link);
                            jsDocComments = [];
                            jsDocLinkEnd = scanner.GetTokenEnd();
                            break;
                        }
                        // fallthrough if it's not a {@link sequence
                        goto default;
                    }
                default:
                    // Anything else is doc comment text. We just save it. Because it
                    // wasn't a tag, we can no longer parse a tag on this line until we hit the next
                    // line break.
                    state = JSDocState.SavingComments;
                    PushComment(scanner.GetTokenText());
                    break;
            }
            if (state == JSDocState.SavingComments)
            {
                NextJSDocCommentTextToken(/*inBackticks*/ false);
            }
            else
            {
                NextTokenJSDoc();
            }
        }
    endLoop:
        var trimmedComments = JsTrimEnd(string.Concat(jsDocComments));
        if (jsDocParts.Count != 0 && trimmedComments.Length != 0)
        {
            jsDocParts.Add(FinishNode(factory.CreateJSDocText(trimmedComments), jsDocLinkEnd ?? start, jsDocCommentsPos));
        }
        if (jsDocParts.Count != 0 && jsDocTags != null)
        {
            Debug.AssertIsDefined(jsDocCommentsPos, "having parsed tags implies that the end of the comment span should be set");
        }
        var tagsArray = jsDocTags != null ? CreateNodeArray(jsDocTags, jsDocTagsPos, jsDocTagsEnd) : null;
        object? comment = jsDocParts.Count != 0 ? CreateNodeArray(jsDocParts, start, jsDocCommentsPos) : trimmedComments.Length != 0 ? trimmedComments : null;
        return FinishNode(factory.CreateJSDocComment(comment, tagsArray), start, end);

        void PushComment(string text)
        {
            if (margin is null or 0)
            {
                margin = indent;
            }
            jsDocComments.Add(text);
            indent += text.Length;
        }
    }

    /// <summary>`text.slice(start)` with JavaScript's handling of a negative start.</summary>
    private static string JsSlice(string text, int start)
    {
        var from = start < 0 ? Math.Max(text.Length + start, 0) : Math.Min(start, text.Length);
        return text.Substring(from);
    }

    /// <summary>`text.trimEnd()`.</summary>
    private static string JsTrimEnd(string text)
    {
        var end = text.Length;
        while (end > 0 && Utilities.IsJsWhiteSpaceOrLineTerminator(text[end - 1]))
        {
            end--;
        }
        return end == text.Length ? text : text.Substring(0, end);
    }

    private static void RemoveLeadingNewlines(List<string> comments)
    {
        while (comments.Count != 0 && (comments[0] == "\n" || comments[0] == "\r"))
        {
            comments.RemoveAt(0);
        }
    }

    private static void RemoveTrailingWhitespace(List<string> comments)
    {
        while (comments.Count != 0)
        {
            var trimmed = JsTrimEnd(comments[^1]);
            if (trimmed.Length == 0)
            {
                comments.RemoveAt(comments.Count - 1);
            }
            else if (trimmed.Length < comments[^1].Length)
            {
                comments[^1] = trimmed;
                break;
            }
            else
            {
                break;
            }
        }
    }

    private bool IsNextNonwhitespaceTokenEndOfFile()
    {
        // We must use infinite lookahead, as there could be any number of newlines :(
        while (true)
        {
            NextTokenJSDoc();
            if (Token() == SyntaxKind.EndOfFileToken)
            {
                return true;
            }
            if (!(Token() == SyntaxKind.WhitespaceTrivia || Token() == SyntaxKind.NewLineTrivia))
            {
                return false;
            }
        }
    }

    private void SkipWhitespace()
    {
        if (Token() == SyntaxKind.WhitespaceTrivia || Token() == SyntaxKind.NewLineTrivia)
        {
            if (LookAhead(static p => p.IsNextNonwhitespaceTokenEndOfFile()))
            {
                return; // Don't skip whitespace prior to EoF (or end of comment) - that shouldn't be included in any node's range
            }
        }
        while (Token() == SyntaxKind.WhitespaceTrivia || Token() == SyntaxKind.NewLineTrivia)
        {
            NextTokenJSDoc();
        }
    }

    private string SkipWhitespaceOrAsterisk()
    {
        if (Token() == SyntaxKind.WhitespaceTrivia || Token() == SyntaxKind.NewLineTrivia)
        {
            if (LookAhead(static p => p.IsNextNonwhitespaceTokenEndOfFile()))
            {
                return ""; // Don't skip whitespace prior to EoF (or end of comment) - that shouldn't be included in any node's range
            }
        }

        var precedingLineBreak = scanner.HasPrecedingLineBreak();
        var seenLineBreak = false;
        var indentText = "";
        while ((precedingLineBreak && Token() == SyntaxKind.AsteriskToken) || Token() == SyntaxKind.WhitespaceTrivia || Token() == SyntaxKind.NewLineTrivia)
        {
            indentText += scanner.GetTokenText();
            if (Token() == SyntaxKind.NewLineTrivia)
            {
                precedingLineBreak = true;
                seenLineBreak = true;
                indentText = "";
            }
            else if (Token() == SyntaxKind.AsteriskToken)
            {
                precedingLineBreak = false;
            }
            NextTokenJSDoc();
        }
        return seenLineBreak ? indentText : "";
    }

    private object? ParseTrailingTagComments(int pos, int end, int margin, string indentText)
    {
        // some tags, like typedef and callback, have already parsed their comments earlier
        if (indentText.Length == 0)
        {
            margin += end - pos;
        }
        return ParseTagComments(margin, JsSlice(indentText, margin));
    }

    /// <summary>Returns a string, a NodeArray of JSDocComment or null.</summary>
    private object? ParseTagComments(int indent, string? initialMargin = null)
    {
        var commentsPos = GetNodePos();
        var comments = new List<string>();
        var parts = new NodeArray();
        int? linkEnd = null;
        var state = JSDocState.BeginningOfLine;
        int? margin = null;
        if (initialMargin != null)
        {
            // jump straight to saving comments if there is some initial indentation
            if (initialMargin.Length != 0)
            {
                PushComment(initialMargin);
            }
            state = JSDocState.SawAsterisk;
        }
        var tok = Token();
        while (true)
        {
            switch (tok)
            {
                case SyntaxKind.NewLineTrivia:
                    state = JSDocState.BeginningOfLine;
                    // don't use pushComment here because we want to keep the margin unchanged
                    comments.Add(scanner.GetTokenText());
                    indent = 0;
                    break;
                case SyntaxKind.AtToken:
                    scanner.ResetTokenState(scanner.GetTokenEnd() - 1);
                    goto endLoop;
                case SyntaxKind.EndOfFileToken:
                    // Done
                    goto endLoop;
                case SyntaxKind.WhitespaceTrivia:
                    {
                        Debug.Assert(state != JSDocState.SavingComments && state != JSDocState.SavingBackticks, "whitespace shouldn't come from the scanner while saving comment text");
                        var whitespace = scanner.GetTokenText();
                        // if the whitespace crosses the margin, take only the whitespace that passes the margin
                        if (margin != null && indent + whitespace.Length > margin.Value)
                        {
                            comments.Add(JsSlice(whitespace, margin.Value - indent));
                            state = JSDocState.SavingComments;
                        }
                        indent += whitespace.Length;
                        break;
                    }
                case SyntaxKind.OpenBraceToken:
                    {
                        state = JSDocState.SavingComments;
                        var commentEnd = scanner.GetTokenFullStart();
                        var linkStart = scanner.GetTokenEnd() - 1;
                        var link = ParseJSDocLink(linkStart);
                        if (link != null)
                        {
                            parts.Add(FinishNode(factory.CreateJSDocText(string.Concat(comments)), linkEnd ?? commentsPos, commentEnd));
                            parts.Add(link);
                            comments = [];
                            linkEnd = scanner.GetTokenEnd();
                        }
                        else
                        {
                            PushComment(scanner.GetTokenText());
                        }
                        break;
                    }
                case SyntaxKind.BacktickToken:
                    if (state == JSDocState.SavingBackticks)
                    {
                        state = JSDocState.SavingComments;
                    }
                    else
                    {
                        state = JSDocState.SavingBackticks;
                    }
                    PushComment(scanner.GetTokenText());
                    break;
                case SyntaxKind.JSDocCommentTextToken:
                    if (state != JSDocState.SavingBackticks)
                    {
                        state = JSDocState.SavingComments; // leading identifiers start recording as well
                    }
                    PushComment(scanner.GetTokenValue());
                    break;
                case SyntaxKind.AsteriskToken:
                    if (state == JSDocState.BeginningOfLine)
                    {
                        // leading asterisks start recording on the *next* (non-whitespace) token
                        state = JSDocState.SawAsterisk;
                        indent += 1;
                        break;
                    }
                    // record the * as a comment
                    goto default;
                default:
                    if (state != JSDocState.SavingBackticks)
                    {
                        state = JSDocState.SavingComments; // leading identifiers start recording as well
                    }
                    PushComment(scanner.GetTokenText());
                    break;
            }
            if (state == JSDocState.SavingComments || state == JSDocState.SavingBackticks)
            {
                tok = NextJSDocCommentTextToken(state == JSDocState.SavingBackticks);
            }
            else
            {
                tok = NextTokenJSDoc();
            }
        }
    endLoop:

        RemoveLeadingNewlines(comments);
        var trimmedComments = JsTrimEnd(string.Concat(comments));
        if (parts.Count != 0)
        {
            if (trimmedComments.Length != 0)
            {
                parts.Add(FinishNode(factory.CreateJSDocText(trimmedComments), linkEnd ?? commentsPos));
            }
            return CreateNodeArray(parts, commentsPos, scanner.GetTokenEnd());
        }
        else if (trimmedComments.Length != 0)
        {
            return trimmedComments;
        }
        return null;

        void PushComment(string text)
        {
            if (margin is null or 0)
            {
                margin = indent;
            }
            comments.Add(text);
            indent += text.Length;
        }
    }

    private Node? ParseJSDocLink(int start)
    {
        var linkType = TryParse(static p => p.ParseJSDocLinkPrefix());
        if (linkType == null)
        {
            return null;
        }
        NextTokenJSDoc(); // start at token after link, then skip any whitespace
        SkipWhitespace();
        var name = ParseJSDocLinkName();
        var text = new List<string>();
        while (Token() != SyntaxKind.CloseBraceToken && Token() != SyntaxKind.NewLineTrivia && Token() != SyntaxKind.EndOfFileToken)
        {
            text.Add(scanner.GetTokenText());
            NextTokenJSDoc();
        }
        var joined = string.Concat(text);
        Node create = linkType == "link" ? factory.CreateJSDocLink(name, joined)
            : linkType == "linkcode" ? factory.CreateJSDocLinkCode(name, joined)
            : factory.CreateJSDocLinkPlain(name, joined);
        return FinishNode(create, start, scanner.GetTokenEnd());
    }

    private Node? ParseJSDocLinkName()
    {
        if (Scanner.TokenIsIdentifierOrKeyword(Token()))
        {
            var pos = GetNodePos();

            Node name = ParseIdentifierName();
            while (ParseOptional(SyntaxKind.DotToken))
            {
                name = FinishNode(factory.CreateQualifiedName(name, Token() == SyntaxKind.PrivateIdentifier ? CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ false) : ParseIdentifierName()), pos);
            }
            while (Token() == SyntaxKind.PrivateIdentifier)
            {
                ReScanHashToken();
                NextTokenJSDoc();
                name = FinishNode(factory.CreateJSDocMemberName(name, ParseIdentifier()), pos);
            }
            return name;
        }
        return null;
    }

    private string? ParseJSDocLinkPrefix()
    {
        SkipWhitespaceOrAsterisk();
        if (
            Token() == SyntaxKind.OpenBraceToken
            && NextTokenJSDoc() == SyntaxKind.AtToken
            && Scanner.TokenIsIdentifierOrKeyword(NextTokenJSDoc()))
        {
            var kind = scanner.GetTokenValue();
            if (IsJSDocLinkTag(kind))
            {
                return kind;
            }
        }
        return null;
    }

    private static bool IsJSDocLinkTag(string kind)
    {
        return kind == "link" || kind == "linkcode" || kind == "linkplain";
    }

    private void AddTag(Node? tag)
    {
        if (tag == null)
        {
            return;
        }
        if (jsDocTags == null)
        {
            jsDocTags = [tag];
            jsDocTagsPos = tag.Pos;
        }
        else
        {
            jsDocTags.Add(tag);
        }
        jsDocTagsEnd = tag.End;
    }

    private bool ParseOptionalJsdoc(SyntaxKind t)
    {
        if (Token() == t)
        {
            NextTokenJSDoc();
            return true;
        }
        return false;
    }

    private Node ParseJSDocEntityName()
    {
        Node entity = ParseJSDocIdentifierName();
        if (ParseOptional(SyntaxKind.OpenBracketToken))
        {
            ParseExpected(SyntaxKind.CloseBracketToken);
            // Note that y[] is accepted as an entity name, but the postfix brackets are not saved for checking.
            // Technically usejsdoc.org requires them for specifying a property of a type equivalent to Array<{ x: ...}>
            // but it's not worth it to enforce that restriction.
        }
        while (ParseOptional(SyntaxKind.DotToken))
        {
            var name = ParseJSDocIdentifierName();
            if (ParseOptional(SyntaxKind.OpenBracketToken))
            {
                ParseExpected(SyntaxKind.CloseBracketToken);
            }
            entity = CreateQualifiedName(entity, name);
        }
        return entity;
    }

    private Identifier ParseJSDocIdentifierName(DiagnosticMessage? message = null)
    {
        if (!Scanner.TokenIsIdentifierOrKeyword(Token()))
        {
            return CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ message == null, message ?? Diagnostics.Identifier_expected);
        }

        identifierCount++;
        var start = scanner.GetTokenStart();
        var end = scanner.GetTokenEnd();
        var originalKeywordKind = Token();
        var text = InternIdentifier(scanner.GetTokenValue());
        var result = FinishNode(factory.CreateIdentifier(text, originalKeywordKind), start, end);
        NextTokenJSDoc();
        return result;
    }
}
