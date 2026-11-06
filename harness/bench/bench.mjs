// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Benchmark runner. For every (repository, configuration) pair:
//   1. one validation run: the driver's output must report the expected file count, a
//      node count above zero and, for C# drivers, the runtime its configuration names;
//   2. wall-clock via hyperfine (--shell=none, warmup, N runs, JSON export);
//   3. a separate memory pass under peakmem (not timed together with step 2).
// Writes artifacts/bench/results/<runId>/run.json (schema: result.schema.json)
// plus the raw hyperfine exports. A run whose rig checks fail is still written
// but marked protocolCompliant=false; the report labels it as a probe.
//
// Usage: node bench.mjs [--repos a,b] [--profile full|quick|binder|check | --configs x,y] [--runs 20] [--warmup 3]
//                       [--mem-runs 5] [--priority high|normal] [--corpus benchmark|tuning|tuning-check] [--label text]
//        node bench.mjs --check [--repos a,b] [--configs x,y] [--corpus benchmark|tuning|tuning-check]
//        node bench.mjs --dry-run [the options of a run]   (module D7: lists the selected configurations and exits)
// --determinism-runs N (default 5): untimed runs with --dump-diagnostics for the configurations whose parallel
// diagnostics depend on timing (`*-checkpool`, draft pre-registration §20).
// Profiles also: check12 (pre-registration §18), emit (§19, module D7), checkpool (draft §20). Emit configurations (`*-emit`,
// `*-emit-single`) run the drivers with --check --emit and additionally require output counts above zero; they are not
// part of profile full.
//
// --check: functional check, not a measurement: step 1 only, for every selected configuration and repository
// plus the empty file list; prints the drivers' file, node and symbol counts and writes nothing.
//
// Parse+bind configurations (`*-bind`, `*-bind-single`; pre-registration §15) run the drivers with --bind and
// additionally require a symbol count above zero. Check configurations (`*-check`, `*-check-single`; draft §17) run them
// with --check (the file list as one program) and require a diagnostic count and a program file count above zero (the
// tuning corpus has JavaScript files, which the program does not include without allowJs). In the functional check (--check) the validation run goes through peakmem, which reports the
// run's peak working set and wall time.
//
// Default: every configuration (C# drivers: build-cs.mjs). Before the C# parsers exist,
// select the configurations with --configs.
//
// --priority high (default): every driver raises itself to the high priority
// class at start (NEVERSHARP_PRIORITY=high) and reports the class in effect;
// the runner rejects a driver whose reported class differs.

import { execFileSync, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
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
const parseRs = join(bin, "parse-rs" + exe);
const here = import.meta.dirname;

// Configurations of the parse-only suite. Every configuration is reported; none is dropped after the fact.
const configurations = [
    // threads: "single" configurations form the rig check of the variance gate (decision B11).
    { id: "tsc6-parse", label: "tsc 6.0.3 (Node 24), single-threaded", threads: "single", command: list => [node, join(here, "parse-ts.mjs"), list] },
    { id: "tsgo7-parse", label: "typescript-go 7.0.2, parallel (default)", threads: "parallel", command: list => [parseGo, list] },
    { id: "tsgo7-parse-single", label: "typescript-go 7.0.2, single-threaded", threads: "single", command: list => [parseGo, "--single", list] },
    // Secondary reference (prereg/v5): tsrs, the Rust port of TypeScript 7 / typescript-go (build-rs.mjs).
    { id: "tsrs-parse", label: "tsrs 0.18.0 (Rust), parallel (rayon, as tsrs parses files)", threads: "parallel", command: list => [parseRs, list] },
    { id: "tsrs-parse-single", label: "tsrs 0.18.0 (Rust), single-threaded", threads: "single", command: list => [parseRs, "--single", list] },
    // Phase 2 (pre-registration §15): parse+bind per file in the same work item (--bind); the driver reports the
    // symbols its binder created, which must be above zero.
    { id: "tsgo7-bind", label: "typescript-go 7.0.2, parse+bind, parallel (default)", threads: "parallel", bind: true,
        expectDriver: "tsgo-7.0.2-bind-parallel", command: list => [parseGo, "--bind", list] },
    { id: "tsgo7-bind-single", label: "typescript-go 7.0.2, parse+bind, single-threaded", threads: "single", bind: true,
        expectDriver: "tsgo-7.0.2-bind-single", command: list => [parseGo, "--bind", "--single", list] },
    { id: "tsrs-bind", label: "tsrs 0.18.0 (Rust), parse+bind, parallel (rayon)", threads: "parallel", bind: true,
        expectDriver: "tsrs-0.18.0-bind-parallel", command: list => [parseRs, "--bind", list] },
    { id: "tsrs-bind-single", label: "tsrs 0.18.0 (Rust), parse+bind, single-threaded", threads: "single", bind: true,
        expectDriver: "tsrs-0.18.0-bind-single", command: list => [parseRs, "--bind", "--single", list] },
    // Phase 3 (pre-registration §17): the file list as one program, parse+bind+check (--check); the driver reports the
    // diagnostics its checker produced (semantic diagnostics of every file and the global diagnostics).
    { id: "tsgo7-check", label: "typescript-go 7.0.2, program parse+bind+check, parallel (default checker pool)", threads: "parallel", check: true,
        expectDriver: "tsgo-7.0.2-check-parallel", command: list => [parseGo, "--check", list] },
    { id: "tsgo7-check-single", label: "typescript-go 7.0.2, program parse+bind+check, single-threaded", threads: "single", check: true,
        expectDriver: "tsgo-7.0.2-check-single", command: list => [parseGo, "--check", "--single", list] },
    // Pre-registration §18 (prereg/v10): the same program check with 12 checkers (CompilerOptions.Checkers, file i to
    // checker i % 12), secondary configuration of profile check12.
    { id: "tsgo7-check12", label: "typescript-go 7.0.2, program parse+bind+check, parallel (12 checkers)", threads: "parallel", check: true,
        expectDriver: "tsgo-7.0.2-check-parallel-n12", command: list => [parseGo, "--check", "--checkers", "12", list] },
    // tsrs's program check (module C11, decided by the author on 2026-10-29): tsrs_compiler::new_program and its
    // built-in checker pool with tsrs's default parallelism (parse-rs --check; docs/benchmark-harness.md §4.1).
    { id: "tsrs-check", label: "tsrs 0.18.0 (Rust), program parse+bind+check, parallel (tsrs's default checker pool)", threads: "parallel", check: true,
        expectDriver: "tsrs-0.18.0-check-parallel", command: list => [parseRs, "--check", list] },
    { id: "tsrs-check-single", label: "tsrs 0.18.0 (Rust), program parse+bind+check, single-threaded", threads: "single", check: true,
        expectDriver: "tsrs-0.18.0-check-single", command: list => [parseRs, "--check", "--single", list] },
    // Phase 4 (draft pre-registration §19): the file list as one program with Gate D's options, parse+bind+check (the
    // diagnostics of the check configurations), then every file emitted to memory (--check --emit); the driver reports
    // its output files, output bytes and emit diagnostics.
    { id: "tsgo7-emit", label: "typescript-go 7.0.2, program parse+bind+check+emit, parallel (default checker pool)", threads: "parallel", check: true, emit: true,
        expectDriver: "tsgo-7.0.2-emit-parallel", command: list => [parseGo, "--check", "--emit", list] },
    { id: "tsgo7-emit-single", label: "typescript-go 7.0.2, program parse+bind+check+emit, single-threaded", threads: "single", check: true, emit: true,
        expectDriver: "tsgo-7.0.2-emit-single", command: list => [parseGo, "--check", "--emit", "--single", list] },
    { id: "tsrs-emit", label: "tsrs 0.18.0 (Rust), program parse+bind+check+emit, parallel (tsrs's default checker pool)", threads: "parallel", check: true, emit: true,
        expectDriver: "tsrs-0.18.0-emit-parallel", command: list => [parseRs, "--check", "--emit", list] },
    { id: "tsrs-emit-single", label: "tsrs 0.18.0 (Rust), program parse+bind+check+emit, single-threaded", threads: "single", check: true, emit: true,
        expectDriver: "tsrs-0.18.0-emit-single", command: list => [parseRs, "--check", "--emit", "--single", list] },
];
// C# configurations of pre-registration §6.4, built by build-cs.mjs. The driver reports the
// runtime it actually runs with; the runner checks it against the configuration's name.
const csRuntimes = {
    "aot-ws": "NativeAOT, Workstation GC",
    "aot-srv": "NativeAOT, Server GC",
    "jit-ws": ".NET 10 JIT (TieredPGO default), Workstation GC",
    "jit-srv": ".NET 10 JIT, Server GC",
};
for (const [track, prefix] of [["faithful", "cs"], ["optimized", "cs-o"]]) {
    for (const [runtime, description] of Object.entries(csRuntimes)) {
        const driver = join(bin, "cs", `${track}-${runtime}`, `parse-cs-${track}-${runtime}${exe}`);
        const primary = runtime === "aot-ws" ? (track === "faithful" ? " (primary C# configuration)" : " (primary Track O configuration)") : "";
        // draft pre-registration §20 (prereg/v12), amendment of §6.4 A7: the -aot-ws builds embed a gen0 budget of 32 MB
        // (eng/RuntimeConfiguration.props); CoreCLR reads that knob only from the environment, so -jit-ws runs set it.
        const env = runtime === "jit-ws" ? { DOTNET_GCgen0size: "2000000" } : undefined;
        const first = configurations.length;
        configurations.push(
            { id: `${prefix}-${runtime}`, label: `C# Track ${track === "faithful" ? "F" : "O"}, ${description}, parallel${primary}`, threads: "parallel",
                expectDriver: `cs-${track}-${runtime}-parallel`, command: list => [driver, list] },
            { id: `${prefix}-${runtime}-single`, label: `C# Track ${track === "faithful" ? "F" : "O"}, ${description}, single-threaded`, threads: "single",
                expectDriver: `cs-${track}-${runtime}-single`, command: list => [driver, "--single", list] });
        // Parse+bind (pre-registration §15): Track O only, Track F has no binder.
        if (track === "optimized") {
            const endpoint = runtime === "aot-ws" ? " (parse+bind endpoint)" : "";
            configurations.push(
                { id: `${prefix}-${runtime}-bind`, label: `C# Track O, ${description}, parse+bind, parallel${endpoint}`, threads: "parallel", bind: true,
                    expectDriver: `cs-${track}-${runtime}-bind-parallel`, command: list => [driver, "--bind", list] },
                { id: `${prefix}-${runtime}-bind-single`, label: `C# Track O, ${description}, parse+bind, single-threaded`, threads: "single", bind: true,
                    expectDriver: `cs-${track}-${runtime}-bind-single`, command: list => [driver, "--bind", "--single", list] });
        }
        // Parse+bind+check of the program (pre-registration §17): Track O, NativeAOT runtimes only.
        if (track === "optimized" && runtime.startsWith("aot-")) {
            const endpoint = runtime === "aot-ws" ? " (checker endpoint)" : "";
            configurations.push(
                { id: `${prefix}-${runtime}-check`, label: `C# Track O, ${description}, program parse+bind+check, parallel (4 checkers)${endpoint}`, threads: "parallel", check: true,
                    expectDriver: `cs-${track}-${runtime}-check-parallel`, command: list => [driver, "--check", list] },
                { id: `${prefix}-${runtime}-check-single`, label: `C# Track O, ${description}, program parse+bind+check, single-threaded`, threads: "single", check: true,
                    expectDriver: `cs-${track}-${runtime}-check-single`, command: list => [driver, "--check", "--single", list] },
                // pre-registration §18 (prereg/v10): 12 checkers, file i to checker i % 12, as tsgo7-check12
                { id: `${prefix}-${runtime}-check12`, label: `C# Track O, ${description}, program parse+bind+check, parallel (12 checkers)`, threads: "parallel", check: true,
                    expectDriver: `cs-${track}-${runtime}-check-parallel-n12`, command: list => [driver, "--check", "--checkers", "12", list] },
                // draft pre-registration §20 (prereg/v12): tsrs's kind of checker pool (--pool tsrs: 12 checkers under
                // Workstation GC, 16 under Server GC, locality assignment, tail work stealing), compared with tsrs-check.
                // With stealing the parallel diagnostics depend on timing: a determinism pass dumps them per run.
                { id: `${prefix}-${runtime}-checkpool`, label: `C# Track O, ${description}, program parse+bind+check, parallel (--pool tsrs: ${runtime === "aot-ws" ? 12 : 16} checkers, locality, stealing)`, threads: "parallel", check: true,
                    expectDriver: `cs-${track}-${runtime}-check-parallel-n${runtime === "aot-ws" ? 12 : 16}-loc-steal`, command: list => [driver, "--check", "--pool", "tsrs", list],
                    dumpCommand: (list, file) => [driver, "--check", "--pool", "tsrs", "--dump-diagnostics", file, list] });
        }
        // Parse+bind+check+emit of the program (draft pre-registration §19): Track O, NativeAOT runtimes only.
        if (track === "optimized" && runtime.startsWith("aot-")) {
            const endpoint = runtime === "aot-ws" ? " (emitter endpoint)" : "";
            configurations.push(
                { id: `${prefix}-${runtime}-emit`, label: `C# Track O, ${description}, program parse+bind+check+emit, parallel (4 checkers)${endpoint}`, threads: "parallel", check: true, emit: true,
                    expectDriver: `cs-${track}-${runtime}-emit-parallel`, command: list => [driver, "--check", "--emit", list] },
                { id: `${prefix}-${runtime}-emit-single`, label: `C# Track O, ${description}, program parse+bind+check+emit, single-threaded`, threads: "single", check: true, emit: true,
                    expectDriver: `cs-${track}-${runtime}-emit-single`, command: list => [driver, "--check", "--emit", "--single", list] });
        }
        if (env) for (const c of configurations.slice(first)) c.env = env;
    }
}
const envOf = config => config.env ? { ...driverEnv, ...config.env } : driverEnv;

const runs = Number(arg("--runs", "20"));
const warmup = Number(arg("--warmup", "3"));
const memRuns = Number(arg("--mem-runs", "5"));
const determinismRuns = Number(arg("--determinism-runs", "5"));
const repoFilter = arg("--repos", undefined)?.split(",");
const configFilter = arg("--configs", undefined)?.split(",");
const label = arg("--label", "");
const priority = arg("--priority", "high");
if (priority !== "high" && priority !== "normal") throw new Error("--priority must be high or normal");
const driverEnv = { ...process.env, NEVERSHARP_PRIORITY: priority };

// --corpus benchmark (default): Microsoft's five repositories; --corpus tuning: the tuning corpus (pre-registration §3);
// --corpus tuning-check: the tuning corpus without the units tsc 6.0.3 cannot check as one program (tuning-check.mjs).
const corpusName = arg("--corpus", "benchmark");
if (corpusName !== "benchmark" && corpusName !== "tuning" && corpusName !== "tuning-check") throw new Error("--corpus must be benchmark, tuning or tuning-check");
const fileManifest = JSON.parse(readFileSync(join(benchDir, "filelists", corpusName === "benchmark" ? "manifest.json" : `${corpusName}-manifest.json`), "utf8"));
const repos = (corpusName === "benchmark" ? pins.benchmarkCorpus.repos.map(r => r.name) : [corpusName]).filter(r => !repoFilter || repoFilter.includes(r));
// --profile (prereg/v8): fixed configuration sets for protocol runs. full: every configuration; quick: additional
// parse-only runs of Track O; binder: the Phase 2 binder endpoint set. A profile and --configs exclude each other.
// The variance gate binds per profile (report.mjs, PROFILE_RIG_CHECK).
const PROFILES = {
    quick: ["tsgo7-parse", "tsgo7-parse-single", "tsrs-parse", "tsrs-parse-single", "cs-o-aot-ws", "cs-o-aot-ws-single"],
    binder: ["tsgo7-bind", "tsgo7-bind-single", "tsrs-bind", "tsrs-bind-single",
        "cs-o-aot-ws-bind", "cs-o-aot-ws-bind-single", "cs-o-aot-srv-bind", "cs-o-aot-srv-bind-single"],
    // Phase 3 checker endpoint (draft §17); tsrs's program check reported next to it (module C11)
    check: ["tsgo7-check", "tsgo7-check-single", "tsrs-check", "tsrs-check-single", "cs-o-aot-ws-check", "cs-o-aot-ws-check-single", "cs-o-aot-srv-check", "cs-o-aot-srv-check-single"],
    // pre-registration §18 (prereg/v10): 12 checkers on both sides (secondary); the single-threaded rows of profile
    // check bind the variance gate; tsrs's program check is reported next to it
    check12: ["tsgo7-check12", "tsgo7-check-single", "tsrs-check", "cs-o-aot-ws-check12", "cs-o-aot-ws-check-single", "cs-o-aot-srv-check12"],
    // Phase 4 emitter endpoint (draft §19): program check plus emit; binding rows tsgo7-emit-single and
    // cs-o-aot-ws-emit-single (report.mjs)
    emit: ["tsgo7-emit", "tsgo7-emit-single", "tsrs-emit", "tsrs-emit-single", "cs-o-aot-ws-emit", "cs-o-aot-ws-emit-single", "cs-o-aot-srv-emit", "cs-o-aot-srv-emit-single"],
    // draft pre-registration §20 (prereg/v12): tsrs's kind of pool on the C# side against tsrs-check (secondary);
    // typescript-go at 4 and 12 checkers reported next to it; binding rows as in profile check
    checkpool: ["tsrs-check", "tsgo7-check", "tsgo7-check12", "tsgo7-check-single", "cs-o-aot-ws-checkpool", "cs-o-aot-srv-checkpool", "cs-o-aot-ws-check-single"],
};
const profile = arg("--profile", configFilter ? undefined : "full");
if (profile && configFilter) throw new Error("--profile and --configs exclude each other");
if (profile && profile !== "full" && !PROFILES[profile]) throw new Error(`unknown profile ${profile} (full, ${Object.keys(PROFILES).join(", ")})`);
const selected = configFilter ?? (profile === "full" ? undefined : PROFILES[profile]);
for (const id of selected ?? []) if (!configurations.some(c => c.id === id)) throw new Error(`unknown configuration ${id}`);
// Profile full keeps the configuration set it had before the emit configurations (module D7): those run only in profile
// emit or when selected with --configs.
const configs = configurations.filter(c => selected ? selected.includes(c.id) : !c.emit);

// --dry-run (module D7): list the run's profile, settings and configurations (id, label, threads, mode flags, expected
// driver, command with the file list as <list>) as JSON and exit; nothing runs, nothing is written.
if (process.argv.includes("--dry-run")) {
    console.log(JSON.stringify({
        profile: profile ?? "custom", corpus: corpusName, repos, settings: { runs, warmup, memRuns, determinismRuns, priority },
        configurations: configs.map(c => ({ id: c.id, label: c.label, threads: c.threads, bind: c.bind ?? false, check: c.check ?? false, emit: c.emit ?? false,
            env: c.env ?? null, determinism: Boolean(c.dumpCommand), expectDriver: c.expectDriver ?? null, command: c.command("<list>").map(p => p.startsWith(repoRoot) ? relative(repoRoot, p).split("\\").join("/") : p) })),
    }, null, 1));
    process.exit(0);
}

// Step 1 of measure(): one run, checked against the expected file count, the priority class, the driver name and,
// for parse+bind configurations, a symbol count above zero. Returns the driver's report.
// withPeak (the functional check, --check): the run goes through peakmem, which adds the peak working set and the wall
// time it measured to the returned report (peak, peakMs).
function validate(name, list, expectedFiles, config, withPeak = false) {
    const command = config.command(list);
    const check = withPeak
        ? spawnSync(peakmem, command, { encoding: "utf8", env: envOf(config), maxBuffer: 1 << 26 })
        : spawnSync(command[0], command.slice(1), { encoding: "utf8", env: envOf(config), maxBuffer: 1 << 26 });
    if (check.status !== 0) throw new Error(`${config.id} on ${name} failed (${check.status}): ${check.stderr}`);
    const output = JSON.parse(check.stdout.trim().split("\n").pop());
    if (withPeak) {
        const peak = JSON.parse(check.stderr.trim().split("\n").pop());
        output.peak = peak.peakWorkingSetBytes;
        output.peakMs = peak.wallMs;
    }
    if (output.files !== expectedFiles) throw new Error(`${config.id} on ${name}: parsed ${output.files} files, expected ${expectedFiles}`);
    if (output.priority !== priority) throw new Error(`${config.id} on ${name}: driver reports priority class ${output.priority}, expected ${priority}`);
    if (config.expectDriver && output.driver !== config.expectDriver) throw new Error(`${config.id} on ${name}: driver reports ${output.driver}, expected ${config.expectDriver}`);
    if (expectedFiles > 0 && !(output.nodes > 0)) throw new Error(`${config.id} on ${name}: driver reports ${output.nodes} nodes (no parser behind the driver?)`);
    if (config.bind && typeof output.symbols !== "number") throw new Error(`${config.id} on ${name}: driver reports no symbol count (no bind mode?)`);
    if (config.bind && expectedFiles > 0 && !(output.symbols > 0)) throw new Error(`${config.id} on ${name}: driver reports ${output.symbols} symbols (no binder behind the driver?)`);
    if (config.check && typeof output.diagnostics !== "number") throw new Error(`${config.id} on ${name}: driver reports no diagnostic count (no check mode?)`);
    if (config.check && expectedFiles > 0 && !(output.programFiles > 0)) throw new Error(`${config.id} on ${name}: driver reports ${output.programFiles} program files (no program behind the driver?)`);
    if (config.emit && (typeof output.outputFiles !== "number" || typeof output.outputBytes !== "number" || typeof output.emitDiagnostics !== "number")) throw new Error(`${config.id} on ${name}: driver reports no output counts (no emit mode?)`);
    if (config.emit && expectedFiles > 0 && !(output.outputFiles > 0 && output.outputBytes > 0)) throw new Error(`${config.id} on ${name}: driver reports ${output.outputFiles} output files, ${output.outputBytes} bytes (no emitter behind the driver?)`);
    return output;
}

// --check: functional check only (no rig checks, no timing, no memory runs, nothing written): validate every selected
// configuration once per repository and print the drivers' counts.
if (process.argv.includes("--check")) {
    console.log("| Repository | Configuration | Driver | Files | Nodes | Symbols | Diagnostics | Peak working set (MiB) | Wall (s) |");
    console.log("|---|---|---|---:|---:|---:|---:|---:|---:|");
    const emitRows = [];
    for (const repo of repos) {
        for (const config of configs) {
            const output = validate(repo, join(benchDir, "filelists", `${repo}.txt`), fileManifest.repos[repo].files, config, existsSync(peakmem));
            const peak = output.peak === undefined ? "-" : (output.peak / 1048576).toFixed(0);
            const seconds = output.peakMs === undefined ? "-" : (output.peakMs / 1000).toFixed(1);
            console.log(`| ${repo} | ${config.id} | ${output.driver} | ${output.files} | ${output.nodes} | ${output.symbols ?? "-"} | ${output.diagnostics ?? "-"} | ${peak} | ${seconds} |`);
            if (config.emit) emitRows.push(`| ${repo} | ${config.id} | ${output.outputFiles} | ${output.outputBytes} | ${output.emitDiagnostics} | ${output.emitSkipped} |`);
        }
    }
    // emit configurations (module D7): their output counts
    if (emitRows.length > 0) console.log(["", "| Repository | Configuration | Output files | Output bytes | Emit diagnostics | emitSkipped |", "|---|---|---:|---:|---:|---|", ...emitRows].join("\n"));
    for (const config of configs) validate("fixed-cost", join(benchDir, "filelists", "empty.txt"), 0, config);
    console.log(`\nok: ${configs.length} configuration(s) on ${repos.join(", ")} and the empty file list`);
    process.exit(0);
}

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
    rust: existsSync(join(tools, "rust", "cargo", "bin", "rustc" + exe)) ? version(join(tools, "rust", "cargo", "bin", "rustc" + exe), ["--version"]) : "not installed",
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
    const output = validate(name, list, expectedFiles, config);

    // 2. wall-clock
    const exportFile = join(outDir, `hyperfine-${name}-${config.id}.json`);
    console.log(`\n== ${name} / ${config.id}`);
    execFileSync(hyperfine, ["--shell=none", "--warmup", String(warmup), "--runs", String(runs), "--export-json", exportFile,
        "--command-name", `${name}/${config.id}`, command.map(quote).join(" ")], { stdio: "inherit", env: envOf(config) });
    const hf = JSON.parse(readFileSync(exportFile, "utf8")).results[0];
    const times = hf.times;
    const med = median(times);

    // 3. memory
    const peaks = [];
    for (let i = 0; i < memRuns; i++) {
        const r = spawnSync(peakmem, command, { encoding: "utf8", env: envOf(config) });
        if (r.status !== 0) throw new Error(`peakmem ${config.id} on ${name} failed: ${r.stderr}`);
        peaks.push(JSON.parse(r.stderr.trim().split("\n").pop()));
    }

    // 4. determinism (draft pre-registration §20): configurations whose parallel diagnostics depend on timing (work
    // stealing) dump their diagnostics in separate untimed runs; reported are the diagnostic count of each run and the
    // number of distinct dumps (SHA-256). The dump file is deleted after hashing.
    let diagnosticsRuns = null;
    if (config.dumpCommand && expectedFiles > 0) {
        const dumpFile = join(outDir, `diagnostics-${name}-${config.id}.tmp`);
        const counts = [], hashes = new Set();
        for (let i = 0; i < determinismRuns; i++) {
            const dumpCmd = config.dumpCommand(list, dumpFile);
            const r = spawnSync(dumpCmd[0], dumpCmd.slice(1), { encoding: "utf8", env: envOf(config), maxBuffer: 1 << 26 });
            if (r.status !== 0) throw new Error(`determinism run ${config.id} on ${name} failed: ${r.stderr}`);
            counts.push(JSON.parse(r.stdout.trim().split("\n").pop()).diagnostics);
            hashes.add(createHash("sha256").update(readFileSync(dumpFile)).digest("hex"));
        }
        rmSync(dumpFile, { force: true });
        diagnosticsRuns = { runs: determinismRuns, counts, distinctDumps: hashes.size };
    }

    return {
        diagnosticsRuns,
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
    profile: profile ?? "custom",
    startedAt: startedAt.toISOString(),
    finishedAt: new Date().toISOString(),
    protocolCompliant: environment.protocolCompliant,
    suite: corpusName === "benchmark" ? "parse" : `parse-${corpusName}`,
    settings: { runs, warmup, memRuns, determinismRuns, hyperfineShell: "none", processPriority: priority },
    environment,
    corpus: Object.fromEntries(repos.map(r => [r, { commit: fileManifest.repos[r].commit, files: fileManifest.repos[r].files, bytes: fileManifest.repos[r].bytes, contentSha256: fileManifest.repos[r].contentSha256 }])),
    configurations: configs.map(c => ({ id: c.id, label: c.label, threads: c.threads })),
    results,
    fixedCost,
};
writeFileSync(join(outDir, "run.json"), JSON.stringify(run, null, 1));
console.log(`\nwritten ${join(outDir, "run.json")}`);
