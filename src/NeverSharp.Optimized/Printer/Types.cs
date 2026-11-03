// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts (EmitHint, PrinterOptions, ListFormat) and src/compiler/utilities.ts (GetLiteralTextFlags)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/printer/printer.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; PrinterOptions reduced to the members the Track O printer supports.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>types.ts: EmitHint.</summary>
internal enum EmitHint
{
    SourceFile,              // Emitting a SourceFile
    Expression,              // Emitting an Expression
    IdentifierName,          // Emitting an IdentifierName
    MappedTypeParameter,     // Emitting a TypeParameterDeclaration inside of a MappedTypeNode
    Unspecified,             // Emitting an otherwise unspecified node
    EmbeddedStatement,       // Emitting an embedded statement
    JsxAttributeValue,       // Emitting a JSX attribute value
    ImportTypeNodeAttributes, // Emitting attributes as part of an ImportTypeNode
}

/// <summary>
/// types.ts: PrinterOptions, the members the Track O printer reads. No source maps. Track O's default for
/// <see cref="RemoveComments"/> is true (the type printer's); the emitter passes false (module D0: comment emission).
/// </summary>
internal sealed class PrinterOptions
{
    public bool RemoveComments { get; init; } = true;

    /// <summary>Only JSDoc-style (/** */) and pinned (/*! */) comments are written (the declaration printer).</summary>
    public bool OnlyPrintJsDocStyle { get; init; }

    /// <summary>The declaration printer: emit helpers are never written (TypeScript's noEmitHelpers).</summary>
    public bool NoEmitHelpers { get; init; }

    public NewLineKind? NewLine { get; init; }

    public bool NeverAsciiEscape { get; init; }

    public bool TerminateUnterminatedLiterals { get; init; }

    public ScriptTarget? Target { get; init; }
}

/// <summary>utilities.ts: GetLiteralTextFlags.</summary>
[Flags]
internal enum GetLiteralTextFlags
{
    None = 0,
    NeverAsciiEscape = 1 << 0,
    JsxAttributeEscape = 1 << 1,
    TerminateUnterminatedLiterals = 1 << 2,
    AllowNumericSeparator = 1 << 3,
}

/// <summary>types.ts: ListFormat.</summary>
[Flags]
#pragma warning disable CA1069 // precomputed formats with the same value are part of TypeScript's enum
internal enum ListFormat
{
    None = 0,

    // Line separators
    SingleLine = None,              // Prints the list on a single line (default).
    MultiLine = 1 << 0,             // Prints the list on multiple lines.
    PreserveLines = 1 << 1,         // Prints the list using line preservation if possible.
    LinesMask = SingleLine | MultiLine | PreserveLines,

    // Delimiters
    NotDelimited = None,            // There is no delimiter between list items (default).
    BarDelimited = 1 << 2,          // Each list item is space-and-bar (" |") delimited.
    AmpersandDelimited = 1 << 3,    // Each list item is space-and-ampersand (" &") delimited.
    CommaDelimited = 1 << 4,        // Each list item is comma (",") delimited.
    AsteriskDelimited = 1 << 5,     // Each list item is asterisk ("\n *") delimited, used with JSDoc.
    DelimitersMask = BarDelimited | AmpersandDelimited | CommaDelimited | AsteriskDelimited,

    AllowTrailingComma = 1 << 6,    // Write a trailing comma (",") if present.

    // Whitespace
    Indented = 1 << 7,              // The list should be indented.
    SpaceBetweenBraces = 1 << 8,    // Inserts a space after the opening brace and before the closing brace.
    SpaceBetweenSiblings = 1 << 9,  // Inserts a space between each sibling node.

    // Brackets/Braces
    Braces = 1 << 10,                // The list is surrounded by "{" and "}".
    Parenthesis = 1 << 11,          // The list is surrounded by "(" and ")".
    AngleBrackets = 1 << 12,        // The list is surrounded by "<" and ">".
    SquareBrackets = 1 << 13,       // The list is surrounded by "[" and "]".
    BracketsMask = Braces | Parenthesis | AngleBrackets | SquareBrackets,

    OptionalIfUndefined = 1 << 14,  // Do not emit brackets if the list is undefined.
    OptionalIfEmpty = 1 << 15,      // Do not emit brackets if the list is empty.
    Optional = OptionalIfUndefined | OptionalIfEmpty,

    // Other
    PreferNewLine = 1 << 16,        // Prefer adding a LineTerminator between synthesized nodes.
    NoTrailingNewLine = 1 << 17,    // Do not emit a trailing NewLine for a MultiLine list.
    NoInterveningComments = 1 << 18, // Do not emit comments between each node
    NoSpaceIfEmpty = 1 << 19,       // If the literal is empty, do not add spaces between braces.
    SingleElement = 1 << 20,
    SpaceAfterList = 1 << 21,       // Add space after list

    // Precomputed Formats
    Modifiers = SingleLine | SpaceBetweenSiblings | NoInterveningComments | SpaceAfterList,
    HeritageClauses = SingleLine | SpaceBetweenSiblings,
    SingleLineTypeLiteralMembers = SingleLine | SpaceBetweenBraces | SpaceBetweenSiblings,
    MultiLineTypeLiteralMembers = MultiLine | Indented | OptionalIfEmpty,

    SingleLineTupleTypeElements = CommaDelimited | SpaceBetweenSiblings | SingleLine,
    MultiLineTupleTypeElements = CommaDelimited | Indented | SpaceBetweenSiblings | MultiLine,
    UnionTypeConstituents = BarDelimited | SpaceBetweenSiblings | SingleLine,
    IntersectionTypeConstituents = AmpersandDelimited | SpaceBetweenSiblings | SingleLine,
    ObjectBindingPatternElements = SingleLine | AllowTrailingComma | SpaceBetweenBraces | CommaDelimited | SpaceBetweenSiblings | NoSpaceIfEmpty,
    ArrayBindingPatternElements = SingleLine | AllowTrailingComma | CommaDelimited | SpaceBetweenSiblings | NoSpaceIfEmpty,
    ObjectLiteralExpressionProperties = PreserveLines | CommaDelimited | SpaceBetweenSiblings | SpaceBetweenBraces | Indented | Braces | NoSpaceIfEmpty,
    ImportAttributes = PreserveLines | CommaDelimited | SpaceBetweenSiblings | SpaceBetweenBraces | Indented | Braces | NoSpaceIfEmpty,
    ArrayLiteralExpressionElements = PreserveLines | CommaDelimited | SpaceBetweenSiblings | AllowTrailingComma | Indented | SquareBrackets,
    CommaListElements = CommaDelimited | SpaceBetweenSiblings | SingleLine,
    CallExpressionArguments = CommaDelimited | SpaceBetweenSiblings | SingleLine | Parenthesis,
    NewExpressionArguments = CommaDelimited | SpaceBetweenSiblings | SingleLine | Parenthesis | OptionalIfUndefined,
    TemplateExpressionSpans = SingleLine | NoInterveningComments,
    SingleLineBlockStatements = SpaceBetweenBraces | SpaceBetweenSiblings | SingleLine,
    MultiLineBlockStatements = Indented | MultiLine,
    VariableDeclarationList = CommaDelimited | SpaceBetweenSiblings | SingleLine,
    SingleLineFunctionBodyStatements = SingleLine | SpaceBetweenSiblings | SpaceBetweenBraces,
    MultiLineFunctionBodyStatements = MultiLine,
    ClassHeritageClauses = SingleLine,
    ClassMembers = Indented | MultiLine,
    InterfaceMembers = Indented | MultiLine,
    EnumMembers = CommaDelimited | Indented | MultiLine,
    CaseBlockClauses = Indented | MultiLine,
    NamedImportsOrExportsElements = CommaDelimited | SpaceBetweenSiblings | AllowTrailingComma | SingleLine | SpaceBetweenBraces | NoSpaceIfEmpty,
    JsxElementOrFragmentChildren = SingleLine | NoInterveningComments,
    JsxElementAttributes = SingleLine | SpaceBetweenSiblings | NoInterveningComments,
    CaseOrDefaultClauseStatements = Indented | MultiLine | NoTrailingNewLine | OptionalIfEmpty,
    HeritageClauseTypes = CommaDelimited | SpaceBetweenSiblings | SingleLine,
    SourceFileStatements = MultiLine | NoTrailingNewLine,
    Decorators = MultiLine | Optional | SpaceAfterList,
    TypeArguments = CommaDelimited | SpaceBetweenSiblings | SingleLine | AngleBrackets | Optional,
    TypeParameters = CommaDelimited | SpaceBetweenSiblings | SingleLine | AngleBrackets | Optional,
    Parameters = CommaDelimited | SpaceBetweenSiblings | SingleLine | Parenthesis,
    IndexSignatureParameters = CommaDelimited | SpaceBetweenSiblings | SingleLine | Indented | SquareBrackets,
    JSDocComment = MultiLine | AsteriskDelimited,
}
#pragma warning restore CA1069
