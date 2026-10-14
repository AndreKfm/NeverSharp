// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Captures the measurement environment and checks the rig settings required by
// docs/hardware.md section 2. Windows only for now (the reference machine).
//
// The boost check is indirect: with Core Performance Boost off, the processor
// cannot run above its nominal clock, so "% Processor Performance" under an
// all-core load stays at or below 100. With boost on it exceeds 100.

import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { availableParallelism } from "node:os";
import { join } from "node:path";
import { Worker } from "node:worker_threads";
import { repoRoot } from "./common.mjs";

const powershell = (script) => execFileSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", script], { encoding: "utf8" }).trim();

function machine() {
    const json = powershell(`
        $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
        $cs = Get-CimInstance Win32_ComputerSystem
        $bb = Get-CimInstance Win32_BaseBoard
        $bios = Get-CimInstance Win32_BIOS
        $os = Get-CimInstance Win32_OperatingSystem
        $ubr = (Get-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion').UBR
        [ordered]@{
            cpu = $cpu.Name.Trim(); cores = $cpu.NumberOfCores; logicalProcessors = $cpu.NumberOfLogicalProcessors
            nominalMHz = $cpu.MaxClockSpeed; ramBytes = $cs.TotalPhysicalMemory
            board = "$($bb.Manufacturer) $($bb.Product)"; bios = $bios.SMBIOSBIOSVersion
            os = $os.Caption; osVersion = "$($os.Version).$ubr"
        } | ConvertTo-Json -Compress`);
    return JSON.parse(json);
}

function powerSettings() {
    const scheme = execFileSync("powercfg", ["/getactivescheme"], { encoding: "utf8" }).match(/[0-9a-f-]{36}/i)?.[0];
    const value = (setting) => {
        const out = execFileSync("powercfg", ["/query", "SCHEME_CURRENT", "SUB_PROCESSOR", setting], { encoding: "utf8" });
        // First "0x" index line after the setting is the AC value (output is localized, so match the hex only).
        const hex = [...out.matchAll(/:\s*0x([0-9a-f]+)\s*$/gim)].map(m => parseInt(m[1], 16));
        return hex.length >= 2 ? hex[hex.length - 2] : undefined;
    };
    return { scheme, minProcessorStateAc: value("PROCTHROTTLEMIN"), maxProcessorStateAc: value("PROCTHROTTLEMAX") };
}

const counter = (cls, prop) => `(Get-CimInstance ${cls} -Filter "Name='_Total'").${prop}`;

async function processorPerformanceUnderLoad() {
    // Load every logical processor for ~2.5 s and sample the performance counter meanwhile.
    const workers = Array.from({ length: availableParallelism() }, () =>
        new Worker("const end = Date.now() + 2500; let x = 0; while (Date.now() < end) x += Math.sqrt(x + 1);", { eval: true }));
    await new Promise(r => setTimeout(r, 800));
    const samples = powershell(`1..3 | ForEach-Object { ${counter("Win32_PerfFormattedData_Counters_ProcessorInformation", "PercentProcessorPerformance")}; Start-Sleep -Milliseconds 300 }`)
        .split(/\s+/).map(Number).filter(Number.isFinite);
    await Promise.all(workers.map(w => new Promise(r => w.once("exit", r))));
    return Math.max(...samples);
}

function idleCpuPercent() {
    const samples = powershell(`1..3 | ForEach-Object { ${counter("Win32_PerfFormattedData_PerfOS_Processor", "PercentProcessorTime")}; Start-Sleep -Milliseconds 500 }`)
        .split(/\s+/).map(Number).filter(Number.isFinite);
    return samples.reduce((a, b) => a + b, 0) / samples.length;
}

function git() {
    const run = (...args) => execFileSync("git", ["-C", repoRoot, ...args], { encoding: "utf8" }).trim();
    return { commit: run("rev-parse", "HEAD"), dirty: run("status", "--porcelain").length > 0 };
}

export async function captureEnvironment(toolchain) {
    const idle = idleCpuPercent();
    const rig = { ...powerSettings(), idleCpuPercent: idle, processorPerformanceUnderLoadPercent: await processorPerformanceUnderLoad(), defenderExclusion: "not verifiable without administrator rights" };
    const checks = [
        { id: "min-processor-state", required: "100", value: String(rig.minProcessorStateAc), ok: rig.minProcessorStateAc === 100 },
        { id: "max-processor-state", required: "100", value: String(rig.maxProcessorStateAc), ok: rig.maxProcessorStateAc === 100 },
        { id: "boost-off", required: "processor performance under all-core load <= 101 %", value: `${rig.processorPerformanceUnderLoadPercent} %`, ok: rig.processorPerformanceUnderLoadPercent <= 101 },
        { id: "idle-before-run", required: "mean CPU < 5 %", value: `${idle.toFixed(1)} %`, ok: idle < 5 },
    ];
    const repo = git();
    checks.push({ id: "harness-committed", required: "clean working tree", value: repo.dirty ? "dirty" : "clean", ok: !repo.dirty });
    const pinsSha256 = createHash("sha256").update(readFileSync(join(repoRoot, "pins.json"))).digest("hex");
    return { machine: machine(), rig, toolchain, git: repo, pinsSha256, checks, protocolCompliant: checks.every(c => c.ok) };
}
