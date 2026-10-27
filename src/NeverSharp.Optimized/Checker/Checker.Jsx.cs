// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (checkJsxElement, checkJsxSelfClosingElement, checkJsxFragment,
// checkJsxOpeningLikeElementOrOpeningFragment, checkJsxPreconditions, markJsxAliasReferenced, getJsxNamespace,
// getJsxNamespaceAt, getJsxType, getIntrinsicTagSymbol)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/jsx.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#. Brought forward from module C9 in module C1: the name resolution and the
// preconditions of JSX elements (the jsx flag, the JSX namespace, the factory reference); TODO(C9): attributes types,
// children, intrinsic element properties, signatures of components, the JSX element types.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private const string JsxNamespaceName = "JSX";
    private const string JsxIntrinsicElements = "IntrinsicElements";

    // checkJsxElement / checkJsxSelfClosingElement / checkJsxFragment in the walk (children are visited by the caller)
    private void CheckJsxNode(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.JsxElement:
            {
                var opening = At(node, store.OpeningElement(n));
                CheckJsxOpeningLikeElementOrOpeningFragment(opening);
                foreach (var child in store.Nodes(store.Children(n)))
                {
                    CheckJsxChild(At(node, child));
                }
                // checkJsxElementDeferred: the closing tag
                var closing = At(node, store.ClosingElement(n));
                var tagName = At(node, store.TagName(closing.Node));
                if (IsJsxIntrinsicTagName(store, tagName.Node))
                {
                    GetIntrinsicTagSymbol(closing);
                }
                else
                {
                    CheckJsxTagNameExpression(tagName);
                }
                break;
            }
            case SyntaxKind.JsxSelfClosingElement:
                CheckJsxOpeningLikeElementOrOpeningFragment(node);
                break;
            case SyntaxKind.JsxFragment:
                CheckJsxOpeningLikeElementOrOpeningFragment(At(node, store.OpeningFragment(n)));
                foreach (var child in store.Nodes(store.Children(n)))
                {
                    CheckJsxChild(At(node, child));
                }
                break;
        }
    }

    // a JSX child or attribute initializer: expressions are checked, nested elements are checked as JSX nodes
    private void CheckJsxChild(NodeRef node)
    {
        var store = StoreOf(node);
        switch (store.Kind(node.Node))
        {
            case SyntaxKind.JsxExpression:
                if (!store.Expression(node.Node).IsNone)
                {
                    CheckExpression(At(node, store.Expression(node.Node)));
                }
                break;
            case SyntaxKind.JsxElement:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxFragment:
                CheckJsxNode(node);
                break;
        }
    }

    // checker.ts checkJsxOpeningLikeElementOrOpeningFragment (the C1 part)
    private void CheckJsxOpeningLikeElementOrOpeningFragment(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var isOpeningLikeElement = store.Kind(n) is SyntaxKind.JsxOpeningElement or SyntaxKind.JsxSelfClosingElement;
        // checkJsxPreconditions
        if ((compilerOptions.Jsx ?? JsxEmit.None) == JsxEmit.None)
        {
            Error(node, Diagnostics.Cannot_use_JSX_unless_the_jsx_flag_is_provided);
        }
        // (getJsxElementTypeAt is errorType, never undefined, when the JSX namespace is missing: no error)
        MarkJsxAliasReferenced(node);
        if (!isOpeningLikeElement)
        {
            return;
        }
        var tagName = At(node, store.TagName(n));
        if (IsJsxIntrinsicTagName(store, tagName.Node))
        {
            GetIntrinsicTagSymbol(node);
        }
        else
        {
            CheckJsxTagNameExpression(tagName);
        }
        var typeArguments = store.TypeArguments(n);
        if (!typeArguments.IsNone)
        {
            CheckSourceElements(node, typeArguments);
        }
        // checkJsxAttributes: the attribute initializers and spread expressions
        var attributes = store.Attributes(n);
        foreach (var attribute in store.Nodes(store.Properties(attributes)))
        {
            if (store.Kind(attribute) == SyntaxKind.JsxSpreadAttribute)
            {
                CheckExpression(At(node, store.Expression(attribute)));
            }
            else
            {
                var initializer = store.Initializer(attribute);
                if (!initializer.IsNone)
                {
                    CheckJsxChild(At(node, initializer));
                }
            }
        }
    }

    // the tag name of a value-based element: an identifier, `this`, a property access or a namespaced name
    private void CheckJsxTagNameExpression(NodeRef tagName)
    {
        var store = StoreOf(tagName);
        switch (store.Kind(tagName.Node))
        {
            case SyntaxKind.Identifier:
                CheckIdentifier(tagName);
                break;
            case SyntaxKind.ThisKeyword:
                CheckThisExpression(tagName);
                break;
            case SyntaxKind.PropertyAccessExpression:
                CheckExpressionCached(tagName);
                break;
        }
    }

    // checker.ts markJsxAliasReferenced (implicit jsx imports: the react-jsx modes, not set in Gate C)
    private void MarkJsxAliasReferenced(NodeRef node)
    {
        var store = StoreOf(node);
        var isFragment = store.Kind(node.Node) == SyntaxKind.JsxOpeningFragment;
        var jsxFactoryRefErr = compilerOptions.Jsx == JsxEmit.React ? Diagnostics.This_JSX_tag_requires_0_to_be_in_scope_but_it_could_not_be_found : null;
        var jsxFactoryNamespace = GetJsxNamespace(node);
        var jsxFactoryLocation = isFragment ? node : At(node, store.TagName(node.Node));
        var jsxFactorySym = SymbolRef.None;
        if (!(isFragment && jsxFactoryNamespace == "null"))
        {
            jsxFactorySym = ResolveName(jsxFactoryLocation, jsxFactoryNamespace, NodeRef.None,
                compilerOptions.Jsx == JsxEmit.Preserve ? SymbolFlags.Value & ~SymbolFlags.Enum : SymbolFlags.Value, jsxFactoryRefErr, isUse: true);
        }
        if (!jsxFactorySym.IsNone)
        {
            // Mark local symbol as referenced here because it might not have been marked
            // if jsx emit was not jsxFactory as there wont be error being emitted
            MarkReferenced(jsxFactorySym, SymbolFlags.All);
        }
    }

    // checker.ts getJsxNamespace (jsxFactory / reactNamespace options: not set in Gate C)
    private string GetJsxNamespace(NodeRef location)
    {
        var file = FileAt(location.File);
        var store = file.Store;
        var pragmas = store.Pragmas(file.Root);
        if (store.Kind(location.Node) == SyntaxKind.JsxOpeningFragment)
        {
            var fragment = PragmaFactoryNamespace(pragmas, "jsxfrag");
            if (fragment is not null)
            {
                return fragment;
            }
        }
        else
        {
            var local = PragmaFactoryNamespace(pragmas, "jsx");
            if (local is not null)
            {
                return local;
            }
        }
        return "React";
    }

    // the first identifier of a pragma's factory entity name (parseIsolatedEntityName)
    private string? PragmaFactoryNamespace(PragmaMap? pragmas, string name)
    {
        if (pragmas is null || !pragmas.TryGetValue(name, out var value))
        {
            return null;
        }
        var entry = value is List<PragmaEntry> list ? list[0] : (PragmaEntry)value;
        var factory = entry.Arguments.Get("factory") switch
        {
            string s => s,
            PragmaArgumentSpan span => span.Value,
            _ => null,
        };
        if (string.IsNullOrEmpty(factory))
        {
            return null;
        }
        var first = factory.Split('.')[0];
        return Scanner.IsIdentifierText(first, languageVersion) ? Utilities.EscapeLeadingUnderscores(first) : null;
    }

    // checker.ts getJsxNamespaceAt
    private SymbolRef GetJsxNamespaceAt(NodeRef location)
    {
        var namespaceName = GetJsxNamespace(location);
        var resolvedNamespace = ResolveName(location, namespaceName, NodeRef.None, SymbolFlags.Namespace, null, isUse: false);
        if (!resolvedNamespace.IsNone)
        {
            var candidate = ResolveSymbol(GetSymbol(GetExportsOfSymbol(ResolveSymbol(resolvedNamespace)), JsxNamespaceName, SymbolFlags.Namespace));
            if (!candidate.IsNone && candidate != unknownSymbol)
            {
                return candidate;
            }
        }
        // JSX global fallback
        var s = ResolveSymbol(GetGlobalSymbol(JsxNamespaceName, SymbolFlags.Namespace, null));
        return s == unknownSymbol ? SymbolRef.None : s;
    }

    // checker.ts getJsxType: the declared type of a type of the JSX namespace (None for errorType)
    private Type GetJsxType(string name, NodeRef location)
    {
        var ns = GetJsxNamespaceAt(location);
        var typeSymbol = ns.IsNone ? SymbolRef.None : GetSymbol(GetExportsOfSymbol(ns), name, SymbolFlags.Type);
        return typeSymbol.IsNone ? errorType : GetDeclaredTypeOfSymbol(typeSymbol);
    }

    // checker.ts getIntrinsicTagSymbol (the properties of JSX.IntrinsicElements: TODO(C9))
    private SymbolRef GetIntrinsicTagSymbol(NodeRef node)
    {
        var intrinsicElementsType = GetJsxType(JsxIntrinsicElements, node);
        if (!IsErrorType(intrinsicElementsType))
        {
            var store = StoreOf(node);
            var tagName = store.TagName(node.Node);
            if (store.Kind(tagName) == SyntaxKind.Identifier)
            {
                var property = GetPropertyOfType(intrinsicElementsType, store.Texts.GetString(store.EscapedText(tagName))!);
                if (!property.IsNone)
                {
                    return property;
                }
            }
            return unknownSymbol;
        }
        if (noImplicitAny)
        {
            Error(node, Diagnostics.JSX_element_implicitly_has_type_any_because_no_interface_JSX_0_exists, JsxIntrinsicElements);
        }
        return unknownSymbol;
    }

    // Whether JSX expressions have contextual types (the JSX namespace exists; C1 heuristic for getContextualType)
    private bool HasJsxContext(NodeRef node) => !GetJsxNamespaceAt(node).IsNone;
}
