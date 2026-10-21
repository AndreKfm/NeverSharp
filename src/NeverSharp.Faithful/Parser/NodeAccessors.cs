// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/types.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/ast.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

namespace NeverSharp.Faithful;

/// <summary>
/// TypeScript reads and writes members structurally through interfaces (`(node as NamedDeclaration).name`,
/// `(node as HasModifiers).modifiers`, `(node as LiteralLikeNode).text`, ...). The node classes are separate
/// types, so these accessors dispatch on the class, as typescript-go's `Node.Name()`, `Node.Modifiers()`, ...
/// do on the node's data (internal/ast/ast.go). A node without the member reads as undefined (null, false).
/// Generated from the node classes by a helper script of the port.
/// </summary>
internal static class NodeAccessors
{

    /// <summary>`node.name` on any node.</summary>
    internal static Node? GetName(Node node) => node switch
    {
        ArrowFunction n => (Node?)n.Name,
        BindingElement n => n.Name,
        CallSignatureDeclaration n => n.Name,
        ClassDeclaration n => n.Name,
        ClassExpression n => n.Name,
        ClassStaticBlockDeclaration n => n.Name,
        ConstructSignatureDeclaration n => n.Name,
        ConstructorDeclaration n => n.Name,
        ConstructorTypeNode n => n.Name,
        EnumDeclaration n => n.Name,
        EnumMember n => n.Name,
        ExportAssignment n => n.Name,
        ExportDeclaration n => n.Name,
        ExportSpecifier n => n.Name,
        FunctionDeclaration n => n.Name,
        FunctionExpression n => n.Name,
        FunctionTypeNode n => n.Name,
        GetAccessorDeclaration n => n.Name,
        ImportAttribute n => n.Name,
        ImportClause n => n.Name,
        ImportEqualsDeclaration n => n.Name,
        ImportSpecifier n => n.Name,
        IndexSignatureDeclaration n => n.Name,
        InterfaceDeclaration n => n.Name,
        JSDocCallbackTag n => n.Name,
        JSDocFunctionType n => n.Name,
        JSDocLink n => n.Name,
        JSDocLinkCode n => n.Name,
        JSDocLinkPlain n => n.Name,
        JSDocNameReference n => n.Name,
        JSDocParameterTag n => n.Name,
        JSDocPropertyTag n => n.Name,
        JSDocSeeTag n => n.Name,
        JSDocTypedefTag n => n.Name,
        JsxAttribute n => n.Name,
        JsxNamespacedName n => n.Name,
        JsxSpreadAttribute n => n.Name,
        MetaProperty n => n.Name,
        MethodDeclaration n => n.Name,
        MethodSignature n => n.Name,
        MissingDeclaration n => n.Name,
        ModuleDeclaration n => n.Name,
        NamedTupleMember n => n.Name,
        NamespaceExport n => n.Name,
        NamespaceExportDeclaration n => n.Name,
        NamespaceImport n => n.Name,
        NotEmittedTypeElement n => n.Name,
        ParameterDeclaration n => n.Name,
        PropertyAccessExpression n => n.Name,
        PropertyAssignment n => n.Name,
        PropertyDeclaration n => n.Name,
        PropertySignature n => n.Name,
        SemicolonClassElement n => n.Name,
        SetAccessorDeclaration n => n.Name,
        ShorthandPropertyAssignment n => n.Name,
        SpreadAssignment n => n.Name,
        TypeAliasDeclaration n => n.Name,
        TypeParameterDeclaration n => n.Name,
        VariableDeclaration n => n.Name,
        _ => null,
    };

    /// <summary>`node.modifiers` on any node.</summary>
    internal static NodeArray? GetModifiers(Node node) => node switch
    {
        ArrowFunction n => n.Modifiers,
        ClassDeclaration n => n.Modifiers,
        ClassExpression n => n.Modifiers,
        ClassStaticBlockDeclaration n => n.Modifiers,
        ConstructorDeclaration n => n.Modifiers,
        ConstructorTypeNode n => n.Modifiers,
        EnumDeclaration n => n.Modifiers,
        ExportAssignment n => n.Modifiers,
        ExportDeclaration n => n.Modifiers,
        FunctionDeclaration n => n.Modifiers,
        FunctionExpression n => n.Modifiers,
        FunctionTypeNode n => n.Modifiers,
        GetAccessorDeclaration n => n.Modifiers,
        ImportDeclaration n => n.Modifiers,
        ImportEqualsDeclaration n => n.Modifiers,
        IndexSignatureDeclaration n => n.Modifiers,
        InterfaceDeclaration n => n.Modifiers,
        MethodDeclaration n => n.Modifiers,
        MethodSignature n => n.Modifiers,
        MissingDeclaration n => n.Modifiers,
        ModuleDeclaration n => n.Modifiers,
        NamespaceExportDeclaration n => n.Modifiers,
        ParameterDeclaration n => n.Modifiers,
        PropertyAssignment n => n.Modifiers,
        PropertyDeclaration n => n.Modifiers,
        PropertySignature n => n.Modifiers,
        SetAccessorDeclaration n => n.Modifiers,
        ShorthandPropertyAssignment n => n.Modifiers,
        TypeAliasDeclaration n => n.Modifiers,
        TypeParameterDeclaration n => n.Modifiers,
        VariableStatement n => n.Modifiers,
        _ => null,
    };

    /// <summary>`node.initializer` on any node.</summary>
    internal static Node? GetInitializer(Node node) => node switch
    {
        BindingElement n => n.Initializer,
        EnumMember n => n.Initializer,
        ForInStatement n => n.Initializer,
        ForOfStatement n => n.Initializer,
        ForStatement n => n.Initializer,
        JsxAttribute n => n.Initializer,
        ParameterDeclaration n => n.Initializer,
        PropertyAssignment n => n.Initializer,
        PropertyDeclaration n => n.Initializer,
        PropertySignature n => n.Initializer,
        VariableDeclaration n => n.Initializer,
        _ => null,
    };

    /// <summary>`node.type` on any node.</summary>
    internal static Node? GetTypeNode(Node node) => node switch
    {
        ArrowFunction n => n.Type,
        AsExpression n => n.Type,
        CallSignatureDeclaration n => n.Type,
        ConstructSignatureDeclaration n => n.Type,
        ConstructorDeclaration n => n.Type,
        ConstructorTypeNode n => n.Type,
        FunctionDeclaration n => n.Type,
        FunctionExpression n => n.Type,
        FunctionTypeNode n => n.Type,
        GetAccessorDeclaration n => n.Type,
        IndexSignatureDeclaration n => n.Type,
        JSDocFunctionType n => n.Type,
        JSDocNamepathType n => n.Type,
        JSDocNonNullableType n => n.Type,
        JSDocNullableType n => n.Type,
        JSDocOptionalType n => n.Type,
        JSDocSignature n => n.Type,
        JSDocTypeExpression n => n.Type,
        JSDocVariadicType n => n.Type,
        MappedTypeNode n => n.Type,
        MethodDeclaration n => n.Type,
        MethodSignature n => n.Type,
        NamedTupleMember n => n.Type,
        OptionalTypeNode n => n.Type,
        ParameterDeclaration n => n.Type,
        ParenthesizedTypeNode n => n.Type,
        PropertyDeclaration n => n.Type,
        PropertySignature n => n.Type,
        RestTypeNode n => n.Type,
        SatisfiesExpression n => n.Type,
        SetAccessorDeclaration n => n.Type,
        SyntheticExpression n => (Node?)n.Type,
        TemplateLiteralTypeSpan n => n.Type,
        TypeAliasDeclaration n => n.Type,
        TypeAssertion n => n.Type,
        TypeOperatorNode n => n.Type,
        TypePredicateNode n => n.Type,
        VariableDeclaration n => n.Type,
        _ => null,
    };

    /// <summary>`node.expression` on any node.</summary>
    internal static Node? GetExpression(Node node) => node switch
    {
        AsExpression n => n.Expression,
        AwaitExpression n => n.Expression,
        CallExpression n => n.Expression,
        CaseClause n => n.Expression,
        ComputedPropertyName n => n.Expression,
        Decorator n => n.Expression,
        DeleteExpression n => n.Expression,
        DoStatement n => n.Expression,
        ElementAccessExpression n => n.Expression,
        ExportAssignment n => n.Expression,
        ExpressionStatement n => n.Expression,
        ExpressionWithTypeArguments n => n.Expression,
        ExternalModuleReference n => n.Expression,
        ForInStatement n => n.Expression,
        ForOfStatement n => n.Expression,
        IfStatement n => n.Expression,
        JsxExpression n => n.Expression,
        JsxSpreadAttribute n => n.Expression,
        NewExpression n => n.Expression,
        NonNullExpression n => n.Expression,
        ParenthesizedExpression n => n.Expression,
        PartiallyEmittedExpression n => n.Expression,
        PropertyAccessExpression n => n.Expression,
        ReturnStatement n => n.Expression,
        SatisfiesExpression n => n.Expression,
        SpreadAssignment n => n.Expression,
        SpreadElement n => n.Expression,
        SwitchStatement n => n.Expression,
        SyntheticReferenceExpression n => n.Expression,
        TemplateSpan n => n.Expression,
        ThrowStatement n => n.Expression,
        TypeAssertion n => n.Expression,
        TypeOfExpression n => n.Expression,
        TypeParameterDeclaration n => n.Expression,
        VoidExpression n => n.Expression,
        WhileStatement n => n.Expression,
        WithStatement n => n.Expression,
        YieldExpression n => n.Expression,
        _ => null,
    };

    /// <summary>`node.body` on any node.</summary>
    internal static Node? GetBody(Node node) => node switch
    {
        ArrowFunction n => n.Body,
        ClassStaticBlockDeclaration n => n.Body,
        ConstructorDeclaration n => n.Body,
        FunctionDeclaration n => n.Body,
        FunctionExpression n => n.Body,
        GetAccessorDeclaration n => n.Body,
        MethodDeclaration n => n.Body,
        ModuleDeclaration n => n.Body,
        SetAccessorDeclaration n => n.Body,
        _ => null,
    };

    /// <summary>`node.parameters` on any node.</summary>
    internal static NodeArray? GetParameters(Node node) => node switch
    {
        ArrowFunction n => n.Parameters,
        CallSignatureDeclaration n => n.Parameters,
        ConstructSignatureDeclaration n => n.Parameters,
        ConstructorDeclaration n => n.Parameters,
        ConstructorTypeNode n => n.Parameters,
        FunctionDeclaration n => n.Parameters,
        FunctionExpression n => n.Parameters,
        FunctionTypeNode n => n.Parameters,
        GetAccessorDeclaration n => n.Parameters,
        IndexSignatureDeclaration n => n.Parameters,
        JSDocFunctionType n => n.Parameters,
        MethodDeclaration n => n.Parameters,
        MethodSignature n => n.Parameters,
        SetAccessorDeclaration n => n.Parameters,
        _ => null,
    };

    /// <summary>`node.typeParameters` on any node.</summary>
    internal static NodeArray? GetTypeParameters(Node node) => node switch
    {
        ArrowFunction n => n.TypeParameters,
        CallSignatureDeclaration n => n.TypeParameters,
        ClassDeclaration n => n.TypeParameters,
        ClassExpression n => n.TypeParameters,
        ConstructSignatureDeclaration n => n.TypeParameters,
        ConstructorDeclaration n => n.TypeParameters,
        ConstructorTypeNode n => n.TypeParameters,
        FunctionDeclaration n => n.TypeParameters,
        FunctionExpression n => n.TypeParameters,
        FunctionTypeNode n => n.TypeParameters,
        GetAccessorDeclaration n => n.TypeParameters,
        IndexSignatureDeclaration n => n.TypeParameters,
        InterfaceDeclaration n => n.TypeParameters,
        JSDocFunctionType n => n.TypeParameters,
        JSDocTemplateTag n => n.TypeParameters,
        MethodDeclaration n => n.TypeParameters,
        MethodSignature n => n.TypeParameters,
        SetAccessorDeclaration n => n.TypeParameters,
        TypeAliasDeclaration n => n.TypeParameters,
        _ => null,
    };

    /// <summary>`node.typeArguments` on any node.</summary>
    internal static NodeArray? GetTypeArguments(Node node) => node switch
    {
        ArrowFunction n => n.TypeArguments,
        CallExpression n => n.TypeArguments,
        CallSignatureDeclaration n => n.TypeArguments,
        ConstructSignatureDeclaration n => n.TypeArguments,
        ConstructorDeclaration n => n.TypeArguments,
        ConstructorTypeNode n => n.TypeArguments,
        ExpressionWithTypeArguments n => n.TypeArguments,
        FunctionDeclaration n => n.TypeArguments,
        FunctionExpression n => n.TypeArguments,
        FunctionTypeNode n => n.TypeArguments,
        GetAccessorDeclaration n => n.TypeArguments,
        ImportTypeNode n => n.TypeArguments,
        IndexSignatureDeclaration n => n.TypeArguments,
        JSDocFunctionType n => n.TypeArguments,
        JsxOpeningElement n => n.TypeArguments,
        JsxSelfClosingElement n => n.TypeArguments,
        MethodDeclaration n => n.TypeArguments,
        MethodSignature n => n.TypeArguments,
        NewExpression n => n.TypeArguments,
        SetAccessorDeclaration n => n.TypeArguments,
        TaggedTemplateExpression n => n.TypeArguments,
        TypeQueryNode n => n.TypeArguments,
        TypeReferenceNode n => n.TypeArguments,
        _ => null,
    };

    /// <summary>`node.questionToken` on any node.</summary>
    internal static Node? GetQuestionToken(Node node) => node switch
    {
        ArrowFunction n => n.QuestionToken,
        CallSignatureDeclaration n => n.QuestionToken,
        ConditionalExpression n => n.QuestionToken,
        ConstructSignatureDeclaration n => n.QuestionToken,
        ConstructorDeclaration n => n.QuestionToken,
        FunctionDeclaration n => n.QuestionToken,
        FunctionExpression n => n.QuestionToken,
        GetAccessorDeclaration n => n.QuestionToken,
        IndexSignatureDeclaration n => n.QuestionToken,
        MappedTypeNode n => n.QuestionToken,
        MethodDeclaration n => n.QuestionToken,
        MethodSignature n => n.QuestionToken,
        NamedTupleMember n => n.QuestionToken,
        NotEmittedTypeElement n => n.QuestionToken,
        ParameterDeclaration n => n.QuestionToken,
        PropertyAssignment n => n.QuestionToken,
        PropertyDeclaration n => n.QuestionToken,
        PropertySignature n => n.QuestionToken,
        SetAccessorDeclaration n => n.QuestionToken,
        ShorthandPropertyAssignment n => n.QuestionToken,
        _ => null,
    };

    /// <summary>`node.exclamationToken` on any node.</summary>
    internal static Node? GetExclamationToken(Node node) => node switch
    {
        ArrowFunction n => n.ExclamationToken,
        ConstructorDeclaration n => n.ExclamationToken,
        FunctionDeclaration n => n.ExclamationToken,
        FunctionExpression n => n.ExclamationToken,
        GetAccessorDeclaration n => n.ExclamationToken,
        MethodDeclaration n => n.ExclamationToken,
        PropertyAssignment n => n.ExclamationToken,
        PropertyDeclaration n => n.ExclamationToken,
        SetAccessorDeclaration n => n.ExclamationToken,
        ShorthandPropertyAssignment n => n.ExclamationToken,
        VariableDeclaration n => n.ExclamationToken,
        _ => null,
    };

    /// <summary>`node.asteriskToken` on any node.</summary>
    internal static Node? GetAsteriskToken(Node node) => node switch
    {
        ArrowFunction n => n.AsteriskToken,
        ConstructorDeclaration n => n.AsteriskToken,
        FunctionDeclaration n => n.AsteriskToken,
        FunctionExpression n => n.AsteriskToken,
        GetAccessorDeclaration n => n.AsteriskToken,
        MethodDeclaration n => n.AsteriskToken,
        SetAccessorDeclaration n => n.AsteriskToken,
        YieldExpression n => n.AsteriskToken,
        _ => null,
    };

    /// <summary>`node.dotDotDotToken` on any node.</summary>
    internal static Node? GetDotDotDotToken(Node node) => node switch
    {
        BindingElement n => n.DotDotDotToken,
        JsxExpression n => n.DotDotDotToken,
        NamedTupleMember n => n.DotDotDotToken,
        ParameterDeclaration n => n.DotDotDotToken,
        _ => null,
    };

    /// <summary>`node.statements` on any node.</summary>
    internal static NodeArray? GetStatements(Node node) => node switch
    {
        Block n => n.Statements,
        CaseClause n => n.Statements,
        DefaultClause n => n.Statements,
        ModuleBlock n => n.Statements,
        SourceFile n => n.Statements,
        _ => null,
    };

    /// <summary>`node.text` on any node.</summary>
    internal static string? GetText(Node node) => node switch
    {
        BigIntLiteral n => n.Text,
        JSDocLink n => n.Text,
        JSDocLinkCode n => n.Text,
        JSDocLinkPlain n => n.Text,
        JSDocText n => n.Text,
        JsxText n => n.Text,
        NoSubstitutionTemplateLiteral n => n.Text,
        NumericLiteral n => n.Text,
        RegularExpressionLiteral n => n.Text,
        SourceFile n => n.Text,
        StringLiteral n => n.Text,
        TemplateHead n => n.Text,
        TemplateMiddle n => n.Text,
        TemplateTail n => n.Text,
        _ => null,
    };

    /// <summary>`node.elements` on any node.</summary>
    internal static NodeArray? GetElements(Node node) => node switch
    {
        ArrayBindingPattern n => n.Elements,
        ArrayLiteralExpression n => n.Elements,
        CommaListExpression n => n.Elements,
        ImportAttributes n => n.Elements,
        NamedExports n => n.Elements,
        NamedImports n => n.Elements,
        ObjectBindingPattern n => n.Elements,
        TupleTypeNode n => n.Elements,
        _ => null,
    };

    /// <summary>`node.members` on any node.</summary>
    internal static NodeArray? GetMembers(Node node) => node switch
    {
        ClassDeclaration n => n.Members,
        ClassExpression n => n.Members,
        EnumDeclaration n => n.Members,
        InterfaceDeclaration n => n.Members,
        MappedTypeNode n => n.Members,
        TypeLiteralNode n => n.Members,
        _ => null,
    };

    /// <summary>`node.properties` on any node.</summary>
    internal static NodeArray? GetProperties(Node node) => node switch
    {
        JsxAttributes n => n.Properties,
        ObjectLiteralExpression n => n.Properties,
        _ => null,
    };

    /// <summary>`node.label` on any node.</summary>
    internal static Node? GetLabel(Node node) => node switch
    {
        BreakStatement n => n.Label,
        ContinueStatement n => n.Label,
        LabeledStatement n => n.Label,
        _ => null,
    };

    /// <summary>`node.literal` on any node.</summary>
    internal static Node? GetLiteral(Node node) => node switch
    {
        LiteralTypeNode n => n.Literal,
        TemplateLiteralTypeSpan n => n.Literal,
        TemplateSpan n => n.Literal,
        _ => null,
    };

    /// <summary>`node.tags` on any node.</summary>
    internal static NodeArray? GetTags(Node node) => node switch
    {
        JSDoc n => n.Tags,
        _ => null,
    };

    /// <summary>`node.tagName` on any node.</summary>
    internal static Node? GetTagName(Node node) => node switch
    {
        JSDocAugmentsTag n => n.TagName,
        JSDocAuthorTag n => n.TagName,
        JSDocCallbackTag n => n.TagName,
        JSDocClassTag n => n.TagName,
        JSDocDeprecatedTag n => n.TagName,
        JSDocEnumTag n => n.TagName,
        JSDocImplementsTag n => n.TagName,
        JSDocImportTag n => n.TagName,
        JSDocOverloadTag n => n.TagName,
        JSDocOverrideTag n => n.TagName,
        JSDocParameterTag n => n.TagName,
        JSDocPrivateTag n => n.TagName,
        JSDocPropertyTag n => n.TagName,
        JSDocProtectedTag n => n.TagName,
        JSDocPublicTag n => n.TagName,
        JSDocReadonlyTag n => n.TagName,
        JSDocReturnTag n => n.TagName,
        JSDocSatisfiesTag n => n.TagName,
        JSDocSeeTag n => n.TagName,
        JSDocTemplateTag n => n.TagName,
        JSDocThisTag n => n.TagName,
        JSDocThrowsTag n => n.TagName,
        JSDocTypeTag n => n.TagName,
        JSDocTypedefTag n => n.TagName,
        JSDocUnknownTag n => n.TagName,
        JsxClosingElement n => n.TagName,
        JsxOpeningElement n => n.TagName,
        JsxSelfClosingElement n => n.TagName,
        _ => null,
    };

    /// <summary>`node.typeExpression` on any node.</summary>
    internal static Node? GetTypeExpression(Node node) => node switch
    {
        JSDocCallbackTag n => n.TypeExpression,
        JSDocEnumTag n => n.TypeExpression,
        JSDocOverloadTag n => n.TypeExpression,
        JSDocParameterTag n => n.TypeExpression,
        JSDocPropertyTag n => n.TypeExpression,
        JSDocReturnTag n => n.TypeExpression,
        JSDocSatisfiesTag n => n.TypeExpression,
        JSDocThisTag n => n.TypeExpression,
        JSDocThrowsTag n => n.TypeExpression,
        JSDocTypeTag n => n.TypeExpression,
        JSDocTypedefTag n => n.TypeExpression,
        _ => null,
    };

    /// <summary>`node.propertyName` on any node.</summary>
    internal static Node? GetPropertyName(Node node) => node switch
    {
        BindingElement n => n.PropertyName,
        ExportSpecifier n => n.PropertyName,
        ImportSpecifier n => n.PropertyName,
        _ => null,
    };

    /// <summary>`node.left` on any node.</summary>
    internal static Node? GetLeft(Node node) => node switch
    {
        BinaryExpression n => n.Left,
        JSDocMemberName n => n.Left,
        QualifiedName n => n.Left,
        _ => null,
    };

    /// <summary>`node.right` on any node.</summary>
    internal static Node? GetRight(Node node) => node switch
    {
        BinaryExpression n => n.Right,
        JSDocMemberName n => n.Right,
        QualifiedName n => n.Right,
        _ => null,
    };

    /// <summary>`node.arguments` on any node.</summary>
    internal static NodeArray? GetArguments(Node node) => node switch
    {
        CallExpression n => n.Arguments,
        NewExpression n => n.Arguments,
        _ => null,
    };

    /// <summary>`node.statement` on any node.</summary>
    internal static Node? GetStatement(Node node) => node switch
    {
        DoStatement n => n.Statement,
        ForInStatement n => n.Statement,
        ForOfStatement n => n.Statement,
        ForStatement n => n.Statement,
        LabeledStatement n => n.Statement,
        WhileStatement n => n.Statement,
        WithStatement n => n.Statement,
        _ => null,
    };

    /// <summary>`node.moduleSpecifier` on any node.</summary>
    internal static Node? GetModuleSpecifier(Node node) => node switch
    {
        ExportDeclaration n => n.ModuleSpecifier,
        ImportDeclaration n => n.ModuleSpecifier,
        JSDocImportTag n => n.ModuleSpecifier,
        _ => null,
    };

    /// <summary>`node.heritageClauses` on any node.</summary>
    internal static NodeArray? GetHeritageClauses(Node node) => node switch
    {
        ClassDeclaration n => n.HeritageClauses,
        ClassExpression n => n.HeritageClauses,
        InterfaceDeclaration n => n.HeritageClauses,
        _ => null,
    };

    /// <summary>`node.constraint` on any node.</summary>
    internal static Node? GetConstraint(Node node) => node switch
    {
        JSDocTemplateTag n => n.Constraint,
        TypeParameterDeclaration n => n.Constraint,
        _ => null,
    };

    /// <summary>`node.questionDotToken` on any node.</summary>
    internal static Node? GetQuestionDotToken(Node node) => node switch
    {
        CallExpression n => n.QuestionDotToken,
        ElementAccessExpression n => n.QuestionDotToken,
        PropertyAccessExpression n => n.QuestionDotToken,
        TaggedTemplateExpression n => n.QuestionDotToken,
        _ => null,
    };

    /// <summary>`node.operand` on any node.</summary>
    internal static Node? GetOperand(Node node) => node switch
    {
        PostfixUnaryExpression n => n.Operand,
        PrefixUnaryExpression n => n.Operand,
        _ => null,
    };

    /// <summary>`node.isTypeOnly` on any node.</summary>
    internal static bool GetIsTypeOnly(Node node) => node switch
    {
        ExportDeclaration n => n.IsTypeOnly,
        ExportSpecifier n => n.IsTypeOnly,
        ImportClause n => n.IsTypeOnly,
        ImportEqualsDeclaration n => n.IsTypeOnly,
        ImportSpecifier n => n.IsTypeOnly,
        _ => false,
    };

    /// <summary>`node.text = value` on a node that declares the member.</summary>
    internal static void SetText(Node node, string value)
    {
        switch (node)
        {
            case BigIntLiteral n:
                n.Text = value;
                break;
            case JSDocLink n:
                n.Text = value;
                break;
            case JSDocLinkCode n:
                n.Text = value;
                break;
            case JSDocLinkPlain n:
                n.Text = value;
                break;
            case JSDocText n:
                n.Text = value;
                break;
            case JsxText n:
                n.Text = value;
                break;
            case NoSubstitutionTemplateLiteral n:
                n.Text = value;
                break;
            case NumericLiteral n:
                n.Text = value;
                break;
            case RegularExpressionLiteral n:
                n.Text = value;
                break;
            case SourceFile n:
                n.Text = value;
                break;
            case StringLiteral n:
                n.Text = value;
                break;
            case TemplateHead n:
                n.Text = value;
                break;
            case TemplateMiddle n:
                n.Text = value;
                break;
            case TemplateTail n:
                n.Text = value;
                break;
            default:
                throw new InvalidOperationException($"{node.Kind} has no member Text");
        }
    }

    /// <summary>`node.hasExtendedUnicodeEscape = value` on a node that declares the member.</summary>
    internal static void SetHasExtendedUnicodeEscape(Node node, bool value)
    {
        switch (node)
        {
            case BigIntLiteral n:
                n.HasExtendedUnicodeEscape = value;
                break;
            case JsxText n:
                n.HasExtendedUnicodeEscape = value;
                break;
            case NoSubstitutionTemplateLiteral n:
                n.HasExtendedUnicodeEscape = value;
                break;
            case NumericLiteral n:
                n.HasExtendedUnicodeEscape = value;
                break;
            case RegularExpressionLiteral n:
                n.HasExtendedUnicodeEscape = value;
                break;
            case StringLiteral n:
                n.HasExtendedUnicodeEscape = value;
                break;
            case TemplateHead n:
                n.HasExtendedUnicodeEscape = value;
                break;
            case TemplateMiddle n:
                n.HasExtendedUnicodeEscape = value;
                break;
            case TemplateTail n:
                n.HasExtendedUnicodeEscape = value;
                break;
            default:
                throw new InvalidOperationException($"{node.Kind} has no member HasExtendedUnicodeEscape");
        }
    }

    /// <summary>`node.isUnterminated = value` on a node that declares the member.</summary>
    internal static void SetIsUnterminated(Node node, bool value)
    {
        switch (node)
        {
            case BigIntLiteral n:
                n.IsUnterminated = value;
                break;
            case JsxText n:
                n.IsUnterminated = value;
                break;
            case NoSubstitutionTemplateLiteral n:
                n.IsUnterminated = value;
                break;
            case NumericLiteral n:
                n.IsUnterminated = value;
                break;
            case RegularExpressionLiteral n:
                n.IsUnterminated = value;
                break;
            case StringLiteral n:
                n.IsUnterminated = value;
                break;
            case TemplateHead n:
                n.IsUnterminated = value;
                break;
            case TemplateMiddle n:
                n.IsUnterminated = value;
                break;
            case TemplateTail n:
                n.IsUnterminated = value;
                break;
            default:
                throw new InvalidOperationException($"{node.Kind} has no member IsUnterminated");
        }
    }

    /// <summary>`node.modifiers = value` on a node that declares the member.</summary>
    internal static void SetModifiers(Node node, NodeArray? value)
    {
        switch (node)
        {
            case ArrowFunction n:
                n.Modifiers = value;
                break;
            case ClassDeclaration n:
                n.Modifiers = value;
                break;
            case ClassExpression n:
                n.Modifiers = value;
                break;
            case ClassStaticBlockDeclaration n:
                n.Modifiers = value;
                break;
            case ConstructorDeclaration n:
                n.Modifiers = value;
                break;
            case ConstructorTypeNode n:
                n.Modifiers = value;
                break;
            case EnumDeclaration n:
                n.Modifiers = value;
                break;
            case ExportAssignment n:
                n.Modifiers = value;
                break;
            case ExportDeclaration n:
                n.Modifiers = value;
                break;
            case FunctionDeclaration n:
                n.Modifiers = value;
                break;
            case FunctionExpression n:
                n.Modifiers = value;
                break;
            case GetAccessorDeclaration n:
                n.Modifiers = value;
                break;
            case ImportDeclaration n:
                n.Modifiers = value;
                break;
            case ImportEqualsDeclaration n:
                n.Modifiers = value;
                break;
            case IndexSignatureDeclaration n:
                n.Modifiers = value;
                break;
            case InterfaceDeclaration n:
                n.Modifiers = value;
                break;
            case MethodDeclaration n:
                n.Modifiers = value;
                break;
            case MethodSignature n:
                n.Modifiers = value;
                break;
            case MissingDeclaration n:
                n.Modifiers = value;
                break;
            case ModuleDeclaration n:
                n.Modifiers = value;
                break;
            case NamespaceExportDeclaration n:
                n.Modifiers = value;
                break;
            case ParameterDeclaration n:
                n.Modifiers = value;
                break;
            case PropertyAssignment n:
                n.Modifiers = value;
                break;
            case PropertyDeclaration n:
                n.Modifiers = value;
                break;
            case PropertySignature n:
                n.Modifiers = value;
                break;
            case SetAccessorDeclaration n:
                n.Modifiers = value;
                break;
            case ShorthandPropertyAssignment n:
                n.Modifiers = value;
                break;
            case TypeAliasDeclaration n:
                n.Modifiers = value;
                break;
            case TypeParameterDeclaration n:
                n.Modifiers = value;
                break;
            case VariableStatement n:
                n.Modifiers = value;
                break;
            default:
                throw new InvalidOperationException($"{node.Kind} has no member Modifiers");
        }
    }

    /// <summary>`node.name = value` on a node that declares the member.</summary>
    internal static void SetName(Node node, Node? value)
    {
        switch (node)
        {
            case BindingElement n:
                n.Name = value!;
                break;
            case CallSignatureDeclaration n:
                n.Name = value;
                break;
            case ClassDeclaration n:
                n.Name = (Identifier?)value;
                break;
            case ClassExpression n:
                n.Name = (Identifier?)value;
                break;
            case ClassStaticBlockDeclaration n:
                n.Name = value;
                break;
            case ConstructSignatureDeclaration n:
                n.Name = value;
                break;
            case ConstructorDeclaration n:
                n.Name = value;
                break;
            case ConstructorTypeNode n:
                n.Name = value;
                break;
            case EnumDeclaration n:
                n.Name = (Identifier)value!;
                break;
            case EnumMember n:
                n.Name = value!;
                break;
            case ExportAssignment n:
                n.Name = value;
                break;
            case ExportDeclaration n:
                n.Name = value;
                break;
            case ExportSpecifier n:
                n.Name = value!;
                break;
            case FunctionDeclaration n:
                n.Name = (Identifier?)value;
                break;
            case FunctionExpression n:
                n.Name = (Identifier?)value;
                break;
            case FunctionTypeNode n:
                n.Name = value;
                break;
            case GetAccessorDeclaration n:
                n.Name = value!;
                break;
            case ImportAttribute n:
                n.Name = value!;
                break;
            case ImportClause n:
                n.Name = (Identifier?)value;
                break;
            case ImportEqualsDeclaration n:
                n.Name = (Identifier)value!;
                break;
            case ImportSpecifier n:
                n.Name = (Identifier)value!;
                break;
            case IndexSignatureDeclaration n:
                n.Name = value;
                break;
            case InterfaceDeclaration n:
                n.Name = (Identifier)value!;
                break;
            case JSDocCallbackTag n:
                n.Name = (Identifier?)value;
                break;
            case JSDocFunctionType n:
                n.Name = value;
                break;
            case JSDocLink n:
                n.Name = value;
                break;
            case JSDocLinkCode n:
                n.Name = value;
                break;
            case JSDocLinkPlain n:
                n.Name = value;
                break;
            case JSDocNameReference n:
                n.Name = value!;
                break;
            case JSDocParameterTag n:
                n.Name = value!;
                break;
            case JSDocPropertyTag n:
                n.Name = value!;
                break;
            case JSDocSeeTag n:
                n.Name = (JSDocNameReference?)value;
                break;
            case JSDocTypedefTag n:
                n.Name = (Identifier?)value;
                break;
            case JsxAttribute n:
                n.Name = value!;
                break;
            case JsxNamespacedName n:
                n.Name = (Identifier)value!;
                break;
            case JsxSpreadAttribute n:
                n.Name = value;
                break;
            case MetaProperty n:
                n.Name = (Identifier)value!;
                break;
            case MethodDeclaration n:
                n.Name = value!;
                break;
            case MethodSignature n:
                n.Name = value!;
                break;
            case MissingDeclaration n:
                n.Name = (Identifier?)value;
                break;
            case ModuleDeclaration n:
                n.Name = value!;
                break;
            case NamedTupleMember n:
                n.Name = (Identifier)value!;
                break;
            case NamespaceExport n:
                n.Name = value!;
                break;
            case NamespaceExportDeclaration n:
                n.Name = (Identifier)value!;
                break;
            case NamespaceImport n:
                n.Name = (Identifier)value!;
                break;
            case NotEmittedTypeElement n:
                n.Name = value;
                break;
            case ParameterDeclaration n:
                n.Name = value!;
                break;
            case PropertyAccessExpression n:
                n.Name = value!;
                break;
            case PropertyAssignment n:
                n.Name = value!;
                break;
            case PropertyDeclaration n:
                n.Name = value!;
                break;
            case PropertySignature n:
                n.Name = value!;
                break;
            case SemicolonClassElement n:
                n.Name = value;
                break;
            case SetAccessorDeclaration n:
                n.Name = value!;
                break;
            case ShorthandPropertyAssignment n:
                n.Name = (Identifier)value!;
                break;
            case SpreadAssignment n:
                n.Name = value;
                break;
            case TypeAliasDeclaration n:
                n.Name = (Identifier)value!;
                break;
            case TypeParameterDeclaration n:
                n.Name = (Identifier)value!;
                break;
            case VariableDeclaration n:
                n.Name = value!;
                break;
            default:
                throw new InvalidOperationException($"{node.Kind} has no member Name");
        }
    }

    /// <summary>`node.initializer = value` on a node that declares the member.</summary>
    internal static void SetInitializer(Node node, Node? value)
    {
        switch (node)
        {
            case BindingElement n:
                n.Initializer = value;
                break;
            case EnumMember n:
                n.Initializer = value;
                break;
            case ForInStatement n:
                n.Initializer = value!;
                break;
            case ForOfStatement n:
                n.Initializer = value!;
                break;
            case ForStatement n:
                n.Initializer = value;
                break;
            case JsxAttribute n:
                n.Initializer = value;
                break;
            case ParameterDeclaration n:
                n.Initializer = value;
                break;
            case PropertyAssignment n:
                n.Initializer = value!;
                break;
            case PropertyDeclaration n:
                n.Initializer = value;
                break;
            case PropertySignature n:
                n.Initializer = value;
                break;
            case VariableDeclaration n:
                n.Initializer = value;
                break;
            default:
                throw new InvalidOperationException($"{node.Kind} has no member Initializer");
        }
    }

    /// <summary>`node.questionToken = value` on a node that declares the member.</summary>
    internal static void SetQuestionToken(Node node, Node? value)
    {
        switch (node)
        {
            case ArrowFunction n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case CallSignatureDeclaration n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case ConditionalExpression n:
                n.QuestionToken = (QuestionToken)value!;
                break;
            case ConstructSignatureDeclaration n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case ConstructorDeclaration n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case FunctionDeclaration n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case FunctionExpression n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case GetAccessorDeclaration n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case IndexSignatureDeclaration n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case MappedTypeNode n:
                n.QuestionToken = value;
                break;
            case MethodDeclaration n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case MethodSignature n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case NamedTupleMember n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case NotEmittedTypeElement n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case ParameterDeclaration n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case PropertyAssignment n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case PropertyDeclaration n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case PropertySignature n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case SetAccessorDeclaration n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            case ShorthandPropertyAssignment n:
                n.QuestionToken = (QuestionToken?)value;
                break;
            default:
                throw new InvalidOperationException($"{node.Kind} has no member QuestionToken");
        }
    }

    /// <summary>`node.exclamationToken = value` on a node that declares the member.</summary>
    internal static void SetExclamationToken(Node node, Node? value)
    {
        switch (node)
        {
            case ArrowFunction n:
                n.ExclamationToken = (ExclamationToken?)value;
                break;
            case ConstructorDeclaration n:
                n.ExclamationToken = (ExclamationToken?)value;
                break;
            case FunctionDeclaration n:
                n.ExclamationToken = (ExclamationToken?)value;
                break;
            case FunctionExpression n:
                n.ExclamationToken = (ExclamationToken?)value;
                break;
            case GetAccessorDeclaration n:
                n.ExclamationToken = (ExclamationToken?)value;
                break;
            case MethodDeclaration n:
                n.ExclamationToken = (ExclamationToken?)value;
                break;
            case PropertyAssignment n:
                n.ExclamationToken = (ExclamationToken?)value;
                break;
            case PropertyDeclaration n:
                n.ExclamationToken = (ExclamationToken?)value;
                break;
            case SetAccessorDeclaration n:
                n.ExclamationToken = (ExclamationToken?)value;
                break;
            case ShorthandPropertyAssignment n:
                n.ExclamationToken = (ExclamationToken?)value;
                break;
            case VariableDeclaration n:
                n.ExclamationToken = (ExclamationToken?)value;
                break;
            default:
                throw new InvalidOperationException($"{node.Kind} has no member ExclamationToken");
        }
    }

    /// <summary>`node.expression = value` on a node that declares the member.</summary>
    internal static void SetExpression(Node node, Node? value)
    {
        switch (node)
        {
            case AsExpression n:
                n.Expression = value!;
                break;
            case AwaitExpression n:
                n.Expression = value!;
                break;
            case CallExpression n:
                n.Expression = value!;
                break;
            case CaseClause n:
                n.Expression = value!;
                break;
            case ComputedPropertyName n:
                n.Expression = value!;
                break;
            case Decorator n:
                n.Expression = value!;
                break;
            case DeleteExpression n:
                n.Expression = value!;
                break;
            case DoStatement n:
                n.Expression = value!;
                break;
            case ElementAccessExpression n:
                n.Expression = value!;
                break;
            case ExportAssignment n:
                n.Expression = value!;
                break;
            case ExpressionStatement n:
                n.Expression = value!;
                break;
            case ExpressionWithTypeArguments n:
                n.Expression = value!;
                break;
            case ExternalModuleReference n:
                n.Expression = value!;
                break;
            case ForInStatement n:
                n.Expression = value!;
                break;
            case ForOfStatement n:
                n.Expression = value!;
                break;
            case IfStatement n:
                n.Expression = value!;
                break;
            case JsxExpression n:
                n.Expression = value;
                break;
            case JsxSpreadAttribute n:
                n.Expression = value!;
                break;
            case NewExpression n:
                n.Expression = value!;
                break;
            case NonNullExpression n:
                n.Expression = value!;
                break;
            case ParenthesizedExpression n:
                n.Expression = value!;
                break;
            case PartiallyEmittedExpression n:
                n.Expression = value!;
                break;
            case PropertyAccessExpression n:
                n.Expression = value!;
                break;
            case ReturnStatement n:
                n.Expression = value;
                break;
            case SatisfiesExpression n:
                n.Expression = value!;
                break;
            case SpreadAssignment n:
                n.Expression = value!;
                break;
            case SpreadElement n:
                n.Expression = value!;
                break;
            case SwitchStatement n:
                n.Expression = value!;
                break;
            case SyntheticReferenceExpression n:
                n.Expression = value!;
                break;
            case TemplateSpan n:
                n.Expression = value!;
                break;
            case ThrowStatement n:
                n.Expression = value!;
                break;
            case TypeAssertion n:
                n.Expression = value!;
                break;
            case TypeOfExpression n:
                n.Expression = value!;
                break;
            case TypeParameterDeclaration n:
                n.Expression = value;
                break;
            case VoidExpression n:
                n.Expression = value!;
                break;
            case WhileStatement n:
                n.Expression = value!;
                break;
            case WithStatement n:
                n.Expression = value!;
                break;
            case YieldExpression n:
                n.Expression = value;
                break;
            default:
                throw new InvalidOperationException($"{node.Kind} has no member Expression");
        }
    }

    /// <summary>`node.typeParameters = value` on a node that declares the member.</summary>
    internal static void SetTypeParameters(Node node, NodeArray? value)
    {
        switch (node)
        {
            case ArrowFunction n:
                n.TypeParameters = value;
                break;
            case CallSignatureDeclaration n:
                n.TypeParameters = value;
                break;
            case ClassDeclaration n:
                n.TypeParameters = value;
                break;
            case ClassExpression n:
                n.TypeParameters = value;
                break;
            case ConstructSignatureDeclaration n:
                n.TypeParameters = value;
                break;
            case ConstructorDeclaration n:
                n.TypeParameters = value;
                break;
            case ConstructorTypeNode n:
                n.TypeParameters = value;
                break;
            case FunctionDeclaration n:
                n.TypeParameters = value;
                break;
            case FunctionExpression n:
                n.TypeParameters = value;
                break;
            case FunctionTypeNode n:
                n.TypeParameters = value;
                break;
            case GetAccessorDeclaration n:
                n.TypeParameters = value;
                break;
            case IndexSignatureDeclaration n:
                n.TypeParameters = value;
                break;
            case InterfaceDeclaration n:
                n.TypeParameters = value;
                break;
            case JSDocFunctionType n:
                n.TypeParameters = value;
                break;
            case JSDocTemplateTag n:
                n.TypeParameters = value!;
                break;
            case MethodDeclaration n:
                n.TypeParameters = value;
                break;
            case MethodSignature n:
                n.TypeParameters = value;
                break;
            case SetAccessorDeclaration n:
                n.TypeParameters = value;
                break;
            case TypeAliasDeclaration n:
                n.TypeParameters = value;
                break;
            default:
                throw new InvalidOperationException($"{node.Kind} has no member TypeParameters");
        }
    }

    /// <summary>`node.body = value` on a node that declares the member.</summary>
    internal static void SetBody(Node node, Node? value)
    {
        switch (node)
        {
            case ArrowFunction n:
                n.Body = value!;
                break;
            case ClassStaticBlockDeclaration n:
                n.Body = (Block)value!;
                break;
            case ConstructorDeclaration n:
                n.Body = (Block?)value;
                break;
            case FunctionDeclaration n:
                n.Body = (Block?)value;
                break;
            case FunctionExpression n:
                n.Body = (Block)value!;
                break;
            case GetAccessorDeclaration n:
                n.Body = (Block?)value;
                break;
            case MethodDeclaration n:
                n.Body = (Block?)value;
                break;
            case ModuleDeclaration n:
                n.Body = value;
                break;
            case SetAccessorDeclaration n:
                n.Body = (Block?)value;
                break;
            default:
                throw new InvalidOperationException($"{node.Kind} has no member Body");
        }
    }
}
