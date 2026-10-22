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

namespace NeverSharp.Optimized;

/// <summary>
/// parser.ts: `namespace JSDocParser` (without the test and language-service entry points parseIsolatedJSDocComment and
/// parseJSDocTypeExpressionForTests). As in TypeScript, where it is a namespace nested in `Parser`, it uses the
/// parser's state; the inner functions of parseJSDocCommentWorker are methods of the parser (as in
/// internal/parser/jsdoc.go), and the variables of the worker's closure that they share are fields.
/// </summary>
internal sealed partial class Parser
{
    /// <summary>parser.ts: JSDocParser's JSDocState.</summary>
    private enum JSDocState
    {
        BeginningOfLine,
        SawAsterisk,
        SavingComments,
        SavingBackticks, // NOTE: Only used when parsing tag comments
    }

    /// <summary>parser.ts: JSDocParser's PropertyLikeParse.</summary>
    [Flags]
    private enum PropertyLikeParse
    {
        Property = 1 << 0,
        Parameter = 1 << 1,
        CallbackParameter = 1 << 2,
    }

    // The variables of parseJSDocCommentWorker's closure read outside doJSDocScan: `tags` (addTag, parseReturnTag,
    // parseTypeTag). The worker saves and restores it, since a JSDoc comment may be parsed while another is.
    // (Track O: `tags` is a list under construction once hasJsDocTags is set; undefined before.)
    private ListBuilder jsDocTags;
    private bool hasJsDocTags;

    // The worker's `start` and `end`, read by doJSDocScan (the scanner's callback).
    private int jsDocWorkerStart;
    private int jsDocWorkerEnd;
    private Func<Node>? doJSDocScanCallback;

    // Parses out a JSDoc type expression.
    private Node ParseJSDocTypeExpression(bool mayOmitBraces = false)
    {
        var pos = GetNodePos();
        var hasBrace = mayOmitBraces ? ParseOptional(SyntaxKind.OpenBraceToken) : ParseExpected(SyntaxKind.OpenBraceToken);
        var type = DoInsideOfContext(NodeFlags.JSDoc, static p => p.ParseJSDocType());
        if (!mayOmitBraces || hasBrace)
        {
            ParseExpectedJSDoc(SyntaxKind.CloseBraceToken);
        }

        var result = factory.CreateJSDocTypeExpression(type);
        FixupParentReferences(store, result);
        return FinishNode(result, pos);
    }

    private Node ParseJSDocNameReference()
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
        FixupParentReferences(store, result);
        return FinishNode(result, pos);
    }

    /// <summary>parser.ts: JSDocParser.parseJSDocComment(parent, start, length).</summary>
    private Node JSDocParserParseJSDocComment(Node parent, int start, int length)
    {
        var saveToken = currentToken;
        var saveParseDiagnosticsLength = parseDiagnostics.Count;
        var saveParseErrorBeforeNextFinishedNode = parseErrorBeforeNextFinishedNode;

        // (TypeScript: `doInsideOfContext(NodeFlags.JSDoc, () => parseJSDocCommentWorker(start, length))`; the
        // closure is the body of DoInsideOfContext, written out, as typescript-go's parseJSDocComment in
        // internal/parser/jsdoc.go sets NodeFlagsJSDoc and calls parseJSDocCommentWorker directly.)
        Node comment;
        var contextFlagsToSet = NodeFlags.JSDoc & ~contextFlags;
        if (contextFlagsToSet != 0)
        {
            SetContextFlag(/*val*/ true, contextFlagsToSet);
            comment = ParseJSDocCommentWorker(start, length);
            SetContextFlag(/*val*/ false, contextFlagsToSet);
        }
        else
        {
            comment = ParseJSDocCommentWorker(start, length);
        }
        Utilities.SetParent(store, comment, parent);

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

    private Node ParseJSDocCommentWorker(int start, int? length)
    {
        var content = sourceText;
        var end = length == null ? content.Length : start + length.Value;
        length = end - start;

        Debug.Assert(start >= 0);
        Debug.Assert(start <= end);
        Debug.Assert(end <= content.Length);

        // Check for /** (JSDoc opening part)
        if (!IsJSDocLikeText(content, start))
        {
            return default;
        }

        var saveTags = jsDocTags;
        var saveHasTags = hasJsDocTags;
        hasJsDocTags = false;

        var saveParsingContext = parsingContext;
        parsingContext |= 1 << (int)ParsingContext.JSDocComment;

        // + 3 for leading /**, - 5 in total for /** */
        jsDocWorkerStart = start;
        jsDocWorkerEnd = end;
        var result = scanner.ScanRange(start + 3, length.Value - 5, doJSDocScanCallback ??= DoJSDocScan);
        parsingContext = saveParsingContext;
        jsDocTags = saveTags;
        hasJsDocTags = saveHasTags;
        return result;
    }

    private Node DoJSDocScan()
    {
        var content = sourceText;
        var start = jsDocWorkerStart;
        var end = jsDocWorkerEnd;
        int tagsPos = 0;
        int tagsEnd = 0;
        int? linkEnd = null;
        int? commentsPos = null;
        List<string> comments = [];
        var parts = store.BeginList();

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
            var exitLoop = false; // TypeScript: `break loop;`
            switch (Token())
            {
                case SyntaxKind.AtToken:
                    RemoveTrailingWhitespace(comments);
                    if (commentsPos is null or 0)
                    {
                        commentsPos = GetNodePos();
                    }
                    AddTag(ParseTag(indent), ref tagsPos, ref tagsEnd);
                    // NOTE: According to usejsdoc.org, a tag goes to end of line, except the last tag.
                    // Real-world comments may break this rule, so "BeginningOfLine" will not be a real line beginning
                    // for malformed examples like `/** @param {string} x @returns {number} the length */`
                    state = JSDocState.BeginningOfLine;
                    margin = null;
                    break;
                case SyntaxKind.NewLineTrivia:
                    comments.Add(scanner.GetTokenText());
                    state = JSDocState.BeginningOfLine;
                    indent = 0;
                    break;
                case SyntaxKind.AsteriskToken:
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
                case SyntaxKind.WhitespaceTrivia:
                    Debug.Assert(state != JSDocState.SavingComments, "whitespace shouldn't come from the scanner while saving top-level comment text");
                    // only collect whitespace if we're already saving comments or have just crossed the comment indent margin
                    var whitespace = scanner.GetTokenText();
                    if (margin != null && indent + whitespace.Length > margin.Value)
                    {
                        comments.Add(JsSlice(whitespace, margin.Value - indent));
                    }
                    indent += whitespace.Length;
                    break;
                case SyntaxKind.EndOfFileToken:
                    exitLoop = true;
                    break;
                case SyntaxKind.JSDocCommentTextToken:
                    state = JSDocState.SavingComments;
                    PushComment(scanner.GetTokenValue());
                    break;
                case SyntaxKind.OpenBraceToken:
                    state = JSDocState.SavingComments;
                    var commentEnd = scanner.GetTokenFullStart();
                    var linkStart = scanner.GetTokenEnd() - 1;
                    var link = ParseJSDocLink(linkStart);
                    if (!link.IsNone)
                    {
                        if (linkEnd is null or 0)
                        {
                            RemoveLeadingNewlines(comments);
                        }
                        var text = FinishNode(factory.CreateJSDocText(string.Concat(comments)), linkEnd ?? start, commentEnd);
                        store.Push(parts, text);
                        store.Push(parts, link);
                        comments = [];
                        linkEnd = scanner.GetTokenEnd();
                        break;
                    }
                    // fallthrough if it's not a {@link sequence
                    goto default;
                default:
                    // Anything else is doc comment text. We just save it. Because it
                    // wasn't a tag, we can no longer parse a tag on this line until we hit the next
                    // line break.
                    state = JSDocState.SavingComments;
                    PushComment(scanner.GetTokenText());
                    break;
            }
            if (exitLoop)
            {
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
        var trimmedComments = JsTrimEnd(string.Concat(comments));
        // (Track O: the tags are turned into a NodeArray before the last part is pushed, not after: `tags` was opened
        // after `parts` and is finished first, so both lists are built at the top of the scratch stack. Creating a
        // list creates no node, so the order of the nodes is TypeScript's.)
        var tagsArray = hasJsDocTags ? CreateNodeArray(jsDocTags, tagsPos, tagsEnd) : default;
        if (store.Length(parts) != 0 && trimmedComments.Length != 0)
        {
            var text = FinishNode(factory.CreateJSDocText(trimmedComments), linkEnd ?? start, commentsPos);
            store.Push(parts, text);
        }
        if (store.Length(parts) != 0 && hasJsDocTags)
        {
            Debug.Assert(commentsPos != null, "having parsed tags implies that the end of the comment span should be set");
        }
        object? comment;
        if (store.Length(parts) != 0)
        {
            comment = CreateNodeArray(parts, start, commentsPos);
        }
        else
        {
            store.DiscardList(parts);
            comment = trimmedComments.Length != 0 ? trimmedComments : null;
        }
        return FinishNode(factory.CreateJSDocComment(comment, tagsArray), start, end);

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

    private Node ParseTag(int margin)
    {
        Debug.Assert(Token() == SyntaxKind.AtToken);
        var start = scanner.GetTokenStart();
        NextTokenJSDoc();

        var tagName = ParseJSDocIdentifierName(/*message*/ null);
        var indentText = SkipWhitespaceOrAsterisk();

        Node tag;
        switch ((string)store.EscapedText(tagName)!)
        {
            case "author":
                tag = ParseAuthorTag(start, tagName, margin, indentText);
                break;
            case "implements":
                tag = ParseImplementsTag(start, tagName, margin, indentText);
                break;
            case "augments":
            case "extends":
                tag = ParseAugmentsTag(start, tagName, margin, indentText);
                break;
            case "class":
            case "constructor":
                tag = ParseSimpleTag(start, static (f, t, c) => f.CreateJSDocClassTag(t, c), tagName, margin, indentText);
                break;
            case "public":
                tag = ParseSimpleTag(start, static (f, t, c) => f.CreateJSDocPublicTag(t, c), tagName, margin, indentText);
                break;
            case "private":
                tag = ParseSimpleTag(start, static (f, t, c) => f.CreateJSDocPrivateTag(t, c), tagName, margin, indentText);
                break;
            case "protected":
                tag = ParseSimpleTag(start, static (f, t, c) => f.CreateJSDocProtectedTag(t, c), tagName, margin, indentText);
                break;
            case "readonly":
                tag = ParseSimpleTag(start, static (f, t, c) => f.CreateJSDocReadonlyTag(t, c), tagName, margin, indentText);
                break;
            case "override":
                tag = ParseSimpleTag(start, static (f, t, c) => f.CreateJSDocOverrideTag(t, c), tagName, margin, indentText);
                break;
            case "deprecated":
                hasDeprecatedTag = true;
                tag = ParseSimpleTag(start, static (f, t, c) => f.CreateJSDocDeprecatedTag(t, c), tagName, margin, indentText);
                break;
            case "this":
                tag = ParseThisTag(start, tagName, margin, indentText);
                break;
            case "enum":
                tag = ParseEnumTag(start, tagName, margin, indentText);
                break;
            case "arg":
            case "argument":
            case "param":
                return ParseParameterOrPropertyTag(start, tagName, PropertyLikeParse.Parameter, margin);
            case "return":
            case "returns":
                tag = ParseReturnTag(start, tagName, margin, indentText);
                break;
            case "template":
                tag = ParseTemplateTag(start, tagName, margin, indentText);
                break;
            case "type":
                tag = ParseTypeTag(start, tagName, margin, indentText);
                break;
            case "typedef":
                tag = ParseTypedefTag(start, tagName, margin, indentText);
                break;
            case "callback":
                tag = ParseCallbackTag(start, tagName, margin, indentText);
                break;
            case "overload":
                tag = ParseOverloadTag(start, tagName, margin, indentText);
                break;
            case "satisfies":
                tag = ParseSatisfiesTag(start, tagName, margin, indentText);
                break;
            case "see":
                tag = ParseSeeTag(start, tagName, margin, indentText);
                break;
            case "exception":
            case "throws":
                tag = ParseThrowsTag(start, tagName, margin, indentText);
                break;
            case "import":
                tag = ParseImportTag(start, tagName, margin, indentText);
                break;
            default:
                tag = ParseUnknownTag(start, tagName, margin, indentText);
                break;
        }
        return tag;
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

    /// <returns>A string, a NodeArray of JSDocComment, or null (TypeScript: `string | NodeArray&lt;JSDocComment&gt; | undefined`).</returns>
    private object? ParseTagComments(int indent, string? initialMargin = null)
    {
        var commentsPos = GetNodePos();
        List<string> comments = [];
        var parts = store.BeginList();
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
            var exitLoop = false; // TypeScript: `break loop;`
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
                    exitLoop = true;
                    break;
                case SyntaxKind.EndOfFileToken:
                    // Done
                    exitLoop = true;
                    break;
                case SyntaxKind.WhitespaceTrivia:
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
                case SyntaxKind.OpenBraceToken:
                    state = JSDocState.SavingComments;
                    var commentEnd = scanner.GetTokenFullStart();
                    var linkStart = scanner.GetTokenEnd() - 1;
                    var link = ParseJSDocLink(linkStart);
                    if (!link.IsNone)
                    {
                        var text = FinishNode(factory.CreateJSDocText(string.Concat(comments)), linkEnd ?? commentsPos, commentEnd);
                        store.Push(parts, text);
                        store.Push(parts, link);
                        comments = [];
                        linkEnd = scanner.GetTokenEnd();
                    }
                    else
                    {
                        PushComment(scanner.GetTokenText());
                    }
                    break;
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
                    // falls through
                    goto default;
                default:
                    if (state != JSDocState.SavingBackticks)
                    {
                        state = JSDocState.SavingComments; // leading identifiers start recording as well
                    }
                    PushComment(scanner.GetTokenText());
                    break;
            }
            if (exitLoop)
            {
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

        RemoveLeadingNewlines(comments);
        var trimmedComments = JsTrimEnd(string.Concat(comments));
        if (store.Length(parts) != 0)
        {
            if (trimmedComments.Length != 0)
            {
                var text = FinishNode(factory.CreateJSDocText(trimmedComments), linkEnd ?? commentsPos);
                store.Push(parts, text);
            }
            return CreateNodeArray(parts, commentsPos, scanner.GetTokenEnd());
        }
        store.DiscardList(parts);
        if (trimmedComments.Length != 0)
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

    private Node ParseJSDocLink(int start)
    {
        var linkType = TryParse(static p => p.ParseJSDocLinkPrefix());
        if (string.IsNullOrEmpty(linkType))
        {
            return default;
        }
        NextTokenJSDoc(); // start at token after link, then skip any whitespace
        SkipWhitespace();
        var name = ParseJSDocLinkName();
        List<string> text = [];
        while (Token() != SyntaxKind.CloseBraceToken && Token() != SyntaxKind.NewLineTrivia && Token() != SyntaxKind.EndOfFileToken)
        {
            text.Add(scanner.GetTokenText());
            NextTokenJSDoc();
        }
        var joined = string.Concat(text);
        Node link = linkType == "link" ? factory.CreateJSDocLink(name, joined)
            : linkType == "linkcode" ? factory.CreateJSDocLinkCode(name, joined)
            : factory.CreateJSDocLinkPlain(name, joined);
        return FinishNode(link, start, scanner.GetTokenEnd());
    }

    private Node ParseJSDocLinkName()
    {
        if (Scanner.TokenIsIdentifierOrKeyword(Token()))
        {
            var pos = GetNodePos();

            Node name = ParseIdentifierName();
            while (ParseOptional(SyntaxKind.DotToken))
            {
                name = FinishNode(factory.CreateQualifiedName(name, Token() == SyntaxKind.PrivateIdentifier ? CreateMissingNode(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ false) : ParseIdentifierName()), pos);
            }
            while (Token() == SyntaxKind.PrivateIdentifier)
            {
                ReScanHashToken();
                NextTokenJSDoc();
                name = FinishNode(factory.CreateJSDocMemberName(name, ParseIdentifier()), pos);
            }
            return name;
        }
        return default;
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

    private Node ParseUnknownTag(int start, Node tagName, int indent, string indentText)
    {
        return FinishNode(factory.CreateJSDocUnknownTag(tagName, ParseTrailingTagComments(start, GetNodePos(), indent, indentText)), start);
    }

    private void AddTag(Node tag, ref int tagsPos, ref int tagsEnd)
    {
        if (tag.IsNone)
        {
            return;
        }
        if (!hasJsDocTags)
        {
            jsDocTags = store.BeginList();
            hasJsDocTags = true;
            store.Push(jsDocTags, tag);
            tagsPos = store.Pos(tag);
        }
        else
        {
            store.Push(jsDocTags, tag);
        }
        tagsEnd = store.End(tag);
    }

    private Node TryParseTypeExpression()
    {
        SkipWhitespaceOrAsterisk();
        return Token() == SyntaxKind.OpenBraceToken ? ParseJSDocTypeExpression() : default;
    }

    private (Node Name, bool IsBracketed) ParseBracketNameInPropertyAndParamTag()
    {
        // Looking for something like '[foo]', 'foo', '[foo.bar]' or 'foo.bar'
        var isBracketed = ParseOptionalJsdoc(SyntaxKind.OpenBracketToken);
        if (isBracketed)
        {
            SkipWhitespace();
        }
        // a markdown-quoted name: `arg` is not legal jsdoc, but occurs in the wild
        var isBackquoted = ParseOptionalJsdoc(SyntaxKind.BacktickToken);
        var name = ParseJSDocEntityName();
        if (isBackquoted)
        {
            ParseExpectedTokenJSDoc(SyntaxKind.BacktickToken);
        }
        if (isBracketed)
        {
            SkipWhitespace();
            // May have an optional default, e.g. '[foo = 42]'
            if (!ParseOptionalToken(SyntaxKind.EqualsToken).IsNone)
            {
                ParseExpression();
            }

            ParseExpected(SyntaxKind.CloseBracketToken);
        }

        return (name, isBracketed);
    }

    private static bool IsObjectOrObjectArrayTypeReference(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ObjectKeyword:
                return true;
            case SyntaxKind.ArrayType:
                return IsObjectOrObjectArrayTypeReference(store, store.ElementType(node));
            default:
                return FactoryNodeTests.IsTypeReferenceNode(store, node) && FactoryNodeTests.IsIdentifier(store, store.TypeName(node)) && (string)store.EscapedText(store.TypeName(node))! == "Object" && store.TypeArguments(node).IsNone;
        }
    }

    private Node ParseParameterOrPropertyTag(int start, Node tagName, PropertyLikeParse target, int indent)
    {
        var typeExpression = TryParseTypeExpression();
        var isNameFirst = typeExpression.IsNone;
        SkipWhitespaceOrAsterisk();

        var (name, isBracketed) = ParseBracketNameInPropertyAndParamTag();
        var indentText = SkipWhitespaceOrAsterisk();

        if (isNameFirst && LookAhead(static p => p.ParseJSDocLinkPrefix()) == null)
        {
            typeExpression = TryParseTypeExpression();
        }

        var comment = ParseTrailingTagComments(start, GetNodePos(), indent, indentText);

        var nestedTypeLiteral = ParseNestedTypeLiteral(typeExpression, name, target, indent);
        if (!nestedTypeLiteral.IsNone)
        {
            typeExpression = nestedTypeLiteral;
            isNameFirst = true;
        }
        Node result = target == PropertyLikeParse.Property
            ? factory.CreateJSDocPropertyTag(tagName, name, isBracketed, typeExpression, isNameFirst, comment)
            : factory.CreateJSDocParameterTag(tagName, name, isBracketed, typeExpression, isNameFirst, comment);
        return FinishNode(result, start);
    }

    private Node ParseNestedTypeLiteral(Node typeExpression, Node name, PropertyLikeParse target, int indent)
    {
        if (!typeExpression.IsNone && IsObjectOrObjectArrayTypeReference(store, store.Type(typeExpression)))
        {
            var pos = GetNodePos();
            Node child;
            // (TypeScript: `let children: JSDocPropertyLikeTag[] | undefined`; a list under construction, dropped if empty.)
            var children = store.BeginList();
            while (!(child = TryParse(p => p.ParseChildParameterOrPropertyTag(target, indent, name))).IsNone)
            {
                if (store.Kind(child) == SyntaxKind.JSDocParameterTag || store.Kind(child) == SyntaxKind.JSDocPropertyTag)
                {
                    store.Push(children, child);
                }
                else if (store.Kind(child) == SyntaxKind.JSDocTemplateTag)
                {
                    ParseErrorAtRange(store.TagName(child), Diagnostics.A_JSDoc_template_tag_may_not_follow_a_typedef_callback_or_overload_tag);
                }
            }
            if (store.Length(children) != 0)
            {
                // (createJSDocTypeLiteral's asNodeArray turns the plain array into a NodeArray with range -1.)
                var literal = FinishNode(factory.CreateJSDocTypeLiteral(NodeFactory.CreateNodeArray(store, children), store.Kind(store.Type(typeExpression)) == SyntaxKind.ArrayType), pos);
                return FinishNode(factory.CreateJSDocTypeExpression(literal), pos);
            }
            store.DiscardList(children);
        }
        return default;
    }

    private Node ParseReturnTag(int start, Node tagName, int indent, string indentText)
    {
        if (hasJsDocTags && Core.Some(store.Nodes(jsDocTags), ((a0) => FactoryNodeTests.IsJSDocReturnTag(store, a0))))
        {
            ParseErrorAt(store.Pos(tagName), scanner.GetTokenStart(), Diagnostics._0_tag_already_specified, UtilitiesPublic.UnescapeLeadingUnderscores((string)store.EscapedText(tagName)!));
        }

        var typeExpression = TryParseTypeExpression();
        return FinishNode(factory.CreateJSDocReturnTag(tagName, typeExpression, ParseTrailingTagComments(start, GetNodePos(), indent, indentText)), start);
    }

    private Node ParseTypeTag(int start, Node tagName, int? indent = null, string? indentText = null)
    {
        if (hasJsDocTags && Core.Some(store.Nodes(jsDocTags), ((a0) => FactoryNodeTests.IsJSDocTypeTag(store, a0))))
        {
            ParseErrorAt(store.Pos(tagName), scanner.GetTokenStart(), Diagnostics._0_tag_already_specified, UtilitiesPublic.UnescapeLeadingUnderscores((string)store.EscapedText(tagName)!));
        }

        var typeExpression = ParseJSDocTypeExpression(/*mayOmitBraces*/ true);
        var comments = indent != null && indentText != null ? ParseTrailingTagComments(start, GetNodePos(), indent.Value, indentText) : null;
        return FinishNode(factory.CreateJSDocTypeTag(tagName, typeExpression, comments), start);
    }

    private Node ParseSeeTag(int start, Node tagName, int? indent = null, string? indentText = null)
    {
        var isMarkdownOrJSDocLink = Token() == SyntaxKind.OpenBracketToken
            || LookAhead(static p => p.NextTokenJSDoc() == SyntaxKind.AtToken && Scanner.TokenIsIdentifierOrKeyword(p.NextTokenJSDoc()) && IsJSDocLinkTag(p.scanner.GetTokenValue()));
        var nameExpression = isMarkdownOrJSDocLink ? default : ParseJSDocNameReference();
        var comments = indent != null && indentText != null ? ParseTrailingTagComments(start, GetNodePos(), indent.Value, indentText) : null;
        return FinishNode(factory.CreateJSDocSeeTag(tagName, nameExpression, comments), start);
    }

    private Node ParseThrowsTag(int start, Node tagName, int indent, string indentText)
    {
        var typeExpression = TryParseTypeExpression();
        var comment = ParseTrailingTagComments(start, GetNodePos(), indent, indentText);
        return FinishNode(factory.CreateJSDocThrowsTag(tagName, typeExpression, comment), start);
    }

    private Node ParseAuthorTag(int start, Node tagName, int indent, string indentText)
    {
        var commentStart = GetNodePos();
        var textOnly = ParseAuthorNameAndEmail();
        var commentEnd = scanner.GetTokenFullStart();
        var comments = ParseTrailingTagComments(start, commentEnd, indent, indentText);
        if (comments == null)
        {
            commentEnd = scanner.GetTokenFullStart();
        }
        object allParts;
        if (comments is not string commentText)
        {
            // TypeScript: `createNodeArray(concatenate([finishNode(textOnly, commentStart, commentEnd)], comments), commentStart)`
            var first = FinishNode(textOnly, commentStart, commentEnd);
            var elements = store.BeginList();
            store.Push(elements, first);
            if (comments is NodeList commentParts && store.Length(commentParts) != 0)
            {
                foreach (var part in store.Nodes(commentParts))
                {
                    store.Push(elements, part);
                }
            }
            allParts = CreateNodeArray(elements, commentStart);
        }
        else
        {
            allParts = store.Text(textOnly) + commentText;
        }
        return FinishNode(factory.CreateJSDocAuthorTag(tagName, allParts), start);
    }

    private Node ParseAuthorNameAndEmail()
    {
        List<string> comments = [];
        var inEmail = false;
        var token = scanner.GetToken();
        while (token != SyntaxKind.EndOfFileToken && token != SyntaxKind.NewLineTrivia)
        {
            if (token == SyntaxKind.LessThanToken)
            {
                inEmail = true;
            }
            else if (token == SyntaxKind.AtToken && !inEmail)
            {
                break;
            }
            else if (token == SyntaxKind.GreaterThanToken && inEmail)
            {
                comments.Add(scanner.GetTokenText());
                scanner.ResetTokenState(scanner.GetTokenEnd());
                break;
            }
            comments.Add(scanner.GetTokenText());
            token = NextTokenJSDoc();
        }

        return factory.CreateJSDocText(string.Concat(comments));
    }

    private Node ParseImplementsTag(int start, Node tagName, int margin, string indentText)
    {
        var className = ParseExpressionWithTypeArgumentsForAugments();
        return FinishNode(factory.CreateJSDocImplementsTag(tagName, className, ParseTrailingTagComments(start, GetNodePos(), margin, indentText)), start);
    }

    private Node ParseAugmentsTag(int start, Node tagName, int margin, string indentText)
    {
        var className = ParseExpressionWithTypeArgumentsForAugments();
        return FinishNode(factory.CreateJSDocAugmentsTag(tagName, className, ParseTrailingTagComments(start, GetNodePos(), margin, indentText)), start);
    }

    private Node ParseSatisfiesTag(int start, Node tagName, int margin, string indentText)
    {
        var typeExpression = ParseJSDocTypeExpression(/*mayOmitBraces*/ false);
        // (TypeScript: `margin !== undefined && indentText !== undefined ? ... : undefined`; both are always defined.)
        var comments = ParseTrailingTagComments(start, GetNodePos(), margin, indentText);
        return FinishNode(factory.CreateJSDocSatisfiesTag(tagName, typeExpression, comments), start);
    }

    private Node ParseImportTag(int start, Node tagName, int margin, string indentText)
    {
        var afterImportTagPos = scanner.GetTokenFullStart();

        Node identifier = default;
        if (IsIdentifier())
        {
            identifier = ParseIdentifier();
        }

        var importClause = TryParseImportClause(identifier, afterImportTagPos, SyntaxKind.TypeKeyword, /*skipJsDocLeadingAsterisks*/ true);
        var moduleSpecifier = ParseModuleSpecifier();
        var attributes = TryParseImportAttributes();

        // (TypeScript: `margin !== undefined && indentText !== undefined ? ... : undefined`; both are always defined.)
        var comments = ParseTrailingTagComments(start, GetNodePos(), margin, indentText);
        return FinishNode(factory.CreateJSDocImportTag(tagName, importClause, moduleSpecifier, attributes, comments), start);
    }

    private Node ParseExpressionWithTypeArgumentsForAugments()
    {
        var usedBrace = ParseOptional(SyntaxKind.OpenBraceToken);
        var pos = GetNodePos();
        var expression = ParsePropertyAccessEntityNameExpression();
        scanner.SetSkipJsDocLeadingAsterisks(true);
        var typeArguments = TryParseTypeArguments();
        scanner.SetSkipJsDocLeadingAsterisks(false);
        var node = factory.CreateExpressionWithTypeArguments(expression, typeArguments);
        var res = FinishNode(node, pos);
        if (usedBrace)
        {
            SkipWhitespace();
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        return res;
    }

    private Node ParsePropertyAccessEntityNameExpression()
    {
        var pos = GetNodePos();
        Node node = ParseJSDocIdentifierName();
        while (ParseOptional(SyntaxKind.DotToken))
        {
            var name = ParseJSDocIdentifierName();
            node = FinishNode(factory.CreatePropertyAccessExpression(node, name), pos);
        }
        return node;
    }

    private Node ParseSimpleTag(int start, Func<NodeFactory, Node, object?, Node> createTag, Node tagName, int margin, string indentText)
    {
        return FinishNode(createTag(factory, tagName, ParseTrailingTagComments(start, GetNodePos(), margin, indentText)), start);
    }

    private Node ParseThisTag(int start, Node tagName, int margin, string indentText)
    {
        var typeExpression = ParseJSDocTypeExpression(/*mayOmitBraces*/ true);
        SkipWhitespace();
        return FinishNode(factory.CreateJSDocThisTag(tagName, typeExpression, ParseTrailingTagComments(start, GetNodePos(), margin, indentText)), start);
    }

    private Node ParseEnumTag(int start, Node tagName, int margin, string indentText)
    {
        var typeExpression = ParseJSDocTypeExpression(/*mayOmitBraces*/ true);
        SkipWhitespace();
        return FinishNode(factory.CreateJSDocEnumTag(tagName, typeExpression, ParseTrailingTagComments(start, GetNodePos(), margin, indentText)), start);
    }

    private Node ParseTypedefTag(int start, Node tagName, int indent, string indentText)
    {
        Node typeExpression = TryParseTypeExpression();
        SkipWhitespaceOrAsterisk();

        var fullName = ParseJSDocTypeNameWithNamespace();
        SkipWhitespace();
        var comment = ParseTagComments(indent);

        int? end = null;
        if (typeExpression.IsNone || IsObjectOrObjectArrayTypeReference(store, store.Type(typeExpression)))
        {
            Node child;
            Node childTypeTag = default;
            // (TypeScript: `let jsDocPropertyTags: JSDocPropertyTag[] | undefined`; a list under construction, dropped if empty.)
            var jsDocPropertyTags = store.BeginList();
            var hasChildren = false;
            while (!(child = TryParse(p => p.ParseChildPropertyTag(indent))).IsNone)
            {
                if (store.Kind(child) == SyntaxKind.JSDocTemplateTag)
                {
                    break;
                }
                hasChildren = true;
                if (store.Kind(child) == SyntaxKind.JSDocTypeTag)
                {
                    if (!childTypeTag.IsNone)
                    {
                        var lastError = ParseErrorAtCurrentToken(Diagnostics.A_JSDoc_typedef_comment_may_not_contain_multiple_type_tags);
                        if (lastError != null)
                        {
                            Utilities.AddRelatedInfo(lastError, Utilities.CreateDetachedDiagnostic(fileName, sourceText, 0, 0, Diagnostics.The_tag_was_first_specified_here));
                        }
                        break;
                    }
                    else
                    {
                        childTypeTag = child;
                    }
                }
                else
                {
                    store.Push(jsDocPropertyTags, child);
                }
            }
            NodeList propertyTags = default;
            if (store.Length(jsDocPropertyTags) != 0)
            {
                // (createJSDocTypeLiteral's asNodeArray turns the plain array into a NodeArray with range -1.)
                propertyTags = NodeFactory.CreateNodeArray(store, jsDocPropertyTags);
            }
            else
            {
                store.DiscardList(jsDocPropertyTags);
            }
            if (hasChildren)
            {
                var isArrayType = !typeExpression.IsNone && store.Kind(store.Type(typeExpression)) == SyntaxKind.ArrayType;
                var jsdocTypeLiteral = factory.CreateJSDocTypeLiteral(propertyTags, isArrayType);
                typeExpression = !childTypeTag.IsNone && !store.TypeExpression(childTypeTag).IsNone && !IsObjectOrObjectArrayTypeReference(store, store.Type(store.TypeExpression(childTypeTag))) ?
                    store.TypeExpression(childTypeTag) :
                    FinishNode(jsdocTypeLiteral, start);
                end = store.End(typeExpression);
            }
        }

        // Only include the characters between the name end and the next token if a comment was actually parsed out - otherwise it's just whitespace
        end = end is not null and not 0 || comment != null ?
            GetNodePos() :
            store.End(fullName.IsNone ? (typeExpression.IsNone ? tagName : typeExpression) : fullName);

        comment ??= ParseTrailingTagComments(start, end.Value, indent, indentText);

        var typedefTag = factory.CreateJSDocTypedefTag(tagName, typeExpression, fullName, comment);
        return FinishNode(typedefTag, start, end);
    }

    private Node ParseJSDocTypeNameWithNamespace(bool nested = false)
    {
        var start = scanner.GetTokenStart();
        if (!Scanner.TokenIsIdentifierOrKeyword(Token()))
        {
            return default;
        }
        var typeNameOrNamespaceName = ParseJSDocIdentifierName();
        if (ParseOptional(SyntaxKind.DotToken))
        {
            var body = ParseJSDocTypeNameWithNamespace(/*nested*/ true);
            var jsDocNamespaceNode = factory.CreateModuleDeclaration(
                /*modifiers*/ default,
                typeNameOrNamespaceName,
                body,
                nested ? NodeFlags.NestedNamespace : NodeFlags.None);
            return FinishNode(jsDocNamespaceNode, start);
        }

        if (nested)
        {
            store.SetFlags(typeNameOrNamespaceName, store.Flags(typeNameOrNamespaceName) | NodeFlags.IdentifierIsInJSDocNamespace);
        }
        return typeNameOrNamespaceName;
    }

    private NodeList ParseCallbackTagParameters(int indent)
    {
        var pos = GetNodePos();
        Node child;
        // (TypeScript: `let parameters` grown by `append`, then `createNodeArray(parameters || [], pos)`.)
        var parameters = store.BeginList();
        while (!(child = TryParse(p => p.ParseChildParameterOrPropertyTag(PropertyLikeParse.CallbackParameter, indent))).IsNone)
        {
            if (store.Kind(child) == SyntaxKind.JSDocTemplateTag)
            {
                ParseErrorAtRange(store.TagName(child), Diagnostics.A_JSDoc_template_tag_may_not_follow_a_typedef_callback_or_overload_tag);
                break;
            }
            store.Push(parameters, child);
        }
        return CreateNodeArray(parameters, pos);
    }

    private Node ParseJSDocSignature(int start, int indent)
    {
        var parameters = ParseCallbackTagParameters(indent);
        var returnTag = TryParse(p =>
        {
            if (p.ParseOptionalJsdoc(SyntaxKind.AtToken))
            {
                var tag = p.ParseTag(indent);
                if (!tag.IsNone && store.Kind(tag) == SyntaxKind.JSDocReturnTag)
                {
                    return tag;
                }
            }
            return default;
        });
        return FinishNode(factory.CreateJSDocSignature(/*typeParameters*/ default, parameters, returnTag), start);
    }

    private Node ParseCallbackTag(int start, Node tagName, int indent, string indentText)
    {
        var fullName = ParseJSDocTypeNameWithNamespace();
        SkipWhitespace();
        var comment = ParseTagComments(indent);
        var typeExpression = ParseJSDocSignature(start, indent);
        comment ??= ParseTrailingTagComments(start, GetNodePos(), indent, indentText);
        var end = comment != null ? GetNodePos() : store.End(typeExpression);
        return FinishNode(factory.CreateJSDocCallbackTag(tagName, typeExpression, fullName, comment), start, end);
    }

    private Node ParseOverloadTag(int start, Node tagName, int indent, string indentText)
    {
        SkipWhitespace();
        var comment = ParseTagComments(indent);
        var typeExpression = ParseJSDocSignature(start, indent);
        comment ??= ParseTrailingTagComments(start, GetNodePos(), indent, indentText);
        var end = comment != null ? GetNodePos() : store.End(typeExpression);
        return FinishNode(factory.CreateJSDocOverloadTag(tagName, typeExpression, comment), start, end);
    }

    private static bool EscapedTextsEqual(NodeStore store, Node a, Node b)
    {
        while (!FactoryNodeTests.IsIdentifier(store, a) || !FactoryNodeTests.IsIdentifier(store, b))
        {
            if (!FactoryNodeTests.IsIdentifier(store, a) && !FactoryNodeTests.IsIdentifier(store, b) && (string)store.EscapedText(store.Right(a))! == (string)store.EscapedText(store.Right(b))!)
            {
                a = store.Left(a);
                b = store.Left(b);
            }
            else
            {
                return false;
            }
        }
        return (string)store.EscapedText(a)! == (string)store.EscapedText(b)!;
    }

    private Node ParseChildPropertyTag(int indent)
    {
        return ParseChildParameterOrPropertyTag(PropertyLikeParse.Property, indent);
    }

    /// <returns>The tag, or null for TypeScript's `false`.</returns>
    private Node ParseChildParameterOrPropertyTag(PropertyLikeParse target, int indent, Node name = default)
    {
        var canParseTag = true;
        var seenAsterisk = false;
        while (true)
        {
            switch (NextTokenJSDoc())
            {
                case SyntaxKind.AtToken:
                    if (canParseTag)
                    {
                        var child = TryParseChildTag(target, indent);
                        if (
                            !child.IsNone && (store.Kind(child) == SyntaxKind.JSDocParameterTag || store.Kind(child) == SyntaxKind.JSDocPropertyTag) &&
                            !name.IsNone && (FactoryNodeTests.IsIdentifier(store, GetPropertyLikeTagName(store, child)) || !EscapedTextsEqual(store, name, store.Left(GetPropertyLikeTagName(store, child)))))
                        {
                            return default;
                        }
                        return child;
                    }
                    seenAsterisk = false;
                    break;
                case SyntaxKind.NewLineTrivia:
                    canParseTag = true;
                    seenAsterisk = false;
                    break;
                case SyntaxKind.AsteriskToken:
                    if (seenAsterisk)
                    {
                        canParseTag = false;
                    }
                    seenAsterisk = true;
                    break;
                case SyntaxKind.Identifier:
                    canParseTag = false;
                    break;
                case SyntaxKind.EndOfFileToken:
                    return default;
            }
        }
    }

    // `child.name` of a JSDocParameterTag or JSDocPropertyTag (JSDocPropertyLikeTag).
    private static Node GetPropertyLikeTagName(NodeStore store, Node tag)
    {
        return store.Kind(tag) == SyntaxKind.JSDocParameterTag ? store.Name(tag) : store.Name(tag);
    }

    /// <returns>The tag, or null for TypeScript's `false`.</returns>
    private Node TryParseChildTag(PropertyLikeParse target, int indent)
    {
        Debug.Assert(Token() == SyntaxKind.AtToken);
        var start = scanner.GetTokenFullStart();
        NextTokenJSDoc();

        var tagName = ParseJSDocIdentifierName();
        var indentText = SkipWhitespaceOrAsterisk();
        PropertyLikeParse t;
        switch ((string)store.EscapedText(tagName)!)
        {
            case "type":
                return target == PropertyLikeParse.Property ? ParseTypeTag(start, tagName) : default;
            case "prop":
            case "property":
                t = PropertyLikeParse.Property;
                break;
            case "arg":
            case "argument":
            case "param":
                t = PropertyLikeParse.Parameter | PropertyLikeParse.CallbackParameter;
                break;
            case "template":
                return ParseTemplateTag(start, tagName, indent, indentText);
            case "this":
                return ParseThisTag(start, tagName, indent, indentText);
            default:
                return default;
        }
        if ((target & t) == 0)
        {
            return default;
        }
        return ParseParameterOrPropertyTag(start, tagName, target, indent);
    }

    private Node ParseTemplateTagTypeParameter()
    {
        var typeParameterPos = GetNodePos();
        var isBracketed = ParseOptionalJsdoc(SyntaxKind.OpenBracketToken);
        if (isBracketed)
        {
            SkipWhitespace();
        }

        var modifiers = ParseModifiers(/*allowDecorators*/ false, /*permitConstAsModifier*/ true);
        var name = ParseJSDocIdentifierName(Diagnostics.Unexpected_token_A_type_parameter_name_was_expected_without_curly_braces);
        Node defaultType = default;
        if (isBracketed)
        {
            SkipWhitespace();
            ParseExpected(SyntaxKind.EqualsToken);
            defaultType = DoInsideOfContext(NodeFlags.JSDoc, static p => p.ParseJSDocType());
            ParseExpected(SyntaxKind.CloseBracketToken);
        }

        if (Utilities.NodeIsMissing(store, name))
        {
            return default;
        }
        return FinishNode(factory.CreateTypeParameterDeclaration(modifiers, name, /*constraint*/ default, defaultType), typeParameterPos);
    }

    private NodeList ParseTemplateTagTypeParameters()
    {
        var pos = GetNodePos();
        var typeParameters = store.BeginList();
        do
        {
            SkipWhitespace();
            var node = ParseTemplateTagTypeParameter();
            if (!node.IsNone)
            {
                store.Push(typeParameters, node);
            }
            SkipWhitespaceOrAsterisk();
        }
        while (ParseOptionalJsdoc(SyntaxKind.CommaToken));
        return CreateNodeArray(typeParameters, pos);
    }

    private Node ParseTemplateTag(int start, Node tagName, int indent, string indentText)
    {
        // The template tag looks like one of the following:
        //   @template T,U,V
        //   @template {Constraint} T
        //
        // According to the [closure docs](https://github.com/google/closure-compiler/wiki/Generic-Types#multiple-bounded-template-types):
        //   > Multiple bounded generics cannot be declared on the same line. For the sake of clarity, if multiple templates share the same
        //   > type bound they must be declared on separate lines.
        //
        // TODO: Determine whether we should enforce this in the checker.
        // TODO: Consider moving the `constraint` to the first type parameter as we could then remove `getEffectiveConstraintOfTypeParameter`.
        // TODO: Consider only parsing a single type parameter if there is a constraint.
        var constraint = Token() == SyntaxKind.OpenBraceToken ? ParseJSDocTypeExpression() : default;
        var typeParameters = ParseTemplateTagTypeParameters();
        return FinishNode(factory.CreateJSDocTemplateTag(tagName, constraint, typeParameters, ParseTrailingTagComments(start, GetNodePos(), indent, indentText)), start);
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

    private Node ParseJSDocIdentifierName(DiagnosticMessage? message = null)
    {
        if (!Scanner.TokenIsIdentifierOrKeyword(Token()))
        {
            return CreateMissingNode(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ message == null, message ?? Diagnostics.Identifier_expected);
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

    /// <summary>JavaScript's `s.slice(start)` with one argument (a negative start counts from the end).</summary>
    private static string JsSlice(string s, int start)
    {
        if (start < 0)
        {
            start = Math.Max(s.Length + start, 0);
        }
        return start >= s.Length ? "" : start == 0 ? s : s[start..];
    }

    /// <summary>JavaScript's String.prototype.trimEnd.</summary>
    private static string JsTrimEnd(string s)
    {
        var end = s.Length;
        while (end > 0 && Utilities.IsJsWhiteSpaceOrLineTerminator(s[end - 1]))
        {
            end--;
        }
        return end == s.Length ? s : s[..end];
    }
}
