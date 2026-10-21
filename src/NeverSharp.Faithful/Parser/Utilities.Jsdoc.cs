// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/ast/utilities.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>utilities.ts (the units the parser reaches): JSDoc comment lookup and assignment declarations.</summary>
internal static partial class Utilities
{
    internal static List<CommentRange>? GetJSDocCommentRanges(Node node, string text)
    {
        var commentRanges = (node.Kind == SyntaxKind.Parameter ||
                node.Kind == SyntaxKind.TypeParameter ||
                node.Kind == SyntaxKind.FunctionExpression ||
                node.Kind == SyntaxKind.ArrowFunction ||
                node.Kind == SyntaxKind.ParenthesizedExpression ||
                node.Kind == SyntaxKind.VariableDeclaration ||
                node.Kind == SyntaxKind.ExportSpecifier) ?
            Core.Concatenate(Scanner.GetTrailingCommentRanges(text, node.Pos), Scanner.GetLeadingCommentRanges(text, node.Pos)) :
            Scanner.GetLeadingCommentRanges(text, node.Pos);
        // True if the comment starts with '/**' but not if it is '/**/'
        return Core.Filter(commentRanges, comment =>
            comment.End <= node.End && // Due to parse errors sometime empty parameter may get comments assigned to it that end up not in parameter range
            CharCodeAtOrNaN(text, comment.Pos + 1) == (int)CharacterCodes.asterisk &&
            CharCodeAtOrNaN(text, comment.Pos + 2) == (int)CharacterCodes.asterisk &&
            CharCodeAtOrNaN(text, comment.Pos + 3) != (int)CharacterCodes.slash);
    }

    internal static Node GetRightMostAssignedExpression(Node node)
    {
        while (IsAssignmentExpression(node, /*excludeCompoundAssignment*/ true))
        {
            node = ((BinaryExpression)node).Right;
        }
        return node;
    }

    internal static bool IsExportsIdentifier(Node node)
    {
        return NodeTests.IsIdentifier(node) && (((Identifier)node).EscapedText as string) == "exports";
    }

    internal static bool IsModuleIdentifier(Node node)
    {
        return NodeTests.IsIdentifier(node) && (((Identifier)node).EscapedText as string) == "module";
    }

    internal static bool IsModuleExportsAccessExpression(Node node)
    {
        return (NodeTests.IsPropertyAccessExpression(node) || IsLiteralLikeElementAccess(node))
            && IsModuleIdentifier(GetExpressionOfAccessExpression(node))
            && GetElementOrPropertyAccessName(node) == "exports";
    }

    // Given a BinaryExpression, returns SpecialPropertyAssignmentKind for the various kinds of property
    // assignments we treat as special in the binder
    internal static AssignmentDeclarationKind GetAssignmentDeclarationKind(Node expr)
    {
        var special = GetAssignmentDeclarationKindWorker(expr);
        return special == AssignmentDeclarationKind.Property || IsInJSFile(expr) ? special : AssignmentDeclarationKind.None;
    }

    internal static bool IsBindableObjectDefinePropertyCall(Node expr)
    {
        var call = (CallExpression)expr;
        return Core.Length(call.Arguments) == 3 &&
            NodeTests.IsPropertyAccessExpression(call.Expression) &&
            NodeTests.IsIdentifier(((PropertyAccessExpression)call.Expression).Expression) &&
            UtilitiesPublic.IdText(((PropertyAccessExpression)call.Expression).Expression) == "Object" &&
            UtilitiesPublic.IdText(((PropertyAccessExpression)call.Expression).Name) == "defineProperty" &&
            IsStringOrNumericLiteralLike(call.Arguments[1]) &&
            IsBindableStaticNameExpression(call.Arguments[0], /*excludeThisKeyword*/ true);
    }

    /// <summary>x[0] OR x['a'] OR x[Symbol.y]</summary>
    internal static bool IsLiteralLikeElementAccess(Node node)
    {
        return NodeTests.IsElementAccessExpression(node) && IsStringOrNumericLiteralLike(((ElementAccessExpression)node).ArgumentExpression);
    }

    /// <summary>Any series of property and element accesses.</summary>
    internal static bool IsBindableStaticAccessExpression(Node node, bool excludeThisKeyword = false)
    {
        return (NodeTests.IsPropertyAccessExpression(node) &&
                ((!excludeThisKeyword && ((PropertyAccessExpression)node).Expression.Kind == SyntaxKind.ThisKeyword) ||
                    (NodeTests.IsIdentifier(((PropertyAccessExpression)node).Name) && IsBindableStaticNameExpression(((PropertyAccessExpression)node).Expression, /*excludeThisKeyword*/ true))))
            || IsBindableStaticElementAccessExpression(node, excludeThisKeyword);
    }

    /// <summary>Any series of property and element accesses, ending in a literal element access</summary>
    internal static bool IsBindableStaticElementAccessExpression(Node node, bool excludeThisKeyword = false)
    {
        return IsLiteralLikeElementAccess(node)
            && ((!excludeThisKeyword && ((ElementAccessExpression)node).Expression.Kind == SyntaxKind.ThisKeyword) ||
                IsEntityNameExpression(((ElementAccessExpression)node).Expression) ||
                IsBindableStaticAccessExpression(((ElementAccessExpression)node).Expression, /*excludeThisKeyword*/ true));
    }

    internal static bool IsBindableStaticNameExpression(Node node, bool excludeThisKeyword = false)
    {
        return IsEntityNameExpression(node) || IsBindableStaticAccessExpression(node, excludeThisKeyword);
    }

    private static AssignmentDeclarationKind GetAssignmentDeclarationKindWorker(Node expr)
    {
        if (NodeTests.IsCallExpression(expr))
        {
            if (!IsBindableObjectDefinePropertyCall(expr))
            {
                return AssignmentDeclarationKind.None;
            }
            var entityName = ((CallExpression)expr).Arguments[0];
            if (IsExportsIdentifier(entityName) || IsModuleExportsAccessExpression(entityName))
            {
                return AssignmentDeclarationKind.ObjectDefinePropertyExports;
            }
            if (IsBindableStaticAccessExpression(entityName) && GetElementOrPropertyAccessName(entityName) == "prototype")
            {
                return AssignmentDeclarationKind.ObjectDefinePrototypeProperty;
            }
            return AssignmentDeclarationKind.ObjectDefinePropertyValue;
        }
        var binaryExpression = (BinaryExpression)expr;
        if (binaryExpression.OperatorToken.Kind != SyntaxKind.EqualsToken || !IsAccessExpression(binaryExpression.Left) || IsVoidZero(GetRightMostAssignedExpression(binaryExpression)))
        {
            return AssignmentDeclarationKind.None;
        }
        if (IsBindableStaticNameExpression(GetExpressionOfAccessExpression(binaryExpression.Left), /*excludeThisKeyword*/ true) && GetElementOrPropertyAccessName(binaryExpression.Left) == "prototype" && NodeTests.IsObjectLiteralExpression(GetInitializerOfBinaryExpression(binaryExpression)))
        {
            // F.prototype = { ... }
            return AssignmentDeclarationKind.Prototype;
        }
        return GetAssignmentDeclarationPropertyAccessKind(binaryExpression.Left);
    }

    private static bool IsVoidZero(Node node)
    {
        return NodeTests.IsVoidExpression(node) && NodeTests.IsNumericLiteral(((VoidExpression)node).Expression) && ((NumericLiteral)((VoidExpression)node).Expression).Text == "0";
    }

    /// <summary>
    /// Does not handle signed numeric names like `a[+0]` - handling those would require handling prefix unary expressions
    /// throughout late binding handling as well, which is awkward (but ultimately probably doable if there is demand)
    /// </summary>
    internal static Node? GetElementOrPropertyAccessArgumentExpressionOrName(Node node)
    {
        if (NodeTests.IsPropertyAccessExpression(node))
        {
            return ((PropertyAccessExpression)node).Name;
        }
        var arg = SkipParentheses(((ElementAccessExpression)node).ArgumentExpression);
        if (NodeTests.IsNumericLiteral(arg) || UtilitiesPublic.IsStringLiteralLike(arg))
        {
            return arg;
        }
        return node;
    }

    internal static string? GetElementOrPropertyAccessName(Node node)
    {
        var name = GetElementOrPropertyAccessArgumentExpressionOrName(node);
        if (name != null)
        {
            if (NodeTests.IsIdentifier(name))
            {
                return ((Identifier)name).EscapedText as string;
            }
            if (UtilitiesPublic.IsStringLiteralLike(name) || NodeTests.IsNumericLiteral(name))
            {
                return UtilitiesPublic.EscapeLeadingUnderscores(GetLiteralText(name));
            }
        }
        return null;
    }

    internal static AssignmentDeclarationKind GetAssignmentDeclarationPropertyAccessKind(Node lhs)
    {
        if (GetExpressionOfAccessExpression(lhs).Kind == SyntaxKind.ThisKeyword)
        {
            return AssignmentDeclarationKind.ThisProperty;
        }
        else if (IsModuleExportsAccessExpression(lhs))
        {
            // module.exports = expr
            return AssignmentDeclarationKind.ModuleExports;
        }
        else if (IsBindableStaticNameExpression(GetExpressionOfAccessExpression(lhs), /*excludeThisKeyword*/ true))
        {
            if (IsPrototypeAccess(GetExpressionOfAccessExpression(lhs)))
            {
                // F.G....prototype.x = expr
                return AssignmentDeclarationKind.PrototypeProperty;
            }

            var nextToLast = lhs;
            while (!NodeTests.IsIdentifier(GetExpressionOfAccessExpression(nextToLast)))
            {
                nextToLast = GetExpressionOfAccessExpression(nextToLast);
            }
            var id = (Identifier)GetExpressionOfAccessExpression(nextToLast);
            var idEscapedText = id.EscapedText as string;
            if (
                (idEscapedText == "exports" ||
                    (idEscapedText == "module" && GetElementOrPropertyAccessName(nextToLast) == "exports")) &&
                // ExportsProperty does not support binding with computed names
                IsBindableStaticAccessExpression(lhs)
            )
            {
                // exports.name = expr OR module.exports.name = expr OR exports["name"] = expr ...
                return AssignmentDeclarationKind.ExportsProperty;
            }
            if (IsBindableStaticNameExpression(lhs, /*excludeThisKeyword*/ true) || (NodeTests.IsElementAccessExpression(lhs) && IsDynamicName(lhs)))
            {
                // F.G...x = expr
                return AssignmentDeclarationKind.Property;
            }
        }

        return AssignmentDeclarationKind.None;
    }

    internal static Node GetInitializerOfBinaryExpression(Node expr)
    {
        while (NodeTests.IsBinaryExpression(((BinaryExpression)expr).Right))
        {
            expr = ((BinaryExpression)expr).Right;
        }
        return ((BinaryExpression)expr).Right;
    }

    internal static bool IsEntityNameExpression(Node node)
    {
        return node.Kind == SyntaxKind.Identifier || IsPropertyAccessEntityNameExpression(node);
    }

    internal static bool IsPropertyAccessEntityNameExpression(Node node)
    {
        return NodeTests.IsPropertyAccessExpression(node) && NodeTests.IsIdentifier(((PropertyAccessExpression)node).Name) && IsEntityNameExpression(((PropertyAccessExpression)node).Expression);
    }

    internal static bool IsPrototypeAccess(Node node)
    {
        return IsBindableStaticAccessExpression(node) && GetElementOrPropertyAccessName(node) == "prototype";
    }

    private static Node? GetSourceOfDefaultedAssignment(Node node)
    {
        if (NodeTests.IsExpressionStatement(node))
        {
            var expression = ((ExpressionStatement)node).Expression;
            if (NodeTests.IsBinaryExpression(expression) &&
                GetAssignmentDeclarationKind(expression) != AssignmentDeclarationKind.None &&
                NodeTests.IsBinaryExpression(((BinaryExpression)expression).Right))
            {
                var right = (BinaryExpression)((BinaryExpression)expression).Right;
                if (right.OperatorToken.Kind == SyntaxKind.BarBarToken || right.OperatorToken.Kind == SyntaxKind.QuestionQuestionToken)
                {
                    return right.Right;
                }
            }
        }
        return null;
    }

    private static Node? GetSingleInitializerOfVariableStatementOrPropertyDeclaration(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.VariableStatement:
            {
                var v = GetSingleVariableOfVariableStatement(node);
                return v?.Initializer;
            }
            case SyntaxKind.PropertyDeclaration:
                return ((PropertyDeclaration)node).Initializer;
            case SyntaxKind.PropertyAssignment:
                return ((PropertyAssignment)node).Initializer;
        }
        return null;
    }

    internal static VariableDeclaration? GetSingleVariableOfVariableStatement(Node node)
    {
        return NodeTests.IsVariableStatement(node) ? (VariableDeclaration?)Core.FirstOrUndefined(((VariableStatement)node).DeclarationList.Declarations) : null;
    }

    private static Node? GetNestedModuleDeclaration(Node node)
    {
        if (NodeTests.IsModuleDeclaration(node))
        {
            var body = ((ModuleDeclaration)node).Body;
            if (body != null && body.Kind == SyntaxKind.ModuleDeclaration)
            {
                return body;
            }
        }
        return null;
    }

    internal static bool CanHaveJSDoc(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.BinaryExpression:
            case SyntaxKind.Block:
            case SyntaxKind.BreakStatement:
            case SyntaxKind.CallSignature:
            case SyntaxKind.CaseClause:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.ClassStaticBlockDeclaration:
            case SyntaxKind.Constructor:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.ContinueStatement:
            case SyntaxKind.DebuggerStatement:
            case SyntaxKind.DoStatement:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.EmptyStatement:
            case SyntaxKind.EndOfFileToken:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.EnumMember:
            case SyntaxKind.ExportAssignment:
            case SyntaxKind.ExportDeclaration:
            case SyntaxKind.ExportSpecifier:
            case SyntaxKind.ExpressionStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
            case SyntaxKind.ForStatement:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionType:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.Identifier:
            case SyntaxKind.IfStatement:
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.JSDocFunctionType:
            case SyntaxKind.JSDocSignature:
            case SyntaxKind.LabeledStatement:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.NamedTupleMember:
            case SyntaxKind.NamespaceExportDeclaration:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.Parameter:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.ReturnStatement:
            case SyntaxKind.SemicolonClassElement:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.ShorthandPropertyAssignment:
            case SyntaxKind.SpreadAssignment:
            case SyntaxKind.SwitchStatement:
            case SyntaxKind.ThrowStatement:
            case SyntaxKind.TryStatement:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.TypeParameter:
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.VariableStatement:
            case SyntaxKind.WhileStatement:
            case SyntaxKind.WithStatement:
                return true;
            default:
                return false;
        }
    }

    /// <summary>`node.initializer` of the variable-like kinds (TypeScript's VariableLikeDeclaration with an initializer).</summary>
    private static Node? GetInitializerOfVariableLike(Node node)
    {
        switch (node)
        {
            case BindingElement bindingElement:
                return bindingElement.Initializer;
            case EnumMember enumMember:
                return enumMember.Initializer;
            case ParameterDeclaration parameter:
                return parameter.Initializer;
            case PropertyAssignment propertyAssignment:
                return propertyAssignment.Initializer;
            case PropertyDeclaration propertyDeclaration:
                return propertyDeclaration.Initializer;
            case PropertySignature propertySignature:
                return propertySignature.Initializer;
            case VariableDeclaration variableDeclaration:
                return variableDeclaration.Initializer;
            default:
                return null;
        }
    }

    /// <summary>
    /// This function checks multiple locations for JSDoc comments that apply to a host node.
    /// At each location, the whole comment may apply to the node, or only a specific tag in
    /// the comment. In the first case, location adds the entire JSDoc object. In the
    /// second case, it adds the applicable JSDocTag.
    ///
    /// For example, a JSDoc comment before a parameter adds the entire JSDoc. But a
    /// `@param` tag on the parent function only adds the JSDocTag for the `@param`.
    /// </summary>
    internal static IReadOnlyList<Node> GetJSDocCommentsAndTags(Node hostNode, bool noCache = false)
    {
        List<Node>? result = null;
        // Pull parameter comments from declaring function as well
        var initializer = IsVariableLike(hostNode) && UtilitiesPublic.HasInitializer(hostNode) ? GetInitializerOfVariableLike(hostNode) : null;
        if (initializer != null && UtilitiesPublic.HasJSDocNodes(initializer))
        {
            result = Core.AddRange(result, FilterOwnedJSDocTags(hostNode, JSDocContainers.GetJsDoc(initializer)!));
        }

        Node? node = hostNode;
        while (node != null && node.Parent != null)
        {
            if (UtilitiesPublic.HasJSDocNodes(node))
            {
                result = Core.AddRange(result, FilterOwnedJSDocTags(hostNode, JSDocContainers.GetJsDoc(node)!));
            }

            if (node.Kind == SyntaxKind.Parameter)
            {
                result = Core.AddRange<Node>(result, noCache ? UtilitiesPublic.GetJSDocParameterTagsNoCache((ParameterDeclaration)node) : UtilitiesPublic.GetJSDocParameterTags((ParameterDeclaration)node));
                break;
            }
            if (node.Kind == SyntaxKind.TypeParameter)
            {
                result = Core.AddRange<Node>(result, noCache ? UtilitiesPublic.GetJSDocTypeParameterTagsNoCache((TypeParameterDeclaration)node) : UtilitiesPublic.GetJSDocTypeParameterTags((TypeParameterDeclaration)node));
                break;
            }
            node = GetNextJSDocCommentLocation(node);
        }
        return result ?? (IReadOnlyList<Node>)Core.EmptyArray<Node>();
    }

    private static IReadOnlyList<Node> FilterOwnedJSDocTags(Node hostNode, List<JSDoc> comments)
    {
        var lastJsDoc = Core.Last(comments);
        return Core.FlatMap<JSDoc, Node>(comments, (jsDoc, _) =>
        {
            if (ReferenceEquals(jsDoc, lastJsDoc))
            {
                var ownedTags = Core.Filter(jsDoc.Tags, tag => OwnsJSDocTag(hostNode, tag));
                return ReferenceEquals(jsDoc.Tags, ownedTags) ? new List<Node> { jsDoc } : ownedTags;
            }
            else
            {
                return Core.Filter(jsDoc.Tags, NodeTests.IsJSDocOverloadTag);
            }
        });
    }

    /// <summary>
    /// Determines whether a host node owns a jsDoc tag. A `@type`/`@satisfies` tag attached to a
    /// a ParenthesizedExpression belongs only to the ParenthesizedExpression.
    /// </summary>
    private static bool OwnsJSDocTag(Node hostNode, Node tag)
    {
        return !(NodeTests.IsJSDocTypeTag(tag) || NodeTests.IsJSDocSatisfiesTag(tag))
            || tag.Parent == null
            || !NodeTests.IsJSDoc(tag.Parent)
            || !NodeTests.IsParenthesizedExpression(tag.Parent.Parent)
            || tag.Parent.Parent == hostNode;
    }

    internal static Node? GetNextJSDocCommentLocation(Node node)
    {
        var parent = node.Parent;
        if (
            parent.Kind == SyntaxKind.PropertyAssignment ||
            parent.Kind == SyntaxKind.ExportAssignment ||
            parent.Kind == SyntaxKind.PropertyDeclaration ||
            (parent.Kind == SyntaxKind.ExpressionStatement && node.Kind == SyntaxKind.PropertyAccessExpression) ||
            parent.Kind == SyntaxKind.ReturnStatement ||
            GetNestedModuleDeclaration(parent) != null ||
            IsAssignmentExpression(node)
        )
        {
            return parent;
        }
        // Try to recognize this pattern when node is initializer of variable declaration and JSDoc comments are on containing variable statement.
        // /**
        //   * @param {number} name
        //   * @returns {number}
        //   */
        // var x = function(name) { return name.length; }
        else if (
            parent.Parent != null &&
            (GetSingleVariableOfVariableStatement(parent.Parent) == node || IsAssignmentExpression(parent))
        )
        {
            return parent.Parent;
        }
        else if (
            parent.Parent != null && parent.Parent.Parent != null &&
            (GetSingleVariableOfVariableStatement(parent.Parent.Parent) != null ||
                GetSingleInitializerOfVariableStatementOrPropertyDeclaration(parent.Parent.Parent) == node ||
                GetSourceOfDefaultedAssignment(parent.Parent.Parent) != null)
        )
        {
            return parent.Parent.Parent;
        }
        return null;
    }
}
