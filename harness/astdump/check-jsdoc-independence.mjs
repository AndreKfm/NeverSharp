// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Checks the premise of the M4 gate (Gate 1 without the JSDoc section, phase1-plan.md §4):
// tsc 6.0.3's dump with JSDocParsingMode.ParseNone must equal its ParseAll dump with the JSDoc
// section removed ("@" lines, the "=HasJSDoc" attribute, "!jsdocdiag" lines) on every unit.
// If so, a parser without the JSDoc parser can be compared on the stripped dumps.
//
// Usage: node check-jsdoc-independence.mjs [--corpus <dir>]

import { readFileSync } from "node:fs";
import { join } from "node:path";
import ts from "typescript";
import { artifactsDir } from "./common.mjs";
import { stripJsDoc } from "./strip-jsdoc.mjs";

const corpus = process.argv.includes("--corpus") ? process.argv[process.argv.indexOf("--corpus") + 1] : join(artifactsDir, "equivalence");
const scriptKinds = { TS: ts.ScriptKind.TS, TSX: ts.ScriptKind.TSX, JS: ts.ScriptKind.JS, JSX: ts.ScriptKind.JSX, JSON: ts.ScriptKind.JSON };
const kindNames = new Map();
for (const [name, value] of Object.entries(ts.SyntaxKind)) if (typeof value === "number" && !kindNames.has(value)) kindNames.set(value, name);
const flagBits = [];
const seen = new Set();
for (const [name, value] of Object.entries(ts.NodeFlags)) {
    if (typeof value !== "number" || seen.has(value) || value === 0 || (value & (value - 1)) !== 0) continue;
    seen.add(value);
    flagBits.push([value, name]);
}
flagBits.sort((a, b) => a[0] - b[0]);

// The same rendering as dump-ts.mjs + format.mjs, with ParseNone.
function dumpParseNone(text, unit) {
    const sf = ts.createSourceFile(unit.fileName, text, { languageVersion: ts.ScriptTarget.Latest, jsDocParsingMode: ts.JSDocParsingMode.ParseNone }, false, scriptKinds[unit.kind]);
    let out = "";
    const visit = (node, depth) => {
        const names = [];
        for (const [bit, name] of flagBits) {
            if ((node.flags & bit) === 0) continue;
            if (node.kind === ts.SyntaxKind.Identifier && bit === ts.NodeFlags.ContainsThis) names.push("IdentifierHasExtendedUnicodeEscape");
            else if (node.kind === ts.SyntaxKind.Identifier && bit === ts.NodeFlags.HasAsyncFunctions) names.push("IdentifierIsInJSDocNamespace");
            else names.push(name);
        }
        if (node.jsDoc?.length) names.push("=HasJSDoc");
        out += "  ".repeat(depth) + kindNames.get(node.kind) + " " + node.pos + " " + node.end + (names.length ? " " + names.sort().join("|") : "") + "\n";
        ts.forEachChild(node, child => void visit(child, depth + 1));
    };
    visit(sf, 0);
    for (const d of sf.parseDiagnostics) out += `!diag ${d.code} ${d.start} ${d.length}\n`;
    for (const d of sf.jsDocDiagnostics ?? []) out += `!jsdocdiag ${d.code} ${d.start} ${d.length}\n`;
    return out;
}

const { units } = JSON.parse(readFileSync(join(corpus, "manifest.json"), "utf8"));
const differing = [];
for (const unit of units) {
    const text = readFileSync(join(corpus, "units", `${unit.id}.txt`), "utf8");
    const all = stripJsDoc(readFileSync(join(corpus, "dumps", "ts", `${unit.id}.txt`), "utf8"));
    if (dumpParseNone(text, unit) !== all) differing.push(unit.id);
}
console.log(JSON.stringify({ units: units.length, identical: units.length - differing.length, differing: differing.length, firstDiffering: differing.slice(0, 30) }));
process.exit(differing.length ? 1 : 0);
