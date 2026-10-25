// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (the type facts uses of the expression checker: getAdjustedTypeWithFacts,
// removeNullableByIntersection, getGlobalNonNullableTypeInstantiation, getNonNullableType, removeDefinitelyFalsyTypes,
// extractDefinitelyFalsyTypes, getDefinitelyFalsyPartOfType, isZeroBigInt)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (getTypeFacts and getTypeWithFacts: Checker.TypeFacts.cs, C4).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // checker.ts getAdjustedTypeWithFacts: like getTypeWithFacts, except that in strictNullChecks mode it replaces type
    // unknown with the union {} | null | undefined (and reduces that accordingly), and it intersects remaining
    // instantiable types with {}, {} | null, or {} | undefined in order to remove null and/or undefined.
    private Type GetAdjustedTypeWithFacts(Type type, TypeFacts facts)
    {
        var reduced = GetTypeWithFacts(strictNullChecks && (Types.Flags(type) & TypeFlags.Unknown) != 0 ? unknownUnionType : type, facts);
        if (reduced == unknownUnionType)
        {
            reduced = unknownType; // recombineUnknownType
        }
        if (strictNullChecks)
        {
            switch (facts)
            {
                case TypeFacts.NEUndefined:
                    return RemoveNullableByIntersection(reduced, TypeFacts.EQUndefined, TypeFacts.EQNull, TypeFacts.IsNull, nullType);
                case TypeFacts.NENull:
                    return RemoveNullableByIntersection(reduced, TypeFacts.EQNull, TypeFacts.EQUndefined, TypeFacts.IsUndefined, undefinedType);
                case TypeFacts.NEUndefinedOrNull:
                case TypeFacts.Truthy:
                    return MapType(reduced, t => HasTypeFacts(t, TypeFacts.EQUndefinedOrNull) ? GetGlobalNonNullableTypeInstantiation(t) : t);
            }
        }
        return reduced;
    }

    // checker.ts removeNullableByIntersection
    private Type RemoveNullableByIntersection(Type type, TypeFacts targetFacts, TypeFacts otherFacts, TypeFacts otherIncludesFacts, Type otherType)
    {
        var facts = GetTypeFacts(type, TypeFacts.EQUndefined | TypeFacts.EQNull | TypeFacts.IsUndefined | TypeFacts.IsNull);
        // Simply return the type if it never compares equal to the target nullable.
        if ((facts & targetFacts) == 0)
        {
            return type;
        }
        // By default we intersect with a union of {} and the opposite nullable.
        var emptyAndOtherUnion = GetUnionType([emptyObjectType, otherType]);
        // For each constituent type that can compare equal to the target nullable, intersect with the above union
        // if the type doesn't already include the opppsite nullable and the constituent can compare equal to the
        // opposite nullable; otherwise, just intersect with {}.
        return MapType(type, t => HasTypeFacts(t, targetFacts)
            ? GetIntersectionType([t, (facts & otherIncludesFacts) == 0 && HasTypeFacts(t, otherFacts) ? emptyAndOtherUnion : emptyObjectType])
            : t);
    }

    private SymbolRef deferredGlobalNonNullableTypeAlias;

    // checker.ts getGlobalNonNullableTypeInstantiation
    private Type GetGlobalNonNullableTypeInstantiation(Type type)
    {
        if (deferredGlobalNonNullableTypeAlias.IsNone)
        {
            var alias = GetGlobalSymbol("NonNullable", SymbolFlags.TypeAlias, null);
            deferredGlobalNonNullableTypeAlias = alias.IsNone ? unknownSymbol : alias;
        }
        return deferredGlobalNonNullableTypeAlias != unknownSymbol
            ? GetTypeAliasInstantiation(deferredGlobalNonNullableTypeAlias, [type])
            : GetIntersectionType([type, emptyObjectType]);
    }

    /// <summary>checker.ts getNonNullableType.</summary>
    internal Type GetNonNullableType(Type type) => strictNullChecks ? GetAdjustedTypeWithFacts(type, TypeFacts.NEUndefinedOrNull) : type;

    /// <summary>checker.ts removeDefinitelyFalsyTypes.</summary>
    internal Type RemoveDefinitelyFalsyTypes(Type type) => FilterType(type, t => HasTypeFacts(t, TypeFacts.Truthy));

    /// <summary>checker.ts extractDefinitelyFalsyTypes.</summary>
    internal Type ExtractDefinitelyFalsyTypes(Type type) => MapType(type, GetDefinitelyFalsyPartOfType);

    // checker.ts getDefinitelyFalsyPartOfType
    private Type GetDefinitelyFalsyPartOfType(Type type)
    {
        var flags = Types.Flags(type);
        return (flags & TypeFlags.String) != 0 ? emptyStringType
            : (flags & TypeFlags.Number) != 0 ? zeroType
            : (flags & TypeFlags.BigInt) != 0 ? zeroBigIntType
            : type == regularFalseType || type == falseType ||
              (flags & (TypeFlags.Void | TypeFlags.Undefined | TypeFlags.Null | TypeFlags.AnyOrUnknown)) != 0 ||
              ((flags & TypeFlags.StringLiteral) != 0 && ((string)Types.Literal(type).Value!).Length == 0) ||
              ((flags & TypeFlags.NumberLiteral) != 0 && (double)Types.Literal(type).Value! == 0) ||
              ((flags & TypeFlags.BigIntLiteral) != 0 && IsZeroBigInt(type)) ? type
            : neverType;
    }

    // checker.ts isZeroBigInt
    private bool IsZeroBigInt(Type type) => (string)Types.Literal(type).Value! == "0";
}
