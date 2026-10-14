// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Compares the tsc 6.0.3 and typescript-go 7.0.2 dumps unit by unit, applying
// the numbered normalization rules in order, and reports how many units are
// identical after each rule plus a classification of what remains.
//
// Usage: node compare.mjs [--corpus <dir>] [--json <file>]

import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { artifactsDir } from "./common.mjs";
import { parseDump, renderParsed } from "./parse-dump.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const corpus = arg("--corpus", join(artifactsDir, "equivalence"));
const { units } = JSON.parse(readFileSync(join(corpus, "manifest.json"), "utf8"));

// ---- tree helpers -----------------------------------------------------------

function toTree(nodes) {
    const root = { children: [] };
    const stack = [root];
    for (const n of nodes) {
        const node = { ...n, children: [] };
        stack.length = n.depth + 1;
        stack[n.depth].children.push(node);
        stack.push(node);
    }
    return root.children[0];
}

function flatten(tree) {
    const out = [];
    const walk = (n, depth) => {
        out.push({ depth, kind: n.kind, pos: n.pos, end: n.end, flags: n.flags });
        for (const c of n.children) walk(c, depth + 1);
    };
    if (tree) walk(tree, 0);
    return out;
}

function mapTree(tree, fn) {
    const walk = (n, parent) => {
        fn(n, parent);
        for (const c of n.children) walk(c, n);
    };
    walk(tree, undefined);
    return tree;
}

// ---- normalization rules ----------------------------------------------------
// Each rule receives { ts, go } (trees + diagnostics) and mutates them.
// Rules are applied cumulatively in this order.

const rules = [
    {
        id: "N1",
        title: "Kind name: Go EndOfFile = TS EndOfFileToken",
        apply: ({ go }) => mapTree(go.tree, n => { if (n.kind === "EndOfFile") n.kind = "EndOfFileToken"; }),
    },
    {
        id: "N2",
        title: "Derived attributes: Go ModuleDeclaration.Keyword -> Namespace/GlobalAugmentation flags; TS node.jsDoc -> HasJSDoc flag",
        apply: ({ ts, go }) => {
            for (const tree of [ts.tree, go.tree]) mapTree(tree, n => { n.flags = n.flags.map(f => f.startsWith("=") ? f.slice(1) : f); });
        },
    },
    {
        id: "N3",
        title: "Nested namespace: Go implicit Reparsed ExportKeyword modifier = TS NodeFlags.NestedNamespace",
        apply: ({ go }) => mapTree(go.tree, n => {
            const first = n.children[0];
            if (n.kind === "ModuleDeclaration" && first?.kind === "ExportKeyword" && first.flags.includes("Reparsed") && first.pos === first.end) {
                n.children.shift();
                n.flags = [...n.flags, "NestedNamespace"];
            }
        }),
    },
    {
        id: "N4",
        title: "Array binding hole: Go childless BindingElement in ArrayBindingPattern = TS OmittedExpression",
        apply: ({ go }) => mapTree(go.tree, (n, parent) => {
            if (n.kind === "BindingElement" && n.children.length === 0 && parent?.kind === "ArrayBindingPattern") n.kind = "OmittedExpression";
        }),
    },
    {
        id: "N5",
        title: "JSDoc cast in JS: unwrap Go As/SatisfiesExpression built by the reparser (makeNewCast) around the cast expression",
        apply: ({ go }) => mapTree(go.tree, n => {
            const isCast = c => {
                const [expr, type] = c.children;
                return (c.kind === "AsExpression" || c.kind === "SatisfiesExpression") && c.children.length === 2
                    && type.flags.includes("Reparsed") && !c.flags.includes("Reparsed") && c.pos === expr.pos && c.end === expr.end;
            };
            // Several JSDoc tags on one expression produce nested casts.
            n.children = n.children.map(c => {
                while (isCast(c)) c = c.children[0];
                return c;
            });
        }),
    },
    {
        id: "N6",
        title: "Remove Go nodes flagged Reparsed (synthesized from JSDoc by typescript-go's reparser; no TS counterpart)",
        apply: ({ go }) => mapTree(go.tree, n => { n.children = n.children.filter(c => !c.flags.includes("Reparsed")); }),
    },
    {
        id: "N7",
        title: "Drop TS flag IdentifierHasExtendedUnicodeEscape (typescript-go defines but never sets it; only used for the removed ES5 target)",
        apply: ({ ts }) => mapTree(ts.tree, n => { n.flags = n.flags.filter(f => f !== "IdentifierHasExtendedUnicodeEscape"); }),
    },
    {
        id: "N8",
        title: "Drop TS flag HasAggregatedChildData (memoization bit of TypeScript's aggregateChildData cache, not parse output)",
        apply: ({ ts }) => mapTree(ts.tree, n => { n.flags = n.flags.filter(f => f !== "HasAggregatedChildData"); }),
    },
    {
        id: "N9",
        title: "Drop JSDoc-derived deprecation flags: TS Deprecated (parsed @deprecated tag) and Go PossiblyContainsDeprecatedTag (text scan)",
        apply: ({ ts, go }) => {
            mapTree(ts.tree, n => { n.flags = n.flags.filter(f => f !== "Deprecated"); });
            mapTree(go.tree, n => { n.flags = n.flags.filter(f => f !== "PossiblyContainsDeprecatedTag"); });
        },
    },
];

// ---- classification of what remains ----------------------------------------
// Every remaining difference is assigned to a named category; a unit can have
// several. Categories describe divergences, they are not normalizations.

function classify(a, b, unit) {
    const cats = new Set();
    const isJs = unit.kind === "JS" || unit.kind === "JSX";
    const shapeEqual = a.nodes.length === b.nodes.length && a.nodes.every((n, i) => n.depth === b.nodes[i].depth && n.kind === b.nodes[i].kind);
    if (!shapeEqual) {
        let i = 0;
        while (i < a.nodes.length && i < b.nodes.length && a.nodes[i].depth === b.nodes[i].depth && a.nodes[i].kind === b.nodes[i].kind) i++;
        const kinds = [a.nodes[i]?.kind ?? "<end>", b.nodes[i]?.kind ?? "<end>"];
        if (kinds.some(k => k.startsWith("JSDoc")) && !isJs) cats.add("jsdoc-type-syntax-in-ts-file");
        else cats.add(`structure-other (${kinds.join(" vs ")})`);
    }
    else {
        a.nodes.forEach((x, j) => {
            const y = b.nodes[j];
            if (x.pos !== y.pos || x.end !== y.end) cats.add("error-recovery-node-range");
            const fa = new Set(x.flags), fb = new Set(y.flags);
            for (const f of new Set([...fa, ...fb])) {
                if (fa.has(f) === fb.has(f)) continue;
                if (f === "HasJSDoc") cats.add("jsdoc-attachment");
                else if (f === "ThisNodeHasError") cats.add(isJs ? "error-flag-js-jsdoc-reparse" : "error-recovery-ThisNodeHasError");
                else if (f === "AwaitContext") cats.add("top-level-await-context");
                else if (f === "JavaScriptFile" || f === "Ambient") cats.add("missing-token-context-flags");
                else cats.add(`flag-other (${f} on ${x.kind})`);
            }
        });
    }
    const key = d => `${d.code} ${d.start} ${d.length}`;
    const ka = new Set(a.diags.map(key)), kb = new Set(b.diags.map(key));
    const tsOnly = a.diags.filter(d => !kb.has(key(d))), goOnly = b.diags.filter(d => !ka.has(key(d)));
    for (const d of tsOnly) {
        if (d.code === 1127 && goOnly.some(g => g.code === 1127 && g.start === d.start)) cats.add("diag-1127-length");
        else if (d.code === 2458) cats.add("diag-2458-amd-removed");
        else if (goOnly.some(g => g.code === d.code)) cats.add(`diag-position (${d.code})`);
        else cats.add(`diag-ts-only (${d.code})`);
    }
    for (const d of goOnly) {
        if (tsOnly.some(x => x.code === d.code)) continue;
        if (d.code === 2880) cats.add("diag-2880-import-assert-in-parser");
        else if (d.code === 1003 && isJs) cats.add("diag-1003-js-jsdoc-reparse");
        else cats.add(`diag-go-only (${d.code})`);
    }
    if (tsOnly.length === 0 && goOnly.length === 0 && a.diags.map(key).join() !== b.diags.map(key).join()) cats.add("diag-order");
    return [...cats];
}

// ---- comparison -------------------------------------------------------------

// Normalized dumps of units that still differ, for inspection with any diff tool.
const normalizedDir = join(corpus, "normalized");
rmSync(normalizedDir, { recursive: true, force: true });
mkdirSync(join(normalizedDir, "ts"), { recursive: true });
mkdirSync(join(normalizedDir, "go"), { recursive: true });

const inc = (map, key, n = 1) => map.set(key, (map.get(key) ?? 0) + n);
const identicalAfter = new Map([["raw", 0], ...rules.map(r => [r.id, 0])]);
const remaining = [];
const firstDiff = new Map();
const flagDiff = new Map();
const diagDiff = new Map();
const categoryCount = new Map();
const ruleEffect = new Map(rules.map(r => [r.id, { units: {}, nodesRemoved: 0 }]));
const examples = new Map();

function diffKey(a, b) {
    let i = 0;
    while (i < a.nodes.length && i < b.nodes.length && a.nodes[i].depth === b.nodes[i].depth && a.nodes[i].kind === b.nodes[i].kind) i++;
    if (i < a.nodes.length || i < b.nodes.length) {
        const x = a.nodes[i], y = b.nodes[i];
        return { type: "structure", key: `${x ? x.kind : "<end>"} vs ${y ? y.kind + (y.flags.includes("Reparsed") ? "[Reparsed]" : "") : "<end>"}`, index: i };
    }
    for (let j = 0; j < a.nodes.length; j++) {
        const x = a.nodes[j], y = b.nodes[j];
        if (x.pos !== y.pos || x.end !== y.end) return { type: "position", key: `${x.kind} ${x.pos}-${x.end} vs ${y.pos}-${y.end}`, index: j };
    }
    for (let j = 0; j < a.nodes.length; j++) {
        const x = a.nodes[j], y = b.nodes[j];
        if (x.flags.join("|") !== y.flags.join("|")) return { type: "flags", key: x.kind, index: j };
    }
    return { type: "diagnostics", key: "", index: -1 };
}

for (const unit of units) {
    const tsText = readFileSync(join(corpus, "dumps", "ts", `${unit.id}.txt`), "utf8");
    const goText = readFileSync(join(corpus, "dumps", "go", `${unit.id}.txt`), "utf8");
    if (tsText === goText) {
        for (const k of identicalAfter.keys()) inc(identicalAfter, k);
        continue;
    }
    const tsParsed = parseDump(tsText), goParsed = parseDump(goText);
    const state = {
        ts: { tree: toTree(tsParsed.nodes), diags: tsParsed.diags },
        go: { tree: toTree(goParsed.nodes), diags: goParsed.diags },
    };
    const render = side => renderParsed({ nodes: flatten(state[side].tree), diags: state[side].diags.filter(d => d.section === "diag") });
    let equal = false;
    let before = { ts: render("ts"), go: render("go"), tsNodes: flatten(state.ts.tree).length, goNodes: flatten(state.go.tree).length };
    for (const rule of rules) {
        rule.apply(state);
        const after = { ts: render("ts"), go: render("go"), tsNodes: flatten(state.ts.tree).length, goNodes: flatten(state.go.tree).length };
        // Effect of each rule, recorded independently of whether the unit ends up identical.
        if (after.ts !== before.ts || after.go !== before.go) {
            const effect = ruleEffect.get(rule.id);
            effect.units[unit.kind] = (effect.units[unit.kind] ?? 0) + 1;
            effect.nodesRemoved += (before.tsNodes - after.tsNodes) + (before.goNodes - after.goNodes);
        }
        before = after;
        if (!equal && after.ts === after.go) equal = true;
        if (equal) inc(identicalAfter, rule.id);
    }
    if (equal) continue;
    writeFileSync(join(normalizedDir, "ts", `${unit.id}.txt`), render("ts"));
    writeFileSync(join(normalizedDir, "go", `${unit.id}.txt`), render("go"));

    const a = { nodes: flatten(state.ts.tree), diags: state.ts.diags.filter(d => d.section === "diag") };
    const b = { nodes: flatten(state.go.tree), diags: state.go.diags.filter(d => d.section === "diag") };
    const d = diffKey(a, b);
    const key = `${d.type}: ${d.key} (${unit.kind})`;
    inc(firstDiff, key);
    if (!examples.has(key)) examples.set(key, `${unit.id} ${unit.test} :: ${unit.unit}${d.index >= 0 ? " @node " + d.index : ""}`);
    if (d.type === "flags" || d.type === "diagnostics") {
        a.nodes.forEach((x, j) => {
            const y = b.nodes[j];
            for (const f of x.flags) if (!y.flags.includes(f)) inc(flagDiff, `ts-only ${f} on ${x.kind}`);
            for (const f of y.flags) if (!x.flags.includes(f)) inc(flagDiff, `go-only ${f} on ${y.kind}`);
        });
    }
    const da = a.diags.map(x => `${x.code} ${x.start} ${x.length}`), db = b.diags.map(x => `${x.code} ${x.start} ${x.length}`);
    if (da.join(";") !== db.join(";")) {
        const sa = new Set(da), sb = new Set(db);
        const k = da.filter(x => !sb.has(x)).map(x => `ts-only ${x.split(" ")[0]}`).concat(db.filter(x => !sa.has(x)).map(x => `go-only ${x.split(" ")[0]}`)).join(",") || "same diagnostics, different order";
        inc(diagDiff, k);
        if (!examples.has("diag:" + k)) examples.set("diag:" + k, `${unit.id} ${unit.test} :: ${unit.unit}`);
    }
    const categories = classify(a, b, unit);
    for (const c of categories) {
        inc(categoryCount, c);
        if (!examples.has("cat:" + c)) examples.set("cat:" + c, `${unit.id} ${unit.test} :: ${unit.unit}`);
    }
    remaining.push({ id: unit.id, test: unit.test, unit: unit.unit, kind: unit.kind, categories });
}

const top = (map, n = 40) => [...map].sort((x, y) => y[1] - x[1]).slice(0, n);
console.log(`units: ${units.length}`);
console.log(`identical raw:           ${identicalAfter.get("raw")}`);
for (const r of rules) console.log(`identical after ${r.id.padEnd(4)}:   ${identicalAfter.get(r.id)}   ${r.title}`);
console.log("\nrule effects (units changed by the rule, by script kind; nodes removed):");
for (const r of rules) {
    const e = ruleEffect.get(r.id);
    console.log(`  ${r.id.padEnd(4)} ${JSON.stringify(e.units)} nodesRemoved=${e.nodesRemoved}`);
}
console.log(`\nremaining units: ${remaining.length}`);
console.log("\ndivergence categories (units per category; a unit can be in several):");
for (const [k, v] of top(categoryCount, 60)) console.log(String(v).padStart(7), k, " e.g.", examples.get("cat:" + k));
console.log("\nfirst difference after all rules:");
for (const [k, v] of top(firstDiff)) console.log(String(v).padStart(7), k, " e.g.", examples.get(k));
console.log("\nflag differences in units whose first difference is flags/diagnostics:");
for (const [k, v] of top(flagDiff, 25)) console.log(String(v).padStart(7), k);
console.log("\nparse diagnostic differences:");
for (const [k, v] of top(diagDiff, 25)) console.log(String(v).padStart(7), k, " e.g.", examples.get("diag:" + k));

const jsonOut = arg("--json", join(corpus, "comparison.json"));
writeFileSync(jsonOut, JSON.stringify({
    units: units.length,
    identical: Object.fromEntries(identicalAfter),
    rules: rules.map(r => ({ id: r.id, title: r.title, effect: ruleEffect.get(r.id) })),
    categories: Object.fromEntries(categoryCount),
    remaining,
}, null, 1));

// Oracle-agreement check: the remaining units must match the pre-registered
// divergence list exactly (same units, same categories). Any difference is an
// unexplained oracle disagreement.
const tsv = ["test\tunit\tkind\tcategories", ...remaining.map(r => `${r.test}\t${r.unit}\t${r.kind}\t${r.categories.sort().join(",")}`).sort()].join("\n") + "\n";
writeFileSync(join(corpus, "oracle-divergences.tsv"), tsv);
const registeredFile = join(import.meta.dirname, "oracle-divergences.tsv");
let registered;
try { registered = readFileSync(registeredFile, "utf8").replace(/\r\n/g, "\n"); } catch { registered = undefined; }
if (registered === undefined) {
    console.log(`\nno pre-registered divergence list at ${registeredFile}; current list written to ${join(corpus, "oracle-divergences.tsv")}`);
}
else if (registered === tsv) {
    console.log(`\noracle agreement: OK (${remaining.length} units differ, all match the pre-registered divergence list)`);
}
else {
    const want = new Set(registered.split("\n")), have = new Set(tsv.split("\n"));
    const unexpected = [...have].filter(l => l && !want.has(l)), missing = [...want].filter(l => l && !have.has(l));
    console.log(`\noracle agreement: FAILED (${unexpected.length} unexpected, ${missing.length} no longer present)`);
    for (const l of unexpected.slice(0, 20)) console.log("  + " + l);
    for (const l of missing.slice(0, 20)) console.log("  - " + l);
    process.exitCode = 1;
}
