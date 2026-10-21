// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): extracts the TypeScript 6.0.3 AST schema from its sources.
//
// - Children per SyntaxKind, in order, from forEachChildTable (parser.ts):
//   each entry's return expression is translated into a small IR
//   (node / nodes / or / cond / isString / bool / none). An unknown construct is an error.
// - SyntaxKind -> node interface from the `kind` declarations in types.ts.
// - Properties per interface from the type checker.
//
// Writes artifacts/generate/ast-schema.json; used by the emitters.

import { readdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { repoRoot, ts, tsRoot } from "./common.mjs";

function* walk(dir) {
    for (const e of readdirSync(dir, { withFileTypes: true })) {
        const full = join(dir, e.name);
        if (e.isDirectory()) yield* walk(full);
        else if (e.name.endsWith(".ts") && !e.name.endsWith(".d.ts")) yield full;
    }
}

export function extractSchema() {
    const compilerDir = join(tsRoot, "src", "compiler");
    const program = ts.createProgram([...walk(compilerDir)], { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.NodeNext, moduleResolution: ts.ModuleResolutionKind.NodeNext, noEmit: true, types: [], skipLibCheck: true });
    const checker = program.getTypeChecker();
    const file = name => program.getSourceFiles().find(s => s.fileName.endsWith(`/compiler/${name}`));
    const parser = file("parser.ts"), types = file("types.ts");

    // --- forEachChildTable -------------------------------------------------
    const functions = new Map();
    for (const s of parser.statements) if (ts.isFunctionDeclaration(s) && s.name) functions.set(s.name.text, s);
    let table;
    ts.forEachChild(parser, function find(n) {
        if (ts.isVariableDeclaration(n) && n.name.getText(parser) === "forEachChildTable") table = n.initializer;
        if (!table) ts.forEachChild(n, find);
    });

    const unwrap = e => ts.isParenthesizedExpression(e) ? unwrap(e.expression) : e;
    // node.p, or (node as T).p in functions shared by several kinds (the property exists only on T;
    // for the other kinds TypeScript reads undefined and visits nothing).
    const propOf = e => {
        e = unwrap(e);
        if (ts.isPropertyAccessExpression(e)) {
            const target = unwrap(e.expression);
            if (ts.isIdentifier(target) && target.text === "node") return e.name.text;
            if (ts.isAsExpression(target) && ts.isIdentifier(target.expression) && target.expression.text === "node") return e.name.text;
        }
        throw new Error(`unexpected operand: ${e.getText(parser)}`);
    };
    function toIr(e) {
        e = unwrap(e);
        if (ts.isBinaryExpression(e) && e.operatorToken.kind === ts.SyntaxKind.BarBarToken) {
            const items = [toIr(e.left), toIr(e.right)].flatMap(x => x.op === "or" ? x.items : [x]);
            return { op: "or", items };
        }
        if (ts.isConditionalExpression(e)) return { op: "cond", test: toTest(e.condition), then: toIr(e.whenTrue), else: toIr(e.whenFalse) };
        if (ts.isIdentifier(e) && e.text === "undefined") return { op: "none" };
        if (ts.isCallExpression(e) && ts.isIdentifier(e.expression)) {
            const callee = e.expression.text;
            if (callee === "visitNode") return { op: "node", prop: propOf(e.arguments[1]) };
            if (callee === "visitNodes") return { op: "nodes", prop: propOf(e.arguments[2]) };
            // forEach(node.p, cbNode): every element through cbNode, even when a cbNodes callback is given
            if (callee === "forEach" && e.arguments[1]?.getText(parser) === "cbNode") return { op: "each", prop: propOf(e.arguments[0]) };
        }
        throw new Error(`unexpected forEachChild construct: ${e.getText(parser)}`);
    }
    function toTest(e) {
        e = unwrap(e);
        if (ts.isBinaryExpression(e) && e.operatorToken.kind === ts.SyntaxKind.EqualsEqualsEqualsToken && ts.isTypeOfExpression(e.left) && ts.isStringLiteral(e.right) && e.right.text === "string")
            return { op: "isString", prop: propOf(e.left.expression) };
        if (ts.isBinaryExpression(e) && e.operatorToken.kind === ts.SyntaxKind.AmpersandAmpersandToken) return { op: "and", left: toTest(e.left), right: toTest(e.right) };
        // node.p.kind === SyntaxKind.X
        if (ts.isBinaryExpression(e) && e.operatorToken.kind === ts.SyntaxKind.EqualsEqualsEqualsToken && ts.isPropertyAccessExpression(e.left) && e.left.name.text === "kind"
            && ts.isPropertyAccessExpression(e.right) && e.right.expression.getText(parser) === "SyntaxKind")
            return { op: "kindIs", prop: propOf(e.left.expression), kind: e.right.name.text };
        // node.p as a truth test: a boolean field, or a node field being present
        if (ts.isPropertyAccessExpression(e)) return { op: "bool", prop: propOf(e) };
        throw new Error(`unexpected test: ${e.getText(parser)}`);
    }
    function bodyOf(init) {
        const fn = ts.isIdentifier(init) ? functions.get(init.text) : init;
        if (!fn || !fn.body) throw new Error(`no function for ${init.getText(parser)}`);
        const statements = fn.body.statements;
        if (statements.length !== 1 || !ts.isReturnStatement(statements[0])) throw new Error(`function ${fn.name?.text ?? "(anonymous)"} is not a single return`);
        const param = fn.parameters[0];
        return { name: fn.name?.text, paramType: param.type?.getText(parser), ir: toIr(statements[0].expression) };
    }
    const children = {};
    for (const p of table.properties) {
        const kind = p.name.expression.name.text; // [SyntaxKind.X]
        children[kind] = bodyOf(p.initializer);
    }

    // --- SyntaxKind -> interface from `readonly kind: SyntaxKind.X` ---------
    const kindToInterfaces = {};
    for (const s of types.statements) {
        if (!ts.isInterfaceDeclaration(s)) continue;
        for (const m of s.members) {
            if (!ts.isPropertySignature(m) || m.name.getText(types) !== "kind" || !m.type) continue;
            const kinds = ts.isUnionTypeNode(m.type) ? m.type.types : [m.type];
            for (const k of kinds) {
                const text = k.getText(types);
                const match = text.match(/^SyntaxKind\.(\w+)$/);
                if (match) (kindToInterfaces[match[1]] ??= []).push(s.name.text);
            }
        }
    }

    // --- SyntaxKind -> interface through the resolved type of `kind` --------
    // Catches interfaces that inherit their kind generically (EndOfFileToken extends Token<SyntaxKind.EndOfFileToken>).
    const syntaxKindName = value => ts.SyntaxKind[value] && Object.entries(ts.SyntaxKind).find(([, v]) => v === value)[0];
    const declaredType = s => checker.getDeclaredTypeOfSymbol(checker.getSymbolAtLocation(s.name));
    // Interfaces and type aliases (EndOfFileToken = Token<SyntaxKind.EndOfFileToken> & JSDocContainer) whose
    // kind resolves to exactly one SyntaxKind member and which are nodes.
    const nodeType = declaredType(types.statements.find(s => ts.isInterfaceDeclaration(s) && s.name.text === "Node"));
    const resolvedKindToInterfaces = {};
    const aliasTypes = {};
    for (const s of types.statements) {
        if (!(ts.isInterfaceDeclaration(s) || ts.isTypeAliasDeclaration(s)) || s.typeParameters?.length) continue;
        const type = declaredType(s);
        if (!type || !checker.isTypeAssignableTo(type, nodeType)) continue;
        const kindProp = checker.getPropertyOfType(type, "kind");
        if (!kindProp) continue;
        const kindType = checker.getTypeOfSymbol(kindProp);
        if (!(kindType.flags & ts.TypeFlags.EnumLiteral) || kindType.flags & ts.TypeFlags.Union || typeof kindType.value !== "number") continue;
        if (checker.getBaseTypeOfLiteralType(kindType).symbol?.name !== "SyntaxKind") continue;
        (resolvedKindToInterfaces[syntaxKindName(kindType.value)] ??= []).push(s.name.text);
        if (ts.isTypeAliasDeclaration(s)) aliasTypes[s.name.text] = type;
    }

    // Class per kind: the interface that declares `kind: SyntaxKind.X` literally (the canonical one; "...Base"
    // interfaces and the JsonMinusNumericLiteral narrowing excluded), else a resolved interface or alias, else Token.
    const kindToClass = {};
    for (const [name, value] of Object.entries(ts.SyntaxKind)) {
        if (typeof value !== "number" || syntaxKindName(value) !== name) continue;
        const direct = (kindToInterfaces[name] ?? []).filter(i => !/Base$/.test(i) && i !== "JsonMinusNumericLiteral");
        const resolved = resolvedKindToInterfaces[name] ?? [];
        kindToClass[name] = direct[0] ?? (resolved.includes(name) ? name : resolved[0]) ?? "Token";
    }

    // --- properties per interface, classified with the type checker -------
    const concreteNames = new Set(Object.values(kindToClass));
    function classify(type) {
        const t = checker.getNonNullableType(type);
        // Declared as `p?: undefined` but assigned anyway (e.g. FunctionTypeNode.modifiers, set through a
        // Mutable<> cast for grammar errors): the emitter decides from the forEachChild usage.
        if (t.flags & ts.TypeFlags.Never) return { k: "undefinedOnly" };
        const parts = t.flags & ts.TypeFlags.Union ? t.types : [t];
        const all = pred => parts.every(pred);
        const symbolName = x => (x.aliasSymbol ?? x.symbol)?.name;
        if (parts.length > 1 && parts.some(p => p.flags & ts.TypeFlags.StringLike) && parts.some(p => symbolName(p) === "NodeArray")) return { k: "stringOrNodes" };
        if (all(p => p.flags & ts.TypeFlags.BooleanLike)) return { k: "bool" };
        if (all(p => p.flags & ts.TypeFlags.EnumLike)) {
            const enumSymbol = parts[0].flags & ts.TypeFlags.EnumLiteral ? checker.getBaseTypeOfLiteralType(parts[0]).symbol : parts[0].symbol;
            return { k: "enum", name: enumSymbol?.name ?? "int" };
        }
        if (all(p => p.flags & ts.TypeFlags.StringLike) || symbolName(t) === "__String") return { k: "string" };
        if (all(p => p.flags & ts.TypeFlags.NumberLike)) return { k: "int" };
        if (symbolName(t) === "NodeArray") {
            const elem = checker.getTypeArguments(t)[0];
            return { k: "nodes", elem: elem && concreteNames.has(symbolName(elem)) ? symbolName(elem) : "Node" };
        }
        if (checker.isArrayType(t)) {
            const elem = checker.getTypeArguments(t)[0];
            if (elem && checker.isTypeAssignableTo(elem, nodeType)) return { k: "array", elem: concreteNames.has(symbolName(elem)) ? symbolName(elem) : "Node" };
            return { k: "object", ts: checker.typeToString(t) };
        }
        if (all(p => checker.isTypeAssignableTo(p, nodeType))) return { k: "node", cls: parts.length === 1 && concreteNames.has(symbolName(t)) ? symbolName(t) : "Node" };
        return { k: "object", ts: checker.typeToString(t) };
    }
    const interfaces = {};
    for (const s of types.statements) {
        if (!ts.isInterfaceDeclaration(s) && !(ts.isTypeAliasDeclaration(s) && aliasTypes[s.name.text])) continue;
        interfaces[s.name.text] = checker.getPropertiesOfType(declaredType(s)).map(p => {
            const decl = p.declarations?.[0];
            const owner = decl && ts.isInterfaceDeclaration(decl.parent) ? decl.parent.name.text : undefined;
            const type = checker.getTypeOfSymbol(p);
            return { name: p.name, type: checker.typeToString(type), cs: classify(type), optional: !!(p.flags & ts.SymbolFlags.Optional), declaredIn: owner };
        });
    }

    const schema = { source: "TypeScript v6.0.3 src/compiler/types.ts + parser.ts forEachChildTable", children, kindToClass, kindToInterfaces, resolvedKindToInterfaces, interfaces };
    writeFileSync(join(repoRoot, "artifacts", "generate", "ast-schema.json"), JSON.stringify(schema, null, 1));
    return schema;
}
