// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Gate G1a (phase1-plan.md §4): compares a C# token-stream dump with the tsc 6.0.3
// dump (harness/astdump/dump-tokens-ts.mjs) byte for byte, unit by unit. No
// normalization and no divergence list: the C# scanner must reproduce tsc exactly.
//
// Usage: node compare-tokens.mjs [--expected <dir>] [--actual <dir>] [--corpus <dir>] [--show 10]
//   defaults: <corpus>/dumps/ts-tokens and <corpus>/dumps/cs-faithful-tokens
// Prints the first differing line of the first --show units, then a JSON summary.
// Exit code 0 only if every unit is identical.

import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { artifactsDir } from "./common.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const corpus = arg("--corpus", join(artifactsDir, "equivalence"));
const expectedDir = arg("--expected", join(corpus, "dumps", "ts-tokens"));
const actualDir = arg("--actual", join(corpus, "dumps", "cs-faithful-tokens"));
const show = Number(arg("--show", "10"));

const { units } = JSON.parse(readFileSync(join(corpus, "manifest.json"), "utf8"));
let identical = 0, missing = 0;
const differing = [];
for (const unit of units) {
    const actualFile = join(actualDir, `${unit.id}.txt`);
    if (!existsSync(actualFile)) { missing++; continue; }
    const expected = readFileSync(join(expectedDir, `${unit.id}.txt`));
    const actual = readFileSync(actualFile);
    if (expected.equals(actual)) { identical++; continue; }
    differing.push(unit);
    if (differing.length <= show) {
        const e = expected.toString("utf8").split("\n"), a = actual.toString("utf8").split("\n");
        let i = 0;
        while (i < e.length && i < a.length && e[i] === a[i]) i++;
        console.log(`unit ${unit.id} (${unit.test} :: ${unit.unit}), line ${i + 1}:`);
        if (i > 0) console.log(`  same:     ${e[i - 1]}`);
        console.log(`  tsc:      ${e[i] ?? "<end>"}`);
        console.log(`  C#:       ${a[i] ?? "<end>"}`);
    }
}
const summary = { units: units.length, identical, differing: differing.length, missing, firstDiffering: differing.slice(0, 50).map(u => u.id) };
console.log(JSON.stringify(summary));
process.exit(differing.length === 0 && missing === 0 ? 0 : 1);
