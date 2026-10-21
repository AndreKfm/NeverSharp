// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Benchmark runner. For every (repository, configuration) pair:
//   1. one validation run: the driver's output must report the expected file count;
//   2. wall-clock via hyperfine (--shell=none, warmup, N runs, JSON export);
//   3. a separate memory pass under peakmem (not timed together with step 2).
// Writes artifacts/bench/results/<runId>/run.json (schema: result.schema.json)
// plus the raw hyperfine exports. A run whose rig checks fail is still written
// but marked protocolCompliant=false; the report labels it as a probe.
//
// Usage: node bench.mjs [--repos a,b] [--configs x,y] [--runs 20] [--warmup 3]
//                       [--mem-runs 5] [--priority high|normal] [--corpus benchmark|tuning] [--label text]
//
// --priority high (default): every driver raises itself to the high priority
// class at start (NEVERSHARP_PRIORITY=high) and reports the class in effect;
// the runner rejects a driver whose reported class differs.

import { execFileSync, spawnSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join, relative } from "node:path";
import { benchDir, exe, pins, repoRoot } from "./common.mjs";
import { captureEnvironment } from "./env.mjs";
import { mad, median } from "./stats.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const tools = join(repoRoot, ".tools");
const bin = join(repoRoot, "artifacts", "bin");
const node = join(tools, `node-${pins.tools.node.version}`, "node" + exe);
const hyperfine = join(tools, `hyperfine-${pins.tools.hyperfine.version}`, "hyperfine" + exe);
const peakmem = join(bin, "peakmem", "peakmem" + exe);
const parseGo = join(bin, "parse-go" + exe);
const here = import.meta.dirname;

// Configurations of the parse-only suite. Every configuration is reported; none is dropped after the fact.
const configurations = [
    // threads: "single" configurations form the rig check of the variance gate (decision B11).
    { id: "tsc6-parse", label: "tsc 6.0.3 (Node 24), single-threaded", threads: "single", command: list => [node, join(here, "parse-ts.mjs"), list] },
    { id: "tsgo7-parse", label: "typescript-go 7.0.2, parallel (default)", threads: "parallel", command: list => [parseGo, list] },
    { id: "tsgo7-parse-single", label: "typescript-go 7.0.2, single-threaded", threads: "single", command: list => [parseGo, "--single", list] },
];

const runs = Number(arg("--runs", "20"));
const warmup = Number(arg("--warmup", "3"));
const memRuns = Number(arg("--mem-runs", "5"));
const repoFilter = arg("--repos", undefined)?.split(",");
const configFilter = arg("--configs", undefined)?.split(",");
const label = arg("--label", "");
const priority = arg("--priority", "high");
if (priority !== "high" && priority !== "normal") throw new Error("--priority must be high or normal");
const driverEnv = { ...process.env, NEVERSHARP_PRIORITY: priority };

// --corpus benchmark (default): Microsoft's five repositories; --corpus tuning: the tuning corpus (pre-registration §3).
const corpusName = arg("--corpus", "benchmark");
if (corpusName !== "benchmark" && corpusName !== "tuning") throw new Error("--corpus must be benchmark or tuning");
const fileManifest = JSON.parse(readFileSync(join(benchDir, "filelists", corpusName === "tuning" ? "tuning-manifest.json" : "manifest.json"), "utf8"));
const repos = (corpusName === "tuning" ? ["tuning"] : pins.benchmarkCorpus.repos.map(r => r.name)).filter(r => !repoFilter || repoFilter.includes(r));
const configs = configurations.filter(c => !configFilter || configFilter.includes(c.id));

// hyperfine --shell=none splits the command line with POSIX shell-word rules, where a
// backslash is an escape character: pass paths with forward slashes (valid on Windows).
const quote = s => {
    const v = s.replaceAll("\\", "/");
    return /[\s"']/.test(v) ? `'${v}'` : v;
};
const version = (cmd, args) => execFileSync(cmd, args, { encoding: "utf8" }).trim().split("\n")[0];
const toolchain = {
    node: version(node, ["--version"]),
    hyperfine: version(hyperfine, ["--version"]),
    goToolset: execFileSync(join(tools, `go-${pins.tools.go.version}`, "go", "bin", "go" + exe), ["version", "-m", parseGo], { encoding: "utf8" }).match(/microsoft_toolset_version=(\S+)/)?.[1] ?? "unknown",
    dotnetSdk: version("dotnet", ["--version"]),
    typescript6: JSON.parse(readFileSync(join(here, "node_modules", "typescript6", "package.json"), "utf8")).version,
    typescript7: JSON.parse(readFileSync(join(here, "node_modules", "typescript7", "package.json"), "utf8")).version,
};

const startedAt = new Date();
const runId = startedAt.toISOString().replace(/[:.]/g, "-");
const outDir = join(benchDir, "results", runId);
mkdirSync(outDir, { recursive: true });

console.log("capturing environment and checking rig ...");
const environment = await captureEnvironment(toolchain);
for (const c of environment.checks) console.log(`  ${c.ok ? "ok  " : "FAIL"} ${c.id}: ${c.value} (required: ${c.required})`);
if (!environment.protocolCompliant) console.log("  -> rig checks failed: results are recorded as a PROBE, not as protocol results");

// Validation, wall-clock and memory for one (file list, configuration) pair.
function measure(name, list, expectedFiles, config) {
    const command = config.command(list);
    // 1. validation
    const check = spawnSync(command[0], command.slice(1), { encoding: "utf8", env: driverEnv });
    if (check.status !== 0) throw new Error(`${config.id} on ${name} failed: ${check.stderr}`);
    const output = JSON.parse(check.stdout.trim().split("\n").pop());
    if (output.files !== expectedFiles) throw new Error(`${config.id} on ${name}: parsed ${output.files} files, expected ${expectedFiles}`);
    if (output.priority !== priority) throw new Error(`${config.id} on ${name}: driver reports priority class ${output.priority}, expected ${priority}`);

    // 2. wall-clock
    const exportFile = join(outDir, `hyperfine-${name}-${config.id}.json`);
    console.log(`\n== ${name} / ${config.id}`);
    execFileSync(hyperfine, ["--shell=none", "--warmup", String(warmup), "--runs", String(runs), "--export-json", exportFile,
        "--command-name", `${name}/${config.id}`, command.map(quote).join(" ")], { stdio: "inherit", env: driverEnv });
    const hf = JSON.parse(readFileSync(exportFile, "utf8")).results[0];
    const times = hf.times;
    const med = median(times);

    // 3. memory
    const peaks = [];
    for (let i = 0; i < memRuns; i++) {
        const r = spawnSync(peakmem, command, { encoding: "utf8", env: driverEnv });
        if (r.status !== 0) throw new Error(`peakmem ${config.id} on ${name} failed: ${r.stderr}`);
        peaks.push(JSON.parse(r.stderr.trim().split("\n").pop()));
    }

    return {
        config: config.id,
        configLabel: config.label,
        command: command.map(c => c.startsWith(repoRoot) ? relative(repoRoot, c).split("\\").join("/") : c),
        output,
        timing: {
            unit: "s",
            times,
            median: med,
            mad: mad(times),
            madOverMedianPercent: 100 * mad(times) / med,
            mean: hf.mean,
            stddev: hf.stddev,
            min: hf.min,
            max: hf.max,
            userMean: hf.user,
            systemMean: hf.system,
        },
        memory: {
            runs: memRuns,
            peakWorkingSetBytes: peaks.map(p => p.peakWorkingSetBytes),
            peakCommitBytes: peaks.map(p => p.peakCommitBytes),
            medianPeakWorkingSetBytes: median(peaks.map(p => p.peakWorkingSetBytes)),
            medianPeakCommitBytes: median(peaks.map(p => p.peakCommitBytes)),
        },
        // C# configurations report their own GC metrics (driver contract, docs/benchmark-harness.md §4.1).
        runtime: output.allocatedBytes === undefined ? null : {
            allocatedBytes: output.allocatedBytes,
            gen0: output.gen0,
            gen1: output.gen1,
            gen2: output.gen2,
            pauseMs: output.gcPauseMs,
        },
        hyperfineExport: relative(outDir, exportFile),
    };
}

const results = [];
for (const repo of repos) {
    const corpus = fileManifest.repos[repo];
    for (const config of configs) {
        const m = measure(repo, join(benchDir, "filelists", `${repo}.txt`), corpus.files, config);
        results.push({
            repo,
            kind: "parse",
            ...m,
            throughput: { filesPerSecond: corpus.files / m.timing.median, megabytesPerSecond: corpus.bytes / 1e6 / m.timing.median },
        });
    }
}

// Fixed cost (A2): every configuration on an empty file list: process start-up,
// runtime initialization and driver overhead. Reported next to the results, never subtracted.
const fixedCost = configs.map(config => measure("fixed-cost", join(benchDir, "filelists", "empty.txt"), 0, config));

const run = {
    schemaVersion: 1,
    runId,
    label,
    startedAt: startedAt.toISOString(),
    finishedAt: new Date().toISOString(),
    protocolCompliant: environment.protocolCompliant,
    suite: corpusName === "tuning" ? "parse-tuning" : "parse",
    settings: { runs, warmup, memRuns, hyperfineShell: "none", processPriority: priority },
    environment,
    corpus: Object.fromEntries(repos.map(r => [r, { commit: fileManifest.repos[r].commit, files: fileManifest.repos[r].files, bytes: fileManifest.repos[r].bytes, contentSha256: fileManifest.repos[r].contentSha256 }])),
    configurations: configs.map(c => ({ id: c.id, label: c.label, threads: c.threads })),
    results,
    fixedCost,
};
writeFileSync(join(outDir, "run.json"), JSON.stringify(run, null, 1));
console.log(`\nwritten ${join(outDir, "run.json")}`);
