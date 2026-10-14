// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Fetches every pinned upstream reference from pins.json into external/<name>
// at its exact commit and fails if the checked-out HEAD differs from the pin.
//
// Usage: node scripts/fetch-references.mjs [name ...]

import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const pins = JSON.parse(readFileSync(join(root, "pins.json"), "utf8"));

const git = (cwd, ...args) =>
    execFileSync("git", args, { cwd, encoding: "utf8", stdio: ["ignore", "pipe", "inherit"] }).trim();

function fetchPinned(name, repository, commit) {
    if (!commit) {
        console.log(`skip ${name}: no commit pinned`);
        return;
    }
    const dir = join(root, "external", name);
    if (!existsSync(join(dir, ".git"))) {
        mkdirSync(dir, { recursive: true });
        git(dir, "init", "--quiet");
        git(dir, "remote", "add", "origin", repository);
    }
    let current = "";
    try {
        current = git(dir, "rev-parse", "--verify", "--quiet", "HEAD^{commit}");
    } catch {
        // Fresh repository without a HEAD yet.
    }
    if (current !== commit) {
        git(dir, "fetch", "--quiet", "--depth", "1", "origin", commit);
        git(dir, "-c", "advice.detachedHead=false", "checkout", "--quiet", "--force", commit);
    }
    const head = git(dir, "rev-parse", "HEAD");
    if (head !== commit) throw new Error(`${name}: HEAD ${head} != pinned ${commit}`);
    console.log(`ok   ${name} ${commit}`);
}

const targets = [
    ...Object.entries(pins.references).map(([name, r]) => ({ name, repository: r.repository, commit: r.commit })),
    ...pins.benchmarkCorpus.repos.map(r => ({ name: `corpus/${r.name}`, repository: r.repository, commit: r.commit })),
];
const wanted = new Set(process.argv.slice(2));
for (const t of targets) {
    if (wanted.size === 0 || wanted.has(t.name)) fetchPinned(t.name, t.repository, t.commit);
}
