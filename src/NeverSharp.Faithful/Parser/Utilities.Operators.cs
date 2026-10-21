// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/precedence.go and internal/ast/utilities.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>utilities.ts: `const enum Associativity`.</summary>
internal enum Associativity
{
    Left,
    Right,
}

#pragma warning disable CA1069 // aliases with the same value (Coalesce, Highest, Lowest) are part of TypeScript's enum
/// <summary>utilities.ts: `const enum OperatorPrecedence` (the comments of the TypeScript source are kept short).</summary>
internal enum OperatorPrecedence
{
    // Expression:
    //     AssignmentExpression
    //     Expression `,` AssignmentExpression
    Comma,

    // NOTE: `Spread` is higher than `Comma` due to how it is parsed in |ElementList|
    // SpreadElement:
    //     `...` AssignmentExpression
    Spread,

    // AssignmentExpression: YieldExpression
    Yield,

    // AssignmentExpression: LeftHandSideExpression `=` AssignmentExpression
    // AssignmentExpression: LeftHandSideExpression AssignmentOperator AssignmentExpression
    Assignment,

    // NOTE: `Conditional` is considered higher than `Assignment` here, but in reality they have
    //       the same precedence.
    // AssignmentExpression: ConditionalExpression
    Conditional,

    // LogicalORExpression:
    //     LogicalANDExpression
    //     LogicalORExpression `||` LogicalANDExpression
    LogicalOR,

    // CoalesceExpression:
    //     CoalesceExpressionHead `??` BitwiseORExpression
    Coalesce = LogicalOR,

    // LogicalANDExpression:
    //     BitwiseORExpression
    //     LogicalANDExprerssion `&&` BitwiseORExpression
    LogicalAND,

    // BitwiseORExpression:
    //     BitwiseXORExpression
    //     BitwiseORExpression `^` BitwiseXORExpression
    BitwiseOR,

    // BitwiseXORExpression:
    //     BitwiseANDExpression
    //     BitwiseXORExpression `^` BitwiseANDExpression
    BitwiseXOR,

    // BitwiseANDExpression:
    //     EqualityExpression
    //     BitwiseANDExpression `^` EqualityExpression
    BitwiseAND,

    // EqualityExpression:
    //     RelationalExpression
    //     EqualityExpression `==` RelationalExpression
    //     EqualityExpression `!=` RelationalExpression
    //     EqualityExpression `===` RelationalExpression
    //     EqualityExpression `!==` RelationalExpression
    Equality,

    // RelationalExpression:
    //     ShiftExpression
    //     RelationalExpression `<` ShiftExpression
    //     RelationalExpression `>` ShiftExpression
    //     RelationalExpression `<=` ShiftExpression
    //     RelationalExpression `>=` ShiftExpression
    //     RelationalExpression `instanceof` ShiftExpression
    //     RelationalExpression `in` ShiftExpression
    //     [+TypeScript] RelationalExpression `as` Type
    Relational,

    // ShiftExpression:
    //     AdditiveExpression
    //     ShiftExpression `<<` AdditiveExpression
    //     ShiftExpression `>>` AdditiveExpression
    //     ShiftExpression `>>>` AdditiveExpression
    Shift,

    // AdditiveExpression:
    //     MultiplicativeExpression
    //     AdditiveExpression `+` MultiplicativeExpression
    //     AdditiveExpression `-` MultiplicativeExpression
    Additive,

    // MultiplicativeExpression:
    //     ExponentiationExpression
    //     MultiplicativeExpression MultiplicativeOperator ExponentiationExpression
    // MultiplicativeOperator: one of `*`, `/`, `%`
    Multiplicative,

    // ExponentiationExpression:
    //     UnaryExpression
    //     UpdateExpression `**` ExponentiationExpression
    Exponentiation,

    // UnaryExpression:
    //     UpdateExpression
    //     `delete` UnaryExpression
    //     `void` UnaryExpression
    //     `typeof` UnaryExpression
    //     `+` UnaryExpression
    //     `-` UnaryExpression
    //     `~` UnaryExpression
    //     `!` UnaryExpression
    //     AwaitExpression
    // UpdateExpression:            // TODO: Do we need to investigate the precedence here?
    //     `++` UnaryExpression
    //     `--` UnaryExpression
    Unary,

    // UpdateExpression:
    //     LeftHandSideExpression
    //     LeftHandSideExpression `++`
    //     LeftHandSideExpression `--`
    Update,

    // LeftHandSideExpression:
    //     NewExpression
    //     CallExpression
    // NewExpression:
    //     MemberExpression
    //     `new` NewExpression
    LeftHandSide,

    // CallExpression:
    //     CoverCallExpressionAndAsyncArrowHead
    //     SuperCall
    //     ImportCall
    //     CallExpression Arguments
    //     CallExpression `[` Expression `]`
    //     CallExpression `.` IdentifierName
    //     CallExpression TemplateLiteral
    // MemberExpression:
    //     PrimaryExpression
    //     MemberExpression `[` Expression `]`
    //     MemberExpression `.` IdentifierName
    //     MemberExpression TemplateLiteral
    //     SuperProperty
    //     MetaProperty
    //     `new` MemberExpression Arguments
    Member,

    // TODO: JSXElement?
    // PrimaryExpression:
    //     `this`
    //     IdentifierReference
    //     Literal
    //     ArrayLiteral
    //     ObjectLiteral
    //     FunctionExpression
    //     ClassExpression
    //     GeneratorExpression
    //     AsyncFunctionExpression
    //     AsyncGeneratorExpression
    //     RegularExpressionLiteral
    //     TemplateLiteral
    //     CoverParenthesizedExpressionAndArrowParameterList
    Primary,

    Highest = Primary,
    Lowest = Comma,

    // -1 is lower than all other precedences. Returning it will cause binary expression
    // parsing to stop.
    Invalid = -1,
}
#pragma warning restore CA1069

/// <summary>utilities.ts (the units the parser reaches): operator precedence, modifier flags, assignment operators.</summary>
internal static partial class Utilities
{
    internal static Associativity GetExpressionAssociativity(Node expression)
    {
        var @operator = GetOperator(expression);
        var hasArguments = expression.Kind == SyntaxKind.NewExpression && ((NewExpression)expression).Arguments != null;
        return GetOperatorAssociativity(expression.Kind, @operator, hasArguments);
    }

    internal static Associativity GetOperatorAssociativity(SyntaxKind kind, SyntaxKind @operator, bool hasArguments = false)
    {
        switch (kind)
        {
            case SyntaxKind.NewExpression:
                return hasArguments ? Associativity.Left : Associativity.Right;

            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.TypeOfExpression:
            case SyntaxKind.VoidExpression:
            case SyntaxKind.DeleteExpression:
            case SyntaxKind.AwaitExpression:
            case SyntaxKind.ConditionalExpression:
            case SyntaxKind.YieldExpression:
                return Associativity.Right;

            case SyntaxKind.BinaryExpression:
                switch (@operator)
                {
                    case SyntaxKind.AsteriskAsteriskToken:
                    case SyntaxKind.EqualsToken:
                    case SyntaxKind.PlusEqualsToken:
                    case SyntaxKind.MinusEqualsToken:
                    case SyntaxKind.AsteriskAsteriskEqualsToken:
                    case SyntaxKind.AsteriskEqualsToken:
                    case SyntaxKind.SlashEqualsToken:
                    case SyntaxKind.PercentEqualsToken:
                    case SyntaxKind.LessThanLessThanEqualsToken:
                    case SyntaxKind.GreaterThanGreaterThanEqualsToken:
                    case SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken:
                    case SyntaxKind.AmpersandEqualsToken:
                    case SyntaxKind.CaretEqualsToken:
                    case SyntaxKind.BarEqualsToken:
                    case SyntaxKind.BarBarEqualsToken:
                    case SyntaxKind.AmpersandAmpersandEqualsToken:
                    case SyntaxKind.QuestionQuestionEqualsToken:
                        return Associativity.Right;
                }
                break;
        }
        return Associativity.Left;
    }

    internal static OperatorPrecedence GetExpressionPrecedence(Node expression)
    {
        var @operator = GetOperator(expression);
        var hasArguments = expression.Kind == SyntaxKind.NewExpression && ((NewExpression)expression).Arguments != null;
        return GetOperatorPrecedence(expression.Kind, @operator, hasArguments);
    }

    private static SyntaxKind GetOperator(Node expression)
    {
        if (expression.Kind == SyntaxKind.BinaryExpression)
        {
            return ((BinaryExpression)expression).OperatorToken.Kind;
        }
        else if (expression.Kind == SyntaxKind.PrefixUnaryExpression)
        {
            return ((PrefixUnaryExpression)expression).Operator;
        }
        else if (expression.Kind == SyntaxKind.PostfixUnaryExpression)
        {
            return ((PostfixUnaryExpression)expression).Operator;
        }
        else
        {
            return expression.Kind;
        }
    }

    internal static OperatorPrecedence GetOperatorPrecedence(SyntaxKind nodeKind, SyntaxKind operatorKind, bool hasArguments = false)
    {
        switch (nodeKind)
        {
            case SyntaxKind.CommaListExpression:
                return OperatorPrecedence.Comma;

            case SyntaxKind.SpreadElement:
                return OperatorPrecedence.Spread;

            case SyntaxKind.YieldExpression:
                return OperatorPrecedence.Yield;

            case SyntaxKind.ConditionalExpression:
                return OperatorPrecedence.Conditional;

            case SyntaxKind.BinaryExpression:
                switch (operatorKind)
                {
                    case SyntaxKind.CommaToken:
                        return OperatorPrecedence.Comma;

                    case SyntaxKind.EqualsToken:
                    case SyntaxKind.PlusEqualsToken:
                    case SyntaxKind.MinusEqualsToken:
                    case SyntaxKind.AsteriskAsteriskEqualsToken:
                    case SyntaxKind.AsteriskEqualsToken:
                    case SyntaxKind.SlashEqualsToken:
                    case SyntaxKind.PercentEqualsToken:
                    case SyntaxKind.LessThanLessThanEqualsToken:
                    case SyntaxKind.GreaterThanGreaterThanEqualsToken:
                    case SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken:
                    case SyntaxKind.AmpersandEqualsToken:
                    case SyntaxKind.CaretEqualsToken:
                    case SyntaxKind.BarEqualsToken:
                    case SyntaxKind.BarBarEqualsToken:
                    case SyntaxKind.AmpersandAmpersandEqualsToken:
                    case SyntaxKind.QuestionQuestionEqualsToken:
                        return OperatorPrecedence.Assignment;

                    default:
                        return GetBinaryOperatorPrecedence(operatorKind);
                }

            // TODO: Should prefix `++` and `--` be moved to the `Update` precedence?
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.NonNullExpression:
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.TypeOfExpression:
            case SyntaxKind.VoidExpression:
            case SyntaxKind.DeleteExpression:
            case SyntaxKind.AwaitExpression:
                return OperatorPrecedence.Unary;

            case SyntaxKind.PostfixUnaryExpression:
                return OperatorPrecedence.Update;

            case SyntaxKind.CallExpression:
                return OperatorPrecedence.LeftHandSide;

            case SyntaxKind.NewExpression:
                return hasArguments ? OperatorPrecedence.Member : OperatorPrecedence.LeftHandSide;

            case SyntaxKind.TaggedTemplateExpression:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.MetaProperty:
                return OperatorPrecedence.Member;

            case SyntaxKind.AsExpression:
            case SyntaxKind.SatisfiesExpression:
                return OperatorPrecedence.Relational;

            case SyntaxKind.ThisKeyword:
            case SyntaxKind.SuperKeyword:
            case SyntaxKind.Identifier:
            case SyntaxKind.PrivateIdentifier:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.RegularExpressionLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateExpression:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.OmittedExpression:
            case SyntaxKind.JsxElement:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxFragment:
                return OperatorPrecedence.Primary;

            default:
                return OperatorPrecedence.Invalid;
        }
    }

    internal static OperatorPrecedence GetBinaryOperatorPrecedence(SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.QuestionQuestionToken:
                return OperatorPrecedence.Coalesce;
            case SyntaxKind.BarBarToken:
                return OperatorPrecedence.LogicalOR;
            case SyntaxKind.AmpersandAmpersandToken:
                return OperatorPrecedence.LogicalAND;
            case SyntaxKind.BarToken:
                return OperatorPrecedence.BitwiseOR;
            case SyntaxKind.CaretToken:
                return OperatorPrecedence.BitwiseXOR;
            case SyntaxKind.AmpersandToken:
                return OperatorPrecedence.BitwiseAND;
            case SyntaxKind.EqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsToken:
            case SyntaxKind.EqualsEqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsEqualsToken:
                return OperatorPrecedence.Equality;
            case SyntaxKind.LessThanToken:
            case SyntaxKind.GreaterThanToken:
            case SyntaxKind.LessThanEqualsToken:
            case SyntaxKind.GreaterThanEqualsToken:
            case SyntaxKind.InstanceOfKeyword:
            case SyntaxKind.InKeyword:
            case SyntaxKind.AsKeyword:
            case SyntaxKind.SatisfiesKeyword:
                return OperatorPrecedence.Relational;
            case SyntaxKind.LessThanLessThanToken:
            case SyntaxKind.GreaterThanGreaterThanToken:
            case SyntaxKind.GreaterThanGreaterThanGreaterThanToken:
                return OperatorPrecedence.Shift;
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
                return OperatorPrecedence.Additive;
            case SyntaxKind.AsteriskToken:
            case SyntaxKind.SlashToken:
            case SyntaxKind.PercentToken:
                return OperatorPrecedence.Multiplicative;
            case SyntaxKind.AsteriskAsteriskToken:
                return OperatorPrecedence.Exponentiation;
        }

        // -1 is lower than all other precedences.  Returning it will cause binary expression
        // parsing to stop.
        return OperatorPrecedence.Invalid;
    }

    internal static bool HasSyntacticModifier(Node node, ModifierFlags flags)
    {
        return GetSelectedSyntacticModifierFlags(node, flags) != 0;
    }

    internal static ModifierFlags GetSelectedSyntacticModifierFlags(Node node, ModifierFlags flags)
    {
        return GetSyntacticModifierFlags(node) & flags;
    }

    private static ModifierFlags GetModifierFlagsWorker(Node node, bool includeJSDoc, bool alwaysIncludeJSDoc = false)
    {
        if (node.Kind >= SyntaxKind.FirstToken && node.Kind <= SyntaxKind.LastToken)
        {
            return ModifierFlags.None;
        }

        if ((node.ModifierFlagsCache & ModifierFlags.HasComputedFlags) == 0)
        {
            node.ModifierFlagsCache = GetSyntacticModifierFlagsNoCache(node) | ModifierFlags.HasComputedFlags;
        }

        if (alwaysIncludeJSDoc || (includeJSDoc && IsInJSFile(node)))
        {
            if ((node.ModifierFlagsCache & ModifierFlags.HasComputedJSDocModifiers) == 0 && node.Parent != null)
            {
                node.ModifierFlagsCache |= GetRawJSDocModifierFlagsNoCache(node) | ModifierFlags.HasComputedJSDocModifiers;
            }
            return SelectEffectiveModifierFlags(node.ModifierFlagsCache);
        }

        return SelectSyntacticModifierFlags(node.ModifierFlagsCache);
    }

    /// <summary>
    /// Gets the ModifierFlags for syntactic modifiers on the provided node. The modifiers will be cached on the node to improve performance.
    ///
    /// NOTE: This function does not use `parent` pointers and will not include modifiers from JSDoc.
    /// </summary>
    internal static ModifierFlags GetSyntacticModifierFlags(Node node)
    {
        return GetModifierFlagsWorker(node, /*includeJSDoc*/ false);
    }

    private static ModifierFlags GetRawJSDocModifierFlagsNoCache(Node node)
    {
        var flags = ModifierFlags.None;
        if (node.Parent != null && !NodeTests.IsParameter(node))
        {
            if (IsInJSFile(node))
            {
                if (UtilitiesPublic.GetJSDocPublicTagNoCache(node) != null)
                {
                    flags |= ModifierFlags.JSDocPublic;
                }
                if (UtilitiesPublic.GetJSDocPrivateTagNoCache(node) != null)
                {
                    flags |= ModifierFlags.JSDocPrivate;
                }
                if (UtilitiesPublic.GetJSDocProtectedTagNoCache(node) != null)
                {
                    flags |= ModifierFlags.JSDocProtected;
                }
                if (UtilitiesPublic.GetJSDocReadonlyTagNoCache(node) != null)
                {
                    flags |= ModifierFlags.JSDocReadonly;
                }
                if (UtilitiesPublic.GetJSDocOverrideTagNoCache(node) != null)
                {
                    flags |= ModifierFlags.JSDocOverride;
                }
            }
            if (UtilitiesPublic.GetJSDocDeprecatedTagNoCache(node) != null)
            {
                flags |= ModifierFlags.Deprecated;
            }
        }

        return flags;
    }

    private static ModifierFlags SelectSyntacticModifierFlags(ModifierFlags flags)
    {
        return flags & ModifierFlags.SyntacticModifiers;
    }

    private static ModifierFlags SelectEffectiveModifierFlags(ModifierFlags flags)
    {
        return (flags & ModifierFlags.NonCacheOnlyModifiers) |
            (ModifierFlags)(int)((uint)(int)(flags & ModifierFlags.JSDocCacheOnlyModifiers) >> 23); // shift ModifierFlags.JSDoc* to match ModifierFlags.*
    }

    /// <summary>`node.modifiers` of a node for which canHaveModifiers holds (TypeScript's HasModifiers).</summary>
    private static NodeArray? GetModifiersOfHasModifiers(Node node)
    {
        switch (node)
        {
            case TypeParameterDeclaration typeParameter:
                return typeParameter.Modifiers;
            case ParameterDeclaration parameter:
                return parameter.Modifiers;
            case PropertySignature propertySignature:
                return propertySignature.Modifiers;
            case PropertyDeclaration propertyDeclaration:
                return propertyDeclaration.Modifiers;
            case MethodSignature methodSignature:
                return methodSignature.Modifiers;
            case MethodDeclaration methodDeclaration:
                return methodDeclaration.Modifiers;
            case ConstructorDeclaration constructorDeclaration:
                return constructorDeclaration.Modifiers;
            case GetAccessorDeclaration getAccessorDeclaration:
                return getAccessorDeclaration.Modifiers;
            case SetAccessorDeclaration setAccessorDeclaration:
                return setAccessorDeclaration.Modifiers;
            case IndexSignatureDeclaration indexSignatureDeclaration:
                return indexSignatureDeclaration.Modifiers;
            case ConstructorTypeNode constructorTypeNode:
                return constructorTypeNode.Modifiers;
            case FunctionExpression functionExpression:
                return functionExpression.Modifiers;
            case ArrowFunction arrowFunction:
                return arrowFunction.Modifiers;
            case ClassExpression classExpression:
                return classExpression.Modifiers;
            case VariableStatement variableStatement:
                return variableStatement.Modifiers;
            case FunctionDeclaration functionDeclaration:
                return functionDeclaration.Modifiers;
            case ClassDeclaration classDeclaration:
                return classDeclaration.Modifiers;
            case InterfaceDeclaration interfaceDeclaration:
                return interfaceDeclaration.Modifiers;
            case TypeAliasDeclaration typeAliasDeclaration:
                return typeAliasDeclaration.Modifiers;
            case EnumDeclaration enumDeclaration:
                return enumDeclaration.Modifiers;
            case ModuleDeclaration moduleDeclaration:
                return moduleDeclaration.Modifiers;
            case ImportEqualsDeclaration importEqualsDeclaration:
                return importEqualsDeclaration.Modifiers;
            case ImportDeclaration importDeclaration:
                return importDeclaration.Modifiers;
            case ExportAssignment exportAssignment:
                return exportAssignment.Modifiers;
            case ExportDeclaration exportDeclaration:
                return exportDeclaration.Modifiers;
            default:
                return null;
        }
    }

    /// <summary>
    /// Gets the ModifierFlags for syntactic modifiers on the provided node. The modifier flags cache on the node is ignored.
    ///
    /// NOTE: This function does not use `parent` pointers and will not include modifiers from JSDoc.
    /// </summary>
    internal static ModifierFlags GetSyntacticModifierFlagsNoCache(Node node)
    {
        var flags = FactoryUtilitiesPublic.CanHaveModifiers(node) ? ModifiersToFlags(GetModifiersOfHasModifiers(node)) : ModifierFlags.None;
        if ((node.Flags & NodeFlags.NestedNamespace) != 0 || (node.Kind == SyntaxKind.Identifier && (node.Flags & NodeFlags.IdentifierIsInJSDocNamespace) != 0))
        {
            flags |= ModifierFlags.Export;
        }
        return flags;
    }

    internal static ModifierFlags ModifiersToFlags(IReadOnlyList<Node>? modifiers)
    {
        var flags = ModifierFlags.None;
        if (modifiers != null)
        {
            for (var i = 0; i < modifiers.Count; i++)
            {
                flags |= ModifierToFlag(modifiers[i].Kind);
            }
        }
        return flags;
    }

    internal static ModifierFlags ModifierToFlag(SyntaxKind token)
    {
        switch (token)
        {
            case SyntaxKind.StaticKeyword:
                return ModifierFlags.Static;
            case SyntaxKind.PublicKeyword:
                return ModifierFlags.Public;
            case SyntaxKind.ProtectedKeyword:
                return ModifierFlags.Protected;
            case SyntaxKind.PrivateKeyword:
                return ModifierFlags.Private;
            case SyntaxKind.AbstractKeyword:
                return ModifierFlags.Abstract;
            case SyntaxKind.AccessorKeyword:
                return ModifierFlags.Accessor;
            case SyntaxKind.ExportKeyword:
                return ModifierFlags.Export;
            case SyntaxKind.DeclareKeyword:
                return ModifierFlags.Ambient;
            case SyntaxKind.ConstKeyword:
                return ModifierFlags.Const;
            case SyntaxKind.DefaultKeyword:
                return ModifierFlags.Default;
            case SyntaxKind.AsyncKeyword:
                return ModifierFlags.Async;
            case SyntaxKind.ReadonlyKeyword:
                return ModifierFlags.Readonly;
            case SyntaxKind.OverrideKeyword:
                return ModifierFlags.Override;
            case SyntaxKind.InKeyword:
                return ModifierFlags.In;
            case SyntaxKind.OutKeyword:
                return ModifierFlags.Out;
            case SyntaxKind.Decorator:
                return ModifierFlags.Decorator;
        }
        return ModifierFlags.None;
    }

    internal static bool IsLogicalOrCoalescingAssignmentOperator(SyntaxKind token)
    {
        return token == SyntaxKind.BarBarEqualsToken
            || token == SyntaxKind.AmpersandAmpersandEqualsToken
            || token == SyntaxKind.QuestionQuestionEqualsToken;
    }

    internal static bool IsAssignmentOperator(SyntaxKind token)
    {
        return token >= SyntaxKind.FirstAssignment && token <= SyntaxKind.LastAssignment;
    }

    internal static bool IsAssignmentExpression(Node node, bool excludeCompoundAssignment = false)
    {
        return NodeTests.IsBinaryExpression(node)
            && (excludeCompoundAssignment
                ? ((BinaryExpression)node).OperatorToken.Kind == SyntaxKind.EqualsToken
                : IsAssignmentOperator(((BinaryExpression)node).OperatorToken.Kind))
            && UtilitiesPublic.IsLeftHandSideExpression(((BinaryExpression)node).Left);
    }
}
