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

    // Parses out a JSDoc type expression.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseJSDocTypeExpression(bool mayOmitBraces = false)
    {
        var pos = GetNodePos();
        var hasBrace = mayOmitBraces ? ParseOptional(SyntaxKind.OpenBraceToken) : ParseExpected(SyntaxKind.OpenBraceToken);
        var type = DoInsideOfContext(NodeFlags.JSDoc, new F.ParseJSDocType());
        if (!mayOmitBraces || hasBrace)
        {
            ParseExpectedJSDoc(SyntaxKind.CloseBraceToken);
        }

        var result = factory.CreateJSDocTypeExpression(type);
        FixupParentReferences(store, result);
        return FinishNode(result, pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    // `if (!jsDocDiagnostics) jsDocDiagnostics = []; addRange(jsDocDiagnostics, parseDiagnostics, saveParseDiagnosticsLength);`
    [NoAllocUnchecked("per-file result: the JSDoc diagnostics of the SourceFile")]
    private void AddJSDocDiagnostics(int saveParseDiagnosticsLength)
    {
        jsDocDiagnostics ??= [];
        Core.AddRange(jsDocDiagnostics, parseDiagnostics, saveParseDiagnosticsLength);
    }

    /// <summary>parser.ts: JSDocParser.parseJSDocComment(parent, start, length).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
            AddJSDocDiagnostics(saveParseDiagnosticsLength);
        }
        currentToken = saveToken;
        parseDiagnostics.RemoveRange(saveParseDiagnosticsLength, parseDiagnostics.Count - saveParseDiagnosticsLength);
        parseErrorBeforeNextFinishedNode = saveParseErrorBeforeNextFinishedNode;
        return comment;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
        var result = scanner.ScanRange<JSDocScan, Node>(start + 3, length.Value - 5, new JSDocScan(this));
        parsingContext = saveParsingContext;
        jsDocTags = saveTags;
        hasJsDocTags = saveHasTags;
        return result;
    }

    // `scanner.scanRange(start + 3, length - 5, doJSDocScan)` (Track O, O-NA: the callback as a struct).
    private readonly struct JSDocScan : IScannerCallback<Node>
    {
        private readonly Parser parser;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        internal JSDocScan(Parser parser)
        {
            this.parser = parser;
        }

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public Node Invoke() => parser.DoJSDocScan();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node DoJSDocScan()
    {
        var content = sourceText;
        var start = jsDocWorkerStart;
        var end = jsDocWorkerEnd;
        int tagsPos = 0;
        int tagsEnd = 0;
        int? linkEnd = null;
        int? commentsPos = null;
        var comments = BeginTextParts();
        var parts = store.BeginList();

        // Initially we can parse out a tag.  We also have seen a starting asterisk.
        // This is so that /** * @type */ doesn't parse.
        var state = JSDocState.SawAsterisk;
        int? margin = null;
        // + 4 for leading '/** '
        // + 1 because the last index of \n is always one index before the first character in the line and coincidentally, if there is no \n before start, it is -1, which is also one index before the first character
        // (Step O1: `content.lastIndexOf("\n", start)` on the characters: the last line feed at or before start.)
        var lastLineFeed = start;
        while (lastLineFeed >= 0 && content[lastLineFeed] != '\n')
        {
            lastLineFeed--;
        }
        var indent = start - (lastLineFeed + 1) + 4;

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
                    AddTextPart(comments, scanner.GetTokenTextRef());
                    state = JSDocState.BeginningOfLine;
                    indent = 0;
                    break;
                case SyntaxKind.AsteriskToken:
                    var asterisk = scanner.GetTokenTextRef();
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
                    var whitespace = scanner.GetTokenTextRef();
                    if (margin != null && indent + whitespace.Length > margin.Value)
                    {
                        AddTextPart(comments, JsSlice(whitespace, margin.Value - indent));
                    }
                    indent += whitespace.Length;
                    break;
                case SyntaxKind.EndOfFileToken:
                    exitLoop = true;
                    break;
                case SyntaxKind.JSDocCommentTextToken:
                    state = JSDocState.SavingComments;
                    PushComment(scanner.GetTokenValueRef());
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
                        var text = FinishNode(factory.CreateJSDocText(JoinTexts(comments)), linkEnd ?? start, commentEnd);
                        store.Push(parts, text);
                        store.Push(parts, link);
                        ClearTextParts(comments); // (TypeScript: `comments = []`)
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
                    PushComment(scanner.GetTokenTextRef());
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
        var trimmedComments = TrimEnd(JoinTexts(comments));
        EndTextParts(comments);
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
        TextOrList comment;
        if (store.Length(parts) != 0)
        {
            comment = CreateNodeArray(parts, start, commentsPos);
        }
        else
        {
            store.DiscardList(parts);
            comment = trimmedComments.Length != 0 ? trimmedComments : TextOrList.None;
        }
        return FinishNode(factory.CreateJSDocComment(comment, tagsArray), start, end);

        void PushComment(TextRef text)
        {
            if (margin is null or 0)
            {
                margin = indent;
            }
            AddTextPart(comments, text);
            indent += text.Length;
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void RemoveLeadingNewlines(TextParts comments)
    {
        while (TextPartCount(comments) != 0 && (texts.Equals(textParts[comments.Start], "\n") || texts.Equals(textParts[comments.Start], "\r")))
        {
            RemoveFirstTextPart(comments);
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void RemoveTrailingWhitespace(TextParts comments)
    {
        while (TextPartCount(comments) != 0)
        {
            var last = textParts[textPartsTop - 1];
            var trimmed = TrimEnd(last);
            if (trimmed.Length == 0)
            {
                textPartsTop--; // (TypeScript: `comments.pop()`)
            }
            else if (trimmed.Length < last.Length)
            {
                textParts[textPartsTop - 1] = trimmed;
                break;
            }
            else
            {
                break;
            }
        }
    }

    //// Track O (O-NA): the lists of comment parts of the JSDoc parser (TypeScript: `const comments: string[] = []`, joined
    //// later) as ranges of one stack per parser instead of a list per comment. A list runs from its start to the top of the
    //// stack: the lists of nested functions are opened above it and closed (EndTextParts) before it grows again.

    private TextRef[] textParts = new TextRef[64];
    private int textPartsTop;

    /// <summary>A list of comment parts: its start in <see cref="textParts"/>; it ends at the top of the stack.</summary>
    private readonly struct TextParts
    {
        internal readonly int Start;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        internal TextParts(int start)
        {
            Start = start;
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private TextParts BeginTextParts()
    {
        return new TextParts(textPartsTop);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void EndTextParts(TextParts parts)
    {
        Debug.Assert(parts.Start <= textPartsTop);
        textPartsTop = parts.Start;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void ClearTextParts(TextParts parts)
    {
        textPartsTop = parts.Start;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private int TextPartCount(TextParts parts)
    {
        return textPartsTop - parts.Start;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void AddTextPart(TextParts parts, TextRef text)
    {
        Debug.Assert(parts.Start <= textPartsTop);
        if (textPartsTop == textParts.Length)
        {
            GrowTextParts();
        }
        textParts[textPartsTop++] = text;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void RemoveFirstTextPart(TextParts parts)
    {
        textParts.AsSpan(parts.Start + 1, textPartsTop - parts.Start - 1).CopyTo(textParts.AsSpan(parts.Start));
        textPartsTop--;
    }

    [NoAllocUnchecked("arena growth: the stack of JSDoc comment parts doubles")]
    private void GrowTextParts()
    {
        Array.Resize(ref textParts, textParts.Length * 2);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void SkipWhitespace()
    {
        if (Token() == SyntaxKind.WhitespaceTrivia || Token() == SyntaxKind.NewLineTrivia)
        {
            if (LookAhead(new F.IsNextNonwhitespaceTokenEndOfFile()))
            {
                return; // Don't skip whitespace prior to EoF (or end of comment) - that shouldn't be included in any node's range
            }
        }
        while (Token() == SyntaxKind.WhitespaceTrivia || Token() == SyntaxKind.NewLineTrivia)
        {
            NextTokenJSDoc();
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private TextRef SkipWhitespaceOrAsterisk()
    {
        if (Token() == SyntaxKind.WhitespaceTrivia || Token() == SyntaxKind.NewLineTrivia)
        {
            if (LookAhead(new F.IsNextNonwhitespaceTokenEndOfFile()))
            {
                return TextRef.Empty; // Don't skip whitespace prior to EoF (or end of comment) - that shouldn't be included in any node's range
            }
        }

        var precedingLineBreak = scanner.HasPrecedingLineBreak();
        var seenLineBreak = false;
        var indentText = TextRef.Empty;
        while ((precedingLineBreak && Token() == SyntaxKind.AsteriskToken) || Token() == SyntaxKind.WhitespaceTrivia || Token() == SyntaxKind.NewLineTrivia)
        {
            // (Track O: adjacent tokens, so the concatenation is a span of the text.)
            indentText = texts.Concat(indentText, scanner.GetTokenTextRef());
            if (Token() == SyntaxKind.NewLineTrivia)
            {
                precedingLineBreak = true;
                seenLineBreak = true;
                indentText = TextRef.Empty;
            }
            else if (Token() == SyntaxKind.AsteriskToken)
            {
                precedingLineBreak = false;
            }
            NextTokenJSDoc();
        }
        return seenLineBreak ? indentText : TextRef.Empty;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseTag(int margin)
    {
        Debug.Assert(Token() == SyntaxKind.AtToken);
        var start = scanner.GetTokenStart();
        NextTokenJSDoc();

        var tagName = ParseJSDocIdentifierName(/*message*/ null);
        var indentText = SkipWhitespaceOrAsterisk();

        Node tag;
        switch (texts.Span(store.EscapedText(tagName)))
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
                tag = ParseSimpleTag(start, SyntaxKind.JSDocClassTag, tagName, margin, indentText);
                break;
            case "public":
                tag = ParseSimpleTag(start, SyntaxKind.JSDocPublicTag, tagName, margin, indentText);
                break;
            case "private":
                tag = ParseSimpleTag(start, SyntaxKind.JSDocPrivateTag, tagName, margin, indentText);
                break;
            case "protected":
                tag = ParseSimpleTag(start, SyntaxKind.JSDocProtectedTag, tagName, margin, indentText);
                break;
            case "readonly":
                tag = ParseSimpleTag(start, SyntaxKind.JSDocReadonlyTag, tagName, margin, indentText);
                break;
            case "override":
                tag = ParseSimpleTag(start, SyntaxKind.JSDocOverrideTag, tagName, margin, indentText);
                break;
            case "deprecated":
                hasDeprecatedTag = true;
                tag = ParseSimpleTag(start, SyntaxKind.JSDocDeprecatedTag, tagName, margin, indentText);
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private TextOrList ParseTrailingTagComments(int pos, int end, int margin, TextRef indentText)
    {
        // some tags, like typedef and callback, have already parsed their comments earlier
        if (indentText.Length == 0)
        {
            margin += end - pos;
        }
        return ParseTagComments(margin, JsSlice(indentText, margin));
    }

    /// <returns>A string, a NodeArray of JSDocComment, or null (TypeScript: `string | NodeArray&lt;JSDocComment&gt; | undefined`).</returns>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private TextOrList ParseTagComments(int indent, TextRef initialMargin = default)
    {
        var commentsPos = GetNodePos();
        var comments = BeginTextParts();
        var parts = store.BeginList();
        int? linkEnd = null;
        var state = JSDocState.BeginningOfLine;
        int? margin = null;
        if (!initialMargin.IsNone)
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
                    AddTextPart(comments, scanner.GetTokenTextRef());
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
                    var whitespace = scanner.GetTokenTextRef();
                    // if the whitespace crosses the margin, take only the whitespace that passes the margin
                    if (margin != null && indent + whitespace.Length > margin.Value)
                    {
                        AddTextPart(comments, JsSlice(whitespace, margin.Value - indent));
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
                        var text = FinishNode(factory.CreateJSDocText(JoinTexts(comments)), linkEnd ?? commentsPos, commentEnd);
                        store.Push(parts, text);
                        store.Push(parts, link);
                        ClearTextParts(comments); // (TypeScript: `comments = []`)
                        linkEnd = scanner.GetTokenEnd();
                    }
                    else
                    {
                        PushComment(scanner.GetTokenTextRef());
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
                    PushComment(scanner.GetTokenTextRef());
                    break;
                case SyntaxKind.JSDocCommentTextToken:
                    if (state != JSDocState.SavingBackticks)
                    {
                        state = JSDocState.SavingComments; // leading identifiers start recording as well
                    }
                    PushComment(scanner.GetTokenValueRef());
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
                    PushComment(scanner.GetTokenTextRef());
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
        var trimmedComments = TrimEnd(JoinTexts(comments));
        EndTextParts(comments);
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
        return TextOrList.None;

        void PushComment(TextRef text)
        {
            if (margin is null or 0)
            {
                margin = indent;
            }
            AddTextPart(comments, text);
            indent += text.Length;
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseJSDocLink(int start)
    {
        var linkType = TryParse<F.ParseJSDocLinkPrefix, string?>(new F.ParseJSDocLinkPrefix());
        if (string.IsNullOrEmpty(linkType))
        {
            return default;
        }
        NextTokenJSDoc(); // start at token after link, then skip any whitespace
        SkipWhitespace();
        var name = ParseJSDocLinkName();
        var text = BeginTextParts();
        while (Token() != SyntaxKind.CloseBraceToken && Token() != SyntaxKind.NewLineTrivia && Token() != SyntaxKind.EndOfFileToken)
        {
            AddTextPart(text, scanner.GetTokenTextRef());
            NextTokenJSDoc();
        }
        var joined = JoinTexts(text);
        EndTextParts(text);
        Node link = linkType == "link" ? factory.CreateJSDocLink(name, joined)
            : linkType == "linkcode" ? factory.CreateJSDocLinkCode(name, joined)
            : factory.CreateJSDocLinkPlain(name, joined);
        return FinishNode(link, start, scanner.GetTokenEnd());
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private string? ParseJSDocLinkPrefix()
    {
        SkipWhitespaceOrAsterisk();
        if (
            Token() == SyntaxKind.OpenBraceToken
            && NextTokenJSDoc() == SyntaxKind.AtToken
            && Scanner.TokenIsIdentifierOrKeyword(NextTokenJSDoc()))
        {
            // (Track O: the matching constant, not the token value, which a rolled-back lookahead may discard.)
            var kind = JSDocLinkTag(scanner.GetTokenValueRef());
            if (kind != null)
            {
                return kind;
            }
        }
        return null;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsJSDocLinkTag(TextRef kind)
    {
        return JSDocLinkTag(kind) != null;
    }

    // Track O (O5a): the link tag name a text equals ("link", "linkcode" or "linkplain"), or null.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private string? JSDocLinkTag(TextRef kind)
    {
        return texts.Span(kind) switch
        {
            "link" => "link",
            "linkcode" => "linkcode",
            "linkplain" => "linkplain",
            _ => null,
        };
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseUnknownTag(int start, Node tagName, int indent, TextRef indentText)
    {
        return FinishNode(factory.CreateJSDocUnknownTag(tagName, ParseTrailingTagComments(start, GetNodePos(), indent, indentText)), start);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node TryParseTypeExpression()
    {
        SkipWhitespaceOrAsterisk();
        return Token() == SyntaxKind.OpenBraceToken ? ParseJSDocTypeExpression() : default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsObjectOrObjectArrayTypeReference(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ObjectKeyword:
                return true;
            case SyntaxKind.ArrayType:
                return IsObjectOrObjectArrayTypeReference(store, store.ElementType(node));
            default:
                return FactoryNodeTests.IsTypeReferenceNode(store, node) && FactoryNodeTests.IsIdentifier(store, store.TypeName(node)) && store.Texts.Equals(store.EscapedText(store.TypeName(node)), "Object") && store.TypeArguments(node).IsNone;
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseParameterOrPropertyTag(int start, Node tagName, PropertyLikeParse target, int indent)
    {
        var typeExpression = TryParseTypeExpression();
        var isNameFirst = typeExpression.IsNone;
        SkipWhitespaceOrAsterisk();

        var (name, isBracketed) = ParseBracketNameInPropertyAndParamTag();
        var indentText = SkipWhitespaceOrAsterisk();

        if (isNameFirst && LookAhead<F.ParseJSDocLinkPrefix, string?>(new F.ParseJSDocLinkPrefix()) == null)
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseNestedTypeLiteral(Node typeExpression, Node name, PropertyLikeParse target, int indent)
    {
        if (!typeExpression.IsNone && IsObjectOrObjectArrayTypeReference(store, store.Type(typeExpression)))
        {
            var pos = GetNodePos();
            Node child;
            // (TypeScript: `let children: JSDocPropertyLikeTag[] | undefined`; a list under construction, dropped if empty.)
            var children = store.BeginList();
            while (!(child = TryParse(new F.ParseChildParameterOrPropertyTag3(target, indent, name))).IsNone)
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

    /// <summary>Track O (O-NA): `some(jsDocTags, isJSDocReturnTag)` (or isJSDocTypeTag) as a loop over the tags so far.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool JSDocTagsContain(SyntaxKind kind)
    {
        foreach (var t in store.Nodes(jsDocTags))
        {
            if (store.Kind(t) == kind)
            {
                return true;
            }
        }
        return false;
    }

    [NoAllocUnchecked("error path: adds related information to a diagnostic")]
    private void AddTagFirstSpecifiedInfo(DiagnosticWithDetachedLocation lastError)
    {
        Utilities.AddRelatedInfo(lastError, Utilities.CreateDetachedDiagnostic(fileName, sourceText, 0, 0, Diagnostics.The_tag_was_first_specified_here));
    }

    [NoAllocUnchecked("error path: the message names the tag")]
    private void ReportTagAlreadySpecified(Node tagName)
    {
        ParseErrorAt(store.Pos(tagName), scanner.GetTokenStart(), Diagnostics._0_tag_already_specified, texts.GetString(UtilitiesPublic.UnescapeLeadingUnderscores(texts, store.EscapedText(tagName))));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseReturnTag(int start, Node tagName, int indent, TextRef indentText)
    {
        if (hasJsDocTags && JSDocTagsContain(SyntaxKind.JSDocReturnTag))
        {
            ReportTagAlreadySpecified(tagName);
        }

        var typeExpression = TryParseTypeExpression();
        return FinishNode(factory.CreateJSDocReturnTag(tagName, typeExpression, ParseTrailingTagComments(start, GetNodePos(), indent, indentText)), start);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseTypeTag(int start, Node tagName, int? indent = null, TextRef indentText = default)
    {
        if (hasJsDocTags && JSDocTagsContain(SyntaxKind.JSDocTypeTag))
        {
            ReportTagAlreadySpecified(tagName);
        }

        var typeExpression = ParseJSDocTypeExpression(/*mayOmitBraces*/ true);
        var comments = indent != null && !indentText.IsNone ? ParseTrailingTagComments(start, GetNodePos(), indent.Value, indentText) : TextOrList.None;
        return FinishNode(factory.CreateJSDocTypeTag(tagName, typeExpression, comments), start);
    }

    // The callback of lookAhead in parseSeeTag.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool NextTokensAreJSDocLinkTag()
    {
        return NextTokenJSDoc() == SyntaxKind.AtToken && Scanner.TokenIsIdentifierOrKeyword(NextTokenJSDoc()) && IsJSDocLinkTag(scanner.GetTokenValueRef());
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseSeeTag(int start, Node tagName, int? indent = null, TextRef indentText = default)
    {
        var isMarkdownOrJSDocLink = Token() == SyntaxKind.OpenBracketToken
            || LookAhead(new F.NextTokensAreJSDocLinkTag());
        var nameExpression = isMarkdownOrJSDocLink ? default : ParseJSDocNameReference();
        var comments = indent != null && !indentText.IsNone ? ParseTrailingTagComments(start, GetNodePos(), indent.Value, indentText) : TextOrList.None;
        return FinishNode(factory.CreateJSDocSeeTag(tagName, nameExpression, comments), start);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseThrowsTag(int start, Node tagName, int indent, TextRef indentText)
    {
        var typeExpression = TryParseTypeExpression();
        var comment = ParseTrailingTagComments(start, GetNodePos(), indent, indentText);
        return FinishNode(factory.CreateJSDocThrowsTag(tagName, typeExpression, comment), start);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseAuthorTag(int start, Node tagName, int indent, TextRef indentText)
    {
        var commentStart = GetNodePos();
        var textOnly = ParseAuthorNameAndEmail();
        var commentEnd = scanner.GetTokenFullStart();
        var comments = ParseTrailingTagComments(start, commentEnd, indent, indentText);
        if (comments.IsNone)
        {
            commentEnd = scanner.GetTokenFullStart();
        }
        TextOrList allParts;
        if (!comments.IsText)
        {
            // TypeScript: `createNodeArray(concatenate([finishNode(textOnly, commentStart, commentEnd)], comments), commentStart)`
            var first = FinishNode(textOnly, commentStart, commentEnd);
            var elements = store.BeginList();
            store.Push(elements, first);
            if (comments.List is var commentParts && !commentParts.IsNone && store.Length(commentParts) != 0)
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
            allParts = texts.Concat(store.Text(textOnly), comments.Text);
        }
        return FinishNode(factory.CreateJSDocAuthorTag(tagName, allParts), start);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseAuthorNameAndEmail()
    {
        var comments = BeginTextParts();
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
                AddTextPart(comments, scanner.GetTokenTextRef());
                scanner.ResetTokenState(scanner.GetTokenEnd());
                break;
            }
            AddTextPart(comments, scanner.GetTokenTextRef());
            token = NextTokenJSDoc();
        }

        var joined = JoinTexts(comments);
        EndTextParts(comments);
        return factory.CreateJSDocText(joined);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseImplementsTag(int start, Node tagName, int margin, TextRef indentText)
    {
        var className = ParseExpressionWithTypeArgumentsForAugments();
        return FinishNode(factory.CreateJSDocImplementsTag(tagName, className, ParseTrailingTagComments(start, GetNodePos(), margin, indentText)), start);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseAugmentsTag(int start, Node tagName, int margin, TextRef indentText)
    {
        var className = ParseExpressionWithTypeArgumentsForAugments();
        return FinishNode(factory.CreateJSDocAugmentsTag(tagName, className, ParseTrailingTagComments(start, GetNodePos(), margin, indentText)), start);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseSatisfiesTag(int start, Node tagName, int margin, TextRef indentText)
    {
        var typeExpression = ParseJSDocTypeExpression(/*mayOmitBraces*/ false);
        // (TypeScript: `margin !== undefined && indentText !== undefined ? ... : undefined`; both are always defined.)
        var comments = ParseTrailingTagComments(start, GetNodePos(), margin, indentText);
        return FinishNode(factory.CreateJSDocSatisfiesTag(tagName, typeExpression, comments), start);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseImportTag(int start, Node tagName, int margin, TextRef indentText)
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    // TypeScript: `parseSimpleTag(start, createTag, tagName, margin, indentText)` with createTag one of the factory's
    // createJSDoc...Tag functions; Track O (O-NA): the tag's kind selects the function (no delegate).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseSimpleTag(int start, SyntaxKind kind, Node tagName, int margin, TextRef indentText)
    {
        var comment = ParseTrailingTagComments(start, GetNodePos(), margin, indentText);
        Node tag;
        switch (kind)
        {
            case SyntaxKind.JSDocClassTag:
                tag = factory.CreateJSDocClassTag(tagName, comment);
                break;
            case SyntaxKind.JSDocPublicTag:
                tag = factory.CreateJSDocPublicTag(tagName, comment);
                break;
            case SyntaxKind.JSDocPrivateTag:
                tag = factory.CreateJSDocPrivateTag(tagName, comment);
                break;
            case SyntaxKind.JSDocProtectedTag:
                tag = factory.CreateJSDocProtectedTag(tagName, comment);
                break;
            case SyntaxKind.JSDocReadonlyTag:
                tag = factory.CreateJSDocReadonlyTag(tagName, comment);
                break;
            case SyntaxKind.JSDocOverrideTag:
                tag = factory.CreateJSDocOverrideTag(tagName, comment);
                break;
            default:
                Debug.Assert(kind == SyntaxKind.JSDocDeprecatedTag);
                tag = factory.CreateJSDocDeprecatedTag(tagName, comment);
                break;
        }
        return FinishNode(tag, start);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseThisTag(int start, Node tagName, int margin, TextRef indentText)
    {
        var typeExpression = ParseJSDocTypeExpression(/*mayOmitBraces*/ true);
        SkipWhitespace();
        return FinishNode(factory.CreateJSDocThisTag(tagName, typeExpression, ParseTrailingTagComments(start, GetNodePos(), margin, indentText)), start);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseEnumTag(int start, Node tagName, int margin, TextRef indentText)
    {
        var typeExpression = ParseJSDocTypeExpression(/*mayOmitBraces*/ true);
        SkipWhitespace();
        return FinishNode(factory.CreateJSDocEnumTag(tagName, typeExpression, ParseTrailingTagComments(start, GetNodePos(), margin, indentText)), start);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseTypedefTag(int start, Node tagName, int indent, TextRef indentText)
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
            while (!(child = TryParse(new F.ParseChildPropertyTag(indent))).IsNone)
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
                            AddTagFirstSpecifiedInfo(lastError);
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
        end = end is not null and not 0 || !comment.IsNone ?
            GetNodePos() :
            store.End(fullName.IsNone ? (typeExpression.IsNone ? tagName : typeExpression) : fullName);

        if (comment.IsNone)
        {
            comment = ParseTrailingTagComments(start, end.Value, indent, indentText);
        }

        var typedefTag = factory.CreateJSDocTypedefTag(tagName, typeExpression, fullName, comment);
        return FinishNode(typedefTag, start, end);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private NodeList ParseCallbackTagParameters(int indent)
    {
        var pos = GetNodePos();
        Node child;
        // (TypeScript: `let parameters` grown by `append`, then `createNodeArray(parameters || [], pos)`.)
        var parameters = store.BeginList();
        while (!(child = TryParse(new F.ParseChildParameterOrPropertyTag2(PropertyLikeParse.CallbackParameter, indent))).IsNone)
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseJSDocSignature(int start, int indent)
    {
        var parameters = ParseCallbackTagParameters(indent);
        var returnTag = TryParse(new F.ParseReturnTagOfSignature(indent));
        return FinishNode(factory.CreateJSDocSignature(/*typeParameters*/ default, parameters, returnTag), start);
    }

    // The callback of tryParse in parseJSDocSignature.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseReturnTagOfSignature(int indent)
    {
        if (ParseOptionalJsdoc(SyntaxKind.AtToken))
        {
            var tag = ParseTag(indent);
            if (!tag.IsNone && store.Kind(tag) == SyntaxKind.JSDocReturnTag)
            {
                return tag;
            }
        }
        return default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseCallbackTag(int start, Node tagName, int indent, TextRef indentText)
    {
        var fullName = ParseJSDocTypeNameWithNamespace();
        SkipWhitespace();
        var comment = ParseTagComments(indent);
        var typeExpression = ParseJSDocSignature(start, indent);
        if (comment.IsNone)
        {
            comment = ParseTrailingTagComments(start, GetNodePos(), indent, indentText);
        }
        var end = !comment.IsNone ? GetNodePos() : store.End(typeExpression);
        return FinishNode(factory.CreateJSDocCallbackTag(tagName, typeExpression, fullName, comment), start, end);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseOverloadTag(int start, Node tagName, int indent, TextRef indentText)
    {
        SkipWhitespace();
        var comment = ParseTagComments(indent);
        var typeExpression = ParseJSDocSignature(start, indent);
        if (comment.IsNone)
        {
            comment = ParseTrailingTagComments(start, GetNodePos(), indent, indentText);
        }
        var end = !comment.IsNone ? GetNodePos() : store.End(typeExpression);
        return FinishNode(factory.CreateJSDocOverloadTag(tagName, typeExpression, comment), start, end);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool EscapedTextsEqual(NodeStore store, Node a, Node b)
    {
        while (!FactoryNodeTests.IsIdentifier(store, a) || !FactoryNodeTests.IsIdentifier(store, b))
        {
            if (!FactoryNodeTests.IsIdentifier(store, a) && !FactoryNodeTests.IsIdentifier(store, b) && store.Texts.Equals(store.EscapedText(store.Right(a)), store.EscapedText(store.Right(b))))
            {
                a = store.Left(a);
                b = store.Left(b);
            }
            else
            {
                return false;
            }
        }
        return store.Texts.Equals(store.EscapedText(a), store.EscapedText(b));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseChildPropertyTag(int indent)
    {
        return ParseChildParameterOrPropertyTag(PropertyLikeParse.Property, indent);
    }

    /// <returns>The tag, or null for TypeScript's `false`.</returns>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static Node GetPropertyLikeTagName(NodeStore store, Node tag)
    {
        return store.Kind(tag) == SyntaxKind.JSDocParameterTag ? store.Name(tag) : store.Name(tag);
    }

    /// <returns>The tag, or null for TypeScript's `false`.</returns>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node TryParseChildTag(PropertyLikeParse target, int indent)
    {
        Debug.Assert(Token() == SyntaxKind.AtToken);
        var start = scanner.GetTokenFullStart();
        NextTokenJSDoc();

        var tagName = ParseJSDocIdentifierName();
        var indentText = SkipWhitespaceOrAsterisk();
        PropertyLikeParse t;
        switch (texts.Span(store.EscapedText(tagName)))
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
            defaultType = DoInsideOfContext(NodeFlags.JSDoc, new F.ParseJSDocType());
            ParseExpected(SyntaxKind.CloseBracketToken);
        }

        if (Utilities.NodeIsMissing(store, name))
        {
            return default;
        }
        return FinishNode(factory.CreateTypeParameterDeclaration(modifiers, name, /*constraint*/ default, defaultType), typeParameterPos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseTemplateTag(int start, Node tagName, int indent, TextRef indentText)
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool ParseOptionalJsdoc(SyntaxKind t)
    {
        if (Token() == t)
        {
            NextTokenJSDoc();
            return true;
        }
        return false;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
        var text = InternIdentifier(scanner.GetTokenValueRef());
        var result = FinishNode(factory.CreateIdentifier(text, originalKeywordKind), start, end);
        NextTokenJSDoc();
        return result;
    }

    /// <summary>JavaScript's `s.slice(start)` with one argument (a negative start counts from the end); Track O: a slice of the text value.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static TextRef JsSlice(TextRef s, int start)
    {
        var length = s.Length;
        if (start < 0)
        {
            start = Math.Max(length + start, 0);
        }
        return start >= length ? TextRef.Empty : start == 0 ? s : s.Slice(start);
    }

    /// <summary>JavaScript's String.prototype.trimEnd; Track O: a slice of the text value.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private TextRef TrimEnd(TextRef s)
    {
        var span = texts.Span(s);
        var end = span.Length;
        while (end > 0 && Utilities.IsJsWhiteSpaceOrLineTerminator(span[end - 1]))
        {
            end--;
        }
        return end == span.Length ? s : s.Slice(0, end);
    }

    /// <summary>`parts.join("")` (Track O: adjacent spans of the text stay one span; other parts go to the character arena).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private TextRef JoinTexts(TextParts parts)
    {
        var value = new TextBuilder(texts);
        foreach (var part in textParts.AsSpan(parts.Start, textPartsTop - parts.Start))
        {
            value.Append(part);
        }
        return value.Result();
    }
}
