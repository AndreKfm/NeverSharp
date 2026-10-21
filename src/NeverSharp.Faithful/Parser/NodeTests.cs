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

/// <summary>factory/nodeTests.ts: kind tests of nodes (the type predicates of TypeScript are plain bool functions).</summary>
internal static class NodeTests
{
    internal static bool IsNumericLiteral(Node node)
    {
        return node.Kind == SyntaxKind.NumericLiteral;
    }

    internal static bool IsBigIntLiteral(Node node)
    {
        return node.Kind == SyntaxKind.BigIntLiteral;
    }

    internal static bool IsStringLiteral(Node node)
    {
        return node.Kind == SyntaxKind.StringLiteral;
    }

    internal static bool IsJsxText(Node node)
    {
        return node.Kind == SyntaxKind.JsxText;
    }

    internal static bool IsRegularExpressionLiteral(Node node)
    {
        return node.Kind == SyntaxKind.RegularExpressionLiteral;
    }

    internal static bool IsNoSubstitutionTemplateLiteral(Node node)
    {
        return node.Kind == SyntaxKind.NoSubstitutionTemplateLiteral;
    }

    internal static bool IsTemplateHead(Node node)
    {
        return node.Kind == SyntaxKind.TemplateHead;
    }

    internal static bool IsTemplateMiddle(Node node)
    {
        return node.Kind == SyntaxKind.TemplateMiddle;
    }

    internal static bool IsTemplateTail(Node node)
    {
        return node.Kind == SyntaxKind.TemplateTail;
    }

    internal static bool IsDotDotDotToken(Node node)
    {
        return node.Kind == SyntaxKind.DotDotDotToken;
    }

    internal static bool IsCommaToken(Node node)
    {
        return node.Kind == SyntaxKind.CommaToken;
    }

    internal static bool IsPlusToken(Node node)
    {
        return node.Kind == SyntaxKind.PlusToken;
    }

    internal static bool IsMinusToken(Node node)
    {
        return node.Kind == SyntaxKind.MinusToken;
    }

    internal static bool IsAsteriskToken(Node node)
    {
        return node.Kind == SyntaxKind.AsteriskToken;
    }

    internal static bool IsExclamationToken(Node node)
    {
        return node.Kind == SyntaxKind.ExclamationToken;
    }

    internal static bool IsQuestionToken(Node node)
    {
        return node.Kind == SyntaxKind.QuestionToken;
    }

    internal static bool IsColonToken(Node node)
    {
        return node.Kind == SyntaxKind.ColonToken;
    }

    internal static bool IsQuestionDotToken(Node node)
    {
        return node.Kind == SyntaxKind.QuestionDotToken;
    }

    internal static bool IsEqualsGreaterThanToken(Node node)
    {
        return node.Kind == SyntaxKind.EqualsGreaterThanToken;
    }

    internal static bool IsIdentifier(Node node)
    {
        return node.Kind == SyntaxKind.Identifier;
    }

    internal static bool IsPrivateIdentifier(Node node)
    {
        return node.Kind == SyntaxKind.PrivateIdentifier;
    }

    internal static bool IsExportModifier(Node node)
    {
        return node.Kind == SyntaxKind.ExportKeyword;
    }

    internal static bool IsDefaultModifier(Node node)
    {
        return node.Kind == SyntaxKind.DefaultKeyword;
    }

    internal static bool IsAsyncModifier(Node node)
    {
        return node.Kind == SyntaxKind.AsyncKeyword;
    }

    internal static bool IsAssertsKeyword(Node node)
    {
        return node.Kind == SyntaxKind.AssertsKeyword;
    }

    internal static bool IsAwaitKeyword(Node node)
    {
        return node.Kind == SyntaxKind.AwaitKeyword;
    }

    internal static bool IsReadonlyKeyword(Node node)
    {
        return node.Kind == SyntaxKind.ReadonlyKeyword;
    }

    internal static bool IsStaticModifier(Node node)
    {
        return node.Kind == SyntaxKind.StaticKeyword;
    }

    internal static bool IsAbstractModifier(Node node)
    {
        return node.Kind == SyntaxKind.AbstractKeyword;
    }

    internal static bool IsOverrideModifier(Node node)
    {
        return node.Kind == SyntaxKind.OverrideKeyword;
    }

    internal static bool IsAccessorModifier(Node node)
    {
        return node.Kind == SyntaxKind.AccessorKeyword;
    }

    internal static bool IsSuperKeyword(Node node)
    {
        return node.Kind == SyntaxKind.SuperKeyword;
    }

    internal static bool IsImportKeyword(Node node)
    {
        return node.Kind == SyntaxKind.ImportKeyword;
    }

    internal static bool IsCaseKeyword(Node node)
    {
        return node.Kind == SyntaxKind.CaseKeyword;
    }

    internal static bool IsQualifiedName(Node node)
    {
        return node.Kind == SyntaxKind.QualifiedName;
    }

    internal static bool IsComputedPropertyName(Node node)
    {
        return node.Kind == SyntaxKind.ComputedPropertyName;
    }

    internal static bool IsTypeParameterDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.TypeParameter;
    }

    internal static bool IsParameter(Node node)
    {
        return node.Kind == SyntaxKind.Parameter;
    }

    internal static bool IsDecorator(Node node)
    {
        return node.Kind == SyntaxKind.Decorator;
    }

    internal static bool IsPropertySignature(Node node)
    {
        return node.Kind == SyntaxKind.PropertySignature;
    }

    internal static bool IsPropertyDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.PropertyDeclaration;
    }

    internal static bool IsMethodSignature(Node node)
    {
        return node.Kind == SyntaxKind.MethodSignature;
    }

    internal static bool IsMethodDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.MethodDeclaration;
    }

    internal static bool IsClassStaticBlockDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.ClassStaticBlockDeclaration;
    }

    internal static bool IsConstructorDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.Constructor;
    }

    internal static bool IsGetAccessorDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.GetAccessor;
    }

    internal static bool IsSetAccessorDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.SetAccessor;
    }

    internal static bool IsCallSignatureDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.CallSignature;
    }

    internal static bool IsConstructSignatureDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.ConstructSignature;
    }

    internal static bool IsIndexSignatureDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.IndexSignature;
    }

    internal static bool IsTypePredicateNode(Node node)
    {
        return node.Kind == SyntaxKind.TypePredicate;
    }

    internal static bool IsTypeReferenceNode(Node node)
    {
        return node.Kind == SyntaxKind.TypeReference;
    }

    internal static bool IsFunctionTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.FunctionType;
    }

    internal static bool IsConstructorTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.ConstructorType;
    }

    internal static bool IsTypeQueryNode(Node node)
    {
        return node.Kind == SyntaxKind.TypeQuery;
    }

    internal static bool IsTypeLiteralNode(Node node)
    {
        return node.Kind == SyntaxKind.TypeLiteral;
    }

    internal static bool IsArrayTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.ArrayType;
    }

    internal static bool IsTupleTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.TupleType;
    }

    internal static bool IsNamedTupleMember(Node node)
    {
        return node.Kind == SyntaxKind.NamedTupleMember;
    }

    internal static bool IsOptionalTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.OptionalType;
    }

    internal static bool IsRestTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.RestType;
    }

    internal static bool IsUnionTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.UnionType;
    }

    internal static bool IsIntersectionTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.IntersectionType;
    }

    internal static bool IsConditionalTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.ConditionalType;
    }

    internal static bool IsInferTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.InferType;
    }

    internal static bool IsParenthesizedTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.ParenthesizedType;
    }

    internal static bool IsThisTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.ThisType;
    }

    internal static bool IsTypeOperatorNode(Node node)
    {
        return node.Kind == SyntaxKind.TypeOperator;
    }

    internal static bool IsIndexedAccessTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.IndexedAccessType;
    }

    internal static bool IsMappedTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.MappedType;
    }

    internal static bool IsLiteralTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.LiteralType;
    }

    internal static bool IsImportTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.ImportType;
    }

    internal static bool IsTemplateLiteralTypeSpan(Node node)
    {
        return node.Kind == SyntaxKind.TemplateLiteralTypeSpan;
    }

    internal static bool IsTemplateLiteralTypeNode(Node node)
    {
        return node.Kind == SyntaxKind.TemplateLiteralType;
    }

    internal static bool IsObjectBindingPattern(Node node)
    {
        return node.Kind == SyntaxKind.ObjectBindingPattern;
    }

    internal static bool IsArrayBindingPattern(Node node)
    {
        return node.Kind == SyntaxKind.ArrayBindingPattern;
    }

    internal static bool IsBindingElement(Node node)
    {
        return node.Kind == SyntaxKind.BindingElement;
    }

    internal static bool IsArrayLiteralExpression(Node node)
    {
        return node.Kind == SyntaxKind.ArrayLiteralExpression;
    }

    internal static bool IsObjectLiteralExpression(Node node)
    {
        return node.Kind == SyntaxKind.ObjectLiteralExpression;
    }

    internal static bool IsPropertyAccessExpression(Node node)
    {
        return node.Kind == SyntaxKind.PropertyAccessExpression;
    }

    internal static bool IsElementAccessExpression(Node node)
    {
        return node.Kind == SyntaxKind.ElementAccessExpression;
    }

    internal static bool IsCallExpression(Node node)
    {
        return node.Kind == SyntaxKind.CallExpression;
    }

    internal static bool IsNewExpression(Node node)
    {
        return node.Kind == SyntaxKind.NewExpression;
    }

    internal static bool IsTaggedTemplateExpression(Node node)
    {
        return node.Kind == SyntaxKind.TaggedTemplateExpression;
    }

    internal static bool IsTypeAssertionExpression(Node node)
    {
        return node.Kind == SyntaxKind.TypeAssertionExpression;
    }

    internal static bool IsParenthesizedExpression(Node node)
    {
        return node.Kind == SyntaxKind.ParenthesizedExpression;
    }

    internal static bool IsFunctionExpression(Node node)
    {
        return node.Kind == SyntaxKind.FunctionExpression;
    }

    internal static bool IsArrowFunction(Node node)
    {
        return node.Kind == SyntaxKind.ArrowFunction;
    }

    internal static bool IsDeleteExpression(Node node)
    {
        return node.Kind == SyntaxKind.DeleteExpression;
    }

    internal static bool IsTypeOfExpression(Node node)
    {
        return node.Kind == SyntaxKind.TypeOfExpression;
    }

    internal static bool IsVoidExpression(Node node)
    {
        return node.Kind == SyntaxKind.VoidExpression;
    }

    internal static bool IsAwaitExpression(Node node)
    {
        return node.Kind == SyntaxKind.AwaitExpression;
    }

    internal static bool IsPrefixUnaryExpression(Node node)
    {
        return node.Kind == SyntaxKind.PrefixUnaryExpression;
    }

    internal static bool IsPostfixUnaryExpression(Node node)
    {
        return node.Kind == SyntaxKind.PostfixUnaryExpression;
    }

    internal static bool IsBinaryExpression(Node node)
    {
        return node.Kind == SyntaxKind.BinaryExpression;
    }

    internal static bool IsConditionalExpression(Node node)
    {
        return node.Kind == SyntaxKind.ConditionalExpression;
    }

    internal static bool IsTemplateExpression(Node node)
    {
        return node.Kind == SyntaxKind.TemplateExpression;
    }

    internal static bool IsYieldExpression(Node node)
    {
        return node.Kind == SyntaxKind.YieldExpression;
    }

    internal static bool IsSpreadElement(Node node)
    {
        return node.Kind == SyntaxKind.SpreadElement;
    }

    internal static bool IsClassExpression(Node node)
    {
        return node.Kind == SyntaxKind.ClassExpression;
    }

    internal static bool IsOmittedExpression(Node node)
    {
        return node.Kind == SyntaxKind.OmittedExpression;
    }

    internal static bool IsExpressionWithTypeArguments(Node node)
    {
        return node.Kind == SyntaxKind.ExpressionWithTypeArguments;
    }

    internal static bool IsAsExpression(Node node)
    {
        return node.Kind == SyntaxKind.AsExpression;
    }

    internal static bool IsSatisfiesExpression(Node node)
    {
        return node.Kind == SyntaxKind.SatisfiesExpression;
    }

    internal static bool IsNonNullExpression(Node node)
    {
        return node.Kind == SyntaxKind.NonNullExpression;
    }

    internal static bool IsMetaProperty(Node node)
    {
        return node.Kind == SyntaxKind.MetaProperty;
    }

    internal static bool IsSyntheticExpression(Node node)
    {
        return node.Kind == SyntaxKind.SyntheticExpression;
    }

    internal static bool IsPartiallyEmittedExpression(Node node)
    {
        return node.Kind == SyntaxKind.PartiallyEmittedExpression;
    }

    internal static bool IsCommaListExpression(Node node)
    {
        return node.Kind == SyntaxKind.CommaListExpression;
    }

    internal static bool IsTemplateSpan(Node node)
    {
        return node.Kind == SyntaxKind.TemplateSpan;
    }

    internal static bool IsSemicolonClassElement(Node node)
    {
        return node.Kind == SyntaxKind.SemicolonClassElement;
    }

    internal static bool IsBlock(Node node)
    {
        return node.Kind == SyntaxKind.Block;
    }

    internal static bool IsVariableStatement(Node node)
    {
        return node.Kind == SyntaxKind.VariableStatement;
    }

    internal static bool IsEmptyStatement(Node node)
    {
        return node.Kind == SyntaxKind.EmptyStatement;
    }

    internal static bool IsExpressionStatement(Node node)
    {
        return node.Kind == SyntaxKind.ExpressionStatement;
    }

    internal static bool IsIfStatement(Node node)
    {
        return node.Kind == SyntaxKind.IfStatement;
    }

    internal static bool IsDoStatement(Node node)
    {
        return node.Kind == SyntaxKind.DoStatement;
    }

    internal static bool IsWhileStatement(Node node)
    {
        return node.Kind == SyntaxKind.WhileStatement;
    }

    internal static bool IsForStatement(Node node)
    {
        return node.Kind == SyntaxKind.ForStatement;
    }

    internal static bool IsForInStatement(Node node)
    {
        return node.Kind == SyntaxKind.ForInStatement;
    }

    internal static bool IsForOfStatement(Node node)
    {
        return node.Kind == SyntaxKind.ForOfStatement;
    }

    internal static bool IsContinueStatement(Node node)
    {
        return node.Kind == SyntaxKind.ContinueStatement;
    }

    internal static bool IsBreakStatement(Node node)
    {
        return node.Kind == SyntaxKind.BreakStatement;
    }

    internal static bool IsReturnStatement(Node node)
    {
        return node.Kind == SyntaxKind.ReturnStatement;
    }

    internal static bool IsWithStatement(Node node)
    {
        return node.Kind == SyntaxKind.WithStatement;
    }

    internal static bool IsSwitchStatement(Node node)
    {
        return node.Kind == SyntaxKind.SwitchStatement;
    }

    internal static bool IsLabeledStatement(Node node)
    {
        return node.Kind == SyntaxKind.LabeledStatement;
    }

    internal static bool IsThrowStatement(Node node)
    {
        return node.Kind == SyntaxKind.ThrowStatement;
    }

    internal static bool IsTryStatement(Node node)
    {
        return node.Kind == SyntaxKind.TryStatement;
    }

    internal static bool IsDebuggerStatement(Node node)
    {
        return node.Kind == SyntaxKind.DebuggerStatement;
    }

    internal static bool IsVariableDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.VariableDeclaration;
    }

    internal static bool IsVariableDeclarationList(Node node)
    {
        return node.Kind == SyntaxKind.VariableDeclarationList;
    }

    internal static bool IsFunctionDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.FunctionDeclaration;
    }

    internal static bool IsClassDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.ClassDeclaration;
    }

    internal static bool IsInterfaceDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.InterfaceDeclaration;
    }

    internal static bool IsTypeAliasDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.TypeAliasDeclaration;
    }

    internal static bool IsEnumDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.EnumDeclaration;
    }

    internal static bool IsModuleDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.ModuleDeclaration;
    }

    internal static bool IsModuleBlock(Node node)
    {
        return node.Kind == SyntaxKind.ModuleBlock;
    }

    internal static bool IsCaseBlock(Node node)
    {
        return node.Kind == SyntaxKind.CaseBlock;
    }

    internal static bool IsNamespaceExportDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.NamespaceExportDeclaration;
    }

    internal static bool IsImportEqualsDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.ImportEqualsDeclaration;
    }

    internal static bool IsImportDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.ImportDeclaration;
    }

    internal static bool IsImportClause(Node node)
    {
        return node.Kind == SyntaxKind.ImportClause;
    }

    internal static bool IsImportTypeAssertionContainer(Node node)
    {
        return node.Kind == SyntaxKind.ImportTypeAssertionContainer;
    }

    internal static bool IsAssertClause(Node node)
    {
        return node.Kind == SyntaxKind.AssertClause;
    }

    internal static bool IsAssertEntry(Node node)
    {
        return node.Kind == SyntaxKind.AssertEntry;
    }

    internal static bool IsImportAttributes(Node node)
    {
        return node.Kind == SyntaxKind.ImportAttributes;
    }

    internal static bool IsImportAttribute(Node node)
    {
        return node.Kind == SyntaxKind.ImportAttribute;
    }

    internal static bool IsNamespaceImport(Node node)
    {
        return node.Kind == SyntaxKind.NamespaceImport;
    }

    internal static bool IsNamespaceExport(Node node)
    {
        return node.Kind == SyntaxKind.NamespaceExport;
    }

    internal static bool IsNamedImports(Node node)
    {
        return node.Kind == SyntaxKind.NamedImports;
    }

    internal static bool IsImportSpecifier(Node node)
    {
        return node.Kind == SyntaxKind.ImportSpecifier;
    }

    internal static bool IsExportAssignment(Node node)
    {
        return node.Kind == SyntaxKind.ExportAssignment;
    }

    internal static bool IsExportDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.ExportDeclaration;
    }

    internal static bool IsNamedExports(Node node)
    {
        return node.Kind == SyntaxKind.NamedExports;
    }

    internal static bool IsExportSpecifier(Node node)
    {
        return node.Kind == SyntaxKind.ExportSpecifier;
    }

    internal static bool IsModuleExportName(Node node)
    {
        return node.Kind == SyntaxKind.Identifier || node.Kind == SyntaxKind.StringLiteral;
    }

    internal static bool IsMissingDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.MissingDeclaration;
    }

    internal static bool IsNotEmittedStatement(Node node)
    {
        return node.Kind == SyntaxKind.NotEmittedStatement;
    }

    internal static bool IsSyntheticReference(Node node)
    {
        return node.Kind == SyntaxKind.SyntheticReferenceExpression;
    }

    internal static bool IsExternalModuleReference(Node node)
    {
        return node.Kind == SyntaxKind.ExternalModuleReference;
    }

    internal static bool IsJsxElement(Node node)
    {
        return node.Kind == SyntaxKind.JsxElement;
    }

    internal static bool IsJsxSelfClosingElement(Node node)
    {
        return node.Kind == SyntaxKind.JsxSelfClosingElement;
    }

    internal static bool IsJsxOpeningElement(Node node)
    {
        return node.Kind == SyntaxKind.JsxOpeningElement;
    }

    internal static bool IsJsxClosingElement(Node node)
    {
        return node.Kind == SyntaxKind.JsxClosingElement;
    }

    internal static bool IsJsxFragment(Node node)
    {
        return node.Kind == SyntaxKind.JsxFragment;
    }

    internal static bool IsJsxOpeningFragment(Node node)
    {
        return node.Kind == SyntaxKind.JsxOpeningFragment;
    }

    internal static bool IsJsxClosingFragment(Node node)
    {
        return node.Kind == SyntaxKind.JsxClosingFragment;
    }

    internal static bool IsJsxAttribute(Node node)
    {
        return node.Kind == SyntaxKind.JsxAttribute;
    }

    internal static bool IsJsxAttributes(Node node)
    {
        return node.Kind == SyntaxKind.JsxAttributes;
    }

    internal static bool IsJsxSpreadAttribute(Node node)
    {
        return node.Kind == SyntaxKind.JsxSpreadAttribute;
    }

    internal static bool IsJsxExpression(Node node)
    {
        return node.Kind == SyntaxKind.JsxExpression;
    }

    internal static bool IsJsxNamespacedName(Node node)
    {
        return node.Kind == SyntaxKind.JsxNamespacedName;
    }

    internal static bool IsCaseClause(Node node)
    {
        return node.Kind == SyntaxKind.CaseClause;
    }

    internal static bool IsDefaultClause(Node node)
    {
        return node.Kind == SyntaxKind.DefaultClause;
    }

    internal static bool IsHeritageClause(Node node)
    {
        return node.Kind == SyntaxKind.HeritageClause;
    }

    internal static bool IsCatchClause(Node node)
    {
        return node.Kind == SyntaxKind.CatchClause;
    }

    internal static bool IsPropertyAssignment(Node node)
    {
        return node.Kind == SyntaxKind.PropertyAssignment;
    }

    internal static bool IsShorthandPropertyAssignment(Node node)
    {
        return node.Kind == SyntaxKind.ShorthandPropertyAssignment;
    }

    internal static bool IsSpreadAssignment(Node node)
    {
        return node.Kind == SyntaxKind.SpreadAssignment;
    }

    internal static bool IsEnumMember(Node node)
    {
        return node.Kind == SyntaxKind.EnumMember;
    }

    internal static bool IsSourceFile(Node node)
    {
        return node.Kind == SyntaxKind.SourceFile;
    }

    internal static bool IsBundle(Node node)
    {
        return node.Kind == SyntaxKind.Bundle;
    }

    internal static bool IsJSDocTypeExpression(Node node)
    {
        return node.Kind == SyntaxKind.JSDocTypeExpression;
    }

    internal static bool IsJSDocNameReference(Node node)
    {
        return node.Kind == SyntaxKind.JSDocNameReference;
    }

    internal static bool IsJSDocMemberName(Node node)
    {
        return node.Kind == SyntaxKind.JSDocMemberName;
    }

    internal static bool IsJSDocLink(Node node)
    {
        return node.Kind == SyntaxKind.JSDocLink;
    }

    internal static bool IsJSDocLinkCode(Node node)
    {
        return node.Kind == SyntaxKind.JSDocLinkCode;
    }

    internal static bool IsJSDocLinkPlain(Node node)
    {
        return node.Kind == SyntaxKind.JSDocLinkPlain;
    }

    internal static bool IsJSDocAllType(Node node)
    {
        return node.Kind == SyntaxKind.JSDocAllType;
    }

    internal static bool IsJSDocUnknownType(Node node)
    {
        return node.Kind == SyntaxKind.JSDocUnknownType;
    }

    internal static bool IsJSDocNullableType(Node node)
    {
        return node.Kind == SyntaxKind.JSDocNullableType;
    }

    internal static bool IsJSDocNonNullableType(Node node)
    {
        return node.Kind == SyntaxKind.JSDocNonNullableType;
    }

    internal static bool IsJSDocOptionalType(Node node)
    {
        return node.Kind == SyntaxKind.JSDocOptionalType;
    }

    internal static bool IsJSDocFunctionType(Node node)
    {
        return node.Kind == SyntaxKind.JSDocFunctionType;
    }

    internal static bool IsJSDocVariadicType(Node node)
    {
        return node.Kind == SyntaxKind.JSDocVariadicType;
    }

    internal static bool IsJSDocNamepathType(Node node)
    {
        return node.Kind == SyntaxKind.JSDocNamepathType;
    }

    internal static bool IsJSDoc(Node node)
    {
        return node.Kind == SyntaxKind.JSDoc;
    }

    internal static bool IsJSDocTypeLiteral(Node node)
    {
        return node.Kind == SyntaxKind.JSDocTypeLiteral;
    }

    internal static bool IsJSDocSignature(Node node)
    {
        return node.Kind == SyntaxKind.JSDocSignature;
    }

    internal static bool IsJSDocAugmentsTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocAugmentsTag;
    }

    internal static bool IsJSDocAuthorTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocAuthorTag;
    }

    internal static bool IsJSDocClassTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocClassTag;
    }

    internal static bool IsJSDocCallbackTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocCallbackTag;
    }

    internal static bool IsJSDocPublicTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocPublicTag;
    }

    internal static bool IsJSDocPrivateTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocPrivateTag;
    }

    internal static bool IsJSDocProtectedTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocProtectedTag;
    }

    internal static bool IsJSDocReadonlyTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocReadonlyTag;
    }

    internal static bool IsJSDocOverrideTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocOverrideTag;
    }

    internal static bool IsJSDocOverloadTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocOverloadTag;
    }

    internal static bool IsJSDocDeprecatedTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocDeprecatedTag;
    }

    internal static bool IsJSDocSeeTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocSeeTag;
    }

    internal static bool IsJSDocEnumTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocEnumTag;
    }

    internal static bool IsJSDocParameterTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocParameterTag;
    }

    internal static bool IsJSDocReturnTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocReturnTag;
    }

    internal static bool IsJSDocThisTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocThisTag;
    }

    internal static bool IsJSDocTypeTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocTypeTag;
    }

    internal static bool IsJSDocTemplateTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocTemplateTag;
    }

    internal static bool IsJSDocTypedefTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocTypedefTag;
    }

    internal static bool IsJSDocUnknownTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocTag;
    }

    internal static bool IsJSDocPropertyTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocPropertyTag;
    }

    internal static bool IsJSDocImplementsTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocImplementsTag;
    }

    internal static bool IsJSDocSatisfiesTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocSatisfiesTag;
    }

    internal static bool IsJSDocThrowsTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocThrowsTag;
    }

    internal static bool IsJSDocImportTag(Node node)
    {
        return node.Kind == SyntaxKind.JSDocImportTag;
    }

    internal static bool IsSyntaxList(Node node)
    {
        return node.Kind == SyntaxKind.SyntaxList;
    }
}
