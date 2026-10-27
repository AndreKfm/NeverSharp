// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (signature helpers: getParameterCount, getMinArgumentCount, getTypeAtPosition,
// getRestTypeAtPosition, getErasedSignature, getCanonicalSignature, getSingleCallSignature, sliceTupleType, ...)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C4. Call resolution (C5) shares these functions. The erased and
// canonical signature caches are per checker dictionaries (TypeScript caches them in the signature objects).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private readonly Dictionary<Signature, Signature> erasedSignatureCache = [];
    private readonly Dictionary<Signature, Signature> canonicalSignatureCache = [];

    // checker.ts signatureHasRestParameter
    private bool SignatureHasRestParameter(Signature signature) => (Signatures.Record(signature).Flags & SignatureFlags.HasRestParameter) != 0;

    private SymbolRef[] ParametersOf(Signature signature) => Signatures.Record(signature).Parameters ?? [];

    /// <summary>checker.ts getTypeOfParameter.</summary>
    internal Type GetTypeOfParameter(SymbolRef symbol)
    {
        var declaration = ValueDeclarationOf(symbol);
        return AddOptionality(GetTypeOfSymbol(symbol), isProperty: false,
            isOptional: !declaration.IsNone && (UtilitiesPublic.HasInitializer(StoreOf(declaration), declaration.Node) || IsOptionalDeclaration(declaration)));
    }

    /// <summary>checker.ts getParameterNameAtPosition.</summary>
    internal string GetParameterNameAtPosition(Signature signature, int pos, Type overrideRestType = default)
    {
        var parameters = ParametersOf(signature);
        var paramCount = parameters.Length - (SignatureHasRestParameter(signature) ? 1 : 0);
        if (pos < paramCount)
        {
            return NameOf(parameters[pos]);
        }
        var restParameter = paramCount < parameters.Length ? parameters[paramCount] : unknownSymbol;
        var restType = !overrideRestType.IsNone ? overrideRestType : GetTypeOfSymbol(restParameter);
        if (IsTupleType(restType))
        {
            var tupleType = TupleTarget(restType);
            var index = pos - paramCount;
            var associatedName = tupleType.LabeledElementDeclarations is { } labels && index < labels.Length ? labels[index] : NodeRef.None;
            return GetTupleElementLabel(associatedName, index, tupleType.ElementFlags![index], restParameter);
        }
        return NameOf(restParameter);
    }

    // checker.ts getTupleElementLabel(d, index, elementFlags, restSymbol)
    private string GetTupleElementLabel(NodeRef declaration, int index, ElementFlags elementFlags, SymbolRef restSymbol)
    {
        if (declaration.IsNone)
        {
            var restParameter = restSymbol.IsNone ? NodeRef.None : ValueDeclarationOf(restSymbol);
            return !restParameter.IsNone && Kind(restParameter) == SyntaxKind.Parameter ? GetTupleElementLabelFromBindingElement(restParameter, index, elementFlags)
                : (restSymbol.IsNone ? "arg" : NameOf(restSymbol)) + "_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return GetTupleElementLabel(declaration);
    }

    // checker.ts getTupleElementLabelFromBindingElement
    private string GetTupleElementLabelFromBindingElement(NodeRef node, int index, ElementFlags elementFlags)
    {
        var store = StoreOf(node);
        var name = store.Name(node.Node);
        switch (store.Kind(name))
        {
            case SyntaxKind.Identifier:
            {
                var text = store.Texts.GetString(store.EscapedText(name)) ?? "";
                if (!store.DotDotDotToken(node.Node).IsNone)
                {
                    // (...[x, y, ...z]: [number, number, ...number[]]) => ... produces (x: number, y: number, ...z: number[])
                    return (elementFlags & ElementFlags.Variable) != 0 ? text : text + "_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                // (...[x]: [number]) => ... produces (x: number); (...[x]: ...number[]) produces (x_0: number)
                return (elementFlags & ElementFlags.Fixed) != 0 ? text : text + "_n";
            }
            case SyntaxKind.ArrayBindingPattern:
            {
                if (!store.DotDotDotToken(node.Node).IsNone)
                {
                    var elements = store.Nodes(store.Elements(name));
                    var lastElement = elements.Length > 0 && store.Kind(elements[^1]) == SyntaxKind.BindingElement ? elements[^1] : Node.None;
                    var elementCount = elements.Length - (!lastElement.IsNone && !store.DotDotDotToken(lastElement).IsNone ? 1 : 0);
                    if (index < elementCount)
                    {
                        var element = elements[index];
                        if (store.Kind(element) == SyntaxKind.BindingElement)
                        {
                            return GetTupleElementLabelFromBindingElement(At(node, element), index, elementFlags);
                        }
                    }
                    else if (!lastElement.IsNone && !store.DotDotDotToken(lastElement).IsNone)
                    {
                        return GetTupleElementLabelFromBindingElement(At(node, lastElement), index - elementCount, elementFlags);
                    }
                }
                break;
            }
        }
        return "arg_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // checker.ts getNameableDeclarationAtPosition
    private NodeRef GetNameableDeclarationAtPosition(Signature signature, int pos)
    {
        var parameters = ParametersOf(signature);
        var paramCount = parameters.Length - (SignatureHasRestParameter(signature) ? 1 : 0);
        if (pos < paramCount)
        {
            var decl = ValueDeclarationOf(parameters[pos]);
            return !decl.IsNone && IsValidDeclarationForTupleLabel(decl) ? decl : NodeRef.None;
        }
        var restParameter = paramCount < parameters.Length ? parameters[paramCount] : unknownSymbol;
        var restType = GetTypeOfSymbol(restParameter);
        if (IsTupleType(restType))
        {
            var associatedNames = TupleTarget(restType).LabeledElementDeclarations;
            var index = pos - paramCount;
            return associatedNames is not null && index < associatedNames.Length ? associatedNames[index] : NodeRef.None;
        }
        var restDeclaration = ValueDeclarationOf(restParameter);
        return !restDeclaration.IsNone && IsValidDeclarationForTupleLabel(restDeclaration) ? restDeclaration : NodeRef.None;
    }

    // checker.ts isValidDeclarationForTupleLabel
    private bool IsValidDeclarationForTupleLabel(NodeRef d)
    {
        var store = StoreOf(d);
        var kind = store.Kind(d.Node);
        return kind == SyntaxKind.NamedTupleMember || (kind == SyntaxKind.Parameter && !store.Name(d.Node).IsNone && store.Kind(store.Name(d.Node)) == SyntaxKind.Identifier);
    }

    /// <summary>checker.ts getTypeAtPosition.</summary>
    internal Type GetTypeAtPosition(Signature signature, int pos)
    {
        var type = TryGetTypeAtPosition(signature, pos);
        return type.IsNone ? anyType : type;
    }

    /// <summary>checker.ts tryGetTypeAtPosition.</summary>
    internal Type TryGetTypeAtPosition(Signature signature, int pos)
    {
        var parameters = ParametersOf(signature);
        var paramCount = parameters.Length - (SignatureHasRestParameter(signature) ? 1 : 0);
        if (pos < paramCount)
        {
            return GetTypeOfParameter(parameters[pos]);
        }
        if (SignatureHasRestParameter(signature))
        {
            // We want to return the value undefined for an out of bounds parameter position,
            // so we need to check bounds here before calling getIndexedAccessType (which
            // otherwise would return the type 'undefined').
            var restType = GetTypeOfSymbol(parameters[paramCount]);
            var index = pos - paramCount;
            if (!IsTupleType(restType) || (TupleTarget(restType).CombinedFlags & ElementFlags.Variable) != 0 || index < TupleTarget(restType).FixedLength)
            {
                return GetIndexedAccessType(restType, GetNumberLiteralType(index));
            }
        }
        return Type.None;
    }

    /// <summary>checker.ts getRestTypeAtPosition.</summary>
    internal Type GetRestTypeAtPosition(Signature source, int pos, bool isReadonly = false)
    {
        var parameterCount = GetParameterCount(source);
        var minArgumentCount = GetMinArgumentCount(source);
        var restType = GetEffectiveRestType(source);
        if (!restType.IsNone && pos >= parameterCount - 1)
        {
            return pos == parameterCount - 1 ? restType : CreateArrayType(GetIndexedAccessType(restType, numberType));
        }
        var types = new List<Type>();
        var flags = new List<ElementFlags>();
        var names = new List<NodeRef>();
        for (var i = pos; i < parameterCount; i++)
        {
            if (restType.IsNone || i < parameterCount - 1)
            {
                types.Add(GetTypeAtPosition(source, i));
                flags.Add(i < minArgumentCount ? ElementFlags.Required : ElementFlags.Optional);
            }
            else
            {
                types.Add(restType);
                flags.Add(ElementFlags.Variadic);
            }
            names.Add(GetNameableDeclarationAtPosition(source, i));
        }
        return CreateTupleType([.. types], [.. flags], isReadonly, [.. names]);
    }

    /// <summary>
    /// checker.ts getRestOrAnyTypeAtPosition: the rest type at the given position, transforming `any[]` into just
    /// `any`. We do this because in signatures we want `any[]` in a rest position to be compatible with anything, but
    /// `any[]` isn't assignable to tuple types with required elements.
    /// </summary>
    internal Type GetRestOrAnyTypeAtPosition(Signature source, int pos)
    {
        var restType = GetRestTypeAtPosition(source, pos);
        var elementType = restType.IsNone ? Type.None : GetElementTypeOfArrayType(restType);
        return !elementType.IsNone && (Types.Flags(elementType) & TypeFlags.Any) != 0 ? anyType : restType;
    }

    /// <summary>
    /// checker.ts getParameterCount: the number of parameters in a signature. The rest parameter, if present, counts
    /// as one parameter; a rest parameter of a tuple type counts as its fixed elements.
    /// </summary>
    internal int GetParameterCount(Signature signature)
    {
        var parameters = ParametersOf(signature);
        var length = parameters.Length;
        if (SignatureHasRestParameter(signature))
        {
            var restType = GetTypeOfSymbol(parameters[length - 1]);
            if (IsTupleType(restType))
            {
                var target = TupleTarget(restType);
                return length + target.FixedLength - ((target.CombinedFlags & ElementFlags.Variable) != 0 ? 0 : 1);
            }
        }
        return length;
    }

    /// <summary>checker.ts getMinArgumentCount (flags: MinArgumentCountFlags StrongArityForUntypedJS = 1, VoidIsNonOptional = 2).</summary>
    internal int GetMinArgumentCount(Signature signature, int flags = 0)
    {
        var strongArityForUntypedJS = (flags & 1) != 0;
        var voidIsNonOptional = (flags & 2) != 0;

        if (voidIsNonOptional || Signatures.Record(signature).ResolvedMinArgumentCount < 0)
        {
            var minArgumentCount = -1;
            var parameters = ParametersOf(signature);
            if (SignatureHasRestParameter(signature))
            {
                var restType = GetTypeOfSymbol(parameters[^1]);
                if (IsTupleType(restType))
                {
                    var target = TupleTarget(restType);
                    var firstOptionalIndex = Array.FindIndex(target.ElementFlags!, f => (f & ElementFlags.Required) == 0);
                    var requiredCount = firstOptionalIndex < 0 ? target.FixedLength : firstOptionalIndex;
                    if (requiredCount > 0)
                    {
                        minArgumentCount = parameters.Length - 1 + requiredCount;
                    }
                }
            }
            if (minArgumentCount < 0)
            {
                if (!strongArityForUntypedJS && (Signatures.Record(signature).Flags & SignatureFlags.IsUntypedSignatureInJSFile) != 0)
                {
                    return 0;
                }
                minArgumentCount = Signatures.Record(signature).MinArgumentCount;
            }
            if (voidIsNonOptional)
            {
                return minArgumentCount;
            }
            for (var i = minArgumentCount - 1; i >= 0; i--)
            {
                var type = GetTypeAtPosition(signature, i);
                if ((Types.Flags(FilterType(type, t => (Types.Flags(t) & TypeFlags.Void) != 0)) & TypeFlags.Never) != 0)
                {
                    break;
                }
                minArgumentCount = i;
            }
            Signatures.Record(signature).ResolvedMinArgumentCount = minArgumentCount;
        }
        return Signatures.Record(signature).ResolvedMinArgumentCount;
    }

    /// <summary>checker.ts hasEffectiveRestParameter.</summary>
    internal bool HasEffectiveRestParameter(Signature signature)
    {
        if (SignatureHasRestParameter(signature))
        {
            var restType = GetTypeOfSymbol(ParametersOf(signature)[^1]);
            return !IsTupleType(restType) || (TupleTarget(restType).CombinedFlags & ElementFlags.Variable) != 0;
        }
        return false;
    }

    /// <summary>checker.ts getEffectiveRestType.</summary>
    internal Type GetEffectiveRestType(Signature signature)
    {
        if (SignatureHasRestParameter(signature))
        {
            var restType = GetTypeOfSymbol(ParametersOf(signature)[^1]);
            if (!IsTupleType(restType))
            {
                return (Types.Flags(restType) & TypeFlags.Any) != 0 ? anyArrayType : restType;
            }
            if ((TupleTarget(restType).CombinedFlags & ElementFlags.Variable) != 0)
            {
                return SliceTupleType(restType, TupleTarget(restType).FixedLength);
            }
        }
        return Type.None;
    }

    /// <summary>checker.ts getNonArrayRestType.</summary>
    internal Type GetNonArrayRestType(Signature signature)
    {
        var restType = GetEffectiveRestType(signature);
        return !restType.IsNone && !IsArrayType(restType) && (Types.Flags(restType) & TypeFlags.Any) == 0 ? restType : Type.None;
    }

    /// <summary>checker.ts sliceTupleType.</summary>
    internal Type SliceTupleType(Type type, int index, int endSkipCount = 0)
    {
        var target = TupleTarget(type);
        var endIndex = GetTypeReferenceArity(type) - endSkipCount;
        if (index > target.FixedLength)
        {
            var rest = GetRestArrayTypeOfTupleType(type);
            return rest.IsNone ? CreateTupleType([]) : rest;
        }
        // (Array.prototype.slice clamps to the array and gives an empty slice when index > endIndex, module C5)
        var typeArguments = GetTypeArguments(type);
        var end = Math.Max(Math.Min(endIndex, Math.Min(typeArguments.Length, target.ElementFlags!.Length)), 0);
        var start = Math.Min(index, end);
        var labels = target.LabeledElementDeclarations;
        return CreateTupleType(typeArguments[start..end].ToArray(), target.ElementFlags![start..end], isReadonly: false,
            labels is null ? null : labels[Math.Min(start, labels.Length)..Math.Min(end, labels.Length)]);
    }

    // checker.ts getRestArrayTypeOfTupleType
    private Type GetRestArrayTypeOfTupleType(Type type)
    {
        var restType = GetRestTypeOfTupleType(type);
        return restType.IsNone ? Type.None : CreateArrayType(restType);
    }

    /// <summary>checker.ts getThisTypeOfSignature (None when the signature has no `this` parameter).</summary>
    internal Type GetThisTypeOfSignature(Signature signature)
    {
        var thisParameter = Signatures.Record(signature).ThisParameter;
        return thisParameter.IsNone ? Type.None : GetTypeOfSymbol(thisParameter);
    }

    /// <summary>checker.ts isResolvingReturnTypeOfSignature.</summary>
    internal bool IsResolvingReturnTypeOfSignature(Signature signature)
    {
        if (Signatures.Record(signature).CompositeSignatures is { } composites)
        {
            foreach (var composite in composites)
            {
                if (IsResolvingReturnTypeOfSignature(composite))
                {
                    return true;
                }
            }
        }
        return Signatures.Record(signature).ResolvedReturnType.IsNone && FindResolutionCycleStartIndex(signature, ResolvedReturnTypeProperty) >= 0;
    }

    /// <summary>checker.ts getErasedSignature.</summary>
    internal Signature GetErasedSignature(Signature signature)
    {
        var typeParameters = Signatures.Record(signature).TypeParameters;
        if (typeParameters.Count == 0)
        {
            return signature;
        }
        if (!erasedSignatureCache.TryGetValue(signature, out var erased))
        {
            // createErasedSignature: an instantiation of the signature where all type arguments are the any type.
            erased = InstantiateSignature(signature, CreateTypeEraser(Types.Elements(typeParameters).ToArray()), eraseTypeParameters: true);
            erasedSignatureCache[signature] = erased;
        }
        return erased;
    }

    /// <summary>checker.ts getCanonicalSignature.</summary>
    internal Signature GetCanonicalSignature(Signature signature)
    {
        var typeParameters = Signatures.Record(signature).TypeParameters;
        if (typeParameters.Count == 0)
        {
            return signature;
        }
        if (!canonicalSignatureCache.TryGetValue(signature, out var canonical))
        {
            // createCanonicalSignature: each unconstrained type parameter is replaced with its original.
            var arguments = Types.Elements(typeParameters).ToArray();
            for (var i = 0; i < arguments.Length; i++)
            {
                var target = Types.TypeParameter(arguments[i]).Target;
                if (!target.IsNone && GetConstraintOfTypeParameter(target).IsNone)
                {
                    arguments[i] = target;
                }
            }
            var declaration = Signatures.Record(signature).Declaration;
            canonical = GetSignatureInstantiation(signature, arguments, !declaration.IsNone && Utilities.IsInJSFile(StoreOf(declaration), declaration.Node));
            canonicalSignatureCache[signature] = canonical;
        }
        return canonical;
    }

    // checker.ts isInstantiatedGenericParameter
    private bool IsInstantiatedGenericParameter(Signature signature, int pos)
    {
        var target = Signatures.Record(signature).Target;
        if (target.IsNone)
        {
            return false;
        }
        var type = TryGetTypeAtPosition(target, pos);
        return !type.IsNone && IsGenericType(type);
    }

    /// <summary>checker.ts getSingleCallSignature: the single call signature of a type with no other members.</summary>
    internal Signature GetSingleCallSignature(Type type) => GetSingleSignature(type, SignatureKind.Call, allowMembers: false);

    /// <summary>checker.ts getSingleSignature.</summary>
    internal Signature GetSingleSignature(Type type, SignatureKind kind, bool allowMembers)
    {
        if ((Types.Flags(type) & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers(type);
            if (allowMembers || (resolved.Properties!.Count == 0 && resolved.IndexInfos!.Count == 0))
            {
                if (kind == SignatureKind.Call && resolved.CallSignatures!.Count == 1 && resolved.ConstructSignatures!.Count == 0)
                {
                    return resolved.CallSignatures[0];
                }
                if (kind == SignatureKind.Construct && resolved.ConstructSignatures!.Count == 1 && resolved.CallSignatures!.Count == 0)
                {
                    return resolved.ConstructSignatures[0];
                }
            }
        }
        return Signature.None;
    }
}
