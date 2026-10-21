// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Module definitions of the Phase 1 port (phase1-plan.md §4) and the unit list
// each porting attempt receives. A module is a set of units of the dependency
// slice (harness/analysis/slice.mjs): top-level or namespace-level declarations
// of the TypeScript v6.0.3 sources. For every unit the list gives the source
// file and line range at the pinned commit, so every attempt of a module gets
// the identical scope (pre-registration §5.2).
//
// Usage: node modules.mjs --module M1 [--out docs/modules/M1-units.md]

import { readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join } from "node:path";
import { repoRoot } from "../astdump/common.mjs";

const require = createRequire(join(repoRoot, "harness", "astdump", "package.json"));
const ts = require("typescript");
const compilerDir = join(repoRoot, "external", "typescript", "src", "compiler");

// file -> unit names; "*" = every value unit the slice reaches in that file.
export const modules = {
    M1: {
        title: "Core: helpers, paths, Debug, text ranges",
        units: {
            "core.ts": "*",
            "path.ts": "*",
            "debug.ts": ["assert", "assertEqual", "assertGreaterThanOrEqual", "assertIsDefined", "assertLessThanOrEqual", "assertNever",
                "assertNode", "checkDefined", "fail", "shouldAssert", "shouldAssertFunction", "currentAssertionLevel", "assertionCache", "isDebugging"],
            "utilities.ts": ["setTextRangePos", "setTextRangeEnd", "setTextRangePosEnd", "setTextRangePosWidth", "positionIsSynthesized"],
            "utilitiesPublic.ts": ["createTextSpan", "createTextSpanFromBounds"],
            "factory/utilitiesPublic.ts": ["setTextRange"],
        },
        // Units of these files deliberately left out of every module (phase1-plan.md §2).
        notPorted: { "debug.ts": "formatting helpers and debug-info attachment (formatEnum, formatSyntaxKind, attachNodeArrayDebugInfo, …): replaced by plain exception messages" },
    },
    // Scanner: every scanner.ts unit the parser slice reaches, plus what the scanner slice
    // (slice.mjs --root-file scanner.ts) reaches outside scanner.ts and M1.
    M2: {
        title: "Scanner",
        units: {
            "scanner.ts": "*",
            "utilities.ts": ["parsePseudoBigInt", "forEachEntry", "getNameOfScriptTarget"],
            "commandLineParser.ts": ["targetOptionDeclaration"],
            "types.ts": ["LanguageFeatureMinimumTarget"],
        },
    },
    // AST factory and parser, ported together (the factory has no gate of its own; both are
    // checked by the AST dump). "*" here means: every value unit of the file that the slice
    // reaches and that no earlier module (M1, M2) contains.
    M34: {
        title: "AST factory and parser (M3 + M4)",
        units: {
            "factory/nodeFactory.ts": "*",
            "factory/emitNode.ts": "*",
            "factory/utilities.ts": "*",
            "factory/nodeTests.ts": "*",
            "factory/utilitiesPublic.ts": "*",
            "factory/parenthesizerRules.ts": ["nullParenthesizerRules"],
            "factory/nodeConverters.ts": ["nullNodeConverters"],
            "parser.ts": "*",
            "utilities.ts": "*",
            "utilitiesPublic.ts": "*",
            "commandLineParser.ts": ["convertToJson", "getCompilerOptionValueTypeString"],
            "types.ts": ["commentPragmas"],
        },
        afterModules: ["M1", "M2"],
        // M5 (JSDocParser) and incremental parsing (not in Phase 1) are left out by namespace.
        excludeNamespaces: { "parser.ts": ["JSDocParser", "IncrementalParser"] },
        // ... except what the parser itself calls: reparseTopLevelAwait (parser.ts:1864) reuses nodes
        // through IncrementalParser.createSyntaxCursor (corrected 2026-10-21, reported by attempt 1).
        keepInExcludedNamespaces: { "parser.ts": ["createSyntaxCursor"] },
        // Generated instead of ported (src/NeverSharp.Faithful/Generated/NodeVisitor.g.cs), or M5.
        excludeNames: { "parser.ts": [/^forEachChild$/, /^forEachChildTable$/, /^forEachChildIn/, /^parseIsolatedJSDocComment$/, /^parseJSDocTypeExpressionForTests$/] },
        notPorted: {
            "parser.ts": "forEachChild and forEachChildTable with its forEachChildIn* helpers: generated (NodeVisitor.ForEachChild); namespace JSDocParser, parseIsolatedJSDocComment, parseJSDocTypeExpressionForTests: module M5; namespace IncrementalParser except createSyntaxCursor: incremental parsing, not in Phase 1 (createSyntaxCursor is in scope: reparseTopLevelAwait uses it)",
            "factory/parenthesizerRules.ts, factory/nodeConverters.ts": "createParenthesizerRules, createNodeConverters: the parser's factory is created with NoParenthesizerRules | NoNodeConverters; only the null implementations are ported",
            "performance.ts, performanceCore.ts, tracing.ts": "instrumentation, no-ops for parsing: calls are left out",
            "debug.ts": "formatting helpers and debug-info attachment, as in M1",
        },
    },
    // JSDoc parser: the units of namespace JSDocParser that the slice reaches.
    M5: {
        title: "JSDoc parser",
        units: { "parser.ts": "*" },
        afterModules: ["M1", "M2", "M34"],
        onlyNamespaces: { "parser.ts": ["JSDocParser"] },
        excludeNames: { "parser.ts": [/^parseIsolatedJSDocComment$/, /^parseJSDocTypeExpressionForTests$/] },
        notPorted: { "parser.ts": "parseIsolatedJSDocComment, parseJSDocTypeExpressionForTests: test and language-service entry points, not reached by createSourceFile" },
    },
};

// Units listed in an earlier module's generated unit list (docs/modules/<id>-units.md).
function unitsOf(moduleId) {
    const md = readFileSync(join(repoRoot, "docs", "modules", `${moduleId}-units.md`), "utf8");
    return new Set([...md.matchAll(/^\| (\S+) \| `([^`]+)`/gm)].map(m => `${m[1]}:${m[2]}`));
}

// Line span of each namespace with the given names (nested namespaces too).
function namespaceSpans(sf, names) {
    const spans = [];
    const visit = node => {
        if (ts.isModuleDeclaration(node) && names.includes(node.name.text)) spans.push([node.getStart(sf), node.end]);
        ts.forEachChild(node, visit);
    };
    ts.forEachChild(sf, visit);
    return spans;
}

// Declarations of a unit name in a file: functions (all overloads plus the body), variable statements, enums.
function declarationsOf(sf, name) {
    const found = [];
    const visit = (node, depth) => {
        const named = (ts.isFunctionDeclaration(node) || ts.isEnumDeclaration(node) || ts.isClassDeclaration(node)) && node.name?.text === name
            || ts.isVariableStatement(node) && node.declarationList.declarations.some(d => ts.isIdentifier(d.name) ? d.name.text === name : d.name.getText(sf) === name);
        if (named) found.push(node);
        if (ts.isModuleDeclaration(node) && node.body && depth < 3) ts.forEachChild(node.body, n => visit(n, depth + 1));
        // createNodeFactory's inner functions are units of their own (slice.mjs explodes it).
        if (ts.isFunctionDeclaration(node) && node.name?.text === "createNodeFactory" && node.body) ts.forEachChild(node.body, n => visit(n, depth + 1));
    };
    ts.forEachChild(sf, n => visit(n, 0));
    return found;
}

export function unitList(moduleId) {
    const def = modules[moduleId];
    if (!def) throw new Error(`unknown module ${moduleId}`);
    const slice = JSON.parse(readFileSync(join(repoRoot, "artifacts", "analysis", "slice.json"), "utf8"));
    const rows = [];
    const earlier = new Set();
    for (const m of def.afterModules ?? []) for (const u of unitsOf(m)) earlier.add(u);
    for (const [file, names] of Object.entries(def.units)) {
        const text = readFileSync(join(compilerDir, file), "utf8");
        const sf = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true);
        const spans = namespaceSpans(sf, def.excludeNamespaces?.[file] ?? []);
        const excludedName = name => (def.excludeNames?.[file] ?? []).some(re => re.test(name));
        const wanted = names === "*"
            ? [...new Set(slice.units.filter(u => u.file === file && u.kind === "value").map(u => u.name))].filter(n => !earlier.has(`${file}:${n}`) && !excludedName(n))
            : names;
        for (const name of wanted) {
            const kept = (def.keepInExcludedNamespaces?.[file] ?? []).includes(name);
            const only = namespaceSpans(sf, def.onlyNamespaces?.[file] ?? []);
            const decls = declarationsOf(sf, name).filter(d => kept || !spans.some(([a, b]) => d.getStart(sf) >= a && d.end <= b))
                .filter(d => only.length === 0 || only.some(([a, b]) => d.getStart(sf) >= a && d.end <= b));
            if (decls.length === 0) {
                if (names === "*" && declarationsOf(sf, name).length > 0) continue; // only inside an excluded namespace
                throw new Error(`${file}: no declaration of ${name}`);
            }
            const line = p => sf.getLineAndCharacterOfPosition(p).line + 1;
            // Adjacent declarations (overloads and body) form one range; the same name declared again
            // elsewhere (e.g. createSourceFile at top level and inside namespace Parser) is a range of its own.
            const ranges = [];
            let previousEnd = -1;
            for (const d of [...decls].sort((a, b) => a.pos - b.pos)) {
                const a = line(d.getStart(sf)), b = line(d.end);
                const adjacent = previousEnd >= 0 && ts.skipTrivia(text, previousEnd) >= d.getStart(sf); // only comments and white space between
                if (adjacent) ranges.at(-1)[1] = Math.max(ranges.at(-1)[1], b); else ranges.push([a, b]);
                previousEnd = d.end;
            }
            const start = ranges[0][0], end = ranges.at(-1)[1];
            // createNodeFactory counts only its own code, not its inner functions (as in slice.mjs).
            const own = name === "createNodeFactory" ? slice.units.find(u => u.file === file && u.name === name)?.lines : undefined;
            rows.push({ file, name, start, end, ranges, lines: own ?? ranges.reduce((n, [a, b]) => n + b - a + 1, 0) });
        }
    }
    rows.sort((a, b) => a.file < b.file ? -1 : a.file > b.file ? 1 : a.start - b.start);
    return { def, rows };
}

if (import.meta.url === `file:///${process.argv[1].replaceAll("\\", "/")}` || process.argv[1]?.endsWith("modules.mjs")) {
    const arg = (n, f) => process.argv.includes(n) ? process.argv[process.argv.indexOf(n) + 1] : f;
    const id = arg("--module", "M1");
    const { def, rows } = unitList(id);
    const pins = JSON.parse(readFileSync(join(repoRoot, "pins.json"), "utf8"));
    const out = [`# ${id} units: ${def.title}`, "",
        `Generated by \`harness/port/modules.mjs --module ${id}\` from the dependency slice. Line ranges refer to \`src/compiler/<file>\` of TypeScript at commit ${pins.references.typescript.commit}.`, "",
        `${rows.length} units, ${rows.reduce((a, r) => a + r.lines, 0)} lines (including overload signatures and comments inside the range).`, "",
        "| File | Unit | Lines |", "|---|---|---|",
        ...rows.map(r => `| ${r.file} | \`${r.name}\` | ${r.ranges.map(([a, b]) => `${a}–${b}`).join(", ")} |`), ""];
    if (def.notPorted) out.push("Not ported:", "", ...Object.entries(def.notPorted).map(([f, why]) => `- ${f}: ${why}`), "");
    const file = arg("--out", undefined);
    if (file) writeFileSync(join(repoRoot, file), out.join("\n")); else console.log(out.join("\n"));
}
