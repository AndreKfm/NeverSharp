// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/parser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/parser/parser.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>types.ts: FileReference.</summary>
internal sealed class FileReference : TextRange
{
    public int Pos { get; set; }

    public int End { get; set; }

    public string FileName { get; set; } = "";

    /// <summary>`resolutionMode?: ResolutionMode` (`ModuleKind.ESNext | ModuleKind.CommonJS | undefined`).</summary>
    public ModuleKind? ResolutionMode { get; set; }

    /// <summary>`preserve?: boolean` (the parser only sets it to true).</summary>
    public bool? Preserve { get; set; }
}

/// <summary>types.ts: CheckJsDirective.</summary>
internal sealed class CheckJsDirective : TextRange
{
    public int Pos { get; set; }

    public int End { get; set; }

    public bool Enabled { get; set; }
}

/// <summary>types.ts: AmdDependency (`path` is undefined at run time for `path=''`, see ExtractPragmas).</summary>
internal sealed class AmdDependency
{
    public string? Path { get; set; }

    public string? Name { get; set; }
}

/// <summary>types.ts: PragmaArgumentSpecification.</summary>
internal sealed class PragmaArgumentSpecification
{
    /// <summary>Determines the name of the key in the resulting parsed type.</summary>
    public required string Name { get; init; }

    public bool Optional { get; init; }

    public bool CaptureSpan { get; init; }
}

/// <summary>types.ts: PragmaDefinition.</summary>
internal sealed class PragmaDefinition
{
    public IReadOnlyList<PragmaArgumentSpecification>? Args { get; init; }

    /// <summary>`kind?: PragmaKindFlags` (every entry of commentPragmas defines it).</summary>
    public PragmaKindFlags Kind { get; init; }
}

/// <summary>
/// One argument value of a pragma: TypeScript's `string | { value: string; pos: number; end: number; }`. `Value` is null
/// where TypeScript has `undefined` (an optional argument that is absent from `args`, or an empty `'...'` value).
/// </summary>
internal sealed class PragmaArgument
{
    internal PragmaArgument(string? value)
    {
        Value = value;
    }

    internal PragmaArgument(string value, int pos, int end)
    {
        Value = value;
        Pos = pos;
        End = end;
        CapturesSpan = true;
    }

    internal string? Value { get; }

    /// <summary>True for the `{ value, pos, end }` form (arguments with `captureSpan`).</summary>
    internal bool CapturesSpan { get; }

    internal int Pos { get; }

    internal int End { get; }
}

/// <summary>types.ts: `PragmaPseudoMap[K]`, `{ arguments: PragmaArgumentType<K>; range: CommentRange; }`.</summary>
internal sealed class PragmaPseudoMapValue(Dictionary<string, PragmaArgument> arguments, CommentRange range)
{
    internal Dictionary<string, PragmaArgument> Arguments { get; } = arguments;

    internal CommentRange Range { get; } = range;
}

/// <summary>types.ts: PragmaPseudoMapEntry, `{ name: K; args: PragmaPseudoMap[K]; }`.</summary>
internal sealed class PragmaPseudoMapEntry(string name, PragmaPseudoMapValue args)
{
    internal string Name { get; } = name;

    internal PragmaPseudoMapValue Args { get; } = args;
}

/// <summary>
/// types.ts: PragmaMap / ReadonlyPragmaMap, a Map in insertion order whose values are either a single
/// <see cref="PragmaPseudoMapValue"/> (the pragma was found once) or a <c>List&lt;PragmaPseudoMapValue&gt;</c>
/// (found in several places).
/// </summary>
internal sealed class PragmaMap
{
    internal OrderedDictionary<string, object> Entries { get; } = new(StringComparer.Ordinal);

    internal bool Has(string key)
    {
        return Entries.ContainsKey(key);
    }

    internal bool TryGetValue(string key, out object? value)
    {
        if (Entries.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }
        value = null;
        return false;
    }

    internal void Set(string key, object value)
    {
        // Like Map.prototype.set: an existing key keeps its position in the iteration order.
        Entries[key] = value;
    }
}

/// <summary>types.ts: `commentPragmas` (a value, in `Types` like the other values of types.ts).</summary>
internal static partial class Types
{
    internal static readonly IReadOnlyDictionary<string, PragmaDefinition> CommentPragmas = new Dictionary<string, PragmaDefinition>(StringComparer.Ordinal)
    {
        ["reference"] = new PragmaDefinition
        {
            Args =
            [
                new PragmaArgumentSpecification { Name = "types", Optional = true, CaptureSpan = true },
                new PragmaArgumentSpecification { Name = "lib", Optional = true, CaptureSpan = true },
                new PragmaArgumentSpecification { Name = "path", Optional = true, CaptureSpan = true },
                new PragmaArgumentSpecification { Name = "no-default-lib", Optional = true },
                new PragmaArgumentSpecification { Name = "resolution-mode", Optional = true },
                new PragmaArgumentSpecification { Name = "preserve", Optional = true },
            ],
            Kind = PragmaKindFlags.TripleSlashXML,
        },
        ["amd-dependency"] = new PragmaDefinition
        {
            Args =
            [
                new PragmaArgumentSpecification { Name = "path" },
                new PragmaArgumentSpecification { Name = "name", Optional = true },
            ],
            Kind = PragmaKindFlags.TripleSlashXML,
        },
        ["amd-module"] = new PragmaDefinition
        {
            Args = [new PragmaArgumentSpecification { Name = "name" }],
            Kind = PragmaKindFlags.TripleSlashXML,
        },
        ["ts-check"] = new PragmaDefinition
        {
            Kind = PragmaKindFlags.SingleLine,
        },
        ["ts-nocheck"] = new PragmaDefinition
        {
            Kind = PragmaKindFlags.SingleLine,
        },
        ["jsx"] = new PragmaDefinition
        {
            Args = [new PragmaArgumentSpecification { Name = "factory" }],
            Kind = PragmaKindFlags.MultiLine,
        },
        ["jsxfrag"] = new PragmaDefinition
        {
            Args = [new PragmaArgumentSpecification { Name = "factory" }],
            Kind = PragmaKindFlags.MultiLine,
        },
        ["jsximportsource"] = new PragmaDefinition
        {
            Args = [new PragmaArgumentSpecification { Name = "factory" }],
            Kind = PragmaKindFlags.MultiLine,
        },
        ["jsxruntime"] = new PragmaDefinition
        {
            Args = [new PragmaArgumentSpecification { Name = "factory" }],
            Kind = PragmaKindFlags.MultiLine,
        },
    };
}

/// <summary>
/// The members of SourceFile that the generator leaves to the port and that concern pragmas (types.ts SourceFile,
/// ReadonlyPragmaContext). The diagnostics lists are in Diagnostics.cs.
/// </summary>
public sealed partial class SourceFile
{
    // `amdDependencies: readonly AmdDependency[]`
    internal IReadOnlyList<AmdDependency> AmdDependencies = null!;

    // `referencedFiles: readonly FileReference[]`
    internal IReadOnlyList<FileReference> ReferencedFiles = null!;

    // `typeReferenceDirectives: readonly FileReference[]`
    internal IReadOnlyList<FileReference> TypeReferenceDirectives = null!;

    // `libReferenceDirectives: readonly FileReference[]`
    internal IReadOnlyList<FileReference> LibReferenceDirectives = null!;

    // `commentDirectives: CommentDirective[] | undefined`
    internal List<CommentDirective>? CommentDirectives;

    // `checkJsDirective: CheckJsDirective | undefined`
    internal CheckJsDirective? CheckJsDirective;

    // `pragmas: ReadonlyPragmaMap`
    internal PragmaMap? Pragmas;
}

/// <summary>The members of Bundle that the generator leaves to the port.</summary>
public sealed partial class Bundle
{
    // `syntheticFileReferences?: readonly FileReference[]`
    internal IReadOnlyList<FileReference>? SyntheticFileReferences;

    // `syntheticTypeReferences?: readonly FileReference[]`
    internal IReadOnlyList<FileReference>? SyntheticTypeReferences;

    // `syntheticLibReferences?: readonly FileReference[]`
    internal IReadOnlyList<FileReference>? SyntheticLibReferences;
}

// parser.ts lines 10556-10823: declaration file names and pragma processing (file-level functions of parser.ts).
//
// TypeScript uses regular expressions here; regular expressions are banned in this port, so each one is replaced by a
// hand-written matcher with exactly the semantics of `RegExp.prototype.exec` for that expression (leftmost match,
// greedy/lazy quantifiers with backtracking, the `i` and `m` flags). For each matcher the derivation is given in
// its comment: where the regex could backtrack, the comment shows that only one way of matching can succeed, so the
// matcher is deterministic.
internal sealed partial class Parser
{
    // utilities.ts supportedDeclarationExtensions (`[Extension.Dts, Extension.Dcts, Extension.Dmts]`); a private copy
    // because it is immutable and only used here.
    private static readonly string[] SupportedDeclarationExtensions = [Extension.Dts, Extension.Dcts, Extension.Dmts];

    internal static bool IsDeclarationFileName(string fileName)
    {
        return GetDeclarationFileExtension(fileName) is not null;
    }

    internal static string? GetDeclarationFileExtension(string fileName)
    {
        var standardExtension = TsPath.GetAnyExtensionFromPath(fileName, SupportedDeclarationExtensions, /*ignoreCase*/ false);
        if (!string.IsNullOrEmpty(standardExtension))
        {
            return standardExtension;
        }
        if (TsPath.FileExtensionIs(fileName, Extension.Ts))
        {
            var baseName = TsPath.GetBaseFileName(fileName);
            var index = baseName.LastIndexOf(".d.", StringComparison.Ordinal);
            if (index >= 0)
            {
                return baseName.Substring(index);
            }
        }
        return null;
    }

    private static ModuleKind? ParseResolutionMode(string? mode, int pos, int end, Action<int, int, DiagnosticMessage> reportDiagnostic)
    {
        if (string.IsNullOrEmpty(mode))
        {
            return null;
        }
        if (mode == "import")
        {
            return ModuleKind.ESNext;
        }
        if (mode == "require")
        {
            return ModuleKind.CommonJS;
        }
        reportDiagnostic(pos, end - pos, Diagnostics.resolution_mode_should_be_either_require_or_import);
        return null;
    }

    internal static void ProcessCommentPragmas(SourceFile context, string sourceText)
    {
        var pragmas = new List<PragmaPseudoMapEntry>();

        var ranges = Scanner.GetLeadingCommentRanges(sourceText, 0);
        if (ranges is not null)
        {
            foreach (var range in ranges)
            {
                var comment = sourceText.Substring(range.Pos, range.End - range.Pos);
                ExtractPragmas(pragmas, range, comment);
            }
        }

        var pragmaMap = new PragmaMap();
        context.Pragmas = pragmaMap;
        foreach (var pragma in pragmas)
        {
            if (pragmaMap.TryGetValue(pragma.Name, out var currentValue))
            {
                if (currentValue is List<PragmaPseudoMapValue> currentList)
                {
                    currentList.Add(pragma.Args);
                }
                else
                {
                    pragmaMap.Set(pragma.Name, new List<PragmaPseudoMapValue> { (PragmaPseudoMapValue)currentValue!, pragma.Args });
                }
                continue;
            }
            pragmaMap.Set(pragma.Name, pragma.Args);
        }
    }

    // `toArray(entryOrList)` for the values of a PragmaMap.
    private static List<PragmaPseudoMapValue> PragmaValuesToArray(object entryOrList)
    {
        if (entryOrList is List<PragmaPseudoMapValue> list)
        {
            return list;
        }
        return [(PragmaPseudoMapValue)entryOrList];
    }

    // `arguments.<name>` read as a string: undefined (null) when the argument is absent or has no value.
    private static string? PragmaArgumentValueOf(Dictionary<string, PragmaArgument> arguments, string name)
    {
        return arguments.TryGetValue(name, out var argument) ? argument.Value : null;
    }

    /// <remarks>`reportDiagnostic` is TypeScript's `PragmaDiagnosticReporter` `(pos, length, message) => void`.</remarks>
    internal static void ProcessPragmasIntoFields(SourceFile context, Action<int, int, DiagnosticMessage> reportDiagnostic)
    {
        context.CheckJsDirective = null;
        var referencedFiles = new List<FileReference>();
        context.ReferencedFiles = referencedFiles;
        var typeReferenceDirectives = new List<FileReference>();
        context.TypeReferenceDirectives = typeReferenceDirectives;
        var libReferenceDirectives = new List<FileReference>();
        context.LibReferenceDirectives = libReferenceDirectives;
        context.AmdDependencies = new List<AmdDependency>();
        foreach (var item in context.Pragmas!.Entries)
        {
            var key = item.Key;
            var entryOrList = item.Value;
            switch (key)
            {
                case "reference":
                {
                    foreach (var arg in PragmaValuesToArray(entryOrList))
                    {
                        var arguments = arg.Arguments;
                        arguments.TryGetValue("types", out var types);
                        arguments.TryGetValue("lib", out var lib);
                        arguments.TryGetValue("path", out var path);
                        var res = PragmaArgumentValueOf(arguments, "resolution-mode");
                        var preserve = PragmaArgumentValueOf(arguments, "preserve") == "true" ? true : (bool?)null;
                        if (PragmaArgumentValueOf(arguments, "no-default-lib") == "true")
                        {
                            // This has been removed; parse but ignore it.
                        }
                        else if (types is not null)
                        {
                            var parsed = ParseResolutionMode(res, types.Pos, types.End, reportDiagnostic);
                            typeReferenceDirectives.Add(new FileReference { Pos = types.Pos, End = types.End, FileName = types.Value!, ResolutionMode = parsed, Preserve = preserve });
                        }
                        else if (lib is not null)
                        {
                            libReferenceDirectives.Add(new FileReference { Pos = lib.Pos, End = lib.End, FileName = lib.Value!, Preserve = preserve });
                        }
                        else if (path is not null)
                        {
                            referencedFiles.Add(new FileReference { Pos = path.Pos, End = path.End, FileName = path.Value!, Preserve = preserve });
                        }
                        else
                        {
                            reportDiagnostic(arg.Range.Pos, arg.Range.End - arg.Range.Pos, Diagnostics.Invalid_reference_directive_syntax);
                        }
                    }
                    break;
                }
                case "amd-dependency":
                {
                    var amdDependencies = new List<AmdDependency>();
                    foreach (var x in PragmaValuesToArray(entryOrList))
                    {
                        amdDependencies.Add(new AmdDependency { Name = PragmaArgumentValueOf(x.Arguments, "name"), Path = PragmaArgumentValueOf(x.Arguments, "path") });
                    }
                    context.AmdDependencies = amdDependencies;
                    break;
                }
                case "amd-module":
                {
                    if (entryOrList is List<PragmaPseudoMapValue> entries)
                    {
                        foreach (var entry in entries)
                        {
                            if (!string.IsNullOrEmpty(context.ModuleName))
                            {
                                // TODO: It's probably fine to issue this diagnostic on all instances of the pragma
                                reportDiagnostic(entry.Range.Pos, entry.Range.End - entry.Range.Pos, Diagnostics.An_AMD_module_cannot_have_multiple_name_assignments);
                            }
                            context.ModuleName = PragmaArgumentValueOf(entry.Arguments, "name");
                        }
                    }
                    else
                    {
                        context.ModuleName = PragmaArgumentValueOf(((PragmaPseudoMapValue)entryOrList).Arguments, "name");
                    }
                    break;
                }
                case "ts-nocheck":
                case "ts-check":
                {
                    // _last_ of either nocheck or check in a file is the "winner"
                    foreach (var entry in PragmaValuesToArray(entryOrList))
                    {
                        if (context.CheckJsDirective is null || entry.Range.Pos > context.CheckJsDirective.Pos)
                        {
                            context.CheckJsDirective = new CheckJsDirective
                            {
                                Enabled = key == "ts-check",
                                End = entry.Range.End,
                                Pos = entry.Range.Pos,
                            };
                        }
                    }
                    break;
                }
                case "jsx":
                case "jsxfrag":
                case "jsximportsource":
                case "jsxruntime":
                    continue; // Accessed directly (TypeScript: `return` from the forEach callback)
                default:
                    Debug.Fail("Unhandled pragma kind"); // Can this be made into an assertNever in the future?
                    break;
            }
        }
    }

    // ----- Character classes of JavaScript regular expressions (without the `u` flag) -----

    // `\s`: WhiteSpace and LineTerminator of ECMAScript (also the set `String.prototype.trim` removes).
    private static bool IsRegExpWhiteSpace(char c)
    {
        return c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or ' ' or ' '
            or (>= ' ' and <= ' ')
            or '\u2028' or '\u2029' or ' ' or ' ' or '　' or '﻿';
    }

    // LineTerminator: what `.` does not match and what `$` (with the `m` flag) matches before.
    private static bool IsRegExpLineTerminator(char c)
    {
        return c is '\n' or '\r' or '\u2028' or '\u2029';
    }

    // `String.prototype.toLowerCase`, only used to look names up in commentPragmas (ASCII keys). The per-character
    // invariant mapping equals the JavaScript mapping for every character that can lower-case to an ASCII letter
    // (U+212A KELVIN SIGN -> k); U+0130, whose JavaScript lower case is two characters, is kept as it is.
    private static string RegExpToLowerCase(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] != 'İ')
            {
                chars[i] = char.ToLowerInvariant(chars[i]);
            }
        }
        return new string(chars);
    }

    // `String.prototype.trim`
    private static string RegExpTrim(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && IsRegExpWhiteSpace(text[start]))
        {
            start++;
        }
        while (end > start && IsRegExpWhiteSpace(text[end - 1]))
        {
            end--;
        }
        return text.Substring(start, end - start);
    }

    // `text.split(/\s+/)` for a text without leading and trailing white space (the result of RegExpTrim): the empty
    // text gives [""], any other text its runs of non-white space.
    private static List<string> SplitOnWhiteSpace(string trimmedText)
    {
        var parts = new List<string>();
        if (trimmedText.Length == 0)
        {
            parts.Add("");
            return parts;
        }
        var i = 0;
        while (i < trimmedText.Length)
        {
            var j = i;
            while (j < trimmedText.Length && !IsRegExpWhiteSpace(trimmedText[j]))
            {
                j++;
            }
            parts.Add(trimmedText.Substring(i, j - i));
            while (j < trimmedText.Length && IsRegExpWhiteSpace(trimmedText[j]))
            {
                j++;
            }
            i = j;
        }
        return parts;
    }

    // Case-insensitive character comparison of a regex with the `i` flag and without the `u` flag, for a pattern
    // character that is ASCII: the canonicalization (toUpperCase) never maps a non-ASCII character to an ASCII one,
    // so only ASCII letters of the other case match.
    private static bool EqualsIgnoringRegExpCase(char pattern, char actual)
    {
        if (pattern == actual)
        {
            return true;
        }
        var patternIsLetter = (pattern >= 'a' && pattern <= 'z') || (pattern >= 'A' && pattern <= 'Z');
        var actualIsLetter = (actual >= 'a' && actual <= 'z') || (actual >= 'A' && actual <= 'Z');
        return patternIsLetter && actualIsLetter && (pattern | 0x20) == (actual | 0x20);
    }

    // ----- getNamedArgRegEx -----

    // TypeScript:
    //   const namedArgRegExCache = new Map<string, RegExp>();
    //   function getNamedArgRegEx(name: string): RegExp {
    //       ...cache lookup...
    //       const result = new RegExp(`(\\s${name}\\s*=\\s*)(?:(?:'([^']*)')|(?:"([^"]*)"))`, "im");
    //   }
    // The cache is mutable global state and only saves compiling the regex, so it is not ported: this function does
    // the work of `getNamedArgRegEx(name).exec(text)` directly. `name` is one of the (literal) argument names of
    // commentPragmas. The `m` flag has no effect (no anchors). Matching at a start position p, in order:
    //   `\s` at p, the name (ignoring ASCII case), `\s*`, `=`, `\s*` (together with the first `\s`: group 1),
    //   then either `'` [^']* `'` (group 2) or `"` [^"]* `"` (group 3).
    // No backtracking can change a result: each `\s*` is followed by a character that is not white space (`=` or
    // a quote), and `[^']*` followed by `'` can only end at the first `'`.
    // Returns false when there is no match. `index` is `match.index`, `group1Length` is `match[1].length`; `group2` is
    // `match[2]` and `group3` is `match[3]` (null: the group did not take part in the match, i.e. `undefined`).
    private static bool TryMatchNamedArgument(string text, string name, out int index, out int group1Length, out string? group2, out string? group3)
    {
        for (var p = 0; p < text.Length; p++)
        {
            if (!IsRegExpWhiteSpace(text[p]))
            {
                continue;
            }
            var i = p + 1;
            if (i + name.Length > text.Length)
            {
                continue;
            }
            var nameMatches = true;
            for (var k = 0; k < name.Length; k++)
            {
                if (!EqualsIgnoringRegExpCase(name[k], text[i + k]))
                {
                    nameMatches = false;
                    break;
                }
            }
            if (!nameMatches)
            {
                continue;
            }
            i += name.Length;
            while (i < text.Length && IsRegExpWhiteSpace(text[i]))
            {
                i++;
            }
            if (i >= text.Length || text[i] != '=')
            {
                continue;
            }
            i++;
            while (i < text.Length && IsRegExpWhiteSpace(text[i]))
            {
                i++;
            }
            if (i >= text.Length)
            {
                continue;
            }
            var quote = text[i];
            if (quote != '\'' && quote != '"')
            {
                continue;
            }
            var close = text.IndexOf(quote, i + 1);
            if (close < 0)
            {
                continue;
            }
            var content = text.Substring(i + 1, close - (i + 1));
            index = p;
            group1Length = i - p;
            group2 = quote == '\'' ? content : null;
            group3 = quote == '"' ? content : null;
            return true;
        }
        index = 0;
        group1Length = 0;
        group2 = null;
        group3 = null;
        return false;
    }

    // ----- tripleSlashXMLCommentStartRegEx -----

    // TypeScript: const tripleSlashXMLCommentStartRegEx = /^\/\/\/\s*<(\S+)\s.*?\/>/m;
    // `exec` finds the leftmost match; `^` (flag `m`) matches at the start of the text and after every line
    // terminator. At such a position p the regex means: `///`, `\s*`, `<`, `(\S+)` (group 1), one `\s`, then the lazy
    // `.*?` up to the first `/>` that can be reached without crossing a line terminator (`.` does not match one).
    // Backtracking cannot help: `\s*` is followed by `<` (not white space) and `\S+` by `\s`, so both quantifiers can
    // only take their maximal run; the lazy `.*?` finds the first `/>` or fails at the first line terminator or the end.
    // Returns group 1 (the match index is not used by the caller).
    private static bool TryMatchTripleSlashXMLCommentStart(string text, out string name)
    {
        var p = 0;
        while (true)
        {
            if (TryMatchTripleSlashXMLCommentStartAt(text, p, out name))
            {
                return true;
            }
            // The next position after which `^` holds: behind the next line terminator at or after p.
            var q = p;
            while (q < text.Length && !IsRegExpLineTerminator(text[q]))
            {
                q++;
            }
            if (q >= text.Length)
            {
                name = "";
                return false;
            }
            p = q + 1;
        }
    }

    private static bool TryMatchTripleSlashXMLCommentStartAt(string text, int p, out string name)
    {
        name = "";
        if (p + 3 > text.Length || text[p] != '/' || text[p + 1] != '/' || text[p + 2] != '/')
        {
            return false;
        }
        var i = p + 3;
        while (i < text.Length && IsRegExpWhiteSpace(text[i]))
        {
            i++;
        }
        if (i >= text.Length || text[i] != '<')
        {
            return false;
        }
        var nameStart = i + 1;
        var nameEnd = nameStart;
        while (nameEnd < text.Length && !IsRegExpWhiteSpace(text[nameEnd]))
        {
            nameEnd++;
        }
        if (nameEnd == nameStart || nameEnd >= text.Length)
        {
            return false; // `\S+` needs at least one character, and `\s` needs a character after it
        }
        // `\s` consumes text[nameEnd]; then `.*?\/>`
        var m = nameEnd + 1;
        while (true)
        {
            if (m + 1 < text.Length && text[m] == '/' && text[m + 1] == '>')
            {
                name = text.Substring(nameStart, nameEnd - nameStart);
                return true;
            }
            if (m >= text.Length || IsRegExpLineTerminator(text[m]))
            {
                return false;
            }
            m++;
        }
    }

    // ----- singleLinePragmaRegEx -----

    // TypeScript: const singleLinePragmaRegEx = /^\/\/\/?\s*@([^\s:]+)((?:[^\S\r\n]|:).*)?$/m;
    // At a position p after which `^` holds (see TryMatchTripleSlashXMLCommentStart): `//`, an optional `/`, `\s*`,
    // `@`, group 1 `[^\s:]+`, then the optional group 2 `((?:[^\S\r\n]|:).*)` and `$`.
    // Prefix: `\/?` takes a third slash if there is one (not taking it would require `\s*@` to match at a `/`),
    // `\s*` takes all white space (the next character must be `@`).
    // Group 1 takes its maximal run of characters that are neither white space nor `:`, and at least one is needed.
    // Giving back characters of group 1 can never help, and the maximal run always leads to a match: it ends at the
    // end of the text, or at a white space character or `:`. At the end of the text, or at `\r` or `\n`, group 2
    // cannot start (`[^\S\r\n]` is white space other than \r and \n; note that U+2028 and U+2029 are such white
    // space), it is skipped and `$` holds (end, or before a line terminator). Otherwise group 2 starts there and `.*`
    // takes the rest of the line, after which `$` holds.
    // Returns group 1 and group 2 (null: undefined).
    private static bool TryMatchSingleLinePragma(string text, out string name, out string? args)
    {
        var p = 0;
        while (true)
        {
            if (TryMatchSingleLinePragmaAt(text, p, out name, out args))
            {
                return true;
            }
            var q = p;
            while (q < text.Length && !IsRegExpLineTerminator(text[q]))
            {
                q++;
            }
            if (q >= text.Length)
            {
                name = "";
                args = null;
                return false;
            }
            p = q + 1;
        }
    }

    private static bool TryMatchSingleLinePragmaAt(string text, int p, out string name, out string? args)
    {
        name = "";
        args = null;
        if (p + 2 > text.Length || text[p] != '/' || text[p + 1] != '/')
        {
            return false;
        }
        var i = p + 2;
        if (i < text.Length && text[i] == '/')
        {
            i++;
        }
        while (i < text.Length && IsRegExpWhiteSpace(text[i]))
        {
            i++;
        }
        if (i >= text.Length || text[i] != '@')
        {
            return false;
        }
        var nameStart = i + 1;
        var nameEnd = nameStart;
        while (nameEnd < text.Length && !IsRegExpWhiteSpace(text[nameEnd]) && text[nameEnd] != ':')
        {
            nameEnd++;
        }
        if (nameEnd == nameStart)
        {
            return false;
        }
        name = text.Substring(nameStart, nameEnd - nameStart);
        if (nameEnd >= text.Length || text[nameEnd] == '\r' || text[nameEnd] == '\n')
        {
            args = null; // group 2 did not take part
            return true;
        }
        var end = nameEnd + 1;
        while (end < text.Length && !IsRegExpLineTerminator(text[end]))
        {
            end++;
        }
        args = text.Substring(nameEnd, end - nameEnd);
        return true;
    }

    // ----- multiLinePragmaRegEx -----

    // TypeScript (defined inline in extractPragmas since it uses the "g" flag, which keeps a persistent index):
    //   const multiLinePragmaRegEx = /@(\S+)(\s+(?:\S.*)?)?$/gm;
    //   while (multiLineMatch = multiLinePragmaRegEx.exec(text)) { ... }
    // This is one `exec` call: the leftmost match starting at or after `lastIndex`. At a position p with
    // text[p] == '@': group 1 `\S+` takes its maximal run (at least one character). Giving characters back cannot
    // lead to a match, because then the next character is not white space (group 2 cannot start) and `$` needs a
    // line terminator or the end (line terminators are white space). At the end of the run: the end of the text ends
    // the match (group 2 undefined); otherwise a white space character follows and group 2 `\s+` takes the whole run of
    // white space (line terminators included), followed, if the run is not at the end of the text, by `\S.*`, which
    // takes the rest of the line (`.` stops before a line terminator); `$` then holds. `matchEnd` is the new `lastIndex`.
    // Returns group 1 and group 2 (null: undefined).
    private static bool TryMatchMultiLinePragma(string text, int lastIndex, out string name, out string? args, out int matchEnd)
    {
        name = "";
        args = null;
        matchEnd = lastIndex;
        for (var p = lastIndex; p < text.Length; p++)
        {
            if (text[p] != '@')
            {
                continue;
            }
            var nameStart = p + 1;
            var nameEnd = nameStart;
            while (nameEnd < text.Length && !IsRegExpWhiteSpace(text[nameEnd]))
            {
                nameEnd++;
            }
            if (nameEnd == nameStart)
            {
                continue;
            }
            name = text.Substring(nameStart, nameEnd - nameStart);
            if (nameEnd >= text.Length)
            {
                args = null;
                matchEnd = text.Length;
                return true;
            }
            var whiteSpaceEnd = nameEnd;
            while (whiteSpaceEnd < text.Length && IsRegExpWhiteSpace(text[whiteSpaceEnd]))
            {
                whiteSpaceEnd++;
            }
            var end = whiteSpaceEnd;
            if (whiteSpaceEnd < text.Length)
            {
                end = whiteSpaceEnd + 1; // `\S`
                while (end < text.Length && !IsRegExpLineTerminator(text[end]))
                {
                    end++;
                }
            }
            args = text.Substring(nameEnd, end - nameEnd);
            matchEnd = end;
            return true;
        }
        return false;
    }

    private static void ExtractPragmas(List<PragmaPseudoMapEntry> pragmas, CommentRange range, string text)
    {
        if (range.Kind == SyntaxKind.SingleLineCommentTrivia && TryMatchTripleSlashXMLCommentStart(text, out var tripleSlashName))
        {
            var name = RegExpToLowerCase(tripleSlashName);
            if (!Types.CommentPragmas.TryGetValue(name, out var pragma) || (pragma.Kind & PragmaKindFlags.TripleSlashXML) == 0)
            {
                return;
            }
            if (pragma.Args is not null)
            {
                var argument = new Dictionary<string, PragmaArgument>(StringComparer.Ordinal);
                foreach (var arg in pragma.Args)
                {
                    var matched = TryMatchNamedArgument(text, arg.Name, out var matchIndex, out var group1Length, out var group2, out var group3);
                    if (!matched && !arg.Optional)
                    {
                        return; // Missing required argument, don't parse
                    }
                    else if (matched)
                    {
                        // `matchResult[2] || matchResult[3]`: the empty string is falsy, so an empty `'...'` value is
                        // `undefined` (null), an empty `"..."` value is "".
                        var value = !string.IsNullOrEmpty(group2) ? group2 : group3;
                        if (arg.CaptureSpan)
                        {
                            var startPos = range.Pos + matchIndex + group1Length + 1;
                            if (value is null)
                            {
                                // TypeScript: `value.length` throws a TypeError for `undefined` (e.g. `path=''`).
                                throw new InvalidOperationException("TypeError: Cannot read properties of undefined (reading 'length')");
                            }
                            argument[arg.Name] = new PragmaArgument(value, startPos, startPos + value.Length);
                        }
                        else
                        {
                            argument[arg.Name] = new PragmaArgument(value);
                        }
                    }
                }
                pragmas.Add(new PragmaPseudoMapEntry(name, new PragmaPseudoMapValue(argument, range)));
            }
            else
            {
                pragmas.Add(new PragmaPseudoMapEntry(name, new PragmaPseudoMapValue(new Dictionary<string, PragmaArgument>(StringComparer.Ordinal), range)));
            }
            return;
        }

        if (range.Kind == SyntaxKind.SingleLineCommentTrivia && TryMatchSingleLinePragma(text, out var singleLineName, out var singleLineArgs))
        {
            AddPragmaForMatch(pragmas, range, PragmaKindFlags.SingleLine, singleLineName, singleLineArgs);
            return;
        }

        if (range.Kind == SyntaxKind.MultiLineCommentTrivia)
        {
            var lastIndex = 0;
            while (TryMatchMultiLinePragma(text, lastIndex, out var multiLineName, out var multiLineArgs, out var multiLineEnd))
            {
                lastIndex = multiLineEnd;
                AddPragmaForMatch(pragmas, range, PragmaKindFlags.MultiLine, multiLineName, multiLineArgs);
            }
        }
    }

    // TypeScript passes the RegExpExecArray `match`; this takes `match[1]` (matchName) and `match[2]` (matchArgs,
    // null for undefined). `if (!match) return;` is dropped: the matchers only return true for a match.
    private static void AddPragmaForMatch(List<PragmaPseudoMapEntry> pragmas, CommentRange range, PragmaKindFlags kind, string matchName, string? matchArgs)
    {
        var name = RegExpToLowerCase(matchName);
        if (!Types.CommentPragmas.TryGetValue(name, out var pragma) || (pragma.Kind & kind) == 0)
        {
            return;
        }
        var args = matchArgs; // Split on spaces and match up positionally with definition
        var argument = GetNamedPragmaArguments(pragma, args);
        if (argument is null)
        {
            return; // Missing required argument, fail to parse it
        }
        pragmas.Add(new PragmaPseudoMapEntry(name, new PragmaPseudoMapValue(argument, range)));
    }

    // Returns null for TypeScript's "fail".
    private static Dictionary<string, PragmaArgument>? GetNamedPragmaArguments(PragmaDefinition pragma, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new Dictionary<string, PragmaArgument>(StringComparer.Ordinal);
        }
        if (pragma.Args is null)
        {
            return new Dictionary<string, PragmaArgument>(StringComparer.Ordinal);
        }
        var args = SplitOnWhiteSpace(RegExpTrim(text));
        var argMap = new Dictionary<string, PragmaArgument>(StringComparer.Ordinal);
        for (var i = 0; i < pragma.Args.Count; i++)
        {
            var argument = pragma.Args[i];
            var argText = i < args.Count ? args[i] : null;
            if (string.IsNullOrEmpty(argText) && !argument.Optional)
            {
                return null;
            }
            if (argument.CaptureSpan)
            {
                Debug.Fail("Capture spans not yet implemented for non-xml pragmas");
            }
            argMap[argument.Name] = new PragmaArgument(argText);
        }
        return argMap;
    }

    private static bool TagNameEscapedTextEquals(object? lhs, object? rhs)
    {
        return string.Equals(lhs as string, rhs as string, StringComparison.Ordinal);
    }

    // `name.escapedText` of an Identifier or PrivateIdentifier.
    private static object? TagNameEscapedTextOf(Node name)
    {
        return name is PrivateIdentifier privateIdentifier ? privateIdentifier.EscapedText : ((Identifier)name).EscapedText;
    }

    internal static bool TagNamesAreEquivalent(Node lhs, Node rhs)
    {
        if (lhs.Kind != rhs.Kind)
        {
            return false;
        }

        if (lhs.Kind == SyntaxKind.Identifier)
        {
            return TagNameEscapedTextEquals(((Identifier)lhs).EscapedText, ((Identifier)rhs).EscapedText);
        }

        if (lhs.Kind == SyntaxKind.ThisKeyword)
        {
            return true;
        }

        if (lhs.Kind == SyntaxKind.JsxNamespacedName)
        {
            var lhsNamespaced = (JsxNamespacedName)lhs;
            var rhsNamespaced = (JsxNamespacedName)rhs;
            return TagNameEscapedTextEquals(lhsNamespaced.Namespace.EscapedText, rhsNamespaced.Namespace.EscapedText) &&
                TagNameEscapedTextEquals(lhsNamespaced.Name.EscapedText, rhsNamespaced.Name.EscapedText);
        }

        // If we are at this statement then we must have PropertyAccessExpression and because tag name in Jsx element can only
        // take forms of JsxTagNameExpression which includes an identifier, "this" expression, or another propertyAccessExpression
        // it is safe to case the expression property as such. See parseJsxElementName for how we parse tag name in Jsx element
        var lhsAccess = (PropertyAccessExpression)lhs;
        var rhsAccess = (PropertyAccessExpression)rhs;
        return TagNameEscapedTextEquals(TagNameEscapedTextOf(lhsAccess.Name), TagNameEscapedTextOf(rhsAccess.Name)) &&
            TagNamesAreEquivalent(lhsAccess.Expression, rhsAccess.Expression);
    }
}
