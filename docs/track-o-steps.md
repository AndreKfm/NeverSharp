# Track O: steps evaluated and not evaluated (A9)

The list the Track O endpoint is reported with (pre-registration §8, A9). Each step: up to 3 variants, Gate 1 and Track O Gate 2 on every variant, survivors measured on the tuning corpus (never on the benchmark repositories), winner by the pre-registered rule (lowest median; if the 95 % intervals overlap, fewer bytes allocated, then fewer lines). Order set by the profile in [track-o-profile.md](track-o-profile.md).

| Step | What | Status | Winning variant | Record |
|---|---|---|---|---|
| O0 | Seed: Track O starts as a copy of Track F (`tools/NeverSharp.Generate/seed-optimized.mjs`, node classes from the generator) | done 2026-10-22; full gate passes (Gate 2 with NoAlloc analyzer, unit tests, G1a, G1 ParseNone, Gate 1) | — (no variants) | phase1-progress.md |
| O6a | Handle-based node API with one simple storage (no variants; brief [modules/O6a.md](modules/O6a.md)) | running | — | |
| O6b | Node arena layout: up to 3 storage layouts behind the O6a API | not started | | |
| O5 | Text spans and identifier interning | not started | | |
| O7 | Node lists | not started | | |
| O1 | File reading and decoding | not started | | |
| O2 | Character classification | not started | | |
| O3 | Trivia and comment skipping | not started | | |
| O4 | String, template and numeric literals | not started | | |
| O8 | Parallel scheduling | not started | | |

NoAlloc coverage and exceptions: `node harness/port/noalloc-report.mjs`, reported with every Track O result.
