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

/// <summary>nodeFactory.ts: statements, declarations, module elements and module references.</summary>
internal sealed partial class NodeFactory
{
    //
    // Element
    //

    // @api
    internal Node CreateBlock(IReadOnlyList<Node> statements, bool? multiLine = null)
    {
        var node = CreateBaseNode(SyntaxKind.Block);
        store.SetStatements(node, CreateNodeArray(store, statements));
        store.SetMultiLine(node, multiLine == true);
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildrenFlags(store.Statements(node)));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    /// <summary>`createVariableStatement(modifiers, declarationList: VariableDeclarationList | readonly VariableDeclaration[])`.</summary>
    // @api
    internal Node CreateVariableStatement(IReadOnlyList<Node>? modifiers, object declarationList)
    {
        var node = CreateBaseNode(SyntaxKind.VariableStatement);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetDeclarationList(node, declarationList is IReadOnlyList<Node> list ? CreateVariableDeclarationList(list) : Node.FromObject(declarationList));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildrenFlags(store.Modifiers(node)) |
            PropagateChildFlags(store, store.DeclarationList(node))));
        if ((Utilities.ModifiersToFlags(store, store.Modifiers(node)) & ModifierFlags.Ambient) != 0)
        {
            store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        }
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateEmptyStatement()
    {
        var node = CreateBaseNode(SyntaxKind.EmptyStatement);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateExpressionStatement(Node expression)
    {
        var node = CreateBaseNode(SyntaxKind.ExpressionStatement);
        store.SetExpression(node, Parenthesizer.ParenthesizeExpressionOfExpressionStatement(expression));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildFlags(store, store.Expression(node)));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateIfStatement(Node expression, Node thenStatement, Node elseStatement = default)
    {
        var node = CreateBaseNode(SyntaxKind.IfStatement);
        store.SetExpression(node, expression);
        store.SetThenStatement(node, AsEmbeddedStatement(thenStatement));
        store.SetElseStatement(node, AsEmbeddedStatement(elseStatement));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.ThenStatement(node)) |
            PropagateChildFlags(store, store.ElseStatement(node))));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateDoStatement(Node statement, Node expression)
    {
        var node = CreateBaseNode(SyntaxKind.DoStatement);
        store.SetStatement(node, AsEmbeddedStatement(statement));
        store.SetExpression(node, expression);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Statement(node)) |
            PropagateChildFlags(store, store.Expression(node))));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateWhileStatement(Node expression, Node statement)
    {
        var node = CreateBaseNode(SyntaxKind.WhileStatement);
        store.SetExpression(node, expression);
        store.SetStatement(node, AsEmbeddedStatement(statement));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.Statement(node))));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateForStatement(Node initializer, Node condition, Node incrementor, Node statement)
    {
        var node = CreateBaseNode(SyntaxKind.ForStatement);
        store.SetInitializer(node, initializer);
        store.SetCondition(node, condition);
        store.SetIncrementor(node, incrementor);
        store.SetStatement(node, AsEmbeddedStatement(statement));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Initializer(node)) |
            PropagateChildFlags(store, store.Condition(node)) |
            PropagateChildFlags(store, store.Incrementor(node)) |
            PropagateChildFlags(store, store.Statement(node))));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateForInStatement(Node initializer, Node expression, Node statement)
    {
        var node = CreateBaseNode(SyntaxKind.ForInStatement);
        store.SetInitializer(node, initializer);
        store.SetExpression(node, expression);
        store.SetStatement(node, AsEmbeddedStatement(statement));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Initializer(node)) |
            PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.Statement(node))));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateForOfStatement(Node awaitModifier, Node initializer, Node expression, Node statement)
    {
        var node = CreateBaseNode(SyntaxKind.ForOfStatement);
        store.SetAwaitModifier(node, awaitModifier);
        store.SetInitializer(node, initializer);
        store.SetExpression(node, Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression));
        store.SetStatement(node, AsEmbeddedStatement(statement));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.AwaitModifier(node)) |
            PropagateChildFlags(store, store.Initializer(node)) |
            PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.Statement(node)) |
            TransformFlags.ContainsES2015));
        if (!awaitModifier.IsNone)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsES2018);
        }
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateContinueStatement(object? label = null)
    {
        var node = CreateBaseNode(SyntaxKind.ContinueStatement);
        store.SetLabel(node, AsName(label));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Label(node)) |
            TransformFlags.ContainsHoistedDeclarationOrCompletion));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateBreakStatement(object? label = null)
    {
        var node = CreateBaseNode(SyntaxKind.BreakStatement);
        store.SetLabel(node, AsName(label));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Label(node)) |
            TransformFlags.ContainsHoistedDeclarationOrCompletion));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateReturnStatement(Node expression = default)
    {
        var node = CreateBaseNode(SyntaxKind.ReturnStatement);
        store.SetExpression(node, expression);
        // return in an ES2018 async generator must be awaited
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            TransformFlags.ContainsES2018 |
            TransformFlags.ContainsHoistedDeclarationOrCompletion));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateWithStatement(Node expression, Node statement)
    {
        var node = CreateBaseNode(SyntaxKind.WithStatement);
        store.SetExpression(node, expression);
        store.SetStatement(node, AsEmbeddedStatement(statement));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.Statement(node))));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateSwitchStatement(Node expression, Node caseBlock)
    {
        var node = CreateBaseNode(SyntaxKind.SwitchStatement);
        store.SetExpression(node, Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression));
        store.SetCaseBlock(node, caseBlock);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.CaseBlock(node))));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        store.SetPossiblyExhaustive(node, false); // initialized by binder
        return node;
    }

    // @api
    internal Node CreateLabeledStatement(object label, Node statement)
    {
        var node = CreateBaseNode(SyntaxKind.LabeledStatement);
        store.SetLabel(node, AsName(label));
        store.SetStatement(node, AsEmbeddedStatement(statement));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Label(node)) |
            PropagateChildFlags(store, store.Statement(node))));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateThrowStatement(Node expression)
    {
        var node = CreateBaseNode(SyntaxKind.ThrowStatement);
        store.SetExpression(node, expression);
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildFlags(store, store.Expression(node)));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateTryStatement(Node tryBlock, Node catchClause, Node finallyBlock)
    {
        var node = CreateBaseNode(SyntaxKind.TryStatement);
        store.SetTryBlock(node, tryBlock);
        store.SetCatchClause(node, catchClause);
        store.SetFinallyBlock(node, finallyBlock);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.TryBlock(node)) |
            PropagateChildFlags(store, store.CatchClause(node)) |
            PropagateChildFlags(store, store.FinallyBlock(node))));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateDebuggerStatement()
    {
        var node = CreateBaseNode(SyntaxKind.DebuggerStatement);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal Node CreateVariableDeclaration(object? name, Node exclamationToken, Node type, Node initializer)
    {
        var node = CreateBaseDeclaration(SyntaxKind.VariableDeclaration);
        store.SetName(node, AsName(name));
        store.SetExclamationToken(node, exclamationToken);
        store.SetType(node, type);
        store.SetInitializer(node, AsInitializer(initializer));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateNameFlags(store, store.Name(node)) |
            PropagateChildFlags(store, store.Initializer(node)) |
            (!((store.ExclamationToken(node) is { IsNone: false } __t1 ? __t1 : store.Type(node))).IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None)));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateVariableDeclarationList(IReadOnlyList<Node> declarations, NodeFlags flags = NodeFlags.None)
    {
        var node = CreateBaseNode(SyntaxKind.VariableDeclarationList);
        store.SetFlags(node, store.Flags(node) | (flags & NodeFlags.BlockScoped));
        store.SetDeclarations(node, CreateNodeArray(store, declarations));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildrenFlags(store.Declarations(node)) |
            TransformFlags.ContainsHoistedDeclarationOrCompletion));
        if ((flags & NodeFlags.BlockScoped) != 0)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | (TransformFlags.ContainsES2015 |
                TransformFlags.ContainsBlockScopedBinding));
        }
        if ((flags & NodeFlags.Using) != 0)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsESNext);
        }
        return node;
    }

    // @api
    internal Node CreateFunctionDeclaration(
        IReadOnlyList<Node>? modifiers,
        Node asteriskToken,
        object? name,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node type,
        Node body)
    {
        var node = CreateBaseDeclaration(SyntaxKind.FunctionDeclaration);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetAsteriskToken(node, asteriskToken);
        store.SetName(node, AsName(name));
        store.SetTypeParameters(node, AsNodeArray(store, typeParameters));
        store.SetParameters(node, CreateNodeArray(store, parameters));
        store.SetType(node, type);
        store.SetBody(node, body);
        if (store.Body(node).IsNone || (Utilities.ModifiersToFlags(store, store.Modifiers(node)) & ModifierFlags.Ambient) != 0)
        {
            store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        }
        else
        {
            var isAsync = (Utilities.ModifiersToFlags(store, store.Modifiers(node)) & ModifierFlags.Async) != 0;
            var isGenerator = !store.AsteriskToken(node).IsNone;
            var isAsyncGenerator = isAsync && isGenerator;
            store.SetTransformFlags(node, PropagateChildrenFlags(store.Modifiers(node)) |
                PropagateChildFlags(store, store.AsteriskToken(node)) |
                PropagateNameFlags(store, store.Name(node)) |
                PropagateChildrenFlags(store.TypeParameters(node)) |
                PropagateChildrenFlags(store.Parameters(node)) |
                PropagateChildFlags(store, store.Type(node)) |
                (PropagateChildFlags(store, store.Body(node)) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
                (isAsyncGenerator ? TransformFlags.ContainsES2018 :
                    isAsync ? TransformFlags.ContainsES2017 :
                    isGenerator ? TransformFlags.ContainsGenerator :
                    TransformFlags.None) |
                (store.TypeParameters(node) != null || !store.Type(node).IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
                TransformFlags.ContainsHoistedDeclarationOrCompletion);
        }
        // node.typeArguments = undefined; // used in quick info
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.endFlowNode = undefined;
        // node.returnFlowNode = undefined;
        return node;
    }

    // @api
    internal Node CreateClassDeclaration(
        IReadOnlyList<Node>? modifiers,
        object? name,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node>? heritageClauses,
        IReadOnlyList<Node> members)
    {
        var node = CreateBaseDeclaration(SyntaxKind.ClassDeclaration);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetName(node, AsName(name));
        store.SetTypeParameters(node, AsNodeArray(store, typeParameters));
        store.SetHeritageClauses(node, AsNodeArray(store, heritageClauses));
        store.SetMembers(node, CreateNodeArray(store, members));
        if ((Utilities.ModifiersToFlags(store, store.Modifiers(node)) & ModifierFlags.Ambient) != 0)
        {
            store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        }
        else
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildrenFlags(store.Modifiers(node)) |
                PropagateNameFlags(store, store.Name(node)) |
                PropagateChildrenFlags(store.TypeParameters(node)) |
                PropagateChildrenFlags(store.HeritageClauses(node)) |
                PropagateChildrenFlags(store.Members(node)!) |
                (store.TypeParameters(node) != null ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
                TransformFlags.ContainsES2015));
            if ((store.TransformFlags(node) & TransformFlags.ContainsTypeScriptClassSyntax) != 0)
            {
                store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsTypeScript);
            }
        }
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateInterfaceDeclaration(
        IReadOnlyList<Node>? modifiers,
        object name,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node>? heritageClauses,
        IReadOnlyList<Node> members)
    {
        var node = CreateBaseDeclaration(SyntaxKind.InterfaceDeclaration);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetName(node, AsName(name));
        store.SetTypeParameters(node, AsNodeArray(store, typeParameters));
        store.SetHeritageClauses(node, AsNodeArray(store, heritageClauses));
        store.SetMembers(node, CreateNodeArray(store, members));
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateTypeAliasDeclaration(
        IReadOnlyList<Node>? modifiers,
        object name,
        IReadOnlyList<Node>? typeParameters,
        Node type)
    {
        var node = CreateBaseDeclaration(SyntaxKind.TypeAliasDeclaration);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetName(node, AsName(name));
        store.SetTypeParameters(node, AsNodeArray(store, typeParameters));
        store.SetType(node, type);
        store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    internal Node CreateEnumDeclaration(
        IReadOnlyList<Node>? modifiers,
        object name,
        IReadOnlyList<Node> members)
    {
        var node = CreateBaseDeclaration(SyntaxKind.EnumDeclaration);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetName(node, AsName(name));
        store.SetMembers(node, CreateNodeArray(store, members));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildrenFlags(store.Modifiers(node)) |
            PropagateChildFlags(store, store.Name(node)) |
            PropagateChildrenFlags(store.Members(node)!) |
            TransformFlags.ContainsTypeScript));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // Enum declarations cannot contain `await`
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateModuleDeclaration(
        IReadOnlyList<Node>? modifiers,
        Node name,
        Node body,
        NodeFlags flags = NodeFlags.None)
    {
        var node = CreateBaseDeclaration(SyntaxKind.ModuleDeclaration);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetFlags(node, store.Flags(node) | (flags & (NodeFlags.Namespace | NodeFlags.NestedNamespace | NodeFlags.GlobalAugmentation)));
        store.SetName(node, name);
        store.SetBody(node, body);
        if ((Utilities.ModifiersToFlags(store, store.Modifiers(node)) & ModifierFlags.Ambient) != 0)
        {
            store.SetTransformFlags(node, TransformFlags.ContainsTypeScript);
        }
        else
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildrenFlags(store.Modifiers(node)) |
                PropagateChildFlags(store, store.Name(node)) |
                PropagateChildFlags(store, store.Body(node)) |
                TransformFlags.ContainsTypeScript));
        }
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // Module declarations cannot contain `await`.
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    internal Node CreateModuleBlock(IReadOnlyList<Node> statements)
    {
        var node = CreateBaseNode(SyntaxKind.ModuleBlock);
        store.SetStatements(node, CreateNodeArray(store, statements));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildrenFlags(store.Statements(node)));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateCaseBlock(IReadOnlyList<Node> clauses)
    {
        var node = CreateBaseNode(SyntaxKind.CaseBlock);
        store.SetClauses(node, CreateNodeArray(store, clauses));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildrenFlags(store.Clauses(node)));
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    internal Node CreateNamespaceExportDeclaration(object name)
    {
        var node = CreateBaseDeclaration(SyntaxKind.NamespaceExportDeclaration);
        store.SetName(node, AsName(name));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateIdentifierNameFlags(store, store.Name(node)) |
            TransformFlags.ContainsTypeScript));
        store.SetModifiers(node, null); // initialized by parser to report grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateImportEqualsDeclaration(
        IReadOnlyList<Node>? modifiers,
        bool isTypeOnly,
        object name,
        Node moduleReference)
    {
        var node = CreateBaseDeclaration(SyntaxKind.ImportEqualsDeclaration);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetName(node, AsName(name));
        store.SetIsTypeOnly(node, isTypeOnly);
        store.SetModuleReference(node, moduleReference);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildrenFlags(store.Modifiers(node)) |
            PropagateIdentifierNameFlags(store, store.Name(node)) |
            PropagateChildFlags(store, store.ModuleReference(node))));
        if (!FactoryNodeTests.IsExternalModuleReference(store, store.ModuleReference(node)))
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsTypeScript);
        }
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // Import= declaration is always parsed in an Await context
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateImportDeclaration(
        IReadOnlyList<Node>? modifiers,
        Node importClause,
        Node moduleSpecifier,
        Node attributes)
    {
        var node = CreateBaseNode(SyntaxKind.ImportDeclaration);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetImportClause(node, importClause);
        store.SetModuleSpecifier(node, moduleSpecifier);
        store.SetAttributes(node, attributes);
        store.SetAssertClause(node, attributes);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.ImportClause(node)) |
            PropagateChildFlags(store, store.ModuleSpecifier(node))));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    /// <summary>`createImportClause(phaseModifier: ImportPhaseModifierSyntaxKind | boolean | undefined, ...)` with a boolean.</summary>
    // @api
    internal Node CreateImportClause(bool phaseModifier, Node name, Node namedBindings)
    {
        return CreateImportClause(phaseModifier ? (SyntaxKind?)SyntaxKind.TypeKeyword : null, name, namedBindings);
    }

    /// <summary>`createImportClause(phaseModifier: ImportPhaseModifierSyntaxKind | boolean | undefined, ...)` with a kind or undefined.</summary>
    // @api
    internal Node CreateImportClause(SyntaxKind? phaseModifier, Node name, Node namedBindings)
    {
        var node = CreateBaseDeclaration(SyntaxKind.ImportClause);
        store.SetIsTypeOnly(node, phaseModifier == SyntaxKind.TypeKeyword);
        // (TypeScript stores undefined; the generated field is a SyntaxKind, so undefined is SyntaxKind.Unknown.)
        store.SetPhaseModifier(node, phaseModifier ?? SyntaxKind.Unknown);
        store.SetName(node, name);
        store.SetNamedBindings(node, namedBindings);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Name(node)) |
            PropagateChildFlags(store, store.NamedBindings(node))));
        if (phaseModifier == SyntaxKind.TypeKeyword)
        {
            store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsTypeScript);
        }
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        return node;
    }

    // @api
    internal Node CreateImportAttributes(IReadOnlyList<Node> elements, bool? multiLine = null, SyntaxKind? token = null)
    {
        var node = CreateBaseNode(SyntaxKind.ImportAttributes);
        store.SetToken(node, token ?? SyntaxKind.WithKeyword);
        store.SetElements(node, CreateNodeArray(store, elements));
        store.SetMultiLine(node, multiLine == true);
        store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsESNext);
        return node;
    }

    // @api
    internal Node CreateImportAttribute(Node name, Node value)
    {
        var node = CreateBaseNode(SyntaxKind.ImportAttribute);
        store.SetName(node, name);
        store.SetValue(node, value);
        store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsESNext);
        return node;
    }

    // @api
    internal Node CreateNamespaceImport(Node name)
    {
        var node = CreateBaseDeclaration(SyntaxKind.NamespaceImport);
        store.SetName(node, name);
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildFlags(store, store.Name(node)));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        return node;
    }

    // @api
    internal Node CreateNamespaceExport(Node name)
    {
        var node = CreateBaseDeclaration(SyntaxKind.NamespaceExport);
        store.SetName(node, name);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Name(node)) |
            TransformFlags.ContainsES2020));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        return node;
    }

    // @api
    internal Node CreateNamedImports(IReadOnlyList<Node> elements)
    {
        var node = CreateBaseNode(SyntaxKind.NamedImports);
        store.SetElements(node, CreateNodeArray(store, elements));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildrenFlags(store.Elements(node)));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        return node;
    }

    // @api
    internal Node CreateImportSpecifier(bool isTypeOnly, Node propertyName, Node name)
    {
        var node = CreateBaseDeclaration(SyntaxKind.ImportSpecifier);
        store.SetIsTypeOnly(node, isTypeOnly);
        store.SetPropertyName(node, propertyName);
        store.SetName(node, name);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.PropertyName(node)) |
            PropagateChildFlags(store, store.Name(node))));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        return node;
    }

    // @api
    internal Node CreateExportAssignment(
        IReadOnlyList<Node>? modifiers,
        bool? isExportEquals,
        Node expression)
    {
        var node = CreateBaseDeclaration(SyntaxKind.ExportAssignment);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetIsExportEquals(node, isExportEquals == true);
        store.SetExpression(node, isExportEquals == true
            ? Parenthesizer.ParenthesizeRightSideOfBinary(SyntaxKind.EqualsToken, /*leftSide*/ default, expression)
            : Parenthesizer.ParenthesizeExpressionOfExportDefault(expression));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildrenFlags(store.Modifiers(node)) | PropagateChildFlags(store, store.Expression(node))));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateExportDeclaration(
        IReadOnlyList<Node>? modifiers,
        bool isTypeOnly,
        Node exportClause,
        Node moduleSpecifier = default,
        Node attributes = default)
    {
        var node = CreateBaseDeclaration(SyntaxKind.ExportDeclaration);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetIsTypeOnly(node, isTypeOnly);
        store.SetExportClause(node, exportClause);
        store.SetModuleSpecifier(node, moduleSpecifier);
        store.SetAttributes(node, attributes);
        store.SetAssertClause(node, attributes);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildrenFlags(store.Modifiers(node)) |
            PropagateChildFlags(store, store.ExportClause(node)) |
            PropagateChildFlags(store, store.ModuleSpecifier(node))));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateNamedExports(IReadOnlyList<Node> elements)
    {
        var node = CreateBaseNode(SyntaxKind.NamedExports);
        store.SetElements(node, CreateNodeArray(store, elements));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildrenFlags(store.Elements(node)));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        return node;
    }

    // @api
    internal Node CreateExportSpecifier(bool isTypeOnly, object? propertyName, object name)
    {
        var node = CreateBaseNode(SyntaxKind.ExportSpecifier);
        store.SetIsTypeOnly(node, isTypeOnly);
        store.SetPropertyName(node, AsName(propertyName));
        store.SetName(node, AsName(name));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.PropertyName(node)) |
            PropagateChildFlags(store, store.Name(node))));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal Node CreateMissingDeclaration()
    {
        var node = CreateBaseDeclaration(SyntaxKind.MissingDeclaration);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    //
    // Module references
    //

    // @api
    internal Node CreateExternalModuleReference(Node expression)
    {
        var node = CreateBaseNode(SyntaxKind.ExternalModuleReference);
        store.SetExpression(node, expression);
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildFlags(store, store.Expression(node)));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        return node;
    }
}
