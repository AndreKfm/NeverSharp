# Binder additional run after BO1 (2026-10-24) — Windows only

Protocol-compliant run (profile `binder`, §16; rig checks ok, idle 9.0 %; variance gate PASS) at harness commit f10774a, label `binder-additional-run-after-BO1`. **Additional run, not the endpoint:** the binder endpoint stays G = 1.207 [1.191, 1.219] (inconclusive; `../../binder-endpoint/2026-10-24T11-21-53-808Z/VERDICT.md`). Step since the endpoint: BO1 (binder allocations: dense parent array, binder storage in per-thread slabs). Full tables: [report.md](report.md).

| Parse+bind (95 % percentile bootstrap, seed 20261021) | Binder endpoint | This run |
|---|---|---|
| cs-o-aot-ws-bind vs tsgo7-bind (G) | 1.207 [1.191, 1.219] | **0.489 [0.483, 0.498]**: faster than typescript-go (upper bound < 1.00), about 2.0 times as fast |
| per repository | 1.12–1.35 | vscode 0.42, sentry 0.51, bluesky 0.52, playwright 0.50, tldraw 0.50 |
| single-threaded vs tsgo7-bind-single | 0.985 | 0.763 [0.762, 0.765] |
| cs-o-aot-srv-bind vs tsgo7-bind | 1.127 | 0.53 |
| vs tsrs-bind (secondary) | 3.31 | 1.334 [1.310, 1.352] (more than 20 % slower than tsrs) |
| single-threaded vs tsrs-bind-single | 1.822 | 1.416 [1.412, 1.419] |

vscode parse+bind: Track O 0.562 s (endpoint 1.519 s), typescript-go 1.340 s, tsrs 0.430 s; Track O allocates 91 MB (endpoint 3,984 MB) with 51 ms GC pause; peak working set 1,797 MiB (typescript-go 2,777 MiB, tsrs 1,148 MiB).

Variance notes (reported configurations above 3 %): tsgo7-bind bluesky 3.81 % / playwright 4.67 %, tsrs-bind vscode 4.67 % / playwright 4.72 %, cs-o-aot-srv-bind bluesky 3.54 % / playwright 4.95 %. Disclosed: typescript-go's and tsrs's binders port TypeScript 7 (symbol totals differ by 1 on four repositories); JSDoc and code-generation differences as in docs/references.md §2a.
