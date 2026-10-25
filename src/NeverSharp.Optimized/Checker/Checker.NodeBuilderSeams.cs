// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (the data of mapper, tuple, index, template literal, string mapping, indexed
// access, substitution, conditional, mapped and instantiation expression types as the node builder reads them)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Modified: translated from TypeScript to C#; module C3. Accessors under the names module C2's node builder uses for
// its placeholders (Checker.NodeBuilder.Support.cs), so the merge can drop the placeholders.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // signature.mapper
    private TypeMapper? GetSignatureMapper(Signature signature) => MapperAt(Signatures.Record(signature).Mapper);

    // instantiateType(type, signature.mapper)
    private Type InstantiateTypeWithMapper(Type type, object? mapper) => mapper is null ? type : InstantiateType(type, (TypeMapper)mapper);

    // getMappedType(type, mapper) for an untyped mapper
    private Type GetMappedType(Type type, object mapper) => GetMappedType(type, (TypeMapper)mapper);

    // getMappedType(typeParameter, symbol.links.mapper) of an instantiated symbol
    private Type GetMappedTypeOfInstantiatedSymbol(Type typeParameter, SymbolRef instantiatedSymbol)
    {
        if ((GetCheckFlags(instantiatedSymbol) & CheckFlags.Instantiated) == 0)
        {
            return typeParameter;
        }
        var mapper = MapperAt(ValueSymbolLinks.Get(instantiatedSymbol).Mapper);
        return mapper is null ? typeParameter : GetMappedType(typeParameter, mapper);
    }

    // TupleType.elementFlags / readonly / labeledElementDeclarations
    private (ElementFlags[] ElementFlags, bool Readonly, NodeRef[]? LabeledElementDeclarations) GetTupleTargetInfo(Type target)
    {
        var data = Types.Object(target);
        return (data.ElementFlags ?? [], data.Readonly, data.LabeledElementDeclarations);
    }

    // IndexType.type
    private Type GetIndexTypeOperand(Type type) => Types.Data<IndexData>(type).Type;

    // TemplateLiteralType.texts / types
    private (string[] Texts, Type[] Types) GetTemplateLiteralParts(Type type)
    {
        var data = Types.Data<TemplateLiteralData>(type);
        return (data.Texts, data.Types);
    }

    // StringMappingType.type
    private Type GetStringMappingOperand(Type type) => Types.Data<StringMappingData>(type).Type;

    // IndexedAccessType.objectType / indexType
    private (Type ObjectType, Type IndexType) GetIndexedAccessParts(Type type)
    {
        var data = Types.Data<IndexedAccessData>(type);
        return (data.ObjectType, data.IndexType);
    }

    // SubstitutionType.baseType
    private Type GetSubstitutionBaseType(Type type) => Types.Data<SubstitutionData>(type).BaseType;

    // ConditionalType.checkType / extendsType / root.isDistributive / root.inferTypeParameters
    private (Type CheckType, Type ExtendsType, bool IsDistributive, List<Type>? InferTypeParameters) GetConditionalTypeParts(Type type)
    {
        var data = Types.Data<ConditionalData>(type);
        return (data.CheckType, data.ExtendsType, data.Root.IsDistributive, data.Root.InferTypeParameters is { } infer ? [.. infer] : null);
    }

    // ConditionalType.root.node
    private NodeRef GetConditionalRootNode(Type type) => Types.Data<ConditionalData>(type).Root.Node;

    // MappedType.containsError
    private bool MappedTypeContainsError(Type type) => (Types.ObjectFlagsOf(type) & ObjectFlags.Mapped) != 0 && Types.Object(type).ContainsError;

    // MappedType.declaration
    private NodeRef GetMappedTypeDeclaration(Type type) => Types.Object(type).Declaration;

    // InstantiationExpressionType.node
    private NodeRef GetInstantiationExpressionNode(Type type) =>
        (Types.ObjectFlagsOf(type) & ObjectFlags.InstantiationExpressionType) != 0 ? Types.Object(type).Node : NodeRef.None;

    // getCheckFlags(symbol) & CheckFlags.Mapped
    private bool IsMappedSymbol(SymbolRef symbol) => (GetCheckFlags(symbol) & CheckFlags.Mapped) != 0;
}
