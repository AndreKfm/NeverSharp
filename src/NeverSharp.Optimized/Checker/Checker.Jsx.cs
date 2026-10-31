// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (JSX checking; the functions are listed below)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/jsx.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; modules C1 (preconditions) and C9b (the rest, details below).
//
// Functions: checkJsxElement, checkJsxElementDeferred, checkJsxSelfClosingElement,
// checkJsxSelfClosingElementDeferred, checkJsxFragment, checkJsxExpression, checkJsxAttributes, checkJsxAttribute,
// checkJsxChildren, checkJsxOpeningLikeElementOrOpeningFragment, checkJsxPreconditions,
// checkJsxReturnAssignableToAppropriateBound, createJsxAttributesTypeFromAttributesProperty, createJsxAttributesType,
// inferJsxTypeArguments, getContextualTypeForJsxExpression, getContextualTypeForJsxAttribute,
// getContextualJsxElementAttributesType, getContextualTypeForChildJsxExpression,
// discriminateContextualTypeByJSXAttributes, elaborateJsxComponents, generateJsxAttributes, generateJsxChildren,
// getElaborationElementForJsxChild, elaborateIterableOrArrayLikeTargetElementwise,
// getSuggestedSymbolForNonexistentJSXAttribute, getJSXFragmentType, resolveJsxOpeningLikeElement,
// checkApplicableSignatureForJsxCallLikeElement, getUninstantiatedJsxSignaturesOfType,
// getEffectiveFirstArgumentForJsxSignature, getJsxPropsTypeFromCallSignature, getJsxPropsTypeFromClassType,
// getJsxPropsTypeForSignatureFromMember, getJsxManagedAttributesFromLocatedAttributes,
// instantiateAliasOrInterfaceWithDefaults, getJsxLibraryManagedAttributes, getJsxElementTypeSymbol,
// getJsxElementPropertiesName, getJsxElementChildrenPropertyName, getNameFromJsxElementAttributesContainer,
// getStaticTypeOfReferencedJsxConstructor, getIntrinsicAttributesTypeFromStringLiteralType, getJsxReferenceKind,
// createSignatureForJSXIntrinsic, getIntrinsicAttributesTypeFromJsxOpeningLikeElement, getIntrinsicTagSymbol,
// getApplicableIndexSymbol, getJsxStatelessElementTypeAt, getJsxElementClassTypeAt, getJsxElementTypeAt,
// getJsxElementTypeTypeAt, getJsxType, getJsxNamespaceAt, getJsxNamespace, getLocalJsxNamespace,
// getJsxFactoryEntity, getJsxNamespaceContainerForImplicitImport, markJsxAliasReferenced, checkGrammarJsxElement,
// checkGrammarJsxName, checkGrammarJsxExpression, getJSXImplicitImportBase, getJSXRuntimeImport.
// Details: factory entities (the jsx/jsxFrag pragmas, React.createElement) are kept as their dotted names and resolved
// part by part instead of as synthetic entity name nodes. Gate C's programs never set the jsx option, jsxFactory,
// jsxFragmentFactory, reactNamespace or jsxImportSource (the C# CompilerOptions have only jsx), so the implicit runtime
// import comes only from the jsxImportSource/jsxRuntime pragmas and its error goes to the JSX node (no synthetic import
// specifier); the fake `children` property of the attributes type has no synthetic declaration node;
// markAliasSymbolAsReferenced (emit only) is not ported.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>types.ts JsxFlags.</summary>
[Flags]
internal enum JsxFlags
{
    None = 0,
    IntrinsicNamedElement = 1 << 0, // An element from a named property of the JSX.IntrinsicElements interface
    IntrinsicIndexedElement = 1 << 1, // An element inferred from the string index signature of the JSX.IntrinsicElements interface
    IntrinsicElement = IntrinsicNamedElement | IntrinsicIndexedElement,
}

/// <summary>checker.ts JsxReferenceKind.</summary>
internal enum JsxReferenceKind
{
    Component,
    Function,
    Mixed,
}

/// <summary>jsx.go JsxElementLinks.</summary>
internal struct JsxElementLinks
{
    public JsxFlags JsxFlags;
    public Type ResolvedJsxElementAttributesType;
    public SymbolRef JsxNamespace;
    public byte JsxNamespaceState; // links.jsxNamespace: 0 undefined, 1 false, 2 a symbol
}

internal sealed partial class Checker
{
    private const string JsxNamespaceName = "JSX";
    private const string JsxIntrinsicElements = "IntrinsicElements";
    private const string JsxElementClass = "ElementClass";
    private const string JsxElementAttributesPropertyNameContainer = "ElementAttributesProperty";
    private const string JsxElementChildrenAttributeNameContainer = "ElementChildrenAttribute";
    private const string JsxElement = "Element";
    private const string JsxElementType = "ElementType";
    private const string JsxIntrinsicAttributes = "IntrinsicAttributes";
    private const string JsxIntrinsicClassAttributes = "IntrinsicClassAttributes";
    private const string JsxLibraryManagedAttributes = "LibraryManagedAttributes";
    private const string ReactFragment = "Fragment";

    private NodeLinkStore<JsxElementLinks>? jsxElementLinks;

    private NodeLinkStore<JsxElementLinks> JsxElementLinks => jsxElementLinks ??= new NodeLinkStore<JsxElementLinks>(program.SourceFiles.Count);

    // sourceFileLinks.jsxFragmentType per file
    private readonly Dictionary<int, Type> jsxFragmentTypes = [];

    // ---- syntax helpers ----

    // utilities.ts isJsxOpeningLikeElement
    private static bool IsJsxOpeningLikeElement(NodeStore store, Node node) =>
        store.Kind(node) is SyntaxKind.JsxOpeningElement or SyntaxKind.JsxSelfClosingElement;

    // utilities.ts isJsxCallLike
    private static bool IsJsxCallLike(NodeStore store, Node node) =>
        store.Kind(node) is SyntaxKind.JsxOpeningElement or SyntaxKind.JsxSelfClosingElement or SyntaxKind.JsxOpeningFragment;

    // utilities.ts getTextOfJsxAttributeName / getEscapedTextOfJsxAttributeName / intrinsicTagNameToString
    private static string GetTextOfJsxAttributeName(NodeStore store, Node name) =>
        store.Kind(name) == SyntaxKind.JsxNamespacedName
            ? store.Texts.GetString(store.EscapedText(store.Namespace(name)))! + ":" + store.Texts.GetString(store.EscapedText(store.Name(name)))!
            : store.Texts.GetString(store.EscapedText(name))!;

    // utilities.ts getSemanticJsxChildren
    private static List<Node> GetSemanticJsxChildren(NodeStore store, NodeList children)
    {
        var result = new List<Node>();
        if (children.IsNone)
        {
            return result;
        }
        foreach (var child in store.Nodes(children))
        {
            switch (store.Kind(child))
            {
                case SyntaxKind.JsxExpression:
                    if (!store.Expression(child).IsNone)
                    {
                        result.Add(child);
                    }
                    break;
                case SyntaxKind.JsxText:
                    if (!store.ContainsOnlyTriviaWhiteSpaces(child))
                    {
                        result.Add(child);
                    }
                    break;
                default:
                    result.Add(child);
                    break;
            }
        }
        return result;
    }

    // ---- elements ----

    // checker.ts checkJsxElement / checkJsxSelfClosingElement
    private Type CheckJsxElement(NodeRef node)
    {
        CheckNodeDeferred(node);
        return GetJsxElementTypeAt(node);
    }

    // checker.ts checkJsxSelfClosingElementDeferred
    private void CheckJsxSelfClosingElementDeferred(NodeRef node) => CheckJsxOpeningLikeElementOrOpeningFragment(node);

    // checker.ts checkJsxElementDeferred
    private void CheckJsxElementDeferred(NodeRef node)
    {
        var store = StoreOf(node);
        // Check attributes
        CheckJsxOpeningLikeElementOrOpeningFragment(At(node, store.OpeningElement(node.Node)));
        // Perform resolution on the closing tag so that rename/go to definition/etc work
        var closing = At(node, store.ClosingElement(node.Node));
        var tagName = store.TagName(closing.Node);
        if (IsJsxIntrinsicTagName(store, tagName))
        {
            GetIntrinsicTagSymbol(closing);
        }
        else
        {
            CheckExpression(At(node, tagName));
        }
        CheckJsxChildren(node);
    }

    // checker.ts checkJsxFragment
    private Type CheckJsxFragment(NodeRef node)
    {
        var store = StoreOf(node);
        CheckJsxOpeningLikeElementOrOpeningFragment(At(node, store.OpeningFragment(node.Node)));
        // by default, jsx:'react' will use jsxFactory = React.createElement and jsxFragmentFactory = React.Fragment
        // if jsxFactory compiler option is provided, ensure jsxFragmentFactory compiler option or @jsxFrag pragma is provided too
        // (the jsxFactory and jsxFragmentFactory options are not part of the C# CompilerOptions: never set in Gate C)
        var pragmas = store.Pragmas(FileAt(node.File).Root);
        if (GetJSXTransformEnabled() && pragmas is not null && pragmas.ContainsKey("jsx") && !pragmas.ContainsKey("jsxfrag"))
        {
            Error(node, Diagnostics.An_jsxFrag_pragma_is_required_when_using_an_jsx_pragma_with_JSX_fragments);
        }
        CheckJsxChildren(node);
        var jsxElementType = GetJsxElementTypeAt(node);
        return IsErrorType(jsxElementType) ? anyType : jsxElementType;
    }

    // compilerOptions.ts getJSXTransformEnabled
    private bool GetJSXTransformEnabled() => compilerOptions.Jsx is JsxEmit.React or JsxEmit.ReactJSX or JsxEmit.ReactJSXDev;

    // checker.ts checkJsxExpression
    private Type CheckJsxExpression(NodeRef node, CheckMode checkMode)
    {
        CheckGrammarJsxExpression(node);
        var store = StoreOf(node);
        var expression = store.Expression(node.Node);
        if (expression.IsNone)
        {
            return errorType;
        }
        var type = CheckExpression(At(node, expression), checkMode);
        if (!store.DotDotDotToken(node.Node).IsNone && type != anyType && !IsArrayType(type))
        {
            Error(node, Diagnostics.JSX_spread_child_must_be_an_array_type);
        }
        return type;
    }

    // checker.ts checkJsxAttributes
    private Type CheckJsxAttributes(NodeRef node, CheckMode checkMode) => CreateJsxAttributesTypeFromAttributesProperty(Parent(node), checkMode);

    // checker.ts checkJsxAttribute
    private Type CheckJsxAttribute(NodeRef node, CheckMode checkMode = CheckMode.Normal)
    {
        var initializer = StoreOf(node).Initializer(node.Node);
        return !initializer.IsNone
            ? CheckExpressionForMutableLocation(At(node, initializer), checkMode)
            : trueType; // <Elem attr /> is sugar for <Elem attr={true} />
    }

    // checker.ts checkJsxChildren
    private List<Type> CheckJsxChildren(NodeRef node, CheckMode checkMode = CheckMode.Normal)
    {
        var store = StoreOf(node);
        var childrenTypes = new List<Type>();
        foreach (var child in store.Nodes(store.Children(node.Node)).ToArray())
        {
            // In React, JSX text that contains only whitespaces will be ignored so we don't want to type-check that
            // because then type of children property will have constituent of string type.
            if (store.Kind(child) == SyntaxKind.JsxText)
            {
                if (!store.ContainsOnlyTriviaWhiteSpaces(child))
                {
                    childrenTypes.Add(stringType);
                }
            }
            else if (store.Kind(child) == SyntaxKind.JsxExpression && store.Expression(child).IsNone)
            {
                continue; // empty jsx expressions don't *really* count as present children
            }
            else
            {
                childrenTypes.Add(CheckExpressionForMutableLocation(At(node, child), checkMode));
            }
        }
        return childrenTypes;
    }

    // checker.ts checkJsxOpeningLikeElementOrOpeningFragment
    private void CheckJsxOpeningLikeElementOrOpeningFragment(NodeRef node)
    {
        var store = StoreOf(node);
        var isNodeOpeningLikeElement = IsJsxOpeningLikeElement(store, node.Node);
        if (isNodeOpeningLikeElement)
        {
            CheckGrammarJsxElement(node);
        }
        CheckJsxPreconditions(node);
        MarkJsxAliasReferenced(node);
        var sig = GetResolvedSignature(node);
        CheckDeprecatedSignature(sig, node);
        if (isNodeOpeningLikeElement)
        {
            var elementTypeConstraint = GetJsxElementTypeTypeAt(node);
            if (!elementTypeConstraint.IsNone)
            {
                var tagName = At(node, store.TagName(node.Node));
                var tagType = IsJsxIntrinsicTagName(store, tagName.Node)
                    ? GetStringLiteralType(GetTextOfJsxAttributeName(store, tagName.Node))
                    : CheckExpression(tagName);
                CheckTypeRelatedTo(tagType, elementTypeConstraint, assignableRelation, tagName, Diagnostics.Its_type_0_is_not_a_valid_JSX_element_type,
                    () => ChainDiagnosticMessages(null, Diagnostics._0_cannot_be_used_as_a_JSX_component, GetTextOfNode(tagName)));
            }
            else
            {
                CheckJsxReturnAssignableToAppropriateBound(GetJsxReferenceKind(node), GetReturnTypeOfSignature(sig), node);
            }
        }
    }

    // checker.ts checkJsxPreconditions
    private void CheckJsxPreconditions(NodeRef errorNode)
    {
        // Preconditions for using JSX
        if ((compilerOptions.Jsx ?? JsxEmit.None) == JsxEmit.None)
        {
            Error(errorNode, Diagnostics.Cannot_use_JSX_unless_the_jsx_flag_is_provided);
        }
        // (getJsxElementTypeAt is errorType, never undefined: the noImplicitAny error of a missing JSX.Element is never
        // reported)
    }

    // checker.ts checkJsxReturnAssignableToAppropriateBound
    private void CheckJsxReturnAssignableToAppropriateBound(JsxReferenceKind refKind, Type elemInstanceType, NodeRef openingLikeElement)
    {
        var tagName = At(openingLikeElement, StoreOf(openingLikeElement).TagName(openingLikeElement.Node));
        Func<DiagnosticMessageChain?> generateInitialErrorChain = () => ChainDiagnosticMessages(null, Diagnostics._0_cannot_be_used_as_a_JSX_component, GetTextOfNode(tagName));
        if (refKind == JsxReferenceKind.Function)
        {
            var sfcReturnConstraint = GetJsxStatelessElementTypeAt(openingLikeElement);
            if (!sfcReturnConstraint.IsNone)
            {
                CheckTypeRelatedTo(elemInstanceType, sfcReturnConstraint, assignableRelation, tagName, Diagnostics.Its_return_type_0_is_not_a_valid_JSX_element, generateInitialErrorChain);
            }
        }
        else if (refKind == JsxReferenceKind.Component)
        {
            var classConstraint = GetJsxElementClassTypeAt(openingLikeElement);
            if (!classConstraint.IsNone)
            {
                // Issue an error if this return type isn't assignable to JSX.ElementClass, failing that
                CheckTypeRelatedTo(elemInstanceType, classConstraint, assignableRelation, tagName, Diagnostics.Its_instance_type_0_is_not_a_valid_JSX_element, generateInitialErrorChain);
            }
        }
        else
        {
            // Mixed
            var sfcReturnConstraint = GetJsxStatelessElementTypeAt(openingLikeElement);
            var classConstraint = GetJsxElementClassTypeAt(openingLikeElement);
            if (sfcReturnConstraint.IsNone || classConstraint.IsNone)
            {
                return;
            }
            var combined = GetUnionType([sfcReturnConstraint, classConstraint]);
            CheckTypeRelatedTo(elemInstanceType, combined, assignableRelation, tagName, Diagnostics.Its_element_type_0_is_not_a_valid_JSX_element, generateInitialErrorChain);
        }
    }

    // ---- attributes ----

    // checker.ts createJsxAttributesTypeFromAttributesProperty
    private Type CreateJsxAttributesTypeFromAttributesProperty(NodeRef openingLikeElement, CheckMode checkMode = CheckMode.Normal)
    {
        var store = StoreOf(openingLikeElement);
        SymTable? allAttributesTable = strictNullChecks ? new SymTable(Names) : null;
        var attributesTable = new SymTable(Names);
        var spread = emptyJsxObjectType;
        var hasSpreadAnyType = false;
        var typeToIntersect = Type.None;
        var explicitlySpecifyChildrenAttribute = false;
        var objectFlags = ObjectFlags.JsxAttributes;
        var jsxChildrenPropertyName = GetJsxElementChildrenPropertyName(GetJsxNamespaceAt(openingLikeElement));
        var isJsxOpenFragment = store.Kind(openingLikeElement.Node) == SyntaxKind.JsxOpeningFragment;
        var attributesSymbol = SymbolRef.None;
        var attributeParent = openingLikeElement;
        if (!isJsxOpenFragment)
        {
            var attributes = At(openingLikeElement, store.Attributes(openingLikeElement.Node));
            attributesSymbol = NodeSymbol(attributes);
            attributeParent = attributes;
            var contextualType = GetContextualType(attributes, ContextFlags.None);
            foreach (var attributeNode in store.Nodes(store.Properties(attributes.Node)).ToArray())
            {
                var attributeDecl = At(attributes, attributeNode);
                var member = NodeSymbol(attributeDecl);
                if (store.Kind(attributeNode) == SyntaxKind.JsxAttribute)
                {
                    var exprType = CheckJsxAttribute(attributeDecl, checkMode);
                    objectFlags |= Types.ObjectFlagsOf(exprType) & ObjectFlags.PropagatingFlags;
                    var attributeSymbol = CreateSymbol(SymbolFlags.Property | SymbolFlagsOf(member), NameIdOf(member));
                    ref var attributeRecord = ref TransientSymbols.Record(attributeSymbol);
                    attributeRecord.Declarations = DeclRefOf(member);
                    attributeRecord.Parent = ParentOf(member);
                    var valueDeclaration = ValueDeclarationOf(member);
                    if (!valueDeclaration.IsNone)
                    {
                        attributeRecord.ValueDeclaration = valueDeclaration;
                    }
                    ref var attributeLinks = ref ValueSymbolLinks.Get(attributeSymbol);
                    attributeLinks.ResolvedType = exprType;
                    attributeLinks.Target = member;
                    attributesTable.Set(NameOf(attributeSymbol), attributeSymbol);
                    allAttributesTable?.Set(NameOf(attributeSymbol), attributeSymbol);
                    var attributeName = store.Name(attributeNode);
                    if (GetTextOfJsxAttributeName(store, attributeName) == jsxChildrenPropertyName)
                    {
                        explicitlySpecifyChildrenAttribute = true;
                    }
                    if (!contextualType.IsNone)
                    {
                        var prop = GetPropertyOfType(contextualType, NameOf(member));
                        if (!prop.IsNone && DeclarationsOf(prop) is { } propDeclarations && IsDeprecatedSymbol(prop) && store.Kind(attributeName) == SyntaxKind.Identifier)
                        {
                            AddDeprecatedSuggestion(At(attributes, attributeName), propDeclarations, store.Texts.GetString(store.EscapedText(attributeName))!);
                        }
                    }
                    if (!contextualType.IsNone && (checkMode & CheckMode.Inferential) != 0 && (checkMode & CheckMode.SkipContextSensitive) == 0 && IsContextSensitive(attributeDecl))
                    {
                        var inferenceContext = GetInferenceContext(attributes);
                        // In CheckMode.Inferential we should always have an inference context
                        if (inferenceContext is not null)
                        {
                            var inferenceNode = At(attributes, store.Expression(store.Initializer(attributeNode)));
                            AddIntraExpressionInferenceSite(inferenceContext, inferenceNode, exprType);
                        }
                    }
                }
                else
                {
                    if (attributesTable.Count > 0)
                    {
                        spread = GetSpreadType(spread, CreateJsxAttributesTypeHelper(), attributesSymbol, objectFlags, isReadonly: false);
                        attributesTable = new SymTable(Names);
                    }
                    var exprType = GetReducedType(CheckExpression(At(attributes, store.Expression(attributeNode)), checkMode & CheckMode.Inferential));
                    if (IsTypeAny(exprType))
                    {
                        hasSpreadAnyType = true;
                    }
                    if (IsValidSpreadType(exprType))
                    {
                        spread = GetSpreadType(spread, exprType, attributesSymbol, objectFlags, isReadonly: false);
                        if (allAttributesTable is { } allTable)
                        {
                            CheckSpreadPropOverrides(exprType, allTable, attributeDecl);
                        }
                    }
                    else
                    {
                        Error(At(attributes, store.Expression(attributeNode)), Diagnostics.Spread_types_may_only_be_created_from_object_types);
                        typeToIntersect = !typeToIntersect.IsNone ? GetIntersectionType([typeToIntersect, exprType]) : exprType;
                    }
                }
            }
            if (!hasSpreadAnyType)
            {
                if (attributesTable.Count > 0)
                {
                    spread = GetSpreadType(spread, CreateJsxAttributesTypeHelper(), attributesSymbol, objectFlags, isReadonly: false);
                }
            }
        }
        // Handle children attribute
        var parent = store.Parent(openingLikeElement.Node);
        // We have to check that openingElement of the parent is the one we are visiting as this may not be true for selfClosingElement
        if (((store.Kind(parent) == SyntaxKind.JsxElement && store.OpeningElement(parent) == openingLikeElement.Node) ||
             (store.Kind(parent) == SyntaxKind.JsxFragment && store.OpeningFragment(parent) == openingLikeElement.Node)) &&
            GetSemanticJsxChildren(store, store.Children(parent)).Count > 0)
        {
            var childrenTypes = CheckJsxChildren(At(openingLikeElement, parent), checkMode);
            if (!hasSpreadAnyType && !string.IsNullOrEmpty(jsxChildrenPropertyName))
            {
                // Error if there is a attribute named "children" explicitly specified and children element.
                // This is because children element will overwrite the value from attributes.
                // Note: we will not warn "children" attribute overwritten if "children" attribute is specified in object spread.
                if (explicitlySpecifyChildrenAttribute)
                {
                    Error(attributeParent, Diagnostics._0_are_specified_twice_The_attribute_named_0_will_be_overwritten, Utilities.UnescapeLeadingUnderscores(jsxChildrenPropertyName));
                }
                var contextualType = store.Kind(openingLikeElement.Node) == SyntaxKind.JsxOpeningElement
                    ? GetApparentTypeOfContextualType(At(openingLikeElement, store.Attributes(openingLikeElement.Node)), ContextFlags.None)
                    : Type.None;
                var childrenContextualType = contextualType.IsNone ? Type.None : GetTypeOfPropertyOfContextualType(contextualType, jsxChildrenPropertyName);
                // If there are children in the body of JSX element, create dummy attribute "children" with the union of children types so that it will pass the attribute checking process
                var childrenPropSymbol = CreateSymbol(SymbolFlags.Property, jsxChildrenPropertyName);
                var childrenType = childrenTypes.Count == 1 ? childrenTypes[0]
                    : !childrenContextualType.IsNone && SomeType(childrenContextualType, IsTupleLikeType) ? CreateTupleType([.. childrenTypes])
                    : CreateArrayType(GetUnionType([.. childrenTypes]));
                ValueSymbolLinks.Get(childrenPropSymbol).ResolvedType = childrenType;
                // (the fake property declaration of the children: not created, see the file header)
                var childPropMap = new SymTable(Names);
                childPropMap.Set(jsxChildrenPropertyName, childrenPropSymbol);
                spread = GetSpreadType(spread, CreateAnonymousType(attributesSymbol, childPropMap), attributesSymbol,
                    objectFlags | GetPropagatingFlagsOfTypes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(childrenTypes)), isReadonly: false);
            }
        }
        if (hasSpreadAnyType)
        {
            return anyType;
        }
        if (!typeToIntersect.IsNone && spread != emptyJsxObjectType)
        {
            return GetIntersectionType([typeToIntersect, spread]);
        }
        return !typeToIntersect.IsNone ? typeToIntersect : spread == emptyJsxObjectType ? CreateJsxAttributesTypeHelper() : spread;

        Type CreateJsxAttributesTypeHelper()
        {
            objectFlags |= ObjectFlags.FreshLiteral;
            return CreateJsxAttributesType(objectFlags, attributesSymbol, attributesTable);
        }
    }

    // checker.ts createJsxAttributesType
    private Type CreateJsxAttributesType(ObjectFlags objectFlags, SymbolRef attributesSymbol, SymTable attributesTable)
    {
        var result = CreateAnonymousType(attributesSymbol, attributesTable);
        Types.Record(result).ObjectFlags |= objectFlags | ObjectFlags.FreshLiteral | ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
        return result;
    }

    // ---- contextual types ----

    // checker.ts inferJsxTypeArguments
    private Type[] InferJsxTypeArguments(NodeRef node, Signature signature, CheckMode checkMode, InferenceContext context)
    {
        var paramType = GetEffectiveFirstArgumentForJsxSignature(signature, node);
        var checkAttrType = CheckExpressionWithContextualType(At(node, StoreOf(node).Attributes(node.Node)), paramType, context, checkMode);
        InferTypes(context.Inferences, checkAttrType, paramType);
        return GetInferredTypes(context);
    }

    // checker.ts getContextualTypeForJsxExpression
    private Type GetContextualTypeForJsxExpression(NodeRef node, ContextFlags contextFlags)
    {
        var exprParent = Parent(node);
        return Kind(exprParent) switch
        {
            SyntaxKind.JsxAttribute or SyntaxKind.JsxSpreadAttribute => GetContextualType(node, contextFlags),
            SyntaxKind.JsxElement => GetContextualTypeForChildJsxExpression(exprParent, node, contextFlags),
            _ => Type.None,
        };
    }

    // checker.ts getContextualTypeForJsxAttribute
    private Type GetContextualTypeForJsxAttribute(NodeRef attribute, ContextFlags contextFlags)
    {
        // When we trying to resolve JsxOpeningLikeElement as a stateless function element, we will already give its attributes a contextual type
        // which is a type of the parameter of the signature we are trying out.
        // If there is no contextual type (e.g. we are trying to resolve stateful component), get attributes type from resolving element's tagName
        if (Kind(attribute) == SyntaxKind.JsxAttribute)
        {
            var attributesType = GetApparentTypeOfContextualType(Parent(attribute), contextFlags);
            if (attributesType.IsNone || IsTypeAny(attributesType))
            {
                return Type.None;
            }
            var store = StoreOf(attribute);
            return GetTypeOfPropertyOfContextualType(attributesType, GetTextOfJsxAttributeName(store, store.Name(attribute.Node)));
        }
        return GetContextualType(Parent(attribute), contextFlags);
    }

    // checker.ts getContextualJsxElementAttributesType
    private Type GetContextualJsxElementAttributesType(NodeRef node, ContextFlags contextFlags)
    {
        if (Kind(node) == SyntaxKind.JsxOpeningElement && contextFlags != ContextFlags.IgnoreNodeInferences)
        {
            var index = FindContextualNode(Parent(node), includeCaches: contextFlags == ContextFlags.None);
            if (index >= 0)
            {
                // Contextually applied type is moved from attributes up to the outer jsx attributes so when walking up from the children they get hit
                // _However_ to hit them from the _attributes_ we must look for them here; otherwise we'll used the declared type
                // (as below) instead!
                return contextualTypes[index];
            }
        }
        return GetContextualTypeForArgumentAtIndex(node, 0);
    }

    // checker.ts getContextualTypeForChildJsxExpression
    private Type GetContextualTypeForChildJsxExpression(NodeRef node, NodeRef child, ContextFlags contextFlags)
    {
        var store = StoreOf(node);
        var attributesType = GetApparentTypeOfContextualType(At(node, store.Attributes(store.OpeningElement(node.Node))), contextFlags);
        // JSX expression is in children of JSX Element, we will look for an "children" attribute (we get the name from JSX.ElementAttributesProperty)
        var jsxChildrenPropertyName = GetJsxElementChildrenPropertyName(GetJsxNamespaceAt(node));
        if (!(!attributesType.IsNone && !IsTypeAny(attributesType) && !string.IsNullOrEmpty(jsxChildrenPropertyName)))
        {
            return Type.None;
        }
        var realChildren = GetSemanticJsxChildren(store, store.Children(node.Node));
        var childIndex = realChildren.IndexOf(child.Node);
        var childFieldType = GetTypeOfPropertyOfContextualType(attributesType, jsxChildrenPropertyName);
        if (childFieldType.IsNone)
        {
            return Type.None;
        }
        return realChildren.Count == 1 ? childFieldType : MapType(childFieldType, t => IsArrayLikeType(t) ? GetIndexedAccessType(t, GetNumberLiteralType(childIndex)) : t, noReductions: true);
    }

    // checker.ts discriminateContextualTypeByJSXAttributes
    private Type DiscriminateContextualTypeByJSXAttributes(NodeRef node, Type contextualType)
    {
        if (discriminatedContextualTypes.TryGetValue((node, contextualType), out var cached))
        {
            return cached;
        }
        var store = StoreOf(node);
        var jsxChildrenPropertyName = GetJsxElementChildrenPropertyName(GetJsxNamespaceAt(node));
        var discriminators = new List<(Func<Type> GetType, string Name)>();
        foreach (var p in store.Nodes(store.Properties(node.Node)))
        {
            var symbol = NodeSymbol(At(node, p));
            if (symbol.IsNone || store.Kind(p) != SyntaxKind.JsxAttribute)
            {
                continue;
            }
            var initializer = store.Initializer(p);
            var name = NameOf(symbol);
            if (IsDiscriminantProperty(contextualType, name) && (initializer.IsNone || IsPossiblyDiscriminantValue(store, initializer)))
            {
                discriminators.Add((initializer.IsNone ? () => trueType : () => GetContextFreeTypeOfExpression(At(node, initializer)), name));
            }
        }
        var attributesSymbol = NodeSymbol(node);
        var members = attributesSymbol.IsNone ? null : MembersOf(attributesSymbol);
        var element = store.Parent(store.Parent(node.Node));
        foreach (var s in GetPropertiesOfType(contextualType))
        {
            if ((SymbolFlagsOf(s) & SymbolFlags.Optional) == 0 || members is null)
            {
                continue;
            }
            var name = NameOf(s);
            if (name == jsxChildrenPropertyName && store.Kind(element) == SyntaxKind.JsxElement && GetSemanticJsxChildren(store, store.Children(element)).Count > 0)
            {
                continue;
            }
            if (!members.Has(name) && IsDiscriminantProperty(contextualType, name))
            {
                discriminators.Add((() => undefinedType, name));
            }
        }
        var result = DiscriminateTypeByDiscriminableItems(contextualType, discriminators, IsTypeAssignableTo);
        discriminatedContextualTypes[(node, contextualType)] = result;
        return result;
    }

    // ---- elaboration ----

    // checker.ts generateJsxAttributes
    private IEnumerable<ElaborationElement> GenerateJsxAttributes(NodeRef node)
    {
        var store = StoreOf(node);
        foreach (var prop in store.Nodes(store.Properties(node.Node)).ToArray())
        {
            if (store.Kind(prop) == SyntaxKind.JsxSpreadAttribute)
            {
                continue;
            }
            var name = GetTextOfJsxAttributeName(store, store.Name(prop));
            if (IsHyphenatedJsxName(name))
            {
                continue;
            }
            var initializer = store.Initializer(prop);
            yield return new ElaborationElement(At(node, store.Name(prop)), initializer.IsNone ? NodeRef.None : At(node, initializer), GetStringLiteralType(name));
        }
    }

    // checker.ts generateJsxChildren
    private IEnumerable<ElaborationElement> GenerateJsxChildren(NodeRef node, Func<DiagnosticMessage> getInvalidTextDiagnostic)
    {
        var store = StoreOf(node);
        var children = store.Nodes(store.Children(node.Node)).ToArray();
        var memberOffset = 0;
        for (var i = 0; i < children.Length; i++)
        {
            var nameType = GetNumberLiteralType(i - memberOffset);
            var elem = GetElaborationElementForJsxChild(At(node, children[i]), nameType, getInvalidTextDiagnostic);
            if (elem is { } e)
            {
                yield return e;
            }
            else
            {
                memberOffset++;
            }
        }
    }

    // checker.ts getElaborationElementForJsxChild
    private ElaborationElement? GetElaborationElementForJsxChild(NodeRef child, Type nameType, Func<DiagnosticMessage> getInvalidTextDiagnostic)
    {
        var store = StoreOf(child);
        switch (store.Kind(child.Node))
        {
            case SyntaxKind.JsxExpression:
            {
                // child is of the type of the expression
                var expression = store.Expression(child.Node);
                return new ElaborationElement(child, expression.IsNone ? NodeRef.None : At(child, expression), nameType);
            }
            case SyntaxKind.JsxText:
                if (store.ContainsOnlyTriviaWhiteSpaces(child.Node))
                {
                    return null; // Whitespace only jsx text isn't real jsx text
                }
                // child is a string
                return new ElaborationElement(child, NodeRef.None, nameType, getInvalidTextDiagnostic());
            default:
                // JsxElement, JsxSelfClosingElement, JsxFragment: child is of type JSX.Element
                return new ElaborationElement(child, child, nameType);
        }
    }

    // checker.ts elaborateJsxComponents
    private bool ElaborateJsxComponents(NodeRef node, Type source, Type target, Relation relation, Func<DiagnosticMessageChain?>? containingMessageChain, ErrorOutputContainer? errorOutputContainer)
    {
        var store = StoreOf(node);
        var result = ElaborateElementwise(GenerateJsxAttributes(node), source, target, relation, containingMessageChain, errorOutputContainer);
        DiagnosticMessage? invalidTextDiagnostic = null;
        var parent = store.Parent(node.Node);
        if (store.Kind(parent) == SyntaxKind.JsxOpeningElement && store.Kind(store.Parent(parent)) == SyntaxKind.JsxElement)
        {
            var containingElement = At(node, store.Parent(parent));
            var childPropName = GetJsxElementChildrenPropertyName(GetJsxNamespaceAt(node));
            var childrenPropName = childPropName is null ? "children" : Utilities.UnescapeLeadingUnderscores(childPropName);
            var childrenNameType = GetStringLiteralType(childrenPropName);
            var childrenTargetType = GetIndexedAccessType(target, childrenNameType);
            var validChildren = GetSemanticJsxChildren(store, store.Children(containingElement.Node));
            if (validChildren.Count == 0)
            {
                return result;
            }
            var moreThanOneRealChildren = validChildren.Count > 1;
            Type arrayLikeTargetParts;
            Type nonArrayLikeTargetParts;
            var iterableType = GetGlobalIterableType(reportErrors: false);
            if (iterableType != emptyGenericType)
            {
                var anyIterable = CreateIterableType(anyType);
                arrayLikeTargetParts = FilterType(childrenTargetType, t => IsTypeAssignableTo(t, anyIterable));
                nonArrayLikeTargetParts = FilterType(childrenTargetType, t => !IsTypeAssignableTo(t, anyIterable));
            }
            else
            {
                arrayLikeTargetParts = FilterType(childrenTargetType, IsArrayOrTupleLikeType);
                nonArrayLikeTargetParts = FilterType(childrenTargetType, t => !IsArrayOrTupleLikeType(t));
            }
            var tagName = At(node, store.TagName(store.OpeningElement(containingElement.Node)));
            if (moreThanOneRealChildren)
            {
                if (arrayLikeTargetParts != neverType)
                {
                    var realSource = CreateTupleType([.. CheckJsxChildren(containingElement)]);
                    var children = GenerateJsxChildren(containingElement, GetInvalidTextualChildDiagnostic);
                    result = ElaborateIterableOrArrayLikeTargetElementwise(children, realSource, arrayLikeTargetParts, relation, containingMessageChain, errorOutputContainer) || result;
                }
                else if (!IsTypeRelatedTo(GetIndexedAccessType(source, childrenNameType), childrenTargetType, relation))
                {
                    // arity mismatch
                    result = true;
                    var diag = Error(tagName, Diagnostics.This_JSX_tag_s_0_prop_expects_a_single_child_of_type_1_but_multiple_children_were_provided, childrenPropName, TypeToString(childrenTargetType));
                    if (errorOutputContainer is { SkipLogging: true })
                    {
                        (errorOutputContainer.Errors ??= []).Add(diag);
                    }
                }
            }
            else
            {
                if (nonArrayLikeTargetParts != neverType)
                {
                    var child = At(node, validChildren[0]);
                    var elem = GetElaborationElementForJsxChild(child, childrenNameType, GetInvalidTextualChildDiagnostic);
                    if (elem is { } e)
                    {
                        result = ElaborateElementwise([e], source, target, relation, containingMessageChain, errorOutputContainer) || result;
                    }
                }
                else if (!IsTypeRelatedTo(GetIndexedAccessType(source, childrenNameType), childrenTargetType, relation))
                {
                    // arity mismatch
                    result = true;
                    var diag = Error(tagName, Diagnostics.This_JSX_tag_s_0_prop_expects_type_1_which_requires_multiple_children_but_only_a_single_child_was_provided, childrenPropName, TypeToString(childrenTargetType));
                    if (errorOutputContainer is { SkipLogging: true })
                    {
                        (errorOutputContainer.Errors ??= []).Add(diag);
                    }
                }
            }
        }
        return result;

        DiagnosticMessage GetInvalidTextualChildDiagnostic()
        {
            if (invalidTextDiagnostic is null)
            {
                var tagNameText = GetTextOfNode(At(node, store.TagName(store.Parent(node.Node))));
                var childPropName = GetJsxElementChildrenPropertyName(GetJsxNamespaceAt(node));
                var childrenPropName = childPropName is null ? "children" : Utilities.UnescapeLeadingUnderscores(childPropName);
                var childrenTargetType = GetIndexedAccessType(target, GetStringLiteralType(childrenPropName));
                var diagnostic = Diagnostics._0_components_don_t_accept_text_as_child_elements_Text_in_JSX_has_the_type_string_but_the_expected_type_of_1_is_2;
                invalidTextDiagnostic = new DiagnosticMessage(diagnostic.Code, diagnostic.Category, "!!ALREADY FORMATTED!!",
                    Utilities.FormatStringFromArgs(diagnostic.Message, [tagNameText, childrenPropName, TypeToString(childrenTargetType)]),
                    diagnostic.ReportsUnnecessary, diagnostic.ElidedInCompatibilityPyramid, diagnostic.ReportsDeprecated);
            }
            return invalidTextDiagnostic;
        }
    }

    // checker.ts elaborateIterableOrArrayLikeTargetElementwise: assumes `target` type is assignable to the `Iterable`
    // type, if `Iterable` is defined, or that it's an array or tuple-like type, if `Iterable` is not defined.
    private bool ElaborateIterableOrArrayLikeTargetElementwise(IEnumerable<ElaborationElement> iterator, Type source, Type target, Relation relation,
        Func<DiagnosticMessageChain?>? containingMessageChain, ErrorOutputContainer? errorOutputContainer)
    {
        var tupleOrArrayLikeTargetParts = FilterType(target, IsArrayOrTupleLikeType);
        var nonTupleOrArrayLikeTargetParts = FilterType(target, t => !IsArrayOrTupleLikeType(t));
        // If `nonTupleOrArrayLikeTargetParts` is not `never`, then that should mean `Iterable` is defined.
        var iterationType = nonTupleOrArrayLikeTargetParts != neverType
            ? GetIterationTypeOfIterable(IterationUse.ForOf, IterationTypeKind.Yield, nonTupleOrArrayLikeTargetParts, NodeRef.None)
            : Type.None;
        var reportedError = false;
        foreach (var value in iterator)
        {
            var (prop, next, nameType, errorMessage) = value;
            var targetPropType = iterationType;
            var targetIndexedPropType = tupleOrArrayLikeTargetParts != neverType ? GetBestMatchIndexedAccessTypeOrUndefined(source, tupleOrArrayLikeTargetParts, nameType) : Type.None;
            if (!targetIndexedPropType.IsNone && (Types.Flags(targetIndexedPropType) & TypeFlags.IndexedAccess) == 0)
            {
                // Don't elaborate on indexes on generic variables
                targetPropType = !iterationType.IsNone ? GetUnionType([iterationType, targetIndexedPropType]) : targetIndexedPropType;
            }
            if (targetPropType.IsNone)
            {
                continue;
            }
            var sourcePropType = GetIndexedAccessTypeOrUndefined(source, nameType);
            if (sourcePropType.IsNone)
            {
                continue;
            }
            var propName = GetPropertyNameFromIndex(nameType, NodeRef.None);
            if (!CheckTypeRelatedTo(sourcePropType, targetPropType, relation, NodeRef.None))
            {
                var elaborated = !next.IsNone && ElaborateError(next, sourcePropType, targetPropType, relation, null, containingMessageChain, errorOutputContainer);
                reportedError = true;
                if (!elaborated)
                {
                    // Issue error on the prop itself, since the prop couldn't elaborate the error
                    var resultObj = errorOutputContainer ?? new ErrorOutputContainer();
                    // Use the expression type, if available
                    var specificSource = !next.IsNone ? CheckExpressionForMutableLocationWithContextualType(next, sourcePropType) : sourcePropType;
                    if (exactOptionalPropertyTypes && IsExactOptionalPropertyMismatch(specificSource, targetPropType))
                    {
                        var diag = CreateDiagnosticForNode(prop, Diagnostics.Type_0_is_not_assignable_to_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_type_of_the_target,
                            TypeToString(specificSource), TypeToString(targetPropType));
                        diagnostics.Add(diag);
                        resultObj.Errors = [diag];
                    }
                    else
                    {
                        var targetIsOptional = propName is not null && (SymbolFlagsOf(OrUnknownSymbol(GetPropertyOfType(tupleOrArrayLikeTargetParts, propName))) & SymbolFlags.Optional) != 0;
                        var sourceIsOptional = propName is not null && (SymbolFlagsOf(OrUnknownSymbol(GetPropertyOfType(source, propName))) & SymbolFlags.Optional) != 0;
                        targetPropType = RemoveMissingType(targetPropType, targetIsOptional);
                        sourcePropType = RemoveMissingType(sourcePropType, targetIsOptional && sourceIsOptional);
                        var result = CheckTypeRelatedTo(specificSource, targetPropType, relation, prop, errorMessage, containingMessageChain, resultObj);
                        if (result && specificSource != sourcePropType)
                        {
                            // If for whatever reason the expression type doesn't yield an error, make sure we still issue an error on the sourcePropType
                            CheckTypeRelatedTo(sourcePropType, targetPropType, relation, prop, errorMessage, containingMessageChain, resultObj);
                        }
                    }
                }
            }
        }
        return reportedError;
    }

    // checker.ts getSuggestedSymbolForNonexistentJSXAttribute
    internal SymbolRef GetSuggestedSymbolForNonexistentJSXAttribute(string name, Type containingType)
    {
        var properties = GetPropertiesOfType(containingType);
        var jsxSpecific = SymbolRef.None;
        switch (name)
        {
            case "for":
                jsxSpecific = properties.Find(x => NameOf(x) == "htmlFor");
                break;
            case "class":
                jsxSpecific = properties.Find(x => NameOf(x) == "className");
                break;
        }
        return !jsxSpecific.IsNone ? jsxSpecific : GetSpellingSuggestionForName(name, properties, SymbolFlags.Value);
    }

    // ---- resolution ----

    // checker.ts getJSXFragmentType
    private Type GetJSXFragmentType(NodeRef node)
    {
        // An opening fragment is required in order for `getJsxNamespace` to give the fragment factory
        if (jsxFragmentTypes.TryGetValue(node.File, out var cached))
        {
            return cached;
        }
        var jsxFragmentFactoryName = GetJsxNamespace(node);
        // #38720/60122, allow null as jsxFragmentFactory
        var shouldResolveFactoryReference = compilerOptions.Jsx == JsxEmit.React && jsxFragmentFactoryName != "null"; // (|| jsxFragmentFactory: never set)
        if (!shouldResolveFactoryReference)
        {
            return jsxFragmentTypes[node.File] = anyType;
        }
        var shouldModuleRefErr = compilerOptions.Jsx != JsxEmit.Preserve && compilerOptions.Jsx != JsxEmit.ReactNative;
        var jsxFactorySymbol = GetJsxNamespaceContainerForImplicitImport(node);
        if (jsxFactorySymbol.IsNone)
        {
            jsxFactorySymbol = ResolveName(node, jsxFragmentFactoryName, NodeRef.None, shouldModuleRefErr ? SymbolFlags.Value : SymbolFlags.Value & ~SymbolFlags.Enum,
                Diagnostics.Using_JSX_fragments_requires_fragment_factory_0_to_be_in_scope_but_it_could_not_be_found, isUse: true);
        }
        if (jsxFactorySymbol.IsNone)
        {
            return jsxFragmentTypes[node.File] = errorType;
        }
        if (NameOf(jsxFactorySymbol) == ReactFragment)
        {
            var fragmentType = GetTypeOfSymbol(jsxFactorySymbol);
            return jsxFragmentTypes[node.File] = fragmentType;
        }
        var resolvedAlias = (SymbolFlagsOf(jsxFactorySymbol) & SymbolFlags.Alias) == 0 ? jsxFactorySymbol : ResolveAlias(jsxFactorySymbol);
        var reactExports = GetExportsOfSymbol(resolvedAlias);
        var typeSymbol = GetSymbol(reactExports, ReactFragment, SymbolFlags.BlockScopedVariable);
        var type = typeSymbol.IsNone ? errorType : GetTypeOfSymbol(typeSymbol);
        return jsxFragmentTypes[node.File] = type;
    }

    // checker.ts resolveJsxOpeningLikeElement
    private Signature ResolveJsxOpeningLikeElement(NodeRef node, List<Signature>? candidatesOutArray, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var isJsxOpenFragment = store.Kind(node.Node) == SyntaxKind.JsxOpeningFragment;
        Type exprTypes;
        if (!isJsxOpenFragment)
        {
            var tagName = At(node, store.TagName(node.Node));
            if (IsJsxIntrinsicTagName(store, tagName.Node))
            {
                var result = GetIntrinsicAttributesTypeFromJsxOpeningLikeElement(node);
                var fakeSignature = CreateSignatureForJSXIntrinsic(node, result);
                var attributes = At(node, store.Attributes(node.Node));
                CheckTypeAssignableToAndOptionallyElaborate(CheckExpressionWithContextualType(attributes, GetEffectiveFirstArgumentForJsxSignature(fakeSignature, node), null, CheckMode.Normal),
                    result, tagName, attributes);
                var typeArguments = store.TypeArguments(node.Node);
                if (!typeArguments.IsNone && store.Length(typeArguments) > 0)
                {
                    CheckSourceElements(node, typeArguments);
                    var start = Scanner.SkipTrivia(FileTextOf(node.File), store.Pos(typeArguments));
                    diagnostics.Add(CreateFileDiagnosticAt(node.File, start, store.End(typeArguments) - start, Diagnostics.Expected_0_type_arguments_but_got_1, 0, store.Length(typeArguments)));
                }
                return fakeSignature;
            }
            exprTypes = CheckExpression(tagName);
        }
        else
        {
            exprTypes = GetJSXFragmentType(node);
        }
        var apparentType = GetApparentType(exprTypes);
        if (IsErrorType(apparentType))
        {
            return ResolveErrorCall(node);
        }
        var signatures = GetUninstantiatedJsxSignaturesOfType(exprTypes, node);
        if (IsUntypedFunctionCall(exprTypes, apparentType, signatures.Count, 0))
        {
            return ResolveUntypedCall(node);
        }
        if (signatures.Count == 0)
        {
            // We found no signatures at all, which is an error
            if (isJsxOpenFragment)
            {
                Error(node, Diagnostics.JSX_element_type_0_does_not_have_any_construct_or_call_signatures, GetTextOfNode(node));
            }
            else
            {
                var tagName = At(node, store.TagName(node.Node));
                Error(tagName, Diagnostics.JSX_element_type_0_does_not_have_any_construct_or_call_signatures, GetTextOfNode(tagName));
            }
            return ResolveErrorCall(node);
        }
        return ResolveCall(node, signatures, candidatesOutArray, checkMode, SignatureFlags.None);
    }

    // checker.ts checkApplicableSignatureForJsxCallLikeElement: whether the given signature can possibly be a signature
    // called by the JSX opening-like element (the errors, when reported, go to the error output container)
    private bool CheckApplicableSignatureForJsxCallLikeElement(NodeRef node, Signature signature, Relation relation, CheckMode checkMode, bool reportErrors,
        Func<DiagnosticMessageChain?>? containingMessageChain, ErrorOutputContainer errorOutputContainer)
    {
        // Stateless function components can have maximum of three arguments: "props", "context", and "updater".
        // However "context" and "updater" are implicit and can't be specify by users. Only the first parameter, props,
        // can be specified by users through attributes property.
        var store = StoreOf(node);
        var isFragment = store.Kind(node.Node) == SyntaxKind.JsxOpeningFragment;
        var paramType = GetEffectiveFirstArgumentForJsxSignature(signature, node);
        var attributesType = isFragment
            ? CreateJsxAttributesTypeFromAttributesProperty(node)
            : CheckExpressionWithContextualType(At(node, store.Attributes(node.Node)), paramType, null, checkMode);
        var checkAttributesType = (checkMode & CheckMode.SkipContextSensitive) != 0 ? GetRegularTypeOfObjectLiteral(attributesType) : attributesType;
        return CheckTagNameDoesNotExpectTooManyArguments() && CheckTypeRelatedToAndOptionallyElaborate(
            checkAttributesType,
            paramType,
            relation,
            reportErrors ? (isFragment ? node : At(node, store.TagName(node.Node))) : NodeRef.None,
            isFragment ? NodeRef.None : At(node, store.Attributes(node.Node)),
            null,
            containingMessageChain,
            errorOutputContainer);

        bool CheckTagNameDoesNotExpectTooManyArguments()
        {
            if (!GetJsxNamespaceContainerForImplicitImport(node).IsNone)
            {
                return true; // factory is implicitly jsx/jsxdev - assume it fits the bill, since we don't strongly look for the jsx/jsxs/jsxDEV factory APIs anywhere else (at least not yet)
            }
            // We assume fragments have the correct arity since the node does not have attributes
            var tagType = IsJsxOpeningLikeElement(store, node.Node) && !IsJsxIntrinsicTagName(store, store.TagName(node.Node))
                ? CheckExpression(At(node, store.TagName(node.Node)))
                : Type.None;
            if (tagType.IsNone)
            {
                return true;
            }
            var tagCallSignatures = GetSignaturesOfType(tagType, SignatureKind.Call);
            if (tagCallSignatures.Count == 0)
            {
                return true;
            }
            var factory = GetJsxFactoryEntity(node);
            if (factory is null)
            {
                return true;
            }
            var factorySymbol = ResolveJsxFactoryEntity(factory, node);
            if (factorySymbol.IsNone)
            {
                return true;
            }
            var factoryType = GetTypeOfSymbol(factorySymbol);
            var callSignatures = GetSignaturesOfType(factoryType, SignatureKind.Call);
            if (callSignatures.Count == 0)
            {
                return true;
            }
            var hasFirstParamSignatures = false;
            var maxParamCount = 0;
            // Check that _some_ first parameter expects a FC-like thing, and that some overload of the SFC expects an acceptable number of arguments
            foreach (var sig in callSignatures)
            {
                var firstparam = GetTypeAtPosition(sig, 0);
                var signaturesOfParam = GetSignaturesOfType(firstparam, SignatureKind.Call);
                if (signaturesOfParam.Count == 0)
                {
                    continue;
                }
                foreach (var paramSig in signaturesOfParam)
                {
                    hasFirstParamSignatures = true;
                    if (HasEffectiveRestParameter(paramSig))
                    {
                        return true; // some signature has a rest param, so function components can have an arbitrary number of arguments
                    }
                    var paramCount = GetParameterCount(paramSig);
                    if (paramCount > maxParamCount)
                    {
                        maxParamCount = paramCount;
                    }
                }
            }
            if (!hasFirstParamSignatures)
            {
                // Not a single signature had a first parameter which expected a signature - for back compat, and
                // to guard against generic factories which won't have signatures directly, do not error
                return true;
            }
            var absoluteMinArgCount = int.MaxValue;
            foreach (var tagSig in tagCallSignatures)
            {
                var tagRequiredArgCount = GetMinArgumentCount(tagSig);
                if (tagRequiredArgCount < absoluteMinArgCount)
                {
                    absoluteMinArgCount = tagRequiredArgCount;
                }
            }
            if (absoluteMinArgCount <= maxParamCount)
            {
                return true; // some signature accepts the number of arguments the function component provides
            }
            if (reportErrors)
            {
                var tagName = At(node, store.TagName(node.Node));
                var diag = CreateDiagnosticForNode(tagName, Diagnostics.Tag_0_expects_at_least_1_arguments_but_the_JSX_factory_2_provides_at_most_3,
                    EntityNameToString(tagName), absoluteMinArgCount, string.Join('.', factory), maxParamCount);
                var tagNameSymbol = GetSymbolAtLocation(tagName);
                var tagNameDeclaration = tagNameSymbol.IsNone ? NodeRef.None : ValueDeclarationOf(tagNameSymbol);
                if (!tagNameDeclaration.IsNone)
                {
                    AddRelatedInfo(diag, CreateRelatedForNode(tagNameDeclaration, Diagnostics._0_is_declared_here, EntityNameToString(tagName)));
                }
                if (errorOutputContainer.SkipLogging)
                {
                    (errorOutputContainer.Errors ??= []).Add(diag);
                }
                else
                {
                    diagnostics.Add(diag);
                }
            }
            return false;
        }
    }

    // checker.ts resolveEntityName for the synthetic factory entity name (ignoreErrors, the location given)
    private SymbolRef ResolveJsxFactoryEntity(string[] parts, NodeRef location)
    {
        var meaning = parts.Length == 1 ? SymbolFlags.Value : SymbolFlags.Namespace;
        var symbol = GetMergedSymbol(ResolveName(location, parts[0], NodeRef.None, meaning, null, isUse: true));
        for (var i = 1; i < parts.Length && !symbol.IsNone; i++)
        {
            if (symbol == unknownSymbol)
            {
                return symbol;
            }
            var namespaceSymbol = symbol;
            symbol = GetMergedSymbol(GetSymbol(GetExportsOfSymbol(namespaceSymbol), parts[i], i == parts.Length - 1 ? SymbolFlags.Value : SymbolFlags.Namespace));
        }
        if (symbol.IsNone)
        {
            return SymbolRef.None;
        }
        return (SymbolFlagsOf(symbol) & SymbolFlags.Value) != 0 ? symbol : ResolveAlias(symbol);
    }

    // checker.ts getUninstantiatedJsxSignaturesOfType
    private Items<Signature> GetUninstantiatedJsxSignaturesOfType(Type elementType, NodeRef caller)
    {
        if ((Types.Flags(elementType) & TypeFlags.String) != 0)
        {
            return [anySignature];
        }
        if ((Types.Flags(elementType) & TypeFlags.StringLiteral) != 0)
        {
            var intrinsicType = GetIntrinsicAttributesTypeFromStringLiteralType(elementType, caller);
            if (intrinsicType.IsNone)
            {
                Error(caller, Diagnostics.Property_0_does_not_exist_on_type_1, GetStringLiteralValue(elementType), "JSX." + JsxIntrinsicElements);
                return [];
            }
            var fakeSignature = CreateSignatureForJSXIntrinsic(caller, intrinsicType);
            return [fakeSignature];
        }
        var apparentElemType = GetApparentType(elementType);
        // Resolve the signatures, preferring constructor
        var signatures = GetSignaturesOfType(apparentElemType, SignatureKind.Construct);
        if (signatures.Count == 0)
        {
            // No construct signatures, try call signatures
            signatures = GetSignaturesOfType(apparentElemType, SignatureKind.Call);
        }
        if (signatures.Count == 0 && (Types.Flags(apparentElemType) & TypeFlags.Union) != 0)
        {
            // If each member has some combination of new/call signatures; make a union signature list for those
            signatures = GetUnionSignatures(Array.ConvertAll(Types.Elements(Types.Union(apparentElemType).Types).ToArray(), t => GetUninstantiatedJsxSignaturesOfType(t, caller)));
        }
        return signatures;
    }

    // the value of a string literal type
    private string GetStringLiteralValue(Type type) => (string)Types.Literal(type).Value!;

    // checker.ts getEffectiveFirstArgumentForJsxSignature
    private Type GetEffectiveFirstArgumentForJsxSignature(Signature signature, NodeRef node) =>
        Kind(node) == SyntaxKind.JsxOpeningFragment || GetJsxReferenceKind(node) != JsxReferenceKind.Component
            ? GetJsxPropsTypeFromCallSignature(signature, node)
            : GetJsxPropsTypeFromClassType(signature, node);

    // checker.ts getJsxPropsTypeFromCallSignature
    private Type GetJsxPropsTypeFromCallSignature(Signature sig, NodeRef context)
    {
        var propsType = GetTypeOfFirstParameterOfSignatureWithFallback(sig, unknownType);
        propsType = GetJsxManagedAttributesFromLocatedAttributes(context, GetJsxNamespaceAt(context), propsType);
        var intrinsicAttribs = GetJsxType(JsxIntrinsicAttributes, context);
        if (!IsErrorType(intrinsicAttribs))
        {
            propsType = IntersectTypes(intrinsicAttribs, propsType);
        }
        return propsType;
    }

    // checker.ts getJsxPropsTypeForSignatureFromMember
    private Type GetJsxPropsTypeForSignatureFromMember(Signature sig, string forcedLookupLocation)
    {
        if (Signatures.Record(sig).CompositeSignatures is { } compositeSignatures)
        {
            // JSX Elements using the legacy `props`-field based lookup (eg, react class components) need to treat the `props` member as an input
            // instead of an output position when resolving the signature. We need to go back to the input signatures of the composite signature,
            // get the type of `props` on each return type individually, and then _intersect them_, rather than union them (as would normally occur
            // for a union signature). It's an unfortunate quirk of looking in the output of the signature for the type we want to use for the input.
            // The default behavior of `getTypeOfFirstParameterOfSignatureWithFallback` when no `props` member name is defined is much more sane.
            var results = new List<Type>();
            foreach (var signature in compositeSignatures)
            {
                var instance = GetReturnTypeOfSignature(signature);
                if (IsTypeAny(instance))
                {
                    return instance;
                }
                var propType = GetTypeOfPropertyOfType(instance, forcedLookupLocation);
                if (propType.IsNone)
                {
                    return Type.None;
                }
                results.Add(propType);
            }
            return GetIntersectionType([.. results]); // Same result for both union and intersection signatures
        }
        var instanceType = GetReturnTypeOfSignature(sig);
        return IsTypeAny(instanceType) ? instanceType : GetTypeOfPropertyOfType(instanceType, forcedLookupLocation);
    }

    // checker.ts getStaticTypeOfReferencedJsxConstructor
    private Type GetStaticTypeOfReferencedJsxConstructor(NodeRef context)
    {
        var store = StoreOf(context);
        if (store.Kind(context.Node) == SyntaxKind.JsxOpeningFragment)
        {
            return GetJSXFragmentType(context);
        }
        if (IsJsxIntrinsicTagName(store, store.TagName(context.Node)))
        {
            var result = GetIntrinsicAttributesTypeFromJsxOpeningLikeElement(context);
            var fakeSignature = CreateSignatureForJSXIntrinsic(context, result);
            return GetOrCreateTypeFromSignature(fakeSignature);
        }
        var tagType = CheckExpressionCached(At(context, store.TagName(context.Node)));
        if ((Types.Flags(tagType) & TypeFlags.StringLiteral) != 0)
        {
            var result = GetIntrinsicAttributesTypeFromStringLiteralType(tagType, context);
            if (result.IsNone)
            {
                return errorType;
            }
            var fakeSignature = CreateSignatureForJSXIntrinsic(context, result);
            return GetOrCreateTypeFromSignature(fakeSignature);
        }
        return tagType;
    }

    // checker.ts getJsxManagedAttributesFromLocatedAttributes
    private Type GetJsxManagedAttributesFromLocatedAttributes(NodeRef context, SymbolRef ns, Type attributesType)
    {
        var managedSym = GetJsxLibraryManagedAttributes(ns);
        if (!managedSym.IsNone)
        {
            var ctorType = GetStaticTypeOfReferencedJsxConstructor(context);
            var result = InstantiateAliasOrInterfaceWithDefaults(managedSym, Utilities.IsInJSFile(StoreOf(context), context.Node), ctorType, attributesType);
            if (!result.IsNone)
            {
                return result;
            }
        }
        return attributesType;
    }

    // checker.ts getJsxPropsTypeFromClassType
    private Type GetJsxPropsTypeFromClassType(Signature sig, NodeRef context)
    {
        var ns = GetJsxNamespaceAt(context);
        var forcedLookupLocation = GetJsxElementPropertiesName(ns);
        var attributesType = forcedLookupLocation is null
            // If there is no type ElementAttributesProperty, return the type of the first parameter of the signature, which should be the props type
            ? GetTypeOfFirstParameterOfSignatureWithFallback(sig, unknownType)
            : forcedLookupLocation.Length == 0
            // If there is no e.g. 'props' member in ElementAttributesProperty, use the element class type instead
            ? GetReturnTypeOfSignature(sig)
            // Otherwise get the type of the property on the signature return type
            : GetJsxPropsTypeForSignatureFromMember(sig, forcedLookupLocation);
        if (attributesType.IsNone)
        {
            // There is no property named 'props' on this instance type
            var store = StoreOf(context);
            if (!string.IsNullOrEmpty(forcedLookupLocation) && store.Length(store.Properties(store.Attributes(context.Node))) > 0)
            {
                Error(context, Diagnostics.JSX_element_class_does_not_support_attributes_because_it_does_not_have_a_0_property, Utilities.UnescapeLeadingUnderscores(forcedLookupLocation));
            }
            return unknownType;
        }
        attributesType = GetJsxManagedAttributesFromLocatedAttributes(context, ns, attributesType);
        if (IsTypeAny(attributesType))
        {
            // Props is of type 'any' or unknown
            return attributesType;
        }
        // Normal case -- add in IntrinsicClassElements<T> and IntrinsicElements
        var apparentAttributesType = attributesType;
        var intrinsicClassAttribs = GetJsxType(JsxIntrinsicClassAttributes, context);
        if (!IsErrorType(intrinsicClassAttribs))
        {
            var typeParams = GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(Types.Symbol(intrinsicClassAttribs));
            var hostClassType = GetReturnTypeOfSignature(sig);
            Type libraryManagedAttributeType;
            if (typeParams is not null)
            {
                // apply JSX.IntrinsicClassElements<hostClassType, ...>
                var typeParamArray = typeParams.ToArray();
                var inferredArgs = FillMissingTypeArguments([hostClassType], typeParamArray, GetMinTypeArgumentCount(typeParamArray), Utilities.IsInJSFile(StoreOf(context), context.Node));
                libraryManagedAttributeType = InstantiateType(intrinsicClassAttribs, CreateTypeMapper(typeParamArray, inferredArgs));
            }
            else
            {
                // or JSX.IntrinsicClassElements has no generics.
                libraryManagedAttributeType = intrinsicClassAttribs;
            }
            apparentAttributesType = IntersectTypes(libraryManagedAttributeType, apparentAttributesType);
        }
        var intrinsicAttribs = GetJsxType(JsxIntrinsicAttributes, context);
        if (!IsErrorType(intrinsicAttribs))
        {
            apparentAttributesType = IntersectTypes(intrinsicAttribs, apparentAttributesType);
        }
        return apparentAttributesType;
    }

    // checker.ts instantiateAliasOrInterfaceWithDefaults
    private Type InstantiateAliasOrInterfaceWithDefaults(SymbolRef managedSym, bool inJs, params Type[] typeArguments)
    {
        var declaredManagedType = GetDeclaredTypeOfSymbol(managedSym); // fetches interface type, or initializes symbol links type parmaeters
        if ((SymbolFlagsOf(managedSym) & SymbolFlags.TypeAlias) != 0)
        {
            var parameters = GetTypeAliasTypeParameters(managedSym) ?? [];
            if (parameters.Length >= typeArguments.Length)
            {
                var args = FillMissingTypeArguments(typeArguments, parameters, typeArguments.Length, inJs);
                return args.Length == 0 ? declaredManagedType : GetTypeAliasInstantiation(managedSym, args);
            }
        }
        var typeParameters = (Types.Flags(declaredManagedType) & TypeFlags.Object) != 0 && (Types.ObjectFlagsOf(declaredManagedType) & ObjectFlags.ClassOrInterface) != 0
            ? Types.Elements(Types.Object(declaredManagedType).TypeParameters).ToArray()
            : [];
        if (typeParameters.Length >= typeArguments.Length)
        {
            if (typeParameters.Length == 0)
            {
                return declaredManagedType;
            }
            var args = FillMissingTypeArguments(typeArguments, typeParameters, typeArguments.Length, inJs);
            return CreateTypeReference(declaredManagedType, args);
        }
        return Type.None;
    }

    // checker.ts getJsxLibraryManagedAttributes
    private SymbolRef GetJsxLibraryManagedAttributes(SymbolRef jsxNamespace) =>
        jsxNamespace.IsNone ? SymbolRef.None : GetSymbol(ExportsOf(jsxNamespace), JsxLibraryManagedAttributes, SymbolFlags.Type);

    // checker.ts getJsxElementTypeSymbol
    private SymbolRef GetJsxElementTypeSymbol(SymbolRef jsxNamespace) =>
        jsxNamespace.IsNone ? SymbolRef.None : GetSymbol(ExportsOf(jsxNamespace), JsxElementType, SymbolFlags.Type);

    // checker.ts getJsxElementPropertiesName: e.g. "props" for React.d.ts, or null (undefined) if
    // ElementAttributesProperty doesn't exist (which means all non-intrinsic elements' attributes type is 'any'), or ""
    // if it has 0 properties (which means every non-intrinsic elements' attributes type is the element instance type)
    private string? GetJsxElementPropertiesName(SymbolRef jsxNamespace) =>
        GetNameFromJsxElementAttributesContainer(JsxElementAttributesPropertyNameContainer, jsxNamespace);

    // checker.ts getJsxElementChildrenPropertyName
    private string? GetJsxElementChildrenPropertyName(SymbolRef jsxNamespace)
    {
        if (compilerOptions.Jsx is JsxEmit.ReactJSX or JsxEmit.ReactJSXDev)
        {
            // In these JsxEmit modes the children property is fixed to 'children'
            return "children";
        }
        return GetNameFromJsxElementAttributesContainer(JsxElementChildrenAttributeNameContainer, jsxNamespace);
    }

    // checker.ts getNameFromJsxElementAttributesContainer: look into JSX namespace and then look for container with
    // matching name as nameOfAttribPropContainer. Get a single property from that container if existed. Report an
    // error if there are more than one property.
    private string? GetNameFromJsxElementAttributesContainer(string nameOfAttribPropContainer, SymbolRef jsxNamespace)
    {
        // JSX.ElementAttributesProperty | JSX.ElementChildrenAttribute [symbol]
        var jsxElementAttribPropInterfaceSym = jsxNamespace.IsNone ? SymbolRef.None : GetSymbol(ExportsOf(jsxNamespace), nameOfAttribPropContainer, SymbolFlags.Type);
        if (jsxElementAttribPropInterfaceSym.IsNone)
        {
            return null;
        }
        // JSX.ElementAttributesProperty | JSX.ElementChildrenAttribute [type]
        var jsxElementAttribPropInterfaceType = GetDeclaredTypeOfSymbol(jsxElementAttribPropInterfaceSym);
        // The properties of JSX.ElementAttributesProperty | JSX.ElementChildrenAttribute
        var propertiesOfJsxElementAttribPropInterface = GetPropertiesOfType(jsxElementAttribPropInterfaceType);
        // Element Attributes has zero properties, so the element attributes type will be the class instance type
        if (propertiesOfJsxElementAttribPropInterface.Count == 0)
        {
            return "";
        }
        // Element Attributes has one property, so the element attributes type will be the type of the corresponding
        // property of the class instance type
        if (propertiesOfJsxElementAttribPropInterface.Count == 1)
        {
            return NameOf(propertiesOfJsxElementAttribPropInterface[0]);
        }
        if (DeclarationsOf(jsxElementAttribPropInterfaceSym) is { } declarations)
        {
            // More than one property on ElementAttributesProperty is an error
            Error(declarations[0], Diagnostics.The_global_type_JSX_0_may_not_have_more_than_one_property, Utilities.UnescapeLeadingUnderscores(nameOfAttribPropContainer));
        }
        return null;
    }

    // checker.ts getIntrinsicAttributesTypeFromStringLiteralType
    private Type GetIntrinsicAttributesTypeFromStringLiteralType(Type type, NodeRef location)
    {
        // If the elemType is a stringLiteral type, we can then provide a check to make sure that the string literal type is one of the Jsx intrinsic element type
        // For example:
        //      var CustomTag: "h1" = "h1";
        //      <CustomTag> Hello World </CustomTag>
        var intrinsicElementsType = GetJsxType(JsxIntrinsicElements, location);
        if (!IsErrorType(intrinsicElementsType))
        {
            var stringLiteralTypeName = GetStringLiteralValue(type);
            var intrinsicProp = GetPropertyOfType(intrinsicElementsType, Utilities.EscapeLeadingUnderscores(stringLiteralTypeName));
            if (!intrinsicProp.IsNone)
            {
                return GetTypeOfSymbol(intrinsicProp);
            }
            var indexSignatureType = GetIndexTypeOfType(intrinsicElementsType, stringType);
            if (!indexSignatureType.IsNone)
            {
                return indexSignatureType;
            }
            return Type.None;
        }
        // If we need to report an error, we already done so here. So just return any to prevent any more error downstream
        return anyType;
    }

    // checker.ts getJsxReferenceKind
    private JsxReferenceKind GetJsxReferenceKind(NodeRef node)
    {
        var store = StoreOf(node);
        var tagName = store.TagName(node.Node);
        if (IsJsxIntrinsicTagName(store, tagName))
        {
            return JsxReferenceKind.Mixed;
        }
        var tagType = GetApparentType(CheckExpression(At(node, tagName)));
        if (GetSignaturesOfType(tagType, SignatureKind.Construct).Count > 0)
        {
            return JsxReferenceKind.Component;
        }
        if (GetSignaturesOfType(tagType, SignatureKind.Call).Count > 0)
        {
            return JsxReferenceKind.Function;
        }
        return JsxReferenceKind.Mixed;
    }

    // checker.ts createSignatureForJSXIntrinsic (the synthetic function type node declaration is not created)
    private Signature CreateSignatureForJSXIntrinsic(NodeRef node, Type result)
    {
        var ns = GetJsxNamespaceAt(node);
        // We fake up a SFC signature for each intrinsic, however a more specific per-element signature drawn from the JSX declaration
        // file would probably be preferable.
        var typeSymbol = ns.IsNone ? SymbolRef.None : GetSymbol(GetExportsOfSymbol(ns), JsxElement, SymbolFlags.Type);
        var parameterSymbol = CreateSymbol(SymbolFlags.FunctionScopedVariable, "props");
        ValueSymbolLinks.Get(parameterSymbol).ResolvedType = result;
        var returnType = typeSymbol.IsNone ? errorType : GetDeclaredTypeOfSymbol(typeSymbol);
        return CreateSignature(NodeRef.None, TypeList.Empty, SymbolRef.None, [parameterSymbol], returnType, 1, SignatureFlags.None);
    }

    // checker.ts getIntrinsicAttributesTypeFromJsxOpeningLikeElement: the attributes type of the given intrinsic
    // opening-like Jsx element by resolving the tag name
    private Type GetIntrinsicAttributesTypeFromJsxOpeningLikeElement(NodeRef node)
    {
        if (JsxElementLinks.Has(node) && !JsxElementLinks.Get(node).ResolvedJsxElementAttributesType.IsNone)
        {
            return JsxElementLinks.Get(node).ResolvedJsxElementAttributesType;
        }
        var symbol = GetIntrinsicTagSymbol(node);
        var jsxFlags = JsxElementLinks.Has(node) ? JsxElementLinks.Get(node).JsxFlags : JsxFlags.None;
        Type result;
        if ((jsxFlags & JsxFlags.IntrinsicNamedElement) != 0)
        {
            var t = GetTypeOfSymbol(symbol);
            result = t.IsNone ? errorType : t;
        }
        else if ((jsxFlags & JsxFlags.IntrinsicIndexedElement) != 0)
        {
            var store = StoreOf(node);
            var propName = GetTextOfJsxAttributeName(store, store.TagName(node.Node));
            result = GetApplicableIndexInfoForName(GetJsxType(JsxIntrinsicElements, node), propName)?.Type ?? errorType;
        }
        else
        {
            result = errorType;
        }
        JsxElementLinks.Get(node).ResolvedJsxElementAttributesType = result;
        return result;
    }

    // checker.ts getIntrinsicTagSymbol: looks up an intrinsic tag name and returns a symbol that either points to an
    // intrinsic property (in which case jsxFlags will be IntrinsicNamedElement) or an intrinsic string index signature
    // (in which case jsxFlags will be IntrinsicIndexedElement). May also return unknownSymbol if both of these lookups fail.
    private SymbolRef GetIntrinsicTagSymbol(NodeRef node)
    {
        if (SymbolNodeLinks.Has(node) && !SymbolNodeLinks.Get(node).ResolvedSymbol.IsNone)
        {
            return SymbolNodeLinks.Get(node).ResolvedSymbol;
        }
        var result = GetIntrinsicTagSymbolWorker(node);
        SymbolNodeLinks.Get(node).ResolvedSymbol = result;
        return result;
    }

    private SymbolRef GetIntrinsicTagSymbolWorker(NodeRef node)
    {
        var intrinsicElementsType = GetJsxType(JsxIntrinsicElements, node);
        if (!IsErrorType(intrinsicElementsType))
        {
            // Property case
            var store = StoreOf(node);
            var propName = GetTextOfJsxAttributeName(store, store.TagName(node.Node));
            var intrinsicProp = GetPropertyOfType(intrinsicElementsType, propName);
            if (!intrinsicProp.IsNone)
            {
                JsxElementLinks.Get(node).JsxFlags |= JsxFlags.IntrinsicNamedElement;
                return intrinsicProp;
            }
            // Intrinsic string indexer case
            var indexSymbol = GetApplicableIndexSymbol(intrinsicElementsType, GetStringLiteralType(Utilities.UnescapeLeadingUnderscores(propName)));
            if (!indexSymbol.IsNone)
            {
                JsxElementLinks.Get(node).JsxFlags |= JsxFlags.IntrinsicIndexedElement;
                return indexSymbol;
            }
            if (!GetTypeOfPropertyOrIndexSignatureOfType(intrinsicElementsType, propName).IsNone)
            {
                JsxElementLinks.Get(node).JsxFlags |= JsxFlags.IntrinsicIndexedElement;
                return Types.Symbol(intrinsicElementsType);
            }
            // Wasn't found
            Error(node, Diagnostics.Property_0_does_not_exist_on_type_1, propName, "JSX." + JsxIntrinsicElements);
            return unknownSymbol;
        }
        if (noImplicitAny)
        {
            Error(node, Diagnostics.JSX_element_implicitly_has_type_any_because_no_interface_JSX_0_exists, JsxIntrinsicElements);
        }
        return unknownSymbol;
    }

    // symbolLinks.filteredIndexSymbolCache per index symbol, keyed by the declarations' node ids
    private readonly Dictionary<(SymbolRef, string), SymbolRef> filteredIndexSymbolCache = [];

    // checker.ts getApplicableIndexSymbol
    private SymbolRef GetApplicableIndexSymbol(Type type, Type keyType)
    {
        var allInfos = GetIndexInfosOfType(type);
        var infos = allInfos.FindAll(info => IsApplicableIndexType(keyType, info.KeyType));
        if (infos.Count == 0 || (Types.Flags(type) & TypeFlags.Object) == 0)
        {
            return SymbolRef.None;
        }
        var members = ResolveStructuredTypeMembers(type).Members;
        if (members.IsNone)
        {
            return SymbolRef.None;
        }
        var symbol = members.Get(InternalSymbolName.Index);
        // (`infos === getIndexInfosOfType(type)` in checker.ts compares a fresh filtered array by reference: never true,
        // so the filtered copy below is always made)
        if (symbol.IsNone)
        {
            return SymbolRef.None;
        }
        var declarationList = new List<NodeRef>();
        foreach (var info in infos)
        {
            if (!info.Declaration.IsNone)
            {
                declarationList.Add(info.Declaration);
            }
        }
        var nodeListId = string.Join(",", declarationList.ConvertAll(d => d.File + ":" + d.Node.Index));
        if (filteredIndexSymbolCache.TryGetValue((symbol, nodeListId), out var cached))
        {
            return cached;
        }
        var copy = CreateSymbol(SymbolFlags.Signature, InternalSymbolName.Index);
        var declarationRef = NewDeclarations(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(declarationList));
        ref var record = ref TransientSymbols.Record(copy);
        record.Declarations = declarationRef;
        var aliasSymbol = Types.Record(type).AliasSymbol;
        record.Parent = !aliasSymbol.IsNone ? aliasSymbol : !Types.Symbol(type).IsNone ? Types.Symbol(type) : GetSymbolAtLocation(Parent(declarationList[0]));
        filteredIndexSymbolCache[(symbol, nodeListId)] = copy;
        return copy;
    }

    // checker.ts getJsxStatelessElementTypeAt
    private Type GetJsxStatelessElementTypeAt(NodeRef location)
    {
        var jsxElementType = GetJsxElementTypeAt(location);
        return jsxElementType.IsNone ? Type.None : GetUnionType([jsxElementType, nullType]);
    }

    // checker.ts getJsxElementClassTypeAt
    private Type GetJsxElementClassTypeAt(NodeRef location)
    {
        var type = GetJsxType(JsxElementClass, location);
        return IsErrorType(type) ? Type.None : type;
    }

    // checker.ts getJsxElementTypeAt
    private Type GetJsxElementTypeAt(NodeRef location) => GetJsxType(JsxElement, location);

    // checker.ts getJsxElementTypeTypeAt
    private Type GetJsxElementTypeTypeAt(NodeRef location)
    {
        var ns = GetJsxNamespaceAt(location);
        if (ns.IsNone)
        {
            return Type.None;
        }
        var sym = GetJsxElementTypeSymbol(ns);
        if (sym.IsNone)
        {
            return Type.None;
        }
        var type = InstantiateAliasOrInterfaceWithDefaults(sym, Utilities.IsInJSFile(StoreOf(location), location.Node));
        return type.IsNone || IsErrorType(type) ? Type.None : type;
    }

    // ---- the JSX namespace and factory ----

    // checker.ts markJsxAliasReferenced
    private void MarkJsxAliasReferenced(NodeRef node)
    {
        if (!GetJsxNamespaceContainerForImplicitImport(node).IsNone)
        {
            return;
        }
        // The reactNamespace/jsxFactory's root symbol should be marked as 'used' so we don't incorrectly elide its import.
        // And if there is no reactNamespace/jsxFactory's symbol in scope when targeting React emit, we should issue an error.
        var store = StoreOf(node);
        var isFragment = store.Kind(node.Node) == SyntaxKind.JsxOpeningFragment;
        var jsxFactoryRefErr = compilerOptions.Jsx == JsxEmit.React ? Diagnostics.This_JSX_tag_requires_0_to_be_in_scope_but_it_could_not_be_found : null;
        var jsxFactoryNamespace = GetJsxNamespace(node);
        var jsxFactoryLocation = isFragment ? node : At(node, store.TagName(node.Node));
        var shouldFactoryRefErr = compilerOptions.Jsx != JsxEmit.Preserve && compilerOptions.Jsx != JsxEmit.ReactNative;
        var meaning = shouldFactoryRefErr ? SymbolFlags.Value : SymbolFlags.Value & ~SymbolFlags.Enum;
        // #38720/60122, allow null as jsxFragmentFactory
        var jsxFactorySym = SymbolRef.None;
        if (!(isFragment && jsxFactoryNamespace == "null"))
        {
            jsxFactorySym = ResolveName(jsxFactoryLocation, jsxFactoryNamespace, NodeRef.None, meaning, jsxFactoryRefErr, isUse: true);
        }
        if (!jsxFactorySym.IsNone)
        {
            // Mark local symbol as referenced here because it might not have been marked
            // if jsx emit was not jsxFactory as there wont be error being emitted
            MarkReferenced(jsxFactorySym, SymbolFlags.All);
            // (If react/jsxFactory symbol is alias, mark it as refereced: markAliasSymbolAsReferenced serves the emit's
            // import elision only, not ported)
        }
        // if JsxFragment, additionally mark jsx pragma as referenced, since `getJsxNamespace` above would have resolved to only the fragment factory if they are distinct
        if (isFragment)
        {
            var entity = GetJsxFactoryEntity(At(node, FileAt(node.File).Root));
            if (entity is not null)
            {
                ResolveName(jsxFactoryLocation, entity[0], NodeRef.None, meaning, jsxFactoryRefErr, isUse: true);
            }
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
            var fragment = PragmaFactoryEntity(pragmas, "jsxfrag");
            if (fragment is not null)
            {
                return fragment[0];
            }
            // (getJsxFragmentFactoryEntity: the jsxFragmentFactory option)
        }
        else
        {
            var local = PragmaFactoryEntity(pragmas, "jsx");
            if (local is not null)
            {
                return local[0];
            }
        }
        return "React";
    }

    // checker.ts getJsxFactoryEntity: the local jsx pragma's factory, else the default React.createElement
    private string[]? GetJsxFactoryEntity(NodeRef location)
    {
        var file = FileAt(location.File);
        return PragmaFactoryEntity(file.Store.Pragmas(file.Root), "jsx") ?? ["React", "createElement"];
    }

    // parseIsolatedEntityName of a pragma's factory argument: its identifiers (null if it is not an entity name)
    private string[]? PragmaFactoryEntity(PragmaMap? pragmas, string name)
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
        var parts = factory.Split('.');
        foreach (var part in parts)
        {
            if (!Scanner.IsIdentifierText(part, languageVersion))
            {
                return null;
            }
        }
        return Array.ConvertAll(parts, Utilities.EscapeLeadingUnderscores);
    }

    // sourceFileLinks.jsxImplicitImportContainer per file (unknownSymbol for false)
    private readonly Dictionary<int, SymbolRef> jsxImplicitImportContainers = [];

    // checker.ts getJsxNamespaceContainerForImplicitImport. The program's synthetic import specifier is not a node here
    // (getJSXRuntimeImportSpecifier): when the program synthesized the import, its resolution mode is the specifier's
    // (module C10); otherwise the location's, as in TypeScript.
    private SymbolRef GetJsxNamespaceContainerForImplicitImport(NodeRef location)
    {
        if (location.IsNone)
        {
            return SymbolRef.None;
        }
        if (jsxImplicitImportContainers.TryGetValue(location.File, out var cached))
        {
            return cached == unknownSymbol ? SymbolRef.None : cached;
        }
        var runtimeImportSpecifier = GetJSXRuntimeImport(GetJSXImplicitImportBase(location.File));
        if (runtimeImportSpecifier is null)
        {
            return SymbolRef.None;
        }
        // (moduleResolution classic: not used in Gate C)
        var synthetic = program.GetSyntheticJsxImport(FileAt(location.File));
        var modeOverride = synthetic is { } s && s.Text == runtimeImportSpecifier ? (true, s.Mode) : default;
        var mod = ResolveExternalModule(location, runtimeImportSpecifier,
            Diagnostics.This_JSX_tag_requires_the_module_path_0_to_exist_but_none_could_be_found_Make_sure_you_have_types_for_the_appropriate_package_installed, location, isForAugmentation: false, modeOverride);
        var result = !mod.IsNone && mod != unknownSymbol ? GetMergedSymbol(ResolveSymbol(mod)) : SymbolRef.None;
        jsxImplicitImportContainers[location.File] = result.IsNone ? unknownSymbol : result;
        return result;
    }

    // utilities.ts getJSXImplicitImportBase (the jsxImportSource option: not set in Gate C)
    private string? GetJSXImplicitImportBase(int fileNumber)
    {
        var file = FileAt(fileNumber);
        var pragmas = file.Store.Pragmas(file.Root);
        var jsxImportSourcePragma = LastPragmaFactory(pragmas, "jsximportsource", out var hasImportSource);
        var jsxRuntimeFactory = LastPragmaFactory(pragmas, "jsxruntime", out _);
        if (jsxRuntimeFactory == "classic")
        {
            return null;
        }
        return compilerOptions.Jsx is JsxEmit.ReactJSX or JsxEmit.ReactJSXDev || hasImportSource || jsxRuntimeFactory == "automatic"
            ? (!string.IsNullOrEmpty(jsxImportSourcePragma) ? jsxImportSourcePragma : "react")
            : null;
    }

    // utilities.ts getJSXRuntimeImport
    private string? GetJSXRuntimeImport(string? importBase) =>
        importBase is null ? null : importBase + "/" + (compilerOptions.Jsx == JsxEmit.ReactJSXDev ? "jsx-dev-runtime" : "jsx-runtime");

    // the factory argument of the last pragma of the given name
    internal static string? LastPragmaFactory(PragmaMap? pragmas, string name, out bool present)
    {
        present = false;
        if (pragmas is null || !pragmas.TryGetValue(name, out var value))
        {
            return null;
        }
        present = true;
        var entry = value is List<PragmaEntry> list ? list[^1] : (PragmaEntry)value;
        return entry.Arguments.Get("factory") switch
        {
            string s => s,
            PragmaArgumentSpan span => span.Value,
            _ => null,
        };
    }

    // checker.ts getJsxNamespaceAt
    private SymbolRef GetJsxNamespaceAt(NodeRef location)
    {
        if (!location.IsNone)
        {
            if (JsxElementLinks.Has(location) && JsxElementLinks.Get(location).JsxNamespaceState == 2)
            {
                return JsxElementLinks.Get(location).JsxNamespace;
            }
            if (!JsxElementLinks.Has(location) || JsxElementLinks.Get(location).JsxNamespaceState != 1)
            {
                var resolvedNamespace = GetJsxNamespaceContainerForImplicitImport(location);
                if (resolvedNamespace.IsNone || resolvedNamespace == unknownSymbol)
                {
                    var namespaceName = GetJsxNamespace(location);
                    resolvedNamespace = ResolveName(location, namespaceName, NodeRef.None, SymbolFlags.Namespace, null, isUse: false);
                }
                if (!resolvedNamespace.IsNone)
                {
                    var candidate = ResolveSymbol(GetSymbol(GetExportsOfSymbol(ResolveSymbol(resolvedNamespace)), JsxNamespaceName, SymbolFlags.Namespace));
                    if (!candidate.IsNone && candidate != unknownSymbol)
                    {
                        ref var links = ref JsxElementLinks.Get(location);
                        links.JsxNamespace = candidate;
                        links.JsxNamespaceState = 2;
                        return candidate;
                    }
                }
                JsxElementLinks.Get(location).JsxNamespaceState = 1;
            }
        }
        // JSX global fallback
        var s = ResolveSymbol(GetGlobalSymbol(JsxNamespaceName, SymbolFlags.Namespace, null));
        return s == unknownSymbol ? SymbolRef.None : s;
    }

    // checker.ts getJsxType: the declared type of a type of the JSX namespace (errorType if missing)
    private Type GetJsxType(string name, NodeRef location)
    {
        var ns = GetJsxNamespaceAt(location);
        var typeSymbol = ns.IsNone ? SymbolRef.None : GetSymbol(GetExportsOfSymbol(ns), name, SymbolFlags.Type);
        return typeSymbol.IsNone ? errorType : GetDeclaredTypeOfSymbol(typeSymbol);
    }

    // ---- grammar ----

    // checker.ts checkGrammarJsxElement
    private bool CheckGrammarJsxElement(NodeRef node)
    {
        var store = StoreOf(node);
        CheckGrammarJsxName(At(node, store.TagName(node.Node)));
        CheckGrammarTypeArguments(node, store.TypeArguments(node.Node));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attr in store.Nodes(store.Properties(store.Attributes(node.Node))))
        {
            if (store.Kind(attr) == SyntaxKind.JsxSpreadAttribute)
            {
                continue;
            }
            var name = store.Name(attr);
            var initializer = store.Initializer(attr);
            if (!seen.Add(GetTextOfJsxAttributeName(store, name)))
            {
                return GrammarErrorOnNode(At(node, name), Diagnostics.JSX_elements_cannot_have_multiple_attributes_with_the_same_name);
            }
            if (!initializer.IsNone && store.Kind(initializer) == SyntaxKind.JsxExpression && store.Expression(initializer).IsNone)
            {
                return GrammarErrorOnNode(At(node, initializer), Diagnostics.JSX_attributes_must_only_be_assigned_a_non_empty_expression);
            }
        }
        return false;
    }

    // checker.ts checkGrammarJsxName
    private bool CheckGrammarJsxName(NodeRef node)
    {
        var store = StoreOf(node);
        if (store.Kind(node.Node) == SyntaxKind.PropertyAccessExpression && store.Kind(store.Expression(node.Node)) == SyntaxKind.JsxNamespacedName)
        {
            return GrammarErrorOnNode(At(node, store.Expression(node.Node)), Diagnostics.JSX_property_access_expressions_cannot_include_JSX_namespace_names);
        }
        if (store.Kind(node.Node) == SyntaxKind.JsxNamespacedName && GetJSXTransformEnabled() &&
            !Utilities.IsIntrinsicJsxName(store.Texts.GetString(store.EscapedText(store.Namespace(node.Node)))!))
        {
            return GrammarErrorOnNode(node, Diagnostics.React_components_cannot_include_JSX_namespace_names);
        }
        return false;
    }

    // checker.ts checkGrammarJsxExpression
    private bool CheckGrammarJsxExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var expression = store.Expression(node.Node);
        if (!expression.IsNone && store.Kind(expression) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(expression)) == SyntaxKind.CommaToken)
        {
            return GrammarErrorOnNode(At(node, expression), Diagnostics.JSX_expressions_may_not_use_the_comma_operator_Did_you_mean_to_write_an_array);
        }
        return false;
    }
}
