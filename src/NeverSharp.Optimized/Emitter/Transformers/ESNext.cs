// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/transformers/esnext.ts (transformESNext)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/transformers/estransforms/using.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D3. `using` and `await using` declarations (target ES2025 is
// below ESNext): the __addDisposableResource / __disposeResources helpers with the env object, the hoisting of
// top-level declarations around the `try` block, `using` in `for` and `for...of`, and the exports at module top level.
// The assignment-pattern converter (a destructured top-level variable after a top-level `using`) and the class form of
// named evaluation are module D4's and throw.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>esnext.ts transformESNext: the closure of the transformer is the instance.</summary>
internal sealed class ESNextTransformer
{
    private enum UsingKind
    {
        None,
        Sync,
        Async,
    }

    private readonly TransformationContext context;
    private readonly NodeFactory factory;
    private readonly Func<Node, VisitResult> visitor;

    private IdentifierNameMap<Node>? exportBindings;
    private List<Node>? exportVars;
    private Node defaultExportBinding;
    private Node exportEqualsBinding;

    private ESNextTransformer(TransformationContext context)
    {
        this.context = context;
        factory = context.Factory;
        visitor = Visitor;
        defaultExportBinding = Node.None;
        exportEqualsBinding = Node.None;
    }

    private NodeStore store => context.Store;

    /// <summary>transformESNext(context).</summary>
    internal static Func<Node, Node> TransformESNext(TransformationContext context) => new ESNextTransformer(context).TransformSourceFile;

    private Node TransformSourceFile(Node node)
    {
        if (store.IsDeclarationFile(node))
        {
            return node;
        }

        var visited = Visitors.VisitNode(factory, node, visitor);
        FactoryEmitNode.AddEmitHelpers(store, visited, context.ReadEmitHelpers());
        exportVars = null;
        exportBindings = null;
        defaultExportBinding = Node.None;
        return visited;
    }

    private VisitResult Visitor(Node node)
    {
        if ((store.TransformFlags(node) & TransformFlags.ContainsESNext) == 0)
        {
            return node;
        }

        switch (store.Kind(node))
        {
            case SyntaxKind.SourceFile:
                return VisitSourceFile(node);

            case SyntaxKind.Block:
                return VisitBlock(node);

            case SyntaxKind.ForStatement:
                return VisitForStatement(node);

            case SyntaxKind.ForOfStatement:
                return VisitForOfStatement(node);

            default:
                return Visitors.VisitEachChild(node, visitor, context);
        }
    }

    // visitArray(nodes, visitor, isStatement, start, count) appended to a list (addRange).
    private void AddVisited(List<Node> target, NodeList nodes, int start, int count)
    {
        for (var i = start; i < start + count; i++)
        {
            var visited = visitor(store.Element(nodes, i));
            if (visited.Nodes is not null)
            {
                target.AddRange(visited.Nodes);
            }
            else if (!visited.Node.IsNone)
            {
                target.Add(visited.Node);
            }
        }
    }

    private Node VisitSourceFile(Node node)
    {
        var statements = store.Statements(node);
        var usingKind = GetUsingKindOfStatements(statements);
        if (usingKind != UsingKind.None)
        {
            // Imports and exports must stay at the top level. This means we must hoist all imports, exports, and
            // top-level function declarations and bindings out of the `try` statements we generate. For example:
            //
            // given:
            //
            //  import { w } from "mod";
            //  const x = expr1;
            //  using y = expr2;
            //  const z = expr3;
            //  export function f() {
            //    console.log(z);
            //  }
            //
            // produces:
            //
            //  import { x } from "mod";        // <-- preserved
            //  const x = expr1;                // <-- preserved
            //  var y, z;                       // <-- hoisted
            //  export function f() {           // <-- hoisted
            //    console.log(z);
            //  }
            //  const env_1 = { stack: [], error: void 0, hasError: false };
            //  try {
            //    y = __addDisposableResource(env_1, expr2, false);
            //    z = expr3;
            //  }
            //  catch (e_1) {
            //    env_1.error = e_1;
            //    env_1.hasError = true;
            //  }
            //  finally {
            //    __disposeResource(env_1);
            //  }
            //
            // In this transformation, we hoist `y`, `z`, and `f` to a new outer statement list while moving all other
            // statements in the source file into the `try` block, which is the same approach we use for System module
            // emit. Unlike System module emit, we attempt to preserve all statements prior to the first top-level
            // `using` to isolate the complexity of the transformed output to only where it is necessary.
            context.StartLexicalEnvironment();

            exportBindings = new IdentifierNameMap<Node>(store);
            exportVars = [];

            var prologueCount = CountPrologueStatements(statements);
            var topLevelStatements = new List<Node>();
            AddVisited(topLevelStatements, statements, 0, prologueCount);

            // Collect and transform any leading statements up to the first `using` or `await using`. This preserves
            // the original statement order much as is possible.
            var length = store.Length(statements);
            var pos = prologueCount;
            while (pos < length)
            {
                var statement = store.Element(statements, pos);
                if (GetUsingKind(statement) != UsingKind.None)
                {
                    if (pos > prologueCount)
                    {
                        AddVisited(topLevelStatements, statements, prologueCount, pos - prologueCount);
                    }
                    break;
                }
                pos++;
            }

            Debug.Assert(pos < length, "Should have encountered at least one 'using' statement.");

            // transform the rest of the body
            var envBinding = CreateEnvBinding();
            var bodyStatements = TransformUsingDeclarations(statements, pos, length, envBinding, topLevelStatements);

            // add `export {}` declarations for any hoisted bindings.
            if (exportBindings.Count > 0)
            {
                topLevelStatements.Add(
                    factory.CreateExportDeclaration(
                        default,
                        isTypeOnly: false,
                        factory.CreateNamedExports(factory.CreateNodeArray(exportBindings.Values))));
            }

            var hoisted = context.EndLexicalEnvironment();
            if (hoisted is not null)
            {
                topLevelStatements.AddRange(hoisted);
            }
            if (exportVars.Count > 0)
            {
                topLevelStatements.Add(factory.CreateVariableStatement(
                    factory.CreateNodeArray(new[] { factory.CreateToken(SyntaxKind.ExportKeyword) }),
                    factory.CreateVariableDeclarationList(
                        factory.CreateNodeArray(exportVars),
                        NodeFlags.Let)));
            }
            topLevelStatements.AddRange(CreateDownlevelUsingStatements(bodyStatements, envBinding, usingKind == UsingKind.Async));

            if (!exportEqualsBinding.IsNone)
            {
                topLevelStatements.Add(factory.CreateExportAssignment(
                    default,
                    isExportEquals: true,
                    exportEqualsBinding));
            }

            return factory.UpdateSourceFile(node, factory.CreateNodeArray(topLevelStatements));
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitBlock(Node node)
    {
        var statements = store.Statements(node);
        var usingKind = GetUsingKindOfStatements(statements);
        if (usingKind != UsingKind.None)
        {
            var prologueCount = CountPrologueStatements(statements);
            var envBinding = CreateEnvBinding();
            var updated = new List<Node>();
            AddVisited(updated, statements, 0, prologueCount);
            updated.AddRange(CreateDownlevelUsingStatements(
                TransformUsingDeclarations(statements, prologueCount, store.Length(statements), envBinding, topLevelStatements: null),
                envBinding,
                usingKind == UsingKind.Async));
            return factory.UpdateBlock(node, factory.CreateNodeArray(updated));
        }
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitForStatement(Node node)
    {
        var initializer = store.Initializer(node);
        if (!initializer.IsNone && IsUsingVariableDeclarationList(initializer))
        {
            // given:
            //
            //  for (using x = expr; cond; incr) { ... }
            //
            // produces a shallow transformation to:
            //
            //  {
            //    using x = expr;
            //    for (; cond; incr) { ... }
            //  }
            //
            // before handing the shallow transformation back to the visitor for an in-depth transformation.
            return Visitors.VisitNode(
                factory,
                factory.CreateBlock(factory.CreateNodeArray(new[]
                {
                    factory.CreateVariableStatement(default, initializer),
                    factory.UpdateForStatement(
                        node,
                        default,
                        store.Condition(node),
                        store.Incrementor(node),
                        store.Statement(node)),
                })),
                visitor);
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitForOfStatement(Node node)
    {
        var forInitializer = store.Initializer(node);
        if (IsUsingVariableDeclarationList(forInitializer))
        {
            // given:
            //
            //  for (using x of y) { ... }
            //
            // produces a shallow transformation to:
            //
            //  for (const x_1 of y) {
            //    using x = x;
            //    ...
            //  }
            //
            // before handing the shallow transformation back to the visitor for an in-depth transformation.
            var declarations = store.Declarations(forInitializer);
            var forDecl = store.Length(declarations) > 0 ? store.Element(declarations, 0) : factory.CreateVariableDeclaration(factory.CreateTempVariable(null), default, default, default);

            var isAwaitUsing = GetUsingKindOfVariableDeclarationList(forInitializer) == UsingKind.Async;
            var temp = factory.GetGeneratedNameForNode(store.Name(forDecl));
            var usingVar = factory.UpdateVariableDeclaration(forDecl, store.Name(forDecl), default, default, temp);
            var usingVarList = factory.CreateVariableDeclarationList(factory.CreateNodeArray(new[] { usingVar }), isAwaitUsing ? NodeFlags.AwaitUsing : NodeFlags.Using);
            var usingVarStatement = factory.CreateVariableStatement(default, usingVarList);
            var statement = store.Statement(node);
            Node body;
            if (store.Kind(statement) == SyntaxKind.Block)
            {
                var bodyStatements = new List<Node> { usingVarStatement };
                bodyStatements.AddRange(store.Nodes(store.Statements(statement)).ToArray());
                body = factory.UpdateBlock(statement, factory.CreateNodeArray(bodyStatements));
            }
            else
            {
                body = factory.CreateBlock(factory.CreateNodeArray(new[] { usingVarStatement, statement }), multiLine: true);
            }
            return Visitors.VisitNode(
                factory,
                factory.UpdateForOfStatement(
                    node,
                    store.AwaitModifier(node),
                    factory.CreateVariableDeclarationList(factory.CreateNodeArray(new[]
                    {
                        factory.CreateVariableDeclaration(temp, default, default, default),
                    }), NodeFlags.Const),
                    store.Expression(node),
                    body),
                visitor);
        }
        return Visitors.VisitEachChild(node, visitor, context);
    }

    /// <summary>Transform `using` declarations in a statement list.</summary>
    private List<Node> TransformUsingDeclarations(NodeList statementsIn, int start, int end, Node envBinding, List<Node>? topLevelStatements)
    {
        var statements = new List<Node>();

        for (var i = start; i < end; i++)
        {
            var statement = store.Element(statementsIn, i);
            var usingKind = GetUsingKind(statement);
            if (usingKind != UsingKind.None)
            {
                Debug.Assert(store.Kind(statement) == SyntaxKind.VariableStatement);
                var declarations = new List<Node>();
                var declarationList = store.DeclarationList(statement);
                foreach (var d in store.Nodes(store.Declarations(declarationList)))
                {
                    var declaration = d;
                    if (store.Kind(store.Name(declaration)) != SyntaxKind.Identifier)
                    {
                        // Since binding patterns are a grammar error, we reset `declarations` so we don't process this as a `using`.
                        declarations.Clear();
                        break;
                    }

                    // perform a shallow transform for any named evaluation
                    if (NamedEvaluation.IsNamedEvaluation(store, declaration))
                    {
                        declaration = NamedEvaluation.TransformNamedEvaluation(context, declaration);
                    }

                    var initializer = Visitors.VisitNode(factory, store.Initializer(declaration), visitor);
                    if (initializer.IsNone)
                    {
                        initializer = factory.CreateVoidZero();
                    }
                    declarations.Add(factory.UpdateVariableDeclaration(
                        declaration,
                        store.Name(declaration),
                        default,
                        default,
                        context.GetEmitHelperFactory().CreateAddDisposableResourceHelper(
                            envBinding,
                            initializer,
                            usingKind == UsingKind.Async)));
                }

                // Only replace the statement if it was valid.
                if (declarations.Count > 0)
                {
                    var varList = factory.CreateVariableDeclarationList(factory.CreateNodeArray(declarations), NodeFlags.Const);
                    NodeFactory.SetOriginalNode(store, varList, declarationList);
                    FactoryUtilitiesPublic.SetTextRange(store, varList, declarationList);
                    HoistOrAppendNode(factory.UpdateVariableStatement(statement, default, varList));
                    continue;
                }
            }

            var result = visitor(statement);
            if (result.Nodes is not null)
            {
                foreach (var n in result.Nodes)
                {
                    HoistOrAppendNode(n);
                }
            }
            else if (!result.Node.IsNone)
            {
                HoistOrAppendNode(result.Node);
            }
        }

        return statements;

        void HoistOrAppendNode(Node node)
        {
            var hoisted = Hoist(node);
            if (!hoisted.IsNone)
            {
                statements.Add(hoisted);
            }
        }

        Node Hoist(Node node)
        {
            if (topLevelStatements is null)
            {
                return node;
            }

            switch (store.Kind(node))
            {
                case SyntaxKind.ImportDeclaration:
                case SyntaxKind.ImportEqualsDeclaration:
                case SyntaxKind.ExportDeclaration:
                case SyntaxKind.FunctionDeclaration:
                    return HoistImportOrExportOrHoistedDeclaration(node, topLevelStatements);
                case SyntaxKind.ExportAssignment:
                    return HoistExportAssignment(node);
                case SyntaxKind.ClassDeclaration:
                    return HoistClassDeclaration(node);
                case SyntaxKind.VariableStatement:
                    return HoistVariableStatement(node);
            }

            return node;
        }
    }

    private static Node HoistImportOrExportOrHoistedDeclaration(Node node, List<Node> topLevelStatements)
    {
        // NOTE: `node` has already been visited
        topLevelStatements.Add(node);
        return Node.None;
    }

    private Node HoistExportAssignment(Node node)
    {
        return store.IsExportEquals(node) ? HoistExportEquals(node) : HoistExportDefault(node);
    }

    private Node CreateDefaultBinding()
    {
        return factory.CreateUniqueName("_default", GeneratedIdentifierFlags.ReservedInNestedScopes | GeneratedIdentifierFlags.FileLevel | GeneratedIdentifierFlags.Optimistic);
    }

    private Node HoistExportDefault(Node node)
    {
        // NOTE: `node` has already been visited
        if (!defaultExportBinding.IsNone)
        {
            // invalid case of multiple `export default` declarations. Don't assert here, just pass it through
            return node;
        }

        // given:
        //
        //   export default expr;
        //
        // produces:
        //
        //   // top level
        //   var default_1;
        //   export { default_1 as default };
        //
        //   // body
        //   default_1 = expr;

        defaultExportBinding = CreateDefaultBinding();
        HoistBindingIdentifier(defaultExportBinding, isExport: true, "default", node);

        // give a class or function expression an assigned name, if needed.
        var expression = store.Expression(node);
        var innerExpression = FactoryUtilities.SkipOuterExpressions(store, expression);
        if (NamedEvaluation.IsNamedEvaluation(store, innerExpression))
        {
            innerExpression = NamedEvaluation.TransformNamedEvaluation(context, innerExpression, ignoreEmptyStringLiteral: false, "default");
            expression = factory.RestoreOuterExpressions(expression, innerExpression);
        }

        var assignment = factory.CreateAssignment(defaultExportBinding, expression);
        return factory.CreateExpressionStatement(assignment);
    }

    private Node HoistExportEquals(Node node)
    {
        // NOTE: `node` has already been visited
        if (!exportEqualsBinding.IsNone)
        {
            // invalid case of multiple `export default` declarations. Don't assert here, just pass it through
            return node;
        }

        // given:
        //
        //   export = expr;
        //
        // produces:
        //
        //   // top level
        //   var default_1;
        //
        //   try {
        //       // body
        //       default_1 = expr;
        //   } ...
        //
        //   // top level suffix
        //   export = default_1;

        exportEqualsBinding = CreateDefaultBinding();
        context.HoistVariableDeclaration(exportEqualsBinding);

        // give a class or function expression an assigned name, if needed.
        var assignment = factory.CreateAssignment(exportEqualsBinding, store.Expression(node));
        return factory.CreateExpressionStatement(assignment);
    }

    private Node HoistClassDeclaration(Node node)
    {
        // NOTE: `node` has already been visited
        if (store.Name(node).IsNone && !defaultExportBinding.IsNone)
        {
            // invalid case of multiple `export default` declarations. Don't assert here, just pass it through
            return node;
        }

        var isExported = Utilities.HasSyntacticModifier(store, node, ModifierFlags.Export);
        var isDefault = Utilities.HasSyntacticModifier(store, node, ModifierFlags.Default);

        // When hoisting a class declaration at the top level of a file containing a top-level `using` statement, we
        // must first convert it to a class expression so that we can hoist the binding outside of the `try`.
        var expression = factory.Converters.ConvertToClassExpression(node);
        if (!store.Name(node).IsNone)
        {
            // given:
            //
            //  using x = expr;
            //  class C {}
            //
            // produces:
            //
            //  var x, C;
            //  const env_1 = { ... };
            //  try {
            //    x = __addDisposableResource(env_1, expr, false);
            //    C = class {};
            //  }
            //  catch (e_1) {
            //    env_1.error = e_1;
            //    env_1.hasError = true;
            //  }
            //  finally {
            //    __disposeResources(env_1);
            //  }
            //
            // If the class is exported, we also produce an `export { C };`
            HoistBindingIdentifier(factory.GetLocalName(node), isExported && !isDefault, exportAlias: null, node);
            expression = factory.CreateAssignment(factory.GetDeclarationName(node), expression);
            if (NamedEvaluation.IsNamedEvaluation(store, expression))
            {
                expression = NamedEvaluation.TransformNamedEvaluation(context, expression, ignoreEmptyStringLiteral: false);
            }
            NodeFactory.SetOriginalNode(store, expression, node);
            // (setSourceMapRange: no source maps)
            FactoryEmitNode.SetCommentRange(store, expression, node);
        }

        if (isDefault && defaultExportBinding.IsNone)
        {
            // In the case of a default export, we create a temporary variable that we export as the default and then
            // assign to that variable.
            //
            // given:
            //
            //  using x = expr;
            //  export default class C {}
            //
            // produces:
            //
            //  export { default_1 as default };
            //  var x, C, default_1;
            //  const env_1 = { ... };
            //  try {
            //    x = __addDisposableResource(env_1, expr, false);
            //    default_1 = C = class {};
            //  }
            //  catch (e_1) {
            //    env_1.error = e_1;
            //    env_1.hasError = true;
            //  }
            //  finally {
            //    __disposeResources(env_1);
            //  }
            //
            // Though we will never reassign `default_1`, this most closely matches the specified runtime semantics.
            defaultExportBinding = CreateDefaultBinding();
            HoistBindingIdentifier(defaultExportBinding, isExport: true, "default", node);
            expression = factory.CreateAssignment(defaultExportBinding, expression);
            if (NamedEvaluation.IsNamedEvaluation(store, expression))
            {
                expression = NamedEvaluation.TransformNamedEvaluation(context, expression, ignoreEmptyStringLiteral: false, "default");
            }
            NodeFactory.SetOriginalNode(store, expression, node);
        }

        return factory.CreateExpressionStatement(expression);
    }

    private Node HoistVariableStatement(Node node)
    {
        // NOTE: `node` has already been visited
        List<Node>? expressions = null;
        var isExported = Utilities.HasSyntacticModifier(store, node, ModifierFlags.Export);
        foreach (var variable in store.Nodes(store.Declarations(store.DeclarationList(node))))
        {
            HoistBindingElement(variable, isExported, variable);
            if (!store.Initializer(variable).IsNone)
            {
                (expressions ??= []).Add(HoistInitializedVariable(variable));
            }
        }
        if (expressions is not null)
        {
            var statement = factory.CreateExpressionStatement(factory.InlineExpressions(expressions));
            NodeFactory.SetOriginalNode(store, statement, node);
            FactoryEmitNode.SetCommentRange(store, statement, node);
            // (setSourceMapRange: no source maps)
            return statement;
        }
        return Node.None;
    }

    private Node HoistInitializedVariable(Node node)
    {
        // NOTE: `node` has already been visited
        Debug.Assert(!store.Initializer(node).IsNone);
        Node target;
        var name = store.Name(node);
        if (store.Kind(name) == SyntaxKind.Identifier)
        {
            target = factory.CloneNode(name);
            FactoryEmitNode.SetEmitFlags(store, target, Utilities.GetEmitFlags(store, target) & ~(EmitFlags.LocalName | EmitFlags.ExportName | EmitFlags.InternalName));
        }
        else
        {
            target = factory.Converters.ConvertToAssignmentPattern(name);
        }

        var assignment = factory.CreateAssignment(target, store.Initializer(node));
        NodeFactory.SetOriginalNode(store, assignment, node);
        FactoryEmitNode.SetCommentRange(store, assignment, node);
        // (setSourceMapRange: no source maps)
        return assignment;
    }

    private void HoistBindingElement(Node node, bool isExportedDeclaration, Node original)
    {
        // NOTE: `node` has already been visited
        var name = store.Name(node);
        if (store.Kind(name) is SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern)
        {
            foreach (var element in store.Nodes(store.Elements(name)))
            {
                if (store.Kind(element) != SyntaxKind.OmittedExpression)
                {
                    HoistBindingElement(element, isExportedDeclaration, original);
                }
            }
        }
        else
        {
            HoistBindingIdentifier(name, isExportedDeclaration, exportAlias: null, original);
        }
    }

    private void HoistBindingIdentifier(Node node, bool isExport, string? exportAlias, Node original)
    {
        // NOTE: `node` has already been visited
        var name = UtilitiesPublic.IsGeneratedIdentifier(store, node) ? node : factory.CloneNode(node);
        if (isExport)
        {
            if (exportAlias is null && !FactoryUtilities.IsLocalName(store, name))
            {
                var varDecl = factory.CreateVariableDeclaration(name, default, default, default);
                if (!original.IsNone)
                {
                    NodeFactory.SetOriginalNode(store, varDecl, original);
                }
                exportVars!.Add(varDecl);
                return;
            }

            var localName = exportAlias is not null ? name : Node.None;
            var exportName = exportAlias is not null ? factory.CreateIdentifier(exportAlias) : name;
            var specifier = factory.CreateExportSpecifier(isTypeOnly: false, localName, exportName);
            if (!original.IsNone)
            {
                NodeFactory.SetOriginalNode(store, specifier, original);
            }
            exportBindings!.Set(name, specifier);
        }
        context.HoistVariableDeclaration(name);
    }

    private Node CreateEnvBinding()
    {
        return factory.CreateUniqueName("env");
    }

    private List<Node> CreateDownlevelUsingStatements(List<Node> bodyStatements, Node envBinding, bool async)
    {
        var statements = new List<Node>();

        // produces:
        //
        //  const env_1 = { stack: [], error: void 0, hasError: false };
        //
        var envObject = factory.CreateObjectLiteralExpression(factory.CreateNodeArray(new[]
        {
            factory.CreatePropertyAssignment(factory.CreateIdentifier("stack"), factory.CreateArrayLiteralExpression()),
            factory.CreatePropertyAssignment(factory.CreateIdentifier("error"), factory.CreateVoidZero()),
            factory.CreatePropertyAssignment(factory.CreateIdentifier("hasError"), factory.CreateToken(SyntaxKind.FalseKeyword)),
        }));
        var envVar = factory.CreateVariableDeclaration(envBinding, default, default, envObject);
        var envVarList = factory.CreateVariableDeclarationList(factory.CreateNodeArray(new[] { envVar }), NodeFlags.Const);
        var envVarStatement = factory.CreateVariableStatement(default, envVarList);
        statements.Add(envVarStatement);

        // when `async` is `false`, produces:
        //
        //  try {
        //    <bodyStatements>
        //  }
        //  catch (e_1) {
        //      env_1.error = e_1;
        //      env_1.hasError = true;
        //  }
        //  finally {
        //    __disposeResources(env_1);
        //  }

        // when `async` is `true`, produces:
        //
        //  try {
        //    <bodyStatements>
        //  }
        //  catch (e_1) {
        //      env_1.error = e_1;
        //      env_1.hasError = true;
        //  }
        //  finally {
        //    const result_1 = __disposeResources(env_1);
        //    if (result_1) {
        //      await result_1;
        //    }
        //  }

        // Unfortunately, it is necessary to use two properties to indicate an error because `throw undefined` is legal
        // JavaScript.
        var tryBlock = factory.CreateBlock(factory.CreateNodeArray(bodyStatements), multiLine: true);
        var bodyCatchBinding = factory.CreateUniqueName("e");
        var catchClause = factory.CreateCatchClause(
            bodyCatchBinding,
            factory.CreateBlock(factory.CreateNodeArray(new[]
            {
                factory.CreateExpressionStatement(
                    factory.CreateAssignment(
                        factory.CreatePropertyAccessExpression(envBinding, factory.CreateIdentifier("error")),
                        bodyCatchBinding)),
                factory.CreateExpressionStatement(
                    factory.CreateAssignment(
                        factory.CreatePropertyAccessExpression(envBinding, factory.CreateIdentifier("hasError")),
                        factory.CreateToken(SyntaxKind.TrueKeyword))),
            }), multiLine: true));

        Node finallyBlock;
        if (async)
        {
            var result = factory.CreateUniqueName("result");
            finallyBlock = factory.CreateBlock(factory.CreateNodeArray(new[]
            {
                factory.CreateVariableStatement(
                    default,
                    factory.CreateVariableDeclarationList(factory.CreateNodeArray(new[]
                    {
                        factory.CreateVariableDeclaration(
                            result,
                            default,
                            default,
                            context.GetEmitHelperFactory().CreateDisposeResourcesHelper(envBinding)),
                    }), NodeFlags.Const)),
                factory.CreateIfStatement(result, factory.CreateExpressionStatement(factory.CreateAwaitExpression(result))),
            }), multiLine: true);
        }
        else
        {
            finallyBlock = factory.CreateBlock(factory.CreateNodeArray(new[]
            {
                factory.CreateExpressionStatement(
                    context.GetEmitHelperFactory().CreateDisposeResourcesHelper(envBinding)),
            }), multiLine: true);
        }

        var tryStatement = factory.CreateTryStatement(tryBlock, catchClause, finallyBlock);
        statements.Add(tryStatement);

        return statements;
    }

    private int CountPrologueStatements(NodeList statements)
    {
        var length = store.Length(statements);
        for (var i = 0; i < length; i++)
        {
            var statement = store.Element(statements, i);
            if (!Utilities.IsPrologueDirective(store, statement) && !NodeFactory.IsCustomPrologue(store, statement))
            {
                return i;
            }
        }
        return 0;
    }

    private bool IsUsingVariableDeclarationList(Node node)
    {
        return store.Kind(node) == SyntaxKind.VariableDeclarationList && GetUsingKindOfVariableDeclarationList(node) != UsingKind.None;
    }

    private UsingKind GetUsingKindOfVariableDeclarationList(Node node)
    {
        var blockScoped = store.Flags(node) & NodeFlags.BlockScoped;
        return blockScoped == NodeFlags.AwaitUsing ? UsingKind.Async :
            blockScoped == NodeFlags.Using ? UsingKind.Sync :
            UsingKind.None;
    }

    private UsingKind GetUsingKind(Node statement)
    {
        return store.Kind(statement) == SyntaxKind.VariableStatement ? GetUsingKindOfVariableDeclarationList(store.DeclarationList(statement)) : UsingKind.None;
    }

    private UsingKind GetUsingKindOfStatements(NodeList statements)
    {
        var result = UsingKind.None;
        foreach (var statement in store.Nodes(statements))
        {
            var usingKind = GetUsingKind(statement);
            if (usingKind == UsingKind.Async)
            {
                return UsingKind.Async;
            }
            if (usingKind > result)
            {
                result = usingKind;
            }
        }
        return result;
    }
}
