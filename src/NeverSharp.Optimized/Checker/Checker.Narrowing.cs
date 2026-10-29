// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (getFlowTypeOfReference with getTypeAtFlowNode and its cases, and narrowing)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/flow.go (FlowState carries getFlowTypeOfReference's closure variables)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C7. Ported: getFlowCacheKey, getTypeAtFlowNode with
// getTypeAtFlowAssignment, getTypeAtFlowCall, getTypeAtFlowArrayMutation, getTypeAtFlowCondition, getTypeAtSwitchClause,
// getTypeAtFlowBranchLabel, getTypeAtFlowLoopLabel (the loop-type cache, the 2000-deep bail-out), and narrowType,
// narrowTypeByTruthiness, narrowTypeByBinaryExpression, narrowTypeByEquality, narrowTypeByTypeof,
// narrowTypeByDiscriminant(Property), narrowTypeBySwitchOn*, narrowTypeByInKeyword, narrowTypeByInstanceof,
// narrowTypeByConstructor, narrowTypeByCallExpression, narrowTypeByTypePredicate, narrowTypeByOptionality,
// getNarrowedType.

using System.Runtime.InteropServices;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    /// <summary>getFlowTypeOfReference's closure variables (typescript-go: FlowState).</summary>
    private sealed class FlowState
    {
        public NodeRef Reference;
        public Type DeclaredType;
        public Type InitialType;
        public NodeRef FlowContainer;
        public string? Key;
        public bool IsKeySet;
        public int FlowDepth;
        public int SharedFlowStart;
    }

    /// <summary>checker.ts getFlowTypeOfReference.</summary>
    internal Type GetFlowTypeOfReference(NodeRef reference, Type declaredType, Type initialType = default, NodeRef flowContainer = default, FlowRef flowNode = default)
    {
        if (initialType.IsNone)
        {
            initialType = declaredType;
        }
        if (flowNode.IsNone)
        {
            var node = FlowNodeOf(reference);
            flowNode = new FlowRef(reference.File, node);
        }
        if (flowAnalysisDisabled)
        {
            return errorType;
        }
        if (flowNode.IsNone)
        {
            return declaredType;
        }
        flowInvocationCount++;
        var f = new FlowState
        {
            Reference = reference,
            DeclaredType = declaredType,
            InitialType = initialType,
            FlowContainer = flowContainer,
            SharedFlowStart = sharedFlowCount,
        };
        var evolvedType = GetTypeFromFlowType(GetTypeAtFlowNode(f, flowNode));
        sharedFlowCount = f.SharedFlowStart;
        // When the reference is 'x' in an 'x.length', 'x.push(value)', 'x.unshift(value)' or x[n] = value' operation,
        // we give type 'any[]' to 'x' instead of using the type determined by control flow analysis such that operations
        // on empty arrays are possible without implicit any errors and new element types can be inferred without
        // type mismatch errors.
        var resultType = (Types.ObjectFlagsOf(evolvedType) & ObjectFlags.EvolvingArray) != 0 && IsEvolvingArrayOperationTarget(reference) ? autoArrayType : FinalizeEvolvingArrayType(evolvedType);
        if (resultType == unreachableNeverType)
        {
            return declaredType;
        }
        if (!IsSyntheticReference(reference) && !IsSyntheticElementAccess(reference))
        {
            var store = StoreOf(reference);
            var parent = store.Parent(reference.Node);
            if (!parent.IsNone && store.Kind(parent) == SyntaxKind.NonNullExpression && (Types.Flags(resultType) & TypeFlags.Never) == 0 &&
                (Types.Flags(GetTypeWithFacts(resultType, TypeFacts.NEUndefinedOrNull)) & TypeFlags.Never) != 0)
            {
                return declaredType;
            }
        }
        return resultType;
    }

    private string? GetOrSetCacheKey(FlowState f)
    {
        if (f.IsKeySet)
        {
            return f.Key;
        }
        f.IsKeySet = true;
        return f.Key = GetFlowCacheKey(f.Reference, f.DeclaredType, f.InitialType, f.FlowContainer);
    }

    /// <summary>
    /// checker.ts getFlowCacheKey: the key of a "dotted name" (the id of the symbol referenced by the leftmost identifier
    /// followed by zero or more property names separated by dots); null if the reference isn't a dotted name.
    /// </summary>
    private string? GetFlowCacheKey(NodeRef node, Type declaredType, Type initialType, NodeRef flowContainer)
    {
        var container = flowContainer.IsNone ? "-1" : NodeIdForCache(flowContainer);
        if (IsSyntheticReference(node))
        {
            // `this.<name>`: the key of `this` and the name
            return syntheticThisAccessName is null ? null : "0|" + container + "|" + declaredType.Id + "|" + initialType.Id + "." + syntheticThisAccessName;
        }
        if (IsSyntheticElementAccess(node))
        {
            var access = SyntheticElementAccessOf(node);
            var key = GetFlowCacheKey(access.Expression, declaredType, initialType, flowContainer);
            return key is null ? null : key + "." + access.Name;
        }
        var store = StoreOf(node);
        var n = node.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.Identifier:
                if (!IsThisInTypeQuery(store, n))
                {
                    var symbol = GetResolvedSymbol(node);
                    return symbol != unknownSymbol ? container + "|" + declaredType.Id + "|" + initialType.Id + "|" + SymbolIdForCache(symbol) : null;
                }
                goto case SyntaxKind.ThisKeyword;
            case SyntaxKind.ThisKeyword:
                return "0|" + container + "|" + declaredType.Id + "|" + initialType.Id;
            case SyntaxKind.NonNullExpression:
            case SyntaxKind.ParenthesizedExpression:
                return GetFlowCacheKey(At(node, store.Expression(n)), declaredType, initialType, flowContainer);
            case SyntaxKind.QualifiedName:
            {
                var left = GetFlowCacheKey(At(node, store.Left(n)), declaredType, initialType, flowContainer);
                return left is null ? null : left + "." + store.Texts.GetString(store.EscapedText(store.Right(n)));
            }
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            {
                var propName = GetAccessedPropertyName(node);
                if (propName is not null)
                {
                    var key = GetFlowCacheKey(At(node, store.Expression(n)), declaredType, initialType, flowContainer);
                    return key is null ? null : key + "." + propName;
                }
                if (store.Kind(n) == SyntaxKind.ElementAccessExpression && store.Kind(store.ArgumentExpression(n)) == SyntaxKind.Identifier)
                {
                    var symbol = GetResolvedSymbol(At(node, store.ArgumentExpression(n)));
                    if (IsConstantVariable(symbol) || (IsParameterOrMutableLocalVariable(symbol) && !IsSymbolAssigned(symbol)))
                    {
                        var key = GetFlowCacheKey(At(node, store.Expression(n)), declaredType, initialType, flowContainer);
                        return key is null ? null : key + ".@" + SymbolIdForCache(symbol);
                    }
                }
                break;
            }
            case SyntaxKind.ObjectBindingPattern:
            case SyntaxKind.ArrayBindingPattern:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.MethodDeclaration:
                // Handle pseudo-references originating in getNarrowedTypeOfSymbol.
                return NodeIdForCache(node) + "#" + declaredType.Id;
        }
        return null;
    }

    // Keys only need to be unique per checker: a node or symbol reference spelled out instead of getNodeId/getSymbolId.
    private static string NodeIdForCache(NodeRef node) => node.File + ":" + node.Node.Index;

    private static string SymbolIdForCache(SymbolRef symbol) => symbol.File + ":" + symbol.Index;

    private FlowType GetTypeAtFlowNode(FlowState f, FlowRef flow)
    {
        if (f.FlowDepth == 2000)
        {
            // We have made 2000 recursive invocations. To avoid overflowing the call stack we report an error
            // and disable further control flow analysis in the containing function or module body.
            flowAnalysisDisabled = true;
            // (a synthetic element access has its parent's text range)
            ReportFlowControlError(IsSyntheticElementAccess(f.Reference) ? SyntheticElementAccessOf(f.Reference).Parent : f.Reference);
            return new FlowType(errorType, false);
        }
        f.FlowDepth++;
        var sharedFlow = default(FlowRef);
        while (true)
        {
            var flags = FlowFlagsOf(flow);
            if ((flags & FlowFlags.Shared) != 0)
            {
                // We cache results of flow type resolution for shared nodes that were previously visited in
                // the same getFlowTypeOfReference invocation. A node is considered shared when it is the
                // antecedent of more than one node.
                for (var i = f.SharedFlowStart; i < sharedFlowCount; i++)
                {
                    if (sharedFlowNodes[i] == flow)
                    {
                        f.FlowDepth--;
                        return sharedFlowTypes[i];
                    }
                }
                sharedFlow = flow;
            }
            FlowType type;
            if ((flags & FlowFlags.Assignment) != 0)
            {
                var t = GetTypeAtFlowAssignment(f, flow);
                if (t is null)
                {
                    flow = FlowAntecedent(flow);
                    continue;
                }
                type = t.Value;
            }
            else if ((flags & FlowFlags.Call) != 0)
            {
                var t = GetTypeAtFlowCall(f, flow);
                if (t is null)
                {
                    flow = FlowAntecedent(flow);
                    continue;
                }
                type = t.Value;
            }
            else if ((flags & FlowFlags.Condition) != 0)
            {
                type = GetTypeAtFlowCondition(f, flow);
            }
            else if ((flags & FlowFlags.SwitchClause) != 0)
            {
                type = GetTypeAtSwitchClause(f, flow);
            }
            else if ((flags & FlowFlags.Label) != 0)
            {
                var antecedents = LabelAntecedentList(flow);
                if (antecedents.Count == 1)
                {
                    flow = antecedents[0];
                    continue;
                }
                type = (flags & FlowFlags.BranchLabel) != 0 ? GetTypeAtFlowBranchLabel(f, flow, antecedents) : GetTypeAtFlowLoopLabel(f, flow, antecedents);
            }
            else if ((flags & FlowFlags.ArrayMutation) != 0)
            {
                var t = GetTypeAtFlowArrayMutation(f, flow);
                if (t is null)
                {
                    flow = FlowAntecedent(flow);
                    continue;
                }
                type = t.Value;
            }
            else if ((flags & FlowFlags.ReduceLabel) != 0)
            {
                var reduceLabel = flow;
                type = WithReducedLabel(reduceLabel, () => GetTypeAtFlowNode(f, FlowAntecedent(reduceLabel)));
            }
            else if ((flags & FlowFlags.Start) != 0)
            {
                // Check if we should continue with the control flow of the containing function.
                var container = FlowNodeNode(flow);
                var referenceKind = IsSyntheticReference(f.Reference) ? SyntaxKind.PropertyAccessExpression
                    : IsSyntheticElementAccess(f.Reference) ? SyntaxKind.ElementAccessExpression : Kind(f.Reference);
                if (!container.IsNone && container != f.FlowContainer &&
                    referenceKind != SyntaxKind.PropertyAccessExpression &&
                    referenceKind != SyntaxKind.ElementAccessExpression &&
                    !(referenceKind == SyntaxKind.ThisKeyword && Kind(container) != SyntaxKind.ArrowFunction))
                {
                    flow = new FlowRef(container.File, FlowNodeOf(container));
                    continue;
                }
                // At the top of the flow we have the initial type.
                type = new FlowType(f.InitialType, false);
            }
            else
            {
                // Unreachable code errors are reported in the binding phase. Here we
                // simply return the non-auto declared type to reduce follow-on errors.
                type = new FlowType(ConvertAutoToAny(f.DeclaredType), false);
            }
            if (!sharedFlow.IsNone)
            {
                // Record visited node and the associated type in the cache.
                SetAt(sharedFlowNodes, sharedFlowCount, sharedFlow);
                SetAt(sharedFlowTypes, sharedFlowCount, type);
                sharedFlowCount++;
            }
            f.FlowDepth--;
            return type;
        }
    }

    private static void SetAt<T>(List<T> list, int index, T value)
    {
        while (list.Count <= index)
        {
            list.Add(default!);
        }
        list[index] = value;
    }

    private Type GetInitialOrAssignedType(FlowState f, NodeRef node) =>
        GetNarrowableTypeForReference(Kind(node) is SyntaxKind.VariableDeclaration or SyntaxKind.BindingElement ? GetInitialType(node) : GetAssignedType(node), f.Reference);

    private FlowType? GetTypeAtFlowAssignment(FlowState f, FlowRef flow)
    {
        var node = FlowNodeNode(flow);
        var reference = f.Reference;
        var declaredType = f.DeclaredType;
        // Assignments only narrow the computed type if the declared type is a union type. Thus, we
        // only need to evaluate the assigned type if the declared type is a union type.
        if (IsMatchingReference(reference, node))
        {
            if (!IsReachableFlowNode(flow))
            {
                return new FlowType(unreachableNeverType, false);
            }
            var store = StoreOf(node);
            if (GetAssignmentTargetKind(store, node.Node) == AssignmentKind.Compound)
            {
                var flowType = GetTypeAtFlowNode(f, FlowAntecedent(flow));
                return CreateFlowType(GetBaseTypeOfLiteralType(GetTypeFromFlowType(flowType)), IsIncomplete(flowType));
            }
            if (declaredType == autoType || declaredType == autoArrayType)
            {
                if (IsEmptyArrayAssignment(node))
                {
                    return new FlowType(GetEvolvingArrayType(neverType), false);
                }
                var assignedType = GetWidenedLiteralType(GetInitialOrAssignedType(f, node));
                return new FlowType(IsTypeAssignableTo(assignedType, declaredType) ? assignedType : anyArrayType, false);
            }
            var t = IsInCompoundLikeAssignment(store, node.Node) ? GetBaseTypeOfLiteralType(declaredType) : declaredType;
            if ((Types.Flags(t) & TypeFlags.Union) != 0)
            {
                return new FlowType(GetAssignmentReducedType(t, GetInitialOrAssignedType(f, node)), false);
            }
            return new FlowType(t, false);
        }
        // We didn't have a direct match. However, if the reference is a dotted name, this
        // may be an assignment to a left hand part of the reference. For example, for a
        // reference 'x.y.z', we may be at an assignment to 'x.y' or 'x'. In that case,
        // return the declared type.
        if (ContainsMatchingReference(reference, node))
        {
            if (!IsReachableFlowNode(flow))
            {
                return new FlowType(unreachableNeverType, false);
            }
            // A matching dotted name might also be an expando property on a function *expression*,
            // in which case we continue control flow analysis back to the function's declaration
            var store = StoreOf(node);
            if (store.Kind(node.Node) == SyntaxKind.VariableDeclaration && (Utilities.IsInJSFile(store, node.Node) || IsVarConstLike(store, node.Node)))
            {
                var init = GetDeclaredExpandoInitializer(node);
                if (!init.IsNone && Kind(init) is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction)
                {
                    return GetTypeAtFlowNode(f, FlowAntecedent(flow));
                }
            }
            return new FlowType(declaredType, false);
        }
        // for (const _ in ref) acts as a nonnull on ref
        var nodeStore = StoreOf(node);
        if (nodeStore.Kind(node.Node) == SyntaxKind.VariableDeclaration && nodeStore.Kind(nodeStore.Parent(nodeStore.Parent(node.Node))) == SyntaxKind.ForInStatement)
        {
            var expression = At(node, nodeStore.Expression(nodeStore.Parent(nodeStore.Parent(node.Node))));
            if (IsMatchingReference(reference, expression) || OptionalChainContainsReference(expression, reference))
            {
                return new FlowType(GetNonNullableTypeIfNeeded(FinalizeEvolvingArrayType(GetTypeFromFlowType(GetTypeAtFlowNode(f, FlowAntecedent(flow))))), false);
            }
        }
        // Assignment doesn't affect reference
        return null;
    }

    // utilities.ts getDeclaredExpandoInitializer
    private NodeRef GetDeclaredExpandoInitializer(NodeRef node)
    {
        var store = StoreOf(node);
        var initializer = GetEffectiveInitializer(store, node.Node);
        if (initializer.IsNone)
        {
            return NodeRef.None;
        }
        var expando = Utilities.GetExpandoInitializer(store, initializer, Utilities.IsPrototypeAccess(store, store.Name(node.Node)));
        return expando.IsNone ? NodeRef.None : At(node, expando);
    }

    // utilities.ts getEffectiveInitializer
    internal static Node GetEffectiveInitializer(NodeStore store, Node node)
    {
        var initializer = store.Initializer(node);
        if (Utilities.IsInJSFile(store, node) && !initializer.IsNone && store.Kind(initializer) == SyntaxKind.BinaryExpression &&
            store.Kind(store.OperatorToken(initializer)) is SyntaxKind.BarBarToken or SyntaxKind.QuestionQuestionToken)
        {
            var name = store.Name(node);
            if (!name.IsNone && Utilities.IsEntityNameExpression(store, name) && Utilities.IsSameEntityName(store, name, store.Left(initializer)))
            {
                return store.Right(initializer);
            }
        }
        return initializer;
    }

    private Type NarrowTypeByAssertion(FlowState f, Type type, NodeRef expr)
    {
        var store = StoreOf(expr);
        var node = At(expr, Utilities.SkipParentheses(store, expr.Node, true));
        if (store.Kind(node.Node) == SyntaxKind.FalseKeyword)
        {
            return unreachableNeverType;
        }
        if (store.Kind(node.Node) == SyntaxKind.BinaryExpression)
        {
            var op = store.Kind(store.OperatorToken(node.Node));
            if (op == SyntaxKind.AmpersandAmpersandToken)
            {
                return NarrowTypeByAssertion(f, NarrowTypeByAssertion(f, type, At(node, store.Left(node.Node))), At(node, store.Right(node.Node)));
            }
            if (op == SyntaxKind.BarBarToken)
            {
                return GetUnionType([NarrowTypeByAssertion(f, type, At(node, store.Left(node.Node))), NarrowTypeByAssertion(f, type, At(node, store.Right(node.Node)))]);
            }
        }
        return NarrowType(f, type, node, assumeTrue: true);
    }

    private FlowType? GetTypeAtFlowCall(FlowState f, FlowRef flow)
    {
        var call = FlowNodeNode(flow);
        var signature = GetEffectsSignature(call);
        if (!signature.IsNone)
        {
            var predicate = GetTypePredicateOfSignature(signature);
            if (predicate is not null && predicate.Kind is TypePredicateKind.AssertsThis or TypePredicateKind.AssertsIdentifier)
            {
                var flowType = GetTypeAtFlowNode(f, FlowAntecedent(flow));
                var type = FinalizeEvolvingArrayType(GetTypeFromFlowType(flowType));
                var store = StoreOf(call);
                var arguments = store.Arguments(call.Node);
                var narrowedType = !predicate.Type.IsNone ? NarrowTypeByTypePredicate(f, type, predicate, call, assumeTrue: true)
                    : predicate.Kind == TypePredicateKind.AssertsIdentifier && predicate.ParameterIndex >= 0 && predicate.ParameterIndex < store.Length(arguments)
                    ? NarrowTypeByAssertion(f, type, At(call, store.Element(arguments, predicate.ParameterIndex)))
                    : type;
                return narrowedType == type ? flowType : CreateFlowType(narrowedType, IsIncomplete(flowType));
            }
            if ((Types.Flags(GetReturnTypeOfSignature(signature)) & TypeFlags.Never) != 0)
            {
                return new FlowType(unreachableNeverType, false);
            }
        }
        return null;
    }

    private FlowType? GetTypeAtFlowArrayMutation(FlowState f, FlowRef flow)
    {
        if (f.DeclaredType == autoType || f.DeclaredType == autoArrayType)
        {
            var node = FlowNodeNode(flow);
            var store = StoreOf(node);
            var expr = store.Kind(node.Node) == SyntaxKind.CallExpression
                ? store.Expression(store.Expression(node.Node))
                : store.Expression(store.Left(node.Node));
            if (IsMatchingReference(f.Reference, At(node, GetReferenceCandidate(store, expr))))
            {
                var flowType = GetTypeAtFlowNode(f, FlowAntecedent(flow));
                var type = GetTypeFromFlowType(flowType);
                if ((Types.ObjectFlagsOf(type) & ObjectFlags.EvolvingArray) != 0)
                {
                    var evolvedType = type;
                    if (store.Kind(node.Node) == SyntaxKind.CallExpression)
                    {
                        foreach (var arg in store.Nodes(store.Arguments(node.Node)))
                        {
                            evolvedType = AddEvolvingArrayElementType(evolvedType, At(node, arg));
                        }
                    }
                    else
                    {
                        // We must get the context free expression type so as to not recur in an uncached fashion on the LHS (which causes exponential blowup in compile time)
                        var indexType = GetTypeOfExpression(At(node, store.ArgumentExpression(store.Left(node.Node))));
                        if (IsTypeAssignableToKind(indexType, TypeFlags.NumberLike))
                        {
                            evolvedType = AddEvolvingArrayElementType(evolvedType, At(node, store.Right(node.Node)));
                        }
                    }
                    return evolvedType == type ? flowType : CreateFlowType(evolvedType, IsIncomplete(flowType));
                }
                return flowType;
            }
        }
        return null;
    }

    private FlowType GetTypeAtFlowCondition(FlowState f, FlowRef flow)
    {
        var flowType = GetTypeAtFlowNode(f, FlowAntecedent(flow));
        var type = GetTypeFromFlowType(flowType);
        if ((Types.Flags(type) & TypeFlags.Never) != 0)
        {
            return flowType;
        }
        // If we have an antecedent type (meaning we're reachable in some way), we first
        // attempt to narrow the antecedent type. If that produces the never type, and if
        // the antecedent type is incomplete (i.e. a transient type in a loop), then we
        // take the type guard as an indication that control *could* reach here once we
        // have the complete type. We proceed by switching to the silent never type which
        // doesn't report errors when operators are applied to it. Note that this is the
        // *only* place a silent never type is ever generated.
        var assumeTrue = (FlowFlagsOf(flow) & FlowFlags.TrueCondition) != 0;
        var nonEvolvingType = FinalizeEvolvingArrayType(type);
        var narrowedType = NarrowType(f, nonEvolvingType, FlowNodeNode(flow), assumeTrue);
        if (narrowedType == nonEvolvingType)
        {
            return flowType;
        }
        return CreateFlowType(narrowedType, IsIncomplete(flowType));
    }

    private readonly record struct SwitchClauseData(NodeRef SwitchStatement, int ClauseStart, int ClauseEnd);

    private SwitchClauseData SwitchDataOf(FlowRef flow)
    {
        var binding = BindingOf(flow.File);
        return new SwitchClauseData(FlowNodeNode(flow), binding.ClauseStart(flow.Node), binding.ClauseEnd(flow.Node));
    }

    private FlowType GetTypeAtSwitchClause(FlowState f, FlowRef flow)
    {
        var data = SwitchDataOf(flow);
        var store = StoreOf(data.SwitchStatement);
        var expr = At(data.SwitchStatement, Utilities.SkipParentheses(store, store.Expression(data.SwitchStatement.Node)));
        var flowType = GetTypeAtFlowNode(f, FlowAntecedent(flow));
        var type = GetTypeFromFlowType(flowType);
        var reference = f.Reference;
        if (IsMatchingReference(reference, expr))
        {
            type = NarrowTypeBySwitchOnDiscriminant(type, data);
        }
        else if (store.Kind(expr.Node) == SyntaxKind.TypeOfExpression && IsMatchingReference(reference, At(expr, store.Expression(expr.Node))))
        {
            type = NarrowTypeBySwitchOnTypeOf(type, data);
        }
        else if (store.Kind(expr.Node) == SyntaxKind.TrueKeyword)
        {
            type = NarrowTypeBySwitchOnTrue(f, type, data);
        }
        else
        {
            if (strictNullChecks)
            {
                if (OptionalChainContainsReference(expr, reference))
                {
                    type = NarrowTypeBySwitchOptionalChainContainment(type, data, t => (Types.Flags(t) & (TypeFlags.Undefined | TypeFlags.Never)) == 0);
                }
                else if (store.Kind(expr.Node) == SyntaxKind.TypeOfExpression && OptionalChainContainsReference(At(expr, store.Expression(expr.Node)), reference))
                {
                    type = NarrowTypeBySwitchOptionalChainContainment(type, data, t =>
                        !((Types.Flags(t) & TypeFlags.Never) != 0 || ((Types.Flags(t) & TypeFlags.StringLiteral) != 0 && (string)Types.Literal(t).Value! == "undefined")));
                }
            }
            var access = GetDiscriminantPropertyAccess(f, expr, type);
            if (!access.IsNone)
            {
                type = NarrowTypeBySwitchOnDiscriminantProperty(f, type, access, data);
            }
        }
        return CreateFlowType(type, IsIncomplete(flowType));
    }

    private FlowType GetTypeAtFlowBranchLabel(FlowState f, FlowRef flow, List<FlowRef> antecedents)
    {
        var antecedentTypes = new List<Type>();
        var subtypeReduction = false;
        var seenIncomplete = false;
        var bypassFlow = default(FlowRef);
        var binding = BindingOf(flow.File);
        foreach (var antecedent in antecedents)
        {
            if (bypassFlow.IsNone && (FlowFlagsOf(antecedent) & FlowFlags.SwitchClause) != 0 && binding.ClauseStart(antecedent.Node) == binding.ClauseEnd(antecedent.Node))
            {
                // The antecedent is the bypass branch of a potentially exhaustive switch statement.
                bypassFlow = antecedent;
                continue;
            }
            var flowType = GetTypeAtFlowNode(f, antecedent);
            var type = GetTypeFromFlowType(flowType);
            // If the type at a particular antecedent path is the declared type and the
            // reference is known to always be assigned (i.e. when declared and initial types
            // are the same), there is no reason to process more antecedents since the only
            // possible outcome is subtypes that will be removed in the final union type anyway.
            if (type == f.DeclaredType && f.DeclaredType == f.InitialType)
            {
                return new FlowType(type, false);
            }
            if (!antecedentTypes.Contains(type))
            {
                antecedentTypes.Add(type);
            }
            // If an antecedent type is not a subset of the declared type, we need to perform
            // subtype reduction. This happens when a "foreign" type is injected into the control
            // flow using the instanceof operator or a user defined type predicate.
            if (!IsTypeSubsetOf(type, f.InitialType))
            {
                subtypeReduction = true;
            }
            if (IsIncomplete(flowType))
            {
                seenIncomplete = true;
            }
        }
        if (!bypassFlow.IsNone)
        {
            var flowType = GetTypeAtFlowNode(f, bypassFlow);
            var type = GetTypeFromFlowType(flowType);
            // If the bypass flow contributes a type we haven't seen yet and the switch statement
            // isn't exhaustive, process the bypass flow type. Since exhaustiveness checks increase
            // the risk of circularities, we only want to perform them when they make a difference.
            if ((Types.Flags(type) & TypeFlags.Never) == 0 && !antecedentTypes.Contains(type) && !IsExhaustiveSwitchStatement(FlowNodeNode(bypassFlow)))
            {
                if (type == f.DeclaredType && f.DeclaredType == f.InitialType)
                {
                    return new FlowType(type, false);
                }
                antecedentTypes.Add(type);
                if (!IsTypeSubsetOf(type, f.InitialType))
                {
                    subtypeReduction = true;
                }
                if (IsIncomplete(flowType))
                {
                    seenIncomplete = true;
                }
            }
        }
        return CreateFlowType(GetUnionOrEvolvingArrayType(f, antecedentTypes, subtypeReduction ? UnionReduction.Subtype : UnionReduction.Literal), seenIncomplete);
    }

    private FlowType GetTypeAtFlowLoopLabel(FlowState f, FlowRef flow, List<FlowRef> antecedents)
    {
        // If we have previously computed the control flow type for the reference at
        // this flow loop junction, return the cached type.
        if (!flowLoopCaches.TryGetValue(flow, out var cache))
        {
            cache = new Dictionary<string, Type>(StringComparer.Ordinal);
            flowLoopCaches[flow] = cache;
        }
        var key = GetOrSetCacheKey(f);
        if (key is null)
        {
            // No cache key is generated when binding patterns are in unnarrowable situations
            return new FlowType(f.DeclaredType, false);
        }
        if (cache.TryGetValue(key, out var cached))
        {
            return new FlowType(cached, false);
        }
        // If this flow loop junction and reference are already being processed, return
        // the union of the types computed for each branch so far, marked as incomplete.
        // It is possible to see an empty array in cases where loops are nested and the
        // back edge of the outer loop reaches an inner loop that is already being analyzed.
        // In such cases we restart the analysis of the inner loop, which will then see
        // a non-empty in-process array for the outer loop and eventually terminate because
        // the first antecedent of a loop junction is always the non-looping control flow
        // path that leads to the top.
        for (var i = flowLoopStart; i < flowLoopCount; i++)
        {
            if (flowLoopNodes[i] == flow && flowLoopKeys[i] == key && flowLoopTypes[i].Count > 0)
            {
                return CreateFlowType(GetUnionOrEvolvingArrayType(f, flowLoopTypes[i], UnionReduction.Literal), incomplete: true);
            }
        }
        // Add the flow loop junction and reference to the in-process stack and analyze
        // each antecedent code path.
        var antecedentTypes = new List<Type>();
        var subtypeReduction = false;
        FlowType? firstAntecedentType = null;
        foreach (var antecedent in antecedents)
        {
            FlowType flowType;
            if (firstAntecedentType is null)
            {
                // The first antecedent of a loop junction is always the non-looping control
                // flow path that leads to the top.
                flowType = GetTypeAtFlowNode(f, antecedent);
                firstAntecedentType = flowType;
            }
            else
            {
                // All but the first antecedent are the looping control flow paths that lead
                // back to the loop junction. We track these on the flow loop stack.
                SetAt(flowLoopNodes, flowLoopCount, flow);
                SetAt(flowLoopKeys, flowLoopCount, key);
                SetAt(flowLoopTypes, flowLoopCount, antecedentTypes);
                flowLoopCount++;
                var saveFlowTypeCache = flowTypeCache;
                flowTypeCache = null;
                flowType = GetTypeAtFlowNode(f, antecedent);
                flowTypeCache = saveFlowTypeCache;
                flowLoopCount--;
                // If we see a value appear in the cache it is a sign that control flow analysis
                // was restarted and completed by checkExpressionCached. We can simply pick up
                // the resulting type and bail out.
                if (cache.TryGetValue(key, out var restarted))
                {
                    return new FlowType(restarted, false);
                }
            }
            var type = GetTypeFromFlowType(flowType);
            if (!antecedentTypes.Contains(type))
            {
                antecedentTypes.Add(type);
            }
            // If an antecedent type is not a subset of the declared type, we need to perform
            // subtype reduction. This happens when a "foreign" type is injected into the control
            // flow using the instanceof operator or a user defined type predicate.
            if (!IsTypeSubsetOf(type, f.InitialType))
            {
                subtypeReduction = true;
            }
            // If the type at a particular antecedent path is the declared type there is no
            // reason to process more antecedents since the only possible outcome is subtypes
            // that will be removed in the final union type anyway.
            if (type == f.DeclaredType)
            {
                break;
            }
        }
        // The result is incomplete if the first antecedent (the non-looping control flow path)
        // is incomplete.
        var result = GetUnionOrEvolvingArrayType(f, antecedentTypes, subtypeReduction ? UnionReduction.Subtype : UnionReduction.Literal);
        if (IsIncomplete(firstAntecedentType!.Value))
        {
            return CreateFlowType(result, incomplete: true);
        }
        cache[key] = result;
        return new FlowType(result, false);
    }

    // At flow control branch or loop junctions, if the type along every antecedent code path
    // is an evolving array type, we construct a combined evolving array type. Otherwise we
    // finalize all evolving array types.
    private Type GetUnionOrEvolvingArrayType(FlowState f, List<Type> types, UnionReduction subtypeReduction)
    {
        if (IsEvolvingArrayTypeList(types))
        {
            return GetEvolvingArrayType(GetUnionType(types.ConvertAll(GetElementTypeOfEvolvingArrayType).ToArray()));
        }
        var result = RecombineUnknownType(GetUnionType(types.ConvertAll(FinalizeEvolvingArrayType).ToArray(), subtypeReduction));
        var declaredType = f.DeclaredType;
        if (result != declaredType && (Types.Flags(result) & Types.Flags(declaredType) & TypeFlags.Union) != 0 &&
            Types.Elements(Types.Union(result).Types).SequenceEqual(Types.Elements(Types.Union(declaredType).Types)))
        {
            return declaredType;
        }
        return result;
    }

    private NodeRef GetCandidateDiscriminantPropertyAccess(FlowState f, NodeRef expr)
    {
        var reference = f.Reference;
        var store = StoreOf(expr);
        var isPseudoReference = false;
        if (!IsSyntheticReference(reference) && !IsSyntheticElementAccess(reference))
        {
            var refStore = StoreOf(reference);
            var refKind = refStore.Kind(reference.Node);
            isPseudoReference = Utilities.IsBindingPattern(refStore, reference.Node) || refKind is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction || Utilities.IsObjectLiteralMethod(refStore, reference.Node);
        }
        if (isPseudoReference)
        {
            // When the reference is a binding pattern or function or arrow expression, we are narrowing a pesudo-reference in
            // getNarrowedTypeOfSymbol. An identifier for a destructuring variable declared in the same binding pattern or
            // parameter declared in the same parameter list is a candidate.
            if (store.Kind(expr.Node) == SyntaxKind.Identifier)
            {
                var symbol = GetResolvedSymbol(expr);
                var declaration = ValueDeclarationOf(GetExportSymbolOfValueSymbolIfExported(symbol));
                if (!declaration.IsNone && Kind(declaration) is SyntaxKind.BindingElement or SyntaxKind.Parameter &&
                    reference == Parent(declaration) && StoreOf(declaration).Initializer(declaration.Node).IsNone && StoreOf(declaration).DotDotDotToken(declaration.Node).IsNone)
                {
                    return declaration;
                }
            }
        }
        else if (Utilities.IsAccessExpression(store, expr.Node))
        {
            // An access expression is a candidate if the reference matches the left hand expression.
            if (IsMatchingReference(reference, At(expr, store.Expression(expr.Node))))
            {
                return expr;
            }
        }
        else if (store.Kind(expr.Node) == SyntaxKind.Identifier)
        {
            var symbol = GetResolvedSymbol(expr);
            if (IsConstantVariable(symbol))
            {
                var declaration = ValueDeclarationOf(symbol);
                var initializer = GetCandidateVariableDeclarationInitializer(declaration);
                // Given 'const x = obj.kind', allow 'x' as an alias for 'obj.kind'
                if (!initializer.IsNone && Utilities.IsAccessExpression(StoreOf(initializer), initializer.Node) &&
                    IsMatchingReference(reference, At(initializer, StoreOf(initializer).Expression(initializer.Node))))
                {
                    return initializer;
                }
                // Given 'const { kind: x } = obj', allow 'x' as an alias for 'obj.kind'
                if (Kind(declaration) == SyntaxKind.BindingElement && StoreOf(declaration).Initializer(declaration.Node).IsNone)
                {
                    initializer = GetCandidateVariableDeclarationInitializer(Parent(Parent(declaration)));
                    if (!initializer.IsNone && (Kind(initializer) == SyntaxKind.Identifier || Utilities.IsAccessExpression(StoreOf(initializer), initializer.Node)) &&
                        IsMatchingReference(reference, initializer))
                    {
                        return declaration;
                    }
                }
            }
        }
        return NodeRef.None;
    }

    private NodeRef GetCandidateVariableDeclarationInitializer(NodeRef node)
    {
        if (node.IsNone)
        {
            return NodeRef.None;
        }
        var store = StoreOf(node);
        return store.Kind(node.Node) == SyntaxKind.VariableDeclaration && store.Type(node.Node).IsNone && !store.Initializer(node.Node).IsNone
            ? At(node, Utilities.SkipParentheses(store, store.Initializer(node.Node)))
            : NodeRef.None;
    }

    private NodeRef GetDiscriminantPropertyAccess(FlowState f, NodeRef expr, Type computedType)
    {
        // As long as the computed type is a subset of the declared type, we use the full declared type to detect
        // a discriminant property. In cases where the computed type isn't a subset, e.g because of a preceding type
        // predicate narrowing, we use the actual computed type.
        var declaredType = f.DeclaredType;
        if ((Types.Flags(declaredType) & TypeFlags.Union) != 0 || (Types.Flags(computedType) & TypeFlags.Union) != 0)
        {
            var access = GetCandidateDiscriminantPropertyAccess(f, expr);
            if (!access.IsNone)
            {
                var name = GetAccessedPropertyName(access);
                if (!string.IsNullOrEmpty(name))
                {
                    var type = (Types.Flags(declaredType) & TypeFlags.Union) != 0 && IsTypeSubsetOf(computedType, declaredType) ? declaredType : computedType;
                    if (IsDiscriminantProperty(type, name))
                    {
                        return access;
                    }
                }
            }
        }
        return NodeRef.None;
    }

    private Type NarrowTypeByDiscriminant(Type type, NodeRef access, Func<Type, Type> narrowType)
    {
        var propName = GetAccessedPropertyName(access);
        if (propName is null)
        {
            return type;
        }
        var store = StoreOf(access);
        var optionalChain = UtilitiesPublic.IsOptionalChain(store, access.Node);
        var removeNullable = strictNullChecks && (optionalChain || IsNonNullAccess(store, access.Node)) && MaybeTypeOfKind(type, TypeFlags.Nullable);
        var propType = GetTypeOfPropertyOfType(removeNullable ? GetTypeWithFacts(type, TypeFacts.NEUndefinedOrNull) : type, propName);
        if (propType.IsNone)
        {
            return type;
        }
        propType = removeNullable && optionalChain ? GetOptionalType(propType) : propType;
        var narrowedPropType = narrowType(propType);
        return FilterType(type, t =>
        {
            var discriminantType = GetTypeOfPropertyOrIndexSignatureOfType(t, propName);
            if (discriminantType.IsNone)
            {
                discriminantType = unknownType;
            }
            return (Types.Flags(discriminantType) & TypeFlags.Never) == 0 && (Types.Flags(narrowedPropType) & TypeFlags.Never) == 0 && AreTypesComparable(narrowedPropType, discriminantType);
        });
    }

    // isAccessExpression(reference) && isMatchingReference(reference.expression, target)
    private bool IsAccessReferenceOn(NodeRef reference, NodeRef target)
    {
        if (IsSyntheticReference(reference))
        {
            return IsMatchingThisTarget(target);
        }
        if (IsSyntheticElementAccess(reference))
        {
            return IsMatchingReference(SyntheticElementAccessOf(reference).Expression, target);
        }
        var refStore = StoreOf(reference);
        return Utilities.IsAccessExpression(refStore, reference.Node) && IsMatchingReference(At(reference, refStore.Expression(reference.Node)), target);
    }

    // utilities.ts isNonNullAccess
    private static bool IsNonNullAccess(NodeStore store, Node node) =>
        Utilities.IsAccessExpression(store, node) && store.Kind(store.Expression(node)) == SyntaxKind.NonNullExpression;

    private Type NarrowTypeByDiscriminantProperty(FlowState f, Type type, NodeRef access, SyntaxKind op, NodeRef value, bool assumeTrue)
    {
        if (op is SyntaxKind.EqualsEqualsEqualsToken or SyntaxKind.ExclamationEqualsEqualsToken && (Types.Flags(type) & TypeFlags.Union) != 0)
        {
            var keyPropertyName = GetKeyPropertyName(type);
            if (keyPropertyName is not null && keyPropertyName == GetAccessedPropertyName(access))
            {
                var candidate = GetConstituentTypeForKeyType(type, GetTypeOfExpression(value));
                if (!candidate.IsNone)
                {
                    if (op == (assumeTrue ? SyntaxKind.EqualsEqualsEqualsToken : SyntaxKind.ExclamationEqualsEqualsToken))
                    {
                        return candidate;
                    }
                    var keyType = GetTypeOfPropertyOfType(candidate, keyPropertyName);
                    return IsUnitType(keyType.IsNone ? unknownType : keyType) ? FilterType(type, t => t != candidate) : type;
                }
            }
        }
        return NarrowTypeByDiscriminant(type, access, t => NarrowTypeByEquality(t, op, value, assumeTrue));
    }

    private Type NarrowTypeBySwitchOnDiscriminantProperty(FlowState f, Type type, NodeRef access, SwitchClauseData data)
    {
        if (data.ClauseStart < data.ClauseEnd && (Types.Flags(type) & TypeFlags.Union) != 0 && GetKeyPropertyName(type) is { } key && key == GetAccessedPropertyName(access))
        {
            var clauseTypes = GetSwitchClauseTypes(data.SwitchStatement)[data.ClauseStart..data.ClauseEnd];
            var candidate = GetUnionType(Array.ConvertAll(clauseTypes, t =>
            {
                var c = GetConstituentTypeForKeyType(type, t);
                return c.IsNone ? unknownType : c;
            }));
            if (candidate != unknownType)
            {
                return candidate;
            }
        }
        return NarrowTypeByDiscriminant(type, access, t => NarrowTypeBySwitchOnDiscriminant(t, data));
    }

    private Type NarrowTypeByTruthiness(FlowState f, Type type, NodeRef expr, bool assumeTrue)
    {
        if (IsMatchingReference(f.Reference, expr))
        {
            return GetAdjustedTypeWithFacts(type, assumeTrue ? TypeFacts.Truthy : TypeFacts.Falsy);
        }
        if (strictNullChecks && assumeTrue && OptionalChainContainsReference(expr, f.Reference))
        {
            type = GetAdjustedTypeWithFacts(type, TypeFacts.NEUndefinedOrNull);
        }
        var access = GetDiscriminantPropertyAccess(f, expr, type);
        if (!access.IsNone)
        {
            return NarrowTypeByDiscriminant(type, access, t => GetTypeWithFacts(t, assumeTrue ? TypeFacts.Truthy : TypeFacts.Falsy));
        }
        return type;
    }

    private bool IsTypePresencePossible(Type type, string propName, bool assumeTrue)
    {
        var prop = GetPropertyOfType(type, propName);
        return !prop.IsNone
            ? (SymbolFlagsOf(prop) & SymbolFlags.Optional) != 0 || (GetCheckFlags(prop) & CheckFlags.Partial) != 0 || assumeTrue
            : GetApplicableIndexInfoForName(type, propName) is not null || !assumeTrue;
    }

    private Type NarrowTypeByInKeyword(Type type, Type nameType, bool assumeTrue)
    {
        var name = GetPropertyNameFromType(nameType);
        var isKnownProperty = SomeType(type, t => IsTypePresencePossible(t, name, assumeTrue: true));
        if (isKnownProperty)
        {
            // If the check is for a known property (i.e. a property declared in some constituent of
            // the target type), we filter the target type by presence of absence of the property.
            return FilterType(type, t => IsTypePresencePossible(t, name, assumeTrue));
        }
        if (assumeTrue)
        {
            // If the check is for an unknown property, we intersect the target type with `Record<X, unknown>`,
            // where X is the name of the property.
            var recordSymbol = GetGlobalRecordSymbol();
            if (!recordSymbol.IsNone)
            {
                return GetIntersectionType([type, GetTypeAliasInstantiation(recordSymbol, [nameType, unknownType])]);
            }
        }
        return type;
    }

    // checker.ts getGlobalRecordSymbol
    private SymbolRef GetGlobalRecordSymbol()
    {
        if (deferredGlobalRecordSymbol.IsNone)
        {
            var symbol = GetGlobalSymbol("Record", SymbolFlags.TypeAlias, null);
            deferredGlobalRecordSymbol = symbol.IsNone ? unknownSymbol : symbol;
        }
        return deferredGlobalRecordSymbol == unknownSymbol ? SymbolRef.None : deferredGlobalRecordSymbol;
    }

    private SymbolRef deferredGlobalRecordSymbol;

    private Type NarrowTypeByBooleanComparison(FlowState f, Type type, NodeRef expr, NodeRef boolNode, SyntaxKind op, bool assumeTrue)
    {
        assumeTrue = (assumeTrue != (Kind(boolNode) == SyntaxKind.TrueKeyword)) != (op != SyntaxKind.ExclamationEqualsEqualsToken && op != SyntaxKind.ExclamationEqualsToken);
        return NarrowType(f, type, expr, assumeTrue);
    }

    private Type NarrowTypeByBinaryExpression(FlowState f, Type type, NodeRef expr, bool assumeTrue)
    {
        var store = StoreOf(expr);
        var n = expr.Node;
        var reference = f.Reference;
        switch (store.Kind(store.OperatorToken(n)))
        {
            case SyntaxKind.EqualsToken:
            case SyntaxKind.BarBarEqualsToken:
            case SyntaxKind.AmpersandAmpersandEqualsToken:
            case SyntaxKind.QuestionQuestionEqualsToken:
                return NarrowTypeByTruthiness(f, NarrowType(f, type, At(expr, store.Right(n)), assumeTrue), At(expr, store.Left(n)), assumeTrue);
            case SyntaxKind.EqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsToken:
            case SyntaxKind.EqualsEqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsEqualsToken:
            {
                var op = store.Kind(store.OperatorToken(n));
                var left = At(expr, GetReferenceCandidate(store, store.Left(n)));
                var right = At(expr, GetReferenceCandidate(store, store.Right(n)));
                if (store.Kind(left.Node) == SyntaxKind.TypeOfExpression && Utilities.IsStringLiteralLike(store, right.Node))
                {
                    return NarrowTypeByTypeof(f, type, left, op, right, assumeTrue);
                }
                if (store.Kind(right.Node) == SyntaxKind.TypeOfExpression && Utilities.IsStringLiteralLike(store, left.Node))
                {
                    return NarrowTypeByTypeof(f, type, right, op, left, assumeTrue);
                }
                if (IsMatchingReference(reference, left))
                {
                    return NarrowTypeByEquality(type, op, right, assumeTrue);
                }
                if (IsMatchingReference(reference, right))
                {
                    return NarrowTypeByEquality(type, op, left, assumeTrue);
                }
                if (strictNullChecks)
                {
                    if (OptionalChainContainsReference(left, reference))
                    {
                        type = NarrowTypeByOptionalChainContainment(type, op, right, assumeTrue);
                    }
                    else if (OptionalChainContainsReference(right, reference))
                    {
                        type = NarrowTypeByOptionalChainContainment(type, op, left, assumeTrue);
                    }
                }
                var leftAccess = GetDiscriminantPropertyAccess(f, left, type);
                if (!leftAccess.IsNone)
                {
                    return NarrowTypeByDiscriminantProperty(f, type, leftAccess, op, right, assumeTrue);
                }
                var rightAccess = GetDiscriminantPropertyAccess(f, right, type);
                if (!rightAccess.IsNone)
                {
                    return NarrowTypeByDiscriminantProperty(f, type, rightAccess, op, left, assumeTrue);
                }
                if (IsMatchingConstructorReference(f, left))
                {
                    return NarrowTypeByConstructor(type, op, right, assumeTrue);
                }
                if (IsMatchingConstructorReference(f, right))
                {
                    return NarrowTypeByConstructor(type, op, left, assumeTrue);
                }
                if (store.Kind(right.Node) is SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword && !Utilities.IsAccessExpression(store, left.Node))
                {
                    return NarrowTypeByBooleanComparison(f, type, left, right, op, assumeTrue);
                }
                if (store.Kind(left.Node) is SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword && !Utilities.IsAccessExpression(store, right.Node))
                {
                    return NarrowTypeByBooleanComparison(f, type, right, left, op, assumeTrue);
                }
                break;
            }
            case SyntaxKind.InstanceOfKeyword:
                return NarrowTypeByInstanceof(f, type, expr, assumeTrue);
            case SyntaxKind.InKeyword:
            {
                if (store.Kind(store.Left(n)) == SyntaxKind.PrivateIdentifier)
                {
                    return NarrowTypeByPrivateIdentifierInInExpression(f, type, expr, assumeTrue);
                }
                var target = At(expr, GetReferenceCandidate(store, store.Right(n)));
                if (ContainsMissingType(type) && IsAccessReferenceOn(reference, target))
                {
                    var leftType = GetTypeOfExpression(At(expr, store.Left(n)));
                    if (IsTypeUsableAsPropertyName(leftType) && GetAccessedPropertyName(reference) == GetPropertyNameFromType(leftType))
                    {
                        return GetTypeWithFacts(type, assumeTrue ? TypeFacts.NEUndefined : TypeFacts.EQUndefined);
                    }
                }
                if (IsMatchingReference(reference, target))
                {
                    var leftType = GetTypeOfExpression(At(expr, store.Left(n)));
                    if (IsTypeUsableAsPropertyName(leftType))
                    {
                        return NarrowTypeByInKeyword(type, leftType, assumeTrue);
                    }
                }
                break;
            }
            case SyntaxKind.CommaToken:
                return NarrowType(f, type, At(expr, store.Right(n)), assumeTrue);
            // Ordinarily we won't see && and || expressions in control flow analysis because the Binder breaks those
            // expressions down to individual conditional control flows. However, we may encounter them when analyzing
            // aliased conditional expressions.
            case SyntaxKind.AmpersandAmpersandToken:
                return assumeTrue
                    ? NarrowType(f, NarrowType(f, type, At(expr, store.Left(n)), assumeTrue: true), At(expr, store.Right(n)), assumeTrue: true)
                    : GetUnionType([NarrowType(f, type, At(expr, store.Left(n)), assumeTrue: false), NarrowType(f, type, At(expr, store.Right(n)), assumeTrue: false)]);
            case SyntaxKind.BarBarToken:
                return assumeTrue
                    ? GetUnionType([NarrowType(f, type, At(expr, store.Left(n)), assumeTrue: true), NarrowType(f, type, At(expr, store.Right(n)), assumeTrue: true)])
                    : NarrowType(f, NarrowType(f, type, At(expr, store.Left(n)), assumeTrue: false), At(expr, store.Right(n)), assumeTrue: false);
        }
        return type;
    }

    private Type NarrowTypeByPrivateIdentifierInInExpression(FlowState f, Type type, NodeRef expr, bool assumeTrue)
    {
        var store = StoreOf(expr);
        var target = At(expr, GetReferenceCandidate(store, store.Right(expr.Node)));
        if (!IsMatchingReference(f.Reference, target))
        {
            return type;
        }
        var symbol = LookupSymbolForPrivateIdentifierDeclaration(At(expr, store.Left(expr.Node)));
        if (symbol.IsNone)
        {
            return type;
        }
        var classSymbol = ParentOf(symbol);
        var valueDeclaration = ValueDeclarationOf(symbol);
        var targetType = !valueDeclaration.IsNone && Utilities.IsStatic(StoreOf(valueDeclaration), valueDeclaration.Node)
            ? GetTypeOfSymbol(classSymbol)
            : GetDeclaredTypeOfSymbol(classSymbol);
        return GetNarrowedType(type, targetType, assumeTrue, checkDerived: true);
    }

    private Type NarrowTypeByOptionalChainContainment(Type type, SyntaxKind op, NodeRef value, bool assumeTrue)
    {
        // We are in a branch of obj?.foo === value (or any one of the other equality operators). We narrow obj as follows:
        // When operator is === and type of value excludes undefined, null and undefined is removed from type of obj in true branch.
        // When operator is !== and type of value excludes undefined, null and undefined is removed from type of obj in false branch.
        // When operator is == and type of value excludes null and undefined, null and undefined is removed from type of obj in true branch.
        // When operator is != and type of value excludes null and undefined, null and undefined is removed from type of obj in false branch.
        // When operator is === and type of value is undefined, null and undefined is removed from type of obj in false branch.
        // When operator is !== and type of value is undefined, null and undefined is removed from type of obj in true branch.
        // When operator is == and type of value is null or undefined, null and undefined is removed from type of obj in false branch.
        // When operator is != and type of value is null or undefined, null and undefined is removed from type of obj in true branch.
        var equalsOperator = op is SyntaxKind.EqualsEqualsToken or SyntaxKind.EqualsEqualsEqualsToken;
        var nullableFlags = op is SyntaxKind.EqualsEqualsToken or SyntaxKind.ExclamationEqualsToken ? TypeFlags.Nullable : TypeFlags.Undefined;
        var valueType = GetTypeOfExpression(value);
        // Note that we include any and unknown in the exclusion test because their domain includes null and undefined.
        var removeNullable = (equalsOperator != assumeTrue && EveryType(valueType, t => (Types.Flags(t) & nullableFlags) != 0)) ||
            (equalsOperator == assumeTrue && EveryType(valueType, t => (Types.Flags(t) & (TypeFlags.AnyOrUnknown | nullableFlags)) == 0));
        return removeNullable ? GetAdjustedTypeWithFacts(type, TypeFacts.NEUndefinedOrNull) : type;
    }

    private Type NarrowTypeByEquality(Type type, SyntaxKind op, NodeRef value, bool assumeTrue)
    {
        if ((Types.Flags(type) & TypeFlags.Any) != 0)
        {
            return type;
        }
        if (op is SyntaxKind.ExclamationEqualsToken or SyntaxKind.ExclamationEqualsEqualsToken)
        {
            assumeTrue = !assumeTrue;
        }
        var valueType = GetTypeOfExpression(value);
        var doubleEquals = op is SyntaxKind.EqualsEqualsToken or SyntaxKind.ExclamationEqualsToken;
        if ((Types.Flags(valueType) & TypeFlags.Nullable) != 0)
        {
            if (!strictNullChecks)
            {
                return type;
            }
            var facts = doubleEquals
                ? assumeTrue ? TypeFacts.EQUndefinedOrNull : TypeFacts.NEUndefinedOrNull
                : (Types.Flags(valueType) & TypeFlags.Null) != 0
                ? assumeTrue ? TypeFacts.EQNull : TypeFacts.NENull
                : assumeTrue ? TypeFacts.EQUndefined : TypeFacts.NEUndefined;
            return GetAdjustedTypeWithFacts(type, facts);
        }
        if (assumeTrue)
        {
            if (!doubleEquals && ((Types.Flags(type) & TypeFlags.Unknown) != 0 || SomeType(type, IsEmptyAnonymousObjectType)))
            {
                if ((Types.Flags(valueType) & (TypeFlags.Primitive | TypeFlags.NonPrimitive)) != 0 || IsEmptyAnonymousObjectType(valueType))
                {
                    return valueType;
                }
                if ((Types.Flags(valueType) & TypeFlags.Object) != 0)
                {
                    return nonPrimitiveType;
                }
            }
            var filteredType = FilterType(type, t => AreTypesComparable(t, valueType) || (doubleEquals && IsCoercibleUnderDoubleEquals(t, valueType)));
            return ReplacePrimitivesWithLiterals(filteredType, valueType);
        }
        if (IsUnitType(valueType))
        {
            return FilterType(type, t => !(IsUnitLikeType(t) && AreTypesComparable(t, valueType)));
        }
        return type;
    }

    // checker.ts isCoercibleUnderDoubleEquals
    private bool IsCoercibleUnderDoubleEquals(Type source, Type target) =>
        (Types.Flags(source) & (TypeFlags.Number | TypeFlags.String | TypeFlags.BooleanLiteral)) != 0 &&
        (Types.Flags(target) & (TypeFlags.Number | TypeFlags.String | TypeFlags.Boolean)) != 0;

    private Type NarrowTypeByTypeof(FlowState f, Type type, NodeRef typeOfExpr, SyntaxKind op, NodeRef literal, bool assumeTrue)
    {
        // We have '==', '!=', '===', or !==' operator with 'typeof xxx' and string literal operands
        if (op is SyntaxKind.ExclamationEqualsToken or SyntaxKind.ExclamationEqualsEqualsToken)
        {
            assumeTrue = !assumeTrue;
        }
        var store = StoreOf(typeOfExpr);
        var target = At(typeOfExpr, GetReferenceCandidate(store, store.Expression(typeOfExpr.Node)));
        var text = store.Texts.GetString(store.Text(literal.Node)) ?? "";
        if (!IsMatchingReference(f.Reference, target))
        {
            if (strictNullChecks && OptionalChainContainsReference(target, f.Reference) && assumeTrue == (text != "undefined"))
            {
                type = GetAdjustedTypeWithFacts(type, TypeFacts.NEUndefinedOrNull);
            }
            var propertyAccess = GetDiscriminantPropertyAccess(f, target, type);
            if (!propertyAccess.IsNone)
            {
                return NarrowTypeByDiscriminant(type, propertyAccess, t => NarrowTypeByLiteralExpression(t, text, assumeTrue));
            }
            return type;
        }
        return NarrowTypeByLiteralExpression(type, text, assumeTrue);
    }

    private Type NarrowTypeByLiteralExpression(Type type, string text, bool assumeTrue)
    {
        if (assumeTrue)
        {
            return NarrowTypeByTypeName(type, text);
        }
        var facts = TypeofNEFacts(text);
        return GetAdjustedTypeWithFacts(type, facts != TypeFacts.None ? facts : TypeFacts.TypeofNEHostObject);
    }

    private Type NarrowTypeBySwitchOptionalChainContainment(Type type, SwitchClauseData data, Func<Type, bool> clauseCheck)
    {
        var everyClauseChecks = data.ClauseStart != data.ClauseEnd && Array.TrueForAll(GetSwitchClauseTypes(data.SwitchStatement)[data.ClauseStart..data.ClauseEnd], t => clauseCheck(t));
        return everyClauseChecks ? GetTypeWithFacts(type, TypeFacts.NEUndefinedOrNull) : type;
    }

    private Type NarrowTypeBySwitchOnDiscriminant(Type type, SwitchClauseData data)
    {
        // We only narrow if all case expressions specify
        // values with unit types, except for the case where
        // `type` is unknown. In this instance we map object
        // types to the nonPrimitive type and narrow with that.
        var switchTypes = GetSwitchClauseTypes(data.SwitchStatement);
        if (switchTypes.Length == 0)
        {
            return type;
        }
        var clauseTypes = switchTypes[data.ClauseStart..data.ClauseEnd];
        var hasDefaultClause = data.ClauseStart == data.ClauseEnd || Array.IndexOf(clauseTypes, neverType) >= 0;
        if ((Types.Flags(type) & TypeFlags.Unknown) != 0 && !hasDefaultClause)
        {
            List<Type>? groundClauseTypes = null;
            for (var i = 0; i < clauseTypes.Length; i++)
            {
                var t = clauseTypes[i];
                if ((Types.Flags(t) & (TypeFlags.Primitive | TypeFlags.NonPrimitive)) != 0)
                {
                    groundClauseTypes?.Add(t);
                }
                else if ((Types.Flags(t) & TypeFlags.Object) != 0)
                {
                    groundClauseTypes ??= [.. clauseTypes[..i]];
                    groundClauseTypes.Add(nonPrimitiveType);
                }
                else
                {
                    return type;
                }
            }
            return GetUnionType(groundClauseTypes is null ? clauseTypes : groundClauseTypes.ToArray());
        }
        var discriminantType = GetUnionType(clauseTypes);
        var caseType = (Types.Flags(discriminantType) & TypeFlags.Never) != 0 ? neverType
            : ReplacePrimitivesWithLiterals(FilterType(type, t => AreTypesComparable(discriminantType, t)), discriminantType);
        if (!hasDefaultClause)
        {
            return caseType;
        }
        var defaultType = FilterType(type, t => !(IsUnitLikeType(t) && ContainsComparableUnit(switchTypes, (Types.Flags(t) & TypeFlags.Undefined) != 0 ? undefinedType : GetRegularTypeOfLiteralType(ExtractUnitType(t)))));
        return (Types.Flags(caseType) & TypeFlags.Never) != 0 ? defaultType : GetUnionType([caseType, defaultType]);
    }

    // contains(switchTypes, value, (t1, t2) => isUnitType(t1) && areTypesComparable(t1, t2))
    private bool ContainsComparableUnit(Type[] switchTypes, Type value)
    {
        foreach (var t in switchTypes)
        {
            if (IsUnitType(t) && AreTypesComparable(t, value))
            {
                return true;
            }
        }
        return false;
    }

    private Type NarrowTypeByTypeName(Type type, string typeName)
    {
        switch (typeName)
        {
            case "string":
                return NarrowTypeByTypeFacts(type, stringType, TypeFacts.TypeofEQString);
            case "number":
                return NarrowTypeByTypeFacts(type, numberType, TypeFacts.TypeofEQNumber);
            case "bigint":
                return NarrowTypeByTypeFacts(type, bigintType, TypeFacts.TypeofEQBigInt);
            case "boolean":
                return NarrowTypeByTypeFacts(type, booleanType, TypeFacts.TypeofEQBoolean);
            case "symbol":
                return NarrowTypeByTypeFacts(type, esSymbolType, TypeFacts.TypeofEQSymbol);
            case "object":
                return (Types.Flags(type) & TypeFlags.Any) != 0 ? type
                    : GetUnionType([NarrowTypeByTypeFacts(type, nonPrimitiveType, TypeFacts.TypeofEQObject), NarrowTypeByTypeFacts(type, nullType, TypeFacts.EQNull)]);
            case "function":
                return (Types.Flags(type) & TypeFlags.Any) != 0 ? type : NarrowTypeByTypeFacts(type, globalFunctionType, TypeFacts.TypeofEQFunction);
            case "undefined":
                return NarrowTypeByTypeFacts(type, undefinedType, TypeFacts.EQUndefined);
        }
        return NarrowTypeByTypeFacts(type, nonPrimitiveType, TypeFacts.TypeofEQHostObject);
    }

    private Type NarrowTypeByTypeFacts(Type type, Type impliedType, TypeFacts facts) =>
        MapType(type, t =>
            // We first check if a constituent is a subtype of the implied type. If so, we either keep or eliminate
            // the constituent based on its type facts. We use the strict subtype relation because it treats `object`
            // as a subtype of `{}`, and we need the type facts check because function types are subtypes of `object`,
            // but are classified as "function" according to `typeof`.
            IsTypeStrictSubtypeOf(t, impliedType) ? HasTypeFacts(t, facts) ? t : neverType :
            // We next check if the consituent is a supertype of the implied type. If so, we substitute the implied
            // type. This handles top types like `unknown` and `{}`, and supertypes like `{ toString(): string }`.
            IsTypeSubtypeOf(impliedType, t) ? impliedType :
            // Neither the constituent nor the implied type is a subtype of the other, however their domains may still
            // overlap. For example, an unconstrained type parameter and type `string`. If the type facts indicate
            // possible overlap, we form an intersection. Otherwise, we eliminate the constituent.
            HasTypeFacts(t, facts) ? GetIntersectionType([t, impliedType]) :
            neverType);

    private Type NarrowTypeBySwitchOnTypeOf(Type type, SwitchClauseData data)
    {
        var witnesses = GetSwitchClauseTypeOfWitnesses(data.SwitchStatement);
        if (witnesses is null)
        {
            return type;
        }
        // Equal start and end denotes implicit fallthrough; undefined marks explicit default clause.
        var store = StoreOf(data.SwitchStatement);
        var clauses = store.Nodes(store.Clauses(store.CaseBlock(data.SwitchStatement.Node)));
        var defaultIndex = Array.FindIndex(clauses.ToArray(), c => store.Kind(c) == SyntaxKind.DefaultClause);
        var hasDefaultClause = data.ClauseStart == data.ClauseEnd || (defaultIndex >= data.ClauseStart && defaultIndex < data.ClauseEnd);
        if (hasDefaultClause)
        {
            // In the default clause we filter constituents down to those that are not-equal to all handled cases.
            var notEqualFacts = GetNotEqualFactsFromTypeofSwitch(data.ClauseStart, data.ClauseEnd, witnesses);
            return FilterType(type, t => GetTypeFacts(t, notEqualFacts) == notEqualFacts);
        }
        // In the non-default cause we create a union of the type narrowed by each of the listed cases.
        var clauseWitnesses = witnesses[data.ClauseStart..data.ClauseEnd];
        return GetUnionType(Array.ConvertAll(clauseWitnesses, text => text is not null ? NarrowTypeByTypeName(type, text) : neverType));
    }

    // checker.ts getNotEqualFactsFromTypeofSwitch
    private static TypeFacts GetNotEqualFactsFromTypeofSwitch(int start, int end, string?[] witnesses)
    {
        var facts = TypeFacts.None;
        for (var i = 0; i < witnesses.Length; i++)
        {
            var witness = i < start || i >= end ? witnesses[i] : null;
            if (witness is not null)
            {
                var f = TypeofNEFacts(witness);
                facts |= f != TypeFacts.None ? f : TypeFacts.TypeofNEHostObject;
            }
        }
        return facts;
    }

    private Type NarrowTypeBySwitchOnTrue(FlowState f, Type type, SwitchClauseData data)
    {
        var store = StoreOf(data.SwitchStatement);
        var clauses = store.Nodes(store.Clauses(store.CaseBlock(data.SwitchStatement.Node))).ToArray();
        var defaultIndex = Array.FindIndex(clauses, c => store.Kind(c) == SyntaxKind.DefaultClause);
        var hasDefaultClause = data.ClauseStart == data.ClauseEnd || (defaultIndex >= data.ClauseStart && defaultIndex < data.ClauseEnd);
        // First, narrow away all of the cases that preceded this set of cases.
        for (var i = 0; i < data.ClauseStart; i++)
        {
            if (store.Kind(clauses[i]) == SyntaxKind.CaseClause)
            {
                type = NarrowType(f, type, At(data.SwitchStatement, store.Expression(clauses[i])), assumeTrue: false);
            }
        }
        // If our current set has a default, then none the other cases were hit either.
        // There's no point in narrowing by the the other cases in the set, since we can
        // get here through other paths.
        if (hasDefaultClause)
        {
            for (var i = data.ClauseEnd; i < clauses.Length; i++)
            {
                if (store.Kind(clauses[i]) == SyntaxKind.CaseClause)
                {
                    type = NarrowType(f, type, At(data.SwitchStatement, store.Expression(clauses[i])), assumeTrue: false);
                }
            }
            return type;
        }
        // Now, narrow based on the cases in this set.
        var narrowed = new Type[data.ClauseEnd - data.ClauseStart];
        for (var i = data.ClauseStart; i < data.ClauseEnd; i++)
        {
            narrowed[i - data.ClauseStart] = store.Kind(clauses[i]) == SyntaxKind.CaseClause
                ? NarrowType(f, type, At(data.SwitchStatement, store.Expression(clauses[i])), assumeTrue: true)
                : neverType;
        }
        return GetUnionType(narrowed);
    }

    private bool IsMatchingConstructorReference(FlowState f, NodeRef expr)
    {
        var store = StoreOf(expr);
        var n = expr.Node;
        return ((store.Kind(n) == SyntaxKind.PropertyAccessExpression && store.Texts.Equals(store.EscapedText(store.Name(n)), "constructor")) ||
                (store.Kind(n) == SyntaxKind.ElementAccessExpression && Utilities.IsStringLiteralLike(store, store.ArgumentExpression(n)) &&
                    store.Texts.GetString(store.Text(store.ArgumentExpression(n))) == "constructor")) &&
            IsMatchingReference(f.Reference, At(expr, store.Expression(n)));
    }

    private Type NarrowTypeByConstructor(Type type, SyntaxKind op, NodeRef identifier, bool assumeTrue)
    {
        // Do not narrow when checking inequality.
        if (assumeTrue ? op is not (SyntaxKind.EqualsEqualsToken or SyntaxKind.EqualsEqualsEqualsToken) : op is not (SyntaxKind.ExclamationEqualsToken or SyntaxKind.ExclamationEqualsEqualsToken))
        {
            return type;
        }
        // Get the type of the constructor identifier expression, if it is not a function then do not narrow.
        var identifierType = GetTypeOfExpression(identifier);
        if (!IsFunctionType(identifierType) && !IsConstructorType(identifierType))
        {
            return type;
        }
        // Get the prototype property of the type identifier so we can find out its type.
        var prototypeProperty = GetPropertyOfType(identifierType, "prototype");
        if (prototypeProperty.IsNone)
        {
            return type;
        }
        // Get the type of the prototype, if it is undefined, or the global `Object` or `Function` types then do not narrow.
        var prototypeType = GetTypeOfSymbol(prototypeProperty);
        var candidate = !IsTypeAny(Types, prototypeType) ? prototypeType : Type.None;
        if (candidate.IsNone || candidate == globalObjectType || candidate == globalFunctionType)
        {
            return type;
        }
        // If the type that is being narrowed is `any` then just return the `candidate` type since every type is a subtype of `any`.
        if (IsTypeAny(Types, type))
        {
            return candidate;
        }
        // Filter out types that are not considered to be "constructed by" the `candidate` type.
        return FilterType(type, t => IsConstructedBy(t, candidate));

        bool IsConstructedBy(Type source, Type target)
        {
            // If either the source or target type are a class type then we need to check that they are the same exact type.
            // This is because you may have a class `A` that defines some set of properties, and another class `B`
            // that defines the same set of properties as class `A`, in that case they are structurally the same
            // type, but when you do something like `instanceOfA.constructor === B` it will return false.
            if (((Types.Flags(source) & TypeFlags.Object) != 0 && (Types.ObjectFlagsOf(source) & ObjectFlags.Class) != 0) ||
                ((Types.Flags(target) & TypeFlags.Object) != 0 && (Types.ObjectFlagsOf(target) & ObjectFlags.Class) != 0))
            {
                return Types.Symbol(source) == Types.Symbol(target);
            }
            // For all other types just check that the `source` type is a subtype of the `target` type.
            return IsTypeSubtypeOf(source, target);
        }
    }

    private Type NarrowTypeByInstanceof(FlowState f, Type type, NodeRef expr, bool assumeTrue)
    {
        var store = StoreOf(expr);
        var left = At(expr, GetReferenceCandidate(store, store.Left(expr.Node)));
        if (!IsMatchingReference(f.Reference, left))
        {
            if (assumeTrue && strictNullChecks && OptionalChainContainsReference(left, f.Reference))
            {
                return GetAdjustedTypeWithFacts(type, TypeFacts.NEUndefinedOrNull);
            }
            return type;
        }
        var right = At(expr, store.Right(expr.Node));
        var rightType = GetTypeOfExpression(right);
        if (!IsTypeDerivedFrom(rightType, globalObjectType))
        {
            return type;
        }
        // if the right-hand side has an object type with a custom `[Symbol.hasInstance]` method, and that method
        // has a type predicate, use the type predicate to perform narrowing. This allows normal `object` types to
        // participate in `instanceof`, as per Step 2 of https://tc39.es/ecma262/#sec-instanceofoperator.
        var signature = GetEffectsSignature(expr);
        var predicate = signature.IsNone ? null : GetTypePredicateOfSignature(signature);
        if (predicate is not null && predicate.Kind == TypePredicateKind.Identifier && predicate.ParameterIndex == 0)
        {
            return GetNarrowedType(type, predicate.Type, assumeTrue, checkDerived: true);
        }
        if (!IsTypeDerivedFrom(rightType, globalFunctionType))
        {
            return type;
        }
        var instanceType = MapType(rightType, GetInstanceType);
        // Don't narrow from `any` if the target type is exactly `Object` or `Function`, and narrow
        // in the false branch only if the target is a non-empty object type.
        if ((IsTypeAny(Types, type) && (instanceType == globalObjectType || instanceType == globalFunctionType)) ||
            (!assumeTrue && !((Types.Flags(instanceType) & TypeFlags.Object) != 0 && !IsEmptyAnonymousObjectType(instanceType))))
        {
            return type;
        }
        return GetNarrowedType(type, instanceType, assumeTrue, checkDerived: true);
    }

    private Type GetInstanceType(Type constructorType)
    {
        var prototypePropertyType = GetTypeOfPropertyOfType(constructorType, "prototype");
        if (!prototypePropertyType.IsNone && !IsTypeAny(Types, prototypePropertyType))
        {
            return prototypePropertyType;
        }
        var constructSignatures = GetSignaturesOfType(constructorType, SignatureKind.Construct);
        if (constructSignatures.Count > 0)
        {
            return GetUnionType(constructSignatures.ConvertAll(signature => GetReturnTypeOfSignature(GetErasedSignature(signature))).ToArray());
        }
        // We use the empty object type to indicate we don't know the type of objects created by
        // this constructor function.
        return emptyObjectType;
    }

    /// <summary>checker.ts getNarrowedType.</summary>
    private Type GetNarrowedType(Type type, Type candidate, bool assumeTrue, bool checkDerived)
    {
        var key = (Types.Flags(type) & TypeFlags.Union) != 0
            ? "N" + type.Id + "," + candidate.Id + "," + ((assumeTrue ? 1 : 0) | (checkDerived ? 2 : 0))
            : null;
        return GetOrSetFlowCachedType(key, () => GetNarrowedTypeWorker(type, candidate, assumeTrue, checkDerived));
    }

    private Type GetNarrowedTypeWorker(Type type, Type candidate, bool assumeTrue, bool checkDerived)
    {
        if (!assumeTrue)
        {
            if (type == candidate)
            {
                return neverType;
            }
            if (checkDerived)
            {
                return FilterType(type, t => !IsTypeDerivedFrom(t, candidate));
            }
            type = (Types.Flags(type) & TypeFlags.Unknown) != 0 ? unknownUnionType : type;
            var trueType = GetNarrowedType(type, candidate, assumeTrue: true, checkDerived: false);
            return RecombineUnknownType(FilterType(type, t => !IsTypeSubsetOf(t, trueType)));
        }
        if ((Types.Flags(type) & TypeFlags.AnyOrUnknown) != 0)
        {
            return candidate;
        }
        if (type == candidate)
        {
            return candidate;
        }
        // We first attempt to filter the current type, narrowing constituents as appropriate and removing
        // constituents that are unrelated to the candidate.
        Func<Type, Type, bool> isRelated = checkDerived ? IsTypeDerivedFrom : IsTypeSubtypeOf;
        var keyPropertyName = (Types.Flags(type) & TypeFlags.Union) != 0 ? GetKeyPropertyName(type) : null;
        var narrowedType = MapType(candidate, c =>
        {
            // If a discriminant property is available, use that to reduce the type.
            var discriminant = keyPropertyName is not null ? GetTypeOfPropertyOfType(c, keyPropertyName) : Type.None;
            var matching = !discriminant.IsNone ? GetConstituentTypeForKeyType(type, discriminant) : Type.None;
            // For each constituent t in the current type, if t and and c are directly related, pick the most
            // specific of the two. When t and c are related in both directions, we prefer c for type predicates
            // because that is the asserted type, but t for `instanceof` because generics aren't reflected in
            // prototype object types.
            var directlyRelated = MapType(!matching.IsNone ? matching : type, checkDerived
                ? t => IsTypeDerivedFrom(t, c) ? t : IsTypeDerivedFrom(c, t) ? c : neverType
                : t => IsTypeStrictSubtypeOf(t, c) ? t : IsTypeStrictSubtypeOf(c, t) ? c : IsTypeSubtypeOf(t, c) ? t : IsTypeSubtypeOf(c, t) ? c : neverType);
            // If no constituents are directly related, create intersections for any generic constituents that
            // are related by constraint.
            return (Types.Flags(directlyRelated) & TypeFlags.Never) != 0
                ? MapType(type, t =>
                {
                    if (!MaybeTypeOfKind(t, TypeFlags.Instantiable))
                    {
                        return neverType;
                    }
                    var constraint = GetBaseConstraintOfType(t);
                    return isRelated(c, constraint.IsNone ? unknownType : constraint) ? GetIntersectionType([t, c]) : neverType;
                })
                : directlyRelated;
        });
        // If filtering produced a non-empty type, return that. Otherwise, pick the most specific of the two
        // based on assignability, or as a last resort produce an intersection.
        return (Types.Flags(narrowedType) & TypeFlags.Never) == 0 ? narrowedType :
            IsTypeSubtypeOf(candidate, type) ? candidate :
            IsTypeAssignableTo(type, candidate) ? type :
            IsTypeAssignableTo(candidate, type) ? candidate :
            GetIntersectionType([type, candidate]);
    }

    private Type NarrowTypeByCallExpression(FlowState f, Type type, NodeRef callExpression, bool assumeTrue)
    {
        var reference = f.Reference;
        var store = StoreOf(callExpression);
        if (HasMatchingArgument(callExpression, reference))
        {
            var signature = assumeTrue || !UtilitiesPublic.IsCallChain(store, callExpression.Node) ? GetEffectsSignature(callExpression) : Signature.None;
            var predicate = signature.IsNone ? null : GetTypePredicateOfSignature(signature);
            if (predicate is not null && predicate.Kind is TypePredicateKind.This or TypePredicateKind.Identifier)
            {
                return NarrowTypeByTypePredicate(f, type, predicate, callExpression, assumeTrue);
            }
        }
        var callee = store.Expression(callExpression.Node);
        if (ContainsMissingType(type) && store.Kind(callee) == SyntaxKind.PropertyAccessExpression)
        {
            if (IsAccessReferenceOn(reference, At(callExpression, GetReferenceCandidate(store, store.Expression(callee)))) &&
                store.Kind(store.Name(callee)) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(store.Name(callee)), "hasOwnProperty") &&
                store.Length(store.Arguments(callExpression.Node)) == 1)
            {
                var argument = store.Element(store.Arguments(callExpression.Node), 0);
                if (Utilities.IsStringLiteralLike(store, argument) && GetAccessedPropertyName(reference) == Utilities.EscapeLeadingUnderscores(store.Texts.GetString(store.Text(argument))!))
                {
                    return GetTypeWithFacts(type, assumeTrue ? TypeFacts.NEUndefined : TypeFacts.EQUndefined);
                }
            }
        }
        return type;
    }

    private Type NarrowTypeByTypePredicate(FlowState f, Type type, TypePredicateInfo predicate, NodeRef callExpression, bool assumeTrue)
    {
        // Don't narrow from 'any' if the predicate type is exactly 'Object' or 'Function'
        if (!predicate.Type.IsNone && !(IsTypeAny(Types, type) && (predicate.Type == globalObjectType || predicate.Type == globalFunctionType)))
        {
            var predicateArgument = GetTypePredicateArgument(predicate, callExpression);
            if (!predicateArgument.IsNone)
            {
                if (IsMatchingReference(f.Reference, predicateArgument))
                {
                    return GetNarrowedType(type, predicate.Type, assumeTrue, checkDerived: false);
                }
                if (strictNullChecks && OptionalChainContainsReference(predicateArgument, f.Reference) &&
                    ((assumeTrue && !HasTypeFacts(predicate.Type, TypeFacts.EQUndefined)) || (!assumeTrue && EveryType(predicate.Type, IsNullableType))))
                {
                    type = GetAdjustedTypeWithFacts(type, TypeFacts.NEUndefinedOrNull);
                }
                var access = GetDiscriminantPropertyAccess(f, predicateArgument, type);
                if (!access.IsNone)
                {
                    return NarrowTypeByDiscriminant(type, access, t => GetNarrowedType(t, predicate.Type, assumeTrue, checkDerived: false));
                }
            }
        }
        return type;
    }

    // Narrow the given type based on the given expression having the assumed boolean value. The returned type
    // will be a subtype or the same type as the argument.
    private Type NarrowType(FlowState f, Type type, NodeRef expr, bool assumeTrue)
    {
        var store = StoreOf(expr);
        var n = expr.Node;
        var parent = store.Parent(n);
        // for `a?.b`, we emulate a synthetic `a !== null && a !== undefined` condition for `a`
        if (Utilities.IsExpressionOfOptionalChainRoot(store, n) ||
            (store.Kind(parent) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(parent)) is SyntaxKind.QuestionQuestionToken or SyntaxKind.QuestionQuestionEqualsToken && store.Left(parent) == n))
        {
            return NarrowTypeByOptionality(f, type, expr, assumeTrue);
        }
        switch (store.Kind(n))
        {
            case SyntaxKind.Identifier:
                // When narrowing a reference to a const variable, non-assigned parameter, or readonly property, we inline
                // up to five levels of aliased conditional expressions that are themselves declared as const variables.
                if (!IsMatchingReference(f.Reference, expr) && inlineLevel < 5)
                {
                    var symbol = GetResolvedSymbol(expr);
                    if (IsConstantVariable(symbol))
                    {
                        var declaration = ValueDeclarationOf(symbol);
                        if (!declaration.IsNone && Kind(declaration) == SyntaxKind.VariableDeclaration && StoreOf(declaration).Type(declaration.Node).IsNone &&
                            !StoreOf(declaration).Initializer(declaration.Node).IsNone && IsConstantReference(f.Reference))
                        {
                            inlineLevel++;
                            var result = NarrowType(f, type, At(declaration, StoreOf(declaration).Initializer(declaration.Node)), assumeTrue);
                            inlineLevel--;
                            return result;
                        }
                    }
                }
                goto case SyntaxKind.ThisKeyword;
            case SyntaxKind.ThisKeyword:
            case SyntaxKind.SuperKeyword:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                return NarrowTypeByTruthiness(f, type, expr, assumeTrue);
            case SyntaxKind.CallExpression:
                return NarrowTypeByCallExpression(f, type, expr, assumeTrue);
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.NonNullExpression:
            case SyntaxKind.SatisfiesExpression:
                return NarrowType(f, type, At(expr, store.Expression(n)), assumeTrue);
            case SyntaxKind.BinaryExpression:
                return NarrowTypeByBinaryExpression(f, type, expr, assumeTrue);
            case SyntaxKind.PrefixUnaryExpression:
                if (store.Operator(n) == SyntaxKind.ExclamationToken)
                {
                    return NarrowType(f, type, At(expr, store.Operand(n)), !assumeTrue);
                }
                break;
        }
        return type;
    }

    private Type NarrowTypeByOptionality(FlowState f, Type type, NodeRef expr, bool assumePresent)
    {
        if (IsMatchingReference(f.Reference, expr))
        {
            return GetAdjustedTypeWithFacts(type, assumePresent ? TypeFacts.NEUndefinedOrNull : TypeFacts.EQUndefinedOrNull);
        }
        var access = GetDiscriminantPropertyAccess(f, expr, type);
        if (!access.IsNone)
        {
            return NarrowTypeByDiscriminant(type, access, t => GetTypeWithFacts(t, assumePresent ? TypeFacts.NEUndefinedOrNull : TypeFacts.EQUndefinedOrNull));
        }
        return type;
    }
}
