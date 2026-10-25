// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts (InferenceContext, InferenceInfo, InferenceFlags, InferencePriority) and
// src/compiler/checker.ts (the inference entry points the expression checker calls: createInferenceContext,
// cloneInferenceContext, cloneInferredPartOfContext, getMapperFromContext, createOuterReturnMapper, inferTypes,
// getInferredTypes, addIntraExpressionInferenceSite, hasInferenceCandidates)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/inference.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5. TODO(C6): this file is a seam for type inference (module C6
// ports inferTypes and getInferredTypes and replaces it). Until then nothing is inferred: inferTypes records nothing and
// the inferred type of every type parameter is the type parameter itself, so an inferred instantiation is the
// uninstantiated signature (the types C1 used). Results that depend on inference are marked with C1's "uncertain"
// mechanism by the callers (calls resolved with inferred type arguments, parameters typed by an inferential context).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>types.ts InferenceFlags.</summary>
[Flags]
internal enum InferenceFlags
{
    None = 0,                   // No special inference behaviors
    NoDefault = 1 << 0,         // Infer silentNeverType for no inferences (otherwise anyType or unknownType)
    AnyDefault = 1 << 1,        // Infer anyType (in JS files) for no inferences (otherwise unknownType)
    SkippedGenericFunction = 1 << 2, // A generic function was skipped during inference
}

/// <summary>types.ts InferencePriority.</summary>
[Flags]
internal enum InferencePriority
{
    None = 0,
    NakedTypeVariable = 1 << 0,            // Naked type variable in union or intersection type
    SpeculativeTuple = 1 << 1,             // Speculative tuple inference
    SubstituteSource = 1 << 2,             // Source of inference originated within a substitution type's substitute
    HomomorphicMappedType = 1 << 3,        // Reverse inference for homomorphic mapped type
    PartialHomomorphicMappedType = 1 << 4, // Partial reverse inference for homomorphic mapped type
    MappedTypeConstraint = 1 << 5,         // Reverse inference for mapped type
    ContravariantConditional = 1 << 6,     // Conditional type in contravariant position
    ReturnType = 1 << 7,                   // Inference made from return type of generic function
    LiteralKeyof = 1 << 8,                 // Inference made from a string literal to a keyof T
    NoConstraints = 1 << 9,                // Don't infer from constraints of instantiable types
    AlwaysStrict = 1 << 10,                // Always use strict rules for contravariant inferences
    MaxValue = 1 << 11,                    // Seed for inference priority tracking
}

/// <summary>types.ts InferenceInfo.</summary>
internal sealed class InferenceInfo
{
    public Type TypeParameter;
    public List<Type>? Candidates;
    public List<Type>? ContraCandidates;
    public Type InferredType;
    public InferencePriority Priority;
    public bool TopLevel;
    public bool IsFixed;
    public int? ImpliedArity;
}

/// <summary>types.ts InferenceContext.</summary>
internal sealed class InferenceContext
{
    public List<InferenceInfo> Inferences = [];
    public Signature Signature;
    public InferenceFlags Flags;
    public Func<Type, Type, bool>? CompareTypes;
    public TypeMapper Mapper = null!;
    public TypeMapper NonFixingMapper = null!;
    public TypeMapper? ReturnMapper;
    public List<Type>? InferredTypeParameters;
    public List<(NodeRef Node, Type Type)>? IntraExpressionInferenceSites;
}

internal sealed partial class Checker
{
    /// <summary>checker.ts createInferenceContext.</summary>
    internal InferenceContext CreateInferenceContext(ReadOnlySpan<Type> typeParameters, Signature signature, InferenceFlags flags, Func<Type, Type, bool>? compareTypes = null)
    {
        var inferences = new List<InferenceInfo>(typeParameters.Length);
        foreach (var tp in typeParameters)
        {
            inferences.Add(CreateInferenceInfo(tp));
        }
        return CreateInferenceContextWorker(inferences, signature, flags, compareTypes ?? ((s, t) => CompareTypesAssignable(s, t) != Ternary.False));
    }

    /// <summary>checker.ts cloneInferenceContext.</summary>
    internal static InferenceContext? CloneInferenceContext(InferenceContext? context, InferenceFlags extraFlags = InferenceFlags.None) =>
        context is null ? null : CreateInferenceContextWorker(context.Inferences.ConvertAll(CloneInferenceInfo), context.Signature, context.Flags | extraFlags, context.CompareTypes);

    // checker.ts createInferenceContextWorker (TODO(C6): the fixing and non-fixing mappers map each type parameter to
    // its inferred type; the seam infers nothing and maps type parameters to themselves)
    private static InferenceContext CreateInferenceContextWorker(List<InferenceInfo> inferences, Signature signature, InferenceFlags flags, Func<Type, Type, bool>? compareTypes)
    {
        var context = new InferenceContext { Inferences = inferences, Signature = signature, Flags = flags, CompareTypes = compareTypes };
        var sources = inferences.ConvertAll(i => i.TypeParameter).ToArray();
        context.Mapper = CreateTypeMapper(sources, sources);
        context.NonFixingMapper = CreateTypeMapper(sources, sources);
        return context;
    }

    // checker.ts createInferenceInfo
    private static InferenceInfo CreateInferenceInfo(Type typeParameter) => new() { TypeParameter = typeParameter, TopLevel = true };

    // checker.ts cloneInferenceInfo
    private static InferenceInfo CloneInferenceInfo(InferenceInfo inference) => new()
    {
        TypeParameter = inference.TypeParameter,
        Candidates = inference.Candidates is null ? null : [.. inference.Candidates],
        ContraCandidates = inference.ContraCandidates is null ? null : [.. inference.ContraCandidates],
        InferredType = inference.InferredType,
        Priority = inference.Priority,
        TopLevel = inference.TopLevel,
        IsFixed = inference.IsFixed,
        ImpliedArity = inference.ImpliedArity,
    };

    /// <summary>checker.ts cloneInferredPartOfContext.</summary>
    internal static InferenceContext? CloneInferredPartOfContext(InferenceContext context)
    {
        var inferences = context.Inferences.FindAll(HasInferenceCandidates);
        return inferences.Count > 0 ? CreateInferenceContextWorker(inferences.ConvertAll(CloneInferenceInfo), context.Signature, context.Flags, context.CompareTypes) : null;
    }

    /// <summary>checker.ts getMapperFromContext.</summary>
    internal static TypeMapper? GetMapperFromContext(InferenceContext? context) => context?.Mapper;

    /// <summary>checker.ts createOuterReturnMapper (TODO(C6)).</summary>
    internal static TypeMapper? CreateOuterReturnMapper(InferenceContext context) => context.ReturnMapper ?? context.Mapper;

    /// <summary>checker.ts inferTypes. TODO(C6): the seam records no inferences.</summary>
    internal static void InferTypes(List<InferenceInfo> inferences, Type originalSource, Type originalTarget, InferencePriority priority = InferencePriority.None, bool contravariant = false)
    {
        _ = inferences;
        _ = originalSource;
        _ = originalTarget;
        _ = priority;
        _ = contravariant;
    }

    /// <summary>checker.ts getInferredTypes. TODO(C6): the seam's inferred type of a type parameter is the type parameter.</summary>
    internal static Type[] GetInferredTypes(InferenceContext context) => context.Inferences.ConvertAll(i => i.TypeParameter).ToArray();

    /// <summary>checker.ts addIntraExpressionInferenceSite.</summary>
    internal static void AddIntraExpressionInferenceSite(InferenceContext context, NodeRef node, Type type) =>
        (context.IntraExpressionInferenceSites ??= []).Add((node, type));

    /// <summary>checker.ts hasInferenceCandidates.</summary>
    internal static bool HasInferenceCandidates(InferenceInfo info) => info.Candidates is not null || info.ContraCandidates is not null;

    /// <summary>checker.ts hasInferenceCandidatesOrDefault.</summary>
    internal bool HasInferenceCandidatesOrDefault(InferenceInfo info) => info.Candidates is not null || info.ContraCandidates is not null || HasTypeParameterDefault(info.TypeParameter);
}
