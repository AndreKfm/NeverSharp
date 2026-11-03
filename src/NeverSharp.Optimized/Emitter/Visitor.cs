// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/visitorPublic.ts (visitNode, visitNodes, visitArray, visitLexicalEnvironment,
// visitParameterList, visitFunctionBody, visitIterationBody, visitCommaListElements, visitEachChild and its table)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/visitor.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D1. The node tests the visitors assert (`Debug.assertNode`) are
// left out; no transformer of the default options passes a nodesVisitor, nodeVisitor or tokenVisitor of its own, so
// those parameters are left out too (tokens are kept as they are).

using System.Runtime.InteropServices;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// types.ts VisitResult: what a visitor returns for a node: undefined (<see cref="None"/>: the node is removed), one
/// node, or several nodes.
/// </summary>
internal readonly struct VisitResult
{
    internal readonly Node Node;
    internal readonly IReadOnlyList<Node>? Nodes;

    private VisitResult(Node node, IReadOnlyList<Node>? nodes)
    {
        Node = node;
        Nodes = nodes;
    }

    /// <summary>undefined.</summary>
    internal static VisitResult None => default;

    /// <summary>Whether the result is undefined.</summary>
    internal bool IsNone => Node.IsNone && Nodes is null;

    /// <summary>An array of nodes (TypeScript: `Node[]`).</summary>
    internal static VisitResult Many(IReadOnlyList<Node> nodes) => new(default, nodes);

    public static implicit operator VisitResult(Node node) => new(node, null);
}

/// <summary>visitorPublic.ts: the node visitors.</summary>
internal static class Visitors
{
    /// <summary>
    /// Visits a Node using the supplied visitor, possibly returning a new Node in its place. A visitor result of
    /// several nodes is lifted to one (<paramref name="lift"/>, or extractSingleNode).
    /// </summary>
    internal static Node VisitNode(NodeFactory factory, Node node, Func<Node, VisitResult> visitor, Func<IReadOnlyList<Node>, Node>? lift = null)
    {
        if (node.IsNone)
        {
            // If the input type is undefined, then the output type can be undefined.
            return node;
        }

        var visited = visitor(node);
        if (visited.Nodes is not null)
        {
            return (lift ?? ExtractSingleNode)(visited.Nodes);
        }
        return visited.Node;
    }

    /// <summary>Extracts the single node from a NodeArray.</summary>
    private static Node ExtractSingleNode(IReadOnlyList<Node> nodes)
    {
        Debug.Assert(nodes.Count <= 1, "Too many nodes written to output.");
        return nodes.Count == 1 ? nodes[0] : Node.None;
    }

    /// <summary>Visits a NodeArray using the supplied visitor, possibly returning a new NodeArray in its place.</summary>
    internal static NodeList VisitNodes(NodeFactory factory, NodeList nodes, Func<Node, VisitResult> visitor, int start = -1, int count = -1)
    {
        if (nodes.IsNone)
        {
            // If the input type is undefined, then the output type can be undefined.
            return nodes;
        }

        var store = factory.store;

        // Ensure start and count have valid values
        var length = store.Length(nodes);
        if (start < 0)
        {
            start = 0;
        }

        if (count < 0 || count > length - start)
        {
            count = length - start;
        }

        bool hasTrailingComma;
        var pos = -1;
        var end = -1;
        if (start > 0 || count < length)
        {
            // Since this is a fragment of a node array, we do not copy over the previous location
            // and will only copy over `hasTrailingComma` if we are including the last element.
            hasTrailingComma = store.HasTrailingComma(nodes) && start + count == length;
        }
        else
        {
            pos = store.Pos(nodes);
            end = store.End(nodes);
            hasTrailingComma = store.HasTrailingComma(nodes);
        }

        var updated = VisitArrayWorker(store, nodes, visitor, start, count);
        if (updated is not null)
        {
            // TODO(rbuckton): Remove dependency on `ts.factory` in favor of a provided factory.
            var updatedArray = NodeFactory.CreateNodeArray(store, CollectionsMarshal.AsSpan(updated), hasTrailingComma);
            Utilities.SetTextRangePosEnd(store, updatedArray, pos, end);
            return updatedArray;
        }

        // If we are here, updated === nodes. This means that it's still a NodeArray.
        return nodes;
    }

    /// <summary>
    /// visitArrayWorker: the visited elements, or null when every element visited to itself (the array is returned
    /// unchanged).
    /// </summary>
    private static List<Node>? VisitArrayWorker(NodeStore store, NodeList nodes, Func<Node, VisitResult> visitor, int start, int count)
    {
        List<Node>? updated = null;

        var length = store.Length(nodes);
        if (start > 0 || count < length)
        {
            // If we are not visiting all of the original nodes, we must always create a new array.
            updated = [];
        }

        // Visit each original node.
        for (var i = 0; i < count; i++)
        {
            var node = store.Element(nodes, i + start);
            var visited = !node.IsNone ? visitor(node) : VisitResult.None;
            if (updated is not null || visited.IsNone || visited.Nodes is not null || visited.Node != node)
            {
                if (updated is null)
                {
                    // Ensure we have a copy of `nodes`, up to the current index.
                    updated = new List<Node>(store.Nodes(nodes).Slice(0, i).ToArray());
                }
                if (visited.Nodes is not null)
                {
                    foreach (var visitedNode in visited.Nodes)
                    {
                        updated.Add(visitedNode);
                    }
                }
                else if (!visited.Node.IsNone)
                {
                    updated.Add(visited.Node);
                }
            }
        }

        return updated;
    }

    /// <summary>visitArray: the visited elements of a plain array (the array itself when nothing changed).</summary>
    internal static List<Node> VisitArray(NodeStore store, List<Node> nodes, Func<Node, VisitResult> visitor)
    {
        List<Node>? updated = null;
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var visited = visitor(node);
            if (updated is not null || visited.IsNone || visited.Nodes is not null || visited.Node != node)
            {
                updated ??= nodes.GetRange(0, i);
                if (visited.Nodes is not null)
                {
                    updated.AddRange(visited.Nodes);
                }
                else if (!visited.Node.IsNone)
                {
                    updated.Add(visited.Node);
                }
            }
        }
        return updated ?? nodes;
    }

    /// <summary>
    /// Starts a new lexical environment and visits a statement list, ending the lexical environment and merging hoisted
    /// declarations upon completion.
    /// </summary>
    internal static NodeList VisitLexicalEnvironment(NodeList statements, Func<Node, VisitResult> visitor, TransformationContext context, int start = -1, bool ensureUseStrict = false)
    {
        context.StartLexicalEnvironment();
        statements = VisitNodes(context.Factory, statements, visitor, start);
        if (ensureUseStrict)
        {
            statements = context.Factory.EnsureUseStrict(statements);
        }
        return context.Factory.MergeLexicalEnvironment(statements, context.EndLexicalEnvironment());
    }

    /// <summary>
    /// Starts a new lexical environment and visits a parameter list, suspending the lexical
    /// environment upon completion.
    /// </summary>
    internal static NodeList VisitParameterList(NodeList nodes, Func<Node, VisitResult> visitor, TransformationContext context)
    {
        var updated = NodeList.None;
        context.StartLexicalEnvironment();
        if (!nodes.IsNone)
        {
            context.SetLexicalEnvironmentFlags(LexicalEnvironmentFlags.InParameters, true);
            updated = VisitNodes(context.Factory, nodes, visitor);

            // As of ES2015, any runtime execution of that occurs in for a parameter (such as evaluating an
            // initializer or a binding pattern), occurs in its own lexical scope. As a result, any expression
            // that we might transform that introduces a temporary variable would fail as the temporary variable
            // exists in a different lexical scope. To address this, we move any binding patterns and initializers
            // in a parameter list to the body if we detect a variable being hoisted while visiting a parameter list
            // when the emit target is greater than ES2015.
            if ((context.GetLexicalEnvironmentFlags() & LexicalEnvironmentFlags.VariablesHoistedInParameters) != 0 &&
                context.Options.GetEmitScriptTarget() >= ScriptTarget.ES2015)
            {
                updated = AddDefaultValueAssignmentsIfNeeded(updated, context);
            }
            context.SetLexicalEnvironmentFlags(LexicalEnvironmentFlags.InParameters, false);
        }
        context.SuspendLexicalEnvironment();
        return updated;
    }

    private static NodeList AddDefaultValueAssignmentsIfNeeded(NodeList parameters, TransformationContext context)
    {
        var store = context.Store;
        List<Node>? result = null;
        var length = store.Length(parameters);
        for (var i = 0; i < length; i++)
        {
            var parameter = store.Element(parameters, i);
            var updated = AddDefaultValueAssignmentIfNeeded(parameter, context);
            if (result is not null || updated != parameter)
            {
                result ??= new List<Node>(store.Nodes(parameters).Slice(0, i).ToArray());
                result.Add(updated);
            }
        }
        if (result is not null)
        {
            return FactoryUtilitiesPublic.SetTextRange(store, context.Factory.CreateNodeArray(result, store.HasTrailingComma(parameters)), parameters);
        }
        return parameters;
    }

    private static Node AddDefaultValueAssignmentIfNeeded(Node parameter, TransformationContext context)
    {
        var store = context.Store;
        // A rest parameter cannot have a binding pattern or an initializer,
        // so let's just ignore it.
        return !store.DotDotDotToken(parameter).IsNone ? parameter :
            store.Kind(store.Name(parameter)) is SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern ? AddDefaultValueAssignmentForBindingPattern(parameter, context) :
            !store.Initializer(parameter).IsNone ? AddDefaultValueAssignmentForInitializer(parameter, store.Name(parameter), store.Initializer(parameter), context) :
            parameter;
    }

    private static Node AddDefaultValueAssignmentForBindingPattern(Node parameter, TransformationContext context)
    {
        var factory = context.Factory;
        var store = context.Store;
        var initializer = store.Initializer(parameter);
        context.AddInitializationStatement(
            factory.CreateVariableStatement(
                default,
                factory.CreateVariableDeclarationList(factory.CreateNodeArray(new[]
                {
                    factory.CreateVariableDeclaration(
                        store.Name(parameter),
                        default,
                        store.Type(parameter),
                        !initializer.IsNone ?
                            factory.CreateConditionalExpression(
                                factory.CreateStrictEquality(
                                    factory.GetGeneratedNameForNode(parameter),
                                    factory.CreateVoidZero()),
                                default,
                                initializer,
                                default,
                                factory.GetGeneratedNameForNode(parameter)) :
                            factory.GetGeneratedNameForNode(parameter)),
                }))));
        return factory.UpdateParameterDeclaration(parameter, store.Modifiers(parameter), store.DotDotDotToken(parameter), factory.GetGeneratedNameForNode(parameter), store.QuestionToken(parameter), store.Type(parameter), default);
    }

    private static Node AddDefaultValueAssignmentForInitializer(Node parameter, Node name, Node initializer, TransformationContext context)
    {
        var factory = context.Factory;
        var store = context.Store;
        var assignment = factory.CreateAssignment(
            FactoryEmitNode.SetEmitFlags(store, factory.CloneNode(name), EmitFlags.NoSourceMap),
            FactoryEmitNode.SetEmitFlags(store, initializer, EmitFlags.NoSourceMap | Utilities.GetEmitFlags(store, initializer) | EmitFlags.NoComments));
        FactoryUtilitiesPublic.SetTextRange(store, assignment, parameter);
        var statement = factory.CreateExpressionStatement(FactoryEmitNode.SetEmitFlags(store, assignment, EmitFlags.NoComments));
        var block = factory.CreateBlock(factory.CreateNodeArray(new[] { statement }));
        FactoryUtilitiesPublic.SetTextRange(store, block, parameter);
        FactoryEmitNode.SetEmitFlags(store, block, EmitFlags.SingleLine | EmitFlags.NoTrailingSourceMap | EmitFlags.NoTokenSourceMaps | EmitFlags.NoComments);
        context.AddInitializationStatement(
            factory.CreateIfStatement(
                factory.CreateTypeCheck(factory.CloneNode(name), "undefined"),
                block));
        return factory.UpdateParameterDeclaration(parameter, store.Modifiers(parameter), store.DotDotDotToken(parameter), store.Name(parameter), store.QuestionToken(parameter), store.Type(parameter), default);
    }

    /// <summary>
    /// Resumes a suspended lexical environment and visits a function body, ending the lexical
    /// environment and merging hoisted declarations upon completion.
    /// </summary>
    internal static Node VisitFunctionBody(Node node, Func<Node, VisitResult> visitor, TransformationContext context)
    {
        context.ResumeLexicalEnvironment();
        var updated = VisitNode(context.Factory, node, visitor);
        var declarations = context.EndLexicalEnvironment();
        if (Core.Some(declarations))
        {
            if (updated.IsNone)
            {
                return context.Factory.CreateBlock(context.Factory.CreateNodeArray(declarations));
            }
            var block = context.Factory.Converters.ConvertToFunctionBlock(updated);
            var statements = context.Factory.MergeLexicalEnvironment(context.Store.Statements(block), declarations);
            return context.Factory.UpdateBlock(block, statements);
        }
        return updated;
    }

    /// <summary>Visits an iteration body, adding any block-scoped variables required by the transformation.</summary>
    internal static Node VisitIterationBody(Node body, Func<Node, VisitResult> visitor, TransformationContext context)
    {
        context.StartBlockScope();
        var updated = VisitNode(context.Factory, body, visitor, context.Factory.LiftToBlock);
        Debug.Assert(!updated.IsNone);
        var declarations = context.EndBlockScope();
        if (Core.Some(declarations))
        {
            if (context.Store.Kind(updated) == SyntaxKind.Block)
            {
                declarations.AddRange(context.Store.Nodes(context.Store.Statements(updated)));
                return context.Factory.UpdateBlock(updated, context.Factory.CreateNodeArray(declarations));
            }
            declarations.Add(updated);
            return context.Factory.CreateBlock(context.Factory.CreateNodeArray(declarations));
        }
        return updated;
    }

    /// <summary>Visits the elements of a comma list, the last one with <paramref name="visitor"/>, the others with <paramref name="discardVisitor"/>.</summary>
    internal static NodeList VisitCommaListElements(NodeFactory factory, NodeList elements, Func<Node, VisitResult> visitor, Func<Node, VisitResult>? discardVisitor = null)
    {
        discardVisitor ??= visitor;
        var store = factory.store;
        if (discardVisitor == visitor || store.Length(elements) <= 1)
        {
            return VisitNodes(factory, elements, visitor);
        }

        var i = 0;
        var length = store.Length(elements);
        return VisitNodes(factory, elements, node =>
        {
            var discarded = i < length - 1;
            i++;
            return discarded ? discardVisitor(node) : visitor(node);
        });
    }

    /// <summary>
    /// Visits each child of a Node using the supplied visitor, possibly returning a new Node of the same kind in its
    /// place (visitEachChildTable; kinds without an entry are returned as they are).
    /// </summary>
    internal static Node VisitEachChild(Node node, Func<Node, VisitResult> visitor, TransformationContext context)
    {
        if (node.IsNone)
        {
            return node;
        }
        var f = context.Factory;
        var s = context.Store;
        Node V(Node child) => VisitNode(f, child, visitor);
        Node VL(Node child) => VisitNode(f, child, visitor, f.LiftToBlock);
        NodeList L(NodeList list) => VisitNodes(f, list, visitor);
        switch (s.Kind(node))
        {
            case SyntaxKind.QualifiedName:
                return f.UpdateQualifiedName(node, V(s.Left(node)), V(s.Right(node)));
            case SyntaxKind.ComputedPropertyName:
                return f.UpdateComputedPropertyName(node, V(s.Expression(node)));

            // Signature elements
            case SyntaxKind.TypeParameter:
                return f.UpdateTypeParameterDeclaration(node, L(s.Modifiers(node)), V(s.Name(node)), V(s.Constraint(node)), V(s.Default(node)));
            case SyntaxKind.Parameter:
                return f.UpdateParameterDeclaration(node, L(s.Modifiers(node)), s.DotDotDotToken(node), V(s.Name(node)), s.QuestionToken(node), V(s.Type(node)), V(s.Initializer(node)));
            case SyntaxKind.Decorator:
                return f.UpdateDecorator(node, V(s.Expression(node)));

            // Type elements
            case SyntaxKind.PropertySignature:
                return f.UpdatePropertySignature(node, L(s.Modifiers(node)), V(s.Name(node)), s.QuestionToken(node), V(s.Type(node)));
            case SyntaxKind.PropertyDeclaration:
                // QuestionToken and ExclamationToken are mutually exclusive in PropertyDeclaration
                return f.UpdatePropertyDeclaration(node, L(s.Modifiers(node)), V(s.Name(node)), s.QuestionToken(node).IsNone ? s.ExclamationToken(node) : s.QuestionToken(node), V(s.Type(node)), V(s.Initializer(node)));
            case SyntaxKind.MethodSignature:
                return f.UpdateMethodSignature(node, L(s.Modifiers(node)), V(s.Name(node)), s.QuestionToken(node), L(s.TypeParameters(node)), L(s.Parameters(node)), V(s.Type(node)));
            case SyntaxKind.MethodDeclaration:
            {
                var modifiers = L(s.Modifiers(node));
                var name = V(s.Name(node));
                var typeParameters = L(s.TypeParameters(node));
                var parameters = VisitParameterList(s.Parameters(node), visitor, context);
                var type = V(s.Type(node));
                return f.UpdateMethodDeclaration(node, modifiers, s.AsteriskToken(node), name, s.QuestionToken(node), typeParameters, parameters, type, VisitFunctionBody(s.Body(node), visitor, context));
            }
            case SyntaxKind.Constructor:
            {
                var modifiers = L(s.Modifiers(node));
                var parameters = VisitParameterList(s.Parameters(node), visitor, context);
                return f.UpdateConstructorDeclaration(node, modifiers, parameters, VisitFunctionBody(s.Body(node), visitor, context));
            }
            case SyntaxKind.GetAccessor:
            {
                var modifiers = L(s.Modifiers(node));
                var name = V(s.Name(node));
                var parameters = VisitParameterList(s.Parameters(node), visitor, context);
                var type = V(s.Type(node));
                return f.UpdateGetAccessorDeclaration(node, modifiers, name, parameters, type, VisitFunctionBody(s.Body(node), visitor, context));
            }
            case SyntaxKind.SetAccessor:
            {
                var modifiers = L(s.Modifiers(node));
                var name = V(s.Name(node));
                var parameters = VisitParameterList(s.Parameters(node), visitor, context);
                return f.UpdateSetAccessorDeclaration(node, modifiers, name, parameters, VisitFunctionBody(s.Body(node), visitor, context));
            }
            case SyntaxKind.ClassStaticBlockDeclaration:
                context.StartLexicalEnvironment();
                context.SuspendLexicalEnvironment();
                return f.UpdateClassStaticBlockDeclaration(node, VisitFunctionBody(s.Body(node), visitor, context));
            case SyntaxKind.CallSignature:
                return f.UpdateCallSignature(node, L(s.TypeParameters(node)), L(s.Parameters(node)), V(s.Type(node)));
            case SyntaxKind.ConstructSignature:
                return f.UpdateConstructSignature(node, L(s.TypeParameters(node)), L(s.Parameters(node)), V(s.Type(node)));
            case SyntaxKind.IndexSignature:
                return f.UpdateIndexSignature(node, L(s.Modifiers(node)), L(s.Parameters(node)), V(s.Type(node)));

            // Types
            case SyntaxKind.TypePredicate:
                return f.UpdateTypePredicateNode(node, V(s.AssertsModifier(node)), V(s.ParameterName(node)), V(s.Type(node)));
            case SyntaxKind.TypeReference:
                return f.UpdateTypeReferenceNode(node, V(s.TypeName(node)), L(s.TypeArguments(node)));
            case SyntaxKind.FunctionType:
                return f.UpdateFunctionTypeNode(node, L(s.TypeParameters(node)), L(s.Parameters(node)), V(s.Type(node)));
            case SyntaxKind.ConstructorType:
                return f.UpdateConstructorTypeNode(node, L(s.Modifiers(node)), L(s.TypeParameters(node)), L(s.Parameters(node)), V(s.Type(node)));
            case SyntaxKind.TypeQuery:
                return f.UpdateTypeQueryNode(node, V(s.ExprName(node)), L(s.TypeArguments(node)));
            case SyntaxKind.TypeLiteral:
                return f.UpdateTypeLiteralNode(node, L(s.Members(node)));
            case SyntaxKind.ArrayType:
                return f.UpdateArrayTypeNode(node, V(s.ElementType(node)));
            case SyntaxKind.TupleType:
                return f.UpdateTupleTypeNode(node, L(s.Elements(node)));
            case SyntaxKind.OptionalType:
                return f.UpdateOptionalTypeNode(node, V(s.Type(node)));
            case SyntaxKind.RestType:
                return f.UpdateRestTypeNode(node, V(s.Type(node)));
            case SyntaxKind.UnionType:
                return f.UpdateUnionTypeNode(node, L(s.Types(node)));
            case SyntaxKind.IntersectionType:
                return f.UpdateIntersectionTypeNode(node, L(s.Types(node)));
            case SyntaxKind.ConditionalType:
                return f.UpdateConditionalTypeNode(node, V(s.CheckType(node)), V(s.ExtendsType(node)), V(s.TrueType(node)), V(s.FalseType(node)));
            case SyntaxKind.InferType:
                return f.UpdateInferTypeNode(node, V(s.TypeParameter(node)));
            case SyntaxKind.ImportType:
                return f.UpdateImportTypeNode(node, V(s.Argument(node)), V(s.Attributes(node)), V(s.Qualifier(node)), L(s.TypeArguments(node)), s.IsTypeOf(node));
            case SyntaxKind.NamedTupleMember:
                return f.UpdateNamedTupleMember(node, s.DotDotDotToken(node), V(s.Name(node)), s.QuestionToken(node), V(s.Type(node)));
            case SyntaxKind.ParenthesizedType:
                return f.UpdateParenthesizedType(node, V(s.Type(node)));
            case SyntaxKind.TypeOperator:
                return f.UpdateTypeOperatorNode(node, V(s.Type(node)));
            case SyntaxKind.IndexedAccessType:
                return f.UpdateIndexedAccessTypeNode(node, V(s.ObjectType(node)), V(s.IndexType(node)));
            case SyntaxKind.MappedType:
                return f.UpdateMappedTypeNode(node, s.ReadonlyToken(node), V(s.TypeParameter(node)), V(s.NameType(node)), s.QuestionToken(node), V(s.Type(node)), L(s.Members(node)));
            case SyntaxKind.LiteralType:
                return f.UpdateLiteralTypeNode(node, V(s.Literal(node)));
            case SyntaxKind.TemplateLiteralType:
                return f.UpdateTemplateLiteralType(node, V(s.Head(node)), L(s.TemplateSpans(node)));
            case SyntaxKind.TemplateLiteralTypeSpan:
                return f.UpdateTemplateLiteralTypeSpan(node, V(s.Type(node)), V(s.Literal(node)));

            // Binding patterns
            case SyntaxKind.ObjectBindingPattern:
                return f.UpdateObjectBindingPattern(node, L(s.Elements(node)));
            case SyntaxKind.ArrayBindingPattern:
                return f.UpdateArrayBindingPattern(node, L(s.Elements(node)));
            case SyntaxKind.BindingElement:
                return f.UpdateBindingElement(node, s.DotDotDotToken(node), V(s.PropertyName(node)), V(s.Name(node)), V(s.Initializer(node)));

            // Expression
            case SyntaxKind.ArrayLiteralExpression:
                return f.UpdateArrayLiteralExpression(node, L(s.Elements(node)));
            case SyntaxKind.ObjectLiteralExpression:
                return f.UpdateObjectLiteralExpression(node, L(s.Properties(node)));
            case SyntaxKind.PropertyAccessExpression:
                return NodeFactory.IsPropertyAccessChain(s, node)
                    ? f.UpdatePropertyAccessChain(node, V(s.Expression(node)), s.QuestionDotToken(node), V(s.Name(node)))
                    : f.UpdatePropertyAccessExpression(node, V(s.Expression(node)), V(s.Name(node)));
            case SyntaxKind.ElementAccessExpression:
                return NodeFactory.IsElementAccessChain(s, node)
                    ? f.UpdateElementAccessChain(node, V(s.Expression(node)), s.QuestionDotToken(node), V(s.ArgumentExpression(node)))
                    : f.UpdateElementAccessExpression(node, V(s.Expression(node)), V(s.ArgumentExpression(node)));
            case SyntaxKind.CallExpression:
                return UtilitiesPublic.IsCallChain(s, node)
                    ? f.UpdateCallChain(node, V(s.Expression(node)), s.QuestionDotToken(node), L(s.TypeArguments(node)), L(s.Arguments(node)))
                    : f.UpdateCallExpression(node, V(s.Expression(node)), L(s.TypeArguments(node)), L(s.Arguments(node)));
            case SyntaxKind.NewExpression:
                return f.UpdateNewExpression(node, V(s.Expression(node)), L(s.TypeArguments(node)), L(s.Arguments(node)));
            case SyntaxKind.TaggedTemplateExpression:
                return f.UpdateTaggedTemplateExpression(node, V(s.Tag(node)), L(s.TypeArguments(node)), V(s.Template(node)));
            case SyntaxKind.TypeAssertionExpression:
                return f.UpdateTypeAssertion(node, V(s.Type(node)), V(s.Expression(node)));
            case SyntaxKind.ParenthesizedExpression:
                return f.UpdateParenthesizedExpression(node, V(s.Expression(node)));
            case SyntaxKind.FunctionExpression:
            {
                var modifiers = L(s.Modifiers(node));
                var name = V(s.Name(node));
                var typeParameters = L(s.TypeParameters(node));
                var parameters = VisitParameterList(s.Parameters(node), visitor, context);
                var type = V(s.Type(node));
                return f.UpdateFunctionExpression(node, modifiers, s.AsteriskToken(node), name, typeParameters, parameters, type, VisitFunctionBody(s.Body(node), visitor, context));
            }
            case SyntaxKind.ArrowFunction:
            {
                var modifiers = L(s.Modifiers(node));
                var typeParameters = L(s.TypeParameters(node));
                var parameters = VisitParameterList(s.Parameters(node), visitor, context);
                var type = V(s.Type(node));
                return f.UpdateArrowFunction(node, modifiers, typeParameters, parameters, type, s.EqualsGreaterThanToken(node), VisitFunctionBody(s.Body(node), visitor, context));
            }
            case SyntaxKind.DeleteExpression:
                return f.UpdateDeleteExpression(node, V(s.Expression(node)));
            case SyntaxKind.TypeOfExpression:
                return f.UpdateTypeOfExpression(node, V(s.Expression(node)));
            case SyntaxKind.VoidExpression:
                return f.UpdateVoidExpression(node, V(s.Expression(node)));
            case SyntaxKind.AwaitExpression:
                return f.UpdateAwaitExpression(node, V(s.Expression(node)));
            case SyntaxKind.PrefixUnaryExpression:
                return f.UpdatePrefixUnaryExpression(node, V(s.Operand(node)));
            case SyntaxKind.PostfixUnaryExpression:
                return f.UpdatePostfixUnaryExpression(node, V(s.Operand(node)));
            case SyntaxKind.BinaryExpression:
                return f.UpdateBinaryExpression(node, V(s.Left(node)), s.OperatorToken(node), V(s.Right(node)));
            case SyntaxKind.ConditionalExpression:
                return f.UpdateConditionalExpression(node, V(s.Condition(node)), s.QuestionToken(node), V(s.WhenTrue(node)), s.ColonToken(node), V(s.WhenFalse(node)));
            case SyntaxKind.TemplateExpression:
                return f.UpdateTemplateExpression(node, V(s.Head(node)), L(s.TemplateSpans(node)));
            case SyntaxKind.YieldExpression:
                return f.UpdateYieldExpression(node, s.AsteriskToken(node), V(s.Expression(node)));
            case SyntaxKind.SpreadElement:
                return f.UpdateSpreadElement(node, V(s.Expression(node)));
            case SyntaxKind.ClassExpression:
                return f.UpdateClassExpression(node, L(s.Modifiers(node)), V(s.Name(node)), L(s.TypeParameters(node)), L(s.HeritageClauses(node)), L(s.Members(node)));
            case SyntaxKind.ExpressionWithTypeArguments:
                return f.UpdateExpressionWithTypeArguments(node, V(s.Expression(node)), L(s.TypeArguments(node)));
            case SyntaxKind.AsExpression:
                return f.UpdateAsExpression(node, V(s.Expression(node)), V(s.Type(node)));
            case SyntaxKind.SatisfiesExpression:
                return f.UpdateSatisfiesExpression(node, V(s.Expression(node)), V(s.Type(node)));
            case SyntaxKind.NonNullExpression:
                return UtilitiesPublic.IsOptionalChain(s, node)
                    ? f.UpdateNonNullChain(node, V(s.Expression(node)))
                    : f.UpdateNonNullExpression(node, V(s.Expression(node)));
            case SyntaxKind.MetaProperty:
                return f.UpdateMetaProperty(node, V(s.Name(node)));

            // Misc
            case SyntaxKind.TemplateSpan:
                return f.UpdateTemplateSpan(node, V(s.Expression(node)), V(s.Literal(node)));

            // Element
            case SyntaxKind.Block:
                return f.UpdateBlock(node, L(s.Statements(node)));
            case SyntaxKind.VariableStatement:
                return f.UpdateVariableStatement(node, L(s.Modifiers(node)), V(s.DeclarationList(node)));
            case SyntaxKind.ExpressionStatement:
                return f.UpdateExpressionStatement(node, V(s.Expression(node)));
            case SyntaxKind.IfStatement:
                return f.UpdateIfStatement(node, V(s.Expression(node)), VL(s.ThenStatement(node)), VL(s.ElseStatement(node)));
            case SyntaxKind.DoStatement:
            {
                var statement = VisitIterationBody(s.Statement(node), visitor, context);
                return f.UpdateDoStatement(node, statement, V(s.Expression(node)));
            }
            case SyntaxKind.WhileStatement:
            {
                var expression = V(s.Expression(node));
                return f.UpdateWhileStatement(node, expression, VisitIterationBody(s.Statement(node), visitor, context));
            }
            case SyntaxKind.ForStatement:
            {
                var initializer = V(s.Initializer(node));
                var condition = V(s.Condition(node));
                var incrementor = V(s.Incrementor(node));
                return f.UpdateForStatement(node, initializer, condition, incrementor, VisitIterationBody(s.Statement(node), visitor, context));
            }
            case SyntaxKind.ForInStatement:
            {
                var initializer = V(s.Initializer(node));
                var expression = V(s.Expression(node));
                return f.UpdateForInStatement(node, initializer, expression, VisitIterationBody(s.Statement(node), visitor, context));
            }
            case SyntaxKind.ForOfStatement:
            {
                var initializer = V(s.Initializer(node));
                var expression = V(s.Expression(node));
                return f.UpdateForOfStatement(node, s.AwaitModifier(node), initializer, expression, VisitIterationBody(s.Statement(node), visitor, context));
            }
            case SyntaxKind.ContinueStatement:
                return f.UpdateContinueStatement(node, V(s.Label(node)));
            case SyntaxKind.BreakStatement:
                return f.UpdateBreakStatement(node, V(s.Label(node)));
            case SyntaxKind.ReturnStatement:
                return f.UpdateReturnStatement(node, V(s.Expression(node)));
            case SyntaxKind.WithStatement:
                return f.UpdateWithStatement(node, V(s.Expression(node)), VL(s.Statement(node)));
            case SyntaxKind.SwitchStatement:
                return f.UpdateSwitchStatement(node, V(s.Expression(node)), V(s.CaseBlock(node)));
            case SyntaxKind.LabeledStatement:
                return f.UpdateLabeledStatement(node, V(s.Label(node)), VL(s.Statement(node)));
            case SyntaxKind.ThrowStatement:
                return f.UpdateThrowStatement(node, V(s.Expression(node)));
            case SyntaxKind.TryStatement:
                return f.UpdateTryStatement(node, V(s.TryBlock(node)), V(s.CatchClause(node)), V(s.FinallyBlock(node)));
            case SyntaxKind.VariableDeclaration:
                return f.UpdateVariableDeclaration(node, V(s.Name(node)), s.ExclamationToken(node), V(s.Type(node)), V(s.Initializer(node)));
            case SyntaxKind.VariableDeclarationList:
                return f.UpdateVariableDeclarationList(node, L(s.Declarations(node)));
            case SyntaxKind.FunctionDeclaration:
            {
                var modifiers = L(s.Modifiers(node));
                var name = V(s.Name(node));
                var typeParameters = L(s.TypeParameters(node));
                var parameters = VisitParameterList(s.Parameters(node), visitor, context);
                var type = V(s.Type(node));
                return f.UpdateFunctionDeclaration(node, modifiers, s.AsteriskToken(node), name, typeParameters, parameters, type, VisitFunctionBody(s.Body(node), visitor, context));
            }
            case SyntaxKind.ClassDeclaration:
                return f.UpdateClassDeclaration(node, L(s.Modifiers(node)), V(s.Name(node)), L(s.TypeParameters(node)), L(s.HeritageClauses(node)), L(s.Members(node)));
            case SyntaxKind.InterfaceDeclaration:
                return f.UpdateInterfaceDeclaration(node, L(s.Modifiers(node)), V(s.Name(node)), L(s.TypeParameters(node)), L(s.HeritageClauses(node)), L(s.Members(node)));
            case SyntaxKind.TypeAliasDeclaration:
                return f.UpdateTypeAliasDeclaration(node, L(s.Modifiers(node)), V(s.Name(node)), L(s.TypeParameters(node)), V(s.Type(node)));
            case SyntaxKind.EnumDeclaration:
                return f.UpdateEnumDeclaration(node, L(s.Modifiers(node)), V(s.Name(node)), L(s.Members(node)));
            case SyntaxKind.ModuleDeclaration:
                return f.UpdateModuleDeclaration(node, L(s.Modifiers(node)), V(s.Name(node)), V(s.Body(node)));
            case SyntaxKind.ModuleBlock:
                return f.UpdateModuleBlock(node, L(s.Statements(node)));
            case SyntaxKind.CaseBlock:
                return f.UpdateCaseBlock(node, L(s.Clauses(node)));
            case SyntaxKind.NamespaceExportDeclaration:
                return f.UpdateNamespaceExportDeclaration(node, V(s.Name(node)));
            case SyntaxKind.ImportEqualsDeclaration:
                return f.UpdateImportEqualsDeclaration(node, L(s.Modifiers(node)), s.IsTypeOnly(node), V(s.Name(node)), V(s.ModuleReference(node)));
            case SyntaxKind.ImportDeclaration:
                return f.UpdateImportDeclaration(node, L(s.Modifiers(node)), V(s.ImportClause(node)), V(s.ModuleSpecifier(node)), V(s.Attributes(node)));
            case SyntaxKind.ImportAttributes:
                return f.UpdateImportAttributes(node, L(s.Elements(node)), s.MultiLine(node));
            case SyntaxKind.ImportAttribute:
                return f.UpdateImportAttribute(node, V(s.Name(node)), V(s.Value(node)));
            case SyntaxKind.ImportClause:
            {
                var phaseModifier = s.PhaseModifier(node);
                return f.UpdateImportClause(node, phaseModifier == SyntaxKind.Unknown ? null : phaseModifier, V(s.Name(node)), V(s.NamedBindings(node)));
            }
            case SyntaxKind.NamespaceImport:
                return f.UpdateNamespaceImport(node, V(s.Name(node)));
            case SyntaxKind.NamespaceExport:
                return f.UpdateNamespaceExport(node, V(s.Name(node)));
            case SyntaxKind.NamedImports:
                return f.UpdateNamedImports(node, L(s.Elements(node)));
            case SyntaxKind.ImportSpecifier:
                return f.UpdateImportSpecifier(node, s.IsTypeOnly(node), V(s.PropertyName(node)), V(s.Name(node)));
            case SyntaxKind.ExportAssignment:
                return f.UpdateExportAssignment(node, L(s.Modifiers(node)), V(s.Expression(node)));
            case SyntaxKind.ExportDeclaration:
                return f.UpdateExportDeclaration(node, L(s.Modifiers(node)), s.IsTypeOnly(node), V(s.ExportClause(node)), V(s.ModuleSpecifier(node)), V(s.Attributes(node)));
            case SyntaxKind.NamedExports:
                return f.UpdateNamedExports(node, L(s.Elements(node)));
            case SyntaxKind.ExportSpecifier:
                return f.UpdateExportSpecifier(node, s.IsTypeOnly(node), V(s.PropertyName(node)), V(s.Name(node)));

            // Module references
            case SyntaxKind.ExternalModuleReference:
                return f.UpdateExternalModuleReference(node, V(s.Expression(node)));

            // JSX
            case SyntaxKind.JsxElement:
                return f.UpdateJsxElement(node, V(s.OpeningElement(node)), L(s.Children(node)), V(s.ClosingElement(node)));
            case SyntaxKind.JsxSelfClosingElement:
                return f.UpdateJsxSelfClosingElement(node, V(s.TagName(node)), L(s.TypeArguments(node)), V(s.Attributes(node)));
            case SyntaxKind.JsxOpeningElement:
                return f.UpdateJsxOpeningElement(node, V(s.TagName(node)), L(s.TypeArguments(node)), V(s.Attributes(node)));
            case SyntaxKind.JsxClosingElement:
                return f.UpdateJsxClosingElement(node, V(s.TagName(node)));
            case SyntaxKind.JsxNamespacedName:
                return f.UpdateJsxNamespacedName(node, V(s.Namespace(node)), V(s.Name(node)));
            case SyntaxKind.JsxFragment:
                return f.UpdateJsxFragment(node, V(s.OpeningFragment(node)), L(s.Children(node)), V(s.ClosingFragment(node)));
            case SyntaxKind.JsxAttribute:
                return f.UpdateJsxAttribute(node, V(s.Name(node)), V(s.Initializer(node)));
            case SyntaxKind.JsxAttributes:
                return f.UpdateJsxAttributes(node, L(s.Properties(node)));
            case SyntaxKind.JsxSpreadAttribute:
                return f.UpdateJsxSpreadAttribute(node, V(s.Expression(node)));
            case SyntaxKind.JsxExpression:
                return f.UpdateJsxExpression(node, V(s.Expression(node)));

            // Clauses
            case SyntaxKind.CaseClause:
                return f.UpdateCaseClause(node, V(s.Expression(node)), L(s.Statements(node)));
            case SyntaxKind.DefaultClause:
                return f.UpdateDefaultClause(node, L(s.Statements(node)));
            case SyntaxKind.HeritageClause:
                return f.UpdateHeritageClause(node, L(s.Types(node)));
            case SyntaxKind.CatchClause:
                return f.UpdateCatchClause(node, V(s.VariableDeclaration(node)), V(s.Block(node)));

            // Property assignments
            case SyntaxKind.PropertyAssignment:
                return f.UpdatePropertyAssignment(node, V(s.Name(node)), V(s.Initializer(node)));
            case SyntaxKind.ShorthandPropertyAssignment:
                return f.UpdateShorthandPropertyAssignment(node, V(s.Name(node)), V(s.ObjectAssignmentInitializer(node)));
            case SyntaxKind.SpreadAssignment:
                return f.UpdateSpreadAssignment(node, V(s.Expression(node)));

            // Enum
            case SyntaxKind.EnumMember:
                return f.UpdateEnumMember(node, V(s.Name(node)), V(s.Initializer(node)));

            // Top-level nodes
            case SyntaxKind.SourceFile:
                return f.UpdateSourceFile(node, VisitLexicalEnvironment(s.Statements(node), visitor, context));

            // Transformation nodes
            case SyntaxKind.PartiallyEmittedExpression:
                return f.UpdatePartiallyEmittedExpression(node, V(s.Expression(node)));
            case SyntaxKind.CommaListExpression:
                return f.UpdateCommaListExpression(node, L(s.Elements(node)));
        }
        return node;
    }
}
