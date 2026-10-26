// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (module C5; the functions are listed below)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (details below).
// Functions (variable-like declarations and destructuring: getTypeForVariableLikeDeclaration,
// getWidenedTypeForVariableLikeDeclaration, widenTypeForVariableLikeDeclaration, widenTypeInferredFromInitializer,
// isGlobalSymbolConstructor, getTypeForBindingElementParent, getTypeForBindingElement,
// getBindingElementTypeFromParentType, getRestType, isGenericTypeWithUndefinedConstraint, getNonUndefinedType,
// getFlowTypeOfDestructuring, getTypeFromBindingElement, getTypeFromObjectBindingPattern,
// getTypeFromArrayBindingPattern, getTypeFromBindingPattern, createIterableType, getTypeOfInitializer,
// getGlobalOmitSymbol, cloneTypeReference)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (replaces C1's getTypeForVariableLikeDeclaration and its
// for-of and binding element approximations). TODO(C7): getFlowTypeOfDestructuring and getFlowTypeInConstructor;
// TODO(C9): JavaScript container object types and JSDoc types.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    /// <summary>checker.ts getTypeForVariableLikeDeclaration (None: no type specified and nothing can be inferred).</summary>
    private Type GetTypeForVariableLikeDeclaration(NodeRef declaration, bool includeOptionality, CheckMode checkMode = CheckMode.Normal)
    {
        var store = StoreOf(declaration);
        var node = declaration.Node;
        var kind = store.Kind(node);
        var parent = store.Parent(node);
        // A variable declared in a for..in statement is of type string, or of type keyof T when the
        // right hand expression is of a type parameter type.
        if (kind == SyntaxKind.VariableDeclaration && store.Kind(store.Parent(parent)) == SyntaxKind.ForInStatement)
        {
            var exprType = CheckExpression(At(declaration, store.Expression(store.Parent(parent))), checkMode);
            var indexType = GetIndexType(GetNonNullableTypeIfNeeded(exprType));
            return (Types.Flags(indexType) & (TypeFlags.TypeParameter | TypeFlags.Index)) != 0 ? GetExtractStringType(indexType) : stringType;
        }
        if (kind == SyntaxKind.VariableDeclaration && store.Kind(store.Parent(parent)) == SyntaxKind.ForOfStatement)
        {
            // checkRightHandSideOfForOf will return undefined if the for-of expression type was
            // missing properties/signatures required to get its iteratedType (like
            // [Symbol.iterator] or next). This may be because we accessed properties from anyType,
            // or it may have led to an error inside getElementTypeOfIterable.
            var forOfStatement = At(declaration, store.Parent(parent));
            var t = CheckRightHandSideOfForOf(forOfStatement);
            if (IsUncertainExpression(At(declaration, store.Expression(forOfStatement.Node))))
            {
                MarkUncertainDeclaration(declaration);
            }
            return t.IsNone ? anyType : t;
        }
        if (Utilities.IsBindingPattern(store, parent))
        {
            return GetTypeForBindingElement(declaration);
        }
        var isProperty = (kind == SyntaxKind.PropertyDeclaration && !Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Accessor)) || kind == SyntaxKind.PropertySignature;
        var isOptional = includeOptionality && IsOptionalDeclaration(declaration);
        // Use type from type annotation if one is present
        var typeNode = GetEffectiveTypeAnnotationNode(declaration);
        var declaredType = typeNode.IsNone ? Type.None : GetTypeFromTypeNode(typeNode);
        if (IsCatchClauseVariableDeclarationOrBindingElement(store, node))
        {
            if (!declaredType.IsNone)
            {
                // If the catch clause is explicitly annotated with any or unknown, accept it, otherwise error.
                return IsTypeAny(declaredType) || declaredType == unknownType ? declaredType : errorType;
            }
            // If the catch clause is not explicitly annotated, treat it as though it were explicitly
            // annotated with unknown or any, depending on useUnknownInCatchVariables.
            return compilerOptions.GetStrictOptionValue(compilerOptions.UseUnknownInCatchVariables) ? unknownType : anyType;
        }
        if (!declaredType.IsNone)
        {
            return AddOptionality(declaredType, isProperty, isOptional);
        }
        var initializer = kind is SyntaxKind.VariableDeclaration or SyntaxKind.Parameter or SyntaxKind.PropertyDeclaration or SyntaxKind.BindingElement
            ? store.Initializer(node)
            : Node.None;
        if ((noImplicitAny || Utilities.IsInJSFile(store, node)) && kind == SyntaxKind.VariableDeclaration && !Utilities.IsBindingPattern(store, store.Name(node)) &&
            (Utilities.GetCombinedModifierFlags(store, node) & ModifierFlags.Export) == 0 && (store.Flags(node) & NodeFlags.Ambient) == 0)
        {
            // If --noImplicitAny is on or the declaration is in a Javascript file,
            // use control flow tracked 'any' type for non-ambient, non-exported var or let variables with no
            // initializer or a 'null' or 'undefined' initializer.
            if ((Utilities.GetCombinedNodeFlags(store, node) & NodeFlags.Constant) == 0 && (initializer.IsNone || IsNullOrUndefined(At(declaration, initializer))))
            {
                return autoType;
            }
            // Use control flow tracked 'any[]' type for non-ambient, non-exported variables with an empty array
            // literal initializer.
            if (!initializer.IsNone && IsEmptyArrayLiteral(store, initializer))
            {
                return autoArrayType;
            }
        }
        if (kind == SyntaxKind.Parameter)
        {
            var parameterSymbol = NodeSymbol(declaration);
            if (parameterSymbol.IsNone)
            {
                // parameters of function types defined in JSDoc in TS files don't have symbols
                return Type.None;
            }
            var func = At(declaration, parent);
            // For a parameter of a set accessor, use the type of the get accessor if one is present
            if (store.Kind(parent) == SyntaxKind.SetAccessor && HasBindableName(func))
            {
                var getter = GetDeclarationOfKind(GetSymbolOfDeclaration(func), SyntaxKind.GetAccessor);
                if (!getter.IsNone)
                {
                    var getterSignature = GetSignatureFromDeclaration(getter);
                    var thisParameter = GetAccessorThisParameter(func);
                    if (!thisParameter.IsNone && declaration == thisParameter)
                    {
                        // Use the type from the *getter*
                        return GetTypeOfSymbol(Signatures.Record(getterSignature).ThisParameter);
                    }
                    return GetReturnTypeOfSignature(getterSignature);
                }
            }
            // Use contextual parameter type if one is available
            var type = NameOf(parameterSymbol) == "this" ? GetContextualThisParameterType(func) : GetContextuallyTypedParameterType(declaration);
            if (!type.IsNone)
            {
                return AddOptionality(type, isProperty: false, isOptional);
            }
        }
        // Use the type of the initializer expression if one is present and the declaration is
        // not a parameter of a contextually typed function
        if (!initializer.IsNone)
        {
            var initializerType = CheckDeclarationInitializer(declaration, checkMode);
            if (IsUncertainExpression(At(declaration, initializer)))
            {
                MarkUncertainDeclaration(declaration);
            }
            var type = WidenTypeInferredFromInitializer(declaration, initializerType);
            return AddOptionality(type, isProperty, isOptional);
        }
        if (kind == SyntaxKind.PropertyDeclaration && (noImplicitAny || Utilities.IsInJSFile(store, node)))
        {
            // module C7: the type of a property from this.x assignments in the constructor or static blocks (TODO(C9): the
            // JavaScript forms)
            var flowType = GetTypeOfPropertyFromConstructorFlow(declaration);
            return flowType.IsNone ? Type.None : AddOptionality(flowType, isProperty: true, isOptional);
        }
        if (kind == SyntaxKind.JsxAttribute)
        {
            return trueType;
        }
        // If the declaration specifies a binding pattern and is not a parameter of a contextually
        // typed function, use the type implied by the binding pattern
        var name = kind is SyntaxKind.Parameter or SyntaxKind.VariableDeclaration or SyntaxKind.BindingElement ? store.Name(node) : Node.None;
        if (!name.IsNone && Utilities.IsBindingPattern(store, name))
        {
            return GetTypeFromBindingPattern(At(declaration, name), includePatternInType: false, reportErrors: true);
        }
        // No type specified and nothing can be inferred
        return Type.None;
    }

    // utilities.ts isCatchClauseVariableDeclarationOrBindingElement
    private static bool IsCatchClauseVariableDeclarationOrBindingElement(NodeStore store, Node declaration)
    {
        var node = Utilities.GetRootDeclaration(store, declaration);
        return store.Kind(node) == SyntaxKind.VariableDeclaration && store.Kind(store.Parent(node)) == SyntaxKind.CatchClause;
    }

    // checker.ts getNonNullableTypeIfNeeded
    private Type GetNonNullableTypeIfNeeded(Type type) => IsNullableType(type) ? GetNonNullableType(type) : type;

    // checker.ts isNullableType
    private bool IsNullableType(Type type) => HasTypeFacts(type, TypeFacts.IsUndefinedOrNull);

    // utilities.ts getThisParameter of an accessor (getAccessorThisParameter)
    private NodeRef GetAccessorThisParameter(NodeRef accessor)
    {
        var store = StoreOf(accessor);
        var parameters = store.Nodes(store.Parameters(accessor.Node));
        if (parameters.Length == (store.Kind(accessor.Node) == SyntaxKind.GetAccessor ? 1 : 2))
        {
            var first = parameters[0];
            var name = store.Name(first);
            if (store.Kind(name) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(name), "this"))
            {
                return At(accessor, first);
            }
        }
        return NodeRef.None;
    }

    // checker.ts getWidenedTypeForVariableLikeDeclaration
    private Type GetWidenedTypeForVariableLikeDeclaration(NodeRef declaration, bool reportErrors) =>
        WidenTypeForVariableLikeDeclaration(GetTypeForVariableLikeDeclaration(declaration, includeOptionality: true), declaration, reportErrors);

    // checker.ts widenTypeForVariableLikeDeclaration
    private Type WidenTypeForVariableLikeDeclaration(Type type, NodeRef declaration, bool reportErrors)
    {
        var store = StoreOf(declaration);
        if (!type.IsNone)
        {
            // TODO: If back compat with pre-3.0/4.0 libs isn't required, remove the following SymbolConstructor special case transforming `symbol` into `unique symbol`
            if ((Types.Flags(type) & TypeFlags.ESSymbol) != 0 && IsGlobalSymbolConstructor(At(declaration, store.Parent(declaration.Node))))
            {
                type = GetESSymbolLikeTypeForNode(declaration);
            }
            if (reportErrors)
            {
                ReportErrorsFromWidening(declaration, type);
            }
            // always widen a 'unique symbol' type if the type was created for a different declaration.
            if ((Types.Flags(type) & TypeFlags.UniqueESSymbol) != 0 && (store.Kind(declaration.Node) == SyntaxKind.BindingElement || store.Type(declaration.Node).IsNone) &&
                Types.Symbol(type) != GetSymbolOfDeclaration(declaration))
            {
                type = esSymbolType;
            }
            return GetWidenedType(type);
        }
        // Rest parameters default to type any[], other parameters default to type any
        type = store.Kind(declaration.Node) == SyntaxKind.Parameter && !store.DotDotDotToken(declaration.Node).IsNone ? anyArrayType : anyType;
        // Report implicit any errors unless this is a private property within an ambient declaration
        if (reportErrors && !DeclarationBelongsToPrivateAmbientMember(declaration))
        {
            ReportImplicitAny(declaration, type);
        }
        return type;
    }

    // checker.ts isGlobalSymbolConstructor
    private bool IsGlobalSymbolConstructor(NodeRef node)
    {
        var symbol = NodeSymbol(node);
        var globalSymbol = GetGlobalESSymbolConstructorTypeSymbol();
        return !globalSymbol.IsNone && !symbol.IsNone && symbol == globalSymbol;
    }

    // checker.ts widenTypeInferredFromInitializer
    private Type WidenTypeInferredFromInitializer(NodeRef declaration, Type type)
    {
        var store = StoreOf(declaration);
        var widened = (Utilities.GetCombinedNodeFlags(store, declaration.Node) & NodeFlags.Constant) != 0 || IsDeclarationReadonly(store, declaration.Node) ? type : GetWidenedLiteralType(type);
        // (JSDoc enum tags: C9)
        return widened;
    }

    // The type of the declaration's symbol is an approximation (C1's "uncertain" mechanism, for C6's inferences): no
    // diagnostics are derived from its uses
    private void MarkUncertainDeclaration(NodeRef declaration)
    {
        var symbol = GetSymbolOfDeclaration(declaration);
        if (!symbol.IsNone)
        {
            uncertainSymbols.Add(symbol);
        }
    }

    // ---- binding elements ----

    // checker.ts getTypeForBindingElementParent
    private Type GetTypeForBindingElementParent(NodeRef node, CheckMode checkMode)
    {
        if (checkMode != CheckMode.Normal)
        {
            return GetTypeForVariableLikeDeclaration(node, includeOptionality: false, checkMode);
        }
        var symbol = GetSymbolOfDeclaration(node);
        if (!symbol.IsNone && ValueSymbolLinks.Has(symbol) && !ValueSymbolLinks.Get(symbol).ResolvedType.IsNone)
        {
            return ValueSymbolLinks.Get(symbol).ResolvedType;
        }
        return GetTypeForVariableLikeDeclaration(node, includeOptionality: false, checkMode);
    }

    /// <summary>checker.ts getTypeForBindingElement.</summary>
    private Type GetTypeForBindingElement(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var checkMode = !store.DotDotDotToken(declaration.Node).IsNone ? CheckMode.RestBindingElement : CheckMode.Normal;
        var grandparent = At(declaration, store.Parent(store.Parent(declaration.Node)));
        var parentType = GetTypeForBindingElementParent(grandparent, checkMode);
        if (IsUncertainBindingParent(grandparent))
        {
            MarkUncertainDeclaration(declaration);
        }
        return parentType.IsNone ? Type.None : GetBindingElementTypeFromParentType(declaration, parentType, noTupleBoundsCheck: false);
    }

    // C1's "uncertain" mechanism: the parent of a binding element is an uncertain declaration
    private bool IsUncertainBindingParent(NodeRef parent)
    {
        var symbol = GetSymbolOfDeclaration(parent);
        if (!symbol.IsNone && uncertainSymbols.Contains(symbol))
        {
            return true;
        }
        var store = StoreOf(parent);
        var initializer = store.Kind(parent.Node) is SyntaxKind.VariableDeclaration or SyntaxKind.Parameter or SyntaxKind.BindingElement ? store.Initializer(parent.Node) : Node.None;
        return !initializer.IsNone && IsUncertainExpression(At(parent, initializer));
    }

    // Whether the diagnostics derived from the type of a binding pattern's parent are not reported: the parent's type
    // is an approximation (C1's "uncertain" mechanism, TODO(C6)) or comes from a reference whose narrowing is pending
    // (TODO(C7))
    private bool SuppressBindingPatternDiagnostics(NodeRef parent, Type parentType)
    {
        if (IsUncertainBindingParent(parent))
        {
            return true;
        }
        var store = StoreOf(parent);
        switch (store.Kind(parent.Node))
        {
            case SyntaxKind.BindingElement:
                return SuppressBindingPatternDiagnostics(At(parent, store.Parent(store.Parent(parent.Node))), parentType);
            case SyntaxKind.Parameter:
            {
                var func = store.Parent(parent.Node);
                return store.Type(parent.Node).IsNone && uncertainExpressions.Contains(At(parent, func));
            }
            case SyntaxKind.VariableDeclaration:
            {
                var initializer = store.Initializer(parent.Node);
                if (!initializer.IsNone)
                {
                    return false;
                }
                var statement = store.Parent(store.Parent(parent.Node));
                if (store.Kind(statement) == SyntaxKind.ForOfStatement)
                {
                    return IsUncertainExpression(At(parent, store.Expression(statement)));
                }
                return false;
            }
        }
        return false;
    }

    /// <summary>checker.ts getBindingElementTypeFromParentType.</summary>
    private Type GetBindingElementTypeFromParentType(NodeRef declaration, Type parentType, bool noTupleBoundsCheck)
    {
        // If an any type was inferred for parent, infer that for the binding element
        if (IsTypeAny(parentType))
        {
            return parentType;
        }
        var store = StoreOf(declaration);
        var n = declaration.Node;
        var pattern = At(declaration, store.Parent(n));
        var patternParent = store.Parent(pattern.Node);
        var patternParentInitializer = store.Kind(patternParent) is SyntaxKind.VariableDeclaration or SyntaxKind.Parameter or SyntaxKind.BindingElement ? store.Initializer(patternParent) : Node.None;
        // Relax null check on ambient destructuring parameters, since the parameters have no implementation and are just documentation
        if (strictNullChecks && (store.Flags(n) & NodeFlags.Ambient) != 0 && Utilities.IsPartOfParameterDeclaration(store, n))
        {
            parentType = GetNonNullableType(parentType);
        }
        // Filter `undefined` from the type we check against if the parent has an initializer and that initializer is not possibly `undefined`
        else if (strictNullChecks && !patternParentInitializer.IsNone && !HasTypeFacts(GetTypeOfInitializer(At(declaration, patternParentInitializer)), TypeFacts.EQUndefined))
        {
            parentType = GetTypeWithFacts(parentType, TypeFacts.NEUndefined);
        }
        var accessFlags = AccessFlags.ExpressionPosition | (noTupleBoundsCheck || HasDefaultValue(store, n) ? AccessFlags.AllowMissing : AccessFlags.None);
        var suppress = SuppressBindingPatternDiagnostics(At(declaration, patternParent), parentType);
        Type type;
        if (store.Kind(pattern.Node) == SyntaxKind.ObjectBindingPattern)
        {
            if (!store.DotDotDotToken(n).IsNone)
            {
                parentType = GetReducedType(parentType);
                if ((Types.Flags(parentType) & TypeFlags.Unknown) != 0 || !IsValidSpreadType(parentType))
                {
                    if (!suppress)
                    {
                        Error(declaration, Diagnostics.Rest_types_may_only_be_created_from_object_types);
                    }
                    return errorType;
                }
                var literalMembers = new List<NodeRef>();
                foreach (var element in store.Nodes(store.Elements(pattern.Node)))
                {
                    if (store.DotDotDotToken(element).IsNone)
                    {
                        var propertyName = store.PropertyName(element);
                        literalMembers.Add(At(declaration, propertyName.IsNone ? store.Name(element) : propertyName));
                    }
                }
                type = GetRestType(parentType, literalMembers, NodeSymbol(declaration));
            }
            else
            {
                // Use explicitly specified property name ({ p: xxx } form), or otherwise the implied name ({ p } form)
                var propertyName = store.PropertyName(n);
                var name = At(declaration, propertyName.IsNone ? store.Name(n) : propertyName);
                var indexType = GetLiteralTypeFromPropertyName(name);
                Type declaredType;
                if (suppress)
                {
                    declaredType = GetIndexedAccessTypeOrUndefined(parentType, indexType, accessFlags);
                    if (declaredType.IsNone)
                    {
                        declaredType = errorType;
                    }
                }
                else
                {
                    declaredType = GetIndexedAccessType(parentType, indexType, accessFlags, name);
                }
                type = GetFlowTypeOfDestructuring(declaration, declaredType);
            }
        }
        else
        {
            // This elementType will be used if the specific property corresponding to this index is not
            // present (aka the tuple element property). This call also checks that the parentType is in
            // fact an iterable or array (depending on target language).
            var elementType = CheckIteratedTypeOrElementType(IterationUse.Destructuring | (!store.DotDotDotToken(n).IsNone ? IterationUse.None : IterationUse.PossiblyOutOfBounds), parentType, undefinedType, suppress ? NodeRef.None : pattern);
            var index = store.Nodes(store.Elements(pattern.Node)).IndexOf(n);
            if (!store.DotDotDotToken(n).IsNone)
            {
                // If the parent is a tuple type, the rest element has a tuple type of the
                // remaining tuple element types. Otherwise, the rest element has an array type with same
                // element type as the parent type.
                var baseConstraint = MapType(parentType, t => (Types.Flags(t) & TypeFlags.InstantiableNonPrimitive) != 0 ? GetBaseConstraintOrType(t) : t);
                type = EveryType(baseConstraint, IsTupleType)
                    ? MapType(baseConstraint, t => SliceTupleType(t, index))
                    : CreateArrayType(elementType);
            }
            else if (IsArrayLikeType(parentType))
            {
                var indexType = GetNumberLiteralType(index);
                var declaredType = GetIndexedAccessTypeOrUndefined(parentType, indexType, accessFlags, suppress ? NodeRef.None : At(declaration, store.Name(n)));
                type = GetFlowTypeOfDestructuring(declaration, declaredType.IsNone ? errorType : declaredType);
            }
            else
            {
                type = elementType;
            }
        }
        if (store.Initializer(n).IsNone)
        {
            return type;
        }
        if (!GetEffectiveTypeAnnotationNode(WalkUpBindingElementsAndPatterns(declaration)).IsNone)
        {
            // In strict null checking mode, if a default value of a non-undefined type is specified, remove
            // undefined from the final type.
            return strictNullChecks && !HasTypeFacts(CheckDeclarationInitializer(declaration, CheckMode.Normal), TypeFacts.IsUndefined) ? GetNonUndefinedType(type) : type;
        }
        return WidenTypeInferredFromInitializer(declaration, GetUnionType([GetNonUndefinedType(type), CheckDeclarationInitializer(declaration, CheckMode.Normal)], UnionReduction.Subtype));
    }

    // checker.ts getRestType
    private Type GetRestType(Type source, List<NodeRef> properties, SymbolRef symbol)
    {
        source = FilterType(source, t => (Types.Flags(t) & TypeFlags.Nullable) == 0);
        if ((Types.Flags(source) & TypeFlags.Never) != 0)
        {
            return emptyObjectType;
        }
        if ((Types.Flags(source) & TypeFlags.Union) != 0)
        {
            return MapType(source, t => GetRestType(t, properties, symbol));
        }
        var omitKeyType = GetUnionType(properties.ConvertAll(GetLiteralTypeFromPropertyName).ToArray());
        var spreadableProperties = new List<SymbolRef>();
        var unspreadableToRestKeys = new List<Type>();
        foreach (var prop in GetPropertiesOfType(source))
        {
            var literalTypeFromProperty = GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique);
            if (!IsTypeAssignableTo(literalTypeFromProperty, omitKeyType) &&
                (GetDeclarationModifierFlagsFromSymbol(prop) & (ModifierFlags.Private | ModifierFlags.Protected)) == 0 &&
                IsSpreadableProperty(prop))
            {
                spreadableProperties.Add(prop);
            }
            else
            {
                unspreadableToRestKeys.Add(literalTypeFromProperty);
            }
        }
        if (IsGenericObjectType(source) || IsGenericIndexType(omitKeyType))
        {
            if (unspreadableToRestKeys.Count > 0)
            {
                // If the type we're spreading from has properties that cannot
                // be spread into the rest type (e.g. getters, methods), ensure
                // they are explicitly omitted, as they would in the non-generic case.
                unspreadableToRestKeys.Insert(0, omitKeyType);
                omitKeyType = GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(unspreadableToRestKeys));
            }
            if ((Types.Flags(omitKeyType) & TypeFlags.Never) != 0)
            {
                return source;
            }
            var omitTypeAlias = GetGlobalOmitSymbol();
            if (omitTypeAlias.IsNone)
            {
                return errorType;
            }
            return GetTypeAliasInstantiation(omitTypeAlias, [source, omitKeyType]);
        }
        var members = new SymTable();
        foreach (var prop in spreadableProperties)
        {
            members.Set(NameOf(prop), GetSpreadSymbol(prop, isReadonly: false));
        }
        var result = CreateAnonymousType(symbol, members, [], [], GetIndexInfosOfType(source));
        Types.Record(result).ObjectFlags |= ObjectFlags.ObjectRestType;
        return result;
    }

    private SymbolRef deferredGlobalOmitSymbol;

    // checker.ts getGlobalOmitSymbol (getGlobalTypeAliasSymbol("Omit", 2, true))
    private SymbolRef GetGlobalOmitSymbol()
    {
        if (deferredGlobalOmitSymbol.IsNone)
        {
            var symbol = GetGlobalSymbol("Omit", SymbolFlags.Type, Diagnostics.Cannot_find_global_type_0);
            if (!symbol.IsNone)
            {
                GetDeclaredTypeOfSymbol(symbol);
                if ((GetTypeAliasTypeParameters(symbol)?.Length ?? 0) != 2)
                {
                    var declaration = DeclarationsOf(symbol)?.Find(d => Kind(d) == SyntaxKind.TypeAliasDeclaration) ?? NodeRef.None;
                    Error(declaration, Diagnostics.Global_type_0_must_have_1_type_parameter_s, SymbolName(symbol), 2);
                    symbol = SymbolRef.None;
                }
            }
            deferredGlobalOmitSymbol = symbol.IsNone ? unknownSymbol : symbol;
        }
        return deferredGlobalOmitSymbol == unknownSymbol ? SymbolRef.None : deferredGlobalOmitSymbol;
    }

    // checker.ts isGenericTypeWithUndefinedConstraint
    private bool IsGenericTypeWithUndefinedConstraint(Type type)
    {
        if ((Types.Flags(type) & TypeFlags.Instantiable) == 0)
        {
            return false;
        }
        var constraint = GetBaseConstraintOfType(type);
        return MaybeTypeOfKind(constraint.IsNone ? unknownType : constraint, TypeFlags.Undefined);
    }

    // checker.ts getNonUndefinedType
    internal Type GetNonUndefinedType(Type type)
    {
        var typeOrConstraint = SomeType(type, IsGenericTypeWithUndefinedConstraint)
            ? MapType(type, t => (Types.Flags(t) & TypeFlags.Instantiable) != 0 ? GetBaseConstraintOrType(t) : t)
            : type;
        return GetTypeWithFacts(typeOrConstraint, TypeFacts.NEUndefined);
    }

    // checker.ts getFlowTypeOfDestructuring (TODO(C7): the synthetic element access and its flow type)
    private static Type GetFlowTypeOfDestructuring(NodeRef node, Type declaredType)
    {
        _ = node;
        return declaredType;
    }

    // checker.ts getTypeOfInitializer
    private Type GetTypeOfInitializer(NodeRef node)
    {
        // Return the cached type if one is available. If the type of the variable was inferred
        // from its initializer, we'll already have cached the type. Otherwise we compute it now
        // without caching such that transient types are reflected.
        var resolved = ExpressionLinks.Has(node) ? ExpressionLinks.Get(node).ResolvedType : Type.None;
        return !resolved.IsNone ? resolved : GetTypeOfExpression(node);
    }

    // ---- binding patterns ----

    // checker.ts getTypeFromBindingElement
    private Type GetTypeFromBindingElement(NodeRef element, bool includePatternInType, bool reportErrors)
    {
        var store = StoreOf(element);
        var name = store.Name(element.Node);
        if (!store.Initializer(element.Node).IsNone)
        {
            // The type implied by a binding pattern is independent of context, so we check the initializer with no
            // contextual type or, if the element itself is a binding pattern, with the type implied by that binding
            // pattern.
            var contextualType = Utilities.IsBindingPattern(store, name) ? GetTypeFromBindingPattern(At(element, name), includePatternInType: true, reportErrors: false) : unknownType;
            return AddOptionality(GetWidenedLiteralTypeForInitializer(element, CheckDeclarationInitializer(element, CheckMode.Normal, contextualType)));
        }
        if (Utilities.IsBindingPattern(store, name))
        {
            return GetTypeFromBindingPattern(At(element, name), includePatternInType, reportErrors);
        }
        if (reportErrors && suppressImplicitAnyDepth == 0 && !DeclarationBelongsToPrivateAmbientMember(element))
        {
            ReportImplicitAny(element, anyType);
        }
        // When we're including the pattern in the type (an indication we're obtaining a contextual type), we
        // use a non-inferrable any type. Inference will never directly infer this type, but it is possible
        // to infer a type that contains it, e.g. for a binding pattern like [foo] or { foo }. In such cases,
        // widening of the binding pattern type substitutes a regular any for the non-inferrable any.
        return includePatternInType ? nonInferrableAnyType : anyType;
    }

    // checker.ts getTypeFromObjectBindingPattern
    private Type GetTypeFromObjectBindingPattern(NodeRef pattern, bool includePatternInType, bool reportErrors)
    {
        var store = StoreOf(pattern);
        var members = new SymTable();
        IndexInfo? stringIndexInfo = null;
        var objectFlags = ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
        foreach (var e in store.Nodes(store.Elements(pattern.Node)))
        {
            var propertyName = store.PropertyName(e);
            var name = At(pattern, propertyName.IsNone ? store.Name(e) : propertyName);
            if (!store.DotDotDotToken(e).IsNone)
            {
                stringIndexInfo = new IndexInfo { KeyType = stringType, Type = anyType, IsReadonly = false };
                continue;
            }
            var exprType = GetLiteralTypeFromPropertyName(name);
            if (!IsTypeUsableAsPropertyName(exprType))
            {
                // do not include computed properties in the implied type
                objectFlags |= ObjectFlags.ObjectLiteralPatternWithComputedProperties;
                continue;
            }
            var text = GetPropertyNameFromType(exprType);
            var flags = SymbolFlags.Property | (!store.Initializer(e).IsNone ? SymbolFlags.Optional : SymbolFlags.None);
            var symbol = CreateSymbol(flags, text);
            ValueSymbolLinks.Get(symbol).ResolvedType = GetTypeFromBindingElement(At(pattern, e), includePatternInType, reportErrors);
            members.Set(text, symbol);
        }
        var result = CreateAnonymousType(SymbolRef.None, members, [], [], stringIndexInfo is not null ? [stringIndexInfo] : []);
        Types.Record(result).ObjectFlags |= objectFlags;
        if (includePatternInType)
        {
            typePatterns[result] = pattern;
            Types.Record(result).ObjectFlags |= ObjectFlags.ContainsObjectOrArrayLiteral;
        }
        return result;
    }

    // checker.ts getTypeFromArrayBindingPattern
    private Type GetTypeFromArrayBindingPattern(NodeRef pattern, bool includePatternInType, bool reportErrors)
    {
        var store = StoreOf(pattern);
        var elements = store.Nodes(store.Elements(pattern.Node));
        var lastElement = elements.Length > 0 ? elements[^1] : Node.None;
        var restElement = !lastElement.IsNone && store.Kind(lastElement) == SyntaxKind.BindingElement && !store.DotDotDotToken(lastElement).IsNone ? lastElement : Node.None;
        if (elements.Length == 0 || (elements.Length == 1 && !restElement.IsNone))
        {
            return languageVersion >= ScriptTarget.ES2015 ? CreateIterableType(anyType) : anyArrayType;
        }
        var elementTypes = new Type[elements.Length];
        for (var i = 0; i < elements.Length; i++)
        {
            elementTypes[i] = store.Kind(elements[i]) == SyntaxKind.OmittedExpression ? anyType : GetTypeFromBindingElement(At(pattern, elements[i]), includePatternInType, reportErrors);
        }
        var minLength = elements.Length - 1;
        while (minLength >= 0 && (elements[minLength] == restElement || store.Kind(elements[minLength]) == SyntaxKind.OmittedExpression || HasDefaultValue(store, elements[minLength])))
        {
            minLength--;
        }
        minLength++;
        var elementFlags = new ElementFlags[elements.Length];
        for (var i = 0; i < elements.Length; i++)
        {
            elementFlags[i] = elements[i] == restElement ? ElementFlags.Rest : i >= minLength ? ElementFlags.Optional : ElementFlags.Required;
        }
        var result = CreateTupleType(elementTypes, elementFlags);
        if (includePatternInType)
        {
            result = CloneTypeReference(result);
            typePatterns[result] = pattern;
            Types.Record(result).ObjectFlags |= ObjectFlags.ContainsObjectOrArrayLiteral;
        }
        return result;
    }

    /// <summary>checker.ts getTypeFromBindingPattern.</summary>
    internal Type GetTypeFromBindingPattern(NodeRef pattern, bool includePatternInType = false, bool reportErrors = false)
    {
        if (includePatternInType)
        {
            contextualBindingPatterns.Add(pattern);
        }
        var result = Kind(pattern) == SyntaxKind.ObjectBindingPattern
            ? GetTypeFromObjectBindingPattern(pattern, includePatternInType, reportErrors)
            : GetTypeFromArrayBindingPattern(pattern, includePatternInType, reportErrors);
        if (includePatternInType)
        {
            contextualBindingPatterns.RemoveAt(contextualBindingPatterns.Count - 1);
        }
        return result;
    }

    // checker.ts createIterableType
    private Type CreateIterableType(Type iteratedType) =>
        CreateTypeFromGenericGlobalType(GetGlobalIterableType(reportErrors: true), [iteratedType, voidType, undefinedType]);

    // checker.ts cloneTypeReference
    private Type CloneTypeReference(Type source)
    {
        var flags = Types.ObjectFlagsOf(source);
        var type = CreateObjectType(flags, Types.Symbol(source));
        Types.Record(type).ObjectFlags = flags;
        var data = Types.Object(type);
        var sourceData = Types.Object(source);
        data.Target = sourceData.Target;
        data.ResolvedTypeArguments = Types.CreateList(GetTypeArguments(source));
        data.HasResolvedTypeArguments = true;
        data.Node = sourceData.Node;
        data.Mapper = sourceData.Mapper;
        return type;
    }
}
