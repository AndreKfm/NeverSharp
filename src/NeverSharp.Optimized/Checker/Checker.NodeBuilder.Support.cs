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
        foreach (var d in DeclarationsOf(symbol) ?? [])
        {
            if (IsReadonlyAssignmentDeclaration(d))
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts isReadonlyAssignmentDeclaration: Object.defineProperty assignments with writable false or no setter
    private bool IsReadonlyAssignmentDeclaration(NodeRef d)
    {
        var store = StoreOf(d);
        if (store.Kind(d.Node) != SyntaxKind.CallExpression || !Utilities.IsBindableObjectDefinePropertyCall(store, d.Node))
        {
            return false;
        }
        var objectLitType = CheckExpressionCached(At(d, store.Element(store.Arguments(d.Node), 2)));
        var valueType = GetTypeOfPropertyOfType(objectLitType, "value");
        if (!valueType.IsNone)
        {
            var writableProp = GetPropertyOfType(objectLitType, "writable");
            var writableType = writableProp.IsNone ? Type.None : GetTypeOfSymbol(writableProp);
            if (writableType.IsNone || writableType == falseType || writableType == regularFalseType)
            {
                return true;
            }
            // We include this definition whereupon we walk back and check the type at the declaration because
            // The usual definition of `Object.defineProperty` will _not_ cause literal types to be preserved in the
            // argument types, should the type be contextualized by the call itself.
            var writableDeclaration = ValueDeclarationOf(writableProp);
            if (!writableDeclaration.IsNone && Kind(writableDeclaration) == SyntaxKind.PropertyAssignment)
            {
                var rawOriginalType = CheckExpression(At(writableDeclaration, StoreOf(writableDeclaration).Initializer(writableDeclaration.Node)));
                if (rawOriginalType == falseType || rawOriginalType == regularFalseType)
                {
                    return true;
                }
            }
            return false;
        }
        return GetPropertyOfType(objectLitType, "set").IsNone;
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

    // checker.ts isJSConstructor
    private bool IsJSConstructor(NodeRef node)
    {
        if (node.IsNone || !Utilities.IsInJSFile(StoreOf(node), node.Node))
        {
            return false;
        }
        var store = StoreOf(node);
        var n = node.Node;
        var func = store.Kind(n) is SyntaxKind.FunctionDeclaration or SyntaxKind.FunctionExpression ? n
            : store.Kind(n) is SyntaxKind.VariableDeclaration or SyntaxKind.PropertyAssignment && !store.Initializer(n).IsNone && store.Kind(store.Initializer(n)) == SyntaxKind.FunctionExpression ? store.Initializer(n)
            : Node.None;
        if (func.IsNone)
        {
            return false;
        }
        // If the node has a @class or @constructor tag, treat it like a constructor.
        if (!GetFirstJSDocTagOfKind(store, n, SyntaxKind.JSDocClassTag).IsNone)
        {
            return true;
        }
        // If the node is a property of an object literal.
        if (store.Kind(WalkUpParenthesizedExpressions(store, store.Parent(func))) == SyntaxKind.PropertyAssignment)
        {
            return false;
        }
        // If the symbol of the node has members, treat it like a constructor.
        var symbol = GetSymbolOfDeclaration(At(node, func));
        return !symbol.IsNone && MembersOf(symbol) is { Count: > 0 };
    }

    // checker.ts getBaseTypeVariableOfClass
    private Type GetBaseTypeVariableOfClass(SymbolRef symbol)
    {
        var baseConstructorType = GetBaseConstructorTypeOfClass(GetDeclaredTypeOfClassOrInterface(symbol));
        if ((Types.Flags(baseConstructorType) & TypeFlags.TypeVariable) != 0)
        {
            return baseConstructorType;
        }
        if ((Types.Flags(baseConstructorType) & TypeFlags.Intersection) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(baseConstructorType).Types))
            {
                if ((Types.Flags(t) & TypeFlags.TypeVariable) != 0)
                {
                    return t;
                }
            }
        }
        return Type.None;
    }

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

    // checker.ts getExpandedParameters (module C6)
    private SymbolRef[][] GetExpandedParameters(Signature signature, bool skipUnionExpanding)
    {
        var parameters = Signatures.Record(signature).Parameters ?? [];
        if (SignatureHasRestParameter(signature))
        {
            var restIndex = parameters.Length - 1;
            var restSymbol = parameters[restIndex];
            var restType = GetTypeOfSymbol(restSymbol);
            if (IsTupleType(restType))
            {
                return [ExpandSignatureParametersWithTupleMembers(restType)];
            }
            if (!skipUnionExpanding && (Types.Flags(restType) & TypeFlags.Union) != 0 && AllTypes(Types.Elements(Types.Union(restType).Types), IsTupleType))
            {
                var members = Types.Elements(Types.Union(restType).Types).ToArray();
                return Array.ConvertAll(members, ExpandSignatureParametersWithTupleMembers);
            }

            SymbolRef[] ExpandSignatureParametersWithTupleMembers(Type tupleType)
            {
                var elementTypes = GetElementTypes(tupleType); // (getTypeArguments: the element types, without a this argument)
                var associatedNames = GetUniqAssociatedNamesFromTupleType(tupleType, restSymbol);
                var target = TupleTarget(tupleType);
                var result = new SymbolRef[restIndex + elementTypes.Length];
                Array.Copy(parameters, result, restIndex);
                for (var i = 0; i < elementTypes.Length; i++)
                {
                    // Lookup the label from the individual tuple passed in before falling back to the signature `rest` parameter name
                    var name = associatedNames is not null && i < associatedNames.Length && associatedNames[i] is { } associated ? associated : GetParameterNameAtPosition(signature, restIndex + i, tupleType);
                    var flags = target.ElementFlags![i];
                    var checkFlags = (flags & ElementFlags.Variable) != 0 ? CheckFlags.RestParameter : (flags & ElementFlags.Optional) != 0 ? CheckFlags.OptionalParameter : CheckFlags.None;
                    var symbol = CreateSymbol(SymbolFlags.FunctionScopedVariable, name, checkFlags);
                    var type = (flags & ElementFlags.Rest) != 0 ? CreateArrayType(elementTypes[i]) : elementTypes[i]; // (before the links are written)
                    ValueSymbolLinks.Get(symbol).ResolvedType = type;
                    result[restIndex + i] = symbol;
                }
                return result;
            }
        }
        return [parameters];
    }

    // checker.ts getUniqAssociatedNamesFromTupleType
    private string?[]? GetUniqAssociatedNamesFromTupleType(Type type, SymbolRef restSymbol)
    {
        var target = TupleTarget(type);
        if (target.LabeledElementDeclarations is not { } labels)
        {
            return null;
        }
        var names = new string?[Math.Min(labels.Length, target.ElementFlags!.Length)];
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = GetTupleElementLabel(labels[i], i, target.ElementFlags![i], restSymbol);
        }
        var duplicates = new List<int>();
        var uniqueNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
        {
            if (!uniqueNames.Add(names[i]!))
            {
                duplicates.Add(i);
            }
        }
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var i in duplicates)
        {
            var counter = counters.TryGetValue(names[i]!, out var c) ? c : 1;
            string name;
            while (!uniqueNames.Add(name = names[i] + "_" + counter.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            {
                counter++;
            }
            names[i] = name;
            counters[names[i]!] = counter + 1;
        }
        return names;
    }

    // ---- instantiation (C3 seams not ported yet) ----

    private Signature InstantiateSignatureForSymbol(Signature signature, SymbolRef symbol) => signature;

    private static NotSupportedException C3Seam() => new("TODO(C3): type kind or instantiation not ported yet.");

    // ---- type kinds (C3 seams not ported yet) ----

    // checker.ts typeReferenceToTypeNode: isReferenceToSomeType(type, [Iterable, IterableIterator, AsyncIterable, AsyncIterableIterator])
    private bool IsReferenceToGlobalIterableLikeType(Type type) =>
        IsReferenceToType(type, GetGlobalIterableType(reportErrors: false)) ||
        IsReferenceToType(type, GetGlobalTypeCached(ref deferredGlobalIterableIteratorType, "IterableIterator", 3, false)) ||
        IsReferenceToType(type, GetGlobalAsyncIterableType(reportErrors: false)) ||
        IsReferenceToType(type, GetGlobalTypeCached(ref deferredGlobalAsyncIterableIteratorType, "AsyncIterableIterator", 3, false));

    // (GetReverseMappedPropertyType, GetReverseMappedMappedType: Checker.Inference.cs, module C6)

    // (getOrCreateTypeFromSignature, getResolvedTypeWithoutAbstractConstructSignatures: Checker.Signatures.cs, module C5)
}
