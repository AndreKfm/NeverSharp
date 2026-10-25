// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): checker dump of Track O for Gate C (module C0).
//
// Reproduces harness/astdump/dump-ts-checker.mjs (checker dump format version 1, described there): each unit becomes a
// one-file program, Program.Create([unit.fileName], { noResolve: true }, MemoryCompilerHost) with the default library
// from the pinned typescript 6.0.3 package (--lib-dir, default harness/astdump/node_modules/typescript/lib, the folder
// the oracle reads), parsed and bound once per process and shared by all units and threads (DefaultLibrary). Written:
// the header, the options/global/semantic/suggestion diagnostics, and the types and symbols walks of typeWriter.ts.
// Module C0: the checker is a skeleton, so the checker's diagnostics and both walks are empty; the program layer's
// header and diagnostics (library list, TS6504 for JavaScript units, bind diagnostics) are already real.

using System.Globalization;
using System.Text;
using System.Text.Json;
using NeverSharp.Generated;
using NeverSharp.Optimized;
using TsProgram = NeverSharp.Optimized.Program;

namespace NeverSharp.Dump;

internal static class CheckerDump
{
    private const int StackSize = 256 * 1024 * 1024;

    private static readonly Dictionary<int, string> KindNames = Common.Names(EnumNames.SyntaxKind);

    /// <summary>The Gate C options (phase3-plan.md): <c>{ noResolve: true }</c>, as the oracle's compilerOptions.</summary>
    private const string OptionsJson = "{\"noResolve\":true}";

    private static CompilerOptions GateOptions() => new() { NoResolve = true };

    public static int Run(Options options)
    {
        var corpus = options.Corpus;
        var jsdocMode = options.Get("--jsdoc-mode", "types");
        JSDocParsingMode mode = jsdocMode switch
        {
            "all" => JSDocParsingMode.ParseAll,
            "none" => JSDocParsingMode.ParseNone,
            "types" => JSDocParsingMode.ParseForTypeErrors,
            _ => throw new InvalidOperationException("--jsdoc-mode must be all, none or types"),
        };
        var dumpName = $"cs-{Track.Name}-checker-{jsdocMode}";
        var outDir = Path.GetFullPath(options.Get("--out", Path.Combine(corpus, "dumps", dumpName)));
        var timeoutMs = int.Parse(options.Get("--timeout-ms", "120000"), CultureInfo.InvariantCulture);
        var libDir = Path.GetFullPath(options.Get("--lib-dir", Path.Combine(Common.FindRoot(), "harness", "astdump", "node_modules", "typescript", "lib")));
        Common.ResetDirectory(outDir);
        var units = Common.ReadUnits(corpus);
        var unitsFile = options.Get("--units", "");
        if (unitsFile.Length > 0)
        {
            var ids = new HashSet<string>(File.ReadAllLines(unitsFile), StringComparer.Ordinal);
            units = units.FindAll(u => ids.Contains(u.Id));
        }
        var library = DefaultLibrary.Get(libDir, MemoryCompilerHost.LibFolder, GateOptions(), mode);

        var started = DateTime.UtcNow;
        // Before the first unit, as the oracle's warm-up: the library files of the default program parsed and bound (not timed per unit).
        var warmUp = TsProgram.Create(["/__empty__.ts"], GateOptions(), new MemoryCompilerHost("/__empty__.ts", "", ScriptKind.TS, library, mode));
        warmUp.GetTypeChecker();
        File.WriteAllText(outDir + ".lib.txt", DumpLibrary(warmUp), Common.Utf8);
        var libSeconds = (DateTime.UtcNow - started).TotalSeconds;

        var ok = new List<string>();
        var timeout = new List<string>();
        var error = new List<(string Id, string Error)>();
        Parallel.ForEach(units, unit =>
        {
            var text = Common.ReadUnitText(corpus, unit);
            string? result = null;
            string? outcome = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = DumpUnit(text, unit, mode, library);
                }
                catch (Exception e)
                {
                    outcome = e.ToString();
                }
            }, StackSize)
            {
                IsBackground = true,
            };
            thread.Start();
            if (!thread.Join(timeoutMs))
            {
                outcome = "timeout";
            }
            lock (ok)
            {
                if (outcome is null)
                {
                    ok.Add(unit.Id);
                }
                else if (outcome == "timeout")
                {
                    timeout.Add(unit.Id);
                }
                else
                {
                    error.Add((unit.Id, outcome));
                }
            }
            if (outcome is null)
            {
                File.WriteAllText(Path.Combine(outDir, unit.Id + ".txt"), result!, Common.Utf8);
            }
        });
        ok.Sort(string.CompareOrdinal);
        timeout.Sort(string.CompareOrdinal);
        error.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        var outcomes = new
        {
            timeoutMs,
            ok,
            timeout,
            error = error.ConvertAll(e => new { id = e.Id, error = e.Error }),
        };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(outDir)!, Path.GetFileName(outDir) + ".outcomes.json"), JsonSerializer.Serialize(outcomes), Common.Utf8);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{dumpName}: ok {ok.Count}, timeout {timeout.Count}, error {error.Count} in {(DateTime.UtcNow - started).TotalSeconds:0.0} s (library {libSeconds:0.0} s)"));
        for (var i = 0; i < Math.Min(5, error.Count); i++)
        {
            Console.WriteLine($"error {error[i].Id}: {error[i].Error}");
        }
        Environment.Exit(0); // threads of timed-out units may still run
        return 0;
    }

    private static ScriptKind ScriptKindOf(Unit unit) => unit.Kind switch
    {
        "TS" => ScriptKind.TS,
        "TSX" => ScriptKind.TSX,
        "JS" => ScriptKind.JS,
        "JSX" => ScriptKind.JSX,
        "JSON" => ScriptKind.JSON,
        _ => throw new InvalidOperationException(unit.Kind),
    };

    internal static string DumpUnit(string text, Unit unit, JSDocParsingMode mode, DefaultLibrary library)
    {
        var host = new MemoryCompilerHost(unit.FileName, text, ScriptKindOf(unit), library, mode);
        var options = GateOptions();
        var program = TsProgram.Create([unit.FileName], options, host);
        var sourceFile = program.GetSourceFile(unit.FileName);
        var inProgram = sourceFile is not null;
        var optionDiagnostics = program.GetOptionsDiagnostics();
        var globalDiagnostics = program.GetGlobalDiagnostics();
        var semantic = sourceFile is null ? [] : program.GetSemanticDiagnostics(sourceFile);
        var suggestion = sourceFile is null ? [] : program.GetSuggestionDiagnostics(sourceFile);
        var syntactic = sourceFile is null ? [] : program.GetSyntacticDiagnostics(sourceFile);
        var hadErrors = optionDiagnostics.Count + globalDiagnostics.Count + syntactic.Count + semantic.Count > 0;

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"checker-dump 1\nfile {unit.FileName} {unit.Kind} inProgram={Bool(inProgram)} hadErrors={Bool(hadErrors)}\n");
        sb.Append("options ").Append(OptionsJson).Append('\n');
        sb.Append("computed");
        foreach (var (name, value) in options.ComputedOptionValues())
        {
            sb.Append(' ').Append(name).Append('=').Append(value is bool b ? Bool(b) : ((int)value).ToString(CultureInfo.InvariantCulture));
        }
        sb.Append('\n');
        var libs = new List<string>();
        foreach (var file in program.SourceFiles)
        {
            var name = file.Store.FileName(file.Root);
            if (name.StartsWith(MemoryCompilerHost.LibFolder + "/", StringComparison.Ordinal))
            {
                libs.Add(BaseName(name));
            }
        }
        sb.Append(CultureInfo.InvariantCulture, $"libs {libs.Count}");
        foreach (var lib in libs)
        {
            sb.Append(' ').Append(lib);
        }
        sb.Append('\n');

        sb.Append("== diagnostics\n");
        WriteDiagnostics(sb, "option", optionDiagnostics, unit.FileName);
        WriteDiagnostics(sb, "global", globalDiagnostics, unit.FileName);
        WriteDiagnostics(sb, "semantic", semantic, unit.FileName);
        WriteDiagnostics(sb, "suggestion", suggestion, unit.FileName);
        var checker = program.GetTypeChecker();
        sb.Append("== types\n");
        if (sourceFile is not null)
        {
            Walk(sb, checker, sourceFile, hadErrors, isSymbolWalk: false);
        }
        sb.Append("== symbols\n");
        if (sourceFile is not null)
        {
            Walk(sb, checker, sourceFile, hadErrors, isSymbolWalk: true);
        }
        return sb.ToString();
    }

    // The library file of the dump (dump-ts-checker.mjs dumpLibrary): per library file of the program of an empty file
    // its semantic diagnostics, then the global diagnostics.
    private static string DumpLibrary(TsProgram program)
    {
        var sb = new StringBuilder();
        foreach (var file in program.SourceFiles)
        {
            var name = file.Store.FileName(file.Root);
            if (!name.StartsWith(MemoryCompilerHost.LibFolder + "/", StringComparison.Ordinal))
            {
                continue;
            }
            sb.Append("== ").Append(BaseName(name)).Append('\n');
            WriteDiagnostics(sb, "semantic", program.GetSemanticDiagnostics(file), "/__empty__.ts");
        }
        sb.Append("== global\n");
        WriteDiagnostics(sb, "global", program.GetGlobalDiagnostics(), "/__empty__.ts");
        return sb.ToString();
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string BaseName(string fileName) => fileName[(fileName.LastIndexOf('/') + 1)..];

    private static string Category(DiagnosticCategory category) => category switch
    {
        DiagnosticCategory.Warning => "Warning",
        DiagnosticCategory.Error => "Error",
        DiagnosticCategory.Suggestion => "Suggestion",
        DiagnosticCategory.Message => "Message",
        _ => throw new InvalidOperationException(category.ToString()),
    };

    private static void WriteDiagnostics(StringBuilder sb, string tag, List<ProgramDiagnostic> list, string unitName)
    {
        foreach (var d in list)
        {
            sb.Append(tag).Append(' ');
            WriteDiagnostic(sb, d, d.SourceFile, unitName);
            if (d.ReportsUnnecessary)
            {
                sb.Append(" unnecessary");
            }
            if (d.ReportsDeprecated)
            {
                sb.Append(" deprecated");
            }
            sb.Append('\n');
            foreach (var r in d.RelatedInformation ?? [])
            {
                sb.Append("  related ");
                WriteDiagnostic(sb, r, (r as ProgramRelatedInformation)?.SourceFile, unitName);
                sb.Append('\n');
            }
        }
    }

    private static void WriteDiagnostic(StringBuilder sb, DiagnosticRelatedInformation d, SourceFile? file, string unitName)
    {
        var fileName = file?.Store.FileName(file.Root);
        var fileText = fileName is null ? "-" : fileName == unitName ? "." : fileName.StartsWith(MemoryCompilerHost.LibFolder + "/", StringComparison.Ordinal) ? BaseName(fileName) : fileName;
        sb.Append(fileText).Append(' ')
            .Append(d.StartOrUndefined is { } start ? start.ToString(CultureInfo.InvariantCulture) : "-").Append(' ')
            .Append(d.LengthOrUndefined is { } length ? length.ToString(CultureInfo.InvariantCulture) : "-").Append(' ')
            .Append(d.Code.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(Category(d.Category)).Append(' ')
            .Append(BinderDump.Json(ProgramDiagnostics.FlattenDiagnosticMessageText(d, "\n")));
    }

    // typeWriter.ts TypeWriterWalker: forEachASTNode (pre-order over forEachChild, without JSDoc) and writeTypeOrSymbol.
    private static void Walk(StringBuilder sb, Checker checker, SourceFile file, bool hadErrors, bool isSymbolWalk)
    {
        var store = file.Store;
        var work = new Stack<Node>();
        work.Push(file.Root);
        var children = new List<Node>();
        while (work.Count > 0)
        {
            var node = work.Pop();
            if (IsWalkCandidate(store, node))
            {
                if (isSymbolWalk)
                {
                    WriteSymbol(sb, checker, file, node);
                }
                else
                {
                    WriteType(sb, checker, file, node, hadErrors);
                }
            }
            children.Clear();
            NodeVisitor.ForEachChild<object>(store, node, child =>
            {
                children.Add(child);
                return null;
            });
            for (var i = children.Count - 1; i >= 0; i--)
            {
                work.Push(children[i]);
            }
        }
    }

    /// <summary>
    /// typeWriter.ts visitNode's filter: <c>isExpressionNode(node) || node.kind === Identifier || isDeclarationName(node)</c>.
    /// </summary>
    private static bool IsWalkCandidate(NodeStore store, Node node) =>
        Utilities.IsExpressionNode(store, node) || store.Kind(node) == SyntaxKind.Identifier || Utilities.IsDeclarationName(store, node);

    private static void WriteType(StringBuilder sb, Checker checker, SourceFile file, Node node, bool hadErrors)
    {
        var store = file.Store;
        var parent = store.Parent(node);
        var kind = store.Kind(node);
        var isTypeAliasName = store.Kind(parent) == SyntaxKind.TypeAliasDeclaration && store.Name(parent) == node;
        if (Utilities.IsPartOfTypeNode(store, node) ||
            (kind == SyntaxKind.Identifier && (Utilities.GetMeaningFromDeclaration(store, parent) & SemanticMeaning.Value) == 0 && !isTypeAliasName))
        {
            return;
        }
        if (kind == SyntaxKind.OmittedExpression)
        {
            return;
        }
        var type = Utilities.IsExpressionWithTypeArgumentsInClassExtendsClause(store, parent) ? checker.GetTypeAtLocation(checker.Ref(file, parent)) : NeverSharp.Optimized.Type.None;
        if (type.IsNone || (checker.GetTypeFlags(type) & TypeFlags.Any) != 0)
        {
            type = checker.GetTypeAtLocation(checker.Ref(file, node));
        }
        string typeString;
        string? underline = null;
        if (!hadErrors && (checker.GetTypeFlags(type) & TypeFlags.Any) != 0 &&
            store.Kind(parent) != SyntaxKind.BindingElement &&
            !Utilities.IsPropertyAccessOrQualifiedName(store, parent) &&
            !Utilities.IsLabelName(store, node) &&
            !(store.Kind(parent) == SyntaxKind.ModuleDeclaration && Utilities.IsGlobalScopeAugmentation(store, parent)) &&
            store.Kind(parent) != SyntaxKind.MetaProperty &&
            !IsImportStatementName(store, node) &&
            !IsExportStatementName(store, node) &&
            !IsIntrinsicJsxTag(store, file, node))
        {
            typeString = checker.IntrinsicNameOf(type);
        }
        else
        {
            var enclosing = checker.Ref(file, parent);
            (typeString, underline) = checker.TypeToBaselineText(type, enclosing, inTypeAlias: false);
            if (kind == SyntaxKind.Identifier && isTypeAliasName && typeString == store.Texts.GetString(store.EscapedText(node)))
            {
                (typeString, underline) = checker.TypeToBaselineText(type, enclosing, inTypeAlias: true);
            }
        }
        var (prefix, lineText) = LinePrefix(file, node);
        sb.Append(prefix).Append(lineText).Append(" : ").Append(EscapeLine(typeString)).Append('\n');
        if (!string.IsNullOrEmpty(underline))
        {
            sb.Append(prefix).Append(' ', lineText.Length).Append(" : ").Append(EscapeLine(underline)).Append('\n');
        }
    }

    // typeWriter.ts isImportStatementName
    private static bool IsImportStatementName(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        return store.Kind(parent) switch
        {
            SyntaxKind.ImportSpecifier => store.Name(parent) == node || store.PropertyName(parent) == node,
            SyntaxKind.ImportClause => store.Name(parent) == node,
            SyntaxKind.ImportEqualsDeclaration => store.Name(parent) == node,
            _ => false,
        };
    }

    // typeWriter.ts isExportStatementName
    private static bool IsExportStatementName(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        return store.Kind(parent) switch
        {
            SyntaxKind.ExportAssignment => store.Expression(parent) == node,
            SyntaxKind.ExportSpecifier => store.Name(parent) == node || store.PropertyName(parent) == node,
            _ => false,
        };
    }

    // typeWriter.ts isIntrinsicJsxTag
    private static bool IsIntrinsicJsxTag(NodeStore store, SourceFile file, Node node)
    {
        var p = store.Parent(node);
        if (store.Kind(p) is not (SyntaxKind.JsxOpeningElement or SyntaxKind.JsxClosingElement or SyntaxKind.JsxSelfClosingElement))
        {
            return false;
        }
        if (store.TagName(p) != node)
        {
            return false;
        }
        return Utilities.IsIntrinsicJsxName(Utilities.GetSourceTextOfNodeFromSourceFile(store, file.Root, node));
    }

    private static void WriteSymbol(StringBuilder sb, Checker checker, SourceFile file, Node node)
    {
        var symbol = checker.GetSymbolAtLocationPublic(checker.Ref(file, node));
        if (symbol.IsNone)
        {
            return;
        }
        var symbolString = new StringBuilder("Symbol(").Append(checker.SymbolToString(symbol, checker.Ref(file, file.Store.Parent(node))));
        var declarations = checker.Declarations(symbol);
        if (declarations is not null)
        {
            var count = 0;
            foreach (var declaration in declarations)
            {
                if (count >= 5)
                {
                    symbolString.Append(CultureInfo.InvariantCulture, $" ... and {declarations.Count - count} more");
                    break;
                }
                count++;
                var declFile = checker.FileAt(declaration.File);
                var fileName = TsPath.GetBaseFileName(declFile.Store.FileName(declFile.Root));
                var isLibFile = IsLibFileName(fileName);
                var position = Scanner.GetLineAndCharacterOfPosition(new SourceFileNodeLike(declFile.Store, declFile.Root), declFile.Store.Pos(declaration.Node));
                symbolString.Append(", Decl(").Append(fileName).Append(", ")
                    .Append(isLibFile ? "--" : position.Line.ToString(CultureInfo.InvariantCulture)).Append(", ")
                    .Append(isLibFile ? "--" : position.Character.ToString(CultureInfo.InvariantCulture)).Append(')');
            }
        }
        symbolString.Append(')');
        var (prefix, lineText) = LinePrefix(file, node);
        sb.Append(prefix).Append(lineText).Append(" : ").Append(EscapeLine(symbolString.ToString())).Append('\n');
    }

    // typeWriter.ts: /lib.*\.d\.ts/i on the base name.
    private static bool IsLibFileName(string fileName)
    {
        var lower = fileName.ToUpperInvariant();
        var lib = lower.IndexOf("LIB", StringComparison.Ordinal);
        return lib >= 0 && lower.IndexOf(".D.TS", lib + 3, StringComparison.Ordinal) >= 0;
    }

    // "<line> <Kind> >" and the node's source text without line breaks (as the .types baseline writes it).
    private static (string Prefix, string LineText) LinePrefix(SourceFile file, Node node)
    {
        var store = file.Store;
        var text = store.SourceFileText(file.Root).Chars;
        var actualPos = Scanner.SkipTrivia(text, store.Pos(node));
        var line = Scanner.GetLineAndCharacterOfPosition(new SourceFileNodeLike(store, file.Root), actualPos).Line;
        var sourceText = Utilities.GetSourceTextOfNodeFromSourceFile(store, file.Root, node);
        var lineText = EscapeLine(sourceText.Replace("\r\n", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal));
        var kind = KindNames.TryGetValue((int)store.Kind(node), out var name) ? name : "undefined";
        return (string.Create(CultureInfo.InvariantCulture, $"{line} {kind} >"), lineText);
    }

    // The dump's line-safe escapes: CR, LF, U+2028, U+2029 as backslash r, backslash n, backslash u2028, backslash u2029.
    private static string EscapeLine(string s)
    {
        if (s.AsSpan().IndexOfAny('\r', '\n', (char)0x2028) < 0 && !s.Contains((char)0x2029, StringComparison.Ordinal))
        {
            return s;
        }
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case (char)0x2028:
                    sb.Append("\\u2028");
                    break;
                case (char)0x2029:
                    sb.Append("\\u2029");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
