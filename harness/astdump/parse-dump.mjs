// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Parser for the canonical dump format written by format.mjs and go/main.go.

export function parseDump(text) {
    const nodes = [];
    const diags = [];
    let panic;
    for (const line of text.split("\n")) {
        if (line === "") continue;
        if (line.startsWith("!panic")) { panic = line; continue; }
        if (line.startsWith("!")) {
            const [section, code, start, length] = line.slice(1).split(" ");
            diags.push({ section, code: Number(code), start: Number(start), length: Number(length) });
            continue;
        }
        const indent = line.length - line.trimStart().length;
        const [kind, pos, end, flags] = line.trimStart().split(" ");
        nodes.push({ depth: indent / 2, kind, pos: Number(pos), end: Number(end), flags: flags ? flags.split("|") : [] });
    }
    return { nodes, diags, panic };
}

export function renderParsed({ nodes, diags }) {
    let out = "";
    for (const n of nodes) {
        out += "  ".repeat(n.depth) + n.kind + " " + n.pos + " " + n.end;
        if (n.flags.length) out += " " + [...n.flags].sort().join("|");
        out += "\n";
    }
    for (const d of diags) out += `!${d.section} ${d.code} ${d.start} ${d.length}\n`;
    return out;
}
