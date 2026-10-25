// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts (createTextWriter, getIndentString, getIndentSize, getNewLineCharacter)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/printer/textwriter.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; the output is a StringBuilder, and the line starts of a written string
// are counted while scanning it instead of building computeLineStarts' array.

using System.Text;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// utilities.ts: createTextWriter (types.ts: EmitTextWriter). The variables of its closure are the fields of the instance.
/// Every write kind (keyword, operator, punctuation, ...) writes its text unchanged, as in TypeScript.
/// </summary>
internal sealed class EmitTextWriter
{
    private readonly string newLine;
    private readonly StringBuilder output = new();
    private int indent;
    private bool lineStart;
    private int lineCount;
    private int linePos;
    private bool hasTrailingComment;

    internal EmitTextWriter(string newLine)
    {
        this.newLine = newLine;
        Reset();
    }

    // computeLineStarts(s): the count of line starts after the first and the last line start (line breaks: CR, LF, CR LF,
    // and the non-ASCII line breaks of isLineBreak, U+2028 and U+2029).
    private void UpdateLineCountAndPosFor(string s)
    {
        var breaks = 0;
        var lastLineStart = 0;
        var pos = 0;
        while (pos < s.Length)
        {
            var ch = s[pos];
            pos++;
            switch (ch)
            {
                case '\r':
                    if (pos < s.Length && s[pos] == '\n')
                    {
                        pos++;
                    }
                    breaks++;
                    lastLineStart = pos;
                    break;
                case '\n':
                    breaks++;
                    lastLineStart = pos;
                    break;
                default:
                    if (ch > (char)CharacterCodes.maxAsciiCharacter && Scanner.IsLineBreak(ch))
                    {
                        breaks++;
                        lastLineStart = pos;
                    }
                    break;
            }
        }
        if (breaks > 0)
        {
            lineCount += breaks;
            linePos = output.Length - s.Length + lastLineStart;
            lineStart = linePos - output.Length == 0;
        }
        else
        {
            lineStart = false;
        }
    }

    /// <summary>
    /// Track O (module C2): the second writer of typeWriter.ts' underlining printer (src/harness/typeWriter.ts), kept in
    /// step with this one: every text written here is written there as '^' (while <see cref="Underlining"/>) or spaces.
    /// </summary>
    internal StringBuilder? Underline { get; set; }

    /// <summary>Whether the nodes being written are fully synthetic (see <see cref="Underline"/>).</summary>
    internal bool Underlining { get; set; }

    private void WriteText(string s)
    {
        if (s.Length > 0)
        {
            if (lineStart)
            {
                Underline?.Append(GetIndentString(indent));
                s = GetIndentString(indent) + s;
                Underline?.Append(Underlining ? '^' : ' ', s.Length - GetIndentString(indent).Length);
                lineStart = false;
            }
            else
            {
                Underline?.Append(Underlining ? '^' : ' ', s.Length);
            }
            output.Append(s);
            UpdateLineCountAndPosFor(s);
        }
    }

    internal void Write(string s)
    {
        if (s.Length > 0)
        {
            hasTrailingComment = false;
        }
        WriteText(s);
    }

    internal void WriteComment(string s)
    {
        if (s.Length > 0)
        {
            hasTrailingComment = true;
        }
        WriteText(s);
    }

    private void Reset()
    {
        output.Clear();
        indent = 0;
        lineStart = true;
        lineCount = 0;
        linePos = 0;
        hasTrailingComment = false;
    }

    internal void RawWrite(string s)
    {
        Underline?.Append(Underlining ? '^' : ' ', s.Length);
        output.Append(s);
        UpdateLineCountAndPosFor(s);
        hasTrailingComment = false;
    }

    internal void WriteLiteral(string s)
    {
        if (s.Length > 0)
        {
            Write(s);
        }
    }

    internal void WriteLine(bool force = false)
    {
        if (!lineStart || force)
        {
            Underline?.Append(newLine);
            output.Append(newLine);
            lineCount++;
            linePos = output.Length;
            lineStart = true;
            hasTrailingComment = false;
        }
    }

    internal void IncreaseIndent() => indent++;

    internal void DecreaseIndent() => indent--;

    internal int GetIndent() => indent;

    internal int GetTextPos() => output.Length;

    internal int GetLine() => lineCount;

    internal int GetColumn() => lineStart ? indent * GetIndentSize() : output.Length - linePos;

    internal string GetText() => output.ToString();

    internal bool IsAtStartOfLine() => lineStart;

    internal bool HasTrailingComment() => hasTrailingComment;

    internal bool HasTrailingWhitespace() => output.Length > 0 && Scanner.IsWhiteSpaceLike(output[^1]);

    internal void Clear() => Reset();

    internal void WriteKeyword(string s) => Write(s);

    internal void WriteOperator(string s) => Write(s);

    internal void WriteParameter(string s) => Write(s);

    internal void WriteProperty(string s) => Write(s);

    internal void WritePunctuation(string s) => Write(s);

    internal void WriteSpace(string s) => Write(s);

    internal void WriteStringLiteral(string s) => Write(s);

    internal void WriteSymbol(string s) => Write(s);

    internal void WriteTrailingSemicolon(string s) => Write(s);

    // utilities.ts: indentStrings, getIndentString, getIndentSize. (TypeScript grows one shared cache of indent strings;
    // here the first levels are precomputed, deeper ones built on demand, so that printers on several threads share nothing
    // mutable.)
    private const string SingleLevel = "    ";

    private static readonly string[] IndentStrings = CreateIndentStrings(32);

    private static string[] CreateIndentStrings(int count)
    {
        var strings = new string[count];
        strings[0] = "";
        for (var i = 1; i < count; i++)
        {
            strings[i] = strings[i - 1] + SingleLevel;
        }
        return strings;
    }

    internal static string GetIndentString(int level)
    {
        if (level < IndentStrings.Length)
        {
            return IndentStrings[level];
        }
        var builder = new StringBuilder(level * SingleLevel.Length);
        for (var i = 0; i < level; i++)
        {
            builder.Append(SingleLevel);
        }
        return builder.ToString();
    }

    internal static int GetIndentSize() => SingleLevel.Length;

    /// <summary>utilities.ts: getNewLineCharacter.</summary>
    internal static string GetNewLineCharacter(PrinterOptions options)
    {
        return options.NewLine == NewLineKind.CarriageReturnLineFeed ? "\r\n" : "\n";
    }
}
