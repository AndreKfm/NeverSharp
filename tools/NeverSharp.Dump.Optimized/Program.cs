// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): canonical dump writers of Track O (tools/NeverSharp.Dump/Program.cs plus the binder dump).
//
// Usage: NeverSharp.Dump.Optimized <mode> [--corpus <dir>] [--out <dir>]
//   tokens, ast, ast-replay   as tools/NeverSharp.Dump (outputs cs-optimized-*)
//   binder                    binder dump for Gate B (module B0; harness/astdump/dump-ts-binder.mjs has the format)
//                             --jsdoc-mode all|none|types   -> <corpus>/dumps/cs-optimized-binder-<mode>
//   (--binder with any mode selects the binder dump too)
//   printer                   printer dump for the printer gate (Phase 3, module P1; harness/astdump/dump-ts-printer.mjs)
//                             --jsdoc-mode all|none|types (default types) -> <corpus>/dumps/cs-optimized-printer-<mode>
//                             [--units <id,id,...>] dumps only these units (for probing)
//   (--printer with any mode selects the printer dump too)
//   checker                   checker dump for Gate C (module C0; harness/astdump/dump-ts-checker.mjs has the format)
//                             --jsdoc-mode types|all|none (default types) [--lib-dir <dir>] [--units <file>]
//                             -> <corpus>/dumps/cs-optimized-checker-<mode>
//   (--checker with any mode selects the checker dump too)
//   emit                      emit dump for Gate D (module D0; harness/astdump/dump-ts-emit.mjs has the format)
//                             [--lib-dir <dir>] [--units <file>] -> <corpus>/dumps/cs-optimized-emit-types
//   (--emit with any mode selects the emit dump too)
//   printer --comments keep   the printer dump with removeComments: false (module D0)

using NeverSharp.Dump;

return args.Length == 0 ? Usage()
    : Array.IndexOf(args, "--emit") >= 0 ? EmitDump.Run(new Options(args))
    : Array.IndexOf(args, "--checker") >= 0 ? CheckerDump.Run(new Options(args))
    : Array.IndexOf(args, "--binder") >= 0 ? BinderDump.Run(new Options(args))
    : Array.IndexOf(args, "--printer") >= 0 ? PrinterDump.Run(new Options(args))
    : args[0] switch
    {
        "tokens" => TokenDump.Run(new Options(args)),
        "ast" => AstDump.Run(new Options(args)),
        "ast-replay" => AstDump.Replay(new Options(args)),
        "binder" => BinderDump.Run(new Options(args)),
        "printer" => PrinterDump.Run(new Options(args)),
        "checker" => CheckerDump.Run(new Options(args)),
        "emit" => EmitDump.Run(new Options(args)),
        _ => Usage(),
    };

static int Usage()
{
    Console.Error.WriteLine("usage: NeverSharp.Dump.Optimized tokens|ast|ast-replay|binder|printer|checker|emit [--corpus <dir>] [--out <dir>] [--jsdoc-mode all|none|types] [--timeout-ms <n>] [--lib-dir <dir>] [--units <file or ids>]");
    return 2;
}
