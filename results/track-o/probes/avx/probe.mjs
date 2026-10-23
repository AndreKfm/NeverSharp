// Development probe, not a protocol result: Track O built with NativeAOT's default instruction set (x86-64-v2)
// against the same code built with IlcInstructionSet=x86-64-v3 (AVX2, BMI, FMA allowed without runtime checks).
// hyperfine as bench.mjs runs it (--shell=none, 3 warmups, high priority); each pair is measured twice in
// opposite order; ratios v3/v2 of the pooled medians with the 95 % bootstrap of stats.mjs.
// Usage: node probe.mjs <dir with v2/ and v3/ driver builds>
import { execFileSync } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const root = join(import.meta.dirname, "../../../..");
const { median, bootstrapMedianRatio } = await import(pathToFileURL(join(root, "harness/bench/stats.mjs")).href);
const hyperfine = join(root, ".tools/hyperfine-1.20.0/hyperfine.exe");
const builds = process.argv[2];
const driver = v => join(builds, v, "parse-cs-optimized-aot-ws.exe").replaceAll("\\", "/");
const out = import.meta.dirname;
const cases = [
    { name: "tuning-parallel", list: "tuning", single: false, runs: 20 },
    { name: "tuning-single", list: "tuning", single: true, runs: 10 },
    { name: "vscode-parallel", list: "vscode", single: false, runs: 20 },
    { name: "vscode-single", list: "vscode", single: true, runs: 10 },
];
const result = {};
for (const c of cases) {
    const list = join(root, "artifacts/bench/filelists", `${c.list}.txt`).replaceAll("\\", "/");
    const cmd = v => `${driver(v)}${c.single ? " --single" : ""} ${list}`;
    const times = { v2: [], v3: [] };
    for (const [i, order] of [["v2", "v3"], ["v3", "v2"]].entries()) {
        const file = join(out, `hyperfine-${c.name}-${i + 1}.json`);
        execFileSync(hyperfine, ["--shell=none", "--warmup", "3", "--runs", String(c.runs), "--export-json", file, ...order.map(cmd)],
            { stdio: ["ignore", "ignore", "inherit"], env: { ...process.env, NEVERSHARP_PRIORITY: "high" } });
        JSON.parse(readFileSync(file, "utf8")).results.forEach((r, k) => times[order[k]].push(...r.times));
    }
    const r = bootstrapMedianRatio(times.v3, times.v2);
    result[c.name] = { v2: median(times.v2), v3: median(times.v3), ...r };
    console.log(c.name.padEnd(16), "v2", (1000 * median(times.v2)).toFixed(1), "ms  v3", (1000 * median(times.v3)).toFixed(1), "ms  v3/v2", `${r.estimate.toFixed(3)} [${r.low.toFixed(3)}, ${r.high.toFixed(3)}]`);
}
writeFileSync(join(out, "probe.json"), JSON.stringify({ date: new Date().toISOString(), result }, null, 1));
