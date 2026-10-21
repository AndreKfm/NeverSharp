// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go (NodeFactory)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>nodeFactory.ts, createNodeFactory: statements, declarations, modules and module references.</summary>
internal sealed partial class NodeFactory
{
    //
    // Element
    //

    // @api
    internal Block CreateBlock(IReadOnlyList<Node> statements, bool multiLine = false)
    {
        var node = (Block)CreateBaseNode(SyntaxKind.Block);
        node.Statements = CreateNodeArray(statements);
        node.MultiLine = multiLine;
        node.TransformFlags |= PropagateChildrenFlags(node.Statements);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        return node;
    }

    /// <summary>createVariableStatement with a VariableDeclarationList (`declarationList: VariableDeclarationList | readonly VariableDeclaration[]`).</summary>
    // @api
    internal VariableStatement CreateVariableStatement(IReadOnlyList<Node>? modifiers, VariableDeclarationList declarationList)
    {
        return CreateVariableStatementWorker(modifiers, declarationList, /*declarationArray*/ null);
    }

    /// <summary>createVariableStatement with an array of VariableDeclarations.</summary>
    // @api
    internal VariableStatement CreateVariableStatement(IReadOnlyList<Node>? modifiers, IReadOnlyList<Node> declarationList)
    {
        return CreateVariableStatementWorker(modifiers, /*declarationListNode*/ null, declarationList);
    }

    /// <summary>The body of createVariableStatement; exactly one of the two declaration list forms is set.</summary>
    private VariableStatement CreateVariableStatementWorker(IReadOnlyList<Node>? modifiers, VariableDeclarationList? declarationListNode, IReadOnlyList<Node>? declarationArray)
    {
        var node = (VariableStatement)CreateBaseNode(SyntaxKind.VariableStatement);
        node.Modifiers = AsNodeArray(modifiers);
        // isArray(declarationList) ? createVariableDeclarationList(declarationList) : declarationList
        node.DeclarationList = declarationArray != null ? CreateVariableDeclarationList(declarationArray) : declarationListNode!;
        node.TransformFlags |= PropagateChildrenFlags(node.Modifiers) |
            PropagateChildFlags(node.DeclarationList);
        if ((Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Ambient) != 0)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal EmptyStatement CreateEmptyStatement()
    {
        var node = (EmptyStatement)CreateBaseNode(SyntaxKind.EmptyStatement);
        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal ExpressionStatement CreateExpressionStatement(Node expression)
    {
        var node = (ExpressionStatement)CreateBaseNode(SyntaxKind.ExpressionStatement);
        node.Expression = Parenthesizer.ParenthesizeExpressionOfExpressionStatement(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal IfStatement CreateIfStatement(Node expression, Node thenStatement, Node? elseStatement = null)
    {
        var node = (IfStatement)CreateBaseNode(SyntaxKind.IfStatement);
        node.Expression = expression;
        node.ThenStatement = AsEmbeddedStatement(thenStatement);
        node.ElseStatement = AsEmbeddedStatement(elseStatement);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.ThenStatement) |
            PropagateChildFlags(node.ElseStatement);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal DoStatement CreateDoStatement(Node statement, Node expression)
    {
        var node = (DoStatement)CreateBaseNode(SyntaxKind.DoStatement);
        node.Statement = AsEmbeddedStatement(statement);
        node.Expression = expression;
        node.TransformFlags |= PropagateChildFlags(node.Statement) |
            PropagateChildFlags(node.Expression);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal WhileStatement CreateWhileStatement(Node expression, Node statement)
    {
        var node = (WhileStatement)CreateBaseNode(SyntaxKind.WhileStatement);
        node.Expression = expression;
        node.Statement = AsEmbeddedStatement(statement);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.Statement);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal ForStatement CreateForStatement(Node? initializer, Node? condition, Node? incrementor, Node statement)
    {
        var node = (ForStatement)CreateBaseNode(SyntaxKind.ForStatement);
        node.Initializer = initializer;
        node.Condition = condition;
        node.Incrementor = incrementor;
        node.Statement = AsEmbeddedStatement(statement);
        node.TransformFlags |= PropagateChildFlags(node.Initializer) |
            PropagateChildFlags(node.Condition) |
            PropagateChildFlags(node.Incrementor) |
            PropagateChildFlags(node.Statement);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal ForInStatement CreateForInStatement(Node initializer, Node expression, Node statement)
    {
        var node = (ForInStatement)CreateBaseNode(SyntaxKind.ForInStatement);
        node.Initializer = initializer;
        node.Expression = expression;
        node.Statement = AsEmbeddedStatement(statement);
        node.TransformFlags |= PropagateChildFlags(node.Initializer) |
            PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.Statement);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal ForOfStatement CreateForOfStatement(AwaitKeyword? awaitModifier, Node initializer, Node expression, Node statement)
    {
        var node = (ForOfStatement)CreateBaseNode(SyntaxKind.ForOfStatement);
        node.AwaitModifier = awaitModifier;
        node.Initializer = initializer;
        node.Expression = Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression);
        node.Statement = AsEmbeddedStatement(statement);
        node.TransformFlags |= PropagateChildFlags(node.AwaitModifier) |
            PropagateChildFlags(node.Initializer) |
            PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.Statement) |
            TransformFlags.ContainsES2015;
        if (awaitModifier != null)
        {
            node.TransformFlags |= TransformFlags.ContainsES2018;
        }

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal ContinueStatement CreateContinueStatement(Identifier? label = null)
    {
        var node = (ContinueStatement)CreateBaseNode(SyntaxKind.ContinueStatement);
        node.Label = AsName(label);
        node.TransformFlags |= PropagateChildFlags(node.Label) |
            TransformFlags.ContainsHoistedDeclarationOrCompletion;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal BreakStatement CreateBreakStatement(Identifier? label = null)
    {
        var node = (BreakStatement)CreateBaseNode(SyntaxKind.BreakStatement);
        node.Label = AsName(label);
        node.TransformFlags |= PropagateChildFlags(node.Label) |
            TransformFlags.ContainsHoistedDeclarationOrCompletion;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal ReturnStatement CreateReturnStatement(Node? expression = null)
    {
        var node = (ReturnStatement)CreateBaseNode(SyntaxKind.ReturnStatement);
        node.Expression = expression;
        // return in an ES2018 async generator must be awaited
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsES2018 |
            TransformFlags.ContainsHoistedDeclarationOrCompletion;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal WithStatement CreateWithStatement(Node expression, Node statement)
    {
        var node = (WithStatement)CreateBaseNode(SyntaxKind.WithStatement);
        node.Expression = expression;
        node.Statement = AsEmbeddedStatement(statement);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.Statement);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal SwitchStatement CreateSwitchStatement(Node expression, CaseBlock caseBlock)
    {
        var node = (SwitchStatement)CreateBaseNode(SyntaxKind.SwitchStatement);
        node.Expression = Parenthesizer.ParenthesizeExpressionForDisallowedComma(expression);
        node.CaseBlock = caseBlock;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildFlags(node.CaseBlock);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        node.PossiblyExhaustive = false; // initialized by binder
        return node;
    }

    // @api
    internal LabeledStatement CreateLabeledStatement(Identifier label, Node statement)
    {
        var node = (LabeledStatement)CreateBaseNode(SyntaxKind.LabeledStatement);
        node.Label = AsName(label);
        node.Statement = AsEmbeddedStatement(statement);
        node.TransformFlags |= PropagateChildFlags(node.Label) |
            PropagateChildFlags(node.Statement);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal ThrowStatement CreateThrowStatement(Node expression)
    {
        var node = (ThrowStatement)CreateBaseNode(SyntaxKind.ThrowStatement);
        node.Expression = expression;
        node.TransformFlags |= PropagateChildFlags(node.Expression);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal TryStatement CreateTryStatement(Block tryBlock, CatchClause? catchClause, Block? finallyBlock)
    {
        var node = (TryStatement)CreateBaseNode(SyntaxKind.TryStatement);
        node.TryBlock = tryBlock;
        node.CatchClause = catchClause;
        node.FinallyBlock = finallyBlock;
        node.TransformFlags |= PropagateChildFlags(node.TryBlock) |
            PropagateChildFlags(node.CatchClause) |
            PropagateChildFlags(node.FinallyBlock);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal DebuggerStatement CreateDebuggerStatement()
    {
        var node = (DebuggerStatement)CreateBaseNode(SyntaxKind.DebuggerStatement);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    // @api
    internal VariableDeclaration CreateVariableDeclaration(Node name, ExclamationToken? exclamationToken = null, Node? type = null, Node? initializer = null)
    {
        var node = (VariableDeclaration)CreateBaseDeclaration(SyntaxKind.VariableDeclaration);
        node.Name = AsName(name);
        node.ExclamationToken = exclamationToken;
        node.Type = type;
        node.Initializer = AsInitializer(initializer);
        node.TransformFlags |= PropagateNameFlags(node.Name) |
            PropagateChildFlags(node.Initializer) |
            ((node.ExclamationToken ?? node.Type) != null ? TransformFlags.ContainsTypeScript : TransformFlags.None);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal VariableDeclarationList CreateVariableDeclarationList(IReadOnlyList<Node> declarations, NodeFlags flags = NodeFlags.None)
    {
        var node = (VariableDeclarationList)CreateBaseNode(SyntaxKind.VariableDeclarationList);
        node.Flags |= flags & NodeFlags.BlockScoped;
        node.Declarations = CreateNodeArray(declarations);
        node.TransformFlags |= PropagateChildrenFlags(node.Declarations) |
            TransformFlags.ContainsHoistedDeclarationOrCompletion;
        if ((flags & NodeFlags.BlockScoped) != 0)
        {
            node.TransformFlags |= TransformFlags.ContainsES2015 |
                TransformFlags.ContainsBlockScopedBinding;
        }
        if ((flags & NodeFlags.Using) != 0)
        {
            node.TransformFlags |= TransformFlags.ContainsESNext;
        }
        return node;
    }

    // @api
    internal FunctionDeclaration CreateFunctionDeclaration(
        IReadOnlyList<Node>? modifiers,
        AsteriskToken? asteriskToken,
        Identifier? name,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node> parameters,
        Node? type,
        Block? body)
    {
        var node = (FunctionDeclaration)CreateBaseDeclaration(SyntaxKind.FunctionDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.AsteriskToken = asteriskToken;
        node.Name = AsName(name);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = CreateNodeArray(parameters);
        node.Type = type;
        node.Body = body;

        if (node.Body == null || (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Ambient) != 0)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            var isAsync = (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Async) != 0;
            var isGenerator = node.AsteriskToken != null;
            var isAsyncGenerator = isAsync && isGenerator;

            node.TransformFlags = PropagateChildrenFlags(node.Modifiers) |
                PropagateChildFlags(node.AsteriskToken) |
                PropagateNameFlags(node.Name) |
                PropagateChildrenFlags(node.TypeParameters) |
                PropagateChildrenFlags(node.Parameters) |
                PropagateChildFlags(node.Type) |
                (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait) |
                (isAsyncGenerator ? TransformFlags.ContainsES2018 :
                    isAsync ? TransformFlags.ContainsES2017 :
                    isGenerator ? TransformFlags.ContainsGenerator :
                    TransformFlags.None) |
                (node.TypeParameters != null || node.Type != null ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
                TransformFlags.ContainsHoistedDeclarationOrCompletion;
        }

        node.TypeArguments = null; // used in quick info
        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.EndFlowNode = null;
        node.ReturnFlowNode = null;
        return node;
    }

    // @api
    internal ClassDeclaration CreateClassDeclaration(
        IReadOnlyList<Node>? modifiers,
        Identifier? name,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node>? heritageClauses,
        IReadOnlyList<Node> members)
    {
        var node = (ClassDeclaration)CreateBaseDeclaration(SyntaxKind.ClassDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.HeritageClauses = AsNodeArray(heritageClauses);
        node.Members = CreateNodeArray(members);

        if ((Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Ambient) != 0)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            node.TransformFlags |= PropagateChildrenFlags(node.Modifiers) |
                PropagateNameFlags(node.Name) |
                PropagateChildrenFlags(node.TypeParameters) |
                PropagateChildrenFlags(node.HeritageClauses) |
                PropagateChildrenFlags(node.Members) |
                (node.TypeParameters != null ? TransformFlags.ContainsTypeScript : TransformFlags.None) |
                TransformFlags.ContainsES2015;
            if ((node.TransformFlags & TransformFlags.ContainsTypeScriptClassSyntax) != 0)
            {
                node.TransformFlags |= TransformFlags.ContainsTypeScript;
            }
        }

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal InterfaceDeclaration CreateInterfaceDeclaration(
        IReadOnlyList<Node>? modifiers,
        Identifier name,
        IReadOnlyList<Node>? typeParameters,
        IReadOnlyList<Node>? heritageClauses,
        IReadOnlyList<Node> members)
    {
        var node = (InterfaceDeclaration)CreateBaseDeclaration(SyntaxKind.InterfaceDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.HeritageClauses = AsNodeArray(heritageClauses);
        node.Members = CreateNodeArray(members);
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal TypeAliasDeclaration CreateTypeAliasDeclaration(
        IReadOnlyList<Node>? modifiers,
        Identifier name,
        IReadOnlyList<Node>? typeParameters,
        Node type)
    {
        var node = (TypeAliasDeclaration)CreateBaseDeclaration(SyntaxKind.TypeAliasDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    internal EnumDeclaration CreateEnumDeclaration(
        IReadOnlyList<Node>? modifiers,
        Identifier name,
        IReadOnlyList<Node> members)
    {
        var node = (EnumDeclaration)CreateBaseDeclaration(SyntaxKind.EnumDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.Members = CreateNodeArray(members);
        node.TransformFlags |= PropagateChildrenFlags(node.Modifiers) |
            PropagateChildFlags(node.Name) |
            PropagateChildrenFlags(node.Members) |
            TransformFlags.ContainsTypeScript;
        node.TransformFlags &= ~TransformFlags.ContainsPossibleTopLevelAwait; // Enum declarations cannot contain `await`

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal ModuleDeclaration CreateModuleDeclaration(
        IReadOnlyList<Node>? modifiers,
        Node name,
        Node? body,
        NodeFlags flags = NodeFlags.None)
    {
        var node = (ModuleDeclaration)CreateBaseDeclaration(SyntaxKind.ModuleDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.Flags |= flags & (NodeFlags.Namespace | NodeFlags.NestedNamespace | NodeFlags.GlobalAugmentation);
        node.Name = name;
        node.Body = body;
        if ((Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Ambient) != 0)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            node.TransformFlags |= PropagateChildrenFlags(node.Modifiers) |
                PropagateChildFlags(node.Name) |
                PropagateChildFlags(node.Body) |
                TransformFlags.ContainsTypeScript;
        }
        node.TransformFlags &= ~TransformFlags.ContainsPossibleTopLevelAwait; // Module declarations cannot contain `await`.

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    internal ModuleBlock CreateModuleBlock(IReadOnlyList<Node> statements)
    {
        var node = (ModuleBlock)CreateBaseNode(SyntaxKind.ModuleBlock);
        node.Statements = CreateNodeArray(statements);
        node.TransformFlags |= PropagateChildrenFlags(node.Statements);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal CaseBlock CreateCaseBlock(IReadOnlyList<Node> clauses)
    {
        var node = (CaseBlock)CreateBaseNode(SyntaxKind.CaseBlock);
        node.Clauses = CreateNodeArray(clauses);
        node.TransformFlags |= PropagateChildrenFlags(node.Clauses);

        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    internal NamespaceExportDeclaration CreateNamespaceExportDeclaration(Identifier name)
    {
        var node = (NamespaceExportDeclaration)CreateBaseDeclaration(SyntaxKind.NamespaceExportDeclaration);
        node.Name = AsName(name);
        node.TransformFlags |= PropagateIdentifierNameFlags(node.Name) |
            TransformFlags.ContainsTypeScript;

        node.Modifiers = null; // initialized by parser to report grammar errors
        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal ImportEqualsDeclaration CreateImportEqualsDeclaration(
        IReadOnlyList<Node>? modifiers,
        bool isTypeOnly,
        Identifier name,
        Node moduleReference)
    {
        var node = (ImportEqualsDeclaration)CreateBaseDeclaration(SyntaxKind.ImportEqualsDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.IsTypeOnly = isTypeOnly;
        node.ModuleReference = moduleReference;
        node.TransformFlags |= PropagateChildrenFlags(node.Modifiers) |
            PropagateIdentifierNameFlags(node.Name) |
            PropagateChildFlags(node.ModuleReference);

        if (!NodeTests.IsExternalModuleReference(node.ModuleReference))
        {
            node.TransformFlags |= TransformFlags.ContainsTypeScript;
        }

        node.TransformFlags &= ~TransformFlags.ContainsPossibleTopLevelAwait; // Import= declaration is always parsed in an Await context

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal ImportDeclaration CreateImportDeclaration(
        IReadOnlyList<Node>? modifiers,
        ImportClause? importClause,
        Node moduleSpecifier,
        ImportAttributes? attributes = null)
    {
        var node = (ImportDeclaration)CreateBaseNode(SyntaxKind.ImportDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.ImportClause = importClause;
        node.ModuleSpecifier = moduleSpecifier;
        node.Attributes = attributes;
        node.AssertClause = attributes;
        node.TransformFlags |= PropagateChildFlags(node.ImportClause) |
            PropagateChildFlags(node.ModuleSpecifier);
        node.TransformFlags &= ~TransformFlags.ContainsPossibleTopLevelAwait; // always parsed in an Await context

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    /// <summary>
    /// createImportClause with `phaseModifier: ImportPhaseModifierSyntaxKind | undefined` (the parser's form). The generated
    /// `PhaseModifier` field is a non-nullable SyntaxKind, so undefined is stored as SyntaxKind.Unknown.
    /// </summary>
    // @api
    internal ImportClause CreateImportClause(SyntaxKind? phaseModifier, Identifier? name, Node? namedBindings)
    {
        var node = (ImportClause)CreateBaseDeclaration(SyntaxKind.ImportClause);
        node.IsTypeOnly = phaseModifier == SyntaxKind.TypeKeyword;
        node.PhaseModifier = phaseModifier ?? SyntaxKind.Unknown;
        node.Name = name;
        node.NamedBindings = namedBindings;
        node.TransformFlags |= PropagateChildFlags(node.Name) |
            PropagateChildFlags(node.NamedBindings);
        if (phaseModifier == SyntaxKind.TypeKeyword)
        {
            node.TransformFlags |= TransformFlags.ContainsTypeScript;
        }
        node.TransformFlags &= ~TransformFlags.ContainsPossibleTopLevelAwait; // always parsed in an Await context
        return node;
    }

    /// <summary>createImportClause with `phaseModifier: boolean` (`phaseModifier ? SyntaxKind.TypeKeyword : undefined`).</summary>
    // @api
    internal ImportClause CreateImportClause(bool phaseModifier, Identifier? name, Node? namedBindings)
    {
        return CreateImportClause(phaseModifier ? SyntaxKind.TypeKeyword : (SyntaxKind?)null, name, namedBindings);
    }

    // @api
    internal ImportAttributes CreateImportAttributes(IReadOnlyList<Node> elements, bool multiLine = false, SyntaxKind? token = null)
    {
        var node = (ImportAttributes)CreateBaseNode(SyntaxKind.ImportAttributes);
        node.Token = token ?? SyntaxKind.WithKeyword;
        node.Elements = CreateNodeArray(elements);
        node.MultiLine = multiLine;
        node.TransformFlags |= TransformFlags.ContainsESNext;
        return node;
    }

    // @api
    internal ImportAttribute CreateImportAttribute(Node name, Node value)
    {
        var node = (ImportAttribute)CreateBaseNode(SyntaxKind.ImportAttribute);
        node.Name = name;
        node.Value = value;
        node.TransformFlags |= TransformFlags.ContainsESNext;
        return node;
    }

    // @api
    internal NamespaceImport CreateNamespaceImport(Identifier name)
    {
        var node = (NamespaceImport)CreateBaseDeclaration(SyntaxKind.NamespaceImport);
        node.Name = name;
        node.TransformFlags |= PropagateChildFlags(node.Name);
        node.TransformFlags &= ~TransformFlags.ContainsPossibleTopLevelAwait; // always parsed in an Await context
        return node;
    }

    // @api
    internal NamespaceExport CreateNamespaceExport(Node name)
    {
        var node = (NamespaceExport)CreateBaseDeclaration(SyntaxKind.NamespaceExport);
        node.Name = name;
        node.TransformFlags |= PropagateChildFlags(node.Name) |
            TransformFlags.ContainsES2020;
        node.TransformFlags &= ~TransformFlags.ContainsPossibleTopLevelAwait; // always parsed in an Await context
        return node;
    }

    // @api
    internal NamedImports CreateNamedImports(IReadOnlyList<Node> elements)
    {
        var node = (NamedImports)CreateBaseNode(SyntaxKind.NamedImports);
        node.Elements = CreateNodeArray(elements);
        node.TransformFlags |= PropagateChildrenFlags(node.Elements);
        node.TransformFlags &= ~TransformFlags.ContainsPossibleTopLevelAwait; // always parsed in an Await context
        return node;
    }

    // @api
    internal ImportSpecifier CreateImportSpecifier(bool isTypeOnly, Node? propertyName, Identifier name)
    {
        var node = (ImportSpecifier)CreateBaseDeclaration(SyntaxKind.ImportSpecifier);
        node.IsTypeOnly = isTypeOnly;
        node.PropertyName = propertyName;
        node.Name = name;
        node.TransformFlags |= PropagateChildFlags(node.PropertyName) |
            PropagateChildFlags(node.Name);
        node.TransformFlags &= ~TransformFlags.ContainsPossibleTopLevelAwait; // always parsed in an Await context
        return node;
    }

    // @api
    internal ExportAssignment CreateExportAssignment(
        IReadOnlyList<Node>? modifiers,
        bool? isExportEquals,
        Node expression)
    {
        var node = (ExportAssignment)CreateBaseDeclaration(SyntaxKind.ExportAssignment);
        node.Modifiers = AsNodeArray(modifiers);
        node.IsExportEquals = isExportEquals == true;
        node.Expression = isExportEquals == true
            ? Parenthesizer.ParenthesizeRightSideOfBinary(SyntaxKind.EqualsToken, /*leftSide*/ null, expression)
            : Parenthesizer.ParenthesizeExpressionOfExportDefault(expression);
        node.TransformFlags |= PropagateChildrenFlags(node.Modifiers) | PropagateChildFlags(node.Expression);
        node.TransformFlags &= ~TransformFlags.ContainsPossibleTopLevelAwait; // always parsed in an Await context

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal ExportDeclaration CreateExportDeclaration(
        IReadOnlyList<Node>? modifiers,
        bool isTypeOnly,
        Node? exportClause,
        Node? moduleSpecifier = null,
        ImportAttributes? attributes = null)
    {
        var node = (ExportDeclaration)CreateBaseDeclaration(SyntaxKind.ExportDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.IsTypeOnly = isTypeOnly;
        node.ExportClause = exportClause;
        node.ModuleSpecifier = moduleSpecifier;
        node.Attributes = attributes;
        node.AssertClause = attributes;
        node.TransformFlags |= PropagateChildrenFlags(node.Modifiers) |
            PropagateChildFlags(node.ExportClause) |
            PropagateChildFlags(node.ModuleSpecifier);
        node.TransformFlags &= ~TransformFlags.ContainsPossibleTopLevelAwait; // always parsed in an Await context

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal NamedExports CreateNamedExports(IReadOnlyList<Node> elements)
    {
        var node = (NamedExports)CreateBaseNode(SyntaxKind.NamedExports);
        node.Elements = CreateNodeArray(elements);
        node.TransformFlags |= PropagateChildrenFlags(node.Elements);
        node.TransformFlags &= ~TransformFlags.ContainsPossibleTopLevelAwait; // always parsed in an Await context
        return node;
    }

    // @api
    internal ExportSpecifier CreateExportSpecifier(bool isTypeOnly, Node? propertyName, Node name)
    {
        var node = (ExportSpecifier)CreateBaseNode(SyntaxKind.ExportSpecifier);
        node.IsTypeOnly = isTypeOnly;
        node.PropertyName = AsName(propertyName);
        node.Name = AsName(name);
        node.TransformFlags |= PropagateChildFlags(node.PropertyName) |
            PropagateChildFlags(node.Name);
        node.TransformFlags &= ~TransformFlags.ContainsPossibleTopLevelAwait; // always parsed in an Await context

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    // @api
    internal MissingDeclaration CreateMissingDeclaration()
    {
        var node = (MissingDeclaration)CreateBaseDeclaration(SyntaxKind.MissingDeclaration);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    //
    // Module references
    //

    // @api
    internal ExternalModuleReference CreateExternalModuleReference(Node expression)
    {
        var node = (ExternalModuleReference)CreateBaseNode(SyntaxKind.ExternalModuleReference);
        node.Expression = expression;
        node.TransformFlags |= PropagateChildFlags(node.Expression);
        node.TransformFlags &= ~TransformFlags.ContainsPossibleTopLevelAwait; // always parsed in an Await context
        return node;
    }
}
