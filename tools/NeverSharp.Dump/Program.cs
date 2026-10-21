// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): canonical dump writers of Track F (docs/ast-dump-format.md).
//
// Usage: NeverSharp.Dump <mode> [--corpus <dir>] [--out <dir>]
//   tokens       token-stream dump for gate G1a (§8)                 -> <corpus>/dumps/cs-faithful-tokens
//   ast          AST dump for Gate 1 (format version 1, §4)          -> <corpus>/dumps/cs-faithful
//                --jsdoc-mode none: JSDocParsingMode.ParseNone        -> <corpus>/dumps/cs-faithful-parsenone
//   ast-replay   check of the AST renderer: re-renders tsc's own dumps (<corpus>/dumps/ts) from parsed lines
//   --corpus: the equivalence corpus (manifest.json, units/); default artifacts/equivalence of this checkout

using NeverSharp.Dump;

return args.Length == 0 ? Usage() : args[0] switch
{
    "tokens" => TokenDump.Run(new Options(args)),
    "ast" => AstDump.Run(new Options(args)),
    "ast-replay" => AstDump.Replay(new Options(args)),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: NeverSharp.Dump tokens|ast|ast-replay [--corpus <dir>] [--out <dir>] [--jsdoc-mode all|none] [--timeout-ms <n>]");
    return 2;
}
