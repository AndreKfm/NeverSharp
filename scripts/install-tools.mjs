// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Downloads the pinned toolchain from pins.json into .tools/<name>-<version>,
// verifies each archive's SHA-256 before extracting, and prints the bin paths.
//
// Usage: node scripts/install-tools.mjs [name ...]

import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readdirSync, readFileSync, renameSync, rmSync, writeFileSync } from "node:fs";
import { arch, platform } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const pins = JSON.parse(readFileSync(join(root, "pins.json"), "utf8"));
const host = `${platform()}-${arch()}`;
const exe = platform() === "win32" ? ".exe" : "";
// On Windows use the system bsdtar explicitly: a GNU tar earlier on PATH (Git for Windows) cannot extract .zip.
const tar = platform() === "win32" ? join(process.env.SystemRoot ?? "C:\\Windows", "System32", "tar.exe") : "tar";

// Where each tool's executable lands inside its extracted archive.
const binDir = {
    go: dir => join(dir, "go", "bin"),
    hyperfine: dir => dir,
};

async function install(name, tool) {
    const archive = tool.archives?.[host];
    if (!archive) throw new Error(`${name}: no pinned archive for ${host}`);
    const dir = join(root, ".tools", `${name}-${tool.version}`);
    const stamp = join(dir, ".sha256");
    if (existsSync(stamp) && readFileSync(stamp, "utf8") === archive.sha256) {
        console.log(`ok   ${name} ${tool.version} (cached) ${binDir[name](dir)}`);
        return;
    }

    const response = await fetch(archive.url);
    if (!response.ok) throw new Error(`${name}: HTTP ${response.status} for ${archive.url}`);
    const bytes = Buffer.from(await response.arrayBuffer());
    const actual = createHash("sha256").update(bytes).digest("hex");
    if (actual !== archive.sha256) throw new Error(`${name}: SHA-256 ${actual} != pinned ${archive.sha256}`);

    const tmp = `${dir}.tmp`;
    rmSync(tmp, { recursive: true, force: true });
    mkdirSync(tmp, { recursive: true });
    const file = join(tmp, archive.url.split("/").pop());
    writeFileSync(file, bytes);
    // bsdtar (Windows 10+, macOS) and GNU tar both extract .tar.gz; bsdtar also extracts .zip.
    execFileSync(tar, ["-xf", file, "-C", tmp], { stdio: "inherit" });
    rmSync(file);
    flattenSingleTopDir(name, tmp);
    writeFileSync(join(tmp, ".sha256"), archive.sha256);
    rmSync(dir, { recursive: true, force: true });
    renameSync(tmp, dir);

    const bin = join(binDir[name](dir), name + exe);
    if (!existsSync(bin)) throw new Error(`${name}: expected executable at ${bin}`);
    console.log(`ok   ${name} ${tool.version} ${binDir[name](dir)}`);
}

// hyperfine's Linux/macOS archives contain one top-level directory (hyperfine-vX-target/); lift its contents.
function flattenSingleTopDir(name, dir) {
    if (name !== "hyperfine") return;
    const entries = readdirSync(dir, { withFileTypes: true });
    if (entries.length === 1 && entries[0].isDirectory()) {
        const inner = join(dir, entries[0].name);
        for (const e of readdirSync(inner)) renameSync(join(inner, e), join(dir, e));
        rmSync(inner, { recursive: true });
    }
}

const wanted = new Set(process.argv.slice(2));
for (const [name, tool] of Object.entries(pins.tools)) {
    if (!tool.archives) continue;
    if (wanted.size === 0 || wanted.has(name)) await install(name, tool);
}
