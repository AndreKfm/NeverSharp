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

/// <summary>parser.ts: `namespace Parser`, the expression grammar (the JSX part is in Parser.Jsx.cs).</summary>
internal sealed partial class Parser
{
    // EXPRESSIONS
    private bool IsStartOfLeftHandSideExpression()
    {
        switch (Token())
        {
            case SyntaxKind.ThisKeyword:
            case SyntaxKind.SuperKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateHead:
            case SyntaxKind.OpenParenToken:
            case SyntaxKind.OpenBracketToken:
            case SyntaxKind.OpenBraceToken:
            case SyntaxKind.FunctionKeyword:
            case SyntaxKind.ClassKeyword:
            case SyntaxKind.NewKeyword:
            case SyntaxKind.SlashToken:
            case SyntaxKind.SlashEqualsToken:
            case SyntaxKind.Identifier:
                return true;
            case SyntaxKind.ImportKeyword:
                return LookAhead(static p => p.NextTokenIsOpenParenOrLessThanOrDot());
            default:
                return IsIdentifier();
        }
    }

    private bool IsStartOfExpression()
    {
        if (IsStartOfLeftHandSideExpression())
        {
            return true;
        }

        switch (Token())
        {
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.TildeToken:
            case SyntaxKind.ExclamationToken:
            case SyntaxKind.DeleteKeyword:
            case SyntaxKind.TypeOfKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.PlusPlusToken:
            case SyntaxKind.MinusMinusToken:
            case SyntaxKind.LessThanToken:
            case SyntaxKind.AwaitKeyword:
            case SyntaxKind.YieldKeyword:
            case SyntaxKind.PrivateIdentifier:
            case SyntaxKind.AtToken:
                // Yield/await always starts an expression.  Either it is an identifier (in which case
                // it is definitely an expression).  Or it's a keyword (either because we're in
                // a generator or async function, or in strict mode (or both)) and it started a yield or await expression.
                return true;
            default:
                // Error tolerance.  If we see the start of some binary operator, we consider
                // that the start of an expression.  That way we'll parse out a missing identifier,
                // give a good message about an identifier being missing, and then consume the
                // rest of the binary expression.
                if (IsBinaryOperator())
                {
                    return true;
                }

                return IsIdentifier();
        }
    }

    private bool IsStartOfExpressionStatement()
    {
        // As per the grammar, none of '{' or 'function' or 'class' can start an expression statement.
        return Token() != SyntaxKind.OpenBraceToken &&
            Token() != SyntaxKind.FunctionKeyword &&
            Token() != SyntaxKind.ClassKeyword &&
            Token() != SyntaxKind.AtToken &&
            IsStartOfExpression();
    }

    private Node ParseExpression()
    {
        // Expression[in]:
        //      AssignmentExpression[in]
        //      Expression[in] , AssignmentExpression[in]

        // clear the decorator context when parsing Expression, as it should be unambiguous when parsing a decorator
        var saveDecoratorContext = InDecoratorContext();
        if (saveDecoratorContext)
        {
            SetDecoratorContext(/*val*/ false);
        }

        var pos = GetNodePos();
        var expr = ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);
        Node operatorToken;
        while (!(operatorToken = ParseOptionalToken(SyntaxKind.CommaToken)).IsNone)
        {
            expr = MakeBinaryExpression(expr, operatorToken, ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true), pos);
        }

        if (saveDecoratorContext)
        {
            SetDecoratorContext(/*val*/ true);
        }
        return expr;
    }

    private Node ParseInitializer()
    {
        return ParseOptional(SyntaxKind.EqualsToken) ? ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true) : default;
    }

    private Node ParseAssignmentExpressionOrHigher(bool allowReturnTypeInArrowFunction)
    {
        //  AssignmentExpression[in,yield]:
        //      1) ConditionalExpression[?in,?yield]
        //      2) LeftHandSideExpression = AssignmentExpression[?in,?yield]
        //      3) LeftHandSideExpression AssignmentOperator AssignmentExpression[?in,?yield]
        //      4) ArrowFunctionExpression[?in,?yield]
        //      5) AsyncArrowFunctionExpression[in,yield,await]
        //      6) [+Yield] YieldExpression[?In]
        //
        // Note: for ease of implementation we treat productions '2' and '3' as the same thing.
        // (i.e. they're both BinaryExpressions with an assignment operator in it).

        // First, do the simple check if we have a YieldExpression (production '6').
        if (IsYieldExpression())
        {
            return ParseYieldExpression();
        }

        // Then, check if we have an arrow function (production '4' and '5') that starts with a parenthesized
        // parameter list or is an async arrow function.
        // AsyncArrowFunctionExpression:
        //      1) async[no LineTerminator here]AsyncArrowBindingIdentifier[?Yield][no LineTerminator here]=>AsyncConciseBody[?In]
        //      2) CoverCallExpressionAndAsyncArrowHead[?Yield, ?Await][no LineTerminator here]=>AsyncConciseBody[?In]
        // Production (1) of AsyncArrowFunctionExpression is parsed in "tryParseAsyncSimpleArrowFunctionExpression".
        // And production (2) is parsed in "tryParseParenthesizedArrowFunctionExpression".
        //
        // If we do successfully parse arrow-function, we must *not* recurse for productions 1, 2 or 3. An ArrowFunction is
        // not a LeftHandSideExpression, nor does it start a ConditionalExpression.  So we are done
        // with AssignmentExpression if we see one.
        var arrowExpression = (TryParseParenthesizedArrowFunctionExpression(allowReturnTypeInArrowFunction) is { IsNone: false } __t1 ? __t1 : TryParseAsyncSimpleArrowFunctionExpression(allowReturnTypeInArrowFunction));
        if (!arrowExpression.IsNone)
        {
            return arrowExpression;
        }

        // Now try to see if we're in production '1', '2' or '3'.  A conditional expression can
        // start with a LogicalOrExpression, while the assignment productions can only start with
        // LeftHandSideExpressions.
        //
        // So, first, we try to just parse out a BinaryExpression.  If we get something that is a
        // LeftHandSide or higher, then we can try to parse out the assignment expression part.
        // Otherwise, we try to parse out the conditional expression bit.  We want to allow any
        // binary expression here, so we pass in the 'lowest' precedence here so that it matches
        // and consumes anything.
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var expr = ParseBinaryExpressionOrHigher(OperatorPrecedence.Lowest);

        // To avoid a look-ahead, we did not handle the case of an arrow function with a single un-parenthesized
        // parameter ('x => ...') above. We handle it here by checking if the parsed expression was a single
        // identifier and the current token is an arrow.
        if (store.Kind(expr) == SyntaxKind.Identifier && Token() == SyntaxKind.EqualsGreaterThanToken)
        {
            return ParseSimpleArrowFunctionExpression(pos, expr, allowReturnTypeInArrowFunction, hasJSDoc, /*asyncModifier*/ default);
        }

        // Now see if we might be in cases '2' or '3'.
        // If the expression was a LHS expression, and we have an assignment operator, then
        // we're in '2' or '3'. Consume the assignment and return.
        //
        // Note: we call reScanGreaterToken so that we get an appropriately merged token
        // for cases like `> > =` becoming `>>=`
        if (UtilitiesPublic.IsLeftHandSideExpression(store, expr) && Utilities.IsAssignmentOperator(ReScanGreaterToken()))
        {
            return MakeBinaryExpression(expr, ParseTokenNode(), ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction), pos);
        }

        // It wasn't an assignment or a lambda.  This is a conditional expression:
        return ParseConditionalExpressionRest(expr, pos, allowReturnTypeInArrowFunction);
    }

    private bool IsYieldExpression()
    {
        if (Token() == SyntaxKind.YieldKeyword)
        {
            // If we have a 'yield' keyword, and this is a context where yield expressions are
            // allowed, then definitely parse out a yield expression.
            if (InYieldContext())
            {
                return true;
            }

            // We're in a context where 'yield expr' is not allowed.  However, if we can
            // definitely tell that the user was trying to parse a 'yield expr' and not
            // just a normal expr that start with a 'yield' identifier, then parse out
            // a 'yield expr'.  We can then report an error later that they are only
            // allowed in generator expressions.
            //
            // for example, if we see 'yield(foo)', then we'll have to treat that as an
            // invocation expression of something called 'yield'.  However, if we have
            // 'yield foo' then that is not legal as a normal expression, so we can
            // definitely recognize this as a yield expression.
            //
            // for now we just check if the next token is an identifier.  More heuristics
            // can be added here later as necessary.  We just need to make sure that we
            // don't accidentally consume something legal.
            return LookAhead(static p => p.NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine());
        }

        return false;
    }

    private bool NextTokenIsIdentifierOnSameLine()
    {
        NextToken();
        return !scanner.HasPrecedingLineBreak() && IsIdentifier();
    }

    private Node ParseYieldExpression()
    {
        var pos = GetNodePos();

        // YieldExpression[In] :
        //      yield
        //      yield [no LineTerminator here] [Lexical goal InputElementRegExp]AssignmentExpression[?In, Yield]
        //      yield [no LineTerminator here] * [Lexical goal InputElementRegExp]AssignmentExpression[?In, Yield]
        NextToken();

        if (
            !scanner.HasPrecedingLineBreak() &&
            (Token() == SyntaxKind.AsteriskToken || IsStartOfExpression()))
        {
            return FinishNode(
                factory.CreateYieldExpression(
                    ParseOptionalToken(SyntaxKind.AsteriskToken),
                    ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true)),
                pos);
        }
        else
        {
            // if the next token is not on the same line as yield.  or we don't have an '*' or
            // the start of an expression, then this is just a simple "yield" expression.
            return FinishNode(factory.CreateYieldExpression(/*asteriskToken*/ default, /*expression*/ default), pos);
        }
    }

    private Node ParseSimpleArrowFunctionExpression(int pos, Node identifier, bool allowReturnTypeInArrowFunction, bool hasJSDoc, NodeList asyncModifier)
    {
        Debug.Assert(Token() == SyntaxKind.EqualsGreaterThanToken, "parseSimpleArrowFunctionExpression should only have been called if we had a =>");
        var parameter = factory.CreateParameterDeclaration(
            /*modifiers*/ default,
            /*dotDotDotToken*/ default,
            identifier,
            /*questionToken*/ default,
            /*type*/ default,
            /*initializer*/ default);
        FinishNode(parameter, store.Pos(identifier));

        var parameters = CreateNodeArray([parameter], store.Pos(parameter), store.End(parameter));
        var equalsGreaterThanToken = ParseExpectedToken(SyntaxKind.EqualsGreaterThanToken);
        var body = ParseArrowFunctionExpressionBody(/*isAsync*/ !asyncModifier.IsNone, allowReturnTypeInArrowFunction);
        var node = factory.CreateArrowFunction(asyncModifier, /*typeParameters*/ default, parameters, /*type*/ default, equalsGreaterThanToken, body);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private Node TryParseParenthesizedArrowFunctionExpression(bool allowReturnTypeInArrowFunction)
    {
        var triState = IsParenthesizedArrowFunctionExpression();
        if (triState == Tristate.False)
        {
            // It's definitely not a parenthesized arrow function expression.
            return default;
        }

        // If we definitely have an arrow function, then we can just parse one, not requiring a
        // following => or { token. Otherwise, we *might* have an arrow function.  Try to parse
        // it out, but don't allow any ambiguity, and return 'undefined' if this could be an
        // expression instead.
        if (triState == Tristate.True)
        {
            return ParseParenthesizedArrowFunctionExpression(/*allowAmbiguity*/ true, /*allowReturnTypeInArrowFunction*/ true);
        }
        // (C#: the closure captures a copy declared in this block, so it is allocated only when this branch runs.)
        {
            var allowReturnType = allowReturnTypeInArrowFunction;
            return TryParse(p => p.ParsePossibleParenthesizedArrowFunctionExpression(allowReturnType));
        }
    }

    //  True        -> We definitely expect a parenthesized arrow function here.
    //  False       -> There *cannot* be a parenthesized arrow function here.
    //  Unknown     -> There *might* be a parenthesized arrow function here.
    //                 Speculatively look ahead to be sure, and rollback if not.
    private Tristate IsParenthesizedArrowFunctionExpression()
    {
        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken || Token() == SyntaxKind.AsyncKeyword)
        {
            return LookAhead(static p => p.IsParenthesizedArrowFunctionExpressionWorker());
        }

        if (Token() == SyntaxKind.EqualsGreaterThanToken)
        {
            // ERROR RECOVERY TWEAK:
            // If we see a standalone => try to parse it as an arrow function expression as that's
            // likely what the user intended to write.
            return Tristate.True;
        }
        // Definitely not a parenthesized arrow function.
        return Tristate.False;
    }

    private Tristate IsParenthesizedArrowFunctionExpressionWorker()
    {
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            NextToken();
            if (scanner.HasPrecedingLineBreak())
            {
                return Tristate.False;
            }
            if (Token() != SyntaxKind.OpenParenToken && Token() != SyntaxKind.LessThanToken)
            {
                return Tristate.False;
            }
        }

        var first = Token();
        var second = NextToken();

        if (first == SyntaxKind.OpenParenToken)
        {
            if (second == SyntaxKind.CloseParenToken)
            {
                // Simple cases: "() =>", "(): ", and "() {".
                // This is an arrow function with no parameters.
                // The last one is not actually an arrow function,
                // but this is probably what the user intended.
                var third = NextToken();
                switch (third)
                {
                    case SyntaxKind.EqualsGreaterThanToken:
                    case SyntaxKind.ColonToken:
                    case SyntaxKind.OpenBraceToken:
                        return Tristate.True;
                    default:
                        return Tristate.False;
                }
            }

            // If encounter "([" or "({", this could be the start of a binding pattern.
            // Examples:
            //      ([ x ]) => { }
            //      ({ x }) => { }
            //      ([ x ])
            //      ({ x })
            if (second == SyntaxKind.OpenBracketToken || second == SyntaxKind.OpenBraceToken)
            {
                return Tristate.Unknown;
            }

            // Simple case: "(..."
            // This is an arrow function with a rest parameter.
            if (second == SyntaxKind.DotDotDotToken)
            {
                return Tristate.True;
            }

            // Check for "(xxx yyy", where xxx is a modifier and yyy is an identifier. This
            // isn't actually allowed, but we want to treat it as a lambda so we can provide
            // a good error message.
            if (UtilitiesPublic.IsModifierKind(second) && second != SyntaxKind.AsyncKeyword && LookAhead(static p => p.NextTokenIsIdentifier()))
            {
                if (NextToken() == SyntaxKind.AsKeyword)
                {
                    // https://github.com/microsoft/TypeScript/issues/44466
                    return Tristate.False;
                }
                return Tristate.True;
            }

            // If we had "(" followed by something that's not an identifier,
            // then this definitely doesn't look like a lambda.  "this" is not
            // valid, but we want to parse it and then give a semantic error.
            if (!IsIdentifier() && second != SyntaxKind.ThisKeyword)
            {
                return Tristate.False;
            }

            switch (NextToken())
            {
                case SyntaxKind.ColonToken:
                    // If we have something like "(a:", then we must have a
                    // type-annotated parameter in an arrow function expression.
                    return Tristate.True;
                case SyntaxKind.QuestionToken:
                    NextToken();
                    // If we have "(a?:" or "(a?," or "(a?=" or "(a?)" then it is definitely a lambda.
                    if (Token() == SyntaxKind.ColonToken || Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.EqualsToken || Token() == SyntaxKind.CloseParenToken)
                    {
                        return Tristate.True;
                    }
                    // Otherwise it is definitely not a lambda.
                    return Tristate.False;
                case SyntaxKind.CommaToken:
                case SyntaxKind.EqualsToken:
                case SyntaxKind.CloseParenToken:
                    // If we have "(a," or "(a=" or "(a)" this *could* be an arrow function
                    return Tristate.Unknown;
            }
            // It is definitely not an arrow function
            return Tristate.False;
        }
        else
        {
            Debug.Assert(first == SyntaxKind.LessThanToken);

            // If we have "<" not followed by an identifier,
            // then this definitely is not an arrow function.
            if (!IsIdentifier() && Token() != SyntaxKind.ConstKeyword)
            {
                return Tristate.False;
            }

            // JSX overrides
            if (languageVariant == LanguageVariant.JSX)
            {
                var isArrowFunctionInJsx = LookAhead(p =>
                {
                    ParseOptional(SyntaxKind.ConstKeyword);
                    var third = NextToken();
                    if (third == SyntaxKind.ExtendsKeyword)
                    {
                        var fourth = NextToken();
                        switch (fourth)
                        {
                            case SyntaxKind.EqualsToken:
                            case SyntaxKind.GreaterThanToken:
                            case SyntaxKind.SlashToken:
                                return false;
                            default:
                                return true;
                        }
                    }
                    else if (third == SyntaxKind.CommaToken || third == SyntaxKind.EqualsToken)
                    {
                        return true;
                    }
                    return false;
                });

                if (isArrowFunctionInJsx)
                {
                    return Tristate.True;
                }

                return Tristate.False;
            }

            // This *could* be a parenthesized arrow function.
            return Tristate.Unknown;
        }
    }

    private Node ParsePossibleParenthesizedArrowFunctionExpression(bool allowReturnTypeInArrowFunction)
    {
        var tokenPos = scanner.GetTokenStart();
        if (notParenthesizedArrow != null && notParenthesizedArrow.Contains(tokenPos))
        {
            return default;
        }

        var result = ParseParenthesizedArrowFunctionExpression(/*allowAmbiguity*/ false, allowReturnTypeInArrowFunction);
        if (result.IsNone)
        {
            (notParenthesizedArrow ??= []).Add(tokenPos);
        }

        return result;
    }

    private Node TryParseAsyncSimpleArrowFunctionExpression(bool allowReturnTypeInArrowFunction)
    {
        // We do a check here so that we won't be doing unnecessarily call to "lookAhead"
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            if (LookAhead(static p => p.IsUnParenthesizedAsyncArrowFunctionWorker()) == Tristate.True)
            {
                var pos = GetNodePos();
                var hasJSDoc = HasPrecedingJSDocComment();
                var asyncModifier = ParseModifiersForArrowFunction();
                var expr = ParseBinaryExpressionOrHigher(OperatorPrecedence.Lowest);
                return ParseSimpleArrowFunctionExpression(pos, expr, allowReturnTypeInArrowFunction, hasJSDoc, asyncModifier);
            }
        }
        return default;
    }

    private Tristate IsUnParenthesizedAsyncArrowFunctionWorker()
    {
        // AsyncArrowFunctionExpression:
        //      1) async[no LineTerminator here]AsyncArrowBindingIdentifier[?Yield][no LineTerminator here]=>AsyncConciseBody[?In]
        //      2) CoverCallExpressionAndAsyncArrowHead[?Yield, ?Await][no LineTerminator here]=>AsyncConciseBody[?In]
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            NextToken();
            // If the "async" is followed by "=>" token then it is not a beginning of an async arrow-function
            // but instead a simple arrow-function which will be parsed inside "parseAssignmentExpressionOrHigher"
            if (scanner.HasPrecedingLineBreak() || Token() == SyntaxKind.EqualsGreaterThanToken)
            {
                return Tristate.False;
            }
            // Check for un-parenthesized AsyncArrowFunction
            var expr = ParseBinaryExpressionOrHigher(OperatorPrecedence.Lowest);
            if (!scanner.HasPrecedingLineBreak() && store.Kind(expr) == SyntaxKind.Identifier && Token() == SyntaxKind.EqualsGreaterThanToken)
            {
                return Tristate.True;
            }
        }

        return Tristate.False;
    }

    private Node ParseParenthesizedArrowFunctionExpression(bool allowAmbiguity, bool allowReturnTypeInArrowFunction)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiersForArrowFunction();
        var isAsync = Core.Some(store.Nodes(modifiers), ((a0) => FactoryNodeTests.IsAsyncModifier(store, a0))) ? SignatureFlags.Await : SignatureFlags.None;
        // Arrow functions are never generators.
        //
        // If we're speculatively parsing a signature for a parenthesized arrow function, then
        // we have to have a complete parameter list.  Otherwise we might see something like
        // a => (b => c)
        // And think that "(b =>" was actually a parenthesized arrow function with a missing
        // close paren.
        var typeParameters = ParseTypeParameters();

        NodeList parameters;
        if (!ParseExpected(SyntaxKind.OpenParenToken))
        {
            if (!allowAmbiguity)
            {
                return default;
            }
            parameters = CreateMissingList();
        }
        else
        {
            if (!allowAmbiguity)
            {
                var maybeParameters = ParseParametersWorker(isAsync, allowAmbiguity);
                if (maybeParameters.IsNone)
                {
                    return default;
                }
                parameters = maybeParameters;
            }
            else
            {
                parameters = ParseParametersWorker(isAsync, allowAmbiguity)!;
            }
            if (!ParseExpected(SyntaxKind.CloseParenToken) && !allowAmbiguity)
            {
                return default;
            }
        }

        var hasReturnColon = Token() == SyntaxKind.ColonToken;
        var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
        if (!type.IsNone && !allowAmbiguity && TypeHasArrowFunctionBlockingParseError(store, type))
        {
            return default;
        }

        // Parsing a signature isn't enough.
        // Parenthesized arrow signatures often look like other valid expressions.
        // For instance:
        //  - "(x = 10)" is an assignment expression parsed as a signature with a default parameter value.
        //  - "(x,y)" is a comma expression parsed as a signature with two parameters.
        //  - "a ? (b): c" will have "(b):" parsed as a signature with a return type annotation.
        //  - "a ? (b): function() {}" will too, since function() is a valid JSDoc function type.
        //  - "a ? (b): (function() {})" as well, but inside of a parenthesized type with an arbitrary amount of nesting.
        //
        // So we need just a bit of lookahead to ensure that it can only be a signature.

        var unwrappedType = type;
        while (store.Kind(unwrappedType) == SyntaxKind.ParenthesizedType)
        {
            unwrappedType = store.Type(unwrappedType); // Skip parens if need be
        }

        var hasJSDocFunctionType = !unwrappedType.IsNone && FactoryNodeTests.IsJSDocFunctionType(store, unwrappedType);
        if (!allowAmbiguity && Token() != SyntaxKind.EqualsGreaterThanToken && (hasJSDocFunctionType || Token() != SyntaxKind.OpenBraceToken))
        {
            // Returning undefined here will cause our caller to rewind to where we started from.
            return default;
        }

        // If we have an arrow, then try to parse the body. Even if not, try to parse if we
        // have an opening brace, just in case we're in an error state.
        var lastToken = Token();
        var equalsGreaterThanToken = ParseExpectedToken(SyntaxKind.EqualsGreaterThanToken);
        var body = (lastToken == SyntaxKind.EqualsGreaterThanToken || lastToken == SyntaxKind.OpenBraceToken)
            ? ParseArrowFunctionExpressionBody(Core.Some(store.Nodes(modifiers), ((a0) => FactoryNodeTests.IsAsyncModifier(store, a0))), allowReturnTypeInArrowFunction)
            : ParseIdentifier();

        // Given:
        //     x ? y => ({ y }) : z => ({ z })
        // We try to parse the body of the first arrow function by looking at:
        //     ({ y }) : z => ({ z })
        // This is a valid arrow function with "z" as the return type.
        //
        // But, if we're in the true side of a conditional expression, this colon
        // terminates the expression, so we cannot allow a return type if we aren't
        // certain whether or not the preceding text was parsed as a parameter list.
        //
        // For example,
        //     a() ? (b: number, c?: string): void => d() : e
        // is determined by isParenthesizedArrowFunctionExpression to unambiguously
        // be an arrow expression, so we allow a return type.
        if (!allowReturnTypeInArrowFunction && hasReturnColon)
        {
            // However, if the arrow function we were able to parse is followed by another colon
            // as in:
            //     a ? (x): string => x : null
            // Then allow the arrow function, and treat the second colon as terminating
            // the conditional expression. It's okay to do this because this code would
            // be a syntax error in JavaScript (as the second colon shouldn't be there).
            if (Token() != SyntaxKind.ColonToken)
            {
                return default;
            }
        }

        var node = factory.CreateArrowFunction(modifiers, typeParameters, parameters, type, equalsGreaterThanToken, body);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private Node ParseArrowFunctionExpressionBody(bool isAsync, bool allowReturnTypeInArrowFunction)
    {
        if (Token() == SyntaxKind.OpenBraceToken)
        {
            return ParseFunctionBlock(isAsync ? SignatureFlags.Await : SignatureFlags.None);
        }

        if (
            Token() != SyntaxKind.SemicolonToken &&
            Token() != SyntaxKind.FunctionKeyword &&
            Token() != SyntaxKind.ClassKeyword &&
            IsStartOfStatement() &&
            !IsStartOfExpressionStatement())
        {
            // Check if we got a plain statement (i.e. no expression-statements, no function/class expressions/declarations)
            //
            // Here we try to recover from a potential error situation in the case where the
            // user meant to supply a block. For example, if the user wrote:
            //
            //  a =>
            //      let v = 0;
            //  }
            //
            // they may be missing an open brace.  Check to see if that's the case so we can
            // try to recover better.  If we don't do this, then the next close curly we see may end
            // up preemptively closing the containing construct.
            //
            // Note: even when 'IgnoreMissingOpenBrace' is passed, parseBody will still error.
            return ParseFunctionBlock(SignatureFlags.IgnoreMissingOpenBrace | (isAsync ? SignatureFlags.Await : SignatureFlags.None));
        }

        var savedYieldContext = InYieldContext();
        SetYieldContext(false);
        var savedTopLevel = topLevel;
        topLevel = false;
        var node = isAsync
            ? DoInAwaitContext<Node>(allowReturnTypeInArrowFunction ? static p => p.ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true) : static p => p.ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ false))
            : DoOutsideOfAwaitContext<Node>(allowReturnTypeInArrowFunction ? static p => p.ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true) : static p => p.ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ false));
        topLevel = savedTopLevel;
        SetYieldContext(savedYieldContext);
        return node;
    }

    private Node ParseConditionalExpressionRest(Node leftOperand, int pos, bool allowReturnTypeInArrowFunction)
    {
        // Note: we are passed in an expression which was produced from parseBinaryExpressionOrHigher.
        var questionToken = ParseOptionalToken(SyntaxKind.QuestionToken);
        if (questionToken.IsNone)
        {
            return leftOperand;
        }

        // Note: we explicitly 'allowIn' in the whenTrue part of the condition expression, and
        // we do not that for the 'whenFalse' part.
        Node colonToken;
        return FinishNode(
            factory.CreateConditionalExpression(
                leftOperand,
                questionToken,
                DoOutsideOfContext(DisallowInAndDecoratorContext, static p => p.ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ false)),
                colonToken = ParseExpectedToken(SyntaxKind.ColonToken),
                Utilities.NodeIsPresent(store, colonToken)
                    ? ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction)
                    : CreateMissingNode(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ false, Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.ColonToken))),
            pos);
    }

    private Node ParseBinaryExpressionOrHigher(OperatorPrecedence precedence)
    {
        var pos = GetNodePos();
        var leftOperand = ParseUnaryExpressionOrHigher();
        return ParseBinaryExpressionRest(precedence, leftOperand, pos);
    }

    private static bool IsInOrOfKeyword(SyntaxKind t)
    {
        return t == SyntaxKind.InKeyword || t == SyntaxKind.OfKeyword;
    }

    private Node ParseBinaryExpressionRest(OperatorPrecedence precedence, Node leftOperand, int pos)
    {
        while (true)
        {
            // We either have a binary operator here, or we're finished.  We call
            // reScanGreaterToken so that we merge token sequences like > and = into >=

            ReScanGreaterToken();
            var newPrecedence = Utilities.GetBinaryOperatorPrecedence(Token());

            // Check the precedence to see if we should "take" this operator
            // - For left associative operator (all operator but **), consume the operator,
            //   recursively call the function below, and parse binaryExpression as a rightOperand
            //   of the caller if the new precedence of the operator is greater then or equal to the current precedence.
            //   For example:
            //      a - b - c;
            //            ^token; leftOperand = b. Return b to the caller as a rightOperand
            //      a * b - c
            //            ^token; leftOperand = b. Return b to the caller as a rightOperand
            //      a - b * c;
            //            ^token; leftOperand = b. Return b * c to the caller as a rightOperand
            // - For right associative operator (**), consume the operator, recursively call the function
            //   and parse binaryExpression as a rightOperand of the caller if the new precedence of
            //   the operator is strictly grater than the current precedence
            //   For example:
            //      a ** b ** c;
            //             ^^token; leftOperand = b. Return b ** c to the caller as a rightOperand
            //      a - b ** c;
            //            ^^token; leftOperand = b. Return b ** c to the caller as a rightOperand
            //      a ** b - c
            //             ^token; leftOperand = b. Return b to the caller as a rightOperand
            var consumeCurrentOperator = Token() == SyntaxKind.AsteriskAsteriskToken ?
                newPrecedence >= precedence :
                newPrecedence > precedence;

            if (!consumeCurrentOperator)
            {
                break;
            }

            if (Token() == SyntaxKind.InKeyword && InDisallowInContext())
            {
                break;
            }

            if (Token() == SyntaxKind.AsKeyword || Token() == SyntaxKind.SatisfiesKeyword)
            {
                // Make sure we *do* perform ASI for constructs like this:
                //    var x = foo
                //    as (Bar)
                // This should be parsed as an initialized variable, followed
                // by a function call to 'as' with the argument 'Bar'
                if (scanner.HasPrecedingLineBreak())
                {
                    break;
                }
                else
                {
                    var keywordKind = Token();
                    NextToken();
                    leftOperand = keywordKind == SyntaxKind.SatisfiesKeyword ? MakeSatisfiesExpression(leftOperand, ParseType()) :
                        MakeAsExpression(leftOperand, ParseType());
                }
            }
            else
            {
                leftOperand = MakeBinaryExpression(leftOperand, ParseTokenNode(), ParseBinaryExpressionOrHigher(newPrecedence), pos);
            }
        }

        return leftOperand;
    }

    private bool IsBinaryOperator()
    {
        if (InDisallowInContext() && Token() == SyntaxKind.InKeyword)
        {
            return false;
        }

        return Utilities.GetBinaryOperatorPrecedence(Token()) > 0;
    }

    private Node MakeSatisfiesExpression(Node left, Node right)
    {
        return FinishNode(factory.CreateSatisfiesExpression(left, right), store.Pos(left));
    }

    private Node MakeBinaryExpression(Node left, Node operatorToken, Node right, int pos)
    {
        return FinishNode(factory.CreateBinaryExpression(left, operatorToken, right), pos);
    }

    private Node MakeAsExpression(Node left, Node right)
    {
        return FinishNode(factory.CreateAsExpression(left, right), store.Pos(left));
    }

    private Node ParsePrefixUnaryExpression()
    {
        var pos = GetNodePos();
        return FinishNode(factory.CreatePrefixUnaryExpression(Token(), NextTokenAnd(static p => p.ParseSimpleUnaryExpression())), pos);
    }

    private Node ParseDeleteExpression()
    {
        var pos = GetNodePos();
        return FinishNode(factory.CreateDeleteExpression(NextTokenAnd(static p => p.ParseSimpleUnaryExpression())), pos);
    }

    private Node ParseTypeOfExpression()
    {
        var pos = GetNodePos();
        return FinishNode(factory.CreateTypeOfExpression(NextTokenAnd(static p => p.ParseSimpleUnaryExpression())), pos);
    }

    private Node ParseVoidExpression()
    {
        var pos = GetNodePos();
        return FinishNode(factory.CreateVoidExpression(NextTokenAnd(static p => p.ParseSimpleUnaryExpression())), pos);
    }

    private bool IsAwaitExpression()
    {
        if (Token() == SyntaxKind.AwaitKeyword)
        {
            if (InAwaitContext())
            {
                return true;
            }

            // here we are using similar heuristics as 'isYieldExpression'
            return LookAhead(static p => p.NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine());
        }

        return false;
    }

    private Node ParseAwaitExpression()
    {
        var pos = GetNodePos();
        return FinishNode(factory.CreateAwaitExpression(NextTokenAnd(static p => p.ParseSimpleUnaryExpression())), pos);
    }

    /// <summary>
    /// Parse ES7 exponential expression and await expression
    ///
    /// ES7 ExponentiationExpression:
    ///      1) UnaryExpression[?Yield]
    ///      2) UpdateExpression[?Yield] ** ExponentiationExpression[?Yield]
    /// </summary>
    private Node ParseUnaryExpressionOrHigher()
    {
        /*
         * ES7 UpdateExpression:
         *      1) LeftHandSideExpression[?Yield]
         *      2) LeftHandSideExpression[?Yield][no LineTerminator here]++
         *      3) LeftHandSideExpression[?Yield][no LineTerminator here]--
         *      4) ++UnaryExpression[?Yield]
         *      5) --UnaryExpression[?Yield]
         */
        if (IsUpdateExpression())
        {
            var pos = GetNodePos();
            var updateExpression = ParseUpdateExpression();
            return Token() == SyntaxKind.AsteriskAsteriskToken ?
                ParseBinaryExpressionRest(Utilities.GetBinaryOperatorPrecedence(Token()), updateExpression, pos) :
                updateExpression;
        }

        /*
         * ES7 UnaryExpression:
         *      1) UpdateExpression[?yield]
         *      2) delete UpdateExpression[?yield]
         *      3) void UpdateExpression[?yield]
         *      4) typeof UpdateExpression[?yield]
         *      5) + UpdateExpression[?yield]
         *      6) - UpdateExpression[?yield]
         *      7) ~ UpdateExpression[?yield]
         *      8) ! UpdateExpression[?yield]
         */
        var unaryOperator = Token();
        var simpleUnaryExpression = ParseSimpleUnaryExpression();
        if (Token() == SyntaxKind.AsteriskAsteriskToken)
        {
            var pos = Scanner.SkipTrivia(sourceText, store.Pos(simpleUnaryExpression));
            var end = store.End(simpleUnaryExpression);
            if (store.Kind(simpleUnaryExpression) == SyntaxKind.TypeAssertionExpression)
            {
                ParseErrorAt(pos, end, Diagnostics.A_type_assertion_expression_is_not_allowed_in_the_left_hand_side_of_an_exponentiation_expression_Consider_enclosing_the_expression_in_parentheses);
            }
            else
            {
                Debug.Assert(Utilities.IsKeywordOrPunctuation(unaryOperator));
                ParseErrorAt(pos, end, Diagnostics.An_unary_expression_with_the_0_operator_is_not_allowed_in_the_left_hand_side_of_an_exponentiation_expression_Consider_enclosing_the_expression_in_parentheses, Scanner.TokenToString(unaryOperator));
            }
        }
        return simpleUnaryExpression;
    }

    /// <summary>
    /// Parse ES7 simple-unary expression or higher:
    ///
    /// ES7 UnaryExpression:
    ///      1) UpdateExpression[?yield]
    ///      2) delete UnaryExpression[?yield]
    ///      3) void UnaryExpression[?yield]
    ///      4) typeof UnaryExpression[?yield]
    ///      5) + UnaryExpression[?yield]
    ///      6) - UnaryExpression[?yield]
    ///      7) ~ UnaryExpression[?yield]
    ///      8) ! UnaryExpression[?yield]
    ///      9) [+Await] await UnaryExpression[?yield]
    /// </summary>
    private Node ParseSimpleUnaryExpression()
    {
        switch (Token())
        {
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.TildeToken:
            case SyntaxKind.ExclamationToken:
                return ParsePrefixUnaryExpression();
            case SyntaxKind.DeleteKeyword:
                return ParseDeleteExpression();
            case SyntaxKind.TypeOfKeyword:
                return ParseTypeOfExpression();
            case SyntaxKind.VoidKeyword:
                return ParseVoidExpression();
            case SyntaxKind.LessThanToken:
                // Just like in parseUpdateExpression, we need to avoid parsing type assertions when
                // in JSX and we see an expression like "+ <foo> bar".
                if (languageVariant == LanguageVariant.JSX)
                {
                    return ParseJsxElementOrSelfClosingElementOrFragment(/*inExpressionContext*/ true, /*topInvalidNodePosition*/ null, /*openingTag*/ default, /*mustBeUnary*/ true);
                }
                // This is modified UnaryExpression grammar in TypeScript
                //  UnaryExpression (modified):
                //      < type > UnaryExpression
                return ParseTypeAssertion();
            case SyntaxKind.AwaitKeyword:
                if (IsAwaitExpression())
                {
                    return ParseAwaitExpression();
                }
                // falls through
                return ParseUpdateExpression();
            default:
                return ParseUpdateExpression();
        }
    }

    /// <summary>
    /// Check if the current token can possibly be an ES7 increment expression.
    ///
    /// ES7 UpdateExpression:
    ///      LeftHandSideExpression[?Yield]
    ///      LeftHandSideExpression[?Yield][no LineTerminator here]++
    ///      LeftHandSideExpression[?Yield][no LineTerminator here]--
    ///      ++LeftHandSideExpression[?Yield]
    ///      --LeftHandSideExpression[?Yield]
    /// </summary>
    private bool IsUpdateExpression()
    {
        // This function is called inside parseUnaryExpression to decide
        // whether to call parseSimpleUnaryExpression or call parseUpdateExpression directly
        switch (Token())
        {
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.TildeToken:
            case SyntaxKind.ExclamationToken:
            case SyntaxKind.DeleteKeyword:
            case SyntaxKind.TypeOfKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.AwaitKeyword:
                return false;
            case SyntaxKind.LessThanToken:
                // If we are not in JSX context, we are parsing TypeAssertion which is an UnaryExpression
                if (languageVariant != LanguageVariant.JSX)
                {
                    return false;
                }
                // We are in JSX context and the token is part of JSXElement.
                // falls through
                return true;
            default:
                return true;
        }
    }

    /// <summary>
    /// Parse ES7 UpdateExpression. UpdateExpression is used instead of ES6's PostFixExpression.
    ///
    /// ES7 UpdateExpression[yield]:
    ///      1) LeftHandSideExpression[?yield]
    ///      2) LeftHandSideExpression[?yield] [[no LineTerminator here]]++
    ///      3) LeftHandSideExpression[?yield] [[no LineTerminator here]]--
    ///      4) ++LeftHandSideExpression[?yield]
    ///      5) --LeftHandSideExpression[?yield]
    /// In TypeScript (2), (3) are parsed as PostfixUnaryExpression. (4), (5) are parsed as PrefixUnaryExpression
    /// </summary>
    private Node ParseUpdateExpression()
    {
        if (Token() == SyntaxKind.PlusPlusToken || Token() == SyntaxKind.MinusMinusToken)
        {
            var pos = GetNodePos();
            return FinishNode(factory.CreatePrefixUnaryExpression(Token(), NextTokenAnd(static p => p.ParseLeftHandSideExpressionOrHigher())), pos);
        }
        else if (languageVariant == LanguageVariant.JSX && Token() == SyntaxKind.LessThanToken && LookAhead(static p => p.NextTokenIsIdentifierOrKeywordOrGreaterThan()))
        {
            // JSXElement is part of primaryExpression
            return ParseJsxElementOrSelfClosingElementOrFragment(/*inExpressionContext*/ true);
        }

        var expression = ParseLeftHandSideExpressionOrHigher();

        Debug.Assert(UtilitiesPublic.IsLeftHandSideExpression(store, expression));
        if ((Token() == SyntaxKind.PlusPlusToken || Token() == SyntaxKind.MinusMinusToken) && !scanner.HasPrecedingLineBreak())
        {
            var @operator = Token();
            NextToken();
            return FinishNode(factory.CreatePostfixUnaryExpression(expression, @operator), store.Pos(expression));
        }

        return expression;
    }

    private Node ParseLeftHandSideExpressionOrHigher()
    {
        // Original Ecma:
        // LeftHandSideExpression: See 11.2
        //      NewExpression
        //      CallExpression
        //
        // Our simplification:
        //
        // LeftHandSideExpression: See 11.2
        //      MemberExpression
        //      CallExpression
        //
        // See comment in parseMemberExpressionOrHigher on how we replaced NewExpression with
        // MemberExpression to make our lives easier.
        //
        // to best understand the below code, it's important to see how CallExpression expands
        // out into its own productions:
        //
        // CallExpression:
        //      MemberExpression Arguments
        //      CallExpression Arguments
        //      CallExpression[Expression]
        //      CallExpression.IdentifierName
        //      import (AssignmentExpression)
        //      super Arguments
        //      super.IdentifierName
        //
        // Because of the recursion in these calls, we need to bottom out first. There are three
        // bottom out states we can run into: 1) We see 'super' which must start either of
        // the last two CallExpression productions. 2) We see 'import' which must start import call.
        // 3)we have a MemberExpression which either completes the LeftHandSideExpression,
        // or starts the beginning of the first four CallExpression productions.
        var pos = GetNodePos();
        Node expression;
        if (Token() == SyntaxKind.ImportKeyword)
        {
            if (LookAhead(static p => p.NextTokenIsOpenParenOrLessThan()))
            {
                // We don't want to eagerly consume all import keyword as import call expression so we look ahead to find "("
                // For example:
                //      var foo3 = require("subfolder
                //      import * as foo1 from "module-from-node
                // We want this import to be a statement rather than import call expression
                sourceFlags |= NodeFlags.PossiblyContainsDynamicImport;
                expression = ParseTokenNode();
            }
            else if (LookAhead(static p => p.NextTokenIsDot()))
            {
                // This is an 'import.*' metaproperty (i.e. 'import.meta')
                NextToken(); // advance past the 'import'
                NextToken(); // advance past the dot
                expression = FinishNode(factory.CreateMetaProperty(SyntaxKind.ImportKeyword, ParseIdentifierName()), pos);

                if (store.Texts.Equals(store.EscapedText(store.Name(expression)), "defer"))
                {
                    if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
                    {
                        sourceFlags |= NodeFlags.PossiblyContainsDynamicImport;
                    }
                }
                else
                {
                    sourceFlags |= NodeFlags.PossiblyContainsImportMeta;
                }
            }
            else
            {
                expression = ParseMemberExpressionOrHigher();
            }
        }
        else
        {
            expression = Token() == SyntaxKind.SuperKeyword ? ParseSuperExpression() : ParseMemberExpressionOrHigher();
        }

        // Now, we *may* be complete.  However, we might have consumed the start of a
        // CallExpression or OptionalExpression.  As such, we need to consume the rest
        // of it here to be complete.
        return ParseCallExpressionRest(pos, expression);
    }

    private Node ParseMemberExpressionOrHigher()
    {
        // Note: to make our lives simpler, we decompose the NewExpression productions and
        // place ObjectCreationExpression and FunctionExpression into PrimaryExpression.
        // like so:
        //
        //   PrimaryExpression : See 11.1
        //      this
        //      Identifier
        //      Literal
        //      ArrayLiteral
        //      ObjectLiteral
        //      (Expression)
        //      FunctionExpression
        //      new MemberExpression Arguments?
        //
        //   MemberExpression : See 11.2
        //      PrimaryExpression
        //      MemberExpression[Expression]
        //      MemberExpression.IdentifierName
        //
        //   CallExpression : See 11.2
        //      MemberExpression
        //      CallExpression Arguments
        //      CallExpression[Expression]
        //      CallExpression.IdentifierName
        //
        // Technically this is ambiguous.  i.e. CallExpression defines:
        //
        //   CallExpression:
        //      CallExpression Arguments
        //
        // If you see: "new Foo()"
        //
        // Then that could be treated as a single ObjectCreationExpression, or it could be
        // treated as the invocation of "new Foo".  We disambiguate that in code (to match
        // the original grammar) by making sure that if we see an ObjectCreationExpression
        // we always consume arguments if they are there. So we treat "new Foo()" as an
        // object creation only, and not at all as an invocation.  Another way to think
        // about this is that for every "new" that we see, we will consume an argument list if
        // it is there as part of the *associated* object creation node.  Any additional
        // argument lists we see, will become invocation expressions.
        //
        // Because there are no other places in the grammar now that refer to FunctionExpression
        // or ObjectCreationExpression, it is safe to push down into the PrimaryExpression
        // production.
        //
        // Because CallExpression and MemberExpression are left recursive, we need to bottom out
        // of the recursion immediately.  So we parse out a primary expression to start with.
        var pos = GetNodePos();
        var expression = ParsePrimaryExpression();
        return ParseMemberExpressionRest(pos, expression, /*allowOptionalChain*/ true);
    }

    private Node ParseSuperExpression()
    {
        var pos = GetNodePos();
        var expression = ParseTokenNode();
        if (Token() == SyntaxKind.LessThanToken)
        {
            var startPos = GetNodePos();
            var typeArguments = TryParse(static p => p.ParseTypeArgumentsInExpression());
            if (!typeArguments.IsNone)
            {
                ParseErrorAt(startPos, GetNodePos(), Diagnostics.super_may_not_use_type_arguments);
                if (!IsTemplateStartOfTaggedTemplate())
                {
                    expression = factory.CreateExpressionWithTypeArguments(expression, typeArguments);
                }
            }
        }

        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.DotToken || Token() == SyntaxKind.OpenBracketToken)
        {
            return expression;
        }

        // If we have seen "super" it must be followed by '(' or '.'.
        // If it wasn't then just try to parse out a '.' and report an error.
        ParseExpectedToken(SyntaxKind.DotToken, Diagnostics.super_must_be_followed_by_an_argument_list_or_member_access);
        // private names will never work with `super` (`super.#foo`), but that's a semantic error, not syntactic
        return FinishNode(factory.CreatePropertyAccessExpression(expression, ParseRightSideOfDot(/*allowIdentifierNames*/ true, /*allowPrivateIdentifiers*/ true, /*allowUnicodeEscapeSequenceInIdentifierName*/ true)), pos);
    }

    private Node ParseTypeAssertion()
    {
        Debug.Assert(languageVariant != LanguageVariant.JSX, "Type assertions should never be parsed in JSX; they should be parsed as comparisons or JSX elements/fragments.");
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.LessThanToken);
        var type = ParseType();
        ParseExpected(SyntaxKind.GreaterThanToken);
        var expression = ParseSimpleUnaryExpression();
        return FinishNode(factory.CreateTypeAssertion(type, expression), pos);
    }

    private bool NextTokenIsIdentifierOrKeywordOrOpenBracketOrTemplate()
    {
        NextToken();
        return Scanner.TokenIsIdentifierOrKeyword(Token())
            || Token() == SyntaxKind.OpenBracketToken
            || IsTemplateStartOfTaggedTemplate();
    }

    private bool IsStartOfOptionalPropertyOrElementAccessChain()
    {
        return Token() == SyntaxKind.QuestionDotToken
            && LookAhead(static p => p.NextTokenIsIdentifierOrKeywordOrOpenBracketOrTemplate());
    }

    private static bool TryReparseOptionalChain(NodeStore store, Node node)
    {
        if ((store.Flags(node) & NodeFlags.OptionalChain) != 0)
        {
            return true;
        }
        // check for an optional chain in a non-null expression
        if (FactoryNodeTests.IsNonNullExpression(store, node))
        {
            var expr = store.Expression(node);
            while (FactoryNodeTests.IsNonNullExpression(store, expr) && (store.Flags(expr) & NodeFlags.OptionalChain) == 0)
            {
                expr = store.Expression(expr);
            }
            if ((store.Flags(expr) & NodeFlags.OptionalChain) != 0)
            {
                // this is part of an optional chain. Walk down from `node` to `expression` and set the flag.
                while (FactoryNodeTests.IsNonNullExpression(store, node))
                {
                    store.SetFlags(node, store.Flags(node) | NodeFlags.OptionalChain);
                    node = store.Expression(node);
                }
                return true;
            }
        }
        return false;
    }

    private Node ParsePropertyAccessExpressionRest(int pos, Node expression, Node questionDotToken)
    {
        var name = ParseRightSideOfDot(/*allowIdentifierNames*/ true, /*allowPrivateIdentifiers*/ true, /*allowUnicodeEscapeSequenceInIdentifierName*/ true);
        var isOptionalChain = !questionDotToken.IsNone || TryReparseOptionalChain(store, expression);
        var propertyAccess = isOptionalChain ?
            factory.CreatePropertyAccessChain(expression, questionDotToken, name) :
            factory.CreatePropertyAccessExpression(expression, name);
        if (isOptionalChain && FactoryNodeTests.IsPrivateIdentifier(store, store.Name(propertyAccess)))
        {
            ParseErrorAtRange(store.Name(propertyAccess), Diagnostics.An_optional_chain_cannot_contain_private_identifiers);
        }
        if (FactoryNodeTests.IsExpressionWithTypeArguments(store, expression) && !store.TypeArguments(expression).IsNone)
        {
            var typeArguments = store.TypeArguments(expression);
            var errorPos = store.Pos(typeArguments) - 1;
            var end = Scanner.SkipTrivia(sourceText, store.End(typeArguments)) + 1;
            ParseErrorAt(errorPos, end, Diagnostics.An_instantiation_expression_cannot_be_followed_by_a_property_access);
        }
        return FinishNode(propertyAccess, pos);
    }

    private Node ParseElementAccessExpressionRest(int pos, Node expression, Node questionDotToken)
    {
        Node argumentExpression;
        if (Token() == SyntaxKind.CloseBracketToken)
        {
            argumentExpression = CreateMissingNode(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Diagnostics.An_element_access_expression_should_take_an_argument);
        }
        else
        {
            var argument = AllowInAnd(static p => p.ParseExpression());
            if (Utilities.IsStringOrNumericLiteralLike(store, argument))
            {
                switch (argument)
                {
                    case var s when store.IsStringLiteral(s):
                        store.SetText(s, InternIdentifier(store.Text(s)));
                        break;
                    case var t when store.IsNoSubstitutionTemplateLiteral(t):
                        store.SetText(t, InternIdentifier(store.Text(t)));
                        break;
                    case var n when store.IsNumericLiteral(n):
                        store.SetText(n, InternIdentifier(store.Text(n)));
                        break;
                }
            }
            argumentExpression = argument;
        }

        ParseExpected(SyntaxKind.CloseBracketToken);

        var indexedAccess = !questionDotToken.IsNone || TryReparseOptionalChain(store, expression) ?
            factory.CreateElementAccessChain(expression, questionDotToken, argumentExpression) :
            factory.CreateElementAccessExpression(expression, argumentExpression);
        return FinishNode(indexedAccess, pos);
    }

    private Node ParseMemberExpressionRest(int pos, Node expression, bool allowOptionalChain)
    {
        while (true)
        {
            Node questionDotToken = default;
            var isPropertyAccess = false;
            if (allowOptionalChain && IsStartOfOptionalPropertyOrElementAccessChain())
            {
                questionDotToken = ParseExpectedToken(SyntaxKind.QuestionDotToken);
                isPropertyAccess = Scanner.TokenIsIdentifierOrKeyword(Token());
            }
            else
            {
                isPropertyAccess = ParseOptional(SyntaxKind.DotToken);
            }

            if (isPropertyAccess)
            {
                expression = ParsePropertyAccessExpressionRest(pos, expression, questionDotToken);
                continue;
            }

            // when in the [Decorator] context, we do not parse ElementAccess as it could be part of a ComputedPropertyName
            if ((!questionDotToken.IsNone || !InDecoratorContext()) && ParseOptional(SyntaxKind.OpenBracketToken))
            {
                expression = ParseElementAccessExpressionRest(pos, expression, questionDotToken);
                continue;
            }

            if (IsTemplateStartOfTaggedTemplate())
            {
                // Absorb type arguments into TemplateExpression when preceding expression is ExpressionWithTypeArguments
                expression = questionDotToken.IsNone && store.Kind(expression) == SyntaxKind.ExpressionWithTypeArguments ?
                    ParseTaggedTemplateRest(pos, store.Expression(expression), questionDotToken, store.TypeArguments(expression)) :
                    ParseTaggedTemplateRest(pos, expression, questionDotToken, /*typeArguments*/ default);
                continue;
            }

            if (questionDotToken.IsNone)
            {
                if (Token() == SyntaxKind.ExclamationToken && !scanner.HasPrecedingLineBreak())
                {
                    NextToken();
                    expression = FinishNode(factory.CreateNonNullExpression(expression), pos);
                    continue;
                }
                var typeArguments = TryParse(static p => p.ParseTypeArgumentsInExpression());
                if (!typeArguments.IsNone)
                {
                    expression = FinishNode(factory.CreateExpressionWithTypeArguments(expression, typeArguments), pos);
                    continue;
                }
            }

            return expression;
        }
    }

    private bool IsTemplateStartOfTaggedTemplate()
    {
        return Token() == SyntaxKind.NoSubstitutionTemplateLiteral || Token() == SyntaxKind.TemplateHead;
    }

    private Node ParseTaggedTemplateRest(int pos, Node tag, Node questionDotToken, NodeList typeArguments)
    {
        Node template;
        if (Token() == SyntaxKind.NoSubstitutionTemplateLiteral)
        {
            ReScanTemplateToken(/*isTaggedTemplate*/ true);
            template = ParseLiteralNode();
        }
        else
        {
            template = ParseTemplateExpression(/*isTaggedTemplate*/ true);
        }
        var tagExpression = factory.CreateTaggedTemplateExpression(
            tag,
            typeArguments,
            template);
        if (!questionDotToken.IsNone || (store.Flags(tag) & NodeFlags.OptionalChain) != 0)
        {
            store.SetFlags(tagExpression, store.Flags(tagExpression) | NodeFlags.OptionalChain);
        }
        store.SetQuestionDotToken(tagExpression, questionDotToken);
        return FinishNode(tagExpression, pos);
    }

    private Node ParseCallExpressionRest(int pos, Node expression)
    {
        while (true)
        {
            expression = ParseMemberExpressionRest(pos, expression, /*allowOptionalChain*/ true);
            NodeList typeArguments = default;
            var questionDotToken = ParseOptionalToken(SyntaxKind.QuestionDotToken);
            if (!questionDotToken.IsNone)
            {
                typeArguments = TryParse(static p => p.ParseTypeArgumentsInExpression());
                if (IsTemplateStartOfTaggedTemplate())
                {
                    expression = ParseTaggedTemplateRest(pos, expression, questionDotToken, typeArguments);
                    continue;
                }
            }
            if (!typeArguments.IsNone || Token() == SyntaxKind.OpenParenToken)
            {
                // Absorb type arguments into CallExpression when preceding expression is ExpressionWithTypeArguments
                if (questionDotToken.IsNone && store.Kind(expression) == SyntaxKind.ExpressionWithTypeArguments)
                {
                    typeArguments = store.TypeArguments(expression);
                    expression = store.Expression(expression);
                }
                var argumentList = ParseArgumentList();
                var callExpr = !questionDotToken.IsNone || TryReparseOptionalChain(store, expression) ?
                    factory.CreateCallChain(expression, questionDotToken, typeArguments, argumentList) :
                    factory.CreateCallExpression(expression, typeArguments, argumentList);
                expression = FinishNode(callExpr, pos);
                continue;
            }
            if (!questionDotToken.IsNone)
            {
                // We parsed `?.` but then failed to parse anything, so report a missing identifier here.
                var name = CreateMissingNode(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ false, Diagnostics.Identifier_expected);
                expression = FinishNode(factory.CreatePropertyAccessChain(expression, questionDotToken, name), pos);
            }
            break;
        }
        return expression;
    }

    private NodeList ParseArgumentList()
    {
        ParseExpected(SyntaxKind.OpenParenToken);
        var result = ParseDelimitedList(ParsingContext.ArgumentExpressions, static p => p.ParseArgumentExpression());
        ParseExpected(SyntaxKind.CloseParenToken);
        return result;
    }

    private NodeList ParseTypeArgumentsInExpression()
    {
        if ((contextFlags & NodeFlags.JavaScriptFile) != 0)
        {
            // TypeArguments must not be parsed in JavaScript files to avoid ambiguity with binary operators.
            return default;
        }

        if (ReScanLessThanToken() != SyntaxKind.LessThanToken)
        {
            return default;
        }
        NextToken();

        var typeArguments = ParseDelimitedList(ParsingContext.TypeArguments, static p => p.ParseType());
        if (ReScanGreaterToken() != SyntaxKind.GreaterThanToken)
        {
            // If it doesn't have the closing `>` then it's definitely not an type argument list.
            return default;
        }
        NextToken();

        // We successfully parsed a type argument list. The next token determines whether we want to
        // treat it as such. If the type argument list is followed by `(` or a template literal, as in
        // `f<number>(42)`, we favor the type argument interpretation even though JavaScript would view
        // it as a relational expression.
        return !typeArguments.IsNone && CanFollowTypeArgumentsInExpression() ? typeArguments : default;
    }

    private bool CanFollowTypeArgumentsInExpression()
    {
        switch (Token())
        {
            // These tokens can follow a type argument list in a call expression.
            case SyntaxKind.OpenParenToken: // foo<x>(
            case SyntaxKind.NoSubstitutionTemplateLiteral: // foo<T> `...`
            case SyntaxKind.TemplateHead: // foo<T> `...${100}...`
                return true;
            // A type argument list followed by `<` never makes sense, and a type argument list followed
            // by `>` is ambiguous with a (re-scanned) `>>` operator, so we disqualify both. Also, in
            // this context, `+` and `-` are unary operators, not binary operators.
            case SyntaxKind.LessThanToken:
            case SyntaxKind.GreaterThanToken:
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
                return false;
        }
        // We favor the type argument list interpretation when it is immediately followed by
        // a line break, a binary operator, or something that can't start an expression.
        return scanner.HasPrecedingLineBreak() || IsBinaryOperator() || !IsStartOfExpression();
    }

    private Node ParsePrimaryExpression()
    {
        switch (Token())
        {
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                if ((scanner.GetTokenFlags() & TokenFlags.IsInvalid) != 0)
                {
                    ReScanTemplateToken(/*isTaggedTemplate*/ false);
                }
                // falls through
                return ParseLiteralNode();
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.StringLiteral:
                return ParseLiteralNode();
            case SyntaxKind.ThisKeyword:
            case SyntaxKind.SuperKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
                return ParseTokenNode();
            case SyntaxKind.OpenParenToken:
                return ParseParenthesizedExpression();
            case SyntaxKind.OpenBracketToken:
                return ParseArrayLiteralExpression();
            case SyntaxKind.OpenBraceToken:
                return ParseObjectLiteralExpression();
            case SyntaxKind.AsyncKeyword:
                // Async arrow functions are parsed earlier in parseAssignmentExpressionOrHigher.
                // If we encounter `async [no LineTerminator here] function` then this is an async
                // function; otherwise, its an identifier.
                if (!LookAhead(static p => p.NextTokenIsFunctionKeywordOnSameLine()))
                {
                    break;
                }

                return ParseFunctionExpression();
            case SyntaxKind.AtToken:
                return ParseDecoratedExpression();
            case SyntaxKind.ClassKeyword:
                return ParseClassExpression();
            case SyntaxKind.FunctionKeyword:
                return ParseFunctionExpression();
            case SyntaxKind.NewKeyword:
                return ParseNewExpressionOrNewDotTarget();
            case SyntaxKind.SlashToken:
            case SyntaxKind.SlashEqualsToken:
                if (ReScanSlashToken() == SyntaxKind.RegularExpressionLiteral)
                {
                    return ParseLiteralNode();
                }
                break;
            case SyntaxKind.TemplateHead:
                return ParseTemplateExpression(/*isTaggedTemplate*/ false);
            case SyntaxKind.PrivateIdentifier:
                return ParsePrivateIdentifier();
        }

        return ParseIdentifier(Diagnostics.Expression_expected);
    }

    private Node ParseParenthesizedExpression()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(static p => p.ParseExpression());
        ParseExpected(SyntaxKind.CloseParenToken);
        return WithJSDoc(FinishNode(factory.CreateParenthesizedExpression(expression), pos), hasJSDoc);
    }

    private Node ParseSpreadElement()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.DotDotDotToken);
        var expression = ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);
        return FinishNode(factory.CreateSpreadElement(expression), pos);
    }

    private Node ParseArgumentOrArrayLiteralElement()
    {
        return Token() == SyntaxKind.DotDotDotToken ? ParseSpreadElement() :
            Token() == SyntaxKind.CommaToken ? FinishNode(factory.CreateOmittedExpression(), GetNodePos()) :
            ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);
    }

    private Node ParseArgumentExpression()
    {
        return DoOutsideOfContext(DisallowInAndDecoratorContext, static p => p.ParseArgumentOrArrayLiteralElement());
    }

    private Node ParseArrayLiteralExpression()
    {
        var pos = GetNodePos();
        var openBracketPosition = scanner.GetTokenStart();
        var openBracketParsed = ParseExpected(SyntaxKind.OpenBracketToken);
        var multiLine = scanner.HasPrecedingLineBreak();
        var elements = ParseDelimitedList(ParsingContext.ArrayLiteralMembers, static p => p.ParseArgumentOrArrayLiteralElement());
        ParseExpectedMatchingBrackets(SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken, openBracketParsed, openBracketPosition);
        return FinishNode(factory.CreateArrayLiteralExpression(elements, multiLine), pos);
    }

    private Node ParseObjectLiteralElement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();

        if (!ParseOptionalToken(SyntaxKind.DotDotDotToken).IsNone)
        {
            var expression = ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);
            return WithJSDoc(FinishNode(factory.CreateSpreadAssignment(expression), pos), hasJSDoc);
        }

        var modifiers = ParseModifiers(/*allowDecorators*/ true);
        if (ParseContextualModifier(SyntaxKind.GetKeyword))
        {
            return ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.GetAccessor, SignatureFlags.None);
        }
        if (ParseContextualModifier(SyntaxKind.SetKeyword))
        {
            return ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.SetAccessor, SignatureFlags.None);
        }

        var asteriskToken = ParseOptionalToken(SyntaxKind.AsteriskToken);
        var tokenIsIdentifier = IsIdentifier();
        var name = ParsePropertyName();

        // Disallowing of optional property assignments and definite assignment assertion happens in the grammar checker.
        var questionToken = ParseOptionalToken(SyntaxKind.QuestionToken);
        var exclamationToken = ParseOptionalToken(SyntaxKind.ExclamationToken);

        if (!asteriskToken.IsNone || Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            return ParseMethodDeclaration(pos, hasJSDoc, modifiers, asteriskToken, name, questionToken, exclamationToken);
        }

        // check if it is short-hand property assignment or normal property assignment
        // NOTE: if token is EqualsToken it is interpreted as CoverInitializedName production
        // CoverInitializedName[Yield] :
        //     IdentifierReference[?Yield] Initializer[In, ?Yield]
        // this is necessary because ObjectLiteral productions are also used to cover grammar for ObjectAssignmentPattern
        Node node;
        var isShorthandPropertyAssignment = tokenIsIdentifier && (Token() != SyntaxKind.ColonToken);
        if (isShorthandPropertyAssignment)
        {
            var equalsToken = ParseOptionalToken(SyntaxKind.EqualsToken);
            var objectAssignmentInitializer = !equalsToken.IsNone ? AllowInAnd(static p => p.ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true)) : default;
            var shorthand = factory.CreateShorthandPropertyAssignment(name, objectAssignmentInitializer);
            // Save equals token for error reporting.
            // TODO(rbuckton): Consider manufacturing this when we need to report an error as it is otherwise not useful.
            store.SetEqualsToken(shorthand, equalsToken);
            // Decorators, Modifiers, questionToken, and exclamationToken are not supported by property assignments and are reported in the grammar checker
            store.SetModifiers(shorthand, modifiers);
            store.SetQuestionToken(shorthand, questionToken);
            store.SetExclamationToken(shorthand, exclamationToken);
            node = shorthand;
        }
        else
        {
            ParseExpected(SyntaxKind.ColonToken);
            var initializer = AllowInAnd(static p => p.ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true));
            var assignment = factory.CreatePropertyAssignment(name, initializer);
            // Decorators, Modifiers, questionToken, and exclamationToken are not supported by property assignments and are reported in the grammar checker
            store.SetModifiers(assignment, modifiers);
            store.SetQuestionToken(assignment, questionToken);
            store.SetExclamationToken(assignment, exclamationToken);
            node = assignment;
        }
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private Node ParseObjectLiteralExpression()
    {
        var pos = GetNodePos();
        var openBracePosition = scanner.GetTokenStart();
        var openBraceParsed = ParseExpected(SyntaxKind.OpenBraceToken);
        var multiLine = scanner.HasPrecedingLineBreak();
        var properties = ParseDelimitedList(ParsingContext.ObjectLiteralMembers, static p => p.ParseObjectLiteralElement(), /*considerSemicolonAsDelimiter*/ true);
        ParseExpectedMatchingBrackets(SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken, openBraceParsed, openBracePosition);
        return FinishNode(factory.CreateObjectLiteralExpression(properties, multiLine), pos);
    }

    private Node ParseFunctionExpression()
    {
        // GeneratorExpression:
        //      function* BindingIdentifier [Yield][opt](FormalParameters[Yield]){ GeneratorBody }
        //
        // FunctionExpression:
        //      function BindingIdentifier[opt](FormalParameters){ FunctionBody }
        var savedDecoratorContext = InDecoratorContext();
        SetDecoratorContext(/*val*/ false);

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(/*allowDecorators*/ false);
        ParseExpected(SyntaxKind.FunctionKeyword);
        var asteriskToken = ParseOptionalToken(SyntaxKind.AsteriskToken);
        var isGenerator = !asteriskToken.IsNone ? SignatureFlags.Yield : SignatureFlags.None;
        var isAsync = Core.Some(store.Nodes(modifiers), ((a0) => FactoryNodeTests.IsAsyncModifier(store, a0))) ? SignatureFlags.Await : SignatureFlags.None;
        var name = isGenerator != 0 && isAsync != 0 ? DoInYieldAndAwaitContext(static p => p.ParseOptionalBindingIdentifier()) :
            isGenerator != 0 ? DoInYieldContext(static p => p.ParseOptionalBindingIdentifier()) :
            isAsync != 0 ? DoInAwaitContext(static p => p.ParseOptionalBindingIdentifier()) :
            ParseOptionalBindingIdentifier();

        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(isGenerator | isAsync);
        var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
        var body = ParseFunctionBlock(isGenerator | isAsync);

        SetDecoratorContext(savedDecoratorContext);

        var node = factory.CreateFunctionExpression(modifiers, asteriskToken, name, typeParameters, parameters, type, body);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private Node ParseOptionalBindingIdentifier()
    {
        return IsBindingIdentifier() ? ParseBindingIdentifier() : default;
    }

    private Node ParseNewExpressionOrNewDotTarget()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.NewKeyword);
        if (ParseOptional(SyntaxKind.DotToken))
        {
            var name = ParseIdentifierName();
            return FinishNode(factory.CreateMetaProperty(SyntaxKind.NewKeyword, name), pos);
        }
        var expressionPos = GetNodePos();
        var expression = ParseMemberExpressionRest(expressionPos, ParsePrimaryExpression(), /*allowOptionalChain*/ false);
        NodeList typeArguments = default;
        // Absorb type arguments into NewExpression when preceding expression is ExpressionWithTypeArguments
        if (store.Kind(expression) == SyntaxKind.ExpressionWithTypeArguments)
        {
            typeArguments = store.TypeArguments(expression);
            expression = store.Expression(expression);
        }
        if (Token() == SyntaxKind.QuestionDotToken)
        {
            ParseErrorAtCurrentToken(Diagnostics.Invalid_optional_chain_from_new_expression_Did_you_mean_to_call_0, Utilities.GetTextOfNodeFromSourceText(store, sourceText, expression));
        }
        var argumentList = Token() == SyntaxKind.OpenParenToken ? ParseArgumentList() : default;
        return FinishNode(factory.CreateNewExpression(expression, typeArguments, argumentList), pos);
    }
}
