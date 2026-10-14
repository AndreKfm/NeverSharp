// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Builds the equivalence corpus: every file under tests/cases/** of the pinned
// TypeScript checkout, split into parse units, written as UTF-8 files plus a
// manifest. Both dumpers read exactly these units, so they parse identical text.
//
// Splitting follows TestCaseParser.makeUnitsFromTest in
// src/harness/harnessIO.ts (TypeScript v6.0.3) for tests/cases/compiler and
// tests/cases/conformance, which are the directories the compiler runner splits.
// All other files are one unit each, named by their path.
//
// Usage: node corpus.mjs [--out <dir>]

import { mkdirSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { join, relative, sep } from "node:path";
import ts from "typescript";
import { artifactsDir, repoRoot, scriptKindOf } from "./common.mjs";

const casesRoot = join(repoRoot, "external", "typescript", "tests", "cases");
const outDir = process.argv.includes("--out") ? process.argv[process.argv.indexOf("--out") + 1] : join(artifactsDir, "equivalence");
const splitDirs = new Set(["compiler", "conformance"]);

// Same patterns as harnessIO.ts, without the g flag (each line is matched on its own).
const optionRegex = /^\/{2}\s*@(\w+)\s*:\s*([^\r\n]*)/;
const linkRegex = /^\/{2}\s*@link\s*:\s*([^\r\n]*)\s*->\s*([^\r\n]*)/;
const newlineRegex = /\r?\n/;
const loneSurrogate = /[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/;

function makeUnitsFromTest(code, fileName) {
    const units = [];
    let content;
    let name;
    let prefixError = false;
    for (const line of code.split(newlineRegex)) {
        if (linkRegex.test(line)) continue;
        const meta = optionRegex.exec(line);
        if (meta) {
            if (meta[1].toLowerCase() !== "filename") continue;
            if (name) {
                units.push({ name, content: content ?? "" }); // the harness keeps undefined here; it becomes an empty file
                content = undefined;
            }
            else if (content && ts.skipTrivia(content, 0, false, false) !== content.length) {
                prefixError = true; // the harness throws here
            }
            if (!name) content = "";
            name = meta[2].trim();
        }
        else {
            if (content === undefined) content = "";
            else if (content !== "") content += "\n";
            content += line;
        }
    }
    name = units.length > 0 || name ? name : fileName.split("/").pop();
    units.push({ name, content: content || "" });
    return { units, prefixError };
}

function* walk(dir) {
    for (const entry of readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0)) {
        const full = join(dir, entry.name);
        if (entry.isDirectory()) yield* walk(full);
        else yield full;
    }
}

rmSync(outDir, { recursive: true, force: true });
mkdirSync(join(outDir, "units"), { recursive: true });

const manifest = { generatedFrom: "external/typescript/tests/cases", units: [], skipped: [] };
let id = 0;
for (const file of walk(casesRoot)) {
    const test = relative(casesRoot, file).split(sep).join("/");
    const text = ts.sys.readFile(file); // same BOM/encoding handling as the TypeScript harness
    const split = splitDirs.has(test.split("/")[0]);
    const { units, prefixError } = split ? makeUnitsFromTest(text, test) : { units: [{ name: test, content: text }], prefixError: false };
    if (prefixError) manifest.skipped.push({ test, unit: null, reason: "harness-prefix-error" });
    for (const unit of units) {
        const kind = scriptKindOf(unit.name);
        if (!kind) {
            manifest.skipped.push({ test, unit: unit.name, reason: "not-a-parseable-extension" });
            continue;
        }
        if (loneSurrogate.test(unit.content)) {
            manifest.skipped.push({ test, unit: unit.name, reason: "lone-surrogate-not-representable-in-utf8" });
            continue;
        }
        const idText = String(id++).padStart(6, "0");
        writeFileSync(join(outDir, "units", `${idText}.txt`), unit.content, "utf8");
        // Parsers need a normalized absolute file name (typescript-go asserts it); both dumpers use this one.
        const fileName = ts.getNormalizedAbsolutePath(unit.name, "/");
        manifest.units.push({ id: idText, test, unit: unit.name, fileName, kind: kind.kind, declaration: kind.declaration });
    }
}
writeFileSync(join(outDir, "manifest.json"), JSON.stringify(manifest, null, 1));
const reasons = Object.groupBy(manifest.skipped, s => s.reason);
console.log(`units: ${manifest.units.length}`);
for (const [reason, list] of Object.entries(reasons)) console.log(`skipped (${reason}): ${list.length}`);
console.log(`out: ${outDir}`);
