# Binder additional run after BO3 (2026-10-25) — Windows only

Protocol-compliant run (profile `binder`, §16; rig checks ok, idle 6.4 %; variance gate PASS) at harness commit 36c6cb2, label `binder-additional-run-after-BO3`. **Additional run, not the endpoint:** the binder endpoint stays G = 1.207 [1.191, 1.219] (inconclusive). Step since the previous additional run: BO3 (scratch block and compact commit, token fast path). Full tables: [report.md](report.md).

| Parse+bind (95 % percentile bootstrap, seed 20261021) | Binder endpoint | After BO1 | After BO2 | This run |
|---|---|---|---|---|
| cs-o-aot-ws-bind vs tsgo7-bind (G) | 1.207 | 0.489 | 0.456 | **0.434 [0.430, 0.440]**: faster than typescript-go, about 2.3 times as fast |
| single-threaded vs tsgo7-bind-single | 0.985 | 0.763 | 0.654 | 0.635 [0.633, 0.637] |
| cs-o-aot-srv-bind vs tsgo7-bind | 1.127 | 0.53 | 0.50 | 0.467 |
| vs tsrs-bind (secondary) | 3.31 | 1.334 | 1.222 | **1.193 [1.172, 1.204]**: contains 1.20, inconclusive for "within 20 % of tsrs" |
| per repository vs tsrs-bind | — | — | 1.18–1.28 | vscode 1.17, sentry 1.26, bluesky 1.15, playwright 1.21, tldraw 1.18 |
| single-threaded vs tsrs-bind-single | 1.822 | 1.416 | 1.209 | 1.172 [1.169, 1.176] |

vscode parse+bind: Track O 0.459 s, typescript-go 1.358 s, tsrs 0.393 s; Track O allocates 93 MB, GC pause 42 ms; peak working set 1,535 MiB (typescript-go 2,756, tsrs 1,148). Variance notes (above 3 %): tsgo7-bind vscode 3.37 % / playwright 3.04 %, tsrs-bind vscode 3.41 % / bluesky 3.02 %. Disclosed differences as in the previous notes.
