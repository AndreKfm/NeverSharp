// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (tuple types, index types (keyof), unique symbol, template literal, string
// mapping, indexed access, substitution and NoInfer types, getSimplifiedType, constraints and base constraints,
// getApparentType, getReducedType, the generic object flags and the type nodes of these kinds)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C3 (the relations and the never-reduction of intersections in
// getReducedType: module C4). Stubs: element access expressions (C5).

using System.Globalization;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private Type numericStringType;

    // ---- tuples ----

    private readonly Dictionary<string, Type> tupleTypes = new(StringComparer.Ordinal);

    /// <summary>checker.ts isTupleType.</summary>
    internal bool IsTupleType(Type type) =>
        (Types.ObjectFlagsOf(type) & ObjectFlags.Reference) != 0 && !Types.Object(type).Target.IsNone &&
        (Types.ObjectFlagsOf(Types.Object(type).Target) & ObjectFlags.Tuple) != 0;

    /// <summary>checker.ts isGenericTupleType.</summary>
    internal bool IsGenericTupleType(Type type) =>
        IsTupleType(type) && (Types.Object(Types.Object(type).Target).CombinedFlags & ElementFlags.Variadic) != 0;

    /// <summary>checker.ts isArrayType.</summary>
    internal bool IsArrayType(Type type) =>
        (Types.ObjectFlagsOf(type) & ObjectFlags.Reference) != 0 && !Types.Object(type).Target.IsNone &&
        (Types.Object(type).Target == globalArrayType || Types.Object(type).Target == globalReadonlyArrayType);

    /// <summary>checker.ts isReadonlyArrayType.</summary>
    internal bool IsReadonlyArrayType(Type type) =>
        (Types.ObjectFlagsOf(type) & ObjectFlags.Reference) != 0 && Types.Object(type).Target == globalReadonlyArrayType;

    internal bool IsArrayOrTupleType(Type type) => IsArrayType(type) || IsTupleType(type);

    /// <summary>checker.ts getElementTypeOfArrayType (None when not an array type).</summary>
    internal Type GetElementTypeOfArrayType(Type type) => IsArrayType(type) ? GetTypeArguments(type)[0] : Type.None;

    /// <summary>checker.ts getTypeReferenceArity.</summary>
    internal int GetTypeReferenceArity(Type type) => Types.Object(Types.Object(type).Target).TypeParameters.Count;

    // checker.ts getTupleElementLabel (named tuple members and parameters)
    private string GetTupleElementLabel(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var name = store.Name(declaration.Node);
        if (!name.IsNone && store.Kind(name) == SyntaxKind.Identifier)
        {
            return store.Texts.GetString(store.EscapedText(name)) ?? "";
        }
        return "arg";
    }

    // checker.ts removeMissingType
    private Type RemoveMissingType(Type type, bool isOptional) =>
        exactOptionalPropertyTypes && isOptional ? FilterType(type, t => t != missingType) : type;

    /// <summary>checker.ts getElementTypes.</summary>
    internal Type[] GetElementTypes(Type type)
    {
        var typeArguments = GetTypeArguments(type);
        var arity = GetTypeReferenceArity(type);
        return typeArguments.Length == arity ? typeArguments.ToArray() : typeArguments[..arity].ToArray();
    }

    /// <summary>The tuple target of a tuple type reference (TupleTypeReference.target).</summary>
    internal ObjectTypeData TupleTarget(Type type) => Types.Object(Types.Object(type).Target);

    // checker.ts getTupleElementFlags
    private ElementFlags GetTupleElementFlags(NodeRef node)
    {
        var store = StoreOf(node);
        switch (store.Kind(node.Node))
        {
            case SyntaxKind.OptionalType:
                return ElementFlags.Optional;
            case SyntaxKind.RestType:
                return GetRestTypeElementFlags(node);
            case SyntaxKind.NamedTupleMember:
                return !store.QuestionToken(node.Node).IsNone ? ElementFlags.Optional
                    : !store.DotDotDotToken(node.Node).IsNone ? GetRestTypeElementFlags(node)
                    : ElementFlags.Required;
            default:
                return ElementFlags.Required;
        }
    }

    private ElementFlags GetRestTypeElementFlags(NodeRef node) =>
        !GetArrayElementTypeNode(At(node, StoreOf(node).Type(node.Node))).IsNone ? ElementFlags.Rest : ElementFlags.Variadic;

    // checker.ts getArrayElementTypeNode
    private NodeRef GetArrayElementTypeNode(NodeRef node)
    {
        var store = StoreOf(node);
        switch (store.Kind(node.Node))
        {
            case SyntaxKind.ParenthesizedType:
                return GetArrayElementTypeNode(At(node, store.Type(node.Node)));
            case SyntaxKind.TupleType:
            {
                var elements = store.Elements(node.Node);
                if (store.Length(elements) == 1)
                {
                    var element = store.Element(elements, 0);
                    if (store.Kind(element) == SyntaxKind.RestType || (store.Kind(element) == SyntaxKind.NamedTupleMember && !store.DotDotDotToken(element).IsNone))
                    {
                        return GetArrayElementTypeNode(At(node, store.Type(element)));
                    }
                }
                break;
            }
            case SyntaxKind.ArrayType:
                return At(node, store.ElementType(node.Node));
        }
        return NodeRef.None;
    }

    // checker.ts getArrayOrTupleTargetType
    private Type GetArrayOrTupleTargetType(NodeRef node)
    {
        var store = StoreOf(node);
        var parent = store.Parent(node.Node);
        var isReadonly = store.Kind(parent) == SyntaxKind.TypeOperator && store.Operator(parent) == SyntaxKind.ReadonlyKeyword;
        if (!GetArrayElementTypeNode(node).IsNone)
        {
            return isReadonly ? globalReadonlyArrayType : globalArrayType;
        }
        var elements = store.Nodes(store.Elements(node.Node)).ToArray();
        var elementFlags = new ElementFlags[elements.Length];
        var declarations = new NodeRef[elements.Length];
        for (var i = 0; i < elements.Length; i++)
        {
            elementFlags[i] = GetTupleElementFlags(At(node, elements[i]));
            declarations[i] = store.Kind(elements[i]) is SyntaxKind.NamedTupleMember or SyntaxKind.Parameter ? At(node, elements[i]) : NodeRef.None;
        }
        return GetTupleTargetType(elementFlags, isReadonly, declarations);
    }

    // checker.ts getTypeFromArrayOrTupleTypeNode
    private Type GetTypeFromArrayOrTupleTypeNode(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        var store = StoreOf(node);
        var target = GetArrayOrTupleTargetType(node);
        Type result;
        var isTuple = store.Kind(node.Node) == SyntaxKind.TupleType;
        if (target == emptyGenericType)
        {
            result = emptyObjectType;
        }
        else if (!(isTuple && Array.Exists(store.Nodes(store.Elements(node.Node)).ToArray(), e => (GetTupleElementFlags(At(node, e)) & ElementFlags.Variadic) != 0)) && IsDeferredTypeReferenceNode(node, hasDefaultTypeArguments: false))
        {
            result = isTuple && store.Length(store.Elements(node.Node)) == 0 ? target : CreateDeferredTypeReference(target, node, TypeMapper.None);
        }
        else
        {
            Type[] elementTypes;
            if (!isTuple)
            {
                elementTypes = [GetTypeFromTypeNode(At(node, store.ElementType(node.Node)))];
            }
            else
            {
                var elements = store.Nodes(store.Elements(node.Node)).ToArray();
                elementTypes = new Type[elements.Length];
                for (var i = 0; i < elements.Length; i++)
                {
                    elementTypes[i] = GetTypeFromTypeNode(At(node, elements[i]));
                }
            }
            result = CreateNormalizedTypeReference(target, elementTypes);
        }
        return SetCachedNodeType(node, result);
    }

    /// <summary>checker.ts createTupleType.</summary>
    internal Type CreateTupleType(Type[] elementTypes, ElementFlags[]? elementFlags = null, bool isReadonly = false, NodeRef[]? namedMemberDeclarations = null)
    {
        if (elementFlags is null)
        {
            elementFlags = new ElementFlags[elementTypes.Length];
            Array.Fill(elementFlags, ElementFlags.Required);
        }
        var tupleTarget = GetTupleTargetType(elementFlags, isReadonly, namedMemberDeclarations ?? []);
        return tupleTarget == emptyGenericType ? emptyObjectType
            : elementTypes.Length > 0 ? CreateNormalizedTypeReference(tupleTarget, elementTypes)
            : tupleTarget;
    }

    // checker.ts getTupleTargetType
    private Type GetTupleTargetType(ElementFlags[] elementFlags, bool isReadonly, NodeRef[] namedMemberDeclarations)
    {
        if (elementFlags.Length == 1 && (elementFlags[0] & ElementFlags.Rest) != 0)
        {
            // [...X[]] is equivalent to just X[]
            return isReadonly ? globalReadonlyArrayType : globalArrayType;
        }
        var key = new System.Text.StringBuilder();
        for (var i = 0; i < elementFlags.Length; i++)
        {
            if (i > 0)
            {
                key.Append(',');
            }
            var f = elementFlags[i];
            key.Append((f & ElementFlags.Required) != 0 ? '#' : (f & ElementFlags.Optional) != 0 ? '?' : (f & ElementFlags.Rest) != 0 ? '.' : '*');
        }
        if (isReadonly)
        {
            key.Append('R');
        }
        if (Array.Exists(namedMemberDeclarations, d => !d.IsNone))
        {
            foreach (var d in namedMemberDeclarations)
            {
                key.Append(',').Append(d.IsNone ? "_" : d.File.ToString(CultureInfo.InvariantCulture) + ":" + d.Node.Index.ToString(CultureInfo.InvariantCulture));
            }
        }
        var id = key.ToString();
        if (!tupleTypes.TryGetValue(id, out var type))
        {
            tupleTypes[id] = type = CreateTupleTargetType(elementFlags, isReadonly, namedMemberDeclarations);
        }
        return type;
    }

    // checker.ts createTupleTargetType: tuple types are references to synthesized generic interface types of the form
    //   interface Tuple<T0, T1, T2, ...> extends Array<T0 | T1 | T2 | ...> { 0: T0, 1: T1, 2: T2, ... }
    private Type CreateTupleTargetType(ElementFlags[] elementFlags, bool isReadonly, NodeRef[] namedMemberDeclarations)
    {
        var arity = elementFlags.Length;
        var minLength = 0;
        foreach (var f in elementFlags)
        {
            if ((f & (ElementFlags.Required | ElementFlags.Variadic)) != 0)
            {
                minLength++;
            }
        }
        var typeParameters = new Type[arity];
        var properties = new List<SymbolRef>();
        var combinedFlags = ElementFlags.None;
        for (var i = 0; i < arity; i++)
        {
            var typeParameter = typeParameters[i] = Types.CreateTypeParameter(SymbolRef.None);
            var flags = elementFlags[i];
            combinedFlags |= flags;
            if ((combinedFlags & ElementFlags.Variable) == 0)
            {
                var property = CreateSymbol(SymbolFlags.Property | ((flags & ElementFlags.Optional) != 0 ? SymbolFlags.Optional : 0), i.ToString(CultureInfo.InvariantCulture), isReadonly ? CheckFlags.Readonly : 0);
                if (i < namedMemberDeclarations.Length && !namedMemberDeclarations[i].IsNone)
                {
                    tupleLabelDeclarations[property] = namedMemberDeclarations[i];
                }
                ValueSymbolLinks.Get(property).ResolvedType = typeParameter;
                properties.Add(property);
            }
        }
        var fixedLength = properties.Count;
        var lengthSymbol = CreateSymbol(SymbolFlags.Property, "length", isReadonly ? CheckFlags.Readonly : 0);
        if ((combinedFlags & ElementFlags.Variable) != 0)
        {
            ValueSymbolLinks.Get(lengthSymbol).ResolvedType = numberType;
        }
        else
        {
            var literalTypes = new List<Type>();
            for (var i = minLength; i <= arity; i++)
            {
                literalTypes.Add(GetNumberLiteralType(i));
            }
            var lengthType = GetUnionType(literalTypes.ToArray()); // (before the links are written: the call can grow them)
            ValueSymbolLinks.Get(lengthSymbol).ResolvedType = lengthType;
        }
        properties.Add(lengthSymbol);
        var type = CreateObjectType(ObjectFlags.Tuple | ObjectFlags.Reference, SymbolRef.None);
        var data = Types.Object(type);
        var list = Types.CreateList(typeParameters);
        data.TypeParameters = list;
        data.HasTypeParameters = arity > 0;
        data.OuterTypeParameters = TypeList.Empty;
        data.LocalTypeParameters = list;
        InitializeInstantiations(type, typeParameters);
        data.Target = type;
        data.ResolvedTypeArguments = list;
        data.HasResolvedTypeArguments = true;
        var thisType = Types.CreateTypeParameter(SymbolRef.None);
        Types.TypeParameter(thisType).IsThisType = true;
        Types.TypeParameter(thisType).Constraint = type;
        data.ThisType = thisType;
        data.DeclaredMembersResolved = true;
        data.DeclaredProperties = Types.Store(properties);
        data.DeclaredCallSignatures = default;
        data.DeclaredConstructSignatures = default;
        data.DeclaredIndexInfos = default;
        data.ElementFlags = elementFlags;
        data.MinLength = minLength;
        data.FixedLength = fixedLength;
        data.CombinedFlags = combinedFlags;
        data.Readonly = isReadonly;
        data.LabeledElementDeclarations = namedMemberDeclarations;
        return type;
    }

    // links.tupleLabelDeclaration of tuple element properties
    private readonly Dictionary<SymbolRef, NodeRef> tupleLabelDeclarations = [];

    /// <summary>checker.ts createNormalizedTypeReference.</summary>
    internal Type CreateNormalizedTypeReference(Type target, Type[] typeArguments) =>
        (Types.ObjectFlagsOf(target) & ObjectFlags.Tuple) != 0 ? CreateNormalizedTupleType(target, typeArguments) : CreateTypeReference(target, typeArguments);

    // checker.ts createNormalizedTupleType
    private Type CreateNormalizedTupleType(Type target, Type[] elementTypes)
    {
        var targetData = Types.Object(target);
        if ((targetData.CombinedFlags & ElementFlags.NonRequired) == 0)
        {
            // No need to normalize when we only have regular required elements
            return CreateTypeReference(target, elementTypes);
        }
        // target.elementFlags[i]: undefined (no flag set) past the target's elements, when the type arguments end with the
        // this-type argument of getTypeWithThisArgument
        var allFlags = targetData.ElementFlags!;
        ElementFlags TargetFlag(int i) => i < allFlags.Length ? allFlags[i] : 0;
        if ((targetData.CombinedFlags & ElementFlags.Variadic) != 0)
        {
            // Transform [A, ...(X | Y | Z)] into [A, ...X] | [A, ...Y] | [A, ...Z]
            var unionIndex = -1;
            for (var i = 0; i < elementTypes.Length; i++)
            {
                if ((TargetFlag(i) & ElementFlags.Variadic) != 0 && (Types.Flags(elementTypes[i]) & (TypeFlags.Never | TypeFlags.Union)) != 0)
                {
                    unionIndex = i;
                    break;
                }
            }
            if (unionIndex >= 0)
            {
                var probe = new Type[elementTypes.Length];
                for (var i = 0; i < probe.Length; i++)
                {
                    probe[i] = (TargetFlag(i) & ElementFlags.Variadic) != 0 ? elementTypes[i] : unknownType;
                }
                return CheckCrossProductUnion(probe)
                    ? MapType(elementTypes[unionIndex], t => CreateNormalizedTupleType(target, ReplaceElement(elementTypes, unionIndex, t)))
                    : errorType;
            }
        }
        // We have optional, rest, or variadic elements that may need normalizing. Normalization ensures that all variadic
        // elements are generic and that the tuple type has one of the following layouts, disregarding variadic elements:
        // (1) Zero or more required elements, followed by zero or more optional elements, followed by zero or one rest element.
        // (2) Zero or more required elements, followed by a rest element, followed by zero or more required elements.
        // In either layout, zero or more generic variadic elements may be present at any location.
        var expandedTypes = new List<Type>();
        var expandedFlags = new List<ElementFlags>();
        var expandedDeclarations = new List<NodeRef>();
        var lastRequiredIndex = -1;
        var firstRestIndex = -1;
        var lastOptionalOrRestIndex = -1;
        var labels = targetData.LabeledElementDeclarations;
        for (var i = 0; i < elementTypes.Length; i++)
        {
            var type = elementTypes[i];
            var flags = TargetFlag(i);
            var label = labels is not null && i < labels.Length ? labels[i] : NodeRef.None;
            if ((flags & ElementFlags.Variadic) != 0)
            {
                if ((Types.Flags(type) & TypeFlags.Any) != 0)
                {
                    AddElement(type, ElementFlags.Rest, label);
                }
                else if ((Types.Flags(type) & TypeFlags.InstantiableNonPrimitive) != 0 || IsGenericMappedType(type))
                {
                    // Generic variadic elements stay as they are.
                    AddElement(type, ElementFlags.Variadic, label);
                }
                else if (IsTupleType(type))
                {
                    var elements = GetElementTypes(type);
                    if (elements.Length + expandedTypes.Count >= 10_000)
                    {
                        Error(currentNode, !currentNode.IsNone && Utilities.IsPartOfTypeNode(StoreOf(currentNode), currentNode.Node)
                            ? Diagnostics.Type_produces_a_tuple_type_that_is_too_large_to_represent
                            : Diagnostics.Expression_produces_a_tuple_type_that_is_too_large_to_represent);
                        return errorType;
                    }
                    // Spread variadic elements with tuple types into the resulting tuple.
                    var innerTarget = TupleTarget(type);
                    for (var n = 0; n < elements.Length; n++)
                    {
                        AddElement(elements[n], innerTarget.ElementFlags![n], innerTarget.LabeledElementDeclarations is { } l && n < l.Length ? l[n] : NodeRef.None);
                    }
                }
                else
                {
                    // Treat everything else as an array type and create a rest element.
                    var indexType = IsArrayLikeType(type) ? GetIndexTypeOfType(type, numberType) : Type.None;
                    AddElement(indexType.IsNone ? errorType : indexType, ElementFlags.Rest, label);
                }
            }
            else
            {
                // Copy other element kinds with no change.
                AddElement(type, flags, label);
            }
        }
        // Turn optional elements preceding the last required element into required elements
        for (var i = 0; i < lastRequiredIndex; i++)
        {
            if ((expandedFlags[i] & ElementFlags.Optional) != 0)
            {
                expandedFlags[i] = ElementFlags.Required;
            }
        }
        if (firstRestIndex >= 0 && firstRestIndex < lastOptionalOrRestIndex)
        {
            // Turn elements between first rest and last optional/rest into a single rest element
            var slice = new Type[lastOptionalOrRestIndex - firstRestIndex + 1];
            for (var i = 0; i < slice.Length; i++)
            {
                var t = expandedTypes[firstRestIndex + i];
                slice[i] = (expandedFlags[firstRestIndex + i] & ElementFlags.Variadic) != 0 ? GetIndexedAccessType(t, numberType) : t;
            }
            expandedTypes[firstRestIndex] = GetUnionType(slice);
            expandedTypes.RemoveRange(firstRestIndex + 1, lastOptionalOrRestIndex - firstRestIndex);
            expandedFlags.RemoveRange(firstRestIndex + 1, lastOptionalOrRestIndex - firstRestIndex);
            expandedDeclarations.RemoveRange(firstRestIndex + 1, lastOptionalOrRestIndex - firstRestIndex);
        }
        var tupleTarget = GetTupleTargetType([.. expandedFlags], targetData.Readonly, [.. expandedDeclarations]);
        return tupleTarget == emptyGenericType ? emptyObjectType
            : expandedFlags.Count > 0 ? CreateTypeReference(tupleTarget, expandedTypes.ToArray())
            : tupleTarget;

        void AddElement(Type type, ElementFlags flags, NodeRef declaration)
        {
            if ((flags & ElementFlags.Required) != 0)
            {
                lastRequiredIndex = expandedFlags.Count;
            }
            if ((flags & ElementFlags.Rest) != 0 && firstRestIndex < 0)
            {
                firstRestIndex = expandedFlags.Count;
            }
            if ((flags & (ElementFlags.Optional | ElementFlags.Rest)) != 0)
            {
                lastOptionalOrRestIndex = expandedFlags.Count;
            }
            expandedTypes.Add((flags & ElementFlags.Optional) != 0 ? AddOptionality(type, isProperty: true) : type);
            expandedFlags.Add(flags);
            expandedDeclarations.Add(declaration);
        }
    }

    private static Type[] ReplaceElement(Type[] types, int index, Type type)
    {
        var result = (Type[])types.Clone();
        result[index] = type;
        return result;
    }

    // checker.ts checkCrossProductUnion
    private bool CheckCrossProductUnion(Type[] types)
    {
        double size = 1;
        foreach (var t in types)
        {
            var flags = Types.Flags(t);
            size *= (flags & TypeFlags.Union) != 0 ? Types.Union(t).Types.Count : (flags & TypeFlags.Never) != 0 ? 0 : 1;
        }
        if (size >= 100000)
        {
            Error(currentNode, Diagnostics.Expression_produces_a_union_type_that_is_too_complex_to_represent);
            return false;
        }
        return true;
    }

    // checker.ts getTupleBaseType
    private Type GetTupleBaseType(Type type)
    {
        var data = Types.Object(type);
        var typeParameters = Types.Elements(data.TypeParameters).ToArray();
        var elementTypes = new Type[typeParameters.Length];
        for (var i = 0; i < typeParameters.Length; i++)
        {
            elementTypes[i] = (data.ElementFlags![i] & ElementFlags.Variadic) != 0 ? GetIndexedAccessType(typeParameters[i], numberType) : typeParameters[i];
        }
        return CreateArrayType(GetUnionType(elementTypes), data.Readonly);
    }

    // checker.ts getTypeFromOptionalTypeNode
    private Type GetTypeFromOptionalTypeNode(NodeRef node) => AddOptionality(GetTypeFromTypeNode(At(node, StoreOf(node).Type(node.Node))), isProperty: true);

    // checker.ts getTypeFromRestTypeNode
    private Type GetTypeFromRestTypeNode(NodeRef node)
    {
        var typeNode = At(node, StoreOf(node).Type(node.Node));
        var elementTypeNode = GetArrayElementTypeNode(typeNode);
        return GetTypeFromTypeNode(elementTypeNode.IsNone ? typeNode : elementTypeNode);
    }

    // checker.ts getTypeFromNamedTupleTypeNode
    private Type GetTypeFromNamedTupleTypeNode(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        var store = StoreOf(node);
        var type = !store.DotDotDotToken(node.Node).IsNone ? GetTypeFromRestTypeNode(node)
            : AddOptionality(GetTypeFromTypeNode(At(node, store.Type(node.Node))), isProperty: true, isOptional: !store.QuestionToken(node.Node).IsNone);
        return SetCachedNodeType(node, type);
    }

    // checker.ts getEndElementCount
    private static int GetEndElementCount(ObjectTypeData target, ElementFlags flags)
    {
        var elementFlags = target.ElementFlags!;
        var last = -1;
        for (var i = elementFlags.Length - 1; i >= 0; i--)
        {
            if ((elementFlags[i] & flags) == 0)
            {
                last = i;
                break;
            }
        }
        return elementFlags.Length - last - 1;
    }

    // checker.ts getTotalFixedElementCount
    private static int GetTotalFixedElementCount(ObjectTypeData target) => target.FixedLength + GetEndElementCount(target, ElementFlags.Fixed);

    // checker.ts getTupleElementTypeOutOfStartCount
    private Type GetTupleElementTypeOutOfStartCount(Type type, double index, Type undefinedOrMissing)
    {
        return MapType(type, t =>
        {
            var target = TupleTarget(t);
            var restType = GetRestTypeOfTupleType(t);
            if (restType.IsNone)
            {
                return undefinedType;
            }
            if (!undefinedOrMissing.IsNone && index >= GetTotalFixedElementCount(target))
            {
                return GetUnionType([restType, undefinedOrMissing]);
            }
            return restType;
        });
    }

    // checker.ts getRestTypeOfTupleType
    private Type GetRestTypeOfTupleType(Type type) => GetElementTypeOfSliceOfTupleType(type, TupleTarget(type).FixedLength);

    // checker.ts getElementTypeOfSliceOfTupleType
    private Type GetElementTypeOfSliceOfTupleType(Type type, int index, int endSkipCount = 0, bool writing = false, bool noReductions = false)
    {
        var length = GetTypeReferenceArity(type) - endSkipCount;
        if (index < length)
        {
            var typeArguments = GetTypeArguments(type).ToArray();
            var target = TupleTarget(type);
            var elementTypes = new List<Type>();
            for (var i = index; i < length; i++)
            {
                var t = typeArguments[i];
                elementTypes.Add((target.ElementFlags![i] & ElementFlags.Variadic) != 0 ? GetIndexedAccessType(t, numberType) : t);
            }
            return writing ? GetIntersectionType(elementTypes.ToArray()) : GetUnionType(elementTypes.ToArray(), noReductions ? UnionReduction.None : UnionReduction.Literal);
        }
        return Type.None;
    }

    // checker.ts isArrayLikeType: a reference to the global Array or ReadonlyArray type, or a type that is not undefined
    // or null and is assignable to ReadonlyArray<any>
    private bool IsArrayLikeType(Type type) =>
        IsArrayType(type) || ((Types.Flags(type) & TypeFlags.Nullable) == 0 && IsTypeAssignableTo(type, anyReadonlyArrayType));

    // ---- index types (keyof) ----

    private readonly Dictionary<Type, Type> resolvedIndexTypes = [];
    private readonly Dictionary<Type, Type> resolvedStringIndexTypes = [];

    // checker.ts createIndexType
    private Type CreateIndexType(Type type, IndexFlags indexFlags) =>
        Types.CreateWithData(TypeFlags.Index, new IndexData { Type = type, IndexFlags = indexFlags });

    // checker.ts createOriginIndexType
    private Type CreateOriginIndexType(Type type) => Types.CreateWithData(TypeFlags.Index, new IndexData { Type = type });

    // checker.ts getIndexTypeForGenericType
    private Type GetIndexTypeForGenericType(Type type, IndexFlags indexFlags)
    {
        var cache = (indexFlags & IndexFlags.StringsOnly) != 0 ? resolvedStringIndexTypes : resolvedIndexTypes;
        if (!cache.TryGetValue(type, out var result))
        {
            cache[type] = result = CreateIndexType(type, (indexFlags & IndexFlags.StringsOnly) != 0 ? IndexFlags.StringsOnly : IndexFlags.None);
        }
        return result;
    }

    /// <summary>checker.ts getIndexType.</summary>
    internal Type GetIndexType(Type type, IndexFlags indexFlags = IndexFlags.None)
    {
        type = GetReducedType(type);
        if (IsNoInferType(type))
        {
            return GetNoInferType(GetIndexType(Types.Data<SubstitutionData>(type).BaseType, indexFlags));
        }
        if (ShouldDeferIndexType(type, indexFlags))
        {
            return GetIndexTypeForGenericType(type, indexFlags);
        }
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Union) != 0)
        {
            var types = Types.Elements(Types.Union(type).Types).ToArray();
            return GetIntersectionType(Array.ConvertAll(types, t => GetIndexType(t, indexFlags)));
        }
        if ((flags & TypeFlags.Intersection) != 0)
        {
            var types = Types.Elements(Types.Union(type).Types).ToArray();
            return GetUnionType(Array.ConvertAll(types, t => GetIndexType(t, indexFlags)));
        }
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.Mapped) != 0)
        {
            return GetIndexTypeForMappedType(type, indexFlags);
        }
        if (type == wildcardType)
        {
            return wildcardType;
        }
        if ((flags & TypeFlags.Unknown) != 0)
        {
            return neverType;
        }
        if ((flags & (TypeFlags.Any | TypeFlags.Never)) != 0)
        {
            return stringNumberSymbolType;
        }
        return GetLiteralTypeFromProperties(type,
            ((indexFlags & IndexFlags.NoIndexSignatures) != 0 ? TypeFlags.StringLiteral : TypeFlags.StringLike) | ((indexFlags & IndexFlags.StringsOnly) != 0 ? 0 : TypeFlags.NumberLike | TypeFlags.ESSymbolLike),
            indexFlags == IndexFlags.None);
    }

    // checker.ts shouldDeferIndexType
    private bool ShouldDeferIndexType(Type type, IndexFlags indexFlags = IndexFlags.None)
    {
        var flags = Types.Flags(type);
        return (flags & TypeFlags.InstantiableNonPrimitive) != 0 ||
            IsGenericTupleType(type) ||
            (IsGenericMappedType(type) && !GetNameTypeFromMappedType(type).IsNone) ||
            ((flags & TypeFlags.Union) != 0 && (indexFlags & IndexFlags.NoReducibleCheck) == 0 && IsGenericReducibleType(type)) ||
            ((flags & TypeFlags.Intersection) != 0 && MaybeTypeOfKind(type, TypeFlags.Instantiable) && ExistsType(Types.Elements(Types.Union(type).Types), IsEmptyAnonymousObjectType));
    }

    // checker.ts getLiteralTypeFromProperties
    private Type GetLiteralTypeFromProperties(Type type, TypeFlags include, bool includeOrigin)
    {
        var origin = includeOrigin && ((Types.ObjectFlagsOf(type) & (ObjectFlags.ClassOrInterface | ObjectFlags.Reference)) != 0 || !Types.Record(type).AliasSymbol.IsNone) ? CreateOriginIndexType(type) : Type.None;
        var types = new List<Type>();
        foreach (var prop in GetPropertiesOfType(type))
        {
            types.Add(GetLiteralTypeFromProperty(prop, include));
        }
        foreach (var info in GetIndexInfosOfType(type))
        {
            // (enumNumberIndexInfo: the index signature of enum objects, module C5)
            types.Add(info != enumNumberIndexInfo && IsKeyTypeIncluded(info.KeyType, include)
                ? info.KeyType == stringType && (include & TypeFlags.Number) != 0 ? stringOrNumberType : info.KeyType
                : neverType);
        }
        return GetUnionType(types.ToArray(), UnionReduction.Literal, SymbolRef.None, default, origin);
    }

    // checker.ts isKeyTypeIncluded
    private bool IsKeyTypeIncluded(Type keyType, TypeFlags include) =>
        (Types.Flags(keyType) & include) != 0 ||
        ((Types.Flags(keyType) & TypeFlags.Intersection) != 0 && ExistsType(Types.Elements(Types.Union(keyType).Types), t => IsKeyTypeIncluded(t, include)));

    /// <summary>checker.ts getLiteralTypeFromProperty.</summary>
    internal Type GetLiteralTypeFromProperty(SymbolRef prop, TypeFlags include, bool includeNonPublic = false)
    {
        if (includeNonPublic || (GetDeclarationModifierFlagsFromSymbol(prop) & ModifierFlags.NonPublicAccessibilityModifier) == 0)
        {
            var type = ValueSymbolLinks.Has(prop) ? ValueSymbolLinks.Get(prop).NameType : Type.None;
            if (type.IsNone)
            {
                var valueDeclaration = ValueDeclarationOf(prop);
                var name = valueDeclaration.IsNone ? NodeRef.None : GetNameOfDeclarationRef(valueDeclaration);
                type = NameOf(prop) == InternalSymbolName.Default ? GetStringLiteralType("default")
                    : !name.IsNone ? GetLiteralTypeFromPropertyName(name) : Type.None;
                if (type.IsNone && !IsKnownSymbol(prop))
                {
                    type = GetStringLiteralType(SymbolName(prop));
                }
            }
            if (!type.IsNone && (Types.Flags(type) & include) != 0)
            {
                return type;
            }
        }
        return neverType;
    }

    // checker.ts isKnownSymbol
    private bool IsKnownSymbol(SymbolRef symbol) => NameOf(symbol).StartsWith("__@", StringComparison.Ordinal);

    // checker.ts getDeclarationModifierFlagsFromSymbol (the accessor declarations and synthetic properties: module C5)
    // declarations.find(d => d.kind === kind) as a loop (no delegate per call; Track O)
    private NodeRef FindDeclarationOfKind(Decls declarations, SyntaxKind kind)
    {
        foreach (var d in declarations)
        {
            if (Kind(d) == kind)
            {
                return d;
            }
        }
        return NodeRef.None;
    }

    private ModifierFlags GetDeclarationModifierFlagsFromSymbol(SymbolRef symbol, bool isWrite = false)
    {
        var declaration = ValueDeclarationOf(symbol);
        if (!declaration.IsNone)
        {
            var declarations = DeclarationsOf(symbol);
            var accessor = NodeRef.None;
            if (isWrite && declarations is not null)
            {
                accessor = FindDeclarationOfKind(declarations.Value, SyntaxKind.SetAccessor);
            }
            if (accessor.IsNone && (SymbolFlagsOf(symbol) & SymbolFlags.GetAccessor) != 0 && declarations is not null)
            {
                accessor = FindDeclarationOfKind(declarations.Value, SyntaxKind.GetAccessor);
            }
            if (!accessor.IsNone)
            {
                declaration = accessor;
            }
            var store = StoreOf(declaration);
            var flags = GetCombinedEffectiveModifierFlags(store, declaration.Node);
            var parent = ParentOf(symbol);
            return !parent.IsNone && (SymbolFlagsOf(parent) & SymbolFlags.Class) != 0 ? flags : flags & ~ModifierFlags.AccessibilityModifier;
        }
        if ((GetCheckFlags(symbol) & CheckFlags.Synthetic) != 0)
        {
            var checkFlags = GetCheckFlags(symbol);
            var accessModifier = (checkFlags & CheckFlags.ContainsPrivate) != 0 ? ModifierFlags.Private
                : (checkFlags & CheckFlags.ContainsPublic) != 0 ? ModifierFlags.Public
                : ModifierFlags.Protected;
            var staticModifier = (checkFlags & CheckFlags.ContainsStatic) != 0 ? ModifierFlags.Static : ModifierFlags.None;
            return accessModifier | staticModifier;
        }
        if ((SymbolFlagsOf(symbol) & SymbolFlags.Prototype) != 0)
        {
            return ModifierFlags.Public | ModifierFlags.Static;
        }
        return ModifierFlags.None;
    }

    // checker.ts getLiteralTypeFromPropertyName
    private Type GetLiteralTypeFromPropertyName(NodeRef name)
    {
        var store = StoreOf(name);
        switch (store.Kind(name.Node))
        {
            case SyntaxKind.PrivateIdentifier:
                return neverType;
            case SyntaxKind.NumericLiteral:
                return GetRegularTypeOfLiteralType(CheckExpression(name));
            case SyntaxKind.ComputedPropertyName:
                return GetRegularTypeOfLiteralType(CheckComputedPropertyName(name));
            case SyntaxKind.Identifier:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                return GetStringLiteralType(store.Texts.GetString(Utilities.GetTextOfIdentifierOrLiteral(store, name.Node))!);
            case SyntaxKind.BigIntLiteral:
                return GetRegularTypeOfLiteralType(CheckExpression(name));
        }
        return neverType;
    }

    /// <summary>checker.ts isTypeUsableAsPropertyName.</summary>
    internal bool IsTypeUsableAsPropertyName(Type type) => (Types.Flags(type) & TypeFlags.StringOrNumberLiteralOrUnique) != 0;

    /// <summary>checker.ts getPropertyNameFromType.</summary>
    internal string GetPropertyNameFromType(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.UniqueESSymbol) != 0)
        {
            return uniqueESSymbolNames[type];
        }
        var value = Types.Literal(type).Value;
        return Utilities.EscapeLeadingUnderscores(value is double d ? JsNumber.ToString(d) : (string)value!);
    }

    // ---- unique symbol types ----

    private readonly Dictionary<SymbolRef, Type> uniqueESSymbolTypes = [];
    private readonly Dictionary<Type, string> uniqueESSymbolNames = [];

    // checker.ts createUniqueESSymbolType (the symbol id is the checker's own counter in creation order)
    private Type CreateUniqueESSymbolType(SymbolRef symbol)
    {
        var type = Types.Create(TypeFlags.UniqueESSymbol, ObjectFlags.None, symbol);
        uniqueESSymbolNames[type] = "__@" + NameOf(symbol) + "@" + GetSymbolIdForUniqueName(symbol).ToString(CultureInfo.InvariantCulture);
        return type;
    }

    private readonly Dictionary<SymbolRef, int> symbolIdsForUniqueNames = [];

    private int GetSymbolIdForUniqueName(SymbolRef symbol)
    {
        if (!symbolIdsForUniqueNames.TryGetValue(symbol, out var id))
        {
            symbolIdsForUniqueNames[symbol] = id = symbolIdsForUniqueNames.Count + 1;
        }
        return id;
    }

    // checker.ts getESSymbolLikeTypeForNode (JavaScript: C9)
    private Type GetESSymbolLikeTypeForNode(NodeRef node)
    {
        var nodeStore = StoreOf(node);
        if (Utilities.IsInJSFile(nodeStore, node.Node) && nodeStore.Kind(node.Node) == SyntaxKind.JSDocTypeExpression)
        {
            var host = Utilities.GetJSDocHost(nodeStore, node.Node);
            if (!host.IsNone)
            {
                var single = GetSingleVariableOfVariableStatement(nodeStore, host);
                node = At(node, !single.IsNone ? single : host);
            }
        }
        if (IsValidESSymbolDeclaration(node))
        {
            var symbol = IsCommonJsExportPropertyAssignment(StoreOf(node), node.Node) ? GetSymbolOfNode(At(node, StoreOf(node).Left(node.Node))) : GetSymbolOfNode(node);
            if (!symbol.IsNone)
            {
                if (!uniqueESSymbolTypes.TryGetValue(symbol, out var type))
                {
                    uniqueESSymbolTypes[symbol] = type = CreateUniqueESSymbolType(symbol);
                }
                return type;
            }
        }
        return esSymbolType;
    }

    // utilities.ts isValidESSymbolDeclaration
    private bool IsValidESSymbolDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.VariableDeclaration:
                return IsVarConstLike(store, n) && store.Kind(store.Name(n)) == SyntaxKind.Identifier && IsVariableDeclarationInVariableStatement(store, n);
            case SyntaxKind.PropertyDeclaration:
                return HasEffectiveReadonlyModifier(store, n) && Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Static);
            case SyntaxKind.PropertySignature:
                return HasEffectiveReadonlyModifier(store, n);
        }
        return IsCommonJsExportPropertyAssignment(store, n);
    }

    // utilities.ts hasEffectiveReadonlyModifier
    private static bool HasEffectiveReadonlyModifier(NodeStore store, Node node) =>
        Utilities.IsInJSFile(store, node) ? (Utilities.GetEffectiveModifierFlags(store, node) & ModifierFlags.Readonly) != 0
            : Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Readonly);

    // utilities.ts isCommonJsExportPropertyAssignment
    private static bool IsCommonJsExportPropertyAssignment(NodeStore store, Node node) =>
        Utilities.IsInJSFile(store, node) && store.Kind(node) == SyntaxKind.BinaryExpression &&
        Utilities.GetAssignmentDeclarationKind(store, node) == AssignmentDeclarationKind.ExportsProperty;

    // utilities.ts getSingleVariableOfVariableStatement
    private static Node GetSingleVariableOfVariableStatement(NodeStore store, Node node)
    {
        if (store.Kind(node) != SyntaxKind.VariableStatement)
        {
            return Node.None;
        }
        var declarations = store.Declarations(store.DeclarationList(node));
        return !declarations.IsNone && store.Length(declarations) > 0 ? store.Element(declarations, 0) : Node.None;
    }

    // utilities.ts isVarConstLike: const, using and await using declarations
    private static bool IsVarConstLike(NodeStore store, Node node)
    {
        var list = store.Parent(node);
        if (store.Kind(list) != SyntaxKind.VariableDeclarationList)
        {
            return false;
        }
        var blockScopeKind = store.Flags(list) & NodeFlags.BlockScoped;
        return blockScopeKind is NodeFlags.Const or NodeFlags.Using or NodeFlags.AwaitUsing;
    }

    private static bool IsVariableDeclarationInVariableStatement(NodeStore store, Node node)
    {
        var list = store.Parent(node);
        return store.Kind(list) == SyntaxKind.VariableDeclarationList && store.Kind(store.Parent(list)) == SyntaxKind.VariableStatement;
    }

    // ---- type operator, indexed access and template type nodes ----

    private SymbolRef deferredGlobalExtractSymbol;

    // checker.ts getGlobalExtractSymbol (getGlobalTypeAliasSymbol("Extract", 2, true))
    private SymbolRef GetGlobalExtractSymbol()
    {
        if (deferredGlobalExtractSymbol.IsNone)
        {
            var symbol = GetGlobalSymbol("Extract", SymbolFlags.TypeAlias, Diagnostics.Cannot_find_global_type_0);
            if (!symbol.IsNone)
            {
                GetDeclaredTypeOfSymbol(symbol);
                var typeParameters = GetTypeAliasTypeParameters(symbol);
                if ((typeParameters?.Length ?? 0) != 2)
                {
                    var declaration = DeclarationsOf(symbol)?.Find(d => Kind(d) == SyntaxKind.TypeAliasDeclaration) ?? NodeRef.None;
                    Error(declaration, Diagnostics.Global_type_0_must_have_1_type_parameter_s, SymbolName(symbol), 2);
                    symbol = SymbolRef.None;
                }
            }
            deferredGlobalExtractSymbol = symbol.IsNone ? unknownSymbol : symbol;
        }
        return deferredGlobalExtractSymbol == unknownSymbol ? SymbolRef.None : deferredGlobalExtractSymbol;
    }

    // checker.ts getExtractStringType
    private Type GetExtractStringType(Type type)
    {
        var extractTypeAlias = GetGlobalExtractSymbol();
        return !extractTypeAlias.IsNone ? GetTypeAliasInstantiation(extractTypeAlias, [type, stringType]) : stringType;
    }

    // checker.ts getTypeFromTypeOperatorNode
    private Type GetTypeFromTypeOperatorNode(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        var store = StoreOf(node);
        var typeNode = At(node, store.Type(node.Node));
        Type type;
        switch (store.Operator(node.Node))
        {
            case SyntaxKind.KeyOfKeyword:
                type = GetIndexType(GetTypeFromTypeNode(typeNode));
                break;
            case SyntaxKind.UniqueKeyword:
            {
                if (store.Kind(typeNode.Node) == SyntaxKind.SymbolKeyword)
                {
                    var parent = Parent(node);
                    while (Kind(parent) == SyntaxKind.ParenthesizedType)
                    {
                        parent = Parent(parent);
                    }
                    type = GetESSymbolLikeTypeForNode(parent);
                }
                else
                {
                    type = errorType;
                }
                break;
            }
            default:
                type = GetTypeFromTypeNode(typeNode);
                break;
        }
        return SetCachedNodeType(node, type);
    }

    // checker.ts getTypeFromIndexedAccessTypeNode
    private Type GetTypeFromIndexedAccessTypeNode(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        var store = StoreOf(node);
        var objectType = GetTypeFromTypeNode(At(node, store.ObjectType(node.Node)));
        var indexType = GetTypeFromTypeNode(At(node, store.IndexType(node.Node)));
        var potentialAlias = GetAliasSymbolForTypeNode(node);
        return SetCachedNodeType(node, GetIndexedAccessType(objectType, indexType, AccessFlags.None, node, potentialAlias, GetTypeArgumentsForAliasSymbol(potentialAlias)));
    }

    // checker.ts getTypeFromTemplateTypeNode
    private Type GetTypeFromTemplateTypeNode(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        var store = StoreOf(node);
        var spans = store.Nodes(store.TemplateSpans(node.Node)).ToArray();
        var texts = new string[spans.Length + 1];
        texts[0] = store.Texts.GetString(store.Text(store.Head(node.Node))) ?? "";
        var types = new Type[spans.Length];
        for (var i = 0; i < spans.Length; i++)
        {
            texts[i + 1] = store.Texts.GetString(store.Text(store.Literal(spans[i]))) ?? "";
            types[i] = GetTypeFromTypeNode(At(node, store.Type(spans[i])));
        }
        return SetCachedNodeType(node, GetTemplateLiteralType(texts, types));
    }

    // checker.ts getTypeFromImportTypeNode
    private Type GetTypeFromImportTypeNode(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        var store = StoreOf(node);
        var n = node.Node;
        var argument = store.Argument(n);
        if (store.Kind(argument) != SyntaxKind.LiteralType || !Utilities.IsStringLiteralLike(store, store.Literal(argument)))
        {
            Error(At(node, argument), Diagnostics.String_literal_expected);
            SymbolNodeLinks.Get(node).ResolvedSymbol = unknownSymbol;
            return SetCachedNodeType(node, errorType);
        }
        var targetMeaning = store.IsTypeOf(n) ? SymbolFlags.Value : (store.Flags(n) & NodeFlags.JSDoc) != 0 ? SymbolFlags.Value | SymbolFlags.Type : SymbolFlags.Type;
        var innerModuleSymbol = ResolveExternalModuleName(node, At(node, store.Literal(argument)));
        if (innerModuleSymbol.IsNone)
        {
            SymbolNodeLinks.Get(node).ResolvedSymbol = unknownSymbol;
            return SetCachedNodeType(node, errorType);
        }
        var isExportEquals = ExportsOf(innerModuleSymbol)?.Has(InternalSymbolName.ExportEquals) == true;
        var moduleSymbol = ResolveExternalModuleSymbol(innerModuleSymbol, dontResolveAlias: false);
        var qualifier = store.Qualifier(n);
        if (!qualifier.IsNone && !Utilities.NodeIsMissing(store, qualifier))
        {
            var nameStack = new List<Node>();
            for (var q = qualifier; ; q = store.Left(q))
            {
                if (store.Kind(q) == SyntaxKind.QualifiedName)
                {
                    nameStack.Insert(0, store.Right(q));
                }
                else
                {
                    nameStack.Insert(0, q);
                    break;
                }
            }
            var currentNamespace = moduleSymbol;
            for (var i = 0; i < nameStack.Count; i++)
            {
                var current = nameStack[i];
                var meaning = i < nameStack.Count - 1 ? SymbolFlags.Namespace : targetMeaning;
                var currentName = store.Texts.GetString(store.EscapedText(current)) ?? "";
                // typeof a.b.c is normally resolved using `checkExpression` which in turn defers to `checkQualifiedName`
                // That, in turn, ultimately uses `getPropertyOfType` on the type of the symbol, which differs slightly from
                // the `exports` lookup process that only looks up namespace members which is used for most type references
                var mergedResolvedSymbol = GetMergedSymbol(ResolveSymbol(currentNamespace));
                var symbolFromVariable = store.IsTypeOf(n) || (Utilities.IsInJSFile(store, n) && isExportEquals)
                    ? GetPropertyOfType(GetTypeOfSymbol(mergedResolvedSymbol), currentName)
                    : SymbolRef.None;
                var symbolFromModule = store.IsTypeOf(n) ? SymbolRef.None : GetSymbol(GetExportsOfSymbol(mergedResolvedSymbol), currentName, meaning);
                var next = !symbolFromModule.IsNone ? symbolFromModule : symbolFromVariable;
                if (next.IsNone)
                {
                    Error(At(node, current), Diagnostics.Namespace_0_has_no_exported_member_1, GetFullyQualifiedName(currentNamespace), Utilities.DeclarationNameToString(store, current));
                    return SetCachedNodeType(node, errorType);
                }
                SymbolNodeLinks.Get(At(node, current)).ResolvedSymbol = next;
                SymbolNodeLinks.Get(At(node, store.Parent(current))).ResolvedSymbol = next;
                currentNamespace = next;
            }
            return SetCachedNodeType(node, ResolveImportSymbolType(node, currentNamespace, targetMeaning));
        }
        if ((SymbolFlagsOf(moduleSymbol) & targetMeaning) != 0)
        {
            return SetCachedNodeType(node, ResolveImportSymbolType(node, moduleSymbol, targetMeaning));
        }
        var errorMessage = targetMeaning == SymbolFlags.Value
            ? Diagnostics.Module_0_does_not_refer_to_a_value_but_is_used_as_a_value_here
            : Diagnostics.Module_0_does_not_refer_to_a_type_but_is_used_as_a_type_here_Did_you_mean_typeof_import_0;
        Error(node, errorMessage, store.Texts.GetString(store.Text(store.Literal(argument))));
        SymbolNodeLinks.Get(node).ResolvedSymbol = unknownSymbol;
        return SetCachedNodeType(node, errorType);
    }

    // checker.ts resolveImportSymbolType
    private Type ResolveImportSymbolType(NodeRef node, SymbolRef symbol, SymbolFlags meaning)
    {
        var resolvedSymbol = ResolveSymbol(symbol);
        SymbolNodeLinks.Get(node).ResolvedSymbol = resolvedSymbol;
        if (meaning == SymbolFlags.Value)
        {
            return GetInstantiationExpressionType(GetTypeOfSymbol(symbol), node); // intentionally doesn't use resolved symbol so type is cached as expected on the alias
        }
        return GetTypeReferenceType(node, resolvedSymbol); // getTypeReferenceType doesn't handle aliases - it must get the resolved symbol
    }

    // ---- template literal and string mapping types ----

    private readonly Dictionary<string, Type> templateLiteralTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<(SymbolRef Symbol, Type Type), Type> stringMappingTypes = []; // (module CO2: the key's parts as values)

    /// <summary>checker.ts getTemplateLiteralType.</summary>
    internal Type GetTemplateLiteralType(string[] texts, Type[] types)
    {
        var unionIndex = Array.FindIndex(types, t => (Types.Flags(t) & (TypeFlags.Never | TypeFlags.Union)) != 0);
        if (unionIndex >= 0)
        {
            return CheckCrossProductUnion(types)
                ? MapType(types[unionIndex], t => GetTemplateLiteralType(texts, ReplaceElement(types, unionIndex, t)))
                : errorType;
        }
        if (Array.IndexOf(types, wildcardType) >= 0)
        {
            return wildcardType;
        }
        var newTypes = new List<Type>();
        var newTexts = new List<string>();
        var text = texts[0];
        if (!AddSpans(texts, types))
        {
            return stringType;
        }
        if (newTypes.Count == 0)
        {
            return GetStringLiteralType(text);
        }
        newTexts.Add(text);
        if (newTexts.TrueForAll(t => t.Length == 0))
        {
            if (newTypes.TrueForAll(t => (Types.Flags(t) & TypeFlags.String) != 0))
            {
                return stringType;
            }
            // Normalize `${Mapping<xxx>}` into Mapping<xxx>
            if (newTypes.Count == 1 && IsPatternLiteralType(newTypes[0]))
            {
                return newTypes[0];
            }
        }
        var id = GetTypeListId(newTypes.ToArray()) + "|" + string.Join(",", newTexts.ConvertAll(t => t.Length.ToString(CultureInfo.InvariantCulture))) + "|" + string.Concat(newTexts);
        if (!templateLiteralTypes.TryGetValue(id, out var type))
        {
            templateLiteralTypes[id] = type = Types.CreateWithData(TypeFlags.TemplateLiteral, new TemplateLiteralData { Texts = [.. newTexts], Types = [.. newTypes] });
        }
        return type;

        bool AddSpans(string[] spanTexts, Type[] spanTypes)
        {
            for (var i = 0; i < spanTypes.Length; i++)
            {
                var t = spanTypes[i];
                var flags = Types.Flags(t);
                if ((flags & (TypeFlags.Literal | TypeFlags.Null | TypeFlags.Undefined)) != 0)
                {
                    text += GetTemplateStringForType(t) ?? "";
                    text += spanTexts[i + 1];
                }
                else if ((flags & TypeFlags.TemplateLiteral) != 0)
                {
                    var data = Types.Data<TemplateLiteralData>(t);
                    text += data.Texts[0];
                    if (!AddSpans(data.Texts, data.Types))
                    {
                        return false;
                    }
                    text += spanTexts[i + 1];
                }
                else if (IsGenericIndexType(t) || IsPatternLiteralPlaceholderType(t))
                {
                    newTypes.Add(t);
                    newTexts.Add(text);
                    text = spanTexts[i + 1];
                }
                else
                {
                    return false;
                }
            }
            return true;
        }
    }

    // checker.ts getTemplateStringForType
    private string? GetTemplateStringForType(Type type)
    {
        var flags = Types.Flags(type);
        return (flags & TypeFlags.StringLiteral) != 0 ? (string)Types.Literal(type).Value!
            : (flags & TypeFlags.NumberLiteral) != 0 ? JsNumber.ToString((double)Types.Literal(type).Value!)
            : (flags & TypeFlags.BigIntLiteral) != 0 ? (string)Types.Literal(type).Value!
            : (flags & (TypeFlags.BooleanLiteral | TypeFlags.Nullable)) != 0 ? Types.IntrinsicName(type)
            : null;
    }

    /// <summary>checker.ts getStringMappingType.</summary>
    internal Type GetStringMappingType(SymbolRef symbol, Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & (TypeFlags.Union | TypeFlags.Never)) != 0)
        {
            return MapType(type, t => GetStringMappingType(symbol, t));
        }
        if ((flags & TypeFlags.StringLiteral) != 0)
        {
            return GetStringLiteralType(ApplyStringMapping(symbol, (string)Types.Literal(type).Value!));
        }
        if ((flags & TypeFlags.TemplateLiteral) != 0)
        {
            var data = Types.Data<TemplateLiteralData>(type);
            var (texts, types) = ApplyTemplateStringMapping(symbol, data.Texts, data.Types);
            return GetTemplateLiteralType(texts, types);
        }
        // Mapping<Mapping<T>> === Mapping<T>
        if ((flags & TypeFlags.StringMapping) != 0 && symbol == Types.Symbol(type))
        {
            return type;
        }
        if ((flags & (TypeFlags.Any | TypeFlags.String | TypeFlags.StringMapping)) != 0 || IsGenericIndexType(type))
        {
            return GetStringMappingTypeForGenericType(symbol, type);
        }
        // This handles Mapping<`${number}`> and Mapping<`${bigint}`>
        if (IsPatternLiteralPlaceholderType(type))
        {
            return GetStringMappingTypeForGenericType(symbol, GetTemplateLiteralType(["", ""], [type]));
        }
        return type;
    }

    private string ApplyStringMapping(SymbolRef symbol, string str)
    {
        switch (GetIntrinsicTypeKind(NameOf(symbol)))
        {
            case IntrinsicTypeKind.Uppercase:
                return JsUpper(str);
            case IntrinsicTypeKind.Lowercase:
                return JsLower(str);
            case IntrinsicTypeKind.Capitalize:
                return str.Length == 0 ? str : JsUpper(str[..1]) + str[1..];
            case IntrinsicTypeKind.Uncapitalize:
                return str.Length == 0 ? str : JsLower(str[..1]) + str[1..];
        }
        return str;
    }

    // String.prototype.toUpperCase / toLowerCase (locale-independent)
    private static string JsUpper(string s) => s.ToUpperInvariant();

    private static string JsLower(string s) => s.ToLowerInvariant();

    private (string[] Texts, Type[] Types) ApplyTemplateStringMapping(SymbolRef symbol, string[] texts, Type[] types)
    {
        switch (GetIntrinsicTypeKind(NameOf(symbol)))
        {
            case IntrinsicTypeKind.Uppercase:
                return (Array.ConvertAll(texts, JsUpper), Array.ConvertAll(types, t => GetStringMappingType(symbol, t)));
            case IntrinsicTypeKind.Lowercase:
                return (Array.ConvertAll(texts, JsLower), Array.ConvertAll(types, t => GetStringMappingType(symbol, t)));
            case IntrinsicTypeKind.Capitalize:
            case IntrinsicTypeKind.Uncapitalize:
            {
                var upper = GetIntrinsicTypeKind(NameOf(symbol)) == IntrinsicTypeKind.Capitalize;
                if (texts[0].Length == 0)
                {
                    return (texts, [GetStringMappingType(symbol, types[0]), .. types[1..]]);
                }
                var first = upper ? JsUpper(texts[0][..1]) + texts[0][1..] : JsLower(texts[0][..1]) + texts[0][1..];
                return ([first, .. texts[1..]], types);
            }
        }
        return (texts, types);
    }

    private Type GetStringMappingTypeForGenericType(SymbolRef symbol, Type type)
    {
        var id = (symbol, type);
        if (!stringMappingTypes.TryGetValue(id, out var result))
        {
            stringMappingTypes[id] = result = Types.CreateWithData(TypeFlags.StringMapping, new StringMappingData { Type = type }, symbol);
        }
        return result;
    }

    // checker.ts isPatternLiteralPlaceholderType
    private bool IsPatternLiteralPlaceholderType(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Intersection) != 0)
        {
            // Return true if the intersection consists of one or more placeholders and zero or
            // more object type tags.
            var seenPlaceholder = false;
            foreach (var t in Types.Elements(Types.Union(type).Types).ToArray())
            {
                if ((Types.Flags(t) & (TypeFlags.Literal | TypeFlags.Nullable)) != 0 || IsPatternLiteralPlaceholderType(t))
                {
                    seenPlaceholder = true;
                }
                else if ((Types.Flags(t) & TypeFlags.Object) == 0)
                {
                    return false;
                }
            }
            return seenPlaceholder;
        }
        return (flags & (TypeFlags.Any | TypeFlags.String | TypeFlags.Number | TypeFlags.BigInt)) != 0 || IsPatternLiteralType(type);
    }

    // checker.ts isPatternLiteralType
    private bool IsPatternLiteralType(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.TemplateLiteral) != 0)
        {
            return Array.TrueForAll(Types.Data<TemplateLiteralData>(type).Types, IsPatternLiteralPlaceholderType);
        }
        return (flags & TypeFlags.StringMapping) != 0 && IsPatternLiteralPlaceholderType(Types.Data<StringMappingData>(type).Type);
    }

    // checker.ts isGenericStringLikeType
    private bool IsGenericStringLikeType(Type type) =>
        (Types.Flags(type) & (TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 && !IsPatternLiteralType(type);

    // ---- generic object flags ----

    /// <summary>checker.ts isGenericType.</summary>
    internal bool IsGenericType(Type type) => GetGenericObjectFlags(type) != 0;

    /// <summary>checker.ts isGenericObjectType.</summary>
    internal bool IsGenericObjectType(Type type) => (GetGenericObjectFlags(type) & ObjectFlags.IsGenericObjectType) != 0;

    /// <summary>checker.ts isGenericIndexType.</summary>
    internal bool IsGenericIndexType(Type type) => (GetGenericObjectFlags(type) & ObjectFlags.IsGenericIndexType) != 0;

    // checker.ts getGenericObjectFlags
    private ObjectFlags GetGenericObjectFlags(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.UnionOrIntersection) != 0)
        {
            if ((Types.ObjectFlagsOf(type) & ObjectFlags.IsGenericTypeComputed) == 0)
            {
                ObjectFlags result = 0;
                foreach (var t in Types.Elements(Types.Union(type).Types).ToArray())
                {
                    result |= GetGenericObjectFlags(t);
                }
                Types.Record(type).ObjectFlags |= ObjectFlags.IsGenericTypeComputed | result;
            }
            return Types.ObjectFlagsOf(type) & ObjectFlags.IsGenericType;
        }
        if ((flags & TypeFlags.Substitution) != 0)
        {
            if ((Types.ObjectFlagsOf(type) & ObjectFlags.IsGenericTypeComputed) == 0)
            {
                var data = Types.Data<SubstitutionData>(type);
                var genericFlags = GetGenericObjectFlags(data.BaseType) | GetGenericObjectFlags(data.Constraint); // (before the record is written)
                Types.Record(type).ObjectFlags |= ObjectFlags.IsGenericTypeComputed | genericFlags;
            }
            return Types.ObjectFlagsOf(type) & ObjectFlags.IsGenericType;
        }
        return ((flags & TypeFlags.InstantiableNonPrimitive) != 0 || IsGenericMappedType(type) || IsGenericTupleType(type) ? ObjectFlags.IsGenericObjectType : 0) |
            ((flags & (TypeFlags.InstantiableNonPrimitive | TypeFlags.Index)) != 0 || IsGenericStringLikeType(type) ? ObjectFlags.IsGenericIndexType : 0);
    }

    // checker.ts isGenericReducibleType
    private bool IsGenericReducibleType(Type type) =>
        ((Types.Flags(type) & TypeFlags.Union) != 0 && (Types.ObjectFlagsOf(type) & ObjectFlags.ContainsIntersections) != 0 &&
            ExistsType(Types.Elements(Types.Union(type).Types), IsGenericReducibleType)) ||
        ((Types.Flags(type) & TypeFlags.Intersection) != 0 && IsReducibleIntersection(type));

    /// <summary>checker.ts maybeTypeOfKind.</summary>
    internal bool MaybeTypeOfKind(Type type, TypeFlags kind)
    {
        if ((Types.Flags(type) & kind) != 0)
        {
            return true;
        }
        if ((Types.Flags(type) & TypeFlags.UnionOrIntersection) == 0)
        {
            return false;
        }
        // (some(types, t => maybeTypeOfKind(t, kind)) as a loop: no closure per call; tsrs's closures are monomorphized
        // impl FnMut generics, notes/monomorphization-audit.md; Track O)
        foreach (var t in Types.Elements(Types.Union(type).Types))
        {
            if (MaybeTypeOfKind(t, kind))
            {
                return true;
            }
        }
        return false;
    }

    // ---- indexed access types ----

    private readonly KeyMap<Type> indexedAccessTypes = new();

    /// <summary>checker.ts getIndexedAccessType.</summary>
    internal Type GetIndexedAccessType(Type objectType, Type indexType, AccessFlags accessFlags = AccessFlags.None, NodeRef accessNode = default, SymbolRef aliasSymbol = default, TypeList aliasTypeArguments = default)
    {
        var result = GetIndexedAccessTypeOrUndefined(objectType, indexType, accessFlags, accessNode, aliasSymbol, aliasTypeArguments);
        return !result.IsNone ? result : !accessNode.IsNone ? errorType : unknownType;
    }

    // checker.ts indexTypeLessThan
    private bool IndexTypeLessThan(Type indexType, int limit) =>
        EveryType(indexType, t =>
        {
            if ((Types.Flags(t) & TypeFlags.StringOrNumberLiteral) != 0)
            {
                var propName = GetPropertyNameFromType(t);
                if (IsNumericLiteralName(propName))
                {
                    var index = JsNumber.FromString(propName);
                    return index >= 0 && index < limit;
                }
            }
            return false;
        });

    /// <summary>checker.ts getIndexedAccessTypeOrUndefined.</summary>
    internal Type GetIndexedAccessTypeOrUndefined(Type objectType, Type indexType, AccessFlags accessFlags = AccessFlags.None, NodeRef accessNode = default, SymbolRef aliasSymbol = default, TypeList aliasTypeArguments = default)
    {
        if (objectType == wildcardType || indexType == wildcardType)
        {
            return wildcardType;
        }
        objectType = GetReducedType(objectType);
        // If the object type has a string index signature and no other members we know that the result will
        // always be the type of that index signature and we can simplify accordingly.
        if (IsStringIndexSignatureOnlyType(objectType) && (Types.Flags(indexType) & TypeFlags.Nullable) == 0 && IsTypeAssignableToKind(indexType, TypeFlags.String | TypeFlags.Number))
        {
            indexType = stringType;
        }
        // In noUncheckedIndexedAccess mode, indexed access operations that occur in an expression in a read position and resolve to
        // an index signature have 'undefined' included in their type.
        if (compilerOptions.NoUncheckedIndexedAccess == true && (accessFlags & AccessFlags.ExpressionPosition) != 0)
        {
            accessFlags |= AccessFlags.IncludeUndefined;
        }
        // If the index type is generic, or if the object type is generic and doesn't originate in an expression and
        // the operation isn't exclusively indexing the fixed (non-variadic) portion of a tuple type, we are performing
        // a higher-order index access where we cannot meaningfully access the properties of the object type.
        var accessFromExpression = !accessNode.IsNone && Kind(accessNode) != SyntaxKind.IndexedAccessType;
        if (IsGenericIndexType(indexType) || (accessFromExpression
            ? IsGenericTupleType(objectType) && !IndexTypeLessThan(indexType, GetTotalFixedElementCount(TupleTarget(objectType)))
            : (IsGenericObjectType(objectType) && !(IsTupleType(objectType) && IndexTypeLessThan(indexType, GetTotalFixedElementCount(TupleTarget(objectType))))) || IsGenericReducibleType(objectType)))
        {
            if ((Types.Flags(objectType) & TypeFlags.AnyOrUnknown) != 0)
            {
                return objectType;
            }
            // Defer the operation by creating an indexed access type.
            var persistentAccessFlags = accessFlags & AccessFlags.Persistent;
            var mark = keyScratch.Mark();
            keyScratch.Add(objectType.Id);
            keyScratch.Add(indexType.Id);
            keyScratch.Add((int)persistentAccessFlags);
            AddAliasKey(aliasSymbol, aliasTypeArguments);
            var id = keyScratch.Since(mark);
            if (!indexedAccessTypes.TryGetValue(id, out var type))
            {
                type = Types.CreateWithData(TypeFlags.IndexedAccess, new IndexedAccessData { ObjectType = objectType, IndexType = indexType, AccessFlags = persistentAccessFlags });
                ref var record = ref Types.Record(type);
                record.AliasSymbol = aliasSymbol;
                record.AliasTypeArguments = aliasTypeArguments;
                indexedAccessTypes.Set(id, type);
            }
            keyScratch.Release(mark);
            return type;
        }
        // In the following we resolve T[K] to the type of the property in T selected by K.
        // We treat boolean as different from other unions to improve errors;
        // skipping straight to getPropertyTypeForIndexType gives errors with 'boolean' instead of 'true'.
        var apparentObjectType = GetReducedApparentType(objectType);
        if ((Types.Flags(indexType) & TypeFlags.Union) != 0 && (Types.Flags(indexType) & TypeFlags.Boolean) == 0)
        {
            var propTypes = new List<Type>();
            var wasMissingProp = false;
            foreach (var t in Types.Elements(Types.Union(indexType).Types).ToArray())
            {
                var propType = GetPropertyTypeForIndexType(objectType, apparentObjectType, t, indexType, accessNode, accessFlags | (wasMissingProp ? AccessFlags.SuppressNoImplicitAnyError : 0));
                if (!propType.IsNone)
                {
                    propTypes.Add(propType);
                }
                else if (accessNode.IsNone)
                {
                    // If there's no error node, we can immeditely stop, since error reporting is off
                    return Type.None;
                }
                else
                {
                    // Otherwise we set a flag and return at the end of the loop so we still mark all errors
                    wasMissingProp = true;
                }
            }
            if (wasMissingProp)
            {
                return Type.None;
            }
            return (accessFlags & AccessFlags.Writing) != 0
                ? GetIntersectionType(propTypes.ToArray(), aliasSymbol, aliasTypeArguments)
                : GetUnionType(propTypes.ToArray(), UnionReduction.Literal, aliasSymbol, aliasTypeArguments);
        }
        return GetPropertyTypeForIndexType(objectType, apparentObjectType, indexType, indexType, accessNode, accessFlags | AccessFlags.CacheSymbol | AccessFlags.ReportDeprecated);
    }

    // checker.ts getPropertyTypeForIndexType (the element access expression parts: module C5; deprecation suggestions: C8)
    private Type GetPropertyTypeForIndexType(Type originalObjectType, Type objectType, Type indexType, Type fullIndexType, NodeRef accessNode, AccessFlags accessFlags)
    {
        var accessExpression = !accessNode.IsNone && Kind(accessNode) == SyntaxKind.ElementAccessExpression ? accessNode : NodeRef.None;
        var propName = !accessNode.IsNone && Kind(accessNode) == SyntaxKind.PrivateIdentifier ? null : GetPropertyNameFromIndex(indexType, accessNode);
        if (propName is not null)
        {
            if ((accessFlags & AccessFlags.Contextual) != 0)
            {
                var contextualPropertyType = GetTypeOfPropertyOfContextualType(objectType, propName);
                return contextualPropertyType.IsNone ? anyType : contextualPropertyType;
            }
            var prop = GetPropertyOfType(objectType, propName);
            if (!prop.IsNone)
            {
                if ((accessFlags & AccessFlags.ReportDeprecated) != 0 && !accessNode.IsNone && DeclarationsOf(prop) is { } propDeclarations && IsDeprecatedSymbol(prop) &&
                    IsUncalledFunctionReference(accessNode, prop))
                {
                    var deprecatedNode = !accessExpression.IsNone ? At(accessExpression, StoreOf(accessExpression).ArgumentExpression(accessExpression.Node))
                        : Kind(accessNode) == SyntaxKind.IndexedAccessType ? At(accessNode, StoreOf(accessNode).IndexType(accessNode.Node)) : accessNode;
                    AddDeprecatedSuggestion(deprecatedNode, propDeclarations, propName);
                }
                if (!accessExpression.IsNone)
                {
                    var accessedExpression = At(accessExpression, StoreOf(accessExpression).Expression(accessExpression.Node));
                    MarkPropertyAsReferenced(prop, accessExpression, IsSelfTypeAccess(accessedExpression, Types.Symbol(objectType)));
                    if (IsAssignmentToReadonlyEntity(accessExpression, prop, GetAssignmentTargetKind(accessExpression)))
                    {
                        Error(At(accessExpression, StoreOf(accessExpression).ArgumentExpression(accessExpression.Node)), Diagnostics.Cannot_assign_to_0_because_it_is_a_read_only_property, SymbolToString(prop));
                        return Type.None;
                    }
                    if ((accessFlags & AccessFlags.CacheSymbol) != 0)
                    {
                        SymbolNodeLinks.Get(accessNode).ResolvedSymbol = prop;
                    }
                    if (IsThisPropertyAccessInConstructor(accessExpression, prop))
                    {
                        return autoType;
                    }
                }
                var propType = (accessFlags & AccessFlags.Writing) != 0 ? GetWriteTypeOfSymbol(prop) : GetTypeOfSymbol(prop);
                return !accessExpression.IsNone && GetAssignmentTargetKind(accessExpression) != AssignmentKind.Definite ? GetFlowTypeOfReference(accessExpression, propType)
                    : !accessNode.IsNone && Kind(accessNode) == SyntaxKind.IndexedAccessType && ContainsMissingType(propType) ? GetUnionType([propType, undefinedType])
                    : propType;
            }
            if (EveryType(objectType, IsTupleType) && IsNumericLiteralName(propName))
            {
                var index = JsNumber.FromString(propName);
                if (!accessNode.IsNone && EveryType(objectType, t => (TupleTarget(t).CombinedFlags & ElementFlags.Variable) == 0) && (accessFlags & AccessFlags.AllowMissing) == 0)
                {
                    var indexNode = GetIndexNodeForAccessExpression(accessNode);
                    if (IsTupleType(objectType))
                    {
                        if (index < 0)
                        {
                            Error(indexNode, Diagnostics.A_tuple_type_cannot_be_indexed_with_a_negative_value);
                            return undefinedType;
                        }
                        Error(indexNode, Diagnostics.Tuple_type_0_of_length_1_has_no_element_at_index_2, TypeToString(objectType), GetTypeReferenceArity(objectType), Utilities.UnescapeLeadingUnderscores(propName));
                    }
                    else
                    {
                        Error(indexNode, Diagnostics.Property_0_does_not_exist_on_type_1, Utilities.UnescapeLeadingUnderscores(propName), TypeToString(objectType));
                    }
                }
                if (index >= 0)
                {
                    ErrorIfWritingToReadonlyIndex(GetIndexInfoOfType(objectType, numberType), accessExpression, accessFlags, objectType);
                    return GetTupleElementTypeOutOfStartCount(objectType, index, (accessFlags & AccessFlags.IncludeUndefined) != 0 ? missingType : Type.None);
                }
            }
        }
        if ((Types.Flags(indexType) & TypeFlags.Nullable) == 0 && IsTypeAssignableToKind(indexType, TypeFlags.StringLike | TypeFlags.NumberLike | TypeFlags.ESSymbolLike))
        {
            if ((Types.Flags(objectType) & (TypeFlags.Any | TypeFlags.Never)) != 0)
            {
                return objectType;
            }
            // If no index signature is applicable, we default to the string index signature. In effect, this means the string
            // index signature applies even when accessing with a symbol-like type.
            var indexInfo = GetApplicableIndexInfo(objectType, indexType) ?? GetIndexInfoOfType(objectType, stringType);
            if (indexInfo is not null)
            {
                if ((accessFlags & AccessFlags.NoIndexSignatures) != 0 && indexInfo.KeyType != numberType)
                {
                    if (!accessExpression.IsNone)
                    {
                        if ((accessFlags & AccessFlags.Writing) != 0)
                        {
                            Error(accessExpression, Diagnostics.Type_0_is_generic_and_can_only_be_indexed_for_reading, TypeToString(originalObjectType));
                        }
                        else
                        {
                            Error(accessExpression, Diagnostics.Type_0_cannot_be_used_to_index_type_1, TypeToString(indexType), TypeToString(originalObjectType));
                        }
                    }
                    return Type.None;
                }
                if (!accessNode.IsNone && indexInfo.KeyType == stringType && !IsTypeAssignableToKind(indexType, TypeFlags.String | TypeFlags.Number))
                {
                    var indexNode = GetIndexNodeForAccessExpression(accessNode);
                    Error(indexNode, Diagnostics.Type_0_cannot_be_used_as_an_index_type, TypeToString(indexType));
                    return (accessFlags & AccessFlags.IncludeUndefined) != 0 ? GetUnionType([indexInfo.Type, missingType]) : indexInfo.Type;
                }
                ErrorIfWritingToReadonlyIndex(indexInfo, accessExpression, accessFlags, objectType);
                if ((accessFlags & AccessFlags.IncludeUndefined) != 0)
                {
                    var objectSymbol = Types.Symbol(objectType);
                    var indexSymbol = Types.Symbol(indexType);
                    if (!(!objectSymbol.IsNone && (SymbolFlagsOf(objectSymbol) & (SymbolFlags.RegularEnum | SymbolFlags.ConstEnum)) != 0 &&
                        !indexSymbol.IsNone && (Types.Flags(indexType) & TypeFlags.EnumLiteral) != 0 && GetParentOfSymbol(indexSymbol) == objectSymbol))
                    {
                        return GetUnionType([indexInfo.Type, missingType]);
                    }
                }
                return indexInfo.Type;
            }
            if ((Types.Flags(indexType) & TypeFlags.Never) != 0)
            {
                return neverType;
            }
            if (IsJSLiteralType(objectType))
            {
                return anyType;
            }
            if (!accessExpression.IsNone && !IsConstEnumObjectType(objectType))
            {
                // the property and index diagnostics of element access expressions (module C5)
                ReportElementAccessIndexError(accessExpression, objectType, indexType, fullIndexType, propName, accessFlags, out var resultType);
                return resultType;
            }
        }
        if ((accessFlags & AccessFlags.AllowMissing) != 0 && IsObjectLiteralType(objectType))
        {
            return undefinedType;
        }
        if (IsJSLiteralType(objectType))
        {
            return anyType;
        }
        if (!accessNode.IsNone)
        {
            var indexNode = GetIndexNodeForAccessExpression(accessNode);
            var indexFlags = Types.Flags(indexType);
            if (Kind(indexNode) != SyntaxKind.BigIntLiteral && (indexFlags & (TypeFlags.StringLiteral | TypeFlags.NumberLiteral)) != 0)
            {
                var value = Types.Literal(indexType).Value;
                Error(indexNode, Diagnostics.Property_0_does_not_exist_on_type_1, value is double d ? JsNumber.ToString(d) : (string)value!, TypeToString(objectType));
            }
            else if ((indexFlags & (TypeFlags.String | TypeFlags.Number)) != 0)
            {
                Error(indexNode, Diagnostics.Type_0_has_no_matching_index_signature_for_type_1, TypeToString(objectType), TypeToString(indexType));
            }
            else
            {
                var typeString = Kind(indexNode) == SyntaxKind.BigIntLiteral ? "bigint" : TypeToString(indexType);
                Error(indexNode, Diagnostics.Type_0_cannot_be_used_as_an_index_type, typeString);
            }
        }
        if ((Types.Flags(indexType) & TypeFlags.Any) != 0)
        {
            return indexType;
        }
        return Type.None;
    }

    // checker.ts getIndexNodeForAccessExpression
    private NodeRef GetIndexNodeForAccessExpression(NodeRef accessNode)
    {
        var store = StoreOf(accessNode);
        return store.Kind(accessNode.Node) switch
        {
            SyntaxKind.ElementAccessExpression => At(accessNode, store.ArgumentExpression(accessNode.Node)),
            SyntaxKind.IndexedAccessType => At(accessNode, store.IndexType(accessNode.Node)),
            SyntaxKind.ComputedPropertyName => At(accessNode, store.Expression(accessNode.Node)),
            _ => accessNode,
        };
    }

    // checker.ts getPropertyNameFromIndex
    private string? GetPropertyNameFromIndex(Type indexType, NodeRef accessNode)
    {
        if (IsTypeUsableAsPropertyName(indexType))
        {
            return GetPropertyNameFromType(indexType);
        }
        if (!accessNode.IsNone && Kind(accessNode) is SyntaxKind.Identifier or SyntaxKind.StringLiteral or SyntaxKind.NumericLiteral or SyntaxKind.NoSubstitutionTemplateLiteral)
        {
            var store = StoreOf(accessNode);
            return store.Texts.GetString(Utilities.GetTextOfIdentifierOrLiteral(store, accessNode.Node));
        }
        return null;
    }

    // checker.ts containsMissingType
    private bool ContainsMissingType(Type type) =>
        type == missingType || ((Types.Flags(type) & TypeFlags.Union) != 0 && Types.Elements(Types.Union(type).Types)[0] == missingType);

    // checker.ts isStringIndexSignatureOnlyType
    private bool IsStringIndexSignatureOnlyType(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Object) != 0 && !IsGenericMappedType(type))
        {
            var resolved = ResolveStructuredTypeMembers(type);
            return resolved.Properties.Count == 0 && resolved.CallSignatures.Count == 0 && resolved.ConstructSignatures.Count == 0 &&
                resolved.IndexInfos.Count == 1 && Types.View(resolved.IndexInfos)[0].KeyType == stringType;
        }
        if ((flags & TypeFlags.UnionOrIntersection) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(type).Types).ToArray())
            {
                if (!IsStringIndexSignatureOnlyType(t))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    /// <summary>checker.ts getIndexInfoOfType.</summary>
    internal IndexInfo? GetIndexInfoOfType(Type type, Type keyType)
    {
        foreach (var info in GetIndexInfosOfType(type))
        {
            if (info.KeyType == keyType)
            {
                return info;
            }
        }
        return null;
    }

    /// <summary>checker.ts getIndexTypeOfType (None when there is none).</summary>
    internal Type GetIndexTypeOfType(Type type, Type keyType) => GetIndexInfoOfType(type, keyType)?.Type ?? Type.None;

    /// <summary>checker.ts getApplicableIndexInfo.</summary>
    internal IndexInfo? GetApplicableIndexInfo(Type type, Type keyType) => GetApplicableIndexInfoOf(GetIndexInfosOfType(type), keyType);

    // checker.ts getApplicableIndexInfo(infos, keyType)
    private IndexInfo? GetApplicableIndexInfoOf(Items<IndexInfo> indexInfos, Type keyType)
    {
        // (the union of several applicable infos is created by C5's element access)
        IndexInfo? stringIndexInfo = null;
        IndexInfo? applicableInfo = null;
        List<IndexInfo>? applicableInfos = null;
        foreach (var info in indexInfos)
        {
            if (info.KeyType == stringType)
            {
                stringIndexInfo = info;
            }
            else if (IsApplicableIndexType(keyType, info.KeyType))
            {
                if (applicableInfo is null)
                {
                    applicableInfo = info;
                }
                else
                {
                    (applicableInfos ??= [applicableInfo.Value]).Add(info);
                }
            }
        }
        // When more than one index signature is applicable we create a synthetic IndexInfo. Instead of computing
        // the intersected key type, we just use unknownType for the key type as nothing actually depends on the
        // keyType property of the returned IndexInfo.
        return applicableInfos is not null
            ? new IndexInfo { KeyType = unknownType, Type = GetIntersectionType(applicableInfos.ConvertAll(i => i.Type).ToArray()), IsReadonly = applicableInfos.TrueForAll(i => i.IsReadonly) }
            : applicableInfo ?? (stringIndexInfo is not null && IsApplicableIndexType(keyType, stringType) ? stringIndexInfo : null);
    }

    // checker.ts isApplicableIndexType
    private bool IsApplicableIndexType(Type source, Type target) =>
        // A 'string' index signature applies to types assignable to 'string' or 'number', and a 'number' index
        // signature applies to types assignable to 'number', `${number}` and numeric string literal types.
        IsTypeAssignableTo(source, target) ||
        (target == stringType && IsTypeAssignableTo(source, numberType)) ||
        (target == numberType && (source == numericStringType || ((Types.Flags(source) & TypeFlags.StringLiteral) != 0 && IsNumericLiteralName((string)Types.Literal(source).Value!))));

    // ---- simplification ----

    private readonly Dictionary<Type, Type> simplifiedForReading = [];
    private readonly Dictionary<Type, Type> simplifiedForWriting = [];

    /// <summary>checker.ts getSimplifiedType.</summary>
    internal Type GetSimplifiedType(Type type, bool writing)
    {
        var flags = Types.Flags(type);
        return (flags & TypeFlags.IndexedAccess) != 0 ? GetSimplifiedIndexedAccessType(type, writing)
            : (flags & TypeFlags.Conditional) != 0 ? GetSimplifiedConditionalType(type, writing)
            : (flags & TypeFlags.Index) != 0 ? GetSimplifiedIndexType(type)
            : type;
    }

    // checker.ts distributeIndexOverObjectType
    private Type DistributeIndexOverObjectType(Type objectType, Type indexType, bool writing)
    {
        // (T | U)[K] -> T[K] | U[K] (reading)
        // (T | U)[K] -> T[K] & U[K] (writing)
        // (T & U)[K] -> T[K] & U[K]
        var flags = Types.Flags(objectType);
        if ((flags & TypeFlags.Union) != 0 || ((flags & TypeFlags.Intersection) != 0 && !ShouldDeferIndexType(objectType)))
        {
            var types = Array.ConvertAll(Types.Elements(Types.Union(objectType).Types).ToArray(), t => GetSimplifiedType(GetIndexedAccessType(t, indexType), writing));
            return (flags & TypeFlags.Intersection) != 0 || writing ? GetIntersectionType(types) : GetUnionType(types);
        }
        return Type.None;
    }

    // checker.ts distributeObjectOverIndexType
    private Type DistributeObjectOverIndexType(Type objectType, Type indexType, bool writing)
    {
        // T[A | B] -> T[A] | T[B] (reading)
        // T[A | B] -> T[A] & T[B] (writing)
        if ((Types.Flags(indexType) & TypeFlags.Union) != 0)
        {
            var types = Array.ConvertAll(Types.Elements(Types.Union(indexType).Types).ToArray(), t => GetSimplifiedType(GetIndexedAccessType(objectType, t), writing));
            return writing ? GetIntersectionType(types) : GetUnionType(types);
        }
        return Type.None;
    }

    // checker.ts getSimplifiedIndexedAccessType
    private Type GetSimplifiedIndexedAccessType(Type type, bool writing)
    {
        var cache = writing ? simplifiedForWriting : simplifiedForReading;
        if (cache.TryGetValue(type, out var cached))
        {
            return cached == circularConstraintType ? type : cached;
        }
        cache[type] = circularConstraintType;
        var data = Types.Data<IndexedAccessData>(type);
        // We recursively simplify the object type as it may in turn be an indexed access type. For example, with
        // '{ [P in T]: { [Q in U]: number } }[T][U]' we want to first simplify the inner indexed access type.
        var objectType = GetSimplifiedType(data.ObjectType, writing);
        var indexType = GetSimplifiedType(data.IndexType, writing);
        // T[A | B] -> T[A] | T[B] (reading)
        // T[A | B] -> T[A] & T[B] (writing)
        var distributedOverIndex = DistributeObjectOverIndexType(objectType, indexType, writing);
        if (!distributedOverIndex.IsNone)
        {
            return cache[type] = distributedOverIndex;
        }
        // Only do the inner distributions if the index can no longer be instantiated to cause index distribution again
        if ((Types.Flags(indexType) & TypeFlags.Instantiable) == 0)
        {
            var distributedOverObject = DistributeIndexOverObjectType(objectType, indexType, writing);
            if (!distributedOverObject.IsNone)
            {
                return cache[type] = distributedOverObject;
            }
        }
        // A generic tuple type indexed by a number exists only when the index type doesn't select a
        // fixed element. We simplify to either the combined type of all elements (when the index type
        // the actual number type) or to the combined type of all non-fixed elements.
        if (IsGenericTupleType(objectType) && (Types.Flags(indexType) & TypeFlags.NumberLike) != 0)
        {
            var elementType = GetElementTypeOfSliceOfTupleType(objectType, (Types.Flags(indexType) & TypeFlags.Number) != 0 ? 0 : TupleTarget(objectType).FixedLength, 0, writing);
            if (!elementType.IsNone)
            {
                return cache[type] = elementType;
            }
        }
        // If the object type is a mapped type { [P in K]: E }, where K is generic, or { [P in K as N]: E }, where
        // K is generic and N is assignable to P, instantiate E using a mapper that substitutes the index type for P.
        // For example, for an index access { [P in K]: Box<T[P]> }[X], we construct the type Box<T[X]>.
        if (IsGenericMappedType(objectType))
        {
            if (GetMappedTypeNameTypeKind(objectType) != MappedTypeNameTypeKind.Remapping)
            {
                return cache[type] = MapType(SubstituteIndexedMappedType(objectType, data.IndexType), t => GetSimplifiedType(t, writing));
            }
        }
        return cache[type] = type;
    }

    // Results of getSimplifiedConditionalType per (type, writing). TypeScript recomputes them; the result depends only on
    // the types and the relation caches, so a cache leaves the results unchanged and keeps nested conditional types
    // (whose simplification relates their check types, which simplify the next level again) from exponential work.
    private readonly Dictionary<(Type, bool), Type> simplifiedConditionalTypes = [];

    // checker.ts getSimplifiedConditionalType
    private Type GetSimplifiedConditionalType(Type type, bool writing)
    {
        if (simplifiedConditionalTypes.TryGetValue((type, writing), out var cached))
        {
            return cached;
        }
        var result = GetSimplifiedConditionalTypeWorker(type, writing);
        simplifiedConditionalTypes[(type, writing)] = result;
        return result;
    }

    private Type GetSimplifiedConditionalTypeWorker(Type type, bool writing)
    {
        var data = Types.Data<ConditionalData>(type);
        var checkType = data.CheckType;
        var extendsType = data.ExtendsType;
        var trueType = GetTrueTypeFromConditionalType(type);
        var falseType = GetFalseTypeFromConditionalType(type);
        // Simplifications for types of the form `T extends U ? T : never` and `T extends U ? never : T`.
        if ((Types.Flags(falseType) & TypeFlags.Never) != 0 && GetActualTypeVariable(trueType) == GetActualTypeVariable(checkType))
        {
            if ((Types.Flags(checkType) & TypeFlags.Any) != 0 || IsTypeAssignableTo(GetRestrictiveInstantiation(checkType), GetRestrictiveInstantiation(extendsType)))
            {
                // Always true
                return GetSimplifiedType(trueType, writing);
            }
            if (IsIntersectionEmpty(checkType, extendsType))
            {
                // Always false
                return neverType;
            }
        }
        else if ((Types.Flags(trueType) & TypeFlags.Never) != 0 && GetActualTypeVariable(falseType) == GetActualTypeVariable(checkType))
        {
            if ((Types.Flags(checkType) & TypeFlags.Any) == 0 && IsTypeAssignableTo(GetRestrictiveInstantiation(checkType), GetRestrictiveInstantiation(extendsType)))
            {
                // Always true
                return neverType;
            }
            if ((Types.Flags(checkType) & TypeFlags.Any) != 0 || IsIntersectionEmpty(checkType, extendsType))
            {
                // Always false
                return GetSimplifiedType(falseType, writing);
            }
        }
        return type;
    }

    // checker.ts getSimplifiedIndexType
    private Type GetSimplifiedIndexType(Type type)
    {
        var target = Types.Data<IndexData>(type).Type;
        if (IsGenericMappedType(target) && !GetNameTypeFromMappedType(target).IsNone && !IsMappedTypeWithKeyofConstraintDeclaration(target))
        {
            return GetIndexTypeForMappedType(target, IndexFlags.None);
        }
        return type;
    }

    // checker.ts isIntersectionEmpty
    private bool IsIntersectionEmpty(Type type1, Type type2) =>
        (Types.Flags(GetUnionType([GetIntersectionType([type1, type2]), neverType])) & TypeFlags.Never) != 0;

    /// <summary>checker.ts getActualTypeVariable.</summary>
    internal Type GetActualTypeVariable(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Substitution) != 0)
        {
            return GetActualTypeVariable(Types.Data<SubstitutionData>(type).BaseType);
        }
        if ((flags & TypeFlags.IndexedAccess) != 0)
        {
            var data = Types.Data<IndexedAccessData>(type);
            if ((Types.Flags(data.ObjectType) & TypeFlags.Substitution) != 0 || (Types.Flags(data.IndexType) & TypeFlags.Substitution) != 0)
            {
                return GetIndexedAccessType(GetActualTypeVariable(data.ObjectType), GetActualTypeVariable(data.IndexType));
            }
        }
        return type;
    }

    // ---- substitution and NoInfer types ----

    private readonly Dictionary<long, Type> substitutionTypes = []; // (module CO2: `${baseType.id}>${constraint.id}` as the two ids)

    /// <summary>checker.ts getSubstitutionType.</summary>
    internal Type GetSubstitutionType(Type baseType, Type constraint) =>
        (Types.Flags(constraint) & TypeFlags.AnyOrUnknown) != 0 || constraint == baseType || (Types.Flags(baseType) & TypeFlags.Any) != 0 ? baseType : GetOrCreateSubstitutionType(baseType, constraint);

    private Type GetOrCreateSubstitutionType(Type baseType, Type constraint)
    {
        var id = ((long)baseType.Id << 32) | (uint)constraint.Id;
        if (!substitutionTypes.TryGetValue(id, out var result))
        {
            substitutionTypes[id] = result = Types.CreateWithData(TypeFlags.Substitution, new SubstitutionData { BaseType = baseType, Constraint = constraint });
        }
        return result;
    }

    /// <summary>checker.ts getNoInferType.</summary>
    internal Type GetNoInferType(Type type) => IsNoInferTargetType(type) ? GetOrCreateSubstitutionType(type, unknownType) : type;

    // checker.ts isNoInferTargetType
    private bool IsNoInferTargetType(Type type)
    {
        var flags = Types.Flags(type);
        // This is effectively a more conservative and predictable form of couldContainTypeVariables. We want to
        // preserve NoInfer<T> only for types that could contain type variables, but we don't want to exhaustively
        // examine all object type members.
        return ((flags & TypeFlags.UnionOrIntersection) != 0 && ExistsType(Types.Elements(Types.Union(type).Types), IsNoInferTargetType)) ||
            ((flags & TypeFlags.Substitution) != 0 && !IsNoInferType(type) && IsNoInferTargetType(Types.Data<SubstitutionData>(type).BaseType)) ||
            ((flags & TypeFlags.Object) != 0 && !IsEmptyAnonymousObjectType(type)) ||
            ((flags & (TypeFlags.Instantiable & ~TypeFlags.Substitution)) != 0 && !IsPatternLiteralType(type));
    }

    /// <summary>checker.ts isNoInferType.</summary>
    internal bool IsNoInferType(Type type) =>
        // A NoInfer<T> type is represented as a substitution type with a TypeFlags.Unknown constraint.
        (Types.Flags(type) & TypeFlags.Substitution) != 0 && (Types.Flags(Types.Data<SubstitutionData>(type).Constraint) & TypeFlags.Unknown) != 0;

    // checker.ts getSubstitutionIntersection
    private Type GetSubstitutionIntersection(Type substitutionType)
    {
        var data = Types.Data<SubstitutionData>(substitutionType);
        return IsNoInferType(substitutionType) ? data.BaseType : GetIntersectionType([data.Constraint, data.BaseType]);
    }

    // checker.ts getBuiltinIteratorReturnType
    private Type GetBuiltinIteratorReturnType() =>
        compilerOptions.GetStrictOptionValue(compilerOptions.StrictBuiltinIteratorReturn) ? undefinedType : anyType;

    // ---- constraints ----

    /// <summary>checker.ts getConstraintOfType.</summary>
    internal Type GetConstraintOfType(Type type)
    {
        var flags = Types.Flags(type);
        return (flags & TypeFlags.TypeParameter) != 0 ? GetConstraintOfTypeParameter(type)
            : (flags & TypeFlags.IndexedAccess) != 0 ? GetConstraintOfIndexedAccess(type)
            : (flags & TypeFlags.Conditional) != 0 ? GetConstraintOfConditionalType(type)
            : GetBaseConstraintOfType(type);
    }

    // checker.ts getConstraintOfIndexedAccess
    private Type GetConstraintOfIndexedAccess(Type type) => HasNonCircularBaseConstraint(type) ? GetConstraintFromIndexedAccess(type) : Type.None;

    // checker.ts getSimplifiedTypeOrConstraint
    private Type GetSimplifiedTypeOrConstraint(Type type)
    {
        var simplified = GetSimplifiedType(type, writing: false);
        return simplified != type ? simplified : GetConstraintOfType(type);
    }

    // checker.ts getConstraintFromIndexedAccess
    private Type GetConstraintFromIndexedAccess(Type type)
    {
        var data = Types.Data<IndexedAccessData>(type);
        if (IsMappedTypeGenericIndexedAccess(type))
        {
            // For indexed access types of the form { [P in K]: E }[X], where K is non-generic and X is generic,
            // we substitute an instantiation of E where P is replaced with X.
            return SubstituteIndexedMappedType(data.ObjectType, data.IndexType);
        }
        var indexConstraint = GetSimplifiedTypeOrConstraint(data.IndexType);
        if (!indexConstraint.IsNone && indexConstraint != data.IndexType)
        {
            var indexedAccess = GetIndexedAccessTypeOrUndefined(data.ObjectType, indexConstraint, data.AccessFlags);
            if (!indexedAccess.IsNone)
            {
                return indexedAccess;
            }
        }
        var objectConstraint = GetSimplifiedTypeOrConstraint(data.ObjectType);
        if (!objectConstraint.IsNone && objectConstraint != data.ObjectType)
        {
            return GetIndexedAccessTypeOrUndefined(objectConstraint, data.IndexType, data.AccessFlags);
        }
        return Type.None;
    }

    // checker.ts isMappedTypeGenericIndexedAccess
    private bool IsMappedTypeGenericIndexedAccess(Type type)
    {
        if ((Types.Flags(type) & TypeFlags.IndexedAccess) == 0)
        {
            return false;
        }
        var data = Types.Data<IndexedAccessData>(type);
        var objectType = data.ObjectType;
        return (Types.ObjectFlagsOf(objectType) & ObjectFlags.Mapped) != 0 && !IsGenericMappedType(objectType) && IsGenericIndexType(data.IndexType) &&
            (GetMappedTypeModifiers(objectType) & MappedTypeModifiers.ExcludeOptional) == 0 && StoreOf(Types.Object(objectType).Declaration).NameType(Types.Object(objectType).Declaration.Node).IsNone;
    }

    /// <summary>checker.ts getBaseConstraintOfType (None when there is none).</summary>
    internal Type GetBaseConstraintOfType(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & (TypeFlags.InstantiableNonPrimitive | TypeFlags.UnionOrIntersection | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 || IsGenericTupleType(type))
        {
            var constraint = GetResolvedBaseConstraint(type);
            return constraint != noConstraintType && constraint != circularConstraintType ? constraint : Type.None;
        }
        return (flags & TypeFlags.Index) != 0 ? stringNumberSymbolType : Type.None;
    }

    /// <summary>checker.ts getBaseConstraintOrType.</summary>
    internal Type GetBaseConstraintOrType(Type type)
    {
        var constraint = GetBaseConstraintOfType(type);
        return constraint.IsNone ? type : constraint;
    }

    // checker.ts hasNonCircularBaseConstraint
    private bool HasNonCircularBaseConstraint(Type type) => GetResolvedBaseConstraint(type) != circularConstraintType;

    private readonly Dictionary<Type, Type> resolvedBaseConstraints = [];
    private readonly Dictionary<Type, Type> immediateBaseConstraints = [];
    private const int ImmediateBaseConstraintProperty = 5;

    /// <summary>
    /// checker.ts getResolvedBaseConstraint: noConstraintType when the type variable has no constraint,
    /// circularConstraintType when the constraint circularly references the type variable.
    /// </summary>
    internal Type GetResolvedBaseConstraint(Type type) =>
        resolvedBaseConstraints.TryGetValue(type, out var resolved) ? resolved : GetResolvedBaseConstraintWorker(type);

    // (the cached case above without the nested functions' closure, which is created at the entry of the method whose
    // locals it captures; Track O)
    private Type GetResolvedBaseConstraintWorker(Type type)
    {
        var stack = new List<object>();
        var result = GetImmediateBaseConstraint(type);
        resolvedBaseConstraints[type] = result;
        return result;

        Type GetImmediateBaseConstraint(Type t)
        {
            if (!immediateBaseConstraints.TryGetValue(t, out var immediate))
            {
                if (!PushTypeResolution(t, ImmediateBaseConstraintProperty))
                {
                    return circularConstraintType;
                }
                var computed = Type.None;
                // We always explore at least 10 levels of nested constraints. Thereafter, we continue to explore
                // up to 50 levels of nested constraints provided there are no "deeply nested" types on the stack
                // (i.e. no types for which five instantiations have been recorded on the stack). If we reach 50
                // levels of nesting, we are presumably exploring a repeating pattern with a long cycle that hasn't
                // yet triggered the deeply nested limiter.
                var identity = GetRecursionIdentity(t);
                if (stack.Count < 10 || (stack.Count < 50 && !stack.Contains(identity)))
                {
                    stack.Add(identity);
                    computed = ComputeBaseConstraint(GetSimplifiedType(t, writing: false));
                    stack.RemoveAt(stack.Count - 1);
                }
                if (!PopTypeResolution())
                {
                    if ((Types.Flags(t) & TypeFlags.TypeParameter) != 0)
                    {
                        var errorNode = GetConstraintDeclaration(t);
                        if (!errorNode.IsNone)
                        {
                            var diagnostic = Error(errorNode, Diagnostics.Type_parameter_0_has_a_circular_constraint, TypeToString(t));
                            if (!currentNode.IsNone && !(errorNode.File == currentNode.File && (IsNodeDescendantOf(StoreOf(errorNode), errorNode.Node, currentNode.Node) || IsNodeDescendantOf(StoreOf(currentNode), currentNode.Node, errorNode.Node))))
                            {
                                AddRelatedInfo(diagnostic, CreateRelatedForNode(currentNode, Diagnostics.Circularity_originates_in_type_at_this_location));
                            }
                        }
                    }
                    computed = circularConstraintType;
                }
                if (!immediateBaseConstraints.TryGetValue(t, out immediate))
                {
                    immediate = computed.IsNone ? noConstraintType : computed;
                    immediateBaseConstraints[t] = immediate;
                }
            }
            return immediate;
        }

        Type GetBaseConstraint(Type t)
        {
            var c = GetImmediateBaseConstraint(t);
            return c != noConstraintType && c != circularConstraintType ? c : Type.None;
        }

        Type ComputeBaseConstraint(Type t)
        {
            var flags = Types.Flags(t);
            if ((flags & TypeFlags.TypeParameter) != 0)
            {
                var constraint = GetConstraintFromTypeParameter(t);
                return Types.TypeParameter(t).IsThisType || constraint.IsNone ? constraint : GetBaseConstraint(constraint);
            }
            if ((flags & TypeFlags.UnionOrIntersection) != 0)
            {
                var types = Types.Elements(Types.Union(t).Types).ToArray();
                var baseTypes = new List<Type>();
                var different = false;
                foreach (var type in types)
                {
                    var baseType = GetBaseConstraint(type);
                    if (!baseType.IsNone)
                    {
                        if (baseType != type)
                        {
                            different = true;
                        }
                        baseTypes.Add(baseType);
                    }
                    else
                    {
                        different = true;
                    }
                }
                if (!different)
                {
                    return t;
                }
                return (flags & TypeFlags.Union) != 0 && baseTypes.Count == types.Length ? GetUnionType(baseTypes.ToArray())
                    : (flags & TypeFlags.Intersection) != 0 && baseTypes.Count > 0 ? GetIntersectionType(baseTypes.ToArray())
                    : Type.None;
            }
            if ((flags & TypeFlags.Index) != 0)
            {
                var target = Types.Data<IndexData>(t).Type;
                if (IsGenericMappedType(target))
                {
                    if (!GetNameTypeFromMappedType(target).IsNone && !IsMappedTypeWithKeyofConstraintDeclaration(target))
                    {
                        return GetBaseConstraint(GetIndexTypeForMappedType(target, IndexFlags.None));
                    }
                }
                return stringNumberSymbolType;
            }
            if ((flags & TypeFlags.TemplateLiteral) != 0)
            {
                var data = Types.Data<TemplateLiteralData>(t);
                var constraints = new List<Type>();
                foreach (var type in data.Types)
                {
                    var c = GetBaseConstraint(type);
                    if (!c.IsNone)
                    {
                        constraints.Add(c);
                    }
                }
                return constraints.Count == data.Types.Length ? GetTemplateLiteralType(data.Texts, constraints.ToArray()) : stringType;
            }
            if ((flags & TypeFlags.StringMapping) != 0)
            {
                var inner = Types.Data<StringMappingData>(t).Type;
                var constraint = GetBaseConstraint(inner);
                return !constraint.IsNone && constraint != inner ? GetStringMappingType(Types.Symbol(t), constraint) : stringType;
            }
            if ((flags & TypeFlags.IndexedAccess) != 0)
            {
                var data = Types.Data<IndexedAccessData>(t);
                if (IsMappedTypeGenericIndexedAccess(t))
                {
                    // For indexed access types of the form { [P in K]: E }[X], where K is non-generic and X is generic,
                    // we substitute an instantiation of E where P is replaced with X.
                    return GetBaseConstraint(SubstituteIndexedMappedType(data.ObjectType, data.IndexType));
                }
                var baseObjectType = GetBaseConstraint(data.ObjectType);
                var baseIndexType = GetBaseConstraint(data.IndexType);
                var baseIndexedAccess = !baseObjectType.IsNone && !baseIndexType.IsNone ? GetIndexedAccessTypeOrUndefined(baseObjectType, baseIndexType, data.AccessFlags) : Type.None;
                return baseIndexedAccess.IsNone ? Type.None : GetBaseConstraint(baseIndexedAccess);
            }
            if ((flags & TypeFlags.Conditional) != 0)
            {
                var constraint = GetConstraintFromConditionalType(t);
                return constraint.IsNone ? Type.None : GetBaseConstraint(constraint);
            }
            if ((flags & TypeFlags.Substitution) != 0)
            {
                return GetBaseConstraint(GetSubstitutionIntersection(t));
            }
            if (IsGenericTupleType(t))
            {
                // We substitute constraints for variadic elements only when the constraints are array types or
                // non-variadic tuple types as we want to avoid further (possibly unbounded) recursion.
                var target = TupleTarget(t);
                var elements = GetElementTypes(t);
                var newElements = new Type[elements.Length];
                for (var i = 0; i < elements.Length; i++)
                {
                    var v = elements[i];
                    var constraint = (Types.Flags(v) & TypeFlags.TypeParameter) != 0 && (target.ElementFlags![i] & ElementFlags.Variadic) != 0 ? GetBaseConstraint(v) : Type.None;
                    if (constraint.IsNone)
                    {
                        constraint = v;
                    }
                    newElements[i] = constraint != v && EveryType(constraint, c => IsArrayOrTupleType(c) && !IsGenericTupleType(c)) ? constraint : v;
                }
                return CreateTupleType(newElements, target.ElementFlags, target.Readonly, target.LabeledElementDeclarations);
            }
            return t;
        }
    }

    // checker.ts getRecursionIdentity
    private object GetRecursionIdentity(Type type)
    {
        // Object and array literals are known not to contain recursive references and don't need a recursion identity.
        if ((Types.Flags(type) & TypeFlags.Object) != 0 && !IsObjectOrArrayLiteralType(type))
        {
            var objectFlags = Types.ObjectFlagsOf(type);
            if ((objectFlags & ObjectFlags.Reference) != 0 && !Types.Object(type).Node.IsNone)
            {
                // Deferred type references are tracked through their associated AST node. This gives us finer
                // granularity than using their associated target because each manifest type reference has a
                // unique AST node.
                return Types.Object(type).Node;
            }
            var symbol = Types.Symbol(type);
            if (!symbol.IsNone && !((objectFlags & ObjectFlags.Anonymous) != 0 && (SymbolFlagsOf(symbol) & SymbolFlags.Class) != 0))
            {
                // We track object types that have a symbol by that symbol (representing the origin of the type), but
                // exclude the static side of a class since it shares its symbol with the instance side.
                return symbol;
            }
            if (IsTupleType(type))
            {
                return Types.Object(type).Target;
            }
        }
        if ((Types.Flags(type) & TypeFlags.TypeParameter) != 0)
        {
            // We use the symbol of the type parameter such that all "fresh" instantiations of that type parameter
            // have the same recursion identity.
            return Types.Symbol(type);
        }
        if ((Types.Flags(type) & TypeFlags.IndexedAccess) != 0)
        {
            // Identity is the leftmost object type in a chain of indexed accesses, eg, in A[P][Q] it is A
            var t = type;
            do
            {
                t = Types.Data<IndexedAccessData>(t).ObjectType;
            }
            while ((Types.Flags(t) & TypeFlags.IndexedAccess) != 0);
            return t;
        }
        if ((Types.Flags(type) & TypeFlags.Conditional) != 0)
        {
            // The root object represents the origin of the conditional type
            return Types.Data<ConditionalData>(type).Root;
        }
        return type;
    }

    // checker.ts isObjectOrArrayLiteralType
    private bool IsObjectOrArrayLiteralType(Type type) =>
        (Types.ObjectFlagsOf(type) & (ObjectFlags.ObjectLiteral | ObjectFlags.ArrayLiteral)) != 0;

    private static void AddRelatedInfo(ProgramDiagnostic diagnostic, ProgramRelatedInformation related)
    {
        diagnostic.RelatedInformation ??= [];
        diagnostic.RelatedInformation.Add(related);
    }

    // ---- apparent and reduced types ----

    /// <summary>
    /// checker.ts getApparentType: for a type parameter, the base constraint; for the primitive types, the
    /// corresponding object types; otherwise the type itself.
    /// </summary>
    internal Type GetApparentType(Type type)
    {
        var t = type;
        if ((Types.Flags(type) & TypeFlags.Instantiable) != 0)
        {
            var constraint = GetBaseConstraintOfType(type);
            t = constraint.IsNone ? unknownType : constraint;
        }
        var objectFlags = Types.ObjectFlagsOf(t);
        var flags = Types.Flags(t);
        return (objectFlags & ObjectFlags.Mapped) != 0 ? GetApparentTypeOfMappedType(t)
            : (objectFlags & ObjectFlags.Reference) != 0 && t != type ? GetTypeWithThisArgument(t, type)
            : (flags & TypeFlags.Intersection) != 0 ? GetApparentTypeOfIntersectionType(t, type)
            : (flags & TypeFlags.StringLike) != 0 ? globalStringType
            : (flags & TypeFlags.NumberLike) != 0 ? globalNumberType
            : (flags & TypeFlags.BigIntLike) != 0 ? GetGlobalBigIntType()
            : (flags & TypeFlags.BooleanLike) != 0 ? globalBooleanType
            : (flags & TypeFlags.ESSymbolLike) != 0 ? GetGlobalESSymbolType()
            : (flags & TypeFlags.NonPrimitive) != 0 ? emptyObjectType
            : (flags & TypeFlags.Index) != 0 ? stringNumberSymbolType
            : (flags & TypeFlags.Unknown) != 0 && !strictNullChecks ? emptyObjectType
            : t;
    }

    private readonly Dictionary<Type, Type> resolvedApparentTypesOfIntersections = [];
    private readonly Dictionary<(Type, Type), Type> apparentTypesOfIntersectionsWithThis = [];

    // checker.ts getApparentTypeOfIntersectionType
    private Type GetApparentTypeOfIntersectionType(Type type, Type thisArgument)
    {
        if (type == thisArgument)
        {
            if (!resolvedApparentTypesOfIntersections.TryGetValue(type, out var resolved))
            {
                resolvedApparentTypesOfIntersections[type] = resolved = GetTypeWithThisArgument(type, thisArgument, needApparentType: true);
            }
            return resolved;
        }
        if (!apparentTypesOfIntersectionsWithThis.TryGetValue((type, thisArgument), out var result))
        {
            apparentTypesOfIntersectionsWithThis[(type, thisArgument)] = result = GetTypeWithThisArgument(type, thisArgument, needApparentType: true);
        }
        return result;
    }

    // checker.ts getReducedApparentType
    private Type GetReducedApparentType(Type type) =>
        // Since getApparentType may return a non-reduced union or intersection type, we need to perform
        // type reduction both before and after obtaining the apparent type. For example, given a type parameter
        // 'T extends A | B', the type 'T & X' becomes 'A & X | B & X' after obtaining the apparent type, and
        // that type may need further reduction to remove empty intersections.
        GetReducedType(GetApparentType(GetReducedType(type)));

    private readonly Dictionary<Type, Type> resolvedReducedTypes = [];

    /// <summary>
    /// checker.ts getReducedType: for a union type, a union of the normalized constituent types; for an intersection
    /// of types containing one or more mutually exclusive discriminant properties, never.
    /// </summary>
    internal Type GetReducedType(Type type)
    {
        if ((Types.Flags(type) & TypeFlags.Union) != 0 && (Types.ObjectFlagsOf(type) & ObjectFlags.ContainsIntersections) != 0)
        {
            if (!resolvedReducedTypes.TryGetValue(type, out var reduced))
            {
                reduced = GetReducedUnionType(type);
                resolvedReducedTypes[type] = reduced;
            }
            return reduced;
        }
        if ((Types.Flags(type) & TypeFlags.Intersection) != 0)
        {
            if ((Types.ObjectFlagsOf(type) & ObjectFlags.IsNeverIntersectionComputed) == 0)
            {
                var isNever = false;
                foreach (var prop in GetPropertiesOfUnionOrIntersectionType(type))
                {
                    if (IsNeverReducedProperty(prop))
                    {
                        isNever = true;
                        break;
                    }
                }
                Types.Record(type).ObjectFlags |= ObjectFlags.IsNeverIntersectionComputed | (isNever ? ObjectFlags.IsNeverIntersection : 0);
            }
            return (Types.ObjectFlagsOf(type) & ObjectFlags.IsNeverIntersection) != 0 ? neverType : type;
        }
        return type;
    }

    // checker.ts getReducedUnionType
    private Type GetReducedUnionType(Type unionType)
    {
        var types = Types.Elements(Types.Union(unionType).Types).ToArray();
        var reducedTypes = new Type[types.Length];
        var changed = false;
        for (var i = 0; i < types.Length; i++)
        {
            reducedTypes[i] = GetReducedType(types[i]);
            changed |= reducedTypes[i] != types[i];
        }
        if (!changed)
        {
            return unionType;
        }
        var reduced = GetUnionType(reducedTypes);
        if ((Types.Flags(reduced) & TypeFlags.Union) != 0)
        {
            resolvedReducedTypes[reduced] = reduced;
        }
        return reduced;
    }

    // ---- helpers over unions ----

    // The union helpers read the union's stored member list in place (the type store's list arena is append-only, so the
    // span stays what it was while a callback creates types; tsrs returns the stored slice, notes/cpu-checker.md changes
    // 7 and 8; Track O).

    /// <summary>checker.ts everyType.</summary>
    internal bool EveryType(Type type, Func<Type, bool> f)
    {
        if ((Types.Flags(type) & TypeFlags.Union) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(type).Types))
            {
                if (!f(t))
                {
                    return false;
                }
            }
            return true;
        }
        return f(type);
    }

    /// <summary>
    /// A callback over types as a struct (Track O; tsrs's callbacks are <c>impl FnMut</c> generics, monomorphized per
    /// caller: notes/monomorphization-audit.md): generic methods over it allocate no delegate and no closure.
    /// </summary>
    internal interface ITypePredicate
    {
        bool Invoke(Type t);
    }

    /// <summary>checker.ts everyType with a struct callback.</summary>
    internal bool EveryType<TPredicate>(Type type, TPredicate f)
        where TPredicate : struct, ITypePredicate
    {
        if ((Types.Flags(type) & TypeFlags.Union) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(type).Types))
            {
                if (!f.Invoke(t))
                {
                    return false;
                }
            }
            return true;
        }
        return f.Invoke(type);
    }

    /// <summary>checker.ts some over a list with a struct callback.</summary>
    internal static bool ExistsType<TPredicate>(ReadOnlySpan<Type> types, TPredicate f)
        where TPredicate : struct, ITypePredicate
    {
        foreach (var t in types)
        {
            if (f.Invoke(t))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>checker.ts someType with a struct callback.</summary>
    internal bool SomeType<TPredicate>(Type type, TPredicate f)
        where TPredicate : struct, ITypePredicate =>
        (Types.Flags(type) & TypeFlags.Union) != 0 ? ExistsType(Types.Elements(Types.Union(type).Types), f) : f.Invoke(type);

    /// <summary>checker.ts someType.</summary>
    internal bool SomeType(Type type, Func<Type, bool> f) =>
        (Types.Flags(type) & TypeFlags.Union) != 0 ? ExistsType(Types.Elements(Types.Union(type).Types), f) : f(type);

    /// <summary>checker.ts forEachType (without a result).</summary>
    internal void ForEachType(Type type, Action<Type> f)
    {
        if ((Types.Flags(type) & TypeFlags.Union) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(type).Types))
            {
                f(t);
            }
        }
        else
        {
            f(type);
        }
    }

    /// <summary>checker.ts filterType.</summary>
    internal Type FilterType(Type type, Func<Type, bool> f)
    {
        if ((Types.Flags(type) & TypeFlags.Union) != 0)
        {
            var types = Types.Elements(Types.Union(type).Types);
            // (Array.FindAll as a loop over the stored list: no copy and no wrapper closure, Track O)
            var filtered = new List<Type>(types.Length);
            foreach (var t in types)
            {
                if (f(t))
                {
                    filtered.Add(t);
                }
            }
            if (filtered.Count == types.Length)
            {
                return type;
            }
            // module C7: the origin of the filtered union
            var origin = Types.Union(type).Origin;
            var newOrigin = Type.None;
            if (!origin.IsNone && (Types.Flags(origin) & TypeFlags.Union) != 0)
            {
                // If the origin type is a (denormalized) union type, filter its non-union constituents. If that ends
                // up removing a smaller number of types than in the normalized constituent set (meaning some of the
                // filtered types are within nested unions in the origin), then we can't construct a new origin type.
                // Otherwise, if we have exactly one type left in the origin set, return that as the filtered type.
                // Otherwise, construct a new filtered origin type.
                var originTypes = Types.Elements(Types.Union(origin).Types);
                var originFiltered = new List<Type>(originTypes.Length);
                foreach (var t in originTypes)
                {
                    if ((Types.Flags(t) & TypeFlags.Union) != 0 || f(t))
                    {
                        originFiltered.Add(t);
                    }
                }
                if (originTypes.Length - originFiltered.Count == types.Length - filtered.Count)
                {
                    if (originFiltered.Count == 1)
                    {
                        return originFiltered[0];
                    }
                    newOrigin = CreateOriginUnionOrIntersectionType(TypeFlags.Union, originFiltered);
                }
            }
            // filtering could remove intersections so `ContainsIntersections` might be forwarded "incorrectly"
            // it is purely an optimization hint so there is no harm in accidentally forwarding it
            return GetUnionTypeFromSortedList(filtered, Types.ObjectFlagsOf(type) & (ObjectFlags.PrimitiveUnion | ObjectFlags.ContainsIntersections), default, default, newOrigin);
        }
        return (Types.Flags(type) & TypeFlags.Never) != 0 || f(type) ? type : neverType;
    }

    // checker.ts mapTypeWithAlias
    private Type MapTypeWithAlias(Type type, Func<Type, Type> mapper, SymbolRef aliasSymbol, TypeList aliasTypeArguments)
    {
        if ((Types.Flags(type) & TypeFlags.Union) != 0 && !aliasSymbol.IsNone)
        {
            var types = Types.Elements(Types.Union(type).Types);
            var mapped = new Type[types.Length];
            for (var i = 0; i < types.Length; i++)
            {
                mapped[i] = mapper(types[i]);
            }
            return GetUnionType(mapped, UnionReduction.Literal, aliasSymbol, aliasTypeArguments);
        }
        return MapType(type, mapper);
    }

    // (isTypeAssignableTo and the other relations: Checker.Relations.cs, module C4)

    /// <summary>checker.ts isTypeAssignableToKind.</summary>
    internal bool IsTypeAssignableToKind(Type source, TypeFlags kind, bool strict = false)
    {
        var flags = Types.Flags(source);
        if ((flags & kind) != 0)
        {
            return true;
        }
        if (strict && (flags & (TypeFlags.AnyOrUnknown | TypeFlags.Void | TypeFlags.Undefined | TypeFlags.Null)) != 0)
        {
            return false;
        }
        return ((kind & TypeFlags.NumberLike) != 0 && IsTypeAssignableTo(source, numberType)) ||
            ((kind & TypeFlags.BigIntLike) != 0 && IsTypeAssignableTo(source, bigintType)) ||
            ((kind & TypeFlags.StringLike) != 0 && IsTypeAssignableTo(source, stringType)) ||
            ((kind & TypeFlags.BooleanLike) != 0 && IsTypeAssignableTo(source, booleanType)) ||
            ((kind & TypeFlags.Void) != 0 && IsTypeAssignableTo(source, voidType)) ||
            ((kind & TypeFlags.Never) != 0 && IsTypeAssignableTo(source, neverType)) ||
            ((kind & TypeFlags.Null) != 0 && IsTypeAssignableTo(source, nullType)) ||
            ((kind & TypeFlags.Undefined) != 0 && IsTypeAssignableTo(source, undefinedType)) ||
            ((kind & TypeFlags.ESSymbol) != 0 && IsTypeAssignableTo(source, esSymbolType)) ||
            ((kind & TypeFlags.NonPrimitive) != 0 && IsTypeAssignableTo(source, nonPrimitiveType));
    }
}
