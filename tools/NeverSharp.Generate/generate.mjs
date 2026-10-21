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
import { emitFaithful } from "./emit-faithful.mjs";
import { extractSchema } from "./schema.mjs";

mkdirSync(outDir, { recursive: true });
console.log(`diagnostic messages: ${generateDiagnostics()}`);
console.log(`enums: ${generateEnums()}`);
const faithful = emitFaithful(extractSchema());
console.log(`track F nodes: ${faithful.classes} classes, ${faithful.fields} fields, ${faithful.forEachChildEntries} forEachChild entries${faithful.collisions.length ? `, renamed (name = class): ${faithful.collisions.join(", ")}` : ""}`);
