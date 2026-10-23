// Ranking of the O10 and O23 variants: per configuration median, ratio with 95 % bootstrap interval, rig checks, bytes.
// Usage: PAIRS=O10/A,B/A,... node rank.mjs A=<runId> O10=<runId> ...   (run ids under artifacts/bench/results)
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const root = join(import.meta.dirname, "../../..");
const { median, bootstrapMedianRatio } = await import(pathToFileURL(join(root, "harness/bench/stats.mjs")).href);
const runs = Object.fromEntries(process.argv.slice(2).map(a => a.split("=")).map(([v, id]) => [v, JSON.parse(readFileSync(join(root, "artifacts/bench/results", id, "run.json"), "utf8"))]));
for (const config of ["cs-o-aot-ws", "cs-o-aot-ws-single"]) {
    console.log(`== ${config}`);
    const times = {};
    for (const [v, r] of Object.entries(runs)) {
        const res = r.results.find(x => x.config === config);
        times[v] = res.timing.times.map(s => s * 1000);
        const failed = r.environment.checks.filter(c => !c.ok).map(c => c.id);
        console.log(v, "median", median(times[v]).toFixed(1), "ms; compliant", r.protocolCompliant, "idle", r.environment.rig.idleCpuPercent.toFixed(1), "failed checks", failed.join(",") || "none",
            "|", JSON.stringify(res.runtime), "peakWS MiB", (res.memory.medianPeakWorkingSetBytes / 1048576).toFixed(0));
    }
    for (const [a, b] of (process.env.PAIRS ?? "B/A,C/A,C/B").split(",").map(p => p.split("/"))) {
        const r = bootstrapMedianRatio(times[a], times[b]);
        console.log(`${a}/${b} ${r.estimate.toFixed(3)} [${r.low.toFixed(3)}, ${r.high.toFixed(3)}]`);
    }
}
