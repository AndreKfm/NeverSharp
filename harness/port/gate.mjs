// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Runs the gates of a Track F module (pre-registration §4.2, §5.1; phase1-plan.md §4)
// in the checkout this script lives in. Porting attempts run it in their own worktree
// and iterate on its output; the orchestrator runs it again to decide.
//
//   Gate 2: dotnet build of src/NeverSharp.Faithful in Release with the Track F
//           analyzer ruleset (warnings are errors), plus the header check;
//   unit tests: tests/NeverSharp.Faithful.Tests against vectors computed by tsc 6.0.3
//           (harness/port/vectors-m1.mjs, vectors-m2.mjs); all tests run in every module's
//           gate from M1 on (regression);
//   M2: G1a, the token-stream dump of every equivalence unit (tools/NeverSharp.Dump)
//           compared byte for byte with tsc 6.0.3 (harness/astdump/compare-tokens.mjs), in every
//           module from M2 on;
//   M34: the AST dump with JSDocParsingMode.ParseNone (tools/NeverSharp.Dump ast --jsdoc-mode none)
//           compared byte for byte with tsc 6.0.3's (dumps/ts-parsenone, harness/astdump/compare-ast.mjs);
//           also part of the M5 gate;
//   M5: Gate 1, the AST dump with JSDocParsingMode.ParseAll including the JSDoc section (dumps/ts).
//
// Reference sources, tools, the corpus and vectors are read from the main checkout
// (they are not in git): pass --main <path>, default: this checkout.
//
// Usage: node harness/port/gate.mjs --module M1|M2|M34|M5 [--main D:/.../NeverSharp]
//        node harness/port/gate.mjs --track optimized [--main ...]   (Track O: all gates; Gate 2 with
//        the Track O ruleset incl. the NoAlloc analyzer; tests/NeverSharp.Optimized.Tests and
//        tools/NeverSharp.Dump.Optimized, i.e. the same tests and dump writers built against Track O)
//        node harness/port/gate.mjs --track optimized --phase binder [--jsdoc-mode all,types] [--sections symbols,flow,diagnostics]
//        [--main ...]   (Phase 2, Gate B: Track O Gate 2 build + headers, the tsc 6.0.3 binder dumps
//        (harness/astdump/dump-ts-binder.mjs; reused while the oracle script, typescript and the manifest are unchanged),
//        the Track O binder dumps (NeverSharp.Dump.Optimized binder) and harness/astdump/compare-binder.mjs per JSDoc
//        mode; no parser gates)
//        node harness/port/gate.mjs --track optimized --phase printer [--jsdoc-mode types] [--main ...]
//        (Phase 3, module P1, printer gate P: Track O Gate 2 build + headers, the tsc 6.0.3 printer dumps
//        (harness/astdump/dump-ts-printer.mjs; reused while the oracle script, typescript and the manifest are unchanged),
//        the Track O printer dumps (NeverSharp.Dump.Optimized printer) and harness/astdump/compare-printer.mjs per JSDoc
//        mode; no parser gates)
// Exit code 0 only if every gate passes; prints one line per gate and a JSON summary.

import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { cpSync, existsSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const arg = (n, f) => process.argv.includes(n) ? process.argv[process.argv.indexOf(n) + 1] : f;
const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const main = arg("--main", root);
const track = arg("--track", "faithful");
if (track !== "faithful" && track !== "optimized") throw new Error("--track must be faithful or optimized");
const phase = arg("--phase", "parser");
if (phase !== "parser" && phase !== "binder" && phase !== "printer") throw new Error("--phase must be parser, binder or printer");
if (phase === "binder" && track !== "optimized") throw new Error("--phase binder is defined for --track optimized only (phase2-plan.md)");
if (phase === "printer" && track !== "optimized") throw new Error("--phase printer is defined for --track optimized only (phase3-plan.md)");
// Track O is always checked completely (it starts from the finished Track F): every gate of M5.
const moduleId = track === "optimized" ? "M5" : arg("--module", "M1");
if (!["M1", "M2", "M34", "M5"].includes(moduleId)) throw new Error(`no gate defined for ${moduleId}`);
const Track = track === "optimized" ? "Optimized" : "Faithful";
const dumpProject = track === "optimized" ? "tools/NeverSharp.Dump.Optimized" : "tools/NeverSharp.Dump";
const testProject = track === "optimized" ? "tests/NeverSharp.Optimized.Tests" : "tests/NeverSharp.Faithful.Tests";

// Worktrees lack the untracked inputs: copy the unit-test vectors in (read-only use).
for (const m of phase !== "parser" ? [] : ["M1", "M2"]) {
    const vectors = join(root, "artifacts", "vectors", m);
    if (existsSync(vectors)) continue;
    const source = join(main, "artifacts", "vectors", m);
    if (!existsSync(source)) throw new Error(`vectors missing: run node harness/port/vectors-${m.toLowerCase()}.mjs in ${main}`);
    cpSync(source, vectors, { recursive: true });
}

const results = [];
function gate(name, command, args, { showOutput = false, ...opts } = {}) {
    const started = Date.now();
    const r = spawnSync(command, args, { cwd: root, encoding: "utf8", maxBuffer: 1 << 28, env: { ...process.env, DOTNET_CLI_UI_LANGUAGE: "en" }, ...opts });
    const output = (r.stdout ?? "") + (r.stderr ?? "") + (r.error ? `\n${r.error.message}` : "");
    const pass = r.status === 0;
    results.push({ gate: name, pass, seconds: Math.round((Date.now() - started) / 100) / 10 });
    console.log(`${pass ? "PASS" : "FAIL"}  ${name}`);
    if (!pass && showOutput) console.log(output.slice(-6000));
    else if (!pass) console.log(output.split("\n").filter(l => /error|fail|differ|line \d+:|problem|missing/i.test(l)).slice(0, 80).join("\n") || output.slice(-4000));
    return pass;
}

const node = process.execPath;
gate("gate2-build", "dotnet", ["build", `src/NeverSharp.${Track}`, "-c", "Release", "-nologo", "-v", "q", "-p:RunAnalyzersDuringBuild=true"]);
gate("gate2-headers", node, ["scripts/check-headers.mjs"]);
if (phase === "binder") {
    // Gate B (phase2-plan.md): per JSDoc mode the tsc 6.0.3 binder dump (the oracle), Track O's binder dump and the compare.
    const corpus = join(main, "artifacts", "equivalence");
    const modes = arg("--jsdoc-mode", "all,types").split(",");
    const sections = arg("--sections", "symbols,flow,diagnostics");
    const oracle = join(root, "harness", "astdump", "dump-ts-binder.mjs");
    // The oracle's dumps are reused while its inputs are unchanged: the script, typescript's version and the manifest.
    const tsVersion = JSON.parse(readFileSync(createRequire(oracle).resolve("typescript/package.json"), "utf8")).version;
    // Line endings normalized: checkouts differ (CRLF in the main checkout, LF in worktrees), the script does not.
    const lf = file => readFileSync(file, "utf8").replaceAll("\r\n", "\n");
    const stamp = createHash("sha256").update(lf(oracle)).update(tsVersion).update(readFileSync(join(corpus, "manifest.json"))).digest("hex");
    const valid = dir => existsSync(`${dir}.stamp`) && existsSync(`${dir}.outcomes.json`) && readFileSync(`${dir}.stamp`, "utf8") === stamp;
    let dumpBuilt = false;
    for (const mode of modes) {
        // Expected: the main checkout's dumps if current (read only), else this checkout's, else a new oracle run here.
        let expected = join(corpus, "dumps", `ts-binder-${mode}`);
        if (!valid(expected)) {
            expected = join(root, "artifacts", "dumps", `ts-binder-${mode}`);
            if (valid(expected)) console.log(`(oracle dumps of mode ${mode} reused: ${expected})`);
            else if (gate(`oracle-${mode}`, node, [oracle, "--corpus", corpus, "--jsdoc-mode", mode, "--out", expected], { timeout: 60 * 60 * 1000, showOutput: true })) writeFileSync(`${expected}.stamp`, stamp);
            else continue;
        }
        else console.log(`(oracle dumps of mode ${mode} reused: ${expected})`);
        // Built once (the project, the parser and the binder); then run without building per mode.
        if (!dumpBuilt && !(dumpBuilt = gate("binder-dump-build", "dotnet", ["build", dumpProject, "-c", "Release", "-nologo", "-v", "q"], { showOutput: true }))) break;
        const actual = join(root, "artifacts", "dumps", `cs-${track}-binder-${mode}`);
        const dumped = gate(`binder-dump-${mode}`, "dotnet", ["run", "-c", "Release", "--no-build", "--project", dumpProject, "--", "binder", "--jsdoc-mode", mode, "--corpus", corpus, "--out", actual],
            { timeout: 30 * 60 * 1000, showOutput: true });
        if (dumped) gate(`binder-compare-${mode}`, node, ["harness/astdump/compare-binder.mjs", "--corpus", corpus, "--jsdoc-mode", mode, "--expected", expected, "--actual", actual, "--sections", sections, "--show", "15"], { showOutput: true });
    }
    const pass = results.every(r => r.pass);
    console.log(JSON.stringify({ track, phase, modes, sections: sections.split(","), pass, results }));
    process.exit(pass ? 0 : 1);
}
if (phase === "printer") {
    // Printer gate P (phase3-plan.md, module P1): per JSDoc mode the tsc 6.0.3 printer dump (the oracle), Track O's
    // printer dump and the compare; the same oracle caching as Gate B.
    const corpus = join(main, "artifacts", "equivalence");
    const modes = arg("--jsdoc-mode", "types").split(",");
    const oracle = join(root, "harness", "astdump", "dump-ts-printer.mjs");
    const tsVersion = JSON.parse(readFileSync(createRequire(oracle).resolve("typescript/package.json"), "utf8")).version;
    const lf = file => readFileSync(file, "utf8").replaceAll("\r\n", "\n");
    const stamp = createHash("sha256").update(lf(oracle)).update(tsVersion).update(readFileSync(join(corpus, "manifest.json"))).digest("hex");
    const valid = dir => existsSync(`${dir}.stamp`) && existsSync(`${dir}.outcomes.json`) && readFileSync(`${dir}.stamp`, "utf8") === stamp;
    let dumpBuilt = false;
    for (const mode of modes) {
        let expected = join(corpus, "dumps", `ts-printer-${mode}`);
        if (!valid(expected)) {
            expected = join(root, "artifacts", "dumps", `ts-printer-${mode}`);
            if (valid(expected)) console.log(`(oracle dumps of mode ${mode} reused: ${expected})`);
            else if (gate(`oracle-${mode}`, node, [oracle, "--corpus", corpus, "--jsdoc-mode", mode, "--out", expected], { timeout: 60 * 60 * 1000, showOutput: true })) writeFileSync(`${expected}.stamp`, stamp);
            else continue;
        }
        else console.log(`(oracle dumps of mode ${mode} reused: ${expected})`);
        if (!dumpBuilt && !(dumpBuilt = gate("printer-dump-build", "dotnet", ["build", dumpProject, "-c", "Release", "-nologo", "-v", "q"], { showOutput: true }))) break;
        const actual = join(root, "artifacts", "dumps", `cs-${track}-printer-${mode}`);
        const dumped = gate(`printer-dump-${mode}`, "dotnet", ["run", "-c", "Release", "--no-build", "--project", dumpProject, "--", "printer", "--jsdoc-mode", mode, "--corpus", corpus, "--out", actual],
            { timeout: 30 * 60 * 1000, showOutput: true });
        if (dumped) gate(`printer-compare-${mode}`, node, ["harness/astdump/compare-printer.mjs", "--corpus", corpus, "--jsdoc-mode", mode, "--expected", expected, "--actual", actual, "--show", "15"], { showOutput: true });
    }
    const pass = results.every(r => r.pass);
    console.log(JSON.stringify({ track, phase, modes, pass, results }));
    process.exit(pass ? 0 : 1);
}
gate("unit-tests", "dotnet", ["test", "--project", testProject, "-c", "Release"]);
if (moduleId !== "M1") {
    const corpus = join(main, "artifacts", "equivalence");
    const actual = join(root, "artifacts", "dumps", `cs-${track}-tokens`);
    const dumped = gate("g1a-dump", "dotnet", ["run", "-c", "Release", "--project", dumpProject, "--", "tokens", "--corpus", corpus, "--out", actual],
        { timeout: 20 * 60 * 1000, showOutput: true });
    if (dumped) gate("g1a-compare", node, ["harness/astdump/compare-tokens.mjs", "--corpus", corpus, "--actual", actual, "--show", "15"], { showOutput: true });
}
if (moduleId === "M34" || moduleId === "M5") {
    // AST dump of every unit with JSDocParsingMode.ParseNone, byte-identical to tsc 6.0.3's (dumps/ts-parsenone).
    const corpus = join(main, "artifacts", "equivalence");
    const actual = join(root, "artifacts", "dumps", `cs-${track}-parsenone`);
    const dumped = gate("g1-none-dump", "dotnet", ["run", "-c", "Release", "--project", dumpProject, "--", "ast", "--jsdoc-mode", "none", "--corpus", corpus, "--out", actual],
        { timeout: 30 * 60 * 1000, showOutput: true });
    if (dumped) gate("g1-none-compare", node, ["harness/astdump/compare-ast.mjs", "--jsdoc-mode", "none", "--corpus", corpus, "--actual", actual, "--show", "15"], { showOutput: true });
}
if (track === "optimized") {
    // Step O1: Track O reads files itself; its reader against ts.sys.readFile's semantics on every corpus file.
    gate("reader-check", "dotnet", ["run", "-c", "Release", "--project", "tools/NeverSharp.ReaderCheck", "--", "--main", main], { showOutput: true });
}
if (moduleId === "M5") {
    // Gate 1 proper: AST dump with JSDocParsingMode.ParseAll, including the JSDoc section, byte-identical to tsc 6.0.3's (dumps/ts).
    const corpus = join(main, "artifacts", "equivalence");
    const actual = join(root, "artifacts", "dumps", `cs-${track}`);
    const dumped = gate("g1-dump", "dotnet", ["run", "-c", "Release", "--project", dumpProject, "--", "ast", "--jsdoc-mode", "all", "--corpus", corpus, "--out", actual],
        { timeout: 30 * 60 * 1000, showOutput: true });
    if (dumped) gate("g1-compare", node, ["harness/astdump/compare-ast.mjs", "--jsdoc-mode", "all", "--corpus", corpus, "--actual", actual, "--show", "15"], { showOutput: true });
}
const pass = results.every(r => r.pass);
console.log(JSON.stringify({ track, module: moduleId, pass, results }));
process.exit(pass ? 0 : 1);
