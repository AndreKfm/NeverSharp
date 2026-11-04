// Interleaved timing: for each round, run every variant once (order rotated per round). Usage:
// node interleave.mjs <out.json> <runs> <warmup> <listfile> <flags...> -- name=exe[@ENV=VAL] ...
import { spawnSync } from "node:child_process";
import { writeFileSync } from "node:fs";
const argv = process.argv.slice(2);
const sep = argv.indexOf("--");
const [out, runsS, warmS, list, ...flags] = argv.slice(0, sep);
const variants = argv.slice(sep + 1).map(s => {
  const [name, rest] = s.split(/=(.*)/s);
  const [exe, envs] = rest.split("@");
  const env = { ...process.env };
  for (const kv of (envs ?? "").split(",").filter(Boolean)) { const [k, v] = kv.split("="); env[k] = v; }
  return { name, exe, env, times: [] };
});
const runOnce = v => {
  const t0 = process.hrtime.bigint();
  const r = spawnSync(v.exe, [...flags, list], { env: v.env, stdio: ["ignore", "ignore", "pipe"] });
  const t = Number(process.hrtime.bigint() - t0) / 1e9;
  if (r.status !== 0) throw new Error(`${v.name} exit ${r.status}: ${r.stderr}`);
  return t;
};
for (let w = 0; w < +warmS; w++) for (const v of variants) runOnce(v);
for (let i = 0; i < +runsS; i++) {
  const order = variants.map((_, k) => variants[(k + i) % variants.length]);
  for (const v of order) v.times.push(runOnce(v));
  console.log(`round ${i + 1}: ` + variants.map(v => `${v.name} ${v.times.at(-1).toFixed(3)}`).join("  "));
}
writeFileSync(out, JSON.stringify({ flags, list, results: variants.map(v => ({ command: v.name, times: v.times })) }, null, 1));
