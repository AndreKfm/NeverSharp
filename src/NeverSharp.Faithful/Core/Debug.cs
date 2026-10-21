// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/debug.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/debug/debug.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Diagnostics.CodeAnalysis;
using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>debug.ts: <c>namespace Debug</c> (the assertion part; formatting helpers and debug-info attachment are not ported).</summary>
internal static partial class Debug
{
    // debug.ts:115 (`let currentAssertionLevel = AssertionLevel.None`)
    private static AssertionLevel currentAssertionLevel = AssertionLevel.None;

    // debug.ts:117 (`export let isDebugging = false`)
    internal static bool IsDebugging { get; set; }

    // debug.ts:155. TypeScript replaces an assertion function that is switched off with `noop` and remembers
    // it here; a C# method cannot be replaced, so the cache records the level each switched-off assertion needs.
    private static readonly Dictionary<string, AssertionLevel> assertionCache = [];

    internal static AssertionLevel GetAssertionLevel() => currentAssertionLevel;

    internal static void SetAssertionLevel(AssertionLevel level)
    {
        var prevAssertionLevel = currentAssertionLevel;
        currentAssertionLevel = level;

        if (level > prevAssertionLevel)
        {
            // restore assertion functions for the current assertion level (see `shouldAssertFunction`).
            foreach (var key in new List<string>(assertionCache.Keys))
            {
                if (level >= assertionCache[key])
                {
                    assertionCache.Remove(key);
                }
            }
        }
    }

    // debug.ts:177
    internal static bool ShouldAssert(AssertionLevel level) => currentAssertionLevel >= level;

    /// <summary>
    /// Tests whether an assertion function should be executed. If it shouldn't, it is cached.
    /// Cached assertion functions are restored when `Debug.setAssertionLevel` is set to a high enough level.
    /// </summary>
    /// <param name="level">The minimum assertion level required.</param>
    /// <param name="name">The name of the current assertion function.</param>
    // debug.ts:187
    private static bool ShouldAssertFunction(AssertionLevel level, string name)
    {
        if (!ShouldAssert(level))
        {
            assertionCache[name] = level;
            return false;
        }
        return true;
    }

    /// <summary>
    /// TypeScript's <c>Debug.getFunctionName</c>: the name of a function. A delegate's name needs reflection,
    /// which the port rules ban, so this reports the empty string TypeScript reports for an anonymous function.
    /// </summary>
    internal static string GetFunctionName(Delegate? func) => string.Empty;

    // debug.ts:196
    [DoesNotReturn]
    internal static void Fail(string? message = null) => throw CreateFailure(message);

    /// <summary><c>fail</c> in an expression position (TypeScript's <c>never</c> converts to any type).</summary>
    [DoesNotReturn]
    internal static T Fail<T>(string? message = null) => throw CreateFailure(message);

    private static InvalidOperationException CreateFailure(string? message)
    {
        // `debugger;` and `captureStackTrace` have no counterpart.
        return new InvalidOperationException(Core.IsTruthy(message) ? $"Debug Failure. {message}" : "Debug Failure.");
    }

    // debug.ts:213
    internal static void Assert([DoesNotReturnIf(false)] bool expression, string? message = null, string? verboseDebugInfo = null)
    {
        if (!expression)
        {
            message = Core.IsTruthy(message) ? $"False expression: {message}" : "False expression.";
            if (Core.IsTruthy(verboseDebugInfo))
            {
                message += "\r\nVerbose Debug Information: " + verboseDebugInfo;
            }
            Fail(message);
        }
    }

    // debug.ts:213 (`verboseDebugInfo` as a function)
    internal static void Assert([DoesNotReturnIf(false)] bool expression, string? message, Func<string>? verboseDebugInfo)
    {
        if (!expression)
        {
            message = Core.IsTruthy(message) ? $"False expression: {message}" : "False expression.";
            if (verboseDebugInfo is not null)
            {
                message += "\r\nVerbose Debug Information: " + verboseDebugInfo();
            }
            Fail(message);
        }
    }

    // debug.ts:223
    internal static void AssertEqual<T>(T a, T b, string? msg = null, string? msg2 = null)
    {
        if (!Core.EquateValues(a, b))
        {
            var message = Core.IsTruthy(msg) ? Core.IsTruthy(msg2) ? $"{msg} {msg2}" : msg : "";
            Fail($"Expected {Core.ToJsString(a)} === {Core.ToJsString(b)}. {message}");
        }
    }

    // debug.ts:236
    internal static void AssertLessThanOrEqual(double a, double b)
    {
        if (a > b)
        {
            Fail($"Expected {Core.ToJsString(a)} <= {Core.ToJsString(b)}");
        }
    }

    // debug.ts:242
    internal static void AssertGreaterThanOrEqual(double a, double b)
    {
        if (a < b)
        {
            Fail($"Expected {Core.ToJsString(a)} >= {Core.ToJsString(b)}");
        }
    }

    // debug.ts:248
    internal static void AssertIsDefined<T>([NotNull] T? value, string? message = null)
    {
        if (value is null)
        {
            Fail(message);
        }
    }

    // debug.ts:255
    internal static T CheckDefined<T>(T? value, string? message = null)
    {
        AssertIsDefined(value, message);
        return value;
    }

    // debug.ts:255 (a nullable value type)
    internal static T CheckDefined<T>(T? value, string? message = null)
        where T : struct
    {
        AssertIsDefined(value, message);
        return value.Value;
    }

    // debug.ts:273
    [DoesNotReturn]
    internal static void AssertNever(object? member, string message = "Illegal value:") => Fail(NeverMessage(member, message));

    // debug.ts:273 in an expression position
    [DoesNotReturn]
    internal static T AssertNever<T>(object? member, string message = "Illegal value:") => Fail<T>(NeverMessage(member, message));

    private static string NeverMessage(object? member, string message)
    {
        // TypeScript tests for the properties "kind" and "pos" of an object; a node is the C# counterpart.
        var detail = member is Node node ? "SyntaxKind: " + node.Kind.ToString() : JsonStringify(member);
        return $"{message} {detail}";
    }

    /// <summary>The cases of JSON.stringify that an assertion message needs.</summary>
    private static string JsonStringify(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
        _ => Core.ToJsString(value),
    };

    // debug.ts:294
    internal static void AssertNode(Node? node, Func<Node, bool>? test, string? message = null)
    {
        if (ShouldAssertFunction(AssertionLevel.Normal, "assertNode"))
        {
            Assert(
                node is not null && (test is null || test(node)),
                Core.IsTruthy(message) ? message : "Unexpected node.",
                () => $"Node {(node is null ? nameof(SyntaxKind.Unknown) : node.Kind.ToString())} did not pass test '{GetFunctionName(test)}'.");
        }
    }
}
