// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (getDeclaredTypeOfSymbol, getBaseTypes, member resolution, getPropertyOfType,
// getSignaturesOfType, getSignatureFromDeclaration, getReturnTypeOfSignature, getTypeOfSymbol, reportImplicitAny)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C1. Stubs: instantiation (C3: members of type references and of
// generic declarations are taken uninstantiated from their targets, which keeps the symbols the instantiated members
// would have; mapped, conditional, indexed and template types), return types inferred from bodies (C5), contextual
// parameter types (C5); module C6: reverse mapped types, outer type parameters of mapped and conditional types and of
// context sensitive functions. Union and intersection properties: Checker.UnionProperties.cs (module C4).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>checker.ts SignatureKind.</summary>
internal enum SignatureKind
{
    Call,
    Construct,
}

internal sealed partial class Checker
{
    // ---- type resolution stack (pushTypeResolution / popTypeResolution) ----

    private readonly List<(object Target, int Property)> resolutionTargets = [];
    private readonly List<bool> resolutionResults = [];

    private const int TypeProperty = 0;
    private const int DeclaredTypeProperty = 1;
    private const int ResolvedReturnTypeProperty = 2;

    /// <summary>checker.ts pushTypeResolution: false (and the cycle marked) if the target is already being resolved.</summary>
    private bool PushTypeResolution(object target, int property)
    {
        var resolutionCycleStartIndex = FindResolutionCycleStartIndex(target, property);
        if (resolutionCycleStartIndex >= 0)
        {
            // A cycle was found
            for (var i = resolutionCycleStartIndex; i < resolutionResults.Count; i++)
            {
                resolutionResults[i] = false;
            }
            return false;
        }
        resolutionTargets.Add((target, property));
        resolutionResults.Add(true);
        return true;
    }

    private int FindResolutionCycleStartIndex(object target, int property)
    {
        for (var i = resolutionTargets.Count - 1; i >= resolutionStart; i--)
        {
            if (resolutionTargets[i].Property == property && resolutionTargets[i].Target.Equals(target))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>checker.ts popTypeResolution: true if no cycle was found while the target was resolved.</summary>
    private bool PopTypeResolution()
    {
        resolutionTargets.RemoveAt(resolutionTargets.Count - 1);
        var result = resolutionResults[^1];
        resolutionResults.RemoveAt(resolutionResults.Count - 1);
        return result;
    }

    // ---- declared types ----

    /// <summary>checker.ts getDeclaredTypeOfSymbol.</summary>
    internal Type GetDeclaredTypeOfSymbol(SymbolRef symbol)
    {
        var type = TryGetDeclaredTypeOfSymbol(symbol);
        return type.IsNone ? errorType : type;
    }

    /// <summary>checker.ts tryGetDeclaredTypeOfSymbol.</summary>
    internal Type TryGetDeclaredTypeOfSymbol(SymbolRef symbol)
    {
        var flags = SymbolFlagsOf(symbol);
        if ((flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
        {
            return GetDeclaredTypeOfClassOrInterface(symbol);
        }
        if ((flags & SymbolFlags.TypeAlias) != 0)
        {
            return GetDeclaredTypeOfTypeAlias(symbol);
        }
        if ((flags & SymbolFlags.TypeParameter) != 0)
        {
            return GetDeclaredTypeOfTypeParameter(symbol);
        }
        if ((flags & SymbolFlags.Enum) != 0)
        {
            return GetDeclaredTypeOfEnum(symbol);
        }
        if ((flags & SymbolFlags.EnumMember) != 0)
        {
            return GetDeclaredTypeOfEnumMember(symbol);
        }
        if ((flags & SymbolFlags.Alias) != 0)
        {
            return GetDeclaredTypeOfAlias(symbol);
        }
        return Type.None;
    }

    /// <summary>checker.ts getDeclaredTypeOfClassOrInterface.</summary>
    internal Type GetDeclaredTypeOfClassOrInterface(SymbolRef symbol)
    {
        ref var links = ref DeclaredTypeLinks.Get(symbol);
        if (links.DeclaredType.IsNone)
        {
            var kind = (SymbolFlagsOf(symbol) & SymbolFlags.Class) != 0 ? ObjectFlags.Class : ObjectFlags.Interface;
            var type = CreateObjectType(kind, symbol);
            DeclaredTypeLinks.Get(symbol).DeclaredType = type;
            var outerTypeParameters = GetOuterTypeParametersOfClassOrInterface(symbol);
            var localTypeParameters = GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol);
            // A class or interface is generic if it has type parameters or a "this" type. We always give classes a "this" type
            // because it is not feasible to analyze all members to determine if the "this" type escapes the class (in particular,
            // property types inferred from initializers and method return types inferred from return statements are very hard
            // to exhaustively analyze). We give interfaces a "this" type if we can't definitely determine that they are free of
            // "this" references.
            if (outerTypeParameters is not null || localTypeParameters is not null || kind == ObjectFlags.Class || !IsThislessInterface(symbol))
            {
                Types.Record(type).ObjectFlags |= ObjectFlags.Reference;
                var data = Types.Object(type);
                var all = new List<Type>();
                if (outerTypeParameters is not null)
                {
                    all.AddRange(outerTypeParameters);
                }
                if (localTypeParameters is not null)
                {
                    all.AddRange(localTypeParameters);
                }
                data.HasTypeParameters = outerTypeParameters is not null || localTypeParameters is not null;
                data.TypeParameters = Types.CreateList(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(all));
                data.OuterTypeParameters = outerTypeParameters is null ? TypeList.Empty : Types.CreateList(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(outerTypeParameters));
                data.LocalTypeParameters = localTypeParameters is null ? TypeList.Empty : Types.CreateList(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(localTypeParameters));
                data.Instantiations = new Dictionary<string, Type>(StringComparer.Ordinal)
                {
                    [GetTypeListId(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(all))] = type,
                };
                data.Target = type;
                data.ResolvedTypeArguments = data.TypeParameters;
                data.HasResolvedTypeArguments = true;
                var thisType = Types.CreateTypeParameter(symbol);
                data.ThisType = thisType;
                Types.TypeParameter(thisType).IsThisType = true;
                Types.TypeParameter(thisType).Constraint = type;
            }
        }
        return DeclaredTypeLinks.Get(symbol).DeclaredType;
    }

    /// <summary>The `this` type of a class or interface (InterfaceType.thisType; None if it has none).</summary>
    internal Type ThisTypeOfClassOrInterface(Type type) =>
        (Types.Flags(type) & TypeFlags.Object) != 0 ? Types.Object(type).ThisType : Type.None;

    // checker.ts getOuterTypeParametersOfClassOrInterface / getOuterTypeParameters (C1: the type parameters of enclosing
    // declarations collected syntactically; instantiation with them is C3).
    private List<Type>? GetOuterTypeParametersOfClassOrInterface(SymbolRef symbol)
    {
        var flags = SymbolFlagsOf(symbol);
        var declaration = NodeRef.None;
        if ((flags & (SymbolFlags.Class | SymbolFlags.Function)) != 0)
        {
            declaration = ValueDeclarationOf(symbol);
        }
        else
        {
            foreach (var d in DeclarationsOf(symbol) ?? [])
            {
                if (Kind(d) == SyntaxKind.InterfaceDeclaration)
                {
                    declaration = d;
                    break;
                }
            }
        }
        return declaration.IsNone ? null : GetOuterTypeParameters(declaration, includeThisTypes: false);
    }

    private List<Type>? GetOuterTypeParameters(NodeRef node, bool includeThisTypes)
    {
        while (true)
        {
            node = Parent(node);
            if (node.IsNone)
            {
                return null;
            }
            switch (Kind(node))
            {
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.CallSignature:
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.FunctionType:
                case SyntaxKind.ConstructorType:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ArrowFunction:
                case SyntaxKind.TypeAliasDeclaration:
                case SyntaxKind.MappedType:
                case SyntaxKind.ConditionalType:
                {
                    var outerTypeParameters = GetOuterTypeParameters(node, includeThisTypes);
                    var store = StoreOf(node);
                    if ((Kind(node) is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction ||
                        (Kind(node) == SyntaxKind.MethodDeclaration && store.Kind(store.Parent(node.Node)) == SyntaxKind.ObjectLiteralExpression)) && IsContextSensitive(node))
                    {
                        var signatures = GetSignaturesOfType(GetTypeOfSymbol(GetSymbolOfDeclaration(node)), SignatureKind.Call);
                        if (signatures.Count > 0 && Signatures.Record(signatures[0]).TypeParameters.Count > 0)
                        {
                            var result = outerTypeParameters is null ? new List<Type>() : [.. outerTypeParameters];
                            result.AddRange(Types.Elements(Signatures.Record(signatures[0]).TypeParameters));
                            return result;
                        }
                    }
                    if (Kind(node) == SyntaxKind.MappedType)
                    {
                        (outerTypeParameters ??= []).Add(GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(At(node, store.TypeParameter(node.Node)))));
                        return outerTypeParameters;
                    }
                    if (Kind(node) == SyntaxKind.ConditionalType)
                    {
                        var inferTypeParameters = GetInferTypeParameters(node);
                        if (inferTypeParameters is not null)
                        {
                            (outerTypeParameters ??= []).AddRange(inferTypeParameters);
                        }
                        return outerTypeParameters;
                    }
                    var typeParameters = store.TypeParameters(node.Node);
                    if (!typeParameters.IsNone && store.Length(typeParameters) > 0)
                    {
                        outerTypeParameters ??= [];
                        foreach (var tp in store.Nodes(typeParameters))
                        {
                            var declared = GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(At(node, tp)));
                            if (!outerTypeParameters.Contains(declared))
                            {
                                outerTypeParameters.Add(declared);
                            }
                        }
                    }
                    var kind = Kind(node);
                    var thisType = includeThisTypes && (kind is SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression or SyntaxKind.InterfaceDeclaration)
                        ? ThisTypeOfClassOrInterface(GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration(node)))
                        : Type.None;
                    if (!thisType.IsNone)
                    {
                        (outerTypeParameters ??= []).Add(thisType);
                    }
                    return outerTypeParameters;
                }
            }
        }
    }

    // checker.ts getLocalTypeParametersOfClassOrInterfaceOrTypeAlias
    private List<Type>? GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(SymbolRef symbol)
    {
        List<Type>? result = null;
        foreach (var node in DeclarationsOf(symbol) ?? [])
        {
            var kind = Kind(node);
            if (kind is SyntaxKind.InterfaceDeclaration or SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression or SyntaxKind.TypeAliasDeclaration)
            {
                var store = StoreOf(node);
                var typeParameters = store.TypeParameters(node.Node);
                if (typeParameters.IsNone)
                {
                    continue;
                }
                foreach (var tp in store.Nodes(typeParameters))
                {
                    var declared = GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(At(node, tp)));
                    result ??= [];
                    if (!result.Contains(declared))
                    {
                        result.Add(declared);
                    }
                }
            }
        }
        return result;
    }

    // checker.ts isThislessInterface
    private bool IsThislessInterface(SymbolRef symbol)
    {
        foreach (var declaration in DeclarationsOf(symbol) ?? [])
        {
            if (Kind(declaration) != SyntaxKind.InterfaceDeclaration)
            {
                continue;
            }
            var store = StoreOf(declaration);
            if ((store.Flags(declaration.Node) & NodeFlags.ContainsThis) != 0)
            {
                return false;
            }
            foreach (var node in GetInterfaceBaseTypeNodes(declaration))
            {
                var expression = store.Expression(node);
                if (Utilities.IsEntityNameExpression(store, expression))
                {
                    var baseSymbol = ResolveEntityName(At(declaration, expression), SymbolFlags.Type, ignoreErrors: true);
                    if (baseSymbol.IsNone || (SymbolFlagsOf(baseSymbol) & SymbolFlags.Interface) == 0 || !ThisTypeOfClassOrInterface(GetDeclaredTypeOfClassOrInterface(baseSymbol)).IsNone)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    // utilities.ts getInterfaceBaseTypeNodes (the expressions of the extends clause)
    private List<Node> GetInterfaceBaseTypeNodes(NodeRef declaration)
    {
        var result = new List<Node>();
        var store = StoreOf(declaration);
        var clauses = store.HeritageClauses(declaration.Node);
        if (clauses.IsNone)
        {
            return result;
        }
        foreach (var clause in store.Nodes(clauses))
        {
            if (store.Token(clause) == SyntaxKind.ExtendsKeyword)
            {
                result.AddRange(store.Nodes(store.Types(clause)));
            }
        }
        return result;
    }

    // utilities.ts getEffectiveBaseTypeNode / getClassExtendsHeritageElement
    private NodeRef GetBaseTypeNodeOfClass(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var clauses = store.HeritageClauses(declaration.Node);
        if (clauses.IsNone)
        {
            return NodeRef.None;
        }
        foreach (var clause in store.Nodes(clauses))
        {
            if (store.Token(clause) == SyntaxKind.ExtendsKeyword)
            {
                var types = store.Types(clause);
                return store.Length(types) > 0 ? At(declaration, store.Element(types, 0)) : NodeRef.None;
            }
        }
        return NodeRef.None;
    }

    /// <summary>checker.ts getDeclaredTypeOfTypeAlias.</summary>
    private Type GetDeclaredTypeOfTypeAlias(SymbolRef symbol)
    {
        if (!DeclaredTypeLinks.Get(symbol).DeclaredType.IsNone)
        {
            return DeclaredTypeLinks.Get(symbol).DeclaredType;
        }
        if ((TransientCheckFlags(symbol) & CheckFlags.Unresolved) != 0)
        {
            return unresolvedType;
        }
        if (!PushTypeResolution(symbol, DeclaredTypeProperty))
        {
            return errorType;
        }
        var declaration = NodeRef.None;
        foreach (var d in DeclarationsOf(symbol) ?? [])
        {
            if (Kind(d) is SyntaxKind.TypeAliasDeclaration or SyntaxKind.JSDocTypedefTag or SyntaxKind.JSDocCallbackTag or SyntaxKind.JSDocEnumTag)
            {
                declaration = d;
                break;
            }
        }
        var store = StoreOf(declaration);
        var typeNode = Kind(declaration) == SyntaxKind.TypeAliasDeclaration ? store.Type(declaration.Node) : store.TypeExpression(declaration.Node);
        // If typeNode is missing, we will error in checkJSDocTypedefTag.
        var type = !typeNode.IsNone ? GetTypeFromTypeNode(At(declaration, typeNode)) : errorType;
        if (PopTypeResolution())
        {
            InitializeTypeAliasInstantiations(symbol, type);
            if (type == intrinsicMarkerType && NameOf(symbol) == "BuiltinIteratorReturn")
            {
                type = GetBuiltinIteratorReturnType();
            }
        }
        else
        {
            type = errorType;
            var name = store.Name(declaration.Node);
            Error(!name.IsNone ? At(declaration, name) : declaration, Diagnostics.Type_alias_0_circularly_references_itself, SymbolToString(symbol));
        }
        ref var links = ref DeclaredTypeLinks.Get(symbol);
        if (links.DeclaredType.IsNone)
        {
            links.DeclaredType = type;
        }
        return links.DeclaredType;
    }

    private CheckFlags TransientCheckFlags(SymbolRef symbol) => symbol.IsTransient ? TransientSymbols.Record(symbol).CheckFlags : CheckFlags.None;

    /// <summary>checker.ts getDeclaredTypeOfEnum (member values: computeEnumMemberValues, module C8).</summary>
    private Type GetDeclaredTypeOfEnum(SymbolRef symbol)
    {
        if (!DeclaredTypeLinks.Get(symbol).DeclaredType.IsNone)
        {
            return DeclaredTypeLinks.Get(symbol).DeclaredType;
        }
        var memberTypeList = new List<Type>();
        var enumId = EnumIdOf(symbol);
        foreach (var declaration in DeclarationsOf(symbol) ?? [])
        {
            if (Kind(declaration) != SyntaxKind.EnumDeclaration)
            {
                continue;
            }
            var store = StoreOf(declaration);
            foreach (var member in store.Nodes(store.Members(declaration.Node)))
            {
                var memberRef = At(declaration, member);
                if (!HasBindableName(memberRef))
                {
                    continue;
                }
                var memberSymbol = GetSymbolOfDeclaration(memberRef);
                var value = GetEnumMemberValue(memberRef);
                var memberType = GetFreshTypeOfLiteralType(value is not null ? GetEnumLiteralType(value, enumId, memberSymbol) : CreateComputedEnumType(memberSymbol));
                DeclaredTypeLinks.Get(memberSymbol).DeclaredType = memberType;
                memberTypeList.Add(GetRegularTypeOfLiteralType(memberType));
            }
        }
        var enumType = memberTypeList.Count > 0
            ? GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(memberTypeList), UnionReduction.Literal, symbol)
            : CreateComputedEnumType(symbol);
        if ((Types.Flags(enumType) & TypeFlags.Union) != 0)
        {
            ref var record = ref Types.Record(enumType);
            record.Flags |= TypeFlags.EnumLiteral;
            record.Symbol = symbol;
        }
        DeclaredTypeLinks.Get(symbol).DeclaredType = enumType;
        return enumType;
    }

    private readonly Dictionary<SymbolRef, int> enumIds = [];

    // getSymbolId(symbol) for enum literal cache keys: per checker, in first-use order.
    private int EnumIdOf(SymbolRef symbol)
    {
        if (!enumIds.TryGetValue(symbol, out var id))
        {
            enumIds[symbol] = id = enumIds.Count + 1;
        }
        return id;
    }

    // checker.ts createComputedEnumType
    private Type CreateComputedEnumType(SymbolRef symbol)
    {
        var regularType = Types.CreateLiteral(TypeFlags.Enum, null, symbol, Type.None);
        var freshType = Types.CreateLiteral(TypeFlags.Enum, null, symbol, regularType);
        Types.Literal(regularType).FreshType = freshType;
        Types.Literal(freshType).FreshType = freshType;
        return regularType;
    }

    private Type GetDeclaredTypeOfEnumMember(SymbolRef symbol)
    {
        if (DeclaredTypeLinks.Get(symbol).DeclaredType.IsNone)
        {
            var enumType = GetDeclaredTypeOfEnum(GetParentOfSymbol(symbol));
            if (DeclaredTypeLinks.Get(symbol).DeclaredType.IsNone)
            {
                DeclaredTypeLinks.Get(symbol).DeclaredType = enumType;
            }
        }
        return DeclaredTypeLinks.Get(symbol).DeclaredType;
    }

    /// <summary>checker.ts getDeclaredTypeOfTypeParameter.</summary>
    internal Type GetDeclaredTypeOfTypeParameter(SymbolRef symbol)
    {
        ref var links = ref DeclaredTypeLinks.Get(symbol);
        if (links.DeclaredType.IsNone)
        {
            var type = Types.CreateTypeParameter(symbol);
            DeclaredTypeLinks.Get(symbol).DeclaredType = type;
        }
        return DeclaredTypeLinks.Get(symbol).DeclaredType;
    }

    private Type GetDeclaredTypeOfAlias(SymbolRef symbol)
    {
        if (DeclaredTypeLinks.Get(symbol).DeclaredType.IsNone)
        {
            var type = GetDeclaredTypeOfSymbol(ResolveAlias(symbol));
            DeclaredTypeLinks.Get(symbol).DeclaredType = type;
        }
        return DeclaredTypeLinks.Get(symbol).DeclaredType;
    }

    // ---- base types ----

    /// <summary>checker.ts getBaseTypes.</summary>
    internal List<Type> GetBaseTypes(Type type)
    {
        if ((Types.ObjectFlagsOf(type) & (ObjectFlags.ClassOrInterface | ObjectFlags.Reference)) == 0)
        {
            return [];
        }
        var data = Types.Object(type);
        if (!data.BaseTypesResolved)
        {
            if (PushTypeResolution(type, ResolvedBaseTypesProperty))
            {
                data.ResolvedBaseTypes ??= [];
                var symbol = Types.Symbol(type);
                var flags = symbol.IsNone ? SymbolFlags.None : SymbolFlagsOf(symbol);
                if ((Types.ObjectFlagsOf(type) & ObjectFlags.Tuple) != 0)
                {
                    data.ResolvedBaseTypes = [GetTupleBaseType(type)];
                }
                else
                {
                    if ((flags & SymbolFlags.Class) != 0)
                    {
                        ResolveBaseTypesOfClass(type, data);
                    }
                    if ((flags & SymbolFlags.Interface) != 0)
                    {
                        ResolveBaseTypesOfInterface(type, data);
                    }
                }
                if (!PopTypeResolution())
                {
                    foreach (var declaration in DeclarationsOf(symbol) ?? [])
                    {
                        if (Kind(declaration) is SyntaxKind.ClassDeclaration or SyntaxKind.InterfaceDeclaration)
                        {
                            // reportCircularBaseType
                            Error(declaration, Diagnostics.Type_0_recursively_references_itself_as_a_base_type, TypeToString(type, NodeRef.None, TypeFormatFlags.WriteArrayAsGenericType));
                        }
                    }
                }
            }
            data.BaseTypesResolved = true;
        }
        return data.ResolvedBaseTypes ?? [];
    }

    // checker.ts resolveBaseTypesOfClass (module C3: with the instantiated constructors of the base expression)
    private void ResolveBaseTypesOfClass(Type type, ObjectTypeData data)
    {
        var baseConstructorType = GetApparentType(GetBaseConstructorTypeOfClass(type));
        if ((Types.Flags(baseConstructorType) & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.Any)) == 0)
        {
            data.ResolvedBaseTypes = [];
            return;
        }
        var declaration = ValueDeclarationOf(Types.Symbol(type));
        var baseTypeNode = GetBaseTypeNodeOfClass(declaration);
        Type baseType;
        var baseConstructorSymbol = Types.Symbol(baseConstructorType);
        var originalBaseType = !baseConstructorSymbol.IsNone ? GetDeclaredTypeOfSymbol(baseConstructorSymbol) : Type.None;
        if (!baseConstructorSymbol.IsNone && (SymbolFlagsOf(baseConstructorSymbol) & SymbolFlags.Class) != 0 && AreAllOuterTypeParametersApplied(originalBaseType))
        {
            // When base constructor type is a class with no captured type arguments we know that the constructors all have the same type parameters as the
            // class and all return the instance type of the class. There is no need for further checks and we can apply the
            // type arguments in the same manner as a type reference to get the same error reporting experience.
            baseType = GetTypeFromClassOrInterfaceReference(baseTypeNode, baseConstructorSymbol);
        }
        else if ((Types.Flags(baseConstructorType) & TypeFlags.Any) != 0)
        {
            baseType = baseConstructorType;
        }
        else
        {
            // The class derives from a "class-like" constructor function, check that we have at least one construct signature
            // with a matching number of type parameters and use the return type of the first instantiated signature. Elsewhere
            // we check that all instantiated signatures return the same type.
            var constructors = GetInstantiatedConstructorsForTypeArguments(baseConstructorType, baseTypeNode);
            if (constructors.Count == 0)
            {
                Error(At(baseTypeNode, StoreOf(baseTypeNode).Expression(baseTypeNode.Node)), Diagnostics.No_base_constructor_has_the_specified_number_of_type_arguments);
                data.ResolvedBaseTypes = [];
                return;
            }
            baseType = GetReturnTypeOfSignature(constructors[0]);
        }
        if (IsErrorType(baseType))
        {
            data.ResolvedBaseTypes = [];
            return;
        }
        var reducedBaseType = GetReducedType(baseType);
        if (!IsValidBaseType(reducedBaseType))
        {
            var elaboration = ElaborateNeverIntersection(null, baseType); // module C4
            var diagnostic = ChainDiagnosticMessages(elaboration, Diagnostics.Base_constructor_return_type_0_is_not_an_object_type_or_intersection_of_object_types_with_statically_known_members, TypeToString(reducedBaseType));
            diagnostics.Add(CreateDiagnosticForNodeFromMessageChain(At(baseTypeNode, StoreOf(baseTypeNode).Expression(baseTypeNode.Node)), diagnostic, null));
            data.ResolvedBaseTypes = [];
            return;
        }
        if (type == reducedBaseType || HasBaseType(reducedBaseType, type))
        {
            Error(ValueDeclarationOf(Types.Symbol(type)), Diagnostics.Type_0_recursively_references_itself_as_a_base_type, TypeToString(type, NodeRef.None, TypeFormatFlags.WriteArrayAsGenericType));
            data.ResolvedBaseTypes = [];
            return;
        }
        data.ResolvedBaseTypes = [reducedBaseType];
    }

    // checker.ts areAllOuterTypeParametersApplied
    private bool AreAllOuterTypeParametersApplied(Type type)
    {
        // An unapplied type parameter has its symbol still the same as the matching argument symbol.
        // Since parameters are applied outer-to-inner, only the last outer parameter needs to be checked.
        if ((Types.Flags(type) & TypeFlags.Object) == 0)
        {
            return true;
        }
        var outerTypeParameters = Types.Object(type).OuterTypeParameters;
        if (outerTypeParameters.Count > 0)
        {
            var last = outerTypeParameters.Count - 1;
            var typeArguments = GetTypeArguments(type);
            return Types.Symbol(Types.Elements(outerTypeParameters)[last]) != Types.Symbol(typeArguments[last]);
        }
        return true;
    }

    // checker.ts getInstantiatedConstructorsForTypeArguments / getConstructorsForTypeArguments
    private List<Signature> GetInstantiatedConstructorsForTypeArguments(Type type, NodeRef baseTypeNode)
    {
        var store = StoreOf(baseTypeNode);
        var typeArgumentNodes = store.TypeArguments(baseTypeNode.Node);
        var typeArgCount = typeArgumentNodes.IsNone ? 0 : store.Length(typeArgumentNodes);
        var isJavascript = Utilities.IsInJSFile(store, baseTypeNode.Node);
        var result = new List<Signature>();
        Type[]? typeArguments = null;
        foreach (var sig in GetSignaturesOfType(type, SignatureKind.Construct))
        {
            var typeParameters = Types.Elements(Signatures.Record(sig).TypeParameters).ToArray();
            if ((isJavascript || typeArgCount >= GetMinTypeArgumentCount(typeParameters)) && typeArgCount <= typeParameters.Length)
            {
                typeArguments ??= TypeArgumentsFromTypeReferenceNode(baseTypeNode);
                result.Add(typeParameters.Length > 0 ? GetSignatureInstantiation(sig, typeArguments, isJavascript) : sig);
            }
        }
        return result;
    }

    private void ResolveBaseTypesOfInterface(Type type, ObjectTypeData data)
    {
        foreach (var declaration in DeclarationsOf(Types.Symbol(type)) ?? [])
        {
            if (Kind(declaration) != SyntaxKind.InterfaceDeclaration)
            {
                continue;
            }
            foreach (var node in GetInterfaceBaseTypeNodes(declaration))
            {
                var baseType = GetReducedType(GetTypeFromTypeNode(At(declaration, node)));
                if (IsErrorType(baseType))
                {
                    continue;
                }
                if (IsValidBaseType(baseType))
                {
                    if (type != baseType && !HasBaseType(baseType, type))
                    {
                        data.ResolvedBaseTypes!.Add(baseType);
                    }
                    else
                    {
                        // reportCircularBaseType
                        Error(declaration, Diagnostics.Type_0_recursively_references_itself_as_a_base_type, TypeToString(type, NodeRef.None, TypeFormatFlags.WriteArrayAsGenericType));
                    }
                }
                else
                {
                    Error(At(declaration, node), Diagnostics.An_interface_can_only_extend_an_object_type_or_intersection_of_object_types_with_statically_known_members);
                }
            }
        }
    }

    private bool IsValidBaseType(Type type)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Intersection) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(type).Types))
            {
                if (!IsValidBaseType(t))
                {
                    return false;
                }
            }
            return true;
        }
        return (flags & (TypeFlags.Object | TypeFlags.NonPrimitive | TypeFlags.Any)) != 0;
    }

    private bool HasBaseType(Type type, Type checkBase)
    {
        var visited = new HashSet<Type>();
        return Check(type);

        bool Check(Type t)
        {
            if (!visited.Add(t))
            {
                return false;
            }
            if ((Types.ObjectFlagsOf(t) & (ObjectFlags.ClassOrInterface | ObjectFlags.Reference)) != 0)
            {
                var target = GetTargetType(t);
                if (target == checkBase)
                {
                    return true;
                }
                foreach (var b in GetBaseTypes(target))
                {
                    if (Check(b))
                    {
                        return true;
                    }
                }
            }
            else if ((Types.Flags(t) & TypeFlags.Intersection) != 0)
            {
                foreach (var e in Types.Elements(Types.Union(t).Types))
                {
                    if (Check(e))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    // checker.ts getTargetType
    private Type GetTargetType(Type type) =>
        (Types.ObjectFlagsOf(type) & ObjectFlags.Reference) != 0 && !Types.Object(type).Target.IsNone ? Types.Object(type).Target : type;

    // ---- members ----

    /// <summary>checker.ts resolveDeclaredMembers.</summary>
    private ObjectTypeData ResolveDeclaredMembers(Type type)
    {
        var data = Types.Object(type);
        if (!data.DeclaredMembersResolved)
        {
            // (as resolveDeclaredMembers' `!type.declaredProperties`: a recursive call while the late-bound members are
            // resolved computes the members again)
            var symbol = Types.Symbol(type);
            var members = GetMembersOfSymbol(symbol);
            if (data.DeclaredMembersResolved)
            {
                return data;
            }
            data.DeclaredMembersResolved = true;
            data.DeclaredProperties = GetNamedMembers(members);
            // Start with signatures at empty array in case of recursive types
            data.DeclaredCallSignatures = [];
            data.DeclaredConstructSignatures = [];
            data.DeclaredIndexInfos = [];
            data.DeclaredCallSignatures = GetSignaturesOfSymbol(members.Get(InternalSymbolName.Call));
            data.DeclaredConstructSignatures = GetSignaturesOfSymbol(members.Get(InternalSymbolName.New));
            data.DeclaredIndexInfos = GetIndexInfosOfSymbol(symbol);
        }
        return data;
    }

    /// <summary>checker.ts resolveStructuredTypeMembers.</summary>
    internal ObjectTypeData ResolveStructuredTypeMembers(Type type)
    {
        var data = Types.Object(type);
        if (!data.MembersResolved)
        {
            var objectFlags = Types.ObjectFlagsOf(type);
            if ((objectFlags & ObjectFlags.Reference) != 0)
            {
                ResolveTypeReferenceMembers(type);
            }
            else if ((objectFlags & ObjectFlags.ClassOrInterface) != 0)
            {
                // resolveClassOrInterfaceMembers
                ResolveObjectTypeMembers(type, type, ResolveDeclaredMembers(type), [], []);
            }
            else if ((objectFlags & ObjectFlags.ReverseMapped) != 0)
            {
                ResolveReverseMappedTypeMembers(type);
            }
            else if ((objectFlags & ObjectFlags.Anonymous) != 0)
            {
                ResolveAnonymousTypeMembers(type);
            }
            else if ((objectFlags & ObjectFlags.Mapped) != 0)
            {
                ResolveMappedTypeMembers(type);
            }
            else
            {
                SetStructuredTypeMembers(type, emptySymbols, null, null, null);
            }
        }
        return data;
    }

    // checker.ts addInheritedMembers
    private void AddInheritedMembers(SymTable symbols, List<SymbolRef> baseSymbols)
    {
        foreach (var b in baseSymbols)
        {
            if (IsStaticPrivateIdentifierProperty(b))
            {
                continue;
            }
            var derived = symbols.Get(NameOf(b));
            if (derived.IsNone)
            {
                symbols.Set(NameOf(b), b);
            }
            // (JavaScript assignment declarations that do not override: module C9)
        }
    }

    private bool IsStaticPrivateIdentifierProperty(SymbolRef s)
    {
        var valueDeclaration = ValueDeclarationOf(s);
        if (valueDeclaration.IsNone)
        {
            return false;
        }
        var store = StoreOf(valueDeclaration);
        var name = Utilities.DeclarationNameField(store, valueDeclaration.Node);
        return !name.IsNone && store.Kind(name) == SyntaxKind.PrivateIdentifier && Utilities.IsStatic(store, valueDeclaration.Node);
    }

    // checker.ts resolveAnonymousTypeMembers (instantiated anonymous types: C3)
    private void ResolveAnonymousTypeMembers(Type type)
    {
        var typeData = Types.Object(type);
        if (!typeData.Target.IsNone)
        {
            SetStructuredTypeMembers(type, emptySymbols, null, null, null);
            var mapper = typeData.Mapper!;
            var instantiatedMembers = CreateInstantiatedSymbolTable(GetPropertiesOfObjectType(typeData.Target), mapper, mappingThisOnly: false);
            var instantiatedCallSignatures = InstantiateSignatures(GetSignaturesOfType(typeData.Target, SignatureKind.Call), mapper);
            var instantiatedConstructSignatures = InstantiateSignatures(GetSignaturesOfType(typeData.Target, SignatureKind.Construct), mapper);
            var instantiatedIndexInfos = InstantiateIndexInfos(GetIndexInfosOfType(typeData.Target), mapper);
            SetStructuredTypeMembers(type, instantiatedMembers, instantiatedCallSignatures, instantiatedConstructSignatures, instantiatedIndexInfos);
            return;
        }
        var symbol = GetMergedSymbol(Types.Symbol(type));
        if (symbol.IsNone)
        {
            SetStructuredTypeMembers(type, emptySymbols, null, null, null);
            return;
        }
        var flags = SymbolFlagsOf(symbol);
        if ((flags & SymbolFlags.TypeLiteral) != 0)
        {
            SetStructuredTypeMembers(type, emptySymbols, null, null, null);
            var members = GetMembersOfSymbol(symbol);
            var callSignatures = GetSignaturesOfSymbol(members.Get(InternalSymbolName.Call));
            var constructSignatures = GetSignaturesOfSymbol(members.Get(InternalSymbolName.New));
            var indexInfos = GetIndexInfosOfSymbol(symbol);
            SetStructuredTypeMembers(type, members, callSignatures, constructSignatures, indexInfos);
            return;
        }
        // Combinations of function, class, enum and module (C1: and object literals, whose resolved type checkObjectLiteral
        // builds in TypeScript with the literal's members, TODO(C5))
        var exports = (flags & SymbolFlags.ObjectLiteral) != 0 ? GetMembersOfSymbol(symbol) : GetExportsOfSymbol(symbol);
        if (symbol == globalThisSymbol)
        {
            var varsOnly = new SymTable();
            for (var i = 0; i < exports.Count; i++)
            {
                var p = exports.ValueAt(i);
                var pFlags = SymbolFlagsOf(p);
                if ((pFlags & SymbolFlags.BlockScoped) == 0 &&
                    !((pFlags & SymbolFlags.ValueModule) != 0 && DeclarationsOf(p) is { Count: > 0 } ds && ds.TrueForAll(d => Utilities.IsAmbientModule(StoreOf(d), d.Node))))
                {
                    varsOnly.Set(NameOf(p), p);
                }
            }
            exports = varsOnly;
        }
        SetStructuredTypeMembers(type, exports, null, null, null);
        IndexInfo? baseConstructorIndexInfo = null;
        if ((flags & SymbolFlags.Class) != 0)
        {
            var classType = GetDeclaredTypeOfClassOrInterface(symbol);
            var baseConstructorType = GetBaseConstructorTypeOfClass(classType);
            if (baseConstructorType == anyType)
            {
                baseConstructorIndexInfo = new IndexInfo { KeyType = stringType, Type = anyType, IsReadonly = false }; // (module C5)
            }
            if ((Types.Flags(baseConstructorType) & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.TypeVariable)) != 0)
            {
                var combined = new SymTable();
                for (var i = 0; i < exports.Count; i++)
                {
                    var id = exports.NameAt(i);
                    if (IsNamedMember(exports.ValueAt(i), id) || id == InternalSymbolName.Index)
                    {
                        combined.Set(id, exports.ValueAt(i));
                    }
                }
                AddInheritedMembers(combined, GetPropertiesOfType(baseConstructorType));
                exports = combined;
            }
        }
        // (static index signatures of classes: C8; module C5: the index signatures of any-based classes and enums)
        var hasIndexSymbol = exports.Has(InternalSymbolName.Index);
        var anonymousIndexInfos = baseConstructorIndexInfo is not null && !hasIndexSymbol ? new List<IndexInfo> { baseConstructorIndexInfo } : null;
        if (!hasIndexSymbol && (flags & SymbolFlags.Enum) != 0 && ((Types.Flags(GetDeclaredTypeOfSymbol(symbol)) & TypeFlags.Enum) != 0 || HasNumberLikeProperty(exports)))
        {
            (anonymousIndexInfos ??= []).Add(enumNumberIndexInfo ??= new IndexInfo { KeyType = numberType, Type = stringType, IsReadonly = true });
        }
        SetStructuredTypeMembers(type, exports, null, null, anonymousIndexInfos);
        var data = Types.Object(type);
        // We resolve the members before computing the signatures because a signature may use
        // typeof with a qualified name expression that circularly references the type we are
        // in the process of resolving (see issue #6072).
        if ((flags & (SymbolFlags.Function | SymbolFlags.Method)) != 0)
        {
            data.CallSignatures = GetSignaturesOfSymbol(symbol);
        }
        // And likewise for construct signatures for classes
        if ((flags & SymbolFlags.Class) != 0)
        {
            var classType = GetDeclaredTypeOfClassOrInterface(symbol);
            var members = MembersOf(symbol);
            var constructSignatures = members is not null ? GetSignaturesOfSymbol(members.Get(InternalSymbolName.Constructor)) : [];
            if (constructSignatures.Count == 0)
            {
                constructSignatures = GetDefaultConstructSignatures(classType);
            }
            data.ConstructSignatures = constructSignatures;
        }
    }

    // checker.ts enumNumberIndexInfo
    private IndexInfo? enumNumberIndexInfo;

    // some(type.properties, prop => getTypeOfSymbol(prop).flags & NumberLike)
    private bool HasNumberLikeProperty(SymTable members)
    {
        for (var i = 0; i < members.Count; i++)
        {
            if ((Types.Flags(GetTypeOfSymbol(members.ValueAt(i))) & TypeFlags.NumberLike) != 0)
            {
                return true;
            }
        }
        return false;
    }

    private const int ResolvedBaseConstructorTypeProperty = 6;
    private const int ResolvedBaseTypesProperty = 3;

    /// <summary>
    /// checker.ts getBaseConstructorTypeOfClass: undefinedType without an extends clause, errorType after an error,
    /// nullType for `extends null`, anyType for an any-typed expression, or an object type with construct signatures.
    /// </summary>
    private Type GetBaseConstructorTypeOfClass(Type classType)
    {
        var data = Types.Object(classType);
        if (data.ResolvedBaseConstructorType.IsNone)
        {
            var declaration = ValueDeclarationOf(Types.Symbol(classType));
            var baseTypeNode = declaration.IsNone ? NodeRef.None : GetBaseTypeNodeOfClass(declaration);
            if (baseTypeNode.IsNone)
            {
                return data.ResolvedBaseConstructorType = undefinedType;
            }
            if (!PushTypeResolution(classType, ResolvedBaseConstructorTypeProperty))
            {
                return errorType;
            }
            var baseConstructorType = CheckExpression(At(baseTypeNode, StoreOf(baseTypeNode).Expression(baseTypeNode.Node)));
            // (JavaScript @extends tags: C9)
            if ((Types.Flags(baseConstructorType) & (TypeFlags.Object | TypeFlags.Intersection)) != 0)
            {
                // Resolving the members of a class requires us to resolve the base class of that class.
                // We force resolution here such that we catch circularities now.
                if ((Types.Flags(baseConstructorType) & TypeFlags.Object) != 0)
                {
                    ResolveStructuredTypeMembers(baseConstructorType);
                }
            }
            if (!PopTypeResolution())
            {
                Error(declaration, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_base_expression, SymbolToString(Types.Symbol(classType)));
                if (Types.Object(classType).ResolvedBaseConstructorType.IsNone)
                {
                    Types.Object(classType).ResolvedBaseConstructorType = errorType;
                }
                return Types.Object(classType).ResolvedBaseConstructorType;
            }
            if ((Types.Flags(baseConstructorType) & TypeFlags.Any) == 0 && baseConstructorType != nullWideningType && !IsConstructorType(baseConstructorType))
            {
                // (Type_0_is_not_a_constructor_function_type and its related information: C8)
                if (Types.Object(classType).ResolvedBaseConstructorType.IsNone)
                {
                    Types.Object(classType).ResolvedBaseConstructorType = errorType;
                }
                return Types.Object(classType).ResolvedBaseConstructorType;
            }
            if (Types.Object(classType).ResolvedBaseConstructorType.IsNone)
            {
                Types.Object(classType).ResolvedBaseConstructorType = baseConstructorType;
            }
        }
        return data.ResolvedBaseConstructorType;
    }

    // checker.ts isConstructorType
    private bool IsConstructorType(Type type)
    {
        if (GetSignaturesOfType(type, SignatureKind.Construct).Count > 0)
        {
            return true;
        }
        if ((Types.Flags(type) & TypeFlags.TypeVariable) != 0)
        {
            var constraint = GetBaseConstraintOfType(type);
            return !constraint.IsNone && IsMixinConstructorType(constraint);
        }
        return false;
    }

    // checker.ts isMixinConstructorType: a type with a single construct signature `new (...args: any[]) => X`
    private bool IsMixinConstructorType(Type type)
    {
        var signatures = GetSignaturesOfType(type, SignatureKind.Construct);
        if (signatures.Count == 1)
        {
            var s = signatures[0];
            ref var record = ref Signatures.Record(s);
            if (record.TypeParameters.Count == 0 && record.Parameters is { Length: 1 } && (record.Flags & SignatureFlags.HasRestParameter) != 0)
            {
                var paramType = GetTypeOfSymbol(record.Parameters[0]);
                return (Types.Flags(paramType) & TypeFlags.Any) != 0 || (IsArrayType(paramType) && (Types.Flags(GetTypeArguments(paramType)[0]) & TypeFlags.Any) != 0);
            }
        }
        return false;
    }

    // checker.ts getDefaultConstructSignatures (base class constructors: the base's construct signatures with the
    // class as return type, TODO(C3) instantiation)
    private List<Signature> GetDefaultConstructSignatures(Type classType)
    {
        var baseConstructorType = GetBaseConstructorTypeOfClass(classType);
        var baseSignatures = GetSignaturesOfType(baseConstructorType, SignatureKind.Construct);
        var declaration = ValueDeclarationOf(Types.Symbol(classType));
        var isAbstract = !declaration.IsNone && Utilities.HasSyntacticModifierUncached(StoreOf(declaration), declaration.Node, ModifierFlags.Abstract);
        if (baseSignatures.Count == 0)
        {
            var signature = Signatures.Create(isAbstract ? SignatureFlags.Abstract : SignatureFlags.None, NodeRef.None, Types.Object(classType).LocalTypeParameters, SymbolRef.None, 0);
            ref var record = ref Signatures.Record(signature);
            record.Parameters = [];
            record.ResolvedReturnType = classType;
            return [signature];
        }
        var baseTypeNode = GetBaseTypeNodeOfClass(declaration);
        var isJavaScript = Utilities.IsInJSFile(StoreOf(baseTypeNode), baseTypeNode.Node);
        var typeArguments = TypeArgumentsFromTypeReferenceNode(baseTypeNode);
        var typeArgCount = typeArguments.Length;
        var result = new List<Signature>();
        foreach (var baseSignature in baseSignatures)
        {
            var baseTypeParameters = Types.Elements(Signatures.Record(baseSignature).TypeParameters).ToArray();
            var minTypeArgumentCount = GetMinTypeArgumentCount(baseTypeParameters);
            var typeParamCount = baseTypeParameters.Length;
            if (isJavaScript || (typeArgCount >= minTypeArgumentCount && typeArgCount <= typeParamCount))
            {
                Signature sig;
                if (typeParamCount > 0)
                {
                    sig = CreateSignatureInstantiation(baseSignature, FillMissingTypeArguments(typeArguments, baseTypeParameters, minTypeArgumentCount, isJavaScript));
                }
                else
                {
                    // cloneSignature
                    ref var b = ref Signatures.Record(baseSignature);
                    sig = Signatures.Create(b.Flags & SignatureFlags.PropagatingFlags, b.Declaration, b.TypeParameters, b.ThisParameter, b.MinArgumentCount);
                    ref var c = ref Signatures.Record(sig);
                    c.Parameters = Signatures.Record(baseSignature).Parameters;
                    c.Target = Signatures.Record(baseSignature).Target;
                    c.Mapper = Signatures.Record(baseSignature).Mapper;
                }
                ref var r = ref Signatures.Record(sig);
                r.TypeParameters = Types.Object(classType).LocalTypeParameters;
                r.ResolvedReturnType = classType;
                r.Flags = isAbstract ? r.Flags | SignatureFlags.Abstract : r.Flags & ~SignatureFlags.Abstract;
                result.Add(sig);
            }
        }
        return result;
    }

    /// <summary>checker.ts getPropertiesOfObjectType.</summary>
    internal List<SymbolRef> GetPropertiesOfObjectType(Type type) =>
        (Types.Flags(type) & TypeFlags.Object) != 0 ? ResolveStructuredTypeMembers(type).Properties! : [];

    /// <summary>checker.ts getPropertyOfObjectType.</summary>
    internal SymbolRef GetPropertyOfObjectType(Type type, string name)
    {
        if ((Types.Flags(type) & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers(type);
            var symbol = resolved.Members!.Get(name);
            if (!symbol.IsNone && SymbolIsValue(symbol))
            {
                return symbol;
            }
        }
        return SymbolRef.None;
    }

    /// <summary>checker.ts getPropertiesOfType (unions and intersections: the members common to the constituents).</summary>
    internal List<SymbolRef> GetPropertiesOfType(Type type)
    {
        type = GetReducedApparentType(type);
        return (Types.Flags(type) & TypeFlags.UnionOrIntersection) != 0 ? GetPropertiesOfUnionOrIntersectionType(type) : GetPropertiesOfObjectType(type);
    }

    // (getApparentType: Checker.Generics.cs, module C3)

    private Type deferredGlobalBigIntType;
    private Type deferredGlobalESSymbolType;

    private Type GetGlobalBigIntType()
    {
        if (deferredGlobalBigIntType.IsNone)
        {
            var t = GetGlobalType("BigInt", 0, reportErrors: false);
            deferredGlobalBigIntType = t.IsNone ? emptyObjectType : t;
        }
        return deferredGlobalBigIntType;
    }

    private Type GetGlobalESSymbolType()
    {
        if (deferredGlobalESSymbolType.IsNone)
        {
            var t = GetGlobalType("Symbol", 0, reportErrors: false);
            deferredGlobalESSymbolType = t.IsNone ? emptyObjectType : t;
        }
        return deferredGlobalESSymbolType;
    }

    // (getConstraintOfTypeParameter: Checker.Instantiate.cs, module C3)

    /// <summary>checker.ts getPropertyOfType.</summary>
    internal SymbolRef GetPropertyOfType(Type type, string name, bool skipObjectFunctionPropertyAugment = false)
    {
        if (type.IsNone)
        {
            return SymbolRef.None;
        }
        type = GetReducedApparentType(type);
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers(type);
            var symbol = resolved.Members!.Get(name);
            if (!symbol.IsNone && SymbolIsValue(symbol))
            {
                return symbol;
            }
            if (skipObjectFunctionPropertyAugment)
            {
                return SymbolRef.None;
            }
            var functionType = type == anyFunctionType ? globalFunctionType
                : resolved.CallSignatures!.Count > 0 ? globalCallableFunctionType
                : resolved.ConstructSignatures!.Count > 0 ? globalNewableFunctionType
                : Type.None;
            if (!functionType.IsNone)
            {
                var fromFunction = GetPropertyOfObjectType(functionType, name);
                if (!fromFunction.IsNone)
                {
                    return fromFunction;
                }
            }
            return GetPropertyOfObjectType(globalObjectType, name);
        }
        if ((flags & TypeFlags.Intersection) != 0)
        {
            var prop = GetPropertyOfUnionOrIntersectionType(type, name, skipObjectFunctionPropertyAugment: true);
            if (!prop.IsNone)
            {
                return prop;
            }
            if (!skipObjectFunctionPropertyAugment)
            {
                return GetPropertyOfUnionOrIntersectionType(type, name, skipObjectFunctionPropertyAugment);
            }
            return SymbolRef.None;
        }
        if ((flags & TypeFlags.Union) != 0)
        {
            return GetPropertyOfUnionOrIntersectionType(type, name, skipObjectFunctionPropertyAugment);
        }
        return SymbolRef.None;
    }

    // (getPropertyOfUnionOrIntersectionType / createUnionOrIntersectionProperty: Checker.UnionProperties.cs, module C4)

    /// <summary>checker.ts getSignaturesOfType.</summary>
    internal List<Signature> GetSignaturesOfType(Type type, SignatureKind kind)
    {
        type = GetApparentType(type);
        if ((Types.Flags(type) & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers(type);
            return kind == SignatureKind.Call ? resolved.CallSignatures! : resolved.ConstructSignatures!;
        }
        if ((Types.Flags(type) & TypeFlags.UnionOrIntersection) != 0)
        {
            // resolveUnionTypeMembers / resolveIntersectionTypeMembers (module C5)
            var resolved = ResolveUnionOrIntersectionTypeMembers(type);
            return kind == SignatureKind.Call ? resolved.CallSignatures! : resolved.ConstructSignatures!;
        }
        return [];
    }

    /// <summary>checker.ts getIndexInfosOfType.</summary>
    internal List<IndexInfo> GetIndexInfosOfType(Type type)
    {
        type = GetApparentType(type);
        return (Types.Flags(type) & TypeFlags.Object) != 0 ? ResolveStructuredTypeMembers(type).IndexInfos!
            : (Types.Flags(type) & TypeFlags.UnionOrIntersection) != 0 ? ResolveUnionOrIntersectionTypeMembers(type).IndexInfos!
            : [];
    }

    /// <summary>checker.ts getIndexInfosOfSymbol (getIndexInfosOfIndexSymbol for the symbol's __index member).</summary>
    private List<IndexInfo> GetIndexInfosOfSymbol(SymbolRef symbol)
    {
        var indexSymbol = GetMembersOfSymbol(symbol).Get(InternalSymbolName.Index);
        if (indexSymbol.IsNone)
        {
            return [];
        }
        var result = new List<IndexInfo>();
        foreach (var declaration in DeclarationsOf(indexSymbol) ?? [])
        {
            var store = StoreOf(declaration);
            var parameters = store.Parameters(declaration.Node);
            if (parameters.IsNone || store.Length(parameters) != 1)
            {
                continue;
            }
            var parameter = store.Element(parameters, 0);
            var typeNode = store.Type(parameter);
            if (typeNode.IsNone)
            {
                continue;
            }
            var keyType = GetTypeFromTypeNode(At(declaration, typeNode));
            var valueTypeNode = store.Type(declaration.Node);
            var valueType = valueTypeNode.IsNone ? anyType : GetTypeFromTypeNode(At(declaration, valueTypeNode));
            var isReadonly = Utilities.HasSyntacticModifierUncached(store, declaration.Node, ModifierFlags.Readonly);
            foreach (var k in (Types.Flags(keyType) & TypeFlags.Union) != 0 ? Types.Elements(Types.Union(keyType).Types).ToArray() : [keyType])
            {
                if (!result.Exists(i => i.KeyType == k))
                {
                    result.Add(new IndexInfo { KeyType = k, Type = valueType, IsReadonly = isReadonly, Declaration = declaration });
                }
            }
        }
        return result;
    }

    // ---- signatures ----

    /// <summary>checker.ts getSignaturesOfSymbol.</summary>
    internal List<Signature> GetSignaturesOfSymbol(SymbolRef symbol)
    {
        var result = new List<Signature>();
        var declarations = symbol.IsNone ? null : DeclarationsOf(symbol);
        if (declarations is null)
        {
            return result;
        }
        for (var i = 0; i < declarations.Count; i++)
        {
            var decl = declarations[i];
            var store = StoreOf(decl);
            if (!Utilities.IsFunctionLike(store, decl.Node))
            {
                continue;
            }
            // Don't include signature if node is the implementation of an overloaded function. A node is considered
            // an implementation node if it has a body and the previous node is of the same kind and immediately
            // precedes the implementation node (i.e. has the same parent and ends where the implementation starts).
            if (i > 0 && !store.Body(decl.Node).IsNone)
            {
                var previous = declarations[i - 1];
                if (previous.File == decl.File && store.Parent(decl.Node) == store.Parent(previous.Node) && store.Kind(decl.Node) == store.Kind(previous.Node)
                    && store.Pos(decl.Node) == store.End(previous.Node))
                {
                    continue;
                }
            }
            result.Add(GetSignatureFromDeclaration(decl));
        }
        return result;
    }

    /// <summary>checker.ts getSignatureFromDeclaration.</summary>
    internal Signature GetSignatureFromDeclaration(NodeRef declaration)
    {
        ref var links = ref SignatureLinks.Get(declaration);
        if (!links.ResolvedSignature.IsNone)
        {
            return links.ResolvedSignature;
        }
        var store = StoreOf(declaration);
        var parameters = new List<SymbolRef>();
        var flags = SignatureFlags.None;
        var minArgumentCount = 0;
        var thisParameter = SymbolRef.None;
        var iife = Utilities.GetImmediatelyInvokedFunctionExpression(store, declaration.Node);
        var parameterNodes = store.Parameters(declaration.Node);
        var count = parameterNodes.IsNone ? 0 : store.Length(parameterNodes);
        for (var i = 0; i < count; i++)
        {
            var param = store.Element(parameterNodes, i);
            var paramRef = At(declaration, param);
            var paramSymbol = NodeSymbol(paramRef);
            var type = store.Type(param);
            // Include parameter symbol instead of property symbol in the signature
            if (!paramSymbol.IsNone && (SymbolFlagsOf(paramSymbol) & SymbolFlags.Property) != 0 && !Utilities.IsBindingPattern(store, store.Name(param)))
            {
                paramSymbol = ResolveName(paramRef, NameOf(paramSymbol), NodeRef.None, SymbolFlags.Value, null, isUse: false);
            }
            if (i == 0 && !paramSymbol.IsNone && NameOf(paramSymbol) == InternalSymbolName.This)
            {
                thisParameter = NodeSymbol(paramRef);
            }
            else
            {
                parameters.Add(paramSymbol);
            }
            if (!type.IsNone && store.Kind(type) == SyntaxKind.LiteralType)
            {
                flags |= SignatureFlags.HasLiteralTypes;
            }
            // Record a new minimum argument count if this is not an optional parameter
            var isOptionalParameter = !store.QuestionToken(param).IsNone || !store.Initializer(param).IsNone || !store.DotDotDotToken(param).IsNone ||
                (!iife.IsNone && parameters.Count > store.Length(store.Arguments(iife)) && type.IsNone);
            if (!isOptionalParameter)
            {
                minArgumentCount = parameters.Count;
            }
        }
        // (accessors' this parameters from the other accessor, JSDoc @this: C9)
        var typeParameters = TypeList.Empty;
        if (store.Kind(declaration.Node) == SyntaxKind.Constructor)
        {
            typeParameters = Types.Object(GetDeclaredTypeOfClassOrInterface(GetMergedSymbol(NodeSymbol(Parent(declaration))))).LocalTypeParameters;
        }
        else
        {
            var typeParameterNodes = store.TypeParameters(declaration.Node);
            if (!typeParameterNodes.IsNone && store.Length(typeParameterNodes) > 0)
            {
                var list = new List<Type>();
                foreach (var tp in store.Nodes(typeParameterNodes))
                {
                    list.Add(GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(At(declaration, tp))));
                }
                typeParameters = Types.CreateList(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list));
            }
        }
        if (count > 0 && !store.DotDotDotToken(store.Element(parameterNodes, count - 1)).IsNone)
        {
            flags |= SignatureFlags.HasRestParameter;
        }
        if ((store.Kind(declaration.Node) == SyntaxKind.ConstructorType && Utilities.HasSyntacticModifierUncached(store, declaration.Node, ModifierFlags.Abstract)) ||
            (store.Kind(declaration.Node) == SyntaxKind.Constructor && Utilities.HasSyntacticModifierUncached(store, store.Parent(declaration.Node), ModifierFlags.Abstract)))
        {
            flags |= SignatureFlags.Abstract;
        }
        var signature = Signatures.Create(flags, declaration, typeParameters, thisParameter, minArgumentCount);
        Signatures.Record(signature).Parameters = [.. parameters];
        SignatureLinks.Get(declaration).ResolvedSignature = signature;
        return signature;
    }

    /// <summary>checker.ts getReturnTypeOfSignature (TODO(C5): return types inferred from bodies).</summary>
    internal Type GetReturnTypeOfSignature(Signature signature)
    {
        ref var record = ref Signatures.Record(signature);
        if (!record.ResolvedReturnType.IsNone)
        {
            return record.ResolvedReturnType;
        }
        if (!PushTypeResolution(signature, ResolvedReturnTypeProperty))
        {
            return errorType;
        }
        var declaration = Signatures.Record(signature).Declaration;
        var signatureTarget = Signatures.Record(signature).Target;
        var compositeSignatures = Signatures.Record(signature).CompositeSignatures;
        var type = !signatureTarget.IsNone ? InstantiateType(GetReturnTypeOfSignature(signatureTarget), MapperAt(Signatures.Record(signature).Mapper))
            : compositeSignatures is not null ? InstantiateType(GetUnionOrIntersectionTypeOfReturnTypes(compositeSignatures, Signatures.Record(signature).CompositeKind), MapperAt(Signatures.Record(signature).Mapper))
            : declaration.IsNone ? anyType : GetReturnTypeFromAnnotation(declaration);
        if (type.IsNone)
        {
            var store = StoreOf(declaration);            var body = store.Body(declaration.Node);
            type = body.IsNone || Utilities.NodeIsMissing(store, body) ? anyType : GetReturnTypeFromBody(declaration);
        }
        if (!PopTypeResolution())
        {
            if (!declaration.IsNone)
            {
                var store = StoreOf(declaration);
                var typeNode = store.Type(declaration.Node);
                if (!typeNode.IsNone)
                {
                    Error(At(declaration, typeNode), Diagnostics.Return_type_annotation_circularly_references_itself);
                }
                else if (noImplicitAny)
                {
                    var name = GetNameOfDeclarationRef(declaration);
                    if (!name.IsNone)
                    {
                        Error(name, Diagnostics._0_implicitly_has_return_type_any_because_it_does_not_have_a_return_type_annotation_and_is_referenced_directly_or_indirectly_in_one_of_its_return_expressions,
                            Utilities.DeclarationNameToString(store, name.Node));
                    }
                    else
                    {
                        Error(declaration, Diagnostics.Function_implicitly_has_return_type_any_because_it_does_not_have_a_return_type_annotation_and_is_referenced_directly_or_indirectly_in_one_of_its_return_expressions);                    }
                }
            }
            type = anyType;
        }
        ref var after = ref Signatures.Record(signature);
        if (after.ResolvedReturnType.IsNone)
        {
            after.ResolvedReturnType = type;
        }
        return after.ResolvedReturnType;
    }

    private Type GetReturnTypeFromAnnotation(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        if (store.Kind(declaration.Node) == SyntaxKind.Constructor)
        {
            return GetDeclaredTypeOfClassOrInterface(GetMergedSymbol(NodeSymbol(Parent(declaration))));
        }
        var typeNode = store.Type(declaration.Node);
        if (!typeNode.IsNone)
        {
            return GetTypeFromTypeNode(At(declaration, typeNode));
        }
        if (store.Kind(declaration.Node) == SyntaxKind.GetAccessor)
        {
            var setter = GetDeclarationOfKind(GetSymbolOfDeclaration(declaration), SyntaxKind.SetAccessor);
            var setterType = GetAnnotatedAccessorType(setter);
            if (!setterType.IsNone)
            {
                return setterType;
            }
        }
        return Type.None;
    }

    // checker.ts getAnnotatedAccessorTypeNode !== undefined
    private bool HasAnnotatedAccessorTypeNode(NodeRef accessor)
    {
        if (accessor.IsNone)
        {
            return false;
        }
        var store = StoreOf(accessor);
        if (store.Kind(accessor.Node) != SyntaxKind.SetAccessor)
        {
            return !store.Type(accessor.Node).IsNone;
        }
        var parameters = store.Parameters(accessor.Node);
        if (parameters.IsNone || store.Length(parameters) == 0)
        {
            return false;
        }
        var first = store.Element(parameters, 0);
        if (store.Length(parameters) == 2 && Utilities.IsThisIdentifier(store, store.Name(first)))
        {
            first = store.Element(parameters, 1);
        }
        return !store.Type(first).IsNone;
    }

    private Type GetAnnotatedAccessorType(NodeRef accessor)
    {
        if (accessor.IsNone)
        {
            return Type.None;
        }
        var store = StoreOf(accessor);
        switch (store.Kind(accessor.Node))
        {
            case SyntaxKind.GetAccessor:
            case SyntaxKind.PropertyDeclaration:
            {
                var typeNode = store.Type(accessor.Node);
                return typeNode.IsNone ? Type.None : GetTypeFromTypeNode(At(accessor, typeNode));
            }
            case SyntaxKind.SetAccessor:
            {
                var parameters = store.Parameters(accessor.Node);
                if (parameters.IsNone || store.Length(parameters) == 0)
                {
                    return Type.None;
                }
                var first = store.Element(parameters, 0);
                if (store.Length(parameters) == 2 && Utilities.IsThisIdentifier(store, store.Name(first)))
                {
                    first = store.Element(parameters, 1);
                }
                var typeNode = store.Type(first);
                return typeNode.IsNone ? Type.None : GetTypeFromTypeNode(At(accessor, typeNode));
            }
        }
        return Type.None;
    }

    // ---- types of symbols ----

    /// <summary>checker.ts getTypeOfSymbol.</summary>
    internal Type GetTypeOfSymbol(SymbolRef symbol)
    {
        if (symbol.IsNone)
        {
            return errorType;
        }
        var checkFlags = GetCheckFlags(symbol);
        if ((checkFlags & CheckFlags.ReverseMapped) != 0)
        {
            return GetTypeOfReverseMappedSymbol(symbol);
        }
        if ((checkFlags & CheckFlags.DeferredType) != 0)
        {
            return GetTypeOfSymbolWithDeferredType(symbol);
        }
        if ((checkFlags & CheckFlags.Instantiated) != 0)
        {
            return GetTypeOfInstantiatedSymbol(symbol);
        }
        if ((checkFlags & CheckFlags.Mapped) != 0)
        {
            return GetTypeOfMappedSymbol(symbol);
        }
        var flags = SymbolFlagsOf(symbol);
        if ((flags & (SymbolFlags.Variable | SymbolFlags.Property)) != 0)
        {
            return GetTypeOfVariableOrParameterOrProperty(symbol);
        }
        if ((flags & (SymbolFlags.Function | SymbolFlags.Method | SymbolFlags.Class | SymbolFlags.Enum | SymbolFlags.ValueModule)) != 0)
        {
            return GetTypeOfFuncClassEnumModule(symbol);
        }
        if ((flags & SymbolFlags.EnumMember) != 0)
        {
            return GetTypeOfEnumMember(symbol);
        }
        if ((flags & SymbolFlags.Accessor) != 0)
        {
            return GetTypeOfAccessors(symbol);
        }
        if ((flags & SymbolFlags.Alias) != 0)
        {
            return GetTypeOfAlias(symbol);
        }
        return errorType;
    }

    private Type GetTypeOfVariableOrParameterOrProperty(SymbolRef symbol)
    {
        ref var links = ref ValueSymbolLinks.Get(symbol);
        if (!links.ResolvedType.IsNone)
        {
            return links.ResolvedType;
        }
        var type = GetTypeOfVariableOrParameterOrPropertyWorker(symbol);
        ref var after = ref ValueSymbolLinks.Get(symbol);
        if (after.ResolvedType.IsNone)
        {
            after.ResolvedType = type;
        }
        return after.ResolvedType;
    }

    private Type GetTypeOfVariableOrParameterOrPropertyWorker(SymbolRef symbol)
    {
        var flags = SymbolFlagsOf(symbol);
        // Handle prototype property
        if ((flags & SymbolFlags.Prototype) != 0)
        {
            return GetTypeOfPrototypeProperty(symbol);
        }
        if (symbol == requireSymbol)
        {
            return anyType;
        }
        var declaration = ValueDeclarationOf(symbol);
        if (declaration.IsNone)
        {
            return symbol.IsTransient && ValueSymbolLinks.Has(symbol) && !ValueSymbolLinks.Get(symbol).ResolvedType.IsNone ? ValueSymbolLinks.Get(symbol).ResolvedType : errorType;
        }
        var store = StoreOf(declaration);
        var kind = store.Kind(declaration.Node);
        if (kind == SyntaxKind.SourceFile)
        {
            // (JSON source files: checkExpression of the statement, C5)
            return GetTypeOfFuncClassEnumModule(symbol);
        }
        if (kind is SyntaxKind.GetAccessor or SyntaxKind.SetAccessor)
        {
            return GetTypeOfAccessors(symbol);
        }
        // Handle variable, parameter or property
        if (!PushTypeResolution(symbol, TypeProperty))
        {
            // Symbol is property of some kind that is merged with something - should use `getTypeOfFuncClassEnumModule` and not `getTypeOfVariableOrParameterOrProperty`
            if ((flags & SymbolFlags.ValueModule) != 0 && (flags & SymbolFlags.Assignment) == 0)
            {
                return GetTypeOfFuncClassEnumModule(symbol);
            }
            return ReportCircularityError(symbol);
        }
        Type type;
        switch (kind)
        {
            case SyntaxKind.ExportAssignment:
                type = WidenTypeForVariableLikeDeclaration(CheckExpressionCached(At(declaration, store.Expression(declaration.Node))), declaration, reportErrors: false);
                break;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.Identifier:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.MethodDeclaration when !Utilities.IsObjectLiteralMethod(store, declaration.Node):
                if ((flags & (SymbolFlags.Function | SymbolFlags.Method | SymbolFlags.Class | SymbolFlags.Enum | SymbolFlags.ValueModule)) != 0)
                {
                    PopTypeResolution();
                    return GetTypeOfFuncClassEnumModule(symbol);
                }
                type = anyType; // (assignment declarations: module C9)
                break;
            case SyntaxKind.PropertyAssignment:
            {
                var typeNode = store.Type(declaration.Node);
                type = !typeNode.IsNone ? GetTypeFromTypeNode(At(declaration, typeNode)) : CheckPropertyAssignment(declaration, CheckMode.Normal);
                break;
            }
            case SyntaxKind.ShorthandPropertyAssignment:
                type = CheckExpressionForMutableLocation(At(declaration, store.Name(declaration.Node)), CheckMode.Normal);
                break;
            case SyntaxKind.MethodDeclaration:
                // object literal methods: the function type of the method symbol
                type = GetTypeOfFuncClassEnumModule(symbol);
                break;
            case SyntaxKind.Parameter:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.BindingElement:
                type = GetWidenedTypeForVariableLikeDeclaration(declaration, reportErrors: true);
                break;
            case SyntaxKind.EnumDeclaration:
                type = GetTypeOfFuncClassEnumModule(symbol);
                break;
            case SyntaxKind.EnumMember:
                type = GetTypeOfEnumMember(symbol);
                break;
            default:
                type = errorType;
                break;
        }
        if (!PopTypeResolution())
        {
            // Symbol is property of some kind that is merged with something - should use `getTypeOfFuncClassEnumModule` and not `getTypeOfVariableOrParameterOrProperty`
            if ((flags & SymbolFlags.ValueModule) != 0 && (flags & SymbolFlags.Assignment) == 0)
            {
                return GetTypeOfFuncClassEnumModule(symbol);
            }
            return ReportCircularityError(symbol);
        }
        return type;
    }

    // checker.ts getTypeOfPrototypeProperty: the class instance type with any for its type parameters
    private Type GetTypeOfPrototypeProperty(SymbolRef prototype)
    {
        // TypeScript 1.0 spec (April 2014): 8.4
        // Every class automatically contains a static property member named 'prototype',
        // the type of which is an instantiation of the class type with type Any supplied as a type argument for each type parameter.
        // It is an error to explicitly declare a static property member with the name 'prototype'.
        var classType = GetDeclaredTypeOfClassOrInterface(GetParentOfSymbol(prototype));
        if ((Types.Flags(classType) & TypeFlags.Object) != 0 && Types.Object(classType).HasTypeParameters)
        {
            var typeParameters = Types.Object(classType).TypeParameters;
            var arguments = new Type[typeParameters.Count];
            Array.Fill(arguments, anyType);
            return CreateTypeReference(classType, arguments);
        }
        return classType;
    }

    private Type ReportCircularityError(SymbolRef symbol)
    {
        var declaration = ValueDeclarationOf(symbol);
        // Check if variable has type annotation that circularly references the variable itself
        if (!declaration.IsNone)
        {
            var store = StoreOf(declaration);
            if (!GetEffectiveTypeAnnotationNode(declaration).IsNone)
            {
                Error(declaration, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, SymbolToString(symbol));
                return errorType;
            }
            // Check if variable has initializer that circularly references the variable itself
            if (noImplicitAny && (store.Kind(declaration.Node) != SyntaxKind.Parameter || !store.Initializer(declaration.Node).IsNone))
            {
                Error(declaration, Diagnostics._0_implicitly_has_type_any_because_it_does_not_have_a_type_annotation_and_is_referenced_directly_or_indirectly_in_its_own_initializer, SymbolToString(symbol));
            }
        }
        else if ((SymbolFlagsOf(symbol) & SymbolFlags.Alias) != 0)
        {
            var node = GetDeclarationOfAliasSymbol(symbol);
            if (!node.IsNone)
            {
                Error(node, Diagnostics.Circular_definition_of_import_alias_0, SymbolToString(symbol));
            }
        }
        return anyType;
    }

    /// <summary>utilities.ts getEffectiveTypeAnnotationNode (the TypeScript part; JSDoc types: C9).</summary>
    internal NodeRef GetEffectiveTypeAnnotationNode(NodeRef node)
    {
        var store = StoreOf(node);
        var kind = store.Kind(node.Node);
        if (kind is SyntaxKind.VariableDeclaration or SyntaxKind.Parameter or SyntaxKind.PropertyDeclaration or SyntaxKind.PropertySignature
            or SyntaxKind.BindingElement or SyntaxKind.PropertyAssignment or SyntaxKind.TypeAliasDeclaration or SyntaxKind.ExportAssignment)
        {
            var type = kind is SyntaxKind.BindingElement or SyntaxKind.ExportAssignment ? Node.None : store.Type(node.Node);
            return At(node, type);
        }
        return NodeRef.None;
    }

    private Type GetTypeOfFuncClassEnumModule(SymbolRef symbol)
    {
        ref var links = ref ValueSymbolLinks.Get(symbol);
        if (links.ResolvedType.IsNone)
        {
            var type = GetTypeOfFuncClassEnumModuleWorker(symbol);
            ValueSymbolLinks.Get(symbol).ResolvedType = type;
        }
        return ValueSymbolLinks.Get(symbol).ResolvedType;
    }

    private Type GetTypeOfFuncClassEnumModuleWorker(SymbolRef symbol)
    {
        var flags = SymbolFlagsOf(symbol);
        if ((flags & SymbolFlags.Module) != 0 && IsShorthandAmbientModuleSymbol(symbol))
        {
            return anyType;
        }
        // (JavaScript assignment declarations and CommonJS modules: C9)
        var type = CreateObjectType(ObjectFlags.Anonymous, symbol);
        if ((flags & SymbolFlags.Class) != 0)
        {
            // (getBaseTypeVariableOfClass: mixins, C5)
            return type;
        }
        return strictNullChecks && (flags & SymbolFlags.Optional) != 0 ? GetOptionalType(type, isProperty: true) : type;
    }

    private Type GetTypeOfEnumMember(SymbolRef symbol)
    {
        ref var links = ref ValueSymbolLinks.Get(symbol);
        if (links.ResolvedType.IsNone)
        {
            var type = GetDeclaredTypeOfEnumMember(symbol);
            ValueSymbolLinks.Get(symbol).ResolvedType = type;
        }
        return ValueSymbolLinks.Get(symbol).ResolvedType;
    }

    // checker.ts getTypeOfAccessors (annotations; getter bodies: C5)
    private Type GetTypeOfAccessors(SymbolRef symbol)
    {
        ref var links = ref ValueSymbolLinks.Get(symbol);
        if (!links.ResolvedType.IsNone)
        {
            return links.ResolvedType;
        }
        if (!PushTypeResolution(symbol, TypeProperty))
        {
            return errorType;
        }
        var getter = GetDeclarationOfKind(symbol, SyntaxKind.GetAccessor);
        var setter = GetDeclarationOfKind(symbol, SyntaxKind.SetAccessor);
        var accessor = GetDeclarationOfKind(symbol, SyntaxKind.PropertyDeclaration);
        if (!accessor.IsNone && !Utilities.HasSyntacticModifierUncached(StoreOf(accessor), accessor.Node, ModifierFlags.Accessor))
        {
            accessor = NodeRef.None; // tryCast(..., isAutoAccessorPropertyDeclaration)
        }
        // We try to resolve a getter type annotation, a setter type annotation, or a getter function
        // body return type inference, in that order.
        // (the JSDoc type of a JavaScript getter: module C9)
        var type = GetAnnotatedAccessorType(getter);
        if (type.IsNone)
        {
            type = GetAnnotatedAccessorType(setter);
        }
        if (type.IsNone)
        {
            type = GetAnnotatedAccessorType(accessor);
        }
        if (type.IsNone && !getter.IsNone && !StoreOf(getter).Body(getter.Node).IsNone)
        {
            type = GetReturnTypeFromBody(getter);
        }
        if (type.IsNone && !accessor.IsNone)
        {
            type = GetWidenedTypeForVariableLikeDeclaration(accessor, reportErrors: true);
        }
        if (type.IsNone)
        {
            if (!setter.IsNone && !IsPrivateWithinAmbient(StoreOf(setter), setter.Node))
            {
                ErrorOrSuggestion(noImplicitAny, setter, Diagnostics.Property_0_implicitly_has_type_any_because_its_set_accessor_lacks_a_parameter_type_annotation, SymbolToString(symbol));
            }
            else if (!getter.IsNone && !IsPrivateWithinAmbient(StoreOf(getter), getter.Node))
            {
                ErrorOrSuggestion(noImplicitAny, getter, Diagnostics.Property_0_implicitly_has_type_any_because_its_get_accessor_lacks_a_return_type_annotation, SymbolToString(symbol));
            }
            else if (!accessor.IsNone && !IsPrivateWithinAmbient(StoreOf(accessor), accessor.Node))
            {
                ErrorOrSuggestion(noImplicitAny, accessor, Diagnostics.Member_0_implicitly_has_an_1_type, SymbolToString(symbol), "any");
            }
            type = anyType;
        }
        if (!PopTypeResolution())
        {
            if (HasAnnotatedAccessorTypeNode(getter))
            {
                Error(getter, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, SymbolToString(symbol));
            }
            else if (HasAnnotatedAccessorTypeNode(setter) || HasAnnotatedAccessorTypeNode(accessor))
            {
                Error(setter, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, SymbolToString(symbol));
            }
            else if (!getter.IsNone && noImplicitAny)
            {
                Error(getter, Diagnostics._0_implicitly_has_return_type_any_because_it_does_not_have_a_return_type_annotation_and_is_referenced_directly_or_indirectly_in_one_of_its_return_expressions, SymbolToString(symbol));
            }
            type = anyType;
        }
        ref var after = ref ValueSymbolLinks.Get(symbol);
        if (after.ResolvedType.IsNone)
        {
            after.ResolvedType = type;
        }
        return after.ResolvedType;
    }

    private Type GetTypeOfAlias(SymbolRef symbol)
    {
        ref var links = ref ValueSymbolLinks.Get(symbol);
        if (!links.ResolvedType.IsNone)
        {
            return links.ResolvedType;
        }
        if (!PushTypeResolution(symbol, TypeProperty))
        {
            return errorType;
        }
        var targetSymbol = ResolveAlias(symbol);
        var type = (GetSymbolFlags(targetSymbol) & SymbolFlags.Value) != 0 ? GetTypeOfSymbol(targetSymbol) : errorType;
        if (!PopTypeResolution())
        {
            ReportCircularityError(symbol);
            type = errorType;
        }
        ref var after = ref ValueSymbolLinks.Get(symbol);
        if (after.ResolvedType.IsNone)
        {
            after.ResolvedType = type;
        }
        return after.ResolvedType;
    }

    /// <summary>checker.ts getOptionalType.</summary>
    internal Type GetOptionalType(Type type, bool isProperty = false)
    {
        var missingOrUndefined = isProperty ? undefinedOrMissingType : undefinedType;
        return type == missingOrUndefined || ((Types.Flags(type) & TypeFlags.Union) != 0 && Types.Elements(Types.Union(type).Types)[0] == missingOrUndefined)
            ? type
            : GetUnionType([type, missingOrUndefined]);
    }

    private Type AddOptionality(Type type, bool isProperty = false, bool isOptional = true) =>
        strictNullChecks && isOptional ? GetOptionalType(type, isProperty) : type;

    // (getWidenedTypeForVariableLikeDeclaration, getTypeForVariableLikeDeclaration: Checker.Destructuring.cs, module C5)

    private bool DeclarationBelongsToPrivateAmbientMember(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var root = Utilities.GetRootDeclaration(store, declaration.Node);
        var memberDeclaration = store.Kind(root) == SyntaxKind.Parameter ? store.Parent(root) : root;
        return IsPrivateWithinAmbient(store, memberDeclaration);
    }

    private static bool IsPrivateWithinAmbient(NodeStore store, Node node) =>
        (Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Private) || (Utilities.GetNameOfDeclaration(store, node) is var name && !name.IsNone && store.Kind(name) == SyntaxKind.PrivateIdentifier))
        && (store.Flags(node) & NodeFlags.Ambient) != 0;

    private bool IsNullOrUndefined(NodeRef node)
    {
        var store = StoreOf(node);
        var expr = Utilities.SkipParentheses(store, node.Node, true);
        return store.Kind(expr) == SyntaxKind.NullKeyword || (store.Kind(expr) == SyntaxKind.Identifier && GetResolvedSymbol(At(node, expr)) == undefinedSymbol);
    }

    private static bool IsEmptyArrayLiteral(NodeStore store, Node node)
    {
        var expr = Utilities.SkipParentheses(store, node, true);
        return store.Kind(expr) == SyntaxKind.ArrayLiteralExpression && store.Length(store.Elements(expr)) == 0;
    }

    private static bool IsDeclarationReadonly(NodeStore store, Node declaration) =>
        (Utilities.GetCombinedModifierFlags(store, declaration) & ModifierFlags.Readonly) != 0 && !Utilities.IsParameterPropertyDeclaration(store, declaration, store.Parent(declaration));

    // (getWidenedType: Checker.Literals.cs, module C5)

    // checker.ts reportImplicitAny (the variable, parameter and property cases; the widening kinds: module C5)
    private void ReportImplicitAny(NodeRef declaration, Type type, WideningKind wideningKind = WideningKind.Normal)
    {
        var store = StoreOf(declaration);
        var node = declaration.Node;
        var typeAsString = TypeToString(GetWidenedType(type));
        DiagnosticMessage diagnostic;
        switch (store.Kind(node))
        {
            case SyntaxKind.BinaryExpression:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
                diagnostic = noImplicitAny ? Diagnostics.Member_0_implicitly_has_an_1_type : Diagnostics.Member_0_implicitly_has_an_1_type_but_a_better_type_may_be_inferred_from_usage;
                break;
            case SyntaxKind.Parameter:
            {
                var parent = store.Parent(node);
                var name = store.Name(node);
                if (store.Kind(name) == SyntaxKind.Identifier)
                {
                    var parentKind = store.Kind(parent);
                    var nameText = store.Texts.GetString(store.EscapedText(name))!;
                    var keywordKind = Utilities.IdentifierToKeywordKind(nameText);
                    if (parentKind is SyntaxKind.CallSignature or SyntaxKind.MethodSignature or SyntaxKind.FunctionType
                        && store.Nodes(store.Parameters(parent)).Contains(node)
                        && (!ResolveName(declaration, nameText, NodeRef.None, SymbolFlags.Type, null, isUse: true).IsNone ||
                            (keywordKind is { } k && IsTypeNodeKind(k))))
                    {
                        var newName = "arg" + store.Nodes(store.Parameters(parent)).IndexOf(node).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        var typeName = Utilities.DeclarationNameToString(store, name) + (!store.DotDotDotToken(node).IsNone ? "[]" : "");
                        ErrorOrSuggestion(noImplicitAny, declaration, Diagnostics.Parameter_has_a_name_but_no_type_Did_you_mean_0_Colon_1, newName, typeName);
                        return;
                    }
                }
                diagnostic = !store.DotDotDotToken(node).IsNone
                    ? noImplicitAny ? Diagnostics.Rest_parameter_0_implicitly_has_an_any_type : Diagnostics.Rest_parameter_0_implicitly_has_an_any_type_but_a_better_type_may_be_inferred_from_usage
                    : noImplicitAny ? Diagnostics.Parameter_0_implicitly_has_an_1_type : Diagnostics.Parameter_0_implicitly_has_an_1_type_but_a_better_type_may_be_inferred_from_usage;
                break;
            }
            case SyntaxKind.BindingElement:
                diagnostic = Diagnostics.Binding_element_0_implicitly_has_an_1_type;
                if (!noImplicitAny)
                {
                    // Don't issue a suggestion for binding elements since the codefix doesn't yet support them.
                    return;
                }
                break;
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
                if (noImplicitAny && (store.Kind(node) == SyntaxKind.ArrowFunction || store.Name(node).IsNone))
                {
                    Error(declaration, wideningKind == WideningKind.GeneratorYield
                        ? Diagnostics.Generator_implicitly_has_yield_type_0_Consider_supplying_a_return_type_annotation
                        : Diagnostics.Function_expression_which_lacks_return_type_annotation_implicitly_has_an_0_return_type, typeAsString);
                    return;
                }
                diagnostic = !noImplicitAny ? Diagnostics._0_implicitly_has_an_1_return_type_but_a_better_type_may_be_inferred_from_usage
                    : wideningKind == WideningKind.GeneratorYield ? Diagnostics._0_which_lacks_return_type_annotation_implicitly_has_an_1_yield_type
                    : Diagnostics._0_which_lacks_return_type_annotation_implicitly_has_an_1_return_type;
                break;
            case SyntaxKind.MappedType:
                if (noImplicitAny)
                {
                    Error(declaration, Diagnostics.Mapped_object_type_implicitly_has_an_any_template_type);
                }
                return;
            case SyntaxKind.VariableDeclaration:
            default:
                diagnostic = noImplicitAny ? Diagnostics.Variable_0_implicitly_has_an_1_type : Diagnostics.Variable_0_implicitly_has_an_1_type_but_a_better_type_may_be_inferred_from_usage;
                break;
        }
        ErrorOrSuggestion(noImplicitAny, declaration, diagnostic, Utilities.DeclarationNameToString(store, Utilities.GetNameOfDeclaration(store, node)), typeAsString);
    }
}
