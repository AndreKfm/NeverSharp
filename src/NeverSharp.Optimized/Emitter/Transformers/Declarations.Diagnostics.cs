// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/transformers/declarations/diagnostics.ts (canProduceDiagnostics,
// createGetSymbolAccessibilityDiagnosticForNodeName, createGetSymbolAccessibilityDiagnosticForNode)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/transformers/declarations/diagnostics.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D5. createGetIsolatedDeclarationErrors is left out
// (isolatedDeclarations is not a Phase 4 option).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class DeclarationsTransformer
{
    private bool CanProduceDiagnostics(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.BindingElement:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.CallSignature:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.Parameter:
            case SyntaxKind.TypeParameter:
            case SyntaxKind.ExpressionWithTypeArguments:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.Constructor:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.BinaryExpression:
            case SyntaxKind.JSDocTypedefTag:
            case SyntaxKind.JSDocCallbackTag:
            case SyntaxKind.JSDocEnumTag:
                return true;
        }
        return false;
    }

    private bool IsStatic(Node node) => Utilities.HasSyntacticModifier(store, node, ModifierFlags.Static);

    // (node as NamedDeclaration).name: undefined for the kinds without a name
    private Node NameOrNone(Node node) => store.Kind(node) is SyntaxKind.BinaryExpression or SyntaxKind.ElementAccessExpression or SyntaxKind.Constructor or
        SyntaxKind.CallSignature or SyntaxKind.ConstructSignature or SyntaxKind.IndexSignature
        ? Node.None
        : store.Name(node);

    private static bool IsJSDocTypeAlias(SyntaxKind kind) => kind is SyntaxKind.JSDocTypedefTag or SyntaxKind.JSDocCallbackTag or SyntaxKind.JSDocEnumTag;

    private Func<SymbolAccessibilityResult, SymbolAccessibilityDiagnostic?> CreateGetSymbolAccessibilityDiagnosticForNodeName(Node node)
    {
        var kind = store.Kind(node);
        if (kind is SyntaxKind.SetAccessor or SyntaxKind.GetAccessor)
        {
            return result => new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = GetAccessorNameVisibilityDiagnosticMessage(node, result),
                ErrorNode = node,
                TypeName = store.Name(node),
            };
        }
        if (kind is SyntaxKind.MethodSignature or SyntaxKind.MethodDeclaration)
        {
            return result => new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = GetMethodNameVisibilityDiagnosticMessage(node, result),
                ErrorNode = node,
                TypeName = store.Name(node),
            };
        }
        return CreateGetSymbolAccessibilityDiagnosticForNode(node);
    }

    private DiagnosticMessage GetAccessorNameVisibilityDiagnosticMessage(Node node, SymbolAccessibilityResult result)
    {
        if (IsStatic(node))
        {
            return result.ErrorModuleName is not null ?
                result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                    Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named :
                    Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_name_1_from_private_module_2 :
                Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_private_name_1;
        }
        if (store.Kind(store.Parent(node)) == SyntaxKind.ClassDeclaration)
        {
            return result.ErrorModuleName is not null ?
                result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                    Diagnostics.Public_property_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named :
                    Diagnostics.Public_property_0_of_exported_class_has_or_is_using_name_1_from_private_module_2 :
                Diagnostics.Public_property_0_of_exported_class_has_or_is_using_private_name_1;
        }
        return result.ErrorModuleName is not null ?
            Diagnostics.Property_0_of_exported_interface_has_or_is_using_name_1_from_private_module_2 :
            Diagnostics.Property_0_of_exported_interface_has_or_is_using_private_name_1;
    }

    private DiagnosticMessage GetMethodNameVisibilityDiagnosticMessage(Node node, SymbolAccessibilityResult result)
    {
        if (IsStatic(node))
        {
            return result.ErrorModuleName is not null ?
                result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                    Diagnostics.Public_static_method_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named :
                    Diagnostics.Public_static_method_0_of_exported_class_has_or_is_using_name_1_from_private_module_2 :
                Diagnostics.Public_static_method_0_of_exported_class_has_or_is_using_private_name_1;
        }
        if (store.Kind(store.Parent(node)) == SyntaxKind.ClassDeclaration)
        {
            return result.ErrorModuleName is not null ?
                result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                    Diagnostics.Public_method_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named :
                    Diagnostics.Public_method_0_of_exported_class_has_or_is_using_name_1_from_private_module_2 :
                Diagnostics.Public_method_0_of_exported_class_has_or_is_using_private_name_1;
        }
        return result.ErrorModuleName is not null ?
            Diagnostics.Method_0_of_exported_interface_has_or_is_using_name_1_from_private_module_2 :
            Diagnostics.Method_0_of_exported_interface_has_or_is_using_private_name_1;
    }

    private Func<SymbolAccessibilityResult, SymbolAccessibilityDiagnostic?> CreateGetSymbolAccessibilityDiagnosticForNode(Node node)
    {
        var kind = store.Kind(node);
        if (kind is SyntaxKind.VariableDeclaration or SyntaxKind.PropertyDeclaration or SyntaxKind.PropertySignature or SyntaxKind.PropertyAccessExpression or
            SyntaxKind.ElementAccessExpression or SyntaxKind.BinaryExpression or SyntaxKind.BindingElement or SyntaxKind.Constructor)
        {
            return result => GetVariableDeclarationTypeVisibilityError(node, result);
        }
        if (kind is SyntaxKind.SetAccessor or SyntaxKind.GetAccessor)
        {
            return result => GetAccessorDeclarationTypeVisibilityError(node, result);
        }
        if (kind is SyntaxKind.ConstructSignature or SyntaxKind.CallSignature or SyntaxKind.MethodDeclaration or SyntaxKind.MethodSignature or
            SyntaxKind.FunctionDeclaration or SyntaxKind.IndexSignature)
        {
            return result => GetReturnTypeVisibilityError(node, result);
        }
        if (kind == SyntaxKind.Parameter)
        {
            var parent = store.Parent(node);
            if (Utilities.HasSyntacticModifier(store, node, ModifierFlags.ParameterPropertyModifier) && store.Kind(parent) == SyntaxKind.Constructor &&
                Utilities.HasSyntacticModifier(store, parent, ModifierFlags.Private))
            {
                return result => GetVariableDeclarationTypeVisibilityError(node, result);
            }
            return result => GetParameterDeclarationTypeVisibilityError(node, result);
        }
        if (kind == SyntaxKind.TypeParameter)
        {
            return _ => GetTypeParameterConstraintVisibilityError(node);
        }
        if (kind == SyntaxKind.ExpressionWithTypeArguments)
        {
            return _ => GetHeritageClauseVisibilityError(node);
        }
        if (kind == SyntaxKind.ImportEqualsDeclaration)
        {
            return _ => new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = Diagnostics.Import_declaration_0_is_using_private_name_1,
                ErrorNode = node,
                TypeName = store.Name(node),
            };
        }
        if (kind == SyntaxKind.TypeAliasDeclaration || IsJSDocTypeAlias(kind))
        {
            return result => new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = result.ErrorModuleName is not null
                    ? Diagnostics.Exported_type_alias_0_has_or_is_using_private_name_1_from_module_2
                    : Diagnostics.Exported_type_alias_0_has_or_is_using_private_name_1,
                ErrorNode = IsJSDocTypeAlias(kind) ? store.TypeExpression(node) : store.Type(node),
                TypeName = IsJSDocTypeAlias(kind) ? Utilities.GetNameOfDeclaration(store, node) : store.Name(node),
            };
        }
        throw new InvalidOperationException("Debug Failure. Attempted to set a declaration diagnostic context for unhandled node kind: " + kind);
    }

    private DiagnosticMessage? GetVariableDeclarationTypeVisibilityDiagnosticMessage(Node node, SymbolAccessibilityResult result)
    {
        var kind = store.Kind(node);
        if (kind is SyntaxKind.VariableDeclaration or SyntaxKind.BindingElement)
        {
            return result.ErrorModuleName is not null ?
                result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                    Diagnostics.Exported_variable_0_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named :
                    Diagnostics.Exported_variable_0_has_or_is_using_name_1_from_private_module_2 :
                Diagnostics.Exported_variable_0_has_or_is_using_private_name_1;
        }
        // This check is to ensure we don't report error on constructor parameter property as that error would be reported during parameter emit
        // The only exception here is if the constructor was marked as private. we are not emitting the constructor parameters at all.
        if (kind is SyntaxKind.PropertyDeclaration or SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression or SyntaxKind.BinaryExpression or SyntaxKind.PropertySignature ||
            (kind == SyntaxKind.Parameter && Utilities.HasSyntacticModifier(store, store.Parent(node), ModifierFlags.Private)))
        {
            // TODO(jfreeman): Deal with computed properties in error reporting.
            if (IsStatic(node))
            {
                return result.ErrorModuleName is not null ?
                    result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                        Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named :
                        Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_name_1_from_private_module_2 :
                    Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_private_name_1;
            }
            if (store.Kind(store.Parent(node)) == SyntaxKind.ClassDeclaration || kind == SyntaxKind.Parameter)
            {
                return result.ErrorModuleName is not null ?
                    result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                        Diagnostics.Public_property_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named :
                        Diagnostics.Public_property_0_of_exported_class_has_or_is_using_name_1_from_private_module_2 :
                    Diagnostics.Public_property_0_of_exported_class_has_or_is_using_private_name_1;
            }
            // Interfaces cannot have types that cannot be named
            return result.ErrorModuleName is not null ?
                Diagnostics.Property_0_of_exported_interface_has_or_is_using_name_1_from_private_module_2 :
                Diagnostics.Property_0_of_exported_interface_has_or_is_using_private_name_1;
        }
        return null;
    }

    private SymbolAccessibilityDiagnostic? GetVariableDeclarationTypeVisibilityError(Node node, SymbolAccessibilityResult result)
    {
        var diagnosticMessage = GetVariableDeclarationTypeVisibilityDiagnosticMessage(node, result);
        return diagnosticMessage is not null ? new SymbolAccessibilityDiagnostic
        {
            DiagnosticMessage = diagnosticMessage,
            ErrorNode = node,
            TypeName = NameOrNone(node),
        } : null;
    }

    private SymbolAccessibilityDiagnostic GetAccessorDeclarationTypeVisibilityError(Node node, SymbolAccessibilityResult result)
    {
        DiagnosticMessage diagnosticMessage;
        if (store.Kind(node) == SyntaxKind.SetAccessor)
        {
            // Getters can infer the return type from the returned expression, but setters cannot, so the
            // "_from_external_module_1_but_cannot_be_named" case cannot occur.
            if (IsStatic(node))
            {
                diagnosticMessage = result.ErrorModuleName is not null ?
                    Diagnostics.Parameter_type_of_public_static_setter_0_from_exported_class_has_or_is_using_name_1_from_private_module_2 :
                    Diagnostics.Parameter_type_of_public_static_setter_0_from_exported_class_has_or_is_using_private_name_1;
            }
            else
            {
                diagnosticMessage = result.ErrorModuleName is not null ?
                    Diagnostics.Parameter_type_of_public_setter_0_from_exported_class_has_or_is_using_name_1_from_private_module_2 :
                    Diagnostics.Parameter_type_of_public_setter_0_from_exported_class_has_or_is_using_private_name_1;
            }
        }
        else
        {
            if (IsStatic(node))
            {
                diagnosticMessage = result.ErrorModuleName is not null ?
                    result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                        Diagnostics.Return_type_of_public_static_getter_0_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named :
                        Diagnostics.Return_type_of_public_static_getter_0_from_exported_class_has_or_is_using_name_1_from_private_module_2 :
                    Diagnostics.Return_type_of_public_static_getter_0_from_exported_class_has_or_is_using_private_name_1;
            }
            else
            {
                diagnosticMessage = result.ErrorModuleName is not null ?
                    result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                        Diagnostics.Return_type_of_public_getter_0_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named :
                        Diagnostics.Return_type_of_public_getter_0_from_exported_class_has_or_is_using_name_1_from_private_module_2 :
                    Diagnostics.Return_type_of_public_getter_0_from_exported_class_has_or_is_using_private_name_1;
            }
        }
        return new SymbolAccessibilityDiagnostic
        {
            DiagnosticMessage = diagnosticMessage,
            ErrorNode = store.Name(node),
            TypeName = store.Name(node),
        };
    }

    private SymbolAccessibilityDiagnostic GetReturnTypeVisibilityError(Node node, SymbolAccessibilityResult result)
    {
        DiagnosticMessage diagnosticMessage;
        switch (store.Kind(node))
        {
            case SyntaxKind.ConstructSignature:
                // Interfaces cannot have return types that cannot be named
                diagnosticMessage = result.ErrorModuleName is not null ?
                    Diagnostics.Return_type_of_constructor_signature_from_exported_interface_has_or_is_using_name_0_from_private_module_1 :
                    Diagnostics.Return_type_of_constructor_signature_from_exported_interface_has_or_is_using_private_name_0;
                break;
            case SyntaxKind.CallSignature:
                // Interfaces cannot have return types that cannot be named
                diagnosticMessage = result.ErrorModuleName is not null ?
                    Diagnostics.Return_type_of_call_signature_from_exported_interface_has_or_is_using_name_0_from_private_module_1 :
                    Diagnostics.Return_type_of_call_signature_from_exported_interface_has_or_is_using_private_name_0;
                break;
            case SyntaxKind.IndexSignature:
                // Interfaces cannot have return types that cannot be named
                diagnosticMessage = result.ErrorModuleName is not null ?
                    Diagnostics.Return_type_of_index_signature_from_exported_interface_has_or_is_using_name_0_from_private_module_1 :
                    Diagnostics.Return_type_of_index_signature_from_exported_interface_has_or_is_using_private_name_0;
                break;
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
                if (IsStatic(node))
                {
                    diagnosticMessage = result.ErrorModuleName is not null ?
                        result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                            Diagnostics.Return_type_of_public_static_method_from_exported_class_has_or_is_using_name_0_from_external_module_1_but_cannot_be_named :
                            Diagnostics.Return_type_of_public_static_method_from_exported_class_has_or_is_using_name_0_from_private_module_1 :
                        Diagnostics.Return_type_of_public_static_method_from_exported_class_has_or_is_using_private_name_0;
                }
                else if (store.Kind(store.Parent(node)) == SyntaxKind.ClassDeclaration)
                {
                    diagnosticMessage = result.ErrorModuleName is not null ?
                        result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                            Diagnostics.Return_type_of_public_method_from_exported_class_has_or_is_using_name_0_from_external_module_1_but_cannot_be_named :
                            Diagnostics.Return_type_of_public_method_from_exported_class_has_or_is_using_name_0_from_private_module_1 :
                        Diagnostics.Return_type_of_public_method_from_exported_class_has_or_is_using_private_name_0;
                }
                else
                {
                    // Interfaces cannot have return types that cannot be named
                    diagnosticMessage = result.ErrorModuleName is not null ?
                        Diagnostics.Return_type_of_method_from_exported_interface_has_or_is_using_name_0_from_private_module_1 :
                        Diagnostics.Return_type_of_method_from_exported_interface_has_or_is_using_private_name_0;
                }
                break;
            case SyntaxKind.FunctionDeclaration:
                diagnosticMessage = result.ErrorModuleName is not null ?
                    result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                        Diagnostics.Return_type_of_exported_function_has_or_is_using_name_0_from_external_module_1_but_cannot_be_named :
                        Diagnostics.Return_type_of_exported_function_has_or_is_using_name_0_from_private_module_1 :
                    Diagnostics.Return_type_of_exported_function_has_or_is_using_private_name_0;
                break;
            default:
                throw new InvalidOperationException("Debug Failure. This is unknown kind for signature: " + store.Kind(node));
        }
        var name = NameOrNone(node);
        return new SymbolAccessibilityDiagnostic
        {
            DiagnosticMessage = diagnosticMessage,
            ErrorNode = !name.IsNone ? name : node,
        };
    }

    private SymbolAccessibilityDiagnostic? GetParameterDeclarationTypeVisibilityError(Node node, SymbolAccessibilityResult result)
    {
        var diagnosticMessage = GetParameterDeclarationTypeVisibilityDiagnosticMessage(node, result);
        return new SymbolAccessibilityDiagnostic
        {
            DiagnosticMessage = diagnosticMessage,
            ErrorNode = node,
            TypeName = store.Name(node),
        };
    }

    private DiagnosticMessage GetParameterDeclarationTypeVisibilityDiagnosticMessage(Node node, SymbolAccessibilityResult result)
    {
        var parent = store.Parent(node);
        switch (store.Kind(parent))
        {
            case SyntaxKind.Constructor:
                return result.ErrorModuleName is not null ?
                    result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                        Diagnostics.Parameter_0_of_constructor_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named :
                        Diagnostics.Parameter_0_of_constructor_from_exported_class_has_or_is_using_name_1_from_private_module_2 :
                    Diagnostics.Parameter_0_of_constructor_from_exported_class_has_or_is_using_private_name_1;
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.ConstructorType:
                // Interfaces cannot have parameter types that cannot be named
                return result.ErrorModuleName is not null ?
                    Diagnostics.Parameter_0_of_constructor_signature_from_exported_interface_has_or_is_using_name_1_from_private_module_2 :
                    Diagnostics.Parameter_0_of_constructor_signature_from_exported_interface_has_or_is_using_private_name_1;
            case SyntaxKind.CallSignature:
                // Interfaces cannot have parameter types that cannot be named
                return result.ErrorModuleName is not null ?
                    Diagnostics.Parameter_0_of_call_signature_from_exported_interface_has_or_is_using_name_1_from_private_module_2 :
                    Diagnostics.Parameter_0_of_call_signature_from_exported_interface_has_or_is_using_private_name_1;
            case SyntaxKind.IndexSignature:
                // Interfaces cannot have parameter types that cannot be named
                return result.ErrorModuleName is not null ?
                    Diagnostics.Parameter_0_of_index_signature_from_exported_interface_has_or_is_using_name_1_from_private_module_2 :
                    Diagnostics.Parameter_0_of_index_signature_from_exported_interface_has_or_is_using_private_name_1;
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
                if (IsStatic(parent))
                {
                    return result.ErrorModuleName is not null ?
                        result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                            Diagnostics.Parameter_0_of_public_static_method_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named :
                            Diagnostics.Parameter_0_of_public_static_method_from_exported_class_has_or_is_using_name_1_from_private_module_2 :
                        Diagnostics.Parameter_0_of_public_static_method_from_exported_class_has_or_is_using_private_name_1;
                }
                if (store.Kind(store.Parent(parent)) == SyntaxKind.ClassDeclaration)
                {
                    return result.ErrorModuleName is not null ?
                        result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                            Diagnostics.Parameter_0_of_public_method_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named :
                            Diagnostics.Parameter_0_of_public_method_from_exported_class_has_or_is_using_name_1_from_private_module_2 :
                        Diagnostics.Parameter_0_of_public_method_from_exported_class_has_or_is_using_private_name_1;
                }
                // Interfaces cannot have parameter types that cannot be named
                return result.ErrorModuleName is not null ?
                    Diagnostics.Parameter_0_of_method_from_exported_interface_has_or_is_using_name_1_from_private_module_2 :
                    Diagnostics.Parameter_0_of_method_from_exported_interface_has_or_is_using_private_name_1;
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionType:
                return result.ErrorModuleName is not null ?
                    result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                        Diagnostics.Parameter_0_of_exported_function_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named :
                        Diagnostics.Parameter_0_of_exported_function_has_or_is_using_name_1_from_private_module_2 :
                    Diagnostics.Parameter_0_of_exported_function_has_or_is_using_private_name_1;
            case SyntaxKind.SetAccessor:
            case SyntaxKind.GetAccessor:
                return result.ErrorModuleName is not null ?
                    result.Accessibility == SymbolAccessibility.CannotBeNamed ?
                        Diagnostics.Parameter_0_of_accessor_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named :
                        Diagnostics.Parameter_0_of_accessor_has_or_is_using_name_1_from_private_module_2 :
                    Diagnostics.Parameter_0_of_accessor_has_or_is_using_private_name_1;
            default:
                throw new InvalidOperationException("Debug Failure. Unknown parent for parameter: " + store.Kind(parent));
        }
    }

    private SymbolAccessibilityDiagnostic GetTypeParameterConstraintVisibilityError(Node node)
    {
        // Type parameter constraints are named by user so we should always be able to name it
        DiagnosticMessage diagnosticMessage;
        var parent = store.Parent(node);
        switch (store.Kind(parent))
        {
            case SyntaxKind.ClassDeclaration:
                diagnosticMessage = Diagnostics.Type_parameter_0_of_exported_class_has_or_is_using_private_name_1;
                break;
            case SyntaxKind.InterfaceDeclaration:
                diagnosticMessage = Diagnostics.Type_parameter_0_of_exported_interface_has_or_is_using_private_name_1;
                break;
            case SyntaxKind.MappedType:
                diagnosticMessage = Diagnostics.Type_parameter_0_of_exported_mapped_object_type_is_using_private_name_1;
                break;
            case SyntaxKind.ConstructorType:
            case SyntaxKind.ConstructSignature:
                diagnosticMessage = Diagnostics.Type_parameter_0_of_constructor_signature_from_exported_interface_has_or_is_using_private_name_1;
                break;
            case SyntaxKind.CallSignature:
                diagnosticMessage = Diagnostics.Type_parameter_0_of_call_signature_from_exported_interface_has_or_is_using_private_name_1;
                break;
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
                if (IsStatic(parent))
                {
                    diagnosticMessage = Diagnostics.Type_parameter_0_of_public_static_method_from_exported_class_has_or_is_using_private_name_1;
                }
                else if (store.Kind(store.Parent(parent)) == SyntaxKind.ClassDeclaration)
                {
                    diagnosticMessage = Diagnostics.Type_parameter_0_of_public_method_from_exported_class_has_or_is_using_private_name_1;
                }
                else
                {
                    diagnosticMessage = Diagnostics.Type_parameter_0_of_method_from_exported_interface_has_or_is_using_private_name_1;
                }
                break;
            case SyntaxKind.FunctionType:
            case SyntaxKind.FunctionDeclaration:
                diagnosticMessage = Diagnostics.Type_parameter_0_of_exported_function_has_or_is_using_private_name_1;
                break;
            case SyntaxKind.InferType:
                diagnosticMessage = Diagnostics.Extends_clause_for_inferred_type_0_has_or_is_using_private_name_1;
                break;
            case SyntaxKind.TypeAliasDeclaration:
                diagnosticMessage = Diagnostics.Type_parameter_0_of_exported_type_alias_has_or_is_using_private_name_1;
                break;
            default:
                throw new InvalidOperationException("Debug Failure. This is unknown parent for type parameter: " + store.Kind(parent));
        }
        return new SymbolAccessibilityDiagnostic
        {
            DiagnosticMessage = diagnosticMessage,
            ErrorNode = node,
            TypeName = store.Name(node),
        };
    }

    private SymbolAccessibilityDiagnostic GetHeritageClauseVisibilityError(Node node)
    {
        DiagnosticMessage diagnosticMessage;
        var parent = store.Parent(node);
        var grandParent = store.Parent(parent);
        // Heritage clause is written by user so it can always be named
        if (store.Kind(grandParent) == SyntaxKind.ClassDeclaration)
        {
            // Class or Interface implemented/extended is inaccessible
            diagnosticMessage = store.Kind(parent) == SyntaxKind.HeritageClause && store.Token(parent) == SyntaxKind.ImplementsKeyword ?
                Diagnostics.Implements_clause_of_exported_class_0_has_or_is_using_private_name_1 :
                !store.Name(grandParent).IsNone ? Diagnostics.extends_clause_of_exported_class_0_has_or_is_using_private_name_1 :
                Diagnostics.extends_clause_of_exported_class_has_or_is_using_private_name_0;
        }
        else
        {
            // interface is inaccessible
            diagnosticMessage = Diagnostics.extends_clause_of_exported_interface_0_has_or_is_using_private_name_1;
        }
        return new SymbolAccessibilityDiagnostic
        {
            DiagnosticMessage = diagnosticMessage,
            ErrorNode = node,
            TypeName = Utilities.GetNameOfDeclaration(store, grandParent),
        };
    }
}
