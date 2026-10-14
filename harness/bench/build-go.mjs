// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Builds the typescript-go parse-only driver (go/parse.go) inside the pinned
// typescript-go checkout via a build overlay, with the toolset and flags of
// Microsoft's tsgo 7.0.2 release binary (see pins.json tools.go.releaseFlags).
// The checkout is not modified. Prints `go version -m` of the result so the
// build settings can be compared with the release binary.
//
// Usage: node build-go.mjs

import { execFileSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { artifactsDir, exe, pins, repoRoot } from "./common.mjs";

const goBin = join(repoRoot, ".tools", `go-${pins.tools.go.version}`, "go", "bin", "go" + exe);
const module = join(repoRoot, "external", "typescript-go");
const head = execFileSync("git", ["-C", module, "rev-parse", "HEAD"], { encoding: "utf8" }).trim();
if (head !== pins.references["typescript-go"].commit) throw new Error(`typescript-go checkout is at ${head}, pinned ${pins.references["typescript-go"].commit}`);

const binDir = join(artifactsDir, "bin");
mkdirSync(binDir, { recursive: true });
const overlay = join(binDir, "parse-go.overlay.json");
writeFileSync(overlay, JSON.stringify({ Replace: { [join(module, "cmd", "neversharp-parse", "main.go")]: join(repoRoot, "harness", "bench", "go", "parse.go") } }));
const output = join(binDir, "parse-go" + exe);
const env = { ...process.env, GOROOT: "", GOTOOLCHAIN: "local", GOFLAGS: "-mod=mod", CGO_ENABLED: "0", GOAMD64: "v1" };
execFileSync(goBin, ["build", "-overlay", overlay, "-trimpath", "-ldflags=-s -w", "-tags=noembed", "-o", output, "./cmd/neversharp-parse"], { cwd: module, stdio: "inherit", env });
const info = execFileSync(goBin, ["version", "-m", output], { encoding: "utf8", env });
console.log(info.split("\n").filter(l => !/^\s+dep\s/.test(l)).join("\n"));
