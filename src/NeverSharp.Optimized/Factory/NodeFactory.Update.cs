// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/factory/nodeFactory.ts (the update* functions and their finishUpdate* helpers)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D1. The JSDoc update functions are left out (no transformer of
// the default options updates JSDoc nodes); `!==` on arrays is the identity of the list handles.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>nodeFactory.ts: the update functions the visitors and transformers use.</summary>
internal sealed partial class NodeFactory
{
    //// finishUpdate* helpers

    private Node FinishUpdateBaseSignatureDeclaration(Node updated, Node original)
    {
        if (updated != original)
        {
            // copy children used for quick info
            var typeArguments = store.TypeArguments(original);
            if (!typeArguments.IsNone)
            {
                store.SetTypeArguments(updated, typeArguments);
            }
        }
        return Update(updated, original);
    }

    private Node FinishUpdatePropertySignature(Node updated, Node original)
    {
        if (updated != original)
        {
            // copy children used only for error reporting
            store.SetInitializer(updated, store.Initializer(original));
        }
        return Update(updated, original);
    }

    private Node FinishUpdateMethodDeclaration(Node updated, Node original)
    {
        if (updated != original)
        {
            // copy children used only for error reporting
            store.SetExclamationToken(updated, store.ExclamationToken(original));
        }
        return Update(updated, original);
    }

    private Node FinishUpdateClassStaticBlockDeclaration(Node updated, Node original)
    {
        if (updated != original)
        {
            // copy children used only for error reporting
            store.SetModifiers(updated, store.Modifiers(original));
        }
        return Update(updated, original);
    }

    private Node FinishUpdateConstructorDeclaration(Node updated, Node original)
    {
        if (updated != original)
        {
            store.SetTypeParameters(updated, store.TypeParameters(original));
            store.SetType(updated, store.Type(original));
        }
        return FinishUpdateBaseSignatureDeclaration(updated, original);
    }

    private Node FinishUpdateGetAccessorDeclaration(Node updated, Node original)
    {
        if (updated != original)
        {
            // copy children used only for error reporting
            store.SetTypeParameters(updated, store.TypeParameters(original));
        }
        return FinishUpdateBaseSignatureDeclaration(updated, original);
    }

    private Node FinishUpdateSetAccessorDeclaration(Node updated, Node original)
    {
        if (updated != original)
        {
            // copy children used only for error reporting
            store.SetTypeParameters(updated, store.TypeParameters(original));
            store.SetType(updated, store.Type(original));
        }
        return FinishUpdateBaseSignatureDeclaration(updated, original);
    }

    private Node FinishUpdateFunctionTypeNode(Node updated, Node original)
    {
        if (updated != original)
        {
            // copy children used only for error reporting
            var modifiers = store.Modifiers(original);
            if (!modifiers.IsNone)
            {
                store.SetModifiers(updated, modifiers);
            }
        }
        return FinishUpdateBaseSignatureDeclaration(updated, original);
    }

    private Node FinishUpdateFunctionDeclaration(Node updated, Node original)
    {
        // (TypeScript: `if (updated.modifiers === original.modifiers) updated.modifiers = original.modifiers;`, which
        // changes nothing.)
        return FinishUpdateBaseSignatureDeclaration(updated, original);
    }

    private Node FinishUpdateNamespaceExportDeclaration(Node updated, Node original)
    {
        if (updated != original)
        {
            // copy children used only for error reporting
            var modifiers = store.Modifiers(original);
            if (!modifiers.IsNone)
            {
                store.SetModifiers(updated, modifiers);
            }
        }
        return Update(updated, original);
    }

    private Node FinishUpdatePropertyAssignment(Node updated, Node original)
    {
        if (updated != original)
        {
            // copy children used only for error reporting
            store.SetModifiers(updated, store.Modifiers(original));
            store.SetQuestionToken(updated, store.QuestionToken(original));
            store.SetExclamationToken(updated, store.ExclamationToken(original));
        }
        return Update(updated, original);
    }

    private Node FinishUpdateShorthandPropertyAssignment(Node updated, Node original)
    {
        if (updated != original)
        {
            // copy children used only for error reporting
            store.SetModifiers(updated, store.Modifiers(original));
            store.SetQuestionToken(updated, store.QuestionToken(original));
            store.SetExclamationToken(updated, store.ExclamationToken(original));
            store.SetEqualsToken(updated, store.EqualsToken(original));
        }
        return Update(updated, original);
    }

    //// Names and signature elements

    internal Node UpdateQualifiedName(Node node, Node left, Node right)
    {
        return store.Left(node) != left || store.Right(node) != right
            ? Update(CreateQualifiedName(left, right), node)
            : node;
    }

    internal Node UpdateComputedPropertyName(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateComputedPropertyName(expression), node)
            : node;
    }

    internal Node UpdateTypeParameterDeclaration(Node node, NodeList modifiers, Node name, Node constraint, Node defaultType)
    {
        return store.Modifiers(node) != modifiers
                || store.Name(node) != name
                || store.Constraint(node) != constraint
                || store.Default(node) != defaultType
            ? Update(CreateTypeParameterDeclaration(modifiers, name, constraint, defaultType), node)
            : node;
    }

    internal Node UpdateParameterDeclaration(Node node, NodeList modifiers, Node dotDotDotToken, Node name, Node questionToken, Node type, Node initializer)
    {
        return store.Modifiers(node) != modifiers
                || store.DotDotDotToken(node) != dotDotDotToken
                || store.Name(node) != name
                || store.QuestionToken(node) != questionToken
                || store.Type(node) != type
                || store.Initializer(node) != initializer
            ? Update(CreateParameterDeclaration(modifiers, dotDotDotToken, name, questionToken, type, initializer), node)
            : node;
    }

    internal Node UpdateDecorator(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateDecorator(expression), node)
            : node;
    }

    //// Type elements

    internal Node UpdatePropertySignature(Node node, NodeList modifiers, Node name, Node questionToken, Node type)
    {
        return store.Modifiers(node) != modifiers
                || store.Name(node) != name
                || store.QuestionToken(node) != questionToken
                || store.Type(node) != type
            ? FinishUpdatePropertySignature(CreatePropertySignature(modifiers, name, questionToken, type), node)
            : node;
    }

    internal Node UpdatePropertyDeclaration(Node node, NodeList modifiers, Node name, Node questionOrExclamationToken, Node type, Node initializer)
    {
        var questionToken = !questionOrExclamationToken.IsNone && store.Kind(questionOrExclamationToken) == SyntaxKind.QuestionToken ? questionOrExclamationToken : default;
        var exclamationToken = !questionOrExclamationToken.IsNone && store.Kind(questionOrExclamationToken) == SyntaxKind.ExclamationToken ? questionOrExclamationToken : default;
        return store.Modifiers(node) != modifiers
                || store.Name(node) != name
                || store.QuestionToken(node) != questionToken
                || store.ExclamationToken(node) != exclamationToken
                || store.Type(node) != type
                || store.Initializer(node) != initializer
            ? Update(CreatePropertyDeclaration(modifiers, name, questionOrExclamationToken, type, initializer), node)
            : node;
    }

    internal Node UpdateMethodSignature(Node node, NodeList modifiers, Node name, Node questionToken, NodeList typeParameters, NodeList parameters, Node type)
    {
        return store.Modifiers(node) != modifiers
                || store.Name(node) != name
                || store.QuestionToken(node) != questionToken
                || store.TypeParameters(node) != typeParameters
                || store.Parameters(node) != parameters
                || store.Type(node) != type
            ? FinishUpdateBaseSignatureDeclaration(CreateMethodSignature(modifiers, name, questionToken, typeParameters, parameters, type), node)
            : node;
    }

    internal Node UpdateMethodDeclaration(Node node, NodeList modifiers, Node asteriskToken, Node name, Node questionToken, NodeList typeParameters, NodeList parameters, Node type, Node body)
    {
        return store.Modifiers(node) != modifiers
                || store.AsteriskToken(node) != asteriskToken
                || store.Name(node) != name
                || store.QuestionToken(node) != questionToken
                || store.TypeParameters(node) != typeParameters
                || store.Parameters(node) != parameters
                || store.Type(node) != type
                || store.Body(node) != body
            ? FinishUpdateMethodDeclaration(CreateMethodDeclaration(modifiers, asteriskToken, name, questionToken, typeParameters, parameters, type, body), node)
            : node;
    }

    internal Node UpdateClassStaticBlockDeclaration(Node node, Node body)
    {
        return store.Body(node) != body
            ? FinishUpdateClassStaticBlockDeclaration(CreateClassStaticBlockDeclaration(body), node)
            : node;
    }

    internal Node UpdateConstructorDeclaration(Node node, NodeList modifiers, NodeList parameters, Node body)
    {
        return store.Modifiers(node) != modifiers
                || store.Parameters(node) != parameters
                || store.Body(node) != body
            ? FinishUpdateConstructorDeclaration(CreateConstructorDeclaration(modifiers, parameters, body), node)
            : node;
    }

    internal Node UpdateGetAccessorDeclaration(Node node, NodeList modifiers, Node name, NodeList parameters, Node type, Node body)
    {
        return store.Modifiers(node) != modifiers
                || store.Name(node) != name
                || store.Parameters(node) != parameters
                || store.Type(node) != type
                || store.Body(node) != body
            ? FinishUpdateGetAccessorDeclaration(CreateGetAccessorDeclaration(modifiers, name, parameters, type, body), node)
            : node;
    }

    internal Node UpdateSetAccessorDeclaration(Node node, NodeList modifiers, Node name, NodeList parameters, Node body)
    {
        return store.Modifiers(node) != modifiers
                || store.Name(node) != name
                || store.Parameters(node) != parameters
                || store.Body(node) != body
            ? FinishUpdateSetAccessorDeclaration(CreateSetAccessorDeclaration(modifiers, name, parameters, body), node)
            : node;
    }

    internal Node UpdateCallSignature(Node node, NodeList typeParameters, NodeList parameters, Node type)
    {
        return store.TypeParameters(node) != typeParameters
                || store.Parameters(node) != parameters
                || store.Type(node) != type
            ? FinishUpdateBaseSignatureDeclaration(CreateCallSignature(typeParameters, parameters, type), node)
            : node;
    }

    internal Node UpdateConstructSignature(Node node, NodeList typeParameters, NodeList parameters, Node type)
    {
        return store.TypeParameters(node) != typeParameters
                || store.Parameters(node) != parameters
                || store.Type(node) != type
            ? FinishUpdateBaseSignatureDeclaration(CreateConstructSignature(typeParameters, parameters, type), node)
            : node;
    }

    internal Node UpdateIndexSignature(Node node, NodeList modifiers, NodeList parameters, Node type)
    {
        return store.Parameters(node) != parameters
                || store.Type(node) != type
                || store.Modifiers(node) != modifiers
            ? FinishUpdateBaseSignatureDeclaration(CreateIndexSignature(modifiers, parameters, type), node)
            : node;
    }

    internal Node UpdateTemplateLiteralTypeSpan(Node node, Node type, Node literal)
    {
        return store.Type(node) != type || store.Literal(node) != literal
            ? Update(CreateTemplateLiteralTypeSpan(type, literal), node)
            : node;
    }

    //// Types

    internal Node UpdateTypePredicateNode(Node node, Node assertsModifier, Node parameterName, Node type)
    {
        return store.AssertsModifier(node) != assertsModifier
                || store.ParameterName(node) != parameterName
                || store.Type(node) != type
            ? Update(CreateTypePredicateNode(assertsModifier, parameterName, type), node)
            : node;
    }

    internal Node UpdateTypeReferenceNode(Node node, Node typeName, NodeList typeArguments)
    {
        return store.TypeName(node) != typeName || store.TypeArguments(node) != typeArguments
            ? Update(CreateTypeReferenceNode(typeName, typeArguments), node)
            : node;
    }

    internal Node UpdateFunctionTypeNode(Node node, NodeList typeParameters, NodeList parameters, Node type)
    {
        return store.TypeParameters(node) != typeParameters
                || store.Parameters(node) != parameters
                || store.Type(node) != type
            ? FinishUpdateFunctionTypeNode(CreateFunctionTypeNode(typeParameters, parameters, type), node)
            : node;
    }

    internal Node UpdateConstructorTypeNode(Node node, NodeList modifiers, NodeList typeParameters, NodeList parameters, Node type)
    {
        return store.Modifiers(node) != modifiers
                || store.TypeParameters(node) != typeParameters
                || store.Parameters(node) != parameters
                || store.Type(node) != type
            ? FinishUpdateBaseSignatureDeclaration(CreateConstructorTypeNode(modifiers, typeParameters, parameters, type), node)
            : node;
    }

    internal Node UpdateTypeQueryNode(Node node, Node exprName, NodeList typeArguments)
    {
        return store.ExprName(node) != exprName || store.TypeArguments(node) != typeArguments
            ? Update(CreateTypeQueryNode(exprName, typeArguments), node)
            : node;
    }

    internal Node UpdateTypeLiteralNode(Node node, NodeList members)
    {
        return store.Members(node) != members
            ? Update(CreateTypeLiteralNode(members), node)
            : node;
    }

    internal Node UpdateArrayTypeNode(Node node, Node elementType)
    {
        return store.ElementType(node) != elementType
            ? Update(CreateArrayTypeNode(elementType), node)
            : node;
    }

    internal Node UpdateTupleTypeNode(Node node, NodeList elements)
    {
        return store.Elements(node) != elements
            ? Update(CreateTupleTypeNode(elements), node)
            : node;
    }

    internal Node UpdateNamedTupleMember(Node node, Node dotDotDotToken, Node name, Node questionToken, Node type)
    {
        return store.DotDotDotToken(node) != dotDotDotToken
                || store.Name(node) != name
                || store.QuestionToken(node) != questionToken
                || store.Type(node) != type
            ? Update(CreateNamedTupleMember(dotDotDotToken, name, questionToken, type), node)
            : node;
    }

    internal Node UpdateOptionalTypeNode(Node node, Node type)
    {
        return store.Type(node) != type
            ? Update(CreateOptionalTypeNode(type), node)
            : node;
    }

    internal Node UpdateRestTypeNode(Node node, Node type)
    {
        return store.Type(node) != type
            ? Update(CreateRestTypeNode(type), node)
            : node;
    }

    internal Node UpdateUnionTypeNode(Node node, NodeList types)
    {
        return store.Types(node) != types
            ? Update(CreateUnionOrIntersectionTypeNode(store.Kind(node), types), node)
            : node;
    }

    internal Node UpdateIntersectionTypeNode(Node node, NodeList types)
    {
        return store.Types(node) != types
            ? Update(CreateUnionOrIntersectionTypeNode(store.Kind(node), types), node)
            : node;
    }

    internal Node UpdateConditionalTypeNode(Node node, Node checkType, Node extendsType, Node trueType, Node falseType)
    {
        return store.CheckType(node) != checkType
                || store.ExtendsType(node) != extendsType
                || store.TrueType(node) != trueType
                || store.FalseType(node) != falseType
            ? Update(CreateConditionalTypeNode(checkType, extendsType, trueType, falseType), node)
            : node;
    }

    internal Node UpdateInferTypeNode(Node node, Node typeParameter)
    {
        return store.TypeParameter(node) != typeParameter
            ? Update(CreateInferTypeNode(typeParameter), node)
            : node;
    }

    internal Node UpdateTemplateLiteralType(Node node, Node head, NodeList templateSpans)
    {
        return store.Head(node) != head || store.TemplateSpans(node) != templateSpans
            ? Update(CreateTemplateLiteralType(head, templateSpans), node)
            : node;
    }

    internal Node UpdateImportTypeNode(Node node, Node argument, Node attributes, Node qualifier, NodeList typeArguments, bool? isTypeOf = null)
    {
        var isTypeOfValue = isTypeOf ?? store.IsTypeOf(node);
        return store.Argument(node) != argument
                || store.Attributes(node) != attributes
                || store.Qualifier(node) != qualifier
                || store.TypeArguments(node) != typeArguments
                || store.IsTypeOf(node) != isTypeOfValue
            ? Update(CreateImportTypeNode(argument, attributes, qualifier, typeArguments, isTypeOfValue), node)
            : node;
    }

    internal Node UpdateParenthesizedType(Node node, Node type)
    {
        return store.Type(node) != type
            ? Update(CreateParenthesizedType(type), node)
            : node;
    }

    internal Node UpdateTypeOperatorNode(Node node, Node type)
    {
        return store.Type(node) != type
            ? Update(CreateTypeOperatorNode(store.Operator(node), type), node)
            : node;
    }

    internal Node UpdateIndexedAccessTypeNode(Node node, Node objectType, Node indexType)
    {
        return store.ObjectType(node) != objectType || store.IndexType(node) != indexType
            ? Update(CreateIndexedAccessTypeNode(objectType, indexType), node)
            : node;
    }

    internal Node UpdateMappedTypeNode(Node node, Node readonlyToken, Node typeParameter, Node nameType, Node questionToken, Node type, NodeList members)
    {
        return store.ReadonlyToken(node) != readonlyToken
                || store.TypeParameter(node) != typeParameter
                || store.NameType(node) != nameType
                || store.QuestionToken(node) != questionToken
                || store.Type(node) != type
                || store.Members(node) != members
            ? Update(CreateMappedTypeNode(readonlyToken, typeParameter, nameType, questionToken, type, members), node)
            : node;
    }

    internal Node UpdateLiteralTypeNode(Node node, Node literal)
    {
        return store.Literal(node) != literal
            ? Update(CreateLiteralTypeNode(literal), node)
            : node;
    }

    //// Binding patterns

    internal Node UpdateObjectBindingPattern(Node node, NodeList elements)
    {
        return store.Elements(node) != elements
            ? Update(CreateObjectBindingPattern(elements), node)
            : node;
    }

    internal Node UpdateArrayBindingPattern(Node node, NodeList elements)
    {
        return store.Elements(node) != elements
            ? Update(CreateArrayBindingPattern(elements), node)
            : node;
    }

    internal Node UpdateBindingElement(Node node, Node dotDotDotToken, Node propertyName, Node name, Node initializer)
    {
        return store.PropertyName(node) != propertyName
                || store.DotDotDotToken(node) != dotDotDotToken
                || store.Name(node) != name
                || store.Initializer(node) != initializer
            ? Update(CreateBindingElement(dotDotDotToken, propertyName, name, initializer), node)
            : node;
    }

    //// Expressions

    internal Node UpdateArrayLiteralExpression(Node node, NodeList elements)
    {
        return store.Elements(node) != elements
            ? Update(CreateArrayLiteralExpression(elements, store.MultiLine(node)), node)
            : node;
    }

    internal Node UpdateObjectLiteralExpression(Node node, NodeList properties)
    {
        return store.Properties(node) != properties
            ? Update(CreateObjectLiteralExpression(properties, store.MultiLine(node)), node)
            : node;
    }

    internal Node UpdatePropertyAccessExpression(Node node, Node expression, Node name)
    {
        if (IsPropertyAccessChain(store, node))
        {
            return UpdatePropertyAccessChain(node, expression, store.QuestionDotToken(node), name);
        }
        return store.Expression(node) != expression || store.Name(node) != name
            ? Update(CreatePropertyAccessExpression(expression, name), node)
            : node;
    }

    internal Node UpdatePropertyAccessChain(Node node, Node expression, Node questionDotToken, Node name)
    {
        Debug.Assert((store.Flags(node) & NodeFlags.OptionalChain) != 0, "Cannot update a PropertyAccessExpression using updatePropertyAccessChain. Use updatePropertyAccess instead.");
        return store.Expression(node) != expression
                || store.QuestionDotToken(node) != questionDotToken
                || store.Name(node) != name
            ? Update(CreatePropertyAccessChain(expression, questionDotToken, name), node)
            : node;
    }

    internal Node UpdateElementAccessExpression(Node node, Node expression, Node argumentExpression)
    {
        if (IsElementAccessChain(store, node))
        {
            return UpdateElementAccessChain(node, expression, store.QuestionDotToken(node), argumentExpression);
        }
        return store.Expression(node) != expression || store.ArgumentExpression(node) != argumentExpression
            ? Update(CreateElementAccessExpression(expression, argumentExpression), node)
            : node;
    }

    internal Node UpdateElementAccessChain(Node node, Node expression, Node questionDotToken, Node argumentExpression)
    {
        Debug.Assert((store.Flags(node) & NodeFlags.OptionalChain) != 0, "Cannot update a ElementAccessExpression using updateElementAccessChain. Use updateElementAccess instead.");
        return store.Expression(node) != expression
                || store.QuestionDotToken(node) != questionDotToken
                || store.ArgumentExpression(node) != argumentExpression
            ? Update(CreateElementAccessChain(expression, questionDotToken, argumentExpression), node)
            : node;
    }

    internal Node UpdateNewExpression(Node node, Node expression, NodeList typeArguments, NodeList argumentsArray)
    {
        return store.Expression(node) != expression
                || store.TypeArguments(node) != typeArguments
                || store.Arguments(node) != argumentsArray
            ? Update(CreateNewExpression(expression, typeArguments, argumentsArray), node)
            : node;
    }

    internal Node UpdateTaggedTemplateExpression(Node node, Node tag, NodeList typeArguments, Node template)
    {
        return store.Tag(node) != tag
                || store.TypeArguments(node) != typeArguments
                || store.Template(node) != template
            ? Update(CreateTaggedTemplateExpression(tag, typeArguments, template), node)
            : node;
    }

    internal Node UpdateFunctionExpression(Node node, NodeList modifiers, Node asteriskToken, Node name, NodeList typeParameters, NodeList parameters, Node type, Node body)
    {
        return store.Name(node) != name
                || store.Modifiers(node) != modifiers
                || store.AsteriskToken(node) != asteriskToken
                || store.TypeParameters(node) != typeParameters
                || store.Parameters(node) != parameters
                || store.Type(node) != type
                || store.Body(node) != body
            ? FinishUpdateBaseSignatureDeclaration(CreateFunctionExpression(modifiers, asteriskToken, name, typeParameters, parameters, type, body), node)
            : node;
    }

    internal Node UpdateArrowFunction(Node node, NodeList modifiers, NodeList typeParameters, NodeList parameters, Node type, Node equalsGreaterThanToken, Node body)
    {
        return store.Modifiers(node) != modifiers
                || store.TypeParameters(node) != typeParameters
                || store.Parameters(node) != parameters
                || store.Type(node) != type
                || store.EqualsGreaterThanToken(node) != equalsGreaterThanToken
                || store.Body(node) != body
            ? FinishUpdateBaseSignatureDeclaration(CreateArrowFunction(modifiers, typeParameters, parameters, type, equalsGreaterThanToken, body), node)
            : node;
    }

    internal Node UpdateDeleteExpression(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateDeleteExpression(expression), node)
            : node;
    }

    internal Node UpdateTypeOfExpression(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateTypeOfExpression(expression), node)
            : node;
    }

    internal Node UpdateVoidExpression(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateVoidExpression(expression), node)
            : node;
    }

    internal Node UpdateAwaitExpression(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateAwaitExpression(expression), node)
            : node;
    }

    internal Node UpdatePrefixUnaryExpression(Node node, Node operand)
    {
        return store.Operand(node) != operand
            ? Update(CreatePrefixUnaryExpression(store.Operator(node), operand), node)
            : node;
    }

    internal Node UpdatePostfixUnaryExpression(Node node, Node operand)
    {
        return store.Operand(node) != operand
            ? Update(CreatePostfixUnaryExpression(operand, store.Operator(node)), node)
            : node;
    }

    internal Node UpdateBinaryExpression(Node node, Node left, Node @operator, Node right)
    {
        return store.Left(node) != left
                || store.OperatorToken(node) != @operator
                || store.Right(node) != right
            ? Update(CreateBinaryExpression(left, @operator, right), node)
            : node;
    }

    internal Node UpdateConditionalExpression(Node node, Node condition, Node questionToken, Node whenTrue, Node colonToken, Node whenFalse)
    {
        return store.Condition(node) != condition
                || store.QuestionToken(node) != questionToken
                || store.WhenTrue(node) != whenTrue
                || store.ColonToken(node) != colonToken
                || store.WhenFalse(node) != whenFalse
            ? Update(CreateConditionalExpression(condition, questionToken, whenTrue, colonToken, whenFalse), node)
            : node;
    }

    internal Node UpdateTemplateExpression(Node node, Node head, NodeList templateSpans)
    {
        return store.Head(node) != head || store.TemplateSpans(node) != templateSpans
            ? Update(CreateTemplateExpression(head, templateSpans), node)
            : node;
    }

    internal Node UpdateYieldExpression(Node node, Node asteriskToken, Node expression)
    {
        return store.Expression(node) != expression || store.AsteriskToken(node) != asteriskToken
            ? Update(CreateYieldExpression(asteriskToken, expression), node)
            : node;
    }

    internal Node UpdateSpreadElement(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateSpreadElement(expression), node)
            : node;
    }

    internal Node UpdateClassExpression(Node node, NodeList modifiers, Node name, NodeList typeParameters, NodeList heritageClauses, NodeList members)
    {
        return store.Modifiers(node) != modifiers
                || store.Name(node) != name
                || store.TypeParameters(node) != typeParameters
                || store.HeritageClauses(node) != heritageClauses
                || store.Members(node) != members
            ? Update(CreateClassExpression(modifiers, name, typeParameters, heritageClauses, members), node)
            : node;
    }

    internal Node UpdateMetaProperty(Node node, Node name)
    {
        return store.Name(node) != name
            ? Update(CreateMetaProperty(store.KeywordToken(node), name), node)
            : node;
    }

    internal Node UpdateTemplateSpan(Node node, Node expression, Node literal)
    {
        return store.Expression(node) != expression || store.Literal(node) != literal
            ? Update(CreateTemplateSpan(expression, literal), node)
            : node;
    }

    //// Statements

    internal Node UpdateBlock(Node node, NodeList statements)
    {
        return store.Statements(node) != statements
            ? Update(CreateBlock(statements, store.MultiLine(node)), node)
            : node;
    }

    internal Node UpdateVariableStatement(Node node, NodeList modifiers, Node declarationList)
    {
        return store.Modifiers(node) != modifiers || store.DeclarationList(node) != declarationList
            ? Update(CreateVariableStatement(modifiers, declarationList), node)
            : node;
    }

    internal Node UpdateExpressionStatement(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateExpressionStatement(expression), node)
            : node;
    }

    internal Node UpdateIfStatement(Node node, Node expression, Node thenStatement, Node elseStatement)
    {
        return store.Expression(node) != expression
                || store.ThenStatement(node) != thenStatement
                || store.ElseStatement(node) != elseStatement
            ? Update(CreateIfStatement(expression, thenStatement, elseStatement), node)
            : node;
    }

    internal Node UpdateDoStatement(Node node, Node statement, Node expression)
    {
        return store.Statement(node) != statement || store.Expression(node) != expression
            ? Update(CreateDoStatement(statement, expression), node)
            : node;
    }

    internal Node UpdateWhileStatement(Node node, Node expression, Node statement)
    {
        return store.Expression(node) != expression || store.Statement(node) != statement
            ? Update(CreateWhileStatement(expression, statement), node)
            : node;
    }

    internal Node UpdateForStatement(Node node, Node initializer, Node condition, Node incrementor, Node statement)
    {
        return store.Initializer(node) != initializer
                || store.Condition(node) != condition
                || store.Incrementor(node) != incrementor
                || store.Statement(node) != statement
            ? Update(CreateForStatement(initializer, condition, incrementor, statement), node)
            : node;
    }

    internal Node UpdateForInStatement(Node node, Node initializer, Node expression, Node statement)
    {
        return store.Initializer(node) != initializer
                || store.Expression(node) != expression
                || store.Statement(node) != statement
            ? Update(CreateForInStatement(initializer, expression, statement), node)
            : node;
    }

    internal Node UpdateForOfStatement(Node node, Node awaitModifier, Node initializer, Node expression, Node statement)
    {
        return store.AwaitModifier(node) != awaitModifier
                || store.Initializer(node) != initializer
                || store.Expression(node) != expression
                || store.Statement(node) != statement
            ? Update(CreateForOfStatement(awaitModifier, initializer, expression, statement), node)
            : node;
    }

    internal Node UpdateContinueStatement(Node node, Node label)
    {
        return store.Label(node) != label
            ? Update(CreateContinueStatement(label), node)
            : node;
    }

    internal Node UpdateBreakStatement(Node node, Node label)
    {
        return store.Label(node) != label
            ? Update(CreateBreakStatement(label), node)
            : node;
    }

    internal Node UpdateReturnStatement(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateReturnStatement(expression), node)
            : node;
    }

    internal Node UpdateWithStatement(Node node, Node expression, Node statement)
    {
        return store.Expression(node) != expression || store.Statement(node) != statement
            ? Update(CreateWithStatement(expression, statement), node)
            : node;
    }

    internal Node UpdateSwitchStatement(Node node, Node expression, Node caseBlock)
    {
        return store.Expression(node) != expression || store.CaseBlock(node) != caseBlock
            ? Update(CreateSwitchStatement(expression, caseBlock), node)
            : node;
    }

    internal Node UpdateLabeledStatement(Node node, Node label, Node statement)
    {
        return store.Label(node) != label || store.Statement(node) != statement
            ? Update(CreateLabeledStatement(label, statement), node)
            : node;
    }

    internal Node UpdateThrowStatement(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateThrowStatement(expression), node)
            : node;
    }

    internal Node UpdateTryStatement(Node node, Node tryBlock, Node catchClause, Node finallyBlock)
    {
        return store.TryBlock(node) != tryBlock
                || store.CatchClause(node) != catchClause
                || store.FinallyBlock(node) != finallyBlock
            ? Update(CreateTryStatement(tryBlock, catchClause, finallyBlock), node)
            : node;
    }

    internal Node UpdateVariableDeclaration(Node node, Node name, Node exclamationToken, Node type, Node initializer)
    {
        return store.Name(node) != name
                || store.Type(node) != type
                || store.ExclamationToken(node) != exclamationToken
                || store.Initializer(node) != initializer
            ? Update(CreateVariableDeclaration(name, exclamationToken, type, initializer), node)
            : node;
    }

    internal Node UpdateVariableDeclarationList(Node node, NodeList declarations)
    {
        return store.Declarations(node) != declarations
            ? Update(CreateVariableDeclarationList(declarations, store.Flags(node)), node)
            : node;
    }

    internal Node UpdateFunctionDeclaration(Node node, NodeList modifiers, Node asteriskToken, Node name, NodeList typeParameters, NodeList parameters, Node type, Node body)
    {
        return store.Modifiers(node) != modifiers
                || store.AsteriskToken(node) != asteriskToken
                || store.Name(node) != name
                || store.TypeParameters(node) != typeParameters
                || store.Parameters(node) != parameters
                || store.Type(node) != type
                || store.Body(node) != body
            ? FinishUpdateFunctionDeclaration(CreateFunctionDeclaration(modifiers, asteriskToken, name, typeParameters, parameters, type, body), node)
            : node;
    }

    internal Node UpdateClassDeclaration(Node node, NodeList modifiers, Node name, NodeList typeParameters, NodeList heritageClauses, NodeList members)
    {
        return store.Modifiers(node) != modifiers
                || store.Name(node) != name
                || store.TypeParameters(node) != typeParameters
                || store.HeritageClauses(node) != heritageClauses
                || store.Members(node) != members
            ? Update(CreateClassDeclaration(modifiers, name, typeParameters, heritageClauses, members), node)
            : node;
    }

    internal Node UpdateInterfaceDeclaration(Node node, NodeList modifiers, Node name, NodeList typeParameters, NodeList heritageClauses, NodeList members)
    {
        return store.Modifiers(node) != modifiers
                || store.Name(node) != name
                || store.TypeParameters(node) != typeParameters
                || store.HeritageClauses(node) != heritageClauses
                || store.Members(node) != members
            ? Update(CreateInterfaceDeclaration(modifiers, name, typeParameters, heritageClauses, members), node)
            : node;
    }

    internal Node UpdateTypeAliasDeclaration(Node node, NodeList modifiers, Node name, NodeList typeParameters, Node type)
    {
        return store.Modifiers(node) != modifiers
                || store.Name(node) != name
                || store.TypeParameters(node) != typeParameters
                || store.Type(node) != type
            ? Update(CreateTypeAliasDeclaration(modifiers, name, typeParameters, type), node)
            : node;
    }

    internal Node UpdateEnumDeclaration(Node node, NodeList modifiers, Node name, NodeList members)
    {
        return store.Modifiers(node) != modifiers
                || store.Name(node) != name
                || store.Members(node) != members
            ? Update(CreateEnumDeclaration(modifiers, name, members), node)
            : node;
    }

    internal Node UpdateModuleDeclaration(Node node, NodeList modifiers, Node name, Node body)
    {
        return store.Modifiers(node) != modifiers
                || store.Name(node) != name
                || store.Body(node) != body
            ? Update(CreateModuleDeclaration(modifiers, name, body, store.Flags(node)), node)
            : node;
    }

    internal Node UpdateModuleBlock(Node node, NodeList statements)
    {
        return store.Statements(node) != statements
            ? Update(CreateModuleBlock(statements), node)
            : node;
    }

    internal Node UpdateCaseBlock(Node node, NodeList clauses)
    {
        return store.Clauses(node) != clauses
            ? Update(CreateCaseBlock(clauses), node)
            : node;
    }

    internal Node UpdateNamespaceExportDeclaration(Node node, Node name)
    {
        return store.Name(node) != name
            ? FinishUpdateNamespaceExportDeclaration(CreateNamespaceExportDeclaration(name), node)
            : node;
    }

    internal Node UpdateImportEqualsDeclaration(Node node, NodeList modifiers, bool isTypeOnly, Node name, Node moduleReference)
    {
        return store.Modifiers(node) != modifiers
                || store.IsTypeOnly(node) != isTypeOnly
                || store.Name(node) != name
                || store.ModuleReference(node) != moduleReference
            ? Update(CreateImportEqualsDeclaration(modifiers, isTypeOnly, name, moduleReference), node)
            : node;
    }

    internal Node UpdateImportDeclaration(Node node, NodeList modifiers, Node importClause, Node moduleSpecifier, Node attributes)
    {
        return store.Modifiers(node) != modifiers
                || store.ImportClause(node) != importClause
                || store.ModuleSpecifier(node) != moduleSpecifier
                || store.Attributes(node) != attributes
            ? Update(CreateImportDeclaration(modifiers, importClause, moduleSpecifier, attributes), node)
            : node;
    }

    /// <summary>updateImportClause with the phase modifier as a syntax kind (null: undefined).</summary>
    internal Node UpdateImportClause(Node node, SyntaxKind? phaseModifier, Node name, Node namedBindings)
    {
        var current = store.PhaseModifier(node);
        return (current == SyntaxKind.Unknown ? null : current) != phaseModifier
                || store.Name(node) != name
                || store.NamedBindings(node) != namedBindings
            ? Update(CreateImportClause(phaseModifier, name, namedBindings), node)
            : node;
    }

    internal Node UpdateImportAttributes(Node node, NodeList elements, bool? multiLine = null)
    {
        return store.Elements(node) != elements || (bool?)store.MultiLine(node) != multiLine
            ? Update(CreateImportAttributes(elements, multiLine, store.Token(node)), node)
            : node;
    }

    internal Node UpdateImportAttribute(Node node, Node name, Node value)
    {
        return store.Name(node) != name || store.Value(node) != value
            ? Update(CreateImportAttribute(name, value), node)
            : node;
    }

    internal Node UpdateNamespaceImport(Node node, Node name)
    {
        return store.Name(node) != name
            ? Update(CreateNamespaceImport(name), node)
            : node;
    }

    internal Node UpdateNamespaceExport(Node node, Node name)
    {
        return store.Name(node) != name
            ? Update(CreateNamespaceExport(name), node)
            : node;
    }

    internal Node UpdateNamedImports(Node node, NodeList elements)
    {
        return store.Elements(node) != elements
            ? Update(CreateNamedImports(elements), node)
            : node;
    }

    internal Node UpdateImportSpecifier(Node node, bool isTypeOnly, Node propertyName, Node name)
    {
        return store.IsTypeOnly(node) != isTypeOnly
                || store.PropertyName(node) != propertyName
                || store.Name(node) != name
            ? Update(CreateImportSpecifier(isTypeOnly, propertyName, name), node)
            : node;
    }

    internal Node UpdateExportAssignment(Node node, NodeList modifiers, Node expression)
    {
        return store.Modifiers(node) != modifiers || store.Expression(node) != expression
            ? Update(CreateExportAssignment(modifiers, store.IsExportEquals(node), expression), node)
            : node;
    }

    internal Node UpdateExportDeclaration(Node node, NodeList modifiers, bool isTypeOnly, Node exportClause, Node moduleSpecifier, Node attributes)
    {
        // (finishUpdateExportDeclaration: `if (updated.modifiers === original.modifiers) updated.modifiers =
        // original.modifiers;`, which changes nothing.)
        return store.Modifiers(node) != modifiers
                || store.IsTypeOnly(node) != isTypeOnly
                || store.ExportClause(node) != exportClause
                || store.ModuleSpecifier(node) != moduleSpecifier
                || store.Attributes(node) != attributes
            ? Update(CreateExportDeclaration(modifiers, isTypeOnly, exportClause, moduleSpecifier, attributes), node)
            : node;
    }

    internal Node UpdateNamedExports(Node node, NodeList elements)
    {
        return store.Elements(node) != elements
            ? Update(CreateNamedExports(elements), node)
            : node;
    }

    internal Node UpdateExportSpecifier(Node node, bool isTypeOnly, Node propertyName, Node name)
    {
        return store.IsTypeOnly(node) != isTypeOnly
                || store.PropertyName(node) != propertyName
                || store.Name(node) != name
            ? Update(CreateExportSpecifier(isTypeOnly, propertyName, name), node)
            : node;
    }

    internal Node UpdateExternalModuleReference(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateExternalModuleReference(expression), node)
            : node;
    }

    //// JSX

    internal Node UpdateJsxElement(Node node, Node openingElement, NodeList children, Node closingElement)
    {
        return store.OpeningElement(node) != openingElement
                || store.Children(node) != children
                || store.ClosingElement(node) != closingElement
            ? Update(CreateJsxElement(openingElement, children, closingElement), node)
            : node;
    }

    internal Node UpdateJsxSelfClosingElement(Node node, Node tagName, NodeList typeArguments, Node attributes)
    {
        return store.TagName(node) != tagName
                || store.TypeArguments(node) != typeArguments
                || store.Attributes(node) != attributes
            ? Update(CreateJsxSelfClosingElement(tagName, typeArguments, attributes), node)
            : node;
    }

    internal Node UpdateJsxOpeningElement(Node node, Node tagName, NodeList typeArguments, Node attributes)
    {
        return store.TagName(node) != tagName
                || store.TypeArguments(node) != typeArguments
                || store.Attributes(node) != attributes
            ? Update(CreateJsxOpeningElement(tagName, typeArguments, attributes), node)
            : node;
    }

    internal Node UpdateJsxClosingElement(Node node, Node tagName)
    {
        return store.TagName(node) != tagName
            ? Update(CreateJsxClosingElement(tagName), node)
            : node;
    }

    internal Node UpdateJsxFragment(Node node, Node openingFragment, NodeList children, Node closingFragment)
    {
        return store.OpeningFragment(node) != openingFragment
                || store.Children(node) != children
                || store.ClosingFragment(node) != closingFragment
            ? Update(CreateJsxFragment(openingFragment, children, closingFragment), node)
            : node;
    }

    internal Node UpdateJsxAttribute(Node node, Node name, Node initializer)
    {
        return store.Name(node) != name || store.Initializer(node) != initializer
            ? Update(CreateJsxAttribute(name, initializer), node)
            : node;
    }

    internal Node UpdateJsxAttributes(Node node, NodeList properties)
    {
        return store.Properties(node) != properties
            ? Update(CreateJsxAttributes(properties), node)
            : node;
    }

    internal Node UpdateJsxSpreadAttribute(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateJsxSpreadAttribute(expression), node)
            : node;
    }

    internal Node UpdateJsxExpression(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateJsxExpression(store.DotDotDotToken(node), expression), node)
            : node;
    }

    internal Node UpdateJsxNamespacedName(Node node, Node @namespace, Node name)
    {
        return store.Namespace(node) != @namespace || store.Name(node) != name
            ? Update(CreateJsxNamespacedName(@namespace, name), node)
            : node;
    }

    //// Clauses

    internal Node UpdateCaseClause(Node node, Node expression, NodeList statements)
    {
        return store.Expression(node) != expression || store.Statements(node) != statements
            ? Update(CreateCaseClause(expression, statements), node)
            : node;
    }

    internal Node UpdateDefaultClause(Node node, NodeList statements)
    {
        return store.Statements(node) != statements
            ? Update(CreateDefaultClause(statements), node)
            : node;
    }

    internal Node UpdateHeritageClause(Node node, NodeList types)
    {
        return store.Types(node) != types
            ? Update(CreateHeritageClause(store.Token(node), types), node)
            : node;
    }

    internal Node UpdateCatchClause(Node node, Node variableDeclaration, Node block)
    {
        return store.VariableDeclaration(node) != variableDeclaration || store.Block(node) != block
            ? Update(CreateCatchClause(variableDeclaration, block), node)
            : node;
    }

    //// Property assignments

    internal Node UpdatePropertyAssignment(Node node, Node name, Node initializer)
    {
        return store.Name(node) != name || store.Initializer(node) != initializer
            ? FinishUpdatePropertyAssignment(CreatePropertyAssignment(name, initializer), node)
            : node;
    }

    internal Node UpdateShorthandPropertyAssignment(Node node, Node name, Node objectAssignmentInitializer)
    {
        return store.Name(node) != name || store.ObjectAssignmentInitializer(node) != objectAssignmentInitializer
            ? FinishUpdateShorthandPropertyAssignment(CreateShorthandPropertyAssignment(name, objectAssignmentInitializer), node)
            : node;
    }

    internal Node UpdateSpreadAssignment(Node node, Node expression)
    {
        return store.Expression(node) != expression
            ? Update(CreateSpreadAssignment(expression), node)
            : node;
    }

    //// Enum

    internal Node UpdateEnumMember(Node node, Node name, Node initializer)
    {
        return store.Name(node) != name || store.Initializer(node) != initializer
            ? Update(CreateEnumMember(name, initializer), node)
            : node;
    }

    //// Synthesized

    internal Node UpdateCommaListExpression(Node node, NodeList elements)
    {
        return store.Elements(node) != elements
            ? Update(CreateCommaListExpression(store.Nodes(elements)), node)
            : node;
    }
}
