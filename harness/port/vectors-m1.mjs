// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Test vectors for module M1 (phase1-plan.md §4), computed by tsc 6.0.3 itself:
// the functions are called on the `ts` object of the pinned npm package, which
// exposes them. One JSON line per call: {"f": name, "args": [...], "result": ...}
// (`undefined` results are written as null).
//
// Inputs:
// - every file name of the equivalence corpus (artifacts/equivalence/manifest.json),
//   the names the parser receives, with several current directories;
// - a fixed list of edge cases (roots, volumes, UNC, URLs, dot segments, separators);
// - names for the spelling suggestion: TypeScript's keywords, each mutated
//   deterministically (deletion, transposition, case change, insertion).
//
// Writes artifacts/vectors/M1/*.jsonl, read by tests/NeverSharp.Faithful.Tests.
//
// Usage: node vectors-m1.mjs

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join } from "node:path";
import { artifactsDir, repoRoot } from "../astdump/common.mjs";

const require = createRequire(join(repoRoot, "harness", "astdump", "package.json"));
const ts = require("typescript");
if (ts.version !== "6.0.3") throw new Error(`typescript ${ts.version}, expected 6.0.3`);

const outDir = join(artifactsDir, "vectors", "M1");
mkdirSync(outDir, { recursive: true });

const edgePaths = [
    "", ".", "..", "/", "//", "///", "\\", "\\\\", "a", "a/", "a\\", "/a", "\\a", "a/b", "a\\b", "a//b", "a\\\\b", "a/./b", "a/../b", "../a", "../../a/b",
    "./a", "a/b/..", "a/b/../..", "a/b/../../..", "/..", "/../a", "/a/..", "/a/../..", "a/.", "a/./", "a/b/", "a/b//", "/a/b/c/../../d",
    "c:", "C:", "c:/", "C:\\", "c:a", "c:/a", "C:\\a\\b", "c:/a/../..", "c:/..", "c:\\..\\a", "z:/x/y", "1:/x", "cc:/x",
    "\\\\server\\share", "\\\\server\\share\\a", "//server/share/a/../b", "//server", "//server/",
    "file:///", "file:///c:", "file:///c:/", "file:///C:/a/b", "file:///c%3a/a", "file:///C%3A/a", "file:///c%3A", "file:///a/b", "file://server/share/a",
    "file://localhost/c:/a", "file://", "file:/a", "http://", "http://host", "http://host/", "http://host/a/../b", "https://host:8080/a/./b?x", "^/a/b", "^/",
    "a.ts", "a.d.ts", "a.tsx", "a.js", "a.jsx", "a.mts", "a.cts", "a.d.mts", "a.d.cts", "a.json", "a.mjs", "a.cjs", ".ts", "a.", "a..ts", "a.TS", "a.D.TS",
    "dir.ts/file", "a/b.c/d", "a/b.d.ts/", "/a.b.c.d.ts", "node_modules/@types/x/index.d.ts", "a/b.tsbuildinfo", "lib.es2015.d.ts",
    "a b/c d.ts", "ä/ö.ts", "日本/語.ts", "a\u0000b","%2F/x", "a%2Fb",
];
const currentDirectories = ["", "/", "/home/user/project", "c:/work", "C:\\work\\sub", "\\\\server\\share\\dir", "file:///c:/x/", "http://host/base/", "a/relative"];
const extensions = [".ts", ".d.ts", ".tsx", ".js", ".json", ".mts", ".d.mts", "", "ts", ".TS"];

const manifest = JSON.parse(readFileSync(join(artifactsDir, "equivalence", "manifest.json"), "utf8"));
const corpusNames = [...new Set(manifest.units.map(u => u.fileName))];
const paths = [...edgePaths, ...corpusNames];

function write(name, calls) {
    const lines = calls.map(([f, args]) => {
        const result = ts[f](...args);
        return JSON.stringify({ f, args, result: result === undefined ? null : result });
    });
    writeFileSync(join(outDir, `${name}.jsonl`), lines.join("\n") + "\n");
    console.log(`${name}: ${lines.length}`);
}

// Paths: single-argument functions on every path.
const single = ["normalizePath", "normalizeSlashes", "getRootLength", "getBaseFileName", "getAnyExtensionFromPath", "hasTrailingDirectorySeparator",
    "removeTrailingDirectorySeparator", "ensureTrailingDirectorySeparator", "tryGetExtensionFromPath"];
write("paths-single", paths.flatMap(p => single.map(f => [f, [p]])));
// getNormalizedAbsolutePath: edge cases with every current directory, corpus names with three.
write("paths-absolute", [
    ...edgePaths.flatMap(p => currentDirectories.map(c => ["getNormalizedAbsolutePath", [p, c]])),
    ...edgePaths.map(p => ["getNormalizedAbsolutePath", [p, undefined]]),
    ...corpusNames.flatMap(p => ["", "/", "c:/work"].map(c => ["getNormalizedAbsolutePath", [p, c]])),
]);
write("paths-combine", [
    ...edgePaths.flatMap(a => ["", "b", "/b", "c:/b", "../b", "b/", "file:///b"].map(b => ["combinePaths", [a, b]])),
    ...edgePaths.map(a => ["combinePaths", [a, "x", "../y", "z"]]),
]);
write("paths-extension", paths.slice(0, edgePaths.length + 5000).flatMap(p => extensions.map(e => ["fileExtensionIs", [p, e]])));
// getAnyExtensionFromPath with extensions, as isDeclarationFileName calls it (parser.ts), and with one extension ignoring case.
write("paths-any-extension", paths.flatMap(p => [["getAnyExtensionFromPath", [p, [".d.ts", ".d.cts", ".d.mts"], false]], ["getAnyExtensionFromPath", [p, ".TS", true]]]));

// Spelling suggestion: TypeScript's keywords (tokenToString of every token kind that is a lowercase word).
const keywords = [...new Set([...Array(ts.SyntaxKind.Count).keys()].map(k => ts.tokenToString(k)).filter(t => t && /^[a-z]+$/.test(t)))];
const mutate = (w, i) => {
    switch (i % 5) {
        case 0: return w.slice(0, 1) + w.slice(2);
        case 1: return w.length > 2 ? w[1] + w[0] + w.slice(2) : w + w;
        case 2: return w[0].toUpperCase() + w.slice(1);
        case 3: return w.slice(0, 2) + "x" + w.slice(2);
        default: return w.toUpperCase();
    }
};
const names = keywords.flatMap((w, i) => [w, mutate(w, i), mutate(w, i + 1), mutate(w, i + 2)]);
// levenshteinWithMax is not exported; getSpellingSuggestion exercises it (and its thresholds).
const candidateSets = [keywords, keywords.filter((_, i) => i % 3 === 0), names.filter((_, i) => i % 5 === 1), ["a", "ab", "abc", "Abc", "ABC", "abcd", "x"]];
const spelling = candidateSets.flatMap(set => names.concat(["a", "ab", "aBc", "abcde", "X", ""]).map(n => {
    const result = ts.getSpellingSuggestion(n, set, k => k);
    return JSON.stringify({ f: "getSpellingSuggestion", args: [n, set], result: result ?? null });
}));
writeFileSync(join(outDir, "spelling.jsonl"), spelling.join("\n") + "\n");
console.log(`spelling: ${spelling.length}`);
