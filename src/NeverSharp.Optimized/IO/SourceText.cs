// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's file reading (step O1 seam), with the semantics of ts.sys.readFile
// (sys.ts): byte order marks decide the encoding (UTF-16 BE, UTF-16 LE, UTF-8), UTF-8 otherwise.
//
// The O1 variants replace this implementation (reading, decoding, where the text lives); the contract for the
// tools: SourceText.Read(path) returns the file's text, Chars exposes it as UTF-16 code units, and
// Parser.ParseFile reads through it. The reader check (tools/NeverSharp.ReaderCheck) compares Chars with the
// sys.ts semantics on every corpus file.

using System.Text;

namespace NeverSharp.Optimized;

/// <summary>The text of a source file as Track O reads it.</summary>
internal readonly struct SourceText
{
    private readonly string text;

    private SourceText(string text) => this.text = text;

    /// <summary>The file's text as UTF-16 code units.</summary>
    internal ReadOnlySpan<char> Chars => text;

    internal int Length => text.Length;

    /// <summary>The text as the string the parser takes (the seam's baseline keeps a string).</summary>
    internal string Text => text;

    /// <summary>sys.ts readFile: byte order marks decide the encoding, UTF-8 otherwise.</summary>
    internal static SourceText Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return new SourceText(Encoding.BigEndianUnicode.GetString(bytes, 2, (bytes.Length - 2) & ~1));
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return new SourceText(Encoding.Unicode.GetString(bytes, 2, (bytes.Length - 2) & ~1));
        }
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new SourceText(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        }
        return new SourceText(Encoding.UTF8.GetString(bytes));
    }
}
