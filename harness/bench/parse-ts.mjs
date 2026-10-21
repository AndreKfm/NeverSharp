// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Parse-only driver for tsc 6.0.3: reads every file of a list and parses it
// with ts.createSourceFile, the way tsc's compiler host does (ts.sys.readFile,
// JSDocParsingMode.ParseForTypeErrors as set by executeCommandLine.ts,
// setParentNodes=false). Single-threaded, as tsc is.
//
// All SourceFiles stay referenced until the end, as a Program keeps them, so
// memory and GC behavior reflect a compiler's parse phase.
//
// With NEVERSHARP_PRIORITY=high the process first raises itself to the high
// priority class; the class in effect is reported.
//
// Prints one JSON line (file count, characters, node count, priority class) so
// the work cannot be optimized away and both drivers can be checked to parse
// the same input.
//
// Usage: node parse-ts.mjs <file list>

import { readFileSync } from "node:fs";
import { constants, getPriority, setPriority } from "node:os";
import ts from "typescript6";

if (process.env.NEVERSHARP_PRIORITY === "high") setPriority(constants.priority.PRIORITY_HIGH);
const priority = { [constants.priority.PRIORITY_HIGH]: "high", [constants.priority.PRIORITY_NORMAL]: "normal" }[getPriority()] ?? "other";

const files = readFileSync(process.argv[2], "utf8").split("\n").filter(Boolean);
const options = { languageVersion: ts.ScriptTarget.Latest, jsDocParsingMode: ts.JSDocParsingMode.ParseForTypeErrors };
// Same mapping as typescript-go's core.GetScriptKindFromFileName (the tuning corpus also has JS and JSON units).
const kinds = {
    ".ts": ts.ScriptKind.TS, ".mts": ts.ScriptKind.TS, ".cts": ts.ScriptKind.TS, ".tsx": ts.ScriptKind.TSX,
    ".js": ts.ScriptKind.JS, ".mjs": ts.ScriptKind.JS, ".cjs": ts.ScriptKind.JS, ".jsx": ts.ScriptKind.JSX, ".json": ts.ScriptKind.JSON,
};

const sourceFiles = [];
let nodes = 0;
let chars = 0;
for (const file of files) {
    const text = ts.sys.readFile(file);
    chars += text.length;
    // tsc's host passes normalized paths (forward slashes); the JSON parser asserts it.
    const fileName = file.replaceAll("\\", "/");
    const sourceFile = ts.createSourceFile(fileName, text, options, /*setParentNodes*/ false, kinds[file.slice(file.lastIndexOf(".")).toLowerCase()]);
    nodes += sourceFile.nodeCount;
    sourceFiles.push(sourceFile);
}
process.stdout.write(JSON.stringify({ driver: "tsc-6.0.3", files: sourceFiles.length, chars, nodes, priority }) + "\n");
