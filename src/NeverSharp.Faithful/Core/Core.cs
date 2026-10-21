// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/core.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/core/core.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Port structure follows internal/core/binarysearch.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>core.ts: <c>const enum AssertionLevel</c>.</summary>
internal enum AssertionLevel
{
    None = 0,
    Normal = 1,
    Aggressive = 2,
    VeryAggressive = 3,
}

/// <summary>The functions of core.ts that the parser reaches.</summary>
internal static partial class Core
{
    // core.ts:17 emptyArray: never[] (a shared empty array per element type).
    internal static IReadOnlyList<T> EmptyArray<T>() => Array.Empty<T>();

    // core.ts:19 emptyMap: ReadonlyMap<never, never> (a shared empty map per key/value type).
    internal static IReadOnlyDictionary<TKey, TValue> EmptyMap<TKey, TValue>()
        where TKey : notnull => EmptyMapHolder<TKey, TValue>.Value;

    private static class EmptyMapHolder<TKey, TValue>
        where TKey : notnull
    {
        internal static readonly Dictionary<TKey, TValue> Value = [];
    }

    /// <summary>JavaScript's index access: a position outside the string reads as NaN, which equals no character code; here -1.</summary>
    internal static int CharCodeAt(string text, int index) => (uint)index < (uint)text.Length ? text[index] : -1;

    /// <summary>JavaScript truthiness of a value (<c>if (value)</c>): not undefined/null, not false, 0, NaN or "".</summary>
    internal static bool IsTruthy<T>(T? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        int i => i != 0,
        long l => l != 0,
        double d => d != 0 && !double.IsNaN(d),
        float f => f != 0 && !float.IsNaN(f),
        Enum e => ((IConvertible)e).ToInt64(CultureInfo.InvariantCulture) != 0,
        _ => true,
    };

    /// <summary>JavaScript's <c>String(value)</c> / template-literal conversion for the values this port passes around.</summary>
    internal static string ToJsString(object? value) => value switch
    {
        null => "undefined",
        bool b => b ? "true" : "false",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>JavaScript's <c>typeof</c> for the values this port passes around.</summary>
    private static string JsTypeof(object? value) => value switch
    {
        null => "undefined",
        bool => "boolean",
        string => "string",
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal or Enum => "number",
        _ => "object",
    };

    /// <summary>JavaScript's <c>array.slice(start, end)</c> index normalization: negative values count from the end, both are clamped.</summary>
    private static List<T> Slice<T>(IReadOnlyList<T> array, int? start, int? end)
    {
        var len = array.Count;
        var from = start is null ? 0 : start.Value < 0 ? Math.Max(len + start.Value, 0) : Math.Min(start.Value, len);
        var to = end is null ? len : end.Value < 0 ? Math.Max(len + end.Value, 0) : Math.Min(end.Value, len);
        var result = new List<T>(Math.Max(to - from, 0));
        for (var i = from; i < to; i++)
        {
            result.Add(array[i]);
        }
        return result;
    }

    // core.ts:22
    internal static int Length<T>(IReadOnlyList<T>? array) => array is not null ? array.Count : 0;

    /// <summary>
    /// Iterates through 'array' by index and performs the callback on each element of array until the callback
    /// returns a truthy value, then returns that value.
    /// If no such value is found, the callback is applied to each element of array and undefined is returned.
    /// </summary>
    // core.ts:33
    internal static U? ForEach<T, U>(IReadOnlyList<T>? array, Func<T, int, U?> callback)
    {
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var result = callback(array[i], i);
                if (IsTruthy(result))
                {
                    return result;
                }
            }
        }
        return default;
    }

    /// <summary>Works like Array.prototype.find, returning `undefined` if no element satisfying the predicate is found.</summary>
    // core.ts:162
    internal static T? Find<T>(IReadOnlyList<T>? array, Func<T, int, bool> predicate, int? startIndex = null)
    {
        if (array is null)
        {
            return default;
        }
        for (var i = startIndex ?? 0; i < array.Count; i++)
        {
            var value = array[i];
            if (predicate(value, i))
            {
                return value;
            }
        }
        return default;
    }

    /// <summary>Works like Array.prototype.findIndex, returning `-1` if no element satisfying the predicate is found.</summary>
    // core.ts:198
    internal static int FindIndex<T>(IReadOnlyList<T>? array, Func<T, int, bool> predicate, int? startIndex = null)
    {
        if (array is null)
        {
            return -1;
        }
        for (var i = startIndex ?? 0; i < array.Count; i++)
        {
            if (predicate(array[i], i))
            {
                return i;
            }
        }
        return -1;
    }

    // core.ts:220
    internal static bool Contains<T>(IReadOnlyList<T>? array, T value, Func<T, T, bool>? equalityComparer = null)
    {
        equalityComparer ??= EquateValues;
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (equalityComparer(array[i], value))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Filters an array by a predicate function. Returns the same array instance if the predicate is
    /// true for all elements, otherwise returns a new array instance containing the filtered subset.
    /// </summary>
    // core.ts:261-296 (the overloads for mutable arrays)
    [return: NotNullIfNotNull(nameof(array))]
    internal static List<T>? Filter<T>(List<T>? array, Func<T, bool> f)
    {
        if (array is not null)
        {
            var len = array.Count;
            var i = 0;
            while (i < len && f(array[i]))
            {
                i++;
            }
            if (i < len)
            {
                var result = array.GetRange(0, i);
                i++;
                while (i < len)
                {
                    var item = array[i];
                    if (f(item))
                    {
                        result.Add(item);
                    }
                    i++;
                }
                return result;
            }
        }
        return array;
    }

    // core.ts:261-296 (the overloads for readonly arrays)
    [return: NotNullIfNotNull(nameof(array))]
    internal static IReadOnlyList<T>? Filter<T>(IReadOnlyList<T>? array, Func<T, bool> f)
    {
        if (array is not null)
        {
            var len = array.Count;
            var i = 0;
            while (i < len && f(array[i]))
            {
                i++;
            }
            if (i < len)
            {
                var result = Slice(array, 0, i);
                i++;
                while (i < len)
                {
                    var item = array[i];
                    if (f(item))
                    {
                        result.Add(item);
                    }
                    i++;
                }
                return result;
            }
        }
        return array;
    }

    // core.ts:316
    [return: NotNullIfNotNull(nameof(array))]
    internal static List<U>? Map<T, U>(IReadOnlyList<T>? array, Func<T, int, U> f)
    {
        List<U>? result = null;
        if (array is not null)
        {
            result = [];
            for (var i = 0; i < array.Count; i++)
            {
                result.Add(f(array[i], i));
            }
        }
        return result;
    }

    /// <summary>Maps from T to T and avoids allocation if all elements map to themselves</summary>
    // core.ts:342 (the overloads for mutable arrays)
    [return: NotNullIfNotNull(nameof(array))]
    internal static List<T>? SameMap<T>(List<T>? array, Func<T, int, T> f)
    {
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var item = array[i];
                var mapped = f(item, i);
                if (!Equals(item, mapped))
                {
                    var result = array.GetRange(0, i);
                    result.Add(mapped);
                    for (i++; i < array.Count; i++)
                    {
                        result.Add(f(array[i], i));
                    }
                    return result;
                }
            }
        }
        return array;
    }

    // core.ts:344, 348, 350 (the overloads for readonly arrays)
    [return: NotNullIfNotNull(nameof(array))]
    internal static IReadOnlyList<U>? SameMap<T, U>(IReadOnlyList<T>? array, Func<T, int, U> f)
    {
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var item = array[i];
                var mapped = f(item, i);
                if (!Equals(item, mapped))
                {
                    var result = new List<U>(array.Count);
                    for (var k = 0; k < i; k++)
                    {
                        result.Add((U)(object)array[k]!);
                    }
                    result.Add(mapped);
                    for (i++; i < array.Count; i++)
                    {
                        result.Add(f(array[i], i));
                    }
                    return result;
                }
            }
        }
        return (IReadOnlyList<U>?)(object?)array;
    }

    /// <summary>
    /// Maps an array. If the mapped value is an array, it is spread into the result.
    /// The callback returns a single value (<c>U</c>), an array of them (<c>IReadOnlyList&lt;U&gt;</c>) or undefined;
    /// C# has no union types, so the callback's result is tested at run time as <c>isArray(v)</c> is in TypeScript.
    /// </summary>
    // core.ts:399
    internal static IReadOnlyList<U> FlatMap<T, U>(IReadOnlyList<T>? array, Func<T, int, object?> mapfn)
    {
        List<U>? result = null;
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var v = mapfn(array[i], i);
                if (IsTruthy(v))
                {
                    if (v is IReadOnlyList<U> list)
                    {
                        result = AddRange(result, list);
                    }
                    else
                    {
                        result = Append(result, (U)v!);
                    }
                }
            }
        }
        return result ?? EmptyArray<U>();
    }

    // core.ts:494
    internal static List<U> MapDefined<T, U>(IReadOnlyList<T>? array, Func<T, int, U?> mapFn)
    {
        List<U> result = [];
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var mapped = mapFn(array[i], i);
                if (mapped is not null)
                {
                    result.Add(mapped);
                }
            }
        }
        return result;
    }

    // core.ts:614
    internal static bool Some<T>(IReadOnlyList<T>? array) => array is not null && array.Count > 0;

    // core.ts:616
    internal static bool Some<T>(IReadOnlyList<T>? array, Func<T, bool>? predicate)
    {
        if (array is not null)
        {
            if (predicate is not null)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    if (predicate(array[i]))
                    {
                        return true;
                    }
                }
            }
            else
            {
                return array.Count > 0;
            }
        }
        return false;
    }

    // core.ts:656 (the overloads for mutable arrays)
    [return: NotNullIfNotNull(nameof(array1))]
    [return: NotNullIfNotNull(nameof(array2))]
    internal static List<T>? Concatenate<T>(List<T>? array1, List<T>? array2)
    {
        if (array2 is null || array2.Count == 0)
        {
            return array1;
        }
        if (array1 is null || array1.Count == 0)
        {
            return array2;
        }
        List<T> result = new(array1.Count + array2.Count);
        result.AddRange(array1);
        result.AddRange(array2);
        return result;
    }

    // core.ts:658 (the overloads for readonly arrays)
    [return: NotNullIfNotNull(nameof(array1))]
    [return: NotNullIfNotNull(nameof(array2))]
    internal static IReadOnlyList<T>? Concatenate<T>(IReadOnlyList<T>? array1, IReadOnlyList<T>? array2)
    {
        if (array2 is null || array2.Count == 0)
        {
            return array1;
        }
        if (array1 is null || array1.Count == 0)
        {
            return array2;
        }
        List<T> result = new(array1.Count + array2.Count);
        for (var i = 0; i < array1.Count; i++)
        {
            result.Add(array1[i]);
        }
        for (var i = 0; i < array2.Count; i++)
        {
            result.Add(array2[i]);
        }
        return result;
    }

    /// <summary>
    /// Appends a value to an array, returning the array.
    /// If `to` is `undefined`, a new array is created if `value` was appended.
    /// If `value` is `undefined`, nothing is appended.
    /// </summary>
    // core.ts:921
    [return: NotNullIfNotNull(nameof(to))]
    [return: NotNullIfNotNull(nameof(value))]
    internal static List<T>? Append<T>(List<T>? to, T? value)
    {
        if (value is null)
        {
            return to;
        }
        if (to is null)
        {
            return [value];
        }
        to.Add(value);
        return to;
    }

    /// <summary>
    /// Gets the actual offset into an array for a relative offset. Negative offsets indicate a
    /// position offset from the end of the array.
    /// </summary>
    // core.ts:965
    private static int ToOffset<T>(IReadOnlyList<T> array, int offset) => offset < 0 ? array.Count + offset : offset;

    /// <summary>
    /// Appends a range of value to an array, returning the array.
    /// `to`: If `to` is `undefined`, a new array is created if `value` was appended.
    /// `from`: If `from` is `undefined`, nothing is appended. If an element of `from` is `undefined`, that element is not appended.
    /// `start`: The offset in `from` at which to start copying values.
    /// `end`: The offset in `from` at which to stop copying values (non-inclusive).
    /// </summary>
    // core.ts:981
    [return: NotNullIfNotNull(nameof(to))]
    internal static List<T>? AddRange<T>(List<T>? to, IReadOnlyList<T>? from, int? start = null, int? end = null)
    {
        if (from is null || from.Count == 0)
        {
            return to;
        }
        if (to is null)
        {
            return Slice(from, start, end);
        }
        var startOffset = start is null ? 0 : ToOffset(from, start.Value);
        var endOffset = end is null ? from.Count : ToOffset(from, end.Value);
        for (var i = startOffset; i < endOffset && i < from.Count; i++)
        {
            if (from[i] is not null)
            {
                to.Add(from[i]);
            }
        }
        return to;
    }

    /// <summary>Returns whether the value was added.</summary>
    // core.ts:1003
    internal static bool PushIfUnique<T>(List<T> array, T toAdd, Func<T, T, bool>? equalityComparer = null)
    {
        if (Contains(array, toAdd, equalityComparer))
        {
            return false;
        }
        else
        {
            array.Add(toAdd);
            return true;
        }
    }

    /// <summary>Unlike `pushIfUnique`, this can take `undefined` as an input, and returns a new array.</summary>
    // core.ts:1018
    internal static List<T> AppendIfUnique<T>(List<T>? array, T toAdd, Func<T, T, bool>? equalityComparer = null)
    {
        if (array is not null)
        {
            PushIfUnique(array, toAdd, equalityComparer);
            return array;
        }
        else
        {
            return [toAdd];
        }
    }

    /// <summary>
    /// Returns a new sorted array. This sort is stable, meaning elements equal to each other maintain their relative position in the array.
    /// Without a comparer, elements are ordered by their string form, as JavaScript's Array.prototype.sort does.
    /// </summary>
    // core.ts:1038
    internal static IReadOnlyList<T> ToSorted<T>(IReadOnlyList<T> array, Func<T, T, Comparison>? comparer = null)
    {
        if (array.Count == 0)
        {
            return EmptyArray<T>();
        }
        var entries = new (T Item, int Index)[array.Count];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = (array[i], i);
        }
        Array.Sort(entries, (x, y) =>
        {
            // Stable: sort by value, then by position (core.ts:1030 stableSortIndices).
            var c = comparer is not null
                ? (int)comparer(x.Item, y.Item)
                : string.CompareOrdinal(ToJsString(x.Item), ToJsString(y.Item));
            return c != 0 ? c : x.Index.CompareTo(y.Index);
        });
        var result = new T[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            result[i] = entries[i].Item;
        }
        return result;
    }

    /// <summary>Returns the first element of an array if non-empty, `undefined` otherwise.</summary>
    // core.ts:1083
    internal static T? FirstOrUndefined<T>(IReadOnlyList<T>? array) => array is null || array.Count == 0 ? default : array[0];

    /// <summary>Returns the last element of an array if non-empty, `undefined` otherwise.</summary>
    // core.ts:1116
    internal static T? LastOrUndefined<T>(IReadOnlyList<T>? array) => array is null || array.Count == 0 ? default : array[array.Count - 1];

    // core.ts:1121
    internal static T Last<T>(IReadOnlyList<T> array)
    {
        Debug.Assert(array.Count != 0);
        return array[array.Count - 1];
    }

    /// <summary>
    /// Performs a binary search, finding the index at which `value` occurs in `array`.
    /// If no such index is found, returns the 2's-complement of first index at which
    /// `array[index]` exceeds `value`.
    /// </summary>
    // core.ts:1186
    internal static int BinarySearch<T, U>(IReadOnlyList<T> array, T value, Func<T, U> keySelector, Func<U, U, Comparison> keyComparer, int? offset = null)
    {
        return BinarySearchKey(array, keySelector(value), (v, _) => keySelector(v), keyComparer, offset);
    }

    /// <summary>
    /// Performs a binary search, finding the index at which an object with `key` occurs in `array`.
    /// If no such index is found, returns the 2's-complement of first index at which
    /// `array[index]` exceeds `key`.
    /// </summary>
    // core.ts:1202
    internal static int BinarySearchKey<T, U>(IReadOnlyList<T> array, U key, Func<T, int, U> keySelector, Func<U, U, Comparison> keyComparer, int? offset = null)
    {
        if (!Some(array))
        {
            return -1;
        }

        var low = offset ?? 0;
        var high = array.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var midKey = keySelector(array[middle], middle);
            switch (Math.Sign((int)keyComparer(midKey, key)))
            {
                case (int)Comparison.LessThan:
                    low = middle + 1;
                    break;
                case (int)Comparison.EqualTo:
                    return middle;
                case (int)Comparison.GreaterThan:
                    high = middle - 1;
                    break;
            }
        }

        return ~low;
    }

    // core.ts:1783
    internal static TOut Cast<TIn, TOut>(TIn? value, Func<TIn, bool> test)
        where TOut : TIn
    {
        if (value is not null && test(value))
        {
            return (TOut)(object)value;
        }

        return Debug.Fail<TOut>($"Invalid cast. The supplied value {ToJsString(value)} did not pass the test '{Debug.GetFunctionName(test)}'.");
    }

    /// <summary>Does nothing.</summary>
    // core.ts:1794
    internal static void Noop(object? _ = null)
    {
    }

    /// <summary>Returns its argument.</summary>
    // core.ts:1828
    internal static T Identity<T>(T x) => x;

    /// <summary>Throws an error because a function is not implemented.</summary>
    // core.ts:1886
    [DoesNotReturn]
    internal static void NotImplemented() => throw new InvalidOperationException("Not implemented");

    // core.ts:1891
    internal static Func<T> Memoize<T>(Func<T> callback)
    {
        T value = default!;
        Func<T>? cb = callback;
        return () =>
        {
            if (cb is not null)
            {
                value = cb();
                cb = null;
            }
            return value;
        };
    }

    /// <summary>A version of `memoize` that supports a single primitive argument</summary>
    // core.ts:1907
    internal static Func<A, T> MemoizeOne<A, T>(Func<A, T> callback)
    {
        Dictionary<string, T> map = [];
        return arg =>
        {
            var key = $"{JsTypeof(arg)}:{ToJsString(arg)}";
            if (!map.TryGetValue(key, out var value))
            {
                value = callback(arg);
                map[key] = value;
            }
            return value;
        };
    }

    // core.ts:1937
    internal static bool EquateValues<T>(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);

    /// <summary>
    /// Compare the equality of two strings using a case-insensitive ordinal comparison.
    /// Both strings are mapped to their upper-case form, as TypeScript does, because some unicode
    /// characters do not properly round-trip to lowercase (such as the German sharp capital s).
    /// </summary>
    // core.ts:1951
    internal static bool EquateStringsCaseInsensitive(string? a, string? b)
    {
        return a == b
            || (a is not null
                && b is not null
                && string.Equals(a.ToUpperInvariant(), b.ToUpperInvariant(), StringComparison.Ordinal));
    }

    /// <summary>Compare the equality of two strings using a case-sensitive ordinal comparison.</summary>
    // core.ts:1966
    internal static bool EquateStringsCaseSensitive(string a, string b) => EquateValues(a, b);

    // core.ts:1971 (the overload for strings)
    internal static Comparison CompareComparableValues(string? a, string? b)
    {
        return a == b ? Comparison.EqualTo :
            a is null ? Comparison.LessThan :
            b is null ? Comparison.GreaterThan :
            string.CompareOrdinal(a, b) < 0 ? Comparison.LessThan :
            Comparison.GreaterThan;
    }

    // core.ts:1971 (the overload for numbers)
    internal static Comparison CompareComparableValues(double? a, double? b)
    {
        return a == b ? Comparison.EqualTo :
            a is null ? Comparison.LessThan :
            b is null ? Comparison.GreaterThan :
            a.Value < b.Value ? Comparison.LessThan :
            Comparison.GreaterThan;
    }

    /// <summary>Compare two numeric values for their order relative to each other.</summary>
    // core.ts:1988
    internal static Comparison CompareValues(double? a, double? b) => CompareComparableValues(a, b);

    /// <summary>
    /// Given a name and a list of names that are *not* equal to the name, return a spelling suggestion if there is one that is close enough.
    /// Names less than length 3 only check for case-insensitive equality.
    ///
    /// find the candidate with the smallest Levenshtein distance,
    ///    except for candidates:
    ///      * With no name
    ///      * Whose length differs from the target name by more than 0.34 of the length of the name.
    ///      * Whose levenshtein distance is more than 0.4 of the length of the name
    ///        (0.4 allows 1 substitution/transposition for every 5 characters,
    ///         and 1 insertion/deletion at 3 characters)
    /// </summary>
    // core.ts:2169
    internal static T? GetSpellingSuggestion<T>(string name, IEnumerable<T> candidates, Func<T, string?> getName)
    {
        double maximumLengthDifference = Math.Max(2, Math.Floor(name.Length * 0.34));
        double bestDistance = Math.Floor(name.Length * 0.4) + 1; // If the best result is worse than this, don't bother.
        T? bestCandidate = default;
        foreach (var candidate in candidates)
        {
            var candidateName = getName(candidate);
            if (candidateName is not null && Math.Abs(candidateName.Length - name.Length) <= maximumLengthDifference)
            {
                if (candidateName == name)
                {
                    continue;
                }
                // Only consider candidates less than 3 characters long when they differ by case.
                // Otherwise, don't bother, since a user would usually notice differences of a 2-character name.
                if (candidateName.Length < 3 && !string.Equals(candidateName.ToLowerInvariant(), name.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    continue;
                }

                var distance = LevenshteinWithMax(name, candidateName, bestDistance - 0.1);
                if (distance is null)
                {
                    continue;
                }

                Debug.Assert(distance.Value < bestDistance); // Else `levenshteinWithMax` should return undefined
                bestDistance = distance.Value;
                bestCandidate = candidate;
            }
        }
        return bestCandidate;
    }

    // core.ts:2198
    private static double? LevenshteinWithMax(string s1, string s2, double max)
    {
        var previous = new double[s2.Length + 1];
        var current = new double[s2.Length + 1];
        // Represents any value > max. We don't care about the particular value.
        var big = max + 0.01;

        for (var i = 0; i <= s2.Length; i++)
        {
            previous[i] = i;
        }

        for (var i = 1; i <= s1.Length; i++)
        {
            var c1 = s1[i - 1];
            var minJ = (int)Math.Ceiling(i > max ? i - max : 1);
            var maxJ = (int)Math.Floor(s2.Length > max + i ? max + i : s2.Length);
            current[0] = i;
            // Smallest value of the matrix in the ith column.
            double colMin = i;
            for (var j = 1; j < minJ; j++)
            {
                current[j] = big;
            }
            for (var j = minJ; j <= maxJ; j++)
            {
                // case difference should be significantly cheaper than other differences
                var substitutionDistance = char.ToLowerInvariant(s1[i - 1]) == char.ToLowerInvariant(s2[j - 1])
                    ? (previous[j - 1] + 0.1)
                    : (previous[j - 1] + 2);
                var dist = c1 == s2[j - 1]
                    ? previous[j - 1]
                    : Math.Min(/*delete*/ previous[j] + 1, Math.Min(/*insert*/ current[j - 1] + 1, /*substitute*/ substitutionDistance));
                current[j] = dist;
                colMin = Math.Min(colMin, dist);
            }
            for (var j = maxJ + 1; j <= s2.Length; j++)
            {
                current[j] = big;
            }
            if (colMin > max)
            {
                // Give up -- everything in this column is > max and it can't get better in future columns.
                return null;
            }

            (previous, current) = (current, previous);
        }

        var res = previous[s2.Length];
        return res > max ? null : res;
    }

    // core.ts:2247
    internal static bool EndsWith(string str, string suffix, bool ignoreCase = false)
    {
        var expectedPos = str.Length - suffix.Length;
        return expectedPos >= 0 && (
            ignoreCase
                ? EquateStringsCaseInsensitive(str.Substring(expectedPos), suffix)
                : string.CompareOrdinal(str, expectedPos, suffix, 0, suffix.Length) == 0);
    }

    // core.ts:2430
    internal static bool StartsWith(string str, string prefix, bool ignoreCase = false)
    {
        return ignoreCase
            ? EquateStringsCaseInsensitive(str.Substring(0, Math.Min(prefix.Length, str.Length)), prefix)
            : str.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// core.ts:2588. TypeScript tests whether it runs on Node.js (`process.nextTick`, `require`); a C# program never does.
    /// </summary>
    internal static bool IsNodeLikeSystem() => false;
}
