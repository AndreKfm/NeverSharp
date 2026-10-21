// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go (NodeFactory)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// nodeFactory.ts, createNodeFactory: JSDoc. A JSDoc comment (`string | NodeArray&lt;JSDocComment&gt; | undefined`) is
/// typed `object?` as the generated `Comment` fields are. The factory members that TypeScript defines as getters over
/// memoized worker functions (`get createJSDocAllType() { return getJSDocPrimaryTypeCreateFunction(...) }`, ...) are
/// methods that call the worker.
/// </summary>
internal sealed partial class NodeFactory
{
    //
    // JSDoc
    //

    // @api
    // createJSDocAllType
    // createJSDocUnknownType
    private Node CreateJSDocPrimaryTypeWorker(SyntaxKind kind)
    {
        return CreateBaseNode(kind);
    }

    // @api
    // createJSDocNullableType
    // createJSDocNonNullableType
    private Node CreateJSDocPrePostfixUnaryTypeWorker(SyntaxKind kind, Node? type, bool postfix = false)
    {
        var node = CreateJSDocUnaryTypeWorker(
            kind,
            postfix ? type != null ? Parenthesizer.ParenthesizeNonArrayTypeOfPostfixType(type) : null : type);
        // node.postfix = postfix
        if (node is JSDocNullableType nullable)
        {
            nullable.Postfix = postfix;
        }
        else
        {
            ((JSDocNonNullableType)node).Postfix = postfix;
        }
        return node;
    }

    // @api
    // createJSDocOptionalType
    // createJSDocVariadicType
    // createJSDocNamepathType
    private Node CreateJSDocUnaryTypeWorker(SyntaxKind kind, Node? type)
    {
        var node = CreateBaseNode(kind);
        SetJSDocUnaryTypeType(node, type);
        return node;
    }

    // @api
    // updateJSDocNonNullableType
    // updateJSDocNullableType
    private Node UpdateJSDocPrePostfixUnaryTypeWorker(SyntaxKind kind, Node node, Node? type)
    {
        return GetJSDocUnaryTypeType(node) != type
            ? Update(CreateJSDocPrePostfixUnaryTypeWorker(kind, type, GetJSDocPostfix(node)), node)
            : node;
    }

    // @api
    // updateJSDocOptionalType
    // updateJSDocVariadicType
    // updateJSDocNamepathType
    private Node UpdateJSDocUnaryTypeWorker(SyntaxKind kind, Node node, Node? type)
    {
        return GetJSDocUnaryTypeType(node) != type
            ? Update(CreateJSDocUnaryTypeWorker(kind, type), node)
            : node;
    }

    /// <summary>`node.type = type` on the JSDoc unary types (the generated `Type` fields are non-nullable).</summary>
    private static void SetJSDocUnaryTypeType(Node node, Node? type)
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
                Debug.Fail("Unexpected JSDoc unary type: " + node.Kind.ToString());
                break;
        }
    }

    /// <summary>`node.type` on the JSDoc unary types.</summary>
    private static Node? GetJSDocUnaryTypeType(Node node)
    {
        return node switch
        {
            JSDocNullableType n => n.Type,
            JSDocNonNullableType n => n.Type,
            JSDocOptionalType n => n.Type,
            JSDocVariadicType n => n.Type,
            JSDocNamepathType n => n.Type,
            _ => null,
        };
    }

    /// <summary>`node.postfix` on JSDocNullableType and JSDocNonNullableType.</summary>
    private static bool GetJSDocPostfix(Node node)
    {
        return node is JSDocNullableType nullable ? nullable.Postfix : ((JSDocNonNullableType)node).Postfix;
    }

    // The getters of the factory object over the JSDoc type workers.

    internal JSDocAllType CreateJSDocAllType() => (JSDocAllType)CreateJSDocPrimaryTypeWorker(SyntaxKind.JSDocAllType);

    internal JSDocUnknownType CreateJSDocUnknownType() => (JSDocUnknownType)CreateJSDocPrimaryTypeWorker(SyntaxKind.JSDocUnknownType);

    internal JSDocNonNullableType CreateJSDocNonNullableType(Node? type, bool postfix = false) => (JSDocNonNullableType)CreateJSDocPrePostfixUnaryTypeWorker(SyntaxKind.JSDocNonNullableType, type, postfix);

    internal JSDocNonNullableType UpdateJSDocNonNullableType(JSDocNonNullableType node, Node? type) => (JSDocNonNullableType)UpdateJSDocPrePostfixUnaryTypeWorker(SyntaxKind.JSDocNonNullableType, node, type);

    internal JSDocNullableType CreateJSDocNullableType(Node? type, bool postfix = false) => (JSDocNullableType)CreateJSDocPrePostfixUnaryTypeWorker(SyntaxKind.JSDocNullableType, type, postfix);

    internal JSDocNullableType UpdateJSDocNullableType(JSDocNullableType node, Node? type) => (JSDocNullableType)UpdateJSDocPrePostfixUnaryTypeWorker(SyntaxKind.JSDocNullableType, node, type);

    internal JSDocOptionalType CreateJSDocOptionalType(Node? type) => (JSDocOptionalType)CreateJSDocUnaryTypeWorker(SyntaxKind.JSDocOptionalType, type);

    internal JSDocOptionalType UpdateJSDocOptionalType(JSDocOptionalType node, Node? type) => (JSDocOptionalType)UpdateJSDocUnaryTypeWorker(SyntaxKind.JSDocOptionalType, node, type);

    internal JSDocVariadicType CreateJSDocVariadicType(Node? type) => (JSDocVariadicType)CreateJSDocUnaryTypeWorker(SyntaxKind.JSDocVariadicType, type);

    internal JSDocVariadicType UpdateJSDocVariadicType(JSDocVariadicType node, Node? type) => (JSDocVariadicType)UpdateJSDocUnaryTypeWorker(SyntaxKind.JSDocVariadicType, node, type);

    internal JSDocNamepathType CreateJSDocNamepathType(Node? type) => (JSDocNamepathType)CreateJSDocUnaryTypeWorker(SyntaxKind.JSDocNamepathType, type);

    internal JSDocNamepathType UpdateJSDocNamepathType(JSDocNamepathType node, Node? type) => (JSDocNamepathType)UpdateJSDocUnaryTypeWorker(SyntaxKind.JSDocNamepathType, node, type);

    // @api
    internal JSDocFunctionType CreateJSDocFunctionType(IReadOnlyList<Node> parameters, Node? type)
    {
        var node = (JSDocFunctionType)CreateBaseDeclaration(SyntaxKind.JSDocFunctionType);
        node.Parameters = AsNodeArray(parameters);
        node.Type = type;
        node.TransformFlags = PropagateChildrenFlags(node.Parameters) |
            (node.Type != null ? TransformFlags.ContainsTypeScript : TransformFlags.None);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.TypeArguments = null; // used in quick info
        return node;
    }

    // @api
    internal JSDocTypeLiteral CreateJSDocTypeLiteral(IReadOnlyList<Node>? propertyTags = null, bool isArrayType = false)
    {
        var node = (JSDocTypeLiteral)CreateBaseDeclaration(SyntaxKind.JSDocTypeLiteral);
        // node.jsDocPropertyTags = asNodeArray(propertyTags) (the generated field is an array, not a NodeArray)
        node.JsDocPropertyTags = AsNodeArray(propertyTags)?.ToArray();
        node.IsArrayType = isArrayType;
        return node;
    }

    // @api
    internal JSDocTypeExpression CreateJSDocTypeExpression(Node type)
    {
        var node = (JSDocTypeExpression)CreateBaseNode(SyntaxKind.JSDocTypeExpression);
        node.Type = type;
        return node;
    }

    // @api
    internal JSDocSignature CreateJSDocSignature(IReadOnlyList<Node>? typeParameters, IReadOnlyList<Node> parameters, JSDocReturnTag? type = null)
    {
        var node = (JSDocSignature)CreateBaseDeclaration(SyntaxKind.JSDocSignature);
        // (The generated `typeParameters` and `parameters` fields are typed arrays, not NodeArrays.)
        node.TypeParameters = ToTypedArray<JSDocTemplateTag>(AsNodeArray(typeParameters));
        node.Parameters = ToTypedArray<JSDocParameterTag>(CreateNodeArray(parameters));
        node.Type = type!; // TypeScript: `type?: JSDocReturnTag` (the generated field is non-nullable)

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        return node;
    }

    /// <summary>Copies a NodeArray into the typed array of a generated field.</summary>
    private static T[]? ToTypedArray<T>(NodeArray? array)
        where T : Node
    {
        if (array == null)
        {
            return null;
        }
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
        var tagName = (Identifier)NodeAccessors.GetTagName(node)!;
        return (string?)tagName.EscapedText == UtilitiesPublic.EscapeLeadingUnderscores(defaultTagName)
            ? tagName
            : CreateIdentifier(defaultTagName);
    }

    // @api
    private Node CreateBaseJSDocTag(SyntaxKind kind, Identifier tagName, object? comment)
    {
        var node = CreateBaseNode(kind);
        SetJSDocTagNameAndComment(node, tagName, comment);
        return node;
    }

    private Node CreateBaseJSDocTagDeclaration(SyntaxKind kind, Identifier tagName, object? comment)
    {
        var node = CreateBaseDeclaration(kind);
        SetJSDocTagNameAndComment(node, tagName, comment);
        return node;
    }

    /// <summary>`node.tagName = tagName; node.comment = comment;` on any JSDoc tag.</summary>
    private static void SetJSDocTagNameAndComment(Node node, Identifier tagName, object? comment)
    {
        switch (node)
        {
            case JSDocAugmentsTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocAuthorTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocCallbackTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocClassTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocDeprecatedTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocEnumTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocImplementsTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocImportTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocOverloadTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocOverrideTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocParameterTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocPrivateTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocPropertyTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocProtectedTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocPublicTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocReadonlyTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocReturnTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocSatisfiesTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocSeeTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocTemplateTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocThisTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocThrowsTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocTypeTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocTypedefTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            case JSDocUnknownTag n:
                n.TagName = tagName;
                n.Comment = comment;
                break;
            default:
                Debug.Fail("Not a JSDoc tag: " + node.Kind.ToString());
                break;
        }
    }

    /// <summary>`node.comment` on any JSDoc tag.</summary>
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
            _ => null,
        };
    }

    /// <summary>JavaScript `a !== b` on two comments (`string | NodeArray | undefined`): strings compare by value.</summary>
    private static bool CommentsDiffer(object? a, object? b)
    {
        return a is string sa && b is string sb ? !string.Equals(sa, sb, StringComparison.Ordinal) : !ReferenceEquals(a, b);
    }

    // @api
    internal JSDocTemplateTag CreateJSDocTemplateTag(Identifier? tagName, JSDocTypeExpression? constraint, IReadOnlyList<Node> typeParameters, object? comment = null)
    {
        var node = (JSDocTemplateTag)CreateBaseJSDocTag(SyntaxKind.JSDocTemplateTag, tagName ?? CreateIdentifier("template"), comment);
        node.Constraint = constraint!; // TypeScript: `constraint: JSDocTypeExpression | undefined` (the generated field is non-nullable)
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

        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    internal JSDocParameterTag CreateJSDocParameterTag(Identifier? tagName, Node name, bool isBracketed, JSDocTypeExpression? typeExpression = null, bool isNameFirst = false, object? comment = null)
    {
        var node = (JSDocParameterTag)CreateBaseJSDocTagDeclaration(SyntaxKind.JSDocParameterTag, tagName ?? CreateIdentifier("param"), comment);
        node.TypeExpression = typeExpression;
        node.Name = name;
        node.IsNameFirst = isNameFirst;
        node.IsBracketed = isBracketed;
        return node;
    }

    // @api
    internal JSDocPropertyTag CreateJSDocPropertyTag(Identifier? tagName, Node name, bool isBracketed, JSDocTypeExpression? typeExpression = null, bool isNameFirst = false, object? comment = null)
    {
        var node = (JSDocPropertyTag)CreateBaseJSDocTagDeclaration(SyntaxKind.JSDocPropertyTag, tagName ?? CreateIdentifier("prop"), comment);
        node.TypeExpression = typeExpression;
        node.Name = name;
        node.IsNameFirst = isNameFirst;
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

        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
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
        var node = (JSDocNameReference)CreateBaseNode(SyntaxKind.JSDocNameReference);
        node.Name = name;
        return node;
    }

    // @api
    internal JSDocMemberName CreateJSDocMemberName(Node left, Identifier right)
    {
        var node = (JSDocMemberName)CreateBaseNode(SyntaxKind.JSDocMemberName);
        node.Left = left;
        node.Right = right;
        node.TransformFlags |= PropagateChildFlags(node.Left) |
            PropagateChildFlags(node.Right);
        return node;
    }

    // @api
    internal JSDocLink CreateJSDocLink(Node? name, string text)
    {
        var node = (JSDocLink)CreateBaseNode(SyntaxKind.JSDocLink);
        node.Name = name;
        node.Text = text;
        return node;
    }

    // @api
    internal JSDocLinkCode CreateJSDocLinkCode(Node? name, string text)
    {
        var node = (JSDocLinkCode)CreateBaseNode(SyntaxKind.JSDocLinkCode);
        node.Name = name;
        node.Text = text;
        return node;
    }

    // @api
    internal JSDocLinkPlain CreateJSDocLinkPlain(Node? name, string text)
    {
        var node = (JSDocLinkPlain)CreateBaseNode(SyntaxKind.JSDocLinkPlain);
        node.Name = name;
        node.Text = text;
        return node;
    }

    // @api
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

    // @api
    // updateJSDocAuthorTag
    // updateJSDocClassTag
    // updateJSDocPublicTag
    // updateJSDocPrivateTag
    // updateJSDocProtectedTag
    // updateJSDocReadonlyTag
    // updateJSDocDeprecatedTag
    private Node UpdateJSDocSimpleTagWorker(SyntaxKind kind, Node node, Identifier? tagName, object? comment)
    {
        // `tagName: Identifier = getDefaultTagName(node)`
        tagName ??= GetDefaultTagName(node);
        return NodeAccessors.GetTagName(node) != tagName
                || CommentsDiffer(GetJSDocTagComment(node), comment)
            ? Update(CreateJSDocSimpleTagWorker(kind, tagName, comment), node) :
            node;
    }

    // @api
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

    // @api
    // updateJSDocTypeTag
    // updateJSDocReturnTag
    // updateJSDocThisTag
    // updateJSDocEnumTag
    // updateJSDocSatisfiesTag
    private Node UpdateJSDocTypeLikeTagWorker(SyntaxKind kind, Node node, Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment)
    {
        // `tagName: Identifier = getDefaultTagName(node)`
        tagName ??= GetDefaultTagName(node);
        return NodeAccessors.GetTagName(node) != tagName
                || NodeAccessors.GetTypeExpression(node) != typeExpression
                || CommentsDiffer(GetJSDocTagComment(node), comment)
            ? Update(CreateJSDocTypeLikeTagWorker(kind, tagName, typeExpression, comment), node)
            : node;
    }

    /// <summary>`node.typeExpression = typeExpression` on the type-like tags (some generated fields are non-nullable).</summary>
    private static void SetJSDocTypeLikeTagTypeExpression(Node node, JSDocTypeExpression? typeExpression)
    {
        switch (node)
        {
            case JSDocTypeTag n:
                n.TypeExpression = typeExpression!;
                break;
            case JSDocReturnTag n:
                n.TypeExpression = typeExpression;
                break;
            case JSDocThisTag n:
                n.TypeExpression = typeExpression!;
                break;
            case JSDocThrowsTag n:
                n.TypeExpression = typeExpression;
                break;
            case JSDocSatisfiesTag n:
                n.TypeExpression = typeExpression!;
                break;
            case JSDocEnumTag n:
                n.TypeExpression = typeExpression!;
                break;
            default:
                Debug.Fail("Not a type-like JSDoc tag: " + node.Kind.ToString());
                break;
        }
    }

    // The getters of the factory object over the JSDoc tag workers.

    internal JSDocTypeTag CreateJSDocTypeTag(Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment = null) => (JSDocTypeTag)CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocTypeTag, tagName, typeExpression, comment);

    internal JSDocTypeTag UpdateJSDocTypeTag(JSDocTypeTag node, Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment) => (JSDocTypeTag)UpdateJSDocTypeLikeTagWorker(SyntaxKind.JSDocTypeTag, node, tagName, typeExpression, comment);

    internal JSDocReturnTag CreateJSDocReturnTag(Identifier? tagName, JSDocTypeExpression? typeExpression = null, object? comment = null) => (JSDocReturnTag)CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocReturnTag, tagName, typeExpression, comment);

    internal JSDocReturnTag UpdateJSDocReturnTag(JSDocReturnTag node, Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment) => (JSDocReturnTag)UpdateJSDocTypeLikeTagWorker(SyntaxKind.JSDocReturnTag, node, tagName, typeExpression, comment);

    internal JSDocThisTag CreateJSDocThisTag(Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment = null) => (JSDocThisTag)CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocThisTag, tagName, typeExpression, comment);

    internal JSDocThisTag UpdateJSDocThisTag(JSDocThisTag node, Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment) => (JSDocThisTag)UpdateJSDocTypeLikeTagWorker(SyntaxKind.JSDocThisTag, node, tagName, typeExpression, comment);

    internal JSDocThrowsTag CreateJSDocThrowsTag(Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment = null) => (JSDocThrowsTag)CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocThrowsTag, tagName, typeExpression, comment);

    internal JSDocThrowsTag UpdateJSDocThrowsTag(JSDocThrowsTag node, Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment = null) => (JSDocThrowsTag)UpdateJSDocTypeLikeTagWorker(SyntaxKind.JSDocThrowsTag, node, tagName, typeExpression, comment);

    internal JSDocSatisfiesTag CreateJSDocSatisfiesTag(Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment = null) => (JSDocSatisfiesTag)CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocSatisfiesTag, tagName, typeExpression, comment);

    internal JSDocSatisfiesTag UpdateJSDocSatisfiesTag(JSDocSatisfiesTag node, Identifier? tagName, JSDocTypeExpression? typeExpression, object? comment) => (JSDocSatisfiesTag)UpdateJSDocTypeLikeTagWorker(SyntaxKind.JSDocSatisfiesTag, node, tagName, typeExpression, comment);

    internal JSDocAuthorTag CreateJSDocAuthorTag(Identifier? tagName, object? comment = null) => (JSDocAuthorTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocAuthorTag, tagName, comment);

    internal JSDocAuthorTag UpdateJSDocAuthorTag(JSDocAuthorTag node, Identifier? tagName, object? comment) => (JSDocAuthorTag)UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocAuthorTag, node, tagName, comment);

    internal JSDocClassTag CreateJSDocClassTag(Identifier? tagName, object? comment = null) => (JSDocClassTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocClassTag, tagName, comment);

    internal JSDocClassTag UpdateJSDocClassTag(JSDocClassTag node, Identifier? tagName, object? comment) => (JSDocClassTag)UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocClassTag, node, tagName, comment);

    internal JSDocPublicTag CreateJSDocPublicTag(Identifier? tagName, object? comment = null) => (JSDocPublicTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocPublicTag, tagName, comment);

    internal JSDocPublicTag UpdateJSDocPublicTag(JSDocPublicTag node, Identifier? tagName, object? comment) => (JSDocPublicTag)UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocPublicTag, node, tagName, comment);

    internal JSDocPrivateTag CreateJSDocPrivateTag(Identifier? tagName, object? comment = null) => (JSDocPrivateTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocPrivateTag, tagName, comment);

    internal JSDocPrivateTag UpdateJSDocPrivateTag(JSDocPrivateTag node, Identifier? tagName, object? comment) => (JSDocPrivateTag)UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocPrivateTag, node, tagName, comment);

    internal JSDocProtectedTag CreateJSDocProtectedTag(Identifier? tagName, object? comment = null) => (JSDocProtectedTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocProtectedTag, tagName, comment);

    internal JSDocProtectedTag UpdateJSDocProtectedTag(JSDocProtectedTag node, Identifier? tagName, object? comment) => (JSDocProtectedTag)UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocProtectedTag, node, tagName, comment);

    internal JSDocReadonlyTag CreateJSDocReadonlyTag(Identifier? tagName, object? comment = null) => (JSDocReadonlyTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocReadonlyTag, tagName, comment);

    internal JSDocReadonlyTag UpdateJSDocReadonlyTag(JSDocReadonlyTag node, Identifier? tagName, object? comment) => (JSDocReadonlyTag)UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocReadonlyTag, node, tagName, comment);

    internal JSDocOverrideTag CreateJSDocOverrideTag(Identifier? tagName, object? comment = null) => (JSDocOverrideTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocOverrideTag, tagName, comment);

    internal JSDocOverrideTag UpdateJSDocOverrideTag(JSDocOverrideTag node, Identifier? tagName, object? comment = null) => (JSDocOverrideTag)UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocOverrideTag, node, tagName, comment);

    internal JSDocDeprecatedTag CreateJSDocDeprecatedTag(Identifier? tagName, object? comment = null) => (JSDocDeprecatedTag)CreateJSDocSimpleTagWorker(SyntaxKind.JSDocDeprecatedTag, tagName, comment);

    internal JSDocDeprecatedTag UpdateJSDocDeprecatedTag(JSDocDeprecatedTag node, Identifier? tagName, object? comment = null) => (JSDocDeprecatedTag)UpdateJSDocSimpleTagWorker(SyntaxKind.JSDocDeprecatedTag, node, tagName, comment);

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

        node.Locals = null; // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
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
        var node = (JSDocText)CreateBaseNode(SyntaxKind.JSDocText);
        node.Text = text;
        return node;
    }

    // @api
    internal JSDoc CreateJSDocComment(object? comment = null, IReadOnlyList<Node>? tags = null)
    {
        var node = (JSDoc)CreateBaseNode(SyntaxKind.JSDoc);
        node.Comment = comment;
        node.Tags = AsNodeArray(tags);
        return node;
    }
}
