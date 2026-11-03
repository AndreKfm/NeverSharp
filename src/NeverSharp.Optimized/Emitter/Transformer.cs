// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/transformer.ts (getTransformers, getScriptTransformers, getDeclarationTransformers,
// transformNodes and its TransformationContext, noEmitSubstitution, noEmitNotification)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/transformers/transformer.go and internal/printer/emitcontext.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D1. Only TypeScript 6.0.3's default options (no custom
// transformers, no bundles): the script transformers of target ES2025 and module ES2022, the declaration transformer.
// The transformers run on the emit copy of the file's store (NodeStore.CreateEmitCopy).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>types.ts TransformerFactory: creates the transformation of one node (a source file) for a context.</summary>
internal delegate Func<Node, Node> TransformerFactory(TransformationContext context);

/// <summary>types.ts EmitTransformers.</summary>
internal sealed class EmitTransformers
{
    internal EmitTransformers(List<TransformerFactory> scriptTransformers, List<TransformerFactory> declarationTransformers)
    {
        ScriptTransformers = scriptTransformers;
        DeclarationTransformers = declarationTransformers;
    }

    internal List<TransformerFactory> ScriptTransformers { get; }

    internal List<TransformerFactory> DeclarationTransformers { get; }
}

/// <summary>transformer.ts: the transformer lists and transformNodes.</summary>
internal static class Transformers
{
    /// <summary>transformer.ts getTransformers(compilerOptions) without custom transformers or emitOnly.</summary>
    internal static EmitTransformers GetTransformers(CompilerOptions compilerOptions)
    {
        return new EmitTransformers(GetScriptTransformers(compilerOptions), GetDeclarationTransformers());
    }

    /// <summary>
    /// transformer.ts getScriptTransformers. Track O (module D1): the TypeScript transformer slot (D2 ports it); the
    /// slots of the ESNext, ES decorators, class fields (D4) and ES module (D3) transformers are in tsc's order and
    /// return their input until those modules land. Options that select other transformers are outside Phase 4's
    /// scope (default options only) and throw.
    /// </summary>
    internal static List<TransformerFactory> GetScriptTransformers(CompilerOptions compilerOptions)
    {
        var languageVersion = compilerOptions.GetEmitScriptTarget();
        var moduleKind = compilerOptions.GetEmitModuleKind();
        var useDefineForClassFields = compilerOptions.GetUseDefineForClassFields();
        var transformers = new List<TransformerFactory>();

        transformers.Add(TypeScriptTransformer.TransformTypeScript);

        if (compilerOptions.ExperimentalDecorators == true)
        {
            throw new NotSupportedException("transformLegacyDecorators: experimentalDecorators is not a Phase 4 option.");
        }

        // utilities.ts getJSXTransformEnabled
        if (compilerOptions.Jsx is JsxEmit.React or JsxEmit.ReactJSX or JsxEmit.ReactJSXDev)
        {
            throw new NotSupportedException("transformJsx: the jsx option is not a Phase 4 option.");
        }

        if (languageVersion < ScriptTarget.ESNext)
        {
            transformers.Add(ESNextTransformer.TransformESNext);
        }

        if (compilerOptions.ExperimentalDecorators != true && (languageVersion < ScriptTarget.ESNext || !useDefineForClassFields))
        {
            transformers.Add(NotYetPorted); // transformESDecorators (module D4)
        }

        transformers.Add(NotYetPorted); // transformClassFields (module D4)

        if (languageVersion < ScriptTarget.ES2021)
        {
            throw new NotSupportedException("Downlevel transformers below ES2021 are not Phase 4 options.");
        }

        // getModuleTransformer(moduleKind): module kinds ES2015..ESNext select transformImpliedNodeFormatDependentModule
        // (module D3). CommonJS, AMD, UMD, System, Node16+ and Preserve as the program's module kind are outside Phase 4's
        // options; their slot returns its input.
        if (moduleKind is not (ModuleKind.ES2015 or ModuleKind.ES2020 or ModuleKind.ES2022 or ModuleKind.ESNext))
        {
            transformers.Add(NotYetPorted);
        }
        else
        {
            transformers.Add(ImpliedNodeFormatDependentModuleTransformer.TransformImpliedNodeFormatDependentModule);
        }

        return transformers;
    }

    /// <summary>transformer.ts getDeclarationTransformers: transformDeclarations (module D5; it returns its input until then).</summary>
    internal static List<TransformerFactory> GetDeclarationTransformers()
    {
        return [NotYetPorted];
    }

    // The slot of a transformer a later module of Phase 4 ports: the identity transformation.
    private static Func<Node, Node> NotYetPorted(TransformationContext context) => static node => node;

    /// <summary>
    /// transformer.ts transformNodes(resolver, host, factory, options, nodes, transformers, allowDtsFiles) for one
    /// source file: runs the transformers in order on the emit copy of the file's store.
    /// </summary>
    internal static TransformationResult TransformNodes(EmitResolver? resolver, EmitHost? host, NodeFactory factory, CompilerOptions options, Node node, List<TransformerFactory> transformers, bool allowDtsFiles)
    {
        var context = new TransformationContext(factory, options, resolver, host);

        // Ensure the parse tree is clean before applying transformations
        FactoryEmitNode.DisposeEmitNodes(factory.store, Utilities.GetSourceFileOfNode(factory.store, UtilitiesPublic.GetParseTreeNode(factory.store, node)));

        // Chain together and initialize each transformer.
        var transformersWithContext = new List<Func<Node, Node>>(transformers.Count);
        foreach (var t in transformers)
        {
            transformersWithContext.Add(t(context));
        }

        // prevent modification of transformation hooks.
        context.State = TransformationState.Initialized;

        // Transform each node.
        Node transformed;
        if (allowDtsFiles || !(factory.store.Kind(node) == SyntaxKind.SourceFile && factory.store.IsDeclarationFile(node)))
        {
            transformed = node;
            foreach (var transform in transformersWithContext)
            {
                transformed = transform(transformed);
            }
        }
        else
        {
            transformed = node;
        }

        // prevent modification of the lexical environment.
        context.State = TransformationState.Completed;

        return new TransformationResult(context, node, transformed);
    }
}

/// <summary>transformer.ts TransformationState.</summary>
internal enum TransformationState
{
    Uninitialized,
    Initialized,
    Completed,
    Disposed,
}

/// <summary>types.ts TransformationResult for one node.</summary>
internal sealed class TransformationResult
{
    private readonly TransformationContext context;
    private readonly Node original;

    internal TransformationResult(TransformationContext context, Node original, Node transformed)
    {
        this.context = context;
        this.original = original;
        Transformed = transformed;
    }

    /// <summary>The transformed node.</summary>
    internal Node Transformed { get; }

    /// <summary>The diagnostics the transformers added.</summary>
    internal List<ProgramDiagnostic> Diagnostics => context.Diagnostics;

    /// <summary>Emits a node with possible substitution.</summary>
    internal Node SubstituteNode(EmitHint hint, Node node)
    {
        Debug.Assert(context.State < TransformationState.Disposed, "Cannot substitute a node after the result is disposed.");
        if (node.IsNone || !context.IsSubstitutionEnabled(node))
        {
            return node;
        }
        var substitute = context.OnSubstituteNode(hint, node);
        return substitute.IsNone ? node : substitute;
    }

    /// <summary>Emits a node with possible emit notification.</summary>
    internal void EmitNodeWithNotification(EmitHint hint, Node node, Action<EmitHint, Node> emitCallback)
    {
        Debug.Assert(context.State < TransformationState.Disposed, "Cannot invoke TransformationResult callbacks after the result is disposed.");
        if (!node.IsNone)
        {
            // TODO: Remove check and unconditionally use onEmitNode when API is breakingly changed
            if (context.IsEmitNotificationEnabled(node))
            {
                context.OnEmitNode(hint, node, emitCallback);
            }
            else
            {
                emitCallback(hint, node);
            }
        }
    }

    /// <summary>Determines whether emit notification is enabled for the node.</summary>
    internal bool IsEmitNotificationEnabled(Node node) => context.IsEmitNotificationEnabled(node);

    /// <summary>Clean up EmitNode entries on any parse-tree nodes.</summary>
    internal void Dispose()
    {
        if (context.State < TransformationState.Disposed)
        {
            // Clean up emit nodes on parse tree
            var store = context.Store;
            FactoryEmitNode.DisposeEmitNodes(store, Utilities.GetSourceFileOfNode(store, UtilitiesPublic.GetParseTreeNode(store, original)));

            // Release references to external entries for GC purposes.
            context.ReleaseForDispose();

            // Prevent further use of the transformation result.
            context.State = TransformationState.Disposed;
        }
    }
}

/// <summary>types.ts TransformationContext: the closure of transformNodes.</summary>
internal sealed class TransformationContext
{
    [Flags]
    private enum SyntaxKindFeatureFlags : byte
    {
        Substitution = 1 << 0,
        EmitNotifications = 1 << 1,
    }

    private readonly SyntaxKindFeatureFlags[] enabledSyntaxKindFeatures = new SyntaxKindFeatureFlags[(int)SyntaxKind.Count];
    private List<Node>? lexicalEnvironmentVariableDeclarations;
    private List<Node>? lexicalEnvironmentFunctionDeclarations;
    private List<Node>? lexicalEnvironmentStatements;
    private LexicalEnvironmentFlags lexicalEnvironmentFlags = LexicalEnvironmentFlags.None;
    private List<List<Node>?> lexicalEnvironmentVariableDeclarationsStack = [];
    private List<List<Node>?> lexicalEnvironmentFunctionDeclarationsStack = [];
    private List<List<Node>?> lexicalEnvironmentStatementsStack = [];
    private List<LexicalEnvironmentFlags> lexicalEnvironmentFlagsStack = [];
    private int lexicalEnvironmentStackOffset;
    private bool lexicalEnvironmentSuspended;
    private List<List<Node>?> blockScopedVariableDeclarationsStack = [];
    private int blockScopeStackOffset;
    private List<Node>? blockScopedVariableDeclarations;
    private List<EmitHelper>? emitHelpers;
    private Func<EmitHint, Node, Node> onSubstituteNode = NoEmitSubstitution;
    private Action<EmitHint, Node, Action<EmitHint, Node>> onEmitNode = NoEmitNotification;
    private EmitHelperFactory? emitHelperFactory;

    internal TransformationContext(NodeFactory factory, CompilerOptions options, EmitResolver? resolver, EmitHost? host)
    {
        Factory = factory;
        Options = options;
        Resolver = resolver;
        Host = host;
    }

    internal TransformationState State { get; set; } = TransformationState.Uninitialized;

    internal NodeFactory Factory { get; }

    /// <summary>The store the factory writes into (Track O: the emit copy of the file's store).</summary>
    internal NodeStore Store => Factory.store;

    internal CompilerOptions Options { get; }

    internal CompilerOptions GetCompilerOptions() => Options;

    internal EmitResolver? Resolver { get; }

    internal EmitResolver GetEmitResolver() => Resolver!;

    internal EmitHost? Host { get; }

    internal EmitHost GetEmitHost() => Host!;

    internal List<ProgramDiagnostic> Diagnostics { get; } = [];

    internal EmitHelperFactory GetEmitHelperFactory() => emitHelperFactory ??= new EmitHelperFactory(this);

    internal void AddDiagnostic(ProgramDiagnostic diagnostic) => Diagnostics.Add(diagnostic);

    /// <summary>transformer.ts noEmitSubstitution.</summary>
    internal static Node NoEmitSubstitution(EmitHint hint, Node node) => node;

    /// <summary>transformer.ts noEmitNotification.</summary>
    internal static void NoEmitNotification(EmitHint hint, Node node, Action<EmitHint, Node> callback) => callback(hint, node);

    /// <summary>The chained onSubstituteNode hook: a transformer reads the previous one and sets its own while it is initialized.</summary>
    internal Func<EmitHint, Node, Node> OnSubstituteNode
    {
        get => onSubstituteNode;
        set
        {
            Debug.Assert(State < TransformationState.Initialized, "Cannot modify transformation hooks after initialization has completed.");
            Debug.Assert(value is not null, "Value must not be 'undefined'");
            onSubstituteNode = value!;
        }
    }

    /// <summary>The chained onEmitNode hook.</summary>
    internal Action<EmitHint, Node, Action<EmitHint, Node>> OnEmitNode
    {
        get => onEmitNode;
        set
        {
            Debug.Assert(State < TransformationState.Initialized, "Cannot modify transformation hooks after initialization has completed.");
            Debug.Assert(value is not null, "Value must not be 'undefined'");
            onEmitNode = value!;
        }
    }

    /// <summary>Enables expression substitutions in the pretty printer for the provided SyntaxKind.</summary>
    internal void EnableSubstitution(SyntaxKind kind)
    {
        Debug.Assert(State < TransformationState.Completed, "Cannot modify the transformation context after transformation has completed.");
        enabledSyntaxKindFeatures[(int)kind] |= SyntaxKindFeatureFlags.Substitution;
    }

    /// <summary>Determines whether expression substitutions are enabled for the provided node.</summary>
    internal bool IsSubstitutionEnabled(Node node)
    {
        return (enabledSyntaxKindFeatures[(int)Store.Kind(node)] & SyntaxKindFeatureFlags.Substitution) != 0
            && (Utilities.GetEmitFlags(Store, node) & EmitFlags.NoSubstitution) == 0;
    }

    /// <summary>Enables before/after emit notifications in the pretty printer for the provided SyntaxKind.</summary>
    internal void EnableEmitNotification(SyntaxKind kind)
    {
        Debug.Assert(State < TransformationState.Completed, "Cannot modify the transformation context after transformation has completed.");
        enabledSyntaxKindFeatures[(int)kind] |= SyntaxKindFeatureFlags.EmitNotifications;
    }

    /// <summary>Determines whether before/after emit notifications should be raised in the pretty printer when it emits a node.</summary>
    internal bool IsEmitNotificationEnabled(Node node)
    {
        return (enabledSyntaxKindFeatures[(int)Store.Kind(node)] & SyntaxKindFeatureFlags.EmitNotifications) != 0
            || (Utilities.GetEmitFlags(Store, node) & EmitFlags.AdviseOnEmitNode) != 0;
    }

    private void AssertCanModifyLexicalEnvironment()
    {
        Debug.Assert(State > TransformationState.Uninitialized, "Cannot modify the lexical environment during initialization.");
        Debug.Assert(State < TransformationState.Completed, "Cannot modify the lexical environment after transformation has completed.");
    }

    /// <summary>Records a hoisted variable declaration for the provided name within a lexical environment.</summary>
    internal void HoistVariableDeclaration(Node name)
    {
        AssertCanModifyLexicalEnvironment();
        var decl = FactoryEmitNode.SetEmitFlags(Store, Factory.CreateVariableDeclaration(name, default, default, default), EmitFlags.NoNestedSourceMaps);
        if (lexicalEnvironmentVariableDeclarations is null)
        {
            lexicalEnvironmentVariableDeclarations = [decl];
        }
        else
        {
            lexicalEnvironmentVariableDeclarations.Add(decl);
        }
        if ((lexicalEnvironmentFlags & LexicalEnvironmentFlags.InParameters) != 0)
        {
            lexicalEnvironmentFlags |= LexicalEnvironmentFlags.VariablesHoistedInParameters;
        }
    }

    /// <summary>Records a hoisted function declaration within a lexical environment.</summary>
    internal void HoistFunctionDeclaration(Node func)
    {
        AssertCanModifyLexicalEnvironment();
        FactoryEmitNode.SetEmitFlags(Store, func, EmitFlags.CustomPrologue);
        if (lexicalEnvironmentFunctionDeclarations is null)
        {
            lexicalEnvironmentFunctionDeclarations = [func];
        }
        else
        {
            lexicalEnvironmentFunctionDeclarations.Add(func);
        }
    }

    /// <summary>Adds an initialization statement to the top of the lexical environment.</summary>
    internal void AddInitializationStatement(Node node)
    {
        AssertCanModifyLexicalEnvironment();
        FactoryEmitNode.SetEmitFlags(Store, node, EmitFlags.CustomPrologue);
        if (lexicalEnvironmentStatements is null)
        {
            lexicalEnvironmentStatements = [node];
        }
        else
        {
            lexicalEnvironmentStatements.Add(node);
        }
    }

    private static void SetAt<T>(List<T> stack, int index, T value)
    {
        if (index == stack.Count)
        {
            stack.Add(value);
        }
        else
        {
            stack[index] = value;
        }
    }

    /// <summary>
    /// Starts a new lexical environment. Any existing hoisted variable or function declarations
    /// are pushed onto a stack, and the related storage variables are reset.
    /// </summary>
    internal void StartLexicalEnvironment()
    {
        AssertCanModifyLexicalEnvironment();
        Debug.Assert(!lexicalEnvironmentSuspended, "Lexical environment is suspended.");

        // Save the current lexical environment. Rather than resizing the array we adjust the
        // stack size variable. This allows us to reuse existing array slots we've
        // already allocated between transformations to avoid allocation and GC overhead during
        // transformation.
        SetAt(lexicalEnvironmentVariableDeclarationsStack, lexicalEnvironmentStackOffset, lexicalEnvironmentVariableDeclarations);
        SetAt(lexicalEnvironmentFunctionDeclarationsStack, lexicalEnvironmentStackOffset, lexicalEnvironmentFunctionDeclarations);
        SetAt(lexicalEnvironmentStatementsStack, lexicalEnvironmentStackOffset, lexicalEnvironmentStatements);
        SetAt(lexicalEnvironmentFlagsStack, lexicalEnvironmentStackOffset, lexicalEnvironmentFlags);
        lexicalEnvironmentStackOffset++;
        lexicalEnvironmentVariableDeclarations = null;
        lexicalEnvironmentFunctionDeclarations = null;
        lexicalEnvironmentStatements = null;
        lexicalEnvironmentFlags = LexicalEnvironmentFlags.None;
    }

    /// <summary>Suspends the current lexical environment, usually after visiting a parameter list.</summary>
    internal void SuspendLexicalEnvironment()
    {
        AssertCanModifyLexicalEnvironment();
        Debug.Assert(!lexicalEnvironmentSuspended, "Lexical environment is already suspended.");
        lexicalEnvironmentSuspended = true;
    }

    /// <summary>Resumes a suspended lexical environment, usually before visiting a function body.</summary>
    internal void ResumeLexicalEnvironment()
    {
        AssertCanModifyLexicalEnvironment();
        Debug.Assert(lexicalEnvironmentSuspended, "Lexical environment is not suspended.");
        lexicalEnvironmentSuspended = false;
    }

    /// <summary>
    /// Ends a lexical environment. The previous set of hoisted declarations are restored and
    /// any hoisted declarations added in this environment are returned.
    /// </summary>
    internal List<Node>? EndLexicalEnvironment()
    {
        AssertCanModifyLexicalEnvironment();
        Debug.Assert(!lexicalEnvironmentSuspended, "Lexical environment is suspended.");

        List<Node>? statements = null;
        if (lexicalEnvironmentVariableDeclarations is not null ||
            lexicalEnvironmentFunctionDeclarations is not null ||
            lexicalEnvironmentStatements is not null)
        {
            if (lexicalEnvironmentFunctionDeclarations is not null)
            {
                statements = [.. lexicalEnvironmentFunctionDeclarations];
            }

            if (lexicalEnvironmentVariableDeclarations is not null)
            {
                var statement = Factory.CreateVariableStatement(
                    default,
                    Factory.CreateVariableDeclarationList(Factory.CreateNodeArray(lexicalEnvironmentVariableDeclarations)));

                FactoryEmitNode.SetEmitFlags(Store, statement, EmitFlags.CustomPrologue);

                if (statements is null)
                {
                    statements = [statement];
                }
                else
                {
                    statements.Add(statement);
                }
            }

            if (lexicalEnvironmentStatements is not null)
            {
                if (statements is null)
                {
                    statements = [.. lexicalEnvironmentStatements];
                }
                else
                {
                    statements.AddRange(lexicalEnvironmentStatements);
                }
            }
        }

        // Restore the previous lexical environment.
        lexicalEnvironmentStackOffset--;
        lexicalEnvironmentVariableDeclarations = lexicalEnvironmentVariableDeclarationsStack[lexicalEnvironmentStackOffset];
        lexicalEnvironmentFunctionDeclarations = lexicalEnvironmentFunctionDeclarationsStack[lexicalEnvironmentStackOffset];
        lexicalEnvironmentStatements = lexicalEnvironmentStatementsStack[lexicalEnvironmentStackOffset];
        lexicalEnvironmentFlags = lexicalEnvironmentFlagsStack[lexicalEnvironmentStackOffset];
        if (lexicalEnvironmentStackOffset == 0)
        {
            lexicalEnvironmentVariableDeclarationsStack = [];
            lexicalEnvironmentFunctionDeclarationsStack = [];
            lexicalEnvironmentStatementsStack = [];
            lexicalEnvironmentFlagsStack = [];
        }
        return statements;
    }

    internal void SetLexicalEnvironmentFlags(LexicalEnvironmentFlags flags, bool value)
    {
        lexicalEnvironmentFlags = value ?
            lexicalEnvironmentFlags | flags :
            lexicalEnvironmentFlags & ~flags;
    }

    internal LexicalEnvironmentFlags GetLexicalEnvironmentFlags() => lexicalEnvironmentFlags;

    /// <summary>Starts a block scope. Any existing block hoisted variables are pushed onto the stack and the related storage variables are reset.</summary>
    internal void StartBlockScope()
    {
        Debug.Assert(State > TransformationState.Uninitialized, "Cannot start a block scope during initialization.");
        Debug.Assert(State < TransformationState.Completed, "Cannot start a block scope after transformation has completed.");
        SetAt(blockScopedVariableDeclarationsStack, blockScopeStackOffset, blockScopedVariableDeclarations);
        blockScopeStackOffset++;
        blockScopedVariableDeclarations = null;
    }

    /// <summary>Ends a block scope. The previous set of block hoisted variables are restored. Any hoisted declarations are returned.</summary>
    internal List<Node>? EndBlockScope()
    {
        Debug.Assert(State > TransformationState.Uninitialized, "Cannot end a block scope during initialization.");
        Debug.Assert(State < TransformationState.Completed, "Cannot end a block scope after transformation has completed.");
        List<Node>? statements = null;
        if (Core.Some(blockScopedVariableDeclarations))
        {
            var declarations = new List<Node>(blockScopedVariableDeclarations.Count);
            foreach (var identifier in blockScopedVariableDeclarations)
            {
                declarations.Add(Factory.CreateVariableDeclaration(identifier, default, default, default));
            }
            statements =
            [
                Factory.CreateVariableStatement(
                    default,
                    Factory.CreateVariableDeclarationList(Factory.CreateNodeArray(declarations), NodeFlags.Let)),
            ];
        }
        blockScopeStackOffset--;
        blockScopedVariableDeclarations = blockScopedVariableDeclarationsStack[blockScopeStackOffset];
        if (blockScopeStackOffset == 0)
        {
            blockScopedVariableDeclarationsStack = [];
        }
        return statements;
    }

    internal void AddBlockScopedVariable(Node name)
    {
        Debug.Assert(blockScopeStackOffset > 0, "Cannot add a block scoped variable outside of an iteration body.");
        (blockScopedVariableDeclarations ??= []).Add(name);
    }

    internal void RequestEmitHelper(EmitHelper helper)
    {
        Debug.Assert(State > TransformationState.Uninitialized, "Cannot modify the transformation context during initialization.");
        Debug.Assert(State < TransformationState.Completed, "Cannot modify the transformation context after transformation has completed.");
        Debug.Assert(!helper.Scoped, "Cannot request a scoped emit helper.");
        if (helper.Dependencies is not null)
        {
            foreach (var h in helper.Dependencies)
            {
                RequestEmitHelper(h);
            }
        }
        emitHelpers = Core.Append(emitHelpers, helper);
    }

    internal List<EmitHelper>? ReadEmitHelpers()
    {
        Debug.Assert(State > TransformationState.Uninitialized, "Cannot modify the transformation context during initialization.");
        Debug.Assert(State < TransformationState.Completed, "Cannot modify the transformation context after transformation has completed.");
        var helpers = emitHelpers;
        emitHelpers = null;
        return helpers;
    }

    // dispose: release references to external entries for GC purposes.
    internal void ReleaseForDispose()
    {
        lexicalEnvironmentVariableDeclarations = null;
        lexicalEnvironmentVariableDeclarationsStack = [];
        lexicalEnvironmentFunctionDeclarations = null;
        lexicalEnvironmentFunctionDeclarationsStack = [];
        onSubstituteNode = NoEmitSubstitution;
        onEmitNode = NoEmitNotification;
        emitHelpers = null;
    }
}
