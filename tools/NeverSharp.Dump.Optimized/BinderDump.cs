// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): binder dump of Track O for Gate B (module B0).
//
// Reproduces harness/astdump/dump-ts-binder.mjs (binder dump format version 1, described there): each unit is parsed
// with Parser.CreateSourceFile(fileName, text, { languageVersion: Latest, jsDocParsingMode }, setParentNodes: true,
// scriptKind), bound with Binder.BindSourceFile(file, new CompilerOptions()) and written from Track O's binder output
// (SourceFile.Binding): sections symbols, flow and diagnostics. Units that do not finish within the timeout are
// recorded as timeouts, as by the tsc dumper.

using System.Globalization;
using System.Text;
using System.Text.Json;
using NeverSharp.Generated;
using NeverSharp.Optimized;

namespace NeverSharp.Dump;

internal static class BinderDump
{
    private const int StackSize = 256 * 1024 * 1024;

    private static readonly Dictionary<int, string> KindNames = Common.Names(EnumNames.SyntaxKind);

    // Single-bit members of types.ts' SymbolFlags and FlowFlags in bit order (the first declared name per value, as
    // dump-ts-binder.mjs chooses them).
    private static readonly (int Bit, string Name)[] SymbolFlagBits =
    [
        (1 << 0, "FunctionScopedVariable"), (1 << 1, "BlockScopedVariable"), (1 << 2, "Property"), (1 << 3, "EnumMember"),
        (1 << 4, "Function"), (1 << 5, "Class"), (1 << 6, "Interface"), (1 << 7, "ConstEnum"), (1 << 8, "RegularEnum"),
        (1 << 9, "ValueModule"), (1 << 10, "NamespaceModule"), (1 << 11, "TypeLiteral"), (1 << 12, "ObjectLiteral"),
        (1 << 13, "Method"), (1 << 14, "Constructor"), (1 << 15, "GetAccessor"), (1 << 16, "SetAccessor"),
        (1 << 17, "Signature"), (1 << 18, "TypeParameter"), (1 << 19, "TypeAlias"), (1 << 20, "ExportValue"),
        (1 << 21, "Alias"), (1 << 22, "Prototype"), (1 << 23, "ExportStar"), (1 << 24, "Optional"), (1 << 25, "Transient"),
        (1 << 26, "Assignment"), (1 << 27, "ModuleExports"),
    ];

    private static readonly (int Bit, string Name)[] FlowFlagBits =
    [
        (1 << 0, "Unreachable"), (1 << 1, "Start"), (1 << 2, "BranchLabel"), (1 << 3, "LoopLabel"), (1 << 4, "Assignment"),
        (1 << 5, "TrueCondition"), (1 << 6, "FalseCondition"), (1 << 7, "SwitchClause"), (1 << 8, "ArrayMutation"),
        (1 << 9, "Call"), (1 << 10, "ReduceLabel"), (1 << 11, "Referenced"), (1 << 12, "Shared"),
    ];

    public static int Run(Options options)
    {
        var corpus = options.Corpus;
        var jsdocMode = options.Get("--jsdoc-mode", "all");
        var mode = jsdocMode switch
        {
            "all" => JSDocParsingMode.ParseAll,
            "none" => JSDocParsingMode.ParseNone,
            "types" => JSDocParsingMode.ParseForTypeErrors,
            _ => throw new InvalidOperationException("--jsdoc-mode must be all, none or types"),
        };
        var dumpName = $"cs-{Track.Name}-binder-{jsdocMode}";
        var outDir = Path.GetFullPath(options.Get("--out", Path.Combine(corpus, "dumps", dumpName)));
        var timeoutMs = int.Parse(options.Get("--timeout-ms", "10000"), CultureInfo.InvariantCulture);
        Common.ResetDirectory(outDir);
        var units = Common.ReadUnits(corpus);
        var ok = new List<string>();
        var timeout = new List<string>();
        var error = new List<(string Id, string Error)>();
        var started = DateTime.UtcNow;
        Parallel.ForEach(units, unit =>
        {
            var text = Common.ReadUnitText(corpus, unit);
            // As AstDump.Run: each unit on its own thread with a large stack (parser and binder recurse as deeply as
            // the source nests); a unit that never finishes keeps its thread, the process exits at the end regardless.
            string? result = null;
            string? outcome = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = DumpUnit(text, unit, mode);
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
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{dumpName}: ok {ok.Count}, timeout {timeout.Count}, error {error.Count} in {(DateTime.UtcNow - started).TotalSeconds:0.0} s"));
        for (var i = 0; i < Math.Min(5, error.Count); i++)
        {
            Console.WriteLine($"error {error[i].Id}: {error[i].Error}");
        }
        Environment.Exit(0); // threads of timed-out units may still run
        return 0;
    }

    private static List<Node> Walk(NodeStore store, Node root)
    {
        var order = new List<Node>();
        void Visit(Node node)
        {
            order.Add(node);
            var jsDoc = store.JsDoc(node);
            if (jsDoc is not null)
            {
                foreach (var comment in jsDoc)
                {
                    Visit(comment);
                }
            }
            NodeVisitor.ForEachChild<object>(store, node, child =>
            {
                Visit(child);
                return null;
            });
        }
        Visit(root);
        return order;
    }

    private static string DumpUnit(string text, Unit unit, JSDocParsingMode mode)
    {
        var scriptKind = unit.Kind switch
        {
            "TS" => ScriptKind.TS,
            "TSX" => ScriptKind.TSX,
            "JS" => ScriptKind.JS,
            "JSX" => ScriptKind.JSX,
            "JSON" => ScriptKind.JSON,
            _ => throw new InvalidOperationException(unit.Kind),
        };
        var file = Parser.CreateSourceFile(unit.FileName, text,
            new CreateSourceFileOptions { LanguageVersion = ScriptTarget.Latest, JsDocParsingMode = mode },
            setParentNodes: true, scriptKind);
        var store = file.Store;
        var order = Walk(store, file.Root);
        var flagsBefore = order.ConvertAll(n => (int)store.Flags(n));
        Binder.BindSourceFile(file, new CompilerOptions());
        var after = Walk(store, file.Root);
        if (after.Count != order.Count || !after.SequenceEqual(order))
        {
            throw new InvalidOperationException("the binder changed the tree");
        }
        var b = file.Binding ?? throw new InvalidOperationException("Binder.BindSourceFile left no binder output");
        var index = new Dictionary<Node, int>(order.Count);
        for (var i = 0; i < order.Count; i++)
        {
            index[order[i]] = i;
        }
        var sb = new StringBuilder();

        string Kind(Node n) => KindNames.TryGetValue((int)store.Kind(n), out var name) ? name : "undefined";
        string Ref(Node n) => n.IsNone ? "-" : string.Create(CultureInfo.InvariantCulture,
            $"{Kind(n)}:{store.Pos(n)}-{store.End(n)}#{(index.TryGetValue(n, out var i) ? i.ToString(CultureInfo.InvariantCulture) : "?")}");
        void Head(Node n, int i) => sb.Append(CultureInfo.InvariantCulture, $"#{i} {Kind(n)} {store.Pos(n)} {store.End(n)}");

        // symbols
        var symbolIds = new Dictionary<Symbol, int>();
        var symbols = new List<Symbol>();
        string Sid(Symbol s)
        {
            if (!symbolIds.TryGetValue(s, out var id))
            {
                id = symbols.Count;
                symbolIds[s] = id;
                symbols.Add(s);
            }
            return "s" + id.ToString(CultureInfo.InvariantCulture);
        }
        string Table(SymbolTable t)
        {
            var parts = new List<string>();
            foreach (var (key, s) in b.Entries(t))
            {
                parts.Add((store.Texts.Equals(key, b.Name(s)) ? "" : Json(store.Texts.GetString(key) ?? "") + "=") + Sid(s));
            }
            return "[" + string.Join(',', parts) + "]";
        }
        string Nodes(BinderStore.NodeChain chain)
        {
            var parts = new List<string>();
            foreach (var n in chain)
            {
                parts.Add(Ref(n));
            }
            return "[" + string.Join(',', parts) + "]";
        }
        sb.Append("== symbols\n");
        for (var i = 0; i < order.Count; i++)
        {
            var n = order[i];
            var line = new StringBuilder();
            var symbol = b.SymbolOf(n);
            if (!symbol.IsNone)
            {
                line.Append(" symbol=").Append(Sid(symbol));
            }
            var localSymbol = b.LocalSymbolOf(n);
            if (!localSymbol.IsNone)
            {
                line.Append(" localSymbol=").Append(Sid(localSymbol));
            }
            var locals = b.LocalsOf(n);
            if (!locals.IsNone)
            {
                line.Append(" locals=").Append(Table(locals));
            }
            var next = b.NextContainerOf(n);
            if (!next.IsNone)
            {
                line.Append(" next=").Append(Ref(next));
            }
            if (line.Length > 0)
            {
                Head(n, i);
                sb.Append(line).Append('\n');
            }
        }
        for (var k = 0; k < symbols.Count; k++)
        {
            var s = symbols[k];
            sb.Append(CultureInfo.InvariantCulture, $"s{k} {Json(store.Texts.GetString(b.Name(s)) ?? "")} {FlagsText(SymbolFlagBits, (int)b.Flags(s))}");
            var declarations = b.Declarations(s);
            if (!declarations.IsUndefined)
            {
                sb.Append(" decl=").Append(Nodes(declarations));
            }
            if (!b.ValueDeclaration(s).IsNone)
            {
                sb.Append(" value=").Append(Ref(b.ValueDeclaration(s)));
            }
            if (!b.Parent(s).IsNone)
            {
                sb.Append(" parent=").Append(Sid(b.Parent(s)));
            }
            if (!b.ExportSymbol(s).IsNone)
            {
                sb.Append(" export=").Append(Sid(b.ExportSymbol(s)));
            }
            if (!b.Members(s).IsNone)
            {
                sb.Append(" members=").Append(Table(b.Members(s)));
            }
            if (!b.Exports(s).IsNone)
            {
                sb.Append(" exports=").Append(Table(b.Exports(s)));
            }
            if (!b.GlobalExports(s).IsNone)
            {
                sb.Append(" globalExports=").Append(Table(b.GlobalExports(s)));
            }
            var assignmentMembers = b.AssignmentDeclarationMembers(s);
            if (!assignmentMembers.IsUndefined)
            {
                sb.Append(" assignmentMembers=").Append(Nodes(assignmentMembers));
            }
            if (b.ConstEnumOnlyModule(s) is { } constEnumOnlyModule)
            {
                sb.Append(" constEnumOnlyModule=").Append(constEnumOnlyModule ? "true" : "false");
            }
            if (b.IsReplaceableByMethod(s) is { } replaceable)
            {
                sb.Append(" replaceableByMethod=").Append(replaceable ? "true" : "false");
            }
            sb.Append('\n');
        }

        // flow
        var flowIds = new Dictionary<FlowNode, int>();
        var flows = new List<FlowNode>();
        List<FlowNode> Successors(FlowNode f)
        {
            var next = new List<FlowNode>();
            var flags = b.Flags(f);
            if ((flags & FlowFlags.Label) != 0)
            {
                foreach (var a in b.Antecedents(f))
                {
                    next.Add(a);
                }
            }
            else if (!b.Antecedent(f).IsNone)
            {
                next.Add(b.Antecedent(f));
            }
            if ((flags & FlowFlags.ReduceLabel) != 0)
            {
                next.Add(b.ReduceTarget(f));
                foreach (var a in b.ReduceAntecedents(f))
                {
                    next.Add(a);
                }
            }
            return next;
        }
        string Fid(FlowNode f)
        {
            var stack = new Stack<FlowNode>();
            stack.Push(f);
            while (stack.Count > 0)
            {
                var x = stack.Pop();
                if (flowIds.ContainsKey(x))
                {
                    continue;
                }
                flowIds[x] = flows.Count;
                flows.Add(x);
                var next = Successors(x);
                for (var j = next.Count - 1; j >= 0; j--)
                {
                    stack.Push(next[j]);
                }
            }
            return "f" + flowIds[f].ToString(CultureInfo.InvariantCulture);
        }
        string FlowIds(BinderStore.FlowChain chain)
        {
            var parts = new List<string>();
            foreach (var a in chain)
            {
                parts.Add("f" + flowIds[a].ToString(CultureInfo.InvariantCulture));
            }
            return "[" + string.Join(',', parts) + "]";
        }
        sb.Append("== flow\n");
        for (var i = 0; i < order.Count; i++)
        {
            var n = order[i];
            var line = new StringBuilder();
            foreach (var (field, flow) in new[]
            {
                ("flowNode", b.FlowNodeOf(n)), ("endFlowNode", b.EndFlowNodeOf(n)), ("returnFlowNode", b.ReturnFlowNodeOf(n)), ("fallthroughFlowNode", b.FallthroughFlowNodeOf(n)),
            })
            {
                if (!flow.IsNone)
                {
                    line.Append(' ').Append(field).Append('=').Append(Fid(flow));
                }
            }
            if (line.Length > 0)
            {
                Head(n, i);
                sb.Append(line).Append('\n');
            }
        }
        for (var k = 0; k < flows.Count; k++)
        {
            var f = flows[k];
            var flags = b.Flags(f);
            var shown = (flags & FlowFlags.Unreachable) != 0 ? flags & ~(FlowFlags.Referenced | FlowFlags.Shared) : flags;
            sb.Append(CultureInfo.InvariantCulture, $"f{k} {FlagsText(FlowFlagBits, (int)shown)}");
            if ((flags & FlowFlags.SwitchClause) != 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $" switch={Ref(b.NodeOf(f))} clause={b.ClauseStart(f)}-{b.ClauseEnd(f)}");
            }
            else if ((flags & FlowFlags.ReduceLabel) != 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $" target=f{flowIds[b.ReduceTarget(f)]} antecedents={FlowIds(b.ReduceAntecedents(f))}");
            }
            else if (!b.NodeOf(f).IsNone)
            {
                sb.Append(" node=").Append(Ref(b.NodeOf(f)));
            }
            if ((flags & FlowFlags.Label) != 0)
            {
                var antecedents = b.Antecedents(f);
                if (!antecedents.IsUndefined)
                {
                    sb.Append(" antes=").Append(FlowIds(antecedents));
                }
            }
            else if (!b.Antecedent(f).IsNone)
            {
                sb.Append(CultureInfo.InvariantCulture, $" ante=f{flowIds[b.Antecedent(f)]}");
            }
            sb.Append('\n');
        }

        // diagnostics
        sb.Append("== diagnostics\n");
        void Diagnostics(string tag, List<DiagnosticWithLocation>? list)
        {
            foreach (var d in list ?? [])
            {
                sb.Append(CultureInfo.InvariantCulture, $"{tag} {d.Start} {d.Length} {d.Code} {Category(d.Category)} {Json(d.MessageText)}");
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
                    sb.Append(CultureInfo.InvariantCulture,
                        $"  related {r.StartOrUndefined?.ToString(CultureInfo.InvariantCulture) ?? "undefined"} {r.LengthOrUndefined?.ToString(CultureInfo.InvariantCulture) ?? "undefined"} {r.Code} {Category(r.Category)} {Json(r.MessageText)}\n");
                }
            }
        }
        Diagnostics("diag", b.BindDiagnostics);
        Diagnostics("suggestion", b.BindSuggestionDiagnostics);
        for (var i = 0; i < order.Count; i++)
        {
            var before = flagsBefore[i];
            var now = (int)store.Flags(order[i]);
            if (now == before)
            {
                continue;
            }
            sb.Append("flags ");
            Head(order[i], i);
            sb.Append(CultureInfo.InvariantCulture, $" {before} {now}");
            foreach (var (bit, name) in EnumNames.NodeFlags)
            {
                if ((now & ~before & bit) != 0)
                {
                    sb.Append(" +").Append(name);
                }
            }
            foreach (var (bit, name) in EnumNames.NodeFlags)
            {
                if ((before & ~now & bit) != 0)
                {
                    sb.Append(" -").Append(name);
                }
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string FlagsText((int Bit, string Name)[] bits, int value)
    {
        var names = new List<string>();
        foreach (var (bit, name) in bits)
        {
            if ((value & bit) != 0)
            {
                names.Add(name);
            }
        }
        return value.ToString(CultureInfo.InvariantCulture) + " " + (names.Count == 0 ? "-" : string.Join('|', names));
    }

    private static string Category(DiagnosticCategory category) => category switch
    {
        DiagnosticCategory.Warning => "Warning",
        DiagnosticCategory.Error => "Error",
        DiagnosticCategory.Suggestion => "Suggestion",
        DiagnosticCategory.Message => "Message",
        _ => "undefined",
    };

    /// <summary>JavaScript's JSON.stringify of a string (well-formed: lone surrogates as \u escapes, lowercase hex).</summary>
    internal static string Json(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        sb.Append(c).Append(value[++i]);
                    }
                    else if (char.IsSurrogate(c))
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
