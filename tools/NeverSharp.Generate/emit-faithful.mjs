// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track F emitter for node types and ForEachChild.
//
// Node shape (pre-registration §5 rule 1: the source's representation):
// - Node: the fields TypeScript's Node constructor sets (utilities.ts: function Node).
// - One sealed class per node interface (schema.kindToClass); kinds without an
//   interface of their own (punctuation, keywords, trivia) share Token, as
//   TypeScript's Token<TKind> does.
// - A field exists when the interface declares the property AND parser.ts,
//   factory/nodeFactory.ts or utilities.ts assign a property of that name. Type
//   brands and never-assigned properties do not exist on TypeScript's objects either.
// - ForEachChild translates forEachChildTable entry by entry, with the semantics
//   of visitNode, visitNodes and forEach in parser.ts / core.ts.
//
// Writes src/NeverSharp.Faithful/Generated/Nodes.g.cs and NodeVisitor.g.cs.

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { header, repoRoot, ts, tsRoot } from "./common.mjs";

const BASE_FIELDS = new Set(["kind", "pos", "end", "id", "flags", "modifierFlagsCache", "transformFlags", "parent", "original", "emitNode"]);
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

function csType(cs, optional) {
    const nullable = t => optional ? `${t}?` : t;
    switch (cs.k) {
        case "string": return { type: nullable("string"), init: optional ? "" : " = null!" };
        case "bool": return { type: "bool", init: "" };
        case "int": return { type: "int", init: "" };
        case "enum": return { type: cs.name === "int" ? "int" : cs.name, init: "" };
        case "node": return { type: nullable(cs.cls), init: optional ? "" : " = null!" };
        case "nodes": return { type: nullable("NodeArray"), init: optional ? "" : " = null!", note: cs.elem !== "Node" ? `elements: ${cs.elem}` : undefined };
        case "array": return { type: `${cs.elem}[]?`, init: "" };
        case "stringOrNodes": return { type: "object?", init: "", note: "string or NodeArray (TypeScript: string | NodeArray<...>)" };
        case "undefinedOnly": return cs.usedAs === "nodes" ? { type: "NodeArray?", init: "", note: "declared `?: undefined`; assigned for grammar errors" }
            : cs.usedAs === "node" ? { type: "Node?", init: "", note: "declared `?: undefined`; assigned for grammar errors" }
            : { type: "object?", init: "", note: "declared `?: undefined`" };
        default: {
            const mapped = OBJECT_TYPES[cs.ts];
            if (mapped === "omit") return { omit: true };
            if (mapped) return { type: mapped, init: "", note: `TypeScript: ${cs.ts}` };
            return { type: "object?", init: "", note: `TypeScript: ${cs.ts}; binder/checker data, not used by the parser` };
        }
    }
}

// Fields whose declared type in types.ts is narrower than what the parser stores (TypeScript does not
// check at run time). JSDocSignature.parameters: parseCallbackTagParameters (parser.ts) also keeps
// non-parameter tags there, e.g. @this inside @callback (equivalence unit callbackTag4.ts).
const FIELD_OVERRIDES = {
    "JSDocSignature.parameters": { type: "Node[]?", init: "", note: "types.ts: readonly JSDocParameterTag[]; the parser also stores other JSDoc tags (@this in @callback)" },
};

// Non-node property types the parser assigns. Plain data maps to a C# type here; fields typed by
// a non-node interface of types.ts are omitted, and the port declares them in a partial class
// with its ported type (they are part of the ported module, not of the node shapes).
const OBJECT_TYPES = {
    "JSDocArray": "List<JSDoc>?",
    "Path": "string?",
    "readonly string[]": "IReadOnlyList<string>?",
    "readonly number[]": "IReadOnlyList<int>?",
    "ReadonlyMap<string, string>": "Dictionary<string, string>?",
    "(file: SourceFile) => void": "Action<SourceFile>?",
    "true | Node": "object?",
    "DiagnosticWithLocation[]": "omit",
    "CommentDirective[]": "omit",
    "readonly FileReference[]": "omit",
    "readonly AmdDependency[]": "omit",
    "CheckJsDirective": "omit",
    "ReadonlyPragmaMap": "omit",
};

export function emitFaithful(schema) {
    const assigned = assignedPropertyNames();
    const outDir = join(repoRoot, "src", "NeverSharp.Faithful", "Generated");
    mkdirSync(outDir, { recursive: true });

    const classes = [...new Set(Object.values(schema.kindToClass))].filter(c => c !== "Token").sort();
    const fieldsOf = {};
    const report = { classes: classes.length + 1, fields: 0, collisions: [] };

    const lines = [header("src/compiler/types.ts (node interfaces) and src/compiler/utilities.ts (function Node)"),
        "#nullable enable", "", "using NeverSharp.Generated;", "", "namespace NeverSharp.Faithful;", "",
        "/// <summary>Base of every node: the fields TypeScript's Node constructor sets (utilities.ts).</summary>",
        "public abstract partial class Node",
        "{",
        "    public int Pos = -1;",
        "    public int End = -1;",
        "    public SyntaxKind Kind;",
        "    public int Id;",
        "    public NodeFlags Flags;",
        "    public ModifierFlags ModifierFlagsCache;",
        "    public TransformFlags TransformFlags;",
        "    public Node Parent = null!;",
        "    public Node? Original;",
        "    public object? EmitNode;",
        "}", "",
        "/// <summary>TypeScript's NodeArray: an array of nodes with a text range (types.ts).</summary>",
        "public sealed partial class NodeArray : List<Node>",
        "{",
        "    public int Pos = -1;",
        "    public int End = -1;",
        "    public bool HasTrailingComma;",
        "    public TransformFlags TransformFlags;",
        "}", "",
        "/// <summary>Kinds without a node interface of their own (punctuation, keywords, trivia): TypeScript's Token&lt;TKind&gt;.</summary>",
        "public sealed partial class Token : Node", "{", "}", ""];
    // How forEachChildTable visits each property of each class (for `?: undefined` declarations).
    const usage = {};
    const collect = (ir, cls) => {
        if (ir.op === "node") (usage[cls] ??= {})[ir.prop] = "node";
        if (ir.op === "nodes" || ir.op === "each") (usage[cls] ??= {})[ir.prop] = "nodes";
        (ir.items ?? []).forEach(i => collect(i, cls));
        if (ir.then) { collect(ir.then, cls); collect(ir.else, cls); }
    };
    for (const [kind, entry] of Object.entries(schema.children)) collect(entry.ir, schema.kindToClass[kind]);

    for (const cls of classes) {
        const props = (schema.interfaces[cls] ?? []).filter(p => !BASE_FIELDS.has(p.name) && assigned.has(p.name))
            .map(p => p.cs.k === "undefinedOnly" ? { ...p, cs: { ...p.cs, usedAs: usage[cls]?.[p.name] } } : p);
        fieldsOf[cls] = new Set(props.map(p => p.name));
        const omitted = props.filter(p => csType(p.cs, p.optional).omit).map(p => `${p.name} (${p.cs.ts})`);
        lines.push(`/// <summary>types.ts: ${cls}.</summary>`);
        if (omitted.length) lines.push(`// Declared by the port in a partial class: ${omitted.join(", ")}.`);
        lines.push(`public sealed partial class ${cls} : Node`, "{");
        for (const p of props.sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0)) {
            let name = pascal(p.name);
            if (name === cls) { report.collisions.push(`${cls}.${p.name}`); name += "_"; }
            const t = FIELD_OVERRIDES[`${cls}.${p.name}`] ?? csType(p.cs, p.optional);
            if (t.omit) { report.omitted = (report.omitted ?? 0) + 1; continue; }
            lines.push(`    public ${t.type} ${name}${t.init};${t.note ? ` // ${t.note}` : ""}`);
            report.fields++;
        }
        lines.push("}", "");
    }
    // Allocation by kind, as TypeScript's base node factory picks a constructor per kind.
    lines.push("/// <summary>Creates the node object for a kind (the class per kind of types.ts).</summary>",
        "public static partial class NodeAllocator", "{",
        "    public static Node Create(SyntaxKind kind) => kind switch", "    {");
    for (const [kind, cls] of Object.entries(schema.kindToClass)) if (cls !== "Token") lines.push(`        SyntaxKind.${kind} => new ${cls} { Kind = kind },`);
    lines.push("        _ => new Token { Kind = kind },", "    };", "}", "");
    // node.jsDoc: TypeScript reads and writes it structurally on any JSDocContainer; C# dispatches on the class.
    const containers = classes.filter(c => fieldsOf[c].has("jsDoc"));
    lines.push("/// <summary>TypeScript's <c>(node as JSDocContainer).jsDoc</c> on any node: the classes that declare <c>jsDoc</c>.</summary>",
        "public static partial class JSDocContainers", "{",
        "    public static List<JSDoc>? GetJsDoc(Node node) => node switch", "    {",
        ...containers.map(c => `        ${c} n => n.JsDoc,`), "        _ => null,", "    };", "",
        "    public static void SetJsDoc(Node node, List<JSDoc>? jsDoc)", "    {", "        switch (node)", "        {",
        ...containers.flatMap(c => [`            case ${c} n:`, "                n.JsDoc = jsDoc;", "                break;"]),
        "            default:", "                throw new InvalidOperationException($\"{node.Kind} is not a JSDocContainer\");",
        "        }", "    }", "}", "");
    report.jsDocContainers = containers.length;
    writeFileSync(join(outDir, "Nodes.g.cs"), lines.join("\n"));

    // ---- ForEachChild --------------------------------------------------------
    const has = (cls, prop) => fieldsOf[cls]?.has(prop);
    const propType = (cls, prop) => (schema.interfaces[cls] ?? []).find(p => p.name === prop)?.cs.k;
    function expr(ir, cls) {
        switch (ir.op) {
            case "or": return ir.items.map(i => expr(i, cls)).join("\n                ?? ");
            case "none": return "null";
            case "node": return has(cls, ir.prop) ? `VisitNode(cbNode, n.${pascal(ir.prop)})` : "null";
            case "nodes": return has(cls, ir.prop) ? `VisitNodes(cbNode, cbNodes, n.${pascal(ir.prop)}${propType(cls, ir.prop) === "stringOrNodes" ? " as NodeArray" : ""})` : "null";
            case "each": return has(cls, ir.prop) ? `ForEachElement(n.${pascal(ir.prop)}, cbNode)` : "null";
            case "cond": return `(${test(ir.test, cls)}\n                ? ${expr(ir.then, cls)}\n                : ${expr(ir.else, cls)})`;
            default: throw new Error(`unknown IR op ${ir.op}`);
        }
    }
    function test(t, cls) {
        switch (t.op) {
            case "isString": return `n.${pascal(t.prop)} is string`;
            case "bool": return propType(cls, t.prop) === "bool" ? `n.${pascal(t.prop)}` : `n.${pascal(t.prop)} is not null`;
            case "kindIs": return `n.${pascal(t.prop)}?.Kind == SyntaxKind.${t.kind}`;
            case "and": return `(${test(t.left, cls)} && ${test(t.right, cls)})`;
            default: throw new Error(`unknown test ${t.op}`);
        }
    }
    const v = [header("src/compiler/parser.ts (forEachChild, forEachChildTable, visitNode, visitNodes) and src/compiler/core.ts (forEach)"),
        "#nullable enable", "", "using NeverSharp.Generated;", "", "namespace NeverSharp.Faithful;", "",
        "public static partial class NodeVisitor", "{",
        "    /// <summary>parser.ts forEachChild: visits the children of a node in TypeScript's order; returns the first non-null callback result.</summary>",
        "    public static T? ForEachChild<T>(Node? node, Func<Node, T?> cbNode, Func<NodeArray, T?>? cbNodes = null) where T : class",
        "    {",
        "        if (node is null || node.Kind <= SyntaxKind.LastToken)", "        {", "            return null;", "        }",
        "        switch (node.Kind)", "        {"];
    let entries = 0;
    for (const [kind, entry] of Object.entries(schema.children)) {
        const cls = schema.kindToClass[kind];
        v.push(`            case SyntaxKind.${kind}:`, "            {", `                var n = (${cls})node;`, `                return ${expr(entry.ir, cls)};`, "            }");
        entries++;
    }
    v.push("            default:", "                return null;", "        }", "    }", "",
        "    // parser.ts visitNode", "    private static T? VisitNode<T>(Func<Node, T?> cbNode, Node? node) where T : class => node is null ? null : cbNode(node);", "",
        "    // parser.ts visitNodes", "    private static T? VisitNodes<T>(Func<Node, T?> cbNode, Func<NodeArray, T?>? cbNodes, NodeArray? nodes) where T : class",
        "    {", "        if (nodes is not null)", "        {", "            if (cbNodes is not null)", "            {", "                return cbNodes(nodes);", "            }",
        "            foreach (var node in nodes)", "            {", "                var result = cbNode(node);", "                if (result is not null)", "                {", "                    return result;", "                }", "            }", "        }", "        return null;", "    }", "",
        "    // core.ts forEach(array, callback): every element through cbNode, also when cbNodes is given",
        "    private static T? ForEachElement<T>(IReadOnlyList<Node>? array, Func<Node, T?> cbNode) where T : class",
        "    {", "        if (array is not null)", "        {", "            for (var i = 0; i < array.Count; i++)", "            {", "                var result = cbNode(array[i]);", "                if (result is not null)", "                {", "                    return result;", "                }", "            }", "        }", "        return null;", "    }",
        "}", "");
    writeFileSync(join(outDir, "NodeVisitor.g.cs"), v.join("\n"));
    report.forEachChildEntries = entries;
    return report;
}
