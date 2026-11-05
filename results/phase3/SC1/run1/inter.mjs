// Interleaved timing with per-variant flags (after results/phase4/EO1/run1/follow-up/interleave.mjs). Usage:
// node inter.mjs <out.json> <runs> <warmup> <listfile> <variants.json>
// variants.json: [{ "name": "...", "exe": "...", "flags": ["--check", ...], "env": { "K": "V" } }, ...]
// The list file is appended to each variant's flags. Records wall times and, if stdout is a JSON line, gcPauseMs.
import { spawnSync } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
const [out, runsS, warmS, list, vfile] = process.argv.slice(2);
const variants = JSON.parse(readFileSync(vfile, "utf8")).map(v => ({ ...v, env: { ...process.env, ...(v.env ?? {}) }, times: [], pause: [], stats: [] }));
const runOnce = v => {
  const t0 = process.hrtime.bigint();
  const r = spawnSync(v.exe, [...v.flags, list], { env: v.env, stdio: ["ignore", "pipe", "pipe"], maxBuffer: 1 << 26 });
  const t = Number(process.hrtime.bigint() - t0) / 1e9;
  if (r.status !== 0) throw new Error(`${v.name} exit ${r.status}: ${r.stderr}`);
  let pause = null;
  try { pause = JSON.parse(r.stdout.toString().trim().split("\n").at(-1)).gcPauseMs ?? null; } catch { }
  return { t, pause, stderr: r.stderr.toString() };
};
for (let w = 0; w < +warmS; w++) for (const v of variants) runOnce(v);
for (let i = 0; i < +runsS; i++) {
  const order = variants.map((_, k) => variants[(k + i) % variants.length]);
  for (const v of order) { const r = runOnce(v); v.times.push(r.t); v.pause.push(r.pause); if (v.keepStderr) v.stats.push(r.stderr); }
  console.log(`round ${i + 1}: ` + variants.map(v => `${v.name} ${v.times.at(-1).toFixed(3)}`).join("  "));
}
writeFileSync(out, JSON.stringify({ list, results: variants.map(v => ({ command: v.name, flags: v.flags, env: v.env === undefined ? undefined : Object.fromEntries(Object.entries(v.env).filter(([k]) => k.startsWith("NEVERSHARP") || k.startsWith("TSRS"))), times: v.times, pause: v.pause, stats: v.stats })) }, null, 1));
