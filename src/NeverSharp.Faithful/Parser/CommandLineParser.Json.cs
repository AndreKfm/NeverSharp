// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/commandLineParser.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/tsoptions/tsconfigparsing.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// types.ts: CommandLineOption (the members `convertToJson` reads). The option declarations themselves are not
/// part of the parser's modules; the parser passes none.
/// </summary>
internal interface ICommandLineOption
{
    string Name { get; }

    /// <summary>`"string" | "number" | "boolean" | "object" | "list" | "listOrElement" | Map&lt;string, number | string&gt;`.</summary>
    object Type { get; }
}

/// <summary>types.ts: TsConfigOnlyOption.</summary>
internal interface ITsConfigOnlyOption : ICommandLineOption
{
    IReadOnlyDictionary<string, ICommandLineOption>? ElementOptions { get; }
}

/// <summary>types.ts: CommandLineOptionOfListType.</summary>
internal interface ICommandLineOptionOfListType : ICommandLineOption
{
    ICommandLineOption Element { get; }
}

/// <summary>commandLineParser.ts: JsonConversionNotifier.</summary>
internal interface IJsonConversionNotifier
{
    ITsConfigOnlyOption RootOptions { get; }

    void OnPropertySet(
        string keyText,
        object? value,
        PropertyAssignment propertyAssignment,
        ITsConfigOnlyOption? parentOption,
        ICommandLineOption? option);
}

/// <summary>commandLineParser.ts: convertToJson and getCompilerOptionValueTypeString.</summary>
internal static partial class CommandLineParser
{
    /// <summary>
    /// The value of JavaScript's `undefined` inside the conversion (an invalid property value; distinct from the
    /// `null` of the JSON `null` literal, which array conversion keeps and `undefined` it filters out).
    /// </summary>
    private static readonly object JsUndefined = new();

    /// <summary>
    /// Convert the json syntax tree into the json value and report errors
    /// This returns the json value (apart from checking errors) only if returnValue provided is true.
    /// Otherwise it just checks the errors and returns undefined.
    /// The JavaScript value is a string, a double, a bool, null (also JavaScript's undefined), a
    /// Dictionary&lt;string, object?&gt; (object) or a List&lt;object?&gt; (array).
    /// </summary>
    internal static object? ConvertToJson(
        SourceFile sourceFile,
        Node? rootExpression,
        List<DiagnosticWithLocation> errors,
        bool returnValue,
        IJsonConversionNotifier? jsonConversionNotifier)
    {
        if (rootExpression == null)
        {
            return returnValue ? new Dictionary<string, object?>() : null;
        }

        var converted = ConvertPropertyValueToJson(rootExpression, jsonConversionNotifier?.RootOptions);
        return ReferenceEquals(converted, JsUndefined) ? null : converted;

        object ConvertObjectLiteralExpressionToJson(
            ObjectLiteralExpression node,
            ITsConfigOnlyOption? objectOption)
        {
            var result = returnValue ? new Dictionary<string, object?>() : null;
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
                var keyText = textOfKey == null ? null : UtilitiesPublic.UnescapeLeadingUnderscores(textOfKey);
                ICommandLineOption? option = null;
                if (!string.IsNullOrEmpty(keyText) && objectOption?.ElementOptions != null)
                {
                    objectOption.ElementOptions.TryGetValue(keyText, out option);
                }
                var value = ConvertPropertyValueToJson(propertyAssignment.Initializer, option);
                if (keyText != null)
                {
                    if (returnValue)
                    {
                        // JavaScript stores `undefined` as the property's value.
                        result![keyText] = ReferenceEquals(value, JsUndefined) ? null : value;
                    }

                    // Notify key value set, if user asked for it
                    jsonConversionNotifier?.OnPropertySet(keyText, ReferenceEquals(value, JsUndefined) ? null : value, propertyAssignment, objectOption, option);
                }
            }
            return (object?)result ?? JsUndefined;
        }

        object ConvertArrayLiteralExpressionToJson(
            NodeArray elements,
            ICommandLineOption? elementOption)
        {
            if (!returnValue)
            {
                foreach (var element in elements)
                {
                    ConvertPropertyValueToJson(element, elementOption);
                }
                return JsUndefined;
            }

            // Filter out invalid values
            var mapped = new List<object?>();
            foreach (var element in elements)
            {
                mapped.Add(ConvertPropertyValueToJson(element, elementOption));
            }
            var filtered = new List<object?>();
            foreach (var v in mapped)
            {
                if (!ReferenceEquals(v, JsUndefined))
                {
                    filtered.Add(v);
                }
            }
            return filtered;
        }

        object? ConvertPropertyValueToJson(Node valueExpression, ICommandLineOption? option)
        {
            switch (valueExpression.Kind)
            {
                case SyntaxKind.TrueKeyword:
                    return true;

                case SyntaxKind.FalseKeyword:
                    return false;

                case SyntaxKind.NullKeyword:
                    return null;

                case SyntaxKind.StringLiteral:
                    if (!IsDoubleQuotedString(valueExpression))
                    {
                        errors.Add(Utilities.CreateDiagnosticForNodeInSourceFile(sourceFile, valueExpression, Diagnostics.String_literal_with_double_quotes_expected));
                    }
                    return ((StringLiteral)valueExpression).Text;

                case SyntaxKind.NumericLiteral:
                    return Number(((NumericLiteral)valueExpression).Text);

                case SyntaxKind.PrefixUnaryExpression:
                    var prefixUnaryExpression = (PrefixUnaryExpression)valueExpression;
                    if (prefixUnaryExpression.Operator != SyntaxKind.MinusToken || prefixUnaryExpression.Operand.Kind != SyntaxKind.NumericLiteral)
                    {
                        break; // not valid JSON syntax
                    }
                    return -Number(((NumericLiteral)prefixUnaryExpression.Operand).Text);

                case SyntaxKind.ObjectLiteralExpression:
                    var objectLiteralExpression = (ObjectLiteralExpression)valueExpression;

                    // Currently having element option declaration in the tsconfig with type "object"
                    // determines if it needs onSetValidOptionKeyValueInParent callback or not
                    // At moment there are only "compilerOptions", "typeAcquisition" and "typingOptions"
                    // that satisfies it and need it to modify options set in them (for normalizing file paths)
                    // vs what we set in the json
                    // If need arises, we can modify this interface and callbacks as needed
                    return ConvertObjectLiteralExpressionToJson(objectLiteralExpression, option as ITsConfigOnlyOption);

                case SyntaxKind.ArrayLiteralExpression:
                    return ConvertArrayLiteralExpressionToJson(
                        ((ArrayLiteralExpression)valueExpression).Elements,
                        option == null ? null : (option as ICommandLineOptionOfListType)?.Element);
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

            return JsUndefined;
        }

        bool IsDoubleQuotedString(Node node)
        {
            return NodeTests.IsStringLiteral(node) && Utilities.IsStringDoubleQuoted(node, sourceFile);
        }
    }

    /// <summary>JavaScript's `Number(text)` of the text of a numeric literal.</summary>
    private static double Number(string text)
    {
        return string.Equals(text, "Infinity", StringComparison.Ordinal) ? double.PositiveInfinity : JsNumber.FromString(text);
    }

    private static string GetCompilerOptionValueTypeString(ICommandLineOption option)
    {
        var type = option.Type as string;
        return string.Equals(type, "listOrElement", StringComparison.Ordinal) ?
            GetCompilerOptionValueTypeString(((ICommandLineOptionOfListType)option).Element) + " or Array" :
            string.Equals(type, "list", StringComparison.Ordinal) ?
            "Array" :
            type ?? "string";
    }
}
