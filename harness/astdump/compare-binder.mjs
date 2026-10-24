// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Gate B (phase2-plan.md): compares Track O's binder dump with tsc 6.0.3's (dump-ts-binder.mjs), byte for byte, unit
// by unit, optionally only some sections. Outcomes (ok / timeout / error) must agree too.
//
//   expected: <corpus>/dumps/ts-binder-<mode>           (or --expected <dir>)
//   actual:   <corpus>/dumps/cs-optimized-binder-<mode> (or --actual <dir>)
//   outcomes: <dir>.outcomes.json next to each
//
// Usage: node compare-binder.mjs [--corpus <dir>] [--jsdoc-mode all|none|types] [--expected <dir>] [--actual <dir>]
//                                [--sections symbols,flow,diagnostics] [--show 10]
// Prints the first differing line of the first --show units, then a JSON summary.
// Exit code 0 only if every unit is identical in the selected sections.

import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { artifactsDir } from "./common.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const corpus = arg("--corpus", join(artifactsDir, "equivalence"));
const mode = arg("--jsdoc-mode", "all");
if (!["all", "none", "types"].includes(mode)) throw new Error("--jsdoc-mode must be all, none or types");
const allSections = ["symbols", "flow", "diagnostics"];
const sections = arg("--sections", allSections.join(",")).split(",");
for (const s of sections) if (!allSections.includes(s)) throw new Error(`unknown section ${s}`);
const expectedDir = arg("--expected", join(corpus, "dumps", `ts-binder-${mode}`));
const actualDir = arg("--actual", join(corpus, "dumps", `cs-optimized-binder-${mode}`));
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

// The selected sections of a dump, header lines included; the whole text when all are selected.
const select = text => {
    if (sections.length === allSections.length) return text;
    let out = "", keep = false;
    for (const line of text.split("\n")) {
        if (line.startsWith("== ")) keep = sections.includes(line.slice(3));
        if (keep) out += line + "\n";
    }
    return out;
};

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
    const expected = select(readFileSync(join(expectedDir, `${unit.id}.txt`), "utf8"));
    const actualFile = join(actualDir, `${unit.id}.txt`);
    const actual = existsSync(actualFile) ? select(readFileSync(actualFile, "utf8")) : "";
    if (expected === actual) { identical++; continue; }
    differing.push(unit.id);
    if (shown++ < show) {
        const el = expected.split("\n"), al = actual.split("\n");
        let i = 0;
        while (i < el.length && i < al.length && el[i] === al[i]) i++;
        console.log(`unit ${unit.id} (${unit.test} :: ${unit.unit}), line ${i + 1}:`);
        if (i > 0) console.log(`  same:     ${el[i - 1]}`);
        console.log(`  tsc:      ${el[i] ?? "<end>"}`);
        console.log(`  C#:       ${al[i] ?? "<end>"}`);
    }
}
const summary = { mode, sections, units: units.length, identical, differing: differing.length, outcomeMismatch: outcomeMismatch.length,
    firstDiffering: differing.slice(0, 50), firstOutcomeMismatch: outcomeMismatch.slice(0, 20) };
console.log(JSON.stringify(summary));
process.exit(differing.length === 0 && outcomeMismatch.length === 0 ? 0 : 1);
