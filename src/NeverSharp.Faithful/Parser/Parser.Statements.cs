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

// parser.ts lines 6823-8752 (namespace Parser): statements, declarations, classes, modules, imports and exports.
internal sealed partial class Parser
{
    // STATEMENTS
    internal Block ParseBlock(bool ignoreMissingOpenBrace, DiagnosticMessage? diagnosticMessage = null)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var openBracePosition = scanner.GetTokenStart();
        var openBraceParsed = ParseExpected(SyntaxKind.OpenBraceToken, diagnosticMessage);
        if (openBraceParsed || ignoreMissingOpenBrace)
        {
            var multiLine = scanner.HasPrecedingLineBreak();
            var statements = ParseList(ParsingContext.BlockStatements, ParseStatement);
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
            return WithJSDoc(FinishNode(factory.CreateBlock(statements, null), pos), hasJSDoc);
        }
    }

    internal Block ParseFunctionBlock(SignatureFlags flags, DiagnosticMessage? diagnosticMessage = null)
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
            SetDecoratorContext(false);
        }

        var block = ParseBlock((flags & SignatureFlags.IgnoreMissingOpenBrace) != 0, diagnosticMessage);

        if (saveDecoratorContext)
        {
            SetDecoratorContext(true);
        }

        topLevel = savedTopLevel;
        SetYieldContext(savedYieldContext);
        SetAwaitContext(savedAwaitContext);

        return block;
    }

    internal Node ParseEmptyStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.SemicolonToken);
        return WithJSDoc(FinishNode(factory.CreateEmptyStatement(), pos), hasJSDoc);
    }

    internal IfStatement ParseIfStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.IfKeyword);
        var openParenPosition = scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpectedMatchingBrackets(SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, openParenParsed, openParenPosition);
        var thenStatement = ParseStatement();
        var elseStatement = ParseOptional(SyntaxKind.ElseKeyword) ? ParseStatement() : null;
        return WithJSDoc(FinishNode(factory.CreateIfStatement(expression, thenStatement, elseStatement), pos), hasJSDoc);
    }

    internal DoStatement ParseDoStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.DoKeyword);
        var statement = ParseStatement();
        ParseExpected(SyntaxKind.WhileKeyword);
        var openParenPosition = scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpectedMatchingBrackets(SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, openParenParsed, openParenPosition);

        // From: https://mail.mozilla.org/pipermail/es-discuss/2011-August/016188.html
        // 157 min --- All allen at wirfs-brock.com CONF --- "do{;}while(false)false" prohibited in
        // spec but allowed in consensus reality. Approved -- this is the de-facto standard whereby
        //  do;while(0)x will have a semicolon inserted before x.
        ParseOptional(SyntaxKind.SemicolonToken);
        return WithJSDoc(FinishNode(factory.CreateDoStatement(statement, expression), pos), hasJSDoc);
    }

    internal WhileStatement ParseWhileStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.WhileKeyword);
        var openParenPosition = scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpectedMatchingBrackets(SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, openParenParsed, openParenPosition);
        var statement = ParseStatement();
        return WithJSDoc(FinishNode(factory.CreateWhileStatement(expression, statement), pos), hasJSDoc);
    }

    internal Node ParseForOrForInOrForOfStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.ForKeyword);
        var awaitToken = ParseOptionalToken<AwaitKeyword>(SyntaxKind.AwaitKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);

        Node initializer = null!; // VariableDeclarationList | Expression
        if (Token() != SyntaxKind.SemicolonToken)
        {
            if (
                Token() == SyntaxKind.VarKeyword || Token() == SyntaxKind.LetKeyword || Token() == SyntaxKind.ConstKeyword ||
                (Token() == SyntaxKind.UsingKeyword && LookAhead(NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOf)) ||
                // this one is meant to allow of
                (Token() == SyntaxKind.AwaitKeyword && LookAhead(() => NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine()))
            )
            {
                initializer = ParseVariableDeclarationList(true);
            }
            else
            {
                initializer = DisallowInAnd(ParseExpression);
            }
        }

        Node node; // IterationStatement
        if (awaitToken != null ? ParseExpected(SyntaxKind.OfKeyword) : ParseOptional(SyntaxKind.OfKeyword))
        {
            var expression = AllowInAnd(() => ParseAssignmentExpressionOrHigher(true));
            ParseExpected(SyntaxKind.CloseParenToken);
            node = factory.CreateForOfStatement(awaitToken, initializer, expression, ParseStatement());
        }
        else if (ParseOptional(SyntaxKind.InKeyword))
        {
            var expression = AllowInAnd(ParseExpression);
            ParseExpected(SyntaxKind.CloseParenToken);
            node = factory.CreateForInStatement(initializer, expression, ParseStatement());
        }
        else
        {
            ParseExpected(SyntaxKind.SemicolonToken);
            var condition = Token() != SyntaxKind.SemicolonToken && Token() != SyntaxKind.CloseParenToken
                ? AllowInAnd(ParseExpression)
                : null;
            ParseExpected(SyntaxKind.SemicolonToken);
            var incrementor = Token() != SyntaxKind.CloseParenToken
                ? AllowInAnd(ParseExpression)
                : null;
            ParseExpected(SyntaxKind.CloseParenToken);
            node = factory.CreateForStatement(initializer, condition, incrementor, ParseStatement());
        }

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal Node ParseBreakOrContinueStatement(SyntaxKind kind)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();

        ParseExpected(kind == SyntaxKind.BreakStatement ? SyntaxKind.BreakKeyword : SyntaxKind.ContinueKeyword);
        var label = CanParseSemicolon() ? null : ParseIdentifier();

        ParseSemicolon();
        Node node = kind == SyntaxKind.BreakStatement
            ? factory.CreateBreakStatement(label)
            : factory.CreateContinueStatement(label);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal ReturnStatement ParseReturnStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.ReturnKeyword);
        var expression = CanParseSemicolon() ? null : AllowInAnd(ParseExpression);
        ParseSemicolon();
        return WithJSDoc(FinishNode(factory.CreateReturnStatement(expression), pos), hasJSDoc);
    }

    internal WithStatement ParseWithStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.WithKeyword);
        var openParenPosition = scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpectedMatchingBrackets(SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, openParenParsed, openParenPosition);
        var statement = DoInsideOfContext(NodeFlags.InWithStatement, ParseStatement);
        return WithJSDoc(FinishNode(factory.CreateWithStatement(expression, statement), pos), hasJSDoc);
    }

    internal CaseClause ParseCaseClause()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.CaseKeyword);
        var expression = AllowInAnd(ParseExpression);
        ParseExpected(SyntaxKind.ColonToken);
        var statements = ParseList(ParsingContext.SwitchClauseStatements, ParseStatement);
        return WithJSDoc(FinishNode(factory.CreateCaseClause(expression, statements), pos), hasJSDoc);
    }

    internal DefaultClause ParseDefaultClause()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.DefaultKeyword);
        ParseExpected(SyntaxKind.ColonToken);
        var statements = ParseList(ParsingContext.SwitchClauseStatements, ParseStatement);
        return FinishNode(factory.CreateDefaultClause(statements), pos);
    }

    internal Node ParseCaseOrDefaultClause()
    {
        return Token() == SyntaxKind.CaseKeyword ? ParseCaseClause() : ParseDefaultClause();
    }

    internal CaseBlock ParseCaseBlock()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        var clauses = ParseList(ParsingContext.SwitchClauses, ParseCaseOrDefaultClause);
        ParseExpected(SyntaxKind.CloseBraceToken);
        return FinishNode(factory.CreateCaseBlock(clauses), pos);
    }

    internal SwitchStatement ParseSwitchStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.SwitchKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpected(SyntaxKind.CloseParenToken);
        var caseBlock = ParseCaseBlock();
        return WithJSDoc(FinishNode(factory.CreateSwitchStatement(expression, caseBlock), pos), hasJSDoc);
    }

    internal ThrowStatement ParseThrowStatement()
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
        Node? expression = scanner.HasPrecedingLineBreak() ? null : AllowInAnd(ParseExpression);
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
    internal TryStatement ParseTryStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();

        ParseExpected(SyntaxKind.TryKeyword);
        var tryBlock = ParseBlock(false);
        var catchClause = Token() == SyntaxKind.CatchKeyword ? ParseCatchClause() : null;

        // If we don't have a catch clause, then we must have a finally clause.  Try to parse
        // one out no matter what.
        Block? finallyBlock = null;
        if (catchClause == null || Token() == SyntaxKind.FinallyKeyword)
        {
            ParseExpected(SyntaxKind.FinallyKeyword, Diagnostics.catch_or_finally_expected);
            finallyBlock = ParseBlock(false);
        }

        return WithJSDoc(FinishNode(factory.CreateTryStatement(tryBlock, catchClause, finallyBlock), pos), hasJSDoc);
    }

    internal CatchClause ParseCatchClause()
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

        var block = ParseBlock(false);
        return FinishNode(factory.CreateCatchClause(variableDeclaration, block), pos);
    }

    internal Node ParseDebuggerStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.DebuggerKeyword);
        ParseSemicolon();
        return WithJSDoc(FinishNode(factory.CreateDebuggerStatement(), pos), hasJSDoc);
    }

    internal Node ParseExpressionOrLabeledStatement()
    {
        // Avoiding having to do the lookahead for a labeled statement by just trying to parse
        // out an expression, seeing if it is identifier and then seeing if it is followed by
        // a colon.
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        Node node; // ExpressionStatement | LabeledStatement
        var hasParen = Token() == SyntaxKind.OpenParenToken;
        var expression = AllowInAnd(ParseExpression);
        if (NodeTests.IsIdentifier(expression) && ParseOptional(SyntaxKind.ColonToken))
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

    internal bool NextTokenIsIdentifierOrKeywordOnSameLine()
    {
        NextToken();
        return Scanner.TokenIsIdentifierOrKeyword(Token()) && !scanner.HasPrecedingLineBreak();
    }

    internal bool NextTokenIsClassKeywordOnSameLine()
    {
        NextToken();
        return Token() == SyntaxKind.ClassKeyword && !scanner.HasPrecedingLineBreak();
    }

    internal bool NextTokenIsFunctionKeywordOnSameLine()
    {
        NextToken();
        return Token() == SyntaxKind.FunctionKeyword && !scanner.HasPrecedingLineBreak();
    }

    internal bool NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine()
    {
        NextToken();
        return (Scanner.TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.NumericLiteral || Token() == SyntaxKind.BigIntLiteral || Token() == SyntaxKind.StringLiteral) && !scanner.HasPrecedingLineBreak();
    }

    internal bool IsDeclaration()
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
                    var tok = NextToken();
                    if (tok == SyntaxKind.TypeKeyword)
                    {
                        tok = LookAhead(NextToken);
                    }
                    if (
                        tok == SyntaxKind.EqualsToken || tok == SyntaxKind.AsteriskToken ||
                        tok == SyntaxKind.OpenBraceToken || tok == SyntaxKind.DefaultKeyword ||
                        tok == SyntaxKind.AsKeyword || tok == SyntaxKind.AtToken
                    )
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

    internal bool IsStartOfDeclaration()
    {
        return LookAhead(IsDeclaration);
    }

    internal bool IsStartOfStatement()
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
                return IsStartOfDeclaration() || LookAhead(NextTokenIsOpenParenOrLessThanOrDot);

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
                return IsStartOfDeclaration() || !LookAhead(NextTokenIsIdentifierOrKeywordOnSameLine);

            default:
                return IsStartOfExpression();
        }
    }

    internal bool NextTokenIsBindingIdentifierOrStartOfDestructuring()
    {
        NextToken();
        return IsBindingIdentifier() || Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.OpenBracketToken;
    }

    internal bool IsLetDeclaration()
    {
        // In ES6 'let' always starts a lexical declaration if followed by an identifier or {
        // or [.
        return LookAhead(NextTokenIsBindingIdentifierOrStartOfDestructuring);
    }

    internal bool NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOf()
    {
        return NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(true);
    }

    internal bool NextTokenIsEqualsOrSemicolonOrColonToken()
    {
        NextToken();
        return Token() == SyntaxKind.EqualsToken || Token() == SyntaxKind.SemicolonToken || Token() == SyntaxKind.ColonToken;
    }

    internal bool NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(bool disallowOf = false)
    {
        NextToken();
        if (disallowOf && Token() == SyntaxKind.OfKeyword)
        {
            return LookAhead(NextTokenIsEqualsOrSemicolonOrColonToken);
        }
        return (IsBindingIdentifier() || Token() == SyntaxKind.OpenBraceToken) && !scanner.HasPrecedingLineBreak();
    }

    internal bool IsUsingDeclaration()
    {
        // 'using' always starts a lexical declaration if followed by an identifier. We also eagerly parse
        // |ObjectBindingPattern| so that we can report a grammar error during check. We don't parse out
        // |ArrayBindingPattern| since it potentially conflicts with element access (i.e., `using[x]`).
        return LookAhead(() => NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine());
    }

    internal bool NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine(bool disallowOf = false)
    {
        if (NextToken() == SyntaxKind.UsingKeyword)
        {
            return NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(disallowOf);
        }
        return false;
    }

    internal bool IsAwaitUsingDeclaration()
    {
        // 'await using' always starts a lexical declaration if followed by an identifier. We also eagerly parse
        // |ObjectBindingPattern| so that we can report a grammar error during check. We don't parse out
        // |ArrayBindingPattern| since it potentially conflicts with element access (i.e., `await using[x]`).
        return LookAhead(() => NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine());
    }

    internal Node ParseStatement()
    {
        switch (Token())
        {
            case SyntaxKind.SemicolonToken:
                return ParseEmptyStatement();
            case SyntaxKind.OpenBraceToken:
                return ParseBlock(false);
            case SyntaxKind.VarKeyword:
                return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), null);
            case SyntaxKind.LetKeyword:
                if (IsLetDeclaration())
                {
                    return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), null);
                }
                break;
            case SyntaxKind.AwaitKeyword:
                if (IsAwaitUsingDeclaration())
                {
                    return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), null);
                }
                break;
            case SyntaxKind.UsingKeyword:
                if (IsUsingDeclaration())
                {
                    return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), null);
                }
                break;
            case SyntaxKind.FunctionKeyword:
                return ParseFunctionDeclaration(GetNodePos(), HasPrecedingJSDocComment(), null);
            case SyntaxKind.ClassKeyword:
                return ParseClassDeclaration(GetNodePos(), HasPrecedingJSDocComment(), null);
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
            default:
                break;
        }
        return ParseExpressionOrLabeledStatement();
    }

    internal static bool IsDeclareModifier(Node modifier)
    {
        return modifier.Kind == SyntaxKind.DeclareKeyword;
    }

    internal Node ParseDeclaration()
    {
        // `parseListElement` attempted to get the reused node at this position,
        // but the ambient context flag was not yet set, so the node appeared
        // not reusable in that context.
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(true);
        var isAmbient = Core.Some<Node>(modifiers, IsDeclareModifier);
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
            return DoInsideOfContext(NodeFlags.Ambient, () => ParseDeclarationWorker(pos, hasJSDoc, modifiers));
        }
        else
        {
            return ParseDeclarationWorker(pos, hasJSDoc, modifiers);
        }
    }

    internal Node? TryReuseAmbientDeclaration(int pos)
    {
        return DoInsideOfContext<Node?>(NodeFlags.Ambient, () =>
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

    internal Node ParseDeclarationWorker(int pos, bool hasJSDoc, NodeArray? modifiersIn)
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
            default:
                break;
        }
        if (modifiersIn != null)
        {
            // We reached this point because we encountered decorators and/or modifiers and assumed a declaration
            // would follow. For recovery and error reporting purposes, return an incomplete declaration.
            var missing = CreateMissingNode<MissingDeclaration>(SyntaxKind.MissingDeclaration, true, Diagnostics.Declaration_expected);
            Utilities.SetTextRangePos(missing, pos);
            missing.Modifiers = modifiersIn;
            return missing;
        }
        return null!; // TODO: GH#18217
    }

    internal bool NextTokenIsStringLiteral()
    {
        return NextToken() == SyntaxKind.StringLiteral;
    }

    internal bool NextTokenIsFromKeywordOrEqualsToken()
    {
        NextToken();
        return Token() == SyntaxKind.FromKeyword || Token() == SyntaxKind.EqualsToken;
    }

    internal bool NextTokenIsIdentifierOrStringLiteralOnSameLine()
    {
        NextToken();
        return !scanner.HasPrecedingLineBreak() && (IsIdentifier() || Token() == SyntaxKind.StringLiteral);
    }

    internal Block? ParseFunctionBlockOrSemicolon(SignatureFlags flags, DiagnosticMessage? diagnosticMessage = null)
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

    // DECLARATIONS

    internal Node ParseArrayBindingElement()
    {
        var pos = GetNodePos();
        if (Token() == SyntaxKind.CommaToken)
        {
            return FinishNode(factory.CreateOmittedExpression(), pos);
        }
        var dotDotDotToken = ParseOptionalToken<DotDotDotToken>(SyntaxKind.DotDotDotToken);
        var name = ParseIdentifierOrPattern();
        var initializer = ParseInitializer();
        return FinishNode(factory.CreateBindingElement(dotDotDotToken, null, name, initializer), pos);
    }

    internal BindingElement ParseObjectBindingElement()
    {
        var pos = GetNodePos();
        var dotDotDotToken = ParseOptionalToken<DotDotDotToken>(SyntaxKind.DotDotDotToken);
        var tokenIsIdentifier = IsBindingIdentifier();
        Node? propertyName = ParsePropertyName();
        Node name;
        if (tokenIsIdentifier && Token() != SyntaxKind.ColonToken)
        {
            name = propertyName;
            propertyName = null;
        }
        else
        {
            ParseExpected(SyntaxKind.ColonToken);
            name = ParseIdentifierOrPattern();
        }
        var initializer = ParseInitializer();
        return FinishNode(factory.CreateBindingElement(dotDotDotToken, propertyName, name, initializer), pos);
    }

    internal ObjectBindingPattern ParseObjectBindingPattern()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        var elements = AllowInAnd(() => ParseDelimitedList(ParsingContext.ObjectBindingElements, ParseObjectBindingElement));
        ParseExpected(SyntaxKind.CloseBraceToken);
        return FinishNode(factory.CreateObjectBindingPattern(elements), pos);
    }

    internal ArrayBindingPattern ParseArrayBindingPattern()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBracketToken);
        var elements = AllowInAnd(() => ParseDelimitedList(ParsingContext.ArrayBindingElements, ParseArrayBindingElement));
        ParseExpected(SyntaxKind.CloseBracketToken);
        return FinishNode(factory.CreateArrayBindingPattern(elements), pos);
    }

    internal bool IsBindingIdentifierOrPrivateIdentifierOrPattern()
    {
        return Token() == SyntaxKind.OpenBraceToken
            || Token() == SyntaxKind.OpenBracketToken
            || Token() == SyntaxKind.PrivateIdentifier
            || IsBindingIdentifier();
    }

    internal Node ParseIdentifierOrPattern(DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        if (Token() == SyntaxKind.OpenBracketToken)
        {
            return ParseArrayBindingPattern();
        }
        if (Token() == SyntaxKind.OpenBraceToken)
        {
            return ParseObjectBindingPattern();
        }
        return ParseBindingIdentifier(privateIdentifierDiagnosticMessage);
    }

    internal VariableDeclaration ParseVariableDeclarationAllowExclamation()
    {
        return ParseVariableDeclaration(true);
    }

    internal VariableDeclaration ParseVariableDeclaration(bool allowExclamation = false)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var name = ParseIdentifierOrPattern(Diagnostics.Private_identifiers_are_not_allowed_in_variable_declarations);
        ExclamationToken? exclamationToken = null;
        if (
            allowExclamation && name.Kind == SyntaxKind.Identifier &&
            Token() == SyntaxKind.ExclamationToken && !scanner.HasPrecedingLineBreak()
        )
        {
            exclamationToken = ParseTokenNode<ExclamationToken>();
        }
        var type = ParseTypeAnnotation();
        var initializer = IsInOrOfKeyword(Token()) ? null : ParseInitializer();
        var node = factory.CreateVariableDeclaration(name, exclamationToken, type, initializer);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal VariableDeclarationList ParseVariableDeclarationList(bool inForStatementInitializer)
    {
        var pos = GetNodePos();

        var flags = NodeFlags.None;
        switch (Token())
        {
            case SyntaxKind.VarKeyword:
                break;
            case SyntaxKind.LetKeyword:
                flags |= NodeFlags.Let;
                break;
            case SyntaxKind.ConstKeyword:
                flags |= NodeFlags.Const;
                break;
            case SyntaxKind.UsingKeyword:
                flags |= NodeFlags.Using;
                break;
            case SyntaxKind.AwaitKeyword:
                if (!IsAwaitUsingDeclaration())
                {
                    break;
                }
                flags |= NodeFlags.AwaitUsing;
                NextToken();
                break;
            default:
                Debug.Fail();
                break;
        }

        NextToken();

        // The user may have written the following:
        //
        //    for (let of X) { }
        //
        // In this case, we want to parse an empty declaration list, and then parse 'of'
        // as a keyword. The reason this is not automatic is that 'of' is a valid identifier.
        // So we need to look ahead to determine if 'of' should be treated as a keyword in
        // this context.
        // The checker will then give an error that there is an empty declaration list.
        NodeArray declarations;
        if (Token() == SyntaxKind.OfKeyword && LookAhead(CanFollowContextualOfKeyword))
        {
            declarations = CreateMissingList();
        }
        else
        {
            var savedDisallowIn = InDisallowInContext();
            SetDisallowInContext(inForStatementInitializer);

            Func<Node> parseElement;
            if (inForStatementInitializer)
            {
                parseElement = () => ParseVariableDeclaration();
            }
            else
            {
                parseElement = ParseVariableDeclarationAllowExclamation;
            }
            declarations = ParseDelimitedList(ParsingContext.VariableDeclarations, parseElement);

            SetDisallowInContext(savedDisallowIn);
        }

        return FinishNode(factory.CreateVariableDeclarationList(declarations, flags), pos);
    }

    internal bool CanFollowContextualOfKeyword()
    {
        return NextTokenIsIdentifier() && NextToken() == SyntaxKind.CloseParenToken;
    }

    internal VariableStatement ParseVariableStatement(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        var declarationList = ParseVariableDeclarationList(false);
        ParseSemicolon();
        var node = factory.CreateVariableStatement(modifiers, declarationList);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal FunctionDeclaration ParseFunctionDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        var savedAwaitContext = InAwaitContext();
        var modifierFlags = Utilities.ModifiersToFlags(modifiers);
        ParseExpected(SyntaxKind.FunctionKeyword);
        var asteriskToken = ParseOptionalToken<AsteriskToken>(SyntaxKind.AsteriskToken);
        // We don't parse the name here in await context, instead we will report a grammar error in the checker.
        Identifier? name = (modifierFlags & ModifierFlags.Default) != 0 ? ParseOptionalBindingIdentifier() : ParseBindingIdentifier();
        var isGenerator = asteriskToken != null ? SignatureFlags.Yield : SignatureFlags.None;
        var isAsync = (modifierFlags & ModifierFlags.Async) != 0 ? SignatureFlags.Await : SignatureFlags.None;
        var typeParameters = ParseTypeParameters();
        if ((modifierFlags & ModifierFlags.Export) != 0)
        {
            SetAwaitContext(true);
        }
        var parameters = ParseParameters(isGenerator | isAsync);
        var type = ParseReturnType(SyntaxKind.ColonToken, false);
        var body = ParseFunctionBlockOrSemicolon(isGenerator | isAsync, Diagnostics.or_expected);
        SetAwaitContext(savedAwaitContext);
        var node = factory.CreateFunctionDeclaration(modifiers, asteriskToken, name, typeParameters, parameters, type, body);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    // TypeScript returns `boolean | LiteralExpression | undefined` and only the truthiness is used.
    internal bool ParseConstructorName()
    {
        if (Token() == SyntaxKind.ConstructorKeyword)
        {
            return ParseExpected(SyntaxKind.ConstructorKeyword);
        }
        if (Token() == SyntaxKind.StringLiteral && LookAhead(NextToken) == SyntaxKind.OpenParenToken)
        {
            return TryParse<StringLiteral?>(() =>
            {
                var literalNode = (StringLiteral)ParseLiteralNode();
                return literalNode.Text == "constructor" ? literalNode : null;
            }) != null;
        }
        return false;
    }

    internal ConstructorDeclaration? TryParseConstructorDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        return TryParse<ConstructorDeclaration?>(() =>
        {
            if (ParseConstructorName())
            {
                var typeParameters = ParseTypeParameters();
                var parameters = ParseParameters(SignatureFlags.None);
                var type = ParseReturnType(SyntaxKind.ColonToken, false);
                var body = ParseFunctionBlockOrSemicolon(SignatureFlags.None, Diagnostics.or_expected);
                var node = factory.CreateConstructorDeclaration(modifiers, parameters, body);

                // Attach invalid nodes if they exist so that we can report them in the grammar checker.
                node.TypeParameters = typeParameters;
                node.Type = type;
                return WithJSDoc(FinishNode(node, pos), hasJSDoc);
            }
            return null;
        });
    }

    internal MethodDeclaration ParseMethodDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray? modifiers,
        AsteriskToken? asteriskToken,
        Node name,
        QuestionToken? questionToken,
        ExclamationToken? exclamationToken,
        DiagnosticMessage? diagnosticMessage = null)
    {
        var isGenerator = asteriskToken != null ? SignatureFlags.Yield : SignatureFlags.None;
        var isAsync = Core.Some<Node>(modifiers, NodeTests.IsAsyncModifier) ? SignatureFlags.Await : SignatureFlags.None;
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(isGenerator | isAsync);
        var type = ParseReturnType(SyntaxKind.ColonToken, false);
        var body = ParseFunctionBlockOrSemicolon(isGenerator | isAsync, diagnosticMessage);
        var node = factory.CreateMethodDeclaration(
            modifiers,
            asteriskToken,
            name,
            questionToken,
            typeParameters,
            parameters,
            type,
            body);

        // An exclamation token on a method is invalid syntax and will be handled by the grammar checker
        node.ExclamationToken = exclamationToken;
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal PropertyDeclaration ParsePropertyDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray? modifiers,
        Node name,
        QuestionToken? questionToken)
    {
        var exclamationToken = questionToken == null && !scanner.HasPrecedingLineBreak() ? ParseOptionalToken<ExclamationToken>(SyntaxKind.ExclamationToken) : null;
        var type = ParseTypeAnnotation();
        var initializer = DoOutsideOfContext(NodeFlags.YieldContext | NodeFlags.AwaitContext | NodeFlags.DisallowInContext, ParseInitializer);
        ParseSemicolonAfterPropertyName(name, type, initializer);
        var node = factory.CreatePropertyDeclaration(
            modifiers,
            name,
            (Node?)questionToken ?? exclamationToken,
            type,
            initializer);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal Node ParsePropertyOrMethodDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray? modifiers)
    {
        var asteriskToken = ParseOptionalToken<AsteriskToken>(SyntaxKind.AsteriskToken);
        var name = ParsePropertyName();
        // Note: this is not legal as per the grammar.  But we allow it in the parser and
        // report an error in the grammar checker.
        var questionToken = ParseOptionalToken<QuestionToken>(SyntaxKind.QuestionToken);
        if (asteriskToken != null || Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            return ParseMethodDeclaration(pos, hasJSDoc, modifiers, asteriskToken, name, questionToken, null, Diagnostics.or_expected);
        }
        return ParsePropertyDeclaration(pos, hasJSDoc, modifiers, name, questionToken);
    }

    // `kind` is SyntaxKind.GetAccessor or SyntaxKind.SetAccessor; the result is a GetAccessorDeclaration or a SetAccessorDeclaration.
    internal Node ParseAccessorDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers, SyntaxKind kind, SignatureFlags flags)
    {
        var name = ParsePropertyName();
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(SignatureFlags.None);
        var type = ParseReturnType(SyntaxKind.ColonToken, false);
        var body = ParseFunctionBlockOrSemicolon(flags);
        Node node;
        if (kind == SyntaxKind.GetAccessor)
        {
            var getAccessor = factory.CreateGetAccessorDeclaration(modifiers, name, parameters, type, body);
            // Keep track of `typeParameters` (for both) and `type` (for setters) if they were parsed those indicate grammar errors
            getAccessor.TypeParameters = typeParameters;
            node = getAccessor;
        }
        else
        {
            var setAccessor = factory.CreateSetAccessorDeclaration(modifiers, name, parameters, body);
            // Keep track of `typeParameters` (for both) and `type` (for setters) if they were parsed those indicate grammar errors
            setAccessor.TypeParameters = typeParameters;
            setAccessor.Type = type; // isSetAccessorDeclaration(node)
            node = setAccessor;
        }
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal bool IsClassMemberStart()
    {
        SyntaxKind? idToken = null;

        if (Token() == SyntaxKind.AtToken)
        {
            return true;
        }

        // Eat up all modifiers, but hold on to the last one in case it is actually an identifier.
        while (UtilitiesPublic.IsModifierKind(Token()))
        {
            idToken = Token();
            // If the idToken is a class modifier (protected, private, public, and static), it is
            // certain that we are starting to parse class member. This allows better error recovery
            // Example:
            //      public foo() ...     // true
            //      public @dec blah ... // true; we will then report an error later
            //      export public ...    // true; we will then report an error later
            if (UtilitiesPublic.IsClassMemberModifier(idToken.Value))
            {
                return true;
            }

            NextToken();
        }

        if (Token() == SyntaxKind.AsteriskToken)
        {
            return true;
        }

        // Try to get the first property-like token following all modifiers.
        // This can either be an identifier or the 'get' or 'set' keywords.
        if (IsLiteralPropertyName())
        {
            idToken = Token();
            NextToken();
        }

        // Index signatures and computed properties are class members; we can parse.
        if (Token() == SyntaxKind.OpenBracketToken)
        {
            return true;
        }

        // If we were able to get any potential identifier...
        if (idToken != null)
        {
            // If we have a non-keyword identifier, or if we have an accessor, then it's safe to parse.
            if (!Utilities.IsKeyword(idToken.Value) || idToken == SyntaxKind.SetKeyword || idToken == SyntaxKind.GetKeyword)
            {
                return true;
            }

            // If it *is* a keyword, but not an accessor, check a little farther along
            // to see if it should actually be parsed as a class member.
            switch (Token())
            {
                case SyntaxKind.OpenParenToken: // Method declaration
                case SyntaxKind.LessThanToken: // Generic Method declaration
                case SyntaxKind.ExclamationToken: // Non-null assertion on property name
                case SyntaxKind.ColonToken: // Type Annotation for declaration
                case SyntaxKind.EqualsToken: // Initializer for declaration
                case SyntaxKind.QuestionToken: // Not valid, but permitted so that it gets caught later on.
                    return true;
                default:
                    // Covers
                    //  - Semicolons     (declaration termination)
                    //  - Closing braces (end-of-class, must be declaration)
                    //  - End-of-files   (not valid, but permitted so that it gets caught later on)
                    //  - Line-breaks    (enabling *automatic semicolon insertion*)
                    return CanParseSemicolon();
            }
        }

        return false;
    }

    internal ClassStaticBlockDeclaration ParseClassStaticBlockDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        ParseExpectedToken<Node>(SyntaxKind.StaticKeyword);
        var body = ParseClassStaticBlockBody();
        var node = WithJSDoc(FinishNode(factory.CreateClassStaticBlockDeclaration(body), pos), hasJSDoc);
        node.Modifiers = modifiers;
        return node;
    }

    internal Block ParseClassStaticBlockBody()
    {
        var savedYieldContext = InYieldContext();
        var savedAwaitContext = InAwaitContext();

        SetYieldContext(false);
        SetAwaitContext(true);

        var body = ParseBlock(false);

        SetYieldContext(savedYieldContext);
        SetAwaitContext(savedAwaitContext);

        return body;
    }

    internal Node ParseDecoratorExpression()
    {
        if (InAwaitContext() && Token() == SyntaxKind.AwaitKeyword)
        {
            // `@await` is is disallowed in an [Await] context, but can cause parsing to go off the rails
            // This simply parses the missing identifier and moves on.
            var pos = GetNodePos();
            var awaitExpression = ParseIdentifier(Diagnostics.Expression_expected);
            NextToken();
            var memberExpression = ParseMemberExpressionRest(pos, awaitExpression, true);
            return ParseCallExpressionRest(pos, memberExpression);
        }
        return ParseLeftHandSideExpressionOrHigher();
    }

    internal Decorator? TryParseDecorator()
    {
        var pos = GetNodePos();
        if (!ParseOptional(SyntaxKind.AtToken))
        {
            return null;
        }
        var expression = DoInDecoratorContext(ParseDecoratorExpression);
        return FinishNode(factory.CreateDecorator(expression), pos);
    }

    internal Node? TryParseModifier(bool hasSeenStaticModifier, bool permitConstAsModifier = false, bool stopOnStartOfClassStaticBlock = false)
    {
        var pos = GetNodePos();
        var kind = Token();

        if (Token() == SyntaxKind.ConstKeyword && permitConstAsModifier)
        {
            // We need to ensure that any subsequent modifiers appear on the same line
            // so that when 'const' is a standalone declaration, we don't issue an error.
            if (!TryParse(NextTokenIsOnSameLineAndCanFollowModifier))
            {
                return null;
            }
        }
        else if (stopOnStartOfClassStaticBlock && Token() == SyntaxKind.StaticKeyword && LookAhead(NextTokenIsOpenBrace))
        {
            return null;
        }
        else if (hasSeenStaticModifier && Token() == SyntaxKind.StaticKeyword)
        {
            return null;
        }
        else
        {
            if (!ParseAnyContextualModifier())
            {
                return null;
            }
        }

        return FinishNode(factory.CreateToken(kind), pos);
    }

    /*
     * There are situations in which a modifier like 'const' will appear unexpectedly, such as on a class member.
     * In those situations, if we are entirely sure that 'const' is not valid on its own (such as when ASI takes effect
     * and turns it into a standalone declaration), then it is better to parse it and report an error later.
     *
     * In such situations, 'permitConstAsModifier' should be set to true.
     */
    // The TypeScript overloads (allowDecorators false: NodeArray<Modifier>, true: NodeArray<ModifierLike>) are one method.
    internal NodeArray? ParseModifiers(bool allowDecorators, bool permitConstAsModifier = false, bool stopOnStartOfClassStaticBlock = false)
    {
        var pos = GetNodePos();
        List<Node>? list = null;
        Node? decorator;
        Node? modifier;
        var hasSeenStaticModifier = false;
        var hasLeadingModifier = false;
        var hasTrailingDecorator = false;

        // Decorators should be contiguous in a list of modifiers but can potentially appear in two places (i.e., `[...leadingDecorators, ...leadingModifiers, ...trailingDecorators, ...trailingModifiers]`).
        // The leading modifiers *should* only contain `export` and `default` when trailingDecorators are present, but we'll handle errors for any other leading modifiers in the checker.
        // It is illegal to have both leadingDecorators and trailingDecorators, but we will report that as a grammar check in the checker.

        // parse leading decorators
        if (allowDecorators && Token() == SyntaxKind.AtToken)
        {
            while ((decorator = TryParseDecorator()) != null)
            {
                list = Core.Append(list, decorator);
            }
        }

        // parse leading modifiers
        while ((modifier = TryParseModifier(hasSeenStaticModifier, permitConstAsModifier, stopOnStartOfClassStaticBlock)) != null)
        {
            if (modifier.Kind == SyntaxKind.StaticKeyword)
            {
                hasSeenStaticModifier = true;
            }
            list = Core.Append(list, modifier);
            hasLeadingModifier = true;
        }

        // parse trailing decorators, but only if we parsed any leading modifiers
        if (hasLeadingModifier && allowDecorators && Token() == SyntaxKind.AtToken)
        {
            while ((decorator = TryParseDecorator()) != null)
            {
                list = Core.Append(list, decorator);
                hasTrailingDecorator = true;
            }
        }

        // parse trailing modifiers, but only if we parsed any trailing decorators
        if (hasTrailingDecorator)
        {
            while ((modifier = TryParseModifier(hasSeenStaticModifier, permitConstAsModifier, stopOnStartOfClassStaticBlock)) != null)
            {
                if (modifier.Kind == SyntaxKind.StaticKeyword)
                {
                    hasSeenStaticModifier = true;
                }
                list = Core.Append(list, modifier);
            }
        }

        return list != null ? CreateNodeArray(list, pos) : null;
    }

    internal NodeArray? ParseModifiersForArrowFunction()
    {
        NodeArray? modifiers = null;
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            var pos = GetNodePos();
            NextToken();
            var modifier = FinishNode(factory.CreateToken(SyntaxKind.AsyncKeyword), pos);
            modifiers = CreateNodeArray(new List<Node> { modifier }, pos);
        }
        return modifiers;
    }

    internal Node ParseClassElement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        if (Token() == SyntaxKind.SemicolonToken)
        {
            NextToken();
            return WithJSDoc(FinishNode(factory.CreateSemicolonClassElement(), pos), hasJSDoc);
        }

        var modifiers = ParseModifiers(true, true, true);
        if (Token() == SyntaxKind.StaticKeyword && LookAhead(NextTokenIsOpenBrace))
        {
            return ParseClassStaticBlockDeclaration(pos, hasJSDoc, modifiers);
        }

        if (ParseContextualModifier(SyntaxKind.GetKeyword))
        {
            return ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.GetAccessor, SignatureFlags.None);
        }

        if (ParseContextualModifier(SyntaxKind.SetKeyword))
        {
            return ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.SetAccessor, SignatureFlags.None);
        }

        if (Token() == SyntaxKind.ConstructorKeyword || Token() == SyntaxKind.StringLiteral)
        {
            var constructorDeclaration = TryParseConstructorDeclaration(pos, hasJSDoc, modifiers);
            if (constructorDeclaration != null)
            {
                return constructorDeclaration;
            }
        }

        if (IsIndexSignature())
        {
            return ParseIndexSignatureDeclaration(pos, hasJSDoc, modifiers);
        }

        // It is very important that we check this *after* checking indexers because
        // the [ token can start an index signature or a computed property name
        if (
            Scanner.TokenIsIdentifierOrKeyword(Token()) ||
            Token() == SyntaxKind.StringLiteral ||
            Token() == SyntaxKind.NumericLiteral ||
            Token() == SyntaxKind.BigIntLiteral ||
            Token() == SyntaxKind.AsteriskToken ||
            Token() == SyntaxKind.OpenBracketToken
        )
        {
            var isAmbient = Core.Some<Node>(modifiers, IsDeclareModifier);
            if (isAmbient)
            {
                foreach (var m in modifiers!)
                {
                    m.Flags |= NodeFlags.Ambient;
                }
                return DoInsideOfContext(NodeFlags.Ambient, () => ParsePropertyOrMethodDeclaration(pos, hasJSDoc, modifiers));
            }
            else
            {
                return ParsePropertyOrMethodDeclaration(pos, hasJSDoc, modifiers);
            }
        }

        if (modifiers != null)
        {
            // treat this as a property declaration with a missing name.
            var name = CreateMissingNode<Identifier>(SyntaxKind.Identifier, true, Diagnostics.Declaration_expected);
            return ParsePropertyDeclaration(pos, hasJSDoc, modifiers, name, null);
        }

        // 'isClassMemberStart' should have hinted not to attempt parsing.
        Debug.Fail("Should not have attempted to parse class member declaration.");
        return null!;
    }

    internal Node ParseDecoratedExpression()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(true);
        if (Token() == SyntaxKind.ClassKeyword)
        {
            return ParseClassDeclarationOrExpression(pos, hasJSDoc, modifiers, SyntaxKind.ClassExpression);
        }

        var missing = CreateMissingNode<MissingDeclaration>(SyntaxKind.MissingDeclaration, true, Diagnostics.Expression_expected);
        Utilities.SetTextRangePos(missing, pos);
        missing.Modifiers = modifiers;
        return missing;
    }

    internal ClassExpression ParseClassExpression()
    {
        return (ClassExpression)ParseClassDeclarationOrExpression(GetNodePos(), HasPrecedingJSDocComment(), null, SyntaxKind.ClassExpression);
    }

    internal ClassDeclaration ParseClassDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        return (ClassDeclaration)ParseClassDeclarationOrExpression(pos, hasJSDoc, modifiers, SyntaxKind.ClassDeclaration);
    }

    // `kind` is SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression; the result is a ClassDeclaration or a ClassExpression.
    internal Node ParseClassDeclarationOrExpression(int pos, bool hasJSDoc, NodeArray? modifiers, SyntaxKind kind)
    {
        var savedAwaitContext = InAwaitContext();
        ParseExpected(SyntaxKind.ClassKeyword);

        // We don't parse the name here in await context, instead we will report a grammar error in the checker.
        var name = ParseNameOfClassDeclarationOrExpression();
        var typeParameters = ParseTypeParameters();
        if (Core.Some<Node>(modifiers, NodeTests.IsExportModifier))
        {
            SetAwaitContext(true);
        }
        var heritageClauses = ParseHeritageClauses();

        NodeArray members;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            // ClassTail[Yield,Await] : (Modified) See 14.5
            //      ClassHeritage[?Yield,?Await]opt { ClassBody[?Yield,?Await]opt }
            members = ParseClassMembers();
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            members = CreateMissingList();
        }
        SetAwaitContext(savedAwaitContext);
        Node node = kind == SyntaxKind.ClassDeclaration
            ? factory.CreateClassDeclaration(modifiers, name, typeParameters, heritageClauses, members)
            : factory.CreateClassExpression(modifiers, name, typeParameters, heritageClauses, members);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal Identifier? ParseNameOfClassDeclarationOrExpression()
    {
        // implements is a future reserved word so
        // 'class implements' might mean either
        // - class expression with omitted name, 'implements' starts heritage clause
        // - class with name 'implements'
        // 'isImplementsClause' helps to disambiguate between these two cases
        return IsBindingIdentifier() && !IsImplementsClause()
            ? CreateIdentifier(IsBindingIdentifier())
            : null;
    }

    internal bool IsImplementsClause()
    {
        return Token() == SyntaxKind.ImplementsKeyword && LookAhead(NextTokenIsIdentifierOrKeyword);
    }

    internal NodeArray? ParseHeritageClauses()
    {
        // ClassTail[Yield,Await] : (Modified) See 14.5
        //      ClassHeritage[?Yield,?Await]opt { ClassBody[?Yield,?Await]opt }

        if (IsHeritageClause())
        {
            return ParseList(ParsingContext.HeritageClauses, ParseHeritageClause);
        }

        return null;
    }

    internal HeritageClause ParseHeritageClause()
    {
        var pos = GetNodePos();
        var tok = Token();
        Debug.Assert(tok == SyntaxKind.ExtendsKeyword || tok == SyntaxKind.ImplementsKeyword); // isListElement() should ensure this.
        NextToken();
        var types = ParseDelimitedList(ParsingContext.HeritageClauseElement, ParseExpressionWithTypeArguments);
        return FinishNode(factory.CreateHeritageClause(tok, types), pos);
    }

    internal ExpressionWithTypeArguments ParseExpressionWithTypeArguments()
    {
        var pos = GetNodePos();
        var expression = ParseLeftHandSideExpressionOrHigher();
        if (expression.Kind == SyntaxKind.ExpressionWithTypeArguments)
        {
            return (ExpressionWithTypeArguments)expression;
        }
        var typeArguments = TryParseTypeArguments();
        return FinishNode(factory.CreateExpressionWithTypeArguments(expression, typeArguments), pos);
    }

    internal NodeArray? TryParseTypeArguments()
    {
        return Token() == SyntaxKind.LessThanToken ?
            ParseBracketedList(ParsingContext.TypeArguments, ParseType, SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken) : null;
    }

    internal bool IsHeritageClause()
    {
        return Token() == SyntaxKind.ExtendsKeyword || Token() == SyntaxKind.ImplementsKeyword;
    }

    internal NodeArray ParseClassMembers()
    {
        return ParseList(ParsingContext.ClassMembers, ParseClassElement);
    }

    internal InterfaceDeclaration ParseInterfaceDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        ParseExpected(SyntaxKind.InterfaceKeyword);
        var name = ParseIdentifier();
        var typeParameters = ParseTypeParameters();
        var heritageClauses = ParseHeritageClauses();
        var members = ParseObjectTypeMembers();
        var node = factory.CreateInterfaceDeclaration(modifiers, name, typeParameters, heritageClauses, members);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal TypeAliasDeclaration ParseTypeAliasDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        ParseExpected(SyntaxKind.TypeKeyword);
        if (scanner.HasPrecedingLineBreak())
        {
            ParseErrorAtCurrentToken(Diagnostics.Line_break_not_permitted_here);
        }
        var name = ParseIdentifier();
        var typeParameters = ParseTypeParameters();
        ParseExpected(SyntaxKind.EqualsToken);
        // `token() === IntrinsicKeyword && tryParse(parseKeywordAndNoDot) || parseType()`
        var type = (Token() == SyntaxKind.IntrinsicKeyword ? TryParse<Node?>(ParseKeywordAndNoDot) : null) ?? ParseType();
        ParseSemicolon();
        var node = factory.CreateTypeAliasDeclaration(modifiers, name, typeParameters, type);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    // In an ambient declaration, the grammar only allows integer literals as initializers.
    // In a non-ambient declaration, the grammar allows uninitialized members only in a
    // ConstantEnumMemberSection, which starts at the beginning of an enum declaration
    // or any time an integer literal initializer is encountered.
    internal EnumMember ParseEnumMember()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var name = ParsePropertyName();
        var initializer = AllowInAnd(ParseInitializer);
        return WithJSDoc(FinishNode(factory.CreateEnumMember(name, initializer), pos), hasJSDoc);
    }

    internal EnumDeclaration ParseEnumDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        ParseExpected(SyntaxKind.EnumKeyword);
        var name = ParseIdentifier();
        NodeArray members;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            members = DoOutsideOfYieldAndAwaitContext(() => ParseDelimitedList(ParsingContext.EnumMembers, ParseEnumMember));
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            members = CreateMissingList();
        }
        var node = factory.CreateEnumDeclaration(modifiers, name, members);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal ModuleBlock ParseModuleBlock()
    {
        var pos = GetNodePos();
        NodeArray statements;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            statements = ParseList(ParsingContext.BlockStatements, ParseStatement);
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            statements = CreateMissingList();
        }
        return FinishNode(factory.CreateModuleBlock(statements), pos);
    }

    internal ModuleDeclaration ParseModuleOrNamespaceDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers, NodeFlags flags)
    {
        // If we are parsing a dotted namespace name, we want to
        // propagate the 'Namespace' flag across the names if set.
        var namespaceFlag = flags & NodeFlags.Namespace;
        var name = (flags & NodeFlags.NestedNamespace) != 0 ? ParseIdentifierName() : ParseIdentifier();
        Node body = ParseOptional(SyntaxKind.DotToken)
            ? ParseModuleOrNamespaceDeclaration(GetNodePos(), false, null, NodeFlags.NestedNamespace | namespaceFlag)
            : ParseModuleBlock();
        var node = factory.CreateModuleDeclaration(modifiers, name, body, flags);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal ModuleDeclaration ParseAmbientExternalModuleDeclaration(int pos, bool hasJSDoc, NodeArray? modifiersIn)
    {
        var flags = NodeFlags.None;
        Node name;
        if (Token() == SyntaxKind.GlobalKeyword)
        {
            // parse 'global' as name of global scope augmentation
            name = ParseIdentifier();
            flags |= NodeFlags.GlobalAugmentation;
        }
        else
        {
            var literal = (StringLiteral)ParseLiteralNode();
            literal.Text = InternIdentifier(literal.Text);
            name = literal;
        }
        ModuleBlock? body = null;
        if (Token() == SyntaxKind.OpenBraceToken)
        {
            body = ParseModuleBlock();
        }
        else
        {
            ParseSemicolon();
        }
        var node = factory.CreateModuleDeclaration(modifiersIn, name, body, flags);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal ModuleDeclaration ParseModuleDeclaration(int pos, bool hasJSDoc, NodeArray? modifiersIn)
    {
        var flags = NodeFlags.None;
        if (Token() == SyntaxKind.GlobalKeyword)
        {
            // global augmentation
            return ParseAmbientExternalModuleDeclaration(pos, hasJSDoc, modifiersIn);
        }
        else if (ParseOptional(SyntaxKind.NamespaceKeyword))
        {
            flags |= NodeFlags.Namespace;
        }
        else
        {
            ParseExpected(SyntaxKind.ModuleKeyword);
            if (Token() == SyntaxKind.StringLiteral)
            {
                return ParseAmbientExternalModuleDeclaration(pos, hasJSDoc, modifiersIn);
            }
        }
        return ParseModuleOrNamespaceDeclaration(pos, hasJSDoc, modifiersIn, flags);
    }

    internal bool IsExternalModuleReference()
    {
        return Token() == SyntaxKind.RequireKeyword &&
            LookAhead(NextTokenIsOpenParen);
    }

    internal bool NextTokenIsOpenParen()
    {
        return NextToken() == SyntaxKind.OpenParenToken;
    }

    internal bool NextTokenIsOpenBrace()
    {
        return NextToken() == SyntaxKind.OpenBraceToken;
    }

    internal bool NextTokenIsSlash()
    {
        return NextToken() == SyntaxKind.SlashToken;
    }

    internal NamespaceExportDeclaration ParseNamespaceExportDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        ParseExpected(SyntaxKind.AsKeyword);
        ParseExpected(SyntaxKind.NamespaceKeyword);
        var name = ParseIdentifier();
        ParseSemicolon();
        var node = factory.CreateNamespaceExportDeclaration(name);
        // NamespaceExportDeclaration nodes cannot have decorators or modifiers, so we attach them here so we can report them in the grammar checker
        node.Modifiers = modifiers;
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    // The escapedText of an identifier is a string (the generator declares the field `object?`).
    private static bool EscapedTextIs(Identifier? identifier, string text)
    {
        return identifier != null && (string?)identifier.EscapedText == text;
    }

    internal Node ParseImportDeclarationOrImportEqualsDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        ParseExpected(SyntaxKind.ImportKeyword);

        var afterImportPos = scanner.GetTokenFullStart();

        // We don't parse the identifier here in await context, instead we will report a grammar error in the checker.
        Identifier? identifier = null;
        if (IsIdentifier())
        {
            identifier = ParseIdentifier();
        }

        SyntaxKind? phaseModifier = null; // ImportPhaseModifierSyntaxKind
        if (
            EscapedTextIs(identifier, "type") &&
            (Token() != SyntaxKind.FromKeyword || (IsIdentifier() && LookAhead(NextTokenIsFromKeywordOrEqualsToken))) &&
            (IsIdentifier() || TokenAfterImportDefinitelyProducesImportDeclaration())
        )
        {
            phaseModifier = SyntaxKind.TypeKeyword;
            identifier = IsIdentifier() ? ParseIdentifier() : null;
        }
        else if (
            EscapedTextIs(identifier, "defer") &&
            (Token() == SyntaxKind.FromKeyword ? !LookAhead(NextTokenIsStringLiteral) : Token() != SyntaxKind.CommaToken && Token() != SyntaxKind.EqualsToken)
        )
        {
            phaseModifier = SyntaxKind.DeferKeyword;
            identifier = IsIdentifier() ? ParseIdentifier() : null;
        }

        if (identifier != null && !TokenAfterImportedIdentifierDefinitelyProducesImportDeclaration() && phaseModifier != SyntaxKind.DeferKeyword)
        {
            return ParseImportEqualsDeclaration(pos, hasJSDoc, modifiers, identifier, phaseModifier == SyntaxKind.TypeKeyword);
        }

        var importClause = TryParseImportClause(identifier, afterImportPos, phaseModifier, false);
        var moduleSpecifier = ParseModuleSpecifier();
        var attributes = TryParseImportAttributes();

        ParseSemicolon();
        var node = factory.CreateImportDeclaration(modifiers, importClause, moduleSpecifier, attributes);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal ImportClause? TryParseImportClause(Identifier? identifier, int pos, SyntaxKind? phaseModifier, bool skipJsDocLeadingAsterisks = false)
    {
        // ImportDeclaration:
        //  import ImportClause from ModuleSpecifier ;
        //  import ModuleSpecifier;
        ImportClause? importClause = null;
        if (
            identifier != null || // import id
            Token() == SyntaxKind.AsteriskToken || // import *
            Token() == SyntaxKind.OpenBraceToken // import {
        )
        {
            importClause = ParseImportClause(identifier, pos, phaseModifier, skipJsDocLeadingAsterisks);
            ParseExpected(SyntaxKind.FromKeyword);
        }
        return importClause;
    }

    internal ImportAttributes? TryParseImportAttributes()
    {
        var currentTok = Token();
        if (currentTok == SyntaxKind.WithKeyword || (currentTok == SyntaxKind.AssertKeyword && !scanner.HasPrecedingLineBreak()))
        {
            return ParseImportAttributes(currentTok);
        }
        return null;
    }

    internal ImportAttribute ParseImportAttribute()
    {
        var pos = GetNodePos();
        Node name = Scanner.TokenIsIdentifierOrKeyword(Token()) ? ParseIdentifierName() : ParseLiteralLikeNode(SyntaxKind.StringLiteral);
        ParseExpected(SyntaxKind.ColonToken);
        var value = ParseAssignmentExpressionOrHigher(true);
        return FinishNode(factory.CreateImportAttribute(name, value), pos);
    }

    // `token` is SyntaxKind.AssertKeyword or SyntaxKind.WithKeyword; TypeScript's `skipKeyword?: true` is a bool.
    internal ImportAttributes ParseImportAttributes(SyntaxKind token, bool skipKeyword = false)
    {
        var pos = GetNodePos();
        if (!skipKeyword)
        {
            ParseExpected(token);
        }
        var openBracePosition = scanner.GetTokenStart();
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            var multiLine = scanner.HasPrecedingLineBreak();
            var elements = ParseDelimitedList(ParsingContext.ImportAttributes, ParseImportAttribute, true);
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
            return FinishNode(factory.CreateImportAttributes(elements, multiLine, token), pos);
        }
        else
        {
            var elements = CreateNodeArray(new List<Node>(), GetNodePos(), null, false);
            return FinishNode(factory.CreateImportAttributes(elements, false, token), pos);
        }
    }

    internal bool TokenAfterImportDefinitelyProducesImportDeclaration()
    {
        return Token() == SyntaxKind.AsteriskToken || Token() == SyntaxKind.OpenBraceToken;
    }

    internal bool TokenAfterImportedIdentifierDefinitelyProducesImportDeclaration()
    {
        // In `import id ___`, the current token decides whether to produce
        // an ImportDeclaration or ImportEqualsDeclaration.
        return Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.FromKeyword;
    }

    internal ImportEqualsDeclaration ParseImportEqualsDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers, Identifier identifier, bool isTypeOnly)
    {
        ParseExpected(SyntaxKind.EqualsToken);
        var moduleReference = ParseModuleReference();
        ParseSemicolon();
        var node = factory.CreateImportEqualsDeclaration(modifiers, isTypeOnly, identifier, moduleReference);
        var finished = WithJSDoc(FinishNode(node, pos), hasJSDoc);
        return finished;
    }

    internal ImportClause ParseImportClause(Identifier? identifier, int pos, SyntaxKind? phaseModifier, bool skipJsDocLeadingAsterisks)
    {
        // ImportClause:
        //  ImportedDefaultBinding
        //  NameSpaceImport
        //  NamedImports
        //  ImportedDefaultBinding, NameSpaceImport
        //  ImportedDefaultBinding, NamedImports

        // If there was no default import or if there is comma token after default import
        // parse namespace or named imports
        Node? namedBindings = null;
        if (
            identifier == null ||
            ParseOptional(SyntaxKind.CommaToken)
        )
        {
            if (skipJsDocLeadingAsterisks)
            {
                scanner.SetSkipJsDocLeadingAsterisks(true);
            }
            if (Token() == SyntaxKind.AsteriskToken)
            {
                namedBindings = ParseNamespaceImport();
            }
            else
            {
                namedBindings = ParseNamedImportsOrExports(SyntaxKind.NamedImports);
            }
            if (skipJsDocLeadingAsterisks)
            {
                scanner.SetSkipJsDocLeadingAsterisks(false);
            }
        }

        return FinishNode(factory.CreateImportClause(phaseModifier, identifier, namedBindings), pos);
    }

    internal Node ParseModuleReference()
    {
        return IsExternalModuleReference()
            ? ParseExternalModuleReference()
            : ParseEntityName(false);
    }

    internal ExternalModuleReference ParseExternalModuleReference()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.RequireKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);
        var expression = ParseModuleSpecifier();
        ParseExpected(SyntaxKind.CloseParenToken);
        return FinishNode(factory.CreateExternalModuleReference(expression), pos);
    }

    internal Node ParseModuleSpecifier()
    {
        if (Token() == SyntaxKind.StringLiteral)
        {
            var result = (StringLiteral)ParseLiteralNode();
            result.Text = InternIdentifier(result.Text);
            return result;
        }
        else
        {
            // We allow arbitrary expressions here, even though the grammar only allows string
            // literals.  We check to ensure that it is only a string literal later in the grammar
            // check pass.
            return ParseExpression();
        }
    }

    internal NamespaceImport ParseNamespaceImport()
    {
        // NameSpaceImport:
        //  * as ImportedBinding
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.AsteriskToken);
        ParseExpected(SyntaxKind.AsKeyword);
        var name = ParseIdentifier();
        return FinishNode(factory.CreateNamespaceImport(name), pos);
    }

    internal bool CanParseModuleExportName()
    {
        return Scanner.TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.StringLiteral;
    }

    internal Node ParseModuleExportName(Func<Identifier> parseName)
    {
        return Token() == SyntaxKind.StringLiteral ? (StringLiteral)ParseLiteralNode() : parseName();
    }

    // `kind` is SyntaxKind.NamedImports or SyntaxKind.NamedExports; the result is a NamedImports or a NamedExports.
    internal Node ParseNamedImportsOrExports(SyntaxKind kind)
    {
        var pos = GetNodePos();

        // NamedImports:
        //  { }
        //  { ImportsList }
        //  { ImportsList, }

        // ImportsList:
        //  ImportSpecifier
        //  ImportsList, ImportSpecifier
        Node node = kind == SyntaxKind.NamedImports
            ? factory.CreateNamedImports(ParseBracketedList(ParsingContext.ImportOrExportSpecifiers, ParseImportSpecifier, SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken))
            : factory.CreateNamedExports(ParseBracketedList(ParsingContext.ImportOrExportSpecifiers, ParseExportSpecifier, SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken));
        return FinishNode(node, pos);
    }

    internal ExportSpecifier ParseExportSpecifier()
    {
        var hasJSDoc = HasPrecedingJSDocComment();
        return WithJSDoc((ExportSpecifier)ParseImportOrExportSpecifier(SyntaxKind.ExportSpecifier), hasJSDoc);
    }

    internal ImportSpecifier ParseImportSpecifier()
    {
        return (ImportSpecifier)ParseImportOrExportSpecifier(SyntaxKind.ImportSpecifier);
    }

    // `kind` is SyntaxKind.ImportSpecifier or SyntaxKind.ExportSpecifier; the result is an ImportSpecifier or an ExportSpecifier.
    internal Node ParseImportOrExportSpecifier(SyntaxKind kind)
    {
        var pos = GetNodePos();
        // ImportSpecifier:
        //   BindingIdentifier
        //   ModuleExportName as BindingIdentifier
        // ExportSpecifier:
        //   ModuleExportName
        //   ModuleExportName as ModuleExportName
        var checkIdentifierIsKeyword = Utilities.IsKeyword(Token()) && !IsIdentifier();
        var checkIdentifierStart = scanner.GetTokenStart();
        var checkIdentifierEnd = scanner.GetTokenEnd();
        var isTypeOnly = false;
        Node? propertyName = null; // ModuleExportName | undefined
        var canParseAsKeyword = true;
        var name = ParseModuleExportName(() => ParseIdentifierName());
        if (name.Kind == SyntaxKind.Identifier && EscapedTextIs((Identifier)name, "type"))
        {
            // If the first token of an import specifier is 'type', there are a lot of possibilities,
            // especially if we see 'as' afterwards:
            //
            // import { type } from "mod";          - isTypeOnly: false,   name: type
            // import { type as } from "mod";       - isTypeOnly: true,    name: as
            // import { type as as } from "mod";    - isTypeOnly: false,   name: as,    propertyName: type
            // import { type as as as } from "mod"; - isTypeOnly: true,    name: as,    propertyName: as
            if (Token() == SyntaxKind.AsKeyword)
            {
                // { type as ...? }
                var firstAs = ParseIdentifierName();
                if (Token() == SyntaxKind.AsKeyword)
                {
                    // { type as as ...? }
                    var secondAs = ParseIdentifierName();
                    if (CanParseModuleExportName())
                    {
                        // { type as as something }
                        // { type as as "something" }
                        isTypeOnly = true;
                        propertyName = firstAs;
                        name = ParseModuleExportName(ParseNameWithKeywordCheck);
                        canParseAsKeyword = false;
                    }
                    else
                    {
                        // { type as as }
                        propertyName = name;
                        name = secondAs;
                        canParseAsKeyword = false;
                    }
                }
                else if (CanParseModuleExportName())
                {
                    // { type as something }
                    // { type as "something" }
                    propertyName = name;
                    canParseAsKeyword = false;
                    name = ParseModuleExportName(ParseNameWithKeywordCheck);
                }
                else
                {
                    // { type as }
                    isTypeOnly = true;
                    name = firstAs;
                }
            }
            else if (CanParseModuleExportName())
            {
                // { type something ...? }
                // { type "something" ...? }
                isTypeOnly = true;
                name = ParseModuleExportName(ParseNameWithKeywordCheck);
            }
        }

        if (canParseAsKeyword && Token() == SyntaxKind.AsKeyword)
        {
            propertyName = name;
            ParseExpected(SyntaxKind.AsKeyword);
            name = ParseModuleExportName(ParseNameWithKeywordCheck);
        }
        if (kind == SyntaxKind.ImportSpecifier)
        {
            if (name.Kind != SyntaxKind.Identifier)
            {
                // ImportSpecifier casts "name" to Identifier below, so make sure it's an identifier
                ParseErrorAt(Scanner.SkipTrivia(sourceText, name.Pos), name.End, Diagnostics.Identifier_expected);
                name = Utilities.SetTextRangePosEnd(CreateMissingNode<Identifier>(SyntaxKind.Identifier, false, null), name.Pos, name.Pos);
            }
            else if (checkIdentifierIsKeyword)
            {
                ParseErrorAt(checkIdentifierStart, checkIdentifierEnd, Diagnostics.Identifier_expected);
            }
        }
        Node node = kind == SyntaxKind.ImportSpecifier
            ? factory.CreateImportSpecifier(isTypeOnly, propertyName, (Identifier)name)
            : factory.CreateExportSpecifier(isTypeOnly, propertyName, name);
        return FinishNode(node, pos);

        Identifier ParseNameWithKeywordCheck()
        {
            checkIdentifierIsKeyword = Utilities.IsKeyword(Token()) && !IsIdentifier();
            checkIdentifierStart = scanner.GetTokenStart();
            checkIdentifierEnd = scanner.GetTokenEnd();
            return ParseIdentifierName();
        }
    }

    internal NamespaceExport ParseNamespaceExport(int pos)
    {
        return FinishNode(factory.CreateNamespaceExport(ParseModuleExportName(() => ParseIdentifierName())), pos);
    }

    internal ExportDeclaration ParseExportDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        var savedAwaitContext = InAwaitContext();
        SetAwaitContext(true);
        Node? exportClause = null; // NamedExportBindings | undefined
        Node? moduleSpecifier = null;
        ImportAttributes? attributes = null;
        var isTypeOnly = ParseOptional(SyntaxKind.TypeKeyword);
        var namespaceExportPos = GetNodePos();
        if (ParseOptional(SyntaxKind.AsteriskToken))
        {
            if (ParseOptional(SyntaxKind.AsKeyword))
            {
                exportClause = ParseNamespaceExport(namespaceExportPos);
            }
            ParseExpected(SyntaxKind.FromKeyword);
            moduleSpecifier = ParseModuleSpecifier();
        }
        else
        {
            exportClause = ParseNamedImportsOrExports(SyntaxKind.NamedExports);
            // It is not uncommon to accidentally omit the 'from' keyword. Additionally, in editing scenarios,
            // the 'from' keyword can be parsed as a named export when the export clause is unterminated (i.e. `export { from "moduleName";`)
            // If we don't have a 'from' keyword, see if we have a string literal such that ASI won't take effect.
            if (Token() == SyntaxKind.FromKeyword || (Token() == SyntaxKind.StringLiteral && !scanner.HasPrecedingLineBreak()))
            {
                ParseExpected(SyntaxKind.FromKeyword);
                moduleSpecifier = ParseModuleSpecifier();
            }
        }
        var currentTok = Token();
        if (moduleSpecifier != null && (currentTok == SyntaxKind.WithKeyword || currentTok == SyntaxKind.AssertKeyword) && !scanner.HasPrecedingLineBreak())
        {
            attributes = ParseImportAttributes(currentTok);
        }
        ParseSemicolon();
        SetAwaitContext(savedAwaitContext);
        var node = factory.CreateExportDeclaration(modifiers, isTypeOnly, exportClause, moduleSpecifier, attributes);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal ExportAssignment ParseExportAssignment(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        var savedAwaitContext = InAwaitContext();
        SetAwaitContext(true);
        bool? isExportEquals = null;
        if (ParseOptional(SyntaxKind.EqualsToken))
        {
            isExportEquals = true;
        }
        else
        {
            ParseExpected(SyntaxKind.DefaultKeyword);
        }
        var expression = ParseAssignmentExpressionOrHigher(true);
        ParseSemicolon();
        SetAwaitContext(savedAwaitContext);
        var node = factory.CreateExportAssignment(modifiers, isExportEquals, expression);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }
}
