// Secondary single-threaded ratios of the Track O endpoint run (pre-registration §8, secondary endpoints):
// geometric mean over the five repositories with the 95 % percentile bootstrap of stats.mjs.
// Usage: node single.mjs <run.json>
import { readFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
import { join, dirname } from "node:path";

const { bootstrapGeometricMeanRatio } = await import(pathToFileURL(join(import.meta.dirname, "../../../harness/bench/stats.mjs")).href);
const run = JSON.parse(readFileSync(process.argv[2], "utf8"));
const times = (repo, config) => run.results.find(r => r.repo === repo && r.config === config).timing.times;
const repos = [...new Set(run.results.filter(r => r.kind !== "fixed-cost").map(r => r.repo))];
for (const [a, b] of [["cs-o-aot-ws-single", "tsgo7-parse-single"], ["cs-o-aot-ws-single", "tsrs-parse-single"], ["cs-o-aot-ws", "cs-aot-ws"]]) {
    const r = bootstrapGeometricMeanRatio(repos.map(repo => [times(repo, a), times(repo, b)]));
    console.log(`${a} / ${b}: ${r.estimate.toFixed(3)} [${r.low.toFixed(3)}, ${r.high.toFixed(3)}]`);
}
