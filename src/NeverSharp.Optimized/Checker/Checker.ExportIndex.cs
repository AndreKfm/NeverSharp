// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// Portions Copyright 2026 Max Schwenk and the tsrs contributors
// SPDX-License-Identifier: Apache-2.0
//
// Ported from crates/tsrs_checker/src/printer.rs (exports_by_target, ExportsByTarget, get_symbol_target_for_alias_lookup,
// modules_exporting, summarize_module_exports, ModuleExportIndex) and checker.rs (alias_cache_blockers) of tsrs
//   https://github.com/oxc-project/tsrs at tag v0.18.0 (5f92808bc92bb97b06212153ab73d28d551c0b9b)
// Track O / tsrs-only, no counterpart in TypeScript 6.0.3: indexes for loops of src/compiler/checker.ts
// (getAliasForSymbolInContainer, getAlternativeContainingModules) at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Modified: translated from Rust to C#; module EO2. tsrs ports typescript-go's loop, which collects every matching entry and so
// resolves every entry; TypeScript 6.0.3's loop (forEachEntry) stops at the first match, so this index covers the
// prefix of the table that the loops have resolved so far and grows in the loop's order. The entries are found by
// position (one target per entry, plus an open-addressing index of the first position per target for larger tables)
// instead of tsrs's sorted (target, entry) pairs. tsrs's size guard for replaced tables is a version of the table's
// contents here (SymTable.Version), and a version of the symbol state getMergedSymbol and resolveSymbol read
// (symbolStateVersion) is added. The module export index is dropped when that state or a table it read changes
// (SymTableStore.WatchedChanges), and the modules asked after such a change are asked as the loop asks them.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // tsrs checker.rs alias_cache_blockers: the export table index is bypassed while this is not 0: one per resolveAlias
    // and getExportsOfModule computation in progress, plus one until the constructor's initializeTypeChecker has merged
    // the globals and the module augmentations.
    private int aliasCacheBlockers = 1;

    // Module EO2: changes whenever getMergedSymbol's answer for some symbol may change (recordMergedSymbol) or the flags
    // resolveSymbol reads may change for a symbol that already exists (mergeSymbol, mergeJSSymbols, late-bound aliases).
    private int symbolStateVersion;

    // tsrs checker.rs exports_by_target_index: per symbol table of this checker (by identity).
    private readonly Dictionary<SymTable, ExportTableIndex> exportTableIndexes = [];

    /// <summary>
    /// The loop at the end of checker.ts getAliasForSymbolInContainer: the first entry of <paramref name="exports"/>, in
    /// table order, for which getSymbolIfSameReference(exported, symbol) holds; None if there is none.
    /// </summary>
    private SymbolRef FirstExportOfSameReference(SymTable exports, SymbolRef symbol)
    {
        if (aliasCacheBlockers == 0 && exports.Count > 0 && TryGetPureAliasLookupTarget(symbol, out var target))
        {
            return FirstExportOfSameReferenceIndexed(exports, symbol, target);
        }
        return FirstExportOfSameReferenceFrom(exports, symbol, 0);
    }

    // The loop itself, from entry `start` on.
    private SymbolRef FirstExportOfSameReferenceFrom(SymTable exports, SymbolRef symbol, int start)
    {
        for (var i = start; i < exports.Count; i++)
        {
            if (!GetSymbolIfSameReference(exports.ValueAt(i), symbol).IsNone)
            {
                return exports.ValueAt(i);
            }
        }
        return SymbolRef.None;
    }

    // tsrs printer.rs exports_by_target: the loop answered from an index of the table by the merged resolved target of
    // each entry (getSymbolIfSameReference(exported, symbol) holds iff the two targets are equal). Exactness:
    // - Only with no alias resolution or module export table computation in progress and after initialization
    //   (aliasCacheBlockers 0; a target seen otherwise may be provisional, and a resolveAlias of an alias in progress
    //   would write unknownSymbol), and only when the target of `symbol` is pure to compute (not an alias with an
    //   unresolved target): then the loop's repeated getSymbolIfSameReference(_, symbol) has no effect and is `target`.
    // - The index holds the targets of the entries [0, Count) in table order, which the loops have resolved before.
    //   Re-reading such a target has no effect and gives the same answer as long as the table's contents (TableVersion)
    //   and the symbol state behind getMergedSymbol and resolveSymbol (StateVersion) are unchanged: alias targets are
    //   final once resolved. Otherwise the index is emptied and rebuilt in the loop's order.
    // - So the loop's answer is the first indexed position with `target`, if any (the loop would get there by reads
    //   only); otherwise the loop goes on from entry Count, which this does exactly, resolving the entries in the
    //   loop's order and stopping at the first match, as the loop does. If resolving an entry changes the table or the
    //   symbol state, the rest of this call is the loop itself.
    private SymbolRef FirstExportOfSameReferenceIndexed(SymTable exports, SymbolRef symbol, SymbolRef target)
    {
        var tableVersion = exports.Version;
        var stateVersion = symbolStateVersion;
        if (!exportTableIndexes.TryGetValue(exports, out var index))
        {
            index = new ExportTableIndex(exports.Count, tableVersion, stateVersion);
            exportTableIndexes.Add(exports, index);
        }
        else if (index.TableVersion != tableVersion || index.StateVersion != stateVersion)
        {
            index.Reset(tableVersion, stateVersion);
        }
        var found = index.FirstPosition(target);
        if (found >= 0)
        {
            return exports.ValueAt(found);
        }
        for (var i = index.Count; i < exports.Count; i++)
        {
            var exported = exports.ValueAt(i);
            // the loop's iteration i: getSymbolIfSameReference(exported, symbol) resolves `exported` first
            var exportedTarget = GetSymbolTargetForAliasLookup(exported);
            if (exports.Version != tableVersion || symbolStateVersion != stateVersion)
            {
                index.Reset(exports.Version, symbolStateVersion);
                return exportedTarget == GetSymbolTargetForAliasLookup(symbol) ? exported : FirstExportOfSameReferenceFrom(exports, symbol, i + 1);
            }
            index.Add(exportedTarget);
            if (exportedTarget == target)
            {
                return exported;
            }
        }
        return SymbolRef.None;
    }

    // The target of `symbol` for the index, when computing it has no effect: false for an alias whose target is not
    // resolved yet (tsrs printer.rs modules_exporting's condition).
    private bool TryGetPureAliasLookupTarget(SymbolRef symbol, out SymbolRef target)
    {
        var merged = GetMergedSymbol(symbol);
        if (!merged.IsNone && IsNonLocalAlias(SymbolFlagsOf(merged)))
        {
            var aliasTarget = AliasSymbolLinks.Has(merged) ? AliasSymbolLinks.Get(merged).AliasTarget : SymbolRef.None;
            if (aliasTarget.IsNone || aliasTarget == resolvingSymbol)
            {
                target = SymbolRef.None;
                return false;
            }
        }
        target = GetMergedSymbol(ResolveSymbol(merged));
        return true;
    }

    // tsrs printer.rs get_symbol_target_for_alias_lookup: one side of getSymbolIfSameReference.
    private SymbolRef GetSymbolTargetForAliasLookup(SymbolRef symbol) => GetMergedSymbol(ResolveSymbol(GetMergedSymbol(symbol)));

    // tsrs checker.rs module_export_index
    private readonly ModuleExportIndex moduleExportIndex = new();
    // tsrs printer.rs modules_exporting: the loop over every external module of the program in
    // getAlternativeContainingModules, that is the module symbols m (in program order) for which
    // getAliasForSymbolInContainer(m, symbol) is not undefined; null when the loop has to run instead. The memo
    // (extendedContainers) misses for every fresh (instantiated) symbol, so declaration emit runs that loop often.
    //
    // For a module whose answer no longer has effects, the answer is decided by data that is fixed: it is not undefined
    // iff m is symbol's parent, or m's `export=` entry or an entry of its export table resolves (merged) to symbol's
    // merged resolved target. Such a module is "summarized": its targets go into a reverse index. That holds once its
    // export table is final (cached by getExportsOfModule, no computation in progress), the export table index has
    // indexed all of that table (every entry resolved) and its `export=` entry is resolved. Modules not summarized yet
    // are asked in program order exactly as the loop does (which may summarize them); summarized ones are pure to ask
    // and are answered from the index. symbol's own target is computed up front, so this is used only when that has no
    // effect either (not an alias with an unresolved target), and not while an alias resolution is in progress.
    //
    // Module EO2 adds: the summaries are dropped when the symbol state (symbolStateVersion) or a table they read (the
    // watched tables of the SymTableStore) changes. The summarized answers are taken before any pending module is
    // asked (the loop asks some of the summarized modules earlier, some later, all with that state unless asking a
    // pending module changes it); if asking a pending module changes the state, the modules after it in program order
    // are asked as the loop asks them, and the summaries are dropped.
    private List<SymbolRef>? ModulesExporting(SymbolRef symbol)
    {
        if (aliasCacheBlockers != 0 || !TryGetPureAliasLookupTarget(symbol, out var target))
        {
            return null;
        }
        var index = moduleExportIndex;
        var tables = Names.Tables;
        var files = program.SourceFiles;
        if (index.Started && (index.StateVersion != symbolStateVersion || index.WatchedChanges != tables.WatchedChanges))
        {
            index.Reset();
        }
        if (!index.Started)
        {
            index.Start(symbolStateVersion, tables.WatchedChanges);
            for (var f = 1; f <= files.Count; f++)
            {
                if (Utilities.IsExternalModule(files[f - 1].Store, files[f - 1].Root))
                {
                    index.Pending.Add(f);
                }
            }
        }
        var stateVersion = symbolStateVersion;
        var watchedChanges = tables.WatchedChanges;
        // the summarized modules' answers, with the state before any pending module is asked
        var found = new List<(int File, SymbolRef Module)>();
        index.AddFilesWithTarget(target, found, this);
        var parent = GetParentOfSymbol(symbol);
        if (!parent.IsNone && index.BySymbol.TryGetValue(parent, out var parentFile))
        {
            found.Add((parentFile, parent));
        }
        var pending = index.Pending;
        var stillPending = new List<int>(pending.Count);
        for (var j = 0; j < pending.Count; j++)
        {
            var f = pending[j];
            var sym = GetSymbolOfDeclaration(new NodeRef(f, files[f - 1].Root));
            if (!GetAliasForSymbolInContainer(sym, symbol).IsNone)
            {
                found.Add((f, sym));
            }
            if (symbolStateVersion != stateVersion || tables.WatchedChanges != watchedChanges || aliasCacheBlockers != 0)
            {
                // the loop from here: the summarized answers hold for the modules before f (the loop asked them before
                // the change); the modules after f are asked as the loop asks them
                found.RemoveAll(e => e.File > f);
                for (var g = f + 1; g <= files.Count; g++)
                {
                    if (!Utilities.IsExternalModule(files[g - 1].Store, files[g - 1].Root))
                    {
                        continue;
                    }
                    var other = GetSymbolOfDeclaration(new NodeRef(g, files[g - 1].Root));
                    if (!GetAliasForSymbolInContainer(other, symbol).IsNone)
                    {
                        found.Add((g, other));
                    }
                }
                index.Reset();
                return InProgramOrder(found);
            }
            if (!SummarizeModuleExports(f, sym))
            {
                stillPending.Add(f);
            }
        }
        index.Pending = stillPending;
        return InProgramOrder(found);

        static List<SymbolRef> InProgramOrder(List<(int File, SymbolRef Module)> found)
        {
            found.Sort((a, b) => a.File.CompareTo(b.File));
            var result = new List<SymbolRef>(found.Count);
            for (var i = 0; i < found.Count; i++)
            {
                if (i == 0 || found[i].File != found[i - 1].File)
                {
                    result.Add(found[i].Module);
                }
            }
            return result;
        }
    }

    // tsrs printer.rs summarize_module_exports: true when module file `file` (symbol `sym`) is summarized (see
    // ModulesExporting). Module EO2: the export table index must cover the whole table and be current, and the tables
    // the summary reads are watched.
    private bool SummarizeModuleExports(int file, SymbolRef sym)
    {
        var flags = SymbolFlagsOf(sym);
        if (aliasCacheBlockers != 0 || (flags & SymbolFlags.Module) == 0 || (flags & SymbolFlags.LateBindingContainer) != 0 ||
            !resolvedModuleExports.TryGetValue(sym, out var table) || moduleExportIndex.BySymbol.ContainsKey(sym))
        {
            return false;
        }
        ExportTableIndex? tableIndex = null;
        if (table.Count > 0 &&
            (!exportTableIndexes.TryGetValue(table, out tableIndex) || tableIndex.Count != table.Count ||
             tableIndex.TableVersion != table.Version || tableIndex.StateVersion != symbolStateVersion))
        {
            return false;
        }
        var ownExports = ExportsOf(sym);
        var exportEqualsTarget = SymbolRef.None;
        var exportEquals = ownExports?.Get(InternalSymbolName.ExportEquals) ?? SymbolRef.None;
        if (!exportEquals.IsNone && !TryGetPureAliasLookupTarget(exportEquals, out exportEqualsTarget))
        {
            return false;
        }
        for (var i = 0; tableIndex is not null && i < tableIndex.Count; i++)
        {
            moduleExportIndex.AddFileWithTarget(tableIndex.TargetAt(i), file);
        }
        if (!exportEquals.IsNone)
        {
            moduleExportIndex.AddFileWithTarget(exportEqualsTarget, file);
        }
        moduleExportIndex.BySymbol.Add(sym, file);
        table.Watch();
        ownExports?.Watch();
        return true;
    }

    /// <summary>tsrs printer.rs ModuleExportIndex: which external modules export what (<see cref="ModulesExporting"/>).</summary>
    private sealed class ModuleExportIndex
    {
        private const int End = -1;

        // merged resolved target -> the head of a list in `files` of the summarized modules with an entry resolving to
        // it (most recent first; usually one module, so no list per target)
        private readonly Dictionary<SymbolRef, int> byTarget = [];

        // list nodes of byTarget: (file number, next node or End)
        private readonly List<(int File, int Next)> files = [];

        internal bool Started { get; private set; }

        internal int StateVersion { get; private set; }

        internal int WatchedChanges { get; private set; }

        /// <summary>The file numbers of the external modules not summarized yet, in program order.</summary>
        internal List<int> Pending { get; set; } = [];

        /// <summary>Summarized module symbol -> its file number.</summary>
        internal Dictionary<SymbolRef, int> BySymbol { get; } = [];

        internal void Start(int stateVersion, int watchedChanges)
        {
            Started = true;
            StateVersion = stateVersion;
            WatchedChanges = watchedChanges;
        }

        internal void Reset()
        {
            Started = false;
            Pending = [];
            byTarget.Clear();
            files.Clear();
            BySymbol.Clear();
        }

        /// <summary>Adds the summarized modules with an entry resolving to <paramref name="target"/>.</summary>
        internal void AddFilesWithTarget(SymbolRef target, List<(int File, SymbolRef Module)> into, Checker checker)
        {
            if (!byTarget.TryGetValue(target, out var node))
            {
                return;
            }
            var sourceFiles = checker.program.SourceFiles;
            for (; node != End; node = files[node].Next)
            {
                var file = files[node].File;
                into.Add((file, checker.GetSymbolOfDeclaration(new NodeRef(file, sourceFiles[file - 1].Root))));
            }
        }

        /// <summary>
        /// Records that module <paramref name="file"/> has an entry resolving to <paramref name="target"/> (once per
        /// consecutive repeat).
        /// </summary>
        internal void AddFileWithTarget(SymbolRef target, int file)
        {
            ref var head = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(byTarget, target, out var exists);
            if (!exists)
            {
                head = files.Count;
                files.Add((file, End));
            }
            else if (files[head].File != file)
            {
                files.Add((file, head));
                head = files.Count - 1;
            }
        }
    }

    /// <summary>
    /// tsrs printer.rs ExportsByTarget, for a prefix of the table: the targets of the entries [0, Count) by position,
    /// and for more than <see cref="LinearLimit"/> entries an open-addressing index (linear probing, load at most 1/2)
    /// of the first position of each target.
    /// </summary>
    private sealed class ExportTableIndex
    {
        private const int LinearLimit = 8;

        private SymbolRef[] targets;
        private int[]? buckets; // first position + 1 per slot, 0 free
        private int shift;
        private int distinct;

        internal ExportTableIndex(int capacity, int tableVersion, int stateVersion)
        {
            targets = new SymbolRef[Math.Max(capacity, 1)];
            TableVersion = tableVersion;
            StateVersion = stateVersion;
        }

        internal int TableVersion { get; private set; }

        internal int StateVersion { get; private set; }

        /// <summary>The number of entries indexed (a prefix of the table).</summary>
        internal int Count { get; private set; }

        /// <summary>The target of indexed entry <paramref name="i"/>.</summary>
        internal SymbolRef TargetAt(int i) => targets[i];

        internal void Reset(int tableVersion, int stateVersion)
        {
            TableVersion = tableVersion;
            StateVersion = stateVersion;
            Count = 0;
            buckets = null;
            distinct = 0;
        }

        /// <summary>The first indexed position whose target is <paramref name="target"/>, -1 if none.</summary>
        internal int FirstPosition(SymbolRef target)
        {
            if (buckets is null)
            {
                for (var i = 0; i < Count; i++)
                {
                    if (targets[i] == target)
                    {
                        return i;
                    }
                }
                return -1;
            }
            var mask = buckets.Length - 1;
            for (var slot = Slot(target); ; slot = (slot + 1) & mask)
            {
                var entry = buckets[slot];
                if (entry == 0)
                {
                    return -1;
                }
                if (targets[entry - 1] == target)
                {
                    return entry - 1;
                }
            }
        }

        /// <summary>Indexes the next entry (position <see cref="Count"/>) with its target.</summary>
        internal void Add(SymbolRef target)
        {
            if (Count == targets.Length)
            {
                Array.Resize(ref targets, Count * 2);
            }
            targets[Count++] = target;
            if (buckets is not null)
            {
                Insert(Count - 1);
            }
            else if (Count > LinearLimit)
            {
                Rehash((int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Count * 4));
            }
        }

        // keeps the first position of each target
        private void Insert(int position)
        {
            var target = targets[position];
            var mask = buckets!.Length - 1;
            for (var slot = Slot(target); ; slot = (slot + 1) & mask)
            {
                var entry = buckets[slot];
                if (entry == 0)
                {
                    buckets[slot] = position + 1;
                    distinct++;
                    if (distinct * 2 > buckets.Length)
                    {
                        Rehash(buckets.Length * 2);
                    }
                    return;
                }
                if (targets[entry - 1] == target)
                {
                    return;
                }
            }
        }

        private void Rehash(int size)
        {
            buckets = new int[size];
            shift = 32 - System.Numerics.BitOperations.Log2((uint)size);
            distinct = 0;
            for (var i = 0; i < Count; i++)
            {
                Insert(i);
            }
        }

        private int Slot(SymbolRef target) => (int)(((uint)target.GetHashCode() * 0x9E3779B9u) >> shift);
    }
}
