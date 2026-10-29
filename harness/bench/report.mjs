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
    row(["Toolchain", `Node ${env.toolchain.node}, typescript ${env.toolchain.typescript6} / ${env.toolchain.typescript7}, Go ${env.toolchain.goToolset}, .NET SDK ${env.toolchain.dotnetSdk}${env.toolchain.rust ? `, ${env.toolchain.rust}` : ""}, hyperfine ${env.toolchain.hyperfine.replace(/^hyperfine /, "")}`]),
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

push(`## Table 1. Wall-clock, throughput, peak memory (parse-only${configs.some(c => /-bind(-single)?$/.test(c.id)) ? "; `*-bind` rows: parse+bind" : ""}${configs.some(c => /-check(-single)?$/.test(c.id)) ? "; `*-check` rows: the file list as one program, parse+bind+check" : ""})`, "",
    row(["Repository", "Configuration", "Median (s)", "MAD (s)", "MAD/median", "Files/s", "MB/s", "Peak working set (MiB)", "Peak commit (MiB)"]),
    row(["---", "---", "---:", "---:", "---:", "---:", "---:", "---:", "---:"]));
for (const r of repos) for (const c of configs) {
    const x = result(r, c.id);
    if (!x) continue;
    push(row([r, c.id, s(x.timing.median), s(x.timing.mad), pct(x.timing.madOverMedianPercent), x.throughput.filesPerSecond.toFixed(0), x.throughput.megabytesPerSecond.toFixed(1), mib(x.memory.medianPeakWorkingSetBytes), mib(x.memory.medianPeakCommitBytes)]));
}
push("");

// Phase 2 (pre-registration §15): parse+bind configurations (`*-bind`, `*-bind-single`) report the symbols their
// binder created, and are compared with the parse+bind reference (Tables 2c, 2d), never with a parse-only one.
const isBind = id => /-bind(-single)?$/.test(id);
const bindConfigs = configs.filter(c => isBind(c.id));
// Phase 3 (draft pre-registration §17): check configurations (`*-check`, `*-check-single`) report the diagnostics of
// the checked program, and are compared with typescript-go's program check (Table 2e), never with parse-only or bind rows.
const isCheck = id => /-check(-single)?$/.test(id);
const checkConfigs = configs.filter(c => isCheck(c.id));
if (checkConfigs.length > 0) {
    push("## Table 1c. Program check: diagnostics reported (driver report of the validation run)", "",
        "Semantic diagnostics of every program file plus the global diagnostics. The C# configurations check with TypeScript 6.0.3's checker (Track O, Gate C), typescript-go and tsrs with TypeScript 7's, which differs in places: the counts show that checking happened; the C# diagnostics were compared once with tsc 6.0.3's (harness/bench/check-equivalence.mjs).", "",
        row(["Configuration", ...repos]), row(["---", ...repos.map(() => "---:")]));
    for (const c of checkConfigs) push(row([c.id, ...repos.map(r => result(r, c.id) ? String(result(r, c.id).output.diagnostics) : "n/a")]));
    push("");
}
if (bindConfigs.length > 0) {
    push("## Table 1b. Parse+bind: symbols created (driver report of the validation run)", "",
        "Sum of the files' symbol counts. The C# configurations bind with TypeScript 6.0.3's binder (Track O, Gate B), typescript-go and tsrs with TypeScript 7's, which differs in places: the counts show that binding happened, they are not compared.", "",
        row(["Configuration", ...repos]), row(["---", ...repos.map(() => "---:")]));
    for (const c of bindConfigs) push(row([c.id, ...repos.map(r => result(r, c.id) ? String(result(r, c.id).output.symbols) : "n/a")]));
    push("");
}

const ci = x => `${x.estimate.toFixed(2)} [${x.low.toFixed(2)}, ${x.high.toFixed(2)}]`;
push(`## Table 2. Median wall-clock relative to \`${BASELINE}\`, with 95 % confidence interval`, "",
    "Ratio of medians; ratio > 1 means slower than typescript-go's default (parallel) configuration. Interval: percentile bootstrap, 10,000 resamples of both samples, fixed seed. The geometric mean resamples every repository jointly.", "",
    row(["Configuration", ...repos, "Geometric mean"]), row(["---", ...repos.map(() => "---:"), "---:"]));
for (const c of configs.filter(c => !isBind(c.id) && !isCheck(c.id))) {
    const pairs = repos.map(r => result(r, c.id) && result(r, BASELINE) ? [result(r, c.id).timing.times, result(r, BASELINE).timing.times] : undefined);
    const cells = pairs.map(p => p === undefined ? "n/a" : c.id === BASELINE ? "1.00" : ci(bootstrapMedianRatio(p[0], p[1])));
    const valid = pairs.filter(p => p !== undefined);
    const geo = valid.length === 0 ? "n/a" : c.id === BASELINE ? "1.00" : ci(bootstrapGeometricMeanRatio(valid));
    push(row([c.id, ...cells, geo]));
}
push("");

// Secondary reference (prereg/v5): the same ratios against tsrs-parse, when the run contains it.
const RUST = "tsrs-parse";
if (configs.some(c => c.id === RUST)) {
    push(`## Table 2b. Median wall-clock relative to \`${RUST}\` (secondary reference), with 95 % confidence interval`, "",
        "Same method as Table 2, against tsrs (Rust port of TypeScript 7 / typescript-go), parallel. Secondary: never replaces the primary endpoint against typescript-go. tsrs parses JSDoc in .ts files lazily (TypeScript 7.1), typescript-go 7.0.2 eagerly, the C# tracks as tsc's host does (ParseForTypeErrors); see docs/references.md.", "",
        row(["Configuration", ...repos, "Geometric mean"]), row(["---", ...repos.map(() => "---:"), "---:"]));
    for (const c of configs.filter(c => !isBind(c.id) && !isCheck(c.id))) {
        const pairs = repos.map(r => result(r, c.id) && result(r, RUST) ? [result(r, c.id).timing.times, result(r, RUST).timing.times] : undefined);
        const cells = pairs.map(p => p === undefined ? "n/a" : c.id === RUST ? "1.00" : ci(bootstrapMedianRatio(p[0], p[1])));
        const valid = pairs.filter(p => p !== undefined);
        const geo = valid.length === 0 ? "n/a" : c.id === RUST ? "1.00" : ci(bootstrapGeometricMeanRatio(valid));
        push(row([c.id, ...cells, geo]));
    }
    push("");
}

// Parse+bind (pre-registration §15): the bind configurations against typescript-go's parse+bind (endpoint
// cs-o-aot-ws-bind / tsgo7-bind) and against tsrs's (secondary), when the run contains them.
for (const [table, reference, note] of [
    ["2c", "tsgo7-bind", "Endpoint of pre-registration §15: parse+bind per file, against typescript-go 7.0.2 (parser.ParseSourceFile and binder.BindSourceFile per file, parallel). Same method as Table 2. typescript-go's binder ports TypeScript 7 and differs from 6.0.3's in places."],
    ["2d", "tsrs-bind", "Secondary reference: parse+bind against tsrs 0.18.0 (tsrs_parser::parse_source_file_owned and tsrs_binder::bind_source_file per file, parallel). Same method as Table 2."],
    ["2e", "tsgo7-check", "Endpoint of draft pre-registration §17: each repository's file list as one program, parse+bind+check, against typescript-go 7.0.2 (compiler.NewProgram, GetSemanticDiagnostics and GetGlobalDiagnostics on its default checker pool of 4 checkers). Same method as Table 2. typescript-go's checker ports TypeScript 7 and differs from 6.0.3's in places."],
]) {
    if (!configs.some(c => c.id === reference)) continue;
    const rows = reference === "tsgo7-check" ? checkConfigs : bindConfigs;
    push(`## Table ${table}. ${reference === "tsgo7-check" ? "Program check" : "Parse+bind"}: median wall-clock relative to \`${reference}\`, with 95 % confidence interval`, "", note, "",
        row(["Configuration", ...repos, "Geometric mean"]), row(["---", ...repos.map(() => "---:"), "---:"]));
    for (const c of rows) {
        const pairs = repos.map(r => result(r, c.id) && result(r, reference) ? [result(r, c.id).timing.times, result(r, reference).timing.times] : undefined);
        const cells = pairs.map(p => p === undefined ? "n/a" : c.id === reference ? "1.00" : ci(bootstrapMedianRatio(p[0], p[1])));
        const valid = pairs.filter(p => p !== undefined);
        const geo = valid.length === 0 ? "n/a" : c.id === reference ? "1.00" : ci(bootstrapGeometricMeanRatio(valid));
        push(row([c.id, ...cells, geo]));
    }
    push("");
}

// Decision B11 as amended by prereg/v4: the rig check binds the configurations that measure the rig
// (tsc6-parse, tsgo7-parse-single) and the primary single-threaded C# configuration of each track;
// all other configurations report their spread (JIT and Server GC run background threads even in
// "single-threaded" mode, so their spread belongs to the program, not to the rig).
// prereg/v8: reduced profiles bind the single-threaded baseline and the primary single-threaded Track O configuration
// of what they measure; every binding configuration of the run's profile must be present.
const PROFILE_RIG_CHECK = {
    full: ["tsc6-parse", "tsgo7-parse-single", "cs-aot-ws-single", "cs-o-aot-ws-single"],
    quick: ["tsgo7-parse-single", "cs-o-aot-ws-single"],
    binder: ["tsgo7-bind-single", "cs-o-aot-ws-bind-single"],
    check: ["tsgo7-check-single", "cs-o-aot-ws-check-single"],
};
const runProfile = run.profile ?? "full";
const RIG_CHECK = new Set(PROFILE_RIG_CHECK[runProfile] ?? PROFILE_RIG_CHECK.full);
push(`## Table 3. Variance gate (MAD/median < ${VARIANCE_LIMIT} %)`, "",
    `Profile: ${runProfile}. Binding rows (rig check): ${[...RIG_CHECK].join(", ")}${runProfile === "full" || runProfile === "custom" ? " (B11 as amended by prereg/v4)" : " (prereg/v8)"}. Other rows: reported spread, not gated; a secondary configuration above the limit carries a variance note wherever its ratio is used (docs/benchmark-harness.md).`, "",
    row(["Configuration", "Role", ...repos, "Worst"]), row(["---", "---", ...repos.map(() => "---:"), "---:"]));
let gatePass = true;
// Runs before prereg/v8 (no profile) and custom selections keep the earlier rule: binding where present.
const missing = !run.profile || runProfile === "custom" ? [] : [...RIG_CHECK].filter(id => !configs.some(c => c.id === id));
if (missing.length) { gatePass = false; push(`Missing binding configuration(s) for profile ${runProfile}: ${missing.join(", ")}.`, ""); }
for (const c of configs) {
    const binding = RIG_CHECK.has(c.id);
    const v = repos.map(r => result(r, c.id)?.timing.madOverMedianPercent);
    const worst = Math.max(...v.filter(x => x !== undefined));
    if (binding && worst >= VARIANCE_LIMIT) gatePass = false;
    const fmt = x => x === undefined ? "n/a" : (binding && x >= VARIANCE_LIMIT ? `**${pct(x)}**` : pct(x));
    push(row([c.id, binding ? "rig check" : "reported", ...v.map(fmt), fmt(worst)]));
}
push("", `Variance gate (rig check): **${gatePass ? "PASS" : "FAIL"}**${run.protocolCompliant ? "" : " (probe run; the gate is only binding for protocol results)"}.`, "");

push("## Table 4. Fixed cost (empty file list)", "",
    "Process start-up, runtime initialization and driver overhead, measured with the same method on an empty file list. Reported for context, never subtracted from Table 1.", "");
if (!run.fixedCost?.length) push("Not measured in this run.", "");
else {
    push(row(["Configuration", "Median (s)", "MAD/median", "Share of the smallest repository's median", "Peak working set (MiB)"]), row(["---", "---:", "---:", "---:", "---:"]));
    for (const f of run.fixedCost) {
        const medians = run.results.filter(x => x.config === f.config).map(x => x.timing.median);
        const share = medians.length ? `${(100 * f.timing.median / Math.min(...medians)).toFixed(1)} %` : "n/a";
        push(row([f.config, s(f.timing.median), pct(f.timing.madOverMedianPercent), share, mib(f.memory.medianPeakWorkingSetBytes)]));
    }
    push("");
}

push("## Table 5. Runtime metrics (C# configurations only)", "");
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
