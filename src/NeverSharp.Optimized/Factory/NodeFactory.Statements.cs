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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateBlock(NodeList statements, bool? multiLine = null)
    {
        // (Track O, step O12, here and in the functions below: the fields and the transform flags are computed from the
        // arguments first, then the record is written in one go by the generated NodeStore.New{Class}; the flags of
        // the new node, which TypeScript's code ORs in, are none at that point. No other node is created in between.)
        var statementsArray = CreateNodeArray(store, statements);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return store.NewBlock(PropagateChildrenFlags(statementsArray), statements: statementsArray, multiLine: multiLine == true);
    }

    /// <summary>`createVariableStatement(modifiers, declarationList: VariableDeclarationList | readonly VariableDeclaration[])`.</summary>
    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateVariableStatement(NodeList modifiers, Node declarationList)
    {
        var modifiersArray = AsNodeArray(store, modifiers);
        // (Track O: declarationList is always a VariableDeclarationList node; no array form is passed.)
        var transformFlags = (Utilities.ModifiersToFlags(store, modifiersArray) & ModifierFlags.Ambient) != 0
            ? TransformFlags.ContainsTypeScript
            : PropagateChildrenFlags(modifiersArray) | PropagateChildFlags(store, declarationList);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return store.NewVariableStatement(transformFlags, modifiers: modifiersArray, declarationList: declarationList);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateEmptyStatement()
    {
        var node = CreateBaseNode(SyntaxKind.EmptyStatement);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateExpressionStatement(Node expression)
    {
        var operand = Parenthesizer.ParenthesizeExpressionOfExpressionStatement(expression);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return store.NewExpressionStatement(PropagateChildFlags(store, operand), expression: operand);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateIfStatement(Node expression, Node thenStatement, Node elseStatement = default)
    {
        if (IsNotEmitted(thenStatement) || IsNotEmitted(elseStatement))
        {
            return CreateIfStatementWithEmbedded(expression, thenStatement, elseStatement);
        }
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return store.NewIfStatement(
            PropagateChildFlags(store, expression) |
                PropagateChildFlags(store, thenStatement) |
                PropagateChildFlags(store, elseStatement),
            expression: expression,
            thenStatement: thenStatement,
            elseStatement: elseStatement);
    }

    /// <summary>
    /// Whether asEmbeddedStatement replaces the statement (by a new EmptyStatement, which TypeScript creates after the
    /// node that embeds it; the functions written in one go take TypeScript's way then, Track O step O12).
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private bool IsNotEmitted(Node statement)
    {
        return !statement.IsNone && FactoryNodeTests.IsNotEmittedStatement(store, statement);
    }

    // createIfStatement as TypeScript writes it (statements to embed; see IsNotEmitted).
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateIfStatementWithEmbedded(Node expression, Node thenStatement, Node elseStatement)
    {
        var node = CreateBaseNode(SyntaxKind.IfStatement);
        store.SetExpression(node, expression);
        store.SetThenStatement(node, AsEmbeddedStatement(thenStatement));
        store.SetElseStatement(node, AsEmbeddedStatement(elseStatement));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Expression(node)) |
            PropagateChildFlags(store, store.ThenStatement(node)) |
            PropagateChildFlags(store, store.ElseStatement(node))));
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateContinueStatement(Node label = default)
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateBreakStatement(Node label = default)
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateReturnStatement(Node expression = default)
    {
        // return in an ES2018 async generator must be awaited
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return store.NewReturnStatement(
            PropagateChildFlags(store, expression) |
                TransformFlags.ContainsES2018 |
                TransformFlags.ContainsHoistedDeclarationOrCompletion,
            expression: expression);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateLabeledStatement(Node label, Node statement)
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateThrowStatement(Node expression)
    {
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return store.NewThrowStatement(PropagateChildFlags(store, expression), expression: expression);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateDebuggerStatement()
    {
        var node = CreateBaseNode(SyntaxKind.DebuggerStatement);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.flowNode = undefined; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateVariableDeclaration(Node name, Node exclamationToken, Node type, Node initializer)
    {
        var nameNode = AsName(name);
        var initializerNode = AsInitializer(initializer);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return store.NewVariableDeclaration(
            PropagateNameFlags(store, nameNode) |
                PropagateChildFlags(store, initializerNode) |
                (!exclamationToken.IsNone || !type.IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None),
            name: nameNode,
            exclamationToken: exclamationToken,
            type: type,
            initializer: initializerNode);
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateVariableDeclarationList(NodeList declarations, NodeFlags flags = NodeFlags.None)
    {
        var declarationsArray = CreateNodeArray(store, declarations);
        var transformFlags = PropagateChildrenFlags(declarationsArray) |
            TransformFlags.ContainsHoistedDeclarationOrCompletion;
        if ((flags & NodeFlags.BlockScoped) != 0)
        {
            transformFlags |= TransformFlags.ContainsES2015 |
                TransformFlags.ContainsBlockScopedBinding;
        }
        if ((flags & NodeFlags.Using) != 0)
        {
            transformFlags |= TransformFlags.ContainsESNext;
        }
        var node = store.NewVariableDeclarationList(transformFlags, declarations: declarationsArray);
        if ((flags & NodeFlags.BlockScoped) != 0)
        {
            store.SetFlags(node, store.Flags(node) | (flags & NodeFlags.BlockScoped));
        }
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateFunctionDeclaration(
        NodeList modifiers,
        Node asteriskToken,
        Node name,
        NodeList typeParameters,
        NodeList parameters,
        Node type,
        Node body)
    {
        var modifiersArray = AsNodeArray(store, modifiers);
        var nameNode = AsName(name);
        var typeParametersArray = AsNodeArray(store, typeParameters);
        var parametersArray = CreateNodeArray(store, parameters);
        TransformFlags transformFlags;
        if (body.IsNone || (Utilities.ModifiersToFlags(store, modifiersArray) & ModifierFlags.Ambient) != 0)
        {
            transformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            var isAsync = (Utilities.ModifiersToFlags(store, modifiersArray) & ModifierFlags.Async) != 0;
            var isGenerator = !asteriskToken.IsNone;
            var isAsyncGenerator = isAsync && isGenerator;
            transformFlags = PropagateChildrenFlags(modifiersArray) |
                PropagateChildFlags(store, asteriskToken) |
                PropagateNameFlags(store, nameNode) |
                PropagateChildrenFlags(typeParametersArray) |
                PropagateChildrenFlags(parametersArray) |
                PropagateChildFlags(store, type) |
                (PropagateChildFlags(store, body) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
                (isAsyncGenerator ? TransformFlags.ContainsES2018 :
                    isAsync ? TransformFlags.ContainsES2017 :
                    isGenerator ? TransformFlags.ContainsGenerator :
                    TransformFlags.None) |
                (!typeParametersArray.IsNone || !type.IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
                TransformFlags.ContainsHoistedDeclarationOrCompletion;
        }
        var node = store.NewFunctionDeclaration(
            transformFlags,
            modifiers: modifiersArray,
            asteriskToken: asteriskToken,
            name: nameNode,
            typeParameters: typeParametersArray,
            parameters: parametersArray,
            type: type,
            body: body);
        // node.typeArguments = undefined; // used in quick info
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.endFlowNode = undefined;
        // node.returnFlowNode = undefined;
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateClassDeclaration(
        NodeList modifiers,
        Node name,
        NodeList typeParameters,
        NodeList heritageClauses,
        NodeList members)
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
                PropagateChildrenFlags(store.Members(node)) |
                (!store.TypeParameters(node).IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateInterfaceDeclaration(
        NodeList modifiers,
        Node name,
        NodeList typeParameters,
        NodeList heritageClauses,
        NodeList members)
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateTypeAliasDeclaration(
        NodeList modifiers,
        Node name,
        NodeList typeParameters,
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateEnumDeclaration(
        NodeList modifiers,
        Node name,
        NodeList members)
    {
        var node = CreateBaseDeclaration(SyntaxKind.EnumDeclaration);
        store.SetModifiers(node, AsNodeArray(store, modifiers));
        store.SetName(node, AsName(name));
        store.SetMembers(node, CreateNodeArray(store, members));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildrenFlags(store.Modifiers(node)) |
            PropagateChildFlags(store, store.Name(node)) |
            PropagateChildrenFlags(store.Members(node)) |
            TransformFlags.ContainsTypeScript));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // Enum declarations cannot contain `await`
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateModuleDeclaration(
        NodeList modifiers,
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateModuleBlock(NodeList statements)
    {
        var node = CreateBaseNode(SyntaxKind.ModuleBlock);
        store.SetStatements(node, CreateNodeArray(store, statements));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildrenFlags(store.Statements(node)));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateCaseBlock(NodeList clauses)
    {
        var node = CreateBaseNode(SyntaxKind.CaseBlock);
        store.SetClauses(node, CreateNodeArray(store, clauses));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildrenFlags(store.Clauses(node)));
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateNamespaceExportDeclaration(Node name)
    {
        var node = CreateBaseDeclaration(SyntaxKind.NamespaceExportDeclaration);
        store.SetName(node, AsName(name));
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateIdentifierNameFlags(store, store.Name(node)) |
            TransformFlags.ContainsTypeScript));
        store.SetModifiers(node, default); // initialized by parser to report grammar errors
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateImportEqualsDeclaration(
        NodeList modifiers,
        bool isTypeOnly,
        Node name,
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateImportDeclaration(
        NodeList modifiers,
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateImportClause(bool phaseModifier, Node name, Node namedBindings)
    {
        return CreateImportClause(phaseModifier ? (SyntaxKind?)SyntaxKind.TypeKeyword : null, name, namedBindings);
    }

    /// <summary>`createImportClause(phaseModifier: ImportPhaseModifierSyntaxKind | boolean | undefined, ...)` with a kind or undefined.</summary>
    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateImportAttributes(NodeList elements, bool? multiLine = null, SyntaxKind? token = null)
    {
        var node = CreateBaseNode(SyntaxKind.ImportAttributes);
        store.SetToken(node, token ?? SyntaxKind.WithKeyword);
        store.SetElements(node, CreateNodeArray(store, elements));
        store.SetMultiLine(node, multiLine == true);
        store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsESNext);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateImportAttribute(Node name, Node value)
    {
        var node = CreateBaseNode(SyntaxKind.ImportAttribute);
        store.SetName(node, name);
        store.SetValue(node, value);
        store.SetTransformFlags(node, store.TransformFlags(node) | TransformFlags.ContainsESNext);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateNamespaceImport(Node name)
    {
        var node = CreateBaseDeclaration(SyntaxKind.NamespaceImport);
        store.SetName(node, name);
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildFlags(store, store.Name(node)));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateNamedImports(NodeList elements)
    {
        var node = CreateBaseNode(SyntaxKind.NamedImports);
        store.SetElements(node, CreateNodeArray(store, elements));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildrenFlags(store.Elements(node)));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateExportAssignment(
        NodeList modifiers,
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateExportDeclaration(
        NodeList modifiers,
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateNamedExports(NodeList elements)
    {
        var node = CreateBaseNode(SyntaxKind.NamedExports);
        store.SetElements(node, CreateNodeArray(store, elements));
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildrenFlags(store.Elements(node)));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateExportSpecifier(bool isTypeOnly, Node propertyName, Node name)
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateExternalModuleReference(Node expression)
    {
        var node = CreateBaseNode(SyntaxKind.ExternalModuleReference);
        store.SetExpression(node, expression);
        store.SetTransformFlags(node, store.TransformFlags(node) | PropagateChildFlags(store, store.Expression(node)));
        store.SetTransformFlags(node, store.TransformFlags(node) & (~TransformFlags.ContainsPossibleTopLevelAwait)); // always parsed in an Await context
        return node;
    }
}
