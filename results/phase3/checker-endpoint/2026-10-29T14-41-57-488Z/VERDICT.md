# Phase 3 checker endpoint (2026-10-29) — Windows only

First protocol-compliant run after Gate C passed (pre-registration §17, `prereg/v9`; profile `check`). Harness commit b42ecc2, label `checker endpoint (prereg/v9)`. Rig checks ok (idle 4.6 %); variance gate PASS (binding: tsgo7-check-single ≤ 0.52 %, cs-o-aot-ws-check-single ≤ 0.35 %). Full tables: [report.md](report.md). Track O's checker at this point is the unoptimized port of C0–C11 (Gate C: 50,176 of 50,177 units identical with tsc 6.0.3, 027977's timeout under load accepted); no checker optimization step has run yet.

An earlier attempt (results directory `2026-10-29T13-05-53-902Z`, not kept) was aborted by a crash of `cs-o-aot-ws-check` on vscode and carries no result. Fixed before this run, each followed by Gate C and the repository equivalence: 17817f7 (the "avoid infinite loop" marker of getTypePredicateOfSignature), 7455352 (module specifiers of program files in messages), 3a0db38 (tuple element flags past the target's elements), b42ecc2 (getJSDocTags wrote into a shared file's store while other checkers read it).

**Checker endpoint** (each repository's file list as one program, parse+bind+check, cs-o-aot-ws-check vs tsgo7-check, geometric mean over the five repositories, 95 % percentile bootstrap, seed 20261021):

> G = 1.680 [1.675, 1.689]

The lower bound is above 1.20 → **no parity**: "more than 20 % slower than typescript-go" for parse+bind+check of a program. Per repository: vscode 1.752 [1.743, 1.763], sentry 1.335 [1.329, 1.343], bluesky 1.901 [1.885, 1.930], playwright 2.008 [1.990, 2.032], tldraw 1.499 [1.484, 1.518].

**Secondary** (95 % intervals unless stated):

- Single-threaded: cs-o-aot-ws-check-single vs tsgo7-check-single **0.954 [0.953, 0.956]** — one checker, Track O checks 4.6 % faster than typescript-go (vscode 1.033, sentry 0.717, bluesky 1.173, playwright 1.193, tldraw 0.762).
- cs-o-aot-srv-check vs tsgo7-check 1.055 [1.047, 1.065] (98.75 %, Bonferroni): upper bound ≤ 1.20 → "within 20 % of typescript-go" for the Server GC configuration (secondary; bluesky 1.301 and playwright 1.287 are slower than that, sentry 0.815 and tldraw 0.946 faster). Single-threaded 0.871 [0.869, 0.873].
- Against tsrs 0.18.0 (secondary reference, 16 checkers with work stealing against 4 fixed checkers, disclosed): cs-o-aot-ws-check 8.57 [8.52, 8.66]; single-threaded 2.007 [2.004, 2.012]. tsrs vs tsgo7-check 0.196, single-threaded 0.475.

**Where the parallel gap comes from:** single-threaded the check costs what typescript-go's costs, so the gap is the parallel run. cs-o-aot-ws-check speeds up 1.76× from one to four checkers (vscode 43.9 s → 25.0 s), typescript-go 2.98× (42.5 s → 14.2 s). The checker allocates: vscode 20.2 GB with 1,999/610/11 collections and 13.0 s of GC pause in total under Workstation GC (single-threaded 17.6 GB, 8.0 s); Server GC, with the same allocations, has 2.3 s of pause and is at typescript-go's speed on vscode (1.012). Process CPU on vscode: 55 s user for 25 s wall (C#), 68 s for 14 s (typescript-go).

**Diagnostics** (Table 1c): the C# diagnostics of all five repository programs were line-identical with tsc 6.0.3's single-threaded before the tag (check-equivalence.mjs, after b42ecc2 again). Counts in this run, C# single / parallel: vscode 101,493 / 101,475, sentry 237,529 / 237,529, bluesky 33,337 / 33,337, playwright 30,808 / 30,791, tldraw 51,900 / 51,900; typescript-go 101,933 / 237,531 / 33,337 / 30,838 / 52,608 (single 52,604 on tldraw); tsrs 101,941 / 237,533 / 33,337 / 30,838 / 52,612. With four checkers, a file's diagnostics depend on which types its checker created before (typescript-go's counts move the same way on tldraw); the parallel C# counts were the same in 12 of 12 repeated vscode runs.

**Disclosed:** typescript-go's and tsrs's checkers port TypeScript 7 and differ from 6.0.3's in places; the options are TypeScript 6.0.3's defaults, passed to typescript-go and tsrs explicitly; the repositories' dependencies are not installed and only imports between program files resolve, identically on all sides; tsrs's checker pool differs (above). Variance notes: none above 3 % (highest reported row: cs-o-aot-srv-check bluesky 2.30 %).
