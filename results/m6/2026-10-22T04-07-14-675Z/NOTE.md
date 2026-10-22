# M6 run 1 (2026-10-22): variance gate failed — not a deciding run

Protocol-compliant rig (idle 8.3 %, all rig checks ok), harness commit 7f2baa8, label `m6-track-f`. The variance gate (pre-registration §8, B11: every single-threaded configuration on every repository MAD/median < 3 %) failed in four cells of secondary C# configurations: cs-aot-srv-single vscode 3.55 %, cs-jit-ws-single sentry 3.36 %, cs-jit-srv-single sentry 3.71 % and tldraw 3.56 %. Per §8 the run is repeated before any number is published; this run is archived for transparency and does not carry the verdict.

Crosscheck A6 (`crosscheck.json`): driver/tsgo parse time 0.75–1.00 across the five repositories. `crosscheck.mjs` was first started without its run-file argument (an error in the instructions given to the author) and then run with it in the same session.
