// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts and utilities.ts (functions the relations use: error display names,
// isContextSensitive, excess property and weak type helpers, discriminants and best matches, template literals)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go and internal/checker/relater.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C4. The caches TypeScript keeps in type objects (regularType of
// fresh object literals, keyPropertyName and constituentMap of unions) are per-checker dictionaries.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // ---- error display ----

    /// <summary>checker.ts getTypeNamesForErrorDisplay.</summary>
    internal (string Left, string Right) GetTypeNamesForErrorDisplay(Type left, Type right)
    {
        var leftSymbol = Types.Symbol(left);
        var rightSymbol = Types.Symbol(right);
        var leftStr = SymbolValueDeclarationIsContextSensitive(leftSymbol) ? TypeToString(left, ValueDeclarationOf(leftSymbol)) : TypeToString(left);
        var rightStr = SymbolValueDeclarationIsContextSensitive(rightSymbol) ? TypeToString(right, ValueDeclarationOf(rightSymbol)) : TypeToString(right);
        if (leftStr == rightStr)
        {
            leftStr = GetTypeNameForErrorDisplay(left);
            rightStr = GetTypeNameForErrorDisplay(right);
        }
        return (leftStr, rightStr);
    }

    /// <summary>checker.ts getTypeNameForErrorDisplay.</summary>
    internal string GetTypeNameForErrorDisplay(Type type) => TypeToString(type, default, TypeFormatFlags.UseFullyQualifiedType);

    /// <summary>checker.ts symbolValueDeclarationIsContextSensitive.</summary>
    internal bool SymbolValueDeclarationIsContextSensitive(SymbolRef symbol)
    {
        if (symbol.IsNone)
        {
            return false;
        }
        var valueDeclaration = ValueDeclarationOf(symbol);
        return !valueDeclaration.IsNone && UtilitiesPublic.IsExpression(StoreOf(valueDeclaration), valueDeclaration.Node) && !IsContextSensitive(valueDeclaration);
    }

    /// <summary>checker.ts isContextSensitive.</summary>
    internal bool IsContextSensitive(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.FunctionDeclaration: // Function declarations can have context when annotated with a jsdoc @type
                return IsContextSensitiveFunctionLikeDeclaration(node);
            case SyntaxKind.ObjectLiteralExpression:
                foreach (var p in store.Nodes(store.Properties(n)))
                {
                    if (IsContextSensitive(At(node, p)))
                    {
                        return true;
                    }
                }
                return false;
            case SyntaxKind.ArrayLiteralExpression:
                foreach (var e in store.Nodes(store.Elements(n)))
                {
                    if (IsContextSensitive(At(node, e)))
                    {
                        return true;
                    }
                }
                return false;
            case SyntaxKind.ConditionalExpression:
                return IsContextSensitive(At(node, store.WhenTrue(n))) || IsContextSensitive(At(node, store.WhenFalse(n)));
            case SyntaxKind.BinaryExpression:
            {
                var op = store.Kind(store.OperatorToken(n));
                return op is SyntaxKind.BarBarToken or SyntaxKind.QuestionQuestionToken &&
                    (IsContextSensitive(At(node, store.Left(n))) || IsContextSensitive(At(node, store.Right(n))));
            }
            case SyntaxKind.PropertyAssignment:
                return IsContextSensitive(At(node, store.Initializer(n)));
            case SyntaxKind.ParenthesizedExpression:
                return IsContextSensitive(At(node, store.Expression(n)));
            case SyntaxKind.JsxAttributes:
            {
                foreach (var p in store.Nodes(store.Properties(n)))
                {
                    if (IsContextSensitive(At(node, p)))
                    {
                        return true;
                    }
                }
                var parent = store.Parent(n);
                if (store.Kind(parent) == SyntaxKind.JsxOpeningElement)
                {
                    foreach (var child in store.Nodes(store.Children(store.Parent(parent))))
                    {
                        if (IsContextSensitive(At(node, child)))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            case SyntaxKind.JsxAttribute:
            {
                // If there is no initializer, JSX attribute has a boolean value of true which is not context sensitive.
                var initializer = store.Initializer(n);
                return !initializer.IsNone && IsContextSensitive(At(node, initializer));
            }
            case SyntaxKind.JsxExpression:
            case SyntaxKind.YieldExpression:
            {
                // It is possible to that node.expression is undefined (e.g <div x={} />)
                var expression = store.Expression(n);
                return !expression.IsNone && IsContextSensitive(At(node, expression));
            }
        }
        return false;
    }

    // checker.ts isContextSensitiveFunctionLikeDeclaration
    private bool IsContextSensitiveFunctionLikeDeclaration(NodeRef node) =>
        HasContextSensitiveParameters(StoreOf(node), node.Node) || HasContextSensitiveReturnExpression(node) || HasContextSensitiveYieldExpression(node);

    // utilities.ts hasContextSensitiveParameters (JSDoc type annotations: C9)
    private static bool HasContextSensitiveParameters(NodeStore store, Node node)
    {
        // Functions with type parameters are not context sensitive.
        if (store.TypeParameters(node).IsNone)
        {
            // Functions with any parameters that lack type annotations are context sensitive.
            var parameters = store.Nodes(store.Parameters(node));
            foreach (var p in parameters)
            {
                // !getEffectiveTypeAnnotationNode(p) (JSDoc types of JavaScript parameters count)
                if (store.Type(p).IsNone && (!Utilities.IsInJSFile(store, p) || GetJSDocType(store, p).IsNone))
                {
                    return true;
                }
            }
            if (store.Kind(node) != SyntaxKind.ArrowFunction)
            {
                // If the first parameter is not an explicit 'this' parameter, then the function has
                // an implicit 'this' parameter which is subject to contextual typing.
                if (!(parameters.Length > 0 && ParameterIsThisKeyword(store, parameters[0])))
                {
                    return (store.Flags(node) & NodeFlags.ContainsThis) != 0;
                }
            }
        }
        return false;
    }

    // checker.ts hasContextSensitiveReturnExpression
    private bool HasContextSensitiveReturnExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var body = store.Body(node.Node);
        if (!store.TypeParameters(node.Node).IsNone || !store.Type(node.Node).IsNone || body.IsNone)
        {
            return false;
        }
        if (store.Kind(body) != SyntaxKind.Block)
        {
            return IsContextSensitive(At(node, body));
        }
        var found = false;
        ForEachReturnStatement(store, body, statement =>
        {
            var expression = store.Expression(statement);
            found |= !found && !expression.IsNone && IsContextSensitive(At(node, expression));
        });
        return found;
    }

    // checker.ts hasContextSensitiveYieldExpression
    private bool HasContextSensitiveYieldExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var body = store.Body(node.Node);
        if ((GetFunctionFlagsOf(store, node.Node) & FunctionFlags.Generator) == 0 || body.IsNone)
        {
            return false;
        }
        var found = false;
        ForEachYieldExpression(store, body, yieldExpression => found |= !found && IsContextSensitive(At(node, yieldExpression)));
        return found;
    }

    /// <summary>checker.ts getSuggestedTypeForNonexistentStringLiteralType (None when there is no suggestion).</summary>
    internal Type GetSuggestedTypeForNonexistentStringLiteralType(Type source, Type target)
    {
        var candidates = new List<Type>();
        foreach (var type in Types.Elements(Types.Union(target).Types))
        {
            if ((Types.Flags(type) & TypeFlags.StringLiteral) != 0)
            {
                candidates.Add(type);
            }
        }
        return Core.GetSpellingSuggestion((string)Types.Literal(source).Value!, candidates, type => (string)Types.Literal(type).Value!);
    }

    /// <summary>checker.ts getSuggestionForNonexistentProperty.</summary>
    internal string? GetSuggestionForNonexistentProperty(NodeRef name, Type containingType)
    {
        var store = StoreOf(name);
        var text = store.Texts.GetString(store.EscapedText(name.Node)) ?? "";
        var suggestion = GetSuggestedSymbolForNonexistentProperty(text, containingType);
        return suggestion.IsNone ? null : SymbolName(suggestion);
    }

    // ---- arrays and tuples ----

    /// <summary>checker.ts isMutableArrayOrTuple.</summary>
    internal bool IsMutableArrayOrTuple(Type type) => (IsArrayType(type) && !IsReadonlyArrayType(type)) || (IsTupleType(type) && !TupleTarget(type).Readonly);

    // checker.ts isEmptyLiteralType
    private bool IsEmptyLiteralType(Type type) => strictNullChecks ? type == implicitNeverType : type == undefinedWideningType;

    /// <summary>checker.ts isEmptyArrayLiteralType.</summary>
    internal bool IsEmptyArrayLiteralType(Type type)
    {
        var elementType = GetElementTypeOfArrayType(type);
        return !elementType.IsNone && IsEmptyLiteralType(elementType);
    }

    /// <summary>checker.ts isTupleLikeType.</summary>
    internal bool IsTupleLikeType(Type type)
    {
        if (IsTupleType(type) || !GetPropertyOfType(type, "0").IsNone)
        {
            return true;
        }
        if (!IsArrayLikeType(type))
        {
            return false;
        }
        var lengthType = GetTypeOfPropertyOfType(type, "length");
        return !lengthType.IsNone && EveryType(lengthType, t => (Types.Flags(t) & TypeFlags.NumberLiteral) != 0);
    }

    /// <summary>checker.ts isArrayOrTupleLikeType.</summary>
    internal bool IsArrayOrTupleLikeType(Type type) => IsArrayLikeType(type) || IsTupleLikeType(type);

    /// <summary>checker.ts isSingleElementGenericTupleType.</summary>
    internal bool IsSingleElementGenericTupleType(Type type) => IsGenericTupleType(type) && TupleTarget(type).ElementFlags!.Length == 1;

    // checker.ts getStartElementCount: the count of starting consecutive tuple elements of the given kind(s)
    private static int GetStartElementCount(ObjectTypeData type, ElementFlags flags)
    {
        var index = Array.FindIndex(type.ElementFlags!, f => (f & flags) == 0);
        return index >= 0 ? index : type.ElementFlags!.Length;
    }

    // ---- object literals and excess properties ----

    /// <summary>checker.ts isObjectLiteralType.</summary>
    internal bool IsObjectLiteralType(Type type) => (Types.ObjectFlagsOf(type) & ObjectFlags.ObjectLiteral) != 0;

    /// <summary>checker.ts isKnownProperty.</summary>
    internal bool IsKnownProperty(Type targetType, string name, bool isComparingJsxAttributes)
    {
        var flags = Types.Flags(targetType);
        if ((flags & TypeFlags.Object) != 0)
        {
            // For backwards compatibility a symbol-named property is satisfied by a string index signature. This
            // is incorrect and inconsistent with element access expressions, where it is an error, so eventually
            // we should remove this exception.
            if (!GetPropertyOfObjectType(targetType, name).IsNone ||
                GetApplicableIndexInfoForName(targetType, name) is not null ||
                (IsLateBoundName(name) && GetIndexInfoOfType(targetType, stringType) is not null) ||
                (isComparingJsxAttributes && IsHyphenatedJsxName(name)))
            {
                // For JSXAttributes, if the attribute has a hyphenated name, consider that the attribute to be known.
                return true;
            }
        }
        if ((flags & TypeFlags.Substitution) != 0)
        {
            return IsKnownProperty(Types.Data<SubstitutionData>(targetType).BaseType, name, isComparingJsxAttributes);
        }
        if ((flags & TypeFlags.UnionOrIntersection) != 0 && IsExcessPropertyCheckTarget(targetType))
        {
            foreach (var t in Types.Elements(Types.Union(targetType).Types).ToArray())
            {
                if (IsKnownProperty(t, name, isComparingJsxAttributes))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>checker.ts isExcessPropertyCheckTarget.</summary>
    internal bool IsExcessPropertyCheckTarget(Type type)
    {
        var flags = Types.Flags(type);
        return ((flags & TypeFlags.Object) != 0 && (Types.ObjectFlagsOf(type) & ObjectFlags.ObjectLiteralPatternWithComputedProperties) == 0) ||
            (flags & TypeFlags.NonPrimitive) != 0 ||
            ((flags & TypeFlags.Substitution) != 0 && IsExcessPropertyCheckTarget(Types.Data<SubstitutionData>(type).BaseType)) ||
            ((flags & TypeFlags.Union) != 0 && ExistsType(Types.Elements(Types.Union(type).Types), IsExcessPropertyCheckTarget)) ||
            ((flags & TypeFlags.Intersection) != 0 && AllTypes(Types.Elements(Types.Union(type).Types), IsExcessPropertyCheckTarget));
    }

    /// <summary>checker.ts isTypeSubsetOf.</summary>
    internal bool IsTypeSubsetOf(Type source, Type target) =>
        source == target || (Types.Flags(source) & TypeFlags.Never) != 0 || ((Types.Flags(target) & TypeFlags.Union) != 0 && IsTypeSubsetOfUnion(source, target));

    private bool IsTypeSubsetOfUnion(Type source, Type target)
    {
        var targetTypes = Types.Elements(Types.Union(target).Types);
        if ((Types.Flags(source) & TypeFlags.Union) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(source).Types))
            {
                if (!ContainsType(targetTypes, t))
                {
                    return false;
                }
            }
            return true;
        }
        if ((Types.Flags(source) & TypeFlags.EnumLike) != 0 && GetBaseTypeOfEnumLikeType(source) == target)
        {
            return true;
        }
        return ContainsType(targetTypes, source);
    }

    /// <summary>checker.ts extractTypesOfKind.</summary>
    internal Type ExtractTypesOfKind(Type type, TypeFlags kind) => FilterType(type, t => (Types.Flags(t) & kind) != 0);

    /// <summary>checker.ts filterPrimitivesIfContainsNonPrimitive.</summary>
    internal Type FilterPrimitivesIfContainsNonPrimitive(Type type)
    {
        if (MaybeTypeOfKind(type, TypeFlags.NonPrimitive))
        {
            var result = FilterType(type, t => (Types.Flags(t) & TypeFlags.Primitive) == 0);
            if ((Types.Flags(result) & TypeFlags.Never) == 0)
            {
                return result;
            }
        }
        return type;
    }

    private readonly Dictionary<Type, Type> regularTypesOfObjectLiterals = [];

    /// <summary>
    /// checker.ts getRegularTypeOfObjectLiteral: if the object literal is subject to the excess properties check, a
    /// new type that is exempt (object literal members recursively exempt; signatures left alone).
    /// </summary>
    internal Type GetRegularTypeOfObjectLiteral(Type type)
    {
        if (!(IsObjectLiteralType(type) && (Types.ObjectFlagsOf(type) & ObjectFlags.FreshLiteral) != 0))
        {
            return type;
        }
        if (regularTypesOfObjectLiterals.TryGetValue(type, out var regularType))
        {
            return regularType;
        }
        var resolved = ResolveStructuredTypeMembers(type);
        // transformTypeOfMembers
        var members = new SymTable(Names);
        foreach (var property in GetPropertiesOfObjectType(type))
        {
            var original = GetTypeOfSymbol(property);
            var updated = GetRegularTypeOfObjectLiteral(original);
            members.Set(NameOf(property), updated == original ? property : CreateSymbolWithType(property, updated));
        }
        var regularNew = CreateAnonymousType(Types.Symbol(type), members, Types.View(resolved.CallSignatures), Types.View(resolved.ConstructSignatures), Types.View(resolved.IndexInfos));
        ref var record = ref Types.Record(regularNew);
        record.Flags = Types.Flags(type);
        record.ObjectFlags |= Types.ObjectFlagsOf(type) & ~ObjectFlags.FreshLiteral;
        regularTypesOfObjectLiterals[type] = regularNew;
        return regularNew;
    }

    // ---- discriminants and best matches ----

    /// <summary>checker.ts findDiscriminantProperties.</summary>
    internal List<SymbolRef>? FindDiscriminantProperties(Items<SymbolRef> sourceProperties, Type target)
    {
        List<SymbolRef>? result = null;
        foreach (var sourceProperty in sourceProperties)
        {
            if (IsDiscriminantProperty(target, NameOf(sourceProperty)))
            {
                (result ??= []).Add(sourceProperty);
            }
        }
        return result;
    }

    // types.ts UnionType.keyPropertyName / constituentMap
    private readonly Dictionary<Type, (string KeyPropertyName, Dictionary<Type, Type>? ConstituentMap)> keyPropertyNames = [];

    // checker.ts mapTypesByKeyProperty: a map keyed by the literal types of the property in each constituent type;
    // null if some key property has a non-literal type or if less than 10 or less than 50% of the constituents have
    // a unique key. Entries with duplicate keys have unknownType as the value.
    private Dictionary<Type, Type>? MapTypesByKeyProperty(Type[] types, string name)
    {
        var map = new Dictionary<Type, Type>();
        var count = 0;
        foreach (var type in types)
        {
            if ((Types.Flags(type) & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.InstantiableNonPrimitive)) != 0)
            {
                var discriminant = GetTypeOfPropertyOfType(type, name);
                if (discriminant.IsNone || !IsLiteralType(discriminant))
                {
                    return null;
                }
                var duplicate = false;
                ForEachType(discriminant, t =>
                {
                    var id = GetRegularTypeOfLiteralType(t);
                    if (!map.TryGetValue(id, out var existing))
                    {
                        map[id] = type;
                    }
                    else if (existing != unknownType)
                    {
                        map[id] = unknownType;
                        duplicate = true;
                    }
                });
                if (!duplicate)
                {
                    count++;
                }
            }
        }
        return count >= 10 && count * 2 >= types.Length ? map : null;
    }

    // checker.ts getKeyPropertyName: the name of a discriminant property for which it was possible and feasible to
    // construct a map of constituent types keyed by the literal types of the property.
    private string? GetKeyPropertyName(Type unionType)
    {
        var types = Types.Elements(Types.Union(unionType).Types).ToArray();
        // We only construct maps for unions with many non-primitive constituents.
        if (types.Length < 10 || (Types.ObjectFlagsOf(unionType) & ObjectFlags.PrimitiveUnion) != 0 ||
            Array.FindAll(types, t => (Types.Flags(t) & (TypeFlags.Object | TypeFlags.InstantiableNonPrimitive)) != 0).Length < 10)
        {
            return null;
        }
        if (!keyPropertyNames.TryGetValue(unionType, out var entry))
        {
            // The candidate key property name is the name of the first property with a unit type in one of the
            // constituent types.
            string? keyPropertyName = null;
            foreach (var t in types)
            {
                if ((Types.Flags(t) & (TypeFlags.Object | TypeFlags.InstantiableNonPrimitive)) != 0)
                {
                    foreach (var p in GetPropertiesOfType(t))
                    {
                        if (IsUnitType(GetTypeOfSymbol(p)))
                        {
                            keyPropertyName = NameOf(p);
                            break;
                        }
                    }
                    if (keyPropertyName is not null)
                    {
                        break;
                    }
                }
            }
            var mapByKeyProperty = keyPropertyName is not null ? MapTypesByKeyProperty(types, keyPropertyName) : null;
            entry = (mapByKeyProperty is not null ? keyPropertyName! : "", mapByKeyProperty);
            keyPropertyNames[unionType] = entry;
        }
        return entry.KeyPropertyName.Length > 0 ? entry.KeyPropertyName : null;
    }

    // checker.ts getConstituentTypeForKeyType
    private Type GetConstituentTypeForKeyType(Type unionType, Type keyType)
    {
        var map = keyPropertyNames[unionType].ConstituentMap;
        if (map is null || !map.TryGetValue(GetRegularTypeOfLiteralType(keyType), out var result))
        {
            return Type.None;
        }
        return result != unknownType ? result : Type.None;
    }

    /// <summary>checker.ts getMatchingUnionConstituentForType.</summary>
    internal Type GetMatchingUnionConstituentForType(Type unionType, Type type)
    {
        var keyPropertyName = GetKeyPropertyName(unionType);
        var propType = keyPropertyName is not null ? GetTypeOfPropertyOfType(type, keyPropertyName) : Type.None;
        return propType.IsNone ? Type.None : GetConstituentTypeForKeyType(unionType, propType);
    }

    /// <summary>checker.ts discriminateTypeByDiscriminableItems.</summary>
    internal Type DiscriminateTypeByDiscriminableItems(Type target, List<(Func<Type> GetDiscriminatingType, string PropertyName)> discriminators, Func<Type, Type, bool> related)
    {
        var types = Types.Elements(Types.Union(target).Types).ToArray();
        var include = new int[types.Length];
        for (var i = 0; i < types.Length; i++)
        {
            var t = types[i];
            include[i] = (Types.Flags(t) & TypeFlags.Primitive) != 0 || (Types.Flags(GetReducedType(t)) & TypeFlags.Never) != 0 ? Ternary.False : Ternary.True;
        }
        foreach (var (getDiscriminatingType, propertyName) in discriminators)
        {
            // If the remaining target types include at least one with a matching discriminant, eliminate those that
            // have non-matching discriminants. This ensures that we ignore erroneous discriminators and gradually
            // refine the target set without eliminating every constituent (which would lead to `never`).
            var matched = false;
            for (var i = 0; i < types.Length; i++)
            {
                if (include[i] != Ternary.False)
                {
                    var targetType = GetTypeOfPropertyOrIndexSignatureOfType(types[i], propertyName);
                    if (!targetType.IsNone)
                    {
                        if (SomeType(getDiscriminatingType(), t => related(t, targetType)))
                        {
                            matched = true;
                        }
                        else
                        {
                            include[i] = Ternary.Maybe;
                        }
                    }
                }
            }
            // Turn each Ternary.Maybe into Ternary.False if there was a match. Otherwise, revert to Ternary.True.
            for (var i = 0; i < types.Length; i++)
            {
                if (include[i] == Ternary.Maybe)
                {
                    include[i] = matched ? Ternary.False : Ternary.True;
                }
            }
        }
        if (Array.IndexOf(include, Ternary.False) < 0)
        {
            return target;
        }
        var kept = new List<Type>();
        for (var i = 0; i < types.Length; i++)
        {
            if (include[i] != Ternary.False)
            {
                kept.Add(types[i]);
            }
        }
        var filtered = GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(kept), UnionReduction.None);
        return (Types.Flags(filtered) & TypeFlags.Never) != 0 ? target : filtered;
    }

    /// <summary>checker.ts findMatchingDiscriminantType (None when there is none).</summary>
    internal Type FindMatchingDiscriminantType(Type source, Type target, Func<Type, Type, int> isRelatedTo)
    {
        if ((Types.Flags(target) & TypeFlags.Union) != 0 && (Types.Flags(source) & (TypeFlags.Intersection | TypeFlags.Object)) != 0)
        {
            var match = GetMatchingUnionConstituentForType(target, source);
            if (!match.IsNone)
            {
                return match;
            }
            var sourceProperties = GetPropertiesOfType(source);
            var sourcePropertiesFiltered = FindDiscriminantProperties(sourceProperties, target);
            if (sourcePropertiesFiltered is not null)
            {
                var discriminators = sourcePropertiesFiltered.ConvertAll(p => ((Func<Type>)(() => GetTypeOfSymbol(p)), NameOf(p)));
                var discriminated = DiscriminateTypeByDiscriminableItems(target, discriminators, (s, t) => isRelatedTo(s, t) != Ternary.False);
                if (discriminated != target)
                {
                    return discriminated;
                }
            }
        }
        return Type.None;
    }

    /// <summary>checker.ts getBestMatchingType (None when there is none).</summary>
    internal Type GetBestMatchingType(Type source, Type target, Func<Type, Type, int>? isRelatedTo = null)
    {
        var result = FindMatchingDiscriminantType(source, target, isRelatedTo ?? CompareTypesAssignable);
        if (result.IsNone)
        {
            result = FindMatchingTypeReferenceOrTypeAliasReference(source, target);
        }
        if (result.IsNone)
        {
            result = FindBestTypeForObjectLiteral(source, target);
        }
        if (result.IsNone)
        {
            result = FindBestTypeForInvokable(source, target);
        }
        if (result.IsNone)
        {
            result = FindMostOverlappyType(source, target);
        }
        return result;
    }

    // checker.ts findMatchingTypeReferenceOrTypeAliasReference
    private Type FindMatchingTypeReferenceOrTypeAliasReference(Type source, Type unionTarget)
    {
        var sourceObjectFlags = Types.ObjectFlagsOf(source);
        if ((sourceObjectFlags & (ObjectFlags.Reference | ObjectFlags.Anonymous)) != 0 && (Types.Flags(unionTarget) & TypeFlags.Union) != 0)
        {
            foreach (var target in Types.Elements(Types.Union(unionTarget).Types).ToArray())
            {
                if ((Types.Flags(target) & TypeFlags.Object) != 0)
                {
                    var overlapObjFlags = sourceObjectFlags & Types.ObjectFlagsOf(target);
                    if ((overlapObjFlags & ObjectFlags.Reference) != 0)
                    {
                        if (Types.Object(source).Target == Types.Object(target).Target)
                        {
                            return target;
                        }
                        continue;
                    }
                    if ((overlapObjFlags & ObjectFlags.Anonymous) != 0)
                    {
                        var aliasSymbol = Types.Record(source).AliasSymbol;
                        if (!aliasSymbol.IsNone && aliasSymbol == Types.Record(target).AliasSymbol)
                        {
                            return target;
                        }
                    }
                }
            }
        }
        return Type.None;
    }

    // checker.ts findBestTypeForObjectLiteral
    private Type FindBestTypeForObjectLiteral(Type source, Type unionTarget)
    {
        if ((Types.ObjectFlagsOf(source) & ObjectFlags.ObjectLiteral) != 0 && SomeType(unionTarget, IsArrayLikeType))
        {
            foreach (var t in Types.Elements(Types.Union(unionTarget).Types).ToArray())
            {
                if (!IsArrayLikeType(t))
                {
                    return t;
                }
            }
        }
        return Type.None;
    }

    // checker.ts findBestTypeForInvokable
    private Type FindBestTypeForInvokable(Type source, Type unionTarget)
    {
        var signatureKind = SignatureKind.Call;
        var hasSignatures = GetSignaturesOfType(source, signatureKind).Count > 0;
        if (!hasSignatures)
        {
            signatureKind = SignatureKind.Construct;
            hasSignatures = GetSignaturesOfType(source, signatureKind).Count > 0;
        }
        if (hasSignatures)
        {
            foreach (var t in Types.Elements(Types.Union(unionTarget).Types).ToArray())
            {
                if (GetSignaturesOfType(t, signatureKind).Count > 0)
                {
                    return t;
                }
            }
        }
        return Type.None;
    }

    // checker.ts findMostOverlappyType
    private Type FindMostOverlappyType(Type source, Type unionTarget)
    {
        var bestMatch = Type.None;
        if ((Types.Flags(source) & (TypeFlags.Primitive | TypeFlags.InstantiablePrimitive)) == 0)
        {
            var matchingCount = 0;
            foreach (var target in Types.Elements(Types.Union(unionTarget).Types).ToArray())
            {
                if ((Types.Flags(target) & (TypeFlags.Primitive | TypeFlags.InstantiablePrimitive)) == 0)
                {
                    var overlap = GetIntersectionType([GetIndexType(source), GetIndexType(target)]);
                    if ((Types.Flags(overlap) & TypeFlags.Index) != 0)
                    {
                        // perfect overlap of keys
                        return target;
                    }
                    if (IsUnitType(overlap) || (Types.Flags(overlap) & TypeFlags.Union) != 0)
                    {
                        // We only want to account for literal types otherwise.
                        // If we have a union of index types, it seems likely that we
                        // needed to elaborate between two generic mapped types anyway.
                        var len = 1;
                        if ((Types.Flags(overlap) & TypeFlags.Union) != 0)
                        {
                            len = 0;
                            foreach (var t in Types.Elements(Types.Union(overlap).Types))
                            {
                                if ((Types.Flags(t) & TypeFlags.Unit) != 0)
                                {
                                    len++;
                                }
                            }
                        }
                        if (len >= matchingCount)
                        {
                            bestMatch = target;
                            matchingCount = len;
                        }
                    }
                }
            }
        }
        return bestMatch;
    }

    // ---- signatures and members ----

    /// <summary>checker.ts typeHasCallOrConstructSignatures.</summary>
    internal bool TypeHasCallOrConstructSignatures(Type type) =>
        GetSignaturesOfType(type, SignatureKind.Call).Count != 0 || GetSignaturesOfType(type, SignatureKind.Construct).Count != 0;

    /// <summary>checker.ts getEffectiveConstraintOfIntersection (None when there is none).</summary>
    internal Type GetEffectiveConstraintOfIntersection(Type[] types, bool targetIsUnion)
    {
        List<Type>? constraints = null;
        var hasDisjointDomainType = false;
        foreach (var t in types)
        {
            if ((Types.Flags(t) & TypeFlags.Instantiable) != 0)
            {
                // We keep following constraints as long as we have an instantiable type that is known
                // not to be circular or infinite (hence we stop on index access types).
                var constraint = GetConstraintOfType(t);
                while (!constraint.IsNone && (Types.Flags(constraint) & (TypeFlags.TypeParameter | TypeFlags.Index | TypeFlags.Conditional)) != 0)
                {
                    constraint = GetConstraintOfType(constraint);
                }
                if (!constraint.IsNone)
                {
                    (constraints ??= []).Add(constraint);
                    if (targetIsUnion)
                    {
                        constraints.Add(t);
                    }
                }
            }
            else if ((Types.Flags(t) & TypeFlags.DisjointDomains) != 0 || IsEmptyAnonymousObjectType(t))
            {
                hasDisjointDomainType = true;
            }
        }
        // If the target is a union type or if we are intersecting with types belonging to one of the
        // disjoint domains, we may end up producing a constraint that hasn't been examined before.
        if (constraints is not null && (targetIsUnion || hasDisjointDomainType))
        {
            if (hasDisjointDomainType)
            {
                // We add any types belong to one of the disjoint domains because they might cause the final
                // intersection operation to reduce the union constraints.
                foreach (var t in types)
                {
                    if ((Types.Flags(t) & TypeFlags.DisjointDomains) != 0 || IsEmptyAnonymousObjectType(t))
                    {
                        constraints.Add(t);
                    }
                }
            }
            // The source types were normalized; ensure the result is normalized too.
            return GetNormalizedType(GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(constraints), flags: IntersectionFlags.NoConstraintReduction), writing: false);
        }
        return Type.None;
    }

    /// <summary>checker.ts isNonGenericObjectType.</summary>
    internal bool IsNonGenericObjectType(Type type) => (Types.Flags(type) & TypeFlags.Object) != 0 && !IsGenericMappedType(type);

    /// <summary>checker.ts intersectTypes (None stands for undefined).</summary>
    internal Type IntersectTypes(Type type1, Type type2) => type1.IsNone ? type2 : type2.IsNone ? type1 : GetIntersectionType([type1, type2]);

    /// <summary>checker.ts isDistributionDependent.</summary>
    internal bool IsDistributionDependent(ConditionalRoot root)
    {
        if (!root.IsDistributive)
        {
            return false;
        }
        var store = StoreOf(root.Node);
        return IsTypeParameterPossiblyReferenced(root.CheckType, At(root.Node, store.TrueType(root.Node.Node))) ||
            IsTypeParameterPossiblyReferenced(root.CheckType, At(root.Node, store.FalseType(root.Node.Node)));
    }

    /// <summary>checker.ts isPartialMappedType.</summary>
    internal bool IsPartialMappedType(Type type) =>
        (Types.ObjectFlagsOf(type) & ObjectFlags.Mapped) != 0 && (GetMappedTypeModifiers(type) & MappedTypeModifiers.IncludeOptional) != 0;

    // the mapped type's declaration.nameType (None when the mapped type has no `as` clause)
    internal Type MappedTypeDeclarationNameType(Type type)
    {
        var declaration = Types.Object(type).Declaration;
        return declaration.IsNone || StoreOf(declaration).NameType(declaration.Node).IsNone ? Type.None : GetNameTypeFromMappedType(type);
    }

    /// <summary>checker.ts countTypes.</summary>
    internal int CountTypes(Type type) => (Types.Flags(type) & TypeFlags.Union) != 0 ? Types.Union(type).Types.Count : 1;

    /// <summary>checker.ts getUnmatchedProperties.</summary>
    internal List<SymbolRef> GetUnmatchedProperties(Type source, Type target, bool requireOptionalProperties, bool matchDiscriminantProperties, bool firstOnly = false)
    {
        var result = new List<SymbolRef>();
        foreach (var targetProp in GetPropertiesOfType(target))
        {
            // TODO: remove this when we support static private identifier fields and find other solutions to get privateNamesAndStaticFields test to pass
            if (IsStaticPrivateIdentifierProperty(targetProp))
            {
                continue;
            }
            if (requireOptionalProperties || ((SymbolFlagsOf(targetProp) & SymbolFlags.Optional) == 0 && (GetCheckFlags(targetProp) & CheckFlags.Partial) == 0))
            {
                var sourceProp = GetPropertyOfType(source, NameIdOf(targetProp));
                if (sourceProp.IsNone)
                {
                    result.Add(targetProp);
                }
                else if (matchDiscriminantProperties)
                {
                    var targetType = GetTypeOfSymbol(targetProp);
                    if ((Types.Flags(targetType) & TypeFlags.Unit) != 0)
                    {
                        var sourceType = GetTypeOfSymbol(sourceProp);
                        if (!((Types.Flags(sourceType) & TypeFlags.Any) != 0 || GetRegularTypeOfLiteralType(sourceType) == GetRegularTypeOfLiteralType(targetType)))
                        {
                            result.Add(targetProp);
                        }
                    }
                }
                if (firstOnly && result.Count > 0)
                {
                    return result;
                }
            }
        }
        return result;
    }

    /// <summary>checker.ts getUnmatchedProperty (None when every property matches).</summary>
    internal SymbolRef GetUnmatchedProperty(Type source, Type target, bool requireOptionalProperties, bool matchDiscriminantProperties)
    {
        var props = GetUnmatchedProperties(source, target, requireOptionalProperties, matchDiscriminantProperties, firstOnly: true);
        return props.Count > 0 ? props[0] : SymbolRef.None;
    }

    // checker.ts tupleTypesDefinitelyUnrelated
    private bool TupleTypesDefinitelyUnrelated(Type source, Type target)
    {
        var s = TupleTarget(source);
        var t = TupleTarget(target);
        return ((t.CombinedFlags & ElementFlags.Variadic) == 0 && t.MinLength > s.MinLength) ||
            ((t.CombinedFlags & ElementFlags.Variable) == 0 && ((s.CombinedFlags & ElementFlags.Variable) != 0 || t.FixedLength < s.FixedLength));
    }

    /// <summary>
    /// checker.ts typesDefinitelyUnrelated: two tuple types with incompatible arities, or two object types that each
    /// have a property that is unmatched in the other.
    /// </summary>
    internal bool TypesDefinitelyUnrelated(Type source, Type target) =>
        IsTupleType(source) && IsTupleType(target) ? TupleTypesDefinitelyUnrelated(source, target) :
        !GetUnmatchedProperty(source, target, false, true).IsNone && !GetUnmatchedProperty(target, source, false, false).IsNone;

    /// <summary>
    /// checker.ts isObjectTypeWithInferableIndex: a type inferred from an object literal, written as an object type
    /// literal, or the shape of a module with no call or construct signatures.
    /// </summary>
    internal bool IsObjectTypeWithInferableIndex(Type type)
    {
        var objectFlags = Types.ObjectFlagsOf(type);
        if ((Types.Flags(type) & TypeFlags.Intersection) != 0)
        {
            return AllTypes(Types.Elements(Types.Union(type).Types), IsObjectTypeWithInferableIndex);
        }
        var symbol = Types.Symbol(type);
        return (!symbol.IsNone && (SymbolFlagsOf(symbol) & (SymbolFlags.ObjectLiteral | SymbolFlags.TypeLiteral | SymbolFlags.Enum | SymbolFlags.ValueModule)) != 0 &&
                (SymbolFlagsOf(symbol) & SymbolFlags.Class) == 0 && !TypeHasCallOrConstructSignatures(type)) ||
            (objectFlags & ObjectFlags.ObjectRestType) != 0 ||
            ((objectFlags & ObjectFlags.ReverseMapped) != 0 && IsObjectTypeWithInferableIndex(ReverseMappedSource(type)));
    }

    /// <summary>utilities.ts getSelectedEffectiveModifierFlags (JSDoc modifiers: C9).</summary>
    internal ModifierFlags GetSelectedEffectiveModifierFlags(NodeRef node, ModifierFlags flags) =>
        Utilities.GetSyntacticModifierFlagsNoCache(StoreOf(node), node.Node) & flags;

    // getEnumMemberValue(declaration).value: the value of the member's enum literal type (null when computed)
    private object? GetEnumMemberValueOfDeclaration(NodeRef declaration)
    {
        if (declaration.IsNone)
        {
            return null;
        }
        var memberSymbol = GetSymbolOfDeclaration(declaration);
        var memberType = GetDeclaredTypeOfSymbol(memberSymbol);
        return (Types.Flags(memberType) & TypeFlags.Literal) != 0 ? Types.Literal(memberType).Value : null;
    }

    // ---- template literal and string mapping types ----

    /// <summary>checker.ts templateLiteralTypesDefinitelyUnrelated: differences in their starting or ending text spans.</summary>
    internal bool TemplateLiteralTypesDefinitelyUnrelated(Type source, Type target)
    {
        var sourceTexts = Types.Data<TemplateLiteralData>(source).Texts;
        var targetTexts = Types.Data<TemplateLiteralData>(target).Texts;
        var sourceStart = sourceTexts[0];
        var targetStart = targetTexts[0];
        var sourceEnd = sourceTexts[^1];
        var targetEnd = targetTexts[^1];
        var startLen = Math.Min(sourceStart.Length, targetStart.Length);
        var endLen = Math.Min(sourceEnd.Length, targetEnd.Length);
        return !string.Equals(sourceStart[..startLen], targetStart[..startLen], StringComparison.Ordinal) ||
            !string.Equals(sourceEnd[(sourceEnd.Length - endLen)..], targetEnd[(targetEnd.Length - endLen)..], StringComparison.Ordinal);
    }

    // checker.ts isValidNumberString: whether the string can be parsed as a number (roundTripOnly: converted back, the
    // number matches the input)
    private static bool IsValidNumberString(string s, bool roundTripOnly)
    {
        if (s.Length == 0)
        {
            return false;
        }
        var n = JsNumber.FromString(s);
        return double.IsFinite(n) && (!roundTripOnly || JsNumber.ToString(n) == s);
    }

    // utilities.ts isValidBigIntString: whether `s + "n"` scans as a single bigint literal without errors and without
    // numeric separators (an optional leading '-' allowed); roundTripOnly: the string is the bigint's decimal form.
    private static bool IsValidBigIntString(string s, bool roundTripOnly)
    {
        if (s.Length == 0)
        {
            return false;
        }
        var negative = s[0] == '-';
        var digits = negative ? s[1..] : s;
        if (digits.Length == 0)
        {
            return false;
        }
        bool valid;
        var isDecimal = false;
        if (digits.Length > 2 && digits[0] == '0' && (digits[1] is 'x' or 'X'))
        {
            valid = AllChars(digits, 2, char.IsAsciiHexDigit);
        }
        else if (digits.Length > 2 && digits[0] == '0' && (digits[1] is 'o' or 'O'))
        {
            valid = AllChars(digits, 2, ch => ch is >= '0' and <= '7');
        }
        else if (digits.Length > 2 && digits[0] == '0' && (digits[1] is 'b' or 'B'))
        {
            valid = AllChars(digits, 2, ch => ch is '0' or '1');
        }
        else
        {
            // decimal: a leading zero makes the scanner report an octal-like literal
            isDecimal = true;
            valid = AllChars(digits, 0, char.IsAsciiDigit) && (digits.Length == 1 || digits[0] != '0');
        }
        if (!valid)
        {
            return false;
        }
        // pseudoBigIntToString({ negative, base10Value }) === s
        return !roundTripOnly || (isDecimal && !(negative && digits == "0"));
    }

    private static bool AllChars(string text, int start, Func<char, bool> predicate)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (!predicate(text[i]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>checker.ts isMemberOfStringMapping.</summary>
    internal bool IsMemberOfStringMapping(Type source, Type target)
    {
        if ((Types.Flags(target) & TypeFlags.Any) != 0)
        {
            return true;
        }
        if ((Types.Flags(target) & (TypeFlags.String | TypeFlags.TemplateLiteral)) != 0)
        {
            return IsTypeAssignableTo(source, target);
        }
        if ((Types.Flags(target) & TypeFlags.StringMapping) != 0)
        {
            // We need to see whether applying the same mappings of the target
            // onto the source would produce an identical type *and* that
            // it's compatible with the inner-most non-string-mapped type.
            //
            // The intuition here is that if same mappings don't affect the source at all,
            // and the source is compatible with the unmapped target, then they must
            // still reside in the same domain.
            var mappingStack = new List<SymbolRef>();
            while ((Types.Flags(target) & TypeFlags.StringMapping) != 0)
            {
                mappingStack.Insert(0, Types.Symbol(target));
                target = Types.Data<StringMappingData>(target).Type;
            }
            var mappedSource = source;
            foreach (var symbol in mappingStack)
            {
                mappedSource = GetStringMappingType(symbol, mappedSource);
            }
            return mappedSource == source && IsMemberOfStringMapping(source, target);
        }
        return false;
    }

    // checker.ts isValidTypeForTemplateLiteralPlaceholder
    private bool IsValidTypeForTemplateLiteralPlaceholder(Type source, Type target)
    {
        var targetFlags = Types.Flags(target);
        if ((targetFlags & TypeFlags.Intersection) != 0)
        {
            return AllTypes(Types.Elements(Types.Union(target).Types), t => t == emptyTypeLiteralType || IsValidTypeForTemplateLiteralPlaceholder(source, t));
        }
        if ((targetFlags & TypeFlags.String) != 0 || IsTypeAssignableTo(source, target))
        {
            return true;
        }
        if ((Types.Flags(source) & TypeFlags.StringLiteral) != 0)
        {
            var value = (string)Types.Literal(source).Value!;
            return ((targetFlags & TypeFlags.Number) != 0 && IsValidNumberString(value, false)) ||
                ((targetFlags & TypeFlags.BigInt) != 0 && IsValidBigIntString(value, false)) ||
                ((targetFlags & (TypeFlags.BooleanLiteral | TypeFlags.Nullable)) != 0 && value == IntrinsicNameOf(target)) ||
                ((targetFlags & TypeFlags.StringMapping) != 0 && IsMemberOfStringMapping(source, target)) ||
                ((targetFlags & TypeFlags.TemplateLiteral) != 0 && IsTypeMatchedByTemplateLiteralType(source, target));
        }
        if ((Types.Flags(source) & TypeFlags.TemplateLiteral) != 0)
        {
            var texts = Types.Data<TemplateLiteralData>(source).Texts;
            return texts.Length == 2 && texts[0].Length == 0 && texts[1].Length == 0 && IsTypeAssignableTo(Types.Data<TemplateLiteralData>(source).Types[0], target);
        }
        return false;
    }

    // checker.ts inferTypesFromTemplateLiteralType
    private Type[]? InferTypesFromTemplateLiteralType(Type source, Type target)
    {
        var targetData = Types.Data<TemplateLiteralData>(target);
        if ((Types.Flags(source) & TypeFlags.StringLiteral) != 0)
        {
            return InferFromLiteralPartsToTemplateLiteral(null, (string)Types.Literal(source).Value!, [], target);
        }
        if ((Types.Flags(source) & TypeFlags.TemplateLiteral) != 0)
        {
            var sourceData = Types.Data<TemplateLiteralData>(source);
            if (sourceData.Texts.AsSpan().SequenceEqual(targetData.Texts))
            {
                var result = new Type[sourceData.Types.Length];
                for (var i = 0; i < result.Length; i++)
                {
                    var s = sourceData.Types[i];
                    result[i] = IsTypeAssignableTo(GetBaseConstraintOrType(s), GetBaseConstraintOrType(targetData.Types[i])) ? s : GetStringLikeTypeForType(s);
                }
                return result;
            }
            return InferFromLiteralPartsToTemplateLiteral(sourceData.Texts, "", sourceData.Types, target);
        }
        return null;
    }

    /// <summary>checker.ts isTypeMatchedByTemplateLiteralType.</summary>
    internal bool IsTypeMatchedByTemplateLiteralType(Type source, Type target)
    {
        var inferences = InferTypesFromTemplateLiteralType(source, target);
        if (inferences is null)
        {
            return false;
        }
        var targetTypes = Types.Data<TemplateLiteralData>(target).Types;
        for (var i = 0; i < inferences.Length; i++)
        {
            if (!IsValidTypeForTemplateLiteralPlaceholder(inferences[i], targetTypes[i]))
            {
                return false;
            }
        }
        return true;
    }

    // checker.ts getStringLikeTypeForType
    private Type GetStringLikeTypeForType(Type type) =>
        (Types.Flags(type) & (TypeFlags.Any | TypeFlags.StringLike)) != 0 ? type : GetTemplateLiteralType(["", ""], [type]);

    // checker.ts inferFromLiteralPartsToTemplateLiteral: infers from the text parts and type parts of a source literal
    // to a target template literal; the inferred string or template literal types for the target's placeholders, or
    // null if the source doesn't match the target.
    // (sourceTexts null: the one text singleText, the texts of a string literal type, without an array)
    private Type[]? InferFromLiteralPartsToTemplateLiteral(string[]? sourceTexts, string singleText, Type[] sourceTypes, Type target)
    {
        var sourceTextCount = sourceTexts?.Length ?? 1;
        var lastSourceIndex = sourceTextCount - 1;
        var sourceStartText = SourceTextAt(0);
        var sourceEndText = SourceTextAt(lastSourceIndex);
        var targetTexts = Types.Data<TemplateLiteralData>(target).Texts;
        var lastTargetIndex = targetTexts.Length - 1;
        var targetStartText = targetTexts[0];
        var targetEndText = targetTexts[lastTargetIndex];
        if ((lastSourceIndex == 0 && sourceStartText.Length < targetStartText.Length + targetEndText.Length) ||
            !sourceStartText.StartsWith(targetStartText, StringComparison.Ordinal) || !sourceEndText.EndsWith(targetEndText, StringComparison.Ordinal))
        {
            return null;
        }
        var remainingEndText = sourceEndText[..(sourceEndText.Length - targetEndText.Length)];
        // (one match per placeholder of the target: lastTargetIndex)
        var matches = new Type[lastTargetIndex];
        var matchCount = 0;
        var seg = 0;
        var pos = targetStartText.Length;
        for (var i = 1; i < lastTargetIndex; i++)
        {
            var delim = targetTexts[i];
            if (delim.Length > 0)
            {
                var s = seg;
                var p = pos;
                while (true)
                {
                    p = GetSourceText(s).IndexOf(delim, p, StringComparison.Ordinal);
                    if (p >= 0)
                    {
                        break;
                    }
                    s++;
                    if (s == sourceTextCount)
                    {
                        return null;
                    }
                    p = 0;
                }
                AddMatch(s, p);
                pos += delim.Length;
            }
            else if (pos < GetSourceText(seg).Length)
            {
                AddMatch(seg, pos + 1);
            }
            else if (seg < lastSourceIndex)
            {
                AddMatch(seg + 1, 0);
            }
            else
            {
                return null;
            }
        }
        AddMatch(lastSourceIndex, GetSourceText(lastSourceIndex).Length);
        return matches;

        string SourceTextAt(int index) => sourceTexts is null ? singleText : sourceTexts[index];

        string GetSourceText(int index) => index < lastSourceIndex ? SourceTextAt(index) : remainingEndText;

        void AddMatch(int s, int p)
        {
            Type matchType;
            if (s == seg)
            {
                matchType = GetStringLiteralType(SliceText(GetSourceText(s), pos, p));
            }
            else
            {
                var texts = new List<string> { SliceText(SourceTextAt(seg), pos, SourceTextAt(seg).Length) };
                for (var k = seg + 1; k < s; k++)
                {
                    texts.Add(SourceTextAt(k));
                }
                texts.Add(SliceText(GetSourceText(s), 0, p));
                matchType = GetTemplateLiteralType([.. texts], sourceTypes[seg..s]);
            }
            matches[matchCount++] = matchType;
            seg = s;
            pos = p;
        }
    }

    // ---- substitution types of conditional type branches ----

    /// <summary>
    /// checker.ts getConditionalFlowTypeOfType: in the true branch of a conditional type, a type variable that is the
    /// check type gets the extends type as an implied constraint (a substitution type); in the template of a
    /// homomorphic mapped type over an array or tuple constraint, the key type parameter gets number | `${number}`.
    /// </summary>
    private Type GetConditionalFlowTypeOfType(Type type, NodeRef node)
    {
        List<Type>? constraints = null;
        var covariant = true;
        var store = StoreOf(node);
        var n = node.Node;
        while (!n.IsNone && !IsStatementNode(store, n) && store.Kind(n) != SyntaxKind.JSDoc)
        {
            var parent = store.Parent(n);
            if (parent.IsNone)
            {
                break;
            }
            // only consider variance flipped by parameter locations - `keyof` types would usually be considered variance inverting, but
            // often get used in indexed accesses where they behave sortof invariantly, but our checking is lax
            if (store.Kind(parent) == SyntaxKind.Parameter)
            {
                covariant = !covariant;
            }
            // Always substitute on type parameters, regardless of variance, since even
            // in contravariant positions, they may rely on substituted constraints to be valid
            if ((covariant || (Types.Flags(type) & TypeFlags.TypeVariable) != 0) && store.Kind(parent) == SyntaxKind.ConditionalType && n == store.TrueType(parent))
            {
                var constraint = GetImpliedConstraint(type, At(node, store.CheckType(parent)), At(node, store.ExtendsType(parent)));
                if (!constraint.IsNone)
                {
                    (constraints ??= []).Add(constraint);
                }
            }
            // Given a homomorphic mapped type { [K in keyof T]: XXX }, where T is constrained to an array or tuple type, in the
            // template type XXX, K has an added constraint of number | `${number}`.
            else if ((Types.Flags(type) & TypeFlags.TypeParameter) != 0 && store.Kind(parent) == SyntaxKind.MappedType && store.NameType(parent).IsNone && n == store.Type(parent))
            {
                var mappedType = GetTypeFromTypeNode(At(node, parent));
                if (GetTypeParameterFromMappedType(mappedType) == GetActualTypeVariable(type))
                {
                    var typeParameter = GetHomomorphicTypeVariable(mappedType);
                    if (!typeParameter.IsNone)
                    {
                        var constraint = GetConstraintOfTypeParameter(typeParameter);
                        if (!constraint.IsNone && EveryType(constraint, IsArrayOrTupleType))
                        {
                            (constraints ??= []).Add(GetUnionType([numberType, numericStringType]));
                        }
                    }
                }
            }
            n = parent;
        }
        return constraints is not null ? GetSubstitutionType(type, GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(constraints))) : type;
    }

    // checker.ts getImpliedConstraint (None when there is none)
    private Type GetImpliedConstraint(Type type, NodeRef checkNode, NodeRef extendsNode)
    {
        if (IsUnaryTupleTypeNode(checkNode) && IsUnaryTupleTypeNode(extendsNode))
        {
            return GetImpliedConstraint(type, At(checkNode, StoreOf(checkNode).Nodes(StoreOf(checkNode).Elements(checkNode.Node))[0]),
                At(extendsNode, StoreOf(extendsNode).Nodes(StoreOf(extendsNode).Elements(extendsNode.Node))[0]));
        }
        return GetActualTypeVariable(GetTypeFromTypeNode(checkNode)) == GetActualTypeVariable(type) ? GetTypeFromTypeNode(extendsNode) : Type.None;
    }

    // checker.ts isUnaryTupleTypeNode
    private bool IsUnaryTupleTypeNode(NodeRef node) =>
        Kind(node) == SyntaxKind.TupleType && StoreOf(node).Length(StoreOf(node).Elements(node.Node)) == 1;

    // utilitiesPublic.ts isStatement (isStatementKindButNotDeclarationKind, isDeclarationStatementKind, isBlockStatement)
    private static bool IsStatementNode(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.BreakStatement:
            case SyntaxKind.ContinueStatement:
            case SyntaxKind.DebuggerStatement:
            case SyntaxKind.DoStatement:
            case SyntaxKind.ExpressionStatement:
            case SyntaxKind.EmptyStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
            case SyntaxKind.ForStatement:
            case SyntaxKind.IfStatement:
            case SyntaxKind.LabeledStatement:
            case SyntaxKind.ReturnStatement:
            case SyntaxKind.SwitchStatement:
            case SyntaxKind.ThrowStatement:
            case SyntaxKind.TryStatement:
            case SyntaxKind.VariableStatement:
            case SyntaxKind.WhileStatement:
            case SyntaxKind.WithStatement:
            case SyntaxKind.NotEmittedStatement:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.MissingDeclaration:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.ExportDeclaration:
            case SyntaxKind.ExportAssignment:
            case SyntaxKind.NamespaceExportDeclaration:
                return true;
            case SyntaxKind.Block:
            {
                // isBlockStatement: not the block of a try statement or catch clause, not a function body
                var parent = store.Parent(node);
                if (!parent.IsNone && store.Kind(parent) is SyntaxKind.TryStatement or SyntaxKind.CatchClause)
                {
                    return false;
                }
                return parent.IsNone || !Utilities.IsFunctionLike(store, parent);
            }
        }
        return false;
    }

    // String.prototype.slice(start, end) for indices within the string
    private static string SliceText(string text, int start, int end)
    {
        start = Math.Min(start, text.Length);
        end = Math.Min(end, text.Length);
        return end > start ? text[start..end] : "";
    }
}
