// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): generates the C# enums of types.ts and corePublic.ts.
//
// Which enums: those the Phase 1 slice reaches (harness/analysis/slice.mjs) in
// types.ts and corePublic.ts. Enums local to a module (parser.ts, scanner.ts,
// utilities.ts, nodeFactory.ts) are ported with their module. Values come from
// the TypeScript type checker (getConstantValue), so computed members
// (e.g. FirstAssignment = EqualsToken) get TypeScript's exact values. Member
// order and aliases are kept as declared. String enums become static classes
// with string constants.

import { execFileSync } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { csString, header, outDir, repoRoot, ts, tsRoot } from "./common.mjs";

export function generateEnums() {
    const sliceFile = join(repoRoot, "artifacts", "generate", "slice.json");
    execFileSync(process.execPath, [join(repoRoot, "harness", "analysis", "slice.mjs"), "--json", sliceFile], { stdio: "pipe" });
    const slice = JSON.parse(readFileSync(sliceFile, "utf8"));
    const wanted = new Map(); // file -> Set(enum name)
    for (const u of slice.units) {
        if (u.kind !== "enum" || (u.file !== "types.ts" && u.file !== "corePublic.ts")) continue;
        if (!wanted.has(u.file)) wanted.set(u.file, new Set());
        wanted.get(u.file).add(u.name);
    }

    const files = [...wanted.keys()].map(f => join(tsRoot, "src", "compiler", f));
    const program = ts.createProgram(files, { target: ts.ScriptTarget.ES2022, noEmit: true, types: [], skipLibCheck: true });
    const checker = program.getTypeChecker();
    const emitted = [];
    for (const [file, names] of wanted) {
        const sf = program.getSourceFiles().find(s => s.fileName.endsWith(`/compiler/${file}`));
        for (const statement of sf.statements) {
            if (!ts.isEnumDeclaration(statement) || !names.has(statement.name.text)) continue;
            const members = statement.members.map(m => ({ name: m.name.getText(sf), value: checker.getConstantValue(m) }));
            if (members.some(m => m.value === undefined)) throw new Error(`${statement.name.text}: member without constant value`);
            emitted.push({ file, name: statement.name.text, members });
        }
    }
    emitted.sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0);

    const lines = [header("src/compiler/types.ts and src/compiler/corePublic.ts (enum declarations)"), "namespace NeverSharp.Generated;", ""];
    for (const e of emitted) {
        lines.push(`// ${e.file}: enum ${e.name}`);
        if (e.members.some(m => typeof m.value === "string")) {
            lines.push(`public static class ${e.name}`, "{");
            for (const m of e.members) lines.push(`    public const string ${m.name} = ${csString(m.value)};`);
            lines.push("}", "");
            continue;
        }
        const values = e.members.map(m => m.value);
        const underlying = values.every(v => v >= -2147483648 && v <= 2147483647) ? "" : " : long";
        if (/Flags$|Kinds$/.test(e.name)) lines.push("[System.Flags]");
        // Every member keeps TypeScript's name and value; aliases (same value, later name) stay as declared.
        lines.push("#pragma warning disable CA1069 // aliases with the same value are part of TypeScript's enum", `public enum ${e.name}${underlying}`, "{");
        // C# identifiers cannot contain "$" (CharacterCodes.$): mapped to "Dollar", the only renaming rule.
        for (const m of e.members) lines.push(`    ${m.name.replaceAll("$", "Dollar")} = ${m.value},${m.name.includes("$") ? ` // TypeScript: ${m.name}` : ""}`);
        lines.push("}", "#pragma warning restore CA1069", "");
    }
    writeFileSync(join(outDir, "Enums.g.cs"), lines.join("\n"));

    // Name tables for the dump writers (tools/NeverSharp.Dump): the first declared name per value, as the
    // tsc dumpers pick them from the enum object (harness/astdump/dump-ts.mjs, dump-tokens-ts.mjs).
    const firstNames = (e, singleBitOnly) => {
        const names = new Map();
        for (const m of e.members) {
            if (names.has(m.value) || singleBitOnly && (m.value === 0 || (m.value & (m.value - 1)) !== 0)) continue;
            names.set(m.value, m.name);
        }
        return [...names].sort((a, b) => a[0] - b[0]);
    };
    const byName = Object.fromEntries(emitted.map(e => [e.name, e]));
    const table = (name, singleBitOnly) => [`    public static readonly (int Value, string Name)[] ${name} =`, "    [",
        ...firstNames(byName[name], singleBitOnly).map(([v, n]) => `        (${v}, ${csString(n)}),`), "    ];"];
    const names = [header("src/compiler/types.ts (enum declarations)"), "namespace NeverSharp.Dump;", "",
        "/// <summary>First declared name per value (single-bit members only for flag enums), as the tsc dumpers choose them.</summary>",
        "internal static class EnumNames", "{",
        ...table("SyntaxKind", false), "",
        ...table("TokenFlags", true), "",
        ...table("NodeFlags", true), "}", ""];
    writeFileSync(join(repoRoot, "tools", "NeverSharp.Dump", "EnumNames.g.cs"), names.join("\n"));
    return emitted.length;
}
