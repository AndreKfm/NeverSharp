// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Runs the full oracle-agreement pipeline: corpus -> tsc 6.0.3 dump ->
// typescript-go 7.0.2 dump -> comparison. Requires the pinned references
// (scripts/fetch-references.mjs) and toolchain (scripts/install-tools.mjs).
//
// Usage: node run.mjs

import { execFileSync } from "node:child_process";
import { platform } from "node:os";
import { join } from "node:path";
import { artifactsDir } from "./common.mjs";

const here = import.meta.dirname;
const corpus = join(artifactsDir, "equivalence");
const step = (title, cmd, args) => {
    const started = performance.now();
    execFileSync(cmd, args, { cwd: here, stdio: "inherit" });
    console.log(`-- ${title}: ${((performance.now() - started) / 1000).toFixed(1)} s\n`);
};

step("corpus", process.execPath, ["corpus.mjs"]);
step("dump tsc 6.0.3", process.execPath, ["dump-ts.mjs"]);
step("build typescript-go dumper", process.execPath, ["build-go.mjs"]);
step("dump typescript-go 7.0.2", join(artifactsDir, "bin", "astdump-go" + (platform() === "win32" ? ".exe" : "")), [corpus]);
step("compare", process.execPath, ["compare.mjs"]);
