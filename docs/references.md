# Upstream references and provenance

Status: resolved 2026-10-14. Machine-readable values: [pins.json](../pins.json).
Items marked **[DECISION]** were proposed by the maintainer's assistant; **[CONFIRMED 2026-10-14]** means the author approved them. Unconfirmed decisions remain open to veto before the pre-registration tag.

## 1. Pinned references

| Role | Repository | Tag | Commit | Commit date |
|---|---|---|---|---|
| Port source, oracle 1 (tsc 6.0) | microsoft/TypeScript | `v6.0.3` | `050880ce59e30b356b686bd3144efe24f875ebc8` | 2026-04-15 |
| Baseline, oracle 2 (TypeScript 7.0) | microsoft/typescript-go | `typescript/v7.0.2` | `2bd066d87f5bafd315be9f40889d0a60b9e58e0b` | 2026-07-07 |

How the commits were established:

- `git ls-remote --tags` on both repositories. Both tags are lightweight tags pointing directly at the commits above.
- Cross-checked against the npm registry: `typescript@6.0.3` has `gitHead` `050880ce…` (published 2026-04-16); `typescript@7.0.2` has `gitHead` `2bd066d8…` (published 2026-07-08, the GA date).
- v6.0.3 is the highest 6.0.x tag and npm version as of 2026-10-14. 7.0.2 is the only `typescript/v7.*` tag and the npm `latest` dist-tag.
- Note: the npm metadata of `typescript@7.0.2` gives `repository.url` as microsoft/TypeScript, but its `gitHead` is a typescript-go commit. Cite the typescript-go repository, not the npm metadata.

## 2. Oracle delta: typescript-go was not ported from v6.0.3

typescript-go records the TypeScript source it ports from in the `_submodules/TypeScript` gitlink. At the pinned typescript-go commit that gitlink is:

`4d4f005c8541e0255a9d8791205fdce326e462bc` (branch `tsgo-port`, 2026-06-29, "Merge branch 'main' into tsgo-port")

Relation to `v6.0.3` (GitHub compare API): **diverged**, 113 commits ahead, 11 behind, merge base `206ed1a00ffde637d821bbb3172d1488e3d949e8`. The 11 "behind" commits are release-6.0 branch commits (version bumps, LKG, cherry-picks that also exist on main under different SHAs).

Files under `src/compiler/` that differ between `v6.0.3` and `4d4f005c`:

| File | Lines | Phase |
|---|---|---|
| `scanner.ts` | +1 −1 | 1 |
| `binder.ts` | +3 −0 | 2 |
| `checker.ts` | +7 −3 | 3 |
| `program.ts` | +3 −0 | out of scope |
| `commandLineParser.ts` | +1 −1 | out of scope |
| `diagnosticMessages.json` | +1 −1 | message text only |

`parser.ts`, `types.ts`, `utilities.ts` are identical. No file under `tests/cases/` differs.

The single Phase 1 difference is commit `637d5746b70257028fb95aad32ddec6b26ab0a14` ("Fix infinite loop (#63581)", 2026-06-25), in `scanJsDocToken`:

```diff
- while (pos < end && isIdentifierPart(char = codePointUnchecked(pos), languageVersion) || char === CharacterCodes.minus) pos += charSize(char);
+ while (pos < end && (isIdentifierPart(char = codePointUnchecked(pos), languageVersion) || char === CharacterCodes.minus)) pos += charSize(char);
```

In v6.0.3 the expression parses as `(pos < end && isIdentifierPart(...)) || char === minus`. When a JSDoc identifier token ends in `-` exactly at the end of the scanner's range, `pos < end` is false, `char` keeps the value `-`, and `pos` is incremented forever. typescript-go (`internal/scanner/scanner.go`, `ScanJSDocToken`) has the fixed semantics. Consequences:

- The delta is confined to JSDoc scanning. If JSDoc is out of scope for v1, it does not affect Phase 1 at all.
- On every input where v6.0.3 terminates, the old and new expressions produce the same token. The oracles can disagree only by tsc 6.0.3 hanging; the dumper must therefore run each file under a timeout and report a timeout as its own outcome, not as a mismatch.

**[DECISION, CONFIRMED 2026-10-14] Port source is `v6.0.3`**, as the brief specifies (a released, npm-installable version with a registry integrity hash). The delta above is recorded as a known, pre-registered oracle difference. Rejected alternative: port from `4d4f005c` (exactly what typescript-go ported), which would mean an unreleased commit and building oracle 1 from source.

## 3. Benchmark corpus

The brief's list (VS Code, Sentry, Bluesky, Playwright, tldraw) matches the tables in Microsoft's "Announcing TypeScript 7.0" post of 2026-07-08 (https://devblogs.microsoft.com/typescript/announcing-typescript-7-0/). The March 2025 "A 10x Faster TypeScript" post used a different list (VS Code, Playwright, TypeORM, date-fns, tRPC, rxjs). The 7.0 list is the more recent one and the one tied to the GA build being measured.

The 7.0 post states no commits, commands, run counts, or hardware. Commit selection is therefore ours and needs a rule that is fixed before any commit is looked at.

**[DECISION, CONFIRMED 2026-10-14] Commit rule:** for each repository, the first commit in the first-parent history of its default branch whose committer date is before 2026-07-08T00:00:00Z (the day TypeScript 7.0 GA was announced). Mechanically:

```
git rev-list --first-parent -1 --before=2026-07-08T00:00:00Z <default branch>
```

Rationale: a mechanical rule that does not depend on our results, and close to the state Microsoft benchmarked. Resolved 2026-10-14; for each repository the next first-parent commit was checked to be dated after the cutoff.

| Name | Repository | Default branch | Commit | Committer date | License | Vendored |
|---|---|---|---|---|---|---|
| vscode | microsoft/vscode | main | `f507e492b81074fff62b0806d47760671c3990b5` | 2026-07-07T23:59:34Z | MIT | no |
| sentry | getsentry/sentry | master | `b4e1ddf51f55e4aa973b2d0c028485f13de779a6` | 2026-07-07T23:34:11Z | non-OSI (GitHub reports NOASSERTION) | no |
| bluesky | bluesky-social/social-app | main | `ea3d6827c75c2c2063bdb8d3240c62d8f041c7fa` | 2026-07-07T17:23:03Z | MIT | no |
| playwright | microsoft/playwright | main | `a061d963659048a43169bd87c84960d89f496cdb` | 2026-07-07T18:49:33Z | Apache-2.0 | no |
| tldraw | tldraw/tldraw | main | `9986114e3493118095f60146becd0219e100745e` | 2026-07-07T19:59:52Z | non-OSI (GitHub reports NOASSERTION) | no |

None of these is redistributed. They are fetched at the pinned commit by `scripts/fetch-references.mjs`. The two non-OSI licenses are a reason not to vendor, not an obstacle to measuring.

## 4. Licensing obligations

Both upstreams are Apache-2.0. Obligations and how they are met:

| Apache-2.0 | Obligation | Where |
|---|---|---|
| §4(a) | Give recipients a copy of the license | [LICENSE](../LICENSE); upstream copies in `licenses/` |
| §4(b) | Modified files carry a prominent change notice | Per-file header, see [CONTRIBUTING.md](../CONTRIBUTING.md) |
| §4(c) | Retain copyright and attribution notices | Per-file header (`Portions Copyright (c) Microsoft Corporation.`) |
| §4(d) | Carry the upstream NOTICE attributions | [NOTICE](../NOTICE); `licenses/typescript-go/NOTICE.txt` verbatim |
| §6 | No trademark grant | Trademark statement in NOTICE and README |

Upstream notice files, copied unmodified. The SHA-256 equals that of the upstream git blob at the pinned commit (the upstream files use CRLF; `.gitattributes` marks `licenses/**` as `-text` so line endings are never converted):

| File | SHA-256 |
|---|---|
| `licenses/TypeScript/LICENSE.txt` | `a7d00bfd54525bc694b6e32f64c7ebcf5e6b7ae3657be5cc12767bce74654a47` |
| `licenses/TypeScript/ThirdPartyNoticeText.txt` | `1af3c68039c57e539422da82a4faada506ce6d0ea6f90e0b699d02dbcdb7a90c` |
| `licenses/typescript-go/LICENSE` | `a7d00bfd54525bc694b6e32f64c7ebcf5e6b7ae3657be5cc12767bce74654a47` |
| `licenses/typescript-go/NOTICE.txt` | `f5c708b59114507b8b27b48181b6883d106bbca0c1634bbee45b5e344237b66b` |

The two upstream LICENSE files are byte-identical. The repository's own [LICENSE](../LICENSE) is the standard Apache-2.0 text in a different line layout; the terms are the same.

Third-party material that a port inherits:

- **Unicode data** (scanner identifier tables, Phase 1). Covered by the Unicode, Inc. license in `ThirdPartyNoticeText.txt`. This is the only third-party notice that applies to Phase 1.
- **DOM / WebGL / Web Background Sync IDL, DefinitelyTyped** apply to `src/lib/*.d.ts`. These become relevant only when the lib files are used (checker conformance, Phase 3). They are already carried via the verbatim copies.
- The Go module dependencies listed in typescript-go's NOTICE.txt (golang.org/x/*) are not ported and do not apply.

**[DECISION] `tests/cases/**` and baselines are not vendored.** They are fetched at the pinned commit like the source. Permitted under Apache-2.0 either way; not vendoring keeps the repository small and makes the pin the only version that can be tested against.

**[DECISION, CONFIRMED 2026-10-14] DCO, not CLA.** See CONTRIBUTING.md. Caveat: a DCO certifies origin; it does not grant the maintainer any rights beyond Apache-2.0 itself. Apache-2.0 §2 already allows sublicensing, so the project can always ship contributions under Apache-2.0, but relicensing under different terms would need a CLA. If "keeping license control" means the ability to relicense, a CLA is required instead.

## 5. Toolchain

Pinned in [pins.json](../pins.json) (`tools`) and [global.json](../global.json); installed into `.tools/` by `node scripts/install-tools.mjs`, which verifies each archive's SHA-256 before extracting. Nothing is installed system-wide.

| Tool | Version | Source | Purpose |
|---|---|---|---|
| .NET SDK | 10.0.402 (runtime 10.0.13) | global.json, `rollForward: disable` | C# port, BenchmarkDotNet |
| Go | 1.26.9 | go.dev/dl (official archives) | Building the typescript-go AST dumper |
| hyperfine | 1.20.0 | github.com/sharkdp/hyperfine releases | CLI wall-clock measurement |

- **Go 1.26.9:** typescript-go 7.0.2 declares `go 1.26`; 1.26.9 is the latest 1.26 patch release as of 2026-10-14. typescript-go's own CI installs Microsoft's Go distribution (`.github/actions/setup-go`, `go1.26`). That matters only for the dumper, which measures correctness, not speed. The benchmark uses Microsoft's shipped npm binary (`@typescript/typescript-<platform>@7.0.2`), so no Go toolchain of ours is involved in any timing.
- **[DECISION] hyperfine 1.20.0 (2025-11-18), not 1.21.0 (2026-10-05) or 2.0.0 (2026-10-07).** The newer releases were 9 and 7 days old at pin time, and 2.0.0 is a new major version whose JSON export may change. 1.20.0 has been stable for 11 months.
- **[DECISION] .NET SDK 10.0.402 (runtime 10.0.13) with `rollForward: disable`.** The latest .NET 10 servicing release as of 2026-10-14 (released 2026-10-13, per builds.dotnet.microsoft.com release metadata). Exact pinning means a machine without this SDK fails loudly instead of silently measuring a different runtime. Before the pre-registration tag, re-check for a newer 10.0.x servicing release; a re-pin after the tag is an amendment.
- **NativeAOT on Windows** links with MSVC `link.exe`. The ILCompiler package (10.0.13) locates it via `vswhere -latest -prerelease`, which on the reference machine selects Visual Studio 18 Insiders (MSVC 14.51.36231), not the installed Build Tools 2019. That `vcvarsall.bat` calls `vswhere.exe` by name, so `C:\Program Files (x86)\Microsoft Visual Studio\Installer` must be on `PATH`, otherwise the publish fails with MSB3073. Requirement for the harness (not yet built): set this explicitly and record the linker version with each NativeAOT result. Git Bash drops the `ProgramFiles(x86)` variable, so NativeAOT builds must run from PowerShell or cmd. Code generation is done by ILC, not the linker; the linker version is recorded for completeness, not because it is expected to affect timings.
