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

/// <summary>
/// parser.ts: `interface MissingList&lt;T&gt; extends NodeArray&lt;T&gt; { isMissingList: true }`. TypeScript tags the
/// array object with an extra property; a C# NodeArray carries the tag in this field.
/// </summary>
public sealed partial class NodeArray
{
    internal bool IsMissingListMarker;
}

internal sealed partial class Parser
{
    // parser.ts lines 2835-4963 of namespace Parser: list parsing, entity names, template literals, types.

    internal bool IsListElement(ParsingContext parsingContext, bool inErrorRecovery)
    {
        Node? node = CurrentNode(parsingContext);
        if (node != null)
        {
            return true;
        }

        switch (parsingContext)
        {
            case ParsingContext.SourceElements:
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauseStatements:
                // If we're in error recovery, then we don't want to treat ';' as an empty statement.
                // The problem is that ';' can show up in far too many contexts, and if we see one
                // and assume it's a statement, then we may bail out inappropriately from whatever
                // we're parsing.  For example, if we have a semicolon in the middle of a class, then
                // we really don't want to assume the class is over and we're on a statement in the
                // outer module.  We just want to consume and move on.
                return !(Token() == SyntaxKind.SemicolonToken && inErrorRecovery) && IsStartOfStatement();
            case ParsingContext.SwitchClauses:
                return Token() == SyntaxKind.CaseKeyword || Token() == SyntaxKind.DefaultKeyword;
            case ParsingContext.TypeMembers:
                return LookAhead(IsTypeMemberStart);
            case ParsingContext.ClassMembers:
                // We allow semicolons as class elements (as specified by ES6) as long as we're
                // not in error recovery.  If we're in error recovery, we don't want an errant
                // semicolon to be treated as a class member (since they're almost always used
                // for statements.
                return LookAhead(IsClassMemberStart) || (Token() == SyntaxKind.SemicolonToken && !inErrorRecovery);
            case ParsingContext.EnumMembers:
                // Include open bracket computed properties. This technically also lets in indexers,
                // which would be a candidate for improved error reporting.
                return Token() == SyntaxKind.OpenBracketToken || IsLiteralPropertyName();
            case ParsingContext.ObjectLiteralMembers:
                switch (Token())
                {
                    case SyntaxKind.OpenBracketToken:
                    case SyntaxKind.AsteriskToken:
                    case SyntaxKind.DotDotDotToken:
                    case SyntaxKind.DotToken: // Not an object literal member, but don't want to close the object (see `tests/cases/fourslash/completionsDotInObjectLiteral.ts`)
                        return true;
                    default:
                        return IsLiteralPropertyName();
                }

            case ParsingContext.RestProperties:
                return IsLiteralPropertyName();
            case ParsingContext.ObjectBindingElements:
                return Token() == SyntaxKind.OpenBracketToken || Token() == SyntaxKind.DotDotDotToken || IsLiteralPropertyName();
            case ParsingContext.ImportAttributes:
                return IsImportAttributeName();
            case ParsingContext.HeritageClauseElement:
                // If we see `{ ... }` then only consume it as an expression if it is followed by `,` or `{`
                // That way we won't consume the body of a class in its heritage clause.
                if (Token() == SyntaxKind.OpenBraceToken)
                {
                    return LookAhead(IsValidHeritageClauseObjectLiteral);
                }

                if (!inErrorRecovery)
                {
                    return IsStartOfLeftHandSideExpression() && !IsHeritageClauseExtendsOrImplementsKeyword();
                }
                else
                {
                    // If we're in error recovery we tighten up what we're willing to match.
                    // That way we don't treat something like "this" as a valid heritage clause
                    // element during recovery.
                    return IsIdentifier() && !IsHeritageClauseExtendsOrImplementsKeyword();
                }

            case ParsingContext.VariableDeclarations:
                return IsBindingIdentifierOrPrivateIdentifierOrPattern();
            case ParsingContext.ArrayBindingElements:
                return Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.DotDotDotToken || IsBindingIdentifierOrPrivateIdentifierOrPattern();
            case ParsingContext.TypeParameters:
                return Token() == SyntaxKind.InKeyword || Token() == SyntaxKind.ConstKeyword || IsIdentifier();
            case ParsingContext.ArrayLiteralMembers:
                switch (Token())
                {
                    case SyntaxKind.CommaToken:
                    case SyntaxKind.DotToken: // Not an array literal member, but don't want to close the array (see `tests/cases/fourslash/completionsDotInArrayLiteralInObjectLiteral.ts`)
                        return true;
                }

                // falls through to ParsingContext.ArgumentExpressions
                return Token() == SyntaxKind.DotDotDotToken || IsStartOfExpression();
            case ParsingContext.ArgumentExpressions:
                return Token() == SyntaxKind.DotDotDotToken || IsStartOfExpression();
            case ParsingContext.Parameters:
                return IsStartOfParameter(/*isJSDocParameter*/ false);
            case ParsingContext.JSDocParameters:
                return IsStartOfParameter(/*isJSDocParameter*/ true);
            case ParsingContext.TypeArguments:
            case ParsingContext.TupleElementTypes:
                return Token() == SyntaxKind.CommaToken || IsStartOfType();
            case ParsingContext.HeritageClauses:
                return IsHeritageClause();
            case ParsingContext.ImportOrExportSpecifiers:
                // bail out if the next token is [FromKeyword StringLiteral].
                // That means we're in something like `import { from "mod"`. Stop here can give better error message.
                if (Token() == SyntaxKind.FromKeyword && LookAhead(NextTokenIsStringLiteral))
                {
                    return false;
                }

                if (Token() == SyntaxKind.StringLiteral)
                {
                    return true; // For "arbitrary module namespace identifiers"
                }

                return Scanner.TokenIsIdentifierOrKeyword(Token());
            case ParsingContext.JsxAttributes:
                return Scanner.TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.OpenBraceToken;
            case ParsingContext.JsxChildren:
                return true;
            case ParsingContext.JSDocComment:
                return true;
            case ParsingContext.Count:
                Debug.Fail("ParsingContext.Count used as a context"); // Not a real context, only a marker.
                return false;
            default:
                Debug.AssertNever(parsingContext, "Non-exhaustive case in 'isListElement'.");
                return false;
        }
    }

    internal bool IsValidHeritageClauseObjectLiteral()
    {
        Debug.Assert(Token() == SyntaxKind.OpenBraceToken);
        if (NextToken() == SyntaxKind.CloseBraceToken)
        {
            // if we see "extends {}" then only treat the {} as what we're extending (and not
            // the class body) if we have:
            //
            //      extends {} {
            //      extends {},
            //      extends {} extends
            //      extends {} implements
            SyntaxKind next = NextToken();
            return next == SyntaxKind.CommaToken || next == SyntaxKind.OpenBraceToken || next == SyntaxKind.ExtendsKeyword || next == SyntaxKind.ImplementsKeyword;
        }

        return true;
    }

    internal bool NextTokenIsIdentifier()
    {
        NextToken();
        return IsIdentifier();
    }

    internal bool NextTokenIsIdentifierOrKeyword()
    {
        NextToken();
        return Scanner.TokenIsIdentifierOrKeyword(Token());
    }

    internal bool NextTokenIsIdentifierOrKeywordOrGreaterThan()
    {
        NextToken();
        return Scanner.TokenIsIdentifierOrKeywordOrGreaterThan(Token());
    }

    internal bool IsHeritageClauseExtendsOrImplementsKeyword()
    {
        if (
            Token() == SyntaxKind.ImplementsKeyword ||
            Token() == SyntaxKind.ExtendsKeyword)
        {
            return LookAhead(NextTokenIsStartOfExpression);
        }

        return false;
    }

    internal bool NextTokenIsStartOfExpression()
    {
        NextToken();
        return IsStartOfExpression();
    }

    internal bool NextTokenIsStartOfType()
    {
        NextToken();
        return IsStartOfType();
    }

    // True if positioned at a list terminator
    internal bool IsListTerminator(ParsingContext kind)
    {
        if (Token() == SyntaxKind.EndOfFileToken)
        {
            // Being at the end of the file ends all lists.
            return true;
        }

        switch (kind)
        {
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauses:
            case ParsingContext.TypeMembers:
            case ParsingContext.ClassMembers:
            case ParsingContext.EnumMembers:
            case ParsingContext.ObjectLiteralMembers:
            case ParsingContext.ObjectBindingElements:
            case ParsingContext.ImportOrExportSpecifiers:
            case ParsingContext.ImportAttributes:
                return Token() == SyntaxKind.CloseBraceToken;
            case ParsingContext.SwitchClauseStatements:
                return Token() == SyntaxKind.CloseBraceToken || Token() == SyntaxKind.CaseKeyword || Token() == SyntaxKind.DefaultKeyword;
            case ParsingContext.HeritageClauseElement:
                return Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.ExtendsKeyword || Token() == SyntaxKind.ImplementsKeyword;
            case ParsingContext.VariableDeclarations:
                return IsVariableDeclaratorListTerminator();
            case ParsingContext.TypeParameters:
                // Tokens other than '>' are here for better error recovery
                return Token() == SyntaxKind.GreaterThanToken || Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.ExtendsKeyword || Token() == SyntaxKind.ImplementsKeyword;
            case ParsingContext.ArgumentExpressions:
                // Tokens other than ')' are here for better error recovery
                return Token() == SyntaxKind.CloseParenToken || Token() == SyntaxKind.SemicolonToken;
            case ParsingContext.ArrayLiteralMembers:
            case ParsingContext.TupleElementTypes:
            case ParsingContext.ArrayBindingElements:
                return Token() == SyntaxKind.CloseBracketToken;
            case ParsingContext.JSDocParameters:
            case ParsingContext.Parameters:
            case ParsingContext.RestProperties:
                // Tokens other than ')' and ']' (the latter for index signatures) are here for better error recovery
                return Token() == SyntaxKind.CloseParenToken || Token() == SyntaxKind.CloseBracketToken /*|| token === SyntaxKind.OpenBraceToken*/;
            case ParsingContext.TypeArguments:
                // All other tokens should cause the type-argument to terminate except comma token
                return Token() != SyntaxKind.CommaToken;
            case ParsingContext.HeritageClauses:
                return Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.CloseBraceToken;
            case ParsingContext.JsxAttributes:
                return Token() == SyntaxKind.GreaterThanToken || Token() == SyntaxKind.SlashToken;
            case ParsingContext.JsxChildren:
                return Token() == SyntaxKind.LessThanToken && LookAhead(NextTokenIsSlash);
            default:
                return false;
        }
    }

    internal bool IsVariableDeclaratorListTerminator()
    {
        // If we can consume a semicolon (either explicitly, or with ASI), then consider us done
        // with parsing the list of variable declarators.
        if (CanParseSemicolon())
        {
            return true;
        }

        // in the case where we're parsing the variable declarator of a 'for-in' statement, we
        // are done if we see an 'in' keyword in front of us. Same with for-of
        if (IsInOrOfKeyword(Token()))
        {
            return true;
        }

        // ERROR RECOVERY TWEAK:
        // For better error recovery, if we see an '=>' then we just stop immediately.  We've got an
        // arrow function here and it's going to be very unlikely that we'll resynchronize and get
        // another variable declaration.
        if (Token() == SyntaxKind.EqualsGreaterThanToken)
        {
            return true;
        }

        // Keep trying to parse out variable declarators.
        return false;
    }

    // True if positioned at element or terminator of the current list or any enclosing list
    internal bool IsInSomeParsingContext()
    {
        // We should be in at least one parsing context, be it SourceElements while parsing
        // a SourceFile, or JSDocComment when lazily parsing JSDoc.
        Debug.Assert(parsingContext != 0, "Missing parsing context");
        for (int kind = 0; kind < (int)ParsingContext.Count; kind++)
        {
            if ((parsingContext & (1 << kind)) != 0)
            {
                if (IsListElement((ParsingContext)kind, /*inErrorRecovery*/ true) || IsListTerminator((ParsingContext)kind))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Parses a list of elements
    internal NodeArray ParseList(ParsingContext kind, Func<Node> parseElement)
    {
        int saveParsingContext = parsingContext;
        parsingContext |= 1 << (int)kind;
        List<Node> list = new();
        int listPos = GetNodePos();

        while (!IsListTerminator(kind))
        {
            if (IsListElement(kind, /*inErrorRecovery*/ false))
            {
                list.Add(ParseListElement(kind, parseElement));

                continue;
            }

            if (AbortParsingListOrMoveToNextToken(kind))
            {
                break;
            }
        }

        parsingContext = saveParsingContext;
        return CreateNodeArray(list, listPos);
    }

    /// <summary>TS `parseListElement&lt;T extends Node | undefined&gt;`: T is `Node` or `Node?` here.</summary>
    internal T ParseListElement<T>(ParsingContext parsingContext, Func<T> parseElement)
        where T : Node?
    {
        Node? node = CurrentNode(parsingContext);
        if (node != null)
        {
            return (T)ConsumeNode(node);
        }

        return parseElement();
    }

    internal Node? CurrentNode(ParsingContext parsingContext, int? pos = null)
    {
        // If we don't have a cursor or the parsing context isn't reusable, there's nothing to reuse.
        //
        // If there is an outstanding parse error that we've encountered, but not attached to
        // some node, then we cannot get a node from the old source tree.  This is because we
        // want to mark the next node we encounter as being unusable.
        //
        // Note: This may be too conservative.  Perhaps we could reuse the node and set the bit
        // on it (or its leftmost child) as having the error.  For now though, being conservative
        // is nice and likely won't ever affect perf.
        if (syntaxCursor == null || !IsReusableParsingContext(parsingContext) || parseErrorBeforeNextFinishedNode)
        {
            return null;
        }

        Node? node = syntaxCursor.CurrentNode(pos ?? scanner.GetTokenFullStart());

        // Can't reuse a missing node.
        // Can't reuse a node that intersected the change range.
        // Can't reuse a node that contains a parse error.  This is necessary so that we
        // produce the same set of errors again.
        // (`node == null` is nodeIsMissing(undefined) of TypeScript.)
        if (node == null || Utilities.NodeIsMissing(node) || IntersectsIncrementalChange(node) || Utilities.ContainsParseError(node))
        {
            return null;
        }

        // We can only reuse a node if it was parsed under the same strict mode that we're
        // currently in.  i.e. if we originally parsed a node in non-strict mode, but then
        // the user added 'using strict' at the top of the file, then we can't use that node
        // again as the presence of strict mode may cause us to parse the tokens in the file
        // differently.
        //
        // Note: we *can* reuse tokens when the strict mode changes.  That's because tokens
        // are unaffected by strict mode.  It's just the parser will decide what to do with it
        // differently depending on what mode it is in.
        //
        // This also applies to all our other context flags as well.
        NodeFlags nodeContextFlags = node.Flags & NodeFlags.ContextFlags;
        if (nodeContextFlags != contextFlags)
        {
            return null;
        }

        // Ok, we have a node that looks like it could be reused.  Now verify that it is valid
        // in the current list parsing context that we're currently at.
        if (!CanReuseNode(node, parsingContext))
        {
            return null;
        }

        // TypeScript: `if (canHaveJSDoc(node) && node.jsDoc?.jsDocCache) { node.jsDoc.jsDocCache = undefined; }`.
        // `jsDocCache` is a property of TypeScript's JSDocArray that only getJSDocCommentsAndTags (checker/services) ever
        // sets; the parser never does and the generated `JsDoc` lists have no such member, so there is nothing to clear.
        return node;
    }

    internal Node ConsumeNode(Node node)
    {
        // Move the scanner so it is after the node we just consumed.
        scanner.ResetTokenState(node.End);
        NextToken();
        return node;
    }

    internal static bool IsReusableParsingContext(ParsingContext parsingContext)
    {
        switch (parsingContext)
        {
            case ParsingContext.ClassMembers:
            case ParsingContext.SwitchClauses:
            case ParsingContext.SourceElements:
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauseStatements:
            case ParsingContext.EnumMembers:
            case ParsingContext.TypeMembers:
            case ParsingContext.VariableDeclarations:
            case ParsingContext.JSDocParameters:
            case ParsingContext.Parameters:
                return true;
        }

        return false;
    }

    internal static bool CanReuseNode(Node node, ParsingContext parsingContext)
    {
        switch (parsingContext)
        {
            case ParsingContext.ClassMembers:
                return IsReusableClassMember(node);

            case ParsingContext.SwitchClauses:
                return IsReusableSwitchClause(node);

            case ParsingContext.SourceElements:
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauseStatements:
                return IsReusableStatement(node);

            case ParsingContext.EnumMembers:
                return IsReusableEnumMember(node);

            case ParsingContext.TypeMembers:
                return IsReusableTypeMember(node);

            case ParsingContext.VariableDeclarations:
                return IsReusableVariableDeclaration(node);

            case ParsingContext.JSDocParameters:
            case ParsingContext.Parameters:
                return IsReusableParameter(node);

                // Any other lists we do not care about reusing nodes in.  But feel free to add if
                // you can do so safely.  Danger areas involve nodes that may involve speculative
                // parsing.  If speculative parsing is involved with the node, then the range the
                // parser reached while looking ahead might be in the edited range (see the example
                // in canReuseVariableDeclaratorNode for a good case of this).

                // case ParsingContext.HeritageClauses:
                // This would probably be safe to reuse.  There is no speculative parsing with
                // heritage clauses.

                // case ParsingContext.TypeParameters:
                // This would probably be safe to reuse.  There is no speculative parsing with
                // type parameters.  Note that that's because type *parameters* only occur in
                // unambiguous *type* contexts.  While type *arguments* occur in very ambiguous
                // *expression* contexts.

                // case ParsingContext.TupleElementTypes:
                // This would probably be safe to reuse.  There is no speculative parsing with
                // tuple types.

                // Technically, type argument list types are probably safe to reuse.  While
                // speculative parsing is involved with them (since type argument lists are only
                // produced from speculative parsing a < as a type argument list), we only have
                // the types because speculative parsing succeeded.  Thus, the lookahead never
                // went past the end of the list and rewound.
                // case ParsingContext.TypeArguments:

                // Note: these are almost certainly not safe to ever reuse.  Expressions commonly
                // need a large amount of lookahead, and we should not reuse them as they may
                // have actually intersected the edit.
                // case ParsingContext.ArgumentExpressions:

                // This is not safe to reuse for the same reason as the 'AssignmentExpression'
                // cases.  i.e. a property assignment may end with an expression, and thus might
                // have lookahead far beyond it's old node.
                // case ParsingContext.ObjectLiteralMembers:

                // This is probably not safe to reuse.  There can be speculative parsing with
                // type names in a heritage clause.  There can be generic names in the type
                // name list, and there can be left hand side expressions (which can have type
                // arguments.)
                // case ParsingContext.HeritageClauseElement:

                // Perhaps safe to reuse, but it's unlikely we'd see more than a dozen attributes
                // on any given element. Same for children.
                // case ParsingContext.JsxAttributes:
                // case ParsingContext.JsxChildren:
        }

        return false;
    }

    internal static bool IsReusableClassMember(Node node)
    {
        if (node != null)
        {
            switch (node.Kind)
            {
                case SyntaxKind.Constructor:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.SemicolonClassElement:
                    return true;
                case SyntaxKind.MethodDeclaration:
                {
                    // Method declarations are not necessarily reusable.  An object-literal
                    // may have a method calls "constructor(...)" and we must reparse that
                    // into an actual .ConstructorDeclaration.
                    MethodDeclaration methodDeclaration = (MethodDeclaration)node;
                    bool nameIsConstructor = methodDeclaration.Name.Kind == SyntaxKind.Identifier &&
                        (string?)((Identifier)methodDeclaration.Name).EscapedText == "constructor";

                    return !nameIsConstructor;
                }
            }
        }

        return false;
    }

    internal static bool IsReusableSwitchClause(Node node)
    {
        if (node != null)
        {
            switch (node.Kind)
            {
                case SyntaxKind.CaseClause:
                case SyntaxKind.DefaultClause:
                    return true;
            }
        }

        return false;
    }

    internal static bool IsReusableStatement(Node node)
    {
        if (node != null)
        {
            switch (node.Kind)
            {
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.VariableStatement:
                case SyntaxKind.Block:
                case SyntaxKind.IfStatement:
                case SyntaxKind.ExpressionStatement:
                case SyntaxKind.ThrowStatement:
                case SyntaxKind.ReturnStatement:
                case SyntaxKind.SwitchStatement:
                case SyntaxKind.BreakStatement:
                case SyntaxKind.ContinueStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.WithStatement:
                case SyntaxKind.EmptyStatement:
                case SyntaxKind.TryStatement:
                case SyntaxKind.LabeledStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.DebuggerStatement:
                case SyntaxKind.ImportDeclaration:
                case SyntaxKind.ImportEqualsDeclaration:
                case SyntaxKind.ExportDeclaration:
                case SyntaxKind.ExportAssignment:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.TypeAliasDeclaration:
                    return true;
            }
        }

        return false;
    }

    internal static bool IsReusableEnumMember(Node node)
    {
        return node.Kind == SyntaxKind.EnumMember;
    }

    internal static bool IsReusableTypeMember(Node node)
    {
        if (node != null)
        {
            switch (node.Kind)
            {
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.CallSignature:
                    return true;
            }
        }

        return false;
    }

    internal static bool IsReusableVariableDeclaration(Node node)
    {
        if (node.Kind != SyntaxKind.VariableDeclaration)
        {
            return false;
        }

        // Very subtle incremental parsing bug.  Consider the following code:
        //
        //      let v = new List < A, B
        //
        // This is actually legal code.  It's a list of variable declarators "v = new List<A"
        // on one side and "B" on the other. If you then change that to:
        //
        //      let v = new List < A, B >()
        //
        // then we have a problem.  "v = new List<A" doesn't intersect the change range, so we
        // start reparsing at "B" and we completely fail to handle this properly.
        //
        // In order to prevent this, we do not allow a variable declarator to be reused if it
        // has an initializer.
        VariableDeclaration variableDeclarator = (VariableDeclaration)node;
        return variableDeclarator.Initializer == null;
    }

    internal static bool IsReusableParameter(Node node)
    {
        if (node.Kind != SyntaxKind.Parameter)
        {
            return false;
        }

        // See the comment in isReusableVariableDeclaration for why we do this.
        ParameterDeclaration parameter = (ParameterDeclaration)node;
        return parameter.Initializer == null;
    }

    // Returns true if we should abort parsing.
    internal bool AbortParsingListOrMoveToNextToken(ParsingContext kind)
    {
        ParsingContextErrors(kind);
        if (IsInSomeParsingContext())
        {
            return true;
        }

        NextToken();
        return false;
    }

    /// <summary>TypeScript returns the reported diagnostic from each case; nothing uses it, so this returns nothing.</summary>
    internal void ParsingContextErrors(ParsingContext context)
    {
        switch (context)
        {
            case ParsingContext.SourceElements:
                if (Token() == SyntaxKind.DefaultKeyword)
                {
                    ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.ExportKeyword));
                }
                else
                {
                    ParseErrorAtCurrentToken(Diagnostics.Declaration_or_statement_expected);
                }

                return;
            case ParsingContext.BlockStatements:
                ParseErrorAtCurrentToken(Diagnostics.Declaration_or_statement_expected);
                return;
            case ParsingContext.SwitchClauses:
                ParseErrorAtCurrentToken(Diagnostics.case_or_default_expected);
                return;
            case ParsingContext.SwitchClauseStatements:
                ParseErrorAtCurrentToken(Diagnostics.Statement_expected);
                return;
            case ParsingContext.RestProperties: // fallthrough
            case ParsingContext.TypeMembers:
                ParseErrorAtCurrentToken(Diagnostics.Property_or_signature_expected);
                return;
            case ParsingContext.ClassMembers:
                ParseErrorAtCurrentToken(Diagnostics.Unexpected_token_A_constructor_method_accessor_or_property_was_expected);
                return;
            case ParsingContext.EnumMembers:
                ParseErrorAtCurrentToken(Diagnostics.Enum_member_expected);
                return;
            case ParsingContext.HeritageClauseElement:
                ParseErrorAtCurrentToken(Diagnostics.Expression_expected);
                return;
            case ParsingContext.VariableDeclarations:
                if (Utilities.IsKeyword(Token()))
                {
                    ParseErrorAtCurrentToken(Diagnostics._0_is_not_allowed_as_a_variable_declaration_name, Scanner.TokenToString(Token())!);
                }
                else
                {
                    ParseErrorAtCurrentToken(Diagnostics.Variable_declaration_expected);
                }

                return;
            case ParsingContext.ObjectBindingElements:
                ParseErrorAtCurrentToken(Diagnostics.Property_destructuring_pattern_expected);
                return;
            case ParsingContext.ArrayBindingElements:
                ParseErrorAtCurrentToken(Diagnostics.Array_element_destructuring_pattern_expected);
                return;
            case ParsingContext.ArgumentExpressions:
                ParseErrorAtCurrentToken(Diagnostics.Argument_expression_expected);
                return;
            case ParsingContext.ObjectLiteralMembers:
                ParseErrorAtCurrentToken(Diagnostics.Property_assignment_expected);
                return;
            case ParsingContext.ArrayLiteralMembers:
                ParseErrorAtCurrentToken(Diagnostics.Expression_or_comma_expected);
                return;
            case ParsingContext.JSDocParameters:
                ParseErrorAtCurrentToken(Diagnostics.Parameter_declaration_expected);
                return;
            case ParsingContext.Parameters:
                if (Utilities.IsKeyword(Token()))
                {
                    ParseErrorAtCurrentToken(Diagnostics._0_is_not_allowed_as_a_parameter_name, Scanner.TokenToString(Token())!);
                }
                else
                {
                    ParseErrorAtCurrentToken(Diagnostics.Parameter_declaration_expected);
                }

                return;
            case ParsingContext.TypeParameters:
                ParseErrorAtCurrentToken(Diagnostics.Type_parameter_declaration_expected);
                return;
            case ParsingContext.TypeArguments:
                ParseErrorAtCurrentToken(Diagnostics.Type_argument_expected);
                return;
            case ParsingContext.TupleElementTypes:
                ParseErrorAtCurrentToken(Diagnostics.Type_expected);
                return;
            case ParsingContext.HeritageClauses:
                ParseErrorAtCurrentToken(Diagnostics.Unexpected_token_expected);
                return;
            case ParsingContext.ImportOrExportSpecifiers:
                if (Token() == SyntaxKind.FromKeyword)
                {
                    ParseErrorAtCurrentToken(Diagnostics._0_expected, "}");
                    return;
                }

                ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
                return;
            case ParsingContext.JsxAttributes:
                ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
                return;
            case ParsingContext.JsxChildren:
                ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
                return;
            case ParsingContext.ImportAttributes:
                ParseErrorAtCurrentToken(Diagnostics.Identifier_or_string_literal_expected);
                return;
            case ParsingContext.JSDocComment:
                ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
                return;
            case ParsingContext.Count:
                Debug.Fail("ParsingContext.Count used as a context"); // Not a real context, only a marker.
                return;
            default:
                Debug.AssertNever(context);
                return;
        }
    }

    // Parses a comma-delimited list of elements
    internal NodeArray ParseDelimitedList(ParsingContext kind, Func<Node> parseElement, bool considerSemicolonAsDelimiter = false)
    {
        // TypeScript's first overload (`parseElement: () => T` with `T extends Node` returns `NodeArray<T>`): the
        // list is never undefined when the element parser never returns undefined.
        return ParseDelimitedListOrUndefined(kind, parseElement, considerSemicolonAsDelimiter)!;
    }

    /// <summary>
    /// TypeScript's second overload of parseDelimitedList (`T extends Node | undefined`): returns undefined as soon as an
    /// element parser returns undefined.
    /// </summary>
    internal NodeArray? ParseDelimitedListOrUndefined(ParsingContext kind, Func<Node?> parseElement, bool considerSemicolonAsDelimiter = false)
    {
        int saveParsingContext = parsingContext;
        parsingContext |= 1 << (int)kind;
        List<Node> list = new();
        int listPos = GetNodePos();

        int commaStart = -1; // Meaning the previous token was not a comma
        while (true)
        {
            if (IsListElement(kind, /*inErrorRecovery*/ false))
            {
                int startPos = scanner.GetTokenFullStart();
                Node? result = ParseListElement(kind, parseElement);
                if (result == null)
                {
                    parsingContext = saveParsingContext;
                    return null;
                }

                list.Add(result);
                commaStart = scanner.GetTokenStart();

                if (ParseOptional(SyntaxKind.CommaToken))
                {
                    // No need to check for a zero length node since we know we parsed a comma
                    continue;
                }

                commaStart = -1; // Back to the state where the last token was not a comma
                if (IsListTerminator(kind))
                {
                    break;
                }

                // We didn't get a comma, and the list wasn't terminated, explicitly parse
                // out a comma so we give a good error message.
                ParseExpected(SyntaxKind.CommaToken, GetExpectedCommaDiagnostic(kind));

                // If the token was a semicolon, and the caller allows that, then skip it and
                // continue.  This ensures we get back on track and don't result in tons of
                // parse errors.  For example, this can happen when people do things like use
                // a semicolon to delimit object literal members.   Note: we'll have already
                // reported an error when we called parseExpected above.
                if (considerSemicolonAsDelimiter && Token() == SyntaxKind.SemicolonToken && !scanner.HasPrecedingLineBreak())
                {
                    NextToken();
                }

                if (startPos == scanner.GetTokenFullStart())
                {
                    // What we're parsing isn't actually remotely recognizable as a element and we've consumed no tokens whatsoever
                    // Consume a token to advance the parser in some way and avoid an infinite loop
                    // This can happen when we're speculatively parsing parenthesized expressions which we think may be arrow functions,
                    // or when a modifier keyword which is disallowed as a parameter name (ie, `static` in strict mode) is supplied
                    NextToken();
                }

                continue;
            }

            if (IsListTerminator(kind))
            {
                break;
            }

            if (AbortParsingListOrMoveToNextToken(kind))
            {
                break;
            }
        }

        parsingContext = saveParsingContext;

        // Recording the trailing comma is deliberately done after the previous
        // loop, and not just if we see a list terminator. This is because the list
        // may have ended incorrectly, but it is still important to know if there
        // was a trailing comma.
        // Check if the last token was a comma.
        // Always preserve a trailing comma by marking it on the NodeArray
        return CreateNodeArray(list, listPos, /*end*/ null, commaStart >= 0);
    }

    internal static DiagnosticMessage? GetExpectedCommaDiagnostic(ParsingContext kind)
    {
        return kind == ParsingContext.EnumMembers ? Diagnostics.An_enum_member_name_must_be_followed_by_a_or : null;
    }

    internal NodeArray CreateMissingList()
    {
        NodeArray list = CreateNodeArray(new List<Node>(), GetNodePos());
        list.IsMissingListMarker = true;
        return list;
    }

    internal static bool IsMissingList(NodeArray arr)
    {
        return arr.IsMissingListMarker;
    }

    internal NodeArray ParseBracketedList(ParsingContext kind, Func<Node> parseElement, SyntaxKind open, SyntaxKind close)
    {
        if (ParseExpected(open))
        {
            NodeArray result = ParseDelimitedList(kind, parseElement);
            ParseExpected(close);
            return result;
        }

        return CreateMissingList();
    }

    internal Node ParseEntityName(bool allowReservedWords, DiagnosticMessage? diagnosticMessage = null)
    {
        int pos = GetNodePos();
        Node entity = allowReservedWords ? ParseIdentifierName(diagnosticMessage) : ParseIdentifier(diagnosticMessage);
        while (ParseOptional(SyntaxKind.DotToken))
        {
            if (Token() == SyntaxKind.LessThanToken)
            {
                // The entity is part of a JSDoc-style generic. We will use the gap between `typeName` and
                // `typeArguments` to report it as a grammar error in the checker.
                break;
            }

            entity = FinishNode(
                factory.CreateQualifiedName(
                    entity,
                    (Identifier)ParseRightSideOfDot(allowReservedWords, /*allowPrivateIdentifiers*/ false, /*allowUnicodeEscapeSequenceInIdentifierName*/ true)),
                pos);
        }

        return entity;
    }

    internal QualifiedName CreateQualifiedName(Node entity, Identifier name)
    {
        return FinishNode(factory.CreateQualifiedName(entity, name), entity.Pos);
    }

    /// <summary>Returns `Identifier | PrivateIdentifier`.</summary>
    internal Node ParseRightSideOfDot(bool allowIdentifierNames, bool allowPrivateIdentifiers, bool allowUnicodeEscapeSequenceInIdentifierName)
    {
        // Technically a keyword is valid here as all identifiers and keywords are identifier names.
        // However, often we'll encounter this in error situations when the identifier or keyword
        // is actually starting another valid construct.
        //
        // So, we check for the following specific case:
        //
        //      name.
        //      identifierOrKeyword identifierNameOrKeyword
        //
        // Note: the newlines are important here.  For example, if that above code
        // were rewritten into:
        //
        //      name.identifierOrKeyword
        //      identifierNameOrKeyword
        //
        // Then we would consider it valid.  That's because ASI would take effect and
        // the code would be implicitly: "name.identifierOrKeyword; identifierNameOrKeyword".
        // In the first case though, ASI will not take effect because there is not a
        // line terminator after the identifier or keyword.
        if (scanner.HasPrecedingLineBreak() && Scanner.TokenIsIdentifierOrKeyword(Token()))
        {
            bool matchesPattern = LookAhead(NextTokenIsIdentifierOrKeywordOnSameLine);

            if (matchesPattern)
            {
                // Report that we need an identifier.  However, report it right after the dot,
                // and not on the next token.  This is because the next token might actually
                // be an identifier and the error would be quite confusing.
                return CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Diagnostics.Identifier_expected);
            }
        }

        if (Token() == SyntaxKind.PrivateIdentifier)
        {
            PrivateIdentifier node = ParsePrivateIdentifier();
            if (allowPrivateIdentifiers)
            {
                return node;
            }

            return CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Diagnostics.Identifier_expected);
        }

        if (allowIdentifierNames)
        {
            return allowUnicodeEscapeSequenceInIdentifierName ? ParseIdentifierName() : ParseIdentifierNameErrorOnUnicodeEscapeSequence();
        }

        return ParseIdentifier();
    }

    internal NodeArray ParseTemplateSpans(bool isTaggedTemplate)
    {
        int pos = GetNodePos();
        List<Node> list = new();
        TemplateSpan node;
        do
        {
            node = ParseTemplateSpan(isTaggedTemplate);
            list.Add(node);
        }
        while (node.Literal.Kind == SyntaxKind.TemplateMiddle);
        return CreateNodeArray(list, pos);
    }

    internal TemplateExpression ParseTemplateExpression(bool isTaggedTemplate)
    {
        int pos = GetNodePos();
        return FinishNode(
            factory.CreateTemplateExpression(
                ParseTemplateHead(isTaggedTemplate),
                ParseTemplateSpans(isTaggedTemplate)),
            pos);
    }

    internal TemplateLiteralTypeNode ParseTemplateType()
    {
        int pos = GetNodePos();
        return FinishNode(
            factory.CreateTemplateLiteralType(
                ParseTemplateHead(/*isTaggedTemplate*/ false),
                ParseTemplateTypeSpans()),
            pos);
    }

    internal NodeArray ParseTemplateTypeSpans()
    {
        int pos = GetNodePos();
        List<Node> list = new();
        TemplateLiteralTypeSpan node;
        do
        {
            node = ParseTemplateTypeSpan();
            list.Add(node);
        }
        while (node.Literal.Kind == SyntaxKind.TemplateMiddle);
        return CreateNodeArray(list, pos);
    }

    internal TemplateLiteralTypeSpan ParseTemplateTypeSpan()
    {
        int pos = GetNodePos();
        return FinishNode(
            factory.CreateTemplateLiteralTypeSpan(
                ParseType(),
                ParseLiteralOfTemplateSpan(/*isTaggedTemplate*/ false)),
            pos);
    }

    /// <summary>Returns `TemplateMiddle | TemplateTail`.</summary>
    internal Node ParseLiteralOfTemplateSpan(bool isTaggedTemplate)
    {
        if (Token() == SyntaxKind.CloseBraceToken)
        {
            ReScanTemplateToken(isTaggedTemplate);
            return ParseTemplateMiddleOrTemplateTail();
        }
        else
        {
            // TODO(rbuckton): Do we need to call `parseExpectedToken` or can we just call `createMissingNode` directly?
            return ParseExpectedToken<TemplateTail>(SyntaxKind.TemplateTail, Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.CloseBraceToken));
        }
    }

    internal TemplateSpan ParseTemplateSpan(bool isTaggedTemplate)
    {
        int pos = GetNodePos();
        return FinishNode(
            factory.CreateTemplateSpan(
                AllowInAnd(ParseExpression),
                ParseLiteralOfTemplateSpan(isTaggedTemplate)),
            pos);
    }

    /// <summary>Returns a `LiteralExpression`.</summary>
    internal Node ParseLiteralNode()
    {
        return ParseLiteralLikeNode(Token());
    }

    internal TemplateHead ParseTemplateHead(bool isTaggedTemplate)
    {
        if (!isTaggedTemplate && (scanner.GetTokenFlags() & TokenFlags.IsInvalid) != 0)
        {
            ReScanTemplateToken(/*isTaggedTemplate*/ false);
        }

        Node fragment = ParseLiteralLikeNode(Token());
        Debug.Assert(fragment.Kind == SyntaxKind.TemplateHead, "Template head has wrong token kind");
        return (TemplateHead)fragment;
    }

    /// <summary>Returns `TemplateMiddle | TemplateTail`.</summary>
    internal Node ParseTemplateMiddleOrTemplateTail()
    {
        Node fragment = ParseLiteralLikeNode(Token());
        Debug.Assert(fragment.Kind == SyntaxKind.TemplateMiddle || fragment.Kind == SyntaxKind.TemplateTail, "Template fragment has wrong token kind");
        return fragment;
    }

    internal string GetTemplateLiteralRawText(SyntaxKind kind)
    {
        bool isLast = kind == SyntaxKind.NoSubstitutionTemplateLiteral || kind == SyntaxKind.TemplateTail;
        string tokenText = scanner.GetTokenText();

        // JavaScript's `tokenText.substring(1, tokenText.length - (...))`: bounds are clamped to the string and swapped when reversed.
        int substringStart = Math.Clamp(1, 0, tokenText.Length);
        int substringEnd = Math.Clamp(tokenText.Length - (scanner.IsUnterminated() ? 0 : isLast ? 1 : 2), 0, tokenText.Length);
        if (substringStart > substringEnd)
        {
            (substringStart, substringEnd) = (substringEnd, substringStart);
        }

        return tokenText.Substring(substringStart, substringEnd - substringStart);
    }

    internal Node ParseLiteralLikeNode(SyntaxKind kind)
    {
        int pos = GetNodePos();
        Node node;
        if (UtilitiesPublic.IsTemplateLiteralKind(kind))
        {
            node = factory.CreateTemplateLiteralLikeNode(kind, scanner.GetTokenValue(), GetTemplateLiteralRawText(kind), scanner.GetTokenFlags() & TokenFlags.TemplateLiteralLikeFlags);
        }
        else if (kind == SyntaxKind.NumericLiteral)
        {
            // Note that theoretically the following condition would hold true literals like 009,
            // which is not octal. But because of how the scanner separates the tokens, we would
            // never get a token like this. Instead, we would get 00 and 9 as two separate tokens.
            // We also do not need to check for negatives because any prefix operator would be part of a
            // parent unary expression.
            node = factory.CreateNumericLiteral(scanner.GetTokenValue(), scanner.GetNumericLiteralFlags());
        }
        else if (kind == SyntaxKind.StringLiteral)
        {
            node = factory.CreateStringLiteral(scanner.GetTokenValue(), /*isSingleQuote*/ null, scanner.HasExtendedUnicodeEscape());
        }
        else if (UtilitiesPublic.IsLiteralKind(kind))
        {
            node = factory.CreateLiteralLikeNode(kind, scanner.GetTokenValue());
        }
        else
        {
            Debug.Fail();
            return null!;
        }

        if (scanner.HasExtendedUnicodeEscape())
        {
            SetLiteralHasExtendedUnicodeEscape(node);
        }

        if (scanner.IsUnterminated())
        {
            SetLiteralIsUnterminated(node);
        }

        NextToken();
        return FinishNode(node, pos);
    }

    /// <summary>TypeScript: `node.hasExtendedUnicodeEscape = true` on a `LiteralLikeNode` (a union of the literal classes).</summary>
    private static void SetLiteralHasExtendedUnicodeEscape(Node node)
    {
        switch (node)
        {
            case StringLiteral stringLiteral:
                stringLiteral.HasExtendedUnicodeEscape = true;
                break;
            case NumericLiteral numericLiteral:
                numericLiteral.HasExtendedUnicodeEscape = true;
                break;
            case BigIntLiteral bigIntLiteral:
                bigIntLiteral.HasExtendedUnicodeEscape = true;
                break;
            case JsxText jsxText:
                jsxText.HasExtendedUnicodeEscape = true;
                break;
            case RegularExpressionLiteral regularExpressionLiteral:
                regularExpressionLiteral.HasExtendedUnicodeEscape = true;
                break;
            case NoSubstitutionTemplateLiteral noSubstitutionTemplateLiteral:
                noSubstitutionTemplateLiteral.HasExtendedUnicodeEscape = true;
                break;
            case TemplateHead templateHead:
                templateHead.HasExtendedUnicodeEscape = true;
                break;
            case TemplateMiddle templateMiddle:
                templateMiddle.HasExtendedUnicodeEscape = true;
                break;
            case TemplateTail templateTail:
                templateTail.HasExtendedUnicodeEscape = true;
                break;
        }
    }

    /// <summary>TypeScript: `node.isUnterminated = true` on a `LiteralLikeNode` (a union of the literal classes).</summary>
    private static void SetLiteralIsUnterminated(Node node)
    {
        switch (node)
        {
            case StringLiteral stringLiteral:
                stringLiteral.IsUnterminated = true;
                break;
            case NumericLiteral numericLiteral:
                numericLiteral.IsUnterminated = true;
                break;
            case BigIntLiteral bigIntLiteral:
                bigIntLiteral.IsUnterminated = true;
                break;
            case JsxText jsxText:
                jsxText.IsUnterminated = true;
                break;
            case RegularExpressionLiteral regularExpressionLiteral:
                regularExpressionLiteral.IsUnterminated = true;
                break;
            case NoSubstitutionTemplateLiteral noSubstitutionTemplateLiteral:
                noSubstitutionTemplateLiteral.IsUnterminated = true;
                break;
            case TemplateHead templateHead:
                templateHead.IsUnterminated = true;
                break;
            case TemplateMiddle templateMiddle:
                templateMiddle.IsUnterminated = true;
                break;
            case TemplateTail templateTail:
                templateTail.IsUnterminated = true;
                break;
        }
    }

    // TYPES

    internal Node ParseEntityNameOfTypeReference()
    {
        return ParseEntityName(/*allowReservedWords*/ true, Diagnostics.Type_expected);
    }

    internal NodeArray? ParseTypeArgumentsOfTypeReference()
    {
        if (!scanner.HasPrecedingLineBreak() && ReScanLessThanToken() == SyntaxKind.LessThanToken)
        {
            return ParseBracketedList(ParsingContext.TypeArguments, ParseType, SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken);
        }

        return null;
    }

    internal TypeReferenceNode ParseTypeReference()
    {
        int pos = GetNodePos();
        return FinishNode(
            factory.CreateTypeReferenceNode(
                ParseEntityNameOfTypeReference(),
                ParseTypeArgumentsOfTypeReference()),
            pos);
    }

    // If true, we should abort parsing an error function.
    internal static bool TypeHasArrowFunctionBlockingParseError(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.TypeReference:
                return Utilities.NodeIsMissing(((TypeReferenceNode)node).TypeName);
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            {
                // `const { parameters, type } = node as FunctionOrConstructorTypeNode;`
                NodeArray parameters;
                Node type;
                if (node is FunctionTypeNode functionType)
                {
                    parameters = functionType.Parameters;
                    type = functionType.Type;
                }
                else
                {
                    ConstructorTypeNode constructorType = (ConstructorTypeNode)node;
                    parameters = constructorType.Parameters;
                    type = constructorType.Type;
                }

                return IsMissingList(parameters) || TypeHasArrowFunctionBlockingParseError(type);
            }

            case SyntaxKind.ParenthesizedType:
                return TypeHasArrowFunctionBlockingParseError(((ParenthesizedTypeNode)node).Type);
            default:
                return false;
        }
    }

    internal TypePredicateNode ParseThisTypePredicate(ThisTypeNode lhs)
    {
        NextToken();
        return FinishNode(factory.CreateTypePredicateNode(/*assertsModifier*/ null, lhs, ParseType()), lhs.Pos);
    }

    internal ThisTypeNode ParseThisTypeNode()
    {
        int pos = GetNodePos();
        NextToken();
        return FinishNode(factory.CreateThisTypeNode(), pos);
    }

    /// <summary>TypeScript declares `JSDocAllType | JSDocOptionalType`; only a JSDocAllType is ever created.</summary>
    internal JSDocAllType ParseJSDocAllType()
    {
        int pos = GetNodePos();
        NextToken();
        return FinishNode(factory.CreateJSDocAllType(), pos);
    }

    internal JSDocNonNullableType ParseJSDocNonNullableType()
    {
        int pos = GetNodePos();
        NextToken();
        return FinishNode(factory.CreateJSDocNonNullableType(ParseNonArrayType(), /*postfix*/ false), pos);
    }

    /// <summary>Returns `JSDocUnknownType | JSDocNullableType`.</summary>
    internal Node ParseJSDocUnknownOrNullableType()
    {
        int pos = GetNodePos();
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

    /// <summary>Returns `JSDocFunctionType | TypeReferenceNode`.</summary>
    internal Node ParseJSDocFunctionType()
    {
        int pos = GetNodePos();
        bool hasJSDoc = HasPrecedingJSDocComment();
        if (TryParse(NextTokenIsOpenParen))
        {
            NodeArray parameters = ParseParameters(SignatureFlags.Type | SignatureFlags.JSDoc);
            Node? type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
            return WithJSDoc(FinishNode(factory.CreateJSDocFunctionType(parameters, type), pos), hasJSDoc);
        }

        return FinishNode(factory.CreateTypeReferenceNode(ParseIdentifierName(), /*typeArguments*/ null), pos);
    }

    internal ParameterDeclaration ParseJSDocParameter()
    {
        int pos = GetNodePos();
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

    internal Node ParseJSDocType()
    {
        scanner.SetSkipJsDocLeadingAsterisks(true);
        int pos = GetNodePos();
        if (ParseOptional(SyntaxKind.ModuleKeyword))
        {
            // TODO(rbuckton): We never set the type for a JSDocNamepathType. What should we put here?
            JSDocNamepathType moduleTag = factory.CreateJSDocNamepathType(/*type*/ null!);

            // `terminate: while (true) { switch (token()) { case ...: break terminate; default: nextTokenJSDoc(); } }`
            while (true)
            {
                SyntaxKind jsDocToken = Token();
                if (jsDocToken == SyntaxKind.CloseBraceToken ||
                    jsDocToken == SyntaxKind.EndOfFileToken ||
                    jsDocToken == SyntaxKind.CommaToken ||
                    jsDocToken == SyntaxKind.WhitespaceTrivia)
                {
                    break;
                }

                NextTokenJSDoc();
            }

            scanner.SetSkipJsDocLeadingAsterisks(false);
            return FinishNode(moduleTag, pos);
        }

        bool hasDotDotDot = ParseOptional(SyntaxKind.DotDotDotToken);
        Node type = ParseTypeOrTypePredicate();
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

    internal TypeQueryNode ParseTypeQuery()
    {
        int pos = GetNodePos();
        ParseExpected(SyntaxKind.TypeOfKeyword);
        Node entityName = ParseEntityName(/*allowReservedWords*/ true);
        // Make sure we perform ASI to prevent parsing the next line's type arguments as part of an instantiation expression.
        NodeArray? typeArguments = !scanner.HasPrecedingLineBreak() ? TryParseTypeArguments() : null;
        return FinishNode(factory.CreateTypeQueryNode(entityName, typeArguments), pos);
    }

    internal TypeParameterDeclaration ParseTypeParameter()
    {
        int pos = GetNodePos();
        NodeArray? modifiers = ParseModifiers(/*allowDecorators*/ false, /*permitConstAsModifier*/ true);
        Identifier name = ParseIdentifier();
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

        Node? defaultType = ParseOptional(SyntaxKind.EqualsToken) ? ParseType() : null;
        TypeParameterDeclaration node = factory.CreateTypeParameterDeclaration(modifiers, name, constraint, defaultType);
        node.Expression = expression;
        return FinishNode(node, pos);
    }

    internal NodeArray? ParseTypeParameters()
    {
        if (Token() == SyntaxKind.LessThanToken)
        {
            return ParseBracketedList(ParsingContext.TypeParameters, ParseTypeParameter, SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken);
        }

        return null;
    }

    internal bool IsStartOfParameter(bool isJSDocParameter)
    {
        return Token() == SyntaxKind.DotDotDotToken ||
            IsBindingIdentifierOrPrivateIdentifierOrPattern() ||
            UtilitiesPublic.IsModifierKind(Token()) ||
            Token() == SyntaxKind.AtToken ||
            IsStartOfType(/*inStartOfParameter*/ !isJSDocParameter);
    }

    internal Node ParseNameOfParameter(NodeArray? modifiers)
    {
        // FormalParameter [Yield,Await]:
        //      BindingElement[?Yield,?Await]
        Node name = ParseIdentifierOrPattern(Diagnostics.Private_identifiers_cannot_be_used_as_parameters);
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

    internal bool IsParameterNameStart()
    {
        // Be permissive about await and yield by calling isBindingIdentifier instead of isIdentifier; disallowing
        // them during a speculative parse leads to many more follow-on errors than allowing the function to parse then later
        // complaining about the use of the keywords.
        return IsBindingIdentifier() || Token() == SyntaxKind.OpenBracketToken || Token() == SyntaxKind.OpenBraceToken;
    }

    internal ParameterDeclaration ParseParameter(bool inOuterAwaitContext)
    {
        return ParseParameterWorker(inOuterAwaitContext)!;
    }

    internal ParameterDeclaration? ParseParameterForSpeculation(bool inOuterAwaitContext)
    {
        return ParseParameterWorker(inOuterAwaitContext, /*allowAmbiguity*/ false);
    }

    /// <summary>TypeScript's overloads: with `allowAmbiguity` true (the default) the result is never undefined.</summary>
    internal ParameterDeclaration? ParseParameterWorker(bool inOuterAwaitContext, bool allowAmbiguity = true)
    {
        int pos = GetNodePos();
        bool hasJSDoc = HasPrecedingJSDocComment();

        // FormalParameter [Yield,Await]:
        //      BindingElement[?Yield,?Await]

        // Decorators are parsed in the outer [Await] context, the rest of the parameter is parsed in the function's [Await] context.
        NodeArray? modifiers = inOuterAwaitContext ?
            DoInAwaitContext(() => ParseModifiers(/*allowDecorators*/ true)) :
            DoOutsideOfAwaitContext(() => ParseModifiers(/*allowDecorators*/ true));

        if (Token() == SyntaxKind.ThisKeyword)
        {
            ParameterDeclaration thisNode = factory.CreateParameterDeclaration(
                modifiers,
                /*dotDotDotToken*/ null,
                CreateIdentifier(/*isIdentifier*/ true),
                /*questionToken*/ null,
                ParseTypeAnnotation(),
                /*initializer*/ null);

            Node? modifier = Core.FirstOrUndefined(modifiers);
            if (modifier != null)
            {
                ParseErrorAtRange(modifier, Diagnostics.Neither_decorators_nor_modifiers_may_be_applied_to_this_parameters);
            }

            return WithJSDoc(FinishNode(thisNode, pos), hasJSDoc);
        }

        bool savedTopLevel = topLevel;
        topLevel = false;

        DotDotDotToken? dotDotDotToken = ParseOptionalToken<DotDotDotToken>(SyntaxKind.DotDotDotToken);

        if (!allowAmbiguity && !IsParameterNameStart())
        {
            return null;
        }

        ParameterDeclaration node = WithJSDoc(
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

    /// <summary>TypeScript's overloads: with `returnToken` `=&gt;` the result is never undefined.</summary>
    internal Node? ParseReturnType(SyntaxKind returnToken, bool isType)
    {
        if (ShouldParseReturnType(returnToken, isType))
        {
            return AllowConditionalTypesAnd(ParseTypeOrTypePredicate);
        }

        return null;
    }

    internal bool ShouldParseReturnType(SyntaxKind returnToken, bool isType)
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

    /// <summary>TypeScript's overloads: with `allowAmbiguity` true the result is never undefined.</summary>
    internal NodeArray? ParseParametersWorker(SignatureFlags flags, bool allowAmbiguity)
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
        bool savedYieldContext = InYieldContext();
        bool savedAwaitContext = InAwaitContext();

        SetYieldContext((flags & SignatureFlags.Yield) != 0);
        SetAwaitContext((flags & SignatureFlags.Await) != 0);

        NodeArray? parameters = (flags & SignatureFlags.JSDoc) != 0 ?
            ParseDelimitedListOrUndefined(ParsingContext.JSDocParameters, ParseJSDocParameter) :
            ParseDelimitedListOrUndefined(ParsingContext.Parameters, () => allowAmbiguity ? ParseParameter(savedAwaitContext) : ParseParameterForSpeculation(savedAwaitContext));

        SetYieldContext(savedYieldContext);
        SetAwaitContext(savedAwaitContext);

        return parameters;
    }

    internal NodeArray ParseParameters(SignatureFlags flags)
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

        NodeArray parameters = ParseParametersWorker(flags, /*allowAmbiguity*/ true)!;
        ParseExpected(SyntaxKind.CloseParenToken);
        return parameters;
    }

    internal void ParseTypeMemberSemicolon()
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

    /// <summary>Returns `CallSignatureDeclaration | ConstructSignatureDeclaration`.</summary>
    internal Node ParseSignatureMember(SyntaxKind kind)
    {
        int pos = GetNodePos();
        bool hasJSDoc = HasPrecedingJSDocComment();
        if (kind == SyntaxKind.ConstructSignature)
        {
            ParseExpected(SyntaxKind.NewKeyword);
        }

        NodeArray? typeParameters = ParseTypeParameters();
        NodeArray parameters = ParseParameters(SignatureFlags.Type);
        Node? type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ true);
        ParseTypeMemberSemicolon();
        Node node;
        if (kind == SyntaxKind.CallSignature)
        {
            node = factory.CreateCallSignature(typeParameters, parameters, type);
        }
        else
        {
            node = factory.CreateConstructSignature(typeParameters, parameters, type);
        }

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal bool IsIndexSignature()
    {
        return Token() == SyntaxKind.OpenBracketToken && LookAhead(IsUnambiguouslyIndexSignature);
    }

    internal bool IsUnambiguouslyIndexSignature()
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

    internal IndexSignatureDeclaration ParseIndexSignatureDeclaration(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        NodeArray parameters = ParseBracketedList(ParsingContext.Parameters, () => ParseParameter(/*inOuterAwaitContext*/ false), SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken);
        Node? type = ParseTypeAnnotation();
        ParseTypeMemberSemicolon();
        IndexSignatureDeclaration node = factory.CreateIndexSignature(modifiers, parameters, type);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>Returns `PropertySignature | MethodSignature`.</summary>
    internal Node ParsePropertyOrMethodSignature(int pos, bool hasJSDoc, NodeArray? modifiers)
    {
        Node name = ParsePropertyName();
        QuestionToken? questionToken = ParseOptionalToken<QuestionToken>(SyntaxKind.QuestionToken);
        Node node;
        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            // Method signatures don't exist in expression contexts.  So they have neither
            // [Yield] nor [Await]
            NodeArray? typeParameters = ParseTypeParameters();
            NodeArray parameters = ParseParameters(SignatureFlags.Type);
            Node? type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ true);
            node = factory.CreateMethodSignature(modifiers, name, questionToken, typeParameters, parameters, type);
        }
        else
        {
            Node? type = ParseTypeAnnotation();
            PropertySignature propertySignature = factory.CreatePropertySignature(modifiers, name, questionToken, type);
            node = propertySignature;
            // Although type literal properties cannot not have initializers, we attempt
            // to parse an initializer so we can report in the checker that an interface
            // property or type literal property cannot have an initializer.
            if (Token() == SyntaxKind.EqualsToken)
            {
                propertySignature.Initializer = ParseInitializer();
            }
        }

        ParseTypeMemberSemicolon();
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal bool IsTypeMemberStart()
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

        bool idToken = false;
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

    /// <summary>Returns a `TypeElement`.</summary>
    internal Node ParseTypeMember()
    {
        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            return ParseSignatureMember(SyntaxKind.CallSignature);
        }

        if (Token() == SyntaxKind.NewKeyword && LookAhead(NextTokenIsOpenParenOrLessThan))
        {
            return ParseSignatureMember(SyntaxKind.ConstructSignature);
        }

        int pos = GetNodePos();
        bool hasJSDoc = HasPrecedingJSDocComment();
        NodeArray? modifiers = ParseModifiers(/*allowDecorators*/ false);
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

    internal bool NextTokenIsOpenParenOrLessThan()
    {
        NextToken();
        return Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken;
    }

    internal bool NextTokenIsDot()
    {
        return NextToken() == SyntaxKind.DotToken;
    }

    internal bool NextTokenIsOpenParenOrLessThanOrDot()
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

    internal TypeLiteralNode ParseTypeLiteral()
    {
        int pos = GetNodePos();
        return FinishNode(factory.CreateTypeLiteralNode(ParseObjectTypeMembers()), pos);
    }

    internal NodeArray ParseObjectTypeMembers()
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

    internal bool IsStartOfMappedType()
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

    internal TypeParameterDeclaration ParseMappedTypeParameter()
    {
        int pos = GetNodePos();
        Identifier name = ParseIdentifierName();
        ParseExpected(SyntaxKind.InKeyword);
        Node type = ParseType();
        return FinishNode(factory.CreateTypeParameterDeclaration(/*modifiers*/ null, name, type, /*defaultType*/ null), pos);
    }

    internal MappedTypeNode ParseMappedType()
    {
        int pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        Node? readonlyToken = null;
        if (Token() == SyntaxKind.ReadonlyKeyword || Token() == SyntaxKind.PlusToken || Token() == SyntaxKind.MinusToken)
        {
            readonlyToken = ParseTokenNode<Node>();
            if (readonlyToken.Kind != SyntaxKind.ReadonlyKeyword)
            {
                ParseExpected(SyntaxKind.ReadonlyKeyword);
            }
        }

        ParseExpected(SyntaxKind.OpenBracketToken);
        TypeParameterDeclaration typeParameter = ParseMappedTypeParameter();
        Node? nameType = ParseOptional(SyntaxKind.AsKeyword) ? ParseType() : null;
        ParseExpected(SyntaxKind.CloseBracketToken);
        Node? questionToken = null;
        if (Token() == SyntaxKind.QuestionToken || Token() == SyntaxKind.PlusToken || Token() == SyntaxKind.MinusToken)
        {
            questionToken = ParseTokenNode<Node>();
            if (questionToken.Kind != SyntaxKind.QuestionToken)
            {
                ParseExpected(SyntaxKind.QuestionToken);
            }
        }

        Node? type = ParseTypeAnnotation();
        ParseSemicolon();
        NodeArray members = ParseList(ParsingContext.TypeMembers, ParseTypeMember);
        ParseExpected(SyntaxKind.CloseBraceToken);
        return FinishNode(factory.CreateMappedTypeNode(readonlyToken, typeParameter, nameType, questionToken, type, members), pos);
    }

    internal Node ParseTupleElementType()
    {
        int pos = GetNodePos();
        if (ParseOptional(SyntaxKind.DotDotDotToken))
        {
            return FinishNode(factory.CreateRestTypeNode(ParseType()), pos);
        }

        Node type = ParseType();
        if (NodeTests.IsJSDocNullableType(type) && type.Pos == ((JSDocNullableType)type).Type.Pos)
        {
            OptionalTypeNode node = factory.CreateOptionalTypeNode(((JSDocNullableType)type).Type);
            FactoryUtilitiesPublic.SetTextRange(node, type);
            node.Flags = type.Flags;
            return node;
        }

        return type;
    }

    internal bool IsNextTokenColonOrQuestionColon()
    {
        return NextToken() == SyntaxKind.ColonToken || (Token() == SyntaxKind.QuestionToken && NextToken() == SyntaxKind.ColonToken);
    }

    internal bool IsTupleElementName()
    {
        if (Token() == SyntaxKind.DotDotDotToken)
        {
            return Scanner.TokenIsIdentifierOrKeyword(NextToken()) && IsNextTokenColonOrQuestionColon();
        }

        return Scanner.TokenIsIdentifierOrKeyword(Token()) && IsNextTokenColonOrQuestionColon();
    }

    internal Node ParseTupleElementNameOrTupleElementType()
    {
        if (LookAhead(IsTupleElementName))
        {
            int pos = GetNodePos();
            bool hasJSDoc = HasPrecedingJSDocComment();
            DotDotDotToken? dotDotDotToken = ParseOptionalToken<DotDotDotToken>(SyntaxKind.DotDotDotToken);
            Identifier name = ParseIdentifierName();
            QuestionToken? questionToken = ParseOptionalToken<QuestionToken>(SyntaxKind.QuestionToken);
            ParseExpected(SyntaxKind.ColonToken);
            Node type = ParseTupleElementType();
            NamedTupleMember node = factory.CreateNamedTupleMember(dotDotDotToken, name, questionToken, type);
            return WithJSDoc(FinishNode(node, pos), hasJSDoc);
        }

        return ParseTupleElementType();
    }

    internal TupleTypeNode ParseTupleType()
    {
        int pos = GetNodePos();
        return FinishNode(
            factory.CreateTupleTypeNode(
                ParseBracketedList(ParsingContext.TupleElementTypes, ParseTupleElementNameOrTupleElementType, SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken)),
            pos);
    }

    internal ParenthesizedTypeNode ParseParenthesizedType()
    {
        int pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenParenToken);
        Node type = ParseType();
        ParseExpected(SyntaxKind.CloseParenToken);
        return FinishNode(factory.CreateParenthesizedType(type), pos);
    }

    internal NodeArray? ParseModifiersForConstructorType()
    {
        NodeArray? modifiers = null;
        if (Token() == SyntaxKind.AbstractKeyword)
        {
            int pos = GetNodePos();
            NextToken();
            Node modifier = FinishNode(factory.CreateToken(SyntaxKind.AbstractKeyword), pos);
            modifiers = CreateNodeArray(new List<Node> { modifier }, pos);
        }

        return modifiers;
    }

    internal Node ParseFunctionOrConstructorType()
    {
        int pos = GetNodePos();
        bool hasJSDoc = HasPrecedingJSDocComment();
        NodeArray? modifiers = ParseModifiersForConstructorType();
        bool isConstructorType = ParseOptional(SyntaxKind.NewKeyword);
        Debug.Assert(modifiers == null || isConstructorType, "Per isStartOfFunctionOrConstructorType, a function type cannot have modifiers.");
        NodeArray? typeParameters = ParseTypeParameters();
        NodeArray parameters = ParseParameters(SignatureFlags.Type);
        Node type = ParseReturnType(SyntaxKind.EqualsGreaterThanToken, /*isType*/ false)!;
        Node node;
        if (isConstructorType)
        {
            node = factory.CreateConstructorTypeNode(modifiers, typeParameters, parameters, type);
        }
        else
        {
            node = factory.CreateFunctionTypeNode(typeParameters, parameters, type);
        }

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    internal Node? ParseKeywordAndNoDot()
    {
        Node node = ParseTokenNode<Node>();
        return Token() == SyntaxKind.DotToken ? null : node;
    }

    internal LiteralTypeNode ParseLiteralTypeNode(bool negative = false)
    {
        int pos = GetNodePos();
        if (negative)
        {
            NextToken();
        }

        Node expression = Token() == SyntaxKind.TrueKeyword || Token() == SyntaxKind.FalseKeyword || Token() == SyntaxKind.NullKeyword ?
            ParseTokenNode<Node>() :
            ParseLiteralLikeNode(Token());
        if (negative)
        {
            expression = FinishNode(factory.CreatePrefixUnaryExpression(SyntaxKind.MinusToken, expression), pos);
        }

        return FinishNode(factory.CreateLiteralTypeNode(expression), pos);
    }

    internal bool IsStartOfTypeOfImportType()
    {
        NextToken();
        return Token() == SyntaxKind.ImportKeyword;
    }

    internal ImportTypeNode ParseImportType()
    {
        sourceFlags |= NodeFlags.PossiblyContainsDynamicImport;
        int pos = GetNodePos();
        bool isTypeOf = ParseOptional(SyntaxKind.TypeOfKeyword);
        ParseExpected(SyntaxKind.ImportKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);
        Node type = ParseType();
        ImportAttributes? attributes = null;
        if (ParseOptional(SyntaxKind.CommaToken))
        {
            int openBracePosition = scanner.GetTokenStart();
            ParseExpected(SyntaxKind.OpenBraceToken);
            SyntaxKind currentToken = Token();
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
                DiagnosticWithDetachedLocation? lastError = Core.LastOrUndefined(parseDiagnostics);
                if (lastError != null && lastError.Code == Diagnostics._0_expected.Code)
                {
                    Utilities.AddRelatedInfo(
                        lastError,
                        Utilities.CreateDetachedDiagnostic(fileName, sourceText, openBracePosition, 1, Diagnostics.The_parser_expected_to_find_a_1_to_match_the_0_token_here, "{", "}"));
                }
            }
        }

        ParseExpected(SyntaxKind.CloseParenToken);
        Node? qualifier = ParseOptional(SyntaxKind.DotToken) ? ParseEntityNameOfTypeReference() : null;
        NodeArray? typeArguments = ParseTypeArgumentsOfTypeReference();
        return FinishNode(factory.CreateImportTypeNode(type, attributes, qualifier, typeArguments, isTypeOf), pos);
    }

    internal bool NextTokenIsNumericOrBigIntLiteral()
    {
        NextToken();
        return Token() == SyntaxKind.NumericLiteral || Token() == SyntaxKind.BigIntLiteral;
    }

    internal Node ParseNonArrayType()
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
                // falls through to SyntaxKind.AsteriskToken
                return ParseJSDocAllType();
            case SyntaxKind.AsteriskToken:
                return ParseJSDocAllType();
            case SyntaxKind.QuestionQuestionToken:
                // If there is '??', treat it as prefix-'?' in JSDoc type.
                scanner.ReScanQuestionToken();
                // falls through to SyntaxKind.QuestionToken
                return ParseJSDocUnknownOrNullableType();
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
                return ParseTokenNode<Node>();
            case SyntaxKind.ThisKeyword:
            {
                ThisTypeNode thisKeyword = ParseThisTypeNode();
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

    internal bool IsStartOfType(bool inStartOfParameter = false)
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

    internal bool IsStartOfParenthesizedOrFunctionType()
    {
        NextToken();
        return Token() == SyntaxKind.CloseParenToken || IsStartOfParameter(/*isJSDocParameter*/ false) || IsStartOfType();
    }

    internal Node ParsePostfixTypeOrHigher()
    {
        int pos = GetNodePos();
        Node type = ParseNonArrayType();
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
                        Node indexType = ParseType();
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

    internal TypeOperatorNode ParseTypeOperator(SyntaxKind @operator)
    {
        int pos = GetNodePos();
        ParseExpected(@operator);
        return FinishNode(factory.CreateTypeOperatorNode(@operator, ParseTypeOperatorOrHigher()), pos);
    }

    internal Node? TryParseConstraintOfInferType()
    {
        if (ParseOptional(SyntaxKind.ExtendsKeyword))
        {
            Node constraint = DisallowConditionalTypesAnd(ParseType);
            if (InDisallowConditionalTypesContext() || Token() != SyntaxKind.QuestionToken)
            {
                return constraint;
            }
        }

        return null;
    }

    internal TypeParameterDeclaration ParseTypeParameterOfInferType()
    {
        int pos = GetNodePos();
        Identifier name = ParseIdentifier();
        Node? constraint = TryParse(TryParseConstraintOfInferType);
        TypeParameterDeclaration node = factory.CreateTypeParameterDeclaration(/*modifiers*/ null, name, constraint);
        return FinishNode(node, pos);
    }

    internal InferTypeNode ParseInferType()
    {
        int pos = GetNodePos();
        ParseExpected(SyntaxKind.InferKeyword);
        return FinishNode(factory.CreateInferTypeNode(ParseTypeParameterOfInferType()), pos);
    }

    internal Node ParseTypeOperatorOrHigher()
    {
        SyntaxKind @operator = Token();
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

    internal Node? ParseFunctionOrConstructorTypeToError(bool isInUnionType)
    {
        // the function type and constructor type shorthand notation
        // are not allowed directly in unions and intersections, but we'll
        // try to parse them gracefully and issue a helpful message.
        if (IsStartOfFunctionTypeOrConstructorType())
        {
            Node type = ParseFunctionOrConstructorType();
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

    internal Node ParseUnionOrIntersectionType(
        SyntaxKind @operator,
        Func<Node> parseConstituentType,
        Func<NodeArray, Node> createTypeNode)
    {
        int pos = GetNodePos();
        bool isUnionType = @operator == SyntaxKind.BarToken;
        bool hasLeadingOperator = ParseOptional(@operator);
        // `hasLeadingOperator && parseFunctionOrConstructorTypeToError(isUnionType) || parseConstituentType()`
        Node type = (hasLeadingOperator ? ParseFunctionOrConstructorTypeToError(isUnionType) : null)
            ?? parseConstituentType();
        if (Token() == @operator || hasLeadingOperator)
        {
            List<Node> types = new() { type };
            while (ParseOptional(@operator))
            {
                types.Add(ParseFunctionOrConstructorTypeToError(isUnionType) ?? parseConstituentType());
            }

            type = FinishNode(createTypeNode(CreateNodeArray(types, pos)), pos);
        }

        return type;
    }

    internal Node ParseIntersectionTypeOrHigher()
    {
        return ParseUnionOrIntersectionType(SyntaxKind.AmpersandToken, ParseTypeOperatorOrHigher, factory.CreateIntersectionTypeNode);
    }

    internal Node ParseUnionTypeOrHigher()
    {
        return ParseUnionOrIntersectionType(SyntaxKind.BarToken, ParseIntersectionTypeOrHigher, factory.CreateUnionTypeNode);
    }

    internal bool NextTokenIsNewKeyword()
    {
        NextToken();
        return Token() == SyntaxKind.NewKeyword;
    }

    internal bool IsStartOfFunctionTypeOrConstructorType()
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
            (Token() == SyntaxKind.AbstractKeyword && LookAhead(NextTokenIsNewKeyword));
    }

    internal bool SkipParameterStart()
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
            int previousErrorCount = parseDiagnostics.Count;
            ParseIdentifierOrPattern();
            return previousErrorCount == parseDiagnostics.Count;
        }

        return false;
    }

    internal bool IsUnambiguouslyStartOfFunctionType()
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

    internal Node ParseTypeOrTypePredicate()
    {
        int pos = GetNodePos();
        Identifier? typePredicateVariable = IsIdentifier() ? TryParse(ParseTypePredicatePrefix) : null;
        Node type = ParseType();
        if (typePredicateVariable != null)
        {
            return FinishNode(factory.CreateTypePredicateNode(/*assertsModifier*/ null, typePredicateVariable, type), pos);
        }
        else
        {
            return type;
        }
    }

    internal Identifier? ParseTypePredicatePrefix()
    {
        Identifier id = ParseIdentifier();
        if (Token() == SyntaxKind.IsKeyword && !scanner.HasPrecedingLineBreak())
        {
            NextToken();
            return id;
        }

        return null;
    }

    internal TypePredicateNode ParseAssertsTypePredicate()
    {
        int pos = GetNodePos();
        AssertsKeyword assertsModifier = ParseExpectedToken<AssertsKeyword>(SyntaxKind.AssertsKeyword);
        Node parameterName = Token() == SyntaxKind.ThisKeyword ? ParseThisTypeNode() : ParseIdentifier();
        Node? type = ParseOptional(SyntaxKind.IsKeyword) ? ParseType() : null;
        return FinishNode(factory.CreateTypePredicateNode(assertsModifier, parameterName, type), pos);
    }

    internal Node ParseType()
    {
        if ((contextFlags & NodeFlags.TypeExcludesFlags) != 0)
        {
            return DoOutsideOfContext(NodeFlags.TypeExcludesFlags, ParseType);
        }

        if (IsStartOfFunctionTypeOrConstructorType())
        {
            return ParseFunctionOrConstructorType();
        }

        int pos = GetNodePos();
        Node type = ParseUnionTypeOrHigher();
        if (!InDisallowConditionalTypesContext() && !scanner.HasPrecedingLineBreak() && ParseOptional(SyntaxKind.ExtendsKeyword))
        {
            // The type following 'extends' is not permitted to be another conditional type
            Node extendsType = DisallowConditionalTypesAnd(ParseType);
            ParseExpected(SyntaxKind.QuestionToken);
            Node trueType = AllowConditionalTypesAnd(ParseType);
            ParseExpected(SyntaxKind.ColonToken);
            Node falseType = AllowConditionalTypesAnd(ParseType);
            return FinishNode(factory.CreateConditionalTypeNode(type, extendsType, trueType, falseType), pos);
        }

        return type;
    }

    internal Node? ParseTypeAnnotation()
    {
        return ParseOptional(SyntaxKind.ColonToken) ? ParseType() : null;
    }
}
