// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): stubs for the JSDocParser entry points of module M5.
//
// `namespace JSDocParser` of parser.ts is module M5. With JSDocParsingMode.ParseNone the scanner never
// reports a JSDoc comment, so `withJSDoc` never reaches these stubs; M5 replaces them.

namespace NeverSharp.Faithful;

internal sealed partial class Parser
{
    /// <summary>parser.ts: JSDocParser.parseJSDocComment (module M5).</summary>
    private static JSDoc? ParseJSDocComment(Node parent, int start, int length)
    {
        _ = parent;
        _ = start;
        _ = length;
        throw new NotImplementedException("M5");
    }
}
