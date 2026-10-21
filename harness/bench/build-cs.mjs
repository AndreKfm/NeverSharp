// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Publishes the C# parse-only drivers: every track x runtime configuration of
// pre-registration §6.4 (eng/RuntimeConfiguration.props), self-contained, win-x64,
// into artifacts/bin/cs/<track>-<runtime>/. Then runs each once on an empty file
// list and checks that the driver reports the requested runtime configuration
// (e.g. that a -srv build really runs with Server GC).
//
// NativeAOT on the reference machine links with the MSVC toolset found by vswhere;
// Visual Studio's vcvarsall calls vswhere.exe by name, so its directory is put on
// PATH for this process (docs/references.md §5).
//
// Usage: node build-cs.mjs [--tracks faithful,optimized] [--runtimes aot-ws,aot-srv,jit-ws,jit-srv]

import { execFileSync, spawnSync } from "node:child_process";
import { delimiter, join } from "node:path";
import { artifactsDir, benchDir, exe, repoRoot } from "./common.mjs";

const arg = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const tracks = arg("--tracks", "faithful").split(",");
const runtimes = arg("--runtimes", "aot-ws,aot-srv,jit-ws,jit-srv").split(",");
const project = join(repoRoot, "tools", "NeverSharp.ParseDriver");

const env = { ...process.env };
if (process.platform === "win32") {
    const pathKey = Object.keys(env).find(k => k.toLowerCase() === "path") ?? "PATH";
    const installer = join(process.env["ProgramFiles(x86)"] ?? "C:\\Program Files (x86)", "Microsoft Visual Studio", "Installer");
    env[pathKey] = [installer, env[pathKey]].join(delimiter);
}

const capital = s => s[0].toUpperCase() + s.slice(1);
for (const track of tracks) {
    for (const runtime of runtimes) {
        const out = join(artifactsDir, "bin", "cs", `${track}-${runtime}`);
        execFileSync("dotnet", ["publish", project, "-c", "Release", "-r", "win-x64", "--self-contained",
            `-p:NeverSharpTrack=${capital(track)}`, `-p:NeverSharpRuntime=${runtime}`, "-o", out, "-nologo", "-v", "q"], { stdio: "inherit", env });
        const driver = join(out, `parse-cs-${track}-${runtime}${exe}`);
        const r = spawnSync(driver, [join(benchDir, "filelists", "empty.txt")], { encoding: "utf8" });
        if (r.status !== 0) throw new Error(`${driver} failed: ${r.stderr}`);
        const reported = JSON.parse(r.stdout.trim().split("\n").pop()).driver;
        const expected = `cs-${track}-${runtime}-parallel`;
        if (reported !== expected) throw new Error(`${driver} reports ${reported}, expected ${expected}`);
        console.log(`ok   ${track}-${runtime}  (driver reports ${reported})`);
    }
}
