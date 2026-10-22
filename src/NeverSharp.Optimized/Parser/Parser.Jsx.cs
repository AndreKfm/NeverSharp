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

/// <summary>parser.ts: `namespace Parser`, the JSX grammar.</summary>
internal sealed partial class Parser
{
    /// <summary>
    /// TypeScript's return type is `JsxElement | JsxSelfClosingElement | JsxFragment`, but in the error case below it
    /// returns a BinaryExpression (`as Node as JsxElement`); the C# return type is Node.
    /// </summary>
    private Node ParseJsxElementOrSelfClosingElementOrFragment(bool inExpressionContext, int? topInvalidNodePosition = null, Node openingTag = default, bool mustBeUnary = false)
    {
        var pos = GetNodePos();
        var opening = ParseJsxOpeningOrSelfClosingElementOrOpeningFragment(inExpressionContext);
        Node result;
        if (store.Kind(opening) == SyntaxKind.JsxOpeningElement)
        {
            var openingElement = opening;
            var children = ParseJsxChildren(opening);
            Node closingElement;

            var lastChild = Core.LastOrUndefined(store.Nodes(children));
            if (
                store.Kind(lastChild) == SyntaxKind.JsxElement
                && !TagNamesAreEquivalent(store, store.TagName(store.OpeningElement(lastChild)), store.TagName(store.ClosingElement(lastChild)))
                && TagNamesAreEquivalent(store, store.TagName(openingElement), store.TagName(store.ClosingElement(lastChild))))
            {
                var lastElement = lastChild;
                // when an unclosed JsxOpeningElement incorrectly parses its parent's JsxClosingElement,
                // restructure (<div>(...<span>...</div>)) --> (<div>(...<span>...</>)</div>)
                // (no need to error; the parent will error)
                var end = store.End(store.Children(lastElement));
                var newLast = FinishNode(
                    factory.CreateJsxElement(
                        store.OpeningElement(lastElement),
                        store.Children(lastElement),
                        FinishNode(factory.CreateJsxClosingElement(FinishNode(factory.CreateIdentifier(""), end, end)), end, end)),
                    store.Pos(store.OpeningElement(lastElement)),
                    end);

                // `[...children.slice(0, children.length - 1), newLast]`
                var newChildren = store.BeginList();
                var oldChildren = store.Nodes(children);
                for (var i = 0; i < oldChildren.Length - 1; i++)
                {
                    store.Push(newChildren, oldChildren[i]);
                }
                store.Push(newChildren, newLast);
                children = CreateNodeArray(newChildren, store.Pos(children), end);
                closingElement = store.ClosingElement(lastElement);
            }
            else
            {
                closingElement = ParseJsxClosingElement(openingElement, inExpressionContext);
                if (!TagNamesAreEquivalent(store, store.TagName(openingElement), store.TagName(closingElement)))
                {
                    if (!openingTag.IsNone && FactoryNodeTests.IsJsxOpeningElement(store, openingTag) && TagNamesAreEquivalent(store, store.TagName(closingElement), store.TagName(openingTag)))
                    {
                        // opening incorrectly matched with its parent's closing -- put error on opening
                        ParseErrorAtRange(store.TagName(openingElement), Diagnostics.JSX_element_0_has_no_corresponding_closing_tag, Utilities.GetTextOfNodeFromSourceText(store, sourceText, store.TagName(openingElement)));
                    }
                    else
                    {
                        // other opening/closing mismatches -- put error on closing
                        ParseErrorAtRange(store.TagName(closingElement), Diagnostics.Expected_corresponding_JSX_closing_tag_for_0, Utilities.GetTextOfNodeFromSourceText(store, sourceText, store.TagName(openingElement)));
                    }
                }
            }
            result = FinishNode(factory.CreateJsxElement(openingElement, children, closingElement), pos);
        }
        else if (store.Kind(opening) == SyntaxKind.JsxOpeningFragment)
        {
            result = FinishNode(factory.CreateJsxFragment(opening, ParseJsxChildren(opening), ParseJsxClosingFragment(inExpressionContext)), pos);
        }
        else
        {
            Debug.Assert(store.Kind(opening) == SyntaxKind.JsxSelfClosingElement);
            // Nothing else to do for self-closing elements
            result = opening;
        }

        // If the user writes the invalid code '<div></div><div></div>' in an expression context (i.e. not wrapped in
        // an enclosing tag), we'll naively try to parse   ^ this as a 'less than' operator and the remainder of the tag
        // as garbage, which will cause the formatter to badly mangle the JSX. Perform a speculative parse of a JSX
        // element if we see a < token so that we can wrap it in a synthetic binary expression so the formatter
        // does less damage and we can report a better error.
        // Since JSX elements are invalid < operands anyway, this lookahead parse will only occur in error scenarios
        // of one sort or another.
        // If we are in a unary context, we can't do this recovery; the binary expression we return here is not
        // a valid UnaryExpression and will cause problems later.
        if (!mustBeUnary && inExpressionContext && Token() == SyntaxKind.LessThanToken)
        {
            var topBadPos = topInvalidNodePosition == null ? store.Pos(result) : topInvalidNodePosition.Value;
            var invalidElement = TryParse(p => ParseJsxElementOrSelfClosingElementOrFragment(/*inExpressionContext*/ true, topBadPos));
            if (!invalidElement.IsNone)
            {
                var operatorToken = CreateMissingNode(SyntaxKind.CommaToken, /*reportAtCurrentPosition*/ false);
                Utilities.SetTextRangePosWidth(store, operatorToken, store.Pos(invalidElement), 0);
                ParseErrorAt(Scanner.SkipTrivia(sourceText, topBadPos), store.End(invalidElement), Diagnostics.JSX_expressions_must_have_one_parent_element);
                return FinishNode(factory.CreateBinaryExpression(result, operatorToken, invalidElement), pos);
            }
        }

        return result;
    }

    private Node ParseJsxText()
    {
        var pos = GetNodePos();
        var node = factory.CreateJsxText(scanner.GetTokenValueRef(), currentToken == SyntaxKind.JsxTextAllWhiteSpaces);
        currentToken = scanner.ScanJsxToken();
        return FinishNode(node, pos);
    }

    private Node ParseJsxChild(Node openingTag, SyntaxKind token)
    {
        switch (token)
        {
            case SyntaxKind.EndOfFileToken:
                // If we hit EOF, issue the error at the tag that lacks the closing element
                // rather than at the end of the file (which is useless)
                if (FactoryNodeTests.IsJsxOpeningFragment(store, openingTag))
                {
                    ParseErrorAtRange(openingTag, Diagnostics.JSX_fragment_has_no_corresponding_closing_tag);
                }
                else
                {
                    // We want the error span to cover only 'Foo.Bar' in < Foo.Bar >
                    // or to cover only 'Foo' in < Foo >
                    var tag = store.TagName(openingTag);
                    var start = Math.Min(Scanner.SkipTrivia(sourceText, store.Pos(tag)), store.End(tag));
                    ParseErrorAt(start, store.End(tag), Diagnostics.JSX_element_0_has_no_corresponding_closing_tag, Utilities.GetTextOfNodeFromSourceText(store, sourceText, store.TagName(openingTag)));
                }
                return default;
            case SyntaxKind.LessThanSlashToken:
            case SyntaxKind.ConflictMarkerTrivia:
                return default;
            case SyntaxKind.JsxText:
            case SyntaxKind.JsxTextAllWhiteSpaces:
                return ParseJsxText();
            case SyntaxKind.OpenBraceToken:
                return ParseJsxExpression(/*inExpressionContext*/ false);
            case SyntaxKind.LessThanToken:
                return ParseJsxElementOrSelfClosingElementOrFragment(/*inExpressionContext*/ false, /*topInvalidNodePosition*/ null, openingTag);
            default:
                Debug.AssertNever(token);
                return default;
        }
    }

    private NodeList ParseJsxChildren(Node openingTag)
    {
        var list = store.BeginList();
        var listPos = GetNodePos();
        var saveParsingContext = parsingContext;
        parsingContext |= 1 << (int)ParsingContext.JsxChildren;

        while (true)
        {
            var child = ParseJsxChild(openingTag, currentToken = scanner.ReScanJsxToken());
            if (child.IsNone)
            {
                break;
            }
            store.Push(list, child);
            if (
                FactoryNodeTests.IsJsxOpeningElement(store, openingTag)
                && store.Kind(child) == SyntaxKind.JsxElement
                && !TagNamesAreEquivalent(store, store.TagName(store.OpeningElement(child)), store.TagName(store.ClosingElement(child)))
                && TagNamesAreEquivalent(store, store.TagName(openingTag), store.TagName(store.ClosingElement(child))))
            {
                // stop after parsing a mismatched child like <div>...(<span></div>) in order to reattach the </div> higher
                break;
            }
        }

        parsingContext = saveParsingContext;
        return CreateNodeArray(list, listPos);
    }

    private Node ParseJsxAttributes()
    {
        var pos = GetNodePos();
        return FinishNode(factory.CreateJsxAttributes(ParseList(ParsingContext.JsxAttributes, static p => p.ParseJsxAttribute())), pos);
    }

    private Node ParseJsxOpeningOrSelfClosingElementOrOpeningFragment(bool inExpressionContext)
    {
        var pos = GetNodePos();

        ParseExpected(SyntaxKind.LessThanToken);

        if (Token() == SyntaxKind.GreaterThanToken)
        {
            // See below for explanation of scanJsxText
            ScanJsxText();
            return FinishNode(factory.CreateJsxOpeningFragment(), pos);
        }
        var tagName = ParseJsxElementName();
        var typeArguments = (contextFlags & NodeFlags.JavaScriptFile) == 0 ? TryParseTypeArguments() : default;
        var attributes = ParseJsxAttributes();

        Node node;

        if (Token() == SyntaxKind.GreaterThanToken)
        {
            // Closing tag, so scan the immediately-following text with the JSX scanning instead
            // of regular scanning to avoid treating illegal characters (e.g. '#') as immediate
            // scanning errors
            ScanJsxText();
            node = factory.CreateJsxOpeningElement(tagName, typeArguments, attributes);
        }
        else
        {
            ParseExpected(SyntaxKind.SlashToken);
            if (ParseExpected(SyntaxKind.GreaterThanToken, /*diagnosticMessage*/ null, /*shouldAdvance*/ false))
            {
                // manually advance the scanner in order to look for jsx text inside jsx
                if (inExpressionContext)
                {
                    NextToken();
                }
                else
                {
                    ScanJsxText();
                }
            }
            node = factory.CreateJsxSelfClosingElement(tagName, typeArguments, attributes);
        }

        return FinishNode(node, pos);
    }

    private Node ParseJsxElementName()
    {
        var pos = GetNodePos();
        // JsxElement can have name in the form of
        //      propertyAccessExpression
        //      primaryExpression in the form of an identifier and "this" keyword
        // We can't just simply use parseLeftHandSideExpressionOrHigher because then we will start consider class,function etc as a keyword
        // We only want to consider "this" as a primaryExpression
        var initialExpression = ParseJsxTagName();
        if (FactoryNodeTests.IsJsxNamespacedName(store, initialExpression))
        {
            return initialExpression; // `a:b.c` is invalid syntax, don't even look for the `.` if we parse `a:b`, and let `parseAttribute` report "unexpected :" instead.
        }
        var expression = initialExpression;
        while (ParseOptional(SyntaxKind.DotToken))
        {
            expression = FinishNode(factory.CreatePropertyAccessExpression(expression, ParseRightSideOfDot(/*allowIdentifierNames*/ true, /*allowPrivateIdentifiers*/ false, /*allowUnicodeEscapeSequenceInIdentifierName*/ false)), pos);
        }
        return expression;
    }

    private Node ParseJsxTagName()
    {
        var pos = GetNodePos();
        ScanJsxIdentifier();

        var isThis = Token() == SyntaxKind.ThisKeyword;
        var tagName = ParseIdentifierNameErrorOnUnicodeEscapeSequence();
        if (ParseOptional(SyntaxKind.ColonToken))
        {
            ScanJsxIdentifier();
            return FinishNode(factory.CreateJsxNamespacedName(tagName, ParseIdentifierNameErrorOnUnicodeEscapeSequence()), pos);
        }
        return isThis ? FinishNode(factory.CreateToken(SyntaxKind.ThisKeyword), pos) : tagName;
    }

    private Node ParseJsxExpression(bool inExpressionContext)
    {
        var pos = GetNodePos();
        if (!ParseExpected(SyntaxKind.OpenBraceToken))
        {
            return default;
        }

        Node dotDotDotToken = default;
        Node expression = default;
        if (Token() != SyntaxKind.CloseBraceToken)
        {
            if (!inExpressionContext)
            {
                dotDotDotToken = ParseOptionalToken(SyntaxKind.DotDotDotToken);
            }
            // Only an AssignmentExpression is valid here per the JSX spec,
            // but we can unambiguously parse a comma sequence and provide
            // a better error message in grammar checking.
            expression = ParseExpression();
        }
        if (inExpressionContext)
        {
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            if (ParseExpected(SyntaxKind.CloseBraceToken, /*diagnosticMessage*/ null, /*shouldAdvance*/ false))
            {
                ScanJsxText();
            }
        }

        return FinishNode(factory.CreateJsxExpression(dotDotDotToken, expression), pos);
    }

    private Node ParseJsxAttribute()
    {
        if (Token() == SyntaxKind.OpenBraceToken)
        {
            return ParseJsxSpreadAttribute();
        }

        var pos = GetNodePos();
        return FinishNode(factory.CreateJsxAttribute(ParseJsxAttributeName(), ParseJsxAttributeValue()), pos);
    }

    private Node ParseJsxAttributeValue()
    {
        if (Token() == SyntaxKind.EqualsToken)
        {
            if (ScanJsxAttributeValue() == SyntaxKind.StringLiteral)
            {
                return ParseLiteralNode();
            }
            if (Token() == SyntaxKind.OpenBraceToken)
            {
                return ParseJsxExpression(/*inExpressionContext*/ true);
            }
            if (Token() == SyntaxKind.LessThanToken)
            {
                return ParseJsxElementOrSelfClosingElementOrFragment(/*inExpressionContext*/ true);
            }
            ParseErrorAtCurrentToken(Diagnostics.or_JSX_element_expected);
        }
        return default;
    }

    private Node ParseJsxAttributeName()
    {
        var pos = GetNodePos();
        ScanJsxIdentifier();

        var attrName = ParseIdentifierNameErrorOnUnicodeEscapeSequence();
        if (ParseOptional(SyntaxKind.ColonToken))
        {
            ScanJsxIdentifier();
            return FinishNode(factory.CreateJsxNamespacedName(attrName, ParseIdentifierNameErrorOnUnicodeEscapeSequence()), pos);
        }
        return attrName;
    }

    private Node ParseJsxSpreadAttribute()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        ParseExpected(SyntaxKind.DotDotDotToken);
        var expression = ParseExpression();
        ParseExpected(SyntaxKind.CloseBraceToken);
        return FinishNode(factory.CreateJsxSpreadAttribute(expression), pos);
    }

    private Node ParseJsxClosingElement(Node open, bool inExpressionContext)
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.LessThanSlashToken);
        var tagName = ParseJsxElementName();
        if (ParseExpected(SyntaxKind.GreaterThanToken, /*diagnosticMessage*/ null, /*shouldAdvance*/ false))
        {
            // manually advance the scanner in order to look for jsx text inside jsx
            if (inExpressionContext || !TagNamesAreEquivalent(store, store.TagName(open), tagName))
            {
                NextToken();
            }
            else
            {
                ScanJsxText();
            }
        }
        return FinishNode(factory.CreateJsxClosingElement(tagName), pos);
    }

    private Node ParseJsxClosingFragment(bool inExpressionContext)
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.LessThanSlashToken);
        if (ParseExpected(SyntaxKind.GreaterThanToken, Diagnostics.Expected_corresponding_closing_tag_for_JSX_fragment, /*shouldAdvance*/ false))
        {
            // manually advance the scanner in order to look for jsx text inside jsx
            if (inExpressionContext)
            {
                NextToken();
            }
            else
            {
                ScanJsxText();
            }
        }
        return FinishNode(factory.CreateJsxJsxClosingFragment(), pos);
    }
}
