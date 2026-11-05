// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// Portions Copyright 2026 Max Schwenk and the tsrs contributors
// SPDX-License-Identifier: Apache-2.0
//
// Ported from crates/tsrs_compiler/src/checkerpool.rs (locality_associations, checked_file_weights and the FENNEL
// step) and crates/tsrs_compiler/src/affinity.rs (ModuleAffinity) of tsrs (notes/mem-assignment.md)
//   https://github.com/oxc-project/tsrs at tag v0.18.0 (5f92808bc92bb97b06212153ab73d28d551c0b9b)
// Track O / tsrs-only: no counterpart in TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8 (one checker).
// Modified: translated from Rust to C#; module SC1. Not ported: the measured-cost cache, the debug assignments.
// FENNEL's step follows a later typescript-go's checkerpool.go (getCheckerAssociationsInOrder), which tsrs ports; tsrs's
// get_checker_associations_in_order, get_checker_association_weights, get_import_targets and undirected are here too
// (notes/perf-clustered-assignment.md). Not ported either: --checkerCostCache (refine_group_associations) and the TSRS_*
// overrides. File paths are the program's canonical paths (Program.PathOf), text lengths in UTF-16 code units instead
// of UTF-8 bytes.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// tsrs's locality assignment of the program's files to the checkers of a pool (checkerpool.rs locality_associations):
/// every directory subtree whose checked weight is at most 1/<see cref="LocalityGroupFraction"/> of an average checker
/// load becomes one group (the shallowest such subtree per file; a file directly inside an oversized directory is its
/// own group), and the groups are placed heaviest first with FENNEL (affinity = import edges between groups, 101 % load
/// cap), from 16 checkers on also with the module affinity. Deterministic: paths are sorted, groups are numbered in path
/// order, ties are broken by index. Unchecked declaration files go to checker 0.
/// </summary>
internal static class CheckerAssignment
{
    private const long TextWeightDivisor = 100;
    private const long SourceFileWeightMultiplier = 4;
    private const long LocalityGroupFraction = 4;
    private const long LocalityPenaltyMultiplier = 1;

    /// <summary>checkerpool.rs locality_associations: the checker of each program file (by program file index).</summary>
    internal static int[] Locality(Program program, int checkerCount)
    {
        var files = program.SourceFiles;
        var associations = new int[files.Count];
        if (checkerCount <= 1)
        {
            return associations;
        }
        var weights = CheckedFileWeights(program);
        var paths = new string[files.Count];
        var order = new List<int>(files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            if (weights[i] > 0)
            {
                order.Add(i);
                paths[i] = program.PathOf(files[i]);
            }
        }
        if (order.Count == 0)
        {
            return associations;
        }
        // A total order (ties by index).
        order.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(paths[a], paths[b]);
            return c != 0 ? c : a.CompareTo(b);
        });

        long total = 0;
        foreach (var i in order)
        {
            total += weights[i];
        }
        var threshold = total / (checkerCount * LocalityGroupFraction);

        // Checked weight of every directory subtree (keys are path prefixes ending before a '/').
        var subtreeWeights = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var i in order)
        {
            var path = paths[i];
            for (var pos = path.IndexOf('/', StringComparison.Ordinal); pos >= 0; pos = path.IndexOf('/', pos + 1))
            {
                var key = path[..pos];
                subtreeWeights[key] = subtreeWeights.GetValueOrDefault(key) + weights[i];
            }
        }
        var groupIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var groupOfFile = new int[files.Count];
        Array.Fill(groupOfFile, -1);
        var groupWeights = new List<long>();
        foreach (var i in order)
        {
            var path = paths[i];
            var key = path;
            for (var pos = path.IndexOf('/', StringComparison.Ordinal); pos >= 0; pos = path.IndexOf('/', pos + 1))
            {
                var dir = path[..pos];
                if (subtreeWeights[dir] <= threshold)
                {
                    key = dir;
                    break;
                }
            }
            if (!groupIds.TryGetValue(key, out var group))
            {
                group = groupWeights.Count;
                groupIds.Add(key, group);
                groupWeights.Add(0);
            }
            groupOfFile[i] = group;
            groupWeights[group] += weights[i];
        }

        // Import edges between groups of checked files, one adjacency entry per file-level edge and direction.
        var importTargets = ImportTargets(program);
        var adjacentFiles = Undirected(importTargets);
        var groupAdjacency = new List<int>[groupWeights.Count];
        for (var g = 0; g < groupAdjacency.Length; g++)
        {
            groupAdjacency[g] = [];
        }
        foreach (var i in order)
        {
            foreach (var j in adjacentFiles[i])
            {
                var (gi, gj) = (groupOfFile[i], groupOfFile[j]);
                if (gj != -1 && gi != gj)
                {
                    groupAdjacency[gi].Add(gj);
                }
            }
        }

        var groupWeightArray = groupWeights.ToArray();
        var groupOrder = new int[groupWeightArray.Length];
        for (var g = 0; g < groupOrder.Length; g++)
        {
            groupOrder[g] = g;
        }
        Array.Sort(groupOrder, (a, b) =>
        {
            var c = groupWeightArray[b].CompareTo(groupWeightArray[a]);
            return c != 0 ? c : a.CompareTo(b);
        });
        var affinity = ModuleAffinity.Create(program, importTargets, groupOfFile, groupAdjacency, checkerCount);
        var placed = AssociationsInOrder(groupWeightArray, groupAdjacency, groupOrder, checkerCount, LocalityPenaltyMultiplier, affinity);
        affinity?.Refine(placed, groupWeightArray, checkerCount);
        foreach (var i in order)
        {
            associations[i] = placed[groupOfFile[i]];
        }
        return associations;
    }

    /// <summary>
    /// checkerpool.rs checked_file_weights: Go's file weights (base work times the source multiplier, plus the normalized
    /// import fanout) for the files a checker processes; 0 for declaration and JSON files that are not type checked.
    /// </summary>
    internal static long[] CheckedFileWeights(Program program)
    {
        var files = program.SourceFiles;
        var options = program.Options;
        var baseWeights = new long[files.Count];
        var importCounts = new long[files.Count];
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var store = file.Store;
            var notChecked = (store.IsDeclarationFile(file.Root) || store.ScriptKind(file.Root) == ScriptKind.JSON)
                && !Program.CanIncludeBindAndCheckDiagnostics(file, options);
            if (notChecked)
            {
                continue;
            }
            // get_checker_association_base_weight
            baseWeights[i] = Math.Max(file.NodeCount + (file.TextLength / TextWeightDivisor), 1) * SourceFileWeightMultiplier;
            importCounts[i] = program.GetImports(file).Count;
        }
        // get_checker_association_weights
        long totalBaseWeight = 0, totalImports = 0;
        for (var i = 0; i < files.Count; i++)
        {
            totalBaseWeight += baseWeights[i];
            totalImports += importCounts[i];
        }
        long importWeight = totalImports > 0 ? Math.Max(totalBaseWeight / totalImports, 1) : 0;
        var weights = new long[files.Count];
        for (var i = 0; i < files.Count; i++)
        {
            weights[i] = baseWeights[i] + (importCounts[i] * importWeight);
        }
        return weights;
    }

    /// <summary>
    /// checkerpool.rs get_checker_associations_in_order: weighted FENNEL (gamma = 3/2) over the items in
    /// <paramref name="order"/>; ties are deterministic (the lighter checker, then the lower index).
    /// </summary>
    private static int[] AssociationsInOrder(long[] weights, List<int>[] adjacent, int[] order, int checkerCount, long penaltyMultiplier, ModuleAffinity? affinity)
    {
        var associations = new int[weights.Length];
        Array.Fill(associations, -1);
        if (weights.Length == 0)
        {
            return associations;
        }
        long totalWeight = 0, maxWeight = 0, edgeCount = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            totalWeight += weights[i];
            maxWeight = Math.Max(maxWeight, weights[i]);
            edgeCount += adjacent[i].Count;
        }
        var checkerWeights = new long[checkerCount];
        var averageCheckerWeight = (totalWeight + checkerCount - 1) / checkerCount;
        var maxCheckerWeight = Math.Max(maxWeight, averageCheckerWeight + (averageCheckerWeight / 100));
        var totalWeightFloat = (double)totalWeight;
        var alpha = penaltyMultiplier * (double)(edgeCount / 2) * Math.Sqrt(checkerCount) / (totalWeightFloat * Math.Sqrt(totalWeightFloat));
        var neighborCounts = new long[checkerCount];
        foreach (var item in order)
        {
            Array.Clear(neighborCounts);
            foreach (var adjacentItem in adjacent[item])
            {
                if (associations[adjacentItem] is var c and >= 0)
                {
                    neighborCounts[c]++;
                }
            }
            var moduleAffinities = affinity?.Affinities(item);
            var best = -1;
            var bestScore = double.NegativeInfinity;
            for (var c = 0; c < checkerCount; c++)
            {
                var checkerWeight = checkerWeights[c];
                if (checkerWeight + weights[item] > maxCheckerWeight)
                {
                    continue;
                }
                var oldWeight = (double)checkerWeight;
                var newWeight = (double)(checkerWeight + weights[item]);
                var penalty = alpha * ((newWeight * Math.Sqrt(newWeight)) - (oldWeight * Math.Sqrt(oldWeight)));
                var score = neighborCounts[c] - penalty + (moduleAffinities is null ? 0.0 : moduleAffinities[c]);
                // (exact ties keep the deterministic tie-breaker, as in tsrs)
                if (score > bestScore || (score == bestScore && (best < 0 || checkerWeight < checkerWeights[best])))
                {
                    best = c;
                    bestScore = score;
                }
            }
            if (best < 0)
            {
                best = 0;
                for (var c = 1; c < checkerCount; c++)
                {
                    if (checkerWeights[c] < checkerWeights[best])
                    {
                        best = c;
                    }
                }
            }
            associations[item] = best;
            checkerWeights[best] += weights[item];
            affinity?.Place(item, best);
        }
        return associations;
    }

    /// <summary>
    /// checkerpool.rs get_import_targets: the program files each program file imports (resolved, other than itself), by
    /// program file index.
    /// </summary>
    internal static List<int>[] ImportTargets(Program program)
    {
        var files = program.SourceFiles;
        var fileIndices = new Dictionary<SourceFile, int>(files.Count, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < files.Count; i++)
        {
            fileIndices[files[i]] = i;
        }
        var targets = new List<int>[files.Count];
        for (var i = 0; i < files.Count; i++)
        {
            var list = new List<int>();
            foreach (var resolved in program.ResolvedModulesOf(files[i]))
            {
                if (resolved is not null && program.GetSourceFile(resolved.ResolvedFileName) is { } imported
                    && fileIndices.TryGetValue(imported, out var importedIndex) && importedIndex != i)
                {
                    list.Add(importedIndex);
                }
            }
            targets[i] = list;
        }
        return targets;
    }

    // checkerpool.rs undirected
    private static List<int>[] Undirected(List<int>[] targets)
    {
        var adjacent = new List<int>[targets.Length];
        for (var i = 0; i < adjacent.Length; i++)
        {
            adjacent[i] = [];
        }
        for (var i = 0; i < targets.Length; i++)
        {
            foreach (var j in targets[i])
            {
                adjacent[i].Add(j);
                adjacent[j].Add(i);
            }
        }
        return adjacent;
    }

    /// <summary>
    /// affinity.rs ModuleAffinity: when FENNEL places a group, each checker gets the worth (node count^<see cref="Gamma"/>)
    /// of the group's modules (its files and the files they import) that groups already placed on the checker also have,
    /// scaled to MU * (import edges + 1) / (worth of all the group's modules); after FENNEL, <see cref="Refine"/> moves
    /// single groups to the checker where their modules cost least, under FENNEL's cap of 101 % of an average load. Only
    /// from <see cref="MinCheckers"/> checkers on (<see cref="LargeMinCheckers"/> with <see cref="LargeProgramFiles"/>
    /// checked files).
    /// </summary>
    private sealed class ModuleAffinity
    {
        private const double Mu = 1.0;
        private const double Gamma = 0.5;
        private const int MaxPasses = 3;

        private readonly double[] worth;
        private readonly int[] starts;
        private readonly int[] modules;
        private readonly double[] total;
        private readonly double[] scale;
        private readonly int words;
        private readonly ulong[] holders;
        private readonly double[] affinity;

        private ModuleAffinity(double[] worth, int[] starts, int[] modules, double[] total, double[] scale, int checkerCount)
        {
            this.worth = worth;
            this.starts = starts;
            this.modules = modules;
            this.total = total;
            this.scale = scale;
            words = (checkerCount + 63) / 64;
            holders = new ulong[worth.Length * words];
            affinity = new double[checkerCount];
        }

        // null when it does not apply (affinity.rs `applies`).
        internal static ModuleAffinity? Create(Program program, List<int>[] targets, int[] groupOfFile, List<int>[] groupAdjacency, int checkerCount)
        {
            var checkedFiles = 0;
            foreach (var g in groupOfFile)
            {
                if (g != -1)
                {
                    checkedFiles++;
                }
            }
            if (!CheckerPool.ModuleAffinityApplies(checkerCount, checkedFiles))
            {
                return null;
            }
            var files = program.SourceFiles;
            var worth = new double[files.Count];
            for (var i = 0; i < files.Count; i++)
            {
                worth[i] = Math.Pow(Math.Max(files[i].NodeCount, 1), Gamma);
            }
            var groupCount = groupAdjacency.Length;
            // Group members by counting sort, in file order.
            var memberStarts = new int[groupCount + 1];
            foreach (var g in groupOfFile)
            {
                if (g != -1)
                {
                    memberStarts[g + 1]++;
                }
            }
            for (var g = 0; g < groupCount; g++)
            {
                memberStarts[g + 1] += memberStarts[g];
            }
            var members = new int[memberStarts[groupCount]];
            var next = (int[])memberStarts.Clone();
            for (var i = 0; i < groupOfFile.Length; i++)
            {
                if (groupOfFile[i] is var g and not -1)
                {
                    members[next[g]++] = i;
                }
            }
            var stamp = new int[files.Count];
            Array.Fill(stamp, -1);
            var moduleList = new List<int>(members.Length * 4);
            var moduleStarts = new int[groupCount + 1];
            var totals = new double[groupCount];
            var scales = new double[groupCount];
            for (var g = 0; g < groupCount; g++)
            {
                var start = moduleList.Count;
                for (var k = memberStarts[g]; k < memberStarts[g + 1]; k++)
                {
                    var i = members[k];
                    if (stamp[i] != g)
                    {
                        stamp[i] = g;
                        moduleList.Add(i);
                    }
                    foreach (var m in targets[i])
                    {
                        if (stamp[m] != g)
                        {
                            stamp[m] = g;
                            moduleList.Add(m);
                        }
                    }
                }
                // In file order, so that the sums below do not depend on the order of the import lists.
                moduleList.Sort(start, moduleList.Count - start, null);
                var sum = 0.0;
                for (var k = start; k < moduleList.Count; k++)
                {
                    sum += worth[moduleList[k]];
                }
                moduleStarts[g + 1] = moduleList.Count;
                totals[g] = sum;
                scales[g] = sum > 0.0 ? Mu * (groupAdjacency[g].Count + 1) / sum : 0.0;
            }
            return new ModuleAffinity(worth, moduleStarts, [.. moduleList], totals, scales, checkerCount);
        }

        // The affinity of `group` to each checker, given the groups placed so far.
        internal double[] Affinities(int group)
        {
            Array.Clear(affinity);
            var s = scale[group];
            if (s > 0.0)
            {
                for (var k = starts[group]; k < starts[group + 1]; k++)
                {
                    var m = modules[k];
                    for (var w = 0; w < words; w++)
                    {
                        var bits = holders[(m * words) + w];
                        while (bits != 0)
                        {
                            var c = (w * 64) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                            affinity[c] += worth[m];
                            bits &= bits - 1;
                        }
                    }
                }
                for (var c = 0; c < affinity.Length; c++)
                {
                    affinity[c] *= s;
                }
            }
            return affinity;
        }

        internal void Place(int group, int checker)
        {
            for (var k = starts[group]; k < starts[group + 1]; k++)
            {
                holders[(modules[k] * words) + (checker / 64)] |= 1UL << (checker % 64);
            }
        }

        // Moves groups between checkers while that lowers the worth of the modules held per checker.
        internal void Refine(int[] groupCheckers, long[] groupWeights, int checkerCount)
        {
            if (checkerCount < 2)
            {
                return;
            }
            var k = checkerCount;
            var moduleCount = worth.Length;
            var counts = new uint[moduleCount * k];
            var bits = new ulong[moduleCount * words];
            var spread = new uint[moduleCount];
            var loads = new long[k];
            for (var g = 0; g < groupCheckers.Length; g++)
            {
                var c = groupCheckers[g];
                loads[c] += groupWeights[g];
                for (var j = starts[g]; j < starts[g + 1]; j++)
                {
                    var m = modules[j];
                    if (++counts[(m * k) + c] == 1)
                    {
                        bits[(m * words) + (c / 64)] |= 1UL << (c % 64);
                        spread[m]++;
                    }
                }
            }
            long totalLoad = 0;
            foreach (var l in loads)
            {
                totalLoad += l;
            }
            var average = (totalLoad + k - 1) / k;
            var cap = average + (average / 100);
            var order = new int[groupCheckers.Length];
            for (var g = 0; g < order.Length; g++)
            {
                order[g] = g;
            }
            Array.Sort(order, (a, b) =>
            {
                var c = groupWeights[a].CompareTo(groupWeights[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
            var held = new double[k];
            for (var pass = 0; pass < MaxPasses; pass++)
            {
                var moved = false;
                foreach (var g in order)
                {
                    var from = groupCheckers[g];
                    Array.Clear(held);
                    double onlyHere = 0, heldEverywhere = 0;
                    for (var j = starts[g]; j < starts[g + 1]; j++)
                    {
                        var m = modules[j];
                        var w = worth[m];
                        if (counts[(m * k) + from] == 1)
                        {
                            onlyHere += w;
                        }
                        if (spread[m] == k)
                        {
                            heldEverywhere += w;
                            continue;
                        }
                        for (var word = 0; word < words; word++)
                        {
                            var b = bits[(m * words) + word];
                            while (b != 0)
                            {
                                held[(word * 64) + System.Numerics.BitOperations.TrailingZeroCount(b)] += w;
                                b &= b - 1;
                            }
                        }
                    }
                    var bestGain = 1e-9;
                    var bestTo = -1;
                    for (var to = 0; to < k; to++)
                    {
                        if (to == from || loads[to] + groupWeights[g] > cap)
                        {
                            continue;
                        }
                        var gain = onlyHere - (total[g] - heldEverywhere - held[to]);
                        if (gain > bestGain)
                        {
                            bestGain = gain;
                            bestTo = to;
                        }
                    }
                    if (bestTo < 0)
                    {
                        continue;
                    }
                    for (var j = starts[g]; j < starts[g + 1]; j++)
                    {
                        var m = modules[j];
                        if (--counts[(m * k) + from] == 0)
                        {
                            bits[(m * words) + (from / 64)] &= ~(1UL << (from % 64));
                            spread[m]--;
                        }
                        if (++counts[(m * k) + bestTo] == 1)
                        {
                            bits[(m * words) + (bestTo / 64)] |= 1UL << (bestTo % 64);
                            spread[m]++;
                        }
                    }
                    loads[from] -= groupWeights[g];
                    loads[bestTo] += groupWeights[g];
                    groupCheckers[g] = bestTo;
                    moved = true;
                }
                if (!moved)
                {
                    break;
                }
            }
        }
    }
}
