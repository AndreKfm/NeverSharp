// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's JavaScript truthiness test for the results of speculative callbacks
// (pre-registration §5.4, step O-NA).

using System.Runtime.CompilerServices;

namespace NeverSharp.Optimized;

internal static partial class Core
{
    /// <summary>
    /// JavaScript truthiness (`if (value)`) of the result of a lookAhead, tryScan or tryParse callback, as the scanner's
    /// and the parser's IsTruthy decide it, for the value types such callbacks return: decided by the type argument,
    /// which the compiler folds, so the value is not boxed. Other types take the general test.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTruthyValue<T>(T value)
    {
        if (typeof(T) == typeof(bool))
        {
            return Unsafe.As<T, bool>(ref value);
        }
        if (typeof(T) == typeof(Node))
        {
            return !Unsafe.As<T, Node>(ref value).IsNone;
        }
        if (typeof(T) == typeof(NodeList))
        {
            return !Unsafe.As<T, NodeList>(ref value).IsNone;
        }
        if (typeof(T) == typeof(Parser.Tristate))
        {
            return Unsafe.As<T, Parser.Tristate>(ref value) != Parser.Tristate.False;
        }
        if (typeof(T) == typeof(int))
        {
            return Unsafe.As<T, int>(ref value) != 0;
        }
        if (typeof(T) == typeof(TextRef))
        {
            return Unsafe.As<T, TextRef>(ref value).Length > 0;
        }
        return IsTruthyOther(value);
    }

    // The general test (reference types and the other value types); not reached by the parser's callbacks.
    [NoAllocUnchecked("other types: a value type other than the ones IsTruthyValue tests is boxed for the type test")]
    private static bool IsTruthyOther<T>(T value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            string s => s.Length != 0,
            int i => i != 0,
            double d => d != 0 && !double.IsNaN(d),
            Parser.Tristate t => t != Parser.Tristate.False,
            Node n => !n.IsNone,
            NodeList l => !l.IsNone,
            TextRef t => t.Length > 0,
            TextOrList c => c.IsList || c.Text.Length > 0,
            _ => true,
        };
    }
}
