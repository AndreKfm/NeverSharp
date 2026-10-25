// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (mapped types: the accessors, resolveMappedTypeMembers, getTypeOfMappedSymbol,
// instantiateMappedType; conditional types: getConditionalType, getConditionalTypeInstantiation, their constraints)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C3. Stub: the inference of `infer` type parameters (C6); without
// inference candidates an infer type parameter maps to its instantiated constraint or unknown, as getInferredType
// does when nothing was inferred.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>checker.ts MappedTypeModifiers.</summary>
[Flags]
internal enum MappedTypeModifiers
{
    None = 0,
    IncludeReadonly = 1 << 0,
    ExcludeReadonly = 1 << 1,
    IncludeOptional = 1 << 2,
    ExcludeOptional = 1 << 3,
}

/// <summary>checker.ts MappedTypeNameTypeKind.</summary>
internal enum MappedTypeNameTypeKind
{
    None,
    Filtering,
    Remapping,
}

internal sealed partial class Checker
{
    // MappedSymbol links: mappedType, keyType, syntheticOrigin
    private readonly Dictionary<SymbolRef, (Type MappedType, Type KeyType, SymbolRef SyntheticOrigin)> mappedSymbolLinks = [];

    // ---- mapped types ----

    // checker.ts getTypeFromMappedTypeNode
    private Type GetTypeFromMappedTypeNode(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        var type = CreateObjectType(ObjectFlags.Mapped, NodeSymbol(node));
        Types.Object(type).Declaration = node;
        var aliasSymbol = GetAliasSymbolForTypeNode(node);
        ref var record = ref Types.Record(type);
        record.AliasSymbol = aliasSymbol;
        record.AliasTypeArguments = GetTypeArgumentsForAliasSymbol(aliasSymbol);
        SetCachedNodeType(node, type);
        // Eagerly resolve the constraint type which forces an error if the constraint type circularly
        // references itself through one or more type aliases.
        GetConstraintTypeFromMappedType(type);
        return GetCachedNodeType(node);
    }

    /// <summary>checker.ts getTypeParameterFromMappedType.</summary>
    internal Type GetTypeParameterFromMappedType(Type type)
    {
        var data = Types.Object(type);
        if (data.MappedTypeParameter.IsNone)
        {
            data.MappedTypeParameter = GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(At(data.Declaration, StoreOf(data.Declaration).TypeParameter(data.Declaration.Node))));
        }
        return data.MappedTypeParameter;
    }

    /// <summary>checker.ts getConstraintTypeFromMappedType.</summary>
    internal Type GetConstraintTypeFromMappedType(Type type)
    {
        var data = Types.Object(type);
        if (data.ConstraintType.IsNone)
        {
            var constraint = GetConstraintOfTypeParameter(GetTypeParameterFromMappedType(type));
            data.ConstraintType = constraint.IsNone ? errorType : constraint;
        }
        return data.ConstraintType;
    }

    /// <summary>checker.ts getNameTypeFromMappedType (None without an `as` clause).</summary>
    internal Type GetNameTypeFromMappedType(Type type)
    {
        var data = Types.Object(type);
        var nameTypeNode = StoreOf(data.Declaration).NameType(data.Declaration.Node);
        if (nameTypeNode.IsNone)
        {
            return Type.None;
        }
        if (data.NameType.IsNone)
        {
            data.NameType = InstantiateType(GetTypeFromTypeNode(At(data.Declaration, nameTypeNode)), data.Mapper);
        }
        return data.NameType;
    }

    /// <summary>checker.ts getTemplateTypeFromMappedType.</summary>
    internal Type GetTemplateTypeFromMappedType(Type type)
    {
        var data = Types.Object(type);
        if (data.TemplateType.IsNone)
        {
            var typeNode = StoreOf(data.Declaration).Type(data.Declaration.Node);
            data.TemplateType = !typeNode.IsNone
                ? InstantiateType(AddOptionality(GetTypeFromTypeNode(At(data.Declaration, typeNode)), isProperty: true, isOptional: (GetMappedTypeModifiers(type) & MappedTypeModifiers.IncludeOptional) != 0), data.Mapper)
                : errorType;
        }
        return data.TemplateType;
    }

    // checker.ts getConstraintDeclarationForMappedType
    private NodeRef GetConstraintDeclarationForMappedType(Type type)
    {
        var declaration = Types.Object(type).Declaration;
        var store = StoreOf(declaration);
        return At(declaration, store.Constraint(store.TypeParameter(declaration.Node)));
    }

    /// <summary>checker.ts isMappedTypeWithKeyofConstraintDeclaration.</summary>
    internal bool IsMappedTypeWithKeyofConstraintDeclaration(Type type)
    {
        var constraintDeclaration = GetConstraintDeclarationForMappedType(type);
        return !constraintDeclaration.IsNone && Kind(constraintDeclaration) == SyntaxKind.TypeOperator && StoreOf(constraintDeclaration).Operator(constraintDeclaration.Node) == SyntaxKind.KeyOfKeyword;
    }

    /// <summary>checker.ts getModifiersTypeFromMappedType.</summary>
    internal Type GetModifiersTypeFromMappedType(Type type)
    {
        var data = Types.Object(type);
        if (data.ModifiersType.IsNone)
        {
            if (IsMappedTypeWithKeyofConstraintDeclaration(type))
            {
                // If the constraint declaration is a 'keyof T' node, the modifiers type is T. We check
                // AST nodes here because, when T is a non-generic type, the logic below eagerly resolves
                // 'keyof T' to a literal union type and we can't recover T from that type.
                var constraintDeclaration = GetConstraintDeclarationForMappedType(type);
                data.ModifiersType = InstantiateType(GetTypeFromTypeNode(At(constraintDeclaration, StoreOf(constraintDeclaration).Type(constraintDeclaration.Node))), data.Mapper);
            }
            else
            {
                // Otherwise, get the declared constraint type, and if the constraint type is a type parameter,
                // get the constraint of that type parameter. If the resulting type is an indexed type 'keyof T',
                // the modifiers type is T. Otherwise, the modifiers type is unknown.
                var declaredType = GetTypeFromMappedTypeNode(data.Declaration);
                var constraint = GetConstraintTypeFromMappedType(declaredType);
                var extendedConstraint = !constraint.IsNone && (Types.Flags(constraint) & TypeFlags.TypeParameter) != 0 ? GetConstraintOfTypeParameter(constraint) : constraint;
                data.ModifiersType = !extendedConstraint.IsNone && (Types.Flags(extendedConstraint) & TypeFlags.Index) != 0
                    ? InstantiateType(Types.Data<IndexData>(extendedConstraint).Type, data.Mapper)
                    : unknownType;
            }
        }
        return data.ModifiersType;
    }

    /// <summary>checker.ts getMappedTypeModifiers.</summary>
    internal MappedTypeModifiers GetMappedTypeModifiers(Type type)
    {
        var declaration = Types.Object(type).Declaration;
        var store = StoreOf(declaration);
        var readonlyToken = store.ReadonlyToken(declaration.Node);
        var questionToken = store.QuestionToken(declaration.Node);
        return (!readonlyToken.IsNone ? store.Kind(readonlyToken) == SyntaxKind.MinusToken ? MappedTypeModifiers.ExcludeReadonly : MappedTypeModifiers.IncludeReadonly : 0) |
            (!questionToken.IsNone ? store.Kind(questionToken) == SyntaxKind.MinusToken ? MappedTypeModifiers.ExcludeOptional : MappedTypeModifiers.IncludeOptional : 0);
    }

    // checker.ts getMappedTypeOptionality
    private int GetMappedTypeOptionality(Type type)
    {
        var modifiers = GetMappedTypeModifiers(type);
        return (modifiers & MappedTypeModifiers.ExcludeOptional) != 0 ? -1 : (modifiers & MappedTypeModifiers.IncludeOptional) != 0 ? 1 : 0;
    }

    // checker.ts getCombinedMappedTypeOptionality
    private int GetCombinedMappedTypeOptionality(Type type)
    {
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.Mapped) != 0)
        {
            var optionality = GetMappedTypeOptionality(type);
            return optionality != 0 ? optionality : GetCombinedMappedTypeOptionality(GetModifiersTypeFromMappedType(type));
        }
        if ((Types.Flags(type) & TypeFlags.Intersection) != 0)
        {
            var types = Types.Elements(Types.Union(type).Types).ToArray();
            var optionality = GetCombinedMappedTypeOptionality(types[0]);
            for (var i = 1; i < types.Length; i++)
            {
                if (GetCombinedMappedTypeOptionality(types[i]) != optionality)
                {
                    return 0;
                }
            }
            return optionality;
        }
        return 0;
    }

    /// <summary>checker.ts isGenericMappedType.</summary>
    internal bool IsGenericMappedType(Type type)
    {
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.Mapped) != 0)
        {
            var constraint = GetConstraintTypeFromMappedType(type);
            if (IsGenericIndexType(constraint))
            {
                return true;
            }
            // A mapped type is generic if the 'as' clause references generic types other than the iteration type.
            // To determine this, we substitute the constraint type (that we now know isn't generic) for the iteration
            // type and check whether the resulting type is generic.
            var nameType = GetNameTypeFromMappedType(type);
            if (!nameType.IsNone && IsGenericIndexType(InstantiateType(nameType, MakeUnaryTypeMapper(GetTypeParameterFromMappedType(type), constraint))))
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts getMappedTypeNameTypeKind
    private MappedTypeNameTypeKind GetMappedTypeNameTypeKind(Type type)
    {
        var nameType = GetNameTypeFromMappedType(type);
        if (nameType.IsNone)
        {
            return MappedTypeNameTypeKind.None;
        }
        return IsTypeAssignableTo(nameType, GetTypeParameterFromMappedType(type)) ? MappedTypeNameTypeKind.Filtering : MappedTypeNameTypeKind.Remapping;
    }

    // checker.ts resolveMappedTypeMembers: the members of a mapped type { [P in K]: T }
    private void ResolveMappedTypeMembers(Type type)
    {
        var members = new SymTable();
        List<IndexInfo>? indexInfos = null;
        // Resolve upfront such that recursive references see an empty object type.
        SetStructuredTypeMembers(type, emptySymbols, null, null, null);
        // In { [P in K]: T }, we refer to P as the type parameter type, K as the constraint type,
        // and T as the template type.
        var typeParameter = GetTypeParameterFromMappedType(type);
        var constraintType = GetConstraintTypeFromMappedType(type);
        var typeData = Types.Object(type);
        var mappedType = !typeData.Target.IsNone ? typeData.Target : type;
        var nameType = GetNameTypeFromMappedType(mappedType);
        var shouldLinkPropDeclarations = GetMappedTypeNameTypeKind(mappedType) != MappedTypeNameTypeKind.Remapping;
        var templateType = GetTemplateTypeFromMappedType(mappedType);
        var modifiersType = GetApparentType(GetModifiersTypeFromMappedType(type)); // The 'T' in 'keyof T'
        var templateModifiers = GetMappedTypeModifiers(type);
        const TypeFlags include = TypeFlags.StringOrNumberLiteralOrUnique;
        if (IsMappedTypeWithKeyofConstraintDeclaration(type))
        {
            // We have a { [P in keyof T]: X }
            ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType(modifiersType, include, stringsOnly: false, AddMemberForKeyType);
        }
        else
        {
            ForEachType(GetLowerBoundOfKeyType(constraintType), AddMemberForKeyType);
        }
        SetStructuredTypeMembers(type, members, null, null, indexInfos);

        void AddMemberForKeyType(Type keyType)
        {
            var propNameType = !nameType.IsNone ? InstantiateType(nameType, AppendTypeMapping(typeData.Mapper, typeParameter, keyType)) : keyType;
            ForEachType(propNameType, t => AddMemberForKeyTypeWorker(keyType, t));
        }

        void AddMemberForKeyTypeWorker(Type keyType, Type propNameType)
        {
            // If the current iteration type constituent is a string literal type, create a property.
            // Otherwise, for type string create a string index signature.
            if (IsTypeUsableAsPropertyName(propNameType))
            {
                var propName = GetPropertyNameFromType(propNameType);
                // String enum members from separate enums with identical values
                // are distinct types with the same property name. Make the resulting
                // property symbol's name type be the union of those enum member types.
                var existingProp = members.Get(propName);
                if (!existingProp.IsNone)
                {
                    ref var existingLinks = ref ValueSymbolLinks.Get(existingProp);
                    existingLinks.NameType = GetUnionType([existingLinks.NameType, propNameType]);
                    var links = mappedSymbolLinks[existingProp];
                    mappedSymbolLinks[existingProp] = links with { KeyType = GetUnionType([links.KeyType, keyType]) };
                }
                else
                {
                    var modifiersProp = IsTypeUsableAsPropertyName(keyType) ? GetPropertyOfType(modifiersType, GetPropertyNameFromType(keyType)) : SymbolRef.None;
                    var isOptional = (templateModifiers & MappedTypeModifiers.IncludeOptional) != 0 ||
                        ((templateModifiers & MappedTypeModifiers.ExcludeOptional) == 0 && !modifiersProp.IsNone && (SymbolFlagsOf(modifiersProp) & SymbolFlags.Optional) != 0);
                    var isReadonly = (templateModifiers & MappedTypeModifiers.IncludeReadonly) != 0 ||
                        ((templateModifiers & MappedTypeModifiers.ExcludeReadonly) == 0 && !modifiersProp.IsNone && IsReadonlySymbolForMapping(modifiersProp));
                    var stripOptional = strictNullChecks && !isOptional && !modifiersProp.IsNone && (SymbolFlagsOf(modifiersProp) & SymbolFlags.Optional) != 0;
                    var lateFlag = !modifiersProp.IsNone ? GetCheckFlags(modifiersProp) & CheckFlags.Late : 0;
                    var prop = CreateSymbol(SymbolFlags.Property | (isOptional ? SymbolFlags.Optional : 0), propName,
                        lateFlag | CheckFlags.Mapped | (isReadonly ? CheckFlags.Readonly : 0) | (stripOptional ? CheckFlags.StripOptional : 0));
                    ValueSymbolLinks.Get(prop).NameType = propNameType;
                    mappedSymbolLinks[prop] = (type, keyType, modifiersProp);
                    if (!modifiersProp.IsNone)
                    {
                        TransientSymbols.Record(prop).Declarations = shouldLinkPropDeclarations ? DeclarationsOf(modifiersProp) : null;
                    }
                    members.Set(propName, prop);
                }
            }
            else if (IsValidIndexKeyType(propNameType) || (Types.Flags(propNameType) & (TypeFlags.Any | TypeFlags.Enum)) != 0)
            {
                var flags = Types.Flags(propNameType);
                var indexKeyType = (flags & (TypeFlags.Any | TypeFlags.String)) != 0 ? stringType
                    : (flags & (TypeFlags.Number | TypeFlags.Enum)) != 0 ? numberType
                    : propNameType;
                var propType = InstantiateType(templateType, AppendTypeMapping(typeData.Mapper, typeParameter, keyType));
                var modifiersIndexInfo = GetApplicableIndexInfo(modifiersType, propNameType);
                var isReadonly = (templateModifiers & MappedTypeModifiers.IncludeReadonly) != 0 ||
                    ((templateModifiers & MappedTypeModifiers.ExcludeReadonly) == 0 && modifiersIndexInfo is { IsReadonly: true });
                var indexInfo = new IndexInfo { KeyType = indexKeyType, Type = propType, IsReadonly = isReadonly };
                indexInfos = AppendIndexInfo(indexInfos, indexInfo, union: true);
            }
        }
    }

    // checker.ts isReadonlySymbol (the cases a mapped type's modifiers type has)
    private bool IsReadonlySymbolForMapping(SymbolRef symbol)
    {
        // The following symbols are considered read-only:
        // Properties with a 'readonly' modifier
        // Variables declared with 'const'
        // Get accessors without matching set accessors
        // Enum members
        // Object.defineProperty assignments with writable false or no setter
        // Unions and intersections of the above (unions and intersections eagerly set isReadonly on creation)
        var flags = SymbolFlagsOf(symbol);
        if ((GetCheckFlags(symbol) & CheckFlags.Readonly) != 0 ||
            ((flags & SymbolFlags.Property) != 0 && (GetDeclarationModifierFlagsFromSymbol(symbol) & ModifierFlags.Readonly) != 0) ||
            ((flags & SymbolFlags.Variable) != 0 && (GetDeclarationNodeFlagsFromSymbol(symbol) & NodeFlags.Constant) != 0) ||
            ((flags & SymbolFlags.Accessor) != 0 && (flags & SymbolFlags.SetAccessor) == 0) ||
            (flags & SymbolFlags.EnumMember) != 0)
        {
            return true;
        }
        return false;
    }

    // checker.ts getDeclarationNodeFlagsFromSymbol
    private NodeFlags GetDeclarationNodeFlagsFromSymbol(SymbolRef symbol)
    {
        var declaration = ValueDeclarationOf(symbol);
        return declaration.IsNone ? 0 : Utilities.GetCombinedNodeFlags(StoreOf(declaration), declaration.Node);
    }

    // checker.ts appendIndexInfo
    private List<IndexInfo> AppendIndexInfo(List<IndexInfo>? indexInfos, IndexInfo newInfo, bool union)
    {
        if (indexInfos is not null)
        {
            for (var i = 0; i < indexInfos.Count; i++)
            {
                var info = indexInfos[i];
                if (info.KeyType == newInfo.KeyType)
                {
                    indexInfos[i] = new IndexInfo
                    {
                        KeyType = info.KeyType,
                        Type = union ? GetUnionType([info.Type, newInfo.Type]) : GetIntersectionType([info.Type, newInfo.Type]),
                        IsReadonly = union ? info.IsReadonly || newInfo.IsReadonly : info.IsReadonly && newInfo.IsReadonly,
                    };
                    return indexInfos;
                }
            }
        }
        indexInfos ??= [];
        indexInfos.Add(newInfo);
        return indexInfos;
    }

    // checker.ts isValidIndexKeyType
    private bool IsValidIndexKeyType(Type type) =>
        (Types.Flags(type) & (TypeFlags.String | TypeFlags.Number | TypeFlags.ESSymbol)) != 0 || IsPatternLiteralType(type) ||
        ((Types.Flags(type) & TypeFlags.Intersection) != 0 && !IsGenericType(type) && ExistsType(Types.Elements(Types.Union(type).Types), IsValidIndexKeyType));

    // checker.ts getTypeOfMappedSymbol
    private Type GetTypeOfMappedSymbol(SymbolRef symbol)
    {
        var links = ValueSymbolLinks.Get(symbol);
        if (!links.ResolvedType.IsNone)
        {
            return links.ResolvedType;
        }
        var (mappedType, keyType, _) = mappedSymbolLinks[symbol];
        if (!PushTypeResolution(symbol, TypeProperty))
        {
            Types.Object(mappedType).ContainsError = true;
            return errorType;
        }
        var mappedData = Types.Object(mappedType);
        var templateType = GetTemplateTypeFromMappedType(!mappedData.Target.IsNone ? mappedData.Target : mappedType);
        var mapper = AppendTypeMapping(mappedData.Mapper, GetTypeParameterFromMappedType(mappedType), keyType);
        var propType = InstantiateType(templateType, mapper);
        // When creating an optional property in strictNullChecks mode, if 'undefined' isn't assignable to the
        // type, we include 'undefined' in the type. Similarly, when creating a non-optional property in strictNullChecks
        // mode, if the underlying property is optional we remove 'undefined' from the type.
        var type = strictNullChecks && (SymbolFlagsOf(symbol) & SymbolFlags.Optional) != 0 && !MaybeTypeOfKind(propType, TypeFlags.Undefined | TypeFlags.Void) ? GetOptionalType(propType, isProperty: true)
            : (GetCheckFlags(symbol) & CheckFlags.StripOptional) != 0 ? RemoveMissingOrUndefinedType(propType)
            : propType;
        if (!PopTypeResolution())
        {
            Error(currentNode, Diagnostics.Type_of_property_0_circularly_references_itself_in_mapped_type_1, SymbolToString(symbol), TypeToString(mappedType));
            type = errorType;
        }
        ref var after = ref ValueSymbolLinks.Get(symbol);
        if (after.ResolvedType.IsNone)
        {
            after.ResolvedType = type;
        }
        return after.ResolvedType;
    }

    // checker.ts removeMissingOrUndefinedType
    private Type RemoveMissingOrUndefinedType(Type type) =>
        exactOptionalPropertyTypes ? FilterType(type, t => t != missingType) : GetTypeWithFactsNEUndefined(type);

    // checker.ts getTypeWithFacts(type, TypeFacts.NEUndefined) (TODO(C7): the type facts)
    private Type GetTypeWithFactsNEUndefined(Type type) => GetTypeWithFacts(type, TypeFacts.NEUndefined);

    // checker.ts getLowerBoundOfKeyType: the keys known to always be present
    private Type GetLowerBoundOfKeyType(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Index) != 0)
        {
            var t = GetApparentType(Types.Data<IndexData>(type).Type);
            return IsGenericTupleType(t) ? GetKnownKeysOfTupleType(t) : GetIndexType(t);
        }
        if ((flags & TypeFlags.Conditional) != 0)
        {
            var data = Types.Data<ConditionalData>(type);
            if (data.Root.IsDistributive)
            {
                var checkType = data.CheckType;
                var constraint = GetLowerBoundOfKeyType(checkType);
                if (constraint != checkType)
                {
                    return GetConditionalTypeInstantiation(type, PrependTypeMapping(data.Root.CheckType, constraint, data.Mapper), forConstraint: false);
                }
            }
            return type;
        }
        if ((flags & TypeFlags.Union) != 0)
        {
            return MapType(type, GetLowerBoundOfKeyType, noReductions: true);
        }
        if ((flags & TypeFlags.Intersection) != 0)
        {
            // Similarly to getTypeFromIntersectionTypeNode, we preserve the special string & {}, number & {},
            // and bigint & {} intersections that are used to prevent subtype reduction in union types.
            var types = Types.Elements(Types.Union(type).Types).ToArray();
            if (types.Length == 2 && (Types.Flags(types[0]) & (TypeFlags.String | TypeFlags.Number | TypeFlags.BigInt)) != 0 && types[1] == emptyTypeLiteralType)
            {
                return type;
            }
            var mapped = Array.ConvertAll(types, GetLowerBoundOfKeyType);
            return RangeEquals(mapped, types, types.Length) ? type : GetIntersectionType(mapped);
        }
        return type;
    }

    // checker.ts getKnownKeysOfTupleType
    private Type GetKnownKeysOfTupleType(Type type)
    {
        var target = TupleTarget(type);
        var keys = new List<Type>();
        for (var i = 0; i < target.FixedLength; i++)
        {
            keys.Add(GetStringLiteralType(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        keys.Add(GetIndexType(target.Readonly ? globalReadonlyArrayType : globalArrayType));
        return GetUnionType(keys.ToArray());
    }

    // checker.ts forEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType
    private void ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType(Type type, TypeFlags include, bool stringsOnly, Action<Type> cb)
    {
        foreach (var prop in GetPropertiesOfType(type))
        {
            cb(GetLiteralTypeFromProperty(prop, include));
        }
        if ((Types.Flags(type) & TypeFlags.Any) != 0)
        {
            cb(stringType);
        }
        else
        {
            foreach (var info in GetIndexInfosOfType(type))
            {
                if (!stringsOnly || (Types.Flags(info.KeyType) & (TypeFlags.String | TypeFlags.TemplateLiteral)) != 0)
                {
                    cb(info.KeyType);
                }
            }
        }
    }

    // checker.ts getIndexTypeForMappedType
    private Type GetIndexTypeForMappedType(Type type, IndexFlags indexFlags)
    {
        var typeParameter = GetTypeParameterFromMappedType(type);
        var constraintType = GetConstraintTypeFromMappedType(type);
        var typeData = Types.Object(type);
        var nameType = GetNameTypeFromMappedType(!typeData.Target.IsNone ? typeData.Target : type);
        if (nameType.IsNone && (indexFlags & IndexFlags.NoIndexSignatures) == 0)
        {
            // no mapping and no filtering required, just quickly bail to returning the constraint in the common case
            return constraintType;
        }
        var keyTypes = new List<Type>();
        // Calling getApparentType on the `T` of a `keyof T` in the constraint type of a generic mapped type can
        // trigger a circularity. For example, `T extends { [P in keyof T & string as Captitalize<P>]: any }` is
        // a circular definition. For this reason, we only eagerly manifest the keys if the constraint is non-generic.
        if (IsGenericIndexType(constraintType))
        {
            if (IsMappedTypeWithKeyofConstraintDeclaration(type))
            {
                // We have a generic index and a homomorphic mapping and a key remapping - we need to defer
                // the whole `keyof whatever` for later since it's not safe to resolve the shape of modifier type.
                return GetIndexTypeForGenericType(type, indexFlags);
            }
            // Include the generic component in the resulting type.
            ForEachType(constraintType, AddMemberForKeyType);
        }
        else if (IsMappedTypeWithKeyofConstraintDeclaration(type))
        {
            var modifiersType = GetApparentType(GetModifiersTypeFromMappedType(type)); // The 'T' in 'keyof T'
            ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType(modifiersType, TypeFlags.StringOrNumberLiteralOrUnique, (indexFlags & IndexFlags.StringsOnly) != 0, AddMemberForKeyType);
        }
        else
        {
            ForEachType(GetLowerBoundOfKeyType(constraintType), AddMemberForKeyType);
        }
        // We had to pick apart the constraintType to potentially map/filter it - compare the final resulting list with the
        // original constraintType, so we can return the union that preserves aliases/origin data if possible.
        var result = (indexFlags & IndexFlags.NoIndexSignatures) != 0
            ? FilterType(GetUnionType(keyTypes.ToArray()), t => (Types.Flags(t) & (TypeFlags.Any | TypeFlags.String)) == 0)
            : GetUnionType(keyTypes.ToArray());
        if ((Types.Flags(result) & TypeFlags.Union) != 0 && (Types.Flags(constraintType) & TypeFlags.Union) != 0 &&
            GetTypeListId(Types.Elements(Types.Union(result).Types)) == GetTypeListId(Types.Elements(Types.Union(constraintType).Types)))
        {
            return constraintType;
        }
        return result;

        void AddMemberForKeyType(Type keyType)
        {
            var propNameType = !nameType.IsNone ? InstantiateType(nameType, AppendTypeMapping(typeData.Mapper, typeParameter, keyType)) : keyType;
            // `keyof` currently always returns `string | number` for concrete `string` index signatures - the below ternary keeps that behavior for mapped types
            // See `getLiteralTypeFromProperties` where there's a similar ternary to cause the same behavior.
            keyTypes.Add(propNameType == stringType ? stringOrNumberType : propNameType);
        }
    }

    // checker.ts getHomomorphicTypeVariable
    private Type GetHomomorphicTypeVariable(Type type)
    {
        var constraintType = GetConstraintTypeFromMappedType(type);
        if ((Types.Flags(constraintType) & TypeFlags.Index) != 0)
        {
            var typeVariable = GetActualTypeVariable(Types.Data<IndexData>(constraintType).Type);
            if ((Types.Flags(typeVariable) & TypeFlags.TypeParameter) != 0)
            {
                return typeVariable;
            }
        }
        return Type.None;
    }

    // nodebuilder isMappedTypeHomomorphic / isHomomorphicMappedTypeWithNonHomomorphicInstantiation
    private bool IsHomomorphicMappedTypeWithNonHomomorphicInstantiation(Type type)
    {
        var target = Types.Object(type).Target;
        return !target.IsNone && !GetHomomorphicTypeVariable(target).IsNone && GetHomomorphicTypeVariable(type).IsNone;
    }

    // nodebuilder createMappedTypeNodeFromType: the target's template type instantiated for the new constraint type
    // parameter (instantiateType(getTemplateTypeFromMappedType(target), makeArrayTypeMapper([typeParameter of target,
    // modifiers type of target], [typeParameter, newConstraintParam])))
    private Type InstantiateMappedTemplateForNewConstraint(Type type, Type newConstraintParameter)
    {
        var target = Types.Object(type).Target;
        return InstantiateType(GetTemplateTypeFromMappedType(target),
            MakeArrayTypeMapper([GetTypeParameterFromMappedType(target), GetModifiersTypeFromMappedType(target)], [GetTypeParameterFromMappedType(type), newConstraintParameter]));
    }

    // nodebuilder createMappedTypeNodeFromType: instantiateType(getConstraintOfTypeParameter(getTypeFromTypeNode(
    // type.declaration.typeParameter.constraint.type)) || unknownType, type.mapper)
    private Type GetOriginalConstraintOfHomomorphicMappedType(Type type)
    {
        var data = Types.Object(type);
        var declaration = data.Declaration;
        var store = StoreOf(declaration);
        var constraint = store.Constraint(store.TypeParameter(declaration.Node));
        var constraintOfParameter = GetConstraintOfTypeParameter(GetTypeFromTypeNode(At(declaration, store.Type(constraint))));
        return InstantiateType(constraintOfParameter.IsNone ? unknownType : constraintOfParameter, data.Mapper);
    }

    // nodebuilder conditionalTypeToTypeNode: the extends, true and false types instantiated with
    // prependTypeMapping(type.root.checkType, newParam, type.mapper)
    private (Type ExtendsType, Type TrueType, Type FalseType) GetConditionalTypeBranchesWithNewCheckType(Type type, Type newCheckType)
    {
        var data = Types.Data<ConditionalData>(type);
        var root = data.Root;
        var newMapper = PrependTypeMapping(root.CheckType, newCheckType, data.Mapper);
        var store = StoreOf(root.Node);
        return (InstantiateType(root.ExtendsType, newMapper),
            InstantiateType(GetTypeFromTypeNode(At(root.Node, store.TrueType(root.Node.Node))), newMapper),
            InstantiateType(GetTypeFromTypeNode(At(root.Node, store.FalseType(root.Node.Node))), newMapper));
    }

    // checker.ts instantiateMappedType
    private Type InstantiateMappedType(Type type, TypeMapper mapper, SymbolRef aliasSymbol, TypeList aliasTypeArguments)
    {
        // For a homomorphic mapped type { [P in keyof T]: X }, where T is some type variable, the mapping
        // operation depends on T as follows:
        // * If T is a primitive type no mapping is performed and the result is simply T.
        // * If T is a union type we distribute the mapped type over the union.
        // * If T is an array we map to an array where the element type has been transformed.
        // * If T is a tuple we map to a tuple where the element types have been transformed.
        // * If T is an intersection of array or tuple types we map to an intersection of transformed array or tuple types.
        // * Otherwise we map to an object type where the type of each property has been transformed.
        var typeVariable = GetHomomorphicTypeVariable(type);
        if (!typeVariable.IsNone)
        {
            var mappedTypeVariable = InstantiateType(typeVariable, mapper);
            if (typeVariable != mappedTypeVariable)
            {
                return MapTypeWithAlias(GetReducedType(mappedTypeVariable), InstantiateConstituent, aliasSymbol, aliasTypeArguments);
            }
        }
        // If the constraint type of the instantiation is the wildcard type, return the wildcard type.
        return InstantiateType(GetConstraintTypeFromMappedType(type), mapper) == wildcardType ? wildcardType : InstantiateAnonymousType(type, mapper, aliasSymbol, aliasTypeArguments);

        Type InstantiateConstituent(Type t)
        {
            if ((Types.Flags(t) & (TypeFlags.AnyOrUnknown | TypeFlags.InstantiableNonPrimitive | TypeFlags.Object | TypeFlags.Intersection)) != 0 && t != wildcardType && !IsErrorType(t))
            {
                var declaration = Types.Object(type).Declaration;
                if (StoreOf(declaration).NameType(declaration.Node).IsNone)
                {
                    Type constraint;
                    if (IsArrayType(t) || ((Types.Flags(t) & TypeFlags.Any) != 0 && FindResolutionCycleStartIndex(typeVariable, ImmediateBaseConstraintProperty) < 0 &&
                        !(constraint = GetConstraintOfTypeParameter(typeVariable)).IsNone && EveryType(constraint, IsArrayOrTupleType)))
                    {
                        return InstantiateMappedArrayType(t, type, PrependTypeMapping(typeVariable, t, mapper));
                    }
                    if (IsTupleType(t))
                    {
                        return InstantiateMappedTupleType(t, type, typeVariable, mapper);
                    }
                    if (IsArrayOrTupleOrIntersection(t))
                    {
                        return GetIntersectionType(Array.ConvertAll(Types.Elements(Types.Union(t).Types).ToArray(), InstantiateConstituent));
                    }
                }
                return InstantiateAnonymousType(type, PrependTypeMapping(typeVariable, t, mapper), SymbolRef.None, default);
            }
            return t;
        }
    }

    // checker.ts isArrayOrTupleOrIntersection
    private bool IsArrayOrTupleOrIntersection(Type type) =>
        (Types.Flags(type) & TypeFlags.Intersection) != 0 && Array.TrueForAll(Types.Elements(Types.Union(type).Types).ToArray(), IsArrayOrTupleType);

    // checker.ts getModifiedReadonlyState
    private static bool GetModifiedReadonlyState(bool state, MappedTypeModifiers modifiers) =>
        (modifiers & MappedTypeModifiers.IncludeReadonly) != 0 || ((modifiers & MappedTypeModifiers.ExcludeReadonly) == 0 && state);

    // checker.ts instantiateMappedTupleType
    private Type InstantiateMappedTupleType(Type tupleType, Type mappedType, Type typeVariable, TypeMapper mapper)
    {
        // We apply the mapped type's template type to each of the fixed part elements. For variadic elements, we
        // apply the mapped type itself to the variadic element type. For other elements in the variable part of the
        // tuple, we surround the element type with an array type and apply the mapped type to that.
        var target = TupleTarget(tupleType);
        var elementFlags = target.ElementFlags!;
        var fixedLength = target.FixedLength;
        var fixedMapper = fixedLength > 0 ? PrependTypeMapping(typeVariable, tupleType, mapper) : mapper;
        var elementTypes = GetElementTypes(tupleType);
        var newElementTypes = new Type[elementTypes.Length];
        for (var i = 0; i < elementTypes.Length; i++)
        {
            var flags = elementFlags[i];
            var type = elementTypes[i];
            if (i < fixedLength)
            {
                newElementTypes[i] = InstantiateMappedTypeTemplate(mappedType, GetStringLiteralType(i.ToString(System.Globalization.CultureInfo.InvariantCulture)), (flags & ElementFlags.Optional) != 0, fixedMapper);
            }
            else if ((flags & ElementFlags.Variadic) != 0)
            {
                newElementTypes[i] = InstantiateType(mappedType, PrependTypeMapping(typeVariable, type, mapper));
            }
            else
            {
                var elementType = GetElementTypeOfArrayType(InstantiateType(mappedType, PrependTypeMapping(typeVariable, CreateArrayType(type), mapper)));
                newElementTypes[i] = elementType.IsNone ? unknownType : elementType;
            }
        }
        var modifiers = GetMappedTypeModifiers(mappedType);
        var newElementFlags = (modifiers & MappedTypeModifiers.IncludeOptional) != 0 ? Array.ConvertAll(elementFlags, f => (f & ElementFlags.Required) != 0 ? ElementFlags.Optional : f)
            : (modifiers & MappedTypeModifiers.ExcludeOptional) != 0 ? Array.ConvertAll(elementFlags, f => (f & ElementFlags.Optional) != 0 ? ElementFlags.Required : f)
            : elementFlags;
        var newReadonly = GetModifiedReadonlyState(target.Readonly, GetMappedTypeModifiers(mappedType));
        return Array.IndexOf(newElementTypes, errorType) >= 0 ? errorType : CreateTupleType(newElementTypes, newElementFlags, newReadonly, target.LabeledElementDeclarations);
    }

    // checker.ts instantiateMappedArrayType
    private Type InstantiateMappedArrayType(Type arrayType, Type mappedType, TypeMapper mapper)
    {
        var elementType = InstantiateMappedTypeTemplate(mappedType, numberType, isOptional: true, mapper);
        return IsErrorType(elementType) ? errorType : CreateArrayType(elementType, GetModifiedReadonlyState(IsReadonlyArrayType(arrayType), GetMappedTypeModifiers(mappedType)));
    }

    // checker.ts instantiateMappedTypeTemplate
    private Type InstantiateMappedTypeTemplate(Type type, Type key, bool isOptional, TypeMapper mapper)
    {
        var templateMapper = AppendTypeMapping(mapper, GetTypeParameterFromMappedType(type), key);
        var data = Types.Object(type);
        var propType = InstantiateType(GetTemplateTypeFromMappedType(!data.Target.IsNone ? data.Target : type), templateMapper);
        var modifiers = GetMappedTypeModifiers(type);
        return strictNullChecks && (modifiers & MappedTypeModifiers.IncludeOptional) != 0 && !MaybeTypeOfKind(propType, TypeFlags.Undefined | TypeFlags.Void) ? GetOptionalType(propType, isProperty: true)
            : strictNullChecks && (modifiers & MappedTypeModifiers.ExcludeOptional) != 0 && isOptional ? GetTypeWithFactsNEUndefined(propType)
            : propType;
    }

    // checker.ts getApparentTypeOfMappedType
    private Type GetApparentTypeOfMappedType(Type type)
    {
        var data = Types.Object(type);
        if (data.ResolvedApparentType.IsNone)
        {
            data.ResolvedApparentType = GetResolvedApparentTypeOfMappedType(type);
        }
        return data.ResolvedApparentType;
    }

    // checker.ts getResolvedApparentTypeOfMappedType
    private Type GetResolvedApparentTypeOfMappedType(Type type)
    {
        var data = Types.Object(type);
        var target = !data.Target.IsNone ? data.Target : type;
        var typeVariable = GetHomomorphicTypeVariable(target);
        var targetDeclaration = Types.Object(target).Declaration;
        if (!typeVariable.IsNone && StoreOf(targetDeclaration).NameType(targetDeclaration.Node).IsNone)
        {
            // We have a homomorphic mapped type or an instantiation of a homomorphic mapped type, i.e. a type
            // of the form { [P in keyof T]: X }. Obtain the modifiers type (the T of the keyof T), and if it is
            // another generic mapped type, recursively obtain its apparent type. Otherwise, obtain its base
            // constraint. Then, if every constituent of the base constraint is an array or tuple type, apply
            // this mapped type to the base constraint.
            var modifiersType = GetModifiersTypeFromMappedType(type);
            var baseConstraint = IsGenericMappedType(modifiersType) ? GetApparentTypeOfMappedType(modifiersType) : GetBaseConstraintOfType(modifiersType);
            if (!baseConstraint.IsNone && EveryType(baseConstraint, t => IsArrayOrTupleType(t) || IsArrayOrTupleOrIntersection(t)))
            {
                return InstantiateType(target, PrependTypeMapping(typeVariable, baseConstraint, data.Mapper));
            }
        }
        return type;
    }

    // checker.ts substituteIndexedMappedType: for { [P in K]: E }[X], an instantiation of E where P is replaced with X
    private Type SubstituteIndexedMappedType(Type objectType, Type index)
    {
        var mapper = CreateTypeMapper([GetTypeParameterFromMappedType(objectType)], [index]);
        var data = Types.Object(objectType);
        var templateMapper = CombineTypeMappers(data.Mapper, mapper);
        var instantiatedTemplateType = InstantiateType(GetTemplateTypeFromMappedType(!data.Target.IsNone ? data.Target : objectType), templateMapper);
        var isOptional = GetMappedTypeOptionality(objectType) > 0 || (IsGenericType(objectType)
            ? GetCombinedMappedTypeOptionality(GetModifiersTypeFromMappedType(objectType)) > 0
            : CouldAccessOptionalProperty(objectType, index));
        return AddOptionality(instantiatedTemplateType, isProperty: true, isOptional);
    }

    // checker.ts couldAccessOptionalProperty
    private bool CouldAccessOptionalProperty(Type objectType, Type indexType)
    {
        var indexConstraint = GetBaseConstraintOfType(indexType);
        return !indexConstraint.IsNone && GetPropertiesOfType(objectType).Exists(p =>
            (SymbolFlagsOf(p) & SymbolFlags.Optional) != 0 && IsTypeAssignableTo(GetLiteralTypeFromProperty(p, TypeFlags.StringOrNumberLiteralOrUnique), indexConstraint));
    }

    // ---- conditional types ----

    // checker.ts getTypeFromConditionalTypeNode
    private Type GetTypeFromConditionalTypeNode(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        var store = StoreOf(node);
        var checkType = GetTypeFromTypeNode(At(node, store.CheckType(node.Node)));
        var aliasSymbol = GetAliasSymbolForTypeNode(node);
        var aliasTypeArguments = GetTypeArgumentsForAliasSymbol(aliasSymbol);
        var allOuterTypeParameters = GetOuterTypeParameters(node, includeThisTypes: true);
        Type[]? outerTypeParameters = allOuterTypeParameters is null ? null
            : aliasTypeArguments.Count > 0 ? [.. allOuterTypeParameters]
            : allOuterTypeParameters.FindAll(tp => IsTypeParameterPossiblyReferenced(tp, node)).ToArray();
        var root = new ConditionalRoot
        {
            Node = node,
            CheckType = checkType,
            ExtendsType = GetTypeFromTypeNode(At(node, store.ExtendsType(node.Node))),
            IsDistributive = (Types.Flags(checkType) & TypeFlags.TypeParameter) != 0,
            InferTypeParameters = GetInferTypeParameters(node),
            OuterTypeParameters = outerTypeParameters,
            AliasSymbol = aliasSymbol,
            AliasTypeArguments = aliasTypeArguments,
        };
        var resolved = SetCachedNodeType(node, GetConditionalType(root, null, forConstraint: false));
        if (outerTypeParameters is not null)
        {
            root.Instantiations = new Dictionary<string, Type>(StringComparer.Ordinal) { [GetTypeListId(outerTypeParameters)] = resolved };
        }
        return resolved;
    }

    // checker.ts getInferTypeParameters
    private Type[]? GetInferTypeParameters(NodeRef node)
    {
        List<Type>? result = null;
        var locals = LocalsOf(node);
        if (locals is not null)
        {
            for (var i = 0; i < locals.Count; i++)
            {
                var symbol = locals.ValueAt(i);
                if ((SymbolFlagsOf(symbol) & SymbolFlags.TypeParameter) != 0)
                {
                    (result ??= []).Add(GetDeclaredTypeOfSymbol(symbol));
                }
            }
        }
        return result?.ToArray();
    }

    // checker.ts getTypeFromInferTypeNode
    private Type GetTypeFromInferTypeNode(NodeRef node)
    {
        var cached = GetCachedNodeType(node);
        if (!cached.IsNone)
        {
            return cached;
        }
        return SetCachedNodeType(node, GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(At(node, StoreOf(node).TypeParameter(node.Node)))));
    }

    // checker.ts skipTypeParentheses
    private NodeRef SkipTypeParentheses(NodeRef node)
    {
        while (Kind(node) == SyntaxKind.ParenthesizedType)
        {
            node = At(node, StoreOf(node).Type(node.Node));
        }
        return node;
    }

    // checker.ts isSimpleTupleType
    private bool IsSimpleTupleType(NodeRef node)
    {
        if (Kind(node) != SyntaxKind.TupleType)
        {
            return false;
        }
        var store = StoreOf(node);
        var elements = store.Nodes(store.Elements(node.Node)).ToArray();
        return elements.Length > 0 && !Array.Exists(elements, e =>
            store.Kind(e) is SyntaxKind.OptionalType or SyntaxKind.RestType ||
            (store.Kind(e) == SyntaxKind.NamedTupleMember && (!store.QuestionToken(e).IsNone || !store.DotDotDotToken(e).IsNone)));
    }

    // checker.ts isDeferredType
    private bool IsDeferredType(Type type, bool checkTuples) =>
        IsGenericType(type) || (checkTuples && IsTupleType(type) && Array.Exists(GetElementTypes(type), IsGenericType));

    /// <summary>checker.ts getConditionalType.</summary>
    private Type GetConditionalType(ConditionalRoot root, TypeMapper? mapper, bool forConstraint, SymbolRef aliasSymbol = default, TypeList aliasTypeArguments = default)
    {
        var result = Type.None;
        List<Type>? extraTypes = null;
        var tailCount = 0;
        // We loop here for an immediately nested conditional type in the false position, effectively treating
        // types of the form 'A extends B ? X : C extends D ? Y : E extends F ? Z : ...' as a single construct for
        // purposes of resolution. We also loop here when resolution of a conditional type ends in resolution of
        // another (or, through recursion, possibly the same) conditional type. In the potentially tail-recursive
        // cases we increment the tail recursion counter and stop after 1000 iterations.
        while (true)
        {
            if (tailCount == 1000)
            {
                Error(currentNode, Diagnostics.Type_instantiation_is_excessively_deep_and_possibly_infinite);
                return errorType;
            }
            var checkType = InstantiateType(GetActualTypeVariable(root.CheckType), mapper);
            var extendsType = InstantiateType(root.ExtendsType, mapper);
            if (checkType == errorType || extendsType == errorType)
            {
                return errorType;
            }
            if (checkType == wildcardType || extendsType == wildcardType)
            {
                return wildcardType;
            }
            var store = StoreOf(root.Node);
            var checkTypeNode = SkipTypeParentheses(At(root.Node, store.CheckType(root.Node.Node)));
            var extendsTypeNode = SkipTypeParentheses(At(root.Node, store.ExtendsType(root.Node.Node)));
            // When the check and extends types are simple tuple types of the same arity, we defer resolution of the
            // conditional type when any tuple elements are generic. This is such that non-distributable conditional
            // types can be written `[X] extends [Y] ? ...` and be deferred similarly to `X extends Y ? ...`.
            var checkTuples = IsSimpleTupleType(checkTypeNode) && IsSimpleTupleType(extendsTypeNode) &&
                StoreOf(checkTypeNode).Length(StoreOf(checkTypeNode).Elements(checkTypeNode.Node)) == StoreOf(extendsTypeNode).Length(StoreOf(extendsTypeNode).Elements(extendsTypeNode.Node));
            var checkTypeDeferred = IsDeferredType(checkType, checkTuples);
            TypeMapper? combinedMapper = null;
            if (root.InferTypeParameters is not null)
            {
                // TODO(C6): createInferenceContext and inferTypes(context.inferences, checkType, extendsType, NoConstraints |
                // AlwaysStrict) when the check type is not deferred. Without inference candidates an infer type parameter
                // maps to its constraint instantiated with the non-fixing mapper, or to unknown (getInferredType).
                var contextMapper = CreateUninferredMapper(root.InferTypeParameters, mapper);
                // It's possible for 'infer T' type paramteters to be given uninstantiated constraints when the
                // those type parameters are used in type references (see getInferredTypeParameterConstraint). For
                // that reason we need context.mapper to be first in the combined mapper. See #42636 for examples.
                combinedMapper = mapper is not null ? CombineTypeMappers(contextMapper, mapper) : contextMapper;
            }
            // Instantiate the extends type including inferences for 'infer T' type parameters
            var inferredExtendsType = combinedMapper is not null ? InstantiateType(root.ExtendsType, combinedMapper) : extendsType;
            // We attempt to resolve the conditional type only when the check and extends types are non-generic
            if (!checkTypeDeferred && !IsDeferredType(inferredExtendsType, checkTuples))
            {
                // Return falseType for a definitely false extends check. We check an instantiations of the two
                // types with type parameters mapped to the wildcard type, the most permissive instantiations
                // possible (the wildcard type is assignable to and from all types). If those are not related,
                // then no instantiations will be and we can just return the false branch type.
                if ((Types.Flags(inferredExtendsType) & TypeFlags.AnyOrUnknown) == 0 &&
                    ((Types.Flags(checkType) & TypeFlags.Any) != 0 || !IsTypeAssignableTo(GetPermissiveInstantiation(checkType), GetPermissiveInstantiation(inferredExtendsType))))
                {
                    // Return union of trueType and falseType for 'any' since it matches anything. Furthermore, for a
                    // distributive conditional type applied to the constraint of a type variable, include trueType if
                    // there are possible values of the check type that are also possible values of the extends type.
                    if ((Types.Flags(checkType) & TypeFlags.Any) != 0 || (forConstraint && (Types.Flags(inferredExtendsType) & TypeFlags.Never) == 0 &&
                        SomeType(GetPermissiveInstantiation(inferredExtendsType), t => IsTypeAssignableTo(t, GetPermissiveInstantiation(checkType)))))
                    {
                        (extraTypes ??= []).Add(InstantiateType(GetTypeFromTypeNode(At(root.Node, store.TrueType(root.Node.Node))), combinedMapper ?? mapper));
                    }
                    // If falseType is an immediately nested conditional type that isn't distributive or has an
                    // identical checkType, switch to that type and loop.
                    var falseType = GetTypeFromTypeNode(At(root.Node, store.FalseType(root.Node.Node)));
                    if ((Types.Flags(falseType) & TypeFlags.Conditional) != 0)
                    {
                        var newRoot = Types.Data<ConditionalData>(falseType).Root;
                        if (Parent(newRoot.Node) == root.Node && (!newRoot.IsDistributive || newRoot.CheckType == root.CheckType))
                        {
                            root = newRoot;
                            continue;
                        }
                        if (CanTailRecurse(falseType, mapper))
                        {
                            continue;
                        }
                    }
                    result = InstantiateType(falseType, mapper);
                    break;
                }
                // Return trueType for a definitely true extends check. We check instantiations of the two
                // types with type parameters mapped to their restrictive form, i.e. a form of the type parameter
                // that has no constraint. This ensures that, for example, the type
                //   type Foo<T extends { x: any }> = T extends { x: string } ? string : number
                // doesn't immediately resolve to 'string' instead of being deferred.
                if ((Types.Flags(inferredExtendsType) & TypeFlags.AnyOrUnknown) != 0 || IsTypeAssignableTo(GetRestrictiveInstantiation(checkType), GetRestrictiveInstantiation(inferredExtendsType)))
                {
                    var trueType = GetTypeFromTypeNode(At(root.Node, store.TrueType(root.Node.Node)));
                    var trueMapper = combinedMapper ?? mapper;
                    if (CanTailRecurse(trueType, trueMapper))
                    {
                        continue;
                    }
                    result = InstantiateType(trueType, trueMapper);
                    break;
                }
            }
            // Return a deferred type for a check that is neither definitely true nor definitely false
            result = Types.CreateWithData(TypeFlags.Conditional, new ConditionalData
            {
                Root = root,
                CheckType = InstantiateType(root.CheckType, mapper),
                ExtendsType = InstantiateType(root.ExtendsType, mapper),
                Mapper = mapper,
                CombinedMapper = combinedMapper,
            });
            ref var record = ref Types.Record(result);
            record.AliasSymbol = !aliasSymbol.IsNone ? aliasSymbol : root.AliasSymbol;
            record.AliasTypeArguments = !aliasSymbol.IsNone ? aliasTypeArguments : InstantiateTypeList(root.AliasTypeArguments, mapper);
            break;
        }
        return extraTypes is not null ? GetUnionType([.. extraTypes, result]) : result;

        // We tail-recurse for generic conditional types that (a) have not already been evaluated and cached, and
        // (b) are non distributive, have a check type that is unaffected by instantiation, or have a non-union check
        // type. Note that recursion is possible only through aliased conditional types, so we only increment the tail
        // recursion counter for those.
        bool CanTailRecurse(Type newType, TypeMapper? newMapper)
        {
            if ((Types.Flags(newType) & TypeFlags.Conditional) != 0 && newMapper is not null)
            {
                var newData = Types.Data<ConditionalData>(newType);
                var newRoot = newData.Root;
                if (newRoot.OuterTypeParameters is not null)
                {
                    var typeParamMapper = CombineTypeMappers(newData.Mapper, newMapper);
                    var typeArguments = Array.ConvertAll(newRoot.OuterTypeParameters, t => GetMappedType(t, typeParamMapper));
                    var newRootMapper = CreateTypeMapper(newRoot.OuterTypeParameters, typeArguments);
                    var newCheckType = newRoot.IsDistributive ? GetMappedType(newRoot.CheckType, newRootMapper) : Type.None;
                    if (newCheckType.IsNone || newCheckType == newRoot.CheckType || (Types.Flags(newCheckType) & (TypeFlags.Union | TypeFlags.Never)) == 0)
                    {
                        root = newRoot;
                        mapper = newRootMapper;
                        aliasSymbol = SymbolRef.None;
                        aliasTypeArguments = default;
                        if (!newRoot.AliasSymbol.IsNone)
                        {
                            tailCount++;
                        }
                        return true;
                    }
                }
            }
            return false;
        }
    }

    // The inference context's mapper of a conditional type's infer type parameters without inference candidates (C6
    // ports createInferenceContext and inferTypes): each maps to its constraint instantiated with the non-fixing mapper
    // (the outer mapper combined with the uninferred infer type parameters), or to unknown.
    private TypeMapper CreateUninferredMapper(Type[] inferTypeParameters, TypeMapper? mapper)
    {
        var inferred = new Type[inferTypeParameters.Length];
        TypeMapper? nonFixingMapper = null;
        TypeMapper contextMapper = null!;
        contextMapper = MakeFunctionTypeMapper(t =>
        {
            var index = Array.IndexOf(inferTypeParameters, t);
            if (index < 0)
            {
                return t;
            }
            if (inferred[index].IsNone)
            {
                inferred[index] = unknownType;
                var constraint = GetConstraintOfTypeParameter(t);
                if (!constraint.IsNone)
                {
                    nonFixingMapper ??= mapper is not null ? CombineTypeMappers(contextMapper, mapper) : contextMapper;
                    inferred[index] = InstantiateType(constraint, nonFixingMapper);
                }
                ClearActiveMapperCaches();
            }
            return inferred[index];
        });
        return contextMapper;
    }

    /// <summary>checker.ts getConditionalTypeInstantiation.</summary>
    private Type GetConditionalTypeInstantiation(Type type, TypeMapper mapper, bool forConstraint, SymbolRef aliasSymbol = default, TypeList aliasTypeArguments = default)
    {
        var root = Types.Data<ConditionalData>(type).Root;
        if (root.OuterTypeParameters is not null)
        {
            // We are instantiating a conditional type that has one or more type parameters in scope. Apply the
            // mapper to the type parameters to produce the effective list of type arguments, and compute the
            // instantiation cache key from the type IDs of the type arguments.
            var typeArguments = Array.ConvertAll(root.OuterTypeParameters, t => GetMappedType(t, mapper));
            var id = (forConstraint ? "C" : "") + GetTypeListId(typeArguments) + GetAliasId(aliasSymbol, aliasTypeArguments);
            root.Instantiations ??= new Dictionary<string, Type>(StringComparer.Ordinal);
            if (!root.Instantiations.TryGetValue(id, out var result))
            {
                var newMapper = CreateTypeMapper(root.OuterTypeParameters, typeArguments);
                var checkType = root.CheckType;
                var distributionType = root.IsDistributive ? GetReducedType(GetMappedType(checkType, newMapper)) : Type.None;
                // Distributive conditional types are distributed over union types. For example, when the
                // distributive conditional type T extends U ? X : Y is instantiated with A | B for T, the
                // result is (A extends U ? X : Y) | (B extends U ? X : Y).
                result = !distributionType.IsNone && checkType != distributionType && (Types.Flags(distributionType) & (TypeFlags.Union | TypeFlags.Never)) != 0
                    ? MapTypeWithAlias(distributionType, t => GetConditionalType(root, PrependTypeMapping(checkType, t, newMapper), forConstraint), aliasSymbol, aliasTypeArguments)
                    : GetConditionalType(root, newMapper, forConstraint, aliasSymbol, aliasTypeArguments);
                root.Instantiations[id] = result;
            }
            return result;
        }
        return type;
    }

    /// <summary>checker.ts getTrueTypeFromConditionalType.</summary>
    internal Type GetTrueTypeFromConditionalType(Type type)
    {
        var data = Types.Data<ConditionalData>(type);
        if (data.ResolvedTrueType.IsNone)
        {
            data.ResolvedTrueType = InstantiateType(GetTypeFromTypeNode(At(data.Root.Node, StoreOf(data.Root.Node).TrueType(data.Root.Node.Node))), data.Mapper);
        }
        return data.ResolvedTrueType;
    }

    /// <summary>checker.ts getFalseTypeFromConditionalType.</summary>
    internal Type GetFalseTypeFromConditionalType(Type type)
    {
        var data = Types.Data<ConditionalData>(type);
        if (data.ResolvedFalseType.IsNone)
        {
            data.ResolvedFalseType = InstantiateType(GetTypeFromTypeNode(At(data.Root.Node, StoreOf(data.Root.Node).FalseType(data.Root.Node.Node))), data.Mapper);
        }
        return data.ResolvedFalseType;
    }

    // checker.ts getInferredTrueTypeFromConditionalType
    private Type GetInferredTrueTypeFromConditionalType(Type type)
    {
        var data = Types.Data<ConditionalData>(type);
        if (data.ResolvedInferredTrueType.IsNone)
        {
            data.ResolvedInferredTrueType = data.CombinedMapper is not null
                ? InstantiateType(GetTypeFromTypeNode(At(data.Root.Node, StoreOf(data.Root.Node).TrueType(data.Root.Node.Node))), data.CombinedMapper)
                : GetTrueTypeFromConditionalType(type);
        }
        return data.ResolvedInferredTrueType;
    }

    // checker.ts getDefaultConstraintOfConditionalType
    private Type GetDefaultConstraintOfConditionalType(Type type)
    {
        var data = Types.Data<ConditionalData>(type);
        if (data.ResolvedDefaultConstraint.IsNone)
        {
            // An `any` branch of a conditional type would normally be viral - specifically, without special handling here,
            // a conditional type with a single branch of type `any` would be assignable to anything, since it's constraint would simplify to
            // just `any`. This result is _usually_ unwanted - so instead here we elide an `any` branch from the constraint type,
            // in effect treating `any` like `never` rather than `unknown` in this location.
            var trueConstraint = GetInferredTrueTypeFromConditionalType(type);
            var falseConstraint = GetFalseTypeFromConditionalType(type);
            data.ResolvedDefaultConstraint = (Types.Flags(trueConstraint) & TypeFlags.Any) != 0 ? falseConstraint
                : (Types.Flags(falseConstraint) & TypeFlags.Any) != 0 ? trueConstraint
                : GetUnionType([trueConstraint, falseConstraint]);
        }
        return data.ResolvedDefaultConstraint;
    }

    // checker.ts getConstraintOfDistributiveConditionalType
    private Type GetConstraintOfDistributiveConditionalType(Type type)
    {
        var data = Types.Data<ConditionalData>(type);
        if (!data.ResolvedConstraintOfDistributive.IsNone)
        {
            return data.ResolvedConstraintOfDistributive == noConstraintType ? Type.None : data.ResolvedConstraintOfDistributive;
        }
        // Check if we have a conditional type of the form 'T extends U ? X : Y', where T is a constrained
        // type parameter. If so, create an instantiation of the conditional type where T is replaced
        // with its constraint. We do this because if the constraint is a union type it will be distributed
        // over the conditional type and possibly reduced. For example, 'T extends undefined ? never : T'
        // removes 'undefined' from T.
        if (data.Root.IsDistributive && !IsOwnRestrictiveInstantiation(type))
        {
            var simplified = GetSimplifiedType(data.CheckType, writing: false);
            var constraint = simplified == data.CheckType ? GetConstraintOfType(simplified) : simplified;
            if (!constraint.IsNone && constraint != data.CheckType)
            {
                var instantiated = GetConditionalTypeInstantiation(type, PrependTypeMapping(data.Root.CheckType, constraint, data.Mapper), forConstraint: true);
                if ((Types.Flags(instantiated) & TypeFlags.Never) == 0)
                {
                    data.ResolvedConstraintOfDistributive = instantiated;
                    return instantiated;
                }
            }
        }
        data.ResolvedConstraintOfDistributive = noConstraintType;
        return Type.None;
    }

    // checker.ts getConstraintFromConditionalType
    private Type GetConstraintFromConditionalType(Type type)
    {
        var distributive = GetConstraintOfDistributiveConditionalType(type);
        return !distributive.IsNone ? distributive : GetDefaultConstraintOfConditionalType(type);
    }

    // checker.ts getConstraintOfConditionalType
    private Type GetConstraintOfConditionalType(Type type) => HasNonCircularBaseConstraint(type) ? GetConstraintFromConditionalType(type) : Type.None;
}
