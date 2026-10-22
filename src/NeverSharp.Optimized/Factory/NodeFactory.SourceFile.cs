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
    internal Node CreateSourceFile(
        NodeList statements,
        Node endOfFileToken,
        NodeFlags flags)
    {
        var node = baseFactory.CreateBaseSourceFileNode(SyntaxKind.SourceFile);
        store.SetStatements(node, CreateNodeArray(store, statements));
        store.SetEndOfFileToken(node, endOfFileToken);
        store.SetFlags(node, store.Flags(node) | flags);
        store.SetSourceFileText(node, "");
        store.SetFileName(node, "");
        store.SetPath(node, "");
        store.SetResolvedPath(node, "");
        store.SetOriginalFileName(node, "");
        store.SetLanguageVersion(node, ScriptTarget.LatestStandard);
        store.SetLanguageVariant(node, 0);
        store.SetScriptKind(node, 0);
        store.SetIsDeclarationFile(node, false);
        store.SetHasNoDefaultLib(node, false);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildrenFlags(store.Statements(node)) |
            PropagateChildFlags(store, store.EndOfFileToken(node))));
        store.SetNextContainer(node, default); // initialized by binder (LocalsContainer)
        store.SetNodeCount(node, 0);
        store.SetIdentifierCount(node, 0);
        store.SetSymbolCount(node, 0);
        store.SetParseDiagnostics(node, null!);
        store.SetBindDiagnostics(node, null!);
        store.SetBindSuggestionDiagnostics(node, null);
        store.SetLineMap(node, null!);
        store.SetExternalModuleIndicator(node, null);
        store.SetSetExternalModuleIndicator(node, null);
        store.SetPragmas(node, null!);
        store.SetCheckJsDirective(node, null);
        store.SetReferencedFiles(node, null!);
        store.SetTypeReferenceDirectives(node, null!);
        store.SetLibReferenceDirectives(node, null!);
        store.SetAmdDependencies(node, null!);
        store.SetCommentDirectives(node, null);
        store.SetIdentifiers(node, null!);
        store.SetPackageJsonLocations(node, null);
        store.SetImports(node, null!);
        store.SetModuleAugmentations(node, null!);
        store.SetAmbientModuleNames(node, null!);
        // TypeScript: `node.impliedNodeFormat = undefined` (the generated field is a ModuleKind; undefined is its default).
        store.SetImpliedNodeFormat(node, default);
        return node;
    }

    // createRedirectedSourceFile / cloneRedirectedSourceFile: TypeScript clones a redirect when `source.redirectInfo`
    // is set. Redirects come from program construction, not from the parser, which never sets `redirectInfo`; Track O's
    // node store keeps no binder data such as redirectInfo (step O6a), so a source file is never a redirect here.

    /// <summary>
    /// TypeScript copies every own property of `source` that the new node does not have yet (`for (const p in source)`),
    /// i.e. everything but the properties the node constructor sets (pos, end, kind, id, flags, modifierFlagsCache,
    /// transformFlags, parent, original, emitNode). Here those are the fields of the SourceFile class (the base
    /// Node fields are the constructor's); a field that is not an own property in TypeScript holds its default
    /// value on both objects, so copying it changes nothing.
    /// </summary>
    private Node CloneSourceFileWorker(Node source)
    {
        // TODO: This mechanism for cloning results in megamorphic property reads and writes. In future perf-related
        //       work, we should consider switching explicit property assignments instead of using `for..in`.
        var node = baseFactory.CreateBaseSourceFileNode(SyntaxKind.SourceFile);
        store.SetFlags(node, store.Flags(node) | (store.Flags(source) & ~NodeFlags.Synthesized));

        // Fields declared by the generated class (types.ts SourceFile).
        store.SetAmbientModuleNames(node, store.AmbientModuleNames(source));
        store.SetEndOfFileToken(node, store.EndOfFileToken(source));
        store.SetExternalModuleIndicator(node, store.ExternalModuleIndicator(source));
        store.SetFileName(node, store.FileName(source));
        store.SetHasNoDefaultLib(node, store.HasNoDefaultLib(source));
        store.SetIdentifierCount(node, store.IdentifierCount(source));
        store.SetIdentifiers(node, store.Identifiers(source));
        store.SetImpliedNodeFormat(node, store.ImpliedNodeFormat(source));
        store.SetImports(node, store.Imports(source));
        store.SetIsDeclarationFile(node, store.IsDeclarationFile(source));
        store.SetJsDocParsingMode(node, store.JsDocParsingMode(source));
        store.SetLanguageVariant(node, store.LanguageVariant(source));
        store.SetLanguageVersion(node, store.LanguageVersion(source));
        store.SetLineMap(node, store.LineMap(source));
        store.SetModuleAugmentations(node, store.ModuleAugmentations(source));
        store.SetModuleName(node, store.ModuleName(source));
        store.SetNextContainer(node, store.NextContainer(source));
        store.SetNodeCount(node, store.NodeCount(source));
        store.SetOriginalFileName(node, store.OriginalFileName(source));
        store.SetPackageJsonLocations(node, store.PackageJsonLocations(source));
        store.SetPath(node, store.Path(source));
        store.SetResolvedPath(node, store.ResolvedPath(source));
        store.SetScriptKind(node, store.ScriptKind(source));
        store.SetSetExternalModuleIndicator(node, store.SetExternalModuleIndicator(source));
        store.SetStatements(node, store.Statements(source));
        store.SetSymbolCount(node, store.SymbolCount(source));
        store.SetSourceFileText(node, store.SourceFileText(source));

        // Fields declared by the port (Parser/Types.cs, Parser/Parser.Pragmas.cs).
        store.SetAmdDependencies(node, store.AmdDependencies(source));
        store.SetReferencedFiles(node, store.ReferencedFiles(source));
        store.SetTypeReferenceDirectives(node, store.TypeReferenceDirectives(source));
        store.SetLibReferenceDirectives(node, store.LibReferenceDirectives(source));
        store.SetParseDiagnostics(node, store.ParseDiagnostics(source));
        store.SetBindDiagnostics(node, store.BindDiagnostics(source));
        store.SetBindSuggestionDiagnostics(node, store.BindSuggestionDiagnostics(source));
        store.SetJsDocDiagnostics(node, store.JsDocDiagnostics(source));
        store.SetCommentDirectives(node, store.CommentDirectives(source));
        store.SetCheckJsDirective(node, store.CheckJsDirective(source));
        store.SetPragmas(node, store.Pragmas(source));

        // `if (p === "emitNode") { node.emitNode = undefined; continue; }` (emitNode is a constructor property, so
        // this branch is never taken.)
        return node;
    }

    private Node CloneSourceFile(Node source)
    {
        var node = CloneSourceFileWorker(source);
        SetOriginal(node, source);
        return node;
    }

    private Node CloneSourceFileWithChanges(
        Node source,
        NodeList statements,
        bool isDeclarationFile,
        IReadOnlyList<FileReference> referencedFiles,
        IReadOnlyList<FileReference> typeReferences,
        bool hasNoDefaultLib,
        IReadOnlyList<FileReference> libReferences)
    {
        var node = CloneSourceFile(source);
        store.SetStatements(node, CreateNodeArray(store, statements));
        store.SetIsDeclarationFile(node, isDeclarationFile);
        store.SetReferencedFiles(node, referencedFiles);
        store.SetTypeReferenceDirectives(node, typeReferences);
        store.SetHasNoDefaultLib(node, hasNoDefaultLib);
        store.SetLibReferenceDirectives(node, libReferences);
        store.SetTransformFlags(node, PropagateChildrenFlags(store.Statements(node)) |
            PropagateChildFlags(store, store.EndOfFileToken(node)));
        return node;
    }

    // @api
    internal Node UpdateSourceFile(
        Node node,
        NodeList statements,
        bool? isDeclarationFile = null,
        IReadOnlyList<FileReference>? referencedFiles = null,
        IReadOnlyList<FileReference>? typeReferenceDirectives = null,
        bool hasNoDefaultLib = false,
        IReadOnlyList<FileReference>? libReferenceDirectives = null)
    {
        // Default parameter values: `isDeclarationFile = node.isDeclarationFile`, ... (`_hasNoDefaultLib` is unused).
        _ = hasNoDefaultLib;
        var isDeclarationFileValue = isDeclarationFile ?? store.IsDeclarationFile(node);
        referencedFiles ??= store.ReferencedFiles(node);
        typeReferenceDirectives ??= store.TypeReferenceDirectives(node);
        libReferenceDirectives ??= store.LibReferenceDirectives(node);
        return store.Statements(node) != statements
                || store.IsDeclarationFile(node) != isDeclarationFileValue
                || store.ReferencedFiles(node) != referencedFiles
                || store.TypeReferenceDirectives(node) != typeReferenceDirectives
                || store.LibReferenceDirectives(node) != libReferenceDirectives
            ? Update(CloneSourceFileWithChanges(node, statements, isDeclarationFileValue, referencedFiles, typeReferenceDirectives, /*hasNoDefaultLib*/ false, libReferenceDirectives), node)
            : node;
    }
}
