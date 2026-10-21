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
//           compared byte for byte with tsc 6.0.3's (dumps/ts-parsenone, harness/astdump/compare-ast.mjs).
//
// Reference sources, tools, the corpus and vectors are read from the main checkout
// (they are not in git): pass --main <path>, default: this checkout.
//
// Usage: node harness/port/gate.mjs --module M1|M2|M34 [--main D:/.../NeverSharp]
// Exit code 0 only if every gate passes; prints one line per gate and a JSON summary.

import { spawnSync } from "node:child_process";
import { cpSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const arg = (n, f) => process.argv.includes(n) ? process.argv[process.argv.indexOf(n) + 1] : f;
const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const main = arg("--main", root);
const moduleId = arg("--module", "M1");
if (!["M1", "M2", "M34"].includes(moduleId)) throw new Error(`no gate defined for ${moduleId}`);

// Worktrees lack the untracked inputs: copy the unit-test vectors in (read-only use).
for (const m of ["M1", "M2"]) {
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
gate("gate2-build", "dotnet", ["build", "src/NeverSharp.Faithful", "-c", "Release", "-nologo", "-v", "q", "-p:RunAnalyzersDuringBuild=true"]);
gate("gate2-headers", node, ["scripts/check-headers.mjs"]);
gate("unit-tests", "dotnet", ["test", "--project", "tests/NeverSharp.Faithful.Tests", "-c", "Release"]);
if (moduleId !== "M1") {
    const corpus = join(main, "artifacts", "equivalence");
    const actual = join(root, "artifacts", "dumps", "cs-faithful-tokens");
    const dumped = gate("g1a-dump", "dotnet", ["run", "-c", "Release", "--project", "tools/NeverSharp.Dump", "--", "tokens", "--corpus", corpus, "--out", actual],
        { timeout: 20 * 60 * 1000, showOutput: true });
    if (dumped) gate("g1a-compare", node, ["harness/astdump/compare-tokens.mjs", "--corpus", corpus, "--actual", actual, "--show", "15"], { showOutput: true });
}
if (moduleId === "M34") {
    // AST dump of every unit with JSDocParsingMode.ParseNone, byte-identical to tsc 6.0.3's (dumps/ts-parsenone).
    const corpus = join(main, "artifacts", "equivalence");
    const actual = join(root, "artifacts", "dumps", "cs-faithful-parsenone");
    const dumped = gate("g1-none-dump", "dotnet", ["run", "-c", "Release", "--project", "tools/NeverSharp.Dump", "--", "ast", "--jsdoc-mode", "none", "--corpus", corpus, "--out", actual],
        { timeout: 30 * 60 * 1000, showOutput: true });
    if (dumped) gate("g1-none-compare", node, ["harness/astdump/compare-ast.mjs", "--jsdoc-mode", "none", "--corpus", corpus, "--actual", actual, "--show", "15"], { showOutput: true });
}
const pass = results.every(r => r.pass);
console.log(JSON.stringify({ module: moduleId, pass, results }));
process.exit(pass ? 0 : 1);
