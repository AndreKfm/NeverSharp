// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Gate D (phase4-plan.md, module D0): compares Track O's emit dumps with tsc 6.0.3's (dump-ts-emit.mjs), unit by unit,
// per section:
//   js           the outputs with a JavaScript extension (.js, .jsx, .mjs, .cjs): names, order and texts
//   dts          the outputs with a declaration extension (.d.ts, .d.mts, .d.cts)
//   diagnostics  the emit diagnostics and emitSkipped
// A unit is identical when its whole dump is byte-identical after the symbol id normalization below; crashed: the C#
// side has no dump (error or timeout) while tsc has one. Outcomes (ok / timeout / error) must agree too.
//
// Symbol id normalization (module D6, author's decision 2026-11-03; preregistration.md §19): tsc builds some names
// from its process-wide symbol id (getSymbolId), which depends on how many symbols the oracle's worker numbered before
// the unit, not on the unit. Before comparing, both sides replace that id with "<id>" in exactly these names:
//   - the late-bound member name of a unique symbol, `__@<escapedName>@<id>` (createUniqueESSymbolType);
//   - the local name serialization derives from it (getNameCandidateWorker: "_" + the name with every character
//     other than [a-z0-9] (case-insensitive) replaced by "_", i.e. `____<name>_<id>`), with the optional `_<n>` suffix
//     of getUnusedName.
// The summary counts the units and lines this changes on the tsc side.
//
//   expected: <corpus>/dumps/ts-emit-types           (or --expected <dir>)
//   actual:   <corpus>/dumps/cs-optimized-emit-types (or --actual <dir>)
//
// Usage: node compare-emit.mjs [--corpus <dir>] [--expected <dir>] [--actual <dir>] [--units <file>] [--show 10]
//        [--coverage <coverage.json>]   also counts the units that need no transformer (dump-ts-emit.mjs coverage report)
// Prints the first differing line of the first --show differing units per section, then a JSON summary.
// Exit code 0 only if every unit is identical.

import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { artifactsDir } from "./common.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const corpus = arg("--corpus", join(artifactsDir, "equivalence"));
const expectedDir = arg("--expected", join(corpus, "dumps", "ts-emit-types"));
const actualDir = arg("--actual", join(corpus, "dumps", "cs-optimized-emit-types"));
const show = Number(arg("--show", "10"));
const coverageFile = arg("--coverage", undefined);

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

let { units } = JSON.parse(readFileSync(join(corpus, "manifest.json"), "utf8"));
const unitsFile = arg("--units", undefined);
if (unitsFile) {
    const ids = new Set(readFileSync(unitsFile, "utf8").split(/\r?\n/).filter(Boolean));
    units = units.filter(u => ids.has(u.id));
}

// The symbol id normalization (see the header). The escaped name of a unique symbol's symbol is an identifier here
// (`[\w$]*`); the derived local name has only [A-Za-z0-9_] by construction.
const lateBoundName = /(?<![\w$])__@([\w$]*)@\d+(?![\w$])/g;
const derivedLocalName = /(?<![\w$])____([A-Za-z0-9_]*?)_\d+((?:_\d+)?)(?![\w$])/g;
const normalizeSymbolIds = text => text.replace(lateBoundName, "__@$1@<id>").replace(derivedLocalName, "____$1_<id>$2");
let normalizedUnits = 0, normalizedLines = 0;
function normalizeExpected(text) {
    const result = normalizeSymbolIds(text);
    if (result !== text) {
        normalizedUnits++;
        const before = text.split("\n"), after = result.split("\n");
        for (let i = 0; i < before.length; i++) if (before[i] !== after[i]) normalizedLines++;
    }
    return result;
}

const isJs = name => /\.(js|jsx|mjs|cjs)$/.test(name) && !/\.d\.[cm]?ts$/.test(name);
const isDts = name => /\.d\.[cm]?ts$/.test(name);

// The sections of a dump: js and dts as the concatenated "output" blocks of their kind, diagnostics as the rest.
function sectionsOf(text) {
    const lines = text.split("\n");
    const result = { header: [], js: [], dts: [], other: [], diagnostics: [] };
    let current = result.header;
    for (const line of lines) {
        if (line === "== outputs") { current = result.other; continue; }
        if (line === "== diagnostics" || line === "== emitSkipped") { current = result.diagnostics; current.push(line); continue; }
        if (line.startsWith("output ")) {
            const name = line.slice("output ".length);
            current = isJs(name) ? result.js : isDts(name) ? result.dts : result.other;
        }
        current.push(line);
    }
    return result;
}

const sectionNames = ["js", "dts", "diagnostics"];
const counts = Object.fromEntries(sectionNames.map(s => [s, { identical: 0, differing: 0, crashed: 0 }]));
const firstDiffering = Object.fromEntries(sectionNames.map(s => [s, []]));
let identical = 0, differingUnits = 0, crashedUnits = 0, otherDiffering = 0;
const outcomeMismatch = [];
const shown = Object.fromEntries(sectionNames.map(s => [s, 0]));
const identicalIds = new Set();

for (const unit of units) {
    const e = expectedOutcomes.get(unit.id), a = actualOutcomes.get(unit.id) ?? "missing";
    if (e !== "ok") {
        if (e !== a) outcomeMismatch.push({ id: unit.id, tsc: e, cs: a });
        else { identical++; identicalIds.add(unit.id); }
        continue;
    }
    const actualFile = join(actualDir, `${unit.id}.txt`);
    if (a !== "ok" || !existsSync(actualFile)) {
        crashedUnits++;
        for (const s of sectionNames) counts[s].crashed++;
        if (a !== e) outcomeMismatch.push({ id: unit.id, tsc: e, cs: a });
        continue;
    }
    const expected = normalizeExpected(readFileSync(join(expectedDir, `${unit.id}.txt`), "utf8"));
    const actual = normalizeSymbolIds(readFileSync(actualFile, "utf8"));
    if (expected === actual) {
        identical++;
        identicalIds.add(unit.id);
        for (const s of sectionNames) counts[s].identical++;
        continue;
    }
    differingUnits++;
    const es = sectionsOf(expected), as = sectionsOf(actual);
    if (es.header.join("\n") !== as.header.join("\n") || es.other.join("\n") !== as.other.join("\n")) otherDiffering++;
    for (const s of sectionNames) {
        const el = es[s], al = as[s];
        if (el.join("\n") === al.join("\n")) { counts[s].identical++; continue; }
        counts[s].differing++;
        if (firstDiffering[s].length < 50) firstDiffering[s].push(unit.id);
        if (shown[s]++ < show) {
            let i = 0;
            while (i < el.length && i < al.length && el[i] === al[i]) i++;
            console.log(`[${s}] unit ${unit.id} (${unit.test} :: ${unit.unit}), line ${i + 1} of the section:`);
            if (i > 0) console.log(`  same:     ${JSON.stringify(el[i - 1])}`);
            console.log(`  tsc:      ${i < el.length ? JSON.stringify(el[i]) : "<end>"}`);
            console.log(`  C#:       ${i < al.length ? JSON.stringify(al[i]) : "<end>"}`);
        }
    }
}

const summary = { units: units.length, identical, differing: differingUnits, crashed: crashedUnits, outcomeMismatch: outcomeMismatch.length,
    sections: counts, headerOrOtherOutputsDiffering: otherDiffering, firstDiffering, firstOutcomeMismatch: outcomeMismatch.slice(0, 20),
    symbolIdNormalized: { units: normalizedUnits, lines: normalizedLines } };
if (coverageFile && existsSync(coverageFile)) {
    // The units that need no transformer (dump-ts-emit.mjs coverage report): how many of them are identical.
    const coverage = JSON.parse(readFileSync(coverageFile, "utf8"));
    const unitIds = new Set(units.map(u => u.id));
    let none = 0, noneIdentical = 0;
    const noneDiffering = [];
    for (const [id, c] of Object.entries(coverage)) {
        if (!unitIds.has(id) || c.error || !c.emitted || c.touched.length > 0) continue;
        none++;
        if (identicalIds.has(id)) noneIdentical++;
        else if (noneDiffering.length < 50) noneDiffering.push(id);
    }
    summary.noTransformerUnits = { units: none, identical: noneIdentical, firstDiffering: noneDiffering };
}
console.log(JSON.stringify(summary));
process.exit(differingUnits === 0 && crashedUnits === 0 && outcomeMismatch.length === 0 ? 0 : 1);
