// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (TypeFacts, getTypeFacts, hasTypeFacts, getTypeFactsWorker,
// getIntersectionTypeFacts, getTypeWithFacts, isFunctionObjectType)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/flow.go and internal/checker/checker.go (getTypeFacts)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C4 (the relations need the type facts of signature parameters and
// optional properties). Control-flow narrowing (C7) builds on these functions.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>checker.ts TypeFacts.</summary>
[Flags]
#pragma warning disable CA1069 // aliases with the same value are part of TypeScript's enum
internal enum TypeFacts
{
    None = 0,
    TypeofEQString = 1 << 0,
    TypeofEQNumber = 1 << 1,
    TypeofEQBigInt = 1 << 2,
    TypeofEQBoolean = 1 << 3,
    TypeofEQSymbol = 1 << 4,
    TypeofEQObject = 1 << 5,
    TypeofEQFunction = 1 << 6,
    TypeofEQHostObject = 1 << 7,
    TypeofNEString = 1 << 8,
    TypeofNENumber = 1 << 9,
    TypeofNEBigInt = 1 << 10,
    TypeofNEBoolean = 1 << 11,
    TypeofNESymbol = 1 << 12,
    TypeofNEObject = 1 << 13,
    TypeofNEFunction = 1 << 14,
    TypeofNEHostObject = 1 << 15,
    EQUndefined = 1 << 16,
    EQNull = 1 << 17,
    EQUndefinedOrNull = 1 << 18,
    NEUndefined = 1 << 19,
    NENull = 1 << 20,
    NEUndefinedOrNull = 1 << 21,
    Truthy = 1 << 22,
    Falsy = 1 << 23,
    IsUndefined = 1 << 24,
    IsNull = 1 << 25,
    IsUndefinedOrNull = IsUndefined | IsNull,
    All = (1 << 27) - 1,
    BaseStringStrictFacts = TypeofEQString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull,
    BaseStringFacts = BaseStringStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    StringStrictFacts = BaseStringStrictFacts | Truthy | Falsy,
    StringFacts = BaseStringFacts | Truthy,
    EmptyStringStrictFacts = BaseStringStrictFacts | Falsy,
    EmptyStringFacts = BaseStringFacts,
    NonEmptyStringStrictFacts = BaseStringStrictFacts | Truthy,
    NonEmptyStringFacts = BaseStringFacts | Truthy,
    BaseNumberStrictFacts = TypeofEQNumber | TypeofNEString | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull,
    BaseNumberFacts = BaseNumberStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    NumberStrictFacts = BaseNumberStrictFacts | Truthy | Falsy,
    NumberFacts = BaseNumberFacts | Truthy,
    ZeroNumberStrictFacts = BaseNumberStrictFacts | Falsy,
    ZeroNumberFacts = BaseNumberFacts,
    NonZeroNumberStrictFacts = BaseNumberStrictFacts | Truthy,
    NonZeroNumberFacts = BaseNumberFacts | Truthy,
    BaseBigIntStrictFacts = TypeofEQBigInt | TypeofNEString | TypeofNENumber | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull,
    BaseBigIntFacts = BaseBigIntStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    BigIntStrictFacts = BaseBigIntStrictFacts | Truthy | Falsy,
    BigIntFacts = BaseBigIntFacts | Truthy,
    ZeroBigIntStrictFacts = BaseBigIntStrictFacts | Falsy,
    ZeroBigIntFacts = BaseBigIntFacts,
    NonZeroBigIntStrictFacts = BaseBigIntStrictFacts | Truthy,
    NonZeroBigIntFacts = BaseBigIntFacts | Truthy,
    BaseBooleanStrictFacts = TypeofEQBoolean | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull,
    BaseBooleanFacts = BaseBooleanStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    BooleanStrictFacts = BaseBooleanStrictFacts | Truthy | Falsy,
    BooleanFacts = BaseBooleanFacts | Truthy,
    FalseStrictFacts = BaseBooleanStrictFacts | Falsy,
    FalseFacts = BaseBooleanFacts,
    TrueStrictFacts = BaseBooleanStrictFacts | Truthy,
    TrueFacts = BaseBooleanFacts | Truthy,
    SymbolStrictFacts = TypeofEQSymbol | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull | Truthy,
    SymbolFacts = SymbolStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    ObjectStrictFacts = TypeofEQObject | TypeofEQHostObject | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEFunction | NEUndefined | NENull | NEUndefinedOrNull | Truthy,
    ObjectFacts = ObjectStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    FunctionStrictFacts = TypeofEQFunction | TypeofEQHostObject | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | NEUndefined | NENull | NEUndefinedOrNull | Truthy,
    FunctionFacts = FunctionStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    VoidFacts = TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | EQUndefined | EQUndefinedOrNull | NENull | Falsy,
    UndefinedFacts = TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | EQUndefined | EQUndefinedOrNull | NENull | Falsy | IsUndefined,
    NullFacts = TypeofEQObject | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEFunction | TypeofNEHostObject | EQNull | EQUndefinedOrNull | NEUndefined | Falsy | IsNull,
    EmptyObjectStrictFacts = All & ~(EQUndefined | EQNull | EQUndefinedOrNull | IsUndefinedOrNull),
    EmptyObjectFacts = All & ~IsUndefinedOrNull,
    UnknownFacts = All & ~IsUndefinedOrNull,
    AllTypeofNE = TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | NEUndefined,
    OrFactsMask = TypeofEQFunction | TypeofNEObject,
    AndFactsMask = All & ~OrFactsMask,
}
#pragma warning restore CA1069

internal sealed partial class Checker
{
    /// <summary>checker.ts getTypeFacts.</summary>
    internal TypeFacts GetTypeFacts(Type type, TypeFacts mask) => GetTypeFactsWorker(type, mask) & mask;

    /// <summary>checker.ts hasTypeFacts.</summary>
    internal bool HasTypeFacts(Type type, TypeFacts mask) => GetTypeFacts(type, mask) != 0;

    private TypeFacts GetTypeFactsWorker(Type type, TypeFacts callerOnlyNeeds)
    {
        if ((Types.Flags(type) & (TypeFlags.Intersection | TypeFlags.Instantiable)) != 0)
        {
            var constraint = GetBaseConstraintOfType(type);
            type = constraint.IsNone ? unknownType : constraint;
        }
        var flags = Types.Flags(type);
        if ((flags & (TypeFlags.String | TypeFlags.StringMapping)) != 0)
        {
            return strictNullChecks ? TypeFacts.StringStrictFacts : TypeFacts.StringFacts;
        }
        if ((flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral)) != 0)
        {
            var isEmpty = (flags & TypeFlags.StringLiteral) != 0 && ((string)Types.Literal(type).Value!).Length == 0;
            return strictNullChecks
                ? isEmpty ? TypeFacts.EmptyStringStrictFacts : TypeFacts.NonEmptyStringStrictFacts
                : isEmpty ? TypeFacts.EmptyStringFacts : TypeFacts.NonEmptyStringFacts;
        }
        if ((flags & (TypeFlags.Number | TypeFlags.Enum)) != 0)
        {
            return strictNullChecks ? TypeFacts.NumberStrictFacts : TypeFacts.NumberFacts;
        }
        if ((flags & TypeFlags.NumberLiteral) != 0)
        {
            var isZero = Types.Literal(type).Value is double d && d == 0;
            return strictNullChecks
                ? isZero ? TypeFacts.ZeroNumberStrictFacts : TypeFacts.NonZeroNumberStrictFacts
                : isZero ? TypeFacts.ZeroNumberFacts : TypeFacts.NonZeroNumberFacts;
        }
        if ((flags & TypeFlags.BigInt) != 0)
        {
            return strictNullChecks ? TypeFacts.BigIntStrictFacts : TypeFacts.BigIntFacts;
        }
        if ((flags & TypeFlags.BigIntLiteral) != 0)
        {
            // isZeroBigInt
            var isZero = Types.Literal(type).Value is string s && s == "0";
            return strictNullChecks
                ? isZero ? TypeFacts.ZeroBigIntStrictFacts : TypeFacts.NonZeroBigIntStrictFacts
                : isZero ? TypeFacts.ZeroBigIntFacts : TypeFacts.NonZeroBigIntFacts;
        }
        if ((flags & TypeFlags.Boolean) != 0)
        {
            return strictNullChecks ? TypeFacts.BooleanStrictFacts : TypeFacts.BooleanFacts;
        }
        if ((flags & TypeFlags.BooleanLike) != 0)
        {
            var isFalse = type == falseType || type == regularFalseType;
            return strictNullChecks
                ? isFalse ? TypeFacts.FalseStrictFacts : TypeFacts.TrueStrictFacts
                : isFalse ? TypeFacts.FalseFacts : TypeFacts.TrueFacts;
        }
        if ((flags & TypeFlags.Object) != 0)
        {
            var possibleFacts = strictNullChecks
                ? TypeFacts.EmptyObjectStrictFacts | TypeFacts.FunctionStrictFacts | TypeFacts.ObjectStrictFacts
                : TypeFacts.EmptyObjectFacts | TypeFacts.FunctionFacts | TypeFacts.ObjectFacts;
            if ((callerOnlyNeeds & possibleFacts) == 0)
            {
                // If the caller doesn't care about any of the facts that we could possibly produce,
                // return zero so we can skip resolving members.
                return 0;
            }
            return (Types.ObjectFlagsOf(type) & ObjectFlags.Anonymous) != 0 && IsEmptyObjectType(type)
                ? strictNullChecks ? TypeFacts.EmptyObjectStrictFacts : TypeFacts.EmptyObjectFacts
                : IsFunctionObjectType(type)
                ? strictNullChecks ? TypeFacts.FunctionStrictFacts : TypeFacts.FunctionFacts
                : strictNullChecks ? TypeFacts.ObjectStrictFacts : TypeFacts.ObjectFacts;
        }
        if ((flags & TypeFlags.Void) != 0)
        {
            return TypeFacts.VoidFacts;
        }
        if ((flags & TypeFlags.Undefined) != 0)
        {
            return TypeFacts.UndefinedFacts;
        }
        if ((flags & TypeFlags.Null) != 0)
        {
            return TypeFacts.NullFacts;
        }
        if ((flags & TypeFlags.ESSymbolLike) != 0)
        {
            return strictNullChecks ? TypeFacts.SymbolStrictFacts : TypeFacts.SymbolFacts;
        }
        if ((flags & TypeFlags.NonPrimitive) != 0)
        {
            return strictNullChecks ? TypeFacts.ObjectStrictFacts : TypeFacts.ObjectFacts;
        }
        if ((flags & TypeFlags.Never) != 0)
        {
            return TypeFacts.None;
        }
        if ((flags & TypeFlags.Union) != 0)
        {
            var facts = TypeFacts.None;
            foreach (var t in Types.Elements(Types.Union(type).Types).ToArray())
            {
                facts |= GetTypeFactsWorker(t, callerOnlyNeeds);
            }
            return facts;
        }
        if ((flags & TypeFlags.Intersection) != 0)
        {
            return GetIntersectionTypeFacts(type, callerOnlyNeeds);
        }
        return TypeFacts.UnknownFacts;
    }

    private TypeFacts GetIntersectionTypeFacts(Type type, TypeFacts callerOnlyNeeds)
    {
        // When an intersection contains a primitive type we ignore object type constituents as they are
        // presumably type tags. For example, in string & { __kind__: "name" } we ignore the object type.
        var ignoreObjects = MaybeTypeOfKind(type, TypeFlags.Primitive);
        // When computing the type facts of an intersection type, certain type facts are computed as `and`
        // and others are computed as `or`.
        var oredFacts = TypeFacts.None;
        var andedFacts = TypeFacts.All;
        foreach (var t in Types.Elements(Types.Union(type).Types).ToArray())
        {
            if (!(ignoreObjects && (Types.Flags(t) & TypeFlags.Object) != 0))
            {
                var f = GetTypeFactsWorker(t, callerOnlyNeeds);
                oredFacts |= f;
                andedFacts &= f;
            }
        }
        return (oredFacts & TypeFacts.OrFactsMask) | (andedFacts & TypeFacts.AndFactsMask);
    }

    /// <summary>checker.ts getTypeWithFacts.</summary>
    internal Type GetTypeWithFacts(Type type, TypeFacts include) => FilterType(type, new HasTypeFactsPredicate(this, include));

    // getTypeWithFacts' predicate as a struct (module AR2, Track O: no closure per call)
    private readonly struct HasTypeFactsPredicate(Checker c, TypeFacts include) : ITypePredicate
    {
        public bool Invoke(Type t) => c.HasTypeFacts(t, include);
    }

    /// <summary>checker.ts isFunctionObjectType.</summary>
    internal bool IsFunctionObjectType(Type type)
    {
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.EvolvingArray) != 0)
        {
            return false;
        }
        // We do a quick check for a "bind" property before performing the more expensive subtype
        // check. This gives us a quicker out in the common case where an object type is not a function.
        var resolved = ResolveStructuredTypeMembers(type);
        return resolved.CallSignatures!.Count > 0 || resolved.ConstructSignatures!.Count > 0 ||
            (!resolved.Members!.Get("bind").IsNone && IsTypeSubtypeOf(type, globalFunctionType));
    }
}
