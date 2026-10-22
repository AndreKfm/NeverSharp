// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/commandLineParser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/tsoptions/parsinghelpers.go and internal/tsoptions/tsconfigparsing.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>
/// types.ts: CommandLineOption, reduced to the members convertToJson and getCompilerOptionValueTypeString read
/// (`name`, `type`, and `element` of CommandLineOptionOfListType, `elementOptions` of TsConfigOnlyOption). The
/// parser calls convertToJson without a JsonConversionNotifier, so no option ever reaches these functions from it;
/// the option declarations of tsc's command line and tsconfig.json (commandLineParser.ts) are not ported. (TypeScript's
/// CommandLineOption is a union of interfaces; here one interface with the members read.)
/// </summary>
internal interface CommandLineOption
{
    string Name { get; }

    // TypeScript: "string" | "number" | "boolean" | "object" | "list" | "listOrElement" | Map<string, number | string>.
    object Type { get; }

    // CommandLineOptionOfListType.element
    CommandLineOption? Element { get; }

    // TsConfigOnlyOption.elementOptions
    IReadOnlyDictionary<string, CommandLineOption>? ElementOptions { get; }
}

/// <summary>commandLineParser.ts: JsonConversionNotifier.</summary>
internal interface JsonConversionNotifier
{
    CommandLineOption RootOptions { get; }

    void OnPropertySet(string keyText, object? value, PropertyAssignment propertyAssignment, CommandLineOption? parentOption, CommandLineOption? option);
}

/// <summary>commandLineParser.ts: convertToJson and getCompilerOptionValueTypeString.</summary>
internal static partial class CommandLineParser
{
    /// <summary>
    /// The JSON value null. A C# null is JavaScript's undefined in the values convertToJson returns (TypeScript filters
    /// undefined out of arrays but keeps null).
    /// </summary>
    internal static readonly object JsonNull = new();

    /// <summary>
    /// Convert the json syntax tree into the json value and report errors
    /// This returns the json value (apart from checking errors) only if returnValue provided is true.
    /// Otherwise it just checks the errors and returns undefined
    /// </summary>
    internal static object? ConvertToJson(
        SourceFile sourceFile,
        Node? rootExpression,
        List<DiagnosticWithLocation> errors,
        bool returnValue,
        JsonConversionNotifier? jsonConversionNotifier)
    {
        if (rootExpression == null)
        {
            return returnValue ? new Dictionary<string, object?>(StringComparer.Ordinal) : null;
        }

        return ConvertPropertyValueToJson(rootExpression, jsonConversionNotifier?.RootOptions);

        object? ConvertObjectLiteralExpressionToJson(
            ObjectLiteralExpression node,
            CommandLineOption? objectOption)
        {
            var result = returnValue ? new Dictionary<string, object?>(StringComparer.Ordinal) : null;
            foreach (var element in node.Properties)
            {
                if (element.Kind != SyntaxKind.PropertyAssignment)
                {
                    errors.Add(Utilities.CreateDiagnosticForNodeInSourceFile(sourceFile, element, Diagnostics.Property_assignment_expected));
                    continue;
                }

                var propertyAssignment = (PropertyAssignment)element;
                if (propertyAssignment.QuestionToken != null)
                {
                    errors.Add(Utilities.CreateDiagnosticForNodeInSourceFile(sourceFile, propertyAssignment.QuestionToken, Diagnostics.The_0_modifier_can_only_be_used_in_TypeScript_files, "?"));
                }
                if (!IsDoubleQuotedString(propertyAssignment.Name))
                {
                    errors.Add(Utilities.CreateDiagnosticForNodeInSourceFile(sourceFile, propertyAssignment.Name, Diagnostics.String_literal_with_double_quotes_expected));
                }

                var textOfKey = Utilities.IsComputedNonLiteralName(propertyAssignment.Name) ? null : Utilities.GetTextOfPropertyName(propertyAssignment.Name);
                // `textOfKey && unescapeLeadingUnderscores(textOfKey)`: the empty string stays "".
                var keyText = !string.IsNullOrEmpty(textOfKey) ? UtilitiesPublic.UnescapeLeadingUnderscores(textOfKey) : textOfKey;
                CommandLineOption? option = null;
                if (!string.IsNullOrEmpty(keyText) && objectOption?.ElementOptions != null && objectOption.ElementOptions.TryGetValue(keyText, out var elementOption))
                {
                    option = elementOption;
                }
                var value = ConvertPropertyValueToJson(propertyAssignment.Initializer, option);
                if (keyText != null)
                {
                    if (returnValue)
                    {
                        result![keyText] = value;
                    }

                    // Notify key value set, if user asked for it
                    jsonConversionNotifier?.OnPropertySet(keyText, value, propertyAssignment, objectOption, option);
                }
            }
            return result;
        }

        object? ConvertArrayLiteralExpressionToJson(
            NodeArray elements,
            CommandLineOption? elementOption)
        {
            if (!returnValue)
            {
                foreach (var element in elements)
                {
                    ConvertPropertyValueToJson(element, elementOption);
                }
                return null;
            }

            // Filter out invalid values
            var values = new List<object?>();
            foreach (var element in elements)
            {
                values.Add(ConvertPropertyValueToJson(element, elementOption));
            }
            return Core.Filter(values, v => v != null);
        }

        object? ConvertPropertyValueToJson(Node valueExpression, CommandLineOption? option)
        {
            switch (valueExpression.Kind)
            {
                case SyntaxKind.TrueKeyword:
                    return true;

                case SyntaxKind.FalseKeyword:
                    return false;

                case SyntaxKind.NullKeyword:
                    return JsonNull;

                case SyntaxKind.StringLiteral:
                    if (!IsDoubleQuotedString(valueExpression))
                    {
                        errors.Add(Utilities.CreateDiagnosticForNodeInSourceFile(sourceFile, valueExpression, Diagnostics.String_literal_with_double_quotes_expected));
                    }
                    return ((StringLiteral)valueExpression).Text;

                case SyntaxKind.NumericLiteral:
                    return JsNumber.FromString(((NumericLiteral)valueExpression).Text);

                case SyntaxKind.PrefixUnaryExpression:
                    if (((PrefixUnaryExpression)valueExpression).Operator != SyntaxKind.MinusToken || ((PrefixUnaryExpression)valueExpression).Operand.Kind != SyntaxKind.NumericLiteral)
                    {
                        break; // not valid JSON syntax
                    }
                    return -JsNumber.FromString(((NumericLiteral)((PrefixUnaryExpression)valueExpression).Operand).Text);

                case SyntaxKind.ObjectLiteralExpression:
                    var objectLiteralExpression = (ObjectLiteralExpression)valueExpression;

                    // Currently having element option declaration in the tsconfig with type "object"
                    // determines if it needs onSetValidOptionKeyValueInParent callback or not
                    // At moment there are only "compilerOptions", "typeAcquisition" and "typingOptions"
                    // that satisfies it and need it to modify options set in them (for normalizing file paths)
                    // vs what we set in the json
                    // If need arises, we can modify this interface and callbacks as needed
                    return ConvertObjectLiteralExpressionToJson(objectLiteralExpression, option);

                case SyntaxKind.ArrayLiteralExpression:
                    return ConvertArrayLiteralExpressionToJson(
                        ((ArrayLiteralExpression)valueExpression).Elements,
                        option?.Element);
            }

            // Not in expected format
            if (option != null)
            {
                errors.Add(Utilities.CreateDiagnosticForNodeInSourceFile(sourceFile, valueExpression, Diagnostics.Compiler_option_0_requires_a_value_of_type_1, option.Name, GetCompilerOptionValueTypeString(option)));
            }
            else
            {
                errors.Add(Utilities.CreateDiagnosticForNodeInSourceFile(sourceFile, valueExpression, Diagnostics.Property_value_can_only_be_string_literal_numeric_literal_true_false_null_object_literal_or_array_literal));
            }

            return null;
        }

        bool IsDoubleQuotedString(Node node)
        {
            return FactoryNodeTests.IsStringLiteral(node) && Utilities.IsStringDoubleQuoted(node, sourceFile);
        }
    }

    private static string GetCompilerOptionValueTypeString(CommandLineOption option)
    {
        return option.Type is string listOrElement && listOrElement == "listOrElement" ?
            $"{GetCompilerOptionValueTypeString(option.Element!)} or Array" :
            option.Type is string list && list == "list" ?
            "Array" :
            option.Type is string type ? type : "string";
    }
}
