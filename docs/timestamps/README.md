# Timestamps

OpenTimestamps proofs for the pre-registration tags (pre-registration §11.1).

| Tag | Stamp file | Proof | Submitted | Bitcoin attestation |
|---|---|---|---|---|
| `prereg/v1` | `prereg-v1.txt` | `prereg-v1.txt.ots` | 2026-10-21, to four public calendars (a/b.pool.opentimestamps.org, a.pool.eternitywall.com, ots.btc.catallaxy.com) | block 972458 (proof upgraded 2026-10-21) |
| `prereg/v2` | `prereg-v2.txt` | `prereg-v2.txt.ots` | 2026-10-21, same four calendars | blocks 972468, 972470, 972471 (proof upgraded 2026-10-21) |

Each stamp file lists the tag, the tagged commit and tree, and the SHA-256 of the protocol documents and frozen inputs at that commit. The proof covers the stamp file; the hashes inside it tie the proof to the commit.

Commands (Windows workaround included, see `scripts/ots.mjs`):

```
node scripts/ots.mjs upgrade docs/timestamps/prereg-vN.txt.ots   # once the Bitcoin attestation exists (hours after stamping)
node scripts/ots.mjs verify  docs/timestamps/prereg-vN.txt.ots   # needs a Bitcoin node, or check the block merkle root shown by `ots info` against a block explorer
```

To check the hashes against the repository: `git show <tagged commit>:<path> | sha256sum`.
