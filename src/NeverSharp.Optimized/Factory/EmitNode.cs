// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/emitNode.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>types.ts: SynthesizedComment (pos and end are -1).</summary>
internal sealed class SynthesizedComment : TextRange
{
    public int Pos { get; set; } = -1;

    public int End { get; set; } = -1;

    public SyntaxKind Kind;

    public bool? HasTrailingNewLine;

    public string Text = null!;

    public bool? HasLeadingNewline;
}

/// <summary>types.ts: EmitNode (stored in Node.EmitNode).</summary>
internal sealed class EmitNode
{
    public EmitFlags Flags;                          // Flags that customize emit
    public InternalEmitFlags InternalFlags;          // Internal flags that customize emit
    public List<Node>? AnnotatedNodes;               // Tracks Parse-tree nodes with EmitNodes for eventual cleanup.
    public List<SynthesizedComment>? LeadingComments;  // Synthesized leading comments
    public List<SynthesizedComment>? TrailingComments; // Synthesized trailing comments
    public TextRange? CommentRange;                  // The text range to use when emitting leading or trailing comments
    public TextRange? SourceMapRange;                // The text range to use when emitting leading or trailing source mappings (TypeScript: SourceMapRange)
    public List<TextRange?>? TokenSourceMapRanges;   // The text range to use when emitting source mappings for tokens
    public object? ConstantValue;                    // The constant value of an expression (string | number)
    public Node ExternalHelpersModuleName;    // The local name for an imported helpers module
    public bool? ExternalHelpers;
    public List<EmitHelper>? Helpers;                // Emit helpers for the node
    public bool? StartsOnNewLine;                    // If the node should begin on a new line
    public object? SnippetElement;                   // Snippet element of the node (TypeScript: SnippetElement)
    public Node TypeNode;                           // VariableDeclaration type
    public Node ClassThis;                    // Identifier that points to a captured static `this` for a class which may be updated after decorators are applied
    public Node AssignedName;                       // Expression used as the assigned name of a class or function
    public NodeList IdentifierTypeArguments;       // Only defined on synthesized identifiers.
    public AutoGenerateInfo? AutoGenerate;           // Used for auto-generated identifiers and private identifiers
    public Node GeneratedImportReference; // Reference to the generated import specifier this identifier refers to
}

/// <summary>types.ts: AutoGenerateInfo (module D1).</summary>
internal sealed class AutoGenerateInfo
{
    public GeneratedIdentifierFlags Flags;          // Specifies whether to auto-generate the text for an identifier.
    public int Id;                                  // Ensures unique generated identifiers get unique names, but clones get the same name.
    public string Prefix = "";                      // Optional prefix to apply to the start of the generated name (the string form of TypeScript's string | GeneratedNamePart)
    public GeneratedNamePart? PrefixPart;           // The GeneratedNamePart form of the prefix (module D4); Prefix is "" when it is set
    public string Suffix = "";                      // Optional suffix to apply to the end of the generated name
}

/// <summary>types.ts GeneratedNamePart (module D4): a prefix whose text is sourced from a node when the name is generated.</summary>
internal sealed class GeneratedNamePart
{
    public string Prefix = "";                      // an additional prefix to insert before the text sourced from `node`
    public Node Node;                               // Identifier | PrivateIdentifier
    public string Suffix = "";                      // an additional suffix to insert after the text sourced from `node`
}

/// <summary>types.ts: LexicalEnvironmentFlags (module D1).</summary>
[Flags]
internal enum LexicalEnvironmentFlags
{
    None = 0,
    InParameters = 1 << 0, // currently visiting a parameter list
    VariablesHoistedInParameters = 1 << 1, // a temp variable was hoisted while visiting a parameter list
}

/// <summary>factory/emitNode.ts.</summary>
internal static class FactoryEmitNode
{
    /// <summary>
    /// Associates a node with the current transformation, initializing
    /// various transient transformation properties.
    /// </summary>
    internal static EmitNode GetOrCreateEmitNode(NodeStore store, Node node)
    {
        if (store.EmitNode(node) == null)
        {
            if (UtilitiesPublic.IsParseTreeNode(store, node))
            {
                // To avoid holding onto transformation artifacts, we keep track of any
                // parse tree node we are annotating. This allows us to clean them up after
                // all transformations have completed.
                if (store.Kind(node) == SyntaxKind.SourceFile)
                {
                    var created = new EmitNode { AnnotatedNodes = [node] };
                    store.SetEmitNode(node, created);
                    return created;
                }

                var sourceFile = Utilities.GetSourceFileOfNode(store, UtilitiesPublic.GetParseTreeNode(store, Utilities.GetSourceFileOfNode(store, node)));
                if (sourceFile.IsNone)
                {
                    Debug.Fail("Could not determine parsed source file.");
                }
                GetOrCreateEmitNode(store, sourceFile).AnnotatedNodes!.Add(node);
            }

            store.SetEmitNode(node, new EmitNode());
        }
        else
        {
            Debug.Assert((((EmitNode)store.EmitNode(node)!).InternalFlags & InternalEmitFlags.Immutable) == 0, "Invalid attempt to mutate an immutable node.");
        }
        return (EmitNode)store.EmitNode(node)!;
    }

    /// <summary>
    /// Gets a custom text range to use when emitting source maps.
    /// </summary>
    internal static TextRange GetSourceMapRange(NodeStore store, Node node)
    {
        return (store.EmitNode(node) as EmitNode)?.SourceMapRange ?? new ObjectTextRange { Pos = store.Pos(node), End = store.End(node) };
    }

    /// <summary>
    /// Gets a custom text range to use when emitting comments.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool? GetStartsOnNewLine(NodeStore store, Node node)
    {
        return (store.EmitNode(node) as EmitNode)?.StartsOnNewLine;
    }

    /// <summary>
    /// Sets a custom text range to use when emitting comments.
    /// </summary>
    internal static Node SetStartsOnNewLine(NodeStore store, Node node, bool newLine)
    {
        GetOrCreateEmitNode(store, node).StartsOnNewLine = newLine;
        return node;
    }

    /// <summary>
    /// Gets a custom text range to use when emitting comments.
    /// </summary>
    internal static TextRange GetCommentRange(NodeStore store, Node node)
    {
        return (store.EmitNode(node) as EmitNode)?.CommentRange ?? new ObjectTextRange { Pos = store.Pos(node), End = store.End(node) };
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static List<SynthesizedComment>? GetSyntheticLeadingComments(NodeStore store, Node node)
    {
        return (store.EmitNode(node) as EmitNode)?.LeadingComments;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static List<SynthesizedComment>? GetSyntheticTrailingComments(NodeStore store, Node node)
    {
        return (store.EmitNode(node) as EmitNode)?.TrailingComments;
    }

    //// Module D1: the rest of emitNode.ts the transformers use.

    /// <summary>
    /// Clears any `EmitNode` entries from parse-tree nodes.
    /// (Track O: the transformers write into the emit copy of the file's store, which is dropped after the emit; the
    /// annotated parse tree nodes of the copy are cleared as TypeScript does.)
    /// </summary>
    internal static void DisposeEmitNodes(NodeStore store, Node sourceFile)
    {
        // During transformation we may need to annotate a parse tree node with transient
        // transformation properties. As parse tree nodes live longer than transformation
        // nodes, we need to make sure we reclaim any memory allocated for custom ranges
        // from these nodes to ensure we do not hold onto entire subtrees just for position
        // information. We also need to reset these nodes to a pre-transformation state
        // for incremental parsing scenarios so that we do not impact later emit.
        var parseTreeFile = UtilitiesPublic.GetParseTreeNode(store, sourceFile);
        var file = parseTreeFile.IsNone ? Node.None : Utilities.GetSourceFileOfNode(store, parseTreeFile);
        var annotatedNodes = file.IsNone ? null : (store.EmitNode(file) as EmitNode)?.AnnotatedNodes;
        if (annotatedNodes != null)
        {
            foreach (var node in annotatedNodes)
            {
                store.SetEmitNode(node, null);
            }
        }
    }

    /// <summary>Sets `EmitFlags.NoComments` on a node and removes any leading and trailing synthetic comments.</summary>
    internal static Node RemoveAllComments(NodeStore store, Node node)
    {
        var emitNode = GetOrCreateEmitNode(store, node);
        emitNode.Flags |= EmitFlags.NoComments;
        emitNode.LeadingComments = null;
        emitNode.TrailingComments = null;
        return node;
    }

    /// <summary>Sets flags that control emit behavior of a node.</summary>
    internal static Node SetEmitFlags(NodeStore store, Node node, EmitFlags emitFlags)
    {
        GetOrCreateEmitNode(store, node).Flags = emitFlags;
        return node;
    }

    /// <summary>Sets flags that control emit behavior of a node.</summary>
    internal static Node AddEmitFlags(NodeStore store, Node node, EmitFlags emitFlags)
    {
        var emitNode = GetOrCreateEmitNode(store, node);
        emitNode.Flags |= emitFlags;
        return node;
    }

    /// <summary>Sets flags that control emit behavior of a node.</summary>
    internal static Node SetInternalEmitFlags(NodeStore store, Node node, InternalEmitFlags emitFlags)
    {
        GetOrCreateEmitNode(store, node).InternalFlags = emitFlags;
        return node;
    }

    /// <summary>Sets flags that control emit behavior of a node.</summary>
    internal static Node AddInternalEmitFlags(NodeStore store, Node node, InternalEmitFlags emitFlags)
    {
        var emitNode = GetOrCreateEmitNode(store, node);
        emitNode.InternalFlags |= emitFlags;
        return node;
    }

    /// <summary>Gets the internal flags of a node (utilities.ts getInternalEmitFlags).</summary>
    internal static InternalEmitFlags GetInternalEmitFlags(NodeStore store, Node node)
    {
        return (store.EmitNode(node) as EmitNode)?.InternalFlags ?? InternalEmitFlags.None;
    }

    /// <summary>Sets a custom text range to use when emitting source maps (Track O: no source maps, so nothing reads it).</summary>
    internal static Node SetSourceMapRange(NodeStore store, Node node, TextRange? range)
    {
        GetOrCreateEmitNode(store, node).SourceMapRange = range;
        return node;
    }

    /// <summary>Sets a custom text range to use when emitting comments.</summary>
    internal static Node SetCommentRange(NodeStore store, Node node, TextRange range)
    {
        GetOrCreateEmitNode(store, node).CommentRange = range;
        return node;
    }

    /// <summary>Sets a custom text range to use when emitting comments (TypeScript passes a node as the range).</summary>
    internal static Node SetCommentRange(NodeStore store, Node node, Node range)
    {
        return SetCommentRange(store, node, new ObjectTextRange { Pos = store.Pos(range), End = store.End(range) });
    }

    internal static Node SetSyntheticLeadingComments(NodeStore store, Node node, List<SynthesizedComment>? comments)
    {
        GetOrCreateEmitNode(store, node).LeadingComments = comments;
        return node;
    }

    internal static Node AddSyntheticLeadingComment(NodeStore store, Node node, SyntaxKind kind, string text, bool? hasTrailingNewLine = null)
    {
        return SetSyntheticLeadingComments(store, node, Core.Append(GetSyntheticLeadingComments(store, node), new SynthesizedComment { Kind = kind, HasTrailingNewLine = hasTrailingNewLine, Text = text }));
    }

    internal static Node SetSyntheticTrailingComments(NodeStore store, Node node, List<SynthesizedComment>? comments)
    {
        GetOrCreateEmitNode(store, node).TrailingComments = comments;
        return node;
    }

    internal static Node AddSyntheticTrailingComment(NodeStore store, Node node, SyntaxKind kind, string text, bool? hasTrailingNewLine = null)
    {
        return SetSyntheticTrailingComments(store, node, Core.Append(GetSyntheticTrailingComments(store, node), new SynthesizedComment { Kind = kind, HasTrailingNewLine = hasTrailingNewLine, Text = text }));
    }

    internal static Node MoveSyntheticComments(NodeStore store, Node node, Node original)
    {
        SetSyntheticLeadingComments(store, node, GetSyntheticLeadingComments(store, original));
        SetSyntheticTrailingComments(store, node, GetSyntheticTrailingComments(store, original));
        var emit = GetOrCreateEmitNode(store, original);
        emit.LeadingComments = null;
        emit.TrailingComments = null;
        return node;
    }

    /// <summary>Gets the constant value to emit for an expression representing an enum (string | number, or null).</summary>
    internal static object? GetConstantValue(NodeStore store, Node node)
    {
        return (store.EmitNode(node) as EmitNode)?.ConstantValue;
    }

    /// <summary>Sets the constant value to emit for an expression representing an enum.</summary>
    internal static Node SetConstantValue(NodeStore store, Node node, object value)
    {
        var emitNode = GetOrCreateEmitNode(store, node);
        emitNode.ConstantValue = value;
        return node;
    }

    /// <summary>Adds an EmitHelper to a node.</summary>
    internal static Node AddEmitHelper(NodeStore store, Node node, EmitHelper helper)
    {
        var emitNode = GetOrCreateEmitNode(store, node);
        emitNode.Helpers = Core.Append(emitNode.Helpers, helper);
        return node;
    }

    /// <summary>Add EmitHelpers to a node.</summary>
    internal static Node AddEmitHelpers(NodeStore store, Node node, List<EmitHelper>? helpers)
    {
        if (Core.Some(helpers))
        {
            var emitNode = GetOrCreateEmitNode(store, node);
            foreach (var helper in helpers)
            {
                emitNode.Helpers = Core.AppendIfUnique(emitNode.Helpers, helper);
            }
        }
        return node;
    }

    /// <summary>Removes an EmitHelper from a node.</summary>
    internal static bool RemoveEmitHelper(NodeStore store, Node node, EmitHelper helper)
    {
        var helpers = (store.EmitNode(node) as EmitNode)?.Helpers;
        if (helpers != null)
        {
            return helpers.Remove(helper);
        }
        return false;
    }

    /// <summary>Gets the EmitHelpers of a node.</summary>
    internal static List<EmitHelper>? GetEmitHelpers(NodeStore store, Node node)
    {
        return (store.EmitNode(node) as EmitNode)?.Helpers;
    }

    /// <summary>Moves matching emit helpers from a source node to a target node.</summary>
    internal static void MoveEmitHelpers(NodeStore store, Node source, Node target, Func<EmitHelper, bool> predicate)
    {
        var sourceEmitNode = store.EmitNode(source) as EmitNode;
        var sourceEmitHelpers = sourceEmitNode?.Helpers;
        if (!Core.Some(sourceEmitHelpers))
        {
            return;
        }

        var targetEmitNode = GetOrCreateEmitNode(store, target);
        var helpersRemoved = 0;
        for (var i = 0; i < sourceEmitHelpers.Count; i++)
        {
            var helper = sourceEmitHelpers[i];
            if (predicate(helper))
            {
                helpersRemoved++;
                targetEmitNode.Helpers = Core.AppendIfUnique(targetEmitNode.Helpers, helper);
            }
            else if (helpersRemoved > 0)
            {
                sourceEmitHelpers[i - helpersRemoved] = helper;
            }
        }

        if (helpersRemoved > 0)
        {
            sourceEmitHelpers.RemoveRange(sourceEmitHelpers.Count - helpersRemoved, helpersRemoved);
        }
    }

    internal static Node IgnoreSourceNewlines(NodeStore store, Node node)
    {
        GetOrCreateEmitNode(store, node).InternalFlags |= InternalEmitFlags.IgnoreSourceNewlines;
        return node;
    }

    internal static Node SetTypeNode(NodeStore store, Node node, Node type)
    {
        var emitNode = GetOrCreateEmitNode(store, node);
        emitNode.TypeNode = type;
        return node;
    }

    internal static Node GetTypeNode(NodeStore store, Node node)
    {
        return (store.EmitNode(node) as EmitNode)?.TypeNode ?? Node.None;
    }

    internal static Node SetIdentifierAutoGenerate(NodeStore store, Node node, AutoGenerateInfo? autoGenerate)
    {
        GetOrCreateEmitNode(store, node).AutoGenerate = autoGenerate;
        return node;
    }

    internal static AutoGenerateInfo? GetIdentifierAutoGenerate(NodeStore store, Node node)
    {
        return (store.EmitNode(node) as EmitNode)?.AutoGenerate;
    }

    internal static Node SetIdentifierGeneratedImportReference(NodeStore store, Node node, Node value)
    {
        GetOrCreateEmitNode(store, node).GeneratedImportReference = value;
        return node;
    }

    internal static Node GetIdentifierGeneratedImportReference(NodeStore store, Node node)
    {
        return (store.EmitNode(node) as EmitNode)?.GeneratedImportReference ?? Node.None;
    }
}
