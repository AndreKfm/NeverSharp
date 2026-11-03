// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts (escapeString, escapeNonAsciiString, escapeJsxAttributeString,
// escapeTemplateSubstitution, getLiteralText, canUseOriginalText, isPinnedComment, emitDetachedComments, emitComments,
// emitNewLineBeforeLeadingComments, writeCommentRange, writeTrimmedCurrentLine, calculateIndent, entityNameToString,
// getTextOfJsxNamespacedName) and src/compiler/utilitiesPublic.ts (getTextOfJSDocComment, formatJSDocLink)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/printer/utilities.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; the regular expressions of the escape functions are written as loops.

using System.Globalization;
using System.Text;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>The helpers of utilities.ts and utilitiesPublic.ts the printer uses.</summary>
internal static class PrinterUtilities
{
    // escapedCharsMap (the "\r\n" entry is handled by the backtick case of EscapeString).
    private static string? EscapedChar(char c) => c switch
    {
        '\t' => "\\t",
        '\v' => "\\v",
        '\f' => "\\f",
        '\b' => "\\b",
        '\r' => "\\r",
        '\n' => "\\n",
        '\\' => "\\\\",
        '"' => "\\\"",
        '\'' => "\\'",
        '`' => "\\`",
        LineSeparator => "\\u2028", // lineSeparator
        ParagraphSeparator => "\\u2029", // paragraphSeparator
        NextLine => "\\u0085", // nextLine
        _ => null,
    };

    private static string EncodeUtf16EscapeSequence(int charCode)
    {
        return "\\u" + charCode.ToString("X4", CultureInfo.InvariantCulture);
    }

    // doubleQuoteEscapedCharsRegExp: backslash, double quote, U+0000 to U+001F, U+2028, U+2029, U+0085;
    // singleQuoteEscapedCharsRegExp: the same with single quote for double quote;
    // backtickQuoteEscapedCharsRegExp: CR LF, backslash, backtick, U+0000 to U+001F except LF, U+2028, U+2029, U+0085.
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;
    private const char NextLine = (char)0x0085;

    private static bool IsEscaped(char c, char quoteChar)
    {
        if (c == '\\' || c == LineSeparator || c == ParagraphSeparator || c == NextLine)
        {
            return true;
        }
        if (c <= '\u001f')
        {
            return quoteChar != '`' || c != '\n';
        }
        return c == quoteChar;
    }

    /// <summary>
    /// Based heavily on the abstract 'Quote'/'QuoteJSONString' operation from ECMA-262 (24.3.2.2),
    /// but augmented for a few select characters (e.g. lineSeparator, paragraphSeparator, nextLine)
    /// Note that this doesn't actually wrap the input in double quotes.
    /// </summary>
    internal static string EscapeString(string s, char quoteChar = '"')
    {
        StringBuilder? builder = null;
        var copied = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (!IsEscaped(c, quoteChar))
            {
                continue;
            }
            builder ??= new StringBuilder(s.Length + 8);
            builder.Append(s, copied, i - copied);
            if (quoteChar == '`' && c == '\r' && i + 1 < s.Length && s[i + 1] == '\n')
            {
                // special case for CRLFs in backticks
                builder.Append("\\r\\n");
                i++;
            }
            else if (c == '\0')
            {
                var lookAhead = i + 1 < s.Length ? s[i + 1] : '\0';
                // If the null character is followed by digits, print as a hex escape to prevent the result from parsing as an octal (which is forbidden in strict mode)
                // Otherwise, keep printing a literal \0 for the null character
                builder.Append(lookAhead is >= '0' and <= '9' ? "\\x00" : "\\0");
            }
            else
            {
                builder.Append(EscapedChar(c) ?? EncodeUtf16EscapeSequence(c));
            }
            copied = i + 1;
        }
        if (builder is null)
        {
            return s;
        }
        builder.Append(s, copied, s.Length - copied);
        return builder.ToString();
    }

    internal static string EscapeNonAsciiString(string s, char quoteChar = '"')
    {
        s = EscapeString(s, quoteChar);
        // Replace non-ASCII characters with '\uNNNN' escapes if any exist.
        // Otherwise just return the original string.
        StringBuilder? builder = null;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c > '\u007f')
            {
                if (builder is null)
                {
                    builder = new StringBuilder(s.Length + 16);
                    builder.Append(s, 0, i);
                }
                builder.Append(EncodeUtf16EscapeSequence(c));
            }
            else
            {
                builder?.Append(c);
            }
        }
        return builder is null ? s : builder.ToString();
    }

    // jsxDoubleQuoteEscapedCharsRegExp: double quote, U+0000 to U+001F, U+2028, U+2029, U+0085;
    // jsxSingleQuoteEscapedCharsRegExp: the same with single quote.
    internal static string EscapeJsxAttributeString(string s, char quoteChar = '"')
    {
        StringBuilder? builder = null;
        var copied = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (!(c == quoteChar || c <= (char)0x1f || c == LineSeparator || c == ParagraphSeparator || c == NextLine))
            {
                continue;
            }
            builder ??= new StringBuilder(s.Length + 8);
            builder.Append(s, copied, i - copied);
            builder.Append(c switch
            {
                '\0' => "&#0;",
                '"' => "&quot;",
                '\'' => "&apos;",
                _ => "&#x" + ((int)c).ToString("X", CultureInfo.InvariantCulture) + ";",
            });
            copied = i + 1;
        }
        if (builder is null)
        {
            return s;
        }
        builder.Append(s, copied, s.Length - copied);
        return builder.ToString();
    }

    internal static string EscapeTemplateSubstitution(string str)
    {
        return str.Replace("${", "\\${", StringComparison.Ordinal);
    }

    /// <summary>The text of a literal-like node's `text` member ("" when the node has none).</summary>
    internal static string TextOf(NodeStore store, Node node) => store.Texts.GetString(store.Text(node)) ?? "";

    internal static string GetLiteralText(NodeStore store, Node node, Node sourceFile, GetLiteralTextFlags flags)
    {
        // If we don't need to downlevel and we can reach the original source text using
        // the node's parent reference, then simply get the text as it was originally written.
        if (!sourceFile.IsNone && CanUseOriginalText(store, node, flags))
        {
            return Utilities.GetSourceTextOfNodeFromSourceFile(store, sourceFile, node);
        }

        // If we can't reach the original source text, use the canonical form if it's a number,
        // or a (possibly escaped) quoted form of the original text if it's string-like.
        switch (store.Kind(node))
        {
            case SyntaxKind.StringLiteral:
            {
                var text = TextOf(store, node);
                var quote = store.SingleQuote(node) ? '\'' : '"';
                var escaped = (flags & GetLiteralTextFlags.JsxAttributeEscape) != 0 ? EscapeJsxAttributeString(text, quote) :
                    (flags & GetLiteralTextFlags.NeverAsciiEscape) != 0 || (Utilities.GetEmitFlags(store, node) & EmitFlags.NoAsciiEscaping) != 0 ? EscapeString(text, quote) :
                    EscapeNonAsciiString(text, quote);
                return quote + escaped + quote;
            }
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateHead:
            case SyntaxKind.TemplateMiddle:
            case SyntaxKind.TemplateTail:
            {
                // If a NoSubstitutionTemplateLiteral appears to have a substitution in it, the original text
                // had to include a backslash: `not \${a} substitution`.
                var rawTextRef = store.RawText(node);
                string rawText;
                if (!rawTextRef.IsNone)
                {
                    rawText = store.Texts.GetString(rawTextRef) ?? "";
                }
                else
                {
                    var text = TextOf(store, node);
                    var escaped = (flags & GetLiteralTextFlags.NeverAsciiEscape) != 0 || (Utilities.GetEmitFlags(store, node) & EmitFlags.NoAsciiEscaping) != 0 ?
                        EscapeString(text, '`') : EscapeNonAsciiString(text, '`');
                    rawText = EscapeTemplateSubstitution(escaped);
                }
                return store.Kind(node) switch
                {
                    SyntaxKind.NoSubstitutionTemplateLiteral => "`" + rawText + "`",
                    SyntaxKind.TemplateHead => "`" + rawText + "${",
                    SyntaxKind.TemplateMiddle => "}" + rawText + "${",
                    _ => "}" + rawText + "`",
                };
            }
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
                return TextOf(store, node);
            case SyntaxKind.RegularExpressionLiteral:
            {
                var text = TextOf(store, node);
                if ((flags & GetLiteralTextFlags.TerminateUnterminatedLiterals) != 0 && store.IsUnterminated(node))
                {
                    return text + (text.Length > 0 && text[^1] == '\\' ? " /" : "/");
                }
                return text;
            }
        }

        Debug.Fail("Literal kind '" + store.Kind(node).ToString() + "' not accounted for.");
        return "";
    }

    private static bool CanUseOriginalText(NodeStore store, Node node, GetLiteralTextFlags flags)
    {
        if (Utilities.NodeIsSynthesized(store, node) || store.Parent(node).IsNone || ((flags & GetLiteralTextFlags.TerminateUnterminatedLiterals) != 0 && store.IsUnterminated(node)))
        {
            return false;
        }

        if (store.Kind(node) == SyntaxKind.NumericLiteral)
        {
            var numericLiteralFlags = store.NumericLiteralFlags(node);
            if ((numericLiteralFlags & TokenFlags.IsInvalid) != 0)
            {
                return false;
            }
            if ((numericLiteralFlags & TokenFlags.ContainsSeparator) != 0)
            {
                return (flags & GetLiteralTextFlags.AllowNumericSeparator) != 0;
            }
        }

        return store.Kind(node) != SyntaxKind.BigIntLiteral;
    }

    internal static bool IsPinnedComment(ReadOnlySpan<char> text, int start)
    {
        return start + 2 < text.Length && text[start + 1] == '*' && text[start + 2] == '!';
    }

    /// <summary>utilities.ts: getTextOfJsxNamespacedName.</summary>
    internal static string GetTextOfJsxNamespacedName(NodeStore store, Node node)
    {
        return IdText(store, store.Namespace(node)) + ":" + IdText(store, store.Name(node));
    }

    internal static string IdText(NodeStore store, Node node) => store.Texts.GetString(UtilitiesPublic.IdText(store, node)) ?? "";

    /// <summary>utilitiesPublic.ts: getTextOfJSDocComment.</summary>
    internal static string? GetTextOfJSDocComment(NodeStore store, TextOrList comment)
    {
        if (comment.IsText)
        {
            return store.Texts.GetString(comment.Text) ?? "";
        }
        if (!comment.IsList)
        {
            return null;
        }
        var builder = new StringBuilder();
        foreach (var c in store.Nodes(comment.List))
        {
            builder.Append(store.Kind(c) == SyntaxKind.JSDocText ? TextOf(store, c) : FormatJSDocLink(store, c));
        }
        return builder.ToString();
    }

    private static string FormatJSDocLink(NodeStore store, Node link)
    {
        var kind = store.Kind(link) == SyntaxKind.JSDocLink ? "link"
            : store.Kind(link) == SyntaxKind.JSDocLinkCode ? "linkcode"
            : "linkplain";
        var linkName = store.Name(link);
        var name = !linkName.IsNone ? EntityNameToString(store, linkName) : "";
        var text = TextOf(store, link);
        var space = !linkName.IsNone && (text.Length == 0 || text.StartsWith("://", StringComparison.Ordinal)) ? "" : " ";
        return "{@" + kind + " " + name + space + text + "}";
    }

    /// <summary>utilities.ts: entityNameToString.</summary>
    internal static string EntityNameToString(NodeStore store, Node name)
    {
        switch (store.Kind(name))
        {
            case SyntaxKind.ThisKeyword:
                return "this";
            case SyntaxKind.PrivateIdentifier:
            case SyntaxKind.Identifier:
                return store.End(name) - store.Pos(name) == 0 ? IdText(store, name) :
                    Utilities.GetSourceTextOfNodeFromSourceFile(store, Utilities.GetSourceFileOfNode(store, name), name);
            case SyntaxKind.QualifiedName:
                return EntityNameToString(store, store.Left(name)) + "." + EntityNameToString(store, store.Right(name));
            case SyntaxKind.PropertyAccessExpression:
                return EntityNameToString(store, store.Expression(name)) + "." + EntityNameToString(store, store.Name(name));
            case SyntaxKind.JSDocMemberName:
                return EntityNameToString(store, store.Left(name)) + "#" + EntityNameToString(store, store.Right(name));
            case SyntaxKind.JsxNamespacedName:
                return EntityNameToString(store, store.Namespace(name)) + ":" + EntityNameToString(store, store.Name(name));
            default:
                Debug.Fail("Unexpected node kind " + store.Kind(name).ToString() + ".");
                return "";
        }
    }

    internal static void WriteCommentRange(ReadOnlySpan<char> text, IReadOnlyList<int> lineMap, EmitTextWriter writer, int commentPos, int commentEnd, string newLine)
    {
        if (commentPos + 1 < text.Length && text[commentPos + 1] == '*')
        {
            var firstCommentLine = Scanner.ComputeLineOfPosition(lineMap, commentPos);
            var lineCount = lineMap.Count;
            int? firstCommentLineIndent = null;
            for (int pos = commentPos, currentLine = firstCommentLine; pos < commentEnd; currentLine++)
            {
                var nextLineStart = (currentLine + 1) == lineCount
                    ? text.Length + 1
                    : lineMap[currentLine + 1];

                if (pos != commentPos)
                {
                    // If we are not emitting first line, we need to write the spaces to adjust the alignment
                    firstCommentLineIndent ??= CalculateIndent(text, lineMap[firstCommentLine], commentPos);

                    // These are number of spaces writer is going to write at current indent
                    var currentWriterIndentSpacing = writer.GetIndent() * EmitTextWriter.GetIndentSize();

                    // Number of spaces we want to be writing
                    var spacesToEmit = currentWriterIndentSpacing - firstCommentLineIndent.Value + CalculateIndent(text, pos, nextLineStart);
                    if (spacesToEmit > 0)
                    {
                        var numberOfSingleSpacesToEmit = spacesToEmit % EmitTextWriter.GetIndentSize();
                        var indentSizeSpaceString = EmitTextWriter.GetIndentString((spacesToEmit - numberOfSingleSpacesToEmit) / EmitTextWriter.GetIndentSize());

                        // Write indent size string ( in eg 1: = "", 2: "" , 3: string with 8 spaces 4: string with 12 spaces
                        writer.RawWrite(indentSizeSpaceString);

                        // Emit the single spaces (in eg: 1: 3 spaces, 2: 2 spaces, 3: 1 space, 4: 3 spaces)
                        while (numberOfSingleSpacesToEmit > 0)
                        {
                            writer.RawWrite(" ");
                            numberOfSingleSpacesToEmit--;
                        }
                    }
                    else
                    {
                        // No spaces to emit write empty string
                        writer.RawWrite("");
                    }
                }

                // Write the comment line text
                WriteTrimmedCurrentLine(text, commentEnd, writer, newLine, pos, nextLineStart);

                pos = nextLineStart;
            }
        }
        else
        {
            // Single line comment of style //....
            writer.WriteComment(text[commentPos..commentEnd].ToString());
        }
    }

    private static void WriteTrimmedCurrentLine(ReadOnlySpan<char> text, int commentEnd, EmitTextWriter writer, string newLine, int pos, int nextLineStart)
    {
        var end = Math.Min(commentEnd, nextLineStart - 1);
        var currentLineText = JsTrim(text[pos..end]);
        if (currentLineText.Length > 0)
        {
            // trimmed forward and ending spaces text
            writer.WriteComment(currentLineText);
            if (end != commentEnd)
            {
                writer.WriteLine();
            }
        }
        else
        {
            // Empty string - make sure we write empty line
            writer.RawWrite(newLine);
        }
    }

    // String.prototype.trim: JavaScript's white space and line terminators.
    private static string JsTrim(ReadOnlySpan<char> s)
    {
        var start = 0;
        var end = s.Length;
        while (start < end && Utilities.IsJsWhiteSpaceOrLineTerminator(s[start]))
        {
            start++;
        }
        while (end > start && Utilities.IsJsWhiteSpaceOrLineTerminator(s[end - 1]))
        {
            end--;
        }
        return s[start..end].ToString();
    }

    private static int CalculateIndent(ReadOnlySpan<char> text, int pos, int end)
    {
        var currentLineIndent = 0;
        for (; pos < end && pos < text.Length && Scanner.IsWhiteSpaceSingleLine(text[pos]); pos++)
        {
            if (text[pos] == '\t')
            {
                // Tabs = TabSize = indent size and go to next tabStop
                currentLineIndent += EmitTextWriter.GetIndentSize() - (currentLineIndent % EmitTextWriter.GetIndentSize());
            }
            else
            {
                // Single space
                currentLineIndent++;
            }
        }

        return currentLineIndent;
    }
}
