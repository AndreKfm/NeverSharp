// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilitiesPublic.ts and src/compiler/utilities.ts (the JSDoc accessors the checker uses for
// JavaScript files) and src/compiler/checker.ts (JSDoc typing; the functions are listed below)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C9d.
//
// Functions: getJSDocType, getJSDocReturnType, getJSDocReturnTag, getJSDocThisTag, getJSDocClassTag,
// getJSDocEnumTag, getJSDocSatisfiesTag, hasJSDocParameterTags, isJSDocOptionalParameter,
// isOptionalJSDocPropertyLikeTag, getJSDocTypeParameterDeclarations, getEffectiveTypeParameterDeclarations,
// isJSDocPropertyLikeTag, getTypeForDeclarationFromJSDocComment, getParameterTypeOfTypeTag, getSignatureOfTypeTag,
// getReturnTypeOfTypeTag, getTypeFromJSDocVariadicType.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // utilitiesPublic.ts getFirstJSDocTag (by kind)
    internal static Node GetFirstJSDocTagOfKind(NodeStore store, Node node, SyntaxKind kind)
    {
        foreach (var tag in UtilitiesPublic.GetJSDocTags(store, node))
        {
            if (store.Kind(tag) == kind)
            {
                return tag;
            }
        }
        return Node.None;
    }

    // utilitiesPublic.ts getJSDocType
    internal static Node GetJSDocType(NodeStore store, Node node)
    {
        var tag = GetFirstJSDocTagOfKind(store, node, SyntaxKind.JSDocTypeTag);
        if (tag.IsNone && store.Kind(node) == SyntaxKind.Parameter)
        {
            foreach (var paramTag in UtilitiesPublic.GetJSDocParameterTags(store, node))
            {
                if (!store.TypeExpression(paramTag).IsNone)
                {
                    tag = paramTag;
                    break;
                }
            }
        }
        if (tag.IsNone)
        {
            return Node.None;
        }
        var typeExpression = store.TypeExpression(tag);
        return typeExpression.IsNone ? Node.None : store.Type(typeExpression);
    }

    // utilitiesPublic.ts getJSDocReturnType
    internal static Node GetJSDocReturnType(NodeStore store, Node node)
    {
        var returnTag = GetFirstJSDocTagOfKind(store, node, SyntaxKind.JSDocReturnTag);
        if (!returnTag.IsNone && !store.TypeExpression(returnTag).IsNone)
        {
            return store.Type(store.TypeExpression(returnTag));
        }
        var typeTag = UtilitiesPublic.GetJSDocTypeTag(store, node);
        if (!typeTag.IsNone && !store.TypeExpression(typeTag).IsNone)
        {
            var type = store.Type(store.TypeExpression(typeTag));
            if (store.Kind(type) == SyntaxKind.TypeLiteral)
            {
                foreach (var member in store.Nodes(store.Members(type)))
                {
                    if (store.Kind(member) == SyntaxKind.CallSignature)
                    {
                        return store.Type(member);
                    }
                }
                return Node.None;
            }
            if (store.Kind(type) is SyntaxKind.FunctionType or SyntaxKind.JSDocFunctionType)
            {
                return store.Type(type);
            }
        }
        return Node.None;
    }

    // utilitiesPublic.ts hasJSDocParameterTags
    internal static bool HasJSDocParameterTags(NodeStore store, Node node) => !GetFirstJSDocTagOfKind(store, node, SyntaxKind.JSDocParameterTag).IsNone;

    // utilitiesPublic.ts isJSDocPropertyLikeTag
    internal static bool IsJSDocPropertyLikeTag(NodeStore store, Node node) => store.Kind(node) is SyntaxKind.JSDocPropertyTag or SyntaxKind.JSDocParameterTag;

    // utilities.ts isOptionalJSDocPropertyLikeTag
    internal static bool IsOptionalJSDocPropertyLikeTag(NodeStore store, Node node)
    {
        if (!IsJSDocPropertyLikeTag(store, node))
        {
            return false;
        }
        var typeExpression = store.TypeExpression(node);
        return store.IsBracketed(node) || (!typeExpression.IsNone && store.Kind(store.Type(typeExpression)) == SyntaxKind.JSDocOptionalType);
    }

    // utilities.ts isJSDocOptionalParameter
    internal static bool IsJSDocOptionalParameter(NodeStore store, Node node)
    {
        if (!Utilities.IsInJSFile(store, node))
        {
            return false;
        }
        // node.type should only be a JSDocOptionalType when node is a parameter of a JSDocFunctionType
        var type = store.Type(node);
        if (!type.IsNone && store.Kind(type) == SyntaxKind.JSDocOptionalType)
        {
            return true;
        }
        foreach (var tag in UtilitiesPublic.GetJSDocParameterTags(store, node))
        {
            if (IsOptionalJSDocPropertyLikeTag(store, tag))
            {
                return true;
            }
        }
        return false;
    }

    // utilities.ts isNonTypeAliasTemplate
    private static bool IsNonTypeAliasTemplate(NodeStore store, Node tag)
    {
        if (store.Kind(tag) != SyntaxKind.JSDocTemplateTag)
        {
            return false;
        }
        var jsDoc = store.Parent(tag);
        if (store.Kind(jsDoc) != SyntaxKind.JSDoc)
        {
            return true;
        }
        foreach (var t in store.Nodes(store.Tags(jsDoc)))
        {
            if (Utilities.IsJSDocTypeAlias(store, t))
            {
                return false;
            }
        }
        return true;
    }

    // utilities.ts getJSDocTypeParameterDeclarations
    internal static List<Node>? GetJSDocTypeParameterDeclarations(NodeStore store, Node node)
    {
        List<Node>? result = null;
        foreach (var tag in UtilitiesPublic.GetJSDocTags(store, node))
        {
            if (IsNonTypeAliasTemplate(store, tag))
            {
                foreach (var tp in store.Nodes(store.TypeParameters(tag)))
                {
                    (result ??= []).Add(tp);
                }
            }
        }
        return result;
    }

    // utilitiesPublic.ts getEffectiveTypeParameterDeclarations
    internal static IReadOnlyList<Node> GetEffectiveTypeParameterDeclarations(NodeStore store, Node node)
    {
        var kind = store.Kind(node);
        if (kind == SyntaxKind.JSDocSignature)
        {
            if (store.Kind(store.Parent(node)) == SyntaxKind.JSDocOverloadTag)
            {
                var jsDoc = GetJSDocRoot(store, store.Parent(node));
                if (!jsDoc.IsNone)
                {
                    List<Node>? result = null;
                    foreach (var tag in store.Nodes(store.Tags(jsDoc)))
                    {
                        if (store.Kind(tag) == SyntaxKind.JSDocTemplateTag)
                        {
                            foreach (var tp in store.Nodes(store.TypeParameters(tag)))
                            {
                                (result ??= []).Add(tp);
                            }
                        }
                    }
                    return result ?? (IReadOnlyList<Node>)[];
                }
            }
            return [];
        }
        if (Utilities.IsJSDocTypeAlias(store, node))
        {
            List<Node>? result = null;
            foreach (var tag in store.Nodes(store.Tags(store.Parent(node))))
            {
                if (store.Kind(tag) == SyntaxKind.JSDocTemplateTag)
                {
                    foreach (var tp in store.Nodes(store.TypeParameters(tag)))
                    {
                        (result ??= []).Add(tp);
                    }
                }
            }
            return result ?? (IReadOnlyList<Node>)[];
        }
        var typeParameters = store.TypeParameters(node);
        if (!typeParameters.IsNone)
        {
            return store.Nodes(typeParameters).ToArray();
        }
        if (Utilities.IsInJSFile(store, node))
        {
            var decls = GetJSDocTypeParameterDeclarations(store, node);
            if (decls is { Count: > 0 })
            {
                return decls;
            }
            var typeTag = UtilitiesPublic.GetJSDocTypeTag(store, node);
            if (!typeTag.IsNone)
            {
                var type = store.Type(store.TypeExpression(typeTag));
                if (!type.IsNone && store.Kind(type) is SyntaxKind.FunctionType or SyntaxKind.ConstructorType or SyntaxKind.JSDocFunctionType)
                {
                    var tps = store.TypeParameters(type);
                    if (!tps.IsNone)
                    {
                        return store.Nodes(tps).ToArray();
                    }
                }
            }
        }
        return [];
    }

    /// <summary>utilities.ts getEffectiveReturnTypeNode.</summary>
    internal static Node GetEffectiveReturnTypeNodeOf(NodeStore store, Node node)
    {
        if (store.Kind(node) == SyntaxKind.JSDocSignature)
        {
            var returnTag = store.Type(node);
            if (returnTag.IsNone)
            {
                return Node.None;
            }
            var typeExpression = store.TypeExpression(returnTag);
            return typeExpression.IsNone ? Node.None : store.Type(typeExpression);
        }
        var type = store.Type(node);
        return !type.IsNone || !Utilities.IsInJSFile(store, node) ? type : GetJSDocReturnType(store, node);
    }

    // checker.ts getTypeForDeclarationFromJSDocComment
    private Type GetTypeForDeclarationFromJSDocComment(NodeRef declaration)
    {
        var jsdocType = GetJSDocType(StoreOf(declaration), declaration.Node);
        return jsdocType.IsNone ? Type.None : GetTypeFromTypeNode(At(declaration, jsdocType));
    }

    // checker.ts getSignatureOfTypeTag
    private Signature GetSignatureOfTypeTag(NodeRef node)
    {
        // should be attached to a function declaration or expression
        var store = StoreOf(node);
        if (!Utilities.IsInJSFile(store, node.Node) || !Utilities.IsFunctionLikeDeclaration(store, node.Node))
        {
            return Signature.None;
        }
        var typeTag = UtilitiesPublic.GetJSDocTypeTag(store, node.Node);
        if (typeTag.IsNone)
        {
            return Signature.None;
        }
        return GetSingleCallSignature(GetTypeFromTypeNode(At(node, store.TypeExpression(typeTag))));
    }

    // checker.ts getParameterTypeOfTypeTag
    private Type GetParameterTypeOfTypeTag(NodeRef func, NodeRef parameter)
    {
        var signature = GetSignatureOfTypeTag(func);
        if (signature.IsNone)
        {
            return Type.None;
        }
        var store = StoreOf(func);
        var pos = store.Nodes(store.Parameters(func.Node)).IndexOf(parameter.Node);
        return !store.DotDotDotToken(parameter.Node).IsNone ? GetRestTypeAtPosition(signature, pos) : GetTypeAtPosition(signature, pos);
    }

    // The parameters of a signature declaration (JSDoc signatures keep theirs as parameter tags).
    internal static ReadOnlySpan<Node> ParametersOfSignature(NodeStore store, Node node) =>
        store.Kind(node) == SyntaxKind.JSDocSignature ? new ReadOnlySpan<Node>(store.JSDocSignatureParameters(node)) : store.Nodes(store.Parameters(node));

    // utilitiesPublic.ts isRestParameter
    internal static bool IsRestParameterOrTag(NodeStore store, Node node)
    {
        if (store.Kind(node) == SyntaxKind.JSDocParameterTag)
        {
            var typeExpression = store.TypeExpression(node);
            return !typeExpression.IsNone && store.Kind(store.Type(typeExpression)) == SyntaxKind.JSDocVariadicType;
        }
        var type = store.Type(node);
        return !store.DotDotDotToken(node).IsNone || (!type.IsNone && store.Kind(type) == SyntaxKind.JSDocVariadicType);
    }

    // utilities.ts getParameterSymbolFromJSDoc
    private SymbolRef GetParameterSymbolFromJSDoc(NodeRef node)
    {
        var store = StoreOf(node);
        var own = NodeSymbol(node);
        if (!own.IsNone)
        {
            return own;
        }
        var name = store.Name(node.Node);
        if (store.Kind(name) != SyntaxKind.Identifier)
        {
            return SymbolRef.None;
        }
        var decl = Utilities.GetHostSignatureFromJSDoc(store, node.Node);
        if (decl.IsNone)
        {
            return SymbolRef.None;
        }
        foreach (var p in ParametersOfSignature(store, decl))
        {
            var pName = store.Name(p);
            if (store.Kind(pName) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(pName), store.EscapedText(name)))
            {
                return NodeSymbol(At(node, p));
            }
        }
        return SymbolRef.None;
    }

    // checker.ts getTypeFromJSDocVariadicType
    private Type GetTypeFromJSDocVariadicType(NodeRef node)
    {
        var store = StoreOf(node);
        var type = GetTypeFromTypeNode(At(node, store.Type(node.Node)));
        var parent = store.Parent(node.Node);
        var paramTag = store.Parent(parent);
        if (store.Kind(parent) == SyntaxKind.JSDocTypeExpression && store.Kind(paramTag) == SyntaxKind.JSDocParameterTag)
        {
            // Else we will add a diagnostic, see `checkJSDocVariadicType`.
            var host = Utilities.GetHostSignatureFromJSDoc(store, paramTag);
            var callbackTag = store.Parent(store.Parent(paramTag));
            var isCallbackTag = store.Kind(callbackTag) == SyntaxKind.JSDocCallbackTag;
            if (!host.IsNone || isCallbackTag)
            {
                // Only return an array type if the corresponding parameter is marked as a rest parameter, or if there are no parameters.
                var parameters = isCallbackTag ? ParametersOfSignature(store, store.TypeExpression(callbackTag)) : ParametersOfSignature(store, host);
                var lastParamDeclaration = parameters.Length > 0 ? parameters[^1] : Node.None;
                var symbol = GetParameterSymbolFromJSDoc(At(node, paramTag));
                if (lastParamDeclaration.IsNone ||
                    (!symbol.IsNone && NodeSymbol(At(node, lastParamDeclaration)) == symbol && IsRestParameterOrTag(store, lastParamDeclaration)))
                {
                    return CreateArrayType(type);
                }
            }
        }
        if (store.Kind(parent) == SyntaxKind.Parameter && store.Kind(store.Parent(parent)) == SyntaxKind.JSDocFunctionType)
        {
            return CreateArrayType(type);
        }
        return AddOptionality(type, isProperty: false, isOptional: true);
    }

    // utilities.ts isValueSignatureDeclaration
    private static bool IsValueSignatureDeclarationKind(SyntaxKind kind) => kind is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction
        or SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor or SyntaxKind.FunctionDeclaration or SyntaxKind.Constructor;

    // some(declaration.parameters, p => !!getJSDocType(p))
    private static bool HasParameterWithJSDocType(NodeStore store, ReadOnlySpan<Node> parameters)
    {
        foreach (var p in parameters)
        {
            if (!GetJSDocType(store, p).IsNone)
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts getTypeParametersFromDeclaration
    private TypeList GetTypeParametersFromDeclaration(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        List<Type>? result = null;
        foreach (var node in GetEffectiveTypeParameterDeclarations(store, declaration.Node))
        {
            var tp = GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(At(declaration, node)));
            result ??= [];
            if (!result.Contains(tp))
            {
                result.Add(tp);
            }
        }
        if (result is { Count: > 0 })
        {
            return Types.CreateList(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(result));
        }
        if (store.Kind(declaration.Node) == SyntaxKind.FunctionDeclaration)
        {
            var signature = GetSignatureOfTypeTag(declaration);
            if (!signature.IsNone)
            {
                return Signatures.Record(signature).TypeParameters;
            }
        }
        return TypeList.Empty;
    }

    // checker.ts maybeAddJsSyntheticRestParameter: a JS function gets a synthetic rest parameter if it references
    // `arguments` and has no parameters but a `@param` with a `...` type, or its last parameter has one, or no annotation.
    private bool MaybeAddJsSyntheticRestParameter(NodeRef declaration, List<SymbolRef> parameters)
    {
        var store = StoreOf(declaration);
        if (store.Kind(declaration.Node) == SyntaxKind.JSDocSignature || !ContainsArgumentsReference(declaration))
        {
            return false;
        }
        var declParameters = store.Nodes(store.Parameters(declaration.Node));
        IEnumerable<Node> lastParamTags;
        if (declParameters.Length > 0)
        {
            lastParamTags = UtilitiesPublic.GetJSDocParameterTags(store, declParameters[^1]);
        }
        else
        {
            var tags = new List<Node>();
            foreach (var tag in UtilitiesPublic.GetJSDocTags(store, declaration.Node))
            {
                if (store.Kind(tag) == SyntaxKind.JSDocParameterTag)
                {
                    tags.Add(tag);
                }
            }
            lastParamTags = tags;
        }
        var lastParamVariadicType = Node.None;
        foreach (var p in lastParamTags)
        {
            var typeExpression = store.TypeExpression(p);
            if (!typeExpression.IsNone && store.Kind(store.Type(typeExpression)) == SyntaxKind.JSDocVariadicType)
            {
                lastParamVariadicType = store.Type(typeExpression);
                break;
            }
        }
        var syntheticArgsSymbol = CreateSymbol(SymbolFlags.Variable, "args", CheckFlags.RestParameter);
        if (!lastParamVariadicType.IsNone)
        {
            // Parameter has effective annotation, lock in type
            ValueSymbolLinks.Get(syntheticArgsSymbol).ResolvedType = CreateArrayType(GetTypeFromTypeNode(At(declaration, store.Type(lastParamVariadicType))));
        }
        else
        {
            // Parameter has no annotation
            // By using a `DeferredType` symbol, we allow the type of this rest arg to be overriden by contextual type assignment so long as its type hasn't been
            // cached by `getTypeOfSymbol` yet.
            TransientSymbols.Record(syntheticArgsSymbol).CheckFlags |= CheckFlags.DeferredType;
            deferredTypeSymbols[syntheticArgsSymbol] = (neverType, [anyArrayType], [anyArrayType]);
        }
        if (!lastParamVariadicType.IsNone && parameters.Count > 0)
        {
            // Replace the last parameter with a rest parameter (Array.pop of an empty array does nothing).
            parameters.RemoveAt(parameters.Count - 1);
        }
        parameters.Add(syntheticArgsSymbol);
        return true;
    }

    // checker.ts containsArgumentsReference (links.containsArgumentsReference)
    private readonly Dictionary<NodeRef, bool> containsArgumentsReferences = [];

    private bool ContainsArgumentsReference(NodeRef declaration)
    {
        if (containsArgumentsReferences.TryGetValue(declaration, out var cached))
        {
            return cached;
        }
        var store = StoreOf(declaration);
        var result = Traverse(store.Body(declaration.Node));
        containsArgumentsReferences[declaration] = result;
        return result;

        bool Traverse(Node node)
        {
            if (node.IsNone)
            {
                return false;
            }
            switch (store.Kind(node))
            {
                case SyntaxKind.Identifier:
                    return store.Texts.Equals(store.EscapedText(node), "arguments") &&
                        ResolveName(At(declaration, node), "arguments", NodeRef.None, SymbolFlags.Value | SymbolFlags.ExportValue | SymbolFlags.Alias, null, isUse: true) == argumentsSymbol;
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    return store.Kind(store.Name(node)) == SyntaxKind.ComputedPropertyName && Traverse(store.Name(node));
                case SyntaxKind.PropertyAccessExpression:
                case SyntaxKind.ElementAccessExpression:
                    return Traverse(store.Expression(node));
                case SyntaxKind.PropertyAssignment:
                    return Traverse(store.Initializer(node));
                default:
                    return !NodeStartsNewLexicalEnvironment(store.Kind(node)) && !Utilities.IsPartOfTypeNode(store, node) &&
                        NodeVisitor.ForEachChild<object>(store, node, child => Traverse(child) ? true : null) is not null;
            }
        }
    }

    // utilities.ts nodeStartsNewLexicalEnvironment
    private static bool NodeStartsNewLexicalEnvironment(SyntaxKind kind) => kind is SyntaxKind.Constructor or SyntaxKind.FunctionExpression
        or SyntaxKind.FunctionDeclaration or SyntaxKind.ArrowFunction or SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor
        or SyntaxKind.SetAccessor or SyntaxKind.ModuleDeclaration or SyntaxKind.SourceFile;

    // checker.ts getReturnTypeOfTypeTag
    private Type GetReturnTypeOfTypeTag(NodeRef node)
    {
        var signature = GetSignatureOfTypeTag(node);
        return signature.IsNone ? Type.None : GetReturnTypeOfSignature(signature);
    }
}
