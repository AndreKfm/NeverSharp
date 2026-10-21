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

namespace NeverSharp.Faithful;

/// <summary>parser.ts, namespace Parser: the TYPES section (parseEntityNameOfTypeReference ... parseTypeAnnotation).</summary>
internal sealed partial class Parser
{
    // TYPES

    private Node ParseEntityNameOfTypeReference()
    {
        return ParseEntityName(/*allowReservedWords*/ true, Diagnostics.Type_expected);
    }

    private NodeArray? ParseTypeArgumentsOfTypeReference()
    {
        if (!scanner.HasPrecedingLineBreak() && ReScanLessThanToken() == SyntaxKind.LessThanToken)
        {
            return ParseBracketedList(ParsingContext.TypeArguments, ParseType, SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken);
        }
        return null;
    }

    private TypeReferenceNode ParseTypeReference()
    {
        var pos = GetNodePos();
        return FinishNode(
            factory.CreateTypeReferenceNode(
                ParseEntityNameOfTypeReference(),
                ParseTypeArgumentsOfTypeReference()),
            pos);
    }

    // If true, we should abort parsing an error function.
    private static bool TypeHasArrowFunctionBlockingParseError(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.TypeReference:
                return Utilities.NodeIsMissing(((TypeReferenceNode)node).TypeName);
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
                {
                    // const { parameters, type } = node as FunctionOrConstructorTypeNode;
                    var parameters = NodeAccessors.GetParameters(node)!;
                    var type = NodeAccessors.GetTypeNode(node)!;
                    return IsMissingList(parameters) || TypeHasArrowFunctionBlockingParseError(type);
                }
            case SyntaxKind.ParenthesizedType:
                return TypeHasArrowFunctionBlockingParseError(((ParenthesizedTypeNode)node).Type);
            default:
                return false;
        }
    }

    private TypePredicateNode ParseThisTypePredicate(ThisTypeNode lhs)
    {
        NextToken();
        return FinishNode(factory.CreateTypePredicateNode(/*assertsModifier*/ null, lhs, ParseType()), lhs.Pos);
    }

    private ThisTypeNode ParseThisTypeNode()
    {
        var pos = GetNodePos();
        NextToken();
        return FinishNode(factory.CreateThisTypeNode(), pos);
    }

    private Node ParseJSDocAllType()
    {
        var pos = GetNodePos();
        NextToken();
        return FinishNode(factory.CreateJSDocAllType(), pos);
    }

    private Node ParseJSDocNonNullableType()
    {
        var pos = GetNodePos();
        NextToken();
        return FinishNode(factory.CreateJSDocNonNullableType(ParseNonArrayType(), /*postfix*/ false), pos);
    }

    private Node ParseJSDocUnknownOrNullableType()
    {
        var pos = GetNodePos();
        // skip the ?
        NextToken();

        // Need to lookahead to decide if this is a nullable or unknown type.

        // Here are cases where we'll pick the unknown type:
        //
        //      Foo(?,
        //      { a: ? }
        //      Foo(?)
        //      Foo<?>
        //      Foo(?=
        //      (?|
        if (
            Token() == SyntaxKind.CommaToken ||
            Token() == SyntaxKind.CloseBraceToken ||
            Token() == SyntaxKind.CloseParenToken ||
            Token() == SyntaxKind.GreaterThanToken ||
            Token() == SyntaxKind.EqualsToken ||
            Token() == SyntaxKind.BarToken)
        {
            return FinishNode(factory.CreateJSDocUnknownType(), pos);
        }
        else
        {
            return FinishNode(factory.CreateJSDocNullableType(ParseType(), /*postfix*/ false), pos);
        }
    }

    private Node ParseJSDocFunctionType()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        if (TryParse(NextTokenIsOpenParen))
        {
            var parameters = ParseParameters(SignatureFlags.Type | SignatureFlags.JSDoc);
            var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
            return WithJSDoc(FinishNode(factory.CreateJSDocFunctionType(parameters, type), pos), hasJSDoc);
        }
        return FinishNode(factory.CreateTypeReferenceNode(ParseIdentifierName(), /*typeArguments*/ null), pos);
    }

    private ParameterDeclaration ParseJSDocParameter()
    {
        var pos = GetNodePos();
        Identifier? name = null;
        if (Token() == SyntaxKind.ThisKeyword || Token() == SyntaxKind.NewKeyword)
        {
            name = ParseIdentifierName();
            ParseExpected(SyntaxKind.ColonToken);
        }
        return FinishNode(
            factory.CreateParameterDeclaration(
                /*modifiers*/ null,
                /*dotDotDotToken*/ null,
                // TODO(rbuckton): JSDoc parameters don't have names (except `this`/`new`), should we manufacture an empty identifier?
                name!,
                /*questionToken*/ null,
                ParseJSDocType(),
                /*initializer*/ null),
            pos);
    }

    private Node ParseJSDocType()
    {
        scanner.SetSkipJsDocLeadingAsterisks(true);
        var pos = GetNodePos();
        if (ParseOptional(SyntaxKind.ModuleKeyword))
        {
            // TODO(rbuckton): We never set the type for a JSDocNamepathType. What should we put here?
            var moduleTag = factory.CreateJSDocNamepathType(/*type*/ null!);
            while (true)
            {
                switch (Token())
                {
                    case SyntaxKind.CloseBraceToken:
                    case SyntaxKind.EndOfFileToken:
                    case SyntaxKind.CommaToken:
                    case SyntaxKind.WhitespaceTrivia:
                        goto terminate;
                    default:
                        NextTokenJSDoc();
                        break;
                }
            }

        terminate:
            scanner.SetSkipJsDocLeadingAsterisks(false);
            return FinishNode(moduleTag, pos);
        }

        var hasDotDotDot = ParseOptional(SyntaxKind.DotDotDotToken);
        var type = ParseTypeOrTypePredicate();
        scanner.SetSkipJsDocLeadingAsterisks(false);
        if (hasDotDotDot)
        {
            type = FinishNode(factory.CreateJSDocVariadicType(type), pos);
        }
        if (Token() == SyntaxKind.EqualsToken)
        {
            NextToken();
            return FinishNode(factory.CreateJSDocOptionalType(type), pos);
        }
        return type;
    }

    private TypeQueryNode ParseTypeQuery()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.TypeOfKeyword);
        var entityName = ParseEntityName(/*allowReservedWords*/ true);
        // Make sure we perform ASI to prevent parsing the next line's type arguments as part of an instantiation expression.
        var typeArguments = !scanner.HasPrecedingLineBreak() ? TryParseTypeArguments() : null;
        return FinishNode(factory.CreateTypeQueryNode(entityName, typeArguments), pos);
    }

    private TypeParameterDeclaration ParseTypeParameter()
    {
        var pos = GetNodePos();
        var modifiers = ParseModifiers(/*allowDecorators*/ false, /*permitConstAsModifier*/ true);
        var name = ParseIdentifier();
        Node? constraint = null;
        Node? expression = null;
        if (ParseOptional(SyntaxKind.ExtendsKeyword))
        {
            // It's not uncommon for people to write improper constraints to a generic.  If the
            // user writes a constraint that is an expression and not an actual type, then parse
            // it out as an expression (so we can recover well), but report that a type is needed
            // instead.
            if (IsStartOfType() || !IsStartOfExpression())
            {
                constraint = ParseType();
            }
            else
            {
                // It was not a type, and it looked like an expression.  Parse out an expression
                // here so we recover well.  Note: it is important that we call parseUnaryExpression
                // and not parseExpression here.  If the user has:
                //
                //      <T extends "">
                //
                // We do *not* want to consume the `>` as we're consuming the expression for "".
                expression = ParseUnaryExpressionOrHigher();
            }
        }

        var defaultType = ParseOptional(SyntaxKind.EqualsToken) ? ParseType() : null;
        var node = factory.CreateTypeParameterDeclaration(modifiers, name, constraint, defaultType);
        node.Expression = expression;
        return FinishNode(node, pos);
    }

    private NodeArray? ParseTypeParameters()
    {
        if (Token() == SyntaxKind.LessThanToken)
        {
            return ParseBracketedList(ParsingContext.TypeParameters, ParseTypeParameter, SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken);
        }
        return null;
    }

    private bool IsStartOfParameter(bool isJSDocParameter)
    {
        return Token() == SyntaxKind.DotDotDotToken ||
            IsBindingIdentifierOrPrivateIdentifierOrPattern() ||
            UtilitiesPublic.IsModifierKind(Token()) ||
            Token() == SyntaxKind.AtToken ||
            IsStartOfType(/*inStartOfParameter*/ !isJSDocParameter);
    }

    private Node ParseNameOfParameter(NodeArray? modifiers)
    {
        // FormalParameter [Yield,Await]:
        //      BindingElement[?Yield,?Await]
        var name = ParseIdentifierOrPattern(Diagnostics.Private_identifiers_cannot_be_used_as_parameters);
        if (Utilities.GetFullWidth(name) == 0 && !Core.Some(modifiers) && UtilitiesPublic.IsModifierKind(Token()))
        {
            // in cases like
            // 'use strict'
            // function foo(static)
            // isParameter('static') === true, because of isModifier('static')
            // however 'static' is not a legal identifier in a strict mode.
            // so result of this function will be ParameterDeclaration (flags = 0, name = missing, type = undefined, initializer = undefined)
            // and current token will not change => parsing of the enclosing parameter list will last till the end of time (or OOM)
            // to avoid this we'll advance cursor to the next token.
            NextToken();
        }
        return name;
    }

    private bool IsParameterNameStart()
    {
        // Be permissive about await and yield by calling isBindingIdentifier instead of isIdentifier; disallowing
        // them during a speculative parse leads to many more follow-on errors than allowing the function to parse then later
        // complaining about the use of the keywords.
        return IsBindingIdentifier() || Token() == SyntaxKind.OpenBracketToken || Token() == SyntaxKind.OpenBraceToken;
    }

    private ParameterDeclaration ParseParameter(bool inOuterAwaitContext)
    {
        return ParseParameterWorker(inOuterAwaitContext)!;
    }

    private ParameterDeclaration? ParseParameterForSpeculation(bool inOuterAwaitContext)
    {
        return ParseParameterWorker(inOuterAwaitContext, /*allowAmbiguity*/ false);
    }

    /// <summary>
    /// TypeScript overloads: with allowAmbiguity true (the default) the result is never undefined; with false it may be.
    /// </summary>
    private ParameterDeclaration? ParseParameterWorker(bool inOuterAwaitContext, bool allowAmbiguity = true)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();

        // FormalParameter [Yield,Await]:
        //      BindingElement[?Yield,?Await]

        // Decorators are parsed in the outer [Await] context, the rest of the parameter is parsed in the function's [Await] context.
        var modifiers = inOuterAwaitContext ?
            DoInAwaitContext(() => ParseModifiers(/*allowDecorators*/ true)) :
            DoOutsideOfAwaitContext(() => ParseModifiers(/*allowDecorators*/ true));

        if (Token() == SyntaxKind.ThisKeyword)
        {
            var thisNode = factory.CreateParameterDeclaration(
                modifiers,
                /*dotDotDotToken*/ null,
                CreateIdentifier(/*isIdentifier*/ true),
                /*questionToken*/ null,
                ParseTypeAnnotation(),
                /*initializer*/ null);

            var modifier = Core.FirstOrUndefined(modifiers);
            if (modifier != null)
            {
                ParseErrorAtRange(modifier, Diagnostics.Neither_decorators_nor_modifiers_may_be_applied_to_this_parameters);
            }

            return WithJSDoc(FinishNode(thisNode, pos), hasJSDoc);
        }

        var savedTopLevel = topLevel;
        topLevel = false;

        var dotDotDotToken = ParseOptionalToken<DotDotDotToken>(SyntaxKind.DotDotDotToken);

        if (!allowAmbiguity && !IsParameterNameStart())
        {
            return null;
        }

        var node = WithJSDoc(
            FinishNode(
                factory.CreateParameterDeclaration(
                    modifiers,
                    dotDotDotToken,
                    ParseNameOfParameter(modifiers),
                    ParseOptionalToken<QuestionToken>(SyntaxKind.QuestionToken),
                    ParseTypeAnnotation(),
                    ParseInitializer()),
                pos),
            hasJSDoc);
        topLevel = savedTopLevel;
        return node;
    }

    /// <summary>
    /// TypeScript overloads: with returnToken EqualsGreaterThanToken the result is never undefined.
    /// </summary>
    private Node? ParseReturnType(SyntaxKind returnToken, bool isType)
    {
        if (ShouldParseReturnType(returnToken, isType))
        {
            return AllowConditionalTypesAnd(ParseTypeOrTypePredicate);
        }
        return null;
    }

    private bool ShouldParseReturnType(SyntaxKind returnToken, bool isType)
    {
        if (returnToken == SyntaxKind.EqualsGreaterThanToken)
        {
            ParseExpected(returnToken);
            return true;
        }
        else if (ParseOptional(SyntaxKind.ColonToken))
        {
            return true;
        }
        else if (isType && Token() == SyntaxKind.EqualsGreaterThanToken)
        {
            // This is easy to get backward, especially in type contexts, so parse the type anyway
            ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.ColonToken));
            NextToken();
            return true;
        }
        return false;
    }

    /// <summary>
    /// TypeScript overloads: with allowAmbiguity true the result is never undefined; with false it may be.
    /// </summary>
    private NodeArray? ParseParametersWorker(SignatureFlags flags, bool allowAmbiguity)
    {
        // FormalParameters [Yield,Await]: (modified)
        //      [empty]
        //      FormalParameterList[?Yield,Await]
        //
        // FormalParameter[Yield,Await]: (modified)
        //      BindingElement[?Yield,Await]
        //
        // BindingElement [Yield,Await]: (modified)
        //      SingleNameBinding[?Yield,?Await]
        //      BindingPattern[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        //
        // SingleNameBinding [Yield,Await]:
        //      BindingIdentifier[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        var savedYieldContext = InYieldContext();
        var savedAwaitContext = InAwaitContext();

        SetYieldContext((flags & SignatureFlags.Yield) != 0);
        SetAwaitContext((flags & SignatureFlags.Await) != 0);

        var parameters = (flags & SignatureFlags.JSDoc) != 0 ?
            ParseDelimitedList(ParsingContext.JSDocParameters, ParseJSDocParameter) :
            ParseDelimitedListAllowingUndefined(ParsingContext.Parameters, () => allowAmbiguity ? ParseParameter(savedAwaitContext) : ParseParameterForSpeculation(savedAwaitContext));

        SetYieldContext(savedYieldContext);
        SetAwaitContext(savedAwaitContext);

        return parameters;
    }

    private NodeArray ParseParameters(SignatureFlags flags)
    {
        // FormalParameters [Yield,Await]: (modified)
        //      [empty]
        //      FormalParameterList[?Yield,Await]
        //
        // FormalParameter[Yield,Await]: (modified)
        //      BindingElement[?Yield,Await]
        //
        // BindingElement [Yield,Await]: (modified)
        //      SingleNameBinding[?Yield,?Await]
        //      BindingPattern[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        //
        // SingleNameBinding [Yield,Await]:
        //      BindingIdentifier[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        if (!ParseExpected(SyntaxKind.OpenParenToken))
        {
            return CreateMissingList();
        }

        var parameters = ParseParametersWorker(flags, /*allowAmbiguity*/ true)!;
        ParseExpected(SyntaxKind.CloseParenToken);
        return parameters;
    }

    private void ParseTypeMemberSemicolon()
    {
        // We allow type members to be separated by commas or (possibly ASI) semicolons.
        // First check if it was a comma.  If so, we're done with the member.
        if (ParseOptional(SyntaxKind.CommaToken))
        {
            return;
        }

        // Didn't have a comma.  We must have a (possible ASI) semicolon.
        ParseSemicolon();
    }

    private Node ParseSignatureMember(SyntaxKind kind)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        if (kind == SyntaxKind.ConstructSignature)
        {
            ParseExpected(SyntaxKind.NewKeyword);
        }

        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(SignatureFlags.Type);
        var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ true);
        ParseTypeMemberSemicolon();
        var node = kind == SyntaxKind.CallSignature
            ? (Node)factory.CreateCallSignature(typeParameters, parameters, type)
            : factory.CreateConstructSignature(typeParameters, parameters, type);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private bool IsIndexSignature()
    {
        return Token() == SyntaxKind.OpenBracketToken && LookAhead(IsUnambiguouslyIndexSignature);
    }

    private bool IsUnambiguouslyIndexSignature()
    {
        // The only allowed sequence is:
        //
        //   [id:
        //
        // However, for error recovery, we also check the following cases:
        //
        //   [...
        //   [id,
        //   [id?,
        //   [id?:
        //   [id?]
        //   [public id
        //   [private id
        //   [protected id
        //   []
        //
        NextToken();
        if (Token() == SyntaxKind.DotDotDotToken || Token() == SyntaxKind.CloseBracketToken)
        {
            return true;
        }

        if (UtilitiesPublic.IsModifierKind(Token()))
        {
            NextToken();
            if (IsIdentifier())
            {
                return true;
            }
        }
        else if (!IsIdentifier())
        {
            return false;
        }
        else
        {
            // Skip the identifier
            NextToken();
        }

        // A colon signifies a well formed indexer
        // A comma should be a badly formed indexer because comma expressions are not allowed
        // in computed properties.
        if (Token() == SyntaxKind.ColonToken || Token() == SyntaxKind.CommaToken)
        {
            return true;
        }

        // Question mark could be an indexer with an optional property,
        // or it could be a conditional expression in a computed property.
        if (Token() != SyntaxKind.QuestionToken)
        {
            return false;
        }

        // If any of the following tokens are after the question mark, it cannot
        // be a conditional expression, so treat it as an indexer.
        NextToken();
        return Token() == SyntaxKind.ColonToken || Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.CloseBracketToken;
    }

    private IndexSignatureDeclaration ParseIndexSignatureDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        var parameters = ParseBracketedList(ParsingContext.Parameters, () => ParseParameter(/*inOuterAwaitContext*/ false), SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken);
        var type = ParseTypeAnnotation();
        ParseTypeMemberSemicolon();
        var node = factory.CreateIndexSignature(modifiers, parameters, type);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private Node ParsePropertyOrMethodSignature(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        var name = ParsePropertyName();
        var questionToken = ParseOptionalToken<QuestionToken>(SyntaxKind.QuestionToken);
        Node node;
        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            // Method signatures don't exist in expression contexts.  So they have neither
            // [Yield] nor [Await]
            var typeParameters = ParseTypeParameters();
            var parameters = ParseParameters(SignatureFlags.Type);
            var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ true);
            node = factory.CreateMethodSignature(modifiers, name, questionToken, typeParameters, parameters, type);
        }
        else
        {
            var type = ParseTypeAnnotation();
            node = factory.CreatePropertySignature(modifiers, name, questionToken, type);
            // Although type literal properties cannot not have initializers, we attempt
            // to parse an initializer so we can report in the checker that an interface
            // property or type literal property cannot have an initializer.
            if (Token() == SyntaxKind.EqualsToken)
            {
                ((PropertySignature)node).Initializer = ParseInitializer();
            }
        }
        ParseTypeMemberSemicolon();
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private bool IsTypeMemberStart()
    {
        // Return true if we have the start of a signature member
        if (
            Token() == SyntaxKind.OpenParenToken ||
            Token() == SyntaxKind.LessThanToken ||
            Token() == SyntaxKind.GetKeyword ||
            Token() == SyntaxKind.SetKeyword)
        {
            return true;
        }
        var idToken = false;
        // Eat up all modifiers, but hold on to the last one in case it is actually an identifier
        while (UtilitiesPublic.IsModifierKind(Token()))
        {
            idToken = true;
            NextToken();
        }
        // Index signatures and computed property names are type members
        if (Token() == SyntaxKind.OpenBracketToken)
        {
            return true;
        }
        // Try to get the first property-like token following all modifiers
        if (IsLiteralPropertyName())
        {
            idToken = true;
            NextToken();
        }
        // If we were able to get any potential identifier, check that it is
        // the start of a member declaration
        if (idToken)
        {
            return Token() == SyntaxKind.OpenParenToken ||
                Token() == SyntaxKind.LessThanToken ||
                Token() == SyntaxKind.QuestionToken ||
                Token() == SyntaxKind.ColonToken ||
                Token() == SyntaxKind.CommaToken ||
                CanParseSemicolon();
        }
        return false;
    }

    private Node ParseTypeMember()
    {
        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            return ParseSignatureMember(SyntaxKind.CallSignature);
        }
        if (Token() == SyntaxKind.NewKeyword && LookAhead(NextTokenIsOpenParenOrLessThan))
        {
            return ParseSignatureMember(SyntaxKind.ConstructSignature);
        }
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(/*allowDecorators*/ false);
        if (ParseContextualModifier(SyntaxKind.GetKeyword))
        {
            return ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.GetAccessor, SignatureFlags.Type);
        }

        if (ParseContextualModifier(SyntaxKind.SetKeyword))
        {
            return ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.SetAccessor, SignatureFlags.Type);
        }

        if (IsIndexSignature())
        {
            return ParseIndexSignatureDeclaration(pos, hasJSDoc, modifiers);
        }
        return ParsePropertyOrMethodSignature(pos, hasJSDoc, modifiers);
    }

    private bool NextTokenIsOpenParenOrLessThan()
    {
        NextToken();
        return Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken;
    }

    private bool NextTokenIsDot()
    {
        return NextToken() == SyntaxKind.DotToken;
    }

    private bool NextTokenIsOpenParenOrLessThanOrDot()
    {
        switch (NextToken())
        {
            case SyntaxKind.OpenParenToken:
            case SyntaxKind.LessThanToken:
            case SyntaxKind.DotToken:
                return true;
        }
        return false;
    }

    private TypeLiteralNode ParseTypeLiteral()
    {
        var pos = GetNodePos();
        return FinishNode(factory.CreateTypeLiteralNode(ParseObjectTypeMembers()), pos);
    }

    private NodeArray ParseObjectTypeMembers()
    {
        NodeArray members;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            members = ParseList(ParsingContext.TypeMembers, ParseTypeMember);
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            members = CreateMissingList();
        }

        return members;
    }

    private bool IsStartOfMappedType()
    {
        NextToken();
        if (Token() == SyntaxKind.PlusToken || Token() == SyntaxKind.MinusToken)
        {
            return NextToken() == SyntaxKind.ReadonlyKeyword;
        }
        if (Token() == SyntaxKind.ReadonlyKeyword)
        {
            NextToken();
        }
        return Token() == SyntaxKind.OpenBracketToken && NextTokenIsIdentifier() && NextToken() == SyntaxKind.InKeyword;
    }

    private TypeParameterDeclaration ParseMappedTypeParameter()
    {
        var pos = GetNodePos();
        var name = ParseIdentifierName();
        ParseExpected(SyntaxKind.InKeyword);
        var type = ParseType();
        return FinishNode(factory.CreateTypeParameterDeclaration(/*modifiers*/ null, name, type, /*defaultType*/ null), pos);
    }

    private MappedTypeNode ParseMappedType()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        Node? readonlyToken = null;
        if (Token() == SyntaxKind.ReadonlyKeyword || Token() == SyntaxKind.PlusToken || Token() == SyntaxKind.MinusToken)
        {
            readonlyToken = ParseTokenNode();
            if (readonlyToken.Kind != SyntaxKind.ReadonlyKeyword)
            {
                ParseExpected(SyntaxKind.ReadonlyKeyword);
            }
        }
        ParseExpected(SyntaxKind.OpenBracketToken);
        var typeParameter = ParseMappedTypeParameter();
        var nameType = ParseOptional(SyntaxKind.AsKeyword) ? ParseType() : null;
        ParseExpected(SyntaxKind.CloseBracketToken);
        Node? questionToken = null;
        if (Token() == SyntaxKind.QuestionToken || Token() == SyntaxKind.PlusToken || Token() == SyntaxKind.MinusToken)
        {
            questionToken = ParseTokenNode();
            if (questionToken.Kind != SyntaxKind.QuestionToken)
            {
                ParseExpected(SyntaxKind.QuestionToken);
            }
        }
        var type = ParseTypeAnnotation();
        ParseSemicolon();
        var members = ParseList(ParsingContext.TypeMembers, ParseTypeMember);
        ParseExpected(SyntaxKind.CloseBraceToken);
        return FinishNode(factory.CreateMappedTypeNode(readonlyToken, typeParameter, nameType, questionToken, type, members), pos);
    }

    private Node ParseTupleElementType()
    {
        var pos = GetNodePos();
        if (ParseOptional(SyntaxKind.DotDotDotToken))
        {
            return FinishNode(factory.CreateRestTypeNode(ParseType()), pos);
        }
        var type = ParseType();
        if (NodeTests.IsJSDocNullableType(type) && type.Pos == ((JSDocNullableType)type).Type.Pos)
        {
            var node = factory.CreateOptionalTypeNode(((JSDocNullableType)type).Type);
            FactoryUtilitiesPublic.SetTextRange(node, type);
            node.Flags = type.Flags;
            return node;
        }
        return type;
    }

    private bool IsNextTokenColonOrQuestionColon()
    {
        return NextToken() == SyntaxKind.ColonToken || (Token() == SyntaxKind.QuestionToken && NextToken() == SyntaxKind.ColonToken);
    }

    private bool IsTupleElementName()
    {
        if (Token() == SyntaxKind.DotDotDotToken)
        {
            return Scanner.TokenIsIdentifierOrKeyword(NextToken()) && IsNextTokenColonOrQuestionColon();
        }
        return Scanner.TokenIsIdentifierOrKeyword(Token()) && IsNextTokenColonOrQuestionColon();
    }

    private Node ParseTupleElementNameOrTupleElementType()
    {
        if (LookAhead(IsTupleElementName))
        {
            var pos = GetNodePos();
            var hasJSDoc = HasPrecedingJSDocComment();
            var dotDotDotToken = ParseOptionalToken<DotDotDotToken>(SyntaxKind.DotDotDotToken);
            var name = ParseIdentifierName();
            var questionToken = ParseOptionalToken<QuestionToken>(SyntaxKind.QuestionToken);
            ParseExpected(SyntaxKind.ColonToken);
            var type = ParseTupleElementType();
            var node = factory.CreateNamedTupleMember(dotDotDotToken, name, questionToken, type);
            return WithJSDoc(FinishNode(node, pos), hasJSDoc);
        }
        return ParseTupleElementType();
    }

    private TupleTypeNode ParseTupleType()
    {
        var pos = GetNodePos();
        return FinishNode(
            factory.CreateTupleTypeNode(
                ParseBracketedList(ParsingContext.TupleElementTypes, ParseTupleElementNameOrTupleElementType, SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken)),
            pos);
    }

    private Node ParseParenthesizedType()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenParenToken);
        var type = ParseType();
        ParseExpected(SyntaxKind.CloseParenToken);
        return FinishNode(factory.CreateParenthesizedType(type), pos);
    }

    private NodeArray? ParseModifiersForConstructorType()
    {
        NodeArray? modifiers = null;
        if (Token() == SyntaxKind.AbstractKeyword)
        {
            var pos = GetNodePos();
            NextToken();
            var modifier = FinishNode(factory.CreateToken(SyntaxKind.AbstractKeyword), pos);
            modifiers = CreateNodeArray([modifier], pos);
        }
        return modifiers;
    }

    private Node ParseFunctionOrConstructorType()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiersForConstructorType();
        var isConstructorType = ParseOptional(SyntaxKind.NewKeyword);
        Debug.Assert(modifiers == null || isConstructorType, "Per isStartOfFunctionOrConstructorType, a function type cannot have modifiers.");
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(SignatureFlags.Type);
        // parseReturnType(SyntaxKind.EqualsGreaterThanToken, ...) is the overload that returns TypeNode (never undefined).
        var type = ParseReturnType(SyntaxKind.EqualsGreaterThanToken, /*isType*/ false)!;
        var node = isConstructorType
            ? (Node)factory.CreateConstructorTypeNode(modifiers, typeParameters, parameters, type)
            : factory.CreateFunctionTypeNode(typeParameters, parameters, type);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private Node? ParseKeywordAndNoDot()
    {
        var node = ParseTokenNode();
        return Token() == SyntaxKind.DotToken ? null : node;
    }

    private LiteralTypeNode ParseLiteralTypeNode(bool negative = false)
    {
        var pos = GetNodePos();
        if (negative)
        {
            NextToken();
        }
        var expression = Token() == SyntaxKind.TrueKeyword || Token() == SyntaxKind.FalseKeyword || Token() == SyntaxKind.NullKeyword ?
            ParseTokenNode() :
            ParseLiteralLikeNode(Token());
        if (negative)
        {
            expression = FinishNode(factory.CreatePrefixUnaryExpression(SyntaxKind.MinusToken, expression), pos);
        }
        return FinishNode(factory.CreateLiteralTypeNode(expression), pos);
    }

    private bool IsStartOfTypeOfImportType()
    {
        NextToken();
        return Token() == SyntaxKind.ImportKeyword;
    }

    private ImportTypeNode ParseImportType()
    {
        sourceFlags |= NodeFlags.PossiblyContainsDynamicImport;
        var pos = GetNodePos();
        var isTypeOf = ParseOptional(SyntaxKind.TypeOfKeyword);
        ParseExpected(SyntaxKind.ImportKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);
        var type = ParseType();
        ImportAttributes? attributes = null;
        if (ParseOptional(SyntaxKind.CommaToken))
        {
            var openBracePosition = scanner.GetTokenStart();
            ParseExpected(SyntaxKind.OpenBraceToken);
            var currentToken = Token();
            if (currentToken == SyntaxKind.WithKeyword || currentToken == SyntaxKind.AssertKeyword)
            {
                NextToken();
            }
            else
            {
                ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.WithKeyword));
            }
            ParseExpected(SyntaxKind.ColonToken);
            attributes = ParseImportAttributes(currentToken, /*skipKeyword*/ true);
            ParseOptional(SyntaxKind.CommaToken);
            if (!ParseExpected(SyntaxKind.CloseBraceToken))
            {
                var lastError = Core.LastOrUndefined(parseDiagnostics);
                if (lastError != null && lastError.Code == Diagnostics._0_expected.Code)
                {
                    Utilities.AddRelatedInfo(
                        lastError,
                        Utilities.CreateDetachedDiagnostic(fileName, sourceText, openBracePosition, 1, Diagnostics.The_parser_expected_to_find_a_1_to_match_the_0_token_here, "{", "}"));
                }
            }
        }
        ParseExpected(SyntaxKind.CloseParenToken);
        var qualifier = ParseOptional(SyntaxKind.DotToken) ? ParseEntityNameOfTypeReference() : null;
        var typeArguments = ParseTypeArgumentsOfTypeReference();
        return FinishNode(factory.CreateImportTypeNode(type, attributes, qualifier, typeArguments, isTypeOf), pos);
    }

    private bool NextTokenIsNumericOrBigIntLiteral()
    {
        NextToken();
        return Token() == SyntaxKind.NumericLiteral || Token() == SyntaxKind.BigIntLiteral;
    }

    private Node ParseNonArrayType()
    {
        switch (Token())
        {
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.UnknownKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.BigIntKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.UndefinedKeyword:
            case SyntaxKind.NeverKeyword:
            case SyntaxKind.ObjectKeyword:
                // If these are followed by a dot, then parse these out as a dotted type reference instead.
                return TryParse(ParseKeywordAndNoDot) ?? ParseTypeReference();
            case SyntaxKind.AsteriskEqualsToken:
                // If there is '*=', treat it as * followed by postfix =
                scanner.ReScanAsteriskEqualsToken();
                goto case SyntaxKind.AsteriskToken; // falls through
            case SyntaxKind.AsteriskToken:
                return ParseJSDocAllType();
            case SyntaxKind.QuestionQuestionToken:
                // If there is '??', treat it as prefix-'?' in JSDoc type.
                scanner.ReScanQuestionToken();
                goto case SyntaxKind.QuestionToken; // falls through
            case SyntaxKind.QuestionToken:
                return ParseJSDocUnknownOrNullableType();
            case SyntaxKind.FunctionKeyword:
                return ParseJSDocFunctionType();
            case SyntaxKind.ExclamationToken:
                return ParseJSDocNonNullableType();
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NullKeyword:
                return ParseLiteralTypeNode();
            case SyntaxKind.MinusToken:
                return LookAhead(NextTokenIsNumericOrBigIntLiteral) ? ParseLiteralTypeNode(/*negative*/ true) : ParseTypeReference();
            case SyntaxKind.VoidKeyword:
                return ParseTokenNode();
            case SyntaxKind.ThisKeyword:
                {
                    var thisKeyword = ParseThisTypeNode();
                    if (Token() == SyntaxKind.IsKeyword && !scanner.HasPrecedingLineBreak())
                    {
                        return ParseThisTypePredicate(thisKeyword);
                    }
                    else
                    {
                        return thisKeyword;
                    }
                }
            case SyntaxKind.TypeOfKeyword:
                return LookAhead(IsStartOfTypeOfImportType) ? ParseImportType() : ParseTypeQuery();
            case SyntaxKind.OpenBraceToken:
                return LookAhead(IsStartOfMappedType) ? ParseMappedType() : ParseTypeLiteral();
            case SyntaxKind.OpenBracketToken:
                return ParseTupleType();
            case SyntaxKind.OpenParenToken:
                return ParseParenthesizedType();
            case SyntaxKind.ImportKeyword:
                return ParseImportType();
            case SyntaxKind.AssertsKeyword:
                return LookAhead(NextTokenIsIdentifierOrKeywordOnSameLine) ? ParseAssertsTypePredicate() : ParseTypeReference();
            case SyntaxKind.TemplateHead:
                return ParseTemplateType();
            default:
                return ParseTypeReference();
        }
    }

    private bool IsStartOfType(bool inStartOfParameter = false)
    {
        switch (Token())
        {
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.UnknownKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.BigIntKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.ReadonlyKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.UniqueKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.UndefinedKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.ThisKeyword:
            case SyntaxKind.TypeOfKeyword:
            case SyntaxKind.NeverKeyword:
            case SyntaxKind.OpenBraceToken:
            case SyntaxKind.OpenBracketToken:
            case SyntaxKind.LessThanToken:
            case SyntaxKind.BarToken:
            case SyntaxKind.AmpersandToken:
            case SyntaxKind.NewKeyword:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.ObjectKeyword:
            case SyntaxKind.AsteriskToken:
            case SyntaxKind.QuestionToken:
            case SyntaxKind.ExclamationToken:
            case SyntaxKind.DotDotDotToken:
            case SyntaxKind.InferKeyword:
            case SyntaxKind.ImportKeyword:
            case SyntaxKind.AssertsKeyword:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateHead:
                return true;
            case SyntaxKind.FunctionKeyword:
                return !inStartOfParameter;
            case SyntaxKind.MinusToken:
                return !inStartOfParameter && LookAhead(NextTokenIsNumericOrBigIntLiteral);
            case SyntaxKind.OpenParenToken:
                // Only consider '(' the start of a type if followed by ')', '...', an identifier, a modifier,
                // or something that starts a type. We don't want to consider things like '(1)' a type.
                return !inStartOfParameter && LookAhead(IsStartOfParenthesizedOrFunctionType);
            default:
                return IsIdentifier();
        }
    }

    private bool IsStartOfParenthesizedOrFunctionType()
    {
        NextToken();
        return Token() == SyntaxKind.CloseParenToken || IsStartOfParameter(/*isJSDocParameter*/ false) || IsStartOfType();
    }

    private Node ParsePostfixTypeOrHigher()
    {
        var pos = GetNodePos();
        var type = ParseNonArrayType();
        while (!scanner.HasPrecedingLineBreak())
        {
            switch (Token())
            {
                case SyntaxKind.ExclamationToken:
                    NextToken();
                    type = FinishNode(factory.CreateJSDocNonNullableType(type, /*postfix*/ true), pos);
                    break;
                case SyntaxKind.QuestionToken:
                    // If next token is start of a type we have a conditional type
                    if (LookAhead(NextTokenIsStartOfType))
                    {
                        return type;
                    }
                    NextToken();
                    type = FinishNode(factory.CreateJSDocNullableType(type, /*postfix*/ true), pos);
                    break;
                case SyntaxKind.OpenBracketToken:
                    ParseExpected(SyntaxKind.OpenBracketToken);
                    if (IsStartOfType())
                    {
                        var indexType = ParseType();
                        ParseExpected(SyntaxKind.CloseBracketToken);
                        type = FinishNode(factory.CreateIndexedAccessTypeNode(type, indexType), pos);
                    }
                    else
                    {
                        ParseExpected(SyntaxKind.CloseBracketToken);
                        type = FinishNode(factory.CreateArrayTypeNode(type), pos);
                    }
                    break;
                default:
                    return type;
            }
        }
        return type;
    }

    private TypeOperatorNode ParseTypeOperator(SyntaxKind @operator)
    {
        var pos = GetNodePos();
        ParseExpected(@operator);
        return FinishNode(factory.CreateTypeOperatorNode(@operator, ParseTypeOperatorOrHigher()), pos);
    }

    private Node? TryParseConstraintOfInferType()
    {
        if (ParseOptional(SyntaxKind.ExtendsKeyword))
        {
            var constraint = DisallowConditionalTypesAnd(ParseType);
            if (InDisallowConditionalTypesContext() || Token() != SyntaxKind.QuestionToken)
            {
                return constraint;
            }
        }
        return null;
    }

    private TypeParameterDeclaration ParseTypeParameterOfInferType()
    {
        var pos = GetNodePos();
        var name = ParseIdentifier();
        var constraint = TryParse(TryParseConstraintOfInferType);
        var node = factory.CreateTypeParameterDeclaration(/*modifiers*/ null, name, constraint);
        return FinishNode(node, pos);
    }

    private InferTypeNode ParseInferType()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.InferKeyword);
        return FinishNode(factory.CreateInferTypeNode(ParseTypeParameterOfInferType()), pos);
    }

    private Node ParseTypeOperatorOrHigher()
    {
        var @operator = Token();
        switch (@operator)
        {
            case SyntaxKind.KeyOfKeyword:
            case SyntaxKind.UniqueKeyword:
            case SyntaxKind.ReadonlyKeyword:
                return ParseTypeOperator(@operator);
            case SyntaxKind.InferKeyword:
                return ParseInferType();
        }
        return AllowConditionalTypesAnd(ParsePostfixTypeOrHigher);
    }

    private Node? ParseFunctionOrConstructorTypeToError(bool isInUnionType)
    {
        // the function type and constructor type shorthand notation
        // are not allowed directly in unions and intersections, but we'll
        // try to parse them gracefully and issue a helpful message.
        if (IsStartOfFunctionTypeOrConstructorType())
        {
            var type = ParseFunctionOrConstructorType();
            DiagnosticMessage diagnostic;
            if (NodeTests.IsFunctionTypeNode(type))
            {
                diagnostic = isInUnionType
                    ? Diagnostics.Function_type_notation_must_be_parenthesized_when_used_in_a_union_type
                    : Diagnostics.Function_type_notation_must_be_parenthesized_when_used_in_an_intersection_type;
            }
            else
            {
                diagnostic = isInUnionType
                    ? Diagnostics.Constructor_type_notation_must_be_parenthesized_when_used_in_a_union_type
                    : Diagnostics.Constructor_type_notation_must_be_parenthesized_when_used_in_an_intersection_type;
            }
            ParseErrorAtRange(type, diagnostic);
            return type;
        }
        return null;
    }

    private Node ParseUnionOrIntersectionType(
        SyntaxKind @operator,
        Func<Node> parseConstituentType,
        Func<NodeArray, Node> createTypeNode)
    {
        var pos = GetNodePos();
        var isUnionType = @operator == SyntaxKind.BarToken;
        var hasLeadingOperator = ParseOptional(@operator);
        var type = (hasLeadingOperator ? ParseFunctionOrConstructorTypeToError(isUnionType) : null)
            ?? parseConstituentType();
        if (Token() == @operator || hasLeadingOperator)
        {
            var types = new List<Node> { type };
            while (ParseOptional(@operator))
            {
                types.Add(ParseFunctionOrConstructorTypeToError(isUnionType) ?? parseConstituentType());
            }
            type = FinishNode(createTypeNode(CreateNodeArray(types, pos)), pos);
        }
        return type;
    }

    private Node ParseIntersectionTypeOrHigher()
    {
        return ParseUnionOrIntersectionType(SyntaxKind.AmpersandToken, ParseTypeOperatorOrHigher, factory.CreateIntersectionTypeNode);
    }

    private Node ParseUnionTypeOrHigher()
    {
        return ParseUnionOrIntersectionType(SyntaxKind.BarToken, ParseIntersectionTypeOrHigher, factory.CreateUnionTypeNode);
    }

    private bool NextTokenIsNewKeyword()
    {
        NextToken();
        return Token() == SyntaxKind.NewKeyword;
    }

    private bool IsStartOfFunctionTypeOrConstructorType()
    {
        if (Token() == SyntaxKind.LessThanToken)
        {
            return true;
        }
        if (Token() == SyntaxKind.OpenParenToken && LookAhead(IsUnambiguouslyStartOfFunctionType))
        {
            return true;
        }
        return Token() == SyntaxKind.NewKeyword ||
            Token() == SyntaxKind.AbstractKeyword && LookAhead(NextTokenIsNewKeyword);
    }

    private bool SkipParameterStart()
    {
        if (UtilitiesPublic.IsModifierKind(Token()))
        {
            // Skip modifiers
            ParseModifiers(/*allowDecorators*/ false);
        }
        if (IsIdentifier() || Token() == SyntaxKind.ThisKeyword)
        {
            NextToken();
            return true;
        }
        if (Token() == SyntaxKind.OpenBracketToken || Token() == SyntaxKind.OpenBraceToken)
        {
            // Return true if we can parse an array or object binding pattern with no errors
            var previousErrorCount = parseDiagnostics.Count;
            ParseIdentifierOrPattern();
            return previousErrorCount == parseDiagnostics.Count;
        }
        return false;
    }

    private bool IsUnambiguouslyStartOfFunctionType()
    {
        NextToken();
        if (Token() == SyntaxKind.CloseParenToken || Token() == SyntaxKind.DotDotDotToken)
        {
            // ( )
            // ( ...
            return true;
        }
        if (SkipParameterStart())
        {
            // We successfully skipped modifiers (if any) and an identifier or binding pattern,
            // now see if we have something that indicates a parameter declaration
            if (
                Token() == SyntaxKind.ColonToken || Token() == SyntaxKind.CommaToken ||
                Token() == SyntaxKind.QuestionToken || Token() == SyntaxKind.EqualsToken)
            {
                // ( xxx :
                // ( xxx ,
                // ( xxx ?
                // ( xxx =
                return true;
            }
            if (Token() == SyntaxKind.CloseParenToken)
            {
                NextToken();
                if (Token() == SyntaxKind.EqualsGreaterThanToken)
                {
                    // ( xxx ) =>
                    return true;
                }
            }
        }
        return false;
    }

    private Node ParseTypeOrTypePredicate()
    {
        var pos = GetNodePos();
        var typePredicateVariable = IsIdentifier() ? TryParse(ParseTypePredicatePrefix) : null;
        var type = ParseType();
        if (typePredicateVariable != null)
        {
            return FinishNode(factory.CreateTypePredicateNode(/*assertsModifier*/ null, typePredicateVariable, type), pos);
        }
        else
        {
            return type;
        }
    }

    private Identifier? ParseTypePredicatePrefix()
    {
        var id = ParseIdentifier();
        if (Token() == SyntaxKind.IsKeyword && !scanner.HasPrecedingLineBreak())
        {
            NextToken();
            return id;
        }
        return null;
    }

    private Node ParseAssertsTypePredicate()
    {
        var pos = GetNodePos();
        var assertsModifier = ParseExpectedToken<AssertsKeyword>(SyntaxKind.AssertsKeyword);
        var parameterName = Token() == SyntaxKind.ThisKeyword ? (Node)ParseThisTypeNode() : ParseIdentifier();
        var type = ParseOptional(SyntaxKind.IsKeyword) ? ParseType() : null;
        return FinishNode(factory.CreateTypePredicateNode(assertsModifier, parameterName, type), pos);
    }

    private Node ParseType()
    {
        if ((contextFlags & NodeFlags.TypeExcludesFlags) != 0)
        {
            return DoOutsideOfContext(NodeFlags.TypeExcludesFlags, ParseType);
        }
        if (IsStartOfFunctionTypeOrConstructorType())
        {
            return ParseFunctionOrConstructorType();
        }
        var pos = GetNodePos();
        var type = ParseUnionTypeOrHigher();
        if (!InDisallowConditionalTypesContext() && !scanner.HasPrecedingLineBreak() && ParseOptional(SyntaxKind.ExtendsKeyword))
        {
            // The type following 'extends' is not permitted to be another conditional type
            var extendsType = DisallowConditionalTypesAnd(ParseType);
            ParseExpected(SyntaxKind.QuestionToken);
            var trueType = AllowConditionalTypesAnd(ParseType);
            ParseExpected(SyntaxKind.ColonToken);
            var falseType = AllowConditionalTypesAnd(ParseType);
            return FinishNode(factory.CreateConditionalTypeNode(type, extendsType, trueType, falseType), pos);
        }
        return type;
    }

    private Node? ParseTypeAnnotation()
    {
        return ParseOptional(SyntaxKind.ColonToken) ? ParseType() : null;
    }
}
