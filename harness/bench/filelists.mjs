// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Writes the parse-only file list of each benchmark repository: every file
// tracked by git at the pinned commit with a TypeScript-family extension
// (.ts .tsx .mts .cts, including .d.ts). No file is added or removed by hand.
//
// Output: artifacts/bench/filelists/<repo>.txt (absolute paths, one per line,
// sorted) and artifacts/bench/filelists/manifest.json (counts, bytes, hashes).
//
// Usage: node filelists.mjs

import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { benchDir, pins, repoRoot, sourceExtensions } from "./common.mjs";

const outDir = join(benchDir, "filelists");
mkdirSync(outDir, { recursive: true });
const manifest = { generated: new Date().toISOString(), rule: "git ls-files at the pinned commit, extensions .ts .tsx .mts .cts", repos: {} };

for (const repo of pins.benchmarkCorpus.repos) {
    const dir = join(repoRoot, "external", "corpus", repo.name);
    const head = execFileSync("git", ["-C", dir, "rev-parse", "HEAD"], { encoding: "utf8" }).trim();
    if (head !== repo.commit) throw new Error(`${repo.name}: checkout at ${head}, pinned ${repo.commit}`);
    const tracked = execFileSync("git", ["-C", dir, "ls-files", "-z"], { encoding: "utf8", maxBuffer: 1 << 30 }).split("\0").filter(Boolean);
    // Symlinks are tracked as links; skip anything that is not a regular file on disk.
    const files = tracked.filter(f => sourceExtensions.test(f)).sort().filter(f => statSync(join(dir, f), { throwIfNoEntry: false })?.isFile());
    const content = createHash("sha256");
    let bytes = 0;
    for (const f of files) {
        const data = readFileSync(join(dir, f));
        bytes += data.length;
        content.update(f).update("\0").update(data).update("\0");
    }
    writeFileSync(join(outDir, `${repo.name}.txt`), files.map(f => join(dir, f)).join("\n") + "\n");
    manifest.repos[repo.name] = { commit: repo.commit, files: files.length, bytes, contentSha256: content.digest("hex"), relativePaths: files };
    console.log(`${repo.name.padEnd(11)} ${String(files.length).padStart(6)} files ${(bytes / 1048576).toFixed(1).padStart(7)} MiB`);
}
writeFileSync(join(outDir, "manifest.json"), JSON.stringify(manifest, null, 1));
