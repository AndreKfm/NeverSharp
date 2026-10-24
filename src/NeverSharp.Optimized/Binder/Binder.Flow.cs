// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/binder.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/binder/binder.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; the control flow part of the binder (bindChildren and the statement and
// expression binders): the order in which children are bound and which code is unreachable decide the symbols and
// containers of module B1 (unreachable code is bound in forEachChild order, without hoisting), so it is ported here;
// module B2 verifies the flow graph itself (Gate B section flow).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Binder
{
    // binder.ts ActiveLabel, kept on a stack of records (activeLabelList: the index of the innermost, -1 for none).
    private struct ActiveLabel
    {
        public int Next;
        public TextRef Name;
        public FlowNode BreakTarget;
        public FlowNode ContinueTarget;
        public bool Referenced;
    }

    // state used by control flow analysis
    private FlowNode currentFlow;
    private FlowNode currentBreakTarget;
    private FlowNode currentContinueTarget;
    private FlowNode currentReturnTarget;
    private FlowNode currentTrueTarget;
    private FlowNode currentFalseTarget;
    private FlowNode currentExceptionTarget;
    private FlowNode preSwitchCaseFlow;
    private int activeLabelList = -1;
    private ActiveLabel[] activeLabels = new ActiveLabel[8];
    private int activeLabelCount;
    private bool hasExplicitReturn;
    private bool inReturnPosition;
    private bool hasFlowEffects;
    private FlowNode unreachableFlow;

    // bindBinaryExpressionFlow's work stack (createBinaryExpressionTrampoline), shared by nested runs.
    private BinaryFrame[] binaryFrames = new BinaryFrame[16];
    private int binaryFrameCount;

    private enum BinaryState
    {
        Enter,
        Left,
        Operator,
        Right,
        Exit,
    }

    private struct BinaryFrame
    {
        public Node Node;
        public BinaryState State;
        public bool HasSaved; // inStrictModeStack/parentStack entries are defined (frames entered by the trampoline)
        public bool SavedInStrictMode;
        public Node SavedParent;
    }

    private void ClearFlowState()
    {
        currentFlow = default;
        currentBreakTarget = default;
        currentContinueTarget = default;
        currentReturnTarget = default;
        currentTrueTarget = default;
        currentFalseTarget = default;
        currentExceptionTarget = default;
        preSwitchCaseFlow = default;
        activeLabelList = -1;
        activeLabelCount = 0;
        hasExplicitReturn = false;
        inReturnPosition = false;
        hasFlowEffects = false;
        unreachableFlow = default;
        binaryFrameCount = 0;
    }

    // bindContainer, the part for control flow containers.
    private void BindControlFlowContainer(Node node, ContainerFlags containerFlags)
    {
        var saveCurrentFlow = currentFlow;
        var saveBreakTarget = currentBreakTarget;
        var saveContinueTarget = currentContinueTarget;
        var saveReturnTarget = currentReturnTarget;
        var saveExceptionTarget = currentExceptionTarget;
        var saveActiveLabelList = activeLabelList;
        var saveHasExplicitReturn = hasExplicitReturn;
        var saveSeenThisKeyword = seenThisKeyword;
        var kind = store.Kind(node);
        var isImmediatelyInvoked = (
            (containerFlags & ContainerFlags.IsFunctionExpression) != 0 &&
            !Utilities.HasSyntacticModifier(store, node, ModifierFlags.Async) &&
            store.AsteriskToken(node).IsNone &&
            !Utilities.GetImmediatelyInvokedFunctionExpression(store, node).IsNone) || kind == SyntaxKind.ClassStaticBlockDeclaration;
        // A non-async, non-generator IIFE is considered part of the containing control flow. Return statements behave
        // similarly to break statements that exit to a label just past the statement body.
        if (!isImmediatelyInvoked)
        {
            currentFlow = b.CreateFlowNode(FlowFlags.Start, Node.None, FlowNode.None);
            if ((containerFlags & (ContainerFlags.IsFunctionExpression | ContainerFlags.IsObjectLiteralOrClassExpressionMethodOrAccessor)) != 0)
            {
                b.SetNodeOf(currentFlow, node);
            }
        }
        // We create a return control flow graph for IIFEs and constructors. For constructors
        // we use the return control flow graph in strict property initialization checks.
        var isJsFunction = Utilities.IsInJSFile(store, node) && (kind == SyntaxKind.FunctionDeclaration || kind == SyntaxKind.FunctionExpression);
        currentReturnTarget = isImmediatelyInvoked || kind == SyntaxKind.Constructor || isJsFunction ? CreateBranchLabel() : FlowNode.None;
        currentExceptionTarget = default;
        currentBreakTarget = default;
        currentContinueTarget = default;
        activeLabelList = -1;
        hasExplicitReturn = false;
        seenThisKeyword = false;
        BindChildren(node);
        // Reset flags (for incremental scenarios)
        store.SetFlags(node, store.Flags(node) & ~(NodeFlags.ReachabilityAndEmitFlags | NodeFlags.ContainsThis));
        if ((b.Flags(currentFlow) & FlowFlags.Unreachable) == 0 && (containerFlags & ContainerFlags.IsFunctionLike) != 0 && Utilities.NodeIsPresent(store, store.Body(node)))
        {
            store.SetFlags(node, store.Flags(node) | NodeFlags.HasImplicitReturn);
            if (hasExplicitReturn)
            {
                store.SetFlags(node, store.Flags(node) | NodeFlags.HasExplicitReturn);
            }
            b.SetEndFlowNodeOf(node, currentFlow);
        }
        if (seenThisKeyword)
        {
            store.SetFlags(node, store.Flags(node) | NodeFlags.ContainsThis);
        }
        if (kind == SyntaxKind.SourceFile)
        {
            store.SetFlags(node, store.Flags(node) | emitFlags);
            b.SetEndFlowNodeOf(node, currentFlow);
        }
        if (!currentReturnTarget.IsNone)
        {
            AddAntecedent(currentReturnTarget, currentFlow);
            currentFlow = FinishFlowLabel(currentReturnTarget);
            if (kind == SyntaxKind.Constructor || kind == SyntaxKind.ClassStaticBlockDeclaration || isJsFunction)
            {
                b.SetReturnFlowNodeOf(node, currentFlow);
            }
        }
        if (!isImmediatelyInvoked)
        {
            currentFlow = saveCurrentFlow;
        }
        currentBreakTarget = saveBreakTarget;
        currentContinueTarget = saveContinueTarget;
        currentReturnTarget = saveReturnTarget;
        currentExceptionTarget = saveExceptionTarget;
        activeLabelList = saveActiveLabelList;
        hasExplicitReturn = saveHasExplicitReturn;
        seenThisKeyword = (containerFlags & ContainerFlags.PropagatesThisKeyword) != 0 ? saveSeenThisKeyword || seenThisKeyword : saveSeenThisKeyword;
    }

    private void BindChildren(Node node)
    {
        var saveInAssignmentPattern = inAssignmentPattern;
        // Most nodes aren't valid in an assignment pattern, so we clear the value here
        // and set it before we descend into nodes that could actually be part of an assignment pattern.
        inAssignmentPattern = false;

        // Clear Unreachable flag from previous binding (for incremental scenarios)
        var isPotentiallyExecutable = Utilities.IsPotentiallyExecutableNode(store, node);
        if (isPotentiallyExecutable)
        {
            store.SetFlags(node, store.Flags(node) & ~NodeFlags.Unreachable);
        }

        if (currentFlow == unreachableFlow)
        {
            if (Utilities.CanHaveFlowNode(store, node) && !b.FlowNodeOf(node).IsNone)
            {
                b.SetFlowNodeOf(node, FlowNode.None);
            }
            if (isPotentiallyExecutable)
            {
                store.SetFlags(node, store.Flags(node) | NodeFlags.Unreachable);
            }
            BindEachChild(node);
            BindJSDoc(node);
            inAssignmentPattern = saveInAssignmentPattern;
            return;
        }

        var kind = store.Kind(node);
        if (SyntaxKind.FirstStatement <= kind && kind <= SyntaxKind.LastStatement && Utilities.CanHaveFlowNode(store, node))
        {
            b.SetFlowNodeOf(node, currentFlow);
        }

        switch (kind)
        {
            case SyntaxKind.WhileStatement:
                BindWhileStatement(node);
                break;
            case SyntaxKind.DoStatement:
                BindDoStatement(node);
                break;
            case SyntaxKind.ForStatement:
                BindForStatement(node);
                break;
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
                BindForInOrForOfStatement(node);
                break;
            case SyntaxKind.IfStatement:
                BindIfStatement(node);
                break;
            case SyntaxKind.ReturnStatement:
            case SyntaxKind.ThrowStatement:
                BindReturnOrThrow(node);
                break;
            case SyntaxKind.BreakStatement:
            case SyntaxKind.ContinueStatement:
                BindBreakOrContinueStatement(node);
                break;
            case SyntaxKind.TryStatement:
                BindTryStatement(node);
                break;
            case SyntaxKind.SwitchStatement:
                BindSwitchStatement(node);
                break;
            case SyntaxKind.CaseBlock:
                BindCaseBlock(node);
                break;
            case SyntaxKind.CaseClause:
                BindCaseClause(node);
                break;
            case SyntaxKind.ExpressionStatement:
                BindExpressionStatement(node);
                break;
            case SyntaxKind.LabeledStatement:
                BindLabeledStatement(node);
                break;
            case SyntaxKind.PrefixUnaryExpression:
                BindPrefixUnaryExpressionFlow(node);
                break;
            case SyntaxKind.PostfixUnaryExpression:
                BindPostfixUnaryExpressionFlow(node);
                break;
            case SyntaxKind.BinaryExpression:
                if (Utilities.IsDestructuringAssignment(store, node))
                {
                    // Carry over whether we are in an assignment pattern to
                    // binary expressions that could actually be an initializer
                    inAssignmentPattern = saveInAssignmentPattern;
                    BindDestructuringAssignmentFlow(node);
                    return;
                }
                BindBinaryExpressionFlow(node);
                break;
            case SyntaxKind.DeleteExpression:
                BindDeleteExpressionFlow(node);
                break;
            case SyntaxKind.ConditionalExpression:
                BindConditionalExpressionFlow(node);
                break;
            case SyntaxKind.VariableDeclaration:
                BindVariableDeclarationFlow(node);
                break;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                BindAccessExpressionFlow(node);
                break;
            case SyntaxKind.CallExpression:
                BindCallExpressionFlow(node);
                break;
            case SyntaxKind.NonNullExpression:
                BindNonNullExpressionFlow(node);
                break;
            case SyntaxKind.JSDocTypedefTag:
            case SyntaxKind.JSDocCallbackTag:
            case SyntaxKind.JSDocEnumTag:
                BindJSDocTypeAlias(node);
                break;
            case SyntaxKind.JSDocImportTag:
                BindJSDocImportTag(node);
                break;
            // In source files and blocks, bind functions first to match hoisting that occurs at runtime
            case SyntaxKind.SourceFile:
                BindEachFunctionsFirst(store.Statements(node));
                Bind(store.EndOfFileToken(node));
                break;
            case SyntaxKind.Block:
            case SyntaxKind.ModuleBlock:
                BindEachFunctionsFirst(store.Statements(node));
                break;
            case SyntaxKind.BindingElement:
                BindBindingElementFlow(node);
                break;
            case SyntaxKind.Parameter:
                BindParameterFlow(node);
                break;
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.SpreadElement:
                // Carry over whether we are in an assignment pattern of Object and Array literals
                // as well as their children that are valid assignment targets.
                inAssignmentPattern = saveInAssignmentPattern;
                BindEachChild(node);
                break;
            default:
                BindEachChild(node);
                break;
        }
        BindJSDoc(node);
        inAssignmentPattern = saveInAssignmentPattern;
    }

    private bool IsNarrowingExpression(Node expr)
    {
        switch (store.Kind(expr))
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.ThisKeyword:
                return true;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                return ContainsNarrowableReference(expr);
            case SyntaxKind.CallExpression:
                return HasNarrowableArgument(expr);
            case SyntaxKind.ParenthesizedExpression:
                if (FactoryUtilities.IsJSDocTypeAssertion(store, expr))
                {
                    return false;
                }
                return IsNarrowingExpression(store.Expression(expr));
            case SyntaxKind.NonNullExpression:
                return IsNarrowingExpression(store.Expression(expr));
            case SyntaxKind.BinaryExpression:
                return IsNarrowingBinaryExpression(expr);
            case SyntaxKind.PrefixUnaryExpression:
                return store.Operator(expr) == SyntaxKind.ExclamationToken && IsNarrowingExpression(store.Operand(expr));
            case SyntaxKind.TypeOfExpression:
                return IsNarrowingExpression(store.Expression(expr));
        }
        return false;
    }

    private bool IsNarrowableReference(Node expr)
    {
        switch (store.Kind(expr))
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.ThisKeyword:
            case SyntaxKind.SuperKeyword:
            case SyntaxKind.MetaProperty:
                return true;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.NonNullExpression:
                return IsNarrowableReference(store.Expression(expr));
            case SyntaxKind.ElementAccessExpression:
                var argumentExpression = store.ArgumentExpression(expr);
                return (Utilities.IsStringOrNumericLiteralLike(store, argumentExpression) || Utilities.IsEntityNameExpression(store, argumentExpression)) &&
                    IsNarrowableReference(store.Expression(expr));
            case SyntaxKind.BinaryExpression:
                var operatorKind = store.Kind(store.OperatorToken(expr));
                return operatorKind == SyntaxKind.CommaToken && IsNarrowableReference(store.Right(expr)) ||
                    Utilities.IsAssignmentOperator(operatorKind) && UtilitiesPublic.IsLeftHandSideExpression(store, store.Left(expr));
        }
        return false;
    }

    private bool ContainsNarrowableReference(Node expr)
    {
        return IsNarrowableReference(expr) || UtilitiesPublic.IsOptionalChain(store, expr) && ContainsNarrowableReference(store.Expression(expr));
    }

    private bool HasNarrowableArgument(Node expr)
    {
        var arguments = store.Arguments(expr);
        if (!arguments.IsNone)
        {
            foreach (var argument in store.Nodes(arguments))
            {
                if (ContainsNarrowableReference(argument))
                {
                    return true;
                }
            }
        }
        var expression = store.Expression(expr);
        if (
            store.Kind(expression) == SyntaxKind.PropertyAccessExpression &&
            ContainsNarrowableReference(store.Expression(expression)))
        {
            return true;
        }
        return false;
    }

    private bool IsNarrowingTypeofOperands(Node expr1, Node expr2)
    {
        return store.Kind(expr1) == SyntaxKind.TypeOfExpression && IsNarrowableOperand(store.Expression(expr1)) && UtilitiesPublic.IsStringLiteralLike(store, expr2);
    }

    private bool IsNarrowingBinaryExpression(Node expr)
    {
        switch (store.Kind(store.OperatorToken(expr)))
        {
            case SyntaxKind.EqualsToken:
            case SyntaxKind.BarBarEqualsToken:
            case SyntaxKind.AmpersandAmpersandEqualsToken:
            case SyntaxKind.QuestionQuestionEqualsToken:
                return ContainsNarrowableReference(store.Left(expr));
            case SyntaxKind.EqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsToken:
            case SyntaxKind.EqualsEqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsEqualsToken:
                var left = Utilities.SkipParentheses(store, store.Left(expr));
                var right = Utilities.SkipParentheses(store, store.Right(expr));
                return IsNarrowableOperand(left) || IsNarrowableOperand(right) ||
                    IsNarrowingTypeofOperands(right, left) || IsNarrowingTypeofOperands(left, right) ||
                    (IsBooleanLiteral(right) && IsNarrowingExpression(left) || IsBooleanLiteral(left) && IsNarrowingExpression(right));
            case SyntaxKind.InstanceOfKeyword:
                return IsNarrowableOperand(store.Left(expr));
            case SyntaxKind.InKeyword:
                return IsNarrowingExpression(store.Right(expr));
            case SyntaxKind.CommaToken:
                return IsNarrowingExpression(store.Right(expr));
        }
        return false;
    }

    private bool IsBooleanLiteral(Node node) => store.Kind(node) == SyntaxKind.TrueKeyword || store.Kind(node) == SyntaxKind.FalseKeyword;

    private bool IsNarrowableOperand(Node expr)
    {
        switch (store.Kind(expr))
        {
            case SyntaxKind.ParenthesizedExpression:
                return IsNarrowableOperand(store.Expression(expr));
            case SyntaxKind.BinaryExpression:
                switch (store.Kind(store.OperatorToken(expr)))
                {
                    case SyntaxKind.EqualsToken:
                        return IsNarrowableOperand(store.Left(expr));
                    case SyntaxKind.CommaToken:
                        return IsNarrowableOperand(store.Right(expr));
                }
                break;
        }
        return ContainsNarrowableReference(expr);
    }

    private FlowNode CreateBranchLabel()
    {
        return b.CreateFlowNode(FlowFlags.BranchLabel, Node.None, FlowNode.None);
    }

    private FlowNode CreateLoopLabel()
    {
        return b.CreateFlowNode(FlowFlags.LoopLabel, Node.None, FlowNode.None);
    }

    // createReduceLabel(target, antecedentsOf.antecedent, antecedent)
    private FlowNode CreateReduceLabel(FlowNode target, FlowNode antecedentsOf, FlowNode antecedent)
    {
        return b.CreateReduceLabel(target, antecedentsOf, antecedent);
    }

    private void SetFlowNodeReferenced(FlowNode flow)
    {
        // On first reference we set the Referenced flag, thereafter we set the Shared flag
        var flags = b.Flags(flow);
        b.SetFlags(flow, flags | ((flags & FlowFlags.Referenced) != 0 ? FlowFlags.Shared : FlowFlags.Referenced));
    }

    private void AddAntecedent(FlowNode label, FlowNode antecedent)
    {
        if ((b.Flags(antecedent) & FlowFlags.Unreachable) == 0 && !b.HasAntecedent(label, antecedent))
        {
            b.AddAntecedent(label, antecedent);
            SetFlowNodeReferenced(antecedent);
        }
    }

    private FlowNode CreateFlowCondition(FlowFlags flags, FlowNode antecedent, Node expression)
    {
        if ((b.Flags(antecedent) & FlowFlags.Unreachable) != 0)
        {
            return antecedent;
        }
        if (expression.IsNone)
        {
            return (flags & FlowFlags.TrueCondition) != 0 ? antecedent : unreachableFlow;
        }
        var kind = store.Kind(expression);
        if (
            (kind == SyntaxKind.TrueKeyword && (flags & FlowFlags.FalseCondition) != 0 ||
                kind == SyntaxKind.FalseKeyword && (flags & FlowFlags.TrueCondition) != 0) &&
            !Utilities.IsExpressionOfOptionalChainRoot(store, expression) && !Utilities.IsNullishCoalesce(store, store.Parent(expression)))
        {
            return unreachableFlow;
        }
        if (!IsNarrowingExpression(expression))
        {
            return antecedent;
        }
        SetFlowNodeReferenced(antecedent);
        return b.CreateFlowNode(flags, expression, antecedent);
    }

    private FlowNode CreateFlowSwitchClause(FlowNode antecedent, Node switchStatement, int clauseStart, int clauseEnd)
    {
        SetFlowNodeReferenced(antecedent);
        return b.CreateSwitchClause(antecedent, switchStatement, clauseStart, clauseEnd);
    }

    private FlowNode CreateFlowMutation(FlowFlags flags, FlowNode antecedent, Node node)
    {
        SetFlowNodeReferenced(antecedent);
        hasFlowEffects = true;
        var result = b.CreateFlowNode(flags, node, antecedent);
        if (!currentExceptionTarget.IsNone)
        {
            AddAntecedent(currentExceptionTarget, result);
        }
        return result;
    }

    private FlowNode CreateFlowCall(FlowNode antecedent, Node node)
    {
        SetFlowNodeReferenced(antecedent);
        hasFlowEffects = true;
        return b.CreateFlowNode(FlowFlags.Call, node, antecedent);
    }

    private FlowNode FinishFlowLabel(FlowNode flow)
    {
        var single = b.SingleAntecedent(flow, out var count);
        if (count == 0)
        {
            return unreachableFlow;
        }
        if (count == 1)
        {
            return single;
        }
        return flow;
    }

    private bool IsStatementCondition(Node node)
    {
        var nodeParent = store.Parent(node);
        switch (store.Kind(nodeParent))
        {
            case SyntaxKind.IfStatement:
            case SyntaxKind.WhileStatement:
            case SyntaxKind.DoStatement:
                return store.Expression(nodeParent) == node;
            case SyntaxKind.ForStatement:
            case SyntaxKind.ConditionalExpression:
                return store.Condition(nodeParent) == node;
        }
        return false;
    }

    private bool IsLogicalExpression(Node node)
    {
        while (true)
        {
            if (store.Kind(node) == SyntaxKind.ParenthesizedExpression)
            {
                node = store.Expression(node);
            }
            else if (store.Kind(node) == SyntaxKind.PrefixUnaryExpression && store.Operator(node) == SyntaxKind.ExclamationToken)
            {
                node = store.Operand(node);
            }
            else
            {
                return Utilities.IsLogicalOrCoalescingBinaryExpression(store, node);
            }
        }
    }

    private bool IsLogicalAssignmentExpression(Node node)
    {
        return Utilities.IsLogicalOrCoalescingAssignmentExpression(store, Utilities.SkipParentheses(store, node));
    }

    private bool IsTopLevelLogicalExpression(Node node)
    {
        while (
            store.Kind(store.Parent(node)) == SyntaxKind.ParenthesizedExpression ||
            store.Kind(store.Parent(node)) == SyntaxKind.PrefixUnaryExpression && store.Operator(store.Parent(node)) == SyntaxKind.ExclamationToken)
        {
            node = store.Parent(node);
        }
        var nodeParent = store.Parent(node);
        return !IsStatementCondition(node) &&
            !IsLogicalExpression(nodeParent) &&
            !(UtilitiesPublic.IsOptionalChain(store, nodeParent) && store.Expression(nodeParent) == node);
    }

    // doWithConditionalBranches(bind, node, trueTarget, falseTarget)
    private void BindWithConditionalBranches(Node node, FlowNode trueTarget, FlowNode falseTarget)
    {
        var savedTrueTarget = currentTrueTarget;
        var savedFalseTarget = currentFalseTarget;
        currentTrueTarget = trueTarget;
        currentFalseTarget = falseTarget;
        Bind(node);
        currentTrueTarget = savedTrueTarget;
        currentFalseTarget = savedFalseTarget;
    }

    private void BindCondition(Node node, FlowNode trueTarget, FlowNode falseTarget)
    {
        BindWithConditionalBranches(node, trueTarget, falseTarget);
        if (node.IsNone || !IsLogicalAssignmentExpression(node) && !IsLogicalExpression(node) && !(UtilitiesPublic.IsOptionalChain(store, node) && Utilities.IsOutermostOptionalChain(store, node)))
        {
            AddAntecedent(trueTarget, CreateFlowCondition(FlowFlags.TrueCondition, currentFlow, node));
            AddAntecedent(falseTarget, CreateFlowCondition(FlowFlags.FalseCondition, currentFlow, node));
        }
    }

    private void BindIterativeStatement(Node node, FlowNode breakTarget, FlowNode continueTarget)
    {
        var saveBreakTarget = currentBreakTarget;
        var saveContinueTarget = currentContinueTarget;
        currentBreakTarget = breakTarget;
        currentContinueTarget = continueTarget;
        Bind(node);
        currentBreakTarget = saveBreakTarget;
        currentContinueTarget = saveContinueTarget;
    }

    private FlowNode SetContinueTarget(Node node, FlowNode target)
    {
        var label = activeLabelList;
        while (label >= 0 && store.Kind(store.Parent(node)) == SyntaxKind.LabeledStatement)
        {
            activeLabels[label].ContinueTarget = target;
            label = activeLabels[label].Next;
            node = store.Parent(node);
        }
        return target;
    }

    private void BindWhileStatement(Node node)
    {
        var preWhileLabel = SetContinueTarget(node, CreateLoopLabel());
        var preBodyLabel = CreateBranchLabel();
        var postWhileLabel = CreateBranchLabel();
        AddAntecedent(preWhileLabel, currentFlow);
        currentFlow = preWhileLabel;
        BindCondition(store.Expression(node), preBodyLabel, postWhileLabel);
        currentFlow = FinishFlowLabel(preBodyLabel);
        BindIterativeStatement(store.Statement(node), postWhileLabel, preWhileLabel);
        AddAntecedent(preWhileLabel, currentFlow);
        currentFlow = FinishFlowLabel(postWhileLabel);
    }

    private void BindDoStatement(Node node)
    {
        var preDoLabel = CreateLoopLabel();
        var preConditionLabel = SetContinueTarget(node, CreateBranchLabel());
        var postDoLabel = CreateBranchLabel();
        AddAntecedent(preDoLabel, currentFlow);
        currentFlow = preDoLabel;
        BindIterativeStatement(store.Statement(node), postDoLabel, preConditionLabel);
        AddAntecedent(preConditionLabel, currentFlow);
        currentFlow = FinishFlowLabel(preConditionLabel);
        BindCondition(store.Expression(node), preDoLabel, postDoLabel);
        currentFlow = FinishFlowLabel(postDoLabel);
    }

    private void BindForStatement(Node node)
    {
        var preLoopLabel = SetContinueTarget(node, CreateLoopLabel());
        var preBodyLabel = CreateBranchLabel();
        var preIncrementorLabel = CreateBranchLabel();
        var postLoopLabel = CreateBranchLabel();
        Bind(store.Initializer(node));
        AddAntecedent(preLoopLabel, currentFlow);
        currentFlow = preLoopLabel;
        BindCondition(store.Condition(node), preBodyLabel, postLoopLabel);
        currentFlow = FinishFlowLabel(preBodyLabel);
        BindIterativeStatement(store.Statement(node), postLoopLabel, preIncrementorLabel);
        AddAntecedent(preIncrementorLabel, currentFlow);
        currentFlow = FinishFlowLabel(preIncrementorLabel);
        Bind(store.Incrementor(node));
        AddAntecedent(preLoopLabel, currentFlow);
        currentFlow = FinishFlowLabel(postLoopLabel);
    }

    private void BindForInOrForOfStatement(Node node)
    {
        var preLoopLabel = SetContinueTarget(node, CreateLoopLabel());
        var postLoopLabel = CreateBranchLabel();
        Bind(store.Expression(node));
        AddAntecedent(preLoopLabel, currentFlow);
        currentFlow = preLoopLabel;
        if (store.Kind(node) == SyntaxKind.ForOfStatement)
        {
            Bind(store.AwaitModifier(node));
        }
        AddAntecedent(postLoopLabel, currentFlow);
        var initializer = store.Initializer(node);
        Bind(initializer);
        if (store.Kind(initializer) != SyntaxKind.VariableDeclarationList)
        {
            BindAssignmentTargetFlow(initializer);
        }
        BindIterativeStatement(store.Statement(node), postLoopLabel, preLoopLabel);
        AddAntecedent(preLoopLabel, currentFlow);
        currentFlow = FinishFlowLabel(postLoopLabel);
    }

    private void BindIfStatement(Node node)
    {
        var thenLabel = CreateBranchLabel();
        var elseLabel = CreateBranchLabel();
        var postIfLabel = CreateBranchLabel();
        BindCondition(store.Expression(node), thenLabel, elseLabel);
        currentFlow = FinishFlowLabel(thenLabel);
        Bind(store.ThenStatement(node));
        AddAntecedent(postIfLabel, currentFlow);
        currentFlow = FinishFlowLabel(elseLabel);
        Bind(store.ElseStatement(node));
        AddAntecedent(postIfLabel, currentFlow);
        currentFlow = FinishFlowLabel(postIfLabel);
    }

    private void BindReturnOrThrow(Node node)
    {
        var savedInReturnPosition = inReturnPosition;
        inReturnPosition = true;
        Bind(store.Expression(node));
        inReturnPosition = savedInReturnPosition;
        if (store.Kind(node) == SyntaxKind.ReturnStatement)
        {
            hasExplicitReturn = true;
            if (!currentReturnTarget.IsNone)
            {
                AddAntecedent(currentReturnTarget, currentFlow);
            }
        }
        currentFlow = unreachableFlow;
        hasFlowEffects = true;
    }

    private int FindActiveLabel(TextRef name)
    {
        for (var label = activeLabelList; label >= 0; label = activeLabels[label].Next)
        {
            if (texts.Equals(activeLabels[label].Name, name))
            {
                return label;
            }
        }
        return -1;
    }

    private void BindBreakOrContinueFlow(Node node, FlowNode breakTarget, FlowNode continueTarget)
    {
        var flowLabel = store.Kind(node) == SyntaxKind.BreakStatement ? breakTarget : continueTarget;
        if (!flowLabel.IsNone)
        {
            AddAntecedent(flowLabel, currentFlow);
            currentFlow = unreachableFlow;
            hasFlowEffects = true;
        }
    }

    private void BindBreakOrContinueStatement(Node node)
    {
        var label = store.Label(node);
        Bind(label);
        if (!label.IsNone)
        {
            var activeLabel = FindActiveLabel(store.EscapedText(label));
            if (activeLabel >= 0)
            {
                activeLabels[activeLabel].Referenced = true;
                BindBreakOrContinueFlow(node, activeLabels[activeLabel].BreakTarget, activeLabels[activeLabel].ContinueTarget);
            }
        }
        else
        {
            BindBreakOrContinueFlow(node, currentBreakTarget, currentContinueTarget);
        }
    }

    private void BindTryStatement(Node node)
    {
        // We conservatively assume that *any* code in the try block can cause an exception, but we only need
        // to track code that causes mutations (because only mutations widen the possible control flow type of
        // a variable). The exceptionLabel is the target label for control flows that result from exceptions.
        // We add all mutation flow nodes as antecedents of this label such that we can analyze them as possible
        // antecedents of the start of catch or finally blocks. Furthermore, we add the current control flow to
        // represent exceptions that occur before any mutations.
        var saveReturnTarget = currentReturnTarget;
        var saveExceptionTarget = currentExceptionTarget;
        var normalExitLabel = CreateBranchLabel();
        var returnLabel = CreateBranchLabel();
        var exceptionLabel = CreateBranchLabel();
        var finallyBlock = store.FinallyBlock(node);
        if (!finallyBlock.IsNone)
        {
            currentReturnTarget = returnLabel;
        }
        AddAntecedent(exceptionLabel, currentFlow);
        currentExceptionTarget = exceptionLabel;
        Bind(store.TryBlock(node));
        AddAntecedent(normalExitLabel, currentFlow);
        var catchClause = store.CatchClause(node);
        if (!catchClause.IsNone)
        {
            // Start of catch clause is the target of exceptions from try block.
            currentFlow = FinishFlowLabel(exceptionLabel);
            // The currentExceptionTarget now represents control flows from exceptions in the catch clause.
            // Effectively, in a try-catch-finally, if an exception occurs in the try block, the catch block
            // acts like a second try block.
            exceptionLabel = CreateBranchLabel();
            AddAntecedent(exceptionLabel, currentFlow);
            currentExceptionTarget = exceptionLabel;
            Bind(catchClause);
            AddAntecedent(normalExitLabel, currentFlow);
        }
        currentReturnTarget = saveReturnTarget;
        currentExceptionTarget = saveExceptionTarget;
        if (!finallyBlock.IsNone)
        {
            // Possible ways control can reach the finally block:
            // 1) Normal completion of try block of a try-finally or try-catch-finally
            // 2) Normal completion of catch block (following exception in try block) of a try-catch-finally
            // 3) Return in try or catch block of a try-finally or try-catch-finally
            // 4) Exception in try block of a try-finally
            // 5) Exception in catch block of a try-catch-finally
            // When analyzing a control flow graph that starts inside a finally block we want to consider all
            // five possibilities above. However, when analyzing a control flow graph that starts outside (past)
            // the finally block, we only want to consider the first two (if we're past a finally block then it
            // must have completed normally). Likewise, when analyzing a control flow graph from return statements
            // in try or catch blocks in an IIFE, we only want to consider the third. To make this possible, we
            // inject a ReduceLabel node into the control flow graph. This node contains an alternate reduced
            // set of antecedents for the pre-finally label. As control flow analysis passes by a ReduceLabel
            // node, the pre-finally label is temporarily switched to the reduced antecedent set.
            var finallyLabel = CreateBranchLabel();
            b.ConcatenateAntecedents(finallyLabel, normalExitLabel, exceptionLabel, returnLabel);
            currentFlow = finallyLabel;
            Bind(finallyBlock);
            if ((b.Flags(currentFlow) & FlowFlags.Unreachable) != 0)
            {
                // If the end of the finally block is unreachable, the end of the entire try statement is unreachable.
                currentFlow = unreachableFlow;
            }
            else
            {
                // If we have an IIFE return target and return statements in the try or catch blocks, add a control
                // flow that goes back through the finally block and back through only the return statements.
                if (!currentReturnTarget.IsNone && b.HasAntecedents(returnLabel))
                {
                    AddAntecedent(currentReturnTarget, CreateReduceLabel(finallyLabel, returnLabel, currentFlow));
                }
                // If we have an outer exception target (i.e. a containing try-finally or try-catch-finally), add a
                // control flow that goes back through the finally blok and back through each possible exception source.
                if (!currentExceptionTarget.IsNone && b.HasAntecedents(exceptionLabel))
                {
                    AddAntecedent(currentExceptionTarget, CreateReduceLabel(finallyLabel, exceptionLabel, currentFlow));
                }
                // If the end of the finally block is reachable, but the end of the try and catch blocks are not,
                // convert the current flow to unreachable. For example, 'try { return 1; } finally { ... }' should
                // result in an unreachable current control flow.
                currentFlow = b.HasAntecedents(normalExitLabel) ? CreateReduceLabel(finallyLabel, normalExitLabel, currentFlow) : unreachableFlow;
            }
        }
        else
        {
            currentFlow = FinishFlowLabel(normalExitLabel);
        }
    }

    private void BindSwitchStatement(Node node)
    {
        var postSwitchLabel = CreateBranchLabel();
        Bind(store.Expression(node));
        var saveBreakTarget = currentBreakTarget;
        var savePreSwitchCaseFlow = preSwitchCaseFlow;
        currentBreakTarget = postSwitchLabel;
        preSwitchCaseFlow = currentFlow;
        var caseBlock = store.CaseBlock(node);
        Bind(caseBlock);
        AddAntecedent(postSwitchLabel, currentFlow);
        var hasDefault = false;
        foreach (var c in store.Nodes(store.Clauses(caseBlock)))
        {
            if (store.Kind(c) == SyntaxKind.DefaultClause)
            {
                hasDefault = true;
                break;
            }
        }
        // We mark a switch statement as possibly exhaustive if it has no default clause and if all
        // case clauses have unreachable end points (e.g. they all return). Note, we no longer need
        // this property in control flow analysis, it's there only for backwards compatibility.
        b.SetPossiblyExhaustiveOf(node, !hasDefault && !b.HasAntecedents(postSwitchLabel));
        if (!hasDefault)
        {
            AddAntecedent(postSwitchLabel, CreateFlowSwitchClause(preSwitchCaseFlow, node, 0, 0));
        }
        currentBreakTarget = saveBreakTarget;
        preSwitchCaseFlow = savePreSwitchCaseFlow;
        currentFlow = FinishFlowLabel(postSwitchLabel);
    }

    private void BindCaseBlock(Node node)
    {
        var clauses = store.Clauses(node);
        var switchExpression = store.Expression(store.Parent(node));
        var isNarrowingSwitch = store.Kind(switchExpression) == SyntaxKind.TrueKeyword || IsNarrowingExpression(switchExpression);
        var fallthroughFlow = unreachableFlow;
        var count = store.Length(clauses);

        for (var i = 0; i < count; i++)
        {
            var clauseStart = i;
            while (store.Length(store.Statements(store.Element(clauses, i))) == 0 && i + 1 < count)
            {
                if (fallthroughFlow == unreachableFlow)
                {
                    currentFlow = preSwitchCaseFlow;
                }
                Bind(store.Element(clauses, i));
                i++;
            }
            var preCaseLabel = CreateBranchLabel();
            AddAntecedent(preCaseLabel, isNarrowingSwitch ? CreateFlowSwitchClause(preSwitchCaseFlow, store.Parent(node), clauseStart, i + 1) : preSwitchCaseFlow);
            AddAntecedent(preCaseLabel, fallthroughFlow);
            currentFlow = FinishFlowLabel(preCaseLabel);
            var clause = store.Element(clauses, i);
            Bind(clause);
            fallthroughFlow = currentFlow;
            if ((b.Flags(currentFlow) & FlowFlags.Unreachable) == 0 && i != count - 1 && options.NoFallthroughCasesInSwitch == true)
            {
                b.SetFallthroughFlowNodeOf(clause, currentFlow);
            }
        }
    }

    private void BindCaseClause(Node node)
    {
        var saveCurrentFlow = currentFlow;
        currentFlow = preSwitchCaseFlow;
        Bind(store.Expression(node));
        currentFlow = saveCurrentFlow;
        BindEach(store.Statements(node));
    }

    private void BindExpressionStatement(Node node)
    {
        Bind(store.Expression(node));
        MaybeBindExpressionFlowIfCall(store.Expression(node));
    }

    private void MaybeBindExpressionFlowIfCall(Node node)
    {
        // A top level or comma expression call expression with a dotted function name and at least one argument
        // is potentially an assertion and is therefore included in the control flow.
        if (store.Kind(node) == SyntaxKind.CallExpression)
        {
            var callee = store.Expression(node);
            if (store.Kind(callee) != SyntaxKind.SuperKeyword && Utilities.IsDottedName(store, callee))
            {
                currentFlow = CreateFlowCall(currentFlow, node);
            }
        }
    }

    private void BindLabeledStatement(Node node)
    {
        var postStatementLabel = CreateBranchLabel();
        var label = store.Label(node);
        if (activeLabelCount == activeLabels.Length)
        {
            Array.Resize(ref activeLabels, activeLabels.Length * 2);
        }
        activeLabels[activeLabelCount] = new ActiveLabel
        {
            Next = activeLabelList,
            Name = store.EscapedText(label),
            BreakTarget = postStatementLabel,
            ContinueTarget = FlowNode.None,
            Referenced = false,
        };
        activeLabelList = activeLabelCount++;
        Bind(label);
        Bind(store.Statement(node));
        if (!activeLabels[activeLabelList].Referenced)
        {
            store.SetFlags(label, store.Flags(label) | NodeFlags.Unreachable);
        }
        activeLabelList = activeLabels[activeLabelList].Next;
        activeLabelCount--;
        AddAntecedent(postStatementLabel, currentFlow);
        currentFlow = FinishFlowLabel(postStatementLabel);
    }

    private void BindDestructuringTargetFlow(Node node)
    {
        if (store.Kind(node) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(node)) == SyntaxKind.EqualsToken)
        {
            BindAssignmentTargetFlow(store.Left(node));
        }
        else
        {
            BindAssignmentTargetFlow(node);
        }
    }

    private void BindAssignmentTargetFlow(Node node)
    {
        if (IsNarrowableReference(node))
        {
            currentFlow = CreateFlowMutation(FlowFlags.Assignment, currentFlow, node);
        }
        else if (store.Kind(node) == SyntaxKind.ArrayLiteralExpression)
        {
            foreach (var e in store.Nodes(store.Elements(node)))
            {
                if (store.Kind(e) == SyntaxKind.SpreadElement)
                {
                    BindAssignmentTargetFlow(store.Expression(e));
                }
                else
                {
                    BindDestructuringTargetFlow(e);
                }
            }
        }
        else if (store.Kind(node) == SyntaxKind.ObjectLiteralExpression)
        {
            foreach (var p in store.Nodes(store.Properties(node)))
            {
                if (store.Kind(p) == SyntaxKind.PropertyAssignment)
                {
                    BindDestructuringTargetFlow(store.Initializer(p));
                }
                else if (store.Kind(p) == SyntaxKind.ShorthandPropertyAssignment)
                {
                    BindAssignmentTargetFlow(store.Name(p));
                }
                else if (store.Kind(p) == SyntaxKind.SpreadAssignment)
                {
                    BindAssignmentTargetFlow(store.Expression(p));
                }
            }
        }
    }

    private void BindLogicalLikeExpression(Node node, FlowNode trueTarget, FlowNode falseTarget)
    {
        var preRightLabel = CreateBranchLabel();
        var operatorToken = store.OperatorToken(node);
        var operatorKind = store.Kind(operatorToken);
        if (operatorKind == SyntaxKind.AmpersandAmpersandToken || operatorKind == SyntaxKind.AmpersandAmpersandEqualsToken)
        {
            BindCondition(store.Left(node), preRightLabel, falseTarget);
        }
        else
        {
            BindCondition(store.Left(node), trueTarget, preRightLabel);
        }
        currentFlow = FinishFlowLabel(preRightLabel);
        Bind(operatorToken);

        if (Utilities.IsLogicalOrCoalescingAssignmentOperator(operatorKind))
        {
            BindWithConditionalBranches(store.Right(node), trueTarget, falseTarget);
            BindAssignmentTargetFlow(store.Left(node));

            AddAntecedent(trueTarget, CreateFlowCondition(FlowFlags.TrueCondition, currentFlow, node));
            AddAntecedent(falseTarget, CreateFlowCondition(FlowFlags.FalseCondition, currentFlow, node));
        }
        else
        {
            BindCondition(store.Right(node), trueTarget, falseTarget);
        }
    }

    private void BindPrefixUnaryExpressionFlow(Node node)
    {
        if (store.Operator(node) == SyntaxKind.ExclamationToken)
        {
            var saveTrueTarget = currentTrueTarget;
            currentTrueTarget = currentFalseTarget;
            currentFalseTarget = saveTrueTarget;
            BindEachChild(node);
            currentFalseTarget = currentTrueTarget;
            currentTrueTarget = saveTrueTarget;
        }
        else
        {
            BindEachChild(node);
            if (store.Operator(node) == SyntaxKind.PlusPlusToken || store.Operator(node) == SyntaxKind.MinusMinusToken)
            {
                BindAssignmentTargetFlow(store.Operand(node));
            }
        }
    }

    private void BindPostfixUnaryExpressionFlow(Node node)
    {
        BindEachChild(node);
        if (store.Operator(node) == SyntaxKind.PlusPlusToken || store.Operator(node) == SyntaxKind.MinusMinusToken)
        {
            BindAssignmentTargetFlow(store.Operand(node));
        }
    }

    private void BindDestructuringAssignmentFlow(Node node)
    {
        if (inAssignmentPattern)
        {
            inAssignmentPattern = false;
            Bind(store.OperatorToken(node));
            Bind(store.Right(node));
            inAssignmentPattern = true;
            Bind(store.Left(node));
        }
        else
        {
            inAssignmentPattern = true;
            Bind(store.Left(node));
            inAssignmentPattern = false;
            Bind(store.OperatorToken(node));
            Bind(store.Right(node));
        }
        BindAssignmentTargetFlow(store.Left(node));
    }

    /// <summary>
    /// binder.ts createBindBinaryExpressionFlow: the binary expression trampoline (factory/utilities.ts
    /// createBinaryExpressionTrampoline) with onEnter, onLeft, onOperator, onRight and onExit, on an explicit stack,
    /// so nested binary expressions on either side do not recurse.
    /// </summary>
    private void BindBinaryExpressionFlow(Node node)
    {
        var start = binaryFrameCount;
        PushBinaryFrame(node);
        // The work area shared by the frames of this run.
        var skip = false;
        while (binaryFrameCount > start)
        {
            var top = binaryFrameCount - 1;
            var frame = binaryFrames[top];
            var current = frame.Node;
            switch (frame.State)
            {
                case BinaryState.Enter:
                {
                    binaryFrames[top].State = BinaryState.Left;
                    if (top > start)
                    {
                        // Emulate the work that `bind` does before reaching `bindChildren`. A normal call to
                        // `bindBinaryExpressionFlow` will already have done this work.
                        SetParentOf(current, parent);
                        var saveInStrictMode = inStrictMode;
                        BindWorker(current);
                        var saveParent = parent;
                        parent = current;
                        skip = false;
                        binaryFrames[top].HasSaved = true;
                        binaryFrames[top].SavedInStrictMode = saveInStrictMode;
                        binaryFrames[top].SavedParent = saveParent;
                    }
                    // TODO: bindLogicalExpression is recursive - if we want to handle deeply nested `&&` expressions
                    // we'll need to handle the `bindLogicalExpression` scenarios in this state machine, too
                    // For now, though, since the common cases are chained `+`, leaving it recursive is fine
                    var operatorKind = store.Kind(store.OperatorToken(current));
                    if (Utilities.IsLogicalOrCoalescingBinaryOperator(operatorKind) || Utilities.IsLogicalOrCoalescingAssignmentOperator(operatorKind))
                    {
                        if (IsTopLevelLogicalExpression(current))
                        {
                            var postExpressionLabel = CreateBranchLabel();
                            var saveCurrentFlow = currentFlow;
                            var saveHasFlowEffects = hasFlowEffects;
                            hasFlowEffects = false;
                            BindLogicalLikeExpression(current, postExpressionLabel, postExpressionLabel);
                            currentFlow = hasFlowEffects ? FinishFlowLabel(postExpressionLabel) : saveCurrentFlow;
                            hasFlowEffects |= saveHasFlowEffects;
                        }
                        else
                        {
                            BindLogicalLikeExpression(current, currentTrueTarget, currentFalseTarget);
                        }
                        skip = true;
                    }
                    break;
                }
                case BinaryState.Left:
                    binaryFrames[top].State = BinaryState.Operator;
                    if (!skip)
                    {
                        var left = store.Left(current);
                        var isCommaOperator = store.Kind(store.OperatorToken(current)) == SyntaxKind.CommaToken;
                        if (MaybeBind(left))
                        {
                            if (isCommaOperator)
                            {
                                MaybeBindExpressionFlowIfCall(left);
                            }
                            PushBinaryFrame(left);
                        }
                        else if (isCommaOperator)
                        {
                            MaybeBindExpressionFlowIfCall(left);
                        }
                    }
                    break;
                case BinaryState.Operator:
                    binaryFrames[top].State = BinaryState.Right;
                    if (!skip)
                    {
                        Bind(store.OperatorToken(current));
                    }
                    break;
                case BinaryState.Right:
                    binaryFrames[top].State = BinaryState.Exit;
                    if (!skip)
                    {
                        var right = store.Right(current);
                        var isCommaOperator = store.Kind(store.OperatorToken(current)) == SyntaxKind.CommaToken;
                        if (MaybeBind(right))
                        {
                            if (isCommaOperator)
                            {
                                MaybeBindExpressionFlowIfCall(right);
                            }
                            PushBinaryFrame(right);
                        }
                        else if (isCommaOperator)
                        {
                            MaybeBindExpressionFlowIfCall(right);
                        }
                    }
                    break;
                default:
                {
                    if (!skip)
                    {
                        var operatorKind = store.Kind(store.OperatorToken(current));
                        if (Utilities.IsAssignmentOperator(operatorKind) && !Utilities.IsAssignmentTarget(store, current))
                        {
                            var left = store.Left(current);
                            BindAssignmentTargetFlow(left);
                            if (operatorKind == SyntaxKind.EqualsToken && store.Kind(left) == SyntaxKind.ElementAccessExpression)
                            {
                                if (IsNarrowableOperand(store.Expression(left)))
                                {
                                    currentFlow = CreateFlowMutation(FlowFlags.ArrayMutation, currentFlow, current);
                                }
                            }
                        }
                    }
                    if (frame.HasSaved)
                    {
                        inStrictMode = frame.SavedInStrictMode;
                        parent = frame.SavedParent;
                    }
                    skip = false;
                    binaryFrameCount--;
                    break;
                }
            }
        }
    }

    // maybeBind: whether the node is a binary expression (not a destructuring assignment) the trampoline enters;
    // otherwise binds it.
    private bool MaybeBind(Node node)
    {
        if (!node.IsNone && store.Kind(node) == SyntaxKind.BinaryExpression && !Utilities.IsDestructuringAssignment(store, node))
        {
            return true;
        }
        Bind(node);
        return false;
    }

    private void PushBinaryFrame(Node node)
    {
        if (binaryFrameCount == binaryFrames.Length)
        {
            Array.Resize(ref binaryFrames, binaryFrames.Length * 2);
        }
        binaryFrames[binaryFrameCount++] = new BinaryFrame { Node = node, State = BinaryState.Enter };
    }

    private void BindDeleteExpressionFlow(Node node)
    {
        BindEachChild(node);
        if (store.Kind(store.Expression(node)) == SyntaxKind.PropertyAccessExpression)
        {
            BindAssignmentTargetFlow(store.Expression(node));
        }
    }

    private void BindConditionalExpressionFlow(Node node)
    {
        var trueLabel = CreateBranchLabel();
        var falseLabel = CreateBranchLabel();
        var postExpressionLabel = CreateBranchLabel();
        var saveCurrentFlow = currentFlow;
        var saveHasFlowEffects = hasFlowEffects;
        hasFlowEffects = false;
        BindCondition(store.Condition(node), trueLabel, falseLabel);
        currentFlow = FinishFlowLabel(trueLabel);
        if (inReturnPosition)
        {
            b.SetFlowNodeWhenTrueOf(node, currentFlow);
        }
        Bind(store.QuestionToken(node));
        Bind(store.WhenTrue(node));
        AddAntecedent(postExpressionLabel, currentFlow);
        currentFlow = FinishFlowLabel(falseLabel);
        if (inReturnPosition)
        {
            b.SetFlowNodeWhenFalseOf(node, currentFlow);
        }
        Bind(store.ColonToken(node));
        Bind(store.WhenFalse(node));
        AddAntecedent(postExpressionLabel, currentFlow);
        currentFlow = hasFlowEffects ? FinishFlowLabel(postExpressionLabel) : saveCurrentFlow;
        hasFlowEffects |= saveHasFlowEffects;
    }

    private void BindInitializedVariableFlow(Node node)
    {
        var name = store.Kind(node) != SyntaxKind.OmittedExpression ? store.Name(node) : Node.None;
        if (!name.IsNone && UtilitiesPublic.IsBindingPattern(store, name))
        {
            foreach (var child in store.Nodes(store.Elements(name)))
            {
                BindInitializedVariableFlow(child);
            }
        }
        else
        {
            currentFlow = CreateFlowMutation(FlowFlags.Assignment, currentFlow, node);
        }
    }

    private void BindVariableDeclarationFlow(Node node)
    {
        BindEachChild(node);
        if (!store.Initializer(node).IsNone || IsForInOrOfStatement(store.Parent(store.Parent(node))))
        {
            BindInitializedVariableFlow(node);
        }
    }

    private bool IsForInOrOfStatement(Node node) => store.Kind(node) == SyntaxKind.ForInStatement || store.Kind(node) == SyntaxKind.ForOfStatement;

    private void BindBindingElementFlow(Node node)
    {
        // When evaluating a binding pattern, the initializer is evaluated before the binding pattern, per:
        // - https://tc39.es/ecma262/#sec-destructuring-binding-patterns-runtime-semantics-iteratorbindinginitialization
        //   - `BindingElement: BindingPattern Initializer?`
        // - https://tc39.es/ecma262/#sec-runtime-semantics-keyedbindinginitialization
        //   - `BindingElement: BindingPattern Initializer?`
        Bind(store.DotDotDotToken(node));
        Bind(store.PropertyName(node));
        BindInitializer(store.Initializer(node));
        Bind(store.Name(node));
    }

    private void BindParameterFlow(Node node)
    {
        BindEach(store.Modifiers(node));
        Bind(store.DotDotDotToken(node));
        Bind(store.QuestionToken(node));
        Bind(store.Type(node));
        BindInitializer(store.Initializer(node));
        Bind(store.Name(node));
    }

    // a BindingElement/Parameter does not have side effects if initializers are not evaluated and used. (see GH#49759)
    private void BindInitializer(Node node)
    {
        if (node.IsNone)
        {
            return;
        }
        var entryFlow = currentFlow;
        Bind(node);
        if (entryFlow == unreachableFlow || entryFlow == currentFlow)
        {
            return;
        }
        var exitFlow = CreateBranchLabel();
        AddAntecedent(exitFlow, entryFlow);
        AddAntecedent(exitFlow, currentFlow);
        currentFlow = FinishFlowLabel(exitFlow);
    }

    private void BindJSDocTypeAlias(Node node)
    {
        Bind(store.TagName(node));
        var fullName = store.FullName(node);
        if (store.Kind(node) != SyntaxKind.JSDocEnumTag && !fullName.IsNone)
        {
            // don't bind the type name yet; that's delayed until delayedBindJSDocTypedefTag
            Utilities.SetParent(store, fullName, node);
            Utilities.SetParentRecursive(store, fullName, /*incremental*/ false);
        }
        var comment = store.Comment(node);
        if (!comment.IsText)
        {
            BindEach(comment.List);
        }
    }

    private void BindJSDocImportTag(Node node)
    {
        // don't bind the importClause yet; that's delayed until bindJSDocImports
        Bind(store.TagName(node));
        Bind(store.ModuleSpecifier(node));
        Bind(store.Attributes(node));

        var comment = store.Comment(node);
        if (!comment.IsText)
        {
            BindEach(comment.List);
        }
    }

    private void BindOptionalExpression(Node node, FlowNode trueTarget, FlowNode falseTarget)
    {
        BindWithConditionalBranches(node, trueTarget, falseTarget);
        if (!UtilitiesPublic.IsOptionalChain(store, node) || Utilities.IsOutermostOptionalChain(store, node))
        {
            AddAntecedent(trueTarget, CreateFlowCondition(FlowFlags.TrueCondition, currentFlow, node));
            AddAntecedent(falseTarget, CreateFlowCondition(FlowFlags.FalseCondition, currentFlow, node));
        }
    }

    private void BindOptionalChainRest(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.PropertyAccessExpression:
                Bind(store.QuestionDotToken(node));
                Bind(store.Name(node));
                break;
            case SyntaxKind.ElementAccessExpression:
                Bind(store.QuestionDotToken(node));
                Bind(store.ArgumentExpression(node));
                break;
            case SyntaxKind.CallExpression:
                Bind(store.QuestionDotToken(node));
                BindEach(store.TypeArguments(node));
                BindEach(store.Arguments(node));
                break;
        }
    }

    private void BindOptionalChain(Node node, FlowNode trueTarget, FlowNode falseTarget)
    {
        // For an optional chain, we emulate the behavior of a logical expression:
        //
        // a?.b         -> a && a.b
        // a?.b.c       -> a && a.b.c
        // a?.b?.c      -> a && a.b && a.b.c
        // a?.[x = 1]   -> a && a[x = 1]
        //
        // To do this we descend through the chain until we reach the root of a chain (the expression with a `?.`)
        // and build it's CFA graph as if it were the first condition (`a && ...`). Then we bind the rest
        // of the node as part of the "true" branch, and continue to do so as we ascend back up to the outermost
        // chain node. We then treat the entire node as the right side of the expression.
        var preChainLabel = Utilities.IsOptionalChainRoot(store, node) ? CreateBranchLabel() : FlowNode.None;
        BindOptionalExpression(store.Expression(node), !preChainLabel.IsNone ? preChainLabel : trueTarget, falseTarget);
        if (!preChainLabel.IsNone)
        {
            currentFlow = FinishFlowLabel(preChainLabel);
        }
        // doWithConditionalBranches(bindOptionalChainRest, node, trueTarget, falseTarget)
        var savedTrueTarget = currentTrueTarget;
        var savedFalseTarget = currentFalseTarget;
        currentTrueTarget = trueTarget;
        currentFalseTarget = falseTarget;
        BindOptionalChainRest(node);
        currentTrueTarget = savedTrueTarget;
        currentFalseTarget = savedFalseTarget;
        if (Utilities.IsOutermostOptionalChain(store, node))
        {
            AddAntecedent(trueTarget, CreateFlowCondition(FlowFlags.TrueCondition, currentFlow, node));
            AddAntecedent(falseTarget, CreateFlowCondition(FlowFlags.FalseCondition, currentFlow, node));
        }
    }

    private void BindOptionalChainFlow(Node node)
    {
        if (IsTopLevelLogicalExpression(node))
        {
            var postExpressionLabel = CreateBranchLabel();
            var saveCurrentFlow = currentFlow;
            var saveHasFlowEffects = hasFlowEffects;
            BindOptionalChain(node, postExpressionLabel, postExpressionLabel);
            currentFlow = hasFlowEffects ? FinishFlowLabel(postExpressionLabel) : saveCurrentFlow;
            hasFlowEffects |= saveHasFlowEffects;
        }
        else
        {
            BindOptionalChain(node, currentTrueTarget, currentFalseTarget);
        }
    }

    private void BindNonNullExpressionFlow(Node node)
    {
        if (UtilitiesPublic.IsOptionalChain(store, node))
        {
            BindOptionalChainFlow(node);
        }
        else
        {
            BindEachChild(node);
        }
    }

    private void BindAccessExpressionFlow(Node node)
    {
        if (UtilitiesPublic.IsOptionalChain(store, node))
        {
            BindOptionalChainFlow(node);
        }
        else
        {
            BindEachChild(node);
        }
    }

    private void BindCallExpressionFlow(Node node)
    {
        if (UtilitiesPublic.IsOptionalChain(store, node))
        {
            BindOptionalChainFlow(node);
        }
        else
        {
            // If the target of the call expression is a function expression or arrow function we have
            // an immediately invoked function expression (IIFE). Initialize the flowNode property to
            // the current control flow (which includes evaluation of the IIFE arguments).
            var expr = Utilities.SkipParentheses(store, store.Expression(node));
            if (store.Kind(expr) == SyntaxKind.FunctionExpression || store.Kind(expr) == SyntaxKind.ArrowFunction)
            {
                BindEach(store.TypeArguments(node));
                BindEach(store.Arguments(node));
                Bind(store.Expression(node));
            }
            else
            {
                BindEachChild(node);
                if (store.Kind(store.Expression(node)) == SyntaxKind.SuperKeyword)
                {
                    currentFlow = CreateFlowCall(currentFlow, node);
                }
            }
        }
        var expression = store.Expression(node);
        if (store.Kind(expression) == SyntaxKind.PropertyAccessExpression)
        {
            var name = store.Name(expression);
            if (store.Kind(name) == SyntaxKind.Identifier && IsNarrowableOperand(store.Expression(expression)) && Utilities.IsPushOrUnshiftIdentifier(store, name))
            {
                currentFlow = CreateFlowMutation(FlowFlags.ArrayMutation, currentFlow, node);
            }
        }
    }
}
