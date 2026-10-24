# Binder additional run after BO2 (2026-10-24) — Windows only

Protocol-compliant run (profile `binder`, §16; rig checks ok, idle 5.5 %; variance gate PASS) at harness commit 07ac6df, label `binder-additional-run-after-BO2`. **Additional run, not the endpoint:** the binder endpoint stays G = 1.207 [1.191, 1.219] (inconclusive). Step since the previous additional run: BO2 (binder CPU hot paths). Full tables: [report.md](report.md).

| Parse+bind (95 % percentile bootstrap, seed 20261021) | Binder endpoint | After BO1 | This run |
|---|---|---|---|
| cs-o-aot-ws-bind vs tsgo7-bind (G) | 1.207 [1.191, 1.219] | 0.489 [0.483, 0.498] | **0.456 [0.451, 0.462]**: faster than typescript-go, about 2.2 times as fast |
| single-threaded vs tsgo7-bind-single | 0.985 | 0.763 | 0.654 [0.652, 0.655] |
| vs tsrs-bind (secondary) | 3.31 | 1.334 | **1.222 [1.207, 1.238]**: lower bound > 1.20, more than 20 % slower than tsrs |
| per repository vs tsrs-bind | — | — | vscode 1.18, sentry 1.28, bluesky 1.22, playwright 1.21, tldraw 1.23 |
| single-threaded vs tsrs-bind-single | 1.822 | 1.416 | 1.209 [1.206, 1.213] |

vscode parse+bind: Track O 0.516 s, typescript-go 1.360 s, tsrs 0.437 s; Track O allocates 95 MB, GC pause 52 ms; peak working set 1,680 MiB (typescript-go 2,753 MiB, tsrs 1,148 MiB). Variance notes (above 3 %): tsrs-bind vscode 4.37 % / bluesky 3.31 % / tldraw 3.07 %, cs-o-aot-srv-bind playwright 4.19 % / tldraw 3.24 %. Disclosed differences as in the previous note.
