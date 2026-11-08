// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// Portions Copyright 2026 Max Schwenk and the tsrs contributors
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (module C5; the functions are listed below)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C5 (details below).
// Functions (property and element access: checkNonNullExpression, checkNonNullType,
// checkNonNullTypeWithReporter, checkNonNullNonVoidType, reportObjectPossiblyNullOrUndefinedError,
// reportCannotInvokePossiblyNullOrUndefinedError, checkPropertyAccessExpression, checkPropertyAccessChain,
// checkQualifiedName, checkPropertyAccessExpressionOrQualifiedName, isMethodAccessForCall,
// lookupSymbolForPrivateIdentifierDeclaration, checkPrivateIdentifierPropertyAccess, getFlowTypeOfAccessExpression,
// checkPropertyNotUsedBeforeDeclaration, isPropertyDeclaredInAncestorClass, reportNonexistentProperty,
// typeHasStaticProperty, getSuggestedSymbolForNonexistentProperty, getSuggestionForNonexistentIndexSignature,
// checkPropertyAccessibility, checkPropertyAccessibilityAtLocation, getEnclosingClassFromThisParameter,
// isClassDerivedFromDeclaringClasses, forEachProperty, getDeclaringClass, isAssignmentToReadonlyEntity,
// isThisPropertyAccessInConstructor, isSelfTypeAccess, checkIndexedAccess, checkElementAccessChain,
// checkElementAccessExpression, checkIndexedAccessIndexType, isForInVariableForNumericPropertyNames, the optional
// chain markers, isJSLiteralType and the element access diagnostics of getPropertyTypeForIndexType)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/checker.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; modules C1 (subset) and C5. TODO(C7): the flow types of access
// expressions (Checker.FlowSeam.cs); the diagnostics that depend on them are not reported while narrowing is pending.
// Module C8: deprecation suggestions. Module C9d: unchecked JavaScript suggestions (isUncheckedJSSuggestion), private
// names of plain JavaScript files. Module C9e: the lib suggestions (getSuggestedLibForNonExistentProperty).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // ---- non-null checks ----

    /// <summary>checker.ts checkNonNullExpression.</summary>
    internal Type CheckNonNullExpression(NodeRef node) => CheckNonNullType(CheckExpression(node), node);

    /// <summary>checker.ts checkNonNullType.</summary>
    internal Type CheckNonNullType(Type type, NodeRef node) => CheckNonNullTypeWithReporter(type, node, cannotInvoke: false);

    // checker.ts checkNonNullTypeWithReporter (module AR2, from RS2's saved patch; Track O: the reporter as a flag,
    // reportObjectPossiblyNullOrUndefinedError or reportCannotInvokePossiblyNullOrUndefinedError, instead of a delegate
    // made at every call; tsrs checker_04.rs check_non_null_type_with_reporter takes an impl FnMut, monomorphized)
    private Type CheckNonNullTypeWithReporter(Type type, NodeRef node, bool cannotInvoke)
    {
        if (strictNullChecks && (Types.Flags(type) & TypeFlags.Unknown) != 0)
        {
            var store = StoreOf(node);
            if (Utilities.IsEntityNameExpression(store, node.Node))
            {
                var nodeText = EntityNameToString(node);
                if (nodeText.Length < 100)
                {
                    Error(node, Diagnostics._0_is_of_type_unknown, nodeText);
                    return errorType;
                }
            }
            Error(node, Diagnostics.Object_is_of_type_unknown);
            return errorType;
        }
        var facts = GetTypeFacts(type, TypeFacts.IsUndefinedOrNull);
        if ((facts & TypeFacts.IsUndefinedOrNull) != 0)
        {
            if (cannotInvoke)
            {
                ReportCannotInvokePossiblyNullOrUndefinedError(node, facts);
            }
            else
            {
                ReportObjectPossiblyNullOrUndefinedError(node, facts);
            }
            var t = GetNonNullableType(type);
            return (Types.Flags(t) & (TypeFlags.Nullable | TypeFlags.Never)) != 0 ? errorType : t;
        }
        return type;
    }

    // checker.ts reportObjectPossiblyNullOrUndefinedError
    private void ReportObjectPossiblyNullOrUndefinedError(NodeRef node, TypeFacts facts)
    {
        var store = StoreOf(node);
        var nodeText = Utilities.IsEntityNameExpression(store, node.Node) ? EntityNameToString(node) : null;
        if (store.Kind(node.Node) == SyntaxKind.NullKeyword)
        {
            Error(node, Diagnostics.The_value_0_cannot_be_used_here, "null");
            return;
        }
        if (nodeText is not null && nodeText.Length < 100)
        {
            if (store.Kind(node.Node) == SyntaxKind.Identifier && nodeText == "undefined")
            {
                Error(node, Diagnostics.The_value_0_cannot_be_used_here, "undefined");
                return;
            }
            Error(node, (facts & TypeFacts.IsUndefined) != 0
                ? (facts & TypeFacts.IsNull) != 0 ? Diagnostics._0_is_possibly_null_or_undefined : Diagnostics._0_is_possibly_undefined
                : Diagnostics._0_is_possibly_null, nodeText);
        }
        else
        {
            Error(node, (facts & TypeFacts.IsUndefined) != 0
                ? (facts & TypeFacts.IsNull) != 0 ? Diagnostics.Object_is_possibly_null_or_undefined : Diagnostics.Object_is_possibly_undefined
                : Diagnostics.Object_is_possibly_null);
        }
    }

    // checker.ts reportCannotInvokePossiblyNullOrUndefinedError
    private void ReportCannotInvokePossiblyNullOrUndefinedError(NodeRef node, TypeFacts facts) =>
        Error(node, (facts & TypeFacts.IsUndefined) != 0
            ? (facts & TypeFacts.IsNull) != 0 ? Diagnostics.Cannot_invoke_an_object_which_is_possibly_null_or_undefined : Diagnostics.Cannot_invoke_an_object_which_is_possibly_undefined
            : Diagnostics.Cannot_invoke_an_object_which_is_possibly_null);

    /// <summary>checker.ts checkNonNullNonVoidType.</summary>
    internal Type CheckNonNullNonVoidType(Type type, NodeRef node)
    {
        var nonNullType = CheckNonNullType(type, node);
        if ((Types.Flags(nonNullType) & TypeFlags.Void) != 0)
        {
            var store = StoreOf(node);
            if (Utilities.IsEntityNameExpression(store, node.Node))
            {
                var nodeText = EntityNameToString(node);
                if (store.Kind(node.Node) == SyntaxKind.Identifier && nodeText == "undefined")
                {
                    Error(node, Diagnostics.The_value_0_cannot_be_used_here, nodeText);
                    return nonNullType;
                }
                if (nodeText.Length < 100)
                {
                    Error(node, Diagnostics._0_is_possibly_undefined, nodeText);
                    return nonNullType;
                }
            }
            Error(node, Diagnostics.Object_is_possibly_undefined);
        }
        return nonNullType;
    }

    // ---- optional chains ----

    // checker.ts addOptionalTypeMarker
    private Type AddOptionalTypeMarker(Type type) => strictNullChecks ? GetUnionType([type, optionalType]) : type;

    // checker.ts removeOptionalTypeMarker
    private Type RemoveOptionalTypeMarker(Type type) => strictNullChecks ? FilterType(type, t => t != optionalType) : type;

    // checker.ts propagateOptionalTypeMarker
    private Type PropagateOptionalTypeMarker(Type type, NodeRef node, bool wasOptional) =>
        wasOptional ? Utilities.IsOutermostOptionalChain(StoreOf(node), node.Node) ? GetOptionalType(type) : AddOptionalTypeMarker(type) : type;

    // checker.ts getOptionalExpressionType
    private Type GetOptionalExpressionType(Type exprType, NodeRef expression)
    {
        var store = StoreOf(expression);
        return Utilities.IsExpressionOfOptionalChainRoot(store, expression.Node) ? GetNonNullableType(exprType)
            : UtilitiesPublic.IsOptionalChain(store, expression.Node) ? RemoveOptionalTypeMarker(exprType)
            : exprType;
    }

    // ---- property access ----

    /// <summary>checker.ts checkPropertyAccessExpression.</summary>
    internal Type CheckPropertyAccessExpression(NodeRef node, CheckMode checkMode = CheckMode.Normal, bool writeOnly = false)
    {
        var store = StoreOf(node);
        var left = At(node, store.Expression(node.Node));
        var right = At(node, store.Name(node.Node));
        if ((store.Flags(node.Node) & NodeFlags.OptionalChain) != 0)
        {
            // checkPropertyAccessChain
            var leftType = CheckExpression(left);
            var nonOptionalType = GetOptionalExpressionType(leftType, left);
            return PropagateOptionalTypeMarker(CheckPropertyAccessExpressionOrQualifiedName(node, left, CheckNonNullType(nonOptionalType, left), right, checkMode), node, nonOptionalType != leftType);
        }
        return CheckPropertyAccessExpressionOrQualifiedName(node, left, CheckNonNullExpression(left), right, checkMode, writeOnly);
    }

    /// <summary>checker.ts checkQualifiedName.</summary>
    internal Type CheckQualifiedName(NodeRef node, CheckMode checkMode = CheckMode.Normal)
    {
        var store = StoreOf(node);
        var left = At(node, store.Left(node.Node));
        var leftType = Utilities.IsPartOfTypeQuery(store, node.Node) && Utilities.IsThisIdentifier(store, left.Node)
            ? CheckNonNullType(CheckThisExpression(left), left)
            : CheckNonNullExpression(left);
        return CheckPropertyAccessExpressionOrQualifiedName(node, left, leftType, At(node, store.Right(node.Node)), checkMode);
    }

    /// <summary>The property access part of checkQualifiedName for entity names in type queries (Checker.TypeNodes.cs).</summary>
    internal Type CheckPropertyAccessOnType(NodeRef node, Type leftType, NodeRef right)
    {
        var store = StoreOf(node);
        var left = At(node, store.Kind(node.Node) == SyntaxKind.PropertyAccessExpression ? store.Expression(node.Node) : store.Left(node.Node));
        return CheckPropertyAccessExpressionOrQualifiedName(node, left, CheckNonNullType(leftType, left), right, CheckMode.Normal);
    }

    // checker.ts isMethodAccessForCall
    private static bool IsMethodAccessForCall(NodeStore store, Node node)
    {
        while (store.Kind(store.Parent(node)) == SyntaxKind.ParenthesizedExpression)
        {
            node = store.Parent(node);
        }
        var parent = store.Parent(node);
        return store.Kind(parent) is SyntaxKind.CallExpression or SyntaxKind.NewExpression && store.Expression(parent) == node;
    }

    // checker.ts checkPropertyAccessExpressionOrQualifiedName
    private Type CheckPropertyAccessExpressionOrQualifiedName(NodeRef node, NodeRef left, Type leftType, NodeRef right, CheckMode checkMode, bool writeOnly = false)
    {
        var store = StoreOf(node);
        var parentSymbol = SymbolNodeLinks.Has(left) ? SymbolNodeLinks.Get(left).ResolvedSymbol : SymbolRef.None;
        var assignmentKind = GetAssignmentTargetKind(node);
        var apparentType = GetApparentType(assignmentKind != AssignmentKind.None || IsMethodAccessForCall(store, node.Node) ? GetWidenedType(leftType) : leftType);
        var isAnyLike = (Types.Flags(apparentType) & TypeFlags.Any) != 0 || apparentType == silentNeverType;
        SymbolRef prop;
        string rightName;
        if (store.Kind(right.Node) == SyntaxKind.PrivateIdentifier)
        {
            rightName = store.Texts.GetString(store.EscapedText(right.Node))!;
            var lexicallyScopedSymbol = LookupSymbolForPrivateIdentifierDeclaration(right);
            if (assignmentKind != AssignmentKind.None && !lexicallyScopedSymbol.IsNone && !ValueDeclarationOf(lexicallyScopedSymbol).IsNone && Kind(ValueDeclarationOf(lexicallyScopedSymbol)) == SyntaxKind.MethodDeclaration)
            {
                GrammarErrorOnNode(right, Diagnostics.Cannot_assign_to_private_method_0_Private_methods_are_not_writable, rightName);
            }
            if (isAnyLike)
            {
                if (!lexicallyScopedSymbol.IsNone)
                {
                    return IsErrorType(apparentType) ? errorType : apparentType;
                }
                if (GetContainingClassExcludingClassDecorators(right).IsNone)
                {
                    GrammarErrorOnNode(right, Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
                    return anyType;
                }
            }
            prop = lexicallyScopedSymbol.IsNone ? SymbolRef.None : GetPropertyOfType(leftType, NameOf(lexicallyScopedSymbol));
            if (prop.IsNone)
            {
                // Check for private-identifier-specific shadowing and lexical-scoping errors.
                if (CheckPrivateIdentifierPropertyAccess(leftType, right, lexicallyScopedSymbol))
                {
                    return errorType;
                }
                var containingClass = GetContainingClassExcludingClassDecorators(right);
                if (!containingClass.IsNone && Program.IsPlainJsFile(FileAt(containingClass.File), compilerOptions.CheckJs))
                {
                    GrammarErrorOnNode(right, Diagnostics.Private_field_0_must_be_declared_in_an_enclosing_class, rightName);
                }
            }
            else
            {
                var propFlags = SymbolFlagsOf(prop);
                var isSetonlyAccessor = (propFlags & SymbolFlags.SetAccessor) != 0 && (propFlags & SymbolFlags.GetAccessor) == 0;
                if (isSetonlyAccessor && assignmentKind != AssignmentKind.Definite)
                {
                    Error(node, Diagnostics.Private_accessor_was_defined_without_a_getter);
                }
            }
        }
        else
        {
            rightName = NameText(store, store.EscapedText(right.Node)); // (module AR2: see NameText)
            if (isAnyLike)
            {
                if (store.Kind(left.Node) == SyntaxKind.Identifier && !parentSymbol.IsNone)
                {
                    MarkPropertyAliasReferenced(node, SymbolRef.None, leftType);
                }
                return IsErrorType(apparentType) ? errorType : apparentType;
            }
            prop = GetPropertyOfType(apparentType, rightName, skipObjectFunctionPropertyAugment: IsConstEnumObjectType(apparentType));
            // (includeTypeOnlyMembers for qualified names: the binder's type-only members are found the same way)
        }
        MarkPropertyAliasReferenced(node, prop, leftType);
        Type propType;
        if (prop.IsNone)
        {
            var indexInfo = store.Kind(right.Node) != SyntaxKind.PrivateIdentifier && (assignmentKind == AssignmentKind.None || !IsGenericObjectType(leftType) || IsThisTypeParameter(leftType))
                ? GetApplicableIndexInfoForName(apparentType, rightName)
                : null;
            if (indexInfo is null || indexInfo.Type.IsNone)
            {
                var isUncheckedJS = IsUncheckedJSSuggestion(node, Types.Symbol(leftType), excludeClasses: true);
                if (!isUncheckedJS && IsJSLiteralType(leftType))
                {
                    return anyType;
                }
                if (Types.Symbol(leftType) == globalThisSymbol)
                {
                    var globalThisMember = globals.Get(rightName);
                    if (!globalThisMember.IsNone && (SymbolFlagsOf(globalThisMember) & SymbolFlags.BlockScoped) != 0)
                    {
                        Error(right, Diagnostics.Property_0_does_not_exist_on_type_1, Utilities.UnescapeLeadingUnderscores(rightName), TypeToString(leftType));
                    }
                    else if (noImplicitAny)
                    {
                        Error(right, Diagnostics.Element_implicitly_has_an_any_type_because_type_0_has_no_index_signature, TypeToString(leftType));
                    }
                    return anyType;
                }
                if (rightName.Length > 0 && !CheckAndReportErrorForExtendingInterface(node))
                {
                    ReportNonexistentProperty(right, IsThisTypeParameter(leftType) ? apparentType : leftType, isUncheckedJS);
                }
                return errorType;
            }
            if (indexInfo.IsReadonly && (Utilities.IsAssignmentTarget(store, node.Node) || IsDeleteTarget(store, node.Node)))
            {
                Error(node, Diagnostics.Index_signature_in_type_0_only_permits_reading, TypeToString(apparentType));
            }
            propType = indexInfo.Type;
            if (compilerOptions.NoUncheckedIndexedAccess == true && GetAssignmentTargetKind(node) != AssignmentKind.Definite)
            {
                propType = GetUnionType([propType, missingType]);
            }
            if (compilerOptions.NoPropertyAccessFromIndexSignature == true && store.Kind(node.Node) == SyntaxKind.PropertyAccessExpression)
            {
                Error(right, Diagnostics.Property_0_comes_from_an_index_signature_so_it_must_be_accessed_with_0, Utilities.UnescapeLeadingUnderscores(rightName));
            }
            if (!indexInfo.Declaration.IsNone && IsDeprecatedDeclaration(indexInfo.Declaration))
            {
                AddDeprecatedSuggestion(right, [indexInfo.Declaration], rightName);
            }
        }
        else
        {
            var targetPropSymbol = ResolveAliasWithDeprecationCheck(prop, right);
            if (IsDeprecatedSymbol(targetPropSymbol) && IsUncalledFunctionReference(node, targetPropSymbol) && DeclarationsOf(targetPropSymbol) is { } targetDeclarations)
            {
                AddDeprecatedSuggestion(right, targetDeclarations, rightName);
            }
            CheckPropertyNotUsedBeforeDeclaration(prop, node, right);
            MarkPropertyAsReferenced(prop, node, IsSelfTypeAccess(left, parentSymbol));
            SymbolNodeLinks.Get(node).ResolvedSymbol = prop;
            if (store.Kind(node.Node) is SyntaxKind.PropertyAccessExpression or SyntaxKind.QualifiedName)
            {
                SymbolNodeLinks.Get(right).ResolvedSymbol = prop;
            }
            CheckPropertyAccessibility(node, store.Kind(left.Node) == SyntaxKind.SuperKeyword, Utilities.IsWriteAccess(store, node.Node), apparentType, prop);
            if (IsAssignmentToReadonlyEntity(node, prop, assignmentKind))
            {
                Error(right, Diagnostics.Cannot_assign_to_0_because_it_is_a_read_only_property, Utilities.IdText(store, right.Node) is var idText && !idText.IsNone ? store.Texts.GetString(idText) : rightName);
                return errorType;
            }
            propType = IsThisPropertyAccessInConstructor(node, prop) ? autoType
                : writeOnly || Utilities.IsWriteOnlyAccess(store, node.Node) ? GetWriteTypeOfSymbol(prop)
                : GetTypeOfSymbol(prop);
        }
        return GetFlowTypeOfAccessExpression(node, prop, propType, right, checkMode);
    }

    // checker.ts isThisTypeParameter
    private bool IsThisTypeParameter(Type type) => (Types.Flags(type) & TypeFlags.TypeParameter) != 0 && Types.TypeParameter(type).IsThisType;

    // utilities.ts isDeleteTarget
    private static bool IsDeleteTarget(NodeStore store, Node node)
    {
        if (store.Kind(node) is not (SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression))
        {
            return false;
        }
        node = WalkUpParenthesizedExpressions(store, store.Parent(node));
        return store.Kind(node) == SyntaxKind.DeleteExpression;
    }

    // utilities.ts getContainingClassExcludingClassDecorators
    private NodeRef GetContainingClassExcludingClassDecorators(NodeRef node)
    {
        var store = StoreOf(node);
        var decorator = Node.None;
        for (var n = node.Node; !n.IsNone; n = store.Parent(n))
        {
            if (Utilities.IsClassLike(store, n))
            {
                break;
            }
            if (store.Kind(n) == SyntaxKind.Decorator)
            {
                decorator = n;
                break;
            }
        }
        if (!decorator.IsNone && Utilities.IsClassLike(store, store.Parent(decorator)))
        {
            return At(node, Utilities.GetContainingClass(store, store.Parent(decorator)));
        }
        return At(node, Utilities.GetContainingClass(store, !decorator.IsNone ? decorator : node.Node));
    }

    /// <summary>checker.ts isJSLiteralType.</summary>
    internal bool IsJSLiteralType(Type type)
    {
        if (noImplicitAny)
        {
            return false; // Flag is meaningless under `noImplicitAny` mode
        }
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.JSLiteral) != 0)
        {
            return true;
        }
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Union) != 0)
        {
            return Array.TrueForAll(Types.Elements(Types.Union(type).Types).ToArray(), IsJSLiteralType);
        }
        if ((flags & TypeFlags.Intersection) != 0)
        {
            return Array.Exists(Types.Elements(Types.Union(type).Types).ToArray(), IsJSLiteralType);
        }
        if ((flags & TypeFlags.Instantiable) != 0)
        {
            var constraint = GetResolvedBaseConstraint(type);
            return constraint != type && IsJSLiteralType(constraint);
        }
        return false;
    }

    // checker.ts lookupSymbolForPrivateIdentifierDeclaration: the containing classes from the inside out; the member whose
    // declaration name is this private identifier (the binder names it after the class's symbol id)
    private SymbolRef LookupSymbolForPrivateIdentifierDeclaration(NodeRef propName)
    {
        var store = StoreOf(propName);
        var text = store.Texts.Span(store.EscapedText(propName.Node)).ToString();
        for (var containingClass = GetContainingClassExcludingClassDecorators(propName).Node; !containingClass.IsNone; containingClass = Utilities.GetContainingClass(store, containingClass))
        {
            var classSymbol = GetSymbolOfDeclaration(At(propName, containingClass));
            foreach (var table in (SymTable?[])[MembersOf(classSymbol), ExportsOf(classSymbol)])
            {
                if (table is null)
                {
                    continue;
                }
                for (var i = 0; i < table.Count; i++)
                {
                    var member = table.ValueAt(i);
                    var declaration = ValueDeclarationOf(member);
                    if (declaration.IsNone)
                    {
                        continue;
                    }
                    var ds = StoreOf(declaration);
                    var memberName = Utilities.DeclarationNameField(ds, declaration.Node);
                    if (!memberName.IsNone && ds.Kind(memberName) == SyntaxKind.PrivateIdentifier && ds.Texts.Equals(ds.EscapedText(memberName), text))
                    {
                        return member;
                    }
                }
            }
        }
        return SymbolRef.None;
    }

    // checker.ts checkPrivateIdentifierPropertyAccess
    private bool CheckPrivateIdentifierPropertyAccess(Type leftType, NodeRef right, SymbolRef lexicallyScopedIdentifier)
    {
        // Either the identifier could not be looked up in the lexical scope OR the lexically scoped identifier did not exist on the type.
        // Find a private identifier with the same description on the type.
        var propertyOnType = SymbolRef.None;
        var store = StoreOf(right);
        foreach (var symbol in GetPropertiesOfType(leftType))
        {
            var decl = ValueDeclarationOf(symbol);
            if (!decl.IsNone)
            {
                var ds = StoreOf(decl);
                var declName = Utilities.DeclarationNameField(ds, decl.Node);
                if (!declName.IsNone && ds.Kind(declName) == SyntaxKind.PrivateIdentifier && ds.Texts.GetString(ds.EscapedText(declName)) == store.Texts.GetString(store.EscapedText(right.Node)))
                {
                    propertyOnType = symbol;
                    break;
                }
            }
        }
        var diagName = DiagnosticName("", right);
        if (!propertyOnType.IsNone)
        {
            var typeValueDecl = ValueDeclarationOf(propertyOnType);
            var typeClass = At(typeValueDecl, Utilities.GetContainingClass(StoreOf(typeValueDecl), typeValueDecl.Node));
            // We found a private identifier property with the same description.
            // Either:
            // - There is a lexically scoped private identifier AND it shadows the one we found on the type.
            // - It is an attempt to access the private identifier outside of the class.
            var lexicalValueDecl = lexicallyScopedIdentifier.IsNone ? NodeRef.None : ValueDeclarationOf(lexicallyScopedIdentifier);
            if (!lexicalValueDecl.IsNone)
            {
                var lexicalClass = At(lexicalValueDecl, Utilities.GetContainingClass(StoreOf(lexicalValueDecl), lexicalValueDecl.Node));
                if (IsNodeDescendantOf(lexicalClass, typeClass))
                {
                    var diagnostic = Error(right, Diagnostics.The_property_0_cannot_be_accessed_on_type_1_within_this_class_because_it_is_shadowed_by_another_private_identifier_with_the_same_spelling, diagName, TypeToString(leftType));
                    AddRelatedInfo(diagnostic,
                        CreateRelatedForNode(lexicalValueDecl, Diagnostics.The_shadowing_declaration_of_0_is_defined_here, diagName),
                        CreateRelatedForNode(typeValueDecl, Diagnostics.The_declaration_of_0_that_you_probably_intended_to_use_is_defined_here, diagName));
                    return true;
                }
            }
            var typeClassName = StoreOf(typeClass).Name(typeClass.Node);
            Error(right, Diagnostics.Property_0_is_not_accessible_outside_class_1_because_it_has_a_private_identifier, diagName,
                typeClassName.IsNone ? "(anonymous)" : DiagnosticName("", At(typeClass, typeClassName))); // checker.ts anon
            return true;
        }
        return false;
    }

    // checker.ts getFlowTypeOfAccessExpression (module C7: getFlowTypeOfProperty and the flow type; module C9d: duplicated
    // CommonJS exports and JavaScript assignment declarations)
    private Type GetFlowTypeOfAccessExpression(NodeRef node, SymbolRef prop, Type propType, NodeRef errorNode, CheckMode checkMode)
    {
        // Only compute control flow type if this is a property access expression that isn't an
        // assignment target, and the referenced property was declared as a variable, property,
        // accessor, or optional method.
        var assignmentKind = GetAssignmentTargetKind(node);
        if (assignmentKind == AssignmentKind.Definite)
        {
            return RemoveMissingType(propType, !prop.IsNone && (SymbolFlagsOf(prop) & SymbolFlags.Optional) != 0);
        }
        if (!prop.IsNone && (SymbolFlagsOf(prop) & (SymbolFlags.Variable | SymbolFlags.Property | SymbolFlags.Accessor)) == 0 &&
            !((SymbolFlagsOf(prop) & SymbolFlags.Method) != 0 && (Types.Flags(propType) & TypeFlags.Union) != 0) &&
            !IsDuplicatedCommonJSExport(DeclarationsOf(prop)))
        {
            return propType;
        }
        if (propType == autoType)
        {
            return GetFlowTypeOfProperty(node, prop);
        }
        propType = GetNarrowableTypeForReference(propType, node, checkMode);
        // If strict null checks and strict property initialization checks are enabled, if we have
        // a this.xxx property access, if the property is an instance property without an initializer,
        // and if we are in a constructor of the same class as the property declaration, assume that
        // the property is uninitialized at the top of the control flow.
        var store = StoreOf(node);
        var assumeUninitialized = false;
        if (strictNullChecks && compilerOptions.GetStrictOptionValue(compilerOptions.StrictPropertyInitialization) &&
            Utilities.IsAccessExpression(store, node.Node) && store.Kind(store.Expression(node.Node)) == SyntaxKind.ThisKeyword)
        {
            var declaration = prop.IsNone ? NodeRef.None : ValueDeclarationOf(prop);
            if (!declaration.IsNone && IsPropertyWithoutInitializer(declaration))
            {
                var ds = StoreOf(declaration);
                if (!Utilities.IsStatic(ds, declaration.Node))
                {
                    var flowContainer = GetControlFlowContainer(node);
                    if (Kind(flowContainer) == SyntaxKind.Constructor && flowContainer.File == declaration.File &&
                        StoreOf(flowContainer).Parent(flowContainer.Node) == ds.Parent(declaration.Node) && (ds.Flags(declaration.Node) & NodeFlags.Ambient) == 0)
                    {
                        assumeUninitialized = true;
                    }
                }
            }
        }
        else if (strictNullChecks && !prop.IsNone && !ValueDeclarationOf(prop).IsNone)
        {
            var valueDeclaration = ValueDeclarationOf(prop);
            var vs = StoreOf(valueDeclaration);
            if (vs.Kind(valueDeclaration.Node) == SyntaxKind.PropertyAccessExpression &&
                Utilities.GetAssignmentDeclarationPropertyAccessKind(vs, valueDeclaration.Node) != AssignmentDeclarationKind.None &&
                GetControlFlowContainer(node) == GetControlFlowContainer(valueDeclaration))
            {
                assumeUninitialized = true;
            }
        }
        var flowType = GetFlowTypeOfReference(node, propType, assumeUninitialized ? GetOptionalType(propType) : propType);
        if (assumeUninitialized && !ContainsUndefinedType(propType) && ContainsUndefinedType(flowType))
        {
            Error(errorNode, Diagnostics.Property_0_is_used_before_being_assigned, SymbolToString(prop));
            // Return the declared type to reduce follow-on errors
            return propType;
        }
        return assignmentKind != AssignmentKind.None ? GetBaseTypeOfLiteralType(flowType) : flowType;
    }

    // checker.ts checkPropertyNotUsedBeforeDeclaration
    private void CheckPropertyNotUsedBeforeDeclaration(SymbolRef prop, NodeRef node, NodeRef right)
    {
        var valueDeclaration = ValueDeclarationOf(prop);
        if (valueDeclaration.IsNone || StoreOf(node).IsDeclarationFile(FileAt(node.File).Root))
        {
            return;
        }
        ProgramDiagnostic? diagnosticMessage = null;
        var store = StoreOf(right);
        // (module AR2, Track O: the name is read only for a diagnostic, so it is made only then; tsrs passes the arena text)
        string DeclarationName() => store.Texts.GetString(Utilities.IdText(store, right.Node))!;
        string? declarationName = null;
        var vstore = StoreOf(valueDeclaration);
        if (IsInPropertyInitializerOrClassStaticBlock(node) &&
            !IsOptionalPropertyDeclaration(valueDeclaration) &&
            !(Utilities.IsAccessExpression(StoreOf(node), node.Node) && Utilities.IsAccessExpression(StoreOf(node), StoreOf(node).Expression(node.Node))) &&
            !IsBlockScopedNameDeclaredBeforeUse(valueDeclaration, right) &&
            !(vstore.Kind(valueDeclaration.Node) == SyntaxKind.MethodDeclaration && (Utilities.GetCombinedModifierFlags(vstore, valueDeclaration.Node) & ModifierFlags.Static) != 0) &&
            (compilerOptions.GetUseDefineForClassFields() || !IsPropertyDeclaredInAncestorClass(prop)))
        {
            diagnosticMessage = Error(right, Diagnostics.Property_0_is_used_before_its_initialization, declarationName = DeclarationName());
        }
        else if (vstore.Kind(valueDeclaration.Node) == SyntaxKind.ClassDeclaration &&
            StoreOf(node).Kind(StoreOf(node).Parent(node.Node)) != SyntaxKind.TypeReference &&
            (vstore.Flags(valueDeclaration.Node) & NodeFlags.Ambient) == 0 &&
            !IsBlockScopedNameDeclaredBeforeUse(valueDeclaration, right))
        {
            diagnosticMessage = Error(right, Diagnostics.Class_0_used_before_its_declaration, declarationName = DeclarationName());
        }
        if (diagnosticMessage is not null)
        {
            AddRelatedInfo(diagnosticMessage, CreateRelatedForNode(valueDeclaration, Diagnostics._0_is_declared_here, declarationName!));
        }
    }

    // utilities.ts isOptionalPropertyDeclaration
    private bool IsOptionalPropertyDeclaration(NodeRef node) =>
        Kind(node) == SyntaxKind.PropertyDeclaration && !StoreOf(node).QuestionToken(node.Node).IsNone;

    // checker.ts isPropertyDeclaredInAncestorClass
    private bool IsPropertyDeclaredInAncestorClass(SymbolRef prop)
    {
        var parent = ParentOf(prop);
        if (!parent.IsNone && (SymbolFlagsOf(parent) & SymbolFlags.Class) != 0)
        {
            var baseTypes = GetBaseTypes(GetDeclaredTypeOfSymbol(parent));
            if (baseTypes.Count > 0)
            {
                var superProperty = GetPropertyOfType(baseTypes[0], NameOf(prop));
                return !superProperty.IsNone && !ValueDeclarationOf(superProperty).IsNone;
            }
        }
        return false;
    }

    // checker.ts reportNonexistentProperty (unchecked JavaScript, the lib suggestions and elaborateNeverIntersection:
    // C9/C4)
    private void ReportNonexistentProperty(NodeRef propNode, Type containingType, bool isUncheckedJS = false)
    {
        ref var links = ref ExpressionLinks.Get(propNode);
        links.NonExistentPropCheckCache ??= new HashSet<string>(StringComparer.Ordinal);
        var key = containingType.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + (isUncheckedJS ? "|true" : "|false");
        if (!links.NonExistentPropCheckCache.Add(key))
        {
            return;
        }
        var store = StoreOf(propNode);
        var escapedText = store.Texts.GetString(store.EscapedText(propNode.Node))!;
        DiagnosticMessageChain? errorInfo = null;
        ProgramRelatedInformation? relatedInfo = null;
        if (store.Kind(propNode.Node) != SyntaxKind.PrivateIdentifier && (Types.Flags(containingType) & TypeFlags.Union) != 0 && (Types.Flags(containingType) & TypeFlags.Primitive) == 0)
        {
            foreach (var subtype in Types.Elements(Types.Union(containingType).Types).ToArray())
            {
                if (GetPropertyOfType(subtype, escapedText).IsNone && GetApplicableIndexInfoForName(subtype, escapedText) is null)
                {
                    errorInfo = ChainDiagnosticMessages(errorInfo, Diagnostics.Property_0_does_not_exist_on_type_1, Utilities.DeclarationNameToString(store, propNode.Node), TypeToString(subtype));
                    break;
                }
            }
        }
        if (TypeHasStaticProperty(escapedText, containingType))
        {
            var propName = Utilities.DeclarationNameToString(store, propNode.Node);
            var typeName = TypeToString(containingType);
            errorInfo = ChainDiagnosticMessages(errorInfo, Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_to_access_the_static_member_2_instead, propName, typeName, typeName + "." + propName);
        }
        else
        {
            var promisedType = GetPromisedTypeOfPromise(containingType);
            if (!promisedType.IsNone && !GetPropertyOfType(promisedType, escapedText).IsNone)
            {
                errorInfo = ChainDiagnosticMessages(errorInfo, Diagnostics.Property_0_does_not_exist_on_type_1, Utilities.DeclarationNameToString(store, propNode.Node), TypeToString(containingType));
                relatedInfo = CreateRelatedForNode(propNode, Diagnostics.Did_you_forget_to_use_await);
            }
            else
            {
                var missingProperty = Utilities.DeclarationNameToString(store, propNode.Node);
                var container = TypeToString(containingType);
                var libSuggestion = GetSuggestedLibForNonExistentProperty(missingProperty, containingType);
                var suggestion = libSuggestion is not null ? SymbolRef.None : GetSuggestedSymbolForNonexistentProperty(propNode, containingType);
                if (libSuggestion is not null)
                {
                    errorInfo = ChainDiagnosticMessages(errorInfo, Diagnostics.Property_0_does_not_exist_on_type_1_Do_you_need_to_change_your_target_library_Try_changing_the_lib_compiler_option_to_2_or_later, missingProperty, container, libSuggestion);
                }
                else if (!suggestion.IsNone)
                {
                    var suggestedName = SymbolName(suggestion);
                    var message = isUncheckedJS ? Diagnostics.Property_0_may_not_exist_on_type_1_Did_you_mean_2 : Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_2;
                    errorInfo = ChainDiagnosticMessages(errorInfo, message, missingProperty, container, suggestedName);
                    var suggestionValueDeclaration = ValueDeclarationOf(suggestion);
                    if (!suggestionValueDeclaration.IsNone)
                    {
                        relatedInfo = CreateRelatedForNode(suggestionValueDeclaration, Diagnostics._0_is_declared_here, suggestedName);
                    }
                }
                else
                {
                    // (containerSeemsToBeEmptyDomElement needs the lib option, which Gate C does not set)
                    errorInfo = ChainDiagnosticMessages(ElaborateNeverIntersection(errorInfo, containingType), Diagnostics.Property_0_does_not_exist_on_type_1, missingProperty, container);
                }
            }
        }
        var resultDiagnostic = CreateDiagnosticForNodeFromMessageChain(propNode, errorInfo);
        if (relatedInfo is not null)
        {
            AddRelatedInfo(resultDiagnostic, relatedInfo);
        }
        AddErrorOrSuggestion(!isUncheckedJS || errorInfo!.Code != Diagnostics.Property_0_may_not_exist_on_type_1_Did_you_mean_2.Code, resultDiagnostic);
    }

    // checker.ts typeHasStaticProperty
    private bool TypeHasStaticProperty(string propName, Type containingType)
    {
        var symbol = Types.Symbol(containingType);
        if (symbol.IsNone)
        {
            return false;
        }
        var prop = GetPropertyOfType(GetTypeOfSymbol(symbol), propName);
        if (prop.IsNone)
        {
            return false;
        }
        var valueDeclaration = ValueDeclarationOf(prop);
        return !valueDeclaration.IsNone && Utilities.IsStatic(StoreOf(valueDeclaration), valueDeclaration.Node);
    }

    // checker.ts getSuggestedSymbolForNonexistentProperty
    private SymbolRef GetSuggestedSymbolForNonexistentProperty(NodeRef name, Type containingType)
    {
        var props = GetPropertiesOfType(containingType);
        var store = StoreOf(name);
        var parent = At(name, store.Parent(name.Node));
        if (store.Kind(parent.Node) == SyntaxKind.PropertyAccessExpression)
        {
            props = props.FindAll(prop => IsValidPropertyAccessForCompletions(parent, containingType, prop));
        }
        return GetSpellingSuggestionForName(store.Texts.GetString(Utilities.IdText(store, name.Node))!, props, SymbolFlags.Value);
    }

    // checker.ts getSuggestedSymbolForNonexistentProperty with a name string
    private SymbolRef GetSuggestedSymbolForNonexistentProperty(string name, Type containingType) =>
        GetSpellingSuggestionForName(name, GetPropertiesOfType(containingType), SymbolFlags.Value);

    // checker.ts isValidPropertyAccessForCompletions / isPropertyAccessible
    private bool IsValidPropertyAccessForCompletions(NodeRef node, Type type, SymbolRef property)
    {
        var store = StoreOf(node);
        var isSuper = store.Kind(node.Node) == SyntaxKind.PropertyAccessExpression && store.Kind(store.Expression(node.Node)) == SyntaxKind.SuperKeyword;
        // Short-circuiting for improved performance.
        if ((Types.Flags(type) & TypeFlags.Any) != 0)
        {
            return true;
        }
        // A #private property access in an optional chain is an error dealt with by the parser.
        // The checker does not check for it, so we need to do our own check here.
        var valueDeclaration = ValueDeclarationOf(property);
        if (!valueDeclaration.IsNone && IsPrivateIdentifierClassElementDeclaration(valueDeclaration))
        {
            var declClass = At(valueDeclaration, Utilities.GetContainingClass(StoreOf(valueDeclaration), valueDeclaration.Node));
            return !UtilitiesPublic.IsOptionalChain(store, node.Node) && IsNodeDescendantOf(node, declClass);
        }
        return CheckPropertyAccessibilityAtLocation(node, isSuper, writing: false, type, property, NodeRef.None);
    }

    // checker.ts getSuggestionForNonexistentIndexSignature
    private string? GetSuggestionForNonexistentIndexSignature(Type objectType, NodeRef expr, Type keyedType)
    {
        var store = StoreOf(expr);
        var suggestedMethod = Utilities.IsAssignmentTarget(store, expr.Node) ? "set" : "get";
        // check if object type has setter or getter
        var prop = GetPropertyOfObjectType(objectType, suggestedMethod);
        var hasProp = false;
        if (!prop.IsNone)
        {
            var s = GetSingleCallSignature(GetTypeOfSymbol(prop));
            hasProp = !s.IsNone && GetMinArgumentCount(s) >= 1 && IsTypeAssignableTo(keyedType, GetTypeAtPosition(s, 0));
        }
        if (!hasProp)
        {
            return null;
        }
        var suggestion = TryGetPropertyAccessOrIdentifierToString(At(expr, store.Expression(expr.Node)));
        return suggestion is null ? suggestedMethod : suggestion + "." + suggestedMethod;
    }

    // utilities.ts tryGetPropertyAccessOrIdentifierToString
    private string? TryGetPropertyAccessOrIdentifierToString(NodeRef expr)
    {
        var store = StoreOf(expr);
        switch (store.Kind(expr.Node))
        {
            case SyntaxKind.PropertyAccessExpression:
            {
                var baseStr = TryGetPropertyAccessOrIdentifierToString(At(expr, store.Expression(expr.Node)));
                if (baseStr is not null)
                {
                    return baseStr + "." + EntityNameToString(At(expr, store.Name(expr.Node)));
                }
                return null;
            }
            case SyntaxKind.ElementAccessExpression:
            {
                var baseStr = TryGetPropertyAccessOrIdentifierToString(At(expr, store.Expression(expr.Node)));
                if (baseStr is not null && UtilitiesPublic.IsPropertyName(store, store.ArgumentExpression(expr.Node)))
                {
                    return baseStr + "." + GetPropertyNameForPropertyNameNode(store, store.ArgumentExpression(expr.Node));
                }
                return null;
            }
            case SyntaxKind.Identifier:
                return store.Texts.GetString(Utilities.IdText(store, expr.Node));
        }
        return null;
    }

    // checker.ts markPropertyAsReferenced's isSelfTypeAccess
    private bool IsSelfTypeAccess(NodeRef name, SymbolRef parent)
    {
        var store = StoreOf(name);
        if (store.Kind(name.Node) == SyntaxKind.ThisKeyword)
        {
            return true;
        }
        return !parent.IsNone && Utilities.IsEntityNameExpression(store, name.Node) && parent == GetResolvedSymbol(At(name, Utilities.GetFirstIdentifier(store, name.Node)));
    }

    // ---- accessibility ----

    /// <summary>checker.ts checkPropertyAccessibility.</summary>
    internal bool CheckPropertyAccessibility(NodeRef node, bool isSuper, bool writing, Type type, SymbolRef prop, bool reportError = true)
    {
        var store = StoreOf(node);
        var errorNode = NodeRef.None;
        if (reportError)
        {
            errorNode = store.Kind(node.Node) switch
            {
                SyntaxKind.QualifiedName => At(node, store.Right(node.Node)),
                SyntaxKind.ImportType => node,
                SyntaxKind.BindingElement when !store.PropertyName(node.Node).IsNone => At(node, store.PropertyName(node.Node)),
                _ => At(node, store.Name(node.Node)),
            };
        }
        return CheckPropertyAccessibilityAtLocation(node, isSuper, writing, type, prop, errorNode);
    }

    // checker.ts checkPropertyAccessibilityAtLocation
    private bool CheckPropertyAccessibilityAtLocation(NodeRef location, bool isSuper, bool writing, Type containingType, SymbolRef prop, NodeRef errorNode)
    {
        var flags = GetDeclarationModifierFlagsFromSymbol(prop, writing);
        var store = StoreOf(location);
        if (isSuper)
        {
            // TS 1.0 spec (April 2014): 4.8.2
            // - In a constructor, instance member function, instance member accessor, or
            //   instance member variable initializer where this references a derived class instance,
            //   a super property access is permitted and must specify a public instance member function of the base class.
            // - In a static member function or static member accessor
            //   where this references the constructor function object of a derived class,
            //   a super property access is permitted and must specify a public static member function of the base class.
            if (languageVersion < ScriptTarget.ES2015 && SymbolHasNonMethodDeclaration(prop))
            {
                if (!errorNode.IsNone)
                {
                    Error(errorNode, Diagnostics.Only_public_and_protected_methods_of_the_base_class_are_accessible_via_the_super_keyword);
                }
                return false;
            }
            if ((flags & ModifierFlags.Abstract) != 0)
            {
                // A method cannot be accessed in a super property access if the method is abstract.
                // This error could mask a private property access error. But, a member
                // cannot simultaneously be private and abstract, so this will trigger an
                // additional error elsewhere.
                if (!errorNode.IsNone)
                {
                    Error(errorNode, Diagnostics.Abstract_method_0_in_class_1_cannot_be_accessed_via_super_expression, SymbolToString(prop), TypeToString(GetDeclaringClass(prop)));
                }
                return false;
            }
            // A class field cannot be accessed via super.* from a derived class.
            // This is true for both [[Set]] (old) and [[Define]] (ES spec) semantics.
            if ((flags & ModifierFlags.Static) == 0 && (DeclarationsOf(prop)?.Exists(IsClassInstanceProperty) ?? false))
            {
                if (!errorNode.IsNone)
                {
                    Error(errorNode, Diagnostics.Class_field_0_defined_by_the_parent_class_is_not_accessible_in_the_child_class_via_super, SymbolToString(prop));
                }
                return false;
            }
        }
        // Referencing abstract properties within their own constructors is not allowed
        if ((flags & ModifierFlags.Abstract) != 0 && SymbolHasNonMethodDeclaration(prop) &&
            (IsThisProperty(store, location.Node) || IsThisInitializedObjectBindingExpression(store, location.Node) ||
             (store.Kind(store.Parent(location.Node)) == SyntaxKind.ObjectBindingPattern && IsThisInitializedDeclaration(store, store.Parent(store.Parent(location.Node))))))
        {
            var parentSymbol = GetParentOfSymbol(prop);
            if (!parentSymbol.IsNone && (SymbolFlagsOf(parentSymbol) & SymbolFlags.Class) != 0 && IsNodeUsedDuringClassInitialization(store, location.Node))
            {
                if (!errorNode.IsNone)
                {
                    Error(errorNode, Diagnostics.Abstract_property_0_in_class_1_cannot_be_accessed_in_the_constructor, SymbolToString(prop), SymbolToString(parentSymbol));
                }
                return false;
            }
        }
        // Public properties are otherwise accessible.
        if ((flags & ModifierFlags.NonPublicAccessibilityModifier) == 0)
        {
            return true;
        }
        // Property is known to be private or protected at this point
        // Private property is accessible if the property is within the declaring class
        if ((flags & ModifierFlags.Private) != 0)
        {
            var declaringClassDeclaration = GetClassLikeDeclarationOfSymbol(GetParentOfSymbol(prop));
            if (!IsNodeWithinClass(location, declaringClassDeclaration))
            {
                if (!errorNode.IsNone)
                {
                    Error(errorNode, Diagnostics.Property_0_is_private_and_only_accessible_within_class_1, SymbolToString(prop), TypeToString(GetDeclaringClass(prop)));
                }
                return false;
            }
            return true;
        }
        // Property is known to be protected at this point
        // All protected properties of a supertype are accessible in a super access
        if (isSuper)
        {
            return true;
        }
        // Find the first enclosing class that has the declaring classes of the protected constituents
        // of the property as base classes
        var enclosingClass = Type.None;
        for (var containingClass = Utilities.GetContainingClass(store, location.Node); !containingClass.IsNone; containingClass = Utilities.GetContainingClass(store, containingClass))
        {
            var candidate = GetDeclaredTypeOfSymbol(GetSymbolOfDeclaration(At(location, containingClass)));
            enclosingClass = IsClassDerivedFromDeclaringClasses(candidate, prop, writing);
            if (!enclosingClass.IsNone)
            {
                break;
            }
        }
        // A protected property is accessible if the property is within the declaring class or classes derived from it
        if (enclosingClass.IsNone)
        {
            // allow PropertyAccessibility if context is in function with this parameter
            // static member access is disallowed
            enclosingClass = GetEnclosingClassFromThisParameter(location);
            enclosingClass = enclosingClass.IsNone ? Type.None : IsClassDerivedFromDeclaringClasses(enclosingClass, prop, writing);
            if ((flags & ModifierFlags.Static) != 0 || enclosingClass.IsNone)
            {
                if (!errorNode.IsNone)
                {
                    var declaringClass = GetDeclaringClass(prop);
                    Error(errorNode, Diagnostics.Property_0_is_protected_and_only_accessible_within_class_1_and_its_subclasses, SymbolToString(prop), TypeToString(declaringClass.IsNone ? containingType : declaringClass));
                }
                return false;
            }
        }
        // No further restrictions for static properties
        if ((flags & ModifierFlags.Static) != 0)
        {
            return true;
        }
        if ((Types.Flags(containingType) & TypeFlags.TypeParameter) != 0)
        {
            // get the original type -- represented as the type constraint of the 'this' type
            containingType = Types.TypeParameter(containingType).IsThisType ? GetConstraintOfTypeParameter(containingType) : GetBaseConstraintOfType(containingType);
        }
        if (containingType.IsNone || !HasBaseType(containingType, enclosingClass))
        {
            if (!errorNode.IsNone)
            {
                Error(errorNode, Diagnostics.Property_0_is_protected_and_only_accessible_through_an_instance_of_class_1_This_is_an_instance_of_class_2,
                    SymbolToString(prop), TypeToString(enclosingClass), TypeToString(containingType));
            }
            return false;
        }
        return true;
    }

    // utilities.ts isThisInitializedDeclaration
    private static bool IsThisInitializedDeclaration(NodeStore store, Node node) =>
        !node.IsNone && store.Kind(node) == SyntaxKind.VariableDeclaration && !store.Initializer(node).IsNone && store.Kind(store.Initializer(node)) == SyntaxKind.ThisKeyword;

    // utilities.ts isThisInitializedObjectBindingExpression
    private static bool IsThisInitializedObjectBindingExpression(NodeStore store, Node node)
    {
        if (node.IsNone || store.Kind(node) is not (SyntaxKind.ShorthandPropertyAssignment or SyntaxKind.PropertyAssignment))
        {
            return false;
        }
        var grandParent = store.Parent(store.Parent(node));
        return store.Kind(grandParent) == SyntaxKind.BinaryExpression && store.Kind(store.OperatorToken(grandParent)) == SyntaxKind.EqualsToken &&
            store.Kind(store.Right(grandParent)) == SyntaxKind.ThisKeyword;
    }

    // utilities.ts isClassInstanceProperty (JavaScript expando properties: C9)
    private bool IsClassInstanceProperty(NodeRef node)
    {
        var store = StoreOf(node);
        return store.Kind(node.Node) == SyntaxKind.PropertyDeclaration && !HasAccessorModifier(node) &&!Utilities.IsStatic(store, node.Node);
    }

    // checker.ts isNodeUsedDuringClassInitialization
    private static bool IsNodeUsedDuringClassInitialization(NodeStore store, Node node)
    {
        for (var element = node; !element.IsNone; element = store.Parent(element))
        {
            if ((store.Kind(element) == SyntaxKind.Constructor && Utilities.NodeIsPresent(store, store.Body(element))) || store.Kind(element) == SyntaxKind.PropertyDeclaration)
            {
                return true;
            }
            if (Utilities.IsClassLike(store, element) || Utilities.IsFunctionLikeDeclaration(store, element))
            {
                return false;
            }
        }
        return false;
    }

    // checker.ts isNodeWithinClass
    private bool IsNodeWithinClass(NodeRef node, NodeRef classDeclaration)
    {
        var store = StoreOf(node);
        for (var containingClass = Utilities.GetContainingClass(store, node.Node); !containingClass.IsNone; containingClass = Utilities.GetContainingClass(store, containingClass))
        {
            if (At(node, containingClass) == classDeclaration)
            {
                return true;
            }
        }
        return false;
    }

    // utilities.ts getClassLikeDeclarationOfSymbol
    private NodeRef GetClassLikeDeclarationOfSymbol(SymbolRef symbol)
    {
        var declarations = symbol.IsNone ? null : DeclarationsOf(symbol);
        if (declarations is null)
        {
            return NodeRef.None;
        }
        // (module AR2, Track O: find as a loop, without a delegate per call; tsrs uses iter().find)
        foreach (var d in declarations.Value)
        {
            if (Utilities.IsClassLike(StoreOf(d), d.Node))
            {
                return d;
            }
        }
        return NodeRef.None;
    }

    // checker.ts symbolHasNonMethodDeclaration
    private bool SymbolHasNonMethodDeclaration(SymbolRef symbol) => ForEachProperty(symbol, prop => (SymbolFlagsOf(prop) & SymbolFlags.Method) == 0);

    // checker.ts isClassDerivedFromDeclaringClasses
    private Type IsClassDerivedFromDeclaringClasses(Type checkClass, SymbolRef prop, bool writing) =>
        ForEachProperty(prop, p => (GetDeclarationModifierFlagsFromSymbol(p, writing) & ModifierFlags.Protected) != 0 && !HasBaseType(checkClass, GetDeclaringClass(p)))
            ? Type.None
            : checkClass;

    // checker.ts getEnclosingClassFromThisParameter
    private Type GetEnclosingClassFromThisParameter(NodeRef node)
    {
        // 'this' type for a node comes from, in priority order...
        // 1. The type of a syntactic 'this' parameter in the enclosing function scope
        var store = StoreOf(node);
        var thisContainer = Utilities.GetThisContainer(store, node.Node, includeArrowFunctions: false, includeClassComputedPropertyName: false);
        var thisParameter = !thisContainer.IsNone && Utilities.IsFunctionLike(store, thisContainer) ? GetThisParameter(store, thisContainer) : Node.None;
        var thisType = !thisParameter.IsNone && !store.Type(thisParameter).IsNone ? GetTypeFromTypeNode(At(node, store.Type(thisParameter))) : Type.None;
        if (!thisType.IsNone)
        {
            // 2. The constraint of a type parameter used for an explicit 'this' parameter
            if ((Types.Flags(thisType) & TypeFlags.TypeParameter) != 0)
            {
                thisType = GetConstraintOfTypeParameter(thisType);
            }
        }
        else if (!thisContainer.IsNone && Utilities.IsFunctionLike(store, thisContainer))
        {
            // 3. The 'this' parameter of a contextual type
            thisType = GetContextualThisParameterType(At(node, thisContainer));
        }
        if (!thisType.IsNone && (Types.ObjectFlagsOf(thisType) & (ObjectFlags.ClassOrInterface | ObjectFlags.Reference)) != 0)
        {
            return GetTargetType(thisType);
        }
        return Type.None;
    }

    // checker.ts isAssignmentToReadonlyEntity
    private bool IsAssignmentToReadonlyEntity(NodeRef expr, SymbolRef symbol, AssignmentKind assignmentKind)
    {
        if (assignmentKind == AssignmentKind.None)
        {
            // no assigment means it doesn't matter whether the entity is readonly
            return false;
        }
        var store = StoreOf(expr);
        if (IsReadonlySymbol(symbol))
        {
            // Allow assignments to readonly properties within constructors of the same class declaration.
            if ((SymbolFlagsOf(symbol) & SymbolFlags.Property) != 0 && Utilities.IsAccessExpression(store, expr.Node) && store.Kind(store.Expression(expr.Node)) == SyntaxKind.ThisKeyword)
            {
                // Look for if this is the constructor for the class that `symbol` is a property of.
                var ctor = GetControlFlowContainer(expr);
                if (!(store.Kind(ctor.Node) == SyntaxKind.Constructor || IsJSConstructor(ctor)))
                {
                    return true;
                }
                var valueDeclaration = ValueDeclarationOf(symbol);
                if (!valueDeclaration.IsNone)
                {
                    var isAssignmentDeclaration = Kind(valueDeclaration) == SyntaxKind.BinaryExpression;
                    var isLocalPropertyDeclaration = Parent(ctor) == Parent(valueDeclaration);
                    var isLocalParameterProperty = ctor == Parent(valueDeclaration);
                    var parentValueDeclaration = ParentOf(symbol).IsNone ? NodeRef.None : ValueDeclarationOf(ParentOf(symbol));
                    var isLocalThisPropertyAssignment = isAssignmentDeclaration && parentValueDeclaration == Parent(ctor);
                    var isLocalThisPropertyAssignmentConstructorFunction = isAssignmentDeclaration && parentValueDeclaration == ctor;
                    var isWriteableSymbol = isLocalPropertyDeclaration || isLocalParameterProperty || isLocalThisPropertyAssignment || isLocalThisPropertyAssignmentConstructorFunction;
                    return !isWriteableSymbol;
                }
            }
            return true;
        }
        if (Utilities.IsAccessExpression(store, expr.Node))
        {
            // references through namespace import should be readonly
            var node = Utilities.SkipParentheses(store, store.Expression(expr.Node));
            if (store.Kind(node) == SyntaxKind.Identifier)
            {
                var nodeRef = At(expr, node);
                var resolved = SymbolNodeLinks.Has(nodeRef) ? SymbolNodeLinks.Get(nodeRef).ResolvedSymbol : SymbolRef.None;
                if (!resolved.IsNone && (SymbolFlagsOf(resolved) & SymbolFlags.Alias) != 0)
                {
                    var declaration = GetDeclarationOfAliasSymbol(resolved);
                    return !declaration.IsNone && Kind(declaration) == SyntaxKind.NamespaceImport;
                }
            }
        }
        return false;
    }

    // utilities.ts getControlFlowContainer
    private NodeRef GetControlFlowContainer(NodeRef node)
    {
        var store = StoreOf(node);
        for (var n = store.Parent(node.Node); !n.IsNone; n = store.Parent(n))
        {
            if ((Utilities.IsFunctionLike(store, n) && Utilities.GetImmediatelyInvokedFunctionExpression(store, n).IsNone) ||
                store.Kind(n) is SyntaxKind.ModuleBlock or SyntaxKind.SourceFile or SyntaxKind.PropertyDeclaration)
            {
                return At(node, n);
            }
        }
        return NodeRef.None;
    }

    // checker.ts isThisPropertyAccessInConstructor
    private bool IsThisPropertyAccessInConstructor(NodeRef node, SymbolRef prop)
    {
        if (!(IsConstructorDeclaredProperty(prop) || (IsThisProperty(StoreOf(node), node.Node) && IsAutoTypedProperty(prop))))
        {
            return false;
        }
        var store = StoreOf(node);
        var container = At(node, Utilities.GetThisContainer(store, node.Node, includeArrowFunctions: true, includeClassComputedPropertyName: false));
        return container == GetDeclaringConstructor(prop);
    }

    // checker.ts isAutoTypedProperty
    private bool IsAutoTypedProperty(SymbolRef symbol)
    {
        // A property is auto-typed when its declaration has no type annotation or initializer and we're in
        // noImplicitAny mode or a .js file.
        var declaration = ValueDeclarationOf(symbol);
        return !declaration.IsNone && Kind(declaration) == SyntaxKind.PropertyDeclaration && GetEffectiveTypeAnnotationNode(declaration).IsNone &&
            StoreOf(declaration).Initializer(declaration.Node).IsNone && (noImplicitAny || Utilities.IsInJSFile(StoreOf(declaration), declaration.Node));
    }

    // ---- element access ----

    // checker.ts checkIndexedAccess / checkElementAccessChain
    private Type CheckIndexedAccess(NodeRef node, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var expression = At(node, store.Expression(node.Node));
        if ((store.Flags(node.Node) & NodeFlags.OptionalChain) != 0)
        {
            var exprType = CheckExpression(expression);
            var nonOptionalType = GetOptionalExpressionType(exprType, expression);
            return PropagateOptionalTypeMarker(CheckElementAccessExpression(node, CheckNonNullType(nonOptionalType, expression), checkMode), node, nonOptionalType != exprType);
        }
        return CheckElementAccessExpression(node, CheckNonNullExpression(expression), checkMode);
    }

    // checker.ts checkElementAccessExpression
    private Type CheckElementAccessExpression(NodeRef node, Type exprType, CheckMode checkMode)
    {
        var store = StoreOf(node);
        var objectType = GetAssignmentTargetKind(node) != AssignmentKind.None || IsMethodAccessForCall(store, node.Node) ? GetWidenedType(exprType) : exprType;
        var indexExpression = At(node, store.ArgumentExpression(node.Node));
        var indexType = CheckExpression(indexExpression);
        if (IsErrorType(objectType) || objectType == silentNeverType)
        {
            return objectType;
        }
        if (IsConstEnumObjectType(objectType) && !Utilities.IsStringLiteralLike(store, indexExpression.Node))
        {
            Error(indexExpression, Diagnostics.A_const_enum_member_can_only_be_accessed_using_a_string_literal);
            return errorType;
        }
        var effectiveIndexType = IsForInVariableForNumericPropertyNames(indexExpression) ? numberType : indexType;
        var assignmentTargetKind = GetAssignmentTargetKind(node);
        AccessFlags accessFlags;
        if (assignmentTargetKind == AssignmentKind.None)
        {
            accessFlags = AccessFlags.ExpressionPosition;
        }
        else
        {
            accessFlags = AccessFlags.Writing | (IsGenericObjectType(objectType) && !IsThisTypeParameter(objectType) ? AccessFlags.NoIndexSignatures : AccessFlags.None);
            if (assignmentTargetKind == AssignmentKind.Compound)
            {
                accessFlags |= AccessFlags.ExpressionPosition;
            }
        }
        var indexedAccessType = GetIndexedAccessTypeOrUndefined(objectType, effectiveIndexType, accessFlags, node);
        if (indexedAccessType.IsNone)
        {
            indexedAccessType = errorType;
        }
        var resolved = SymbolNodeLinks.Has(node) ? SymbolNodeLinks.Get(node).ResolvedSymbol : SymbolRef.None;
        return CheckIndexedAccessIndexType(GetFlowTypeOfAccessExpression(node, resolved, indexedAccessType, indexExpression, checkMode), node);
    }

    // checker.ts checkIndexedAccessIndexType
    private Type CheckIndexedAccessIndexType(Type type, NodeRef accessNode)
    {
        if ((Types.Flags(type) & TypeFlags.IndexedAccess) == 0)
        {
            return type;
        }
        // Check if the index type is assignable to 'keyof T' for the object type.
        var data = Types.Data<IndexedAccessData>(type);
        var objectType = data.ObjectType;
        var indexType = data.IndexType;
        var hasNumberIndexInfo = GetIndexInfoOfType(objectType, numberType) is not null;
        if (EveryType(indexType, t => IsTypeAssignableTo(t, GetIndexType(objectType, IndexFlags.None)) || (hasNumberIndexInfo && IsApplicableIndexType(t, numberType))))
        {
            var store = StoreOf(accessNode);
            if (store.Kind(accessNode.Node) == SyntaxKind.ElementAccessExpression && Utilities.IsAssignmentTarget(store, accessNode.Node) &&
                (Types.ObjectFlagsOf(objectType) & ObjectFlags.Mapped) != 0 && (GetMappedTypeModifiers(objectType) & MappedTypeModifiers.IncludeReadonly) != 0)
            {
                Error(accessNode, Diagnostics.Index_signature_in_type_0_only_permits_reading, TypeToString(objectType));
            }
            return type;
        }
        if (IsGenericObjectType(objectType))
        {
            var propertyName = GetPropertyNameFromIndex(indexType, accessNode);
            if (propertyName is not null)
            {
                var propertySymbol = SymbolRef.None;
                ForEachType(GetApparentType(objectType), t =>
                {
                    if (propertySymbol.IsNone)
                    {
                        propertySymbol = GetPropertyOfType(t, propertyName);
                    }
                });
                if (!propertySymbol.IsNone && (GetDeclarationModifierFlagsFromSymbol(propertySymbol) & ModifierFlags.NonPublicAccessibilityModifier) != 0)
                {
                    Error(accessNode, Diagnostics.Private_or_protected_member_0_cannot_be_accessed_on_a_type_parameter, Utilities.UnescapeLeadingUnderscores(propertyName));
                    return errorType;
                }
            }
        }
        // (TODO(C4): the relation decides whether the index type is assignable; the seam assumes it is when unsure)
        Error(accessNode, Diagnostics.Type_0_cannot_be_used_to_index_type_1, TypeToString(indexType), TypeToString(objectType));
        return errorType;
    }

    // checker.ts isForInVariableForNumericPropertyNames
    private bool IsForInVariableForNumericPropertyNames(NodeRef expr)
    {
        var store = StoreOf(expr);
        var e = Utilities.SkipParentheses(store, expr.Node);
        if (store.Kind(e) != SyntaxKind.Identifier)
        {
            return false;
        }
        var symbol = GetResolvedSymbol(At(expr, e));
        if ((SymbolFlagsOf(symbol) & SymbolFlags.Variable) == 0)
        {
            return false;
        }
        var child = expr.Node;
        var node = store.Parent(expr.Node);
        while (!node.IsNone)
        {
            if (store.Kind(node) == SyntaxKind.ForInStatement && child == store.Statement(node) && GetForInVariableSymbol(At(expr, node)) == symbol &&
                HasNumericPropertyNames(GetTypeOfExpression(At(expr, store.Expression(node)))))
            {
                return true;
            }
            child = node;
            node = store.Parent(node);
        }
        return false;
    }

    // checker.ts getForInVariableSymbol
    private SymbolRef GetForInVariableSymbol(NodeRef node)
    {
        var store = StoreOf(node);
        var initializer = store.Initializer(node.Node);
        if (store.Kind(initializer) == SyntaxKind.VariableDeclarationList)
        {
            var declarations = store.Nodes(store.Declarations(initializer));
            if (declarations.Length > 0 && !Utilities.IsBindingPattern(store, store.Name(declarations[0])))
            {
                return GetSymbolOfDeclaration(At(node, declarations[0]));
            }
        }
        else if (store.Kind(initializer) == SyntaxKind.Identifier)
        {
            return GetResolvedSymbol(At(node, initializer));
        }
        return SymbolRef.None;
    }

    // checker.ts hasNumericPropertyNames
    private bool HasNumericPropertyNames(Type type) => GetIndexInfosOfType(type).Count == 1 && GetIndexInfoOfType(type, numberType) is not null;

    // checker.ts getPropertyTypeForIndexType's errorIfWritingToReadonlyIndex
    private void ErrorIfWritingToReadonlyIndex(IndexInfo? indexInfo, NodeRef accessExpression, AccessFlags accessFlags, Type objectType)
    {
        _ = accessFlags;
        if (indexInfo is not null && indexInfo.IsReadonly && !accessExpression.IsNone &&
            (Utilities.IsAssignmentTarget(StoreOf(accessExpression), accessExpression.Node) || IsDeleteTarget(StoreOf(accessExpression), accessExpression.Node)))
        {
            Error(accessExpression, Diagnostics.Index_signature_in_type_0_only_permits_reading, TypeToString(objectType));
        }
    }

    // checker.ts getPropertyTypeForIndexType: the diagnostics of an element access expression whose index has no
    // applicable index signature (resultType: the type returned instead of undefined)
    private void ReportElementAccessIndexError(NodeRef accessExpression, Type objectType, Type indexType, Type fullIndexType, string? propName, AccessFlags accessFlags, out Type resultType)
    {
        resultType = Type.None;
        var store = StoreOf(accessExpression);
        var argumentExpression = At(accessExpression, store.ArgumentExpression(accessExpression.Node));
        if (IsObjectLiteralType(objectType))
        {
            if (noImplicitAny && (Types.Flags(indexType) & (TypeFlags.StringLiteral | TypeFlags.NumberLiteral)) != 0)
            {
                var value = Types.Literal(indexType).Value;
                diagnostics.Add(CreateDiagnosticForNode(accessExpression, Diagnostics.Property_0_does_not_exist_on_type_1, value is double d ? JsNumber.ToString(d) : (string)value!, TypeToString(objectType)));
                resultType = undefinedType;
                return;
            }
            if ((Types.Flags(indexType) & (TypeFlags.Number | TypeFlags.String)) != 0)
            {
                var types = Types.View(ResolveStructuredTypeMembers(objectType).Properties).ConvertAll(GetTypeOfSymbol);
                types.Add(undefinedType);
                resultType = GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(types));
                return;
            }
        }
        var globalThisMember = propName is null ? SymbolRef.None : globals.Get(propName);
        if (Types.Symbol(objectType) == globalThisSymbol && propName is not null && !globalThisMember.IsNone && (SymbolFlagsOf(globalThisMember) & SymbolFlags.BlockScoped) != 0)
        {
            Error(accessExpression, Diagnostics.Property_0_does_not_exist_on_type_1, Utilities.UnescapeLeadingUnderscores(propName), TypeToString(objectType));
        }
        else if (noImplicitAny && (accessFlags & AccessFlags.SuppressNoImplicitAnyError) == 0)
        {
            if (propName is not null && TypeHasStaticProperty(propName, objectType))
            {
                var typeName = TypeToString(objectType);
                Error(accessExpression, Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_to_access_the_static_member_2_instead, propName, typeName, typeName + "[" + GetTextOfNode(argumentExpression) + "]");
            }
            else if (!GetIndexTypeOfType(objectType, numberType).IsNone)
            {
                Error(argumentExpression, Diagnostics.Element_implicitly_has_an_any_type_because_index_expression_is_not_of_type_number);
            }
            else
            {
                var suggestion = propName is null ? SymbolRef.None : GetSuggestedSymbolForNonexistentProperty(propName, objectType);
                if (!suggestion.IsNone)
                {
                    Error(argumentExpression, Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_2, propName, TypeToString(objectType), SymbolName(suggestion));
                }
                else
                {
                    var indexSuggestion = GetSuggestionForNonexistentIndexSignature(objectType, accessExpression, indexType);
                    if (indexSuggestion is not null)
                    {
                        Error(accessExpression, Diagnostics.Element_implicitly_has_an_any_type_because_type_0_has_no_index_signature_Did_you_mean_to_call_1, TypeToString(objectType), indexSuggestion);
                    }
                    else
                    {
                        DiagnosticMessageChain? errorInfo = null;
                        var indexFlags = Types.Flags(indexType);
                        if ((indexFlags & TypeFlags.EnumLiteral) != 0)
                        {
                            errorInfo = ChainDiagnosticMessages(null, Diagnostics.Property_0_does_not_exist_on_type_1, "[" + TypeToString(indexType) + "]", TypeToString(objectType));
                        }
                        else if ((indexFlags & TypeFlags.UniqueESSymbol) != 0)
                        {
                            var symbolName = GetFullyQualifiedName(Types.Symbol(indexType), accessExpression);
                            errorInfo = ChainDiagnosticMessages(null, Diagnostics.Property_0_does_not_exist_on_type_1, "[" + symbolName + "]", TypeToString(objectType));
                        }
                        else if ((indexFlags & TypeFlags.StringLiteral) != 0)
                        {
                            errorInfo = ChainDiagnosticMessages(null, Diagnostics.Property_0_does_not_exist_on_type_1, (string)Types.Literal(indexType).Value!, TypeToString(objectType));
                        }
                        else if ((indexFlags & TypeFlags.NumberLiteral) != 0)
                        {
                            errorInfo = ChainDiagnosticMessages(null, Diagnostics.Property_0_does_not_exist_on_type_1, JsNumber.ToString((double)Types.Literal(indexType).Value!), TypeToString(objectType));
                        }
                        else if ((indexFlags & (TypeFlags.Number | TypeFlags.String)) != 0)
                        {
                            errorInfo = ChainDiagnosticMessages(null, Diagnostics.No_index_signature_with_a_parameter_of_type_0_was_found_on_type_1, TypeToString(indexType), TypeToString(objectType));
                        }
                        errorInfo = ChainDiagnosticMessages(errorInfo, Diagnostics.Element_implicitly_has_an_any_type_because_expression_of_type_0_can_t_be_used_to_index_type_1,
                            TypeToString(fullIndexType), TypeToString(objectType));
                        diagnostics.Add(CreateDiagnosticForNodeFromMessageChain(accessExpression, errorInfo));
                    }
                }
            }
        }
    }
}
