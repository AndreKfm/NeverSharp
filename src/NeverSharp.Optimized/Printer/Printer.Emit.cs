// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/emitter.ts (createPrinter: the emit* functions)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/printer/printer.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; comment emission, source maps, name generation and the parenthesizer rule
// arguments (applied only on substitution) are left out; emitTokenWithComment writes the token text only.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Printer
{
    //
    // Literals/Pseudo-literals
    //

    // SyntaxKind.NumericLiteral
    // SyntaxKind.BigIntLiteral
    private void EmitNumericOrBigIntLiteral(Node node)
    {
        EmitLiteral(node, jsxAttributeEscape: false);
    }

    // SyntaxKind.StringLiteral
    // SyntaxKind.RegularExpressionLiteral
    // SyntaxKind.NoSubstitutionTemplateLiteral
    // SyntaxKind.TemplateHead
    // SyntaxKind.TemplateMiddle
    // SyntaxKind.TemplateTail
    private void EmitLiteral(Node node, bool jsxAttributeEscape)
    {
        var text = GetLiteralTextOfNode(node, currentSourceFile, printerOptions.NeverAsciiEscape, jsxAttributeEscape);
        // Quick info expects all literals to be called with writeStringLiteral, as there's no specific type for numberLiterals
        // (TypeScript writes string and template literals with writeLiteral when source maps are on; never here.)
        WriteStringLiteral(text);
    }

    //
    // Identifiers
    //

    private void EmitIdentifier(Node node)
    {
        // (TypeScript: writeSymbol when the identifier has a symbol; both write the text unchanged.)
        Write(GetTextOfNode(node, includeTrivia: false));
        // Call emitList directly since it could be an array of TypeParameterDeclarations _or_ type arguments
        EmitList(node, (store.EmitNode(node) as EmitNode)?.IdentifierTypeArguments ?? NodeList.None, ListFormat.TypeParameters);
    }

    //
    // Names
    //

    private void EmitPrivateIdentifier(Node node)
    {
        Write(GetTextOfNode(node, includeTrivia: false));
    }

    private void EmitQualifiedName(Node node)
    {
        EmitEntityName(store.Left(node));
        WritePunctuation(".");
        Emit(store.Right(node));
    }

    private void EmitEntityName(Node node)
    {
        if (store.Kind(node) == SyntaxKind.Identifier)
        {
            EmitExpression(node);
        }
        else
        {
            Emit(node);
        }
    }

    private void EmitComputedPropertyName(Node node)
    {
        WritePunctuation("[");
        EmitExpression(store.Expression(node));
        WritePunctuation("]");
    }

    //
    // Signature elements
    //

    private void EmitTypeParameter(Node node)
    {
        EmitModifierList(node, store.Modifiers(node));
        Emit(store.Name(node));
        if (!store.Constraint(node).IsNone)
        {
            WriteSpace();
            WriteKeyword("extends");
            WriteSpace();
            Emit(store.Constraint(node));
        }
        if (!store.Default(node).IsNone)
        {
            WriteSpace();
            WriteOperator("=");
            WriteSpace();
            Emit(store.Default(node));
        }
    }

    private void EmitParameter(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: true);
        Emit(store.DotDotDotToken(node));
        // (emitNodeWithWriter(node.name, writeParameter): the text writer writes parameters unchanged)
        Emit(store.Name(node));
        Emit(store.QuestionToken(node));
        var parent = store.Parent(node);
        if (!parent.IsNone && store.Kind(parent) == SyntaxKind.JSDocFunctionType && store.Name(node).IsNone)
        {
            Emit(store.Type(node));
        }
        else
        {
            EmitTypeAnnotation(store.Type(node));
        }
        EmitInitializer(store.Initializer(node), node);
    }

    private void EmitDecorator(Node decorator)
    {
        WritePunctuation("@");
        EmitExpression(store.Expression(decorator));
    }

    //
    // Type members
    //

    private void EmitPropertySignature(Node node)
    {
        EmitModifierList(node, store.Modifiers(node));
        // (emitNodeWithWriter(node.name, writeProperty))
        Emit(store.Name(node));
        Emit(store.QuestionToken(node));
        EmitTypeAnnotation(store.Type(node));
        WriteTrailingSemicolon();
    }

    private void EmitPropertyDeclaration(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: true);
        Emit(store.Name(node));
        Emit(store.QuestionToken(node));
        Emit(store.ExclamationToken(node));
        EmitTypeAnnotation(store.Type(node));
        EmitInitializer(store.Initializer(node), node);
        WriteTrailingSemicolon();
    }

    private void EmitMethodSignature(Node node)
    {
        EmitModifierList(node, store.Modifiers(node));
        Emit(store.Name(node));
        Emit(store.QuestionToken(node));
        EmitSignatureAndBody(node, SignatureHead.Signature, SignatureBody.Empty);
    }

    private void EmitMethodDeclaration(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: true);
        Emit(store.AsteriskToken(node));
        Emit(store.Name(node));
        Emit(store.QuestionToken(node));
        EmitSignatureAndBody(node, SignatureHead.Signature, SignatureBody.Function);
    }

    private void EmitClassStaticBlockDeclaration(Node node)
    {
        WriteKeyword("static");
        EmitBlockFunctionBody(store.Body(node));
    }

    private void EmitConstructor(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: false);
        WriteKeyword("constructor");
        EmitSignatureAndBody(node, SignatureHead.Signature, SignatureBody.Function);
    }

    private void EmitAccessorDeclaration(Node node)
    {
        var pos = EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: true);
        var token = store.Kind(node) == SyntaxKind.GetAccessor ? SyntaxKind.GetKeyword : SyntaxKind.SetKeyword;
        EmitTokenWithComment(token, pos, WriteKind.Keyword, node);
        WriteSpace();
        Emit(store.Name(node));
        EmitSignatureAndBody(node, SignatureHead.Signature, SignatureBody.Function);
    }

    private void EmitCallSignature(Node node)
    {
        EmitSignatureAndBody(node, SignatureHead.Signature, SignatureBody.Empty);
    }

    private void EmitConstructSignature(Node node)
    {
        WriteKeyword("new");
        WriteSpace();
        EmitSignatureAndBody(node, SignatureHead.Signature, SignatureBody.Empty);
    }

    private void EmitIndexSignature(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: false);
        EmitParametersForIndexSignature(node, store.Parameters(node));
        EmitTypeAnnotation(store.Type(node));
        WriteTrailingSemicolon();
    }

    private void EmitTemplateTypeSpan(Node node)
    {
        Emit(store.Type(node));
        Emit(store.Literal(node));
    }

    private void EmitSemicolonClassElement()
    {
        WriteTrailingSemicolon();
    }

    //
    // Types
    //

    private void EmitTypePredicate(Node node)
    {
        if (!store.AssertsModifier(node).IsNone)
        {
            Emit(store.AssertsModifier(node));
            WriteSpace();
        }
        Emit(store.ParameterName(node));
        if (!store.Type(node).IsNone)
        {
            WriteSpace();
            WriteKeyword("is");
            WriteSpace();
            Emit(store.Type(node));
        }
    }

    private void EmitTypeReference(Node node)
    {
        Emit(store.TypeName(node));
        EmitTypeArguments(node, store.TypeArguments(node));
    }

    private void EmitFunctionType(Node node)
    {
        EmitSignatureAndBody(node, SignatureHead.FunctionType, SignatureBody.FunctionType);
    }

    private void EmitFunctionTypeHead(Node node)
    {
        EmitTypeParameters(node, store.TypeParameters(node));
        EmitParametersForArrow(node, store.Parameters(node));
        WriteSpace();
        WritePunctuation("=>");
    }

    private void EmitFunctionTypeBody(Node node)
    {
        WriteSpace();
        Emit(store.Type(node));
    }

    private void EmitJSDocFunctionType(Node node)
    {
        WriteKeyword("function");
        EmitParameters(node, store.Parameters(node));
        WritePunctuation(":");
        Emit(store.Type(node));
    }

    private void EmitJSDocNullableType(Node node)
    {
        WritePunctuation("?");
        Emit(store.Type(node));
    }

    private void EmitJSDocNonNullableType(Node node)
    {
        WritePunctuation("!");
        Emit(store.Type(node));
    }

    private void EmitJSDocOptionalType(Node node)
    {
        Emit(store.Type(node));
        WritePunctuation("=");
    }

    private void EmitConstructorType(Node node)
    {
        EmitModifierList(node, store.Modifiers(node));
        WriteKeyword("new");
        WriteSpace();
        EmitSignatureAndBody(node, SignatureHead.FunctionType, SignatureBody.FunctionType);
    }

    private void EmitTypeQuery(Node node)
    {
        WriteKeyword("typeof");
        WriteSpace();
        Emit(store.ExprName(node));
        EmitTypeArguments(node, store.TypeArguments(node));
    }

    private void EmitTypeLiteral(Node node)
    {
        WritePunctuation("{");
        var flags = (GetEmitFlags(node) & EmitFlags.SingleLine) != 0 ? ListFormat.SingleLineTypeLiteralMembers : ListFormat.MultiLineTypeLiteralMembers;
        EmitList(node, store.Members(node), flags | ListFormat.NoSpaceIfEmpty);
        WritePunctuation("}");
    }

    private void EmitArrayType(Node node)
    {
        Emit(store.ElementType(node));
        WritePunctuation("[");
        WritePunctuation("]");
    }

    private void EmitRestOrJSDocVariadicType(Node node)
    {
        WritePunctuation("...");
        Emit(store.Type(node));
    }

    private void EmitTupleType(Node node)
    {
        EmitTokenWithComment(SyntaxKind.OpenBracketToken, store.Pos(node), WriteKind.Punctuation, node);
        var flags = (GetEmitFlags(node) & EmitFlags.SingleLine) != 0 ? ListFormat.SingleLineTupleTypeElements : ListFormat.MultiLineTupleTypeElements;
        EmitList(node, store.Elements(node), flags | ListFormat.NoSpaceIfEmpty);
        EmitTokenWithComment(SyntaxKind.CloseBracketToken, store.End(store.Elements(node)), WriteKind.Punctuation, node);
    }

    private void EmitNamedTupleMember(Node node)
    {
        Emit(store.DotDotDotToken(node));
        Emit(store.Name(node));
        Emit(store.QuestionToken(node));
        EmitTokenWithComment(SyntaxKind.ColonToken, store.End(store.Name(node)), WriteKind.Punctuation, node);
        WriteSpace();
        Emit(store.Type(node));
    }

    private void EmitOptionalType(Node node)
    {
        Emit(store.Type(node));
        WritePunctuation("?");
    }

    private void EmitUnionType(Node node)
    {
        EmitList(node, store.Types(node), ListFormat.UnionTypeConstituents);
    }

    private void EmitIntersectionType(Node node)
    {
        EmitList(node, store.Types(node), ListFormat.IntersectionTypeConstituents);
    }

    private void EmitConditionalType(Node node)
    {
        Emit(store.CheckType(node));
        WriteSpace();
        WriteKeyword("extends");
        WriteSpace();
        Emit(store.ExtendsType(node));
        WriteSpace();
        WritePunctuation("?");
        WriteSpace();
        Emit(store.TrueType(node));
        WriteSpace();
        WritePunctuation(":");
        WriteSpace();
        Emit(store.FalseType(node));
    }

    private void EmitInferType(Node node)
    {
        WriteKeyword("infer");
        WriteSpace();
        Emit(store.TypeParameter(node));
    }

    private void EmitParenthesizedType(Node node)
    {
        WritePunctuation("(");
        Emit(store.Type(node));
        WritePunctuation(")");
    }

    private void EmitThisType()
    {
        WriteKeyword("this");
    }

    private void EmitTypeOperator(Node node)
    {
        WriteTokenText(store.Operator(node), WriteKind.Keyword);
        WriteSpace();
        Emit(store.Type(node));
    }

    private void EmitIndexedAccessType(Node node)
    {
        Emit(store.ObjectType(node));
        WritePunctuation("[");
        Emit(store.IndexType(node));
        WritePunctuation("]");
    }

    private void EmitMappedType(Node node)
    {
        var emitFlags = GetEmitFlags(node);
        WritePunctuation("{");
        if ((emitFlags & EmitFlags.SingleLine) != 0)
        {
            WriteSpace();
        }
        else
        {
            WriteLine();
            IncreaseIndent();
        }
        var readonlyToken = store.ReadonlyToken(node);
        if (!readonlyToken.IsNone)
        {
            Emit(readonlyToken);
            if (store.Kind(readonlyToken) != SyntaxKind.ReadonlyKeyword)
            {
                WriteKeyword("readonly");
            }
            WriteSpace();
        }
        WritePunctuation("[");

        PipelineEmit(EmitHint.MappedTypeParameter, store.TypeParameter(node));
        if (!store.NameType(node).IsNone)
        {
            WriteSpace();
            WriteKeyword("as");
            WriteSpace();
            Emit(store.NameType(node));
        }

        WritePunctuation("]");
        var questionToken = store.QuestionToken(node);
        if (!questionToken.IsNone)
        {
            Emit(questionToken);
            if (store.Kind(questionToken) != SyntaxKind.QuestionToken)
            {
                WritePunctuation("?");
            }
        }
        WritePunctuation(":");
        WriteSpace();
        Emit(store.Type(node));
        WriteTrailingSemicolon();
        if ((emitFlags & EmitFlags.SingleLine) != 0)
        {
            WriteSpace();
        }
        else
        {
            WriteLine();
            DecreaseIndent();
        }
        EmitList(node, store.Members(node), ListFormat.PreserveLines);
        WritePunctuation("}");
    }

    private void EmitMappedTypeParameter(Node node)
    {
        Emit(store.Name(node));
        WriteSpace();
        WriteKeyword("in");
        WriteSpace();
        Emit(store.Constraint(node));
    }

    private void EmitLiteralType(Node node)
    {
        EmitExpression(store.Literal(node));
    }

    private void EmitTemplateType(Node node)
    {
        Emit(store.Head(node));
        EmitList(node, store.TemplateSpans(node), ListFormat.TemplateExpressionSpans);
    }

    private void EmitImportTypeNode(Node node)
    {
        if (store.IsTypeOf(node))
        {
            WriteKeyword("typeof");
            WriteSpace();
        }
        WriteKeyword("import");
        WritePunctuation("(");
        Emit(store.Argument(node));
        if (!store.Attributes(node).IsNone)
        {
            WritePunctuation(",");
            WriteSpace();
            PipelineEmit(EmitHint.ImportTypeNodeAttributes, store.Attributes(node));
        }
        WritePunctuation(")");
        if (!store.Qualifier(node).IsNone)
        {
            WritePunctuation(".");
            Emit(store.Qualifier(node));
        }
        EmitTypeArguments(node, store.TypeArguments(node));
    }

    //
    // Binding patterns
    //

    private void EmitObjectBindingPattern(Node node)
    {
        WritePunctuation("{");
        EmitList(node, store.Elements(node), ListFormat.ObjectBindingPatternElements);
        WritePunctuation("}");
    }

    private void EmitArrayBindingPattern(Node node)
    {
        WritePunctuation("[");
        EmitList(node, store.Elements(node), ListFormat.ArrayBindingPatternElements);
        WritePunctuation("]");
    }

    private void EmitBindingElement(Node node)
    {
        Emit(store.DotDotDotToken(node));
        if (!store.PropertyName(node).IsNone)
        {
            Emit(store.PropertyName(node));
            WritePunctuation(":");
            WriteSpace();
        }
        Emit(store.Name(node));
        EmitInitializer(store.Initializer(node), node);
    }

    //
    // Expressions
    //

    private void EmitArrayLiteralExpression(Node node)
    {
        var preferNewLine = store.MultiLine(node) ? ListFormat.PreferNewLine : ListFormat.None;
        EmitExpressionList(node, store.Elements(node), ListFormat.ArrayLiteralExpressionElements | preferNewLine);
    }

    private void EmitObjectLiteralExpression(Node node)
    {
        var indentedFlag = (GetEmitFlags(node) & EmitFlags.Indented) != 0;
        if (indentedFlag)
        {
            IncreaseIndent();
        }

        var preferNewLine = store.MultiLine(node) ? ListFormat.PreferNewLine : ListFormat.None;
        var allowTrailingComma = !currentSourceFile.IsNone && !IsJsonSourceFile(currentSourceFile) ? ListFormat.AllowTrailingComma : ListFormat.None;
        EmitList(node, store.Properties(node), ListFormat.ObjectLiteralExpressionProperties | allowTrailingComma | preferNewLine);

        if (indentedFlag)
        {
            DecreaseIndent();
        }
    }

    private void EmitPropertyAccessExpression(Node node)
    {
        var expression = store.Expression(node);
        var name = store.Name(node);
        var questionDotToken = store.QuestionDotToken(node);
        EmitExpression(expression);
        // const token = node.questionDotToken || setTextRangePosEnd(factory.createToken(SyntaxKind.DotToken), node.expression.end, node.name.pos);
        int linesBeforeDot;
        int linesAfterDot;
        SyntaxKind tokenKind;
        if (!questionDotToken.IsNone)
        {
            tokenKind = SyntaxKind.QuestionDotToken;
            linesBeforeDot = GetLinesBetweenNodes(node, expression, questionDotToken);
            linesAfterDot = GetLinesBetweenNodes(node, questionDotToken, name);
        }
        else
        {
            tokenKind = SyntaxKind.DotToken;
            var skippedExpression = SkipSynthesizedParentheses(expression);
            linesBeforeDot = GetLinesBetweenRanges(node, Node.None, store.End(skippedExpression), store.End(expression), NodeIsSynthesized(skippedExpression));
            var skippedName = SkipSynthesizedParentheses(name);
            linesAfterDot = GetLinesBetweenRanges(node, skippedName, store.Pos(name), store.Pos(skippedName), PositionIsSynthesized(store.End(expression)));
        }

        WriteLinesAndIndent(linesBeforeDot, writeSpaceIfNotIndenting: false);

        var shouldEmitDotDot = tokenKind != SyntaxKind.QuestionDotToken &&
            MayNeedDotDotForPropertyAccess(expression) &&
            !writer.HasTrailingComment() &&
            !writer.HasTrailingWhitespace();

        if (shouldEmitDotDot)
        {
            WritePunctuation(".");
        }

        if (!questionDotToken.IsNone)
        {
            Emit(questionDotToken);
        }
        else
        {
            EmitTokenWithComment(tokenKind, store.End(expression), WriteKind.Punctuation, node);
        }
        WriteLinesAndIndent(linesAfterDot, writeSpaceIfNotIndenting: false);
        Emit(name);
        DecreaseIndentIf(linesBeforeDot, linesAfterDot);
    }

    // 1..toString is a valid property access, emit a dot after the literal
    // Also emit a dot if expression is a integer const enum value - it will appear in generated code as numeric literal
    private bool MayNeedDotDotForPropertyAccess(Node expression)
    {
        expression = UtilitiesPublic.SkipPartiallyEmittedExpressions(store, expression);
        if (store.Kind(expression) == SyntaxKind.NumericLiteral)
        {
            // check if numeric literal is a decimal literal that was originally written with a dot
            var text = GetLiteralTextOfNode(expression, currentSourceFile, neverAsciiEscape: true, jsxAttributeEscape: false);
            // If the number will be printed verbatim and it doesn't already contain a dot or an exponent indicator, add one
            // if the expression doesn't have any comments that will be emitted.
            return (store.NumericLiteralFlags(expression) & TokenFlags.WithSpecifier) == 0
                && !text.Contains('.', StringComparison.Ordinal)
                && !text.Contains('E', StringComparison.Ordinal)
                && !text.Contains('e', StringComparison.Ordinal);
        }
        else if (store.Kind(expression) is SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression)
        {
            // check if constant enum value is a non-negative integer
            var constantValue = (store.EmitNode(expression) as EmitNode)?.ConstantValue;
            // isFinite handles cases when constantValue is undefined
            return constantValue is double number && double.IsFinite(number)
                && number >= 0 && Math.Floor(number) == number;
        }
        return false;
    }

    private void EmitElementAccessExpression(Node node)
    {
        EmitExpression(store.Expression(node));
        Emit(store.QuestionDotToken(node));
        EmitTokenWithComment(SyntaxKind.OpenBracketToken, store.End(store.Expression(node)), WriteKind.Punctuation, node);
        EmitExpression(store.ArgumentExpression(node));
        EmitTokenWithComment(SyntaxKind.CloseBracketToken, store.End(store.ArgumentExpression(node)), WriteKind.Punctuation, node);
    }

    private void EmitCallExpression(Node node)
    {
        var indirectCall = (GetInternalEmitFlags(node) & InternalEmitFlags.IndirectCall) != 0;
        if (indirectCall)
        {
            WritePunctuation("(");
            WriteLiteral("0");
            WritePunctuation(",");
            WriteSpace();
        }
        EmitExpression(store.Expression(node));
        if (indirectCall)
        {
            WritePunctuation(")");
        }
        Emit(store.QuestionDotToken(node));
        EmitTypeArguments(node, store.TypeArguments(node));
        EmitExpressionList(node, store.Arguments(node), ListFormat.CallExpressionArguments);
    }

    private void EmitNewExpression(Node node)
    {
        EmitTokenWithComment(SyntaxKind.NewKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        EmitExpression(store.Expression(node));
        EmitTypeArguments(node, store.TypeArguments(node));
        EmitExpressionList(node, store.Arguments(node), ListFormat.NewExpressionArguments);
    }

    private void EmitTaggedTemplateExpression(Node node)
    {
        var indirectCall = (GetInternalEmitFlags(node) & InternalEmitFlags.IndirectCall) != 0;
        if (indirectCall)
        {
            WritePunctuation("(");
            WriteLiteral("0");
            WritePunctuation(",");
            WriteSpace();
        }
        EmitExpression(store.Tag(node));
        if (indirectCall)
        {
            WritePunctuation(")");
        }
        EmitTypeArguments(node, store.TypeArguments(node));
        WriteSpace();
        EmitExpression(store.Template(node));
    }

    private void EmitTypeAssertionExpression(Node node)
    {
        WritePunctuation("<");
        Emit(store.Type(node));
        WritePunctuation(">");
        EmitExpression(store.Expression(node));
    }

    private void EmitParenthesizedExpression(Node node)
    {
        EmitTokenWithComment(SyntaxKind.OpenParenToken, store.Pos(node), WriteKind.Punctuation, node);
        // (writeLineSeparatorsAndIndentBefore / writeLineSeparatorsAfter write nothing without preserveSourceNewlines)
        EmitExpression(store.Expression(node));
        EmitTokenWithComment(SyntaxKind.CloseParenToken, store.End(store.Expression(node)), WriteKind.Punctuation, node);
    }

    private void EmitFunctionExpression(Node node)
    {
        EmitFunctionDeclarationOrExpression(node);
    }

    private void EmitArrowFunction(Node node)
    {
        EmitModifierList(node, store.Modifiers(node));
        EmitSignatureAndBody(node, SignatureHead.ArrowFunction, SignatureBody.ArrowFunction);
    }

    private void EmitArrowFunctionHead(Node node)
    {
        EmitTypeParameters(node, store.TypeParameters(node));
        EmitParametersForArrow(node, store.Parameters(node));
        EmitTypeAnnotation(store.Type(node));
        WriteSpace();
        Emit(store.EqualsGreaterThanToken(node));
    }

    private void EmitArrowFunctionBody(Node node)
    {
        var body = store.Body(node);
        if (store.Kind(body) == SyntaxKind.Block)
        {
            EmitBlockFunctionBody(body);
        }
        else
        {
            WriteSpace();
            EmitExpression(body);
        }
    }

    private void EmitDeleteExpression(Node node)
    {
        EmitTokenWithComment(SyntaxKind.DeleteKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        EmitExpression(store.Expression(node));
    }

    private void EmitTypeOfExpression(Node node)
    {
        EmitTokenWithComment(SyntaxKind.TypeOfKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        EmitExpression(store.Expression(node));
    }

    private void EmitVoidExpression(Node node)
    {
        EmitTokenWithComment(SyntaxKind.VoidKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        EmitExpression(store.Expression(node));
    }

    private void EmitAwaitExpression(Node node)
    {
        EmitTokenWithComment(SyntaxKind.AwaitKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        EmitExpression(store.Expression(node));
    }

    private void EmitPrefixUnaryExpression(Node node)
    {
        WriteTokenText(store.Operator(node), WriteKind.Operator);
        if (ShouldEmitWhitespaceBeforeOperand(node))
        {
            WriteSpace();
        }
        EmitExpression(store.Operand(node));
    }

    private bool ShouldEmitWhitespaceBeforeOperand(Node node)
    {
        // In some cases, we need to emit a space between the operator and the operand. One obvious case
        // is when the operator is an identifier, like delete or typeof. We also need to do this for plus
        // and minus expressions in certain cases. Specifically, consider the following two cases (parens
        // are just for clarity of exposition, and not part of the source code):
        //
        //  (+(+1))
        //  (+(++1))
        //
        // We need to emit a space in both cases. In the first case, the absence of a space will make
        // the resulting expression a prefix increment operation. And in the second, it will make the resulting
        // expression a prefix increment whose operand is a plus expression - (++(+x))
        // The same is true of minus of course.
        var operand = store.Operand(node);
        if (store.Kind(operand) != SyntaxKind.PrefixUnaryExpression)
        {
            return false;
        }
        var op = store.Operator(node);
        var operandOp = store.Operator(operand);
        return (op == SyntaxKind.PlusToken && (operandOp == SyntaxKind.PlusToken || operandOp == SyntaxKind.PlusPlusToken))
            || (op == SyntaxKind.MinusToken && (operandOp == SyntaxKind.MinusToken || operandOp == SyntaxKind.MinusMinusToken));
    }

    private void EmitPostfixUnaryExpression(Node node)
    {
        EmitExpression(store.Operand(node));
        WriteTokenText(store.Operator(node), WriteKind.Operator);
    }

    // createEmitBinaryExpression: createBinaryExpressionTrampoline(onEnter, onLeft, onOperator, onRight, onExit) with an
    // explicit stack. Without comments, source maps and preserveSourceNewlines, onEnter and the bookkeeping of onExit do
    // nothing; maybeEmitExpression descends into a binary operand and emits any other operand through the pipeline.
    private void EmitBinaryExpression(Node root)
    {
        var stack = new List<(Node Node, int State)> { (root, 0) };
        while (stack.Count > 0)
        {
            var (node, state) = stack[^1];
            switch (state)
            {
                case 0:
                {
                    // onLeft
                    stack[^1] = (node, 1);
                    var left = store.Left(node);
                    if (store.Kind(left) == SyntaxKind.BinaryExpression)
                    {
                        stack.Add((left, 0));
                    }
                    else
                    {
                        PipelineEmit(EmitHint.Expression, left);
                    }
                    break;
                }
                case 1:
                {
                    // onOperator, onRight
                    stack[^1] = (node, 2);
                    var operatorToken = store.OperatorToken(node);
                    var isCommaOperator = store.Kind(operatorToken) != SyntaxKind.CommaToken;
                    var linesBeforeOperator = GetLinesBetweenNodes(node, store.Left(node), operatorToken);
                    var linesAfterOperator = GetLinesBetweenNodes(node, operatorToken, store.Right(node));
                    WriteLinesAndIndent(linesBeforeOperator, isCommaOperator);
                    WriteTokenNode(operatorToken, store.Kind(operatorToken) == SyntaxKind.InKeyword ? WriteKind.Keyword : WriteKind.Operator);
                    WriteLinesAndIndent(linesAfterOperator, writeSpaceIfNotIndenting: true);
                    var right = store.Right(node);
                    if (store.Kind(right) == SyntaxKind.BinaryExpression)
                    {
                        stack.Add((right, 0));
                    }
                    else
                    {
                        PipelineEmit(EmitHint.Expression, right);
                    }
                    break;
                }
                default:
                {
                    // onExit
                    var linesBeforeOperator = GetLinesBetweenNodes(node, store.Left(node), store.OperatorToken(node));
                    var linesAfterOperator = GetLinesBetweenNodes(node, store.OperatorToken(node), store.Right(node));
                    DecreaseIndentIf(linesBeforeOperator, linesAfterOperator);
                    stack.RemoveAt(stack.Count - 1);
                    break;
                }
            }
        }
    }

    private void EmitConditionalExpression(Node node)
    {
        var condition = store.Condition(node);
        var questionToken = store.QuestionToken(node);
        var whenTrue = store.WhenTrue(node);
        var colonToken = store.ColonToken(node);
        var whenFalse = store.WhenFalse(node);
        var linesBeforeQuestion = GetLinesBetweenNodes(node, condition, questionToken);
        var linesAfterQuestion = GetLinesBetweenNodes(node, questionToken, whenTrue);
        var linesBeforeColon = GetLinesBetweenNodes(node, whenTrue, colonToken);
        var linesAfterColon = GetLinesBetweenNodes(node, colonToken, whenFalse);

        EmitExpression(condition);
        WriteLinesAndIndent(linesBeforeQuestion, writeSpaceIfNotIndenting: true);
        Emit(questionToken);
        WriteLinesAndIndent(linesAfterQuestion, writeSpaceIfNotIndenting: true);
        EmitExpression(whenTrue);
        DecreaseIndentIf(linesBeforeQuestion, linesAfterQuestion);

        WriteLinesAndIndent(linesBeforeColon, writeSpaceIfNotIndenting: true);
        Emit(colonToken);
        WriteLinesAndIndent(linesAfterColon, writeSpaceIfNotIndenting: true);
        EmitExpression(whenFalse);
        DecreaseIndentIf(linesBeforeColon, linesAfterColon);
    }

    private void EmitTemplateExpression(Node node)
    {
        Emit(store.Head(node));
        EmitList(node, store.TemplateSpans(node), ListFormat.TemplateExpressionSpans);
    }

    private void EmitYieldExpression(Node node)
    {
        EmitTokenWithComment(SyntaxKind.YieldKeyword, store.Pos(node), WriteKind.Keyword, node);
        Emit(store.AsteriskToken(node));
        // (parenthesizeExpressionForNoAsi returns the node unchanged when comments are disabled)
        EmitExpressionWithLeadingSpace(store.Expression(node));
    }

    private void EmitSpreadElement(Node node)
    {
        EmitTokenWithComment(SyntaxKind.DotDotDotToken, store.Pos(node), WriteKind.Punctuation, node);
        EmitExpression(store.Expression(node));
    }

    private void EmitClassExpression(Node node)
    {
        EmitClassDeclarationOrExpression(node);
    }

    private void EmitExpressionWithTypeArguments(Node node)
    {
        EmitExpression(store.Expression(node));
        EmitTypeArguments(node, store.TypeArguments(node));
    }

    private void EmitAsExpression(Node node)
    {
        EmitExpression(store.Expression(node));
        if (!store.Type(node).IsNone)
        {
            WriteSpace();
            WriteKeyword("as");
            WriteSpace();
            Emit(store.Type(node));
        }
    }

    private void EmitNonNullExpression(Node node)
    {
        EmitExpression(store.Expression(node));
        WriteOperator("!");
    }

    private void EmitSatisfiesExpression(Node node)
    {
        EmitExpression(store.Expression(node));
        if (!store.Type(node).IsNone)
        {
            WriteSpace();
            WriteKeyword("satisfies");
            WriteSpace();
            Emit(store.Type(node));
        }
    }

    private void EmitMetaProperty(Node node)
    {
        WriteToken(store.KeywordToken(node), store.Pos(node), WriteKind.Punctuation);
        WritePunctuation(".");
        Emit(store.Name(node));
    }

    //
    // Misc
    //

    private void EmitTemplateSpan(Node node)
    {
        EmitExpression(store.Expression(node));
        Emit(store.Literal(node));
    }

    //
    // Statements
    //

    private void EmitBlock(Node node)
    {
        EmitBlockStatements(node, forceSingleLine: !store.MultiLine(node) && IsEmptyBlock(node));
    }

    private void EmitBlockStatements(Node node, bool forceSingleLine)
    {
        EmitTokenWithComment(SyntaxKind.OpenBraceToken, store.Pos(node), WriteKind.Punctuation, node);
        var format = forceSingleLine || (GetEmitFlags(node) & EmitFlags.SingleLine) != 0 ? ListFormat.SingleLineBlockStatements : ListFormat.MultiLineBlockStatements;
        EmitList(node, store.Statements(node), format);
        EmitTokenWithComment(SyntaxKind.CloseBraceToken, store.End(store.Statements(node)), WriteKind.Punctuation, node);
    }

    private void EmitVariableStatement(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: false);
        Emit(store.DeclarationList(node));
        WriteTrailingSemicolon();
    }

    private void EmitEmptyStatement(bool isEmbeddedStatement)
    {
        // While most trailing semicolons are possibly insignificant, an embedded "empty"
        // statement is significant and cannot be elided by a trailing-semicolon-omitting writer.
        if (isEmbeddedStatement)
        {
            WritePunctuation(";");
        }
        else
        {
            WriteTrailingSemicolon();
        }
    }

    private void EmitExpressionStatement(Node node)
    {
        EmitExpression(store.Expression(node));
        // Emit semicolon in non json files
        // or if json file that created synthesized expression(eg.define expression statement when --out and amd code generation)
        if (currentSourceFile.IsNone || !IsJsonSourceFile(currentSourceFile) || NodeIsSynthesized(store.Expression(node)))
        {
            WriteTrailingSemicolon();
        }
    }

    private void EmitIfStatement(Node node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.IfKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WriteKind.Punctuation, node);
        EmitExpression(store.Expression(node));
        EmitTokenWithComment(SyntaxKind.CloseParenToken, store.End(store.Expression(node)), WriteKind.Punctuation, node);
        EmitEmbeddedStatement(node, store.ThenStatement(node));
        var elseStatement = store.ElseStatement(node);
        if (!elseStatement.IsNone)
        {
            WriteLineOrSpace(node);
            EmitTokenWithComment(SyntaxKind.ElseKeyword, store.End(store.ThenStatement(node)), WriteKind.Keyword, node);
            if (store.Kind(elseStatement) == SyntaxKind.IfStatement)
            {
                WriteSpace();
                Emit(elseStatement);
            }
            else
            {
                EmitEmbeddedStatement(node, elseStatement);
            }
        }
    }

    private void EmitWhileClause(Node node, int startPos)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.WhileKeyword, startPos, WriteKind.Keyword, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WriteKind.Punctuation, node);
        EmitExpression(store.Expression(node));
        EmitTokenWithComment(SyntaxKind.CloseParenToken, store.End(store.Expression(node)), WriteKind.Punctuation, node);
    }

    private void EmitDoStatement(Node node)
    {
        EmitTokenWithComment(SyntaxKind.DoKeyword, store.Pos(node), WriteKind.Keyword, node);
        EmitEmbeddedStatement(node, store.Statement(node));
        if (store.Kind(store.Statement(node)) == SyntaxKind.Block)
        {
            WriteSpace();
        }
        else
        {
            WriteLineOrSpace(node);
        }

        EmitWhileClause(node, store.End(store.Statement(node)));
        WriteTrailingSemicolon();
    }

    private void EmitWhileStatement(Node node)
    {
        EmitWhileClause(node, store.Pos(node));
        EmitEmbeddedStatement(node, store.Statement(node));
    }

    private void EmitForStatement(Node node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.ForKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        var pos = EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WriteKind.Punctuation, node);
        var initializer = store.Initializer(node);
        EmitForBinding(initializer);
        pos = EmitTokenWithComment(SyntaxKind.SemicolonToken, !initializer.IsNone ? store.End(initializer) : pos, WriteKind.Punctuation, node);
        var condition = store.Condition(node);
        EmitExpressionWithLeadingSpace(condition);
        pos = EmitTokenWithComment(SyntaxKind.SemicolonToken, !condition.IsNone ? store.End(condition) : pos, WriteKind.Punctuation, node);
        var incrementor = store.Incrementor(node);
        EmitExpressionWithLeadingSpace(incrementor);
        EmitTokenWithComment(SyntaxKind.CloseParenToken, !incrementor.IsNone ? store.End(incrementor) : pos, WriteKind.Punctuation, node);
        EmitEmbeddedStatement(node, store.Statement(node));
    }

    private void EmitForInStatement(Node node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.ForKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WriteKind.Punctuation, node);
        EmitForBinding(store.Initializer(node));
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.InKeyword, store.End(store.Initializer(node)), WriteKind.Keyword, node);
        WriteSpace();
        EmitExpression(store.Expression(node));
        EmitTokenWithComment(SyntaxKind.CloseParenToken, store.End(store.Expression(node)), WriteKind.Punctuation, node);
        EmitEmbeddedStatement(node, store.Statement(node));
    }

    private void EmitForOfStatement(Node node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.ForKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        EmitWithTrailingSpace(store.AwaitModifier(node));
        EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WriteKind.Punctuation, node);
        EmitForBinding(store.Initializer(node));
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.OfKeyword, store.End(store.Initializer(node)), WriteKind.Keyword, node);
        WriteSpace();
        EmitExpression(store.Expression(node));
        EmitTokenWithComment(SyntaxKind.CloseParenToken, store.End(store.Expression(node)), WriteKind.Punctuation, node);
        EmitEmbeddedStatement(node, store.Statement(node));
    }

    private void EmitForBinding(Node node)
    {
        if (!node.IsNone)
        {
            if (store.Kind(node) == SyntaxKind.VariableDeclarationList)
            {
                Emit(node);
            }
            else
            {
                EmitExpression(node);
            }
        }
    }

    private void EmitContinueStatement(Node node)
    {
        EmitTokenWithComment(SyntaxKind.ContinueKeyword, store.Pos(node), WriteKind.Keyword, node);
        EmitWithLeadingSpace(store.Label(node));
        WriteTrailingSemicolon();
    }

    private void EmitBreakStatement(Node node)
    {
        EmitTokenWithComment(SyntaxKind.BreakKeyword, store.Pos(node), WriteKind.Keyword, node);
        EmitWithLeadingSpace(store.Label(node));
        WriteTrailingSemicolon();
    }

    /// <summary>
    /// emitTokenWithComment with comments disabled and without source maps: the leading and trailing comments of the
    /// position are not written, so only the token text is (writeToken and writeTokenText write the same then).
    /// </summary>
    private int EmitTokenWithComment(SyntaxKind token, int pos, WriteKind kind, Node contextNode)
    {
        var node = UtilitiesPublic.GetParseTreeNode(store, contextNode);
        var isSimilarNode = !node.IsNone && store.Kind(node) == store.Kind(contextNode);
        if (isSimilarNode && !currentSourceFile.IsNone)
        {
            pos = Scanner.SkipTrivia(CurrentText, pos);
        }
        return WriteTokenText(token, kind, pos);
    }

    private void EmitReturnStatement(Node node)
    {
        EmitTokenWithComment(SyntaxKind.ReturnKeyword, store.Pos(node), WriteKind.Keyword, node);
        // (parenthesizeExpressionForNoAsi returns the node unchanged when comments are disabled)
        EmitExpressionWithLeadingSpace(store.Expression(node));
        WriteTrailingSemicolon();
    }

    private void EmitWithStatement(Node node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.WithKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WriteKind.Punctuation, node);
        EmitExpression(store.Expression(node));
        EmitTokenWithComment(SyntaxKind.CloseParenToken, store.End(store.Expression(node)), WriteKind.Punctuation, node);
        EmitEmbeddedStatement(node, store.Statement(node));
    }

    private void EmitSwitchStatement(Node node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.SwitchKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WriteKind.Punctuation, node);
        EmitExpression(store.Expression(node));
        EmitTokenWithComment(SyntaxKind.CloseParenToken, store.End(store.Expression(node)), WriteKind.Punctuation, node);
        WriteSpace();
        Emit(store.CaseBlock(node));
    }

    private void EmitLabeledStatement(Node node)
    {
        Emit(store.Label(node));
        EmitTokenWithComment(SyntaxKind.ColonToken, store.End(store.Label(node)), WriteKind.Punctuation, node);
        WriteSpace();
        Emit(store.Statement(node));
    }

    private void EmitThrowStatement(Node node)
    {
        EmitTokenWithComment(SyntaxKind.ThrowKeyword, store.Pos(node), WriteKind.Keyword, node);
        EmitExpressionWithLeadingSpace(store.Expression(node));
        WriteTrailingSemicolon();
    }

    private void EmitTryStatement(Node node)
    {
        EmitTokenWithComment(SyntaxKind.TryKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        Emit(store.TryBlock(node));
        var catchClause = store.CatchClause(node);
        if (!catchClause.IsNone)
        {
            WriteLineOrSpace(node);
            Emit(catchClause);
        }
        var finallyBlock = store.FinallyBlock(node);
        if (!finallyBlock.IsNone)
        {
            WriteLineOrSpace(node);
            EmitTokenWithComment(SyntaxKind.FinallyKeyword, store.End(!catchClause.IsNone ? catchClause : store.TryBlock(node)), WriteKind.Keyword, node);
            WriteSpace();
            Emit(finallyBlock);
        }
    }

    private void EmitDebuggerStatement(Node node)
    {
        WriteToken(SyntaxKind.DebuggerKeyword, store.Pos(node), WriteKind.Keyword);
        WriteTrailingSemicolon();
    }

    //
    // Declarations
    //

    private void EmitVariableDeclaration(Node node)
    {
        Emit(store.Name(node));
        Emit(store.ExclamationToken(node));
        EmitTypeAnnotation(store.Type(node));
        EmitInitializer(store.Initializer(node), node);
    }

    private void EmitVariableDeclarationList(Node node)
    {
        var blockScoped = Utilities.GetCombinedNodeFlags(store, node) & NodeFlags.BlockScoped;
        if (blockScoped == NodeFlags.AwaitUsing)
        {
            WriteKeyword("await");
            WriteSpace();
            WriteKeyword("using");
        }
        else
        {
            var head = blockScoped == NodeFlags.Let ? "let" :
                blockScoped == NodeFlags.Const ? "const" :
                blockScoped == NodeFlags.Using ? "using" :
                "var";
            WriteKeyword(head);
        }
        WriteSpace();
        EmitList(node, store.Declarations(node), ListFormat.VariableDeclarationList);
    }

    private void EmitFunctionDeclaration(Node node)
    {
        EmitFunctionDeclarationOrExpression(node);
    }

    private void EmitFunctionDeclarationOrExpression(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: false);
        WriteKeyword("function");
        Emit(store.AsteriskToken(node));
        WriteSpace();
        EmitIdentifierName(store.Name(node));
        EmitSignatureAndBody(node, SignatureHead.Signature, SignatureBody.Function);
    }

    // The emitSignatureHead and emitBody callbacks of emitSignatureAndBody.
    private enum SignatureHead
    {
        Signature,
        FunctionType,
        ArrowFunction,
    }

    private enum SignatureBody
    {
        Function,
        Empty,
        FunctionType,
        ArrowFunction,
    }

    private void EmitSignatureAndBody(Node node, SignatureHead emitSignatureHead, SignatureBody emitBody)
    {
        var indentedFlag = (GetEmitFlags(node) & EmitFlags.Indented) != 0;
        if (indentedFlag)
        {
            IncreaseIndent();
        }

        switch (emitSignatureHead)
        {
            case SignatureHead.Signature:
                EmitSignatureHead(node);
                break;
            case SignatureHead.FunctionType:
                EmitFunctionTypeHead(node);
                break;
            default:
                EmitArrowFunctionHead(node);
                break;
        }
        switch (emitBody)
        {
            case SignatureBody.Function:
                EmitFunctionBody(node);
                break;
            case SignatureBody.Empty:
                EmitEmptyFunctionBody();
                break;
            case SignatureBody.FunctionType:
                EmitFunctionTypeBody(node);
                break;
            default:
                EmitArrowFunctionBody(node);
                break;
        }

        if (indentedFlag)
        {
            DecreaseIndent();
        }
    }

    private void EmitFunctionBody(Node node)
    {
        var body = store.Body(node);
        if (!body.IsNone)
        {
            EmitBlockFunctionBody(body);
        }
        else
        {
            WriteTrailingSemicolon();
        }
    }

    private void EmitEmptyFunctionBody()
    {
        WriteTrailingSemicolon();
    }

    private void EmitSignatureHead(Node node)
    {
        EmitTypeParameters(node, store.TypeParameters(node));
        EmitParameters(node, store.Parameters(node));
        EmitTypeAnnotation(store.Type(node));
    }

    private bool ShouldEmitBlockFunctionBodyOnSingleLine(Node body)
    {
        // We must emit a function body as a single-line body in the following case:
        // * The body has NodeEmitFlags.SingleLine specified.

        // We must emit a function body as a multi-line body in the following cases:
        // * The body is explicitly marked as multi-line.
        // * A non-synthesized body's start and end position are on different lines.
        // * Any statement in the body starts on a new line.

        if ((GetEmitFlags(body) & EmitFlags.SingleLine) != 0)
        {
            return true;
        }

        if (store.MultiLine(body))
        {
            return false;
        }

        if (!NodeIsSynthesized(body) && !currentSourceFile.IsNone && !RangeIsOnSingleLine(body))
        {
            return false;
        }

        var statements = store.Statements(body);
        var nodes = store.Nodes(statements);
        if (
            GetLeadingLineTerminatorCount(body, nodes.Length > 0 ? nodes[0] : Node.None, ListFormat.PreserveLines) != 0
            || GetClosingLineTerminatorCount(body, nodes.Length > 0 ? nodes[^1] : Node.None, ListFormat.PreserveLines) != 0)
        {
            return false;
        }

        var previousStatement = Node.None;
        foreach (var statement in store.Nodes(statements))
        {
            if (GetSeparatingLineTerminatorCount(previousStatement, statement, ListFormat.PreserveLines) > 0)
            {
                return false;
            }

            previousStatement = statement;
        }

        return true;
    }

    private void EmitBlockFunctionBody(Node body)
    {
        WriteSpace();
        WritePunctuation("{");
        IncreaseIndent();

        var singleLine = ShouldEmitBlockFunctionBodyOnSingleLine(body);

        EmitBodyWithDetachedComments(body, store.Pos(store.Statements(body)), () => EmitBlockFunctionBodyWorker(body, singleLine));

        DecreaseIndent();
        WriteToken(SyntaxKind.CloseBraceToken, store.End(store.Statements(body)), WriteKind.Punctuation);
    }

    private void EmitBlockFunctionBodyWorker(Node body, bool emitBlockFunctionBodyOnSingleLine)
    {
        // Emit all the prologue directives (like "use strict").
        var statements = store.Statements(body);
        var statementOffset = EmitPrologueDirectives(statements);
        var pos = writer.GetTextPos();
        EmitHelpers(body);
        if (statementOffset == 0 && pos == writer.GetTextPos() && emitBlockFunctionBodyOnSingleLine)
        {
            DecreaseIndent();
            EmitList(body, statements, ListFormat.SingleLineFunctionBodyStatements);
            IncreaseIndent();
        }
        else
        {
            EmitList(body, statements, ListFormat.MultiLineFunctionBodyStatements, statementOffset);
        }
    }

    private void EmitClassDeclaration(Node node)
    {
        EmitClassDeclarationOrExpression(node);
    }

    private void EmitClassDeclarationOrExpression(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: true);
        // (emitTokenWithComment(SyntaxKind.ClassKeyword, moveRangePastModifiers(node).pos, ...): the position only places comments)
        WriteTokenText(SyntaxKind.ClassKeyword, WriteKind.Keyword);
        var name = store.Name(node);
        if (!name.IsNone)
        {
            WriteSpace();
            EmitIdentifierName(name);
        }

        var indentedFlag = (GetEmitFlags(node) & EmitFlags.Indented) != 0;
        if (indentedFlag)
        {
            IncreaseIndent();
        }

        EmitTypeParameters(node, store.TypeParameters(node));
        EmitList(node, store.HeritageClauses(node), ListFormat.ClassHeritageClauses);
        WriteSpace();
        WritePunctuation("{");

        EmitList(node, store.Members(node), ListFormat.ClassMembers);

        WritePunctuation("}");

        if (indentedFlag)
        {
            DecreaseIndent();
        }
    }

    private void EmitInterfaceDeclaration(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: false);
        WriteKeyword("interface");
        WriteSpace();
        Emit(store.Name(node));
        EmitTypeParameters(node, store.TypeParameters(node));
        EmitList(node, store.HeritageClauses(node), ListFormat.HeritageClauses);
        WriteSpace();
        WritePunctuation("{");

        EmitList(node, store.Members(node), ListFormat.InterfaceMembers);

        WritePunctuation("}");
    }

    private void EmitTypeAliasDeclaration(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: false);
        WriteKeyword("type");
        WriteSpace();
        Emit(store.Name(node));
        EmitTypeParameters(node, store.TypeParameters(node));
        WriteSpace();
        WritePunctuation("=");
        WriteSpace();
        Emit(store.Type(node));
        WriteTrailingSemicolon();
    }

    private void EmitEnumDeclaration(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: false);
        WriteKeyword("enum");
        WriteSpace();
        Emit(store.Name(node));

        WriteSpace();
        WritePunctuation("{");
        EmitList(node, store.Members(node), ListFormat.EnumMembers);
        WritePunctuation("}");
    }

    private void EmitModuleDeclaration(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: false);
        if ((~store.Flags(node) & NodeFlags.GlobalAugmentation) != 0)
        {
            WriteKeyword((store.Flags(node) & NodeFlags.Namespace) != 0 ? "namespace" : "module");
            WriteSpace();
        }
        Emit(store.Name(node));

        var body = store.Body(node);
        if (body.IsNone)
        {
            WriteTrailingSemicolon();
            return;
        }
        while (!body.IsNone && store.Kind(body) == SyntaxKind.ModuleDeclaration)
        {
            WritePunctuation(".");
            Emit(store.Name(body));
            body = store.Body(body);
        }

        WriteSpace();
        Emit(body);
    }

    private void EmitModuleBlock(Node node)
    {
        EmitBlockStatements(node, forceSingleLine: IsEmptyBlock(node));
    }

    private void EmitCaseBlock(Node node)
    {
        EmitTokenWithComment(SyntaxKind.OpenBraceToken, store.Pos(node), WriteKind.Punctuation, node);
        EmitList(node, store.Clauses(node), ListFormat.CaseBlockClauses);
        EmitTokenWithComment(SyntaxKind.CloseBraceToken, store.End(store.Clauses(node)), WriteKind.Punctuation, node);
    }

    private void EmitImportEqualsDeclaration(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: false);
        EmitTokenWithComment(SyntaxKind.ImportKeyword, !store.Modifiers(node).IsNone ? store.End(store.Modifiers(node)) : store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        if (store.IsTypeOnly(node))
        {
            EmitTokenWithComment(SyntaxKind.TypeKeyword, store.Pos(node), WriteKind.Keyword, node);
            WriteSpace();
        }
        Emit(store.Name(node));
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.EqualsToken, store.End(store.Name(node)), WriteKind.Punctuation, node);
        WriteSpace();
        EmitModuleReference(store.ModuleReference(node));
        WriteTrailingSemicolon();
    }

    private void EmitModuleReference(Node node)
    {
        if (store.Kind(node) == SyntaxKind.Identifier)
        {
            EmitExpression(node);
        }
        else
        {
            Emit(node);
        }
    }

    private void EmitImportDeclaration(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: false);
        EmitTokenWithComment(SyntaxKind.ImportKeyword, !store.Modifiers(node).IsNone ? store.End(store.Modifiers(node)) : store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        var importClause = store.ImportClause(node);
        if (!importClause.IsNone)
        {
            Emit(importClause);
            WriteSpace();
            EmitTokenWithComment(SyntaxKind.FromKeyword, store.End(importClause), WriteKind.Keyword, node);
            WriteSpace();
        }
        EmitExpression(store.ModuleSpecifier(node));
        if (!store.Attributes(node).IsNone)
        {
            EmitWithLeadingSpace(store.Attributes(node));
        }
        WriteTrailingSemicolon();
    }

    private void EmitImportClause(Node node)
    {
        if (store.PhaseModifier(node) != SyntaxKind.Unknown)
        {
            EmitTokenWithComment(store.PhaseModifier(node), store.Pos(node), WriteKind.Keyword, node);
            WriteSpace();
        }
        var name = store.Name(node);
        Emit(name);
        if (!name.IsNone && !store.NamedBindings(node).IsNone)
        {
            EmitTokenWithComment(SyntaxKind.CommaToken, store.End(name), WriteKind.Punctuation, node);
            WriteSpace();
        }
        Emit(store.NamedBindings(node));
    }

    private void EmitNamespaceImport(Node node)
    {
        var asPos = EmitTokenWithComment(SyntaxKind.AsteriskToken, store.Pos(node), WriteKind.Punctuation, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.AsKeyword, asPos, WriteKind.Keyword, node);
        WriteSpace();
        Emit(store.Name(node));
    }

    private void EmitNamedImports(Node node)
    {
        EmitNamedImportsOrExports(node);
    }

    private void EmitImportSpecifier(Node node)
    {
        EmitImportOrExportSpecifier(node);
    }

    private void EmitExportAssignment(Node node)
    {
        var nextPos = EmitTokenWithComment(SyntaxKind.ExportKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        if (store.IsExportEquals(node))
        {
            EmitTokenWithComment(SyntaxKind.EqualsToken, nextPos, WriteKind.Operator, node);
        }
        else
        {
            EmitTokenWithComment(SyntaxKind.DefaultKeyword, nextPos, WriteKind.Keyword, node);
        }
        WriteSpace();
        EmitExpression(store.Expression(node));
        WriteTrailingSemicolon();
    }

    private void EmitExportDeclaration(Node node)
    {
        EmitDecoratorsAndModifiers(node, store.Modifiers(node), allowDecorators: false);
        var nextPos = EmitTokenWithComment(SyntaxKind.ExportKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        if (store.IsTypeOnly(node))
        {
            nextPos = EmitTokenWithComment(SyntaxKind.TypeKeyword, nextPos, WriteKind.Keyword, node);
            WriteSpace();
        }
        var exportClause = store.ExportClause(node);
        if (!exportClause.IsNone)
        {
            Emit(exportClause);
        }
        else
        {
            nextPos = EmitTokenWithComment(SyntaxKind.AsteriskToken, nextPos, WriteKind.Punctuation, node);
        }
        if (!store.ModuleSpecifier(node).IsNone)
        {
            WriteSpace();
            var fromPos = !exportClause.IsNone ? store.End(exportClause) : nextPos;
            EmitTokenWithComment(SyntaxKind.FromKeyword, fromPos, WriteKind.Keyword, node);
            WriteSpace();
            EmitExpression(store.ModuleSpecifier(node));
        }
        if (!store.Attributes(node).IsNone)
        {
            EmitWithLeadingSpace(store.Attributes(node));
        }
        WriteTrailingSemicolon();
    }

    private void EmitImportTypeNodeAttributes(Node node)
    {
        WritePunctuation("{");
        WriteSpace();
        WriteKeyword(store.Token(node) == SyntaxKind.AssertKeyword ? "assert" : "with");
        WritePunctuation(":");
        WriteSpace();
        EmitList(node, store.Elements(node), ListFormat.ImportAttributes);
        WriteSpace();
        WritePunctuation("}");
    }

    private void EmitImportAttributes(Node node)
    {
        EmitTokenWithComment(store.Token(node), store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        EmitList(node, store.Elements(node), ListFormat.ImportAttributes);
    }

    private void EmitImportAttribute(Node node)
    {
        Emit(store.Name(node));
        WritePunctuation(":");
        WriteSpace();

        // (emitTrailingCommentsOfPosition of the value's comment range: comments are disabled)
        Emit(store.Value(node));
    }

    private void EmitNamespaceExportDeclaration(Node node)
    {
        var nextPos = EmitTokenWithComment(SyntaxKind.ExportKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        nextPos = EmitTokenWithComment(SyntaxKind.AsKeyword, nextPos, WriteKind.Keyword, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.NamespaceKeyword, nextPos, WriteKind.Keyword, node);
        WriteSpace();
        Emit(store.Name(node));
        WriteTrailingSemicolon();
    }

    private void EmitNamespaceExport(Node node)
    {
        var asPos = EmitTokenWithComment(SyntaxKind.AsteriskToken, store.Pos(node), WriteKind.Punctuation, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.AsKeyword, asPos, WriteKind.Keyword, node);
        WriteSpace();
        Emit(store.Name(node));
    }

    private void EmitNamedExports(Node node)
    {
        EmitNamedImportsOrExports(node);
    }

    private void EmitExportSpecifier(Node node)
    {
        EmitImportOrExportSpecifier(node);
    }

    private void EmitNamedImportsOrExports(Node node)
    {
        WritePunctuation("{");
        EmitList(node, store.Elements(node), ListFormat.NamedImportsOrExportsElements);
        WritePunctuation("}");
    }

    private void EmitImportOrExportSpecifier(Node node)
    {
        if (store.IsTypeOnly(node))
        {
            WriteKeyword("type");
            WriteSpace();
        }
        var propertyName = store.PropertyName(node);
        if (!propertyName.IsNone)
        {
            Emit(propertyName);
            WriteSpace();
            EmitTokenWithComment(SyntaxKind.AsKeyword, store.End(propertyName), WriteKind.Keyword, node);
            WriteSpace();
        }

        Emit(store.Name(node));
    }

    //
    // Module references
    //

    private void EmitExternalModuleReference(Node node)
    {
        WriteKeyword("require");
        WritePunctuation("(");
        EmitExpression(store.Expression(node));
        WritePunctuation(")");
    }

    //
    // JSX
    //

    private void EmitJsxElement(Node node)
    {
        Emit(store.OpeningElement(node));
        EmitList(node, store.Children(node), ListFormat.JsxElementOrFragmentChildren);
        Emit(store.ClosingElement(node));
    }

    private void EmitJsxSelfClosingElement(Node node)
    {
        WritePunctuation("<");
        EmitJsxTagName(store.TagName(node));
        EmitTypeArguments(node, store.TypeArguments(node));
        WriteSpace();
        Emit(store.Attributes(node));
        WritePunctuation("/>");
    }

    private void EmitJsxFragment(Node node)
    {
        Emit(store.OpeningFragment(node));
        EmitList(node, store.Children(node), ListFormat.JsxElementOrFragmentChildren);
        Emit(store.ClosingFragment(node));
    }

    private void EmitJsxOpeningElementOrFragment(Node node)
    {
        WritePunctuation("<");

        if (store.Kind(node) == SyntaxKind.JsxOpeningElement)
        {
            // (writeLineSeparatorsAndIndentBefore / writeLineSeparatorsAfter write nothing without preserveSourceNewlines)
            EmitJsxTagName(store.TagName(node));
            EmitTypeArguments(node, store.TypeArguments(node));
            var attributes = store.Attributes(node);
            var properties = store.Properties(attributes);
            if (!properties.IsNone && store.Length(properties) > 0)
            {
                WriteSpace();
            }
            Emit(attributes);
        }

        WritePunctuation(">");
    }

    private void EmitJsxText(Node node)
    {
        writer.WriteLiteral(PrinterUtilities.TextOf(store, node));
    }

    private void EmitJsxClosingElementOrFragment(Node node)
    {
        WritePunctuation("</");
        if (store.Kind(node) == SyntaxKind.JsxClosingElement)
        {
            EmitJsxTagName(store.TagName(node));
        }
        WritePunctuation(">");
    }

    private void EmitJsxAttributes(Node node)
    {
        EmitList(node, store.Properties(node), ListFormat.JsxElementAttributes);
    }

    private void EmitJsxAttribute(Node node)
    {
        Emit(store.Name(node));
        // emitNodeWithPrefix("=", writePunctuation, node.initializer, emitJsxAttributeValue)
        var initializer = store.Initializer(node);
        if (!initializer.IsNone)
        {
            WritePunctuation("=");
            EmitJsxAttributeValue(initializer);
        }
    }

    private void EmitJsxSpreadAttribute(Node node)
    {
        WritePunctuation("{...");
        EmitExpression(store.Expression(node));
        WritePunctuation("}");
    }

    private void EmitJsxExpression(Node node)
    {
        var expression = store.Expression(node);
        // (an empty expression is preserved only if it contains comments, which are disabled)
        if (!expression.IsNone)
        {
            var isMultiline = !currentSourceFile.IsNone && !NodeIsSynthesized(node) &&
                Scanner.ComputeLineOfPosition(GetCurrentLineMap(), store.Pos(node)) != Scanner.ComputeLineOfPosition(GetCurrentLineMap(), store.End(node));
            if (isMultiline)
            {
                writer.IncreaseIndent();
            }
            var end = EmitTokenWithComment(SyntaxKind.OpenBraceToken, store.Pos(node), WriteKind.Punctuation, node);
            Emit(store.DotDotDotToken(node));
            EmitExpression(expression);
            EmitTokenWithComment(SyntaxKind.CloseBraceToken, store.End(expression) != 0 ? store.End(expression) : end, WriteKind.Punctuation, node);
            if (isMultiline)
            {
                writer.DecreaseIndent();
            }
        }
    }

    private void EmitJsxNamespacedName(Node node)
    {
        EmitIdentifierName(store.Namespace(node));
        WritePunctuation(":");
        EmitIdentifierName(store.Name(node));
    }

    private void EmitJsxTagName(Node node)
    {
        if (store.Kind(node) == SyntaxKind.Identifier)
        {
            EmitExpression(node);
        }
        else
        {
            Emit(node);
        }
    }

    //
    // Clauses
    //

    private void EmitCaseClause(Node node)
    {
        EmitTokenWithComment(SyntaxKind.CaseKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        EmitExpression(store.Expression(node));

        EmitCaseOrDefaultClauseRest(node, store.Statements(node), store.End(store.Expression(node)));
    }

    private void EmitDefaultClause(Node node)
    {
        var pos = EmitTokenWithComment(SyntaxKind.DefaultKeyword, store.Pos(node), WriteKind.Keyword, node);
        EmitCaseOrDefaultClauseRest(node, store.Statements(node), pos);
    }

    private void EmitCaseOrDefaultClauseRest(Node parentNode, NodeList statements, int colonPos)
    {
        var nodes = store.Nodes(statements);
        var emitAsSingleStatement = nodes.Length == 1 &&
            (
                // treat synthesized nodes as located on the same line for emit purposes
                currentSourceFile.IsNone ||
                NodeIsSynthesized(parentNode) ||
                NodeIsSynthesized(nodes[0]) ||
                RangeStartPositionsAreOnSameLine(parentNode, nodes[0])
            );

        var format = ListFormat.CaseOrDefaultClauseStatements;
        if (emitAsSingleStatement)
        {
            WriteToken(SyntaxKind.ColonToken, colonPos, WriteKind.Punctuation);
            WriteSpace();
            format &= ~(ListFormat.MultiLine | ListFormat.Indented);
        }
        else
        {
            EmitTokenWithComment(SyntaxKind.ColonToken, colonPos, WriteKind.Punctuation, parentNode);
        }
        EmitList(parentNode, statements, format);
    }

    private void EmitHeritageClause(Node node)
    {
        WriteSpace();
        WriteTokenText(store.Token(node), WriteKind.Keyword);
        WriteSpace();
        EmitList(node, store.Types(node), ListFormat.HeritageClauseTypes);
    }

    private void EmitCatchClause(Node node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.CatchKeyword, store.Pos(node), WriteKind.Keyword, node);
        WriteSpace();
        var variableDeclaration = store.VariableDeclaration(node);
        if (!variableDeclaration.IsNone)
        {
            EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WriteKind.Punctuation, node);
            Emit(variableDeclaration);
            EmitTokenWithComment(SyntaxKind.CloseParenToken, store.End(variableDeclaration), WriteKind.Punctuation, node);
            WriteSpace();
        }
        Emit(store.Block(node));
    }

    //
    // Property assignments
    //

    private void EmitPropertyAssignment(Node node)
    {
        Emit(store.Name(node));
        WritePunctuation(":");
        WriteSpace();
        // (emitTrailingCommentsOfPosition of the initializer's comment range: comments are disabled)
        EmitExpression(store.Initializer(node));
    }

    private void EmitShorthandPropertyAssignment(Node node)
    {
        Emit(store.Name(node));
        var objectAssignmentInitializer = store.ObjectAssignmentInitializer(node);
        if (!objectAssignmentInitializer.IsNone)
        {
            WriteSpace();
            WritePunctuation("=");
            WriteSpace();
            EmitExpression(objectAssignmentInitializer);
        }
    }

    private void EmitSpreadAssignment(Node node)
    {
        if (!store.Expression(node).IsNone)
        {
            EmitTokenWithComment(SyntaxKind.DotDotDotToken, store.Pos(node), WriteKind.Punctuation, node);
            EmitExpression(store.Expression(node));
        }
    }

    //
    // Enum
    //

    private void EmitEnumMember(Node node)
    {
        Emit(store.Name(node));
        EmitInitializer(store.Initializer(node), node);
    }

    //
    // JSDoc
    //

    private void EmitJSDoc(Node node)
    {
        Write("/**");
        var comment = store.Comment(node);
        if (!comment.IsNone)
        {
            var text = PrinterUtilities.GetTextOfJSDocComment(store, comment);
            if (!string.IsNullOrEmpty(text))
            {
                foreach (var line in SplitLines(text))
                {
                    WriteLine();
                    WriteSpace();
                    WritePunctuation("*");
                    WriteSpace();
                    Write(line);
                }
            }
        }
        var tags = store.Tags(node);
        if (!tags.IsNone)
        {
            var tagNodes = store.Nodes(tags);
            if (tagNodes.Length == 1 && store.Kind(tagNodes[0]) == SyntaxKind.JSDocTypeTag && comment.IsNone)
            {
                WriteSpace();
                Emit(tagNodes[0]);
            }
            else
            {
                EmitList(node, tags, ListFormat.JSDocComment);
            }
        }
        WriteSpace();
        Write("*/");
    }

    // text.split(/\r\n?|\n/)
    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\r' || text[i] == '\n')
            {
                lines.Add(text[start..i]);
                i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                start = i;
                continue;
            }
            i++;
        }
        lines.Add(text[start..]);
        return lines;
    }

    private void EmitJSDocSimpleTypedTag(Node tag)
    {
        EmitJSDocTagName(store.TagName(tag));
        EmitJSDocTypeExpression(store.TypeExpression(tag));
        EmitJSDocComment(store.Comment(tag));
    }

    private void EmitJSDocSeeTag(Node tag)
    {
        EmitJSDocTagName(store.TagName(tag));
        Emit(store.Name(tag));
        EmitJSDocComment(store.Comment(tag));
    }

    private void EmitJSDocImportTag(Node tag)
    {
        EmitJSDocTagName(store.TagName(tag));
        WriteSpace();

        var importClause = store.ImportClause(tag);
        if (!importClause.IsNone)
        {
            Emit(importClause);
            WriteSpace();

            EmitTokenWithComment(SyntaxKind.FromKeyword, store.End(importClause), WriteKind.Keyword, tag);
            WriteSpace();
        }

        EmitExpression(store.ModuleSpecifier(tag));
        if (!store.Attributes(tag).IsNone)
        {
            EmitWithLeadingSpace(store.Attributes(tag));
        }
        EmitJSDocComment(store.Comment(tag));
    }

    private void EmitJSDocNameReference(Node node)
    {
        WriteSpace();
        WritePunctuation("{");
        Emit(store.Name(node));
        WritePunctuation("}");
    }

    private void EmitJSDocHeritageTag(Node tag)
    {
        EmitJSDocTagName(store.TagName(tag));
        WriteSpace();
        WritePunctuation("{");
        Emit(store.Class(tag));
        WritePunctuation("}");
        EmitJSDocComment(store.Comment(tag));
    }

    private void EmitJSDocTemplateTag(Node tag)
    {
        EmitJSDocTagName(store.TagName(tag));
        EmitJSDocTypeExpression(store.Constraint(tag));
        WriteSpace();
        EmitList(tag, store.TypeParameters(tag), ListFormat.CommaListElements);
        EmitJSDocComment(store.Comment(tag));
    }

    private void EmitJSDocTypedefTag(Node tag)
    {
        EmitJSDocTagName(store.TagName(tag));
        var typeExpression = store.TypeExpression(tag);
        if (!typeExpression.IsNone)
        {
            if (store.Kind(typeExpression) == SyntaxKind.JSDocTypeExpression)
            {
                EmitJSDocTypeExpression(typeExpression);
            }
            else
            {
                WriteSpace();
                WritePunctuation("{");
                Write("Object");
                if (store.IsArrayType(typeExpression))
                {
                    WritePunctuation("[");
                    WritePunctuation("]");
                }
                WritePunctuation("}");
            }
        }
        if (!store.FullName(tag).IsNone)
        {
            WriteSpace();
            Emit(store.FullName(tag));
        }
        EmitJSDocComment(store.Comment(tag));
        if (!typeExpression.IsNone && store.Kind(typeExpression) == SyntaxKind.JSDocTypeLiteral)
        {
            EmitJSDocTypeLiteral(typeExpression);
        }
    }

    private void EmitJSDocCallbackTag(Node tag)
    {
        EmitJSDocTagName(store.TagName(tag));
        if (!store.Name(tag).IsNone)
        {
            WriteSpace();
            Emit(store.Name(tag));
        }
        EmitJSDocComment(store.Comment(tag));
        EmitJSDocSignature(store.TypeExpression(tag));
    }

    private void EmitJSDocOverloadTag(Node tag)
    {
        EmitJSDocComment(store.Comment(tag));
        EmitJSDocSignature(store.TypeExpression(tag));
    }

    private void EmitJSDocSimpleTag(Node tag)
    {
        EmitJSDocTagName(store.TagName(tag));
        EmitJSDocComment(store.Comment(tag));
    }

    private void EmitJSDocTypeLiteral(Node lit)
    {
        EmitList(lit, Items(store.JsDocPropertyTags(lit) ?? []), ListFormat.JSDocComment);
    }

    private void EmitJSDocSignature(Node sig)
    {
        var typeParameters = store.JSDocSignatureTypeParameters(sig);
        if (typeParameters is not null)
        {
            EmitList(sig, Items(typeParameters), ListFormat.JSDocComment);
        }
        var parameters = store.JSDocSignatureParameters(sig);
        if (parameters is not null)
        {
            EmitList(sig, Items(parameters), ListFormat.JSDocComment);
        }
        if (!store.Type(sig).IsNone)
        {
            WriteLine();
            WriteSpace();
            WritePunctuation("*");
            WriteSpace();
            Emit(store.Type(sig));
        }
    }

    private void EmitJSDocPropertyLikeTag(Node param)
    {
        EmitJSDocTagName(store.TagName(param));
        EmitJSDocTypeExpression(store.TypeExpression(param));
        WriteSpace();
        if (store.IsBracketed(param))
        {
            WritePunctuation("[");
        }
        Emit(store.Name(param));
        if (store.IsBracketed(param))
        {
            WritePunctuation("]");
        }
        EmitJSDocComment(store.Comment(param));
    }

    private void EmitJSDocTagName(Node tagName)
    {
        WritePunctuation("@");
        Emit(tagName);
    }

    private void EmitJSDocComment(TextOrList comment)
    {
        var text = PrinterUtilities.GetTextOfJSDocComment(store, comment);
        if (!string.IsNullOrEmpty(text))
        {
            WriteSpace();
            Write(text);
        }
    }

    // emitJSDocTypeExpression as a node of the emit dispatch (SyntaxKind.JSDocTypeExpression).
    private void EmitJSDocTypeExpressionNode(Node typeExpression)
    {
        EmitJSDocTypeExpression(typeExpression);
    }

    private void EmitJSDocTypeExpression(Node typeExpression)
    {
        if (!typeExpression.IsNone)
        {
            WriteSpace();
            WritePunctuation("{");
            Emit(store.Type(typeExpression));
            WritePunctuation("}");
        }
    }

    //
    // Top-level nodes
    //

    private void EmitSourceFile(Node node)
    {
        WriteLine();
        var statements = store.Statements(node);
        var nodes = store.Nodes(statements);
        // Emit detached comment if there are no prologue directives or if the first node is synthesized.
        // The synthesized node will have no leading comment so some comments may be missed.
        var shouldEmitDetachedComment = nodes.Length == 0 ||
            !Utilities.IsPrologueDirective(store, nodes[0]) ||
            NodeIsSynthesized(nodes[0]);
        if (shouldEmitDetachedComment)
        {
            EmitBodyWithDetachedComments(node, store.Pos(statements), () => EmitSourceFileWorker(node));
            return;
        }
        EmitSourceFileWorker(node);
    }

    private void EmitTripleSlashDirectivesIfNeeded(Node node)
    {
        if (store.IsDeclarationFile(node))
        {
            EmitTripleSlashDirectives(store.ReferencedFiles(node), store.TypeReferenceDirectives(node), store.LibReferenceDirectives(node));
        }
    }

    private void EmitTripleSlashDirectives(IReadOnlyList<FileReference> files, IReadOnlyList<FileReference> types, IReadOnlyList<FileReference> libs)
    {
        if (!currentSourceFile.IsNone && store.ModuleName(currentSourceFile) is { } moduleName)
        {
            WriteComment("/// <amd-module name=\"" + moduleName + "\" />");
            WriteLine();
        }
        if (!currentSourceFile.IsNone)
        {
            foreach (var dep in store.AmdDependencies(currentSourceFile))
            {
                if (!string.IsNullOrEmpty(dep.Name))
                {
                    WriteComment("/// <amd-dependency name=\"" + dep.Name + "\" path=\"" + dep.Path + "\" />");
                }
                else
                {
                    WriteComment("/// <amd-dependency path=\"" + dep.Path + "\" />");
                }
                WriteLine();
            }
        }

        WriteDirectives("path", files);
        WriteDirectives("types", types);
        WriteDirectives("lib", libs);
    }

    private void WriteDirectives(string kind, IReadOnlyList<FileReference> directives)
    {
        foreach (var directive in directives)
        {
            var resolutionMode = directive.ResolutionMode is { } mode && mode != 0
                ? "resolution-mode=\"" + (mode == ModuleKind.ESNext ? "import" : "require") + "\" "
                : "";
            var preserve = directive.Preserve == true ? "preserve=\"true\" " : "";
            WriteComment("/// <reference " + kind + "=\"" + directive.FileName + "\" " + resolutionMode + preserve + "/>");
            WriteLine();
        }
    }

    private void EmitSourceFileWorker(Node node)
    {
        var statements = store.Statements(node);
        EmitHelpers(node);
        var nodes = store.Nodes(statements);
        var index = -1;
        for (var i = 0; i < nodes.Length; i++)
        {
            if (!Utilities.IsPrologueDirective(store, nodes[i]))
            {
                index = i;
                break;
            }
        }
        EmitTripleSlashDirectivesIfNeeded(node);
        EmitList(node, statements, ListFormat.MultiLine, index == -1 ? nodes.Length : index);
    }

    // Transformation nodes

    private void EmitPartiallyEmittedExpression(Node node)
    {
        // (the comments around the expression are not written: comments are disabled)
        EmitExpression(store.Expression(node));
    }

    private void EmitCommaList(Node node)
    {
        EmitExpressionList(node, store.Elements(node), ListFormat.CommaListElements);
    }

    /// <summary>
    /// Emits any prologue directives at the start of a Statement list, returning the
    /// number of prologue directives written to the output.
    /// </summary>
    private int EmitPrologueDirectives(NodeList statements, Node sourceFile = default)
    {
        var needsToSetSourceFile = !sourceFile.IsNone;
        var nodes = store.Nodes(statements);
        for (var i = 0; i < nodes.Length; i++)
        {
            var statement = nodes[i];
            if (Utilities.IsPrologueDirective(store, statement))
            {
                if (needsToSetSourceFile)
                {
                    needsToSetSourceFile = false;
                    SetSourceFile(sourceFile);
                }
                WriteLine();
                Emit(statement);
            }
            else
            {
                // return index of the first non prologue directive
                return i;
            }
        }

        return nodes.Length;
    }

    private void EmitPrologueDirectivesIfNeeded(Node sourceFile)
    {
        EmitPrologueDirectives(store.Statements(sourceFile), sourceFile);
    }

    private void EmitShebangIfNeeded(Node sourceFile)
    {
        var shebang = Scanner.GetShebang(store.SourceFileText(sourceFile).Chars);
        if (shebang is not null)
        {
            WriteComment(shebang);
            WriteLine();
        }
    }

    // emitHelpers: the Track O printer does not support emit helpers (transformers add them; parsed trees and the
    // node builder's type nodes have none).
    private void EmitHelpers(Node node)
    {
        if ((store.EmitNode(node) as EmitNode)?.Helpers is { Count: > 0 })
        {
            throw new NotSupportedException("Emit helpers are not supported by the Track O printer.");
        }
    }

    // emitBodyWithDetachedComments with comments disabled: only emitDetachedComments of the leading range, which writes
    // the pinned comments at the top of the file (removeComments keeps them).
    private void EmitBodyWithDetachedComments(Node node, int detachedRangePos, Action emitCallback)
    {
        var emitFlags = GetEmitFlags(node);
        var skipLeadingComments = detachedRangePos < 0 || (emitFlags & EmitFlags.NoLeadingComments) != 0;
        if (!skipLeadingComments && !currentSourceFile.IsNone)
        {
            PrinterUtilities.EmitDetachedPinnedComments(CurrentText, GetCurrentLineMap(), writer, detachedRangePos, newLine);
        }

        emitCallback();
    }

    //
    // Helpers
    //

    private int EmitDecoratorsAndModifiers(Node node, NodeList modifiers, bool allowDecorators)
    {
        var nodes = store.Nodes(modifiers);
        if (!modifiers.IsNone && nodes.Length > 0)
        {
            var allModifiers = true;
            var allDecorators = true;
            foreach (var modifier in nodes)
            {
                if (UtilitiesPublic.IsModifier(store, modifier))
                {
                    allDecorators = false;
                }
                else
                {
                    allModifiers = false;
                }
                if (store.Kind(modifier) != SyntaxKind.Decorator)
                {
                    allDecorators = false;
                }
            }
            if (allModifiers)
            {
                // if all modifier-likes are `Modifier`, simply emit the array as modifiers.
                return EmitModifierList(node, modifiers);
            }

            if (allDecorators)
            {
                if (allowDecorators)
                {
                    // if all modifier-likes are `Decorator`, simply emit the array as decorators.
                    return EmitDecoratorList(node, modifiers);
                }
                return store.Pos(node);
            }

            // partition modifiers into contiguous chunks of `Modifier` or `Decorator`
            bool? lastMode = null; // true: decorators, false: modifiers
            bool? mode = null;
            var start = 0;
            var pos = 0;
            var lastModifier = Node.None;
            while (start < nodes.Length)
            {
                while (pos < nodes.Length)
                {
                    lastModifier = nodes[pos];
                    mode = store.Kind(lastModifier) == SyntaxKind.Decorator;
                    if (lastMode is null)
                    {
                        lastMode = mode;
                    }
                    else if (mode != lastMode)
                    {
                        break;
                    }

                    pos++;
                }

                if (lastMode == false || allowDecorators)
                {
                    EmitNodeListItems(
                        expressionList: false,
                        node,
                        nodes,
                        lastMode == false ? ListFormat.Modifiers : ListFormat.Decorators,
                        start,
                        pos - start,
                        hasTrailingComma: false);
                }
                start = pos;
                lastMode = mode;
                pos++;
            }

            if (!lastModifier.IsNone && !PositionIsSynthesized(store.End(lastModifier)))
            {
                return store.End(lastModifier);
            }
        }

        return store.Pos(node);
    }

    private int EmitModifierList(Node node, NodeList modifiers)
    {
        EmitList(node, modifiers, ListFormat.Modifiers);
        var nodes = store.Nodes(modifiers);
        var lastModifier = nodes.Length > 0 ? nodes[^1] : Node.None;
        return !lastModifier.IsNone && !PositionIsSynthesized(store.End(lastModifier)) ? store.End(lastModifier) : store.Pos(node);
    }

    private void EmitTypeAnnotation(Node node)
    {
        if (!node.IsNone)
        {
            WritePunctuation(":");
            WriteSpace();
            Emit(node);
        }
    }

    // emitInitializer (the position of the `=` only places comments)
    private void EmitInitializer(Node node, Node container)
    {
        if (!node.IsNone)
        {
            WriteSpace();
            EmitTokenWithComment(SyntaxKind.EqualsToken, store.Pos(node), WriteKind.Operator, container);
            WriteSpace();
            EmitExpression(node);
        }
    }

    private void EmitWithLeadingSpace(Node node)
    {
        if (!node.IsNone)
        {
            WriteSpace();
            Emit(node);
        }
    }

    private void EmitExpressionWithLeadingSpace(Node node)
    {
        if (!node.IsNone)
        {
            WriteSpace();
            EmitExpression(node);
        }
    }

    private void EmitWithTrailingSpace(Node node)
    {
        if (!node.IsNone)
        {
            Emit(node);
            WriteSpace();
        }
    }

    private void EmitEmbeddedStatement(Node parent, Node node)
    {
        if (
            store.Kind(node) == SyntaxKind.Block ||
            (GetEmitFlags(parent) & EmitFlags.SingleLine) != 0)
        {
            WriteSpace();
            Emit(node);
        }
        else
        {
            WriteLine();
            IncreaseIndent();
            if (store.Kind(node) == SyntaxKind.EmptyStatement)
            {
                PipelineEmit(EmitHint.EmbeddedStatement, node);
            }
            else
            {
                Emit(node);
            }
            DecreaseIndent();
        }
    }

    private int EmitDecoratorList(Node parentNode, NodeList decorators)
    {
        EmitList(parentNode, decorators, ListFormat.Decorators);
        var nodes = store.Nodes(decorators);
        var lastDecorator = nodes.Length > 0 ? nodes[^1] : Node.None;
        return !lastDecorator.IsNone && !PositionIsSynthesized(store.End(lastDecorator)) ? store.End(lastDecorator) : store.Pos(parentNode);
    }

    private void EmitTypeArguments(Node parentNode, NodeList typeArguments)
    {
        EmitList(parentNode, typeArguments, ListFormat.TypeArguments);
    }

    private void EmitTypeParameters(Node parentNode, NodeList typeParameters)
    {
        if (Utilities.IsFunctionLike(store, parentNode) && !store.TypeArguments(parentNode).IsNone)
        {
            // Quick info uses type arguments in place of type parameters on instantiated signatures
            EmitTypeArguments(parentNode, store.TypeArguments(parentNode));
            return;
        }
        EmitList(parentNode, typeParameters, ListFormat.TypeParameters | (store.Kind(parentNode) == SyntaxKind.ArrowFunction ? ListFormat.AllowTrailingComma : ListFormat.None));
    }

    private void EmitParameters(Node parentNode, NodeList parameters)
    {
        EmitList(parentNode, parameters, ListFormat.Parameters);
    }

    private bool CanEmitSimpleArrowHead(Node parentNode, NodeList parameters)
    {
        var nodes = store.Nodes(parameters);
        if (nodes.Length != 1)
        {
            return false;
        }
        var parameter = nodes[0];
        return store.Pos(parameter) == store.Pos(parentNode) // may not have parsed tokens between parent and parameter
            && store.Kind(parentNode) == SyntaxKind.ArrowFunction // only arrow functions may have simple arrow head
            && store.Type(parentNode).IsNone // arrow function may not have return type annotation
            && IsNoneOrEmpty(store.Modifiers(parentNode)) // parent may not have decorators or modifiers
            && IsNoneOrEmpty(store.TypeParameters(parentNode)) // parent may not have type parameters
            && IsNoneOrEmpty(store.Modifiers(parameter)) // parameter may not have decorators or modifiers
            && store.DotDotDotToken(parameter).IsNone // parameter may not be rest
            && store.QuestionToken(parameter).IsNone // parameter may not be optional
            && store.Type(parameter).IsNone // parameter may not have a type annotation
            && store.Initializer(parameter).IsNone // parameter may not have an initializer
            && store.Kind(store.Name(parameter)) == SyntaxKind.Identifier; // parameter name must be identifier
    }

    private bool IsNoneOrEmpty(NodeList list) => list.IsNone || store.Length(list) == 0;

    private void EmitParametersForArrow(Node parentNode, NodeList parameters)
    {
        if (CanEmitSimpleArrowHead(parentNode, parameters))
        {
            EmitList(parentNode, parameters, ListFormat.Parameters & ~ListFormat.Parenthesis);
        }
        else
        {
            EmitParameters(parentNode, parameters);
        }
    }

    private void EmitParametersForIndexSignature(Node parentNode, NodeList parameters)
    {
        EmitList(parentNode, parameters, ListFormat.IndexSignatureParameters);
    }
}
