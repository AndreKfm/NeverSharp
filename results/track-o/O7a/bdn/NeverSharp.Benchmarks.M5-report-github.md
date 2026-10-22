```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.6584/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 3900X 4.15GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
  aot    : .NET 10.0.13, X64 NativeAOT x86-64-v3

Job=aot  Toolchain=Latest ILCompiler  IterationCount=20  
WarmupCount=3  

```
| Method            | Mean    | Error    | StdDev   | Gen0       | Gen1       | Gen2      | Allocated |
|------------------ |--------:|---------:|---------:|-----------:|-----------:|----------:|----------:|
| ParseTuningCorpus | 1.206 s | 0.0052 s | 0.0059 s | 34000.0000 | 15000.0000 | 2000.0000 | 416.95 MB |
