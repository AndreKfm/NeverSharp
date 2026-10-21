# Contributing

## License of contributions

NeverSharp is licensed under the Apache License 2.0 ([LICENSE](LICENSE)).
Contributions are accepted under the same license (inbound = outbound).

## Developer Certificate of Origin

Every commit must be signed off under the [Developer Certificate of Origin 1.1](DCO):

```
Signed-off-by: Full Name <email@example.com>
```

`git commit -s` adds the line. Commits without a sign-off are not merged.

## Header for ported files

Every C# file that is translated from upstream source starts with this header.
The commit is the full SHA from [pins.json](pins.json); one `Ported from` line
per upstream file.

```csharp
// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/scanner.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/scanner/scanner.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.
```

- The `Port structure follows` line is required when the corresponding Go file
  was used as a reference for the translation (it is part of the model inputs
  in the multi-model workflow, so in practice: always for ported modules).
- The `Modified:` line is the change notice required by Apache 2.0, section 4(b).
- Files written from scratch carry only the copyright and SPDX lines. Under `src/`,
  where everything is expected to be ported, such a file must say so explicitly with
  `// Original code (not ported): <reason>` as its third line.
- `node scripts/check-headers.mjs` checks every file under `src/` (part of Gate 2).

## Reference checkouts

Upstream sources are never committed. `node scripts/fetch-references.mjs`
fetches every pinned reference into `external/` and verifies the commit.
