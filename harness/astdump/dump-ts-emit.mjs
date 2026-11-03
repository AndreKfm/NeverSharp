// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Gate D oracle (phase4-plan.md, module D0): canonical emit dump via the tsc 6.0.3 compiler API. Every equivalence unit
// (same unit list, file reading, script kinds, host, library sharing and worker thread with timeout as
// dump-ts-checker.mjs) becomes a one-file program with Gate C's options plus declaration: true and newLine: lf
// (preregistration.md §19), and is emitted with program.emit(unitFile, writeFile): the unit file only, written to memory.
// Library files are declaration files and never emitted. The emit resolver checks the unit file only (getEmitResolver
// with the target file), so the checker state is that of Gate C's dump.
//
// Usage: node dump-ts-emit.mjs [--corpus <dir>] [--out <dir>] [--timeout-ms <n>] [--units <file with one unit id per line>]
//                              [--no-coverage]
//   --out: default <corpus>/dumps/ts-emit-types; outcomes go to <out>.outcomes.json, the coverage report to
//          <out>.coverage.json (per unit) and <out>.coverage.txt (summary); JSDoc mode is ParseForTypeErrors
//
// Emit dump format, version 1 (tools/NeverSharp.Dump.Optimized/EmitDump.cs writes the same):
//
//   emit-dump 1
//   file <fileName> <TS|TSX|JS|JSX|JSON>
//   options <JSON of the options given to createProgram>
//   == outputs
//   output <name>               each file passed to writeFile, in write order; <name> relative to the unit's folder
//   |<line>                     its text split at "\n" only, each piece prefixed with "|" (a text ending in "\n" ends
//                               with a line "|")
//   == diagnostics
//   emit <diag>                 the diagnostics of the emit result (declaration emit diagnostics), in their order
//     related <diag>            a diagnostic's relatedInformation, after it
//   <diag> as in dump-ts-checker.mjs: <file> <start> <length> <code> <Category> "<flattened message>"[ unnecessary][ deprecated]
//   == emitSkipped
//   <true|false>
//
// Coverage report (not part of the gate): after the dump of a unit, the worker runs the default-option transformers
// (ts.getTransformers(options)) again with ts.transformNodes on the same program and the unit's emit resolver, each
// factory wrapped. A transformer "touches" a unit when its result prints differently from its input as printed
// before the transformer ran (ts.createPrinter with the emit's printer options; the declaration transformer with the
// declaration printer's), or, for the declaration transformer, when it reports diagnostics. TypeScript's sources are
// not changed. The ES module transformer is the module transformer of module kind ES2022
// (transformImpliedNodeFormatDependentModule, which picks the CommonJS transformer for .cts/.cjs files).

import { mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join, posix } from "node:path";
import { isMainThread, parentPort, Worker } from "node:worker_threads";
import ts from "typescript";
import { artifactsDir } from "./common.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;

const scriptKinds = { TS: ts.ScriptKind.TS, TSX: ts.ScriptKind.TSX, JS: ts.ScriptKind.JS, JSX: ts.ScriptKind.JSX, JSON: ts.ScriptKind.JSON };
const jsDocParsingMode = ts.JSDocParsingMode.ParseForTypeErrors;
const libFolder = "/.ts/";
// Gate C's options (dump-ts-checker.mjs optionsFor) plus declaration and newLine (preregistration.md §19).
export const optionsFor = unit => unit.kind === "JS" || unit.kind === "JSX"
    ? { noResolve: true, allowJs: true, declaration: true, newLine: ts.NewLineKind.LineFeed }
    : { noResolve: true, declaration: true, newLine: ts.NewLineKind.LineFeed };
const libDir = dirname(createRequire(import.meta.url).resolve("typescript/lib/lib.d.ts"));

// Library texts and parsed source files, per thread (as dump-ts-checker.mjs).
const libTexts = new Map(readdirSync(libDir).filter(f => /^lib\..*\.d\.ts$/.test(f)).map(f => [libFolder + f, null]));
const libFiles = new Map();

function createHost(unitName, unitText, unitKind, outputs) {
    return {
        getSourceFile(fileName, languageVersionOrOptions) {
            if (fileName === unitName) return ts.createSourceFile(fileName, unitText, languageVersionOrOptions, false, unitKind);
            if (!libTexts.has(fileName)) return undefined;
            let file = libFiles.get(fileName);
            if (!file) {
                const text = libTexts.get(fileName) ?? readFileSync(join(libDir, fileName.slice(libFolder.length)), "utf8");
                libTexts.set(fileName, text);
                file = ts.createSourceFile(fileName, text, languageVersionOrOptions, false, ts.ScriptKind.TS);
                libFiles.set(fileName, file);
            }
            return file;
        },
        getDefaultLibFileName: options => libFolder + ts.getDefaultLibFileName(options),
        getDefaultLibLocation: () => libFolder.slice(0, -1),
        writeFile: (fileName, text) => { outputs?.push({ fileName, text }); },
        getCurrentDirectory: () => "/",
        getDirectories: () => [],
        getCanonicalFileName: f => f,
        useCaseSensitiveFileNames: () => true,
        getNewLine: () => "\n",
        fileExists: f => f === unitName || libTexts.has(f),
        readFile: f => f === unitName ? unitText : undefined,
        directoryExists: () => false,
        jsDocParsingMode,
    };
}

const isLibName = name => name.startsWith(libFolder);
const baseName = name => name.slice(name.lastIndexOf("/") + 1);

function diagText(d, unitName) {
    const file = d.file === undefined ? "-" : d.file.fileName === unitName ? "." : isLibName(d.file.fileName) ? baseName(d.file.fileName) : d.file.fileName;
    const message = JSON.stringify(ts.flattenDiagnosticMessageText(d.messageText, "\n"));
    return `${file} ${d.start ?? "-"} ${d.length ?? "-"} ${d.code} ${ts.DiagnosticCategory[d.category]} ${message}${d.reportsUnnecessary ? " unnecessary" : ""}${d.reportsDeprecated ? " deprecated" : ""}`;
}

function writeDiagnostics(tag, list, unitName) {
    let out = "";
    for (const d of list) {
        out += `${tag} ${diagText(d, unitName)}\n`;
        for (const r of d.relatedInformation ?? []) out += `  related ${diagText(r, unitName)}\n`;
    }
    return out;
}

export function dumpUnit(text, unit) {
    const outputs = [];
    const options = optionsFor(unit);
    const host = createHost(unit.fileName, text, scriptKinds[unit.kind], outputs);
    const program = ts.createProgram([unit.fileName], options, host);
    const sourceFile = program.getSourceFile(unit.fileName);
    // The unit file only (a missing unit file emits nothing, as program.emit of no file in the program would).
    const result = sourceFile ? program.emit(sourceFile) : { diagnostics: [], emitSkipped: false };
    const unitDir = posix.dirname(unit.fileName);
    let out = `emit-dump 1\nfile ${unit.fileName} ${unit.kind}\noptions ${JSON.stringify(options)}\n== outputs\n`;
    for (const o of outputs) {
        out += `output ${posix.relative(unitDir, o.fileName)}\n`;
        for (const line of o.text.split("\n")) out += `|${line}\n`;
    }
    out += "== diagnostics\n" + writeDiagnostics("emit", result.diagnostics, unit.fileName);
    out += `== emitSkipped\n${result.emitSkipped}\n`;
    return { out, program, sourceFile };
}

// The coverage pass (see the header).
const transformerNames = new Map([
    [ts.transformTypeScript, "TypeScript"],
    [ts.transformESNext, "ESNext"],
    [ts.transformESDecorators, "ES decorators"],
    [ts.transformClassFields, "class fields"],
    [ts.transformImpliedNodeFormatDependentModule, "ES module"],
    [ts.transformECMAScriptModule, "ES module"],
    [ts.transformDeclarations, "declarations"],
]);
export const coverageNames = ["TypeScript", "ESNext", "ES decorators", "class fields", "ES module", "declarations"];

function emitHostOf(program) {
    const own = {
        getCanonicalFileName: f => f,
        getCurrentDirectory: () => "/",
        getCompilerOptions: () => program.getCompilerOptions(),
        isEmitBlocked: () => false,
        useCaseSensitiveFileNames: () => true,
        writeFile: () => { },
        readFile: () => undefined,
        fileExists: () => false,
        redirectTargetsMap: new Map(),
    };
    return new Proxy(own, {
        get(target, name) {
            if (name in target) return target[name];
            const member = program[name];
            return typeof member === "function" ? member.bind(program) : member;
        },
    });
}

function coverageOf(program, sourceFile, unit) {
    if (!sourceFile || sourceFile.isDeclarationFile || unit.kind === "JSON") return { emitted: false, touched: [] };
    const options = program.getCompilerOptions();
    const resolver = program.getTypeChecker().getEmitResolver(sourceFile, undefined, false);
    const host = emitHostOf(program);
    const touched = new Set();
    const scriptPrinter = ts.createPrinter({ removeComments: false, newLine: ts.NewLineKind.LineFeed, target: ts.getEmitScriptTarget(options) });
    const declarationPrinter = ts.createPrinter({ removeComments: false, newLine: ts.NewLineKind.LineFeed, onlyPrintJsDocStyle: true, noEmitHelpers: true });
    const wrap = (factory, printer) => {
        const name = transformerNames.get(factory) ?? factory.name;
        return context => {
            const inner = factory(context);
            return node => {
                // The input is printed before the transformer runs: transformers also set emit flags on the parse tree
                // nodes they keep (e.g. the declaration transformer's SingleLine on tuple types).
                const before = ts.isSourceFile(node) ? printer.printFile(node) : undefined;
                const result = inner(node);
                if (ts.isSourceFile(result) && before !== undefined) {
                    if (printer.printFile(result) !== before) touched.add(name);
                }
                else if (result !== node) touched.add(name);
                return result;
            };
        };
    };
    const { scriptTransformers, declarationTransformers } = ts.getTransformers(options);
    const script = ts.transformNodes(resolver, host, ts.factory, options, [sourceFile], scriptTransformers.map(f => wrap(f, scriptPrinter)), false);
    script.dispose();
    const declaration = ts.transformNodes(resolver, host, ts.factory, options, [sourceFile], declarationTransformers.map(f => wrap(f, declarationPrinter)), false);
    if (declaration.diagnostics?.length) touched.add("declarations");
    declaration.dispose();
    return { emitted: true, touched: coverageNames.filter(n => touched.has(n)) };
}

function warmUp() {
    const program = ts.createProgram(["/__empty__.ts"], optionsFor({ kind: "TS" }), createHost("/__empty__.ts", "", ts.ScriptKind.TS));
    program.getTypeChecker();
}

if (!isMainThread) {
    let warmedUp = false;
    parentPort.on("message", ({ unit, input, output, coverage }) => {
        if (!warmedUp) {
            warmUp();
            warmedUp = true;
        }
        if (unit === undefined) { parentPort.postMessage("ready"); return; }
        const text = readFileSync(input, "utf8");
        const { out, program, sourceFile } = dumpUnit(text, unit);
        writeFileSync(output, out);
        let cover;
        if (coverage) {
            try {
                cover = coverageOf(program, sourceFile, unit);
            }
            catch (e) {
                cover = { error: String(e?.message ?? e) };
            }
        }
        parentPort.postMessage({ id: unit.id, coverage: cover });
    });
}
else {
    const corpus = arg("--corpus", join(artifactsDir, "equivalence"));
    const timeoutMs = Number(arg("--timeout-ms", "120000"));
    const coverage = !process.argv.includes("--no-coverage");
    const dumpName = "ts-emit-types";
    const outDir = arg("--out", join(corpus, "dumps", dumpName));
    rmSync(outDir, { recursive: true, force: true });
    mkdirSync(outDir, { recursive: true });
    let { units } = JSON.parse(readFileSync(join(corpus, "manifest.json"), "utf8"));
    const unitsFile = arg("--units", undefined);
    if (unitsFile) {
        const ids = new Set(readFileSync(unitsFile, "utf8").split(/\r?\n/).filter(Boolean));
        units = units.filter(u => ids.has(u.id));
    }

    const started = performance.now();
    const outcomes = { ok: [], timeout: [], error: [] };
    const coverageByUnit = {};
    let worker;
    const startWorker = () => new Promise((resolve, reject) => {
        worker = new Worker(new URL(import.meta.url));
        worker.once("message", () => { worker.removeAllListeners("error"); resolve(); });
        worker.once("error", reject);
        worker.postMessage({});
    });
    const runOne = unit => new Promise(resolve => {
        const timer = setTimeout(async () => {
            worker.removeAllListeners();
            await worker.terminate();
            worker = undefined;
            resolve("timeout");
        }, timeoutMs);
        worker.once("message", message => {
            clearTimeout(timer);
            worker.removeAllListeners("error");
            if (message.coverage) coverageByUnit[unit.id] = message.coverage;
            resolve("ok");
        });
        worker.once("error", err => { clearTimeout(timer); worker = undefined; resolve({ error: String(err?.stack ?? err) }); });
        worker.postMessage({ unit, input: join(corpus, "units", `${unit.id}.txt`), output: join(outDir, `${unit.id}.txt`), coverage });
    });
    for (const unit of units) {
        if (!worker) await startWorker();
        const result = await runOne(unit);
        if (result === "ok") outcomes.ok.push(unit.id);
        else if (result === "timeout") outcomes.timeout.push(unit.id);
        else outcomes.error.push({ id: unit.id, error: result.error });
    }
    await worker?.terminate();
    const parent = dirname(outDir);
    const base = outDir.split(/[\\/]/).pop();
    writeFileSync(join(parent, `${base}.outcomes.json`), JSON.stringify({ timeoutMs, ...outcomes }, null, 1));
    if (coverage) {
        writeFileSync(join(parent, `${base}.coverage.json`), JSON.stringify(coverageByUnit));
        // Summary: units per transformer, units needing none (of the emitted units), coverage errors.
        const counts = Object.fromEntries(coverageNames.map(n => [n, 0]));
        let emitted = 0, none = 0, scriptNone = 0, onlyTypeScript = 0, errors = 0;
        for (const c of Object.values(coverageByUnit)) {
            if (c.error) { errors++; continue; }
            if (!c.emitted) continue;
            emitted++;
            for (const n of c.touched) counts[n]++;
            if (c.touched.length === 0) none++;
            const scriptTouched = c.touched.filter(n => n !== "declarations");
            if (scriptTouched.length === 0) scriptNone++;
            if (scriptTouched.length === 1 && scriptTouched[0] === "TypeScript") onlyTypeScript++;
        }
        let summary = `transformer coverage (${units.length} units, ${emitted} emitted, ${errors} coverage errors)\n`;
        for (const n of coverageNames) summary += `${n.padEnd(14)} ${String(counts[n]).padStart(6)}\n`;
        summary += `none (no script transformer and no declaration change): ${none}\n`;
        summary += `no script transformer (js = emit of the parse tree): ${scriptNone}\n`;
        summary += `only the TypeScript transformer among the script transformers: ${onlyTypeScript}\n`;
        writeFileSync(join(parent, `${base}.coverage.txt`), summary);
        console.log(summary.trimEnd());
    }
    console.log(`${dumpName}: ok ${outcomes.ok.length}, timeout ${outcomes.timeout.length}, error ${outcomes.error.length} in ${((performance.now() - started) / 1000).toFixed(1)} s`);
}
