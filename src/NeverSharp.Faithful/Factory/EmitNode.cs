// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/emitNode.ts and src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>types.ts: SourceMapSource (the members the emitter's data model needs).</summary>
public sealed class SourceMapSource
{
    public string FileName { get; set; } = null!;

    public string Text { get; set; } = null!;

    public Func<int, int>? SkipTrivia { get; set; }
}

/// <summary>types.ts: SourceMapRange (extends TextRange).</summary>
public sealed class SourceMapRange : TextRange
{
    public int Pos { get; set; } = -1;

    public int End { get; set; } = -1;

    public SourceMapSource? Source { get; set; }
}

/// <summary>types.ts: SynthesizedComment (extends CommentRange; `pos` and `end` are -1).</summary>
public sealed class SynthesizedComment : TextRange
{
    public int Pos { get; set; } = -1;

    public int End { get; set; } = -1;

    public bool? HasTrailingNewLine { get; set; }

    public SyntaxKind Kind { get; set; }

    public string Text { get; set; } = null!;

    public bool? HasLeadingNewline { get; set; }
}

/// <summary>types.ts: EmitHelper (ScopedEmitHelper | UnscopedEmitHelper); `text` is a string or a function yielding one.</summary>
public sealed class EmitHelper
{
    public string Name { get; set; } = null!;

    public bool Scoped { get; set; }

    public object Text { get; set; } = null!;

    public int? Priority { get; set; }

    public IReadOnlyList<EmitHelper>? Dependencies { get; set; }
}

/// <summary>types.ts: SnippetElement (TabStop | Placeholder).</summary>
public sealed class SnippetElement
{
    public SnippetKind Kind { get; set; }

    public int Order { get; set; }
}

/// <summary>types.ts: AutoGenerateInfo.</summary>
public sealed class AutoGenerateInfo
{
    public GeneratedIdentifierFlags Flags { get; set; }

    /// <summary>Ensures unique generated identifiers get unique names, but clones get the same name.</summary>
    public int Id { get; set; }

    /// <summary>`string | GeneratedNamePart | undefined`.</summary>
    public object? Prefix { get; set; }

    public string? Suffix { get; set; }
}

/// <summary>
/// types.ts: interface EmitNode (the object a node's `emitNode` field holds; the field is `object?` in the generated Node).
/// NOTE: Any new properties should be accounted for in `mergeEmitNode` (NodeFactory.MergeEmitNode).
/// </summary>
internal sealed class EmitNodeData
{
    /// <summary>Flags that customize emit.</summary>
    public EmitFlags Flags;

    /// <summary>Internal flags that customize emit.</summary>
    public InternalEmitFlags InternalFlags;

    /// <summary>Tracks Parse-tree nodes with EmitNodes for eventual cleanup.</summary>
    public List<Node>? AnnotatedNodes;

    /// <summary>Synthesized leading comments.</summary>
    public List<SynthesizedComment>? LeadingComments;

    /// <summary>Synthesized trailing comments.</summary>
    public List<SynthesizedComment>? TrailingComments;

    /// <summary>The text range to use when emitting leading or trailing comments.</summary>
    public TextRange? CommentRange;

    /// <summary>The text range to use when emitting leading or trailing source mappings.</summary>
    public SourceMapRange? SourceMapRange;

    /// <summary>
    /// The text range to use when emitting source mappings for tokens (a sparse array indexed by SyntaxKind in
    /// TypeScript: a map from the token's kind here).
    /// </summary>
    public Dictionary<int, SourceMapRange?>? TokenSourceMapRanges;

    /// <summary>The constant value of an expression (`string | number`).</summary>
    public object? ConstantValue;

    /// <summary>The local name for an imported helpers module.</summary>
    public Identifier? ExternalHelpersModuleName;

    public bool? ExternalHelpers;

    /// <summary>Emit helpers for the node.</summary>
    public List<EmitHelper>? Helpers;

    /// <summary>If the node should begin on a new line.</summary>
    public bool? StartsOnNewLine;

    /// <summary>Snippet element of the node.</summary>
    public SnippetElement? SnippetElement;

    /// <summary>VariableDeclaration type.</summary>
    public Node? TypeNode;

    /// <summary>Identifier that points to a captured static `this` for a class which may be updated after decorators are applied.</summary>
    public Identifier? ClassThis;

    /// <summary>Expression used as the assigned name of a class or function.</summary>
    public Node? AssignedName;

    /// <summary>
    /// Only defined on synthesized identifiers. Though not syntactically valid, used in emitting diagnostics,
    /// quickinfo, and signature help.
    /// </summary>
    public NodeArray? IdentifierTypeArguments;

    /// <summary>Used for auto-generated identifiers and private identifiers.</summary>
    public AutoGenerateInfo? AutoGenerate;

    /// <summary>Reference to the generated import specifier this identifier refers to.</summary>
    public Node? GeneratedImportReference;
}

/// <summary>factory/emitNode.ts.</summary>
internal static class EmitNode
{
    /// <summary>
    /// Associates a node with the current transformation, initializing
    /// various transient transformation properties.
    /// </summary>
    internal static EmitNodeData GetOrCreateEmitNode(Node node)
    {
        if (node.EmitNode == null)
        {
            if (UtilitiesPublic.IsParseTreeNode(node))
            {
                // To avoid holding onto transformation artifacts, we keep track of any
                // parse tree node we are annotating. This allows us to clean them up after
                // all transformations have completed.
                if (node.Kind == SyntaxKind.SourceFile)
                {
                    var sourceFileEmitNode = new EmitNodeData { AnnotatedNodes = [node] };
                    node.EmitNode = sourceFileEmitNode;
                    return sourceFileEmitNode;
                }

                var sourceFile = Utilities.GetSourceFileOfNode(UtilitiesPublic.GetParseTreeNode(Utilities.GetSourceFileOfNode(node)));
                if (sourceFile == null)
                {
                    Debug.Fail("Could not determine parsed source file.");
                }
                GetOrCreateEmitNode(sourceFile).AnnotatedNodes!.Add(node);
            }

            node.EmitNode = new EmitNodeData();
        }
        else
        {
            Debug.Assert((((EmitNodeData)node.EmitNode).InternalFlags & InternalEmitFlags.Immutable) == 0, "Invalid attempt to mutate an immutable node.");
        }
        return (EmitNodeData)node.EmitNode;
    }

    /// <summary>Sets flags that control emit behavior of a node.</summary>
    internal static T SetEmitFlags<T>(T node, EmitFlags emitFlags)
        where T : Node
    {
        GetOrCreateEmitNode(node).Flags = emitFlags;
        return node;
    }

    /// <summary>Gets a custom text range to use when emitting source maps (a SourceMapRange, or the node itself).</summary>
    internal static TextRange GetSourceMapRange(Node node)
    {
        var range = ((EmitNodeData?)node.EmitNode)?.SourceMapRange;
        if (range != null)
        {
            return range;
        }
        return node;
    }

    /// <summary>Gets whether the node starts on a new line.</summary>
    internal static bool? GetStartsOnNewLine(Node node)
    {
        return ((EmitNodeData?)node.EmitNode)?.StartsOnNewLine;
    }

    /// <summary>Sets whether the node starts on a new line.</summary>
    internal static T SetStartsOnNewLine<T>(T node, bool newLine)
        where T : Node
    {
        GetOrCreateEmitNode(node).StartsOnNewLine = newLine;
        return node;
    }

    /// <summary>Gets a custom text range to use when emitting comments.</summary>
    internal static TextRange GetCommentRange(Node node)
    {
        var range = ((EmitNodeData?)node.EmitNode)?.CommentRange;
        if (range != null)
        {
            return range;
        }
        return node;
    }

    internal static List<SynthesizedComment>? GetSyntheticLeadingComments(Node node)
    {
        return ((EmitNodeData?)node.EmitNode)?.LeadingComments;
    }

    internal static List<SynthesizedComment>? GetSyntheticTrailingComments(Node node)
    {
        return ((EmitNodeData?)node.EmitNode)?.TrailingComments;
    }
}
