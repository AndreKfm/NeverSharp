// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Driver cross-check (pre-registration A6): does our typescript-go parse-only
// driver handicap Go? Compares, per repository, the driver's parse time
// (median wall-clock of `tsgo7-parse` minus its fixed cost, from a run.json)
// with the parse time tsgo reports about itself (`Parse time` of
// --extendedDiagnostics, median of 5 runs) on the same files, with module
// resolution and lib files turned off so that tsgo parses exactly the parse set.
//
// tsgo's phase includes program-construction work the driver does not do, so
// the driver is expected to be at most as slow. A ratio well above 1 would mean
// the driver handicaps Go.
//
// Usage: node crosscheck.mjs <run.json> [--runs 5]

import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { benchDir, exe } from "./common.mjs";
import { median } from "./stats.mjs";

const runFile = process.argv[2];
const runsArg = process.argv.includes("--runs") ? Number(process.argv[process.argv.indexOf("--runs") + 1]) : 5;
const run = JSON.parse(readFileSync(runFile, "utf8"));
const tsgo = join(import.meta.dirname, "node_modules", "@typescript", `typescript-${process.platform}-${process.arch}`, "lib", "tsc" + exe);
const configDir = join(benchDir, "crosscheck");
mkdirSync(configDir, { recursive: true });

const fixed = run.fixedCost?.find(f => f.config === "tsgo7-parse")?.timing.median;
if (fixed === undefined) throw new Error("run.json has no fixed-cost entry for tsgo7-parse");

const rows = [];
for (const repo of Object.keys(run.corpus)) {
    const result = run.results.find(r => r.repo === repo && r.config === "tsgo7-parse");
    if (!result) continue;
    const files = readFileSync(join(benchDir, "filelists", `${repo}.txt`), "utf8").split("\n").filter(Boolean).map(f => f.replaceAll("\\", "/"));
    const config = join(configDir, `tsconfig.${repo}.json`);
    writeFileSync(config, JSON.stringify({
        compilerOptions: { noEmit: true, noResolve: true, noLib: true, types: [], skipLibCheck: true, target: "esnext", module: "esnext", jsx: "preserve" },
        files,
    }));
    const parseTimes = [];
    for (let i = 0; i < runsArg; i++) {
        let out;
        try { out = execFileSync(tsgo, ["-p", config, "--extendedDiagnostics"], { encoding: "utf8" }); }
        catch (e) { out = e.stdout; } // type errors are expected (no lib files); only the statistics matter
        const match = out.match(/Parse time:\s+([\d.]+)s/);
        if (!match) throw new Error(`no "Parse time" in tsgo output for ${repo}`);
        parseTimes.push(Number(match[1]));
    }
    const tsgoParse = median(parseTimes);
    const driverParse = result.timing.median - fixed;
    rows.push({ repo, files: files.length, tsgoParseSeconds: tsgoParse, tsgoParseRuns: parseTimes, driverParseSeconds: driverParse, ratio: driverParse / tsgoParse });
    console.log(`${repo.padEnd(11)} tsgo parse ${tsgoParse.toFixed(3)} s   driver ${driverParse.toFixed(3)} s   driver/tsgo ${(driverParse / tsgoParse).toFixed(2)}`);
}
const out = join(dirname(runFile), "crosscheck.json");
writeFileSync(out, JSON.stringify({ runId: run.runId, fixedCostSeconds: fixed, rows }, null, 1));
console.log(`written ${out}`);
