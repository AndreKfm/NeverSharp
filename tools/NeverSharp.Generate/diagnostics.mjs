// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): generates the C# diagnostic message table.
//
// Runs TypeScript's own scripts/processDiagnosticMessages.mjs on a copy of
// diagnosticMessages.json, so that property names and keys are exactly
// TypeScript's (convertPropertyName, createKey), then reads the generated
// diagnosticInformationMap.generated.ts with the TypeScript parser and emits
// one C# field per message.

import { execFileSync } from "node:child_process";
import { copyFileSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { csString, header, outDir, repoRoot, ts, tsRoot } from "./common.mjs";

export function generateDiagnostics() {
    const work = join(repoRoot, "artifacts", "generate", "diagnostics");
    mkdirSync(work, { recursive: true });
    copyFileSync(join(tsRoot, "src", "compiler", "diagnosticMessages.json"), join(work, "diagnosticMessages.json"));
    execFileSync(process.execPath, [join(tsRoot, "scripts", "processDiagnosticMessages.mjs"), join(work, "diagnosticMessages.json")], { stdio: "pipe" });

    const text = readFileSync(join(work, "diagnosticInformationMap.generated.ts"), "utf8");
    const sf = ts.createSourceFile("diagnosticInformationMap.generated.ts", text, ts.ScriptTarget.Latest, true);
    const messages = [];
    ts.forEachChild(sf, function visit(n) {
        if (ts.isPropertyAssignment(n) && ts.isCallExpression(n.initializer.expression ?? n.initializer)) {
            const call = ts.isAsExpression(n.initializer) ? n.initializer.expression : n.initializer;
            if (ts.isIdentifier(call.expression) && call.expression.text === "diag") {
                const a = call.arguments;
                const literal = i => a[i] && !(ts.isIdentifier(a[i]) && a[i].text === "undefined") ? a[i] : undefined;
                messages.push({
                    name: n.name.getText(sf),
                    code: Number(a[0].text),
                    category: a[1].name.text,
                    key: a[2].text,
                    message: a[3].text,
                    reportsUnnecessary: literal(4) !== undefined,
                    elided: literal(5)?.kind === ts.SyntaxKind.TrueKeyword,
                    reportsDeprecated: literal(6) !== undefined,
                });
            }
        }
        ts.forEachChild(n, visit);
    });

    const lines = [header("src/compiler/diagnosticMessages.json (via scripts/processDiagnosticMessages.mjs)"),
        "namespace NeverSharp.Generated;",
        "",
        "/// <summary>A diagnostic message of the TypeScript compiler (types.ts: DiagnosticMessage).</summary>",
        "public sealed class DiagnosticMessage(int code, DiagnosticCategory category, string key, string message, bool reportsUnnecessary, bool elidedInCompatibilityPyramid, bool reportsDeprecated)",
        "{",
        "    public int Code { get; } = code;",
        "    public DiagnosticCategory Category { get; } = category;",
        "    public string Key { get; } = key;",
        "    public string Message { get; } = message;",
        "    public bool ReportsUnnecessary { get; } = reportsUnnecessary;",
        "    public bool ElidedInCompatibilityPyramid { get; } = elidedInCompatibilityPyramid;",
        "    public bool ReportsDeprecated { get; } = reportsDeprecated;",
        "}",
        "",
        `/// <summary>All ${messages.length} diagnostic messages, named as in TypeScript's generated Diagnostics table.</summary>`,
        "public static class Diagnostics",
        "{",
    ];
    for (const m of messages) {
        lines.push(`    public static readonly DiagnosticMessage ${m.name} = new(${m.code}, DiagnosticCategory.${m.category}, ${csString(m.key)}, ${csString(m.message)}, ${m.reportsUnnecessary}, ${m.elided}, ${m.reportsDeprecated});`.replace(/, (true|false)(?=[,)])/g, ", $1"));
    }
    lines.push("}", "");
    writeFileSync(join(outDir, "Diagnostics.g.cs"), lines.join("\n").replace(/\bTrue\b/g, "true").replace(/\bFalse\b/g, "false"));
    return messages.length;
}
