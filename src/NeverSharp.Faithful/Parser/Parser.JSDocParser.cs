// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/parser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/parser/jsdoc.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

namespace NeverSharp.Faithful;

/// <summary>
/// parser.ts: the entry points of `namespace Parser.JSDocParser` that the main grammar calls. JSDocParser is
/// ported by module M5; with JSDocParsingMode.ParseNone the scanner never reports a JSDoc comment, so withJSDoc
/// never calls into it.
/// </summary>
internal sealed partial class Parser
{
    /// <summary>JSDocParser.parseJSDocComment (module M5).</summary>
    private JSDoc? ParseJSDocComment(Node parent, int start, int length)
    {
        throw new NotImplementedException("M5");
    }
}
