# AVX probe (development measurement, not a protocol result), 2026-10-23

Question (author): would allowing AVX/AVX2 help Track O? NativeAOT's default target is x86-64-v2 (SSE4.2, POPCNT); `Avx2.IsSupported` and hardware `Vector256` are false there, so AVX code paths would be compiled away (docs/references.md §2a). Probe step 1: the same Track O code (main at d75bdaf) built twice, the protocol build (`IlcInstructionSet` unset) and `-p:IlcInstructionSet=x86-64-v3` (AVX2, BMI1/2, FMA, LZCNT, MOVBE allowed without runtime checks; VEX encoding for the existing 128-bit vector code). `probe.mjs`: hyperfine --shell=none, 3 warmups, high priority, each pair measured twice in opposite order, pooled; ratio of medians with the 95 % bootstrap of stats.mjs. No rig check.

| Case | v2 (protocol build) | v3 | v3/v2 [95 %] |
|---|---:|---:|---|
| tuning corpus, parallel (40 runs each) | 374.6 ms | 371.3 ms | 0.991 [0.973, 1.014] |
| tuning corpus, single-threaded (20) | 2,228 ms | 2,224 ms | 0.998 [0.986, 1.008] |
| vscode, parallel (40) | 304.6 ms | 308.0 ms | 1.011 [0.994, 1.021] |
| vscode, single-threaded (20) | 2,737 ms | 2,734 ms | 0.999 [0.994, 1.002] |

Result: no measurable effect from the instruction set alone. Step 2 (scanner SIMD widened to 256 bits) was not built: identifiers average 7.6 characters and the 16-character blocks of O24 already cover most runs, and the scanner's SIMD paths are worth about 30 ms of a 547 ms in-memory parse in O24's measurements, so a 256-bit path could gain at most a fraction of that while needing the v3 build, i.e. an amendment and a departure from building each competitor as its project does. The protocol builds stay at .NET's default.
