// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (createNodeBuilder: typeToTypeNodeHelper for tuple types, generic mapped types
// (createMappedTypeNodeFromType), index, indexed access, conditional, template literal, string mapping and substitution
// types) and the parenthesization rules of src/compiler/factory/parenthesizerRules.ts for these nodes
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Modified: translated from TypeScript to C#; module C3. Written as text in the style of C1's TypeToText (the node
// builder proper is module C2, which replaces this file's functions).

using System.Text;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // The text of the type kinds module C3 adds (None when the type is not one of them).
    private string? GenericTypeToText(Type type, NodeRef enclosingDeclaration, int depth)
    {
        var flags = Types.Flags(type);
        if ((flags & TypeFlags.Object) != 0)
        {
            if (IsTupleType(type))
            {
                return TupleTypeToText(type, enclosingDeclaration, depth);
            }
            if ((Types.ObjectFlagsOf(type) & ObjectFlags.Mapped) != 0 && (IsGenericMappedType(type) || Types.Object(type).ContainsError))
            {
                return MappedTypeToText(type, enclosingDeclaration, depth);
            }
            return null;
        }
        if ((flags & TypeFlags.Index) != 0)
        {
            var operand = Types.Data<IndexData>(type).Type;
            return "keyof " + ParenthesizeOperand(operand, ChildText(operand, enclosingDeclaration, depth));
        }
        if ((flags & TypeFlags.IndexedAccess) != 0)
        {
            var data = Types.Data<IndexedAccessData>(type);
            return ParenthesizePostfixOperand(data.ObjectType, ChildText(data.ObjectType, enclosingDeclaration, depth)) + "[" + ChildText(data.IndexType, enclosingDeclaration, depth) + "]";
        }
        if ((flags & TypeFlags.Conditional) != 0)
        {
            var data = Types.Data<ConditionalData>(type);
            var checkText = ChildText(data.CheckType, enclosingDeclaration, depth);
            if (IsFunctionOrConditionalText(data.CheckType, checkText))
            {
                checkText = "(" + checkText + ")";
            }
            var extendsText = ChildText(data.ExtendsType, enclosingDeclaration, depth);
            if ((Types.Flags(data.ExtendsType) & TypeFlags.Conditional) != 0 && Types.Record(data.ExtendsType).AliasSymbol.IsNone)
            {
                extendsText = "(" + extendsText + ")";
            }
            return checkText + " extends " + extendsText + " ? " + ChildText(GetTrueTypeFromConditionalType(type), enclosingDeclaration, depth) + " : " + ChildText(GetFalseTypeFromConditionalType(type), enclosingDeclaration, depth);
        }
        if ((flags & TypeFlags.TemplateLiteral) != 0)
        {
            var data = Types.Data<TemplateLiteralData>(type);
            var sb = new StringBuilder("`").Append(EscapeTemplateText(data.Texts[0]));
            for (var i = 0; i < data.Types.Length; i++)
            {
                sb.Append("${").Append(ChildText(data.Types[i], enclosingDeclaration, depth)).Append('}').Append(EscapeTemplateText(data.Texts[i + 1]));
            }
            return sb.Append('`').ToString();
        }
        if ((flags & TypeFlags.StringMapping) != 0)
        {
            return SymbolToTypeName(Types.Symbol(type), enclosingDeclaration) + "<" + ChildText(Types.Data<StringMappingData>(type).Type, enclosingDeclaration, depth) + ">";
        }
        if ((flags & TypeFlags.Substitution) != 0)
        {
            var data = Types.Data<SubstitutionData>(type);
            if (IsNoInferType(type))
            {
                var noInferSymbol = GetGlobalSymbol("NoInfer", SymbolFlags.Type, null);
                if (!noInferSymbol.IsNone)
                {
                    return SymbolToTypeName(noInferSymbol, enclosingDeclaration) + "<" + ChildText(data.BaseType, enclosingDeclaration, depth) + ">";
                }
            }
            return TypeToText(data.BaseType, enclosingDeclaration, false, false, depth);
        }
        return null;
    }

    private string ChildText(Type type, NodeRef enclosingDeclaration, int depth) => TypeToText(type, enclosingDeclaration, false, false, depth + 1);

    private static bool IsFunctionOrConditionalText(Type type, string text) =>
        text.Contains("=>", StringComparison.Ordinal) && !text.StartsWith('{') && !text.StartsWith('(') || text.Contains(" extends ", StringComparison.Ordinal) && !text.StartsWith('{');

    // parenthesizerRules.ts parenthesizeNonArrayTypeOfPostfixType: unions, intersections, functions, conditional types,
    // type operators and infer types are parenthesized before `[]`, `[K]` and `?`
    private string ParenthesizePostfixOperand(Type type, string text)
    {
        var flags = Types.Flags(type);
        var aliased = !Types.Record(type).AliasSymbol.IsNone;
        if (!aliased && (((flags & (TypeFlags.Union | TypeFlags.Intersection)) != 0 && (flags & TypeFlags.Boolean) == 0) || (flags & (TypeFlags.Conditional | TypeFlags.Index)) != 0))
        {
            return "(" + text + ")";
        }
        return text.Contains("=>", StringComparison.Ordinal) && !text.StartsWith('{') && !text.StartsWith('(') && !text.StartsWith('[') ? "(" + text + ")" : text;
    }

    // parenthesizerRules.ts parenthesizeOperandOfTypeOperator: unions, intersections, functions and conditional types
    private string ParenthesizeOperand(Type type, string text)
    {
        var flags = Types.Flags(type);
        var aliased = !Types.Record(type).AliasSymbol.IsNone;
        if (!aliased && (((flags & (TypeFlags.Union | TypeFlags.Intersection)) != 0 && (flags & TypeFlags.Boolean) == 0) || (flags & TypeFlags.Conditional) != 0))
        {
            return "(" + text + ")";
        }
        return text.Contains("=>", StringComparison.Ordinal) && !text.StartsWith('{') && !text.StartsWith('(') && !text.StartsWith('[') ? "(" + text + ")" : text;
    }

    private static string EscapeTemplateText(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '`':
                    sb.Append("\\`");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '$':
                    sb.Append(i + 1 < text.Length && text[i + 1] == '{' ? "\\$" : "$");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    // typeToTypeNodeHelper: the tuple branch of a type reference
    private string TupleTypeToText(Type type, NodeRef enclosingDeclaration, int depth)
    {
        var target = TupleTarget(type);
        var typeArguments = GetTypeArguments(type).ToArray();
        var arity = GetTypeReferenceArity(type);
        var sb = new StringBuilder();
        if (target.Readonly)
        {
            sb.Append("readonly ");
        }
        sb.Append('[');
        for (var i = 0; i < arity && i < typeArguments.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            var flags = target.ElementFlags![i];
            var element = (flags & ElementFlags.Optional) != 0 ? RemoveMissingType(typeArguments[i], isOptional: true) : typeArguments[i];
            var text = ChildText(element, enclosingDeclaration, depth);
            var label = target.LabeledElementDeclarations is { } labels && i < labels.Length ? labels[i] : NodeRef.None;
            if ((flags & ElementFlags.Rest) != 0)
            {
                text = ParenthesizePostfixOperand(element, text) + "[]";
            }
            if (!label.IsNone)
            {
                sb.Append((flags & ElementFlags.Variable) != 0 ? "..." : "").Append(GetTupleElementLabel(label)).Append((flags & ElementFlags.Optional) != 0 ? "?" : "").Append(": ").Append(text);
            }
            else if ((flags & ElementFlags.Variable) != 0)
            {
                sb.Append("...").Append(text);
            }
            else if ((flags & ElementFlags.Optional) != 0)
            {
                sb.Append(ParenthesizePostfixOperand(element, text)).Append('?');
            }
            else
            {
                sb.Append(text);
            }
        }
        return sb.Append(']').ToString();
    }

    // checker.ts getTupleElementLabel (named tuple members and parameters)
    private string GetTupleElementLabel(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        var name = store.Name(declaration.Node);
        if (!name.IsNone && store.Kind(name) == SyntaxKind.Identifier)
        {
            return store.Texts.GetString(store.EscapedText(name)) ?? "";
        }
        return "arg";
    }

    // nodebuilder indexInfoToIndexSignatureDeclarationHelper: getNameFromIndexInfo(info) || "x"
    private string IndexInfoParameterName(IndexInfo info)
    {
        var declaration = info.Declaration;
        if (!declaration.IsNone && Kind(declaration) == SyntaxKind.IndexSignature)
        {
            var store = StoreOf(declaration);
            var parameters = store.Parameters(declaration.Node);
            if (!parameters.IsNone && store.Length(parameters) > 0)
            {
                return Utilities.DeclarationNameToString(store, store.Name(store.Element(parameters, 0)));
            }
        }
        return "x";
    }

    // checker.ts removeMissingType
    private Type RemoveMissingType(Type type, bool isOptional) =>
        exactOptionalPropertyTypes && isOptional ? FilterType(type, t => t != missingType) : type;

    // nodebuilder createMappedTypeNodeFromType
    private string MappedTypeToText(Type type, NodeRef enclosingDeclaration, int depth)
    {
        var declaration = Types.Object(type).Declaration;
        var store = StoreOf(declaration);
        var sb = new StringBuilder("{ ");
        var readonlyToken = store.ReadonlyToken(declaration.Node);
        if (!readonlyToken.IsNone)
        {
            sb.Append(store.Kind(readonlyToken) switch { SyntaxKind.PlusToken => "+readonly ", SyntaxKind.MinusToken => "-readonly ", _ => "readonly " });
        }
        var typeParameter = GetTypeParameterFromMappedType(type);
        sb.Append('[').Append(SymbolName(Types.Symbol(typeParameter))).Append(" in ");
        if (IsMappedTypeWithKeyofConstraintDeclaration(type))
        {
            var modifiersType = GetModifiersTypeFromMappedType(type);
            sb.Append("keyof ").Append(ParenthesizeOperand(modifiersType, ChildText(modifiersType, enclosingDeclaration, depth)));
        }
        else
        {
            sb.Append(ChildText(GetConstraintTypeFromMappedType(type), enclosingDeclaration, depth));
        }
        var nameType = GetNameTypeFromMappedType(type);
        if (!nameType.IsNone)
        {
            sb.Append(" as ").Append(ChildText(nameType, enclosingDeclaration, depth));
        }
        sb.Append(']');
        var questionToken = store.QuestionToken(declaration.Node);
        if (!questionToken.IsNone)
        {
            sb.Append(store.Kind(questionToken) switch { SyntaxKind.PlusToken => "+?", SyntaxKind.MinusToken => "-?", _ => "?" });
        }
        var templateType = GetTemplateTypeFromMappedType(type);
        if ((GetMappedTypeModifiers(type) & MappedTypeModifiers.IncludeOptional) != 0)
        {
            templateType = RemoveUndefinedForOptional(templateType);
        }
        sb.Append(": ").Append(ChildText(templateType, enclosingDeclaration, depth)).Append("; }");
        return sb.ToString();
    }
}
