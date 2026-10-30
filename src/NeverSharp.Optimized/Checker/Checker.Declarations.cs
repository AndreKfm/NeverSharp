// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (type parameters, signatures, functions and overloads, constructors, accessors,
// type nodes, decorators, deprecations, merged exports and name collisions; each function names its counterpart)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C8. checker.ts's lazy diagnostics run where they are added (see
// AddLazyDiagnostic). JSDoc signatures and @type tags of JavaScript functions are module C9's.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private readonly HashSet<NodeRef> checkedAccessorPairs = [];
    // checker.ts varianceTypeParameter: the type parameter whose variance annotation is being checked (marker type names)
    private Type varianceTypeParameter;
    private Type deferredGlobalDisposableType;
    private Type deferredGlobalAsyncDisposableType;

    // checker.ts getGlobalDisposableType / getGlobalAsyncDisposableType
    private Type GetGlobalDisposableType()
    {
        if (deferredGlobalDisposableType.IsNone)
        {
            var type = GetGlobalType("Disposable", 0, reportErrors: true);
            deferredGlobalDisposableType = type.IsNone ? emptyObjectType : type;
        }
        return deferredGlobalDisposableType;
    }

    private Type GetGlobalAsyncDisposableType()
    {
        if (deferredGlobalAsyncDisposableType.IsNone)
        {
            var type = GetGlobalType("AsyncDisposable", 0, reportErrors: true);
            deferredGlobalAsyncDisposableType = type.IsNone ? emptyObjectType : type;
        }
        return deferredGlobalAsyncDisposableType;
    }

    // ---- deprecations ----

    // checker.ts addDeprecatedSuggestionWorker
    private void AddDeprecatedSuggestionWorker(List<NodeRef> declarations, ProgramDiagnostic diagnostic)
    {
        foreach (var declaration in declarations)
        {
            var deprecatedTag = UtilitiesPublic.GetJSDocDeprecatedTag(StoreOf(declaration), declaration.Node);
            if (!deprecatedTag.IsNone)
            {
                AddRelatedInfo(diagnostic, CreateRelatedForNode(At(declaration, deprecatedTag), Diagnostics.The_declaration_was_marked_as_deprecated_here));
                break;
            }
        }
        // We call `addRelatedInfo()` before adding the diagnostic to prevent duplicates.
        diagnostic.Category = DiagnosticCategory.Suggestion;
        suggestionDiagnostics.Add(diagnostic);
    }

    // checker.ts isDeprecatedSymbol
    private bool IsDeprecatedSymbol(SymbolRef symbol)
    {
        var declarations = DeclarationsOf(symbol);
        var parentSymbol = GetParentOfSymbol(symbol);
        if (!parentSymbol.IsNone && declarations is { Count: > 1 })
        {
            return (SymbolFlagsOf(parentSymbol) & SymbolFlags.Interface) != 0 ? declarations.Exists(IsDeprecatedDeclaration) : declarations.TrueForAll(IsDeprecatedDeclaration);
        }
        var valueDeclaration = ValueDeclarationOf(symbol);
        return (!valueDeclaration.IsNone && IsDeprecatedDeclaration(valueDeclaration)) ||
            (declarations is { Count: > 0 } && declarations.TrueForAll(IsDeprecatedDeclaration));
    }

    // checker.ts isDeprecatedDeclaration
    private bool IsDeprecatedDeclaration(NodeRef declaration) => (Utilities.GetCombinedNodeFlags(StoreOf(declaration), declaration.Node) & NodeFlags.Deprecated) != 0;

    // checker.ts addDeprecatedSuggestion
    private void AddDeprecatedSuggestion(NodeRef location, List<NodeRef> declarations, string deprecatedEntity) =>
        AddDeprecatedSuggestionWorker(declarations, CreateDiagnosticForNode(location, Diagnostics._0_is_deprecated, deprecatedEntity));

    // checker.ts addDeprecatedSuggestionWithSignature
    private void AddDeprecatedSuggestionWithSignature(NodeRef location, NodeRef declaration, string? deprecatedEntity, string signatureString)
    {
        var diagnostic = deprecatedEntity is not null
            ? CreateDiagnosticForNode(location, Diagnostics.The_signature_0_of_1_is_deprecated, signatureString, deprecatedEntity)
            : CreateDiagnosticForNode(location, Diagnostics._0_is_deprecated, signatureString);
        AddDeprecatedSuggestionWorker([declaration], diagnostic);
    }

    // checker.ts resolveAliasWithDeprecationCheck
    private SymbolRef ResolveAliasWithDeprecationCheck(SymbolRef symbol, NodeRef location)
    {
        if ((SymbolFlagsOf(symbol) & SymbolFlags.Alias) == 0 || IsDeprecatedSymbol(symbol) || GetDeclarationOfAliasSymbol(symbol).IsNone)
        {
            return symbol;
        }
        var targetSymbol = ResolveAlias(symbol);
        if (targetSymbol == unknownSymbol)
        {
            return targetSymbol;
        }
        while ((SymbolFlagsOf(symbol) & SymbolFlags.Alias) != 0)
        {
            var target = GetImmediateAliasedSymbol(symbol);
            if (target.IsNone)
            {
                break;
            }
            if (target == targetSymbol)
            {
                break;
            }
            if (DeclarationsOf(target) is { Count: > 0 } targetDeclarations)
            {
                if (IsDeprecatedSymbol(target))
                {
                    AddDeprecatedSuggestion(location, targetDeclarations, NameOf(target));
                    break;
                }
                if (symbol == targetSymbol)
                {
                    break;
                }
                symbol = target;
            }
        }
        return targetSymbol;
    }

    // checker.ts isUncalledFunctionReference
    private bool IsUncalledFunctionReference(NodeRef node, SymbolRef symbol)
    {
        if ((SymbolFlagsOf(symbol) & (SymbolFlags.Function | SymbolFlags.Method)) == 0)
        {
            return true;
        }
        var store = StoreOf(node);
        var parent = UtilitiesPublic.FindAncestor(store, store.Parent(node.Node), n => store.Kind(n) is not (SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression));
        if (parent.IsNone)
        {
            parent = store.Parent(node.Node);
        }
        // isCallLikeExpression
        var parentKind = store.Kind(parent);
        if (parentKind is SyntaxKind.CallExpression or SyntaxKind.NewExpression or SyntaxKind.TaggedTemplateExpression or SyntaxKind.Decorator
                or SyntaxKind.JsxOpeningElement or SyntaxKind.JsxSelfClosingElement ||
            (parentKind == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(parent)) == SyntaxKind.InstanceOfKeyword))
        {
            return parentKind is SyntaxKind.CallExpression or SyntaxKind.NewExpression && store.Kind(node.Node) == SyntaxKind.Identifier && HasMatchingArgument(At(node, parent), node);
        }
        return (DeclarationsOf(symbol) ?? []).TrueForAll(d => !Utilities.IsFunctionLike(StoreOf(d), d.Node) || IsDeprecatedDeclaration(d));
    }

    // checker.ts getDeprecatedSuggestionNode
    private NodeRef GetDeprecatedSuggestionNode(NodeRef node)
    {
        var store = StoreOf(node);
        var n = Utilities.SkipParentheses(store, node.Node);
        switch (store.Kind(n))
        {
            case SyntaxKind.CallExpression:
            case SyntaxKind.Decorator:
            case SyntaxKind.NewExpression:
                return GetDeprecatedSuggestionNode(At(node, store.Expression(n)));
            case SyntaxKind.TaggedTemplateExpression:
                return GetDeprecatedSuggestionNode(At(node, store.Tag(n)));
            case SyntaxKind.JsxOpeningElement:
            case SyntaxKind.JsxSelfClosingElement:
                return GetDeprecatedSuggestionNode(At(node, store.TagName(n)));
            case SyntaxKind.ElementAccessExpression:
                return At(node, store.ArgumentExpression(n));
            case SyntaxKind.PropertyAccessExpression:
                return At(node, store.Name(n));
            case SyntaxKind.TypeReference:
            {
                var typeName = store.TypeName(n);
                return store.Kind(typeName) == SyntaxKind.QualifiedName ? At(node, store.Right(typeName)) : At(node, n);
            }
            default:
                return At(node, n);
        }
    }

    // ---- type parameters ----

    // checker.ts checkTypeParameter
    private void CheckTypeParameter(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar Checking
        CheckGrammarModifiers(node);
        if (!store.Expression(n).IsNone)
        {
            GrammarErrorOnFirstToken(At(node, store.Expression(n)), Diagnostics.Type_expected);
        }
        CheckSourceElement(At(node, store.Constraint(n)));
        CheckSourceElement(At(node, store.Default(n)));
        var typeParameter = GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(node));
        // Resolve base constraint to reveal circularity errors
        GetBaseConstraintOfType(typeParameter);
        if (GetResolvedTypeParameterDefault(typeParameter) == circularConstraintType)
        {
            Error(At(node, store.Default(n)), Diagnostics.Type_parameter_0_has_a_circular_default, TypeToString(typeParameter));
        }
        var constraintType = GetConstraintOfTypeParameter(typeParameter);
        var defaultType = GetDefaultFromTypeParameter(typeParameter);
        if (!constraintType.IsNone && !defaultType.IsNone)
        {
            CheckTypeAssignableTo(defaultType, GetTypeWithThisArgument(InstantiateType(constraintType, MakeUnaryTypeMapper(typeParameter, defaultType)), defaultType),
                At(node, store.Default(n)), Diagnostics.Type_0_does_not_satisfy_the_constraint_1);
        }
        CheckNodeDeferred(node);
        CheckTypeNameIsReserved(At(node, store.Name(n)), Diagnostics.Type_parameter_name_cannot_be_0);
    }

    // checker.ts checkTypeParameterDeferred
    private void CheckTypeParameterDeferred(NodeRef node)
    {
        var store = StoreOf(node);
        var parent = store.Parent(node.Node);
        var parentKind = store.Kind(parent);
        if (parentKind is not (SyntaxKind.InterfaceDeclaration or SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression or SyntaxKind.TypeAliasDeclaration))
        {
            return;
        }
        var typeParameter = GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(node));
        var modifiers = GetTypeParameterModifiers(typeParameter) & (ModifierFlags.In | ModifierFlags.Out);
        if (modifiers == 0)
        {
            return;
        }
        var symbol = GetSymbolOfDeclaration(At(node, parent));
        if (parentKind == SyntaxKind.TypeAliasDeclaration && (ObjectFlagsOf(GetDeclaredTypeOfSymbol(symbol)) & (ObjectFlags.Anonymous | ObjectFlags.Mapped)) == 0)
        {
            Error(node, Diagnostics.Variance_annotations_are_only_supported_in_type_aliases_for_object_function_constructor_and_mapped_types);
        }
        else if (modifiers is ModifierFlags.In or ModifierFlags.Out)
        {
            var source = CreateMarkerType(symbol, typeParameter, modifiers == ModifierFlags.Out ? markerSubTypeForCheck : markerSuperTypeForCheck);
            var target = CreateMarkerType(symbol, typeParameter, modifiers == ModifierFlags.Out ? markerSuperTypeForCheck : markerSubTypeForCheck);
            var saveVarianceTypeParameter = varianceTypeParameter;
            varianceTypeParameter = typeParameter;
            CheckTypeAssignableTo(source, target, node, Diagnostics.Type_0_is_not_assignable_to_type_1_as_implied_by_variance_annotation);
            varianceTypeParameter = saveVarianceTypeParameter;
        }
    }

    // checker.ts checkTypeParameters: each type parameter, then (lazily) defaults and duplicates
    private void CheckTypeParameters(NodeRef context, NodeList typeParameterDeclarations)
    {
        if (typeParameterDeclarations.IsNone)
        {
            return;
        }
        CheckTypeParameters(context, StoreOf(context).Nodes(typeParameterDeclarations).ToArray());
    }

    private void CheckTypeParameters(NodeRef context, Node[] declarations)
    {
        var store = StoreOf(context);
        var seenDefault = false;
        for (var i = 0; i < declarations.Length; i++)
        {
            var node = At(context, declarations[i]);
            CheckTypeParameter(node);
            var defaultNode = store.Default(declarations[i]);
            if (!defaultNode.IsNone)
            {
                seenDefault = true;
                CheckTypeParametersNotReferenced(At(context, defaultNode), declarations, i);
            }
            else if (seenDefault)
            {
                Error(node, Diagnostics.Required_type_parameters_may_not_follow_optional_type_parameters);
            }
            for (var j = 0; j < i; j++)
            {
                if (NodeSymbol(At(context, declarations[j])) == NodeSymbol(node))
                {
                    Error(At(context, store.Name(declarations[i])), Diagnostics.Duplicate_identifier_0, Utilities.DeclarationNameToString(store, store.Name(declarations[i])));
                }
            }
        }
    }

    // checker.ts checkTypeParametersNotReferenced: type parameter defaults only reference previously declared type parameters
    private void CheckTypeParametersNotReferenced(NodeRef root, Node[] typeParameters, int index)
    {
        var store = StoreOf(root);
        Visit(root.Node);

        void Visit(Node node)
        {
            if (store.Kind(node) == SyntaxKind.TypeReference)
            {
                var type = GetTypeFromTypeReference(At(root, node));
                if ((Types.Flags(type) & TypeFlags.TypeParameter) != 0)
                {
                    for (var i = index; i < typeParameters.Length; i++)
                    {
                        if (Types.Symbol(type) == GetSymbolOfDeclaration(At(root, typeParameters[i])))
                        {
                            Error(At(root, node), Diagnostics.Type_parameter_defaults_can_only_reference_previously_declared_type_parameters);
                        }
                    }
                }
            }
            var children = new List<Node>();
            NodeVisitor.ForEachChild<object>(store, node, child =>
            {
                children.Add(child);
                return null;
            });
            foreach (var child in children)
            {
                Visit(child);
            }
        }
    }

    // checker.ts checkTypeNameIsReserved
    private void CheckTypeNameIsReserved(NodeRef name, DiagnosticMessage message)
    {
        if (name.IsNone || Kind(name) != SyntaxKind.Identifier)
        {
            return;
        }
        var store = StoreOf(name);
        // TS 1.0 spec (April 2014): 3.6.1
        // The predefined type keywords are reserved and cannot be used as names of user defined types.
        var text = store.Texts.GetString(store.EscapedText(name.Node));
        switch (text)
        {
            case "any":
            case "unknown":
            case "never":
            case "number":
            case "bigint":
            case "boolean":
            case "string":
            case "symbol":
            case "void":
            case "object":
            case "undefined":
                Error(name, message, text);
                break;
        }
    }

    // ---- signatures and functions ----

    /// <summary>checker.ts checkSignatureDeclaration.</summary>
    private void CheckSignatureDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var kind = store.Kind(n);
        // Grammar checking
        if (kind == SyntaxKind.IndexSignature)
        {
            CheckGrammarIndexSignature(node);
        }
        else if (kind is SyntaxKind.FunctionType or SyntaxKind.FunctionDeclaration or SyntaxKind.ConstructorType or SyntaxKind.CallSignature or SyntaxKind.Constructor or SyntaxKind.ConstructSignature)
        {
            CheckGrammarFunctionLikeDeclaration(node);
        }
        // (async functions and generators prior to ES2018 need emit helpers: Gate C targets ES2025)
        CheckTypeParameters(node, [.. GetEffectiveTypeParameterDeclarations(store, n)]);
        CheckUnmatchedJSDocParameters(node);
        foreach (var parameter in store.Nodes(store.Parameters(n)))
        {
            CheckParameter(At(node, parameter));
        }
        var returnTypeNode = store.Type(n);
        CheckSourceElement(At(node, returnTypeNode));
        // checkSignatureDeclarationDiagnostics
        // (checkCollisionWithArgumentsInGeneratedCode: Gate C targets ES2025)
        if (noImplicitAny && returnTypeNode.IsNone)
        {
            switch (kind)
            {
                case SyntaxKind.ConstructSignature:
                    Error(node, Diagnostics.Construct_signature_which_lacks_return_type_annotation_implicitly_has_an_any_return_type);
                    break;
                case SyntaxKind.CallSignature:
                    Error(node, Diagnostics.Call_signature_which_lacks_return_type_annotation_implicitly_has_an_any_return_type);
                    break;
            }
        }
        if (!returnTypeNode.IsNone)
        {
            var functionFlags = GetFunctionFlagsOf(store, n);
            var returnTypeRef = At(node, returnTypeNode);
            if ((functionFlags & (FunctionFlags.Invalid | FunctionFlags.Generator)) == FunctionFlags.Generator)
            {
                var returnType = GetTypeFromTypeNode(returnTypeRef);
                if (returnType == voidType)
                {
                    Error(returnTypeRef, Diagnostics.A_generator_cannot_have_a_void_type_annotation);
                }
                else
                {
                    CheckGeneratorInstantiationAssignabilityToReturnType(returnType, functionFlags, returnTypeRef);
                }
            }
            else if ((functionFlags & FunctionFlags.AsyncGenerator) == FunctionFlags.Async)
            {
                CheckAsyncFunctionReturnType(node, returnTypeRef);
            }
        }
        if (kind is not (SyntaxKind.IndexSignature or SyntaxKind.JSDocFunctionType))
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    // checker.ts checkAsyncFunctionReturnType: an async function has a valid Promise-compatible return type
    private void CheckAsyncFunctionReturnType(NodeRef node, NodeRef returnTypeNode)
    {
        // As part of our emit for an async function, we will need to emit the entity name of
        // the return type annotation as an expression. To meet the necessary runtime semantics
        // for __awaiter, we must also check that the type of the declaration (e.g. the static
        // side or "constructor" of the promise type) is compatible `PromiseConstructorLike`.
        //
        // An example might be (from lib.es6.d.ts):
        //
        //  interface Promise<T> { ... }
        //  interface PromiseConstructor {
        //      new <T>(...): Promise<T>;
        //  }
        //  declare var Promise: PromiseConstructor;
        //
        // When an async function declares a return type annotation of `Promise<T>`, we
        // need to get the type of the `Promise` variable declaration above, which would
        // be `PromiseConstructor`.
        //
        // The same case applies to a class:
        //
        //  declare class Promise<T> {
        //      constructor(...);
        //      then<U>(...): Promise<U>;
        //  }
        //
        var returnType = GetTypeFromTypeNode(returnTypeNode);
        if (languageVersion >= ScriptTarget.ES2017)
        {
            if (IsErrorType(returnType))
            {
                return;
            }
            var globalPromiseType = GetGlobalPromiseType(reportErrors: true);
            if (globalPromiseType != emptyGenericType && !IsReferenceToType(returnType, globalPromiseType))
            {
                // The promise type was not a valid type reference to the global promise type, so we
                // report an error and return the unknown type.
                var awaited = GetAwaitedTypeNoAlias(returnType);
                ReportErrorForInvalidReturnType(Diagnostics.The_return_type_of_an_async_function_or_method_must_be_the_global_Promise_T_type_Did_you_mean_to_write_Promise_0,
                    returnTypeNode, TypeToString(awaited.IsNone ? voidType : awaited));
                return;
            }
        }
        // (the promise constructor check prior to ES2017: Gate C targets ES2025)
        CheckAwaitedType(returnType, withAlias: false, node, Diagnostics.The_return_type_of_an_async_function_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member);

        void ReportErrorForInvalidReturnType(DiagnosticMessage message, NodeRef location, string arg) => Error(location, message, arg);
    }

    // checker.ts checkMethodDeclaration
    private void CheckMethodDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var name = store.Name(n);
        // Grammar checking
        if (!CheckGrammarMethod(node))
        {
            CheckGrammarComputedPropertyName(At(node, name));
        }
        if (store.Kind(n) == SyntaxKind.MethodDeclaration && !store.AsteriskToken(n).IsNone && store.Kind(name) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(name), "constructor"))
        {
            Error(At(node, name), Diagnostics.Class_constructor_may_not_be_a_generator);
        }
        // Grammar checking for modifiers is done inside the function checkGrammarFunctionLikeDeclaration
        CheckFunctionOrMethodDeclaration(node);
        // method signatures already report "implementation not allowed in ambient context" elsewhere
        if (Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Abstract) && store.Kind(n) == SyntaxKind.MethodDeclaration && !store.Body(n).IsNone)
        {
            Error(node, Diagnostics.Method_0_cannot_have_an_implementation_because_it_is_marked_abstract, Utilities.DeclarationNameToString(store, name));
        }
        // Private named methods are only allowed in class declarations
        if (store.Kind(name) == SyntaxKind.PrivateIdentifier && Utilities.GetContainingClass(store, n).IsNone)
        {
            Error(node, Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
        }
    }

    // checker.ts checkClassStaticBlockDeclaration
    private void CheckClassStaticBlockDeclaration(NodeRef node)
    {
        CheckGrammarModifiers(node);
        ForEachChildCheckSourceElement(node);
    }

    // checker.ts checkConstructorDeclaration
    private void CheckConstructorDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar check on signature of constructor and modifier of the constructor is done in checkSignatureDeclaration function.
        CheckSignatureDeclaration(node);
        // Grammar check for checking only related to constructorDeclaration
        if (!CheckGrammarConstructorTypeParameters(node))
        {
            CheckGrammarConstructorTypeAnnotation(node);
        }
        CheckSourceElement(At(node, store.Body(n)));
        var symbol = GetSymbolOfDeclaration(node);
        var firstDeclaration = GetDeclarationOfKind(symbol, store.Kind(n));
        // Only type check the symbol once
        if (node == firstDeclaration)
        {
            CheckFunctionOrConstructorSymbol(symbol);
        }
        // exit early in the case of signature - super checks are not relevant to them
        if (Utilities.NodeIsMissing(store, store.Body(n)))
        {
            return;
        }
        // checkConstructorDeclarationDiagnostics
        // TS 1.0 spec (April 2014): 8.3.2
        // Constructors of classes with no extends clause may not contain super calls, whereas
        // constructors of derived classes must contain at least one super call somewhere in their function body.
        var containingClassDecl = At(node, store.Parent(n));
        if (GetClassExtendsHeritageElement(store, containingClassDecl.Node).IsNone)
        {
            return;
        }
        var classExtendsNull = ClassDeclarationExtendsNull(containingClassDecl);
        var superCall = FindFirstSuperCall(store, store.Body(n));
        if (!superCall.IsNone)
        {
            if (classExtendsNull)
            {
                Error(At(node, superCall), Diagnostics.A_constructor_cannot_contain_a_super_call_when_its_class_extends_null);
            }
            // A super call must be root-level in a constructor if both of the following are true:
            // - The containing class is a derived class.
            // - The constructor declares parameter properties
            //   or the containing class declares instance member variables with initializers.
            var superCallShouldBeRootLevel = false;
            if (!EmitStandardClassFields)
            {
                foreach (var m in store.Nodes(store.Members(containingClassDecl.Node)))
                {
                    superCallShouldBeRootLevel |= IsInstancePropertyWithInitializerOrPrivateIdentifierProperty(store, m);
                }
                foreach (var p in store.Nodes(store.Parameters(n)))
                {
                    superCallShouldBeRootLevel |= Utilities.HasSyntacticModifierUncached(store, p, ModifierFlags.ParameterPropertyModifier);
                }
            }
            if (superCallShouldBeRootLevel)
            {
                // Until we have better flow analysis, it is an error to place the super call within any kind of block or conditional
                // See GH #8277
                if (!SuperCallIsRootLevelInConstructor(store, superCall, store.Body(n)))
                {
                    Error(At(node, superCall), Diagnostics.A_super_call_must_be_a_root_level_statement_within_a_constructor_of_a_derived_class_that_contains_initialized_properties_parameter_properties_or_private_identifiers);
                }
                // Skip past any prologue directives to check statements for referring to 'super' or 'this' before a super call
                else
                {
                    var superCallStatement = Node.None;
                    foreach (var statement in store.Nodes(store.Statements(store.Body(n))))
                    {
                        if (store.Kind(statement) == SyntaxKind.ExpressionStatement && IsSuperCall(store, SkipOuterExpressions(store, store.Expression(statement))))
                        {
                            superCallStatement = statement;
                            break;
                        }
                        if (NodeImmediatelyReferencesSuperOrThis(store, statement))
                        {
                            break;
                        }
                    }
                    // Until we have better flow analysis, it is an error to place the super call within any kind of block or conditional
                    // See GH #8277
                    if (superCallStatement.IsNone)
                    {
                        Error(node, Diagnostics.A_super_call_must_be_the_first_statement_in_the_constructor_to_refer_to_super_or_this_when_a_derived_class_contains_initialized_properties_parameter_properties_or_private_identifiers);
                    }
                }
            }
        }
        else if (!classExtendsNull)
        {
            Error(node, Diagnostics.Constructors_for_derived_classes_must_contain_a_super_call);
        }
    }

    private static bool IsInstancePropertyWithInitializerOrPrivateIdentifierProperty(NodeStore store, Node n) =>
        IsPrivateIdentifierClassElementDeclaration(store, n) ||
        (store.Kind(n) == SyntaxKind.PropertyDeclaration && !Utilities.IsStatic(store, n) && !store.Initializer(n).IsNone);

    // utilities.ts isPrivateIdentifierClassElementDeclaration
    private static bool IsPrivateIdentifierClassElementDeclaration(NodeStore store, Node n) =>
        store.Kind(n) is SyntaxKind.PropertyDeclaration or SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor &&
        store.Kind(store.Name(n)) == SyntaxKind.PrivateIdentifier;

    private static bool SuperCallIsRootLevelInConstructor(NodeStore store, Node superCall, Node body)
    {
        var superCallParent = WalkUpParenthesizedExpressions(store, store.Parent(superCall));
        return store.Kind(superCallParent) == SyntaxKind.ExpressionStatement && store.Parent(superCallParent) == body;
    }

    // checker.ts findFirstSuperCall
    private static Node FindFirstSuperCall(NodeStore store, Node node)
    {
        if (IsSuperCall(store, node))
        {
            return node;
        }
        if (Utilities.IsFunctionLike(store, node))
        {
            return Node.None;
        }
        var result = NodeVisitor.ForEachChild<object>(store, node, child =>
        {
            var found = FindFirstSuperCall(store, child);
            return found.IsNone ? null : found;
        });
        return result is Node found ? found : Node.None;
    }

    // checker.ts nodeImmediatelyReferencesSuperOrThis
    private static bool NodeImmediatelyReferencesSuperOrThis(NodeStore store, Node node)
    {
        if (store.Kind(node) is SyntaxKind.SuperKeyword or SyntaxKind.ThisKeyword)
        {
            return true;
        }
        if (IsThisContainerOrFunctionBlock(store, node))
        {
            return false;
        }
        return NodeVisitor.ForEachChild<object>(store, node, child => NodeImmediatelyReferencesSuperOrThis(store, child) ? true : null) is not null;
    }

    // utilities.ts isThisContainerOrFunctionBlock
    private static bool IsThisContainerOrFunctionBlock(NodeStore store, Node node)
    {
        switch (store.Kind(node))
        {
            // Arrow functions use the same scope, but may do so in a "delayed" manner
            // For example, `const getThis = () => this` may be before a super() call in a derived constructor
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.PropertyDeclaration:
                return true;
            case SyntaxKind.Block:
                switch (store.Kind(store.Parent(node)))
                {
                    case SyntaxKind.Constructor:
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.GetAccessor:
                    case SyntaxKind.SetAccessor:
                        // Object properties can have computed names; only method-like bodies start a new scope
                        return true;
                    default:
                        return false;
                }
            default:
                return false;
        }
    }

    // utilities.ts getClassExtendsHeritageElement
    private static Node GetClassExtendsHeritageElement(NodeStore store, Node node)
    {
        var clauses = store.HeritageClauses(node);
        if (clauses.IsNone)
        {
            return Node.None;
        }
        foreach (var clause in store.Nodes(clauses))
        {
            if (store.Token(clause) == SyntaxKind.ExtendsKeyword)
            {
                var types = store.Nodes(store.Types(clause));
                return types.Length > 0 ? types[0] : Node.None;
            }
        }
        return Node.None;
    }

    // utilities.ts getEffectiveImplementsTypeNodes (getImplementsHeritageClauseElements)
    private static List<Node> GetImplementsTypeNodes(NodeStore store, Node node)
    {
        var result = new List<Node>();
        if (Utilities.IsInJSFile(store, node))
        {
            // getJSDocImplementsTags(node).map(n => n.class)
            foreach (var tag in UtilitiesPublic.GetJSDocTags(store, node))
            {
                if (store.Kind(tag) == SyntaxKind.JSDocImplementsTag)
                {
                    result.Add(store.Class(tag));
                }
            }
            return result;
        }
        var clauses = store.HeritageClauses(node);
        if (!clauses.IsNone)
        {
            foreach (var clause in store.Nodes(clauses))
            {
                if (store.Token(clause) == SyntaxKind.ImplementsKeyword)
                {
                    result.AddRange(store.Nodes(store.Types(clause)).ToArray());
                    break; // getHeritageClause: the first implements clause
                }
            }
        }
        return result;
    }

    // checker.ts checkAccessorDeclaration
    private void CheckAccessorDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var name = store.Name(n);
        if (store.Kind(name) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(name), "constructor") && Utilities.IsClassLike(store, store.Parent(n)))
        {
            Error(At(node, name), Diagnostics.Class_constructor_may_not_be_an_accessor);
        }
        // checkAccessorDeclarationDiagnostics
        // Grammar checking accessors
        if (!CheckGrammarFunctionLikeDeclaration(node) && !CheckGrammarAccessor(node))
        {
            CheckGrammarComputedPropertyName(At(node, name));
        }
        CheckDecorators(node);
        CheckSignatureDeclaration(node);
        if (store.Kind(n) == SyntaxKind.GetAccessor)
        {
            if ((store.Flags(n) & NodeFlags.Ambient) == 0 && !Utilities.NodeIsMissing(store, store.Body(n)) && (store.Flags(n) & NodeFlags.HasImplicitReturn) != 0)
            {
                if ((store.Flags(n) & NodeFlags.HasExplicitReturn) == 0)
                {
                    Error(At(node, name), Diagnostics.A_get_accessor_must_return_a_value);
                }
            }
        }
        // Do not use hasDynamicName here, because that returns false for well known symbols.
        // We want to perform checkComputedPropertyName for all computed properties, including
        // well known symbols.
        if (store.Kind(name) == SyntaxKind.ComputedPropertyName)
        {
            CheckComputedPropertyName(At(node, name));
        }
        if (HasBindableName(node))
        {
            // TypeScript 1.0 spec (April 2014): 8.4.3
            // Accessors for the same member name must specify the same accessibility.
            var symbol = GetSymbolOfDeclaration(node);
            var getter = GetDeclarationOfKind(symbol, SyntaxKind.GetAccessor);
            var setter = GetDeclarationOfKind(symbol, SyntaxKind.SetAccessor);
            if (!getter.IsNone && !setter.IsNone && checkedAccessorPairs.Add(getter))
            {
                var getterFlags = Utilities.GetSyntacticModifierFlagsNoCache(StoreOf(getter), getter.Node);
                var setterFlags = Utilities.GetSyntacticModifierFlagsNoCache(StoreOf(setter), setter.Node);
                var getterName = At(getter, StoreOf(getter).Name(getter.Node));
                var setterName = At(setter, StoreOf(setter).Name(setter.Node));
                if ((getterFlags & ModifierFlags.Abstract) != (setterFlags & ModifierFlags.Abstract))
                {
                    Error(getterName, Diagnostics.Accessors_must_both_be_abstract_or_non_abstract);
                    Error(setterName, Diagnostics.Accessors_must_both_be_abstract_or_non_abstract);
                }
                if (((getterFlags & ModifierFlags.Protected) != 0 && (setterFlags & (ModifierFlags.Protected | ModifierFlags.Private)) == 0) ||
                    ((getterFlags & ModifierFlags.Private) != 0 && (setterFlags & ModifierFlags.Private) == 0))
                {
                    Error(getterName, Diagnostics.A_get_accessor_must_be_at_least_as_accessible_as_the_setter);
                    Error(setterName, Diagnostics.A_get_accessor_must_be_at_least_as_accessible_as_the_setter);
                }
            }
        }
        var returnType = GetTypeOfAccessors(GetSymbolOfDeclaration(node));
        if (store.Kind(n) == SyntaxKind.GetAccessor)
        {
            CheckAllCodePathsInNonVoidFunctionReturnOrThrow(node, returnType);
        }
        CheckSourceElement(At(node, store.Body(n)));
        // (setNodeLinksForPrivateIdentifierScope: emit flags only)
    }

    // checker.ts checkFunctionDeclaration
    private void CheckFunctionDeclaration(NodeRef node)
    {
        // checkFunctionDeclarationDiagnostics runs first (a lazy diagnostic in checker.ts, before the declaration's checks)
        CheckFunctionOrMethodDeclaration(node);
        CheckGrammarForGenerator(node);
        CheckCollisionsForDeclarationName(node, At(node, StoreOf(node).Name(node.Node)));
    }

    // checker.ts checkFunctionOrMethodDeclaration
    private void CheckFunctionOrMethodDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        CheckDecorators(node);
        CheckSignatureDeclaration(node);
        var functionFlags = GetFunctionFlagsOf(store, n);
        var name = store.Name(n);
        // Do not use hasDynamicName here, because that returns false for well known symbols.
        // We want to perform checkComputedPropertyName for all computed properties, including
        // well known symbols.
        if (!name.IsNone && store.Kind(name) == SyntaxKind.ComputedPropertyName)
        {
            // This check will account for methods in class/interface declarations,
            // as well as accessors in classes/object literals
            CheckComputedPropertyName(At(node, name));
        }
        if (HasBindableName(node))
        {
            // first we want to check the local symbol that contain this declaration
            // - if node.localSymbol !== undefined - this is current declaration is exported and localSymbol points to the local symbol
            // - if node.localSymbol === undefined - this node is non-exported so we can just pick the result of getSymbolOfNode
            var symbol = GetSymbolOfDeclaration(node);
            var localSymbol = LocalSymbolOfDeclaration(node);
            if (localSymbol.IsNone)
            {
                localSymbol = symbol;
            }
            // Since the javascript won't do semantic analysis like typescript,
            // if the javascript file comes before the typescript file and both contain same name functions,
            // checkFunctionOrConstructorSymbol wouldn't be called if we didnt ignore javascript function.
            var firstDeclaration = DeclarationsOf(localSymbol)?.Find(d => Kind(d) == store.Kind(n) && (StoreOf(d).Flags(d.Node) & NodeFlags.JavaScriptFile) == 0) ?? NodeRef.None;
            // Only type check the symbol once
            if (node == firstDeclaration)
            {
                CheckFunctionOrConstructorSymbol(localSymbol);
            }
            if (!ParentOf(symbol).IsNone)
            {
                // run check on export symbol to check that modifiers agree across all exported declarations
                CheckFunctionOrConstructorSymbol(symbol);
            }
        }
        var body = store.Kind(n) == SyntaxKind.MethodSignature ? Node.None : store.Body(n);
        CheckSourceElement(At(node, body));
        CheckAllCodePathsInNonVoidFunctionReturnOrThrow(node, GetReturnTypeFromAnnotation(node));
        // checkFunctionOrMethodDeclarationDiagnostics
        if (GetEffectiveReturnTypeNode(store, n).IsNone)
        {
            // Report an implicit any error if there is no body, no explicit return type, and node is not a private method
            // in an ambient context
            if (Utilities.NodeIsMissing(store, body) && !IsPrivateWithinAmbient(store, n))
            {
                ReportImplicitAny(node, anyType);
            }
            if ((functionFlags & FunctionFlags.Generator) != 0 && !Utilities.NodeIsMissing(store, body))
            {
                // A generator with a body and no type annotation can still cause errors. It can error if the
                // yielded values have no common supertype, or it can give an implicit any error if it has no
                // yielded values. The only way to trigger these errors is to try checking its return type.
                GetReturnTypeOfSignature(GetSignatureFromDeclaration(node));
            }
        }
        // A js function declaration can have a @type tag instead of a return type node, but that type must have a call signature
        if (Utilities.IsInJSFile(store, n))
        {
            var typeTag = UtilitiesPublic.GetJSDocTypeTag(store, n);
            if (!typeTag.IsNone && !store.TypeExpression(typeTag).IsNone &&
                GetContextualCallSignature(GetTypeFromTypeNode(At(node, store.TypeExpression(typeTag))), node).IsNone)
            {
                Error(At(node, store.Type(store.TypeExpression(typeTag))), Diagnostics.The_type_of_a_function_declaration_must_match_the_function_s_signature);
            }
        }
    }

    // node.localSymbol (the binder's local symbol of an exported declaration)
    private SymbolRef LocalSymbolOfDeclaration(NodeRef node)
    {
        var local = BindingOf(node.File).LocalSymbolOf(node.Node);
        return local.IsNone ? SymbolRef.None : GetMergedSymbol(new SymbolRef(node.File, local.Index));
    }

    // checker.ts getEffectiveDeclarationFlags
    private ModifierFlags GetEffectiveDeclarationFlags(NodeRef n, ModifierFlags flagsToCheck)
    {
        var store = StoreOf(n);
        var flags = Utilities.GetCombinedModifierFlags(store, n.Node);
        // children of classes (even ambient classes) should not be marked as ambient or export
        // because those flags have no useful semantics there.
        var parent = store.Parent(n.Node);
        if (store.Kind(parent) is not (SyntaxKind.InterfaceDeclaration or SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression) && (store.Flags(n.Node) & NodeFlags.Ambient) != 0)
        {
            var container = Utilities.GetEnclosingContainer(store, n.Node);
            if (!container.IsNone && (store.Flags(container) & NodeFlags.ExportContext) != 0 && (flags & ModifierFlags.Ambient) == 0 &&
                !(store.Kind(parent) == SyntaxKind.ModuleBlock && store.Kind(store.Parent(parent)) == SyntaxKind.ModuleDeclaration && Utilities.IsGlobalScopeAugmentation(store, store.Parent(parent))))
            {
                // It is nested in an ambient export context, which means it is automatically exported
                flags |= ModifierFlags.Export;
            }
            flags |= ModifierFlags.Ambient;
        }
        return flags & flagsToCheck;
    }

    // checker.ts checkFunctionOrConstructorSymbol (a lazy diagnostic)
    private void CheckFunctionOrConstructorSymbol(SymbolRef symbol)
    {
        const ModifierFlags flagsToCheck = ModifierFlags.Export | ModifierFlags.Ambient | ModifierFlags.Private | ModifierFlags.Protected | ModifierFlags.Abstract;
        var someNodeFlags = ModifierFlags.None;
        var allNodeFlags = flagsToCheck;
        var someHaveQuestionToken = false;
        var allHaveQuestionToken = true;
        var hasOverloads = false;
        var bodyDeclaration = NodeRef.None;
        var lastSeenNonAmbientDeclaration = NodeRef.None;
        var previousDeclaration = NodeRef.None;
        var declarations = DeclarationsOf(symbol);
        var isConstructor = (SymbolFlagsOf(symbol) & SymbolFlags.Constructor) != 0;
        var duplicateFunctionDeclaration = false;
        var multipleConstructorImplementation = false;
        var hasNonAmbientClass = false;
        var functionDeclarations = new List<NodeRef>();
        if (declarations is not null)
        {
            foreach (var current in declarations)
            {
                var store = StoreOf(current);
                var node = current.Node;
                var kind = store.Kind(node);
                var inAmbientContext = (store.Flags(node) & NodeFlags.Ambient) != 0;
                var parent = store.Parent(node);
                var inAmbientContextOrInterface = (!parent.IsNone && store.Kind(parent) is SyntaxKind.InterfaceDeclaration or SyntaxKind.TypeLiteral) || inAmbientContext;
                if (inAmbientContextOrInterface)
                {
                    // check if declarations are consecutive only if they are non-ambient
                    // 1. ambient declarations can be interleaved
                    // i.e. this is legal
                    //     declare function foo();
                    //     declare function bar();
                    //     declare function foo();
                    // 2. mixing ambient and non-ambient declarations is a separate error that will be reported - do not want to report an extra one
                    previousDeclaration = NodeRef.None;
                }
                if (kind is SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression && !inAmbientContext)
                {
                    hasNonAmbientClass = true;
                }
                if (kind is SyntaxKind.FunctionDeclaration or SyntaxKind.MethodDeclaration or SyntaxKind.MethodSignature or SyntaxKind.Constructor)
                {
                    functionDeclarations.Add(current);
                    var currentNodeFlags = GetEffectiveDeclarationFlags(current, flagsToCheck);
                    someNodeFlags |= currentNodeFlags;
                    allNodeFlags &= currentNodeFlags;
                    someHaveQuestionToken = someHaveQuestionToken || IsOptionalDeclaration(store, node);
                    allHaveQuestionToken = allHaveQuestionToken && IsOptionalDeclaration(store, node);
                    var bodyIsPresent = !Utilities.NodeIsMissing(store, store.Body(node));
                    if (bodyIsPresent && !bodyDeclaration.IsNone)
                    {
                        if (isConstructor)
                        {
                            multipleConstructorImplementation = true;
                        }
                        else
                        {
                            duplicateFunctionDeclaration = true;
                        }
                    }
                    else if (!previousDeclaration.IsNone && previousDeclaration.File == current.File && store.Parent(previousDeclaration.Node) == parent &&
                        store.End(previousDeclaration.Node) != store.Pos(node))
                    {
                        ReportImplementationExpectedError(previousDeclaration);
                    }
                    if (bodyIsPresent)
                    {
                        if (bodyDeclaration.IsNone)
                        {
                            bodyDeclaration = current;
                        }
                    }
                    else
                    {
                        hasOverloads = true;
                    }
                    previousDeclaration = current;
                    if (!inAmbientContextOrInterface)
                    {
                        lastSeenNonAmbientDeclaration = current;
                    }
                }
                if (Utilities.IsInJSFile(store, node) && Utilities.IsFunctionLike(store, node) && UtilitiesPublic.HasJsDocList(store, node))
                {
                    hasOverloads = !GetFirstJSDocTagOfKind(store, node, SyntaxKind.JSDocOverloadTag).IsNone;
                }
            }
        }
        if (multipleConstructorImplementation)
        {
            foreach (var declaration in functionDeclarations)
            {
                Error(declaration, Diagnostics.Multiple_constructor_implementations_are_not_allowed);
            }
        }
        if (duplicateFunctionDeclaration)
        {
            foreach (var declaration in functionDeclarations)
            {
                Error(NameOrNode(declaration), Diagnostics.Duplicate_function_implementation);
            }
        }
        if (hasNonAmbientClass && !isConstructor && (SymbolFlagsOf(symbol) & SymbolFlags.Function) != 0 && declarations is not null)
        {
            var relatedList = new List<ProgramRelatedInformation>();
            foreach (var d in declarations)
            {
                if (Kind(d) == SyntaxKind.ClassDeclaration)
                {
                    relatedList.Add(CreateRelatedForNode(d, Diagnostics.Consider_adding_a_declare_modifier_to_this_class));
                }
            }
            var relatedDiagnostics = relatedList.ToArray();
            foreach (var declaration in declarations)
            {
                var diagnostic = Kind(declaration) switch
                {
                    SyntaxKind.ClassDeclaration => Diagnostics.Class_declaration_cannot_implement_overload_list_for_0,
                    SyntaxKind.FunctionDeclaration => Diagnostics.Function_with_bodies_can_only_merge_with_classes_that_are_ambient,
                    _ => null,
                };
                if (diagnostic is not null)
                {
                    AddRelatedInfo(Error(NameOrNode(declaration), diagnostic, SymbolName(symbol)), relatedDiagnostics);
                }
            }
        }
        // Abstract methods can't have an implementation -- in particular, they don't need one.
        if (!lastSeenNonAmbientDeclaration.IsNone)
        {
            var lastStore = StoreOf(lastSeenNonAmbientDeclaration);
            if (lastStore.Body(lastSeenNonAmbientDeclaration.Node).IsNone && !Utilities.HasSyntacticModifierUncached(lastStore, lastSeenNonAmbientDeclaration.Node, ModifierFlags.Abstract) &&
                lastStore.QuestionToken(lastSeenNonAmbientDeclaration.Node).IsNone)
            {
                ReportImplementationExpectedError(lastSeenNonAmbientDeclaration);
            }
        }
        if (hasOverloads)
        {
            if (declarations is not null)
            {
                CheckFlagAgreementBetweenOverloads(declarations, bodyDeclaration, flagsToCheck, someNodeFlags, allNodeFlags);
                CheckQuestionTokenAgreementBetweenOverloads(declarations, bodyDeclaration, someHaveQuestionToken, allHaveQuestionToken);
            }
            if (!bodyDeclaration.IsNone)
            {
                var signatures = GetSignaturesOfSymbol(symbol);
                var bodySignature = GetSignatureFromDeclaration(bodyDeclaration);
                foreach (var signature in signatures)
                {
                    if (!IsImplementationCompatibleWithOverload(bodySignature, signature))
                    {
                        var errorNode = Signatures.Record(signature).Declaration;
                        if (!errorNode.IsNone && Kind(errorNode) == SyntaxKind.JSDocSignature)
                        {
                            errorNode = At(errorNode, StoreOf(errorNode).TagName(StoreOf(errorNode).Parent(errorNode.Node)));
                        }
                        AddRelatedInfo(Error(errorNode, Diagnostics.This_overload_signature_is_not_compatible_with_its_implementation_signature),
                            CreateRelatedForNode(bodyDeclaration, Diagnostics.The_implementation_signature_is_declared_here));
                        break;
                    }
                }
            }
        }

        NodeRef GetCanonicalOverload(List<NodeRef> overloads, NodeRef implementation)
        {
            // Consider the canonical set of flags to be the flags of the bodyDeclaration or the first declaration
            // Error on all deviations from this canonical set of flags
            // The caveat is that if some overloads are defined in lib.d.ts, we don't want to
            // report the errors on those. To achieve this, we will say that the implementation is
            // the canonical signature only if it is in the same container as the first overload
            var implementationSharesContainerWithFirstOverload = !implementation.IsNone && Parent(implementation) == Parent(overloads[0]);
            return implementationSharesContainerWithFirstOverload ? implementation : overloads[0];
        }

        void CheckFlagAgreementBetweenOverloads(List<NodeRef> overloads, NodeRef implementation, ModifierFlags flagsToCheck, ModifierFlags someOverloadFlags, ModifierFlags allOverloadFlags)
        {
            // Error if some overloads have a flag that is not shared by all overloads. To find the
            // deviations, we XOR someOverloadFlags with allOverloadFlags
            var someButNotAllOverloadFlags = someOverloadFlags ^ allOverloadFlags;
            if (someButNotAllOverloadFlags == 0)
            {
                return;
            }
            var canonicalFlags = GetEffectiveDeclarationFlags(GetCanonicalOverload(overloads, implementation), flagsToCheck);
            // group(overloads, o => getSourceFileOfNode(o).fileName): groups in order of first appearance (indexed by
            // file number: one program file per file name)
            var groups = new List<List<NodeRef>>();
            var groupOfFile = new Dictionary<int, List<NodeRef>>();
            foreach (var o in overloads)
            {
                if (groupOfFile.TryGetValue(o.File, out var existing))
                {
                    existing.Add(o);
                }
                else
                {
                    List<NodeRef> group = [o];
                    groups.Add(group);
                    groupOfFile.Add(o.File, group);
                }
            }
            foreach (var group in groups)
            {
                var canonicalFlagsForFile = GetEffectiveDeclarationFlags(GetCanonicalOverload(group, implementation), flagsToCheck);
                foreach (var o in group)
                {
                    var deviation = GetEffectiveDeclarationFlags(o, flagsToCheck) ^ canonicalFlags;
                    var deviationInFile = GetEffectiveDeclarationFlags(o, flagsToCheck) ^ canonicalFlagsForFile;
                    var oName = At(o, Utilities.GetNameOfDeclaration(StoreOf(o), o.Node));
                    if ((deviationInFile & ModifierFlags.Export) != 0)
                    {
                        // Overloads in different files need not all have export modifiers. This is ok:
                        //   // lib.d.ts
                        //   declare function foo(s: number): string;
                        //   declare function foo(s: string): number;
                        //   export { foo };
                        //
                        //   // app.ts
                        //   declare module "lib" {
                        //     export function foo(s: boolean): boolean;
                        //   }
                        Error(oName, Diagnostics.Overload_signatures_must_all_be_exported_or_non_exported);
                    }
                    else if ((deviationInFile & ModifierFlags.Ambient) != 0)
                    {
                        // Though rare, a module augmentation (necessarily ambient) is allowed to add overloads
                        // to a non-ambient function in an implementation file.
                        Error(oName, Diagnostics.Overload_signatures_must_all_be_ambient_or_non_ambient);
                    }
                    else if ((deviation & (ModifierFlags.Private | ModifierFlags.Protected)) != 0)
                    {
                        Error(oName.IsNone ? o : oName, Diagnostics.Overload_signatures_must_all_be_public_private_or_protected);
                    }
                    else if ((deviation & ModifierFlags.Abstract) != 0)
                    {
                        Error(oName, Diagnostics.Overload_signatures_must_all_be_abstract_or_non_abstract);
                    }
                }
            }
        }

        void CheckQuestionTokenAgreementBetweenOverloads(List<NodeRef> overloads, NodeRef implementation, bool someHaveQuestionToken, bool allHaveQuestionToken)
        {
            if (someHaveQuestionToken != allHaveQuestionToken)
            {
                var canonical = GetCanonicalOverload(overloads, implementation);
                var canonicalHasQuestionToken = IsOptionalDeclaration(StoreOf(canonical), canonical.Node);
                foreach (var o in overloads)
                {
                    if (IsOptionalDeclaration(StoreOf(o), o.Node) != canonicalHasQuestionToken)
                    {
                        Error(At(o, Utilities.GetNameOfDeclaration(StoreOf(o), o.Node)), Diagnostics.Overload_signatures_must_all_be_optional_or_required);
                    }
                }
            }
        }

        void ReportImplementationExpectedError(NodeRef declaration)
        {
            var store = StoreOf(declaration);
            var node = declaration.Node;
            var name = store.Name(node);
            if (!name.IsNone && Utilities.NodeIsMissing(store, name))
            {
                return;
            }
            var seen = false;
            var subsequentNode = Node.None;
            NodeVisitor.ForEachChild<object>(store, store.Parent(node), c =>
            {
                if (seen)
                {
                    subsequentNode = c;
                    return true;
                }
                seen = c == node;
                return null;
            });
            // We may be here because of some extra nodes between overloads that could not be parsed into a valid node.
            // In this case the subsequent node is not really consecutive (.pos !== node.end), and we must ignore it here.
            if (!subsequentNode.IsNone && store.Pos(subsequentNode) == store.End(node))
            {
                if (store.Kind(subsequentNode) == store.Kind(node))
                {
                    var subsequentName = store.Name(subsequentNode);
                    var errorNode = At(declaration, !subsequentName.IsNone ? subsequentName : subsequentNode);
                    if (!name.IsNone && !subsequentName.IsNone &&
                        (
                            // both are private identifiers
                            (store.Kind(name) == SyntaxKind.PrivateIdentifier && store.Kind(subsequentName) == SyntaxKind.PrivateIdentifier &&
                                store.Texts.Equals(store.EscapedText(name), store.Texts.GetString(store.EscapedText(subsequentName))!)) ||
                            // Both are computed property names
                            (store.Kind(name) == SyntaxKind.ComputedPropertyName && store.Kind(subsequentName) == SyntaxKind.ComputedPropertyName &&
                                IsTypeIdenticalTo(CheckComputedPropertyName(At(declaration, name)), CheckComputedPropertyName(At(declaration, subsequentName)))) ||
                            // Both are literal property names that are the same.
                            (Utilities.IsPropertyNameLiteral(store, name) && Utilities.IsPropertyNameLiteral(store, subsequentName) &&
                                store.Texts.GetString(Utilities.GetEscapedTextOfIdentifierOrLiteral(store, name)) == store.Texts.GetString(Utilities.GetEscapedTextOfIdentifierOrLiteral(store, subsequentName)))))
                    {
                        var reportError = store.Kind(node) is SyntaxKind.MethodDeclaration or SyntaxKind.MethodSignature &&
                            Utilities.IsStatic(store, node) != Utilities.IsStatic(store, subsequentNode);
                        // we can get here in two cases
                        // 1. mixed static and instance class members
                        // 2. something with the same name was defined before the set of overloads that prevents them from merging
                        // here we'll report error only for the first case since for second we should already report error in binder
                        if (reportError)
                        {
                            var diagnostic = Utilities.IsStatic(store, node) ? Diagnostics.Function_overload_must_be_static : Diagnostics.Function_overload_must_not_be_static;
                            Error(errorNode, diagnostic);
                        }
                        return;
                    }
                    if (!Utilities.NodeIsMissing(store, store.Body(subsequentNode)))
                    {
                        Error(errorNode, Diagnostics.Function_implementation_name_must_be_0, Utilities.DeclarationNameToString(store, name));
                        return;
                    }
                }
            }
            var errorNode2 = !name.IsNone ? At(declaration, name) : declaration;
            if (isConstructor)
            {
                Error(errorNode2, Diagnostics.Constructor_implementation_is_missing);
            }
            else
            {
                // Report different errors regarding non-consecutive blocks of declarations depending on whether
                // the node in question is abstract.
                if (Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Abstract))
                {
                    Error(errorNode2, Diagnostics.All_declarations_of_an_abstract_method_must_be_consecutive);
                }
                else
                {
                    Error(errorNode2, Diagnostics.Function_implementation_is_missing_or_not_immediately_following_the_declaration);
                }
            }
        }
    }

    // getNameOfDeclaration(declaration) || declaration
    private NodeRef NameOrNode(NodeRef declaration)
    {
        var name = Utilities.GetNameOfDeclaration(StoreOf(declaration), declaration.Node);
        return name.IsNone ? declaration : At(declaration, name);
    }

    // checker.ts checkDecorators
    private void CheckDecorators(NodeRef node)
    {
        // skip this check for nodes that cannot have decorators. These should have already had an error reported by
        // checkGrammarModifiers.
        var store = StoreOf(node);
        var modifiers = store.Modifiers(node.Node);
        if (modifiers.IsNone || !CanHaveDecorators(store, node.Node) || !HasDecorators(node) || !NodeCanBeDecorated(compilerOptions.ExperimentalDecorators == true, node))
        {
            return;
        }
        // (emit helpers; markLinkedReferences: emit only)
        foreach (var modifier in store.Nodes(modifiers))
        {
            if (store.Kind(modifier) == SyntaxKind.Decorator)
            {
                CheckDecorator(At(node, modifier));
            }
        }
    }

    // ---- type nodes ----

    // checker.ts checkTypeReferenceNode
    private void CheckTypeReferenceNode(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var typeArguments = store.TypeArguments(n);
        CheckGrammarTypeArguments(node, typeArguments);
        if (store.Kind(n) == SyntaxKind.TypeReference && !Utilities.IsInJSFile(store, n) && (store.Flags(n) & NodeFlags.JSDoc) == 0 && !typeArguments.IsNone &&
            store.End(store.TypeName(n)) != store.Pos(typeArguments))
        {
            // If there was a token between the type name and the type arguments, check if it was a DotToken
            var text = store.SourceFileText(FileAt(node.File).Root).Chars;
            var dotPos = Scanner.SkipTrivia(text, store.End(store.TypeName(n)));
            if (dotPos < text.Length && text[dotPos] == '.')
            {
                GrammarErrorAtPos(node, dotPos, 1, Diagnostics.JSDoc_types_can_only_be_used_inside_documentation_comments);
            }
        }
        CheckSourceElements(node, typeArguments);
        CheckTypeReferenceOrImport(node);
    }

    // checker.ts checkTypeReferenceOrImport (the constraint check is a lazy diagnostic)
    private void CheckTypeReferenceOrImport(NodeRef node)
    {
        var type = GetTypeFromTypeNode(node);
        if (IsErrorType(type))
        {
            return;
        }
        if (!StoreOf(node).TypeArguments(node.Node).IsNone)
        {
            var typeParameters = GetTypeParametersForTypeReferenceOrImport(node);
            if (typeParameters is not null)
            {
                CheckTypeArgumentConstraints(node, typeParameters);
            }
        }
        var symbol = SymbolNodeLinks.Has(node) ? SymbolNodeLinks.Get(node).ResolvedSymbol : SymbolRef.None;
        if (!symbol.IsNone && DeclarationsOf(symbol) is { } declarations &&
            declarations.Exists(d => IsTypeDeclaration(d) && (StoreOf(d).Flags(d.Node) & NodeFlags.Deprecated) != 0))
        {
            AddDeprecatedSuggestion(GetDeprecatedSuggestionNode(node), declarations, NameOf(symbol));
        }
    }

    // checker.ts checkTypePredicate
    private void CheckTypePredicate(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var parentNode = GetTypePredicateParent(store, n);
        if (parentNode.IsNone)
        {
            // The parent must not be valid.
            Error(node, Diagnostics.A_type_predicate_is_only_allowed_in_return_type_position_for_functions_and_methods);
            return;
        }
        var parent = At(node, parentNode);
        var signature = GetSignatureFromDeclaration(parent);
        var typePredicate = GetTypePredicateOfSignature(signature);
        if (typePredicate is null)
        {
            return;
        }
        CheckSourceElement(At(node, store.Type(n)));
        var parameterName = At(node, store.ParameterName(n));
        if (typePredicate.Kind is not (TypePredicateKind.This or TypePredicateKind.AssertsThis))
        {
            if (typePredicate.ParameterIndex >= 0)
            {
                var parameters = ParametersOf(signature);
                if (SignatureHasRestParameter(signature) && typePredicate.ParameterIndex == parameters.Length - 1)
                {
                    Error(parameterName, Diagnostics.A_type_predicate_cannot_reference_a_rest_parameter);
                }
                else if (!typePredicate.Type.IsNone)
                {
                    DiagnosticMessageChain LeadingError() => ChainDiagnosticMessages(null, Diagnostics.A_type_predicate_s_type_must_be_assignable_to_its_parameter_s_type);
                    CheckTypeAssignableTo(typePredicate.Type, GetTypeOfSymbol(parameters[typePredicate.ParameterIndex]), At(node, store.Type(n)), null, LeadingError);
                }
            }
            else if (!parameterName.IsNone)
            {
                var hasReportedError = false;
                foreach (var parameter in store.Nodes(store.Parameters(parentNode)))
                {
                    var name = store.Name(parameter);
                    if (Utilities.IsBindingPattern(store, name) && CheckIfTypePredicateVariableIsDeclaredInBindingPattern(At(node, name), parameterName, typePredicate.ParameterName!))
                    {
                        hasReportedError = true;
                        break;
                    }
                }
                if (!hasReportedError)
                {
                    Error(parameterName, Diagnostics.Cannot_find_parameter_0, typePredicate.ParameterName);
                }
            }
        }
    }

    // checker.ts getTypePredicateParent
    private static Node GetTypePredicateParent(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        switch (store.Kind(parent))
        {
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.CallSignature:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionType:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
                if (node == store.Type(parent))
                {
                    return parent;
                }
                break;
        }
        return Node.None;
    }

    // checker.ts checkIfTypePredicateVariableIsDeclaredInBindingPattern
    private bool CheckIfTypePredicateVariableIsDeclaredInBindingPattern(NodeRef pattern, NodeRef predicateVariableNode, string predicateVariableName)
    {
        var store = StoreOf(pattern);
        foreach (var element in store.Nodes(store.Elements(pattern.Node)))
        {
            if (store.Kind(element) == SyntaxKind.OmittedExpression)
            {
                continue;
            }
            var name = store.Name(element);
            if (store.Kind(name) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(name), predicateVariableName))
            {
                Error(predicateVariableNode, Diagnostics.A_type_predicate_cannot_reference_element_0_in_a_binding_pattern, predicateVariableName);
                return true;
            }
            if (store.Kind(name) is SyntaxKind.ArrayBindingPattern or SyntaxKind.ObjectBindingPattern &&
                CheckIfTypePredicateVariableIsDeclaredInBindingPattern(At(pattern, name), predicateVariableNode, predicateVariableName))
            {
                return true;
            }
        }
        return false;
    }

    // checker.ts checkTypeQuery
    private void CheckTypeQuery(NodeRef node) => GetTypeFromTypeQueryNode(node);

    // checker.ts checkTypeLiteral
    private void CheckTypeLiteral(NodeRef node)
    {
        CheckSourceElements(node, StoreOf(node).Members(node.Node));
        // checkTypeLiteralDiagnostics
        var type = GetTypeFromTypeLiteralOrFunctionOrConstructorTypeNode(node);
        CheckIndexConstraints(type, Types.Symbol(type));
        CheckTypeForDuplicateIndexSignatures(node);
        CheckObjectTypeForDuplicateDeclarations(node);
    }

    // checker.ts checkTupleType
    private void CheckTupleType(NodeRef node)
    {
        var store = StoreOf(node);
        var seenOptionalElement = false;
        var seenRestElement = false;
        foreach (var element in store.Nodes(store.Elements(node.Node)))
        {
            var e = At(node, element);
            var flags = GetTupleElementFlags(e);
            if ((flags & ElementFlags.Variadic) != 0)
            {
                var type = GetTypeFromTypeNode(At(node, store.Type(element)));
                if (!IsArrayLikeType(type))
                {
                    Error(e, Diagnostics.A_rest_element_type_must_be_an_array_type);
                    break;
                }
                if (IsArrayType(type) || (IsTupleType(type) && (TupleTarget(type).CombinedFlags & ElementFlags.Rest) != 0))
                {
                    flags |= ElementFlags.Rest;
                }
            }
            if ((flags & ElementFlags.Rest) != 0)
            {
                if (seenRestElement)
                {
                    GrammarErrorOnNode(e, Diagnostics.A_rest_element_cannot_follow_another_rest_element);
                    break;
                }
                seenRestElement = true;
            }
            else if ((flags & ElementFlags.Optional) != 0)
            {
                if (seenRestElement)
                {
                    GrammarErrorOnNode(e, Diagnostics.An_optional_element_cannot_follow_a_rest_element);
                    break;
                }
                seenOptionalElement = true;
            }
            else if ((flags & ElementFlags.Required) != 0 && seenOptionalElement)
            {
                GrammarErrorOnNode(e, Diagnostics.A_required_element_cannot_follow_an_optional_element);
                break;
            }
        }
        CheckSourceElements(node, store.Elements(node.Node));
        GetTypeFromTypeNode(node);
    }

    // checker.ts checkInferType
    private void CheckInferType(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (UtilitiesPublic.FindAncestor(store, n, a => !store.Parent(a).IsNone && store.Kind(store.Parent(a)) == SyntaxKind.ConditionalType && store.ExtendsType(store.Parent(a)) == a).IsNone)
        {
            GrammarErrorOnNode(node, Diagnostics.infer_declarations_are_only_permitted_in_the_extends_clause_of_a_conditional_type);
        }
        var typeParameterNode = At(node, store.TypeParameter(n));
        CheckSourceElement(typeParameterNode);
        var symbol = GetSymbolOfDeclaration(typeParameterNode);
        if (DeclarationsOf(symbol) is { Count: > 1 } && !DeclaredTypeLinks.Get(symbol).TypeParametersChecked)
        {
            DeclaredTypeLinks.Get(symbol).TypeParametersChecked = true;
            var typeParameter = GetDeclaredTypeOfTypeParameter(symbol);
            var declarations = new List<NodeRef>();
            foreach (var d in DeclarationsOf(symbol)!)
            {
                if (Kind(d) == SyntaxKind.TypeParameter)
                {
                    declarations.Add(d);
                }
            }
            if (!AreTypeParametersIdentical(declarations, [typeParameter], d => [d.Node]))
            {
                // Report an error on every conflicting declaration.
                var name = SymbolToString(symbol);
                foreach (var declaration in declarations)
                {
                    Error(At(declaration, StoreOf(declaration).Name(declaration.Node)), Diagnostics.All_declarations_of_0_must_have_identical_constraints, name);
                }
            }
        }
        RegisterForUnusedIdentifiersCheck(node);
    }

    // checker.ts checkTemplateLiteralType
    private void CheckTemplateLiteralType(NodeRef node)
    {
        var store = StoreOf(node);
        foreach (var span in store.Nodes(store.TemplateSpans(node.Node)))
        {
            var spanType = At(node, store.Type(span));
            CheckSourceElement(spanType);
            var type = GetTypeFromTypeNode(spanType);
            CheckTypeAssignableTo(type, templateConstraintType, spanType);
        }
        GetTypeFromTypeNode(node);
    }

    // checker.ts checkImportType
    private void CheckImportType(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        CheckSourceElement(At(node, store.Argument(n)));
        var attributes = store.Attributes(n);
        if (!attributes.IsNone)
        {
            if (store.Token(attributes) != SyntaxKind.WithKeyword) // (ignoreDeprecations: not set in Gate C)
            {
                GrammarErrorOnFirstToken(At(node, attributes), Diagnostics.Import_assertions_have_been_replaced_by_import_attributes_Use_with_instead_of_assert);
            }
            GetResolutionModeOverride(At(node, attributes), reportErrors: true);
        }
        // the module reference is resolved in getTypeFromImportTypeNode (module C3)
        CheckImportTypeModule(node);
        CheckTypeReferenceOrImport(node);
    }

    // checker.ts getResolutionModeOverride: whether the attributes set a valid resolution mode (and the grammar errors)
    private bool GetResolutionModeOverride(NodeRef node, bool reportErrors)
    {
        var store = StoreOf(node);
        var elements = store.Nodes(store.Elements(node.Node));
        if (elements.Length != 1)
        {
            if (reportErrors)
            {
                GrammarErrorOnNode(node, store.Token(node.Node) == SyntaxKind.WithKeyword
                    ? Diagnostics.Type_import_attributes_should_have_exactly_one_key_resolution_mode_with_value_import_or_require
                    : Diagnostics.Type_import_assertions_should_have_exactly_one_key_resolution_mode_with_value_import_or_require);
            }
            return false;
        }
        var elem = elements[0];
        var name = store.Name(elem);
        if (!Utilities.IsStringLiteralLike(store, name))
        {
            return false;
        }
        if (!store.Texts.Equals(store.Text(name), "resolution-mode"))
        {
            if (reportErrors)
            {
                GrammarErrorOnNode(At(node, name), store.Token(node.Node) == SyntaxKind.WithKeyword
                    ? Diagnostics.resolution_mode_is_the_only_valid_key_for_type_import_attributes
                    : Diagnostics.resolution_mode_is_the_only_valid_key_for_type_import_assertions);
            }
            return false;
        }
        var value = store.Value(elem);
        if (!Utilities.IsStringLiteralLike(store, value))
        {
            return false;
        }
        if (!store.Texts.Equals(store.Text(value), "import") && !store.Texts.Equals(store.Text(value), "require"))
        {
            if (reportErrors)
            {
                GrammarErrorOnNode(At(node, value), Diagnostics.resolution_mode_should_be_either_require_or_import);
            }
            return false;
        }
        return true;
    }

    // checker.ts checkNamedTupleMember
    private void CheckNamedTupleMember(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (!store.DotDotDotToken(n).IsNone && !store.QuestionToken(n).IsNone)
        {
            GrammarErrorOnNode(node, Diagnostics.A_tuple_member_cannot_be_both_optional_and_rest);
        }
        var type = store.Type(n);
        if (store.Kind(type) == SyntaxKind.OptionalType)
        {
            GrammarErrorOnNode(At(node, type), Diagnostics.A_labeled_tuple_element_is_declared_as_optional_with_a_question_mark_after_the_name_and_before_the_colon_rather_than_after_the_type);
        }
        if (store.Kind(type) == SyntaxKind.RestType)
        {
            GrammarErrorOnNode(At(node, type), Diagnostics.A_labeled_tuple_element_is_declared_as_rest_with_a_before_the_name_rather_than_before_the_type);
        }
        CheckSourceElement(At(node, type));
        GetTypeFromTypeNode(node);
    }

    // checker.ts checkMappedType
    private void CheckMappedType(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // checkGrammarMappedType
        var members = store.Members(n);
        if (!members.IsNone && store.Length(members) > 0)
        {
            GrammarErrorOnNode(At(node, store.Nodes(members)[0]), Diagnostics.A_mapped_type_may_not_declare_properties_or_methods);
        }
        CheckSourceElement(At(node, store.TypeParameter(n)));
        CheckSourceElement(At(node, store.NameType(n)));
        CheckSourceElement(At(node, store.Type(n)));
        if (store.Type(n).IsNone)
        {
            ReportImplicitAny(node, anyType);
        }
        var type = GetTypeFromMappedTypeNode(node);
        var nameType = GetNameTypeFromMappedType(type);
        if (!nameType.IsNone)
        {
            CheckTypeAssignableTo(nameType, stringNumberSymbolType, At(node, store.NameType(n)));
        }
        else
        {
            var constraintType = GetConstraintTypeFromMappedType(type);
            CheckTypeAssignableTo(constraintType, stringNumberSymbolType, At(node, store.Constraint(store.TypeParameter(n))));
        }
    }

    // checker.ts checkTypeAliasDeclaration
    private void CheckTypeAliasDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar checking
        CheckGrammarModifiers(node);
        CheckTypeNameIsReserved(At(node, store.Name(n)), Diagnostics.Type_alias_name_cannot_be_0);
        if (!AllowBlockDeclarations(store, store.Parent(n)))
        {
            GrammarErrorOnNode(node, Diagnostics._0_declarations_can_only_be_declared_inside_a_block, "type");
        }
        CheckExportsOnMergedDeclarations(node);
        CheckTypeParameters(node, store.TypeParameters(n));
        var type = store.Type(n);
        if (store.Kind(type) == SyntaxKind.IntrinsicKeyword)
        {
            var typeParameterCount = store.TypeParameters(n).IsNone ? 0 : store.Length(store.TypeParameters(n));
            var nameText = store.Texts.GetString(store.EscapedText(store.Name(n)))!;
            var valid = typeParameterCount == 0 ? nameText == "BuiltinIteratorReturn" : typeParameterCount == 1 && GetIntrinsicTypeKind(nameText) != IntrinsicTypeKind.None;
            if (!valid)
            {
                Error(At(node, type), Diagnostics.The_intrinsic_keyword_can_only_be_used_to_declare_compiler_provided_intrinsic_types);
            }
        }
        else
        {
            CheckSourceElement(At(node, type));
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    // checker.ts allowBlockDeclarations
    private static bool AllowBlockDeclarations(NodeStore store, Node parent)
    {
        switch (store.Kind(parent))
        {
            case SyntaxKind.IfStatement:
            case SyntaxKind.DoStatement:
            case SyntaxKind.WhileStatement:
            case SyntaxKind.WithStatement:
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
                return false;
            case SyntaxKind.LabeledStatement:
                return AllowBlockDeclarations(store, store.Parent(parent));
        }
        return true;
    }

    // checker.ts checkObjectTypeForDuplicateDeclarations (type literals and interfaces)
    private void CheckObjectTypeForDuplicateDeclarations(NodeRef node)
    {
        var store = StoreOf(node);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in store.Nodes(store.Members(node.Node)))
        {
            if (store.Kind(member) != SyntaxKind.PropertySignature)
            {
                continue;
            }
            var name = store.Name(member);
            string memberName;
            switch (store.Kind(name))
            {
                case SyntaxKind.StringLiteral:
                case SyntaxKind.NumericLiteral:
                    memberName = store.Texts.GetString(store.Text(name))!;
                    break;
                case SyntaxKind.Identifier:
                    memberName = store.Texts.GetString(Utilities.IdText(store, name))!;
                    break;
                default:
                    continue;
            }
            if (!names.Add(memberName))
            {
                var valueDeclaration = ValueDeclarationOf(NodeSymbol(At(node, member)));
                Error(valueDeclaration.IsNone ? NodeRef.None : At(valueDeclaration, Utilities.GetNameOfDeclaration(StoreOf(valueDeclaration), valueDeclaration.Node)), Diagnostics.Duplicate_identifier_0, memberName);
                Error(At(node, name), Diagnostics.Duplicate_identifier_0, memberName);
            }
        }
    }

    // checker.ts checkTypeForDuplicateIndexSignatures
    private void CheckTypeForDuplicateIndexSignatures(NodeRef node)
    {
        var store = StoreOf(node);
        var nodeSymbol = GetSymbolOfDeclaration(node);
        if (store.Kind(node.Node) == SyntaxKind.InterfaceDeclaration)
        {
            // in case of merging interface declaration it is possible that we'll enter this check procedure several times for every declaration
            // to prevent this run check only for the first declaration of a given kind
            if (DeclarationsOf(nodeSymbol) is { Count: > 0 } declarations && declarations[0] != node)
            {
                return;
            }
        }
        // TypeScript 1.0 spec (April 2014)
        // 3.7.4: An object type can contain at most one string index signature and one numeric index signature.
        // 8.5: A class declaration can have at most one string index member declaration and one numeric index member declaration
        // getIndexSymbol: symbol.members ? getIndexSymbolFromSymbolTable(getMembersOfSymbol(symbol)) : undefined
        var indexSymbol = nodeSymbol.IsNone || MembersOf(nodeSymbol) is null ? SymbolRef.None : GetMembersOfSymbol(nodeSymbol).Get(InternalSymbolName.Index);
        if (indexSymbol.IsNone || DeclarationsOf(indexSymbol) is not { } indexDeclarations)
        {
            return;
        }
        var indexSignatureMap = new Dictionary<Type, List<NodeRef>>();
        var order = new List<Type>();
        foreach (var declaration in indexDeclarations)
        {
            if (Kind(declaration) != SyntaxKind.IndexSignature)
            {
                continue;
            }
            var dstore = StoreOf(declaration);
            var parameters = dstore.Nodes(dstore.Parameters(declaration.Node));
            if (parameters.Length == 1 && !dstore.Type(parameters[0]).IsNone)
            {
                ForEachType(GetTypeFromTypeNode(At(declaration, dstore.Type(parameters[0]))), type =>
                {
                    if (!indexSignatureMap.TryGetValue(type, out var list))
                    {
                        indexSignatureMap[type] = list = [];
                        order.Add(type);
                    }
                    list.Add(declaration);
                });
            }
            // Do nothing for late-bound index signatures: allow these to duplicate one another and explicit indexes
        }
        foreach (var type in order)
        {
            var list = indexSignatureMap[type];
            if (list.Count > 1)
            {
                foreach (var declaration in list)
                {
                    Error(declaration, Diagnostics.Duplicate_index_signature_for_type_0, TypeToString(type));
                }
            }
        }
    }

    // checker.ts checkTypeParameterListsIdentical: type parameter lists are identical across multiple declarations
    private void CheckTypeParameterListsIdentical(SymbolRef symbol)
    {
        if (DeclarationsOf(symbol) is { Count: 1 })
        {
            return;
        }
        ref var links = ref DeclaredTypeLinks.Get(symbol);
        if (links.TypeParametersChecked)
        {
            return;
        }
        links.TypeParametersChecked = true;
        var declarations = new List<NodeRef>();
        foreach (var d in DeclarationsOf(symbol) ?? [])
        {
            if (Kind(d) is SyntaxKind.ClassDeclaration or SyntaxKind.InterfaceDeclaration)
            {
                declarations.Add(d);
            }
        }
        if (declarations.Count <= 1)
        {
            return;
        }
        var type = GetDeclaredTypeOfSymbol(symbol);
        var localTypeParameters = Types.Elements(Types.Object(type).LocalTypeParameters).ToArray();
        if (!AreTypeParametersIdentical(declarations, localTypeParameters, d =>
            {
                var tps = StoreOf(d).TypeParameters(d.Node);
                return tps.IsNone ? [] : StoreOf(d).Nodes(tps).ToArray();
            }))
        {
            // Report an error on every conflicting declaration.
            var name = SymbolToString(symbol);
            foreach (var declaration in declarations)
            {
                Error(At(declaration, StoreOf(declaration).Name(declaration.Node)), Diagnostics.All_declarations_of_0_must_have_identical_type_parameters, name);
            }
        }
    }

    // checker.ts areTypeParametersIdentical
    private bool AreTypeParametersIdentical(List<NodeRef> declarations, Type[] targetParameters, Func<NodeRef, Node[]> getTypeParameterDeclarations)
    {
        var maxTypeArgumentCount = targetParameters.Length;
        var minTypeArgumentCount = GetMinTypeArgumentCount(targetParameters);
        foreach (var declaration in declarations)
        {
            var store = StoreOf(declaration);
            // If this declaration has too few or too many type parameters, we report an error
            var sourceParameters = getTypeParameterDeclarations(declaration);
            var numTypeParameters = sourceParameters.Length;
            if (numTypeParameters < minTypeArgumentCount || numTypeParameters > maxTypeArgumentCount)
            {
                return false;
            }
            for (var i = 0; i < numTypeParameters; i++)
            {
                var source = sourceParameters[i];
                var target = targetParameters[i];
                // If the type parameter node does not have the same as the resolved type
                // parameter at this position, we report an error.
                if (!store.Texts.Equals(store.EscapedText(store.Name(source)), NameOf(Types.Symbol(target))))
                {
                    return false;
                }
                // If the type parameter node does not have an identical constraint as the resolved
                // type parameter at this position, we report an error.
                var constraint = store.Constraint(source);
                var sourceConstraint = constraint.IsNone ? Type.None : GetTypeFromTypeNode(At(declaration, constraint));
                var targetConstraint = GetConstraintOfTypeParameter(target);
                // relax check if later interface augmentation has no constraint, it's more broad and is OK to merge with
                // a more constrained interface (this could be generalized to a full hierarchy check, but that's maybe overkill)
                if (!sourceConstraint.IsNone && !targetConstraint.IsNone && !IsTypeIdenticalTo(sourceConstraint, targetConstraint))
                {
                    return false;
                }
                // If the type parameter node has a default and it is not identical to the default
                // for the type parameter at this position, we report an error.
                var defaultNode = store.Default(source);
                var sourceDefault = defaultNode.IsNone ? Type.None : GetTypeFromTypeNode(At(declaration, defaultNode));
                var targetDefault = GetDefaultFromTypeParameter(target);
                if (!sourceDefault.IsNone && !targetDefault.IsNone && !IsTypeIdenticalTo(sourceDefault, targetDefault))
                {
                    return false;
                }
            }
        }
        return true;
    }

    // ---- exports and collisions ----

    [Flags]
    private enum DeclarationSpaces
    {
        None = 0,
        ExportValue = 1 << 0,
        ExportType = 1 << 1,
        ExportNamespace = 1 << 2,
    }

    // checker.ts checkExportsOnMergedDeclarations (a lazy diagnostic)
    private void CheckExportsOnMergedDeclarations(NodeRef node)
    {
        // if localSymbol is defined on node then node itself is exported - check is required
        var symbol = LocalSymbolOfDeclaration(node);
        if (symbol.IsNone)
        {
            // local symbol is undefined => this declaration is non-exported.
            // however symbol might contain other declarations that are exported
            symbol = GetSymbolOfDeclaration(node);
            if (symbol.IsNone || ExportSymbolOf(symbol).IsNone)
            {
                // this is a pure local symbol (all declarations are non-exported) - no need to check anything
                return;
            }
        }
        // run the check only for the first declaration in the list
        if (GetDeclarationOfKind(symbol, Kind(node)) != node)
        {
            return;
        }
        var exportedDeclarationSpaces = DeclarationSpaces.None;
        var nonExportedDeclarationSpaces = DeclarationSpaces.None;
        var defaultExportedDeclarationSpaces = DeclarationSpaces.None;
        var declarations = DeclarationsOf(symbol) ?? [];
        foreach (var d in declarations)
        {
            var declarationSpaces = GetDeclarationSpaces(d);
            var effectiveDeclarationFlags = GetEffectiveDeclarationFlags(d, ModifierFlags.Export | ModifierFlags.Default);
            if ((effectiveDeclarationFlags & ModifierFlags.Export) != 0)
            {
                if ((effectiveDeclarationFlags & ModifierFlags.Default) != 0)
                {
                    defaultExportedDeclarationSpaces |= declarationSpaces;
                }
                else
                {
                    exportedDeclarationSpaces |= declarationSpaces;
                }
            }
            else
            {
                nonExportedDeclarationSpaces |= declarationSpaces;
            }
        }
        // Spaces for anything not declared a 'default export'.
        var nonDefaultExportedDeclarationSpaces = exportedDeclarationSpaces | nonExportedDeclarationSpaces;
        var commonDeclarationSpacesForExportsAndLocals = exportedDeclarationSpaces & nonExportedDeclarationSpaces;
        var commonDeclarationSpacesForDefaultAndNonDefault = defaultExportedDeclarationSpaces & nonDefaultExportedDeclarationSpaces;
        if (commonDeclarationSpacesForExportsAndLocals != 0 || commonDeclarationSpacesForDefaultAndNonDefault != 0)
        {
            // declaration spaces for exported and non-exported declarations intersect
            foreach (var d in declarations)
            {
                var declarationSpaces = GetDeclarationSpaces(d);
                var dstore = StoreOf(d);
                var name = At(d, Utilities.GetNameOfDeclaration(dstore, d.Node));
                // Only error on the declarations that contributed to the intersecting spaces.
                if ((declarationSpaces & commonDeclarationSpacesForDefaultAndNonDefault) != 0)
                {
                    Error(name, Diagnostics.Merged_declaration_0_cannot_include_a_default_export_declaration_Consider_adding_a_separate_export_default_0_declaration_instead, Utilities.DeclarationNameToString(dstore, name.Node));
                }
                else if ((declarationSpaces & commonDeclarationSpacesForExportsAndLocals) != 0)
                {
                    Error(name, Diagnostics.Individual_declarations_in_merged_declaration_0_must_be_all_exported_or_all_local, Utilities.DeclarationNameToString(dstore, name.Node));
                }
            }
        }

        DeclarationSpaces GetDeclarationSpaces(NodeRef decl)
        {
            var dstore = StoreOf(decl);
            switch (dstore.Kind(decl.Node))
            {
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.TypeAliasDeclaration:
                // A jsdoc typedef and callback are, by definition, type aliases.
                case SyntaxKind.JSDocTypedefTag:
                case SyntaxKind.JSDocCallbackTag:
                case SyntaxKind.JSDocEnumTag:
                    return DeclarationSpaces.ExportType;
                case SyntaxKind.ModuleDeclaration:
                    return Utilities.IsAmbientModule(dstore, decl.Node) || Utilities.GetModuleInstanceState(dstore, decl.Node) != ModuleInstanceState.NonInstantiated
                        ? DeclarationSpaces.ExportNamespace | DeclarationSpaces.ExportValue
                        : DeclarationSpaces.ExportNamespace;
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.EnumMember:
                    return DeclarationSpaces.ExportType | DeclarationSpaces.ExportValue;
                case SyntaxKind.SourceFile:
                    return DeclarationSpaces.ExportType | DeclarationSpaces.ExportValue | DeclarationSpaces.ExportNamespace;
                case SyntaxKind.ExportAssignment:
                case SyntaxKind.BinaryExpression:
                {
                    var expression = dstore.Kind(decl.Node) == SyntaxKind.ExportAssignment ? dstore.Expression(decl.Node) : dstore.Right(decl.Node);
                    // Export assigned entity name expressions act as aliases and should fall through, otherwise they export values
                    if (!Utilities.IsEntityNameExpression(dstore, expression))
                    {
                        return DeclarationSpaces.ExportValue;
                    }
                    return AliasDeclarationSpaces(decl);
                }
                // The below options all declare an Alias, which is allowed to merge with other values within the importing module.
                case SyntaxKind.ImportEqualsDeclaration:
                case SyntaxKind.NamespaceImport:
                case SyntaxKind.ImportClause:
                    return AliasDeclarationSpaces(decl);
                case SyntaxKind.VariableDeclaration:
                case SyntaxKind.BindingElement:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.ImportSpecifier: // https://github.com/Microsoft/TypeScript/pull/7591
                case SyntaxKind.Identifier: // https://github.com/microsoft/TypeScript/issues/36098
                    // Identifiers are used as declarations of assignment declarations whose parents may be
                    // SyntaxKind.CallExpression - `Object.defineProperty(thing, "aField", {value: 42});`
                    // SyntaxKind.ElementAccessExpression - `thing["aField"] = 42;` or `thing["aField"];` (with a doc comment on it)
                    // or SyntaxKind.PropertyAccessExpression - `thing.aField = 42;`
                    // all of which are pretty much always values, or at least imply a value meaning.
                    // It may be apprpriate to treat these as aliases in the future.
                    return DeclarationSpaces.ExportValue;
                case SyntaxKind.MethodSignature:
                case SyntaxKind.PropertySignature:
                    return DeclarationSpaces.ExportType;
                default:
                    return DeclarationSpaces.ExportValue; // (Debug.failBadSyntaxKind)
            }
        }

        DeclarationSpaces AliasDeclarationSpaces(NodeRef decl)
        {
            var result = DeclarationSpaces.None;
            var target = ResolveAlias(GetSymbolOfDeclaration(decl));
            foreach (var d in DeclarationsOf(target) ?? [])
            {
                result |= GetDeclarationSpaces(d);
            }
            return result;
        }
    }

    // checker.ts checkCollisionsForDeclarationName
    private void CheckCollisionsForDeclarationName(NodeRef node, NodeRef name)
    {
        if (name.IsNone || Kind(name) != SyntaxKind.Identifier)
        {
            return;
        }
        var store = StoreOf(node);
        CheckCollisionWithRequireExportsInGeneratedCode(node, name);
        // (checkCollisionWithGlobalPromiseInGeneratedCode: only prior to ES2017; the WeakMap/WeakSet and Reflect
        // collisions: only up to ES2021 — Gate C targets ES2025)
        if (Utilities.IsClassLike(store, node.Node))
        {
            CheckTypeNameIsReserved(name, Diagnostics.Class_name_cannot_be_0);
            if ((store.Flags(node.Node) & NodeFlags.Ambient) == 0)
            {
                // checkClassNameCollisionWithObject
                if (store.Texts.Equals(store.EscapedText(name.Node), "Object") && GetEmitModuleFormatOfFile(node.File) < ModuleKind.ES2015)
                {
                    Error(name, Diagnostics.Class_name_cannot_be_Object_when_targeting_ES5_and_above_with_module_0, moduleKind.ToString()); // https://github.com/Microsoft/TypeScript/issues/17494
                }
            }
        }
        else if (store.Kind(node.Node) == SyntaxKind.EnumDeclaration)
        {
            CheckTypeNameIsReserved(name, Diagnostics.Enum_name_cannot_be_0);
        }
    }

    // program.ts getEmitModuleFormatOfFile: the implied format of the file, else the module option
    private ModuleKind GetEmitModuleFormatOfFile(int file) => GetImpliedNodeFormatForEmit(file) ?? moduleKind;

    // checker.ts checkCollisionWithRequireExportsInGeneratedCode
    private void CheckCollisionWithRequireExportsInGeneratedCode(NodeRef node, NodeRef name)
    {
        // No need to check for require or exports for ES6 modules and later
        if (GetEmitModuleFormatOfFile(node.File) >= ModuleKind.ES2015)
        {
            return;
        }
        if (!NeedCollisionCheckForIdentifier(node, name, "require") && !NeedCollisionCheckForIdentifier(node, name, "exports"))
        {
            return;
        }
        var store = StoreOf(node);
        // Uninstantiated modules shouldnt do this check
        if (store.Kind(node.Node) == SyntaxKind.ModuleDeclaration && Utilities.GetModuleInstanceState(store, node.Node) != ModuleInstanceState.Instantiated)
        {
            return;
        }
        // In case of variable declaration, node.parent is variable statement so look at the variable statement's parent
        var parent = GetDeclarationContainer(node);
        if (Kind(parent) == SyntaxKind.SourceFile && Utilities.IsExternalOrCommonJsModule(store, parent.Node, BindingOf(parent.File)))
        {
            // If the declaration happens to be in external module, report error that require and exports are reserved keywords
            Error(name, Diagnostics.Duplicate_identifier_0_Compiler_reserves_name_1_in_top_level_scope_of_a_module,
                Utilities.DeclarationNameToString(store, name.Node), Utilities.DeclarationNameToString(store, name.Node));
        }
    }

    // checker.ts needCollisionCheckForIdentifier
    private bool NeedCollisionCheckForIdentifier(NodeRef node, NodeRef identifier, string name)
    {
        var store = StoreOf(node);
        if (identifier.IsNone || !store.Texts.Equals(store.EscapedText(identifier.Node), name))
        {
            return false;
        }
        if (store.Kind(node.Node) is SyntaxKind.PropertyDeclaration or SyntaxKind.PropertySignature or SyntaxKind.MethodDeclaration or SyntaxKind.MethodSignature
            or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor or SyntaxKind.PropertyAssignment)
        {
            // it is ok to have member named '_super', '_i' or '_this' - member access is always qualified
            return false;
        }
        if ((store.Flags(node.Node) & NodeFlags.Ambient) != 0)
        {
            // ambient context - no codegen impact
            return false;
        }
        if (store.Kind(node.Node) is SyntaxKind.ImportClause or SyntaxKind.ImportEqualsDeclaration or SyntaxKind.ImportSpecifier)
        {
            // type-only imports do not require collision checks against runtime values.
            if (IsTypeOnlyImportOrExportDeclaration(node))
            {
                return false;
            }
        }
        var root = Utilities.GetRootDeclaration(store, node.Node);
        if (store.Kind(root) == SyntaxKind.Parameter && Utilities.NodeIsMissing(store, store.Body(store.Parent(root))))
        {
            // just an overload - no codegen impact
            return false;
        }
        return true;
    }
}
