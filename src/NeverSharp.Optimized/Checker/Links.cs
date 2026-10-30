// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts (SymbolLinks, NodeLinks) and src/compiler/checker.ts (getSymbolLinks, getNodeLinks)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/types.go (per-kind link structs: ValueSymbolLinks, AliasSymbolLinks, ...,
// NodeLinks, TypeNodeLinks, SignatureLinks, ...) and internal/core/linkstore.go (LinkStore[K, V])
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: per-checker stores keyed by SymbolRef / NodeRef (docs/phase3-checker-layout.md); module C0 defines the
// stores and the first link kinds, the checker modules add the kinds they need (one store per kind, as typescript-go).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// typescript-go's <c>core.LinkStore[*ast.Symbol, V]</c> for one link kind <typeparamref name="T"/>: the links of a
/// symbol, created zeroed on first access. Keyed by <see cref="SymbolRef"/>: per file (0 = transient symbols) a dense
/// slot array indexed by the symbol's index (binder symbol indices are dense per file), the records in one array in
/// creation order. Only symbols that have links of this kind cost a record.
/// </summary>
internal sealed class SymbolLinkStore<T>
    where T : struct
{
    private int[][] slots;           // [file][symbol index] -> record index (0: none)
    private readonly Chunks<T> records = new(); // records[0] unused
    private int count;

    internal SymbolLinkStore(int fileCount)
    {
        slots = new int[fileCount + 1][];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = [];
        }
    }

    /// <summary>The links of <paramref name="symbol"/>, created zeroed if it has none (getSymbolLinks).</summary>
    internal ref T Get(SymbolRef symbol)
    {
        var fileSlots = slots[symbol.File];
        if (symbol.Index >= fileSlots.Length)
        {
            var grown = new int[Math.Max(symbol.Index + 1, fileSlots.Length * 2)];
            fileSlots.CopyTo(grown, 0);
            slots[symbol.File] = fileSlots = grown;
        }
        var slot = fileSlots[symbol.Index];
        if (slot == 0)
        {
            slot = fileSlots[symbol.Index] = ++count;
            records.EnsureIndex(slot);
        }
        return ref records[slot];
    }

    /// <summary>Whether <paramref name="symbol"/> has links of this kind (typescript-go LinkStore.Has).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool Has(SymbolRef symbol)
    {
        var fileSlots = slots[symbol.File];
        return symbol.Index < fileSlots.Length && fileSlots[symbol.Index] != 0;
    }

    /// <summary>Grows the per-file tables when the program has more files (not needed by C0's programs).</summary>
    internal void EnsureFiles(int fileCount)
    {
        if (fileCount + 1 > slots.Length)
        {
            var old = slots.Length;
            Array.Resize(ref slots, fileCount + 1);
            for (var i = old; i < slots.Length; i++)
            {
                slots[i] = [];
            }
        }
    }
}

/// <summary>
/// typescript-go's <c>core.LinkStore[*ast.Node, V]</c> for one link kind <typeparamref name="T"/>: the links of a node,
/// created zeroed on first access. Node handles are record offsets (sparse), so per file an open-addressing map from
/// node handle to record index (linear probing, load at most 1/2, as the binder's node maps), the records in one array.
/// </summary>
internal sealed class NodeLinkStore<T>
    where T : struct
{
    private int[][] keys;            // [file] -> node handles (0: free slot)
    private int[][] values;          // [file] -> record index
    private int[] counts;
    private T[] records = new T[64]; // records[0] unused
    private int count;

    internal NodeLinkStore(int fileCount)
    {
        keys = new int[fileCount + 1][];
        values = new int[fileCount + 1][];
        counts = new int[fileCount + 1];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = [];
            values[i] = [];
        }
    }

    /// <summary>The links of <paramref name="node"/>, created zeroed if it has none (getNodeLinks).</summary>
    internal ref T Get(NodeRef node)
    {
        var file = node.File;
        if ((counts[file] + 1) * 2 > keys[file].Length)
        {
            Grow(file);
        }
        var k = keys[file];
        var mask = k.Length - 1;
        for (var i = Slot(node.Node.Index, k.Length); ; i = (i + 1) & mask)
        {
            if (k[i] == node.Node.Index)
            {
                return ref records[values[file][i]];
            }
            if (k[i] == 0)
            {
                if (count + 1 == records.Length)
                {
                    Array.Resize(ref records, records.Length * 2);
                }
                k[i] = node.Node.Index;
                values[file][i] = ++count;
                counts[file]++;
                return ref records[count];
            }
        }
    }

    /// <summary>Whether <paramref name="node"/> has links of this kind.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool Has(NodeRef node)
    {
        var k = keys[node.File];
        if (k.Length == 0)
        {
            return false;
        }
        var mask = k.Length - 1;
        for (var i = Slot(node.Node.Index, k.Length); ; i = (i + 1) & mask)
        {
            if (k[i] == node.Node.Index)
            {
                return true;
            }
            if (k[i] == 0)
            {
                return false;
            }
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int Slot(int handle, int capacity) => (int)(((uint)handle * 0x9E3779B1u) >> (32 - System.Numerics.BitOperations.Log2((uint)capacity)));

    private void Grow(int file)
    {
        var oldKeys = keys[file];
        var oldValues = values[file];
        var capacity = Math.Max(16, oldKeys.Length * 2);
        var newKeys = new int[capacity];
        var newValues = new int[capacity];
        var mask = capacity - 1;
        for (var j = 0; j < oldKeys.Length; j++)
        {
            if (oldKeys[j] == 0)
            {
                continue;
            }
            var i = Slot(oldKeys[j], capacity);
            while (newKeys[i] != 0)
            {
                i = (i + 1) & mask;
            }
            newKeys[i] = oldKeys[j];
            newValues[i] = oldValues[j];
        }
        keys[file] = newKeys;
        values[file] = newValues;
    }
}

/// <summary>types.go ValueSymbolLinks (checker.ts SymbolLinks: type, writeType, target, mapper, nameType, containingType).</summary>
internal struct ValueSymbolLinks
{
    public Type ResolvedType;
    public Type WriteType;
    public SymbolRef Target;
    public int Mapper;
    public Type NameType;
    public Type ContainingType;
    public bool FunctionOrConstructorChecked;
}

/// <summary>types.go AliasSymbolLinks.</summary>
internal struct AliasSymbolLinks
{
    public SymbolRef ImmediateTarget;
    public SymbolRef AliasTarget;
    public bool Referenced;
    public NodeRef TypeOnlyDeclaration;
    public byte TypeOnlyState; // links.typeOnlyDeclaration: 0 undefined, 1 false, 2 a declaration (module C8)
}

/// <summary>types.go ExportTypeLinks (checker.ts SymbolLinks.target and originatingImport of the module symbols
/// cloneTypeAsModuleType creates).</summary>
internal struct ExportTypeLinks
{
    public SymbolRef Target;
    public NodeRef OriginatingImport;
}

/// <summary>types.go DeclaredTypeLinks (declared types of type parameters, classes, interfaces, enums).</summary>
internal struct DeclaredTypeLinks
{
    public Type DeclaredType;
    public bool InterfaceChecked;
    public bool IndexSignaturesChecked;
    public bool TypeParametersChecked;
    public bool EnumChecked;
}

/// <summary>types.go NodeLinks (checker.ts NodeLinks.flags and the members typescript-go keeps with them).</summary>
internal struct NodeLinks
{
    public NodeCheckFlags Flags;
    public sbyte DeclarationRequiresScopeChange; // core.Tristate: 0 unknown, 1 false, 2 true
    public bool HasReportedStatementInAmbientContext;
}

/// <summary>types.go SymbolNodeLinks (checker.ts NodeLinks.resolvedSymbol).</summary>
internal struct SymbolNodeLinks
{
    public SymbolRef ResolvedSymbol;
}

/// <summary>types.go TypeNodeLinks (checker.ts NodeLinks.resolvedType, outerTypeParameters).</summary>
internal struct TypeNodeLinks
{
    public Type ResolvedType;
    public TypeList OuterTypeParameters;
}

/// <summary>types.go SignatureLinks (checker.ts NodeLinks.resolvedSignature, effectsSignature, decoratorSignature).</summary>
internal struct SignatureLinks
{
    public Signature ResolvedSignature;
    public Signature EffectsSignature;
    public Signature DecoratorSignature;
}
