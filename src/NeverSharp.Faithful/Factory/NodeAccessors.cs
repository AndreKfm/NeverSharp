// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// The property reads TypeScript writes on a node of unknown kind, such as `(node as NamedDeclaration).name`
/// or `(node as HasModifiers).modifiers` (types.ts declares these properties on many interfaces). The C#
/// node classes declare their fields one by one, so the read is a switch over the kinds whose class declares
/// the field (typescript-go: the accessors `Node.Name()`, `Node.Modifiers()`, … in internal/ast/ast.go). A
/// kind whose class has no such field reads as undefined, as the property does in TypeScript.
/// </summary>
internal static class NodeAccessors
{
    /// <summary>TypeScript: `(node as NamedDeclaration).name` on any node.</summary>
    internal static Node? GetName(Node node)
    {
        return node.Kind switch
        {
            SyntaxKind.ArrowFunction => ((ArrowFunction)node).Name as Node,
            SyntaxKind.BindingElement => ((BindingElement)node).Name,
            SyntaxKind.CallSignature => ((CallSignatureDeclaration)node).Name,
            SyntaxKind.ClassDeclaration => ((ClassDeclaration)node).Name,
            SyntaxKind.ClassExpression => ((ClassExpression)node).Name,
            SyntaxKind.ClassStaticBlockDeclaration => ((ClassStaticBlockDeclaration)node).Name,
            SyntaxKind.ConstructSignature => ((ConstructSignatureDeclaration)node).Name,
            SyntaxKind.Constructor => ((ConstructorDeclaration)node).Name,
            SyntaxKind.ConstructorType => ((ConstructorTypeNode)node).Name,
            SyntaxKind.EnumDeclaration => ((EnumDeclaration)node).Name,
            SyntaxKind.EnumMember => ((EnumMember)node).Name,
            SyntaxKind.ExportAssignment => ((ExportAssignment)node).Name,
            SyntaxKind.ExportDeclaration => ((ExportDeclaration)node).Name,
            SyntaxKind.ExportSpecifier => ((ExportSpecifier)node).Name,
            SyntaxKind.FunctionDeclaration => ((FunctionDeclaration)node).Name,
            SyntaxKind.FunctionExpression => ((FunctionExpression)node).Name,
            SyntaxKind.FunctionType => ((FunctionTypeNode)node).Name,
            SyntaxKind.GetAccessor => ((GetAccessorDeclaration)node).Name,
            SyntaxKind.ImportAttribute => ((ImportAttribute)node).Name,
            SyntaxKind.ImportClause => ((ImportClause)node).Name,
            SyntaxKind.ImportEqualsDeclaration => ((ImportEqualsDeclaration)node).Name,
            SyntaxKind.ImportSpecifier => ((ImportSpecifier)node).Name,
            SyntaxKind.IndexSignature => ((IndexSignatureDeclaration)node).Name,
            SyntaxKind.InterfaceDeclaration => ((InterfaceDeclaration)node).Name,
            SyntaxKind.JSDocCallbackTag => ((JSDocCallbackTag)node).Name,
            SyntaxKind.JSDocFunctionType => ((JSDocFunctionType)node).Name,
            SyntaxKind.JSDocLink => ((JSDocLink)node).Name,
            SyntaxKind.JSDocLinkCode => ((JSDocLinkCode)node).Name,
            SyntaxKind.JSDocLinkPlain => ((JSDocLinkPlain)node).Name,
            SyntaxKind.JSDocNameReference => ((JSDocNameReference)node).Name,
            SyntaxKind.JSDocParameterTag => ((JSDocParameterTag)node).Name,
            SyntaxKind.JSDocPropertyTag => ((JSDocPropertyTag)node).Name,
            SyntaxKind.JSDocSeeTag => ((JSDocSeeTag)node).Name,
            SyntaxKind.JSDocTypedefTag => ((JSDocTypedefTag)node).Name,
            SyntaxKind.JsxAttribute => ((JsxAttribute)node).Name,
            SyntaxKind.JsxNamespacedName => ((JsxNamespacedName)node).Name,
            SyntaxKind.JsxSpreadAttribute => ((JsxSpreadAttribute)node).Name,
            SyntaxKind.MetaProperty => ((MetaProperty)node).Name,
            SyntaxKind.MethodDeclaration => ((MethodDeclaration)node).Name,
            SyntaxKind.MethodSignature => ((MethodSignature)node).Name,
            SyntaxKind.MissingDeclaration => ((MissingDeclaration)node).Name,
            SyntaxKind.ModuleDeclaration => ((ModuleDeclaration)node).Name,
            SyntaxKind.NamedTupleMember => ((NamedTupleMember)node).Name,
            SyntaxKind.NamespaceExport => ((NamespaceExport)node).Name,
            SyntaxKind.NamespaceExportDeclaration => ((NamespaceExportDeclaration)node).Name,
            SyntaxKind.NamespaceImport => ((NamespaceImport)node).Name,
            SyntaxKind.NotEmittedTypeElement => ((NotEmittedTypeElement)node).Name,
            SyntaxKind.Parameter => ((ParameterDeclaration)node).Name,
            SyntaxKind.PropertyAccessExpression => ((PropertyAccessExpression)node).Name,
            SyntaxKind.PropertyAssignment => ((PropertyAssignment)node).Name,
            SyntaxKind.PropertyDeclaration => ((PropertyDeclaration)node).Name,
            SyntaxKind.PropertySignature => ((PropertySignature)node).Name,
            SyntaxKind.SemicolonClassElement => ((SemicolonClassElement)node).Name,
            SyntaxKind.SetAccessor => ((SetAccessorDeclaration)node).Name,
            SyntaxKind.ShorthandPropertyAssignment => ((ShorthandPropertyAssignment)node).Name,
            SyntaxKind.SpreadAssignment => ((SpreadAssignment)node).Name,
            SyntaxKind.TypeAliasDeclaration => ((TypeAliasDeclaration)node).Name,
            SyntaxKind.TypeParameter => ((TypeParameterDeclaration)node).Name,
            SyntaxKind.VariableDeclaration => ((VariableDeclaration)node).Name,
            _ => null,
        };
    }

    /// <summary>TypeScript: `(node as HasModifiers).modifiers` on any node.</summary>
    internal static NodeArray? GetModifiers(Node node)
    {
        return node.Kind switch
        {
            SyntaxKind.ArrowFunction => ((ArrowFunction)node).Modifiers,
            SyntaxKind.ClassDeclaration => ((ClassDeclaration)node).Modifiers,
            SyntaxKind.ClassExpression => ((ClassExpression)node).Modifiers,
            SyntaxKind.ClassStaticBlockDeclaration => ((ClassStaticBlockDeclaration)node).Modifiers,
            SyntaxKind.Constructor => ((ConstructorDeclaration)node).Modifiers,
            SyntaxKind.ConstructorType => ((ConstructorTypeNode)node).Modifiers,
            SyntaxKind.EnumDeclaration => ((EnumDeclaration)node).Modifiers,
            SyntaxKind.ExportAssignment => ((ExportAssignment)node).Modifiers,
            SyntaxKind.ExportDeclaration => ((ExportDeclaration)node).Modifiers,
            SyntaxKind.FunctionDeclaration => ((FunctionDeclaration)node).Modifiers,
            SyntaxKind.FunctionExpression => ((FunctionExpression)node).Modifiers,
            SyntaxKind.FunctionType => ((FunctionTypeNode)node).Modifiers,
            SyntaxKind.GetAccessor => ((GetAccessorDeclaration)node).Modifiers,
            SyntaxKind.ImportDeclaration => ((ImportDeclaration)node).Modifiers,
            SyntaxKind.ImportEqualsDeclaration => ((ImportEqualsDeclaration)node).Modifiers,
            SyntaxKind.IndexSignature => ((IndexSignatureDeclaration)node).Modifiers,
            SyntaxKind.InterfaceDeclaration => ((InterfaceDeclaration)node).Modifiers,
            SyntaxKind.MethodDeclaration => ((MethodDeclaration)node).Modifiers,
            SyntaxKind.MethodSignature => ((MethodSignature)node).Modifiers,
            SyntaxKind.MissingDeclaration => ((MissingDeclaration)node).Modifiers,
            SyntaxKind.ModuleDeclaration => ((ModuleDeclaration)node).Modifiers,
            SyntaxKind.NamespaceExportDeclaration => ((NamespaceExportDeclaration)node).Modifiers,
            SyntaxKind.Parameter => ((ParameterDeclaration)node).Modifiers,
            SyntaxKind.PropertyAssignment => ((PropertyAssignment)node).Modifiers,
            SyntaxKind.PropertyDeclaration => ((PropertyDeclaration)node).Modifiers,
            SyntaxKind.PropertySignature => ((PropertySignature)node).Modifiers,
            SyntaxKind.SetAccessor => ((SetAccessorDeclaration)node).Modifiers,
            SyntaxKind.ShorthandPropertyAssignment => ((ShorthandPropertyAssignment)node).Modifiers,
            SyntaxKind.TypeAliasDeclaration => ((TypeAliasDeclaration)node).Modifiers,
            SyntaxKind.TypeParameter => ((TypeParameterDeclaration)node).Modifiers,
            SyntaxKind.VariableStatement => ((VariableStatement)node).Modifiers,
            _ => null,
        };
    }

    /// <summary>TypeScript: `(node as HasInitializer).initializer` on any node.</summary>
    internal static Node? GetInitializer(Node node)
    {
        return node.Kind switch
        {
            SyntaxKind.BindingElement => ((BindingElement)node).Initializer,
            SyntaxKind.EnumMember => ((EnumMember)node).Initializer,
            SyntaxKind.ForInStatement => ((ForInStatement)node).Initializer,
            SyntaxKind.ForOfStatement => ((ForOfStatement)node).Initializer,
            SyntaxKind.ForStatement => ((ForStatement)node).Initializer,
            SyntaxKind.JsxAttribute => ((JsxAttribute)node).Initializer,
            SyntaxKind.Parameter => ((ParameterDeclaration)node).Initializer,
            SyntaxKind.PropertyAssignment => ((PropertyAssignment)node).Initializer,
            SyntaxKind.PropertyDeclaration => ((PropertyDeclaration)node).Initializer,
            SyntaxKind.PropertySignature => ((PropertySignature)node).Initializer,
            SyntaxKind.VariableDeclaration => ((VariableDeclaration)node).Initializer,
            _ => null,
        };
    }

    /// <summary>TypeScript: `(node as HasType).type` on any node.</summary>
    internal static Node? GetTypeNode(Node node)
    {
        return node.Kind switch
        {
            SyntaxKind.ArrowFunction => ((ArrowFunction)node).Type,
            SyntaxKind.AsExpression => ((AsExpression)node).Type,
            SyntaxKind.CallSignature => ((CallSignatureDeclaration)node).Type,
            SyntaxKind.ConstructSignature => ((ConstructSignatureDeclaration)node).Type,
            SyntaxKind.Constructor => ((ConstructorDeclaration)node).Type,
            SyntaxKind.ConstructorType => ((ConstructorTypeNode)node).Type,
            SyntaxKind.FunctionDeclaration => ((FunctionDeclaration)node).Type,
            SyntaxKind.FunctionExpression => ((FunctionExpression)node).Type,
            SyntaxKind.FunctionType => ((FunctionTypeNode)node).Type,
            SyntaxKind.GetAccessor => ((GetAccessorDeclaration)node).Type,
            SyntaxKind.IndexSignature => ((IndexSignatureDeclaration)node).Type,
            SyntaxKind.JSDocFunctionType => ((JSDocFunctionType)node).Type,
            SyntaxKind.JSDocNamepathType => ((JSDocNamepathType)node).Type,
            SyntaxKind.JSDocNonNullableType => ((JSDocNonNullableType)node).Type,
            SyntaxKind.JSDocNullableType => ((JSDocNullableType)node).Type,
            SyntaxKind.JSDocOptionalType => ((JSDocOptionalType)node).Type,
            SyntaxKind.JSDocSignature => ((JSDocSignature)node).Type,
            SyntaxKind.JSDocTypeExpression => ((JSDocTypeExpression)node).Type,
            SyntaxKind.JSDocVariadicType => ((JSDocVariadicType)node).Type,
            SyntaxKind.MappedType => ((MappedTypeNode)node).Type,
            SyntaxKind.MethodDeclaration => ((MethodDeclaration)node).Type,
            SyntaxKind.MethodSignature => ((MethodSignature)node).Type,
            SyntaxKind.NamedTupleMember => ((NamedTupleMember)node).Type,
            SyntaxKind.OptionalType => ((OptionalTypeNode)node).Type,
            SyntaxKind.Parameter => ((ParameterDeclaration)node).Type,
            SyntaxKind.ParenthesizedType => ((ParenthesizedTypeNode)node).Type,
            SyntaxKind.PropertyDeclaration => ((PropertyDeclaration)node).Type,
            SyntaxKind.PropertySignature => ((PropertySignature)node).Type,
            SyntaxKind.RestType => ((RestTypeNode)node).Type,
            SyntaxKind.SatisfiesExpression => ((SatisfiesExpression)node).Type,
            SyntaxKind.SetAccessor => ((SetAccessorDeclaration)node).Type,
            SyntaxKind.TemplateLiteralTypeSpan => ((TemplateLiteralTypeSpan)node).Type,
            SyntaxKind.TypeAliasDeclaration => ((TypeAliasDeclaration)node).Type,
            SyntaxKind.TypeAssertionExpression => ((TypeAssertion)node).Type,
            SyntaxKind.TypeOperator => ((TypeOperatorNode)node).Type,
            SyntaxKind.TypePredicate => ((TypePredicateNode)node).Type,
            SyntaxKind.VariableDeclaration => ((VariableDeclaration)node).Type,
            _ => null,
        };
    }

    /// <summary>TypeScript: `(node as DeclarationWithTypeParameterChildren).typeParameters` on any node.</summary>
    internal static NodeArray? GetTypeParameters(Node node)
    {
        return node.Kind switch
        {
            SyntaxKind.ArrowFunction => ((ArrowFunction)node).TypeParameters,
            SyntaxKind.CallSignature => ((CallSignatureDeclaration)node).TypeParameters,
            SyntaxKind.ClassDeclaration => ((ClassDeclaration)node).TypeParameters,
            SyntaxKind.ClassExpression => ((ClassExpression)node).TypeParameters,
            SyntaxKind.ConstructSignature => ((ConstructSignatureDeclaration)node).TypeParameters,
            SyntaxKind.Constructor => ((ConstructorDeclaration)node).TypeParameters,
            SyntaxKind.ConstructorType => ((ConstructorTypeNode)node).TypeParameters,
            SyntaxKind.FunctionDeclaration => ((FunctionDeclaration)node).TypeParameters,
            SyntaxKind.FunctionExpression => ((FunctionExpression)node).TypeParameters,
            SyntaxKind.FunctionType => ((FunctionTypeNode)node).TypeParameters,
            SyntaxKind.GetAccessor => ((GetAccessorDeclaration)node).TypeParameters,
            SyntaxKind.IndexSignature => ((IndexSignatureDeclaration)node).TypeParameters,
            SyntaxKind.InterfaceDeclaration => ((InterfaceDeclaration)node).TypeParameters,
            SyntaxKind.JSDocFunctionType => ((JSDocFunctionType)node).TypeParameters,
            SyntaxKind.JSDocTemplateTag => ((JSDocTemplateTag)node).TypeParameters,
            SyntaxKind.MethodDeclaration => ((MethodDeclaration)node).TypeParameters,
            SyntaxKind.MethodSignature => ((MethodSignature)node).TypeParameters,
            SyntaxKind.SetAccessor => ((SetAccessorDeclaration)node).TypeParameters,
            SyntaxKind.TypeAliasDeclaration => ((TypeAliasDeclaration)node).TypeParameters,
            _ => null,
        };
    }
}
