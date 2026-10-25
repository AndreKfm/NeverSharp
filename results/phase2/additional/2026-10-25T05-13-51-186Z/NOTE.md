# Binder additional run after BO4 (2026-10-25) — Windows only

Protocol-compliant run (profile `binder`, §16; rig checks ok, idle 5.2 %; variance gate PASS) at harness commit 68a7829, label `binder-additional-run-after-BO4`. **Additional run, not the endpoint:** the binder endpoint stays G = 1.207 [1.191, 1.219] (inconclusive). Step since the previous additional run: BO4 (smaller binder output and store object). Full tables: [report.md](report.md).

| Parse+bind (95 % percentile bootstrap, seed 20261021) | Endpoint | After BO1 | After BO2 | After BO3 | This run |
|---|---|---|---|---|---|
| cs-o-aot-ws-bind vs tsgo7-bind (G) | 1.207 | 0.489 | 0.456 | 0.434 | **0.425 [0.419, 0.431]**: faster than typescript-go, about 2.4 times as fast |
| single-threaded vs tsgo7-bind-single | 0.985 | 0.763 | 0.654 | 0.635 | 0.635 [0.633, 0.636] |
| vs tsrs-bind (secondary) | 3.31 | 1.334 | 1.222 | 1.193 | **1.146 [1.127, 1.161]**: upper bound ≤ 1.20, **within 20 % of tsrs** (secondary) |
| per repository vs tsrs-bind | — | — | 1.18–1.28 | 1.15–1.26 | vscode 1.12, sentry 1.19, bluesky 1.14, playwright 1.15, tldraw 1.13 |
| single-threaded vs tsrs-bind-single | 1.822 | 1.416 | 1.209 | 1.172 | 1.176 [1.172, 1.179] |

vscode parse+bind: Track O 0.450 s, typescript-go 1.325 s, tsrs 0.404 s; Track O allocates 89 MB, GC pause 50 ms; peak working set 1,404 MiB (typescript-go 2,764, tsrs 1,148). Variance notes (above 3 %): tsgo7-bind vscode 3.15 %, tsrs-bind vscode 3.87 % / sentry 3.56 % / tldraw 3.16 %, cs-o-aot-srv-bind playwright 4.28 % / tldraw 4.21 %. Disclosed differences as in the previous notes.
