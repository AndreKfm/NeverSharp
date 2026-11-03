// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/transformers/namedEvaluation.ts, classThis.ts, factory/utilities.ts (isCallToHelper) and
// utilities.ts (isNamedEvaluation and its predicates)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/transformers/estransforms/namedevaluation.go and classthis.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D3 ported what the ESNext transformer reaches (variable
// declarations, assignment expressions and export assignments), module D4 the rest (property assignments, shorthand
// property assignments, parameters, binding elements, property declarations with getAssignedNameOfPropertyName, the
// callbacks of isNamedEvaluation) and classThis.ts.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>namedEvaluation.ts and the named-evaluation predicates of utilities.ts.</summary>
internal static class NamedEvaluation
{
    /// <summary>utilities.ts isNamedEvaluationSource.</summary>
    internal static bool IsNamedEvaluationSource(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.PropertyAssignment:
                return !IsProtoSetter(store, store.Name(node));
            case SyntaxKind.ShorthandPropertyAssignment:
                return !store.ObjectAssignmentInitializer(node).IsNone;
            case SyntaxKind.VariableDeclaration:
                return store.Kind(store.Name(node)) == SyntaxKind.Identifier && !store.Initializer(node).IsNone;
            case SyntaxKind.Parameter:
            case SyntaxKind.BindingElement:
                return store.Kind(store.Name(node)) == SyntaxKind.Identifier && !store.Initializer(node).IsNone && store.DotDotDotToken(node).IsNone;
            case SyntaxKind.PropertyDeclaration:
                return !store.Initializer(node).IsNone;
            case SyntaxKind.BinaryExpression:
                switch (store.Kind(store.OperatorToken(node)))
                {
                    case SyntaxKind.EqualsToken:
                    case SyntaxKind.AmpersandAmpersandEqualsToken:
                    case SyntaxKind.BarBarEqualsToken:
                    case SyntaxKind.QuestionQuestionEqualsToken:
                        return store.Kind(store.Left(node)) == SyntaxKind.Identifier;
                }
                break;
            case SyntaxKind.ExportAssignment:
                return true;
        }
        return false;
    }

    // utilities.ts isProtoSetter
    private static bool IsProtoSetter(NodeStore store, Node node)
    {
        // idText(node): the unescaped text (the escaped text of `__proto__` is `___proto__`)
        return store.Kind(node) == SyntaxKind.Identifier ? store.Texts.Equals(UtilitiesPublic.IdText(store, node), "__proto__")
            : store.Kind(node) == SyntaxKind.StringLiteral && store.Texts.Equals(store.Text(node), "__proto__");
    }

    /// <summary>utilities.ts isNamedEvaluation(node, cb).</summary>
    internal static bool IsNamedEvaluation(NodeStore store, Node node, Func<Node, bool>? cb = null)
    {
        if (!IsNamedEvaluationSource(store, node))
        {
            return false;
        }
        switch (store.Kind(node))
        {
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.Parameter:
            case SyntaxKind.BindingElement:
            case SyntaxKind.PropertyDeclaration:
                return IsAnonymousFunctionDefinition(store, store.Initializer(node), cb);
            case SyntaxKind.ShorthandPropertyAssignment:
                return IsAnonymousFunctionDefinition(store, store.ObjectAssignmentInitializer(node), cb);
            case SyntaxKind.BinaryExpression:
                return IsAnonymousFunctionDefinition(store, store.Right(node), cb);
            case SyntaxKind.ExportAssignment:
                return IsAnonymousFunctionDefinition(store, store.Expression(node), cb);
        }
        return false;
    }

    /// <summary>utilities.ts isAnonymousFunctionDefinition(node, cb).</summary>
    internal static bool IsAnonymousFunctionDefinition(NodeStore store, Node node, Func<Node, bool>? cb = null)
    {
        node = FactoryUtilities.SkipOuterExpressions(store, node);
        switch (store.Kind(node))
        {
            case SyntaxKind.ClassExpression:
                if (ClassHasDeclaredOrExplicitlyAssignedName(store, node))
                {
                    return false;
                }
                break;
            case SyntaxKind.FunctionExpression:
                if (!store.Name(node).IsNone)
                {
                    return false;
                }
                break;
            case SyntaxKind.ArrowFunction:
                break;
            default:
                return false;
        }
        return cb is null || cb(node);
    }

    /// <summary>Gets a string literal to use as the assigned name of an anonymous class or function declaration.</summary>
    private static Node GetAssignedNameOfIdentifier(NodeFactory factory, Node name, Node expression)
    {
        var store = factory.store;
        var original = TransformerUtilities.GetOriginalNode(store, FactoryUtilities.SkipOuterExpressions(store, expression));
        if (store.Kind(original) is SyntaxKind.ClassDeclaration or SyntaxKind.FunctionDeclaration &&
            store.Name(original).IsNone && Utilities.HasSyntacticModifier(store, original, ModifierFlags.Default))
        {
            return factory.CreateStringLiteral(store.Texts.Add("default"));
        }
        return factory.CreateStringLiteralFromNode(name);
    }

    private static Node FinishTransformNamedEvaluation(TransformationContext context, Node expression, Node assignedName, bool ignoreEmptyStringLiteral)
    {
        var factory = context.Factory;
        var store = factory.store;
        if (ignoreEmptyStringLiteral && store.Kind(assignedName) == SyntaxKind.StringLiteral && store.Texts.Equals(store.Text(assignedName), ""))
        {
            return expression;
        }

        var innerExpression = FactoryUtilities.SkipOuterExpressions(store, expression);

        var updatedExpression = store.Kind(innerExpression) == SyntaxKind.ClassExpression ?
            InjectClassNamedEvaluationHelperBlockIfMissing(context, innerExpression, assignedName) :
            context.GetEmitHelperFactory().CreateSetFunctionNameHelper(innerExpression, assignedName);

        return factory.RestoreOuterExpressions(expression, updatedExpression);
    }

    /// <summary>Creates a class `static {}` block used to dynamically set the name of a class.</summary>
    private static Node CreateClassNamedEvaluationHelperBlock(TransformationContext context, Node assignedName, Node thisExpression)
    {
        // produces:
        //
        //  static { __setFunctionName(this, "C"); }
        //

        var factory = context.Factory;
        var store = factory.store;
        var expression = context.GetEmitHelperFactory().CreateSetFunctionNameHelper(thisExpression.IsNone ? factory.CreateToken(SyntaxKind.ThisKeyword) : thisExpression, assignedName);
        var statement = factory.CreateExpressionStatement(expression);
        var body = factory.CreateBlock(factory.CreateNodeArray(new[] { statement }), multiLine: false);
        var block = factory.CreateClassStaticBlockDeclaration(body);

        // We use `emitNode.assignedName` to indicate this is a NamedEvaluation helper block
        // and to stash the expression used to resolve the assigned name.
        FactoryEmitNode.GetOrCreateEmitNode(store, block).AssignedName = assignedName;
        return block;
    }

    /// <summary>
    /// Gets whether a node is a `static {}` block containing only a single call to the `__setFunctionName` helper where that
    /// call's second argument is the value stored in the `assignedName` property of the block's `EmitNode`.
    /// </summary>
    internal static bool IsClassNamedEvaluationHelperBlock(NodeStore store, Node node)
    {
        if (store.Kind(node) != SyntaxKind.ClassStaticBlockDeclaration || store.Length(store.Statements(store.Body(node))) != 1)
        {
            return false;
        }

        var statement = store.Element(store.Statements(store.Body(node)), 0);
        if (store.Kind(statement) != SyntaxKind.ExpressionStatement)
        {
            return false;
        }
        var call = store.Expression(statement);
        return IsCallToHelper(store, call, "___setFunctionName") &&
            store.Length(store.Arguments(call)) >= 2 &&
            store.EmitNode(node) is EmitNode emitNode && store.Element(store.Arguments(call), 1) == emitNode.AssignedName;
    }

    // factory/utilities.ts isCallToHelper
    private static bool IsCallToHelper(NodeStore store, Node firstSegment, string helperName)
    {
        if (store.Kind(firstSegment) != SyntaxKind.CallExpression)
        {
            return false;
        }
        var expression = store.Expression(firstSegment);
        return store.Kind(expression) == SyntaxKind.Identifier
            && (Utilities.GetEmitFlags(store, expression) & EmitFlags.HelperName) != 0
            && store.Texts.Equals(store.EscapedText(expression), helperName);
    }

    /// <summary>
    /// Gets whether a `ClassLikeDeclaration` has a `static {}` block containing only a single call to the
    /// `__setFunctionName` helper.
    /// </summary>
    internal static bool ClassHasExplicitlyAssignedName(NodeStore store, Node node)
    {
        if (store.EmitNode(node) is not EmitNode { AssignedName.IsNone: false })
        {
            return false;
        }
        foreach (var member in store.Nodes(store.Members(node)))
        {
            if (IsClassNamedEvaluationHelperBlock(store, member))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Gets whether a `ClassLikeDeclaration` has a declared name or contains a `static {}` block containing only a single
    /// call to the `__setFunctionName` helper.
    /// </summary>
    internal static bool ClassHasDeclaredOrExplicitlyAssignedName(NodeStore store, Node node)
    {
        return !store.Name(node).IsNone || ClassHasExplicitlyAssignedName(store, node);
    }

    /// <summary>
    /// classThis.ts isClassThisAssignmentBlock: a `static {}` block containing only a single assignment of `this` to the
    /// class's `_classThis` (or similar) variable.
    /// </summary>
    internal static bool IsClassThisAssignmentBlock(NodeStore store, Node node)
    {
        if (store.Kind(node) != SyntaxKind.ClassStaticBlockDeclaration || store.Length(store.Statements(store.Body(node))) != 1)
        {
            return false;
        }

        var statement = store.Element(store.Statements(store.Body(node)), 0);
        if (store.Kind(statement) != SyntaxKind.ExpressionStatement)
        {
            return false;
        }
        var expression = store.Expression(statement);
        return store.Kind(expression) == SyntaxKind.BinaryExpression &&
            store.Kind(store.OperatorToken(expression)) == SyntaxKind.EqualsToken &&
            store.Kind(store.Left(expression)) == SyntaxKind.Identifier &&
            store.EmitNode(node) is EmitNode emitNode && emitNode.ClassThis == store.Left(expression) &&
            store.Kind(store.Right(expression)) == SyntaxKind.ThisKeyword;
    }

    /// <summary>Injects a class `static {}` block used to dynamically set the name of a class, if one does not already exist.</summary>
    internal static Node InjectClassNamedEvaluationHelperBlockIfMissing(TransformationContext context, Node node, Node assignedName, Node thisExpression = default)
    {
        // given:
        //
        //  let C = class {
        //  };
        //
        // produces:
        //
        //  let C = class {
        //      static { __setFunctionName(this, "C"); }
        //  };

        // NOTE: If the class has a `_classThis` assignment block, this helper will be injected after that block.

        var factory = context.Factory;
        var store = factory.store;
        if (ClassHasExplicitlyAssignedName(store, node))
        {
            return node;
        }

        var namedEvaluationBlock = CreateClassNamedEvaluationHelperBlock(context, assignedName, thisExpression);
        if (!store.Name(node).IsNone)
        {
            FactoryEmitNode.SetSourceMapRange(store, store.Element(store.Statements(store.Body(namedEvaluationBlock)), 0), TransformerUtilities.RangeOf(store, store.Name(node)));
        }

        var nodeMembers = store.Members(node);
        var memberNodes = store.Nodes(nodeMembers);
        var insertionIndex = 0;
        for (var i = 0; i < memberNodes.Length; i++)
        {
            if (IsClassThisAssignmentBlock(store, memberNodes[i]))
            {
                insertionIndex = i + 1;
                break;
            }
        }
        var newMembers = new List<Node>(memberNodes.Length + 1);
        newMembers.AddRange(memberNodes.Slice(0, insertionIndex).ToArray());
        newMembers.Add(namedEvaluationBlock);
        newMembers.AddRange(memberNodes.Slice(insertionIndex).ToArray());
        var members = factory.CreateNodeArray(newMembers);
        FactoryUtilitiesPublic.SetTextRange(store, members, nodeMembers);

        node = store.Kind(node) == SyntaxKind.ClassDeclaration ?
            factory.UpdateClassDeclaration(
                node,
                store.Modifiers(node),
                store.Name(node),
                store.TypeParameters(node),
                store.HeritageClauses(node),
                members) :
            factory.UpdateClassExpression(
                node,
                store.Modifiers(node),
                store.Name(node),
                store.TypeParameters(node),
                store.HeritageClauses(node),
                members);

        FactoryEmitNode.GetOrCreateEmitNode(store, node).AssignedName = assignedName;
        return node;
    }

    /// <summary>
    /// namedEvaluation.ts transformNamedEvaluation: performs a shallow transformation of a `NamedEvaluation` node, such
    /// that a valid name will be assigned. Track O (module D3): variable declarations, assignment expressions and export
    /// assignments.
    /// </summary>
    internal static Node TransformNamedEvaluation(TransformationContext context, Node node, bool ignoreEmptyStringLiteral = false, string? assignedNameText = null)
    {
        var factory = context.Factory;
        var store = factory.store;
        switch (store.Kind(node))
        {
            case SyntaxKind.VariableDeclaration:
            {
                var assignedName = assignedNameText is not null ? factory.CreateStringLiteral(store.Texts.Add(assignedNameText)) :
                    GetAssignedNameOfIdentifier(factory, store.Name(node), store.Initializer(node));
                var initializer = FinishTransformNamedEvaluation(context, store.Initializer(node), assignedName, ignoreEmptyStringLiteral);
                return factory.UpdateVariableDeclaration(
                    node,
                    store.Name(node),
                    store.ExclamationToken(node),
                    store.Type(node),
                    initializer);
            }
            case SyntaxKind.BinaryExpression:
            {
                var assignedName = assignedNameText is not null ? factory.CreateStringLiteral(store.Texts.Add(assignedNameText)) :
                    GetAssignedNameOfIdentifier(factory, store.Left(node), store.Right(node));
                var right = FinishTransformNamedEvaluation(context, store.Right(node), assignedName, ignoreEmptyStringLiteral);
                return factory.UpdateBinaryExpression(
                    node,
                    store.Left(node),
                    store.OperatorToken(node),
                    right);
            }
            case SyntaxKind.ExportAssignment:
            {
                var assignedName = assignedNameText is not null ? factory.CreateStringLiteral(store.Texts.Add(assignedNameText)) :
                    factory.CreateStringLiteral(store.Texts.Add(store.IsExportEquals(node) ? "" : "default"));
                var expression = FinishTransformNamedEvaluation(context, store.Expression(node), assignedName, ignoreEmptyStringLiteral);
                return factory.UpdateExportAssignment(
                    node,
                    store.Modifiers(node),
                    expression);
            }
            case SyntaxKind.PropertyAssignment:
            {
                var (assignedName, name) = GetAssignedNameOfPropertyName(context, store.Name(node), assignedNameText);
                var initializer = FinishTransformNamedEvaluation(context, store.Initializer(node), assignedName, ignoreEmptyStringLiteral);
                return factory.UpdatePropertyAssignment(
                    node,
                    name,
                    initializer);
            }
            case SyntaxKind.ShorthandPropertyAssignment:
            {
                var assignedName = assignedNameText is not null ? factory.CreateStringLiteral(store.Texts.Add(assignedNameText)) :
                    GetAssignedNameOfIdentifier(factory, store.Name(node), store.ObjectAssignmentInitializer(node));
                var objectAssignmentInitializer = FinishTransformNamedEvaluation(context, store.ObjectAssignmentInitializer(node), assignedName, ignoreEmptyStringLiteral);
                return factory.UpdateShorthandPropertyAssignment(
                    node,
                    store.Name(node),
                    objectAssignmentInitializer);
            }
            case SyntaxKind.Parameter:
            {
                var assignedName = assignedNameText is not null ? factory.CreateStringLiteral(store.Texts.Add(assignedNameText)) :
                    GetAssignedNameOfIdentifier(factory, store.Name(node), store.Initializer(node));
                var initializer = FinishTransformNamedEvaluation(context, store.Initializer(node), assignedName, ignoreEmptyStringLiteral);
                return factory.UpdateParameterDeclaration(
                    node,
                    store.Modifiers(node),
                    store.DotDotDotToken(node),
                    store.Name(node),
                    store.QuestionToken(node),
                    store.Type(node),
                    initializer);
            }
            case SyntaxKind.BindingElement:
            {
                var assignedName = assignedNameText is not null ? factory.CreateStringLiteral(store.Texts.Add(assignedNameText)) :
                    GetAssignedNameOfIdentifier(factory, store.Name(node), store.Initializer(node));
                var initializer = FinishTransformNamedEvaluation(context, store.Initializer(node), assignedName, ignoreEmptyStringLiteral);
                return factory.UpdateBindingElement(
                    node,
                    store.DotDotDotToken(node),
                    store.PropertyName(node),
                    store.Name(node),
                    initializer);
            }
            case SyntaxKind.PropertyDeclaration:
            {
                var (assignedName, name) = GetAssignedNameOfPropertyName(context, store.Name(node), assignedNameText);
                var initializer = FinishTransformNamedEvaluation(context, store.Initializer(node), assignedName, ignoreEmptyStringLiteral);
                return factory.UpdatePropertyDeclaration(
                    node,
                    store.Modifiers(node),
                    name,
                    store.QuestionToken(node).IsNone ? store.ExclamationToken(node) : store.QuestionToken(node),
                    store.Type(node),
                    initializer);
            }
            default:
                throw Debug.UnexpectedKind(store.Kind(node), "a NamedEvaluation");
        }
    }

    // namedEvaluation.ts getAssignedNameOfPropertyName (module D4)
    private static (Node AssignedName, Node Name) GetAssignedNameOfPropertyName(TransformationContext context, Node name, string? assignedNameText)
    {
        var factory = context.Factory;
        var store = factory.store;
        if (assignedNameText is not null)
        {
            return (factory.CreateStringLiteral(store.Texts.Add(assignedNameText)), name);
        }

        if (Utilities.IsPropertyNameLiteral(store, name) || store.Kind(name) == SyntaxKind.PrivateIdentifier)
        {
            return (factory.CreateStringLiteralFromNode(name), name);
        }

        var expression = store.Expression(name);
        if (Utilities.IsPropertyNameLiteral(store, expression) && store.Kind(expression) != SyntaxKind.Identifier)
        {
            return (factory.CreateStringLiteralFromNode(expression), name);
        }

        var assignedName = factory.GetGeneratedNameForNode(name);
        context.HoistVariableDeclaration(assignedName);

        var key = context.GetEmitHelperFactory().CreatePropKeyHelper(expression);
        var assignment = factory.CreateAssignment(assignedName, key);
        var updatedName = factory.UpdateComputedPropertyName(name, assignment);
        return (assignedName, updatedName);
    }

    //// classThis.ts (module D4)

    // classThis.ts createClassThisAssignmentBlock
    private static Node CreateClassThisAssignmentBlock(NodeFactory factory, Node classThis, Node thisExpression)
    {
        // produces:
        //
        //  static { _classThis = this; }
        //

        var store = factory.store;
        if (thisExpression.IsNone)
        {
            thisExpression = factory.CreateToken(SyntaxKind.ThisKeyword);
        }
        var expression = factory.CreateAssignment(classThis, thisExpression);
        var statement = factory.CreateExpressionStatement(expression);
        var body = factory.CreateBlock(factory.CreateNodeArray(new[] { statement }), multiLine: false);
        var block = factory.CreateClassStaticBlockDeclaration(body);

        // We use `emitNode.classThis` to indicate this is a `_classThis` assignment helper block
        // and to stash the variable used for `_classThis`.
        FactoryEmitNode.GetOrCreateEmitNode(store, block).ClassThis = classThis;

        return block;
    }

    /// <summary>
    /// classThis.ts classHasClassThisAssignment: whether a class has a `static {}` block containing only a single
    /// assignment to a `_classThis` (or similar) variable.
    /// </summary>
    internal static bool ClassHasClassThisAssignment(NodeStore store, Node node)
    {
        if (store.EmitNode(node) is not EmitNode { ClassThis.IsNone: false })
        {
            return false;
        }
        foreach (var member in store.Nodes(store.Members(node)))
        {
            if (IsClassThisAssignmentBlock(store, member))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// classThis.ts injectClassThisAssignmentIfMissing: injects a class `static {}` block containing only an assignment of
    /// the static `this` to a `_classThis` (or similar) variable.
    /// </summary>
    internal static Node InjectClassThisAssignmentIfMissing(NodeFactory factory, Node node, Node classThis, Node thisExpression = default)
    {
        // given:
        //
        //  class C {
        //  }
        //
        // produces:
        //
        //  class C {
        //      static { _classThis = this; }
        //  }

        var store = factory.store;
        if (ClassHasClassThisAssignment(store, node))
        {
            return node;
        }

        var staticBlock = CreateClassThisAssignmentBlock(factory, classThis, thisExpression);
        if (!store.Name(node).IsNone)
        {
            FactoryEmitNode.SetSourceMapRange(store, store.Element(store.Statements(store.Body(staticBlock)), 0), TransformerUtilities.RangeOf(store, store.Name(node)));
        }

        var nodeMembers = store.Members(node);
        var list = new List<Node>(store.Length(nodeMembers) + 1) { staticBlock };
        list.AddRange(store.Nodes(nodeMembers));
        var members = factory.CreateNodeArray(list);
        FactoryUtilitiesPublic.SetTextRange(store, members, nodeMembers);

        var updatedNode = store.Kind(node) == SyntaxKind.ClassDeclaration ?
            factory.UpdateClassDeclaration(
                node,
                store.Modifiers(node),
                store.Name(node),
                store.TypeParameters(node),
                store.HeritageClauses(node),
                members) :
            factory.UpdateClassExpression(
                node,
                store.Modifiers(node),
                store.Name(node),
                store.TypeParameters(node),
                store.HeritageClauses(node),
                members);

        FactoryEmitNode.GetOrCreateEmitNode(store, updatedNode).ClassThis = classThis;
        return updatedNode;
    }
}
