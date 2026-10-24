# Phase 2 binder endpoint (2026-10-24) — Windows only

First protocol-compliant run after Gate B passed (pre-registration §15, `prereg/v7`; profile `binder`, §16, `prereg/v8`). Harness commit 7019057, label `binder-endpoint-run1`. Rig checks ok (idle 6.2 %); variance gate PASS (binding: tsgo7-bind-single ≤ 0.68 %, cs-o-aot-ws-bind-single ≤ 0.58 %). Full tables: [report.md](report.md). Track O's binder at this point is the unoptimized port of B1/B3 (Gate B: binder dumps identical to tsc 6.0.3 on all 50,177 units); no binder optimization step has run yet.

**Binder endpoint** (parse+bind per file, cs-o-aot-ws-bind vs tsgo7-bind, geometric mean over the five repositories, 95 % percentile bootstrap, seed 20261021):

> G = 1.207 [1.191, 1.219]

The interval contains 1.20 → **inconclusive**: neither "within 20 % of typescript-go" nor "more than 20 % slower" is claimed for parse+bind. Per repository: vscode 1.12 [1.09, 1.14], sentry 1.35 [1.33, 1.37], bluesky 1.24 [1.19, 1.27], playwright 1.12 [1.09, 1.14], tldraw 1.22 [1.19, 1.26].

**Secondary** (95 % intervals):

- Single-threaded: cs-o-aot-ws-bind-single vs tsgo7-bind-single **0.985 [0.983, 0.988]** — single-threaded, Track O's parse+bind is as fast as typescript-go's (1.5 % faster).
- cs-o-aot-srv-bind vs tsgo7-bind 1.127 [1.102, 1.164] (variance notes: cs-o-aot-srv-bind up to 8.60 %).
- Against tsrs 0.18.0 (Table 2d): cs-o-aot-ws-bind 3.31 [3.25, 3.36]; single-threaded 1.822 [1.817, 1.828]. tsrs vs tsgo7-bind 0.36.

**Where the parallel gap comes from:** the binder allocates. cs-o-aot-ws-bind allocates 3,984 MB on vscode (parse only: about 65 MB) with 178 gen0 collections and 581 ms of GC pause in total (sentry 1,432 MB / 256 ms); under Workstation GC the parallel run is held up by collections, while single-threaded the cost is equal to typescript-go's. vscode: parse+bind 1.519 s (Track O) vs 1.357 s (typescript-go) vs 0.399 s (tsrs); parse only in the last additional run 0.295 s vs 1.036 s.

**Disclosed:** typescript-go's and tsrs's binders port TypeScript 7 and differ from 6.0.3's in places (symbol totals differ by 1 on four repositories); typescript-go 7.0.2's `BindSourceFile` takes no compiler options, Track O binds with `{}` as tsc's `bindSourceFile`; JSDoc and code-generation differences as in Phase 1 (docs/references.md §2a). Variance notes (reported configurations above 3 %): tsrs-bind vscode 6.20 %, cs-o-aot-ws-bind bluesky 3.27 % / tldraw 3.45 %, cs-o-aot-srv-bind all repositories (3.48–8.60 %).
