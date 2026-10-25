// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (the expression parts of the statement and declaration checks:
// checkExpressionStatement, checkIfStatement, checkDoStatement, checkWhileStatement, checkForStatement,
// checkForOfStatement, checkForInStatement, checkReturnStatement, checkVariableLikeDeclaration's initializers,
// checkDecorator)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5. The resolution walk of Checker.Check.cs stops at the outermost
// expressions ("expression roots") and checks each with the checkExpression variant its statement or declaration uses.
// TODO(C8): the statement checks themselves (assignability of initializers and returns, the for-in checks, grammar).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // the expression kinds the walk checks with checkExpression
    private static bool IsExpressionRootKind(SyntaxKind kind) => kind switch
    {
        SyntaxKind.Identifier or SyntaxKind.ThisKeyword or SyntaxKind.SuperKeyword or SyntaxKind.NullKeyword or SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword
            or SyntaxKind.NumericLiteral or SyntaxKind.BigIntLiteral or SyntaxKind.StringLiteral or SyntaxKind.RegularExpressionLiteral
            or SyntaxKind.NoSubstitutionTemplateLiteral or SyntaxKind.TemplateExpression
            or SyntaxKind.ArrayLiteralExpression or SyntaxKind.ObjectLiteralExpression or SyntaxKind.PropertyAccessExpression
            or SyntaxKind.ElementAccessExpression or SyntaxKind.CallExpression or SyntaxKind.NewExpression or SyntaxKind.TaggedTemplateExpression
            or SyntaxKind.TypeAssertionExpression or SyntaxKind.ParenthesizedExpression or SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction
            or SyntaxKind.DeleteExpression or SyntaxKind.TypeOfExpression or SyntaxKind.VoidExpression or SyntaxKind.AwaitExpression
            or SyntaxKind.PrefixUnaryExpression or SyntaxKind.PostfixUnaryExpression or SyntaxKind.BinaryExpression or SyntaxKind.ConditionalExpression
            or SyntaxKind.YieldExpression or SyntaxKind.ClassExpression or SyntaxKind.AsExpression or SyntaxKind.NonNullExpression
            or SyntaxKind.MetaProperty or SyntaxKind.SatisfiesExpression or SyntaxKind.JsxElement or SyntaxKind.JsxSelfClosingElement
            or SyntaxKind.JsxFragment => true,
        _ => false,
    };

    /// <summary>
    /// Checks an outermost expression of a statement or declaration the way the statement or declaration check of
    /// checker.ts does.
    /// </summary>
    private void CheckExpressionRoot(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var parent = store.Parent(n);
        switch (store.Kind(parent))
        {
            case SyntaxKind.IfStatement when store.Expression(parent) == n:
            {
                // checkIfStatement
                var type = CheckTruthinessExpression(node);
                CheckTestingKnownTruthyCallableOrAwaitableOrEnumMemberType(node, type, At(node, store.ThenStatement(parent)));
                return;
            }
            case SyntaxKind.DoStatement when store.Expression(parent) == n:
            case SyntaxKind.WhileStatement when store.Expression(parent) == n:
            case SyntaxKind.ForStatement when store.Condition(parent) == n:
                CheckTruthinessExpression(node);
                return;
            case SyntaxKind.VariableDeclaration when store.Initializer(parent) == n:
            case SyntaxKind.Parameter when store.Initializer(parent) == n:
            case SyntaxKind.PropertyDeclaration when store.Initializer(parent) == n:
            case SyntaxKind.BindingElement when store.Initializer(parent) == n:
            case SyntaxKind.ReturnStatement:
                CheckExpressionCached(node);
                return;
            case SyntaxKind.ForOfStatement:
                if (store.Initializer(parent) == n)
                {
                    return; // checked with the expression (checkForOfStatement)
                }
                CheckForOfStatementExpressions(At(node, parent));
                return;
            case SyntaxKind.ForInStatement:
            {
                if (store.Initializer(parent) == n)
                {
                    return; // checked with the expression (checkForInStatement)
                }
                // checkForInStatement (TODO(C8): its diagnostics)
                CheckExpression(node);
                var initializer = store.Initializer(parent);
                if (store.Kind(initializer) != SyntaxKind.VariableDeclarationList)
                {
                    CheckExpression(At(node, initializer));
                }
                return;
            }
        }
        CheckExpression(node);
    }

    // checkForOfStatement, the left and right hand sides
    private void CheckForOfStatementExpressions(NodeRef statement)
    {
        var store = StoreOf(statement);
        var initializer = store.Initializer(statement.Node);
        if (store.Kind(initializer) == SyntaxKind.VariableDeclarationList)
        {
            // If the LHS is a declaration, just check it as a variable declaration, which will in turn check the RHS
            // via checkRightHandSideOfForOf.
            if (store.Length(store.Declarations(initializer)) == 0)
            {
                CheckRightHandSideOfForOf(statement);
            }
            return;
        }
        var varExpr = At(statement, initializer);
        var iteratedType = CheckRightHandSideOfForOf(statement);
        // There may be a destructuring assignment on the left side
        if (store.Kind(initializer) is SyntaxKind.ArrayLiteralExpression or SyntaxKind.ObjectLiteralExpression)
        {
            // iteratedType may be undefined. In this case, we still want to check the structure of
            // varExpr, in particular making sure it's a valid LeftHandSideExpression. But we'd like
            // to short circuit the type relation checking as much as possible, so we pass the unknownType.
            CheckDestructuringAssignment(varExpr, iteratedType.IsNone ? errorType : iteratedType);
            return;
        }
        var leftType = CheckExpression(varExpr);
        CheckReferenceExpression(varExpr,
            Diagnostics.The_left_hand_side_of_a_for_of_statement_must_be_a_variable_or_a_property_access,
            Diagnostics.The_left_hand_side_of_a_for_of_statement_may_not_be_an_optional_property_access);
        // iteratedType will be undefined if the rightType was missing properties/signatures
        // required to get its iteratedType (like [Symbol.iterator] or next). This may be
        // because we accessed properties from anyType, or it may have led to an error inside
        // getElementTypeOfIterable.
        var expression = At(statement, store.Expression(statement.Node));
        if (!iteratedType.IsNone && !SuppressDerivedDiagnostics(varExpr, leftType) && !SuppressDerivedDiagnostics(expression, iteratedType))
        {
            CheckTypeAssignableToAndOptionallyElaborate(iteratedType, leftType, varExpr, expression);
        }
    }

    /// <summary>checker.ts checkDecorator (TODO(C9): the decorator call signatures and the return type check).</summary>
    private void CheckDecorator(NodeRef node)
    {
        // resolveDecorator: checkExpression(node.expression)
        CheckExpression(At(node, StoreOf(node).Expression(node.Node)));
    }
}
