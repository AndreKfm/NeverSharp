# Timestamps

OpenTimestamps proofs for the pre-registration tags (pre-registration §11.1).

| Tag | Stamp file | Proof | Submitted | Bitcoin attestation |
|---|---|---|---|---|
| `prereg/v1` | `prereg-v1.txt` | `prereg-v1.txt.ots` | 2026-10-21, to four public calendars (a/b.pool.opentimestamps.org, a.pool.eternitywall.com, ots.btc.catallaxy.com) | block 972458 (proof upgraded 2026-10-21) |
| `prereg/v2` | `prereg-v2.txt` | `prereg-v2.txt.ots` | 2026-10-21, same four calendars | blocks 972468, 972470, 972471 (proof upgraded 2026-10-21) |
| `prereg/v3` | `prereg-v3.txt` | `prereg-v3.txt.ots` | 2026-10-21, same four calendars | blocks 972505,972524 (proof upgraded 2026-10-22) |
| `prereg/v4` | `prereg-v4.txt` | `prereg-v4.txt.ots` | 2026-10-22, same four calendars | blocks 972585,972587 (proof upgraded 2026-10-22) |
| `prereg/v5` | `prereg-v5.txt` | `prereg-v5.txt.ots` | 2026-10-22, same four calendars | blocks 972607,972609 (proof upgraded 2026-10-22) |
| `prereg/v6` | `prereg-v6.txt` | `prereg-v6.txt.ots` | 2026-10-22, same four calendars | upgraded 2026-10-24 (Bitcoin attestation) |
| `prereg/v7` | `prereg-v7.txt` | `prereg-v7.txt.ots` | 2026-10-24, same four calendars | upgraded 2026-10-25 (Bitcoin attestation) |
| `prereg/v8` | `prereg-v8.txt` | `prereg-v8.txt.ots` | 2026-10-24, same four calendars | upgraded 2026-10-25 (Bitcoin attestation) |
| `prereg/v9` | `prereg-v9.txt` | `prereg-v9.txt.ots` | 2026-10-29, same four calendars | upgraded 2026-10-29 (Bitcoin attestation) |
| `prereg/v10` | `prereg-v10.txt` | `prereg-v10.txt.ots` | 2026-11-02, same four calendars | blocks 974221,974225 (proof upgraded 2026-11-02) |
| `prereg/v11` | `prereg-v11.txt` | `prereg-v11.txt.ots` | 2026-11-03, same four calendars | block 974431 (proof upgraded 2026-11-04) |

Each stamp file lists the tag, the tagged commit and tree, and the SHA-256 of the protocol documents and frozen inputs at that commit. The proof covers the stamp file; the hashes inside it tie the proof to the commit.

Commands (Windows workaround included, see `scripts/ots.mjs`):

```
node scripts/ots.mjs upgrade docs/timestamps/prereg-vN.txt.ots   # once the Bitcoin attestation exists (hours after stamping)
node scripts/ots.mjs verify  docs/timestamps/prereg-vN.txt.ots   # needs a Bitcoin node, or check the block merkle root shown by `ots info` against a block explorer
```

To check the hashes against the repository: `git show <tagged commit>:<path> | sha256sum`.
