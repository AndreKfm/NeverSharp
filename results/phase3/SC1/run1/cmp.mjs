// node cmp.mjs <result.json> <reference-name> : median time (and median GC pause) per variant and the bootstrap median
// ratio of each variant against the reference (harness/bench/stats.mjs).
import { readFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
const st = await import(pathToFileURL("D:/Private/Source/Andre/projects/NeverSharp/harness/bench/stats.mjs").href);
const [file, ref] = process.argv.slice(2);
const r = JSON.parse(readFileSync(file, "utf8")).results;
const base = r.find(x => x.command === ref);
for (const x of r) {
  const p = x.pause.filter(v => v !== null);
  const ratio = x === base ? "" : JSON.stringify(st.bootstrapMedianRatio(x.times, base.times), (k, v) => typeof v === "number" ? +v.toFixed(3) : v);
  console.log(`${x.command.padEnd(22)} ${st.median(x.times).toFixed(3)} s` + (p.length ? `  pause ${(st.median(p) / 1000).toFixed(2)} s` : "") + `  ${ratio}`);
}
