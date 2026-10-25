// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (the relation checks of checkVariableLikeDeclaration: the initializer's
// assignability and the identity of subsequent declarations' types, errorNextVariableOrPropertyDeclarationMustHaveSameType,
// convertAutoToAny)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go (checkVariableLikeDeclaration)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C4. The rest of checkVariableLikeDeclaration is module C8's; binding
// patterns and `using` declarations are left to it. Initializers whose types C1 approximates are not related.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // checker.ts convertAutoToAny
    private Type ConvertAutoToAny(Type type) => type == autoType ? anyType : type == autoArrayType ? anyArrayType : type;

    /// <summary>
    /// checker.ts checkVariableLikeDeclaration, the relation checks of a declaration with an identifier name: the
    /// primary declaration's initializer is assignable to the symbol's type; a subsequent declaration has the same type
    /// and its initializer is assignable to its own type.
    /// </summary>
    private void CheckVariableLikeDeclarationRelations(NodeRef node, SymbolRef symbol)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var kind = store.Kind(n);
        var name = store.Name(n);
        if (name.IsNone || store.Kind(name) is SyntaxKind.ComputedPropertyName or SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern)
        {
            return;
        }
        // hasOnlyExpressionInitializer
        var initializer = kind is SyntaxKind.VariableDeclaration or SyntaxKind.Parameter or SyntaxKind.PropertyDeclaration ? store.Initializer(n) : default;
        if (kind == SyntaxKind.Parameter && !initializer.IsNone)
        {
            // a parameter initializer of a function without a body is an error of module C8 (checkVariableLikeDeclaration exits)
            var container = Utilities.GetContainingFunctionOrNone(store, n);
            if (!container.IsNone && Utilities.NodeIsMissing(store, store.Body(container)))
            {
                return;
            }
        }
        if ((SymbolFlagsOf(symbol) & SymbolFlags.Alias) != 0)
        {
            return; // CommonJS `const x = require(...)` aliases (module C9)
        }
        var forIn = kind == SyntaxKind.VariableDeclaration && store.Kind(store.Parent(store.Parent(n))) == SyntaxKind.ForInStatement;
        var type = ConvertAutoToAny(GetTypeOfSymbol(symbol));
        if (node == ValueDeclarationOf(symbol))
        {
            // Node is the primary declaration of the symbol, just validate the initializer
            // Don't validate for-in initializer as it is already an error
            if (!initializer.IsNone && !forIn)
            {
                var initializerRef = At(node, initializer);
                var isJSObjectLiteralInitializer = Utilities.IsInJSFile(store, n) && store.Kind(initializer) == SyntaxKind.ObjectLiteralExpression;
                if (!isJSObjectLiteralInitializer)
                {
                    var initializerType = CheckExpressionCached(initializerRef);
                    if (!IsUncertainExpression(initializerRef) && !IsUnsupportedType(initializerType))
                    {
                        if (IsRelatableInitializer(initializerRef, symbol, type))
                        {
                            CheckTypeAssignableToAndOptionallyElaborate(initializerType, type, node, initializerRef);
                        }
                        else
                        {
                            // the relation is computed as TypeScript does (its caches and the types it creates), its
                            // failure is not reported before the expression types are exact (TODO(C5/C7))
                            IsTypeAssignableTo(initializerType, type);
                        }
                    }
                }
            }
        }
        else if (kind is SyntaxKind.VariableDeclaration or SyntaxKind.PropertyDeclaration or SyntaxKind.PropertySignature)
        {
            // Node is a secondary declaration, check that type is identical to primary declaration and check that
            // initializer is consistent with type associated with the node
            var declarationType = ConvertAutoToAny(GetWidenedTypeForVariableLikeDeclaration(node, reportErrors: false));
            if (uncertainSymbols.Contains(symbol) || IsUnsupportedType(type) || IsUnsupportedType(declarationType))
            {
                return;
            }
            if (!IsErrorType(type) && !IsErrorType(declarationType) && !IsTypeIdenticalTo(type, declarationType) && (SymbolFlagsOf(symbol) & SymbolFlags.Assignment) == 0 &&
                kind == SyntaxKind.VariableDeclaration && HasExactDeclaredType(node) && HasExactDeclaredType(ValueDeclarationOf(symbol)))
            {
                // (declarations without a type annotation have the types of their initializers, which are exact only
                // once C5 lands; property declarations (unique symbol types, 2717) are left to C8: TODO(C5/C8))
                ErrorNextVariableOrPropertyDeclarationMustHaveSameType(ValueDeclarationOf(symbol), type, node, declarationType);
            }
            if (!initializer.IsNone)
            {
                var initializerRef = At(node, initializer);
                var initializerType = CheckExpressionCached(initializerRef);
                if (!IsUncertainExpression(initializerRef) && !IsUnsupportedType(initializerType))
                {
                    if (!store.Type(n).IsNone && IsContextFreeExpression(initializerRef))
                    {
                        CheckTypeAssignableToAndOptionallyElaborate(initializerType, declarationType, node, initializerRef);
                    }
                    else
                    {
                        IsTypeAssignableTo(initializerType, declarationType); // (see above)
                    }
                }
            }
        }
    }

    // Whether the initializer can be related to the declared type before contextual typing (C5), inference (C6) and
    // narrowing (C7) are ported: the declaration has a type annotation and the initializer's type does not depend on
    // its context. TODO(C5/C7): relate every initializer once expression types are exact.
    private bool IsRelatableInitializer(NodeRef initializer, SymbolRef symbol, Type type)
    {
        if (uncertainSymbols.Contains(symbol) || IsUnsupportedType(type) || initializer.IsNone)
        {
            return false;
        }
        // (module C5: expression types are exact except where they depend on inference (C6) or narrowing (C7))
        return !SuppressDerivedDiagnostics(initializer, CheckExpressionCached(initializer));
    }

    // An expression whose type checkExpression computes without a contextual type: literals, null and undefined, signed
    // numeric literals, void expressions and (non-const) type assertions.
    private bool IsContextFreeExpression(NodeRef expression)
    {
        var store = StoreOf(expression);
        var n = expression.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.ParenthesizedExpression:
                return IsContextFreeExpression(At(expression, store.Expression(n)));
            case SyntaxKind.NullKeyword:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.VoidExpression:
                return true;
            case SyntaxKind.Identifier:
                return GetResolvedSymbol(expression) == undefinedSymbol;
            case SyntaxKind.PrefixUnaryExpression:
                return store.Operator(n) is SyntaxKind.MinusToken or SyntaxKind.PlusToken &&
                    store.Kind(store.Operand(n)) is SyntaxKind.NumericLiteral or SyntaxKind.BigIntLiteral;
            case SyntaxKind.AsExpression:
            case SyntaxKind.TypeAssertionExpression:
                return !Utilities.IsConstAssertion(store, n);
        }
        return false;
    }

    // Whether the declaration's type is exact before C5: it has a type annotation, or it is a variable declaration
    // (not of a for-in or for-of statement) without an initializer or with a context-free one.
    private bool HasExactDeclaredType(NodeRef declaration)
    {
        if (declaration.IsNone)
        {
            return false;
        }
        var store = StoreOf(declaration);
        if (!store.Type(declaration.Node).IsNone)
        {
            return true;
        }
        if (store.Kind(declaration.Node) != SyntaxKind.VariableDeclaration)
        {
            return false;
        }
        var statement = store.Parent(store.Parent(declaration.Node));
        if (store.Kind(statement) is SyntaxKind.ForInStatement or SyntaxKind.ForOfStatement)
        {
            // (module C5: the iterated types are exact unless the expression's type depends on inference or narrowing)
            var expression = At(declaration, store.Expression(statement));
            return !SuppressDerivedDiagnostics(expression, CheckExpressionCached(expression));
        }
        var initializer = store.Initializer(declaration.Node);
        // (module C5: initializer types are exact unless they depend on inference (C6) or narrowing (C7))
        return initializer.IsNone || !SuppressDerivedDiagnostics(At(declaration, initializer), CheckExpressionCached(At(declaration, initializer)));
    }

    /// <summary>
    /// checker.ts checkTypeReferenceOrImport's constraint check: the type arguments of a type reference satisfy the
    /// constraints of the type parameters (a lazy diagnostic).
    /// </summary>
    private void CheckTypeReferenceTypeArguments(NodeRef node)
    {
        var store = StoreOf(node);
        var typeArgumentNodes = store.TypeArguments(node.Node);
        if (typeArgumentNodes.IsNone)
        {
            return;
        }
        var type = GetTypeFromTypeNode(node);
        if (IsErrorType(type))
        {
            return;
        }
        AddLazyDiagnostic(() =>
        {
            var typeParameters = GetTypeParametersForTypeReferenceOrImport(node);
            if (typeParameters is not null)
            {
                CheckTypeArgumentConstraints(node, typeParameters);
            }
        });
    }

    // ---- the relation checks of interfaces, classes and type literals (checkInterfaceDeclaration,
    // checkClassLikeDeclaration, checkTypeLiteral: the parts that relate types; the rest is module C8's) ----

    /// <summary>checker.ts checkInterfaceDeclaration's lazy relation checks.</summary>
    private void CheckInterfaceDeclarationRelations(NodeRef node)
    {
        AddLazyDiagnostic(() =>
        {
            var symbol = GetSymbolOfDeclaration(node);
            // Only check this symbol once
            var firstInterfaceDecl = GetDeclarationOfKind(symbol, SyntaxKind.InterfaceDeclaration);
            if (node != firstInterfaceDecl)
            {
                return;
            }
            var type = GetDeclaredTypeOfSymbol(symbol);
            var typeWithThis = GetTypeWithThisArgument(type);
            var name = At(node, StoreOf(node).Name(node.Node));
            // run subsequent checks only if first set succeeded
            if (CheckInheritedPropertiesAreIdentical(type, name))
            {
                foreach (var baseType in GetBaseTypes(type).ToArray())
                {
                    // (intersection base types need the index signatures of intersections (resolveIntersectionTypeMembers,
                    // C5); they are related without reporting: TODO(C5))
                    CheckTypeAssignableTo(typeWithThis, GetTypeWithThisArgument(baseType, Types.Object(type).ThisType),
                        (Types.Flags(baseType) & TypeFlags.Intersection) != 0 ? NodeRef.None : name, Diagnostics.Interface_0_incorrectly_extends_interface_1);
                }
                CheckIndexConstraints(type, symbol);
            }
        });
    }

    /// <summary>checker.ts checkTypeLiteral's lazy checkIndexConstraints.</summary>
    private void CheckTypeLiteralRelations(NodeRef node)
    {
        AddLazyDiagnostic(() =>
        {
            var type = GetTypeFromTypeLiteralOrFunctionOrConstructorTypeNode(node);
            CheckIndexConstraints(type, Types.Symbol(type));
        });
    }

    /// <summary>
    /// checker.ts checkClassLikeDeclaration's relation checks: the class against its base class (instance and static
    /// side) and its implemented interfaces, and the index constraints of the instance and static sides.
    /// </summary>
    private void CheckClassLikeDeclarationRelations(NodeRef node)
    {
        var store = StoreOf(node);
        var symbol = GetSymbolOfDeclaration(node);
        var type = GetDeclaredTypeOfSymbol(symbol);
        if ((Types.Flags(type) & TypeFlags.Object) == 0)
        {
            return;
        }
        var typeWithThis = GetTypeWithThisArgument(type);
        var staticType = GetTypeOfSymbol(symbol);
        var nameOrNode = !store.Name(node.Node).IsNone ? At(node, store.Name(node.Node)) : node;
        var baseTypeNode = GetBaseTypeNodeOfClass(node);
        if (!baseTypeNode.IsNone)
        {
            var baseTypes = GetBaseTypes(type);
            if (baseTypes.Count > 0)
            {
                AddLazyDiagnostic(() =>
                {
                    var baseType = baseTypes[0];
                    var baseConstructorType = GetBaseConstructorTypeOfClass(type);
                    var staticBaseType = GetApparentType(baseConstructorType);
                    // (checkBaseTypeAccessibility, the type argument constraints of the base constructors: module C8)
                    var baseWithThis = GetTypeWithThisArgument(baseType, Types.Object(type).ThisType);
                    var baseExpression = At(baseTypeNode, StoreOf(baseTypeNode).Expression(baseTypeNode.Node));
                    if (!CheckTypeAssignableTo(typeWithThis, baseWithThis, NodeRef.None) &&
                        Utilities.IsEntityNameExpression(StoreOf(baseExpression), baseExpression.Node) && !HasInexactMemberTypes(node))
                    {
                        // (class types with members whose types C5 infers and base types of call expressions (C5/C6) are
                        // related without reporting: TODO(C5/C6))
                        IssueMemberSpecificError(node, typeWithThis, baseWithThis, Diagnostics.Class_0_incorrectly_extends_base_class_1);
                    }
                    else
                    {
                        // Report static side error only when instance type is assignable
                        CheckTypeAssignableTo(staticType, GetTypeWithoutSignatures(staticBaseType), HasInexactMemberTypes(node) ? NodeRef.None : nameOrNode,
                            Diagnostics.Class_static_side_0_incorrectly_extends_base_class_static_side_1);
                    }
                    // (mixin classes, base constructor return types, checkKindsOfPropertyMemberOverrides: module C8)
                });
            }
        }
        var clauses = store.HeritageClauses(node.Node);
        if (!clauses.IsNone)
        {
            foreach (var clause in store.Nodes(clauses))
            {
                if (store.Token(clause) != SyntaxKind.ImplementsKeyword)
                {
                    continue;
                }
                foreach (var typeRefNode in store.Nodes(store.Types(clause)))
                {
                    var typeRef = At(node, typeRefNode);
                    AddLazyDiagnostic(() =>
                    {
                        // createImplementsDiagnostics
                        var t = GetReducedType(GetTypeFromTypeNode(typeRef));
                        if (!IsErrorType(t) && IsValidBaseType(t))
                        {
                            var genericDiag = !Types.Symbol(t).IsNone && (SymbolFlagsOf(Types.Symbol(t)) & SymbolFlags.Class) != 0
                                ? Diagnostics.Class_0_incorrectly_implements_class_1_Did_you_mean_to_extend_1_and_inherit_its_members_as_a_subclass
                                : Diagnostics.Class_0_incorrectly_implements_interface_1;
                            var baseWithThis = GetTypeWithThisArgument(t, Types.Object(type).ThisType);
                            if (!CheckTypeAssignableTo(typeWithThis, baseWithThis, NodeRef.None) && !HasInexactMemberTypes(node))
                            {
                                IssueMemberSpecificError(node, typeWithThis, baseWithThis, genericDiag);
                            }
                        }
                        // (A_class_can_only_implement_an_object_type...: module C8)
                    });
                }
            }
        }
        AddLazyDiagnostic(() =>
        {
            CheckIndexConstraints(type, symbol);
            CheckIndexConstraints(staticType, symbol, isStaticIndex: true);
        });
    }

    // Whether a class has members whose types are inferred from expressions (methods and get accessors without return
    // type annotations, properties and parameter properties without annotations and with initializers that are not
    // context free): their types are exact once C5 lands.
    private bool HasInexactMemberTypes(NodeRef node)
    {
        var store = StoreOf(node);
        foreach (var member in store.Nodes(store.Members(node.Node)))
        {
            switch (store.Kind(member))
            {
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.GetAccessor:
                    // (generic methods: their relation needs the inference of instantiateSignatureInContextOf, C6)
                    if (!store.TypeParameters(member).IsNone)
                    {
                        return true;
                    }
                    if (store.Type(member).IsNone && !store.Body(member).IsNone)
                    {
                        // (module C5: return types inferred from bodies are exact unless they depend on inference (C6)
                        // or narrowing (C7))
                        GetReturnTypeOfSignature(GetSignatureFromDeclaration(At(node, member)));
                        if (uncertainExpressions.Contains(At(node, member)))
                        {
                            return true;
                        }
                    }
                    break;
                case SyntaxKind.PropertyDeclaration:
                    if (store.Type(member).IsNone && !store.Initializer(member).IsNone)
                    {
                        var initializer = At(node, store.Initializer(member));
                        if (SuppressDerivedDiagnostics(initializer, CheckExpressionCached(initializer)))
                        {
                            return true;
                        }
                    }
                    break;
            }
        }
        return false;
    }

    // checker.ts issueMemberSpecificError: iterate over all implemented properties and issue errors on each one which
    // isn't compatible, rather than the class as a whole, if possible
    private void IssueMemberSpecificError(NodeRef node, Type typeWithThis, Type baseWithThis, DiagnosticMessage broadDiag)
    {
        var store = StoreOf(node);
        var issuedMemberError = false;
        foreach (var memberNode in store.Nodes(store.Members(node.Node)))
        {
            if (Utilities.IsStatic(store, memberNode))
            {
                continue;
            }
            var member = At(node, memberNode);
            var memberName = store.Name(memberNode);
            var declaredProp = !memberName.IsNone ? GetSymbolAtLocation(At(node, memberName)) : SymbolRef.None;
            if (declaredProp.IsNone)
            {
                declaredProp = GetSymbolAtLocation(member);
            }
            if (!declaredProp.IsNone)
            {
                var prop = GetPropertyOfType(typeWithThis, NameOf(declaredProp));
                var baseProp = GetPropertyOfType(baseWithThis, NameOf(declaredProp));
                if (!prop.IsNone && !baseProp.IsNone)
                {
                    DiagnosticMessageChain RootChain() =>
                        ChainDiagnosticMessages(null, Diagnostics.Property_0_in_type_1_is_not_assignable_to_the_same_property_in_base_type_2,
                            SymbolToString(declaredProp), TypeToString(typeWithThis), TypeToString(baseWithThis));
                    if (!CheckTypeAssignableTo(GetTypeOfSymbol(prop), GetTypeOfSymbol(baseProp), !memberName.IsNone ? At(node, memberName) : member, null, RootChain))
                    {
                        issuedMemberError = true;
                    }
                }
            }
        }
        if (!issuedMemberError)
        {
            // check again with diagnostics to generate a less-specific error
            var name = store.Name(node.Node);
            CheckTypeAssignableTo(typeWithThis, baseWithThis, !name.IsNone ? At(node, name) : node, broadDiag);
        }
    }

    /// <summary>checker.ts getTypeWithoutSignatures.</summary>
    internal Type GetTypeWithoutSignatures(Type type)
    {
        if ((Types.Flags(type) & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers(type);
            if (resolved.ConstructSignatures!.Count > 0 || resolved.CallSignatures!.Count > 0)
            {
                var result = CreateObjectType(ObjectFlags.Anonymous, Types.Symbol(type));
                SetStructuredTypeMembers(result, resolved.Members!, [], [], []);
                Types.Object(result).Properties = resolved.Properties;
                return result;
            }
        }
        else if ((Types.Flags(type) & TypeFlags.Intersection) != 0)
        {
            return GetIntersectionType(Array.ConvertAll(Types.Elements(Types.Union(type).Types).ToArray(), GetTypeWithoutSignatures));
        }
        return type;
    }

    // checker.ts checkInheritedPropertiesAreIdentical
    private bool CheckInheritedPropertiesAreIdentical(Type type, NodeRef typeNode)
    {
        var baseTypes = GetBaseTypes(type);
        if (baseTypes.Count < 2)
        {
            return true;
        }
        var seen = new Dictionary<string, (SymbolRef Prop, Type ContainingType)>(StringComparer.Ordinal);
        foreach (var p in ResolveDeclaredMembers(type).DeclaredProperties ?? [])
        {
            seen[NameOf(p)] = (p, type);
        }
        var ok = true;
        foreach (var baseType in baseTypes.ToArray())
        {
            var properties = GetPropertiesOfType(GetTypeWithThisArgument(baseType, Types.Object(type).ThisType));
            foreach (var prop in properties)
            {
                if (!seen.TryGetValue(NameOf(prop), out var existing))
                {
                    seen[NameOf(prop)] = (prop, baseType);
                }
                else
                {
                    var isInheritedProperty = existing.ContainingType != type;
                    if (isInheritedProperty && !IsPropertyIdenticalTo(existing.Prop, prop))
                    {
                        ok = false;
                        var typeName1 = TypeToString(existing.ContainingType);
                        var typeName2 = TypeToString(baseType);
                        var errorInfo = ChainDiagnosticMessages(null, Diagnostics.Named_property_0_of_types_1_and_2_are_not_identical, SymbolToString(prop), typeName1, typeName2);
                        errorInfo = ChainDiagnosticMessages(errorInfo, Diagnostics.Interface_0_cannot_simultaneously_extend_types_1_and_2, TypeToString(type), typeName1, typeName2);
                        diagnostics.Add(CreateDiagnosticForNodeFromMessageChain(typeNode, errorInfo, null));
                    }
                }
            }
        }
        return ok;
    }

    // checker.ts checkIndexConstraints
    private void CheckIndexConstraints(Type type, SymbolRef symbol, bool isStaticIndex = false)
    {
        var indexInfos = GetIndexInfosOfType(type);
        if (indexInfos.Count == 0)
        {
            return;
        }
        foreach (var prop in GetPropertiesOfObjectType(type))
        {
            if (!(isStaticIndex && (SymbolFlagsOf(prop) & SymbolFlags.Prototype) != 0))
            {
                CheckIndexConstraintForProperty(type, prop, GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique, includeNonPublic: true), GetNonMissingTypeOfSymbol(prop));
            }
        }
        var typeDeclaration = symbol.IsNone ? NodeRef.None : ValueDeclarationOf(symbol);
        if (!typeDeclaration.IsNone && Utilities.IsClassLike(StoreOf(typeDeclaration), typeDeclaration.Node))
        {
            var store = StoreOf(typeDeclaration);
            foreach (var member in store.Nodes(store.Members(typeDeclaration.Node)))
            {
                // Only process instance properties against instance index signatures and static properties against static index signatures
                var isStatic = Utilities.IsStatic(store, member);
                if (((!isStaticIndex && !isStatic) || (isStaticIndex && isStatic)) && !HasBindableName(At(typeDeclaration, member)))
                {
                    var memberSymbol = GetSymbolOfDeclaration(At(typeDeclaration, member));
                    var name = store.Name(member);
                    if (memberSymbol.IsNone || name.IsNone || store.Kind(name) != SyntaxKind.ComputedPropertyName)
                    {
                        continue;
                    }
                    CheckIndexConstraintForProperty(type, memberSymbol, GetTypeOfExpression(At(typeDeclaration, store.Expression(name))), GetNonMissingTypeOfSymbol(memberSymbol));
                }
            }
        }
        if (indexInfos.Count > 1)
        {
            foreach (var info in indexInfos.ToArray())
            {
                CheckIndexConstraintForIndexSignature(type, info);
            }
        }
    }

    // utilities.ts hasBindableName for a class member: its name is not a computed property name, or is a late-bindable one
    private bool HasBindableName(NodeRef member)
    {
        var store = StoreOf(member);
        var name = store.Name(member.Node);
        return name.IsNone || store.Kind(name) != SyntaxKind.ComputedPropertyName || IsLateBindableName(At(member, name));
    }

    // checker.ts checkIndexConstraintForProperty
    private void CheckIndexConstraintForProperty(Type type, SymbolRef prop, Type propNameType, Type propType)
    {
        var declaration = ValueDeclarationOf(prop);
        var name = declaration.IsNone ? NodeRef.None : GetNameOfDeclarationRef(declaration);
        if (!name.IsNone && Kind(name) == SyntaxKind.PrivateIdentifier)
        {
            return;
        }
        var indexInfos = GetIndexInfosOfType(type).FindAll(info => IsApplicableIndexType(propNameType, info.KeyType));
        var interfaceDeclaration = (Types.ObjectFlagsOf(type) & ObjectFlags.Interface) != 0 ? GetDeclarationOfKind(Types.Symbol(type), SyntaxKind.InterfaceDeclaration) : NodeRef.None;
        var propDeclaration = (!declaration.IsNone && Kind(declaration) == SyntaxKind.BinaryExpression) || (!name.IsNone && Kind(name) == SyntaxKind.ComputedPropertyName) ? declaration : NodeRef.None;
        var localPropDeclaration = GetParentOfSymbol(prop) == Types.Symbol(type) ? declaration : NodeRef.None;
        foreach (var info in indexInfos)
        {
            var localIndexDeclaration = !info.Declaration.IsNone && GetParentOfSymbol(GetSymbolOfDeclaration(info.Declaration)) == Types.Symbol(type) ? info.Declaration : NodeRef.None;
            // We check only when (a) the property is declared in the containing type, or (b) the applicable index signature is declared
            // in the containing type, or (c) the containing type is an interface and no base interface contains both the property and
            // the index signature (i.e. property and index signature are declared in separate inherited interfaces).
            var errorNode = !localPropDeclaration.IsNone ? localPropDeclaration : !localIndexDeclaration.IsNone ? localIndexDeclaration :
                !interfaceDeclaration.IsNone && !GetBaseTypes(type).Exists(b => !GetPropertyOfObjectType(b, NameOf(prop)).IsNone && !GetIndexTypeOfType(b, info.KeyType).IsNone) ? interfaceDeclaration :
                NodeRef.None;
            if (!errorNode.IsNone && !IsTypeAssignableTo(propType, info.Type))
            {
                var diagnostic = CreateDiagnosticForNode(errorNode, Diagnostics.Property_0_of_type_1_is_not_assignable_to_2_index_type_3,
                    SymbolToString(prop), TypeToString(propType), TypeToString(info.KeyType), TypeToString(info.Type));
                if (!propDeclaration.IsNone && errorNode != propDeclaration)
                {
                    AddRelatedInfo(diagnostic, CreateRelatedForNode(propDeclaration, Diagnostics._0_is_declared_here, SymbolToString(prop)));
                }
                diagnostics.Add(diagnostic);
            }
        }
    }

    // checker.ts checkIndexConstraintForIndexSignature
    private void CheckIndexConstraintForIndexSignature(Type type, IndexInfo checkInfo)
    {
        var declaration = checkInfo.Declaration;
        var indexInfos = GetIndexInfosOfType(type).FindAll(info => IsApplicableIndexType(checkInfo.KeyType, info.KeyType));
        var interfaceDeclaration = (Types.ObjectFlagsOf(type) & ObjectFlags.Interface) != 0 ? GetDeclarationOfKind(Types.Symbol(type), SyntaxKind.InterfaceDeclaration) : NodeRef.None;
        var localCheckDeclaration = !declaration.IsNone && GetParentOfSymbol(GetSymbolOfDeclaration(declaration)) == Types.Symbol(type) ? declaration : NodeRef.None;
        foreach (var info in indexInfos)
        {
            if (info == checkInfo)
            {
                continue;
            }
            var localIndexDeclaration = !info.Declaration.IsNone && GetParentOfSymbol(GetSymbolOfDeclaration(info.Declaration)) == Types.Symbol(type) ? info.Declaration : NodeRef.None;
            // We check only when (a) the check index signature is declared in the containing type, or (b) the applicable index
            // signature is declared in the containing type, or (c) the containing type is an interface and no base interface contains
            // both index signatures (i.e. the index signatures are declared in separate inherited interfaces).
            var errorNode = !localCheckDeclaration.IsNone ? localCheckDeclaration : !localIndexDeclaration.IsNone ? localIndexDeclaration :
                !interfaceDeclaration.IsNone && !GetBaseTypes(type).Exists(b => GetIndexInfoOfType(b, checkInfo.KeyType) is not null && !GetIndexTypeOfType(b, info.KeyType).IsNone) ? interfaceDeclaration :
                NodeRef.None;
            if (!errorNode.IsNone && !IsTypeAssignableTo(checkInfo.Type, info.Type))
            {
                Error(errorNode, Diagnostics._0_index_type_1_is_not_assignable_to_2_index_type_3, TypeToString(checkInfo.KeyType), TypeToString(checkInfo.Type), TypeToString(info.KeyType), TypeToString(info.Type));
            }
        }
    }

    /// <summary>
    /// checker.ts checkTypeArguments: the signature's type arguments from the type argument nodes of
    /// <paramref name="node"/> (defaults filled in), or null when one does not satisfy its constraint.
    /// </summary>
    internal Type[]? CheckTypeArguments(Signature signature, NodeRef node, bool reportErrors, DiagnosticMessage? headMessage = null)
    {
        var store = StoreOf(node);
        var typeArgumentNodes = store.Nodes(store.TypeArguments(node.Node)).ToArray();
        var declaration = Signatures.Record(signature).Declaration;
        var isJavascript = !declaration.IsNone && Utilities.IsInJSFile(StoreOf(declaration), declaration.Node);
        var typeParameters = Types.Elements(Signatures.Record(signature).TypeParameters).ToArray();
        var typeArgumentTypes = FillMissingTypeArguments(Array.ConvertAll(typeArgumentNodes, a => GetTypeFromTypeNode(At(node, a))), typeParameters, GetMinTypeArgumentCount(typeParameters), isJavascript);
        TypeMapper? mapper = null;
        for (var i = 0; i < typeArgumentNodes.Length; i++)
        {
            var constraint = GetConstraintOfTypeParameter(typeParameters[i]);
            if (!constraint.IsNone)
            {
                Func<DiagnosticMessageChain?>? errorInfo = reportErrors && headMessage is not null
                    ? () => ChainDiagnosticMessages(null, Diagnostics.Type_0_does_not_satisfy_the_constraint_1)
                    : null;
                var typeArgumentHeadMessage = headMessage ?? Diagnostics.Type_0_does_not_satisfy_the_constraint_1;
                mapper ??= CreateTypeMapper(typeParameters, typeArgumentTypes);
                var typeArgument = typeArgumentTypes[i];
                if (!CheckTypeAssignableTo(typeArgument, GetTypeWithThisArgument(InstantiateType(constraint, mapper), typeArgument),
                    reportErrors ? At(node, typeArgumentNodes[i]) : NodeRef.None, typeArgumentHeadMessage, errorInfo))
                {
                    return null;
                }
            }
        }
        return typeArgumentTypes;
    }

    // checker.ts checkTypeArgumentConstraints
    private bool CheckTypeArgumentConstraints(NodeRef node, Type[] typeParameters)
    {
        var store = StoreOf(node);
        var typeArgumentNodes = store.Nodes(store.TypeArguments(node.Node)).ToArray();
        Type[]? typeArguments = null;
        TypeMapper? mapper = null;
        var result = true;
        for (var i = 0; i < typeParameters.Length; i++)
        {
            var constraint = GetConstraintOfTypeParameter(typeParameters[i]);
            if (!constraint.IsNone)
            {
                if (typeArguments is null)
                {
                    // getEffectiveTypeArguments
                    var argumentTypes = Array.ConvertAll(typeArgumentNodes, a => GetTypeFromTypeNode(At(node, a)));
                    typeArguments = FillMissingTypeArguments(argumentTypes, typeParameters, GetMinTypeArgumentCount(typeParameters), Utilities.IsInJSFile(store, node.Node));
                    mapper = CreateTypeMapper(typeParameters, typeArguments);
                }
                var instantiatedConstraint = InstantiateType(constraint, mapper);
                // Generic type arguments and constraints are related without reporting before inference (C6) and the
                // remaining expression types (C5) are ported: their relations can depend on both. TODO(C5/C6).
                var errorNode = i < typeArgumentNodes.Length && !CouldContainTypeVariables(typeArguments[i]) && !CouldContainTypeVariables(instantiatedConstraint)
                    ? At(node, typeArgumentNodes[i])
                    : NodeRef.None;
                result = result && CheckTypeAssignableTo(typeArguments[i], instantiatedConstraint, errorNode, Diagnostics.Type_0_does_not_satisfy_the_constraint_1);
            }
        }
        return result;
    }

    // checker.ts errorNextVariableOrPropertyDeclarationMustHaveSameType
    private void ErrorNextVariableOrPropertyDeclarationMustHaveSameType(NodeRef firstDeclaration, Type firstType, NodeRef nextDeclaration, Type nextType)
    {
        var store = StoreOf(nextDeclaration);
        var nextDeclarationName = At(nextDeclaration, store.Name(nextDeclaration.Node));
        var message = store.Kind(nextDeclaration.Node) is SyntaxKind.PropertyDeclaration or SyntaxKind.PropertySignature
            ? Diagnostics.Subsequent_property_declarations_must_have_the_same_type_Property_0_must_be_of_type_1_but_here_has_type_2
            : Diagnostics.Subsequent_variable_declarations_must_have_the_same_type_Variable_0_must_be_of_type_1_but_here_has_type_2;
        var declName = Utilities.DeclarationNameToString(store, nextDeclarationName.Node);
        var err = Error(nextDeclarationName, message, declName, TypeToString(firstType), TypeToString(nextType));
        if (!firstDeclaration.IsNone)
        {
            AddRelatedInfo(err, CreateRelatedForNode(firstDeclaration, Diagnostics._0_was_also_declared_here, declName));
        }
    }
}
