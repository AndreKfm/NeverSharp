// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>nodeFactory.ts: top-level nodes (source files).</summary>
internal sealed partial class NodeFactory
{
    //
    // Top-level nodes
    //

    // @api
    internal SourceFile CreateSourceFile(
        IReadOnlyList<Node> statements,
        EndOfFileToken endOfFileToken,
        NodeFlags flags)
    {
        var node = (SourceFile)baseFactory.CreateBaseSourceFileNode(SyntaxKind.SourceFile);
        node.Statements = CreateNodeArray(statements);
        node.EndOfFileToken = endOfFileToken;
        node.Flags |= flags;
        node.Text = "";
        node.FileName = "";
        node.Path = "";
        node.ResolvedPath = "";
        node.OriginalFileName = "";
        node.LanguageVersion = ScriptTarget.LatestStandard;
        node.LanguageVariant = 0;
        node.ScriptKind = 0;
        node.IsDeclarationFile = false;
        node.HasNoDefaultLib = false;
        node.TransformFlags |= PropagateChildrenFlags(node.Statements) |
            PropagateChildFlags(node.EndOfFileToken);
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.EndFlowNode = null;
        node.NodeCount = 0;
        node.IdentifierCount = 0;
        node.SymbolCount = 0;
        node.ParseDiagnostics = null!;
        node.BindDiagnostics = null!;
        node.BindSuggestionDiagnostics = null;
        node.LineMap = null!;
        node.ExternalModuleIndicator = null;
        node.SetExternalModuleIndicator = null;
        node.Pragmas = null!;
        node.CheckJsDirective = null;
        node.ReferencedFiles = null!;
        node.TypeReferenceDirectives = null!;
        node.LibReferenceDirectives = null!;
        node.AmdDependencies = null!;
        node.CommentDirectives = null;
        node.Identifiers = null!;
        node.PackageJsonLocations = null;
        node.PackageJsonScope = null;
        node.Imports = null!;
        node.ModuleAugmentations = null!;
        node.AmbientModuleNames = null!;
        node.ClassifiableNames = null;
        // TypeScript: `node.impliedNodeFormat = undefined` (the generated field is a ModuleKind; undefined is its default).
        node.ImpliedNodeFormat = default;
        return node;
    }

    /// <summary>
    /// TypeScript creates the redirect with `Object.create(redirectInfo.redirectTarget)` (a prototype chain) and
    /// accessors for `id` and `symbol`; C# objects have no prototypes. Redirects come from program construction,
    /// not from the parser, which never sets `redirectInfo`.
    /// </summary>
    private static SourceFile CreateRedirectedSourceFile(object redirectInfo)
    {
        _ = redirectInfo;
        Debug.Fail("createRedirectedSourceFile (prototype-based redirect source files) is not supported");
        return null!;
    }

    private static SourceFile CloneRedirectedSourceFile(SourceFile source)
    {
        var node = CreateRedirectedSourceFile(source.RedirectInfo!);
        node.Flags |= source.Flags & ~NodeFlags.Synthesized;
        node.FileName = source.FileName;
        node.Path = source.Path;
        node.ResolvedPath = source.ResolvedPath;
        node.OriginalFileName = source.OriginalFileName;
        node.PackageJsonLocations = source.PackageJsonLocations;
        node.PackageJsonScope = source.PackageJsonScope;
        node.EmitNode = null;
        return node;
    }

    /// <summary>
    /// TypeScript copies every own property of `source` that the new node does not have yet (`for (const p in source)`),
    /// i.e. everything but the properties the node constructor sets (pos, end, kind, id, flags, modifierFlagsCache,
    /// transformFlags, parent, original, emitNode). Here those are the fields of the SourceFile class (the base
    /// Node fields are the constructor's); a field that is not an own property in TypeScript holds its default
    /// value on both objects, so copying it changes nothing.
    /// </summary>
    private SourceFile CloneSourceFileWorker(SourceFile source)
    {
        // TODO: This mechanism for cloning results in megamorphic property reads and writes. In future perf-related
        //       work, we should consider switching explicit property assignments instead of using `for..in`.
        var node = (SourceFile)baseFactory.CreateBaseSourceFileNode(SyntaxKind.SourceFile);
        node.Flags |= source.Flags & ~NodeFlags.Synthesized;

        // Fields declared by the generated class (types.ts SourceFile).
        node.AmbientModuleNames = source.AmbientModuleNames;
        node.ClassifiableNames = source.ClassifiableNames;
        node.EndFlowNode = source.EndFlowNode;
        node.EndOfFileToken = source.EndOfFileToken;
        node.ExternalModuleIndicator = source.ExternalModuleIndicator;
        node.FileName = source.FileName;
        node.HasNoDefaultLib = source.HasNoDefaultLib;
        node.IdentifierCount = source.IdentifierCount;
        node.Identifiers = source.Identifiers;
        node.ImpliedNodeFormat = source.ImpliedNodeFormat;
        node.Imports = source.Imports;
        node.IsDeclarationFile = source.IsDeclarationFile;
        node.JsDocParsingMode = source.JsDocParsingMode;
        node.LanguageVariant = source.LanguageVariant;
        node.LanguageVersion = source.LanguageVersion;
        node.LineMap = source.LineMap;
        node.LocalSymbol = source.LocalSymbol;
        node.Locals = source.Locals;
        node.ModuleAugmentations = source.ModuleAugmentations;
        node.ModuleName = source.ModuleName;
        node.NextContainer = source.NextContainer;
        node.NodeCount = source.NodeCount;
        node.OriginalFileName = source.OriginalFileName;
        node.PackageJsonLocations = source.PackageJsonLocations;
        node.PackageJsonScope = source.PackageJsonScope;
        node.Path = source.Path;
        node.RedirectInfo = source.RedirectInfo;
        node.ResolvedPath = source.ResolvedPath;
        node.ScriptKind = source.ScriptKind;
        node.SetExternalModuleIndicator = source.SetExternalModuleIndicator;
        node.Statements = source.Statements;
        node.Symbol = source.Symbol;
        node.SymbolCount = source.SymbolCount;
        node.Text = source.Text;

        // Fields declared by the port (Parser/Types.cs, Parser/Parser.Pragmas.cs).
        node.AmdDependencies = source.AmdDependencies;
        node.ReferencedFiles = source.ReferencedFiles;
        node.TypeReferenceDirectives = source.TypeReferenceDirectives;
        node.LibReferenceDirectives = source.LibReferenceDirectives;
        node.ParseDiagnostics = source.ParseDiagnostics;
        node.BindDiagnostics = source.BindDiagnostics;
        node.BindSuggestionDiagnostics = source.BindSuggestionDiagnostics;
        node.JsDocDiagnostics = source.JsDocDiagnostics;
        node.CommentDirectives = source.CommentDirectives;
        node.CheckJsDirective = source.CheckJsDirective;
        node.Pragmas = source.Pragmas;

        // `if (p === "emitNode") { node.emitNode = undefined; continue; }` (emitNode is a constructor property, so
        // this branch is never taken.)
        return node;
    }

    private SourceFile CloneSourceFile(SourceFile source)
    {
        var node = source.RedirectInfo != null ? CloneRedirectedSourceFile(source) : CloneSourceFileWorker(source);
        SetOriginal(node, source);
        return node;
    }

    private SourceFile CloneSourceFileWithChanges(
        SourceFile source,
        IReadOnlyList<Node> statements,
        bool isDeclarationFile,
        IReadOnlyList<FileReference> referencedFiles,
        IReadOnlyList<FileReference> typeReferences,
        bool hasNoDefaultLib,
        IReadOnlyList<FileReference> libReferences)
    {
        var node = CloneSourceFile(source);
        node.Statements = CreateNodeArray(statements);
        node.IsDeclarationFile = isDeclarationFile;
        node.ReferencedFiles = referencedFiles;
        node.TypeReferenceDirectives = typeReferences;
        node.HasNoDefaultLib = hasNoDefaultLib;
        node.LibReferenceDirectives = libReferences;
        node.TransformFlags = PropagateChildrenFlags(node.Statements) |
            PropagateChildFlags(node.EndOfFileToken);
        return node;
    }

    // @api
    internal SourceFile UpdateSourceFile(
        SourceFile node,
        IReadOnlyList<Node> statements,
        bool? isDeclarationFile = null,
        IReadOnlyList<FileReference>? referencedFiles = null,
        IReadOnlyList<FileReference>? typeReferenceDirectives = null,
        bool hasNoDefaultLib = false,
        IReadOnlyList<FileReference>? libReferenceDirectives = null)
    {
        // Default parameter values: `isDeclarationFile = node.isDeclarationFile`, ... (`_hasNoDefaultLib` is unused).
        _ = hasNoDefaultLib;
        var isDeclarationFileValue = isDeclarationFile ?? node.IsDeclarationFile;
        referencedFiles ??= node.ReferencedFiles;
        typeReferenceDirectives ??= node.TypeReferenceDirectives;
        libReferenceDirectives ??= node.LibReferenceDirectives;
        return node.Statements != statements
                || node.IsDeclarationFile != isDeclarationFileValue
                || node.ReferencedFiles != referencedFiles
                || node.TypeReferenceDirectives != typeReferenceDirectives
                || node.LibReferenceDirectives != libReferenceDirectives
            ? Update(CloneSourceFileWithChanges(node, statements, isDeclarationFileValue, referencedFiles, typeReferenceDirectives, /*hasNoDefaultLib*/ false, libReferenceDirectives), node)
            : node;
    }
}
