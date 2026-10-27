// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/utilities.go and internal/ast/precedence.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Text;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>utilities.ts: Associativity.</summary>
internal enum Associativity
{
    Left,
    Right,
}

/// <summary>utilities.ts: OperatorPrecedence.</summary>
internal enum OperatorPrecedence
{
    // Expression:
    //     AssignmentExpression
    //     Expression `,` AssignmentExpression
    Comma,

    // NOTE: `Spread` is higher than `Comma` due to how it is parsed in |ElementList|
    // SpreadElement:
    //     `...` AssignmentExpression
    Spread,

    // AssignmentExpression:
    //     ConditionalExpression
    //     YieldExpression
    //     ArrowFunction
    //     AsyncArrowFunction
    //     LeftHandSideExpression `=` AssignmentExpression
    //     LeftHandSideExpression AssignmentOperator AssignmentExpression
    //
    // NOTE: AssignmentExpression is broken down into several precedences due to the requirements
    //       of the parenthesizer rules.

    // AssignmentExpression: YieldExpression
    // YieldExpression:
    //     `yield`
    //     `yield` AssignmentExpression
    //     `yield` `*` AssignmentExpression
    Yield,

    // AssignmentExpression: LeftHandSideExpression `=` AssignmentExpression
    // AssignmentExpression: LeftHandSideExpression AssignmentOperator AssignmentExpression
    // AssignmentOperator: one of
    //     `*=` `/=` `%=` `+=` `-=` `<<=` `>>=` `>>>=` `&=` `^=` `|=` `**=`
    Assignment,

    // NOTE: `Conditional` is considered higher than `Assignment` here, but in reality they have
    //       the same precedence.
    // AssignmentExpression: ConditionalExpression
    // ConditionalExpression:
    //     ShortCircuitExpression
    //     ShortCircuitExpression `?` AssignmentExpression `:` AssignmentExpression
    // ShortCircuitExpression:
    //     LogicalORExpression
    //     CoalesceExpression
    Conditional,

    // LogicalORExpression:
    //     LogicalANDExpression
    //     LogicalORExpression `||` LogicalANDExpression
    LogicalOR,

    // CoalesceExpression:
    //     CoalesceExpressionHead `??` BitwiseORExpression
    // CoalesceExpressionHead:
    //     CoalesceExpression
    //     BitwiseORExpression
    Coalesce = LogicalOR, // NOTE: This is wrong

    // LogicalANDExpression:
    //     BitwiseORExpression
    //     LogicalANDExprerssion `&&` BitwiseORExpression
    LogicalAND,

    // BitwiseORExpression:
    //     BitwiseXORExpression
    //     BitwiseORExpression `^` BitwiseXORExpression
    BitwiseOR,

    // BitwiseXORExpression:
    //     BitwiseANDExpression
    //     BitwiseXORExpression `^` BitwiseANDExpression
    BitwiseXOR,

    // BitwiseANDExpression:
    //     EqualityExpression
    //     BitwiseANDExpression `^` EqualityExpression
    BitwiseAND,

    // EqualityExpression:
    //     RelationalExpression
    //     EqualityExpression `==` RelationalExpression
    //     EqualityExpression `!=` RelationalExpression
    //     EqualityExpression `===` RelationalExpression
    //     EqualityExpression `!==` RelationalExpression
    Equality,

    // RelationalExpression:
    //     ShiftExpression
    //     RelationalExpression `<` ShiftExpression
    //     RelationalExpression `>` ShiftExpression
    //     RelationalExpression `<=` ShiftExpression
    //     RelationalExpression `>=` ShiftExpression
    //     RelationalExpression `instanceof` ShiftExpression
    //     RelationalExpression `in` ShiftExpression
    //     [+TypeScript] RelationalExpression `as` Type
    Relational,

    // ShiftExpression:
    //     AdditiveExpression
    //     ShiftExpression `<<` AdditiveExpression
    //     ShiftExpression `>>` AdditiveExpression
    //     ShiftExpression `>>>` AdditiveExpression
    Shift,

    // AdditiveExpression:
    //     MultiplicativeExpression
    //     AdditiveExpression `+` MultiplicativeExpression
    //     AdditiveExpression `-` MultiplicativeExpression
    Additive,

    // MultiplicativeExpression:
    //     ExponentiationExpression
    //     MultiplicativeExpression MultiplicativeOperator ExponentiationExpression
    // MultiplicativeOperator: one of `*`, `/`, `%`
    Multiplicative,

    // ExponentiationExpression:
    //     UnaryExpression
    //     UpdateExpression `**` ExponentiationExpression
    Exponentiation,

    // UnaryExpression:
    //     UpdateExpression
    //     `delete` UnaryExpression
    //     `void` UnaryExpression
    //     `typeof` UnaryExpression
    //     `+` UnaryExpression
    //     `-` UnaryExpression
    //     `~` UnaryExpression
    //     `!` UnaryExpression
    //     AwaitExpression
    // UpdateExpression:            // TODO: Do we need to investigate the precedence here?
    //     `++` UnaryExpression
    //     `--` UnaryExpression
    Unary,

    // UpdateExpression:
    //     LeftHandSideExpression
    //     LeftHandSideExpression `++`
    //     LeftHandSideExpression `--`
    Update,

    // LeftHandSideExpression:
    //     NewExpression
    //     CallExpression
    // NewExpression:
    //     MemberExpression
    //     `new` NewExpression
    LeftHandSide,

    // CallExpression:
    //     CoverCallExpressionAndAsyncArrowHead
    //     SuperCall
    //     ImportCall
    //     CallExpression Arguments
    //     CallExpression `[` Expression `]`
    //     CallExpression `.` IdentifierName
    //     CallExpression TemplateLiteral
    // MemberExpression:
    //     PrimaryExpression
    //     MemberExpression `[` Expression `]`
    //     MemberExpression `.` IdentifierName
    //     MemberExpression TemplateLiteral
    //     SuperProperty
    //     MetaProperty
    //     `new` MemberExpression Arguments
    Member,

    // TODO: JSXElement?
    // PrimaryExpression:
    //     `this`
    //     IdentifierReference
    //     Literal
    //     ArrayLiteral
    //     ObjectLiteral
    //     FunctionExpression
    //     ClassExpression
    //     GeneratorExpression
    //     AsyncFunctionExpression
    //     AsyncGeneratorExpression
    //     RegularExpressionLiteral
    //     TemplateLiteral
    //     CoverParenthesizedExpressionAndArrowParameterList
    Primary,

    Highest = Primary,
    Lowest = Comma,

    // -1 is lower than all other precedences. Returning it will cause binary expression
    // parsing to stop.
    Invalid = -1,
}

/// <summary>utilities.ts (the units the parser reaches; the text-range and diagnostic helpers are in Core/ and Parser/Utilities.Diagnostics.cs).</summary>
internal static partial class Utilities
{
    // utilities.ts objectAllocator: the constructors of Node, Token, Identifier, PrivateIdentifier and SourceFile that the
    // parser captures in initializeState. In C# NodeAllocator.Create (generated) is the constructor of every kind's
    // class, and Parser.NewNode gives it TypeScript's (kind, pos 0, end 0); there is no object to allocate the
    // constructors from, so objectAllocator has no C# counterpart.

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static int GetFullWidth(NodeStore store, Node node)
    {
        return store.End(node) - store.Pos(node);
    }

    internal static bool ContainsParseError(NodeStore store, Node node)
    {
        AggregateChildData(store, node);
        return (store.Flags(node) & NodeFlags.ThisNodeOrAnySubNodesHasError) != 0;
    }

    private static void AggregateChildData(NodeStore store, Node node)
    {
        if ((store.Flags(node) & NodeFlags.HasAggregatedChildData) == 0)
        {
            // A node is considered to contain a parse error if:
            //  a) the parser explicitly marked that it had an error
            //  b) any of it's children reported that it had an error.
            var thisNodeOrAnySubNodesHasError = ((store.Flags(node) & NodeFlags.ThisNodeHasError) != 0) ||
                NodeVisitor.ForEachChild<object>(store, node, child => ContainsParseError(store, child) ? true : null) != null;

            // If so, mark ourselves accordingly.
            if (thisNodeOrAnySubNodesHasError)
            {
                store.SetFlags(node, store.Flags(node) | NodeFlags.ThisNodeOrAnySubNodesHasError);
            }

            // Also mark that we've propagated the child information to this node.  This way we can
            // always consult the bit directly on this node without needing to check its children
            // again.
            store.SetFlags(node, store.Flags(node) | NodeFlags.HasAggregatedChildData);
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetSourceFileOfNode(NodeStore store, Node node)
    {
        while (!node.IsNone && store.Kind(node) != SyntaxKind.SourceFile)
        {
            node = store.Parent(node);
        }
        return node;
    }

    internal static int GetEndLinePosition(int line, ISourceFileLike sourceFile)
    {
        Debug.Assert(line >= 0);
        var lineStarts = Scanner.GetLineStarts(sourceFile);

        var lineIndex = line;
        var sourceText = sourceFile.Text;
        if (lineIndex + 1 == lineStarts.Count)
        {
            // last line - return EOF
            return sourceText.Length - 1;
        }
        else
        {
            // current line start
            var start = lineStarts[lineIndex];
            // take the start position of the next line - 1 = it should be some line break
            var pos = lineStarts[lineIndex + 1] - 1;
            Debug.Assert(Scanner.IsLineBreak(CharCodeAtOrNaN(sourceText, pos)));
            // walk backwards skipping line breaks, stop the the beginning of current line.
            // i.e:
            // <some text>
            // $ <- end of line for this position should match the start position
            while (start <= pos && Scanner.IsLineBreak(CharCodeAtOrNaN(sourceText, pos)))
            {
                pos--;
            }
            return pos;
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool NodeIsMissing(NodeStore store, Node node)
    {
        if (node.IsNone)
        {
            return true;
        }

        return store.Pos(node) == store.End(node) && store.Pos(node) >= 0 && store.Kind(node) != SyntaxKind.EndOfFileToken;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool NodeIsPresent(NodeStore store, Node node)
    {
        return !NodeIsMissing(store, node);
    }

    internal static string GetSourceTextOfNodeFromSourceFile(NodeStore store, Node sourceFile, Node node, bool includeTrivia = false)
    {
        return GetTextOfNodeFromSourceText(store, store.SourceFileText(sourceFile).Chars, node, includeTrivia);
    }

    private static bool IsJSDocTypeExpressionOrChild(NodeStore store, Node node)
    {
        return !UtilitiesPublic.FindAncestor(store, node, ((a0) => FactoryNodeTests.IsJSDocTypeExpression(store, a0))).IsNone;
    }

    internal static string GetTextOfNodeFromSourceText(NodeStore store, ReadOnlySpan<char> sourceText, Node node, bool includeTrivia = false)
    {
        if (NodeIsMissing(store, node))
        {
            return "";
        }

        var start = includeTrivia ? store.Pos(node) : Scanner.SkipTrivia(sourceText, store.Pos(node));
        var text = SubstringJs(sourceText, start, store.End(node));

        if (IsJSDocTypeExpressionOrChild(store, node))
        {
            // strip space + asterisk at line start
            // (`text.split(/\r\n|\n|\r/).map(line => line.replace(/^\s*\*/, "").trimStart()).join("\n")`, written as loops)
            var builder = new StringBuilder();
            var lineStart = 0;
            var i = 0;
            while (true)
            {
                var atEnd = i >= text.Length;
                if (atEnd || text[i] == '\r' || text[i] == '\n')
                {
                    builder.Append(StripLeadingAsteriskAndTrimStart(text, lineStart, i));
                    if (atEnd)
                    {
                        break;
                    }
                    builder.Append('\n');
                    i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                    lineStart = i;
                    continue;
                }
                i++;
            }
            text = builder.ToString();
        }

        return text;
    }

    /// <summary>`line.replace(/^\s*\*/, "").trimStart()` for the line text[start..end).</summary>
    private static string StripLeadingAsteriskAndTrimStart(string text, int start, int end)
    {
        var p = start;
        while (p < end && IsJsWhiteSpaceOrLineTerminator(text[p]))
        {
            p++;
        }
        if (p < end && text[p] == '*')
        {
            start = p + 1;
        }
        while (start < end && IsJsWhiteSpaceOrLineTerminator(text[start]))
        {
            start++;
        }
        return text.Substring(start, end - start);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static EmitFlags GetEmitFlags(NodeStore store, Node node)
    {
        var emitNode = (EmitNode?)store.EmitNode(node);
        return emitNode != null ? emitNode.Flags : 0;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsComputedNonLiteralName(NodeStore store, Node name)
    {
        return store.Kind(name) == SyntaxKind.ComputedPropertyName && !IsStringOrNumericLiteralLike(store, store.Expression(name));
    }

    /// <returns>The text (Track O: a text value; <see cref="TextRef.None"/> for undefined).</returns>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TextRef TryGetTextOfPropertyName(NodeStore store, Node name)
    {
        switch (store.Kind(name))
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.PrivateIdentifier:
                return ((EmitNode?)store.EmitNode(name))?.AutoGenerate != null ? default : ((name is var id && store.IsIdentifier(id)) ? store.EscapedText(id) : store.EscapedText(name));
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                return UtilitiesPublic.EscapeLeadingUnderscores(store.Texts, GetLiteralText(store, name));
            case SyntaxKind.ComputedPropertyName:
                var expression = store.Expression(name);
                if (IsStringOrNumericLiteralLike(store, expression))
                {
                    return UtilitiesPublic.EscapeLeadingUnderscores(store.Texts, GetLiteralText(store, expression));
                }
                return default;
            case SyntaxKind.JsxNamespacedName:
                return GetEscapedTextOfJsxNamespacedName(store, name);
            default:
                Debug.AssertNever(name);
                return default;
        }
    }

    /// <summary>`(name as LiteralLikeNode).text` for the literal kinds read above.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static TextRef GetLiteralText(NodeStore store, Node node)
    {
        return node switch
        {
            var n when store.IsStringLiteral(n) => store.Text(n),
            var n when store.IsNumericLiteral(n) => store.Text(n),
            var n when store.IsBigIntLiteral(n) => store.Text(n),
            var n when store.IsNoSubstitutionTemplateLiteral(n) => store.Text(n),
            _ => throw Debug.InvalidCast(store.Kind(node)),
        };
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TextRef GetTextOfPropertyName(NodeStore store, Node name)
    {
        var text = TryGetTextOfPropertyName(store, name);
        Debug.Assert(!text.IsNone);
        return text;
    }

    internal static DiagnosticWithLocation CreateDiagnosticForNodeInSourceFile(NodeStore store, Node sourceFile, Node node, DiagnosticMessage message, params object?[] args)
    {
        var span = GetErrorSpanForNode(store, sourceFile, node);
        return CreateFileDiagnostic(store, sourceFile, span.Start, span.Length, message, args);
    }

    internal static TextSpan GetSpanOfTokenAtPosition(NodeStore store, Node sourceFile, int pos)
    {
        var scanner = Scanner.CreateScanner(store.LanguageVersion(sourceFile), /*skipTrivia*/ true, store.LanguageVariant(sourceFile), store.SourceFileText(sourceFile), /*onError*/ null, pos);
        scanner.Scan();
        var start = scanner.GetTokenStart();
        return UtilitiesPublic.CreateTextSpanFromBounds(start, scanner.GetTokenEnd());
    }

    private static TextSpan GetErrorSpanForArrowFunction(NodeStore store, Node sourceFile, Node node)
    {
        var pos = Scanner.SkipTrivia(store.SourceFileText(sourceFile).Chars, store.Pos(node));
        if (!store.Body(node).IsNone && store.Kind(store.Body(node)) == SyntaxKind.Block)
        {
            var sourceFileLike = new SourceFileNodeLike(store, sourceFile);
            var startLine = Scanner.GetLineAndCharacterOfPosition(sourceFileLike, store.Pos(store.Body(node))).Line;
            var endLine = Scanner.GetLineAndCharacterOfPosition(sourceFileLike, store.End(store.Body(node))).Line;
            if (startLine < endLine)
            {
                // The arrow function spans multiple lines,
                // make the error span be the first line, inclusive.
                return UtilitiesPublic.CreateTextSpan(pos, GetEndLinePosition(startLine, sourceFileLike) - pos + 1);
            }
        }
        return UtilitiesPublic.CreateTextSpanFromBounds(pos, store.End(node));
    }

    internal static TextSpan GetErrorSpanForNode(NodeStore store, Node sourceFile, Node node)
    {
        Node errorNode = node;
        switch (store.Kind(node))
        {
            case SyntaxKind.SourceFile:
            {
                var pos = Scanner.SkipTrivia(store.SourceFileText(sourceFile).Chars, 0, /*stopAfterLineBreak*/ false);
                if (pos == store.SourceFileText(sourceFile).Length)
                {
                    // file is empty - return span for the beginning of the file
                    return UtilitiesPublic.CreateTextSpan(0, 0);
                }
                return GetSpanOfTokenAtPosition(store, sourceFile, pos);
            }
            // This list is a work in progress. Add missing node kinds to improve their error
            // spans.
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.BindingElement:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.EnumMember:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.NamespaceImport:
                errorNode = store.Name(node);
                break;
            case SyntaxKind.ArrowFunction:
                return GetErrorSpanForArrowFunction(store, sourceFile, node);
            case SyntaxKind.CaseClause:
            case SyntaxKind.DefaultClause:
            {
                var statements = (node is var caseClause && store.IsCaseClause(caseClause)) ? store.Statements(caseClause) : store.Statements(node);
                var start = Scanner.SkipTrivia(store.SourceFileText(sourceFile).Chars, store.Pos(node));
                var end = store.Length(statements) > 0 ? store.Pos(store.Element(statements, 0)) : store.End(node);
                return UtilitiesPublic.CreateTextSpanFromBounds(start, end);
            }
            case SyntaxKind.ReturnStatement:
            case SyntaxKind.YieldExpression:
            {
                var pos = Scanner.SkipTrivia(store.SourceFileText(sourceFile).Chars, store.Pos(node));
                return GetSpanOfTokenAtPosition(store, sourceFile, pos);
            }
            case SyntaxKind.SatisfiesExpression:
            {
                var pos = Scanner.SkipTrivia(store.SourceFileText(sourceFile).Chars, store.End(store.Expression(node)));
                return GetSpanOfTokenAtPosition(store, sourceFile, pos);
            }
            case SyntaxKind.JSDocSatisfiesTag:
            {
                var pos = Scanner.SkipTrivia(store.SourceFileText(sourceFile).Chars, store.Pos(store.TagName(node)));
                return GetSpanOfTokenAtPosition(store, sourceFile, pos);
            }
            case SyntaxKind.Constructor:
            {
                var constructorDeclaration = node;
                var start = Scanner.SkipTrivia(store.SourceFileText(sourceFile).Chars, store.Pos(constructorDeclaration));
                var scanner = Scanner.CreateScanner(store.LanguageVersion(sourceFile), /*skipTrivia*/ true, store.LanguageVariant(sourceFile), store.SourceFileText(sourceFile), /*onError*/ null, start);
                var token = scanner.Scan();
                while (token != SyntaxKind.ConstructorKeyword && token != SyntaxKind.EndOfFileToken)
                {
                    token = scanner.Scan();
                }
                var end = scanner.GetTokenEnd();
                return UtilitiesPublic.CreateTextSpanFromBounds(start, end);
            }
        }

        if (errorNode.IsNone)
        {
            // If we don't have a better node, then just set the error on the first token of
            // construct.
            return GetSpanOfTokenAtPosition(store, sourceFile, store.Pos(node));
        }

        Debug.Assert(!FactoryNodeTests.IsJSDoc(store, errorNode));

        var isMissing = NodeIsMissing(store, errorNode);
        var errorPos = isMissing || FactoryNodeTests.IsJsxText(store, node)
            ? store.Pos(errorNode)
            : Scanner.SkipTrivia(store.SourceFileText(sourceFile).Chars, store.Pos(errorNode));

        // These asserts should all be satisfied for a properly constructed `errorNode`.
        if (isMissing)
        {
            Debug.Assert(errorPos == store.Pos(errorNode), "This failure could trigger https://github.com/Microsoft/TypeScript/issues/20809");
            Debug.Assert(errorPos == store.End(errorNode), "This failure could trigger https://github.com/Microsoft/TypeScript/issues/20809");
        }
        else
        {
            Debug.Assert(errorPos >= store.Pos(errorNode), "This failure could trigger https://github.com/Microsoft/TypeScript/issues/20809");
            Debug.Assert(errorPos <= store.End(errorNode), "This failure could trigger https://github.com/Microsoft/TypeScript/issues/20809");
        }

        return UtilitiesPublic.CreateTextSpanFromBounds(errorPos, store.End(errorNode));
    }

    /// <summary>Track O (O-NA): whether getJSDocCommentRanges takes the trailing comment ranges of a node of this kind too.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool HasTrailingJSDocCommentRanges(SyntaxKind kind)
    {
        return kind == SyntaxKind.Parameter ||
            kind == SyntaxKind.TypeParameter ||
            kind == SyntaxKind.FunctionExpression ||
            kind == SyntaxKind.ArrowFunction ||
            kind == SyntaxKind.ParenthesizedExpression ||
            kind == SyntaxKind.VariableDeclaration ||
            kind == SyntaxKind.ExportSpecifier;
    }

    /// <summary>Track O (O-NA): the filter of getJSDocCommentRanges for one comment range of a node ending at <paramref name="nodeEnd"/>.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocCommentRange(ReadOnlySpan<char> text, CommentRange comment, int nodeEnd)
    {
        return comment.End <= nodeEnd && // Due to parse errors sometime empty parameter may get comments assigned to it that end up not in parameter range
            CharCodeAtOrNaN(text, comment.Pos + 1) == (int)CharacterCodes.asterisk &&
            CharCodeAtOrNaN(text, comment.Pos + 2) == (int)CharacterCodes.asterisk &&
            CharCodeAtOrNaN(text, comment.Pos + 3) != (int)CharacterCodes.slash;
    }

    internal static List<CommentRange>? GetJSDocCommentRanges(NodeStore store, Node node, NativeText sourceText)
    {
        var text = sourceText.Chars;
        var commentRanges = (store.Kind(node) == SyntaxKind.Parameter ||
                store.Kind(node) == SyntaxKind.TypeParameter ||
                store.Kind(node) == SyntaxKind.FunctionExpression ||
                store.Kind(node) == SyntaxKind.ArrowFunction ||
                store.Kind(node) == SyntaxKind.ParenthesizedExpression ||
                store.Kind(node) == SyntaxKind.VariableDeclaration ||
                store.Kind(node) == SyntaxKind.ExportSpecifier) ?
            Core.Concatenate(Scanner.GetTrailingCommentRanges(text, store.Pos(node)), Scanner.GetLeadingCommentRanges(text, store.Pos(node))) :
            Scanner.GetLeadingCommentRanges(text, store.Pos(node));
        // True if the comment starts with '/**' but not if it is '/**/'
        return Core.Filter(commentRanges, comment =>
            comment.End <= store.End(node) && // Due to parse errors sometime empty parameter may get comments assigned to it that end up not in parameter range
            CharCodeAtOrNaN(text, comment.Pos + 1) == (int)CharacterCodes.asterisk &&
            CharCodeAtOrNaN(text, comment.Pos + 2) == (int)CharacterCodes.asterisk &&
            CharCodeAtOrNaN(text, comment.Pos + 3) != (int)CharacterCodes.slash);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsVariableLike(NodeStore store, Node node)
    {
        if (!node.IsNone)
        {
            switch (store.Kind(node))
            {
                case SyntaxKind.BindingElement:
                case SyntaxKind.EnumMember:
                case SyntaxKind.Parameter:
                case SyntaxKind.PropertyAssignment:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.ShorthandPropertyAssignment:
                case SyntaxKind.VariableDeclaration:
                    return true;
            }
        }
        return false;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsSuperProperty(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        return (kind == SyntaxKind.PropertyAccessExpression || kind == SyntaxKind.ElementAccessExpression)
            && store.Kind(GetAccessExpressionExpression(store, node)) == SyntaxKind.SuperKeyword;
    }

    /// <summary>`(node as PropertyAccessExpression | ElementAccessExpression).expression`.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static Node GetAccessExpressionExpression(NodeStore store, Node node)
    {
        return (node is var propertyAccess && store.IsPropertyAccessExpression(propertyAccess)) ? store.Expression(propertyAccess) : store.Expression(node);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsInJSFile(NodeStore store, Node node)
    {
        return !node.IsNone && (store.Flags(node) & NodeFlags.JavaScriptFile) != 0;
    }

    internal static bool IsStringDoubleQuoted(NodeStore store, Node str, Node sourceFile)
    {
        // Track O (O5a): the first character of getSourceTextOfNodeFromSourceFile's text is read from the source text,
        // without the substring, unless the node is missing or the JSDoc rewrite of the text applies.
        if (!NodeIsMissing(store, str) && !IsJSDocTypeExpressionOrChild(store, str))
        {
            var sourceText = store.SourceFileText(sourceFile).Chars;
            var start = Math.Clamp(Scanner.SkipTrivia(sourceText, store.Pos(str)), 0, sourceText.Length);
            var end = Math.Clamp(store.End(str), 0, sourceText.Length);
            var from = Math.Min(start, end);
            return from < Math.Max(start, end) && sourceText[from] == (char)CharacterCodes.doubleQuote;
        }
        return CharCodeAtOrNaN(GetSourceTextOfNodeFromSourceFile(store, sourceFile, str), 0) == (int)CharacterCodes.doubleQuote;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetRightMostAssignedExpression(NodeStore store, Node node)
    {
        while (IsAssignmentExpression(store, node, /*excludeCompoundAssignment*/ true))
        {
            node = store.Right(node);
        }
        return node;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsExportsIdentifier(NodeStore store, Node node)
    {
        return FactoryNodeTests.IsIdentifier(store, node) && store.Texts.Equals(store.EscapedText(node), "exports");
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsModuleIdentifier(NodeStore store, Node node)
    {
        return FactoryNodeTests.IsIdentifier(store, node) && store.Texts.Equals(store.EscapedText(node), "module");
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsModuleExportsAccessExpression(NodeStore store, Node node)
    {
        return (FactoryNodeTests.IsPropertyAccessExpression(store, node) || IsLiteralLikeElementAccess(store, node))
            && IsModuleIdentifier(store, GetAccessExpressionExpression(store, node))
            && store.Texts.Equals(GetElementOrPropertyAccessName(store, node), "exports");
    }

    // Given a BinaryExpression, returns SpecialPropertyAssignmentKind for the various kinds of property
    // assignments we treat as special in the binder
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static AssignmentDeclarationKind GetAssignmentDeclarationKind(NodeStore store, Node expr)
    {
        var special = GetAssignmentDeclarationKindWorker(store, expr);
        return special == AssignmentDeclarationKind.Property || IsInJSFile(store, expr) ? special : AssignmentDeclarationKind.None;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsBindableObjectDefinePropertyCall(NodeStore store, Node expr)
    {
        return store.Length(store.Arguments(expr)) == 3 &&
            FactoryNodeTests.IsPropertyAccessExpression(store, store.Expression(expr)) &&
            FactoryNodeTests.IsIdentifier(store, store.Expression(store.Expression(expr))) &&
            store.Texts.Equals(UtilitiesPublic.IdText(store, store.Expression(store.Expression(expr))), "Object") &&
            store.Texts.Equals(UtilitiesPublic.IdText(store, store.Name(store.Expression(expr))), "defineProperty") &&
            IsStringOrNumericLiteralLike(store, store.Element(store.Arguments(expr), 1)) &&
            IsBindableStaticNameExpression(store, store.Element(store.Arguments(expr), 0), /*excludeThisKeyword*/ true);
    }

    /// <summary>
    /// x[0] OR x['a'] OR x[Symbol.y]
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsLiteralLikeElementAccess(NodeStore store, Node node)
    {
        return FactoryNodeTests.IsElementAccessExpression(store, node) && IsStringOrNumericLiteralLike(store, store.ArgumentExpression(node));
    }

    /// <summary>
    /// Any series of property and element accesses.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsBindableStaticAccessExpression(NodeStore store, Node node, bool? excludeThisKeyword = null)
    {
        return FactoryNodeTests.IsPropertyAccessExpression(store, node) && (excludeThisKeyword != true && store.Kind(store.Expression(node)) == SyntaxKind.ThisKeyword || FactoryNodeTests.IsIdentifier(store, store.Name(node)) && IsBindableStaticNameExpression(store, store.Expression(node), /*excludeThisKeyword*/ true))
            || IsBindableStaticElementAccessExpression(store, node, excludeThisKeyword);
    }

    /// <summary>
    /// Any series of property and element accesses, ending in a literal element access
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsBindableStaticElementAccessExpression(NodeStore store, Node node, bool? excludeThisKeyword = null)
    {
        return IsLiteralLikeElementAccess(store, node)
            && ((excludeThisKeyword != true && store.Kind(store.Expression(node)) == SyntaxKind.ThisKeyword) ||
                IsEntityNameExpression(store, store.Expression(node)) ||
                IsBindableStaticAccessExpression(store, store.Expression(node), /*excludeThisKeyword*/ true));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsBindableStaticNameExpression(NodeStore store, Node node, bool? excludeThisKeyword = null)
    {
        return IsEntityNameExpression(store, node) || IsBindableStaticAccessExpression(store, node, excludeThisKeyword);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static AssignmentDeclarationKind GetAssignmentDeclarationKindWorker(NodeStore store, Node expr)
    {
        if (FactoryNodeTests.IsCallExpression(store, expr))
        {
            var call = expr;
            if (!IsBindableObjectDefinePropertyCall(store, call))
            {
                return AssignmentDeclarationKind.None;
            }
            var entityName = store.Element(store.Arguments(call), 0);
            if (IsExportsIdentifier(store, entityName) || IsModuleExportsAccessExpression(store, entityName))
            {
                return AssignmentDeclarationKind.ObjectDefinePropertyExports;
            }
            if (IsBindableStaticAccessExpression(store, entityName) && store.Texts.Equals(GetElementOrPropertyAccessName(store, entityName), "prototype"))
            {
                return AssignmentDeclarationKind.ObjectDefinePrototypeProperty;
            }
            return AssignmentDeclarationKind.ObjectDefinePropertyValue;
        }
        var binary = expr;
        if (store.Kind(store.OperatorToken(binary)) != SyntaxKind.EqualsToken || !IsAccessExpression(store, store.Left(binary)) || IsVoidZero(store, GetRightMostAssignedExpression(store, binary)))
        {
            return AssignmentDeclarationKind.None;
        }
        if (IsBindableStaticNameExpression(store, GetAccessExpressionExpression(store, store.Left(binary)), /*excludeThisKeyword*/ true) && store.Texts.Equals(GetElementOrPropertyAccessName(store, store.Left(binary)), "prototype") && FactoryNodeTests.IsObjectLiteralExpression(store, GetInitializerOfBinaryExpression(store, binary)))
        {
            // F.prototype = { ... }
            return AssignmentDeclarationKind.Prototype;
        }
        return GetAssignmentDeclarationPropertyAccessKind(store, store.Left(binary));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsVoidZero(NodeStore store, Node node)
    {
        return FactoryNodeTests.IsVoidExpression(store, node) && FactoryNodeTests.IsNumericLiteral(store, store.Expression(node)) && store.Texts.Equals(store.Text(store.Expression(node)), "0");
    }

    /// <summary>
    /// Does not handle signed numeric names like `a[+0]` - handling those would require handling prefix unary expressions
    /// throughout late binding handling as well, which is awkward (but ultimately probably doable if there is demand)
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetElementOrPropertyAccessArgumentExpressionOrName(NodeStore store, Node node)
    {
        if (FactoryNodeTests.IsPropertyAccessExpression(store, node))
        {
            return store.Name(node);
        }
        var arg = SkipParentheses(store, store.ArgumentExpression(node));
        if (FactoryNodeTests.IsNumericLiteral(store, arg) || UtilitiesPublic.IsStringLiteralLike(store, arg))
        {
            return arg;
        }
        return node;
    }

    /// <returns>The name (Track O: a text value; <see cref="TextRef.None"/> for undefined).</returns>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TextRef GetElementOrPropertyAccessName(NodeStore store, Node node)
    {
        var name = GetElementOrPropertyAccessArgumentExpressionOrName(store, node);
        if (!name.IsNone)
        {
            if (FactoryNodeTests.IsIdentifier(store, name))
            {
                return store.EscapedText(name);
            }
            if (UtilitiesPublic.IsStringLiteralLike(store, name) || FactoryNodeTests.IsNumericLiteral(store, name))
            {
                return UtilitiesPublic.EscapeLeadingUnderscores(store.Texts, GetLiteralText(store, name));
            }
        }
        return default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static AssignmentDeclarationKind GetAssignmentDeclarationPropertyAccessKind(NodeStore store, Node lhs)
    {
        if (store.Kind(GetAccessExpressionExpression(store, lhs)) == SyntaxKind.ThisKeyword)
        {
            return AssignmentDeclarationKind.ThisProperty;
        }
        else if (IsModuleExportsAccessExpression(store, lhs))
        {
            // module.exports = expr
            return AssignmentDeclarationKind.ModuleExports;
        }
        else if (IsBindableStaticNameExpression(store, GetAccessExpressionExpression(store, lhs), /*excludeThisKeyword*/ true))
        {
            if (IsPrototypeAccess(store, GetAccessExpressionExpression(store, lhs)))
            {
                // F.G....prototype.x = expr
                return AssignmentDeclarationKind.PrototypeProperty;
            }

            var nextToLast = lhs;
            while (!FactoryNodeTests.IsIdentifier(store, GetAccessExpressionExpression(store, nextToLast)))
            {
                nextToLast = GetAccessExpressionExpression(store, nextToLast);
            }
            var id = GetAccessExpressionExpression(store, nextToLast);
            if (
                (store.Texts.Equals(store.EscapedText(id), "exports") ||
                    store.Texts.Equals(store.EscapedText(id), "module") && store.Texts.Equals(GetElementOrPropertyAccessName(store, nextToLast), "exports")) &&
                // ExportsProperty does not support binding with computed names
                IsBindableStaticAccessExpression(store, lhs))
            {
                // exports.name = expr OR module.exports.name = expr OR exports["name"] = expr ...
                return AssignmentDeclarationKind.ExportsProperty;
            }
            if (IsBindableStaticNameExpression(store, lhs, /*excludeThisKeyword*/ true) || (FactoryNodeTests.IsElementAccessExpression(store, lhs) && IsDynamicName(store, lhs)))
            {
                // F.G...x = expr
                return AssignmentDeclarationKind.Property;
            }
        }

        return AssignmentDeclarationKind.None;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetInitializerOfBinaryExpression(NodeStore store, Node expr)
    {
        while (FactoryNodeTests.IsBinaryExpression(store, store.Right(expr)))
        {
            expr = store.Right(expr);
        }
        return store.Right(expr);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static Node GetSourceOfDefaultedAssignment(NodeStore store, Node node)
    {
        if (FactoryNodeTests.IsExpressionStatement(store, node) &&
            FactoryNodeTests.IsBinaryExpression(store, store.Expression(node)))
        {
            var expression = store.Expression(node);
            if (GetAssignmentDeclarationKind(store, expression) != AssignmentDeclarationKind.None &&
                FactoryNodeTests.IsBinaryExpression(store, store.Right(expression)) &&
                (store.Kind(store.OperatorToken(store.Right(expression))) == SyntaxKind.BarBarToken || store.Kind(store.OperatorToken(store.Right(expression))) == SyntaxKind.QuestionQuestionToken))
            {
                return store.Right(store.Right(expression));
            }
        }
        return default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static Node GetSingleInitializerOfVariableStatementOrPropertyDeclaration(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.VariableStatement:
                var v = GetSingleVariableOfVariableStatement(store, node);
                return store.Initializer(v);
            case SyntaxKind.PropertyDeclaration:
                return store.Initializer(node);
            case SyntaxKind.PropertyAssignment:
                return store.Initializer(node);
        }
        return default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetSingleVariableOfVariableStatement(NodeStore store, Node node)
    {
        return FactoryNodeTests.IsVariableStatement(store, node) ? Core.FirstOrUndefined(store.Nodes(store.Declarations(store.DeclarationList(node)))) : default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static Node GetNestedModuleDeclaration(NodeStore store, Node node)
    {
        return FactoryNodeTests.IsModuleDeclaration(store, node) &&
                !store.Body(node).IsNone &&
                store.Kind(store.Body(node)) == SyntaxKind.ModuleDeclaration
            ? store.Body(node)
            : default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool CanHaveJSDoc(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.BinaryExpression:
            case SyntaxKind.Block:
            case SyntaxKind.BreakStatement:
            case SyntaxKind.CallSignature:
            case SyntaxKind.CaseClause:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.ClassStaticBlockDeclaration:
            case SyntaxKind.Constructor:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.ContinueStatement:
            case SyntaxKind.DebuggerStatement:
            case SyntaxKind.DoStatement:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.EmptyStatement:
            case SyntaxKind.EndOfFileToken:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.EnumMember:
            case SyntaxKind.ExportAssignment:
            case SyntaxKind.ExportDeclaration:
            case SyntaxKind.ExportSpecifier:
            case SyntaxKind.ExpressionStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
            case SyntaxKind.ForStatement:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionType:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.Identifier:
            case SyntaxKind.IfStatement:
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.JSDocFunctionType:
            case SyntaxKind.JSDocSignature:
            case SyntaxKind.LabeledStatement:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.NamedTupleMember:
            case SyntaxKind.NamespaceExportDeclaration:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.Parameter:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.ReturnStatement:
            case SyntaxKind.SemicolonClassElement:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.ShorthandPropertyAssignment:
            case SyntaxKind.SpreadAssignment:
            case SyntaxKind.SwitchStatement:
            case SyntaxKind.ThrowStatement:
            case SyntaxKind.TryStatement:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.TypeParameter:
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.VariableStatement:
            case SyntaxKind.WhileStatement:
            case SyntaxKind.WithStatement:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// This function checks multiple locations for JSDoc comments that apply to a host node.
    /// At each location, the whole comment may apply to the node, or only a specific tag in
    /// the comment. In the first case, location adds the entire JSDoc object. In the
    /// second case, it adds the applicable JSDocTag.
    ///
    /// For example, a JSDoc comment before a parameter adds the entire JSDoc. But a
    /// `@param` tag on the parent function only adds the JSDocTag for the `@param`.
    ///
    /// ```ts
    /// /** JSDoc will be returned for `a` *\/
    /// const a = 0
    /// /**
    ///  * Entire JSDoc will be returned for `b`
    ///  * @param c JSDocTag will be returned for `c`
    ///  *\/
    /// function b(/** JSDoc will be returned for `c` *\/ c) {}
    /// ```
    /// </summary>
    internal static IReadOnlyList<Node> GetJSDocCommentsAndTags(NodeStore store, Node hostNode, bool? noCache = null)
    {
        List<Node>? result = null;
        // Pull parameter comments from declaring function as well
        if (IsVariableLike(store, hostNode) && UtilitiesPublic.HasInitializer(store, hostNode) && UtilitiesPublic.HasJSDocNodes(store, store.Initializer(hostNode)))
        {
            result = Core.AddRange(result, FilterOwnedJSDocTags(store, hostNode, store.JsDoc(store.Initializer(hostNode))!));
        }

        Node node = hostNode;
        while (!node.IsNone && !store.Parent(node).IsNone)
        {
            if (UtilitiesPublic.HasJSDocNodes(store, node))
            {
                result = Core.AddRange(result, FilterOwnedJSDocTags(store, hostNode, store.JsDoc(node)!));
            }

            if (store.Kind(node) == SyntaxKind.Parameter)
            {
                result = Core.AddRange(result, noCache == true ? UtilitiesPublic.GetJSDocParameterTagsNoCache(store, node) : UtilitiesPublic.GetJSDocParameterTags(store, node));
                break;
            }
            if (store.Kind(node) == SyntaxKind.TypeParameter)
            {
                result = Core.AddRange(result, noCache == true ? UtilitiesPublic.GetJSDocTypeParameterTagsNoCache(store, node) : UtilitiesPublic.GetJSDocTypeParameterTags(store, node));
                break;
            }
            node = GetNextJSDocCommentLocation(store, node);
        }
        return result ?? (IReadOnlyList<Node>)Core.EmptyArray<Node>();
    }

    private static IReadOnlyList<Node> FilterOwnedJSDocTags(NodeStore store, Node hostNode, List<Node> comments)
    {
        var lastJsDoc = Core.Last(comments);
        // flatMap<JSDoc, JSDoc | JSDocTag>(comments, jsDoc => ...)
        List<Node>? result = null;
        foreach (var jsDoc in comments)
        {
            IReadOnlyList<Node>? mapped;
            var tags = store.Tags(jsDoc);
            if (jsDoc == lastJsDoc)
            {
                // `const ownedTags = filter(jsDoc.tags, ...); return jsDoc.tags === ownedTags ? [jsDoc] : ownedTags;`:
                // filter returns its argument (also undefined) when it keeps every element.
                var ownedTags = new List<Node>();
                var keptAll = true;
                foreach (var tag in store.Nodes(tags))
                {
                    if (OwnsJSDocTag(store, hostNode, tag))
                    {
                        ownedTags.Add(tag);
                    }
                    else
                    {
                        keptAll = false;
                    }
                }
                mapped = keptAll ? [jsDoc] : ownedTags;
            }
            else
            {
                mapped = null;
                if (!tags.IsNone)
                {
                    var overloadTags = new List<Node>();
                    foreach (var tag in store.Nodes(tags))
                    {
                        if (FactoryNodeTests.IsJSDocOverloadTag(store, tag))
                        {
                            overloadTags.Add(tag);
                        }
                    }
                    mapped = overloadTags;
                }
            }
            if (mapped != null)
            {
                result ??= [];
                result.AddRange(mapped);
            }
        }
        return result ?? (IReadOnlyList<Node>)Core.EmptyArray<Node>();
    }

    /// <summary>
    /// Determines whether a host node owns a jsDoc tag. A `@type`/`@satisfies` tag attached to a
    /// a ParenthesizedExpression belongs only to the ParenthesizedExpression.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool OwnsJSDocTag(NodeStore store, Node hostNode, Node tag)
    {
        return !(FactoryNodeTests.IsJSDocTypeTag(store, tag) || FactoryNodeTests.IsJSDocSatisfiesTag(store, tag))
            || store.Parent(tag).IsNone
            || !FactoryNodeTests.IsJSDoc(store, store.Parent(tag))
            || !FactoryNodeTests.IsParenthesizedExpression(store, store.Parent(store.Parent(tag)))
            || store.Parent(store.Parent(tag)) == hostNode;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetNextJSDocCommentLocation(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        if (
            store.Kind(parent) == SyntaxKind.PropertyAssignment ||
            store.Kind(parent) == SyntaxKind.ExportAssignment ||
            store.Kind(parent) == SyntaxKind.PropertyDeclaration ||
            store.Kind(parent) == SyntaxKind.ExpressionStatement && store.Kind(node) == SyntaxKind.PropertyAccessExpression ||
            store.Kind(parent) == SyntaxKind.ReturnStatement ||
            !GetNestedModuleDeclaration(store, parent).IsNone ||
            IsAssignmentExpression(store, node))
        {
            return parent;
        }
        // Try to recognize this pattern when node is initializer of variable declaration and JSDoc comments are on containing variable statement.
        // /**
        //   * @param {number} name
        //   * @returns {number}
        //   */
        // var x = function(name) { return name.length; }
        else if (
            !store.Parent(parent).IsNone &&
            (GetSingleVariableOfVariableStatement(store, store.Parent(parent)) == node || IsAssignmentExpression(store, parent)))
        {
            return store.Parent(parent);
        }
        else if (
            !store.Parent(parent).IsNone && !store.Parent(store.Parent(parent)).IsNone &&
            (!GetSingleVariableOfVariableStatement(store, store.Parent(store.Parent(parent))).IsNone ||
                GetSingleInitializerOfVariableStatementOrPropertyDeclaration(store, store.Parent(store.Parent(parent))) == node ||
                !GetSourceOfDefaultedAssignment(store, store.Parent(store.Parent(parent))).IsNone))
        {
            return store.Parent(store.Parent(parent));
        }
        return default;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node SkipParentheses(NodeStore store, Node node, bool? excludeJSDocTypeAssertions = null)
    {
        var flags = excludeJSDocTypeAssertions == true ?
            OuterExpressionKinds.Parentheses | OuterExpressionKinds.ExcludeJSDocTypeAssertion :
            OuterExpressionKinds.Parentheses;
        return FactoryUtilities.SkipOuterExpressions(store, node, flags);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsKeyword(SyntaxKind token)
    {
        return SyntaxKind.FirstKeyword <= token && token <= SyntaxKind.LastKeyword;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPunctuation(SyntaxKind token)
    {
        return SyntaxKind.FirstPunctuation <= token && token <= SyntaxKind.LastPunctuation;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsKeywordOrPunctuation(SyntaxKind token)
    {
        return IsKeyword(token) || IsPunctuation(token);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsStringOrNumericLiteralLike(NodeStore store, Node node)
    {
        return UtilitiesPublic.IsStringLiteralLike(store, node) || FactoryNodeTests.IsNumericLiteral(store, node);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsSignedNumericLiteral(NodeStore store, Node node)
    {
        return FactoryNodeTests.IsPrefixUnaryExpression(store, node) && (store.Operator(node) == SyntaxKind.PlusToken || store.Operator(node) == SyntaxKind.MinusToken) && FactoryNodeTests.IsNumericLiteral(store, store.Operand(node));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsDynamicName(NodeStore store, Node name)
    {
        if (!(store.Kind(name) == SyntaxKind.ComputedPropertyName || store.Kind(name) == SyntaxKind.ElementAccessExpression))
        {
            return false;
        }
        var expr = FactoryNodeTests.IsElementAccessExpression(store, name) ? SkipParentheses(store, store.ArgumentExpression(name)) : store.Expression(name);
        return !IsStringOrNumericLiteralLike(store, expr) &&
            !IsSignedNumericLiteral(store, expr);
    }

    internal static bool NodeIsSynthesized(TextRange range)
    {
        return PositionIsSynthesized(range.Pos)
            || PositionIsSynthesized(range.End);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool NodeIsSynthesized(NodeStore store, Node node)
    {
        return PositionIsSynthesized(store.Pos(node))
            || PositionIsSynthesized(store.End(node));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Associativity GetExpressionAssociativity(NodeStore store, Node expression)
    {
        var @operator = GetOperator(store, expression);
        var hasArguments = store.Kind(expression) == SyntaxKind.NewExpression && !store.Arguments(expression).IsNone;
        return GetOperatorAssociativity(store.Kind(expression), @operator, hasArguments);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Associativity GetOperatorAssociativity(SyntaxKind kind, SyntaxKind @operator, bool? hasArguments = null)
    {
        switch (kind)
        {
            case SyntaxKind.NewExpression:
                return hasArguments == true ? Associativity.Left : Associativity.Right;

            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.TypeOfExpression:
            case SyntaxKind.VoidExpression:
            case SyntaxKind.DeleteExpression:
            case SyntaxKind.AwaitExpression:
            case SyntaxKind.ConditionalExpression:
            case SyntaxKind.YieldExpression:
                return Associativity.Right;

            case SyntaxKind.BinaryExpression:
                switch (@operator)
                {
                    case SyntaxKind.AsteriskAsteriskToken:
                    case SyntaxKind.EqualsToken:
                    case SyntaxKind.PlusEqualsToken:
                    case SyntaxKind.MinusEqualsToken:
                    case SyntaxKind.AsteriskAsteriskEqualsToken:
                    case SyntaxKind.AsteriskEqualsToken:
                    case SyntaxKind.SlashEqualsToken:
                    case SyntaxKind.PercentEqualsToken:
                    case SyntaxKind.LessThanLessThanEqualsToken:
                    case SyntaxKind.GreaterThanGreaterThanEqualsToken:
                    case SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken:
                    case SyntaxKind.AmpersandEqualsToken:
                    case SyntaxKind.CaretEqualsToken:
                    case SyntaxKind.BarEqualsToken:
                    case SyntaxKind.BarBarEqualsToken:
                    case SyntaxKind.AmpersandAmpersandEqualsToken:
                    case SyntaxKind.QuestionQuestionEqualsToken:
                        return Associativity.Right;
                }
                break;
        }
        return Associativity.Left;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static OperatorPrecedence GetExpressionPrecedence(NodeStore store, Node expression)
    {
        var @operator = GetOperator(store, expression);
        var hasArguments = store.Kind(expression) == SyntaxKind.NewExpression && !store.Arguments(expression).IsNone;
        return GetOperatorPrecedence(store.Kind(expression), @operator, hasArguments);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static SyntaxKind GetOperator(NodeStore store, Node expression)
    {
        if (store.Kind(expression) == SyntaxKind.BinaryExpression)
        {
            return store.Kind(store.OperatorToken(expression));
        }
        else if (store.Kind(expression) == SyntaxKind.PrefixUnaryExpression || store.Kind(expression) == SyntaxKind.PostfixUnaryExpression)
        {
            return (expression is var prefix && store.IsPrefixUnaryExpression(prefix)) ? store.Operator(prefix) : store.Operator(expression);
        }
        else
        {
            return store.Kind(expression);
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static OperatorPrecedence GetOperatorPrecedence(SyntaxKind nodeKind, SyntaxKind operatorKind, bool? hasArguments = null)
    {
        switch (nodeKind)
        {
            case SyntaxKind.CommaListExpression:
                return OperatorPrecedence.Comma;

            case SyntaxKind.SpreadElement:
                return OperatorPrecedence.Spread;

            case SyntaxKind.YieldExpression:
                return OperatorPrecedence.Yield;

            case SyntaxKind.ConditionalExpression:
                return OperatorPrecedence.Conditional;

            case SyntaxKind.BinaryExpression:
                switch (operatorKind)
                {
                    case SyntaxKind.CommaToken:
                        return OperatorPrecedence.Comma;

                    case SyntaxKind.EqualsToken:
                    case SyntaxKind.PlusEqualsToken:
                    case SyntaxKind.MinusEqualsToken:
                    case SyntaxKind.AsteriskAsteriskEqualsToken:
                    case SyntaxKind.AsteriskEqualsToken:
                    case SyntaxKind.SlashEqualsToken:
                    case SyntaxKind.PercentEqualsToken:
                    case SyntaxKind.LessThanLessThanEqualsToken:
                    case SyntaxKind.GreaterThanGreaterThanEqualsToken:
                    case SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken:
                    case SyntaxKind.AmpersandEqualsToken:
                    case SyntaxKind.CaretEqualsToken:
                    case SyntaxKind.BarEqualsToken:
                    case SyntaxKind.BarBarEqualsToken:
                    case SyntaxKind.AmpersandAmpersandEqualsToken:
                    case SyntaxKind.QuestionQuestionEqualsToken:
                        return OperatorPrecedence.Assignment;

                    default:
                        return GetBinaryOperatorPrecedence(operatorKind);
                }

            // TODO: Should prefix `++` and `--` be moved to the `Update` precedence?
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.NonNullExpression:
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.TypeOfExpression:
            case SyntaxKind.VoidExpression:
            case SyntaxKind.DeleteExpression:
            case SyntaxKind.AwaitExpression:
                return OperatorPrecedence.Unary;

            case SyntaxKind.PostfixUnaryExpression:
                return OperatorPrecedence.Update;

            case SyntaxKind.CallExpression:
                return OperatorPrecedence.LeftHandSide;

            case SyntaxKind.NewExpression:
                return hasArguments == true ? OperatorPrecedence.Member : OperatorPrecedence.LeftHandSide;

            case SyntaxKind.TaggedTemplateExpression:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.MetaProperty:
                return OperatorPrecedence.Member;

            case SyntaxKind.AsExpression:
            case SyntaxKind.SatisfiesExpression:
                return OperatorPrecedence.Relational;

            case SyntaxKind.ThisKeyword:
            case SyntaxKind.SuperKeyword:
            case SyntaxKind.Identifier:
            case SyntaxKind.PrivateIdentifier:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.RegularExpressionLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateExpression:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.OmittedExpression:
            case SyntaxKind.JsxElement:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxFragment:
                return OperatorPrecedence.Primary;

            default:
                return OperatorPrecedence.Invalid;
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static OperatorPrecedence GetBinaryOperatorPrecedence(SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.QuestionQuestionToken:
                return OperatorPrecedence.Coalesce;
            case SyntaxKind.BarBarToken:
                return OperatorPrecedence.LogicalOR;
            case SyntaxKind.AmpersandAmpersandToken:
                return OperatorPrecedence.LogicalAND;
            case SyntaxKind.BarToken:
                return OperatorPrecedence.BitwiseOR;
            case SyntaxKind.CaretToken:
                return OperatorPrecedence.BitwiseXOR;
            case SyntaxKind.AmpersandToken:
                return OperatorPrecedence.BitwiseAND;
            case SyntaxKind.EqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsToken:
            case SyntaxKind.EqualsEqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsEqualsToken:
                return OperatorPrecedence.Equality;
            case SyntaxKind.LessThanToken:
            case SyntaxKind.GreaterThanToken:
            case SyntaxKind.LessThanEqualsToken:
            case SyntaxKind.GreaterThanEqualsToken:
            case SyntaxKind.InstanceOfKeyword:
            case SyntaxKind.InKeyword:
            case SyntaxKind.AsKeyword:
            case SyntaxKind.SatisfiesKeyword:
                return OperatorPrecedence.Relational;
            case SyntaxKind.LessThanLessThanToken:
            case SyntaxKind.GreaterThanGreaterThanToken:
            case SyntaxKind.GreaterThanGreaterThanGreaterThanToken:
                return OperatorPrecedence.Shift;
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
                return OperatorPrecedence.Additive;
            case SyntaxKind.AsteriskToken:
            case SyntaxKind.SlashToken:
            case SyntaxKind.PercentToken:
                return OperatorPrecedence.Multiplicative;
            case SyntaxKind.AsteriskAsteriskToken:
                return OperatorPrecedence.Exponentiation;
        }

        // -1 is lower than all other precedences.  Returning it will cause binary expression
        // parsing to stop.
        return (OperatorPrecedence)(-1);
    }

    /// <summary>`node.templateFlags` of a TemplateLiteralToken (NoSubstitutionTemplateLiteral, TemplateHead/Middle/Tail).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static TokenFlags GetTemplateFlags(NodeStore store, Node node)
    {
        return node switch
        {
            var n when store.IsNoSubstitutionTemplateLiteral(n) => store.TemplateFlags(n),
            var n when store.IsTemplateHead(n) => store.TemplateFlags(n),
            var n when store.IsTemplateMiddle(n) => store.TemplateFlags(n),
            var n when store.IsTemplateTail(n) => store.TemplateFlags(n),
            _ => TokenFlags.None,
        };
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool ContainsInvalidEscapeFlag(NodeStore store, Node node)
    {
        return (GetTemplateFlags(store, node) & TokenFlags.ContainsInvalidEscape) != 0;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool HasInvalidEscape(NodeStore store, Node template)
    {
        return !template.IsNone && (FactoryNodeTests.IsNoSubstitutionTemplateLiteral(store, template)
            ? ContainsInvalidEscapeFlag(store, template)
            : (ContainsInvalidEscapeFlag(store, store.Head(template)) || SomeSpanContainsInvalidEscape(store, store.TemplateSpans(template))));
    }

    // `some(template.templateSpans, span => containsInvalidEscapeFlag(span.literal))` (Track O, O-NA: a loop).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool SomeSpanContainsInvalidEscape(NodeStore store, NodeList spans)
    {
        foreach (var span in store.Nodes(spans))
        {
            if (ContainsInvalidEscapeFlag(store, store.Literal(span)))
            {
                return true;
            }
        }
        return false;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsThisIdentifier(NodeStore store, Node node)
    {
        return !node.IsNone && store.Kind(node) == SyntaxKind.Identifier && IdentifierIsThisKeyword(store, node);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IdentifierIsThisKeyword(NodeStore store, Node id)
    {
        return store.Texts.Equals(store.EscapedText(id), "this");
    }

    internal static bool HasSyntacticModifier(NodeStore store, Node node, ModifierFlags flags)
    {
        return GetSelectedSyntacticModifierFlags(store, node, flags) != 0;
    }

    internal static ModifierFlags GetSelectedSyntacticModifierFlags(NodeStore store, Node node, ModifierFlags flags)
    {
        return GetSyntacticModifierFlags(store, node) & flags;
    }

    private static ModifierFlags GetModifierFlagsWorker(NodeStore store, Node node, bool includeJSDoc, bool? alwaysIncludeJSDoc = null)
    {
        if (store.Kind(node) >= SyntaxKind.FirstToken && store.Kind(node) <= SyntaxKind.LastToken)
        {
            return ModifierFlags.None;
        }

        if ((store.ModifierFlagsCache(node) & ModifierFlags.HasComputedFlags) == 0)
        {
            store.SetModifierFlagsCache(node, GetSyntacticModifierFlagsNoCache(store, node) | ModifierFlags.HasComputedFlags);
        }

        if (alwaysIncludeJSDoc == true || includeJSDoc && IsInJSFile(store, node))
        {
            if ((store.ModifierFlagsCache(node) & ModifierFlags.HasComputedJSDocModifiers) == 0 && !store.Parent(node).IsNone)
            {
                store.SetModifierFlagsCache(node, store.ModifierFlagsCache(node) | (GetRawJSDocModifierFlagsNoCache(store, node) | ModifierFlags.HasComputedJSDocModifiers));
            }
            return SelectEffectiveModifierFlags(store.ModifierFlagsCache(node));
        }

        return SelectSyntacticModifierFlags(store.ModifierFlagsCache(node));
    }

    /// <summary>
    /// Gets the ModifierFlags for syntactic modifiers on the provided node. The modifiers will be cached on the node to improve performance.
    ///
    /// NOTE: This function does not use `parent` pointers and will not include modifiers from JSDoc.
    /// </summary>
    internal static ModifierFlags GetSyntacticModifierFlags(NodeStore store, Node node)
    {
        return GetModifierFlagsWorker(store, node, /*includeJSDoc*/ false);
    }

    /// <summary>utilities.ts getEffectiveModifierFlags (syntactic modifiers and, in JavaScript files, JSDoc modifier tags; used by the checker, module C9d).</summary>
    internal static ModifierFlags GetEffectiveModifierFlags(NodeStore store, Node node)
    {
        return GetModifierFlagsWorker(store, node, /*includeJSDoc*/ true);
    }

    private static ModifierFlags GetRawJSDocModifierFlagsNoCache(NodeStore store, Node node)
    {
        var flags = ModifierFlags.None;
        if (!store.Parent(node).IsNone && !FactoryNodeTests.IsParameter(store, node))
        {
            if (IsInJSFile(store, node))
            {
                if (!UtilitiesPublic.GetJSDocPublicTagNoCache(store, node).IsNone)
                {
                    flags |= ModifierFlags.JSDocPublic;
                }
                if (!UtilitiesPublic.GetJSDocPrivateTagNoCache(store, node).IsNone)
                {
                    flags |= ModifierFlags.JSDocPrivate;
                }
                if (!UtilitiesPublic.GetJSDocProtectedTagNoCache(store, node).IsNone)
                {
                    flags |= ModifierFlags.JSDocProtected;
                }
                if (!UtilitiesPublic.GetJSDocReadonlyTagNoCache(store, node).IsNone)
                {
                    flags |= ModifierFlags.JSDocReadonly;
                }
                if (!UtilitiesPublic.GetJSDocOverrideTagNoCache(store, node).IsNone)
                {
                    flags |= ModifierFlags.JSDocOverride;
                }
            }
            if (!UtilitiesPublic.GetJSDocDeprecatedTagNoCache(store, node).IsNone)
            {
                flags |= ModifierFlags.Deprecated;
            }
        }

        return flags;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static ModifierFlags SelectSyntacticModifierFlags(ModifierFlags flags)
    {
        return flags & ModifierFlags.SyntacticModifiers;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static ModifierFlags SelectEffectiveModifierFlags(ModifierFlags flags)
    {
        return (flags & ModifierFlags.NonCacheOnlyModifiers) |
            (ModifierFlags)((uint)(flags & ModifierFlags.JSDocCacheOnlyModifiers) >> 23); // shift ModifierFlags.JSDoc* to match ModifierFlags.*
    }

    /// <summary>
    /// Gets the ModifierFlags for syntactic modifiers on the provided node. The modifier flags cache on the node is ignored.
    ///
    /// NOTE: This function does not use `parent` pointers and will not include modifiers from JSDoc.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static ModifierFlags GetSyntacticModifierFlagsNoCache(NodeStore store, Node node)
    {
        var flags = FactoryUtilitiesPublic.CanHaveModifiers(store, node) ? ModifiersToFlags(store, store.Modifiers(node)) : ModifierFlags.None;
        if ((store.Flags(node) & NodeFlags.NestedNamespace) != 0 || store.Kind(node) == SyntaxKind.Identifier && (store.Flags(node) & NodeFlags.IdentifierIsInJSDocNamespace) != 0)
        {
            flags |= ModifierFlags.Export;
        }
        return flags;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static ModifierFlags ModifiersToFlags(NodeStore store, NodeList modifiers)
    {
        var flags = ModifierFlags.None;
        if (!modifiers.IsNone)
        {
            foreach (var modifier in store.Nodes(modifiers))
            {
                flags |= ModifierToFlag(store.Kind(modifier));
            }
        }
        return flags;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static ModifierFlags ModifierToFlag(SyntaxKind token)
    {
        switch (token)
        {
            case SyntaxKind.StaticKeyword:
                return ModifierFlags.Static;
            case SyntaxKind.PublicKeyword:
                return ModifierFlags.Public;
            case SyntaxKind.ProtectedKeyword:
                return ModifierFlags.Protected;
            case SyntaxKind.PrivateKeyword:
                return ModifierFlags.Private;
            case SyntaxKind.AbstractKeyword:
                return ModifierFlags.Abstract;
            case SyntaxKind.AccessorKeyword:
                return ModifierFlags.Accessor;
            case SyntaxKind.ExportKeyword:
                return ModifierFlags.Export;
            case SyntaxKind.DeclareKeyword:
                return ModifierFlags.Ambient;
            case SyntaxKind.ConstKeyword:
                return ModifierFlags.Const;
            case SyntaxKind.DefaultKeyword:
                return ModifierFlags.Default;
            case SyntaxKind.AsyncKeyword:
                return ModifierFlags.Async;
            case SyntaxKind.ReadonlyKeyword:
                return ModifierFlags.Readonly;
            case SyntaxKind.OverrideKeyword:
                return ModifierFlags.Override;
            case SyntaxKind.InKeyword:
                return ModifierFlags.In;
            case SyntaxKind.OutKeyword:
                return ModifierFlags.Out;
            case SyntaxKind.Decorator:
                return ModifierFlags.Decorator;
        }
        return ModifierFlags.None;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsLogicalOrCoalescingAssignmentOperator(SyntaxKind token)
    {
        return token == SyntaxKind.BarBarEqualsToken
            || token == SyntaxKind.AmpersandAmpersandEqualsToken
            || token == SyntaxKind.QuestionQuestionEqualsToken;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAssignmentOperator(SyntaxKind token)
    {
        return token >= SyntaxKind.FirstAssignment && token <= SyntaxKind.LastAssignment;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAssignmentExpression(NodeStore store, Node node, bool? excludeCompoundAssignment = null)
    {
        return FactoryNodeTests.IsBinaryExpression(store, node)
            && (excludeCompoundAssignment == true
                ? store.Kind(store.OperatorToken(node)) == SyntaxKind.EqualsToken
                : IsAssignmentOperator(store.Kind(store.OperatorToken(node))))
            && UtilitiesPublic.IsLeftHandSideExpression(store, store.Left(node));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsEntityNameExpression(NodeStore store, Node node)
    {
        return store.Kind(node) == SyntaxKind.Identifier || IsPropertyAccessEntityNameExpression(store, node);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPropertyAccessEntityNameExpression(NodeStore store, Node node)
    {
        return FactoryNodeTests.IsPropertyAccessExpression(store, node) && FactoryNodeTests.IsIdentifier(store, store.Name(node)) && IsEntityNameExpression(store, store.Expression(node));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPrototypeAccess(NodeStore store, Node node)
    {
        return IsBindableStaticAccessExpression(store, node) && store.Texts.Equals(GetElementOrPropertyAccessName(store, node), "prototype");
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAccessExpression(NodeStore store, Node node)
    {
        return store.Kind(node) == SyntaxKind.PropertyAccessExpression || store.Kind(node) == SyntaxKind.ElementAccessExpression;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node GetLeftmostExpression(NodeStore store, Node node, bool stopAtCallExpressions)
    {
        while (true)
        {
            switch (store.Kind(node))
            {
                case SyntaxKind.PostfixUnaryExpression:
                    node = store.Operand(node);
                    continue;

                case SyntaxKind.BinaryExpression:
                    node = store.Left(node);
                    continue;

                case SyntaxKind.ConditionalExpression:
                    node = store.Condition(node);
                    continue;

                case SyntaxKind.TaggedTemplateExpression:
                    node = store.Tag(node);
                    continue;

                case SyntaxKind.CallExpression:
                    if (stopAtCallExpressions)
                    {
                        return node;
                    }
                    node = store.Expression(node);
                    continue;

                // falls through (C#: the CallExpression case above continues with its expression, as the cases below)
                case SyntaxKind.AsExpression:
                    node = store.Expression(node);
                    continue;
                case SyntaxKind.ElementAccessExpression:
                    node = store.Expression(node);
                    continue;
                case SyntaxKind.PropertyAccessExpression:
                    node = store.Expression(node);
                    continue;
                case SyntaxKind.NonNullExpression:
                    node = store.Expression(node);
                    continue;
                case SyntaxKind.PartiallyEmittedExpression:
                    node = store.Expression(node);
                    continue;
                case SyntaxKind.SatisfiesExpression:
                    node = store.Expression(node);
                    continue;
            }

            return node;
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static LanguageVariant GetLanguageVariant(ScriptKind scriptKind)
    {
        // .tsx and .jsx files are treated as jsx language variant.
        return scriptKind == ScriptKind.TSX || scriptKind == ScriptKind.JSX || scriptKind == ScriptKind.JS || scriptKind == ScriptKind.JSON ? LanguageVariant.JSX : LanguageVariant.Standard;
    }

    internal static ScriptKind EnsureScriptKind(string fileName, ScriptKind? scriptKind)
    {
        // Using scriptKind as a condition handles both:
        // - 'scriptKind' is unspecified and thus it is `undefined`
        // - 'scriptKind' is set and it is `Unknown` (0)
        // If the 'scriptKind' is 'undefined' or 'Unknown' then we attempt
        // to get the ScriptKind from the file name. If it cannot be resolved
        // from the file name then the default 'TS' script kind is returned.
        if (scriptKind != null && scriptKind != ScriptKind.Unknown)
        {
            return scriptKind.Value;
        }
        var fromFileName = GetScriptKindFromFileName(fileName);
        return fromFileName != ScriptKind.Unknown ? fromFileName : ScriptKind.TS;
    }

    internal static ScriptKind GetScriptKindFromFileName(string fileName)
    {
        // `fileName.substr(fileName.lastIndexOf("."))`: substr with -1 (no dot) starts at the last character.
        var dot = fileName.LastIndexOf('.');
        var ext = dot >= 0 ? fileName[dot..] : fileName.Length == 0 ? "" : fileName[^1..];
        // `ext.toLowerCase()`, compared with ASCII extensions: no character outside A-Z lower-cases to one of their
        // letters, so lowering A-Z gives the same comparisons.
        var lowerExt = new StringBuilder(ext.Length);
        foreach (var c in ext)
        {
            lowerExt.Append(char.IsAsciiLetterUpper(c) ? (char)(c | 0x20) : c);
        }
        switch (lowerExt.ToString())
        {
            case Extension.Js:
            case Extension.Cjs:
            case Extension.Mjs:
                return ScriptKind.JS;
            case Extension.Jsx:
                return ScriptKind.JSX;
            case Extension.Ts:
            case Extension.Cts:
            case Extension.Mts:
                return ScriptKind.TS;
            case Extension.Tsx:
                return ScriptKind.TSX;
            case Extension.Json:
                return ScriptKind.JSON;
            default:
                return ScriptKind.Unknown;
        }
    }

    internal static readonly string[] SupportedDeclarationExtensions = [Extension.Dts, Extension.Dcts, Extension.Dmts];

    /// <summary>
    /// Bypasses immutability and directly sets the `parent` property of a `Node`.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node SetParent(NodeStore store, Node child, Node parent)
    {
        if (!child.IsNone && !parent.IsNone)
        {
            store.SetParent(child, parent);
        }
        return child;
    }

    /// <summary>
    /// Bypasses immutability and directly sets the `parent` property of each `Node` recursively.
    /// </summary>
    /// <param name="rootNode">The root node from which to start the recursion.</param>
    /// <param name="incremental">When `true`, only recursively descends through nodes whose `parent` pointers are incorrect.
    /// This allows us to quickly bail out of setting `parent` for subtrees during incremental parsing.</param>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static Node SetParentRecursive(NodeStore store, Node rootNode, bool incremental)
    {
        if (rootNode.IsNone)
        {
            return rootNode;
        }
        // TypeScript: `forEachChildRecursively(rootNode, isJSDocNode(rootNode) ? bindParentToChildIgnoringJSDoc :
        // bindParentToChild)`. Track O (O-NA): a recursive walk with a struct visitor instead of forEachChildRecursively's
        // queues and the closures of the inner functions (the walk recurses as deep as the parse that built the tree did).
        // The order of the visits differs; the parents set do not ("skip" depends only on a node's own parent).
        NodeVisitor.ForEachChild<ParentBinder, bool>(store, rootNode, new ParentBinder(store, rootNode, incremental, UtilitiesPublic.IsJSDocNode(store, rootNode)));
        return rootNode;
    }

    /// <summary>The callbacks of setParentRecursive for the children of <c>parent</c>.</summary>
    private readonly struct ParentBinder : INodeVisitor<bool>
    {
        private readonly NodeStore store;
        private readonly Node parent;
        private readonly bool incremental;
        private readonly bool ignoringJSDoc;

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        internal ParentBinder(NodeStore store, Node parent, bool incremental, bool ignoringJSDoc)
        {
            this.store = store;
            this.parent = parent;
            this.incremental = incremental;
            this.ignoringJSDoc = ignoringJSDoc;
        }

        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public bool VisitNode(Node node)
        {
            BindParentToChild(store, node, parent, incremental, ignoringJSDoc);
            return false;
        }

        // (forEachChildRecursively has no cbNodes here: the elements of a list are visited one by one.)
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        public bool VisitNodes(NodeList nodes)
        {
            foreach (var node in store.Nodes(nodes))
            {
                BindParentToChild(store, node, parent, incremental, ignoringJSDoc);
            }
            return false;
        }
    }

    // bindParentToChildIgnoringJSDoc (ignoringJSDoc) or bindParentToChild, then the child's own children unless skipped.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static void BindParentToChild(NodeStore store, Node child, Node parent, bool incremental, bool ignoringJSDoc)
    {
        if (incremental && store.Parent(child) == parent)
        {
            return; // "skip"
        }
        SetParent(store, child, parent);
        if (!ignoringJSDoc && UtilitiesPublic.HasJSDocNodes(store, child))
        {
            // bindJSDoc: every JSDoc comment of the child, and everything below it, ignoring JSDoc.
            var jsDoc = store.JsDoc(child)!;
            for (var i = 0; i < jsDoc.Count; i++)
            {
                var doc = jsDoc[i];
                if (!(incremental && store.Parent(doc) == child))
                {
                    SetParent(store, doc, child);
                }
                NodeVisitor.ForEachChild<ParentBinder, bool>(store, doc, new ParentBinder(store, doc, incremental, /*ignoringJSDoc*/ true));
            }
        }
        NodeVisitor.ForEachChild<ParentBinder, bool>(store, child, new ParentBinder(store, child, incremental, ignoringJSDoc));
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static TextRef GetEscapedTextOfJsxNamespacedName(NodeStore store, Node node)
    {
        // `${namespace.escapedText}:${idText(name)}` (Track O: a span of the source text when the three parts are adjacent there).
        var value = new TextBuilder(store.Texts);
        value.Append(store.EscapedText(store.Namespace(node)));
        value.Append(":");
        value.Append(UtilitiesPublic.IdText(store, store.Name(node)));
        return value.Result();
    }

    //// JavaScript semantics the ported functions rely on.

    /// <summary>JavaScript's WhiteSpace or LineTerminator (the characters of `\s` and of String.prototype.trim).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJsWhiteSpaceOrLineTerminator(char ch)
    {
        switch (ch)
        {
            case '\t':
            case '\n':
            case '\v':
            case '\f':
            case '\r':
            case ' ':
            case (char)0x00A0:
            case (char)0x1680:
            case (char)0x2028:
            case (char)0x2029:
            case (char)0x202F:
            case (char)0x205F:
            case (char)0x3000:
            case (char)0xFEFF:
                return true;
            default:
                return ch >= (char)0x2000 && ch <= (char)0x200A;
        }
    }

    /// <summary>JavaScript's String.prototype.substring(start, end): arguments are clamped and swapped if reversed.</summary>
    private static string SubstringJs(ReadOnlySpan<char> s, int start, int end)
    {
        start = Math.Clamp(start, 0, s.Length);
        end = Math.Clamp(end, 0, s.Length);
        if (start > end)
        {
            (start, end) = (end, start);
        }
        return s.Slice(start, end - start).ToString();
    }
}
