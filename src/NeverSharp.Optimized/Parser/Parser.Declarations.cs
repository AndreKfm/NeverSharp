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

/// <summary>parser.ts: `namespace Parser`, declarations (parseArrayBindingElement through parseExportAssignment).</summary>
internal sealed partial class Parser
{
    // DECLARATIONS

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseArrayBindingElement()
    {
        var pos = GetNodePos();
        if (Token() == SyntaxKind.CommaToken)
        {
            return FinishNode(factory.CreateOmittedExpression(), pos);
        }
        var dotDotDotToken = ParseOptionalToken(SyntaxKind.DotDotDotToken);
        var name = ParseIdentifierOrPattern();
        var initializer = ParseInitializer();
        return FinishNode(factory.CreateBindingElement(dotDotDotToken, /*propertyName*/ default, name, initializer), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseObjectBindingElement()
    {
        var pos = GetNodePos();
        var dotDotDotToken = ParseOptionalToken(SyntaxKind.DotDotDotToken);
        var tokenIsIdentifier = IsBindingIdentifier();
        Node propertyName = ParsePropertyName();
        Node name;
        if (tokenIsIdentifier && Token() != SyntaxKind.ColonToken)
        {
            name = propertyName;
            propertyName = default;
        }
        else
        {
            ParseExpected(SyntaxKind.ColonToken);
            name = ParseIdentifierOrPattern();
        }
        var initializer = ParseInitializer();
        return FinishNode(factory.CreateBindingElement(dotDotDotToken, propertyName, name, initializer), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseObjectBindingPattern()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        var elements = AllowInAnd<F.DelimitedList<F.ParseObjectBindingElement>, NodeList>(new(ParsingContext.ObjectBindingElements));
        ParseExpected(SyntaxKind.CloseBraceToken);
        return FinishNode(factory.CreateObjectBindingPattern(elements), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseArrayBindingPattern()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBracketToken);
        var elements = AllowInAnd<F.DelimitedList<F.ParseArrayBindingElement>, NodeList>(new(ParsingContext.ArrayBindingElements));
        ParseExpected(SyntaxKind.CloseBracketToken);
        return FinishNode(factory.CreateArrayBindingPattern(elements), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsBindingIdentifierOrPrivateIdentifierOrPattern()
    {
        return Token() == SyntaxKind.OpenBraceToken
            || Token() == SyntaxKind.OpenBracketToken
            || Token() == SyntaxKind.PrivateIdentifier
            || IsBindingIdentifier();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseIdentifierOrPattern(DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseVariableDeclarationAllowExclamation()
    {
        return ParseVariableDeclaration(/*allowExclamation*/ true);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseVariableDeclaration(bool? allowExclamation = null)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var name = ParseIdentifierOrPattern(Diagnostics.Private_identifiers_are_not_allowed_in_variable_declarations);
        Node exclamationToken = default;
        if (
            allowExclamation == true && store.Kind(name) == SyntaxKind.Identifier &&
            Token() == SyntaxKind.ExclamationToken && !scanner.HasPrecedingLineBreak())
        {
            exclamationToken = ParseTokenNode();
        }
        var type = ParseTypeAnnotation();
        var initializer = IsInOrOfKeyword(Token()) ? default : ParseInitializer();
        var node = factory.CreateVariableDeclaration(name, exclamationToken, type, initializer);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseVariableDeclarationList(bool inForStatementInitializer)
    {
        var pos = GetNodePos();

        NodeFlags flags = 0;
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
        NodeList declarations;
        if (Token() == SyntaxKind.OfKeyword && LookAhead(new F.CanFollowContextualOfKeyword()))
        {
            declarations = CreateMissingList();
        }
        else
        {
            var savedDisallowIn = InDisallowInContext();
            SetDisallowInContext(inForStatementInitializer);

            declarations = ParseDelimitedList(
                ParsingContext.VariableDeclarations,
                new F.ParseVariableDeclarationOf(inForStatementInitializer)); // `inForStatementInitializer ? parseVariableDeclaration : parseVariableDeclarationAllowExclamation`

            SetDisallowInContext(savedDisallowIn);
        }

        return FinishNode(factory.CreateVariableDeclarationList(declarations, flags), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool CanFollowContextualOfKeyword()
    {
        return NextTokenIsIdentifier() && NextToken() == SyntaxKind.CloseParenToken;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseVariableStatement(int pos, bool hasJSDoc, NodeList modifiers)
    {
        var declarationList = ParseVariableDeclarationList(/*inForStatementInitializer*/ false);
        ParseSemicolon();
        var node = factory.CreateVariableStatement(modifiers, declarationList);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseFunctionDeclaration(int pos, bool hasJSDoc, NodeList modifiers)
    {
        var savedAwaitContext = InAwaitContext();
        var modifierFlags = Utilities.ModifiersToFlags(store, modifiers);
        ParseExpected(SyntaxKind.FunctionKeyword);
        var asteriskToken = ParseOptionalToken(SyntaxKind.AsteriskToken);
        // We don't parse the name here in await context, instead we will report a grammar error in the checker.
        var name = (modifierFlags & ModifierFlags.Default) != 0 ? ParseOptionalBindingIdentifier() : ParseBindingIdentifier();
        var isGenerator = !asteriskToken.IsNone ? SignatureFlags.Yield : SignatureFlags.None;
        var isAsync = (modifierFlags & ModifierFlags.Async) != 0 ? SignatureFlags.Await : SignatureFlags.None;
        var typeParameters = ParseTypeParameters();
        if ((modifierFlags & ModifierFlags.Export) != 0)
        {
            SetAwaitContext(/*value*/ true);
        }
        var parameters = ParseParameters(isGenerator | isAsync);
        var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
        var body = ParseFunctionBlockOrSemicolon(isGenerator | isAsync, Diagnostics.or_expected);
        SetAwaitContext(savedAwaitContext);
        var node = factory.CreateFunctionDeclaration(modifiers, asteriskToken, name, typeParameters, parameters, type, body);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>
    /// TypeScript returns `boolean | LiteralExpression | undefined`; its only caller tests its truthiness, which is what
    /// this returns (Track O, O-NA: no boxed value).
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool ParseConstructorName()
    {
        if (Token() == SyntaxKind.ConstructorKeyword)
        {
            return ParseExpected(SyntaxKind.ConstructorKeyword);
        }
        if (Token() == SyntaxKind.StringLiteral && LookAhead<F.NextToken, SyntaxKind>(new F.NextToken()) == SyntaxKind.OpenParenToken)
        {
            return !TryParse(new F.ParseConstructorStringLiteral()).IsNone;
        }
        return false;
    }

    // The callback of tryParse in parseConstructorName.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseConstructorStringLiteral()
    {
        var literalNode = ParseLiteralNode();
        return store.Texts.Equals(store.Text(literalNode), "constructor") ? literalNode : default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node TryParseConstructorDeclaration(int pos, bool hasJSDoc, NodeList modifiers)
    {
        return TryParse(new F.ParseConstructorDeclarationAfterModifiers(pos, hasJSDoc, modifiers));
    }

    // The callback of tryParse in tryParseConstructorDeclaration.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseConstructorDeclarationAfterModifiers(int pos, bool hasJSDoc, NodeList modifiers)
    {
        if (ParseConstructorName())
        {
            var typeParameters = ParseTypeParameters();
            var parameters = ParseParameters(SignatureFlags.None);
            var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
            var body = ParseFunctionBlockOrSemicolon(SignatureFlags.None, Diagnostics.or_expected);
            var node = factory.CreateConstructorDeclaration(modifiers, parameters, body);

            // Attach invalid nodes if they exist so that we can report them in the grammar checker.
            store.SetTypeParameters(node, typeParameters);
            store.SetType(node, type);
            return WithJSDoc(FinishNode(node, pos), hasJSDoc);
        }
        return default;
    }

    // `const lastError = lastOrUndefined(parseDiagnostics); if (lastError && lastError.code === Diagnostics._0_expected.code)
    // { addRelatedInfo(lastError, createDetachedDiagnostic(..., "{", "}")); }` (parseImportAttributes, parseImportType).
    [NoAllocUnchecked("error path: adds related information to a diagnostic")]
    private void AddMatchingBraceInfo(int openBracePosition)
    {
        var lastError = Core.LastOrUndefined(parseDiagnostics);
        if (lastError != null && lastError.Code == Diagnostics._0_expected.Code)
        {
            Utilities.AddRelatedInfo(
                lastError,
                Utilities.CreateDetachedDiagnostic(fileName, sourceText, openBracePosition, 1, Diagnostics.The_parser_expected_to_find_a_1_to_match_the_0_token_here, "{", "}"));
        }
    }

    /// <summary>Track O (O-NA): `some(modifiers, m => m.kind === kind)` (isAsyncModifier, isDeclareModifier, isExportModifier) as a loop.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool ModifiersContain(NodeList modifiers, SyntaxKind kind)
    {
        foreach (var m in store.Nodes(modifiers))
        {
            if (store.Kind(m) == kind)
            {
                return true;
            }
        }
        return false;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseMethodDeclaration(
        int pos,
        bool hasJSDoc,
        NodeList modifiers,
        Node asteriskToken,
        Node name,
        Node questionToken,
        Node exclamationToken,
        DiagnosticMessage? diagnosticMessage = null)
    {
        var isGenerator = !asteriskToken.IsNone ? SignatureFlags.Yield : SignatureFlags.None;
        var isAsync = ModifiersContain(modifiers, SyntaxKind.AsyncKeyword) ? SignatureFlags.Await : SignatureFlags.None;
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(isGenerator | isAsync);
        var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
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
        store.SetExclamationToken(node, exclamationToken);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParsePropertyDeclaration(
        int pos,
        bool hasJSDoc,
        NodeList modifiers,
        Node name,
        Node questionToken)
    {
        var exclamationToken = questionToken.IsNone && !scanner.HasPrecedingLineBreak() ? ParseOptionalToken(SyntaxKind.ExclamationToken) : default;
        var type = ParseTypeAnnotation();
        var initializer = DoOutsideOfContext(NodeFlags.YieldContext | NodeFlags.AwaitContext | NodeFlags.DisallowInContext, new F.ParseInitializer());
        ParseSemicolonAfterPropertyName(name, type, initializer);
        var node = factory.CreatePropertyDeclaration(
            modifiers,
            name,
            (questionToken is { IsNone: false } __t1 ? __t1 : exclamationToken),
            type,
            initializer);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParsePropertyOrMethodDeclaration(
        int pos,
        bool hasJSDoc,
        NodeList modifiers)
    {
        var asteriskToken = ParseOptionalToken(SyntaxKind.AsteriskToken);
        var name = ParsePropertyName();
        // Note: this is not legal as per the grammar.  But we allow it in the parser and
        // report an error in the grammar checker.
        var questionToken = ParseOptionalToken(SyntaxKind.QuestionToken);
        if (!asteriskToken.IsNone || Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            return ParseMethodDeclaration(pos, hasJSDoc, modifiers, asteriskToken, name, questionToken, /*exclamationToken*/ default, Diagnostics.or_expected);
        }
        return ParsePropertyDeclaration(pos, hasJSDoc, modifiers, name, questionToken);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseAccessorDeclaration(int pos, bool hasJSDoc, NodeList modifiers, SyntaxKind kind, SignatureFlags flags)
    {
        var name = ParsePropertyName();
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(SignatureFlags.None);
        var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
        var body = ParseFunctionBlockOrSemicolon(flags);
        var node = kind == SyntaxKind.GetAccessor
            ? factory.CreateGetAccessorDeclaration(modifiers, name, parameters, type, body)
            : factory.CreateSetAccessorDeclaration(modifiers, name, parameters, body);
        // Keep track of `typeParameters` (for both) and `type` (for setters) if they were parsed those indicate grammar errors
        if ((node is var getAccessor && store.IsGetAccessorDeclaration(getAccessor)))
        {
            store.SetTypeParameters(getAccessor, typeParameters);
        }
        else
        {
            store.SetTypeParameters(node, typeParameters);
        }
        if (FactoryNodeTests.IsSetAccessorDeclaration(store, node))
        {
            store.SetType(node, type);
        }
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsClassMemberStart()
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseClassStaticBlockDeclaration(int pos, bool hasJSDoc, NodeList modifiers)
    {
        ParseExpectedToken(SyntaxKind.StaticKeyword);
        var body = ParseClassStaticBlockBody();
        var node = WithJSDoc(FinishNode(factory.CreateClassStaticBlockDeclaration(body), pos), hasJSDoc);
        store.SetModifiers(node, modifiers);
        return node;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseClassStaticBlockBody()
    {
        var savedYieldContext = InYieldContext();
        var savedAwaitContext = InAwaitContext();

        SetYieldContext(false);
        SetAwaitContext(true);

        var body = ParseBlock(/*ignoreMissingOpenBrace*/ false);

        SetYieldContext(savedYieldContext);
        SetAwaitContext(savedAwaitContext);

        return body;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseDecoratorExpression()
    {
        if (InAwaitContext() && Token() == SyntaxKind.AwaitKeyword)
        {
            // `@await` is is disallowed in an [Await] context, but can cause parsing to go off the rails
            // This simply parses the missing identifier and moves on.
            var pos = GetNodePos();
            var awaitExpression = ParseIdentifier(Diagnostics.Expression_expected);
            NextToken();
            var memberExpression = ParseMemberExpressionRest(pos, awaitExpression, /*allowOptionalChain*/ true);
            return ParseCallExpressionRest(pos, memberExpression);
        }
        return ParseLeftHandSideExpressionOrHigher();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node TryParseDecorator()
    {
        var pos = GetNodePos();
        if (!ParseOptional(SyntaxKind.AtToken))
        {
            return default;
        }
        var expression = DoInDecoratorContext(new F.ParseDecoratorExpression());
        return FinishNode(factory.CreateDecorator(expression), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node TryParseModifier(bool hasSeenStaticModifier, bool? permitConstAsModifier = null, bool? stopOnStartOfClassStaticBlock = null)
    {
        var pos = GetNodePos();
        var kind = Token();

        if (Token() == SyntaxKind.ConstKeyword && permitConstAsModifier == true)
        {
            // We need to ensure that any subsequent modifiers appear on the same line
            // so that when 'const' is a standalone declaration, we don't issue an error.
            if (!TryParse<F.NextTokenIsOnSameLineAndCanFollowModifier, bool>(new F.NextTokenIsOnSameLineAndCanFollowModifier()))
            {
                return default;
            }
        }
        else if (stopOnStartOfClassStaticBlock == true && Token() == SyntaxKind.StaticKeyword && LookAhead(new F.NextTokenIsOpenBrace()))
        {
            return default;
        }
        else if (hasSeenStaticModifier && Token() == SyntaxKind.StaticKeyword)
        {
            return default;
        }
        else
        {
            if (!ParseAnyContextualModifier())
            {
                return default;
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private NodeList ParseModifiers(bool allowDecorators, bool? permitConstAsModifier = null, bool? stopOnStartOfClassStaticBlock = null)
    {
        var pos = GetNodePos();
        // TypeScript: `let list: ModifierLike[] | undefined` (a plain array, see CreateNodeArray), grown by `append`;
        // here a list under construction from the start, dropped if it stays empty.
        var list = store.BeginList();
        Node decorator, modifier;
        bool hasSeenStaticModifier = false, hasLeadingModifier = false, hasTrailingDecorator = false;

        // Decorators should be contiguous in a list of modifiers but can potentially appear in two places (i.e., `[...leadingDecorators, ...leadingModifiers, ...trailingDecorators, ...trailingModifiers]`).
        // The leading modifiers *should* only contain `export` and `default` when trailingDecorators are present, but we'll handle errors for any other leading modifiers in the checker.
        // It is illegal to have both leadingDecorators and trailingDecorators, but we will report that as a grammar check in the checker.

        // parse leading decorators
        if (allowDecorators && Token() == SyntaxKind.AtToken)
        {
            while (!(decorator = TryParseDecorator()).IsNone)
            {
                store.Push(list, decorator);
            }
        }

        // parse leading modifiers
        while (!(modifier = TryParseModifier(hasSeenStaticModifier, permitConstAsModifier, stopOnStartOfClassStaticBlock)).IsNone)
        {
            if (store.Kind(modifier) == SyntaxKind.StaticKeyword)
            {
                hasSeenStaticModifier = true;
            }
            store.Push(list, modifier);
            hasLeadingModifier = true;
        }

        // parse trailing decorators, but only if we parsed any leading modifiers
        if (hasLeadingModifier && allowDecorators && Token() == SyntaxKind.AtToken)
        {
            while (!(decorator = TryParseDecorator()).IsNone)
            {
                store.Push(list, decorator);
                hasTrailingDecorator = true;
            }
        }

        // parse trailing modifiers, but only if we parsed any trailing decorators
        if (hasTrailingDecorator)
        {
            while (!(modifier = TryParseModifier(hasSeenStaticModifier, permitConstAsModifier, stopOnStartOfClassStaticBlock)).IsNone)
            {
                if (store.Kind(modifier) == SyntaxKind.StaticKeyword)
                {
                    hasSeenStaticModifier = true;
                }
                store.Push(list, modifier);
            }
        }

        if (store.Length(list) == 0)
        {
            store.DiscardList(list);
            return default;
        }
        return CreateNodeArray(list, pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private NodeList ParseModifiersForArrowFunction()
    {
        NodeList modifiers = default;
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            var pos = GetNodePos();
            NextToken();
            var modifier = FinishNode(factory.CreateToken(SyntaxKind.AsyncKeyword), pos);
            modifiers = CreateNodeArray([modifier], pos);
        }
        return modifiers;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseClassElement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        if (Token() == SyntaxKind.SemicolonToken)
        {
            NextToken();
            return WithJSDoc(FinishNode(factory.CreateSemicolonClassElement(), pos), hasJSDoc);
        }

        var modifiers = ParseModifiers(/*allowDecorators*/ true, /*permitConstAsModifier*/ true, /*stopOnStartOfClassStaticBlock*/ true);
        if (Token() == SyntaxKind.StaticKeyword && LookAhead(new F.NextTokenIsOpenBrace()))
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
            if (!constructorDeclaration.IsNone)
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
            Token() == SyntaxKind.OpenBracketToken)
        {
            var isAmbient = ModifiersContain(modifiers, SyntaxKind.DeclareKeyword);
            if (isAmbient)
            {
                foreach (var m in store.Nodes(modifiers))
                {
                    store.SetFlags(m, store.Flags(m) | NodeFlags.Ambient);
                }
                return DoInsideOfContext(NodeFlags.Ambient, new F.ParsePropertyOrMethodDeclaration(pos, hasJSDoc, modifiers));
            }
            else
            {
                return ParsePropertyOrMethodDeclaration(pos, hasJSDoc, modifiers);
            }
        }

        if (!modifiers.IsNone)
        {
            // treat this as a property declaration with a missing name.
            var name = CreateMissingNode(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Diagnostics.Declaration_expected);
            return ParsePropertyDeclaration(pos, hasJSDoc, modifiers, name, /*questionToken*/ default);
        }

        // 'isClassMemberStart' should have hinted not to attempt parsing.
        Debug.Fail("Should not have attempted to parse class member declaration.");
        return default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseDecoratedExpression()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(/*allowDecorators*/ true);
        if (Token() == SyntaxKind.ClassKeyword)
        {
            return ParseClassDeclarationOrExpression(pos, hasJSDoc, modifiers, SyntaxKind.ClassExpression);
        }

        var missing = CreateMissingNode(SyntaxKind.MissingDeclaration, /*reportAtCurrentPosition*/ true, Diagnostics.Expression_expected);
        Utilities.SetTextRangePos(store, missing, pos);
        store.SetModifiers(missing, modifiers);
        return missing;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseClassExpression()
    {
        return ParseClassDeclarationOrExpression(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ default, SyntaxKind.ClassExpression);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseClassDeclaration(int pos, bool hasJSDoc, NodeList modifiers)
    {
        return ParseClassDeclarationOrExpression(pos, hasJSDoc, modifiers, SyntaxKind.ClassDeclaration);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseClassDeclarationOrExpression(int pos, bool hasJSDoc, NodeList modifiers, SyntaxKind kind)
    {
        var savedAwaitContext = InAwaitContext();
        ParseExpected(SyntaxKind.ClassKeyword);

        // We don't parse the name here in await context, instead we will report a grammar error in the checker.
        var name = ParseNameOfClassDeclarationOrExpression();
        var typeParameters = ParseTypeParameters();
        if (ModifiersContain(modifiers, SyntaxKind.ExportKeyword))
        {
            SetAwaitContext(/*value*/ true);
        }
        var heritageClauses = ParseHeritageClauses();

        NodeList members;
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
        var node = kind == SyntaxKind.ClassDeclaration
            ? factory.CreateClassDeclaration(modifiers, name, typeParameters, heritageClauses, members)
            : factory.CreateClassExpression(modifiers, name, typeParameters, heritageClauses, members);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseNameOfClassDeclarationOrExpression()
    {
        // implements is a future reserved word so
        // 'class implements' might mean either
        // - class expression with omitted name, 'implements' starts heritage clause
        // - class with name 'implements'
        // 'isImplementsClause' helps to disambiguate between these two cases
        return IsBindingIdentifier() && !IsImplementsClause()
            ? CreateIdentifier(IsBindingIdentifier())
            : default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsImplementsClause()
    {
        return Token() == SyntaxKind.ImplementsKeyword && LookAhead(new F.NextTokenIsIdentifierOrKeyword());
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private NodeList ParseHeritageClauses()
    {
        // ClassTail[Yield,Await] : (Modified) See 14.5
        //      ClassHeritage[?Yield,?Await]opt { ClassBody[?Yield,?Await]opt }

        if (IsHeritageClause())
        {
            return ParseList(ParsingContext.HeritageClauses, new F.ParseHeritageClause());
        }

        return default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseHeritageClause()
    {
        var pos = GetNodePos();
        var tok = Token();
        Debug.Assert(tok == SyntaxKind.ExtendsKeyword || tok == SyntaxKind.ImplementsKeyword); // isListElement() should ensure this.
        NextToken();
        var types = ParseDelimitedList(ParsingContext.HeritageClauseElement, new F.ParseExpressionWithTypeArguments());
        return FinishNode(factory.CreateHeritageClause(tok, types), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseExpressionWithTypeArguments()
    {
        var pos = GetNodePos();
        var expression = ParseLeftHandSideExpressionOrHigher();
        if (store.Kind(expression) == SyntaxKind.ExpressionWithTypeArguments)
        {
            return expression;
        }
        var typeArguments = TryParseTypeArguments();
        return FinishNode(factory.CreateExpressionWithTypeArguments(expression, typeArguments), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private NodeList TryParseTypeArguments()
    {
        return Token() == SyntaxKind.LessThanToken ?
            ParseBracketedList(ParsingContext.TypeArguments, new F.ParseType(), SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken) : default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsHeritageClause()
    {
        return Token() == SyntaxKind.ExtendsKeyword || Token() == SyntaxKind.ImplementsKeyword;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private NodeList ParseClassMembers()
    {
        return ParseList(ParsingContext.ClassMembers, new F.ParseClassElement());
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseInterfaceDeclaration(int pos, bool hasJSDoc, NodeList modifiers)
    {
        ParseExpected(SyntaxKind.InterfaceKeyword);
        var name = ParseIdentifier();
        var typeParameters = ParseTypeParameters();
        var heritageClauses = ParseHeritageClauses();
        var members = ParseObjectTypeMembers();
        var node = factory.CreateInterfaceDeclaration(modifiers, name, typeParameters, heritageClauses, members);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseTypeAliasDeclaration(int pos, bool hasJSDoc, NodeList modifiers)
    {
        ParseExpected(SyntaxKind.TypeKeyword);
        if (scanner.HasPrecedingLineBreak())
        {
            ParseErrorAtCurrentToken(Diagnostics.Line_break_not_permitted_here);
        }
        var name = ParseIdentifier();
        var typeParameters = ParseTypeParameters();
        ParseExpected(SyntaxKind.EqualsToken);
        var type = ((Token() == SyntaxKind.IntrinsicKeyword ? TryParse(new F.ParseKeywordAndNoDot()) : default) is { IsNone: false } __t2 ? __t2 : ParseType());
        ParseSemicolon();
        var node = factory.CreateTypeAliasDeclaration(modifiers, name, typeParameters, type);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    // In an ambient declaration, the grammar only allows integer literals as initializers.
    // In a non-ambient declaration, the grammar allows uninitialized members only in a
    // ConstantEnumMemberSection, which starts at the beginning of an enum declaration
    // or any time an integer literal initializer is encountered.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseEnumMember()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var name = ParsePropertyName();
        var initializer = AllowInAnd(new F.ParseInitializer());
        return WithJSDoc(FinishNode(factory.CreateEnumMember(name, initializer), pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseEnumDeclaration(int pos, bool hasJSDoc, NodeList modifiers)
    {
        ParseExpected(SyntaxKind.EnumKeyword);
        var name = ParseIdentifier();
        NodeList members;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            members = DoOutsideOfYieldAndAwaitContext<F.DelimitedList<F.ParseEnumMember>, NodeList>(new(ParsingContext.EnumMembers));
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            members = CreateMissingList();
        }
        var node = factory.CreateEnumDeclaration(modifiers, name, members);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseModuleBlock()
    {
        var pos = GetNodePos();
        NodeList statements;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            statements = ParseList(ParsingContext.BlockStatements, new F.ParseStatement());
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            statements = CreateMissingList();
        }
        return FinishNode(factory.CreateModuleBlock(statements), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseModuleOrNamespaceDeclaration(int pos, bool hasJSDoc, NodeList modifiers, NodeFlags flags)
    {
        // If we are parsing a dotted namespace name, we want to
        // propagate the 'Namespace' flag across the names if set.
        var namespaceFlag = flags & NodeFlags.Namespace;
        var name = (flags & NodeFlags.NestedNamespace) != 0 ? ParseIdentifierName() : ParseIdentifier();
        var body = ParseOptional(SyntaxKind.DotToken)
            ? ParseModuleOrNamespaceDeclaration(GetNodePos(), /*hasJSDoc*/ false, /*modifiers*/ default, NodeFlags.NestedNamespace | namespaceFlag)
            : ParseModuleBlock();
        var node = factory.CreateModuleDeclaration(modifiers, name, body, flags);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseAmbientExternalModuleDeclaration(int pos, bool hasJSDoc, NodeList modifiersIn)
    {
        NodeFlags flags = 0;
        Node name;
        if (Token() == SyntaxKind.GlobalKeyword)
        {
            // parse 'global' as name of global scope augmentation
            name = ParseIdentifier();
            flags |= NodeFlags.GlobalAugmentation;
        }
        else
        {
            var literal = ParseLiteralNode();
            store.SetText(literal, InternIdentifier(store.Text(literal)));
            name = literal;
        }
        Node body = default;
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseModuleDeclaration(int pos, bool hasJSDoc, NodeList modifiersIn)
    {
        NodeFlags flags = 0;
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsExternalModuleReference()
    {
        return Token() == SyntaxKind.RequireKeyword &&
            LookAhead(new F.NextTokenIsOpenParen());
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool NextTokenIsOpenParen()
    {
        return NextToken() == SyntaxKind.OpenParenToken;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool NextTokenIsOpenBrace()
    {
        return NextToken() == SyntaxKind.OpenBraceToken;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool NextTokenIsSlash()
    {
        return NextToken() == SyntaxKind.SlashToken;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseNamespaceExportDeclaration(int pos, bool hasJSDoc, NodeList modifiers)
    {
        ParseExpected(SyntaxKind.AsKeyword);
        ParseExpected(SyntaxKind.NamespaceKeyword);
        var name = ParseIdentifier();
        ParseSemicolon();
        var node = factory.CreateNamespaceExportDeclaration(name);
        // NamespaceExportDeclaration nodes cannot have decorators or modifiers, so we attach them here so we can report them in the grammar checker
        store.SetModifiers(node, modifiers);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseImportDeclarationOrImportEqualsDeclaration(int pos, bool hasJSDoc, NodeList modifiers)
    {
        ParseExpected(SyntaxKind.ImportKeyword);

        var afterImportPos = scanner.GetTokenFullStart();

        // We don't parse the identifier here in await context, instead we will report a grammar error in the checker.
        Node identifier = default;
        if (IsIdentifier())
        {
            identifier = ParseIdentifier();
        }

        SyntaxKind? phaseModifier = null;
        if (
            !identifier.IsNone && store.Texts.Equals(store.EscapedText(identifier), "type") &&
            (Token() != SyntaxKind.FromKeyword || IsIdentifier() && LookAhead(new F.NextTokenIsFromKeywordOrEqualsToken())) &&
            (IsIdentifier() || TokenAfterImportDefinitelyProducesImportDeclaration()))
        {
            phaseModifier = SyntaxKind.TypeKeyword;
            identifier = IsIdentifier() ? ParseIdentifier() : default;
        }
        else if (
            !identifier.IsNone && store.Texts.Equals(store.EscapedText(identifier), "defer") &&
            (Token() == SyntaxKind.FromKeyword ? !LookAhead(new F.NextTokenIsStringLiteral()) : Token() != SyntaxKind.CommaToken && Token() != SyntaxKind.EqualsToken))
        {
            phaseModifier = SyntaxKind.DeferKeyword;
            identifier = IsIdentifier() ? ParseIdentifier() : default;
        }

        if (!identifier.IsNone && !TokenAfterImportedIdentifierDefinitelyProducesImportDeclaration() && phaseModifier != SyntaxKind.DeferKeyword)
        {
            return ParseImportEqualsDeclaration(pos, hasJSDoc, modifiers, identifier, phaseModifier == SyntaxKind.TypeKeyword);
        }

        var importClause = TryParseImportClause(identifier, afterImportPos, phaseModifier, /*skipJsDocLeadingAsterisks*/ false);
        var moduleSpecifier = ParseModuleSpecifier();
        var attributes = TryParseImportAttributes();

        ParseSemicolon();
        var node = factory.CreateImportDeclaration(modifiers, importClause, moduleSpecifier, attributes);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node TryParseImportClause(Node identifier, int pos, SyntaxKind? phaseModifier, bool skipJsDocLeadingAsterisks = false)
    {
        // ImportDeclaration:
        //  import ImportClause from ModuleSpecifier ;
        //  import ModuleSpecifier;
        Node importClause = default;
        if (
            !identifier.IsNone || // import id
            Token() == SyntaxKind.AsteriskToken || // import *
            Token() == SyntaxKind.OpenBraceToken) // import {
        {
            importClause = ParseImportClause(identifier, pos, phaseModifier, skipJsDocLeadingAsterisks);
            ParseExpected(SyntaxKind.FromKeyword);
        }
        return importClause;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node TryParseImportAttributes()
    {
        // (TypeScript names this local `currentToken`, shadowing the parser variable.)
        var currentTokenLocal = Token();
        if (currentTokenLocal == SyntaxKind.WithKeyword || (currentTokenLocal == SyntaxKind.AssertKeyword && !scanner.HasPrecedingLineBreak()))
        {
            return ParseImportAttributes(currentTokenLocal);
        }
        return default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseImportAttribute()
    {
        var pos = GetNodePos();
        var name = Scanner.TokenIsIdentifierOrKeyword(Token()) ? ParseIdentifierName() : ParseLiteralLikeNode(SyntaxKind.StringLiteral);
        ParseExpected(SyntaxKind.ColonToken);
        var value = ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);
        return FinishNode(factory.CreateImportAttribute(name, value), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseImportAttributes(SyntaxKind token, bool? skipKeyword = null)
    {
        var pos = GetNodePos();
        if (skipKeyword != true)
        {
            ParseExpected(token);
        }
        var openBracePosition = scanner.GetTokenStart();
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            var multiLine = scanner.HasPrecedingLineBreak();
            var elements = ParseDelimitedList(ParsingContext.ImportAttributes, new F.ParseImportAttribute(), /*considerSemicolonAsDelimiter*/ true);
            if (!ParseExpected(SyntaxKind.CloseBraceToken))
            {
                AddMatchingBraceInfo(openBracePosition);
            }
            return FinishNode(factory.CreateImportAttributes(elements, multiLine, token), pos);
        }
        else
        {
            var elements = CreateNodeArray([], GetNodePos(), /*end*/ null); // (hasTrailingComma false)
            return FinishNode(factory.CreateImportAttributes(elements, /*multiLine*/ false, token), pos);
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool TokenAfterImportDefinitelyProducesImportDeclaration()
    {
        return Token() == SyntaxKind.AsteriskToken || Token() == SyntaxKind.OpenBraceToken;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool TokenAfterImportedIdentifierDefinitelyProducesImportDeclaration()
    {
        // In `import id ___`, the current token decides whether to produce
        // an ImportDeclaration or ImportEqualsDeclaration.
        return Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.FromKeyword;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseImportEqualsDeclaration(int pos, bool hasJSDoc, NodeList modifiers, Node identifier, bool isTypeOnly)
    {
        ParseExpected(SyntaxKind.EqualsToken);
        var moduleReference = ParseModuleReference();
        ParseSemicolon();
        var node = factory.CreateImportEqualsDeclaration(modifiers, isTypeOnly, identifier, moduleReference);
        var finished = WithJSDoc(FinishNode(node, pos), hasJSDoc);
        return finished;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseImportClause(Node identifier, int pos, SyntaxKind? phaseModifier, bool skipJsDocLeadingAsterisks)
    {
        // ImportClause:
        //  ImportedDefaultBinding
        //  NameSpaceImport
        //  NamedImports
        //  ImportedDefaultBinding, NameSpaceImport
        //  ImportedDefaultBinding, NamedImports

        // If there was no default import or if there is comma token after default import
        // parse namespace or named imports
        Node namedBindings = default;
        if (
            identifier.IsNone ||
            ParseOptional(SyntaxKind.CommaToken))
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseModuleReference()
    {
        return IsExternalModuleReference()
            ? ParseExternalModuleReference()
            : ParseEntityName(/*allowReservedWords*/ false);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseExternalModuleReference()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.RequireKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);
        var expression = ParseModuleSpecifier();
        ParseExpected(SyntaxKind.CloseParenToken);
        return FinishNode(factory.CreateExternalModuleReference(expression), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseModuleSpecifier()
    {
        if (Token() == SyntaxKind.StringLiteral)
        {
            var result = ParseLiteralNode();
            store.SetText(result, InternIdentifier(store.Text(result)));
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseNamespaceImport()
    {
        // NameSpaceImport:
        //  * as ImportedBinding
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.AsteriskToken);
        ParseExpected(SyntaxKind.AsKeyword);
        var name = ParseIdentifier();
        return FinishNode(factory.CreateNamespaceImport(name), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool CanParseModuleExportName()
    {
        return Scanner.TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.StringLiteral;
    }

    // TypeScript: `parseModuleExportName(parseName)`; Track O (O-NA): this is the form with `() => parseIdentifierName()`,
    // the callers with parseNameWithKeywordCheck write the test out and call the local function directly (no delegate).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseModuleExportName()
    {
        return Token() == SyntaxKind.StringLiteral ? ParseLiteralNode() : ParseIdentifierName();
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseNamedImportsOrExports(SyntaxKind kind)
    {
        var pos = GetNodePos();

        // NamedImports:
        //  { }
        //  { ImportsList }
        //  { ImportsList, }

        // ImportsList:
        //  ImportSpecifier
        //  ImportsList, ImportSpecifier
        var node = kind == SyntaxKind.NamedImports
            ? factory.CreateNamedImports(ParseBracketedList(ParsingContext.ImportOrExportSpecifiers, new F.ParseImportSpecifier(), SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken))
            : factory.CreateNamedExports(ParseBracketedList(ParsingContext.ImportOrExportSpecifiers, new F.ParseExportSpecifier(), SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken));
        return FinishNode(node, pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseExportSpecifier()
    {
        var hasJSDoc = HasPrecedingJSDocComment();
        return WithJSDoc(ParseImportOrExportSpecifier(SyntaxKind.ExportSpecifier), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseImportSpecifier()
    {
        return ParseImportOrExportSpecifier(SyntaxKind.ImportSpecifier);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseImportOrExportSpecifier(SyntaxKind kind)
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
        Node propertyName = default;
        var canParseAsKeyword = true;
        var name = ParseModuleExportName();
        if (store.Kind(name) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(name), "type"))
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
                        name = (Token() == SyntaxKind.StringLiteral ? ParseLiteralNode() : ParseNameWithKeywordCheck());
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
                    name = (Token() == SyntaxKind.StringLiteral ? ParseLiteralNode() : ParseNameWithKeywordCheck());
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
                name = (Token() == SyntaxKind.StringLiteral ? ParseLiteralNode() : ParseNameWithKeywordCheck());
            }
        }

        if (canParseAsKeyword && Token() == SyntaxKind.AsKeyword)
        {
            propertyName = name;
            ParseExpected(SyntaxKind.AsKeyword);
            name = (Token() == SyntaxKind.StringLiteral ? ParseLiteralNode() : ParseNameWithKeywordCheck());
        }
        if (kind == SyntaxKind.ImportSpecifier)
        {
            if (store.Kind(name) != SyntaxKind.Identifier)
            {
                // ImportSpecifier casts "name" to Identifier below, so make sure it's an identifier
                ParseErrorAt(Scanner.SkipTrivia(sourceText, store.Pos(name)), store.End(name), Diagnostics.Identifier_expected);
                name = Utilities.SetTextRangePosEnd(store, CreateMissingNode(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ false), store.Pos(name), store.Pos(name));
            }
            else if (checkIdentifierIsKeyword)
            {
                ParseErrorAt(checkIdentifierStart, checkIdentifierEnd, Diagnostics.Identifier_expected);
            }
        }
        var node = kind == SyntaxKind.ImportSpecifier
            ? factory.CreateImportSpecifier(isTypeOnly, propertyName, name)
            : factory.CreateExportSpecifier(isTypeOnly, propertyName, name);
        return FinishNode(node, pos);

        Node ParseNameWithKeywordCheck()
        {
            checkIdentifierIsKeyword = Utilities.IsKeyword(Token()) && !IsIdentifier();
            checkIdentifierStart = scanner.GetTokenStart();
            checkIdentifierEnd = scanner.GetTokenEnd();
            return ParseIdentifierName();
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseNamespaceExport(int pos)
    {
        return FinishNode(factory.CreateNamespaceExport(ParseModuleExportName()), pos);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseExportDeclaration(int pos, bool hasJSDoc, NodeList modifiers)
    {
        var savedAwaitContext = InAwaitContext();
        SetAwaitContext(/*value*/ true);
        Node exportClause = default;
        Node moduleSpecifier = default;
        Node attributes = default;
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
        // (TypeScript names this local `currentToken`, shadowing the parser variable.)
        var currentTokenLocal = Token();
        if (!moduleSpecifier.IsNone && (currentTokenLocal == SyntaxKind.WithKeyword || currentTokenLocal == SyntaxKind.AssertKeyword) && !scanner.HasPrecedingLineBreak())
        {
            attributes = ParseImportAttributes(currentTokenLocal);
        }
        ParseSemicolon();
        SetAwaitContext(savedAwaitContext);
        var node = factory.CreateExportDeclaration(modifiers, isTypeOnly, exportClause, moduleSpecifier, attributes);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node ParseExportAssignment(int pos, bool hasJSDoc, NodeList modifiers)
    {
        var savedAwaitContext = InAwaitContext();
        SetAwaitContext(/*value*/ true);
        bool? isExportEquals = null;
        if (ParseOptional(SyntaxKind.EqualsToken))
        {
            isExportEquals = true;
        }
        else
        {
            ParseExpected(SyntaxKind.DefaultKeyword);
        }
        var expression = ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);
        ParseSemicolon();
        SetAwaitContext(savedAwaitContext);
        var node = factory.CreateExportAssignment(modifiers, isExportEquals, expression);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }
}
