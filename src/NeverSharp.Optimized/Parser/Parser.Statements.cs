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

/// <summary>parser.ts: `namespace Parser`, statements (parseBlock through parseFunctionBlockOrSemicolon).</summary>
internal sealed partial class Parser
{
    // STATEMENTS
    private Block ParseBlock(bool ignoreMissingOpenBrace, DiagnosticMessage? diagnosticMessage = null)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var openBracePosition = scanner.GetTokenStart();
        var openBraceParsed = ParseExpected(SyntaxKind.OpenBraceToken, diagnosticMessage);
        if (openBraceParsed || ignoreMissingOpenBrace)
        {
            var multiLine = scanner.HasPrecedingLineBreak();
            var statements = ParseList(ParsingContext.BlockStatements, static p => p.ParseStatement());
            ParseExpectedMatchingBrackets(SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken, openBraceParsed, openBracePosition);
            var result = WithJSDoc(FinishNode(factory.CreateBlock(statements, multiLine), pos), hasJSDoc);
            if (Token() == SyntaxKind.EqualsToken)
            {
                ParseErrorAtCurrentToken(Diagnostics.Declaration_or_statement_expected_This_follows_a_block_of_statements_so_if_you_intended_to_write_a_destructuring_assignment_you_might_need_to_wrap_the_whole_assignment_in_parentheses);
                NextToken();
            }

            return result;
        }
        else
        {
            var statements = CreateMissingList();
            return WithJSDoc(FinishNode(factory.CreateBlock(statements, /*multiLine*/ null), pos), hasJSDoc);
        }
    }

    private Block ParseFunctionBlock(SignatureFlags flags, DiagnosticMessage? diagnosticMessage = null)
    {
        var savedYieldContext = InYieldContext();
        SetYieldContext((flags & SignatureFlags.Yield) != 0);

        var savedAwaitContext = InAwaitContext();
        SetAwaitContext((flags & SignatureFlags.Await) != 0);

        var savedTopLevel = topLevel;
        topLevel = false;

        // We may be in a [Decorator] context when parsing a function expression or
        // arrow function. The body of the function is not in [Decorator] context.
        var saveDecoratorContext = InDecoratorContext();
        if (saveDecoratorContext)
        {
            SetDecoratorContext(/*val*/ false);
        }

        var block = ParseBlock((flags & SignatureFlags.IgnoreMissingOpenBrace) != 0, diagnosticMessage);

        if (saveDecoratorContext)
        {
            SetDecoratorContext(/*val*/ true);
        }

        topLevel = savedTopLevel;
        SetYieldContext(savedYieldContext);
        SetAwaitContext(savedAwaitContext);

        return block;
    }

    private EmptyStatement ParseEmptyStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.SemicolonToken);
        return WithJSDoc(FinishNode(factory.CreateEmptyStatement(), pos), hasJSDoc);
    }

    private IfStatement ParseIfStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.IfKeyword);
        var openParenPosition = scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(static p => p.ParseExpression());
        ParseExpectedMatchingBrackets(SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, openParenParsed, openParenPosition);
        var thenStatement = ParseStatement();
        var elseStatement = ParseOptional(SyntaxKind.ElseKeyword) ? ParseStatement() : null;
        return WithJSDoc(FinishNode(factory.CreateIfStatement(expression, thenStatement, elseStatement), pos), hasJSDoc);
    }

    private DoStatement ParseDoStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.DoKeyword);
        var statement = ParseStatement();
        ParseExpected(SyntaxKind.WhileKeyword);
        var openParenPosition = scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(static p => p.ParseExpression());
        ParseExpectedMatchingBrackets(SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, openParenParsed, openParenPosition);

        // From: https://mail.mozilla.org/pipermail/es-discuss/2011-August/016188.html
        // 157 min --- All allen at wirfs-brock.com CONF --- "do{;}while(false)false" prohibited in
        // spec but allowed in consensus reality. Approved -- this is the de-facto standard whereby
        //  do;while(0)x will have a semicolon inserted before x.
        ParseOptional(SyntaxKind.SemicolonToken);
        return WithJSDoc(FinishNode(factory.CreateDoStatement(statement, expression), pos), hasJSDoc);
    }

    private WhileStatement ParseWhileStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.WhileKeyword);
        var openParenPosition = scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(static p => p.ParseExpression());
        ParseExpectedMatchingBrackets(SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, openParenParsed, openParenPosition);
        var statement = ParseStatement();
        return WithJSDoc(FinishNode(factory.CreateWhileStatement(expression, statement), pos), hasJSDoc);
    }

    private Node ParseForOrForInOrForOfStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.ForKeyword);
        var awaitToken = ParseOptionalToken<AwaitKeyword>(SyntaxKind.AwaitKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);

        // TypeScript: `let initializer!: VariableDeclarationList | Expression;` (undefined when the token is ';').
        Node? initializer = null;
        if (Token() != SyntaxKind.SemicolonToken)
        {
            if (
                Token() == SyntaxKind.VarKeyword || Token() == SyntaxKind.LetKeyword || Token() == SyntaxKind.ConstKeyword ||
                Token() == SyntaxKind.UsingKeyword && LookAhead(static p => p.NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOf()) ||
                // this one is meant to allow of
                Token() == SyntaxKind.AwaitKeyword && LookAhead(static p => p.NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine()))
            {
                initializer = ParseVariableDeclarationList(/*inForStatementInitializer*/ true);
            }
            else
            {
                initializer = DisallowInAnd(static p => p.ParseExpression());
            }
        }

        Node node;
        if (awaitToken != null ? ParseExpected(SyntaxKind.OfKeyword) : ParseOptional(SyntaxKind.OfKeyword))
        {
            var expression = AllowInAnd(static p => p.ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true));
            ParseExpected(SyntaxKind.CloseParenToken);
            node = factory.CreateForOfStatement(awaitToken, initializer!, expression, ParseStatement());
        }
        else if (ParseOptional(SyntaxKind.InKeyword))
        {
            var expression = AllowInAnd(static p => p.ParseExpression());
            ParseExpected(SyntaxKind.CloseParenToken);
            node = factory.CreateForInStatement(initializer!, expression, ParseStatement());
        }
        else
        {
            ParseExpected(SyntaxKind.SemicolonToken);
            var condition = Token() != SyntaxKind.SemicolonToken && Token() != SyntaxKind.CloseParenToken
                ? AllowInAnd(static p => p.ParseExpression())
                : null;
            ParseExpected(SyntaxKind.SemicolonToken);
            var incrementor = Token() != SyntaxKind.CloseParenToken
                ? AllowInAnd(static p => p.ParseExpression())
                : null;
            ParseExpected(SyntaxKind.CloseParenToken);
            node = factory.CreateForStatement(initializer, condition, incrementor, ParseStatement());
        }

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private Node ParseBreakOrContinueStatement(SyntaxKind kind)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();

        ParseExpected(kind == SyntaxKind.BreakStatement ? SyntaxKind.BreakKeyword : SyntaxKind.ContinueKeyword);
        var label = CanParseSemicolon() ? null : ParseIdentifier();

        ParseSemicolon();
        var node = kind == SyntaxKind.BreakStatement
            ? (Node)factory.CreateBreakStatement(label)
            : factory.CreateContinueStatement(label);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private ReturnStatement ParseReturnStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.ReturnKeyword);
        var expression = CanParseSemicolon() ? null : AllowInAnd(static p => p.ParseExpression());
        ParseSemicolon();
        return WithJSDoc(FinishNode(factory.CreateReturnStatement(expression), pos), hasJSDoc);
    }

    private WithStatement ParseWithStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.WithKeyword);
        var openParenPosition = scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(static p => p.ParseExpression());
        ParseExpectedMatchingBrackets(SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, openParenParsed, openParenPosition);
        var statement = DoInsideOfContext(NodeFlags.InWithStatement, static p => p.ParseStatement());
        return WithJSDoc(FinishNode(factory.CreateWithStatement(expression, statement), pos), hasJSDoc);
    }

    private CaseClause ParseCaseClause()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.CaseKeyword);
        var expression = AllowInAnd(static p => p.ParseExpression());
        ParseExpected(SyntaxKind.ColonToken);
        var statements = ParseList(ParsingContext.SwitchClauseStatements, static p => p.ParseStatement());
        return WithJSDoc(FinishNode(factory.CreateCaseClause(expression, statements), pos), hasJSDoc);
    }

    private DefaultClause ParseDefaultClause()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.DefaultKeyword);
        ParseExpected(SyntaxKind.ColonToken);
        var statements = ParseList(ParsingContext.SwitchClauseStatements, static p => p.ParseStatement());
        return FinishNode(factory.CreateDefaultClause(statements), pos);
    }

    private Node ParseCaseOrDefaultClause()
    {
        return Token() == SyntaxKind.CaseKeyword ? ParseCaseClause() : ParseDefaultClause();
    }

    private CaseBlock ParseCaseBlock()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        var clauses = ParseList(ParsingContext.SwitchClauses, static p => p.ParseCaseOrDefaultClause());
        ParseExpected(SyntaxKind.CloseBraceToken);
        return FinishNode(factory.CreateCaseBlock(clauses), pos);
    }

    private SwitchStatement ParseSwitchStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.SwitchKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(static p => p.ParseExpression());
        ParseExpected(SyntaxKind.CloseParenToken);
        var caseBlock = ParseCaseBlock();
        return WithJSDoc(FinishNode(factory.CreateSwitchStatement(expression, caseBlock), pos), hasJSDoc);
    }

    private ThrowStatement ParseThrowStatement()
    {
        // ThrowStatement[Yield] :
        //      throw [no LineTerminator here]Expression[In, ?Yield];

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.ThrowKeyword);

        // Because of automatic semicolon insertion, we need to report error if this
        // throw could be terminated with a semicolon.  Note: we can't call 'parseExpression'
        // directly as that might consume an expression on the following line.
        // Instead, we create a "missing" identifier, but don't report an error. The actual error
        // will be reported in the grammar walker.
        var expression = scanner.HasPrecedingLineBreak() ? null : AllowInAnd(static p => p.ParseExpression());
        if (expression == null)
        {
            identifierCount++;
            expression = FinishNode(factory.CreateIdentifier(""), GetNodePos());
        }
        if (!TryParseSemicolon())
        {
            ParseErrorForMissingSemicolonAfter(expression);
        }
        return WithJSDoc(FinishNode(factory.CreateThrowStatement(expression), pos), hasJSDoc);
    }

    // TODO: Review for error recovery
    private TryStatement ParseTryStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();

        ParseExpected(SyntaxKind.TryKeyword);
        var tryBlock = ParseBlock(/*ignoreMissingOpenBrace*/ false);
        var catchClause = Token() == SyntaxKind.CatchKeyword ? ParseCatchClause() : null;

        // If we don't have a catch clause, then we must have a finally clause.  Try to parse
        // one out no matter what.
        Block? finallyBlock = null;
        if (catchClause == null || Token() == SyntaxKind.FinallyKeyword)
        {
            ParseExpected(SyntaxKind.FinallyKeyword, Diagnostics.catch_or_finally_expected);
            finallyBlock = ParseBlock(/*ignoreMissingOpenBrace*/ false);
        }

        return WithJSDoc(FinishNode(factory.CreateTryStatement(tryBlock, catchClause, finallyBlock), pos), hasJSDoc);
    }

    private CatchClause ParseCatchClause()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.CatchKeyword);

        VariableDeclaration? variableDeclaration;
        if (ParseOptional(SyntaxKind.OpenParenToken))
        {
            variableDeclaration = ParseVariableDeclaration();
            ParseExpected(SyntaxKind.CloseParenToken);
        }
        else
        {
            // Keep shape of node to avoid degrading performance.
            variableDeclaration = null;
        }

        var block = ParseBlock(/*ignoreMissingOpenBrace*/ false);
        return FinishNode(factory.CreateCatchClause(variableDeclaration, block), pos);
    }

    private DebuggerStatement ParseDebuggerStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.DebuggerKeyword);
        ParseSemicolon();
        return WithJSDoc(FinishNode(factory.CreateDebuggerStatement(), pos), hasJSDoc);
    }

    private Node ParseExpressionOrLabeledStatement()
    {
        // Avoiding having to do the lookahead for a labeled statement by just trying to parse
        // out an expression, seeing if it is identifier and then seeing if it is followed by
        // a colon.
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        Node node;
        var hasParen = Token() == SyntaxKind.OpenParenToken;
        var expression = AllowInAnd(static p => p.ParseExpression());
        if (FactoryNodeTests.IsIdentifier(expression) && ParseOptional(SyntaxKind.ColonToken))
        {
            node = factory.CreateLabeledStatement(expression, ParseStatement());
        }
        else
        {
            if (!TryParseSemicolon())
            {
                ParseErrorForMissingSemicolonAfter(expression);
            }
            node = factory.CreateExpressionStatement(expression);
            if (hasParen)
            {
                // do not parse the same jsdoc twice
                hasJSDoc = false;
            }
        }
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private bool NextTokenIsIdentifierOrKeywordOnSameLine()
    {
        NextToken();
        return Scanner.TokenIsIdentifierOrKeyword(Token()) && !scanner.HasPrecedingLineBreak();
    }

    private bool NextTokenIsClassKeywordOnSameLine()
    {
        NextToken();
        return Token() == SyntaxKind.ClassKeyword && !scanner.HasPrecedingLineBreak();
    }

    private bool NextTokenIsFunctionKeywordOnSameLine()
    {
        NextToken();
        return Token() == SyntaxKind.FunctionKeyword && !scanner.HasPrecedingLineBreak();
    }

    private bool NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine()
    {
        NextToken();
        return (Scanner.TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.NumericLiteral || Token() == SyntaxKind.BigIntLiteral || Token() == SyntaxKind.StringLiteral) && !scanner.HasPrecedingLineBreak();
    }

    private bool IsDeclaration()
    {
        while (true)
        {
            switch (Token())
            {
                case SyntaxKind.VarKeyword:
                case SyntaxKind.LetKeyword:
                case SyntaxKind.ConstKeyword:
                case SyntaxKind.FunctionKeyword:
                case SyntaxKind.ClassKeyword:
                case SyntaxKind.EnumKeyword:
                    return true;
                case SyntaxKind.UsingKeyword:
                    return IsUsingDeclaration();
                case SyntaxKind.AwaitKeyword:
                    return IsAwaitUsingDeclaration();

                // 'declare', 'module', 'namespace', 'interface'* and 'type' are all legal JavaScript identifiers;
                // however, an identifier cannot be followed by another identifier on the same line. This is what we
                // count on to parse out the respective declarations. For instance, we exploit this to say that
                //
                //    namespace n
                //
                // can be none other than the beginning of a namespace declaration, but need to respect that JavaScript sees
                //
                //    namespace
                //    n
                //
                // as the identifier 'namespace' on one line followed by the identifier 'n' on another.
                // We need to look one token ahead to see if it permissible to try parsing a declaration.
                //
                // *Note*: 'interface' is actually a strict mode reserved word. So while
                //
                //   "use strict"
                //   interface
                //   I {}
                //
                // could be legal, it would add complexity for very little gain.
                case SyntaxKind.InterfaceKeyword:
                case SyntaxKind.TypeKeyword:
                case SyntaxKind.DeferKeyword:
                    return NextTokenIsIdentifierOnSameLine();
                case SyntaxKind.ModuleKeyword:
                case SyntaxKind.NamespaceKeyword:
                    return NextTokenIsIdentifierOrStringLiteralOnSameLine();
                case SyntaxKind.AbstractKeyword:
                case SyntaxKind.AccessorKeyword:
                case SyntaxKind.AsyncKeyword:
                case SyntaxKind.DeclareKeyword:
                case SyntaxKind.PrivateKeyword:
                case SyntaxKind.ProtectedKeyword:
                case SyntaxKind.PublicKeyword:
                case SyntaxKind.ReadonlyKeyword:
                {
                    var previousToken = Token();
                    NextToken();
                    // ASI takes effect for this modifier.
                    if (scanner.HasPrecedingLineBreak())
                    {
                        return false;
                    }
                    if (previousToken == SyntaxKind.DeclareKeyword && Token() == SyntaxKind.TypeKeyword)
                    {
                        // If we see 'declare type', then commit to parsing a type alias. parseTypeAliasDeclaration will
                        // report Line_break_not_permitted_here if needed.
                        return true;
                    }
                    continue;
                }

                case SyntaxKind.GlobalKeyword:
                    NextToken();
                    return Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.Identifier || Token() == SyntaxKind.ExportKeyword;

                case SyntaxKind.ImportKeyword:
                    NextToken();
                    return Token() == SyntaxKind.DeferKeyword || Token() == SyntaxKind.StringLiteral || Token() == SyntaxKind.AsteriskToken ||
                        Token() == SyntaxKind.OpenBraceToken || Scanner.TokenIsIdentifierOrKeyword(Token());
                case SyntaxKind.ExportKeyword:
                {
                    // (TypeScript names this local `currentToken`, shadowing the parser variable.)
                    var currentTokenLocal = NextToken();
                    if (currentTokenLocal == SyntaxKind.TypeKeyword)
                    {
                        currentTokenLocal = LookAhead(static p => p.NextToken());
                    }
                    if (
                        currentTokenLocal == SyntaxKind.EqualsToken || currentTokenLocal == SyntaxKind.AsteriskToken ||
                        currentTokenLocal == SyntaxKind.OpenBraceToken || currentTokenLocal == SyntaxKind.DefaultKeyword ||
                        currentTokenLocal == SyntaxKind.AsKeyword || currentTokenLocal == SyntaxKind.AtToken)
                    {
                        return true;
                    }
                    continue;
                }

                case SyntaxKind.StaticKeyword:
                    NextToken();
                    continue;

                default:
                    return false;
            }
        }
    }

    private bool IsStartOfDeclaration()
    {
        return LookAhead(static p => p.IsDeclaration());
    }

    private bool IsStartOfStatement()
    {
        switch (Token())
        {
            case SyntaxKind.AtToken:
            case SyntaxKind.SemicolonToken:
            case SyntaxKind.OpenBraceToken:
            case SyntaxKind.VarKeyword:
            case SyntaxKind.LetKeyword:
            case SyntaxKind.UsingKeyword:
            case SyntaxKind.FunctionKeyword:
            case SyntaxKind.ClassKeyword:
            case SyntaxKind.EnumKeyword:
            case SyntaxKind.IfKeyword:
            case SyntaxKind.DoKeyword:
            case SyntaxKind.WhileKeyword:
            case SyntaxKind.ForKeyword:
            case SyntaxKind.ContinueKeyword:
            case SyntaxKind.BreakKeyword:
            case SyntaxKind.ReturnKeyword:
            case SyntaxKind.WithKeyword:
            case SyntaxKind.SwitchKeyword:
            case SyntaxKind.ThrowKeyword:
            case SyntaxKind.TryKeyword:
            case SyntaxKind.DebuggerKeyword:
            // 'catch' and 'finally' do not actually indicate that the code is part of a statement,
            // however, we say they are here so that we may gracefully parse them and error later.
            // falls through
            case SyntaxKind.CatchKeyword:
            case SyntaxKind.FinallyKeyword:
                return true;

            case SyntaxKind.ImportKeyword:
                return IsStartOfDeclaration() || LookAhead(static p => p.NextTokenIsOpenParenOrLessThanOrDot());

            case SyntaxKind.ConstKeyword:
            case SyntaxKind.ExportKeyword:
                return IsStartOfDeclaration();

            case SyntaxKind.AsyncKeyword:
            case SyntaxKind.DeclareKeyword:
            case SyntaxKind.InterfaceKeyword:
            case SyntaxKind.ModuleKeyword:
            case SyntaxKind.NamespaceKeyword:
            case SyntaxKind.TypeKeyword:
            case SyntaxKind.GlobalKeyword:
            case SyntaxKind.DeferKeyword:
                // When these don't start a declaration, they're an identifier in an expression statement
                return true;

            case SyntaxKind.AccessorKeyword:
            case SyntaxKind.PublicKeyword:
            case SyntaxKind.PrivateKeyword:
            case SyntaxKind.ProtectedKeyword:
            case SyntaxKind.StaticKeyword:
            case SyntaxKind.ReadonlyKeyword:
                // When these don't start a declaration, they may be the start of a class member if an identifier
                // immediately follows. Otherwise they're an identifier in an expression statement.
                return IsStartOfDeclaration() || !LookAhead(static p => p.NextTokenIsIdentifierOrKeywordOnSameLine());

            default:
                return IsStartOfExpression();
        }
    }

    private bool NextTokenIsBindingIdentifierOrStartOfDestructuring()
    {
        NextToken();
        return IsBindingIdentifier() || Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.OpenBracketToken;
    }

    private bool IsLetDeclaration()
    {
        // In ES6 'let' always starts a lexical declaration if followed by an identifier or {
        // or [.
        return LookAhead(static p => p.NextTokenIsBindingIdentifierOrStartOfDestructuring());
    }

    private bool NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOf()
    {
        return NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(/*disallowOf*/ true);
    }

    private bool NextTokenIsEqualsOrSemicolonOrColonToken()
    {
        NextToken();
        return Token() == SyntaxKind.EqualsToken || Token() == SyntaxKind.SemicolonToken || Token() == SyntaxKind.ColonToken;
    }

    private bool NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(bool? disallowOf = null)
    {
        NextToken();
        if (disallowOf == true && Token() == SyntaxKind.OfKeyword)
        {
            return LookAhead(static p => p.NextTokenIsEqualsOrSemicolonOrColonToken());
        }
        return (IsBindingIdentifier() || Token() == SyntaxKind.OpenBraceToken) && !scanner.HasPrecedingLineBreak();
    }

    private bool IsUsingDeclaration()
    {
        // 'using' always starts a lexical declaration if followed by an identifier. We also eagerly parse
        // |ObjectBindingPattern| so that we can report a grammar error during check. We don't parse out
        // |ArrayBindingPattern| since it potentially conflicts with element access (i.e., `using[x]`).
        return LookAhead(static p => p.NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine());
    }

    private bool NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine(bool? disallowOf = null)
    {
        if (NextToken() == SyntaxKind.UsingKeyword)
        {
            return NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(disallowOf);
        }
        return false;
    }

    private bool IsAwaitUsingDeclaration()
    {
        // 'await using' always starts a lexical declaration if followed by an identifier. We also eagerly parse
        // |ObjectBindingPattern| so that we can report a grammar error during check. We don't parse out
        // |ArrayBindingPattern| since it potentially conflicts with element access (i.e., `await using[x]`).
        return LookAhead(static p => p.NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine());
    }

    private Node ParseStatement()
    {
        switch (Token())
        {
            case SyntaxKind.SemicolonToken:
                return ParseEmptyStatement();
            case SyntaxKind.OpenBraceToken:
                return ParseBlock(/*ignoreMissingOpenBrace*/ false);
            case SyntaxKind.VarKeyword:
                return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ null);
            case SyntaxKind.LetKeyword:
                if (IsLetDeclaration())
                {
                    return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ null);
                }
                break;
            case SyntaxKind.AwaitKeyword:
                if (IsAwaitUsingDeclaration())
                {
                    return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ null);
                }
                break;
            case SyntaxKind.UsingKeyword:
                if (IsUsingDeclaration())
                {
                    return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ null);
                }
                break;
            case SyntaxKind.FunctionKeyword:
                return ParseFunctionDeclaration(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ null);
            case SyntaxKind.ClassKeyword:
                return ParseClassDeclaration(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ null);
            case SyntaxKind.IfKeyword:
                return ParseIfStatement();
            case SyntaxKind.DoKeyword:
                return ParseDoStatement();
            case SyntaxKind.WhileKeyword:
                return ParseWhileStatement();
            case SyntaxKind.ForKeyword:
                return ParseForOrForInOrForOfStatement();
            case SyntaxKind.ContinueKeyword:
                return ParseBreakOrContinueStatement(SyntaxKind.ContinueStatement);
            case SyntaxKind.BreakKeyword:
                return ParseBreakOrContinueStatement(SyntaxKind.BreakStatement);
            case SyntaxKind.ReturnKeyword:
                return ParseReturnStatement();
            case SyntaxKind.WithKeyword:
                return ParseWithStatement();
            case SyntaxKind.SwitchKeyword:
                return ParseSwitchStatement();
            case SyntaxKind.ThrowKeyword:
                return ParseThrowStatement();
            case SyntaxKind.TryKeyword:
            // Include 'catch' and 'finally' for error recovery.
            // falls through
            case SyntaxKind.CatchKeyword:
            case SyntaxKind.FinallyKeyword:
                return ParseTryStatement();
            case SyntaxKind.DebuggerKeyword:
                return ParseDebuggerStatement();
            case SyntaxKind.AtToken:
                return ParseDeclaration();
            case SyntaxKind.AsyncKeyword:
            case SyntaxKind.InterfaceKeyword:
            case SyntaxKind.TypeKeyword:
            case SyntaxKind.ModuleKeyword:
            case SyntaxKind.NamespaceKeyword:
            case SyntaxKind.DeclareKeyword:
            case SyntaxKind.ConstKeyword:
            case SyntaxKind.EnumKeyword:
            case SyntaxKind.ExportKeyword:
            case SyntaxKind.ImportKeyword:
            case SyntaxKind.PrivateKeyword:
            case SyntaxKind.ProtectedKeyword:
            case SyntaxKind.PublicKeyword:
            case SyntaxKind.AbstractKeyword:
            case SyntaxKind.AccessorKeyword:
            case SyntaxKind.StaticKeyword:
            case SyntaxKind.ReadonlyKeyword:
            case SyntaxKind.GlobalKeyword:
                if (IsStartOfDeclaration())
                {
                    return ParseDeclaration();
                }
                break;
        }
        return ParseExpressionOrLabeledStatement();
    }

    private static bool IsDeclareModifier(Node modifier)
    {
        return modifier.Kind == SyntaxKind.DeclareKeyword;
    }

    private Node ParseDeclaration()
    {
        // `parseListElement` attempted to get the reused node at this position,
        // but the ambient context flag was not yet set, so the node appeared
        // not reusable in that context.
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(/*allowDecorators*/ true);
        var isAmbient = Core.Some(modifiers, IsDeclareModifier);
        if (isAmbient)
        {
            var node = TryReuseAmbientDeclaration(pos);
            if (node != null)
            {
                return node;
            }

            foreach (var m in modifiers!)
            {
                m.Flags |= NodeFlags.Ambient;
            }
            // (C#: the closure captures copies declared in this block, so it is allocated only when this branch runs.)
            {
                var ambientPos = pos;
                var ambientHasJSDoc = hasJSDoc;
                var ambientModifiers = modifiers;
                return DoInsideOfContext(NodeFlags.Ambient, p => p.ParseDeclarationWorker(ambientPos, ambientHasJSDoc, ambientModifiers));
            }
        }
        else
        {
            return ParseDeclarationWorker(pos, hasJSDoc, modifiers);
        }
    }

    private Node? TryReuseAmbientDeclaration(int pos)
    {
        return DoInsideOfContext(NodeFlags.Ambient, p =>
        {
            // TODO(jakebailey): this is totally wrong; `parsingContext` is the result of ORing a bunch of `1 << ParsingContext.XYZ`.
            // The enum should really be a bunch of flags.
            var node = CurrentNode((ParsingContext)parsingContext, pos);
            if (node != null)
            {
                return ConsumeNode(node);
            }
            return null;
        });
    }

    private Node ParseDeclarationWorker(int pos, bool hasJSDoc, NodeArray? modifiersIn)
    {
        switch (Token())
        {
            case SyntaxKind.VarKeyword:
            case SyntaxKind.LetKeyword:
            case SyntaxKind.ConstKeyword:
            case SyntaxKind.UsingKeyword:
                return ParseVariableStatement(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.AwaitKeyword:
                if (!IsAwaitUsingDeclaration())
                {
                    break;
                }
                return ParseVariableStatement(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.FunctionKeyword:
                return ParseFunctionDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.ClassKeyword:
                return ParseClassDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.InterfaceKeyword:
                return ParseInterfaceDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.TypeKeyword:
                return ParseTypeAliasDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.EnumKeyword:
                return ParseEnumDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.GlobalKeyword:
            case SyntaxKind.ModuleKeyword:
            case SyntaxKind.NamespaceKeyword:
                return ParseModuleDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.ImportKeyword:
                return ParseImportDeclarationOrImportEqualsDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.ExportKeyword:
                NextToken();
                switch (Token())
                {
                    case SyntaxKind.DefaultKeyword:
                    case SyntaxKind.EqualsToken:
                        return ParseExportAssignment(pos, hasJSDoc, modifiersIn);
                    case SyntaxKind.AsKeyword:
                        return ParseNamespaceExportDeclaration(pos, hasJSDoc, modifiersIn);
                    default:
                        return ParseExportDeclaration(pos, hasJSDoc, modifiersIn);
                }
        }
        if (modifiersIn != null)
        {
            // We reached this point because we encountered decorators and/or modifiers and assumed a declaration
            // would follow. For recovery and error reporting purposes, return an incomplete declaration.
            var missing = CreateMissingNode<MissingDeclaration>(SyntaxKind.MissingDeclaration, /*reportAtCurrentPosition*/ true, Diagnostics.Declaration_expected);
            Utilities.SetTextRangePos(missing, pos);
            missing.Modifiers = modifiersIn;
            return missing;
        }
        return null!; // TODO: GH#18217
    }

    private bool NextTokenIsStringLiteral()
    {
        return NextToken() == SyntaxKind.StringLiteral;
    }

    private bool NextTokenIsFromKeywordOrEqualsToken()
    {
        NextToken();
        return Token() == SyntaxKind.FromKeyword || Token() == SyntaxKind.EqualsToken;
    }

    private bool NextTokenIsIdentifierOrStringLiteralOnSameLine()
    {
        NextToken();
        return !scanner.HasPrecedingLineBreak() && (IsIdentifier() || Token() == SyntaxKind.StringLiteral);
    }

    private Block? ParseFunctionBlockOrSemicolon(SignatureFlags flags, DiagnosticMessage? diagnosticMessage = null)
    {
        if (Token() != SyntaxKind.OpenBraceToken)
        {
            if ((flags & SignatureFlags.Type) != 0)
            {
                ParseTypeMemberSemicolon();
                return null;
            }
            if (CanParseSemicolon())
            {
                ParseSemicolon();
                return null;
            }
        }
        return ParseFunctionBlock(flags, diagnosticMessage);
    }
}
