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

namespace NeverSharp.Optimized;

/// <summary>
/// nodeFactory.ts: JSDoc nodes. TypeScript exposes several of them through memoized getters that bind a worker to
/// a kind (`get createJSDocAllType() { return getJSDocPrimaryTypeCreateFunction(SyntaxKind.JSDocAllType); }`);
/// here each is a method calling the worker with its kind (the memoization only caches the bound function).
/// </summary>
internal sealed partial class NodeFactory
{
    //
    // JSDoc
    //

    // createJSDocAllType
    // createJSDocUnknownType
    private Node CreateJSDocPrimaryTypeWorker(SyntaxKind kind)
    {
        return CreateBaseNode<Node>(kind);
    }

    // createJSDocNullableType
    // createJSDocNonNullableType
    private Node CreateJSDocPrePostfixUnaryTypeWorker(SyntaxKind kind, Node? type, bool postfix = false)
    {
        var node = CreateJSDocUnaryTypeWorker(
            kind,
            postfix ? type != null ? Parenthesizer.ParenthesizeNonArrayTypeOfPostfixType(type) : null : type);
        switch (node)
        {
            case JSDocNullableType nullable:
                nullable.Postfix = postfix;
                break;
            case JSDocNonNullableType nonNullable:
                nonNullable.Postfix = postfix;
                break;
        }
        return node;
    }

    // createJSDocOptionalType
    // createJSDocVariadicType
    // createJSDocNamepathType
    private Node CreateJSDocUnaryTypeWorker(SyntaxKind kind, Node? type)
    {
        var node = CreateBaseNode<Node>(kind);
        SetJSDocUnaryType(node, type);
        return node;
    }

    // updateJSDocNonNullableType
    // updateJSDocNullableType
    private Node UpdateJSDocPrePostfixUnaryTypeWorker(SyntaxKind kind, Node node, Node? type)
    {
        return GetJSDocUnaryType(node) != type
            ? Update(CreateJSDocPrePostfixUnaryTypeWorker(kind, type, node is JSDocNullableType n ? n.Postfix : ((JSDocNonNullableType)node).Postfix), node)
            : node;
    }

    // updateJSDocOptionalType
    // updateJSDocVariadicType
    // updateJSDocNamepathType
    private Node UpdateJSDocUnaryTypeWorker(SyntaxKind kind, Node node, Node? type)
    {
        return GetJSDocUnaryType(node) != type
            ? Update(CreateJSDocUnaryTypeWorker(kind, type), node)
            : node;
    }

    /// <summary>`node.type = type` on the JSDoc unary type nodes.</summary>
    private static void SetJSDocUnaryType(Node node, Node? type)
    {
        switch (node)
        {
            case JSDocNullableType n:
                n.Type = type!;
                break;
            case JSDocNonNullableType n:
                n.Type = type!;
                break;
            case JSDocOptionalType n:
                n.Type = type!;
                break;
            case JSDocVariadicType n:
                n.Type = type!;
                break;
            case JSDocNamepathType n:
                n.Type = type!;
                break;
            default:
                throw new InvalidOperationException($"{node.Kind} is not a JSDoc unary type");
        }
    }

    /// <summary>`node.type` on the JSDoc unary type nodes.</summary>
    private static Node? GetJSDocUnaryType(Node node)
    {
        return node switch
        {
            JSDocNullableType n => n.Type,
            JSDocNonNullableType n => n.Type,
            JSDocOptionalType n => n.Type,
            JSDocVariadicType n => n.Type,
            JSDocNamepathType n => n.Type,
            _ => throw new InvalidOperationException($"{node.Kind} is not a JSDoc unary type"),
        };
    }

    // @api
    internal JSDocAllType CreateJSDocAllType() => (JSDocAllType)CreateJSDocPrimaryTypeWorker(SyntaxKind.JSDocAllType);

    // @api
    internal JSDocUnknownType CreateJSDocUnknownType() => (JSDocUnknownType)CreateJSDocPrimaryTypeWorker(SyntaxKind.JSDocUnknownType);

    // @api
    internal JSDocNonNullableType CreateJSDocNonNullableType(Node? type, bool postfix = false) => (JSDocNonNullableType)CreateJSDocPrePostfixUnaryTypeWorker(SyntaxKind.JSDocNonNullableType, type, postfix);

    // @api
    internal JSDocNullableType CreateJSDocNullableType(Node? type, bool postfix = false) => (JSDocNullableType)CreateJSDocPrePostfixUnaryTypeWorker(SyntaxKind.JSDocNullableType, type, postfix);

    // @api
    internal JSDocOptionalType CreateJSDocOptionalType(Node? type) => (JSDocOptionalType)CreateJSDocUnaryTypeWorker(SyntaxKind.JSDocOptionalType, type);

    // @api
    internal JSDocVariadicType CreateJSDocVariadicType(Node? type) => (JSDocVariadicType)CreateJSDocUnaryTypeWorker(SyntaxKind.JSDocVariadicType, type);

    // @api
    internal JSDocNamepathType CreateJSDocNamepathType(Node? type) => (JSDocNamepathType)CreateJSDocUnaryTypeWorker(SyntaxKind.JSDocNamepathType, type);

    // @api
    internal JSDocFunctionType CreateJSDocFunctionType(IReadOnlyList<Node> parameters, Node? type)
    {
        var node = CreateBaseDeclaration<JSDocFunctionType>(SyntaxKind.JSDocFunctionType);
        node.Parameters = AsNodeArray(parameters)!;
        node.Type = type;
        node.TransformFlags = PropagateChildrenFlags(node.Parameters) |
            (node.Type != null ? TransformFlags.ContainsTypeScript : TransformFlags.None);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.typeArguments = undefined; // used in quick info
        return node;
    }

    // @api
    internal JSDocTypeLiteral CreateJSDocTypeLiteral(IReadOnlyList<Node>? propertyTags = null, bool isArrayType = false)
    {
        var node = CreateBaseDeclaration<JSDocTypeLiteral>(SyntaxKind.JSDocTypeLiteral);
        // TypeScript assigns `asNodeArray(propertyTags)`; the generated field is an array (Node[]), so the NodeArray's
        // elements are stored.
        var array = AsNodeArray(propertyTags);
        node.JsDocPropertyTags = array?.ToArray();
        node.IsArrayType = isArrayType;
        return node;
    }

    // @api
    internal JSDocTypeExpression CreateJSDocTypeExpression(Node type)
    {
        var node = CreateBaseNode<JSDocTypeExpression>(SyntaxKind.JSDocTypeExpression);
        node.Type = type;
        return node;
    }

    // @api
    internal JSDocSignature CreateJSDocSignature(IReadOnlyList<Node>? typeParameters, IReadOnlyList<Node> parameters, JSDocReturnTag? type = null)
    {
        var node = CreateBaseDeclaration<JSDocSignature>(SyntaxKind.JSDocSignature);
        // TypeScript assigns NodeArrays (asNodeArray / createNodeArray); the generated fields are typed arrays, so the
        // NodeArrays' elements are stored.
        var typeParametersArray = AsNodeArray(typeParameters);
        node.TypeParameters = typeParametersArray != null ? ToArray<JSDocTemplateTag>(typeParametersArray) : null;
        // (Node[]: besides JSDocParameterTags the parser stores other tags here, e.g. a `@this` tag inside `@callback`.)
        node.Parameters = ToArray<Node>(CreateNodeArray(parameters));
        node.Type = type!;
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    private static T[] ToArray<T>(NodeArray array)
        where T : Node
    {
        var result = new T[array.Count];
        for (var i = 0; i < array.Count; i++)
        {
            result[i] = (T)array[i];
        }
        return result;
    }

    private Identifier GetDefaultTagName(Node node)
    {
        var defaultTagName = GetDefaultTagNameForKind(node.Kind);
        var tagName = GetJSDocTagName(node);
        return (string)tagName.EscapedText! == UtilitiesPublic.EscapeLeadingUnderscores(defaultTagName)
            ? tagName
            : CreateIdentifier(defaultTagName);
    }

    /// <summary>`createBaseJSDocTag(kind, tagName, comment: string | NodeArray&lt;JSDocComment&gt; | undefined)`.</summary>
    private Node CreateBaseJSDocTag(SyntaxKind kind, Identifier tagName, object? comment)
    {
        var node = CreateBaseNode<Node>(kind);
        SetJSDocTagNameAndComment(node, tagName, comment);
        return node;
    }

    private Node CreateBaseJSDocTagDeclaration(SyntaxKind kind, Identifier tagName, object? comment)
    {
        var node = CreateBaseDeclaration<Node>(kind);
        SetJSDocTagNameAndComment(node, tagName, comment);
        return node;
    }

    /// <summary>`node.tagName = tagName; node.comment = comment;` on the JSDoc tag nodes.</summary>
    private static void SetJSDocTagNameAndComment(Node node, Identifier tagName, object? comment)
    {
        switch (node)
        {
            case JSDocAugmentsTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocAuthorTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocCallbackTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocClassTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocDeprecatedTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocEnumTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocImplementsTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocImportTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocOverloadTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocOverrideTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocParameterTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocPrivateTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocPropertyTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocProtectedTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocPublicTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocReadonlyTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocReturnTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocSatisfiesTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocSeeTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocTemplateTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocThisTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocThrowsTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocTypeTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocTypedefTag n: n.TagName = tagName; n.Comment = comment; break;
            case JSDocUnknownTag n: n.TagName = tagName; n.Comment = comment; break;
            default: throw new InvalidOperationException($"{node.Kind} is not a JSDoc tag");
        }
    }

    /// <summary>`node.tagName` on the JSDoc tag nodes.</summary>
    private static Identifier GetJSDocTagName(Node node)
    {
        return node switch
        {
            JSDocAugmentsTag n => n.TagName,
            JSDocAuthorTag n => n.TagName,
            JSDocCallbackTag n => n.TagName,
            JSDocClassTag n => n.TagName,
            JSDocDeprecatedTag n => n.TagName,
            JSDocEnumTag n => n.TagName,
            JSDocImplementsTag n => n.TagName,
            JSDocImportTag n => n.TagName,
            JSDocOverloadTag n => n.TagName,
            JSDocOverrideTag n => n.TagName,
            JSDocParameterTag n => n.TagName,
            JSDocPrivateTag n => n.TagName,
            JSDocPropertyTag n => n.TagName,
            JSDocProtectedTag n => n.TagName,
            JSDocPublicTag n => n.TagName,
            JSDocReadonlyTag n => n.TagName,
            JSDocReturnTag n => n.TagName,
            JSDocSatisfiesTag n => n.TagName,
            JSDocSeeTag n => n.TagName,
            JSDocTemplateTag n => n.TagName,
            JSDocThisTag n => n.TagName,
            JSDocThrowsTag n => n.TagName,
            JSDocTypeTag n => n.TagName,
            JSDocTypedefTag n => n.TagName,
            JSDocUnknownTag n => n.TagName,
            _ => throw new InvalidOperationException($"{node.Kind} is not a JSDoc tag"),
        };
    }

    /// <summary>`node.comment` on the JSDoc tag nodes.</summary>
    private static object? GetJSDocTagComment(Node node)
    {
        return node switch
        {
            JSDocAugmentsTag n => n.Comment,
            JSDocAuthorTag n => n.Comment,
            JSDocCallbackTag n => n.Comment,
            JSDocClassTag n => n.Comment,
            JSDocDeprecatedTag n => n.Comment,
            JSDocEnumTag n => n.Comment,
            JSDocImplementsTag n => n.Comment,
            JSDocImportTag n => n.Comment,
            JSDocOverloadTag n => n.Comment,
            JSDocOverrideTag n => n.Comment,
            JSDocParameterTag n => n.Comment,
            JSDocPrivateTag n => n.Comment,
            JSDocPropertyTag n => n.Comment,
            JSDocProtectedTag n => n.Comment,
            JSDocPublicTag n => n.Comment,
            JSDocReadonlyTag n => n.Comment,
            JSDocReturnTag n => n.Comment,
            JSDocSatisfiesTag n => n.Comment,
            JSDocSeeTag n => n.Comment,
            JSDocTemplateTag n => n.Comment,
            JSDocThisTag n => n.Comment,
            JSDocThrowsTag n => n.Comment,
            JSDocTypeTag n => n.Comment,
            JSDocTypedefTag n => n.Comment,
            JSDocUnknownTag n => n.Comment,
            _ => throw new InvalidOperationException($"{node.Kind} is not a JSDoc tag"),
        };
    }

    /// <summary>`node.typeExpression = typeExpression` on the type-like JSDoc tags.</summary>
    private static void SetJSDocTypeLikeTagTypeExpression(Node node, JSDocTypeExpression? typeExpression)
    {
        switch (node)
        {
            case JSDocTypeTag n: n.TypeExpression = typeExpression!; break;
            case JSDocReturnTag n: n.TypeExpression = typeExpression; break;
            case JSDocThisTag n: n.TypeExpression = typeExpression!; break;
            case JSDocThrowsTag n: n.TypeExpression = typeExpression; break;
            case JSDocSatisfiesTag n: n.TypeExpression = typeExpression!; break;
            case JSDocEnumTag n: n.TypeExpression = typeExpression!; break;
            default: throw new InvalidOperationException($"{node.Kind} is not a type-like JSDoc tag");
        }
    }

    /// <summary>`node.typeExpression` on the type-like JSDoc tags.</summary>
    private static JSDocTypeExpression? GetJSDocTypeLikeTagTypeExpression(Node node)
    {
        return node switch
        {
            JSDocTypeTag n => n.TypeExpression,
            JSDocReturnTag n => n.TypeExpression,
            JSDocThisTag n => n.TypeExpression,
            JSDocThrowsTag n => n.TypeExpression,
            JSDocSatisfiesTag n => n.TypeExpression,
            JSDocEnumTag n => n.TypeExpression,
            _ => throw new InvalidOperationException($"{node.Kind} is not a type-like JSDoc tag"),
        };
    }

    // @api
    internal JSDocTemplateTag CreateJSDocTemplateTag(Identifier? tagName, JSDocTypeExpression? constraint, IReadOnlyList<Node> typeParameters, object? comment = null)
    {
        var node = (JSDocTemplateTag)CreateBaseJSDocTag(SyntaxKind.JSDocTemplateTag, tagName ?? CreateIdentifier("template"), comment);
        node.Constraint = constraint!;
        node.TypeParameters = CreateNodeArray(typeParameters);
        return node;
    }

    // @api
    internal JSDocTypedefTag CreateJSDocTypedefTag(Identifier? tagName, Node? typeExpression = null, Node? fullName = null, object? comment = null)
    {
        var node = (JSDocTypedefTag)CreateBaseJSDocTagDeclaration(SyntaxKind.JSDocTypedefTag, tagName ?? CreateIdentifier("typedef"), comment);
        node.TypeExpression = typeExpression;
        node.FullName = fullName;
        node.Name = FactoryUtilities.GetJSDocTypeAliasName(fullName);
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    internal JSDocParameterTag CreateJSDocParameterTag(Identifier? tagName, Node name, bool isBracketed, JSDocTypeExpression? typeExpression = null, bool? isNameFirst = null, object? comment = null)
    {
        var node = (JSDocParameterTag)CreateBaseJSDocTagDeclaration(SyntaxKind.JSDocParameterTag, tagName ?? CreateIdentifier("param"), comment);
        node.TypeExpression = typeExpression;
        node.Name = name;
        node.IsNameFirst = isNameFirst == true;
        node.IsBracketed = isBracketed;
        return node;
    }

    // @api
    internal JSDocPropertyTag CreateJSDocPropertyTag(Identifier? tagName, Node name, bool isBracketed, JSDocTypeExpression? typeExpression = null, bool? isNameFirst = null, object? comment = null)
    {
        var node = (JSDocPropertyTag)CreateBaseJSDocTagDeclaration(SyntaxKind.JSDocPropertyTag, tagName ?? CreateIdentifier("prop"), comment);
        node.TypeExpression = typeExpression;
        node.Name = name;
        node.IsNameFirst = isNameFirst == true;
        node.IsBracketed = isBracketed;
        return node;
    }

    // @api
    internal JSDocCallbackTag CreateJSDocCallbackTag(Identifier? tagName, JSDocSignature typeExpression, Node? fullName = null, object? comment = null)
    {
        var node = (JSDocCallbackTag)CreateBaseJSDocTagDeclaration(SyntaxKind.JSDocCallbackTag, tagName ?? CreateIdentifier("callback"), comment);
        node.TypeExpression = typeExpression;
        node.FullName = fullName;
        node.Name = FactoryUtilities.GetJSDocTypeAliasName(fullName);
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    internal JSDocOverloadTag CreateJSDocOverloadTag(Identifier? tagName, JSDocSignature typeExpression, object? comment = null)
    {
        var node = (JSDocOverloadTag)CreateBaseJSDocTag(SyntaxKind.JSDocOverloadTag, tagName ?? CreateIdentifier("overload"), comment);
        node.TypeExpression = typeExpression;
        return node;
    }

    // @api
    internal JSDocAugmentsTag CreateJSDocAugmentsTag(Identifier? tagName, Node className, object? comment = null)
    {
        var node = (JSDocAugmentsTag)CreateBaseJSDocTag(SyntaxKind.JSDocAugmentsTag, tagName ?? CreateIdentifier("augments"), comment);
        node.Class = className;
        return node;
    }

    // @api
    internal JSDocImplementsTag CreateJSDocImplementsTag(Identifier? tagName, Node className, object? comment = null)
    {
        var node = (JSDocImplementsTag)CreateBaseJSDocTag(SyntaxKind.JSDocImplementsTag, tagName ?? CreateIdentifier("implements"), comment);
        node.Class = className;
        return node;
    }

    // @api
    internal JSDocSeeTag CreateJSDocSeeTag(Identifier? tagName, JSDocNameReference? name, object? comment = null)
    {
        var node = (JSDocSeeTag)CreateBaseJSDocTag(SyntaxKind.JSDocSeeTag, tagName ?? CreateIdentifier("see"), comment);
        node.Name = name;
        return node;
    }

    // @api
    internal JSDocNameReference CreateJSDocNameReference(Node name)
    {
        var node = CreateBaseNode<JSDocNameReference>(SyntaxKind.JSDocNameReference);
        node.Name = name;
        return node;
    }

    // @api
    internal JSDocMemberName CreateJSDocMemberName(Node left, Identifier right)
    {
        var node = CreateBaseNode<JSDocMemberName>(SyntaxKind.JSDocMemberName);
        node.Left = left;
        node.Right = right;
        node.TransformFlags |= PropagateChildFlags(node.Left) |
            PropagateChildFlags(node.Right);
        return node;
    }

    // @api
    internal JSDocLink CreateJSDocLink(Node? name, string text)
    {
        var node = CreateBaseNode<JSDocLink>(SyntaxKind.JSDocLink);
        node.Name = name;
        node.Text = text;
        return node;
    }

    // @api
    internal JSDocLinkCode CreateJSDocLinkCode(Node? name, string text)
    {
        var node = CreateBaseNode<JSDocLinkCode>(SyntaxKind.JSDocLinkCode);
        node.Name = name;
        node.Text = text;
        return node;
    }

    // @api
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
    private Node CreateJSDocSimpleTagWorker(SyntaxKind kind, Identifier? tagName, object? comment = null)
    {
        var node = CreateBaseJSDocTag(kind, tagName ?? CreateIdentifier(GetDefaultTagNameForKind(kind)), comment);
        return node;
    }

    // updateJSDocAuthorTag
    // updateJSDocClassTag
    // updateJSDocPublicTag
    // updateJSDocPrivateTag
    // updateJSDocProtectedTag
    // updateJSDocReadonlyTag
    // updateJSDocDeprecatedTag
    private Node UpdateJSDocSimpleTagWorker(SyntaxKind kind, Node node, Identifier? tagName, object? comment)
    {
        tagName ??= GetDefaultTagName(node);
        return GetJSDocTagName(node) != tagName
                || !Equals(GetJSDocTagComment(node), comment) // `!==`: strings by value, NodeArrays by reference
            ? Update(CreateJSDocSimpleTagWorker(kind, tagName, comment), node) :
            node;
    }

    // createJSDocTypeTag
    // createJSDocReturnTag
    // createJSDocThisTag
    // createJSDocEnumTag
    // createJSDocSatisfiesTag
    private Node CreateJSDocTypeLikeTagWorker(SyntaxKind kind, Identifier? tagName, JSDocTypeExpression? typeExpression = null, object? comment = null)
    {
        var node = CreateBaseJSDocTag(kind, tagName ?? CreateIdentifier(GetDefaultTagNameForKind(kind)), comment);
        SetJSDocTypeLikeTagTypeExpression(node, typeExpression);
        return node;
    }

    // updateJSDocTypeTag
    // updateJSDocReturnTag
    // updateJSDocThisTag
    // updateJSDocEnumTag
    // updateJSDocSatisfiesTag
    private Node UpdateJSDocTypeLikeTagWorker(SyntaxKind kind, Node node, Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment)
    {
        tagName ??= GetDefaultTagName(node);
        return GetJSDocTagName(node) != tagName
                || GetJSDocTypeLikeTagTypeExpression(node) != typeExpression
                || !Equals(GetJSDocTagComment(node), comment) // `!==`: strings by value, NodeArrays by reference
            ? Update(CreateJSDocTypeLikeTagWorker(kind, tagName, typeExpression, comment), node)
            : node;
    }

    // @api
    internal JSDocTypeTag CreateJSDocTypeTag(Identifier? tagName, JSDocTypeExpression? typeExpression = null, object? comment = null) => (JSDocTypeTag)CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocTypeTag, tagName, typeExpression, comment);

    // @api
    internal JSDocReturnTag CreateJSDocReturnTag(Identifier? tagName, JSDocTypeExpression? typeExpression = null, object? comment = null) => (JSDocReturnTag)CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocReturnTag, tagName, typeExpression, comment);

    // @api
    internal JSDocThisTag CreateJSDocThisTag(Identifier? tagName, JSDocTypeExpression? typeExpression = null, object? comment = null) => (JSDocThisTag)CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocThisTag, tagName, typeExpression, comment);

    // @api
    internal JSDocThrowsTag CreateJSDocThrowsTag(Identifier? tagName, JSDocTypeExpression? typeExpression = null, object? comment = null) => (JSDocThrowsTag)CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocThrowsTag, tagName, typeExpression, comment);

    // @api
    internal JSDocSatisfiesTag CreateJSDocSatisfiesTag(Identifier? tagName, JSDocTypeExpression? typeExpression = null, object? comment = null) => (JSDocSatisfiesTag)CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocSatisfiesTag, tagName, typeExpression, comment);

    // @api
    internal JSDocAuthorTag CreateJSDocAuthorTag(Identifier? tagName, object? comment = null) => (JSDocAuthorTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocAuthorTag, tagName, comment);

    // @api
    internal JSDocClassTag CreateJSDocClassTag(Identifier? tagName, object? comment = null) => (JSDocClassTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocClassTag, tagName, comment);

    // @api
    internal JSDocPublicTag CreateJSDocPublicTag(Identifier? tagName, object? comment = null) => (JSDocPublicTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocPublicTag, tagName, comment);

    // @api
    internal JSDocPrivateTag CreateJSDocPrivateTag(Identifier? tagName, object? comment = null) => (JSDocPrivateTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocPrivateTag, tagName, comment);

    // @api
    internal JSDocProtectedTag CreateJSDocProtectedTag(Identifier? tagName, object? comment = null) => (JSDocProtectedTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocProtectedTag, tagName, comment);

    // @api
    internal JSDocReadonlyTag CreateJSDocReadonlyTag(Identifier? tagName, object? comment = null) => (JSDocReadonlyTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocReadonlyTag, tagName, comment);

    // @api
    internal JSDocOverrideTag CreateJSDocOverrideTag(Identifier? tagName, object? comment = null) => (JSDocOverrideTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocOverrideTag, tagName, comment);

    // @api
    internal JSDocDeprecatedTag CreateJSDocDeprecatedTag(Identifier? tagName, object? comment = null) => (JSDocDeprecatedTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocDeprecatedTag, tagName, comment);

    // @api
    internal JSDocUnknownTag CreateJSDocUnknownTag(Identifier tagName, object? comment = null)
    {
        var node = (JSDocUnknownTag)CreateBaseJSDocTag(SyntaxKind.JSDocTag, tagName, comment);
        return node;
    }

    // @api
    internal JSDocEnumTag CreateJSDocEnumTag(Identifier? tagName, JSDocTypeExpression typeExpression, object? comment = null)
    {
        var node = (JSDocEnumTag)CreateBaseJSDocTagDeclaration(SyntaxKind.JSDocEnumTag, tagName ?? CreateIdentifier(GetDefaultTagNameForKind(SyntaxKind.JSDocEnumTag)), comment);
        node.TypeExpression = typeExpression;
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    internal JSDocImportTag CreateJSDocImportTag(Identifier? tagName, ImportClause? importClause, Node moduleSpecifier, ImportAttributes? attributes = null, object? comment = null)
    {
        var node = (JSDocImportTag)CreateBaseJSDocTag(SyntaxKind.JSDocImportTag, tagName ?? CreateIdentifier("import"), comment);
        node.ImportClause = importClause;
        node.ModuleSpecifier = moduleSpecifier;
        node.Attributes = attributes;
        node.Comment = comment;
        return node;
    }

    // @api
    internal JSDocText CreateJSDocText(string text)
    {
        var node = CreateBaseNode<JSDocText>(SyntaxKind.JSDocText);
        node.Text = text;
        return node;
    }

    // @api
    internal JSDoc CreateJSDocComment(object? comment = null, IReadOnlyList<Node>? tags = null)
    {
        var node = CreateBaseNode<JSDoc>(SyntaxKind.JSDoc);
        node.Comment = comment;
        node.Tags = AsNodeArray(tags);
        return node;
    }
}
