# Timestamps

OpenTimestamps proofs for the pre-registration tags (pre-registration §11.1).

| Tag | Stamp file | Proof | Submitted |
|---|---|---|---|
| `prereg/v1` | `prereg-v1.txt` | `prereg-v1.txt.ots` | 2026-10-21, to four public calendars (a/b.pool.opentimestamps.org, a.pool.eternitywall.com, ots.btc.catallaxy.com) |

Each stamp file lists the tag, the tagged commit and tree, and the SHA-256 of the protocol documents and frozen inputs at that commit. The proof covers the stamp file; the hashes inside it tie the proof to the commit.

Commands (Windows workaround included, see `scripts/ots.mjs`):

```
node scripts/ots.mjs upgrade docs/timestamps/prereg-v1.txt.ots   # once the Bitcoin attestation exists (hours after stamping)
node scripts/ots.mjs verify  docs/timestamps/prereg-v1.txt.ots
```

To check the hashes against the repository: `git show <tagged commit>:<path> | sha256sum`.
