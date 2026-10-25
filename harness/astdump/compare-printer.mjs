// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Printer gate P (phase3-plan.md, module P1): compares Track O's printer dump with tsc 6.0.3's (dump-ts-printer.mjs),
// byte for byte, unit by unit. Outcomes (ok / timeout / error) must agree too.
//
//   expected: <corpus>/dumps/ts-printer-<mode>           (or --expected <dir>)
//   actual:   <corpus>/dumps/cs-optimized-printer-<mode> (or --actual <dir>)
//   outcomes: <dir>.outcomes.json next to each
//
// Usage: node compare-printer.mjs [--corpus <dir>] [--jsdoc-mode all|none|types] [--expected <dir>] [--actual <dir>] [--show 10]
// Prints the first differing line of the first --show units, then a JSON summary (identical / differing units).
// Exit code 0 only if every unit is identical.

import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { artifactsDir } from "./common.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const corpus = arg("--corpus", join(artifactsDir, "equivalence"));
const mode = arg("--jsdoc-mode", "types");
if (!["all", "none", "types"].includes(mode)) throw new Error("--jsdoc-mode must be all, none or types");
const expectedDir = arg("--expected", join(corpus, "dumps", `ts-printer-${mode}`));
const actualDir = arg("--actual", join(corpus, "dumps", `cs-optimized-printer-${mode}`));
const show = Number(arg("--show", "10"));

const outcomesOf = file => {
    const o = JSON.parse(readFileSync(file, "utf8"));
    const map = new Map();
    for (const id of o.ok) map.set(id, "ok");
    for (const id of o.timeout) map.set(id, "timeout");
    for (const e of o.error) map.set(e.id, "error");
    return map;
};
const expectedOutcomes = outcomesOf(`${expectedDir}.outcomes.json`);
const actualOutcomes = outcomesOf(`${actualDir}.outcomes.json`);

const { units } = JSON.parse(readFileSync(join(corpus, "manifest.json"), "utf8"));
let identical = 0, shown = 0;
const differing = [], outcomeMismatch = [];
for (const unit of units) {
    const e = expectedOutcomes.get(unit.id), a = actualOutcomes.get(unit.id) ?? "missing";
    if (e !== a) {
        outcomeMismatch.push({ id: unit.id, tsc: e, cs: a });
        if (shown++ < show) console.log(`unit ${unit.id} (${unit.test} :: ${unit.unit}): outcome tsc ${e}, C# ${a}`);
        continue;
    }
    if (e !== "ok") { identical++; continue; }
    const expected = readFileSync(join(expectedDir, `${unit.id}.txt`));
    const actualFile = join(actualDir, `${unit.id}.txt`);
    const actual = existsSync(actualFile) ? readFileSync(actualFile) : Buffer.alloc(0);
    if (expected.equals(actual)) { identical++; continue; }
    differing.push(unit.id);
    if (shown++ < show) {
        const el = expected.toString("utf8").split("\n"), al = actual.toString("utf8").split("\n");
        let i = 0;
        while (i < el.length && i < al.length && el[i] === al[i]) i++;
        console.log(`unit ${unit.id} (${unit.test} :: ${unit.unit}), line ${i + 1}:`);
        if (i > 0) console.log(`  same:     ${JSON.stringify(el[i - 1])}`);
        console.log(`  tsc:      ${i < el.length ? JSON.stringify(el[i]) : "<end>"}`);
        console.log(`  C#:       ${i < al.length ? JSON.stringify(al[i]) : "<end>"}`);
    }
}
const summary = { mode, units: units.length, identical, differing: differing.length, outcomeMismatch: outcomeMismatch.length,
    firstDiffering: differing.slice(0, 50), firstOutcomeMismatch: outcomeMismatch.slice(0, 20) };
console.log(JSON.stringify(summary));
process.exit(differing.length === 0 && outcomeMismatch.length === 0 ? 0 : 1);
