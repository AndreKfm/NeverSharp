// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O emitter for the node store's field accessors and ForEachChild (step O6a).
//
// Track O's nodes are int handles into a per-parse arena (pre-registration §5.4); the parser and the factory
// read and write a node only through NodeStore (src/NeverSharp.Optimized/Arena/NodeStore.cs). This emitter writes
// the mechanical part of that API from the same schema as Track F's node classes (emit-faithful.mjs):
// - The field set per node class is Track F's (a field exists when the interface declares the property and
//   parser.ts, factory/nodeFactory.ts or utilities.ts assign it), without the binder and checker data the
//   parser never touches, plus the SourceFile members Track F declares by hand (Parser/Types.cs, Parser.Pragmas.cs).
// - Layout of O6a (the simplest that works; O6b compares layouts behind the same API): each node owns a block
//   of int slots (child handles and scalar fields) and a block of object slots (strings, node lists, other
//   objects); a field's offset in its block depends on the node's class, looked up per SyntaxKind in a table.
// - One getter and one setter per field name (`Expression(n)`, `SetExpression(n, v)`). A getter on a kind
//   without the field reads as undefined (default), as the property does in TypeScript; a setter throws.
//   Where one name has different types on different classes, the minority classes get `{Class}{Field}`.
// - `Is{Class}(n)`: whether a node's kind belongs to a node class (Track F's `node is Class`).
// - NodeVisitor.ForEachChild over handles, translating forEachChildTable entry by entry.
//
// Writes src/NeverSharp.Optimized/Generated/NodeStore.g.cs and NodeVisitor.g.cs, and
// artifacts/generate/optimized-fields.json (the class/field -> accessor map, for tools).

import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { header, repoRoot, ts, tsRoot } from "./common.mjs";

const BASE_FIELDS = new Set(["kind", "pos", "end", "id", "flags", "modifierFlagsCache", "transformFlags", "parent", "original", "emitNode"]);
// Accessor names NodeStore.cs defines by hand (base fields and infrastructure).
const RESERVED = new Set(["Kind", "Pos", "End", "Flags", "Parent", "TransformFlags", "ModifierFlagsCache", "Original", "EmitNode",
    "SetPos", "SetEnd", "SetFlags", "SetParent", "SetTransformFlags", "SetModifierFlagsCache", "SetOriginal", "SetEmitNode",
    "Create", "GetMark", "Truncate", "Count"]);
const pascal = n => n[0].toUpperCase() + n.slice(1);

function assignedPropertyNames() {
    const assigned = new Set();
    for (const f of ["parser.ts", "factory/nodeFactory.ts", "utilities.ts"]) {
        const text = readFileSync(join(tsRoot, "src", "compiler", f), "utf8");
        const sf = ts.createSourceFile(f, text, ts.ScriptTarget.Latest, true);
        ts.forEachChild(sf, function visit(n) {
            if (ts.isBinaryExpression(n) && n.operatorToken.kind >= ts.SyntaxKind.FirstAssignment && n.operatorToken.kind <= ts.SyntaxKind.LastAssignment) {
                let left = n.left;
                while (ts.isParenthesizedExpression(left)) left = left.expression;
                if (ts.isPropertyAccessExpression(left)) assigned.add(left.name.text);
            }
            ts.forEachChild(n, visit);
        });
    }
    return assigned;
}

// Non-node property types the parser assigns (as emit-faithful.mjs maps them); node types are handles here.
const OBJECT_TYPES = {
    "JSDocArray": { type: "List<Node>", nullable: true },
    "Path": { type: "string", nullable: true },
    "readonly string[]": { type: "IReadOnlyList<string>", nullable: true },
    "readonly number[]": { type: "IReadOnlyList<int>", nullable: true },
    "ReadonlyMap<string, string>": { type: "Dictionary<string, string>", nullable: true },
    "(file: SourceFile) => void": { type: "Action<Node>", nullable: true },
    "true | Node": { type: "object", nullable: true },
    // __String (Identifier.escapedText, PrivateIdentifier.escapedText): the parser stores the escaped text.
    "(string & { __escapedIdentifier: void; }) | InternalSymbolName": { type: "string", nullable: true },
};

// The SourceFile members Track F declares by hand with their ported types (Parser/Types.cs, Parser.Pragmas.cs).
const EXTRA_FIELDS = {
    SourceFile: [
        ["AmdDependencies", "IReadOnlyList<AmdDependency>", false],
        ["ReferencedFiles", "IReadOnlyList<FileReference>", false],
        ["TypeReferenceDirectives", "IReadOnlyList<FileReference>", false],
        ["LibReferenceDirectives", "IReadOnlyList<FileReference>", false],
        ["ParseDiagnostics", "List<DiagnosticWithLocation>", false],
        ["BindDiagnostics", "List<DiagnosticWithLocation>", true],
        ["BindSuggestionDiagnostics", "List<DiagnosticWithLocation>", true],
        ["JsDocDiagnostics", "List<DiagnosticWithLocation>", true],
        ["CommentDirectives", "List<CommentDirective>", true],
        ["CheckJsDirective", "CheckJsDirective", true],
        ["Pragmas", "PragmaMap", false],
    ],
};

// Field storage: { store: "slot" | "ref", type (C#, without `?`), nullable (refs), conv (slots: node|bool|int|enum) }.
function storage(cs, optional, usedAs) {
    switch (cs.k) {
        case "string": return { store: "ref", type: "string", nullable: optional };
        case "bool": return { store: "slot", type: "bool", conv: "bool" };
        case "int": return { store: "slot", type: "int", conv: "int" };
        case "enum": return cs.name === "int" ? { store: "slot", type: "int", conv: "int" } : { store: "slot", type: cs.name, conv: "enum" };
        case "node": return { store: "slot", type: "Node", conv: "node" };
        case "nodes": return { store: "ref", type: "NodeArray", nullable: optional };
        case "array": return { store: "ref", type: "Node[]", nullable: true };
        case "stringOrNodes": return { store: "ref", type: "object", nullable: true };
        case "undefinedOnly":
            // Declared `?: undefined`; assigned for grammar errors where forEachChild visits it, never otherwise.
            return usedAs === "nodes" ? { store: "ref", type: "NodeArray", nullable: true }
                : usedAs === "node" ? { store: "slot", type: "Node", conv: "node" } : null;
        default: return OBJECT_TYPES[cs.ts] ? { store: "ref", ...OBJECT_TYPES[cs.ts] } : null; // binder/checker data: not stored
    }
}

// JSDocSignature.parameters: parseCallbackTagParameters (parser.ts) also keeps non-parameter tags there.
const FIELD_OVERRIDES = { "JSDocSignature.parameters": { store: "ref", type: "Node[]", nullable: true } };

export function emitOptimized(schema) {
    const assigned = assignedPropertyNames();
    const outDir = join(repoRoot, "src", "NeverSharp.Optimized", "Generated");
    mkdirSync(outDir, { recursive: true });
    rmSync(join(outDir, "Nodes.g.cs"), { force: true }); // Track O's node classes until O6a (step O0)

    const kindNames = Object.keys(schema.kindToClass);
    const kindValue = name => ts.SyntaxKind[name];
    const kindCount = ts.SyntaxKind.Count;
    const classes = [...new Set(Object.values(schema.kindToClass))].sort();
    if (!classes.includes("Token")) classes.unshift("Token");

    const usage = {};
    const collect = (ir, cls) => {
        if (ir.op === "node") (usage[cls] ??= {})[ir.prop] = "node";
        if (ir.op === "nodes" || ir.op === "each") (usage[cls] ??= {})[ir.prop] = "nodes";
        (ir.items ?? []).forEach(i => collect(i, cls));
        if (ir.then) { collect(ir.then, cls); collect(ir.else, cls); }
    };
    for (const [kind, entry] of Object.entries(schema.children)) collect(entry.ir, schema.kindToClass[kind]);

    // fields[cls] = [{ ts, name, store, type, nullable, conv }]
    const fields = {};
    const omitted = {};
    for (const cls of classes) {
        const list = [];
        for (const p of (schema.interfaces[cls] ?? []).filter(p => !BASE_FIELDS.has(p.name) && assigned.has(p.name))) {
            const s = FIELD_OVERRIDES[`${cls}.${p.name}`] ?? storage(p.cs, p.optional, usage[cls]?.[p.name]);
            if (!s) { (omitted[cls] ??= []).push(pascal(p.name)); continue; }
            list.push({ ts: p.name, name: pascal(p.name), ...s });
        }
        for (const [name, type, nullable] of EXTRA_FIELDS[cls] ?? []) list.push({ ts: name, name, store: "ref", type, nullable });
        list.sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0);
        let slot = 0, ref = 0;
        for (const f of list) f.offset = f.store === "slot" ? slot++ : ref++;
        fields[cls] = { list, slots: slot, refs: ref };
    }

    // Accessor names: one per field name and storage type; the type most classes use keeps the plain name.
    const byName = new Map();
    for (const cls of classes) for (const f of fields[cls].list) {
        const sig = `${f.store}:${f.type}`;
        if (!byName.has(f.name)) byName.set(f.name, new Map());
        const sigs = byName.get(f.name);
        if (!sigs.has(sig)) sigs.set(sig, []);
        sigs.get(sig).push(cls);
    }
    const accessors = new Map(); // accessor -> { store, type, nullable, conv, offsets: Map<cls, offset> }
    const accessorOf = {}; // `${cls}.${field}` -> accessor
    const collisions = [];
    for (const [name, sigs] of byName) {
        const ranked = [...sigs].sort((a, b) => b[1].length - a[1].length);
        ranked.forEach(([, owners], i) => {
            for (const cls of owners) {
                const accessor = i === 0 ? name : `${cls}${name}`;
                if (i > 0) collisions.push(`${cls}.${name} -> ${accessor}`);
                if (RESERVED.has(accessor)) throw new Error(`accessor ${accessor} collides with a hand-written NodeStore member`);
                const f = fields[cls].list.find(x => x.name === name);
                if (!accessors.has(accessor)) accessors.set(accessor, { store: f.store, type: f.type, nullable: false, conv: f.conv, offsets: new Map() });
                const a = accessors.get(accessor);
                a.nullable ||= !!f.nullable;
                a.offsets.set(cls, f.offset);
                accessorOf[`${cls}.${name}`] = accessor;
                f.accessor = accessor;
            }
        });
    }

    const classId = new Map(classes.map((c, i) => [c, i]));
    const tableOf = valueOfKind => {
        const values = [];
        for (let k = 0; k < kindCount; k++) values.push(valueOfKind(k));
        const rows = [];
        for (let i = 0; i < values.length; i += 40) rows.push("        " + values.slice(i, i + 40).join(",") + ",");
        return rows;
    };
    const kindClass = new Array(kindCount).fill("Token");
    for (const name of kindNames) kindClass[kindValue(name)] = schema.kindToClass[name];

    const lines = [header("src/compiler/types.ts (node interfaces) and src/compiler/parser.ts (forEachChildTable)"),
        "#nullable enable", "", "using NeverSharp.Generated;", "", "namespace NeverSharp.Optimized;", "",
        "/// <summary>The generated part of the node store: layout tables, field accessors and node class tests (O6a layout).</summary>",
        "internal sealed partial class NodeStore", "{",
        `    private const int KindCount = ${kindCount};`, "",
        "    /// <summary>Int slots (child handles, scalar fields) per kind.</summary>",
        "    private static ReadOnlySpan<byte> SlotCounts =>", "    [",
        ...tableOf(k => fields[kindClass[k]].slots), "    ];", "",
        "    /// <summary>Object slots (strings, node lists, other objects) per kind.</summary>",
        "    private static ReadOnlySpan<byte> RefCounts =>", "    [",
        ...tableOf(k => fields[kindClass[k]].refs), "    ];", "",
        "    /// <summary>The node class of each kind (index into the classes of types.ts, Token for kinds without one).</summary>",
        "    private static ReadOnlySpan<byte> ClassIds =>", "    [",
        ...tableOf(k => classId.get(kindClass[k])), "    ];", ""];
    if (classes.length > 255) throw new Error("more than 255 node classes");

    const getter = (name, a) => {
        const nullable = a.store === "ref" && a.nullable;
        const ret = a.store === "ref" ? `${a.type}${nullable ? "?" : ""}` : a.type;
        const read = a.store === "ref" ? (nullable ? `(${a.type}?)refs[r.Refs + o]` : `(${a.type})refs[r.Refs + o]!`)
            : a.conv === "node" ? "new Node(slots[r.Slots + o])"
            : a.conv === "bool" ? "slots[r.Slots + o] != 0"
            : a.conv === "enum" ? `(${a.type})slots[r.Slots + o]` : "slots[r.Slots + o]";
        const absent = a.store === "ref" ? (nullable ? "null" : "null!") : "default";
        const write = a.store === "ref" ? "refs[r.Refs + o] = value;"
            : a.conv === "node" ? "slots[r.Slots + o] = value.Index;"
            : a.conv === "bool" ? "slots[r.Slots + o] = value ? 1 : 0;"
            : a.conv === "enum" ? "slots[r.Slots + o] = (int)value;" : "slots[r.Slots + o] = value;";
        return [
            `    public ${ret} ${name}(Node node)`, "    {",
            "        ref var r = ref records[node.Index];",
            `        var o = ${name}Offsets[(int)r.Kind];`,
            `        return o < 0 ? ${absent} : ${read};`, "    }", "",
            `    public void Set${name}(Node node, ${ret} value)`, "    {",
            "        ref var r = ref records[node.Index];",
            `        var o = ${name}Offsets[(int)r.Kind];`,
            "        if (o < 0)", "        {", `            ThrowNoField(r.Kind, nameof(${name}));`, "        }",
            `        ${write}`, "    }", "",
            `    private static ReadOnlySpan<sbyte> ${name}Offsets =>`, "    [",
            ...tableOf(k => a.offsets.has(kindClass[k]) ? a.offsets.get(kindClass[k]) : -1), "    ];", ""];
    };
    for (const [name, a] of [...accessors].sort((x, y) => x[0] < y[0] ? -1 : 1)) {
        const owners = [...a.offsets.keys()];
        lines.push(`    // ${owners.length > 6 ? `${owners.slice(0, 6).join(", ")}, … (${owners.length} classes)` : owners.join(", ")}`);
        lines.push(...getter(name, a));
    }
    lines.push("    // Node classes of types.ts: whether a node's kind is one of the class's kinds.");
    for (const cls of classes) lines.push(`    public bool Is${cls}(Node node) => !node.IsNone && ClassIds[(int)records[node.Index].Kind] == ${classId.get(cls)};`, "");
    lines.pop();
    lines.push("}", "");
    writeFileSync(join(outDir, "NodeStore.g.cs"), lines.join("\n"));

    // ---- ForEachChild --------------------------------------------------------
    const field = (cls, prop) => fields[cls]?.list.find(f => f.ts === prop);
    function expr(ir, cls) {
        switch (ir.op) {
            case "or": return ir.items.map(i => expr(i, cls)).join("\n                ?? ");
            case "none": return "null";
            case "node": { const f = field(cls, ir.prop); return f ? `VisitNode(cbNode, store.${f.accessor}(node))` : "null"; }
            case "nodes": { const f = field(cls, ir.prop); return f ? `VisitNodes(cbNode, cbNodes, store.${f.accessor}(node)${f.type === "object" ? " as NodeArray" : ""})` : "null"; }
            case "each": { const f = field(cls, ir.prop); return f ? `ForEachElement(store.${f.accessor}(node), cbNode)` : "null"; }
            case "cond": return `(${test(ir.test, cls)}\n                ? ${expr(ir.then, cls)}\n                : ${expr(ir.else, cls)})`;
            default: throw new Error(`unknown IR op ${ir.op}`);
        }
    }
    function test(t, cls) {
        const f = t.op === "and" ? null : field(cls, t.prop);
        if (t.op !== "and" && !f) throw new Error(`${cls}.${t.prop} tested but not stored`);
        switch (t.op) {
            case "isString": return `store.${f.accessor}(node) is string`;
            case "bool": return f.type === "bool" ? `store.${f.accessor}(node)` : f.conv === "node" ? `!store.${f.accessor}(node).IsNone` : `store.${f.accessor}(node) is not null`;
            case "kindIs": return `store.Kind(store.${f.accessor}(node)) == SyntaxKind.${t.kind}`;
            case "and": return `(${test(t.left, cls)} && ${test(t.right, cls)})`;
            default: throw new Error(`unknown test ${t.op}`);
        }
    }
    const v = [header("src/compiler/parser.ts (forEachChild, forEachChildTable, visitNode, visitNodes) and src/compiler/core.ts (forEach)"),
        "#nullable enable", "", "using NeverSharp.Generated;", "", "namespace NeverSharp.Optimized;", "",
        "internal static partial class NodeVisitor", "{",
        "    /// <summary>parser.ts forEachChild: visits the children of a node in TypeScript's order; returns the first non-null callback result.</summary>",
        "    public static T? ForEachChild<T>(NodeStore store, Node node, Func<Node, T?> cbNode, Func<NodeArray, T?>? cbNodes = null) where T : class",
        "    {",
        "        if (node.IsNone || store.Kind(node) <= SyntaxKind.LastToken)", "        {", "            return null;", "        }",
        "        switch (store.Kind(node))", "        {"];
    let entries = 0;
    for (const [kind, entry] of Object.entries(schema.children)) {
        v.push(`            case SyntaxKind.${kind}:`, `                return ${expr(entry.ir, schema.kindToClass[kind])};`);
        entries++;
    }
    v.push("            default:", "                return null;", "        }", "    }", "",
        "    // parser.ts visitNode", "    private static T? VisitNode<T>(Func<Node, T?> cbNode, Node node) where T : class => node.IsNone ? null : cbNode(node);", "",
        "    // parser.ts visitNodes", "    private static T? VisitNodes<T>(Func<Node, T?> cbNode, Func<NodeArray, T?>? cbNodes, NodeArray? nodes) where T : class",
        "    {", "        if (nodes is not null)", "        {", "            if (cbNodes is not null)", "            {", "                return cbNodes(nodes);", "            }",
        "            foreach (var node in nodes)", "            {", "                var result = cbNode(node);", "                if (result is not null)", "                {", "                    return result;", "                }", "            }", "        }", "        return null;", "    }", "",
        "    // core.ts forEach(array, callback): every element through cbNode, also when cbNodes is given",
        "    private static T? ForEachElement<T>(IReadOnlyList<Node>? array, Func<Node, T?> cbNode) where T : class",
        "    {", "        if (array is not null)", "        {", "            for (var i = 0; i < array.Count; i++)", "            {", "                var result = cbNode(array[i]);", "                if (result is not null)", "                {", "                    return result;", "                }", "            }", "        }", "        return null;", "    }",
        "}", "");
    writeFileSync(join(outDir, "NodeVisitor.g.cs"), v.join("\n"));

    const map = { classes: {}, omitted };
    for (const cls of classes) map.classes[cls] = Object.fromEntries(fields[cls].list.map(f => [f.name, { accessor: f.accessor, store: f.store, type: f.type, nullable: accessors.get(f.accessor).nullable }]));
    mkdirSync(join(repoRoot, "artifacts", "generate"), { recursive: true });
    writeFileSync(join(repoRoot, "artifacts", "generate", "optimized-fields.json"), JSON.stringify(map, null, 1));
    return { classes: classes.length, accessors: accessors.size, forEachChildEntries: entries, collisions };
}
