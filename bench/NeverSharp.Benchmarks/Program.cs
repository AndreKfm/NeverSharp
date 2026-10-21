// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): BenchmarkDotNet entry point.
//
// Job (fixed in docs/modules/M1.md for the ranking): NativeAOT (.NET 10), 3 warmup and
// 20 measured iterations, MemoryDiagnoser. NEVERSHARP_FILELIST names the file list
// (default: artifacts/bench/filelists/tuning.txt of the checkout).

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.NativeAot;

var config = DefaultConfig.Instance
    .AddJob(Job.Default.WithToolchain(NativeAotToolchain.Net10_0).WithWarmupCount(3).WithIterationCount(20).WithId("aot"))
    .AddDiagnoser(MemoryDiagnoser.Default);
BenchmarkSwitcher.FromAssembly(typeof(NeverSharp.Benchmarks.M1).Assembly).Run(args, config);
