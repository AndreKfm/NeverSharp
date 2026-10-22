// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/parser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/parser/parser.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; the arrow functions parser.ts passes to its helpers are structs (Track O, O-NA).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// Track O (O-NA): a callback the parser passes to its grammar helpers (TypeScript: an arrow function such as the
/// `parseElement` of parseList or the callback of lookAhead and tryParse) as a struct, so a call creates no delegate or
/// closure and binds statically (typescript-go passes method expressions such as `(*Parser).nextTokenIsSlash`,
/// internal/parser/parser.go). Arguments and captured values are the struct's fields.
/// </summary>
internal interface IParserCallback<out T>
{
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    T Invoke(Parser parser);
}

internal sealed partial class Parser
{
    /// <summary>The parser's callbacks (arrow functions of parser.ts), named after the function each calls.</summary>
    private static class F
    {
        internal readonly struct CanFollowContextualOfKeyword : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.CanFollowContextualOfKeyword();
        }

        internal readonly struct IsClassMemberStart : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.IsClassMemberStart();
        }

        internal readonly struct IsDeclaration : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.IsDeclaration();
        }

        internal readonly struct IsNextNonwhitespaceTokenEndOfFile : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.IsNextNonwhitespaceTokenEndOfFile();
        }

        internal readonly struct IsParenthesizedArrowFunctionExpressionWorker : IParserCallback<Tristate>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Tristate Invoke(Parser p) => p.IsParenthesizedArrowFunctionExpressionWorker();
        }

        internal readonly struct IsStartOfMappedType : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.IsStartOfMappedType();
        }

        internal readonly struct IsStartOfParenthesizedOrFunctionType : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.IsStartOfParenthesizedOrFunctionType();
        }

        internal readonly struct IsStartOfTypeOfImportType : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.IsStartOfTypeOfImportType();
        }

        internal readonly struct IsTupleElementName : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.IsTupleElementName();
        }

        internal readonly struct IsTypeMemberStart : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.IsTypeMemberStart();
        }

        internal readonly struct IsUnambiguouslyIndexSignature : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.IsUnambiguouslyIndexSignature();
        }

        internal readonly struct IsUnambiguouslyStartOfFunctionType : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.IsUnambiguouslyStartOfFunctionType();
        }

        internal readonly struct IsUnParenthesizedAsyncArrowFunctionWorker : IParserCallback<Tristate>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Tristate Invoke(Parser p) => p.IsUnParenthesizedAsyncArrowFunctionWorker();
        }

        internal readonly struct IsValidHeritageClauseObjectLiteral : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.IsValidHeritageClauseObjectLiteral();
        }

        internal readonly struct NextToken : IParserCallback<SyntaxKind>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public SyntaxKind Invoke(Parser p) => p.NextToken();
        }

        internal readonly struct NextTokenCanFollowDefaultKeyword : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenCanFollowDefaultKeyword();
        }

        internal readonly struct NextTokenCanFollowExportModifier : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenCanFollowExportModifier();
        }

        internal readonly struct NextTokenCanFollowModifier : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenCanFollowModifier();
        }

        internal readonly struct NextTokenIsBindingIdentifierOrStartOfDestructuring : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsBindingIdentifierOrStartOfDestructuring();
        }

        internal readonly struct NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine();
        }

        internal readonly struct NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOf : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOf();
        }

        internal readonly struct NextTokenIsClassKeywordOnSameLine : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsClassKeywordOnSameLine();
        }

        internal readonly struct NextTokenIsDot : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsDot();
        }

        internal readonly struct NextTokenIsEqualsOrSemicolonOrColonToken : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsEqualsOrSemicolonOrColonToken();
        }

        internal readonly struct NextTokenIsFromKeywordOrEqualsToken : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsFromKeywordOrEqualsToken();
        }

        internal readonly struct NextTokenIsFunctionKeywordOnSameLine : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsFunctionKeywordOnSameLine();
        }

        internal readonly struct NextTokenIsIdentifier : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsIdentifier();
        }

        internal readonly struct NextTokenIsIdentifierOrKeyword : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsIdentifierOrKeyword();
        }

        internal readonly struct NextTokenIsIdentifierOrKeywordOnSameLine : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsIdentifierOrKeywordOnSameLine();
        }

        internal readonly struct NextTokenIsIdentifierOrKeywordOrGreaterThan : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsIdentifierOrKeywordOrGreaterThan();
        }

        internal readonly struct NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine();
        }

        internal readonly struct NextTokenIsIdentifierOrKeywordOrOpenBracketOrTemplate : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsIdentifierOrKeywordOrOpenBracketOrTemplate();
        }

        internal readonly struct NextTokenIsNewKeyword : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsNewKeyword();
        }

        internal readonly struct NextTokenIsNumericOrBigIntLiteral : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsNumericOrBigIntLiteral();
        }

        internal readonly struct NextTokenIsOnSameLineAndCanFollowModifier : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsOnSameLineAndCanFollowModifier();
        }

        internal readonly struct NextTokenIsOpenBrace : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsOpenBrace();
        }

        internal readonly struct NextTokenIsOpenParen : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsOpenParen();
        }

        internal readonly struct NextTokenIsOpenParenOrLessThan : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsOpenParenOrLessThan();
        }

        internal readonly struct NextTokenIsOpenParenOrLessThanOrDot : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsOpenParenOrLessThanOrDot();
        }

        internal readonly struct NextTokenIsSlash : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsSlash();
        }

        internal readonly struct NextTokenIsStartOfExpression : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsStartOfExpression();
        }

        internal readonly struct NextTokenIsStartOfType : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsStartOfType();
        }

        internal readonly struct NextTokenIsStringLiteral : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsStringLiteral();
        }

        internal readonly struct NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine();
        }

        internal readonly struct ParseArgumentExpression : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseArgumentExpression();
        }

        internal readonly struct ParseArgumentOrArrayLiteralElement : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseArgumentOrArrayLiteralElement();
        }

        internal readonly struct ParseArrayBindingElement : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseArrayBindingElement();
        }

        internal readonly struct ParseAssignmentExpressionOrHigher : IParserCallback<Node>
        {
            private readonly bool allowReturnTypeInArrowFunction;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ParseAssignmentExpressionOrHigher(bool allowReturnTypeInArrowFunction)
            {
                this.allowReturnTypeInArrowFunction = allowReturnTypeInArrowFunction;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction);
        }

        internal readonly struct ParseCaseOrDefaultClause : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseCaseOrDefaultClause();
        }

        internal readonly struct ParseClassElement : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseClassElement();
        }

        internal readonly struct ParseDecoratorExpression : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseDecoratorExpression();
        }

        internal readonly struct ParseEnumMember : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseEnumMember();
        }

        internal readonly struct ParseExportSpecifier : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseExportSpecifier();
        }

        internal readonly struct ParseExpression : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseExpression();
        }

        internal readonly struct ParseExpressionWithTypeArguments : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseExpressionWithTypeArguments();
        }

        internal readonly struct ParseHeritageClause : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseHeritageClause();
        }

        internal readonly struct ParseImportAttribute : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseImportAttribute();
        }

        internal readonly struct ParseImportSpecifier : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseImportSpecifier();
        }

        internal readonly struct ParseInitializer : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseInitializer();
        }

        internal readonly struct ParseIntersectionTypeOrHigher : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseIntersectionTypeOrHigher();
        }

        internal readonly struct ParseJSDocLinkPrefix : IParserCallback<string?>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public string? Invoke(Parser p) => p.ParseJSDocLinkPrefix();
        }

        internal readonly struct ParseJSDocParameter : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseJSDocParameter();
        }

        internal readonly struct ParseJSDocType : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseJSDocType();
        }

        internal readonly struct ParseJsxAttribute : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseJsxAttribute();
        }

        internal readonly struct ParseKeywordAndNoDot : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseKeywordAndNoDot();
        }

        internal readonly struct ParseLeftHandSideExpressionOrHigher : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseLeftHandSideExpressionOrHigher();
        }

        internal readonly struct ParseModifiers : IParserCallback<NodeList>
        {
            private readonly bool allowDecorators;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ParseModifiers(bool allowDecorators)
            {
                this.allowDecorators = allowDecorators;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public NodeList Invoke(Parser p) => p.ParseModifiers(allowDecorators);
        }

        internal readonly struct ParseObjectBindingElement : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseObjectBindingElement();
        }

        internal readonly struct ParseObjectLiteralElement : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseObjectLiteralElement();
        }

        internal readonly struct ParseOptionalBindingIdentifier : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseOptionalBindingIdentifier();
        }

        internal readonly struct ParseParameter : IParserCallback<Node>
        {
            private readonly bool inOuterAwaitContext;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ParseParameter(bool inOuterAwaitContext)
            {
                this.inOuterAwaitContext = inOuterAwaitContext;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseParameter(inOuterAwaitContext);
        }

        internal readonly struct ParsePostfixTypeOrHigher : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParsePostfixTypeOrHigher();
        }

        internal readonly struct ParseSimpleUnaryExpression : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseSimpleUnaryExpression();
        }

        internal readonly struct ParseStatement : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseStatement();
        }

        internal readonly struct ParseTupleElementNameOrTupleElementType : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseTupleElementNameOrTupleElementType();
        }

        internal readonly struct ParseType : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseType();
        }

        internal readonly struct ParseTypeArgumentsInExpression : IParserCallback<NodeList>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public NodeList Invoke(Parser p) => p.ParseTypeArgumentsInExpression();
        }

        internal readonly struct ParseTypeMember : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseTypeMember();
        }

        internal readonly struct ParseTypeOperatorOrHigher : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseTypeOperatorOrHigher();
        }

        internal readonly struct ParseTypeOrTypePredicate : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseTypeOrTypePredicate();
        }

        internal readonly struct ParseTypeParameter : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseTypeParameter();
        }

        internal readonly struct ParseTypePredicatePrefix : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseTypePredicatePrefix();
        }

        internal readonly struct TryParseConstraintOfInferType : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.TryParseConstraintOfInferType();
        }

        // `() => parseDelimitedList(kind, parseElement)`, the element parser a struct without state.
        internal readonly struct DelimitedList<TElement> : IParserCallback<NodeList>
            where TElement : struct, IParserCallback<Node>
        {
            private readonly ParsingContext kind;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal DelimitedList(ParsingContext kind)
            {
                this.kind = kind;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public NodeList Invoke(Parser p) => p.ParseDelimitedList(kind, default(TElement));
        }

        // `inForStatementInitializer ? parseVariableDeclaration : parseVariableDeclarationAllowExclamation`.
        internal readonly struct ParseVariableDeclarationOf : IParserCallback<Node>
        {
            private readonly bool inForStatementInitializer;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ParseVariableDeclarationOf(bool inForStatementInitializer)
            {
                this.inForStatementInitializer = inForStatementInitializer;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => inForStatementInitializer ? p.ParseVariableDeclaration() : p.ParseVariableDeclarationAllowExclamation();
        }

        internal readonly struct ParseConstructorStringLiteral : IParserCallback<Node>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseConstructorStringLiteral();
        }

        internal readonly struct ParseConstructorDeclarationAfterModifiers : IParserCallback<Node>
        {
            private readonly int pos;
            private readonly bool hasJSDoc;
            private readonly NodeList modifiers;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ParseConstructorDeclarationAfterModifiers(int pos, bool hasJSDoc, NodeList modifiers)
            {
                this.pos = pos;
                this.hasJSDoc = hasJSDoc;
                this.modifiers = modifiers;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseConstructorDeclarationAfterModifiers(pos, hasJSDoc, modifiers);
        }

        internal readonly struct ParsePropertyOrMethodDeclaration : IParserCallback<Node>
        {
            private readonly int pos;
            private readonly bool hasJSDoc;
            private readonly NodeList modifiers;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ParsePropertyOrMethodDeclaration(int pos, bool hasJSDoc, NodeList modifiers)
            {
                this.pos = pos;
                this.hasJSDoc = hasJSDoc;
                this.modifiers = modifiers;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParsePropertyOrMethodDeclaration(pos, hasJSDoc, modifiers);
        }

        internal readonly struct ParsePossibleParenthesizedArrowFunctionExpression : IParserCallback<Node>
        {
            private readonly bool allowReturnTypeInArrowFunction;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ParsePossibleParenthesizedArrowFunctionExpression(bool allowReturnTypeInArrowFunction)
            {
                this.allowReturnTypeInArrowFunction = allowReturnTypeInArrowFunction;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParsePossibleParenthesizedArrowFunctionExpression(allowReturnTypeInArrowFunction);
        }

        internal readonly struct IsArrowFunctionInJsx : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.IsArrowFunctionInJsx();
        }

        // `allowAmbiguity ? (savedAwaitContext ? ...) : ...` of parseParametersWorker: parseParameter or parseParameterForSpeculation.
        internal readonly struct ParseParameterOrForSpeculation : IParserCallback<Node>
        {
            private readonly bool inOuterAwaitContext;
            private readonly bool forSpeculation;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ParseParameterOrForSpeculation(bool inOuterAwaitContext, bool forSpeculation)
            {
                this.inOuterAwaitContext = inOuterAwaitContext;
                this.forSpeculation = forSpeculation;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => forSpeculation ? p.ParseParameterForSpeculation(inOuterAwaitContext) : p.ParseParameter(inOuterAwaitContext);
        }

        internal readonly struct ParseChildParameterOrPropertyTag3 : IParserCallback<Node>
        {
            private readonly PropertyLikeParse target;
            private readonly int indent;
            private readonly Node name;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ParseChildParameterOrPropertyTag3(PropertyLikeParse target, int indent, Node name)
            {
                this.target = target;
                this.indent = indent;
                this.name = name;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseChildParameterOrPropertyTag(target, indent, name);
        }

        internal readonly struct ParseChildParameterOrPropertyTag2 : IParserCallback<Node>
        {
            private readonly PropertyLikeParse target;
            private readonly int indent;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ParseChildParameterOrPropertyTag2(PropertyLikeParse target, int indent)
            {
                this.target = target;
                this.indent = indent;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseChildParameterOrPropertyTag(target, indent);
        }

        internal readonly struct ParseChildPropertyTag : IParserCallback<Node>
        {
            private readonly int indent;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ParseChildPropertyTag(int indent)
            {
                this.indent = indent;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseChildPropertyTag(indent);
        }

        internal readonly struct ParseReturnTagOfSignature : IParserCallback<Node>
        {
            private readonly int indent;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ParseReturnTagOfSignature(int indent)
            {
                this.indent = indent;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseReturnTagOfSignature(indent);
        }

        internal readonly struct NextTokensAreJSDocLinkTag : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextTokensAreJSDocLinkTag();
        }

        // `() => nextToken() === SyntaxKind.NumericLiteral && nextToken() !== SyntaxKind.ColonToken` (parseJsonText).
        internal readonly struct NextTokenIsNumericLiteralNotFollowedByColon : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextToken() == SyntaxKind.NumericLiteral && p.NextToken() != SyntaxKind.ColonToken;
        }

        // `() => nextToken() !== SyntaxKind.ColonToken` (parseJsonText).
        internal readonly struct NextTokenIsNotColon : IParserCallback<bool>
        {
            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.NextToken() != SyntaxKind.ColonToken;
        }

        internal readonly struct ParseJsxElementOrSelfClosingElementOrFragment : IParserCallback<Node>
        {
            private readonly bool inExpressionContext;
            private readonly int topInvalidNodePosition;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ParseJsxElementOrSelfClosingElementOrFragment(bool inExpressionContext, int topInvalidNodePosition)
            {
                this.inExpressionContext = inExpressionContext;
                this.topInvalidNodePosition = topInvalidNodePosition;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseJsxElementOrSelfClosingElementOrFragment(inExpressionContext, topInvalidNodePosition);
        }

        internal readonly struct ParseDeclarationWorker : IParserCallback<Node>
        {
            private readonly int pos;
            private readonly bool hasJSDoc;
            private readonly NodeList modifiers;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ParseDeclarationWorker(int pos, bool hasJSDoc, NodeList modifiers)
            {
                this.pos = pos;
                this.hasJSDoc = hasJSDoc;
                this.modifiers = modifiers;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ParseDeclarationWorker(pos, hasJSDoc, modifiers);
        }

        internal readonly struct ReuseAmbientDeclaration : IParserCallback<Node>
        {
            private readonly int pos;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ReuseAmbientDeclaration(int pos)
            {
                this.pos = pos;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public Node Invoke(Parser p) => p.ReuseAmbientDeclaration(pos);
        }

        internal readonly struct ReparseStatements : IParserCallback<bool>
        {
            private readonly ListBuilder statements;
            private readonly Node sourceFile;
            private readonly Node nextStatement;

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            internal ReparseStatements(ListBuilder statements, Node sourceFile, Node nextStatement)
            {
                this.statements = statements;
                this.sourceFile = sourceFile;
                this.nextStatement = nextStatement;
            }

            [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
            public bool Invoke(Parser p) => p.ReparseStatements(statements, sourceFile, nextStatement);
        }
    }
}
