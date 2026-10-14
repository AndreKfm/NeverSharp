// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0

import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

export const repoRoot = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
export const artifactsDir = join(repoRoot, "artifacts");

// Script kind by unit name. The Go dumper (go/main.go) implements the same table.
// Returns undefined for names the compiler would not parse as source.
export function scriptKindOf(name) {
    const lower = name.toLowerCase();
    const declaration = /\.d\.([cm]?ts|[^./]+\.ts)$/.test(lower);
    if (/\.(ts|mts|cts)$/.test(lower)) return { kind: "TS", declaration };
    if (lower.endsWith(".tsx")) return { kind: "TSX", declaration: false };
    if (/\.(js|mjs|cjs)$/.test(lower)) return { kind: "JS", declaration: false };
    if (lower.endsWith(".jsx")) return { kind: "JSX", declaration: false };
    if (lower.endsWith(".json")) return { kind: "JSON", declaration: false };
    return undefined;
}
