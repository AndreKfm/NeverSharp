// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Gate C (phase3-plan.md, module C0): compares Track O's checker dump with tsc 6.0.3's (dump-ts-checker.mjs), byte for
// byte, unit by unit, optionally only some sections. A dump's header (the lines before the first section) belongs to
// every selection. Outcomes (ok / timeout / error) must agree too.
//
//   expected: <corpus>/dumps/ts-checker-<mode>           (or --expected <dir>)
//   actual:   <corpus>/dumps/cs-optimized-checker-<mode> (or --actual <dir>)
//   outcomes: <dir>.outcomes.json next to each
//
// Usage: node compare-checker.mjs [--corpus <dir>] [--jsdoc-mode types|all|none] [--expected <dir>] [--actual <dir>]
//                                 [--sections diagnostics,types,symbols] [--units <file>] [--show 10]
//   --units: compare only these units (one id per line; e.g. a sample); default: every unit of the manifest
// Prints the first differing line of the first --show units, then a JSON summary: units identical / differing /
// crashed (the C# writer failed or timed out where tsc did not) / outcome mismatches of other kinds, and per selected
// section the number of units identical in that section (header included), and whether the library diagnostics
// (<dir>.lib.txt) are identical. Exit code 0 only if every unit is identical in the selected sections and the library
// diagnostics do not differ.

import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { artifactsDir } from "./common.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const corpus = arg("--corpus", join(artifactsDir, "equivalence"));
const mode = arg("--jsdoc-mode", "types");
if (!["all", "none", "types"].includes(mode)) throw new Error("--jsdoc-mode must be all, none or types");
const allSections = ["diagnostics", "types", "symbols"];
const sections = arg("--sections", allSections.join(",")).split(",");
for (const s of sections) if (!allSections.includes(s)) throw new Error(`unknown section ${s}`);
const expectedDir = arg("--expected", join(corpus, "dumps", `ts-checker-${mode}`));
const actualDir = arg("--actual", join(corpus, "dumps", `cs-optimized-checker-${mode}`));
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

// The header and the given sections of a dump.
const select = (text, keepSections) => {
    let out = "", keep = true;
    for (const line of text.split("\n")) {
        if (line.startsWith("== ")) keep = keepSections.includes(line.slice(3));
        if (keep) out += line + "\n";
    }
    return out;
};

let { units } = JSON.parse(readFileSync(join(corpus, "manifest.json"), "utf8"));
const unitsFile = arg("--units", undefined);
if (unitsFile) {
    const ids = new Set(readFileSync(unitsFile, "utf8").split(/\r?\n/).filter(Boolean));
    units = units.filter(u => ids.has(u.id));
}
let identical = 0, shown = 0;
const differing = [], crashed = [], outcomeMismatch = [];
const perSection = Object.fromEntries(sections.map(s => [s, 0]));
for (const unit of units) {
    const e = expectedOutcomes.get(unit.id), a = actualOutcomes.get(unit.id) ?? "missing";
    if (e !== a) {
        (e === "ok" ? crashed : outcomeMismatch).push({ id: unit.id, tsc: e, cs: a });
        if (shown++ < show) console.log(`unit ${unit.id} (${unit.test} :: ${unit.unit}): outcome tsc ${e}, C# ${a}`);
        continue;
    }
    if (e !== "ok") { identical++; for (const s of sections) perSection[s]++; continue; }
    const expectedText = readFileSync(join(expectedDir, `${unit.id}.txt`), "utf8");
    const actualFile = join(actualDir, `${unit.id}.txt`);
    const actualText = existsSync(actualFile) ? readFileSync(actualFile, "utf8") : "";
    for (const s of sections) if (select(expectedText, [s]) === select(actualText, [s])) perSection[s]++;
    const expected = select(expectedText, sections), actual = select(actualText, sections);
    if (expected === actual) { identical++; continue; }
    differing.push(unit.id);
    if (shown++ < show) {
        const el = expected.split("\n"), al = actual.split("\n");
        let i = 0;
        while (i < el.length && i < al.length && el[i] === al[i]) i++;
        console.log(`unit ${unit.id} (${unit.test} :: ${unit.unit}), line ${i + 1}:`);
        if (i > 0) console.log(`  same:     ${el[i - 1].slice(0, 300)}`);
        console.log(`  tsc:      ${(el[i] ?? "<end>").slice(0, 300)}`);
        console.log(`  C#:       ${(al[i] ?? "<end>").slice(0, 300)}`);
    }
}
// The library's own diagnostics (<dir>.lib.txt, written once per run): compared when both exist, reported apart.
const libText = dir => existsSync(`${dir}.lib.txt`) ? readFileSync(`${dir}.lib.txt`, "utf8") : undefined;
const expectedLib = libText(expectedDir), actualLib = libText(actualDir);
const library = expectedLib === undefined || actualLib === undefined ? "missing" : expectedLib === actualLib ? "identical" : "differing";
if (library === "differing") {
    const el = expectedLib.split("\n"), al = actualLib.split("\n");
    let i = 0;
    while (i < el.length && i < al.length && el[i] === al[i]) i++;
    console.log(`library, line ${i + 1}:\n  tsc:      ${(el[i] ?? "<end>").slice(0, 300)}\n  C#:       ${(al[i] ?? "<end>").slice(0, 300)}`);
}
const summary = { mode, sections, units: units.length, identical, differing: differing.length, crashed: crashed.length,
    outcomeMismatch: outcomeMismatch.length, identicalPerSection: perSection, library,
    firstDiffering: differing.slice(0, 50), firstCrashed: crashed.slice(0, 20), firstOutcomeMismatch: outcomeMismatch.slice(0, 20) };
console.log(JSON.stringify(summary));
process.exit(differing.length === 0 && crashed.length === 0 && outcomeMismatch.length === 0 && library !== "differing" ? 0 : 1);
