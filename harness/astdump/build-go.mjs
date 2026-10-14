// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Builds go/main.go inside the pinned typescript-go checkout via a build
// overlay, using the pinned Go toolchain from .tools/. The checkout is not
// modified: the overlay maps a virtual cmd/neversharp-astdump/main.go to our file.
//
// Usage: node build-go.mjs

import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { platform } from "node:os";
import { join } from "node:path";
import { artifactsDir, repoRoot } from "./common.mjs";

const pins = JSON.parse(readFileSync(join(repoRoot, "pins.json"), "utf8"));
const exe = platform() === "win32" ? ".exe" : "";
const goBin = join(repoRoot, ".tools", `go-${pins.tools.go.version}`, "go", "bin", "go" + exe);
const module = join(repoRoot, "external", "typescript-go");

const head = execFileSync("git", ["-C", module, "rev-parse", "HEAD"], { encoding: "utf8" }).trim();
if (head !== pins.references["typescript-go"].commit) throw new Error(`typescript-go checkout is at ${head}, pinned ${pins.references["typescript-go"].commit}`);

const binDir = join(artifactsDir, "bin");
mkdirSync(binDir, { recursive: true });
const overlay = join(binDir, "astdump-go.overlay.json");
writeFileSync(overlay, JSON.stringify({ Replace: { [join(module, "cmd", "neversharp-astdump", "main.go")]: join(repoRoot, "harness", "astdump", "go", "main.go") } }));
const output = join(binDir, "astdump-go" + exe);
execFileSync(goBin, ["build", "-overlay", overlay, "-o", output, "./cmd/neversharp-astdump"], {
    cwd: module,
    stdio: "inherit",
    env: { ...process.env, GOROOT: "", GOTOOLCHAIN: "local", GOFLAGS: "-mod=mod" },
});
console.log(`built ${output} (typescript-go ${head}, go ${pins.tools.go.version})`);
