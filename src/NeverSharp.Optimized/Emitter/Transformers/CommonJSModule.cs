// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/transformers/module/module.ts (transformModule, CommonJS) and transformers/utilities.ts
// (collectExternalModuleInfo, ExternalModuleInfo, the import helper predicates) and factory/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/transformers/moduletransforms/commonjsmodule.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D3. The CommonJS transform of files whose emit module format is
// CommonJS (.cts) under TypeScript 6.0.3's default options (esModuleInterop, no importHelpers): AMD and UMD, outFile,
// rewriteRelativeImportExtensions and the flattening of destructuring assignments to exported names are not ported.

using System.Runtime.InteropServices;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>transformers/utilities.ts ExternalModuleInfo.</summary>
internal sealed class ExternalModuleInfo
{
    internal required List<Node> ExternalImports; // imports of other external modules
    internal required IdentifierNameMultiMap<Node> ExportSpecifiers; // file-local export specifiers by name (no reexports)
    internal required Dictionary<Node, List<Node>> ExportedBindings; // exported names of local declarations (by original node)
    internal List<Node>? ExportedNames; // all exported names in the module, both local and reexported, excluding the names of locally exported function declarations
    internal required List<Node> ExportedFunctions; // all of the top-level exported function declarations (a set, in insertion order)
    internal Node ExportEquals; // an export= declaration if one was present
    internal bool HasExportStarsToExportValues; // whether this module contains export*
}

/// <summary>module.ts transformModule for CommonJS: the closure of the transformer is the instance.</summary>
internal sealed class CommonJSModuleTransformer
{
    private readonly TransformationContext context;
    private readonly NodeFactory factory;
    private readonly CompilerOptions compilerOptions;
    private readonly EmitResolver resolver;
    private readonly EmitHost host;
    private readonly ScriptTarget languageVersion;
    private readonly Func<EmitHint, Node, Node> previousOnSubstituteNode;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> previousOnEmitNode;
    private readonly Func<Node, VisitResult> topLevelVisitor;
    private readonly Func<Node, VisitResult> topLevelNestedVisitor;
    private readonly Func<Node, VisitResult> visitor;
    private readonly Func<Node, VisitResult> discardedValueVisitor;
    private readonly Func<Node, VisitResult> modifierVisitor;
    private readonly Func<Node, Node, Node, Node> createAllExportExpressions;

    private readonly Dictionary<Node, ExternalModuleInfo> moduleInfoMap = []; // The ExternalModuleInfo for each file.
    private readonly HashSet<Node> noSubstitution = []; // Set of nodes for which substitution rules should be ignored.
    private Node currentSourceFile; // The current file.
    private ExternalModuleInfo? currentModuleInfo; // The ExternalModuleInfo for the current file.

    private CommonJSModuleTransformer(TransformationContext context)
    {
        this.context = context;
        factory = context.Factory;
        compilerOptions = context.GetCompilerOptions();
        resolver = context.GetEmitResolver();
        host = context.GetEmitHost();
        languageVersion = compilerOptions.GetEmitScriptTarget();
        var moduleKind = compilerOptions.GetEmitModuleKind();
        if (moduleKind is ModuleKind.AMD or ModuleKind.UMD)
        {
            throw new NotSupportedException("transformModule: AMD and UMD are not Phase 4 options.");
        }
        if (compilerOptions.RewriteRelativeImportExtensions == true)
        {
            throw new NotSupportedException("transformModule: rewriteRelativeImportExtensions is not a Phase 4 option.");
        }

        previousOnSubstituteNode = context.OnSubstituteNode;
        previousOnEmitNode = context.OnEmitNode;
        context.OnSubstituteNode = OnSubstituteNode;
        context.OnEmitNode = OnEmitNode;
        context.EnableSubstitution(SyntaxKind.CallExpression); // Substitute calls to imported/exported symbols to avoid incorrect `this`.
        context.EnableSubstitution(SyntaxKind.TaggedTemplateExpression); // Substitute calls to imported/exported symbols to avoid incorrect `this`.
        context.EnableSubstitution(SyntaxKind.Identifier); // Substitutes expression identifiers with imported/exported symbols.
        context.EnableSubstitution(SyntaxKind.BinaryExpression); // Substitutes assignments to exported symbols.
        context.EnableSubstitution(SyntaxKind.ShorthandPropertyAssignment); // Substitutes shorthand property assignments for imported/exported symbols.
        context.EnableEmitNotification(SyntaxKind.SourceFile); // Restore state when substituting nodes in a file.

        currentSourceFile = Node.None;
        topLevelVisitor = TopLevelVisitor;
        topLevelNestedVisitor = TopLevelNestedVisitor;
        visitor = Visitor;
        discardedValueVisitor = DiscardedValueVisitor;
        modifierVisitor = ModifierVisitor;
        createAllExportExpressions = CreateAllExportExpressions;
    }

    private NodeStore store => context.Store;

    private ExternalModuleInfo moduleInfo => currentModuleInfo!;

    /// <summary>transformModule(context): sets its emit hooks on the context (chainBundle: source files only).</summary>
    internal static CommonJSModuleTransformer Create(TransformationContext context) => new(context);

    // getOriginalNode(node) as the key of moduleInfoMap and exportedBindings (getOriginalNodeId).
    private Node OriginalKey(Node node) => TransformerUtilities.GetOriginalNode(store, node);

    /// <summary>Transforms the module aspects of a SourceFile.</summary>
    internal Node TransformSourceFile(Node node)
    {
        if (store.IsDeclarationFile(node) ||
            !(IsEffectiveExternalModule(node) ||
                (store.TransformFlags(node) & TransformFlags.ContainsDynamicImport) != 0))
        {
            // (isJsonSourceFile && hasJsonModuleEmitEnabled && outFile: no outFile.)
            return node;
        }

        currentSourceFile = node;
        currentModuleInfo = CollectExternalModuleInfo(node);
        moduleInfoMap[OriginalKey(node)] = currentModuleInfo;

        // Perform the transformation.
        var updated = TransformCommonJSModule(node);
        currentSourceFile = Node.None;
        currentModuleInfo = null;
        return updated;
    }

    // utilities.ts isEffectiveExternalModule. (commonJsModuleIndicator: the binder sets it for JavaScript files only, and
    // under Phase 4's options a JavaScript file's .js output would overwrite its input, so it is never transformed.)
    private bool IsEffectiveExternalModule(Node node)
    {
        Debug.Assert(store.ScriptKind(node) is not (ScriptKind.JS or ScriptKind.JSX));
        return Utilities.IsExternalModule(store, node);
    }

    private bool ShouldEmitUnderscoreUnderscoreESModule()
    {
        // (hasJSFileExtension(currentSourceFile.fileName) && currentSourceFile.commonJsModuleIndicator && ...: no
        // JavaScript file is transformed, see IsEffectiveExternalModule.)
        if (moduleInfo.ExportEquals.IsNone && Utilities.IsExternalModule(store, currentSourceFile))
        {
            return true;
        }
        return false;
    }

    /// <summary>Transforms a SourceFile into a CommonJS module.</summary>
    private Node TransformCommonJSModule(Node node)
    {
        context.StartLexicalEnvironment();

        var statements = new List<Node>();
        var ensureUseStrict = compilerOptions.GetAlwaysStrict() || Utilities.IsExternalModule(store, currentSourceFile);
        var sourceStatements = store.Statements(node);
        var statementOffset = factory.CopyPrologue(store.Nodes(sourceStatements), statements, ensureUseStrict && store.ScriptKind(node) != ScriptKind.JSON, topLevelVisitor);

        if (ShouldEmitUnderscoreUnderscoreESModule())
        {
            statements.Add(CreateUnderscoreUnderscoreESModule());
        }
        if (moduleInfo.ExportedNames is { Count: > 0 } exportedNames)
        {
            const int chunkSize = 50;
            for (var i = 0; i < exportedNames.Count; i += chunkSize)
            {
                var prev = factory.CreateVoidZero();
                for (var j = i; j < Math.Min(i + chunkSize, exportedNames.Count); j++)
                {
                    var nextId = exportedNames[j];
                    prev = store.Kind(nextId) == SyntaxKind.StringLiteral
                        ? factory.CreateAssignment(factory.CreateElementAccessExpression(factory.CreateIdentifier("exports"), factory.CreateStringLiteral(store.Text(nextId))), prev)
                        : factory.CreateAssignment(factory.CreatePropertyAccessExpression(factory.CreateIdentifier("exports"), factory.CreateIdentifier(UtilitiesPublic.IdText(store, nextId))), prev);
                }
                statements.Add(factory.CreateExpressionStatement(prev));
            }
        }
        foreach (var f in moduleInfo.ExportedFunctions)
        {
            AppendExportsOfHoistedDeclaration(statements, f);
        }

        // (externalHelpersImportDeclaration: none without importHelpers.)
        statements.AddRange(store.Nodes(Visitors.VisitNodes(factory, sourceStatements, topLevelVisitor, statementOffset)).ToArray());
        AddExportEqualsIfNeeded(statements, emitAsReturn: false);
        InsertStatementsAfterStandardPrologue(statements, context.EndLexicalEnvironment());

        var updated = factory.UpdateSourceFile(node, FactoryUtilitiesPublic.SetTextRange(store, factory.CreateNodeArray(statements), sourceStatements));
        FactoryEmitNode.AddEmitHelpers(store, updated, context.ReadEmitHelpers());
        return updated;
    }

    // utilities.ts insertStatementsAfterStandardPrologue
    private void InsertStatementsAfterStandardPrologue(List<Node> to, List<Node>? from)
    {
        if (from is null || from.Count == 0)
        {
            return;
        }
        var statementIndex = 0;
        // skip all prologue directives to insert at the correct position
        for (; statementIndex < to.Count; ++statementIndex)
        {
            if (!Utilities.IsPrologueDirective(store, to[statementIndex]))
            {
                break;
            }
        }
        to.InsertRange(statementIndex, from);
    }

    private void AddExportEqualsIfNeeded(List<Node> statements, bool emitAsReturn)
    {
        if (!moduleInfo.ExportEquals.IsNone)
        {
            var expressionResult = Visitors.VisitNode(factory, store.Expression(moduleInfo.ExportEquals), visitor);
            if (!expressionResult.IsNone)
            {
                Debug.Assert(!emitAsReturn); // (AMD and UMD only)
                var statement = factory.CreateExpressionStatement(
                    factory.CreateAssignment(
                        factory.CreatePropertyAccessExpression(
                            factory.CreateIdentifier("module"),
                            factory.CreateIdentifier("exports")),
                        expressionResult));

                FactoryUtilitiesPublic.SetTextRange(store, statement, moduleInfo.ExportEquals);
                FactoryEmitNode.SetEmitFlags(store, statement, EmitFlags.NoComments);
                statements.Add(statement);
            }
        }
    }

    //
    // Top-Level Source Element Visitors
    //

    /// <summary>Visits a node at the top level of the source file.</summary>
    private VisitResult TopLevelVisitor(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ImportDeclaration:
                return VisitTopLevelImportDeclaration(node);

            case SyntaxKind.ImportEqualsDeclaration:
                return VisitTopLevelImportEqualsDeclaration(node);

            case SyntaxKind.ExportDeclaration:
                return VisitTopLevelExportDeclaration(node);

            case SyntaxKind.ExportAssignment:
                return VisitTopLevelExportAssignment(node);

            default:
                return TopLevelNestedVisitor(node);
        }
    }

    /// <summary>Visit nested elements at the top-level of a module.</summary>
    private VisitResult TopLevelNestedVisitor(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.VariableStatement:
                return VisitVariableStatement(node);

            case SyntaxKind.FunctionDeclaration:
                return VisitFunctionDeclaration(node);

            case SyntaxKind.ClassDeclaration:
                return VisitClassDeclaration(node);

            case SyntaxKind.ForStatement:
                return VisitForStatement(node, isTopLevel: true);

            case SyntaxKind.ForInStatement:
                return VisitForInStatement(node);

            case SyntaxKind.ForOfStatement:
                return VisitForOfStatement(node);

            case SyntaxKind.DoStatement:
                return factory.UpdateDoStatement(
                    node,
                    Visitors.VisitIterationBody(store.Statement(node), topLevelNestedVisitor, context),
                    Visitors.VisitNode(factory, store.Expression(node), visitor));

            case SyntaxKind.WhileStatement:
                return factory.UpdateWhileStatement(
                    node,
                    Visitors.VisitNode(factory, store.Expression(node), visitor),
                    Visitors.VisitIterationBody(store.Statement(node), topLevelNestedVisitor, context));

            case SyntaxKind.LabeledStatement:
            {
                var statement = Visitors.VisitNode(factory, store.Statement(node), topLevelNestedVisitor, factory.LiftToBlock);
                if (statement.IsNone)
                {
                    statement = FactoryUtilitiesPublic.SetTextRange(store, factory.CreateEmptyStatement(), store.Statement(node));
                }
                return factory.UpdateLabeledStatement(node, store.Label(node), statement);
            }

            case SyntaxKind.WithStatement:
                return factory.UpdateWithStatement(
                    node,
                    Visitors.VisitNode(factory, store.Expression(node), visitor),
                    Visitors.VisitNode(factory, store.Statement(node), topLevelNestedVisitor, factory.LiftToBlock));

            case SyntaxKind.IfStatement:
            {
                var thenStatement = Visitors.VisitNode(factory, store.ThenStatement(node), topLevelNestedVisitor, factory.LiftToBlock);
                if (thenStatement.IsNone)
                {
                    thenStatement = factory.CreateBlock(factory.CreateNodeArray(ReadOnlySpan<Node>.Empty));
                }
                return factory.UpdateIfStatement(
                    node,
                    Visitors.VisitNode(factory, store.Expression(node), visitor),
                    thenStatement,
                    Visitors.VisitNode(factory, store.ElseStatement(node), topLevelNestedVisitor, factory.LiftToBlock));
            }

            case SyntaxKind.SwitchStatement:
                return factory.UpdateSwitchStatement(
                    node,
                    Visitors.VisitNode(factory, store.Expression(node), visitor),
                    Visitors.VisitNode(factory, store.CaseBlock(node), topLevelNestedVisitor));

            case SyntaxKind.CaseBlock:
                return factory.UpdateCaseBlock(
                    node,
                    Visitors.VisitNodes(factory, store.Clauses(node), topLevelNestedVisitor));

            case SyntaxKind.CaseClause:
                return factory.UpdateCaseClause(
                    node,
                    Visitors.VisitNode(factory, store.Expression(node), visitor),
                    Visitors.VisitNodes(factory, store.Statements(node), topLevelNestedVisitor));

            case SyntaxKind.DefaultClause:
            case SyntaxKind.TryStatement:
            case SyntaxKind.Block:
                return Visitors.VisitEachChild(node, topLevelNestedVisitor, context);

            case SyntaxKind.CatchClause:
                return factory.UpdateCatchClause(
                    node,
                    store.VariableDeclaration(node),
                    Visitors.VisitNode(factory, store.Block(node), topLevelNestedVisitor));

            default:
                return Visitor(node);
        }
    }

    private VisitResult VisitorWorker(Node node, bool valueIsDiscarded)
    {
        // This visitor does not need to descend into the tree if there is no dynamic import, destructuring assignment, or update expression
        // as export/import statements are only transformed at the top level of a file.
        if ((store.TransformFlags(node) & (TransformFlags.ContainsDynamicImport | TransformFlags.ContainsDestructuringAssignment | TransformFlags.ContainsUpdateExpressionForIdentifier)) == 0)
        {
            return node;
        }

        switch (store.Kind(node))
        {
            case SyntaxKind.ForStatement:
                return VisitForStatement(node, isTopLevel: false);
            case SyntaxKind.ExpressionStatement:
                return factory.UpdateExpressionStatement(
                    node,
                    Visitors.VisitNode(factory, store.Expression(node), discardedValueVisitor));
            case SyntaxKind.ParenthesizedExpression:
                return factory.UpdateParenthesizedExpression(node, Visitors.VisitNode(factory, store.Expression(node), valueIsDiscarded ? discardedValueVisitor : visitor));
            case SyntaxKind.PartiallyEmittedExpression:
                return factory.UpdatePartiallyEmittedExpression(node, Visitors.VisitNode(factory, store.Expression(node), valueIsDiscarded ? discardedValueVisitor : visitor));
            case SyntaxKind.CallExpression:
                // (importsAndRequiresToRewriteOrShim: only with rewriteRelativeImportExtensions.)
                if (Utilities.IsImportCall(store, node) && host.ShouldTransformImportCall(store, currentSourceFile))
                {
                    return VisitImportCallExpression(node);
                }
                break;
            case SyntaxKind.BinaryExpression:
                if (Utilities.IsDestructuringAssignment(store, node))
                {
                    return VisitDestructuringAssignment(node);
                }
                break;
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
                return VisitPreOrPostfixUnaryExpression(node, valueIsDiscarded);
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult Visitor(Node node) => VisitorWorker(node, valueIsDiscarded: false);

    private VisitResult DiscardedValueVisitor(Node node) => VisitorWorker(node, valueIsDiscarded: true);

    private bool DestructuringNeedsFlattening(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ObjectLiteralExpression:
                foreach (var elem in store.Nodes(store.Properties(node)))
                {
                    switch (store.Kind(elem))
                    {
                        case SyntaxKind.PropertyAssignment:
                            if (DestructuringNeedsFlattening(store.Initializer(elem)))
                            {
                                return true;
                            }
                            break;
                        case SyntaxKind.ShorthandPropertyAssignment:
                            if (DestructuringNeedsFlattening(store.Name(elem)))
                            {
                                return true;
                            }
                            break;
                        case SyntaxKind.SpreadAssignment:
                            if (DestructuringNeedsFlattening(store.Expression(elem)))
                            {
                                return true;
                            }
                            break;
                        case SyntaxKind.MethodDeclaration:
                        case SyntaxKind.GetAccessor:
                        case SyntaxKind.SetAccessor:
                            return false;
                        default:
                            Debug.Fail("Unhandled object member kind");
                            break;
                    }
                }
                break;
            case SyntaxKind.ArrayLiteralExpression:
                foreach (var elem in store.Nodes(store.Elements(node)))
                {
                    if (store.Kind(elem) == SyntaxKind.SpreadElement)
                    {
                        if (DestructuringNeedsFlattening(store.Expression(elem)))
                        {
                            return true;
                        }
                    }
                    else if (DestructuringNeedsFlattening(elem))
                    {
                        return true;
                    }
                }
                break;
            case SyntaxKind.Identifier:
                return (GetExports(node)?.Count ?? 0) > (IsExportName(node) ? 1 : 0);
        }
        return false;
    }

    private Node VisitDestructuringAssignment(Node node)
    {
        if (DestructuringNeedsFlattening(store.Left(node)))
        {
            throw new NotSupportedException("transformModule: flattenDestructuringAssignment of an assignment to exported names is not ported (module D3).");
        }
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitForStatement(Node node, bool isTopLevel)
    {
        var initializer = store.Initializer(node);
        if (isTopLevel && !initializer.IsNone &&
            store.Kind(initializer) == SyntaxKind.VariableDeclarationList &&
            (store.Flags(initializer) & NodeFlags.BlockScoped) == 0)
        {
            var exportStatements = AppendExportsOfVariableDeclarationList(null, initializer, isForInOrOfInitializer: false);
            if (exportStatements is not null)
            {
                var statements = new List<Node>();
                var varDeclList = Visitors.VisitNode(factory, initializer, discardedValueVisitor);
                var varStatement = factory.CreateVariableStatement(default, varDeclList);
                statements.Add(varStatement);
                statements.AddRange(exportStatements);

                var condition = Visitors.VisitNode(factory, store.Condition(node), visitor);
                var incrementor = Visitors.VisitNode(factory, store.Incrementor(node), discardedValueVisitor);
                var body = Visitors.VisitIterationBody(store.Statement(node), isTopLevel ? topLevelNestedVisitor : visitor, context);
                statements.Add(factory.UpdateForStatement(node, default, condition, incrementor, body));
                return VisitResult.Many(statements);
            }
        }
        return factory.UpdateForStatement(
            node,
            Visitors.VisitNode(factory, initializer, discardedValueVisitor),
            Visitors.VisitNode(factory, store.Condition(node), visitor),
            Visitors.VisitNode(factory, store.Incrementor(node), discardedValueVisitor),
            Visitors.VisitIterationBody(store.Statement(node), isTopLevel ? topLevelNestedVisitor : visitor, context));
    }

    private Node MergeExportStatementsIntoBody(List<Node> exportStatements, Node body)
    {
        if (store.Kind(body) == SyntaxKind.Block)
        {
            var merged = new List<Node>(exportStatements);
            merged.AddRange(store.Nodes(store.Statements(body)).ToArray());
            return factory.UpdateBlock(body, factory.CreateNodeArray(merged));
        }
        var statements = new List<Node>(exportStatements) { body };
        return factory.CreateBlock(factory.CreateNodeArray(statements), multiLine: true);
    }

    /// <summary>Visits the body of a ForInStatement to hoist declarations.</summary>
    private VisitResult VisitForInStatement(Node node)
    {
        var initializer = store.Initializer(node);
        if (store.Kind(initializer) == SyntaxKind.VariableDeclarationList && (store.Flags(initializer) & NodeFlags.BlockScoped) == 0)
        {
            var exportStatements = AppendExportsOfVariableDeclarationList(null, initializer, isForInOrOfInitializer: true);
            if (exportStatements is { Count: > 0 })
            {
                var visitedInitializer = Visitors.VisitNode(factory, initializer, discardedValueVisitor);
                var expression = Visitors.VisitNode(factory, store.Expression(node), visitor);
                var body = Visitors.VisitIterationBody(store.Statement(node), topLevelNestedVisitor, context);
                return factory.UpdateForInStatement(node, visitedInitializer, expression, MergeExportStatementsIntoBody(exportStatements, body));
            }
        }
        return factory.UpdateForInStatement(
            node,
            Visitors.VisitNode(factory, initializer, discardedValueVisitor),
            Visitors.VisitNode(factory, store.Expression(node), visitor),
            Visitors.VisitIterationBody(store.Statement(node), topLevelNestedVisitor, context));
    }

    /// <summary>Visits the body of a ForOfStatement to hoist declarations.</summary>
    private VisitResult VisitForOfStatement(Node node)
    {
        var initializer = store.Initializer(node);
        if (store.Kind(initializer) == SyntaxKind.VariableDeclarationList && (store.Flags(initializer) & NodeFlags.BlockScoped) == 0)
        {
            var exportStatements = AppendExportsOfVariableDeclarationList(null, initializer, isForInOrOfInitializer: true);
            var visitedInitializer = Visitors.VisitNode(factory, initializer, discardedValueVisitor);
            var expression = Visitors.VisitNode(factory, store.Expression(node), visitor);
            var body = Visitors.VisitIterationBody(store.Statement(node), topLevelNestedVisitor, context);
            if (exportStatements is { Count: > 0 })
            {
                body = MergeExportStatementsIntoBody(exportStatements, body);
            }
            return factory.UpdateForOfStatement(node, store.AwaitModifier(node), visitedInitializer, expression, body);
        }
        return factory.UpdateForOfStatement(
            node,
            store.AwaitModifier(node),
            Visitors.VisitNode(factory, initializer, discardedValueVisitor),
            Visitors.VisitNode(factory, store.Expression(node), visitor),
            Visitors.VisitIterationBody(store.Statement(node), topLevelNestedVisitor, context));
    }

    private VisitResult VisitPreOrPostfixUnaryExpression(Node node, bool valueIsDiscarded)
    {
        // When we see a prefix or postfix increment expression whose operand is an exported
        // symbol, we should ensure all exports of that symbol are updated with the correct
        // value.
        //
        // - We do not transform generated identifiers for any reason.
        // - We do not transform identifiers tagged with the LocalName flag.
        // - We do not transform identifiers that were originally the name of an enum or
        //   namespace due to how they are transformed in TypeScript.
        // - We only transform identifiers that are exported at the top level.
        var operand = store.Operand(node);
        if (store.Operator(node) is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken
            && store.Kind(operand) == SyntaxKind.Identifier
            && !UtilitiesPublic.IsGeneratedIdentifier(store, operand)
            && !FactoryUtilities.IsLocalName(store, operand)
            && !IsDeclarationNameOfEnumOrNamespace(operand))
        {
            var exportedNames = GetExports(operand);
            if (exportedNames is not null)
            {
                var temp = Node.None;
                var expression = Visitors.VisitNode(factory, operand, visitor);
                if (store.Kind(node) == SyntaxKind.PrefixUnaryExpression)
                {
                    expression = factory.UpdatePrefixUnaryExpression(node, expression);
                }
                else
                {
                    expression = factory.UpdatePostfixUnaryExpression(node, expression);
                    if (!valueIsDiscarded)
                    {
                        temp = factory.CreateTempVariable(context.HoistVariableDeclaration);
                        expression = factory.CreateAssignment(temp, expression);
                        FactoryUtilitiesPublic.SetTextRange(store, expression, node);
                    }
                    expression = factory.CreateComma(expression, factory.CloneNode(operand));
                    FactoryUtilitiesPublic.SetTextRange(store, expression, node);
                }

                foreach (var exportName in exportedNames)
                {
                    noSubstitution.Add(expression);
                    expression = CreateExportExpression(exportName, expression);
                    FactoryUtilitiesPublic.SetTextRange(store, expression, node);
                }

                if (!temp.IsNone)
                {
                    noSubstitution.Add(expression);
                    expression = factory.CreateComma(expression, temp);
                    FactoryUtilitiesPublic.SetTextRange(store, expression, node);
                }
                return expression;
            }
        }
        return Visitors.VisitEachChild(node, visitor, context);
    }

    // utilities.ts isDeclarationNameOfEnumOrNamespace
    private bool IsDeclarationNameOfEnumOrNamespace(Node node)
    {
        var parseNode = UtilitiesPublic.GetParseTreeNode(store, node);
        if (!parseNode.IsNone)
        {
            var parent = store.Parent(parseNode);
            switch (store.Kind(parent))
            {
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.ModuleDeclaration:
                    return parseNode == store.Name(parent);
            }
        }
        return false;
    }

    private Node VisitImportCallExpression(Node node)
    {
        // (moduleKind None: not the emit module kind of a Phase 4 program.)
        var externalModuleName = GetExternalModuleNameLiteral(node);
        var arguments = store.Arguments(node);
        var firstArgument = Visitors.VisitNode(factory, store.Length(arguments) > 0 ? store.Element(arguments, 0) : Node.None, visitor);
        // Only use the external module name if it differs from the first argument. This allows us to preserve the quote style of the argument on output.
        var argument = !externalModuleName.IsNone && (firstArgument.IsNone || store.Kind(firstArgument) != SyntaxKind.StringLiteral || !store.Texts.Equals(store.Text(firstArgument), store.Text(externalModuleName)))
            ? externalModuleName
            : firstArgument;
        // (compilerOptions.module AMD or UMD: not Phase 4 options.)
        return CreateImportCallExpressionCommonJS(argument);
    }

    private Node CreateImportCallExpressionCommonJS(Node arg, bool isInlineable = false)
    {
        // import(x)
        // emit as
        // Promise.resolve(`${x}`).then((s) => require(s)) /*CommonJs Require*/
        // We have to wrap require in then callback so that require is done in asynchronously
        // if we simply do require in resolve callback in Promise constructor. We will execute the loading immediately
        // If the arg is not inlineable, we have to evaluate and ToString() it in the current scope
        // Otherwise, we inline it in require() so that it's statically analyzable
        var needSyncEval = !arg.IsNone && !TransformerUtilities.IsSimpleInlineableExpression(store, arg) && !isInlineable;

        Node[] promiseArguments;
        if (needSyncEval)
        {
            Debug.Assert(languageVersion >= ScriptTarget.ES2015);
            promiseArguments =
            [
                factory.CreateTemplateExpression(
                    factory.CreateTemplateLiteralLikeNode(SyntaxKind.TemplateHead, store.Texts.Add(""), default, 0),
                    factory.CreateNodeArray(new[]
                    {
                        factory.CreateTemplateSpan(arg, factory.CreateTemplateLiteralLikeNode(SyntaxKind.TemplateTail, store.Texts.Add(""), default, 0)),
                    })),
            ];
        }
        else
        {
            promiseArguments = [];
        }
        var promiseResolveCall = factory.CreateCallExpression(
            factory.CreatePropertyAccessExpression(factory.CreateIdentifier("Promise"), factory.CreateIdentifier("resolve")),
            default,
            factory.CreateNodeArray(promiseArguments));

        var requireCall = factory.CreateCallExpression(
            factory.CreateIdentifier("require"),
            default,
            factory.CreateNodeArray(needSyncEval ? [factory.CreateIdentifier("s")] : !arg.IsNone ? [arg] : ReadOnlySpan<Node>.Empty));
        if (compilerOptions.GetESModuleInterop())
        {
            requireCall = context.GetEmitHelperFactory().CreateImportStarHelper(requireCall);
        }

        var parameters = needSyncEval
            ? new[]
            {
                factory.CreateParameterDeclaration(
                    default,
                    default,
                    factory.CreateIdentifier("s")),
            }
            : [];

        // (languageVersion is ES2015 or later: an arrow function.)
        var func = factory.CreateArrowFunction(
            default,
            default,
            factory.CreateNodeArray(parameters),
            default,
            default,
            requireCall);

        var downleveledImport = factory.CreateCallExpression(factory.CreatePropertyAccessExpression(promiseResolveCall, factory.CreateIdentifier("then")), default, factory.CreateNodeArray(new[] { func }));

        return downleveledImport;
    }

    private Node GetHelperExpressionForExport(Node node, Node innerExpr)
    {
        if (!compilerOptions.GetESModuleInterop() || (FactoryEmitNode.GetInternalEmitFlags(store, node) & InternalEmitFlags.NeverApplyImportHelper) != 0)
        {
            return innerExpr;
        }
        if (GetExportNeedsImportStarHelper(node))
        {
            return context.GetEmitHelperFactory().CreateImportStarHelper(innerExpr);
        }
        return innerExpr;
    }

    private Node GetHelperExpressionForImport(Node node, Node innerExpr)
    {
        if (!compilerOptions.GetESModuleInterop() || (FactoryEmitNode.GetInternalEmitFlags(store, node) & InternalEmitFlags.NeverApplyImportHelper) != 0)
        {
            return innerExpr;
        }
        if (GetImportNeedsImportStarHelper(node))
        {
            return context.GetEmitHelperFactory().CreateImportStarHelper(innerExpr);
        }
        if (GetImportNeedsImportDefaultHelper(node))
        {
            return context.GetEmitHelperFactory().CreateImportDefaultHelper(innerExpr);
        }
        return innerExpr;
    }

    private static VisitResult SingleOrMany(List<Node>? statements)
    {
        return statements is null ? VisitResult.None : statements.Count == 1 ? statements[0] : VisitResult.Many(statements);
    }

    private Node CreateConstOrVarStatement(List<Node> declarations, Node location, Node original)
    {
        var statement = factory.CreateVariableStatement(
            default,
            factory.CreateVariableDeclarationList(
                factory.CreateNodeArray(declarations),
                languageVersion >= ScriptTarget.ES2015 ? NodeFlags.Const : NodeFlags.None));
        FactoryUtilitiesPublic.SetTextRange(store, statement, location);
        NodeFactory.SetOriginalNode(store, statement, original);
        return statement;
    }

    /// <summary>Visits an ImportDeclaration node.</summary>
    private VisitResult VisitTopLevelImportDeclaration(Node node)
    {
        List<Node>? statements = null;
        var namespaceDeclaration = GetNamespaceDeclarationNode(node);
        // (moduleKind is not AMD)
        if (store.ImportClause(node).IsNone)
        {
            // import "mod";
            var statement = factory.CreateExpressionStatement(CreateRequireCall(node));
            FactoryUtilitiesPublic.SetTextRange(store, statement, node);
            return NodeFactory.SetOriginalNode(store, statement, node);
        }
        else
        {
            var variables = new List<Node>();
            if (!namespaceDeclaration.IsNone && !IsDefaultImport(node))
            {
                // import * as n from "mod";
                variables.Add(
                    factory.CreateVariableDeclaration(
                        factory.CloneNode(store.Name(namespaceDeclaration)),
                        default,
                        default,
                        GetHelperExpressionForImport(node, CreateRequireCall(node))));
            }
            else
            {
                // import d from "mod";
                // import { x, y } from "mod";
                // import d, { x, y } from "mod";
                // import d, * as n from "mod";
                variables.Add(
                    factory.CreateVariableDeclaration(
                        factory.GetGeneratedNameForNode(node),
                        default,
                        default,
                        GetHelperExpressionForImport(node, CreateRequireCall(node))));

                if (!namespaceDeclaration.IsNone && IsDefaultImport(node))
                {
                    variables.Add(
                        factory.CreateVariableDeclaration(
                            factory.CloneNode(store.Name(namespaceDeclaration)),
                            default,
                            default,
                            factory.GetGeneratedNameForNode(node)));
                }
            }

            statements = [CreateConstOrVarStatement(variables, node, node)];
        }

        statements = AppendExportsOfImportDeclaration(statements, node);
        return SingleOrMany(statements);
    }

    /// <summary>Creates a `require()` call to import an external module.</summary>
    private Node CreateRequireCall(Node importNode)
    {
        var moduleName = GetExternalModuleNameLiteral(importNode);
        // (rewriteModuleSpecifier: the module name itself without rewriteRelativeImportExtensions.)
        return factory.CreateCallExpression(
            factory.CreateIdentifier("require"),
            default,
            factory.CreateNodeArray(!moduleName.IsNone ? [moduleName] : ReadOnlySpan<Node>.Empty));
    }

    /// <summary>Visits an ImportEqualsDeclaration node.</summary>
    private VisitResult VisitTopLevelImportEqualsDeclaration(Node node)
    {
        Debug.Assert(Utilities.IsExternalModuleImportEqualsDeclaration(store, node), "import= for internal module references should be handled in an earlier transformer.");

        List<Node>? statements = null;
        // (moduleKind is not AMD)
        if (Utilities.HasSyntacticModifier(store, node, ModifierFlags.Export))
        {
            var statement = factory.CreateExpressionStatement(
                CreateExportExpression(
                    store.Name(node),
                    CreateRequireCall(node)));
            FactoryUtilitiesPublic.SetTextRange(store, statement, node);
            statements = [NodeFactory.SetOriginalNode(store, statement, node)];
        }
        else
        {
            statements =
            [
                CreateConstOrVarStatement(
                    [
                        factory.CreateVariableDeclaration(
                            factory.CloneNode(store.Name(node)),
                            default,
                            default,
                            CreateRequireCall(node)),
                    ],
                    node,
                    node),
            ];
        }

        statements = AppendExportsOfImportEqualsDeclaration(statements, node);
        return SingleOrMany(statements);
    }

    /// <summary>Visits an ExportDeclaration node.</summary>
    private VisitResult VisitTopLevelExportDeclaration(Node node)
    {
        if (store.ModuleSpecifier(node).IsNone)
        {
            // Elide export declarations with no module specifier as they are handled
            // elsewhere.
            return VisitResult.None;
        }

        var generatedName = factory.GetGeneratedNameForNode(node);
        var exportClause = store.ExportClause(node);

        if (!exportClause.IsNone && store.Kind(exportClause) == SyntaxKind.NamedExports)
        {
            var statements = new List<Node>();
            // export { x, y } from "mod";
            // (moduleKind is not AMD)
            var requireStatement = factory.CreateVariableStatement(
                default,
                factory.CreateVariableDeclarationList(factory.CreateNodeArray(new[]
                {
                    factory.CreateVariableDeclaration(
                        generatedName,
                        default,
                        default,
                        CreateRequireCall(node)),
                })));
            FactoryUtilitiesPublic.SetTextRange(store, requireStatement, node);
            statements.Add(NodeFactory.SetOriginalNode(store, requireStatement, node));
            foreach (var specifier in store.Nodes(store.Elements(exportClause)))
            {
                var specifierName = !store.PropertyName(specifier).IsNone ? store.PropertyName(specifier) : store.Name(specifier);
                var exportNeedsImportDefault = compilerOptions.GetESModuleInterop() &&
                    (FactoryEmitNode.GetInternalEmitFlags(store, node) & InternalEmitFlags.NeverApplyImportHelper) == 0 &&
                    ModuleExportNameIsDefault(specifierName);
                var target = exportNeedsImportDefault ? context.GetEmitHelperFactory().CreateImportDefaultHelper(generatedName) : generatedName;
                var exportedValue = store.Kind(specifierName) == SyntaxKind.StringLiteral
                    ? factory.CreateElementAccessExpression(target, specifierName)
                    : factory.CreatePropertyAccessExpression(target, specifierName);
                var specifierNameNode = store.Name(specifier);
                var statement = factory.CreateExpressionStatement(
                    CreateExportExpression(
                        store.Kind(specifierNameNode) == SyntaxKind.StringLiteral ? factory.CloneNode(specifierNameNode) : factory.GetExportName(specifier),
                        exportedValue,
                        location: default,
                        liveBinding: true));
                FactoryUtilitiesPublic.SetTextRange(store, statement, specifier);
                statements.Add(NodeFactory.SetOriginalNode(store, statement, specifier));
            }

            return SingleOrMany(statements);
        }
        else if (!exportClause.IsNone)
        {
            // export * as ns from "mod";
            // export * as default from "mod";
            var statement = factory.CreateExpressionStatement(
                CreateExportExpression(
                    factory.CloneNode(store.Name(exportClause)),
                    GetHelperExpressionForExport(
                        node,
                        CreateRequireCall(node))));
            FactoryUtilitiesPublic.SetTextRange(store, statement, node);
            return NodeFactory.SetOriginalNode(store, statement, node);
        }
        else
        {
            // export * from "mod";
            var statement = factory.CreateExpressionStatement(
                context.GetEmitHelperFactory().CreateExportStarHelper(CreateRequireCall(node)));
            FactoryUtilitiesPublic.SetTextRange(store, statement, node);
            return NodeFactory.SetOriginalNode(store, statement, node);
        }
    }

    /// <summary>Visits an ExportAssignment node.</summary>
    private VisitResult VisitTopLevelExportAssignment(Node node)
    {
        if (store.IsExportEquals(node))
        {
            return VisitResult.None;
        }

        return CreateExportStatement(factory.CreateIdentifier("default"), Visitors.VisitNode(factory, store.Expression(node), visitor), location: node, allowComments: true);
    }

    /// <summary>Visits a FunctionDeclaration node.</summary>
    private VisitResult VisitFunctionDeclaration(Node node)
    {
        if (Utilities.HasSyntacticModifier(store, node, ModifierFlags.Export))
        {
            var declaration = factory.CreateFunctionDeclaration(
                Visitors.VisitNodes(factory, store.Modifiers(node), modifierVisitor),
                store.AsteriskToken(node),
                factory.GetDeclarationName(node, allowComments: true, allowSourceMaps: true),
                default,
                Visitors.VisitNodes(factory, store.Parameters(node), visitor),
                default,
                Visitors.VisitEachChild(store.Body(node), visitor, context));
            FactoryUtilitiesPublic.SetTextRange(store, declaration, node);
            return NodeFactory.SetOriginalNode(store, declaration, node);
        }

        // NOTE: CommonJS/AMD/UMD exports are hoisted to the top of the module body and do not need to be added here.
        return Visitors.VisitEachChild(node, visitor, context);
    }

    /// <summary>Visits a ClassDeclaration node.</summary>
    private VisitResult VisitClassDeclaration(Node node)
    {
        List<Node>? statements = null;
        if (Utilities.HasSyntacticModifier(store, node, ModifierFlags.Export))
        {
            var declaration = factory.CreateClassDeclaration(
                Visitors.VisitNodes(factory, store.Modifiers(node), modifierVisitor),
                factory.GetDeclarationName(node, allowComments: true, allowSourceMaps: true),
                default,
                Visitors.VisitNodes(factory, store.HeritageClauses(node), visitor),
                Visitors.VisitNodes(factory, store.Members(node), visitor));
            FactoryUtilitiesPublic.SetTextRange(store, declaration, node);
            statements = [NodeFactory.SetOriginalNode(store, declaration, node)];
        }
        else
        {
            var visited = Visitors.VisitEachChild(node, visitor, context);
            if (!visited.IsNone)
            {
                statements = [visited];
            }
        }

        statements = AppendExportsOfHoistedDeclaration(statements, node);
        return SingleOrMany(statements);
    }

    /// <summary>Visits a VariableStatement node.</summary>
    private VisitResult VisitVariableStatement(Node node)
    {
        List<Node>? statements = null;
        List<Node>? variables = null;
        List<Node>? expressions = null;

        if (Utilities.HasSyntacticModifier(store, node, ModifierFlags.Export))
        {
            var modifiers = NodeList.None;
            var modifiersVisited = false;
            var removeCommentsOnExpressions = false;

            // If we're exporting these variables, then these just become assignments to 'exports.x'.
            foreach (var variable in store.Nodes(store.Declarations(store.DeclarationList(node))))
            {
                var name = store.Name(variable);
                var initializer = store.Initializer(variable);
                if (store.Kind(name) == SyntaxKind.Identifier && FactoryUtilities.IsLocalName(store, name))
                {
                    // A "local name" generally means a variable declaration that *shouldn't* be
                    // converted to `exports.x = ...`, even if the declaration is exported. This
                    // usually indicates a class or function declaration that was converted into
                    // a variable declaration, as most references to the declaration will remain
                    // untransformed (i.e., `new C` rather than `new exports.C`). In these cases,
                    // an `export { x }` declaration will follow.
                    if (!modifiersVisited)
                    {
                        modifiers = Visitors.VisitNodes(factory, store.Modifiers(node), modifierVisitor);
                        modifiersVisited = true;
                    }

                    if (!initializer.IsNone)
                    {
                        var updatedVariable = factory.UpdateVariableDeclaration(
                            variable,
                            name,
                            default,
                            default,
                            CreateExportExpression(
                                name,
                                Visitors.VisitNode(factory, initializer, visitor)));
                        (variables ??= []).Add(updatedVariable);
                    }
                    else
                    {
                        (variables ??= []).Add(variable);
                    }
                }
                else if (!initializer.IsNone)
                {
                    if (store.Kind(name) is not (SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern) &&
                        store.Kind(initializer) is SyntaxKind.ArrowFunction or SyntaxKind.FunctionExpression or SyntaxKind.ClassExpression)
                    {
                        var target = factory.CreatePropertyAccessExpression(
                            factory.CreateIdentifier("exports"),
                            name);
                        FactoryUtilitiesPublic.SetTextRange(store, target, name);
                        var expression = factory.CreateAssignment(
                            target,
                            factory.CreateIdentifier(GetTextOfIdentifierOrLiteral(name)));
                        var updatedVariable = factory.CreateVariableDeclaration(
                            name,
                            store.ExclamationToken(variable),
                            store.Type(variable),
                            Visitors.VisitNode(factory, initializer, visitor));

                        (variables ??= []).Add(updatedVariable);
                        (expressions ??= []).Add(expression);
                        removeCommentsOnExpressions = true;
                    }
                    else
                    {
                        (expressions ??= []).Add(TransformInitializedVariable(variable));
                    }
                }
            }

            if (variables is not null)
            {
                statements = [factory.UpdateVariableStatement(node, modifiers, factory.UpdateVariableDeclarationList(store.DeclarationList(node), factory.CreateNodeArray(variables)))];
            }

            if (expressions is not null)
            {
                var statement = factory.CreateExpressionStatement(factory.InlineExpressions(expressions));
                FactoryUtilitiesPublic.SetTextRange(store, statement, node);
                NodeFactory.SetOriginalNode(store, statement, node);
                if (removeCommentsOnExpressions)
                {
                    FactoryEmitNode.RemoveAllComments(store, statement);
                }
                (statements ??= []).Add(statement);
            }
        }
        else
        {
            var visited = Visitors.VisitEachChild(node, visitor, context);
            if (!visited.IsNone)
            {
                statements = [visited];
            }
        }

        statements = AppendExportsOfVariableStatement(statements, node);
        return SingleOrMany(statements);
    }

    // utilities.ts getTextOfIdentifierOrLiteral
    private TextRef GetTextOfIdentifierOrLiteral(Node node)
    {
        return store.Kind(node) is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier ? UtilitiesPublic.IdText(store, node) : store.Text(node);
    }

    private Node CreateAllExportExpressions(Node name, Node value, Node location)
    {
        var exportedNames = GetExports(name);
        if (exportedNames is not null)
        {
            // For each additional export of the declaration, apply an export assignment.
            var expression = IsExportName(name) ? value : factory.CreateAssignment(name, value);
            foreach (var exportName in exportedNames)
            {
                // Mark the node to prevent triggering substitution.
                FactoryEmitNode.SetEmitFlags(store, expression, EmitFlags.NoSubstitution);
                expression = CreateExportExpression(exportName, expression, location);
            }

            return expression;
        }
        return factory.CreateAssignment(name, value);
    }

    /// <summary>Transforms an exported variable with an initializer into an expression.</summary>
    private Node TransformInitializedVariable(Node node)
    {
        var name = store.Name(node);
        if (store.Kind(name) is SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern)
        {
            return Destructuring.FlattenDestructuringAssignment(
                Visitors.VisitNode(factory, node, visitor),
                visitor,
                context,
                FlattenLevel.All,
                needsValue: false,
                createAllExportExpressions);
        }
        else
        {
            var target = factory.CreatePropertyAccessExpression(
                factory.CreateIdentifier("exports"),
                name);
            FactoryUtilitiesPublic.SetTextRange(store, target, name);
            var initializer = store.Initializer(node);
            return factory.CreateAssignment(
                target,
                !initializer.IsNone ? Visitors.VisitNode(factory, initializer, visitor) : factory.CreateVoidZero());
        }
    }

    /// <summary>Appends the exports of an ImportDeclaration to a statement list, returning the statement list.</summary>
    private List<Node>? AppendExportsOfImportDeclaration(List<Node>? statements, Node decl)
    {
        if (!moduleInfo.ExportEquals.IsNone)
        {
            return statements;
        }

        var importClause = store.ImportClause(decl);
        if (importClause.IsNone)
        {
            return statements;
        }

        var seen = new IdentifierNameMap<bool>(store);
        if (!store.Name(importClause).IsNone)
        {
            statements = AppendExportsOfDeclaration(statements, seen, importClause);
        }

        var namedBindings = store.NamedBindings(importClause);
        if (!namedBindings.IsNone)
        {
            switch (store.Kind(namedBindings))
            {
                case SyntaxKind.NamespaceImport:
                    statements = AppendExportsOfDeclaration(statements, seen, namedBindings);
                    break;

                case SyntaxKind.NamedImports:
                    foreach (var importBinding in store.Nodes(store.Elements(namedBindings)))
                    {
                        statements = AppendExportsOfDeclaration(statements, seen, importBinding, liveBinding: true);
                    }

                    break;
            }
        }

        return statements;
    }

    /// <summary>Appends the exports of an ImportEqualsDeclaration to a statement list, returning the statement list.</summary>
    private List<Node>? AppendExportsOfImportEqualsDeclaration(List<Node>? statements, Node decl)
    {
        if (!moduleInfo.ExportEquals.IsNone)
        {
            return statements;
        }

        return AppendExportsOfDeclaration(statements, new IdentifierNameMap<bool>(store), decl);
    }

    /// <summary>Appends the exports of a VariableStatement to a statement list, returning the statement list.</summary>
    private List<Node>? AppendExportsOfVariableStatement(List<Node>? statements, Node node)
    {
        return AppendExportsOfVariableDeclarationList(statements, store.DeclarationList(node), isForInOrOfInitializer: false);
    }

    /// <summary>Appends the exports of a VariableDeclarationList to a statement list, returning the statement list.</summary>
    private List<Node>? AppendExportsOfVariableDeclarationList(List<Node>? statements, Node node, bool isForInOrOfInitializer)
    {
        if (!moduleInfo.ExportEquals.IsNone)
        {
            return statements;
        }

        foreach (var decl in store.Nodes(store.Declarations(node)))
        {
            statements = AppendExportsOfBindingElement(statements, decl, isForInOrOfInitializer);
        }

        return statements;
    }

    /// <summary>Appends the exports of a VariableDeclaration or BindingElement to a statement list, returning the statement list.</summary>
    private List<Node>? AppendExportsOfBindingElement(List<Node>? statements, Node decl, bool isForInOrOfInitializer)
    {
        if (!moduleInfo.ExportEquals.IsNone)
        {
            return statements;
        }

        var name = store.Name(decl);
        if (store.Kind(name) is SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern)
        {
            foreach (var element in store.Nodes(store.Elements(name)))
            {
                if (store.Kind(element) != SyntaxKind.OmittedExpression)
                {
                    statements = AppendExportsOfBindingElement(statements, element, isForInOrOfInitializer);
                }
            }
        }
        else if (!UtilitiesPublic.IsGeneratedIdentifier(store, name) &&
            (store.Kind(decl) != SyntaxKind.VariableDeclaration || !store.Initializer(decl).IsNone || isForInOrOfInitializer))
        {
            statements = AppendExportsOfDeclaration(statements, new IdentifierNameMap<bool>(store), decl);
        }

        return statements;
    }

    /// <summary>Appends the exports of a ClassDeclaration or FunctionDeclaration to a statement list, returning the statement list.</summary>
    private List<Node>? AppendExportsOfHoistedDeclaration(List<Node>? statements, Node decl)
    {
        if (!moduleInfo.ExportEquals.IsNone)
        {
            return statements;
        }

        var seen = new IdentifierNameMap<bool>(store);
        if (Utilities.HasSyntacticModifier(store, decl, ModifierFlags.Export))
        {
            var exportName = Utilities.HasSyntacticModifier(store, decl, ModifierFlags.Default) ? factory.CreateIdentifier("default") : factory.GetDeclarationName(decl);
            statements = AppendExportStatement(statements, seen, exportName, factory.GetLocalName(decl), location: decl);
        }

        if (!store.Name(decl).IsNone)
        {
            statements = AppendExportsOfDeclaration(statements, seen, decl);
        }

        return statements;
    }

    /// <summary>Appends the exports of a declaration to a statement list, returning the statement list.</summary>
    private List<Node>? AppendExportsOfDeclaration(List<Node>? statements, IdentifierNameMap<bool> seen, Node decl, bool liveBinding = false)
    {
        var name = factory.GetDeclarationName(decl);
        if (moduleInfo.ExportSpecifiers.TryGet(name, out var exportSpecifiers))
        {
            foreach (var exportSpecifier in exportSpecifiers)
            {
                statements = AppendExportStatement(statements, seen, store.Name(exportSpecifier), name, location: store.Name(exportSpecifier), allowComments: false, liveBinding);
            }
        }
        return statements;
    }

    /// <summary>Appends the down-level representation of an export to a statement list, returning the statement list.</summary>
    private List<Node>? AppendExportStatement(List<Node>? statements, IdentifierNameMap<bool> seen, Node exportName, Node expression, Node location = default, bool allowComments = false, bool liveBinding = false)
    {
        if (store.Kind(exportName) != SyntaxKind.StringLiteral)
        {
            if (seen.Has(exportName))
            {
                return statements;
            }
            seen.Set(exportName, true);
        }
        (statements ??= []).Add(CreateExportStatement(exportName, expression, location, allowComments, liveBinding));
        return statements;
    }

    private Node CreateUnderscoreUnderscoreESModule()
    {
        var statement = factory.CreateExpressionStatement(
            factory.CreateCallExpression(
                factory.CreatePropertyAccessExpression(factory.CreateIdentifier("Object"), factory.CreateIdentifier("defineProperty")),
                default,
                factory.CreateNodeArray(new[]
                {
                    factory.CreateIdentifier("exports"),
                    factory.CreateStringLiteral(store.Texts.Add("__esModule")),
                    factory.CreateObjectLiteralExpression(factory.CreateNodeArray(new[]
                    {
                        factory.CreatePropertyAssignment(factory.CreateIdentifier("value"), factory.CreateToken(SyntaxKind.TrueKeyword)),
                    })),
                })));
        FactoryEmitNode.SetEmitFlags(store, statement, EmitFlags.CustomPrologue);
        return statement;
    }

    /// <summary>Creates a call to the current file's export function to export a value.</summary>
    private Node CreateExportStatement(Node name, Node value, Node location = default, bool allowComments = false, bool liveBinding = false)
    {
        var statement = factory.CreateExpressionStatement(CreateExportExpression(name, value, location: default, liveBinding));
        if (!location.IsNone)
        {
            FactoryUtilitiesPublic.SetTextRange(store, statement, location);
        }
        FactoryEmitNode.SetStartsOnNewLine(store, statement, newLine: true);
        if (!allowComments)
        {
            FactoryEmitNode.SetEmitFlags(store, statement, EmitFlags.NoComments);
        }

        return statement;
    }

    /// <summary>Creates a call to the current file's export function to export a value.</summary>
    private Node CreateExportExpression(Node name, Node value, Node location = default, bool liveBinding = false)
    {
        Node expression;
        if (liveBinding)
        {
            expression = factory.CreateCallExpression(
                factory.CreatePropertyAccessExpression(
                    factory.CreateIdentifier("Object"),
                    factory.CreateIdentifier("defineProperty")),
                default,
                factory.CreateNodeArray(new[]
                {
                    factory.CreateIdentifier("exports"),
                    factory.CreateStringLiteralFromNode(name),
                    factory.CreateObjectLiteralExpression(factory.CreateNodeArray(new[]
                    {
                        factory.CreatePropertyAssignment(factory.CreateIdentifier("enumerable"), factory.CreateToken(SyntaxKind.TrueKeyword)),
                        factory.CreatePropertyAssignment(
                            factory.CreateIdentifier("get"),
                            factory.CreateFunctionExpression(
                                default,
                                default,
                                default,
                                default,
                                factory.CreateNodeArray(ReadOnlySpan<Node>.Empty),
                                default,
                                factory.CreateBlock(factory.CreateNodeArray(new[] { factory.CreateReturnStatement(value) })))),
                    })),
                }));
        }
        else
        {
            expression = factory.CreateAssignment(
                store.Kind(name) == SyntaxKind.StringLiteral
                    ? factory.CreateElementAccessExpression(
                        factory.CreateIdentifier("exports"),
                        factory.CloneNode(name))
                    : factory.CreatePropertyAccessExpression(
                        factory.CreateIdentifier("exports"),
                        factory.CloneNode(name)),
                value);
        }
        if (!location.IsNone)
        {
            FactoryUtilitiesPublic.SetTextRange(store, expression, location);
        }
        return expression;
    }

    //
    // Modifier Visitors
    //

    /// <summary>Visit nodes to elide module-specific modifiers.</summary>
    private VisitResult ModifierVisitor(Node node)
    {
        // Elide module-specific modifiers.
        switch (store.Kind(node))
        {
            case SyntaxKind.ExportKeyword:
            case SyntaxKind.DefaultKeyword:
                return VisitResult.None;
        }

        return node;
    }

    //
    // Emit Notification
    //

    /// <summary>Hook for node emit notifications.</summary>
    private void OnEmitNode(EmitHint hint, Node node, Action<EmitHint, Node> emitCallback)
    {
        if (store.Kind(node) == SyntaxKind.SourceFile)
        {
            currentSourceFile = node;
            currentModuleInfo = moduleInfoMap.GetValueOrDefault(OriginalKey(currentSourceFile));

            previousOnEmitNode(hint, node, emitCallback);

            currentSourceFile = Node.None;
            currentModuleInfo = null;
        }
        else
        {
            previousOnEmitNode(hint, node, emitCallback);
        }
    }

    //
    // Substitutions
    //

    /// <summary>Hooks node substitutions.</summary>
    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        node = previousOnSubstituteNode(hint, node);
        if (noSubstitution.Contains(node))
        {
            return node;
        }

        if (hint == EmitHint.Expression)
        {
            return SubstituteExpression(node);
        }
        else if (store.Kind(node) == SyntaxKind.ShorthandPropertyAssignment)
        {
            return SubstituteShorthandPropertyAssignment(node);
        }

        return node;
    }

    /// <summary>Substitution for a ShorthandPropertyAssignment whose declaration name is an imported or exported symbol.</summary>
    private Node SubstituteShorthandPropertyAssignment(Node node)
    {
        var name = store.Name(node);
        var exportedOrImportedName = SubstituteExpressionIdentifier(name);
        if (exportedOrImportedName != name)
        {
            // A shorthand property with an assignment initializer is probably part of a
            // destructuring assignment
            var objectAssignmentInitializer = store.ObjectAssignmentInitializer(node);
            if (!objectAssignmentInitializer.IsNone)
            {
                var initializer = factory.CreateAssignment(exportedOrImportedName, objectAssignmentInitializer);
                return FactoryUtilitiesPublic.SetTextRange(store, factory.CreatePropertyAssignment(name, initializer), node);
            }
            return FactoryUtilitiesPublic.SetTextRange(store, factory.CreatePropertyAssignment(name, exportedOrImportedName), node);
        }
        return node;
    }

    /// <summary>Substitution for an Expression that may contain an imported or exported symbol.</summary>
    private Node SubstituteExpression(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.Identifier:
                return SubstituteExpressionIdentifier(node);
            case SyntaxKind.CallExpression:
                return SubstituteCallExpression(node);
            case SyntaxKind.TaggedTemplateExpression:
                return SubstituteTaggedTemplateExpression(node);
            case SyntaxKind.BinaryExpression:
                return SubstituteBinaryExpression(node);
        }

        return node;
    }

    private Node SubstituteCallExpression(Node node)
    {
        var callee = store.Expression(node);
        if (store.Kind(callee) == SyntaxKind.Identifier)
        {
            var expression = SubstituteExpressionIdentifier(callee);
            noSubstitution.Add(expression);
            if (store.Kind(expression) != SyntaxKind.Identifier && (Utilities.GetEmitFlags(store, callee) & EmitFlags.HelperName) == 0)
            {
                return FactoryEmitNode.AddInternalEmitFlags(
                    store,
                    factory.UpdateCallExpression(node, expression, default, store.Arguments(node)),
                    InternalEmitFlags.IndirectCall);
            }
        }
        return node;
    }

    private Node SubstituteTaggedTemplateExpression(Node node)
    {
        var tagNode = store.Tag(node);
        if (store.Kind(tagNode) == SyntaxKind.Identifier)
        {
            var tag = SubstituteExpressionIdentifier(tagNode);
            noSubstitution.Add(tag);
            if (store.Kind(tag) != SyntaxKind.Identifier && (Utilities.GetEmitFlags(store, tagNode) & EmitFlags.HelperName) == 0)
            {
                return FactoryEmitNode.AddInternalEmitFlags(
                    store,
                    factory.UpdateTaggedTemplateExpression(node, tag, default, store.Template(node)),
                    InternalEmitFlags.IndirectCall);
            }
        }
        return node;
    }

    /// <summary>Substitution for an Identifier expression that may contain an imported or exported symbol.</summary>
    private Node SubstituteExpressionIdentifier(Node node)
    {
        if ((Utilities.GetEmitFlags(store, node) & EmitFlags.HelperName) != 0)
        {
            // (getExternalHelpersModuleName: none without importHelpers.)
            return node;
        }
        else if (!(UtilitiesPublic.IsGeneratedIdentifier(store, node) && (FactoryEmitNode.GetIdentifierAutoGenerate(store, node)!.Flags & GeneratedIdentifierFlags.AllowNameSubstitution) == 0) &&
            !FactoryUtilities.IsLocalName(store, node))
        {
            var exportContainer = resolver.GetReferencedExportContainer(node, IsExportName(node));
            if (!exportContainer.IsNone && store.Kind(exportContainer) == SyntaxKind.SourceFile)
            {
                return FactoryUtilitiesPublic.SetTextRange(
                    store,
                    factory.CreatePropertyAccessExpression(
                        factory.CreateIdentifier("exports"),
                        factory.CloneNode(node)),
                    node);
            }
            var importDeclaration = resolver.NodeInThisFile(resolver.GetReferencedImportDeclaration(node));
            if (!importDeclaration.IsNone)
            {
                if (store.Kind(importDeclaration) == SyntaxKind.ImportClause)
                {
                    return FactoryUtilitiesPublic.SetTextRange(
                        store,
                        factory.CreatePropertyAccessExpression(
                            factory.GetGeneratedNameForNode(store.Parent(importDeclaration)),
                            factory.CreateIdentifier("default")),
                        node);
                }
                else if (store.Kind(importDeclaration) == SyntaxKind.ImportSpecifier)
                {
                    var name = !store.PropertyName(importDeclaration).IsNone ? store.PropertyName(importDeclaration) : store.Name(importDeclaration);
                    var importDecl = store.Parent(store.Parent(store.Parent(importDeclaration)));
                    var target = factory.GetGeneratedNameForNode(!importDecl.IsNone ? importDecl : importDeclaration);
                    return FactoryUtilitiesPublic.SetTextRange(
                        store,
                        store.Kind(name) == SyntaxKind.StringLiteral
                            ? factory.CreateElementAccessExpression(target, factory.CloneNode(name))
                            : factory.CreatePropertyAccessExpression(target, factory.CloneNode(name)),
                        node);
                }
            }
        }
        return node;
    }

    /// <summary>Substitution for a BinaryExpression that may contain an imported or exported symbol.</summary>
    private Node SubstituteBinaryExpression(Node node)
    {
        // When we see an assignment expression whose left-hand side is an exported symbol,
        // we should ensure all exports of that symbol are updated with the correct value.
        //
        // - We do not substitute generated identifiers unless they are file-level reserved names.
        // - We do not substitute identifiers tagged with the LocalName flag.
        // - We only substitute identifiers that are exported at the top level.
        var left = store.Left(node);
        if (Utilities.IsAssignmentOperator(store.Kind(store.OperatorToken(node)))
            && store.Kind(left) == SyntaxKind.Identifier
            && (!UtilitiesPublic.IsGeneratedIdentifier(store, left) || IsFileLevelReservedGeneratedIdentifier(left))
            && !FactoryUtilities.IsLocalName(store, left))
        {
            var exportedNames = GetExports(left);
            if (exportedNames is not null)
            {
                // For each additional export of the declaration, apply an export assignment.
                var expression = node;
                foreach (var exportName in exportedNames)
                {
                    // Mark the node to prevent triggering this rule again.
                    noSubstitution.Add(expression);
                    expression = CreateExportExpression(exportName, expression, location: node);
                }

                return expression;
            }
        }

        return node;
    }

    /// <summary>Gets the additional exports of a name.</summary>
    private List<Node>? GetExports(Node name)
    {
        if (!UtilitiesPublic.IsGeneratedIdentifier(store, name))
        {
            var importDeclaration = resolver.NodeInThisFile(resolver.GetReferencedImportDeclaration(name));
            if (!importDeclaration.IsNone)
            {
                return currentModuleInfo?.ExportedBindings.GetValueOrDefault(OriginalKey(importDeclaration));
            }

            // An exported namespace or enum may merge with an ambient declaration, which won't show up in .js emit, so
            // we analyze all value exports of a symbol.
            var declarations = resolver.GetReferencedValueDeclarations(name);
            if (declarations is not null)
            {
                List<Node>? bindingsSet = null;
                foreach (var declarationRef in declarations)
                {
                    var declaration = resolver.NodeInThisFile(declarationRef);
                    if (declaration.IsNone)
                    {
                        continue;
                    }
                    if (currentModuleInfo?.ExportedBindings.GetValueOrDefault(OriginalKey(declaration)) is { } bindings)
                    {
                        foreach (var binding in bindings)
                        {
                            bindingsSet ??= [];
                            if (!bindingsSet.Contains(binding))
                            {
                                bindingsSet.Add(binding);
                            }
                        }
                    }
                }
                if (bindingsSet is not null)
                {
                    return bindingsSet;
                }
            }
        }
        else if (IsFileLevelReservedGeneratedIdentifier(name))
        {
            if (currentModuleInfo is not null && currentModuleInfo.ExportSpecifiers.TryGet(name, out var exportSpecifiers))
            {
                var exportedNames = new List<Node>();
                foreach (var exportSpecifier in exportSpecifiers)
                {
                    exportedNames.Add(store.Name(exportSpecifier));
                }
                return exportedNames;
            }
        }
        return null;
    }

    // utilitiesPublic.ts isFileLevelReservedGeneratedIdentifier
    private bool IsFileLevelReservedGeneratedIdentifier(Node node)
    {
        var flags = FactoryEmitNode.GetIdentifierAutoGenerate(store, node)!.Flags;
        return (flags & GeneratedIdentifierFlags.FileLevel) != 0
            && (flags & GeneratedIdentifierFlags.Optimistic) != 0
            && (flags & GeneratedIdentifierFlags.ReservedInNestedScopes) != 0;
    }

    // factory/utilities.ts isExportName
    private bool IsExportName(Node node) => (Utilities.GetEmitFlags(store, node) & EmitFlags.ExportName) != 0;

    //
    // transformers/utilities.ts and factory/utilities.ts
    //

    // utilities.ts moduleExportNameIsDefault
    private bool ModuleExportNameIsDefault(Node node)
    {
        var text = store.Kind(node) == SyntaxKind.StringLiteral ? store.Text(node) : store.EscapedText(node);
        return store.Texts.Equals(text, "default");
    }

    // utilities.ts moduleExportNameTextUnescaped
    private string ModuleExportNameTextUnescaped(Node node)
    {
        return store.Kind(node) == SyntaxKind.StringLiteral ? store.Texts.GetString(store.Text(node))! : store.Texts.GetString(UtilitiesPublic.IdText(store, node))!;
    }

    // utilities.ts getNamespaceDeclarationNode
    private Node GetNamespaceDeclarationNode(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ImportDeclaration:
            {
                var importClause = store.ImportClause(node);
                var namedBindings = importClause.IsNone ? Node.None : store.NamedBindings(importClause);
                return !namedBindings.IsNone && store.Kind(namedBindings) == SyntaxKind.NamespaceImport ? namedBindings : Node.None;
            }
            case SyntaxKind.ImportEqualsDeclaration:
                return node;
            case SyntaxKind.ExportDeclaration:
            {
                var exportClause = store.ExportClause(node);
                return !exportClause.IsNone && store.Kind(exportClause) == SyntaxKind.NamespaceExport ? exportClause : Node.None;
            }
        }
        return Node.None;
    }

    // utilities.ts isDefaultImport
    private bool IsDefaultImport(Node node)
    {
        return store.Kind(node) == SyntaxKind.ImportDeclaration && !store.ImportClause(node).IsNone && !store.Name(store.ImportClause(node)).IsNone;
    }

    // transformers/utilities.ts containsDefaultReference
    private bool ContainsDefaultReference(Node node)
    {
        if (node.IsNone || store.Kind(node) is not (SyntaxKind.NamedImports or SyntaxKind.NamedExports))
        {
            return false;
        }
        foreach (var element in store.Nodes(store.Elements(node)))
        {
            if (IsNamedDefaultReference(element))
            {
                return true;
            }
        }
        return false;
    }

    // transformers/utilities.ts isNamedDefaultReference
    private bool IsNamedDefaultReference(Node e)
    {
        return ModuleExportNameIsDefault(!store.PropertyName(e).IsNone ? store.PropertyName(e) : store.Name(e));
    }

    // transformers/utilities.ts getExportNeedsImportStarHelper
    private bool GetExportNeedsImportStarHelper(Node node) => !GetNamespaceDeclarationNode(node).IsNone;

    // transformers/utilities.ts getImportNeedsImportStarHelper
    private bool GetImportNeedsImportStarHelper(Node node)
    {
        if (!GetNamespaceDeclarationNode(node).IsNone)
        {
            return true;
        }
        var importClause = store.ImportClause(node);
        var bindings = importClause.IsNone ? Node.None : store.NamedBindings(importClause);
        if (bindings.IsNone)
        {
            return false;
        }
        if (store.Kind(bindings) != SyntaxKind.NamedImports)
        {
            return false;
        }
        var defaultRefCount = 0;
        var elements = store.Nodes(store.Elements(bindings));
        foreach (var binding in elements)
        {
            if (IsNamedDefaultReference(binding))
            {
                defaultRefCount++;
            }
        }
        // Import star is required if there's default named refs mixed with non-default refs, or if theres non-default refs and it has a default import
        return (defaultRefCount > 0 && defaultRefCount != elements.Length) || ((elements.Length - defaultRefCount) != 0 && IsDefaultImport(node));
    }

    // transformers/utilities.ts getImportNeedsImportDefaultHelper
    private bool GetImportNeedsImportDefaultHelper(Node node)
    {
        // Import default is needed if there's a default import or a default ref and no other refs (meaning an import star helper wasn't requested)
        var importClause = store.ImportClause(node);
        return !GetImportNeedsImportStarHelper(node) && (IsDefaultImport(node) ||
            (!importClause.IsNone && !store.NamedBindings(importClause).IsNone && store.Kind(store.NamedBindings(importClause)) == SyntaxKind.NamedImports && ContainsDefaultReference(store.NamedBindings(importClause))));
    }

    // utilities.ts getExternalModuleName
    private Node GetExternalModuleName(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ExportDeclaration:
                return store.ModuleSpecifier(node);
            case SyntaxKind.ImportEqualsDeclaration:
                return store.Kind(store.ModuleReference(node)) == SyntaxKind.ExternalModuleReference ? store.Expression(store.ModuleReference(node)) : Node.None;
            case SyntaxKind.CallExpression:
            {
                var arguments = store.Arguments(node);
                return store.Length(arguments) > 0 ? store.Element(arguments, 0) : Node.None;
            }
        }
        return Node.None;
    }

    /// <summary>
    /// factory/utilities.ts getExternalModuleNameLiteral: the module name as a string literal. (tryGetModuleNameFromFile:
    /// a resolved file has no `moduleName` without AMD module directives and there is no outFile; tryRenameExternalModule:
    /// no renamed dependencies.)
    /// </summary>
    private Node GetExternalModuleNameLiteral(Node importNode)
    {
        var moduleName = GetExternalModuleName(importNode);
        if (!moduleName.IsNone && store.Kind(moduleName) == SyntaxKind.StringLiteral)
        {
            return factory.CloneNode(moduleName);
        }
        return Node.None;
    }

    //
    // collectExternalModuleInfo
    //

    private static void MultiMapAdd(Dictionary<Node, List<Node>> map, Node key, Node value)
    {
        ref var values = ref CollectionsMarshal.GetValueRefOrAddDefault(map, key, out _);
        (values ??= []).Add(value);
    }

    /// <summary>transformers/utilities.ts collectExternalModuleInfo.</summary>
    private ExternalModuleInfo CollectExternalModuleInfo(Node sourceFile)
    {
        var externalImports = new List<Node>();
        var exportSpecifiers = new IdentifierNameMultiMap<Node>(store);
        var exportedBindings = new Dictionary<Node, List<Node>>();
        var uniqueExports = new HashSet<string>(StringComparer.Ordinal);
        var exportedFunctions = new List<Node>();
        var exportedFunctionsSet = new HashSet<Node>();
        List<Node>? exportedNames = null;
        var hasExportDefault = false;
        var exportEquals = Node.None;
        var hasExportStarsToExportValues = false;

        foreach (var node in store.Nodes(store.Statements(sourceFile)))
        {
            switch (store.Kind(node))
            {
                case SyntaxKind.ImportDeclaration:
                    // import "mod"
                    // import x from "mod"
                    // import * as x from "mod"
                    // import { x, y } from "mod"
                    // (hasImportStar, hasImportDefault: read only by createExternalHelpersImportDeclarationIfNeeded.)
                    externalImports.Add(node);
                    break;

                case SyntaxKind.ImportEqualsDeclaration:
                    if (store.Kind(store.ModuleReference(node)) == SyntaxKind.ExternalModuleReference)
                    {
                        // import x = require("mod")
                        externalImports.Add(node);
                    }

                    break;

                case SyntaxKind.ExportDeclaration:
                    if (!store.ModuleSpecifier(node).IsNone)
                    {
                        var exportClause = store.ExportClause(node);
                        if (exportClause.IsNone)
                        {
                            // export * from "mod"
                            externalImports.Add(node);
                            hasExportStarsToExportValues = true;
                        }
                        else
                        {
                            // export * as ns from "mod"
                            // export { x, y } from "mod"
                            externalImports.Add(node);
                            if (store.Kind(exportClause) == SyntaxKind.NamedExports)
                            {
                                AddExportedNamesForExportDeclaration(node);
                            }
                            else
                            {
                                var name = store.Name(exportClause);
                                var nameText = ModuleExportNameTextUnescaped(name);
                                if (uniqueExports.Add(nameText))
                                {
                                    MultiMapAdd(exportedBindings, OriginalKey(node), name);
                                    (exportedNames ??= []).Add(name);
                                }
                            }
                        }
                    }
                    else
                    {
                        // export { x, y }
                        AddExportedNamesForExportDeclaration(node);
                    }
                    break;

                case SyntaxKind.ExportAssignment:
                    if (store.IsExportEquals(node) && exportEquals.IsNone)
                    {
                        // export = x
                        exportEquals = node;
                    }
                    break;

                case SyntaxKind.VariableStatement:
                    if (Utilities.HasSyntacticModifier(store, node, ModifierFlags.Export))
                    {
                        foreach (var decl in store.Nodes(store.Declarations(store.DeclarationList(node))))
                        {
                            CollectExportedVariableInfo(decl);
                        }
                    }
                    break;

                case SyntaxKind.FunctionDeclaration:
                    if (Utilities.HasSyntacticModifier(store, node, ModifierFlags.Export))
                    {
                        AddExportedFunctionDeclaration(node, Node.None, Utilities.HasSyntacticModifier(store, node, ModifierFlags.Default));
                    }
                    break;

                case SyntaxKind.ClassDeclaration:
                    if (Utilities.HasSyntacticModifier(store, node, ModifierFlags.Export))
                    {
                        if (Utilities.HasSyntacticModifier(store, node, ModifierFlags.Default))
                        {
                            // export default class { }
                            if (!hasExportDefault)
                            {
                                MultiMapAdd(exportedBindings, OriginalKey(node), factory.GetDeclarationName(node));
                                hasExportDefault = true;
                            }
                        }
                        else
                        {
                            // export class x { }
                            var name = store.Name(node);
                            if (!name.IsNone && uniqueExports.Add(store.Texts.GetString(UtilitiesPublic.IdText(store, name))!))
                            {
                                MultiMapAdd(exportedBindings, OriginalKey(node), name);
                                (exportedNames ??= []).Add(name);
                            }
                        }
                    }
                    break;
            }
        }

        // (createExternalHelpersImportDeclarationIfNeeded: undefined without importHelpers.)

        return new ExternalModuleInfo
        {
            ExternalImports = externalImports,
            ExportSpecifiers = exportSpecifiers,
            ExportEquals = exportEquals,
            HasExportStarsToExportValues = hasExportStarsToExportValues,
            ExportedBindings = exportedBindings,
            ExportedNames = exportedNames,
            ExportedFunctions = exportedFunctions,
        };

        void AddExportedNamesForExportDeclaration(Node node)
        {
            foreach (var specifier in store.Nodes(store.Elements(store.ExportClause(node))))
            {
                var specifierName = store.Name(specifier);
                var specifierNameText = ModuleExportNameTextUnescaped(specifierName);
                if (!uniqueExports.Contains(specifierNameText))
                {
                    var name = !store.PropertyName(specifier).IsNone ? store.PropertyName(specifier) : specifierName;
                    if (store.Kind(name) != SyntaxKind.StringLiteral)
                    {
                        if (store.ModuleSpecifier(node).IsNone)
                        {
                            exportSpecifiers.Add(name, specifier);
                        }

                        var importDecl = resolver.NodeInThisFile(resolver.GetReferencedImportDeclaration(name));
                        var decl = !importDecl.IsNone ? importDecl : resolver.NodeInThisFile(resolver.GetReferencedValueDeclaration(name));

                        if (!decl.IsNone)
                        {
                            if (store.Kind(decl) == SyntaxKind.FunctionDeclaration)
                            {
                                AddExportedFunctionDeclaration(decl, specifierName, ModuleExportNameIsDefault(specifierName));
                                continue;
                            }

                            MultiMapAdd(exportedBindings, OriginalKey(decl), specifierName);
                        }
                    }

                    uniqueExports.Add(specifierNameText);
                    (exportedNames ??= []).Add(specifierName);
                }
            }
        }

        void AddExportedFunctionDeclaration(Node node, Node name, bool isDefault)
        {
            var original = TransformerUtilities.GetOriginalNode(store, node, n => store.Kind(n) == SyntaxKind.FunctionDeclaration);
            if (exportedFunctionsSet.Add(original))
            {
                exportedFunctions.Add(original);
            }
            if (isDefault)
            {
                // export default function() { }
                // function x() { } + export { x as default };
                if (!hasExportDefault)
                {
                    MultiMapAdd(exportedBindings, OriginalKey(node), !name.IsNone ? name : factory.GetDeclarationName(node));
                    hasExportDefault = true;
                }
            }
            else
            {
                // export function x() { }
                // function x() { } + export { x }
                if (name.IsNone)
                {
                    name = store.Name(node);
                }
                var nameText = ModuleExportNameTextUnescaped(name);
                if (uniqueExports.Add(nameText))
                {
                    MultiMapAdd(exportedBindings, OriginalKey(node), name);
                }
            }
        }

        void CollectExportedVariableInfo(Node decl)
        {
            var name = store.Name(decl);
            if (store.Kind(name) is SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern)
            {
                foreach (var element in store.Nodes(store.Elements(name)))
                {
                    if (store.Kind(element) != SyntaxKind.OmittedExpression)
                    {
                        CollectExportedVariableInfo(element);
                    }
                }
            }
            else if (!UtilitiesPublic.IsGeneratedIdentifier(store, name))
            {
                var text = store.Texts.GetString(UtilitiesPublic.IdText(store, name))!;
                if (uniqueExports.Add(text))
                {
                    (exportedNames ??= []).Add(name);
                    if (FactoryUtilities.IsLocalName(store, name))
                    {
                        MultiMapAdd(exportedBindings, OriginalKey(decl), name);
                    }
                }
            }
        }
    }
}
