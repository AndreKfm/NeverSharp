// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O seed (step O0, phase1-plan.md §7).
//
// Track O starts from the finished Track F modules (they pass Gate 1) and transforms them step by
// step. This copies the hand-written Track F sources (Core/, Scanner/, Factory/, Parser/) into
// src/NeverSharp.Optimized/ with the namespace changed; the node classes come from the generator
// (generate.mjs emits them for both tracks until step O6 replaces Track O's with the node arena).
// File headers are kept: the copies are still ports of the same TypeScript sources.
//
// Run once to seed Track O; refuses to overwrite existing Track O sources.
// Usage: node tools/NeverSharp.Generate/seed-optimized.mjs

import { cpSync, existsSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { repoRoot } from "./common.mjs";

const from = join(repoRoot, "src", "NeverSharp.Faithful");
const to = join(repoRoot, "src", "NeverSharp.Optimized");
const dirs = ["Core", "Scanner", "Factory", "Parser"];
for (const d of dirs) if (existsSync(join(to, d))) throw new Error(`src/NeverSharp.Optimized/${d} exists; the seed is only made once`);

let files = 0;
for (const d of dirs) {
    cpSync(join(from, d), join(to, d), { recursive: true });
    for (const name of readdirSync(join(to, d))) {
        if (!name.endsWith(".cs")) continue;
        const file = join(to, d, name);
        const text = readFileSync(file, "utf8")
            .replaceAll("namespace NeverSharp.Faithful", "namespace NeverSharp.Optimized")
            .replaceAll("using NeverSharp.Faithful", "using NeverSharp.Optimized")
            .replaceAll("NeverSharp.Faithful.", "NeverSharp.Optimized.");
        writeFileSync(file, text);
        files++;
    }
}
console.log(`Track O seed: ${files} files copied from Track F`);
