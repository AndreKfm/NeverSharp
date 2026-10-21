// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/parser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/parser/references.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Text;
using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>types.ts: PragmaArgumentSpecification.</summary>
internal sealed class PragmaArgumentSpecification
{
    public required string Name; // Determines the name of the key in the resulting parsed type, type parameter to cause literal type inference
    public bool Optional;
    public bool CaptureSpan;
}

/// <summary>types.ts: PragmaDefinition.</summary>
internal sealed class PragmaDefinition
{
    public IReadOnlyList<PragmaArgumentSpecification>? Args;

    // If not present, defaults to PragmaKindFlags.Default
    public PragmaKindFlags? Kind;
}

/// <summary>types.ts: the argument of a pragma with `captureSpan: true` (`{ value: string; pos: number; end: number; }`).</summary>
internal sealed class PragmaArgumentSpan
{
    public required string Value;
    public int Pos;
    public int End;
}

/// <summary>
/// types.ts: the arguments object of a pragma (`{ [index: string]: string | { value: string; pos: number; end: number; } }`).
/// A value is a string, a PragmaArgumentSpan, or null for an argument present with the value undefined.
/// </summary>
internal sealed class PragmaArguments : OrderedDictionary<string, object?>
{
    public PragmaArguments()
        : base(StringComparer.Ordinal)
    {
    }

    /// <summary>`arguments[name]`: undefined (null) when the argument is absent.</summary>
    public object? Get(string name)
    {
        return TryGetValue(name, out var value) ? value : null;
    }
}

/// <summary>types.ts: the value of a PragmaPseudoMap entry (`{ arguments: ...; range: CommentRange }`).</summary>
internal sealed class PragmaEntry
{
    public required PragmaArguments Arguments;
    public required CommentRange Range;
}

/// <summary>types.ts: PragmaPseudoMapEntry (`{ name: K; args: PragmaPseudoMap[K] }`).</summary>
internal sealed class PragmaPseudoMapEntry
{
    public required string Name;
    public required PragmaEntry Args;
}

/// <summary>
/// types.ts: PragmaMap / ReadonlyPragmaMap (`Map&lt;string, PragmaPseudoMap[keyof PragmaPseudoMap] | PragmaPseudoMap[keyof PragmaPseudoMap][]&gt;`).
/// A value is a PragmaEntry or a List&lt;PragmaEntry&gt;. A JavaScript Map iterates in insertion order, as OrderedDictionary does.
/// </summary>
internal sealed class PragmaMap : OrderedDictionary<string, object>
{
    /// <summary>TypeScript's `emptyMap as ReadonlyPragmaMap` (core.ts emptyMap, a shared empty map).</summary>
    public static readonly PragmaMap Empty = new();

    public PragmaMap()
        : base(StringComparer.Ordinal)
    {
    }
}

/// <summary>types.ts: SourceFile.pragmas (declared by the port, see Types.cs).</summary>
public sealed partial class SourceFile
{
    internal PragmaMap Pragmas = null!;
}

/// <summary>types.ts: declarations that are values (not types).</summary>
internal static partial class Types
{
    // While not strictly a type, this is here because `PragmaMap` needs to be here to be used with `SourceFile`, and we don't
    //  fancy effectively defining it twice, once in value-space and once in type-space
    // (TypeScript: an object literal; `commentPragmas[name]` on another name reads a missing property or one of
    // Object.prototype, neither of which has a `kind`, so callers treat both as no pragma.)
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
            Args = [new PragmaArgumentSpecification { Name = "path" }, new PragmaArgumentSpecification { Name = "name", Optional = true }],
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

/// <summary>parser.ts: pragma processing and the file-level helpers after namespace IncrementalParser.</summary>
internal sealed partial class Parser
{
    internal static bool IsDeclarationFileName(string fileName)
    {
        return GetDeclarationFileExtension(fileName) != null;
    }

    internal static string? GetDeclarationFileExtension(string fileName)
    {
        var standardExtension = TsPath.GetAnyExtensionFromPath(fileName, Utilities.SupportedDeclarationExtensions, /*ignoreCase*/ false);
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
                return baseName[index..];
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

        foreach (var range in (IReadOnlyList<CommentRange>?)Scanner.GetLeadingCommentRanges(sourceText, 0) ?? Core.EmptyArray<CommentRange>())
        {
            var comment = sourceText[range.Pos..range.End];
            ExtractPragmas(pragmas, range, comment);
        }

        context.Pragmas = new PragmaMap();
        foreach (var pragma in pragmas)
        {
            if (context.Pragmas.TryGetValue(pragma.Name, out var currentValue))
            {
                if (currentValue is List<PragmaEntry> list)
                {
                    list.Add(pragma.Args);
                }
                else
                {
                    context.Pragmas[pragma.Name] = new List<PragmaEntry> { (PragmaEntry)currentValue, pragma.Args };
                }
                continue;
            }
            context.Pragmas[pragma.Name] = pragma.Args;
        }
    }

    internal static void ProcessPragmasIntoFields(SourceFile context, Action<int, int, DiagnosticMessage> reportDiagnostic)
    {
        context.CheckJsDirective = null;
        var contextReferencedFiles = new List<FileReference>();
        var contextTypeReferenceDirectives = new List<FileReference>();
        var contextLibReferenceDirectives = new List<FileReference>();
        context.ReferencedFiles = contextReferencedFiles;
        context.TypeReferenceDirectives = contextTypeReferenceDirectives;
        context.LibReferenceDirectives = contextLibReferenceDirectives;
        context.AmdDependencies = new List<AmdDependency>();
        foreach (var (key, entryOrList) in context.Pragmas)
        {
            // TODO: The below should be strongly type-guarded and not need casts/explicit annotations, since entryOrList is related to
            // key and key is constrained to a union; but it's not (see GH#21483 for at least partial fix) :(
            switch (key)
            {
                case "reference":
                {
                    var referencedFiles = contextReferencedFiles;
                    var typeReferenceDirectives = contextTypeReferenceDirectives;
                    var libReferenceDirectives = contextLibReferenceDirectives;
                    foreach (var arg in ToEntryArray(entryOrList))
                    {
                        var types = (PragmaArgumentSpan?)arg.Arguments.Get("types");
                        var lib = (PragmaArgumentSpan?)arg.Arguments.Get("lib");
                        var path = (PragmaArgumentSpan?)arg.Arguments.Get("path");
                        var res = (string?)arg.Arguments.Get("resolution-mode");
                        var preserveValue = (string?)arg.Arguments.Get("preserve");
                        bool? preserve = preserveValue == "true" ? true : null;
                        if ((string?)arg.Arguments.Get("no-default-lib") == "true")
                        {
                            // This has been removed; parse but ignore it.
                        }
                        else if (types != null)
                        {
                            var parsed = ParseResolutionMode(res, types.Pos, types.End, reportDiagnostic);
                            typeReferenceDirectives.Add(new FileReference { Pos = types.Pos, End = types.End, FileName = types.Value, ResolutionMode = parsed, Preserve = preserve });
                        }
                        else if (lib != null)
                        {
                            libReferenceDirectives.Add(new FileReference { Pos = lib.Pos, End = lib.End, FileName = lib.Value, Preserve = preserve });
                        }
                        else if (path != null)
                        {
                            referencedFiles.Add(new FileReference { Pos = path.Pos, End = path.End, FileName = path.Value, Preserve = preserve });
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
                    foreach (var x in ToEntryArray(entryOrList))
                    {
                        amdDependencies.Add(new AmdDependency { Name = (string?)x.Arguments.Get("name"), Path = (string)x.Arguments.Get("path")! });
                    }
                    context.AmdDependencies = amdDependencies;
                    break;
                }
                case "amd-module":
                {
                    if (entryOrList is List<PragmaEntry> entries)
                    {
                        foreach (var entry in entries)
                        {
                            if (!string.IsNullOrEmpty(context.ModuleName))
                            {
                                // TODO: It's probably fine to issue this diagnostic on all instances of the pragma
                                reportDiagnostic(entry.Range.Pos, entry.Range.End - entry.Range.Pos, Diagnostics.An_AMD_module_cannot_have_multiple_name_assignments);
                            }
                            context.ModuleName = (string?)entry.Arguments.Get("name");
                        }
                    }
                    else
                    {
                        context.ModuleName = (string?)((PragmaEntry)entryOrList).Arguments.Get("name");
                    }
                    break;
                }
                case "ts-nocheck":
                case "ts-check":
                {
                    // _last_ of either nocheck or check in a file is the "winner"
                    foreach (var entry in ToEntryArray(entryOrList))
                    {
                        if (context.CheckJsDirective == null || entry.Range.Pos > context.CheckJsDirective.Pos)
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

    /// <summary>core.ts toArray on a PragmaMap value (a PragmaEntry or a list of them).</summary>
    private static List<PragmaEntry> ToEntryArray(object entryOrList)
    {
        return entryOrList is List<PragmaEntry> list ? list : [(PragmaEntry)entryOrList];
    }

    private static void ExtractPragmas(List<PragmaPseudoMapEntry> pragmas, CommentRange range, string text)
    {
        var tripleSlash = range.Kind == SyntaxKind.SingleLineCommentTrivia ? MatchTripleSlashXMLCommentStart(text) : null;
        if (tripleSlash != null)
        {
            var name = JsToLowerCase(tripleSlash); // Technically unsafe cast, but we do it so the below check to make it safe typechecks
            if (!Types.CommentPragmas.TryGetValue(name, out var pragma) || ((pragma.Kind ?? 0) & PragmaKindFlags.TripleSlashXML) == 0)
            {
                return;
            }
            if (pragma.Args != null)
            {
                var argument = new PragmaArguments();
                foreach (var arg in pragma.Args)
                {
                    var matchResult = MatchNamedArg(arg.Name, text);
                    if (matchResult == null && !arg.Optional)
                    {
                        return; // Missing required argument, don't parse
                    }
                    else if (matchResult != null)
                    {
                        var value = matchResult.Value.Value;
                        if (arg.CaptureSpan)
                        {
                            // TypeScript reads `value.length`, which throws a TypeError when value is undefined (an
                            // empty single-quoted value); `value!.Length` throws as well.
                            var startPos = range.Pos + matchResult.Value.Index + matchResult.Value.Group1Length + 1;
                            argument[arg.Name] = new PragmaArgumentSpan
                            {
                                Value = value!,
                                Pos = startPos,
                                End = startPos + value!.Length,
                            };
                        }
                        else
                        {
                            argument[arg.Name] = value;
                        }
                    }
                }
                pragmas.Add(new PragmaPseudoMapEntry { Name = name, Args = new PragmaEntry { Arguments = argument, Range = range } });
            }
            else
            {
                pragmas.Add(new PragmaPseudoMapEntry { Name = name, Args = new PragmaEntry { Arguments = new PragmaArguments(), Range = range } });
            }
            return;
        }

        var singleLine = range.Kind == SyntaxKind.SingleLineCommentTrivia ? MatchSingleLinePragma(text) : null;
        if (singleLine != null)
        {
            AddPragmaForMatch(pragmas, range, PragmaKindFlags.SingleLine, singleLine.Value.Name, singleLine.Value.Arguments);
            return;
        }

        if (range.Kind == SyntaxKind.MultiLineCommentTrivia)
        {
            // multiLinePragmaRegEx = /@(\S+)(\s+(?:\S.*)?)?$/gm, iterated with exec (its lastIndex persists across matches).
            var lastIndex = 0;
            while (MatchMultiLinePragma(text, ref lastIndex, out var name, out var arguments))
            {
                AddPragmaForMatch(pragmas, range, PragmaKindFlags.MultiLine, name, arguments);
            }
        }
    }

    /// <summary>TypeScript: `addPragmaForMatch(pragmas, range, kind, match)` with match[1] (name) and match[2] (args).</summary>
    private static void AddPragmaForMatch(List<PragmaPseudoMapEntry> pragmas, CommentRange range, PragmaKindFlags kind, string matchName, string? matchArgs)
    {
        var name = JsToLowerCase(matchName); // Technically unsafe cast, but we do it so they below check to make it safe typechecks
        if (!Types.CommentPragmas.TryGetValue(name, out var pragma) || ((pragma.Kind ?? 0) & kind) == 0)
        {
            return;
        }
        var args = matchArgs; // Split on spaces and match up positionally with definition
        var argument = GetNamedPragmaArguments(pragma, args);
        if (argument == null)
        {
            return; // Missing required argument, fail to parse it
        }
        pragmas.Add(new PragmaPseudoMapEntry { Name = name, Args = new PragmaEntry { Arguments = argument, Range = range } });
    }

    /// <summary>TypeScript returns the string "fail" for a missing required argument; here that is null.</summary>
    private static PragmaArguments? GetNamedPragmaArguments(PragmaDefinition pragma, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new PragmaArguments();
        }
        if (pragma.Args == null)
        {
            return new PragmaArguments();
        }
        var args = JsSplitOnWhiteSpace(JsTrim(text));
        var argMap = new PragmaArguments();
        for (var i = 0; i < pragma.Args.Count; i++)
        {
            var argument = pragma.Args[i];
            var argValue = i < args.Count ? args[i] : null;
            if (string.IsNullOrEmpty(argValue) && !argument.Optional)
            {
                return null;
            }
            if (argument.CaptureSpan)
            {
                Debug.Fail("Capture spans not yet implemented for non-xml pragmas");
            }
            argMap[argument.Name] = argValue;
        }
        return argMap;
    }

    internal static bool TagNamesAreEquivalent(Node lhs, Node rhs)
    {
        if (lhs.Kind != rhs.Kind)
        {
            return false;
        }

        if (lhs.Kind == SyntaxKind.Identifier)
        {
            return (string)((Identifier)lhs).EscapedText! == (string)((Identifier)rhs).EscapedText!;
        }

        if (lhs.Kind == SyntaxKind.ThisKeyword)
        {
            return true;
        }

        if (lhs.Kind == SyntaxKind.JsxNamespacedName)
        {
            return (string)((JsxNamespacedName)lhs).Namespace.EscapedText! == (string)((JsxNamespacedName)rhs).Namespace.EscapedText! &&
                (string)((JsxNamespacedName)lhs).Name.EscapedText! == (string)((JsxNamespacedName)rhs).Name.EscapedText!;
        }

        // If we are at this statement then we must have PropertyAccessExpression and because tag name in Jsx element can only
        // take forms of JsxTagNameExpression which includes an identifier, "this" expression, or another propertyAccessExpression
        // it is safe to case the expression property as such. See parseJsxElementName for how we parse tag name in Jsx element
        return EscapedTextOfMemberName(((PropertyAccessExpression)lhs).Name) == EscapedTextOfMemberName(((PropertyAccessExpression)rhs).Name) &&
            TagNamesAreEquivalent(((PropertyAccessExpression)lhs).Expression, ((PropertyAccessExpression)rhs).Expression);
    }

    /// <summary>`(name as MemberName).escapedText` (Identifier or PrivateIdentifier).</summary>
    private static string? EscapedTextOfMemberName(Node name)
    {
        return (string?)(name is Identifier identifier ? identifier.EscapedText : ((PrivateIdentifier)name).EscapedText);
    }

    //// The regular expressions of pragma processing, written as matchers with exactly their semantics (Track F bans
    //// System.Text.RegularExpressions). `\s` is JavaScript's WhiteSpace or LineTerminator; `.` and the `m` flag's `^`/`$`
    //// use JavaScript's line terminators (LF, CR, U+2028, U+2029).

    private static bool IsJsLineTerminator(char ch)
    {
        return ch == (char)0x000A || ch == (char)0x000D || ch == (char)0x2028 || ch == (char)0x2029;
    }

    /// <summary>Positions where `^` matches under the `m` flag: the start, and after each line terminator.</summary>
    private static bool IsLineStart(string text, int p)
    {
        return p == 0 || IsJsLineTerminator(text[p - 1]);
    }

    /// <summary>
    /// tripleSlashXMLCommentStartRegEx = /^\/\/\/\s*&lt;(\S+)\s.*?\/&gt;/m: returns group 1 of the first match, or null.
    /// </summary>
    private static string? MatchTripleSlashXMLCommentStart(string text)
    {
        var len = text.Length;
        for (var p = 0; p <= len; p++)
        {
            if (!IsLineStart(text, p))
            {
                continue;
            }
            if (p + 3 > len || text[p] != '/' || text[p + 1] != '/' || text[p + 2] != '/')
            {
                continue;
            }
            var q = p + 3;
            while (q < len && Utilities.IsJsWhiteSpaceOrLineTerminator(text[q]))
            {
                q++;
            }
            if (q >= len || text[q] != '<')
            {
                continue;
            }
            q++;
            var nameStart = q;
            // (\S+) takes the longest run; a shorter one is followed by \S, never by the required \s.
            while (q < len && !Utilities.IsJsWhiteSpaceOrLineTerminator(text[q]))
            {
                q++;
            }
            if (q == nameStart || q >= len)
            {
                continue;
            }
            var name = text[nameStart..q];
            q++; // \s
            // .*?\/> : the first "/>" that no line terminator precedes.
            for (var r = q; r < len; r++)
            {
                if (r + 1 < len && text[r] == '/' && text[r + 1] == '>')
                {
                    return name;
                }
                if (IsJsLineTerminator(text[r]))
                {
                    break;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// singleLinePragmaRegEx = /^\/\/\/?\s*@([^\s:]+)((?:[^\S\r\n]|:).*)?$/m: returns groups 1 and 2 of the first match, or null.
    /// </summary>
    private static (string Name, string? Arguments)? MatchSingleLinePragma(string text)
    {
        var len = text.Length;
        for (var p = 0; p <= len; p++)
        {
            if (!IsLineStart(text, p))
            {
                continue;
            }
            if (p + 2 > len || text[p] != '/' || text[p + 1] != '/')
            {
                continue;
            }
            var q = p + 2;
            // `\/?` takes the slash if present; without it, \s*@ would have to match at the slash, which it cannot.
            if (q < len && text[q] == '/')
            {
                q++;
            }
            while (q < len && Utilities.IsJsWhiteSpaceOrLineTerminator(text[q]))
            {
                q++;
            }
            if (q >= len || text[q] != '@')
            {
                continue;
            }
            q++;
            var nameStart = q;
            while (q < len && !Utilities.IsJsWhiteSpaceOrLineTerminator(text[q]) && text[q] != ':')
            {
                q++;
            }
            if (q == nameStart)
            {
                continue;
            }
            var name = text[nameStart..q];
            if (q < len && (text[q] == ':' || (Utilities.IsJsWhiteSpaceOrLineTerminator(text[q]) && text[q] != '\r' && text[q] != '\n')))
            {
                // ((?:[^\S\r\n]|:).*) then $: `.*` stops at a line terminator or the end, where $ matches.
                var e = q + 1;
                while (e < len && !IsJsLineTerminator(text[e]))
                {
                    e++;
                }
                return (name, text[q..e]);
            }
            if (q == len || IsJsLineTerminator(text[q]))
            {
                return (name, null);
            }
        }
        return null;
    }

    /// <summary>
    /// One `exec` of multiLinePragmaRegEx = /@(\S+)(\s+(?:\S.*)?)?$/gm from lastIndex: the match's groups 1 and 2, and
    /// lastIndex moved to the match's end; false (and lastIndex reset to 0) when there is no further match.
    /// </summary>
    private static bool MatchMultiLinePragma(string text, ref int lastIndex, out string name, out string? arguments)
    {
        var len = text.Length;
        for (var i = lastIndex; i < len; i++)
        {
            if (text[i] != '@' || i + 1 >= len || Utilities.IsJsWhiteSpaceOrLineTerminator(text[i + 1]))
            {
                continue;
            }
            var e = i + 1;
            while (e < len && !Utilities.IsJsWhiteSpaceOrLineTerminator(text[e]))
            {
                e++;
            }
            name = text[(i + 1)..e];
            if (e == len)
            {
                // No room for group 2; $ matches at the end.
                arguments = null;
                lastIndex = e;
                return true;
            }
            // text[e] is white space: group 2 (tried first, greedily) always matches: \s+, then \S.* if anything
            // follows, which ends at a line terminator or the end, where $ matches.
            var f = e;
            while (f < len && Utilities.IsJsWhiteSpaceOrLineTerminator(text[f]))
            {
                f++;
            }
            var end = f;
            if (f < len)
            {
                end = f + 1;
                while (end < len && !IsJsLineTerminator(text[end]))
                {
                    end++;
                }
            }
            arguments = text[e..end];
            lastIndex = end;
            return true;
        }
        name = "";
        arguments = null;
        lastIndex = 0;
        return false;
    }

    /// <summary>
    /// getNamedArgRegEx(name) = new RegExp(`(\\s${name}\\s*=\\s*)(?:(?:'([^']*)')|(?:"([^"]*)"))`, "im"), exec on text:
    /// the match's index, the length of group 1 and `matchResult[2] || matchResult[3]`, or null when there is no match.
    /// The `i` flag compares the (ASCII) names case-insensitively; JavaScript's non-unicode case folding never maps a
    /// non-ASCII character to an ASCII one.
    /// </summary>
    private static (int Index, int Group1Length, string? Value)? MatchNamedArg(string name, string text)
    {
        var len = text.Length;
        for (var i = 0; i < len; i++)
        {
            if (!Utilities.IsJsWhiteSpaceOrLineTerminator(text[i]))
            {
                continue;
            }
            var j = i + 1;
            if (j + name.Length > len || !EqualsAsciiIgnoreCase(text, j, name))
            {
                continue;
            }
            var q = j + name.Length;
            while (q < len && Utilities.IsJsWhiteSpaceOrLineTerminator(text[q]))
            {
                q++;
            }
            if (q >= len || text[q] != '=')
            {
                continue;
            }
            q++;
            while (q < len && Utilities.IsJsWhiteSpaceOrLineTerminator(text[q]))
            {
                q++;
            }
            if (q >= len)
            {
                continue;
            }
            var group1Length = q - i;
            if (text[q] == '\'')
            {
                var close = text.IndexOf('\'', q + 1);
                if (close < 0)
                {
                    continue;
                }
                var group2 = text[(q + 1)..close];
                // `matchResult[2] || matchResult[3]`: an empty group 2 is falsy, and group 3 did not participate.
                return (i, group1Length, group2.Length != 0 ? group2 : null);
            }
            if (text[q] == '"')
            {
                var close = text.IndexOf('"', q + 1);
                if (close < 0)
                {
                    continue;
                }
                return (i, group1Length, text[(q + 1)..close]);
            }
        }
        return null;
    }

    private static bool EqualsAsciiIgnoreCase(string text, int start, string name)
    {
        for (var k = 0; k < name.Length; k++)
        {
            var c = text[start + k];
            var n = name[k];
            if (c == n)
            {
                continue;
            }
            if (char.IsAsciiLetter(n) && char.IsAsciiLetter(c) && (c | 0x20) == (n | 0x20))
            {
                continue;
            }
            return false;
        }
        return true;
    }

    /// <summary>
    /// JavaScript's String.prototype.toLowerCase as far as it matters here: the result is only used if it equals one of
    /// the (ASCII) pragma names. The characters whose lower case is or contains ASCII are A–Z, U+212A KELVIN SIGN ("k")
    /// and U+0130 (two characters: "i" and U+0307); every other character lower-cases to a non-ASCII character, so a name with one
    /// matches no pragma either way and is left as it is.
    /// </summary>
    private static string JsToLowerCase(string s)
    {
        StringBuilder? builder = null;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var lower = c == (char)0x0130 ? "i" + (char)0x0307 : c == (char)0x212A ? "k" : char.IsAsciiLetterUpper(c) ? ((char)(c | 0x20)).ToString() : null;
            if (lower == null)
            {
                builder?.Append(c);
                continue;
            }
            if (builder == null && (lower.Length != 1 || lower[0] != c))
            {
                builder = new StringBuilder(s.Length + 1);
                builder.Append(s, 0, i);
            }
            builder?.Append(lower);
        }
        return builder != null ? builder.ToString() : s;
    }

    /// <summary>JavaScript's String.prototype.trim.</summary>
    private static string JsTrim(string s)
    {
        var start = 0;
        var end = s.Length;
        while (start < end && Utilities.IsJsWhiteSpaceOrLineTerminator(s[start]))
        {
            start++;
        }
        while (end > start && Utilities.IsJsWhiteSpaceOrLineTerminator(s[end - 1]))
        {
            end--;
        }
        return s[start..end];
    }

    /// <summary>`s.split(/\s+/)`: for the empty string, [""].</summary>
    private static List<string> JsSplitOnWhiteSpace(string s)
    {
        var result = new List<string>();
        var partStart = 0;
        var i = 0;
        while (i < s.Length)
        {
            if (Utilities.IsJsWhiteSpaceOrLineTerminator(s[i]))
            {
                result.Add(s[partStart..i]);
                while (i < s.Length && Utilities.IsJsWhiteSpaceOrLineTerminator(s[i]))
                {
                    i++;
                }
                partStart = i;
                continue;
            }
            i++;
        }
        result.Add(s[partStart..]);
        return result;
    }
}
