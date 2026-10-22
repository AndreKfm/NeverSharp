// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O emitter for the node store's field accessors and ForEachChild (steps O6a, O6b, O7a, O5a).
//
// Track O's nodes are int handles into a per-parse arena (pre-registration §5.4); the parser and the factory
// read and write a node only through NodeStore (src/NeverSharp.Optimized/Arena/NodeStore.cs). This emitter writes
// the mechanical part of that API from the same schema as Track F's node classes (emit-faithful.mjs):
// - The field set per node class is Track F's (a field exists when the interface declares the property and
//   parser.ts, factory/nodeFactory.ts or utilities.ts assign it), without the binder and checker data the
//   parser never touches, plus the SourceFile members Track F declares by hand (Parser/Types.cs, Parser.Pragmas.cs).
// - Layout of O6b variant A (compact records): a node is a variable-length record in one int array, its handle
//   the record's offset. The record holds the header (kind word, flags, pos, end, transform flags), the index of
//   the node's first object slot (only for kinds with object slots), and one int slot per child handle or scalar
//   field; boolean fields are bits of the kind word; node lists are list handles (NodeList, step O7a) in int slots;
//   texts are text handles (TextRef, TextOrList, step O5a) in two int slots; the SourceFile node's strings and other
//   objects live in a block of object slots per node; rare fields (SIDE_FIELDS) live in the
//   store's side table. A field's word offset, bit
//   or object slot depends on the node's class, looked up per SyntaxKind in a table.
// - One getter and one setter per field name (`Expression(n)`, `SetExpression(n, v)`). A getter on a kind
//   without the field reads as undefined (default), as the property does in TypeScript; a setter throws.
//   Where one name has different types on different classes, the minority classes get `{Class}{Field}`.
// - `Is{Class}(n)`: whether a node's kind belongs to a node class (Track F's `node is Class`).
// - NodeVisitor.ForEachChild over handles, translating forEachChildTable entry by entry; list elements are read
//   through the store's list store.
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
    "Create", "GetMark", "Truncate", "Count",
    // The list store (NodeList.cs, step O7a).
    "Nodes", "Length", "Element", "HasTrailingComma", "IsMissingList", "SetIsMissingList", "NewList", "BeginList", "Push", "FinishList", "DiscardList"]);
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
    // SourceFile.identifiers: the parse's texts, whose interning table is TypeScript's map (step O5a).
    "ReadonlyMap<string, string>": { type: "TextTable", nullable: true },
    "(file: SourceFile) => void": { type: "Action<Node>", nullable: true },
    "true | Node": { type: "object", nullable: true },
};
// Text values (step O5a): handles into the parse's texts (TextRef: source span or character arena) in two int slots;
// `string | NodeArray` (JSDoc comments) as TextOrList in two int slots. The SourceFile node keeps its strings (its
// source text and file names) in object slots.
const TEXT = { store: "slot", type: "TextRef", conv: "text", words: 2 };
const TEXT_OR_LIST = { store: "slot", type: "TextOrList", conv: "textOrList", words: 2 };
// __String (Identifier.escapedText, PrivateIdentifier.escapedText): the parser stores the escaped text.
const ESCAPED_TEXT = "(string & { __escapedIdentifier: void; }) | InternalSymbolName";

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

// Field storage: { store: "slot" | "ref", type (C#, without `?`), nullable (refs), conv (slots: node|list|bool|int|enum|text|textOrList),
// words (slots of two ints) }.
function storage(cls, cs, optional, usedAs) {
    if (cls !== "SourceFile" && (cs.k === "string" || cs.ts === ESCAPED_TEXT)) return TEXT;
    switch (cs.k) {
        case "string": return { store: "ref", type: "string", nullable: optional };
        case "bool": return { store: "slot", type: "bool", conv: "bool" };
        case "int": return { store: "slot", type: "int", conv: "int" };
        case "enum": return cs.name === "int" ? { store: "slot", type: "int", conv: "int" } : { store: "slot", type: cs.name, conv: "enum" };
        case "node": return { store: "slot", type: "Node", conv: "node" };
        case "nodes": return { store: "slot", type: "NodeList", conv: "list" };
        case "array": return { store: "ref", type: "Node[]", nullable: true };
        case "stringOrNodes": return TEXT_OR_LIST;
        case "undefinedOnly":
            // Declared `?: undefined`; assigned for grammar errors where forEachChild visits it, never otherwise.
            return usedAs === "nodes" ? { store: "slot", type: "NodeList", conv: "list" }
                : usedAs === "node" ? { store: "slot", type: "Node", conv: "node" } : null;
        default: return OBJECT_TYPES[cs.ts] ? { store: "ref", ...OBJECT_TYPES[cs.ts] } : null; // binder/checker data: not stored
    }
}

// JSDocSignature.parameters: parseCallbackTagParameters (parser.ts) also keeps non-parameter tags there.
const FIELD_OVERRIDES = { "JSDocSignature.parameters": { store: "ref", type: "Node[]", nullable: true } };

// Record layout (O6b variant A). Words of the header; NodeStore.cs uses the same constants.
const HEADER_WORDS = 5; // kind word, flags, pos, end, transform flags
const REF_BASE_WORD = HEADER_WORDS; // index of the first object slot, only in records of kinds with object slots
const FIRST_BIT = 16; // boolean fields: bits 16..30 of the kind word (kind in bits 0..15, bit 31: side table entries)
const LAST_BIT = 30;
// Fields that are undefined on almost every node: kept in the store's side table instead of the record. Chosen from
// the tuning corpus (JSDocParsingMode.ParseForTypeErrors): set on fewer than about 3 % of the nodes of their class
// (most never: jsDoc only on nodes with a JSDoc comment the parser keeps, nextContainer is binder data the factory
// only clears, some members exist for grammar errors only). Classes with fewer than about 1,000 nodes in the corpus
// are left alone. Reading a side field of a node without side entries costs a bit test of the kind word.
// In every class, except the classes listed:
const SIDE_FIELDS = {
    jsDoc: [], nextContainer: [], textSourceNode: [], thisArg: [], tupleNameSource: [], isSpread: [], _children: [], sourceFiles: [],
    exclamationToken: [], asteriskToken: ["YieldExpression"], questionDotToken: [], templateFlags: [], numericLiteralFlags: [], assertClause: [],
    typeArguments: ["TypeReferenceNode", "NewExpression", "ExpressionWithTypeArguments", "ImportTypeNode", "JsxOpeningElement", "JsxSelfClosingElement"],
};
// Only in the classes listed:
const SIDE_IN = {
    modifiers: ["PropertyAssignment", "ParameterDeclaration", "TypeParameterDeclaration", "ShorthandPropertyAssignment", "MethodSignature",
        "FunctionTypeNode", "ConstructorTypeNode", "ImportDeclaration", "ExportDeclaration", "ExportAssignment", "ArrowFunction"],
    questionToken: ["PropertyAssignment", "ShorthandPropertyAssignment", "MethodDeclaration", "FunctionDeclaration", "FunctionExpression", "ArrowFunction",
        "ConstructorDeclaration", "GetAccessorDeclaration", "SetAccessorDeclaration", "CallSignatureDeclaration", "ConstructSignatureDeclaration",
        "IndexSignatureDeclaration", "PropertyDeclaration"],
    initializer: ["ParameterDeclaration", "PropertySignature"],
    dotDotDotToken: ["ParameterDeclaration"],
    typeParameters: ["ArrowFunction", "ConstructorDeclaration", "GetAccessorDeclaration", "SetAccessorDeclaration", "IndexSignatureDeclaration"],
    name: ["CallSignatureDeclaration", "ConstructSignatureDeclaration", "ConstructorDeclaration", "ConstructorTypeNode", "FunctionTypeNode",
        "IndexSignatureDeclaration", "ExportDeclaration", "ExportAssignment", "SpreadAssignment"],
    propertyName: ["ImportSpecifier"],
    equalsToken: ["ShorthandPropertyAssignment"],
    objectAssignmentInitializer: ["ShorthandPropertyAssignment"],
    attributes: ["ImportDeclaration", "ExportDeclaration"],
    expression: ["TypeParameterDeclaration"],
    type: ["ConstructorDeclaration", "SetAccessorDeclaration"],
};
const isSide = (cls, field) => Object.hasOwn(SIDE_FIELDS, field) ? !SIDE_FIELDS[field].includes(cls) : !!SIDE_IN[field]?.includes(cls);
// The NoAlloc contract of Track O's code (pre-registration §5.4, step O-NA). The analyzer does not verify generated code;
// the members marked here only read and write arrays and call members of NodeStore.cs that it verifies.
const NO_ALLOC = "    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]";
// Offset tables: -1 absent, SIDE_OFFSET in the side table, otherwise the word offset, bit or object slot.
const SIDE_OFFSET = 127;
// Side table ids of the base fields NodeStore.cs keeps there (parent, original, modifierFlagsCache, emitNode).
const FIRST_SIDE_ID = 4;

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
            const s = FIELD_OVERRIDES[`${cls}.${p.name}`] ?? storage(cls, p.cs, p.optional, usage[cls]?.[p.name]);
            if (!s) { (omitted[cls] ??= []).push(pascal(p.name)); continue; }
            list.push({ ts: p.name, name: pascal(p.name), ...s });
        }
        for (const [name, type, nullable] of EXTRA_FIELDS[cls] ?? []) list.push({ ts: name, name, store: "ref", type, nullable });
        list.sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0);
        // Layout: side table, bit of the kind word, object slot, or int slot (word offset in the record).
        let slot = 0, ref = 0, bit = FIRST_BIT;
        for (const f of list) {
            f.layout = isSide(cls, f.ts) ? "side" : f.store === "ref" ? "ref" : f.conv === "bool" ? "bit" : "word";
            if (f.layout === "side" && f.words === 2) throw new Error(`${cls}.${f.ts}: a two-int field cannot live in the side table`);
            if (f.layout === "side") f.offset = SIDE_OFFSET;
            if (f.layout === "ref") f.offset = ref++;
            if (f.layout === "bit") f.offset = bit++;
        }
        if (bit > LAST_BIT + 1) throw new Error(`${cls}: more boolean fields than free bits in the kind word`);
        const firstSlot = HEADER_WORDS + (ref > 0 ? 1 : 0);
        for (const f of list) if (f.layout === "word") { f.offset = firstSlot + slot; slot += f.words ?? 1; }
        if (firstSlot + slot >= SIDE_OFFSET || ref >= SIDE_OFFSET) throw new Error(`${cls}: record too large for sbyte offsets`);
        fields[cls] = { list, words: firstSlot + slot, refs: ref };
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
                if (!accessors.has(accessor)) accessors.set(accessor, { store: f.store, layouts: new Set(), type: f.type, nullable: false, conv: f.conv, offsets: new Map() });
                const a = accessors.get(accessor);
                a.layouts.add(f.layout);
                if (a.layouts.has("bit") && a.layouts.size > 1) throw new Error(`accessor ${accessor}: a boolean field in a bit and elsewhere`);
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
        "#nullable enable", "", "using System.Runtime.CompilerServices;", "using NeverSharp.Generated;", "", "namespace NeverSharp.Optimized;", "",
        "/// <summary>The generated part of the node store: layout tables, field accessors and node class tests (O6b variant A layout).</summary>",
        "internal sealed partial class NodeStore", "{",
        `    private const int KindCount = ${kindCount};`, "",
        `    private const int HeaderWords = ${HEADER_WORDS};`, "",
        `    private const int RefBaseWord = ${REF_BASE_WORD};`, "",
        "    /// <summary>The offset table value of a field kept in the side table.</summary>",
        `    private const int SideOffset = ${SIDE_OFFSET};`, "",
        "    /// <summary>Record size in ints per kind: header, first object slot (kinds with object slots), int slots.</summary>",
        NO_ALLOC, "    private static ReadOnlySpan<byte> RecordSizes =>", "    [",
        ...tableOf(k => fields[kindClass[k]].words), "    ];", "",
        "    /// <summary>Object slots (strings, node lists, other objects) per kind.</summary>",
        NO_ALLOC, "    private static ReadOnlySpan<byte> RefCounts =>", "    [",
        ...tableOf(k => fields[kindClass[k]].refs), "    ];", "",
        "    /// <summary>The node class of each kind (index into the classes of types.ts, Token for kinds without one).</summary>",
        "    private static ReadOnlySpan<byte> ClassIds =>", "    [",
        ...tableOf(k => classId.get(kindClass[k])), "    ];", ""];
    if (classes.length > 255) throw new Error("more than 255 node classes");

    let nextSideId = FIRST_SIDE_ID;
    const getter = (name, a) => {
        const isRef = a.store === "ref";
        const nullable = isRef && a.nullable;
        const ret = isRef ? `${a.type}${nullable ? "?" : ""}` : a.type;
        const absent = isRef ? (nullable ? "null" : "null!") : "default";
        const fromInt = v => a.conv === "node" ? `new Node(${v})` : a.conv === "list" ? `new NodeList(${v})` : a.conv === "bool" ? `${v} != 0` : a.conv === "enum" ? `(${a.type})${v}` : v;
        const toInt = a.conv === "node" || a.conv === "list" ? "value.Index" : a.conv === "bool" ? "(value ? 1 : 0)" : a.conv === "enum" ? "(int)value" : "value";
        const cast = v => a.type === "object" ? v : `Unsafe.As<${ret}>(${v})${nullable ? "" : "!"}`;
        const side = a.layouts.has("side");
        const layout = [...a.layouts].find(l => l !== "side") ?? "side"; // word, ref or bit; side if only there
        const table = layout === "bit" ? `${name}Bits` : `${name}Offsets`;
        if (side) {
            a.sideId = nextSideId++;
            if (a.sideId > 255) throw new Error("more than 256 side table fields");
        }
        const sideRead = isRef ? cast(`GetSideRef(h, ${a.sideId})`) : fromInt(`GetSideInt(h, ${a.sideId})`);
        const sideWrite = isRef ? `SetSideRef(h, ${a.sideId}, value);` : `SetSideInt(h, ${a.sideId}, ${toInt});`;
        // Text values (two ints): TextRef (start, size) or TextOrList (a, b).
        const pair = a.conv === "text" ? ["Start", "Size"] : a.conv === "textOrList" ? ["A", "B"] : null;
        if (pair && side) throw new Error(`accessor ${name}: a two-int field in the side table`);
        const recordRead = layout === "ref" ? cast("refs[d[h + RefBaseWord] + o]") : pair ? `new ${a.type}(d[h + o], d[h + o + 1])` : fromInt("d[h + o]");
        const recordWrite = layout === "ref" ? "refs[d[h + RefBaseWord] + o] = value;" : pair ? `d[h + o] = value.${pair[0]};\n        d[h + o + 1] = value.${pair[1]};` : `d[h + o] = ${toInt};`;
        const head = ["        var d = data;", "        var h = node.Index;"];
        const throwIf = cond => [`        if (${cond})`, "        {", `            ThrowNoField(d[h], nameof(${name}));`, "        }"];
        let get, set;
        if (layout === "bit") {
            get = ["        var w = data[node.Index];", `        var b = ${table}[w & KindMask];`, "        return b >= 0 && (w & (1 << b)) != 0;"];
            set = [...head, "        var w = d[h];", `        var b = ${table}[w & KindMask];`, ...throwIf("b < 0"), "        d[h] = value ? w | (1 << b) : w & ~(1 << b);"];
        } else if (layout === "side") {
            // Only in the side table: the side bit of the kind word (bit 31) says whether to look there.
            get = [...head, "        var w = d[h];", `        return w >= 0 || ${table}[w & KindMask] < 0 ? ${absent} : ${sideRead};`];
            set = [...head, ...throwIf(`${table}[d[h] & KindMask] < 0`), `        ${sideWrite}`];
        } else if (side) {
            // In the record for most classes, in the side table for some.
            get = [...head, "        var w = d[h];", `        var o = ${table}[w & KindMask];`, "        if ((uint)o < SideOffset)", "        {", `            return ${recordRead};`, "        }",
                `        return o == SideOffset && w < 0 ? ${sideRead} : ${absent};`];
            set = [...head, `        var o = ${table}[d[h] & KindMask];`, "        if ((uint)o < SideOffset)", "        {", `            ${recordWrite}`, "            return;", "        }",
                ...throwIf("o != SideOffset"), `        ${sideWrite}`];
        } else {
            get = [...head, `        var o = ${table}[d[h] & KindMask];`, `        return o < 0 ? ${absent} : ${recordRead};`];
            set = [...head, `        var o = ${table}[d[h] & KindMask];`, ...throwIf("o < 0"), `        ${recordWrite}`];
        }
        // NoAlloc (step O-NA): the accessors read and write the record arrays and the side table (whose growth, and the
        // exception for a missing field, are the store's [NoAllocUnchecked] members).
        return [
            NO_ALLOC, "    [MethodImpl(MethodImplOptions.AggressiveInlining)]", `    public ${ret} ${name}(Node node)`, "    {", ...get, "    }", "",
            NO_ALLOC, "    [MethodImpl(MethodImplOptions.AggressiveInlining)]", `    public void Set${name}(Node node, ${ret} value)`, "    {", ...set, "    }", "",
            `    private static ReadOnlySpan<sbyte> ${table} =>`, "    [",
            ...tableOf(k => a.offsets.has(kindClass[k]) ? a.offsets.get(kindClass[k]) : -1), "    ];", ""];
    };
    for (const [name, a] of [...accessors].sort((x, y) => x[0] < y[0] ? -1 : 1)) {
        const owners = [...a.offsets.keys()];
        const where = a.layouts.size === 1 && a.layouts.has("side") ? " (side table)" : a.layouts.has("side") ? " (some in the side table)" : "";
        lines.push(`    // ${owners.length > 6 ? `${owners.slice(0, 6).join(", ")}, … (${owners.length} classes)` : owners.join(", ")}${where}`);
        lines.push(...getter(name, a));
    }
    lines.push("    // Node classes of types.ts: whether a node's kind is one of the class's kinds.");
    for (const cls of classes) lines.push(NO_ALLOC, `    public bool Is${cls}(Node node) => !node.IsNone && ClassIds[data[node.Index] & KindMask] == ${classId.get(cls)};`, "");
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
            case "nodes": { const f = field(cls, ir.prop); return f ? `VisitNodes(store, cbNode, cbNodes, ${f.conv === "textOrList" ? `store.${f.accessor}(node).List` : `store.${f.accessor}(node)`})` : "null"; }
            case "each": { const f = field(cls, ir.prop); return f ? `ForEachElement(store.${f.accessor}(node), cbNode)` : "null"; }
            case "cond": return `(${test(ir.test, cls)}\n                ? ${expr(ir.then, cls)}\n                : ${expr(ir.else, cls)})`;
            default: throw new Error(`unknown IR op ${ir.op}`);
        }
    }
    function test(t, cls) {
        const f = t.op === "and" ? null : field(cls, t.prop);
        if (t.op !== "and" && !f) throw new Error(`${cls}.${t.prop} tested but not stored`);
        switch (t.op) {
            case "isString": return f.conv === "textOrList" ? `store.${f.accessor}(node).IsText` : `store.${f.accessor}(node) is string`;
            case "bool": return f.type === "bool" ? `store.${f.accessor}(node)` : f.conv === "node" || f.conv === "list" || f.conv === "text" || f.conv === "textOrList" ? `!store.${f.accessor}(node).IsNone` : `store.${f.accessor}(node) is not null`;
            case "kindIs": return `store.Kind(store.${f.accessor}(node)) == SyntaxKind.${t.kind}`;
            case "and": return `(${test(t.left, cls)} && ${test(t.right, cls)})`;
            default: throw new Error(`unknown test ${t.op}`);
        }
    }
    const v = [header("src/compiler/parser.ts (forEachChild, forEachChildTable, visitNode, visitNodes) and src/compiler/core.ts (forEach)"),
        "#nullable enable", "", "using NeverSharp.Generated;", "", "namespace NeverSharp.Optimized;", "",
        "internal static partial class NodeVisitor", "{",
        "    /// <summary>parser.ts forEachChild: visits the children of a node in TypeScript's order; returns the first non-null callback result.</summary>",
        "    public static T? ForEachChild<T>(NodeStore store, Node node, Func<Node, T?> cbNode, Func<NodeList, T?>? cbNodes = null) where T : class",
        "    {",
        "        if (node.IsNone || store.Kind(node) <= SyntaxKind.LastToken)", "        {", "            return null;", "        }",
        "        switch (store.Kind(node))", "        {"];
    let entries = 0;
    for (const [kind, entry] of Object.entries(schema.children)) {
        v.push(`            case SyntaxKind.${kind}:`, `                return ${expr(entry.ir, schema.kindToClass[kind])};`);
        entries++;
    }
    v.push("            default:", "                return null;", "        }", "    }", "");
    // Track O (O-NA): forEachChild with a struct visitor (both callbacks in one value, no delegates; a value-type
    // result is not boxed). The same table as a chain of tests: each visit returns whether the search ends.
    function sexpr(ir, cls) {
        switch (ir.op) {
            case "or": return ir.items.map(i => sexpr(i, cls)).join("\n                || ");
            case "none": return "false";
            case "node": { const f = field(cls, ir.prop); return f ? `Visit(ref visitor, store.${f.accessor}(node), ref result)` : "false"; }
            case "nodes": { const f = field(cls, ir.prop); return f ? `VisitList(ref visitor, ${f.conv === "textOrList" ? `store.${f.accessor}(node).List` : `store.${f.accessor}(node)`}, ref result)` : "false"; }
            case "each": { const f = field(cls, ir.prop); return f ? `VisitEach(ref visitor, store.${f.accessor}(node), ref result)` : "false"; }
            case "cond": return `(${test(ir.test, cls)}\n                ? ${sexpr(ir.then, cls)}\n                : ${sexpr(ir.else, cls)})`;
            default: throw new Error(`unknown IR op ${ir.op}`);
        }
    }
    v.push("    /// <summary>",
        "    /// parser.ts forEachChild with a struct visitor (Track O, O-NA): <c>cbNode</c> is <see cref=\"INodeVisitor{T}.VisitNode\"/>,",
        "    /// <c>cbNodes</c> <see cref=\"INodeVisitor{T}.VisitNodes\"/>; returns the first truthy result (default if there is none).",
        "    /// </summary>",
        NO_ALLOC,
        "    public static T? ForEachChild<TVisitor, T>(NodeStore store, Node node, TVisitor visitor) where TVisitor : struct, INodeVisitor<T>",
        "    {",
        "        if (node.IsNone || store.Kind(node) <= SyntaxKind.LastToken)", "        {", "            return default;", "        }",
        "        T result = default!;",
        "        switch (store.Kind(node))", "        {");
    for (const [kind, entry] of Object.entries(schema.children)) {
        v.push(`            case SyntaxKind.${kind}:`, `                return ${sexpr(entry.ir, schema.kindToClass[kind])}`, "                ? result", "                : default;");
    }
    v.push("            default:", "                return default;", "        }", "    }", "",
        "    // parser.ts visitNode", NO_ALLOC,
        "    private static bool Visit<TVisitor, T>(ref TVisitor visitor, Node node, ref T result) where TVisitor : struct, INodeVisitor<T>",
        "    {", "        if (node.IsNone)", "        {", "            return false;", "        }", "        result = visitor.VisitNode(node);", "        return Core.IsTruthyValue(result);", "    }", "",
        "    // parser.ts visitNodes", NO_ALLOC,
        "    private static bool VisitList<TVisitor, T>(ref TVisitor visitor, NodeList nodes, ref T result) where TVisitor : struct, INodeVisitor<T>",
        "    {", "        if (nodes.IsNone)", "        {", "            return false;", "        }", "        result = visitor.VisitNodes(nodes);", "        return Core.IsTruthyValue(result);", "    }", "",
        "    // core.ts forEach(array, callback): every element through cbNode", NO_ALLOC,
        "    private static bool VisitEach<TVisitor, T>(ref TVisitor visitor, IReadOnlyList<Node>? array, ref T result) where TVisitor : struct, INodeVisitor<T>",
        "    {", "        if (array is not null)", "        {", "            for (var i = 0; i < array.Count; i++)", "            {", "                result = visitor.VisitNode(array[i]);",
        "                if (Core.IsTruthyValue(result))", "                {", "                    return true;", "                }", "            }", "        }", "        return false;", "    }", "",
        "    // parser.ts visitNode", "    private static T? VisitNode<T>(Func<Node, T?> cbNode, Node node) where T : class => node.IsNone ? null : cbNode(node);", "",
        "    // parser.ts visitNodes (a list's elements stay where they are while lists are added)",
        "    private static T? VisitNodes<T>(NodeStore store, Func<Node, T?> cbNode, Func<NodeList, T?>? cbNodes, NodeList nodes) where T : class",
        "    {", "        if (!nodes.IsNone)", "        {", "            if (cbNodes is not null)", "            {", "                return cbNodes(nodes);", "            }",
        "            foreach (var node in store.Nodes(nodes))", "            {", "                var result = cbNode(node);", "                if (result is not null)", "                {", "                    return result;", "                }", "            }", "        }", "        return null;", "    }", "",
        "    // core.ts forEach(array, callback): every element through cbNode, also when cbNodes is given",
        "    private static T? ForEachElement<T>(IReadOnlyList<Node>? array, Func<Node, T?> cbNode) where T : class",
        "    {", "        if (array is not null)", "        {", "            for (var i = 0; i < array.Count; i++)", "            {", "                var result = cbNode(array[i]);", "                if (result is not null)", "                {", "                    return result;", "                }", "            }", "        }", "        return null;", "    }",
        "}", "",
        "/// <summary>The two callbacks of forEachChild (<c>cbNode</c>, <c>cbNodes</c>) as a struct (Track O, O-NA).</summary>",
        "internal interface INodeVisitor<out T>", "{", NO_ALLOC, "    T VisitNode(Node node);", "", NO_ALLOC, "    T VisitNodes(NodeList nodes);", "}", "");
    writeFileSync(join(outDir, "NodeVisitor.g.cs"), v.join("\n"));

    const map = { classes: {}, omitted };
    for (const cls of classes) map.classes[cls] = Object.fromEntries(fields[cls].list.map(f => [f.name, { accessor: f.accessor, store: f.store, layout: f.layout, type: f.type, nullable: accessors.get(f.accessor).nullable }]));
    mkdirSync(join(repoRoot, "artifacts", "generate"), { recursive: true });
    writeFileSync(join(repoRoot, "artifacts", "generate", "optimized-fields.json"), JSON.stringify(map, null, 1));
    return { classes: classes.length, accessors: accessors.size, forEachChildEntries: entries, collisions };
}
