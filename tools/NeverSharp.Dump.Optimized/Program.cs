// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): canonical dump writers of Track O (tools/NeverSharp.Dump/Program.cs plus the binder dump).
//
// Usage: NeverSharp.Dump.Optimized <mode> [--corpus <dir>] [--out <dir>]
//   tokens, ast, ast-replay   as tools/NeverSharp.Dump (outputs cs-optimized-*)
//   binder                    binder dump for Gate B (module B0; harness/astdump/dump-ts-binder.mjs has the format)
//                             --jsdoc-mode all|none|types   -> <corpus>/dumps/cs-optimized-binder-<mode>
//   (--binder with any mode selects the binder dump too)

using NeverSharp.Dump;

return args.Length == 0 ? Usage() : Array.IndexOf(args, "--binder") >= 0 ? BinderDump.Run(new Options(args)) : args[0] switch
{
    "tokens" => TokenDump.Run(new Options(args)),
    "ast" => AstDump.Run(new Options(args)),
    "ast-replay" => AstDump.Replay(new Options(args)),
    "binder" => BinderDump.Run(new Options(args)),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: NeverSharp.Dump.Optimized tokens|ast|ast-replay|binder [--corpus <dir>] [--out <dir>] [--jsdoc-mode all|none|types] [--timeout-ms <n>]");
    return 2;
}
