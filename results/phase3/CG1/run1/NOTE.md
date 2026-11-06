# CG1: NativeAOT code-generation settings (orchestrator experiment), 2026-11-06

Exploratory, nothing committed in the build: the settings below are excluded by pre-registration §5.4 rule 2 and would need an amendment (author decision). Drivers built from bfea197 (MM1 final) with `build.sh <out> <props>`:
- `final`: as registered (no `IlcInstructionSet`, no `IlcOptimizationPreference`);
- `v3`: `-p:IlcInstructionSet=x86-64-v3`;
- `speed`: `-p:IlcOptimizationPreference=Speed`;
- `v3speed`: both.

Dump checks (MM1's `dumpcheck.sh`: default-pool diagnostics sentry, bluesky, tuning-check; `--pool tsrs` ws vscode and sentry; emit playwright and tldraw): identical for all three variants.

Interleaved, 5 rounds after 1 warm-up, `--check --pool tsrs` (srv 16 checkers, ws 12), plus `--single` (ws) on the four smaller repositories; `bootstrapMedianRatio` against `final` of the same GC mode (95 % interval):

| repo | v3 srv | speed srv | v3speed srv | v3 ws | speed ws | v3speed ws | v3 single | v3speed single |
|---|---|---|---|---|---|---|---|---|
| bluesky | 0.951 [0.917, 1.062] | 1.016 | 0.976 | 1.022 | 1.042 | 1.022 | 0.995 [0.979, 1.002] | 1.002 |
| playwright | 1.013 | 1.063 | 1.029 | 1.006 | 1.006 | 1.020 | 0.996 | 0.991 |
| tldraw | 0.989 | 0.997 | 0.981 | 0.997 | 1.013 | 1.024 | 0.987 [0.976, 0.991] | 0.990 [0.985, 0.997] |
| sentry | 0.982 | 1.024 | 0.996 | 1.006 | 1.003 | 0.994 | 1.002 | 0.999 |
| vscode | 0.957 [0.940, 0.979] | 1.005 | 0.957 [0.940, 1.018] | 0.988 | 0.976 [0.955, 0.993] | 0.989 | | |

Factor of `final` against tsrs in these rounds: srv bluesky 2.16, playwright 1.89, tldraw 1.67, sentry 1.43, vscode 1.98; ws 2.14, 2.21, 1.97, 1.88, 2.42.

Result: the instruction-set and optimization-preference settings move the check by 0–4 % (only vscode srv with x86-64-v3 is faster by its interval, −4 %; single-threaded at most −1.3 %). The remaining per-operation gap to tsrs is not in these settings. Not tested: the code generator itself (RyuJIT/NativeAOT against LLVM with fat LTO) and static PGO for NativeAOT, which needs tooling not installed here.
