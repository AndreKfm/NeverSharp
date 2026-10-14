// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0

import { readFileSync } from "node:fs";
import { platform } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

export const repoRoot = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
export const artifactsDir = join(repoRoot, "artifacts");
export const benchDir = join(artifactsDir, "bench");
export const exe = platform() === "win32" ? ".exe" : "";
export const pins = JSON.parse(readFileSync(join(repoRoot, "pins.json"), "utf8"));

// Benchmark file set: TypeScript-family sources tracked by git at the pinned commit.
// The same set is added to the equivalence corpus (harness/astdump/corpus.mjs).
export const sourceExtensions = /\.(ts|tsx|mts|cts)$/i;
