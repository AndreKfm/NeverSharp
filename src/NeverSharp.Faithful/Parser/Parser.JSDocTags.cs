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

/// <summary>parser.ts: `namespace JSDocParser`, the tags (parseTag and the parsers of the individual tags).</summary>
internal sealed partial class Parser
{
    private Node? ParseTag(int margin)
    {
        Debug.Assert(Token() == SyntaxKind.AtToken);
        var start = scanner.GetTokenStart();
        NextTokenJSDoc();

        var tagName = ParseJSDocIdentifierName(/*message*/ null);
        var indentText = SkipWhitespaceOrAsterisk();

        Node? tag;
        switch ((string)tagName.EscapedText!)
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

    private JSDocUnknownTag ParseUnknownTag(int start, Identifier tagName, int indent, string indentText)
    {
        return FinishNode(factory.CreateJSDocUnknownTag(tagName, ParseTrailingTagComments(start, GetNodePos(), indent, indentText)), start);
    }

    private JSDocTypeExpression? TryParseTypeExpression()
    {
        SkipWhitespaceOrAsterisk();
        return Token() == SyntaxKind.OpenBraceToken ? ParseJSDocTypeExpression() : null;
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
            if (ParseOptionalToken(SyntaxKind.EqualsToken) != null)
            {
                ParseExpression();
            }

            ParseExpected(SyntaxKind.CloseBracketToken);
        }

        return (name, isBracketed);
    }

    private static bool IsObjectOrObjectArrayTypeReference(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ObjectKeyword:
                return true;
            case SyntaxKind.ArrayType:
                return IsObjectOrObjectArrayTypeReference(((ArrayTypeNode)node).ElementType);
            default:
                return FactoryNodeTests.IsTypeReferenceNode(node)
                    && FactoryNodeTests.IsIdentifier(((TypeReferenceNode)node).TypeName)
                    && (string)((Identifier)((TypeReferenceNode)node).TypeName).EscapedText! == "Object"
                    && ((TypeReferenceNode)node).TypeArguments == null;
        }
    }

    private Node ParseParameterOrPropertyTag(int start, Identifier tagName, PropertyLikeParse target, int indent)
    {
        var typeExpression = TryParseTypeExpression();
        var isNameFirst = typeExpression == null;
        SkipWhitespaceOrAsterisk();

        var (name, isBracketed) = ParseBracketNameInPropertyAndParamTag();
        var indentText = SkipWhitespaceOrAsterisk();

        if (isNameFirst && !IsTruthy(LookAhead(static p => p.ParseJSDocLinkPrefix())))
        {
            typeExpression = TryParseTypeExpression();
        }

        var comment = ParseTrailingTagComments(start, GetNodePos(), indent, indentText);

        var nestedTypeLiteral = ParseNestedTypeLiteral(typeExpression, name, target, indent);
        if (nestedTypeLiteral != null)
        {
            typeExpression = nestedTypeLiteral;
            isNameFirst = true;
        }
        Node result = target == PropertyLikeParse.Property
            ? factory.CreateJSDocPropertyTag(tagName, name, isBracketed, typeExpression, isNameFirst, comment)
            : factory.CreateJSDocParameterTag(tagName, name, isBracketed, typeExpression, isNameFirst, comment);
        return FinishNode(result, start);
    }

    private JSDocTypeExpression? ParseNestedTypeLiteral(JSDocTypeExpression? typeExpression, Node name, PropertyLikeParse target, int indent)
    {
        if (typeExpression != null && IsObjectOrObjectArrayTypeReference(typeExpression.Type))
        {
            var pos = GetNodePos();
            Node? child;
            List<Node>? children = null;
            while ((child = TryParse(p => p.ParseChildParameterOrPropertyTag(target, indent, name))) != null)
            {
                if (child.Kind == SyntaxKind.JSDocParameterTag || child.Kind == SyntaxKind.JSDocPropertyTag)
                {
                    children = Core.Append(children, child);
                }
                else if (child.Kind == SyntaxKind.JSDocTemplateTag)
                {
                    ParseErrorAtRange(((JSDocTemplateTag)child).TagName, Diagnostics.A_JSDoc_template_tag_may_not_follow_a_typedef_callback_or_overload_tag);
                }
            }
            if (children != null)
            {
                var literal = FinishNode(factory.CreateJSDocTypeLiteral(children, typeExpression.Type.Kind == SyntaxKind.ArrayType), pos);
                return FinishNode(factory.CreateJSDocTypeExpression(literal), pos);
            }
        }
        return null;
    }

    private JSDocReturnTag ParseReturnTag(int start, Identifier tagName, int indent, string indentText)
    {
        if (Core.Some(jsDocTags, FactoryNodeTests.IsJSDocReturnTag))
        {
            ParseErrorAt(tagName.Pos, scanner.GetTokenStart(), Diagnostics._0_tag_already_specified, UtilitiesPublic.UnescapeLeadingUnderscores((string)tagName.EscapedText!));
        }

        var typeExpression = TryParseTypeExpression();
        return FinishNode(factory.CreateJSDocReturnTag(tagName, typeExpression, ParseTrailingTagComments(start, GetNodePos(), indent, indentText)), start);
    }

    private JSDocTypeTag ParseTypeTag(int start, Identifier tagName, int? indent = null, string? indentText = null)
    {
        if (Core.Some(jsDocTags, FactoryNodeTests.IsJSDocTypeTag))
        {
            ParseErrorAt(tagName.Pos, scanner.GetTokenStart(), Diagnostics._0_tag_already_specified, UtilitiesPublic.UnescapeLeadingUnderscores((string)tagName.EscapedText!));
        }

        var typeExpression = ParseJSDocTypeExpression(/*mayOmitBraces*/ true);
        var comments = indent != null && indentText != null ? ParseTrailingTagComments(start, GetNodePos(), indent.Value, indentText) : null;
        return FinishNode(factory.CreateJSDocTypeTag(tagName, typeExpression, comments), start);
    }

    private JSDocSeeTag ParseSeeTag(int start, Identifier tagName, int? indent = null, string? indentText = null)
    {
        var isMarkdownOrJSDocLink = Token() == SyntaxKind.OpenBracketToken
            || LookAhead(static p => p.NextTokenJSDoc() == SyntaxKind.AtToken && Scanner.TokenIsIdentifierOrKeyword(p.NextTokenJSDoc()) && IsJSDocLinkTag(p.scanner.GetTokenValue()));
        var nameExpression = isMarkdownOrJSDocLink ? null : ParseJSDocNameReference();
        var comments = indent != null && indentText != null ? ParseTrailingTagComments(start, GetNodePos(), indent.Value, indentText) : null;
        return FinishNode(factory.CreateJSDocSeeTag(tagName, nameExpression, comments), start);
    }

    private JSDocThrowsTag ParseThrowsTag(int start, Identifier tagName, int indent, string indentText)
    {
        var typeExpression = TryParseTypeExpression();
        var comment = ParseTrailingTagComments(start, GetNodePos(), indent, indentText);
        return FinishNode(factory.CreateJSDocThrowsTag(tagName, typeExpression, comment), start);
    }

    private JSDocAuthorTag ParseAuthorTag(int start, Identifier tagName, int indent, string indentText)
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
        if (comments is not string)
        {
            var parts = new NodeArray { FinishNode(textOnly, commentStart, commentEnd) };
            if (comments is NodeArray commentParts)
            {
                parts.AddRange(commentParts);
            }
            allParts = CreateNodeArray(parts, commentStart);
        }
        else
        {
            allParts = textOnly.Text + (string)comments;
        }
        return FinishNode(factory.CreateJSDocAuthorTag(tagName, allParts), start);
    }

    private JSDocText ParseAuthorNameAndEmail()
    {
        var comments = new List<string>();
        var inEmail = false;
        var tok = scanner.GetToken();
        while (tok != SyntaxKind.EndOfFileToken && tok != SyntaxKind.NewLineTrivia)
        {
            if (tok == SyntaxKind.LessThanToken)
            {
                inEmail = true;
            }
            else if (tok == SyntaxKind.AtToken && !inEmail)
            {
                break;
            }
            else if (tok == SyntaxKind.GreaterThanToken && inEmail)
            {
                comments.Add(scanner.GetTokenText());
                scanner.ResetTokenState(scanner.GetTokenEnd());
                break;
            }
            comments.Add(scanner.GetTokenText());
            tok = NextTokenJSDoc();
        }

        return factory.CreateJSDocText(string.Concat(comments));
    }

    private JSDocImplementsTag ParseImplementsTag(int start, Identifier tagName, int margin, string indentText)
    {
        var className = ParseExpressionWithTypeArgumentsForAugments();
        return FinishNode(factory.CreateJSDocImplementsTag(tagName, className, ParseTrailingTagComments(start, GetNodePos(), margin, indentText)), start);
    }

    private JSDocAugmentsTag ParseAugmentsTag(int start, Identifier tagName, int margin, string indentText)
    {
        var className = ParseExpressionWithTypeArgumentsForAugments();
        return FinishNode(factory.CreateJSDocAugmentsTag(tagName, className, ParseTrailingTagComments(start, GetNodePos(), margin, indentText)), start);
    }

    private JSDocSatisfiesTag ParseSatisfiesTag(int start, Identifier tagName, int margin, string indentText)
    {
        var typeExpression = ParseJSDocTypeExpression(/*mayOmitBraces*/ false);
        var comments = ParseTrailingTagComments(start, GetNodePos(), margin, indentText);
        return FinishNode(factory.CreateJSDocSatisfiesTag(tagName, typeExpression, comments), start);
    }

    private JSDocImportTag ParseImportTag(int start, Identifier tagName, int margin, string indentText)
    {
        var afterImportTagPos = scanner.GetTokenFullStart();

        Identifier? identifier = null;
        if (IsIdentifier())
        {
            identifier = ParseIdentifier();
        }

        var importClause = TryParseImportClause(identifier, afterImportTagPos, SyntaxKind.TypeKeyword, /*skipJsDocLeadingAsterisks*/ true);
        var moduleSpecifier = ParseModuleSpecifier();
        var attributes = TryParseImportAttributes();

        var comments = ParseTrailingTagComments(start, GetNodePos(), margin, indentText);
        return FinishNode(factory.CreateJSDocImportTag(tagName, importClause, moduleSpecifier, attributes, comments), start);
    }

    private ExpressionWithTypeArguments ParseExpressionWithTypeArgumentsForAugments()
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

    private Node ParseSimpleTag(int start, Func<NodeFactory, Identifier?, object?, Node> createTag, Identifier tagName, int margin, string indentText)
    {
        return FinishNode(createTag(factory, tagName, ParseTrailingTagComments(start, GetNodePos(), margin, indentText)), start);
    }

    private JSDocThisTag ParseThisTag(int start, Identifier tagName, int margin, string indentText)
    {
        var typeExpression = ParseJSDocTypeExpression(/*mayOmitBraces*/ true);
        SkipWhitespace();
        return FinishNode(factory.CreateJSDocThisTag(tagName, typeExpression, ParseTrailingTagComments(start, GetNodePos(), margin, indentText)), start);
    }

    private JSDocEnumTag ParseEnumTag(int start, Identifier tagName, int margin, string indentText)
    {
        var typeExpression = ParseJSDocTypeExpression(/*mayOmitBraces*/ true);
        SkipWhitespace();
        return FinishNode(factory.CreateJSDocEnumTag(tagName, typeExpression, ParseTrailingTagComments(start, GetNodePos(), margin, indentText)), start);
    }

    private JSDocTypedefTag ParseTypedefTag(int start, Identifier tagName, int indent, string indentText)
    {
        Node? typeExpression = TryParseTypeExpression();
        SkipWhitespaceOrAsterisk();

        var fullName = ParseJSDocTypeNameWithNamespace();
        SkipWhitespace();
        var comment = ParseTagComments(indent);

        int? end = null;
        if (typeExpression == null || IsObjectOrObjectArrayTypeReference(((JSDocTypeExpression)typeExpression).Type))
        {
            Node? child;
            JSDocTypeTag? childTypeTag = null;
            List<Node>? jsDocPropertyTags = null;
            var hasChildren = false;
            while ((child = TryParse(p => p.ParseChildPropertyTag(indent))) != null)
            {
                if (child.Kind == SyntaxKind.JSDocTemplateTag)
                {
                    break;
                }
                hasChildren = true;
                if (child.Kind == SyntaxKind.JSDocTypeTag)
                {
                    if (childTypeTag != null)
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
                        childTypeTag = (JSDocTypeTag)child;
                    }
                }
                else
                {
                    jsDocPropertyTags = Core.Append(jsDocPropertyTags, child);
                }
            }
            if (hasChildren)
            {
                var isArrayType = typeExpression != null && ((JSDocTypeExpression)typeExpression).Type.Kind == SyntaxKind.ArrayType;
                var jsdocTypeLiteral = factory.CreateJSDocTypeLiteral(jsDocPropertyTags, isArrayType);
                typeExpression = childTypeTag != null && childTypeTag.TypeExpression != null && !IsObjectOrObjectArrayTypeReference(childTypeTag.TypeExpression.Type) ?
                    childTypeTag.TypeExpression :
                    FinishNode(jsdocTypeLiteral, start);
                end = typeExpression.End;
            }
        }

        // Only include the characters between the name end and the next token if a comment was actually parsed out - otherwise it's just whitespace
        end = (end is not null and not 0) || comment != null ?
            GetNodePos() :
            (fullName ?? typeExpression ?? tagName).End;

        if (comment == null)
        {
            comment = ParseTrailingTagComments(start, end.Value, indent, indentText);
        }

        var typedefTag = factory.CreateJSDocTypedefTag(tagName, typeExpression, fullName, comment);
        return FinishNode(typedefTag, start, end);
    }

    private Node? ParseJSDocTypeNameWithNamespace(bool nested = false)
    {
        var start = scanner.GetTokenStart();
        if (!Scanner.TokenIsIdentifierOrKeyword(Token()))
        {
            return null;
        }
        var typeNameOrNamespaceName = ParseJSDocIdentifierName();
        if (ParseOptional(SyntaxKind.DotToken))
        {
            var body = ParseJSDocTypeNameWithNamespace(/*nested*/ true);
            var jsDocNamespaceNode = factory.CreateModuleDeclaration(
                /*modifiers*/ null,
                typeNameOrNamespaceName,
                body,
                nested ? NodeFlags.NestedNamespace : NodeFlags.None);
            return FinishNode(jsDocNamespaceNode, start);
        }

        if (nested)
        {
            typeNameOrNamespaceName.Flags |= NodeFlags.IdentifierIsInJSDocNamespace;
        }
        return typeNameOrNamespaceName;
    }

    private NodeArray ParseCallbackTagParameters(int indent)
    {
        var pos = GetNodePos();
        Node? child;
        List<Node>? parameters = null;
        while ((child = TryParse(p => p.ParseChildParameterOrPropertyTag(PropertyLikeParse.CallbackParameter, indent))) != null)
        {
            if (child.Kind == SyntaxKind.JSDocTemplateTag)
            {
                ParseErrorAtRange(((JSDocTemplateTag)child).TagName, Diagnostics.A_JSDoc_template_tag_may_not_follow_a_typedef_callback_or_overload_tag);
                break;
            }
            parameters = Core.Append(parameters, child);
        }
        var array = new NodeArray();
        if (parameters != null)
        {
            array.AddRange(parameters);
        }
        return CreateNodeArray(array, pos);
    }

    private JSDocSignature ParseJSDocSignature(int start, int indent)
    {
        var parameters = ParseCallbackTagParameters(indent);
        var returnTag = TryParse(p => p.ParseJSDocSignatureReturnTag(indent));
        return FinishNode(factory.CreateJSDocSignature(/*typeParameters*/ null, parameters, returnTag), start);
    }

    private JSDocReturnTag? ParseJSDocSignatureReturnTag(int indent)
    {
        if (ParseOptionalJsdoc(SyntaxKind.AtToken))
        {
            var tag = ParseTag(indent);
            if (tag != null && tag.Kind == SyntaxKind.JSDocReturnTag)
            {
                return (JSDocReturnTag)tag;
            }
        }
        return null;
    }

    private JSDocCallbackTag ParseCallbackTag(int start, Identifier tagName, int indent, string indentText)
    {
        var fullName = ParseJSDocTypeNameWithNamespace();
        SkipWhitespace();
        var comment = ParseTagComments(indent);
        var typeExpression = ParseJSDocSignature(start, indent);
        if (comment == null)
        {
            comment = ParseTrailingTagComments(start, GetNodePos(), indent, indentText);
        }
        var end = comment != null ? GetNodePos() : typeExpression.End;
        return FinishNode(factory.CreateJSDocCallbackTag(tagName, typeExpression, fullName, comment), start, end);
    }

    private JSDocOverloadTag ParseOverloadTag(int start, Identifier tagName, int indent, string indentText)
    {
        SkipWhitespace();
        var comment = ParseTagComments(indent);
        var typeExpression = ParseJSDocSignature(start, indent);
        if (comment == null)
        {
            comment = ParseTrailingTagComments(start, GetNodePos(), indent, indentText);
        }
        var end = comment != null ? GetNodePos() : typeExpression.End;
        return FinishNode(factory.CreateJSDocOverloadTag(tagName, typeExpression, comment), start, end);
    }

    private static bool EscapedTextsEqual(Node a, Node b)
    {
        while (!FactoryNodeTests.IsIdentifier(a) || !FactoryNodeTests.IsIdentifier(b))
        {
            if (!FactoryNodeTests.IsIdentifier(a) && !FactoryNodeTests.IsIdentifier(b)
                && (string)((QualifiedName)a).Right.EscapedText! == (string)((QualifiedName)b).Right.EscapedText!)
            {
                a = ((QualifiedName)a).Left;
                b = ((QualifiedName)b).Left;
            }
            else
            {
                return false;
            }
        }
        return (string)((Identifier)a).EscapedText! == (string)((Identifier)b).EscapedText!;
    }

    private Node? ParseChildPropertyTag(int indent)
    {
        return ParseChildParameterOrPropertyTag(PropertyLikeParse.Property, indent);
    }

    /// <summary>Returns the child tag, or null where TypeScript returns `false`.</summary>
    private Node? ParseChildParameterOrPropertyTag(PropertyLikeParse target, int indent, Node? name = null)
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
                            child != null && (child.Kind == SyntaxKind.JSDocParameterTag || child.Kind == SyntaxKind.JSDocPropertyTag) &&
                            name != null && (FactoryNodeTests.IsIdentifier(ChildTagName(child)) || !EscapedTextsEqual(name, ((QualifiedName)ChildTagName(child)).Left)))
                        {
                            return null;
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
                    return null;
            }
        }
    }

    private static Node ChildTagName(Node child)
    {
        return child is JSDocParameterTag parameterTag ? parameterTag.Name : ((JSDocPropertyTag)child).Name;
    }

    private Node? TryParseChildTag(PropertyLikeParse target, int indent)
    {
        Debug.Assert(Token() == SyntaxKind.AtToken);
        var start = scanner.GetTokenFullStart();
        NextTokenJSDoc();

        var tagName = ParseJSDocIdentifierName();
        var indentText = SkipWhitespaceOrAsterisk();
        PropertyLikeParse t;
        switch ((string)tagName.EscapedText!)
        {
            case "type":
                return target == PropertyLikeParse.Property ? ParseTypeTag(start, tagName) : null;
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
                return null;
        }
        if ((target & t) == 0)
        {
            return null;
        }
        return ParseParameterOrPropertyTag(start, tagName, target, indent);
    }

    private TypeParameterDeclaration? ParseTemplateTagTypeParameter()
    {
        var typeParameterPos = GetNodePos();
        var isBracketed = ParseOptionalJsdoc(SyntaxKind.OpenBracketToken);
        if (isBracketed)
        {
            SkipWhitespace();
        }

        var modifiers = ParseModifiers(/*allowDecorators*/ false, /*permitConstAsModifier*/ true);
        var name = ParseJSDocIdentifierName(Diagnostics.Unexpected_token_A_type_parameter_name_was_expected_without_curly_braces);
        Node? defaultType = null;
        if (isBracketed)
        {
            SkipWhitespace();
            ParseExpected(SyntaxKind.EqualsToken);
            defaultType = DoInsideOfContext(NodeFlags.JSDoc, static p => p.ParseJSDocType());
            ParseExpected(SyntaxKind.CloseBracketToken);
        }

        if (Utilities.NodeIsMissing(name))
        {
            return null;
        }
        return FinishNode(factory.CreateTypeParameterDeclaration(modifiers, name, /*constraint*/ null, defaultType), typeParameterPos);
    }

    private NodeArray ParseTemplateTagTypeParameters()
    {
        var pos = GetNodePos();
        var typeParameters = new NodeArray();
        do
        {
            SkipWhitespace();
            var node = ParseTemplateTagTypeParameter();
            if (node != null)
            {
                typeParameters.Add(node);
            }
            SkipWhitespaceOrAsterisk();
        }
        while (ParseOptionalJsdoc(SyntaxKind.CommaToken));
        return CreateNodeArray(typeParameters, pos);
    }

    private JSDocTemplateTag ParseTemplateTag(int start, Identifier tagName, int indent, string indentText)
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
        var constraint = Token() == SyntaxKind.OpenBraceToken ? ParseJSDocTypeExpression() : null;
        var typeParameters = ParseTemplateTagTypeParameters();
        return FinishNode(factory.CreateJSDocTemplateTag(tagName, constraint, typeParameters, ParseTrailingTagComments(start, GetNodePos(), indent, indentText)), start);
    }
}
