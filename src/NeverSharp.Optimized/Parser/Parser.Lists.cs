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

/// <summary>
/// parser.ts: `interface MissingList&lt;T&gt; extends NodeArray&lt;T&gt; { isMissingList: true }`. createMissingList assigns
/// the property to the NodeArray object; a C# object cannot gain properties, so NodeArray declares it.
/// </summary>
public sealed partial class NodeArray
{
    internal bool IsMissingList;
}

/// <summary>parser.ts: `namespace Parser`, list parsing (with node reuse), entity names, templates and literals.</summary>
internal sealed partial class Parser
{
    // True if positioned at the start of a list element
    private bool IsListElement(ParsingContext parsingContext, bool inErrorRecovery)
    {
        var node = CurrentNode(parsingContext);
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
                return LookAhead(static p => p.IsTypeMemberStart());
            case ParsingContext.ClassMembers:
                // We allow semicolons as class elements (as specified by ES6) as long as we're
                // not in error recovery.  If we're in error recovery, we don't want an errant
                // semicolon to be treated as a class member (since they're almost always used
                // for statements.
                return LookAhead(static p => p.IsClassMemberStart()) || (Token() == SyntaxKind.SemicolonToken && !inErrorRecovery);
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
                    return LookAhead(static p => p.IsValidHeritageClauseObjectLiteral());
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
                // falls through
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
                if (Token() == SyntaxKind.FromKeyword && LookAhead(static p => p.NextTokenIsStringLiteral()))
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

    private bool IsValidHeritageClauseObjectLiteral()
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

            var next = NextToken();
            return next == SyntaxKind.CommaToken || next == SyntaxKind.OpenBraceToken || next == SyntaxKind.ExtendsKeyword || next == SyntaxKind.ImplementsKeyword;
        }

        return true;
    }

    private bool NextTokenIsIdentifier()
    {
        NextToken();
        return IsIdentifier();
    }

    private bool NextTokenIsIdentifierOrKeyword()
    {
        NextToken();
        return Scanner.TokenIsIdentifierOrKeyword(Token());
    }

    private bool NextTokenIsIdentifierOrKeywordOrGreaterThan()
    {
        NextToken();
        return Scanner.TokenIsIdentifierOrKeywordOrGreaterThan(Token());
    }

    private bool IsHeritageClauseExtendsOrImplementsKeyword()
    {
        if (
            Token() == SyntaxKind.ImplementsKeyword ||
            Token() == SyntaxKind.ExtendsKeyword)
        {
            return LookAhead(static p => p.NextTokenIsStartOfExpression());
        }

        return false;
    }

    private bool NextTokenIsStartOfExpression()
    {
        NextToken();
        return IsStartOfExpression();
    }

    private bool NextTokenIsStartOfType()
    {
        NextToken();
        return IsStartOfType();
    }

    // True if positioned at a list terminator
    private bool IsListTerminator(ParsingContext kind)
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
                return Token() == SyntaxKind.LessThanToken && LookAhead(static p => p.NextTokenIsSlash());
            default:
                return false;
        }
    }

    private bool IsVariableDeclaratorListTerminator()
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
    private bool IsInSomeParsingContext()
    {
        // We should be in at least one parsing context, be it SourceElements while parsing
        // a SourceFile, or JSDocComment when lazily parsing JSDoc.
        Debug.Assert(parsingContext != 0, "Missing parsing context");
        for (var kind = 0; kind < (int)ParsingContext.Count; kind++)
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
    private NodeArray ParseList(ParsingContext kind, Func<Parser, Node> parseElement)
    {
        var saveParsingContext = parsingContext;
        parsingContext |= 1 << (int)kind;
        var list = new NodeArray();
        var listPos = GetNodePos();

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

    private T ParseListElement<T>(ParsingContext parsingContext, Func<Parser, T> parseElement)
        where T : Node?
    {
        var node = CurrentNode(parsingContext);
        if (node != null)
        {
            return (T)ConsumeNode(node);
        }

        return parseElement(this);
    }

    private Node? CurrentNode(ParsingContext parsingContext, int? pos = null)
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

        var node = syntaxCursor.CurrentNode(pos ?? scanner.GetTokenFullStart());

        // Can't reuse a missing node.
        // Can't reuse a node that intersected the change range.
        // Can't reuse a node that contains a parse error.  This is necessary so that we
        // produce the same set of errors again.
        if (Utilities.NodeIsMissing(node) || IntersectsIncrementalChange(node!) || Utilities.ContainsParseError(node!))
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
        var nodeContextFlags = node!.Flags & NodeFlags.ContextFlags;
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

        // TypeScript: `if (canHaveJSDoc(node) && node.jsDoc?.jsDocCache) { node.jsDoc.jsDocCache = undefined; }`
        // jsDocCache may include tags from parent nodes, which might have been modified. The cache is
        // filled only by getJSDocTags and friends, which the parser does not reach, so it is never set
        // here; the C# JSDoc array (List<JSDoc>) does not carry it.

        return node;
    }

    private Node ConsumeNode(Node node)
    {
        // Move the scanner so it is after the node we just consumed.
        scanner.ResetTokenState(node.End);
        NextToken();
        return node;
    }

    private static bool IsReusableParsingContext(ParsingContext parsingContext)
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

    private static bool CanReuseNode(Node node, ParsingContext parsingContext)
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

    private static bool IsReusableClassMember(Node node)
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
                    // Method declarations are not necessarily reusable.  An object-literal
                    // may have a method calls "constructor(...)" and we must reparse that
                    // into an actual .ConstructorDeclaration.
                    var methodDeclaration = (MethodDeclaration)node;
                    var nameIsConstructor = methodDeclaration.Name.Kind == SyntaxKind.Identifier &&
                        (string)((Identifier)methodDeclaration.Name).EscapedText! == "constructor";

                    return !nameIsConstructor;
            }
        }

        return false;
    }

    private static bool IsReusableSwitchClause(Node node)
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

    private static bool IsReusableStatement(Node node)
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

    private static bool IsReusableEnumMember(Node node)
    {
        return node.Kind == SyntaxKind.EnumMember;
    }

    private static bool IsReusableTypeMember(Node node)
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

    private static bool IsReusableVariableDeclaration(Node node)
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
        var variableDeclarator = (VariableDeclaration)node;
        return variableDeclarator.Initializer == null;
    }

    private static bool IsReusableParameter(Node node)
    {
        if (node.Kind != SyntaxKind.Parameter)
        {
            return false;
        }

        // See the comment in isReusableVariableDeclaration for why we do this.
        var parameter = (ParameterDeclaration)node;
        return parameter.Initializer == null;
    }

    // Returns true if we should abort parsing.
    private bool AbortParsingListOrMoveToNextToken(ParsingContext kind)
    {
        ParsingContextErrors(kind);
        if (IsInSomeParsingContext())
        {
            return true;
        }

        NextToken();
        return false;
    }

    private DiagnosticWithDetachedLocation? ParsingContextErrors(ParsingContext context)
    {
        switch (context)
        {
            case ParsingContext.SourceElements:
                return Token() == SyntaxKind.DefaultKeyword
                    ? ParseErrorAtCurrentToken(Diagnostics._0_expected, Scanner.TokenToString(SyntaxKind.ExportKeyword))
                    : ParseErrorAtCurrentToken(Diagnostics.Declaration_or_statement_expected);
            case ParsingContext.BlockStatements:
                return ParseErrorAtCurrentToken(Diagnostics.Declaration_or_statement_expected);
            case ParsingContext.SwitchClauses:
                return ParseErrorAtCurrentToken(Diagnostics.case_or_default_expected);
            case ParsingContext.SwitchClauseStatements:
                return ParseErrorAtCurrentToken(Diagnostics.Statement_expected);
            case ParsingContext.RestProperties: // fallthrough
            case ParsingContext.TypeMembers:
                return ParseErrorAtCurrentToken(Diagnostics.Property_or_signature_expected);
            case ParsingContext.ClassMembers:
                return ParseErrorAtCurrentToken(Diagnostics.Unexpected_token_A_constructor_method_accessor_or_property_was_expected);
            case ParsingContext.EnumMembers:
                return ParseErrorAtCurrentToken(Diagnostics.Enum_member_expected);
            case ParsingContext.HeritageClauseElement:
                return ParseErrorAtCurrentToken(Diagnostics.Expression_expected);
            case ParsingContext.VariableDeclarations:
                return Utilities.IsKeyword(Token())
                    ? ParseErrorAtCurrentToken(Diagnostics._0_is_not_allowed_as_a_variable_declaration_name, Scanner.TokenToString(Token())!)
                    : ParseErrorAtCurrentToken(Diagnostics.Variable_declaration_expected);
            case ParsingContext.ObjectBindingElements:
                return ParseErrorAtCurrentToken(Diagnostics.Property_destructuring_pattern_expected);
            case ParsingContext.ArrayBindingElements:
                return ParseErrorAtCurrentToken(Diagnostics.Array_element_destructuring_pattern_expected);
            case ParsingContext.ArgumentExpressions:
                return ParseErrorAtCurrentToken(Diagnostics.Argument_expression_expected);
            case ParsingContext.ObjectLiteralMembers:
                return ParseErrorAtCurrentToken(Diagnostics.Property_assignment_expected);
            case ParsingContext.ArrayLiteralMembers:
                return ParseErrorAtCurrentToken(Diagnostics.Expression_or_comma_expected);
            case ParsingContext.JSDocParameters:
                return ParseErrorAtCurrentToken(Diagnostics.Parameter_declaration_expected);
            case ParsingContext.Parameters:
                return Utilities.IsKeyword(Token())
                    ? ParseErrorAtCurrentToken(Diagnostics._0_is_not_allowed_as_a_parameter_name, Scanner.TokenToString(Token())!)
                    : ParseErrorAtCurrentToken(Diagnostics.Parameter_declaration_expected);
            case ParsingContext.TypeParameters:
                return ParseErrorAtCurrentToken(Diagnostics.Type_parameter_declaration_expected);
            case ParsingContext.TypeArguments:
                return ParseErrorAtCurrentToken(Diagnostics.Type_argument_expected);
            case ParsingContext.TupleElementTypes:
                return ParseErrorAtCurrentToken(Diagnostics.Type_expected);
            case ParsingContext.HeritageClauses:
                return ParseErrorAtCurrentToken(Diagnostics.Unexpected_token_expected);
            case ParsingContext.ImportOrExportSpecifiers:
                if (Token() == SyntaxKind.FromKeyword)
                {
                    return ParseErrorAtCurrentToken(Diagnostics._0_expected, "}");
                }
                return ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
            case ParsingContext.JsxAttributes:
                return ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
            case ParsingContext.JsxChildren:
                return ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
            case ParsingContext.ImportAttributes:
                return ParseErrorAtCurrentToken(Diagnostics.Identifier_or_string_literal_expected);
            case ParsingContext.JSDocComment:
                return ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
            case ParsingContext.Count:
                Debug.Fail("ParsingContext.Count used as a context"); // Not a real context, only a marker.
                return null;
            default:
                Debug.AssertNever(context);
                return null;
        }
    }

    /// <summary>
    /// Parses a comma-delimited list of elements (TypeScript's overload whose elements are never undefined).
    /// </summary>
    private NodeArray ParseDelimitedList(ParsingContext kind, Func<Parser, Node> parseElement, bool? considerSemicolonAsDelimiter = null)
    {
        return ParseDelimitedListOrUndefined(kind, parseElement, considerSemicolonAsDelimiter)!;
    }

    /// <summary>
    /// Parses a comma-delimited list of elements. TypeScript's overload whose element parser may return undefined
    /// (then the list is undefined); C# cannot overload on the nullability of the callback, so it has its own name.
    /// </summary>
    private NodeArray? ParseDelimitedListOrUndefined(ParsingContext kind, Func<Parser, Node?> parseElement, bool? considerSemicolonAsDelimiter = null)
    {
        var saveParsingContext = parsingContext;
        parsingContext |= 1 << (int)kind;
        var list = new NodeArray();
        var listPos = GetNodePos();

        var commaStart = -1; // Meaning the previous token was not a comma
        while (true)
        {
            if (IsListElement(kind, /*inErrorRecovery*/ false))
            {
                var startPos = scanner.GetTokenFullStart();
                var result = ParseListElement(kind, parseElement);
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
                if (considerSemicolonAsDelimiter == true && Token() == SyntaxKind.SemicolonToken && !scanner.HasPrecedingLineBreak())
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

    private static DiagnosticMessage? GetExpectedCommaDiagnostic(ParsingContext kind)
    {
        return kind == ParsingContext.EnumMembers ? Diagnostics.An_enum_member_name_must_be_followed_by_a_or : null;
    }

    private NodeArray CreateMissingList()
    {
        var list = CreateNodeArray(new NodeArray(), GetNodePos());
        list.IsMissingList = true;
        return list;
    }

    private static bool IsMissingList(NodeArray arr)
    {
        return arr.IsMissingList;
    }

    private NodeArray ParseBracketedList(ParsingContext kind, Func<Parser, Node> parseElement, SyntaxKind open, SyntaxKind close)
    {
        if (ParseExpected(open))
        {
            var result = ParseDelimitedList(kind, parseElement);
            ParseExpected(close);
            return result;
        }

        return CreateMissingList();
    }

    private Node ParseEntityName(bool allowReservedWords, DiagnosticMessage? diagnosticMessage = null)
    {
        var pos = GetNodePos();
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

    private QualifiedName CreateQualifiedName(Node entity, Identifier name)
    {
        return FinishNode(factory.CreateQualifiedName(entity, name), entity.Pos);
    }

    private Node ParseRightSideOfDot(bool allowIdentifierNames, bool allowPrivateIdentifiers, bool allowUnicodeEscapeSequenceInIdentifierName)
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
            var matchesPattern = LookAhead(static p => p.NextTokenIsIdentifierOrKeywordOnSameLine());

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
            var node = ParsePrivateIdentifier();
            return allowPrivateIdentifiers ? node : CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Diagnostics.Identifier_expected);
        }

        if (allowIdentifierNames)
        {
            return allowUnicodeEscapeSequenceInIdentifierName ? ParseIdentifierName() : ParseIdentifierNameErrorOnUnicodeEscapeSequence();
        }

        return ParseIdentifier();
    }

    private NodeArray ParseTemplateSpans(bool isTaggedTemplate)
    {
        var pos = GetNodePos();
        var list = new NodeArray();
        TemplateSpan node;
        do
        {
            node = ParseTemplateSpan(isTaggedTemplate);
            list.Add(node);
        }
        while (node.Literal.Kind == SyntaxKind.TemplateMiddle);
        return CreateNodeArray(list, pos);
    }

    private TemplateExpression ParseTemplateExpression(bool isTaggedTemplate)
    {
        var pos = GetNodePos();
        return FinishNode(
            factory.CreateTemplateExpression(
                ParseTemplateHead(isTaggedTemplate),
                ParseTemplateSpans(isTaggedTemplate)),
            pos);
    }

    private TemplateLiteralTypeNode ParseTemplateType()
    {
        var pos = GetNodePos();
        return FinishNode(
            factory.CreateTemplateLiteralType(
                ParseTemplateHead(/*isTaggedTemplate*/ false),
                ParseTemplateTypeSpans()),
            pos);
    }

    private NodeArray ParseTemplateTypeSpans()
    {
        var pos = GetNodePos();
        var list = new NodeArray();
        TemplateLiteralTypeSpan node;
        do
        {
            node = ParseTemplateTypeSpan();
            list.Add(node);
        }
        while (node.Literal.Kind == SyntaxKind.TemplateMiddle);
        return CreateNodeArray(list, pos);
    }

    private TemplateLiteralTypeSpan ParseTemplateTypeSpan()
    {
        var pos = GetNodePos();
        return FinishNode(
            factory.CreateTemplateLiteralTypeSpan(
                ParseType(),
                ParseLiteralOfTemplateSpan(/*isTaggedTemplate*/ false)),
            pos);
    }

    private Node ParseLiteralOfTemplateSpan(bool isTaggedTemplate)
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

    private TemplateSpan ParseTemplateSpan(bool isTaggedTemplate)
    {
        var pos = GetNodePos();
        return FinishNode(
            factory.CreateTemplateSpan(
                AllowInAnd(static p => p.ParseExpression()),
                ParseLiteralOfTemplateSpan(isTaggedTemplate)),
            pos);
    }

    private Node ParseLiteralNode()
    {
        return ParseLiteralLikeNode(Token());
    }

    private TemplateHead ParseTemplateHead(bool isTaggedTemplate)
    {
        if (!isTaggedTemplate && (scanner.GetTokenFlags() & TokenFlags.IsInvalid) != 0)
        {
            ReScanTemplateToken(/*isTaggedTemplate*/ false);
        }
        var fragment = ParseLiteralLikeNode(Token());
        Debug.Assert(fragment.Kind == SyntaxKind.TemplateHead, "Template head has wrong token kind");
        return (TemplateHead)fragment;
    }

    private Node ParseTemplateMiddleOrTemplateTail()
    {
        var fragment = ParseLiteralLikeNode(Token());
        Debug.Assert(fragment.Kind == SyntaxKind.TemplateMiddle || fragment.Kind == SyntaxKind.TemplateTail, "Template fragment has wrong token kind");
        return fragment;
    }

    private string GetTemplateLiteralRawText(SyntaxKind kind)
    {
        var isLast = kind == SyntaxKind.NoSubstitutionTemplateLiteral || kind == SyntaxKind.TemplateTail;
        var tokenText = scanner.GetTokenText();
        return JsSubstring(tokenText, 1, tokenText.Length - (scanner.IsUnterminated() ? 0 : isLast ? 1 : 2));
    }

    private Node ParseLiteralLikeNode(SyntaxKind kind)
    {
        var pos = GetNodePos();
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
            node = null!;
        }

        if (scanner.HasExtendedUnicodeEscape())
        {
            SetLiteralLikeHasExtendedUnicodeEscape(node);
        }

        if (scanner.IsUnterminated())
        {
            SetLiteralLikeIsUnterminated(node);
        }

        NextToken();
        return FinishNode(node, pos);
    }

    /// <summary>`node.hasExtendedUnicodeEscape = true` on a LiteralLikeNode (the classes declare the field one by one).</summary>
    private static void SetLiteralLikeHasExtendedUnicodeEscape(Node node)
    {
        switch (node)
        {
            case NumericLiteral n:
                n.HasExtendedUnicodeEscape = true;
                break;
            case BigIntLiteral n:
                n.HasExtendedUnicodeEscape = true;
                break;
            case StringLiteral n:
                n.HasExtendedUnicodeEscape = true;
                break;
            case JsxText n:
                n.HasExtendedUnicodeEscape = true;
                break;
            case RegularExpressionLiteral n:
                n.HasExtendedUnicodeEscape = true;
                break;
            case NoSubstitutionTemplateLiteral n:
                n.HasExtendedUnicodeEscape = true;
                break;
            case TemplateHead n:
                n.HasExtendedUnicodeEscape = true;
                break;
            case TemplateMiddle n:
                n.HasExtendedUnicodeEscape = true;
                break;
            case TemplateTail n:
                n.HasExtendedUnicodeEscape = true;
                break;
        }
    }

    /// <summary>`node.isUnterminated = true` on a LiteralLikeNode (the classes declare the field one by one).</summary>
    private static void SetLiteralLikeIsUnterminated(Node node)
    {
        switch (node)
        {
            case NumericLiteral n:
                n.IsUnterminated = true;
                break;
            case BigIntLiteral n:
                n.IsUnterminated = true;
                break;
            case StringLiteral n:
                n.IsUnterminated = true;
                break;
            case JsxText n:
                n.IsUnterminated = true;
                break;
            case RegularExpressionLiteral n:
                n.IsUnterminated = true;
                break;
            case NoSubstitutionTemplateLiteral n:
                n.IsUnterminated = true;
                break;
            case TemplateHead n:
                n.IsUnterminated = true;
                break;
            case TemplateMiddle n:
                n.IsUnterminated = true;
                break;
            case TemplateTail n:
                n.IsUnterminated = true;
                break;
        }
    }

    /// <summary>JavaScript's String.prototype.substring(start, end): clamps to the string and swaps reversed bounds.</summary>
    private static string JsSubstring(string s, int start, int end)
    {
        var from = Math.Min(Math.Max(start, 0), s.Length);
        var to = Math.Min(Math.Max(end, 0), s.Length);
        if (from > to)
        {
            (from, to) = (to, from);
        }
        return s.Substring(from, to - from);
    }
}
