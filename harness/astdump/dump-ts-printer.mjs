// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Printer gate oracle (phase3-plan.md, module P1): tsc 6.0.3's printer applied to its own parse of every equivalence
// unit. Each unit is parsed as dump-ts.mjs parses it (same unit list, file reading, script kinds, setParentNodes false
// and the worker thread with timeout), with JSDocParsingMode ParseForTypeErrors by default, and printed with
// ts.createPrinter({ removeComments: true, newLine: LineFeed }).printFile(sourceFile). The dump of a unit is the
// printed text itself, written as UTF-8 (tools/NeverSharp.Dump.Optimized/PrinterDump.cs writes the same).
//
// Comment mode (Phase 4, module D0): with --comments keep the printer is ts.createPrinter({ removeComments: false,
// newLine: LineFeed }), so comment emission is compared too; the default (--comments remove) is the mode above, unchanged.
//
// Usage: node dump-ts-printer.mjs [--corpus <dir>] [--out <dir>] [--timeout-ms <n>] [--jsdoc-mode all|none|types]
//                                 [--comments remove|keep]
//   --jsdoc-mode: all = ParseAll, none = ParseNone, types = ParseForTypeErrors (default, the measured mode)
//   --out: default <corpus>/dumps/ts-printer-<mode> (ts-printer-<mode>-comments with --comments keep); the outcomes go
//          to <out>.outcomes.json

import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { isMainThread, parentPort, Worker } from "node:worker_threads";
import ts from "typescript";
import { artifactsDir } from "./common.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;

const scriptKinds = { TS: ts.ScriptKind.TS, TSX: ts.ScriptKind.TSX, JS: ts.ScriptKind.JS, JSX: ts.ScriptKind.JSX, JSON: ts.ScriptKind.JSON };
const jsDocModes = { all: ts.JSDocParsingMode.ParseAll, none: ts.JSDocParsingMode.ParseNone, types: ts.JSDocParsingMode.ParseForTypeErrors };

let printer;

function dumpUnit(text, unit, jsDocParsingMode, removeComments) {
    const sourceFile = ts.createSourceFile(
        unit.fileName,
        text,
        { languageVersion: ts.ScriptTarget.Latest, jsDocParsingMode },
        /*setParentNodes*/ false,
        scriptKinds[unit.kind],
    );
    printer ??= ts.createPrinter({ removeComments, newLine: ts.NewLineKind.LineFeed });
    return printer.printFile(sourceFile);
}

if (!isMainThread) {
    parentPort.on("message", ({ unit, input, output, jsDocParsingMode, removeComments }) => {
        const text = readFileSync(input, "utf8");
        writeFileSync(output, dumpUnit(text, unit, jsDocParsingMode, removeComments));
        parentPort.postMessage(unit.id);
    });
}
else {
    const corpus = arg("--corpus", join(artifactsDir, "equivalence"));
    const timeoutMs = Number(arg("--timeout-ms", "10000"));
    const jsdocMode = arg("--jsdoc-mode", "types");
    if (!(jsdocMode in jsDocModes)) throw new Error("--jsdoc-mode must be all, none or types");
    const jsDocParsingMode = jsDocModes[jsdocMode];
    const comments = arg("--comments", "remove");
    if (comments !== "remove" && comments !== "keep") throw new Error("--comments must be remove or keep");
    const removeComments = comments === "remove";
    const dumpName = `ts-printer-${jsdocMode}${removeComments ? "" : "-comments"}`;
    const outDir = arg("--out", join(corpus, "dumps", dumpName));
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
        worker.postMessage({ unit, input: join(corpus, "units", `${unit.id}.txt`), output: join(outDir, `${unit.id}.txt`), jsDocParsingMode, removeComments });
    });
    for (const unit of units) {
        const result = await runOne(unit);
        if (result === "ok") outcomes.ok.push(unit.id);
        else if (result === "timeout") outcomes.timeout.push(unit.id);
        else outcomes.error.push({ id: unit.id, error: result.error });
    }
    await worker?.terminate();
    writeFileSync(join(dirname(outDir), `${outDir.split(/[\\/]/).pop()}.outcomes.json`), JSON.stringify({ timeoutMs, ...outcomes }, null, 1));
    console.log(`${dumpName}: ok ${outcomes.ok.length}, timeout ${outcomes.timeout.length}, error ${outcomes.error.length} in ${((performance.now() - started) / 1000).toFixed(1)} s`);
}
