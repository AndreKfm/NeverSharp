# M6 run 2 (2026-10-22): variance gate failed again — not a deciding run

Repeat of run 1 per pre-registration §8. Rig compliant (idle 9.2 %), harness commit b3fd637, label `m6-track-f-run2`. Variance gate failed in two cells, both JIT single-threaded C# configurations: cs-jit-ws-single tldraw 3.94 %, cs-jit-srv-single sentry 5.08 %. The rig reference configurations (tsc6-parse, tsgo7-parse-single) stayed at or below 0.93 %, the primary single-threaded C# configuration cs-aot-ws-single at or below 1.68 %. Crosscheck A6: driver/tsgo 0.76–0.99.
