// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeTests.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// factory/nodeTests.ts: the node kind tests (all of the file; each is `node.kind === SyntaxKind.X`). TypeScript's
/// type predicates narrow the node; C# callers cast after the test.
/// </summary>
internal static partial class FactoryNodeTests
{
    // Literals

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNumericLiteral(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NumericLiteral;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsBigIntLiteral(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.BigIntLiteral;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsStringLiteral(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.StringLiteral;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJsxText(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxText;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsRegularExpressionLiteral(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.RegularExpressionLiteral;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNoSubstitutionTemplateLiteral(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NoSubstitutionTemplateLiteral;

    // Pseudo-literals

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTemplateHead(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TemplateHead;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTemplateMiddle(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TemplateMiddle;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTemplateTail(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TemplateTail;

    // Punctuation

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsDotDotDotToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.DotDotDotToken;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsCommaToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CommaToken;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPlusToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PlusToken;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsMinusToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.MinusToken;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAsteriskToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AsteriskToken;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsExclamationToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExclamationToken;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsQuestionToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.QuestionToken;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsColonToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ColonToken;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsQuestionDotToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.QuestionDotToken;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsEqualsGreaterThanToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.EqualsGreaterThanToken;

    // Identifiers

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsIdentifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.Identifier;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPrivateIdentifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PrivateIdentifier;

    // Reserved Words

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsExportModifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExportKeyword;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsDefaultModifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.DefaultKeyword;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAsyncModifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AsyncKeyword;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAssertsKeyword(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AssertsKeyword;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAwaitKeyword(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AwaitKeyword;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsReadonlyKeyword(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ReadonlyKeyword;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsStaticModifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.StaticKeyword;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAbstractModifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AbstractKeyword;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsOverrideModifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.OverrideKeyword;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAccessorModifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AccessorKeyword;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsSuperKeyword(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SuperKeyword;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsImportKeyword(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportKeyword;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsCaseKeyword(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CaseKeyword;

    // Names

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsQualifiedName(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.QualifiedName;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsComputedPropertyName(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ComputedPropertyName;

    // Signature elements

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTypeParameterDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeParameter;

    // TODO(rbuckton): Rename to 'isParameterDeclaration'
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsParameter(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.Parameter;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsDecorator(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.Decorator;

    // TypeMember

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPropertySignature(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PropertySignature;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPropertyDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PropertyDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsMethodSignature(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.MethodSignature;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsMethodDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.MethodDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsClassStaticBlockDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ClassStaticBlockDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsConstructorDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.Constructor;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsGetAccessorDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.GetAccessor;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsSetAccessorDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SetAccessor;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsCallSignatureDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CallSignature;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsConstructSignatureDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ConstructSignature;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsIndexSignatureDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.IndexSignature;

    // Type

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTypePredicateNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypePredicate;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTypeReferenceNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeReference;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsFunctionTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.FunctionType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsConstructorTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ConstructorType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTypeQueryNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeQuery;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTypeLiteralNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeLiteral;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsArrayTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ArrayType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTupleTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TupleType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNamedTupleMember(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NamedTupleMember;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsOptionalTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.OptionalType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsRestTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.RestType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsUnionTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.UnionType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsIntersectionTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.IntersectionType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsConditionalTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ConditionalType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsInferTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.InferType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsParenthesizedTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ParenthesizedType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsThisTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ThisType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTypeOperatorNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeOperator;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsIndexedAccessTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.IndexedAccessType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsMappedTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.MappedType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsLiteralTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.LiteralType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsImportTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTemplateLiteralTypeSpan(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TemplateLiteralTypeSpan;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTemplateLiteralTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TemplateLiteralType;

    // Binding patterns

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsObjectBindingPattern(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ObjectBindingPattern;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsArrayBindingPattern(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ArrayBindingPattern;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsBindingElement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.BindingElement;

    // Expression

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsArrayLiteralExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ArrayLiteralExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsObjectLiteralExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ObjectLiteralExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPropertyAccessExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PropertyAccessExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsElementAccessExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ElementAccessExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsCallExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CallExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNewExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NewExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTaggedTemplateExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TaggedTemplateExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTypeAssertionExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeAssertionExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsParenthesizedExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ParenthesizedExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsFunctionExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.FunctionExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsArrowFunction(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ArrowFunction;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsDeleteExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.DeleteExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTypeOfExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeOfExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsVoidExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.VoidExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAwaitExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AwaitExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPrefixUnaryExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PrefixUnaryExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPostfixUnaryExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PostfixUnaryExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsBinaryExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.BinaryExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsConditionalExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ConditionalExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTemplateExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TemplateExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsYieldExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.YieldExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsSpreadElement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SpreadElement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsClassExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ClassExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsOmittedExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.OmittedExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsExpressionWithTypeArguments(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExpressionWithTypeArguments;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAsExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AsExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsSatisfiesExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SatisfiesExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNonNullExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NonNullExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsMetaProperty(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.MetaProperty;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsSyntheticExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SyntheticExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPartiallyEmittedExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PartiallyEmittedExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsCommaListExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CommaListExpression;

    // Misc

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTemplateSpan(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TemplateSpan;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsSemicolonClassElement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SemicolonClassElement;

    // Elements

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsBlock(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.Block;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsVariableStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.VariableStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsEmptyStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.EmptyStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsExpressionStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExpressionStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsIfStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.IfStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsDoStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.DoStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsWhileStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.WhileStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsForStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ForStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsForInStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ForInStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsForOfStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ForOfStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsContinueStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ContinueStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsBreakStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.BreakStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsReturnStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ReturnStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsWithStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.WithStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsSwitchStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SwitchStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsLabeledStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.LabeledStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsThrowStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ThrowStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTryStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TryStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsDebuggerStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.DebuggerStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsVariableDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.VariableDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsVariableDeclarationList(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.VariableDeclarationList;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsFunctionDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.FunctionDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsClassDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ClassDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsInterfaceDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.InterfaceDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsTypeAliasDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeAliasDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsEnumDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.EnumDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsModuleDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ModuleDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsModuleBlock(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ModuleBlock;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsCaseBlock(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CaseBlock;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNamespaceExportDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NamespaceExportDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsImportEqualsDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportEqualsDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsImportDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsImportClause(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportClause;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsImportTypeAssertionContainer(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportTypeAssertionContainer;

    /// <summary>Deprecated.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAssertClause(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AssertClause;

    /// <summary>Deprecated.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAssertEntry(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AssertEntry;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsImportAttributes(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportAttributes;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsImportAttribute(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportAttribute;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNamespaceImport(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NamespaceImport;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNamespaceExport(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NamespaceExport;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNamedImports(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NamedImports;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsImportSpecifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportSpecifier;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsExportAssignment(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExportAssignment;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsExportDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExportDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNamedExports(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NamedExports;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsExportSpecifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExportSpecifier;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsModuleExportName(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.Identifier || store.Kind(node) == SyntaxKind.StringLiteral;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsMissingDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.MissingDeclaration;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsNotEmittedStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NotEmittedStatement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsSyntheticReference(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SyntheticReferenceExpression;

    // Module References

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsExternalModuleReference(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExternalModuleReference;

    // JSX

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJsxElement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxElement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJsxSelfClosingElement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxSelfClosingElement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJsxOpeningElement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxOpeningElement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJsxClosingElement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxClosingElement;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJsxFragment(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxFragment;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJsxOpeningFragment(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxOpeningFragment;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJsxClosingFragment(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxClosingFragment;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJsxAttribute(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxAttribute;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJsxAttributes(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxAttributes;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJsxSpreadAttribute(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxSpreadAttribute;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJsxExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJsxNamespacedName(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxNamespacedName;

    // Clauses

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsCaseClause(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CaseClause;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsDefaultClause(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.DefaultClause;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsHeritageClause(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.HeritageClause;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsCatchClause(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CatchClause;

    // Property assignments

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsPropertyAssignment(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PropertyAssignment;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsShorthandPropertyAssignment(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ShorthandPropertyAssignment;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsSpreadAssignment(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SpreadAssignment;

    // Enum

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsEnumMember(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.EnumMember;

    // Top-level nodes

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsSourceFile(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SourceFile;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsBundle(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.Bundle;

    // JSDoc Elements

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocTypeExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocTypeExpression;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocNameReference(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocNameReference;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocMemberName(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocMemberName;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocLink(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocLink;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocLinkCode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocLinkCode;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocLinkPlain(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocLinkPlain;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocAllType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocAllType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocUnknownType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocUnknownType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocNullableType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocNullableType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocNonNullableType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocNonNullableType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocOptionalType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocOptionalType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocFunctionType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocFunctionType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocVariadicType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocVariadicType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocNamepathType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocNamepathType;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDoc(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDoc;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocTypeLiteral(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocTypeLiteral;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocSignature(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocSignature;

    // JSDoc Tags

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocAugmentsTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocAugmentsTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocAuthorTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocAuthorTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocClassTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocClassTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocCallbackTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocCallbackTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocPublicTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocPublicTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocPrivateTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocPrivateTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocProtectedTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocProtectedTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocReadonlyTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocReadonlyTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocOverrideTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocOverrideTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocOverloadTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocOverloadTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocDeprecatedTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocDeprecatedTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocSeeTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocSeeTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocEnumTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocEnumTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocParameterTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocParameterTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocReturnTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocReturnTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocThisTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocThisTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocTypeTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocTypeTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocTemplateTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocTemplateTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocTypedefTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocTypedefTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocUnknownTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocPropertyTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocPropertyTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocImplementsTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocImplementsTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocSatisfiesTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocSatisfiesTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocThrowsTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocThrowsTag;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsJSDocImportTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocImportTag;

    // Synthesized list

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsSyntaxList(NodeStore store, Node n) => store.Kind(n) == SyntaxKind.SyntaxList;
}
