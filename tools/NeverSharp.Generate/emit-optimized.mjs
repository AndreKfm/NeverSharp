// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O emitter for the node store's field accessors and ForEachChild (steps O6a, O6b).
//
// Track O's nodes are int handles into a per-parse arena (pre-registration §5.4); the parser and the factory
// read and write a node only through NodeStore (src/NeverSharp.Optimized/Arena/NodeStore.cs). This emitter writes
// the mechanical part of that API from the same schema as Track F's node classes (emit-faithful.mjs):
// - The field set per node class is Track F's (a field exists when the interface declares the property and
//   parser.ts, factory/nodeFactory.ts or utilities.ts assign it), without the binder and checker data the
//   parser never touches, plus the SourceFile members Track F declares by hand (Parser/Types.cs, Parser.Pragmas.cs).
// - Layout of O6b variant C (header plus per-kind payload arenas): the store keeps a small header per node
//   (Arena/NodeStore.cs); the fields of a node class form one payload struct ({Class}Payload, child handles,
//   scalars and object fields typed as declared, explicit layout: references, 4-byte values, bools). The
//   payloads live in typed arenas, one per payload shape (size and reference offsets; Shape{n}, viewed as the
//   class's payload struct), created with the shape's first node; the header holds the node's index in its
//   arena. Fields almost always undefined during parsing (SIDE_FIELDS: JsDoc) live in a side table instead of
//   the payload.
// - One getter and one setter per field name (`Expression(n)`, `SetExpression(n, v)`). A field of one class is
//   read through the typed payload after a test of the kind; a field of several classes at its byte offset in
//   the payload of the node's kind, from a table per field (a switch over the owning kinds was measured slower:
//   its compare chains mispredict). A getter on a kind without the field reads as undefined (default), as the
//   property does in TypeScript; a setter throws.
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
    "Create", "GetMark", "Truncate", "Count", "PayloadOf", "PayloadByte", "NewPayloads", "SetJsDocEntry"]);
const pascal = n => n[0].toUpperCase() + n.slice(1);

// Fields the parser almost never sets (tuning corpus, ParseForTypeErrors: 1,522 of 1.6 million owners have JSDoc):
// kept in a side table keyed by node index (NodeStore.cs) instead of a payload slot of every owner.
const SIDE_FIELDS = new Set(["JsDoc"]);

// Nodes per class per 65,536 nodes, measured with TypeScript's parser on the tuning corpus (step O6b): the first
// capacity of a class's payload array is this share of the nodes the store expects for its source text.
const CLASS_SHARES = Object.fromEntries(("Identifier 22155 PropertyAccessExpression 3560 CallExpression 2582 StringLiteral 2276 " +
    "BinaryExpression 2200 TypeReferenceNode 2198 ExpressionStatement 1850 NumericLiteral 1627 VariableDeclaration 1545 " +
    "ParameterDeclaration 1531 VariableDeclarationList 1520 VariableStatement 1459 Block 1458 PropertyAssignment 1344 " +
    "ReturnStatement 674 ObjectLiteralExpression 646 FunctionDeclaration 521 IfStatement 473 PropertySignature 446 " +
    "ImportSpecifier 378 ArrayLiteralExpression 365 ElementAccessExpression 352 LiteralTypeNode 313 ArrowFunction 313 " +
    "TypeParameterDeclaration 304 UnionTypeNode 282 ParenthesizedExpression 267 PrefixUnaryExpression 256 QualifiedName 244 " +
    "AsExpression 243 ArrayTypeNode 241 ClassDeclaration 199 MethodDeclaration 194 TypeLiteralNode 188 CaseClause 180 " +
    "BindingElement 163 InterfaceDeclaration 153 ConditionalExpression 152 PropertyDeclaration 148 " +
    "NoSubstitutionTemplateLiteral 133 NewExpression 125 EnumMember 122 FunctionTypeNode 120 TypeAliasDeclaration 110 " +
    "ExpressionWithTypeArguments 101 ShorthandPropertyAssignment 100 MethodSignature 100 TemplateSpan 98 HeritageClause 92 " +
    "TypeOperatorNode 86 ImportDeclaration 83 ImportClause 81 ModuleDeclaration 79 TemplateHead 75 TemplateTail 75 " +
    "ModuleBlock 74 TemplateExpression 67 ObjectBindingPattern 59 ComputedPropertyName 50 NonNullExpression 50 " +
    "NamedImports 49 ConstructorDeclaration 47 FunctionExpression 46 PostfixUnaryExpression 45 BreakStatement 39 " +
    "ForOfStatement 39 ExportDeclaration 37 TupleTypeNode 37 IndexedAccessTypeNode 36 JSDocTypeExpression 36 " +
    "TemplateMiddle 34 TypeQueryNode 34 JSDoc 32 JsxAttributes 31 IntersectionTypeNode 30 IndexSignatureDeclaration 29 " +
    "SpreadElement 29 GetAccessorDeclaration 27 ArrayBindingPattern 27 CallSignatureDeclaration 26 ForStatement 25 " +
    "SwitchStatement 25 CaseBlock 25 NamespaceImport 25 TypeOfExpression 24 ImportEqualsDeclaration 24 PrivateIdentifier 24 " +
    "TypePredicateNode 24 ConditionalTypeNode 24 EnumDeclaration 24 NamedExports 23 ParenthesizedTypeNode 23 " +
    "ExportSpecifier 22").split(" ").reduce((pairs, x, i, all) => i % 2 ? pairs : [...pairs, [x, Number(all[i + 1])]], []));

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

// Field storage: { store: "slot" (value: child handle or scalar) | "ref" (object), type (C#, without `?`), nullable (refs), conv (slots: node|bool|int|enum) }.
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
        for (const f of list) f.side = SIDE_FIELDS.has(f.name);
        fields[cls] = { list, payload: list.filter(f => !f.side) };
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
    const accessors = new Map(); // accessor -> { store, type, nullable, conv, side, owners: [cls] }
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
                if (!accessors.has(accessor)) accessors.set(accessor, { store: f.store, type: f.type, nullable: false, conv: f.conv, side: f.side, owners: [] });
                const a = accessors.get(accessor);
                a.nullable ||= !!f.nullable;
                if (a.side !== f.side) throw new Error(`${accessor}: side table on some classes only`);
                a.owners.push(cls);
                accessorOf[`${cls}.${name}`] = accessor;
                f.accessor = accessor;
            }
        });
    }

    const classId = new Map(classes.map((c, i) => [c, i]));
    const tableOf = (valueOf, count = kindCount) => {
        const values = [];
        for (let k = 0; k < count; k++) values.push(valueOf(k));
        const rows = [];
        for (let i = 0; i < values.length; i += 40) rows.push("        " + values.slice(i, i + 40).join(",") + ",");
        return rows;
    };
    const kindClass = new Array(kindCount).fill("Token");
    for (const name of kindNames) kindClass[kindValue(name)] = schema.kindToClass[name];
    const kindsOf = cls => kindNames.filter(k => schema.kindToClass[k] === cls).sort((a, b) => kindValue(a) - kindValue(b));
    if (classes.length > 255) throw new Error("more than 255 node classes");
    const payloadClasses = classes.filter(c => fields[c].payload.length > 0);
    for (const c of Object.keys(CLASS_SHARES)) if (!payloadClasses.includes(c)) throw new Error(`CLASS_SHARES: ${c} has no payload`);
    // Payload struct layout per class (explicit, so that offset tables and the struct agree): object references
    // first (8 bytes each, pointer aligned also on 64-bit), then 4-byte values (child handles, ints, enums), then bools.
    const layout = {};
    for (const cls of payloadClasses) {
        const order = f => f.store === "ref" ? 0 : f.type === "bool" ? 2 : 1;
        const list = [...fields[cls].payload].sort((a, b) => order(a) - order(b));
        let offset = 0;
        for (const f of list) {
            f.byteOffset = offset;
            offset += f.store === "ref" ? 8 : f.type === "bool" ? 1 : 4;
        }
        const align = list.some(f => f.store === "ref") ? 8 : 4;
        layout[cls] = { list, size: Math.ceil(offset / align) * align };
    }
    const singleOwner = a => a.owners.length === 1;
    // Classes whose payloads have the same size and the same reference offsets share one arena (a {Shape} array
    // viewed as the class's payload struct): 194 payload classes have 17 shapes, so a small file has about 7 arenas
    // instead of 11.
    const signature = cls => `${layout[cls].size}:${layout[cls].list.filter(f => f.store === "ref").map(f => f.byteOffset).join(",")}`;
    const shapes = [...new Set(payloadClasses.map(signature))].sort((a, b) => {
        const [sa, ra] = a.split(":"), [sb, rb] = b.split(":");
        return Number(sa) - Number(sb) || ra.length - rb.length || (ra < rb ? -1 : ra > rb ? 1 : 0);
    });
    if (shapes.length > 31) throw new Error("more than 31 payload shapes: offset tables keep the shape in 5 bits");
    if (payloadClasses.some(c => layout[c].size > 2048)) throw new Error("payload above 2,048 bytes: offset tables keep the offset in 11 bits");
    const shapeOfKind = k => layout[kindClass[k]] ? shapes.indexOf(signature(kindClass[k])) : -1;
    if (kindCount > 512) throw new Error("more than 512 kinds: NodeStore's header keeps the kind in 9 bits");
    const shapeShare = s => payloadClasses.filter(c => signature(c) === shapes[s]).reduce((n, c) => n + (CLASS_SHARES[c] ?? 0), 0);
    if (shapes.some((_, s) => shapeShare(s) > 0xFFFF)) throw new Error("shape share above 65,535");

    const lines = [header("src/compiler/types.ts (node interfaces) and src/compiler/parser.ts (forEachChildTable)"),
        "#nullable enable", "", "using System.Runtime.CompilerServices;", "using System.Runtime.InteropServices;", "using NeverSharp.Generated;", "",
        "namespace NeverSharp.Optimized;", "",
        "/// <summary>The generated part of the node store: node classes, payload structs, field accessors and node class tests (O6b variant C).</summary>",
        "internal sealed partial class NodeStore", "{",
        `    private const int KindCount = ${kindCount};`, "",
        "    /// <summary>The node class of each kind (index into the classes of types.ts, Token for kinds without one).</summary>",
        "    private static ReadOnlySpan<byte> ClassIds =>", "    [",
        ...tableOf(k => classId.get(kindClass[k])), "    ];", "",
        "    /// <summary>Bytes of a payload per kind (0: the kind's class has no fields in a payload).</summary>",
        "    private static ReadOnlySpan<ushort> PayloadSizes =>", "    [",
        ...tableOf(k => layout[kindClass[k]]?.size ?? 0), "    ];", "",
        `    private const int ShapeCount = ${shapes.length};`, "",
        "    /// <summary>The payload shape of each kind, indexed by the header's kind bits (NoShape: none): the arena its payloads share with other kinds of the same shape.</summary>",
        "    private static ReadOnlySpan<byte> ShapeIds =>", "    [",
        ...tableOf(k => k >= kindCount || shapeOfKind(k) < 0 ? 255 : shapeOfKind(k), 512), "    ];", "",
        "    /// <summary>The store's arenas, one per payload shape (created with the shape's first node).</summary>",
        "    [InlineArray(ShapeCount)]", "    private struct Arenas", "    {", "        private Arena arena;", "    }", "",
        "    /// <summary>Nodes of each shape per 65,536 nodes (tuning corpus): the first capacity of the shape's arena.</summary>",
        "    private static ReadOnlySpan<ushort> ShapeShares =>", "    [",
        ...tableOf(shapeShare, shapes.length), "    ];", ""];
    for (const name of SIDE_FIELDS) {
        lines.push(`    /// <summary>Whether a kind's class has the field ${name} (kept in a side table, not in the payload).</summary>`,
            `    private static ReadOnlySpan<byte> ${name}Owners =>`, "    [",
            ...tableOf(k => fields[kindClass[k]].list.some(f => f.name === name) ? 1 : 0), "    ];", "");
    }
    lines.push("#pragma warning disable CS0169 // The fields of a shape only tell the collector where the references are.", "");
    shapes.forEach((sig, s) => {
        const [size, refs] = sig.split(":");
        lines.push(`    [StructLayout(LayoutKind.Explicit, Size = ${size})]`, `    private struct Shape${s}`, "    {");
        const offsets = refs ? refs.split(",") : [];
        if (offsets.length === 0) lines.push("        [FieldOffset(0)]", "        private int value;");
        offsets.forEach((o, i) => lines.push(...(i ? [""] : []), `        [FieldOffset(${o})]`, `        private object? reference${o};`));
        lines.push("    }", "");
    });
    lines.push("#pragma warning restore CS0169", "");
    for (const cls of payloadClasses) {
        lines.push(`    // Shape${shapes.indexOf(signature(cls))}`, `    [StructLayout(LayoutKind.Explicit, Size = ${layout[cls].size})]`, `    private struct ${cls}Payload`, "    {");
        for (const f of layout[cls].list) lines.push(`        [FieldOffset(${f.byteOffset})]`, `        public ${f.store === "ref" ? `${f.type}?` : f.type} ${f.name};`, "");
        lines.pop();
        lines.push("    }", "");
    }
    lines.push("    /// <summary>An arena of the shape with the given capacity.</summary>",
        "    private static Array NewPayloads(int shapeId, int length)", "    {", "        switch (shapeId)", "        {");
    shapes.forEach((_, s) => lines.push(`            case ${s}:`, `                return new Shape${s}[length];`));
    lines.push("            default:", "                throw new InvalidOperationException(\"no payload\");", "        }", "    }", "");

    const accessor = (name, a) => {
        const nullable = a.store === "ref" && a.nullable;
        const ret = a.store === "ref" ? `${a.type}${nullable ? "?" : ""}` : a.type;
        const field = cls => fields[cls].list.find(f => f.accessor === name);
        if (a.side) {
            return [
                `    public ${ret} ${name}(Node node) => ${name}Entry(node);`, "",
                `    public void Set${name}(Node node, ${ret} value)`, "    {",
                `        if (${name}Owners[(int)Kind(node)] == 0)`, "        {", `            ThrowNoField(Kind(node), nameof(${name}));`, "        }",
                `        Set${name}Entry(node, value);`, "    }", ""];
        }
        const absent = a.store === "ref" ? (nullable ? "null" : "null!") : "default";
        const bang = a.store === "ref" && !nullable ? "!" : "";
        if (singleOwner(a)) {
            // One class: a test of the kind, then the typed payload.
            const cls = a.owners[0];
            const test = kindsOf(cls).map(k => `(h & KindMask) == (int)SyntaxKind.${k}`).join(" || ");
            const at = `PayloadOf<${cls}Payload>(h, ${shapes.indexOf(signature(cls))}).${field(cls).name}`;
            return [
                `    public ${ret} ${name}(Node node)`, "    {",
                "        var h = headers[node.Index].KindPayload;",
                `        return ${test} ? ${at}${bang} : ${absent};`, "    }", "",
                `    public void Set${name}(Node node, ${ret} value)`, "    {",
                "        var h = headers[node.Index].KindPayload;",
                `        if (!(${test}))`, "        {", `            ThrowNoField((SyntaxKind)(h & KindMask), nameof(${name}));`, "        }",
                `        ${at} = value;`, "    }", ""];
        }
        // Several classes: the payload shape (high 5 bits) and the field's offset in the payload (low 11 bits) of the
        // node's kind (0xFFFF: no such field).
        const offsets = new Map(a.owners.map(cls => [cls, (shapes.indexOf(signature(cls)) << 11) | field(cls).byteOffset]));
        const view = `Unsafe.As<byte, ${ret}>(ref PayloadByte(h, o))`;
        return [
            `    public ${ret} ${name}(Node node)`, "    {",
            "        var h = headers[node.Index].KindPayload;",
            `        int o = ${name}Offsets[h & KindMask];`,
            `        return o == NoField ? ${absent} : ${view}${bang};`, "    }", "",
            `    public void Set${name}(Node node, ${ret} value)`, "    {",
            "        var h = headers[node.Index].KindPayload;",
            `        int o = ${name}Offsets[h & KindMask];`,
            "        if (o == NoField)", "        {", `            ThrowNoField((SyntaxKind)(h & KindMask), nameof(${name}));`, "        }",
            `        ${view} = value;`, "    }", "",
            `    private static ReadOnlySpan<ushort> ${name}Offsets =>`, "    [",
            ...tableOf(k => offsets.has(kindClass[k]) ? offsets.get(kindClass[k]) : 0xFFFF, 512), "    ];", ""];
    };
    for (const [name, a] of [...accessors].sort((x, y) => x[0] < y[0] ? -1 : 1)) {
        const owners = a.owners;
        lines.push(`    // ${owners.length > 6 ? `${owners.slice(0, 6).join(", ")}, … (${owners.length} classes)` : owners.join(", ")}`);
        lines.push(...accessor(name, a));
    }
    lines.push("    // Node classes of types.ts: whether a node's kind is one of the class's kinds.");
    for (const cls of classes) lines.push(`    public bool Is${cls}(Node node) => !node.IsNone && ClassIds[(int)Kind(node)] == ${classId.get(cls)};`, "");
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
