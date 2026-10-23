# Track O: steps evaluated and not evaluated (A9)

The list the Track O endpoint is reported with (pre-registration §8, A9). Each step: up to 3 variants, Gate 1 and Track O Gate 2 on every variant, survivors measured on the tuning corpus (never on the benchmark repositories), winner by the pre-registered rule (lowest median; if the 95 % intervals overlap, fewer bytes allocated, then fewer lines). Order set by the profile in [track-o-profile.md](track-o-profile.md).

| Step | What | Status | Winning variant | Record |
|---|---|---|---|---|
| O0 | Seed: Track O starts as a copy of Track F (`tools/NeverSharp.Generate/seed-optimized.mjs`, node classes from the generator) | done 2026-10-22; full gate passes (Gate 2 with NoAlloc analyzer, unit tests, G1a, G1 ParseNone, Gate 1) | — (no variants) | phase1-progress.md |
| O6a | Handle-based node API with one simple storage (no variants; brief [modules/O6a.md](modules/O6a.md)) | done 2026-10-22; full gate passes; M5 workload 1,649 ms vs Track F 2,495 ms (−34 %), 823.5 vs 737.8 MB allocated | — (no variants) | phase1-progress.md, results/track-o/O6a/ |
| O6b | Node arena layout: up to 3 storage layouts behind the O6a API (brief [modules/O6b.md](modules/O6b.md)) | done 2026-10-22; tuning corpus cs-o-aot-ws: A 1,093 ms, B 1,137 ms, C 1,256 ms, O6a 1,312 ms | **A: compact records** (A/B 0.961 [0.946, 0.986]; A/O6a 0.833 [0.815, 0.854]) | phase1-progress.md, results/track-o/O6b/ |
| O5a | Text as spans plus character arena, interning with a span-keyed hash table (no variants; brief [modules/O5a.md](modules/O5a.md)) | done 2026-10-22; tuning corpus cs-o-aot-ws 579 ms vs main 837 ms (0.692 [0.674, 0.705]); M5 959 ms, 276 MB | — (no variants) | phase1-progress.md, results/track-o/O5a/ |
| O5b | Identifier interning lookup: switch on length/first character, perfect hash (behind the O5a seam) | not started; depends on O5a | | |
| O7a | Node lists as handles into a list store, ranges in one index arena (no variants; brief [modules/O7a.md](modules/O7a.md)) | done 2026-10-22; tuning corpus cs-o-aot-ws 845 ms vs main 1,100 ms (0.768 [0.760, 0.774]); M5 1.206 s, 417 MB | — (no variants) | phase1-progress.md, results/track-o/O7a/ |
| O7b | Node list layouts: up to 2 more behind the O7a seam (pooled arrays, small-list inline) | not started; depends on O7a | | |
| O-NA | Remaining allocations and NoAlloc coverage (phase1-plan §9 "NoAlloc compliance"; no variants; brief [modules/ONA.md](modules/ONA.md)) | done 2026-10-22; tuning corpus cs-o-aot-ws 473 ms vs main 581 ms (0.815 [0.800, 0.833]); NoAlloc 2,119 of 2,437 methods, 55 exceptions | — (no variants) | phase1-progress.md, results/track-o/ONA/ |
| O1 | File reading and decoding: A seam baseline (string), B pooled bytes + SIMD decode into a char arena, C memory-mapped + native text (brief [modules/O1.md](modules/O1.md)) | done 2026-10-22; tuning corpus cs-o-aot-ws: A 481 ms, B 461 ms, C 450 ms; C/B 0.977 [0.962, 1.003] overlaps, bytes decide (C 291 MB, B 392 MB, A 405 MB) | **C: native text** (C/A 0.935 [0.917, 0.956]) | phase1-progress.md, results/track-o/O1/ |
| O2 | Character classification | running as part of O23 (brief [modules/O23.md](modules/O23.md)) | | |
| O3 | Trivia and comment skipping | running as part of O23: B lookup tables, C SIMD | | |
| O4 | String, template and numeric literals | not started | | |
| O8 | Parallel scheduling | not started (probe 2026-10-23: Track O scales slightly better than tsrs; low priority) | | |
| O9 | Per-file overhead after the endpoint: Seal copies, parser pool, hash tables (brief [modules/O9.md](modules/O9.md)); A main, B hand over the scratch, C per-thread slabs | done 2026-10-23; tuning corpus cs-o-aot-ws: A 444 ms, B 456 ms, C 408 ms; single A 2,840, B 2,809, C 2,651 ms | **C: per-thread slabs** (C/A 0.918 [0.893, 0.936]; 97 vs 290 MB) | phase1-progress.md, results/track-o/O9/ |
| O10 | Side table of rare node fields without per-file allocation (no variants; brief [modules/O10.md](modules/O10.md)) | running | — (no variants) | |

Endpoint (2026-10-22, after O1, Windows only): optimized track G = 0.60 [0.59, 0.61] vs tsgo7-parse (faster claim allowed); vs tsrs 1.52 [1.50, 1.54] (secondary). See results/track-o/endpoint/2026-10-22T18-55-52-105Z/VERDICT.md.

NoAlloc coverage and exceptions: `node harness/port/noalloc-report.mjs`, reported with every Track O result.
