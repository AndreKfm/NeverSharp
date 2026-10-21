// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go and internal/core/text.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// types.ts: an object literal `{ pos, end }` typed as TextRange (TypeScript creates such ranges inline,
/// e.g. `range: { pos: lineStart, end: pos }` in scanner.ts; C# needs a class for them).
/// </summary>
internal sealed class TextRangeLiteral : TextRange
{
    public int Pos { get; set; }

    public int End { get; set; }
}

/// <summary>types.ts: CommentRange extends TextRange (`hasTrailingNewLine?: boolean; kind: CommentKind`).</summary>
internal sealed class CommentRange : TextRange
{
    public int Pos { get; set; }

    public int End { get; set; }

    public bool HasTrailingNewLine { get; set; }

    // TypeScript: CommentKind = SyntaxKind.SingleLineCommentTrivia | SyntaxKind.MultiLineCommentTrivia.
    public SyntaxKind Kind { get; set; }
}

/// <summary>types.ts: CommentDirective.</summary>
internal sealed class CommentDirective
{
    public TextRange Range { get; set; } = null!;

    public CommentDirectiveType Type { get; set; }
}

/// <summary>types.ts: LineAndCharacter.</summary>
internal sealed class LineAndCharacter
{
    /// <summary>0-based.</summary>
    public int Line { get; set; }

    /// <summary>
    /// 0-based. This value denotes the character position in line and is different from the 'column' because of tab characters.
    /// </summary>
    public int Character { get; set; }
}

/// <summary>types.ts: SourceFileLike (the members the scanner.ts units use).</summary>
internal interface SourceFileLike
{
    string Text { get; }

    /// <summary>TypeScript: `lineMap?: readonly number[]` (the cache that getLineStarts fills).</summary>
    IReadOnlyList<int>? LineMap { get; set; }
}

/// <summary>types.ts (the file-level declarations the scanner needs).</summary>
internal static partial class Types
{
    /// <summary>
    /// Indicates the minimum `ScriptTarget` (inclusive) after which a specific language feature is no longer transpiled.
    /// (TypeScript: `const LanguageFeatureMinimumTarget: Record&lt;LanugageFeatures, ScriptTarget&gt;`.)
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
