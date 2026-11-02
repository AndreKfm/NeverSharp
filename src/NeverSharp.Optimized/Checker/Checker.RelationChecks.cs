// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (convertAutoToAny, issueMemberSpecificError, getTypeWithoutSignatures,
// checkInheritedPropertiesAreIdentical, checkIndexConstraints, checkTypeArguments, checkTypeArgumentConstraints,
// errorNextVariableOrPropertyDeclarationMustHaveSameType)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go (checkVariableLikeDeclaration)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C4 (the declaration checks that call these are module C8's, in
// Checker.Statements.cs, Checker.Declarations.cs and Checker.Classes.cs).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // checker.ts convertAutoToAny
    private Type ConvertAutoToAny(Type type) => type == autoType ? anyType : type == autoArrayType ? anyArrayType : type;

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
        foreach (var p in Types.View(ResolveDeclaredMembers(type).DeclaredProperties))
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

    // checker.ts hasBindableName
    private bool HasBindableName(NodeRef member) =>
        !Utilities.HasDynamicName(StoreOf(member), member.Node) || HasLateBindableName(member);

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
        var mapper = TypeMapper.None;
        for (var i = 0; i < typeArgumentNodes.Length; i++)
        {
            var constraint = GetConstraintOfTypeParameter(typeParameters[i]);
            if (!constraint.IsNone)
            {
                Func<DiagnosticMessageChain?>? errorInfo = reportErrors && headMessage is not null
                    ? () => ChainDiagnosticMessages(null, Diagnostics.Type_0_does_not_satisfy_the_constraint_1)
                    : null;
                var typeArgumentHeadMessage = headMessage ?? Diagnostics.Type_0_does_not_satisfy_the_constraint_1;
                if (mapper.IsNone)
                {
                    mapper = CreateTypeMapper(typeParameters, typeArgumentTypes);
                }
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
        var mapper = TypeMapper.None;
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
                var errorNode = i < typeArgumentNodes.Length ? At(node, typeArgumentNodes[i]) : NodeRef.None;
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
