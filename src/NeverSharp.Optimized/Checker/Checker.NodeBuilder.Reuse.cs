// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/expressionToTypeNode.ts (createSyntacticTypeNodeBuilder) and src/compiler/checker.ts (the syntacticBuilderResolver, createRecoveryBoundary, trackExistingEntityName, serializeTypeName, canReuseTypeNode, serializeExistingTypeNode)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/nodebuilderimpl.go and internal/pseudochecker
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C2. An existing node the builder reuses is copied into the
// builder's store (VisitEachChildCopy: the node kinds of type annotations), and the copy is marked reused. Literals
// copied from the enclosing file keep their source text where TypeScript's printer would print it (numeric literals);
// the printer otherwise writes the copies from their values. typeFromArrayLiteral and typeFromObjectLiteral build
// nodes TypeScript discards (they return notImplemented); only their decisions are ported. Symbol trackers and the
// inference fallback reports are left out (typeToString passes none).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    /// <summary>expressionToTypeNode.ts SyntacticResult: a type node, or none with whether a fallback is reported.</summary>
    private readonly record struct SyntacticResult(Node Type, bool ReportFallback)
    {
        public static readonly SyntacticResult NotImplemented = new(Node.None, false);
        public static readonly SyntacticResult AlreadyReported = new(Node.None, false);
        public static readonly SyntacticResult Failed = new(Node.None, true);
    }

    private static SyntacticResult Result(Node type) => new(type, true);

    // Set when an existing node of a kind the copier does not know is met; the reuse is given up.
    private bool reuseUnsupported;

    // ---- recovery boundary (checker.ts createRecoveryBoundary) ----

    private static void MarkError(NodeBuilderContext context)
    {
        if (context.BoundaryHadError is not null)
        {
            context.BoundaryHadError = true;
        }
    }

    private static bool HadError(NodeBuilderContext context) => context.BoundaryHadError == true;

    // ---- tryReuseExistingTypeNode ----

    // syntacticNodeBuilder.tryReuseExistingTypeNode (with the resolver's canReuseTypeNode check)
    private Node TryReuseExistingTypeNodeChecked(NodeBuilderContext context, NodeRef existing)
    {
        if (!CanReuseTypeNode(context, existing))
        {
            return Node.None;
        }
        return TryReuseExistingTypeNode(context, existing);
    }

    // expressionToTypeNode.ts tryReuseExistingTypeNode
    private Node TryReuseExistingTypeNode(NodeBuilderContext context, NodeRef existing)
    {
        var oldBoundary = context.BoundaryHadError;
        var oldEncounteredError = context.EncounteredError;
        var oldLength = context.ApproximateLength;
        var oldUnsupported = reuseUnsupported;
        context.BoundaryHadError = false;
        reuseUnsupported = false;
        var transformed = VisitExistingNodeTreeSymbols(context, existing);
        var hadError = context.BoundaryHadError == true || reuseUnsupported;
        // finalizeBoundary
        context.BoundaryHadError = oldBoundary;
        context.EncounteredError = oldEncounteredError;
        reuseUnsupported = oldUnsupported;
        if (hadError)
        {
            context.ApproximateLength = oldLength;
            return Node.None;
        }
        var store = StoreOf(existing);
        context.ApproximateLength += store.End(existing.Node) - store.Pos(existing.Node);
        return transformed;
    }

    // utilities.ts isNewScopeNode
    private static bool IsNewScopeNode(NodeStore store, Node node) =>
        Utilities.IsFunctionLike(store, node) || store.Kind(node) is SyntaxKind.JSDocSignature or SyntaxKind.MappedType;

    // expressionToTypeNode.ts visitExistingNodeTreeSymbols
    private Node VisitExistingNodeTreeSymbols(NodeBuilderContext context, NodeRef node)
    {
        if (node.IsNone)
        {
            return Node.None;
        }
        // If there was an error in a sibling node bail early, the result will be discarded anyway
        if (HadError(context))
        {
            return Node.None;
        }
        var store = StoreOf(node);
        Action? onExitNewScope = IsNewScopeNode(store, node.Node) ? EnterNewScopeForNode(context, node) : null;
        var result = VisitExistingNodeTreeSymbolsWorker(context, node);
        onExitNewScope?.Invoke();

        // If there was an error, maybe we can recover by serializing the actual type of the node
        if (HadError(context))
        {
            if (IsTypeNodeKind(store.Kind(node.Node)) && store.Kind(node.Node) != SyntaxKind.TypePredicate)
            {
                // recover(): the error is reset and the actual type of the node is serialized
                context.BoundaryHadError = false;
                return SerializeExistingTypeNode(context, node, addUndefined: false);
            }
            return Node.None;
        }
        return !result.IsNone ? SetTextRange(context, result, node) : Node.None;
    }

    private Node TryVisitSimpleTypeNode(NodeBuilderContext context, NodeRef node)
    {
        var innerNode = SkipTypeParentheses(node);
        switch (Kind(innerNode))
        {
            case SyntaxKind.TypeReference:
                return TryVisitTypeReference(context, innerNode);
            case SyntaxKind.TypeQuery:
                return TryVisitTypeQuery(context, innerNode);
            case SyntaxKind.IndexedAccessType:
                return TryVisitIndexedAccess(context, innerNode);
            case SyntaxKind.TypeOperator:
                if (StoreOf(innerNode).Operator(innerNode.Node) == SyntaxKind.KeyOfKeyword)
                {
                    return TryVisitKeyOf(context, innerNode);
                }
                break;
        }
        return VisitExistingNodeTreeSymbols(context, node);
    }

    private Node TryVisitIndexedAccess(NodeBuilderContext context, NodeRef node)
    {
        var store = StoreOf(node);
        var resultObjectType = TryVisitSimpleTypeNode(context, At(node, store.ObjectType(node.Node)));
        if (resultObjectType.IsNone)
        {
            return Node.None;
        }
        var updated = SIndexedAccessType(resultObjectType, VisitExistingNodeTreeSymbols(context, At(node, store.IndexType(node.Node))));
        return SetTextRange(context, updated, node);
    }

    private Node TryVisitKeyOf(NodeBuilderContext context, NodeRef node)
    {
        var store = StoreOf(node);
        var type = TryVisitSimpleTypeNode(context, At(node, store.Type(node.Node)));
        if (type.IsNone)
        {
            return Node.None;
        }
        return SetTextRange(context, STypeOperator(SyntaxKind.KeyOfKeyword, type), node);
    }

    private Node TryVisitTypeQuery(NodeBuilderContext context, NodeRef node)
    {
        var store = StoreOf(node);
        var exprNameNode = At(node, store.ExprName(node.Node));
        var (introducesError, exprName) = TrackExistingEntityName(context, exprNameNode);
        if (!introducesError)
        {
            return SetTextRange(context, STypeQuery(exprName, VisitExistingNodes(context, node, store.TypeArguments(node.Node))), node);
        }
        var serializedName = SerializeTypeName(context, exprNameNode, isTypeOf: true, null);
        if (!serializedName.IsNone)
        {
            return SetTextRange(context, serializedName, exprNameNode);
        }
        return Node.None;
    }

    private Node TryVisitTypeReference(NodeBuilderContext context, NodeRef node)
    {
        if (CanReuseTypeNode(context, node))
        {
            var store = StoreOf(node);
            var typeNameNode = At(node, store.TypeName(node.Node));
            var (introducesError, newName) = TrackExistingEntityName(context, typeNameNode);
            var typeArguments = VisitExistingNodes(context, node, store.TypeArguments(node.Node));
            if (!introducesError)
            {
                return SetTextRange(context, STypeReference(newName, typeArguments), node);
            }
            var typeArgumentList = typeArguments.IsNone ? null : new List<Node>(S.Nodes(typeArguments).ToArray());
            var serializedName = SerializeTypeName(context, typeNameNode, isTypeOf: false, typeArgumentList);
            if (!serializedName.IsNone)
            {
                return SetTextRange(context, serializedName, typeNameNode);
            }
        }
        return Node.None;
    }

    private NodeList VisitExistingNodes(NodeBuilderContext context, NodeRef owner, NodeList list)
    {
        if (list.IsNone)
        {
            return NodeList.None;
        }
        var store = StoreOf(owner);
        var result = new List<Node>();
        foreach (var element in store.Nodes(list))
        {
            var visited = VisitExistingNodeTreeSymbols(context, At(owner, element));
            if (!visited.IsNone)
            {
                result.Add(visited);
            }
        }
        // visitNodes keeps the list's hasTrailingComma
        return S.NewList(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(result), store.HasTrailingComma(list));
    }

    // expressionToTypeNode.ts: a parameter of a reused JSDoc function type (getEffectiveDotDotDotForParameter,
    // getNameForJSDocFunctionParameter)
    private Node JSDocFunctionParameter(NodeBuilderContext context, NodeRef owner, Node p, int index)
    {
        var store = StoreOf(owner);
        var type = store.Type(p);
        var dotDotDot = !store.DotDotDotToken(p).IsNone || (!type.IsNone && store.Kind(type) == SyntaxKind.JSDocVariadicType);
        var pName = store.Name(p);
        var name = !pName.IsNone && store.Kind(pName) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(pName), "this") ? "this"
            : dotDotDot ? "args"
            : "arg" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return SParameterDeclaration(NodeList.None, dotDotDot ? SToken(SyntaxKind.DotDotDotToken) : Node.None,
            SetTextRange(context, SIdentifier(name), At(owner, p)),
            store.QuestionToken(p).IsNone ? Node.None : SToken(SyntaxKind.QuestionToken),
            VisitExistingNodeTreeSymbols(context, At(owner, type)));
    }

    // checker.ts getJsDocPropertyOverride
    private Node GetJsDocPropertyOverride(NodeBuilderContext context, NodeRef jsDocTypeLiteral, NodeRef jsDocProperty, NodeRef name)
    {
        var store = StoreOf(jsDocProperty);
        var typeViaParent = GetTypeOfPropertyOfType(GetTypeFromTypeNodeInContext(context, jsDocTypeLiteral), store.Texts.GetString(store.EscapedText(name.Node))!);
        var typeExpression = store.TypeExpression(jsDocProperty.Node);
        return !typeViaParent.IsNone && !typeExpression.IsNone && GetTypeFromTypeNodeInContext(context, At(jsDocProperty, store.Type(typeExpression))) != typeViaParent
            ? TypeToTypeNodeHelper(typeViaParent, context) : Node.None;
    }

    // expressionToTypeNode.ts visitExistingNodeTreeSymbolsWorker
    private Node VisitExistingNodeTreeSymbolsWorker(NodeBuilderContext context, NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var kind = store.Kind(n);
        switch (kind)
        {
            case SyntaxKind.JSDocTypeExpression:
                // Unwrap JSDocTypeExpressions
                return VisitExistingNodeTreeSymbols(context, At(node, store.Type(n)));
            case SyntaxKind.JSDocAllType:
            case SyntaxKind.JSDocNamepathType:
                // We don't _actually_ support jsdoc namepath types, emit `any` instead
                return SKeyword(SyntaxKind.AnyKeyword);
            case SyntaxKind.JSDocUnknownType:
                return SKeyword(SyntaxKind.UnknownKeyword);
            case SyntaxKind.JSDocNullableType:
                return SUnionType([VisitExistingNodeTreeSymbols(context, At(node, store.Type(n))), SLiteralType(SToken(SyntaxKind.NullKeyword))]);
            case SyntaxKind.JSDocOptionalType:
                return SUnionType([VisitExistingNodeTreeSymbols(context, At(node, store.Type(n))), SKeyword(SyntaxKind.UndefinedKeyword)]);
            case SyntaxKind.JSDocNonNullableType:
                return VisitExistingNodeTreeSymbols(context, At(node, store.Type(n)));
            case SyntaxKind.JSDocVariadicType:
                return SArrayType(VisitExistingNodeTreeSymbols(context, At(node, store.Type(n))));
            case SyntaxKind.JSDocTypeLiteral:
            {
                var members = new List<Node>();
                foreach (var t in store.JsDocPropertyTags(n) ?? [])
                {
                    var tagName = store.Name(t);
                    var nameNode = store.Kind(tagName) == SyntaxKind.Identifier ? tagName : store.Right(tagName);
                    var name = VisitExistingNodeTreeSymbols(context, At(node, nameNode));
                    var overrideTypeNode = GetJsDocPropertyOverride(context, node, At(node, t), At(node, nameNode));
                    var typeExpression = store.TypeExpression(t);
                    var optional = store.IsBracketed(t) || (!typeExpression.IsNone && store.Kind(store.Type(typeExpression)) == SyntaxKind.JSDocOptionalType);
                    var memberType = !overrideTypeNode.IsNone ? overrideTypeNode
                        : !typeExpression.IsNone ? VisitExistingNodeTreeSymbols(context, At(node, store.Type(typeExpression))) : Node.None;
                    members.Add(SPropertySignature(NodeList.None, name, optional ? SToken(SyntaxKind.QuestionToken) : Node.None,
                        !memberType.IsNone ? memberType : SKeyword(SyntaxKind.AnyKeyword)));
                }
                return STypeLiteral(members);
            }
            case SyntaxKind.JSDocFunctionType:
            {
                var parameters = store.Nodes(store.Parameters(n));
                var list = new List<Node>();
                if (Utilities.IsJSDocConstructSignature(store, n))
                {
                    var newTypeNode = Node.None;
                    for (var i = 0; i < parameters.Length; i++)
                    {
                        var p = parameters[i];
                        var pName = store.Name(p);
                        if (!pName.IsNone && store.Kind(pName) == SyntaxKind.Identifier && store.Texts.Equals(store.EscapedText(pName), "new"))
                        {
                            newTypeNode = store.Type(p);
                            continue;
                        }
                        list.Add(JSDocFunctionParameter(context, node, p, i));
                    }
                    var constructorReturn = VisitExistingNodeTreeSymbols(context, At(node, !newTypeNode.IsNone ? newTypeNode : store.Type(n)));
                    return SSignature(SyntaxKind.ConstructorType, NodeList.None, Node.None, Node.None, VisitExistingNodes(context, node, store.TypeParameters(n)), SList(list),
                        !constructorReturn.IsNone ? constructorReturn : SKeyword(SyntaxKind.AnyKeyword));
                }
                for (var i = 0; i < parameters.Length; i++)
                {
                    list.Add(JSDocFunctionParameter(context, node, parameters[i], i));
                }
                var returnTypeNode = VisitExistingNodeTreeSymbols(context, At(node, store.Type(n)));
                return SSignature(SyntaxKind.FunctionType, NodeList.None, Node.None, Node.None, VisitExistingNodes(context, node, store.TypeParameters(n)), SList(list),
                    !returnTypeNode.IsNone ? returnTypeNode : SKeyword(SyntaxKind.AnyKeyword));
            }
        }
        if (kind == SyntaxKind.TypeReference && store.Kind(store.TypeName(n)) == SyntaxKind.Identifier && string.IsNullOrEmpty(store.Texts.GetString(store.EscapedText(store.TypeName(n)))))
        {
            return SetTextRange(context, SKeyword(SyntaxKind.AnyKeyword), node);
        }
        if (kind is SyntaxKind.ExpressionWithTypeArguments or SyntaxKind.TypeReference && IsJSDocIndexSignature(node))
        {
            reuseUnsupported = true;
            return Node.None;
        }
        if (kind == SyntaxKind.ThisType)
        {
            if (CanReuseTypeNode(context, node))
            {
                return CopyShell(node);
            }
            MarkError(context);
            return CopyShell(node);
        }
        if (kind == SyntaxKind.TypeParameter)
        {
            var (_, newName) = TrackExistingEntityName(context, At(node, store.Name(n)));
            return STypeParameterDeclaration(
                VisitExistingNodes(context, node, store.Modifiers(n)),
                newName,
                VisitExistingNodeTreeSymbols(context, At(node, store.Constraint(n))),
                VisitExistingNodeTreeSymbols(context, At(node, store.Default(n))));
        }
        if (kind == SyntaxKind.IndexedAccessType)
        {
            var result = TryVisitIndexedAccess(context, node);
            if (result.IsNone)
            {
                MarkError(context);
                return CopyShell(node);
            }
            return result;
        }
        if (kind == SyntaxKind.TypeReference)
        {
            var result = TryVisitTypeReference(context, node);
            if (!result.IsNone)
            {
                return result;
            }
            MarkError(context);
            return CopyShell(node);
        }
        if (kind == SyntaxKind.ImportType && store.Kind(store.Argument(n)) == SyntaxKind.LiteralType && store.Kind(store.Literal(store.Argument(n))) == SyntaxKind.StringLiteral)
        {
            // assert keyword in imported attributes is deprecated, so we don't reuse types that contain it
            var attributes = store.Attributes(n);
            if (!attributes.IsNone && store.Token(attributes) == SyntaxKind.AssertKeyword)
            {
                MarkError(context);
                return CopyShell(node);
            }
            if (!CanReuseTypeNode(context, node))
            {
                return SerializeExistingTypeNode(context, node, addUndefined: false);
            }
            return VisitEachChildCopy(context, node);
        }
        if (UtilitiesPublic.IsNamedDeclaration(store, n) && store.Kind(store.Name(n)) == SyntaxKind.ComputedPropertyName && !HasLateBindableName(node))
        {
            if (!Utilities.HasDynamicName(store, n))
            {
                return VisitEachChildCopy(context, node);
            }
            if (ShouldRemoveDeclaration(context, node))
            {
                return Node.None;
            }
        }
        if ((Utilities.IsFunctionLike(store, n) && store.Type(n).IsNone)
            || (kind == SyntaxKind.PropertyDeclaration && store.Type(n).IsNone && store.Initializer(n).IsNone)
            || (kind == SyntaxKind.PropertySignature && store.Type(n).IsNone && store.Initializer(n).IsNone)
            || (kind == SyntaxKind.Parameter && store.Type(n).IsNone && store.Initializer(n).IsNone))
        {
            var visited = VisitEachChildCopy(context, node);
            S.SetType(visited, SKeyword(SyntaxKind.AnyKeyword));
            if (kind == SyntaxKind.Parameter)
            {
                S.SetModifiers(visited, NodeList.None);
            }
            return visited;
        }
        if (kind == SyntaxKind.TypeQuery)
        {
            var result = TryVisitTypeQuery(context, node);
            if (result.IsNone)
            {
                MarkError(context);
                return CopyShell(node);
            }
            return result;
        }
        if (kind == SyntaxKind.ComputedPropertyName && Utilities.IsEntityNameExpression(store, store.Expression(n)))
        {
            var (introducesError, result) = TrackExistingEntityName(context, At(node, store.Expression(n)));
            if (!introducesError)
            {
                return SComputedPropertyName(result);
            }
            // (computed names whose entity is not accessible: the evaluated literal; C8)
            reuseUnsupported = true;
            return Node.None;
        }
        if (kind == SyntaxKind.TypePredicate)
        {
            Node parameterName;
            var parameterNameNode = At(node, store.ParameterName(n));
            if (Kind(parameterNameNode) == SyntaxKind.Identifier)
            {
                var (introducesError, result) = TrackExistingEntityName(context, parameterNameNode);
                // Should not usually happen the only case is when a type predicate comes from a JSDoc type annotation with it's own parameter symbol definition.
                if (introducesError)
                {
                    MarkError(context);
                }
                parameterName = result;
            }
            else
            {
                // factory.cloneNode: the clone's original is the existing node (reused)
                parameterName = CopyExistingNode(parameterNameNode);
            }
            var predicate = SNew(SyntaxKind.TypePredicate);
            var asserts = store.AssertsModifier(n);
            S.SetAssertsModifier(predicate, asserts.IsNone ? Node.None : CopyExistingNode(At(node, asserts)));
            S.SetParameterName(predicate, parameterName);
            S.SetType(predicate, VisitExistingNodeTreeSymbols(context, At(node, store.Type(n))));
            return predicate;
        }
        if (kind is SyntaxKind.TupleType or SyntaxKind.TypeLiteral or SyntaxKind.MappedType)
        {
            var clone = SetTextRange(context, VisitEachChildCopy(context, node), node);
            var flags = GetSynthEmitFlags(clone);
            SetEmitFlags(clone, flags | ((context.Flags & NodeBuilderFlags.MultilineObjectLiterals) != 0 && kind == SyntaxKind.TypeLiteral ? 0 : EmitFlags.SingleLine));
            return clone;
        }
        if (kind == SyntaxKind.StringLiteral && (context.Flags & NodeBuilderFlags.UseSingleQuotesForStringLiteralType) != 0 && !store.SingleQuote(n))
        {
            var clone = CopyShell(node);
            S.SetSingleQuote(clone, true);
            return clone;
        }
        if (kind == SyntaxKind.ConditionalType)
        {
            var checkType = VisitExistingNodeTreeSymbols(context, At(node, store.CheckType(n)));
            var disposeScope = EnterNewScopeForNode(context, node);
            var extendType = VisitExistingNodeTreeSymbols(context, At(node, store.ExtendsType(n)));
            var trueType = VisitExistingNodeTreeSymbols(context, At(node, store.TrueType(n)));
            disposeScope();
            var falseType = VisitExistingNodeTreeSymbols(context, At(node, store.FalseType(n)));
            return SConditionalType(checkType, extendType, trueType, falseType);
        }
        if (kind == SyntaxKind.TypeOperator)
        {
            var @operator = store.Operator(n);
            if (@operator == SyntaxKind.UniqueKeyword && store.Kind(store.Type(n)) == SyntaxKind.SymbolKeyword)
            {
                if (!CanReuseTypeNode(context, node))
                {
                    MarkError(context);
                    return CopyShell(node);
                }
            }
            else if (@operator == SyntaxKind.KeyOfKeyword)
            {
                var result = TryVisitKeyOf(context, node);
                if (result.IsNone)
                {
                    MarkError(context);
                    return CopyShell(node);
                }
                return result;
            }
        }
        return VisitEachChildCopy(context, node);
    }

    // utilities.ts isJSDocIndexSignature: Object.<string, T> in JSDoc
    private bool IsJSDocIndexSignature(NodeRef node)
    {
        var store = StoreOf(node);
        var n = node.Node;
        var typeName = store.Kind(n) == SyntaxKind.TypeReference ? store.TypeName(n) : store.Expression(n);
        var typeArguments = store.TypeArguments(n);
        return store.Kind(typeName) == SyntaxKind.Identifier && (store.Texts.GetString(store.EscapedText(typeName)) ?? "") == "Object" &&
            !typeArguments.IsNone && store.Length(typeArguments) == 2 &&
            store.Kind(store.Element(typeArguments, 0)) is SyntaxKind.StringKeyword or SyntaxKind.NumberKeyword &&
            (store.Flags(n) & NodeFlags.JSDoc) != 0;
    }


    // the resolver's shouldRemoveDeclaration
    private bool ShouldRemoveDeclaration(NodeBuilderContext context, NodeRef node)
    {
        var store = StoreOf(node);
        var nameExpression = store.Expression(store.Name(node.Node));
        return !((context.InternalFlags & InternalNodeBuilderFlags.AllowUnresolvedNames) != 0 && Utilities.IsEntityNameExpression(store, nameExpression) &&
            (Types.Flags(CheckExpressionForNameType(At(node, nameExpression))) & TypeFlags.Any) != 0);
    }

    // the resolver's enterNewScope(context, node)
    private Action EnterNewScopeForNode(NodeBuilderContext context, NodeRef node)
    {
        var store = StoreOf(node);
        if (Utilities.IsFunctionLike(store, node.Node) || store.Kind(node.Node) == SyntaxKind.JSDocSignature)
        {
            var signature = GetSignatureFromDeclaration(node);
            ref var record = ref Signatures.Record(signature);
            return EnterNewScope(context, node, record.Parameters, Types.Elements(record.TypeParameters).ToArray());
        }
        var typeParameters = store.Kind(node.Node) == SyntaxKind.ConditionalType
            ? GetInferTypeParameters(node) ?? []
            :[GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(At(node, store.TypeParameter(node.Node))))];
        return EnterNewScope(context, node, null, typeParameters);
    }

    // ---- copies of existing nodes ----

    // A copy of the node without its children (its scalar fields), marked reused.
    private Node CopyShell(NodeRef node, bool markReused = true) => CopyShellFrom(StoreOf(node), node.Node, markReused);

    private Node CopyShellFrom(NodeStore store, Node n, bool markReused)
    {
        var kind = store.Kind(n);
        var copy = SNew(kind);
        switch (kind)
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.PrivateIdentifier:
                S.SetEscapedText(copy, S.Texts.Add(store.Texts.GetString(store.EscapedText(n)) ?? ""));
                break;
            case SyntaxKind.StringLiteral:
                S.SetText(copy, S.Texts.Add(store.Texts.GetString(store.Text(n)) ?? ""));
                S.SetSingleQuote(copy, store.SingleQuote(n));
                S.SetHasExtendedUnicodeEscape(copy, store.HasExtendedUnicodeEscape(n));
                break;
            case SyntaxKind.NumericLiteral:
            {
                // TypeScript's printer writes a reused literal of the enclosing file from the source text (setTextRange keeps
                // positions only within the enclosing file; a literal of another file prints its canonical text)
                var text = store.Texts.GetString(store.Text(n)) ?? "";
                if (store.Pos(n) >= 0 && !store.Parent(n).IsNone && ReferenceEquals(store, builderEnclosingStore))
                {
                    text = Utilities.GetSourceTextOfNodeFromSourceFile(store, Utilities.RootOf(store, n), n);
                }
                S.SetText(copy, S.Texts.Add(text));
                S.SetNumericLiteralFlags(copy, store.NumericLiteralFlags(n));
                break;
            }
            case SyntaxKind.BigIntLiteral:
                S.SetText(copy, S.Texts.Add(store.Texts.GetString(store.Text(n)) ?? ""));
                break;
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateHead:
            case SyntaxKind.TemplateMiddle:
            case SyntaxKind.TemplateTail:
                S.SetText(copy, S.Texts.Add(store.Texts.GetString(store.Text(n)) ?? ""));
                if (!store.RawText(n).IsNone)
                {
                    S.SetRawText(copy, S.Texts.Add(store.Texts.GetString(store.RawText(n)) ?? ""));
                }
                S.SetTemplateFlags(copy, store.TemplateFlags(n));
                break;
            case SyntaxKind.TypeOperator:
            case SyntaxKind.PrefixUnaryExpression:
                S.SetOperator(copy, store.Operator(n));
                break;
            case SyntaxKind.ImportType:
                S.SetIsTypeOf(copy, store.IsTypeOf(n));
                break;
            case SyntaxKind.ImportAttributes:
                S.SetToken(copy, store.Token(n));
                S.SetMultiLine(copy, store.MultiLine(n));
                break;
        }
        if (markReused)
        {
            MarkReused(copy);
        }
        return copy;
    }

    // visitEachChild(node, visitor) for the node kinds of type annotations, into the builder's store
    private Node VisitEachChildCopy(NodeBuilderContext context, NodeRef node) => CopyWithChildren(node, child => VisitExistingNodeTreeSymbols(context, child), list => VisitExistingNodes(context, node, list), markReused: true);

    /// <summary>A deep copy of an existing node in the builder's store (factory.cloneNode of each node: reused).</summary>
    private Node CopyExistingNode(NodeRef node, bool markReused = true)
    {
        if (node.IsNone)
        {
            return Node.None;
        }
        return CopyWithChildren(node, child => CopyExistingNode(child, markReused), list => CopyExistingList(node, list, markReused), markReused);
    }

    private NodeList CopyExistingList(NodeRef owner, NodeList list, bool markReused)
    {
        if (list.IsNone)
        {
            return NodeList.None;
        }
        var store = StoreOf(owner);
        var result = new List<Node>();
        foreach (var element in store.Nodes(list))
        {
            result.Add(CopyExistingNode(At(owner, element), markReused));
        }
        // getSynthesizedDeepClone keeps the list's hasTrailingComma
        return S.NewList(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(result), store.HasTrailingComma(list));
    }

    private Node CopyExistingEntityName(NodeRef name) => CopyExistingNode(name);

    private Node CopyWithChildren(NodeRef node, Func<NodeRef, Node> visit, Func<NodeList, NodeList> visitList, bool markReused) =>
        CopyWithChildrenFrom(StoreOf(node), node.Node, child => child.IsNone ? Node.None : visit(At(node, child)), visitList, markReused);

    /// <summary>
    /// factory.cloneNode of a node of the builder's store (a shallow copy sharing the children, with the emit flags and
    /// identifier type arguments), marked reused: TypeScript's setTextRange clones a fresh synthesized node before it
    /// sets the node's original.
    /// </summary>
    private Node CloneSynthForReuse(Node node)
    {
        var clone = CopyWithChildrenFrom(S, node, child => child, list => list, markReused: true);
        if (S.EmitNode(node) is EmitNode emitNode)
        {
            var copy = SEmitNode(clone);
            copy.Flags = emitNode.Flags;
            copy.IdentifierTypeArguments = emitNode.IdentifierTypeArguments;
        }
        return clone;
    }

    private static Node ParenthesizeIfPresent(Node node, Func<Node, Node> parenthesize) => node.IsNone ? node : parenthesize(node);

    private Node CopyWithChildrenFrom(NodeStore store, Node n, Func<Node, Node> V, Func<NodeList, NodeList> visitList, bool markReused)
    {
        var copy = CopyShellFrom(store, n, markReused);
        switch (store.Kind(n))
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.PrivateIdentifier:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateHead:
            case SyntaxKind.TemplateMiddle:
            case SyntaxKind.TemplateTail:
            case SyntaxKind.ThisType:
            case SyntaxKind.OmittedExpression:
                break;
            case SyntaxKind.QualifiedName:
                S.SetLeft(copy, V(store.Left(n)));
                S.SetRight(copy, V(store.Right(n)));
                break;
            case SyntaxKind.TypeReference:
                S.SetTypeName(copy, V(store.TypeName(n)));
                S.SetTypeArguments(copy, ParenthesizeTypeArguments(visitList(store.TypeArguments(n))));
                break;
            case SyntaxKind.ExpressionWithTypeArguments:
                S.SetExpression(copy, V(store.Expression(n)));
                S.SetTypeArguments(copy, visitList(store.TypeArguments(n)));
                break;
            case SyntaxKind.TypeQuery:
                S.SetExprName(copy, V(store.ExprName(n)));
                S.SetTypeArguments(copy, visitList(store.TypeArguments(n)));
                break;
            case SyntaxKind.ArrayType:
            {
                var elementType = V(store.ElementType(n));
                S.SetElementType(copy, elementType.IsNone ? elementType : ParenthesizeNonArrayTypeOfPostfixType(elementType));
                break;
            }
            case SyntaxKind.TupleType:
                S.SetElements(copy, visitList(store.Elements(n)));
                break;
            case SyntaxKind.NamedTupleMember:
                S.SetDotDotDotToken(copy, V(store.DotDotDotToken(n)));
                S.SetName(copy, V(store.Name(n)));
                S.SetQuestionToken(copy, V(store.QuestionToken(n)));
                S.SetType(copy, V(store.Type(n)));
                break;
            // (the factory's update functions apply the parenthesizer rules to the visited children)
            case SyntaxKind.OptionalType:
            {
                var type = V(store.Type(n));
                S.SetType(copy, type.IsNone ? type : HasJSDocPostfixQuestion(type) ? SParenthesizedType(type) : ParenthesizeNonArrayTypeOfPostfixType(type));
                break;
            }
            case SyntaxKind.TypeOperator:
            {
                var type = V(store.Type(n));
                S.SetType(copy, type.IsNone ? type : store.Operator(n) == SyntaxKind.ReadonlyKeyword ? ParenthesizeOperandOfReadonlyTypeOperator(type) : ParenthesizeOperandOfTypeOperator(type));
                break;
            }
            case SyntaxKind.RestType:
            case SyntaxKind.ParenthesizedType:
            case SyntaxKind.JSDocTypeExpression:
                S.SetType(copy, V(store.Type(n)));
                break;
            case SyntaxKind.UnionType:
            case SyntaxKind.IntersectionType:
            {
                var types = visitList(store.Types(n));
                if (!types.IsNone)
                {
                    var parenthesized = new Node[S.Length(types)];
                    for (var i = 0; i < parenthesized.Length; i++)
                    {
                        var member = S.Element(types, i);
                        parenthesized[i] = store.Kind(n) == SyntaxKind.UnionType ? ParenthesizeConstituentTypeOfUnionType(member) : ParenthesizeConstituentTypeOfIntersectionType(member);
                    }
                    types = S.NewList(parenthesized, S.HasTrailingComma(types));
                }
                S.SetTypes(copy, types);
                break;
            }
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.CallSignature:
            case SyntaxKind.ConstructSignature:
                if (store.Kind(n) == SyntaxKind.ConstructorType)
                {
                    S.SetModifiers(copy, visitList(store.Modifiers(n)));
                }
                S.SetTypeParameters(copy, visitList(store.TypeParameters(n)));
                S.SetParameters(copy, visitList(store.Parameters(n)));
                S.SetType(copy, V(store.Type(n)));
                break;
            case SyntaxKind.IndexSignature:
                S.SetModifiers(copy, visitList(store.Modifiers(n)));
                S.SetParameters(copy, visitList(store.Parameters(n)));
                S.SetType(copy, V(store.Type(n)));
                break;
            case SyntaxKind.MethodSignature:
                S.SetModifiers(copy, visitList(store.Modifiers(n)));
                S.SetName(copy, V(store.Name(n)));
                S.SetQuestionToken(copy, V(store.QuestionToken(n)));
                S.SetTypeParameters(copy, visitList(store.TypeParameters(n)));
                S.SetParameters(copy, visitList(store.Parameters(n)));
                S.SetType(copy, V(store.Type(n)));
                break;
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                S.SetModifiers(copy, visitList(store.Modifiers(n)));
                S.SetName(copy, V(store.Name(n)));
                S.SetParameters(copy, visitList(store.Parameters(n)));
                if (store.Kind(n) == SyntaxKind.GetAccessor)
                {
                    S.SetType(copy, V(store.Type(n)));
                }
                break;
            case SyntaxKind.TypeLiteral:
                S.SetMembers(copy, visitList(store.Members(n)));
                break;
            case SyntaxKind.PropertySignature:
                S.SetModifiers(copy, visitList(store.Modifiers(n)));
                S.SetName(copy, V(store.Name(n)));
                S.SetQuestionToken(copy, V(store.QuestionToken(n)));
                S.SetType(copy, V(store.Type(n)));
                break;
            case SyntaxKind.Parameter:
                S.SetModifiers(copy, visitList(store.Modifiers(n)));
                S.SetDotDotDotToken(copy, V(store.DotDotDotToken(n)));
                S.SetName(copy, V(store.Name(n)));
                S.SetQuestionToken(copy, V(store.QuestionToken(n)));
                S.SetType(copy, V(store.Type(n)));
                S.SetInitializer(copy, V(store.Initializer(n)));
                break;
            case SyntaxKind.TypeParameter:
                S.SetModifiers(copy, visitList(store.Modifiers(n)));
                S.SetName(copy, V(store.Name(n)));
                S.SetConstraint(copy, V(store.Constraint(n)));
                S.SetDefault(copy, V(store.Default(n)));
                break;
            case SyntaxKind.IndexedAccessType:
            {
                var objectType = V(store.ObjectType(n));
                S.SetObjectType(copy, objectType.IsNone ? objectType : ParenthesizeNonArrayTypeOfPostfixType(objectType));
                S.SetIndexType(copy, V(store.IndexType(n)));
                break;
            }
            case SyntaxKind.MappedType:
                S.SetReadonlyToken(copy, V(store.ReadonlyToken(n)));
                S.SetTypeParameter(copy, V(store.TypeParameter(n)));
                S.SetNameType(copy, V(store.NameType(n)));
                S.SetQuestionToken(copy, V(store.QuestionToken(n)));
                S.SetType(copy, V(store.Type(n)));
                S.SetMembers(copy, visitList(store.Members(n)));
                break;
            case SyntaxKind.LiteralType:
                S.SetLiteral(copy, V(store.Literal(n)));
                break;
            case SyntaxKind.PrefixUnaryExpression:
                S.SetOperand(copy, V(store.Operand(n)));
                break;
            case SyntaxKind.TemplateLiteralType:
                S.SetHead(copy, V(store.Head(n)));
                S.SetTemplateSpans(copy, visitList(store.TemplateSpans(n)));
                break;
            case SyntaxKind.TemplateLiteralTypeSpan:
                S.SetType(copy, V(store.Type(n)));
                S.SetLiteral(copy, V(store.Literal(n)));
                break;
            case SyntaxKind.ConditionalType:
                S.SetCheckType(copy, ParenthesizeIfPresent(V(store.CheckType(n)), ParenthesizeCheckTypeOfConditionalType));
                S.SetExtendsType(copy, ParenthesizeIfPresent(V(store.ExtendsType(n)), ParenthesizeExtendsTypeOfConditionalType));
                S.SetTrueType(copy, V(store.TrueType(n)));
                S.SetFalseType(copy, V(store.FalseType(n)));
                break;
            case SyntaxKind.InferType:
                S.SetTypeParameter(copy, V(store.TypeParameter(n)));
                break;
            case SyntaxKind.ImportType:
                S.SetArgument(copy, V(store.Argument(n)));
                S.SetAttributes(copy, V(store.Attributes(n)));
                S.SetQualifier(copy, V(store.Qualifier(n)));
                S.SetTypeArguments(copy, visitList(store.TypeArguments(n)));
                break;
            case SyntaxKind.ImportAttributes:
                S.SetElements(copy, visitList(store.Elements(n)));
                break;
            case SyntaxKind.ImportAttribute:
                S.SetName(copy, V(store.Name(n)));
                S.SetValue(copy, V(store.Value(n)));
                break;
            case SyntaxKind.TypePredicate:
                S.SetAssertsModifier(copy, V(store.AssertsModifier(n)));
                S.SetParameterName(copy, V(store.ParameterName(n)));
                S.SetType(copy, V(store.Type(n)));
                break;
            case SyntaxKind.ComputedPropertyName:
                S.SetExpression(copy, V(store.Expression(n)));
                break;
            case SyntaxKind.PropertyAccessExpression:
                S.SetExpression(copy, V(store.Expression(n)));
                S.SetQuestionDotToken(copy, V(store.QuestionDotToken(n)));
                S.SetName(copy, V(store.Name(n)));
                break;
            case SyntaxKind.ElementAccessExpression:
                S.SetExpression(copy, V(store.Expression(n)));
                S.SetQuestionDotToken(copy, V(store.QuestionDotToken(n)));
                S.SetArgumentExpression(copy, V(store.ArgumentExpression(n)));
                break;
            case SyntaxKind.ObjectBindingPattern:
            case SyntaxKind.ArrayBindingPattern:
                S.SetElements(copy, visitList(store.Elements(n)));
                break;
            // (module C9e: expressions of computed names in binding patterns, visitEachChild of cloneBindingName)
            case SyntaxKind.CallExpression:
                S.SetExpression(copy, V(store.Expression(n)));
                S.SetQuestionDotToken(copy, V(store.QuestionDotToken(n)));
                S.SetTypeArguments(copy, visitList(store.TypeArguments(n)));
                S.SetArguments(copy, visitList(store.Arguments(n)));
                break;
            case SyntaxKind.BinaryExpression:
                S.SetLeft(copy, V(store.Left(n)));
                S.SetOperatorToken(copy, V(store.OperatorToken(n)));
                S.SetRight(copy, V(store.Right(n)));
                break;
            case SyntaxKind.ParenthesizedExpression:
                S.SetExpression(copy, V(store.Expression(n)));
                break;
            case SyntaxKind.ClassExpression:
                S.SetModifiers(copy, visitList(store.Modifiers(n)));
                S.SetName(copy, V(store.Name(n)));
                S.SetTypeParameters(copy, visitList(store.TypeParameters(n)));
                S.SetHeritageClauses(copy, visitList(store.HeritageClauses(n)));
                S.SetMembers(copy, visitList(store.Members(n)));
                break;
            case SyntaxKind.HeritageClause:
                S.SetToken(copy, store.Token(n));
                S.SetTypes(copy, visitList(store.Types(n)));
                break;
            case SyntaxKind.PropertyDeclaration:
                S.SetModifiers(copy, visitList(store.Modifiers(n)));
                S.SetName(copy, V(store.Name(n)));
                S.SetQuestionToken(copy, V(store.QuestionToken(n)));
                S.SetExclamationToken(copy, V(store.ExclamationToken(n)));
                S.SetType(copy, V(store.Type(n)));
                S.SetInitializer(copy, V(store.Initializer(n)));
                break;
            case SyntaxKind.BindingElement:
                S.SetDotDotDotToken(copy, V(store.DotDotDotToken(n)));
                S.SetPropertyName(copy, V(store.PropertyName(n)));
                S.SetName(copy, V(store.Name(n)));
                S.SetInitializer(copy, V(store.Initializer(n)));
                break;
            default:
                if (Utilities.IsKeyword(store.Kind(n)) || store.Kind(n) is >= SyntaxKind.FirstToken and <= SyntaxKind.LastToken)
                {
                    break;
                }
                reuseUnsupported = true;
                break;
        }
        return copy;
    }

    // checker.ts parameterToParameterDeclarationName's cloneBindingName: the binding pattern without initializers,
    // every node SingleLine | NoAsciiEscaping
    private Node CloneBindingName(NodeRef name)
    {
        Node Visit(NodeRef node)
        {
            // elideInitializerAndSetEmitFlags: the initializer of a binding element is removed before its children are visited
            var store = StoreOf(node);
            var initializer = store.Kind(node.Node) == SyntaxKind.BindingElement ? store.Initializer(node.Node) : Node.None;
            Node VisitChild(NodeRef child) => !initializer.IsNone && child.Node == initializer ? Node.None : Visit(child);
            var copy = CopyWithChildren(node, VisitChild, list => CopyListWith(node, list, Visit), markReused: true);
            if (S.Kind(copy) == SyntaxKind.BindingElement)
            {
                S.SetInitializer(copy, Node.None);
            }
            return SetEmitFlags(copy, EmitFlags.SingleLine | EmitFlags.NoAsciiEscaping);
        }
        var oldUnsupported = reuseUnsupported;
        reuseUnsupported = false;
        var result = Visit(name);
        if (reuseUnsupported)
        {
            result = SIdentifier(Utilities.DeclarationNameToString(StoreOf(name), name.Node));
        }
        reuseUnsupported = oldUnsupported;
        return result;
    }

    private NodeList CopyListWith(NodeRef owner, NodeList list, Func<NodeRef, Node> visit)
    {
        if (list.IsNone)
        {
            return NodeList.None;
        }
        var store = StoreOf(owner);
        var result = new List<Node>();
        foreach (var element in store.Nodes(list))
        {
            result.Add(visit(At(owner, element)));
        }
        // visitNodes keeps the list's hasTrailingComma
        return S.NewList(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(result), store.HasTrailingComma(list));
    }

    // ---- resolver: canReuseTypeNode, trackExistingEntityName, serializeTypeName, serializeExistingTypeNode ----

    // checker.ts getTypeFromTypeNode(context, node, noMappedTypes)
    private Type GetTypeFromTypeNodeInContext(NodeBuilderContext context, NodeRef node, bool noMappedTypes = false)
    {
        var type = GetTypeFromTypeNode(node);
        if (context.Mapper is null)
        {
            return type;
        }
        var mappedType = InstantiateTypeWithMapper(type, context.Mapper);
        return noMappedTypes && mappedType != type ? Type.None : mappedType;
    }

    // checker.ts canReuseTypeNode
    private bool CanReuseTypeNode(NodeBuilderContext context, NodeRef existing)
    {
        var type = GetTypeFromTypeNodeInContext(context, existing, noMappedTypes: true);
        if (type.IsNone)
        {
            return false;
        }
        var store = StoreOf(existing);
        var n = existing.Node;
        if (Utilities.IsInJSFile(store, n) && store.Kind(n) == SyntaxKind.ImportType && store.Kind(store.Argument(n)) == SyntaxKind.LiteralType &&
            store.Kind(store.Literal(store.Argument(n))) == SyntaxKind.StringLiteral)
        {
            // isLiteralImportTypeNode: ensure resolvedSymbol is present
            GetTypeFromTypeNode(existing);
            var nodeSymbol = SymbolNodeLinks.Has(existing) ? SymbolNodeLinks.Get(existing).ResolvedSymbol : SymbolRef.None;
            if (nodeSymbol.IsNone)
            {
                return true;
            }
            var typeArguments = store.TypeArguments(n);
            return !(
                // The import type resolved using jsdoc fallback logic
                (!store.IsTypeOf(n) && (SymbolFlagsOf(nodeSymbol) & SymbolFlags.Type) == 0) ||
                // The import type had type arguments autofilled by js fallback logic
                !((typeArguments.IsNone ? 0 : store.Length(typeArguments)) >= GetMinTypeArgumentCount(GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(nodeSymbol)?.ToArray() ?? [])));
        }
        if (store.Kind(n) == SyntaxKind.TypeReference)
        {
            if (IsConstTypeReference(store, n))
            {
                return false;
            }
            var symbol = SymbolNodeLinks.Has(existing) ? SymbolNodeLinks.Get(existing).ResolvedSymbol : SymbolRef.None;
            if (symbol.IsNone)
            {
                return false;
            }
            if ((SymbolFlagsOf(symbol) & SymbolFlags.TypeParameter) != 0)
            {
                var declaredType = GetDeclaredTypeOfSymbol(symbol);
                return !(context.Mapper is not null && GetMappedType(declaredType, context.Mapper) != declaredType);
            }
            if ((store.Flags(n) & NodeFlags.JSDoc) != 0)
            {
                return ExistingTypeNodeIsNotReferenceOrIsReferenceWithCompatibleTypeArgumentCount(existing, type)
                    && (SymbolFlagsOf(symbol) & SymbolFlags.Type) != 0;
            }
        }
        if (store.Kind(n) == SyntaxKind.TypeOperator && store.Operator(n) == SyntaxKind.UniqueKeyword && store.Kind(store.Type(n)) == SyntaxKind.SymbolKeyword)
        {
            var effectiveEnclosingContext = context.EnclosingDeclaration;
            for (var a = existing; !a.IsNone; a = Parent(a))
            {
                if (a == effectiveEnclosingContext)
                {
                    return true;
                }
            }
            return false;
        }
        return true;
    }

    // utilities.ts isConstTypeReference
    private static bool IsConstTypeReference(NodeStore store, Node node) =>
        store.Kind(node) == SyntaxKind.TypeReference && store.Kind(store.TypeName(node)) == SyntaxKind.Identifier &&
        (store.Texts.GetString(store.EscapedText(store.TypeName(node))) ?? "") == "const" && store.TypeArguments(node).IsNone;

    // checker.ts existingTypeNodeIsNotReferenceOrIsReferenceWithCompatibleTypeArgumentCount
    private bool ExistingTypeNodeIsNotReferenceOrIsReferenceWithCompatibleTypeArgumentCount(NodeRef existing, Type type)
    {
        // In JS, you can say something like `Foo` and get a `Foo<any>` implicitly - we don't want to preserve that original `Foo` in these cases, though.
        if ((Types.ObjectFlagsOf(type) & ObjectFlags.Reference) == 0)
        {
            return true;
        }
        if (Kind(existing) != SyntaxKind.TypeReference)
        {
            return true;
        }
        // `type` is a reference type, and `existing` is a type reference node, but we still need to make sure they refer to the _same_ target type
        // before we go comparing their type argument counts.
        _ = GetTypeFromTypeNode(existing); // call to ensure symbol is resolved
        var symbol = SymbolNodeLinks.Has(existing) ? SymbolNodeLinks.Get(existing).ResolvedSymbol : SymbolRef.None;
        var existingTarget = symbol.IsNone ? Type.None : GetDeclaredTypeOfSymbol(symbol);
        if (existingTarget.IsNone || existingTarget != Types.Object(type).Target)
        {
            return true;
        }
        var store = StoreOf(existing);
        var typeArguments = store.TypeArguments(existing.Node);
        return (typeArguments.IsNone ? 0 : store.Length(typeArguments)) >= GetMinTypeArgumentCount(Types.Object(Types.Object(type).Target).TypeParameters);
    }

    // checker.ts trackExistingEntityName
    private (bool IntroducesError, Node Node) TrackExistingEntityName(NodeBuilderContext context, NodeRef node)
    {
        var store = StoreOf(node);
        var leftmost = At(node, Utilities.GetFirstIdentifier(store, node.Node));
        if (Utilities.IsInJSFile(store, node.Node))
        {
            var leftmostParent = store.Parent(leftmost.Node);
            if (Utilities.IsExportsIdentifier(store, leftmost.Node) || Utilities.IsModuleExportsAccessExpression(store, leftmostParent) ||
                (store.Kind(leftmostParent) == SyntaxKind.QualifiedName && Utilities.IsModuleIdentifier(store, store.Left(leftmostParent)) &&
                    Utilities.IsExportsIdentifier(store, store.Right(leftmostParent))))
            {
                return (true, CopyExistingNode(node));
            }
        }
        var meaning = GetMeaningOfEntityNameReference(store, node.Node);
        SymbolRef sym;
        if (Utilities.IsThisIdentifier(store, leftmost.Node))
        {
            // `this` isn't a bindable identifier - skip resolution, find a relevant `this` symbol directly and avoid exhaustive scope traversal
            sym = GetSymbolOfDeclaration(At(node, Utilities.GetThisContainer(store, leftmost.Node, false, false)));
            var introduces = IsSymbolAccessible(sym, leftmost, null, meaning) != SymbolAccessibility.Accessible;
            return (introduces, AttachSymbolToLeftmostIdentifier(node));
        }
        sym = ResolveEntityName(leftmost, meaning, ignoreErrors: true, dontResolveAlias: true);
        if ((!context.EnclosingDeclaration.IsNone || context.FakeScope is not null) && !(!sym.IsNone && (SymbolFlagsOf(sym) & SymbolFlags.TypeParameter) != 0))
        {
            sym = GetExportSymbolOfValueSymbolIfExported(sym);
            // Some declarations may be transplanted to a new location.
            // When this happens we need to make sure that the name has the same meaning at both locations
            // We also check for the unknownSymbol because when we create a fake scope some parameters may actually not be usable
            // either because they are the expanded rest parameter,
            // or because they are the newly added parameters from the tuple, which might have different meanings in the original context
            var symAtLocation = ResolveEntityNameAtBuilderLocation(leftmost, meaning, context);
            if (
                // Check for unusable parameters symbols
                symAtLocation == unknownSymbol ||
                // If the symbol is not found, but was not found in the original scope either we probably have an error, don't reuse the node
                (symAtLocation.IsNone && !sym.IsNone) ||
                // If the symbol is found both in declaration scope and in current scope then it should point to the same reference
                (!symAtLocation.IsNone && !sym.IsNone && GetSymbolIfSameReference(GetExportSymbolOfValueSymbolIfExported(symAtLocation), sym).IsNone))
            {
                return (true, CopyExistingNode(node));
            }
            sym = symAtLocation;
        }
        if (!sym.IsNone)
        {
            // If a parameter is resolvable in the current context it is also visible, so no need to go to symbol accesibility
            var valueDeclaration = ValueDeclarationOf(sym);
            if ((SymbolFlagsOf(sym) & SymbolFlags.FunctionScopedVariable) != 0 && !valueDeclaration.IsNone)
            {
                if (Utilities.IsPartOfParameterDeclaration(StoreOf(valueDeclaration), valueDeclaration.Node) || Kind(valueDeclaration) == SyntaxKind.JSDocParameterTag)
                {
                    return (false, AttachSymbolToLeftmostIdentifier(node));
                }
            }
            var introducesError = false;
            if ((SymbolFlagsOf(sym) & SymbolFlags.TypeParameter) == 0 && // Type parameters are visible in the current context if they are are resolvable
                !Utilities.IsDeclarationName(store, node.Node) &&
                IsSymbolAccessible(sym, context.EnclosingDeclaration, context.FakeScope, meaning) != SymbolAccessibility.Accessible)
            {
                introducesError = true;
            }
            return (introducesError, AttachSymbolToLeftmostIdentifier(node));
        }
        return (false, CopyExistingNode(node));

        // Attaches a `.symbol` member to an identifier, cloning it to do so, so symbol information
        // is smuggled out for symbol display information.
        Node AttachSymbolToLeftmostIdentifier(NodeRef current)
        {
            if (current == leftmost)
            {
                Node name;
                if (!sym.IsNone && (SymbolFlagsOf(sym) & SymbolFlags.TypeParameter) != 0)
                {
                    name = TypeParameterToName(GetDeclaredTypeOfSymbol(sym), context);
                }
                else
                {
                    name = CopyShell(current);
                }
                return SetTextRange(context, SetEmitFlags(name, EmitFlags.NoAsciiEscaping), current);
            }
            var currentStore = StoreOf(current);
            Node updated;
            switch (currentStore.Kind(current.Node))
            {
                case SyntaxKind.QualifiedName:
                    updated = SQualifiedName(AttachSymbolToLeftmostIdentifier(At(current, currentStore.Left(current.Node))), CopyExistingNode(At(current, currentStore.Right(current.Node))));
                    break;
                case SyntaxKind.PropertyAccessExpression:
                    updated = SPropertyAccessExpression(AttachSymbolToLeftmostIdentifier(At(current, currentStore.Expression(current.Node))), CopyExistingNode(At(current, currentStore.Name(current.Node))));
                    break;
                default:
                    updated = CopyExistingNode(current);
                    break;
            }
            return SetTextRange(context, updated, current);
        }
    }

    // resolveEntityName(leftmost, meaning, ignoreErrors, dontResolveAlias, context.enclosingDeclaration): the first
    // identifier resolved at the builder's location (a fake scope first)
    private SymbolRef ResolveEntityNameAtBuilderLocation(NodeRef leftmost, SymbolFlags meaning, NodeBuilderContext context)
    {
        var store = StoreOf(leftmost);
        var name = store.Texts.GetString(store.EscapedText(leftmost.Node)) ?? "";
        return GetMergedSymbol(ResolveNameInScope(context.EnclosingDeclaration, context.FakeScope, name, meaning, isUse: true));
    }

    // checker.ts serializeTypeName
    private Node SerializeTypeName(NodeBuilderContext context, NodeRef node, bool isTypeOf, List<Node>? typeArguments)
    {
        var meaning = isTypeOf ? SymbolFlags.Value : SymbolFlags.Type;
        var symbol = ResolveEntityName(node, meaning, ignoreErrors: true);
        if (symbol.IsNone)
        {
            return Node.None;
        }
        var resolvedSymbol = (SymbolFlagsOf(symbol) & SymbolFlags.Alias) != 0 ? ResolveAlias(symbol) : symbol;
        if (IsSymbolAccessible(symbol, context.EnclosingDeclaration, context.FakeScope, meaning) != SymbolAccessibility.Accessible)
        {
            return Node.None;
        }
        return SymbolToTypeNode(resolvedSymbol, context, meaning, typeArguments);
    }

    // checker.ts serializeExistingTypeNode (the resolver's)
    private Node SerializeExistingTypeNode(NodeBuilderContext context, NodeRef typeNode, bool addUndefined)
    {
        var type = GetTypeFromTypeNodeInContext(context, typeNode);
        if (addUndefined && !SomeType(type, t => (Types.Flags(t) & TypeFlags.Undefined) != 0) && CanReuseTypeNode(context, typeNode))
        {
            var clone = TryReuseExistingTypeNode(context, typeNode);
            if (!clone.IsNone)
            {
                return SUnionType([clone, SKeyword(SyntaxKind.UndefinedKeyword)]);
            }
        }
        return TypeToTypeNodeHelper(type, context);
    }

    // ---- the syntactic builder (expressionToTypeNode.ts) ----

    // serializeExistingTypeNode (the syntactic builder's)
    private Node SerializeExistingTypeNodeSyntactic(NodeRef typeNode, NodeBuilderContext context, bool addUndefined = false)
    {
        if (typeNode.IsNone)
        {
            return Node.None;
        }
        var result = Node.None;
        if ((!addUndefined || CanAddUndefinedExisting(typeNode)) && CanReuseTypeNode(context, typeNode))
        {
            result = TryReuseExistingTypeNode(context, typeNode);
            if (!result.IsNone)
            {
                result = AddUndefinedIfNeeded(result, addUndefined, NodeRef.None, context);
            }
        }
        return result;
    }

    // serializeTypeAnnotationOfDeclaration (requiresAddingUndefined: null = undefined)
    private Node SerializeTypeAnnotationOfDeclaration(NodeRef declaredType, NodeBuilderContext context, NodeRef node, SymbolRef symbol, bool? requiresAddingUndefined = null)
    {
        var useFallback = requiresAddingUndefined is not null;
        if (declaredType.IsNone)
        {
            return Node.None;
        }
        var addUndefined = requiresAddingUndefined == true;
        if (!CanReuseTypeNodeAnnotation(context, node, declaredType, symbol, addUndefined))
        {
            // If we need to add undefined, can add undefined, and the resolver says we can reuse the type, we reuse the type
            // If we don't know syntactically that we can add the undefined, we will report the fallback below.
            if (!addUndefined || !CanReuseTypeNodeAnnotation(context, node, declaredType, symbol, false))
            {
                return Node.None;
            }
        }
        var result = Node.None;
        if (!addUndefined || CanAddUndefinedExisting(declaredType))
        {
            result = SerializeExistingTypeNodeSyntactic(declaredType, context, addUndefined);
        }
        if (!result.IsNone || !useFallback)
        {
            return result;
        }
        var fallback = SerializeExistingTypeNode(context, declaredType, addUndefined);
        return !fallback.IsNone ? fallback : SKeyword(SyntaxKind.AnyKeyword);
    }

    // serializeExistingTypeNodeWithFallback
    private Node SerializeExistingTypeNodeWithFallback(NodeRef typeNode, NodeBuilderContext context, bool addUndefined = false)
    {
        if (typeNode.IsNone)
        {
            return Node.None;
        }
        var result = SerializeExistingTypeNodeSyntactic(typeNode, context, addUndefined);
        if (!result.IsNone)
        {
            return result;
        }
        var fallback = SerializeExistingTypeNode(context, typeNode, addUndefined);
        return !fallback.IsNone ? fallback : SKeyword(SyntaxKind.AnyKeyword);
    }

    // serializeTypeOfAccessor
    private Node SerializeTypeOfAccessor(NodeRef accessor, SymbolRef symbol, NodeBuilderContext context)
    {
        var result = TypeFromAccessor(accessor, symbol, context);
        return !result.IsNone ? result : InferAccessorType(accessor, GetAllAccessorDeclarationsRef(accessor), context, symbol);
    }

    // serializeTypeOfExpression
    private Node SerializeTypeOfExpression(NodeRef expr, NodeBuilderContext context, bool addUndefined = false, bool preserveLiterals = false)
    {
        var result = TypeFromExpression(expr, context, false, addUndefined, preserveLiterals);
        return !result.Type.IsNone ? result.Type : InferExpressionType(expr, context);
    }

    // serializeTypeOfDeclaration (the syntactic builder's)
    private Node SerializeTypeOfDeclarationSyntactic(NodeRef node, SymbolRef symbol, NodeBuilderContext context)
    {
        switch (Kind(node))
        {
            case SyntaxKind.Parameter:
            case SyntaxKind.JSDocParameterTag:
                return TypeFromParameter(node, symbol, context);
            case SyntaxKind.VariableDeclaration:
                return TypeFromVariable(node, symbol, context);
            case SyntaxKind.PropertySignature:
            case SyntaxKind.JSDocPropertyTag:
            case SyntaxKind.PropertyDeclaration:
                return TypeFromProperty(node, symbol, context);
            case SyntaxKind.BindingElement:
                return InferTypeOfDeclaration(node, symbol, context);
            case SyntaxKind.ExportAssignment:
                return SerializeTypeOfExpression(At(node, StoreOf(node).Expression(node.Node)), context, false, preserveLiterals: true);
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.BinaryExpression:
                return TypeFromExpandoProperty(node, symbol, context);
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.ShorthandPropertyAssignment:
                return TypeFromPropertyAssignment(node, symbol, context);
            default:
                throw new InvalidOperationException("Node needs to be an inferrable node, found " + Kind(node));
        }
    }

    // typeFromPropertyAssignment
    private Node TypeFromPropertyAssignment(NodeRef node, SymbolRef symbol, NodeBuilderContext context)
    {
        var typeAnnotation = GetEffectiveTypeAnnotationNodeRef(node);
        var result = Node.None;
        if (!typeAnnotation.IsNone && CanReuseTypeNodeAnnotation(context, node, typeAnnotation, symbol, false))
        {
            result = SerializeExistingTypeNodeSyntactic(typeAnnotation, context);
        }
        if (result.IsNone && Kind(node) == SyntaxKind.PropertyAssignment)
        {
            var store = StoreOf(node);
            var initializer = store.Initializer(node.Node);
            var assertionNode = NodeRef.None;
            if (FactoryUtilities.IsJSDocTypeAssertion(store, initializer))
            {
                assertionNode = GetJSDocTypeAssertionType(At(node, initializer));
            }
            else if (store.Kind(initializer) is SyntaxKind.AsExpression or SyntaxKind.TypeAssertionExpression)
            {
                assertionNode = At(node, store.Type(initializer));
            }
            if (!assertionNode.IsNone && !IsConstTypeReference(store, assertionNode.Node) && CanReuseTypeNodeAnnotation(context, node, assertionNode, symbol, false))
            {
                result = SerializeExistingTypeNodeSyntactic(assertionNode, context);
            }
        }
        return !result.IsNone ? result : InferTypeOfDeclaration(node, symbol, context);
    }

    // serializeReturnTypeForSignature (the syntactic builder's)
    private Node SerializeReturnTypeForSignatureSyntactic(NodeRef node, SymbolRef symbol, NodeBuilderContext context)
    {
        switch (Kind(node))
        {
            case SyntaxKind.GetAccessor:
                return SerializeTypeOfAccessor(node, symbol, context);
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.CallSignature:
            case SyntaxKind.Constructor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.JSDocFunctionType:
            case SyntaxKind.JSDocSignature:
                return CreateReturnFromSignature(node, symbol, context);
            default:
                // (signatures of other declarations, e.g. JavaScript expando functions: C9)
                return Node.None;
        }
    }

    // getTypeAnnotationFromAccessor
    private NodeRef GetTypeAnnotationFromAccessor(NodeRef accessor)
    {
        if (accessor.IsNone)
        {
            return NodeRef.None;
        }
        var store = StoreOf(accessor);
        if (store.Kind(accessor.Node) == SyntaxKind.GetAccessor)
        {
            var jsDocType = Utilities.IsInJSFile(store, accessor.Node) ? GetJSDocType(store, accessor.Node) : Node.None;
            var typeNode = !jsDocType.IsNone ? jsDocType : GetEffectiveReturnTypeNode(store, accessor.Node);
            return typeNode.IsNone ? NodeRef.None : At(accessor, typeNode);
        }
        // getEffectiveSetAccessorTypeAnnotationNode
        return GetAnnotatedAccessorTypeNode(accessor);
    }

    private NodeRef GetTypeAnnotationFromAllAccessorDeclarations(NodeRef node, (NodeRef First, NodeRef Second, NodeRef Get, NodeRef Set) accessors)
    {
        var accessorType = GetTypeAnnotationFromAccessor(node);
        if (accessorType.IsNone && node != accessors.First)
        {
            accessorType = GetTypeAnnotationFromAccessor(accessors.First);
        }
        if (accessorType.IsNone && !accessors.Second.IsNone && node != accessors.Second)
        {
            accessorType = GetTypeAnnotationFromAccessor(accessors.Second);
        }
        return accessorType;
    }

    // typeFromAccessor
    private Node TypeFromAccessor(NodeRef node, SymbolRef symbol, NodeBuilderContext context)
    {
        var accessorDeclarations = GetAllAccessorDeclarationsRef(node);
        var accessorType = GetTypeAnnotationFromAllAccessorDeclarations(node, accessorDeclarations);
        if (!accessorType.IsNone && Kind(accessorType) != SyntaxKind.TypePredicate)
        {
            return WithNewScope(context, node, () =>
            {
                var r = SerializeTypeAnnotationOfDeclaration(accessorType, context, node, symbol);
                return !r.IsNone ? r : InferTypeOfDeclaration(node, symbol, context);
            });
        }
        if (!accessorDeclarations.Get.IsNone)
        {
            return WithNewScope(context, accessorDeclarations.Get, () => CreateReturnFromSignature(accessorDeclarations.Get, symbol, context));
        }
        return Node.None;
    }

    // typeFromVariable
    private Node TypeFromVariable(NodeRef node, SymbolRef symbol, NodeBuilderContext context)
    {
        var declaredType = GetEffectiveTypeAnnotationNodeRef(node);
        var resultType = SyntacticResult.Failed;
        var store = StoreOf(node);
        if (!declaredType.IsNone)
        {
            resultType = Result(SerializeTypeAnnotationOfDeclaration(declaredType, context, node, symbol));
        }
        else if (!store.Initializer(node.Node).IsNone && ((DeclarationsOf(symbol)?.Count ?? 0) == 1 || (DeclarationsOf(symbol) ?? []).FindAll(d => Kind(d) == SyntaxKind.VariableDeclaration).Count == 1))
        {
            if (!IsExpandoFunctionDeclaration(node) && !IsContextuallyTyped(node))
            {
                resultType = TypeFromExpression(At(node, store.Initializer(node.Node)), context, false, false, IsVarConstLike(node));
            }
        }
        return !resultType.Type.IsNone ? resultType.Type : InferTypeOfDeclaration(node, symbol, context);
    }

    // typeFromParameter
    private Node TypeFromParameter(NodeRef node, SymbolRef symbol, NodeBuilderContext context)
    {
        var parent = Parent(node);
        if (Kind(parent) == SyntaxKind.SetAccessor)
        {
            return SerializeTypeOfAccessor(parent, SymbolRef.None, context);
        }
        var declaredType = GetEffectiveTypeAnnotationNodeRef(node);
        var addUndefined = ResolverRequiresAddingImplicitUndefined(node, symbol, context.EnclosingDeclaration);
        var resultType = SyntacticResult.Failed;
        var store = StoreOf(node);
        if (!declaredType.IsNone)
        {
            resultType = Result(SerializeTypeAnnotationOfDeclaration(declaredType, context, node, symbol, addUndefined));
        }
        else if (store.Kind(node.Node) == SyntaxKind.Parameter && !store.Initializer(node.Node).IsNone && store.Kind(store.Name(node.Node)) == SyntaxKind.Identifier && !IsContextuallyTyped(node))
        {
            resultType = TypeFromExpression(At(node, store.Initializer(node.Node)), context, false, addUndefined);
        }
        return !resultType.Type.IsNone ? resultType.Type : InferTypeOfDeclaration(node, symbol, context);
    }

    // typeFromExpandoProperty
    private Node TypeFromExpandoProperty(NodeRef node, SymbolRef symbol, NodeBuilderContext context)
    {
        var declaredType = GetEffectiveTypeAnnotationNodeRef(node);
        var result = Node.None;
        if (!declaredType.IsNone)
        {
            result = SerializeTypeAnnotationOfDeclaration(declaredType, context, node, symbol);
        }
        var oldSuppressReportInferenceFallback = context.SuppressReportInferenceFallback;
        context.SuppressReportInferenceFallback = true;
        var resultType = !result.IsNone ? result : InferTypeOfDeclaration(node, symbol, context);
        context.SuppressReportInferenceFallback = oldSuppressReportInferenceFallback;
        return resultType;
    }

    // typeFromProperty
    private Node TypeFromProperty(NodeRef node, SymbolRef symbol, NodeBuilderContext context)
    {
        var declaredType = GetEffectiveTypeAnnotationNodeRef(node);
        var requiresAddingUndefined = ResolverRequiresAddingImplicitUndefined(node, symbol, context.EnclosingDeclaration);
        var resultType = SyntacticResult.Failed;
        if (!declaredType.IsNone)
        {
            resultType = Result(SerializeTypeAnnotationOfDeclaration(declaredType, context, node, symbol, requiresAddingUndefined));
        }
        else
        {
            var store = StoreOf(node);
            var initializer = store.Kind(node.Node) == SyntaxKind.PropertyDeclaration ? store.Initializer(node.Node) : Node.None;
            if (!initializer.IsNone && !IsContextuallyTyped(node))
            {
                var isReadonly = IsDeclarationReadonly(node);
                resultType = TypeFromExpression(At(node, initializer), context, false, requiresAddingUndefined, isReadonly);
            }
        }
        return !resultType.Type.IsNone ? resultType.Type : InferTypeOfDeclaration(node, symbol, context);
    }

    // utilities.ts isDeclarationReadonly
    private bool IsDeclarationReadonly(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        return (GetCombinedEffectiveModifierFlags(store, declaration.Node) & ModifierFlags.Readonly) != 0 &&
            !(store.Kind(declaration.Node) == SyntaxKind.Parameter && Utilities.IsFunctionLike(store, store.Parent(declaration.Node)) && store.Kind(store.Parent(declaration.Node)) != SyntaxKind.Constructor);
    }

    // utilities.ts isVarConstLike
    private bool IsVarConstLike(NodeRef node)
    {
        var store = StoreOf(node);
        var list = store.Parent(node.Node);
        var blockScope = store.Kind(list) == SyntaxKind.VariableDeclarationList ? store.Flags(list) & NodeFlags.BlockScoped : 0;
        return blockScope is NodeFlags.Const or NodeFlags.Using or NodeFlags.AwaitUsing;
    }

    // inferTypeOfDeclaration (context.noInferenceFallback is set only while the discarded literal nodes are built)
    private Node InferTypeOfDeclaration(NodeRef node, SymbolRef symbol, NodeBuilderContext context) =>
        ResolverSerializeTypeOfDeclaration(context, node, symbol);

    // inferExpressionType
    private Node InferExpressionType(NodeRef node, NodeBuilderContext context)
    {
        var result = ResolverSerializeTypeOfExpression(context, node);
        return !result.IsNone ? result : SKeyword(SyntaxKind.AnyKeyword);
    }

    // inferReturnTypeOfSignatureSignature
    private Node InferReturnTypeOfSignatureSignature(NodeRef node, NodeBuilderContext context, SymbolRef symbol)
    {
        var result = ResolverSerializeReturnTypeForSignature(context, node, symbol);
        return !result.IsNone ? result : SKeyword(SyntaxKind.AnyKeyword);
    }

    // inferAccessorType
    private Node InferAccessorType(NodeRef node, (NodeRef First, NodeRef Second, NodeRef Get, NodeRef Set) allAccessors, NodeBuilderContext context, SymbolRef symbol)
    {
        if (Kind(node) == SyntaxKind.GetAccessor)
        {
            return CreateReturnFromSignature(node, symbol, context);
        }
        var result = !allAccessors.Get.IsNone ? CreateReturnFromSignature(allAccessors.Get, symbol, context) : Node.None;
        if (result.IsNone)
        {
            result = ResolverSerializeTypeOfDeclaration(context, node, symbol);
        }
        return !result.IsNone ? result : SKeyword(SyntaxKind.AnyKeyword);
    }

    private T WithNewScope<T>(NodeBuilderContext context, NodeRef node, Func<T> fn)
    {
        var cleanup = EnterNewScopeForNode(context, node);
        var result = fn();
        cleanup();
        return result;
    }

    // typeFromTypeAssertion
    private SyntacticResult TypeFromTypeAssertion(NodeRef expression, NodeRef type, NodeBuilderContext context, bool requiresAddingUndefined)
    {
        if (IsConstTypeReference(StoreOf(type), type.Node))
        {
            return TypeFromExpression(expression, context, true, requiresAddingUndefined);
        }
        return Result(SerializeExistingTypeNodeWithFallback(type, context, requiresAddingUndefined));
    }

    // typeFromExpression
    private SyntacticResult TypeFromExpression(NodeRef node, NodeBuilderContext context, bool isConstContext = false, bool requiresAddingUndefined = false, bool preserveLiterals = false)
    {
        var store = StoreOf(node);
        var n = node.Node;
        switch (store.Kind(n))
        {
            case SyntaxKind.ParenthesizedExpression:
                if (FactoryUtilities.IsJSDocTypeAssertion(store, n))
                {
                    return TypeFromTypeAssertion(At(node, store.Expression(n)), GetJSDocTypeAssertionType(node), context, requiresAddingUndefined);
                }
                return TypeFromExpression(At(node, store.Expression(n)), context, isConstContext, requiresAddingUndefined);
            case SyntaxKind.Identifier:
                if (GetSymbolAtLocation(node) == undefinedSymbol)
                {
                    return Result(CreateUndefinedTypeNode());
                }
                break;
            case SyntaxKind.NullKeyword:
                if (strictNullChecks)
                {
                    return Result(AddUndefinedIfNeeded(SLiteralType(SToken(SyntaxKind.NullKeyword)), requiresAddingUndefined, node, context));
                }
                return Result(SKeyword(SyntaxKind.AnyKeyword));
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.FunctionExpression:
                return WithNewScope(context, node, () => TypeFromFunctionLikeExpression(node, context));
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.AsExpression:
                return TypeFromTypeAssertion(At(node, store.Expression(n)), At(node, store.Type(n)), context, requiresAddingUndefined);
            case SyntaxKind.PrefixUnaryExpression:
                if (IsPrimitiveLiteralValue(store, n))
                {
                    var operand = store.Operand(n);
                    return TypeFromPrimitiveLiteral(
                        store.Operator(n) == SyntaxKind.PlusToken ? At(node, operand) : node,
                        store.Kind(operand) == SyntaxKind.BigIntLiteral ? SyntaxKind.BigIntKeyword : SyntaxKind.NumberKeyword,
                        context,
                        isConstContext || preserveLiterals,
                        requiresAddingUndefined);
                }
                break;
            case SyntaxKind.ArrayLiteralExpression:
                return TypeFromArrayLiteral(node, context, isConstContext, requiresAddingUndefined);
            case SyntaxKind.ObjectLiteralExpression:
                return TypeFromObjectLiteral(node, context, requiresAddingUndefined);
            case SyntaxKind.ClassExpression:
                return Result(InferExpressionType(node, context));
            case SyntaxKind.TemplateExpression:
                if (!isConstContext && !preserveLiterals)
                {
                    return Result(SKeyword(SyntaxKind.StringKeyword));
                }
                break;
            default:
            {
                SyntaxKind typeKind = 0;
                switch (store.Kind(n))
                {
                    case SyntaxKind.NumericLiteral:
                        typeKind = SyntaxKind.NumberKeyword;
                        break;
                    case SyntaxKind.NoSubstitutionTemplateLiteral:
                    case SyntaxKind.StringLiteral:
                        typeKind = SyntaxKind.StringKeyword;
                        break;
                    case SyntaxKind.BigIntLiteral:
                        typeKind = SyntaxKind.BigIntKeyword;
                        break;
                    case SyntaxKind.TrueKeyword:
                    case SyntaxKind.FalseKeyword:
                        typeKind = SyntaxKind.BooleanKeyword;
                        break;
                }
                if (typeKind != 0)
                {
                    return TypeFromPrimitiveLiteral(node, typeKind, context, isConstContext || preserveLiterals, requiresAddingUndefined);
                }
                break;
            }
        }
        return SyntacticResult.Failed;
    }

    // utilities.ts isPrimitiveLiteralValue
    private static bool IsPrimitiveLiteralValue(NodeStore store, Node node, bool includeBigInt = true)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                return true;
            case SyntaxKind.BigIntLiteral:
                return includeBigInt;
            case SyntaxKind.PrefixUnaryExpression:
            {
                var operand = store.Operand(node);
                if (store.Operator(node) == SyntaxKind.MinusToken)
                {
                    return store.Kind(operand) == SyntaxKind.NumericLiteral || (includeBigInt && store.Kind(operand) == SyntaxKind.BigIntLiteral);
                }
                if (store.Operator(node) == SyntaxKind.PlusToken)
                {
                    return store.Kind(operand) == SyntaxKind.NumericLiteral;
                }
                return false;
            }
            default:
                return false;
        }
    }

    // typeFromFunctionLikeExpression
    private SyntacticResult TypeFromFunctionLikeExpression(NodeRef fnNode, NodeBuilderContext context)
    {
        var returnType = CreateReturnFromSignature(fnNode, SymbolRef.None, context);
        var store = StoreOf(fnNode);
        var typeParameters = ReuseTypeParameters(fnNode, store.TypeParameters(fnNode.Node), context);
        var parameters = new List<Node>();
        foreach (var p in store.Nodes(store.Parameters(fnNode.Node)))
        {
            parameters.Add(EnsureParameter(At(fnNode, p), context));
        }
        return Result(SSignature(SyntaxKind.FunctionType, NodeList.None, Node.None, Node.None, typeParameters, SList(parameters), returnType));
    }

    // typeFromArrayLiteral: TypeScript builds a tuple node and discards it (notImplemented)
    private SyntacticResult TypeFromArrayLiteral(NodeRef arrayLiteral, NodeBuilderContext context, bool isConstContext, bool requiresAddingUndefined)
    {
        var store = StoreOf(arrayLiteral);
        var canGet = isConstContext;
        if (canGet)
        {
            foreach (var element in store.Nodes(store.Elements(arrayLiteral.Node)))
            {
                if (store.Kind(element) == SyntaxKind.SpreadElement)
                {
                    canGet = false;
                    break;
                }
            }
        }
        if (!canGet)
        {
            if (requiresAddingUndefined || Utilities.IsDeclaration(store, WalkUpParenthesizedExpressionsParent(store, arrayLiteral.Node)))
            {
                return SyntacticResult.AlreadyReported;
            }
            return Result(InferExpressionType(arrayLiteral, context));
        }
        return SyntacticResult.NotImplemented;
    }

    // typeFromObjectLiteral: TypeScript builds a type literal and discards it (notImplemented)
    private SyntacticResult TypeFromObjectLiteral(NodeRef objectLiteral, NodeBuilderContext context, bool requiresAddingUndefined)
    {
        var store = StoreOf(objectLiteral);
        var canGet = true;
        foreach (var prop in store.Nodes(store.Properties(objectLiteral.Node)))
        {
            if ((store.Flags(prop) & NodeFlags.ThisNodeHasError) != 0)
            {
                canGet = false;
                break; // Bail if parse errors
            }
            var kind = store.Kind(prop);
            if (kind is SyntaxKind.ShorthandPropertyAssignment or SyntaxKind.SpreadAssignment)
            {
                canGet = false;
                continue;
            }
            var name = store.Name(prop);
            if ((store.Flags(name) & NodeFlags.ThisNodeHasError) != 0)
            {
                canGet = false;
                break; // Bail if parse errors
            }
            if (store.Kind(name) == SyntaxKind.PrivateIdentifier)
            {
                // Not valid in object literals but the compiler will complain about this, we just ignore it here.
                canGet = false;
            }
            else if (store.Kind(name) == SyntaxKind.ComputedPropertyName)
            {
                var expression = store.Expression(name);
                if (!IsPrimitiveLiteralValue(store, expression, includeBigInt: false) && !IsDefinitelyReferenceToGlobalSymbolObject(At(objectLiteral, expression)))
                {
                    canGet = false;
                }
            }
        }
        if (!canGet)
        {
            if (requiresAddingUndefined || Utilities.IsDeclaration(store, WalkUpParenthesizedExpressionsParent(store, objectLiteral.Node)))
            {
                return SyntacticResult.AlreadyReported;
            }
            return Result(InferExpressionType(objectLiteral, context));
        }
        return SyntacticResult.NotImplemented;
    }

    // walkUpParenthesizedExpressions(node).parent
    private static Node WalkUpParenthesizedExpressionsParent(NodeStore store, Node node)
    {
        var parent = store.Parent(node);
        while (store.Kind(parent) == SyntaxKind.ParenthesizedExpression)
        {
            parent = store.Parent(parent);
        }
        return parent;
    }

    // checker.ts isDefinitelyReferenceToGlobalSymbolObject
    private bool IsDefinitelyReferenceToGlobalSymbolObject(NodeRef node)
    {
        var store = StoreOf(node);
        if (store.Kind(node.Node) != SyntaxKind.PropertyAccessExpression)
        {
            return false;
        }
        var expression = store.Expression(node.Node);
        if (store.Kind(expression) != SyntaxKind.Identifier || (store.Texts.GetString(store.EscapedText(expression)) ?? "") != "Symbol")
        {
            return false;
        }
        // Exactly `Symbol.something` and `Symbol` either does not resolve or definitely resolves to the global Symbol
        return ResolveName(At(node, expression), "Symbol", At(node, expression), SymbolFlags.Value | SymbolFlags.ExportValue, null, isUse: false) == GetGlobalSymbolForBuilder("Symbol");
    }

    // ensureParameter
    private Node EnsureParameter(NodeRef p, NodeBuilderContext context)
    {
        var store = StoreOf(p);
        var dotDotDot = store.DotDotDotToken(p.Node);
        var parameter = SParameterDeclaration(
            NodeList.None,
            dotDotDot.IsNone ? Node.None : SetTextRange(context, SToken(SyntaxKind.DotDotDotToken), At(p, dotDotDot)),
            ParameterToParameterDeclarationName(GetSymbolOfDeclaration(p), p, context),
            IsOptionalParameter(p) ? SToken(SyntaxKind.QuestionToken) : Node.None,
            TypeFromParameter(p, SymbolRef.None, context)); // Ignore private param props, since this type is going straight back into a param
        // factory.updateParameterDeclaration: the updated node keeps the original
        return SetTextRange(context, parameter, p);
    }

    // reuseTypeParameters
    private NodeList ReuseTypeParameters(NodeRef owner, NodeList typeParameters, NodeBuilderContext context)
    {
        if (typeParameters.IsNone)
        {
            return NodeList.None;
        }
        var store = StoreOf(owner);
        var result = new List<Node>();
        foreach (var tp in store.Nodes(typeParameters))
        {
            var tpRef = At(owner, tp);
            var (_, tpName) = TrackExistingEntityName(context, At(owner, store.Name(tp)));
            var modifiers = new List<Node>();
            foreach (var m in store.Nodes(store.Modifiers(tp)))
            {
                modifiers.Add(SetTextRange(context, SToken(store.Kind(m)), At(owner, m)));
            }
            var declaration = STypeParameterDeclaration(
                modifiers.Count > 0 ? SList(modifiers) : (store.Modifiers(tp).IsNone ? NodeList.None : SList(modifiers)),
                tpName,
                SerializeExistingTypeNodeWithFallback(At(owner, store.Constraint(tp)), context),
                SerializeExistingTypeNodeWithFallback(At(owner, store.Default(tp)), context));
            result.Add(SetTextRange(context, declaration, tpRef));
        }
        return SList(result);
    }

    private Node CreateUndefinedTypeNode() => strictNullChecks ? SKeyword(SyntaxKind.UndefinedKeyword) : SKeyword(SyntaxKind.AnyKeyword);

    // typeFromPrimitiveLiteral
    private SyntacticResult TypeFromPrimitiveLiteral(NodeRef node, SyntaxKind baseType, NodeBuilderContext context, bool preserveLiterals, bool requiresAddingUndefined)
    {
        Node result;
        if (preserveLiterals)
        {
            Node literal;
            var store = StoreOf(node);
            if (store.Kind(node.Node) == SyntaxKind.NoSubstitutionTemplateLiteral)
            {
                // factory.createStringLiteral(node.text): a new node
                literal = SStringLiteral(store.Texts.GetString(store.Text(node.Node)) ?? "");
            }
            else
            {
                literal = SetTextRange(context, CopyExistingNode(node), node);
            }
            result = SLiteralType(literal);
        }
        else
        {
            result = SKeyword(baseType);
        }
        return Result(AddUndefinedIfNeeded(result, requiresAddingUndefined, node, context));
    }

    // addUndefinedIfNeeded
    private Node AddUndefinedIfNeeded(Node node, bool addUndefined, NodeRef owner, NodeBuilderContext context)
    {
        _ = context;
        var optionalDeclaration = false;
        if (!owner.IsNone)
        {
            var store = StoreOf(owner);
            var parentDeclaration = WalkUpParenthesizedExpressionsParent(store, owner.Node);
            optionalDeclaration = !parentDeclaration.IsNone && Utilities.IsDeclaration(store, parentDeclaration) && IsOptionalDeclaration(At(owner, parentDeclaration));
        }
        if (!strictNullChecks || !(addUndefined || optionalDeclaration))
        {
            return node;
        }
        if (S.Kind(node) == SyntaxKind.UnionType)
        {
            var types = new List<Node>(S.Nodes(S.Types(node)).ToArray());
            types.Add(SKeyword(SyntaxKind.UndefinedKeyword));
            return SUnionType(types);
        }
        return SUnionType([node, SKeyword(SyntaxKind.UndefinedKeyword)]);
    }

    // utilities.ts isOptionalDeclaration
    private bool IsOptionalDeclaration(NodeRef declaration)
    {
        var store = StoreOf(declaration);
        if (store.Kind(declaration.Node) is SyntaxKind.JSDocPropertyTag or SyntaxKind.JSDocParameterTag)
        {
            return IsOptionalJSDocPropertyLikeTag(store, declaration.Node);
        }
        if (store.Kind(declaration.Node) == SyntaxKind.Parameter && IsJSDocOptionalParameter(store, declaration.Node))
        {
            return true;
        }
        return store.Kind(declaration.Node) is SyntaxKind.Parameter or SyntaxKind.PropertySignature or SyntaxKind.PropertyDeclaration or SyntaxKind.MethodDeclaration or SyntaxKind.MethodSignature or SyntaxKind.PropertyAssignment or SyntaxKind.ShorthandPropertyAssignment
            && !store.QuestionToken(declaration.Node).IsNone;
    }

    // canAddUndefined for an existing type node
    private bool CanAddUndefinedExisting(NodeRef node)
    {
        if (!strictNullChecks)
        {
            return true;
        }
        var store = StoreOf(node);
        var kind = store.Kind(node.Node);
        if (Utilities.IsKeyword(kind) || kind is SyntaxKind.LiteralType or SyntaxKind.FunctionType or SyntaxKind.ConstructorType or SyntaxKind.ArrayType or SyntaxKind.TupleType or SyntaxKind.TypeLiteral or SyntaxKind.TemplateLiteralType or SyntaxKind.ThisType)
        {
            return true;
        }
        if (kind == SyntaxKind.ParenthesizedType)
        {
            return CanAddUndefinedExisting(At(node, store.Type(node.Node)));
        }
        if (kind is SyntaxKind.UnionType or SyntaxKind.IntersectionType)
        {
            foreach (var t in store.Nodes(store.Types(node.Node)))
            {
                if (!CanAddUndefinedExisting(At(node, t)))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    // createReturnFromSignature
    private Node CreateReturnFromSignature(NodeRef fn, SymbolRef symbol, NodeBuilderContext context)
    {
        var returnType = SyntacticResult.Failed;
        var store = StoreOf(fn);
        var returnTypeNode = Utilities.IsJSDocConstructSignature(store, fn.Node) ? GetEffectiveTypeAnnotationNode(At(fn, store.Element(store.Parameters(fn.Node), 0)))
            : At(fn, GetEffectiveReturnTypeNode(store, fn.Node));
        if (!returnTypeNode.IsNone)
        {
            returnType = Result(SerializeTypeAnnotationOfDeclaration(returnTypeNode, context, fn, symbol));
        }
        else if (IsValueSignatureDeclaration(store, fn.Node))
        {
            returnType = TypeFromSingleReturnExpression(fn, context);
        }
        return !returnType.Type.IsNone ? returnType.Type : InferReturnTypeOfSignatureSignature(fn, context, symbol);
    }

    // utilities.ts isValueSignatureDeclaration
    private static bool IsValueSignatureDeclaration(NodeStore store, Node node) =>
        store.Kind(node) is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction or SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor or SyntaxKind.FunctionDeclaration or SyntaxKind.Constructor;

    // typeFromSingleReturnExpression
    private SyntacticResult TypeFromSingleReturnExpression(NodeRef declaration, NodeBuilderContext context)
    {
        var candidateExpr = Node.None;
        var store = StoreOf(declaration);
        var body = store.Body(declaration.Node);
        if (!declaration.IsNone && !Utilities.NodeIsMissing(store, body))
        {
            var flags = GetFunctionFlagsOf(store, declaration.Node);
            if ((flags & FunctionFlags.AsyncGenerator) != 0)
            {
                return SyntacticResult.Failed;
            }
            if (store.Kind(body) == SyntaxKind.Block)
            {
                var done = false;
                ForEachReturnStatementInBody(store, body, s =>
                {
                    if (done)
                    {
                        return;
                    }
                    if (store.Parent(s) != body)
                    {
                        candidateExpr = Node.None;
                        done = true;
                        return;
                    }
                    if (candidateExpr.IsNone)
                    {
                        candidateExpr = store.Expression(s);
                        if (candidateExpr.IsNone)
                        {
                            // `return;`: forEachReturnStatement continues; a later return makes the candidate undefined again
                        }
                    }
                    else
                    {
                        candidateExpr = Node.None;
                        done = true;
                    }
                });
            }
            else
            {
                candidateExpr = body;
            }
        }
        if (!candidateExpr.IsNone)
        {
            var candidate = At(declaration, candidateExpr);
            if (IsContextuallyTyped(candidate))
            {
                var kind = store.Kind(candidateExpr);
                var type = FactoryUtilities.IsJSDocTypeAssertion(store, candidateExpr) ? GetJSDocTypeAssertionType(candidate)
                    : kind is SyntaxKind.AsExpression or SyntaxKind.TypeAssertionExpression ? At(declaration, store.Type(candidateExpr)) : NodeRef.None;
                if (!type.IsNone && !IsConstTypeReference(store, type.Node))
                {
                    return Result(SerializeExistingTypeNodeSyntactic(type, context));
                }
            }
            else
            {
                return TypeFromExpression(candidate, context);
            }
        }
        return SyntacticResult.Failed;
    }

    // isContextuallyTyped: function calls or parent type annotations (but not the return type of a function expression)
    // may impact the inferred type and local inference is unreliable
    private bool IsContextuallyTyped(NodeRef node)
    {
        var store = StoreOf(node);
        for (var n = store.Parent(node.Node); !n.IsNone; n = store.Parent(n))
        {
            var kind = store.Kind(n);
            if (kind is SyntaxKind.CallExpression or SyntaxKind.JsxElement or SyntaxKind.JsxExpression)
            {
                return true;
            }
            if (!Utilities.IsFunctionLikeDeclaration(store, n) && !GetEffectiveTypeAnnotationNodeRef(At(node, n)).IsNone)
            {
                return true;
            }
        }
        return false;
    }

    // utilities.ts getEffectiveTypeAnnotationNode
    private NodeRef GetEffectiveTypeAnnotationNodeRef(NodeRef node)
    {
        var store = StoreOf(node);
        var kind = store.Kind(node.Node);
        if (Utilities.IsInJSFile(store, node.Node))
        {
            return GetEffectiveTypeAnnotationNode(node);
        }
        if (kind is SyntaxKind.Parameter or SyntaxKind.PropertySignature or SyntaxKind.PropertyDeclaration or SyntaxKind.VariableDeclaration or SyntaxKind.BindingElement or SyntaxKind.TypeAliasDeclaration or
            SyntaxKind.AsExpression or SyntaxKind.TypeAssertionExpression or SyntaxKind.SatisfiesExpression or SyntaxKind.IndexSignature or SyntaxKind.PropertyAssignment)
        {
            if (kind == SyntaxKind.TypeAliasDeclaration)
            {
                return NodeRef.None;
            }
            var type = kind == SyntaxKind.PropertyAssignment ? Node.None : store.Type(node.Node);
            return type.IsNone ? NodeRef.None : At(node, type);
        }
        if (Utilities.IsFunctionLike(store, node.Node))
        {
            // the return type annotation of a signature is not a type annotation of the node itself
            return NodeRef.None;
        }
        return NodeRef.None;
    }

    // checker.ts getNonlocalEffectiveTypeAnnotationNode
    private NodeRef GetNonlocalEffectiveTypeAnnotationNode(NodeRef node) => GetEffectiveTypeAnnotationNodeRef(node);

    // ---- the resolver's serialize functions ----

    // the resolver's serializeTypeOfDeclaration
    private Node ResolverSerializeTypeOfDeclaration(NodeBuilderContext context, NodeRef declaration, SymbolRef symbol)
    {
        // Get type of the symbol if this is the valid symbol otherwise get type at location
        if (symbol.IsNone)
        {
            symbol = GetSymbolOfDeclaration(declaration);
        }
        if (!context.EnclosingSymbolTypes.TryGetValue(symbol, out var type))
        {
            type = (SymbolFlagsOf(symbol) & SymbolFlags.Accessor) != 0 && Kind(declaration) == SyntaxKind.SetAccessor ? InstantiateTypeWithMapper(GetWriteTypeOfSymbol(symbol), context.Mapper) :
                !symbol.IsNone && (SymbolFlagsOf(symbol) & (SymbolFlags.TypeLiteral | SymbolFlags.Signature)) == 0
                ? InstantiateTypeWithMapper(GetWidenedLiteralType(GetTypeOfSymbol(symbol)), context.Mapper)
                : errorType;
        }
        var addUndefinedForParameter = !declaration.IsNone && Kind(declaration) is SyntaxKind.Parameter or SyntaxKind.JSDocParameterTag && RequiresAddingImplicitUndefined(declaration, context.EnclosingDeclaration);
        if (addUndefinedForParameter)
        {
            type = GetOptionalType(type);
        }
        return SerializeInferredTypeForDeclaration(symbol, context, type);
    }

    // the resolver's serializeReturnTypeForSignature
    private Node ResolverSerializeReturnTypeForSignature(NodeBuilderContext context, NodeRef signatureDeclaration, SymbolRef symbol)
    {
        var signature = GetSignatureFromDeclaration(signatureDeclaration);
        if (symbol.IsNone)
        {
            symbol = GetSymbolOfDeclaration(signatureDeclaration);
        }
        if (!context.EnclosingSymbolTypes.TryGetValue(symbol, out var returnType))
        {
            returnType = InstantiateTypeWithMapper(GetReturnTypeOfSignature(signature), context.Mapper);
        }
        return SerializeInferredReturnTypeForSignature(context, signature, returnType);
    }

    // the resolver's serializeTypeOfExpression
    private Node ResolverSerializeTypeOfExpression(NodeBuilderContext context, NodeRef expr)
    {
        var type = InstantiateTypeWithMapper(GetWidenedType(GetRegularTypeOfExpression(expr)), context.Mapper);
        return TypeToTypeNodeHelper(type, context);
    }

    // the resolver's canReuseTypeNodeAnnotation
    private bool CanReuseTypeNodeAnnotation(NodeBuilderContext context, NodeRef node, NodeRef existing, SymbolRef symbol, bool requiresAddingUndefined)
    {
        if (context.EnclosingDeclaration.IsNone && context.FakeScope is null)
        {
            return false;
        }
        if (symbol.IsNone)
        {
            symbol = GetSymbolOfDeclaration(node);
        }
        if (!context.EnclosingSymbolTypes.TryGetValue(symbol, out var type))
        {
            if ((SymbolFlagsOf(symbol) & SymbolFlags.Accessor) != 0)
            {
                type = Kind(node) == SyntaxKind.SetAccessor ? GetWriteTypeOfSymbol(symbol) : GetTypeOfAccessorsForBuilder(symbol);
            }
            else if (IsValueSignatureDeclaration(StoreOf(node), node.Node))
            {
                type = GetReturnTypeOfSignature(GetSignatureFromDeclaration(node));
            }
            else
            {
                type = GetTypeOfSymbol(symbol);
            }
        }
        var annotationType = GetTypeFromTypeNode(existing);
        if (IsErrorType(annotationType))
        {
            // allow "reusing" type nodes that resolve to error types
            // those can't truly be reused but it prevents cascading errors in isolatedDeclarations
            // for source with errors there is no guarantee to emit correct code anyway
            return true;
        }
        if (requiresAddingUndefined && !annotationType.IsNone)
        {
            annotationType = strictNullChecks ? GetOptionalType(annotationType, Kind(node) != SyntaxKind.Parameter) : annotationType;
        }
        return !annotationType.IsNone && TypeNodeIsEquivalentToType(node, type, annotationType) && ExistingTypeNodeIsNotReferenceOrIsReferenceWithCompatibleTypeArgumentCount(existing, type);
    }

    // the resolver's requiresAddingImplicitUndefined
    private bool ResolverRequiresAddingImplicitUndefined(NodeRef declaration, SymbolRef symbol, NodeRef enclosingDeclaration)
    {
        switch (Kind(declaration))
        {
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.JSDocPropertyTag:
            {
                if (symbol.IsNone)
                {
                    symbol = GetSymbolOfDeclaration(declaration);
                }
                var type = GetTypeOfSymbol(symbol);
                return (SymbolFlagsOf(symbol) & SymbolFlags.Property) != 0 && (SymbolFlagsOf(symbol) & SymbolFlags.Optional) != 0 && IsOptionalDeclaration(declaration) &&
                    IsMappedSymbol(symbol) && ContainsNonMissingUndefinedType(type);
            }
            case SyntaxKind.Parameter:
            case SyntaxKind.JSDocParameterTag:
                return RequiresAddingImplicitUndefined(declaration, enclosingDeclaration);
            default:
                return false;
        }
    }

    // checker.ts requiresAddingImplicitUndefined
    private bool RequiresAddingImplicitUndefined(NodeRef parameter, NodeRef enclosingDeclaration) =>
        (IsRequiredInitializedParameter(parameter, enclosingDeclaration) || IsOptionalUninitializedParameterProperty(parameter)) && !DeclaredParameterTypeContainsUndefined(parameter);

    // checker.ts isRequiredInitializedParameter
    private bool IsRequiredInitializedParameter(NodeRef parameter, NodeRef enclosingDeclaration)
    {
        var store = StoreOf(parameter);
        if (!strictNullChecks || IsOptionalParameter(parameter) || store.Kind(parameter.Node) == SyntaxKind.JSDocParameterTag || store.Initializer(parameter.Node).IsNone)
        {
            return false;
        }
        if (Utilities.HasSyntacticModifierUncached(store, parameter.Node, ModifierFlags.ParameterPropertyModifier))
        {
            return !enclosingDeclaration.IsNone && Utilities.IsFunctionLikeDeclaration(StoreOf(enclosingDeclaration), enclosingDeclaration.Node);
        }
        return true;
    }

    // checker.ts isOptionalUninitializedParameterProperty
    private bool IsOptionalUninitializedParameterProperty(NodeRef parameter)
    {
        var store = StoreOf(parameter);
        return strictNullChecks && IsOptionalParameter(parameter) && (store.Kind(parameter.Node) == SyntaxKind.JSDocParameterTag || store.Initializer(parameter.Node).IsNone) &&
            Utilities.HasSyntacticModifierUncached(store, parameter.Node, ModifierFlags.ParameterPropertyModifier);
    }

    // checker.ts declaredParameterTypeContainsUndefined
    private bool DeclaredParameterTypeContainsUndefined(NodeRef parameter)
    {
        var typeNode = GetNonlocalEffectiveTypeAnnotationNode(parameter);
        if (typeNode.IsNone)
        {
            return false;
        }
        var type = GetTypeFromTypeNode(typeNode);
        return IsErrorType(type) || SomeType(type, t => (Types.Flags(t) & TypeFlags.Undefined) != 0);
    }

    // checker.ts isOptionalParameter (immediately invoked function expressions: C5)
    private bool IsOptionalParameter(NodeRef node)
    {
        var store = StoreOf(node);
        if (HasEffectiveQuestionToken(node))
        {
            return true;
        }
        if (store.Kind(node.Node) == SyntaxKind.Parameter && !store.Initializer(node.Node).IsNone)
        {
            var parent = At(node, store.Parent(node.Node));
            var signature = GetSignatureFromDeclaration(parent);
            var parameterIndex = 0;
            foreach (var p in store.Nodes(store.Parameters(parent.Node)))
            {
                if (p == node.Node)
                {
                    break;
                }
                parameterIndex++;
            }
            return parameterIndex >= GetMinArgumentCountForBuilder(signature);
        }
        if (store.Kind(node.Node) == SyntaxKind.Parameter)
        {
            var func = store.Parent(node.Node);
            var iife = Utilities.GetImmediatelyInvokedFunctionExpression(store, func);
            if (!iife.IsNone)
            {
                var parameterIndex = 0;
                foreach (var p in store.Nodes(store.Parameters(func)))
                {
                    if (p == node.Node)
                    {
                        break;
                    }
                    parameterIndex++;
                }
                return store.Type(node.Node).IsNone && store.DotDotDotToken(node.Node).IsNone && parameterIndex >= GetEffectiveCallArguments(At(node, iife)).Count;
            }
        }
        return false;
    }
}
