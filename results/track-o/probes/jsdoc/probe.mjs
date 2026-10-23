// Development probe, not a protocol result: Track O (cs-o-aot-ws) with JSDocParsingMode.ParseAll
// (NEVERSHARP_JSDOC=all) against its default (ParseForTypeErrors, as tsc's host) and tsgo7-parse,
// on the five benchmark repositories; hyperfine as bench.mjs runs it (--shell=none, 3 warmups, 20 runs,
// NEVERSHARP_PRIORITY=high). Ratios: geometric mean of median ratios, 95 % bootstrap (stats.mjs).
// Usage: node probe.mjs   (writes hyperfine exports and probe.json next to this script)
import { execFileSync } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const root = join(import.meta.dirname, "../../../..");
const { bootstrapGeometricMeanRatio } = await import(pathToFileURL(join(root, "harness/bench/stats.mjs")).href);
const hyperfine = join(root, ".tools/hyperfine-1.20.0/hyperfine.exe");
const csO = join(root, "artifacts/bin/cs/optimized-aot-ws/parse-cs-optimized-aot-ws.exe");
const go = join(root, "artifacts/bin/parse-go.exe");
const repos = ["vscode", "sentry", "bluesky", "playwright", "tldraw"];
const out = import.meta.dirname;

const run = (name, commands, env) => {
    const file = join(out, `hyperfine-${name}.json`);
    execFileSync(hyperfine, ["--shell=none", "--warmup", "3", "--runs", "20", "--export-json", file, ...commands.map(c => c.replaceAll("\\", "/"))],
        { stdio: ["ignore", "ignore", "inherit"], env: { ...process.env, NEVERSHARP_PRIORITY: "high", ...env } });
    return JSON.parse(readFileSync(file, "utf8")).results.map(r => r.times);
};

const times = {};
for (const repo of repos) {
    const list = join(root, "artifacts/bench/filelists", `${repo}.txt`);
    const [all] = run(`${repo}-cs-o-jsdoc-all`, [`${csO} ${list}`], { NEVERSHARP_JSDOC: "all" });
    const [def, tsgo] = run(`${repo}-cs-o-default-tsgo`, [`${csO} ${list}`, `${go} ${list}`], {});
    times[repo] = { all, def, tsgo };
    console.log(repo, "median s: all", median(all).toFixed(4), "default", median(def).toFixed(4), "tsgo", median(tsgo).toFixed(4));
}
function median(v) { const s = [...v].sort((a, b) => a - b); const m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; }

const result = {};
for (const [name, a, b] of [["all/tsgo", "all", "tsgo"], ["default/tsgo", "def", "tsgo"], ["all/default", "all", "def"]]) {
    result[name] = bootstrapGeometricMeanRatio(repos.map(r => [times[r][a], times[r][b]]));
    console.log(name, `${result[name].estimate.toFixed(3)} [${result[name].low.toFixed(3)}, ${result[name].high.toFixed(3)}]`);
}
writeFileSync(join(out, "probe.json"), JSON.stringify({ date: new Date().toISOString(), result }, null, 1));
