// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/binder.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/binder/binder.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; the binder's state lives in instances (one per thread, reused), its
// output in a BinderStore (docs/phase2-binder-layout.md). Module B1: traversal, containers, symbols and declarations;
// Binder.Flow.cs holds the control flow traversal, Binder.Special.cs the strict-mode checks and the stubs of module B3
// (the JavaScript special assignments and the delayed JSDoc typedef/callback binding).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>binder.ts: ModuleInstanceState.</summary>
internal enum ModuleInstanceState
{
    NonInstantiated = 0,
    Instantiated = 1,
    ConstEnumOnly = 2,
}

/// <summary>binder.ts: ContainerFlags.</summary>
[Flags]
internal enum ContainerFlags
{
    // The current node is not a container, and no container manipulation should happen before
    // recursing into it.
    None = 0,

    // The current node is a container.  It should be set as the current container (and block-
    // container) before recursing into it.  The current node does not have locals.  Examples:
    //
    //      Classes, ObjectLiterals, TypeLiterals, Interfaces...
    IsContainer = 1 << 0,

    // The current node is a block-scoped-container.  It should be set as the current block-
    // container before recursing into it.  Examples:
    //
    //      Blocks (when not parented by functions), Catch clauses, For/For-in/For-of statements...
    IsBlockScopedContainer = 1 << 1,

    // The current node is the container of a control flow path. The current control flow should
    // be saved and restored, and a new control flow initialized within the container.
    IsControlFlowContainer = 1 << 2,

    IsFunctionLike = 1 << 3,
    IsFunctionExpression = 1 << 4,
    HasLocals = 1 << 5,
    IsInterface = 1 << 6,
    IsObjectLiteralOrClassExpressionMethodOrAccessor = 1 << 7,
    PropagatesThisKeyword = 1 << 8,
}

/// <summary>
/// binder.ts: createBinder / bindSourceFile. TypeScript keeps the binder's state in the closure of one binder
/// (createBinder); here the state lives in the instances of this class, one per thread and reused, like the parser's
/// (typescript-go: the Binder struct of internal/binder/binder.go, taken from a pool per file).
/// </summary>
internal sealed partial class Binder
{
    [ThreadStatic]
    private static Binder? threadBinder;

    private NodeStore store = null!;
    private BinderStore b = null!;
    private TextTable texts = null!;
    private Node file;
    private CompilerOptions options = null!;
    private ScriptTarget languageVersion;
    private Node parent;
    private Node container; // IsContainer | EntityNameExpression
    private Node thisParentContainer; // Container one level up
    private Node blockScopeContainer;
    private Node lastContainer;
    private List<Node>? delayedTypeAliases;
    private bool seenThisKeyword;
    private List<Node>? jsDocImports;

    // state used for emit helpers
    private NodeFlags emitFlags;

    // If this file is an external module, then it is automatically in strict-mode according to
    // ES6.  If it is not an external module, then we'll determine if it is in strict mode or
    // not depending on if we see "use strict" in certain places or if we hit a class/namespace
    // or if compiler options contain alwaysStrict.
    private bool inStrictMode;

    // If we are binding an assignment pattern, we will bind certain expressions differently.
    private bool inAssignmentPattern;

    private int symbolCount;

    // file.commonJsModuleIndicator (set by the JavaScript special assignments, module B3).
    private Node commonJsModuleIndicator;

    // getModuleInstanceState's visited map, reused.
    private readonly Dictionary<int, ModuleInstanceState?> moduleInstanceStates = [];

    // forEachChild(node, bind, bindEach) callbacks.
    private readonly Func<Node, object?> bindCallback;
    private readonly Func<NodeList, object?> bindEachCallback;

    // InternalSymbolName and other constant names as texts of the file's table (created on first use per file).
    private TextRef nameCall;
    private TextRef nameConstructor;
    private TextRef nameNew;
    private TextRef nameIndex;
    private TextRef nameExportStar;
    private TextRef nameGlobal;
    private TextRef nameMissing;
    private TextRef nameType;
    private TextRef nameObject;
    private TextRef nameJSXAttributes;
    private TextRef nameClass;
    private TextRef nameFunction;
    private TextRef nameComputed;
    private TextRef nameExportEquals;
    private TextRef nameDefault;
    private TextRef namePrototype;

    private Binder()
    {
        bindCallback = n =>
        {
            Bind(n);
            return null;
        };
        bindEachCallback = list =>
        {
            BindEach(list);
            return null;
        };
    }

    /// <summary>
    /// binder.ts: bindSourceFile(file, options). Binds the file once (TypeScript: `if (!file.locals)`): the output is
    /// stored in a <see cref="BinderStore"/> owned by the result (<see cref="SourceFile.Binding"/>).
    /// </summary>
    internal static void BindSourceFile(SourceFile file, CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(options);
        if (file.Binding is not null)
        {
            return;
        }
        var binder = threadBinder ?? new Binder();
        threadBinder = null;
        try
        {
            binder.BindFile(file, options);
        }
        finally
        {
            // Step BO1: the binder store's range in the thread's binder slab ends here.
            file.Binding?.Commit();
            binder.Clear();
            threadBinder = binder;
        }
    }

    private void BindFile(SourceFile f, CompilerOptions opts)
    {
        store = f.Store;
        texts = store.Texts;
        b = new BinderStore(store);
        f.Binding = b;
        file = f.Root;
        options = opts;
        // utilities.ts getEmitScriptTarget: ES3 is not supported any more and treated as undefined.
        languageVersion = opts.Target is { } target && target != ScriptTarget.ES3 ? target : ScriptTarget.LatestStandard;
        inStrictMode = BindInStrictMode(opts);
        symbolCount = 0;
        // (TypeScript shares one unreachable flow node per binder; here it is one per file, docs/phase2-binder-layout.md.)
        unreachableFlow = b.CreateFlowNode(FlowFlags.Unreachable, Node.None, FlowNode.None);

        Bind(file);
        b.SymbolCount = symbolCount;
        // (file.classifiableNames: not kept, nothing in Track O reads them.)
        DelayedBindJSDocTypedefTag();
        BindJSDocImports();
    }

    private void Clear()
    {
        store = null!;
        b = null!;
        texts = null!;
        file = default;
        options = null!;
        languageVersion = default;
        parent = default;
        container = default;
        thisParentContainer = default;
        blockScopeContainer = default;
        lastContainer = default;
        delayedTypeAliases = null;
        jsDocImports = null;
        seenThisKeyword = false;
        commonJsModuleIndicator = default;
        ClearFlowState();
        inAssignmentPattern = false;
        emitFlags = NodeFlags.None;
        nameCall = nameConstructor = nameNew = nameIndex = nameExportStar = nameGlobal = nameMissing = nameType = default;
        nameObject = nameJSXAttributes = nameClass = nameFunction = nameComputed = nameExportEquals = nameDefault = namePrototype = nameModule = default;
    }

    private bool BindInStrictMode(CompilerOptions opts)
    {
        // utilities.ts getAlwaysStrict: alwaysStrict !== false.
        if (opts.AlwaysStrict != false && !store.IsDeclarationFile(file))
        {
            // bind in strict mode source files with alwaysStrict option
            return true;
        }
        return store.ExternalModuleIndicator(file) is not null;
    }

    // A constant name as a text of the file's table.
    private TextRef Constant(ref TextRef slot, string value)
    {
        if (slot.IsNone)
        {
            slot = texts.Add(value);
        }
        return slot;
    }

    /// <summary>
    /// Inside the binder, we may create a diagnostic for an as-yet unbound node (with potentially no parent pointers, implying no accessible source file)
    /// If so, the node _must_ be in the current file (as that's the only way anything could have traversed to it to yield it as the error node)
    /// This version of `createDiagnosticForNode` uses the binder's context to account for this, and always yields correct diagnostics even in these situations.
    /// </summary>
    private DiagnosticWithLocation CreateDiagnosticForNode(Node node, DiagnosticMessage message, params object?[] args)
    {
        return Utilities.CreateDiagnosticForNodeInSourceFile(store, file, node, message, args);
    }

    private Symbol CreateSymbol(SymbolFlags flags, TextRef name)
    {
        symbolCount++;
        return b.CreateSymbol(flags, name);
    }

    private void AddDeclarationToSymbol(Symbol symbol, Node node, SymbolFlags symbolFlags)
    {
        b.SetFlags(symbol, b.Flags(symbol) | symbolFlags);

        b.SetSymbolOf(node, symbol);
        if (!HasDeclaration(symbol, node))
        {
            b.AddDeclaration(symbol, node);
        }

        if ((symbolFlags & (SymbolFlags.Class | SymbolFlags.Enum | SymbolFlags.Module | SymbolFlags.Variable)) != 0 && b.Exports(symbol).IsNone)
        {
            b.SetExports(symbol, b.CreateTable());
        }

        if ((symbolFlags & (SymbolFlags.Class | SymbolFlags.Interface | SymbolFlags.TypeLiteral | SymbolFlags.ObjectLiteral)) != 0 && b.Members(symbol).IsNone)
        {
            b.SetMembers(symbol, b.CreateTable());
        }

        // On merge of const enum module with class or function, reset const enum only flag (namespaces will already recalculate)
        if (b.ConstEnumOnlyModule(symbol) == true && (b.Flags(symbol) & (SymbolFlags.Function | SymbolFlags.Class | SymbolFlags.RegularEnum)) != 0)
        {
            b.SetConstEnumOnlyModule(symbol, false);
        }

        if ((symbolFlags & SymbolFlags.Value) != 0)
        {
            SetValueDeclaration(symbol, node);
        }
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool HasDeclaration(Symbol symbol, Node node)
    {
        foreach (var declaration in b.Declarations(symbol))
        {
            if (declaration == node)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>utilities.ts: setValueDeclaration.</summary>
    private void SetValueDeclaration(Symbol symbol, Node node)
    {
        var valueDeclaration = b.ValueDeclaration(symbol);
        if (
            valueDeclaration.IsNone ||
            !((store.Flags(node) & NodeFlags.Ambient) != 0 && !Utilities.IsInJSFile(store, node) && (store.Flags(valueDeclaration) & NodeFlags.Ambient) == 0) &&
                (IsAssignmentDeclaration(valueDeclaration) && !IsAssignmentDeclaration(node)) ||
            (store.Kind(valueDeclaration) != store.Kind(node) && IsEffectiveModuleDeclaration(valueDeclaration)))
        {
            // other kinds of value declarations take precedence over modules and assignment declarations
            b.SetValueDeclaration(symbol, node);
        }
    }

    /// <summary>utilities.ts: isAssignmentDeclaration.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsAssignmentDeclaration(Node decl)
    {
        var kind = store.Kind(decl);
        return kind == SyntaxKind.BinaryExpression || kind == SyntaxKind.PropertyAccessExpression || kind == SyntaxKind.ElementAccessExpression
            || kind == SyntaxKind.Identifier || kind == SyntaxKind.CallExpression;
    }

    /// <summary>utilities.ts: isEffectiveModuleDeclaration.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsEffectiveModuleDeclaration(Node node)
    {
        return store.Kind(node) == SyntaxKind.ModuleDeclaration || store.Kind(node) == SyntaxKind.Identifier;
    }

    // Should not be called on a declaration with a computed property name,
    // unless it is a well known Symbol.
    private TextRef GetDeclarationName(Node node)
    {
        if (store.Kind(node) == SyntaxKind.ExportAssignment)
        {
            return store.IsExportEquals(node) ? Constant(ref nameExportEquals, InternalSymbolName.ExportEquals) : Constant(ref nameDefault, InternalSymbolName.Default);
        }

        var name = Utilities.GetNameOfDeclaration(store, node);
        if (!name.IsNone)
        {
            if (Utilities.IsAmbientModule(store, node))
            {
                var moduleName = Utilities.GetTextOfIdentifierOrLiteral(store, name);
                if (Utilities.IsGlobalScopeAugmentation(store, node))
                {
                    return Constant(ref nameGlobal, InternalSymbolName.Global);
                }
                var quoted = new TextBuilder(texts);
                quoted.Append('"');
                quoted.Append(moduleName);
                quoted.Append('"');
                return quoted.Result();
            }
            if (store.Kind(name) == SyntaxKind.ComputedPropertyName)
            {
                var nameExpression = store.Expression(name);
                // treat computed property names where expression is string/numeric literal as just string/numeric literal
                if (Utilities.IsStringOrNumericLiteralLike(store, nameExpression))
                {
                    return UtilitiesPublic.EscapeLeadingUnderscores(texts, store.Text(nameExpression));
                }
                if (Utilities.IsSignedNumericLiteral(store, nameExpression))
                {
                    return texts.Concat(Scanner.TokenToString(store.Operator(nameExpression))!, store.Text(store.Operand(nameExpression)));
                }
                Debug.Fail("Only computed properties with literal names have declaration names");
            }
            if (store.Kind(name) == SyntaxKind.PrivateIdentifier)
            {
                // containingClass exists because private names only allowed inside classes
                var containingClass = Utilities.GetContainingClass(store, node);
                if (containingClass.IsNone)
                {
                    // we can get here in cases where there is already a parse error.
                    return TextRef.None;
                }
                var containingClassSymbol = b.SymbolOf(containingClass);
                return GetSymbolNameForPrivateIdentifier(containingClassSymbol, store.EscapedText(name));
            }
            if (store.Kind(name) == SyntaxKind.JsxNamespacedName)
            {
                return Utilities.GetEscapedTextOfJsxNamespacedName(store, name);
            }
            return Utilities.IsPropertyNameLiteral(store, name) ? Utilities.GetEscapedTextOfIdentifierOrLiteral(store, name) : TextRef.None;
        }
        switch (store.Kind(node))
        {
            case SyntaxKind.Constructor:
                return Constant(ref nameConstructor, InternalSymbolName.Constructor);
            case SyntaxKind.FunctionType:
            case SyntaxKind.CallSignature:
            case SyntaxKind.JSDocSignature:
                return Constant(ref nameCall, InternalSymbolName.Call);
            case SyntaxKind.ConstructorType:
            case SyntaxKind.ConstructSignature:
                return Constant(ref nameNew, InternalSymbolName.New);
            case SyntaxKind.IndexSignature:
                return Constant(ref nameIndex, InternalSymbolName.Index);
            case SyntaxKind.ExportDeclaration:
                return Constant(ref nameExportStar, InternalSymbolName.ExportStar);
            case SyntaxKind.SourceFile:
                // json file should behave as
                // module.exports = ...
                return Constant(ref nameExportEquals, InternalSymbolName.ExportEquals);
            case SyntaxKind.BinaryExpression:
                if (Utilities.GetAssignmentDeclarationKind(store, node) == AssignmentDeclarationKind.ModuleExports)
                {
                    // module.exports = ...
                    return Constant(ref nameExportEquals, InternalSymbolName.ExportEquals);
                }
                Debug.Fail("Unknown binary declaration kind");
                break;
            case SyntaxKind.JSDocFunctionType:
                return Utilities.IsJSDocConstructSignature(store, node) ? Constant(ref nameNew, InternalSymbolName.New) : Constant(ref nameCall, InternalSymbolName.Call);
            case SyntaxKind.Parameter:
                // Parameters with names are handled at the top of this function.  Parameters
                // without names can only come from JSDocFunctionTypes.
                Debug.Assert(store.Kind(store.Parent(node)) == SyntaxKind.JSDocFunctionType, "Impossible parameter parent kind");
                var functionType = store.Parent(node);
                var index = IndexOf(store.Parameters(functionType), node);
                return texts.Add("arg" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return TextRef.None;
    }

    private int IndexOf(NodeList list, Node node)
    {
        var nodes = store.Nodes(list);
        for (var i = 0; i < nodes.Length; i++)
        {
            if (nodes[i] == node)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>utilities.ts: getSymbolNameForPrivateIdentifier: `__#${getSymbolId(containingClassSymbol)}@${description}`.</summary>
    private TextRef GetSymbolNameForPrivateIdentifier(Symbol containingClassSymbol, TextRef description)
    {
        var name = new TextBuilder(texts);
        name.Append("__#");
        name.Append(b.SymbolId(containingClassSymbol).ToString(System.Globalization.CultureInfo.InvariantCulture));
        name.Append('@');
        name.Append(description);
        return name.Result();
    }

    private string GetDisplayName(Node node)
    {
        if (UtilitiesPublic.IsNamedDeclaration(store, node))
        {
            return Utilities.DeclarationNameToString(store, store.Name(node));
        }
        var declarationName = GetDeclarationName(node);
        Debug.Assert(!declarationName.IsNone);
        return UtilitiesPublic.UnescapeLeadingUnderscores(texts.GetString(declarationName)!);
    }

    /// <summary>
    /// Declares a Symbol for the node and adds it to symbols. Reports errors for conflicting identifier names.
    /// </summary>
    /// <param name="symbolTable">The symbol table which node will be added to.</param>
    /// <param name="parent">node's parent declaration.</param>
    /// <param name="node">The declaration to be added to the symbol table</param>
    /// <param name="includes">The SymbolFlags that node has in addition to its declaration type (eg: export, ambient, etc.)</param>
    /// <param name="excludes">The flags which node cannot be declared alongside in a symbol table. Used to report forbidden declarations.</param>
    /// <param name="isReplaceableByMethod">Whether a method may replace the symbol (JavaScript constructor-declared properties).</param>
    /// <param name="isComputedName">Whether the name is computed (the symbol is named __computed).</param>
    private Symbol DeclareSymbol(SymbolTable symbolTable, Symbol parent, Node node, SymbolFlags includes, SymbolFlags excludes, bool isReplaceableByMethod = false, bool isComputedName = false)
    {
        Debug.Assert(isComputedName || !Utilities.HasDynamicName(store, node));

        var isDefaultExport = Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Default) || store.Kind(node) == SyntaxKind.ExportSpecifier && Utilities.ModuleExportNameIsDefault(store, store.Name(node));

        // The exported symbol for an export default function/class node is always named "default"
        var name = isComputedName ? Constant(ref nameComputed, InternalSymbolName.Computed)
            : isDefaultExport && !parent.IsNone ? Constant(ref nameDefault, InternalSymbolName.Default)
            : GetDeclarationName(node);

        Symbol symbol;
        if (name.IsNone)
        {
            symbol = CreateSymbol(SymbolFlags.None, Constant(ref nameMissing, InternalSymbolName.Missing));
        }
        else
        {
            // Check and see if the symbol table already has a symbol with this name.  If not,
            // create a new symbol with this name and add it to the table.  Note that we don't
            // give the new symbol any flags *yet*.  This ensures that it will not conflict
            // with the 'excludes' flags we pass in.
            //
            // If we do get an existing symbol, see if it conflicts with the new symbol we're
            // creating.  For example, a 'var' symbol and a 'class' symbol will conflict within
            // the same symbol table.  If we have a conflict, report the issue on each
            // declaration we have for this symbol, and then create a new symbol for this
            // declaration.
            //
            // Note that when properties declared in Javascript constructors
            // (marked by isReplaceableByMethod) conflict with another symbol, the property loses.
            // Always. This allows the common Javascript pattern of overwriting a prototype method
            // with an bound instance method of the same type: `this.method = this.method.bind(this)`
            //
            // If we created a new symbol, either because we didn't have a symbol with this name
            // in the symbol table, or we conflicted with an existing symbol, then just add this
            // node as the sole declaration of the new symbol.
            //
            // Otherwise, we'll be merging into a compatible existing symbol (for example when
            // you have multiple 'vars' with the same name in the same container).  In this case
            // just add this node into the declarations list of the symbol.
            // (Track O, step BO2: the name is hashed once for the lookup and the insertion.)
            symbol = b.Get(symbolTable, name, out var nameHash);

            // (classifiableNames: not kept.)

            if (symbol.IsNone)
            {
                symbol = CreateSymbol(SymbolFlags.None, name);
                b.AddNew(symbolTable, name, symbol, nameHash);
                if (isReplaceableByMethod)
                {
                    b.SetIsReplaceableByMethod(symbol, true);
                }
            }
            else if (isReplaceableByMethod && b.IsReplaceableByMethod(symbol) != true)
            {
                // A symbol already exists, so don't add this as a declaration.
                return symbol;
            }
            else if ((b.Flags(symbol) & excludes) != 0)
            {
                if (b.IsReplaceableByMethod(symbol) == true)
                {
                    // Javascript constructor-declared symbols can be discarded in favor of
                    // prototype symbols like methods.
                    symbol = CreateSymbol(SymbolFlags.None, name);
                    b.Set(symbolTable, name, symbol);
                }
                else if (!((includes & SymbolFlags.Variable) != 0 && (b.Flags(symbol) & SymbolFlags.Assignment) != 0))
                {
                    // Assignment declarations are allowed to merge with variables, no matter what other flags they have.
                    ReportDuplicateDeclaration(symbol, node, includes, isDefaultExport);
                    symbol = CreateSymbol(SymbolFlags.None, name);
                }
            }
        }

        AddDeclarationToSymbol(symbol, node, includes);
        if (!b.Parent(symbol).IsNone)
        {
            Debug.Assert(b.Parent(symbol) == parent, "Existing symbol parent should match new one");
        }
        else
        {
            b.SetParent(symbol, parent);
        }

        return symbol;
    }

    // The diagnostics of declareSymbol for a declaration that conflicts with the existing symbol.
    private void ReportDuplicateDeclaration(Symbol symbol, Node node, SymbolFlags includes, bool isDefaultExport)
    {
        if (UtilitiesPublic.IsNamedDeclaration(store, node))
        {
            Utilities.SetParent(store, store.Name(node), node);
        }
        // Report errors every position with duplicate declaration
        // Report errors on previous encountered declarations
        var message = (b.Flags(symbol) & SymbolFlags.BlockScopedVariable) != 0
            ? Diagnostics.Cannot_redeclare_block_scoped_variable_0
            : Diagnostics.Duplicate_identifier_0;
        var messageNeedsName = true;

        if ((b.Flags(symbol) & SymbolFlags.Enum) != 0 || (includes & SymbolFlags.Enum) != 0)
        {
            message = Diagnostics.Enum_declarations_can_only_merge_with_namespace_or_other_enum_declarations;
            messageNeedsName = false;
        }

        var multipleDefaultExports = false;
        if (!b.Declarations(symbol).IsUndefined)
        {
            // If the current node is a default export of some sort, then check if
            // there are any other default exports that we need to error on.
            // We'll know whether we have other default exports depending on if `symbol` already has a declaration list set.
            if (isDefaultExport)
            {
                message = Diagnostics.A_module_cannot_have_multiple_default_exports;
                messageNeedsName = false;
                multipleDefaultExports = true;
            }
            else
            {
                // This is to properly report an error in the case "export default { }" is after export default of class declaration or function declaration.
                // Error on multiple export default in the following case:
                // 1. multiple export default of class declaration or function declaration by checking NodeFlags.Default
                // 2. multiple export default of export assignment. This one doesn't have NodeFlags.Default on (as export default doesn't considered as modifiers)
                if (store.Kind(node) == SyntaxKind.ExportAssignment && !store.IsExportEquals(node))
                {
                    message = Diagnostics.A_module_cannot_have_multiple_default_exports;
                    messageNeedsName = false;
                    multipleDefaultExports = true;
                }
            }
        }

        var relatedInformation = new List<DiagnosticRelatedInformation>();
        if (store.Kind(node) == SyntaxKind.TypeAliasDeclaration && Utilities.NodeIsMissing(store, store.Type(node)) && Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Export)
            && (b.Flags(symbol) & (SymbolFlags.Alias | SymbolFlags.Type | SymbolFlags.Namespace)) != 0)
        {
            // export type T; - may have meant export type { T }?
            relatedInformation.Add(CreateDiagnosticForNode(node, Diagnostics.Did_you_mean_0, "export type { " + UtilitiesPublic.UnescapeLeadingUnderscores(texts.GetString(store.EscapedText(store.Name(node)))!) + " }"));
        }

        var declarationName = Utilities.GetNameOfDeclaration(store, node);
        if (declarationName.IsNone)
        {
            declarationName = node;
        }
        var index = 0;
        foreach (var declaration in b.Declarations(symbol))
        {
            var decl = Utilities.GetNameOfDeclaration(store, declaration);
            if (decl.IsNone)
            {
                decl = declaration;
            }
            var diag = messageNeedsName ? CreateDiagnosticForNode(decl, message, GetDisplayName(declaration)) : CreateDiagnosticForNode(decl, message);
            b.BindDiagnostics.Add(
                multipleDefaultExports ? Utilities.AddRelatedInfo(diag, CreateDiagnosticForNode(declarationName, index == 0 ? Diagnostics.Another_export_default_is_here : Diagnostics.and_here)) : diag);
            if (multipleDefaultExports)
            {
                relatedInformation.Add(CreateDiagnosticForNode(decl, Diagnostics.The_first_export_default_is_here));
            }
            index++;
        }

        var nodeDiag = messageNeedsName ? CreateDiagnosticForNode(declarationName, message, GetDisplayName(node)) : CreateDiagnosticForNode(declarationName, message);
        b.BindDiagnostics.Add(Utilities.AddRelatedInfo(nodeDiag, [.. relatedInformation]));
    }

    private Symbol DeclareModuleMember(Node node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        var hasExportModifier = (Utilities.GetCombinedModifierFlags(store, node) & ModifierFlags.Export) != 0 || JsdocTreatAsExported(node);
        var containerSymbol = b.SymbolOf(container);
        if ((symbolFlags & SymbolFlags.Alias) != 0)
        {
            if (store.Kind(node) == SyntaxKind.ExportSpecifier || (store.Kind(node) == SyntaxKind.ImportEqualsDeclaration && hasExportModifier))
            {
                return DeclareSymbol(b.Exports(containerSymbol), containerSymbol, node, symbolFlags, symbolExcludes);
            }
            Debug.Assert(Utilities.CanHaveLocals(store, container));
            return DeclareSymbol(b.LocalsOf(container), Symbol.None, node, symbolFlags, symbolExcludes);
        }

        // Exported module members are given 2 symbols: A local symbol that is classified with an ExportValue flag,
        // and an associated export symbol with all the correct flags set on it. There are 2 main reasons:
        //
        //   1. We treat locals and exports of the same name as mutually exclusive within a container.
        //      That means the binder will issue a Duplicate Identifier error if you mix locals and exports
        //      with the same name in the same container.
        //      TODO: Make this a more specific error and decouple it from the exclusion logic.
        //   2. When we checkIdentifier in the checker, we set its resolved symbol to the local symbol,
        //      but return the export symbol (by calling getExportSymbolOfValueSymbolIfExported). That way
        //      when the emitter comes back to it, it knows not to qualify the name if it was found in a containing scope.

        // NOTE: Nested ambient modules always should go to to 'locals' table to prevent their automatic merge
        //       during global merging in the checker. Why? The only case when ambient module is permitted inside another module is module augmentation
        //       and this case is specially handled. Module augmentations should only be merged with original module definition
        //       and should never be merged directly with other augmentation, and the latter case would be possible if automatic merge is allowed.
        if (!Utilities.IsAmbientModule(store, node) && (hasExportModifier || (store.Flags(container) & NodeFlags.ExportContext) != 0))
        {
            if (!Utilities.CanHaveLocals(store, container) || b.LocalsOf(container).IsNone || (Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Default) && GetDeclarationName(node).IsNone))
            {
                return DeclareSymbol(b.Exports(containerSymbol), containerSymbol, node, symbolFlags, symbolExcludes); // No local symbol for an unnamed default!
            }
            var exportKind = (symbolFlags & SymbolFlags.Value) != 0 ? SymbolFlags.ExportValue : SymbolFlags.None;
            var local = DeclareSymbol(b.LocalsOf(container), Symbol.None, node, exportKind, symbolExcludes);
            b.SetExportSymbol(local, DeclareSymbol(b.Exports(containerSymbol), containerSymbol, node, symbolFlags, symbolExcludes));
            b.SetLocalSymbolOf(node, local);
            return local;
        }
        Debug.Assert(Utilities.CanHaveLocals(store, container));
        return DeclareSymbol(b.LocalsOf(container), Symbol.None, node, symbolFlags, symbolExcludes);
    }

    private bool JsdocTreatAsExported(Node node)
    {
        if (!store.Parent(node).IsNone && store.Kind(node) == SyntaxKind.ModuleDeclaration)
        {
            node = store.Parent(node);
        }
        if (!Utilities.IsJSDocTypeAlias(store, node))
        {
            return false;
        }
        // jsdoc typedef handling is a bit of a doozy, but to summarize, treat the typedef as exported if:
        // 1. It has an explicit name (since by default typedefs are always directly exported, either at the top level or in a container), or
        if (store.Kind(node) != SyntaxKind.JSDocEnumTag && !store.FullName(node).IsNone)
        {
            return true;
        }
        // 2. The thing a nameless typedef pulls its name from is implicitly a direct export (either by assignment or actual export flag).
        var declName = Utilities.GetNameOfDeclaration(store, node);
        if (declName.IsNone)
        {
            return false;
        }
        var declNameParent = store.Parent(declName);
        if (Utilities.IsPropertyAccessEntityNameExpression(store, declNameParent) && IsTopLevelNamespaceAssignment(declNameParent))
        {
            return true;
        }
        if (Utilities.IsDeclaration(store, declNameParent) && (Utilities.GetCombinedModifierFlags(store, declNameParent) & ModifierFlags.Export) != 0)
        {
            return true;
        }
        // This could potentially be simplified by having `delayedBindJSDocTypedefTag` pass in an override for `hasExportModifier`, since it should
        // already have calculated and branched on most of this.
        return false;
    }

    private bool IsTopLevelNamespaceAssignment(Node propertyAccess)
    {
        var propertyAccessParent = store.Parent(propertyAccess);
        return store.Kind(propertyAccessParent) == SyntaxKind.BinaryExpression
            ? store.Kind(store.Parent(GetParentOfBinaryExpression(propertyAccessParent))) == SyntaxKind.SourceFile
            : store.Kind(store.Parent(propertyAccessParent)) == SyntaxKind.SourceFile;
    }

    private Node GetParentOfBinaryExpression(Node expr)
    {
        while (store.Kind(store.Parent(expr)) == SyntaxKind.BinaryExpression)
        {
            expr = store.Parent(expr);
        }
        return store.Parent(expr);
    }

    // All container nodes are kept on a linked list in declaration order. This list is used by
    // the getLocalNameOfContainer function in the type checker to validate that the local name
    // used for a container is unique.
    private void BindContainer(Node node, ContainerFlags containerFlags)
    {
        // Before we recurse into a node's children, we first save the existing parent, container
        // and block-container.  Then after we pop out of processing the children, we restore
        // these saved values.
        var saveContainer = container;
        var saveThisParentContainer = thisParentContainer;
        var savedBlockScopeContainer = blockScopeContainer;
        var savedInReturnPosition = inReturnPosition;

        if (store.Kind(node) == SyntaxKind.ArrowFunction && store.Kind(store.Body(node)) != SyntaxKind.Block)
        {
            inReturnPosition = true;
        }
        // Depending on what kind of node this is, we may have to adjust the current container
        // and block-container.   If the current node is a container, then it is automatically
        // considered the current block-container as well.  Also, for containers that we know
        // may contain locals, we eagerly initialize the .locals field. We do this because
        // it's highly likely that the .locals will be needed to place some child in (for example,
        // a parameter, or variable declaration).
        //
        // However, we do not proactively create the .locals for block-containers because it's
        // totally normal and common for block-containers to never actually have a block-scoped
        // variable in them.  We don't want to end up allocating an object for every 'block' we
        // run into when most of them won't be necessary.
        //
        // Finally, if this is a block-container, then we clear out any existing .locals object
        // it may contain within it.  This happens in incremental scenarios.  Because we can be
        // reusing a node from a previous compilation, that node may have had 'locals' created
        // for it.  We must clear this so we don't accidentally move any stale data forward from
        // a previous compilation.
        if ((containerFlags & ContainerFlags.IsContainer) != 0)
        {
            if (store.Kind(node) != SyntaxKind.ArrowFunction)
            {
                thisParentContainer = container;
            }
            container = blockScopeContainer = node;
            if ((containerFlags & ContainerFlags.HasLocals) != 0)
            {
                b.SetLocalsOf(container, b.CreateTable());
                AddToContainerChain(container);
            }
        }
        else if ((containerFlags & ContainerFlags.IsBlockScopedContainer) != 0)
        {
            blockScopeContainer = node;
            if ((containerFlags & ContainerFlags.HasLocals) != 0 && !b.LocalsOf(blockScopeContainer).IsNone)
            {
                b.SetLocalsOf(blockScopeContainer, SymbolTable.None);
            }
        }
        if ((containerFlags & ContainerFlags.IsControlFlowContainer) != 0)
        {
            BindControlFlowContainer(node, containerFlags);
        }
        else if ((containerFlags & ContainerFlags.IsInterface) != 0)
        {
            var saveSeenThisKeyword = seenThisKeyword;
            seenThisKeyword = false;
            BindChildren(node);
            Debug.Assert(store.Kind(node) != SyntaxKind.Identifier); // ContainsThis cannot overlap with HasExtendedUnicodeEscape on Identifier
            store.SetFlags(node, seenThisKeyword ? store.Flags(node) | NodeFlags.ContainsThis : store.Flags(node) & ~NodeFlags.ContainsThis);
            seenThisKeyword = saveSeenThisKeyword;
        }
        else
        {
            BindChildren(node);
        }

        inReturnPosition = savedInReturnPosition;
        container = saveContainer;
        thisParentContainer = saveThisParentContainer;
        blockScopeContainer = savedBlockScopeContainer;
    }

    private void BindEachFunctionsFirst(NodeList nodes)
    {
        if (nodes.IsNone)
        {
            return;
        }
        var count = store.Length(nodes);
        for (var i = 0; i < count; i++)
        {
            var n = store.Element(nodes, i);
            if (store.Kind(n) == SyntaxKind.FunctionDeclaration)
            {
                Bind(n);
            }
        }
        for (var i = 0; i < count; i++)
        {
            var n = store.Element(nodes, i);
            if (store.Kind(n) != SyntaxKind.FunctionDeclaration)
            {
                Bind(n);
            }
        }
    }

    private void BindEach(NodeList nodes)
    {
        if (nodes.IsNone)
        {
            return;
        }
        var count = store.Length(nodes);
        for (var i = 0; i < count; i++)
        {
            Bind(store.Element(nodes, i));
        }
    }

    // forEachChild(node, bind, bindEach). Track O (step BO2): the kinds that reach here most often (on the tuning
    // corpus about two thirds of the calls) bind their children directly, in forEachChild's order (Generated/
    // NodeVisitor.g.cs; bind and bindEach return undefined, so every child is visited); the others through the
    // callbacks.
    private void BindEachChild(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.PropertyAccessExpression:
                Bind(store.Expression(node));
                Bind(store.QuestionDotToken(node));
                Bind(store.Name(node));
                return;
            case SyntaxKind.ElementAccessExpression:
                Bind(store.Expression(node));
                Bind(store.QuestionDotToken(node));
                Bind(store.ArgumentExpression(node));
                return;
            case SyntaxKind.CallExpression:
                Bind(store.Expression(node));
                Bind(store.QuestionDotToken(node));
                BindEach(store.TypeArguments(node));
                BindEach(store.Arguments(node));
                return;
            case SyntaxKind.NewExpression:
                Bind(store.Expression(node));
                BindEach(store.TypeArguments(node));
                BindEach(store.Arguments(node));
                return;
            case SyntaxKind.TypeReference:
                Bind(store.TypeName(node));
                BindEach(store.TypeArguments(node));
                return;
            case SyntaxKind.VariableDeclaration:
                Bind(store.Name(node));
                Bind(store.ExclamationToken(node));
                Bind(store.Type(node));
                Bind(store.Initializer(node));
                return;
            case SyntaxKind.VariableDeclarationList:
                BindEach(store.Declarations(node));
                return;
            case SyntaxKind.VariableStatement:
                BindEach(store.Modifiers(node));
                Bind(store.DeclarationList(node));
                return;
            case SyntaxKind.PropertyAssignment:
                BindEach(store.Modifiers(node));
                Bind(store.Name(node));
                Bind(store.QuestionToken(node));
                Bind(store.ExclamationToken(node));
                Bind(store.Initializer(node));
                return;
            case SyntaxKind.ObjectLiteralExpression:
                BindEach(store.Properties(node));
                return;
            case SyntaxKind.ArrayLiteralExpression:
                BindEach(store.Elements(node));
                return;
            case SyntaxKind.ParenthesizedExpression:
                Bind(store.Expression(node));
                return;
            case SyntaxKind.ImportSpecifier:
                Bind(store.PropertyName(node));
                Bind(store.Name(node));
                return;
            case SyntaxKind.FunctionDeclaration:
                BindEach(store.Modifiers(node));
                Bind(store.AsteriskToken(node));
                Bind(store.Name(node));
                BindEach(store.TypeParameters(node));
                BindEach(store.Parameters(node));
                Bind(store.Type(node));
                Bind(store.Body(node));
                return;
            case SyntaxKind.MethodDeclaration:
                BindEach(store.Modifiers(node));
                Bind(store.AsteriskToken(node));
                Bind(store.Name(node));
                Bind(store.QuestionToken(node));
                Bind(store.ExclamationToken(node));
                BindEach(store.TypeParameters(node));
                BindEach(store.Parameters(node));
                Bind(store.Type(node));
                Bind(store.Body(node));
                return;
            case SyntaxKind.ArrowFunction:
                BindEach(store.Modifiers(node));
                BindEach(store.TypeParameters(node));
                BindEach(store.Parameters(node));
                Bind(store.Type(node));
                Bind(store.EqualsGreaterThanToken(node));
                Bind(store.Body(node));
                return;
            case SyntaxKind.FunctionType:
                BindEach(store.Modifiers(node));
                BindEach(store.TypeParameters(node));
                BindEach(store.Parameters(node));
                Bind(store.Type(node));
                return;
            case SyntaxKind.MethodSignature:
                BindEach(store.Modifiers(node));
                Bind(store.Name(node));
                Bind(store.QuestionToken(node));
                BindEach(store.TypeParameters(node));
                BindEach(store.Parameters(node));
                Bind(store.Type(node));
                return;
            case SyntaxKind.PropertySignature:
                BindEach(store.Modifiers(node));
                Bind(store.Name(node));
                Bind(store.QuestionToken(node));
                Bind(store.Type(node));
                Bind(store.Initializer(node));
                return;
            case SyntaxKind.PropertyDeclaration:
                BindEach(store.Modifiers(node));
                Bind(store.Name(node));
                Bind(store.QuestionToken(node));
                Bind(store.ExclamationToken(node));
                Bind(store.Type(node));
                Bind(store.Initializer(node));
                return;
            case SyntaxKind.ShorthandPropertyAssignment:
                BindEach(store.Modifiers(node));
                Bind(store.Name(node));
                Bind(store.QuestionToken(node));
                Bind(store.ExclamationToken(node));
                Bind(store.EqualsToken(node));
                Bind(store.ObjectAssignmentInitializer(node));
                return;
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.InterfaceDeclaration:
                BindEach(store.Modifiers(node));
                Bind(store.Name(node));
                BindEach(store.TypeParameters(node));
                BindEach(store.HeritageClauses(node));
                BindEach(store.Members(node));
                return;
            case SyntaxKind.TypeAliasDeclaration:
                BindEach(store.Modifiers(node));
                Bind(store.Name(node));
                BindEach(store.TypeParameters(node));
                Bind(store.Type(node));
                return;
            case SyntaxKind.ModuleDeclaration:
                BindEach(store.Modifiers(node));
                Bind(store.Name(node));
                Bind(store.Body(node));
                return;
            case SyntaxKind.ImportDeclaration:
                BindEach(store.Modifiers(node));
                Bind(store.ImportClause(node));
                Bind(store.ModuleSpecifier(node));
                Bind(store.Attributes(node));
                return;
            case SyntaxKind.ImportClause:
                Bind(store.Name(node));
                Bind(store.NamedBindings(node));
                return;
            case SyntaxKind.TypeParameter:
                BindEach(store.Modifiers(node));
                Bind(store.Name(node));
                Bind(store.Constraint(node));
                Bind(store.Default(node));
                Bind(store.Expression(node));
                return;
            case SyntaxKind.EnumMember:
                Bind(store.Name(node));
                Bind(store.Initializer(node));
                return;
            case SyntaxKind.QualifiedName:
                Bind(store.Left(node));
                Bind(store.Right(node));
                return;
            case SyntaxKind.AsExpression:
                Bind(store.Expression(node));
                Bind(store.Type(node));
                return;
            case SyntaxKind.ExpressionWithTypeArguments:
                Bind(store.Expression(node));
                BindEach(store.TypeArguments(node));
                return;
            case SyntaxKind.TemplateExpression:
                Bind(store.Head(node));
                BindEach(store.TemplateSpans(node));
                return;
            case SyntaxKind.TemplateSpan:
                Bind(store.Expression(node));
                Bind(store.Literal(node));
                return;
            case SyntaxKind.IndexedAccessType:
                Bind(store.ObjectType(node));
                Bind(store.IndexType(node));
                return;
            case SyntaxKind.LiteralType:
                Bind(store.Literal(node));
                return;
            case SyntaxKind.PrefixUnaryExpression:
                Bind(store.Operand(node));
                return;
            case SyntaxKind.ArrayType:
                Bind(store.ElementType(node));
                return;
            case SyntaxKind.TypeOperator:
                Bind(store.Type(node));
                return;
            case SyntaxKind.SpreadElement:
                Bind(store.Expression(node));
                return;
            case SyntaxKind.UnionType:
            case SyntaxKind.IntersectionType:
            case SyntaxKind.HeritageClause:
                BindEach(store.Types(node));
                return;
            case SyntaxKind.TypeLiteral:
                BindEach(store.Members(node));
                return;
            case SyntaxKind.ArrayBindingPattern:
            case SyntaxKind.ObjectBindingPattern:
            case SyntaxKind.NamedImports:
                BindEach(store.Elements(node));
                return;
            default:
                NodeVisitor.ForEachChild(store, node, bindCallback, bindEachCallback);
                return;
        }
    }

    private void AddToContainerChain(Node next)
    {
        if (!lastContainer.IsNone)
        {
            b.SetNextContainerOf(lastContainer, next);
        }

        lastContainer = next;
    }

    private Symbol DeclareSymbolAndAddToSymbolTable(Node node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        switch (store.Kind(container))
        {
            // Modules, source files, and classes need specialized handling for how their
            // members are declared (for example, a member of a class will go into a specific
            // symbol table depending on if it is static or not). We defer to specialized
            // handlers to take care of declaring these child members.
            case SyntaxKind.ModuleDeclaration:
                return DeclareModuleMember(node, symbolFlags, symbolExcludes);

            case SyntaxKind.SourceFile:
                return DeclareSourceFileMember(node, symbolFlags, symbolExcludes);

            case SyntaxKind.ClassExpression:
            case SyntaxKind.ClassDeclaration:
                return DeclareClassMember(node, symbolFlags, symbolExcludes);

            case SyntaxKind.EnumDeclaration:
                return DeclareSymbol(b.Exports(b.SymbolOf(container)), b.SymbolOf(container), node, symbolFlags, symbolExcludes);

            case SyntaxKind.TypeLiteral:
            case SyntaxKind.JSDocTypeLiteral:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.JsxAttributes:
                // Interface/Object-types always have their children added to the 'members' of
                // their container. They are only accessible through an instance of their
                // container, and are never in scope otherwise (even inside the body of the
                // object / type / interface declaring them). An exception is type parameters,
                // which are in scope without qualification (similar to 'locals').
                return DeclareSymbol(b.Members(b.SymbolOf(container)), b.SymbolOf(container), node, symbolFlags, symbolExcludes);

            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.CallSignature:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.JSDocSignature:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.Constructor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.JSDocFunctionType:
            case SyntaxKind.ClassStaticBlockDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.MappedType:
                // All the children of these container types are never visible through another
                // symbol (i.e. through another symbol's 'exports' or 'members').  Instead,
                // they're only accessed 'lexically' (i.e. from code that exists underneath
                // their container in the tree). To accomplish this, we simply add their declared
                // symbol to the 'locals' of the container.  These symbols can then be found as
                // the type checker walks up the containers, checking them for matching names.
                return DeclareSymbol(b.LocalsOf(container), Symbol.None, node, symbolFlags, symbolExcludes);
        }
        return Symbol.None;
    }

    private Symbol DeclareClassMember(Node node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        var containerSymbol = b.SymbolOf(container);
        return Utilities.IsStatic(store, node)
            ? DeclareSymbol(b.Exports(containerSymbol), containerSymbol, node, symbolFlags, symbolExcludes)
            : DeclareSymbol(b.Members(containerSymbol), containerSymbol, node, symbolFlags, symbolExcludes);
    }

    private Symbol DeclareSourceFileMember(Node node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        return Utilities.IsExternalModule(store, file)
            ? DeclareModuleMember(node, symbolFlags, symbolExcludes)
            : DeclareSymbol(b.LocalsOf(file), Symbol.None, node, symbolFlags, symbolExcludes);
    }

    private bool HasExportDeclarations(Node node)
    {
        var body = store.Kind(node) == SyntaxKind.SourceFile ? node : store.Kind(store.Body(node)) == SyntaxKind.ModuleBlock ? store.Body(node) : Node.None;
        if (body.IsNone)
        {
            return false;
        }
        foreach (var s in store.Nodes(store.Statements(body)))
        {
            if (store.Kind(s) == SyntaxKind.ExportDeclaration || store.Kind(s) == SyntaxKind.ExportAssignment)
            {
                return true;
            }
        }
        return false;
    }

    private void SetExportContextFlag(Node node)
    {
        // A declaration source file or ambient module declaration that contains no export declarations (but possibly regular
        // declarations with export modifiers) is an export context in which declarations are implicitly exported.
        if ((store.Flags(node) & NodeFlags.Ambient) != 0 && !HasExportDeclarations(node))
        {
            store.SetFlags(node, store.Flags(node) | NodeFlags.ExportContext);
        }
        else
        {
            store.SetFlags(node, store.Flags(node) & ~NodeFlags.ExportContext);
        }
    }

    private void BindModuleDeclaration(Node node)
    {
        SetExportContextFlag(node);
        if (Utilities.IsAmbientModule(store, node))
        {
            if (Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Export))
            {
                ErrorOnFirstToken(node, Diagnostics.export_modifier_cannot_be_applied_to_ambient_modules_and_module_augmentations_since_they_are_always_visible);
            }
            if (Utilities.IsModuleAugmentationExternal(store, node))
            {
                DeclareModuleSymbol(node);
            }
            else
            {
                var name = store.Name(node);
                if (store.Kind(name) == SyntaxKind.StringLiteral)
                {
                    var text = store.Text(name);
                    if (!Utilities.IsValidPattern(texts.Span(text)))
                    {
                        ErrorOnFirstToken(name, Diagnostics.Pattern_0_can_have_at_most_one_Asterisk_character, texts.GetString(text));
                    }
                }

                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.ValueModule, SymbolFlags.ValueModuleExcludes);
                // (file.patternAmbientModules: not kept, nothing in Track O reads them.)
            }
        }
        else
        {
            var state = DeclareModuleSymbol(node);
            if (state != ModuleInstanceState.NonInstantiated)
            {
                var symbol = b.SymbolOf(node);
                // if module was already merged with some function, class or non-const enum, treat it as non-const-enum-only
                b.SetConstEnumOnlyModule(symbol, (b.Flags(symbol) & (SymbolFlags.Function | SymbolFlags.Class | SymbolFlags.RegularEnum)) == 0
                    // Current must be `const enum` only
                    && state == ModuleInstanceState.ConstEnumOnly
                    // Can't have been set to 'false' in a previous merged symbol. ('undefined' OK)
                    && b.ConstEnumOnlyModule(symbol) != false);
            }
        }
    }

    private ModuleInstanceState DeclareModuleSymbol(Node node)
    {
        var state = GetModuleInstanceState(node);
        var instantiated = state != ModuleInstanceState.NonInstantiated;
        DeclareSymbolAndAddToSymbolTable(
            node,
            instantiated ? SymbolFlags.ValueModule : SymbolFlags.NamespaceModule,
            instantiated ? SymbolFlags.ValueModuleExcludes : SymbolFlags.NamespaceModuleExcludes);
        return state;
    }

    /// <summary>binder.ts: getModuleInstanceState (a new visited map per call).</summary>
    private ModuleInstanceState GetModuleInstanceState(Node node)
    {
        moduleInstanceStates.Clear();
        return GetModuleInstanceStateOf(node);
    }

    private ModuleInstanceState GetModuleInstanceStateOf(Node node)
    {
        var body = store.Body(node);
        if (!body.IsNone && store.Parent(body).IsNone)
        {
            // getModuleInstanceStateForAliasTarget needs to walk up the parent chain, so parent pointers must be set on this tree already
            Utilities.SetParent(store, body, node);
            Utilities.SetParentRecursive(store, body, /*incremental*/ false);
        }
        return !body.IsNone ? GetModuleInstanceStateCached(body) : ModuleInstanceState.Instantiated;
    }

    private ModuleInstanceState GetModuleInstanceStateCached(Node node)
    {
        if (moduleInstanceStates.TryGetValue(node.Index, out var cached))
        {
            return cached ?? ModuleInstanceState.NonInstantiated;
        }
        moduleInstanceStates[node.Index] = null;
        var result = GetModuleInstanceStateWorker(node);
        moduleInstanceStates[node.Index] = result;
        return result;
    }

    private ModuleInstanceState GetModuleInstanceStateWorker(Node node)
    {
        // A module is uninstantiated if it contains only
        switch (store.Kind(node))
        {
            // 1. interface declarations, type alias declarations
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
                return ModuleInstanceState.NonInstantiated;
            // 2. const enum declarations
            case SyntaxKind.EnumDeclaration:
                if (Utilities.IsEnumConst(store, node))
                {
                    return ModuleInstanceState.ConstEnumOnly;
                }
                break;
            // 3. non-exported import declarations
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
                if (!Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Export))
                {
                    return ModuleInstanceState.NonInstantiated;
                }
                break;
            // 4. Export alias declarations pointing at only uninstantiated modules or things uninstantiated modules contain
            case SyntaxKind.ExportDeclaration:
                var exportClause = store.ExportClause(node);
                if (store.ModuleSpecifier(node).IsNone && !exportClause.IsNone && store.Kind(exportClause) == SyntaxKind.NamedExports)
                {
                    var state = ModuleInstanceState.NonInstantiated;
                    foreach (var specifier in store.Nodes(store.Elements(exportClause)))
                    {
                        var specifierState = GetModuleInstanceStateForAliasTarget(specifier);
                        if (specifierState > state)
                        {
                            state = specifierState;
                        }
                        if (state == ModuleInstanceState.Instantiated)
                        {
                            return state;
                        }
                    }
                    return state;
                }
                break;
            // 5. other uninstantiated module declarations.
            case SyntaxKind.ModuleBlock:
            {
                var state = ModuleInstanceState.NonInstantiated;
                foreach (var n in store.Nodes(store.Statements(node)))
                {
                    var childState = GetModuleInstanceStateCached(n);
                    if (childState == ModuleInstanceState.NonInstantiated)
                    {
                        // child is non-instantiated - continue searching
                        continue;
                    }
                    if (childState == ModuleInstanceState.ConstEnumOnly)
                    {
                        // child is const enum only - record state and continue searching
                        state = ModuleInstanceState.ConstEnumOnly;
                        continue;
                    }
                    // child is instantiated - record state and stop
                    state = ModuleInstanceState.Instantiated;
                    break;
                }
                return state;
            }
            case SyntaxKind.ModuleDeclaration:
                return GetModuleInstanceStateOf(node);
            case SyntaxKind.Identifier:
                // Only jsdoc typedef definition can exist in jsdoc namespace, and it should
                // be considered the same as type alias
                if ((store.Flags(node) & NodeFlags.IdentifierIsInJSDocNamespace) != 0)
                {
                    return ModuleInstanceState.NonInstantiated;
                }
                break;
        }
        return ModuleInstanceState.Instantiated;
    }

    private ModuleInstanceState GetModuleInstanceStateForAliasTarget(Node specifier)
    {
        var name = !store.PropertyName(specifier).IsNone ? store.PropertyName(specifier) : store.Name(specifier);
        if (store.Kind(name) != SyntaxKind.Identifier)
        {
            return ModuleInstanceState.Instantiated; // Skip for invalid syntax like this: export { "x" }
        }
        for (var p = store.Parent(specifier); !p.IsNone; p = store.Parent(p))
        {
            var kind = store.Kind(p);
            if (kind == SyntaxKind.Block || kind == SyntaxKind.ModuleBlock || kind == SyntaxKind.SourceFile)
            {
                ModuleInstanceState? found = null;
                foreach (var statement in store.Nodes(store.Statements(p)))
                {
                    if (Utilities.NodeHasName(store, statement, name))
                    {
                        if (store.Parent(statement).IsNone)
                        {
                            Utilities.SetParent(store, statement, p);
                            Utilities.SetParentRecursive(store, statement, /*incremental*/ false);
                        }
                        var state = GetModuleInstanceStateCached(statement);
                        if (found is null || state > found)
                        {
                            found = state;
                        }
                        if (found == ModuleInstanceState.Instantiated)
                        {
                            return ModuleInstanceState.Instantiated;
                        }
                        if (store.Kind(statement) == SyntaxKind.ImportEqualsDeclaration)
                        {
                            // Treat re-exports of import aliases as instantiated,
                            // since they're ambiguous. This is consistent with
                            // `export import x = mod.x` being treated as instantiated:
                            //   import x = mod.x;
                            //   export { x };
                            found = ModuleInstanceState.Instantiated;
                        }
                    }
                }
                if (found is { } value)
                {
                    return value;
                }
            }
        }
        return ModuleInstanceState.Instantiated; // Couldn't locate, assume could refer to a value
    }

    private void BindFunctionOrConstructorType(Node node)
    {
        // For a given function symbol "<...>(...) => T" we want to generate a symbol identical
        // to the one we would get for: { <...>(...): T }
        //
        // We do that by making an anonymous type literal symbol, and then setting the function
        // symbol as its sole member. To the rest of the system, this symbol will be indistinguishable
        // from an actual type literal symbol you would have gotten had you used the long form.
        var symbol = CreateSymbol(SymbolFlags.Signature, GetDeclarationName(node));
        AddDeclarationToSymbol(symbol, node, SymbolFlags.Signature);

        var typeLiteralSymbol = CreateSymbol(SymbolFlags.TypeLiteral, Constant(ref nameType, InternalSymbolName.Type));
        AddDeclarationToSymbol(typeLiteralSymbol, node, SymbolFlags.TypeLiteral);
        b.SetMembers(typeLiteralSymbol, b.CreateTable());
        b.Set(b.Members(typeLiteralSymbol), b.Name(symbol), symbol);
    }

    private Symbol BindAnonymousDeclaration(Node node, SymbolFlags symbolFlags, TextRef name)
    {
        var symbol = CreateSymbol(symbolFlags, name);
        if ((symbolFlags & (SymbolFlags.EnumMember | SymbolFlags.ClassMember)) != 0)
        {
            b.SetParent(symbol, b.SymbolOf(container));
        }
        AddDeclarationToSymbol(symbol, node, symbolFlags);
        return symbol;
    }

    private void BindBlockScopedDeclaration(Node node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        switch (store.Kind(blockScopeContainer))
        {
            case SyntaxKind.ModuleDeclaration:
                DeclareModuleMember(node, symbolFlags, symbolExcludes);
                return;
            case SyntaxKind.SourceFile:
                if (IsExternalOrCommonJsModule(container))
                {
                    DeclareModuleMember(node, symbolFlags, symbolExcludes);
                    return;
                }
                break;
        }
        Debug.Assert(Utilities.CanHaveLocals(store, blockScopeContainer));
        if (b.LocalsOf(blockScopeContainer).IsNone)
        {
            b.SetLocalsOf(blockScopeContainer, b.CreateTable());
            AddToContainerChain(blockScopeContainer);
        }
        DeclareSymbol(b.LocalsOf(blockScopeContainer), Symbol.None, node, symbolFlags, symbolExcludes);
    }

    /// <summary>utilities.ts: isExternalOrCommonJsModule (of a source file; false for any other node).</summary>
    private bool IsExternalOrCommonJsModule(Node sourceFile)
    {
        return store.Kind(sourceFile) == SyntaxKind.SourceFile
            && (store.ExternalModuleIndicator(sourceFile) is not null || (sourceFile == file && !commonJsModuleIndicator.IsNone));
    }

    private void BindJSDocImports()
    {
        if (jsDocImports is null)
        {
            return;
        }

        var saveContainer = container;
        var saveLastContainer = lastContainer;
        var saveBlockScopeContainer = blockScopeContainer;
        var saveParent = parent;
        var saveCurrentFlow = currentFlow;

        foreach (var jsDocImportTag in jsDocImports)
        {
            var host = Utilities.GetJSDocHost(store, jsDocImportTag);
            var enclosingContainer = !host.IsNone ? Utilities.GetEnclosingContainer(store, host) : Node.None;
            var enclosingBlockScopeContainer = !host.IsNone ? Utilities.GetEnclosingBlockScopeContainer(store, host) : Node.None;
            container = !enclosingContainer.IsNone ? enclosingContainer : file;
            blockScopeContainer = !enclosingBlockScopeContainer.IsNone ? enclosingBlockScopeContainer : file;
            currentFlow = b.CreateFlowNode(FlowFlags.Start, Node.None, FlowNode.None);
            parent = jsDocImportTag;
            Bind(store.ImportClause(jsDocImportTag));
        }

        container = saveContainer;
        lastContainer = saveLastContainer;
        blockScopeContainer = saveBlockScopeContainer;
        parent = saveParent;
        currentFlow = saveCurrentFlow;
    }

    private void ErrorOnFirstToken(Node node, DiagnosticMessage message, params object?[] args)
    {
        var span = Utilities.GetSpanOfTokenAtPosition(store, file, store.Pos(node));
        b.BindDiagnostics.Add(Utilities.CreateFileDiagnostic(store, file, span.Start, span.Length, message, args));
    }

    private void Bind(Node node)
    {
        if (node.IsNone)
        {
            return;
        }
        SetParentOf(node, parent);
        if (store.Kind(node) <= SyntaxKind.LastToken)
        {
            BindToken(node);
            return;
        }
        var saveInStrictMode = inStrictMode;

        // Even though in the AST the jsdoc @typedef node belongs to the current node,
        // its symbol might be in the same scope with the current node's symbol. Consider:
        //
        //     /** @typedef {string | number} MyType */
        //     function foo();
        //
        // Here the current node is "foo", which is a container, but the scope of "MyType" should
        // not be inside "foo". Therefore we always bind @typedef before bind the parent node,
        // and skip binding this tag later when binding all the other jsdoc tags.

        // First we bind declaration nodes to a symbol if possible. We'll both create a symbol
        // and then potentially add the symbol to an appropriate symbol table. Possible
        // destination symbol tables are:
        //
        //  1) The 'exports' table of the current container's symbol.
        //  2) The 'members' table of the current container's symbol.
        //  3) The 'locals' table of the current container.
        //
        // However, not all symbols will end up in any of these tables. 'Anonymous' symbols
        // (like TypeLiterals for example) will not be put in any table.
        BindWorker(node);
        // Then we recurse into the children of the node to bind them as well. For certain
        // symbols we do specialized work when we recurse. For example, we'll keep track of
        // the current 'container' node when it changes. This helps us know which symbol table
        // a local should go into for example. Since terminal nodes are known not to have
        // children, as an optimization we don't process those.
        var saveParent = parent;
        parent = node;
        var containerFlags = GetContainerFlags(store, node);
        if (containerFlags == ContainerFlags.None)
        {
            BindChildren(node);
        }
        else
        {
            BindContainer(node, containerFlags);
        }
        parent = saveParent;
        inStrictMode = saveInStrictMode;
    }

    // Bind for a token (Track O, step BO3: split from Bind, the most frequent nodes). Of the tokens, bindWorker acts
    // only on identifiers, private identifiers, `this` and `super`, and none of its token cases changes inStrictMode
    // (nested binds restore it themselves), so it needs no saving here.
    private void BindToken(Node node)
    {
        var kind = store.Kind(node);
        if (kind == SyntaxKind.Identifier && (store.Flags(node) & NodeFlags.IdentifierIsInJSDocNamespace) == 0)
        {
            // bindWorker's case for identifiers outside JSDoc namespaces.
            if (!currentFlow.IsNone)
            {
                b.SetFlowNodeOf(node, currentFlow);
            }
            CheckContextualIdentifier(node);
        }
        else if (kind is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier or SyntaxKind.ThisKeyword or SyntaxKind.SuperKeyword)
        {
            BindWorker(node);
        }
        if (kind == SyntaxKind.EndOfFileToken)
        {
            var saveParent = parent;
            parent = node;
            BindJSDoc(node);
            parent = saveParent;
            return;
        }
        BindJSDoc(node);
    }

    // setParent(node, parent). (Track O, step BO2: written without reading the old parent first; writing the same
    // parent again changes nothing.)
    private void SetParentOf(Node node, Node newParent)
    {
        if (!newParent.IsNone)
        {
            store.SetParent(node, newParent);
        }
    }

    private void BindJSDoc(Node node)
    {
        // hasJSDocNodes(node), with the node's jsDoc read first (Track O, step BO2: a node without side-table entries
        // has none, which one bit of its record tells; canHaveJSDoc is a switch over the kinds).
        var jsDoc = store.JsDoc(node);
        if (jsDoc is not null && jsDoc.Count > 0 && Utilities.CanHaveJSDoc(store, node))
        {
            if (Utilities.IsInJSFile(store, node))
            {
                for (var i = 0; i < jsDoc.Count; i++)
                {
                    Bind(jsDoc[i]);
                }
            }
            else
            {
                for (var i = 0; i < jsDoc.Count; i++)
                {
                    Utilities.SetParent(store, jsDoc[i], node);
                    Utilities.SetParentRecursive(store, jsDoc[i], /*incremental*/ false);
                }
            }
        }
    }

    private void UpdateStrictModeStatementList(NodeList statements)
    {
        if (!inStrictMode)
        {
            foreach (var statement in store.Nodes(statements))
            {
                if (!Utilities.IsPrologueDirective(store, statement))
                {
                    return;
                }

                if (IsUseStrictPrologueDirective(statement))
                {
                    inStrictMode = true;
                    return;
                }
            }
        }
    }

    /// Should be called only on prologue directives (isPrologueDirective(node) should be true)
    private bool IsUseStrictPrologueDirective(Node node)
    {
        var nodeText = Utilities.GetSourceTextOfNodeFromSourceFile(store, file, store.Expression(node));

        // Note: the node text must be exactly "use strict" or 'use strict'.  It is not ok for the
        // string to contain unicode escapes (as per ES5).
        return nodeText == "\"use strict\"" || nodeText == "'use strict'";
    }

    private void BindWorker(Node node)
    {
        switch (store.Kind(node))
        {
            /* Strict mode checks */
            case SyntaxKind.Identifier:
                // for typedef type names with namespaces, bind the new jsdoc type symbol here
                // because it requires all containing namespaces to be in effect, namely the
                // current "blockScopeContainer" needs to be set to its immediate namespace parent.
                if ((store.Flags(node) & NodeFlags.IdentifierIsInJSDocNamespace) != 0)
                {
                    var parentNode = store.Parent(node);
                    while (!parentNode.IsNone && !Utilities.IsJSDocTypeAlias(store, parentNode))
                    {
                        parentNode = store.Parent(parentNode);
                    }
                    BindBlockScopedDeclaration(parentNode, SymbolFlags.TypeAlias, SymbolFlags.TypeAliasExcludes);
                    break;
                }
                goto case SyntaxKind.ThisKeyword;
            case SyntaxKind.ThisKeyword:
                if (store.Kind(node) == SyntaxKind.ThisKeyword)
                {
                    seenThisKeyword = true;
                }
                // TODO: Why use `isExpression` here? both Identifier and ThisKeyword are expressions.
                // (Track O, step BO2: isExpression is true for both kinds, primary expressions, so only currentFlow is tested.)
                if (!currentFlow.IsNone)
                {
                    b.SetFlowNodeOf(node, currentFlow);
                }
                // TODO: a `ThisExpression` is not an Identifier, this cast is unsound
                CheckContextualIdentifier(node);
                return;
            case SyntaxKind.QualifiedName:
                if (!currentFlow.IsNone && Utilities.IsPartOfTypeQuery(store, node))
                {
                    b.SetFlowNodeOf(node, currentFlow);
                }
                break;
            case SyntaxKind.MetaProperty:
            case SyntaxKind.SuperKeyword:
                b.SetFlowNodeOf(node, currentFlow);
                break;
            case SyntaxKind.PrivateIdentifier:
                CheckPrivateIdentifier(node);
                return;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                if (!currentFlow.IsNone && IsNarrowableReference(node))
                {
                    b.SetFlowNodeOf(node, currentFlow);
                }
                if (Utilities.IsSpecialPropertyDeclaration(store, node))
                {
                    BindSpecialPropertyDeclaration(node);
                }
                if (
                    Utilities.IsInJSFile(store, node) &&
                    !commonJsModuleIndicator.IsNone &&
                    Utilities.IsModuleExportsAccessExpression(store, node) &&
                    LookupSymbolForName(blockScopeContainer, NameModule).IsNone)
                {
                    DeclareSymbol(b.LocalsOf(file), Symbol.None, store.Expression(node), SymbolFlags.FunctionScopedVariable | SymbolFlags.ModuleExports, SymbolFlags.FunctionScopedVariableExcludes);
                }
                break;
            case SyntaxKind.BinaryExpression:
                var specialKind = Utilities.GetAssignmentDeclarationKind(store, node);
                switch (specialKind)
                {
                    case AssignmentDeclarationKind.ExportsProperty:
                        BindExportsPropertyAssignment(node);
                        break;
                    case AssignmentDeclarationKind.ModuleExports:
                        BindModuleExportsAssignment(node);
                        break;
                    case AssignmentDeclarationKind.PrototypeProperty:
                        BindPrototypePropertyAssignment(store.Left(node), node);
                        break;
                    case AssignmentDeclarationKind.Prototype:
                        BindPrototypeAssignment(node);
                        break;
                    case AssignmentDeclarationKind.ThisProperty:
                        BindThisPropertyAssignment(node);
                        break;
                    case AssignmentDeclarationKind.Property:
                        var expression = store.Expression(store.Left(node));
                        if (Utilities.IsInJSFile(store, node) && store.Kind(expression) == SyntaxKind.Identifier)
                        {
                            var symbol = LookupSymbolForName(blockScopeContainer, store.EscapedText(expression));
                            if (IsThisInitializedDeclaration(symbol.IsNone ? Node.None : b.ValueDeclaration(symbol)))
                            {
                                BindThisPropertyAssignment(node);
                                break;
                            }
                        }
                        BindSpecialPropertyAssignment(node);
                        break;
                    case AssignmentDeclarationKind.None:
                        // Nothing to do
                        break;
                    default:
                        Debug.Fail("Unknown binary expression special property assignment kind");
                        break;
                }
                CheckStrictModeBinaryExpression(node);
                return;
            case SyntaxKind.CatchClause:
                CheckStrictModeCatchClause(node);
                return;
            case SyntaxKind.DeleteExpression:
                CheckStrictModeDeleteExpression(node);
                return;
            case SyntaxKind.PostfixUnaryExpression:
                CheckStrictModePostfixUnaryExpression(node);
                return;
            case SyntaxKind.PrefixUnaryExpression:
                CheckStrictModePrefixUnaryExpression(node);
                return;
            case SyntaxKind.WithStatement:
                CheckStrictModeWithStatement(node);
                return;
            case SyntaxKind.LabeledStatement:
                CheckStrictModeLabeledStatement(node);
                return;
            case SyntaxKind.ThisType:
                seenThisKeyword = true;
                return;
            case SyntaxKind.TypePredicate:
                break; // Binding the children will handle everything
            case SyntaxKind.TypeParameter:
                BindTypeParameter(node);
                return;
            case SyntaxKind.Parameter:
                BindParameter(node);
                return;
            case SyntaxKind.VariableDeclaration:
                BindVariableDeclarationOrBindingElement(node);
                return;
            case SyntaxKind.BindingElement:
                b.SetFlowNodeOf(node, currentFlow);
                BindVariableDeclarationOrBindingElement(node);
                return;
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
                BindPropertyWorker(node);
                return;
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.ShorthandPropertyAssignment:
                BindPropertyOrMethodOrAccessor(node, SymbolFlags.Property, SymbolFlags.PropertyExcludes);
                return;
            case SyntaxKind.EnumMember:
                BindPropertyOrMethodOrAccessor(node, SymbolFlags.EnumMember, SymbolFlags.EnumMemberExcludes);
                return;

            case SyntaxKind.CallSignature:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.IndexSignature:
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Signature, SymbolFlags.None);
                return;
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
                // If this is an ObjectLiteralExpression method, then it sits in the same space
                // as other properties in the object literal.  So we use SymbolFlags.PropertyExcludes
                // so that it will conflict with any other object literal members with the same
                // name.
                BindPropertyOrMethodOrAccessor(node, SymbolFlags.Method | (!store.QuestionToken(node).IsNone ? SymbolFlags.Optional : SymbolFlags.None), Utilities.IsObjectLiteralMethod(store, node) ? SymbolFlags.PropertyExcludes : SymbolFlags.MethodExcludes);
                return;
            case SyntaxKind.FunctionDeclaration:
                BindFunctionDeclaration(node);
                return;
            case SyntaxKind.Constructor:
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Constructor, /*symbolExcludes:*/ SymbolFlags.None);
                return;
            case SyntaxKind.GetAccessor:
                BindPropertyOrMethodOrAccessor(node, SymbolFlags.GetAccessor, SymbolFlags.GetAccessorExcludes);
                return;
            case SyntaxKind.SetAccessor:
                BindPropertyOrMethodOrAccessor(node, SymbolFlags.SetAccessor, SymbolFlags.SetAccessorExcludes);
                return;
            case SyntaxKind.FunctionType:
            case SyntaxKind.JSDocFunctionType:
            case SyntaxKind.JSDocSignature:
            case SyntaxKind.ConstructorType:
                BindFunctionOrConstructorType(node);
                return;
            case SyntaxKind.TypeLiteral:
            case SyntaxKind.JSDocTypeLiteral:
            case SyntaxKind.MappedType:
                BindAnonymousDeclaration(node, SymbolFlags.TypeLiteral, Constant(ref nameType, InternalSymbolName.Type));
                return;
            case SyntaxKind.JSDocClassTag:
                BindJSDocClassTag(node);
                return;
            case SyntaxKind.ObjectLiteralExpression:
                BindAnonymousDeclaration(node, SymbolFlags.ObjectLiteral, Constant(ref nameObject, InternalSymbolName.Object));
                return;
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
                BindFunctionExpression(node);
                return;

            case SyntaxKind.CallExpression:
                var assignmentKind = Utilities.GetAssignmentDeclarationKind(store, node);
                switch (assignmentKind)
                {
                    case AssignmentDeclarationKind.ObjectDefinePropertyValue:
                        BindObjectDefinePropertyAssignment(node);
                        return;
                    case AssignmentDeclarationKind.ObjectDefinePropertyExports:
                        BindObjectDefinePropertyExport(node);
                        return;
                    case AssignmentDeclarationKind.ObjectDefinePrototypeProperty:
                        BindObjectDefinePrototypeProperty(node);
                        return;
                    case AssignmentDeclarationKind.None:
                        break; // Nothing to do
                    default:
                        Debug.Fail("Unknown call expression assignment declaration kind");
                        return;
                }
                if (Utilities.IsInJSFile(store, node))
                {
                    BindCallExpression(node);
                }
                break;

            // Members of classes, interfaces, and modules
            case SyntaxKind.ClassExpression:
            case SyntaxKind.ClassDeclaration:
                // All classes are automatically in strict mode in ES6.
                inStrictMode = true;
                BindClassLikeDeclaration(node);
                return;
            case SyntaxKind.InterfaceDeclaration:
                BindBlockScopedDeclaration(node, SymbolFlags.Interface, SymbolFlags.InterfaceExcludes);
                return;
            case SyntaxKind.TypeAliasDeclaration:
                BindBlockScopedDeclaration(node, SymbolFlags.TypeAlias, SymbolFlags.TypeAliasExcludes);
                return;
            case SyntaxKind.EnumDeclaration:
                BindEnumDeclaration(node);
                return;
            case SyntaxKind.ModuleDeclaration:
                BindModuleDeclaration(node);
                return;
            // Jsx-attributes
            case SyntaxKind.JsxAttributes:
                BindAnonymousDeclaration(node, SymbolFlags.ObjectLiteral, Constant(ref nameJSXAttributes, InternalSymbolName.JSXAttributes));
                return;
            case SyntaxKind.JsxAttribute:
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Property, SymbolFlags.PropertyExcludes);
                return;

            // Imports and exports
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.NamespaceImport:
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.ExportSpecifier:
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
                return;
            case SyntaxKind.NamespaceExportDeclaration:
                BindNamespaceExportDeclaration(node);
                return;
            case SyntaxKind.ImportClause:
                BindImportClause(node);
                return;
            case SyntaxKind.ExportDeclaration:
                BindExportDeclaration(node);
                return;
            case SyntaxKind.ExportAssignment:
                BindExportAssignment(node);
                return;
            case SyntaxKind.SourceFile:
                UpdateStrictModeStatementList(store.Statements(node));
                BindSourceFileIfExternalModule();
                return;
            case SyntaxKind.Block:
                if (!Utilities.IsFunctionLikeOrClassStaticBlockDeclaration(store, store.Parent(node)))
                {
                    return;
                }
                UpdateStrictModeStatementList(store.Statements(node));
                return;
            case SyntaxKind.ModuleBlock:
                UpdateStrictModeStatementList(store.Statements(node));
                return;

            case SyntaxKind.JSDocParameterTag:
                if (store.Kind(store.Parent(node)) == SyntaxKind.JSDocSignature)
                {
                    BindParameter(node);
                    return;
                }
                if (store.Kind(store.Parent(node)) != SyntaxKind.JSDocTypeLiteral)
                {
                    break;
                }
                goto case SyntaxKind.JSDocPropertyTag;
            case SyntaxKind.JSDocPropertyTag:
                var typeExpression = store.TypeExpression(node);
                var flags = store.IsBracketed(node) || !typeExpression.IsNone && store.Kind(store.Type(typeExpression)) == SyntaxKind.JSDocOptionalType ?
                    SymbolFlags.Property | SymbolFlags.Optional :
                    SymbolFlags.Property;
                DeclareSymbolAndAddToSymbolTable(node, flags, SymbolFlags.PropertyExcludes);
                return;
            case SyntaxKind.JSDocTypedefTag:
            case SyntaxKind.JSDocCallbackTag:
            case SyntaxKind.JSDocEnumTag:
                (delayedTypeAliases ??= []).Add(node);
                return;
            case SyntaxKind.JSDocOverloadTag:
                Bind(store.TypeExpression(node));
                return;
            case SyntaxKind.JSDocImportTag:
                (jsDocImports ??= []).Add(node);
                return;
        }
    }

    private void BindPropertyWorker(Node node)
    {
        var isAutoAccessor = Utilities.IsAutoAccessorPropertyDeclaration(store, node);
        var includes = isAutoAccessor ? SymbolFlags.Accessor : SymbolFlags.Property;
        var excludes = isAutoAccessor ? SymbolFlags.AccessorExcludes : SymbolFlags.PropertyExcludes;
        BindPropertyOrMethodOrAccessor(node, includes | (!store.QuestionToken(node).IsNone ? SymbolFlags.Optional : SymbolFlags.None), excludes);
    }

    private void BindSourceFileIfExternalModule()
    {
        SetExportContextFlag(file);
        if (Utilities.IsExternalModule(store, file))
        {
            BindSourceFileAsExternalModule();
        }
        else if (store.ScriptKind(file) == ScriptKind.JSON)
        {
            BindSourceFileAsExternalModule();
            // Create symbol equivalent for the module.exports = {}
            var originalSymbol = b.SymbolOf(file);
            DeclareSymbol(b.Exports(originalSymbol), originalSymbol, file, SymbolFlags.Property, SymbolFlags.All);
            b.SetSymbolOf(file, originalSymbol);
        }
    }

    private void BindSourceFileAsExternalModule()
    {
        // `"\"" + removeFileExtension(file.fileName) + "\""`, written into the file's text arena directly (step BO4: no
        // strings per file).
        var fileName = store.FileName(file);
        var length = Utilities.RemoveFileExtensionLength(fileName) + 2;
        var at = texts.Reserve(length);
        var name = texts.ArenaSpan(at, length);
        name[0] = '"';
        fileName.AsSpan(0, length - 2).CopyTo(name[1..]);
        name[length - 1] = '"';
        texts.SetCharCount(at + length);
        BindAnonymousDeclaration(file, SymbolFlags.ValueModule, TextRef.Arena(at, length));
    }

    private void BindExportAssignment(Node node)
    {
        var containerSymbol = b.SymbolOf(container);
        if (containerSymbol.IsNone || b.Exports(containerSymbol).IsNone)
        {
            // Incorrect export assignment in some sort of block construct
            BindAnonymousDeclaration(node, SymbolFlags.Value, GetDeclarationName(node));
        }
        else
        {
            var flags = Utilities.ExportAssignmentIsAlias(store, node)
                // An export default clause with an EntityNameExpression or a class expression exports all meanings of that identifier or expression;
                ? SymbolFlags.Alias
                // An export default clause with any other expression exports a value
                : SymbolFlags.Property;
            // If there is an `export default x;` alias declaration, can't `export default` anything else.
            // (In contrast, you can still have `export default function f() {}` and `export default interface I {}`.)
            var symbol = DeclareSymbol(b.Exports(containerSymbol), containerSymbol, node, flags, SymbolFlags.All);

            if (store.IsExportEquals(node))
            {
                // Will be an error later, since the module already has other exports. Just make sure this has a valueDeclaration set.
                SetValueDeclaration(symbol, node);
            }
        }
    }

    private void BindNamespaceExportDeclaration(Node node)
    {
        var modifiers = store.Modifiers(node);
        if (!modifiers.IsNone && store.Length(modifiers) > 0)
        {
            b.BindDiagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.Modifiers_cannot_appear_here));
        }
        var nodeParent = store.Parent(node);
        var diag = store.Kind(nodeParent) != SyntaxKind.SourceFile ? Diagnostics.Global_module_exports_may_only_appear_at_top_level
            : !Utilities.IsExternalModule(store, nodeParent) ? Diagnostics.Global_module_exports_may_only_appear_in_module_files
            : !store.IsDeclarationFile(nodeParent) ? Diagnostics.Global_module_exports_may_only_appear_in_declaration_files
            : null;
        if (diag is not null)
        {
            b.BindDiagnostics.Add(CreateDiagnosticForNode(node, diag));
        }
        else
        {
            var fileSymbol = b.SymbolOf(file);
            var globalExports = b.GlobalExports(fileSymbol);
            if (globalExports.IsNone)
            {
                globalExports = b.CreateTable();
                b.SetGlobalExports(fileSymbol, globalExports);
            }
            DeclareSymbol(globalExports, fileSymbol, node, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
        }
    }

    private void BindExportDeclaration(Node node)
    {
        var containerSymbol = b.SymbolOf(container);
        var exportClause = store.ExportClause(node);
        if (containerSymbol.IsNone || b.Exports(containerSymbol).IsNone)
        {
            // Export * in some sort of block construct
            BindAnonymousDeclaration(node, SymbolFlags.ExportStar, GetDeclarationName(node));
        }
        else if (exportClause.IsNone)
        {
            // All export * declarations are collected in an __export symbol
            DeclareSymbol(b.Exports(containerSymbol), containerSymbol, node, SymbolFlags.ExportStar, SymbolFlags.None);
        }
        else if (store.Kind(exportClause) == SyntaxKind.NamespaceExport)
        {
            // declareSymbol walks up parents to find name text, parent _must_ be set
            // but won't be set by the normal binder walk until `bindChildren` later on.
            Utilities.SetParent(store, exportClause, node);
            DeclareSymbol(b.Exports(containerSymbol), containerSymbol, exportClause, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
        }
    }

    private void BindImportClause(Node node)
    {
        if (!store.Name(node).IsNone)
        {
            DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
        }
    }

    private void BindJSDocClassTag(Node node)
    {
        BindEachChild(node);
        var host = Utilities.GetHostSignatureFromJSDoc(store, node);
        if (!host.IsNone && store.Kind(host) != SyntaxKind.MethodDeclaration)
        {
            AddDeclarationToSymbol(b.SymbolOf(host), host, SymbolFlags.Class);
        }
    }

    private void BindClassLikeDeclaration(Node node)
    {
        var name = store.Name(node);
        if (store.Kind(node) == SyntaxKind.ClassDeclaration)
        {
            BindBlockScopedDeclaration(node, SymbolFlags.Class, SymbolFlags.ClassExcludes);
        }
        else
        {
            var bindingName = !name.IsNone ? store.EscapedText(name) : Constant(ref nameClass, InternalSymbolName.Class);
            BindAnonymousDeclaration(node, SymbolFlags.Class, bindingName);
            // (Add name of class expression into the map for semantic classifier: classifiableNames are not kept.)
        }

        var symbol = b.SymbolOf(node);

        // TypeScript 1.0 spec (April 2014): 8.4
        // Every class automatically contains a static property member named 'prototype', the
        // type of which is an instantiation of the class type with type Any supplied as a type
        // argument for each type parameter. It is an error to explicitly declare a static
        // property member with the name 'prototype'.
        //
        // Note: we check for this here because this class may be merging into a module.  The
        // module might have an exported variable called 'prototype'.  We can't allow that as
        // that would clash with the built-in 'prototype' for the class.
        var prototypeSymbol = CreateSymbol(SymbolFlags.Property | SymbolFlags.Prototype, Constant(ref namePrototype, "prototype"));
        var exports = b.Exports(symbol);
        var symbolExport = b.Get(exports, b.Name(prototypeSymbol));
        if (!symbolExport.IsNone)
        {
            if (!name.IsNone)
            {
                Utilities.SetParent(store, name, node);
            }
            foreach (var first in b.Declarations(symbolExport))
            {
                b.BindDiagnostics.Add(CreateDiagnosticForNode(first, Diagnostics.Duplicate_identifier_0, "prototype"));
                break;
            }
        }
        b.Set(exports, b.Name(prototypeSymbol), prototypeSymbol);
        b.SetParent(prototypeSymbol, symbol);
    }

    private void BindEnumDeclaration(Node node)
    {
        if (Utilities.IsEnumConst(store, node))
        {
            BindBlockScopedDeclaration(node, SymbolFlags.ConstEnum, SymbolFlags.ConstEnumExcludes);
        }
        else
        {
            BindBlockScopedDeclaration(node, SymbolFlags.RegularEnum, SymbolFlags.RegularEnumExcludes);
        }
    }

    private void BindVariableDeclarationOrBindingElement(Node node)
    {
        var name = store.Name(node);
        if (inStrictMode)
        {
            CheckStrictModeEvalOrArguments(node, name);
        }

        if (!UtilitiesPublic.IsBindingPattern(store, name))
        {
            var possibleVariableDecl = store.Kind(node) == SyntaxKind.VariableDeclaration ? node : store.Parent(store.Parent(node));
            if (
                Utilities.IsInJSFile(store, node) &&
                IsVariableDeclarationInitializedToBareOrAccessedRequire(possibleVariableDecl) &&
                UtilitiesPublic.GetJSDocTypeTag(store, node).IsNone &&
                (Utilities.GetCombinedModifierFlags(store, node) & ModifierFlags.Export) == 0)
            {
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
            }
            else if (Utilities.IsBlockOrCatchScoped(store, node))
            {
                BindBlockScopedDeclaration(node, SymbolFlags.BlockScopedVariable, SymbolFlags.BlockScopedVariableExcludes);
            }
            else if (Utilities.IsPartOfParameterDeclaration(store, node))
            {
                // It is safe to walk up parent chain to find whether the node is a destructuring parameter declaration
                // because its parent chain has already been set up, since parents are set before descending into children.
                //
                // If node is a binding element in parameter declaration, we need to use ParameterExcludes.
                // Using ParameterExcludes flag allows the compiler to report an error on duplicate identifiers in Parameter Declaration
                // For example:
                //      function foo([a,a]) {} // Duplicate Identifier error
                //      function bar(a,a) {}   // Duplicate Identifier error, parameter declaration in this case is handled in bindParameter
                //                             // which correctly set excluded symbols
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.FunctionScopedVariable, SymbolFlags.ParameterExcludes);
            }
            else
            {
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.FunctionScopedVariable, SymbolFlags.FunctionScopedVariableExcludes);
            }
        }
    }

    private void BindParameter(Node node)
    {
        if (store.Kind(node) == SyntaxKind.JSDocParameterTag && store.Kind(container) != SyntaxKind.JSDocSignature)
        {
            return;
        }
        var name = store.Name(node);
        if (inStrictMode && (store.Flags(node) & NodeFlags.Ambient) == 0)
        {
            // It is a SyntaxError if the identifier eval or arguments appears within a FormalParameterList of a
            // strict mode FunctionLikeDeclaration or FunctionExpression(13.1)
            CheckStrictModeEvalOrArguments(node, name);
        }

        if (UtilitiesPublic.IsBindingPattern(store, name))
        {
            var index = IndexOf(store.Parameters(store.Parent(node)), node);
            BindAnonymousDeclaration(node, SymbolFlags.FunctionScopedVariable, texts.Add("__" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        else
        {
            DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.FunctionScopedVariable, SymbolFlags.ParameterExcludes);
        }

        // If this is a property-parameter, then also declare the property symbol into the
        // containing class.
        if (Utilities.IsParameterPropertyDeclaration(store, node, store.Parent(node)))
        {
            var classDeclaration = store.Parent(store.Parent(node));
            var classSymbol = b.SymbolOf(classDeclaration);
            DeclareSymbol(b.Members(classSymbol), classSymbol, node, SymbolFlags.Property | (!store.QuestionToken(node).IsNone ? SymbolFlags.Optional : SymbolFlags.None), SymbolFlags.PropertyExcludes);
        }
    }

    private void BindFunctionDeclaration(Node node)
    {
        if (!store.IsDeclarationFile(file) && (store.Flags(node) & NodeFlags.Ambient) == 0)
        {
            if (Utilities.IsAsyncFunction(store, node))
            {
                emitFlags |= NodeFlags.HasAsyncFunctions;
            }
        }

        CheckStrictModeFunctionName(node);
        if (inStrictMode)
        {
            CheckStrictModeFunctionDeclaration(node);
            BindBlockScopedDeclaration(node, SymbolFlags.Function, SymbolFlags.FunctionExcludes);
        }
        else
        {
            DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Function, SymbolFlags.FunctionExcludes);
        }
    }

    private void BindFunctionExpression(Node node)
    {
        if (!store.IsDeclarationFile(file) && (store.Flags(node) & NodeFlags.Ambient) == 0)
        {
            if (Utilities.IsAsyncFunction(store, node))
            {
                emitFlags |= NodeFlags.HasAsyncFunctions;
            }
        }
        if (!currentFlow.IsNone)
        {
            b.SetFlowNodeOf(node, currentFlow);
        }
        CheckStrictModeFunctionName(node);
        var name = store.Name(node);
        var bindingName = !name.IsNone ? store.EscapedText(name) : Constant(ref nameFunction, InternalSymbolName.Function);
        BindAnonymousDeclaration(node, SymbolFlags.Function, bindingName);
    }

    private void BindPropertyOrMethodOrAccessor(Node node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        if (!store.IsDeclarationFile(file) && (store.Flags(node) & NodeFlags.Ambient) == 0 && Utilities.IsAsyncFunction(store, node))
        {
            emitFlags |= NodeFlags.HasAsyncFunctions;
        }

        if (!currentFlow.IsNone && Utilities.IsObjectLiteralOrClassExpressionMethodOrAccessor(store, node))
        {
            b.SetFlowNodeOf(node, currentFlow);
        }

        if (Utilities.HasDynamicName(store, node))
        {
            BindAnonymousDeclaration(node, symbolFlags, Constant(ref nameComputed, InternalSymbolName.Computed));
        }
        else
        {
            DeclareSymbolAndAddToSymbolTable(node, symbolFlags, symbolExcludes);
        }
    }

    private Node GetInferTypeContainer(Node node)
    {
        for (var n = node; !n.IsNone; n = store.Parent(n))
        {
            var p = store.Parent(n);
            if (!p.IsNone && store.Kind(p) == SyntaxKind.ConditionalType && store.ExtendsType(p) == n)
            {
                return p;
            }
        }
        return default;
    }

    private void BindTypeParameter(Node node)
    {
        var nodeParent = store.Parent(node);
        if (store.Kind(nodeParent) == SyntaxKind.JSDocTemplateTag)
        {
            var templateContainer = Utilities.GetEffectiveContainerForJSDocTemplateTag(store, nodeParent);
            if (!templateContainer.IsNone)
            {
                Debug.Assert(Utilities.CanHaveLocals(store, templateContainer));
                if (b.LocalsOf(templateContainer).IsNone)
                {
                    b.SetLocalsOf(templateContainer, b.CreateTable());
                }
                DeclareSymbol(b.LocalsOf(templateContainer), Symbol.None, node, SymbolFlags.TypeParameter, SymbolFlags.TypeParameterExcludes);
            }
            else
            {
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.TypeParameter, SymbolFlags.TypeParameterExcludes);
            }
        }
        else if (store.Kind(nodeParent) == SyntaxKind.InferType)
        {
            var inferContainer = GetInferTypeContainer(nodeParent);
            if (!inferContainer.IsNone)
            {
                Debug.Assert(Utilities.CanHaveLocals(store, inferContainer));
                if (b.LocalsOf(inferContainer).IsNone)
                {
                    b.SetLocalsOf(inferContainer, b.CreateTable());
                }
                DeclareSymbol(b.LocalsOf(inferContainer), Symbol.None, node, SymbolFlags.TypeParameter, SymbolFlags.TypeParameterExcludes);
            }
            else
            {
                BindAnonymousDeclaration(node, SymbolFlags.TypeParameter, GetDeclarationName(node));
            }
        }
        else
        {
            DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.TypeParameter, SymbolFlags.TypeParameterExcludes);
        }
    }

    /// <summary>utilities.ts: isThisInitializedDeclaration.</summary>
    private bool IsThisInitializedDeclaration(Node node)
    {
        return !node.IsNone && store.Kind(node) == SyntaxKind.VariableDeclaration && !store.Initializer(node).IsNone && store.Kind(store.Initializer(node)) == SyntaxKind.ThisKeyword;
    }

    /// <summary>utilities.ts: isVariableDeclarationInitializedToBareOrAccessedRequire.</summary>
    private bool IsVariableDeclarationInitializedToBareOrAccessedRequire(Node node)
    {
        return store.Kind(node) == SyntaxKind.VariableDeclaration &&
            !store.Initializer(node).IsNone &&
            IsRequireCall(GetLeftmostAccessExpression(store.Initializer(node)), /*requireStringLiteralLikeArgument*/ true);
    }

    /// <summary>utilities.ts: getLeftmostAccessExpression.</summary>
    private Node GetLeftmostAccessExpression(Node expr)
    {
        while (Utilities.IsAccessExpression(store, expr))
        {
            expr = store.Expression(expr);
        }
        return expr;
    }

    /// <summary>utilities.ts: isRequireCall.</summary>
    private bool IsRequireCall(Node callExpression, bool requireStringLiteralLikeArgument)
    {
        if (store.Kind(callExpression) != SyntaxKind.CallExpression)
        {
            return false;
        }
        var expression = store.Expression(callExpression);
        var args = store.Arguments(callExpression);

        if (store.Kind(expression) != SyntaxKind.Identifier || !store.Texts.Equals(store.EscapedText(expression), "require"))
        {
            return false;
        }

        if (args.IsNone || store.Length(args) != 1)
        {
            return false;
        }
        var arg = store.Element(args, 0);
        return !requireStringLiteralLikeArgument || UtilitiesPublic.IsStringLiteralLike(store, arg);
    }

    /// <summary>binder.ts: lookupSymbolForName.</summary>
    private Symbol LookupSymbolForName(Node lookupContainer, string name) => LookupSymbolForName(lookupContainer, texts.Add(name));

    private Symbol LookupSymbolForName(Node lookupContainer, TextRef name)
    {
        var locals = Utilities.CanHaveLocals(store, lookupContainer) ? b.LocalsOf(lookupContainer) : SymbolTable.None;
        var local = locals.IsNone ? Symbol.None : b.Get(locals, name);
        if (!local.IsNone)
        {
            var exportSymbol = b.ExportSymbol(local);
            return !exportSymbol.IsNone ? exportSymbol : local;
        }
        if (store.Kind(lookupContainer) == SyntaxKind.SourceFile && lookupContainer == file && !b.JsGlobalAugmentations.IsNone)
        {
            var augmentation = b.Get(b.JsGlobalAugmentations, name);
            if (!augmentation.IsNone)
            {
                return augmentation;
            }
        }
        if (Utilities.CanHaveSymbol(store, lookupContainer))
        {
            var symbol = b.SymbolOf(lookupContainer);
            var exports = symbol.IsNone ? SymbolTable.None : b.Exports(symbol);
            return exports.IsNone ? Symbol.None : b.Get(exports, name);
        }
        return Symbol.None;
    }

    /// <summary>binder.ts: getContainerFlags.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static ContainerFlags GetContainerFlags(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ClassExpression:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.TypeLiteral:
            case SyntaxKind.JSDocTypeLiteral:
            case SyntaxKind.JsxAttributes:
                return ContainerFlags.IsContainer;

            case SyntaxKind.InterfaceDeclaration:
                return ContainerFlags.IsContainer | ContainerFlags.IsInterface;

            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.MappedType:
            case SyntaxKind.IndexSignature:
                return ContainerFlags.IsContainer | ContainerFlags.HasLocals;

            case SyntaxKind.SourceFile:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals;

            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.MethodDeclaration:
                if (Utilities.IsObjectLiteralOrClassExpressionMethodOrAccessor(store, node))
                {
                    return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike | ContainerFlags.IsObjectLiteralOrClassExpressionMethodOrAccessor;
                }
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike;
            case SyntaxKind.Constructor:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.ClassStaticBlockDeclaration:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike;

            case SyntaxKind.MethodSignature:
            case SyntaxKind.CallSignature:
            case SyntaxKind.JSDocSignature:
            case SyntaxKind.JSDocFunctionType:
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.ConstructorType:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike | ContainerFlags.PropagatesThisKeyword;

            case SyntaxKind.JSDocImportTag:
                // treat as a container to prevent using an enclosing effective host, ensuring import bindings are scoped correctly
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.PropagatesThisKeyword;

            case SyntaxKind.FunctionExpression:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike | ContainerFlags.IsFunctionExpression;

            case SyntaxKind.ArrowFunction:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike | ContainerFlags.IsFunctionExpression | ContainerFlags.PropagatesThisKeyword;

            case SyntaxKind.ModuleBlock:
                return ContainerFlags.IsControlFlowContainer;

            case SyntaxKind.PropertyDeclaration:
                return !store.Initializer(node).IsNone ? ContainerFlags.IsControlFlowContainer : ContainerFlags.None;

            case SyntaxKind.CatchClause:
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
            case SyntaxKind.CaseBlock:
                return ContainerFlags.IsBlockScopedContainer | ContainerFlags.HasLocals;

            case SyntaxKind.Block:
                // do not treat blocks directly inside a function as a block-scoped-container.
                // Locals that reside in this block should go to the function locals. Otherwise 'x'
                // would not appear to be a redeclaration of a block scoped local in the following
                // example:
                //
                //      function foo() {
                //          var x;
                //          let x;
                //      }
                //
                // If we placed 'var x' into the function locals and 'let x' into the locals of
                // the block, then there would be no collision.
                //
                // By not creating a new block-scoped-container here, we ensure that both 'var x'
                // and 'let x' go into the Function-container's locals, and we do get a collision
                // conflict.
                var blockParent = store.Parent(node);
                return Utilities.IsFunctionLike(store, blockParent) || store.Kind(blockParent) == SyntaxKind.ClassStaticBlockDeclaration ? ContainerFlags.None : ContainerFlags.IsBlockScopedContainer | ContainerFlags.HasLocals;
        }

        return ContainerFlags.None;
    }
}
