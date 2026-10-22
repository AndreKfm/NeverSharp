// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/parser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/parser/parser.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using System.Collections.Concurrent;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>parser.ts: CreateSourceFileOptions.</summary>
internal sealed class CreateSourceFileOptions
{
    public ScriptTarget LanguageVersion { get; set; }

    /// <summary>
    /// Controls the format the file is detected as - this can be derived from only the path
    /// and files on disk, but needs to be done with a module resolution cache in scope to be performant.
    /// This is usually `undefined` for compilations that do not have `moduleResolution` values of `node16` or `nodenext`.
    /// (TypeScript: ResolutionMode, i.e. ModuleKind.ESNext | ModuleKind.CommonJS | undefined.)
    /// </summary>
    public ModuleKind? ImpliedNodeFormat { get; set; }

    /// <summary>
    /// Controls how module-y-ness is set for the given file. Usually the result of calling
    /// `getSetExternalModuleIndicator` on a valid `CompilerOptions` object. If not present, the default
    /// check specified by `isFileProbablyExternalModule` will be used to set the field.
    /// </summary>
    public Action<Node>? SetExternalModuleIndicator { get; set; }

    public IReadOnlyList<string>? PackageJsonLocations { get; set; }

    // TypeScript: PackageJsonInfo (module resolution data, not used by the parser).
    public object? PackageJsonScope { get; set; }

    public JSDocParsingMode? JsDocParsingMode { get; set; }
}

/// <summary>
/// parser.ts. TypeScript keeps the parser's state in the variables of `namespace Parser`, a singleton that
/// shares one scanner across all parses. Here the state lives in the instances of this class (as in
/// typescript-go's `type Parser struct`, internal/parser/parser.go); the file-level functions of parser.ts
/// are its static members.
/// </summary>
internal sealed partial class Parser
{
    // Instances are reused across parses, as TypeScript reuses its singleton namespace; to parse on many threads
    // at once each parse takes its own instance from this pool.
    // typescript-go counterpart: parserPool, getParser, putParser (internal/parser/parser.go).
    private static readonly ConcurrentBag<Parser> ParserPool = [];

    private static Parser GetParser()
    {
        return ParserPool.TryTake(out var parser) ? parser : new Parser();
    }

    private static void PutParser(Parser parser)
    {
        ParserPool.Add(parser);
    }

    internal static bool IsJSDocLikeText(string text, int start)
    {
        return CharCodeAt(text, start + 1) == (int)CharacterCodes.asterisk &&
            CharCodeAt(text, start + 2) == (int)CharacterCodes.asterisk &&
            CharCodeAt(text, start + 3) != (int)CharacterCodes.slash;
    }

    internal static Node IsFileProbablyExternalModule(NodeStore store, Node sourceFile)
    {
        // Try to use the first top-level import/export when available, then
        // fall back to looking for an 'import.meta' somewhere in the tree if necessary.
        return (Core.ForEach(store.Statements(sourceFile), (statement, _) => IsAnExternalModuleIndicatorNode(store, statement)) is { IsNone: false } __t1 ? __t1 : GetImportMetaIfNecessary(store, sourceFile));
    }

    private static Node IsAnExternalModuleIndicatorNode(NodeStore store, Node node)
    {
        return FactoryUtilitiesPublic.CanHaveModifiers(store, node) && HasModifierOfKind(store, node, SyntaxKind.ExportKeyword)
                || FactoryNodeTests.IsImportEqualsDeclaration(store, node) && FactoryNodeTests.IsExternalModuleReference(store, store.ModuleReference(node))
                || FactoryNodeTests.IsImportDeclaration(store, node)
                || FactoryNodeTests.IsExportAssignment(store, node)
                || FactoryNodeTests.IsExportDeclaration(store, node) ? node : default;
    }

    private static Node GetImportMetaIfNecessary(NodeStore store, Node sourceFile)
    {
        return (store.Flags(sourceFile) & NodeFlags.PossiblyContainsImportMeta) != 0 ?
            WalkTreeForImportMeta(store, sourceFile) :
            default;
    }

    private static Node WalkTreeForImportMeta(NodeStore store, Node node)
    {
        if (IsImportMeta(store, node))
        {
            return node;
        }
        // (ForEachChild's callbacks return a reference or null: the node found is returned boxed.)
        var found = NodeVisitor.ForEachChild<object>(store, node, child => WalkTreeForImportMeta(store, child) is { IsNone: false } meta ? meta : null);
        return found is Node n ? n : default;
    }

    /// <summary>Do not use hasModifier inside the parser; it relies on parent pointers. Use this instead.</summary>
    private static bool HasModifierOfKind(NodeStore store, Node node, SyntaxKind kind)
    {
        return Core.Some(store.Modifiers(node), m => store.Kind(m) == kind);
    }

    private static bool IsImportMeta(NodeStore store, Node node)
    {
        return FactoryNodeTests.IsMetaProperty(store, node) && store.KeywordToken(node) == SyntaxKind.ImportKeyword && (string)store.EscapedText(store.Name(node))! == "meta";
    }

    /// <summary>
    /// Iterates through the children of a node recursively (depth-first, pre-order), as forEachChild does for one
    /// level. A callback that returns the string "skip" prevents the traversal of the current node's children.
    /// </summary>
    internal static T? ForEachChildRecursively<T>(NodeStore store, Node rootNode, Func<Node, Node, object?> cbNode, Func<NodeArray, Node, object?>? cbNodes = null)
        where T : class
    {
        var queue = GatherPossibleChildren(store, rootNode);
        var parents = new List<Node>(); // tracks parent references for elements in queue
        while (parents.Count < queue.Count)
        {
            parents.Add(rootNode);
        }
        while (queue.Count != 0)
        {
            var current = queue[^1];
            queue.RemoveAt(queue.Count - 1);
            var parent = parents[^1];
            parents.RemoveAt(parents.Count - 1);
            if (current is NodeArray currentArray)
            {
                if (cbNodes != null)
                {
                    var res = cbNodes(currentArray, parent);
                    if (IsTruthy(res))
                    {
                        if (res is string s && s == "skip")
                        {
                            continue;
                        }
                        return (T)res!;
                    }
                }
                for (var i = currentArray.Count - 1; i >= 0; --i)
                {
                    queue.Add(currentArray[i]);
                    parents.Add(parent);
                }
            }
            else
            {
                var currentNode = Node.FromObject(current);
                var res = cbNode(currentNode, parent);
                if (IsTruthy(res))
                {
                    if (res is string s && s == "skip")
                    {
                        continue;
                    }
                    return (T)res!;
                }
                if (store.Kind(currentNode) >= SyntaxKind.FirstNode)
                {
                    // add children in reverse order to the queue, so popping gives the first child
                    foreach (var child in GatherPossibleChildren(store, currentNode))
                    {
                        queue.Add(child);
                        parents.Add(currentNode);
                    }
                }
            }
        }
        return null;
    }

    private static List<object> GatherPossibleChildren(NodeStore store, Node node)
    {
        var children = new List<object>();
        // By using a stack above and `unshift` here, we emulate a depth-first preorder traversal
        NodeVisitor.ForEachChild<object>(store, node, n =>
        {
            children.Insert(0, n);
            return null;
        }, n =>
        {
            children.Insert(0, n);
            return null;
        });
        return children;
    }

    private static void SetExternalModuleIndicator(NodeStore store, Node sourceFile)
    {
        // TypeScript: `true | Node`; undefined when the file is not a module (a boxed handle only for a module).
        var indicator = IsFileProbablyExternalModule(store, sourceFile);
        store.SetExternalModuleIndicator(sourceFile, indicator.IsNone ? null : indicator);
    }

    internal static SourceFile CreateSourceFile(string fileName, string sourceText, ScriptTarget languageVersion, bool setParentNodes = false, ScriptKind? scriptKind = null)
    {
        return CreateSourceFile(fileName, sourceText, new CreateSourceFileOptions { LanguageVersion = languageVersion }, setParentNodes, scriptKind);
    }

    internal static SourceFile CreateSourceFile(string fileName, string sourceText, CreateSourceFileOptions languageVersionOrOptions, bool setParentNodes = false, ScriptKind? scriptKind = null)
    {
        // (tracing and performance marks are instrumentation and left out.)
        Node result;

        // Track O: the nodes of this parse live in their own store, which the result keeps.
        var store = new NodeStore(sourceText.Length);
        var languageVersion = languageVersionOrOptions.LanguageVersion;
        var overrideSetExternalModuleIndicator = languageVersionOrOptions.SetExternalModuleIndicator;
        var format = languageVersionOrOptions.ImpliedNodeFormat;
        var jsDocParsingMode = languageVersionOrOptions.JsDocParsingMode;
        var parser = GetParser();
        try
        {
            if (languageVersion == ScriptTarget.JSON)
            {
                result = parser.ParseSourceFile(store, fileName, sourceText, languageVersion, /*syntaxCursor*/ null, setParentNodes, ScriptKind.JSON, _ => { }, jsDocParsingMode ?? JSDocParsingMode.ParseAll);
            }
            else
            {
                Action<Node>? setIndicator = format == null ? overrideSetExternalModuleIndicator : file =>
                {
                    store.SetImpliedNodeFormat(file, format.Value);
                    if (overrideSetExternalModuleIndicator != null)
                    {
                        overrideSetExternalModuleIndicator(file);
                    }
                    else
                    {
                        SetExternalModuleIndicator(store, file);
                    }
                };
                result = parser.ParseSourceFile(store, fileName, sourceText, languageVersion, /*syntaxCursor*/ null, setParentNodes, scriptKind, setIndicator, jsDocParsingMode ?? JSDocParsingMode.ParseAll);
            }
        }
        finally
        {
            parser.UseStore(null!); // the result keeps the store, the pooled parser does not
            PutParser(parser);
        }

        return new SourceFile(store, result);
    }

    /// <summary>
    /// Parse json text into SyntaxTree and return node and parse errors if any
    /// </summary>
    internal static SourceFile ParseJsonText(string fileName, string sourceText)
    {
        var store = new NodeStore(sourceText.Length);
        var parser = GetParser();
        try
        {
            return new SourceFile(store, parser.ParseJsonText(store, fileName, sourceText, ScriptTarget.ES2015, null, false));
        }
        finally
        {
            parser.UseStore(null!);
            PutParser(parser);
        }
    }

    // See also `isExternalOrCommonJsModule` in utilities.ts
    internal static bool IsExternalModule(NodeStore store, Node file)
    {
        return store.ExternalModuleIndicator(file) != null;
    }

    //// JavaScript semantics the ported functions rely on.

    /// <summary>`text.charCodeAt(index)`; NaN (outside the string) is -1, which equals no character code.</summary>
    private static int CharCodeAt(string text, int index)
    {
        return (uint)index < (uint)text.Length ? text[index] : -1;
    }

    /// <summary>JavaScript truthiness of a value (`if (value)`): undefined, null, false, 0 and "" are falsy.</summary>
    private static bool IsTruthy<T>(T value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            string s => s.Length != 0,
            int i => i != 0,
            Tristate t => t != Tristate.False,
            Node n => !n.IsNone,
            _ => true,
        };
    }
}
