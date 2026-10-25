// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (checker functions the node builder calls: isReadonlySymbol, getTypeParameterModifiers, ...) and src/compiler/types.ts (TypePredicate) and src/compiler/utilities.ts (FunctionFlags)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C2. The functions below are what the node builder needs from
// parts of the checker other modules port; where a module has not ported its part yet, the function answers for the
// types the ported modules create and is marked with the module that owns it. When the owning module lands, its
// definition replaces the one here (module C3's instantiation, tuple, mapped and conditional accessors have).

using NeverSharp.Generated;

// The seams below are instance members of the checker once their modules port them.
#pragma warning disable CA1822

namespace NeverSharp.Optimized;

/// <summary>utilities.ts FunctionFlags.</summary>
[Flags]
internal enum FunctionFlags
{
    Normal = 0, // Function is a normal function
    Generator = 1 << 0, // Function is a generator function or async generator function
    Async = 1 << 1, // Function is an async function or an async generator function
    Invalid = 1 << 2, // Function is a signature or overload and does not have a body.
    AsyncGenerator = Async | Generator, // Function is an async generator function
}

/// <summary>types.ts TypePredicate (C5/C7 compute type predicates of signatures).</summary>
internal sealed class TypePredicateInfo
{
    public TypePredicateKind Kind;
    public string? ParameterName;
    public int ParameterIndex;
    public Type Type;
}

internal sealed partial class Checker
{
    // ---- symbols ----

    // checker.ts isReadonlySymbol
    private bool IsReadonlySymbol(SymbolRef symbol)
    {
        // The following symbols are considered read-only:
        // Properties with a 'readonly' modifier
        // Variables declared with 'const'
        // Get accessors without matching set accessors
        // Enum members
        // Object.defineProperty assignments with writable false or no setter
        // Unions and intersections of the above (unions and intersections eagerly set isReadonly on creation)
        var flags = SymbolFlagsOf(symbol);
        if ((GetCheckFlags(symbol) & CheckFlags.Readonly) != 0)
        {
            return true;
        }
        if ((flags & SymbolFlags.Property) != 0 && (GetDeclarationModifierFlagsFromSymbol(symbol) & ModifierFlags.Readonly) != 0)
        {
            return true;
        }
        if ((flags & SymbolFlags.Variable) != 0 && (GetDeclarationNodeFlagsFromSymbol(symbol) & NodeFlags.Constant) != 0)
        {
            return true;
        }
        if ((flags & SymbolFlags.Accessor) != 0 && (flags & SymbolFlags.SetAccessor) == 0)
        {
            return true;
        }
        if ((flags & SymbolFlags.EnumMember) != 0)
        {
            return true;
        }
        // (Object.defineProperty assignments: C9)
        return false;
    }

    // checker.ts getNonMissingTypeOfSymbol (exactOptionalPropertyTypes: the missing type is the undefined type otherwise)
    private Type GetNonMissingTypeOfSymbol(SymbolRef symbol) => RemoveMissingType(GetTypeOfSymbol(symbol), (SymbolFlagsOf(symbol) & SymbolFlags.Optional) != 0);

    // checker.ts containsNonMissingUndefinedType
    private bool ContainsNonMissingUndefinedType(Type type)
    {
        var candidate = (Types.Flags(type) & TypeFlags.Union) != 0 ? Types.Elements(Types.Union(type).Types)[0] : type;
        return (Types.Flags(candidate) & TypeFlags.Undefined) != 0 && candidate != missingType;
    }

    // checker.ts getParentSymbolOfTypeParameter
    private SymbolRef GetParentSymbolOfTypeParameter(Type typeParameter)
    {
        var symbol = Types.Symbol(typeParameter);
        var tp = symbol.IsNone ? NodeRef.None : GetDeclarationOfKind(symbol, SyntaxKind.TypeParameter);
        if (tp.IsNone)
        {
            return SymbolRef.None;
        }
        var host = Parent(tp);
        if (Kind(host) == SyntaxKind.JSDocTemplateTag)
        {
            // (getEffectiveContainerForJSDocTemplateTag: C9)
            return SymbolRef.None;
        }
        return GetSymbolOfNode(host);
    }

    // checker.ts getTypeParameterModifiers
    private ModifierFlags GetTypeParameterModifiers(Type typeParameter)
    {
        var result = ModifierFlags.None;
        var symbol = Types.Symbol(typeParameter);
        foreach (var d in symbol.IsNone ? [] : DeclarationsOf(symbol) ?? [])
        {
            if (Kind(d) == SyntaxKind.TypeParameter)
            {
                // getEffectiveModifierFlags (JSDoc modifiers: C9)
                result |= Utilities.GetSyntacticModifierFlagsNoCache(StoreOf(d), d.Node);
            }
        }
        return result & (ModifierFlags.In | ModifierFlags.Out | ModifierFlags.Const);
    }

    // checker.ts getTargetSymbol
    private SymbolRef GetTargetSymbol(SymbolRef symbol)
    {
        // if symbol is instantiated its flags are not copied from the 'target'
        // so we'll need to get back original 'target' symbol to work with correct set of flags
        if ((GetCheckFlags(symbol) & CheckFlags.Instantiated) != 0 && ValueSymbolLinks.Has(symbol))
        {
            var target = ValueSymbolLinks.Get(symbol).Target;
            if (!target.IsNone)
            {
                return target;
            }
        }
        return symbol;
    }

    // checker.ts getTypeParametersOfClassOrInterface
    private List<Type> GetTypeParametersOfClassOrInterface(SymbolRef symbol)
    {
        var result = new List<Type>();
        result.AddRange(GetOuterTypeParametersOfClassOrInterface(symbol) ?? []);
        result.AddRange(GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol) ?? []);
        return result;
    }

    // checker.ts isJSConstructor (JavaScript constructor functions: C9)
    private bool IsJSConstructor(NodeRef node)
    {
        if (node.IsNone || !Utilities.IsInJSFile(StoreOf(node), node.Node))
        {
            return false;
        }
        // (function declarations with `this` property assignments or @class tags: C9)
        return false;
    }

    // checker.ts getBaseTypeVariableOfClass (mixin classes: the base constructor type's type variable; C3)
    private Type GetBaseTypeVariableOfClass(SymbolRef symbol) => Type.None;

    // utilities.ts isLateBoundName: "__@" prefix
    private static bool IsLateBoundName(string name) =>
        name.Length > 2 && name[0] == '_' && name[1] == '_' && name[2] == '@';

    // the name type of a late-bound or computed member (SymbolLinks.nameType; C3 sets it)
    private Type GetNameTypeOfSymbol(SymbolRef symbol) =>
        ValueSymbolLinks.Has(symbol) ? ValueSymbolLinks.Get(symbol).NameType : Type.None;

    // checkExpression of a computed property name expression for the node builder's isStringNamed / late-bindable checks
    private Type CheckExpressionForNameType(NodeRef expression) => GetRegularTypeOfExpression(expression);

    // checker.ts getMinArgumentCount (the signature's minArgumentCount; strong arity for untyped JS: C9)
    private int GetMinArgumentCountForBuilder(Signature signature) => Signatures.Record(signature).MinArgumentCount;

    // getGlobalSymbol(name, Value, undefined)
    private SymbolRef GetGlobalSymbolForBuilder(string name) => GetGlobalSymbol(name, SymbolFlags.Value, null);

    // checker.ts getTypeOfAccessors for the node builder
    private Type GetTypeOfAccessorsForBuilder(SymbolRef symbol) => GetTypeOfAccessors(symbol);

    // checker.ts getAllAccessorDeclarationsForDeclaration: (firstAccessor, secondAccessor, getAccessor, setAccessor)
    private (NodeRef First, NodeRef Second, NodeRef Get, NodeRef Set) GetAllAccessorDeclarationsRef(NodeRef accessor)
    {
        var symbol = GetSymbolOfDeclaration(accessor);
        var get = GetDeclarationOfKind(symbol, SyntaxKind.GetAccessor);
        var set = GetDeclarationOfKind(symbol, SyntaxKind.SetAccessor);
        var first = accessor;
        var second = NodeRef.None;
        foreach (var d in DeclarationsOf(symbol) ?? [])
        {
            if (Kind(d) is SyntaxKind.GetAccessor or SyntaxKind.SetAccessor && d != accessor)
            {
                if (second.IsNone)
                {
                    second = d;
                }
            }
        }
        var declarations = DeclarationsOf(symbol) ?? [];
        var accessors = declarations.FindAll(d => Kind(d) is SyntaxKind.GetAccessor or SyntaxKind.SetAccessor);
        if (accessors.Count > 0)
        {
            first = accessors[0];
            second = accessors.Count > 1 ? accessors[1] : NodeRef.None;
        }
        return (first, second, get, set);
    }

    // checker.ts isExpandoFunctionDeclaration (JavaScript-style expando assignments to functions: C9)
    private bool IsExpandoFunctionDeclaration(NodeRef node) => false;

    // utilities.ts getFunctionFlags
    private static FunctionFlags GetFunctionFlagsOf(NodeStore store, Node node)
    {
        var flags = FunctionFlags.Normal;
        switch (store.Kind(node))
        {
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.MethodDeclaration:
                if (!store.AsteriskToken(node).IsNone)
                {
                    flags |= FunctionFlags.Generator;
                }
                goto case SyntaxKind.ArrowFunction;
            case SyntaxKind.ArrowFunction:
                if (Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Async))
                {
                    flags |= FunctionFlags.Async;
                }
                break;
        }
        if (store.Body(node).IsNone)
        {
            flags |= FunctionFlags.Invalid;
        }
        return flags;
    }

    // a signature created by the node builder (checker.ts createSignature for fake accessor signatures)
    private Signature CreateSignatureForBuilder(NodeRef declaration, SymbolRef[] parameters, Type returnType)
    {
        var signature = Signatures.Create(SignatureFlags.None, declaration, default, SymbolRef.None, parameters.Length);
        ref var record = ref Signatures.Record(signature);
        record.Parameters = parameters;
        record.ResolvedReturnType = returnType;
        return signature;
    }

    // checker.ts getExpandedParameters (rest parameters of tuple types: C3)
    private SymbolRef[][] GetExpandedParameters(Signature signature, bool skipUnionExpanding)
    {
        _ = skipUnionExpanding;
        return [Signatures.Record(signature).Parameters ?? []];
    }

    // checker.ts getTypePredicateOfSignature (C5/C7)
    private TypePredicateInfo? GetTypePredicateOfSignature(Signature signature) => null;

    // ---- instantiation (C3 seams not ported yet) ----

    private TypePredicateInfo InstantiateTypePredicate(TypePredicateInfo predicate, object mapper) => throw C3Seam();

    private Signature InstantiateSignatureForSymbol(Signature signature, SymbolRef symbol) => signature;

    private static NotSupportedException C3Seam() => new("TODO(C3): type kind or instantiation not ported yet.");

    // ---- type kinds (C3 seams not ported yet) ----

    private bool IsReferenceToGlobalIterableLikeType(Type type) => false;

    private bool IsTypeIdenticalTo(Type source, Type target) => source == target;

    private Type GetReverseMappedPropertyType(SymbolRef symbol) => throw C3Seam();

    private Type GetReverseMappedMappedType(SymbolRef symbol) => throw C3Seam();

    // checker.ts getOrCreateTypeFromSignature (C5)
    private Type GetOrCreateTypeFromSignature(Signature signature) => throw new NotSupportedException("TODO(C5): types of abstract construct signatures.");

    private Type GetResolvedTypeWithoutAbstractConstructSignatures(Type type, ObjectTypeData resolved) => throw new NotSupportedException("TODO(C5): types of abstract construct signatures.");
}
