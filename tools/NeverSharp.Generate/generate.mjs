// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): entry point of the code generator (phase1-plan.md §3).
//
// Writes src/Generated/*.g.cs, compiled into both tracks (they share generated
// data, no runtime code). Requires the pinned TypeScript checkout
// (scripts/fetch-references.mjs) and harness/astdump's npm install.
//
// Usage: node tools/NeverSharp.Generate/generate.mjs

import { mkdirSync } from "node:fs";
import { outDir } from "./common.mjs";
import { generateDiagnostics } from "./diagnostics.mjs";
import { generateEnums } from "./enums.mjs";

mkdirSync(outDir, { recursive: true });
console.log(`diagnostic messages: ${generateDiagnostics()}`);
console.log(`enums: ${generateEnums()}`);
