// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (getTypeFromTypeNode and the type node kinds that need no instantiation:
// keywords, literal types, type references to classes, interfaces, aliases, enums and type parameters, unresolved
// references, array types, unions, intersections, parenthesized types, type literals and function types, type queries,
// this types; getAliasSymbolForTypeNode)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go (getTypeFromTypeNode)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C1. TODO(C3): tuples, optional and rest types, keyof, indexed
// access, mapped, conditional, infer, template literal and import types, generic alias instantiation and type
// argument defaults, deferred type references.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    /// <summary>checker.ts getTypeFromTypeNode (getConditionalFlowTypeOfType of the worker's type: module C4).</summary>
    internal Type GetTypeFromTypeNode(NodeRef node) =>
        node.IsNone ? errorType : GetConditionalFlowTypeOfType(GetTypeFromTypeNodeWorker(node), node);

    // checker.ts getTypeFromTypeNodeWorker
    private Type GetTypeFromTypeNodeWorker(NodeRef node)
    {
        if (node.IsNone)
        {
            return errorType;
        }
        var store = StoreOf(node);
        switch (store.Kind(node.Node))
        {
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.JSDocAllType:
            case SyntaxKind.JSDocUnknownType:
                return anyType;
            case SyntaxKind.UnknownKeyword:
                return unknownType;
            case SyntaxKind.StringKeyword:
                return stringType;
            case SyntaxKind.NumberKeyword:
                return numberType;
            case SyntaxKind.BigIntKeyword:
                return bigintType;
            case SyntaxKind.BooleanKeyword:
                return booleanType;
            case SyntaxKind.SymbolKeyword:
                return esSymbolType;
            case SyntaxKind.VoidKeyword:
                return voidType;
            case SyntaxKind.UndefinedKeyword:
                return undefinedType;
            case SyntaxKind.NullKeyword:
                return nullType;
            case SyntaxKind.NeverKeyword:
                return neverType;
            case SyntaxKind.ObjectKeyword:
                return (store.Flags(node.Node) & NodeFlags.JavaScriptFile) != 0 && !noImplicitAny ? anyType : nonPrimitiveType;
            case SyntaxKind.IntrinsicKeyword:
                return intrinsicMarkerType;
            case SyntaxKind.ThisType:
            case SyntaxKind.ThisKeyword:
                return GetTypeFromThisTypeNode(node);
            case SyntaxKind.LiteralType:
                return GetTypeFromLiteralTypeNode(node);
            case SyntaxKind.TypeReference:
            case SyntaxKind.ExpressionWithTypeArguments:
                return GetTypeFromTypeReference(node);
            case SyntaxKind.TypePredicate:
                return !store.AssertsModifier(node.Node).IsNone ? voidType : booleanType;
            case SyntaxKind.TypeQuery:
                return GetTypeFromTypeQueryNode(node);
            case SyntaxKind.ArrayType:
            case SyntaxKind.TupleType:
                return GetTypeFromArrayOrTupleTypeNode(node);
            case SyntaxKind.OptionalType:
                return GetTypeFromOptionalTypeNode(node);
            case SyntaxKind.UnionType:
                return GetTypeFromUnionTypeNode(node);
            case SyntaxKind.IntersectionType:
                return GetTypeFromIntersectionTypeNode(node);
            case SyntaxKind.NamedTupleMember:
                return GetTypeFromNamedTupleTypeNode(node);
            case SyntaxKind.ParenthesizedType:
            case SyntaxKind.JSDocNonNullableType:
            case SyntaxKind.JSDocTypeExpression:
                return GetTypeFromTypeNode(At(node, store.Type(node.Node)));
            case SyntaxKind.RestType:
                return GetTypeFromRestTypeNode(node);
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.TypeLiteral:
            case SyntaxKind.JSDocTypeLiteral:
            case SyntaxKind.JSDocFunctionType:
            case SyntaxKind.JSDocSignature:
                return GetTypeFromTypeLiteralOrFunctionOrConstructorTypeNode(node);
            case SyntaxKind.JSDocOptionalType:
                return AddOptionality(GetTypeFromTypeNode(At(node, store.Type(node.Node))), isProperty: false, isOptional: true);
            case SyntaxKind.JSDocVariadicType:
                return GetTypeFromJSDocVariadicType(node);
            case SyntaxKind.TypeOperator:
                return GetTypeFromTypeOperatorNode(node);
            case SyntaxKind.IndexedAccessType:
                return GetTypeFromIndexedAccessTypeNode(node);
            case SyntaxKind.MappedType:
                return GetTypeFromMappedTypeNode(node);
            case SyntaxKind.ConditionalType:
                return GetTypeFromConditionalTypeNode(node);
            case SyntaxKind.InferType:
                return GetTypeFromInferTypeNode(node);
            case SyntaxKind.TemplateLiteralType:
                return GetTypeFromTemplateTypeNode(node);
            case SyntaxKind.ImportType:
                return GetTypeFromImportTypeNode(node);
            case SyntaxKind.JSDocNullableType:
            {
                // getTypeFromJSDocNullableTypeNode (module C8: JSDoc type syntax in TypeScript code is reported by
                // checkJSDocTypeIsInJsFile with this type)
                var type = GetTypeFromTypeNode(At(node, store.Type(node.Node)));
                return strictNullChecks ? GetNullableType(type, TypeFlags.Null) : type;
            }
            case SyntaxKind.Identifier:
            case SyntaxKind.QualifiedName:
            case SyntaxKind.PropertyAccessExpression:
            {
                var symbol = GetSymbolAtLocation(node);
                return !symbol.IsNone ? GetDeclaredTypeOfSymbol(symbol) : errorType;
            }
            default:
            {
                // TODO(C5/C9): import types (module references), JSDoc types. C1 gives the type node of a type alias a
                // placeholder type that carries the alias (so references to the alias are written by name) and
                // errorType elsewhere.
                var cached = GetCachedNodeType(node);
                if (!cached.IsNone)
                {
                    return cached;
                }
                var aliasSymbol = GetAliasSymbolForTypeNode(node);
                if (aliasSymbol.IsNone)
                {
                    return errorType;
                }
                var placeholder = CreateObjectType(ObjectFlags.Anonymous, SymbolRef.None);
                Types.Record(placeholder).AliasSymbol = aliasSymbol;
                var placeholderAliasTypeArguments = GetTypeArgumentsForAliasSymbol(aliasSymbol);
                Types.Record(placeholder).AliasTypeArguments = placeholderAliasTypeArguments;
                unsupportedTypes.Add(placeholder);
                return SetCachedNodeType(node, placeholder);
            }
        }
    }

    // The placeholder types of type nodes C1 does not resolve (TODO(C3)): property accesses on them are not reported.
    private readonly HashSet<Type> unsupportedTypes = [];

    /// <summary>Whether a type stands for a type C1 cannot compute (a placeholder, or a reference to an alias of one).</summary>
    private bool IsUnsupportedType(Type type)
    {
        if (unsupportedTypes.Contains(type))
        {
            return true;
        }
        var alias = Types.Record(type).AliasSymbol;
        if (!alias.IsNone && (SymbolFlagsOf(alias) & SymbolFlags.TypeAlias) != 0)
        {
            return unsupportedTypes.Contains(GetDeclaredTypeOfSymbol(alias));
        }
        return false;
    }

    private Type GetCachedNodeType(NodeRef node) => TypeNodeLinks.Has(node) ? TypeNodeLinks.Get(node).ResolvedType : Type.None;

    private Type SetCachedNodeType(NodeRef node, Type type)
    {
        ref var links = ref TypeNodeLinks.Get(node);
        if (links.ResolvedType.IsNone)
        {
            links.ResolvedType = type;
        }
        return links.ResolvedType;
    }

    private Type GetTypeFromThisTypeNode(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        return !cached.IsNone ? cached : SetCachedNodeType(node, GetThisType(node));
    }

    // checker.ts getThisType (JavaScript prototype and constructor-function forms: C9)
    private Type GetThisType(NodeRef node)
    {
        var store = StoreOf(node);
        var container = Utilities.GetThisContainer(store, node.Node, includeArrowFunctions: false, includeClassComputedPropertyName: false);
        var parent = container.IsNone ? Node.None : store.Parent(container);
        if (!parent.IsNone && (Utilities.IsClassLike(store, parent) || store.Kind(parent) == SyntaxKind.InterfaceDeclaration))
        {
            if (!Utilities.IsStatic(store, container) &&
                (store.Kind(container) != SyntaxKind.Constructor || IsNodeDescendantOf(store, node.Node, store.Body(container))))
            {
                return ThisTypeOfClassOrInterface(GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration(At(node, parent))));
            }
        }
        // inside x.prototype = { ... }
        if (!parent.IsNone && store.Kind(parent) == SyntaxKind.ObjectLiteralExpression && store.Kind(store.Parent(parent)) == SyntaxKind.BinaryExpression &&
            Utilities.GetAssignmentDeclarationKind(store, store.Parent(parent)) == AssignmentDeclarationKind.Prototype)
        {
            return ThisTypeOfClassOrInterface(GetDeclaredTypeOfClassOrInterface(ParentOf(GetSymbolOfNode(At(node, store.Left(store.Parent(parent)))))));
        }
        // /** @return {this} */
        // x.prototype.m = function() { ... }
        var host = (store.Flags(node.Node) & NodeFlags.JSDoc) != 0 ? Utilities.GetHostSignatureFromJSDoc(store, node.Node) : Node.None;
        if (!host.IsNone && store.Kind(host) == SyntaxKind.FunctionExpression && store.Kind(store.Parent(host)) == SyntaxKind.BinaryExpression &&
            Utilities.GetAssignmentDeclarationKind(store, store.Parent(host)) == AssignmentDeclarationKind.PrototypeProperty)
        {
            return ThisTypeOfClassOrInterface(GetDeclaredTypeOfClassOrInterface(ParentOf(GetSymbolOfNode(At(node, store.Left(store.Parent(host)))))));
        }
        // inside constructor function C() { ... }
        if (!container.IsNone && IsJSConstructor(At(node, container)) && IsNodeDescendantOf(store, node.Node, store.Body(container)))
        {
            return ThisTypeOfClassOrInterface(GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration(At(node, container))));
        }
        Error(node, Diagnostics.A_this_type_is_available_only_in_a_non_static_member_of_a_class_or_interface);
        return errorType;
    }

    private static bool IsNodeDescendantOf(NodeStore store, Node node, Node ancestor)
    {
        if (ancestor.IsNone)
        {
            return false;
        }
        for (var n = node; !n.IsNone; n = store.Parent(n))
        {
            if (n == ancestor)
            {
                return true;
            }
        }
        return false;
    }

    private Type GetTypeFromLiteralTypeNode(NodeRef node)
    {
        var store = StoreOf(node);
        var literal = store.Literal(node.Node);
        if (store.Kind(literal) == SyntaxKind.NullKeyword)
        {
            return nullType;
        }
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        return SetCachedNodeType(node, GetRegularTypeOfLiteralType(CheckExpression(At(node, literal))));
    }

    private Type GetTypeFromTypeReference(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        var store = StoreOf(node);
        // handle LS queries on the `const` in `x as const` by resolving to the type of `x`
        var parent = store.Parent(node.Node);
        if (store.Kind(node.Node) == SyntaxKind.TypeReference && store.Kind(store.TypeName(node.Node)) == SyntaxKind.Identifier &&
            store.Texts.Equals(store.EscapedText(store.TypeName(node.Node)), "const") && store.TypeArguments(node.Node).IsNone &&
            store.Kind(parent) is SyntaxKind.AsExpression or SyntaxKind.TypeAssertionExpression)
        {
            SymbolNodeLinks.Get(node).ResolvedSymbol = unknownSymbol;
            return SetCachedNodeType(node, CheckExpressionCached(At(node, store.Expression(parent))));
        }
        var symbol = SymbolRef.None;
        var type = Type.None;
        if (IsJSDocTypeReference(node))
        {
            type = GetIntendedTypeFromJSDocTypeReference(node);
            if (type.IsNone)
            {
                symbol = ResolveTypeReferenceName(node, SymbolFlags.Type, ignoreErrors: true);
                if (symbol == unknownSymbol)
                {
                    symbol = ResolveTypeReferenceName(node, SymbolFlags.Type | SymbolFlags.Value);
                }
                else
                {
                    ResolveTypeReferenceName(node, SymbolFlags.Type); // Resolve again to mark errors, if any
                }
                type = GetTypeReferenceType(node, symbol);
            }
        }
        if (type.IsNone)
        {
            symbol = ResolveTypeReferenceName(node, SymbolFlags.Type);
            type = GetTypeReferenceType(node, symbol);
        }
        // Cache both the resolved symbol and the resolved type. The resolved symbol is needed when we check the
        // type reference in checkTypeReferenceNode.
        SymbolNodeLinks.Get(node).ResolvedSymbol = symbol;
        return SetCachedNodeType(node, type);
    }

    // checker.ts getTypeReferenceName
    private NodeRef GetTypeReferenceName(NodeRef node)
    {
        var store = StoreOf(node);
        switch (store.Kind(node.Node))
        {
            case SyntaxKind.TypeReference:
                return At(node, store.TypeName(node.Node));
            case SyntaxKind.ExpressionWithTypeArguments:
            {
                // We only support expressions that are simple qualified names. For other
                // expressions this produces undefined.
                var expr = store.Expression(node.Node);
                if (Utilities.IsEntityNameExpression(store, expr))
                {
                    return At(node, expr);
                }
                break;
            }
        }
        return NodeRef.None;
    }

    /// <summary>checker.ts resolveTypeReferenceName.</summary>
    internal SymbolRef ResolveTypeReferenceName(NodeRef typeReference, SymbolFlags meaning, bool ignoreErrors = false)
    {
        var name = GetTypeReferenceName(typeReference);
        if (name.IsNone)
        {
            return unknownSymbol;
        }
        var symbol = ResolveEntityName(name, meaning, ignoreErrors);
        return !symbol.IsNone && symbol != unknownSymbol ? symbol
            : ignoreErrors ? unknownSymbol
            : GetUnresolvedSymbolForEntityName(name);
    }

    // checker.ts getUnresolvedSymbolForEntityName
    private SymbolRef GetUnresolvedSymbolForEntityName(NodeRef name)
    {
        var store = StoreOf(name);
        var kind = store.Kind(name.Node);
        var identifier = kind == SyntaxKind.QualifiedName ? store.Right(name.Node)
            : kind == SyntaxKind.PropertyAccessExpression ? store.Name(name.Node)
            : name.Node;
        var text = store.Texts.GetString(store.EscapedText(identifier));
        if (!string.IsNullOrEmpty(text))
        {
            var parentSymbol = kind == SyntaxKind.QualifiedName ? GetUnresolvedSymbolForEntityName(At(name, store.Left(name.Node)))
                : kind == SyntaxKind.PropertyAccessExpression ? GetUnresolvedSymbolForEntityName(At(name, store.Expression(name.Node)))
                : SymbolRef.None;
            var path = !parentSymbol.IsNone ? GetSymbolPath(parentSymbol) + "." + text : text;
            if (!unresolvedSymbols.TryGetValue(path, out var result))
            {
                unresolvedSymbols[path] = result = CreateSymbol(SymbolFlags.TypeAlias, text, CheckFlags.Unresolved);
                TransientSymbols.Record(result).Parent = parentSymbol;
                DeclaredTypeLinks.Get(result).DeclaredType = unresolvedType;
            }
            return result;
        }
        return unknownSymbol;
    }

    private string GetSymbolPath(SymbolRef symbol)
    {
        var parent = ParentOf(symbol);
        return !parent.IsNone ? GetSymbolPath(parent) + "." + NameOf(symbol) : NameOf(symbol);
    }

    // checker.ts getTypeReferenceType
    private Type GetTypeReferenceType(NodeRef node, SymbolRef symbol)
    {
        if (symbol == unknownSymbol)
        {
            return errorType;
        }
        var expandoSymbol = GetExpandoSymbol(symbol);
        if (!expandoSymbol.IsNone)
        {
            symbol = expandoSymbol;
        }
        var flags = SymbolFlagsOf(symbol);
        if ((flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
        {
            return GetTypeFromClassOrInterfaceReference(node, symbol);
        }
        if ((flags & SymbolFlags.TypeAlias) != 0)
        {
            return GetTypeFromTypeAliasReference(node, symbol);
        }
        // Get type from reference to named type that cannot be generic (enum or type parameter)
        var res = TryGetDeclaredTypeOfSymbol(symbol);
        if (!res.IsNone)
        {
            return CheckNoTypeArguments(node, symbol) ? GetRegularTypeOfLiteralType(res) : errorType;
        }
        if ((flags & SymbolFlags.Value) != 0 && IsJSDocTypeReference(node))
        {
            var jsdocType = GetTypeFromJSDocValueReference(node, symbol);
            if (!jsdocType.IsNone)
            {
                return jsdocType;
            }
            // Resolve the type reference as a Type for the purpose of reporting errors.
            ResolveTypeReferenceName(node, SymbolFlags.Type);
            return GetTypeOfSymbol(symbol);
        }
        return errorType;
    }

    // checker.ts isJSDocTypeReference
    private bool IsJSDocTypeReference(NodeRef node)
    {
        var store = StoreOf(node);
        return (store.Flags(node.Node) & NodeFlags.JSDoc) != 0 && store.Kind(node.Node) is SyntaxKind.TypeReference or SyntaxKind.ImportType;
    }

    // checker.ts getTypeFromJSDocValueReference (links.resolvedJSDocType)
    private readonly Dictionary<NodeRef, Type> resolvedJSDocTypes = [];

    private Type GetTypeFromJSDocValueReference(NodeRef node, SymbolRef symbol)
    {
        if (resolvedJSDocTypes.TryGetValue(node, out var cached))
        {
            return cached;
        }
        var valueType = GetTypeOfSymbol(symbol);
        var typeType = valueType;
        if (!ValueDeclarationOf(symbol).IsNone)
        {
            var store = StoreOf(node);
            var isImportTypeWithQualifier = store.Kind(node.Node) == SyntaxKind.ImportType && !store.Qualifier(node.Node).IsNone;
            // valueType might not have a symbol, eg, {import('./b').STRING_LITERAL}
            var valueSymbol = Types.Symbol(valueType);
            if (!valueSymbol.IsNone && valueSymbol != symbol && isImportTypeWithQualifier)
            {
                typeType = GetTypeReferenceType(node, valueSymbol);
            }
        }
        resolvedJSDocTypes[node] = typeType;
        return typeType;
    }

    // checker.ts getExpandoSymbol
    private SymbolRef GetExpandoSymbol(SymbolRef symbol)
    {
        var decl = ValueDeclarationOf(symbol);
        if (decl.IsNone)
        {
            return SymbolRef.None;
        }
        var store = StoreOf(decl);
        if (!Utilities.IsInJSFile(store, decl.Node) || (SymbolFlagsOf(symbol) & SymbolFlags.TypeAlias) != 0 || !Utilities.GetExpandoInitializer(store, decl.Node, isPrototypeAssignment: false).IsNone)
        {
            return SymbolRef.None;
        }
        var init = store.Kind(decl.Node) == SyntaxKind.VariableDeclaration ? GetDeclaredExpandoInitializer(decl)
            : Utilities.GetAssignedExpandoInitializer(store, decl.Node) is var assigned && !assigned.IsNone ? At(decl, assigned) : NodeRef.None;
        if (!init.IsNone)
        {
            var initSymbol = GetSymbolOfNode(init);
            if (!initSymbol.IsNone)
            {
                return MergeJSSymbols(initSymbol, symbol);
            }
        }
        return SymbolRef.None;
    }

    // checker.ts getIntendedTypeFromJSDocTypeReference
    private Type GetIntendedTypeFromJSDocTypeReference(NodeRef node)
    {
        var store = StoreOf(node);
        var typeName = store.TypeName(node.Node);
        if (store.Kind(typeName) != SyntaxKind.Identifier)
        {
            return Type.None;
        }
        var typeArgs = store.TypeArguments(node.Node);
        var typeArgCount = typeArgs.IsNone ? 0 : store.Length(typeArgs);
        switch (store.Texts.GetString(store.EscapedText(typeName)))
        {
            case "String":
                CheckNoTypeArguments(node, SymbolRef.None);
                return stringType;
            case "Number":
                CheckNoTypeArguments(node, SymbolRef.None);
                return numberType;
            case "BigInt":
                CheckNoTypeArguments(node, SymbolRef.None);
                return bigintType;
            case "Boolean":
                CheckNoTypeArguments(node, SymbolRef.None);
                return booleanType;
            case "Void":
                CheckNoTypeArguments(node, SymbolRef.None);
                return voidType;
            case "Undefined":
                CheckNoTypeArguments(node, SymbolRef.None);
                return undefinedType;
            case "Null":
                CheckNoTypeArguments(node, SymbolRef.None);
                return nullType;
            case "Function":
            case "function":
                CheckNoTypeArguments(node, SymbolRef.None);
                return globalFunctionType;
            case "array":
                return typeArgCount == 0 && !noImplicitAny ? anyArrayType : Type.None;
            case "promise":
                return typeArgCount == 0 && !noImplicitAny ? CreatePromiseType(anyType) : Type.None;
            case "Object":
                if (typeArgCount == 2)
                {
                    if (IsJSDocIndexSignature(node))
                    {
                        var indexed = GetTypeFromTypeNode(At(node, store.Element(typeArgs, 0)));
                        var target = GetTypeFromTypeNode(At(node, store.Element(typeArgs, 1)));
                        Items<IndexInfo> indexInfo = indexed == stringType || indexed == numberType ? [new IndexInfo { KeyType = indexed, Type = target, IsReadonly = false }] : default;
                        return CreateAnonymousType(SymbolRef.None, emptySymbols, default, default, indexInfo);
                    }
                    return anyType;
                }
                CheckNoTypeArguments(node, SymbolRef.None);
                return !noImplicitAny ? anyType : Type.None;
        }
        return Type.None;
    }

    private NodeList TypeArgumentsOf(NodeRef node)
    {
        var store = StoreOf(node);
        return store.Kind(node.Node) is SyntaxKind.TypeReference or SyntaxKind.ExpressionWithTypeArguments or SyntaxKind.ImportType or SyntaxKind.TypeQuery ? store.TypeArguments(node.Node) : NodeList.None;
    }

    private Type[] TypeArgumentsFromTypeReferenceNode(NodeRef node)
    {
        var store = StoreOf(node);
        var typeArguments = TypeArgumentsOf(node);
        if (typeArguments.IsNone)
        {
            return [];
        }
        var result = new Type[store.Length(typeArguments)];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = GetTypeFromTypeNode(At(node, store.Element(typeArguments, i)));
        }
        return result;
    }

    // checker.ts getTypeFromClassOrInterfaceReference (TODO(C3): type parameter defaults, fillMissingTypeArguments)
    private Type GetTypeFromClassOrInterfaceReference(NodeRef node, SymbolRef symbol)
    {
        var type = GetDeclaredTypeOfSymbol(GetMergedSymbol(symbol));
        if ((Types.Flags(type) & TypeFlags.Object) == 0)
        {
            return type;
        }
        var data = Types.Object(type);
        var typeParameters = data.LocalTypeParameters;
        if (typeParameters.Count > 0)
        {
            var store = StoreOf(node);
            var typeArgumentNodes = TypeArgumentsOf(node);
            var numTypeArguments = typeArgumentNodes.IsNone ? 0 : store.Length(typeArgumentNodes);
            var minTypeArgumentCount = GetMinTypeArgumentCount(typeParameters);
            var isJs = Utilities.IsInJSFile(store, node.Node);
            var isJsImplicitAny = !noImplicitAny && isJs;
            if (!isJsImplicitAny && (numTypeArguments < minTypeArgumentCount || numTypeArguments > typeParameters.Count))
            {
                var missingAugmentsTag = isJs && store.Kind(node.Node) == SyntaxKind.ExpressionWithTypeArguments && store.Kind(store.Parent(node.Node)) != SyntaxKind.JSDocAugmentsTag;
                var diag = minTypeArgumentCount == typeParameters.Count
                    ? missingAugmentsTag ? Diagnostics.Expected_0_type_arguments_provide_these_with_an_extends_tag : Diagnostics.Generic_type_0_requires_1_type_argument_s
                    : missingAugmentsTag ? Diagnostics.Expected_0_1_type_arguments_provide_these_with_an_extends_tag : Diagnostics.Generic_type_0_requires_between_1_and_2_type_arguments;
                var typeStr = TypeToString(type, NodeRef.None, TypeFormatFlags.WriteArrayAsGenericType);
                Error(node, diag, typeStr, minTypeArgumentCount, typeParameters.Count);
                if (!isJs)
                {
                    // TODO: Adopt same permissive behavior in TS as in JS to reduce follow-on editing experience failures (requires editing fillMissingTypeArguments)
                    return errorType;
                }
            }
            if (store.Kind(node.Node) == SyntaxKind.TypeReference && IsDeferredTypeReferenceNode(node, numTypeArguments != typeParameters.Count))
            {
                return CreateDeferredTypeReference(type, node, null);
            }
            // In a type reference, the outer type parameters of the referenced class or interface are automatically
            // supplied as type arguments and the type reference only specifies arguments for the local type parameters
            // of the class or interface.
            var typeArguments = new List<Type>(Types.Elements(data.OuterTypeParameters).ToArray());
            typeArguments.AddRange(FillMissingTypeArguments(TypeArgumentsFromTypeReferenceNode(node), Types.Elements(typeParameters).ToArray(), minTypeArgumentCount, isJs));
            return CreateTypeReference(type, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(typeArguments));
        }
        return CheckNoTypeArguments(node, symbol) ? type : errorType;
    }

    // checker.ts getMinTypeArgumentCount
    private int GetMinTypeArgumentCount(TypeList typeParameters) => GetMinTypeArgumentCount(Types.Elements(typeParameters).ToArray());

    private bool HasTypeParameterDefault(Type typeParameter)
    {
        foreach (var declaration in DeclarationsOf(Types.Symbol(typeParameter)) ?? [])
        {
            if (Kind(declaration) == SyntaxKind.TypeParameter && !StoreOf(declaration).Default(declaration.Node).IsNone)
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts checkNoTypeArguments
    private bool CheckNoTypeArguments(NodeRef node, SymbolRef symbol)
    {
        var typeArguments = TypeArgumentsOf(node);
        var store = StoreOf(node);
        // (node.typeArguments: an empty list `T<>` counts)
        if (!typeArguments.IsNone)
        {
            Error(node, Diagnostics.Type_0_is_not_generic, !symbol.IsNone ? SymbolToString(symbol) : Utilities.DeclarationNameToString(store, store.TypeName(node.Node)));
            return false;
        }
        return true;
    }

    // checker.ts getTypeFromTypeAliasReference
    private Type GetTypeFromTypeAliasReference(NodeRef node, SymbolRef symbol)
    {
        if ((TransientCheckFlags(symbol) & CheckFlags.Unresolved) != 0)
        {
            var typeArguments = TypeArgumentsFromTypeReferenceNode(node);
            var typeArgumentList = Types.CreateList(typeArguments);
            var id = GetAliasId(symbol, typeArgumentList, Types);
            if (!errorTypes.TryGetValue(id, out var unresolved))
            {
                unresolved = Types.CreateIntrinsic(TypeFlags.Any, "error", ObjectFlags.CouldContainTypeVariablesComputed | ObjectFlags.IsGenericTypeComputed | ObjectFlags.IsUnknownLikeUnionComputed | ObjectFlags.IsNeverIntersectionComputed, "alias " + id);
                ref var record = ref Types.Record(unresolved);
                record.AliasSymbol = symbol;
                record.AliasTypeArguments = typeArgumentList;
                errorTypes[id] = unresolved;
            }
            return unresolved;
        }
        var type = GetDeclaredTypeOfSymbol(symbol);
        var typeParameters = aliasInstantiations.TryGetValue(symbol, out var entry) ? entry : null;
        if (typeParameters is not null)
        {
            var store = StoreOf(node);
            var typeArgumentNodes = TypeArgumentsOf(node);
            var numTypeArguments = typeArgumentNodes.IsNone ? 0 : store.Length(typeArgumentNodes);
            var minTypeArgumentCount = GetMinTypeArgumentCount(typeParameters);
            if (numTypeArguments < minTypeArgumentCount || numTypeArguments > typeParameters.Length)
            {
                Error(node, minTypeArgumentCount == typeParameters.Length ? Diagnostics.Generic_type_0_requires_1_type_argument_s : Diagnostics.Generic_type_0_requires_between_1_and_2_type_arguments,
                    SymbolToString(symbol), minTypeArgumentCount, typeParameters.Length);
                return errorType;
            }
            // We refrain from associating a local type alias with an instantiation of a top-level type alias
            // because the local alias may end up being referenced in an inferred return type where it is not
            // accessible--which in turn may lead to a large structural expansion of the type when generating
            // a .d.ts file. See #43622 for an example.
            var aliasSymbol = GetAliasSymbolForTypeNode(node);
            var newAliasSymbol = !aliasSymbol.IsNone && (IsLocalTypeAlias(symbol) || !IsLocalTypeAlias(aliasSymbol)) ? aliasSymbol : SymbolRef.None;
            TypeList aliasTypeArguments = default;
            if (!newAliasSymbol.IsNone)
            {
                aliasTypeArguments = GetTypeArgumentsForAliasSymbol(newAliasSymbol);
            }
            else if (store.Kind(node.Node) is SyntaxKind.TypeReference or SyntaxKind.ExpressionWithTypeArguments)
            {
                var importAlias = ResolveTypeReferenceName(node, SymbolFlags.Alias, ignoreErrors: true);
                // refers to an alias import/export/reexport - by making sure we use the target as an aliasSymbol,
                // we ensure the exported symbol is used to refer to the type when it's reserialized later
                if (!importAlias.IsNone && importAlias != unknownSymbol)
                {
                    var resolved = ResolveAlias(importAlias);
                    if (!resolved.IsNone && (SymbolFlagsOf(resolved) & SymbolFlags.TypeAlias) != 0)
                    {
                        newAliasSymbol = resolved;
                        // typeArgumentsFromTypeReferenceNode(node) || (typeParameters ? [] : undefined)
                        var importedAliasArguments = TypeArgumentsFromTypeReferenceNode(node);
                        aliasTypeArguments = importedAliasArguments.Length > 0 ? Types.CreateList(importedAliasArguments) : TypeList.PresentEmpty;
                    }
                }
            }
            return GetTypeAliasInstantiation(symbol, TypeArgumentsFromTypeReferenceNode(node), newAliasSymbol, aliasTypeArguments);
        }
        return CheckNoTypeArguments(node, symbol) ? type : errorType;
    }

    private Type GetTypeFromTypeQueryNode(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        // TypeScript 1.0 spec (April 2014): 3.6.3
        // The expression is processed as an identifier expression (section 4.3)
        // or property access expression(section 4.10),
        // the widened type(section 3.9) of which becomes the result.
        var type = CheckExpressionWithTypeArguments(node);
        return SetCachedNodeType(node, GetRegularTypeOfLiteralType(GetWidenedType(type)));
    }

    // (checkExpressionWithTypeArguments: Checker.Expressions.cs, module C5)

    // checker.ts isDeferredTypeReferenceNode: directly aliased, or possibly in a circular chain of eagerly resolved types
    private bool IsDeferredTypeReferenceNode(NodeRef node, bool hasDefaultTypeArguments)
    {
        if (!GetAliasSymbolForTypeNode(node).IsNone)
        {
            return true;
        }
        var store = StoreOf(node);
        if (!IsResolvedByTypeAlias(store, node.Node))
        {
            return false;
        }
        switch (store.Kind(node.Node))
        {
            case SyntaxKind.ArrayType:
                return MayResolveTypeAlias(At(node, store.ElementType(node.Node)));
            case SyntaxKind.TupleType:
                foreach (var element in store.Nodes(store.Elements(node.Node)))
                {
                    if (MayResolveTypeAlias(At(node, element)))
                    {
                        return true;
                    }
                }
                return false;
            default:
            {
                if (hasDefaultTypeArguments)
                {
                    return true;
                }
                var typeArguments = TypeArgumentsOf(node);
                if (!typeArguments.IsNone)
                {
                    foreach (var argument in store.Nodes(typeArguments))
                    {
                        if (MayResolveTypeAlias(At(node, argument)))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }
    }

    // checker.ts isResolvedByTypeAlias
    private static bool IsResolvedByTypeAlias(NodeStore store, Node node)
    {
        while (true)
        {
            var parent = store.Parent(node);
            switch (store.Kind(parent))
            {
                case SyntaxKind.ParenthesizedType:
                case SyntaxKind.NamedTupleMember:
                case SyntaxKind.TypeReference:
                case SyntaxKind.UnionType:
                case SyntaxKind.IntersectionType:
                case SyntaxKind.IndexedAccessType:
                case SyntaxKind.ConditionalType:
                case SyntaxKind.TypeOperator:
                case SyntaxKind.ArrayType:
                case SyntaxKind.TupleType:
                    node = parent;
                    continue;
                case SyntaxKind.TypeAliasDeclaration:
                    return true;
                default:
                    return false;
            }
        }
    }

    // checker.ts mayResolveTypeAlias
    private bool MayResolveTypeAlias(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.TypeReference:
                return (SymbolFlagsOf(ResolveTypeReferenceName(node, SymbolFlags.Type)) & SymbolFlags.TypeAlias) != 0;
            case SyntaxKind.TypeQuery:
                return true;
            case SyntaxKind.TypeOperator:
                return store.Operator(n) != SyntaxKind.UniqueKeyword && MayResolveTypeAlias(At(node, store.Type(n)));
            case SyntaxKind.ParenthesizedType:
            case SyntaxKind.OptionalType:
            case SyntaxKind.NamedTupleMember:
            case SyntaxKind.JSDocOptionalType:
            case SyntaxKind.JSDocNullableType:
            case SyntaxKind.JSDocNonNullableType:
            case SyntaxKind.JSDocTypeExpression:
                return MayResolveTypeAlias(At(node, store.Type(n)));
            case SyntaxKind.RestType:
            {
                var type = store.Type(n);
                return store.Kind(type) != SyntaxKind.ArrayType || MayResolveTypeAlias(At(node, store.ElementType(type)));
            }
            case SyntaxKind.UnionType:
            case SyntaxKind.IntersectionType:
                foreach (var t in store.Nodes(store.Types(n)))
                {
                    if (MayResolveTypeAlias(At(node, t)))
                    {
                        return true;
                    }
                }
                return false;
            case SyntaxKind.IndexedAccessType:
                return MayResolveTypeAlias(At(node, store.ObjectType(n))) || MayResolveTypeAlias(At(node, store.IndexType(n)));
            case SyntaxKind.ConditionalType:
                return MayResolveTypeAlias(At(node, store.CheckType(n))) || MayResolveTypeAlias(At(node, store.ExtendsType(n))) ||
                    MayResolveTypeAlias(At(node, store.TrueType(n))) || MayResolveTypeAlias(At(node, store.FalseType(n)));
        }
        return false;
    }

    private Type GetTypeFromUnionTypeNode(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        var store = StoreOf(node);
        var aliasSymbol = GetAliasSymbolForTypeNode(node);
        var types = new List<Type>();
        foreach (var t in store.Nodes(store.Types(node.Node)))
        {
            types.Add(GetTypeFromTypeNode(At(node, t)));
        }
        return SetCachedNodeType(node, GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(types), UnionReduction.Literal, aliasSymbol, GetTypeArgumentsForAliasSymbol(aliasSymbol)));
    }

    private Type GetTypeFromIntersectionTypeNode(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        var store = StoreOf(node);
        var aliasSymbol = GetAliasSymbolForTypeNode(node);
        var types = new List<Type>();
        foreach (var t in store.Nodes(store.Types(node.Node)))
        {
            types.Add(GetTypeFromTypeNode(At(node, t)));
        }
        // We perform no supertype reduction for X & {} or {} & X, where X is one of string, number, bigint,
        // or a pattern literal template type. This enables union types like "a" | "b" | string & {} or
        // "aa" | "ab" | `a${string}` which preserve the literal types for purposes of statement completion.
        var emptyIndex = types.Count == 2 ? types.IndexOf(emptyTypeLiteralType) : -1;
        var other = emptyIndex >= 0 ? types[1 - emptyIndex] : unknownType;
        var noSupertypeReduction = (Types.Flags(other) & (TypeFlags.String | TypeFlags.Number | TypeFlags.BigInt)) != 0 ||
            ((Types.Flags(other) & TypeFlags.TemplateLiteral) != 0 && IsPatternLiteralType(other));
        return SetCachedNodeType(node, GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(types), aliasSymbol, GetTypeArgumentsForAliasSymbol(aliasSymbol),
            noSupertypeReduction ? IntersectionFlags.NoSupertypeReduction : IntersectionFlags.None));
    }

    private Type GetTypeFromTypeLiteralOrFunctionOrConstructorTypeNode(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        // Deferred resolution of members is handled by resolveObjectTypeMembers
        var aliasSymbol = GetAliasSymbolForTypeNode(node);
        var symbol = NodeSymbol(node);
        Type type;
        if (symbol.IsNone || (GetMembersOfSymbol(symbol).Count == 0 && aliasSymbol.IsNone))
        {
            type = emptyTypeLiteralType;
        }
        else
        {
            type = CreateObjectType(ObjectFlags.Anonymous, symbol);
            Types.Record(type).AliasSymbol = aliasSymbol;
            // (computed before the record is written: the call can grow the type store)
            var aliasTypeArguments = GetTypeArgumentsForAliasSymbol(aliasSymbol);
            Types.Record(type).AliasTypeArguments = aliasTypeArguments;
            var store = StoreOf(node);
            if (store.Kind(node.Node) == SyntaxKind.JSDocTypeLiteral && store.IsArrayType(node.Node))
            {
                type = CreateArrayType(type);
            }
        }
        return SetCachedNodeType(node, type);
    }

    // checker.ts getAliasSymbolForTypeNode
    private SymbolRef GetAliasSymbolForTypeNode(NodeRef node)
    {
        var store = StoreOf(node);
        var host = store.Parent(node.Node);
        while (store.Kind(host) is SyntaxKind.ParenthesizedType or SyntaxKind.JSDocTypeExpression ||
            (store.Kind(host) == SyntaxKind.TypeOperator && store.Operator(host) == SyntaxKind.ReadonlyKeyword))
        {
            host = store.Parent(host);
        }
        return store.Kind(host) is SyntaxKind.TypeAliasDeclaration or SyntaxKind.JSDocTypedefTag or SyntaxKind.JSDocCallbackTag or SyntaxKind.JSDocEnumTag
            ? GetSymbolOfDeclaration(At(node, host))
            : SymbolRef.None;
    }

    // checker.ts getTypeArgumentsForAliasSymbol
    private TypeList GetTypeArgumentsForAliasSymbol(SymbolRef symbol)
    {
        if (symbol.IsNone)
        {
            return TypeList.Empty;
        }
        var typeParameters = GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol);
        return typeParameters is null ? TypeList.Empty : Types.CreateList(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(typeParameters));
    }
}
