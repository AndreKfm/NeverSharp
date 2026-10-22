// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/diagnostic.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Globalization;
using System.Text;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>utilities.ts: creation of diagnostics (the units the parser reaches).</summary>
internal static partial class Utilities
{
    /// <summary>
    /// utilities.ts formatStringFromArgs: `text.replace(/\{(\d+)\}/g, (_match, index) => "" + Debug.checkDefined(args[+index]))`,
    /// with the regular expression written as a loop (Track F bans regular expressions).
    /// </summary>
    internal static string FormatStringFromArgs(string text, IReadOnlyList<object?> args)
    {
        StringBuilder? result = null;
        var last = 0;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '{')
            {
                var j = i + 1;
                while (j < text.Length && text[j] >= '0' && text[j] <= '9')
                {
                    j++;
                }
                if (j > i + 1 && j < text.Length && text[j] == '}')
                {
                    result ??= new StringBuilder();
                    result.Append(text, last, i - last);
                    // `+index`: digits only, so the number is a non-negative integer (or beyond any index).
                    var index = long.TryParse(text.AsSpan(i + 1, j - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n < args.Count ? (int)n : -1;
                    result.Append(JsToString(Debug.CheckDefined(index >= 0 ? args[index] : null)));
                    i = j + 1;
                    last = i;
                    continue;
                }
            }
            i++;
        }
        if (result == null)
        {
            return text;
        }
        result.Append(text, last, text.Length - last);
        return result.ToString();
    }

    /// <summary>JavaScript's `"" + value` for the values of DiagnosticArguments (string | number).</summary>
    private static string JsToString(object value)
    {
        return value switch
        {
            string s => s,
            int i => i.ToString(CultureInfo.InvariantCulture),
            double d => JsNumber.ToString(d),
            _ => Debug.Stringify(value),
        };
    }

    // TypeScript: `let localizedDiagnosticMessages: MapLike<string> | undefined`. Only setLocalizedDiagnosticMessages
    // and maybeSetLocalizedDiagnosticMessages assign it (tsc's command line, not the parser), so it is undefined here
    // and the expression `localizedDiagnosticMessages && localizedDiagnosticMessages[message.key] || message.message`
    // is `message.message`.
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static string GetLocaleSpecificMessage(DiagnosticMessage message)
    {
        return message.Message;
    }

    internal static DiagnosticWithDetachedLocation CreateDetachedDiagnostic(string fileName, ReadOnlySpan<char> sourceText, int start, int length, DiagnosticMessage message, params object?[] args)
    {
        if ((start + length) > sourceText.Length)
        {
            length = sourceText.Length - start;
        }

        AssertDiagnosticLocation(sourceText, start, length);
        var text = GetLocaleSpecificMessage(message);

        if (args.Length != 0)
        {
            text = FormatStringFromArgs(text, args);
        }

        return new DiagnosticWithDetachedLocation
        {
            File = default,
            Start = start,
            Length = length,

            MessageText = text,
            Category = message.Category,
            Code = message.Code,
            ReportsUnnecessary = message.ReportsUnnecessary,
            FileName = fileName,
        };
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsDiagnosticWithDetachedLocation(DiagnosticRelatedInformation diagnostic)
    {
        return diagnostic.File.IsNone
            && diagnostic.StartOrUndefined != null
            && diagnostic.LengthOrUndefined != null
            && diagnostic is DiagnosticWithDetachedLocation;
    }

    private static DiagnosticWithLocation AttachFileToDiagnostic(NodeStore store, DiagnosticWithDetachedLocation diagnostic, Node file)
    {
        var fileName = !string.IsNullOrEmpty(store.FileName(file)) ? store.FileName(file) : "";
        var length = store.SourceFileText(file).Length;
        Debug.AssertEqual(diagnostic.FileName, fileName);
        Debug.AssertLessThanOrEqual(diagnostic.Start, length);
        Debug.AssertLessThanOrEqual(diagnostic.Start + diagnostic.Length, length);
        var diagnosticWithLocation = new DiagnosticWithLocation
        {
            File = file,
            Start = diagnostic.Start,
            Length = diagnostic.Length,
            MessageText = diagnostic.MessageText,
            Category = diagnostic.Category,
            Code = diagnostic.Code,
            ReportsUnnecessary = diagnostic.ReportsUnnecessary,
        };
        if (diagnostic.RelatedInformation != null)
        {
            diagnosticWithLocation.RelatedInformation = [];
            foreach (var related in diagnostic.RelatedInformation)
            {
                if (IsDiagnosticWithDetachedLocation(related) && ((DiagnosticWithDetachedLocation)related).FileName == fileName)
                {
                    var detached = (DiagnosticWithDetachedLocation)related;
                    Debug.AssertLessThanOrEqual(detached.Start, length);
                    Debug.AssertLessThanOrEqual(detached.Start + detached.Length, length);
                    diagnosticWithLocation.RelatedInformation.Add(AttachFileToDiagnostic(store, detached, file));
                }
                else
                {
                    diagnosticWithLocation.RelatedInformation.Add(related);
                }
            }
        }
        return diagnosticWithLocation;
    }

    internal static List<DiagnosticWithLocation> AttachFileToDiagnostics(NodeStore store, List<DiagnosticWithDetachedLocation> diagnostics, Node file)
    {
        var diagnosticsWithLocation = new List<DiagnosticWithLocation>();
        foreach (var diagnostic in diagnostics)
        {
            diagnosticsWithLocation.Add(AttachFileToDiagnostic(store, diagnostic, file));
        }
        return diagnosticsWithLocation;
    }

    internal static DiagnosticWithLocation CreateFileDiagnostic(NodeStore store, Node file, int start, int length, DiagnosticMessage message, params object?[] args)
    {
        AssertDiagnosticLocation(store.SourceFileText(file), start, length);

        var text = GetLocaleSpecificMessage(message);

        if (args.Length != 0)
        {
            text = FormatStringFromArgs(text, args);
        }

        return new DiagnosticWithLocation
        {
            File = file,
            Start = start,
            Length = length,

            MessageText = text,
            Category = message.Category,
            Code = message.Code,
            ReportsUnnecessary = message.ReportsUnnecessary,
            ReportsDeprecated = message.ReportsDeprecated,
        };
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static void AssertDiagnosticLocation(ReadOnlySpan<char> sourceText, int start, int length)
    {
        Debug.AssertGreaterThanOrEqual(start, 0);
        Debug.AssertGreaterThanOrEqual(length, 0);
        Debug.AssertLessThanOrEqual(start, sourceText.Length);
        Debug.AssertLessThanOrEqual(start + length, sourceText.Length);
    }

    internal static T AddRelatedInfo<T>(T diagnostic, params DiagnosticRelatedInformation[] relatedInformation)
        where T : Diagnostic
    {
        if (relatedInformation.Length == 0)
        {
            return diagnostic;
        }
        diagnostic.RelatedInformation ??= [];
        diagnostic.RelatedInformation.AddRange(relatedInformation);
        return diagnostic;
    }
}
