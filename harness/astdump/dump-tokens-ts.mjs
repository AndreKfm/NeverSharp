// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Token-stream dump of tsc 6.0.3 for the scanner gate G1a (phase1-plan.md §4).
// Each unit is scanned in the parser's mode (ts.createScanner with
// skipTrivia=true, language variant from the script kind) until
// EndOfFileToken, without the parser's contextual rescans.
//
// Format (docs/ast-dump-format.md §8), one line per token:
//   <Kind> <fullStart> <start> <end>[ <TokenFlag>|<TokenFlag>...][ =<JSON token value>]
//   !scan <code> <start> <length>            scanner diagnostics, in emission order
// The token value is written for identifiers, keywords used as identifiers,
// and literals (where ts.Scanner.getTokenValue() is meaningful).
//
// Usage: node dump-tokens-ts.mjs [--corpus <dir>]

import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import ts from "typescript";
import { artifactsDir } from "./common.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const corpus = arg("--corpus", join(artifactsDir, "equivalence"));
const outDir = join(corpus, "dumps", "ts-tokens");

function firstNames(enumObject, singleBitOnly) {
    const names = new Map();
    for (const [name, value] of Object.entries(enumObject)) {
        if (typeof value !== "number" || names.has(value)) continue;
        if (singleBitOnly && (value === 0 || (value & (value - 1)) !== 0)) continue;
        names.set(value, name);
    }
    return names;
}
const kindNames = firstNames(ts.SyntaxKind, false);
const tokenFlagBits = [...firstNames(ts.TokenFlags, true)].sort((a, b) => a[0] - b[0]);
const withValue = kind => kind === ts.SyntaxKind.Identifier || kind === ts.SyntaxKind.PrivateIdentifier
    || (kind >= ts.SyntaxKind.FirstLiteralToken && kind <= ts.SyntaxKind.LastLiteralToken)
    || kind === ts.SyntaxKind.NoSubstitutionTemplateLiteral || kind === ts.SyntaxKind.TemplateHead
    || kind === ts.SyntaxKind.TemplateMiddle || kind === ts.SyntaxKind.TemplateTail;

function dumpTokens(text, unit) {
    const diagnostics = [];
    const variant = unit.kind === "TSX" || unit.kind === "JSX" ? ts.LanguageVariant.JSX : ts.LanguageVariant.Standard;
    const scanner = ts.createScanner(ts.ScriptTarget.Latest, /*skipTrivia*/ true, variant, text,
        (message, length) => diagnostics.push(`!scan ${message.code} ${scanner.getTokenEnd()} ${length}`));
    let out = "";
    for (let guard = 0; guard <= text.length + 1; guard++) {
        const kind = scanner.scan();
        out += `${kindNames.get(kind)} ${scanner.getTokenFullStart()} ${scanner.getTokenStart()} ${scanner.getTokenEnd()}`;
        const flags = scanner.getTokenFlags();
        const names = tokenFlagBits.filter(([bit]) => flags & bit).map(([, name]) => name).sort();
        if (names.length) out += " " + names.join("|");
        if (withValue(kind)) out += " =" + JSON.stringify(scanner.getTokenValue());
        out += "\n";
        if (kind === ts.SyntaxKind.EndOfFileToken) break;
    }
    return out + diagnostics.map(d => d + "\n").join("");
}

rmSync(outDir, { recursive: true, force: true });
mkdirSync(outDir, { recursive: true });
const { units } = JSON.parse(readFileSync(join(corpus, "manifest.json"), "utf8"));
const started = performance.now();
let tokens = 0;
for (const unit of units) {
    const dump = dumpTokens(readFileSync(join(corpus, "units", `${unit.id}.txt`), "utf8"), unit);
    tokens += dump.split("\n").filter(l => l && !l.startsWith("!")).length;
    writeFileSync(join(outDir, `${unit.id}.txt`), dump);
}
console.log(`ts tokens: ${units.length} units, ${tokens} tokens in ${((performance.now() - started) / 1000).toFixed(1)} s`);
