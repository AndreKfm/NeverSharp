// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Hot paths of a dotnet-trace Speedscope export (dotnet-sampled-thread-time): self time per frame
// (deepest frame, runtime frames attributed to their nearest NeverSharp caller as well), inclusive time
// per NeverSharp frame, and the number of samples. Usage:
//   node harness/profile/hot-paths.mjs <file.speedscope.json> [top=40]

import { readFileSync } from "node:fs";

const d = JSON.parse(readFileSync(process.argv[2], "utf8"));
const top = Number(process.argv[3] ?? 40);
const frames = d.shared.frames.map(f => f.name.replace(/\(.*$/, "").replace(/^.*?!/, "").replace(/^NeverSharp\.Optimized\./, ""));
const ours = n => !n.includes("System.") && !n.includes("Microsoft.") && !n.includes("Interop") && n !== "?" && !n.startsWith("Program") && !n.includes("CPU_TIME") && !n.includes("UNMANAGED_CODE_TIME");
const self = new Map(), selfOurs = new Map(), inclusive = new Map();
let total = 0, samples = 0;
const add = (m, k, v) => m.set(k, (m.get(k) ?? 0) + v);
for (const p of d.profiles) {
    if (p.type !== "evented") continue;
    const stack = []; let last = p.startValue;
    const account = t => {
        const dt = t - last; last = t; if (!stack.length || dt <= 0) return;
        total += dt; samples++;
        const names = stack.map(f => frames[f]).filter(n => n !== "CPU_TIME" && n !== "UNMANAGED_CODE_TIME");
        add(self, names[names.length - 1] ?? "?", dt);
        const nearest = [...names].reverse().find(ours) ?? "(runtime only)";
        add(selfOurs, nearest, dt);
        for (const n of new Set(names.filter(ours))) add(inclusive, n, dt);
    };
    for (const e of p.events) { account(e.at); if (e.type === "O") stack.push(e.frame); else stack.pop(); }
}
const pct = v => (100 * v / total).toFixed(1).padStart(5) + " %";
const show = (title, m) => { console.log(`\n${title}`); for (const [k, v] of [...m].sort((a, b) => b[1] - a[1]).slice(0, top)) console.log(pct(v), k); };
console.log(`samples: ${samples}, total ${(total / 1000).toFixed(1)} s (unit as exported)`);
show("SELF (deepest frame)", self);
show("SELF attributed to nearest NeverSharp frame", selfOurs);
show("INCLUSIVE (NeverSharp frames)", inclusive);
