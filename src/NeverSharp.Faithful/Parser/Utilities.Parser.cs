// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/utilities.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Diagnostics.CodeAnalysis;
using System.Text;
using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// types.ts: `SourceFile extends SourceFileLike` (`text`, `lineMap`). The interface the scanner's getLineStarts takes;
/// the members are the fields the generator declares on SourceFile.
/// </summary>
public sealed partial class SourceFile : ISourceFileLike
{
    string ISourceFileLike.Text => Text;

    IReadOnlyList<int>? ISourceFileLike.LineMap
    {
        get => LineMap;
        set => LineMap = value;
    }
}

/// <summary>
/// types.ts: a DiagnosticRelatedInformation that is a DiagnosticWithDetachedLocation (`file` undefined, `fileName` a string).
/// In TypeScript one object literal serves as both; the C# classes of Diagnostics.cs are unrelated, so the detached
/// related information the parser attaches (see Utilities.AddRelatedInfo) is this subclass.
/// </summary>
internal sealed class DiagnosticRelatedInformationWithDetachedLocation : DiagnosticRelatedInformation
{
    public string FileName { get; set; } = "";
}

/// <summary>utilities.ts (the units the parser reaches): nodes, text, diagnostics, parents.</summary>
internal static partial class Utilities
{
    /// <summary>utilities.ts: `declarationExtensions` of supportedDeclarationExtensions: `[Extension.Dts, Extension.Dcts, Extension.Dmts]`.</summary>
    internal static readonly IReadOnlyList<string> SupportedDeclarationExtensions = [Extension.Dts, Extension.Dcts, Extension.Dmts];

    internal static int GetFullWidth(Node node)
    {
        return node.End - node.Pos;
    }

    // Returns true if this node contains a parse error anywhere underneath it.
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
            // (forEachChild returns the first truthy callback result; the callback returns the child itself for `true`.)
            var thisNodeOrAnySubNodesHasError = ((node.Flags & NodeFlags.ThisNodeHasError) != 0) ||
                NodeVisitor.ForEachChild<Node>(node, child => ContainsParseError(child) ? child : null) != null;

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

    [return: NotNullIfNotNull(nameof(node))]
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
            Debug.Assert(Scanner.IsLineBreak(sourceText[pos]));
            // walk backwards skipping line breaks, stop the the beginning of current line.
            // i.e:
            // <some text>
            // $ <- end of line for this position should match the start position
            while (start <= pos && Scanner.IsLineBreak(sourceText[pos]))
            {
                pos--;
            }
            return pos;
        }
    }

    // Returns true if this node is missing from the actual source code. A 'missing' node is different
    // from 'undefined/defined'. When a node is undefined (which can happen for optional nodes
    // in the tree), it is definitely missing. However, a node may be defined, but still be
    // missing.  This happens whenever the parser knows it needs to parse something, but can't
    // get anything in the source code that it expects at that location. For example:
    //
    //          let a: ;
    //
    // Here, the Type in the Type-Annotation is not-optional (as there is a colon in the source
    // code). So the parser will attempt to parse out a type, and will create an actual node.
    // However, this node will be 'missing' in the sense that no actual source-code/tokens are
    // contained within it.
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
        return UtilitiesPublic.FindAncestor(node, NodeTests.IsJSDocTypeExpression) != null;
    }

    internal static string GetTextOfNodeFromSourceText(string sourceText, Node node, bool includeTrivia = false)
    {
        if (NodeIsMissing(node))
        {
            return "";
        }

        var text = JsSubstring(sourceText, includeTrivia ? node.Pos : Scanner.SkipTrivia(sourceText, node.Pos), node.End);

        if (IsJSDocTypeExpressionOrChild(node))
        {
            // strip space + asterisk at line start
            text = StripLeadingAsterisksOfLines(text);
        }

        return text;
    }

    /// <summary>JavaScript's String.prototype.substring(start, end): both arguments are clamped to the string, and swapped when start is greater than end.</summary>
    private static string JsSubstring(string text, int start, int end)
    {
        var length = text.Length;
        var from = Math.Min(Math.Max(start, 0), length);
        var to = Math.Min(Math.Max(end, 0), length);
        if (from > to)
        {
            (from, to) = (to, from);
        }
        return text.Substring(from, to - from);
    }

    /// <summary>The characters of the regular expression class `\s` (and of String.prototype.trimStart): JavaScript's WhiteSpace and LineTerminator.</summary>
    private static bool IsJsWhiteSpace(char ch)
    {
        return ch == '\t' || ch == '\n' || ch == '\v' || ch == '\f' || ch == '\r' || ch == ' ' ||
            ch == ' ' || ch == ' ' || (ch >= ' ' && ch <= ' ') ||
            ch == '\u2028' || ch == '\u2029' || ch == ' ' || ch == ' ' || ch == '　' || ch == '﻿';
    }

    /// <summary>
    /// `text.split(/\r\n|\n|\r/).map(line => line.replace(/^\s*\*/, "").trimStart()).join("\n")`, written out
    /// (no regular expressions in the port).
    /// </summary>
    private static string StripLeadingAsterisksOfLines(string text)
    {
        var result = new StringBuilder(text.Length);
        var lineStart = 0;
        var firstLine = true;
        while (true)
        {
            // The line ends at "\r\n", "\n" or "\r" (not at the other line terminators).
            var lineEnd = lineStart;
            while (lineEnd < text.Length && text[lineEnd] != '\r' && text[lineEnd] != '\n')
            {
                lineEnd++;
            }

            if (!firstLine)
            {
                result.Append('\n');
            }
            firstLine = false;

            // line.replace(/^\s*\*/, ""): `\s*` is greedy and `\*` is no whitespace, so it matches exactly when the
            // first non-whitespace character of the line is an asterisk.
            var position = lineStart;
            while (position < lineEnd && IsJsWhiteSpace(text[position]))
            {
                position++;
            }
            var contentStart = position < lineEnd && text[position] == '*' ? position + 1 : lineStart;

            // .trimStart()
            while (contentStart < lineEnd && IsJsWhiteSpace(text[contentStart]))
            {
                contentStart++;
            }
            result.Append(text, contentStart, lineEnd - contentStart);

            if (lineEnd >= text.Length)
            {
                break;
            }
            lineStart = text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n' ? lineEnd + 2 : lineEnd + 1;
        }
        return result.ToString();
    }

    /// <summary>Gets flags that control emit behavior of a node.</summary>
    internal static EmitFlags GetEmitFlags(Node node)
    {
        var emitNode = node.EmitNode as EmitNodeData;
        return emitNode != null ? emitNode.Flags : EmitFlags.None;
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
                return (name.EmitNode as EmitNodeData)?.AutoGenerate != null ? null : ((Identifier)name).EscapedText as string;
            case SyntaxKind.PrivateIdentifier:
                return (name.EmitNode as EmitNodeData)?.AutoGenerate != null ? null : ((PrivateIdentifier)name).EscapedText as string;
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                return UtilitiesPublic.EscapeLeadingUnderscores(GetLiteralText(name));
            case SyntaxKind.ComputedPropertyName:
            {
                var expression = ((ComputedPropertyName)name).Expression;
                if (IsStringOrNumericLiteralLike(expression))
                {
                    return UtilitiesPublic.EscapeLeadingUnderscores(GetLiteralText(expression));
                }
                return null;
            }
            case SyntaxKind.JsxNamespacedName:
                return GetEscapedTextOfJsxNamespacedName((JsxNamespacedName)name);
            default:
                Debug.AssertNever(name);
                return null;
        }
    }

    internal static string GetTextOfPropertyName(Node name)
    {
        return Debug.CheckDefined(TryGetTextOfPropertyName(name));
    }

    /// <summary>`node.text` of the literal kinds (TypeScript reads the property from any node; the callers pass these kinds).</summary>
    private static string GetLiteralText(Node node)
    {
        switch (node)
        {
            case StringLiteral stringLiteral:
                return stringLiteral.Text;
            case NumericLiteral numericLiteral:
                return numericLiteral.Text;
            case BigIntLiteral bigIntLiteral:
                return bigIntLiteral.Text;
            case NoSubstitutionTemplateLiteral noSubstitutionTemplateLiteral:
                return noSubstitutionTemplateLiteral.Text;
            default:
                throw new InvalidOperationException("Node of kind " + node.Kind.ToString() + " has no text.");
        }
    }

    internal static string GetTextOfIdentifierOrLiteral(Node node)
    {
        return node.Kind == SyntaxKind.Identifier || node.Kind == SyntaxKind.PrivateIdentifier
            ? UtilitiesPublic.IdText(node)
            : node.Kind == SyntaxKind.JsxNamespacedName
                ? GetTextOfJsxNamespacedName((JsxNamespacedName)node)
                : GetLiteralText(node);
    }

    internal static DiagnosticWithLocation CreateDiagnosticForNodeInSourceFile(SourceFile sourceFile, Node node, DiagnosticMessage message, params object?[] args)
    {
        var span = GetErrorSpanForNode(sourceFile, node);
        return CreateFileDiagnostic(sourceFile, span.Start, span.Length, message, args);
    }

    private static void AssertDiagnosticLocation(string sourceText, int start, int length)
    {
        Debug.AssertGreaterThanOrEqual(start, 0);
        Debug.AssertGreaterThanOrEqual(length, 0);
        Debug.AssertLessThanOrEqual(start, sourceText.Length);
        Debug.AssertLessThanOrEqual(start + length, sourceText.Length);
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

    /// <summary>`(node as NamedDeclaration).name` for the kinds getErrorSpanForNode lists.</summary>
    private static Node? GetNameOfErrorSpanDeclaration(Node node)
    {
        switch (node)
        {
            case VariableDeclaration variableDeclaration:
                return variableDeclaration.Name;
            case BindingElement bindingElement:
                return bindingElement.Name;
            case ClassDeclaration classDeclaration:
                return classDeclaration.Name;
            case ClassExpression classExpression:
                return classExpression.Name;
            case InterfaceDeclaration interfaceDeclaration:
                return interfaceDeclaration.Name;
            case ModuleDeclaration moduleDeclaration:
                return moduleDeclaration.Name;
            case EnumDeclaration enumDeclaration:
                return enumDeclaration.Name;
            case EnumMember enumMember:
                return enumMember.Name;
            case FunctionDeclaration functionDeclaration:
                return functionDeclaration.Name;
            case FunctionExpression functionExpression:
                return functionExpression.Name;
            case MethodDeclaration methodDeclaration:
                return methodDeclaration.Name;
            case GetAccessorDeclaration getAccessorDeclaration:
                return getAccessorDeclaration.Name;
            case SetAccessorDeclaration setAccessorDeclaration:
                return setAccessorDeclaration.Name;
            case TypeAliasDeclaration typeAliasDeclaration:
                return typeAliasDeclaration.Name;
            case PropertyDeclaration propertyDeclaration:
                return propertyDeclaration.Name;
            case PropertySignature propertySignature:
                return propertySignature.Name;
            case NamespaceImport namespaceImport:
                return namespaceImport.Name;
            default:
                return null;
        }
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
                errorNode = GetNameOfErrorSpanDeclaration(node);
                break;
            case SyntaxKind.ArrowFunction:
                return GetErrorSpanForArrowFunction(sourceFile, (ArrowFunction)node);
            case SyntaxKind.CaseClause:
            case SyntaxKind.DefaultClause:
            {
                var statements = node.Kind == SyntaxKind.CaseClause ? ((CaseClause)node).Statements : ((DefaultClause)node).Statements;
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

        Debug.Assert(!NodeTests.IsJSDoc(errorNode));

        var isMissing = NodeIsMissing(errorNode);
        var errorPos = isMissing || NodeTests.IsJsxText(node)
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

    internal static bool IsVariableLike(Node node)
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

    internal static bool IsStringDoubleQuoted(Node str, SourceFile sourceFile)
    {
        var text = GetSourceTextOfNodeFromSourceFile(sourceFile, str);
        return text.Length > 0 && text[0] == (int)CharacterCodes.doubleQuote;
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
        return UtilitiesPublic.IsStringLiteralLike(node) || NodeTests.IsNumericLiteral(node);
    }

    internal static bool IsSignedNumericLiteral(Node node)
    {
        return NodeTests.IsPrefixUnaryExpression(node)
            && (((PrefixUnaryExpression)node).Operator == SyntaxKind.PlusToken || ((PrefixUnaryExpression)node).Operator == SyntaxKind.MinusToken)
            && NodeTests.IsNumericLiteral(((PrefixUnaryExpression)node).Operand);
    }

    /// <summary>
    /// A declaration has a dynamic name if all of the following are true:
    ///   1. The declaration has a computed property name.
    ///   2. The computed name is *not* expressed as a StringLiteral.
    ///   3. The computed name is *not* expressed as a NumericLiteral.
    ///   4. The computed name is *not* expressed as a PlusToken or MinusToken
    ///      immediately followed by a NumericLiteral.
    /// </summary>
    internal static bool IsDynamicName(Node name)
    {
        if (!(name.Kind == SyntaxKind.ComputedPropertyName || name.Kind == SyntaxKind.ElementAccessExpression))
        {
            return false;
        }
        var expr = NodeTests.IsElementAccessExpression(name)
            ? SkipParentheses(((ElementAccessExpression)name).ArgumentExpression)
            : ((ComputedPropertyName)name).Expression;
        return !IsStringOrNumericLiteralLike(expr) &&
            !IsSignedNumericLiteral(expr);
    }

    internal static bool NodeIsSynthesized(TextRange range)
    {
        return PositionIsSynthesized(range.Pos)
            || PositionIsSynthesized(range.End);
    }

    internal static Node SkipParentheses(Node node, bool excludeJSDocTypeAssertions = false)
    {
        var flags = excludeJSDocTypeAssertions ?
            OuterExpressionKinds.Parentheses | OuterExpressionKinds.ExcludeJSDocTypeAssertion :
            OuterExpressionKinds.Parentheses;
        return FactoryUtilities.SkipOuterExpressions(node, flags);
    }

    /// <summary>`(node.templateFlags || 0) &amp; TokenFlags.ContainsInvalidEscape` of a TemplateLiteralToken (the four kinds that have `templateFlags`).</summary>
    private static bool ContainsInvalidEscapeFlag(Node node)
    {
        TokenFlags templateFlags;
        switch (node)
        {
            case NoSubstitutionTemplateLiteral noSubstitutionTemplateLiteral:
                templateFlags = noSubstitutionTemplateLiteral.TemplateFlags;
                break;
            case TemplateHead templateHead:
                templateFlags = templateHead.TemplateFlags;
                break;
            case TemplateMiddle templateMiddle:
                templateFlags = templateMiddle.TemplateFlags;
                break;
            case TemplateTail templateTail:
                templateFlags = templateTail.TemplateFlags;
                break;
            default:
                templateFlags = TokenFlags.None;
                break;
        }
        return (templateFlags & TokenFlags.ContainsInvalidEscape) != 0;
    }

    internal static bool HasInvalidEscape(Node? template)
    {
        return template != null && (NodeTests.IsNoSubstitutionTemplateLiteral(template)
            ? ContainsInvalidEscapeFlag(template)
            : (ContainsInvalidEscapeFlag(((TemplateExpression)template).Head) || Core.Some(((TemplateExpression)template).TemplateSpans, span => ContainsInvalidEscapeFlag(((TemplateSpan)span).Literal))));
    }

    internal static bool IsThisIdentifier(Node? node)
    {
        return node != null && node.Kind == SyntaxKind.Identifier && IdentifierIsThisKeyword((Identifier)node);
    }

    internal static bool IdentifierIsThisKeyword(Identifier id)
    {
        return (id.EscapedText as string) == "this";
    }

    /// <summary>Determines whether a node is a property or element access expression for `super`.</summary>
    internal static bool IsSuperProperty(Node node)
    {
        var kind = node.Kind;
        return (kind == SyntaxKind.PropertyAccessExpression || kind == SyntaxKind.ElementAccessExpression)
            && GetExpressionOfAccessExpression(node).Kind == SyntaxKind.SuperKeyword;
    }

    internal static bool IsInJSFile(Node? node)
    {
        return node != null && (node.Flags & NodeFlags.JavaScriptFile) != 0;
    }

    internal static bool IsAccessExpression(Node node)
    {
        return node.Kind == SyntaxKind.PropertyAccessExpression || node.Kind == SyntaxKind.ElementAccessExpression;
    }

    /// <summary>`.expression` of a PropertyAccessExpression or an ElementAccessExpression (TypeScript's AccessExpression).</summary>
    private static Node GetExpressionOfAccessExpression(Node node)
    {
        return node is PropertyAccessExpression propertyAccess ? propertyAccess.Expression : ((ElementAccessExpression)node).Expression;
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

    // utilities.ts: `objectAllocator` (the table of node constructors) is not ported: the parser creates nodes
    // with `NodeAllocator.Create(kind)`.
    // utilities.ts: `localizedDiagnosticMessages` is never set here (no localization), so getLocaleSpecificMessage
    // always yields `message.message`.

    internal static string FormatStringFromArgs(string text, object?[] args)
    {
        // text.replace(/\{(\d+)\}/g, (_match, index) => "" + Debug.checkDefined(args[+index]))
        StringBuilder? result = null;
        var copiedUpTo = 0;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '{')
            {
                i++;
                continue;
            }
            var digitsStart = i + 1;
            var digitsEnd = digitsStart;
            long index = 0;
            while (digitsEnd < text.Length && text[digitsEnd] >= '0' && text[digitsEnd] <= '9')
            {
                // Saturating: an index beyond the arguments is undefined whatever its size.
                index = Math.Min((index * 10) + (text[digitsEnd] - '0'), int.MaxValue);
                digitsEnd++;
            }
            if (digitsEnd == digitsStart || digitsEnd >= text.Length || text[digitsEnd] != '}')
            {
                i++;
                continue;
            }
            result ??= new StringBuilder(text.Length);
            result.Append(text, copiedUpTo, i - copiedUpTo);
            var argument = Debug.CheckDefined(index < args.Length ? args[(int)index] : null);
            result.Append(Debug.Stringify(argument));
            i = digitsEnd + 1;
            copiedUpTo = i;
        }
        if (result == null)
        {
            return text;
        }
        result.Append(text, copiedUpTo, text.Length - copiedUpTo);
        return result.ToString();
    }

    internal static string GetLocaleSpecificMessage(DiagnosticMessage message)
    {
        return message.Message;
    }

    internal static DiagnosticWithDetachedLocation CreateDetachedDiagnostic(string fileName, string sourceText, int start, int length, DiagnosticMessage message, params object?[] args)
    {
        if ((start + length) > sourceText.Length)
        {
            length = sourceText.Length - start;
        }

        AssertDiagnosticLocation(sourceText, start, length);
        var text = GetLocaleSpecificMessage(message);

        if (Core.Some(args))
        {
            text = FormatStringFromArgs(text, args);
        }

        // `file: undefined` is the absence of a File member on DiagnosticWithDetachedLocation. TypeScript stores
        // message.reportsUnnecessary as it is (`true` or undefined).
        return new DiagnosticWithDetachedLocation
        {
            Start = start,
            Length = length,

            MessageText = text,
            Category = message.Category,
            Code = message.Code,
            ReportsUnnecessary = message.ReportsUnnecessary ? true : null,
            FileName = fileName,
        };
    }

    /// <summary>
    /// utilities.ts isDiagnosticWithDetachedLocation(diagnostic: DiagnosticRelatedInformation | DiagnosticWithDetachedLocation).
    /// A DiagnosticWithDetachedLocation is not a DiagnosticRelatedInformation in the C# classes; a detached
    /// related information is the subclass DiagnosticRelatedInformationWithDetachedLocation.
    /// </summary>
    private static bool IsDiagnosticWithDetachedLocation(DiagnosticRelatedInformation diagnostic)
    {
        return diagnostic.File == null
            && diagnostic.Start != null
            && diagnostic.Length != null
            && diagnostic is DiagnosticRelatedInformationWithDetachedLocation;
    }

    private static DiagnosticWithLocation AttachFileToDiagnostic(DiagnosticWithDetachedLocation diagnostic, SourceFile file)
    {
        var fileName = string.IsNullOrEmpty(file.FileName) ? "" : file.FileName;
        var length = file.Text.Length;
        Debug.AssertEqual(diagnostic.FileName, fileName);
        Debug.AssertLessThanOrEqual(diagnostic.Start, length);
        Debug.AssertLessThanOrEqual(diagnostic.Start + diagnostic.Length, length);
        var diagnosticWithLocation = new DiagnosticWithLocation
        {
            File = file,
            Start = diagnostic.Start,
            Length = diagnostic.Length,
            MessageText = diagnostic.MessageText,
            Category = diagnostic.Category,
            Code = diagnostic.Code,
            ReportsUnnecessary = diagnostic.ReportsUnnecessary,
        };
        if (diagnostic.RelatedInformation != null)
        {
            diagnosticWithLocation.RelatedInformation = [];
            foreach (var related in diagnostic.RelatedInformation)
            {
                if (IsDiagnosticWithDetachedLocation(related) && ((DiagnosticRelatedInformationWithDetachedLocation)related).FileName == fileName)
                {
                    var relatedStart = related.Start!.Value;
                    var relatedLength = related.Length!.Value;
                    Debug.AssertLessThanOrEqual(relatedStart, length);
                    Debug.AssertLessThanOrEqual(relatedStart + relatedLength, length);
                    var attached = AttachFileToDiagnostic(
                        new DiagnosticWithDetachedLocation
                        {
                            FileName = ((DiagnosticRelatedInformationWithDetachedLocation)related).FileName,
                            Start = relatedStart,
                            Length = relatedLength,
                            MessageText = related.MessageText,
                            Category = related.Category,
                            Code = related.Code,
                        },
                        file);
                    // TypeScript pushes the attached diagnostic itself; as related information it is these members.
                    diagnosticWithLocation.RelatedInformation.Add(new DiagnosticRelatedInformation
                    {
                        Category = attached.Category,
                        Code = attached.Code,
                        File = attached.File,
                        Start = attached.Start,
                        Length = attached.Length,
                        MessageText = attached.MessageText,
                    });
                }
                else
                {
                    diagnosticWithLocation.RelatedInformation.Add(related);
                }
            }
        }
        return diagnosticWithLocation;
    }

    internal static List<DiagnosticWithLocation> AttachFileToDiagnostics(List<DiagnosticWithDetachedLocation> diagnostics, SourceFile file)
    {
        var diagnosticsWithLocation = new List<DiagnosticWithLocation>();
        foreach (var diagnostic in diagnostics)
        {
            diagnosticsWithLocation.Add(AttachFileToDiagnostic(diagnostic, file));
        }
        return diagnosticsWithLocation;
    }

    internal static DiagnosticWithLocation CreateFileDiagnostic(SourceFile file, int start, int length, DiagnosticMessage message, params object?[] args)
    {
        AssertDiagnosticLocation(file.Text, start, length);

        var text = GetLocaleSpecificMessage(message);

        if (Core.Some(args))
        {
            text = FormatStringFromArgs(text, args);
        }

        return new DiagnosticWithLocation
        {
            File = file,
            Start = start,
            Length = length,

            MessageText = text,
            Category = message.Category,
            Code = message.Code,
            ReportsUnnecessary = message.ReportsUnnecessary ? true : null,
            ReportsDeprecated = message.ReportsDeprecated ? true : null,
        };
    }

    internal static T AddRelatedInfo<T>(T diagnostic, params DiagnosticRelatedInformation[] relatedInformation)
        where T : Diagnostic
    {
        if (relatedInformation.Length == 0)
        {
            return diagnostic;
        }
        diagnostic.RelatedInformation ??= [];
        // TypeScript asserts here that relatedInformation is not the shared `emptyArray` singleton; the C# list
        // is always a fresh one.
        diagnostic.RelatedInformation.AddRange(relatedInformation);
        return diagnostic;
    }

    /// <summary>
    /// addRelatedInfo with a DiagnosticWithDetachedLocation as the related information (the parser passes
    /// createDetachedDiagnostic's result): it is stored as a DiagnosticRelatedInformationWithDetachedLocation.
    /// </summary>
    internal static T AddRelatedInfo<T>(T diagnostic, DiagnosticWithDetachedLocation relatedInformation)
        where T : Diagnostic
    {
        return AddRelatedInfo(
            diagnostic,
            new DiagnosticRelatedInformationWithDetachedLocation
            {
                Category = relatedInformation.Category,
                Code = relatedInformation.Code,
                File = null,
                Start = relatedInformation.Start,
                Length = relatedInformation.Length,
                MessageText = relatedInformation.MessageText,
                FileName = relatedInformation.FileName,
            });
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
        if (scriptKind != null && scriptKind.Value != ScriptKind.Unknown)
        {
            return scriptKind.Value;
        }
        var fromFileName = GetScriptKindFromFileName(fileName);
        return fromFileName != ScriptKind.Unknown ? fromFileName : ScriptKind.TS;
    }

    internal static ScriptKind GetScriptKindFromFileName(string fileName)
    {
        // fileName.substr(fileName.lastIndexOf(".")): without a ".", substr(-1) is the last character.
        var dotIndex = fileName.LastIndexOf('.');
        var ext = dotIndex >= 0 ? fileName.Substring(dotIndex) : fileName.Length > 0 ? fileName.Substring(fileName.Length - 1) : "";
        // ext.toLowerCase() in a switch over lower-case extensions: the extensions contain only ASCII letters that
        // no other character lower-cases to, so an ordinal ignore-case comparison selects the same case.
        if (string.Equals(ext, Extension.Js, StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, Extension.Cjs, StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, Extension.Mjs, StringComparison.OrdinalIgnoreCase))
        {
            return ScriptKind.JS;
        }
        if (string.Equals(ext, Extension.Jsx, StringComparison.OrdinalIgnoreCase))
        {
            return ScriptKind.JSX;
        }
        if (string.Equals(ext, Extension.Ts, StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, Extension.Cts, StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, Extension.Mts, StringComparison.OrdinalIgnoreCase))
        {
            return ScriptKind.TS;
        }
        if (string.Equals(ext, Extension.Tsx, StringComparison.OrdinalIgnoreCase))
        {
            return ScriptKind.TSX;
        }
        if (string.Equals(ext, Extension.Json, StringComparison.OrdinalIgnoreCase))
        {
            return ScriptKind.JSON;
        }
        return ScriptKind.Unknown;
    }

    /// <summary>Bypasses immutability and directly sets the `parent` property of a `Node`.</summary>
    [return: NotNullIfNotNull(nameof(child))]
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
    [return: NotNullIfNotNull(nameof(rootNode))]
    internal static T? SetParentRecursive<T>(T? rootNode, bool incremental)
        where T : Node
    {
        if (rootNode == null)
        {
            return rootNode;
        }
        Func<Node, Node, object?> callback;
        if (UtilitiesPublic.IsJSDocNode(rootNode))
        {
            callback = BindParentToChildIgnoringJSDoc;
        }
        else
        {
            callback = BindParentToChild;
        }
        Parser.ForEachChildRecursively<object>(rootNode, callback);
        return rootNode;

        // The callbacks return "skip" or undefined (null), the protocol of Parser.ForEachChildRecursively.
        object? BindParentToChildIgnoringJSDoc(Node child, Node parent)
        {
            if (incremental && child.Parent == parent)
            {
                return "skip";
            }
            SetParent(child, parent);
            return null;
        }

        void BindJSDoc(Node child)
        {
            if (UtilitiesPublic.HasJSDocNodes(child))
            {
                foreach (var doc in JSDocContainers.GetJsDoc(child)!)
                {
                    BindParentToChildIgnoringJSDoc(doc, child);
                    Parser.ForEachChildRecursively<object>(doc, BindParentToChildIgnoringJSDoc);
                }
            }
        }

        object? BindParentToChild(Node child, Node parent)
        {
            var result = BindParentToChildIgnoringJSDoc(child, parent);
            if (result != null)
            {
                return result;
            }
            BindJSDoc(child);
            return null;
        }
    }

    internal static string GetEscapedTextOfJsxNamespacedName(JsxNamespacedName node)
    {
        return (node.Namespace.EscapedText as string) + ":" + UtilitiesPublic.IdText(node.Name);
    }

    internal static string GetTextOfJsxNamespacedName(JsxNamespacedName node)
    {
        return UtilitiesPublic.IdText(node.Namespace) + ":" + UtilitiesPublic.IdText(node.Name);
    }
}
