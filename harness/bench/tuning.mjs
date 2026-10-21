// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Builds the tuning corpus (pre-registration §3, A5): every tests/cases unit of
// the equivalence corpus plus TypeScript v6.0.3's own src/**/*.ts. It is used
// for every speed-dependent choice in both tracks; the benchmark corpus is not.
//
// tests/cases units are written to artifacts/bench/tuning/<id><ext>, because
// the drivers take the script kind from the file extension. TypeScript's own
// sources are listed by path.
//
// Output: artifacts/bench/filelists/tuning.txt and tuning-manifest.json
// (same shape as manifest.json, one pseudo-repository "tuning").
//
// Usage: node tuning.mjs   (after harness/astdump/corpus.mjs)

import { createHash } from "node:crypto";
import { mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join, relative, sep } from "node:path";
import { artifactsDir, benchDir, pins, repoRoot } from "./common.mjs";

const corpus = join(artifactsDir, "equivalence");
const outDir = join(benchDir, "tuning");
const extensions = { TS: ".ts", TSX: ".tsx", JS: ".js", JSX: ".jsx", JSON: ".json" };

rmSync(outDir, { recursive: true, force: true });
mkdirSync(outDir, { recursive: true });

const { units } = JSON.parse(readFileSync(join(corpus, "manifest.json"), "utf8"));
const files = [];
for (const u of units) {
    if (u.test.startsWith("bench/")) continue; // benchmark sources are never tuning input
    const ext = u.declaration ? ".d.ts" : extensions[u.kind];
    const target = join(outDir, u.id + ext);
    writeFileSync(target, readFileSync(join(corpus, "units", `${u.id}.txt`)));
    files.push({ path: target, name: `tests/cases/${u.test}::${u.unit}` });
}
const testsCases = files.length;

function* walk(dir) {
    for (const e of readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0)) {
        const full = join(dir, e.name);
        if (e.isDirectory()) yield* walk(full);
        else if (e.name.endsWith(".ts") && !e.name.endsWith(".d.ts")) yield full;
    }
}
const tsRoot = join(repoRoot, "external", "typescript");
for (const f of walk(join(tsRoot, "src"))) files.push({ path: f, name: relative(tsRoot, f).split(sep).join("/") });

const hash = createHash("sha256");
let bytes = 0;
for (const f of files) {
    const data = readFileSync(f.path);
    bytes += data.length;
    hash.update(f.name).update("\0").update(data).update("\0");
}
writeFileSync(join(benchDir, "filelists", "tuning.txt"), files.map(f => f.path).join("\n") + "\n");
writeFileSync(join(benchDir, "filelists", "tuning-manifest.json"), JSON.stringify({
    generated: new Date().toISOString(),
    rule: "tests/cases units of the equivalence corpus + TypeScript v6.0.3 src/**/*.ts (excluding .d.ts)",
    repos: { tuning: { commit: pins.references.typescript.commit, files: files.length, bytes, contentSha256: hash.digest("hex"), testsCasesUnits: testsCases, typescriptSourceFiles: files.length - testsCases } },
}, null, 1));
console.log(`tuning corpus: ${files.length} files (${testsCases} tests/cases units, ${files.length - testsCases} TypeScript sources), ${(bytes / 1048576).toFixed(1)} MiB`);
