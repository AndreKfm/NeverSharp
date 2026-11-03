// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/transformers/utilities.ts (IdentifierNameMap, IdentifierNameMultiMap)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Modified: translated from TypeScript to C#; module D3 (the ESNext and CommonJS module transformers). The values keep
// the insertion order of their keys, as a Map does.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// transformers/utilities.ts IdentifierNameMap: a map keyed by an identifier's name, or by the generated name's
/// identity for a generated identifier (`(auto@id)` or the node it was generated for, with its prefix and suffix).
/// </summary>
internal class IdentifierNameMap<TValue>
{
    private readonly NodeStore store;
    private readonly Dictionary<string, int> indices = new(StringComparer.Ordinal);
    private readonly List<TValue> values = [];

    internal IdentifierNameMap(NodeStore store)
    {
        this.store = store;
    }

    internal int Count => values.Count;

    /// <summary>The values in the insertion order of their keys.</summary>
    internal List<TValue> Values => values;

    internal bool Has(Node key) => indices.ContainsKey(ToKey(key));

    internal bool TryGet(Node key, out TValue value)
    {
        if (indices.TryGetValue(ToKey(key), out var index))
        {
            value = values[index];
            return true;
        }
        value = default!;
        return false;
    }

    internal void Set(Node key, TValue value)
    {
        var k = ToKey(key);
        if (indices.TryGetValue(k, out var index))
        {
            values[index] = value;
        }
        else
        {
            indices.Add(k, values.Count);
            values.Add(value);
        }
    }

    private string ToKey(Node name)
    {
        var autoGenerate = FactoryEmitNode.GetIdentifierAutoGenerate(store, name);
        if (autoGenerate is not null && store.Kind(name) is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier)
        {
            string baseName;
            if ((autoGenerate.Flags & GeneratedIdentifierFlags.KindMask) == GeneratedIdentifierFlags.Node)
            {
                // getNodeForGeneratedName(name): the node a node-generated name was generated for
                var node = GetNodeForGeneratedName(name);
                baseName = store.Kind(node) is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier && node != name
                    ? ToKey(node)
                    : "(generated@" + node.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
            }
            else
            {
                baseName = "(auto@" + autoGenerate.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
            }
            return NodeFactory.FormatGeneratedName(privateName: false, autoGenerate.Prefix, baseName, autoGenerate.Suffix);
        }
        var text = store.Texts.GetString(UtilitiesPublic.IdText(store, name))!;
        return store.Kind(name) == SyntaxKind.PrivateIdentifier ? text[1..] : text;
    }

    // utilities.ts getNodeForGeneratedName
    private Node GetNodeForGeneratedName(Node name)
    {
        var autoGenerate = FactoryEmitNode.GetIdentifierAutoGenerate(store, name)!;
        if ((autoGenerate.Flags & GeneratedIdentifierFlags.Node) != 0)
        {
            var autoGenerateId = autoGenerate.Id;
            var node = name;
            var original = store.Original(node);
            while (!original.IsNone)
            {
                node = original;
                var nodeAutoGenerate = FactoryEmitNode.GetIdentifierAutoGenerate(store, node);
                // if "node" is a different generated name (having a different "autoGenerateId"), use it and stop traversing.
                if (store.Kind(node) is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier &&
                    (nodeAutoGenerate is null || ((nodeAutoGenerate.Flags & GeneratedIdentifierFlags.Node) != 0 && nodeAutoGenerate.Id != autoGenerateId)))
                {
                    break;
                }
                original = store.Original(node);
            }
            // otherwise, return the original node for the source
            return node;
        }
        return name;
    }
}

/// <summary>transformers/utilities.ts IdentifierNameMultiMap: several values per identifier name.</summary>
internal sealed class IdentifierNameMultiMap<TValue> : IdentifierNameMap<List<TValue>>
{
    internal IdentifierNameMultiMap(NodeStore store)
        : base(store)
    {
    }

    internal void Add(Node key, TValue value)
    {
        if (TryGet(key, out var values))
        {
            values.Add(value);
        }
        else
        {
            Set(key, [value]);
        }
    }
}
