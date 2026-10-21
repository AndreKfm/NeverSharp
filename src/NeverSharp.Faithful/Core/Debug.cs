// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/debug.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/debug/debug.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace NeverSharp.Faithful;

/// <summary>
/// debug.ts: namespace Debug (the assertion part). The `stackCrawlMark` parameters of TypeScript only
/// trim JavaScript stack traces (Error.captureStackTrace) and have no counterpart here.
/// </summary>
internal static partial class Debug
{
    // TypeScript: `let currentAssertionLevel = AssertionLevel.None`; it changes only through setAssertionLevel.
    private static AssertionLevel currentAssertionLevel = AssertionLevel.None;

    internal static bool IsDebugging;

    // TypeScript caches the assertion function it replaces with `noop` (see ShouldAssertFunction). C# methods
    // cannot be replaced, so the cache records the level of each replaced assertion by name, and the
    // assertion consults ShouldAssertFunction on every call, which then returns false just as the replaced
    // function would do nothing.
    private static readonly ConcurrentDictionary<string, AssertionLevel> AssertionCache = new(StringComparer.Ordinal);

    internal static bool ShouldAssert(AssertionLevel level)
    {
        return currentAssertionLevel >= level;
    }

    /// <summary>
    /// Tests whether an assertion function should be executed. If it shouldn't, it is cached and replaced with `ts.noop`.
    /// Replaced assertion functions are restored when `Debug.setAssertionLevel` is set to a high enough level.
    /// </summary>
    /// <param name="level">The minimum assertion level required.</param>
    /// <param name="name">The name of the current assertion function.</param>
    private static bool ShouldAssertFunction(AssertionLevel level, string name)
    {
        if (!ShouldAssert(level))
        {
            AssertionCache.TryAdd(name, level);
            return false;
        }
        return true;
    }

    [DoesNotReturn]
    internal static void Fail()
    {
        Fail(null);
    }

    [DoesNotReturn]
    internal static void Fail(string? message)
    {
        // TypeScript: `debugger;` (breaks only when a debugger is attached).
        if (System.Diagnostics.Debugger.IsAttached)
        {
            System.Diagnostics.Debugger.Break();
        }
        throw new InvalidOperationException(!string.IsNullOrEmpty(message) ? $"Debug Failure. {message}" : "Debug Failure.");
    }

    internal static void Assert([DoesNotReturnIf(false)] bool expression)
    {
        Assert(expression, (string?)null);
    }

    internal static void Assert([DoesNotReturnIf(false)] bool expression, string? message)
    {
        if (!expression)
        {
            AssertFailed(message, null, null);
        }
    }

    internal static void Assert([DoesNotReturnIf(false)] bool expression, string? message, string? verboseDebugInfo)
    {
        if (!expression)
        {
            AssertFailed(message, verboseDebugInfo, null);
        }
    }

    internal static void Assert([DoesNotReturnIf(false)] bool expression, string? message, Func<string>? verboseDebugInfo)
    {
        if (!expression)
        {
            AssertFailed(message, null, verboseDebugInfo);
        }
    }

    // The failing branch of assert; `verboseDebugInfo: string | (() => string)` arrives as one of the two last parameters.
    [DoesNotReturn]
    private static void AssertFailed(string? message, string? verboseDebugInfoText, Func<string>? verboseDebugInfoFunction)
    {
        message = !string.IsNullOrEmpty(message) ? $"False expression: {message}" : "False expression.";
        if (!string.IsNullOrEmpty(verboseDebugInfoText) || verboseDebugInfoFunction != null)
        {
            message += "\r\nVerbose Debug Information: " + (verboseDebugInfoFunction == null ? verboseDebugInfoText : verboseDebugInfoFunction());
        }
        Fail(message);
    }

    internal static void AssertEqual<T>(T a, T b, string? msg = null, string? msg2 = null)
    {
        if (!EqualityComparer<T>.Default.Equals(a, b))
        {
            var message = !string.IsNullOrEmpty(msg) ? !string.IsNullOrEmpty(msg2) ? $"{msg} {msg2}" : msg : "";
            Fail($"Expected {Stringify(a)} === {Stringify(b)}. {message}");
        }
    }

    internal static void AssertLessThanOrEqual(int a, int b)
    {
        if (a > b)
        {
            Fail($"Expected {Stringify(a)} <= {Stringify(b)}");
        }
    }

    internal static void AssertGreaterThanOrEqual(int a, int b)
    {
        if (a < b)
        {
            Fail($"Expected {Stringify(a)} >= {Stringify(b)}");
        }
    }

    internal static void AssertIsDefined<T>([NotNull] T value, string? message = null)
    {
        if (value == null)
        {
            Fail(message);
        }
    }

    internal static T CheckDefined<T>([NotNull] T? value, string? message = null)
    {
        AssertIsDefined(value, message);
        return value;
    }

    internal static T CheckDefined<T>([NotNull] T? value, string? message = null)
        where T : struct
    {
        AssertIsDefined(value, message);
        return value.Value;
    }

    [DoesNotReturn]
    internal static void AssertNever(object? member, string message = "Illegal value:")
    {
        // formatSyntaxKind and JSON.stringify are replaced by plain formatting (see Stringify).
        var detail = member is Node node ? "SyntaxKind: " + node.Kind.ToString() : JsonStringify(member);
        Fail($"{message} {detail}");
    }

    internal static void AssertNode(Node? node, Func<Node, bool>? test, string? message = null)
    {
        if (ShouldAssertFunction(AssertionLevel.Normal, "assertNode"))
        {
            Assert(
                node != null && (test == null || test(node)),
                !string.IsNullOrEmpty(message) ? message : "Unexpected node.",
                () => $"Node {node?.Kind.ToString() ?? "undefined"} did not pass test ''.");
        }
    }

    //// Formatting of values in messages (JavaScript template literals and JSON.stringify).

    /// <summary>A value as JavaScript's template literal `${value}` writes it (for the values ported code formats).</summary>
    internal static string Stringify<T>(T value)
    {
        return value switch
        {
            null => "undefined",
            bool b => b ? "true" : "false",
            string s => s,
            Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
    }

    /// <summary>JSON.stringify for the values assertNever receives (enum members are numbers in TypeScript).</summary>
    private static string JsonStringify(object? value)
    {
        if (value is string s)
        {
            return "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }
        return Stringify(value);
    }
}
