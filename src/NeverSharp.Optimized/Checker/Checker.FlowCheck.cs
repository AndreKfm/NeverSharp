// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (the uses of control flow analysis: checkIdentifier, property access, predicates,
// exhaustive switches, implicit returns, unreachable code)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C7. Ported: checkIdentifier's flow part with the assignment
// checks, definite assignment (2454) and auto-typed variables (7005/7034), removeOptionalityFromDeclaredType,
// getFlowTypeOfAccessExpression, getTypePredicateOfSignature, instantiateTypePredicate, isExhaustiveSwitchStatement,
// functionHasImplicitReturn, checkAllCodePathsInNonVoidFunctionReturnOrThrow, checkSourceElementUnreachable, the unused
// label of checkLabeledStatement, isTypeDerivedFrom. The subtype, strict subtype and comparable relations narrowing
// asks for are stand-ins on top of the assignability stub (TODO(C4), marked below; C4's relater replaces them).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // checker.ts flowTypeCache (getTypeOfExpression's cache of types that needed flow analysis; null while the back
    // edges of a loop are analyzed)
    private Dictionary<NodeRef, Type>? flowTypeCache;

    // checker.ts withinUnreachableCode / reportedUnreachableNodes
    private bool withinUnreachableCode;
    private HashSet<NodeRef>? reportedUnreachableNodes;

    // Whether control flow analysis of a loop is in progress where checkExpression must not use cached types
    // (checkExpressionCached moves flowLoopStart to the top of the stack; checkExpression does not cache).
    private bool InFlowLoopAnalysis => flowLoopCount > flowLoopStart;

    // ---- type predicates ----

    private readonly Dictionary<Signature, TypePredicateInfo?> resolvedTypePredicates = [];

    /// <summary>
    /// checker.ts getTypePredicateOfSignature (TODO(C5): composite signatures of unions and intersections, JSDoc
    /// signatures and getTypePredicateFromBody's inferred predicates).
    /// </summary>
    private TypePredicateInfo? GetTypePredicateOfSignature(Signature signature)
    {
        if (resolvedTypePredicates.TryGetValue(signature, out var resolved))
        {
            return resolved;
        }
        // (no marker while it resolves: a predicate that needs itself, e.g. through the effects signature of a call in the
        // flow of its own type, resolves again and meets the circularity in the type it references, as in checker.ts)
        ref var record = ref Signatures.Record(signature);
        TypePredicateInfo? predicate = null;
        if (!record.Target.IsNone)
        {
            var targetTypePredicate = GetTypePredicateOfSignature(record.Target);
            var mapper = MapperAt(Signatures.Record(signature).Mapper);
            predicate = targetTypePredicate is null ? null : mapper is null ? targetTypePredicate : InstantiateTypePredicate(targetTypePredicate, mapper);
        }
        else if (Signatures.Record(signature).CompositeSignatures is { } compositeSignatures)
        {
            predicate = GetUnionOrIntersectionTypePredicate(compositeSignatures, Signatures.Record(signature).CompositeKind);
        }
        else
        {
            var declaration = Signatures.Record(signature).Declaration;
            var type = declaration.IsNone ? NodeRef.None : GetEffectiveReturnTypeNodeOf(declaration);
            TypePredicateInfo? jsdocPredicate = null;
            if (type.IsNone && !declaration.IsNone)
            {
                var jsdocSignature = GetSignatureOfTypeTag(declaration);
                if (!jsdocSignature.IsNone && signature != jsdocSignature)
                {
                    jsdocPredicate = GetTypePredicateOfSignature(jsdocSignature);
                }
            }
            if (!type.IsNone || jsdocPredicate is not null)
            {
                predicate = !type.IsNone && Kind(type) == SyntaxKind.TypePredicate ? CreateTypePredicateFromTypePredicateNode(type, signature) : jsdocPredicate;
            }
            else if (!declaration.IsNone && Utilities.IsFunctionLikeDeclaration(StoreOf(declaration), declaration.Node) &&
                (Signatures.Record(signature).ResolvedReturnType.IsNone || (Types.Flags(Signatures.Record(signature).ResolvedReturnType) & TypeFlags.Boolean) != 0) &&
                (Signatures.Record(signature).Parameters?.Length ?? 0) > 0)
            {
                predicate = GetTypePredicateFromBody(declaration);
            }
        }
        resolvedTypePredicates[signature] = predicate;
        return predicate;
    }

    // checker.ts getUnionOrIntersectionTypePredicate
    private TypePredicateInfo? GetUnionOrIntersectionTypePredicate(Signature[] signatures, TypeFlags kind)
    {
        TypePredicateInfo? last = null;
        var types = new List<Type>();
        foreach (var sig in signatures)
        {
            var pred = GetTypePredicateOfSignature(sig);
            if (pred is not null)
            {
                // Constituent type predicates must all have matching kinds. We don't create composite type predicates for assertions.
                if ((pred.Kind != TypePredicateKind.This && pred.Kind != TypePredicateKind.Identifier) ||
                    (last is not null && !(last.Kind == pred.Kind && last.ParameterIndex == pred.ParameterIndex)))
                {
                    return null;
                }
                last = pred;
                types.Add(pred.Type);
            }
            else
            {
                // In composite union signatures we permit and ignore signatures with a return type `false`.
                var returnType = kind != TypeFlags.Intersection ? GetReturnTypeOfSignature(sig) : Type.None;
                if (returnType != falseType && returnType != regularFalseType)
                {
                    return null;
                }
            }
        }
        if (last is null)
        {
            return null;
        }
        var compositeType = kind == TypeFlags.Intersection ? GetIntersectionType(types.ToArray()) : GetUnionType(types.ToArray());
        return new TypePredicateInfo { Kind = last.Kind, ParameterName = last.ParameterName, ParameterIndex = last.ParameterIndex, Type = compositeType };
    }

    // checker.ts getTypePredicateFromBody: a function with a single return of a boolean expression that refines one
    // of its parameters
    private TypePredicateInfo? GetTypePredicateFromBody(NodeRef func)
    {
        var store = StoreOf(func);
        switch (store.Kind(func.Node))
        {
            case SyntaxKind.Constructor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                return null;
        }
        if (GetFunctionFlagsOf(store, func.Node) != FunctionFlags.Normal)
        {
            return null;
        }
        // Only attempt to infer a type predicate if there's exactly one return.
        var body = store.Body(func.Node);
        var singleReturn = Node.None;
        if (!body.IsNone && store.Kind(body) != SyntaxKind.Block)
        {
            singleReturn = body; // arrow function
        }
        else
        {
            var bailedEarly = false;
            ForEachReturnStatementUntil(store, body, returnStatement =>
            {
                if (!singleReturn.IsNone || store.Expression(returnStatement).IsNone)
                {
                    bailedEarly = true;
                    return true;
                }
                singleReturn = store.Expression(returnStatement);
                return false;
            });
            if (bailedEarly || singleReturn.IsNone || FunctionHasImplicitReturn(func))
            {
                return null;
            }
        }
        return CheckIfExpressionRefinesAnyParameter(func, At(func, singleReturn));
    }

    // utilities.ts forEachReturnStatement (stopping when the visitor returns true)
    private static void ForEachReturnStatementUntil(NodeStore store, Node body, Func<Node, bool> visitor)
    {
        var stop = false;
        Traverse(body);

        void Traverse(Node node)
        {
            if (stop)
            {
                return;
            }
            switch (store.Kind(node))
            {
                case SyntaxKind.ReturnStatement:
                    stop = visitor(node);
                    return;
                case SyntaxKind.CaseBlock:
                case SyntaxKind.Block:
                case SyntaxKind.IfStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                case SyntaxKind.WithStatement:
                case SyntaxKind.SwitchStatement:
                case SyntaxKind.CaseClause:
                case SyntaxKind.DefaultClause:
                case SyntaxKind.LabeledStatement:
                case SyntaxKind.TryStatement:
                case SyntaxKind.CatchClause:
                    NodeVisitor.ForEachChild<object>(store, node, child =>
                    {
                        Traverse(child);
                        return stop ? child : null;
                    });
                    return;
            }
        }
    }

    // checker.ts checkIfExpressionRefinesAnyParameter
    private TypePredicateInfo? CheckIfExpressionRefinesAnyParameter(NodeRef func, NodeRef expr)
    {
        var store = StoreOf(func);
        expr = At(expr, Utilities.SkipParentheses(store, expr.Node, true));
        var returnType = CheckExpressionCached(expr);
        if ((Types.Flags(returnType) & TypeFlags.Boolean) == 0)
        {
            return null;
        }
        var parameters = store.Nodes(store.Parameters(func.Node)).ToArray();
        for (var i = 0; i < parameters.Length; i++)
        {
            var param = At(func, parameters[i]);
            var paramSymbol = NodeSymbol(param);
            var initType = paramSymbol.IsNone ? Type.None : GetTypeOfSymbol(paramSymbol);
            if (initType.IsNone || (Types.Flags(initType) & TypeFlags.Boolean) != 0 || store.Kind(store.Name(param.Node)) != SyntaxKind.Identifier ||
                IsSymbolAssigned(paramSymbol) || !store.DotDotDotToken(param.Node).IsNone)
            {
                // Refining "x: boolean" to "x is true" or "x is false" isn't useful.
                continue;
            }
            var trueType = CheckIfExpressionRefinesParameter(func, expr, param, initType);
            if (!trueType.IsNone)
            {
                return new TypePredicateInfo
                {
                    Kind = TypePredicateKind.Identifier,
                    ParameterName = Utilities.UnescapeLeadingUnderscores(store.Texts.GetString(store.EscapedText(store.Name(param.Node)))!),
                    ParameterIndex = i,
                    Type = trueType,
                };
            }
        }
        return null;
    }

    // checker.ts checkIfExpressionRefinesParameter
    private Type CheckIfExpressionRefinesParameter(NodeRef func, NodeRef expr, NodeRef param, Type initType)
    {
        var store = StoreOf(expr);
        var exprFlow = FlowNodeOf(expr);
        var parent = store.Parent(expr.Node);
        var antecedent = !exprFlow.IsNone ? new FlowRef(expr.File, exprFlow)
            : store.Kind(parent) == SyntaxKind.ReturnStatement && !FlowNodeOf(At(expr, parent)).IsNone ? new FlowRef(expr.File, FlowNodeOf(At(expr, parent)))
            : CreateSyntheticFlowNode(FlowFlags.Start, NodeRef.None, default);
        var trueCondition = CreateSyntheticFlowNode(FlowFlags.TrueCondition, expr, antecedent);
        var name = At(param, StoreOf(param).Name(param.Node));
        var trueType = GetFlowTypeOfReference(name, initType, initType, func, trueCondition);
        if (trueType == initType)
        {
            return Type.None;
        }
        // "x is T" means that x is T if and only if it returns true. If it returns false then x is not T.
        // This means that if the function is called with an argument of type trueType, there can't be anything left in the `else` branch. It must reduce to `never`.
        var falseCondition = CreateSyntheticFlowNode(FlowFlags.FalseCondition, expr, antecedent);
        var falseSubtype = GetReducedType(GetFlowTypeOfReference(name, initType, trueType, func, falseCondition));
        return (Types.Flags(falseSubtype) & TypeFlags.Never) != 0 ? trueType : Type.None;
    }

    // utilities.ts getEffectiveReturnTypeNode (JSDoc return types: C9)
    private NodeRef GetEffectiveReturnTypeNodeOf(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        return Utilities.IsFunctionLike(store, declaration.Node) || store.Kind(declaration.Node) is SyntaxKind.JSDocFunctionType or SyntaxKind.JSDocSignature
            ? At(declaration, GetEffectiveReturnTypeNodeOf(store, declaration.Node))
            : NodeRef.None;
    }

    // checker.ts createTypePredicateFromTypePredicateNode
    private TypePredicateInfo CreateTypePredicateFromTypePredicateNode(NodeRef node, Signature signature)
    {
        var store = StoreOf(node);
        var parameterName = store.ParameterName(node.Node);
        var typeNode = store.Type(node.Node);
        var type = typeNode.IsNone ? Type.None : GetTypeFromTypeNode(At(node, typeNode));
        var asserts = !store.AssertsModifier(node.Node).IsNone;
        if (store.Kind(parameterName) == SyntaxKind.ThisType)
        {
            return new TypePredicateInfo { Kind = asserts ? TypePredicateKind.AssertsThis : TypePredicateKind.This, ParameterName = null, ParameterIndex = -1, Type = type };
        }
        var name = store.Texts.GetString(store.EscapedText(parameterName))!;
        var parameters = Signatures.Record(signature).Parameters ?? [];
        return new TypePredicateInfo
        {
            Kind = asserts ? TypePredicateKind.AssertsIdentifier : TypePredicateKind.Identifier,
            ParameterName = Utilities.UnescapeLeadingUnderscores(name),
            ParameterIndex = Array.FindIndex(parameters, p => NameOf(p) == name),
            Type = type,
        };
    }

    // checker.ts instantiateTypePredicate
    private TypePredicateInfo InstantiateTypePredicate(TypePredicateInfo predicate, object mapper) =>
        new()
        {
            Kind = predicate.Kind,
            ParameterName = predicate.ParameterName,
            ParameterIndex = predicate.ParameterIndex,
            Type = predicate.Type.IsNone ? Type.None : InstantiateType(predicate.Type, (TypeMapper)mapper),
        };

    // getNodeLinks(parent).flags & NodeCheckFlags.InCheckIdentifier of binding patterns' parents
    private readonly HashSet<NodeRef> inCheckIdentifier = [];

    // checker.ts getNarrowedTypeOfSymbol
    internal Type GetNarrowedTypeOfSymbol(SymbolRef symbol, NodeRef location)
    {
        var type = GetTypeOfSymbol(symbol);
        var declaration = ValueDeclarationOf(symbol);
        if (declaration.IsNone)
        {
            return type;
        }
        var store = StoreOf(declaration);
        var d = declaration.Node;
        // If we have a non-rest binding element with no initializer declared as a const variable or a const-like
        // parameter (a parameter for which there are no assignments in the function body), and if the parent type
        // for the destructuring is a union type, one or more of the binding elements may represent discriminant
        // properties, and we want the effects of conditional checks on such discriminants to affect the types of
        // other binding elements from the same destructuring. We use the binding pattern as a pseudo-reference.
        if (store.Kind(d) == SyntaxKind.BindingElement && store.Initializer(d).IsNone && store.DotDotDotToken(d).IsNone && store.Length(store.Elements(store.Parent(d))) >= 2)
        {
            var parent = At(declaration, store.Parent(store.Parent(d)));
            var rootDeclaration = At(declaration, Utilities.GetRootDeclaration(store, parent.Node));
            if ((store.Kind(rootDeclaration.Node) == SyntaxKind.VariableDeclaration && (Utilities.GetCombinedNodeFlags(store, rootDeclaration.Node) & NodeFlags.Constant) != 0) ||
                store.Kind(rootDeclaration.Node) == SyntaxKind.Parameter)
            {
                if (inCheckIdentifier.Add(parent))
                {
                    var parentType = GetTypeForBindingElementParent(parent, CheckMode.Normal);
                    var parentTypeConstraint = parentType.IsNone ? Type.None : MapType(parentType, GetBaseConstraintOrType);
                    inCheckIdentifier.Remove(parent);
                    if (!parentTypeConstraint.IsNone && (Types.Flags(parentTypeConstraint) & TypeFlags.Union) != 0 &&
                        !(store.Kind(rootDeclaration.Node) == SyntaxKind.Parameter && IsSomeSymbolAssigned(rootDeclaration)))
                    {
                        var pattern = At(declaration, store.Parent(d));
                        var narrowedType = GetFlowTypeOfReference(pattern, parentTypeConstraint, parentTypeConstraint, NodeRef.None, new FlowRef(location.File, FlowNodeOf(location)));
                        if ((Types.Flags(narrowedType) & TypeFlags.Never) != 0)
                        {
                            return neverType;
                        }
                        // Destructurings are validated against the parent type elsewhere. Here we disable tuple bounds
                        // checks because the narrowed type may have lower arity than the full parent type.
                        return GetBindingElementTypeFromParentType(declaration, narrowedType, noTupleBoundsCheck: true);
                    }
                }
            }
        }
        // If we have a const-like parameter with no type annotation or initializer, and if the parameter is contextually
        // typed by a signature with a single rest parameter of a union of tuple types, one or more of the parameters may
        // represent discriminant tuple elements. We use the function as a pseudo-reference.
        if (store.Kind(d) == SyntaxKind.Parameter && store.Type(d).IsNone && store.Initializer(d).IsNone && store.DotDotDotToken(d).IsNone)
        {
            var func = At(declaration, store.Parent(d));
            var parameters = store.Parameters(func.Node);
            if (store.Length(parameters) >= 2 && IsContextSensitiveFunctionOrObjectLiteralMethod(store, func.Node))
            {
                var contextualSignature = GetContextualSignature(func);
                if (!contextualSignature.IsNone && Signatures.Record(contextualSignature).Parameters.Length == 1 && SignatureHasRestParameter(contextualSignature))
                {
                    var restType = GetReducedApparentType(InstantiateType(GetTypeOfSymbol(Signatures.Record(contextualSignature).Parameters[0]), GetInferenceContext(func)?.NonFixingMapper));
                    var anyAssigned = false;
                    var position = 0;
                    var index = -1;
                    foreach (var p in store.Nodes(parameters))
                    {
                        anyAssigned |= IsSomeSymbolAssigned(At(func, p));
                        if (p == d)
                        {
                            index = position;
                        }
                        position++;
                    }
                    if ((Types.Flags(restType) & TypeFlags.Union) != 0 && EveryType(restType, IsTupleType) && !anyAssigned)
                    {
                        var narrowedType = GetFlowTypeOfReference(func, restType, restType, NodeRef.None, new FlowRef(location.File, FlowNodeOf(location)));
                        index -= GetThisParameter(store, func.Node).IsNone ? 0 : 1;
                        return GetIndexedAccessType(narrowedType, GetNumberLiteralType(index));
                    }
                }
            }
        }
        return type;
    }

    // ---- checkIdentifier ----

    // checker.ts checkIdentifier from getNarrowableTypeForReference on: the declared and initial types and the flow type
    // of a variable, parameter or alias reference, definite assignment (2454), auto-typed variables (7005/7034)
    internal Type CheckIdentifierFlowType(NodeRef node, SymbolRef symbol, SymbolRef localOrExportSymbol, NodeRef declaration, Type type, AssignmentKind assignmentKind, bool isAlias, CheckMode checkMode)
    {
        // A cycle through approximate expression checks would overflow the stack and end the process; it fails the check
        // of this file instead (TypeScript's checks have no such cycle).
        if (++identifierFlowNesting > 1000)
        {
            identifierFlowNesting = 0;
            throw new InvalidOperationException("checkIdentifier nested too deeply (a cycle through approximate expression checks)");
        }
        try
        {
            return CheckIdentifierFlowTypeWorker(node, symbol, localOrExportSymbol, declaration, type, assignmentKind, isAlias, checkMode);
        }
        finally
        {
            identifierFlowNesting--;
        }
    }

    private int identifierFlowNesting;

    private Type CheckIdentifierFlowTypeWorker(NodeRef node, SymbolRef symbol, SymbolRef localOrExportSymbol, NodeRef declaration, Type type, AssignmentKind assignmentKind, bool isAlias, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var immediateDeclaration = ValueDeclarationOf(localOrExportSymbol);
        type = GetNarrowableTypeForReference(type, node, checkMode);
        // The declaration container is the innermost function that encloses the declaration of the variable
        // or parameter. The flow container is the innermost function starting with which we analyze the control
        // flow graph to determine the control flow based type.
        var declarationStore = StoreOf(declaration);
        var isParameter = declarationStore.Kind(Utilities.GetRootDeclaration(declarationStore, declaration.Node)) == SyntaxKind.Parameter;
        var declarationContainer = GetControlFlowContainer(declaration);
        var flowContainer = GetControlFlowContainer(node);
        var isOuterVariable = flowContainer != declarationContainer;
        var parent = store.Parent(n);
        var isSpreadDestructuringAssignmentTarget = !parent.IsNone && !store.Parent(parent).IsNone && store.Kind(parent) == SyntaxKind.SpreadAssignment &&
            IsDestructuringAssignmentTarget(store, store.Parent(parent));
        var isModuleExports = (SymbolFlagsOf(symbol) & SymbolFlags.ModuleExports) != 0;
        var typeIsAutomatic = type == autoType || type == autoArrayType;
        var isAutomaticTypeInNonNull = typeIsAutomatic && store.Kind(parent) == SyntaxKind.NonNullExpression;
        // When the control flow originates in a function expression, arrow function, method, or accessor, and
        // we are referencing a closed-over const variable or parameter or mutable local variable past its last
        // assignment, we extend the origin of the control flow analysis to include the immediately enclosing
        // control flow container.
        while (flowContainer != declarationContainer &&
            (Kind(flowContainer) is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction || Utilities.IsObjectLiteralOrClassExpressionMethodOrAccessor(StoreOf(flowContainer), flowContainer.Node)) &&
            ((IsConstantVariable(localOrExportSymbol) && type != autoArrayType) || (IsParameterOrMutableLocalVariable(localOrExportSymbol) && IsPastLastAssignment(localOrExportSymbol, node))))
        {
            flowContainer = GetControlFlowContainer(flowContainer);
        }
        // We only look for uninitialized variables in strict null checking mode, and only when we can analyze
        // the entire control flow graph from the variable's declaration (i.e. when the flow container and
        // declaration container are the same).
        var isNeverInitialized = false;
        if (!immediateDeclaration.IsNone && Kind(immediateDeclaration) == SyntaxKind.VariableDeclaration)
        {
            var ids = StoreOf(immediateDeclaration);
            var statement = ids.Parent(ids.Parent(immediateDeclaration.Node));
            isNeverInitialized = ids.Kind(statement) is not (SyntaxKind.ForInStatement or SyntaxKind.ForOfStatement) &&
                ids.Initializer(immediateDeclaration.Node).IsNone && ids.ExclamationToken(immediateDeclaration.Node).IsNone &&
                IsMutableLocalVariableDeclaration(immediateDeclaration) && !IsSymbolAssignedDefinitely(symbol);
        }
        var assumeInitialized = isParameter || isAlias ||
            (isOuterVariable && !isNeverInitialized) ||
            isSpreadDestructuringAssignmentTarget || isModuleExports || IsSameScopedBindingElement(node, declaration) ||
            (type != autoType && type != autoArrayType && (!strictNullChecks || (Types.Flags(type) & (TypeFlags.AnyOrUnknown | TypeFlags.Void)) != 0 ||
                Utilities.IsInTypeQuery(store, n) || IsInAmbientOrTypeNode(store, n) || store.Kind(parent) == SyntaxKind.ExportSpecifier)) ||
            store.Kind(parent) == SyntaxKind.NonNullExpression ||
            (declarationStore.Kind(declaration.Node) == SyntaxKind.VariableDeclaration && !declarationStore.ExclamationToken(declaration.Node).IsNone) ||
            (declarationStore.Flags(declaration.Node) & NodeFlags.Ambient) != 0;
        var initialType = isAutomaticTypeInNonNull ? undefinedType
            : assumeInitialized ? (isParameter ? RemoveOptionalityFromDeclaredType(type, declaration) : type)
            : typeIsAutomatic ? undefinedType : GetOptionalType(type);
        var flowType = isAutomaticTypeInNonNull
            ? GetNonNullableType(GetFlowTypeOfReference(node, type, initialType, flowContainer))
            : GetFlowTypeOfReference(node, type, initialType, flowContainer);
        // A variable is considered uninitialized when it is possible to analyze the entire control flow graph
        // from declaration to use, and when the variable's declared type doesn't include undefined but the
        // control flow based type does include undefined.
        if (!IsEvolvingArrayOperationTarget(node) && (type == autoType || type == autoArrayType))
        {
            if (flowType == autoType || flowType == autoArrayType)
            {
                if (noImplicitAny)
                {
                    Error(At(declaration, Utilities.GetNameOfDeclaration(declarationStore, declaration.Node)), Diagnostics.Variable_0_implicitly_has_type_1_in_some_locations_where_its_type_cannot_be_determined, SymbolToString(symbol), TypeToString(flowType));
                    Error(node, Diagnostics.Variable_0_implicitly_has_an_1_type, SymbolToString(symbol), TypeToString(flowType));
                }
                return ConvertAutoToAny(flowType);
            }
        }
        else if (!assumeInitialized && !ContainsUndefinedType(type) && ContainsUndefinedType(flowType))
        {
            Error(node, Diagnostics.Variable_0_is_used_before_being_assigned, SymbolToString(symbol));
            // Return the declared type to reduce follow-on errors
            return type;
        }
        return assignmentKind != AssignmentKind.None ? GetBaseTypeOfLiteralType(flowType) : flowType;
    }

    // checker.ts isSameScopedBindingElement
    private bool IsSameScopedBindingElement(NodeRef node, NodeRef declaration)
    {
        if (Kind(declaration) != SyntaxKind.BindingElement)
        {
            return false;
        }
        var store = StoreOf(node);
        var bindingElement = node.Node;
        while (!bindingElement.IsNone && store.Kind(bindingElement) != SyntaxKind.BindingElement)
        {
            bindingElement = store.Parent(bindingElement);
        }
        return !bindingElement.IsNone && node.File == declaration.File &&
            Utilities.GetRootDeclaration(store, bindingElement) == Utilities.GetRootDeclaration(StoreOf(declaration), declaration.Node);
    }

    // NodeLinks.parameterInitializerContainsUndefined
    private readonly Dictionary<NodeRef, bool> parameterInitializerContainsUndefined = [];

    private const int ParameterInitializerContainsUndefinedProperty = 8;

    // checker.ts parameterInitializerContainsUndefined (checkDeclarationInitializer: the cached initializer type)
    private bool ParameterInitializerContainsUndefined(NodeRef declaration)
    {
        if (!parameterInitializerContainsUndefined.TryGetValue(declaration, out var containsUndefined))
        {
            if (!PushTypeResolution(declaration, ParameterInitializerContainsUndefinedProperty))
            {
                ReportCircularityError(GetSymbolOfDeclaration(declaration));
                return true;
            }
            containsUndefined = HasTypeFacts(CheckDeclarationInitializer(declaration, CheckMode.Normal), TypeFacts.IsUndefined);
            if (!PopTypeResolution())
            {
                ReportCircularityError(GetSymbolOfDeclaration(declaration));
                return true;
            }
            parameterInitializerContainsUndefined.TryAdd(declaration, containsUndefined);
            containsUndefined = parameterInitializerContainsUndefined[declaration];
        }
        return containsUndefined;
    }

    // checker.ts removeOptionalityFromDeclaredType: undefined removed from the annotated type of a parameter with an
    // initializer that doesn't include undefined
    private Type RemoveOptionalityFromDeclaredType(Type declaredType, NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var removeUndefined = strictNullChecks &&
            store.Kind(declaration.Node) == SyntaxKind.Parameter &&
            !store.Initializer(declaration.Node).IsNone &&
            HasTypeFacts(declaredType, TypeFacts.IsUndefined) &&
            !ParameterInitializerContainsUndefined(declaration);
        return removeUndefined ? GetTypeWithFacts(declaredType, TypeFacts.NEUndefined) : declaredType;
    }

    // ---- quick types ----

    // ---- logical and nullish operators ----

    // ---- non-null checks ----

    // ---- properties typed by the constructor ----

    // checker.ts getTypeForVariableLikeDeclaration: a property declaration without a type annotation or initializer
    // (noImplicitAny) takes its type from the this.x assignments of the constructor or of the static blocks
    private Type GetTypeOfPropertyFromConstructorFlow(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var symbol = GetSymbolOfDeclaration(declaration);
        var classNode = store.Parent(declaration.Node);
        var ambient = (Utilities.GetCombinedModifierFlags(store, declaration.Node) & ModifierFlags.Ambient) != 0;
        if (!Utilities.IsStatic(store, declaration.Node))
        {
            var constructor = Node.None;
            foreach (var member in store.Nodes(store.Members(classNode)))
            {
                if (store.Kind(member) == SyntaxKind.Constructor && !store.Body(member).IsNone)
                {
                    constructor = member;
                    break;
                }
            }
            return !constructor.IsNone ? GetFlowTypeInConstructorOrStaticBlock(symbol, At(declaration, constructor), forStaticBlock: false)
                : ambient ? GetTypeOfPropertyInBaseClass(symbol) : Type.None;
        }
        var hasStaticBlocks = false;
        foreach (var member in store.Nodes(store.Members(classNode)))
        {
            if (store.Kind(member) != SyntaxKind.ClassStaticBlockDeclaration)
            {
                continue;
            }
            hasStaticBlocks = true;
            // getFlowTypeInStaticBlocks: the first block whose assignments are not only null or undefined
            var type = GetFlowTypeInConstructorOrStaticBlock(symbol, At(declaration, member), forStaticBlock: true);
            if (!type.IsNone)
            {
                return type;
            }
        }
        return hasStaticBlocks ? Type.None : ambient ? GetTypeOfPropertyInBaseClass(symbol) : Type.None;
    }

    // checker.ts getFlowTypeInConstructor / one block of getFlowTypeInStaticBlocks: the flow type of the synthetic
    // `this.<name>` at the return of the constructor or block (None: only null or undefined assigned)
    private Type GetFlowTypeInConstructorOrStaticBlock(SymbolRef symbol, NodeRef container, bool forStaticBlock)
    {
        var escapedName = NameOf(symbol);
        // a private name's symbol is named "__#<id>@#name"; the reference's name is "#name"
        var accessName = escapedName.StartsWith("__#", StringComparison.Ordinal) ? escapedName[(escapedName.IndexOf('@', StringComparison.Ordinal) + 1)..] : escapedName;
        var returnFlowNode = BindingOf(container.File).ReturnFlowNodeOf(container.Node);
        Type flowType;
        if (returnFlowNode.IsNone)
        {
            flowType = autoType; // (reference.flowNode undefined: the declared type)
        }
        else
        {
            var saveName = syntheticThisAccessName;
            syntheticThisAccessName = accessName;
            try
            {
                flowType = GetFlowTypeOfProperty(syntheticThisAccess, symbol, new FlowRef(container.File, returnFlowNode));
            }
            finally
            {
                syntheticThisAccessName = saveName;
            }
        }
        if (noImplicitAny && (flowType == autoType || flowType == autoArrayType))
        {
            Error(ValueDeclarationOf(symbol), Diagnostics.Member_0_implicitly_has_an_1_type, SymbolToString(symbol), TypeToString(flowType));
        }
        // We don't infer a type if assignments are only null or undefined.
        if (EveryType(flowType, IsNullableType))
        {
            return Type.None;
        }
        return forStaticBlock ? ConvertAutoToAny(flowType) : flowType;
    }

    // checker.ts getFlowTypeOfProperty
    private Type GetFlowTypeOfProperty(NodeRef reference, SymbolRef prop, FlowRef flowNode = default)
    {
        var initialType = Type.None;
        var valueDeclaration = prop.IsNone ? NodeRef.None : ValueDeclarationOf(prop);
        if (!valueDeclaration.IsNone && (!IsAutoTypedProperty(prop) || (Utilities.GetCombinedModifierFlags(StoreOf(valueDeclaration), valueDeclaration.Node) & ModifierFlags.Ambient) != 0))
        {
            initialType = GetTypeOfPropertyInBaseClass(prop);
        }
        return GetFlowTypeOfReference(reference, autoType, initialType.IsNone ? undefinedType : initialType, NodeRef.None, flowNode);
    }

    // ---- property access ----

    // checker.ts isPropertyWithoutInitializer
    private bool IsPropertyWithoutInitializer(NodeRef node)
    {
        var store = StoreOf(node);
        return store.Kind(node.Node) == SyntaxKind.PropertyDeclaration && !Utilities.HasSyntacticModifierUncached(store, node.Node, ModifierFlags.Abstract) &&
            store.ExclamationToken(node.Node).IsNone && store.Initializer(node.Node).IsNone;
    }

    // ---- switch exhaustiveness ----

    // NodeLinks.isExhaustive: 0 in process, 1 false, 2 true
    private readonly Dictionary<NodeRef, int> exhaustiveSwitches = [];

    /// <summary>checker.ts isExhaustiveSwitchStatement.</summary>
    private bool IsExhaustiveSwitchStatement(NodeRef node)
    {
        if (!exhaustiveSwitches.TryGetValue(node, out var state))
        {
            exhaustiveSwitches[node] = 0; // Indicate resolution is in process
            var exhaustive = ComputeExhaustiveSwitchStatement(node);
            if (exhaustiveSwitches[node] == 0)
            {
                exhaustiveSwitches[node] = exhaustive ? 2 : 1;
            }
        }
        else if (state == 0)
        {
            exhaustiveSwitches[node] = 1; // Resolve circularity to false
        }
        return exhaustiveSwitches[node] == 2;
    }

    private bool ComputeExhaustiveSwitchStatement(NodeRef node)
    {
        var store = StoreOf(node);
        var expression = store.Expression(node.Node);
        if (store.Kind(expression) == SyntaxKind.TypeOfExpression)
        {
            var witnesses = GetSwitchClauseTypeOfWitnesses(node);
            if (witnesses is null)
            {
                return false;
            }
            var operandConstraint = GetBaseConstraintOrType(CheckExpressionCached(At(node, store.Expression(expression))));
            // Get the not-equal flags for all handled cases.
            var notEqualFacts = GetNotEqualFactsFromTypeofSwitch(0, 0, witnesses);
            if ((Types.Flags(operandConstraint) & TypeFlags.AnyOrUnknown) != 0)
            {
                // We special case the top types to be exhaustive when all cases are handled.
                return (TypeFacts.AllTypeofNE & notEqualFacts) == TypeFacts.AllTypeofNE;
            }
            // A missing not-equal flag indicates that the type wasn't handled by some case.
            return !SomeType(operandConstraint, t => GetTypeFacts(t, notEqualFacts) == notEqualFacts);
        }
        var type = GetBaseConstraintOrType(CheckExpressionCached(At(node, expression)));
        if (!IsLiteralType(type))
        {
            return false;
        }
        var switchTypes = GetSwitchClauseTypes(node);
        if (switchTypes.Length == 0 || Array.Exists(switchTypes, t => (Types.Flags(t) & (TypeFlags.Unit | TypeFlags.Never)) == 0))
        {
            return false;
        }
        return EachTypeContainedIn(MapType(type, GetRegularTypeOfLiteralType), switchTypes);
    }

    // ---- implicit returns ----

    /// <summary>checker.ts functionHasImplicitReturn.</summary>
    private bool FunctionHasImplicitReturn(NodeRef func)
    {
        var endFlowNode = BindingOf(func.File).EndFlowNodeOf(func.Node);
        return !endFlowNode.IsNone && IsReachableFlowNode(new FlowRef(func.File, endFlowNode));
    }

    /// <summary>
    /// checker.ts checkAllCodePathsInNonVoidFunctionReturnOrThrow: an explicitly typed function whose return type isn't
    /// void, any, or a union containing void or any must have a return or throw on every path.
    /// </summary>
    private void CheckAllCodePathsInNonVoidFunctionReturnOrThrow(NodeRef func, Type returnType)
    {
        AddLazyDiagnostic(() =>
        {
            var store = StoreOf(func);
            var functionFlags = GetFunctionFlagsOf(store, func.Node);
            var type = returnType.IsNone ? Type.None : UnwrapReturnType(returnType, functionFlags);
            // Functions with an explicitly specified return type that includes `void` or is exactly `any` or `undefined` don't
            // need any return statements.
            if (!type.IsNone && (MaybeTypeOfKind(type, TypeFlags.Void) || (Types.Flags(type) & (TypeFlags.Any | TypeFlags.Undefined)) != 0))
            {
                return;
            }
            // If all we have is a function signature, or an arrow function with an expression body, then there is nothing to check.
            // also if HasImplicitReturn flag is not set this means that all codepaths in function body end with return or throw
            var body = store.Body(func.Node);
            if (store.Kind(func.Node) == SyntaxKind.MethodSignature || body.IsNone || Utilities.NodeIsMissing(store, body) || store.Kind(body) != SyntaxKind.Block || !FunctionHasImplicitReturn(func))
            {
                return;
            }
            var hasExplicitReturn = (store.Flags(func.Node) & NodeFlags.HasExplicitReturn) != 0;
            var returnTypeNode = GetEffectiveReturnTypeNodeOf(func);
            var errorNode = returnTypeNode.IsNone ? func : returnTypeNode;
            if (!type.IsNone && (Types.Flags(type) & TypeFlags.Never) != 0)
            {
                Error(errorNode, Diagnostics.A_function_returning_never_cannot_have_a_reachable_end_point);
            }
            else if (!type.IsNone && !hasExplicitReturn)
            {
                // minimal check: function has syntactic return type annotation and no explicit return statements in the body
                // this function does not conform to the specification.
                Error(errorNode, Diagnostics.A_function_whose_declared_type_is_neither_undefined_void_nor_any_must_return_a_value);
            }
            else if (!type.IsNone && strictNullChecks && !IsTypeAssignableTo(undefinedType, type))
            {
                Error(errorNode, Diagnostics.Function_lacks_ending_return_statement_and_return_type_does_not_include_undefined);
            }
            else if (compilerOptions.NoImplicitReturns == true)
            {
                if (type.IsNone)
                {
                    // If return type annotation is omitted check if function has any explicit return statements.
                    // If it does not have any - its inferred return type is void - don't do any checks.
                    // Otherwise get inferred return type from function body and report error only if it is not void / anytype
                    if (!hasExplicitReturn)
                    {
                        return;
                    }
                    var inferredReturnType = GetReturnTypeOfSignature(GetSignatureFromDeclaration(func));
                    if (IsUnwrappedReturnTypeUndefinedVoidOrAny(func, inferredReturnType))
                    {
                        return;
                    }
                }
                Error(errorNode, Diagnostics.Not_all_code_paths_return_a_value);
            }
        });
    }

    // checker.ts isUnwrappedReturnTypeUndefinedVoidOrAny
    private bool IsUnwrappedReturnTypeUndefinedVoidOrAny(NodeRef func, Type returnType)
    {
        var type = UnwrapReturnType(returnType, GetFunctionFlagsOf(StoreOf(func), func.Node));
        return !type.IsNone && ((MaybeTypeOfKind(type, TypeFlags.Void) || (Types.Flags(type) & (TypeFlags.AnyOrUnknown | TypeFlags.Undefined)) != 0));
    }

    // ---- unreachable code ----

    // checker.ts checkSourceElement's unreachable-code part (checkSourceElementWorker): whether the node starts a
    // region of unreachable code (reported once per region)
    private bool CheckSourceElementUnreachable(NodeRef node)
    {
        var store = StoreOf(node);
        if (!Utilities.IsPotentiallyExecutableNode(store, node.Node))
        {
            return false;
        }
        if (reportedUnreachableNodes is not null && reportedUnreachableNodes.Contains(node))
        {
            return true;
        }
        if (!IsSourceElementUnreachable(node))
        {
            return false;
        }
        (reportedUnreachableNodes ??= []).Add(node);
        var startNode = node.Node;
        var endNode = node.Node;
        var parent = store.Parent(node.Node);
        if (CanHaveStatements(store, parent))
        {
            var statements = store.Nodes(store.Statements(parent)).ToArray();
            var offset = Array.IndexOf(statements, node.Node);
            if (offset >= 0)
            {
                // Scan backwards to find the first unreachable unreported node;
                // this may happen when producing region diagnostics where not all nodes
                // will have been visited.
                var first = offset;
                for (var i = offset - 1; i >= 0; i--)
                {
                    var prevNode = At(node, statements[i]);
                    if (!Utilities.IsPotentiallyExecutableNode(store, prevNode.Node) || reportedUnreachableNodes.Contains(prevNode) || !IsSourceElementUnreachable(prevNode))
                    {
                        break;
                    }
                    first = i;
                    reportedUnreachableNodes.Add(prevNode);
                }
                var last = offset;
                for (var i = offset + 1; i < statements.Length; i++)
                {
                    var nextNode = At(node, statements[i]);
                    if (!Utilities.IsPotentiallyExecutableNode(store, nextNode.Node) || !IsSourceElementUnreachable(nextNode))
                    {
                        break;
                    }
                    last = i;
                    reportedUnreachableNodes.Add(nextNode);
                }
                startNode = statements[first];
                endNode = statements[last];
            }
        }
        var file = FileAt(node.File);
        // getTokenPosOfNode (a missing node starts at its pos)
        var start = Utilities.NodeIsMissing(store, startNode) ? store.Pos(startNode) : Scanner.SkipTrivia(store.SourceFileText(file.Root).Chars, store.Pos(startNode));
        var d = ProgramDiagnostic.FromFile(file, Utilities.CreateFileDiagnostic(store, file.Root, start, store.End(endNode) - start, Diagnostics.Unreachable_code_detected));
        // addErrorOrSuggestion(compilerOptions.allowUnreachableCode === false, ...)
        if (compilerOptions.AllowUnreachableCode == false)
        {
            diagnostics.Add(d);
        }
        else
        {
            d.Category = DiagnosticCategory.Suggestion;
            suggestionDiagnostics.Add(d);
        }
        return true;
    }

    // utilities.ts canHaveStatements
    private static bool CanHaveStatements(NodeStore store, Node node) =>
        store.Kind(node) is SyntaxKind.Block or SyntaxKind.ModuleBlock or SyntaxKind.SourceFile or SyntaxKind.CaseClause or SyntaxKind.DefaultClause;

    // checker.ts isSourceElementUnreachable (precondition: isPotentiallyExecutableNode)
    private bool IsSourceElementUnreachable(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if ((store.Flags(n) & NodeFlags.Unreachable) != 0)
        {
            // The binder has determined that this code is unreachable.
            // Ignore const enums unless preserveConstEnums is set.
            switch (store.Kind(n))
            {
                case SyntaxKind.EnumDeclaration:
                    return !Utilities.IsEnumConst(store, n) || compilerOptions.ShouldPreserveConstEnums();
                case SyntaxKind.ModuleDeclaration:
                {
                    var moduleState = Utilities.GetModuleInstanceState(store, n);
                    return moduleState == ModuleInstanceState.Instantiated || (compilerOptions.ShouldPreserveConstEnums() && moduleState == ModuleInstanceState.ConstEnumOnly);
                }
                default:
                    return true;
            }
        }
        var flowNode = FlowNodeOf(node);
        if (!flowNode.IsNone)
        {
            // For code the binder doesn't know is unreachable, use control flow / types.
            return !IsReachableFlowNode(new FlowRef(node.File, flowNode));
        }
        return false;
    }

    // checker.ts checkLabeledStatement: the label of a labeled statement that no break or continue references
    private void CheckUnusedLabel(NodeRef node)
    {
        var store = StoreOf(node);
        var label = store.Label(node.Node);
        if ((store.Flags(label) & NodeFlags.Unreachable) != 0 && compilerOptions.AllowUnusedLabels != true)
        {
            ErrorOrSuggestion(compilerOptions.AllowUnusedLabels == false, At(node, label), Diagnostics.Unused_label);
        }
    }
}
