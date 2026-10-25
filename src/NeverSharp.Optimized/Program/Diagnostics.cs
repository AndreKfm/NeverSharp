// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts (DiagnosticMessageChain) and src/compiler/program.ts / utilities.ts
// (chainDiagnosticMessages, createCompilerDiagnosticFromMessageChain, flattenDiagnosticMessageText)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/diagnostic.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; diagnostics of the program layer and the checker carry their source file
// (Track O's Node handles are per file, so a Diagnostic's File node alone does not name the file in a program).

using System.Text;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>types.ts: DiagnosticMessageChain.</summary>
internal sealed class DiagnosticMessageChain
{
    public string MessageText = null!;
    public DiagnosticCategory Category;
    public int Code;
    public List<DiagnosticMessageChain>? Next;

    /// <summary>types.ts DiagnosticMessageChain.canonicalHead (the head a suppressed relation error would have had).</summary>
    public (int Code, string MessageText)? CanonicalHead;
}

/// <summary>
/// A diagnostic of the program layer or the checker: types.ts' Diagnostic with its source file (null: a global or
/// options diagnostic, TypeScript's `file: undefined`) and its message as a string or a chain (TypeScript:
/// <c>messageText: string | DiagnosticMessageChain</c>; <see cref="DiagnosticRelatedInformation.MessageText"/> holds
/// the string when <see cref="Chain"/> is null).
/// </summary>
internal sealed class ProgramDiagnostic : Diagnostic
{
    public SourceFile? SourceFile;
    public DiagnosticMessageChain? Chain;

    /// <summary>types.ts Diagnostic.canonicalHead (used to compare and deduplicate diagnostics).</summary>
    public (int Code, string MessageText)? CanonicalHead;

    /// <summary>A diagnostic of <paramref name="file"/> made by the parser or the binder (same members, its file named).</summary>
    internal static ProgramDiagnostic FromFile(SourceFile file, Diagnostic d) => new()
    {
        SourceFile = file,
        File = file.Root,
        StartOrUndefined = d.StartOrUndefined,
        LengthOrUndefined = d.LengthOrUndefined,
        Code = d.Code,
        Category = d.Category,
        MessageText = d.MessageText,
        ReportsUnnecessary = d.ReportsUnnecessary,
        ReportsDeprecated = d.ReportsDeprecated,
        Source = d.Source,
        RelatedInformation = d.RelatedInformation is null ? null : RelatedOf(file, d.RelatedInformation),
    };

    private static List<DiagnosticRelatedInformation> RelatedOf(SourceFile file, List<DiagnosticRelatedInformation> related)
    {
        var result = new List<DiagnosticRelatedInformation>(related.Count);
        foreach (var r in related)
        {
            result.Add(r is ProgramRelatedInformation ? r : new ProgramRelatedInformation
            {
                SourceFile = r.File.IsNone ? null : file,
                File = r.File,
                StartOrUndefined = r.StartOrUndefined,
                LengthOrUndefined = r.LengthOrUndefined,
                Code = r.Code,
                Category = r.Category,
                MessageText = r.MessageText,
            });
        }
        return result;
    }
}

/// <summary>types.ts: DiagnosticRelatedInformation with its source file and an optional message chain.</summary>
internal sealed class ProgramRelatedInformation : DiagnosticRelatedInformation
{
    public SourceFile? SourceFile;
    public DiagnosticMessageChain? Chain;
}

/// <summary>Creation and flattening of chained diagnostics (utilities.ts, program.ts).</summary>
internal static class ProgramDiagnostics
{
    /// <summary>utilities.ts chainDiagnosticMessages.</summary>
    internal static DiagnosticMessageChain ChainDiagnosticMessages(List<DiagnosticMessageChain>? details, DiagnosticMessage message, params object?[] args)
    {
        var text = Utilities.GetLocaleSpecificMessage(message);
        if (args.Length > 0)
        {
            text = Utilities.FormatStringFromArgs(text, args);
        }
        return new DiagnosticMessageChain { MessageText = text, Category = message.Category, Code = message.Code, Next = details };
    }

    /// <summary>utilities.ts createCompilerDiagnosticFromMessageChain (no file, no location).</summary>
    internal static ProgramDiagnostic CreateCompilerDiagnosticFromMessageChain(DiagnosticMessageChain chain) => new()
    {
        Code = chain.Code,
        Category = chain.Category,
        MessageText = chain.MessageText,
        Chain = chain.Next is null ? null : chain,
    };

    /// <summary>utilities.ts flattenDiagnosticMessageText for a diagnostic's message (a string or a chain).</summary>
    internal static string FlattenDiagnosticMessageText(DiagnosticRelatedInformation d, string newLine)
    {
        var chain = d switch
        {
            ProgramDiagnostic p => p.Chain,
            ProgramRelatedInformation r => r.Chain,
            _ => null,
        };
        if (chain is null)
        {
            return d.MessageText;
        }
        var result = new StringBuilder();
        Flatten(chain, newLine, 0, result);
        return result.ToString();
    }

    private static void Flatten(DiagnosticMessageChain diag, string newLine, int indent, StringBuilder result)
    {
        if (indent != 0)
        {
            result.Append(newLine);
            for (var i = 0; i < indent; i++)
            {
                result.Append("  ");
            }
        }
        result.Append(diag.MessageText);
        indent++;
        if (diag.Next is not null)
        {
            foreach (var kid in diag.Next)
            {
                Flatten(kid, newLine, indent, result);
            }
        }
    }
}
