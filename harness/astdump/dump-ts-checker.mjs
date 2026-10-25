// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Gate C oracle (phase3-plan.md, module C0): canonical checker dump via the tsc 6.0.3 compiler API.
// Every equivalence unit (same unit list, file reading, script kinds and worker thread with timeout as dump-ts.mjs and
// dump-ts-binder.mjs) becomes a one-file program: ts.createProgram([unit.fileName], { noResolve: true }, host) with a
// CompilerHost that serves the unit from memory and the default library files from typescript 6.0.3's lib folder
// (harness/astdump/node_modules/typescript/lib) under the virtual folder /.ts/ (the folder TypeScript's own test
// harness uses for them). Nothing else exists for the host: no directories, no @types, no other files.
//
// The library source files are parsed once per worker and shared by all programs of that worker, as TypeScript's test
// harness and language service share them (a SourceFile is bound once; each checker merges the bound lib files into
// its own globals). Before the first unit the worker creates one program with an empty file and its checker, so every
// unit sees the library in the same state (parsed, bound, merged once), also after a worker restart.
//
// Usage: node dump-ts-checker.mjs [--corpus <dir>] [--out <dir>] [--timeout-ms <n>] [--jsdoc-mode types|all|none]
//                                 [--units <file with one unit id per line>]
//   --timeout-ms: per unit, default 120000 (two units take 25-30 s each; a timeout near the limit would make the
//                 outcomes depend on the machine's load)
//   --jsdoc-mode: types = ParseForTypeErrors (default, the measured mode), all = ParseAll, none = ParseNone; it is the
//                 program's host.jsDocParsingMode (unit and library files)
//   --out: default <corpus>/dumps/ts-checker-<mode>; outcomes go to <out>.outcomes.json, the library's own diagnostics
//          to <out>.lib.txt
//   --units: only these units (e.g. a sample); default: all units of the manifest
//
// Checker dump format, version 1 (tools/NeverSharp.Dump.Optimized/CheckerDump.cs writes the same):
//
//   checker-dump 1
//   file <fileName> <TS|TSX|JS|JSX|JSON> inProgram=<true|false> hadErrors=<true|false>
//   options <JSON of the options given to createProgram>
//   computed <name>=<JSON value> ...      ts.computedOptions in declaration order, each computeValue(options)
//   libs <n> <basename> ...               the program's library files in program order
//   == diagnostics
//   option <diag>                         program.getOptionsDiagnostics() (e.g. TS6504 for a JavaScript unit)
//   global <diag>                         program.getGlobalDiagnostics()
//   semantic <diag>                       program.getSemanticDiagnostics(unitFile)     (only when inProgram)
//   suggestion <diag>                     program.getSuggestionDiagnostics(unitFile)   (only when inProgram)
//     related <diag>                      a diagnostic's relatedInformation, after it
//   <diag> = <file> <start> <length> <code> <Category> "<message>"[ unnecessary][ deprecated]
//       <file> is "." for the unit file, the base name for a library file, "-" for none; start and length "-" when
//       undefined; the message chain flattened with ts.flattenDiagnosticMessageText(messageText, "\n"), JSON-quoted.
//   == types
//   <line> <Kind> ><text> : <type>
//   <line> <Kind> ><spaces> : <underline>   when typeWriter.ts produces a non-empty underline (as the baseline)
//       typeWriter.ts's TypeWriterWalker.getTypes on the unit file, its nodes in its order (pre-order over
//       forEachChild; expressions, identifiers and declaration names that are not part of a type node), <line> the
//       0-based line of the node's first token, <text> the node's source text without line breaks (as the .types
//       baseline writes it), <type> as the walker computes it (typeToTypeNode + printer; the intrinsic name of an
//       `any`-flagged type when the program has no errors, "hadErrors" above)
//   == symbols
//   <line> <Kind> ><text> : Symbol(<name>, Decl(<file>, <line>, <character>), ...)
//       TypeWriterWalker.getSymbols on the unit file, written after the whole types walk (the order of the .types and
//       .symbols baselines; the checker caches what the first walk computed); library declarations as Decl(<lib>, --, --)
//   Line-safe text: in <text>, <type>, <underline> and symbol strings the characters \r, \n, U+2028 and U+2029 are
//   written as the two-character escapes for CR and LF (backslash r, backslash n) and six-character escapes for the
//   line and paragraph separators (backslash u2028, backslash u2029).
//
//   "hadErrors" is the walker's hadErrorBaseline (harness compilerRunner: the program has any diagnostic): true when
//   any of the options, global, syntactic(unit), semantic(unit) diagnostics exists.
//
//   Library file: <out>.lib.txt, written once per run by the main thread with its own TypeScript instance (so the
//   workers' library state is not touched): a program of an empty file /__empty__.ts with the same host, then for each
//   library file in program order "== <basename>" and its getSemanticDiagnostics as "semantic <diag>" lines, then
//   "== global" and getGlobalDiagnostics.

import { mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { isMainThread, parentPort, Worker } from "node:worker_threads";
import ts from "typescript";
import { artifactsDir } from "./common.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;

const scriptKinds = { TS: ts.ScriptKind.TS, TSX: ts.ScriptKind.TSX, JS: ts.ScriptKind.JS, JSX: ts.ScriptKind.JSX, JSON: ts.ScriptKind.JSON };
const jsDocModes = { all: ts.JSDocParsingMode.ParseAll, none: ts.JSDocParsingMode.ParseNone, types: ts.JSDocParsingMode.ParseForTypeErrors };
export const libFolder = "/.ts/";
export const compilerOptions = { noResolve: true };
// JavaScript/JSX units with allowJs, so that their typing is checked (author's decision, 2026-10-25; preregistration.md §17).
export const optionsFor = unit => unit.kind === "JS" || unit.kind === "JSX" ? { noResolve: true, allowJs: true } : compilerOptions;
const libDir = dirname(createRequire(import.meta.url).resolve("typescript/lib/lib.d.ts"));

const kindNames = new Map();
for (const [name, value] of Object.entries(ts.SyntaxKind)) if (typeof value === "number" && !kindNames.has(value)) kindNames.set(value, name);

// Library texts and parsed source files, per thread (per TypeScript instance).
const libTexts = new Map(readdirSync(libDir).filter(f => /^lib\..*\.d\.ts$/.test(f)).map(f => [libFolder + f, null]));
const libFiles = new Map();

function createHost(unitName, unitText, unitKind, jsDocParsingMode) {
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
        writeFile: () => { },
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

const lineEscapes = new Map([["\r", "\\r"], ["\n", "\\n"], [String.fromCharCode(0x2028), "\\u2028"], [String.fromCharCode(0x2029), "\\u2029"]]);
const lineBreakPattern = new RegExp(`[${[...lineEscapes.keys()].join("")}]`, "g");
export const escapeLine = s => s.replace(lineBreakPattern, c => lineEscapes.get(c));
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

function header(unit, inProgram, hadErrors, program) {
    let out = `checker-dump 1\nfile ${unit.fileName} ${unit.kind} inProgram=${inProgram} hadErrors=${hadErrors}\n`;
    const options = optionsFor(unit);
    out += `options ${JSON.stringify(options)}\n`;
    out += `computed ${Object.entries(ts.computedOptions).map(([name, o]) => `${name}=${JSON.stringify(o.computeValue(options))}`).join(" ")}\n`;
    const libs = program.getSourceFiles().filter(f => isLibName(f.fileName)).map(f => baseName(f.fileName));
    out += `libs ${libs.length}${libs.map(l => " " + l).join("")}\n`;
    return out;
}

// typeWriter.ts (src/harness/typeWriter.ts, TypeScript 6.0.3): the printer that underlines fully synthetic nodes.
function createUnderliningPrinter() {
    let underlining = false;
    const nodeIsFullySynthetic = node => ts.nodeIsSynthesized(node) && !ts.getParseTreeNode(node);
    const printer = ts.createPrinter({ removeComments: true }, {
        onEmitNode: (hint, node, cb) => {
            if (nodeIsFullySynthetic(node) !== underlining) {
                underlining = !underlining;
                const result = cb(hint, node);
                underlining = !underlining;
                return result;
            }
            return cb(hint, node);
        },
    });
    const baseWriter = ts.createTextWriter("");
    const underliner = ts.createTextWriter("");
    const underlineFor = s => s.length === 0 ? s : (underlining ? "^" : " ").repeat(s.length);
    const both = name => (s, ...rest) => { baseWriter[name](s, ...rest); underliner[name](underlineFor(s), ...rest); };
    const writer = {
        write: both("write"), writeTrailingSemicolon: both("writeTrailingSemicolon"), writeComment: both("writeComment"),
        rawWrite: both("rawWrite"), writeLiteral: both("writeLiteral"), writeKeyword: both("writeKeyword"),
        writeOperator: both("writeOperator"), writePunctuation: both("writePunctuation"), writeSpace: both("writeSpace"),
        writeStringLiteral: both("writeStringLiteral"), writeParameter: both("writeParameter"), writeProperty: both("writeProperty"),
        writeSymbol: both("writeSymbol"),
        getText: () => baseWriter.getText(), getTextPos: () => baseWriter.getTextPos(), getLine: () => baseWriter.getLine(),
        getColumn: () => baseWriter.getColumn(), getIndent: () => baseWriter.getIndent(), isAtStartOfLine: () => baseWriter.isAtStartOfLine(),
        hasTrailingComment: () => baseWriter.hasTrailingComment(), hasTrailingWhitespace: () => baseWriter.hasTrailingWhitespace(),
        writeLine(force) { baseWriter.writeLine(force); underliner.writeLine(force); },
        increaseIndent() { baseWriter.increaseIndent(); underliner.increaseIndent(); },
        decreaseIndent() { baseWriter.decreaseIndent(); underliner.decreaseIndent(); },
        clear() { baseWriter.clear(); underliner.clear(); },
    };
    return { printer, writer, underliner, reset() { underlining = false; baseWriter.clear(); underliner.clear(); } };
}
let underliningPrinter;

// typeWriter.ts: TypeWriterWalker, both walks.
function* forEachASTNode(node) {
    const work = [node];
    while (work.length) {
        const elem = work.pop();
        yield elem;
        const resChildren = [];
        ts.forEachChild(elem, c => { resChildren.unshift(c); });
        work.push(...resChildren);
    }
}

function walk(program, checker, sourceFile, hadErrorBaseline, isSymbolWalk) {
    let out = "";
    const isImportStatementName = node =>
        (ts.isImportSpecifier(node.parent) && (node.parent.name === node || node.parent.propertyName === node))
        || (ts.isImportClause(node.parent) && node.parent.name === node)
        || (ts.isImportEqualsDeclaration(node.parent) && node.parent.name === node);
    const isExportStatementName = node =>
        (ts.isExportAssignment(node.parent) && node.parent.expression === node)
        || (ts.isExportSpecifier(node.parent) && (node.parent.name === node || node.parent.propertyName === node));
    const isIntrinsicJsxTag = node => {
        const p = node.parent;
        if (!(ts.isJsxOpeningElement(p) || ts.isJsxClosingElement(p) || ts.isJsxSelfClosingElement(p))) return false;
        if (p.tagName !== node) return false;
        return ts.isIntrinsicJsxName(node.getText());
    };
    for (const node of forEachASTNode(sourceFile)) {
        if (!(ts.isExpressionNode(node) || node.kind === ts.SyntaxKind.Identifier || ts.isDeclarationName(node))) continue;
        const actualPos = ts.skipTrivia(sourceFile.text, node.pos);
        const line = sourceFile.getLineAndCharacterOfPosition(actualPos).line;
        const sourceText = ts.getSourceTextOfNodeFromSourceFile(sourceFile, node);
        const lineText = escapeLine(sourceText.replace(/\r?\n/g, ""));
        const prefix = `${line} ${kindNames.get(node.kind)} >`;
        if (!isSymbolWalk) {
            if (ts.isPartOfTypeNode(node) || ts.isIdentifier(node) && !(ts.getMeaningFromDeclaration(node.parent) & ts.SemanticMeaning.Value) && !(ts.isTypeAliasDeclaration(node.parent) && node.parent.name === node)) continue;
            if (ts.isOmittedExpression(node)) continue;
            let type = ts.isExpressionWithTypeArgumentsInClassExtendsClause(node.parent) ? checker.getTypeAtLocation(node.parent) : undefined;
            if (!type || type.flags & ts.TypeFlags.Any) type = checker.getTypeAtLocation(node);
            let typeString, underline;
            if (
                !hadErrorBaseline &&
                type.flags & ts.TypeFlags.Any &&
                !ts.isBindingElement(node.parent) &&
                !ts.isPropertyAccessOrQualifiedName(node.parent) &&
                !ts.isLabelName(node) &&
                !(ts.isModuleDeclaration(node.parent) && ts.isGlobalScopeAugmentation(node.parent)) &&
                !ts.isMetaProperty(node.parent) &&
                !isImportStatementName(node) &&
                !isExportStatementName(node) &&
                !isIntrinsicJsxTag(node)
            ) {
                typeString = type.intrinsicName;
            }
            else {
                const typeFormatFlags = ts.TypeFormatFlags.NoTruncation | ts.TypeFormatFlags.AllowUniqueESSymbolType | ts.TypeFormatFlags.GenerateNamesForShadowedTypeParams;
                let typeNode = checker.typeToTypeNode(type, node.parent, (typeFormatFlags & ts.TypeFormatFlags.NodeBuilderFlagsMask) | ts.NodeBuilderFlags.IgnoreErrors, ts.InternalNodeBuilderFlags.AllowUnresolvedNames);
                if (ts.isIdentifier(node) && ts.isTypeAliasDeclaration(node.parent) && node.parent.name === node && ts.isIdentifier(typeNode) && ts.idText(typeNode) === ts.idText(node)) {
                    typeNode = checker.typeToTypeNode(type, node.parent, ((typeFormatFlags | ts.TypeFormatFlags.InTypeAlias) & ts.TypeFormatFlags.NodeBuilderFlagsMask) | ts.NodeBuilderFlags.IgnoreErrors);
                }
                underliningPrinter ??= createUnderliningPrinter();
                const { printer, writer, underliner, reset } = underliningPrinter;
                printer.writeNode(ts.EmitHint.Unspecified, typeNode, sourceFile, writer);
                typeString = writer.getText();
                underline = underliner.getText();
                reset();
            }
            out += `${prefix}${lineText} : ${escapeLine(typeString)}\n`;
            if (underline) out += `${prefix}${" ".repeat(lineText.length)} : ${escapeLine(underline)}\n`;
            continue;
        }
        const symbol = checker.getSymbolAtLocation(node);
        if (!symbol) continue;
        let symbolString = "Symbol(" + checker.symbolToString(symbol, node.parent);
        if (symbol.declarations) {
            let count = 0;
            for (const declaration of symbol.declarations) {
                if (count >= 5) {
                    symbolString += ` ... and ${symbol.declarations.length - count} more`;
                    break;
                }
                count++;
                const declSourceFile = declaration.getSourceFile();
                const position = declSourceFile.getLineAndCharacterOfPosition(declaration.pos);
                const fileName = ts.getBaseFileName(declSourceFile.fileName);
                const isLibFile = /lib.*\.d\.ts/i.test(fileName);
                symbolString += `, Decl(${fileName}, ${isLibFile ? "--" : position.line}, ${isLibFile ? "--" : position.character})`;
            }
        }
        symbolString += ")";
        out += `${prefix}${lineText} : ${escapeLine(symbolString)}\n`;
    }
    return out;
}

export function dumpUnit(text, unit, jsDocParsingMode) {
    const host = createHost(unit.fileName, text, scriptKinds[unit.kind], jsDocParsingMode);
    const program = ts.createProgram([unit.fileName], optionsFor(unit), host);
    const sourceFile = program.getSourceFile(unit.fileName);
    const inProgram = sourceFile !== undefined;
    const optionDiagnostics = program.getOptionsDiagnostics();
    const globalDiagnostics = program.getGlobalDiagnostics();
    const semantic = inProgram ? program.getSemanticDiagnostics(sourceFile) : [];
    const suggestion = inProgram ? program.getSuggestionDiagnostics(sourceFile) : [];
    const syntactic = inProgram ? program.getSyntacticDiagnostics(sourceFile) : [];
    const hadErrors = optionDiagnostics.length + globalDiagnostics.length + syntactic.length + semantic.length > 0;
    let out = header(unit, inProgram, hadErrors, program);
    out += "== diagnostics\n";
    out += writeDiagnostics("option", optionDiagnostics, unit.fileName);
    out += writeDiagnostics("global", globalDiagnostics, unit.fileName);
    out += writeDiagnostics("semantic", semantic, unit.fileName);
    out += writeDiagnostics("suggestion", suggestion, unit.fileName);
    out += "== types\n";
    const checker = program.getTypeChecker();
    if (inProgram) out += walk(program, checker, sourceFile, hadErrors, false);
    out += "== symbols\n";
    if (inProgram) out += walk(program, checker, sourceFile, hadErrors, true);
    return out;
}

function warmUp(jsDocParsingMode) {
    const program = ts.createProgram(["/__empty__.ts"], compilerOptions, createHost("/__empty__.ts", "", ts.ScriptKind.TS, jsDocParsingMode));
    program.getTypeChecker();
    return program;
}

export function dumpLibrary(jsDocParsingMode) {
    const program = warmUp(jsDocParsingMode);
    let out = "";
    for (const file of program.getSourceFiles()) {
        if (!isLibName(file.fileName)) continue;
        out += `== ${baseName(file.fileName)}\n`;
        out += writeDiagnostics("semantic", program.getSemanticDiagnostics(file), "/__empty__.ts");
    }
    out += "== global\n" + writeDiagnostics("global", program.getGlobalDiagnostics(), "/__empty__.ts");
    return out;
}

if (!isMainThread) {
    let warmedUp = false;
    parentPort.on("message", ({ unit, input, output, jsDocParsingMode }) => {
        if (!warmedUp) {
            warmUp(jsDocParsingMode);
            warmedUp = true;
        }
        if (unit === undefined) { parentPort.postMessage("ready"); return; }
        const text = readFileSync(input, "utf8");
        writeFileSync(output, dumpUnit(text, unit, jsDocParsingMode));
        parentPort.postMessage(unit.id);
    });
}
else {
    const corpus = arg("--corpus", join(artifactsDir, "equivalence"));
    const timeoutMs = Number(arg("--timeout-ms", "120000"));
    const jsdocMode = arg("--jsdoc-mode", "types");
    if (!(jsdocMode in jsDocModes)) throw new Error("--jsdoc-mode must be all, none or types");
    const jsDocParsingMode = jsDocModes[jsdocMode];
    const dumpName = `ts-checker-${jsdocMode}`;
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
    writeFileSync(`${outDir}.lib.txt`, dumpLibrary(jsDocParsingMode));
    const libSeconds = (performance.now() - started) / 1000;

    const outcomes = { ok: [], timeout: [], error: [] };
    let worker;
    // A new worker parses, binds and merges the library before its first unit; that is not timed.
    const startWorker = () => new Promise((resolve, reject) => {
        worker = new Worker(new URL(import.meta.url));
        worker.once("message", () => { worker.removeAllListeners("error"); resolve(); });
        worker.once("error", reject);
        worker.postMessage({ jsDocParsingMode });
    });
    const runOne = unit => new Promise(resolve => {
        const timer = setTimeout(async () => {
            worker.removeAllListeners();
            await worker.terminate();
            worker = undefined;
            resolve("timeout");
        }, timeoutMs);
        worker.once("message", () => { clearTimeout(timer); worker.removeAllListeners("error"); resolve("ok"); });
        worker.once("error", err => { clearTimeout(timer); worker = undefined; resolve({ error: String(err?.stack ?? err) }); });
        worker.postMessage({ unit, input: join(corpus, "units", `${unit.id}.txt`), output: join(outDir, `${unit.id}.txt`), jsDocParsingMode });
    });
    for (const unit of units) {
        if (!worker) await startWorker();
        const result = await runOne(unit);
        if (result === "ok") outcomes.ok.push(unit.id);
        else if (result === "timeout") outcomes.timeout.push(unit.id);
        else outcomes.error.push({ id: unit.id, error: result.error });
    }
    await worker?.terminate();
    writeFileSync(join(dirname(outDir), `${outDir.split(/[\\/]/).pop()}.outcomes.json`), JSON.stringify({ timeoutMs, ...outcomes }, null, 1));
    console.log(`${dumpName}: ok ${outcomes.ok.length}, timeout ${outcomes.timeout.length}, error ${outcomes.error.length} in ${((performance.now() - started) / 1000).toFixed(1)} s (library ${libSeconds.toFixed(1)} s)`);
}
