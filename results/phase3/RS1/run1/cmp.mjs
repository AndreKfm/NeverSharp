import { readFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
const st = await import(pathToFileURL(process.cwd() + "/harness/bench/stats.mjs").href);
for (const f of process.argv.slice(2)) {
  const r = JSON.parse(readFileSync(f, "utf8")).results;
  const g = n => r.find(x => x.command === n).times;
  console.log(f.split("/").pop(), st.median(g("base")).toFixed(3), st.median(g("final")).toFixed(3), JSON.stringify(st.bootstrapMedianRatio(g("final"), g("base"))));
}
