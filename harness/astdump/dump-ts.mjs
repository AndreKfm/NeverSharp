// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Oracle 1: canonical AST dump via the tsc 6.0.3 compiler API.
// Each unit is parsed in a worker thread; a unit that does not finish within
// the timeout is recorded as "timeout" (v6.0.3 has a known non-terminating
// path in scanJsDocToken, see docs/references.md section 2).
//
// Usage: node dump-ts.mjs [--corpus <dir>] [--timeout-ms <n>]

import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { isMainThread, parentPort, Worker } from "node:worker_threads";
import ts from "typescript";
import { artifactsDir } from "./common.mjs";
import { renderDump } from "./format.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;

const scriptKinds = { TS: ts.ScriptKind.TS, TSX: ts.ScriptKind.TSX, JS: ts.ScriptKind.JS, JSX: ts.ScriptKind.JSX, JSON: ts.ScriptKind.JSON };

// First declared name per value: later aliases (FirstToken, LastKeyword, ...) are ignored.
function firstNames(enumObject, singleBitOnly) {
    const names = new Map();
    for (const [name, value] of Object.entries(enumObject)) {
        if (typeof value !== "number" || names.has(value)) continue;
        if (singleBitOnly && (value === 0 || (value & (value - 1)) !== 0)) continue;
        names.set(value, name);
    }
    return names;
}

const kindNames = firstNames(ts.SyntaxKind, false);
const flagBits = [...firstNames(ts.NodeFlags, true)].sort((a, b) => a[0] - b[0]);

function flagNames(node) {
    const names = [];
    for (const [bit, name] of flagBits) {
        if ((node.flags & bit) === 0) continue;
        if (node.kind === ts.SyntaxKind.Identifier && bit === ts.NodeFlags.ContainsThis) names.push("IdentifierHasExtendedUnicodeEscape");
        else if (node.kind === ts.SyntaxKind.Identifier && bit === ts.NodeFlags.HasAsyncFunctions) names.push("IdentifierIsInJSDocNamespace");
        else names.push(name);
    }
    // Derived attribute, prefixed with "=": typescript-go encodes "has JSDoc" as
    // NodeFlags.HasJSDoc; TypeScript keeps the parsed comments in node.jsDoc.
    if (node.jsDoc?.length) names.push("=HasJSDoc");
    return names;
}

function dumpUnit(text, unit) {
    const sourceFile = ts.createSourceFile(
        unit.fileName,
        text,
        { languageVersion: ts.ScriptTarget.Latest, jsDocParsingMode: ts.JSDocParsingMode.ParseAll },
        /*setParentNodes*/ false,
        scriptKinds[unit.kind],
    );
    const nodes = [];
    const visit = (node, depth) => {
        nodes.push({ depth, kind: kindNames.get(node.kind), pos: node.pos, end: node.end, flags: flagNames(node) });
        ts.forEachChild(node, child => void visit(child, depth + 1));
    };
    visit(sourceFile, 0);
    const diagnostics = d => ({ code: d.code, start: d.start, length: d.length });
    return renderDump(nodes, {
        diag: sourceFile.parseDiagnostics.map(diagnostics),
        jsdiag: [],
        jsdocdiag: (sourceFile.jsDocDiagnostics ?? []).map(diagnostics),
    });
}

if (!isMainThread) {
    parentPort.on("message", ({ unit, input, output }) => {
        const text = readFileSync(input, "utf8");
        writeFileSync(output, dumpUnit(text, unit));
        parentPort.postMessage(unit.id);
    });
}
else {
    const corpus = arg("--corpus", join(artifactsDir, "equivalence"));
    const timeoutMs = Number(arg("--timeout-ms", "10000"));
    const outDir = join(corpus, "dumps", "ts");
    rmSync(outDir, { recursive: true, force: true });
    mkdirSync(outDir, { recursive: true });
    const { units } = JSON.parse(readFileSync(join(corpus, "manifest.json"), "utf8"));

    const outcomes = { ok: [], timeout: [], error: [] };
    let worker;
    const started = performance.now();
    const runOne = unit => new Promise(resolve => {
        worker ??= new Worker(new URL(import.meta.url));
        const timer = setTimeout(async () => {
            worker.removeAllListeners();
            await worker.terminate();
            worker = undefined;
            resolve("timeout");
        }, timeoutMs);
        worker.once("message", () => { clearTimeout(timer); worker.removeAllListeners("error"); resolve("ok"); });
        worker.once("error", err => { clearTimeout(timer); worker = undefined; resolve({ error: String(err?.stack ?? err) }); });
        worker.postMessage({ unit, input: join(corpus, "units", `${unit.id}.txt`), output: join(outDir, `${unit.id}.txt`) });
    });
    for (const unit of units) {
        const result = await runOne(unit);
        if (result === "ok") outcomes.ok.push(unit.id);
        else if (result === "timeout") outcomes.timeout.push(unit.id);
        else outcomes.error.push({ id: unit.id, error: result.error });
    }
    await worker?.terminate();
    writeFileSync(join(corpus, "dumps", "ts.outcomes.json"), JSON.stringify({ timeoutMs, ...outcomes }, null, 1));
    console.log(`ts: ok ${outcomes.ok.length}, timeout ${outcomes.timeout.length}, error ${outcomes.error.length} in ${((performance.now() - started) / 1000).toFixed(1)} s`);
}
