// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/core.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/core/core.go
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

/// <summary>
/// core.ts. TypeScript's arrays map to <see cref="IReadOnlyList{T}"/> (readonly) and <see cref="List{T}"/>
/// (mutable); <c>undefined</c> maps to <c>null</c>. Overloads that differ only by a type guard
/// (<c>x is U</c>) have no C# counterpart and are folded into the boolean form.
/// </summary>
internal static partial class Core
{
    /// <summary>core.ts: <c>export const emptyArray: never[] = [] as never[];</c> (one shared instance per element type).</summary>
    internal static IReadOnlyList<T> EmptyArray<T>() => Array.Empty<T>();

    internal static int Length<T>(IReadOnlyList<T>? array)
    {
        return array is not null ? array.Count : 0;
    }

    /// <summary>
    /// Iterates through 'array' by index and performs the callback on each element of array until the callback
    /// returns a truthy value, then returns that value.
    /// If no such value is found, the callback is applied to each element of array and undefined is returned.
    /// </summary>
    internal static U? ForEach<T, U>(IReadOnlyList<T>? array, Func<T, int, U?> callback)
        where U : class
    {
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var result = callback(array[i], i);
                if (result is not null)
                {
                    return result;
                }
            }
        }
        return null;
    }

    /// <summary>core.ts: <c>forEach</c> with a boolean callback (truthy is <c>true</c>; undefined is <c>false</c>).</summary>
    internal static bool ForEach<T>(IReadOnlyList<T>? array, Func<T, int, bool> callback)
    {
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var result = callback(array[i], i);
                if (result)
                {
                    return result;
                }
            }
        }
        return false;
    }

    /// <summary>Works like Array.prototype.find, returning <c>undefined</c> if no element satisfying the predicate is found.</summary>
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

    /// <summary>Works like Array.prototype.findIndex, returning <c>-1</c> if no element satisfying the predicate is found.</summary>
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
    [return: NotNullIfNotNull(nameof(array))]
    internal static List<T>? Filter<T>(List<T>? array, Func<T, bool> f)
    {
        // The result is `array` itself or a new List (see the readonly overload).
        return (List<T>?)Filter((IReadOnlyList<T>?)array, f);
    }

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

    /// <summary>Maps from T to T and avoids allocation if all elements map to themselves.</summary>
    [return: NotNullIfNotNull(nameof(array))]
    internal static List<T>? SameMap<T>(List<T>? array, Func<T, int, T> f)
    {
        // The result is `array` itself or a new List (see the readonly overload).
        return (List<T>?)SameMap<T, T>(array, f);
    }

    /// <summary>
    /// Maps from T to T and avoids allocation if all elements map to themselves. TypeScript's
    /// <c>U = T</c> is cast to <c>U[]</c> unchecked; C# requires <c>T</c> to convert to <c>U</c>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(array))]
    internal static IReadOnlyList<U>? SameMap<T, U>(IReadOnlyList<T>? array, Func<T, int, U> f)
        where T : U
    {
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var item = array[i];
                var mapped = f(item, i);
                if (!EquateValues<U>(item, mapped))
                {
                    var result = new List<U>(array.Count);
                    for (var k = 0; k < i; k++)
                    {
                        result.Add(array[k]);
                    }
                    result.Add(mapped);
                    for (i++; i < array.Count; i++)
                    {
                        result.Add(f(array[i], i));
                    }
                    return result;
                }
            }
            return array as IReadOnlyList<U> ?? CopyAs<T, U>(array);
        }
        return null;
    }

    // `array as unknown[] as U[]` when every element mapped to itself: the same instance where the CLR's
    // array variance allows it (reference types), otherwise an element-wise copy (value types).
    private static List<U> CopyAs<T, U>(IReadOnlyList<T> array)
        where T : U
    {
        var result = new List<U>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            result.Add(array[i]);
        }
        return result;
    }

    /// <summary>
    /// Maps an array. If the mapped value is an array, it is spread into the result.
    /// TypeScript's callback returns <c>U | readonly U[] | undefined</c>; a C# callback returns the list
    /// form (a single value as a one-element list), which takes TypeScript's <c>isArray</c> branch.
    /// </summary>
    /// <param name="array">The array to map.</param>
    /// <param name="mapfn">The callback used to map the result into one or more values.</param>
    internal static IReadOnlyList<U> FlatMap<T, U>(IReadOnlyList<T>? array, Func<T, int, IReadOnlyList<U>?> mapfn)
        where U : notnull
    {
        List<U>? result = null;
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var v = mapfn(array[i], i);
                if (v is not null)
                {
                    result = AddRange(result, v);
                }
            }
        }
        return result ?? EmptyArray<U>();
    }

    internal static List<U> MapDefined<T, U>(IReadOnlyList<T>? array, Func<T, int, U?> mapFn)
    {
        var result = new List<U>();
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

    internal static bool Some<T>([NotNullWhen(true)] IReadOnlyList<T>? array)
    {
        if (array is not null)
        {
            return array.Count > 0;
        }
        return false;
    }

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

    [return: NotNullIfNotNull(nameof(array1))]
    [return: NotNullIfNotNull(nameof(array2))]
    internal static List<T>? Concatenate<T>(List<T>? array1, List<T>? array2)
    {
        // The result is one of the arguments or a new List (see the readonly overload).
        return (List<T>?)Concatenate((IReadOnlyList<T>?)array1, array2);
    }

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
        // [...array1, ...array2]
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
    /// </summary>
    /// <param name="to">The array to which <paramref name="value"/> is to be appended. If <paramref name="to"/> is <c>undefined</c>, a new array
    /// is created if <paramref name="value"/> was appended.</param>
    /// <param name="value">The value to append to the array. If <paramref name="value"/> is <c>undefined</c>, nothing is
    /// appended.</param>
    [return: NotNullIfNotNull(nameof(to))]
    [return: NotNullIfNotNull(nameof(value))]
    internal static List<T>? Append<T>(List<T>? to, T? value)
        where T : notnull
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
    private static int ToOffset<T>(IReadOnlyList<T> array, int offset)
    {
        return offset < 0 ? array.Count + offset : offset;
    }

    /// <summary>
    /// Appends a range of value to an array, returning the array.
    /// </summary>
    /// <param name="to">The array to which <c>value</c> is to be appended. If <paramref name="to"/> is <c>undefined</c>, a new array
    /// is created if <c>value</c> was appended.</param>
    /// <param name="from">The values to append to the array. If <paramref name="from"/> is <c>undefined</c>, nothing is
    /// appended. If an element of <paramref name="from"/> is <c>undefined</c>, that element is not appended.</param>
    /// <param name="start">The offset in <paramref name="from"/> at which to start copying values.</param>
    /// <param name="end">The offset in <paramref name="from"/> at which to stop copying values (non-inclusive).</param>
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

    /// <summary>Unlike <c>pushIfUnique</c>, this can take <c>undefined</c> as an input, and returns a new array.</summary>
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
    /// </summary>
    internal static IReadOnlyList<T> ToSorted<T>(IReadOnlyList<T> array, Func<T, T, Comparison>? comparer = null)
    {
        return array.Count == 0 ? EmptyArray<T>() : ArraySort(Slice(array, null, null), comparer);
    }

    internal static T? FirstOrUndefined<T>(IReadOnlyList<T>? array)
    {
        return array is null || array.Count == 0 ? default : array[0];
    }

    /// <summary>Returns the last element of an array if non-empty, <c>undefined</c> otherwise.</summary>
    internal static T? LastOrUndefined<T>(IReadOnlyList<T>? array)
    {
        return array is null || array.Count == 0 ? default : array[array.Count - 1];
    }

    internal static T Last<T>(IReadOnlyList<T> array)
    {
        Debug.Assert(array.Count != 0);
        return array[array.Count - 1];
    }

    /// <summary>
    /// Performs a binary search, finding the index at which <paramref name="value"/> occurs in <paramref name="array"/>.
    /// If no such index is found, returns the 2's-complement of first index at which
    /// <c>array[index]</c> exceeds <paramref name="value"/>.
    /// </summary>
    /// <param name="array">A sorted array whose first element must be no larger than number</param>
    /// <param name="value">The value to be searched for in the array.</param>
    /// <param name="keySelector">A callback used to select the search key from <paramref name="value"/> and each element of
    /// <paramref name="array"/>.</param>
    /// <param name="keyComparer">A callback used to compare two keys in a sorted array.</param>
    /// <param name="offset">An offset into <paramref name="array"/> at which to start the search.</param>
    internal static int BinarySearch<T, U>(IReadOnlyList<T> array, T value, Func<T, U> keySelector, Func<U, U, Comparison> keyComparer, int? offset = null)
    {
        return BinarySearchKey(array, keySelector(value), (v, _) => keySelector(v), keyComparer, offset);
    }

    /// <summary>
    /// Performs a binary search, finding the index at which an object with <paramref name="key"/> occurs in <paramref name="array"/>.
    /// If no such index is found, returns the 2's-complement of first index at which
    /// <c>array[index]</c> exceeds <paramref name="key"/>.
    /// </summary>
    /// <param name="array">A sorted array whose first element must be no larger than number</param>
    /// <param name="key">The key to be searched for in the array.</param>
    /// <param name="keySelector">A callback used to select the search key from each element of <paramref name="array"/>.</param>
    /// <param name="keyComparer">A callback used to compare two keys in a sorted array.</param>
    /// <param name="offset">An offset into <paramref name="array"/> at which to start the search.</param>
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

    /// <summary>core.ts: <c>toArray(value: T | T[])</c> for an array: the array itself.</summary>
    internal static List<T> ToArray<T>(List<T> value)
    {
        return value;
    }

    /// <summary>core.ts: <c>toArray(value: T | readonly T[])</c> for a readonly array: the array itself.</summary>
    internal static IReadOnlyList<T> ToArray<T>(IReadOnlyList<T> value)
    {
        return value;
    }

    /// <summary>core.ts: <c>toArray(value: T | T[])</c> for a single value: <c>[value]</c>.</summary>
    internal static List<T> ToArray<T>(T value)
    {
        return [value];
    }

    internal static TOut Cast<TOut, TIn>(TIn? value, Func<TIn, bool> test)
        where TOut : TIn
    {
        if (value is not null && test(value))
        {
            return (TOut)value;
        }

        // Debug.getFunctionName(test): a function has no name without reflection (as for anonymous functions).
        Debug.Fail("Invalid cast. The supplied value " + TemplateString(value) + " did not pass the test ''.");
        return default!; // unreachable: Debug.Fail throws
    }

    /// <summary>Does nothing.</summary>
    internal static void Noop(object? _ = null)
    {
    }

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
            if (pending is not null)
            {
                value = pending();
                pending = null;
            }
            return value;
        };
    }

    /// <summary>A version of <c>memoize</c> that supports a single primitive argument.</summary>
    internal static Func<TArg, T> MemoizeOne<TArg, T>(Func<TArg, T> callback)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        return (TArg arg) =>
        {
            var key = TypeOf(arg) + ":" + TemplateString(arg);
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
    /// Compare the equality of two strings using a case-insensitive ordinal comparison.
    ///
    /// Case-insensitive comparisons compare both strings one code-point at a time using the integer
    /// value of each code-point after applying <c>toUpperCase</c> to each string. We always map both
    /// strings to their upper-case form as some unicode characters do not properly round-trip to
    /// lowercase (such as <c>ẞ</c> (German sharp capital s)).
    /// </summary>
    internal static bool EquateStringsCaseInsensitive(string? a, string? b)
    {
        return a == b
            || (a is not null
                && b is not null
                && ToUpperCase(a) == ToUpperCase(b));
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
            a is null ? Comparison.LessThan :
            b is null ? Comparison.GreaterThan :
            string.CompareOrdinal(a, b) < 0 ? Comparison.LessThan :
            Comparison.GreaterThan;
    }

    internal static Comparison CompareComparableValues(double? a, double? b)
    {
        return a == b ? Comparison.EqualTo :
            a is null ? Comparison.LessThan :
            b is null ? Comparison.GreaterThan :
            a < b ? Comparison.LessThan :
            Comparison.GreaterThan;
    }

    /// <summary>
    /// Compare two numeric values for their order relative to each other.
    /// To compare strings, use any of the <c>compareStrings</c> functions.
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
            if (candidateName is not null && Math.Abs(candidateName.Length - name.Length) <= maximumLengthDifference)
            {
                if (candidateName == name)
                {
                    continue;
                }
                // Only consider candidates less than 3 characters long when they differ by case.
                // Otherwise, don't bother, since a user would usually notice differences of a 2-character name.
                if (candidateName.Length < 3 && ToLowerCase(candidateName) != ToLowerCase(name))
                {
                    continue;
                }

                var distance = LevenshteinWithMax(name, candidateName, bestDistance - 0.1);
                if (distance is null)
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
        // Represents any value > max. We don't care about the particular value.
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
            // Smallest value of the matrix in the ith column.
            double colMin = i;
            for (var j = 1; j < minJ; j++)
            {
                current[j] = big;
            }
            for (var j = minJ; j <= maxJ; j++)
            {
                // case difference should be significantly cheaper than other differences
                var substitutionDistance = CharToLowerCase(s1[i - 1]) == CharToLowerCase(s2[j - 1])
                    ? (previous[j - 1] + 0.1)
                    : (previous[j - 1] + 2);
                var dist = c1 == s2[j - 1]
                    ? previous[j - 1]
                    : Math.Min(Math.Min(/*delete*/ previous[j] + 1, /*insert*/ current[j - 1] + 1), /*substitute*/ substitutionDistance);
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

            var temp = previous;
            previous = current;
            current = temp;
        }

        var res = previous[s2.Length];
        return res > max ? null : res;
    }

    internal static bool EndsWith(string str, string suffix, bool ignoreCase = false)
    {
        var expectedPos = str.Length - suffix.Length;
        return expectedPos >= 0 && (
            ignoreCase
                ? EquateStringsCaseInsensitive(str.Substring(expectedPos), suffix)
                : str.EndsWith(suffix, StringComparison.Ordinal));
    }

    internal static bool StartsWith(string str, string prefix, bool ignoreCase = false)
    {
        return ignoreCase
            ? EquateStringsCaseInsensitive(Slice(str, 0, prefix.Length), prefix)
            : str.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// core.ts: whether the host is Node.js (<c>typeof process !== "undefined" &amp;&amp; …</c>). The C# port
    /// does not run on a JavaScript host, where TypeScript's test is <c>false</c>.
    /// </summary>
    internal static bool IsNodeLikeSystem()
    {
        return false;
    }

    // ---- JavaScript built-ins the units above call (Array.prototype.slice/sort, String.prototype case mapping,
    //      typeof, template literals). Not units of core.ts; kept private to this file. ----

    // Array.prototype.slice(start, end): negative offsets count from the end, the result is a new array.
    private static List<T> Slice<T>(IReadOnlyList<T> array, int? start, int? end)
    {
        var length = array.Count;
        var from = start is null ? 0 : start.Value < 0 ? Math.Max(length + start.Value, 0) : Math.Min(start.Value, length);
        var to = end is null ? length : end.Value < 0 ? Math.Max(length + end.Value, 0) : Math.Min(end.Value, length);
        var result = new List<T>(Math.Max(to - from, 0));
        for (var i = from; i < to; i++)
        {
            result.Add(array[i]);
        }
        return result;
    }

    // String.prototype.slice(start, end) for non-negative offsets.
    private static string Slice(string str, int start, int end)
    {
        var from = Math.Min(start, str.Length);
        var to = Math.Min(end, str.Length);
        return from < to ? str.Substring(from, to - from) : "";
    }

    // Array.prototype.sort(comparer): stable; undefined elements go last without calling the comparer; without
    // a comparer, elements compare by their string conversion (UTF-16 code units). The stable order is
    // obtained as in core.ts stableSortIndices: sort indices by value, then by position.
    private static List<T> ArraySort<T>(List<T> array, Func<T, T, Comparison>? comparer)
    {
        var defined = new List<T>(array.Count);
        var undefinedCount = 0;
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is null)
            {
                undefinedCount++;
            }
            else
            {
                defined.Add(array[i]);
            }
        }
        var indices = new int[defined.Count];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }
        Comparison Compare(T x, T y) => comparer is not null
            ? comparer(x, y)
            : (Comparison)Math.Sign(string.CompareOrdinal(TemplateString(x), TemplateString(y)));
        Array.Sort(indices, (x, y) =>
        {
            var c = (int)Compare(defined[x], defined[y]);
            return c != 0 ? c : (int)CompareValues(x, y);
        });
        array.Clear();
        for (var i = 0; i < indices.Length; i++)
        {
            array.Add(defined[indices[i]]);
        }
        for (var i = 0; i < undefinedCount; i++)
        {
            array.Add(default!);
        }
        return array;
    }

    // String.prototype.toUpperCase/toLowerCase. JavaScript applies the full Unicode case mapping
    // (SpecialCasing.txt, e.g. "ß" to "SS"); .NET's invariant mapping is the simple one-to-one mapping.
    // They agree on every character that maps to a single character.
    private static string ToUpperCase(string s) => s.ToUpperInvariant();

    private static string ToLowerCase(string s) => s.ToLowerInvariant();

    private static char CharToLowerCase(char c) => char.ToLowerInvariant(c);

    // `typeof value` for the primitive arguments memoizeOne accepts.
    private static string TypeOf(object? value) => value switch
    {
        null => "undefined",
        string => "string",
        bool => "boolean",
        _ => "number",
    };

    // `${value}` of a JavaScript template literal for primitive values.
    private static string TemplateString(object? value) => value switch
    {
        null => "undefined",
        bool b => b ? "true" : "false",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
