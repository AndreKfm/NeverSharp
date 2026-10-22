// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/core.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/core/core.go and internal/core/binarysearch.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>core.ts: AssertionLevel (a const enum of core.ts, not part of the generated enums).</summary>
internal enum AssertionLevel
{
    None = 0,
    Normal = 1,
    Aggressive = 2,
    VeryAggressive = 3,
}

/// <summary>core.ts.</summary>
internal static partial class Core
{
    // core.ts: emptyArray. TypeScript shares one never[] for every element type; C# arrays are typed,
    // so the shared empty array is per element type (Array.Empty).
    internal static T[] EmptyArray<T>() => [];

    // core.ts: emptyMap is a JavaScript ReadonlyMap constant: not ported (see the attempt report).

    internal static int Length<T>(IReadOnlyList<T>? array)
    {
        return array != null ? array.Count : 0;
    }

    /// <summary>
    /// Iterates through 'array' by index and performs the callback on each element of array until the callback
    /// returns a truthy value, then returns that value.
    /// If no such value is found, the callback is applied to each element of array and undefined is returned.
    /// </summary>
    internal static TResult? ForEach<T, TResult>(IReadOnlyList<T>? array, Func<T, int, TResult?> callback)
    {
        if (array != null)
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
    internal static T? Find<T>(IReadOnlyList<T>? array, Func<T, int, bool> predicate, int? startIndex = null)
    {
        if (array == null)
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
    internal static int FindIndex<T>(IReadOnlyList<T>? array, Func<T, int, bool> predicate, int? startIndex = null)
    {
        if (array == null)
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

    internal static bool Contains<T>(IReadOnlyList<T>? array, T value, Func<T, T, bool>? equalityComparer = null)
    {
        equalityComparer ??= EquateValues;
        if (array != null)
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
    [return: NotNullIfNotNull(nameof(array))]
    internal static List<T>? Filter<T>(List<T>? array, Func<T, bool> f)
    {
        return (List<T>?)Filter((IReadOnlyList<T>?)array, f);
    }

    /// <summary>
    /// Filters an array by a predicate function. Returns the same array instance if the predicate is
    /// true for all elements, otherwise returns a new array instance containing the filtered subset.
    /// </summary>
    [return: NotNullIfNotNull(nameof(array))]
    internal static IReadOnlyList<T>? Filter<T>(IReadOnlyList<T>? array, Func<T, bool> f)
    {
        if (array != null)
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

    [return: NotNullIfNotNull(nameof(array))]
    internal static List<TResult>? Map<T, TResult>(IReadOnlyList<T>? array, Func<T, int, TResult> f)
    {
        List<TResult>? result = null;
        if (array != null)
        {
            result = [];
            for (var i = 0; i < array.Count; i++)
            {
                result.Add(f(array[i], i));
            }
        }
        return result;
    }

    /// <summary>Maps from T to T and avoids allocation if all elements map to themselves.</summary>
    [return: NotNullIfNotNull(nameof(array))]
    internal static List<T>? SameMap<T>(List<T>? array, Func<T, int, T> f)
    {
        return (List<T>?)SameMap((IReadOnlyList<T>?)array, f);
    }

    /// <summary>Maps from T to T and avoids allocation if all elements map to themselves.</summary>
    [return: NotNullIfNotNull(nameof(array))]
    internal static IReadOnlyList<T>? SameMap<T>(IReadOnlyList<T>? array, Func<T, int, T> f)
    {
        if (array != null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var item = array[i];
                var mapped = f(item, i);
                if (!EquateValues(item, mapped))
                {
                    var result = Slice(array, 0, i);
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

    /// <summary>
    /// Maps an array. If the mapped value is an array, it is spread into the result.
    /// (TypeScript's callback returns U | readonly U[] | undefined; C# has no such union, so the
    /// callback returns a list, as the callback of Go's core.FlatMap does.)
    /// </summary>
    internal static IReadOnlyList<TResult> FlatMap<T, TResult>(IReadOnlyList<T>? array, Func<T, int, IReadOnlyList<TResult>?> mapfn)
    {
        List<TResult>? result = null;
        if (array != null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var v = mapfn(array[i], i);
                if (v != null)
                {
                    result = AddRange(result, v);
                }
            }
        }
        return result ?? (IReadOnlyList<TResult>)EmptyArray<TResult>();
    }

    internal static List<TResult> MapDefined<T, TResult>(IReadOnlyList<T>? array, Func<T, int, TResult?> mapFn)
    {
        var result = new List<TResult>();
        if (array != null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var mapped = mapFn(array[i], i);
                if (IsDefined(mapped))
                {
                    result.Add(mapped);
                }
            }
        }
        return result;
    }

    internal static bool Some<T>([NotNullWhen(true)] IReadOnlyList<T>? array)
    {
        return Some(array, null);
    }

    internal static bool Some<T>([NotNullWhen(true)] IReadOnlyList<T>? array, Func<T, bool>? predicate)
    {
        if (array != null)
        {
            if (predicate != null)
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

    [return: NotNullIfNotNull(nameof(array1))]
    [return: NotNullIfNotNull(nameof(array2))]
    internal static List<T>? Concatenate<T>(List<T>? array1, List<T>? array2)
    {
        return (List<T>?)Concatenate((IReadOnlyList<T>?)array1, array2);
    }

    [return: NotNullIfNotNull(nameof(array1))]
    [return: NotNullIfNotNull(nameof(array2))]
    internal static IReadOnlyList<T>? Concatenate<T>(IReadOnlyList<T>? array1, IReadOnlyList<T>? array2)
    {
        if (array2 == null || array2.Count == 0)
        {
            return array1;
        }
        if (array1 == null || array1.Count == 0)
        {
            return array2;
        }
        var result = new List<T>(array1.Count + array2.Count);
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
    [return: NotNullIfNotNull(nameof(to))]
    [return: NotNullIfNotNull(nameof(value))]
    internal static List<T>? Append<T>(List<T>? to, T? value)
        where T : notnull
    {
        if (!IsDefined(value))
        {
            return to;
        }
        if (to == null)
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
    private static int ToOffset<T>(IReadOnlyList<T> array, int offset)
    {
        return offset < 0 ? array.Count + offset : offset;
    }

    /// <summary>
    /// Appends a range of value to an array, returning the array.
    /// </summary>
    /// <param name="to">The array to which `value` is to be appended. If `to` is `undefined`, a new array
    /// is created if `value` was appended.</param>
    /// <param name="from">The values to append to the array. If `from` is `undefined`, nothing is
    /// appended. If an element of `from` is `undefined`, that element is not appended.</param>
    /// <param name="start">The offset in `from` at which to start copying values.</param>
    /// <param name="end">The offset in `from` at which to stop copying values (non-inclusive).</param>
    [return: NotNullIfNotNull(nameof(to))]
    internal static List<T>? AddRange<T>(List<T>? to, IReadOnlyList<T>? from, int? start = null, int? end = null)
    {
        if (from == null || from.Count == 0)
        {
            return to;
        }
        if (to == null)
        {
            return Slice(from, start, end);
        }
        var startIndex = start == null ? 0 : ToOffset(from, start.Value);
        var endIndex = end == null ? from.Count : ToOffset(from, end.Value);
        for (var i = startIndex; i < endIndex && i < from.Count; i++)
        {
            if (IsDefined(from[i]))
            {
                to.Add(from[i]);
            }
        }
        return to;
    }

    /// <returns>Whether the value was added.</returns>
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
    internal static List<T> AppendIfUnique<T>(List<T>? array, T toAdd, Func<T, T, bool>? equalityComparer = null)
    {
        if (array != null)
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
    /// </summary>
    internal static IReadOnlyList<T> ToSorted<T>(IReadOnlyList<T> array, Func<T, T, Comparison>? comparer = null)
    {
        return array.Count == 0 ? EmptyArray<T>() : SortArray(Slice(array, null, null), comparer);
    }

    internal static T? FirstOrUndefined<T>(IReadOnlyList<T>? array)
    {
        return array == null || array.Count == 0 ? default : array[0];
    }

    /// <summary>Returns the last element of an array if non-empty, `undefined` otherwise.</summary>
    internal static T? LastOrUndefined<T>(IReadOnlyList<T>? array)
    {
        return array == null || array.Count == 0 ? default : array[array.Count - 1];
    }

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
    /// <param name="array">A sorted array whose first element must be no larger than number</param>
    /// <param name="value">The value to be searched for in the array.</param>
    /// <param name="keySelector">A callback used to select the search key from `value` and each element of
    /// `array`.</param>
    /// <param name="keyComparer">A callback used to compare two keys in a sorted array.</param>
    /// <param name="offset">An offset into `array` at which to start the search.</param>
    internal static int BinarySearch<T, TKey>(IReadOnlyList<T> array, T value, Func<T, TKey> keySelector, Func<TKey, TKey, Comparison> keyComparer, int? offset = null)
    {
        return BinarySearchKey(array, keySelector(value), (v, _) => keySelector(v), keyComparer, offset);
    }

    /// <summary>
    /// Performs a binary search, finding the index at which an object with `key` occurs in `array`.
    /// If no such index is found, returns the 2's-complement of first index at which
    /// `array[index]` exceeds `key`.
    /// </summary>
    /// <param name="array">A sorted array whose first element must be no larger than number</param>
    /// <param name="key">The key to be searched for in the array.</param>
    /// <param name="keySelector">A callback used to select the search key from each element of `array`.</param>
    /// <param name="keyComparer">A callback used to compare two keys in a sorted array.</param>
    /// <param name="offset">An offset into `array` at which to start the search.</param>
    internal static int BinarySearchKey<T, TKey>(IReadOnlyList<T> array, TKey key, Func<T, int, TKey> keySelector, Func<TKey, TKey, Comparison> keyComparer, int? offset = null)
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
            switch ((Comparison)Math.Sign((int)keyComparer(midKey, key)))
            {
                case Comparison.LessThan:
                    low = middle + 1;
                    break;
                case Comparison.EqualTo:
                    return middle;
                case Comparison.GreaterThan:
                    high = middle - 1;
                    break;
            }
        }

        return ~low;
    }

    // core.ts: hasOwnProperty, hasProperty, isArray and isString test JavaScript values: not ported (see the attempt report).

    // core.ts: toArray(value: T | T[]). The union is resolved by overloads instead of isArray.
    internal static List<T> ToArray<T>(T value)
    {
        return [value];
    }

    internal static List<T> ToArray<T>(List<T> value)
    {
        return value;
    }

    internal static IReadOnlyList<T> ToArray<T>(IReadOnlyList<T> value)
    {
        return value;
    }

    internal static TOut Cast<TOut, TIn>(TIn? value, Func<TIn, bool> test)
        where TOut : TIn
    {
        if (IsDefined(value) && test(value))
        {
            return (TOut)(object)value;
        }

        // Debug.getFunctionName needs the function's runtime name (reflection in C#): left empty.
        Debug.Fail($"Invalid cast. The supplied value {Debug.Stringify(value)} did not pass the test ''.");
        return default!;
    }

    /// <summary>Does nothing.</summary>
    internal static void Noop(object? _ = null)
    {
    }

    /// <summary>Returns its argument.</summary>
    internal static T Identity<T>(T x)
    {
        return x;
    }

    /// <summary>Throws an error because a function is not implemented.</summary>
    [DoesNotReturn]
    internal static void NotImplemented()
    {
        throw new NotImplementedException("Not implemented");
    }

    internal static Func<T> Memoize<T>(Func<T> callback)
    {
        T value = default!;
        Func<T>? pending = callback;
        return () =>
        {
            if (pending != null)
            {
                value = pending();
                pending = null;
            }
            return value;
        };
    }

    /// <summary>A version of `memoize` that supports a single primitive argument.</summary>
    internal static Func<TArg, T> MemoizeOne<TArg, T>(Func<TArg, T> callback)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        return arg =>
        {
            var key = JsTypeOf(arg) + ":" + Debug.Stringify(arg);
            if (!map.TryGetValue(key, out var value))
            {
                value = callback(arg);
                map[key] = value;
            }
            return value;
        };
    }

    internal static bool EquateValues<T>(T a, T b)
    {
        return EqualityComparer<T>.Default.Equals(a, b);
    }

    /// <summary>
    /// Compare the equality of two strings using a case-sensitive ordinal comparison.
    ///
    /// Case-sensitive comparisons compare both strings one code-point at a time using the integer
    /// value of each code-point after applying `toUpperCase` to each string. We always map both
    /// strings to their upper-case form as some unicode characters do not properly round-trip to
    /// lowercase (such as `ẞ` (German sharp capital s)).
    /// </summary>
    internal static bool EquateStringsCaseInsensitive(string? a, string? b)
    {
        return a == b
            || a != null
                && b != null
                && string.Equals(a.ToUpperInvariant(), b.ToUpperInvariant(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Compare the equality of two strings using a case-sensitive ordinal comparison.
    ///
    /// Case-sensitive comparisons compare both strings one code-point at a time using the
    /// integer value of each code-point.
    /// </summary>
    internal static bool EquateStringsCaseSensitive(string? a, string? b)
    {
        return EquateValues(a, b);
    }

    internal static Comparison CompareComparableValues(string? a, string? b)
    {
        return a == b ? Comparison.EqualTo :
            a == null ? Comparison.LessThan :
            b == null ? Comparison.GreaterThan :
            string.CompareOrdinal(a, b) < 0 ? Comparison.LessThan :
            Comparison.GreaterThan;
    }

    internal static Comparison CompareComparableValues(int? a, int? b)
    {
        return a == b ? Comparison.EqualTo :
            a == null ? Comparison.LessThan :
            b == null ? Comparison.GreaterThan :
            a < b ? Comparison.LessThan :
            Comparison.GreaterThan;
    }

    internal static Comparison CompareComparableValues(double? a, double? b)
    {
        return a == b ? Comparison.EqualTo :
            a == null ? Comparison.LessThan :
            b == null ? Comparison.GreaterThan :
            a < b ? Comparison.LessThan :
            Comparison.GreaterThan;
    }

    /// <summary>
    /// Compare two numeric values for their order relative to each other.
    /// To compare strings, use any of the `compareStrings` functions.
    /// </summary>
    internal static Comparison CompareValues(int? a, int? b)
    {
        return CompareComparableValues(a, b);
    }

    /// <summary>
    /// Compare two numeric values for their order relative to each other.
    /// To compare strings, use any of the `compareStrings` functions.
    /// </summary>
    internal static Comparison CompareValues(double? a, double? b)
    {
        return CompareComparableValues(a, b);
    }

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
    internal static T? GetSpellingSuggestion<T>(string name, IEnumerable<T> candidates, Func<T, string?> getName)
    {
        var maximumLengthDifference = Math.Max(2, Math.Floor(name.Length * 0.34));
        var bestDistance = Math.Floor(name.Length * 0.4) + 1; // If the best result is worse than this, don't bother.
        T? bestCandidate = default;
        foreach (var candidate in candidates)
        {
            var candidateName = getName(candidate);
            if (candidateName != null && Math.Abs(candidateName.Length - name.Length) <= maximumLengthDifference)
            {
                if (candidateName == name)
                {
                    continue;
                }
                // Only consider candidates less than 3 characters long when they differ by case.
                // Otherwise, don't bother, since a user would usually notice differences of a 2-character name.
                if (candidateName.Length < 3 && !string.Equals(ToLowerCase(candidateName), ToLowerCase(name), StringComparison.Ordinal))
                {
                    continue;
                }

                var distance = LevenshteinWithMax(name, candidateName, bestDistance - 0.1);
                if (distance == null)
                {
                    continue;
                }

                Debug.Assert(distance < bestDistance); // Else `levenshteinWithMax` should return undefined
                bestDistance = distance.Value;
                bestCandidate = candidate;
            }
        }
        return bestCandidate;
    }

    private static double? LevenshteinWithMax(string s1, string s2, double max)
    {
        var previous = new double[s2.Length + 1];
        var current = new double[s2.Length + 1];
        /** Represents any value > max. We don't care about the particular value. */
        var big = max + 0.01;

        for (var i = 0; i <= s2.Length; i++)
        {
            previous[i] = i;
        }

        for (var i = 1; i <= s1.Length; i++)
        {
            int c1 = s1[i - 1];
            var minJ = (int)Math.Ceiling(i > max ? i - max : 1);
            var maxJ = (int)Math.Floor(s2.Length > max + i ? max + i : s2.Length);
            current[0] = i;
            /** Smallest value of the matrix in the ith column. */
            double colMin = i;
            for (var j = 1; j < minJ; j++)
            {
                current[j] = big;
            }
            for (var j = minJ; j <= maxJ; j++)
            {
                // case difference should be significantly cheaper than other differences
                var substitutionDistance = ToLowerCase(s1[i - 1]) == ToLowerCase(s2[j - 1])
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

            (current, previous) = (previous, current);
        }

        var res = previous[s2.Length];
        return res > max ? null : res;
    }

    internal static bool EndsWith(string str, string suffix, bool ignoreCase = false)
    {
        var expectedPos = str.Length - suffix.Length;
        return expectedPos >= 0 && (
            ignoreCase
                ? EquateStringsCaseInsensitive(str[expectedPos..], suffix)
                : str.IndexOf(suffix, expectedPos, StringComparison.Ordinal) == expectedPos
        );
    }

    internal static bool StartsWith(string str, string prefix, bool ignoreCase = false)
    {
        // `str.lastIndexOf(prefix, 0) === 0` tests for prefix at position 0 only, which is an ordinal StartsWith.
        return ignoreCase
            ? EquateStringsCaseInsensitive(str[..Math.Min(prefix.Length, str.Length)], prefix)
            : str.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// core.ts tests for Node.js (`process`, `require`). A .NET process is not a Node-like system.
    /// </summary>
    internal static bool IsNodeLikeSystem()
    {
        return false;
    }

    //// JavaScript semantics the ported functions rely on.

    /// <summary>`value !== undefined` (and not null) for a generic value; Track O: a node handle is undefined when it is None.</summary>
    private static bool IsDefined<T>([NotNullWhen(true)] T? value)
    {
        return value is not null && !(value is Node node && node.IsNone);
    }

    /// <summary>JavaScript truthiness of a value (`if (value)`): undefined, null, false, 0, NaN and "" are falsy.</summary>
    private static bool IsTruthy<T>(T value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            string s => s.Length != 0,
            int i => i != 0,
            double d => d != 0 && !double.IsNaN(d),
            Node n => !n.IsNone, // Track O: a node handle; None is undefined
            NodeList l => !l.IsNone, // Track O (O7a): a list handle; None is undefined
            _ => true,
        };
    }

    /// <summary>JavaScript's Array.prototype.slice(start, end) for an array: negative offsets count from the end.</summary>
    private static List<T> Slice<T>(IReadOnlyList<T> array, int? start, int? end)
    {
        var length = array.Count;
        var from = start == null ? 0 : start.Value < 0 ? Math.Max(length + start.Value, 0) : Math.Min(start.Value, length);
        var to = end == null ? length : end.Value < 0 ? Math.Max(length + end.Value, 0) : Math.Min(end.Value, length);
        var result = new List<T>(Math.Max(to - from, 0));
        for (var i = from; i < to; i++)
        {
            result.Add(array[i]);
        }
        return result;
    }

    /// <summary>
    /// JavaScript's Array.prototype.sort, which is stable; without a comparer it compares the elements'
    /// string forms by UTF-16 code units and puts undefined last. Stability is kept as core.ts's
    /// stableSortIndices does: by sorting indices and comparing positions on ties.
    /// </summary>
    private static List<T> SortArray<T>(List<T> array, Func<T, T, Comparison>? comparer)
    {
        var indices = new int[array.Count];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }
        Array.Sort(indices, (x, y) =>
        {
            var result = comparer != null ? (int)comparer(array[x], array[y]) : CompareDefault(array[x], array[y]);
            return result != 0 ? result : (int)CompareValues(x, y);
        });
        var sorted = new List<T>(array.Count);
        for (var i = 0; i < indices.Length; i++)
        {
            sorted.Add(array[indices[i]]);
        }
        return sorted;
    }

    private static int CompareDefault<T>(T a, T b)
    {
        if (a == null)
        {
            return b == null ? 0 : 1;
        }
        if (b == null)
        {
            return -1;
        }
        return string.CompareOrdinal(Debug.Stringify(a), Debug.Stringify(b));
    }

    /// <summary>JavaScript's `typeof` for the primitive arguments memoizeOne accepts.</summary>
    private static string JsTypeOf<T>(T value)
    {
        return value switch
        {
            null => "undefined",
            string => "string",
            bool => "boolean",
            int or long or double or float or short or byte or sbyte or ushort or uint or ulong or decimal => "number",
            _ => "object",
        };
    }

    /// <summary>JavaScript's String.prototype.toLowerCase (invariant, as JavaScript's is locale-independent).</summary>
    private static string ToLowerCase(string text)
    {
        return text.ToLowerInvariant();
    }

    /// <summary>`s[i].toLowerCase()` on a one-character string, compared as a character.</summary>
    private static char ToLowerCase(char c)
    {
        return char.ToLowerInvariant(c);
    }
}

/// <summary>
/// core.ts array helpers over the elements of a node list (Track O, step O7a: a NodeArray's elements are a span of
/// the list store).
/// </summary>
internal static partial class Core
{
    internal static TResult? ForEach<T, TResult>(ReadOnlySpan<T> array, Func<T, int, TResult?> callback)
    {
        for (var i = 0; i < array.Length; i++)
        {
            var result = callback(array[i], i);
            if (IsTruthy(result))
            {
                return result;
            }
        }
        return default;
    }

    internal static bool Some<T>(ReadOnlySpan<T> array)
    {
        return array.Length > 0;
    }

    internal static bool Some<T>(ReadOnlySpan<T> array, Func<T, bool>? predicate)
    {
        if (predicate == null)
        {
            return array.Length > 0;
        }
        foreach (var element in array)
        {
            if (predicate(element))
            {
                return true;
            }
        }
        return false;
    }

    internal static T? FirstOrUndefined<T>(ReadOnlySpan<T> array)
    {
        return array.Length == 0 ? default : array[0];
    }

    /// <summary>Returns the last element of an array if non-empty, `undefined` otherwise.</summary>
    internal static T? LastOrUndefined<T>(ReadOnlySpan<T> array)
    {
        return array.Length == 0 ? default : array[^1];
    }
}
