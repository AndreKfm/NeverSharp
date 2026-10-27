// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (classes and inheritance, interfaces, enums and the enum member evaluator,
// modules and checkExternalModuleExports; each function names its checker.ts counterpart)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C8 (replaces C4's checkClassLikeDeclaration relation checks and
// C1's override and property initialization checks). Lazy diagnostics run where they are added.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private readonly HashSet<SymbolRef> exportsCheckedModules = [];
    private readonly HashSet<NodeRef> enumValuesComputed = [];
    private readonly Dictionary<NodeRef, object?> enumMemberValues = [];

    // checker.ts checkClassDeclaration
    private void CheckClassDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var modifiers = store.Modifiers(n);
        var firstDecorator = Node.None;
        if (!modifiers.IsNone)
        {
            foreach (var modifier in store.Nodes(modifiers))
            {
                if (store.Kind(modifier) == SyntaxKind.Decorator)
                {
                    firstDecorator = modifier;
                    break;
                }
            }
        }
        var hasStaticPrivateMember = false;
        foreach (var p in store.Nodes(store.Members(n)))
        {
            hasStaticPrivateMember |= Utilities.HasSyntacticModifierUncached(store, p, ModifierFlags.Static) && IsPrivateIdentifierClassElementDeclaration(store, p);
        }
        if (compilerOptions.ExperimentalDecorators == true && !firstDecorator.IsNone && hasStaticPrivateMember)
        {
            GrammarErrorOnNode(At(node, firstDecorator), Diagnostics.Class_decorators_can_t_be_used_with_static_private_identifier_Consider_removing_the_experimental_decorator);
        }
        if (store.Name(n).IsNone && !Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Default))
        {
            GrammarErrorOnFirstToken(node, Diagnostics.A_class_declaration_without_the_default_modifier_must_have_a_name);
        }
        CheckClassLikeDeclaration(node);
        CheckSourceElements(node, store.Members(n));
        RegisterForUnusedIdentifiersCheck(node);
    }

    /// <summary>checker.ts checkClassExpression.</summary>
    internal Type CheckClassExpression(NodeRef node)
    {
        if (checkedClassExpressions.Add(node))
        {
            CheckClassLikeDeclaration(node);
        }
        CheckNodeDeferred(node);
        return GetTypeOfSymbol(GetSymbolOfDeclaration(node));
    }

    // checker.ts checkClassLikeDeclaration
    private void CheckClassLikeDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        CheckGrammarClassLikeDeclaration(node);
        CheckDecorators(node);
        var nameNode = store.Name(n);
        CheckCollisionsForDeclarationName(node, At(node, nameNode));
        CheckTypeParameters(node, store.TypeParameters(n));
        CheckExportsOnMergedDeclarations(node);
        var symbol = GetSymbolOfDeclaration(node);
        var type = GetDeclaredTypeOfSymbol(symbol);
        var typeWithThis = GetTypeWithThisArgument(type);
        var staticType = GetTypeOfSymbol(symbol);
        CheckTypeParameterListsIdentical(symbol);
        CheckFunctionOrConstructorSymbol(symbol);
        CheckClassForDuplicateDeclarations(node);
        // Only check for reserved static identifiers on non-ambient context.
        var nodeInAmbientContext = (store.Flags(n) & NodeFlags.Ambient) != 0;
        if (!nodeInAmbientContext)
        {
            CheckClassForStaticPropertyNameConflicts(node);
        }
        var nameOrNode = !nameNode.IsNone ? At(node, nameNode) : node;
        var baseTypeNodeRaw = GetClassExtendsHeritageElement(store, n);
        if (!baseTypeNodeRaw.IsNone)
        {
            var baseTypeNode = At(node, baseTypeNodeRaw);
            CheckSourceElements(node, store.TypeArguments(baseTypeNodeRaw));
            var baseTypes = GetBaseTypes(type);
            if (baseTypes.Count > 0)
            {
                var baseType = baseTypes[0];
                var baseConstructorType = GetBaseConstructorTypeOfClass(type);
                var staticBaseType = GetApparentType(baseConstructorType);
                CheckBaseTypeAccessibility(staticBaseType, baseTypeNode);
                CheckExpression(At(node, store.Expression(baseTypeNodeRaw))); // checkSourceElement(baseTypeNode.expression)
                var typeArguments = store.TypeArguments(baseTypeNodeRaw);
                if (!typeArguments.IsNone && store.Length(typeArguments) > 0)
                {
                    CheckSourceElements(node, typeArguments);
                    foreach (var constructor in GetConstructorsForTypeArguments(staticBaseType, baseTypeNode))
                    {
                        if (!CheckTypeArgumentConstraints(baseTypeNode, Types.Elements(Signatures.Record(constructor).TypeParameters).ToArray()))
                        {
                            break;
                        }
                    }
                }
                var baseWithThis = GetTypeWithThisArgument(baseType, Types.Object(type).ThisType);
                if (!CheckTypeAssignableTo(typeWithThis, baseWithThis, NodeRef.None))
                {
                    IssueMemberSpecificError(node, typeWithThis, baseWithThis, Diagnostics.Class_0_incorrectly_extends_base_class_1);
                }
                else
                {
                    // Report static side error only when instance type is assignable
                    CheckTypeAssignableTo(staticType, GetTypeWithoutSignatures(staticBaseType), nameOrNode, Diagnostics.Class_static_side_0_incorrectly_extends_base_class_static_side_1);
                }
                if ((Types.Flags(baseConstructorType) & TypeFlags.TypeVariable) != 0)
                {
                    if (!IsMixinConstructorType(staticType))
                    {
                        Error(nameOrNode, Diagnostics.A_mixin_class_must_have_a_constructor_with_a_single_rest_parameter_of_type_any);
                    }
                    else
                    {
                        var constructSignatures = GetSignaturesOfType(baseConstructorType, SignatureKind.Construct);
                        if (constructSignatures.Exists(signature => (Signatures.Record(signature).Flags & SignatureFlags.Abstract) != 0) &&
                            !Utilities.HasSyntacticModifierUncached(store, n, ModifierFlags.Abstract))
                        {
                            Error(nameOrNode, Diagnostics.A_mixin_class_that_extends_from_a_type_variable_containing_an_abstract_construct_signature_must_also_be_declared_abstract);
                        }
                    }
                }
                var staticBaseSymbol = Types.Symbol(staticBaseType);
                if (!(!staticBaseSymbol.IsNone && (SymbolFlagsOf(staticBaseSymbol) & SymbolFlags.Class) != 0) && (Types.Flags(baseConstructorType) & TypeFlags.TypeVariable) == 0)
                {
                    // When the static base type is a "class-like" constructor function (but not actually a class), we verify
                    // that all instantiated base constructor signatures return the same type.
                    var constructors = GetInstantiatedConstructorsForTypeArguments(staticBaseType, baseTypeNode);
                    if (constructors.Exists(sig => !IsTypeIdenticalTo(GetReturnTypeOfSignature(sig), baseType)))
                    {
                        Error(At(node, store.Expression(baseTypeNodeRaw)), Diagnostics.Base_constructors_must_all_have_the_same_return_type);
                    }
                }
                CheckKindsOfPropertyMemberOverrides(type, baseType);
            }
        }
        CheckMembersForOverrideModifier(node, type, typeWithThis, staticType);
        foreach (var typeRefNodeRaw in GetImplementsTypeNodes(store, n))
        {
            var typeRefNode = At(node, typeRefNodeRaw);
            var expression = store.Expression(typeRefNodeRaw);
            if (!Utilities.IsEntityNameExpression(store, expression) || UtilitiesPublic.IsOptionalChain(store, expression))
            {
                Error(At(node, expression), Diagnostics.A_class_can_only_implement_an_identifier_Slashqualified_name_with_optional_type_arguments);
            }
            CheckTypeReferenceNode(typeRefNode);
            // createImplementsDiagnostics
            var t = GetReducedType(GetTypeFromTypeNode(typeRefNode));
            if (!IsErrorType(t))
            {
                if (IsValidBaseType(t))
                {
                    var genericDiag = !Types.Symbol(t).IsNone && (SymbolFlagsOf(Types.Symbol(t)) & SymbolFlags.Class) != 0
                        ? Diagnostics.Class_0_incorrectly_implements_class_1_Did_you_mean_to_extend_1_and_inherit_its_members_as_a_subclass
                        : Diagnostics.Class_0_incorrectly_implements_interface_1;
                    var baseWithThis = GetTypeWithThisArgument(t, Types.Object(type).ThisType);
                    if (!CheckTypeAssignableTo(typeWithThis, baseWithThis, NodeRef.None))
                    {
                        IssueMemberSpecificError(node, typeWithThis, baseWithThis, genericDiag);
                    }
                }
                else
                {
                    Error(typeRefNode, Diagnostics.A_class_can_only_implement_an_object_type_or_intersection_of_object_types_with_statically_known_members);
                }
            }
        }
        CheckIndexConstraints(type, symbol);
        CheckIndexConstraints(staticType, symbol, isStaticIndex: true);
        CheckTypeForDuplicateIndexSignatures(node);
        CheckPropertyInitialization(node);
    }

    // checker.ts getConstructorsForTypeArguments
    private List<Signature> GetConstructorsForTypeArguments(Type type, NodeRef typeArgumentNodeOwner)
    {
        var store = StoreOf(typeArgumentNodeOwner);
        var typeArgumentNodes = store.TypeArguments(typeArgumentNodeOwner.Node);
        var typeArgCount = typeArgumentNodes.IsNone ? 0 : store.Length(typeArgumentNodes);
        var isJavascript = Utilities.IsInJSFile(store, typeArgumentNodeOwner.Node);
        return GetSignaturesOfType(type, SignatureKind.Construct).FindAll(sig =>
        {
            var typeParameters = Types.Elements(Signatures.Record(sig).TypeParameters).ToArray();
            return (isJavascript || typeArgCount >= GetMinTypeArgumentCount(typeParameters)) && typeArgCount <= typeParameters.Length;
        });
    }

    // checker.ts checkBaseTypeAccessibility
    private void CheckBaseTypeAccessibility(Type type, NodeRef node)
    {
        var signatures = GetSignaturesOfType(type, SignatureKind.Construct);
        if (signatures.Count > 0)
        {
            var declaration = Signatures.Record(signatures[0]).Declaration;
            if (!declaration.IsNone && Utilities.HasSyntacticModifierUncached(StoreOf(declaration), declaration.Node, ModifierFlags.Private))
            {
                var typeClassDeclaration = GetClassLikeDeclarationOfSymbol(Types.Symbol(type));
                if (!IsNodeWithinClass(node, typeClassDeclaration))
                {
                    Error(node, Diagnostics.Cannot_extend_a_class_0_Class_constructor_is_marked_as_private, GetFullyQualifiedName(Types.Symbol(type)));
                }
            }
        }
    }

    [Flags]
    private enum DeclarationMeaning
    {
        GetAccessor = 1,
        SetAccessor = 2,
        PropertyAssignment = 4,
        Method = 8,
        PrivateStatic = 16,
        GetOrSetAccessor = GetAccessor | SetAccessor,
        PropertyAssignmentOrMethod = PropertyAssignment | Method,
    }

    // checker.ts checkClassForDuplicateDeclarations
    private void CheckClassForDuplicateDeclarations(NodeRef node)
    {
        var store = StoreOf(node);
        var instanceNames = new Dictionary<string, DeclarationMeaning>(StringComparer.Ordinal);
        var staticNames = new Dictionary<string, DeclarationMeaning>(StringComparer.Ordinal);
        // instance and static private identifiers share the same scope
        var privateIdentifiers = new Dictionary<string, DeclarationMeaning>(StringComparer.Ordinal);
        foreach (var member in store.Nodes(store.Members(node.Node)))
        {
            if (store.Kind(member) == SyntaxKind.Constructor)
            {
                foreach (var param in store.Nodes(store.Parameters(member)))
                {
                    if (Utilities.IsParameterPropertyDeclaration(store, param, member) && !Utilities.IsBindingPattern(store, store.Name(param)))
                    {
                        var paramName = store.Name(param);
                        AddName(instanceNames, paramName, store.Texts.GetString(store.EscapedText(paramName))!, DeclarationMeaning.GetOrSetAccessor);
                    }
                }
            }
            else
            {
                var isStaticMember = Utilities.IsStatic(store, member);
                var name = store.Name(member);
                if (name.IsNone)
                {
                    continue;
                }
                var isPrivate = store.Kind(name) == SyntaxKind.PrivateIdentifier;
                var privateStaticFlags = isPrivate && isStaticMember ? DeclarationMeaning.PrivateStatic : 0;
                var names = isPrivate ? privateIdentifiers : isStaticMember ? staticNames : instanceNames;
                var memberName = GetEffectivePropertyNameForPropertyNameNode(At(node, name));
                if (memberName is not null)
                {
                    switch (store.Kind(member))
                    {
                        case SyntaxKind.GetAccessor:
                            AddName(names, name, memberName, DeclarationMeaning.GetAccessor | privateStaticFlags);
                            break;
                        case SyntaxKind.SetAccessor:
                            AddName(names, name, memberName, DeclarationMeaning.SetAccessor | privateStaticFlags);
                            break;
                        case SyntaxKind.PropertyDeclaration:
                            AddName(names, name, memberName, DeclarationMeaning.GetOrSetAccessor | privateStaticFlags);
                            break;
                        case SyntaxKind.MethodDeclaration:
                            AddName(names, name, memberName, DeclarationMeaning.Method | privateStaticFlags);
                            break;
                    }
                }
            }
        }

        void AddName(Dictionary<string, DeclarationMeaning> names, Node location, string name, DeclarationMeaning meaning)
        {
            if (names.TryGetValue(name, out var prev))
            {
                // For private identifiers, do not allow mixing of static and instance members with the same name
                if ((prev & DeclarationMeaning.PrivateStatic) != (meaning & DeclarationMeaning.PrivateStatic))
                {
                    Error(At(node, location), Diagnostics.Duplicate_identifier_0_Static_and_instance_elements_cannot_share_the_same_private_name, GetTextOfNode(At(node, location)));
                }
                else
                {
                    var prevIsMethod = (prev & DeclarationMeaning.Method) != 0;
                    var isMethod = (meaning & DeclarationMeaning.Method) != 0;
                    if (prevIsMethod || isMethod)
                    {
                        if (prevIsMethod != isMethod)
                        {
                            Error(At(node, location), Diagnostics.Duplicate_identifier_0, GetTextOfNode(At(node, location)));
                        }
                        // If this is a method/method duplication is might be an overload, so this will be handled when overloads are considered
                    }
                    else if ((prev & meaning & ~DeclarationMeaning.PrivateStatic) != 0)
                    {
                        Error(At(node, location), Diagnostics.Duplicate_identifier_0, GetTextOfNode(At(node, location)));
                    }
                    else
                    {
                        names[name] = prev | meaning;
                    }
                }
            }
            else
            {
                names[name] = meaning;
            }
        }
    }

    // checker.ts checkClassForStaticPropertyNameConflicts
    private void CheckClassForStaticPropertyNameConflicts(NodeRef node)
    {
        var store = StoreOf(node);
        foreach (var member in store.Nodes(store.Members(node.Node)))
        {
            var memberNameNode = store.Name(member);
            var isStaticMember = Utilities.IsStatic(store, member);
            if (isStaticMember && !memberNameNode.IsNone)
            {
                var memberName = GetEffectivePropertyNameForPropertyNameNode(At(node, memberNameNode));
                switch (memberName)
                {
                    case "name":
                    case "length":
                    case "caller":
                    case "arguments":
                        if (compilerOptions.GetUseDefineForClassFields())
                        {
                            break;
                        }
                        goto case "prototype";
                    case "prototype":
                        var className = GetNameOfSymbolAsWritten(GetSymbolOfDeclaration(node));
                        Error(At(node, memberNameNode), Diagnostics.Static_property_0_conflicts_with_built_in_property_Function_0_of_constructor_function_1, memberName, className);
                        break;
                }
            }
        }
    }

    // checker.ts checkKindsOfPropertyMemberOverrides
    private void CheckKindsOfPropertyMemberOverrides(Type type, Type baseType)
    {
        // TypeScript 1.0 spec (April 2014): 8.2.3
        // A derived class inherits all members from its base class it doesn't override.
        // Inheritance means that a derived class implicitly contains all non - overridden members of the base class.
        // Both public and private property members are inherited, but only public property members can be overridden.
        // A property member in a derived class is said to override a property member in a base class
        // when the derived class property member has the same name and kind(instance or static)
        // as the base class property member.
        // The type of an overriding property member must be assignable(section 3.8.4)
        // to the type of the overridden property member, or otherwise a compile - time error occurs.
        // Base class instance member functions can be overridden by derived class instance member functions,
        // but not by other kinds of members.
        // Base class instance member variables and accessors can be overridden by
        // derived class instance member variables and accessors, but not by other kinds of members.

        // NOTE: assignability is checked in checkClassDeclaration
        var baseProperties = GetPropertiesOfType(baseType);
        var notImplementedInfo = new List<(NodeRef ErrorNode, string BaseTypeName, string TypeName, List<string> MissedProperties)>();
        foreach (var baseProperty in baseProperties.ToArray())
        {
            var @base = GetTargetSymbol(baseProperty);
            if ((SymbolFlagsOf(@base) & SymbolFlags.Prototype) != 0)
            {
                continue;
            }
            var baseSymbol = GetPropertyOfObjectType(type, NameOf(@base));
            if (baseSymbol.IsNone)
            {
                continue;
            }
            var derived = GetTargetSymbol(baseSymbol);
            var baseDeclarationFlags = GetDeclarationModifierFlagsFromSymbol(@base);
            // In order to resolve whether the inherited method was overridden in the base class or not,
            // we compare the Symbols obtained. Since getTargetSymbol returns the symbol on the *uninstantiated*
            // type declaration, derived and base resolve to the same symbol even in the case of generic classes.
            if (derived == @base)
            {
                // derived class inherits base without override/redeclaration
                var derivedClassDecl = GetClassLikeDeclarationOfSymbol(Types.Symbol(type));
                // It is an error to inherit an abstract member without implementing it or being declared abstract.
                // If there is no declaration for the derived class (as in the case of class expressions),
                // then the class cannot be declared abstract.
                if ((baseDeclarationFlags & ModifierFlags.Abstract) != 0 &&
                    (derivedClassDecl.IsNone || !Utilities.HasSyntacticModifierUncached(StoreOf(derivedClassDecl), derivedClassDecl.Node, ModifierFlags.Abstract)))
                {
                    // Searches other base types for a declaration that would satisfy the inherited abstract member.
                    // (The class may have more than one base type via declaration merging with an interface with the
                    // same name.)
                    var derivedElsewhereFound = false;
                    foreach (var otherBaseType in GetBaseTypes(type).ToArray())
                    {
                        if (otherBaseType == baseType)
                        {
                            continue;
                        }
                        var otherSymbol = GetPropertyOfObjectType(otherBaseType, NameOf(@base));
                        var derivedElsewhere = otherSymbol.IsNone ? SymbolRef.None : GetTargetSymbol(otherSymbol);
                        if (!derivedElsewhere.IsNone && derivedElsewhere != @base)
                        {
                            derivedElsewhereFound = true;
                            break;
                        }
                    }
                    if (derivedElsewhereFound)
                    {
                        continue;
                    }
                    var baseTypeName = TypeToString(baseType);
                    var typeName = TypeToString(type);
                    var basePropertyName = SymbolToString(baseProperty);
                    var index = notImplementedInfo.FindIndex(e => e.ErrorNode == derivedClassDecl);
                    if (index >= 0)
                    {
                        var existing = notImplementedInfo[index];
                        existing.MissedProperties.Add(basePropertyName);
                        notImplementedInfo[index] = (derivedClassDecl, baseTypeName, typeName, existing.MissedProperties);
                    }
                    else
                    {
                        notImplementedInfo.Add((derivedClassDecl, baseTypeName, typeName, [basePropertyName]));
                    }
                }
            }
            else
            {
                // derived overrides base.
                var derivedDeclarationFlags = GetDeclarationModifierFlagsFromSymbol(derived);
                if ((baseDeclarationFlags & ModifierFlags.Private) != 0 || (derivedDeclarationFlags & ModifierFlags.Private) != 0)
                {
                    // either base or derived property is private - not override, skip it
                    continue;
                }
                DiagnosticMessage errorMessage;
                var basePropertyFlags = SymbolFlagsOf(@base) & SymbolFlags.PropertyOrAccessor;
                var derivedPropertyFlags = SymbolFlagsOf(derived) & SymbolFlags.PropertyOrAccessor;
                var derivedValueDeclaration = ValueDeclarationOf(derived);
                if (basePropertyFlags != 0 && derivedPropertyFlags != 0)
                {
                    // property/accessor is overridden with property/accessor
                    var baseDeclarations = DeclarationsOf(@base);
                    if (((TransientCheckFlags(@base) & CheckFlags.Synthetic) != 0
                            ? baseDeclarations is not null && baseDeclarations.Exists(d => IsPropertyAbstractOrInterface(d, baseDeclarationFlags))
                            : baseDeclarations is null || baseDeclarations.TrueForAll(d => IsPropertyAbstractOrInterface(d, baseDeclarationFlags))) ||
                        (TransientCheckFlags(@base) & CheckFlags.Mapped) != 0 ||
                        (!derivedValueDeclaration.IsNone && Kind(derivedValueDeclaration) == SyntaxKind.BinaryExpression))
                    {
                        // when the base property is abstract or from an interface, base/derived flags don't need to match
                        // for intersection properties, this must be true of *any* of the declarations, for others it must be true of *all*
                        // same when the derived property is from an assignment
                        continue;
                    }
                    var overriddenInstanceProperty = basePropertyFlags != SymbolFlags.Property && derivedPropertyFlags == SymbolFlags.Property;
                    var overriddenInstanceAccessor = basePropertyFlags == SymbolFlags.Property && derivedPropertyFlags != SymbolFlags.Property;
                    if (overriddenInstanceProperty || overriddenInstanceAccessor)
                    {
                        var message = overriddenInstanceProperty
                            ? Diagnostics._0_is_defined_as_an_accessor_in_class_1_but_is_overridden_here_in_2_as_an_instance_property
                            : Diagnostics._0_is_defined_as_a_property_in_class_1_but_is_overridden_here_in_2_as_an_accessor;
                        Error(NameOrNode(derivedValueDeclaration), message, SymbolToString(@base), TypeToString(baseType), TypeToString(type));
                    }
                    else if (compilerOptions.GetUseDefineForClassFields())
                    {
                        var uninitialized = DeclarationsOf(derived)?.Find(d => Kind(d) == SyntaxKind.PropertyDeclaration && StoreOf(d).Initializer(d.Node).IsNone) ?? NodeRef.None;
                        if (!uninitialized.IsNone && !derived.IsTransient && (baseDeclarationFlags & ModifierFlags.Abstract) == 0 &&
                            (derivedDeclarationFlags & ModifierFlags.Abstract) == 0 &&
                            !(DeclarationsOf(derived) ?? []).Exists(d => (StoreOf(d).Flags(d.Node) & NodeFlags.Ambient) != 0))
                        {
                            var classDeclaration = GetClassLikeDeclarationOfSymbol(Types.Symbol(type));
                            var constructor = FindConstructorDeclaration(classDeclaration);
                            var ustore = StoreOf(uninitialized);
                            var propName = At(uninitialized, ustore.Name(uninitialized.Node));
                            if (!ustore.ExclamationToken(uninitialized.Node).IsNone || constructor.IsNone || Kind(propName) != SyntaxKind.Identifier || !strictNullChecks ||
                                !IsPropertyInitializedInConstructor(propName, type, constructor))
                            {
                                Error(NameOrNode(derivedValueDeclaration),
                                    Diagnostics.Property_0_will_overwrite_the_base_property_in_1_If_this_is_intentional_add_an_initializer_Otherwise_add_a_declare_modifier_or_remove_the_redundant_declaration,
                                    SymbolToString(@base), TypeToString(baseType));
                            }
                        }
                    }
                    // correct case
                    continue;
                }
                else if (IsPrototypeProperty(@base))
                {
                    if (IsPrototypeProperty(derived) || (SymbolFlagsOf(derived) & SymbolFlags.Property) != 0)
                    {
                        // method is overridden with method or property -- correct case
                        continue;
                    }
                    errorMessage = Diagnostics.Class_0_defines_instance_member_function_1_but_extended_class_2_defines_it_as_instance_member_accessor;
                }
                else if ((SymbolFlagsOf(@base) & SymbolFlags.Accessor) != 0)
                {
                    errorMessage = Diagnostics.Class_0_defines_instance_member_accessor_1_but_extended_class_2_defines_it_as_instance_member_function;
                }
                else
                {
                    errorMessage = Diagnostics.Class_0_defines_instance_member_property_1_but_extended_class_2_defines_it_as_instance_member_function;
                }
                Error(NameOrNode(derivedValueDeclaration), errorMessage, TypeToString(baseType), SymbolToString(@base), TypeToString(type));
            }
        }
        foreach (var (errorNode, baseTypeName, typeName, missedProperties) in notImplementedInfo)
        {
            var isClassExpression = !errorNode.IsNone && Kind(errorNode) == SyntaxKind.ClassExpression;
            if (missedProperties.Count == 1)
            {
                if (isClassExpression)
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_expression_does_not_implement_inherited_abstract_member_0_from_class_1, missedProperties[0], baseTypeName);
                }
                else
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_0_does_not_implement_inherited_abstract_member_1_from_class_2, typeName, missedProperties[0], baseTypeName);
                }
            }
            else if (missedProperties.Count > 5)
            {
                var listed = QuotedList(missedProperties, 4);
                var remainingMissedProperties = missedProperties.Count - 4;
                if (isClassExpression)
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_expression_is_missing_implementations_for_the_following_members_of_0_Colon_1_and_2_more, baseTypeName, listed, remainingMissedProperties);
                }
                else
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_0_is_missing_implementations_for_the_following_members_of_1_Colon_2_and_3_more, typeName, baseTypeName, listed, remainingMissedProperties);
                }
            }
            else
            {
                var listed = QuotedList(missedProperties, missedProperties.Count);
                if (isClassExpression)
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_expression_is_missing_implementations_for_the_following_members_of_0_Colon_1, baseTypeName, listed);
                }
                else
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_0_is_missing_implementations_for_the_following_members_of_1_Colon_2, typeName, baseTypeName, listed);
                }
            }
        }
    }

    // map(list.slice(0, count), prop => `'${prop}'`).join(", ")
    private static string QuotedList(List<string> list, int count)
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }
            builder.Append('\'').Append(list[i]).Append('\'');
        }
        return builder.ToString();
    }

    // checker.ts isPropertyAbstractOrInterface
    private bool IsPropertyAbstractOrInterface(NodeRef declaration, ModifierFlags baseDeclarationFlags)
    {
        var store = StoreOf(declaration);
        return ((baseDeclarationFlags & ModifierFlags.Abstract) != 0 && (store.Kind(declaration.Node) != SyntaxKind.PropertyDeclaration || store.Initializer(declaration.Node).IsNone)) ||
            store.Kind(store.Parent(declaration.Node)) == SyntaxKind.InterfaceDeclaration;
    }

    // utilities.ts findConstructorDeclaration
    private NodeRef FindConstructorDeclaration(NodeRef node)
    {
        if (node.IsNone)
        {
            return NodeRef.None;
        }
        var store = StoreOf(node);
        foreach (var member in store.Nodes(store.Members(node.Node)))
        {
            if (store.Kind(member) == SyntaxKind.Constructor && !Utilities.NodeIsMissing(store, store.Body(member)))
            {
                return At(node, member);
            }
        }
        return NodeRef.None;
    }

    // checker.ts checkMembersForOverrideModifier
    private void CheckMembersForOverrideModifier(NodeRef node, Type type, Type typeWithThis, Type staticType)
    {
        var store = StoreOf(node);
        var baseTypeNode = GetClassExtendsHeritageElement(store, node.Node);
        var baseTypes = baseTypeNode.IsNone ? null : GetBaseTypes(type);
        var baseWithThis = baseTypes is { Count: > 0 } ? GetTypeWithThisArgument(baseTypes[0], Types.Object(type).ThisType) : Type.None;
        var baseStaticType = GetBaseConstructorTypeOfClass(type);
        foreach (var member in store.Nodes(store.Members(node.Node)))
        {
            if (Utilities.HasSyntacticModifierUncached(store, member, ModifierFlags.Ambient))
            {
                continue;
            }
            if (store.Kind(member) == SyntaxKind.Constructor)
            {
                foreach (var param in store.Nodes(store.Parameters(member)))
                {
                    if (Utilities.IsParameterPropertyDeclaration(store, param, member))
                    {
                        CheckExistingMemberForOverrideModifier(node, staticType, baseStaticType, baseWithThis, type, typeWithThis, At(node, param), memberIsParameterProperty: true);
                    }
                }
            }
            CheckExistingMemberForOverrideModifier(node, staticType, baseStaticType, baseWithThis, type, typeWithThis, At(node, member), memberIsParameterProperty: false);
        }
    }

    // checker.ts checkExistingMemberForOverrideModifier
    private void CheckExistingMemberForOverrideModifier(NodeRef node, Type staticType, Type baseStaticType, Type baseWithThis, Type type, Type typeWithThis, NodeRef member, bool memberIsParameterProperty)
    {
        var store = StoreOf(member);
        var name = store.Name(member.Node);
        var declaredProp = !name.IsNone ? GetSymbolAtLocation(At(member, name)) : SymbolRef.None;
        if (declaredProp.IsNone)
        {
            declaredProp = GetSymbolAtLocation(member);
        }
        if (declaredProp.IsNone)
        {
            return;
        }
        CheckMemberForOverrideModifier(node, staticType, baseStaticType, baseWithThis, type, typeWithThis,
            Utilities.HasSyntacticModifierUncached(store, member.Node, ModifierFlags.Override), Utilities.HasSyntacticModifierUncached(store, member.Node, ModifierFlags.Abstract),
            !memberIsParameterProperty && Utilities.IsStatic(store, member.Node), memberIsParameterProperty, declaredProp, member);
    }

    // checker.ts checkMemberForOverrideModifier
    private void CheckMemberForOverrideModifier(NodeRef node, Type staticType, Type baseStaticType, Type baseWithThis, Type type, Type typeWithThis,
        bool memberHasOverrideModifier, bool memberHasAbstractModifier, bool memberIsStatic, bool memberIsParameterProperty, SymbolRef member, NodeRef errorNode)
    {
        var nodeInAmbientContext = (StoreOf(node).Flags(node.Node) & NodeFlags.Ambient) != 0;
        var valueDeclaration = ValueDeclarationOf(member);
        if (memberHasOverrideModifier && !valueDeclaration.IsNone && Utilities.IsClassElement(StoreOf(valueDeclaration), valueDeclaration.Node))
        {
            var vstore = StoreOf(valueDeclaration);
            var vname = vstore.Name(valueDeclaration.Node);
            if (!vname.IsNone && IsNonBindableDynamicName(At(valueDeclaration, vname)))
            {
                Error(errorNode, Diagnostics.This_member_cannot_have_an_override_modifier_because_its_name_is_dynamic);
                return;
            }
        }
        if (!baseWithThis.IsNone && (memberHasOverrideModifier || compilerOptions.NoImplicitOverride == true))
        {
            var thisType = memberIsStatic ? staticType : typeWithThis;
            var baseType = memberIsStatic ? baseStaticType : baseWithThis;
            var prop = GetPropertyOfType(thisType, NameOf(member));
            var baseProp = GetPropertyOfType(baseType, NameOf(member));
            var baseClassName = TypeToString(baseWithThis);
            if (!prop.IsNone && baseProp.IsNone && memberHasOverrideModifier)
            {
                var suggestion = GetSpellingSuggestionForName(SymbolName(member), GetPropertiesOfType(baseType), SymbolFlags.ClassMember); // Again, using symbol name: note that's different from `symbol.escapedName`
                if (!suggestion.IsNone)
                {
                    Error(errorNode, Diagnostics.This_member_cannot_have_an_override_modifier_because_it_is_not_declared_in_the_base_class_0_Did_you_mean_1, baseClassName, SymbolToString(suggestion));
                }
                else
                {
                    Error(errorNode, Diagnostics.This_member_cannot_have_an_override_modifier_because_it_is_not_declared_in_the_base_class_0, baseClassName);
                }
            }
            else if (!prop.IsNone && DeclarationsOf(baseProp) is { } baseDeclarations && compilerOptions.NoImplicitOverride == true && !nodeInAmbientContext)
            {
                var baseHasAbstract = baseDeclarations.Exists(d => Utilities.HasSyntacticModifierUncached(StoreOf(d), d.Node, ModifierFlags.Abstract));
                if (memberHasOverrideModifier)
                {
                    return;
                }
                if (!baseHasAbstract)
                {
                    Error(errorNode, memberIsParameterProperty
                        ? Diagnostics.This_parameter_property_must_have_an_override_modifier_because_it_overrides_a_member_in_base_class_0
                        : Diagnostics.This_member_must_have_an_override_modifier_because_it_overrides_a_member_in_the_base_class_0, baseClassName);
                }
                else if (memberHasAbstractModifier && baseHasAbstract)
                {
                    Error(errorNode, Diagnostics.This_member_must_have_an_override_modifier_because_it_overrides_an_abstract_method_that_is_declared_in_the_base_class_0, baseClassName);
                }
            }
        }
        else if (memberHasOverrideModifier)
        {
            Error(errorNode, Diagnostics.This_member_cannot_have_an_override_modifier_because_its_containing_class_0_does_not_extend_another_class, TypeToString(type));
        }
    }

    // checker.ts isNonBindableDynamicName
    private bool IsNonBindableDynamicName(NodeRef node)
    {
        // checker.ts isNonBindableDynamicName: isDynamicName(node) && !isLateBindableName(node)
        return Utilities.IsDynamicName(StoreOf(node), node.Node) && !IsLateBindableName(node);
    }

    // checker.ts checkPropertyInitialization
    private void CheckPropertyInitialization(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        if (!strictNullChecks || !compilerOptions.GetStrictOptionValue(compilerOptions.StrictPropertyInitialization) || (store.Flags(n) & NodeFlags.Ambient) != 0)
        {
            return;
        }
        var constructor = FindConstructorDeclaration(node);
        foreach (var member in store.Nodes(store.Members(n)))
        {
            if ((Utilities.GetSyntacticModifierFlagsNoCache(store, member) & ModifierFlags.Ambient) != 0)
            {
                continue;
            }
            if (!Utilities.IsStatic(store, member) && IsPropertyWithoutInitializer(store, member))
            {
                var propName = store.Name(member);
                if (store.Kind(propName) is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier or SyntaxKind.ComputedPropertyName)
                {
                    var type = GetTypeOfSymbol(GetSymbolOfDeclaration(At(node, member)));
                    if (!((Types.Flags(type) & TypeFlags.AnyOrUnknown) != 0 || ContainsUndefinedType(type)))
                    {
                        if (constructor.IsNone || !IsPropertyInitializedInConstructor(At(node, propName), type, constructor))
                        {
                            Error(At(node, propName), Diagnostics.Property_0_has_no_initializer_and_is_not_definitely_assigned_in_the_constructor, Utilities.DeclarationNameToString(store, propName));
                        }
                    }
                }
            }
        }
    }

    // checker.ts isPropertyWithoutInitializer
    private static bool IsPropertyWithoutInitializer(NodeStore store, Node node) =>
        store.Kind(node) == SyntaxKind.PropertyDeclaration && !Utilities.HasSyntacticModifierUncached(store, node, ModifierFlags.Abstract) &&
        store.ExclamationToken(node).IsNone && store.Initializer(node).IsNone;

    // ---- interfaces ----

    // checker.ts checkInterfaceDeclaration
    private void CheckInterfaceDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar checking
        if (!CheckGrammarModifiers(node))
        {
            CheckGrammarInterfaceDeclaration(node);
        }
        if (!AllowBlockDeclarations(store, store.Parent(n)))
        {
            GrammarErrorOnNode(node, Diagnostics._0_declarations_can_only_be_declared_inside_a_block, "interface");
        }
        CheckTypeParameters(node, store.TypeParameters(n));
        var name = At(node, store.Name(n));
        CheckTypeNameIsReserved(name, Diagnostics.Interface_name_cannot_be_0);
        CheckExportsOnMergedDeclarations(node);
        var symbol = GetSymbolOfDeclaration(node);
        CheckTypeParameterListsIdentical(symbol);
        // Only check this symbol once
        var firstInterfaceDecl = GetDeclarationOfKind(symbol, SyntaxKind.InterfaceDeclaration);
        if (node == firstInterfaceDecl)
        {
            var type = GetDeclaredTypeOfSymbol(symbol);
            var typeWithThis = GetTypeWithThisArgument(type);
            // run subsequent checks only if first set succeeded
            if (CheckInheritedPropertiesAreIdentical(type, name))
            {
                foreach (var baseType in GetBaseTypes(type).ToArray())
                {
                    CheckTypeAssignableTo(typeWithThis, GetTypeWithThisArgument(baseType, Types.Object(type).ThisType), name, Diagnostics.Interface_0_incorrectly_extends_interface_1);
                }
                CheckIndexConstraints(type, symbol);
            }
        }
        CheckObjectTypeForDuplicateDeclarations(node);
        foreach (var heritageElement in GetInterfaceBaseTypeNodes(node))
        {
            var expression = store.Expression(heritageElement);
            if (!Utilities.IsEntityNameExpression(store, expression) || UtilitiesPublic.IsOptionalChain(store, expression))
            {
                Error(At(node, expression), Diagnostics.An_interface_can_only_extend_an_identifier_Slashqualified_name_with_optional_type_arguments);
            }
            CheckTypeReferenceNode(At(node, heritageElement));
        }
        CheckSourceElements(node, store.Members(n));
        CheckTypeForDuplicateIndexSignatures(node);
        RegisterForUnusedIdentifiersCheck(node);
    }

    // ---- enums ----

    // checker.ts checkEnumDeclaration / checkEnumDeclarationWorker
    private void CheckEnumDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        // Grammar checking
        CheckGrammarModifiers(node);
        CheckCollisionsForDeclarationName(node, At(node, store.Name(n)));
        CheckExportsOnMergedDeclarations(node);
        CheckSourceElements(node, store.Members(n));
        // (erasableSyntaxOnly: not an option of Gate C)
        ComputeEnumMemberValues(node);
        // Spec 2014 - Section 9.3:
        // It isn't possible for one enum declaration to continue the automatic numbering sequence of another,
        // and when an enum type has multiple declarations, only one declaration is permitted to omit a value
        // for the first member.
        //
        // Only perform this check once per symbol
        var enumSymbol = GetSymbolOfDeclaration(node);
        var firstDeclaration = GetDeclarationOfKind(enumSymbol, store.Kind(n));
        if (node != firstDeclaration)
        {
            return;
        }
        var declarations = DeclarationsOf(enumSymbol) ?? [];
        if (declarations.Count > 1)
        {
            var enumIsConst = Utilities.IsEnumConst(store, n);
            // check that const is placed\omitted on all enum declarations
            foreach (var decl in declarations)
            {
                if (Kind(decl) == SyntaxKind.EnumDeclaration && Utilities.IsEnumConst(StoreOf(decl), decl.Node) != enumIsConst)
                {
                    Error(At(decl, Utilities.GetNameOfDeclaration(StoreOf(decl), decl.Node)), Diagnostics.Enum_declarations_must_all_be_const_or_non_const);
                }
            }
        }
        var seenEnumMissingInitialInitializer = false;
        foreach (var declaration in declarations)
        {
            // return true if we hit a violation of the rule, false otherwise
            if (Kind(declaration) != SyntaxKind.EnumDeclaration)
            {
                continue;
            }
            var dstore = StoreOf(declaration);
            var members = dstore.Nodes(dstore.Members(declaration.Node));
            if (members.Length == 0)
            {
                continue;
            }
            var firstEnumMember = members[0];
            if (dstore.Initializer(firstEnumMember).IsNone)
            {
                if (seenEnumMissingInitialInitializer)
                {
                    Error(At(declaration, dstore.Name(firstEnumMember)), Diagnostics.In_an_enum_with_multiple_declarations_only_one_declaration_can_omit_an_initializer_for_its_first_enum_element);
                }
                else
                {
                    seenEnumMissingInitialInitializer = true;
                }
            }
        }
    }

    // checker.ts computeEnumMemberValues
    private void ComputeEnumMemberValues(NodeRef node)
    {
        if (!enumValuesComputed.Add(node))
        {
            return;
        }
        var store = StoreOf(node);
        double? autoValue = 0;
        var previous = NodeRef.None;
        foreach (var memberNode in store.Nodes(store.Members(node.Node)))
        {
            var member = At(node, memberNode);
            var result = ComputeEnumMemberValue(member, autoValue, previous);
            enumMemberValues[member] = result;
            autoValue = result is double d ? d + 1 : null;
            previous = member;
        }
    }

    // checker.ts computeEnumMemberValue
    private object? ComputeEnumMemberValue(NodeRef member, double? autoValue, NodeRef previous)
    {
        var store = StoreOf(member);
        var name = store.Name(member.Node);
        if (IsComputedNonLiteralName(store, name))
        {
            Error(At(member, name), Diagnostics.Computed_property_names_are_not_allowed_in_enums);
        }
        else if (store.Kind(name) == SyntaxKind.BigIntLiteral)
        {
            Error(At(member, name), Diagnostics.An_enum_member_cannot_have_a_numeric_name);
        }
        else
        {
            var text = store.Texts.GetString(Utilities.GetTextOfPropertyName(store, name));
            if (text is not null && IsNumericLiteralName(text) && text is not ("Infinity" or "-Infinity" or "NaN"))
            {
                Error(At(member, name), Diagnostics.An_enum_member_cannot_have_a_numeric_name);
            }
        }
        if (!store.Initializer(member.Node).IsNone)
        {
            return ComputeConstantEnumMemberValue(member);
        }
        var parent = store.Parent(member.Node);
        // In ambient non-const numeric enum declarations, enum members without initializers are
        // considered computed members (as opposed to having auto-incremented values).
        if ((store.Flags(parent) & NodeFlags.Ambient) != 0 && !Utilities.IsEnumConst(store, parent))
        {
            return null;
        }
        // If the member declaration specifies no value, the member is considered a constant enum member.
        // If the member is the first member in the enum declaration, it is assigned the value zero.
        // Otherwise, it is assigned the value of the immediately preceding member plus one, and an error
        // occurs if the immediately preceding member is not a constant enum member.
        if (autoValue is null)
        {
            Error(At(member, name), Diagnostics.Enum_member_must_have_initializer);
            return null;
        }
        if (compilerOptions.GetIsolatedModules() && !previous.IsNone && !store.Initializer(previous.Node).IsNone)
        {
            var prevValue = GetEnumMemberValue(previous);
            if (prevValue is not double)
            {
                Error(At(member, name), Diagnostics.Enum_member_following_a_non_literal_numeric_member_must_have_an_initializer_when_isolatedModules_is_enabled);
            }
        }
        return autoValue.Value;
    }

    // utilities.ts isComputedNonLiteralName
    private static bool IsComputedNonLiteralName(NodeStore store, Node name) =>
        store.Kind(name) == SyntaxKind.ComputedPropertyName && !Utilities.IsStringOrNumericLiteralLike(store, store.Expression(name));

    // checker.ts computeConstantEnumMemberValue
    private object? ComputeConstantEnumMemberValue(NodeRef member)
    {
        var store = StoreOf(member);
        var parent = store.Parent(member.Node);
        var isConstEnum = Utilities.IsEnumConst(store, parent);
        var initializer = At(member, store.Initializer(member.Node));
        var result = EvaluateConstantExpression(initializer, member);
        if (result is not null)
        {
            if (isConstEnum && result is double d && !double.IsFinite(d))
            {
                Error(initializer, double.IsNaN(d)
                    ? Diagnostics.const_enum_member_initializer_was_evaluated_to_disallowed_value_NaN
                    : Diagnostics.const_enum_member_initializer_was_evaluated_to_a_non_finite_value);
            }
            // (isolatedModules: the syntactically-string check)
        }
        else if (isConstEnum)
        {
            Error(initializer, Diagnostics.const_enum_member_initializers_must_be_constant_expressions);
        }
        else if ((store.Flags(parent) & NodeFlags.Ambient) != 0)
        {
            Error(initializer, Diagnostics.In_ambient_enum_declarations_member_initializer_must_be_constant_expression);
        }
        else
        {
            CheckTypeAssignableTo(CheckExpression(initializer), numberType, initializer, Diagnostics.Type_0_is_not_assignable_to_type_1_as_required_for_computed_enum_member_values);
        }
        return result;
    }

    /// <summary>checker.ts getEnumMemberValue(node).value.</summary>
    private object? GetEnumMemberValue(NodeRef member)
    {
        ComputeEnumMemberValues(Parent(member));
        return enumMemberValues.TryGetValue(member, out var value) ? value : null;
    }

    // checker.ts evaluateEnumMember
    private object? EvaluateEnumMember(NodeRef expr, SymbolRef symbol, NodeRef location)
    {
        var declaration = ValueDeclarationOf(symbol);
        if (declaration.IsNone || declaration == location)
        {
            Error(expr, Diagnostics.Property_0_is_used_before_being_assigned, SymbolToString(symbol));
            return null;
        }
        if (!IsBlockScopedNameDeclaredBeforeUse(declaration, location))
        {
            Error(expr, Diagnostics.A_member_initializer_in_a_enum_declaration_cannot_reference_members_declared_after_it_including_members_defined_in_other_enums);
            return 0.0;
        }
        return GetEnumMemberValue(declaration);
    }

    // checker.ts evaluateElementAccessExpression
    private object? EvaluateElementAccessExpression(NodeRef expr, NodeRef location)
    {
        var store = StoreOf(expr);
        var root = store.Expression(expr.Node);
        var argument = store.ArgumentExpression(expr.Node);
        if (Utilities.IsEntityNameExpression(store, root) && Utilities.IsStringLiteralLike(store, argument))
        {
            var rootSymbol = ResolveEntityName(At(expr, root), SymbolFlags.Value, ignoreErrors: true);
            if (!rootSymbol.IsNone && (SymbolFlagsOf(rootSymbol) & SymbolFlags.Enum) != 0)
            {
                var name = Utilities.EscapeLeadingUnderscores(store.Texts.GetString(store.Text(argument))!);
                var member = ExportsOf(rootSymbol)?.Get(name) ?? SymbolRef.None;
                if (!member.IsNone)
                {
                    return !location.IsNone ? EvaluateEnumMember(expr, member, location) : GetEnumMemberValue(ValueDeclarationOf(member));
                }
            }
        }
        return null;
    }

    // ---- modules ----

    // checker.ts checkModuleDeclaration
    private void CheckModuleDeclaration(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var body = store.Body(n);
        if (!body.IsNone)
        {
            CheckSourceElement(At(node, body));
            if (!Utilities.IsGlobalScopeAugmentation(store, n))
            {
                RegisterForUnusedIdentifiersCheck(node);
            }
        }
        // checkModuleDeclarationDiagnostics
        // Grammar checking
        var isGlobalAugmentation = Utilities.IsGlobalScopeAugmentation(store, n);
        var inAmbientContext = (store.Flags(n) & NodeFlags.Ambient) != 0;
        var name = At(node, store.Name(n));
        if (isGlobalAugmentation && !inAmbientContext)
        {
            Error(name, Diagnostics.Augmentations_for_the_global_scope_should_have_declare_modifier_unless_they_appear_in_already_ambient_context);
        }
        var isAmbientExternalModule = Utilities.IsAmbientModule(store, n);
        var contextErrorMessage = isAmbientExternalModule
            ? Diagnostics.An_ambient_module_declaration_is_only_allowed_at_the_top_level_in_a_file
            : Diagnostics.A_namespace_declaration_is_only_allowed_at_the_top_level_of_a_namespace_or_module;
        if (CheckGrammarModuleElementContext(node, contextErrorMessage))
        {
            // If we hit a module declaration in an illegal context, just bail out to avoid cascading errors.
            return;
        }
        if (!CheckGrammarModifiers(node))
        {
            if (!inAmbientContext && store.Kind(name.Node) == SyntaxKind.StringLiteral)
            {
                GrammarErrorOnNode(name, Diagnostics.Only_ambient_modules_can_use_quoted_names);
            }
        }
        if (store.Kind(name.Node) == SyntaxKind.Identifier)
        {
            CheckCollisionsForDeclarationName(node, name);
            if ((store.Flags(n) & (NodeFlags.Namespace | NodeFlags.GlobalAugmentation)) == 0)
            {
                Error(name, Diagnostics.A_namespace_declaration_should_not_be_declared_using_the_module_keyword_Please_use_the_namespace_keyword_instead);
            }
        }
        CheckExportsOnMergedDeclarations(node);
        var symbol = GetSymbolOfDeclaration(node);
        // The following checks only apply on a non-ambient instantiated module declaration.
        if ((SymbolFlagsOf(symbol) & SymbolFlags.ValueModule) != 0 && !inAmbientContext && IsInstantiatedModule(store, n, compilerOptions.ShouldPreserveConstEnums()))
        {
            // (erasableSyntaxOnly: not an option of Gate C)
            if (compilerOptions.GetIsolatedModules() && store.ExternalModuleIndicator(FileAt(node.File).Root) is null)
            {
                // This could be loosened a little if needed. The only problem we are trying to avoid is unqualified
                // references to namespace members declared in other files. But use of namespaces is discouraged anyway,
                // so for now we will just not allow them in scripts, which is the only place they can merge cross-file.
                Error(name, Diagnostics.Namespaces_are_not_allowed_in_global_script_files_when_0_is_enabled_If_this_file_is_not_intended_to_be_a_global_script_set_moduleDetection_to_force_or_add_an_empty_export_statement,
                    compilerOptions.VerbatimModuleSyntax == true ? "verbatimModuleSyntax" : "isolatedModules");
            }
            if (DeclarationsOf(symbol) is { Count: > 1 })
            {
                var firstNonAmbientClassOrFunc = GetFirstNonAmbientClassOrFunctionDeclaration(symbol);
                if (!firstNonAmbientClassOrFunc.IsNone)
                {
                    if (node.File != firstNonAmbientClassOrFunc.File)
                    {
                        Error(name, Diagnostics.A_namespace_declaration_cannot_be_in_a_different_file_from_a_class_or_function_with_which_it_is_merged);
                    }
                    else if (store.Pos(n) < store.Pos(firstNonAmbientClassOrFunc.Node))
                    {
                        Error(name, Diagnostics.A_namespace_declaration_cannot_be_located_prior_to_a_class_or_function_with_which_it_is_merged);
                    }
                }
                // (LexicalModuleMergesWithClass: emit only)
            }
            // (verbatimModuleSyntax in CommonJS files: Gate C does not set verbatimModuleSyntax)
        }
        if (isAmbientExternalModule)
        {
            if (IsExternalModuleAugmentation(store, n))
            {
                // body of the augmentation should be checked for consistency only if augmentation was applied to its target (either global scope or module)
                // otherwise we'll be swamped in cascading errors.
                // We can detect if augmentation was applied using following rules:
                // - augmentation for a global scope is always applied
                // - augmentation for some external module is applied if symbol for augmentation is merged (it was combined with target module).
                var checkBody = isGlobalAugmentation || GetSymbolOfDeclaration(node).IsTransient;
                if (checkBody && !body.IsNone)
                {
                    foreach (var statement in store.Nodes(store.Statements(body)))
                    {
                        CheckModuleAugmentationElement(At(node, statement), isGlobalAugmentation);
                    }
                }
            }
            else if (IsGlobalSourceFile(At(node, store.Parent(n))))
            {
                if (isGlobalAugmentation)
                {
                    Error(name, Diagnostics.Augmentations_for_the_global_scope_can_only_be_directly_nested_in_external_modules_or_ambient_module_declarations);
                }
                else if (Utilities.IsExternalModuleNameRelative(store.Texts.GetString(Utilities.GetTextOfIdentifierOrLiteral(store, name.Node))!))
                {
                    Error(name, Diagnostics.Ambient_module_declaration_cannot_specify_relative_module_name);
                }
            }
            else
            {
                if (isGlobalAugmentation)
                {
                    Error(name, Diagnostics.Augmentations_for_the_global_scope_can_only_be_directly_nested_in_external_modules_or_ambient_module_declarations);
                }
                else
                {
                    // Node is not an augmentation and is not located on the script level.
                    // This means that this is declaration of ambient module that is located in other module or namespace which is prohibited.
                    Error(name, Diagnostics.Ambient_modules_cannot_be_nested_in_other_modules_or_namespaces);
                }
            }
        }
    }

    // checker.ts isInstantiatedModule
    private static bool IsInstantiatedModule(NodeStore store, Node node, bool preserveConstEnums)
    {
        var moduleState = Utilities.GetModuleInstanceState(store, node);
        return moduleState == ModuleInstanceState.Instantiated || (preserveConstEnums && moduleState == ModuleInstanceState.ConstEnumOnly);
    }

    // checker.ts getFirstNonAmbientClassOrFunctionDeclaration
    private NodeRef GetFirstNonAmbientClassOrFunctionDeclaration(SymbolRef symbol)
    {
        foreach (var declaration in DeclarationsOf(symbol) ?? [])
        {
            var store = StoreOf(declaration);
            if ((store.Kind(declaration.Node) == SyntaxKind.ClassDeclaration ||
                    (store.Kind(declaration.Node) == SyntaxKind.FunctionDeclaration && !Utilities.NodeIsMissing(store, store.Body(declaration.Node)))) &&
                (store.Flags(declaration.Node) & NodeFlags.Ambient) == 0)
            {
                return declaration;
            }
        }
        return NodeRef.None;
    }

    // checker.ts checkModuleAugmentationElement
    private void CheckModuleAugmentationElement(NodeRef node, bool isGlobalAugmentation)
    {
        var store = StoreOf(node);
        var n = node.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.VariableStatement:
                // error each individual name in variable statement instead of marking the entire variable statement
                foreach (var decl in store.Nodes(store.Declarations(store.DeclarationList(n))))
                {
                    CheckModuleAugmentationElement(At(node, decl), isGlobalAugmentation);
                }
                break;
            case SyntaxKind.ExportAssignment:
            case SyntaxKind.ExportDeclaration:
                GrammarErrorOnFirstToken(node, Diagnostics.Exports_and_export_assignments_are_not_permitted_in_module_augmentations);
                break;
            case SyntaxKind.ImportEqualsDeclaration:
                // import a = e.x; in module augmentation is ok, but not import a = require('fs)
                if (Utilities.IsInternalModuleImportEqualsDeclaration(store, n))
                {
                    break;
                }
                goto case SyntaxKind.ImportDeclaration;
            case SyntaxKind.ImportDeclaration:
                GrammarErrorOnFirstToken(node, Diagnostics.Imports_are_not_permitted_in_module_augmentations_Consider_moving_them_to_the_enclosing_external_module);
                break;
            case SyntaxKind.BindingElement:
            case SyntaxKind.VariableDeclaration:
                var name = store.Name(n);
                if (Utilities.IsBindingPattern(store, name))
                {
                    foreach (var el in store.Nodes(store.Elements(name)))
                    {
                        // mark individual names in binding pattern
                        CheckModuleAugmentationElement(At(node, el), isGlobalAugmentation);
                    }
                }
                break;
        }
    }

    // checker.ts checkExternalModuleExports
    private void CheckExternalModuleExports(NodeRef node)
    {
        var moduleSymbol = GetSymbolOfDeclaration(node);
        if (moduleSymbol.IsNone || !exportsCheckedModules.Add(moduleSymbol))
        {
            return;
        }
        var moduleExports = ExportsOf(moduleSymbol);
        var exportEqualsSymbol = moduleExports?.Get(InternalSymbolName.ExportEquals) ?? SymbolRef.None;
        if (!exportEqualsSymbol.IsNone && HasExportedMembers(moduleExports!))
        {
            var declaration = GetDeclarationOfAliasSymbol(exportEqualsSymbol);
            if (declaration.IsNone)
            {
                declaration = ValueDeclarationOf(exportEqualsSymbol);
            }
            if (!declaration.IsNone && !IsTopLevelInExternalModuleAugmentation(StoreOf(declaration), declaration.Node) && !Utilities.IsInJSFile(StoreOf(declaration), declaration.Node))
            {
                Error(declaration, Diagnostics.An_export_assignment_cannot_be_used_in_a_module_with_other_exported_elements);
            }
        }
        // Checks for export * conflicts
        var exports = GetExportsOfModule(moduleSymbol);
        for (var i = 0; i < exports.Count; i++)
        {
            var id = exports.NameAt(i);
            var symbol = exports.ValueAt(i);
            if (id == "__export")
            {
                continue;
            }
            // ECMA262: 15.2.1.1 It is a Syntax Error if the ExportedNames of ModuleItemList contains any duplicate entries.
            // (TS Exceptions: namespaces, function overloads, enums, and interfaces)
            var flags = SymbolFlagsOf(symbol);
            if ((flags & (SymbolFlags.Namespace | SymbolFlags.Enum)) != 0)
            {
                continue;
            }
            var declarations = DeclarationsOf(symbol);
            var exportedDeclarationsCount = 0;
            foreach (var d in declarations ?? [])
            {
                if (IsNotOverloadAndNotAccessor(d) && Kind(d) != SyntaxKind.InterfaceDeclaration)
                {
                    exportedDeclarationsCount++;
                }
            }
            if ((flags & SymbolFlags.TypeAlias) != 0 && exportedDeclarationsCount <= 2)
            {
                // it is legal to merge type alias with other values
                // so count should be either 1 (just type alias) or 2 (type alias + merged value)
                continue;
            }
            if (exportedDeclarationsCount > 1)
            {
                // (isDuplicatedCommonJSExport: JavaScript, module C9)
                foreach (var declaration in declarations!)
                {
                    if (IsNotOverload(declaration))
                    {
                        diagnostics.Add(CreateDiagnosticForNode(declaration, Diagnostics.Cannot_redeclare_exported_variable_0, Utilities.UnescapeLeadingUnderscores(id)));
                    }
                }
            }
        }
    }

    private static bool HasExportedMembers(SymTable exports)
    {
        for (var i = 0; i < exports.Count; i++)
        {
            if (exports.NameAt(i) != InternalSymbolName.ExportEquals)
            {
                return true;
            }
        }
        return false;
    }

    // utilities.ts isNotOverload / isNotAccessor / isNotOverloadAndNotAccessor
    private bool IsNotOverload(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        return (store.Kind(declaration.Node) != SyntaxKind.FunctionDeclaration && store.Kind(declaration.Node) != SyntaxKind.MethodDeclaration) || !store.Body(declaration.Node).IsNone;
    }

    private bool IsNotOverloadAndNotAccessor(NodeRef declaration) => IsNotOverload(declaration) && !IsAccessorKind(StoreOf(declaration), declaration.Node);
}
