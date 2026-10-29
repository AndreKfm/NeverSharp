// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Equivalence check of the repository programs (Phase 3, pre-registration §17, module C10; not timed, not a gate):
// each benchmark repository's file list as one program with the Gate C options ({ noResolve: true }), checked by
// tsc 6.0.3 (harness/astdump/node_modules/typescript, the Gate C oracle's package) and by the C# check driver
// (cs-o-aot-ws, --check --single --dump-diagnostics). Compares the semantic diagnostics of every program file (in program
// order) and the global diagnostics, line by line in the canonical form of tools/NeverSharp.ParseDriver/CheckMode.cs.
// typescript-go 7.0.2's counts (parse-go --check --single) and tsrs 0.18.0's (parse-rs --check --single, module C11) are
// reported next to them; they are expected to differ (TypeScript 7).
//
// tsc runs as in executeCommandLine: ts.createCompilerHost(options) on the real file system with jsDocParsingMode
// ParseForTypeErrors, ts.createProgram(root files of the list, options, host), then program.getSemanticDiagnostics(file)
// for every program file and program.getGlobalDiagnostics(). Every side runs with the repository root as the current
// directory.
//
// Output: artifacts/bench/check-equivalence/<repo>.{tsc,cs}.txt and <repo>.json; a markdown summary on stdout.
//
// Usage: node check-equivalence.mjs [--repos a,b] [--corpus benchmark|tuning] [--skip-tsgo] [--skip-tsrs]
//        node check-equivalence.mjs --tsc-dump <file list> <out>   (internal: the tsc side, in its own process)

import { spawnSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { benchDir, exe, pins, repoRoot } from "./common.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;

// The tsc side, in its own process (large heap): writes the canonical diagnostics and prints the counts.
if (process.argv.includes("--tsc-dump")) {
    const [list, out] = process.argv.slice(process.argv.indexOf("--tsc-dump") + 1);
    const ts = createRequire(join(repoRoot, "harness", "astdump", "package.json"))("typescript");
    const files = readFileSync(list, "utf8").split(/\r?\n/).filter(Boolean);
    const options = { noResolve: true };
    const host = ts.createCompilerHost(options);
    host.jsDocParsingMode = ts.JSDocParsingMode.ParseForTypeErrors;
    const libFolder = ts.normalizePath(dirname(host.getDefaultLibFileName(options)));
    const program = ts.createProgram(files, options, host);
    const lines = [];
    const counts = { total: 0, Error: 0, Warning: 0, Suggestion: 0, Message: 0 };
    const write = (tag, d) => {
        const fileName = d.file?.fileName;
        const fileText = fileName === undefined ? "-" : fileName.startsWith(libFolder + "/") ? fileName.slice(libFolder.length + 1) : fileName;
        const category = ts.DiagnosticCategory[d.category];
        counts.total++;
        counts[category]++;
        lines.push([tag, fileText, d.start ?? "-", d.length ?? "-", d.code, category, JSON.stringify(ts.flattenDiagnosticMessageText(d.messageText, "\n"))].join("\t"));
    };
    for (const file of program.getSourceFiles()) for (const d of program.getSemanticDiagnostics(file)) write("semantic", d);
    for (const d of program.getGlobalDiagnostics()) write("global", d);
    writeFileSync(out, lines.map(l => l + "\n").join(""));
    console.log(JSON.stringify({ programFiles: program.getSourceFiles().length, ...counts }));
    process.exit(0);
}

const node = join(repoRoot, ".tools", `node-${pins.tools.node.version}`, "node" + exe);
const csDriver = join(repoRoot, "artifacts", "bin", "cs", "optimized-aot-ws", "parse-cs-optimized-aot-ws" + exe);
const parseGo = join(repoRoot, "artifacts", "bin", "parse-go" + exe);
const parseRs = join(repoRoot, "artifacts", "bin", "parse-rs" + exe);
const corpusName = arg("--corpus", "benchmark");
const repos = arg("--repos", undefined)?.split(",") ?? (corpusName === "tuning" ? ["tuning"] : pins.benchmarkCorpus.repos.map(r => r.name));
const outDir = join(repoRoot, "artifacts", "bench", "check-equivalence");
mkdirSync(outDir, { recursive: true });

const run = (command, args) => {
    const started = Date.now();
    const r = spawnSync(command, args, { cwd: repoRoot, encoding: "utf8", maxBuffer: 1 << 30 });
    if (r.status !== 0) throw new Error(`${command} ${args.join(" ")} failed (${r.status}): ${r.stderr}`);
    return { output: JSON.parse(r.stdout.trim().split("\n").pop()), seconds: (Date.now() - started) / 1000 };
};
const readLines = file => readFileSync(file, "utf8").split("\n").filter(Boolean);
const codeOf = line => line.split("\t")[4];

console.log("| Repository | tsc 6.0.3 | C# (single) | identical | only tsc | only C# | tsgo 7.0.2 (single) | tsrs 0.18.0 (single) |");
console.log("|---|---:|---:|---|---:|---:|---:|---:|");
const details = [];
for (const repo of repos) {
    const list = join(benchDir, "filelists", `${repo}.txt`);
    const tscOut = join(outDir, `${repo}.tsc.txt`);
    const csOut = join(outDir, `${repo}.cs.txt`);
    const tsc = run(node, ["--max-old-space-size=16384", "--stack-size=8192", import.meta.filename, "--tsc-dump", list, tscOut]);
    const cs = run(csDriver, ["--check", "--single", "--dump-diagnostics", csOut, list]);
    const tsgo = process.argv.includes("--skip-tsgo") ? null : run(parseGo, ["--check", "--single", list]);
    const tsrs = process.argv.includes("--skip-tsrs") ? null : run(parseRs, ["--check", "--single", list]);
    const a = readLines(tscOut);
    const b = readLines(csOut);
    const identical = a.length === b.length && a.every((l, i) => l === b[i]);
    // multiset differences, grouped by code with the first example of each side
    const count = new Map();
    for (const l of a) count.set(l, (count.get(l) ?? 0) + 1);
    const onlyCs = [];
    for (const l of b) {
        const n = count.get(l) ?? 0;
        if (n > 0) count.set(l, n - 1);
        else onlyCs.push(l);
    }
    const onlyTsc = [...count].flatMap(([l, n]) => Array(n).fill(l));
    const byCode = new Map();
    for (const [side, ls] of [["tsc", onlyTsc], ["cs", onlyCs]]) {
        for (const l of ls) {
            const e = byCode.get(codeOf(l)) ?? { code: codeOf(l), tsc: 0, cs: 0, exampleTsc: null, exampleCs: null };
            e[side]++;
            if (side === "tsc") e.exampleTsc ??= l;
            else e.exampleCs ??= l;
            byCode.set(codeOf(l), e);
        }
    }
    const summary = {
        repo, identical,
        tsc: { ...tsc.output, seconds: tsc.seconds },
        cs: { ...cs.output, seconds: cs.seconds },
        tsgo: tsgo && { ...tsgo.output, seconds: tsgo.seconds },
        tsrs: tsrs && { ...tsrs.output, seconds: tsrs.seconds },
        sameOrder: identical,
        onlyTsc: onlyTsc.length, onlyCs: onlyCs.length,
        differencesByCode: [...byCode.values()].sort((x, y) => (y.tsc + y.cs) - (x.tsc + x.cs)),
    };
    writeFileSync(join(outDir, `${repo}.json`), JSON.stringify(summary, null, 1));
    details.push(summary);
    console.log(`| ${repo} | ${tsc.output.total} | ${cs.output.diagnostics} | ${identical ? "yes" : "no"} | ${onlyTsc.length} | ${onlyCs.length} | ${tsgo ? tsgo.output.diagnostics : "-"} | ${tsrs ? tsrs.output.diagnostics : "-"} |`);
}
for (const s of details) {
    if (s.identical) continue;
    console.log(`\n### ${s.repo}: differences by code (count only in tsc / only in C#, first example)\n`);
    for (const d of s.differencesByCode) {
        console.log(`- ${d.code}: ${d.tsc} / ${d.cs}`);
        if (d.exampleTsc) console.log(`  - tsc: \`${d.exampleTsc}\``);
        if (d.exampleCs) console.log(`  - C#:  \`${d.exampleCs}\``);
    }
}
