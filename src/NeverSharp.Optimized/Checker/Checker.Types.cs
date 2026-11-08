// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// Portions Copyright 2026 Max Schwenk and the tsrs contributors
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (createIntrinsicType and the intrinsic types of createTypeChecker, createLiteralType,
// getFreshTypeOfLiteralType, getRegularTypeOfLiteralType, get*LiteralType, getUnionType and its helpers, getIntersectionType,
// createObjectType, createAnonymousType, createTypeReference, createArrayType, getGlobalType, getTypeOfGlobalSymbol,
// getDeclaredTypeOf*, getWidenedLiteralType, getBaseTypeOfLiteralType)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go (type creation, union and intersection construction)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C1. Types are handles in creation order (the handle is the type id,
// docs/phase3-checker-layout.md), so union members sorted by id are sorted by handle. Module C4: subtype reduction
// (removeSubtypes), removeStringLiteralsMatchedByTemplateLiterals, removeConstrainedTypeVariables and the intersection
// reductions that need the relations (T & primitive, redundant template literals, IntersectionFlags).

using System.Globalization;
using System.Text;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>checker.ts UnionReduction.</summary>
internal enum UnionReduction
{
    None = 0,
    Literal = 1,
    Subtype = 2,
}

internal sealed partial class Checker
{
    // intrinsic types (createTypeChecker's vars), in TypeScript's creation order
    private Type anyType;
    private Type autoType;
    private Type wildcardType;
    private Type blockedStringType;
    private Type errorType;
    private Type unresolvedType;
    private Type nonInferrableAnyType;
    private Type intrinsicMarkerType;
    private Type unknownType;
    private Type undefinedType;
    private Type undefinedWideningType;
    private Type missingType;
    private Type undefinedOrMissingType;
    private Type optionalType;
    private Type nullType;
    private Type nullWideningType;
    private Type stringType;
    private Type numberType;
    private Type bigintType;
    private Type falseType;
    private Type regularFalseType;
    private Type trueType;
    private Type regularTrueType;
    private Type booleanType;
    private Type esSymbolType;
    private Type voidType;
    private Type neverType;
    private Type silentNeverType;
    private Type implicitNeverType;
    private Type unreachableNeverType;
    private Type nonPrimitiveType;
    private Type stringOrNumberType;
    private Type stringNumberSymbolType;
    private Type numberOrBigIntType;
    private Type templateConstraintType;
    private Type uniqueLiteralType;
    private Type emptyObjectType;
    private Type emptyJsxObjectType;
    private Type emptyFreshJsxObjectType;
    private SymbolRef emptyTypeLiteralSymbol;
    private Type emptyTypeLiteralType;
    private Type unknownEmptyObjectType;
    private Type unknownUnionType;
    private Type emptyGenericType;
    private Type anyFunctionType;
    private Type noConstraintType;
    private Type circularConstraintType;
    private Type resolvingDefaultType;

    // global types (initializeTypeChecker)
    private Type globalArrayType;
    private Type globalObjectType;
    private Type globalFunctionType;
    private Type globalCallableFunctionType;
    private Type globalNewableFunctionType;
    private Type globalStringType;
    private Type globalNumberType;
    private Type globalBooleanType;
    private Type globalRegExpType;
    private Type anyArrayType;
    private Type autoArrayType;
    private Type globalReadonlyArrayType;
    private Type anyReadonlyArrayType;
    private Type globalThisType;

    private readonly Dictionary<string, Type> stringLiteralTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<double, Type> numberLiteralTypes = [];
    private readonly Dictionary<string, Type> bigIntLiteralTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> enumLiteralTypes = new(StringComparer.Ordinal);
    private readonly KeyMap<Type> unionTypes = new();
    private readonly KeyMap<Type> unionOfUnionTypes = new();
    private readonly KeyMap<Type> intersectionTypes = new();
    private readonly Dictionary<Type, Type> freshTypes = [];
    private readonly Dictionary<Type, Type> regularUnionTypes = [];
    private readonly Dictionary<string, SymbolRef> unresolvedSymbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> errorTypes = new(StringComparer.Ordinal);

    // createTypeChecker: the intrinsic types and the types built from them, in TypeScript's order.
    private void CreateIntrinsicTypes()
    {
        const ObjectFlags computed = ObjectFlags.CouldContainTypeVariablesComputed | ObjectFlags.IsGenericTypeComputed | ObjectFlags.IsUnknownLikeUnionComputed | ObjectFlags.IsNeverIntersectionComputed;
        Type Intrinsic(TypeFlags kind, string name, ObjectFlags objectFlags = ObjectFlags.None, string? debugName = null) =>
            Types.CreateIntrinsic(kind, name, objectFlags | computed, debugName);
        anyType = Intrinsic(TypeFlags.Any, "any");
        autoType = Intrinsic(TypeFlags.Any, "any", ObjectFlags.NonInferrableType, "auto");
        wildcardType = Intrinsic(TypeFlags.Any, "any", ObjectFlags.None, "wildcard");
        blockedStringType = Intrinsic(TypeFlags.Any, "any", ObjectFlags.None, "blocked string");
        errorType = Intrinsic(TypeFlags.Any, "error");
        unresolvedType = Intrinsic(TypeFlags.Any, "unresolved");
        nonInferrableAnyType = Intrinsic(TypeFlags.Any, "any", ObjectFlags.ContainsWideningType, "non-inferrable");
        intrinsicMarkerType = Intrinsic(TypeFlags.Any, "intrinsic");
        unknownType = Intrinsic(TypeFlags.Unknown, "unknown");
        undefinedType = Intrinsic(TypeFlags.Undefined, "undefined");
        undefinedWideningType = strictNullChecks ? undefinedType : Intrinsic(TypeFlags.Undefined, "undefined", ObjectFlags.ContainsWideningType, "widening");
        missingType = Intrinsic(TypeFlags.Undefined, "undefined", ObjectFlags.None, "missing");
        undefinedOrMissingType = exactOptionalPropertyTypes ? missingType : undefinedType;
        optionalType = Intrinsic(TypeFlags.Undefined, "undefined", ObjectFlags.None, "optional");
        nullType = Intrinsic(TypeFlags.Null, "null");
        nullWideningType = strictNullChecks ? nullType : Intrinsic(TypeFlags.Null, "null", ObjectFlags.ContainsWideningType, "widening");
        stringType = Intrinsic(TypeFlags.String, "string");
        numberType = Intrinsic(TypeFlags.Number, "number");
        bigintType = Intrinsic(TypeFlags.BigInt, "bigint");
        falseType = Intrinsic(TypeFlags.BooleanLiteral, "false", ObjectFlags.None, "fresh");
        regularFalseType = Intrinsic(TypeFlags.BooleanLiteral, "false");
        trueType = Intrinsic(TypeFlags.BooleanLiteral, "true", ObjectFlags.None, "fresh");
        regularTrueType = Intrinsic(TypeFlags.BooleanLiteral, "true");
        freshTypes[trueType] = trueType;
        freshTypes[regularTrueType] = trueType;
        freshTypes[falseType] = falseType;
        freshTypes[regularFalseType] = falseType;
        booleanType = GetUnionType([regularFalseType, regularTrueType]);
        esSymbolType = Intrinsic(TypeFlags.ESSymbol, "symbol");
        voidType = Intrinsic(TypeFlags.Void, "void");
        neverType = Intrinsic(TypeFlags.Never, "never");
        silentNeverType = Intrinsic(TypeFlags.Never, "never", ObjectFlags.NonInferrableType, "silent");
        implicitNeverType = Intrinsic(TypeFlags.Never, "never", ObjectFlags.None, "implicit");
        unreachableNeverType = Intrinsic(TypeFlags.Never, "never", ObjectFlags.None, "unreachable");
        nonPrimitiveType = Intrinsic(TypeFlags.NonPrimitive, "object");
        stringOrNumberType = GetUnionType([stringType, numberType]);
        stringNumberSymbolType = GetUnionType([stringType, numberType, esSymbolType]);
        numberOrBigIntType = GetUnionType([numberType, bigintType]);
        templateConstraintType = GetUnionType([stringType, numberType, booleanType, bigintType, nullType, undefinedType]);
        numericStringType = GetTemplateLiteralType(["", ""], [numberType]);
        uniqueLiteralType = Intrinsic(TypeFlags.Never, "never", ObjectFlags.None, "unique literal");
        emptyObjectType = CreateAnonymousType(SymbolRef.None, emptySymbols);
        emptyJsxObjectType = CreateAnonymousType(SymbolRef.None, emptySymbols);
        Types.Record(emptyJsxObjectType).ObjectFlags |= ObjectFlags.JsxAttributes;
        emptyFreshJsxObjectType = CreateAnonymousType(SymbolRef.None, emptySymbols);
        Types.Record(emptyFreshJsxObjectType).ObjectFlags |= ObjectFlags.JsxAttributes | ObjectFlags.FreshLiteral | ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
        emptyTypeLiteralSymbol = CreateSymbol(SymbolFlags.TypeLiteral, InternalSymbolName.Type);
        TransientSymbols.Record(emptyTypeLiteralSymbol).Members = new SymTable(Names);
        emptyTypeLiteralType = CreateAnonymousType(emptyTypeLiteralSymbol, emptySymbols);
        unknownEmptyObjectType = CreateAnonymousType(SymbolRef.None, emptySymbols);
        unknownUnionType = strictNullChecks ? GetUnionType([undefinedType, nullType, unknownEmptyObjectType]) : unknownType;
        emptyGenericType = CreateAnonymousType(SymbolRef.None, emptySymbols);
        Types.Object(emptyGenericType).HasInstantiations = true;
        anyFunctionType = CreateAnonymousType(SymbolRef.None, emptySymbols);
        Types.Record(anyFunctionType).ObjectFlags |= ObjectFlags.NonInferrableType;
        noConstraintType = CreateAnonymousType(SymbolRef.None, emptySymbols);
        circularConstraintType = CreateAnonymousType(SymbolRef.None, emptySymbols);
        resolvingDefaultType = CreateAnonymousType(SymbolRef.None, emptySymbols);
        // markerSuperType, markerSubType, markerOtherType, markerSuperTypeForCheck, markerSubTypeForCheck (module C4)
        markerSuperType = Types.CreateTypeParameter(SymbolRef.None);
        markerSubType = Types.CreateTypeParameter(SymbolRef.None);
        Types.TypeParameter(markerSubType).Constraint = markerSuperType;
        markerOtherType = Types.CreateTypeParameter(SymbolRef.None);
        markerSuperTypeForCheck = Types.CreateTypeParameter(SymbolRef.None);
        markerSubTypeForCheck = Types.CreateTypeParameter(SymbolRef.None);
        Types.TypeParameter(markerSubTypeForCheck).Constraint = markerSuperTypeForCheck;
        // createTypeChecker, after the signatures and iteration types (module C3: these types take their ids here)
        emptyStringType = GetStringLiteralType("");
        zeroType = GetNumberLiteralType(0);
        zeroBigIntType = GetBigIntLiteralType("0");
        // createTypeofType: typeofNEFacts' keys in insertion order
        typeofType = GetUnionType([GetStringLiteralType("string"), GetStringLiteralType("number"), GetStringLiteralType("bigint"), GetStringLiteralType("boolean"),
            GetStringLiteralType("symbol"), GetStringLiteralType("undefined"), GetStringLiteralType("object"), GetStringLiteralType("function")]);
    }

    private Type emptyStringType;
    private Type zeroType;
    private Type zeroBigIntType;
    private Type typeofType;

    // ---- common accessors ----

    internal TypeFlags FlagsOf(Type type) => Types.Flags(type);

    internal ObjectFlags ObjectFlagsOf(Type type) => Types.ObjectFlagsOf(type);

    internal SymbolRef SymbolOfType(Type type) => Types.Symbol(type);

    /// <summary>The intrinsic name of an intrinsic type or of the boolean union (types.ts intrinsicName).</summary>
    internal string IntrinsicNameOf(Type type) =>
        (Types.Flags(type) & TypeFlags.Union) != 0 ? "boolean" : Types.IntrinsicName(type);

    private bool IsErrorType(Type type) =>
        // The only 'any' types that have alias symbols are those manufactured by getTypeFromTypeAliasReference for
        // a reference to an unresolved symbol. We want those to behave like the errorType.
        type == errorType || ((Types.Flags(type) & TypeFlags.Any) != 0 && !Types.Record(type).AliasSymbol.IsNone);

    // ---- literal types ----

    /// <summary>checker.ts createLiteralType.</summary>
    private Type CreateLiteralType(TypeFlags flags, object value, SymbolRef symbol = default, Type regularType = default) =>
        Types.CreateLiteral(flags, value, symbol, regularType);

    /// <summary>checker.ts getFreshTypeOfLiteralType.</summary>
    internal Type GetFreshTypeOfLiteralType(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Freshable) != 0)
        {
            if ((flags & TypeFlags.BooleanLiteral) != 0)
            {
                return freshTypes[type];
            }
            ref var literal = ref Types.Literal(type);
            if (literal.FreshType.IsNone)
            {
                var freshType = CreateLiteralType(flags, literal.Value!, Types.Symbol(type), type);
                Types.Literal(freshType).FreshType = freshType;
                Types.Literal(type).FreshType = freshType;
            }
            return Types.Literal(type).FreshType;
        }
        return type;
    }

    /// <summary>checker.ts getRegularTypeOfLiteralType.</summary>
    internal Type GetRegularTypeOfLiteralType(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Freshable) != 0)
        {
            if ((flags & TypeFlags.BooleanLiteral) != 0)
            {
                return type == trueType ? regularTrueType : type == falseType ? regularFalseType : type;
            }
            return Types.Literal(type).RegularType;
        }
        if ((flags & TypeFlags.Union) != 0)
        {
            if (!regularUnionTypes.TryGetValue(type, out var regular))
            {
                regular = MapType(type, GetRegularTypeOfLiteralType);
                regularUnionTypes[type] = regular;
            }
            return regular;
        }
        return type;
    }

    /// <summary>checker.ts isFreshLiteralType.</summary>
    internal bool IsFreshLiteralType(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Freshable) == 0)
        {
            return false;
        }
        if ((flags & TypeFlags.BooleanLiteral) != 0)
        {
            return type == trueType || type == falseType;
        }
        return Types.Literal(type).FreshType == type;
    }

    internal Type GetStringLiteralType(string value)
    {
        if (!stringLiteralTypes.TryGetValue(value, out var type))
        {
            stringLiteralTypes[value] = type = CreateLiteralType(TypeFlags.StringLiteral, value);
        }
        return type;
    }

    /// <summary>
    /// checker.ts getStringLiteralType for a value given by its characters (module AR2, Track O: the cache is asked
    /// with the characters, and a string is made only for a new literal type; tsrs checker_12.rs get_string_literal_type
    /// takes a &amp;str slice).
    /// </summary>
    internal Type GetStringLiteralType(ReadOnlySpan<char> value)
    {
        if (stringLiteralTypes.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(value, out var type))
        {
            return type;
        }
        return GetStringLiteralType(value.ToString());
    }

    /// <summary>getStringLiteralType of a node's text (module AR2: see the span overload).</summary>
    internal Type GetStringLiteralType(NodeStore store, TextRef text) => GetStringLiteralType(store.Texts.Span(text));

    internal Type GetNumberLiteralType(double value)
    {
        if (!numberLiteralTypes.TryGetValue(value, out var type))
        {
            numberLiteralTypes[value] = type = CreateLiteralType(TypeFlags.NumberLiteral, value);
        }
        return type;
    }

    /// <summary>checker.ts getBigIntLiteralType (the value is the normalized base-10 text, pseudoBigIntToString).</summary>
    internal Type GetBigIntLiteralType(string value)
    {
        if (!bigIntLiteralTypes.TryGetValue(value, out var type))
        {
            bigIntLiteralTypes[value] = type = CreateLiteralType(TypeFlags.BigIntLiteral, value);
        }
        return type;
    }

    /// <summary>checker.ts getEnumLiteralType.</summary>
    internal Type GetEnumLiteralType(object value, int enumId, SymbolRef symbol)
    {
        var key = enumId.ToString(CultureInfo.InvariantCulture) + (value is string ? "@" : "#") + (value is double d ? NumberToString(d) : (string)value);
        var flags = TypeFlags.EnumLiteral | (value is string ? TypeFlags.StringLiteral : TypeFlags.NumberLiteral);
        if (!enumLiteralTypes.TryGetValue(key, out var type))
        {
            enumLiteralTypes[key] = type = CreateLiteralType(flags, value, symbol);
        }
        return type;
    }

    /// <summary>JavaScript's Number.prototype.toString for the values literal types hold.</summary>
    internal static string NumberToString(double value) => JsNumber.ToString(value);

    // ---- union types ----

    // (the builder GetTypeListId reuses on this thread; the id is complete before the next call starts)
    [ThreadStatic]
    private static StringBuilder? typeListIdBuilder;

    private static string GetTypeListId(ReadOnlySpan<Type> types)
    {
        if (types.Length == 1)
        {
            return types[0].Id.ToString(CultureInfo.InvariantCulture);
        }
        var result = typeListIdBuilder ??= new StringBuilder();
        result.Clear();
        var length = types.Length;
        var i = 0;
        while (i < length)
        {
            var startId = types[i].Id;
            var count = 1;
            while (i + count < length && types[i + count].Id == startId + count)
            {
                count++;
            }
            if (result.Length > 0)
            {
                result.Append(',');
            }
            result.Append(CultureInfo.InvariantCulture, $"{startId}");
            if (count > 1)
            {
                result.Append(':').Append(CultureInfo.InvariantCulture, $"{count}");
            }
            i += count;
        }
        return result.ToString();
    }

    // checker.ts getAliasId as text (the error types of unresolved aliases print it)
    private static string GetAliasId(SymbolRef aliasSymbol, TypeList aliasTypeArguments, TypeStore types) =>
        aliasSymbol.IsNone ? "" : "@" + aliasSymbol.File.ToString(CultureInfo.InvariantCulture) + "." + aliasSymbol.Index.ToString(CultureInfo.InvariantCulture)
            + (aliasTypeArguments.IsPresent ? ":" + GetTypeListId(types.Elements(aliasTypeArguments)) : "");

    // The scratch stack of KeyMap keys (module CO2: the caches TypeScript keys by id strings)
    private readonly KeyScratch keyScratch = new();

    // checker.ts getAliasId as key ints: nothing (no alias: the empty string), or -2, the symbol and then -1 (no type
    // arguments) or the list. (Keys put it after a list or an id, so "nothing" cannot run into what follows.)
    private void AddAliasKey(SymbolRef aliasSymbol, TypeList aliasTypeArguments)
    {
        if (aliasSymbol.IsNone)
        {
            return;
        }
        keyScratch.Add(-2);
        keyScratch.Add(aliasSymbol.File);
        keyScratch.Add(aliasSymbol.Index);
        if (aliasTypeArguments.IsPresent)
        {
            keyScratch.AddTypes(Types.Elements(aliasTypeArguments));
        }
        else
        {
            keyScratch.Add(-1);
        }
    }

    private ObjectFlags GetPropagatingFlagsOfTypes(ReadOnlySpan<Type> types, TypeFlags excludeKinds = 0)
    {
        ObjectFlags result = 0;
        foreach (var type in types)
        {
            if (excludeKinds == 0 || (Types.Flags(type) & excludeKinds) == 0)
            {
                result |= Types.ObjectFlagsOf(type);
            }
        }
        return result & ObjectFlags.PropagatingFlags;
    }

    private static int BinarySearchById(List<Type> types, Type type)
    {
        var low = 0;
        var high = types.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var midId = types[middle].Id;
            if (midId == type.Id)
            {
                return middle;
            }
            if (midId < type.Id)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return ~low;
    }

    private static bool ContainsType(ReadOnlySpan<Type> types, Type type)
    {
        var low = 0;
        var high = types.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var midId = types[middle].Id;
            if (midId == type.Id)
            {
                return true;
            }
            if (midId < type.Id)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return false;
    }

    private static bool InsertType(List<Type> types, Type type)
    {
        var index = BinarySearchById(types, type);
        if (index < 0)
        {
            types.Insert(~index, type);
            return true;
        }
        return false;
    }

    private TypeFlags AddTypeToUnion(List<Type> typeSet, TypeFlags includes, Type type)
    {
        var flags = Types.Flags(type);
        // We ignore 'never' types in unions
        if ((flags & TypeFlags.Never) == 0)
        {
            includes |= flags & TypeFlags.IncludesMask;
            if ((flags & TypeFlags.Instantiable) != 0)
            {
                includes |= TypeFlags.IncludesInstantiable;
            }
            if ((flags & TypeFlags.Intersection) != 0 && (Types.ObjectFlagsOf(type) & ObjectFlags.IsConstrainedTypeVariable) != 0)
            {
                includes |= TypeFlags.IncludesConstrainedTypeVariable;
            }
            if (type == wildcardType)
            {
                includes |= TypeFlags.IncludesWildcard;
            }
            if (IsErrorType(type))
            {
                includes |= TypeFlags.IncludesError;
            }
            if (!strictNullChecks && (flags & TypeFlags.Nullable) != 0)
            {
                if ((Types.ObjectFlagsOf(type) & ObjectFlags.ContainsWideningType) == 0)
                {
                    includes |= TypeFlags.IncludesNonWideningType;
                }
            }
            else
            {
                var len = typeSet.Count;
                var index = len > 0 && type.Id > typeSet[len - 1].Id ? ~len : BinarySearchById(typeSet, type);
                if (index < 0)
                {
                    typeSet.Insert(~index, type);
                }
            }
        }
        return includes;
    }

    // Add the given types to the given type set. Order is preserved, duplicates are removed,
    // and nested types of the given kind are flattened into the set.
    private TypeFlags AddTypesToUnion(List<Type> typeSet, TypeFlags includes, ReadOnlySpan<Type> types)
    {
        var lastType = Type.None;
        foreach (var type in types)
        {
            // We skip the type if it is the same as the last type we processed.
            if (type != lastType)
            {
                includes = (Types.Flags(type) & TypeFlags.Union) != 0
                    ? AddTypesToUnion(typeSet, includes | (IsNamedUnionType(type) ? TypeFlags.Union : 0), Types.Elements(Types.Union(type).Types).ToArray())
                    : AddTypeToUnion(typeSet, includes, type);
                lastType = type;
            }
        }
        return includes;
    }

    private void RemoveRedundantLiteralTypes(List<Type> types, TypeFlags includes, bool reduceVoidUndefined)
    {
        var i = types.Count;
        while (i > 0)
        {
            i--;
            var t = types[i];
            var flags = Types.Flags(t);
            var remove = ((flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 && (includes & TypeFlags.String) != 0) ||
                ((flags & TypeFlags.NumberLiteral) != 0 && (includes & TypeFlags.Number) != 0) ||
                ((flags & TypeFlags.BigIntLiteral) != 0 && (includes & TypeFlags.BigInt) != 0) ||
                ((flags & TypeFlags.UniqueESSymbol) != 0 && (includes & TypeFlags.ESSymbol) != 0) ||
                (reduceVoidUndefined && (flags & TypeFlags.Undefined) != 0 && (includes & TypeFlags.Void) != 0) ||
                (IsFreshLiteralType(t) && ContainsType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(types), GetRegularTypeOfLiteralType(t)));
            if (remove)
            {
                types.RemoveAt(i);
            }
        }
    }

    private bool IsNamedUnionType(Type type) =>
        (Types.Flags(type) & TypeFlags.Union) != 0 && (!Types.Record(type).AliasSymbol.IsNone || !Types.Union(type).Origin.IsNone);

    private void AddNamedUnions(List<Type> namedUnions, ReadOnlySpan<Type> types)
    {
        foreach (var t in types)
        {
            if ((Types.Flags(t) & TypeFlags.Union) != 0)
            {
                var origin = Types.Union(t).Origin;
                if (!Types.Record(t).AliasSymbol.IsNone || (!origin.IsNone && (Types.Flags(origin) & TypeFlags.Union) == 0))
                {
                    if (!namedUnions.Contains(t))
                    {
                        namedUnions.Add(t);
                    }
                }
                else if (!origin.IsNone && (Types.Flags(origin) & TypeFlags.Union) != 0)
                {
                    AddNamedUnions(namedUnions, Types.Elements(Types.Union(origin).Types).ToArray());
                }
            }
        }
    }

    private Type CreateOriginUnionOrIntersectionType(TypeFlags flags, List<Type> types) =>
        Types.CreateUnionOrIntersection(flags, ObjectFlags.None, Types.CreateList(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(types)), Type.None);

    /// <summary>checker.ts getUnionType.</summary>
    internal Type GetUnionType(ReadOnlySpan<Type> types, UnionReduction unionReduction = UnionReduction.Literal, SymbolRef aliasSymbol = default, TypeList aliasTypeArguments = default, Type origin = default)
    {
        if (types.Length == 0)
        {
            return neverType;
        }
        if (types.Length == 1)
        {
            return types[0];
        }
        // We optimize for the common case of unioning a union type with some other type (such as `undefined`).
        if (types.Length == 2 && origin.IsNone && ((Types.Flags(types[0]) & TypeFlags.Union) != 0 || (Types.Flags(types[1]) & TypeFlags.Union) != 0))
        {
            // (the key `${id}${infix}${id}${getAliasId(...)}` as ints: the ids, the reduction, the alias)
            var index = types[0].Id < types[1].Id ? 0 : 1;
            var mark = keyScratch.Mark();
            keyScratch.Add(types[index].Id);
            keyScratch.Add((int)unionReduction);
            keyScratch.Add(types[1 - index].Id);
            AddAliasKey(aliasSymbol, aliasTypeArguments);
            var id = keyScratch.Since(mark);
            if (!unionOfUnionTypes.TryGetValue(id, out var type))
            {
                type = GetUnionTypeWorker(types, unionReduction, aliasSymbol, aliasTypeArguments, Type.None);
                unionOfUnionTypes.Set(id, type);
            }
            keyScratch.Release(mark);
            return type;
        }
        return GetUnionTypeWorker(types, unionReduction, aliasSymbol, aliasTypeArguments, origin);
    }

    private Type GetUnionTypeWorker(ReadOnlySpan<Type> types, UnionReduction unionReduction, SymbolRef aliasSymbol, TypeList aliasTypeArguments, Type origin)
    {
        var typeSet = new List<Type>(types.Length);
        var includes = AddTypesToUnion(typeSet, 0, types);
        if (unionReduction != UnionReduction.None)
        {
            if ((includes & TypeFlags.AnyOrUnknown) != 0)
            {
                return (includes & TypeFlags.Any) != 0
                    ? (includes & TypeFlags.IncludesWildcard) != 0 ? wildcardType : (includes & TypeFlags.IncludesError) != 0 ? errorType : anyType
                    : unknownType;
            }
            if ((includes & TypeFlags.Undefined) != 0)
            {
                // If type set contains both undefinedType and missingType, remove missingType
                if (typeSet.Count >= 2 && typeSet[0] == undefinedType && typeSet[1] == missingType)
                {
                    typeSet.RemoveAt(1);
                }
            }
            if ((includes & (TypeFlags.Enum | TypeFlags.Literal | TypeFlags.UniqueESSymbol | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 ||
                ((includes & TypeFlags.Void) != 0 && (includes & TypeFlags.Undefined) != 0))
            {
                RemoveRedundantLiteralTypes(typeSet, includes, (unionReduction & UnionReduction.Subtype) != 0);
            }
            if ((includes & TypeFlags.StringLiteral) != 0 && (includes & (TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0)
            {
                RemoveStringLiteralsMatchedByTemplateLiterals(typeSet);
            }
            if ((includes & TypeFlags.IncludesConstrainedTypeVariable) != 0)
            {
                RemoveConstrainedTypeVariables(typeSet);
            }
            if (unionReduction == UnionReduction.Subtype)
            {
                // removeSubtypes (module C4)
                var reducedSet = RemoveSubtypes(typeSet, (includes & TypeFlags.Object) != 0);
                if (reducedSet is null)
                {
                    return errorType;
                }
                typeSet = reducedSet;
            }
            if (typeSet.Count == 0)
            {
                return (includes & TypeFlags.Null) != 0 ? (includes & TypeFlags.IncludesNonWideningType) != 0 ? nullType : nullWideningType
                    : (includes & TypeFlags.Undefined) != 0 ? (includes & TypeFlags.IncludesNonWideningType) != 0 ? undefinedType : undefinedWideningType
                    : neverType;
            }
        }
        if (origin.IsNone && (includes & TypeFlags.Union) != 0)
        {
            var namedUnions = new List<Type>();
            AddNamedUnions(namedUnions, types);
            var reducedTypes = new List<Type>();
            foreach (var t in typeSet)
            {
                if (!namedUnions.Exists(union => ContainsType(Types.Elements(Types.Union(union).Types), t)))
                {
                    reducedTypes.Add(t);
                }
            }
            if (aliasSymbol.IsNone && namedUnions.Count == 1 && reducedTypes.Count == 0)
            {
                return namedUnions[0];
            }
            // We create a denormalized origin type only when the union was created from one or more named unions
            // (unions with alias symbols or origins) and when there is no overlap between those named unions.
            var namedTypesCount = 0;
            foreach (var union in namedUnions)
            {
                namedTypesCount += Types.Union(union).Types.Count;
            }
            if (namedTypesCount + reducedTypes.Count == typeSet.Count)
            {
                foreach (var t in namedUnions)
                {
                    InsertType(reducedTypes, t);
                }
                origin = CreateOriginUnionOrIntersectionType(TypeFlags.Union, reducedTypes);
            }
        }
        var objectFlags = ((includes & TypeFlags.NotPrimitiveUnion) != 0 ? 0 : ObjectFlags.PrimitiveUnion) |
            ((includes & TypeFlags.Intersection) != 0 ? ObjectFlags.ContainsIntersections : 0);
        return GetUnionTypeFromSortedList(typeSet, objectFlags, aliasSymbol, aliasTypeArguments, origin);
    }

    // We sort and deduplicate the constituent types based on object identity.
    private Type GetUnionTypeFromSortedList(List<Type> types, ObjectFlags precomputedObjectFlags, SymbolRef aliasSymbol = default, TypeList aliasTypeArguments = default, Type origin = default)
    {
        if (types.Count == 0)
        {
            return neverType;
        }
        if (types.Count == 1)
        {
            return types[0];
        }
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(types);
        // (the key: typeKey + getAliasId(aliasSymbol, aliasTypeArguments), as ints: a tag for the origin kind)
        var mark = keyScratch.Mark();
        if (origin.IsNone)
        {
            keyScratch.Add(0);
            keyScratch.AddTypes(span);
        }
        else if ((Types.Flags(origin) & TypeFlags.Union) != 0)
        {
            keyScratch.Add(1);
            keyScratch.AddTypes(Types.Elements(Types.Union(origin).Types));
        }
        else if ((Types.Flags(origin) & TypeFlags.Intersection) != 0)
        {
            keyScratch.Add(2);
            keyScratch.AddTypes(Types.Elements(Types.Union(origin).Types));
        }
        else
        {
            // origin type id alone is insufficient, as `keyof x` may resolve to multiple WIP values while `x` is still resolving
            keyScratch.Add(3);
            keyScratch.Add(Types.Data<IndexData>(origin).Type.Id);
            keyScratch.AddTypes(span);
        }
        AddAliasKey(aliasSymbol, aliasTypeArguments);
        var id = keyScratch.Since(mark);
        if (!unionTypes.TryGetValue(id, out var type))
        {
            var flags = TypeFlags.Union;
            if (types.Count == 2 && (Types.Flags(types[0]) & TypeFlags.BooleanLiteral) != 0 && (Types.Flags(types[1]) & TypeFlags.BooleanLiteral) != 0)
            {
                flags |= TypeFlags.Boolean;
            }
            type = Types.CreateUnionOrIntersection(flags, precomputedObjectFlags | GetPropagatingFlagsOfTypes(span, TypeFlags.Nullable), Types.CreateList(span), origin);
            ref var record = ref Types.Record(type);
            record.AliasSymbol = aliasSymbol;
            record.AliasTypeArguments = aliasTypeArguments;
            unionTypes.Set(id, type);
        }
        keyScratch.Release(mark);
        return type;
    }

    /// <summary>checker.ts mapType for unions (and a single type).</summary>
    internal Type MapType(Type type, Func<Type, Type> mapper, bool noReductions = false)
    {
        if ((Types.Flags(type) & TypeFlags.Never) != 0)
        {
            return type;
        }
        if ((Types.Flags(type) & TypeFlags.Union) == 0)
        {
            return mapper(type);
        }
        var origin = Types.Union(type).Origin;
        var types = !origin.IsNone && (Types.Flags(origin) & TypeFlags.Union) != 0 ? Types.Union(origin).Types : Types.Union(type).Types;
        List<Type>? mappedTypes = null;
        var changed = false;
        // (the stored members in place: the list arena is append-only; tsrs returns the stored slice; Track O)
        var elements = Types.Elements(types);
        foreach (var t in elements)
        {
            var mapped = (Types.Flags(t) & TypeFlags.Union) != 0 ? MapType(t, mapper, noReductions) : mapper(t);
            changed |= t != mapped;
            if (!mapped.IsNone)
            {
                (mappedTypes ??= []).Add(mapped);
            }
        }
        return changed ? mappedTypes is null ? Type.None : GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(mappedTypes), noReductions ? UnionReduction.None : UnionReduction.Literal) : type;
    }

    /// <summary>
    /// A mapper over types as a struct (module AR2, Track O; tsrs checker_12.rs map_type / map_type_ex take an
    /// <c>impl FnMut</c>, monomorphized per caller: notes/monomorphization-audit.md): <see cref="MapType{TMapper}"/> over it makes no delegate and no closure.
    /// </summary>
    internal interface ITypeMapFn
    {
        Type Invoke(Type t);
    }

    /// <summary>checker.ts mapType with a struct mapper: the same steps as <see cref="MapType(Type, Func{Type, Type}, bool)"/>.</summary>
    internal Type MapType<TMapper>(Type type, TMapper mapper, bool noReductions = false)
        where TMapper : struct, ITypeMapFn
    {
        if ((Types.Flags(type) & TypeFlags.Never) != 0)
        {
            return type;
        }
        if ((Types.Flags(type) & TypeFlags.Union) == 0)
        {
            return mapper.Invoke(type);
        }
        var origin = Types.Union(type).Origin;
        var types = !origin.IsNone && (Types.Flags(origin) & TypeFlags.Union) != 0 ? Types.Union(origin).Types : Types.Union(type).Types;
        List<Type>? mappedTypes = null;
        var changed = false;
        var elements = Types.Elements(types);
        foreach (var t in elements)
        {
            var mapped = (Types.Flags(t) & TypeFlags.Union) != 0 ? MapType(t, mapper, noReductions) : mapper.Invoke(t);
            changed |= t != mapped;
            if (!mapped.IsNone)
            {
                (mappedTypes ??= []).Add(mapped);
            }
        }
        return changed ? mappedTypes is null ? Type.None : GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(mappedTypes), noReductions ? UnionReduction.None : UnionReduction.Literal) : type;
    }

    // ---- intersection types ----

    private bool IsEmptyAnonymousObjectType(Type type)
    {
        if ((Types.Flags(type) & TypeFlags.Object) == 0 || (Types.ObjectFlagsOf(type) & ObjectFlags.Anonymous) == 0)
        {
            return false;
        }
        var data = Types.Object(type);
        if (data.MembersResolved && IsEmptyResolvedType(data))
        {
            return true;
        }
        var symbol = Types.Symbol(type);
        return !symbol.IsNone && (SymbolFlagsOf(symbol) & SymbolFlags.TypeLiteral) != 0 && GetMembersOfSymbol(symbol).Count == 0;
    }

    private static bool IsEmptyResolvedType(ObjectTypeData data) =>
        data.Properties is not { Count: > 0 } && data.CallSignatures is not { Count: > 0 } && data.ConstructSignatures is not { Count: > 0 } && data.IndexInfos is not { Count: > 0 };

    private TypeFlags AddTypeToIntersection(List<Type> typeSet, TypeFlags includes, Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Intersection) != 0)
        {
            return AddTypesToIntersection(typeSet, includes, Types.Elements(Types.Union(type).Types).ToArray());
        }
        if (IsEmptyAnonymousObjectType(type))
        {
            if ((includes & TypeFlags.IncludesEmptyObject) == 0)
            {
                includes |= TypeFlags.IncludesEmptyObject;
                typeSet.Add(type);
            }
        }
        else
        {
            if ((flags & TypeFlags.AnyOrUnknown) != 0)
            {
                if (type == wildcardType)
                {
                    includes |= TypeFlags.IncludesWildcard;
                }
                if (IsErrorType(type))
                {
                    includes |= TypeFlags.IncludesError;
                }
            }
            else if (strictNullChecks || (flags & TypeFlags.Nullable) == 0)
            {
                if (type == missingType)
                {
                    includes |= TypeFlags.IncludesMissingType;
                    type = undefinedType;
                }
                if (!typeSet.Contains(type))
                {
                    if ((Types.Flags(type) & TypeFlags.Unit) != 0 && (includes & TypeFlags.Unit) != 0)
                    {
                        // We have seen two distinct unit types which means we should reduce to an
                        // empty intersection. Adding TypeFlags.NonPrimitive causes that to happen.
                        includes |= TypeFlags.NonPrimitive;
                    }
                    typeSet.Add(type);
                }
            }
            includes |= flags & TypeFlags.IncludesMask;
        }
        return includes;
    }

    private TypeFlags AddTypesToIntersection(List<Type> typeSet, TypeFlags includes, ReadOnlySpan<Type> types)
    {
        foreach (var type in types)
        {
            includes = AddTypeToIntersection(typeSet, includes, GetRegularTypeOfLiteralType(type));
        }
        return includes;
    }

    private void RemoveRedundantSupertypes(List<Type> types, TypeFlags includes)
    {
        var i = types.Count;
        while (i > 0)
        {
            i--;
            var t = types[i];
            var flags = Types.Flags(t);
            var remove = ((flags & TypeFlags.String) != 0 && (includes & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0) ||
                ((flags & TypeFlags.Number) != 0 && (includes & TypeFlags.NumberLiteral) != 0) ||
                ((flags & TypeFlags.BigInt) != 0 && (includes & TypeFlags.BigIntLiteral) != 0) ||
                ((flags & TypeFlags.ESSymbol) != 0 && (includes & TypeFlags.UniqueESSymbol) != 0) ||
                ((flags & TypeFlags.Void) != 0 && (includes & TypeFlags.Undefined) != 0) ||
                (IsEmptyAnonymousObjectType(t) && (includes & TypeFlags.DefinitelyNonNullable) != 0);
            if (remove)
            {
                types.RemoveAt(i);
            }
        }
    }

    /// <summary>checker.ts getIntersectionType (the flags parameter last, IntersectionFlags).</summary>
    // getIntersectionType's callbacks as structs (no closure of primitiveType at every call; Track O)
    private readonly struct PrimitiveOrEmptyObjectPredicate(Checker c) : ITypePredicate
    {
        public bool Invoke(Type t) => (c.Types.Flags(t) & (TypeFlags.Primitive | TypeFlags.NonPrimitive)) != 0 || c.IsEmptyAnonymousObjectType(t);
    }

    private readonly struct StrictSubtypeOfPredicate(Checker c, Type target) : ITypePredicate
    {
        public bool Invoke(Type t) => c.IsTypeStrictSubtypeOf(t, target);
    }

    internal Type GetIntersectionType(ReadOnlySpan<Type> types, SymbolRef aliasSymbol = default, TypeList aliasTypeArguments = default, IntersectionFlags flags = IntersectionFlags.None)
    {
        var typeSet = new List<Type>();
        var includes = AddTypesToIntersection(typeSet, 0, types);
        var objectFlags = ObjectFlags.None;
        if ((includes & TypeFlags.Never) != 0)
        {
            return typeSet.Contains(silentNeverType) ? silentNeverType : neverType;
        }
        if ((strictNullChecks && (includes & TypeFlags.Nullable) != 0 && (includes & (TypeFlags.Object | TypeFlags.NonPrimitive | TypeFlags.IncludesEmptyObject)) != 0) ||
            ((includes & TypeFlags.NonPrimitive) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.NonPrimitive)) != 0) ||
            ((includes & TypeFlags.StringLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.StringLike)) != 0) ||
            ((includes & TypeFlags.NumberLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.NumberLike)) != 0) ||
            ((includes & TypeFlags.BigIntLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.BigIntLike)) != 0) ||
            ((includes & TypeFlags.ESSymbolLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.ESSymbolLike)) != 0) ||
            ((includes & TypeFlags.VoidLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.VoidLike)) != 0))
        {
            return neverType;
        }
        if ((includes & (TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 && (includes & TypeFlags.StringLiteral) != 0 && ExtractRedundantTemplateLiterals(typeSet))
        {
            return neverType;
        }
        if ((includes & TypeFlags.Any) != 0)
        {
            return (includes & TypeFlags.IncludesWildcard) != 0 ? wildcardType : (includes & TypeFlags.IncludesError) != 0 ? errorType : anyType;
        }
        if (!strictNullChecks && (includes & TypeFlags.Nullable) != 0)
        {
            return (includes & TypeFlags.IncludesEmptyObject) != 0 ? neverType : (includes & TypeFlags.Undefined) != 0 ? undefinedType : nullType;
        }
        if (((includes & TypeFlags.String) != 0 && (includes & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0) ||
            ((includes & TypeFlags.Number) != 0 && (includes & TypeFlags.NumberLiteral) != 0) ||
            ((includes & TypeFlags.BigInt) != 0 && (includes & TypeFlags.BigIntLiteral) != 0) ||
            ((includes & TypeFlags.ESSymbol) != 0 && (includes & TypeFlags.UniqueESSymbol) != 0) ||
            ((includes & TypeFlags.Void) != 0 && (includes & TypeFlags.Undefined) != 0) ||
            ((includes & TypeFlags.IncludesEmptyObject) != 0 && (includes & TypeFlags.DefinitelyNonNullable) != 0))
        {
            if ((flags & IntersectionFlags.NoSupertypeReduction) == 0)
            {
                RemoveRedundantSupertypes(typeSet, includes);
            }
        }
        if ((includes & TypeFlags.IncludesMissingType) != 0)
        {
            typeSet[typeSet.IndexOf(undefinedType)] = missingType;
        }
        if (typeSet.Count == 0)
        {
            return unknownType;
        }
        if (typeSet.Count == 1)
        {
            return typeSet[0];
        }
        if (typeSet.Count == 2 && (flags & IntersectionFlags.NoConstraintReduction) == 0)
        {
            var typeVarIndex = (Types.Flags(typeSet[0]) & TypeFlags.TypeVariable) != 0 ? 0 : 1;
            var typeVariable = typeSet[typeVarIndex];
            var primitiveType = typeSet[1 - typeVarIndex];
            if ((Types.Flags(typeVariable) & TypeFlags.TypeVariable) != 0 &&
                (((Types.Flags(primitiveType) & (TypeFlags.Primitive | TypeFlags.NonPrimitive)) != 0 && !IsGenericStringLikeType(primitiveType)) || (includes & TypeFlags.IncludesEmptyObject) != 0))
            {
                // We have an intersection T & P or P & T, where T is a type variable and P is a primitive type, the object type, or {}.
                var constraint = GetBaseConstraintOfType(typeVariable);
                // Check that T's constraint is similarly composed of primitive types, the object type, or {}.
                if (!constraint.IsNone && EveryType(constraint, new PrimitiveOrEmptyObjectPredicate(this)))
                {
                    // If T's constraint is a subtype of P, simply return T. For example, given `T extends "a" | "b"`,
                    // the intersection `T & string` reduces to just T.
                    if (IsTypeStrictSubtypeOf(constraint, primitiveType))
                    {
                        return typeVariable;
                    }
                    if (!((Types.Flags(constraint) & TypeFlags.Union) != 0 && SomeType(constraint, new StrictSubtypeOfPredicate(this, primitiveType))))
                    {
                        // No constituent of T's constraint is a subtype of P. If P is also not a subtype of T's constraint,
                        // then the constraint and P are unrelated, and the intersection reduces to never. For example, given
                        // `T extends "a" | "b"`, the intersection `T & number` reduces to never.
                        if (!IsTypeStrictSubtypeOf(primitiveType, constraint))
                        {
                            return neverType;
                        }
                    }
                    // Some constituent of T's constraint is a subtype of P, or P is a subtype of T's constraint. Thus,
                    // the intersection further constrains the type variable. For example, given `T extends string | number`,
                    // the intersection `T & "a"` is marked as a constrained type variable. Likewise, given `T extends "a" | 1`,
                    // the intersection `T & number` is marked as a constrained type variable.
                    objectFlags = ObjectFlags.IsConstrainedTypeVariable;
                }
            }
        }
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(typeSet);
        // (the key getTypeListId(typeSet) + ("*" or getAliasId(...)) as ints: -3 for "*", else the alias key)
        var mark = keyScratch.Mark();
        keyScratch.AddTypes(span);
        if ((flags & IntersectionFlags.NoConstraintReduction) != 0)
        {
            keyScratch.Add(-3);
        }
        else
        {
            AddAliasKey(aliasSymbol, aliasTypeArguments);
        }
        var id = keyScratch.Since(mark);
        if (!intersectionTypes.TryGetValue(id, out var result))
        {
            if ((includes & TypeFlags.Union) != 0)
            {
                // module C3: the normalization of intersections containing unions
                result = GetIntersectionOfUnions(typeSet, types.Length, aliasSymbol, aliasTypeArguments, flags);
            }
            else
            {
                result = Types.CreateUnionOrIntersection(TypeFlags.Intersection, objectFlags | GetPropagatingFlagsOfTypes(span, TypeFlags.Nullable), Types.CreateList(span), Type.None);
                ref var record = ref Types.Record(result);
                record.AliasSymbol = aliasSymbol;
                record.AliasTypeArguments = aliasTypeArguments;
            }
            intersectionTypes.Set(id, result);
        }
        keyScratch.Release(mark);
        return result;
    }

    // getIntersectionType's union cases: we normalize combinations of intersection and union types based on the
    // distributive property of the '&' operator (X & (A | B) is X & A | X & B), so union types are always at the top level.
    private Type GetIntersectionOfUnions(List<Type> typeSet, int inputCount, SymbolRef aliasSymbol, TypeList aliasTypeArguments, IntersectionFlags flags)
    {
        if (IntersectUnionsOfPrimitiveTypes(typeSet))
        {
            // When the intersection creates a reduced set (which might mean that *all* union types have
            // disappeared), we restart the operation to get a new set of combined flags. Once we have
            // reduced we'll never reduce again, so this occurs at most once.
            return GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(typeSet), aliasSymbol, aliasTypeArguments, flags);
        }
        if (typeSet.TrueForAll(t => (Types.Flags(t) & TypeFlags.Union) != 0 && (Types.Flags(Types.Elements(Types.Union(t).Types)[0]) & TypeFlags.Undefined) != 0))
        {
            var containedUndefinedType = typeSet.Exists(ContainsMissingType) ? missingType : undefinedType;
            RemoveFromEach(typeSet, TypeFlags.Undefined);
            return GetUnionType([GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(typeSet), flags: flags), containedUndefinedType], UnionReduction.Literal, aliasSymbol, aliasTypeArguments);
        }
        if (typeSet.TrueForAll(t => (Types.Flags(t) & TypeFlags.Union) != 0 &&
            ((Types.Flags(Types.Elements(Types.Union(t).Types)[0]) & TypeFlags.Null) != 0 || (Types.Flags(Types.Elements(Types.Union(t).Types)[1]) & TypeFlags.Null) != 0)))
        {
            RemoveFromEach(typeSet, TypeFlags.Null);
            return GetUnionType([GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(typeSet), flags: flags), nullType], UnionReduction.Literal, aliasSymbol, aliasTypeArguments);
        }
        if (typeSet.Count >= 3 && inputCount > 2)
        {
            // When we have three or more constituents, more than two inputs (to head off infinite reexpansion), some of which are unions, we employ a "divide and conquer" strategy
            // where A & B & C & D is processed as (A & B) & (C & D).
            var middle = typeSet.Count / 2;
            var left = GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(typeSet)[..middle], flags: flags);
            var right = GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(typeSet)[middle..], flags: flags);
            return GetIntersectionType([left, right], aliasSymbol, aliasTypeArguments, flags);
        }
        // We are attempting to construct a type of the form X & (A | B) & (C | D). Transform this into a type of
        // the form X & A & C | X & A & D | X & B & C | X & B & D. If the estimated size of the resulting union type
        // exceeds 100000 constituents, report an error.
        var array = typeSet.ToArray();
        if (!CheckCrossProductUnion(array))
        {
            return errorType;
        }
        var constituents = GetCrossProductIntersections(array, flags);
        // We attach a denormalized origin type when at least one constituent of the cross-product union is an
        // intersection (i.e. when the intersection didn't just reduce one or more unions to smaller unions) and
        // the denormalized origin has fewer constituents than the union itself.
        var origin = constituents.Exists(t => (Types.Flags(t) & TypeFlags.Intersection) != 0) && GetConstituentCountOfTypes(constituents) > GetConstituentCountOfTypes(typeSet)
            ? Types.CreateUnionOrIntersection(TypeFlags.Intersection, ObjectFlags.None, Types.CreateList(array), Type.None)
            : Type.None;
        return GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(constituents), UnionReduction.Literal, aliasSymbol, aliasTypeArguments, origin);
    }

    // checker.ts getCrossProductIntersections
    private List<Type> GetCrossProductIntersections(Type[] types, IntersectionFlags flags)
    {
        var count = 1;
        foreach (var t in types)
        {
            count *= (Types.Flags(t) & TypeFlags.Union) != 0 ? Types.Union(t).Types.Count : (Types.Flags(t) & TypeFlags.Never) != 0 ? 0 : 1;
        }
        var intersections = new List<Type>();
        // (types.slice() per combination: one buffer, refilled; getIntersectionType reads it as a span)
        var constituents = new Type[types.Length];
        for (var i = 0; i < count; i++)
        {
            types.CopyTo(constituents, 0);
            var n = i;
            for (var j = types.Length - 1; j >= 0; j--)
            {
                if ((Types.Flags(types[j]) & TypeFlags.Union) != 0)
                {
                    var sourceTypes = Types.Elements(Types.Union(types[j]).Types);
                    var length = sourceTypes.Length;
                    constituents[j] = sourceTypes[n % length];
                    n /= length;
                }
            }
            var t = GetIntersectionType(constituents, flags: flags);
            if ((Types.Flags(t) & TypeFlags.Never) == 0)
            {
                intersections.Add(t);
            }
        }
        return intersections;
    }

    // ---- reductions that need the relations (module C4) ----

    /// <summary>types.ts IntersectionFlags.</summary>
    [Flags]
    internal enum IntersectionFlags
    {
        None = 0,
        NoSupertypeReduction = 1 << 0,
        NoConstraintReduction = 1 << 1,
    }

    // checker.ts extractRedundantTemplateLiterals
    private bool ExtractRedundantTemplateLiterals(List<Type> types)
    {
        var i = types.Count;
        var literals = types.FindAll(t => (Types.Flags(t) & TypeFlags.StringLiteral) != 0);
        while (i > 0)
        {
            i--;
            var t = types[i];
            if ((Types.Flags(t) & (TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) == 0)
            {
                continue;
            }
            foreach (var t2 in literals)
            {
                if (IsTypeSubtypeOf(t2, t))
                {
                    // For example, `get${T}` & "getX" is just "getX", and Lowercase<string> & "foo" is just "foo"
                    types.RemoveAt(i);
                    break;
                }
                if (IsPatternLiteralType(t))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // checker.ts removeStringLiteralsMatchedByTemplateLiterals
    private void RemoveStringLiteralsMatchedByTemplateLiterals(List<Type> types)
    {
        List<Type>? templates = null;
        foreach (var type in types)
        {
            if (IsPatternLiteralType(type))
            {
                (templates ??= []).Add(type);
            }
        }
        if (templates is not null)
        {
            var i = types.Count;
            while (i > 0)
            {
                i--;
                var t = types[i];
                if ((Types.Flags(t) & TypeFlags.StringLiteral) != 0 && IsMatchedBySomeTemplate(t, templates))
                {
                    types.RemoveAt(i);
                }
            }
        }
    }

    // some(templates, template => isTypeMatchedByTemplateLiteralOrStringMapping(t, template))
    private bool IsMatchedBySomeTemplate(Type t, List<Type> templates)
    {
        foreach (var template in templates)
        {
            if (IsTypeMatchedByTemplateLiteralOrStringMapping(t, template))
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts isTypeMatchedByTemplateLiteralOrStringMapping
    private bool IsTypeMatchedByTemplateLiteralOrStringMapping(Type type, Type template) =>
        (Types.Flags(template) & TypeFlags.TemplateLiteral) != 0 ? IsTypeMatchedByTemplateLiteralType(type, template) : IsMemberOfStringMapping(type, template);

    // checker.ts removeConstrainedTypeVariables
    private void RemoveConstrainedTypeVariables(List<Type> types)
    {
        var typeVariables = new List<Type>();
        // First collect a list of the type variables occurring in constraining intersections.
        foreach (var type in types)
        {
            if ((Types.Flags(type) & TypeFlags.Intersection) != 0 && (Types.ObjectFlagsOf(type) & ObjectFlags.IsConstrainedTypeVariable) != 0)
            {
                var members = Types.Elements(Types.Union(type).Types);
                var index = (Types.Flags(members[0]) & TypeFlags.TypeVariable) != 0 ? 0 : 1;
                if (!typeVariables.Contains(members[index]))
                {
                    typeVariables.Add(members[index]);
                }
            }
        }
        // For each type variable, check if the constraining intersections for that type variable fully
        // cover the constraint of the type variable; if so, remove the constraining intersections and
        // substitute the type variable.
        foreach (var typeVariable in typeVariables)
        {
            var primitives = new List<Type>();
            // First collect the primitive types from the constraining intersections.
            foreach (var type in types)
            {
                if ((Types.Flags(type) & TypeFlags.Intersection) != 0 && (Types.ObjectFlagsOf(type) & ObjectFlags.IsConstrainedTypeVariable) != 0)
                {
                    var members = Types.Elements(Types.Union(type).Types);
                    var index = (Types.Flags(members[0]) & TypeFlags.TypeVariable) != 0 ? 0 : 1;
                    if (members[index] == typeVariable)
                    {
                        InsertType(primitives, members[1 - index]);
                    }
                }
            }
            // If every constituent in the type variable's constraint is covered by an intersection of the type
            // variable and that constituent, remove those intersections and substitute the type variable.
            var constraint = GetBaseConstraintOfType(typeVariable);
            if (EveryType(constraint, t => ContainsType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(primitives), t)))
            {
                var i = types.Count;
                while (i > 0)
                {
                    i--;
                    var type = types[i];
                    if ((Types.Flags(type) & TypeFlags.Intersection) != 0 && (Types.ObjectFlagsOf(type) & ObjectFlags.IsConstrainedTypeVariable) != 0)
                    {
                        var members = Types.Elements(Types.Union(type).Types);
                        var index = (Types.Flags(members[0]) & TypeFlags.TypeVariable) != 0 ? 0 : 1;
                        if (members[index] == typeVariable && ContainsType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(primitives), members[1 - index]))
                        {
                            types.RemoveAt(i);
                        }
                    }
                }
                InsertType(types, typeVariable);
            }
        }
    }

    private readonly KeyMap<TypeList> subtypeReductionCache = new(); // (module CO2: the reduced lists in the type list arena)

    // checker.ts removeSubtypes (null: the union is too complex to represent)
    private List<Type>? RemoveSubtypes(List<Type> types, bool hasObjectTypes)
    {
        // [] and [T] immediately reduce to [] and [T] respectively
        if (types.Count < 2)
        {
            return types;
        }
        var mark = keyScratch.Mark();
        keyScratch.AddTypes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(types));
        var id = keyScratch.Since(mark);
        if (subtypeReductionCache.TryGetValue(id, out var match))
        {
            keyScratch.Release(mark);
            return [.. Types.Elements(match)];
        }
        // We assume that redundant primitive types have already been removed from the types array and that there
        // are no any and unknown types in the array. Thus, the only possible supertypes for primitive types are empty
        // object types, and if none of those are present we can exclude primitive types from the subtype check.
        var hasEmptyObject = hasObjectTypes && types.Exists(t => (Types.Flags(t) & TypeFlags.Object) != 0 && !IsGenericMappedType(t) && IsEmptyResolvedTypeOf(t, ResolveStructuredTypeMembers(t)));
        var len = types.Count;
        var i = len;
        var count = 0;
        while (i > 0)
        {
            i--;
            var source = types[i];
            if (hasEmptyObject || (Types.Flags(source) & TypeFlags.StructuredOrInstantiable) != 0)
            {
                // A type parameter with a union constraint may be a subtype of some union, but not a subtype of the
                // individual constituents of that union. For example, `T extends A | B` is a subtype of `A | B`, but not
                // a subtype of just `A` or just `B`. When we encounter such a type parameter, we therefore check if the
                // type parameter is a subtype of a union of all the other types.
                if ((Types.Flags(source) & TypeFlags.TypeParameter) != 0 && (Types.Flags(GetBaseConstraintOrType(source)) & TypeFlags.Union) != 0)
                {
                    if (IsTypeRelatedTo(source, GetUnionType(types.ConvertAll(t => t == source ? neverType : t).ToArray()), strictSubtypeRelation))
                    {
                        types.RemoveAt(i);
                    }
                    continue;
                }
                // Find the first property with a unit type, if any. When constituents have a property by the same name
                // but of a different unit type, we can quickly disqualify them from subtype checks. This helps subtype
                // reduction of large discriminated union types.
                var keyProperty = (Types.Flags(source) & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.InstantiableNonPrimitive)) != 0
                    ? GetPropertiesOfType(source).Find(p => IsUnitType(GetTypeOfSymbol(p)))
                    : SymbolRef.None;
                var keyPropertyType = keyProperty.IsNone ? Type.None : GetRegularTypeOfLiteralType(GetTypeOfSymbol(keyProperty));
                foreach (var target in types.ToArray())
                {
                    if (source != target)
                    {
                        if (count == 100000)
                        {
                            // After 100000 subtype checks we estimate the remaining amount of work by assuming the
                            // same ratio of checks per element. If the estimated number of remaining type checks is
                            // greater than 1M we deem the union type too complex to represent. This for example
                            // caps union types at 1000 unique object types.
                            var estimatedCount = (double)count / (len - i) * len;
                            if (estimatedCount > 1000000)
                            {
                                Error(currentNode, Diagnostics.Expression_produces_a_union_type_that_is_too_complex_to_represent);
                                keyScratch.Release(mark);
                                return null;
                            }
                        }
                        count++;
                        if (!keyProperty.IsNone && (Types.Flags(target) & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.InstantiableNonPrimitive)) != 0)
                        {
                            var t = GetTypeOfPropertyOfType(target, NameOf(keyProperty));
                            if (!t.IsNone && IsUnitType(t) && GetRegularTypeOfLiteralType(t) != keyPropertyType)
                            {
                                continue;
                            }
                        }
                        if (IsTypeRelatedTo(source, target, strictSubtypeRelation) &&
                            ((Types.ObjectFlagsOf(GetTargetType(source)) & ObjectFlags.Class) == 0 ||
                             (Types.ObjectFlagsOf(GetTargetType(target)) & ObjectFlags.Class) == 0 ||
                             IsTypeDerivedFrom(source, target)))
                        {
                            types.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
        }
        subtypeReductionCache.Set(id, Types.CreateList(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(types)));
        keyScratch.Release(mark);
        return types;
    }

    // checker.ts getConstituentCount
    private int GetConstituentCount(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.UnionOrIntersection) == 0 || !Types.Record(type).AliasSymbol.IsNone)
        {
            return 1;
        }
        if ((flags & TypeFlags.Union) != 0 && !Types.Union(type).Origin.IsNone)
        {
            return GetConstituentCount(Types.Union(type).Origin);
        }
        return GetConstituentCountOfTypes(Types.Elements(Types.Union(type).Types).ToArray());
    }

    private int GetConstituentCountOfTypes(IEnumerable<Type> types)
    {
        var n = 0;
        foreach (var t in types)
        {
            n += GetConstituentCount(t);
        }
        return n;
    }

    // checker.ts removeFromEach
    private void RemoveFromEach(List<Type> types, TypeFlags flag)
    {
        for (var i = 0; i < types.Count; i++)
        {
            types[i] = FilterType(types[i], t => (Types.Flags(t) & flag) == 0);
        }
    }

    // checker.ts eachUnionContains
    private bool EachUnionContains(List<Type> unionTypes, Type type)
    {
        foreach (var u in unionTypes)
        {
            var members = Types.Elements(Types.Union(u).Types);
            if (!ContainsType(members, type))
            {
                if (type == missingType)
                {
                    return ContainsType(members, undefinedType);
                }
                if (type == undefinedType)
                {
                    return ContainsType(members, missingType);
                }
                var flags = Types.Flags(type);
                var primitive = (flags & TypeFlags.StringLiteral) != 0 ? stringType
                    : (flags & (TypeFlags.Enum | TypeFlags.NumberLiteral)) != 0 ? numberType
                    : (flags & TypeFlags.BigIntLiteral) != 0 ? bigintType
                    : (flags & TypeFlags.UniqueESSymbol) != 0 ? esSymbolType
                    : Type.None;
                if (primitive.IsNone || !ContainsType(members, primitive))
                {
                    return false;
                }
            }
        }
        return true;
    }

    // checker.ts intersectUnionsOfPrimitiveTypes: if the given list of types contains more than one union of primitive
    // types, replace the first with a union containing an intersection of those primitive types, then remove the other
    // unions and return true. Otherwise, do nothing and return false.
    private bool IntersectUnionsOfPrimitiveTypes(List<Type> types)
    {
        List<Type>? unionTypes = null;
        var index = types.FindIndex(t => (Types.ObjectFlagsOf(t) & ObjectFlags.PrimitiveUnion) != 0);
        if (index < 0)
        {
            return false;
        }
        var i = index + 1;
        // Remove all but the first union of primitive types and collect them in the unionTypes array.
        while (i < types.Count)
        {
            var t = types[i];
            if ((Types.ObjectFlagsOf(t) & ObjectFlags.PrimitiveUnion) != 0)
            {
                (unionTypes ??= [types[index]]).Add(t);
                types.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }
        // Return false if there was only one union of primitive types
        if (unionTypes is null)
        {
            return false;
        }
        // We have more than one union of primitive types, now intersect them. For each
        // type in each union we check if the type is matched in every union and if so
        // we include it in the result.
        var checkedTypes = new List<Type>();
        var result = new List<Type>();
        foreach (var u in unionTypes)
        {
            foreach (var t in Types.Elements(Types.Union(u).Types).ToArray())
            {
                if (InsertType(checkedTypes, t))
                {
                    if (EachUnionContains(unionTypes, t))
                    {
                        // undefinedType/missingType should always come sorted first so we leverage that here
                        if (t == undefinedType && result.Count > 0 && result[0] == missingType)
                        {
                            continue;
                        }
                        if (t == missingType && result.Count > 0 && result[0] == undefinedType)
                        {
                            result[0] = missingType;
                            continue;
                        }
                        InsertType(result, t);
                    }
                }
            }
        }
        // Finally replace the first union with the result
        types[index] = GetUnionTypeFromSortedList(result, ObjectFlags.PrimitiveUnion);
        return true;
    }

    // ---- widening of literal types ----

    /// <summary>checker.ts getBaseTypeOfLiteralType.</summary>
    internal Type GetBaseTypeOfLiteralType(Type type)
    {
        var flags = Types.Flags(type);
        return (flags & TypeFlags.EnumLike) != 0 ? GetBaseTypeOfEnumLikeType(type)
            : (flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 ? stringType
            : (flags & TypeFlags.NumberLiteral) != 0 ? numberType
            : (flags & TypeFlags.BigIntLiteral) != 0 ? bigintType
            : (flags & TypeFlags.BooleanLiteral) != 0 ? booleanType
            : (flags & TypeFlags.Union) != 0 ? MapType(type, GetBaseTypeOfLiteralType)
            : type;
    }

    /// <summary>checker.ts getWidenedLiteralType.</summary>
    internal Type GetWidenedLiteralType(Type type)
    {
        var flags = Types.Flags(type);
        return (flags & TypeFlags.EnumLike) != 0 && IsFreshLiteralType(type) ? GetBaseTypeOfEnumLikeType(type)
            : (flags & TypeFlags.StringLiteral) != 0 && IsFreshLiteralType(type) ? stringType
            : (flags & TypeFlags.NumberLiteral) != 0 && IsFreshLiteralType(type) ? numberType
            : (flags & TypeFlags.BigIntLiteral) != 0 && IsFreshLiteralType(type) ? bigintType
            : (flags & TypeFlags.BooleanLiteral) != 0 && IsFreshLiteralType(type) ? booleanType
            : (flags & TypeFlags.Union) != 0 ? MapType(type, GetWidenedLiteralType)
            : type;
    }

    // checker.ts getBaseTypeOfEnumLikeType: the enum type of an enum literal (its parent enum's declared type).
    private Type GetBaseTypeOfEnumLikeType(Type type)
    {
        var symbol = Types.Symbol(type);
        if ((Types.Flags(type) & TypeFlags.EnumLike) != 0 && !symbol.IsNone && (SymbolFlagsOf(symbol) & SymbolFlags.EnumMember) != 0)
        {
            var parent = GetParentOfSymbol(symbol);
            return !parent.IsNone ? GetDeclaredTypeOfSymbol(parent) : type;
        }
        return type;
    }

    // ---- object types ----

    /// <summary>checker.ts createObjectType.</summary>
    internal Type CreateObjectType(ObjectFlags objectFlags, SymbolRef symbol) => Types.CreateObject(objectFlags, symbol);

    /// <summary>checker.ts createAnonymousType (setStructuredTypeMembers).</summary>
    internal Type CreateAnonymousType(SymbolRef symbol, SymTable members, Items<Signature> callSignatures = default, Items<Signature> constructSignatures = default, Items<IndexInfo> indexInfos = default)
    {
        var type = CreateObjectType(ObjectFlags.Anonymous, symbol);
        SetStructuredTypeMembers(type, members, callSignatures, constructSignatures, indexInfos);
        return type;
    }

    /// <summary>checker.ts setStructuredTypeMembers.</summary>
    private void SetStructuredTypeMembers(Type type, SymTable members, Items<Signature> callSignatures, Items<Signature> constructSignatures, Items<IndexInfo> indexInfos)
    {
        var data = Types.Object(type);
        data.Members = members;
        data.Properties = default;
        data.CallSignatures = Types.Store(callSignatures);
        data.ConstructSignatures = Types.Store(constructSignatures);
        data.IndexInfos = Types.Store(indexInfos);
        data.MembersResolved = true;
        // This can loop back to getPropertyOfType() which would crash if `callSignatures` & `constructSignatures` are not initialized.
        if (members != emptySymbols)
        {
            data.Properties = GetNamedMembers(members);
        }
    }

    /// <summary>checker.ts getNamedMembers: the members that are not internal names (`__call`, ...), as a stored list.</summary>
    private ItemRange<SymbolRef> GetNamedMembers(SymTable members)
    {
        var result = namedMembersScratch;
        var mark = result.Count;
        for (var i = 0; i < members.Count; i++)
        {
            var id = members.NameAt(i);
            var symbol = members.ValueAt(i);
            if (IsNamedMember(symbol, id))
            {
                result.Add(symbol);
            }
        }
        var stored = Types.Store(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(result)[mark..]);
        result.RemoveRange(mark, result.Count - mark);
        return stored;
    }

    // (a stack: IsNamedMember may resolve an alias, which may resolve members again above the outer call's elements)
    private readonly List<SymbolRef> namedMembersScratch = [];

    private bool IsNamedMember(SymbolRef member, string escapedName) =>
        !IsReservedMemberName(escapedName) && SymbolIsValue(member);

    // checker.ts isReservedMemberName: `__` followed by a character other than `_`, `@` or `#`.
    private static bool IsReservedMemberName(string name) =>
        name.Length >= 3 && name[0] == '_' && name[1] == '_' && name[2] != '_' && name[2] != '@' && name[2] != '#';

    /// <summary>checker.ts createTypeReference.</summary>
    internal Type CreateTypeReference(Type target, ReadOnlySpan<Type> typeArguments)
    {
        // (target.instantiations keyed by getTypeListId(typeArguments): the ints after target's id)
        Types.Object(target).HasInstantiations = true;
        var mark = keyScratch.Mark();
        keyScratch.Add(target.Id);
        keyScratch.AddTypes(typeArguments);
        var id = keyScratch.Since(mark);
        if (!typeInstantiations.TryGetValue(id, out var type))
        {
            type = CreateObjectType(ObjectFlags.Reference, Types.Symbol(target));
            typeInstantiations.Set(id, type);
            Types.Record(type).ObjectFlags |= typeArguments.Length > 0 ? GetPropagatingFlagsOfTypes(typeArguments) : 0;
            var data = Types.Object(type);
            data.Target = target;
            data.ResolvedTypeArguments = Types.CreateList(typeArguments);
            data.HasResolvedTypeArguments = true;
        }
        keyScratch.Release(mark);
        return type;
    }

    // types.ts ObjectType.instantiations of every object type of this checker (module CO2: one map, the owner type's id
    // first in each key; ObjectTypeData.HasInstantiations records that the owner's map exists)
    private readonly KeyMap<Type> typeInstantiations = new();

    // `type.instantiations = new Map([[getTypeListId(typeParameters), type]])` (generic and tuple target types)
    private void InitializeInstantiations(Type type, ReadOnlySpan<Type> typeParameters)
    {
        Types.Object(type).HasInstantiations = true;
        var mark = keyScratch.Mark();
        keyScratch.Add(type.Id);
        keyScratch.AddTypes(typeParameters);
        typeInstantiations.Set(keyScratch.Since(mark), type);
        keyScratch.Release(mark);
    }

    /// <summary>checker.ts createDeferredTypeReference.</summary>
    internal Type CreateDeferredTypeReference(Type target, NodeRef node, TypeMapper mapper, SymbolRef aliasSymbol = default, TypeList aliasTypeArguments = default)
    {
        if (aliasSymbol.IsNone)
        {
            aliasSymbol = GetAliasSymbolForTypeNode(node);
            var localAliasTypeArguments = GetTypeArgumentsForAliasSymbol(aliasSymbol);
            aliasTypeArguments = !mapper.IsNone ? InstantiateTypeList(localAliasTypeArguments, mapper) : localAliasTypeArguments;
        }
        var type = CreateObjectType(ObjectFlags.Reference, Types.Symbol(target));
        var data = Types.Object(type);
        data.Target = target;
        data.Node = node;
        data.Mapper = mapper;
        ref var record = ref Types.Record(type);
        record.AliasSymbol = aliasSymbol;
        record.AliasTypeArguments = aliasTypeArguments;
        return type;
    }

    /// <summary>checker.ts getTypeArguments (the arguments of a deferred reference are resolved on first use).</summary>
    internal ReadOnlySpan<Type> GetTypeArguments(Type type)
    {
        var data = Types.Object(type);
        if (!data.HasResolvedTypeArguments)
        {
            var targetData = Types.Object(data.Target);
            if (!PushTypeResolution(type, ResolvedTypeArgumentsProperty))
            {
                return ErrorTypeArguments(targetData);
            }
            var node = data.Node;
            var typeArguments = new List<Type>();
            if (!node.IsNone)
            {
                var store = StoreOf(node);
                switch (store.Kind(node.Node))
                {
                    case SyntaxKind.TypeReference:
                        typeArguments.AddRange(Types.Elements(targetData.OuterTypeParameters).ToArray());
                        typeArguments.AddRange(GetEffectiveTypeArguments(node, Types.Elements(targetData.LocalTypeParameters).ToArray()));
                        break;
                    case SyntaxKind.ArrayType:
                        typeArguments.Add(GetTypeFromTypeNode(At(node, store.ElementType(node.Node))));
                        break;
                    default:
                        foreach (var element in store.Nodes(store.Elements(node.Node)))
                        {
                            typeArguments.Add(GetTypeFromTypeNode(At(node, element)));
                        }
                        break;
                }
            }
            if (PopTypeResolution())
            {
                if (!data.HasResolvedTypeArguments)
                {
                    var resolved = typeArguments.ToArray();
                    if (!data.Mapper.IsNone)
                    {
                        resolved = InstantiateTypes(resolved, data.Mapper, out _);
                    }
                    data.ResolvedTypeArguments = Types.CreateList(resolved);
                    data.HasResolvedTypeArguments = true;
                }
            }
            else
            {
                if (!data.HasResolvedTypeArguments)
                {
                    ErrorTypeArguments(targetData);
                    data.ResolvedTypeArguments = errorTypeArgumentsScratch;
                    data.HasResolvedTypeArguments = true;
                }
                var targetSymbol = Types.Symbol(data.Target);
                Error(data.Node, !targetSymbol.IsNone ? Diagnostics.Type_arguments_for_0_circularly_reference_themselves : Diagnostics.Tuple_type_arguments_circularly_reference_themselves,
                    targetSymbol.IsNone ? null : SymbolToString(targetSymbol));
            }
        }
        return Types.Elements(data.ResolvedTypeArguments);
    }

    private TypeList errorTypeArgumentsScratch;

    private const int ResolvedTypeArgumentsProperty = 4;

    // concatenate(target.outerTypeParameters, target.localTypeParameters?.map(() => errorType))
    private ReadOnlySpan<Type> ErrorTypeArguments(ObjectTypeData targetData)
    {
        var list = new List<Type>(Types.Elements(targetData.OuterTypeParameters).ToArray());
        for (var i = 0; i < targetData.LocalTypeParameters.Count; i++)
        {
            list.Add(errorType);
        }
        errorTypeArgumentsScratch = Types.CreateList(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list));
        return Types.Elements(errorTypeArgumentsScratch);
    }

    /// <summary>checker.ts createTypeFromGenericGlobalType.</summary>
    internal Type CreateTypeFromGenericGlobalType(Type genericGlobalType, ReadOnlySpan<Type> typeArguments) =>
        genericGlobalType != emptyGenericType ? CreateTypeReference(genericGlobalType, typeArguments) : emptyObjectType;

    /// <summary>checker.ts createArrayType.</summary>
    internal Type CreateArrayType(Type elementType, bool isReadonly = false) =>
        CreateTypeFromGenericGlobalType(isReadonly ? globalReadonlyArrayType : globalArrayType, [elementType]);

    // ---- global types ----

    private SymbolRef GetGlobalSymbol(string name, SymbolFlags meaning, DiagnosticMessage? diagnostic) =>
        ResolveName(NodeRef.None, name, NodeRef.None, meaning, diagnostic, isUse: false);

    private SymbolRef GetGlobalTypeSymbol(string name, bool reportErrors) =>
        GetGlobalSymbol(name, SymbolFlags.Type, reportErrors ? Diagnostics.Cannot_find_global_type_0 : null);

    /// <summary>checker.ts getGlobalType.</summary>
    internal Type GetGlobalType(string name, int arity, bool reportErrors)
    {
        var symbol = GetGlobalTypeSymbol(name, reportErrors);
        return !symbol.IsNone || reportErrors ? GetTypeOfGlobalSymbol(symbol, arity) : Type.None;
    }

    private Type GetGlobalTypeOrUndefined(string name, int arity = 0)
    {
        var symbol = GetGlobalSymbol(name, SymbolFlags.Type, null);
        return symbol.IsNone ? Type.None : GetTypeOfGlobalSymbol(symbol, arity);
    }

    private Type GetTypeOfGlobalSymbol(SymbolRef symbol, int arity)
    {
        if (symbol.IsNone)
        {
            return arity != 0 ? emptyGenericType : emptyObjectType;
        }
        var type = GetDeclaredTypeOfSymbol(symbol);
        if ((Types.Flags(type) & TypeFlags.Object) == 0)
        {
            Error(GetTypeDeclarationOfGlobalSymbol(symbol), Diagnostics.Global_type_0_must_be_a_class_or_interface_type, SymbolName(symbol));
            return arity != 0 ? emptyGenericType : emptyObjectType;
        }
        if (Types.Object(type).TypeParameters.Count != arity)
        {
            Error(GetTypeDeclarationOfGlobalSymbol(symbol), Diagnostics.Global_type_0_must_have_1_type_parameter_s, SymbolName(symbol), arity);
            return arity != 0 ? emptyGenericType : emptyObjectType;
        }
        return type;
    }

    private NodeRef GetTypeDeclarationOfGlobalSymbol(SymbolRef symbol)
    {
        foreach (var declaration in DeclarationsOf(symbol) ?? [])
        {
            if (Kind(declaration) is SyntaxKind.ClassDeclaration or SyntaxKind.InterfaceDeclaration or SyntaxKind.EnumDeclaration)
            {
                return declaration;
            }
        }
        return NodeRef.None;
    }

    /// <summary>utilities.ts symbolName: the unescaped name.</summary>
    internal string SymbolName(SymbolRef symbol) => Utilities.UnescapeLeadingUnderscores(NameOf(symbol));
}
