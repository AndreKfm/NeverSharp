// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Builds the tsrs parse-only driver (rust/parse-rs) and the tsrs AST dumper (../astdump/rust)
// against the pinned tsrs checkout (external/tsrs, pins.json references.tsrs) with the pinned Rust
// toolchain (.tools/rust, pins.json tools.rust), tsrs's Cargo.lock (Cargo keeps its versions and adds only
// this crate) and release profile.
// Copies the driver to artifacts/bin/parse-rs(.exe) and checks it (parse mode and check mode) on an empty file list.
//
// NativeAOT-style linking on Windows needs the MSVC linker found through vswhere, as for the C# builds.
//
// Usage: node harness/bench/build-rs.mjs

import { copyFileSync, mkdirSync } from "node:fs";
import { delimiter, join } from "node:path";
import { execFileSync, spawnSync } from "node:child_process";
import { artifactsDir, benchDir, exe, pins, repoRoot } from "./common.mjs";

const tsrs = join(repoRoot, "external", "tsrs");
const head = execFileSync("git", ["-C", tsrs, "rev-parse", "HEAD"], { encoding: "utf8" }).trim();
if (head !== pins.references.tsrs.commit) throw new Error(`external/tsrs at ${head}, pinned ${pins.references.tsrs.commit} (node scripts/fetch-references.mjs tsrs)`);

const rust = join(repoRoot, ".tools", "rust");
const env = { ...process.env, RUSTUP_HOME: join(rust, "rustup"), CARGO_HOME: join(rust, "cargo") };
const pathKey = Object.keys(env).find(k => k.toLowerCase() === "path") ?? "PATH";
const installer = join(process.env["ProgramFiles(x86)"] ?? "C:\\Program Files (x86)", "Microsoft Visual Studio", "Installer");
env[pathKey] = [join(rust, "cargo", "bin"), installer, env[pathKey]].join(delimiter);
const cargo = join(rust, "cargo", "bin", "cargo" + exe);
const rustc = execFileSync(join(rust, "cargo", "bin", "rustc" + exe), ["--version"], { encoding: "utf8", env }).trim();
if (!rustc.startsWith(`rustc ${pins.tools.rust.version} `)) throw new Error(`${rustc}, pinned ${pins.tools.rust.version}`);

const target = join(artifactsDir, "rust-target");
for (const dir of [join(repoRoot, "harness", "bench", "rust", "parse-rs"), join(repoRoot, "harness", "astdump", "rust")]) {
    copyFileSync(join(tsrs, "Cargo.lock"), join(dir, "Cargo.lock"));
    execFileSync(cargo, ["build", "--release", "--target-dir", target], { cwd: dir, stdio: "inherit", env });
}
mkdirSync(join(artifactsDir, "bin"), { recursive: true });
const driver = join(artifactsDir, "bin", "parse-rs" + exe);
copyFileSync(join(target, "release", "parse-rs" + exe), driver);
const r = spawnSync(driver, [join(benchDir, "filelists", "empty.txt")], { encoding: "utf8" });
if (r.status !== 0) throw new Error(`parse-rs failed: ${r.stderr}`);
console.log(`ok   parse-rs (${rustc}): ${r.stdout.trim()}`);
// the check mode (module C11) on the empty file list
const c = spawnSync(driver, ["--check", join(benchDir, "filelists", "empty.txt")], { encoding: "utf8" });
if (c.status !== 0 || JSON.parse(c.stdout.trim().split("\n").pop()).driver !== "tsrs-0.18.0-check-parallel") throw new Error(`parse-rs --check failed: ${c.stderr}${c.stdout}`);
console.log(`ok   parse-rs --check: ${c.stdout.trim()}`);
