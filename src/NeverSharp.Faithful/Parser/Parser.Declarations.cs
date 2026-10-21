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

/// <summary>parser.ts: `namespace Parser`, declarations (parseArrayBindingElement through parseExportAssignment).</summary>
internal sealed partial class Parser
{
    // DECLARATIONS

    private Node ParseArrayBindingElement()
    {
        var pos = GetNodePos();
        if (Token() == SyntaxKind.CommaToken)
        {
            return FinishNode(factory.CreateOmittedExpression(), pos);
        }
        var dotDotDotToken = ParseOptionalToken<DotDotDotToken>(SyntaxKind.DotDotDotToken);
        var name = ParseIdentifierOrPattern();
        var initializer = ParseInitializer();
        return FinishNode(factory.CreateBindingElement(dotDotDotToken, /*propertyName*/ null, name, initializer), pos);
    }

    private BindingElement ParseObjectBindingElement()
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

    private ObjectBindingPattern ParseObjectBindingPattern()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        var elements = AllowInAnd(p => ParseDelimitedList(ParsingContext.ObjectBindingElements, static p => p.ParseObjectBindingElement()));
        ParseExpected(SyntaxKind.CloseBraceToken);
        return FinishNode(factory.CreateObjectBindingPattern(elements), pos);
    }

    private ArrayBindingPattern ParseArrayBindingPattern()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBracketToken);
        var elements = AllowInAnd(p => ParseDelimitedList(ParsingContext.ArrayBindingElements, static p => p.ParseArrayBindingElement()));
        ParseExpected(SyntaxKind.CloseBracketToken);
        return FinishNode(factory.CreateArrayBindingPattern(elements), pos);
    }

    private bool IsBindingIdentifierOrPrivateIdentifierOrPattern()
    {
        return Token() == SyntaxKind.OpenBraceToken
            || Token() == SyntaxKind.OpenBracketToken
            || Token() == SyntaxKind.PrivateIdentifier
            || IsBindingIdentifier();
    }

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

    private VariableDeclaration ParseVariableDeclarationAllowExclamation()
    {
        return ParseVariableDeclaration(/*allowExclamation*/ true);
    }

    private VariableDeclaration ParseVariableDeclaration(bool? allowExclamation = null)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var name = ParseIdentifierOrPattern(Diagnostics.Private_identifiers_are_not_allowed_in_variable_declarations);
        ExclamationToken? exclamationToken = null;
        if (
            allowExclamation == true && name.Kind == SyntaxKind.Identifier &&
            Token() == SyntaxKind.ExclamationToken && !scanner.HasPrecedingLineBreak())
        {
            exclamationToken = ParseTokenNode<ExclamationToken>();
        }
        var type = ParseTypeAnnotation();
        var initializer = IsInOrOfKeyword(Token()) ? null : ParseInitializer();
        var node = factory.CreateVariableDeclaration(name, exclamationToken, type, initializer);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private VariableDeclarationList ParseVariableDeclarationList(bool inForStatementInitializer)
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
        NodeArray declarations;
        if (Token() == SyntaxKind.OfKeyword && LookAhead(static p => p.CanFollowContextualOfKeyword()))
        {
            declarations = CreateMissingList();
        }
        else
        {
            var savedDisallowIn = InDisallowInContext();
            SetDisallowInContext(inForStatementInitializer);

            declarations = ParseDelimitedList(
                ParsingContext.VariableDeclarations,
                inForStatementInitializer ? static p => p.ParseVariableDeclaration() : static p => p.ParseVariableDeclarationAllowExclamation());

            SetDisallowInContext(savedDisallowIn);
        }

        return FinishNode(factory.CreateVariableDeclarationList(declarations, flags), pos);
    }

    private bool CanFollowContextualOfKeyword()
    {
        return NextTokenIsIdentifier() && NextToken() == SyntaxKind.CloseParenToken;
    }

    private VariableStatement ParseVariableStatement(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        var declarationList = ParseVariableDeclarationList(/*inForStatementInitializer*/ false);
        ParseSemicolon();
        var node = factory.CreateVariableStatement(modifiers, declarationList);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private FunctionDeclaration ParseFunctionDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        var savedAwaitContext = InAwaitContext();
        var modifierFlags = Utilities.ModifiersToFlags(modifiers);
        ParseExpected(SyntaxKind.FunctionKeyword);
        var asteriskToken = ParseOptionalToken<AsteriskToken>(SyntaxKind.AsteriskToken);
        // We don't parse the name here in await context, instead we will report a grammar error in the checker.
        var name = (modifierFlags & ModifierFlags.Default) != 0 ? ParseOptionalBindingIdentifier() : ParseBindingIdentifier();
        var isGenerator = asteriskToken != null ? SignatureFlags.Yield : SignatureFlags.None;
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

    /// <summary>TypeScript returns `boolean | LiteralExpression | undefined`; callers test its truthiness.</summary>
    private object? ParseConstructorName()
    {
        if (Token() == SyntaxKind.ConstructorKeyword)
        {
            return ParseExpected(SyntaxKind.ConstructorKeyword);
        }
        if (Token() == SyntaxKind.StringLiteral && LookAhead(static p => p.NextToken()) == SyntaxKind.OpenParenToken)
        {
            return TryParse(p =>
            {
                var literalNode = ParseLiteralNode();
                return ((StringLiteral)literalNode).Text == "constructor" ? literalNode : null;
            });
        }
        return null;
    }

    private ConstructorDeclaration? TryParseConstructorDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        return TryParse(p =>
        {
            if (IsTruthy(ParseConstructorName()))
            {
                var typeParameters = ParseTypeParameters();
                var parameters = ParseParameters(SignatureFlags.None);
                var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
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

    private MethodDeclaration ParseMethodDeclaration(
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
        var isAsync = Core.Some(modifiers, FactoryNodeTests.IsAsyncModifier) ? SignatureFlags.Await : SignatureFlags.None;
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
        node.ExclamationToken = exclamationToken;
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private PropertyDeclaration ParsePropertyDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray? modifiers,
        Node name,
        QuestionToken? questionToken)
    {
        var exclamationToken = questionToken == null && !scanner.HasPrecedingLineBreak() ? ParseOptionalToken<ExclamationToken>(SyntaxKind.ExclamationToken) : null;
        var type = ParseTypeAnnotation();
        var initializer = DoOutsideOfContext(NodeFlags.YieldContext | NodeFlags.AwaitContext | NodeFlags.DisallowInContext, static p => p.ParseInitializer());
        ParseSemicolonAfterPropertyName(name, type, initializer);
        var node = factory.CreatePropertyDeclaration(
            modifiers,
            name,
            (Node?)questionToken ?? exclamationToken,
            type,
            initializer);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private Node ParsePropertyOrMethodDeclaration(
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
            return ParseMethodDeclaration(pos, hasJSDoc, modifiers, asteriskToken, name, questionToken, /*exclamationToken*/ null, Diagnostics.or_expected);
        }
        return ParsePropertyDeclaration(pos, hasJSDoc, modifiers, name, questionToken);
    }

    private Node ParseAccessorDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers, SyntaxKind kind, SignatureFlags flags)
    {
        var name = ParsePropertyName();
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(SignatureFlags.None);
        var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
        var body = ParseFunctionBlockOrSemicolon(flags);
        var node = kind == SyntaxKind.GetAccessor
            ? (Node)factory.CreateGetAccessorDeclaration(modifiers, name, parameters, type, body)
            : factory.CreateSetAccessorDeclaration(modifiers, name, parameters, body);
        // Keep track of `typeParameters` (for both) and `type` (for setters) if they were parsed those indicate grammar errors
        if (node is GetAccessorDeclaration getAccessor)
        {
            getAccessor.TypeParameters = typeParameters;
        }
        else
        {
            ((SetAccessorDeclaration)node).TypeParameters = typeParameters;
        }
        if (FactoryNodeTests.IsSetAccessorDeclaration(node))
        {
            ((SetAccessorDeclaration)node).Type = type;
        }
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

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

    private ClassStaticBlockDeclaration ParseClassStaticBlockDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        ParseExpectedToken(SyntaxKind.StaticKeyword);
        var body = ParseClassStaticBlockBody();
        var node = WithJSDoc(FinishNode(factory.CreateClassStaticBlockDeclaration(body), pos), hasJSDoc);
        node.Modifiers = modifiers;
        return node;
    }

    private Block ParseClassStaticBlockBody()
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

    private Decorator? TryParseDecorator()
    {
        var pos = GetNodePos();
        if (!ParseOptional(SyntaxKind.AtToken))
        {
            return null;
        }
        var expression = DoInDecoratorContext(static p => p.ParseDecoratorExpression());
        return FinishNode(factory.CreateDecorator(expression), pos);
    }

    private Node? TryParseModifier(bool hasSeenStaticModifier, bool? permitConstAsModifier = null, bool? stopOnStartOfClassStaticBlock = null)
    {
        var pos = GetNodePos();
        var kind = Token();

        if (Token() == SyntaxKind.ConstKeyword && permitConstAsModifier == true)
        {
            // We need to ensure that any subsequent modifiers appear on the same line
            // so that when 'const' is a standalone declaration, we don't issue an error.
            if (!TryParse(static p => p.NextTokenIsOnSameLineAndCanFollowModifier()))
            {
                return null;
            }
        }
        else if (stopOnStartOfClassStaticBlock == true && Token() == SyntaxKind.StaticKeyword && LookAhead(static p => p.NextTokenIsOpenBrace()))
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
    private NodeArray? ParseModifiers(bool allowDecorators, bool? permitConstAsModifier = null, bool? stopOnStartOfClassStaticBlock = null)
    {
        var pos = GetNodePos();
        // TypeScript: `let list: ModifierLike[] | undefined` (a plain array, see CreateNodeArray), grown by `append`.
        NodeArray? list = null;
        Node? decorator, modifier;
        bool hasSeenStaticModifier = false, hasLeadingModifier = false, hasTrailingDecorator = false;

        // Decorators should be contiguous in a list of modifiers but can potentially appear in two places (i.e., `[...leadingDecorators, ...leadingModifiers, ...trailingDecorators, ...trailingModifiers]`).
        // The leading modifiers *should* only contain `export` and `default` when trailingDecorators are present, but we'll handle errors for any other leading modifiers in the checker.
        // It is illegal to have both leadingDecorators and trailingDecorators, but we will report that as a grammar check in the checker.

        // parse leading decorators
        if (allowDecorators && Token() == SyntaxKind.AtToken)
        {
            while ((decorator = TryParseDecorator()) != null)
            {
                list ??= new NodeArray();
                list.Add(decorator);
            }
        }

        // parse leading modifiers
        while ((modifier = TryParseModifier(hasSeenStaticModifier, permitConstAsModifier, stopOnStartOfClassStaticBlock)) != null)
        {
            if (modifier.Kind == SyntaxKind.StaticKeyword)
            {
                hasSeenStaticModifier = true;
            }
            list ??= new NodeArray();
            list.Add(modifier);
            hasLeadingModifier = true;
        }

        // parse trailing decorators, but only if we parsed any leading modifiers
        if (hasLeadingModifier && allowDecorators && Token() == SyntaxKind.AtToken)
        {
            while ((decorator = TryParseDecorator()) != null)
            {
                list ??= new NodeArray();
                list.Add(decorator);
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
                list ??= new NodeArray();
                list.Add(modifier);
            }
        }

        return list != null ? CreateNodeArray(list, pos) : null;
    }

    private NodeArray? ParseModifiersForArrowFunction()
    {
        NodeArray? modifiers = null;
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            var pos = GetNodePos();
            NextToken();
            var modifier = FinishNode(factory.CreateToken(SyntaxKind.AsyncKeyword), pos);
            modifiers = CreateNodeArray(new NodeArray { modifier }, pos);
        }
        return modifiers;
    }

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
        if (Token() == SyntaxKind.StaticKeyword && LookAhead(static p => p.NextTokenIsOpenBrace()))
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
            Token() == SyntaxKind.OpenBracketToken)
        {
            var isAmbient = Core.Some(modifiers, IsDeclareModifier);
            if (isAmbient)
            {
                foreach (var m in modifiers!)
                {
                    m.Flags |= NodeFlags.Ambient;
                }
                // (C#: the closure captures copies declared in this block, so it is allocated only when this branch runs.)
                {
                    var ambientPos = pos;
                    var ambientHasJSDoc = hasJSDoc;
                    var ambientModifiers = modifiers;
                    return DoInsideOfContext(NodeFlags.Ambient, p => p.ParsePropertyOrMethodDeclaration(ambientPos, ambientHasJSDoc, ambientModifiers));
                }
            }
            else
            {
                return ParsePropertyOrMethodDeclaration(pos, hasJSDoc, modifiers);
            }
        }

        if (modifiers != null)
        {
            // treat this as a property declaration with a missing name.
            var name = CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Diagnostics.Declaration_expected);
            return ParsePropertyDeclaration(pos, hasJSDoc, modifiers, name, /*questionToken*/ null);
        }

        // 'isClassMemberStart' should have hinted not to attempt parsing.
        Debug.Fail("Should not have attempted to parse class member declaration.");
        return null!;
    }

    private Node ParseDecoratedExpression()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(/*allowDecorators*/ true);
        if (Token() == SyntaxKind.ClassKeyword)
        {
            return ParseClassDeclarationOrExpression(pos, hasJSDoc, modifiers, SyntaxKind.ClassExpression);
        }

        var missing = CreateMissingNode<MissingDeclaration>(SyntaxKind.MissingDeclaration, /*reportAtCurrentPosition*/ true, Diagnostics.Expression_expected);
        Utilities.SetTextRangePos(missing, pos);
        missing.Modifiers = modifiers;
        return missing;
    }

    private ClassExpression ParseClassExpression()
    {
        return (ClassExpression)ParseClassDeclarationOrExpression(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ null, SyntaxKind.ClassExpression);
    }

    private ClassDeclaration ParseClassDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        return (ClassDeclaration)ParseClassDeclarationOrExpression(pos, hasJSDoc, modifiers, SyntaxKind.ClassDeclaration);
    }

    private Node ParseClassDeclarationOrExpression(int pos, bool hasJSDoc, NodeArray? modifiers, SyntaxKind kind)
    {
        var savedAwaitContext = InAwaitContext();
        ParseExpected(SyntaxKind.ClassKeyword);

        // We don't parse the name here in await context, instead we will report a grammar error in the checker.
        var name = ParseNameOfClassDeclarationOrExpression();
        var typeParameters = ParseTypeParameters();
        if (Core.Some(modifiers, FactoryNodeTests.IsExportModifier))
        {
            SetAwaitContext(/*value*/ true);
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
        var node = kind == SyntaxKind.ClassDeclaration
            ? (Node)factory.CreateClassDeclaration(modifiers, name, typeParameters, heritageClauses, members)
            : factory.CreateClassExpression(modifiers, name, typeParameters, heritageClauses, members);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private Identifier? ParseNameOfClassDeclarationOrExpression()
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

    private bool IsImplementsClause()
    {
        return Token() == SyntaxKind.ImplementsKeyword && LookAhead(static p => p.NextTokenIsIdentifierOrKeyword());
    }

    private NodeArray? ParseHeritageClauses()
    {
        // ClassTail[Yield,Await] : (Modified) See 14.5
        //      ClassHeritage[?Yield,?Await]opt { ClassBody[?Yield,?Await]opt }

        if (IsHeritageClause())
        {
            return ParseList(ParsingContext.HeritageClauses, static p => p.ParseHeritageClause());
        }

        return null;
    }

    private HeritageClause ParseHeritageClause()
    {
        var pos = GetNodePos();
        var tok = Token();
        Debug.Assert(tok == SyntaxKind.ExtendsKeyword || tok == SyntaxKind.ImplementsKeyword); // isListElement() should ensure this.
        NextToken();
        var types = ParseDelimitedList(ParsingContext.HeritageClauseElement, static p => p.ParseExpressionWithTypeArguments());
        return FinishNode(factory.CreateHeritageClause(tok, types), pos);
    }

    private ExpressionWithTypeArguments ParseExpressionWithTypeArguments()
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

    private NodeArray? TryParseTypeArguments()
    {
        return Token() == SyntaxKind.LessThanToken ?
            ParseBracketedList(ParsingContext.TypeArguments, static p => p.ParseType(), SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken) : null;
    }

    private bool IsHeritageClause()
    {
        return Token() == SyntaxKind.ExtendsKeyword || Token() == SyntaxKind.ImplementsKeyword;
    }

    private NodeArray ParseClassMembers()
    {
        return ParseList(ParsingContext.ClassMembers, static p => p.ParseClassElement());
    }

    private InterfaceDeclaration ParseInterfaceDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        ParseExpected(SyntaxKind.InterfaceKeyword);
        var name = ParseIdentifier();
        var typeParameters = ParseTypeParameters();
        var heritageClauses = ParseHeritageClauses();
        var members = ParseObjectTypeMembers();
        var node = factory.CreateInterfaceDeclaration(modifiers, name, typeParameters, heritageClauses, members);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private TypeAliasDeclaration ParseTypeAliasDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        ParseExpected(SyntaxKind.TypeKeyword);
        if (scanner.HasPrecedingLineBreak())
        {
            ParseErrorAtCurrentToken(Diagnostics.Line_break_not_permitted_here);
        }
        var name = ParseIdentifier();
        var typeParameters = ParseTypeParameters();
        ParseExpected(SyntaxKind.EqualsToken);
        var type = (Token() == SyntaxKind.IntrinsicKeyword ? TryParse(static p => p.ParseKeywordAndNoDot()) : null) ?? ParseType();
        ParseSemicolon();
        var node = factory.CreateTypeAliasDeclaration(modifiers, name, typeParameters, type);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    // In an ambient declaration, the grammar only allows integer literals as initializers.
    // In a non-ambient declaration, the grammar allows uninitialized members only in a
    // ConstantEnumMemberSection, which starts at the beginning of an enum declaration
    // or any time an integer literal initializer is encountered.
    private EnumMember ParseEnumMember()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var name = ParsePropertyName();
        var initializer = AllowInAnd(static p => p.ParseInitializer());
        return WithJSDoc(FinishNode(factory.CreateEnumMember(name, initializer), pos), hasJSDoc);
    }

    private EnumDeclaration ParseEnumDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        ParseExpected(SyntaxKind.EnumKeyword);
        var name = ParseIdentifier();
        NodeArray members;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            members = DoOutsideOfYieldAndAwaitContext(p => ParseDelimitedList(ParsingContext.EnumMembers, static p => p.ParseEnumMember()));
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            members = CreateMissingList();
        }
        var node = factory.CreateEnumDeclaration(modifiers, name, members);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private ModuleBlock ParseModuleBlock()
    {
        var pos = GetNodePos();
        NodeArray statements;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            statements = ParseList(ParsingContext.BlockStatements, static p => p.ParseStatement());
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            statements = CreateMissingList();
        }
        return FinishNode(factory.CreateModuleBlock(statements), pos);
    }

    private ModuleDeclaration ParseModuleOrNamespaceDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers, NodeFlags flags)
    {
        // If we are parsing a dotted namespace name, we want to
        // propagate the 'Namespace' flag across the names if set.
        var namespaceFlag = flags & NodeFlags.Namespace;
        var name = (flags & NodeFlags.NestedNamespace) != 0 ? ParseIdentifierName() : ParseIdentifier();
        var body = ParseOptional(SyntaxKind.DotToken)
            ? (Node)ParseModuleOrNamespaceDeclaration(GetNodePos(), /*hasJSDoc*/ false, /*modifiers*/ null, NodeFlags.NestedNamespace | namespaceFlag)
            : ParseModuleBlock();
        var node = factory.CreateModuleDeclaration(modifiers, name, body, flags);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private ModuleDeclaration ParseAmbientExternalModuleDeclaration(int pos, bool hasJSDoc, NodeArray? modifiersIn)
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

    private ModuleDeclaration ParseModuleDeclaration(int pos, bool hasJSDoc, NodeArray? modifiersIn)
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

    private bool IsExternalModuleReference()
    {
        return Token() == SyntaxKind.RequireKeyword &&
            LookAhead(static p => p.NextTokenIsOpenParen());
    }

    private bool NextTokenIsOpenParen()
    {
        return NextToken() == SyntaxKind.OpenParenToken;
    }

    private bool NextTokenIsOpenBrace()
    {
        return NextToken() == SyntaxKind.OpenBraceToken;
    }

    private bool NextTokenIsSlash()
    {
        return NextToken() == SyntaxKind.SlashToken;
    }

    private NamespaceExportDeclaration ParseNamespaceExportDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
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

    private Node ParseImportDeclarationOrImportEqualsDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        ParseExpected(SyntaxKind.ImportKeyword);

        var afterImportPos = scanner.GetTokenFullStart();

        // We don't parse the identifier here in await context, instead we will report a grammar error in the checker.
        Identifier? identifier = null;
        if (IsIdentifier())
        {
            identifier = ParseIdentifier();
        }

        SyntaxKind? phaseModifier = null;
        if (
            identifier != null && (string)identifier.EscapedText! == "type" &&
            (Token() != SyntaxKind.FromKeyword || IsIdentifier() && LookAhead(static p => p.NextTokenIsFromKeywordOrEqualsToken())) &&
            (IsIdentifier() || TokenAfterImportDefinitelyProducesImportDeclaration()))
        {
            phaseModifier = SyntaxKind.TypeKeyword;
            identifier = IsIdentifier() ? ParseIdentifier() : null;
        }
        else if (
            identifier != null && (string)identifier.EscapedText! == "defer" &&
            (Token() == SyntaxKind.FromKeyword ? !LookAhead(static p => p.NextTokenIsStringLiteral()) : Token() != SyntaxKind.CommaToken && Token() != SyntaxKind.EqualsToken))
        {
            phaseModifier = SyntaxKind.DeferKeyword;
            identifier = IsIdentifier() ? ParseIdentifier() : null;
        }

        if (identifier != null && !TokenAfterImportedIdentifierDefinitelyProducesImportDeclaration() && phaseModifier != SyntaxKind.DeferKeyword)
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

    private ImportClause? TryParseImportClause(Identifier? identifier, int pos, SyntaxKind? phaseModifier, bool skipJsDocLeadingAsterisks = false)
    {
        // ImportDeclaration:
        //  import ImportClause from ModuleSpecifier ;
        //  import ModuleSpecifier;
        ImportClause? importClause = null;
        if (
            identifier != null || // import id
            Token() == SyntaxKind.AsteriskToken || // import *
            Token() == SyntaxKind.OpenBraceToken) // import {
        {
            importClause = ParseImportClause(identifier, pos, phaseModifier, skipJsDocLeadingAsterisks);
            ParseExpected(SyntaxKind.FromKeyword);
        }
        return importClause;
    }

    private ImportAttributes? TryParseImportAttributes()
    {
        // (TypeScript names this local `currentToken`, shadowing the parser variable.)
        var currentTokenLocal = Token();
        if (currentTokenLocal == SyntaxKind.WithKeyword || (currentTokenLocal == SyntaxKind.AssertKeyword && !scanner.HasPrecedingLineBreak()))
        {
            return ParseImportAttributes(currentTokenLocal);
        }
        return null;
    }

    private ImportAttribute ParseImportAttribute()
    {
        var pos = GetNodePos();
        var name = Scanner.TokenIsIdentifierOrKeyword(Token()) ? ParseIdentifierName() : ParseLiteralLikeNode(SyntaxKind.StringLiteral);
        ParseExpected(SyntaxKind.ColonToken);
        var value = ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);
        return FinishNode(factory.CreateImportAttribute(name, value), pos);
    }

    private ImportAttributes ParseImportAttributes(SyntaxKind token, bool? skipKeyword = null)
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
            var elements = ParseDelimitedList(ParsingContext.ImportAttributes, static p => p.ParseImportAttribute(), /*considerSemicolonAsDelimiter*/ true);
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
            var elements = CreateNodeArray(new NodeArray(), GetNodePos(), /*end*/ null, /*hasTrailingComma*/ false);
            return FinishNode(factory.CreateImportAttributes(elements, /*multiLine*/ false, token), pos);
        }
    }

    private bool TokenAfterImportDefinitelyProducesImportDeclaration()
    {
        return Token() == SyntaxKind.AsteriskToken || Token() == SyntaxKind.OpenBraceToken;
    }

    private bool TokenAfterImportedIdentifierDefinitelyProducesImportDeclaration()
    {
        // In `import id ___`, the current token decides whether to produce
        // an ImportDeclaration or ImportEqualsDeclaration.
        return Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.FromKeyword;
    }

    private ImportEqualsDeclaration ParseImportEqualsDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers, Identifier identifier, bool isTypeOnly)
    {
        ParseExpected(SyntaxKind.EqualsToken);
        var moduleReference = ParseModuleReference();
        ParseSemicolon();
        var node = factory.CreateImportEqualsDeclaration(modifiers, isTypeOnly, identifier, moduleReference);
        var finished = WithJSDoc(FinishNode(node, pos), hasJSDoc);
        return finished;
    }

    private ImportClause ParseImportClause(Identifier? identifier, int pos, SyntaxKind? phaseModifier, bool skipJsDocLeadingAsterisks)
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

    private Node ParseModuleReference()
    {
        return IsExternalModuleReference()
            ? ParseExternalModuleReference()
            : ParseEntityName(/*allowReservedWords*/ false);
    }

    private ExternalModuleReference ParseExternalModuleReference()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.RequireKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);
        var expression = ParseModuleSpecifier();
        ParseExpected(SyntaxKind.CloseParenToken);
        return FinishNode(factory.CreateExternalModuleReference(expression), pos);
    }

    private Node ParseModuleSpecifier()
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

    private NamespaceImport ParseNamespaceImport()
    {
        // NameSpaceImport:
        //  * as ImportedBinding
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.AsteriskToken);
        ParseExpected(SyntaxKind.AsKeyword);
        var name = ParseIdentifier();
        return FinishNode(factory.CreateNamespaceImport(name), pos);
    }

    private bool CanParseModuleExportName()
    {
        return Scanner.TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.StringLiteral;
    }

    private Node ParseModuleExportName(Func<Identifier> parseName)
    {
        return Token() == SyntaxKind.StringLiteral ? ParseLiteralNode() : parseName();
    }

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
            ? (Node)factory.CreateNamedImports(ParseBracketedList(ParsingContext.ImportOrExportSpecifiers, static p => p.ParseImportSpecifier(), SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken))
            : factory.CreateNamedExports(ParseBracketedList(ParsingContext.ImportOrExportSpecifiers, static p => p.ParseExportSpecifier(), SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken));
        return FinishNode(node, pos);
    }

    private ExportSpecifier ParseExportSpecifier()
    {
        var hasJSDoc = HasPrecedingJSDocComment();
        return WithJSDoc((ExportSpecifier)ParseImportOrExportSpecifier(SyntaxKind.ExportSpecifier), hasJSDoc);
    }

    private ImportSpecifier ParseImportSpecifier()
    {
        return (ImportSpecifier)ParseImportOrExportSpecifier(SyntaxKind.ImportSpecifier);
    }

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
        Node? propertyName = null;
        var canParseAsKeyword = true;
        var name = ParseModuleExportName(() => ParseIdentifierName());
        if (name.Kind == SyntaxKind.Identifier && (string)((Identifier)name).EscapedText! == "type")
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
                name = Utilities.SetTextRangePosEnd(CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ false), name.Pos, name.Pos);
            }
            else if (checkIdentifierIsKeyword)
            {
                ParseErrorAt(checkIdentifierStart, checkIdentifierEnd, Diagnostics.Identifier_expected);
            }
        }
        var node = kind == SyntaxKind.ImportSpecifier
            ? (Node)factory.CreateImportSpecifier(isTypeOnly, propertyName, (Identifier)name)
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

    private NamespaceExport ParseNamespaceExport(int pos)
    {
        return FinishNode(factory.CreateNamespaceExport(ParseModuleExportName(() => ParseIdentifierName())), pos);
    }

    private ExportDeclaration ParseExportDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        var savedAwaitContext = InAwaitContext();
        SetAwaitContext(/*value*/ true);
        Node? exportClause = null;
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
        // (TypeScript names this local `currentToken`, shadowing the parser variable.)
        var currentTokenLocal = Token();
        if (moduleSpecifier != null && (currentTokenLocal == SyntaxKind.WithKeyword || currentTokenLocal == SyntaxKind.AssertKeyword) && !scanner.HasPrecedingLineBreak())
        {
            attributes = ParseImportAttributes(currentTokenLocal);
        }
        ParseSemicolon();
        SetAwaitContext(savedAwaitContext);
        var node = factory.CreateExportDeclaration(modifiers, isTypeOnly, exportClause, moduleSpecifier, attributes);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    private ExportAssignment ParseExportAssignment(int pos, bool hasJSDoc, NodeArray? modifiers)
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
