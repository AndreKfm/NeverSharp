// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (createUnionOrIntersectionProperty, getUnionOrIntersectionProperty, the
// deferred types of synthetic properties, getReducedType's never-reduction, isDiscriminantProperty, isLiteralType)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C4 (replaces module C1's approximation of synthetic union and
// intersection properties). The property caches of union and intersection types and the deferred constituents of
// synthetic properties are per-checker dictionaries.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // (types.ts UnionOrIntersectionType.propertyCache / propertyCacheWithoutObjectFunctionPropertyAugment /
    // resolvedProperties: in the type's UnionData record)

    // The tables ComputePropertiesOfUnionOrIntersectionType collects names in (it can recurse), reused
    private readonly List<SymTable> scratchTables = [];
    private int scratchTablesInUse;

    // SymbolLinks.deferralParent / deferralConstituents / deferralWriteConstituents
    private readonly Dictionary<SymbolRef, (Type Parent, Type[] Constituents, Type[]? WriteConstituents)> deferredTypeSymbols = [];

    // types.ts IntersectionType.uniqueLiteralFilledInstantiation
    private readonly Dictionary<Type, Type> uniqueLiteralFilledInstantiations = [];

    // SymbolLinks.isDiscriminantProperty
    private readonly Dictionary<SymbolRef, bool> discriminantProperties = [];

    // checker.ts createUnionOrIntersectionProperty
    private SymbolRef CreateUnionOrIntersectionProperty(Type containingType, string name, bool skipObjectFunctionPropertyAugment)
    {
        var propFlags = SymbolFlags.None;
        var singleProp = SymbolRef.None;
        List<SymbolRef>? propSet = null;
        List<Type>? indexTypes = null;
        var isUnion = (Types.Flags(containingType) & TypeFlags.Union) != 0;
        // Flags we want to propagate to the result if they exist in all source symbols
        SymbolFlags? optionalFlag = null;
        var syntheticFlag = CheckFlags.SyntheticMethod;
        var checkFlags = isUnion ? 0 : CheckFlags.Readonly;
        var mergedInstantiations = false;
        var containingTypes = Types.Union(containingType).Types;
        for (var ci = 0; ci < containingTypes.Count; ci++)
        {
            var current = Types.Element(containingTypes, ci);
            var type = GetApparentType(current);
            if (!(IsErrorType(type) || (Types.Flags(type) & TypeFlags.Never) != 0))
            {
                var prop = GetPropertyOfType(type, name, skipObjectFunctionPropertyAugment);
                var modifiers = !prop.IsNone ? GetDeclarationModifierFlagsFromSymbol(prop) : 0;
                if (!prop.IsNone)
                {
                    var propSymbolFlags = SymbolFlagsOf(prop);
                    if ((propSymbolFlags & SymbolFlags.ClassMember) != 0)
                    {
                        optionalFlag ??= isUnion ? SymbolFlags.None : SymbolFlags.Optional;
                        if (isUnion)
                        {
                            optionalFlag |= propSymbolFlags & SymbolFlags.Optional;
                        }
                        else
                        {
                            optionalFlag &= propSymbolFlags;
                        }
                    }
                    if (singleProp.IsNone)
                    {
                        singleProp = prop;
                        propFlags = (propSymbolFlags & SymbolFlags.Accessor) != 0 ? propSymbolFlags & SymbolFlags.Accessor : SymbolFlags.Property;
                    }
                    else if (prop != singleProp)
                    {
                        var isInstantiation = GetTargetSymbol(prop) == GetTargetSymbol(singleProp);
                        // If the symbols are instances of one another with identical types - consider the symbols
                        // equivalent and just use the first one, which thus allows us to avoid eliding private
                        // members when intersecting a (this-)instantiations of a class with its raw base or another instance
                        if (isInstantiation && CompareProperties(singleProp, prop, (a, b) => a == b ? Ternary.True : Ternary.False) == Ternary.True)
                        {
                            // If we merged instantiations of a generic type, we replicate the symbol parent resetting behavior we used
                            // to do when we recorded multiple distinct symbols so that we still get, eg, `Array<T>.length` printed
                            // back and not `Array<string>.length` when we're looking at a `.length` access on a `string[] | number[]`
                            var singleParent = ParentOf(singleProp);
                            mergedInstantiations = !singleParent.IsNone && GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(singleParent) is { Count: > 0 };
                        }
                        else
                        {
                            propSet ??= new List<SymbolRef>(containingTypes.Count) { singleProp };
                            if (!propSet.Contains(prop))
                            {
                                propSet.Add(prop);
                            }
                        }
                        // classes created by mixins are represented as intersections
                        // and overriding a property in a derived class redefines it completely at runtime
                        // so a get accessor can't be merged with a set accessor in a base class,
                        // for that reason the accessor flags are only used when they are the same in all constituents
                        if ((propFlags & SymbolFlags.Accessor) != 0 && (propSymbolFlags & SymbolFlags.Accessor) != (propFlags & SymbolFlags.Accessor))
                        {
                            propFlags = (propFlags & ~SymbolFlags.Accessor) | SymbolFlags.Property;
                        }
                    }
                    if (isUnion && IsReadonlySymbol(prop))
                    {
                        checkFlags |= CheckFlags.Readonly;
                    }
                    else if (!isUnion && !IsReadonlySymbol(prop))
                    {
                        checkFlags &= ~CheckFlags.Readonly;
                    }
                    checkFlags |= ((modifiers & ModifierFlags.NonPublicAccessibilityModifier) == 0 ? CheckFlags.ContainsPublic : 0) |
                        ((modifiers & ModifierFlags.Protected) != 0 ? CheckFlags.ContainsProtected : 0) |
                        ((modifiers & ModifierFlags.Private) != 0 ? CheckFlags.ContainsPrivate : 0) |
                        ((modifiers & ModifierFlags.Static) != 0 ? CheckFlags.ContainsStatic : 0);
                    if (!IsPrototypeProperty(prop))
                    {
                        syntheticFlag = CheckFlags.SyntheticProperty;
                    }
                }
                else if (isUnion)
                {
                    var indexInfo = !IsLateBoundName(name) ? GetApplicableIndexInfoForName(type, name) : null;
                    if (indexInfo is not null)
                    {
                        propFlags = (propFlags & ~SymbolFlags.Accessor) | SymbolFlags.Property;
                        checkFlags |= CheckFlags.WritePartial | (indexInfo.IsReadonly ? CheckFlags.Readonly : 0);
                        Type indexType;
                        if (IsTupleType(type))
                        {
                            var rest = GetRestTypeOfTupleType(type);
                            indexType = rest.IsNone ? undefinedType : rest;
                        }
                        else
                        {
                            indexType = indexInfo.Type;
                        }
                        (indexTypes ??= []).Add(indexType);
                    }
                    else if (IsObjectLiteralType(type) && (Types.ObjectFlagsOf(type) & ObjectFlags.ContainsSpread) == 0)
                    {
                        checkFlags |= CheckFlags.WritePartial;
                        (indexTypes ??= []).Add(undefinedType);
                    }
                    else
                    {
                        checkFlags |= CheckFlags.ReadPartial;
                    }
                }
            }
        }
        if (singleProp.IsNone ||
            (isUnion &&
                (propSet is not null || (checkFlags & CheckFlags.Partial) != 0) &&
                (checkFlags & (CheckFlags.ContainsPrivate | CheckFlags.ContainsProtected)) != 0 &&
                !(propSet is not null && HasCommonDeclarationsOfSymbols(propSet))))
        {
            // No property was found, or, in a union, a property has a private or protected declaration in one
            // constituent, but is missing or has a different declaration in another constituent.
            return SymbolRef.None;
        }
        if (propSet is null && (checkFlags & CheckFlags.ReadPartial) == 0 && indexTypes is null)
        {
            if (mergedInstantiations)
            {
                // No symbol from a union/intersection should have a `.parent` set (since unions/intersections don't act as symbol parents)
                // Unless that parent is "reconstituted" from the "first value declaration" on the symbol (which is likely different than its instantiated parent!)
                // They also have a `.containingType` set, which affects some services endpoints behavior, like `getRootSymbol`
                var linksType = singleProp.IsTransient && ValueSymbolLinks.Has(singleProp) ? ValueSymbolLinks.Get(singleProp).ResolvedType : Type.None;
                var linksMapper = singleProp.IsTransient && ValueSymbolLinks.Has(singleProp) ? ValueSymbolLinks.Get(singleProp).Mapper : 0;
                var clone = CreateSymbolWithType(singleProp, linksType);
                var valueDeclaration = ValueDeclarationOf(singleProp);
                var declarationSymbol = valueDeclaration.IsNone ? SymbolRef.None : NodeSymbol(valueDeclaration);
                TransientSymbols.Record(clone).Parent = declarationSymbol.IsNone ? SymbolRef.None : ParentOf(declarationSymbol);
                var writeType = GetWriteTypeOfSymbol(singleProp);
                ref var cloneLinks = ref ValueSymbolLinks.Get(clone);
                cloneLinks.ContainingType = containingType;
                cloneLinks.Mapper = linksMapper;
                cloneLinks.WriteType = writeType;
                return clone;
            }
            return singleProp;
        }
        var props = propSet ?? [singleProp];
        var declarations = new List<NodeRef>(props.Count);
        var hasDeclarations = false;
        var firstType = Type.None;
        var nameType = Type.None;
        var propTypes = new List<Type>(props.Count + (indexTypes?.Count ?? 0));
        List<Type>? writeTypes = null;
        var firstValueDeclaration = NodeRef.None;
        var hasNonUniformValueDeclaration = false;
        foreach (var prop in props)
        {
            var valueDeclaration = ValueDeclarationOf(prop);
            if (firstValueDeclaration.IsNone)
            {
                firstValueDeclaration = valueDeclaration;
            }
            else if (!valueDeclaration.IsNone && valueDeclaration != firstValueDeclaration)
            {
                hasNonUniformValueDeclaration = true;
            }
            if (DeclarationsOf(prop) is { } propDeclarations)
            {
                declarations.AddRange(propDeclarations);
                hasDeclarations = true;
            }
            var type = GetTypeOfSymbol(prop);
            if (firstType.IsNone)
            {
                firstType = type;
                nameType = GetNameTypeOfSymbol(prop);
            }
            var writeType = GetWriteTypeOfSymbol(prop);
            if (writeTypes is not null || writeType != type)
            {
                writeTypes ??= [.. propTypes];
                writeTypes.Add(writeType);
            }
            if (type != firstType)
            {
                checkFlags |= CheckFlags.HasNonUniformType;
            }
            if (IsLiteralType(type) || IsPatternLiteralType(type))
            {
                checkFlags |= CheckFlags.HasLiteralType;
            }
            if ((Types.Flags(type) & TypeFlags.Never) != 0 && type != uniqueLiteralType)
            {
                checkFlags |= CheckFlags.HasNeverType;
            }
            propTypes.Add(type);
        }
        if (indexTypes is not null)
        {
            propTypes.AddRange(indexTypes);
        }
        var result = CreateSymbol(propFlags | (optionalFlag ?? 0), name, syntheticFlag | checkFlags);
        ValueSymbolLinks.Get(result).ContainingType = containingType;
        if (!hasNonUniformValueDeclaration && !firstValueDeclaration.IsNone)
        {
            TransientSymbols.Record(result).ValueDeclaration = firstValueDeclaration;
            // Inherit information about parent type.
            var declarationSymbol = NodeSymbol(firstValueDeclaration);
            if (!declarationSymbol.IsNone && !ParentOf(declarationSymbol).IsNone)
            {
                TransientSymbols.Record(result).Parent = ParentOf(declarationSymbol);
            }
        }
        TransientSymbols.Record(result).Declarations = hasDeclarations ? declarations : null;
        ValueSymbolLinks.Get(result).NameType = nameType;
        if (propTypes.Count > 2)
        {
            // When `propTypes` has the potential to explode in size when normalized, defer normalization until absolutely needed
            TransientSymbols.Record(result).CheckFlags |= CheckFlags.DeferredType;
            deferredTypeSymbols[result] = (containingType, [.. propTypes], writeTypes?.ToArray());
        }
        else
        {
            var resultType = isUnion ? GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(propTypes)) : GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(propTypes));
            ValueSymbolLinks.Get(result).ResolvedType = resultType;
            if (writeTypes is not null)
            {
                var resultWriteType = isUnion ? GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(writeTypes)) : GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(writeTypes));
                ValueSymbolLinks.Get(result).WriteType = resultWriteType;
            }
        }
        return result;
    }

    // checker.ts getTypeOfSymbolWithDeferredType
    private Type GetTypeOfSymbolWithDeferredType(SymbolRef symbol)
    {
        ref var links = ref ValueSymbolLinks.Get(symbol);
        if (links.ResolvedType.IsNone)
        {
            var (parent, constituents, _) = deferredTypeSymbols[symbol];
            var type = (Types.Flags(parent) & TypeFlags.Union) != 0 ? GetUnionType(constituents) : GetIntersectionType(constituents);
            ValueSymbolLinks.Get(symbol).ResolvedType = type;
        }
        return ValueSymbolLinks.Get(symbol).ResolvedType;
    }

    // checker.ts getWriteTypeOfSymbolWithDeferredType (None when there are no write constituents)
    private Type GetWriteTypeOfSymbolWithDeferredType(SymbolRef symbol)
    {
        var (parent, _, writeConstituents) = deferredTypeSymbols[symbol];
        if (ValueSymbolLinks.Get(symbol).WriteType.IsNone && writeConstituents is not null)
        {
            var type = (Types.Flags(parent) & TypeFlags.Union) != 0 ? GetUnionType(writeConstituents) : GetIntersectionType(writeConstituents);
            ValueSymbolLinks.Get(symbol).WriteType = type;
        }
        return ValueSymbolLinks.Get(symbol).WriteType;
    }

    // checker.ts getCommonDeclarationsOfSymbols (whether the set is non-empty)
    private bool HasCommonDeclarationsOfSymbols(List<SymbolRef> symbols)
    {
        List<NodeRef>? commonDeclarations = null;
        foreach (var symbol in symbols)
        {
            var declarations = DeclarationsOf(symbol);
            if (declarations is null)
            {
                return false;
            }
            if (commonDeclarations is null)
            {
                commonDeclarations = [.. declarations];
                continue;
            }
            commonDeclarations.RemoveAll(d => !declarations.Contains(d));
            if (commonDeclarations.Count == 0)
            {
                return false;
            }
        }
        return commonDeclarations is not null;
    }

    // checker.ts isPrototypeProperty
    private bool IsPrototypeProperty(SymbolRef symbol)
    {
        if ((SymbolFlagsOf(symbol) & SymbolFlags.Method) != 0 || (GetCheckFlags(symbol) & CheckFlags.SyntheticMethod) != 0)
        {
            return true;
        }
        var valueDeclaration = ValueDeclarationOf(symbol);
        if (!valueDeclaration.IsNone && Utilities.IsInJSFile(StoreOf(valueDeclaration), valueDeclaration.Node))
        {
            var parent = Parent(valueDeclaration);
            return !parent.IsNone && Kind(parent) == SyntaxKind.BinaryExpression &&
                Utilities.GetAssignmentDeclarationKind(StoreOf(parent), parent.Node) == AssignmentDeclarationKind.PrototypeProperty;
        }
        return false;
    }

    // checker.ts createSymbolWithType
    private SymbolRef CreateSymbolWithType(SymbolRef source, Type type)
    {
        var symbol = CreateSymbol(SymbolFlagsOf(source), NameOf(source), GetCheckFlags(source) & CheckFlags.Readonly);
        ref var record = ref TransientSymbols.Record(symbol);
        record.Declarations = DeclarationsOf(source);
        record.Parent = ParentOf(source);
        var valueDeclaration = ValueDeclarationOf(source);
        if (!valueDeclaration.IsNone)
        {
            record.ValueDeclaration = valueDeclaration;
        }
        var nameType = GetNameTypeOfSymbol(source);
        ref var links = ref ValueSymbolLinks.Get(symbol);
        links.ResolvedType = type;
        links.Target = source;
        if (!nameType.IsNone)
        {
            links.NameType = nameType;
        }
        return symbol;
    }

    // checker.ts getUnionOrIntersectionProperty: the property, which may be present in some constituents only (a
    // partial property of a union type, needed to identify discriminant properties).
    private SymbolRef GetUnionOrIntersectionProperty(Type type, string name, bool skipObjectFunctionPropertyAugment)
    {
        var cache = skipObjectFunctionPropertyAugment ? Types.Union(type).PropertyCacheWithoutObjectFunctionPropertyAugment : Types.Union(type).PropertyCache;
        var property = cache?.Get(name) ?? SymbolRef.None;
        if (property.IsNone)
        {
            property = CreateUnionOrIntersectionProperty(type, name, skipObjectFunctionPropertyAugment);
            if (!property.IsNone)
            {
                // (the record is read again: creating the property can grow the union store)
                ref var data = ref Types.Union(type);
                if (skipObjectFunctionPropertyAugment)
                {
                    (data.PropertyCacheWithoutObjectFunctionPropertyAugment ??= new SymTable()).Set(name, property);
                    // Propagate an entry from the non-augmented cache to the augmented cache unless the property is partial.
                    if ((GetCheckFlags(property) & CheckFlags.Partial) == 0)
                    {
                        data.PropertyCache ??= new SymTable();
                        if (!data.PropertyCache.Has(name))
                        {
                            data.PropertyCache.Set(name, property);
                        }
                    }
                }
                else
                {
                    (data.PropertyCache ??= new SymTable()).Set(name, property);
                }
            }
        }
        return property;
    }

    /// <summary>checker.ts getPropertyOfUnionOrIntersectionType: partial properties of union types filtered out.</summary>
    private SymbolRef GetPropertyOfUnionOrIntersectionType(Type type, string name, bool skipObjectFunctionPropertyAugment)
    {
        var property = GetUnionOrIntersectionProperty(type, name, skipObjectFunctionPropertyAugment);
        // We need to filter out partial properties in union types
        return !property.IsNone && (GetCheckFlags(property) & CheckFlags.ReadPartial) == 0 ? property : SymbolRef.None;
    }

    /// <summary>checker.ts getPropertiesOfUnionOrIntersectionType.</summary>
    internal List<SymbolRef> GetPropertiesOfUnionOrIntersectionType(Type type)
    {
        if (Types.Union(type).ResolvedProperties is { } resolved)
        {
            return resolved;
        }
        resolved = ComputePropertiesOfUnionOrIntersectionType(type);
        Types.Union(type).ResolvedProperties = resolved;
        return resolved;
    }

    private List<SymbolRef> ComputePropertiesOfUnionOrIntersectionType(Type type)
    {
        // (createSymbolTable(): a reused table, cleared when the result is taken)
        if (scratchTablesInUse == scratchTables.Count)
        {
            scratchTables.Add(new SymTable());
        }
        var members = scratchTables[scratchTablesInUse++];
        var isUnion = (Types.Flags(type) & TypeFlags.Union) != 0;
        var isIntersection = (Types.Flags(type) & TypeFlags.Intersection) != 0;
        var constituents = Types.Union(type).Types;
        for (var ci = 0; ci < constituents.Count; ci++)
        {
            var current = Types.Element(constituents, ci);
            var properties = GetPropertiesOfType(current);
            if (ci == 0)
            {
                // (room for the first constituent's names in the tables this loop fills; an empty cache table is
                // the same as none)
                members.EnsureCapacity(properties.Count);
                ref var data = ref Types.Union(type);
                (data.PropertyCache ??= new SymTable(properties.Count)).EnsureCapacity(properties.Count);
                if (isIntersection)
                {
                    (data.PropertyCacheWithoutObjectFunctionPropertyAugment ??= new SymTable(properties.Count)).EnsureCapacity(properties.Count);
                }
            }
            foreach (var prop in properties)
            {
                var name = NameOf(prop);
                if (!members.Has(name))
                {
                    var combinedProp = GetPropertyOfUnionOrIntersectionType(type, name, isIntersection);
                    if (!combinedProp.IsNone)
                    {
                        members.Set(name, combinedProp);
                    }
                }
            }
            // The properties of a union type are those that are present in all constituent types, so
            // we only need to check the properties of the first type without index signature
            if (isUnion && GetIndexInfosOfType(current).Count == 0)
            {
                break;
            }
        }
        var result = GetNamedMembers(members);
        members.Clear();
        scratchTablesInUse--;
        return result;
    }

    /// <summary>checker.ts getApplicableIndexInfoForName.</summary>
    internal IndexInfo? GetApplicableIndexInfoForName(Type type, string name) =>
        GetApplicableIndexInfo(type, IsLateBoundName(name) ? esSymbolType : GetStringLiteralType(Utilities.UnescapeLeadingUnderscores(name)));

    /// <summary>checker.ts isLiteralType.</summary>
    internal bool IsLiteralType(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Boolean) != 0)
        {
            return true;
        }
        if ((flags & TypeFlags.Union) != 0)
        {
            if ((flags & TypeFlags.EnumLiteral) != 0)
            {
                return true;
            }
            foreach (var t in Types.Elements(Types.Union(type).Types))
            {
                if ((Types.Flags(t) & TypeFlags.Unit) == 0)
                {
                    return false;
                }
            }
            return true;
        }
        return (flags & TypeFlags.Unit) != 0;
    }

    // ---- getReducedType's never-reduction ----

    // checker.ts isNeverReducedProperty
    private bool IsNeverReducedProperty(SymbolRef prop) => IsDiscriminantWithNeverType(prop) || IsConflictingPrivateProperty(prop);

    // checker.ts isDiscriminantWithNeverType: a synthetic non-optional property with non-uniform types, where at
    // least one is a literal type and none is never, that reduces to never.
    private bool IsDiscriminantWithNeverType(SymbolRef prop) =>
        (SymbolFlagsOf(prop) & SymbolFlags.Optional) == 0 &&
        (GetCheckFlags(prop) & (CheckFlags.Discriminant | CheckFlags.HasNeverType)) == CheckFlags.Discriminant &&
        (Types.Flags(GetTypeOfSymbol(prop)) & TypeFlags.Never) != 0;

    // checker.ts isConflictingPrivateProperty: a synthetic property with multiple declarations, at least one of which
    // is private.
    private bool IsConflictingPrivateProperty(SymbolRef prop) =>
        ValueDeclarationOf(prop).IsNone && (GetCheckFlags(prop) & CheckFlags.ContainsPrivate) != 0;

    // checker.ts isReducibleIntersection
    private bool IsReducibleIntersection(Type type)
    {
        if (!uniqueLiteralFilledInstantiations.TryGetValue(type, out var uniqueFilled))
        {
            uniqueFilled = InstantiateType(type, UniqueLiteralMapper);
            uniqueLiteralFilledInstantiations[type] = uniqueFilled;
        }
        return GetReducedType(uniqueFilled) != uniqueFilled;
    }

    // checker.ts elaborateNeverIntersection
    private DiagnosticMessageChain? ElaborateNeverIntersection(DiagnosticMessageChain? errorInfo, Type type)
    {
        if ((Types.Flags(type) & TypeFlags.Intersection) != 0 && (Types.ObjectFlagsOf(type) & ObjectFlags.IsNeverIntersection) != 0)
        {
            var properties = GetPropertiesOfUnionOrIntersectionType(type);
            var neverProp = properties.Find(IsDiscriminantWithNeverType);
            if (!neverProp.IsNone)
            {
                return ChainDiagnosticMessages(errorInfo, Diagnostics.The_intersection_0_was_reduced_to_never_because_property_1_has_conflicting_types_in_some_constituents,
                    TypeToString(type, default, TypeFormatFlags.NoTypeReduction), SymbolToString(neverProp));
            }
            var privateProp = properties.Find(IsConflictingPrivateProperty);
            if (!privateProp.IsNone)
            {
                return ChainDiagnosticMessages(errorInfo, Diagnostics.The_intersection_0_was_reduced_to_never_because_property_1_exists_in_multiple_constituents_and_is_private_in_some,
                    TypeToString(type, default, TypeFormatFlags.NoTypeReduction), SymbolToString(privateProp));
            }
        }
        return errorInfo;
    }

    /// <summary>checker.ts isDiscriminantProperty.</summary>
    internal bool IsDiscriminantProperty(Type type, string name)
    {
        if (!type.IsNone && (Types.Flags(type) & TypeFlags.Union) != 0)
        {
            var prop = GetUnionOrIntersectionProperty(type, name, skipObjectFunctionPropertyAugment: false);
            if (!prop.IsNone && (GetCheckFlags(prop) & CheckFlags.SyntheticProperty) != 0)
            {
                if (!discriminantProperties.TryGetValue(prop, out var isDiscriminant))
                {
                    isDiscriminant = (GetCheckFlags(prop) & CheckFlags.Discriminant) == CheckFlags.Discriminant && !IsGenericType(GetTypeOfSymbol(prop));
                    discriminantProperties[prop] = isDiscriminant;
                }
                return isDiscriminant;
            }
        }
        return false;
    }

    /// <summary>utilities.ts chainDiagnosticMessages with a single detail (or none).</summary>
    internal static DiagnosticMessageChain ChainDiagnosticMessages(DiagnosticMessageChain? details, DiagnosticMessage message, params object?[] args) =>
        ProgramDiagnostics.ChainDiagnosticMessages(details is null ? null : [details], message, args);
}
