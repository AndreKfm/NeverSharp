// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Builds the checker's tuning list (module C12): the tuning corpus (tuning.mjs) without the units listed in
// tuning-check-exclude.txt, which tsc 6.0.3 cannot check when the whole corpus is one program. Used for the checker's
// speed-dependent choices and its equivalence with tsc (check-equivalence.mjs --corpus tuning-check); never results.
// tuning.txt itself stays unchanged, so the parse and bind tuning figures stay comparable.
//
// Output: artifacts/bench/filelists/tuning-check.txt and tuning-check-manifest.json
// (same shape as tuning-manifest.json, one pseudo-repository "tuning-check").
//
// Usage: node tuning-check.mjs   (after tuning.mjs)

import { createHash } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import { basename, join, relative, sep } from "node:path";
import { benchDir, repoRoot } from "./common.mjs";

const filelists = join(benchDir, "filelists");
const tuning = JSON.parse(readFileSync(join(filelists, "tuning-manifest.json"), "utf8")).repos.tuning;
const excluded = readFileSync(join(import.meta.dirname, "tuning-check-exclude.txt"), "utf8").split(/\r?\n/)
    .filter(l => l && !l.startsWith("#")).map(l => l.split("\t")[0]);
const tuningDir = join(benchDir, "tuning");
const all = readFileSync(join(filelists, "tuning.txt"), "utf8").split(/\r?\n/).filter(Boolean);
if (all.length !== tuning.files) throw new Error(`tuning.txt has ${all.length} files, tuning-manifest.json ${tuning.files}: run tuning.mjs`);
const isExcluded = f => relative(tuningDir, f) === basename(f) && excluded.includes(basename(f));
const files = all.filter(f => !isExcluded(f));
if (all.length - files.length !== excluded.length) throw new Error(`excluded ${all.length - files.length} files, the list has ${excluded.length}`);

// the same content hash as tuning.mjs: tests/cases units by their tuning file name, TypeScript sources by path
const hash = createHash("sha256");
let bytes = 0;
for (const f of files) {
    const data = readFileSync(f);
    bytes += data.length;
    const name = relative(tuningDir, f) === basename(f) ? basename(f) : relative(join(repoRoot, "external", "typescript"), f).split(sep).join("/");
    hash.update(name).update("\0").update(data).update("\0");
}
writeFileSync(join(filelists, "tuning-check.txt"), files.join("\n") + "\n");
writeFileSync(join(filelists, "tuning-check-manifest.json"), JSON.stringify({
    generated: new Date().toISOString(),
    rule: `tuning corpus (contentSha256 ${tuning.contentSha256}) without the ${excluded.length} units of harness/bench/tuning-check-exclude.txt`,
    repos: { "tuning-check": { commit: tuning.commit, files: files.length, bytes, contentSha256: hash.digest("hex"), excluded } },
}, null, 1));
console.log(`tuning-check: ${files.length} files (${excluded.length} units of the tuning corpus left out), ${(bytes / 1048576).toFixed(1)} MiB`);
