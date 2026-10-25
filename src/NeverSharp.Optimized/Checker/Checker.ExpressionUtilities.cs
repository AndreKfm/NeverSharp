// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts and src/compiler/checker.ts (the syntactic helpers of the expression checker:
// getAssignmentTargetKind, isCompoundAssignment, isInCompoundLikeAssignment, skipOuterExpressions,
// walkUpParenthesizedExpressions, isBinaryLogicalOperator, isFunctionExpressionOrArrowFunction, isSuperCall,
// getInvokedExpression, getEffectiveReturnTypeNode, getThisParameter, createDiagnosticForNodeFromMessageChain,
// createDiagnosticForNodeArray)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/utilities.go and internal/checker/utilities.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>utilities.ts AssignmentKind.</summary>
internal enum AssignmentKind
{
    None,
    Definite,
    Compound,
}

/// <summary>checker.ts CheckMode.</summary>
[Flags]
internal enum CheckMode
{
    Normal = 0,                     // Normal type checking
    Contextual = 1 << 0,            // Explicitly assigned contextual type, therefore not cacheable
    Inferential = 1 << 1,           // Inferential typing
    SkipContextSensitive = 1 << 2,  // Skip context sensitive function expressions
    SkipGenericFunctions = 1 << 3,  // Skip single signature generic functions
    IsForSignatureHelp = 1 << 4,    // Call resolution for purposes of signature help
    RestBindingElement = 1 << 5,    // Checking a type that is going to be used to determine the type of a rest binding element
    TypeOnly = 1 << 6,              // Called from getTypeOfExpression, diagnostics may be omitted
}

internal sealed partial class Checker
{
    // utilities.ts getAssignmentTarget
    private static Node GetAssignmentTarget(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        while (true)
        {
            switch (store.Kind(parent))
            {
                case SyntaxKind.BinaryExpression:
                    return Utilities.IsAssignmentOperator(store.Kind(store.OperatorToken(parent))) && store.Left(parent) == node ? parent : Node.None;
                case SyntaxKind.PrefixUnaryExpression:
                case SyntaxKind.PostfixUnaryExpression:
                {
                    var unaryOperator = store.Operator(parent);
                    return unaryOperator is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken ? parent : Node.None;
                }
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                    return store.Initializer(parent) == node ? parent : Node.None;
                case SyntaxKind.ParenthesizedExpression:
                case SyntaxKind.ArrayLiteralExpression:
                case SyntaxKind.SpreadElement:
                case SyntaxKind.NonNullExpression:
                    node = parent;
                    break;
                case SyntaxKind.SpreadAssignment:
                    node = store.Parent(parent);
                    break;
                case SyntaxKind.ShorthandPropertyAssignment:
                    if (store.Name(parent) != node)
                    {
                        return Node.None;
                    }
                    node = store.Parent(parent);
                    break;
                case SyntaxKind.PropertyAssignment:
                    if (store.Name(parent) == node)
                    {
                        return Node.None;
                    }
                    node = store.Parent(parent);
                    break;
                default:
                    return Node.None;
            }
            parent = store.Parent(node);
        }
    }

    /// <summary>utilities.ts getAssignmentTargetKind.</summary>
    internal static AssignmentKind GetAssignmentTargetKind(NodeStore store, Node node)
    {
        var target = GetAssignmentTarget(store, node);
        if (target.IsNone)
        {
            return AssignmentKind.None;
        }
        switch (store.Kind(target))
        {
            case SyntaxKind.BinaryExpression:
            {
                var binaryOperator = store.Kind(store.OperatorToken(target));
                return binaryOperator == SyntaxKind.EqualsToken || Utilities.IsLogicalOrCoalescingAssignmentOperator(binaryOperator)
                    ? AssignmentKind.Definite
                    : AssignmentKind.Compound;
            }
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
                return AssignmentKind.Compound;
            default:
                return AssignmentKind.Definite;
        }
    }

    private AssignmentKind GetAssignmentTargetKind(NodeRef node) => GetAssignmentTargetKind(StoreOf(node), node.Node);

    /// <summary>utilities.ts isCompoundAssignment.</summary>
    internal static bool IsCompoundAssignment(SyntaxKind token) =>
        token >= SyntaxKind.FirstCompoundAssignment && token <= SyntaxKind.LastCompoundAssignment;

    // utilities.ts isInCompoundLikeAssignment
    private static bool IsInCompoundLikeAssignment(NodeStore store, Node node)
    {
        var target = GetAssignmentTarget(store, node);
        return !target.IsNone && store.Kind(target) == SyntaxKind.BinaryExpression && Utilities.IsAssignmentExpression(store, target, excludeCompoundAssignment: true) &&
            IsCompoundLikeAssignment(store, target);
    }

    // utilities.ts isCompoundLikeAssignment
    private static bool IsCompoundLikeAssignment(NodeStore store, Node assignment)
    {
        var right = Utilities.SkipParentheses(store, store.Right(assignment));
        return store.Kind(right) == SyntaxKind.BinaryExpression && IsShiftOperatorOrHigher(store.Kind(store.OperatorToken(right)));
    }

    // utilities.ts isShiftOperatorOrHigher
    private static bool IsShiftOperatorOrHigher(SyntaxKind kind) =>
        kind is SyntaxKind.LessThanLessThanToken or SyntaxKind.GreaterThanGreaterThanToken or SyntaxKind.GreaterThanGreaterThanGreaterThanToken
            or SyntaxKind.AsteriskAsteriskToken or SyntaxKind.AsteriskToken or SyntaxKind.SlashToken or SyntaxKind.PercentToken or SyntaxKind.PlusToken or SyntaxKind.MinusToken;

    /// <summary>utilities.ts isOuterExpression.</summary>
    internal static bool IsOuterExpression(NodeStore store, Node node, OuterExpressionKinds kinds = OuterExpressionKinds.All)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ParenthesizedExpression:
                // (JSDoc type assertions: C9)
                return (kinds & OuterExpressionKinds.Parentheses) != 0;
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.AsExpression:
                return (kinds & OuterExpressionKinds.TypeAssertions) != 0;
            case SyntaxKind.SatisfiesExpression:
                return (kinds & (OuterExpressionKinds.TypeAssertions | OuterExpressionKinds.Satisfies)) != 0;
            case SyntaxKind.ExpressionWithTypeArguments:
                return (kinds & OuterExpressionKinds.ExpressionsWithTypeArguments) != 0;
            case SyntaxKind.NonNullExpression:
                return (kinds & OuterExpressionKinds.NonNullAssertions) != 0;
            case SyntaxKind.PartiallyEmittedExpression:
                return (kinds & OuterExpressionKinds.PartiallyEmittedExpressions) != 0;
        }
        return false;
    }

    /// <summary>utilities.ts skipOuterExpressions.</summary>
    internal static Node SkipOuterExpressions(NodeStore store, Node node, OuterExpressionKinds kinds = OuterExpressionKinds.All)
    {
        while (IsOuterExpression(store, node, kinds))
        {
            node = store.Expression(node);
        }
        return node;
    }

    /// <summary>utilities.ts walkUpParenthesizedExpressions.</summary>
    internal static Node WalkUpParenthesizedExpressions(NodeStore store, Node node)
    {
        while (store.Kind(node) == SyntaxKind.ParenthesizedExpression)
        {
            node = store.Parent(node);
        }
        return node;
    }

    /// <summary>utilities.ts isBinaryLogicalOperator.</summary>
    internal static bool IsBinaryLogicalOperator(SyntaxKind token) => token is SyntaxKind.BarBarToken or SyntaxKind.AmpersandAmpersandToken;

    /// <summary>utilities.ts isFunctionExpressionOrArrowFunction.</summary>
    internal static bool IsFunctionExpressionOrArrowFunction(NodeStore store, Node node) =>
        store.Kind(node) is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction;

    /// <summary>utilities.ts isSuperCall.</summary>
    internal static bool IsSuperCall(NodeStore store, Node node) =>
        store.Kind(node) == SyntaxKind.CallExpression && store.Kind(store.Expression(node)) == SyntaxKind.SuperKeyword;

    /// <summary>utilities.ts getInvokedExpression.</summary>
    internal static Node GetInvokedExpression(NodeStore store, Node node) => store.Kind(node) switch
    {
        SyntaxKind.TaggedTemplateExpression => store.Tag(node),
        SyntaxKind.JsxOpeningElement or SyntaxKind.JsxSelfClosingElement => store.TagName(node),
        SyntaxKind.BinaryExpression => store.Right(node),
        SyntaxKind.JsxOpeningFragment => node,
        _ => store.Expression(node),
    };

    /// <summary>utilities.ts getEffectiveReturnTypeNode (JSDoc return types: C9).</summary>
    internal static Node GetEffectiveReturnTypeNode(NodeStore store, Node node) => store.Type(node);

    /// <summary>utilities.ts getThisParameter.</summary>
    internal static Node GetThisParameter(NodeStore store, Node signature)
    {
        // callback tags do not currently support this parameters
        var parameters = store.Parameters(signature);
        if (!parameters.IsNone && store.Length(parameters) > 0)
        {
            var thisParameter = store.Element(parameters, 0);
            if (ParameterIsThisKeyword(store, thisParameter))
            {
                return thisParameter;
            }
        }
        return Node.None;
    }

    /// <summary>utilities.ts getTextOfNode.</summary>
    internal string GetTextOfNode(NodeRef node)
    {
        var store = StoreOf(node);
        return Utilities.GetSourceTextOfNodeFromSourceFile(store, Utilities.RootOf(store, node.Node), node.Node);
    }

    /// <summary>utilities.ts createDiagnosticForNodeFromMessageChain.</summary>
    internal ProgramDiagnostic CreateDiagnosticForNodeFromMessageChain(NodeRef node, DiagnosticMessageChain chain) => CreateDiagnosticForNodeFromMessageChain(node, chain, null);

    /// <summary>utilities.ts createFileDiagnosticFromMessageChain.</summary>
    internal ProgramDiagnostic CreateFileDiagnosticFromMessageChain(int fileNumber, int start, int length, DiagnosticMessageChain chain, List<DiagnosticRelatedInformation>? relatedInformation = null)
    {
        var file = FileAt(fileNumber);
        return new ProgramDiagnostic
        {
            SourceFile = file,
            File = file.Root,
            StartOrUndefined = start,
            LengthOrUndefined = length,
            Code = chain.Code,
            Category = chain.Category,
            MessageText = chain.MessageText,
            Chain = chain.Next is null ? null : chain,
            RelatedInformation = relatedInformation,
        };
    }

    /// <summary>utilities.ts createFileDiagnostic (with the checker's program file).</summary>
    internal ProgramDiagnostic CreateFileDiagnostic(int fileNumber, int start, int length, DiagnosticMessage message, params object?[] args)
    {
        var file = FileAt(fileNumber);
        return ProgramDiagnostic.FromFile(file, Utilities.CreateFileDiagnostic(file.Store, file.Root, start, length, message, args));
    }

    /// <summary>utilities.ts createDiagnosticForNodeArray: the span of the list (its first element's start to its end).</summary>
    internal ProgramDiagnostic CreateDiagnosticForNodeArray(NodeRef context, NodeList list, DiagnosticMessage message, params object?[] args)
    {
        var store = StoreOf(context);
        var start = Scanner.SkipTrivia(store.SourceFileText(Utilities.RootOf(store, context.Node)).Chars, store.Pos(list));
        return CreateFileDiagnostic(context.File, start, store.End(list) - start, message, args);
    }

    /// <summary>utilities.ts createDiagnosticForNodeArrayFromMessageChain.</summary>
    internal ProgramDiagnostic CreateDiagnosticForNodeArrayFromMessageChain(NodeRef context, NodeList list, DiagnosticMessageChain chain)
    {
        var store = StoreOf(context);
        var start = Scanner.SkipTrivia(store.SourceFileText(Utilities.RootOf(store, context.Node)).Chars, store.Pos(list));
        return CreateFileDiagnosticFromMessageChain(context.File, start, store.End(list) - start, chain);
    }

    /// <summary>checker.ts addRelatedInfo.</summary>
    internal static ProgramDiagnostic AddRelatedInfo(ProgramDiagnostic diagnostic, params ProgramRelatedInformation[] relatedInformation)
    {
        if (relatedInformation.Length == 0)
        {
            return diagnostic;
        }
        diagnostic.RelatedInformation ??= [];
        foreach (var info in relatedInformation)
        {
            diagnostic.RelatedInformation.Add(info);
        }
        return diagnostic;
    }

    /// <summary>A diagnostic as related information (createDiagnosticForNode used in addRelatedInfo).</summary>
    internal static ProgramRelatedInformation AsRelatedInformation(ProgramDiagnostic d) => new()
    {
        SourceFile = d.SourceFile,
        File = d.File,
        StartOrUndefined = d.StartOrUndefined,
        LengthOrUndefined = d.LengthOrUndefined,
        Code = d.Code,
        Category = d.Category,
        MessageText = d.MessageText,
        Chain = d.Chain,
    };

    /// <summary>scanner.ts tokenToString.</summary>
    internal static string TokenText(SyntaxKind kind) => Scanner.TokenToString(kind) ?? "";
}
