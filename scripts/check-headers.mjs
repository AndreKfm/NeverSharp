// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Gate 2, port rule 5: every C# file under src/ starts with the copyright and
// SPDX lines and either names its upstream source ("// Ported from <path>" plus
// the TypeScript commit), says it is generated from upstream data
// ("// Generated from <path>" plus the commit), or states that it is original code
// ("// Original code (not ported): <reason>"). See CONTRIBUTING.md.
//
// Usage: node scripts/check-headers.mjs   (exit code 1 on any violation)

import { readdirSync, readFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const pins = JSON.parse(readFileSync(join(root, "pins.json"), "utf8"));
const tsCommit = pins.references.typescript.commit;

function* walk(dir) {
    for (const e of readdirSync(dir, { withFileTypes: true })) {
        if (e.name === "bin" || e.name === "obj") continue;
        const full = join(dir, e.name);
        if (e.isDirectory()) yield* walk(full);
        else if (e.name.endsWith(".cs")) yield full;
    }
}

const problems = [];
let files = 0;
for (const file of walk(join(root, "src"))) {
    files++;
    const rel = relative(root, file).split("\\").join("/");
    const head = readFileSync(file, "utf8").replace(/\r\n/g, "\n").split("\n").slice(0, 12);
    if (!/^\/\/ Copyright \d{4} Andre Kaufmann$/.test(head[0] ?? "")) problems.push(`${rel}: line 1 must be the copyright line`);
    const spdx = head.findIndex(l => l === "// SPDX-License-Identifier: Apache-2.0");
    if (spdx < 0) problems.push(`${rel}: missing "// SPDX-License-Identifier: Apache-2.0"`);
    const ported = head.some(l => l.startsWith("// Ported from "));
    const generated = head.some(l => l.startsWith("// Generated from "));
    const original = head.some(l => /^\/\/ Original code \(not ported\): \S/.test(l));
    if ([ported, generated, original].filter(Boolean).length !== 1) problems.push(`${rel}: needs exactly one of "// Ported from ...", "// Generated from ..." or "// Original code (not ported): <reason>"`);
    if (ported || generated) {
        if (!head.some(l => l.includes("Portions Copyright (c) Microsoft Corporation."))) problems.push(`${rel}: ported file without "Portions Copyright (c) Microsoft Corporation."`);
        if (!head.some(l => l.includes(tsCommit))) problems.push(`${rel}: ported file does not name the pinned TypeScript commit ${tsCommit}`);
        if (!head.some(l => l.startsWith("// Modified: "))) problems.push(`${rel}: ported file without a "// Modified:" change notice (Apache-2.0 §4(b))`);
    }
}
for (const p of problems) console.log(p);
console.log(`${files} files checked, ${problems.length} problems`);
process.exitCode = problems.length ? 1 : 0;
