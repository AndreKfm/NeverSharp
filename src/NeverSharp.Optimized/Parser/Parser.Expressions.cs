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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
                return LookAhead(new F.NextTokenIsOpenParenOrLessThanOrDot());
            default:
                return IsIdentifier();
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsStartOfExpressionStatement()
    {
        // As per the grammar, none of '{' or 'function' or 'class' can start an expression statement.
        return Token() != SyntaxKind.OpenBraceToken &&
            Token() != SyntaxKind.FunctionKeyword &&
            Token() != SyntaxKind.ClassKeyword &&
            Token() != SyntaxKind.AtToken &&
            IsStartOfExpression();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseInitializer()
    {
        return ParseOptional(SyntaxKind.EqualsToken) ? ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true) : default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

        // Track O (step O25): a token that only ParsePrimaryExpression acts on (IsPlainPrimaryStart) takes the descent
        // below without any level acting before the primary expression: no yield expression, arrow function or async
        // arrow function check fires, and every level down to ParsePrimaryExpression takes its fall-through path with
        // the same node position (no token is consumed in between). So the primary expression is parsed directly; if
        // the token after it continues no level (ContinuesOperand), every level above returns it unchanged, without
        // side effects, and so does this function. Otherwise the levels above the primary expression run as before
        // (ParseUnaryOperandRest, ParseBinaryExpressionRest, ParseAssignmentExpressionRest).
        if (IsPlainPrimaryStart(Token()))
        {
            var primaryPos = GetNodePos();
            var primaryHasJSDoc = HasPrecedingJSDocComment();
            var primary = ParsePlainPrimaryExpression();
            if (!ContinuesOperand(Token()))
            {
                return primary;
            }
            var operand = ParseUnaryOperandRest(primaryPos, primary);
            if (!ContinuesOperand(Token()))
            {
                return operand;
            }
            operand = ParseBinaryExpressionRest(OperatorPrecedence.Lowest, operand, primaryPos);
            return ParseAssignmentExpressionRest(operand, primaryPos, primaryHasJSDoc, allowReturnTypeInArrowFunction);
        }

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
        return ParseAssignmentExpressionRest(expr, pos, hasJSDoc, allowReturnTypeInArrowFunction);
    }

    // The part of parseAssignmentExpressionOrHigher after its parseBinaryExpressionOrHigher (Track O, step O25: shared
    // with the path for a plain primary expression above).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseAssignmentExpressionRest(Node expr, int pos, bool hasJSDoc, bool allowReturnTypeInArrowFunction)
    {
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
            return LookAhead(new F.NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine());
        }

        return false;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool NextTokenIsIdentifierOnSameLine()
    {
        NextToken();
        return !scanner.HasPrecedingLineBreak() && IsIdentifier();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
        return TryParse(new F.ParsePossibleParenthesizedArrowFunctionExpression(allowReturnTypeInArrowFunction));
    }

    // The callback of lookAhead in isParenthesizedArrowFunctionExpressionWorker (JSX overrides).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsArrowFunctionInJsx()
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
    }

    //  True        -> We definitely expect a parenthesized arrow function here.
    //  False       -> There *cannot* be a parenthesized arrow function here.
    //  Unknown     -> There *might* be a parenthesized arrow function here.
    //                 Speculatively look ahead to be sure, and rollback if not.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Tristate IsParenthesizedArrowFunctionExpression()
    {
        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken || Token() == SyntaxKind.AsyncKeyword)
        {
            return LookAhead<F.IsParenthesizedArrowFunctionExpressionWorker, Tristate>(new F.IsParenthesizedArrowFunctionExpressionWorker());
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
            if (UtilitiesPublic.IsModifierKind(second) && second != SyntaxKind.AsyncKeyword && LookAhead(new F.NextTokenIsIdentifier()))
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
                var isArrowFunctionInJsx = LookAhead(new F.IsArrowFunctionInJsx());

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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
            AddNotParenthesizedArrow(tokenPos);
        }

        return result;
    }

    // `(notParenthesizedArrow ||= new Set()).add(tokenPos)`.
    [NoAllocUnchecked("arena growth: the parse's set of positions that do not start an arrow function")]
    private void AddNotParenthesizedArrow(int tokenPos)
    {
        (notParenthesizedArrow ??= []).Add(tokenPos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node TryParseAsyncSimpleArrowFunctionExpression(bool allowReturnTypeInArrowFunction)
    {
        // We do a check here so that we won't be doing unnecessarily call to "lookAhead"
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            if (LookAhead<F.IsUnParenthesizedAsyncArrowFunctionWorker, Tristate>(new F.IsUnParenthesizedAsyncArrowFunctionWorker()) == Tristate.True)
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseParenthesizedArrowFunctionExpression(bool allowAmbiguity, bool allowReturnTypeInArrowFunction)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiersForArrowFunction();
        var isAsync = ModifiersContain(modifiers, SyntaxKind.AsyncKeyword) ? SignatureFlags.Await : SignatureFlags.None;
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
            ? ParseArrowFunctionExpressionBody(ModifiersContain(modifiers, SyntaxKind.AsyncKeyword), allowReturnTypeInArrowFunction)
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
            ? DoInAwaitContext<F.ParseAssignmentExpressionOrHigher, Node>(new F.ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction))
            : DoOutsideOfAwaitContext<F.ParseAssignmentExpressionOrHigher, Node>(new F.ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction));
        topLevel = savedTopLevel;
        SetYieldContext(savedYieldContext);
        return node;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
                DoOutsideOfContext(DisallowInAndDecoratorContext, new F.ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ false)),
                colonToken = ParseExpectedToken(SyntaxKind.ColonToken),
                Utilities.NodeIsPresent(store, colonToken)
                    ? ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction)
                    : CreateMissingNode(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ false, Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.ColonToken))),
            pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseBinaryExpressionOrHigher(OperatorPrecedence precedence)
    {
        var pos = GetNodePos();

        // Track O (step O25): for a token that only ParsePrimaryExpression acts on, parseUnaryExpressionOrHigher takes
        // its update-expression branch, parseUpdateExpression and parseLeftHandSideExpressionOrHigher their fall-through
        // paths down to parseMemberExpressionOrHigher's parsePrimaryExpression, all at this position (see
        // ParseAssignmentExpressionOrHigher). If the token after the primary expression continues no level, the levels
        // above it and parseBinaryExpressionRest return it unchanged (its precedence is -1, below every precedence).
        if (IsPlainPrimaryStart(Token()))
        {
            var primary = ParsePlainPrimaryExpression();
            if (!ContinuesOperand(Token()))
            {
                return primary;
            }
            var operand = ParseUnaryOperandRest(pos, primary);
            return ContinuesOperand(Token()) ? ParseBinaryExpressionRest(precedence, operand, pos) : operand;
        }

        var leftOperand = ParseUnaryExpressionOrHigher();
        return ParseBinaryExpressionRest(precedence, leftOperand, pos);
    }

    /// <summary>
    /// Track O (step O25): the tokens with which parseAssignmentExpressionOrHigher and parseBinaryExpressionOrHigher
    /// reach parsePrimaryExpression with no level acting before it. None of them is `yield`, `(`, `&lt;`, `async`,
    /// `=&gt;` (the yield and arrow function checks of parseAssignmentExpressionOrHigher), a prefix operator or `await`
    /// (isUpdateExpression is true for all of them), `++`, `--` (parseUpdateExpression), `import` or `super`
    /// (parseLeftHandSideExpressionOrHigher); parsePrimaryExpression handles each of them itself.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsPlainPrimaryStart(SyntaxKind token)
    {
        switch (token)
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.ThisKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateHead:
            case SyntaxKind.OpenBracketToken:
            case SyntaxKind.OpenBraceToken:
            case SyntaxKind.NewKeyword:
            case SyntaxKind.FunctionKeyword:
            case SyntaxKind.ClassKeyword:
            case SyntaxKind.PrivateIdentifier:
                return true;
            case SyntaxKind.YieldKeyword:
            case SyntaxKind.AsyncKeyword:
            case SyntaxKind.AwaitKeyword:
                return false;
            default:
                // The other future reserved words and contextual keywords: parsePrimaryExpression parses them as
                // identifiers (its default case), and no level above acts on them.
                return token > SyntaxKind.LastReservedWord && token <= SyntaxKind.LastKeyword;
        }
    }

    /// <summary>
    /// parsePrimaryExpression for a token of <see cref="IsPlainPrimaryStart"/> (Track O, step O25): an identifier token
    /// takes its default case, `parseIdentifier(Diagnostics.Expression_expected)`, which creates the identifier
    /// (isIdentifier is true for it, so the message is not used).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParsePlainPrimaryExpression()
    {
        return Token() == SyntaxKind.Identifier ? CreateIdentifier(/*isIdentifier*/ true) : ParsePrimaryExpression();
    }

    /// <summary>
    /// Track O (step O25): whether the current token, after an operand, can make any level between
    /// parseMemberExpressionOrHigher and parseAssignmentExpressionOrHigher act (<see cref="ExpressionTokens"/>). For
    /// any other token each of those levels only compares the token and returns the operand unchanged.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool ContinuesOperand(SyntaxKind token)
    {
        var table = ExpressionTokens.ContinuesOperand;
        return (uint)token >= (uint)table.Length || table[(int)token];
    }

    /// <summary>
    /// Track O (step O25): the tokens on which a branch of parseMemberExpressionRest acts; each branch first requires
    /// one of them (`?.` for an optional chain, `.`, `[`, a template for a tagged template, `!` for a non-null
    /// expression, `&lt;` or `&lt;&lt;` for type arguments: tryParse(parseTypeArgumentsInExpression) decides on any
    /// other token without changing anything, see TryParseTypeArgumentsInExpression).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static OperatorPrecedence BinaryOperatorPrecedence(SyntaxKind token)
    {
        // Track O (step O25): getBinaryOperatorPrecedence from a table computed with it.
        var table = ExpressionTokens.BinaryOperatorPrecedence;
        return (uint)token < (uint)table.Length ? (OperatorPrecedence)table[(int)token] : Utilities.GetBinaryOperatorPrecedence(token);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsMemberExpressionContinuation(SyntaxKind token)
    {
        switch (token)
        {
            case SyntaxKind.QuestionDotToken:
            case SyntaxKind.DotToken:
            case SyntaxKind.OpenBracketToken:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateHead:
            case SyntaxKind.ExclamationToken:
            case SyntaxKind.LessThanToken:
            case SyntaxKind.LessThanLessThanToken:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Track O (step O25): token tables of the expression descent, computed once from the parser's predicates.</summary>
    private static class ExpressionTokens
    {
        /// <summary>
        /// Per token kind: true if the token, after an operand, continues it at some level: a member expression
        /// (<see cref="IsMemberExpressionContinuation"/>), a call (`(`; `?.` is included above), a postfix `++`/`--`,
        /// an exponentiation (`**`), any binary operator (including `as`, `satisfies`, `in`) or a `&gt;` that
        /// reScanGreaterToken may merge, an arrow (`=&gt;`), an assignment operator, or a conditional (`?`).
        /// </summary>
        internal static readonly bool[] ContinuesOperand = CreateContinuesOperand();

        /// <summary>Per token kind: getBinaryOperatorPrecedence of the kind.</summary>
        internal static readonly sbyte[] BinaryOperatorPrecedence = CreateBinaryOperatorPrecedence();

        private static sbyte[] CreateBinaryOperatorPrecedence()
        {
            var table = new sbyte[(int)SyntaxKind.Count];
            for (var k = 0; k < table.Length; k++)
            {
                table[k] = (sbyte)Utilities.GetBinaryOperatorPrecedence((SyntaxKind)k);
            }
            return table;
        }

        private static bool[] CreateContinuesOperand()
        {
            var table = new bool[(int)SyntaxKind.Count];
            for (var k = 0; k < table.Length; k++)
            {
                var kind = (SyntaxKind)k;
                table[k] = IsMemberExpressionContinuation(kind) ||
                    kind == SyntaxKind.OpenParenToken ||
                    kind == SyntaxKind.PlusPlusToken ||
                    kind == SyntaxKind.MinusMinusToken ||
                    kind == SyntaxKind.AsteriskAsteriskToken ||
                    kind == SyntaxKind.GreaterThanToken ||
                    Utilities.GetBinaryOperatorPrecedence(kind) > OperatorPrecedence.Invalid ||
                    kind == SyntaxKind.EqualsGreaterThanToken ||
                    Utilities.IsAssignmentOperator(kind) ||
                    kind == SyntaxKind.QuestionToken;
            }
            return table;
        }
    }

    /// <summary>
    /// Track O (step O25): the levels above a primary expression parsed by parseMemberExpressionOrHigher, as
    /// parseUnaryExpressionOrHigher's update-expression branch completes it: the rest of parseMemberExpressionOrHigher
    /// and of parseLeftHandSideExpressionOrHigher, the postfix operator of parseUpdateExpression, and an
    /// exponentiation. All at the primary expression's position, which is the position of every one of those levels.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseUnaryOperandRest(int pos, Node primary)
    {
        // (Track O, step O28: parseMemberExpressionRest returns the expression unchanged on a token that does not
        // continue a member expression.)
        var expression = IsMemberExpressionContinuation(Token()) ? ParseMemberExpressionRest(pos, primary, /*allowOptionalChain*/ true) : primary;
        if (!ContinuesOperand(Token()))
        {
            // (The rest of parseLeftHandSideExpressionOrHigher, the postfix check and the exponentiation check would
            // each compare the token and return the expression unchanged.)
            return expression;
        }
        expression = ParseLeftHandSideExpressionRest(pos, expression);
        expression = ParseUpdateExpressionRest(expression);
        return Token() == SyntaxKind.AsteriskAsteriskToken ?
            ParseBinaryExpressionRest(Utilities.GetBinaryOperatorPrecedence(Token()), expression, pos) :
            expression;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsInOrOfKeyword(SyntaxKind t)
    {
        return t == SyntaxKind.InKeyword || t == SyntaxKind.OfKeyword;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseBinaryExpressionRest(OperatorPrecedence precedence, Node leftOperand, int pos)
    {
        while (true)
        {
            // We either have a binary operator here, or we're finished.  We call
            // reScanGreaterToken so that we merge token sequences like > and = into >=

            ReScanGreaterToken();
            var newPrecedence = BinaryOperatorPrecedence(Token());

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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsBinaryOperator()
    {
        if (InDisallowInContext() && Token() == SyntaxKind.InKeyword)
        {
            return false;
        }

        return Utilities.GetBinaryOperatorPrecedence(Token()) > 0;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node MakeSatisfiesExpression(Node left, Node right)
    {
        return FinishNode(factory.CreateSatisfiesExpression(left, right), store.Pos(left));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node MakeBinaryExpression(Node left, Node operatorToken, Node right, int pos)
    {
        return FinishNode(factory.CreateBinaryExpression(left, operatorToken, right), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node MakeAsExpression(Node left, Node right)
    {
        return FinishNode(factory.CreateAsExpression(left, right), store.Pos(left));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParsePrefixUnaryExpression()
    {
        var pos = GetNodePos();
        return FinishNode(factory.CreatePrefixUnaryExpression(Token(), NextTokenAnd(new F.ParseSimpleUnaryExpression())), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseDeleteExpression()
    {
        var pos = GetNodePos();
        return FinishNode(factory.CreateDeleteExpression(NextTokenAnd(new F.ParseSimpleUnaryExpression())), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseTypeOfExpression()
    {
        var pos = GetNodePos();
        return FinishNode(factory.CreateTypeOfExpression(NextTokenAnd(new F.ParseSimpleUnaryExpression())), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseVoidExpression()
    {
        var pos = GetNodePos();
        return FinishNode(factory.CreateVoidExpression(NextTokenAnd(new F.ParseSimpleUnaryExpression())), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsAwaitExpression()
    {
        if (Token() == SyntaxKind.AwaitKeyword)
        {
            if (InAwaitContext())
            {
                return true;
            }

            // here we are using similar heuristics as 'isYieldExpression'
            return LookAhead(new F.NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine());
        }

        return false;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseAwaitExpression()
    {
        var pos = GetNodePos();
        return FinishNode(factory.CreateAwaitExpression(NextTokenAnd(new F.ParseSimpleUnaryExpression())), pos);
    }

    /// <summary>
    /// Parse ES7 exponential expression and await expression
    ///
    /// ES7 ExponentiationExpression:
    ///      1) UnaryExpression[?Yield]
    ///      2) UpdateExpression[?Yield] ** ExponentiationExpression[?Yield]
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseUpdateExpression()
    {
        if (Token() == SyntaxKind.PlusPlusToken || Token() == SyntaxKind.MinusMinusToken)
        {
            var pos = GetNodePos();
            return FinishNode(factory.CreatePrefixUnaryExpression(Token(), NextTokenAnd(new F.ParseLeftHandSideExpressionOrHigher())), pos);
        }
        else if (languageVariant == LanguageVariant.JSX && Token() == SyntaxKind.LessThanToken && LookAhead(new F.NextTokenIsIdentifierOrKeywordOrGreaterThan()))
        {
            // JSXElement is part of primaryExpression
            return ParseJsxElementOrSelfClosingElementOrFragment(/*inExpressionContext*/ true);
        }

        return ParseUpdateExpressionRest(ParseLeftHandSideExpressionOrHigher());
    }

    // The postfix part of parseUpdateExpression (Track O, step O25: shared with ParseUnaryOperandRest).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseUpdateExpressionRest(Node expression)
    {
        Debug.Assert(UtilitiesPublic.IsLeftHandSideExpression(store, expression));
        if ((Token() == SyntaxKind.PlusPlusToken || Token() == SyntaxKind.MinusMinusToken) && !scanner.HasPrecedingLineBreak())
        {
            var @operator = Token();
            NextToken();
            return FinishNode(factory.CreatePostfixUnaryExpression(expression, @operator), store.Pos(expression));
        }

        return expression;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
            if (LookAhead(new F.NextTokenIsOpenParenOrLessThan()))
            {
                // We don't want to eagerly consume all import keyword as import call expression so we look ahead to find "("
                // For example:
                //      var foo3 = require("subfolder
                //      import * as foo1 from "module-from-node
                // We want this import to be a statement rather than import call expression
                sourceFlags |= NodeFlags.PossiblyContainsDynamicImport;
                expression = ParseTokenNode();
            }
            else if (LookAhead(new F.NextTokenIsDot()))
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
                return ParseLeftHandSideExpressionRest(pos, ParseMemberExpressionOrHigher());
            }
        }
        else if (Token() == SyntaxKind.SuperKeyword)
        {
            expression = ParseSuperExpression();
        }
        else
        {
            return ParseLeftHandSideExpressionRest(pos, ParseMemberExpressionOrHigher());
        }

        // Now, we *may* be complete.  However, we might have consumed the start of a
        // CallExpression or OptionalExpression.  As such, we need to consume the rest
        // of it here to be complete.
        return ParseCallExpressionRest(pos, expression);
    }

    /// <summary>
    /// `parseCallExpressionRest(pos, expression)` for an expression that parseMemberExpressionOrHigher (or
    /// parseMemberExpressionRest) has just returned. Track O (step O25): parseCallExpressionRest starts with
    /// parseMemberExpressionRest on that expression in the same state, which ended exactly because none of its branches
    /// acted; on a token other than `?.`, `&lt;` and `&lt;&lt;` its checks are plain comparisons of that state, so the
    /// second call returns the expression unchanged without side effects and is left out. (On `?.` the check may look
    /// ahead, on `&lt;`/`&lt;&lt;` it may speculate: both scan, so they are repeated as before.)
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseLeftHandSideExpressionRest(int pos, Node expression)
    {
        var token = Token();
        if (token == SyntaxKind.QuestionDotToken || token == SyntaxKind.LessThanToken || token == SyntaxKind.LessThanLessThanToken)
        {
            return ParseCallExpressionRest(pos, expression);
        }
        return ParseCallExpressionRestAfterMemberRest(pos, expression);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
        // (Track O, step O28: parseMemberExpressionRest returns the expression unchanged on a token that does not
        // continue a member expression.)
        return IsMemberExpressionContinuation(Token()) ? ParseMemberExpressionRest(pos, expression, /*allowOptionalChain*/ true) : expression;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseSuperExpression()
    {
        var pos = GetNodePos();
        var expression = ParseTokenNode();
        if (Token() == SyntaxKind.LessThanToken)
        {
            var startPos = GetNodePos();
            var typeArguments = TryParse<F.ParseTypeArgumentsInExpression, NodeList>(new F.ParseTypeArgumentsInExpression());
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool NextTokenIsIdentifierOrKeywordOrOpenBracketOrTemplate()
    {
        NextToken();
        return Scanner.TokenIsIdentifierOrKeyword(Token())
            || Token() == SyntaxKind.OpenBracketToken
            || IsTemplateStartOfTaggedTemplate();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsStartOfOptionalPropertyOrElementAccessChain()
    {
        if (Token() != SyntaxKind.QuestionDotToken)
        {
            return false;
        }
        // Track O (step O11 variant C): exact pre-decision on a plain next token (Scanner.PeekPlainToken: kind known,
        // no comment before it; never a template, which starts with '`'); the lookahead would change nothing that
        // outlives it.
        var next = scanner.PeekPlainToken();
        if (next != SyntaxKind.Unknown)
        {
            return Scanner.TokenIsIdentifierOrKeyword(next) || next == SyntaxKind.OpenBracketToken;
        }
        return LookAhead(new F.NextTokenIsIdentifierOrKeywordOrOpenBracketOrTemplate());
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParsePropertyAccessExpressionRest(int pos, Node expression, Node questionDotToken)
    {
        var name = ParseRightSideOfDot(/*allowIdentifierNames*/ true, /*allowPrivateIdentifiers*/ true, /*allowUnicodeEscapeSequenceInIdentifierName*/ true);
        var isOptionalChain = !questionDotToken.IsNone || IsOrReparsesOptionalChain(store, expression);
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
        return FinishChainNode(propertyAccess, pos);
    }

    /// <summary>
    /// `finishNode(node, pos)` (FinishNode with the end at the current token's full start), statement for statement, in
    /// line in the call and member functions (Track O, step O28; NativeAOT does not inline FinishNode).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node FinishChainNode(Node node, int pos)
    {
        var flags = contextFlags;
        if (parseErrorBeforeNextFinishedNode)
        {
            parseErrorBeforeNextFinishedNode = false;
            flags |= NodeFlags.ThisNodeHasError;
        }
        store.Finish(node, pos, scanner.GetTokenFullStart(), flags);
        return node;
    }

    /// <summary>
    /// `tryReparseOptionalChain(node)` (Track O, step O28): for a node without the OptionalChain flag that is not a
    /// non-null expression it returns false before it changes anything, so it is called only for the others.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsOrReparsesOptionalChain(NodeStore store, Node node)
    {
        return (store.Flags(node) & NodeFlags.OptionalChain) != 0 ||
            (FactoryNodeTests.IsNonNullExpression(store, node) && TryReparseOptionalChain(store, node));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseElementAccessExpressionRest(int pos, Node expression, Node questionDotToken)
    {
        Node argumentExpression;
        if (Token() == SyntaxKind.CloseBracketToken)
        {
            argumentExpression = CreateMissingNode(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Diagnostics.An_element_access_expression_should_take_an_argument);
        }
        else
        {
            var argument = AllowInAnd(new F.ParseExpression());
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseMemberExpressionRest(int pos, Node expression, bool allowOptionalChain)
    {
        while (true)
        {
            var token = Token();
            // Track O (step O28): on `.`, isStartOfOptionalPropertyOrElementAccessChain returns false without side
            // effects (the token is not `?.`), and parseOptional(DotToken) consumes the dot: a property access without
            // a question-dot token, as below.
            if (token == SyntaxKind.DotToken)
            {
                NextTokenAfter(SyntaxKind.DotToken);
                expression = ParsePropertyAccessExpressionRest(pos, expression, /*questionDotToken*/ default);
                continue;
            }

            // Track O (step O25): on a token none of the branches below acts on, each of them compares the token and
            // falls through to the return (IsMemberExpressionContinuation).
            if (!IsMemberExpressionContinuation(token))
            {
                return expression;
            }

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
                var typeArguments = TryParseTypeArgumentsInExpression();
                if (!typeArguments.IsNone)
                {
                    expression = FinishNode(factory.CreateExpressionWithTypeArguments(expression, typeArguments), pos);
                    continue;
                }
            }

            return expression;
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsTemplateStartOfTaggedTemplate()
    {
        return Token() == SyntaxKind.NoSubstitutionTemplateLiteral || Token() == SyntaxKind.TemplateHead;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseCallExpressionRest(int pos, Node expression)
    {
        return ParseCallExpressionRestAfterMemberRest(pos, ParseMemberExpressionRest(pos, expression, /*allowOptionalChain*/ true));
    }

    /// <summary>
    /// parseCallExpressionRest after the parseMemberExpressionRest that starts each iteration of its loop (Track O, step
    /// O25: the loop is rotated, so each `continue` calls parseMemberExpressionRest before the next iteration, as the
    /// loop's first statement did; see ParseLeftHandSideExpressionRest for the caller that leaves out the first one).
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseCallExpressionRestAfterMemberRest(int pos, Node expression)
    {
        while (true)
        {
            // Track O (step O28): on a token other than `?.` and `(`, no question-dot token is parsed, there are no type
            // arguments, and the loop breaks without acting.
            var token = Token();
            if (token != SyntaxKind.QuestionDotToken && token != SyntaxKind.OpenParenToken)
            {
                return expression;
            }

            NodeList typeArguments = default;
            var questionDotToken = ParseOptionalToken(SyntaxKind.QuestionDotToken);
            if (!questionDotToken.IsNone)
            {
                typeArguments = TryParseTypeArgumentsInExpression();
                if (IsTemplateStartOfTaggedTemplate())
                {
                    expression = ParseTaggedTemplateRest(pos, expression, questionDotToken, typeArguments);
                    expression = ParseMemberExpressionRest(pos, expression, /*allowOptionalChain*/ true);
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
                var callExpr = !questionDotToken.IsNone || IsOrReparsesOptionalChain(store, expression) ?
                    factory.CreateCallChain(expression, questionDotToken, typeArguments, argumentList) :
                    factory.CreateCallExpression(expression, typeArguments, argumentList);
                expression = FinishChainNode(callExpr, pos);
                // (Track O, step O28: parseMemberExpressionRest returns the expression unchanged on a token that does not
                // continue a member expression, see there.)
                if (IsMemberExpressionContinuation(Token()))
                {
                    expression = ParseMemberExpressionRest(pos, expression, /*allowOptionalChain*/ true);
                }
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

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private NodeList ParseArgumentList()
    {
        ParseExpected(SyntaxKind.OpenParenToken);
        var result = ParseArgumentExpressions();
        ParseExpected(SyntaxKind.CloseParenToken);
        return result;
    }

    /// <summary>
    /// `parseDelimitedList(ParsingContext.ArgumentExpressions, parseArgumentExpression)` with the context fixed (Track
    /// O, step O28), statement for statement as ParseDelimitedListOrUndefined runs it for this context:
    /// isListElement and parseListElement call currentNode, which returns undefined for a context that is not reusable
    /// (isReusableParsingContext is false for ArgumentExpressions) before it reads anything, so isListElement is
    /// `token === DotDotDotToken || isStartOfExpression()` (isStartOfLeftHandSideExpression returns true for an
    /// identifier before anything else, so the identifier is tested first) and parseListElement is the callback;
    /// isListTerminator is end of file, `)` or `;`; getExpectedCommaDiagnostic is undefined; considerSemicolonAsDelimiter
    /// is not given.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private NodeList ParseArgumentExpressions()
    {
        var saveParsingContext = parsingContext;
        parsingContext |= 1 << (int)ParsingContext.ArgumentExpressions;
        var list = store.BeginList();
        var listPos = GetNodePos();

        var commaStart = -1; // Meaning the previous token was not a comma
        while (true)
        {
            var token = Token();
            if (token == SyntaxKind.Identifier || token == SyntaxKind.DotDotDotToken || IsStartOfExpression())
            {
                var startPos = scanner.GetTokenFullStart();
                // parseArgumentExpression: doOutsideOfContext(disallowInAndDecoratorContext,
                // parseArgumentOrArrayLiteralElement), with doOutsideOfContext's statements in line.
                Node result;
                var contextFlagsToClear = DisallowInAndDecoratorContext & contextFlags;
                if (contextFlagsToClear != 0)
                {
                    SetContextFlag(/*val*/ false, contextFlagsToClear);
                    result = ParseArgumentOrArrayLiteralElement();
                    SetContextFlag(/*val*/ true, contextFlagsToClear);
                }
                else
                {
                    result = ParseArgumentOrArrayLiteralElement();
                }
                if (result.IsNone)
                {
                    parsingContext = saveParsingContext;
                    store.DiscardList(list);
                    return default;
                }
                store.Push(list, result);
                commaStart = scanner.GetTokenStart();

                if (ParseOptional(SyntaxKind.CommaToken))
                {
                    // No need to check for a zero length node since we know we parsed a comma
                    continue;
                }

                commaStart = -1; // Back to the state where the last token was not a comma
                token = Token();
                if (token == SyntaxKind.CloseParenToken || token == SyntaxKind.EndOfFileToken || token == SyntaxKind.SemicolonToken)
                {
                    break;
                }

                // We didn't get a comma, and the list wasn't terminated, explicitly parse
                // out a comma so we give a good error message.
                ParseExpected(SyntaxKind.CommaToken, /*diagnosticMessage*/ null);

                if (startPos == scanner.GetTokenFullStart())
                {
                    // What we're parsing isn't actually remotely recognizable as a element and we've consumed no tokens whatsoever
                    // Consume a token to advance the parser in some way and avoid an infinite loop
                    NextToken();
                }
                continue;
            }

            token = Token();
            if (token == SyntaxKind.CloseParenToken || token == SyntaxKind.EndOfFileToken || token == SyntaxKind.SemicolonToken)
            {
                break;
            }

            if (AbortParsingListOrMoveToNextToken(ParsingContext.ArgumentExpressions))
            {
                break;
            }
        }

        parsingContext = saveParsingContext;
        return CreateNodeArray(list, listPos, /*end*/ null, commaStart >= 0);
    }

    /// <summary>
    /// `tryParse(parseTypeArgumentsInExpression)` with an exact pre-decision (Track O, step O11 variant C). In a
    /// JavaScript file, or when the scanner's token is neither `&lt;` nor `&lt;&lt;`, the callback returns undefined
    /// before it changes anything (`reScanLessThanToken` changes the scanner only for `&lt;&lt;`; the parser's
    /// `currentToken` it assigns is restored by the rollback): the speculation would create no node, scan nothing
    /// and report nothing, and its rollback would restore what was already there. So the result is known without
    /// it. Only a `&lt;` or `&lt;&lt;` in a TypeScript file speculates.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private NodeList TryParseTypeArgumentsInExpression()
    {
        if ((contextFlags & NodeFlags.JavaScriptFile) != 0 ||
            (scanner.GetToken() != SyntaxKind.LessThanToken && scanner.GetToken() != SyntaxKind.LessThanLessThanToken))
        {
            return default;
        }
        return TryParse<F.ParseTypeArgumentsInExpression, NodeList>(new F.ParseTypeArgumentsInExpression());
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

        var typeArguments = ParseDelimitedList(ParsingContext.TypeArguments, new F.ParseType());
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
                if (!LookAhead(new F.NextTokenIsFunctionKeywordOnSameLine()))
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseParenthesizedExpression()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(new F.ParseExpression());
        ParseExpected(SyntaxKind.CloseParenToken);
        return WithJSDoc(FinishNode(factory.CreateParenthesizedExpression(expression), pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseSpreadElement()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.DotDotDotToken);
        var expression = ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);
        return FinishNode(factory.CreateSpreadElement(expression), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseArgumentOrArrayLiteralElement()
    {
        return Token() == SyntaxKind.DotDotDotToken ? ParseSpreadElement() :
            Token() == SyntaxKind.CommaToken ? FinishNode(factory.CreateOmittedExpression(), GetNodePos()) :
            ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseArgumentExpression()
    {
        return DoOutsideOfContext(DisallowInAndDecoratorContext, new F.ParseArgumentOrArrayLiteralElement());
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseArrayLiteralExpression()
    {
        var pos = GetNodePos();
        var openBracketPosition = scanner.GetTokenStart();
        var openBracketParsed = ParseExpected(SyntaxKind.OpenBracketToken);
        var multiLine = scanner.HasPrecedingLineBreak();
        var elements = ParseDelimitedList(ParsingContext.ArrayLiteralMembers, new F.ParseArgumentOrArrayLiteralElement());
        ParseExpectedMatchingBrackets(SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken, openBracketParsed, openBracketPosition);
        return FinishNode(factory.CreateArrayLiteralExpression(elements, multiLine), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
            var objectAssignmentInitializer = !equalsToken.IsNone ? AllowInAnd(new F.ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true)) : default;
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
            var initializer = AllowInAnd(new F.ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true));
            var assignment = factory.CreatePropertyAssignment(name, initializer);
            // Decorators, Modifiers, questionToken, and exclamationToken are not supported by property assignments and are reported in the grammar checker
            store.SetModifiers(assignment, modifiers);
            store.SetQuestionToken(assignment, questionToken);
            store.SetExclamationToken(assignment, exclamationToken);
            node = assignment;
        }
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseObjectLiteralExpression()
    {
        var pos = GetNodePos();
        var openBracePosition = scanner.GetTokenStart();
        var openBraceParsed = ParseExpected(SyntaxKind.OpenBraceToken);
        var multiLine = scanner.HasPrecedingLineBreak();
        var properties = ParseDelimitedList(ParsingContext.ObjectLiteralMembers, new F.ParseObjectLiteralElement(), /*considerSemicolonAsDelimiter*/ true);
        ParseExpectedMatchingBrackets(SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken, openBraceParsed, openBracePosition);
        return FinishNode(factory.CreateObjectLiteralExpression(properties, multiLine), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
        var isAsync = ModifiersContain(modifiers, SyntaxKind.AsyncKeyword) ? SignatureFlags.Await : SignatureFlags.None;
        var name = isGenerator != 0 && isAsync != 0 ? DoInYieldAndAwaitContext(new F.ParseOptionalBindingIdentifier()) :
            isGenerator != 0 ? DoInYieldContext(new F.ParseOptionalBindingIdentifier()) :
            isAsync != 0 ? DoInAwaitContext<F.ParseOptionalBindingIdentifier, Node>(new F.ParseOptionalBindingIdentifier()) :
            ParseOptionalBindingIdentifier();

        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(isGenerator | isAsync);
        var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
        var body = ParseFunctionBlock(isGenerator | isAsync);

        SetDecoratorContext(savedDecoratorContext);

        var node = factory.CreateFunctionExpression(modifiers, asteriskToken, name, typeParameters, parameters, type, body);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseOptionalBindingIdentifier()
    {
        return IsBindingIdentifier() ? ParseBindingIdentifier() : default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
            ParseErrorWithTextOfNode(scanner.GetTokenStart(), scanner.GetTokenEnd(), Diagnostics.Invalid_optional_chain_from_new_expression_Did_you_mean_to_call_0, expression);
        }
        var argumentList = Token() == SyntaxKind.OpenParenToken ? ParseArgumentList() : default;
        return FinishNode(factory.CreateNewExpression(expression, typeArguments, argumentList), pos);
    }
}
