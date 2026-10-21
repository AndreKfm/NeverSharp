// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/scanner.ts and src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/scanner/scanner.go and internal/core/text.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>scanner.ts: ErrorCallback. `arg0` is the optional argument of the message ("{0}").</summary>
internal delegate void ErrorCallback(DiagnosticMessage message, int length, object? arg0);

/// <summary>types.ts: CommentRange (kind is SingleLineCommentTrivia or MultiLineCommentTrivia).</summary>
internal sealed class CommentRange : TextRange
{
    public int Pos { get; set; }

    public int End { get; set; }

    public bool HasTrailingNewLine { get; set; }

    public SyntaxKind Kind { get; set; }
}

/// <summary>types.ts: LineAndCharacter.</summary>
internal readonly record struct LineAndCharacter(int Line, int Character);

/// <summary>types.ts: CommentDirective.</summary>
internal sealed class CommentDirective
{
    public required TextRange Range { get; init; }

    public required CommentDirectiveType Type { get; init; }
}

/// <summary>A TextRange that is not a node (types.ts: `{ pos, end }` object literals).</summary>
internal sealed class SimpleTextRange : TextRange
{
    public int Pos { get; set; }

    public int End { get; set; }
}

/// <summary>types.ts: SourceFileLike (the part the scanner's line map helpers use).</summary>
internal interface SourceFileLike
{
    string Text { get; }

    IReadOnlyList<int>? LineMap { get; set; }
}

/// <summary>types.ts: the table of minimum language versions of language features.</summary>
internal static partial class Types
{
    /// <summary>
    /// Indicates the minimum `ScriptTarget` (inclusive) after which a specific language feature is no longer transpiled.
    /// TypeScript's `Record&lt;LanugageFeatures, ScriptTarget&gt;` is a constant table of this class.
    /// </summary>
    internal static class LanguageFeatureMinimumTarget
    {
        internal const ScriptTarget Classes = ScriptTarget.ES2015;
        internal const ScriptTarget ForOf = ScriptTarget.ES2015;
        internal const ScriptTarget Generators = ScriptTarget.ES2015;
        internal const ScriptTarget Iteration = ScriptTarget.ES2015;
        internal const ScriptTarget SpreadElements = ScriptTarget.ES2015;
        internal const ScriptTarget RestElements = ScriptTarget.ES2015;
        internal const ScriptTarget TaggedTemplates = ScriptTarget.ES2015;
        internal const ScriptTarget DestructuringAssignment = ScriptTarget.ES2015;
        internal const ScriptTarget BindingPatterns = ScriptTarget.ES2015;
        internal const ScriptTarget ArrowFunctions = ScriptTarget.ES2015;
        internal const ScriptTarget BlockScopedVariables = ScriptTarget.ES2015;
        internal const ScriptTarget ObjectAssign = ScriptTarget.ES2015;
        internal const ScriptTarget RegularExpressionFlagsUnicode = ScriptTarget.ES2015;
        internal const ScriptTarget RegularExpressionFlagsSticky = ScriptTarget.ES2015;
        internal const ScriptTarget Exponentiation = ScriptTarget.ES2016;
        internal const ScriptTarget AsyncFunctions = ScriptTarget.ES2017;
        internal const ScriptTarget ForAwaitOf = ScriptTarget.ES2018;
        internal const ScriptTarget AsyncGenerators = ScriptTarget.ES2018;
        internal const ScriptTarget AsyncIteration = ScriptTarget.ES2018;
        internal const ScriptTarget ObjectSpreadRest = ScriptTarget.ES2018;
        internal const ScriptTarget RegularExpressionFlagsDotAll = ScriptTarget.ES2018;
        internal const ScriptTarget BindinglessCatch = ScriptTarget.ES2019;
        internal const ScriptTarget BigInt = ScriptTarget.ES2020;
        internal const ScriptTarget NullishCoalesce = ScriptTarget.ES2020;
        internal const ScriptTarget OptionalChaining = ScriptTarget.ES2020;
        internal const ScriptTarget LogicalAssignment = ScriptTarget.ES2021;
        internal const ScriptTarget TopLevelAwait = ScriptTarget.ES2022;
        internal const ScriptTarget ClassFields = ScriptTarget.ES2022;
        internal const ScriptTarget PrivateNamesAndClassStaticBlocks = ScriptTarget.ES2022;
        internal const ScriptTarget RegularExpressionFlagsHasIndices = ScriptTarget.ES2022;
        internal const ScriptTarget ShebangComments = ScriptTarget.ES2023;
        internal const ScriptTarget RegularExpressionFlagsUnicodeSets = ScriptTarget.ES2024;
        internal const ScriptTarget UsingAndAwaitUsing = ScriptTarget.ESNext;
        internal const ScriptTarget ClassAndClassElementDecorators = ScriptTarget.ESNext;
    }
}
