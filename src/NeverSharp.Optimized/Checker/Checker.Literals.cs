// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (module C5; the functions are listed below)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (details below).
// Functions (checkArrayLiteral, createArrayLiteralType, checkObjectLiteral,
// checkComputedPropertyName, getObjectLiteralIndexInfo, isValidSpreadType, checkSpreadPropOverrides,
// tryMergeUnionOfObjectTypeAndEmptyObject, getSpreadType, isSpreadableProperty, getSpreadSymbol,
// getIndexInfoWithReadonly, checkGrammarObjectLiteralExpression, getRegularTypeOfObjectLiteral, transformTypeOfMembers,
// the widening of object and array literals (getWidenedType, getWidenedTypeWithContext, getWidenedTypeOfObjectLiteral,
// getWidenedProperty, getUndefinedProperty, reportWideningErrorsInType, reportErrorsFromWidening), isEmptyLiteralType,
// isEmptyArrayLiteralType, isTupleLikeType, isMutableArrayLikeType)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (replaces C1's object and array literal subsets and C1's
// getWidenedType). Module C9d: JavaScript object literals with JSDoc types and enum tags.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>checker.ts WideningKind.</summary>
internal enum WideningKind
{
    Normal,
    FunctionReturn,
    GeneratorNext,
    GeneratorYield,
}

internal sealed partial class Checker
{
    private readonly Dictionary<Type, Type> arrayLiteralTypes = [];
    private readonly Dictionary<Type, Type> widenedTypes = [];
    private readonly Dictionary<string, SymbolRef> undefinedProperties = new(StringComparer.Ordinal);

    // checker.ts checkArrayLiteral
    private Type CheckArrayLiteral(NodeRef node, CheckMode checkMode, bool forceTuple)
    {
        var store = StoreOf(node);
        var elements = store.Nodes(store.Elements(node.Node));
        var elementCount = elements.Length;
        var elementTypes = new List<Type>();
        var elementFlags = new List<ElementFlags>();
        PushCachedContextualType(node);
        var inDestructuringPattern = Utilities.IsAssignmentTarget(store, node.Node);
        var inConstContext = IsConstContext(node);
        var contextualType = GetApparentTypeOfContextualType(node, ContextFlags.None);
        var inTupleContext = IsSpreadIntoCallOrNew(node) || (!contextualType.IsNone && SomeType(contextualType, t =>
            IsTupleLikeType(t) || (IsGenericMappedType(t) && Types.Object(t).NameType.IsNone && !GetHomomorphicTypeVariable(Types.Object(t).Target.IsNone ? t : Types.Object(t).Target).IsNone)));
        var hasOmittedExpression = false;
        for (var i = 0; i < elementCount; i++)
        {
            var e = At(node, elements[i]);
            if (store.Kind(e.Node) == SyntaxKind.SpreadElement)
            {
                var spreadExpression = At(node, store.Expression(e.Node));
                var spreadType = CheckExpression(spreadExpression, checkMode, forceTuple);
                if (IsArrayLikeType(spreadType))
                {
                    elementTypes.Add(spreadType);
                    elementFlags.Add(ElementFlags.Variadic);
                }
                else if (inDestructuringPattern)
                {
                    // Given the following situation:
                    //    var c: {};
                    //    [...c] = ["", 0];
                    //
                    // c is represented in the tree as a spread element in an array literal.
                    // But c really functions as a rest element, and its purpose is to provide
                    // a contextual type for the right hand side of the assignment. Therefore,
                    // instead of calling checkExpression on "...c", which will give an error
                    // if c is not iterable/array-like, we need to act as if we are trying to
                    // get the contextual element type from it. So we do something similar to
                    // getContextualTypeForElementExpression, which will crucially not error
                    // if there is no index type / iterated type.
                    var restElementType = GetIndexTypeOfType(spreadType, numberType);
                    if (restElementType.IsNone)
                    {
                        restElementType = GetIteratedTypeOrElementType(IterationUse.Destructuring, spreadType, undefinedType, NodeRef.None, checkAssignability: false);
                    }
                    elementTypes.Add(restElementType.IsNone ? unknownType : restElementType);
                    elementFlags.Add(ElementFlags.Rest);
                }
                else
                {
                    elementTypes.Add(CheckIteratedTypeOrElementType(IterationUse.Spread, spreadType, undefinedType, spreadExpression));
                    elementFlags.Add(ElementFlags.Rest);
                }
            }
            else if (exactOptionalPropertyTypes && store.Kind(e.Node) == SyntaxKind.OmittedExpression)
            {
                hasOmittedExpression = true;
                elementTypes.Add(undefinedOrMissingType);
                elementFlags.Add(ElementFlags.Optional);
            }
            else
            {
                var type = CheckExpressionForMutableLocation(e, checkMode, forceTuple);
                elementTypes.Add(AddOptionality(type, isProperty: true, hasOmittedExpression));
                elementFlags.Add(hasOmittedExpression ? ElementFlags.Optional : ElementFlags.Required);
                if (inTupleContext && (checkMode & CheckMode.Inferential) != 0 && (checkMode & CheckMode.SkipContextSensitive) == 0 && IsContextSensitive(e))
                {
                    var inferenceContext = GetInferenceContext(node);
                    if (inferenceContext is not null)
                    {
                        AddIntraExpressionInferenceSite(inferenceContext, e, type);
                    }
                }
            }
        }
        PopContextualType();
        if (inDestructuringPattern)
        {
            return CreateTupleType([.. elementTypes], [.. elementFlags]);
        }
        if (forceTuple || inConstContext || inTupleContext)
        {
            return CreateArrayLiteralType(CreateTupleType([.. elementTypes], [.. elementFlags], inConstContext && !(!contextualType.IsNone && SomeType(contextualType, IsMutableArrayLikeType))));
        }
        Type elementType;
        if (elementTypes.Count > 0)
        {
            var mapped = new Type[elementTypes.Count];
            for (var i = 0; i < mapped.Length; i++)
            {
                if ((elementFlags[i] & ElementFlags.Variadic) != 0)
                {
                    var indexed = GetIndexedAccessTypeOrUndefined(elementTypes[i], numberType);
                    mapped[i] = indexed.IsNone ? anyType : indexed;
                }
                else
                {
                    mapped[i] = elementTypes[i];
                }
            }
            elementType = GetUnionType(mapped, UnionReduction.Subtype);
        }
        else
        {
            elementType = strictNullChecks ? implicitNeverType : undefinedWideningType;
        }
        return CreateArrayLiteralType(CreateArrayType(elementType, inConstContext));
    }

    // checker.ts createArrayLiteralType (the literal type is a clone of the reference with the ArrayLiteral flags)
    private Type CreateArrayLiteralType(Type type)
    {
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.Reference) == 0)
        {
            return type;
        }
        if (!arrayLiteralTypes.TryGetValue(type, out var literalType))
        {
            // cloneTypeReference
            literalType = CreateObjectType(Types.ObjectFlagsOf(type) | ObjectFlags.ArrayLiteral | ObjectFlags.ContainsObjectOrArrayLiteral, Types.Symbol(type));
            Types.Record(literalType).ObjectFlags = Types.ObjectFlagsOf(type) | ObjectFlags.ArrayLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
            var data = Types.Object(literalType);
            var source = Types.Object(type);
            data.Target = source.Target;
            data.ResolvedTypeArguments = Types.CreateList(GetTypeArguments(type));
            data.HasResolvedTypeArguments = true;
            data.Node = source.Node;
            data.Mapper = source.Mapper;
            arrayLiteralTypes[type] = literalType;
        }
        return literalType;
    }

    // checker.ts isSpreadIntoCallOrNew
    private bool IsSpreadIntoCallOrNew(NodeRef node)
    {
        var store = StoreOf(node);
        var parent = WalkUpParenthesizedExpressions(store, store.Parent(node.Node));
        return store.Kind(parent) == SyntaxKind.SpreadElement && store.Kind(store.Parent(parent)) is SyntaxKind.CallExpression or SyntaxKind.NewExpression;
    }

    /// <summary>checker.ts isMutableArrayLikeType.</summary>
    internal bool IsMutableArrayLikeType(Type type) =>
        // A type is mutable-array-like if it is a reference to the global Array type, or if it is not the
        // any, undefined or null type and if it is assignable to Array<any>
        IsMutableArrayOrTuple(type) || ((Types.Flags(type) & (TypeFlags.Any | TypeFlags.Nullable)) == 0 && IsTypeAssignableTo(type, anyArrayType));

    // ---- object literals ----

    /// <summary>checker.ts checkComputedPropertyName.</summary>
    internal Type CheckComputedPropertyName(NodeRef node)
    {
        var store = StoreOf(node);
        var expression = At(node, store.Expression(node.Node));
        if (ExpressionLinks.Has(expression) && !ExpressionLinks.Get(expression).ResolvedType.IsNone)
        {
            return ExpressionLinks.Get(expression).ResolvedType;
        }
        var parent = store.Parent(node.Node);
        var grandParent = store.Parent(parent);
        if ((store.Kind(grandParent) is SyntaxKind.TypeLiteral or SyntaxKind.InterfaceDeclaration || Utilities.IsClassLike(store, grandParent)) &&
            store.Kind(expression.Node) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(expression.Node)) == SyntaxKind.InKeyword &&
            store.Kind(parent) is not (SyntaxKind.GetAccessor or SyntaxKind.SetAccessor))
        {
            ExpressionLinks.Get(expression).ResolvedType = errorType;
            return errorType;
        }
        var type = CheckExpression(expression);
        ExpressionLinks.Get(expression).ResolvedType = type;
        // (the block-scoped binding flags of computed names in loops: emit details)
        // This will allow types number, string, symbol or any. It will also allow enums, the unknown
        // type, and any union of these types (like string | number).
        if ((Types.Flags(type) & TypeFlags.Nullable) != 0 ||
            (!IsTypeAssignableToKind(type, TypeFlags.StringLike | TypeFlags.NumberLike | TypeFlags.ESSymbolLike) && !IsTypeAssignableTo(type, stringNumberSymbolType)))
        {
            Error(node, Diagnostics.A_computed_property_name_must_be_of_type_string_number_symbol_or_any);
        }
        return type;
    }

    // checker.ts isSymbolWithNumericName
    private bool IsSymbolWithNumericName(SymbolRef symbol)
    {
        if (IsNumericLiteralName(NameOf(symbol)))
        {
            return true;
        }
        var declarations = DeclarationsOf(symbol);
        var firstDecl = declarations is { Count: > 0 } ? declarations[0] : NodeRef.None;
        if (firstDecl.IsNone)
        {
            return false;
        }
        var name = Utilities.GetNameOfDeclaration(StoreOf(firstDecl), firstDecl.Node);
        return !name.IsNone && IsNumericName(At(firstDecl, name));
    }

    // checker.ts isNumericName
    private bool IsNumericName(NodeRef name)
    {
        var store = StoreOf(name);
        return store.Kind(name.Node) switch
        {
            SyntaxKind.ComputedPropertyName => IsTypeAssignableToKind(CheckComputedPropertyName(name), TypeFlags.NumberLike),
            SyntaxKind.Identifier => IsNumericLiteralName(store.Texts.GetString(store.EscapedText(name.Node))!),
            SyntaxKind.NumericLiteral or SyntaxKind.StringLiteral => IsNumericLiteralName(store.Texts.GetString(store.Text(name.Node))!),
            _ => false,
        };
    }

    // checker.ts isSymbolWithSymbolName
    private bool IsSymbolWithSymbolName(SymbolRef symbol)
    {
        if (IsKnownSymbol(symbol))
        {
            return true;
        }
        var declarations = DeclarationsOf(symbol);
        var firstDecl = declarations is { Count: > 0 } ? declarations[0] : NodeRef.None;
        if (firstDecl.IsNone)
        {
            return false;
        }
        var name = Utilities.GetNameOfDeclaration(StoreOf(firstDecl), firstDecl.Node);
        return !name.IsNone && StoreOf(firstDecl).Kind(name) == SyntaxKind.ComputedPropertyName &&
            IsTypeAssignableToKind(CheckComputedPropertyName(At(firstDecl, name)), TypeFlags.ESSymbol);
    }

    // checker.ts isSymbolWithComputedName
    private bool IsSymbolWithComputedName(SymbolRef symbol)
    {
        var declarations = DeclarationsOf(symbol);
        var firstDecl = declarations is { Count: > 0 } ? declarations[0] : NodeRef.None;
        if (firstDecl.IsNone)
        {
            return false;
        }
        var name = Utilities.GetNameOfDeclaration(StoreOf(firstDecl), firstDecl.Node);
        return !name.IsNone && StoreOf(firstDecl).Kind(name) == SyntaxKind.ComputedPropertyName;
    }

    // checker.ts getObjectLiteralIndexInfo
    private IndexInfo GetObjectLiteralIndexInfo(bool isReadonly, int offset, List<SymbolRef> properties, Type keyType)
    {
        var propTypes = new List<Type>();
        List<NodeRef>? components = null;
        for (var i = offset; i < properties.Count; i++)
        {
            var prop = properties[i];
            if ((keyType == stringType && !IsSymbolWithSymbolName(prop)) ||
                (keyType == numberType && IsSymbolWithNumericName(prop)) ||
                (keyType == esSymbolType && IsSymbolWithSymbolName(prop)))
            {
                propTypes.Add(GetTypeOfSymbol(properties[i]));
                if (IsSymbolWithComputedName(properties[i]))
                {
                    (components ??= []).Add(DeclarationsOf(properties[i])![0]);
                }
            }
        }
        var unionType = propTypes.Count > 0 ? GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(propTypes), UnionReduction.Subtype) : undefinedType;
        return new IndexInfo { KeyType = keyType, Type = unionType, IsReadonly = isReadonly, Components = components };
    }

    // checker.ts checkObjectLiteral (JavaScript object literals: C9)
    private Type CheckObjectLiteral(NodeRef node, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var inDestructuringPattern = Utilities.IsAssignmentTarget(store, n);
        // Grammar checking
        CheckGrammarObjectLiteralExpression(node, inDestructuringPattern);
        var allPropertiesTable = strictNullChecks ? new SymTable() : null;
        var propertiesTable = new SymTable();
        var propertiesArray = new List<SymbolRef>();
        var spread = emptyObjectType;
        PushCachedContextualType(node);
        var contextualType = GetApparentTypeOfContextualType(node, ContextFlags.None);
        var contextualTypeHasPattern = !contextualType.IsNone && typePatterns.TryGetValue(contextualType, out var contextualPattern) &&
            StoreOf(contextualPattern).Kind(contextualPattern.Node) is SyntaxKind.ObjectBindingPattern or SyntaxKind.ObjectLiteralExpression;
        var inConstContext = IsConstContext(node);
        var checkFlags = inConstContext ? CheckFlags.Readonly : CheckFlags.None;
        var isInJavascript = Utilities.IsInJSFile(store, n) && !store.IsDeclarationFile(Utilities.RootOf(store, n)) && !IsInJsonFile(node);
        var enumTag = isInJavascript ? GetFirstJSDocTagOfKind(store, n, SyntaxKind.JSDocEnumTag) : Node.None;
        var isJSObjectLiteral = contextualType.IsNone && isInJavascript && enumTag.IsNone;
        var objectFlags = ObjectFlags.FreshLiteral;
        var patternWithComputedProperties = false;
        var hasComputedStringProperty = false;
        var hasComputedNumberProperty = false;
        var hasComputedSymbolProperty = false;
        var properties = store.Nodes(store.Properties(n));
        // Spreads may cause an early bail; ensure computed names are always checked (this is cached)
        // As otherwise they may not be checked until exports for the type at this position are retrieved,
        // which may never occur.
        foreach (var elem in properties)
        {
            var elemName = store.Kind(elem) == SyntaxKind.SpreadAssignment ? Node.None : store.Name(elem);
            if (!elemName.IsNone && store.Kind(elemName) == SyntaxKind.ComputedPropertyName)
            {
                CheckComputedPropertyName(At(node, elemName));
            }
        }
        var offset = 0;
        foreach (var memberNode in properties)
        {
            var memberDecl = At(node, memberNode);
            var memberKind = store.Kind(memberNode);
            var member = GetSymbolOfDeclaration(memberDecl);
            var memberName = memberKind == SyntaxKind.SpreadAssignment ? Node.None : store.Name(memberNode);
            var computedNameType = !memberName.IsNone && store.Kind(memberName) == SyntaxKind.ComputedPropertyName ? CheckComputedPropertyName(At(node, memberName)) : Type.None;
            if (memberKind is SyntaxKind.PropertyAssignment or SyntaxKind.ShorthandPropertyAssignment || Utilities.IsObjectLiteralMethod(store, memberNode))
            {
                Type type;
                if (memberKind == SyntaxKind.PropertyAssignment)
                {
                    type = CheckPropertyAssignment(memberDecl, checkMode);
                }
                else if (memberKind == SyntaxKind.ShorthandPropertyAssignment)
                {
                    // avoid resolving the left side of the ShorthandPropertyAssignment outside of the destructuring
                    // for error recovery purposes. For example, if a user wrote `{ a = 100 }` instead of `{ a: 100 }`.
                    // we don't want to say "could not find 'a'".
                    var objectAssignmentInitializer = store.ObjectAssignmentInitializer(memberNode);
                    type = CheckExpressionForMutableLocation(At(node, !inDestructuringPattern && !objectAssignmentInitializer.IsNone ? objectAssignmentInitializer : store.Name(memberNode)), checkMode);
                }
                else
                {
                    type = CheckObjectLiteralMethod(memberDecl, checkMode);
                }
                if (isInJavascript)
                {
                    var jsDocType = GetTypeForDeclarationFromJSDocComment(memberDecl);
                    if (!jsDocType.IsNone)
                    {
                        CheckTypeAssignableTo(type, jsDocType, memberDecl);
                        type = jsDocType;
                    }
                    else if (!enumTag.IsNone && !store.TypeExpression(enumTag).IsNone)
                    {
                        CheckTypeAssignableTo(type, GetTypeFromTypeNode(At(node, store.TypeExpression(enumTag))), memberDecl);
                    }
                }
                objectFlags |= Types.ObjectFlagsOf(type) & ObjectFlags.PropagatingFlags;
                var nameType = !computedNameType.IsNone && IsTypeUsableAsPropertyName(computedNameType) ? computedNameType : Type.None;
                var memberFlags = SymbolFlagsOf(member);
                var prop = !nameType.IsNone
                    ? CreateSymbol(SymbolFlags.Property | memberFlags, GetPropertyNameFromType(nameType), checkFlags | CheckFlags.Late)
                    : CreateSymbol(SymbolFlags.Property | memberFlags, NameOf(member), checkFlags);
                if (!nameType.IsNone)
                {
                    ValueSymbolLinks.Get(prop).NameType = nameType;
                }
                if (inDestructuringPattern && HasDefaultValue(store, memberNode))
                {
                    // If object literal is an assignment pattern and if the assignment pattern specifies a default value
                    // for the property, make the property optional.
                    TransientSymbols.Record(prop).Flags |= SymbolFlags.Optional;
                }
                else if (contextualTypeHasPattern && (Types.ObjectFlagsOf(contextualType) & ObjectFlags.ObjectLiteralPatternWithComputedProperties) == 0)
                {
                    // If object literal is contextually typed by the implied type of a binding pattern, and if the
                    // binding pattern specifies a default value for the property, make the property optional.
                    var impliedProp = GetPropertyOfType(contextualType, NameOf(member));
                    if (!impliedProp.IsNone)
                    {
                        TransientSymbols.Record(prop).Flags |= SymbolFlagsOf(impliedProp) & SymbolFlags.Optional;
                    }
                    else if (GetIndexInfoOfType(contextualType, stringType) is null)
                    {
                        Error(At(node, memberName), Diagnostics.Object_literal_may_only_specify_known_properties_and_0_does_not_exist_in_type_1, SymbolToString(member), TypeToString(contextualType));
                    }
                }
                ref var propRecord = ref TransientSymbols.Record(prop);
                propRecord.Declarations = DeclarationsOf(member);
                propRecord.Parent = ParentOf(member);
                var valueDeclaration = ValueDeclarationOf(member);
                if (!valueDeclaration.IsNone)
                {
                    propRecord.ValueDeclaration = valueDeclaration;
                }
                ref var propLinks = ref ValueSymbolLinks.Get(prop);
                propLinks.ResolvedType = type;
                propLinks.Target = member;
                member = prop;
                allPropertiesTable?.Set(NameOf(prop), prop);
                if (!contextualType.IsNone && (checkMode & CheckMode.Inferential) != 0 && (checkMode & CheckMode.SkipContextSensitive) == 0 &&
                    memberKind is SyntaxKind.PropertyAssignment or SyntaxKind.MethodDeclaration && IsContextSensitive(memberDecl))
                {
                    var inferenceContext = GetInferenceContext(node);
                    if (inferenceContext is not null)
                    {
                        var inferenceNode = memberKind == SyntaxKind.PropertyAssignment ? At(node, store.Initializer(memberNode)) : memberDecl;
                        AddIntraExpressionInferenceSite(inferenceContext, inferenceNode, type);
                    }
                }
            }
            else if (memberKind == SyntaxKind.SpreadAssignment)
            {
                if (propertiesArray.Count > 0)
                {
                    spread = GetSpreadType(spread, CreateObjectLiteralType(), NodeSymbol(node), objectFlags, inConstContext);
                    propertiesArray = [];
                    propertiesTable = new SymTable();
                    hasComputedStringProperty = false;
                    hasComputedNumberProperty = false;
                    hasComputedSymbolProperty = false;
                }
                var type = GetReducedType(CheckExpression(At(node, store.Expression(memberNode)), checkMode & CheckMode.Inferential));
                if (IsValidSpreadType(type))
                {
                    var mergedType = TryMergeUnionOfObjectTypeAndEmptyObject(type, inConstContext);
                    if (allPropertiesTable is not null)
                    {
                        CheckSpreadPropOverrides(mergedType, allPropertiesTable, memberDecl);
                    }
                    offset = propertiesArray.Count;
                    if (IsErrorType(spread))
                    {
                        continue;
                    }
                    spread = GetSpreadType(spread, mergedType, NodeSymbol(node), objectFlags, inConstContext);
                }
                else
                {
                    Error(memberDecl, Diagnostics.Spread_types_may_only_be_created_from_object_types);
                    spread = errorType;
                }
                continue;
            }
            else
            {
                // TypeScript 1.0 spec (April 2014)
                // A get accessor declaration is processed in the same manner as
                // an ordinary function declaration(section 6.1) with no parameters.
                // A set accessor declaration is processed in the same manner
                // as an ordinary function declaration with a single parameter and a Void return type.
                CheckNodeDeferred(memberDecl);
            }
            if (!computedNameType.IsNone && (Types.Flags(computedNameType) & TypeFlags.StringOrNumberLiteralOrUnique) == 0)
            {
                if (IsTypeAssignableTo(computedNameType, stringNumberSymbolType))
                {
                    if (IsTypeAssignableTo(computedNameType, numberType))
                    {
                        hasComputedNumberProperty = true;
                    }
                    else if (IsTypeAssignableTo(computedNameType, esSymbolType))
                    {
                        hasComputedSymbolProperty = true;
                    }
                    else
                    {
                        hasComputedStringProperty = true;
                    }
                    if (inDestructuringPattern)
                    {
                        patternWithComputedProperties = true;
                    }
                }
            }
            else
            {
                propertiesTable.Set(NameOf(member), member);
            }
            propertiesArray.Add(member);
        }
        PopContextualType();
        if (IsErrorType(spread))
        {
            return errorType;
        }
        if (spread != emptyObjectType)
        {
            if (propertiesArray.Count > 0)
            {
                spread = GetSpreadType(spread, CreateObjectLiteralType(), NodeSymbol(node), objectFlags, inConstContext);
                propertiesArray = [];
                propertiesTable = new SymTable();
                hasComputedStringProperty = false;
                hasComputedNumberProperty = false;
            }
            // remap the raw emptyObjectType fed in at the top into a fresh empty object literal type, unique to this use site
            return MapType(spread, t => t == emptyObjectType ? CreateObjectLiteralType() : t);
        }
        return CreateObjectLiteralType();

        Type CreateObjectLiteralType()
        {
            var indexInfos = new List<IndexInfo>();
            var isReadonly = IsConstContext(node);
            if (hasComputedStringProperty)
            {
                indexInfos.Add(GetObjectLiteralIndexInfo(isReadonly, offset, propertiesArray, stringType));
            }
            if (hasComputedNumberProperty)
            {
                indexInfos.Add(GetObjectLiteralIndexInfo(isReadonly, offset, propertiesArray, numberType));
            }
            if (hasComputedSymbolProperty)
            {
                indexInfos.Add(GetObjectLiteralIndexInfo(isReadonly, offset, propertiesArray, esSymbolType));
            }
            var result = CreateAnonymousType(NodeSymbol(node), propertiesTable, [], [], indexInfos);
            Types.Record(result).ObjectFlags |= objectFlags | ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
            if (isJSObjectLiteral)
            {
                Types.Record(result).ObjectFlags |= ObjectFlags.JSLiteral;
            }
            if (patternWithComputedProperties)
            {
                Types.Record(result).ObjectFlags |= ObjectFlags.ObjectLiteralPatternWithComputedProperties;
            }
            if (inDestructuringPattern)
            {
                typePatterns[result] = node;
            }
            return result;
        }
    }

    // utilities.ts isInJsonFile
    private bool IsInJsonFile(NodeRef node)
    {
        var store = StoreOf(node);
        return store.ScriptKind(Utilities.RootOf(store, node.Node)) == ScriptKind.JSON;
    }

    // checker.ts checkPropertyAssignment
    private Type CheckPropertyAssignment(NodeRef node, CheckMode checkMode)
    {
        var store = StoreOf(node);
        // Do not use hasDynamicName here, because that returns false for well known symbols.
        // We want to perform checkComputedPropertyName for all computed properties, including
        // well known symbols.
        var name = store.Name(node.Node);
        if (store.Kind(name) == SyntaxKind.ComputedPropertyName)
        {
            CheckComputedPropertyName(At(node, name));
        }
        return CheckExpressionForMutableLocation(At(node, store.Initializer(node.Node)), checkMode);
    }

    // checker.ts checkObjectLiteralMethod (checkGrammarMethod: C8)
    private Type CheckObjectLiteralMethod(NodeRef node, CheckMode checkMode)
    {
        var store = StoreOf(node);
        // Do not use hasDynamicName here, because that returns false for well known symbols.
        // We want to perform checkComputedPropertyName for all computed properties, including
        // well known symbols.
        var name = store.Name(node.Node);
        if (store.Kind(name) == SyntaxKind.ComputedPropertyName)
        {
            CheckComputedPropertyName(At(node, name));
        }
        var uninstantiatedType = CheckFunctionExpressionOrObjectLiteralMethod(node, checkMode);
        return InstantiateTypeWithSingleGenericCallSignature(node, uninstantiatedType, checkMode);
    }

    // checker.ts hasDefaultValue
    private static bool HasDefaultValue(NodeStore store, Node node) => store.Kind(node) switch
    {
        SyntaxKind.BindingElement => !store.Initializer(node).IsNone,
        SyntaxKind.PropertyAssignment => HasDefaultValue(store, store.Initializer(node)),
        SyntaxKind.ShorthandPropertyAssignment => !store.ObjectAssignmentInitializer(node).IsNone,
        SyntaxKind.BinaryExpression => store.Kind(store.OperatorToken(node)) == SyntaxKind.EqualsToken,
        _ => false,
    };

    // checker.ts isValidSpreadType
    private bool IsValidSpreadType(Type type)
    {
        var t = RemoveDefinitelyFalsyTypes(MapType(type, GetBaseConstraintOrType));
        var flags = Types.Flags(t);
        if ((flags & (TypeFlags.Any | TypeFlags.NonPrimitive | TypeFlags.Object | TypeFlags.InstantiableNonPrimitive)) != 0)
        {
            return true;
        }
        if ((flags & TypeFlags.UnionOrIntersection) != 0)
        {
            foreach (var member in Types.Elements(Types.Union(t).Types).ToArray())
            {
                if (!IsValidSpreadType(member))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    // checker.ts checkSpreadPropOverrides
    private void CheckSpreadPropOverrides(Type type, SymTable props, NodeRef spread)
    {
        foreach (var right in GetPropertiesOfType(type))
        {
            if ((SymbolFlagsOf(right) & SymbolFlags.Optional) == 0 && (GetCheckFlags(right) & CheckFlags.Partial) == 0)
            {
                var left = props.Get(NameOf(right));
                if (!left.IsNone)
                {
                    var diagnostic = Error(ValueDeclarationOf(left), Diagnostics._0_is_specified_more_than_once_so_this_usage_will_be_overwritten, Utilities.UnescapeLeadingUnderscores(NameOf(left)));
                    AddRelatedInfo(diagnostic, CreateRelatedForNode(spread, Diagnostics.This_spread_always_overwrites_this_property));
                }
            }
        }
    }

    // checker.ts isEmptyObjectTypeOrSpreadsIntoEmptyObject
    private bool IsEmptyObjectTypeOrSpreadsIntoEmptyObject(Type type) =>
        IsEmptyObjectType(type) || (Types.Flags(type) & (TypeFlags.Null | TypeFlags.Undefined | TypeFlags.BooleanLike | TypeFlags.NumberLike | TypeFlags.BigIntLike |
            TypeFlags.StringLike | TypeFlags.EnumLike | TypeFlags.NonPrimitive | TypeFlags.Index)) != 0;

    // checker.ts tryMergeUnionOfObjectTypeAndEmptyObject
    private Type TryMergeUnionOfObjectTypeAndEmptyObject(Type type, bool isReadonly)
    {
        if ((Types.Flags(type) & TypeFlags.Union) == 0)
        {
            return type;
        }
        var types = Types.Elements(Types.Union(type).Types).ToArray();
        if (Array.TrueForAll(types, IsEmptyObjectTypeOrSpreadsIntoEmptyObject))
        {
            var empty = Array.Find(types, IsEmptyObjectType);
            return empty.IsNone ? emptyObjectType : empty;
        }
        var firstType = Array.Find(types, t => !IsEmptyObjectTypeOrSpreadsIntoEmptyObject(t));
        if (firstType.IsNone)
        {
            return type;
        }
        var secondType = Array.Find(types, t => t != firstType && !IsEmptyObjectTypeOrSpreadsIntoEmptyObject(t));
        if (!secondType.IsNone)
        {
            return type;
        }
        // getAnonymousPartialType: gets the type as if it had been spread, but where everything in the spread is made optional
        var members = new SymTable();
        foreach (var prop in GetPropertiesOfType(firstType))
        {
            if ((GetDeclarationModifierFlagsFromSymbol(prop) & (ModifierFlags.Private | ModifierFlags.Protected)) != 0)
            {
                // do nothing, skip privates
            }
            else if (IsSpreadableProperty(prop))
            {
                var flags = SymbolFlagsOf(prop);
                var isSetonlyAccessor = (flags & SymbolFlags.SetAccessor) != 0 && (flags & SymbolFlags.GetAccessor) == 0;
                var result = CreateSymbol(SymbolFlags.Property | SymbolFlags.Optional, NameOf(prop), (GetCheckFlags(prop) & CheckFlags.Late) | (isReadonly ? CheckFlags.Readonly : CheckFlags.None));
                var propType = isSetonlyAccessor ? undefinedType : AddOptionality(GetTypeOfSymbol(prop), isProperty: true); // (before the links are written)
                ValueSymbolLinks.Get(result).ResolvedType = propType;
                TransientSymbols.Record(result).Declarations = DeclarationsOf(prop);
                var nameType = GetNameTypeOfSymbol(prop); // (before the links are written: the call can grow them)
                ValueSymbolLinks.Get(result).NameType = nameType;
                members.Set(NameOf(prop), result);
            }
        }
        var spread = CreateAnonymousType(Types.Symbol(firstType), members, [], [], GetIndexInfosOfType(firstType));
        Types.Record(spread).ObjectFlags |= ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
        return spread;
    }

    /// <summary>checker.ts getSpreadType.</summary>
    internal Type GetSpreadType(Type left, Type right, SymbolRef symbol, ObjectFlags objectFlags, bool isReadonly)
    {
        if ((Types.Flags(left) & TypeFlags.Any) != 0 || (Types.Flags(right) & TypeFlags.Any) != 0)
        {
            return anyType;
        }
        if ((Types.Flags(left) & TypeFlags.Unknown) != 0 || (Types.Flags(right) & TypeFlags.Unknown) != 0)
        {
            return unknownType;
        }
        if ((Types.Flags(left) & TypeFlags.Never) != 0)
        {
            return right;
        }
        if ((Types.Flags(right) & TypeFlags.Never) != 0)
        {
            return left;
        }
        left = TryMergeUnionOfObjectTypeAndEmptyObject(left, isReadonly);
        if ((Types.Flags(left) & TypeFlags.Union) != 0)
        {
            return CheckCrossProductUnion([left, right]) ? MapType(left, t => GetSpreadType(t, right, symbol, objectFlags, isReadonly)) : errorType;
        }
        right = TryMergeUnionOfObjectTypeAndEmptyObject(right, isReadonly);
        if ((Types.Flags(right) & TypeFlags.Union) != 0)
        {
            var leftType = left;
            return CheckCrossProductUnion([left, right]) ? MapType(right, t => GetSpreadType(leftType, t, symbol, objectFlags, isReadonly)) : errorType;
        }
        if ((Types.Flags(right) & (TypeFlags.BooleanLike | TypeFlags.NumberLike | TypeFlags.BigIntLike | TypeFlags.StringLike | TypeFlags.EnumLike | TypeFlags.NonPrimitive | TypeFlags.Index)) != 0)
        {
            return left;
        }
        if (IsGenericObjectType(left) || IsGenericObjectType(right))
        {
            if (IsEmptyObjectType(left))
            {
                return right;
            }
            // When the left type is an intersection, we may need to merge the last constituent of the
            // intersection with the right type. For example when the left type is 'T & { a: string }'
            // and the right type is '{ b: string }' we produce 'T & { a: string, b: string }'.
            if ((Types.Flags(left) & TypeFlags.Intersection) != 0)
            {
                var types = Types.Elements(Types.Union(left).Types).ToArray();
                var lastLeft = types[^1];
                if (IsNonGenericObjectType(lastLeft) && IsNonGenericObjectType(right))
                {
                    var merged = new List<Type>(types.Length);
                    for (var i = 0; i < types.Length - 1; i++)
                    {
                        merged.Add(types[i]);
                    }
                    merged.Add(GetSpreadType(lastLeft, right, symbol, objectFlags, isReadonly));
                    return GetIntersectionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(merged));
                }
            }
            return GetIntersectionType([left, right]);
        }
        var members = new SymTable();
        var skippedPrivateMembers = new HashSet<string>(StringComparer.Ordinal);
        var indexInfos = left == emptyObjectType ? GetIndexInfosOfType(right) : GetUnionIndexInfos([left, right]);
        foreach (var rightProp in GetPropertiesOfType(right))
        {
            if ((GetDeclarationModifierFlagsFromSymbol(rightProp) & (ModifierFlags.Private | ModifierFlags.Protected)) != 0)
            {
                skippedPrivateMembers.Add(NameOf(rightProp));
            }
            else if (IsSpreadableProperty(rightProp))
            {
                members.Set(NameOf(rightProp), GetSpreadSymbol(rightProp, isReadonly));
            }
        }
        foreach (var leftProp in GetPropertiesOfType(left))
        {
            var name = NameOf(leftProp);
            if (skippedPrivateMembers.Contains(name) || !IsSpreadableProperty(leftProp))
            {
                continue;
            }
            if (members.Has(name))
            {
                var rightProp = members.Get(name);
                var rightType = GetTypeOfSymbol(rightProp);
                if ((SymbolFlagsOf(rightProp) & SymbolFlags.Optional) != 0)
                {
                    var declarations = new List<NodeRef>();
                    declarations.AddRange(DeclarationsOf(leftProp) ?? []);
                    declarations.AddRange(DeclarationsOf(rightProp) ?? []);
                    var flags = SymbolFlags.Property | (SymbolFlagsOf(leftProp) & SymbolFlags.Optional);
                    var result = CreateSymbol(flags, name);
                    // Optimization: avoid calculating the union type if spreading into the exact same type.
                    // This is common, e.g. spreading one options bag into another where the bags have the
                    // same type, or have properties which overlap. If the unions are large, it may turn out
                    // to be expensive to perform subtype reduction.
                    var leftType = GetTypeOfSymbol(leftProp);
                    var leftTypeWithoutUndefined = RemoveMissingOrUndefinedType(leftType);
                    var rightTypeWithoutUndefined = RemoveMissingOrUndefinedType(rightType);
                    var propType = leftTypeWithoutUndefined == rightTypeWithoutUndefined ? leftType : GetUnionType([leftType, rightTypeWithoutUndefined], UnionReduction.Subtype); // (before the links are written)
                    ValueSymbolLinks.Get(result).ResolvedType = propType;
                    TransientSymbols.Record(result).Declarations = declarations;
                    var nameType = GetNameTypeOfSymbol(leftProp); // (before the links are written: the call can grow them)
                    ValueSymbolLinks.Get(result).NameType = nameType;
                    members.Set(name, result);
                }
            }
            else
            {
                members.Set(name, GetSpreadSymbol(leftProp, isReadonly));
            }
        }
        var spread = CreateAnonymousType(symbol, members, [], [], indexInfos.ConvertAll(info => GetIndexInfoWithReadonly(info, isReadonly)));
        Types.Record(spread).ObjectFlags |= ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral | ObjectFlags.ContainsSpread | objectFlags;
        return spread;
    }

    // checker.ts isSpreadableProperty: we approximate own properties as non-methods plus methods that are inside the object literal
    private bool IsSpreadableProperty(SymbolRef prop)
    {
        var declarations = DeclarationsOf(prop);
        if (declarations is not null)
        {
            foreach (var d in declarations)
            {
                if (IsPrivateIdentifierClassElementDeclaration(d))
                {
                    return false;
                }
            }
        }
        if ((SymbolFlagsOf(prop) & (SymbolFlags.Method | SymbolFlags.GetAccessor | SymbolFlags.SetAccessor)) == 0)
        {
            return true;
        }
        return declarations is null || !declarations.Exists(d => Utilities.IsClassLike(StoreOf(d), StoreOf(d).Parent(d.Node)));
    }

    // utilities.ts isPrivateIdentifierClassElementDeclaration
    private bool IsPrivateIdentifierClassElementDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        return store.Kind(node.Node) is SyntaxKind.PropertyDeclaration or SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor &&
            store.Kind(store.Name(node.Node)) == SyntaxKind.PrivateIdentifier;
    }

    // checker.ts getSpreadSymbol
    private SymbolRef GetSpreadSymbol(SymbolRef prop, bool isReadonly)
    {
        var flags = SymbolFlagsOf(prop);
        var isSetonlyAccessor = (flags & SymbolFlags.SetAccessor) != 0 && (flags & SymbolFlags.GetAccessor) == 0;
        if (!isSetonlyAccessor && isReadonly == IsReadonlySymbol(prop))
        {
            return prop;
        }
        var result = CreateSymbol(SymbolFlags.Property | (flags & SymbolFlags.Optional), NameOf(prop), (GetCheckFlags(prop) & CheckFlags.Late) | (isReadonly ? CheckFlags.Readonly : CheckFlags.None));
        var propType = isSetonlyAccessor ? undefinedType : GetTypeOfSymbol(prop); // (before the links are written)
        ValueSymbolLinks.Get(result).ResolvedType = propType;
        TransientSymbols.Record(result).Declarations = DeclarationsOf(prop);
        var nameType = GetNameTypeOfSymbol(prop); // (before the links are written: the call can grow them)
        ValueSymbolLinks.Get(result).NameType = nameType;
        return result;
    }

    // checker.ts getIndexInfoWithReadonly
    private static IndexInfo GetIndexInfoWithReadonly(IndexInfo info, bool isReadonly) =>
        info.IsReadonly != isReadonly ? new IndexInfo { KeyType = info.KeyType, Type = info.Type, IsReadonly = isReadonly, Declaration = info.Declaration, Components = info.Components } : info;

    // checker.ts checkGrammarObjectLiteralExpression (DeclarationMeaning: 1 get, 2 set, 4 property assignment, 8 method)
    private void CheckGrammarObjectLiteralExpression(NodeRef node, bool inDestructuring)
    {
        var store = StoreOf(node);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var prop in store.Nodes(store.Properties(node.Node)))
        {
            var propRef = At(node, prop);
            if (store.Kind(prop) == SyntaxKind.SpreadAssignment)
            {
                if (inDestructuring)
                {
                    // a rest property cannot be destructured any further
                    var expression = Utilities.SkipParentheses(store, store.Expression(prop));
                    if (store.Kind(expression) is SyntaxKind.ArrayLiteralExpression or SyntaxKind.ObjectLiteralExpression)
                    {
                        GrammarErrorOnNode(At(node, store.Expression(prop)), Diagnostics.A_rest_element_cannot_contain_a_binding_pattern);
                        return;
                    }
                }
                continue;
            }
            var name = store.Name(prop);
            if (store.Kind(name) == SyntaxKind.ComputedPropertyName)
            {
                // If the name is not a ComputedPropertyName, the grammar checking will skip it
                var nameExpression = store.Expression(name);
                if (store.Kind(nameExpression) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(nameExpression)) == SyntaxKind.CommaToken)
                {
                    GrammarErrorOnNode(At(node, nameExpression), Diagnostics.A_comma_expression_is_not_allowed_in_a_computed_property_name);
                }
            }
            if (store.Kind(prop) == SyntaxKind.ShorthandPropertyAssignment && !inDestructuring && !store.ObjectAssignmentInitializer(prop).IsNone)
            {
                // having objectAssignmentInitializer is only valid in ObjectAssignmentPattern
                // outside of destructuring it is a syntax error
                GrammarErrorOnNode(At(node, store.EqualsToken(prop)), Diagnostics.Did_you_mean_to_use_a_Colon_An_can_only_follow_a_property_name_when_the_containing_object_literal_is_part_of_a_destructuring_pattern);
            }
            if (store.Kind(name) == SyntaxKind.PrivateIdentifier)
            {
                GrammarErrorOnNode(At(node, name), Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
            }
            // Modifiers are never allowed on properties except for 'async' on a method declaration
            var modifiers = store.Modifiers(prop);
            if (!modifiers.IsNone)
            {
                foreach (var mod in store.Nodes(modifiers))
                {
                    if (UtilitiesPublic.IsModifier(store, mod) && (store.Kind(mod) != SyntaxKind.AsyncKeyword || store.Kind(prop) != SyntaxKind.MethodDeclaration))
                    {
                        GrammarErrorOnNode(At(node, mod), Diagnostics._0_modifier_cannot_be_used_here, GetTextOfNode(At(node, mod)));
                    }
                }
            }
            int currentKind;
            switch (store.Kind(prop))
            {
                case SyntaxKind.ShorthandPropertyAssignment:
                case SyntaxKind.PropertyAssignment:
                    // Grammar checking for computedPropertyName and shorthandPropertyAssignment
                    if (!store.ExclamationToken(prop).IsNone)
                    {
                        GrammarErrorOnNode(At(node, store.ExclamationToken(prop)), Diagnostics.A_definite_assignment_assertion_is_not_permitted_in_this_context);
                    }
                    if (!store.QuestionToken(prop).IsNone)
                    {
                        GrammarErrorOnNode(At(node, store.QuestionToken(prop)), Diagnostics.An_object_member_cannot_be_declared_optional);
                    }
                    if (store.Kind(name) == SyntaxKind.BigIntLiteral)
                    {
                        diagnostics.Add(CreateDiagnosticForNode(At(node, name), Diagnostics.A_bigint_literal_cannot_be_used_as_a_property_name));
                    }
                    currentKind = 4;
                    break;
                case SyntaxKind.MethodDeclaration:
                    currentKind = 8;
                    break;
                case SyntaxKind.GetAccessor:
                    currentKind = 1;
                    break;
                default:
                    currentKind = 2;
                    break;
            }
            if (!inDestructuring)
            {
                var effectiveName = GetEffectivePropertyNameForPropertyNameNode(At(node, name));
                if (effectiveName is null)
                {
                    continue;
                }
                if (!seen.TryGetValue(effectiveName, out var existingKind))
                {
                    seen[effectiveName] = currentKind;
                }
                else if ((currentKind & 8) != 0 && (existingKind & 8) != 0)
                {
                    GrammarErrorOnNode(At(node, name), Diagnostics.Duplicate_identifier_0, GetTextOfNode(At(node, name)));
                }
                else if ((currentKind & 4) != 0 && (existingKind & 4) != 0)
                {
                    GrammarErrorOnNode(At(node, name), Diagnostics.An_object_literal_cannot_have_multiple_properties_with_the_same_name, GetTextOfNode(At(node, name)));
                }
                else if ((currentKind & 3) != 0 && (existingKind & 3) != 0)
                {
                    if (existingKind != 3 && currentKind != existingKind)
                    {
                        seen[effectiveName] = currentKind | existingKind;
                    }
                    else
                    {
                        GrammarErrorOnNode(At(node, name), Diagnostics.An_object_literal_cannot_have_multiple_get_Slashset_accessors_with_the_same_name);
                        return;
                    }
                }
                else
                {
                    GrammarErrorOnNode(At(node, name), Diagnostics.An_object_literal_cannot_have_property_and_accessor_with_the_same_name);
                    return;
                }
            }
            _ = propRef;
        }
    }

    // checker.ts getEffectivePropertyNameForPropertyNameNode
    private string? GetEffectivePropertyNameForPropertyNameNode(NodeRef node)
    {
        var store = StoreOf(node);
        var name = GetPropertyNameForPropertyNameNode(store, node.Node);
        if (name is not null)
        {
            return name;
        }
        if (store.Kind(node.Node) == SyntaxKind.ComputedPropertyName)
        {
            var type = GetTypeOfExpression(At(node, store.Expression(node.Node)));
            return TryGetNameFromType(type);
        }
        return null;
    }

    // utilities.ts getPropertyNameForPropertyNameNode
    private static string? GetPropertyNameForPropertyNameNode(NodeStore store, Node name)
    {
        switch (store.Kind(name))
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.PrivateIdentifier:
                return store.Texts.GetString(store.EscapedText(name));
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
                return Utilities.EscapeLeadingUnderscores(store.Texts.GetString(store.Text(name))!);
            case SyntaxKind.ComputedPropertyName:
            {
                var nameExpression = store.Expression(name);
                if (store.Kind(nameExpression) is SyntaxKind.StringLiteral or SyntaxKind.NoSubstitutionTemplateLiteral or SyntaxKind.NumericLiteral)
                {
                    return Utilities.EscapeLeadingUnderscores(store.Texts.GetString(store.Text(nameExpression))!);
                }
                if (Utilities.IsSignedNumericLiteral(store, nameExpression))
                {
                    var operandText = store.Texts.GetString(store.Text(store.Operand(nameExpression)))!;
                    return store.Operator(nameExpression) == SyntaxKind.MinusToken ? "-" + operandText : operandText;
                }
                return null;
            }
        }
        return null;
    }

    // checker.ts tryGetNameFromType
    private string? TryGetNameFromType(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.UniqueESSymbol) != 0)
        {
            return GetPropertyNameFromType(type);
        }
        if ((flags & TypeFlags.StringLiteral) != 0)
        {
            return Utilities.EscapeLeadingUnderscores((string)Types.Literal(type).Value!);
        }
        if ((flags & TypeFlags.NumberLiteral) != 0)
        {
            return JsNumber.ToString((double)Types.Literal(type).Value!);
        }
        return null;
    }

    // ---- regular types and widening ----

    // (getRegularTypeOfObjectLiteral: Checker.RelationHelpers.cs, C4)

    // checker.ts transformTypeOfMembers
    private SymTable TransformTypeOfMembers(Type type, Func<Type, Type> f)
    {
        var members = new SymTable();
        foreach (var property in GetPropertiesOfObjectType(type))
        {
            var original = GetTypeOfSymbol(property);
            var updated = f(original);
            members.Set(NameOf(property), updated == original ? property : CreateSymbolWithType(property, updated));
        }
        return members;
    }

    /// <summary>checker.ts WideningContext.</summary>
    private sealed class WideningContext
    {
        public WideningContext? Parent;
        public string? PropertyName;
        public List<Type>? Siblings;
        public List<SymbolRef>? ResolvedProperties;
    }

    // checker.ts getSiblingsOfContext
    private List<Type> GetSiblingsOfContext(WideningContext context)
    {
        if (context.Siblings is null)
        {
            var siblings = new List<Type>();
            foreach (var type in GetSiblingsOfContext(context.Parent!))
            {
                if (IsObjectLiteralType(type))
                {
                    var prop = GetPropertyOfObjectType(type, context.PropertyName!);
                    if (!prop.IsNone)
                    {
                        ForEachType(GetTypeOfSymbol(prop), siblings.Add);
                    }
                }
            }
            context.Siblings = siblings;
        }
        return context.Siblings;
    }

    // checker.ts getPropertiesOfContext
    private List<SymbolRef> GetPropertiesOfContext(WideningContext context)
    {
        if (context.ResolvedProperties is null)
        {
            var names = new SymTable();
            foreach (var t in GetSiblingsOfContext(context))
            {
                if (IsObjectLiteralType(t) && (Types.ObjectFlagsOf(t) & ObjectFlags.ContainsSpread) == 0)
                {
                    foreach (var prop in GetPropertiesOfType(t))
                    {
                        names.Set(NameOf(prop), prop);
                    }
                }
            }
            var result = new List<SymbolRef>(names.Count);
            for (var i = 0; i < names.Count; i++)
            {
                result.Add(names.ValueAt(i));
            }
            context.ResolvedProperties = result;
        }
        return context.ResolvedProperties;
    }

    // checker.ts getWidenedProperty
    private SymbolRef GetWidenedProperty(SymbolRef prop, WideningContext? context)
    {
        if ((SymbolFlagsOf(prop) & SymbolFlags.Property) == 0)
        {
            // Since get accessors already widen their return value there is no need to
            // widen accessor based properties here.
            return prop;
        }
        var original = GetTypeOfSymbol(prop);
        var propContext = context is null ? null : new WideningContext { Parent = context, PropertyName = NameOf(prop) };
        var widened = GetWidenedTypeWithContext(original, propContext);
        return widened == original ? prop : CreateSymbolWithType(prop, widened);
    }

    // checker.ts getUndefinedProperty
    private SymbolRef GetUndefinedProperty(SymbolRef prop)
    {
        if (undefinedProperties.TryGetValue(NameOf(prop), out var cached))
        {
            return cached;
        }
        var result = CreateSymbolWithType(prop, undefinedOrMissingType);
        TransientSymbols.Record(result).Flags |= SymbolFlags.Optional;
        undefinedProperties[NameOf(prop)] = result;
        return result;
    }

    // checker.ts getWidenedTypeOfObjectLiteral
    private Type GetWidenedTypeOfObjectLiteral(Type type, WideningContext? context)
    {
        var members = new SymTable();
        foreach (var prop in GetPropertiesOfObjectType(type))
        {
            members.Set(NameOf(prop), GetWidenedProperty(prop, context));
        }
        if (context is not null)
        {
            foreach (var prop in GetPropertiesOfContext(context))
            {
                if (!members.Has(NameOf(prop)))
                {
                    members.Set(NameOf(prop), GetUndefinedProperty(prop));
                }
            }
        }
        var indexInfos = GetIndexInfosOfType(type).ConvertAll(info => new IndexInfo { KeyType = info.KeyType, Type = GetWidenedType(info.Type), IsReadonly = info.IsReadonly, Declaration = info.Declaration, Components = info.Components });
        var result = CreateAnonymousType(Types.Symbol(type), members, [], [], indexInfos);
        Types.Record(result).ObjectFlags |= Types.ObjectFlagsOf(type) & (ObjectFlags.JSLiteral | ObjectFlags.NonInferrableType); // Retain js literal flag through widening
        return result;
    }

    /// <summary>checker.ts getWidenedType.</summary>
    internal Type GetWidenedType(Type type) => GetWidenedTypeWithContext(type, null);

    // checker.ts getWidenedTypeWithContext
    private Type GetWidenedTypeWithContext(Type type, WideningContext? context)
    {
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.RequiresWidening) != 0)
        {
            if (context is null && widenedTypes.TryGetValue(type, out var widened))
            {
                return widened;
            }
            var result = Type.None;
            var flags = Types.Flags(type);
            if ((flags & (TypeFlags.Any | TypeFlags.Nullable)) != 0)
            {
                result = anyType;
            }
            else if (IsObjectLiteralType(type))
            {
                result = GetWidenedTypeOfObjectLiteral(type, context);
            }
            else if ((flags & TypeFlags.Union) != 0)
            {
                var members = Types.Elements(Types.Union(type).Types).ToArray();
                var unionContext = context ?? new WideningContext { Siblings = [.. members] };
                var widenedMembers = Array.ConvertAll(members, t => (Types.Flags(t) & TypeFlags.Nullable) != 0 ? t : GetWidenedTypeWithContext(t, unionContext));
                // Widening an empty object literal transitions from a highly restrictive type to
                // a highly inclusive one. For that reason we perform subtype reduction here if the
                // union includes empty object types (e.g. reducing {} | string to just {}).
                result = GetUnionType(widenedMembers, Array.Exists(widenedMembers, IsEmptyObjectType) ? UnionReduction.Subtype : UnionReduction.Literal);
            }
            else if ((flags & TypeFlags.Intersection) != 0)
            {
                result = GetIntersectionType(Array.ConvertAll(Types.Elements(Types.Union(type).Types).ToArray(), GetWidenedType));
            }
            else if (IsArrayOrTupleType(type))
            {
                result = CreateTypeReference(Types.Object(type).Target, Array.ConvertAll(GetTypeArguments(type).ToArray(), GetWidenedType));
            }
            if (!result.IsNone && context is null)
            {
                widenedTypes[type] = result;
            }
            return result.IsNone ? type : result;
        }
        return type;
    }

    // checker.ts reportWideningErrorsInType
    private bool ReportWideningErrorsInType(Type type)
    {
        var errorReported = false;
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.ContainsWideningType) != 0)
        {
            if ((Types.Flags(type) & TypeFlags.Union) != 0)
            {
                var members = Types.Elements(Types.Union(type).Types).ToArray();
                if (Array.Exists(members, IsEmptyObjectType))
                {
                    errorReported = true;
                }
                else
                {
                    foreach (var t in members)
                    {
                        errorReported |= ReportWideningErrorsInType(t);
                    }
                }
            }
            else if (IsArrayOrTupleType(type))
            {
                foreach (var t in GetTypeArguments(type).ToArray())
                {
                    errorReported |= ReportWideningErrorsInType(t);
                }
            }
            else if (IsObjectLiteralType(type))
            {
                foreach (var p in GetPropertiesOfObjectType(type))
                {
                    var t = GetTypeOfSymbol(p);
                    if ((Types.ObjectFlagsOf(t) & ObjectFlags.ContainsWideningType) != 0)
                    {
                        errorReported = ReportWideningErrorsInType(t);
                        if (!errorReported)
                        {
                            // we need to account for property types coming from object literal type normalization in unions
                            var typeValueDeclaration = Types.Symbol(type).IsNone ? NodeRef.None : ValueDeclarationOf(Types.Symbol(type));
                            var valueDeclaration = NodeRef.None;
                            foreach (var d in DeclarationsOf(p) ?? [])
                            {
                                var dSymbol = NodeSymbol(d);
                                if (!dSymbol.IsNone && !typeValueDeclaration.IsNone && Parent(ValueDeclarationOf(dSymbol)) == typeValueDeclaration)
                                {
                                    valueDeclaration = d;
                                    break;
                                }
                            }
                            if (!valueDeclaration.IsNone)
                            {
                                Error(valueDeclaration, Diagnostics.Object_literal_s_property_0_implicitly_has_an_1_type, SymbolToString(p), TypeToString(GetWidenedType(t)));
                                errorReported = true;
                            }
                        }
                    }
                }
            }
        }
        return errorReported;
    }

    // checker.ts shouldReportErrorsFromWideningWithContextualSignature
    private bool ShouldReportErrorsFromWideningWithContextualSignature(NodeRef declaration, WideningKind wideningKind)
    {
        var signature = GetContextualSignatureForFunctionLikeDeclaration(declaration);
        if (signature.IsNone)
        {
            return true;
        }
        var returnType = GetReturnTypeOfSignature(signature);
        var flags = GetFunctionFlagsOf(StoreOf(declaration), declaration.Node);
        switch (wideningKind)
        {
            case WideningKind.FunctionReturn:
                if ((flags & FunctionFlags.Generator) != 0)
                {
                    var iterated = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, returnType, (flags & FunctionFlags.Async) != 0);
                    returnType = iterated.IsNone ? returnType : iterated;
                }
                else if ((flags & FunctionFlags.Async) != 0)
                {
                    var awaited = GetAwaitedTypeNoAlias(returnType);
                    returnType = awaited.IsNone ? returnType : awaited;
                }
                return IsGenericType(returnType);
            case WideningKind.GeneratorYield:
            {
                var yieldType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Yield, returnType, (flags & FunctionFlags.Async) != 0);
                return !yieldType.IsNone && IsGenericType(yieldType);
            }
            case WideningKind.GeneratorNext:
            {
                var nextType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Next, returnType, (flags & FunctionFlags.Async) != 0);
                return !nextType.IsNone && IsGenericType(nextType);
            }
        }
        return false;
    }

    /// <summary>checker.ts reportErrorsFromWidening.</summary>
    internal void ReportErrorsFromWidening(NodeRef declaration, Type type, WideningKind wideningKind = WideningKind.Normal)
    {
        AddLazyDiagnostic(() =>
        {
            if (noImplicitAny && (Types.ObjectFlagsOf(type) & ObjectFlags.ContainsWideningType) != 0)
            {
                if (wideningKind == WideningKind.Normal ||
                    (Utilities.IsFunctionLikeDeclaration(StoreOf(declaration), declaration.Node) && ShouldReportErrorsFromWideningWithContextualSignature(declaration, wideningKind)))
                {
                    // Report implicit any error within type if possible, otherwise report error on declaration
                    if (!ReportWideningErrorsInType(type))
                    {
                        ReportImplicitAny(declaration, type, wideningKind);
                    }
                }
            }
        });
    }
}
