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
using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// debug.ts: <c>namespace Debug</c>, the assertion part. The formatting helpers and the debug-info
/// attachment are not ported (docs/modules/M1-units.md); failures are plain exceptions whose
/// message is TypeScript's. TypeScript's <c>stackCrawlMark</c> parameters only steer
/// <c>Error.captureStackTrace</c> and have no C# counterpart; .NET exceptions record their own stack.
/// </summary>
internal static partial class Debug
{
    // debug.ts: `let currentAssertionLevel = AssertionLevel.None;` (written only by setAssertionLevel, not in this slice).
    private static AssertionLevel CurrentAssertionLevel { get; set; }

    /// <summary>debug.ts: <c>export let isDebugging = false;</c></summary>
    internal static bool IsDebugging { get; set; }

    // debug.ts: `const assertionCache: Partial<Record<AssertionKeys, { level; assertion }>> = {};`
    // TypeScript replaces a cached assertion function by `noop`; C# methods cannot be replaced, so the
    // cache records the level and the replaced function checks its entry (see ShouldAssertFunction).
    private static readonly ConcurrentDictionary<string, AssertionLevel> AssertionCache = new(StringComparer.Ordinal);

    internal static bool ShouldAssert(AssertionLevel level)
    {
        return CurrentAssertionLevel >= level;
    }

    /// <summary>
    /// Tests whether an assertion function should be executed. If it shouldn't, it is cached (in TypeScript
    /// also replaced with <c>ts.noop</c>). Replaced assertion functions are restored when
    /// <c>Debug.setAssertionLevel</c> is set to a high enough level.
    /// </summary>
    /// <param name="level">The minimum assertion level required.</param>
    /// <param name="name">The name of the current assertion function.</param>
    private static bool ShouldAssertFunction(AssertionLevel level, string name)
    {
        if (!ShouldAssert(level))
        {
            if (!AssertionCache.ContainsKey(name))
            {
                AssertionCache.TryAdd(name, level);
            }
            return false;
        }
        return true;
    }

    [DoesNotReturn]
    internal static void Fail(string? message = null)
    {
        throw new InvalidOperationException(!string.IsNullOrEmpty(message) ? "Debug Failure. " + message : "Debug Failure.");
    }

    internal static void Assert([DoesNotReturnIf(false)] bool expression, string? message = null, string? verboseDebugInfo = null)
    {
        if (!expression)
        {
            message = !string.IsNullOrEmpty(message) ? "False expression: " + message : "False expression.";
            if (!string.IsNullOrEmpty(verboseDebugInfo))
            {
                message += "\r\nVerbose Debug Information: " + verboseDebugInfo;
            }
            Fail(message);
        }
    }

    /// <summary>debug.ts: <c>assert</c> with <c>verboseDebugInfo</c> as a function (<c>string | (() =&gt; string)</c>).</summary>
    internal static void Assert([DoesNotReturnIf(false)] bool expression, string? message, Func<string>? verboseDebugInfo)
    {
        if (!expression)
        {
            message = !string.IsNullOrEmpty(message) ? "False expression: " + message : "False expression.";
            if (verboseDebugInfo is not null)
            {
                message += "\r\nVerbose Debug Information: " + verboseDebugInfo();
            }
            Fail(message);
        }
    }

    internal static void AssertEqual<T>(T a, T b, string? msg = null, string? msg2 = null)
    {
        if (!EqualityComparer<T>.Default.Equals(a, b))
        {
            var message = !string.IsNullOrEmpty(msg) ? !string.IsNullOrEmpty(msg2) ? msg + " " + msg2 : msg : "";
            Fail("Expected " + TemplateString(a) + " === " + TemplateString(b) + ". " + message);
        }
    }

    internal static void AssertLessThanOrEqual(int a, int b)
    {
        if (a > b)
        {
            Fail("Expected " + a.ToString(CultureInfo.InvariantCulture) + " <= " + b.ToString(CultureInfo.InvariantCulture));
        }
    }

    internal static void AssertGreaterThanOrEqual(int a, int b)
    {
        if (a < b)
        {
            Fail("Expected " + a.ToString(CultureInfo.InvariantCulture) + " >= " + b.ToString(CultureInfo.InvariantCulture));
        }
    }

    internal static void AssertIsDefined<T>([NotNull] T value, string? message = null)
    {
        if (value is null)
        {
            Fail(message);
        }
    }

    internal static T CheckDefined<T>(T? value, string? message = null)
        where T : class
    {
        AssertIsDefined(value, message);
        return value;
    }

    /// <summary>debug.ts: <c>checkDefined</c> for values that C# represents as nullable value types.</summary>
    internal static T CheckDefined<T>(T? value, string? message = null)
        where T : struct
    {
        AssertIsDefined(value, message);
        return value.Value;
    }

    [DoesNotReturn]
    internal static void AssertNever(object? member, string message = "Illegal value:")
    {
        var detail = member is Node node ? "SyntaxKind: " + FormatSyntaxKind(node.Kind) : JsonStringify(member);
        Fail(message + " " + detail);
    }

    internal static void AssertNode(Node? node, Func<Node, bool>? test, string? message = null)
    {
        // A cached assertion function is `noop` in TypeScript.
        if (AssertionCache.ContainsKey("assertNode"))
        {
            return;
        }
        if (ShouldAssertFunction(AssertionLevel.Normal, "assertNode"))
        {
            Assert(
                node is not null && (test is null || test(node)),
                !string.IsNullOrEmpty(message) ? message : "Unexpected node.",
                () => "Node " + FormatSyntaxKind(node?.Kind) + " did not pass test '" + GetFunctionName() + "'.");
        }
    }

    // The formatting helpers of debug.ts are not ported (docs/modules/M1-units.md): the kind is shown by its
    // enum name, and functions have no name without reflection, as getFunctionName returns for anonymous ones.
    private static string FormatSyntaxKind(SyntaxKind? kind) => kind?.ToString() ?? "undefined";

    private static string GetFunctionName() => "";

    // `${value}` of a JavaScript template literal for the values the assertions format.
    private static string TemplateString(object? value) => value switch
    {
        null => "undefined",
        bool b => b ? "true" : "false",
        string s => s,
        Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    // JSON.stringify for the values assertNever formats (`undefined` gives undefined, shown as "undefined").
    private static string JsonStringify(object? value) => value switch
    {
        null => "undefined",
        string s => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
        _ => TemplateString(value),
    };
}
