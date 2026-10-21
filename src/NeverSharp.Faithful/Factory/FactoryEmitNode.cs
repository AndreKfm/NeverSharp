// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Ported from src/compiler/factory/emitNode.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/printer/emitcontext.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// types.ts: EmitNode. Stored in the generated `Node.EmitNode` field (typed `object?`). Members whose TypeScript types
/// belong to the emitter (SynthesizedComment, EmitHelper, SnippetElement, AutoGenerateInfo) are typed `object`.
/// </summary>
internal sealed class EmitNode
{
    public EmitFlags Flags;                        // Flags that customize emit
    public InternalEmitFlags InternalFlags;        // Internal flags that customize emit
    public List<Node>? AnnotatedNodes;             // Tracks Parse-tree nodes with EmitNodes for eventual cleanup.
    public List<object>? LeadingComments;          // Synthesized leading comments (TypeScript: SynthesizedComment[])
    public List<object>? TrailingComments;         // Synthesized trailing comments (TypeScript: SynthesizedComment[])
    public TextRange? CommentRange;                // The text range to use when emitting leading or trailing comments
    public TextRange? SourceMapRange;              // The text range to use when emitting leading or trailing source mappings

    // The text range to use when emitting source mappings for tokens (TypeScript: a sparse array indexed by SyntaxKind).
    public Dictionary<int, TextRange?>? TokenSourceMapRanges;
    public object? ConstantValue;                  // The constant value of an expression (TypeScript: string | number)
    public Identifier? ExternalHelpersModuleName;  // The local name for an imported helpers module
    public bool? ExternalHelpers;
    public List<object>? Helpers;                  // Emit helpers for the node (TypeScript: EmitHelper[])
    public bool? StartsOnNewLine;                  // If the node should begin on a new line
    public object? SnippetElement;                 // Snippet element of the node (TypeScript: SnippetElement)
    public Node? TypeNode;                         // VariableDeclaration type
    public Identifier? ClassThis;                  // Identifier that points to a captured static `this` for a class which may be updated after decorators are applied
    public Node? AssignedName;                     // Expression used as the assigned name of a class or function

    // Only defined on synthesized identifiers. Though not syntactically valid, used in emitting diagnostics, quickinfo, and signature help.
    public NodeArray? IdentifierTypeArguments;
    public object? AutoGenerate;                   // Used for auto-generated identifiers and private identifiers (TypeScript: AutoGenerateInfo).
    public ImportSpecifier? GeneratedImportReference; // Reference to the generated import specifier this identifier refers to
}

/// <summary>factory/emitNode.ts.</summary>
internal static partial class FactoryEmitNode
{
    /// <summary>
    /// Associates a node with the current transformation, initializing
    /// various transient transformation properties.
    /// </summary>
    internal static EmitNode GetOrCreateEmitNode(Node node)
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
                    var sourceFileEmitNode = new EmitNode { AnnotatedNodes = [node] };
                    node.EmitNode = sourceFileEmitNode;
                    return sourceFileEmitNode;
                }

                // (TypeScript selects the `getSourceFileOfNode(node: Node): SourceFile` overload for the inner call.)
                var sourceFile = Utilities.GetSourceFileOfNode(UtilitiesPublic.GetParseTreeNode(Utilities.GetSourceFileOfNode(node)!));
                if (sourceFile == null)
                {
                    Debug.Fail("Could not determine parsed source file.");
                }
                GetOrCreateEmitNode(sourceFile).AnnotatedNodes!.Add(node);
            }

            node.EmitNode = new EmitNode();
        }
        else
        {
            Debug.Assert((((EmitNode)node.EmitNode).InternalFlags & InternalEmitFlags.Immutable) == 0, "Invalid attempt to mutate an immutable node.");
        }
        return (EmitNode)node.EmitNode;
    }

    /// <summary>Sets flags that control emit behavior of a node.</summary>
    internal static T SetEmitFlags<T>(T node, EmitFlags emitFlags)
        where T : Node
    {
        GetOrCreateEmitNode(node).Flags = emitFlags;
        return node;
    }

    /// <summary>Gets a custom text range to use when emitting source maps.</summary>
    internal static TextRange GetSourceMapRange(Node node)
    {
        return ((EmitNode?)node.EmitNode)?.SourceMapRange ?? node;
    }

    /// <summary>Gets a custom text range to use when emitting comments.</summary>
    internal static bool? GetStartsOnNewLine(Node node)
    {
        return ((EmitNode?)node.EmitNode)?.StartsOnNewLine;
    }

    /// <summary>Sets a custom text range to use when emitting comments.</summary>
    internal static T SetStartsOnNewLine<T>(T node, bool newLine)
        where T : Node
    {
        GetOrCreateEmitNode(node).StartsOnNewLine = newLine;
        return node;
    }

    /// <summary>Gets a custom text range to use when emitting comments.</summary>
    internal static TextRange GetCommentRange(Node node)
    {
        return ((EmitNode?)node.EmitNode)?.CommentRange ?? node;
    }

    internal static List<object>? GetSyntheticLeadingComments(Node node)
    {
        return ((EmitNode?)node.EmitNode)?.LeadingComments;
    }

    internal static List<object>? GetSyntheticTrailingComments(Node node)
    {
        return ((EmitNode?)node.EmitNode)?.TrailingComments;
    }
}
