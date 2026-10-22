# Track O: profile of Track F (2026-10-22)

The order of the Track O steps is set by a profile of the finished Track F parser ([phase1-plan.md](phase1-plan.md) §7). This is that profile. It is a development measurement, not a protocol result.

## Method

- Driver `parse-cs-faithful-jit-ws --single` on the vscode file list (11,724 files, 141 MB), the JIT build because `dotnet-trace` attaches to it without extra NativeAOT settings; the bytes allocated are the same in all configurations (M6 Table 5: about 3.27 GB on vscode).
- `dotnet-trace` 10.0.750501 (`.tools/dotnet-trace`): profile `dotnet-common,dotnet-sampled-thread-time` (thread stacks at about 100 Hz, wall-clock including I/O) and, in a second run, `gc-verbose` (allocation samples per type).
- Summaries: `harness/profile/speedscope-summary.mjs` (thread time) and `tools/NeverSharp.TraceSummary` (allocations, GC count and pause).

Commands (from the repository root; absolute paths, dotnet-trace does not resolve relative executables):

```
.tools/dotnet-trace/dotnet-trace collect --profile dotnet-common,dotnet-sampled-thread-time --format Speedscope -o artifacts/profile/vscode-cpu.nettrace -- <abs>\artifacts\bin\cs\faithful-jit-ws\parse-cs-faithful-jit-ws.exe --single <abs>\artifacts\bench\filelists\vscode.txt
.tools/dotnet-trace/dotnet-trace collect --profile gc-verbose -o artifacts/profile/vscode-alloc.nettrace -- <same driver and list>
node harness/profile/speedscope-summary.mjs artifacts/profile/vscode-cpu.speedscope.json
dotnet run -c Release --project tools/NeverSharp.TraceSummary -- artifacts/profile/vscode-alloc.nettrace
```

## Where the time goes (thread time, single-threaded)

| Share | Where |
|---:|---|
| 39.6 % | waiting for the GC (stacks with `PollGC`); nearest caller mostly `List<T>.AddWithResize` (30.0 %) and `set_Capacity` (3.7 %), i.e. node lists growing |
| 19.9 % | `File.ReadAllBytes`, plus about 8 % in `CreateFile`/`CloseHandle`: file I/O, the same work for every competitor |
| 5.2 % | `NodeAllocator.Create` |
| about 4 % | scanner (`Scan`, `ScanIdentifier`, `ScanString`) |
| rest | spread thinly over the parser's functions |

The parsing logic itself is not the bottleneck; allocation and the collections it triggers are.

## What is allocated (sampled, 3,267 MB, 381 GCs, 4.1 s pause)

| Share | Type | Meaning |
|---:|---|---|
| 21.2 % | `System.String` | the source texts (about 282 MB) and identifier/literal values as substrings |
| 19.9 % | `Identifier` | identifier nodes; the faithful node classes carry every field TypeScript's interfaces declare and assign (binder and checker data, JSDoc, …) |
| 6.0 % | `PropertyAccessExpression` | |
| 5.6 % | `Node[]` | backing arrays of node lists |
| 4.6 % | `Byte[]` | file contents before decoding |
| 4.4 % | `NodeArray` | node list objects |
| 3.5 % | `CallExpression` | |
| 2.3 % | `StringLiteral` | |
| 2.3 % | `Entry<string, string>[]` | the identifier interning dictionary |
| 1.0 % | parser closures (`DisplayClass353`) | |

Node objects together are roughly half of the bytes.

## Consequence for the order of Track O steps

1. **O6 node arena layout** (struct records instead of objects; about half of the bytes and most collections).
2. **Text as (pos, length) and identifier interning (O5)** (the strings).
3. **O7 node lists** as ranges in an index arena (lists and their growth).
4. **O1 file reading and decoding** (the byte arrays and the I/O share).
5. Then the scanner steps O2–O4 and O8 (parallel scheduling), as far as the budget allows.
