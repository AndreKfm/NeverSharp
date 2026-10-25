// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (the control flow entry points the expression checker calls:
// getFlowTypeOfReference, getNarrowableTypeForReference)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/flow.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5. TODO(C7): this whole file is a seam for control flow analysis
// (module C7 ports getFlowTypeOfReference and narrowing and replaces it). Until then the flow type of a reference is its
// declared type (auto-typed variables read as any), and IsNarrowingPending tells the expression checker which
// references narrowing might give another type, so that the diagnostics that depend on it are not reported.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    /// <summary>
    /// checker.ts functionHasImplicitReturn. TODO(C7): isReachableFlowNode; the seam uses the binder's end flow node
    /// (set when the end of the body is syntactically reachable).
    /// </summary>
    internal bool FunctionHasImplicitReturn(NodeRef func) => !BindingOf(func.File).EndFlowNodeOf(func.Node).IsNone;

    /// <summary>
    /// checker.ts getFlowTypeOfReference. TODO(C7): the seam returns the declared type (the any type for the auto types
    /// that control flow analysis evolves).
    /// </summary>
    internal Type GetFlowTypeOfReference(NodeRef reference, Type declaredType, Type initialType = default, NodeRef flowContainer = default)
    {
        _ = reference;
        _ = initialType;
        _ = flowContainer;
        return declaredType == autoType ? anyType : declaredType == autoArrayType ? anyArrayType : declaredType;
    }

    /// <summary>checker.ts getNarrowableTypeForReference. TODO(C7): the seam returns the type.</summary>
    internal static Type GetNarrowableTypeForReference(Type type, NodeRef reference, CheckMode checkMode = CheckMode.Normal)
    {
        _ = reference;
        _ = checkMode;
        return type;
    }

    /// <summary>
    /// TODO(C7): whether narrowing could give the reference <paramref name="expr"/> a type other than
    /// <paramref name="type"/>, its declared type: the type is one narrowing changes (unions, unknown, type variables)
    /// and the control flow before the reference contains an assignment to it, a condition, switch or call that mentions
    /// it. The diagnostics that depend on the flow type of such references are not reported until C7 computes it.
    /// </summary>
    internal bool IsNarrowingPending(NodeRef expr, Type type)
    {
        if (expr.IsNone)
        {
            return false;
        }
        var store = StoreOf(expr);
        var n = expr.Node;
        while (store.Kind(n) is SyntaxKind.ParenthesizedExpression or SyntaxKind.NonNullExpression)
        {
            n = store.Expression(n);
        }
        // the operands that give a conditional or logical expression its type
        if (store.Kind(n) == SyntaxKind.ConditionalExpression)
        {
            return IsNarrowingPending(At(expr, store.WhenTrue(n)), type) || IsNarrowingPending(At(expr, store.WhenFalse(n)), type);
        }
        if (store.Kind(n) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(n)) is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.BarBarToken or SyntaxKind.QuestionQuestionToken)
        {
            return IsNarrowingPending(At(expr, store.Left(n)), type) || IsNarrowingPending(At(expr, store.Right(n)), type);
        }
        // object types are narrowed by instanceof, in and type predicates only
        var sensitive = IsNarrowingSensitiveType(type);
        if (!sensitive && (Types.Flags(type) & TypeFlags.Object) == 0)
        {
            return false;
        }
        var root = n;
        while (store.Kind(root) is SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression or SyntaxKind.ParenthesizedExpression or SyntaxKind.NonNullExpression)
        {
            root = store.Expression(root);
        }
        string rootName;
        switch (store.Kind(root))
        {
            case SyntaxKind.Identifier:
                rootName = store.Texts.GetString(store.EscapedText(root))!;
                break;
            case SyntaxKind.ThisKeyword:
                rootName = "this";
                break;
            case SyntaxKind.SuperKeyword:
                rootName = "super";
                break;
            default:
                return false;
        }
        // `this` itself is narrowed by type guards only (`this instanceof C`, `isC(this)`); it is never assigned
        var bareThis = store.Kind(n) == SyntaxKind.ThisKeyword;
        var binding = BindingOf(expr.File);
        var flow = binding.FlowNodeOf(n);
        if (flow.IsNone)
        {
            return false;
        }
        var visited = new HashSet<int>();
        var work = new Stack<FlowNode>();
        work.Push(flow);
        while (work.Count > 0)
        {
            var f = work.Pop();
            if (f.IsNone || !visited.Add(f.Index) || visited.Count > 4000)
            {
                if (visited.Count > 4000)
                {
                    return true;
                }
                continue;
            }
            var flags = binding.Flags(f);
            if ((flags & FlowFlags.Unreachable) != 0)
            {
                continue;
            }
            if ((flags & FlowFlags.Start) != 0)
            {
                var container = binding.NodeOf(f);
                if (!container.IsNone)
                {
                    work.Push(binding.FlowNodeOf(container));
                }
                continue;
            }
            var node = binding.NodeOf(f);
            if (bareThis)
            {
                if ((flags & FlowFlags.Condition) != 0 && !node.IsNone && MentionsBareThis(store, node))
                {
                    return true;
                }
            }
            else if (!sensitive)
            {
                if ((flags & FlowFlags.Condition) != 0 && !node.IsNone && MayNarrowObject(store, node, rootName))
                {
                    return true;
                }
            }
            else if ((flags & FlowFlags.Assignment) != 0)
            {
                if (!node.IsNone && MentionsName(store, node, rootName))
                {
                    return true;
                }
            }
            else if ((flags & (FlowFlags.Condition | FlowFlags.Call)) != 0)
            {
                if (!node.IsNone && MentionsName(store, node, rootName))
                {
                    return true;
                }
            }
            else if ((flags & FlowFlags.SwitchClause) != 0)
            {
                if (!node.IsNone && MentionsName(store, store.Expression(node), rootName))
                {
                    return true;
                }
            }
            if ((flags & FlowFlags.Label) != 0)
            {
                foreach (var antecedent in binding.Antecedents(f))
                {
                    work.Push(antecedent);
                }
            }
            else if ((flags & FlowFlags.ReduceLabel) != 0)
            {
                work.Push(binding.Antecedent(f));
                foreach (var antecedent in binding.ReduceAntecedents(f))
                {
                    work.Push(antecedent);
                }
            }
            else
            {
                work.Push(binding.Antecedent(f));
            }
        }
        return false;
    }

    // the types narrowing can change: unions (including boolean and enums), unknown and type variables
    private bool IsNarrowingSensitiveType(Type type)
    {
        var flags = Types.Flags(type);
        return (flags & (TypeFlags.Union | TypeFlags.Unknown | TypeFlags.Instantiable | TypeFlags.Intersection)) != 0 ||
            ((flags & TypeFlags.Object) != 0 && IsEmptyAnonymousObjectType(type));
    }

    // whether a condition or call may narrow an object type mentioned by the name: instanceof, in, or a call (type
    // predicates and assertion functions) whose operands mention it
    private static bool MayNarrowObject(NodeStore store, Node node, string name)
    {
        var found = false;
        Visit(node);
        return found;

        void Visit(Node n)
        {
            if (found)
            {
                return;
            }
            switch (store.Kind(n))
            {
                case SyntaxKind.BinaryExpression when store.Kind(store.OperatorToken(n)) is SyntaxKind.InstanceOfKeyword or SyntaxKind.InKeyword:
                    if (MentionsName(store, store.Kind(store.OperatorToken(n)) == SyntaxKind.InKeyword ? store.Right(n) : store.Left(n), name))
                    {
                        found = true;
                        return;
                    }
                    break;
                case SyntaxKind.CallExpression:
                    foreach (var argument in store.Nodes(store.Arguments(n)))
                    {
                        if (MentionsName(store, argument, name))
                        {
                            found = true;
                            return;
                        }
                    }
                    break;
                case SyntaxKind.ArrowFunction:
                case SyntaxKind.FunctionExpression:
                    return;
            }
            NodeVisitor.ForEachChild<object>(store, n, child =>
            {
                Visit(child);
                return null;
            });
        }
    }

    // whether a node mentions `this` other than as the object of a property or element access
    private static bool MentionsBareThis(NodeStore store, Node node)
    {
        var found = false;
        Visit(node);
        return found;

        void Visit(Node n)
        {
            if (found)
            {
                return;
            }
            if (store.Kind(n) == SyntaxKind.ThisKeyword)
            {
                var parent = store.Parent(n);
                found = !(store.Kind(parent) is SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression && store.Expression(parent) == n);
                return;
            }
            NodeVisitor.ForEachChild<object>(store, n, child =>
            {
                Visit(child);
                return null;
            });
        }
    }

    // whether a node mentions an identifier with this name (or `this`/`super`) outside of nested functions
    private static bool MentionsName(NodeStore store, Node node, string name)
    {
        if (node.IsNone)
        {
            return false;
        }
        var found = false;
        Visit(node);
        return found;

        void Visit(Node n)
        {
            if (found)
            {
                return;
            }
            switch (store.Kind(n))
            {
                case SyntaxKind.Identifier:
                    if (store.Texts.Equals(store.EscapedText(n), name))
                    {
                        found = true;
                    }
                    return;
                case SyntaxKind.ThisKeyword:
                    found = name == "this";
                    return;
                case SyntaxKind.SuperKeyword:
                    found = name == "super";
                    return;
            }
            NodeVisitor.ForEachChild<object>(store, n, child =>
            {
                Visit(child);
                return null;
            });
        }
    }
}
