# NeverSharp

A faithful port of the TypeScript compiler to C#, measured against typescript-go under a pre-registered protocol.

Status: Phase 0 (harness, corpus, baselines, pre-registration). No ported code yet.

## Upstream references

| | Repository | Pinned |
|---|---|---|
| Port source, oracle 1 | [microsoft/TypeScript](https://github.com/microsoft/TypeScript) | `v6.0.3` @ `050880ce59e30b356b686bd3144efe24f875ebc8` |
| Baseline, oracle 2 | [microsoft/typescript-go](https://github.com/microsoft/typescript-go) | `typescript/v7.0.2` @ `2bd066d87f5bafd315be9f40889d0a60b9e58e0b` |

Details, provenance and known differences between the two: [docs/references.md](docs/references.md). Fetch them with `node scripts/fetch-references.mjs`.

## License

Apache License 2.0, see [LICENSE](LICENSE) and [NOTICE](NOTICE). Contains code ported from TypeScript and typescript-go, Copyright (c) Microsoft Corporation, Apache License 2.0.

"TypeScript" is a trademark of Microsoft Corporation. This project is not affiliated with, sponsored by, or endorsed by Microsoft Corporation.
