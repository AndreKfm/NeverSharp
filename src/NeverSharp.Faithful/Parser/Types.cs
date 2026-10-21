// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Ported from src/compiler/parser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Ported from src/compiler/factory/baseNodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go and internal/ast/diagnostic.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>parser.ts: CreateSourceFileOptions.</summary>
internal sealed class CreateSourceFileOptions
{
    public ScriptTarget LanguageVersion { get; set; }

    /// <summary>
    /// Controls the format the file is detected as - this can be derived from only the path
    /// and files on disk, but needs to be done with a module resolution cache in scope to be performant.
    /// This is usually `undefined` for compilations that do not have `moduleResolution` values of `node16` or `nodenext`.
    /// (TypeScript: ResolutionMode = ModuleKind.ESNext | ModuleKind.CommonJS | undefined.)
    /// </summary>
    public ModuleKind? ImpliedNodeFormat { get; set; }

    /// <summary>
    /// Controls how module-y-ness is set for the given file. Usually the result of calling
    /// `getSetExternalModuleIndicator` on a valid `CompilerOptions` object. If not present, the default
    /// check specified by `isFileProbablyExternalModule` will be used to set the field.
    /// </summary>
    public Action<SourceFile>? SetExternalModuleIndicator { get; set; }

    public IReadOnlyList<string>? PackageJsonLocations { get; set; }

    /// <summary>TypeScript: PackageJsonInfo (module resolution data, not used by the parser).</summary>
    public object? PackageJsonScope { get; set; }

    public JSDocParsingMode? JsDocParsingMode { get; set; }
}

/// <summary>types.ts: DiagnosticRelatedInformation.</summary>
internal class DiagnosticRelatedInformation
{
    public DiagnosticCategory Category;
    public int Code;
    public SourceFile? File;

    // TypeScript: `start: number | undefined`, `length: number | undefined`; the parser always sets both.
    public int Start;
    public int Length;

    /// <summary>TypeScript: string | DiagnosticMessageChain (the parser only creates strings).</summary>
    public object MessageText = "";
}

/// <summary>types.ts: Diagnostic.</summary>
internal class Diagnostic : DiagnosticRelatedInformation
{
    /// <summary>May store more in future. For now, this will simply be `true` to indicate when a diagnostic is an unused-identifier diagnostic.</summary>
    public bool ReportsUnnecessary;
    public bool ReportsDeprecated;
    public string? Source;
    public List<DiagnosticRelatedInformation>? RelatedInformation;
    public string? SkippedOn;
}

/// <summary>types.ts: DiagnosticWithLocation.</summary>
internal sealed class DiagnosticWithLocation : Diagnostic
{
}

/// <summary>types.ts: DiagnosticWithDetachedLocation.</summary>
internal sealed class DiagnosticWithDetachedLocation : Diagnostic
{
    public string FileName = "";
}

/// <summary>types.ts: FileReference.</summary>
internal sealed class FileReference : TextRange
{
    public int Pos { get; set; }

    public int End { get; set; }

    public string FileName { get; set; } = "";

    /// <summary>TypeScript: ResolutionMode.</summary>
    public ModuleKind? ResolutionMode { get; set; }

    public bool? Preserve { get; set; }
}

/// <summary>types.ts: CheckJsDirective.</summary>
internal sealed class CheckJsDirective : TextRange
{
    public int Pos { get; set; }

    public int End { get; set; }

    public bool Enabled { get; set; }
}

/// <summary>types.ts: AmdDependency.</summary>
internal sealed class AmdDependency
{
    public string Path { get; set; } = "";

    public string? Name { get; set; }
}

/// <summary>types.ts: a captured pragma argument (`{ value: string; pos: number; end: number; }`).</summary>
internal sealed class PragmaArgumentSpan
{
    public string Value { get; set; } = "";

    public int Pos { get; set; }

    public int End { get; set; }
}

/// <summary>
/// types.ts: PragmaPseudoMap[K], `{ arguments: PragmaArgumentType&lt;K&gt;; range: CommentRange; }`. The arguments
/// map each argument name to a string or (captured span) a PragmaArgumentSpan.
/// </summary>
internal sealed class PragmaPseudoMapValue
{
    public required Dictionary<string, object> Arguments { get; init; }

    public required CommentRange Range { get; init; }
}

/// <summary>types.ts: PragmaPseudoMapEntry, `{ name: K; args: PragmaPseudoMap[K]; }`.</summary>
internal sealed class PragmaPseudoMapEntry
{
    public required string Name { get; init; }

    public required PragmaPseudoMapValue Args { get; init; }
}

/// <summary>factory/baseNodeFactory.ts: BaseNodeFactory.</summary>
internal interface BaseNodeFactory
{
    Node CreateBaseSourceFileNode(SyntaxKind kind);

    Node CreateBaseIdentifierNode(SyntaxKind kind);

    Node CreateBasePrivateIdentifierNode(SyntaxKind kind);

    Node CreateBaseTokenNode(SyntaxKind kind);

    Node CreateBaseNode(SyntaxKind kind);
}

/// <summary>types.ts: the constructors of ObjectAllocator (`new (kind: SyntaxKind, pos: number, end: number) => Node`).</summary>
internal delegate Node NodeConstructor(SyntaxKind kind, int pos, int end);

/// <summary>types.ts: SourceFile, the members the generator leaves to the port.</summary>
public sealed partial class SourceFile : ISourceFileLike
{
    internal IReadOnlyList<AmdDependency> AmdDependencies = [];
    internal IReadOnlyList<FileReference> ReferencedFiles = [];
    internal IReadOnlyList<FileReference> TypeReferenceDirectives = [];
    internal IReadOnlyList<FileReference> LibReferenceDirectives = [];
    internal List<DiagnosticWithLocation> ParseDiagnostics = null!;
    internal List<DiagnosticWithLocation> BindDiagnostics = null!;
    internal List<DiagnosticWithLocation>? BindSuggestionDiagnostics;
    internal List<DiagnosticWithLocation>? JsDocDiagnostics;
    internal List<CommentDirective>? CommentDirectives;
    internal CheckJsDirective? CheckJsDirective;

    /// <summary>
    /// TypeScript: ReadonlyPragmaMap, a Map from pragma name to PragmaPseudoMap[K] or an array of them (insertion
    /// ordered, as a JavaScript Map). Values are PragmaPseudoMapValue or List&lt;PragmaPseudoMapValue&gt;.
    /// </summary>
    internal OrderedDictionary<string, object> Pragmas = null!;

    string ISourceFileLike.Text => Text;

    IReadOnlyList<int>? ISourceFileLike.LineMap { get => LineMap; set => LineMap = value; }
}

/// <summary>parser.ts: MissingList&lt;T&gt; (`interface MissingList&lt;T&gt; extends NodeArray&lt;T&gt; { isMissingList: true; }`).</summary>
public sealed partial class NodeArray
{
    internal bool IsMissingList;
}
