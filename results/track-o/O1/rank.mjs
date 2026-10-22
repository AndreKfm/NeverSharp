// Ranking of the O1 variants: median, ratio with 95 % bootstrap interval, rig checks, bytes.
import { readFileSync } from "node:fs";
import { median, bootstrapMedianRatio } from "file:///D:/Private/Source/Andre/projects/NeverSharp/harness/bench/stats.mjs";

const root = "D:/Private/Source/Andre/projects/NeverSharp/artifacts/bench/results/";
const runs = Object.fromEntries(process.argv.slice(2).map(a => a.split("=")).map(([v, id]) => [v, JSON.parse(readFileSync(root + id + "/run.json", "utf8"))]));
const times = {};
for (const [v, r] of Object.entries(runs)) {
    const res = r.results[0];
    const t = res.timing.times.map(s => s * 1000);
    times[v] = t;
    const failed = r.environment.checks.filter(c => !c.ok).map(c => c.id);
    console.log(v, "median", median(t).toFixed(1), "ms; compliant", r.protocolCompliant, "idle", r.environment.rig.idleCpuPercent.toFixed(1), "failed checks", failed.join(",") || "none");
    console.log("   ", JSON.stringify(res.runtime), "peakWS MiB", (res.memory.medianPeakWorkingSetBytes / 1048576).toFixed(0));
}
for (const [a, b] of [["B", "A"], ["C", "A"], ["C", "B"]]) {
    const r = bootstrapMedianRatio(times[a], times[b]);
    console.log(`${a}/${b}`, JSON.stringify(r));
}
