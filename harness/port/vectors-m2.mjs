// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Test vectors for module M2 (scanner), computed by tsc 6.0.3 itself. They cover the
// exported scanner.ts helpers that gate G1a (token stream of scan()) does not reach:
// comment ranges, skipTrivia, line starts, line/character, shebang, identifier text,
// token strings, keyword lookup, parsePseudoBigInt, getNameOfScriptTarget.
//
// Inputs: every 100th unit of the equivalence corpus (texts copied to units.jsonl so
// the vectors are self-contained), at every token full start of the tsc token dump (where leading trivia starts)
// and at the end of the text; plus fixed lists.
//
// One JSON line per call: {"f": name, "args": [...], "result": ...}. An argument
// {"unit": id} stands for that unit's text, {"lineStarts": id} for computeLineStarts
// of it. Arrays and objects are written as canonical strings (see canon* below), so
// results compare as strings.
//
// Writes artifacts/vectors/M2/*.jsonl, read by tests/NeverSharp.Faithful.Tests.
// Usage: node vectors-m2.mjs

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join } from "node:path";
import { artifactsDir, repoRoot } from "../astdump/common.mjs";

const require = createRequire(join(repoRoot, "harness", "astdump", "package.json"));
const ts = require("typescript");
if (ts.version !== "6.0.3") throw new Error(`typescript ${ts.version}, expected 6.0.3`);

const corpus = join(artifactsDir, "equivalence");
const outDir = join(artifactsDir, "vectors", "M2");
mkdirSync(outDir, { recursive: true });

// Canonical forms: comment ranges "pos-end-kind-nl" joined by ";" (null when undefined), number arrays joined by ",".
const canonRanges = r => r === undefined ? null : r.map(c => `${c.pos}-${c.end}-${c.kind}-${c.hasTrailingNewLine ? 1 : 0}`).join(";");
const canonNumbers = a => a.join(",");

const { units } = JSON.parse(readFileSync(join(corpus, "manifest.json"), "utf8"));
const sample = units.filter((_, i) => i % 100 === 0);
const texts = new Map(sample.map(u => [u.id, readFileSync(join(corpus, "units", `${u.id}.txt`), "utf8")]));
writeFileSync(join(outDir, "units.jsonl"), sample.map(u => JSON.stringify({ id: u.id, kind: u.kind, text: texts.get(u.id) })).join("\n") + "\n");

const out = { trivia: [], lines: [], identifiers: [], tokens: [] };
const push = (file, f, args, result) => out[file].push(JSON.stringify({ f, args, result: result === undefined ? null : result }));
const identifierValues = new Set();

for (const u of sample) {
    const text = texts.get(u.id);
    const ref = { unit: u.id };
    const dump = readFileSync(join(corpus, "dumps", "ts-tokens", `${u.id}.txt`), "utf8").split("\n");
    const positions = new Set([text.length]);
    for (const line of dump) {
        if (!line || line.startsWith("!")) continue;
        const parts = line.split(" ");
        positions.add(Number(parts[1]));
        if (parts[0] === "Identifier" || parts[0] === "PrivateIdentifier") identifierValues.add(JSON.parse(line.slice(line.indexOf(" =") + 2)));
    }
    const lineStarts = ts.computeLineStarts(text);
    push("lines", "computeLineStarts", [ref], canonNumbers(lineStarts));
    push("lines", "getShebang", [ref], ts.getShebang(text));
    for (const p of positions) {
        push("trivia", "getLeadingCommentRanges", [ref, p], canonRanges(ts.getLeadingCommentRanges(text, p)));
        push("trivia", "getTrailingCommentRanges", [ref, p], canonRanges(ts.getTrailingCommentRanges(text, p)));
        push("trivia", "skipTrivia", [ref, p, false, false, false], ts.skipTrivia(text, p, false, false, false));
        push("trivia", "skipTrivia", [ref, p, false, true, false], ts.skipTrivia(text, p, false, true, false));
        push("trivia", "skipTrivia", [ref, p, true, false, true], ts.skipTrivia(text, p, true, false, true));
        const lc = ts.computeLineAndCharacterOfPosition(lineStarts, p);
        push("lines", "computeLineAndCharacterOfPosition", [{ lineStarts: u.id }, p], `${lc.line}:${lc.character}`);
    }
}

// Identifier text: identifiers seen in the sample, keywords, and edge cases, for two targets and both variants.
const tokenStrings = [...Array(ts.SyntaxKind.Count).keys()].map(k => ts.tokenToString(k)).filter(Boolean);
const edgeIdentifiers = ["", "a", "_", "$", "1a", "a1", "a-b", "a b", "#a", "\\u0061", "ä", "日本", "\u{1D400}", "a‌b", "a‍b",    "ᢅ", "℘", "℮", "゛", "·", "a·", "a·", "℘x", "à", "̀a", "\u{10000}", "a\u{E0100}", "<", "a<b", "a:b", "data-x", "π", "Ⅻ"];
for (const name of [...identifierValues, ...tokenStrings, ...edgeIdentifiers]) {
    for (const target of [ts.ScriptTarget.ES5, ts.ScriptTarget.Latest]) {
        for (const variant of [ts.LanguageVariant.Standard, ts.LanguageVariant.JSX]) {
            push("identifiers", "isIdentifierText", [name, target, variant], ts.isIdentifierText(name, target, variant));
        }
    }
}

// Token strings, keyword lookup, pseudo bigints, script target names, UTF-16 encoding.
for (let k = 0; k < ts.SyntaxKind.Count; k++) push("tokens", "tokenToString", [k], ts.tokenToString(k));
for (const s of [...tokenStrings, "", "notakeyword", "Async", "ASYNC", "constructor", "__proto__", "toString", "hasOwnProperty", "valueOf"]) {
    push("tokens", "stringToToken", [s], ts.stringToToken(s));
}
const bigints = ["0n", "1n", "00n", "0x0n", "0x1n", "0xFn", "0xffn", "0Xabcdefn", "0b0n", "0b1n", "0B101n", "0o7n", "0O777n", "123456789n",
    "9007199254740991n", "9007199254740992n", "18446744073709551615n", "0x" + "f".repeat(40) + "n", "0b" + "1".repeat(100) + "n", "0o" + "7".repeat(60) + "n",
    "1" + "0".repeat(80) + "n", "0x0000000000000000000001n", "0b0000000000010n"];
for (const b of bigints) push("tokens", "parsePseudoBigInt", [b], ts.parsePseudoBigInt(b));
for (const t of [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 99, 100]) push("tokens", "getNameOfScriptTarget", [t], ts.getNameOfScriptTarget(t));
for (const cp of [0, 0x41, 0x7f, 0x80, 0x7ff, 0x800, 0xd7ff, 0xe000, 0xfffd, 0xffff, 0x10000, 0x1f600, 0x10ffff]) push("tokens", "utf16EncodeAsString", [cp], ts.utf16EncodeAsString(cp));

for (const [name, lines] of Object.entries(out)) {
    writeFileSync(join(outDir, `${name}.jsonl`), lines.join("\n") + "\n");
    console.log(`${name}: ${lines.length}`);
}
console.log(`units: ${sample.length}`);
