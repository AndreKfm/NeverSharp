// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Emit equivalence of the repository programs (Phase 4, draft pre-registration §19, module D7; not timed, not a gate),
// the sibling of check-equivalence.mjs: each benchmark repository's file list as one program with Gate D's options
// ({ noResolve: true, declaration: true, newLine: lf }), checked and then emitted to memory by tsc 6.0.3
// (harness/astdump/node_modules/typescript, the Gate D oracle's package) and by the C# emit driver (cs-o-aot-ws,
// --check --emit --single --dump-diagnostics --dump-emit). Compares every output (name and text, in emit order) and the
// emit diagnostics, and the diagnostics of the check before the emit (as check-equivalence.mjs). typescript-go 7.0.2's
// and tsrs 0.18.0's output file counts and bytes (parse-go / parse-rs --check --emit --single) are reported next to them;
// they are expected to differ (TypeScript 7).
//
// tsc runs as in executeCommandLine: ts.createCompilerHost(options) on the real file system with jsDocParsingMode
// ParseForTypeErrors, ts.createProgram(root files of the list, options, host), program.getSemanticDiagnostics(file) for
// every program file and program.getGlobalDiagnostics(), then program.emit(undefined, writeFile) with an in-memory
// writeFile. Every side runs with the repository root as the current directory.
//
// Canonical emit dump (tools/NeverSharp.ParseDriver/CheckMode.cs WriteEmit), one JSON object per line: every output in
// write order as {"output":name,"text":text}; the emit diagnostics as {"diagnostic":line} (check-equivalence.mjs's
// canonical line with the tag "emit"); last {"emitSkipped":bool}.
//
// Output: artifacts/bench/emit-equivalence/<repo>.{tsc,cs}.{emit.jsonl,diagnostics.txt} and <repo>.json; a markdown
// summary on stdout.
//
// --corpus tuning-check: the tuning list without the units of tuning-emit-exclude.txt (tsc 6.0.3 cannot emit the list as
// one program with them), written to artifacts/bench/filelists/tuning-check-emit.txt and used by every side.
//
// Usage: node emit-equivalence.mjs [--repos a,b] [--corpus benchmark|tuning-check] [--skip-tsgo] [--skip-tsrs]
//                                  [--examples N]
//        node emit-equivalence.mjs --tsc-dump <file list> <emit out> <diagnostics out>   (internal: the tsc side)

import { spawnSync } from "node:child_process";
import { closeSync, createReadStream, mkdirSync, openSync, readFileSync, writeFileSync, writeSync } from "node:fs";
import { createRequire } from "node:module";
import { basename, dirname, join } from "node:path";
import { benchDir, exe, pins, repoRoot } from "./common.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;

// The tsc side, in its own process (large heap): writes the canonical diagnostics and emit dump, prints the counts.
if (process.argv.includes("--tsc-dump")) {
    const [list, emitOut, diagnosticsOut] = process.argv.slice(process.argv.indexOf("--tsc-dump") + 1);
    const ts = createRequire(join(repoRoot, "harness", "astdump", "package.json"))("typescript");
    const files = readFileSync(list, "utf8").split(/\r?\n/).filter(Boolean);
    const options = { noResolve: true, declaration: true, newLine: ts.NewLineKind.LineFeed };
    const host = ts.createCompilerHost(options);
    host.jsDocParsingMode = ts.JSDocParsingMode.ParseForTypeErrors;
    const libFolder = ts.normalizePath(dirname(host.getDefaultLibFileName(options)));
    const program = ts.createProgram(files, options, host);
    const counts = { total: 0, Error: 0, Warning: 0, Suggestion: 0, Message: 0 };
    const line = (tag, d) => {
        const fileName = d.file?.fileName;
        const fileText = fileName === undefined ? "-" : fileName.startsWith(libFolder + "/") ? fileName.slice(libFolder.length + 1) : fileName;
        return [tag, fileText, d.start ?? "-", d.length ?? "-", d.code, ts.DiagnosticCategory[d.category], JSON.stringify(ts.flattenDiagnosticMessageText(d.messageText, "\n"))].join("\t");
    };
    const lines = [];
    const write = (tag, d) => {
        counts.total++;
        counts[ts.DiagnosticCategory[d.category]]++;
        lines.push(line(tag, d));
    };
    for (const file of program.getSourceFiles()) for (const d of program.getSemanticDiagnostics(file)) write("semantic", d);
    for (const d of program.getGlobalDiagnostics()) write("global", d);
    writeFileSync(diagnosticsOut, lines.map(l => l + "\n").join(""));
    const fd = openSync(emitOut, "w");
    let outputFiles = 0, outputBytes = 0;
    const result = program.emit(undefined, (fileName, text) => {
        outputFiles++;
        outputBytes += Buffer.byteLength(text, "utf8");
        writeSync(fd, JSON.stringify({ output: fileName, text }) + "\n");
    });
    for (const d of result.diagnostics) writeSync(fd, JSON.stringify({ diagnostic: line("emit", d) }) + "\n");
    writeSync(fd, JSON.stringify({ emitSkipped: result.emitSkipped }) + "\n");
    closeSync(fd);
    console.log(JSON.stringify({ programFiles: program.getSourceFiles().length, diagnostics: counts.total, ...counts, outputFiles, outputBytes, emitDiagnostics: result.diagnostics.length, emitSkipped: result.emitSkipped }));
    process.exit(0);
}

const node = join(repoRoot, ".tools", `node-${pins.tools.node.version}`, "node" + exe);
const csDriver = join(repoRoot, "artifacts", "bin", "cs", "optimized-aot-ws", "parse-cs-optimized-aot-ws" + exe);
const parseGo = join(repoRoot, "artifacts", "bin", "parse-go" + exe);
const parseRs = join(repoRoot, "artifacts", "bin", "parse-rs" + exe);
const corpusName = arg("--corpus", "benchmark");
if (corpusName !== "benchmark" && corpusName !== "tuning-check") throw new Error("--corpus must be benchmark or tuning-check");
const repos = arg("--repos", undefined)?.split(",") ?? (corpusName === "benchmark" ? pins.benchmarkCorpus.repos.map(r => r.name) : [corpusName]);
// The file list of a repository; tuning-check: without the units of tuning-emit-exclude.txt.
function listOf(repo) {
    if (repo !== "tuning-check") return join(benchDir, "filelists", `${repo}.txt`);
    const excluded = new Set(readFileSync(join(import.meta.dirname, "tuning-emit-exclude.txt"), "utf8").split(/\r?\n/)
        .filter(l => l && !l.startsWith("#")).map(l => l.split("\t")[0]));
    const all = readFileSync(join(benchDir, "filelists", "tuning-check.txt"), "utf8").split(/\r?\n/).filter(Boolean);
    const files = all.filter(f => !(dirname(f) === join(benchDir, "tuning") && excluded.has(basename(f))));
    if (all.length - files.length !== excluded.size) throw new Error(`excluded ${all.length - files.length} files, tuning-emit-exclude.txt lists ${excluded.size}`);
    const list = join(benchDir, "filelists", "tuning-check-emit.txt");
    writeFileSync(list, files.join("\n") + "\n");
    return list;
}
const examples = Number(arg("--examples", "5"));
const outDir = join(repoRoot, "artifacts", "bench", "emit-equivalence");
mkdirSync(outDir, { recursive: true });

const run = (command, args) => {
    const started = Date.now();
    const r = spawnSync(command, args, { cwd: repoRoot, encoding: "utf8", maxBuffer: 1 << 30 });
    if (r.status !== 0) throw new Error(`${command} ${args.join(" ")} failed (${r.status}): ${r.stderr}`);
    return { output: JSON.parse(r.stdout.trim().split("\n").pop()), seconds: (Date.now() - started) / 1000 };
};
// typescript-go and tsrs are only reported: a failure of theirs is recorded (its first stderr line), not fatal.
const runReported = (command, args) => {
    try {
        return run(command, args);
    } catch (e) {
        const reason = String(e.message).split("\n").find(l => /panicked|panic:|error/i.test(l)) ?? String(e.message).split("\n")[0];
        return { output: { outputFiles: "failed", outputBytes: reason.trim().slice(0, 200) }, seconds: 0, failed: true };
    }
};
const readLines = file => readFileSync(file, "utf8").split("\n").filter(Boolean);

// The lines of a file, split at "\n" only (readline would also split at U+2028 and U+2029, which JSON.stringify leaves
// unescaped in the dump's strings).
async function* linesOf(file) {
    let rest = "";
    for await (const chunk of createReadStream(file, { encoding: "utf8" })) {
        const parts = (rest + chunk).split("\n");
        rest = parts.pop();
        yield* parts;
    }
    if (rest) yield rest;
}

// An emit dump: the outputs in write order (name, text), the emit diagnostic lines, emitSkipped.
async function readDump(file) {
    const outputs = [];
    const diagnostics = [];
    let emitSkipped;
    for await (const l of linesOf(file)) {
        if (!l) continue;
        const o = JSON.parse(l);
        if ("output" in o) outputs.push([o.output, o.text]);
        else if ("diagnostic" in o) diagnostics.push(o.diagnostic);
        else emitSkipped = o.emitSkipped;
    }
    return { outputs, diagnostics, emitSkipped };
}

// The first differing line of two texts (1-based), with both lines.
function firstDifference(a, b) {
    const la = a.split("\n"), lb = b.split("\n");
    for (let i = 0; i < Math.max(la.length, lb.length); i++) {
        if (la[i] !== lb[i]) return { line: i + 1, tsc: la[i] ?? null, cs: lb[i] ?? null, differingLines: la.filter((l, j) => l !== lb[j]).length + Math.max(0, lb.length - la.length) };
    }
    return null;
}

const kindOf = name => /\.d\.[cm]?ts$/.test(name) ? "d.ts" : "js";

console.log("| Repository | outputs tsc / C# | bytes tsc / C# | outputs identical | differing outputs (js / d.ts) | emit diagnostics tsc / C# | emitSkipped tsc / C# | check diagnostics identical | tsgo 7.0.2 outputs / bytes | tsrs 0.18.0 outputs / bytes |");
console.log("|---|---:|---:|---|---:|---:|---|---|---:|---:|");
const details = [];
for (const repo of repos) {
    const list = listOf(repo);
    const files = {
        tscEmit: join(outDir, `${repo}.tsc.emit.jsonl`), tscDiagnostics: join(outDir, `${repo}.tsc.diagnostics.txt`),
        csEmit: join(outDir, `${repo}.cs.emit.jsonl`), csDiagnostics: join(outDir, `${repo}.cs.diagnostics.txt`),
    };
    const tsc = run(node, ["--max-old-space-size=16384", "--stack-size=8192", import.meta.filename, "--tsc-dump", list, files.tscEmit, files.tscDiagnostics]);
    const cs = run(csDriver, ["--check", "--emit", "--single", "--dump-diagnostics", files.csDiagnostics, "--dump-emit", files.csEmit, list]);
    const tsgo = process.argv.includes("--skip-tsgo") ? null : runReported(parseGo, ["--check", "--emit", "--single", list]);
    const tsrs = process.argv.includes("--skip-tsrs") ? null : runReported(parseRs, ["--check", "--emit", "--single", list]);

    const a = await readDump(files.tscEmit);
    const b = await readDump(files.csEmit);
    const csByName = new Map(b.outputs);
    const tscNames = new Set(a.outputs.map(([n]) => n));
    const sameOrder = a.outputs.length === b.outputs.length && a.outputs.every(([n], i) => b.outputs[i][0] === n);
    const differing = [];
    const onlyTsc = [];
    for (const [name, text] of a.outputs) {
        const other = csByName.get(name);
        if (other === undefined) onlyTsc.push(name);
        else if (other !== text) differing.push({ name, kind: kindOf(name), ...firstDifference(text, other) });
    }
    const onlyCs = b.outputs.map(([n]) => n).filter(n => !tscNames.has(n));
    const emitDiagnosticsIdentical = a.diagnostics.length === b.diagnostics.length && a.diagnostics.every((l, i) => l === b.diagnostics[i]);
    const checkA = readLines(files.tscDiagnostics), checkB = readLines(files.csDiagnostics);
    const checkIdentical = checkA.length === checkB.length && checkA.every((l, i) => l === checkB[i]);
    const identical = differing.length === 0 && onlyTsc.length === 0 && onlyCs.length === 0 && sameOrder && emitDiagnosticsIdentical && a.emitSkipped === b.emitSkipped;
    const summary = {
        repo, identical, sameOrder, emitDiagnosticsIdentical, checkDiagnosticsIdentical: checkIdentical,
        tsc: { ...tsc.output, seconds: tsc.seconds },
        cs: { ...cs.output, seconds: cs.seconds },
        tsgo: tsgo && { ...tsgo.output, seconds: tsgo.seconds },
        tsrs: tsrs && { ...tsrs.output, seconds: tsrs.seconds },
        differingOutputs: differing.length,
        differingByKind: { js: differing.filter(d => d.kind === "js").length, "d.ts": differing.filter(d => d.kind === "d.ts").length },
        onlyTsc, onlyCs,
        emitDiagnostics: { tsc: a.diagnostics, cs: b.diagnostics },
        differing,
    };
    writeFileSync(join(outDir, `${repo}.json`), JSON.stringify(summary, null, 1));
    details.push(summary);
    const pair = (x, y) => `${x} / ${y}`;
    console.log(`| ${repo} | ${pair(tsc.output.outputFiles, cs.output.outputFiles)} | ${pair(tsc.output.outputBytes, cs.output.outputBytes)} | ${identical ? "yes" : "no"} | ${differing.length} (${summary.differingByKind.js} / ${summary.differingByKind["d.ts"]}) | ${pair(a.diagnostics.length, b.diagnostics.length)}${emitDiagnosticsIdentical ? "" : " (differ)"} | ${pair(a.emitSkipped, b.emitSkipped)} | ${checkIdentical ? "yes" : "no"} (${checkA.length}) | ${tsgo ? pair(tsgo.output.outputFiles, tsgo.output.outputBytes) : "-"} | ${tsrs ? pair(tsrs.output.outputFiles, tsrs.output.outputBytes) : "-"} |`);
}
for (const s of details) {
    if (s.identical) continue;
    console.log(`\n### ${s.repo}: ${s.differingOutputs} differing outputs, ${s.onlyTsc.length} only tsc, ${s.onlyCs.length} only C#${s.sameOrder ? "" : ", order differs"}${s.emitDiagnosticsIdentical ? "" : ", emit diagnostics differ"} (first ${examples}: first differing line)\n`);
    for (const d of s.differing.slice(0, examples)) {
        console.log(`- \`${d.name}\` line ${d.line} (${d.differingLines} lines)`);
        console.log(`  - tsc: \`${d.tsc}\``);
        console.log(`  - C#:  \`${d.cs}\``);
    }
    for (const n of s.onlyTsc.slice(0, examples)) console.log(`- only tsc: \`${n}\``);
    for (const n of s.onlyCs.slice(0, examples)) console.log(`- only C#: \`${n}\``);
}
