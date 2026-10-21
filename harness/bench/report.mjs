// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Generates the protocol's result tables (Markdown) from a run.json.
//
// Usage: node report.mjs <run.json> [--out <file.md>]
//        (default output: report.md next to run.json)

import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { bootstrapGeometricMeanRatio, bootstrapMedianRatio, median } from "./stats.mjs";

const file = process.argv[2];
const out = process.argv.includes("--out") ? process.argv[process.argv.indexOf("--out") + 1] : join(dirname(file), "report.md");
const run = JSON.parse(readFileSync(file, "utf8"));

const BASELINE = "tsgo7-parse";
const VARIANCE_LIMIT = 3; // percent, MAD/median (kill criterion: fix the rig above this)
const mib = b => (b / 1048576).toFixed(0);
const s = v => v >= 10 ? v.toFixed(2) : v >= 1 ? v.toFixed(3) : v.toFixed(4);
const pct = v => v.toFixed(2) + " %";
const row = cells => "| " + cells.join(" | ") + " |";
const lines = [];
const push = (...l) => lines.push(...l);

const repos = Object.keys(run.corpus);
const configs = run.configurations;
const result = (repo, config) => run.results.find(r => r.repo === repo && r.config === config);
const env = run.environment;

push(`# Parse-only benchmark, run ${run.runId}`, "");
if (run.protocolCompliant) push("**Status: protocol result.** All rig checks passed.", "");
else push(`**Status: PROBE, not a protocol result.** Failed rig checks: ${env.checks.filter(c => !c.ok).map(c => `\`${c.id}\` (${c.value})`).join(", ")}.`, "");
if (run.label) push(`Label: ${run.label}`, "");

push("## Environment", "",
    row(["", ""]), row(["---", "---"]),
    row(["Machine", `${env.machine.cpu}, ${env.machine.cores} cores / ${env.machine.logicalProcessors} threads, ${(env.machine.ramBytes / 2 ** 30).toFixed(0)} GiB, ${env.machine.board}, BIOS ${env.machine.bios}`]),
    row(["OS", `${env.machine.os} ${env.machine.osVersion}`]),
    row(["Rig", `power scheme ${env.rig.scheme}, min/max processor state ${env.rig.minProcessorStateAc}/${env.rig.maxProcessorStateAc} %, processor performance under all-core load ${env.rig.processorPerformanceUnderLoadPercent} % of ${env.machine.nominalMHz} MHz, idle before run ${env.rig.idleCpuPercent.toFixed(1)} %${env.rig.idleCpuSamplesPercent ? ` (median of 1 s windows: ${env.rig.idleCpuSamplesPercent.join(", ")})` : ""}`]),
    row(["Toolchain", `Node ${env.toolchain.node}, typescript ${env.toolchain.typescript6} / ${env.toolchain.typescript7}, Go ${env.toolchain.goToolset}, .NET SDK ${env.toolchain.dotnetSdk}, hyperfine ${env.toolchain.hyperfine.replace(/^hyperfine /, "")}`]),
    row(["Harness", `commit ${env.git.commit.slice(0, 10)}${env.git.dirty ? " (dirty)" : ""}, pins.json sha256 ${env.pinsSha256.slice(0, 12)}…`]),
    row(["Settings", `${run.settings.runs} runs after ${run.settings.warmup} warmup runs (hyperfine, --shell=${run.settings.hyperfineShell}); driver priority class: ${run.settings.processPriority ?? "normal"}; memory: ${run.settings.memRuns} separate runs`]),
    "");

push("Rig checks:", "", row(["Check", "Required", "Value", "Result"]), row(["---", "---", "---", "---"]));
for (const c of env.checks) push(row([c.id, c.required, c.value, c.ok ? "ok" : "**FAIL**"]));
push("");

push("## Corpus", "", row(["Repository", "Commit", "Files", "MB"]), row(["---", "---", "---:", "---:"]));
for (const r of repos) push(row([r, `\`${run.corpus[r].commit.slice(0, 10)}\``, String(run.corpus[r].files), (run.corpus[r].bytes / 1e6).toFixed(1)]));
push("", "Configurations:", "");
for (const c of configs) push(`- \`${c.id}\`: ${c.label}`);
push("");

push("## Table 1. Wall-clock, throughput, peak memory (parse-only)", "",
    row(["Repository", "Configuration", "Median (s)", "MAD (s)", "MAD/median", "Files/s", "MB/s", "Peak working set (MiB)", "Peak commit (MiB)"]),
    row(["---", "---", "---:", "---:", "---:", "---:", "---:", "---:", "---:"]));
for (const r of repos) for (const c of configs) {
    const x = result(r, c.id);
    if (!x) continue;
    push(row([r, c.id, s(x.timing.median), s(x.timing.mad), pct(x.timing.madOverMedianPercent), x.throughput.filesPerSecond.toFixed(0), x.throughput.megabytesPerSecond.toFixed(1), mib(x.memory.medianPeakWorkingSetBytes), mib(x.memory.medianPeakCommitBytes)]));
}
push("");

const ci = x => `${x.estimate.toFixed(2)} [${x.low.toFixed(2)}, ${x.high.toFixed(2)}]`;
push(`## Table 2. Median wall-clock relative to \`${BASELINE}\`, with 95 % confidence interval`, "",
    "Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.", "",
    row(["Configuration", ...repos, "Geometric mean"]), row(["---", ...repos.map(() => "---:"), "---:"]));
for (const c of configs) {
    const pairs = repos.map(r => result(r, c.id) && result(r, BASELINE) ? [result(r, c.id).timing.times, result(r, BASELINE).timing.times] : undefined);
    const cells = pairs.map(p => p === undefined ? "n/a" : c.id === BASELINE ? "1.00" : ci(bootstrapMedianRatio(p[0], p[1])));
    const valid = pairs.filter(p => p !== undefined);
    const geo = valid.length === 0 ? "n/a" : c.id === BASELINE ? "1.00" : ci(bootstrapGeometricMeanRatio(valid));
    push(row([c.id, ...cells, geo]));
}
push("");

// Decision B11: single-threaded configurations are the rig check; parallel configurations report their spread.
const threadsOf = c => c.threads ?? (c.id.endsWith("-single") || c.id.startsWith("tsc") ? "single" : "parallel");
push(`## Table 3. Variance gate (MAD/median < ${VARIANCE_LIMIT} %)`, "",
    "Binding rows: single-threaded configurations (rig check). Parallel rows: reported spread, not gated; their run-to-run spread is a property of the parallel program (docs/benchmark-harness.md, B11).", "",
    row(["Configuration", "Role", ...repos, "Worst"]), row(["---", "---", ...repos.map(() => "---:"), "---:"]));
let gatePass = true;
for (const c of configs) {
    const binding = threadsOf(c) === "single";
    const v = repos.map(r => result(r, c.id)?.timing.madOverMedianPercent);
    const worst = Math.max(...v.filter(x => x !== undefined));
    if (binding && worst >= VARIANCE_LIMIT) gatePass = false;
    const fmt = x => x === undefined ? "n/a" : (binding && x >= VARIANCE_LIMIT ? `**${pct(x)}**` : pct(x));
    push(row([c.id, binding ? "rig check" : "reported", ...v.map(fmt), fmt(worst)]));
}
push("", `Variance gate (rig check): **${gatePass ? "PASS" : "FAIL"}**${run.protocolCompliant ? "" : " (probe run; the gate is only binding for protocol results)"}.`, "");

push("## Table 4. Runtime metrics (C# configurations only)", "");
const runtime = run.results.filter(x => x.runtime);
if (runtime.length === 0) push("No C# configuration in this run.", "");
else {
    push(row(["Repository", "Configuration", "Bytes allocated", "GC count (gen0/1/2)", "GC pause total (ms)"]), row(["---", "---", "---:", "---:", "---:"]));
    for (const x of runtime) push(row([x.repo, x.config, String(x.runtime.allocatedBytes), `${x.runtime.gen0}/${x.runtime.gen1}/${x.runtime.gen2}`, x.runtime.pauseMs.toFixed(1)]));
    push("");
}

push("## Notes", "",
    "- Median and MAD over all timed runs. MAD is unscaled (no 1.4826 factor). MB = 10^6 bytes, MiB = 2^20 bytes.",
    "- Peak working set is the Windows equivalent of peak RSS (`PeakWorkingSetSize`); peak commit is peak private committed memory (`PeakPagefileUsage`). Both are read after exit via `GetProcessMemoryInfo`, median of the separate memory runs.",
    "- Throughput uses the on-disk size and file count of the parse set (artifacts/bench/filelists/manifest.json).",
    `- Raw timings: the hyperfine exports next to run.json (${run.results.length} files).`,
    "");

writeFileSync(out, lines.join("\n"));
console.log(`written ${out}`);
void median;
