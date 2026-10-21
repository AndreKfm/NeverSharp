// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Canonical AST dump text format, version 1. The Go dumper (go/main.go) writes
// the same format without the JSDoc section; see docs/ast-dump-format.md.
//
//   <indent><Kind> <pos> <end>[ <Flag>|<Flag>...]     one line per node, pre-order,
//                                                     two spaces per depth, flags sorted
//   <indent>@<Kind> <pos> <end>[ <Flags>]             JSDoc section: after a node's line, each
//                                                     comment of its jsDoc array as a subtree,
//                                                     then the node's regular children
//   !diag <code> <start> <length>                     parse diagnostics, in emission order
//   !jsdiag <code> <start> <length>                   JS-only syntactic diagnostics
//   !jsdocdiag <code> <start> <length>                JSDoc parse diagnostics
//
// Positions are UTF-16 code unit offsets.

export function renderDump(nodes, diagnostics) {
    let out = "";
    for (const n of nodes) {
        out += "  ".repeat(n.depth) + (n.jsdoc ? "@" : "") + n.kind + " " + n.pos + " " + n.end;
        if (n.flags.length) out += " " + [...n.flags].sort().join("|");
        out += "\n";
    }
    for (const section of ["diag", "jsdiag", "jsdocdiag"]) {
        for (const d of diagnostics[section]) out += `!${section} ${d.code} ${d.start} ${d.length}\n`;
    }
    return out;
}
