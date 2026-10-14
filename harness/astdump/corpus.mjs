// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Builds the equivalence corpus: every file under tests/cases/** of the pinned
// TypeScript checkout, split into parse units, plus the parse-only benchmark
// file set of the five benchmark repositories, written as UTF-8 files plus a
// manifest. Both dumpers read exactly these units, so they parse identical text.
//
// Splitting follows TestCaseParser.makeUnitsFromTest in
// src/harness/harnessIO.ts (TypeScript v6.0.3) for tests/cases/compiler and
// tests/cases/conformance, which are the directories the compiler runner splits.
// All other files are one unit each, named by their path.
//
// Usage: node corpus.mjs [--out <dir>]

import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join, relative, sep } from "node:path";
import ts from "typescript";
import { artifactsDir, repoRoot, scriptKindOf } from "./common.mjs";

const pins = JSON.parse(readFileSync(join(repoRoot, "pins.json"), "utf8"));

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

const manifest = { generatedFrom: ["external/typescript/tests/cases", "external/corpus/<repo> (benchmark parse set)"], units: [], skipped: [] };
let id = 0;

function addUnit(test, name, content) {
    const kind = scriptKindOf(name);
    if (!kind) {
        manifest.skipped.push({ test, unit: name, reason: "not-a-parseable-extension" });
        return;
    }
    if (loneSurrogate.test(content)) {
        manifest.skipped.push({ test, unit: name, reason: "lone-surrogate-not-representable-in-utf8" });
        return;
    }
    const idText = String(id++).padStart(6, "0");
    writeFileSync(join(outDir, "units", `${idText}.txt`), content, "utf8");
    // Parsers need a normalized absolute file name (typescript-go asserts it); both dumpers use this one.
    const fileName = ts.getNormalizedAbsolutePath(name, "/");
    manifest.units.push({ id: idText, test, unit: name, fileName, kind: kind.kind, declaration: kind.declaration });
}

// 1. tests/cases/**
for (const file of walk(casesRoot)) {
    const test = relative(casesRoot, file).split(sep).join("/");
    const text = ts.sys.readFile(file); // same BOM/encoding handling as the TypeScript harness
    const split = splitDirs.has(test.split("/")[0]);
    const { units, prefixError } = split ? makeUnitsFromTest(text, test) : { units: [{ name: test, content: text }], prefixError: false };
    if (prefixError) manifest.skipped.push({ test, unit: null, reason: "harness-prefix-error" });
    for (const unit of units) addUnit(test, unit.name, unit.content);
}

// 2. Sources of the benchmark repositories: exactly the parse-only benchmark file set
//    (harness/bench/filelists.mjs), one unit per file, named <repo>/<path>.
const benchManifestFile = join(artifactsDir, "bench", "filelists", "manifest.json");
if (!existsSync(benchManifestFile)) throw new Error("run harness/bench/filelists.mjs first (benchmark file lists missing)");
const benchManifest = JSON.parse(readFileSync(benchManifestFile, "utf8"));
for (const repo of pins.benchmarkCorpus.repos) {
    const entry = benchManifest.repos[repo.name];
    if (entry?.commit !== repo.commit) throw new Error(`benchmark file list for ${repo.name} does not match the pinned commit`);
    for (const path of entry.relativePaths) {
        addUnit(`bench/${repo.name}/${path}`, `${repo.name}/${path}`, ts.sys.readFile(join(repoRoot, "external", "corpus", repo.name, path)));
    }
}
writeFileSync(join(outDir, "manifest.json"), JSON.stringify(manifest, null, 1));
const reasons = Object.groupBy(manifest.skipped, s => s.reason);
console.log(`units: ${manifest.units.length}`);
for (const [reason, list] of Object.entries(reasons)) console.log(`skipped (${reason}): ${list.length}`);
console.log(`out: ${outDir}`);
