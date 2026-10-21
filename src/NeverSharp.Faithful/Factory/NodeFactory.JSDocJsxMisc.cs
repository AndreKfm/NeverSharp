// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

// nodeFactory.ts lines 5063-6290: JSDoc types and tags, JSX, clauses, property assignments, enum member,
// source file (create/clone/update), partially emitted expression.
internal sealed partial class NodeFactory
{
    //
    // JSDoc
    //

    // createJSDocAllType
    // createJSDocUnknownType
    private T CreateJSDocPrimaryTypeWorker<T>(SyntaxKind kind)
        where T : Node
    {
        return CreateBaseNode<T>(kind);
    }

    // createJSDocNullableType
    // createJSDocNonNullableType
    private T CreateJSDocPrePostfixUnaryTypeWorker<T>(SyntaxKind kind, Node? type, bool postfix = false)
        where T : Node
    {
        T node = CreateJSDocUnaryTypeWorker<T>(
            kind,
            postfix ? (type != null ? parenthesizerRules.ParenthesizeNonArrayTypeOfPostfixType(type) : null) : type);
        switch (node)
        {
            case JSDocNullableType nullable:
                nullable.Postfix = postfix;
                break;
            case JSDocNonNullableType nonNullable:
                nonNullable.Postfix = postfix;
                break;
            default:
                Debug.Fail("Unexpected JSDoc pre/postfix unary type kind.");
                break;
        }

        return node;
    }

    // createJSDocOptionalType
    // createJSDocVariadicType
    // createJSDocNamepathType
    private T CreateJSDocUnaryTypeWorker<T>(SyntaxKind kind, Node? type)
        where T : Node
    {
        T node = CreateBaseNode<T>(kind);
        SetJSDocUnaryTypeType(node, type);
        return node;
    }

    // updateJSDocNonNullableType
    // updateJSDocNullableType
    private T UpdateJSDocPrePostfixUnaryTypeWorker<T>(SyntaxKind kind, T node, Node? type)
        where T : Node
    {
        return GetJSDocUnaryTypeType(node) != type
            ? Update(CreateJSDocPrePostfixUnaryTypeWorker<T>(kind, type, GetJSDocTypePostfix(node)), node)
            : node;
    }

    // updateJSDocOptionalType
    // updateJSDocVariadicType
    // updateJSDocNamepathType
    private T UpdateJSDocUnaryTypeWorker<T>(SyntaxKind kind, T node, Node? type)
        where T : Node
    {
        return GetJSDocUnaryTypeType(node) != type
            ? Update(CreateJSDocUnaryTypeWorker<T>(kind, type), node)
            : node;
    }

    // The per-kind creators/updaters that TypeScript creates lazily (getJSDocPrimaryTypeCreateFunction etc.).
    internal JSDocAllType CreateJSDocAllType()
    {
        return CreateJSDocPrimaryTypeWorker<JSDocAllType>(SyntaxKind.JSDocAllType);
    }

    internal JSDocUnknownType CreateJSDocUnknownType()
    {
        return CreateJSDocPrimaryTypeWorker<JSDocUnknownType>(SyntaxKind.JSDocUnknownType);
    }

    internal JSDocNonNullableType CreateJSDocNonNullableType(Node type, bool postfix = false)
    {
        return CreateJSDocPrePostfixUnaryTypeWorker<JSDocNonNullableType>(SyntaxKind.JSDocNonNullableType, type, postfix);
    }

    internal JSDocNonNullableType UpdateJSDocNonNullableType(JSDocNonNullableType node, Node type)
    {
        return UpdateJSDocPrePostfixUnaryTypeWorker(SyntaxKind.JSDocNonNullableType, node, type);
    }

    internal JSDocNullableType CreateJSDocNullableType(Node type, bool postfix = false)
    {
        return CreateJSDocPrePostfixUnaryTypeWorker<JSDocNullableType>(SyntaxKind.JSDocNullableType, type, postfix);
    }

    internal JSDocNullableType UpdateJSDocNullableType(JSDocNullableType node, Node type)
    {
        return UpdateJSDocPrePostfixUnaryTypeWorker(SyntaxKind.JSDocNullableType, node, type);
    }

    internal JSDocOptionalType CreateJSDocOptionalType(Node type)
    {
        return CreateJSDocUnaryTypeWorker<JSDocOptionalType>(SyntaxKind.JSDocOptionalType, type);
    }

    internal JSDocOptionalType UpdateJSDocOptionalType(JSDocOptionalType node, Node type)
    {
        return UpdateJSDocUnaryTypeWorker(SyntaxKind.JSDocOptionalType, node, type);
    }

    internal JSDocVariadicType CreateJSDocVariadicType(Node type)
    {
        return CreateJSDocUnaryTypeWorker<JSDocVariadicType>(SyntaxKind.JSDocVariadicType, type);
    }

    internal JSDocVariadicType UpdateJSDocVariadicType(JSDocVariadicType node, Node type)
    {
        return UpdateJSDocUnaryTypeWorker(SyntaxKind.JSDocVariadicType, node, type);
    }

    internal JSDocNamepathType CreateJSDocNamepathType(Node type)
    {
        return CreateJSDocUnaryTypeWorker<JSDocNamepathType>(SyntaxKind.JSDocNamepathType, type);
    }

    internal JSDocNamepathType UpdateJSDocNamepathType(JSDocNamepathType node, Node type)
    {
        return UpdateJSDocUnaryTypeWorker(SyntaxKind.JSDocNamepathType, node, type);
    }

    internal JSDocFunctionType CreateJSDocFunctionType(IReadOnlyList<Node> parameters, Node? type)
    {
        var node = CreateBaseDeclaration<JSDocFunctionType>(SyntaxKind.JSDocFunctionType);
        node.Parameters = AsNodeArray(parameters)!;
        node.Type = type;
        node.TransformFlags = PropagateChildrenFlags(node.Parameters) |
            (node.Type != null ? TransformFlags.ContainsTypeScript : TransformFlags.None);

        // node.jsDoc, locals, nextContainer, typeArguments: undefined (fields default to null)
        return node;
    }

    internal JSDocTypeLiteral CreateJSDocTypeLiteral(IReadOnlyList<Node>? propertyTags = null, bool isArrayType = false)
    {
        var node = CreateBaseDeclaration<JSDocTypeLiteral>(SyntaxKind.JSDocTypeLiteral);
        // C# deviation: the generated field is a Node[]? (not a NodeArray).
        node.JsDocPropertyTags = AsNodeArray(propertyTags)?.ToArray();
        node.IsArrayType = isArrayType;
        return node;
    }

    internal JSDocTypeExpression CreateJSDocTypeExpression(Node type)
    {
        var node = CreateBaseNode<JSDocTypeExpression>(SyntaxKind.JSDocTypeExpression);
        node.Type = type;
        return node;
    }

    internal JSDocSignature CreateJSDocSignature(IReadOnlyList<Node>? typeParameters, IReadOnlyList<Node> parameters, JSDocReturnTag? type = null)
    {
        var node = CreateBaseDeclaration<JSDocSignature>(SyntaxKind.JSDocSignature);
        // C# deviation: the generated fields are arrays (JSDocTemplateTag[]? / JSDocParameterTag[]?), not NodeArrays.
        node.TypeParameters = CastNodes<JSDocTemplateTag>(AsNodeArray(typeParameters));
        node.Parameters = CastNodes<JSDocParameterTag>(CreateNodeArray(parameters));
        node.Type = type!;

        // node.jsDoc, locals, nextContainer: undefined (fields default to null)
        return node;
    }

    private static T[]? CastNodes<T>(NodeArray? array)
        where T : Node
    {
        if (array == null)
        {
            return null;
        }

        var result = new T[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            result[i] = (T)array[i];
        }

        return result;
    }

    private Identifier GetDefaultTagName(Node node)
    {
        string defaultTagName = GetDefaultTagNameForKind(node.Kind);
        Identifier tagName = GetJSDocTagName(node);
        return (string)tagName.EscapedText! == UtilitiesPublic.EscapeLeadingUnderscores(defaultTagName)
            ? tagName
            : CreateIdentifier(defaultTagName);
    }

    private T CreateBaseJSDocTag<T>(SyntaxKind kind, Identifier tagName, object? comment)
        where T : Node
    {
        T node = CreateBaseNode<T>(kind);
        SetJSDocTagNameAndComment(node, tagName, comment);
        return node;
    }

    private T CreateBaseJSDocTagDeclaration<T>(SyntaxKind kind, Identifier tagName, object? comment)
        where T : Node
    {
        T node = CreateBaseDeclaration<T>(kind);
        SetJSDocTagNameAndComment(node, tagName, comment);
        return node;
    }

    internal JSDocTemplateTag CreateJSDocTemplateTag(Identifier? tagName, JSDocTypeExpression? constraint, IReadOnlyList<Node> typeParameters, object? comment = null)
    {
        var node = CreateBaseJSDocTag<JSDocTemplateTag>(SyntaxKind.JSDocTemplateTag, tagName ?? CreateIdentifier("template"), comment);
        node.Constraint = constraint!;
        node.TypeParameters = CreateNodeArray(typeParameters);
        return node;
    }

    internal JSDocTypedefTag CreateJSDocTypedefTag(Identifier? tagName, Node? typeExpression = null, Node? fullName = null, object? comment = null)
    {
        var node = CreateBaseJSDocTagDeclaration<JSDocTypedefTag>(SyntaxKind.JSDocTypedefTag, tagName ?? CreateIdentifier("typedef"), comment);
        node.TypeExpression = typeExpression;
        node.FullName = fullName;
        node.Name = FactoryUtilities.GetJSDocTypeAliasName(fullName);

        // node.locals, nextContainer: undefined (fields default to null)
        return node;
    }

    internal JSDocParameterTag CreateJSDocParameterTag(Identifier? tagName, Node name, bool isBracketed, JSDocTypeExpression? typeExpression = null, bool? isNameFirst = null, object? comment = null)
    {
        var node = CreateBaseJSDocTagDeclaration<JSDocParameterTag>(SyntaxKind.JSDocParameterTag, tagName ?? CreateIdentifier("param"), comment);
        node.TypeExpression = typeExpression;
        node.Name = name;
        node.IsNameFirst = isNameFirst == true;
        node.IsBracketed = isBracketed;
        return node;
    }

    internal JSDocPropertyTag CreateJSDocPropertyTag(Identifier? tagName, Node name, bool isBracketed, JSDocTypeExpression? typeExpression = null, bool? isNameFirst = null, object? comment = null)
    {
        var node = CreateBaseJSDocTagDeclaration<JSDocPropertyTag>(SyntaxKind.JSDocPropertyTag, tagName ?? CreateIdentifier("prop"), comment);
        node.TypeExpression = typeExpression;
        node.Name = name;
        node.IsNameFirst = isNameFirst == true;
        node.IsBracketed = isBracketed;
        return node;
    }

    internal JSDocCallbackTag CreateJSDocCallbackTag(Identifier? tagName, JSDocSignature typeExpression, Node? fullName = null, object? comment = null)
    {
        var node = CreateBaseJSDocTagDeclaration<JSDocCallbackTag>(SyntaxKind.JSDocCallbackTag, tagName ?? CreateIdentifier("callback"), comment);
        node.TypeExpression = typeExpression;
        node.FullName = fullName;
        node.Name = FactoryUtilities.GetJSDocTypeAliasName(fullName);

        // node.locals, nextContainer: undefined (fields default to null)
        return node;
    }

    internal JSDocOverloadTag CreateJSDocOverloadTag(Identifier? tagName, JSDocSignature typeExpression, object? comment = null)
    {
        var node = CreateBaseJSDocTag<JSDocOverloadTag>(SyntaxKind.JSDocOverloadTag, tagName ?? CreateIdentifier("overload"), comment);
        node.TypeExpression = typeExpression;
        return node;
    }

    internal JSDocAugmentsTag CreateJSDocAugmentsTag(Identifier? tagName, Node className, object? comment = null)
    {
        var node = CreateBaseJSDocTag<JSDocAugmentsTag>(SyntaxKind.JSDocAugmentsTag, tagName ?? CreateIdentifier("augments"), comment);
        node.Class = className;
        return node;
    }

    internal JSDocImplementsTag CreateJSDocImplementsTag(Identifier? tagName, Node className, object? comment = null)
    {
        var node = CreateBaseJSDocTag<JSDocImplementsTag>(SyntaxKind.JSDocImplementsTag, tagName ?? CreateIdentifier("implements"), comment);
        node.Class = className;
        return node;
    }

    internal JSDocSeeTag CreateJSDocSeeTag(Identifier? tagName, JSDocNameReference? name, object? comment = null)
    {
        var node = CreateBaseJSDocTag<JSDocSeeTag>(SyntaxKind.JSDocSeeTag, tagName ?? CreateIdentifier("see"), comment);
        node.Name = name;
        return node;
    }

    internal JSDocNameReference CreateJSDocNameReference(Node name)
    {
        var node = CreateBaseNode<JSDocNameReference>(SyntaxKind.JSDocNameReference);
        node.Name = name;
        return node;
    }

    internal JSDocMemberName CreateJSDocMemberName(Node left, Identifier right)
    {
        var node = CreateBaseNode<JSDocMemberName>(SyntaxKind.JSDocMemberName);
        node.Left = left;
        node.Right = right;
        node.TransformFlags |= PropagateChildFlags(node.Left) |
            PropagateChildFlags(node.Right);
        return node;
    }

    internal JSDocLink CreateJSDocLink(Node? name, string text)
    {
        var node = CreateBaseNode<JSDocLink>(SyntaxKind.JSDocLink);
        node.Name = name;
        node.Text = text;
        return node;
    }

    internal JSDocLinkCode CreateJSDocLinkCode(Node? name, string text)
    {
        var node = CreateBaseNode<JSDocLinkCode>(SyntaxKind.JSDocLinkCode);
        node.Name = name;
        node.Text = text;
        return node;
    }

    internal JSDocLinkPlain CreateJSDocLinkPlain(Node? name, string text)
    {
        var node = CreateBaseNode<JSDocLinkPlain>(SyntaxKind.JSDocLinkPlain);
        node.Name = name;
        node.Text = text;
        return node;
    }

    // createJSDocAuthorTag
    // createJSDocClassTag
    // createJSDocPublicTag
    // createJSDocPrivateTag
    // createJSDocProtectedTag
    // createJSDocReadonlyTag
    // createJSDocDeprecatedTag
    private T CreateJSDocSimpleTagWorker<T>(SyntaxKind kind, Identifier? tagName, object? comment = null)
        where T : Node
    {
        T node = CreateBaseJSDocTag<T>(kind, tagName ?? CreateIdentifier(GetDefaultTagNameForKind(kind)), comment);
        return node;
    }

    // updateJSDocAuthorTag
    // updateJSDocClassTag
    // updateJSDocPublicTag
    // updateJSDocPrivateTag
    // updateJSDocProtectedTag
    // updateJSDocReadonlyTag
    // updateJSDocDeprecatedTag
    private T UpdateJSDocSimpleTagWorker<T>(SyntaxKind kind, T node, Identifier? tagName, object? comment)
        where T : Node
    {
        tagName ??= GetDefaultTagName(node);
        return GetJSDocTagName(node) != tagName
                || !Equals(GetJSDocTagComment(node), comment)
            ? Update(CreateJSDocSimpleTagWorker<T>(kind, tagName, comment), node)
            : node;
    }

    // createJSDocTypeTag
    // createJSDocReturnTag
    // createJSDocThisTag
    // createJSDocEnumTag
    // createJSDocSatisfiesTag
    private T CreateJSDocTypeLikeTagWorker<T>(SyntaxKind kind, Identifier? tagName, JSDocTypeExpression? typeExpression = null, object? comment = null)
        where T : Node
    {
        T node = CreateBaseJSDocTag<T>(kind, tagName ?? CreateIdentifier(GetDefaultTagNameForKind(kind)), comment);
        SetJSDocTypeLikeTagTypeExpression(node, typeExpression);
        return node;
    }

    // updateJSDocTypeTag
    // updateJSDocReturnTag
    // updateJSDocThisTag
    // updateJSDocEnumTag
    // updateJSDocSatisfiesTag
    private T UpdateJSDocTypeLikeTagWorker<T>(SyntaxKind kind, T node, Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment)
        where T : Node
    {
        tagName ??= GetDefaultTagName(node);
        return GetJSDocTagName(node) != tagName
                || GetJSDocTypeLikeTagTypeExpression(node) != typeExpression
                || !Equals(GetJSDocTagComment(node), comment)
            ? Update(CreateJSDocTypeLikeTagWorker<T>(kind, tagName, typeExpression, comment), node)
            : node;
    }

    // Per-kind creators/updaters that TypeScript creates lazily (getJSDocSimpleTagCreateFunction etc.).
    internal JSDocAuthorTag CreateJSDocAuthorTag(Identifier? tagName, object? comment = null)
    {
        return CreateJSDocSimpleTagWorker<JSDocAuthorTag>(SyntaxKind.JSDocAuthorTag, tagName, comment);
    }

    internal JSDocAuthorTag UpdateJSDocAuthorTag(JSDocAuthorTag node, Identifier? tagName, object? comment)
    {
        return UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocAuthorTag, node, tagName, comment);
    }

    internal JSDocClassTag CreateJSDocClassTag(Identifier? tagName, object? comment = null)
    {
        return CreateJSDocSimpleTagWorker<JSDocClassTag>(SyntaxKind.JSDocClassTag, tagName, comment);
    }

    internal JSDocClassTag UpdateJSDocClassTag(JSDocClassTag node, Identifier? tagName, object? comment)
    {
        return UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocClassTag, node, tagName, comment);
    }

    internal JSDocPublicTag CreateJSDocPublicTag(Identifier? tagName, object? comment = null)
    {
        return CreateJSDocSimpleTagWorker<JSDocPublicTag>(SyntaxKind.JSDocPublicTag, tagName, comment);
    }

    internal JSDocPublicTag UpdateJSDocPublicTag(JSDocPublicTag node, Identifier? tagName, object? comment)
    {
        return UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocPublicTag, node, tagName, comment);
    }

    internal JSDocPrivateTag CreateJSDocPrivateTag(Identifier? tagName, object? comment = null)
    {
        return CreateJSDocSimpleTagWorker<JSDocPrivateTag>(SyntaxKind.JSDocPrivateTag, tagName, comment);
    }

    internal JSDocPrivateTag UpdateJSDocPrivateTag(JSDocPrivateTag node, Identifier? tagName, object? comment)
    {
        return UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocPrivateTag, node, tagName, comment);
    }

    internal JSDocProtectedTag CreateJSDocProtectedTag(Identifier? tagName, object? comment = null)
    {
        return CreateJSDocSimpleTagWorker<JSDocProtectedTag>(SyntaxKind.JSDocProtectedTag, tagName, comment);
    }

    internal JSDocProtectedTag UpdateJSDocProtectedTag(JSDocProtectedTag node, Identifier? tagName, object? comment)
    {
        return UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocProtectedTag, node, tagName, comment);
    }

    internal JSDocReadonlyTag CreateJSDocReadonlyTag(Identifier? tagName, object? comment = null)
    {
        return CreateJSDocSimpleTagWorker<JSDocReadonlyTag>(SyntaxKind.JSDocReadonlyTag, tagName, comment);
    }

    internal JSDocReadonlyTag UpdateJSDocReadonlyTag(JSDocReadonlyTag node, Identifier? tagName, object? comment)
    {
        return UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocReadonlyTag, node, tagName, comment);
    }

    internal JSDocOverrideTag CreateJSDocOverrideTag(Identifier? tagName, object? comment = null)
    {
        return CreateJSDocSimpleTagWorker<JSDocOverrideTag>(SyntaxKind.JSDocOverrideTag, tagName, comment);
    }

    internal JSDocOverrideTag UpdateJSDocOverrideTag(JSDocOverrideTag node, Identifier? tagName, object? comment = null)
    {
        return UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocOverrideTag, node, tagName, comment);
    }

    internal JSDocDeprecatedTag CreateJSDocDeprecatedTag(Identifier? tagName, object? comment = null)
    {
        return CreateJSDocSimpleTagWorker<JSDocDeprecatedTag>(SyntaxKind.JSDocDeprecatedTag, tagName, comment);
    }

    internal JSDocDeprecatedTag UpdateJSDocDeprecatedTag(JSDocDeprecatedTag node, Identifier? tagName, object? comment = null)
    {
        return UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocDeprecatedTag, node, tagName, comment);
    }

    internal JSDocTypeTag CreateJSDocTypeTag(Identifier? tagName, JSDocTypeExpression typeExpression, object? comment = null)
    {
        return CreateJSDocTypeLikeTagWorker<JSDocTypeTag>(SyntaxKind.JSDocTypeTag, tagName, typeExpression, comment);
    }

    internal JSDocTypeTag UpdateJSDocTypeTag(JSDocTypeTag node, Identifier? tagName, JSDocTypeExpression typeExpression, object? comment)
    {
        return UpdateJSDocTypeLikeTagWorker(SyntaxKind.JSDocTypeTag, node, tagName, typeExpression, comment);
    }

    internal JSDocReturnTag CreateJSDocReturnTag(Identifier? tagName, JSDocTypeExpression? typeExpression = null, object? comment = null)
    {
        return CreateJSDocTypeLikeTagWorker<JSDocReturnTag>(SyntaxKind.JSDocReturnTag, tagName, typeExpression, comment);
    }

    internal JSDocReturnTag UpdateJSDocReturnTag(JSDocReturnTag node, Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment)
    {
        return UpdateJSDocTypeLikeTagWorker(SyntaxKind.JSDocReturnTag, node, tagName, typeExpression, comment);
    }

    internal JSDocThisTag CreateJSDocThisTag(Identifier? tagName, JSDocTypeExpression typeExpression, object? comment = null)
    {
        return CreateJSDocTypeLikeTagWorker<JSDocThisTag>(SyntaxKind.JSDocThisTag, tagName, typeExpression, comment);
    }

    internal JSDocThisTag UpdateJSDocThisTag(JSDocThisTag node, Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment)
    {
        return UpdateJSDocTypeLikeTagWorker(SyntaxKind.JSDocThisTag, node, tagName, typeExpression, comment);
    }

    internal JSDocThrowsTag CreateJSDocThrowsTag(Identifier tagName, JSDocTypeExpression? typeExpression, object? comment = null)
    {
        return CreateJSDocTypeLikeTagWorker<JSDocThrowsTag>(SyntaxKind.JSDocThrowsTag, tagName, typeExpression, comment);
    }

    internal JSDocThrowsTag UpdateJSDocThrowsTag(JSDocThrowsTag node, Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment = null)
    {
        return UpdateJSDocTypeLikeTagWorker(SyntaxKind.JSDocThrowsTag, node, tagName, typeExpression, comment);
    }

    internal JSDocSatisfiesTag CreateJSDocSatisfiesTag(Identifier? tagName, JSDocTypeExpression typeExpression, object? comment = null)
    {
        return CreateJSDocTypeLikeTagWorker<JSDocSatisfiesTag>(SyntaxKind.JSDocSatisfiesTag, tagName, typeExpression, comment);
    }

    internal JSDocSatisfiesTag UpdateJSDocSatisfiesTag(JSDocSatisfiesTag node, Identifier? tagName, JSDocTypeExpression typeExpression, object? comment)
    {
        return UpdateJSDocTypeLikeTagWorker(SyntaxKind.JSDocSatisfiesTag, node, tagName, typeExpression, comment);
    }

    internal JSDocUnknownTag CreateJSDocUnknownTag(Identifier tagName, object? comment = null)
    {
        var node = CreateBaseJSDocTag<JSDocUnknownTag>(SyntaxKind.JSDocTag, tagName, comment);
        return node;
    }

    internal JSDocEnumTag CreateJSDocEnumTag(Identifier? tagName, JSDocTypeExpression typeExpression, object? comment = null)
    {
        var node = CreateBaseJSDocTagDeclaration<JSDocEnumTag>(SyntaxKind.JSDocEnumTag, tagName ?? CreateIdentifier(GetDefaultTagNameForKind(SyntaxKind.JSDocEnumTag)), comment);
        node.TypeExpression = typeExpression;

        // node.locals, nextContainer: undefined (fields default to null)
        return node;
    }

    internal JSDocImportTag CreateJSDocImportTag(Identifier? tagName, ImportClause? importClause, Node moduleSpecifier, ImportAttributes? attributes = null, object? comment = null)
    {
        var node = CreateBaseJSDocTag<JSDocImportTag>(SyntaxKind.JSDocImportTag, tagName ?? CreateIdentifier("import"), comment);
        node.ImportClause = importClause;
        node.ModuleSpecifier = moduleSpecifier;
        node.Attributes = attributes;
        node.Comment = comment;
        return node;
    }

    internal JSDocText CreateJSDocText(string text)
    {
        var node = CreateBaseNode<JSDocText>(SyntaxKind.JSDocText);
        node.Text = text;
        return node;
    }

    internal JSDoc CreateJSDocComment(object? comment = null, IReadOnlyList<Node>? tags = null)
    {
        var node = CreateBaseNode<JSDoc>(SyntaxKind.JSDoc);
        node.Comment = comment;
        node.Tags = AsNodeArray(tags);
        return node;
    }

    // C# helpers for the JSDoc types/tags that TypeScript handles through structural typing
    // (`T extends JSDocTag`, `T extends JSDocType & { type }`): the generated classes share no base class.

    private static void SetJSDocUnaryTypeType(Node node, Node? type)
    {
        switch (node)
        {
            case JSDocOptionalType optional:
                optional.Type = type!;
                break;
            case JSDocVariadicType variadic:
                variadic.Type = type!;
                break;
            case JSDocNamepathType namepath:
                namepath.Type = type!;
                break;
            case JSDocNullableType nullable:
                nullable.Type = type!;
                break;
            case JSDocNonNullableType nonNullable:
                nonNullable.Type = type!;
                break;
            default:
                Debug.Fail("Unexpected JSDoc unary type kind.");
                break;
        }
    }

    private static Node? GetJSDocUnaryTypeType(Node node)
    {
        switch (node)
        {
            case JSDocOptionalType optional:
                return optional.Type;
            case JSDocVariadicType variadic:
                return variadic.Type;
            case JSDocNamepathType namepath:
                return namepath.Type;
            case JSDocNullableType nullable:
                return nullable.Type;
            case JSDocNonNullableType nonNullable:
                return nonNullable.Type;
            default:
                Debug.Fail("Unexpected JSDoc unary type kind.");
                return null;
        }
    }

    private static bool GetJSDocTypePostfix(Node node)
    {
        switch (node)
        {
            case JSDocNullableType nullable:
                return nullable.Postfix;
            case JSDocNonNullableType nonNullable:
                return nonNullable.Postfix;
            default:
                Debug.Fail("Unexpected JSDoc pre/postfix unary type kind.");
                return false;
        }
    }

    private static void SetJSDocTypeLikeTagTypeExpression(Node node, JSDocTypeExpression? typeExpression)
    {
        switch (node)
        {
            case JSDocTypeTag typeTag:
                typeTag.TypeExpression = typeExpression!;
                break;
            case JSDocReturnTag returnTag:
                returnTag.TypeExpression = typeExpression;
                break;
            case JSDocThisTag thisTag:
                thisTag.TypeExpression = typeExpression!;
                break;
            case JSDocEnumTag enumTag:
                enumTag.TypeExpression = typeExpression!;
                break;
            case JSDocSatisfiesTag satisfiesTag:
                satisfiesTag.TypeExpression = typeExpression!;
                break;
            case JSDocThrowsTag throwsTag:
                throwsTag.TypeExpression = typeExpression;
                break;
            default:
                Debug.Fail("Unexpected JSDoc type-like tag kind.");
                break;
        }
    }

    private static JSDocTypeExpression? GetJSDocTypeLikeTagTypeExpression(Node node)
    {
        switch (node)
        {
            case JSDocTypeTag typeTag:
                return typeTag.TypeExpression;
            case JSDocReturnTag returnTag:
                return returnTag.TypeExpression;
            case JSDocThisTag thisTag:
                return thisTag.TypeExpression;
            case JSDocEnumTag enumTag:
                return enumTag.TypeExpression;
            case JSDocSatisfiesTag satisfiesTag:
                return satisfiesTag.TypeExpression;
            case JSDocThrowsTag throwsTag:
                return throwsTag.TypeExpression;
            default:
                Debug.Fail("Unexpected JSDoc type-like tag kind.");
                return null;
        }
    }

    private static void SetJSDocTagNameAndComment(Node node, Identifier tagName, object? comment)
    {
        switch (node)
        {
            case JSDocAugmentsTag augmentsTag:
                augmentsTag.TagName = tagName;
                augmentsTag.Comment = comment;
                break;
            case JSDocAuthorTag authorTag:
                authorTag.TagName = tagName;
                authorTag.Comment = comment;
                break;
            case JSDocCallbackTag callbackTag:
                callbackTag.TagName = tagName;
                callbackTag.Comment = comment;
                break;
            case JSDocClassTag classTag:
                classTag.TagName = tagName;
                classTag.Comment = comment;
                break;
            case JSDocDeprecatedTag deprecatedTag:
                deprecatedTag.TagName = tagName;
                deprecatedTag.Comment = comment;
                break;
            case JSDocEnumTag enumTag:
                enumTag.TagName = tagName;
                enumTag.Comment = comment;
                break;
            case JSDocImplementsTag implementsTag:
                implementsTag.TagName = tagName;
                implementsTag.Comment = comment;
                break;
            case JSDocImportTag importTag:
                importTag.TagName = tagName;
                importTag.Comment = comment;
                break;
            case JSDocOverloadTag overloadTag:
                overloadTag.TagName = tagName;
                overloadTag.Comment = comment;
                break;
            case JSDocOverrideTag overrideTag:
                overrideTag.TagName = tagName;
                overrideTag.Comment = comment;
                break;
            case JSDocParameterTag parameterTag:
                parameterTag.TagName = tagName;
                parameterTag.Comment = comment;
                break;
            case JSDocPrivateTag privateTag:
                privateTag.TagName = tagName;
                privateTag.Comment = comment;
                break;
            case JSDocPropertyTag propertyTag:
                propertyTag.TagName = tagName;
                propertyTag.Comment = comment;
                break;
            case JSDocProtectedTag protectedTag:
                protectedTag.TagName = tagName;
                protectedTag.Comment = comment;
                break;
            case JSDocPublicTag publicTag:
                publicTag.TagName = tagName;
                publicTag.Comment = comment;
                break;
            case JSDocReadonlyTag readonlyTag:
                readonlyTag.TagName = tagName;
                readonlyTag.Comment = comment;
                break;
            case JSDocReturnTag returnTag:
                returnTag.TagName = tagName;
                returnTag.Comment = comment;
                break;
            case JSDocSatisfiesTag satisfiesTag:
                satisfiesTag.TagName = tagName;
                satisfiesTag.Comment = comment;
                break;
            case JSDocSeeTag seeTag:
                seeTag.TagName = tagName;
                seeTag.Comment = comment;
                break;
            case JSDocTemplateTag templateTag:
                templateTag.TagName = tagName;
                templateTag.Comment = comment;
                break;
            case JSDocThisTag thisTag:
                thisTag.TagName = tagName;
                thisTag.Comment = comment;
                break;
            case JSDocThrowsTag throwsTag:
                throwsTag.TagName = tagName;
                throwsTag.Comment = comment;
                break;
            case JSDocTypeTag typeTag:
                typeTag.TagName = tagName;
                typeTag.Comment = comment;
                break;
            case JSDocTypedefTag typedefTag:
                typedefTag.TagName = tagName;
                typedefTag.Comment = comment;
                break;
            case JSDocUnknownTag unknownTag:
                unknownTag.TagName = tagName;
                unknownTag.Comment = comment;
                break;
            default:
                Debug.Fail("Unexpected JSDoc tag kind.");
                break;
        }
    }

    private static Identifier GetJSDocTagName(Node node)
    {
        switch (node)
        {
            case JSDocAugmentsTag augmentsTag:
                return augmentsTag.TagName;
            case JSDocAuthorTag authorTag:
                return authorTag.TagName;
            case JSDocCallbackTag callbackTag:
                return callbackTag.TagName;
            case JSDocClassTag classTag:
                return classTag.TagName;
            case JSDocDeprecatedTag deprecatedTag:
                return deprecatedTag.TagName;
            case JSDocEnumTag enumTag:
                return enumTag.TagName;
            case JSDocImplementsTag implementsTag:
                return implementsTag.TagName;
            case JSDocImportTag importTag:
                return importTag.TagName;
            case JSDocOverloadTag overloadTag:
                return overloadTag.TagName;
            case JSDocOverrideTag overrideTag:
                return overrideTag.TagName;
            case JSDocParameterTag parameterTag:
                return parameterTag.TagName;
            case JSDocPrivateTag privateTag:
                return privateTag.TagName;
            case JSDocPropertyTag propertyTag:
                return propertyTag.TagName;
            case JSDocProtectedTag protectedTag:
                return protectedTag.TagName;
            case JSDocPublicTag publicTag:
                return publicTag.TagName;
            case JSDocReadonlyTag readonlyTag:
                return readonlyTag.TagName;
            case JSDocReturnTag returnTag:
                return returnTag.TagName;
            case JSDocSatisfiesTag satisfiesTag:
                return satisfiesTag.TagName;
            case JSDocSeeTag seeTag:
                return seeTag.TagName;
            case JSDocTemplateTag templateTag:
                return templateTag.TagName;
            case JSDocThisTag thisTag:
                return thisTag.TagName;
            case JSDocThrowsTag throwsTag:
                return throwsTag.TagName;
            case JSDocTypeTag typeTag:
                return typeTag.TagName;
            case JSDocTypedefTag typedefTag:
                return typedefTag.TagName;
            case JSDocUnknownTag unknownTag:
                return unknownTag.TagName;
            default:
                throw new InvalidOperationException("Unexpected JSDoc tag kind.");
        }
    }

    private static object? GetJSDocTagComment(Node node)
    {
        switch (node)
        {
            case JSDocAugmentsTag augmentsTag:
                return augmentsTag.Comment;
            case JSDocAuthorTag authorTag:
                return authorTag.Comment;
            case JSDocCallbackTag callbackTag:
                return callbackTag.Comment;
            case JSDocClassTag classTag:
                return classTag.Comment;
            case JSDocDeprecatedTag deprecatedTag:
                return deprecatedTag.Comment;
            case JSDocEnumTag enumTag:
                return enumTag.Comment;
            case JSDocImplementsTag implementsTag:
                return implementsTag.Comment;
            case JSDocImportTag importTag:
                return importTag.Comment;
            case JSDocOverloadTag overloadTag:
                return overloadTag.Comment;
            case JSDocOverrideTag overrideTag:
                return overrideTag.Comment;
            case JSDocParameterTag parameterTag:
                return parameterTag.Comment;
            case JSDocPrivateTag privateTag:
                return privateTag.Comment;
            case JSDocPropertyTag propertyTag:
                return propertyTag.Comment;
            case JSDocProtectedTag protectedTag:
                return protectedTag.Comment;
            case JSDocPublicTag publicTag:
                return publicTag.Comment;
            case JSDocReadonlyTag readonlyTag:
                return readonlyTag.Comment;
            case JSDocReturnTag returnTag:
                return returnTag.Comment;
            case JSDocSatisfiesTag satisfiesTag:
                return satisfiesTag.Comment;
            case JSDocSeeTag seeTag:
                return seeTag.Comment;
            case JSDocTemplateTag templateTag:
                return templateTag.Comment;
            case JSDocThisTag thisTag:
                return thisTag.Comment;
            case JSDocThrowsTag throwsTag:
                return throwsTag.Comment;
            case JSDocTypeTag typeTag:
                return typeTag.Comment;
            case JSDocTypedefTag typedefTag:
                return typedefTag.Comment;
            case JSDocUnknownTag unknownTag:
                return unknownTag.Comment;
            default:
                throw new InvalidOperationException("Unexpected JSDoc tag kind.");
        }
    }

    //
    // JSX
    //

    internal JsxElement CreateJsxElement(JsxOpeningElement openingElement, IReadOnlyList<Node> children, JsxClosingElement closingElement)
    {
        var node = CreateBaseNode<JsxElement>(SyntaxKind.JsxElement);
        node.OpeningElement = openingElement;
        node.Children = CreateNodeArray(children);
        node.ClosingElement = closingElement;
        node.TransformFlags |= PropagateChildFlags(node.OpeningElement) |
            PropagateChildrenFlags(node.Children) |
            PropagateChildFlags(node.ClosingElement) |
            TransformFlags.ContainsJsx;
        return node;
    }

    internal JsxSelfClosingElement CreateJsxSelfClosingElement(Node tagName, IReadOnlyList<Node>? typeArguments, JsxAttributes attributes)
    {
        var node = CreateBaseNode<JsxSelfClosingElement>(SyntaxKind.JsxSelfClosingElement);
        node.TagName = tagName;
        node.TypeArguments = AsNodeArray(typeArguments);
        node.Attributes = attributes;
        node.TransformFlags |= PropagateChildFlags(node.TagName) |
            PropagateChildrenFlags(node.TypeArguments) |
            PropagateChildFlags(node.Attributes) |
            TransformFlags.ContainsJsx;
        if (node.TypeArguments != null)
        {
            node.TransformFlags |= TransformFlags.ContainsTypeScript;
        }

        return node;
    }

    internal JsxOpeningElement CreateJsxOpeningElement(Node tagName, IReadOnlyList<Node>? typeArguments, JsxAttributes attributes)
    {
        var node = CreateBaseNode<JsxOpeningElement>(SyntaxKind.JsxOpeningElement);
        node.TagName = tagName;
        node.TypeArguments = AsNodeArray(typeArguments);
        node.Attributes = attributes;
        node.TransformFlags |= PropagateChildFlags(node.TagName) |
            PropagateChildrenFlags(node.TypeArguments) |
            PropagateChildFlags(node.Attributes) |
            TransformFlags.ContainsJsx;
        if (typeArguments != null)
        {
            node.TransformFlags |= TransformFlags.ContainsTypeScript;
        }

        return node;
    }

    internal JsxClosingElement CreateJsxClosingElement(Node tagName)
    {
        var node = CreateBaseNode<JsxClosingElement>(SyntaxKind.JsxClosingElement);
        node.TagName = tagName;
        node.TransformFlags |= PropagateChildFlags(node.TagName) |
            TransformFlags.ContainsJsx;
        return node;
    }

    internal JsxFragment CreateJsxFragment(JsxOpeningFragment openingFragment, IReadOnlyList<Node> children, JsxClosingFragment closingFragment)
    {
        var node = CreateBaseNode<JsxFragment>(SyntaxKind.JsxFragment);
        node.OpeningFragment = openingFragment;
        node.Children = CreateNodeArray(children);
        node.ClosingFragment = closingFragment;
        node.TransformFlags |= PropagateChildFlags(node.OpeningFragment) |
            PropagateChildrenFlags(node.Children) |
            PropagateChildFlags(node.ClosingFragment) |
            TransformFlags.ContainsJsx;
        return node;
    }

    internal JsxText CreateJsxText(string text, bool? containsOnlyTriviaWhiteSpaces = null)
    {
        var node = CreateBaseNode<JsxText>(SyntaxKind.JsxText);
        node.Text = text;
        node.ContainsOnlyTriviaWhiteSpaces = containsOnlyTriviaWhiteSpaces == true;
        node.TransformFlags |= TransformFlags.ContainsJsx;
        return node;
    }

    internal JsxOpeningFragment CreateJsxOpeningFragment()
    {
        var node = CreateBaseNode<JsxOpeningFragment>(SyntaxKind.JsxOpeningFragment);
        node.TransformFlags |= TransformFlags.ContainsJsx;
        return node;
    }

    internal JsxClosingFragment CreateJsxJsxClosingFragment()
    {
        var node = CreateBaseNode<JsxClosingFragment>(SyntaxKind.JsxClosingFragment);
        node.TransformFlags |= TransformFlags.ContainsJsx;
        return node;
    }

    internal JsxAttribute CreateJsxAttribute(Node name, Node? initializer)
    {
        var node = CreateBaseDeclaration<JsxAttribute>(SyntaxKind.JsxAttribute);
        node.Name = name;
        node.Initializer = initializer;
        node.TransformFlags |= PropagateChildFlags(node.Name) |
            PropagateChildFlags(node.Initializer) |
            TransformFlags.ContainsJsx;
        return node;
    }

    internal JsxAttributes CreateJsxAttributes(IReadOnlyList<Node> properties)
    {
        var node = CreateBaseDeclaration<JsxAttributes>(SyntaxKind.JsxAttributes);
        node.Properties = CreateNodeArray(properties);
        node.TransformFlags |= PropagateChildrenFlags(node.Properties) |
            TransformFlags.ContainsJsx;
        return node;
    }

    internal JsxSpreadAttribute CreateJsxSpreadAttribute(Node expression)
    {
        var node = CreateBaseNode<JsxSpreadAttribute>(SyntaxKind.JsxSpreadAttribute);
        node.Expression = expression;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsJsx;
        return node;
    }

    internal JsxExpression CreateJsxExpression(DotDotDotToken? dotDotDotToken, Node? expression)
    {
        var node = CreateBaseNode<JsxExpression>(SyntaxKind.JsxExpression);
        node.DotDotDotToken = dotDotDotToken;
        node.Expression = expression;
        node.TransformFlags |= PropagateChildFlags(node.DotDotDotToken) |
            PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsJsx;
        return node;
    }

    internal JsxNamespacedName CreateJsxNamespacedName(Identifier @namespace, Identifier name)
    {
        var node = CreateBaseNode<JsxNamespacedName>(SyntaxKind.JsxNamespacedName);
        node.Namespace = @namespace;
        node.Name = name;
        node.TransformFlags |= PropagateChildFlags(node.Namespace) |
            PropagateChildFlags(node.Name) |
            TransformFlags.ContainsJsx;
        return node;
    }

    //
    // Clauses
    //

    internal CaseClause CreateCaseClause(Node expression, IReadOnlyList<Node> statements)
    {
        var node = CreateBaseNode<CaseClause>(SyntaxKind.CaseClause);
        node.Expression = parenthesizerRules.ParenthesizeExpressionForDisallowedComma(expression);
        node.Statements = CreateNodeArray(statements);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            PropagateChildrenFlags(node.Statements);

        // node.jsDoc: undefined (initialized by parser)
        return node;
    }

    internal DefaultClause CreateDefaultClause(IReadOnlyList<Node> statements)
    {
        var node = CreateBaseNode<DefaultClause>(SyntaxKind.DefaultClause);
        node.Statements = CreateNodeArray(statements);
        node.TransformFlags = PropagateChildrenFlags(node.Statements);
        return node;
    }

    internal HeritageClause CreateHeritageClause(SyntaxKind token, IReadOnlyList<Node> types)
    {
        var node = CreateBaseNode<HeritageClause>(SyntaxKind.HeritageClause);
        node.Token = token;
        node.Types = CreateNodeArray(types);
        node.TransformFlags |= PropagateChildrenFlags(node.Types);
        switch (token)
        {
            case SyntaxKind.ExtendsKeyword:
                node.TransformFlags |= TransformFlags.ContainsES2015;
                break;
            case SyntaxKind.ImplementsKeyword:
                node.TransformFlags |= TransformFlags.ContainsTypeScript;
                break;
            default:
                Debug.AssertNever(token);
                break;
        }

        return node;
    }

    /// <summary>`variableDeclaration: string | BindingName | VariableDeclaration | undefined` is an <c>object?</c>.</summary>
    internal CatchClause CreateCatchClause(object? variableDeclaration, Block block)
    {
        var node = CreateBaseNode<CatchClause>(SyntaxKind.CatchClause);
        node.VariableDeclaration = AsVariableDeclaration(variableDeclaration);
        node.Block = block;

        node.TransformFlags |= PropagateChildFlags(node.VariableDeclaration) |
            PropagateChildFlags(node.Block) |
            ((variableDeclaration == null || (variableDeclaration is string text && text.Length == 0)) ? TransformFlags.ContainsES2019 : TransformFlags.None);

        // node.locals, nextContainer: undefined (fields default to null)
        return node;
    }

    //
    // Property assignments
    //

    internal PropertyAssignment CreatePropertyAssignment(Node name, Node initializer)
    {
        var node = CreateBaseDeclaration<PropertyAssignment>(SyntaxKind.PropertyAssignment);
        node.Name = AsName(name)!;
        node.Initializer = parenthesizerRules.ParenthesizeExpressionForDisallowedComma(initializer);
        node.TransformFlags |= PropagateNameFlags(node.Name) |
            PropagateChildFlags(node.Initializer);

        // node.modifiers, questionToken, exclamationToken, jsDoc: undefined (initialized by parser)
        return node;
    }

    internal PropertyAssignment CreatePropertyAssignment(string name, Node initializer)
    {
        return CreatePropertyAssignment(AsName(name)!, initializer);
    }

    internal ShorthandPropertyAssignment CreateShorthandPropertyAssignment(Identifier name, Node? objectAssignmentInitializer = null)
    {
        var node = CreateBaseDeclaration<ShorthandPropertyAssignment>(SyntaxKind.ShorthandPropertyAssignment);
        node.Name = (Identifier)AsName(name)!;
        node.ObjectAssignmentInitializer = objectAssignmentInitializer != null
            ? parenthesizerRules.ParenthesizeExpressionForDisallowedComma(objectAssignmentInitializer)
            : null;
        node.TransformFlags |= PropagateIdentifierNameFlags(node.Name) |
            PropagateChildFlags(node.ObjectAssignmentInitializer) |
            TransformFlags.ContainsES2015;

        // node.equalsToken, modifiers, questionToken, exclamationToken, jsDoc: undefined (initialized by parser)
        return node;
    }

    internal ShorthandPropertyAssignment CreateShorthandPropertyAssignment(string name, Node? objectAssignmentInitializer = null)
    {
        return CreateShorthandPropertyAssignment((Identifier)AsName(name)!, objectAssignmentInitializer);
    }

    internal SpreadAssignment CreateSpreadAssignment(Node expression)
    {
        var node = CreateBaseDeclaration<SpreadAssignment>(SyntaxKind.SpreadAssignment);
        node.Expression = parenthesizerRules.ParenthesizeExpressionForDisallowedComma(expression);
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsES2018 |
            TransformFlags.ContainsObjectRestOrSpread;

        // node.jsDoc: undefined (initialized by parser)
        return node;
    }

    //
    // Enum
    //

    internal EnumMember CreateEnumMember(Node name, Node? initializer = null)
    {
        var node = CreateBaseDeclaration<EnumMember>(SyntaxKind.EnumMember);
        node.Name = AsName(name)!;
        node.Initializer = initializer != null
            ? parenthesizerRules.ParenthesizeExpressionForDisallowedComma(initializer)
            : null;
        node.TransformFlags |= PropagateChildFlags(node.Name) |
            PropagateChildFlags(node.Initializer) |
            TransformFlags.ContainsTypeScript;

        // node.jsDoc: undefined (initialized by parser)
        return node;
    }

    internal EnumMember CreateEnumMember(string name, Node? initializer = null)
    {
        return CreateEnumMember(AsName(name)!, initializer);
    }

    //
    // Top-level nodes
    //

    internal SourceFile CreateSourceFile(
        IReadOnlyList<Node> statements,
        EndOfFileToken endOfFileToken,
        NodeFlags flags)
    {
        var node = (SourceFile)baseFactory.CreateBaseSourceFileNode(SyntaxKind.SourceFile);
        node.Statements = CreateNodeArray(statements);
        node.EndOfFileToken = endOfFileToken;
        node.Flags |= flags;
        node.Text = "";
        node.FileName = "";
        node.Path = "";
        node.ResolvedPath = "";
        node.OriginalFileName = "";
        node.LanguageVersion = ScriptTarget.LatestStandard;
        node.LanguageVariant = 0;
        node.ScriptKind = 0;
        node.IsDeclarationFile = false;
        node.HasNoDefaultLib = false;

        node.TransformFlags |= PropagateChildrenFlags(node.Statements) |
            PropagateChildFlags(node.EndOfFileToken);

        // node.locals, nextContainer, endFlowNode: undefined (initialized by binder)
        node.NodeCount = 0;
        node.IdentifierCount = 0;
        node.SymbolCount = 0;

        // The remaining fields (parseDiagnostics, bindDiagnostics, bindSuggestionDiagnostics, lineMap,
        // externalModuleIndicator, setExternalModuleIndicator, pragmas, checkJsDirective, referencedFiles,
        // typeReferenceDirectives, libReferenceDirectives, amdDependencies, commentDirectives, identifiers,
        // packageJsonLocations, packageJsonScope, imports, moduleAugmentations, ambientModuleNames,
        // classifiableNames, impliedNodeFormat) are undefined in TypeScript: they default to null here.
        return node;
    }

    // C# deviation: TypeScript builds a redirected source file with Object.create(redirectTarget) and
    // accessor properties that forward `id` and `symbol` to the redirect target. C# has no prototype
    // delegation; redirected source files are only created by the program (module resolution, outside
    // Phase 1), never by the parser.
    private static SourceFile CreateRedirectedSourceFile(object redirectInfo)
    {
        _ = redirectInfo;
        throw new NotImplementedException("createRedirectedSourceFile needs prototype delegation (program-level, not used by the parser)");
    }

    private static SourceFile CloneRedirectedSourceFile(SourceFile source)
    {
        SourceFile node = CreateRedirectedSourceFile(source.RedirectInfo!);
        node.Flags |= source.Flags & ~NodeFlags.Synthesized;
        node.FileName = source.FileName;
        node.Path = source.Path;
        node.ResolvedPath = source.ResolvedPath;
        node.OriginalFileName = source.OriginalFileName;
        node.PackageJsonLocations = source.PackageJsonLocations;
        node.PackageJsonScope = source.PackageJsonScope;
        node.EmitNode = null;
        return node;
    }

    private SourceFile CloneSourceFileWorker(SourceFile source)
    {
        // TypeScript copies every own property of `source` that the freshly created base node does not have yet
        // (`for (const p in source)` with `hasProperty(node, p)` skipping the Node constructor's fields: pos, end,
        // kind, id, flags, modifierFlagsCache, transformFlags, parent, original, emitNode). `emitNode` is reset.
        // C#: the same fields, listed explicitly.
        var node = (SourceFile)baseFactory.CreateBaseSourceFileNode(SyntaxKind.SourceFile);
        node.Flags |= source.Flags & ~NodeFlags.Synthesized;
        node.AmbientModuleNames = source.AmbientModuleNames;
        node.ClassifiableNames = source.ClassifiableNames;
        node.EndFlowNode = source.EndFlowNode;
        node.EndOfFileToken = source.EndOfFileToken;
        node.ExternalModuleIndicator = source.ExternalModuleIndicator;
        node.FileName = source.FileName;
        node.HasNoDefaultLib = source.HasNoDefaultLib;
        node.IdentifierCount = source.IdentifierCount;
        node.Identifiers = source.Identifiers;
        node.ImpliedNodeFormat = source.ImpliedNodeFormat;
        node.Imports = source.Imports;
        node.IsDeclarationFile = source.IsDeclarationFile;
        node.JsDocParsingMode = source.JsDocParsingMode;
        node.LanguageVariant = source.LanguageVariant;
        node.LanguageVersion = source.LanguageVersion;
        node.LineMap = source.LineMap;
        node.LocalSymbol = source.LocalSymbol;
        node.Locals = source.Locals;
        node.ModuleAugmentations = source.ModuleAugmentations;
        node.ModuleName = source.ModuleName;
        node.NextContainer = source.NextContainer;
        node.NodeCount = source.NodeCount;
        node.OriginalFileName = source.OriginalFileName;
        node.PackageJsonLocations = source.PackageJsonLocations;
        node.PackageJsonScope = source.PackageJsonScope;
        node.Path = source.Path;
        node.RedirectInfo = source.RedirectInfo;
        node.ResolvedPath = source.ResolvedPath;
        node.ScriptKind = source.ScriptKind;
        node.SetExternalModuleIndicator = source.SetExternalModuleIndicator;
        node.Statements = source.Statements;
        node.Symbol = source.Symbol;
        node.SymbolCount = source.SymbolCount;
        node.Text = source.Text;

        // Fields declared by the port in the SourceFile partial (Parser/Types.cs).
        node.AmdDependencies = source.AmdDependencies;
        node.ReferencedFiles = source.ReferencedFiles;
        node.TypeReferenceDirectives = source.TypeReferenceDirectives;
        node.LibReferenceDirectives = source.LibReferenceDirectives;
        node.ParseDiagnostics = source.ParseDiagnostics;
        node.BindDiagnostics = source.BindDiagnostics;
        node.BindSuggestionDiagnostics = source.BindSuggestionDiagnostics;
        node.JsDocDiagnostics = source.JsDocDiagnostics;
        node.CommentDirectives = source.CommentDirectives;
        node.CheckJsDirective = source.CheckJsDirective;
        node.Pragmas = source.Pragmas;
        node.EmitNode = null;
        return node;
    }

    private SourceFile CloneSourceFile(SourceFile source)
    {
        SourceFile node = source.RedirectInfo != null ? CloneRedirectedSourceFile(source) : CloneSourceFileWorker(source);
        SetOriginal(node, source);
        return node;
    }

    private SourceFile CloneSourceFileWithChanges(
        SourceFile source,
        IReadOnlyList<Node> statements,
        bool isDeclarationFile,
        IReadOnlyList<FileReference> referencedFiles,
        IReadOnlyList<FileReference> typeReferences,
        bool hasNoDefaultLib,
        IReadOnlyList<FileReference> libReferences)
    {
        SourceFile node = CloneSourceFile(source);
        node.Statements = CreateNodeArray(statements);
        node.IsDeclarationFile = isDeclarationFile;
        node.ReferencedFiles = referencedFiles;
        node.TypeReferenceDirectives = typeReferences;
        node.HasNoDefaultLib = hasNoDefaultLib;
        node.LibReferenceDirectives = libReferences;
        node.TransformFlags = PropagateChildrenFlags(node.Statements) |
            PropagateChildFlags(node.EndOfFileToken);
        return node;
    }

    /// <summary>
    /// TypeScript's default parameter values `isDeclarationFile = node.isDeclarationFile`,
    /// `referencedFiles = node.referencedFiles`, ... are the <c>null</c> defaults here.
    /// </summary>
    internal SourceFile UpdateSourceFile(
        SourceFile node,
        IReadOnlyList<Node> statements,
        bool? isDeclarationFile = null,
        IReadOnlyList<FileReference>? referencedFiles = null,
        IReadOnlyList<FileReference>? typeReferenceDirectives = null,
        bool hasNoDefaultLib = false,
        IReadOnlyList<FileReference>? libReferenceDirectives = null)
    {
        _ = hasNoDefaultLib; // TypeScript's `_hasNoDefaultLib`: ignored, false is passed on
        bool isDeclarationFileValue = isDeclarationFile ?? node.IsDeclarationFile;
        IReadOnlyList<FileReference> referencedFilesValue = referencedFiles ?? node.ReferencedFiles;
        IReadOnlyList<FileReference> typeReferenceDirectivesValue = typeReferenceDirectives ?? node.TypeReferenceDirectives;
        IReadOnlyList<FileReference> libReferenceDirectivesValue = libReferenceDirectives ?? node.LibReferenceDirectives;
        return node.Statements != statements
                || node.IsDeclarationFile != isDeclarationFileValue
                || node.ReferencedFiles != referencedFilesValue
                || node.TypeReferenceDirectives != typeReferenceDirectivesValue
                || node.LibReferenceDirectives != libReferenceDirectivesValue
            ? Update(CloneSourceFileWithChanges(node, statements, isDeclarationFileValue, referencedFilesValue, typeReferenceDirectivesValue, /*hasNoDefaultLib*/ false, libReferenceDirectivesValue), node)
            : node;
    }

    //
    // Transformation nodes
    //

    internal PartiallyEmittedExpression CreatePartiallyEmittedExpression(Node expression, Node? original = null)
    {
        var node = CreateBaseNode<PartiallyEmittedExpression>(SyntaxKind.PartiallyEmittedExpression);
        node.Expression = expression;
        node.Original = original;
        node.TransformFlags |= PropagateChildFlags(node.Expression) |
            TransformFlags.ContainsTypeScript;
        FactoryUtilitiesPublic.SetTextRange(node, original);
        return node;
    }

    internal PartiallyEmittedExpression UpdatePartiallyEmittedExpression(PartiallyEmittedExpression node, Node expression)
    {
        return node.Expression != expression
            ? Update(CreatePartiallyEmittedExpression(expression, node.Original), node)
            : node;
    }
}
