# Program check with tsrs's kind of pool, protocol run (2026-11-06) — Windows only

This is the first protocol-compliant run of profile `checkpool`, pre-registration §20 (`prereg/v12`, tagged before this run).
- **Harness:** commit 6920a1c, label `checkpool protocol run (prereg/v12)`. The C# drivers were built from 1eabf90, where the full Gate C and Gate D passed.
- **Rig checks:** ok; idle 3.4 %.
- **Variance gate:** PASS. The binding rows reach at most 0.53 % (`tsgo7-check-single`) and 0.38 % (`cs-o-aot-ws-check-single`).
- **Variance notes** (reported rows above 3 %): `cs-o-aot-ws-checkpool` playwright 3.05 %; `cs-o-aot-srv-checkpool` bluesky 3.32 %, playwright 3.75 %, tldraw 3.61 %; `tsrs-check` playwright 3.41 %.

**These comparisons are secondary.** The endpoints are unchanged: check G = 1.680 (§17), emit G = 1.030 (§19), check12 G = 1.305 (§18). **The C# side was tuned on these five repositories** after `prereg/v11` (modules EO1–MM1, disclosed in §20). Full tables: [report.md](report.md).

| Parse+bind+check (percentile bootstrap, seed 20261021) | Factor vs `tsrs-check` |
|---|---|
| cs-o-aot-ws-checkpool (12 checkers), G at 95 % | **2.149 [2.122, 2.182]** |
| per repository | vscode 2.42, sentry 1.91, bluesky 2.22, playwright 2.24, tldraw 2.01 |
| cs-o-aot-srv-checkpool (16 checkers), G at 98.75 % | **1.832 [1.796, 1.866]** |
| per repository | vscode 1.97, sentry 1.45, bluesky 2.12, playwright 1.98, tldraw 1.73 |
| tsgo7-check (4 checkers) / tsgo7-check12 | 5.10 / 3.20 |
| cs-o-aot-ws-check-single (tsrs parallel as the reference) | 10.81 |

Against typescript-go (Table 2k):
- ws checkpool vs tsgo7-check: 0.42 [0.42, 0.43];
- srv checkpool vs tsgo7-check: 0.36 [0.35, 0.36];
- ws checkpool vs tsgo7-check12: 0.67 [0.66, 0.68];
- srv checkpool vs tsgo7-check12: 0.57 [0.56, 0.58];
- single-threaded binding rows, cs-o-aot-ws-check-single vs tsgo7-check-single: 0.696 [0.695, 0.697].

Medians (s), tsrs / ws checkpool / srv checkpool:

| Repository | tsrs | ws checkpool | srv checkpool |
|---|---|---|---|
| vscode | 2.21 | 5.34 | 4.34 |
| sentry | 0.921 | 1.75 | 1.33 |
| bluesky | 0.199 | 0.442 | 0.421 |
| playwright | 0.307 | 0.686 | 0.608 |
| tldraw | 0.376 | 0.754 | 0.651 |

Peak working set on vscode: tsrs 2,766 MiB, ws 4,975, srv 5,346. Workstation GC pause on vscode: 1.28 s of 5.34 s (Table 5).

**Determinism (Table 1e, 5 untimed runs each):**
- **ws:** one distinct output on vscode and bluesky; 2 of 5 distinct on sentry and tldraw, with equal counts; 5 of 5 distinct on playwright (30,826–30,835).
- **srv:** 3 of 5 distinct on vscode; 1 on sentry and bluesky; 2 on tldraw; 5 of 5 on playwright (30,755–30,816).

With stealing, a file's checker depends on timing, and with it the diagnostics that depend on earlier types in that checker. Under Workstation GC playwright varies too, more than seen in AR2. The single-threaded output stays identical with tsc 6.0.3 (Gate C).
