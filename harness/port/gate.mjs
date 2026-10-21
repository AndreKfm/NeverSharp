// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Runs the gates of a Track F module (pre-registration §4.2, §5.1; phase1-plan.md §4)
// in the checkout this script lives in. Porting attempts run it in their own worktree
// and iterate on its output; the orchestrator runs it again to decide.
//
//   Gate 2: dotnet build of src/NeverSharp.Faithful in Release with the Track F
//           analyzer ruleset (warnings are errors), plus the header check;
//   module gate: M1 = the unit tests in tests/NeverSharp.Faithful.Tests against
//           vectors computed by tsc 6.0.3 (harness/port/vectors-m1.mjs).
//
// Reference sources, tools and vectors are read from the main checkout (they are
// not in git): pass --main <path>, default: this checkout.
//
// Usage: node harness/port/gate.mjs --module M1 [--main D:/.../NeverSharp]
// Exit code 0 only if every gate passes; prints one line per gate and a JSON summary.

import { spawnSync } from "node:child_process";
import { cpSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const arg = (n, f) => process.argv.includes(n) ? process.argv[process.argv.indexOf(n) + 1] : f;
const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const main = arg("--main", root);
const moduleId = arg("--module", "M1");

// Worktrees lack the untracked inputs: link the vectors in by copying (small enough, read-only use).
const vectors = join(root, "artifacts", "vectors", moduleId);
if (!existsSync(vectors)) {
    const source = join(main, "artifacts", "vectors", moduleId);
    if (!existsSync(source)) throw new Error(`vectors missing: run node harness/port/vectors-${moduleId.toLowerCase()}.mjs in ${main}`);
    cpSync(source, vectors, { recursive: true });
}

const results = [];
function gate(name, command, args, opts = {}) {
    const started = Date.now();
    const r = spawnSync(command, args, { cwd: root, encoding: "utf8", maxBuffer: 1 << 28, env: { ...process.env, DOTNET_CLI_UI_LANGUAGE: "en" }, ...opts });
    const output = (r.stdout ?? "") + (r.stderr ?? "");
    const pass = r.status === 0;
    results.push({ gate: name, pass, seconds: Math.round((Date.now() - started) / 100) / 10 });
    console.log(`${pass ? "PASS" : "FAIL"}  ${name}`);
    if (!pass) console.log(output.split("\n").filter(l => /error|fail|differ|line \d+:|problem|missing/i.test(l)).slice(0, 80).join("\n") || output.slice(-4000));
    return pass;
}

const node = process.execPath;
gate("gate2-build", "dotnet", ["build", "src/NeverSharp.Faithful", "-c", "Release", "-nologo", "-v", "q", "-p:RunAnalyzersDuringBuild=true"]);
gate("gate2-headers", node, ["scripts/check-headers.mjs"]);
if (moduleId === "M1") {
    gate("m1-unit-tests", "dotnet", ["test", "--project", "tests/NeverSharp.Faithful.Tests", "-c", "Release"]);
}
const pass = results.every(r => r.pass);
console.log(JSON.stringify({ module: moduleId, pass, results }));
process.exit(pass ? 0 : 1);
