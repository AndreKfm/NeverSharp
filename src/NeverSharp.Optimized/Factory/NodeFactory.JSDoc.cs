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
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateJSDocPrimaryTypeWorker(SyntaxKind kind)
    {
        return CreateBaseNode(kind);
    }

    // createJSDocNullableType
    // createJSDocNonNullableType
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateJSDocPrePostfixUnaryTypeWorker(SyntaxKind kind, Node type, bool postfix = false)
    {
        var node = CreateJSDocUnaryTypeWorker(
            kind,
            postfix ? !type.IsNone ? Parenthesizer.ParenthesizeNonArrayTypeOfPostfixType(type) : default : type);
        switch (node)
        {
            case var nullable when store.IsJSDocNullableType(nullable):
                store.SetPostfix(nullable, postfix);
                break;
            case var nonNullable when store.IsJSDocNonNullableType(nonNullable):
                store.SetPostfix(nonNullable, postfix);
                break;
        }
        return node;
    }

    // createJSDocOptionalType
    // createJSDocVariadicType
    // createJSDocNamepathType
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateJSDocUnaryTypeWorker(SyntaxKind kind, Node type)
    {
        var node = CreateBaseNode(kind);
        SetJSDocUnaryType(store, node, type);
        return node;
    }

    // updateJSDocNonNullableType
    // updateJSDocNullableType
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node UpdateJSDocPrePostfixUnaryTypeWorker(SyntaxKind kind, Node node, Node type)
    {
        return GetJSDocUnaryType(store, node) != type
            ? Update(CreateJSDocPrePostfixUnaryTypeWorker(kind, type, (node is var n && store.IsJSDocNullableType(n)) ? store.Postfix(n) : store.Postfix(node)), node)
            : node;
    }

    // updateJSDocOptionalType
    // updateJSDocVariadicType
    // updateJSDocNamepathType
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node UpdateJSDocUnaryTypeWorker(SyntaxKind kind, Node node, Node type)
    {
        return GetJSDocUnaryType(store, node) != type
            ? Update(CreateJSDocUnaryTypeWorker(kind, type), node)
            : node;
    }

    /// <summary>`node.type = type` on the JSDoc unary type nodes.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static void SetJSDocUnaryType(NodeStore store, Node node, Node type)
    {
        switch (node)
        {
            case var n when store.IsJSDocNullableType(n):
                store.SetType(n, type);
                break;
            case var n when store.IsJSDocNonNullableType(n):
                store.SetType(n, type);
                break;
            case var n when store.IsJSDocOptionalType(n):
                store.SetType(n, type);
                break;
            case var n when store.IsJSDocVariadicType(n):
                store.SetType(n, type);
                break;
            case var n when store.IsJSDocNamepathType(n):
                store.SetType(n, type);
                break;
            default:
                throw Debug.UnexpectedKind(store.Kind(node), "a JSDoc unary type");
        }
    }

    /// <summary>`node.type` on the JSDoc unary type nodes.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static Node GetJSDocUnaryType(NodeStore store, Node node)
    {
        return node switch
        {
            var n when store.IsJSDocNullableType(n) => store.Type(n),
            var n when store.IsJSDocNonNullableType(n) => store.Type(n),
            var n when store.IsJSDocOptionalType(n) => store.Type(n),
            var n when store.IsJSDocVariadicType(n) => store.Type(n),
            var n when store.IsJSDocNamepathType(n) => store.Type(n),
            _ => throw Debug.UnexpectedKind(store.Kind(node), "a JSDoc unary type"),
        };
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocAllType() => CreateJSDocPrimaryTypeWorker(SyntaxKind.JSDocAllType);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocUnknownType() => CreateJSDocPrimaryTypeWorker(SyntaxKind.JSDocUnknownType);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocNonNullableType(Node type, bool postfix = false) => CreateJSDocPrePostfixUnaryTypeWorker(SyntaxKind.JSDocNonNullableType, type, postfix);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocNullableType(Node type, bool postfix = false) => CreateJSDocPrePostfixUnaryTypeWorker(SyntaxKind.JSDocNullableType, type, postfix);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocOptionalType(Node type) => CreateJSDocUnaryTypeWorker(SyntaxKind.JSDocOptionalType, type);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocVariadicType(Node type) => CreateJSDocUnaryTypeWorker(SyntaxKind.JSDocVariadicType, type);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocNamepathType(Node type) => CreateJSDocUnaryTypeWorker(SyntaxKind.JSDocNamepathType, type);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocFunctionType(NodeList parameters, Node type)
    {
        var node = CreateBaseDeclaration(SyntaxKind.JSDocFunctionType);
        store.SetParameters(node, AsNodeArray(store, parameters));
        store.SetType(node, type);
        store.SetTransformFlags(node, PropagateChildrenFlags(store.Parameters(node)) |
            (!store.Type(node).IsNone ? TransformFlags.ContainsTypeScript : TransformFlags.None));
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        // node.typeArguments = undefined; // used in quick info
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocTypeLiteral(NodeList propertyTags = default, bool isArrayType = false)
    {
        var node = CreateBaseDeclaration(SyntaxKind.JSDocTypeLiteral);
        // TypeScript assigns `asNodeArray(propertyTags)`; the generated field is an array (Node[]), so the NodeArray's
        // elements are stored.
        var array = AsNodeArray(store, propertyTags);
        store.SetJsDocPropertyTags(node, !array.IsNone ? ToArray(array) : null);
        store.SetIsArrayType(node, isArrayType);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocTypeExpression(Node type)
    {
        var node = CreateBaseNode(SyntaxKind.JSDocTypeExpression);
        store.SetType(node, type);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocSignature(NodeList typeParameters, NodeList parameters, Node type = default)
    {
        var node = CreateBaseDeclaration(SyntaxKind.JSDocSignature);
        // TypeScript assigns NodeArrays (asNodeArray / createNodeArray); the generated fields are typed arrays, so the
        // NodeArrays' elements are stored.
        var typeParametersArray = AsNodeArray(store, typeParameters);
        store.SetJSDocSignatureTypeParameters(node, !typeParametersArray.IsNone ? ToArray(typeParametersArray) : null);
        // (Node[]: besides JSDocParameterTags the parser stores other tags here, e.g. a `@this` tag inside `@callback`.)
        store.SetJSDocSignatureParameters(node, ToArray(CreateNodeArray(store, parameters)));
        store.SetType(node, type);
        // node.jsDoc = undefined; // initialized by parser (JsDocContainer)
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    [NoAllocUnchecked("per-file result: the array of a JSDoc type literal's property tags")]
    private Node[] ToArray(NodeList array)
    {
        return store.Nodes(array).ToArray();
    }

    private Node GetDefaultTagName(Node node)
    {
        var defaultTagName = GetDefaultTagNameForKind(store.Kind(node));
        var tagName = GetJSDocTagName(store, node);
        return store.Texts.Equals(store.EscapedText(tagName), UtilitiesPublic.EscapeLeadingUnderscores(defaultTagName))
            ? tagName
            : CreateIdentifier(defaultTagName);
    }

    /// <summary>`createBaseJSDocTag(kind, tagName, comment: string | NodeArray&lt;JSDocComment&gt; | undefined)`.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateBaseJSDocTag(SyntaxKind kind, Node tagName, TextOrList comment)
    {
        var node = CreateBaseNode(kind);
        SetJSDocTagNameAndComment(store, node, tagName, comment);
        return node;
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateBaseJSDocTagDeclaration(SyntaxKind kind, Node tagName, TextOrList comment)
    {
        var node = CreateBaseDeclaration(kind);
        SetJSDocTagNameAndComment(store, node, tagName, comment);
        return node;
    }

    /// <summary>`node.tagName = tagName; node.comment = comment;` on the JSDoc tag nodes.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static void SetJSDocTagNameAndComment(NodeStore store, Node node, Node tagName, TextOrList comment)
    {
        switch (node)
        {
            case var n when store.IsJSDocAugmentsTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocAuthorTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocCallbackTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocClassTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocDeprecatedTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocEnumTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocImplementsTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocImportTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocOverloadTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocOverrideTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocParameterTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocPrivateTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocPropertyTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocProtectedTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocPublicTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocReadonlyTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocReturnTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocSatisfiesTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocSeeTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocTemplateTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocThisTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocThrowsTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocTypeTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocTypedefTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            case var n when store.IsJSDocUnknownTag(n): store.SetTagName(n, tagName); store.SetComment(n, comment); break;
            default: throw Debug.UnexpectedKind(store.Kind(node), "a JSDoc tag");
        }
    }

    /// <summary>`node.tagName` on the JSDoc tag nodes.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static Node GetJSDocTagName(NodeStore store, Node node)
    {
        return node switch
        {
            var n when store.IsJSDocAugmentsTag(n) => store.TagName(n),
            var n when store.IsJSDocAuthorTag(n) => store.TagName(n),
            var n when store.IsJSDocCallbackTag(n) => store.TagName(n),
            var n when store.IsJSDocClassTag(n) => store.TagName(n),
            var n when store.IsJSDocDeprecatedTag(n) => store.TagName(n),
            var n when store.IsJSDocEnumTag(n) => store.TagName(n),
            var n when store.IsJSDocImplementsTag(n) => store.TagName(n),
            var n when store.IsJSDocImportTag(n) => store.TagName(n),
            var n when store.IsJSDocOverloadTag(n) => store.TagName(n),
            var n when store.IsJSDocOverrideTag(n) => store.TagName(n),
            var n when store.IsJSDocParameterTag(n) => store.TagName(n),
            var n when store.IsJSDocPrivateTag(n) => store.TagName(n),
            var n when store.IsJSDocPropertyTag(n) => store.TagName(n),
            var n when store.IsJSDocProtectedTag(n) => store.TagName(n),
            var n when store.IsJSDocPublicTag(n) => store.TagName(n),
            var n when store.IsJSDocReadonlyTag(n) => store.TagName(n),
            var n when store.IsJSDocReturnTag(n) => store.TagName(n),
            var n when store.IsJSDocSatisfiesTag(n) => store.TagName(n),
            var n when store.IsJSDocSeeTag(n) => store.TagName(n),
            var n when store.IsJSDocTemplateTag(n) => store.TagName(n),
            var n when store.IsJSDocThisTag(n) => store.TagName(n),
            var n when store.IsJSDocThrowsTag(n) => store.TagName(n),
            var n when store.IsJSDocTypeTag(n) => store.TagName(n),
            var n when store.IsJSDocTypedefTag(n) => store.TagName(n),
            var n when store.IsJSDocUnknownTag(n) => store.TagName(n),
            _ => throw Debug.UnexpectedKind(store.Kind(node), "a JSDoc tag"),
        };
    }

    /// <summary>`node.comment` on the JSDoc tag nodes.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static TextOrList GetJSDocTagComment(NodeStore store, Node node)
    {
        return node switch
        {
            var n when store.IsJSDocAugmentsTag(n) => store.Comment(n),
            var n when store.IsJSDocAuthorTag(n) => store.Comment(n),
            var n when store.IsJSDocCallbackTag(n) => store.Comment(n),
            var n when store.IsJSDocClassTag(n) => store.Comment(n),
            var n when store.IsJSDocDeprecatedTag(n) => store.Comment(n),
            var n when store.IsJSDocEnumTag(n) => store.Comment(n),
            var n when store.IsJSDocImplementsTag(n) => store.Comment(n),
            var n when store.IsJSDocImportTag(n) => store.Comment(n),
            var n when store.IsJSDocOverloadTag(n) => store.Comment(n),
            var n when store.IsJSDocOverrideTag(n) => store.Comment(n),
            var n when store.IsJSDocParameterTag(n) => store.Comment(n),
            var n when store.IsJSDocPrivateTag(n) => store.Comment(n),
            var n when store.IsJSDocPropertyTag(n) => store.Comment(n),
            var n when store.IsJSDocProtectedTag(n) => store.Comment(n),
            var n when store.IsJSDocPublicTag(n) => store.Comment(n),
            var n when store.IsJSDocReadonlyTag(n) => store.Comment(n),
            var n when store.IsJSDocReturnTag(n) => store.Comment(n),
            var n when store.IsJSDocSatisfiesTag(n) => store.Comment(n),
            var n when store.IsJSDocSeeTag(n) => store.Comment(n),
            var n when store.IsJSDocTemplateTag(n) => store.Comment(n),
            var n when store.IsJSDocThisTag(n) => store.Comment(n),
            var n when store.IsJSDocThrowsTag(n) => store.Comment(n),
            var n when store.IsJSDocTypeTag(n) => store.Comment(n),
            var n when store.IsJSDocTypedefTag(n) => store.Comment(n),
            var n when store.IsJSDocUnknownTag(n) => store.Comment(n),
            _ => throw Debug.UnexpectedKind(store.Kind(node), "a JSDoc tag"),
        };
    }

    /// <summary>`node.typeExpression = typeExpression` on the type-like JSDoc tags.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static void SetJSDocTypeLikeTagTypeExpression(NodeStore store, Node node, Node typeExpression)
    {
        switch (node)
        {
            case var n when store.IsJSDocTypeTag(n): store.SetTypeExpression(n, typeExpression); break;
            case var n when store.IsJSDocReturnTag(n): store.SetTypeExpression(n, typeExpression); break;
            case var n when store.IsJSDocThisTag(n): store.SetTypeExpression(n, typeExpression); break;
            case var n when store.IsJSDocThrowsTag(n): store.SetTypeExpression(n, typeExpression); break;
            case var n when store.IsJSDocSatisfiesTag(n): store.SetTypeExpression(n, typeExpression); break;
            case var n when store.IsJSDocEnumTag(n): store.SetTypeExpression(n, typeExpression); break;
            default: throw Debug.UnexpectedKind(store.Kind(node), "a type-like JSDoc tag");
        }
    }

    /// <summary>`node.typeExpression` on the type-like JSDoc tags.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static Node GetJSDocTypeLikeTagTypeExpression(NodeStore store, Node node)
    {
        return node switch
        {
            var n when store.IsJSDocTypeTag(n) => store.TypeExpression(n),
            var n when store.IsJSDocReturnTag(n) => store.TypeExpression(n),
            var n when store.IsJSDocThisTag(n) => store.TypeExpression(n),
            var n when store.IsJSDocThrowsTag(n) => store.TypeExpression(n),
            var n when store.IsJSDocSatisfiesTag(n) => store.TypeExpression(n),
            var n when store.IsJSDocEnumTag(n) => store.TypeExpression(n),
            _ => throw Debug.UnexpectedKind(store.Kind(node), "a type-like JSDoc tag"),
        };
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocTemplateTag(Node tagName, Node constraint, NodeList typeParameters, TextOrList comment = default)
    {
        var node = CreateBaseJSDocTag(SyntaxKind.JSDocTemplateTag, (tagName.IsNone ? CreateIdentifier("template") : tagName), comment);
        store.SetConstraint(node, constraint);
        store.SetTypeParameters(node, CreateNodeArray(store, typeParameters));
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocTypedefTag(Node tagName, Node typeExpression = default, Node fullName = default, TextOrList comment = default)
    {
        var node = CreateBaseJSDocTagDeclaration(SyntaxKind.JSDocTypedefTag, (tagName.IsNone ? CreateIdentifier("typedef") : tagName), comment);
        store.SetTypeExpression(node, typeExpression);
        store.SetFullName(node, fullName);
        store.SetName(node, FactoryUtilities.GetJSDocTypeAliasName(store, fullName));
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocParameterTag(Node tagName, Node name, bool isBracketed, Node typeExpression = default, bool? isNameFirst = null, TextOrList comment = default)
    {
        var node = CreateBaseJSDocTagDeclaration(SyntaxKind.JSDocParameterTag, (tagName.IsNone ? CreateIdentifier("param") : tagName), comment);
        store.SetTypeExpression(node, typeExpression);
        store.SetName(node, name);
        store.SetIsNameFirst(node, isNameFirst == true);
        store.SetIsBracketed(node, isBracketed);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocPropertyTag(Node tagName, Node name, bool isBracketed, Node typeExpression = default, bool? isNameFirst = null, TextOrList comment = default)
    {
        var node = CreateBaseJSDocTagDeclaration(SyntaxKind.JSDocPropertyTag, (tagName.IsNone ? CreateIdentifier("prop") : tagName), comment);
        store.SetTypeExpression(node, typeExpression);
        store.SetName(node, name);
        store.SetIsNameFirst(node, isNameFirst == true);
        store.SetIsBracketed(node, isBracketed);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocCallbackTag(Node tagName, Node typeExpression, Node fullName = default, TextOrList comment = default)
    {
        var node = CreateBaseJSDocTagDeclaration(SyntaxKind.JSDocCallbackTag, (tagName.IsNone ? CreateIdentifier("callback") : tagName), comment);
        store.SetTypeExpression(node, typeExpression);
        store.SetFullName(node, fullName);
        store.SetName(node, FactoryUtilities.GetJSDocTypeAliasName(store, fullName));
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocOverloadTag(Node tagName, Node typeExpression, TextOrList comment = default)
    {
        var node = CreateBaseJSDocTag(SyntaxKind.JSDocOverloadTag, (tagName.IsNone ? CreateIdentifier("overload") : tagName), comment);
        store.SetTypeExpression(node, typeExpression);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocAugmentsTag(Node tagName, Node className, TextOrList comment = default)
    {
        var node = CreateBaseJSDocTag(SyntaxKind.JSDocAugmentsTag, (tagName.IsNone ? CreateIdentifier("augments") : tagName), comment);
        store.SetClass(node, className);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocImplementsTag(Node tagName, Node className, TextOrList comment = default)
    {
        var node = CreateBaseJSDocTag(SyntaxKind.JSDocImplementsTag, (tagName.IsNone ? CreateIdentifier("implements") : tagName), comment);
        store.SetClass(node, className);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocSeeTag(Node tagName, Node name, TextOrList comment = default)
    {
        var node = CreateBaseJSDocTag(SyntaxKind.JSDocSeeTag, (tagName.IsNone ? CreateIdentifier("see") : tagName), comment);
        store.SetName(node, name);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocNameReference(Node name)
    {
        var node = CreateBaseNode(SyntaxKind.JSDocNameReference);
        store.SetName(node, name);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocMemberName(Node left, Node right)
    {
        var node = CreateBaseNode(SyntaxKind.JSDocMemberName);
        store.SetLeft(node, left);
        store.SetRight(node, right);
        store.SetTransformFlags(node, store.TransformFlags(node) | (PropagateChildFlags(store, store.Left(node)) |
            PropagateChildFlags(store, store.Right(node))));
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocLink(Node name, TextRef text)
    {
        var node = CreateBaseNode(SyntaxKind.JSDocLink);
        store.SetName(node, name);
        store.SetText(node, text);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocLinkCode(Node name, TextRef text)
    {
        var node = CreateBaseNode(SyntaxKind.JSDocLinkCode);
        store.SetName(node, name);
        store.SetText(node, text);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocLinkPlain(Node name, TextRef text)
    {
        var node = CreateBaseNode(SyntaxKind.JSDocLinkPlain);
        store.SetName(node, name);
        store.SetText(node, text);
        return node;
    }

    // createJSDocAuthorTag
    // createJSDocClassTag
    // createJSDocPublicTag
    // createJSDocPrivateTag
    // createJSDocProtectedTag
    // createJSDocReadonlyTag
    // createJSDocDeprecatedTag
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateJSDocSimpleTagWorker(SyntaxKind kind, Node tagName, TextOrList comment = default)
    {
        var node = CreateBaseJSDocTag(kind, (tagName.IsNone ? CreateIdentifier(GetDefaultTagNameForKind(kind)) : tagName), comment);
        return node;
    }

    // updateJSDocAuthorTag
    // updateJSDocClassTag
    // updateJSDocPublicTag
    // updateJSDocPrivateTag
    // updateJSDocProtectedTag
    // updateJSDocReadonlyTag
    // updateJSDocDeprecatedTag
    private Node UpdateJSDocSimpleTagWorker(SyntaxKind kind, Node node, Node tagName, TextOrList comment)
    {
        tagName = tagName.IsNone ? GetDefaultTagName(node) : tagName;
        return GetJSDocTagName(store, node) != tagName
                || !store.Texts.Equals(GetJSDocTagComment(store, node), comment) // `!==`: strings by value, NodeArrays by reference
            ? Update(CreateJSDocSimpleTagWorker(kind, tagName, comment), node) :
            node;
    }

    // createJSDocTypeTag
    // createJSDocReturnTag
    // createJSDocThisTag
    // createJSDocEnumTag
    // createJSDocSatisfiesTag
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private Node CreateJSDocTypeLikeTagWorker(SyntaxKind kind, Node tagName, Node typeExpression = default, TextOrList comment = default)
    {
        var node = CreateBaseJSDocTag(kind, (tagName.IsNone ? CreateIdentifier(GetDefaultTagNameForKind(kind)) : tagName), comment);
        SetJSDocTypeLikeTagTypeExpression(store, node, typeExpression);
        return node;
    }

    // updateJSDocTypeTag
    // updateJSDocReturnTag
    // updateJSDocThisTag
    // updateJSDocEnumTag
    // updateJSDocSatisfiesTag
    private Node UpdateJSDocTypeLikeTagWorker(SyntaxKind kind, Node node, Node tagName, Node typeExpression, TextOrList comment)
    {
        tagName = tagName.IsNone ? GetDefaultTagName(node) : tagName;
        return GetJSDocTagName(store, node) != tagName
                || GetJSDocTypeLikeTagTypeExpression(store, node) != typeExpression
                || !store.Texts.Equals(GetJSDocTagComment(store, node), comment) // `!==`: strings by value, NodeArrays by reference
            ? Update(CreateJSDocTypeLikeTagWorker(kind, tagName, typeExpression, comment), node)
            : node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocTypeTag(Node tagName, Node typeExpression = default, TextOrList comment = default) => CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocTypeTag, tagName, typeExpression, comment);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocReturnTag(Node tagName, Node typeExpression = default, TextOrList comment = default) => CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocReturnTag, tagName, typeExpression, comment);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocThisTag(Node tagName, Node typeExpression = default, TextOrList comment = default) => CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocThisTag, tagName, typeExpression, comment);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocThrowsTag(Node tagName, Node typeExpression = default, TextOrList comment = default) => CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocThrowsTag, tagName, typeExpression, comment);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocSatisfiesTag(Node tagName, Node typeExpression = default, TextOrList comment = default) => CreateJSDocTypeLikeTagWorker(SyntaxKind.JSDocSatisfiesTag, tagName, typeExpression, comment);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocAuthorTag(Node tagName, TextOrList comment = default) => CreateJSDocSimpleTagWorker(SyntaxKind.JSDocAuthorTag, tagName, comment);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocClassTag(Node tagName, TextOrList comment = default) => CreateJSDocSimpleTagWorker(SyntaxKind.JSDocClassTag, tagName, comment);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocPublicTag(Node tagName, TextOrList comment = default) => CreateJSDocSimpleTagWorker(SyntaxKind.JSDocPublicTag, tagName, comment);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocPrivateTag(Node tagName, TextOrList comment = default) => CreateJSDocSimpleTagWorker(SyntaxKind.JSDocPrivateTag, tagName, comment);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocProtectedTag(Node tagName, TextOrList comment = default) => CreateJSDocSimpleTagWorker(SyntaxKind.JSDocProtectedTag, tagName, comment);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocReadonlyTag(Node tagName, TextOrList comment = default) => CreateJSDocSimpleTagWorker(SyntaxKind.JSDocReadonlyTag, tagName, comment);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocOverrideTag(Node tagName, TextOrList comment = default) => CreateJSDocSimpleTagWorker(SyntaxKind.JSDocOverrideTag, tagName, comment);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocDeprecatedTag(Node tagName, TextOrList comment = default) => CreateJSDocSimpleTagWorker(SyntaxKind.JSDocDeprecatedTag, tagName, comment);

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocUnknownTag(Node tagName, TextOrList comment = default)
    {
        var node = CreateBaseJSDocTag(SyntaxKind.JSDocTag, tagName, comment);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocEnumTag(Node tagName, Node typeExpression, TextOrList comment = default)
    {
        var node = CreateBaseJSDocTagDeclaration(SyntaxKind.JSDocEnumTag, (tagName.IsNone ? CreateIdentifier(GetDefaultTagNameForKind(SyntaxKind.JSDocEnumTag)) : tagName), comment);
        store.SetTypeExpression(node, typeExpression);
        // node.locals = undefined; // initialized by binder (LocalsContainer)
        // node.nextContainer = undefined; // initialized by binder (LocalsContainer)
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocImportTag(Node tagName, Node importClause, Node moduleSpecifier, Node attributes = default, TextOrList comment = default)
    {
        var node = CreateBaseJSDocTag(SyntaxKind.JSDocImportTag, (tagName.IsNone ? CreateIdentifier("import") : tagName), comment);
        store.SetImportClause(node, importClause);
        store.SetModuleSpecifier(node, moduleSpecifier);
        store.SetAttributes(node, attributes);
        store.SetComment(node, comment);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocText(TextRef text)
    {
        var node = CreateBaseNode(SyntaxKind.JSDocText);
        store.SetText(node, text);
        return node;
    }

    // @api
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal Node CreateJSDocComment(TextOrList comment = default, NodeList tags = default)
    {
        var node = CreateBaseNode(SyntaxKind.JSDoc);
        store.SetComment(node, comment);
        store.SetTags(node, AsNodeArray(store, tags));
        return node;
    }
}
