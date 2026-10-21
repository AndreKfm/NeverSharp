// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/diagnostic.go and internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>types.ts: DiagnosticRelatedInformation.</summary>
internal class DiagnosticRelatedInformation
{
    public DiagnosticCategory Category;
    public int Code;
    public SourceFile? File;
    public int? StartOrUndefined;
    public int? LengthOrUndefined;

    // TypeScript: string | DiagnosticMessageChain; the parser creates only strings.
    public string MessageText = null!;
}

/// <summary>types.ts: Diagnostic.</summary>
internal class Diagnostic : DiagnosticRelatedInformation
{
    // TypeScript: `reportsUnnecessary?: {}` (the parser stores the message's boolean).
    public bool ReportsUnnecessary;
    public bool ReportsDeprecated;
    public string? Source;
    public List<DiagnosticRelatedInformation>? RelatedInformation;
}

/// <summary>types.ts: DiagnosticWithLocation (start and length are defined).</summary>
internal sealed class DiagnosticWithLocation : Diagnostic
{
    public int Start
    {
        get => StartOrUndefined!.Value;
        set => StartOrUndefined = value;
    }

    public int Length
    {
        get => LengthOrUndefined!.Value;
        set => LengthOrUndefined = value;
    }
}

/// <summary>types.ts: DiagnosticWithDetachedLocation (no file yet, but its name; start and length are defined).</summary>
internal sealed class DiagnosticWithDetachedLocation : Diagnostic
{
    public string FileName = null!;

    public int Start
    {
        get => StartOrUndefined!.Value;
        set => StartOrUndefined = value;
    }

    public int Length
    {
        get => LengthOrUndefined!.Value;
        set => LengthOrUndefined = value;
    }
}

/// <summary>types.ts: FileReference.</summary>
internal sealed class FileReference : TextRange
{
    public int Pos { get; set; }

    public int End { get; set; }

    public string FileName = null!;

    // TypeScript: ResolutionMode (ModuleKind.ESNext | ModuleKind.CommonJS | undefined).
    public ModuleKind? ResolutionMode;

    public bool? Preserve;
}

/// <summary>types.ts: CheckJsDirective.</summary>
internal sealed class CheckJsDirective : TextRange
{
    public int Pos { get; set; }

    public int End { get; set; }

    public bool Enabled;
}

/// <summary>types.ts: AmdDependency.</summary>
internal sealed class AmdDependency
{
    public string Path = null!;

    public string? Name;
}

/// <summary>
/// types.ts: SourceFile, the members the generator leaves to the port (see the comment above SourceFile in
/// Nodes.g.cs); `pragmas` is declared with the pragma types in Parser.Pragmas.cs.
/// </summary>
public sealed partial class SourceFile : ISourceFileLike
{
    internal IReadOnlyList<AmdDependency> AmdDependencies = null!;
    internal IReadOnlyList<FileReference> ReferencedFiles = null!;
    internal IReadOnlyList<FileReference> TypeReferenceDirectives = null!;
    internal IReadOnlyList<FileReference> LibReferenceDirectives = null!;
    internal List<DiagnosticWithLocation> ParseDiagnostics = null!;
    internal List<DiagnosticWithLocation>? BindDiagnostics;
    internal List<DiagnosticWithLocation>? BindSuggestionDiagnostics;
    internal List<DiagnosticWithLocation>? JsDocDiagnostics;
    internal List<CommentDirective>? CommentDirectives;
    internal CheckJsDirective? CheckJsDirective;

    string ISourceFileLike.Text => Text;

    IReadOnlyList<int>? ISourceFileLike.LineMap
    {
        get => LineMap;
        set => LineMap = value;
    }
}
