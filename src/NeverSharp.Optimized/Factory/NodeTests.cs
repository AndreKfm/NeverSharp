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

    internal static bool IsNumericLiteral(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NumericLiteral;

    internal static bool IsBigIntLiteral(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.BigIntLiteral;

    internal static bool IsStringLiteral(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.StringLiteral;

    internal static bool IsJsxText(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxText;

    internal static bool IsRegularExpressionLiteral(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.RegularExpressionLiteral;

    internal static bool IsNoSubstitutionTemplateLiteral(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NoSubstitutionTemplateLiteral;

    // Pseudo-literals

    internal static bool IsTemplateHead(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TemplateHead;

    internal static bool IsTemplateMiddle(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TemplateMiddle;

    internal static bool IsTemplateTail(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TemplateTail;

    // Punctuation

    internal static bool IsDotDotDotToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.DotDotDotToken;

    internal static bool IsCommaToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CommaToken;

    internal static bool IsPlusToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PlusToken;

    internal static bool IsMinusToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.MinusToken;

    internal static bool IsAsteriskToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AsteriskToken;

    internal static bool IsExclamationToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExclamationToken;

    internal static bool IsQuestionToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.QuestionToken;

    internal static bool IsColonToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ColonToken;

    internal static bool IsQuestionDotToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.QuestionDotToken;

    internal static bool IsEqualsGreaterThanToken(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.EqualsGreaterThanToken;

    // Identifiers

    internal static bool IsIdentifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.Identifier;

    internal static bool IsPrivateIdentifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PrivateIdentifier;

    // Reserved Words

    internal static bool IsExportModifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExportKeyword;

    internal static bool IsDefaultModifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.DefaultKeyword;

    internal static bool IsAsyncModifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AsyncKeyword;

    internal static bool IsAssertsKeyword(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AssertsKeyword;

    internal static bool IsAwaitKeyword(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AwaitKeyword;

    internal static bool IsReadonlyKeyword(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ReadonlyKeyword;

    internal static bool IsStaticModifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.StaticKeyword;

    internal static bool IsAbstractModifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AbstractKeyword;

    internal static bool IsOverrideModifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.OverrideKeyword;

    internal static bool IsAccessorModifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AccessorKeyword;

    internal static bool IsSuperKeyword(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SuperKeyword;

    internal static bool IsImportKeyword(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportKeyword;

    internal static bool IsCaseKeyword(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CaseKeyword;

    // Names

    internal static bool IsQualifiedName(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.QualifiedName;

    internal static bool IsComputedPropertyName(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ComputedPropertyName;

    // Signature elements

    internal static bool IsTypeParameterDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeParameter;

    // TODO(rbuckton): Rename to 'isParameterDeclaration'
    internal static bool IsParameter(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.Parameter;

    internal static bool IsDecorator(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.Decorator;

    // TypeMember

    internal static bool IsPropertySignature(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PropertySignature;

    internal static bool IsPropertyDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PropertyDeclaration;

    internal static bool IsMethodSignature(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.MethodSignature;

    internal static bool IsMethodDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.MethodDeclaration;

    internal static bool IsClassStaticBlockDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ClassStaticBlockDeclaration;

    internal static bool IsConstructorDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.Constructor;

    internal static bool IsGetAccessorDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.GetAccessor;

    internal static bool IsSetAccessorDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SetAccessor;

    internal static bool IsCallSignatureDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CallSignature;

    internal static bool IsConstructSignatureDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ConstructSignature;

    internal static bool IsIndexSignatureDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.IndexSignature;

    // Type

    internal static bool IsTypePredicateNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypePredicate;

    internal static bool IsTypeReferenceNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeReference;

    internal static bool IsFunctionTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.FunctionType;

    internal static bool IsConstructorTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ConstructorType;

    internal static bool IsTypeQueryNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeQuery;

    internal static bool IsTypeLiteralNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeLiteral;

    internal static bool IsArrayTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ArrayType;

    internal static bool IsTupleTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TupleType;

    internal static bool IsNamedTupleMember(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NamedTupleMember;

    internal static bool IsOptionalTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.OptionalType;

    internal static bool IsRestTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.RestType;

    internal static bool IsUnionTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.UnionType;

    internal static bool IsIntersectionTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.IntersectionType;

    internal static bool IsConditionalTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ConditionalType;

    internal static bool IsInferTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.InferType;

    internal static bool IsParenthesizedTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ParenthesizedType;

    internal static bool IsThisTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ThisType;

    internal static bool IsTypeOperatorNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeOperator;

    internal static bool IsIndexedAccessTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.IndexedAccessType;

    internal static bool IsMappedTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.MappedType;

    internal static bool IsLiteralTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.LiteralType;

    internal static bool IsImportTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportType;

    internal static bool IsTemplateLiteralTypeSpan(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TemplateLiteralTypeSpan;

    internal static bool IsTemplateLiteralTypeNode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TemplateLiteralType;

    // Binding patterns

    internal static bool IsObjectBindingPattern(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ObjectBindingPattern;

    internal static bool IsArrayBindingPattern(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ArrayBindingPattern;

    internal static bool IsBindingElement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.BindingElement;

    // Expression

    internal static bool IsArrayLiteralExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ArrayLiteralExpression;

    internal static bool IsObjectLiteralExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ObjectLiteralExpression;

    internal static bool IsPropertyAccessExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PropertyAccessExpression;

    internal static bool IsElementAccessExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ElementAccessExpression;

    internal static bool IsCallExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CallExpression;

    internal static bool IsNewExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NewExpression;

    internal static bool IsTaggedTemplateExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TaggedTemplateExpression;

    internal static bool IsTypeAssertionExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeAssertionExpression;

    internal static bool IsParenthesizedExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ParenthesizedExpression;

    internal static bool IsFunctionExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.FunctionExpression;

    internal static bool IsArrowFunction(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ArrowFunction;

    internal static bool IsDeleteExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.DeleteExpression;

    internal static bool IsTypeOfExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeOfExpression;

    internal static bool IsVoidExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.VoidExpression;

    internal static bool IsAwaitExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AwaitExpression;

    internal static bool IsPrefixUnaryExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PrefixUnaryExpression;

    internal static bool IsPostfixUnaryExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PostfixUnaryExpression;

    internal static bool IsBinaryExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.BinaryExpression;

    internal static bool IsConditionalExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ConditionalExpression;

    internal static bool IsTemplateExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TemplateExpression;

    internal static bool IsYieldExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.YieldExpression;

    internal static bool IsSpreadElement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SpreadElement;

    internal static bool IsClassExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ClassExpression;

    internal static bool IsOmittedExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.OmittedExpression;

    internal static bool IsExpressionWithTypeArguments(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExpressionWithTypeArguments;

    internal static bool IsAsExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AsExpression;

    internal static bool IsSatisfiesExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SatisfiesExpression;

    internal static bool IsNonNullExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NonNullExpression;

    internal static bool IsMetaProperty(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.MetaProperty;

    internal static bool IsSyntheticExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SyntheticExpression;

    internal static bool IsPartiallyEmittedExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PartiallyEmittedExpression;

    internal static bool IsCommaListExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CommaListExpression;

    // Misc

    internal static bool IsTemplateSpan(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TemplateSpan;

    internal static bool IsSemicolonClassElement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SemicolonClassElement;

    // Elements

    internal static bool IsBlock(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.Block;

    internal static bool IsVariableStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.VariableStatement;

    internal static bool IsEmptyStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.EmptyStatement;

    internal static bool IsExpressionStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExpressionStatement;

    internal static bool IsIfStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.IfStatement;

    internal static bool IsDoStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.DoStatement;

    internal static bool IsWhileStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.WhileStatement;

    internal static bool IsForStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ForStatement;

    internal static bool IsForInStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ForInStatement;

    internal static bool IsForOfStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ForOfStatement;

    internal static bool IsContinueStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ContinueStatement;

    internal static bool IsBreakStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.BreakStatement;

    internal static bool IsReturnStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ReturnStatement;

    internal static bool IsWithStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.WithStatement;

    internal static bool IsSwitchStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SwitchStatement;

    internal static bool IsLabeledStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.LabeledStatement;

    internal static bool IsThrowStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ThrowStatement;

    internal static bool IsTryStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TryStatement;

    internal static bool IsDebuggerStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.DebuggerStatement;

    internal static bool IsVariableDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.VariableDeclaration;

    internal static bool IsVariableDeclarationList(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.VariableDeclarationList;

    internal static bool IsFunctionDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.FunctionDeclaration;

    internal static bool IsClassDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ClassDeclaration;

    internal static bool IsInterfaceDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.InterfaceDeclaration;

    internal static bool IsTypeAliasDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.TypeAliasDeclaration;

    internal static bool IsEnumDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.EnumDeclaration;

    internal static bool IsModuleDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ModuleDeclaration;

    internal static bool IsModuleBlock(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ModuleBlock;

    internal static bool IsCaseBlock(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CaseBlock;

    internal static bool IsNamespaceExportDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NamespaceExportDeclaration;

    internal static bool IsImportEqualsDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportEqualsDeclaration;

    internal static bool IsImportDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportDeclaration;

    internal static bool IsImportClause(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportClause;

    internal static bool IsImportTypeAssertionContainer(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportTypeAssertionContainer;

    /// <summary>Deprecated.</summary>
    internal static bool IsAssertClause(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AssertClause;

    /// <summary>Deprecated.</summary>
    internal static bool IsAssertEntry(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.AssertEntry;

    internal static bool IsImportAttributes(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportAttributes;

    internal static bool IsImportAttribute(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportAttribute;

    internal static bool IsNamespaceImport(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NamespaceImport;

    internal static bool IsNamespaceExport(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NamespaceExport;

    internal static bool IsNamedImports(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NamedImports;

    internal static bool IsImportSpecifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ImportSpecifier;

    internal static bool IsExportAssignment(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExportAssignment;

    internal static bool IsExportDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExportDeclaration;

    internal static bool IsNamedExports(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NamedExports;

    internal static bool IsExportSpecifier(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExportSpecifier;

    internal static bool IsModuleExportName(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.Identifier || store.Kind(node) == SyntaxKind.StringLiteral;

    internal static bool IsMissingDeclaration(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.MissingDeclaration;

    internal static bool IsNotEmittedStatement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.NotEmittedStatement;

    internal static bool IsSyntheticReference(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SyntheticReferenceExpression;

    // Module References

    internal static bool IsExternalModuleReference(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ExternalModuleReference;

    // JSX

    internal static bool IsJsxElement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxElement;

    internal static bool IsJsxSelfClosingElement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxSelfClosingElement;

    internal static bool IsJsxOpeningElement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxOpeningElement;

    internal static bool IsJsxClosingElement(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxClosingElement;

    internal static bool IsJsxFragment(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxFragment;

    internal static bool IsJsxOpeningFragment(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxOpeningFragment;

    internal static bool IsJsxClosingFragment(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxClosingFragment;

    internal static bool IsJsxAttribute(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxAttribute;

    internal static bool IsJsxAttributes(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxAttributes;

    internal static bool IsJsxSpreadAttribute(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxSpreadAttribute;

    internal static bool IsJsxExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxExpression;

    internal static bool IsJsxNamespacedName(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JsxNamespacedName;

    // Clauses

    internal static bool IsCaseClause(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CaseClause;

    internal static bool IsDefaultClause(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.DefaultClause;

    internal static bool IsHeritageClause(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.HeritageClause;

    internal static bool IsCatchClause(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.CatchClause;

    // Property assignments

    internal static bool IsPropertyAssignment(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.PropertyAssignment;

    internal static bool IsShorthandPropertyAssignment(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.ShorthandPropertyAssignment;

    internal static bool IsSpreadAssignment(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SpreadAssignment;

    // Enum

    internal static bool IsEnumMember(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.EnumMember;

    // Top-level nodes

    internal static bool IsSourceFile(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.SourceFile;

    internal static bool IsBundle(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.Bundle;

    // JSDoc Elements

    internal static bool IsJSDocTypeExpression(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocTypeExpression;

    internal static bool IsJSDocNameReference(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocNameReference;

    internal static bool IsJSDocMemberName(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocMemberName;

    internal static bool IsJSDocLink(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocLink;

    internal static bool IsJSDocLinkCode(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocLinkCode;

    internal static bool IsJSDocLinkPlain(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocLinkPlain;

    internal static bool IsJSDocAllType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocAllType;

    internal static bool IsJSDocUnknownType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocUnknownType;

    internal static bool IsJSDocNullableType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocNullableType;

    internal static bool IsJSDocNonNullableType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocNonNullableType;

    internal static bool IsJSDocOptionalType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocOptionalType;

    internal static bool IsJSDocFunctionType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocFunctionType;

    internal static bool IsJSDocVariadicType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocVariadicType;

    internal static bool IsJSDocNamepathType(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocNamepathType;

    internal static bool IsJSDoc(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDoc;

    internal static bool IsJSDocTypeLiteral(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocTypeLiteral;

    internal static bool IsJSDocSignature(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocSignature;

    // JSDoc Tags

    internal static bool IsJSDocAugmentsTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocAugmentsTag;

    internal static bool IsJSDocAuthorTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocAuthorTag;

    internal static bool IsJSDocClassTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocClassTag;

    internal static bool IsJSDocCallbackTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocCallbackTag;

    internal static bool IsJSDocPublicTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocPublicTag;

    internal static bool IsJSDocPrivateTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocPrivateTag;

    internal static bool IsJSDocProtectedTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocProtectedTag;

    internal static bool IsJSDocReadonlyTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocReadonlyTag;

    internal static bool IsJSDocOverrideTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocOverrideTag;

    internal static bool IsJSDocOverloadTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocOverloadTag;

    internal static bool IsJSDocDeprecatedTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocDeprecatedTag;

    internal static bool IsJSDocSeeTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocSeeTag;

    internal static bool IsJSDocEnumTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocEnumTag;

    internal static bool IsJSDocParameterTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocParameterTag;

    internal static bool IsJSDocReturnTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocReturnTag;

    internal static bool IsJSDocThisTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocThisTag;

    internal static bool IsJSDocTypeTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocTypeTag;

    internal static bool IsJSDocTemplateTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocTemplateTag;

    internal static bool IsJSDocTypedefTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocTypedefTag;

    internal static bool IsJSDocUnknownTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocTag;

    internal static bool IsJSDocPropertyTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocPropertyTag;

    internal static bool IsJSDocImplementsTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocImplementsTag;

    internal static bool IsJSDocSatisfiesTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocSatisfiesTag;

    internal static bool IsJSDocThrowsTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocThrowsTag;

    internal static bool IsJSDocImportTag(NodeStore store, Node node) => store.Kind(node) == SyntaxKind.JSDocImportTag;

    // Synthesized list

    internal static bool IsSyntaxList(NodeStore store, Node n) => store.Kind(n) == SyntaxKind.SyntaxList;
}
