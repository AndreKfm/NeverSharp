// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (createTypeChecker: the checker's storage and its public surface)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go (Checker struct, NewChecker)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#. Module C0: the skeleton: the per-checker storage
// (docs/phase3-checker-layout.md), the binding of the program's files and the empty surface the program and the checker
// dump call; modules C1-C9 port the checker into it.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// checker.ts: the TypeChecker of one program (createTypeChecker(host)); typescript-go: checker.Checker. All the
/// checker's data (types, signatures, transient symbols, symbol and node links, diagnostics) lives in this instance;
/// the program's binder output is read, never written (library files are shared between checkers on other threads).
/// One thread uses a checker at a time.
/// </summary>
internal sealed class Checker
{
    private readonly Program program;
    private readonly CompilerOptions compilerOptions;
    private readonly Dictionary<SourceFile, int> fileIndex = new(ReferenceEqualityComparer.Instance);
    private readonly List<ProgramDiagnostic> diagnostics = [];
    private readonly List<ProgramDiagnostic> suggestionDiagnostics = [];
    private readonly List<ProgramDiagnostic> globalDiagnostics = [];

    internal Checker(Program program)
    {
        ArgumentNullException.ThrowIfNull(program);
        this.program = program;
        compilerOptions = program.Options;
        var files = program.SourceFiles;
        for (var i = 0; i < files.Count; i++)
        {
            fileIndex[files[i]] = i + 1;
        }
        Types = new TypeStore();
        Signatures = new SignatureStore();
        TransientSymbols = new TransientSymbolStore();
        ValueSymbolLinks = new SymbolLinkStore<ValueSymbolLinks>(files.Count);
        AliasSymbolLinks = new SymbolLinkStore<AliasSymbolLinks>(files.Count);
        DeclaredTypeLinks = new SymbolLinkStore<DeclaredTypeLinks>(files.Count);
        NodeLinks = new NodeLinkStore<NodeLinks>(files.Count);
        SymbolNodeLinks = new NodeLinkStore<SymbolNodeLinks>(files.Count);
        TypeNodeLinks = new NodeLinkStore<TypeNodeLinks>(files.Count);
        SignatureLinks = new NodeLinkStore<SignatureLinks>(files.Count);
        InitializeTypeChecker();
    }

    internal Program Program => program;

    internal CompilerOptions CompilerOptions => compilerOptions;

    internal TypeStore Types { get; }

    internal SignatureStore Signatures { get; }

    internal TransientSymbolStore TransientSymbols { get; }

    internal SymbolLinkStore<ValueSymbolLinks> ValueSymbolLinks { get; }

    internal SymbolLinkStore<AliasSymbolLinks> AliasSymbolLinks { get; }

    internal SymbolLinkStore<DeclaredTypeLinks> DeclaredTypeLinks { get; }

    internal NodeLinkStore<NodeLinks> NodeLinks { get; }

    internal NodeLinkStore<SymbolNodeLinks> SymbolNodeLinks { get; }

    internal NodeLinkStore<TypeNodeLinks> TypeNodeLinks { get; }

    internal NodeLinkStore<SignatureLinks> SignatureLinks { get; }

    /// <summary>The program's source file of a <see cref="NodeRef"/> or <see cref="SymbolRef"/> file number (1-based).</summary>
    internal SourceFile FileAt(int file) => program.SourceFiles[file - 1];

    /// <summary>The file number of a program source file (1-based; 0 if the file is not in the program).</summary>
    internal int FileNumber(SourceFile file) => fileIndex.TryGetValue(file, out var i) ? i : 0;

    /// <summary>A node of <paramref name="file"/> as a program-wide reference.</summary>
    internal NodeRef Ref(SourceFile file, Node node) => new(FileNumber(file), node);

    /// <summary>
    /// checker.ts initializeTypeChecker. C0: binds every file that is not bound yet (library files come bound from the
    /// shared <see cref="DefaultLibrary"/>). C1: merging of the files' globals, the global types and symbols.
    /// </summary>
    private void InitializeTypeChecker()
    {
        foreach (var file in program.SourceFiles)
        {
            Binder.BindSourceFile(file, compilerOptions);
        }
    }

    /// <summary>checker.ts getDiagnostics(sourceFile): the checker's diagnostics of the file (C0: none yet).</summary>
    internal List<ProgramDiagnostic> GetDiagnostics(SourceFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return diagnostics.FindAll(d => ReferenceEquals(d.SourceFile, file));
    }

    /// <summary>checker.ts getSuggestionDiagnostics(file) (C0: none yet).</summary>
    internal List<ProgramDiagnostic> GetSuggestionDiagnostics(SourceFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return suggestionDiagnostics.FindAll(d => ReferenceEquals(d.SourceFile, file));
    }

    /// <summary>checker.ts getGlobalDiagnostics() (C0: none yet).</summary>
    internal List<ProgramDiagnostic> GetGlobalDiagnostics() => [.. globalDiagnostics];

    /// <summary>checker.ts getTypeAtLocation(node) (C0: undefined, the type walker writes nothing for it).</summary>
    internal Type GetTypeAtLocation(NodeRef node)
    {
        CheckNode(node);
        return Type.None;
    }

    /// <summary>checker.ts getSymbolAtLocation(node) (C0: undefined).</summary>
    internal SymbolRef GetSymbolAtLocation(NodeRef node)
    {
        CheckNode(node);
        return SymbolRef.None;
    }

    private void CheckNode(NodeRef node)
    {
        if (node.File < 1 || node.File > program.SourceFiles.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(node), "not a node of this checker's program");
        }
    }

    /// <summary>types.go Type.Flags.</summary>
    internal TypeFlags GetTypeFlags(Type type) => Types.Flags(type);

    /// <summary>The intrinsic name of an intrinsic type (types.ts IntrinsicType.intrinsicName).</summary>
    internal string IntrinsicName(Type type) => Types.IntrinsicName(type);

    /// <summary>
    /// typeToTypeNode(type, enclosingDeclaration, flags) printed by typeWriter.ts' underlining printer: the type text and
    /// its underline (^ under fully synthesized nodes). Module C2 (node builder and printer).
    /// </summary>
    internal (string Text, string Underline) TypeToBaselineText(Type type, NodeRef enclosingDeclaration, bool inTypeAlias) =>
        throw new NotSupportedException("typeToTypeNode and the printer come with module C2");

    /// <summary>checker.ts symbolToString(symbol, enclosingDeclaration). Module C2.</summary>
    internal string SymbolToString(SymbolRef symbol, NodeRef enclosingDeclaration) =>
        throw new NotSupportedException("symbolToString comes with module C2");

    /// <summary>The declarations of a symbol (types.ts Symbol.declarations; null when undefined).</summary>
    internal List<NodeRef>? Declarations(SymbolRef symbol)
    {
        if (symbol.IsTransient)
        {
            return TransientSymbols.Record(symbol).Declarations;
        }
        var file = FileAt(symbol.File);
        var binding = file.Binding!;
        var chain = binding.Declarations(symbol.BinderSymbol);
        List<NodeRef>? result = null;
        foreach (var node in chain)
        {
            (result ??= []).Add(new NodeRef(symbol.File, node));
        }
        return result;
    }
}
