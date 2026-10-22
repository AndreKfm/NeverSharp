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

namespace NeverSharp.Optimized;

/// <summary>types.ts: DiagnosticRelatedInformation.</summary>
internal class DiagnosticRelatedInformation
{
    public DiagnosticCategory Category;
    public int Code;
    public Node File;
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public int Start
    {
        get => StartOrUndefined!.Value;
        set => StartOrUndefined = value;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public int Start
    {
        get => StartOrUndefined!.Value;
        set => StartOrUndefined = value;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
/// The result of Parser.CreateSourceFile in Track O: the SourceFile node (types.ts: SourceFile) and the node store
/// of its parse. The SourceFile node's members are read through the store (Generated/NodeStore.g.cs, including the
/// members Track F declares by hand: diagnostics, references, pragmas); the ones the tools need are exposed here.
/// </summary>
internal sealed class SourceFile
{
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SourceFile(NodeStore store, Node root)
    {
        Store = store;
        Root = root;
    }

    internal NodeStore Store { get; }

    /// <summary>The SourceFile node.</summary>
    internal Node Root { get; }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int NodeCount => Store.NodeCount(Root);

    /// <summary>Length of the source text in UTF-16 code units (the parse-only driver reports it).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal int TextLength => Store.Texts.Source.Length;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal List<DiagnosticWithLocation> ParseDiagnostics => Store.ParseDiagnostics(Root);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal List<DiagnosticWithLocation>? JsDocDiagnostics => Store.JsDocDiagnostics(Root);
}

/// <summary>A SourceFile node as types.ts' SourceFileLike (text and line map read and written through the store).</summary>
internal sealed class SourceFileNodeLike : ISourceFileLike
{
    private readonly NodeStore store;
    private readonly Node sourceFile;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SourceFileNodeLike(NodeStore store, Node sourceFile)
    {
        this.store = store;
        this.sourceFile = sourceFile;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public string Text => store.SourceFileText(sourceFile);

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public IReadOnlyList<int>? LineMap
    {
        get => store.LineMap(sourceFile);
        set => store.SetLineMap(sourceFile, value);
    }
}
