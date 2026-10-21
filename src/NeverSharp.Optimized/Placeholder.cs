// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): placeholder until the first ported module exists

namespace NeverSharp.Optimized;

/// <summary>Placeholder until the first ported module exists; keeps the Gate 2 settings and the NoAlloc analyzer under build.</summary>
[NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
internal static class Placeholder
{
    internal static int CharacterCount(ReadOnlySpan<char> text) => text.Length;
}
