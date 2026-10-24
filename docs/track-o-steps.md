# Track O: steps evaluated and not evaluated (A9)

The list the Track O endpoint is reported with (pre-registration §8, A9). Each step: up to 3 variants, Gate 1 and Track O Gate 2 on every variant, survivors measured on the tuning corpus (never on the benchmark repositories), winner by the pre-registered rule (lowest median; if the 95 % intervals overlap, fewer bytes allocated, then fewer lines). Order set by the profile in [track-o-profile.md](track-o-profile.md).

| Step | What | Status | Winning variant | Record |
|---|---|---|---|---|
| O0 | Seed: Track O starts as a copy of Track F (`tools/NeverSharp.Generate/seed-optimized.mjs`, node classes from the generator) | done 2026-10-22; full gate passes (Gate 2 with NoAlloc analyzer, unit tests, G1a, G1 ParseNone, Gate 1) | — (no variants) | phase1-progress.md |
| O6a | Handle-based node API with one simple storage (no variants; brief [modules/O6a.md](modules/O6a.md)) | done 2026-10-22; full gate passes; M5 workload 1,649 ms vs Track F 2,495 ms (−34 %), 823.5 vs 737.8 MB allocated | — (no variants) | phase1-progress.md, results/track-o/O6a/ |
| O6b | Node arena layout: up to 3 storage layouts behind the O6a API (brief [modules/O6b.md](modules/O6b.md)) | done 2026-10-22; tuning corpus cs-o-aot-ws: A 1,093 ms, B 1,137 ms, C 1,256 ms, O6a 1,312 ms | **A: compact records** (A/B 0.961 [0.946, 0.986]; A/O6a 0.833 [0.815, 0.854]) | phase1-progress.md, results/track-o/O6b/ |
| O5a | Text as spans plus character arena, interning with a span-keyed hash table (no variants; brief [modules/O5a.md](modules/O5a.md)) | done 2026-10-22; tuning corpus cs-o-aot-ws 579 ms vs main 837 ms (0.692 [0.674, 0.705]); M5 959 ms, 276 MB | — (no variants) | phase1-progress.md, results/track-o/O5a/ |
| O5b | Identifier interning lookup: switch on length/first character, perfect hash (behind the O5a seam) | keyword lookup part evaluated inside O23 (both variants: perfect hash; merged with B); interning lookup done 2026-10-23 (brief [modules/O5b.md](modules/O5b.md)): 0.993 [0.950, 1.019], not slower | — (one attempt), merged | phase1-progress.md, results/track-o/O5b-O11/ |
| O7a | Node lists as handles into a list store, ranges in one index arena (no variants; brief [modules/O7a.md](modules/O7a.md)) | done 2026-10-22; tuning corpus cs-o-aot-ws 845 ms vs main 1,100 ms (0.768 [0.760, 0.774]); M5 1.206 s, 417 MB | — (no variants) | phase1-progress.md, results/track-o/O7a/ |
| O7b | Node list layouts: up to 2 more behind the O7a seam (pooled arrays, small-list inline) | not started; depends on O7a | | |
| O-NA | Remaining allocations and NoAlloc coverage (phase1-plan §9 "NoAlloc compliance"; no variants; brief [modules/ONA.md](modules/ONA.md)) | done 2026-10-22; tuning corpus cs-o-aot-ws 473 ms vs main 581 ms (0.815 [0.800, 0.833]); NoAlloc 2,119 of 2,437 methods, 55 exceptions | — (no variants) | phase1-progress.md, results/track-o/ONA/ |
| O1 | File reading and decoding: A seam baseline (string), B pooled bytes + SIMD decode into a char arena, C memory-mapped + native text (brief [modules/O1.md](modules/O1.md)) | done 2026-10-22; tuning corpus cs-o-aot-ws: A 481 ms, B 461 ms, C 450 ms; C/B 0.977 [0.962, 1.003] overlaps, bytes decide (C 291 MB, B 392 MB, A 405 MB) | **C: native text** (C/A 0.935 [0.917, 0.956]) | phase1-progress.md, results/track-o/O1/ |
| O2 | Character classification | done 2026-10-23 as part of O23 (brief [modules/O23.md](modules/O23.md)) | see O23 | phase1-progress.md |
| O3 | Trivia and comment skipping | done 2026-10-23 as part of O23: run 2 on O10: B 393 ms, C 398 ms (C/B 1.013 [0.983, 1.050]), bytes decide | **O23 B: lookup tables** (B/O10 0.949 [0.911, 0.977]) | phase1-progress.md, results/track-o/O10-O23/ |
| O4 | String, template and numeric literals | not started | | |
| O8 | Parallel scheduling | not started (probe 2026-10-23: Track O scales slightly better than tsrs; low priority) | | |
| O9 | Per-file overhead after the endpoint: Seal copies, parser pool, hash tables (brief [modules/O9.md](modules/O9.md)); A main, B hand over the scratch, C per-thread slabs | done 2026-10-23; tuning corpus cs-o-aot-ws: A 444 ms, B 456 ms, C 408 ms; single A 2,840, B 2,809, C 2,651 ms | **C: per-thread slabs** (C/A 0.918 [0.893, 0.936]; 97 vs 290 MB) | phase1-progress.md, results/track-o/O9/ |
| O10 | Side table of rare node fields without per-file allocation (no variants; brief [modules/O10.md](modules/O10.md)) | done 2026-10-23; 409 vs 415 ms (0.987 [0.964, 1.010]), not slower | — (no variants), merged | phase1-progress.md, results/track-o/O10-O23/ |
| O11 | Speculative parsing: B token replay, C exact pre-decisions (brief [modules/O11.md](modules/O11.md)) | done 2026-10-23; main 401 ms, B 406 ms, C 387 ms; C on O5b 0.945 [0.924, 0.970] | **C: exact pre-decisions** (C/main 0.965 [0.935, 0.998]) | phase1-progress.md, results/track-o/O5b-O11/ |
| O12 | Node creation path (one attempt; brief [modules/O12.md](modules/O12.md)) | done 2026-10-23; 380 vs 399 ms (0.951 [0.929, 0.981]) | — (one attempt), merged | phase1-progress.md, results/track-o/O12-O24/ |
| O24 | Scanner, second round: O23 C parts on top of B, token bookkeeping (one attempt; brief [modules/O24.md](modules/O24.md)) | done 2026-10-23; vs main 0.976 [0.940, 1.010]; on O12 1.002 [0.980, 1.029], not slower | — (one attempt), merged | phase1-progress.md, results/track-o/O12-O24/ |
| O25 | Expression descent: exact shortcuts (one attempt; brief [modules/O25.md](modules/O25.md)) | done 2026-10-23; 363 vs 370 ms (0.981 [0.958, 0.992]) | — (one attempt), merged | phase1-progress.md, results/track-o/O25-O26/ |
| O26 | Token consumption: parser reads of scanner state, NextToken, ParseTokenNode (one attempt; brief [modules/O26.md](modules/O26.md)) | done 2026-10-23; vs main 0.976 [0.951, 0.997]; on O25 0.994 [0.978, 1.016], not slower | — (one attempt), merged | phase1-progress.md, results/track-o/O25-O26/ |
| O27 | Identifier path and interning (one attempt; brief [modules/O27.md](modules/O27.md)) | done 2026-10-24; vs main 0.985 [0.958, 1.006]; on O28 0.990 [0.956, 1.006], not slower | — (one attempt), merged | phase1-progress.md, results/track-o/O27-O28/ |
| O28 | Call/member rest and node lists (one attempt; brief [modules/O28.md](modules/O28.md)) | done 2026-10-24; 356 vs 370 ms (0.962 [0.940, 0.976]) | — (one attempt), merged | phase1-progress.md, results/track-o/O27-O28/ |
| O29 | NativeAOT inlining audit of the hot paths (one attempt; brief [modules/O29.md](modules/O29.md)) | running | | |

Endpoint (2026-10-22, after O1, Windows only): optimized track G = 0.60 [0.59, 0.61] vs tsgo7-parse (faster claim allowed); vs tsrs 1.52 [1.50, 1.54] (secondary). See results/track-o/endpoint/2026-10-22T18-55-52-105Z/VERDICT.md.

Additional run (2026-10-23, after O1, O9, O10, O23; not the endpoint): G = 0.47 [0.46, 0.48] vs tsgo7-parse; vs tsrs 1.19 [1.18, 1.21] (contains 1.20, inconclusive; secondary). results/track-o/additional/2026-10-23T08-51-58-122Z/NOTE.md.

Additional run (2026-10-23, after O5b, O11; not the endpoint): G = 0.46 [0.45, 0.46] vs tsgo7-parse; vs tsrs 1.16 [1.14, 1.17]: within 20 % of tsrs (secondary). results/track-o/additional/2026-10-23T11-50-41-097Z/NOTE.md.

Additional run (2026-10-23, after O12, O24; not the endpoint): G = 0.43 [0.42, 0.44] vs tsgo7-parse; vs tsrs 1.06 [1.05, 1.07] (within 20 %, secondary; vscode 0.95 [0.92, 0.99], reported, not claimed). results/track-o/additional/2026-10-23T14-51-13-533Z/NOTE.md.

Additional run (2026-10-23, after O25, O26; not the endpoint): G = 0.41 [0.41, 0.42] vs tsgo7-parse; vs tsrs 1.04 [1.03, 1.06] (within 20 %, secondary; vscode 0.95 [0.90, 0.98], reported, not claimed). results/track-o/additional/2026-10-23T17-40-49-265Z/NOTE.md.

Additional run (2026-10-24, after O27, O28; not the endpoint): G = 0.40 [0.39, 0.40] vs tsgo7-parse; vs tsrs 1.02 [1.01, 1.04] (within 20 %, secondary; vscode 0.92 [0.88, 0.94], reported, not claimed). results/track-o/additional/2026-10-24T03-34-00-173Z/NOTE.md.

NoAlloc coverage and exceptions: `node harness/port/noalloc-report.mjs`, reported with every Track O result.
