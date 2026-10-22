// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Summarizes a dotnet-trace Speedscope export (dotnet-sampled-thread-time): time spent waiting for
// the GC (stacks containing PollGC) with its nearest caller, and the deepest managed frame of all
// other samples. Usage: node harness/profile/speedscope-summary.mjs <file.speedscope.json>

const d = JSON.parse(readFileSync(process.argv[2], "utf8"));
const frames = d.shared.frames.map(f => f.name.replace(/\(.*$/, "").replace(/^.*?!/, ""));
const pseudo = n => n === "CPU_TIME" || n === "UNMANAGED_CODE_TIME";
const leaf = new Map(), gcCaller = new Map(), callerOf = new Map();
let total = 0, gc = 0;
for (const p of d.profiles) {
  if (p.type !== "evented") continue;
  const stack = []; let last = p.startValue;
  const account = t => {
    const dt = t - last; last = t; if (!stack.length || dt <= 0) return; total += dt;
    const names = stack.map(f => frames[f]).filter(n => !pseudo(n));
    const gi = names.findIndex(n => n.includes("PollGC"));
    if (gi >= 0) { gc += dt; const c = names.slice(0, gi).reverse().find(n => n.startsWith("NeverSharp") || n.startsWith("Program") || n.startsWith("System.IO") || n.includes("List`1") || n.includes("String")) ?? "?"; gcCaller.set(c, (gcCaller.get(c) ?? 0) + dt); return; }
    const l = names[names.length - 1] ?? "?"; leaf.set(l, (leaf.get(l) ?? 0) + dt);
    if (l.includes("AddWithResize") || l.includes("Array.Copy") || l.includes("ReadAllBytes")) { const c = names.slice(0, -1).reverse().find(n => n.startsWith("NeverSharp")) ?? "?"; callerOf.set(l + " <- " + c, (callerOf.get(l + " <- " + c) ?? 0) + dt); }
  };
  for (const e of p.events) { account(e.at); if (e.type === "O") stack.push(e.frame); else stack.pop(); }
}
const pc = t => (100 * t / total).toFixed(1).padStart(5) + " %";
console.log(`GC wait (stacks with PollGC): ${pc(gc)}`);
console.log("\nGC wait by nearest caller:"); [...gcCaller].sort((a, b) => b[1] - a[1]).slice(0, 10).forEach(([n, t]) => console.log(pc(t), n.slice(0, 100)));
console.log("\nSELF (deepest managed frame, outside GC wait):"); [...leaf].sort((a, b) => b[1] - a[1]).slice(0, 25).forEach(([n, t]) => console.log(pc(t), n.slice(0, 100)));
console.log("\nCopy/resize/read callers:"); [...callerOf].sort((a, b) => b[1] - a[1]).slice(0, 10).forEach(([n, t]) => console.log(pc(t), n.slice(0, 140)));
