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

    internal static int GetFullWidth(Node node)
    {
        return node.End - node.Pos;
    }

    internal static bool ContainsParseError(Node node)
    {
        AggregateChildData(node);
        return (node.Flags & NodeFlags.ThisNodeOrAnySubNodesHasError) != 0;
    }

    private static void AggregateChildData(Node node)
    {
        if ((node.Flags & NodeFlags.HasAggregatedChildData) == 0)
        {
            // A node is considered to contain a parse error if:
            //  a) the parser explicitly marked that it had an error
            //  b) any of it's children reported that it had an error.
            var thisNodeOrAnySubNodesHasError = ((node.Flags & NodeFlags.ThisNodeHasError) != 0) ||
                NodeVisitor.ForEachChild<object>(node, child => ContainsParseError(child) ? true : null) != null;

            // If so, mark ourselves accordingly.
            if (thisNodeOrAnySubNodesHasError)
            {
                node.Flags |= NodeFlags.ThisNodeOrAnySubNodesHasError;
            }

            // Also mark that we've propagated the child information to this node.  This way we can
            // always consult the bit directly on this node without needing to check its children
            // again.
            node.Flags |= NodeFlags.HasAggregatedChildData;
        }
    }

    internal static SourceFile? GetSourceFileOfNode(Node? node)
    {
        while (node != null && node.Kind != SyntaxKind.SourceFile)
        {
            node = node.Parent;
        }
        return (SourceFile?)node;
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

    internal static bool NodeIsMissing(Node? node)
    {
        if (node == null)
        {
            return true;
        }

        return node.Pos == node.End && node.Pos >= 0 && node.Kind != SyntaxKind.EndOfFileToken;
    }

    internal static bool NodeIsPresent(Node? node)
    {
        return !NodeIsMissing(node);
    }

    internal static string GetSourceTextOfNodeFromSourceFile(SourceFile sourceFile, Node node, bool includeTrivia = false)
    {
        return GetTextOfNodeFromSourceText(sourceFile.Text, node, includeTrivia);
    }

    private static bool IsJSDocTypeExpressionOrChild(Node node)
    {
        return UtilitiesPublic.FindAncestor(node, FactoryNodeTests.IsJSDocTypeExpression) != null;
    }

    internal static string GetTextOfNodeFromSourceText(string sourceText, Node node, bool includeTrivia = false)
    {
        if (NodeIsMissing(node))
        {
            return "";
        }

        var start = includeTrivia ? node.Pos : Scanner.SkipTrivia(sourceText, node.Pos);
        var text = SubstringJs(sourceText, start, node.End);

        if (IsJSDocTypeExpressionOrChild(node))
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

    internal static EmitFlags GetEmitFlags(Node node)
    {
        var emitNode = (EmitNode?)node.EmitNode;
        return emitNode != null ? emitNode.Flags : 0;
    }

    internal static bool IsComputedNonLiteralName(Node name)
    {
        return name.Kind == SyntaxKind.ComputedPropertyName && !IsStringOrNumericLiteralLike(((ComputedPropertyName)name).Expression);
    }

    internal static string? TryGetTextOfPropertyName(Node name)
    {
        switch (name.Kind)
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.PrivateIdentifier:
                return ((EmitNode?)name.EmitNode)?.AutoGenerate != null ? null : (string)(name is Identifier id ? id.EscapedText! : ((PrivateIdentifier)name).EscapedText!);
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                return UtilitiesPublic.EscapeLeadingUnderscores(GetLiteralText(name));
            case SyntaxKind.ComputedPropertyName:
                var expression = ((ComputedPropertyName)name).Expression;
                if (IsStringOrNumericLiteralLike(expression))
                {
                    return UtilitiesPublic.EscapeLeadingUnderscores(GetLiteralText(expression));
                }
                return null;
            case SyntaxKind.JsxNamespacedName:
                return GetEscapedTextOfJsxNamespacedName((JsxNamespacedName)name);
            default:
                Debug.AssertNever(name);
                return null;
        }
    }

    /// <summary>`(name as LiteralLikeNode).text` for the literal kinds read above.</summary>
    private static string GetLiteralText(Node node)
    {
        return node switch
        {
            StringLiteral n => n.Text,
            NumericLiteral n => n.Text,
            BigIntLiteral n => n.Text,
            NoSubstitutionTemplateLiteral n => n.Text,
            _ => throw new InvalidCastException(node.Kind.ToString()),
        };
    }

    internal static string GetTextOfPropertyName(Node name)
    {
        return Debug.CheckDefined(TryGetTextOfPropertyName(name));
    }

    internal static DiagnosticWithLocation CreateDiagnosticForNodeInSourceFile(SourceFile sourceFile, Node node, DiagnosticMessage message, params object?[] args)
    {
        var span = GetErrorSpanForNode(sourceFile, node);
        return CreateFileDiagnostic(sourceFile, span.Start, span.Length, message, args);
    }

    internal static TextSpan GetSpanOfTokenAtPosition(SourceFile sourceFile, int pos)
    {
        var scanner = Scanner.CreateScanner(sourceFile.LanguageVersion, /*skipTrivia*/ true, sourceFile.LanguageVariant, sourceFile.Text, /*onError*/ null, pos);
        scanner.Scan();
        var start = scanner.GetTokenStart();
        return UtilitiesPublic.CreateTextSpanFromBounds(start, scanner.GetTokenEnd());
    }

    private static TextSpan GetErrorSpanForArrowFunction(SourceFile sourceFile, ArrowFunction node)
    {
        var pos = Scanner.SkipTrivia(sourceFile.Text, node.Pos);
        if (node.Body != null && node.Body.Kind == SyntaxKind.Block)
        {
            var startLine = Scanner.GetLineAndCharacterOfPosition(sourceFile, node.Body.Pos).Line;
            var endLine = Scanner.GetLineAndCharacterOfPosition(sourceFile, node.Body.End).Line;
            if (startLine < endLine)
            {
                // The arrow function spans multiple lines,
                // make the error span be the first line, inclusive.
                return UtilitiesPublic.CreateTextSpan(pos, GetEndLinePosition(startLine, sourceFile) - pos + 1);
            }
        }
        return UtilitiesPublic.CreateTextSpanFromBounds(pos, node.End);
    }

    internal static TextSpan GetErrorSpanForNode(SourceFile sourceFile, Node node)
    {
        Node? errorNode = node;
        switch (node.Kind)
        {
            case SyntaxKind.SourceFile:
            {
                var pos = Scanner.SkipTrivia(sourceFile.Text, 0, /*stopAfterLineBreak*/ false);
                if (pos == sourceFile.Text.Length)
                {
                    // file is empty - return span for the beginning of the file
                    return UtilitiesPublic.CreateTextSpan(0, 0);
                }
                return GetSpanOfTokenAtPosition(sourceFile, pos);
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
                errorNode = NodeAccessors.GetName(node);
                break;
            case SyntaxKind.ArrowFunction:
                return GetErrorSpanForArrowFunction(sourceFile, (ArrowFunction)node);
            case SyntaxKind.CaseClause:
            case SyntaxKind.DefaultClause:
            {
                var statements = node is CaseClause caseClause ? caseClause.Statements : ((DefaultClause)node).Statements;
                var start = Scanner.SkipTrivia(sourceFile.Text, node.Pos);
                var end = statements.Count > 0 ? statements[0].Pos : node.End;
                return UtilitiesPublic.CreateTextSpanFromBounds(start, end);
            }
            case SyntaxKind.ReturnStatement:
            case SyntaxKind.YieldExpression:
            {
                var pos = Scanner.SkipTrivia(sourceFile.Text, node.Pos);
                return GetSpanOfTokenAtPosition(sourceFile, pos);
            }
            case SyntaxKind.SatisfiesExpression:
            {
                var pos = Scanner.SkipTrivia(sourceFile.Text, ((SatisfiesExpression)node).Expression.End);
                return GetSpanOfTokenAtPosition(sourceFile, pos);
            }
            case SyntaxKind.JSDocSatisfiesTag:
            {
                var pos = Scanner.SkipTrivia(sourceFile.Text, ((JSDocSatisfiesTag)node).TagName.Pos);
                return GetSpanOfTokenAtPosition(sourceFile, pos);
            }
            case SyntaxKind.Constructor:
            {
                var constructorDeclaration = (ConstructorDeclaration)node;
                var start = Scanner.SkipTrivia(sourceFile.Text, constructorDeclaration.Pos);
                var scanner = Scanner.CreateScanner(sourceFile.LanguageVersion, /*skipTrivia*/ true, sourceFile.LanguageVariant, sourceFile.Text, /*onError*/ null, start);
                var token = scanner.Scan();
                while (token != SyntaxKind.ConstructorKeyword && token != SyntaxKind.EndOfFileToken)
                {
                    token = scanner.Scan();
                }
                var end = scanner.GetTokenEnd();
                return UtilitiesPublic.CreateTextSpanFromBounds(start, end);
            }
        }

        if (errorNode == null)
        {
            // If we don't have a better node, then just set the error on the first token of
            // construct.
            return GetSpanOfTokenAtPosition(sourceFile, node.Pos);
        }

        Debug.Assert(!FactoryNodeTests.IsJSDoc(errorNode));

        var isMissing = NodeIsMissing(errorNode);
        var errorPos = isMissing || FactoryNodeTests.IsJsxText(node)
            ? errorNode.Pos
            : Scanner.SkipTrivia(sourceFile.Text, errorNode.Pos);

        // These asserts should all be satisfied for a properly constructed `errorNode`.
        if (isMissing)
        {
            Debug.Assert(errorPos == errorNode.Pos, "This failure could trigger https://github.com/Microsoft/TypeScript/issues/20809");
            Debug.Assert(errorPos == errorNode.End, "This failure could trigger https://github.com/Microsoft/TypeScript/issues/20809");
        }
        else
        {
            Debug.Assert(errorPos >= errorNode.Pos, "This failure could trigger https://github.com/Microsoft/TypeScript/issues/20809");
            Debug.Assert(errorPos <= errorNode.End, "This failure could trigger https://github.com/Microsoft/TypeScript/issues/20809");
        }

        return UtilitiesPublic.CreateTextSpanFromBounds(errorPos, errorNode.End);
    }

    internal static List<CommentRange>? GetJSDocCommentRanges(Node node, string text)
    {
        var commentRanges = (node.Kind == SyntaxKind.Parameter ||
                node.Kind == SyntaxKind.TypeParameter ||
                node.Kind == SyntaxKind.FunctionExpression ||
                node.Kind == SyntaxKind.ArrowFunction ||
                node.Kind == SyntaxKind.ParenthesizedExpression ||
                node.Kind == SyntaxKind.VariableDeclaration ||
                node.Kind == SyntaxKind.ExportSpecifier) ?
            Core.Concatenate(Scanner.GetTrailingCommentRanges(text, node.Pos), Scanner.GetLeadingCommentRanges(text, node.Pos)) :
            Scanner.GetLeadingCommentRanges(text, node.Pos);
        // True if the comment starts with '/**' but not if it is '/**/'
        return Core.Filter(commentRanges, comment =>
            comment.End <= node.End && // Due to parse errors sometime empty parameter may get comments assigned to it that end up not in parameter range
            CharCodeAtOrNaN(text, comment.Pos + 1) == (int)CharacterCodes.asterisk &&
            CharCodeAtOrNaN(text, comment.Pos + 2) == (int)CharacterCodes.asterisk &&
            CharCodeAtOrNaN(text, comment.Pos + 3) != (int)CharacterCodes.slash);
    }

    internal static bool IsVariableLike(Node? node)
    {
        if (node != null)
        {
            switch (node.Kind)
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

    internal static bool IsSuperProperty(Node node)
    {
        var kind = node.Kind;
        return (kind == SyntaxKind.PropertyAccessExpression || kind == SyntaxKind.ElementAccessExpression)
            && GetAccessExpressionExpression(node).Kind == SyntaxKind.SuperKeyword;
    }

    /// <summary>`(node as PropertyAccessExpression | ElementAccessExpression).expression`.</summary>
    private static Node GetAccessExpressionExpression(Node node)
    {
        return node is PropertyAccessExpression propertyAccess ? propertyAccess.Expression : ((ElementAccessExpression)node).Expression;
    }

    internal static bool IsInJSFile(Node? node)
    {
        return node != null && (node.Flags & NodeFlags.JavaScriptFile) != 0;
    }

    internal static bool IsStringDoubleQuoted(Node str, SourceFile sourceFile)
    {
        return CharCodeAtOrNaN(GetSourceTextOfNodeFromSourceFile(sourceFile, str), 0) == (int)CharacterCodes.doubleQuote;
    }

    internal static Node GetRightMostAssignedExpression(Node node)
    {
        while (IsAssignmentExpression(node, /*excludeCompoundAssignment*/ true))
        {
            node = ((BinaryExpression)node).Right;
        }
        return node;
    }

    internal static bool IsExportsIdentifier(Node node)
    {
        return FactoryNodeTests.IsIdentifier(node) && (string)((Identifier)node).EscapedText! == "exports";
    }

    internal static bool IsModuleIdentifier(Node node)
    {
        return FactoryNodeTests.IsIdentifier(node) && (string)((Identifier)node).EscapedText! == "module";
    }

    internal static bool IsModuleExportsAccessExpression(Node node)
    {
        return (FactoryNodeTests.IsPropertyAccessExpression(node) || IsLiteralLikeElementAccess(node))
            && IsModuleIdentifier(GetAccessExpressionExpression(node))
            && GetElementOrPropertyAccessName(node) == "exports";
    }

    // Given a BinaryExpression, returns SpecialPropertyAssignmentKind for the various kinds of property
    // assignments we treat as special in the binder
    internal static AssignmentDeclarationKind GetAssignmentDeclarationKind(Node expr)
    {
        var special = GetAssignmentDeclarationKindWorker(expr);
        return special == AssignmentDeclarationKind.Property || IsInJSFile(expr) ? special : AssignmentDeclarationKind.None;
    }

    internal static bool IsBindableObjectDefinePropertyCall(CallExpression expr)
    {
        return Core.Length(expr.Arguments) == 3 &&
            FactoryNodeTests.IsPropertyAccessExpression(expr.Expression) &&
            FactoryNodeTests.IsIdentifier(((PropertyAccessExpression)expr.Expression).Expression) &&
            UtilitiesPublic.IdText(((PropertyAccessExpression)expr.Expression).Expression) == "Object" &&
            UtilitiesPublic.IdText(((PropertyAccessExpression)expr.Expression).Name) == "defineProperty" &&
            IsStringOrNumericLiteralLike(expr.Arguments[1]) &&
            IsBindableStaticNameExpression(expr.Arguments[0], /*excludeThisKeyword*/ true);
    }

    /// <summary>
    /// x[0] OR x['a'] OR x[Symbol.y]
    /// </summary>
    private static bool IsLiteralLikeElementAccess(Node node)
    {
        return FactoryNodeTests.IsElementAccessExpression(node) && IsStringOrNumericLiteralLike(((ElementAccessExpression)node).ArgumentExpression);
    }

    /// <summary>
    /// Any series of property and element accesses.
    /// </summary>
    internal static bool IsBindableStaticAccessExpression(Node node, bool? excludeThisKeyword = null)
    {
        return FactoryNodeTests.IsPropertyAccessExpression(node) && (excludeThisKeyword != true && ((PropertyAccessExpression)node).Expression.Kind == SyntaxKind.ThisKeyword || FactoryNodeTests.IsIdentifier(((PropertyAccessExpression)node).Name) && IsBindableStaticNameExpression(((PropertyAccessExpression)node).Expression, /*excludeThisKeyword*/ true))
            || IsBindableStaticElementAccessExpression(node, excludeThisKeyword);
    }

    /// <summary>
    /// Any series of property and element accesses, ending in a literal element access
    /// </summary>
    internal static bool IsBindableStaticElementAccessExpression(Node node, bool? excludeThisKeyword = null)
    {
        return IsLiteralLikeElementAccess(node)
            && ((excludeThisKeyword != true && ((ElementAccessExpression)node).Expression.Kind == SyntaxKind.ThisKeyword) ||
                IsEntityNameExpression(((ElementAccessExpression)node).Expression) ||
                IsBindableStaticAccessExpression(((ElementAccessExpression)node).Expression, /*excludeThisKeyword*/ true));
    }

    internal static bool IsBindableStaticNameExpression(Node node, bool? excludeThisKeyword = null)
    {
        return IsEntityNameExpression(node) || IsBindableStaticAccessExpression(node, excludeThisKeyword);
    }

    private static AssignmentDeclarationKind GetAssignmentDeclarationKindWorker(Node expr)
    {
        if (FactoryNodeTests.IsCallExpression(expr))
        {
            var call = (CallExpression)expr;
            if (!IsBindableObjectDefinePropertyCall(call))
            {
                return AssignmentDeclarationKind.None;
            }
            var entityName = call.Arguments[0];
            if (IsExportsIdentifier(entityName) || IsModuleExportsAccessExpression(entityName))
            {
                return AssignmentDeclarationKind.ObjectDefinePropertyExports;
            }
            if (IsBindableStaticAccessExpression(entityName) && GetElementOrPropertyAccessName(entityName) == "prototype")
            {
                return AssignmentDeclarationKind.ObjectDefinePrototypeProperty;
            }
            return AssignmentDeclarationKind.ObjectDefinePropertyValue;
        }
        var binary = (BinaryExpression)expr;
        if (binary.OperatorToken.Kind != SyntaxKind.EqualsToken || !IsAccessExpression(binary.Left) || IsVoidZero(GetRightMostAssignedExpression(binary)))
        {
            return AssignmentDeclarationKind.None;
        }
        if (IsBindableStaticNameExpression(GetAccessExpressionExpression(binary.Left), /*excludeThisKeyword*/ true) && GetElementOrPropertyAccessName(binary.Left) == "prototype" && FactoryNodeTests.IsObjectLiteralExpression(GetInitializerOfBinaryExpression(binary)))
        {
            // F.prototype = { ... }
            return AssignmentDeclarationKind.Prototype;
        }
        return GetAssignmentDeclarationPropertyAccessKind(binary.Left);
    }

    private static bool IsVoidZero(Node node)
    {
        return FactoryNodeTests.IsVoidExpression(node) && FactoryNodeTests.IsNumericLiteral(((VoidExpression)node).Expression) && ((NumericLiteral)((VoidExpression)node).Expression).Text == "0";
    }

    /// <summary>
    /// Does not handle signed numeric names like `a[+0]` - handling those would require handling prefix unary expressions
    /// throughout late binding handling as well, which is awkward (but ultimately probably doable if there is demand)
    /// </summary>
    internal static Node? GetElementOrPropertyAccessArgumentExpressionOrName(Node node)
    {
        if (FactoryNodeTests.IsPropertyAccessExpression(node))
        {
            return ((PropertyAccessExpression)node).Name;
        }
        var arg = SkipParentheses(((ElementAccessExpression)node).ArgumentExpression);
        if (FactoryNodeTests.IsNumericLiteral(arg) || UtilitiesPublic.IsStringLiteralLike(arg))
        {
            return arg;
        }
        return node;
    }

    internal static string? GetElementOrPropertyAccessName(Node node)
    {
        var name = GetElementOrPropertyAccessArgumentExpressionOrName(node);
        if (name != null)
        {
            if (FactoryNodeTests.IsIdentifier(name))
            {
                return (string)((Identifier)name).EscapedText!;
            }
            if (UtilitiesPublic.IsStringLiteralLike(name) || FactoryNodeTests.IsNumericLiteral(name))
            {
                return UtilitiesPublic.EscapeLeadingUnderscores(GetLiteralText(name));
            }
        }
        return null;
    }

    internal static AssignmentDeclarationKind GetAssignmentDeclarationPropertyAccessKind(Node lhs)
    {
        if (GetAccessExpressionExpression(lhs).Kind == SyntaxKind.ThisKeyword)
        {
            return AssignmentDeclarationKind.ThisProperty;
        }
        else if (IsModuleExportsAccessExpression(lhs))
        {
            // module.exports = expr
            return AssignmentDeclarationKind.ModuleExports;
        }
        else if (IsBindableStaticNameExpression(GetAccessExpressionExpression(lhs), /*excludeThisKeyword*/ true))
        {
            if (IsPrototypeAccess(GetAccessExpressionExpression(lhs)))
            {
                // F.G....prototype.x = expr
                return AssignmentDeclarationKind.PrototypeProperty;
            }

            var nextToLast = lhs;
            while (!FactoryNodeTests.IsIdentifier(GetAccessExpressionExpression(nextToLast)))
            {
                nextToLast = GetAccessExpressionExpression(nextToLast);
            }
            var id = (Identifier)GetAccessExpressionExpression(nextToLast);
            if (
                ((string)id.EscapedText! == "exports" ||
                    (string)id.EscapedText! == "module" && GetElementOrPropertyAccessName(nextToLast) == "exports") &&
                // ExportsProperty does not support binding with computed names
                IsBindableStaticAccessExpression(lhs))
            {
                // exports.name = expr OR module.exports.name = expr OR exports["name"] = expr ...
                return AssignmentDeclarationKind.ExportsProperty;
            }
            if (IsBindableStaticNameExpression(lhs, /*excludeThisKeyword*/ true) || (FactoryNodeTests.IsElementAccessExpression(lhs) && IsDynamicName(lhs)))
            {
                // F.G...x = expr
                return AssignmentDeclarationKind.Property;
            }
        }

        return AssignmentDeclarationKind.None;
    }

    internal static Node GetInitializerOfBinaryExpression(BinaryExpression expr)
    {
        while (FactoryNodeTests.IsBinaryExpression(expr.Right))
        {
            expr = (BinaryExpression)expr.Right;
        }
        return expr.Right;
    }

    private static Node? GetSourceOfDefaultedAssignment(Node node)
    {
        if (FactoryNodeTests.IsExpressionStatement(node) &&
            FactoryNodeTests.IsBinaryExpression(((ExpressionStatement)node).Expression))
        {
            var expression = (BinaryExpression)((ExpressionStatement)node).Expression;
            if (GetAssignmentDeclarationKind(expression) != AssignmentDeclarationKind.None &&
                FactoryNodeTests.IsBinaryExpression(expression.Right) &&
                (((BinaryExpression)expression.Right).OperatorToken.Kind == SyntaxKind.BarBarToken || ((BinaryExpression)expression.Right).OperatorToken.Kind == SyntaxKind.QuestionQuestionToken))
            {
                return ((BinaryExpression)expression.Right).Right;
            }
        }
        return null;
    }

    private static Node? GetSingleInitializerOfVariableStatementOrPropertyDeclaration(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.VariableStatement:
                var v = GetSingleVariableOfVariableStatement(node);
                return v?.Initializer;
            case SyntaxKind.PropertyDeclaration:
                return ((PropertyDeclaration)node).Initializer;
            case SyntaxKind.PropertyAssignment:
                return ((PropertyAssignment)node).Initializer;
        }
        return null;
    }

    internal static VariableDeclaration? GetSingleVariableOfVariableStatement(Node node)
    {
        return FactoryNodeTests.IsVariableStatement(node) ? (VariableDeclaration?)Core.FirstOrUndefined(((VariableStatement)node).DeclarationList.Declarations) : null;
    }

    private static Node? GetNestedModuleDeclaration(Node node)
    {
        return FactoryNodeTests.IsModuleDeclaration(node) &&
                ((ModuleDeclaration)node).Body != null &&
                ((ModuleDeclaration)node).Body!.Kind == SyntaxKind.ModuleDeclaration
            ? ((ModuleDeclaration)node).Body
            : null;
    }

    internal static bool CanHaveJSDoc(Node node)
    {
        switch (node.Kind)
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
    internal static IReadOnlyList<Node> GetJSDocCommentsAndTags(Node hostNode, bool? noCache = null)
    {
        List<Node>? result = null;
        // Pull parameter comments from declaring function as well
        if (IsVariableLike(hostNode) && UtilitiesPublic.HasInitializer(hostNode) && UtilitiesPublic.HasJSDocNodes(NodeAccessors.GetInitializer(hostNode)!))
        {
            result = Core.AddRange(result, FilterOwnedJSDocTags(hostNode, JSDocContainers.GetJsDoc(NodeAccessors.GetInitializer(hostNode)!)!));
        }

        Node? node = hostNode;
        while (node != null && node.Parent != null)
        {
            if (UtilitiesPublic.HasJSDocNodes(node))
            {
                result = Core.AddRange(result, FilterOwnedJSDocTags(hostNode, JSDocContainers.GetJsDoc(node)!));
            }

            if (node.Kind == SyntaxKind.Parameter)
            {
                result = Core.AddRange(result, noCache == true ? UtilitiesPublic.GetJSDocParameterTagsNoCache((ParameterDeclaration)node) : UtilitiesPublic.GetJSDocParameterTags((ParameterDeclaration)node));
                break;
            }
            if (node.Kind == SyntaxKind.TypeParameter)
            {
                result = Core.AddRange(result, noCache == true ? UtilitiesPublic.GetJSDocTypeParameterTagsNoCache((TypeParameterDeclaration)node) : UtilitiesPublic.GetJSDocTypeParameterTags((TypeParameterDeclaration)node));
                break;
            }
            node = GetNextJSDocCommentLocation(node);
        }
        return result ?? (IReadOnlyList<Node>)Core.EmptyArray<Node>();
    }

    private static IReadOnlyList<Node> FilterOwnedJSDocTags(Node hostNode, List<JSDoc> comments)
    {
        var lastJsDoc = Core.Last(comments);
        // flatMap<JSDoc, JSDoc | JSDocTag>(comments, jsDoc => ...)
        List<Node>? result = null;
        foreach (var jsDoc in comments)
        {
            IReadOnlyList<Node>? mapped;
            if (jsDoc == lastJsDoc)
            {
                var ownedTags = Core.Filter((IReadOnlyList<Node>?)jsDoc.Tags, tag => OwnsJSDocTag(hostNode, tag));
                mapped = jsDoc.Tags == ownedTags ? [jsDoc] : ownedTags;
            }
            else
            {
                mapped = Core.Filter((IReadOnlyList<Node>?)jsDoc.Tags, FactoryNodeTests.IsJSDocOverloadTag);
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
    private static bool OwnsJSDocTag(Node hostNode, Node tag)
    {
        return !(FactoryNodeTests.IsJSDocTypeTag(tag) || FactoryNodeTests.IsJSDocSatisfiesTag(tag))
            || tag.Parent == null
            || !FactoryNodeTests.IsJSDoc(tag.Parent)
            || !FactoryNodeTests.IsParenthesizedExpression(tag.Parent.Parent)
            || tag.Parent.Parent == hostNode;
    }

    internal static Node? GetNextJSDocCommentLocation(Node node)
    {
        var parent = node.Parent;
        if (
            parent.Kind == SyntaxKind.PropertyAssignment ||
            parent.Kind == SyntaxKind.ExportAssignment ||
            parent.Kind == SyntaxKind.PropertyDeclaration ||
            parent.Kind == SyntaxKind.ExpressionStatement && node.Kind == SyntaxKind.PropertyAccessExpression ||
            parent.Kind == SyntaxKind.ReturnStatement ||
            GetNestedModuleDeclaration(parent) != null ||
            IsAssignmentExpression(node))
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
            parent.Parent != null &&
            (GetSingleVariableOfVariableStatement(parent.Parent) == node || IsAssignmentExpression(parent)))
        {
            return parent.Parent;
        }
        else if (
            parent.Parent != null && parent.Parent.Parent != null &&
            (GetSingleVariableOfVariableStatement(parent.Parent.Parent) != null ||
                GetSingleInitializerOfVariableStatementOrPropertyDeclaration(parent.Parent.Parent) == node ||
                GetSourceOfDefaultedAssignment(parent.Parent.Parent) != null))
        {
            return parent.Parent.Parent;
        }
        return null;
    }

    internal static Node SkipParentheses(Node node, bool? excludeJSDocTypeAssertions = null)
    {
        var flags = excludeJSDocTypeAssertions == true ?
            OuterExpressionKinds.Parentheses | OuterExpressionKinds.ExcludeJSDocTypeAssertion :
            OuterExpressionKinds.Parentheses;
        return FactoryUtilities.SkipOuterExpressions(node, flags);
    }

    internal static bool IsKeyword(SyntaxKind token)
    {
        return SyntaxKind.FirstKeyword <= token && token <= SyntaxKind.LastKeyword;
    }

    internal static bool IsPunctuation(SyntaxKind token)
    {
        return SyntaxKind.FirstPunctuation <= token && token <= SyntaxKind.LastPunctuation;
    }

    internal static bool IsKeywordOrPunctuation(SyntaxKind token)
    {
        return IsKeyword(token) || IsPunctuation(token);
    }

    internal static bool IsStringOrNumericLiteralLike(Node node)
    {
        return UtilitiesPublic.IsStringLiteralLike(node) || FactoryNodeTests.IsNumericLiteral(node);
    }

    internal static bool IsSignedNumericLiteral(Node node)
    {
        return FactoryNodeTests.IsPrefixUnaryExpression(node) && (((PrefixUnaryExpression)node).Operator == SyntaxKind.PlusToken || ((PrefixUnaryExpression)node).Operator == SyntaxKind.MinusToken) && FactoryNodeTests.IsNumericLiteral(((PrefixUnaryExpression)node).Operand);
    }

    internal static bool IsDynamicName(Node name)
    {
        if (!(name.Kind == SyntaxKind.ComputedPropertyName || name.Kind == SyntaxKind.ElementAccessExpression))
        {
            return false;
        }
        var expr = FactoryNodeTests.IsElementAccessExpression(name) ? SkipParentheses(((ElementAccessExpression)name).ArgumentExpression) : ((ComputedPropertyName)name).Expression;
        return !IsStringOrNumericLiteralLike(expr) &&
            !IsSignedNumericLiteral(expr);
    }

    internal static bool NodeIsSynthesized(TextRange range)
    {
        return PositionIsSynthesized(range.Pos)
            || PositionIsSynthesized(range.End);
    }

    internal static Associativity GetExpressionAssociativity(Node expression)
    {
        var @operator = GetOperator(expression);
        var hasArguments = expression.Kind == SyntaxKind.NewExpression && ((NewExpression)expression).Arguments != null;
        return GetOperatorAssociativity(expression.Kind, @operator, hasArguments);
    }

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

    internal static OperatorPrecedence GetExpressionPrecedence(Node expression)
    {
        var @operator = GetOperator(expression);
        var hasArguments = expression.Kind == SyntaxKind.NewExpression && ((NewExpression)expression).Arguments != null;
        return GetOperatorPrecedence(expression.Kind, @operator, hasArguments);
    }

    private static SyntaxKind GetOperator(Node expression)
    {
        if (expression.Kind == SyntaxKind.BinaryExpression)
        {
            return ((BinaryExpression)expression).OperatorToken.Kind;
        }
        else if (expression.Kind == SyntaxKind.PrefixUnaryExpression || expression.Kind == SyntaxKind.PostfixUnaryExpression)
        {
            return expression is PrefixUnaryExpression prefix ? prefix.Operator : ((PostfixUnaryExpression)expression).Operator;
        }
        else
        {
            return expression.Kind;
        }
    }

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
    private static TokenFlags GetTemplateFlags(Node node)
    {
        return node switch
        {
            NoSubstitutionTemplateLiteral n => n.TemplateFlags,
            TemplateHead n => n.TemplateFlags,
            TemplateMiddle n => n.TemplateFlags,
            TemplateTail n => n.TemplateFlags,
            _ => TokenFlags.None,
        };
    }

    private static bool ContainsInvalidEscapeFlag(Node node)
    {
        return (GetTemplateFlags(node) & TokenFlags.ContainsInvalidEscape) != 0;
    }

    internal static bool HasInvalidEscape(Node? template)
    {
        return template != null && (FactoryNodeTests.IsNoSubstitutionTemplateLiteral(template)
            ? ContainsInvalidEscapeFlag(template)
            : (ContainsInvalidEscapeFlag(((TemplateExpression)template).Head) || Core.Some(((TemplateExpression)template).TemplateSpans, span => ContainsInvalidEscapeFlag(((TemplateSpan)span).Literal))));
    }

    internal static bool IsThisIdentifier(Node? node)
    {
        return node != null && node.Kind == SyntaxKind.Identifier && IdentifierIsThisKeyword((Identifier)node);
    }

    internal static bool IdentifierIsThisKeyword(Identifier id)
    {
        return (string)id.EscapedText! == "this";
    }

    internal static bool HasSyntacticModifier(Node node, ModifierFlags flags)
    {
        return GetSelectedSyntacticModifierFlags(node, flags) != 0;
    }

    internal static ModifierFlags GetSelectedSyntacticModifierFlags(Node node, ModifierFlags flags)
    {
        return GetSyntacticModifierFlags(node) & flags;
    }

    private static ModifierFlags GetModifierFlagsWorker(Node node, bool includeJSDoc, bool? alwaysIncludeJSDoc = null)
    {
        if (node.Kind >= SyntaxKind.FirstToken && node.Kind <= SyntaxKind.LastToken)
        {
            return ModifierFlags.None;
        }

        if ((node.ModifierFlagsCache & ModifierFlags.HasComputedFlags) == 0)
        {
            node.ModifierFlagsCache = GetSyntacticModifierFlagsNoCache(node) | ModifierFlags.HasComputedFlags;
        }

        if (alwaysIncludeJSDoc == true || includeJSDoc && IsInJSFile(node))
        {
            if ((node.ModifierFlagsCache & ModifierFlags.HasComputedJSDocModifiers) == 0 && node.Parent != null)
            {
                node.ModifierFlagsCache |= GetRawJSDocModifierFlagsNoCache(node) | ModifierFlags.HasComputedJSDocModifiers;
            }
            return SelectEffectiveModifierFlags(node.ModifierFlagsCache);
        }

        return SelectSyntacticModifierFlags(node.ModifierFlagsCache);
    }

    /// <summary>
    /// Gets the ModifierFlags for syntactic modifiers on the provided node. The modifiers will be cached on the node to improve performance.
    ///
    /// NOTE: This function does not use `parent` pointers and will not include modifiers from JSDoc.
    /// </summary>
    internal static ModifierFlags GetSyntacticModifierFlags(Node node)
    {
        return GetModifierFlagsWorker(node, /*includeJSDoc*/ false);
    }

    private static ModifierFlags GetRawJSDocModifierFlagsNoCache(Node node)
    {
        var flags = ModifierFlags.None;
        if (node.Parent != null && !FactoryNodeTests.IsParameter(node))
        {
            if (IsInJSFile(node))
            {
                if (UtilitiesPublic.GetJSDocPublicTagNoCache(node) != null)
                {
                    flags |= ModifierFlags.JSDocPublic;
                }
                if (UtilitiesPublic.GetJSDocPrivateTagNoCache(node) != null)
                {
                    flags |= ModifierFlags.JSDocPrivate;
                }
                if (UtilitiesPublic.GetJSDocProtectedTagNoCache(node) != null)
                {
                    flags |= ModifierFlags.JSDocProtected;
                }
                if (UtilitiesPublic.GetJSDocReadonlyTagNoCache(node) != null)
                {
                    flags |= ModifierFlags.JSDocReadonly;
                }
                if (UtilitiesPublic.GetJSDocOverrideTagNoCache(node) != null)
                {
                    flags |= ModifierFlags.JSDocOverride;
                }
            }
            if (UtilitiesPublic.GetJSDocDeprecatedTagNoCache(node) != null)
            {
                flags |= ModifierFlags.Deprecated;
            }
        }

        return flags;
    }

    private static ModifierFlags SelectSyntacticModifierFlags(ModifierFlags flags)
    {
        return flags & ModifierFlags.SyntacticModifiers;
    }

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
    internal static ModifierFlags GetSyntacticModifierFlagsNoCache(Node node)
    {
        var flags = FactoryUtilitiesPublic.CanHaveModifiers(node) ? ModifiersToFlags(NodeAccessors.GetModifiers(node)) : ModifierFlags.None;
        if ((node.Flags & NodeFlags.NestedNamespace) != 0 || node.Kind == SyntaxKind.Identifier && (node.Flags & NodeFlags.IdentifierIsInJSDocNamespace) != 0)
        {
            flags |= ModifierFlags.Export;
        }
        return flags;
    }

    internal static ModifierFlags ModifiersToFlags(IReadOnlyList<Node>? modifiers)
    {
        var flags = ModifierFlags.None;
        if (modifiers != null)
        {
            foreach (var modifier in modifiers)
            {
                flags |= ModifierToFlag(modifier.Kind);
            }
        }
        return flags;
    }

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

    internal static bool IsLogicalOrCoalescingAssignmentOperator(SyntaxKind token)
    {
        return token == SyntaxKind.BarBarEqualsToken
            || token == SyntaxKind.AmpersandAmpersandEqualsToken
            || token == SyntaxKind.QuestionQuestionEqualsToken;
    }

    internal static bool IsAssignmentOperator(SyntaxKind token)
    {
        return token >= SyntaxKind.FirstAssignment && token <= SyntaxKind.LastAssignment;
    }

    internal static bool IsAssignmentExpression(Node node, bool? excludeCompoundAssignment = null)
    {
        return FactoryNodeTests.IsBinaryExpression(node)
            && (excludeCompoundAssignment == true
                ? ((BinaryExpression)node).OperatorToken.Kind == SyntaxKind.EqualsToken
                : IsAssignmentOperator(((BinaryExpression)node).OperatorToken.Kind))
            && UtilitiesPublic.IsLeftHandSideExpression(((BinaryExpression)node).Left);
    }

    internal static bool IsEntityNameExpression(Node node)
    {
        return node.Kind == SyntaxKind.Identifier || IsPropertyAccessEntityNameExpression(node);
    }

    internal static bool IsPropertyAccessEntityNameExpression(Node node)
    {
        return FactoryNodeTests.IsPropertyAccessExpression(node) && FactoryNodeTests.IsIdentifier(((PropertyAccessExpression)node).Name) && IsEntityNameExpression(((PropertyAccessExpression)node).Expression);
    }

    internal static bool IsPrototypeAccess(Node node)
    {
        return IsBindableStaticAccessExpression(node) && GetElementOrPropertyAccessName(node) == "prototype";
    }

    internal static bool IsAccessExpression(Node node)
    {
        return node.Kind == SyntaxKind.PropertyAccessExpression || node.Kind == SyntaxKind.ElementAccessExpression;
    }

    internal static Node GetLeftmostExpression(Node node, bool stopAtCallExpressions)
    {
        while (true)
        {
            switch (node.Kind)
            {
                case SyntaxKind.PostfixUnaryExpression:
                    node = ((PostfixUnaryExpression)node).Operand;
                    continue;

                case SyntaxKind.BinaryExpression:
                    node = ((BinaryExpression)node).Left;
                    continue;

                case SyntaxKind.ConditionalExpression:
                    node = ((ConditionalExpression)node).Condition;
                    continue;

                case SyntaxKind.TaggedTemplateExpression:
                    node = ((TaggedTemplateExpression)node).Tag;
                    continue;

                case SyntaxKind.CallExpression:
                    if (stopAtCallExpressions)
                    {
                        return node;
                    }
                    node = ((CallExpression)node).Expression;
                    continue;

                // falls through (C#: the CallExpression case above continues with its expression, as the cases below)
                case SyntaxKind.AsExpression:
                    node = ((AsExpression)node).Expression;
                    continue;
                case SyntaxKind.ElementAccessExpression:
                    node = ((ElementAccessExpression)node).Expression;
                    continue;
                case SyntaxKind.PropertyAccessExpression:
                    node = ((PropertyAccessExpression)node).Expression;
                    continue;
                case SyntaxKind.NonNullExpression:
                    node = ((NonNullExpression)node).Expression;
                    continue;
                case SyntaxKind.PartiallyEmittedExpression:
                    node = ((PartiallyEmittedExpression)node).Expression;
                    continue;
                case SyntaxKind.SatisfiesExpression:
                    node = ((SatisfiesExpression)node).Expression;
                    continue;
            }

            return node;
        }
    }

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
    internal static T? SetParent<T>(T? child, Node? parent)
        where T : Node
    {
        if (child != null && parent != null)
        {
            child.Parent = parent;
        }
        return child;
    }

    /// <summary>
    /// Bypasses immutability and directly sets the `parent` property of each `Node` recursively.
    /// </summary>
    /// <param name="rootNode">The root node from which to start the recursion.</param>
    /// <param name="incremental">When `true`, only recursively descends through nodes whose `parent` pointers are incorrect.
    /// This allows us to quickly bail out of setting `parent` for subtrees during incremental parsing.</param>
    internal static T? SetParentRecursive<T>(T? rootNode, bool incremental)
        where T : Node
    {
        if (rootNode == null)
        {
            return rootNode;
        }
        Func<Node, Node, object?> cbNode = UtilitiesPublic.IsJSDocNode(rootNode) ? BindParentToChildIgnoringJSDoc : BindParentToChild;
        Parser.ForEachChildRecursively<object>(rootNode, cbNode);
        return rootNode;

        object? BindParentToChildIgnoringJSDoc(Node child, Node parent)
        {
            if (incremental && child.Parent == parent)
            {
                return "skip";
            }
            SetParent(child, parent);
            return null;
        }

        object? BindJSDoc(Node child)
        {
            if (UtilitiesPublic.HasJSDocNodes(child))
            {
                foreach (var doc in JSDocContainers.GetJsDoc(child)!)
                {
                    BindParentToChildIgnoringJSDoc(doc, child);
                    Parser.ForEachChildRecursively<object>(doc, BindParentToChildIgnoringJSDoc);
                }
            }
            return null;
        }

        object? BindParentToChild(Node child, Node parent)
        {
            return BindParentToChildIgnoringJSDoc(child, parent) ?? BindJSDoc(child);
        }
    }

    internal static string GetEscapedTextOfJsxNamespacedName(JsxNamespacedName node)
    {
        return $"{(string)node.Namespace.EscapedText!}:{UtilitiesPublic.IdText(node.Name)}";
    }

    //// JavaScript semantics the ported functions rely on.

    /// <summary>JavaScript's WhiteSpace or LineTerminator (the characters of `\s` and of String.prototype.trim).</summary>
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
    private static string SubstringJs(string s, int start, int end)
    {
        start = Math.Clamp(start, 0, s.Length);
        end = Math.Clamp(end, 0, s.Length);
        if (start > end)
        {
            (start, end) = (end, start);
        }
        return s.Substring(start, end - start);
    }
}
