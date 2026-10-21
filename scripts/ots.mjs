// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Runs the OpenTimestamps client (opentimestamps-client in .tools/ots-venv) with
// the Windows workaround it needs: python-bitcoinlib loads OpenSSL through
// ctypes.util.find_library("ssl"), which on Windows looks for "ssl.dll" on PATH.
// .tools/ots-ssl/ssl.dll is a copy of Git for Windows' libcrypto-3-x64.dll
// (the functions it binds, BN_* and EC_KEY_*, live in libcrypto). Nothing on the
// system is changed; the PATH addition applies to this process only.
//
// Usage: node scripts/ots.mjs <ots arguments>, e.g.
//        node scripts/ots.mjs upgrade docs/timestamps/prereg-v1.txt.ots
//        node scripts/ots.mjs verify docs/timestamps/prereg-v1.txt.ots

import { spawnSync } from "node:child_process";
import { copyFileSync, existsSync, mkdirSync } from "node:fs";
import { delimiter, dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const isWindows = process.platform === "win32";
const ots = isWindows ? join(root, ".tools", "ots-venv", "Scripts", "ots.exe") : join(root, ".tools", "ots-venv", "bin", "ots");
if (!existsSync(ots)) {
    console.error("OpenTimestamps client missing: python -m venv .tools/ots-venv && .tools/ots-venv/<Scripts|bin>/python -m pip install opentimestamps-client");
    process.exit(2);
}

const env = { ...process.env };
if (isWindows) {
    const sslDir = join(root, ".tools", "ots-ssl");
    const gitBin = join(process.env.ProgramFiles ?? "C:\\Program Files", "Git", "mingw64", "bin");
    if (!existsSync(join(sslDir, "ssl.dll"))) {
        mkdirSync(sslDir, { recursive: true });
        copyFileSync(join(gitBin, "libcrypto-3-x64.dll"), join(sslDir, "ssl.dll"));
    }
    const pathKey = Object.keys(env).find(k => k.toLowerCase() === "path") ?? "PATH";
    env[pathKey] = [sslDir, gitBin, env[pathKey]].join(delimiter);
}

const result = spawnSync(ots, process.argv.slice(2), { stdio: "inherit", env });
process.exit(result.status ?? 1);
