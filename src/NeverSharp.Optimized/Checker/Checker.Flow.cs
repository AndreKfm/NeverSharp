// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (control flow analysis: TypeFacts and getTypeFacts, references, discriminants,
// assigned and initial types, evolving arrays, effects signatures, reachability, assignment marking)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/flow.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C7. Flow nodes belong to the binder output of shared files, which
// a checker never writes: flow node ids and the caches keyed by them are per checker (keyed by file and flow node), and
// the reduce label's temporary replacement of its target's antecedents is an override table of the checker. Ported:
// getTypeFacts, getTypeWithFacts, getAdjustedTypeWithFacts, isMatchingReference, getAccessedPropertyName,
// isDiscriminantProperty, getKeyPropertyName, getAssignmentReducedType, getAssignedType, getInitialType, evolving array
// types, getEffectsSignature, isReachableFlowNode, isPostSuperFlowNode, isConstantReference, isSymbolAssigned,
// markNodeAssignments, getNarrowableTypeForReference. getFlowTypeOfReference and narrowing are in
// Checker.Narrowing.cs, the uses (checkIdentifier, property access, statement checks) in Checker.FlowCheck.cs.

using System.Runtime.InteropServices;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>A flow node of a program file (flow nodes are per file).</summary>
internal readonly record struct FlowRef(int File, FlowNode Node)
{
    public bool IsNone => Node.IsNone;
}

/// <summary>checker.ts FlowType: a type, or an incomplete type (flags 0) seen while a loop is analyzed.</summary>
internal readonly record struct FlowType(Type Type, bool Incomplete);

internal sealed partial class Checker
{
    // checker.ts typeofNEFacts
    private static TypeFacts TypeofNEFacts(string text) => text switch
    {
        "string" => TypeFacts.TypeofNEString,
        "number" => TypeFacts.TypeofNENumber,
        "bigint" => TypeFacts.TypeofNEBigInt,
        "boolean" => TypeFacts.TypeofNEBoolean,
        "symbol" => TypeFacts.TypeofNESymbol,
        "undefined" => TypeFacts.NEUndefined,
        "object" => TypeFacts.TypeofNEObject,
        "function" => TypeFacts.TypeofNEFunction,
        _ => TypeFacts.None,
    };

    // ---- the checker's flow state (createTypeChecker's vars) ----

    private bool flowAnalysisDisabled;
    private int flowInvocationCount;
    private int flowLoopStart;
    private int flowLoopCount;
    private int sharedFlowCount;
    private int inlineLevel;
    private readonly List<FlowRef> flowLoopNodes = [];
    private readonly List<string> flowLoopKeys = [];
    private readonly List<List<Type>> flowLoopTypes = [];
    private readonly List<FlowRef> sharedFlowNodes = [];
    private readonly List<FlowType> sharedFlowTypes = [];
    private readonly Dictionary<FlowRef, bool> flowNodeReachable = [];
    private readonly Dictionary<FlowRef, Dictionary<string, Type>> flowLoopCaches = [];
    private FlowRef lastFlowNode;
    private bool lastFlowNodeReachable;
    // the reduce label's `target.antecedent = node.antecedents` while its antecedent is analyzed
    private readonly Dictionary<FlowRef, BinderStore.FlowChain> labelAntecedentOverrides = [];
    // checker.ts getCachedType / setCachedType (the A and N keys of flow analysis)
    private readonly Dictionary<string, Type> flowCachedTypes = new(StringComparer.Ordinal);
    // checker.ts evolvingArrayTypes and EvolvingArrayType's elementType / finalArrayType
    private readonly Dictionary<Type, Type> evolvingArrayTypes = [];
    private readonly Dictionary<Type, Type> evolvingArrayElementTypes = [];
    private readonly Dictionary<Type, Type> evolvingArrayFinalTypes = [];
    // UnionType.keyPropertyName / constituentMap
    // Symbol.lastAssignmentPos (binder symbols are not written; 0 = undefined, double.MaxValue as in TypeScript)
    private readonly Dictionary<SymbolRef, double> lastAssignmentPositions = [];
    // NodeLinks.switchTypes
    private readonly Dictionary<NodeRef, Type[]> switchClauseTypes = [];

    private FlowNode FlowNodeOf(NodeRef node) => BindingOf(node.File).FlowNodeOf(node.Node);

    private FlowFlags FlowFlagsOf(FlowRef flow) => flow.File == 0 ? syntheticFlows[flow.Node.Index - 1].Flags : BindingOf(flow.File).Flags(flow.Node);

    private NodeRef FlowNodeNode(FlowRef flow) => flow.File == 0 ? syntheticFlows[flow.Node.Index - 1].Node : At(new NodeRef(flow.File, default), BindingOf(flow.File).NodeOf(flow.Node));

    private FlowRef FlowAntecedent(FlowRef flow) => flow.File == 0 ? syntheticFlows[flow.Node.Index - 1].Antecedent : new(flow.File, BindingOf(flow.File).Antecedent(flow.Node));

    // The flow nodes the checker creates (checker.ts createFlowNode in checkIfExpressionRefinesParameter): FlowRef with
    // File 0 and the 1-based index into this list; never labels or switch clauses.
    private readonly List<(FlowFlags Flags, NodeRef Node, FlowRef Antecedent)> syntheticFlows = [];

    private FlowRef CreateSyntheticFlowNode(FlowFlags flags, NodeRef node, FlowRef antecedent)
    {
        syntheticFlows.Add((flags, node, antecedent));
        return new FlowRef(0, new FlowNode(syntheticFlows.Count));
    }

    // FlowLabel.antecedent (with the reduce label's override)
    private BinderStore.FlowChain LabelAntecedents(FlowRef label) =>
        labelAntecedentOverrides.TryGetValue(label, out var chain) ? chain : BindingOf(label.File).Antecedents(label.Node);

    private List<FlowRef> LabelAntecedentList(FlowRef label)
    {
        var list = new List<FlowRef>();
        foreach (var antecedent in LabelAntecedents(label))
        {
            list.Add(new FlowRef(label.File, antecedent));
        }
        return list;
    }

    // Runs body with the reduce label's antecedents installed on its target (and the previous state restored).
    private T WithReducedLabel<T>(FlowRef reduceLabel, Func<T> body)
    {
        var binding = BindingOf(reduceLabel.File);
        var target = new FlowRef(reduceLabel.File, binding.ReduceTarget(reduceLabel.Node));
        var had = labelAntecedentOverrides.TryGetValue(target, out var previous);
        labelAntecedentOverrides[target] = binding.ReduceAntecedents(reduceLabel.Node);
        try
        {
            return body();
        }
        finally
        {
            if (had)
            {
                labelAntecedentOverrides[target] = previous;
            }
            else
            {
                labelAntecedentOverrides.Remove(target);
            }
        }
    }

    // ---- type facts ----

    // ---- small type predicates ----

    private static bool IsTypeAny(TypeStore types, Type type) => !type.IsNone && (types.Flags(type) & TypeFlags.Any) != 0;

    // checker.ts recombineUnknownType
    private Type RecombineUnknownType(Type type) => type == unknownUnionType ? unknownType : type;

    // checker.ts isUnitLikeType: intersections that reduce to never (e.g. 'T & null' where 'T extends {}') are not unit types
    private bool IsUnitLikeType(Type type)
    {
        var t = GetBaseConstraintOrType(type);
        return (Types.Flags(t) & TypeFlags.Intersection) != 0 ? Array.Exists(Types.Elements(Types.Union(t).Types).ToArray(), IsUnitType) : IsUnitType(t);
    }

    // checker.ts extractUnitType
    private Type ExtractUnitType(Type type)
    {
        if ((Types.Flags(type) & TypeFlags.Intersection) != 0)
        {
            foreach (var e in Types.Elements(Types.Union(type).Types))
            {
                if (IsUnitType(e))
                {
                    return e;
                }
            }
        }
        return type;
    }

    // checker.ts getCachedType / setCachedType
    private Type GetOrSetFlowCachedType(string? key, Func<Type> compute)
    {
        if (key is null)
        {
            return compute();
        }
        if (flowCachedTypes.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var type = compute();
        flowCachedTypes[key] = type;
        return type;
    }

    // ---- the synthetic reference `this.<name>` ----

    // isPropertyInitializedInConstructor and getFlowTypeInConstructor analyze a reference TypeScript creates with the
    // factory (`this.x` with the constructor as parent). Track O's nodes are records of a file's store, which a checker
    // does not write: the reference is this sentinel, with its name and parent in the fields below, and the functions
    // flow analysis applies to references answer for it as for that property access.
    private static readonly NodeRef syntheticThisAccess = new(-1, new Node(1));
    private string? syntheticThisAccessName;

    private static bool IsSyntheticReference(NodeRef node) => node.File == -1;

    // isMatchingReference(source, `this`) for a source that is not the synthetic reference
    private bool IsMatchingThisSource(NodeRef source)
    {
        var store = StoreOf(source);
        switch (store.Kind(source.Node))
        {
            case SyntaxKind.ThisKeyword:
                return true;
            case SyntaxKind.Identifier:
                return IsThisInTypeQuery(store, source.Node);
            case SyntaxKind.NonNullExpression:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.SatisfiesExpression:
                return IsMatchingThisSource(At(source, store.Expression(source.Node)));
            case SyntaxKind.BinaryExpression:
                return store.Kind(store.OperatorToken(source.Node)) == SyntaxKind.CommaToken && IsMatchingThisSource(At(source, store.Right(source.Node)));
        }
        return false;
    }

    // isMatchingReference(`this`, target)
    private bool IsMatchingThisTarget(NodeRef target)
    {
        var store = StoreOf(target);
        switch (store.Kind(target.Node))
        {
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.NonNullExpression:
                return IsMatchingThisTarget(At(target, store.Expression(target.Node)));
            case SyntaxKind.BinaryExpression:
            {
                var op = store.Kind(store.OperatorToken(target.Node));
                return (op == SyntaxKind.EqualsToken && IsMatchingThisTarget(At(target, store.Left(target.Node)))) ||
                    (op == SyntaxKind.CommaToken && IsMatchingThisTarget(At(target, store.Right(target.Node))));
            }
        }
        return store.Kind(target.Node) == SyntaxKind.ThisKeyword;
    }

    // isMatchingReference(source, the synthetic reference)
    private bool IsMatchingSyntheticTarget(NodeRef source)
    {
        var store = StoreOf(source);
        var s = source.Node;
        switch (store.Kind(s))
        {
            case SyntaxKind.NonNullExpression:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.SatisfiesExpression:
                return IsMatchingSyntheticTarget(At(source, store.Expression(s)));
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            {
                var name = GetAccessedPropertyName(source);
                return name is not null && name == syntheticThisAccessName && IsMatchingThisSource(At(source, store.Expression(s)));
            }
            case SyntaxKind.QualifiedName:
                return store.Texts.GetString(store.EscapedText(store.Right(s))) == syntheticThisAccessName && IsMatchingThisSource(At(source, store.Left(s)));
            case SyntaxKind.BinaryExpression:
                return store.Kind(store.OperatorToken(s)) == SyntaxKind.CommaToken && IsMatchingSyntheticTarget(At(source, store.Right(s)));
        }
        return false;
    }

    /// <summary>checker.ts isPropertyInitializedInStaticBlocks (module C8).</summary>
    private bool IsPropertyInitializedInStaticBlocks(NodeRef propName, Type propType, List<NodeRef> staticBlocks, int startPos, int endPos)
    {
        var store = StoreOf(propName);
        var name = store.Texts.GetString(store.EscapedText(propName.Node));
        foreach (var staticBlock in staticBlocks)
        {
            // static block must be within the provided range as they are evaluated in document order (unlike constructors)
            var pos = StoreOf(staticBlock).Pos(staticBlock.Node);
            if (pos < startPos || pos > endPos)
            {
                continue;
            }
            var returnFlowNode = BindingOf(staticBlock.File).ReturnFlowNodeOf(staticBlock.Node);
            Type flowType;
            if (returnFlowNode.IsNone)
            {
                flowType = propType; // (reference.flowNode undefined: the declared type)
            }
            else
            {
                var saveName = syntheticThisAccessName;
                syntheticThisAccessName = name;
                try
                {
                    flowType = GetFlowTypeOfReference(syntheticThisAccess, propType, GetOptionalType(propType), NodeRef.None, new FlowRef(staticBlock.File, returnFlowNode));
                }
                finally
                {
                    syntheticThisAccessName = saveName;
                }
            }
            if (!ContainsUndefinedType(flowType))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>checker.ts isPropertyInitializedInConstructor.</summary>
    private bool IsPropertyInitializedInConstructor(NodeRef propName, Type propType, NodeRef constructor)
    {
        var store = StoreOf(propName);
        var name = store.Kind(propName.Node) == SyntaxKind.ComputedPropertyName
            ? AccessedNameOfExpression(At(propName, store.Expression(propName.Node)))
            : store.Texts.GetString(store.EscapedText(propName.Node));
        var returnFlowNode = BindingOf(constructor.File).ReturnFlowNodeOf(constructor.Node);
        if (returnFlowNode.IsNone)
        {
            // (reference.flowNode undefined: the declared type)
            return !ContainsUndefinedType(propType);
        }
        var saveName = syntheticThisAccessName;
        syntheticThisAccessName = name;
        try
        {
            var flowType = GetFlowTypeOfReference(syntheticThisAccess, propType, GetOptionalType(propType), NodeRef.None, new FlowRef(constructor.File, returnFlowNode));
            return !ContainsUndefinedType(flowType);
        }
        finally
        {
            syntheticThisAccessName = saveName;
        }
    }

    // tryGetElementAccessExpressionName of an element access whose argument is the expression
    private string? AccessedNameOfExpression(NodeRef argument)
    {
        var store = StoreOf(argument);
        if (Utilities.IsStringOrNumericLiteralLike(store, argument.Node))
        {
            return Utilities.EscapeLeadingUnderscores(store.Texts.GetString(store.Text(argument.Node))!);
        }
        return Utilities.IsEntityNameExpression(store, argument.Node) ? TryGetNameFromEntityNameExpression(argument) : null;
    }

    // ---- references ----

    /// <summary>checker.ts isMatchingReference.</summary>
    private bool IsMatchingReference(NodeRef source, NodeRef target)
    {
        if (IsSyntheticReference(target))
        {
            return !IsSyntheticReference(source) && IsMatchingSyntheticTarget(source);
        }
        var store = StoreOf(target);
        var t = target.Node;
        switch (store.Kind(t))
        {
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.NonNullExpression:
                return IsMatchingReference(source, At(target, store.Expression(t)));
            case SyntaxKind.BinaryExpression:
            {
                var op = store.Kind(store.OperatorToken(t));
                return (Utilities.IsAssignmentOperator(op) && op == SyntaxKind.EqualsToken && IsMatchingReference(source, At(target, store.Left(t)))) ||
                    (op == SyntaxKind.CommaToken && IsMatchingReference(source, At(target, store.Right(t))));
            }
        }
        if (IsSyntheticReference(source))
        {
            // the synthetic `this.<name>`: an access expression with the same name on `this`
            if (!Utilities.IsAccessExpression(store, t))
            {
                return false;
            }
            var targetName = GetAccessedPropertyName(target);
            return targetName is not null && targetName == syntheticThisAccessName && IsMatchingThisTarget(At(target, store.Expression(t)));
        }
        var s = source.Node;
        switch (store.Kind(s))
        {
            case SyntaxKind.MetaProperty:
                return store.Kind(t) == SyntaxKind.MetaProperty && store.KeywordToken(s) == store.KeywordToken(t) &&
                    store.Texts.Equals(store.EscapedText(store.Name(s)), store.Texts.GetString(store.EscapedText(store.Name(t)))!);
            case SyntaxKind.Identifier:
            case SyntaxKind.PrivateIdentifier:
                if (IsThisInTypeQuery(store, s))
                {
                    return store.Kind(t) == SyntaxKind.ThisKeyword;
                }
                return (store.Kind(t) == SyntaxKind.Identifier && GetResolvedSymbol(source) == GetResolvedSymbol(target)) ||
                    (store.Kind(t) is SyntaxKind.VariableDeclaration or SyntaxKind.BindingElement &&
                        GetExportSymbolOfValueSymbolIfExported(GetResolvedSymbol(source)) == GetSymbolOfDeclaration(target));
            case SyntaxKind.ThisKeyword:
                return store.Kind(t) == SyntaxKind.ThisKeyword;
            case SyntaxKind.SuperKeyword:
                return store.Kind(t) == SyntaxKind.SuperKeyword;
            case SyntaxKind.NonNullExpression:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.SatisfiesExpression:
                return IsMatchingReference(At(source, store.Expression(s)), target);
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            {
                var sourcePropertyName = GetAccessedPropertyName(source);
                if (sourcePropertyName is not null)
                {
                    var targetPropertyName = Utilities.IsAccessExpression(store, t) ? GetAccessedPropertyName(target) : null;
                    if (targetPropertyName is not null)
                    {
                        return targetPropertyName == sourcePropertyName && IsMatchingReference(At(source, store.Expression(s)), At(target, store.Expression(t)));
                    }
                }
                if (store.Kind(s) == SyntaxKind.ElementAccessExpression && store.Kind(t) == SyntaxKind.ElementAccessExpression &&
                    store.Kind(store.ArgumentExpression(s)) == SyntaxKind.Identifier && store.Kind(store.ArgumentExpression(t)) == SyntaxKind.Identifier)
                {
                    var symbol = GetResolvedSymbol(At(source, store.ArgumentExpression(s)));
                    if (symbol == GetResolvedSymbol(At(target, store.ArgumentExpression(t))) &&
                        (IsConstantVariable(symbol) || (IsParameterOrMutableLocalVariable(symbol) && !IsSymbolAssigned(symbol))))
                    {
                        return IsMatchingReference(At(source, store.Expression(s)), At(target, store.Expression(t)));
                    }
                }
                break;
            }
            case SyntaxKind.QualifiedName:
                return Utilities.IsAccessExpression(store, t) &&
                    store.Texts.GetString(store.EscapedText(store.Right(s))) == GetAccessedPropertyName(target) &&
                    IsMatchingReference(At(source, store.Left(s)), At(target, store.Expression(t)));
            case SyntaxKind.BinaryExpression:
                return store.Kind(store.OperatorToken(s)) == SyntaxKind.CommaToken && IsMatchingReference(At(source, store.Right(s)), target);
        }
        return false;
    }

    /// <summary>checker.ts getAccessedPropertyName (null: undefined).</summary>
    private string? GetAccessedPropertyName(NodeRef access)
    {
        if (IsSyntheticReference(access))
        {
            return syntheticThisAccessName;
        }
        var store = StoreOf(access);
        var n = access.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.PropertyAccessExpression:
                return store.Texts.GetString(store.EscapedText(store.Name(n)));
            case SyntaxKind.ElementAccessExpression:
                return TryGetElementAccessExpressionName(access);
            case SyntaxKind.BindingElement:
            {
                var name = GetDestructuringPropertyName(access);
                return name is null ? null : Utilities.EscapeLeadingUnderscores(name);
            }
            case SyntaxKind.Parameter:
                return store.Nodes(store.Parameters(store.Parent(n))).IndexOf(n).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return null;
    }

    // utilities.ts getDestructuringPropertyName
    private string? GetDestructuringPropertyName(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var parent = store.Parent(n);
        if (store.Kind(n) == SyntaxKind.BindingElement && store.Kind(parent) == SyntaxKind.ObjectBindingPattern)
        {
            var propertyName = store.PropertyName(n);
            var name = !propertyName.IsNone ? propertyName : store.Name(n);
            return GetTextOfPropertyNameOrNull(At(node, name));
        }
        if (store.Kind(n) == SyntaxKind.BindingElement && store.Kind(parent) == SyntaxKind.ArrayBindingPattern)
        {
            return store.Nodes(store.Elements(parent)).IndexOf(n).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return null;
    }

    // utilities.ts tryGetTextOfPropertyName (identifiers, private names, string and numeric literals, literal computed names)
    private string? GetTextOfPropertyNameOrNull(NodeRef name)
    {
        var store = StoreOf(name);
        var n = name.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.PrivateIdentifier:
                return Utilities.UnescapeLeadingUnderscores(store.Texts.GetString(store.EscapedText(n))!);
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                return store.Texts.GetString(store.Text(n));
            case SyntaxKind.ComputedPropertyName:
            {
                var expression = store.Expression(n);
                return Utilities.IsStringOrNumericLiteralLike(store, expression) ? store.Texts.GetString(store.Text(expression)) : null;
            }
        }
        return null;
    }

    // checker.ts tryGetElementAccessExpressionName
    private string? TryGetElementAccessExpressionName(NodeRef node)
    {
        var store = StoreOf(node);
        var argument = store.ArgumentExpression(node.Node);
        if (Utilities.IsStringOrNumericLiteralLike(store, argument))
        {
            return Utilities.EscapeLeadingUnderscores(store.Texts.GetString(store.Text(argument))!);
        }
        return Utilities.IsEntityNameExpression(store, argument) ? TryGetNameFromEntityNameExpression(At(node, argument)) : null;
    }

    // checker.ts tryGetNameFromEntityNameExpression
    private string? TryGetNameFromEntityNameExpression(NodeRef node)
    {
        var symbol = ResolveEntityName(node, SymbolFlags.Value, ignoreErrors: true);
        if (symbol.IsNone || !(IsConstantVariable(symbol) || (SymbolFlagsOf(symbol) & SymbolFlags.EnumMember) != 0))
        {
            return null;
        }
        var declaration = ValueDeclarationOf(symbol);
        if (declaration.IsNone)
        {
            return null;
        }
        var store = StoreOf(declaration);
        var typeNode = GetEffectiveTypeAnnotationNode(declaration);
        if (!typeNode.IsNone)
        {
            var name = TryGetNameFromType(GetTypeFromTypeNode(typeNode));
            if (name is not null)
            {
                return name;
            }
        }
        if (store.Kind(declaration.Node) is SyntaxKind.VariableDeclaration or SyntaxKind.Parameter or SyntaxKind.BindingElement or SyntaxKind.PropertyDeclaration
            or SyntaxKind.PropertyAssignment or SyntaxKind.EnumMember && IsBlockScopedNameDeclaredBeforeUse(declaration, node))
        {
            var initializer = store.Kind(declaration.Node) == SyntaxKind.PropertyAssignment || store.Kind(declaration.Node) == SyntaxKind.EnumMember ||
                store.Kind(declaration.Node) is SyntaxKind.VariableDeclaration or SyntaxKind.Parameter or SyntaxKind.BindingElement or SyntaxKind.PropertyDeclaration
                ? store.Initializer(declaration.Node) : Node.None;
            if (!initializer.IsNone)
            {
                var initializerType = Utilities.IsBindingPattern(store, store.Parent(declaration.Node))
                    ? GetTypeOfSymbol(symbol) // (getTypeForBindingElement: C5)
                    : GetTypeOfExpression(At(declaration, initializer));
                return initializerType.IsNone ? null : TryGetNameFromType(initializerType);
            }
            if (store.Kind(declaration.Node) == SyntaxKind.EnumMember)
            {
                return GetTextOfPropertyNameOrNull(At(declaration, store.Name(declaration.Node)));
            }
        }
        return null;
    }

    // checker.ts containsMatchingReference
    private bool ContainsMatchingReference(NodeRef source, NodeRef target)
    {
        if (IsSyntheticReference(source))
        {
            return !IsSyntheticReference(target) && IsMatchingThisTarget(target);
        }
        var store = StoreOf(source);
        while (Utilities.IsAccessExpression(store, source.Node))
        {
            source = At(source, store.Expression(source.Node));
            if (IsMatchingReference(source, target))
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts optionalChainContainsReference
    private bool OptionalChainContainsReference(NodeRef source, NodeRef target)
    {
        if (IsSyntheticReference(source))
        {
            return false;
        }
        var store = StoreOf(source);
        while (UtilitiesPublic.IsOptionalChain(store, source.Node))
        {
            source = At(source, store.Expression(source.Node));
            if (IsMatchingReference(source, target))
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts getUnionOrIntersectionProperty (the property of the union as getPropertyOfType creates it)
    private SymbolRef GetUnionOrIntersectionProperty(Type type, string name) => GetPropertyOfUnionOrIntersectionType(type, name, skipObjectFunctionPropertyAugment: false);

    // checker.ts isOrContainsMatchingReference
    private bool IsOrContainsMatchingReference(NodeRef source, NodeRef target) => IsMatchingReference(source, target) || ContainsMatchingReference(source, target);

    // checker.ts hasMatchingArgument
    private bool HasMatchingArgument(NodeRef expression, NodeRef reference)
    {
        var store = StoreOf(expression);
        var arguments = store.Arguments(expression.Node);
        if (!arguments.IsNone)
        {
            foreach (var argument in store.Nodes(arguments))
            {
                if (IsOrContainsMatchingReference(reference, At(expression, argument)) || OptionalChainContainsReference(At(expression, argument), reference))
                {
                    return true;
                }
            }
        }
        var callee = store.Expression(expression.Node);
        return store.Kind(callee) == SyntaxKind.PropertyAccessExpression && IsOrContainsMatchingReference(reference, At(expression, store.Expression(callee)));
    }

    // checker.ts typeMaybeAssignableTo
    private bool TypeMaybeAssignableTo(Type source, Type target)
    {
        if ((Types.Flags(source) & TypeFlags.Union) == 0)
        {
            return IsTypeAssignableTo(source, target);
        }
        foreach (var t in Types.Elements(Types.Union(source).Types).ToArray())
        {
            if (IsTypeAssignableTo(t, target))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>checker.ts getAssignmentReducedType.</summary>
    private Type GetAssignmentReducedType(Type declaredType, Type assignedType)
    {
        if (declaredType == assignedType)
        {
            return declaredType;
        }
        if ((Types.Flags(assignedType) & TypeFlags.Never) != 0)
        {
            return assignedType;
        }
        var key = "A" + declaredType.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + assignedType.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return GetOrSetFlowCachedType(key, () => GetAssignmentReducedTypeWorker(declaredType, assignedType));
    }

    private Type GetAssignmentReducedTypeWorker(Type declaredType, Type assignedType)
    {
        var filteredType = FilterType(declaredType, t => TypeMaybeAssignableTo(assignedType, t));
        // Ensure that we narrow to fresh types if the assignment is a fresh boolean literal type.
        var reducedType = (Types.Flags(assignedType) & TypeFlags.BooleanLiteral) != 0 && IsFreshLiteralType(assignedType) ? MapType(filteredType, GetFreshTypeOfLiteralType) : filteredType;
        // Our crude heuristic produces an invalid result in some cases: see GH#26130.
        // For now, when that happens, we give up and don't narrow at all.  (This also
        // means we'll never narrow for erroneous assignments where the assigned type
        // is not assignable to the declared type.)
        return IsTypeAssignableTo(assignedType, reducedType) ? reducedType : declaredType;
    }

    // ---- assigned and initial types ----

    // checker.ts getTypeWithDefault
    private Type GetTypeWithDefault(Type type, NodeRef defaultExpression) =>
        defaultExpression.IsNone ? type : GetUnionType([GetNonUndefinedType(type), GetTypeOfExpression(defaultExpression)]);

    // checker.ts getTypeOfDestructuredProperty
    private Type GetTypeOfDestructuredProperty(Type type, NodeRef name)
    {
        var nameType = GetLiteralTypeFromPropertyName(name);
        if (!IsTypeUsableAsPropertyName(nameType))
        {
            return errorType;
        }
        var text = GetPropertyNameFromType(nameType);
        var propType = GetTypeOfPropertyOfType(type, text);
        if (!propType.IsNone)
        {
            return propType;
        }
        var info = GetApplicableIndexInfoForName(type, text);
        return info is not null ? IncludeUndefinedInIndexSignature(info.Type) : errorType;
    }

    // checker.ts getTypeOfDestructuredArrayElement (TODO(C5): checkIteratedTypeOrElementType for non-tuple types; the
    // element type of arrays is used)
    private Type GetTypeOfDestructuredArrayElement(Type type, int index)
    {
        if (EveryType(type, IsTupleType))
        {
            var element = GetIndexedAccessTypeOrUndefined(type, GetNumberLiteralType(index));
            if (!element.IsNone)
            {
                return element;
            }
        }
        var elementType = IsArrayType(type) ? GetElementTypeOfArrayType(type) : Type.None;
        return !elementType.IsNone ? IncludeUndefinedInIndexSignature(elementType) : errorType;
    }

    // checker.ts getAssignedTypeOfBinaryExpression
    private Type GetAssignedTypeOfBinaryExpression(NodeRef node)
    {
        var store = StoreOf(node);
        var parent = store.Parent(node.Node);
        var isDestructuringDefaultAssignment = (store.Kind(parent) == SyntaxKind.ArrayLiteralExpression && IsDestructuringAssignmentTarget(store, parent)) ||
            (store.Kind(parent) == SyntaxKind.PropertyAssignment && IsDestructuringAssignmentTarget(store, store.Parent(parent)));
        return isDestructuringDefaultAssignment
            ? GetTypeWithDefault(GetAssignedType(node), At(node, store.Right(node.Node)))
            : GetTypeOfExpression(At(node, store.Right(node.Node)));
    }

    // checker.ts isDestructuringAssignmentTarget
    private static bool IsDestructuringAssignmentTarget(NodeStore store, Node parent)
    {
        var grandParent = store.Parent(parent);
        return (store.Kind(grandParent) == SyntaxKind.BinaryExpression && store.Left(grandParent) == parent) ||
            (store.Kind(grandParent) == SyntaxKind.ForOfStatement && store.Initializer(grandParent) == parent);
    }

    /// <summary>checker.ts getAssignedType.</summary>
    private Type GetAssignedType(NodeRef node)
    {
        var store = StoreOf(node);
        var parent = At(node, store.Parent(node.Node));
        switch (store.Kind(parent.Node))
        {
            case SyntaxKind.ForInStatement:
                return stringType;
            case SyntaxKind.ForOfStatement:
                return CheckRightHandSideOfForOfForFlow(parent);
            case SyntaxKind.BinaryExpression:
                return GetAssignedTypeOfBinaryExpression(parent);
            case SyntaxKind.DeleteExpression:
                return undefinedType;
            case SyntaxKind.ArrayLiteralExpression:
                return GetTypeOfDestructuredArrayElement(GetAssignedType(parent), store.Nodes(store.Elements(parent.Node)).IndexOf(node.Node));
            case SyntaxKind.SpreadElement:
                // getTypeOfDestructuredSpreadExpression (TODO(C5): iteration types)
                return CreateArrayType(errorType);
            case SyntaxKind.PropertyAssignment:
                return GetTypeOfDestructuredProperty(GetAssignedType(Parent(parent)), At(parent, store.Name(parent.Node)));
            case SyntaxKind.ShorthandPropertyAssignment:
                return GetTypeWithDefault(GetTypeOfDestructuredProperty(GetAssignedType(Parent(parent)), At(parent, store.Name(parent.Node))),
                    At(parent, store.ObjectAssignmentInitializer(parent.Node)));
        }
        return errorType;
    }

    // checker.ts checkRightHandSideOfForOf (TODO(C5): iteration types; the element type of arrays and tuples, string
    // characters, the error type otherwise)
    private Type CheckRightHandSideOfForOfForFlow(NodeRef statement)
    {
        var store = StoreOf(statement);
        var expressionType = CheckExpressionCached(At(statement, store.Expression(statement.Node)));
        if (IsArrayType(expressionType))
        {
            return GetElementTypeOfArrayType(expressionType);
        }
        if (IsTupleType(expressionType))
        {
            var element = GetIndexedAccessTypeOrUndefined(expressionType, numberType);
            return element.IsNone ? errorType : element;
        }
        if ((Types.Flags(expressionType) & TypeFlags.StringLike) != 0)
        {
            return stringType;
        }
        return (Types.Flags(expressionType) & TypeFlags.Any) != 0 ? expressionType : errorType;
    }

    // checker.ts getInitialTypeOfBindingElement (TODO(C5): getTypeForBindingElement; C7 uses the declared type of the
    // element's symbol, which the C1 approximation computes)
    private Type GetInitialTypeOfBindingElement(NodeRef node)
    {
        var symbol = GetSymbolOfDeclaration(node);
        return symbol.IsNone ? errorType : GetTypeOfSymbol(symbol);
    }

    // checker.ts getInitialTypeOfVariableDeclaration
    private Type GetInitialTypeOfVariableDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var initializer = store.Initializer(node.Node);
        if (!initializer.IsNone)
        {
            return GetTypeOfInitializer(At(node, initializer));
        }
        var statement = store.Parent(store.Parent(node.Node));
        if (store.Kind(statement) == SyntaxKind.ForInStatement)
        {
            return stringType;
        }
        if (store.Kind(statement) == SyntaxKind.ForOfStatement)
        {
            return CheckRightHandSideOfForOfForFlow(At(node, statement));
        }
        return errorType;
    }

    // checker.ts getInitialType
    private Type GetInitialType(NodeRef node) =>
        Kind(node) == SyntaxKind.VariableDeclaration ? GetInitialTypeOfVariableDeclaration(node) : GetInitialTypeOfBindingElement(node);

    // checker.ts isEmptyArrayAssignment
    private bool IsEmptyArrayAssignment(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (store.Kind(n) == SyntaxKind.VariableDeclaration && !store.Initializer(n).IsNone && IsEmptyArrayLiteral(store, store.Initializer(n)))
        {
            return true;
        }
        var parent = store.Parent(n);
        return store.Kind(n) != SyntaxKind.BindingElement && store.Kind(parent) == SyntaxKind.BinaryExpression && IsEmptyArrayLiteral(store, store.Right(parent));
    }

    // checker.ts getReferenceCandidate
    private static Node GetReferenceCandidate(NodeStore store, Node node)
    {
        while (true)
        {
            switch (store.Kind(node))
            {
                case SyntaxKind.ParenthesizedExpression:
                    node = store.Expression(node);
                    continue;
                case SyntaxKind.BinaryExpression:
                    switch (store.Kind(store.OperatorToken(node)))
                    {
                        case SyntaxKind.EqualsToken:
                        case SyntaxKind.BarBarEqualsToken:
                        case SyntaxKind.AmpersandAmpersandEqualsToken:
                        case SyntaxKind.QuestionQuestionEqualsToken:
                            node = store.Left(node);
                            continue;
                        case SyntaxKind.CommaToken:
                            node = store.Right(node);
                            continue;
                    }
                    return node;
                default:
                    return node;
            }
        }
    }

    // checker.ts getReferenceRoot
    private static Node GetReferenceRoot(NodeStore store, Node node)
    {
        while (true)
        {
            var parent = store.Parent(node);
            if (store.Kind(parent) == SyntaxKind.ParenthesizedExpression ||
                (store.Kind(parent) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(parent)) == SyntaxKind.EqualsToken && store.Left(parent) == node) ||
                (store.Kind(parent) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(parent)) == SyntaxKind.CommaToken && store.Right(parent) == node))
            {
                node = parent;
                continue;
            }
            return node;
        }
    }

    // checker.ts getTypeOfSwitchClause / getSwitchClauseTypes
    private Type[] GetSwitchClauseTypes(NodeRef switchStatement)
    {
        if (!switchClauseTypes.TryGetValue(switchStatement, out var types))
        {
            var store = StoreOf(switchStatement);
            var clauses = store.Nodes(store.Clauses(store.CaseBlock(switchStatement.Node)));
            types = new Type[clauses.Length];
            switchClauseTypes[switchStatement] = types;
            for (var i = 0; i < clauses.Length; i++)
            {
                types[i] = store.Kind(clauses[i]) == SyntaxKind.CaseClause
                    ? GetRegularTypeOfLiteralType(GetTypeOfExpression(At(switchStatement, store.Expression(clauses[i]))))
                    : neverType;
            }
        }
        return types;
    }

    // checker.ts getSwitchClauseTypeOfWitnesses (null entries: the default clause or a duplicate)
    private string?[]? GetSwitchClauseTypeOfWitnesses(NodeRef switchStatement)
    {
        var store = StoreOf(switchStatement);
        var clauses = store.Nodes(store.Clauses(store.CaseBlock(switchStatement.Node)));
        foreach (var clause in clauses)
        {
            if (store.Kind(clause) == SyntaxKind.CaseClause && !Utilities.IsStringLiteralLike(store, store.Expression(clause)))
            {
                return null;
            }
        }
        var witnesses = new string?[clauses.Length];
        for (var i = 0; i < clauses.Length; i++)
        {
            var text = store.Kind(clauses[i]) == SyntaxKind.CaseClause ? store.Texts.GetString(store.Text(store.Expression(clauses[i]))) : null;
            witnesses[i] = !string.IsNullOrEmpty(text) && Array.IndexOf(witnesses, text, 0, i) < 0 ? text : null;
        }
        return witnesses;
    }

    // checker.ts eachTypeContainedIn
    private bool EachTypeContainedIn(Type source, Type[] types)
    {
        if ((Types.Flags(source) & TypeFlags.Union) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(source).Types))
            {
                if (Array.IndexOf(types, t) < 0)
                {
                    return false;
                }
            }
            return true;
        }
        return Array.IndexOf(types, source) >= 0;
    }

    // checker.ts replacePrimitivesWithLiterals
    private Type ReplacePrimitivesWithLiterals(Type typeWithPrimitives, Type typeWithLiterals)
    {
        if (MaybeTypeOfKind(typeWithPrimitives, TypeFlags.String | TypeFlags.TemplateLiteral | TypeFlags.Number | TypeFlags.BigInt) &&
            MaybeTypeOfKind(typeWithLiterals, TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping | TypeFlags.NumberLiteral | TypeFlags.BigIntLiteral))
        {
            return MapType(typeWithPrimitives, t =>
                (Types.Flags(t) & TypeFlags.String) != 0 ? ExtractTypesOfKind(typeWithLiterals, TypeFlags.String | TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping) :
                IsPatternLiteralType(t) && !MaybeTypeOfKind(typeWithLiterals, TypeFlags.String | TypeFlags.TemplateLiteral | TypeFlags.StringMapping) ? ExtractTypesOfKind(typeWithLiterals, TypeFlags.StringLiteral) :
                (Types.Flags(t) & TypeFlags.Number) != 0 ? ExtractTypesOfKind(typeWithLiterals, TypeFlags.Number | TypeFlags.NumberLiteral) :
                (Types.Flags(t) & TypeFlags.BigInt) != 0 ? ExtractTypesOfKind(typeWithLiterals, TypeFlags.BigInt | TypeFlags.BigIntLiteral) : t);
        }
        return typeWithPrimitives;
    }

    private static Type GetTypeFromFlowType(FlowType flowType) => flowType.Type;

    private static bool IsIncomplete(FlowType flowType) => flowType.Incomplete;

    // checker.ts createFlowType
    private FlowType CreateFlowType(Type type, bool incomplete) =>
        incomplete ? new FlowType((Types.Flags(type) & TypeFlags.Never) != 0 ? silentNeverType : type, true) : new FlowType(type, false);

    // ---- evolving array types ----

    // checker.ts getEvolvingArrayType / createEvolvingArrayType
    private Type GetEvolvingArrayType(Type elementType)
    {
        if (!evolvingArrayTypes.TryGetValue(elementType, out var result))
        {
            result = CreateObjectType(ObjectFlags.EvolvingArray, SymbolRef.None);
            evolvingArrayElementTypes[result] = elementType;
            evolvingArrayTypes[elementType] = result;
        }
        return result;
    }

    // checker.ts addEvolvingArrayElementType (TODO(C5): getContextFreeTypeOfExpression; the type of the expression)
    private Type AddEvolvingArrayElementType(Type evolvingArrayType, NodeRef node)
    {
        var elementType = GetBaseTypeOfLiteralType(GetTypeOfExpression(node));
        var current = evolvingArrayElementTypes[evolvingArrayType];
        return IsTypeSubsetOf(elementType, current) ? evolvingArrayType : GetEvolvingArrayType(GetUnionType([current, elementType]));
    }

    // checker.ts createFinalArrayType / getFinalArrayType
    private Type GetFinalArrayType(Type evolvingArrayType)
    {
        if (!evolvingArrayFinalTypes.TryGetValue(evolvingArrayType, out var final))
        {
            var elementType = evolvingArrayElementTypes[evolvingArrayType];
            final = (Types.Flags(elementType) & TypeFlags.Never) != 0
                ? autoArrayType
                : CreateArrayType((Types.Flags(elementType) & TypeFlags.Union) != 0
                    ? GetUnionType(Types.Elements(Types.Union(elementType).Types), UnionReduction.Subtype)
                    : elementType);
            evolvingArrayFinalTypes[evolvingArrayType] = final;
        }
        return final;
    }

    /// <summary>checker.ts finalizeEvolvingArrayType.</summary>
    private Type FinalizeEvolvingArrayType(Type type) =>
        (Types.ObjectFlagsOf(type) & ObjectFlags.EvolvingArray) != 0 ? GetFinalArrayType(type) : type;

    private Type GetElementTypeOfEvolvingArrayType(Type type) =>
        (Types.ObjectFlagsOf(type) & ObjectFlags.EvolvingArray) != 0 ? evolvingArrayElementTypes[type] : neverType;

    private bool IsEvolvingArrayTypeList(List<Type> types)
    {
        var hasEvolvingArrayType = false;
        foreach (var t in types)
        {
            if ((Types.Flags(t) & TypeFlags.Never) == 0)
            {
                if ((Types.ObjectFlagsOf(t) & ObjectFlags.EvolvingArray) == 0)
                {
                    return false;
                }
                hasEvolvingArrayType = true;
            }
        }
        return hasEvolvingArrayType;
    }

    // checker.ts isEvolvingArrayOperationTarget: 'x' in an 'x.length', x.push(value)', 'x.unshift(value)' or
    // 'x[n] = value' operation, where 'n' is an expression of type any, undefined, or a number-like type.
    private bool IsEvolvingArrayOperationTarget(NodeRef node)
    {
        if (IsSyntheticReference(node))
        {
            return false; // (its parent is the constructor)
        }
        var store = StoreOf(node);
        var root = GetReferenceRoot(store, node.Node);
        var parent = store.Parent(root);
        var isLengthPushOrUnshift = store.Kind(parent) == SyntaxKind.PropertyAccessExpression && (
            store.Texts.Equals(store.EscapedText(store.Name(parent)), "length") ||
            (store.Kind(store.Parent(parent)) == SyntaxKind.CallExpression && store.Kind(store.Name(parent)) == SyntaxKind.Identifier &&
                Utilities.IsPushOrUnshiftIdentifier(store, store.Name(parent))));
        var isElementAssignment = store.Kind(parent) == SyntaxKind.ElementAccessExpression &&
            store.Expression(parent) == root &&
            store.Kind(store.Parent(parent)) == SyntaxKind.BinaryExpression &&
            store.Kind(store.OperatorToken(store.Parent(parent))) == SyntaxKind.EqualsToken &&
            store.Left(store.Parent(parent)) == parent &&
            !Utilities.IsAssignmentTarget(store, store.Parent(parent)) &&
            IsTypeAssignableToKind(GetTypeOfExpression(At(node, store.ArgumentExpression(parent))), TypeFlags.NumberLike);
        return isLengthPushOrUnshift || isElementAssignment;
    }

    // ---- effects of calls ----

    // checker.ts isDeclarationWithExplicitTypeAnnotation
    private bool IsDeclarationWithExplicitTypeAnnotation(NodeRef node)
    {
        var store = StoreOf(node);
        var kind = store.Kind(node.Node);
        if (kind is not (SyntaxKind.VariableDeclaration or SyntaxKind.PropertyDeclaration or SyntaxKind.PropertySignature or SyntaxKind.Parameter))
        {
            return false;
        }
        if (!GetEffectiveTypeAnnotationNode(node).IsNone)
        {
            return true;
        }
        if (Utilities.IsInJSFile(store, node.Node) && kind != SyntaxKind.PropertySignature)
        {
            var initializer = store.Initializer(node.Node);
            return !initializer.IsNone && store.Kind(initializer) is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction && !store.Type(initializer).IsNone;
        }
        return false;
    }

    // checker.ts getExplicitTypeOfSymbol
    private Type GetExplicitTypeOfSymbol(SymbolRef symbol, ProgramDiagnostic? diagnostic)
    {
        symbol = ResolveSymbol(symbol);
        var flags = SymbolFlagsOf(symbol);
        if ((flags & (SymbolFlags.Function | SymbolFlags.Method | SymbolFlags.Class | SymbolFlags.ValueModule)) != 0)
        {
            return GetTypeOfSymbol(symbol);
        }
        if ((flags & (SymbolFlags.Variable | SymbolFlags.Property)) != 0)
        {
            if ((GetCheckFlags(symbol) & CheckFlags.Mapped) != 0)
            {
                // (MappedSymbol.links.syntheticOrigin: the origin's explicit type; module C3 does not keep the origin)
            }
            var declaration = ValueDeclarationOf(symbol);
            if (!declaration.IsNone)
            {
                if (IsDeclarationWithExplicitTypeAnnotation(declaration))
                {
                    return GetTypeOfSymbol(symbol);
                }
                var store = StoreOf(declaration);
                if (store.Kind(declaration.Node) == SyntaxKind.VariableDeclaration && store.Kind(store.Parent(store.Parent(declaration.Node))) == SyntaxKind.ForOfStatement)
                {
                    var statement = At(declaration, store.Parent(store.Parent(declaration.Node)));
                    var expressionType = GetTypeOfDottedName(At(statement, store.Expression(statement.Node)), null);
                    if (!expressionType.IsNone)
                    {
                        // checkIteratedTypeOrElementType (TODO(C5): iteration types)
                        return IsArrayType(expressionType) ? GetElementTypeOfArrayType(expressionType) : Type.None;
                    }
                }
                if (diagnostic is not null)
                {
                    (diagnostic.RelatedInformation ??= []).Add(CreateRelatedForNode(declaration, Diagnostics._0_needs_an_explicit_type_annotation, SymbolToString(symbol)));
                }
            }
        }
        return Type.None;
    }

    // checker.ts getTypeOfDottedName: the dotted function name in an assertion expression must be comprised of
    // identifiers that reference function, method, class or value module symbols; or variable, property or parameter
    // symbols with declarations that have explicit type annotations.
    private Type GetTypeOfDottedName(NodeRef node, ProgramDiagnostic? diagnostic)
    {
        var store = StoreOf(node);
        if ((store.Flags(node.Node) & NodeFlags.InWithStatement) != 0)
        {
            return Type.None;
        }
        switch (store.Kind(node.Node))
        {
            case SyntaxKind.Identifier:
                return GetExplicitTypeOfSymbol(GetExportSymbolOfValueSymbolIfExported(GetResolvedSymbol(node)), diagnostic);
            case SyntaxKind.ThisKeyword:
                return GetExplicitThisType(node);
            case SyntaxKind.SuperKeyword:
                return CheckSuperExpression(node);
            case SyntaxKind.PropertyAccessExpression:
            {
                var type = GetTypeOfDottedName(At(node, store.Expression(node.Node)), diagnostic);
                if (!type.IsNone)
                {
                    var name = store.Name(node.Node);
                    SymbolRef prop;
                    if (store.Kind(name) == SyntaxKind.PrivateIdentifier)
                    {
                        var typeSymbol = Types.Symbol(type);
                        if (typeSymbol.IsNone)
                        {
                            return Type.None;
                        }
                        // getSymbolNameForPrivateIdentifier(type.symbol, name.escapedText): the member the private
                        // name declares (the binder's name for it)
                        var declared = LookupSymbolForPrivateIdentifierDeclaration(At(node, name));
                        prop = declared.IsNone ? SymbolRef.None : GetPropertyOfType(type, NameOf(declared));
                    }
                    else
                    {
                        prop = GetPropertyOfType(type, store.Texts.GetString(store.EscapedText(name))!);
                    }
                    return prop.IsNone ? Type.None : GetExplicitTypeOfSymbol(prop, diagnostic);
                }
                return Type.None;
            }
            case SyntaxKind.ParenthesizedExpression:
                return GetTypeOfDottedName(At(node, store.Expression(node.Node)), diagnostic);
        }
        return Type.None;
    }

    // checker.ts getExplicitThisType
    private Type GetExplicitThisType(NodeRef node)
    {
        var store = StoreOf(node);
        var container = Utilities.GetThisContainer(store, node.Node, includeArrowFunctions: false, includeClassComputedPropertyName: false);
        if (Utilities.IsFunctionLike(store, container))
        {
            var signature = GetSignatureFromDeclaration(At(node, container));
            var thisParameter = Signatures.Record(signature).ThisParameter;
            if (!thisParameter.IsNone)
            {
                return GetExplicitTypeOfSymbol(thisParameter, null);
            }
        }
        var parent = store.Parent(container);
        if (!parent.IsNone && Utilities.IsClassLike(store, parent))
        {
            var symbol = GetSymbolOfDeclaration(At(node, parent));
            return Utilities.IsStatic(store, container) ? GetTypeOfSymbol(symbol) : ThisTypeOfClassOrInterface(GetDeclaredTypeOfSymbol(symbol));
        }
        return Type.None;
    }

    // checker.ts unknownSignature as the effects-signature marker (not created: signature ids follow creation order)
    private static readonly Signature unknownEffectsSignature = new(-1);

    /// <summary>checker.ts getEffectsSignature (TODO(C5): getResolvedSignature for overloaded or generic candidates).</summary>
    private Signature GetEffectsSignature(NodeRef node)
    {
        ref var links = ref SignatureLinks.Get(node);
        var signature = links.EffectsSignature;
        if (signature.IsNone)
        {
            // A call expression parented by an expression statement is a potential assertion. Other call
            // expressions are potential type predicate function calls. In order to avoid triggering
            // circularities in control flow analysis, we use getTypeOfDottedName when resolving the call
            // target expression of an assertion.
            var store = StoreOf(node);
            var n = node.Node;
            var funcType = Type.None;
            if (store.Kind(n) == SyntaxKind.BinaryExpression)
            {
                var rightType = CheckNonNullExpression(At(node, store.Right(n)));
                funcType = GetSymbolHasInstanceMethodOfObjectType(rightType);
            }
            else if (store.Kind(store.Parent(n)) == SyntaxKind.ExpressionStatement)
            {
                funcType = GetTypeOfDottedName(At(node, store.Expression(n)), null);
            }
            else if (store.Kind(store.Expression(n)) != SyntaxKind.SuperKeyword)
            {
                var callee = At(node, store.Expression(n));
                funcType = UtilitiesPublic.IsOptionalChain(store, n)
                    ? CheckNonNullType(GetOptionalExpressionType(CheckExpression(callee), callee), callee)
                    : CheckNonNullExpression(callee);
            }
            var signatures = GetSignaturesOfType(!funcType.IsNone ? GetApparentType(funcType) : unknownType, SignatureKind.Call);
            var candidate = signatures.Count == 1 && Signatures.Record(signatures[0]).TypeParameters.Count == 0 ? signatures[0]
                : signatures.Exists(HasTypePredicateOrNeverReturnType) && store.Kind(n) == SyntaxKind.CallExpression ? GetResolvedSignature(node) // (instanceof: TODO(C5) resolveInstanceofExpression)
                : Signature.None;
            signature = !candidate.IsNone && HasTypePredicateOrNeverReturnType(candidate) ? candidate : unknownEffectsSignature;
            SignatureLinks.Get(node).EffectsSignature = signature;
        }
        return signature == unknownEffectsSignature ? Signature.None : signature;
    }

    // checker.ts hasTypePredicateOrNeverReturnType
    private bool HasTypePredicateOrNeverReturnType(Signature signature)
    {
        if (GetTypePredicateOfSignature(signature) is not null)
        {
            return true;
        }
        var declaration = Signatures.Record(signature).Declaration;
        if (declaration.IsNone)
        {
            return false;
        }
        var returnType = GetReturnTypeFromAnnotation(declaration);
        return (Types.Flags(returnType.IsNone ? unknownType : returnType) & TypeFlags.Never) != 0;
    }

    // checker.ts getTypePredicateArgument
    private NodeRef GetTypePredicateArgument(TypePredicateInfo predicate, NodeRef callExpression)
    {
        var store = StoreOf(callExpression);
        if (predicate.Kind is TypePredicateKind.Identifier or TypePredicateKind.AssertsIdentifier)
        {
            var arguments = store.Arguments(callExpression.Node);
            return predicate.ParameterIndex >= 0 && predicate.ParameterIndex < store.Length(arguments) ? At(callExpression, store.Element(arguments, predicate.ParameterIndex)) : NodeRef.None;
        }
        var invokedExpression = Utilities.SkipParentheses(store, store.Expression(callExpression.Node));
        return Utilities.IsAccessExpression(store, invokedExpression) ? At(callExpression, Utilities.SkipParentheses(store, store.Expression(invokedExpression))) : NodeRef.None;
    }

    // checker.ts reportFlowControlError
    private void ReportFlowControlError(NodeRef node)
    {
        var store = StoreOf(node);
        var block = node.Node;
        while (!block.IsNone && !IsFunctionOrModuleBlock(store, block))
        {
            block = store.Parent(block);
        }
        var file = FileAt(node.File);
        var span = Utilities.GetSpanOfTokenAtPosition(store, file.Root, store.Pos(store.Statements(block)));
        diagnostics.Add(ProgramDiagnostic.FromFile(file, Utilities.CreateFileDiagnostic(store, file.Root, span.Start, span.Length, Diagnostics.The_containing_function_or_module_body_is_too_large_for_control_flow_analysis)));
    }

    // utilities.ts isFunctionOrModuleBlock
    private static bool IsFunctionOrModuleBlock(NodeStore store, Node node) =>
        store.Kind(node) is SyntaxKind.SourceFile or SyntaxKind.ModuleBlock ||
        (store.Kind(node) == SyntaxKind.Block && Utilities.IsFunctionLike(store, store.Parent(node)));

    // ---- reachability ----

    /// <summary>checker.ts isReachableFlowNode.</summary>
    internal bool IsReachableFlowNode(FlowRef flow)
    {
        var result = IsReachableFlowNodeWorker(flow, noCacheCheck: false);
        lastFlowNode = flow;
        lastFlowNodeReachable = result;
        return result;
    }

    // checker.ts isFalseExpression
    private static bool IsFalseExpression(NodeStore store, Node expr)
    {
        var node = Utilities.SkipParentheses(store, expr, true);
        if (store.Kind(node) == SyntaxKind.FalseKeyword)
        {
            return true;
        }
        if (store.Kind(node) == SyntaxKind.BinaryExpression)
        {
            var op = store.Kind(store.OperatorToken(node));
            return (op == SyntaxKind.AmpersandAmpersandToken && (IsFalseExpression(store, store.Left(node)) || IsFalseExpression(store, store.Right(node)))) ||
                (op == SyntaxKind.BarBarToken && IsFalseExpression(store, store.Left(node)) && IsFalseExpression(store, store.Right(node)));
        }
        return false;
    }

    private bool IsReachableFlowNodeWorker(FlowRef flow, bool noCacheCheck)
    {
        while (true)
        {
            if (flow == lastFlowNode)
            {
                return lastFlowNodeReachable;
            }
            var flags = FlowFlagsOf(flow);
            if ((flags & FlowFlags.Shared) != 0)
            {
                if (!noCacheCheck)
                {
                    if (flowNodeReachable.TryGetValue(flow, out var cached))
                    {
                        return cached;
                    }
                    var reachable = IsReachableFlowNodeWorker(flow, noCacheCheck: true);
                    flowNodeReachable[flow] = reachable;
                    return reachable;
                }
                noCacheCheck = false;
            }
            if ((flags & (FlowFlags.Assignment | FlowFlags.Condition | FlowFlags.ArrayMutation)) != 0)
            {
                flow = FlowAntecedent(flow);
            }
            else if ((flags & FlowFlags.Call) != 0)
            {
                var call = FlowNodeNode(flow);
                var signature = GetEffectsSignature(call);
                if (!signature.IsNone)
                {
                    var predicate = GetTypePredicateOfSignature(signature);
                    if (predicate is not null && predicate.Kind == TypePredicateKind.AssertsIdentifier && predicate.Type.IsNone)
                    {
                        var store = StoreOf(call);
                        var arguments = store.Arguments(call.Node);
                        if (predicate.ParameterIndex >= 0 && predicate.ParameterIndex < store.Length(arguments) && IsFalseExpression(store, store.Element(arguments, predicate.ParameterIndex)))
                        {
                            return false;
                        }
                    }
                    if ((Types.Flags(GetReturnTypeOfSignature(signature)) & TypeFlags.Never) != 0)
                    {
                        return false;
                    }
                }
                flow = FlowAntecedent(flow);
            }
            else if ((flags & FlowFlags.BranchLabel) != 0)
            {
                // A branching point is reachable if any branch is reachable.
                foreach (var f in LabelAntecedentList(flow))
                {
                    if (IsReachableFlowNodeWorker(f, noCacheCheck: false))
                    {
                        return true;
                    }
                }
                return false;
            }
            else if ((flags & FlowFlags.LoopLabel) != 0)
            {
                var antecedents = LabelAntecedents(flow);
                if (antecedents.IsUndefined)
                {
                    return false;
                }
                // A loop is reachable if the control flow path that leads to the top is reachable.
                var first = new FlowRef(flow.File, FlowNode.None);
                foreach (var a in antecedents)
                {
                    first = new FlowRef(flow.File, a);
                    break;
                }
                flow = first;
            }
            else if ((flags & FlowFlags.SwitchClause) != 0)
            {
                // The control flow path representing an unmatched value in a switch statement with
                // no default clause is unreachable if the switch statement is exhaustive.
                var binding = BindingOf(flow.File);
                if (binding.ClauseStart(flow.Node) == binding.ClauseEnd(flow.Node) && IsExhaustiveSwitchStatement(FlowNodeNode(flow)))
                {
                    return false;
                }
                flow = FlowAntecedent(flow);
            }
            else if ((flags & FlowFlags.ReduceLabel) != 0)
            {
                // Cache is unreliable once we start adjusting labels
                lastFlowNode = default;
                var reduceLabel = flow;
                return WithReducedLabel(reduceLabel, () => IsReachableFlowNodeWorker(FlowAntecedent(reduceLabel), noCacheCheck: false));
            }
            else
            {
                return (flags & FlowFlags.Unreachable) == 0;
            }
        }
    }

    /// <summary>checker.ts isConstantReference.</summary>
    private bool IsConstantReference(NodeRef node)
    {
        if (IsSyntheticReference(node))
        {
            return false; // (`this.x` without a resolved symbol: isReadonlySymbol(unknownSymbol))
        }
        var store = StoreOf(node);
        var n = node.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.ThisKeyword:
                return true;
            case SyntaxKind.Identifier:
                if (!IsThisInTypeQuery(store, n))
                {
                    var symbol = GetResolvedSymbol(node);
                    var valueDeclaration = ValueDeclarationOf(symbol);
                    return IsConstantVariable(symbol) ||
                        (IsParameterOrMutableLocalVariable(symbol) && !IsSymbolAssigned(symbol)) ||
                        (!valueDeclaration.IsNone && Kind(valueDeclaration) == SyntaxKind.FunctionExpression);
                }
                break;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            {
                // The resolvedSymbol property is initialized by checkPropertyAccess or checkElementAccess before we get here.
                var resolved = SymbolNodeLinks.Has(node) ? SymbolNodeLinks.Get(node).ResolvedSymbol : SymbolRef.None;
                return IsConstantReference(At(node, store.Expression(n))) && IsReadonlySymbol(resolved.IsNone ? unknownSymbol : resolved);
            }
            case SyntaxKind.ObjectBindingPattern:
            case SyntaxKind.ArrayBindingPattern:
            {
                var rootDeclaration = At(node, Utilities.GetRootDeclaration(store, store.Parent(n)));
                var rootKind = Kind(rootDeclaration);
                return rootKind == SyntaxKind.Parameter || IsCatchClauseVariableDeclaration(StoreOf(rootDeclaration), rootDeclaration.Node)
                    ? !IsSomeSymbolAssigned(rootDeclaration)
                    : rootKind == SyntaxKind.VariableDeclaration && IsVarConstLike(StoreOf(rootDeclaration), rootDeclaration.Node);
            }
        }
        return false;
    }

    private static bool IsCatchClauseVariableDeclaration(NodeStore store, Node node) =>
        store.Kind(node) == SyntaxKind.VariableDeclaration && store.Kind(store.Parent(node)) == SyntaxKind.CatchClause;

    // ---- assignments ----

    // checker.ts isSymbolAssignedDefinitely
    private bool IsSymbolAssignedDefinitely(SymbolRef symbol)
    {
        if (lastAssignmentPositions.TryGetValue(symbol, out var pos) && pos != 0)
        {
            return pos < 0;
        }
        return IsSymbolAssigned(symbol) && lastAssignmentPositions.TryGetValue(symbol, out pos) && pos < 0;
    }

    /// <summary>checker.ts isSymbolAssigned: a parameter, catch variable, or mutable local variable is assigned anywhere.</summary>
    private bool IsSymbolAssigned(SymbolRef symbol) => !IsPastLastAssignment(symbol, NodeRef.None);

    // checker.ts isPastLastAssignment
    private bool IsPastLastAssignment(SymbolRef symbol, NodeRef location)
    {
        var valueDeclaration = ValueDeclarationOf(symbol);
        if (valueDeclaration.IsNone)
        {
            return false;
        }
        var parent = FindAncestorFunctionOrSourceFile(valueDeclaration);
        if (parent.IsNone)
        {
            return false;
        }
        ref var links = ref NodeLinks.Get(parent);
        if ((links.Flags & NodeCheckFlags.AssignmentsMarked) == 0)
        {
            links.Flags |= NodeCheckFlags.AssignmentsMarked;
            if (!HasParentWithAssignmentsMarked(parent))
            {
                MarkNodeAssignments(parent);
            }
        }
        var last = lastAssignmentPositions.TryGetValue(symbol, out var p) ? p : 0;
        return last == 0 || (!location.IsNone && Math.Abs(last) < StoreOf(location).Pos(location.Node));
    }

    // findAncestor(node, isFunctionOrSourceFile), starting at the node itself
    private NodeRef FindAncestorFunctionOrSourceFile(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        while (!n.IsNone && !(Utilities.IsFunctionLikeDeclaration(store, n) || store.Kind(n) == SyntaxKind.SourceFile))
        {
            n = store.Parent(n);
        }
        return At(node, n);
    }

    // checker.ts isSomeSymbolAssigned
    private bool IsSomeSymbolAssigned(NodeRef rootDeclaration) => IsSomeSymbolAssignedWorker(At(rootDeclaration, StoreOf(rootDeclaration).Name(rootDeclaration.Node)));

    private bool IsSomeSymbolAssignedWorker(NodeRef node)
    {
        var store = StoreOf(node);
        if (store.Kind(node.Node) == SyntaxKind.Identifier)
        {
            return IsSymbolAssigned(GetSymbolOfDeclaration(Parent(node)));
        }
        foreach (var e in store.Nodes(store.Elements(node.Node)))
        {
            if (store.Kind(e) != SyntaxKind.OmittedExpression && IsSomeSymbolAssignedWorker(At(node, store.Name(e))))
            {
                return true;
            }
        }
        return false;
    }

    private bool HasParentWithAssignmentsMarked(NodeRef node)
    {
        var store = StoreOf(node);
        for (var n = store.Parent(node.Node); !n.IsNone; n = store.Parent(n))
        {
            if ((Utilities.IsFunctionLikeDeclaration(store, n) || store.Kind(n) == SyntaxKind.SourceFile) &&
                NodeLinks.Has(At(node, n)) && (NodeLinks.Get(At(node, n)).Flags & NodeCheckFlags.AssignmentsMarked) != 0)
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts markNodeAssignments: for all assignments within the given root node, record the last assignment source
    // position for all referenced parameters and mutable local variables.
    private void MarkNodeAssignments(NodeRef root)
    {
        var store = StoreOf(root);
        var stack = new Stack<Node>();
        stack.Push(root.Node);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            var node = At(root, n);
            switch (store.Kind(n))
            {
                case SyntaxKind.Identifier:
                {
                    var assignmentTarget = GetAssignmentTargetKind(store, n);
                    if (assignmentTarget != AssignmentKind.None)
                    {
                        var symbol = GetResolvedSymbol(node);
                        var has = lastAssignmentPositions.TryGetValue(symbol, out var last);
                        var hasDefiniteAssignment = assignmentTarget == AssignmentKind.Definite || (has && last < 0);
                        if (IsParameterOrMutableLocalVariable(symbol))
                        {
                            if (!has || last == 0 || Math.Abs(last) != double.MaxValue)
                            {
                                var referencingFunction = FindAncestorFunctionOrSourceFile(node);
                                var valueDeclaration = ValueDeclarationOf(symbol);
                                var declaringFunction = FindAncestorFunctionOrSourceFile(valueDeclaration);
                                last = referencingFunction == declaringFunction ? ExtendAssignmentPosition(node, valueDeclaration) : double.MaxValue;
                            }
                            if (hasDefiniteAssignment && last > 0)
                            {
                                last *= -1;
                            }
                            lastAssignmentPositions[symbol] = last;
                        }
                    }
                    continue;
                }
                case SyntaxKind.ExportSpecifier:
                {
                    var exportDeclaration = store.Parent(store.Parent(n));
                    var propertyName = store.PropertyName(n);
                    var name = !propertyName.IsNone ? propertyName : store.Name(n);
                    if (!store.IsTypeOnly(n) && !store.IsTypeOnly(exportDeclaration) && store.ModuleSpecifier(exportDeclaration).IsNone && store.Kind(name) != SyntaxKind.StringLiteral)
                    {
                        var symbol = ResolveEntityName(At(root, name), SymbolFlags.Value, ignoreErrors: true, dontResolveAlias: true);
                        if (!symbol.IsNone && IsParameterOrMutableLocalVariable(symbol))
                        {
                            var sign = lastAssignmentPositions.TryGetValue(symbol, out var last) && last < 0 ? -1 : 1;
                            lastAssignmentPositions[symbol] = sign * double.MaxValue;
                        }
                    }
                    continue;
                }
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.TypeAliasDeclaration:
                case SyntaxKind.EnumDeclaration:
                    continue;
            }
            if (IsTypeNodeKind(store.Kind(n)))
            {
                continue;
            }
            // forEachChild in source order: pushed in reverse
            var children = new List<Node>();
            NodeVisitor.ForEachChild<object>(store, n, child =>
            {
                children.Add(child);
                return null;
            });
            for (var i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }
    }

    // checker.ts extendAssignmentPosition: the position of the assignment extended to the end of any intervening
    // variable statement, expression statement, compound statement, or class declaration
    private double ExtendAssignmentPosition(NodeRef node, NodeRef declaration)
    {
        var store = StoreOf(node);
        var n = node.Node;
        double pos = store.Pos(n);
        var declarationPos = StoreOf(declaration).Pos(declaration.Node);
        while (!n.IsNone && store.Pos(n) > declarationPos)
        {
            switch (store.Kind(n))
            {
                case SyntaxKind.VariableStatement:
                case SyntaxKind.ExpressionStatement:
                case SyntaxKind.IfStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                case SyntaxKind.WithStatement:
                case SyntaxKind.SwitchStatement:
                case SyntaxKind.TryStatement:
                case SyntaxKind.ClassDeclaration:
                    pos = store.End(n);
                    break;
            }
            n = store.Parent(n);
        }
        return pos;
    }

    /// <summary>checker.ts isConstantVariable.</summary>
    private bool IsConstantVariable(SymbolRef symbol) =>
        (SymbolFlagsOf(symbol) & SymbolFlags.Variable) != 0 && (GetDeclarationNodeFlagsFromSymbol(symbol) & NodeFlags.Constant) != 0;

    /// <summary>checker.ts isParameterOrMutableLocalVariable.</summary>
    private bool IsParameterOrMutableLocalVariable(SymbolRef symbol)
    {
        // Return true if symbol is a parameter, a catch clause variable, or a mutable local variable
        var valueDeclaration = ValueDeclarationOf(symbol);
        if (valueDeclaration.IsNone)
        {
            return false;
        }
        var store = StoreOf(valueDeclaration);
        var declaration = Utilities.GetRootDeclaration(store, valueDeclaration.Node);
        return store.Kind(declaration) == SyntaxKind.Parameter ||
            (store.Kind(declaration) == SyntaxKind.VariableDeclaration && (store.Kind(store.Parent(declaration)) == SyntaxKind.CatchClause || IsMutableLocalVariableDeclaration(At(valueDeclaration, declaration))));
    }

    // checker.ts isMutableLocalVariableDeclaration: a non-exported and non-global `let` variable
    private bool IsMutableLocalVariableDeclaration(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var list = store.Parent(declaration.Node);
        if ((store.Flags(list) & NodeFlags.Let) == 0)
        {
            return false;
        }
        var statement = store.Parent(list);
        return !((Utilities.GetCombinedModifierFlags(store, declaration.Node) & ModifierFlags.Export) != 0 ||
            (store.Kind(statement) == SyntaxKind.VariableStatement && IsGlobalSourceFile(At(declaration, store.Parent(statement)))));
    }

    // ---- narrowable types ----

    // checker.ts isConstraintPosition
    private bool IsConstraintPosition(Type type, NodeRef node)
    {
        var store = StoreOf(node);
        var parent = store.Parent(node.Node);
        // In an element access obj[x], we consider obj to be in a constraint position, except when obj is of
        // a generic type without a nullable constraint and x is a generic type. This is because when both obj
        // and x are of generic types T and K, we want the resulting type to be T[K].
        return store.Kind(parent) is SyntaxKind.PropertyAccessExpression or SyntaxKind.QualifiedName ||
            (store.Kind(parent) is SyntaxKind.CallExpression or SyntaxKind.NewExpression && store.Expression(parent) == node.Node) ||
            (store.Kind(parent) == SyntaxKind.ElementAccessExpression && store.Expression(parent) == node.Node &&
                !(SomeType(type, IsGenericTypeWithoutNullableConstraint) && IsGenericIndexType(GetTypeOfExpression(At(node, store.ArgumentExpression(parent))))));
    }

    // checker.ts isGenericTypeWithUnionConstraint
    private bool IsGenericTypeWithUnionConstraint(Type type)
    {
        if ((Types.Flags(type) & TypeFlags.Intersection) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(type).Types).ToArray())
            {
                if (IsGenericTypeWithUnionConstraint(t))
                {
                    return true;
                }
            }
            return false;
        }
        return (Types.Flags(type) & TypeFlags.Instantiable) != 0 && (Types.Flags(GetBaseConstraintOrType(type)) & (TypeFlags.Nullable | TypeFlags.Union)) != 0;
    }

    // checker.ts isGenericTypeWithoutNullableConstraint
    private bool IsGenericTypeWithoutNullableConstraint(Type type)
    {
        if ((Types.Flags(type) & TypeFlags.Intersection) != 0)
        {
            foreach (var t in Types.Elements(Types.Union(type).Types).ToArray())
            {
                if (IsGenericTypeWithoutNullableConstraint(t))
                {
                    return true;
                }
            }
            return false;
        }
        return (Types.Flags(type) & TypeFlags.Instantiable) != 0 && !MaybeTypeOfKind(GetBaseConstraintOrType(type), TypeFlags.Nullable);
    }

    // checker.ts hasContextualTypeWithNoGenericTypes
    private bool HasContextualTypeWithNoGenericTypes(NodeRef node, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var kind = store.Kind(node.Node);
        if (kind is not (SyntaxKind.Identifier or SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression))
        {
            return false;
        }
        // Computing the contextual type for a child of a JSX element involves resolving the type of the
        // element's tag name, so we exclude that here to avoid circularities.
        var parent = store.Parent(node.Node);
        if (store.Kind(parent) is SyntaxKind.JsxOpeningElement or SyntaxKind.JsxSelfClosingElement && store.TagName(parent) == node.Node)
        {
            return false;
        }
        var contextualType = GetContextualType(node, (checkMode & CheckMode.RestBindingElement) != 0 ? ContextFlags.SkipBindingPatterns : ContextFlags.None);
        return !contextualType.IsNone && !IsGenericType(contextualType);
    }

    /// <summary>checker.ts getNarrowableTypeForReference.</summary>
    internal Type GetNarrowableTypeForReference(Type type, NodeRef reference, CheckMode checkMode = CheckMode.Normal)
    {
        if (IsNoInferType(type))
        {
            type = Types.Data<SubstitutionData>(type).BaseType;
        }
        // When the type of a reference is or contains an instantiable type with a union type constraint, and
        // when the reference is in a constraint position (where it is known we'll obtain the apparent type) or
        // has a contextual type containing no top-level instantiables (meaning constraints will determine
        // assignability), we substitute constraints for all instantiables in the type of the reference to give
        // control flow analysis an opportunity to narrow it further.
        var substituteConstraints = (checkMode & CheckMode.Inferential) == 0 && !IsSyntheticReference(reference) &&
            SomeType(type, IsGenericTypeWithUnionConstraint) &&
            (IsConstraintPosition(type, reference) || HasContextualTypeWithNoGenericTypes(reference, checkMode));
        return substituteConstraints ? MapType(type, GetBaseConstraintOrType) : type;
    }
}
