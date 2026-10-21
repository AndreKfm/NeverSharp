// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Removes the JSDoc section from a canonical AST dump (format version 1, docs/ast-dump-format.md):
// lines whose node is marked "@" (JSDoc subtrees), the "=HasJSDoc" attribute, and "!jsdocdiag"
// lines. Used by the M4 gate, which compares a parser without the JSDoc parser (M5).

export function stripJsDoc(dump) {
    let out = "";
    for (const line of dump.split("\n")) {
        if (!line) continue;
        if (line.startsWith("!jsdocdiag ")) continue;
        if (line.trimStart().startsWith("@")) continue;
        const space = line.lastIndexOf(" ");
        const tail = line.slice(space + 1);
        if (!line.startsWith("!") && tail.includes("=HasJSDoc")) {
            const flags = tail.split("|").filter(f => f !== "=HasJSDoc");
            out += (flags.length ? line.slice(0, space + 1) + flags.join("|") : line.slice(0, space)) + "\n";
            continue;
        }
        out += line + "\n";
    }
    return out;
}
