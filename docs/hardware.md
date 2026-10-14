# Reference machine

Status: recorded 2026-10-14; decisions in section 3 confirmed by the author 2026-10-14. All measurements in the protocol are taken on this machine.
Items marked **[DECISION, CONFIRMED]** were proposed by the assistant and approved by the author.

## 1. Hardware and OS (as found)

| Component | Value |
|---|---|
| CPU | AMD Ryzen 9 3900X, 12 cores / 24 threads (SMT on), Zen 2, 2 CCDs / 4 CCXs. Windows reports a nominal frequency of 4150 MHz (the part's specified base clock is 3.8 GHz); see 2.1 |
| Mainboard | MSI X570-A PRO (MS-7C37), BIOS H.60 (2019-11-06) |
| Memory | 64 GB DDR4, 4 × 16 GB Corsair CMW64GX4M4C3200C16, configured 3200 MT/s |
| Storage (benchmark volume D:) | Corsair Force MP510 NVMe SSD, 1.8 TB |
| Storage (other) | Crucial T500 NVMe SSD, 2 TB |
| GPU | NVIDIA GeForce RTX 2060 (not used) |
| OS | Windows 11 Pro 10.0.26200, UBR 6584 |
| Form factor | Desktop, no battery |
| Node.js | 24.21.0, pinned and installed into `.tools/` (runs tsc 6.0.3). The system Node (22.18.0, behind a Volta shim) is not used for measurements. |

## 2. Measurement-relevant state (as found)

| Setting | Found | Required for measurement |
|---|---|---|
| Power plan | High performance (`8c5e7fda-…`) | unchanged |
| Minimum processor state (AC) | 5 % | 100 % |
| Maximum processor state (AC) | 100 % | 100 % |
| Core Performance Boost (BIOS) | presumably enabled (default) | see 3.1 |
| SMT | on | on, see 3.2 |
| Defender real-time protection | on | on, with a path exclusion, see 3.3 |
| Windows Search service | running | benchmark directory excluded from indexing |
| Virtualization-based security | running | unchanged, see 3.4 |

### 2.1 Clock behavior, measured 2026-10-14

Measured with the Windows performance counters `ProcessorFrequency` and `PercentProcessorPerformance` (WMI class `Win32_PerfFormattedData_Counters_ProcessorInformation`):

| Load | Nominal frequency | Processor performance | Effective clock |
|---|---|---|---|
| all 24 logical processors busy | 4150 MHz | 100 % | 4150 MHz |
| one thread busy (highest core) | 4150 MHz | 100 % | 4150 MHz |

The clock is the same under single-thread and all-core load and never exceeds the nominal value. This is a fixed all-core clock. Which firmware setting produces it is not recorded and does not need to be: the protocol requires the measured property of 3.1 ("no clock above nominal under any load"), not a particular BIOS setting, and `harness/bench/env.mjs` verifies that property before every run (`boost-off`). The firmware is left as found; the project makes no BIOS changes.

Minimum processor state was 5 % when first recorded and 100 % at the time of this measurement.

## 3. Decisions

### 3.1 [DECISION, CONFIRMED] No clock above nominal under any load (boost off or fixed clock)

Precision Boost raises the clock depending on temperature, load and the number of active cores. Two consequences:

- It is the largest single source of run-to-run variance on this CPU.
- It is not neutral between the two sides. Boost favors lightly threaded work. typescript-go parses and checks in parallel; tsc 6.0 is single-threaded. With boost on, the less parallel side gets a higher clock than the more parallel side. (Whether the C# port parallelizes the way typescript-go does is a separate protocol question.)

Requirement: no core runs above the nominal clock under any load, so the clock does not depend on how many threads a configuration uses. This is the closest Windows equivalent of the brief's "CPU governor fixed". On the reference machine it is met by a fixed all-core clock of 4150 MHz (section 2.1), not by the 3.8 GHz base clock assumed when this decision was written. A run with boost on may be reported, labeled post hoc.

### 3.2 [DECISION, CONFIRMED] SMT stays on

The default configuration, and what users of both compilers run. Recorded in every result file. The 3900X spreads 12 cores over 2 CCDs; cross-CCD scheduling is a known variance source and is covered by the 3 % MAD/median gate rather than by pinning threads, because pinning would favor whichever side benefits from it.

### 3.3 [DECISION, CONFIRMED] Defender stays on, with a path exclusion for the project directory

On-access scanning hooks every file open. Both compilers open the same files, but the scan cost adds noise that is not part of either compiler. Excluding the project directory (`external/`, `.tools/`, build outputs) removes it without turning protection off for the machine. Security trade-off: files in that directory are no longer scanned on access. Everything in it is fetched from pinned commits or SHA-256-verified archives.

### 3.4 [DECISION, CONFIRMED] Virtualization-based security stays as found

Changing security features to make benchmarks faster is not worth it, and VBS costs the same for both sides. Recorded in every result file.

### 3.5 No changes to the rig during the project

The BIOS (H.60, 2019) is old. It is not updated during the project, because a BIOS update changes the AGESA microcode and therefore the rig. Any hardware, BIOS, OS feature-update or driver change between measurements is an amendment and requires re-running the baseline.

## 4. Setup steps for the maintainer

These steps need administrator rights, so the maintainer does them, not the assistant. Applied by the maintainer on 2026-10-14.

1. **Clock:** no action on the reference machine. The clock is already fixed (section 2.1); the `boost-off` check guards it. A different machine with an active boost would need it disabled.
2. **Minimum processor state 100 % (AC)**, in an elevated shell:
   ```
   powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 100
   powercfg /setactive SCHEME_CURRENT
   ```
3. **Defender exclusion**, in an elevated PowerShell:
   ```
   Add-MpPreference -ExclusionPath "D:\Private\Source\Andre\projects\NeverSharp"
   ```
4. **Indexing:** Settings → Privacy & security → Searching Windows → Excluded folders → add the project directory.
5. **During measurement windows:** pause Windows Update, close IDEs, browsers and Claude, leave the machine idle.

Requirement for the harness (not yet built): record each of these states at the start of every run and refuse to produce protocol results if any of them deviates from section 2, "Required for measurement".
