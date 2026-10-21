// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/diagnostic.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// types.ts: DiagnosticMessageChain, a linked list of formatted diagnostic messages to be used as part of a
/// multiline message.
/// </summary>
internal sealed class DiagnosticMessageChain
{
    public string MessageText { get; set; } = "";

    public DiagnosticCategory Category { get; set; }

    public int Code { get; set; }

    public List<DiagnosticMessageChain>? Next { get; set; }
}

/// <summary>types.ts: DiagnosticRelatedInformation.</summary>
internal class DiagnosticRelatedInformation
{
    public DiagnosticCategory Category { get; set; }

    public int Code { get; set; }

    public SourceFile? File { get; set; }

    public int? Start { get; set; }

    public int? Length { get; set; }

    /// <summary>`string | DiagnosticMessageChain`.</summary>
    public object MessageText { get; set; } = "";
}

/// <summary>types.ts: Diagnostic. Its location members (`file`, `start`, `length`) live in the derived classes.</summary>
internal class Diagnostic
{
    public DiagnosticCategory Category { get; set; }

    public int Code { get; set; }

    /// <summary>`string | DiagnosticMessageChain`.</summary>
    public object MessageText { get; set; } = "";

    /// <summary>`reportsUnnecessary?: {}`: true when set.</summary>
    public bool? ReportsUnnecessary { get; set; }

    /// <summary>`reportsDeprecated?: {}`: true when set.</summary>
    public bool? ReportsDeprecated { get; set; }

    public string? Source { get; set; }

    public List<DiagnosticRelatedInformation>? RelatedInformation { get; set; }

    /// <summary>`skippedOn?: keyof CompilerOptions`.</summary>
    public string? SkippedOn { get; set; }
}

/// <summary>types.ts: DiagnosticWithLocation (`file`, `start` and `length` are always present).</summary>
internal sealed class DiagnosticWithLocation : Diagnostic
{
    public SourceFile File { get; set; } = null!;

    public int Start { get; set; }

    public int Length { get; set; }
}

/// <summary>types.ts: DiagnosticWithDetachedLocation (`file` is undefined, `fileName` names the file).</summary>
internal sealed class DiagnosticWithDetachedLocation : Diagnostic
{
    public string FileName { get; set; } = "";

    public int Start { get; set; }

    public int Length { get; set; }
}

/// <summary>
/// The members the generator leaves to the port, as listed in the comment above `SourceFile` in Nodes.g.cs
/// (the diagnostics lists; the pragma-related ones are declared with the pragma types).
/// </summary>
public sealed partial class SourceFile
{
    // `parseDiagnostics: DiagnosticWithLocation[]`
    internal List<DiagnosticWithLocation> ParseDiagnostics = null!;

    // `bindDiagnostics: DiagnosticWithLocation[]`
    internal List<DiagnosticWithLocation> BindDiagnostics = null!;

    // `bindSuggestionDiagnostics: DiagnosticWithLocation[] | undefined`
    internal List<DiagnosticWithLocation>? BindSuggestionDiagnostics;

    // `jsDocDiagnostics: DiagnosticWithLocation[] | undefined`
    internal List<DiagnosticWithLocation>? JsDocDiagnostics;
}
