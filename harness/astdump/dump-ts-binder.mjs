// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Gate B oracle (phase2-plan.md, module B0): canonical binder dump via the tsc 6.0.3 compiler API.
// Every equivalence unit is parsed as dump-ts.mjs parses it (same unit list, file reading, script kinds and
// worker thread with timeout), but with setParentNodes, then bound with ts.bindSourceFile(file, {}).
//
// Usage: node dump-ts-binder.mjs [--corpus <dir>] [--out <dir>] [--timeout-ms <n>] [--jsdoc-mode all|none|types]
//   --jsdoc-mode: all = ParseAll (default), none = ParseNone, types = ParseForTypeErrors (the measured mode)
//   --out: default <corpus>/dumps/ts-binder-<mode>; the outcomes go to <out>.outcomes.json
//
// Binder dump format, version 1 (tools/NeverSharp.Dump.Optimized/BinderDump.cs writes the same):
//
//   Nodes are numbered #0, #1, ... in the order of the AST dump (dump-ts.mjs): pre-order, a node's jsDoc comments
//   before its forEachChild children. A node reference is <Kind>:<pos>-<end>#<number> (#? for a node outside that
//   walk), "-" for none. Strings (names, messages) are written as JSON.stringify writes them.
//
//   == symbols
//   #<n> <Kind> <pos> <end>[ symbol=s<i>][ localSymbol=s<i>][ locals=<table>][ next=<ref>]
//       one line per node that has any of node.symbol, node.localSymbol, node.locals, node.nextContainer, in node
//       order; <table> is [s<i>,...] in Map insertion order, an entry whose key differs from the symbol's
//       escapedName written as "<key>"=s<i>
//   s<i> "<escapedName>" <SymbolFlags> <Names|...>[ decl=[<ref>,...]][ value=<ref>][ parent=s<i>][ export=s<i>]
//       [ members=<table>][ exports=<table>][ globalExports=<table>][ assignmentMembers=[<ref>,...]]
//       [ constEnumOnlyModule=true|false][ replaceableByMethod=true|false]
//       symbols numbered s0, s1, ... in first-reference order: first while writing the node lines (symbol,
//       localSymbol, then the locals entries), then while writing the symbol lines in number order (parent,
//       exportSymbol, members, exports, globalExports); absent fields are omitted, flag names are the single-bit
//       members in bit order ("-" for 0)
//   == flow
//   #<n> <Kind> <pos> <end>[ flowNode=f<i>][ endFlowNode=f<i>][ returnFlowNode=f<i>][ fallthroughFlowNode=f<i>]
//   f<i> <FlowFlags> <Names|...>[ node=<ref>|switch=<ref> clause=<start>-<end>|target=f<i> antecedents=[f<i>,...]]
//       [ ante=f<i>|antes=[f<i>,...]]
//       flow nodes numbered f0, f1, ... in first-visit order: per node line, its fields in the order above, each
//       followed depth-first (pre-order, explicit stack) by its antecedent(s) and, for a reduce label, its target
//       and antecedents. A label's antecedent array is written as antes=[...], a single antecedent as ante=.
//       The unreachable flow node is one object per binder, shared by every file a binder binds; its Referenced
//       and Shared bits depend on the files bound before, so they are masked out for flow nodes with Unreachable.
//   == diagnostics
//   diag <start> <length> <code> <Category> "<message>"[ unnecessary][ deprecated]       file.bindDiagnostics
//   suggestion <start> <length> <code> <Category> "<message>"[ unnecessary][ deprecated] bindSuggestionDiagnostics
//     related <start> <length> <code> <Category> "<message>"                             their relatedInformation
//   flags #<n> <Kind> <pos> <end> <before> <after>[ +<Name>...][ -<Name>...]
//       nodes whose NodeFlags the binder changed (flags before and after binding, the bits set and cleared)
//   Messages are flattened with ts.flattenDiagnosticMessageText(messageText, "\n").

import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { isMainThread, parentPort, Worker } from "node:worker_threads";
import ts from "typescript";
import { artifactsDir } from "./common.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;

const scriptKinds = { TS: ts.ScriptKind.TS, TSX: ts.ScriptKind.TSX, JS: ts.ScriptKind.JS, JSX: ts.ScriptKind.JSX, JSON: ts.ScriptKind.JSON };
const jsDocModes = { all: ts.JSDocParsingMode.ParseAll, none: ts.JSDocParsingMode.ParseNone, types: ts.JSDocParsingMode.ParseForTypeErrors };

// First declared name per value: later aliases are ignored.
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
const bitsOf = enumObject => [...firstNames(enumObject, true)].sort((a, b) => a[0] - b[0]);
const symbolFlagBits = bitsOf(ts.SymbolFlags);
const flowFlagBits = bitsOf(ts.FlowFlags);
const nodeFlagBits = bitsOf(ts.NodeFlags);
const bitNames = (bits, value) => bits.filter(([bit]) => (value & bit) !== 0).map(([, name]) => name);
const flagsText = (bits, value) => `${value} ${bitNames(bits, value).join("|") || "-"}`;

function dumpUnit(text, unit, jsDocParsingMode) {
    const file = ts.createSourceFile(
        unit.fileName,
        text,
        { languageVersion: ts.ScriptTarget.Latest, jsDocParsingMode },
        /*setParentNodes*/ true,
        scriptKinds[unit.kind],
    );
    const walk = () => {
        const order = [];
        const visit = node => {
            order.push(node);
            for (const comment of node.jsDoc ?? []) visit(comment);
            ts.forEachChild(node, child => void visit(child));
        };
        visit(file);
        return order;
    };
    const order = walk();
    const flagsBefore = order.map(n => n.flags);
    ts.bindSourceFile(file, {});
    const after = walk();
    if (after.length !== order.length || after.some((n, i) => n !== order[i])) throw new Error("the binder changed the tree");

    const index = new Map(order.map((n, i) => [n, i]));
    const ref = n => n === undefined ? "-" : `${kindNames.get(n.kind)}:${n.pos}-${n.end}#${index.has(n) ? index.get(n) : "?"}`;
    const head = (n, i) => `#${i} ${kindNames.get(n.kind)} ${n.pos} ${n.end}`;
    let out = "";

    // symbols
    const symbolIds = new Map();
    const symbols = [];
    const sid = s => {
        let id = symbolIds.get(s);
        if (id === undefined) {
            id = symbols.length;
            symbolIds.set(s, id);
            symbols.push(s);
        }
        return `s${id}`;
    };
    const table = t => {
        const entries = [];
        for (const [key, s] of t) entries.push((key === s.escapedName ? "" : JSON.stringify(key) + "=") + sid(s));
        return `[${entries.join(",")}]`;
    };
    out += "== symbols\n";
    order.forEach((n, i) => {
        let line = "";
        if (n.symbol) line += ` symbol=${sid(n.symbol)}`;
        if (n.localSymbol) line += ` localSymbol=${sid(n.localSymbol)}`;
        if (n.locals) line += ` locals=${table(n.locals)}`;
        if (n.nextContainer) line += ` next=${ref(n.nextContainer)}`;
        if (line) out += head(n, i) + line + "\n";
    });
    for (let k = 0; k < symbols.length; k++) {
        const s = symbols[k];
        let line = `s${k} ${JSON.stringify(s.escapedName)} ${flagsText(symbolFlagBits, s.flags)}`;
        if (s.declarations) line += ` decl=[${s.declarations.map(ref).join(",")}]`;
        if (s.valueDeclaration) line += ` value=${ref(s.valueDeclaration)}`;
        if (s.parent) line += ` parent=${sid(s.parent)}`;
        if (s.exportSymbol) line += ` export=${sid(s.exportSymbol)}`;
        if (s.members) line += ` members=${table(s.members)}`;
        if (s.exports) line += ` exports=${table(s.exports)}`;
        if (s.globalExports) line += ` globalExports=${table(s.globalExports)}`;
        if (s.assignmentDeclarationMembers) line += ` assignmentMembers=[${[...s.assignmentDeclarationMembers.values()].map(ref).join(",")}]`;
        if (s.constEnumOnlyModule !== undefined) line += ` constEnumOnlyModule=${s.constEnumOnlyModule}`;
        if (s.isReplaceableByMethod !== undefined) line += ` replaceableByMethod=${s.isReplaceableByMethod}`;
        out += line + "\n";
    }

    // flow
    const flowIds = new Map();
    const flows = [];
    const successors = f => {
        const next = Array.isArray(f.antecedent) ? [...f.antecedent] : f.antecedent ? [f.antecedent] : [];
        if (f.flags & ts.FlowFlags.ReduceLabel) next.push(f.node.target, ...f.node.antecedents);
        return next;
    };
    const fid = f => {
        const stack = [f];
        while (stack.length) {
            const x = stack.pop();
            if (flowIds.has(x)) continue;
            flowIds.set(x, flows.length);
            flows.push(x);
            const next = successors(x);
            for (let j = next.length - 1; j >= 0; j--) stack.push(next[j]);
        }
        return `f${flowIds.get(f)}`;
    };
    out += "== flow\n";
    order.forEach((n, i) => {
        let line = "";
        for (const field of ["flowNode", "endFlowNode", "returnFlowNode", "fallthroughFlowNode"]) {
            if (n[field]) line += ` ${field}=${fid(n[field])}`;
        }
        if (line) out += head(n, i) + line + "\n";
    });
    flows.forEach((f, k) => {
        const flags = f.flags & ts.FlowFlags.Unreachable ? f.flags & ~(ts.FlowFlags.Referenced | ts.FlowFlags.Shared) : f.flags;
        let line = `f${k} ${flagsText(flowFlagBits, flags)}`;
        if (f.flags & ts.FlowFlags.SwitchClause) line += ` switch=${ref(f.node.switchStatement)} clause=${f.node.clauseStart}-${f.node.clauseEnd}`;
        else if (f.flags & ts.FlowFlags.ReduceLabel) line += ` target=f${flowIds.get(f.node.target)} antecedents=[${f.node.antecedents.map(a => `f${flowIds.get(a)}`).join(",")}]`;
        else if (f.node) line += ` node=${ref(f.node)}`;
        if (Array.isArray(f.antecedent)) line += ` antes=[${f.antecedent.map(a => `f${flowIds.get(a)}`).join(",")}]`;
        else if (f.antecedent) line += ` ante=f${flowIds.get(f.antecedent)}`;
        out += line + "\n";
    });

    // diagnostics
    out += "== diagnostics\n";
    const message = d => JSON.stringify(ts.flattenDiagnosticMessageText(d.messageText, "\n"));
    const category = d => ts.DiagnosticCategory[d.category];
    const diagnostics = (tag, list) => {
        for (const d of list ?? []) {
            out += `${tag} ${d.start} ${d.length} ${d.code} ${category(d)} ${message(d)}${d.reportsUnnecessary ? " unnecessary" : ""}${d.reportsDeprecated ? " deprecated" : ""}\n`;
            for (const r of d.relatedInformation ?? []) out += `  related ${r.start} ${r.length} ${r.code} ${category(r)} ${message(r)}\n`;
        }
    };
    diagnostics("diag", file.bindDiagnostics);
    diagnostics("suggestion", file.bindSuggestionDiagnostics);
    order.forEach((n, i) => {
        const before = flagsBefore[i];
        if (n.flags === before) return;
        const set = bitNames(nodeFlagBits, n.flags & ~before).map(name => ` +${name}`).join("");
        const cleared = bitNames(nodeFlagBits, before & ~n.flags).map(name => ` -${name}`).join("");
        out += `flags ${head(n, i)} ${before} ${n.flags}${set}${cleared}\n`;
    });
    return out;
}

if (!isMainThread) {
    parentPort.on("message", ({ unit, input, output, jsDocParsingMode }) => {
        const text = readFileSync(input, "utf8");
        writeFileSync(output, dumpUnit(text, unit, jsDocParsingMode));
        parentPort.postMessage(unit.id);
    });
}
else {
    const corpus = arg("--corpus", join(artifactsDir, "equivalence"));
    const timeoutMs = Number(arg("--timeout-ms", "10000"));
    const jsdocMode = arg("--jsdoc-mode", "all");
    if (!(jsdocMode in jsDocModes)) throw new Error("--jsdoc-mode must be all, none or types");
    const jsDocParsingMode = jsDocModes[jsdocMode];
    const dumpName = `ts-binder-${jsdocMode}`;
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
        worker.postMessage({ unit, input: join(corpus, "units", `${unit.id}.txt`), output: join(outDir, `${unit.id}.txt`), jsDocParsingMode });
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
