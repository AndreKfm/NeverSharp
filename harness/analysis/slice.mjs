// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Computes the slice of the TypeScript v6.0.3 compiler that Phase 1 has to
// port: every declaration transitively referenced from the parser entry points,
// resolved with the TypeScript type checker (the sources import everything
// through the _namespaces barrel, so import lists do not say where a name
// lives). A "unit" is a top-level or namespace-level declaration (function,
// variable statement, enum, interface, type alias, class); members of the
// generated Diagnostics object are units of their own, so only the messages
// that are used count.
//
// Usage: node slice.mjs [--roots createSourceFile,forEachChild] [--root-file parser.ts] [--json out.json]

import { readdirSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join, relative, sep } from "node:path";
import { repoRoot } from "../astdump/common.mjs";

const require = createRequire(join(repoRoot, "harness", "astdump", "package.json"));
const ts = require("typescript");

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const compilerDir = join(repoRoot, "external", "typescript", "src", "compiler");
const rootNames = arg("--roots", "createSourceFile,forEachChild").split(",");

function* walk(dir) {
    for (const e of readdirSync(dir, { withFileTypes: true })) {
        const full = join(dir, e.name);
        if (e.isDirectory()) yield* walk(full);
        else if (e.name.endsWith(".ts") && !e.name.endsWith(".d.ts")) yield full;
    }
}

const program = ts.createProgram([...walk(compilerDir)], {
    target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.NodeNext, moduleResolution: ts.ModuleResolutionKind.NodeNext,
    noEmit: true, skipLibCheck: true, types: [], strict: false,
});
const checker = program.getTypeChecker();
const inCompiler = sf => sf && !sf.isDeclarationFile && sf.fileName.replaceAll("/", sep).startsWith(compilerDir.replaceAll("/", sep));
const fileOf = node => relative(compilerDir, node.getSourceFile().fileName).split(sep).join("/");

const isTypeUnit = n => ts.isInterfaceDeclaration(n) || ts.isTypeAliasDeclaration(n);
function isDiagnosticsMember(node) {
    // PropertyAssignment inside `export const Diagnostics = { ... }` (diagnosticInformationMap.generated.ts)
    const obj = node.parent;
    const decl = obj && ts.isObjectLiteralExpression(obj) ? obj.parent : undefined;
    return ts.isPropertyAssignment(node) && decl && ts.isVariableDeclaration(decl) && decl.name.getText() === "Diagnostics";
}

// createNodeFactory holds every create*/update* function as an inner function and
// returns them in one object literal. Its inner functions are units of their own,
// reached through the NodeFactory interface member of the same name (see resolve).
const explodedContainers = new Set(["createNodeFactory"]);
const isExplodedInner = n => ts.isFunctionDeclaration(n) && n.parent && ts.isBlock(n.parent)
    && ts.isFunctionDeclaration(n.parent.parent) && explodedContainers.has(n.parent.parent.name?.getText());

// Climb to the enclosing unit: a statement directly in a source file or namespace body.
function unitOf(node) {
    let n = node;
    while (n && n.parent) {
        if (isDiagnosticsMember(n)) return n;
        if (isExplodedInner(n)) return n;
        if (ts.isSourceFile(n.parent) || ts.isModuleBlock(n.parent)) return ts.isModuleDeclaration(n) ? undefined : n;
        n = n.parent;
    }
    return undefined;
}

const units = new Map(); // node -> { file, kind, lines, name }
const usedDiagnostics = new Set();
const queue = [];
function addUnit(unit) {
    if (!unit || units.has(unit) || !inCompiler(unit.getSourceFile())) return;
    const sf = unit.getSourceFile();
    const start = sf.getLineAndCharacterOfPosition(unit.getStart(sf)).line;
    const end = sf.getLineAndCharacterOfPosition(unit.end).line;
    const name = isDiagnosticsMember(unit) ? `Diagnostics.${unit.name.getText()}`
        : unit.name?.getText?.() ?? (ts.isVariableStatement(unit) ? unit.declarationList.declarations.map(d => d.name.getText()).join(",") : ts.SyntaxKind[unit.kind]);
    const kind = isDiagnosticsMember(unit) ? "diagnostic" : isTypeUnit(unit) ? "type" : ts.isEnumDeclaration(unit) ? "enum" : "value";
    let lines = end - start + 1;
    if (ts.isFunctionDeclaration(unit) && explodedContainers.has(unit.name?.getText())) {
        // The container counts only its own code: not its inner functions (units of their own) nor the returned object literal.
        const span = n => sf.getLineAndCharacterOfPosition(n.end).line - sf.getLineAndCharacterOfPosition(n.getStart(sf)).line + 1;
        for (const s of unit.body.statements) {
            if (isExplodedInner(s)) lines -= span(s);
            else if (ts.isVariableStatement(s) && s.declarationList.declarations.some(d => d.name.getText() === "factory")) lines -= span(s);
        }
    }
    units.set(unit, { file: fileOf(unit), kind, lines, name });
    queue.push(unit);
}

// Inner functions of createNodeFactory by name, for mapping NodeFactory interface members.
const factoryFunctions = new Map();
for (const sf of program.getSourceFiles()) {
    if (!sf.fileName.endsWith("/factory/nodeFactory.ts")) continue;
    ts.forEachChild(sf, function visit(n) {
        if (isExplodedInner(n) && n.name) factoryFunctions.set(n.name.getText(), n);
        ts.forEachChild(n, visit);
    });
}

function resolve(identifier) {
    let symbol = checker.getSymbolAtLocation(identifier);
    if (!symbol) return [];
    if (symbol.flags & ts.SymbolFlags.Alias) symbol = checker.getAliasedSymbol(symbol);
    const declarations = symbol.declarations ?? [];
    // factory.createX(...) resolves to the NodeFactory interface member; the implementation is the inner function createX.
    // Only for uses in expressions (x.createX), not for the member declarations of the interface itself.
    const isUse = identifier.parent && ts.isPropertyAccessExpression(identifier.parent) && identifier.parent.name === identifier;
    const mapped = !isUse ? [] : declarations.flatMap(d => (ts.isMethodSignature(d) || ts.isPropertySignature(d)) && d.parent && ts.isInterfaceDeclaration(d.parent)
        && d.parent.name.getText() === "NodeFactory" && factoryFunctions.has(d.name.getText()) ? [factoryFunctions.get(d.name.getText())] : []);
    return [...declarations, ...mapped];
}

// Roots: the named declarations in the root file (default parser.ts; --root-file scanner.ts for the scanner module).
const rootFile = arg("--root-file", "parser.ts");
const parserFile = program.getSourceFiles().find(f => f.fileName.endsWith(`/compiler/${rootFile}`));
ts.forEachChild(parserFile, function visit(n) {
    if ((ts.isFunctionDeclaration(n) || ts.isVariableDeclaration(n)) && n.name && rootNames.includes(n.name.getText())) addUnit(unitOf(n));
    ts.forEachChild(n, visit);
});

while (queue.length) {
    const unit = queue.pop();
    const visit = n => {
        if (ts.isIdentifier(n) || ts.isPrivateIdentifier(n)) {
            for (const d of resolve(n)) {
                const u = unitOf(d);
                if (u && u !== unit) addUnit(u);
            }
        }
        ts.forEachChild(n, visit);
    };
    // createNodeFactory itself: walk everything except its inner functions and the returned object literal.
    const skip = n => isExplodedInner(n) || (ts.isVariableDeclaration(n) && n.name.getText() === "factory" && explodedContainers.has(unit.name?.getText()));
    const visitOuter = n => { if (!skip(n)) visit(n); };
    if (ts.isFunctionDeclaration(unit) && explodedContainers.has(unit.name?.getText())) ts.forEachChild(unit.body, function walkBody(n) { if (!skip(n)) { if (ts.isIdentifier(n)) visit(n); else ts.forEachChild(n, walkBody); } });
    else ts.forEachChild(unit, visit);
    void visitOuter;
    // Diagnostic messages referenced in the unit's text (the generated Diagnostics table is not in the source tree).
    for (const m of unit.getText().matchAll(/\bDiagnostics\.([A-Za-z0-9_]+)/g)) usedDiagnostics.add(m[1]);
}

const byFile = new Map();
for (const u of units.values()) {
    const f = byFile.get(u.file) ?? { file: u.file, value: 0, valueLines: 0, enum: 0, enumLines: 0, type: 0, typeLines: 0, diagnostic: 0 };
    if (u.kind === "diagnostic") f.diagnostic++;
    else { f[u.kind]++; f[u.kind + "Lines"] += u.lines; }
    byFile.set(u.file, f);
}
const rows = [...byFile.values()].sort((a, b) => (b.valueLines + b.typeLines + b.enumLines) - (a.valueLines + a.typeLines + a.enumLines));
console.log(`roots: ${rootNames.join(", ")}; units: ${units.size}`);
console.log("file".padEnd(42), "value(units/lines)".padStart(20), "enum".padStart(12), "type".padStart(14), "diagnostics".padStart(12));
for (const r of rows) console.log(r.file.padEnd(42), `${r.value}/${r.valueLines}`.padStart(20), `${r.enum}/${r.enumLines}`.padStart(12), `${r.type}/${r.typeLines}`.padStart(14), String(r.diagnostic || "").padStart(12));
const total = rows.reduce((a, r) => ({ value: a.value + r.valueLines, enum: a.enum + r.enumLines, type: a.type + r.typeLines, diag: a.diag + r.diagnostic }), { value: 0, enum: 0, type: 0, diag: 0 });
console.log(`total lines: value ${total.value}, enum ${total.enum}, type ${total.type}`);
console.log(`diagnostic messages referenced: ${usedDiagnostics.size} (of all messages in diagnosticMessages.json)`);
const factory = [...units.values()].filter(u => u.file === "factory/nodeFactory.ts");
console.log(`nodeFactory: ${factory.filter(u => factoryFunctions.has(u.name)).length} of ${factoryFunctions.size} inner functions reached`);

const out = arg("--json", undefined);
if (out) writeFileSync(out, JSON.stringify({ roots: rootNames, files: rows, usedDiagnostics: [...usedDiagnostics].sort(), units: [...units.values()].sort((a, b) => a.file.localeCompare(b.file) || b.lines - a.lines) }, null, 1));
