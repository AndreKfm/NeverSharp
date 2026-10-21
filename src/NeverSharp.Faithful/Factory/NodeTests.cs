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

namespace NeverSharp.Faithful;

/// <summary>
/// factory/nodeTests.ts: the node kind tests (all of the file; each is `node.kind === SyntaxKind.X`). TypeScript's
/// type predicates narrow the node; C# callers cast after the test.
/// </summary>
internal static partial class FactoryNodeTests
{
    // Literals

    internal static bool IsNumericLiteral(Node node) => node.Kind == SyntaxKind.NumericLiteral;

    internal static bool IsBigIntLiteral(Node node) => node.Kind == SyntaxKind.BigIntLiteral;

    internal static bool IsStringLiteral(Node node) => node.Kind == SyntaxKind.StringLiteral;

    internal static bool IsJsxText(Node node) => node.Kind == SyntaxKind.JsxText;

    internal static bool IsRegularExpressionLiteral(Node node) => node.Kind == SyntaxKind.RegularExpressionLiteral;

    internal static bool IsNoSubstitutionTemplateLiteral(Node node) => node.Kind == SyntaxKind.NoSubstitutionTemplateLiteral;

    // Pseudo-literals

    internal static bool IsTemplateHead(Node node) => node.Kind == SyntaxKind.TemplateHead;

    internal static bool IsTemplateMiddle(Node node) => node.Kind == SyntaxKind.TemplateMiddle;

    internal static bool IsTemplateTail(Node node) => node.Kind == SyntaxKind.TemplateTail;

    // Punctuation

    internal static bool IsDotDotDotToken(Node node) => node.Kind == SyntaxKind.DotDotDotToken;

    internal static bool IsCommaToken(Node node) => node.Kind == SyntaxKind.CommaToken;

    internal static bool IsPlusToken(Node node) => node.Kind == SyntaxKind.PlusToken;

    internal static bool IsMinusToken(Node node) => node.Kind == SyntaxKind.MinusToken;

    internal static bool IsAsteriskToken(Node node) => node.Kind == SyntaxKind.AsteriskToken;

    internal static bool IsExclamationToken(Node node) => node.Kind == SyntaxKind.ExclamationToken;

    internal static bool IsQuestionToken(Node node) => node.Kind == SyntaxKind.QuestionToken;

    internal static bool IsColonToken(Node node) => node.Kind == SyntaxKind.ColonToken;

    internal static bool IsQuestionDotToken(Node node) => node.Kind == SyntaxKind.QuestionDotToken;

    internal static bool IsEqualsGreaterThanToken(Node node) => node.Kind == SyntaxKind.EqualsGreaterThanToken;

    // Identifiers

    internal static bool IsIdentifier(Node node) => node.Kind == SyntaxKind.Identifier;

    internal static bool IsPrivateIdentifier(Node node) => node.Kind == SyntaxKind.PrivateIdentifier;

    // Reserved Words

    internal static bool IsExportModifier(Node node) => node.Kind == SyntaxKind.ExportKeyword;

    internal static bool IsDefaultModifier(Node node) => node.Kind == SyntaxKind.DefaultKeyword;

    internal static bool IsAsyncModifier(Node node) => node.Kind == SyntaxKind.AsyncKeyword;

    internal static bool IsAssertsKeyword(Node node) => node.Kind == SyntaxKind.AssertsKeyword;

    internal static bool IsAwaitKeyword(Node node) => node.Kind == SyntaxKind.AwaitKeyword;

    internal static bool IsReadonlyKeyword(Node node) => node.Kind == SyntaxKind.ReadonlyKeyword;

    internal static bool IsStaticModifier(Node node) => node.Kind == SyntaxKind.StaticKeyword;

    internal static bool IsAbstractModifier(Node node) => node.Kind == SyntaxKind.AbstractKeyword;

    internal static bool IsOverrideModifier(Node node) => node.Kind == SyntaxKind.OverrideKeyword;

    internal static bool IsAccessorModifier(Node node) => node.Kind == SyntaxKind.AccessorKeyword;

    internal static bool IsSuperKeyword(Node node) => node.Kind == SyntaxKind.SuperKeyword;

    internal static bool IsImportKeyword(Node node) => node.Kind == SyntaxKind.ImportKeyword;

    internal static bool IsCaseKeyword(Node node) => node.Kind == SyntaxKind.CaseKeyword;

    // Names

    internal static bool IsQualifiedName(Node node) => node.Kind == SyntaxKind.QualifiedName;

    internal static bool IsComputedPropertyName(Node node) => node.Kind == SyntaxKind.ComputedPropertyName;

    // Signature elements

    internal static bool IsTypeParameterDeclaration(Node node) => node.Kind == SyntaxKind.TypeParameter;

    // TODO(rbuckton): Rename to 'isParameterDeclaration'
    internal static bool IsParameter(Node node) => node.Kind == SyntaxKind.Parameter;

    internal static bool IsDecorator(Node node) => node.Kind == SyntaxKind.Decorator;

    // TypeMember

    internal static bool IsPropertySignature(Node node) => node.Kind == SyntaxKind.PropertySignature;

    internal static bool IsPropertyDeclaration(Node node) => node.Kind == SyntaxKind.PropertyDeclaration;

    internal static bool IsMethodSignature(Node node) => node.Kind == SyntaxKind.MethodSignature;

    internal static bool IsMethodDeclaration(Node node) => node.Kind == SyntaxKind.MethodDeclaration;

    internal static bool IsClassStaticBlockDeclaration(Node node) => node.Kind == SyntaxKind.ClassStaticBlockDeclaration;

    internal static bool IsConstructorDeclaration(Node node) => node.Kind == SyntaxKind.Constructor;

    internal static bool IsGetAccessorDeclaration(Node node) => node.Kind == SyntaxKind.GetAccessor;

    internal static bool IsSetAccessorDeclaration(Node node) => node.Kind == SyntaxKind.SetAccessor;

    internal static bool IsCallSignatureDeclaration(Node node) => node.Kind == SyntaxKind.CallSignature;

    internal static bool IsConstructSignatureDeclaration(Node node) => node.Kind == SyntaxKind.ConstructSignature;

    internal static bool IsIndexSignatureDeclaration(Node node) => node.Kind == SyntaxKind.IndexSignature;

    // Type

    internal static bool IsTypePredicateNode(Node node) => node.Kind == SyntaxKind.TypePredicate;

    internal static bool IsTypeReferenceNode(Node node) => node.Kind == SyntaxKind.TypeReference;

    internal static bool IsFunctionTypeNode(Node node) => node.Kind == SyntaxKind.FunctionType;

    internal static bool IsConstructorTypeNode(Node node) => node.Kind == SyntaxKind.ConstructorType;

    internal static bool IsTypeQueryNode(Node node) => node.Kind == SyntaxKind.TypeQuery;

    internal static bool IsTypeLiteralNode(Node node) => node.Kind == SyntaxKind.TypeLiteral;

    internal static bool IsArrayTypeNode(Node node) => node.Kind == SyntaxKind.ArrayType;

    internal static bool IsTupleTypeNode(Node node) => node.Kind == SyntaxKind.TupleType;

    internal static bool IsNamedTupleMember(Node node) => node.Kind == SyntaxKind.NamedTupleMember;

    internal static bool IsOptionalTypeNode(Node node) => node.Kind == SyntaxKind.OptionalType;

    internal static bool IsRestTypeNode(Node node) => node.Kind == SyntaxKind.RestType;

    internal static bool IsUnionTypeNode(Node node) => node.Kind == SyntaxKind.UnionType;

    internal static bool IsIntersectionTypeNode(Node node) => node.Kind == SyntaxKind.IntersectionType;

    internal static bool IsConditionalTypeNode(Node node) => node.Kind == SyntaxKind.ConditionalType;

    internal static bool IsInferTypeNode(Node node) => node.Kind == SyntaxKind.InferType;

    internal static bool IsParenthesizedTypeNode(Node node) => node.Kind == SyntaxKind.ParenthesizedType;

    internal static bool IsThisTypeNode(Node node) => node.Kind == SyntaxKind.ThisType;

    internal static bool IsTypeOperatorNode(Node node) => node.Kind == SyntaxKind.TypeOperator;

    internal static bool IsIndexedAccessTypeNode(Node node) => node.Kind == SyntaxKind.IndexedAccessType;

    internal static bool IsMappedTypeNode(Node node) => node.Kind == SyntaxKind.MappedType;

    internal static bool IsLiteralTypeNode(Node node) => node.Kind == SyntaxKind.LiteralType;

    internal static bool IsImportTypeNode(Node node) => node.Kind == SyntaxKind.ImportType;

    internal static bool IsTemplateLiteralTypeSpan(Node node) => node.Kind == SyntaxKind.TemplateLiteralTypeSpan;

    internal static bool IsTemplateLiteralTypeNode(Node node) => node.Kind == SyntaxKind.TemplateLiteralType;

    // Binding patterns

    internal static bool IsObjectBindingPattern(Node node) => node.Kind == SyntaxKind.ObjectBindingPattern;

    internal static bool IsArrayBindingPattern(Node node) => node.Kind == SyntaxKind.ArrayBindingPattern;

    internal static bool IsBindingElement(Node node) => node.Kind == SyntaxKind.BindingElement;

    // Expression

    internal static bool IsArrayLiteralExpression(Node node) => node.Kind == SyntaxKind.ArrayLiteralExpression;

    internal static bool IsObjectLiteralExpression(Node node) => node.Kind == SyntaxKind.ObjectLiteralExpression;

    internal static bool IsPropertyAccessExpression(Node node) => node.Kind == SyntaxKind.PropertyAccessExpression;

    internal static bool IsElementAccessExpression(Node node) => node.Kind == SyntaxKind.ElementAccessExpression;

    internal static bool IsCallExpression(Node node) => node.Kind == SyntaxKind.CallExpression;

    internal static bool IsNewExpression(Node node) => node.Kind == SyntaxKind.NewExpression;

    internal static bool IsTaggedTemplateExpression(Node node) => node.Kind == SyntaxKind.TaggedTemplateExpression;

    internal static bool IsTypeAssertionExpression(Node node) => node.Kind == SyntaxKind.TypeAssertionExpression;

    internal static bool IsParenthesizedExpression(Node node) => node.Kind == SyntaxKind.ParenthesizedExpression;

    internal static bool IsFunctionExpression(Node node) => node.Kind == SyntaxKind.FunctionExpression;

    internal static bool IsArrowFunction(Node node) => node.Kind == SyntaxKind.ArrowFunction;

    internal static bool IsDeleteExpression(Node node) => node.Kind == SyntaxKind.DeleteExpression;

    internal static bool IsTypeOfExpression(Node node) => node.Kind == SyntaxKind.TypeOfExpression;

    internal static bool IsVoidExpression(Node node) => node.Kind == SyntaxKind.VoidExpression;

    internal static bool IsAwaitExpression(Node node) => node.Kind == SyntaxKind.AwaitExpression;

    internal static bool IsPrefixUnaryExpression(Node node) => node.Kind == SyntaxKind.PrefixUnaryExpression;

    internal static bool IsPostfixUnaryExpression(Node node) => node.Kind == SyntaxKind.PostfixUnaryExpression;

    internal static bool IsBinaryExpression(Node node) => node.Kind == SyntaxKind.BinaryExpression;

    internal static bool IsConditionalExpression(Node node) => node.Kind == SyntaxKind.ConditionalExpression;

    internal static bool IsTemplateExpression(Node node) => node.Kind == SyntaxKind.TemplateExpression;

    internal static bool IsYieldExpression(Node node) => node.Kind == SyntaxKind.YieldExpression;

    internal static bool IsSpreadElement(Node node) => node.Kind == SyntaxKind.SpreadElement;

    internal static bool IsClassExpression(Node node) => node.Kind == SyntaxKind.ClassExpression;

    internal static bool IsOmittedExpression(Node node) => node.Kind == SyntaxKind.OmittedExpression;

    internal static bool IsExpressionWithTypeArguments(Node node) => node.Kind == SyntaxKind.ExpressionWithTypeArguments;

    internal static bool IsAsExpression(Node node) => node.Kind == SyntaxKind.AsExpression;

    internal static bool IsSatisfiesExpression(Node node) => node.Kind == SyntaxKind.SatisfiesExpression;

    internal static bool IsNonNullExpression(Node node) => node.Kind == SyntaxKind.NonNullExpression;

    internal static bool IsMetaProperty(Node node) => node.Kind == SyntaxKind.MetaProperty;

    internal static bool IsSyntheticExpression(Node node) => node.Kind == SyntaxKind.SyntheticExpression;

    internal static bool IsPartiallyEmittedExpression(Node node) => node.Kind == SyntaxKind.PartiallyEmittedExpression;

    internal static bool IsCommaListExpression(Node node) => node.Kind == SyntaxKind.CommaListExpression;

    // Misc

    internal static bool IsTemplateSpan(Node node) => node.Kind == SyntaxKind.TemplateSpan;

    internal static bool IsSemicolonClassElement(Node node) => node.Kind == SyntaxKind.SemicolonClassElement;

    // Elements

    internal static bool IsBlock(Node node) => node.Kind == SyntaxKind.Block;

    internal static bool IsVariableStatement(Node node) => node.Kind == SyntaxKind.VariableStatement;

    internal static bool IsEmptyStatement(Node node) => node.Kind == SyntaxKind.EmptyStatement;

    internal static bool IsExpressionStatement(Node node) => node.Kind == SyntaxKind.ExpressionStatement;

    internal static bool IsIfStatement(Node node) => node.Kind == SyntaxKind.IfStatement;

    internal static bool IsDoStatement(Node node) => node.Kind == SyntaxKind.DoStatement;

    internal static bool IsWhileStatement(Node node) => node.Kind == SyntaxKind.WhileStatement;

    internal static bool IsForStatement(Node node) => node.Kind == SyntaxKind.ForStatement;

    internal static bool IsForInStatement(Node node) => node.Kind == SyntaxKind.ForInStatement;

    internal static bool IsForOfStatement(Node node) => node.Kind == SyntaxKind.ForOfStatement;

    internal static bool IsContinueStatement(Node node) => node.Kind == SyntaxKind.ContinueStatement;

    internal static bool IsBreakStatement(Node node) => node.Kind == SyntaxKind.BreakStatement;

    internal static bool IsReturnStatement(Node node) => node.Kind == SyntaxKind.ReturnStatement;

    internal static bool IsWithStatement(Node node) => node.Kind == SyntaxKind.WithStatement;

    internal static bool IsSwitchStatement(Node node) => node.Kind == SyntaxKind.SwitchStatement;

    internal static bool IsLabeledStatement(Node node) => node.Kind == SyntaxKind.LabeledStatement;

    internal static bool IsThrowStatement(Node node) => node.Kind == SyntaxKind.ThrowStatement;

    internal static bool IsTryStatement(Node node) => node.Kind == SyntaxKind.TryStatement;

    internal static bool IsDebuggerStatement(Node node) => node.Kind == SyntaxKind.DebuggerStatement;

    internal static bool IsVariableDeclaration(Node node) => node.Kind == SyntaxKind.VariableDeclaration;

    internal static bool IsVariableDeclarationList(Node node) => node.Kind == SyntaxKind.VariableDeclarationList;

    internal static bool IsFunctionDeclaration(Node node) => node.Kind == SyntaxKind.FunctionDeclaration;

    internal static bool IsClassDeclaration(Node node) => node.Kind == SyntaxKind.ClassDeclaration;

    internal static bool IsInterfaceDeclaration(Node node) => node.Kind == SyntaxKind.InterfaceDeclaration;

    internal static bool IsTypeAliasDeclaration(Node node) => node.Kind == SyntaxKind.TypeAliasDeclaration;

    internal static bool IsEnumDeclaration(Node node) => node.Kind == SyntaxKind.EnumDeclaration;

    internal static bool IsModuleDeclaration(Node node) => node.Kind == SyntaxKind.ModuleDeclaration;

    internal static bool IsModuleBlock(Node node) => node.Kind == SyntaxKind.ModuleBlock;

    internal static bool IsCaseBlock(Node node) => node.Kind == SyntaxKind.CaseBlock;

    internal static bool IsNamespaceExportDeclaration(Node node) => node.Kind == SyntaxKind.NamespaceExportDeclaration;

    internal static bool IsImportEqualsDeclaration(Node node) => node.Kind == SyntaxKind.ImportEqualsDeclaration;

    internal static bool IsImportDeclaration(Node node) => node.Kind == SyntaxKind.ImportDeclaration;

    internal static bool IsImportClause(Node node) => node.Kind == SyntaxKind.ImportClause;

    internal static bool IsImportTypeAssertionContainer(Node node) => node.Kind == SyntaxKind.ImportTypeAssertionContainer;

    /// <summary>Deprecated.</summary>
    internal static bool IsAssertClause(Node node) => node.Kind == SyntaxKind.AssertClause;

    /// <summary>Deprecated.</summary>
    internal static bool IsAssertEntry(Node node) => node.Kind == SyntaxKind.AssertEntry;

    internal static bool IsImportAttributes(Node node) => node.Kind == SyntaxKind.ImportAttributes;

    internal static bool IsImportAttribute(Node node) => node.Kind == SyntaxKind.ImportAttribute;

    internal static bool IsNamespaceImport(Node node) => node.Kind == SyntaxKind.NamespaceImport;

    internal static bool IsNamespaceExport(Node node) => node.Kind == SyntaxKind.NamespaceExport;

    internal static bool IsNamedImports(Node node) => node.Kind == SyntaxKind.NamedImports;

    internal static bool IsImportSpecifier(Node node) => node.Kind == SyntaxKind.ImportSpecifier;

    internal static bool IsExportAssignment(Node node) => node.Kind == SyntaxKind.ExportAssignment;

    internal static bool IsExportDeclaration(Node node) => node.Kind == SyntaxKind.ExportDeclaration;

    internal static bool IsNamedExports(Node node) => node.Kind == SyntaxKind.NamedExports;

    internal static bool IsExportSpecifier(Node node) => node.Kind == SyntaxKind.ExportSpecifier;

    internal static bool IsModuleExportName(Node node) => node.Kind == SyntaxKind.Identifier || node.Kind == SyntaxKind.StringLiteral;

    internal static bool IsMissingDeclaration(Node node) => node.Kind == SyntaxKind.MissingDeclaration;

    internal static bool IsNotEmittedStatement(Node node) => node.Kind == SyntaxKind.NotEmittedStatement;

    internal static bool IsSyntheticReference(Node node) => node.Kind == SyntaxKind.SyntheticReferenceExpression;

    // Module References

    internal static bool IsExternalModuleReference(Node node) => node.Kind == SyntaxKind.ExternalModuleReference;

    // JSX

    internal static bool IsJsxElement(Node node) => node.Kind == SyntaxKind.JsxElement;

    internal static bool IsJsxSelfClosingElement(Node node) => node.Kind == SyntaxKind.JsxSelfClosingElement;

    internal static bool IsJsxOpeningElement(Node node) => node.Kind == SyntaxKind.JsxOpeningElement;

    internal static bool IsJsxClosingElement(Node node) => node.Kind == SyntaxKind.JsxClosingElement;

    internal static bool IsJsxFragment(Node node) => node.Kind == SyntaxKind.JsxFragment;

    internal static bool IsJsxOpeningFragment(Node node) => node.Kind == SyntaxKind.JsxOpeningFragment;

    internal static bool IsJsxClosingFragment(Node node) => node.Kind == SyntaxKind.JsxClosingFragment;

    internal static bool IsJsxAttribute(Node node) => node.Kind == SyntaxKind.JsxAttribute;

    internal static bool IsJsxAttributes(Node node) => node.Kind == SyntaxKind.JsxAttributes;

    internal static bool IsJsxSpreadAttribute(Node node) => node.Kind == SyntaxKind.JsxSpreadAttribute;

    internal static bool IsJsxExpression(Node node) => node.Kind == SyntaxKind.JsxExpression;

    internal static bool IsJsxNamespacedName(Node node) => node.Kind == SyntaxKind.JsxNamespacedName;

    // Clauses

    internal static bool IsCaseClause(Node node) => node.Kind == SyntaxKind.CaseClause;

    internal static bool IsDefaultClause(Node node) => node.Kind == SyntaxKind.DefaultClause;

    internal static bool IsHeritageClause(Node node) => node.Kind == SyntaxKind.HeritageClause;

    internal static bool IsCatchClause(Node node) => node.Kind == SyntaxKind.CatchClause;

    // Property assignments

    internal static bool IsPropertyAssignment(Node node) => node.Kind == SyntaxKind.PropertyAssignment;

    internal static bool IsShorthandPropertyAssignment(Node node) => node.Kind == SyntaxKind.ShorthandPropertyAssignment;

    internal static bool IsSpreadAssignment(Node node) => node.Kind == SyntaxKind.SpreadAssignment;

    // Enum

    internal static bool IsEnumMember(Node node) => node.Kind == SyntaxKind.EnumMember;

    // Top-level nodes

    internal static bool IsSourceFile(Node node) => node.Kind == SyntaxKind.SourceFile;

    internal static bool IsBundle(Node node) => node.Kind == SyntaxKind.Bundle;

    // JSDoc Elements

    internal static bool IsJSDocTypeExpression(Node node) => node.Kind == SyntaxKind.JSDocTypeExpression;

    internal static bool IsJSDocNameReference(Node node) => node.Kind == SyntaxKind.JSDocNameReference;

    internal static bool IsJSDocMemberName(Node node) => node.Kind == SyntaxKind.JSDocMemberName;

    internal static bool IsJSDocLink(Node node) => node.Kind == SyntaxKind.JSDocLink;

    internal static bool IsJSDocLinkCode(Node node) => node.Kind == SyntaxKind.JSDocLinkCode;

    internal static bool IsJSDocLinkPlain(Node node) => node.Kind == SyntaxKind.JSDocLinkPlain;

    internal static bool IsJSDocAllType(Node node) => node.Kind == SyntaxKind.JSDocAllType;

    internal static bool IsJSDocUnknownType(Node node) => node.Kind == SyntaxKind.JSDocUnknownType;

    internal static bool IsJSDocNullableType(Node node) => node.Kind == SyntaxKind.JSDocNullableType;

    internal static bool IsJSDocNonNullableType(Node node) => node.Kind == SyntaxKind.JSDocNonNullableType;

    internal static bool IsJSDocOptionalType(Node node) => node.Kind == SyntaxKind.JSDocOptionalType;

    internal static bool IsJSDocFunctionType(Node node) => node.Kind == SyntaxKind.JSDocFunctionType;

    internal static bool IsJSDocVariadicType(Node node) => node.Kind == SyntaxKind.JSDocVariadicType;

    internal static bool IsJSDocNamepathType(Node node) => node.Kind == SyntaxKind.JSDocNamepathType;

    internal static bool IsJSDoc(Node node) => node.Kind == SyntaxKind.JSDoc;

    internal static bool IsJSDocTypeLiteral(Node node) => node.Kind == SyntaxKind.JSDocTypeLiteral;

    internal static bool IsJSDocSignature(Node node) => node.Kind == SyntaxKind.JSDocSignature;

    // JSDoc Tags

    internal static bool IsJSDocAugmentsTag(Node node) => node.Kind == SyntaxKind.JSDocAugmentsTag;

    internal static bool IsJSDocAuthorTag(Node node) => node.Kind == SyntaxKind.JSDocAuthorTag;

    internal static bool IsJSDocClassTag(Node node) => node.Kind == SyntaxKind.JSDocClassTag;

    internal static bool IsJSDocCallbackTag(Node node) => node.Kind == SyntaxKind.JSDocCallbackTag;

    internal static bool IsJSDocPublicTag(Node node) => node.Kind == SyntaxKind.JSDocPublicTag;

    internal static bool IsJSDocPrivateTag(Node node) => node.Kind == SyntaxKind.JSDocPrivateTag;

    internal static bool IsJSDocProtectedTag(Node node) => node.Kind == SyntaxKind.JSDocProtectedTag;

    internal static bool IsJSDocReadonlyTag(Node node) => node.Kind == SyntaxKind.JSDocReadonlyTag;

    internal static bool IsJSDocOverrideTag(Node node) => node.Kind == SyntaxKind.JSDocOverrideTag;

    internal static bool IsJSDocOverloadTag(Node node) => node.Kind == SyntaxKind.JSDocOverloadTag;

    internal static bool IsJSDocDeprecatedTag(Node node) => node.Kind == SyntaxKind.JSDocDeprecatedTag;

    internal static bool IsJSDocSeeTag(Node node) => node.Kind == SyntaxKind.JSDocSeeTag;

    internal static bool IsJSDocEnumTag(Node node) => node.Kind == SyntaxKind.JSDocEnumTag;

    internal static bool IsJSDocParameterTag(Node node) => node.Kind == SyntaxKind.JSDocParameterTag;

    internal static bool IsJSDocReturnTag(Node node) => node.Kind == SyntaxKind.JSDocReturnTag;

    internal static bool IsJSDocThisTag(Node node) => node.Kind == SyntaxKind.JSDocThisTag;

    internal static bool IsJSDocTypeTag(Node node) => node.Kind == SyntaxKind.JSDocTypeTag;

    internal static bool IsJSDocTemplateTag(Node node) => node.Kind == SyntaxKind.JSDocTemplateTag;

    internal static bool IsJSDocTypedefTag(Node node) => node.Kind == SyntaxKind.JSDocTypedefTag;

    internal static bool IsJSDocUnknownTag(Node node) => node.Kind == SyntaxKind.JSDocTag;

    internal static bool IsJSDocPropertyTag(Node node) => node.Kind == SyntaxKind.JSDocPropertyTag;

    internal static bool IsJSDocImplementsTag(Node node) => node.Kind == SyntaxKind.JSDocImplementsTag;

    internal static bool IsJSDocSatisfiesTag(Node node) => node.Kind == SyntaxKind.JSDocSatisfiesTag;

    internal static bool IsJSDocThrowsTag(Node node) => node.Kind == SyntaxKind.JSDocThrowsTag;

    internal static bool IsJSDocImportTag(Node node) => node.Kind == SyntaxKind.JSDocImportTag;

    // Synthesized list

    internal static bool IsSyntaxList(Node n) => n.Kind == SyntaxKind.SyntaxList;
}
