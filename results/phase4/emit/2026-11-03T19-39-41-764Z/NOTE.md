# Program emit, protocol run of the Phase 4 endpoint (2026-11-03) — Windows only

This is the protocol run of profile `emit`, pre-registration §19 (`prereg/v11`, accepted and tagged before any emit measurement).
- **Harness:** commit 9435b89, label `Phase 4 emit endpoint, protocol run (prereg/v11)`.
- **Rig checks:** ok; idle 5.8 %.
- **Variance gate:** PASS. The binding rows reach at most 0.46 % (`tsgo7-emit-single`) and 0.56 % (`cs-o-aot-ws-emit-single`). No reported row is above 3 %; the highest is `tsrs-emit` playwright at 2.21 %.
- **Equivalence:** before the run, Gate D passed (50,177 of 50,177 units). The C# output of all five repository programs was identical with tsc 6.0.3's (modules D0–D8, `harness/bench/emit-equivalence.mjs`).

The report calls §19 a "draft". That wording is fixed in `report.mjs`, which §19 leaves unchanged; §19 is accepted (`prereg/v11`). Full tables: [report.md](report.md).

| Parse+bind+check+emit to memory, 4 checkers (percentile bootstrap, seed 20261021) | Result |
|---|---|
| **endpoint** cs-o-aot-ws-emit vs tsgo7-emit (G, 95 %) | **1.030 [1.026, 1.035]**: upper bound ≤ 1.20, **within 20 % of typescript-go (parity claim allowed)** |
| per repository | vscode 1.147, sentry 0.812, bluesky 1.153, playwright 1.153, tldraw 0.938 |
| single-threaded, cs-o-aot-ws-emit-single vs tsgo7-emit-single (95 %) | 0.821 [0.820, 0.822], faster |
| Server GC, cs-o-aot-srv-emit vs tsgo7-emit (98.75 %) | 0.894 [0.888, 0.900], faster (bluesky 1.041, playwright 1.045) |
| Server GC single, vs tsgo7-emit-single (98.75 %) | 0.816 [0.814, 0.818], faster |
| vs tsrs-emit (secondary reference) | ws 5.057 [5.032, 5.096]; single 1.630 [1.627, 1.633]; tsgo7-emit itself 4.907 |
| cs-o-aot-srv-emit vs cs-o-aot-ws-emit | 0.868 [0.864, 0.872] |

**Cost of the emit.** The table compares medians with the check-only medians of checker additional run 3 (after CO6, `../../phase3/additional/2026-11-02T22-22-25-874Z`). These are two separate runs, so the comparison is informative only.

| Repository | typescript-go | Track O ws | Track O srv | Track O ws single | typescript-go single |
|---|---|---|---|---|---|
| vscode | 14.29 → 20.18 s (+5.9) | 14.04 → 23.15 s (+9.1) | 11.21 → 19.23 s (+8.0) | 36.81 → 62.21 s (+25.4) | 42.68 → 65.26 s (+22.6) |
| sentry | 4.80 → 6.31 s (+1.5) | 3.58 → 5.13 s (+1.6) | 2.88 → 4.21 s (+1.3) | 10.52 → 15.06 s (+4.5) | 17.41 → 23.42 s (+6.0) |
| bluesky | 0.81 → 1.07 s (+0.25) | 0.95 → 1.23 s (+0.28) | 0.85 → 1.11 s (+0.26) | 2.27 → 3.09 s (+0.82) | 2.26 → 3.31 s (+1.05) |
| playwright | 1.49 → 1.89 s (+0.40) | 1.73 → 2.18 s (+0.44) | 1.56 → 1.97 s (+0.42) | 3.89 → 5.32 s (+1.42) | 3.95 → 5.68 s (+1.73) |
| tldraw | 1.95 → 2.45 s (+0.50) | 1.68 → 2.30 s (+0.62) | 1.47 → 2.03 s (+0.55) | 3.93 → 5.75 s (+1.82) | 6.20 → 8.26 s (+2.06) |

- **vscode:** Track O's emit costs 9.1 s under Workstation GC against typescript-go's 5.9 s. The check alone was at parity there (0.98). The emit also raises the Workstation GC pause on vscode to 4.42 s, against 2.31 s single-threaded and 0.70 s under Server GC (Table 5).
- **The other repositories:** Track O's emit costs about what typescript-go's does.
- **Single-threaded:** Track O's emit costs a little more than typescript-go's on vscode, and less on the other four.

**Work done (Table 1d).**
- **Files:** every side writes the same number of output files, except on sentry: C# writes 17,840, typescript-go and tsrs 17,841.
- **Bytes:** output bytes differ between TypeScript 6.0.3 (C#) and TypeScript 7 (typescript-go, tsrs) by at most 0.2 %. On vscode: C# 161,946,338, typescript-go 162,220,858, tsrs 161,028,849.
- **The C# side** is identical with tsc 6.0.3 on every repository.
- **Dependence on the checker count:** the check diagnostics of the parallel C# rows differ slightly from the single-threaded rows (vscode 101,474 against 101,493; playwright 30,789 against 30,808). typescript-go shows the same on tldraw (52,608 against 52,604).
  - This is the known dependence on the checker count (C12, §19).
  - The emitted bytes of the C# side are the same in all four C# configurations.

**Memory, peak working set on vscode:**

| Configuration | MiB |
|---|---:|
| cs-o-aot-ws-emit | 5,957 |
| cs-o-aot-srv-emit | 6,056 |
| tsgo7-emit | 10,976 |
| tsrs-emit | 3,196 |

Across the five repositories, the geometric mean of peak working set is ws 0.56× typescript-go's and 1.46× tsrs's.
