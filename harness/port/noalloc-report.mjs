// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// NoAlloc coverage of Track O (pre-registration §5.4): how many methods of src/NeverSharp.Optimized
// carry [NoAlloc] (checked by the NoAlloc analyzer in Gate 2), how many of those lift constraints
// (Allow = ... with a Reason), and every [NoAllocUnchecked(reason)] exception. Reported with every
// Track O result. Text-based count over the C# sources (method declarations by signature shape);
// the analyzer, not this script, decides whether code is allocation-free.
//
// Usage: node harness/port/noalloc-report.mjs [--json out.json]

import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(fileURLToPath(new URL(".", import.meta.url)), "..", "..");
const src = join(root, "src", "NeverSharp.Optimized");

function* walk(dir) {
    for (const e of readdirSync(dir, { withFileTypes: true })) {
        if (e.name === "bin" || e.name === "obj") continue;
        const full = join(dir, e.name);
        if (e.isDirectory()) yield* walk(full);
        else if (e.name.endsWith(".cs")) yield full;
    }
}

// A method declaration: modifiers, a return type, a name and a parameter list, at the start of a line.
const methodRe = /^\s*(?:(?:public|internal|private|protected|static|sealed|override|virtual|abstract|unsafe|extern|partial|new|readonly|async)\s+)+[\w<>\[\],.?() ]+?\s+\w+(?:<[^>]*>)?\s*\(/;
const report = { methods: 0, noAlloc: 0, noAllocWithAllow: 0, unchecked: [], byDirectory: {} };
for (const file of walk(src)) {
    const rel = relative(src, file).replaceAll("\\", "/");
    const dir = rel.includes("/") ? rel.slice(0, rel.indexOf("/")) : ".";
    const d = (report.byDirectory[dir] ??= { methods: 0, noAlloc: 0 });
    const lines = readFileSync(file, "utf8").split("\n");
    let pending = [];
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const attr = line.match(/^\s*\[(NoAlloc(?:Unchecked)?)\b(.*)\]\s*$/);
        if (attr) { pending.push({ kind: attr[1], args: attr[2], line: i + 1 }); continue; }
        if (/^\s*\[/.test(line) || /^\s*\/\//.test(line)) continue;
        if (methodRe.test(line) && !/\b(class|struct|record|interface|enum|delegate|new\s*\()\b/.test(line) && !/=\s*new\b/.test(line)) {
            report.methods++; d.methods++;
            for (const a of pending) {
                if (a.kind === "NoAlloc") { report.noAlloc++; d.noAlloc++; if (/Allow\s*=/.test(a.args)) report.noAllocWithAllow++; }
                else report.unchecked.push({ file: rel, line: a.line, reason: a.args.replace(/^\(|\)$/g, "") });
            }
        }
        pending = [];
    }
}
console.log(`Track O NoAlloc coverage: ${report.noAlloc} of ${report.methods} methods annotated (${(100 * report.noAlloc / Math.max(1, report.methods)).toFixed(1)} %), ${report.noAllocWithAllow} with lifted constraints, ${report.unchecked.length} [NoAllocUnchecked] exceptions`);
for (const [dir, d] of Object.entries(report.byDirectory)) console.log(`  ${dir}: ${d.noAlloc} / ${d.methods}`);
for (const u of report.unchecked) console.log(`  unchecked ${u.file}:${u.line} ${u.reason}`);
const out = process.argv.includes("--json") ? process.argv[process.argv.indexOf("--json") + 1] : undefined;
if (out) writeFileSync(out, JSON.stringify(report, null, 1));
