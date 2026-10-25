// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (elaborateError and the elaboration of assignability failures)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/relater.go (elaborateError and the elaboration functions)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C4. The elaboration iterators are C# iterators (lazily evaluated
// like TypeScript's generators). TODO(C5): the contextual typing of the re-checked expressions
// (checkExpressionForMutableLocationWithContextualType, the tuple re-check of array literals); TODO(C9): JSX elaboration.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    // checker.ts ElaborationIterator's elements
    private readonly record struct ElaborationElement(NodeRef ErrorNode, NodeRef InnerExpression, Type NameType, DiagnosticMessage? ErrorMessage = null);

    // checker.ts isOrHasGenericConditional
    private bool IsOrHasGenericConditional(Type type) =>
        (Types.Flags(type) & TypeFlags.Conditional) != 0 ||
        ((Types.Flags(type) & TypeFlags.Intersection) != 0 && ExistsType(Types.Elements(Types.Union(type).Types), IsOrHasGenericConditional));

    // checker.ts elaborateError
    private bool ElaborateError(NodeRef node, Type source, Type target, Relation relation, DiagnosticMessage? headMessage, Func<DiagnosticMessageChain?>? containingMessageChain, ErrorOutputContainer? errorOutputContainer)
    {
        if (node.IsNone || IsOrHasGenericConditional(target))
        {
            return false;
        }
        if (!CheckTypeRelatedTo(source, target, relation, NodeRef.None) &&
            ElaborateDidYouMeanToCallOrConstruct(node, source, target, relation, headMessage, containingMessageChain, errorOutputContainer))
        {
            return true;
        }
        var store = StoreOf(node);
        switch (store.Kind(node.Node))
        {
            case SyntaxKind.AsExpression:
                if (!Utilities.IsConstAssertion(store, node.Node))
                {
                    break;
                }
                goto case SyntaxKind.ParenthesizedExpression;
            case SyntaxKind.JsxExpression:
            case SyntaxKind.ParenthesizedExpression:
                return ElaborateError(At(node, store.Expression(node.Node)), source, target, relation, headMessage, containingMessageChain, errorOutputContainer);
            case SyntaxKind.BinaryExpression:
                switch (store.Kind(store.OperatorToken(node.Node)))
                {
                    case SyntaxKind.EqualsToken:
                    case SyntaxKind.CommaToken:
                        return ElaborateError(At(node, store.Right(node.Node)), source, target, relation, headMessage, containingMessageChain, errorOutputContainer);
                }
                break;
            case SyntaxKind.ObjectLiteralExpression:
                return ElaborateObjectLiteral(node, source, target, relation, containingMessageChain, errorOutputContainer);
            case SyntaxKind.ArrayLiteralExpression:
                return ElaborateArrayLiteral(node, source, target, relation, containingMessageChain, errorOutputContainer);
            case SyntaxKind.JsxAttributes:
                // TODO(C9): elaborateJsxComponents
                return false;
            case SyntaxKind.ArrowFunction:
                return ElaborateArrowFunction(node, source, target, relation, containingMessageChain, errorOutputContainer);
        }
        return false;
    }

    // checker.ts elaborateDidYouMeanToCallOrConstruct
    private bool ElaborateDidYouMeanToCallOrConstruct(NodeRef node, Type source, Type target, Relation relation, DiagnosticMessage? headMessage,
        Func<DiagnosticMessageChain?>? containingMessageChain, ErrorOutputContainer? errorOutputContainer)
    {
        var callSignatures = GetSignaturesOfType(source, SignatureKind.Call);
        var constructSignatures = GetSignaturesOfType(source, SignatureKind.Construct);
        foreach (var signatures in new[] { constructSignatures, callSignatures })
        {
            if (signatures.Exists(s =>
                {
                    var returnType = GetReturnTypeOfSignature(s);
                    return (Types.Flags(returnType) & (TypeFlags.Any | TypeFlags.Never)) == 0 && CheckTypeRelatedTo(returnType, target, relation, NodeRef.None);
                }))
            {
                var resultObj = errorOutputContainer ?? new ErrorOutputContainer();
                CheckTypeAssignableTo(source, target, node, headMessage, containingMessageChain, resultObj);
                var diagnostic = resultObj.Errors![^1];
                AddRelatedInfo(diagnostic, CreateRelatedForNode(node,
                    signatures == constructSignatures ? Diagnostics.Did_you_mean_to_use_new_with_this_expression : Diagnostics.Did_you_mean_to_call_this_expression));
                return true;
            }
        }
        return false;
    }

    // checker.ts elaborateArrowFunction
    private bool ElaborateArrowFunction(NodeRef node, Type source, Type target, Relation relation, Func<DiagnosticMessageChain?>? containingMessageChain, ErrorOutputContainer? errorOutputContainer)
    {
        var store = StoreOf(node);
        var body = store.Body(node.Node);
        // Don't elaborate blocks
        if (store.Kind(body) == SyntaxKind.Block)
        {
            return false;
        }
        // Or functions with annotated parameter types
        foreach (var p in store.Nodes(store.Parameters(node.Node)))
        {
            if (!store.Type(p).IsNone)
            {
                return false;
            }
        }
        var sourceSig = GetSingleCallSignature(source);
        if (sourceSig.IsNone)
        {
            return false;
        }
        var targetSignatures = GetSignaturesOfType(target, SignatureKind.Call);
        if (targetSignatures.Count == 0)
        {
            return false;
        }
        var returnExpression = At(node, body);
        var sourceReturn = GetReturnTypeOfSignature(sourceSig);
        var targetReturn = GetUnionType(targetSignatures.ConvertAll(GetReturnTypeOfSignature).ToArray());
        if (!CheckTypeRelatedTo(sourceReturn, targetReturn, relation, NodeRef.None))
        {
            var elaborated = !returnExpression.IsNone && ElaborateError(returnExpression, sourceReturn, targetReturn, relation, null, containingMessageChain, errorOutputContainer);
            if (elaborated)
            {
                return elaborated;
            }
            var resultObj = errorOutputContainer ?? new ErrorOutputContainer();
            CheckTypeRelatedTo(sourceReturn, targetReturn, relation, returnExpression, null, containingMessageChain, resultObj);
            if (resultObj.Errors is { Count: > 0 } errors)
            {
                var targetSymbol = Types.Symbol(target);
                if (!targetSymbol.IsNone && DeclarationsOf(targetSymbol) is { Count: > 0 } targetDeclarations)
                {
                    AddRelatedInfo(errors[^1], CreateRelatedForNode(targetDeclarations[0], Diagnostics.The_expected_type_comes_from_the_return_type_of_this_signature));
                }
                if ((GetFunctionFlagsOf(store, node.Node) & FunctionFlags.Async) == 0
                    // exclude cases where source itself is promisy - this way we don't make a suggestion when relating
                    // an IPromise and a Promise that are slightly different
                    && GetTypeOfPropertyOfType(sourceReturn, "then").IsNone
                    && CheckTypeRelatedTo(CreatePromiseType(sourceReturn), targetReturn, relation, NodeRef.None))
                {
                    AddRelatedInfo(errors[^1], CreateRelatedForNode(node, Diagnostics.Did_you_mean_to_mark_this_function_as_async));
                }
                return true;
            }
        }
        return false;
    }

    // checker.ts getBestMatchIndexedAccessTypeOrUndefined
    private Type GetBestMatchIndexedAccessTypeOrUndefined(Type source, Type target, Type nameType)
    {
        var idx = GetIndexedAccessTypeOrUndefined(target, nameType);
        if (!idx.IsNone)
        {
            return idx;
        }
        if ((Types.Flags(target) & TypeFlags.Union) != 0)
        {
            var best = GetBestMatchingType(source, target);
            if (!best.IsNone)
            {
                return GetIndexedAccessTypeOrUndefined(best, nameType);
            }
        }
        return Type.None;
    }

    // checker.ts checkExpressionForMutableLocationWithContextualType (TODO(C5): pushContextualType(next, sourcePropType))
    private Type CheckExpressionForMutableLocationWithContextualType(NodeRef next, Type sourcePropType) => CheckExpressionForMutableLocation(next);

    // checker.ts elaborateElementwise: for every element, checks that element to issue an error on a property of that
    // element's type. If that element would issue an error, we first attempt to dive into that element's inner
    // expression and issue a more specific error by recursing into `elaborateError`. Otherwise, we issue an error on
    // _every_ element which fails the assignability check.
    private bool ElaborateElementwise(IEnumerable<ElaborationElement> iterator, Type source, Type target, Relation relation, Func<DiagnosticMessageChain?>? containingMessageChain, ErrorOutputContainer? errorOutputContainer)
    {
        // Assignability failure - check each prop individually, and if that fails, fall back on the bad error span
        var reportedError = false;
        foreach (var value in iterator)
        {
            var (prop, next, nameType, errorMessage) = value;
            var targetPropType = GetBestMatchIndexedAccessTypeOrUndefined(source, target, nameType);
            if (targetPropType.IsNone || (Types.Flags(targetPropType) & TypeFlags.IndexedAccess) != 0)
            {
                continue; // Don't elaborate on indexes on generic variables
            }
            var sourcePropType = GetIndexedAccessTypeOrUndefined(source, nameType);
            if (sourcePropType.IsNone)
            {
                continue;
            }
            var propName = GetPropertyNameFromIndex(nameType, NodeRef.None);
            if (!CheckTypeRelatedTo(sourcePropType, targetPropType, relation, NodeRef.None))
            {
                var elaborated = !next.IsNone && ElaborateError(next, sourcePropType, targetPropType, relation, null, containingMessageChain, errorOutputContainer);
                reportedError = true;
                if (!elaborated)
                {
                    // Issue error on the prop itself, since the prop couldn't elaborate the error
                    var resultObj = errorOutputContainer ?? new ErrorOutputContainer();
                    // Use the expression type, if available
                    var specificSource = !next.IsNone ? CheckExpressionForMutableLocationWithContextualType(next, sourcePropType) : sourcePropType;
                    if (exactOptionalPropertyTypes && IsExactOptionalPropertyMismatch(specificSource, targetPropType))
                    {
                        var diag = CreateDiagnosticForNode(prop, Diagnostics.Type_0_is_not_assignable_to_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_type_of_the_target,
                            TypeToString(specificSource), TypeToString(targetPropType));
                        diagnostics.Add(diag);
                        resultObj.Errors = [diag];
                    }
                    else
                    {
                        var targetIsOptional = propName is not null && (SymbolFlagsOf(OrUnknownSymbol(GetPropertyOfType(target, propName))) & SymbolFlags.Optional) != 0;
                        var sourceIsOptional = propName is not null && (SymbolFlagsOf(OrUnknownSymbol(GetPropertyOfType(source, propName))) & SymbolFlags.Optional) != 0;
                        targetPropType = RemoveMissingType(targetPropType, targetIsOptional);
                        sourcePropType = RemoveMissingType(sourcePropType, targetIsOptional && sourceIsOptional);
                        var result = CheckTypeRelatedTo(specificSource, targetPropType, relation, prop, errorMessage, containingMessageChain, resultObj);
                        if (result && specificSource != sourcePropType)
                        {
                            // If for whatever reason the expression type doesn't yield an error, make sure we still issue an error on the sourcePropType
                            CheckTypeRelatedTo(sourcePropType, targetPropType, relation, prop, errorMessage, containingMessageChain, resultObj);
                        }
                    }
                    if (resultObj.Errors is { Count: > 0 } errors)
                    {
                        var reportedDiag = errors[^1];
                        var propertyName = IsTypeUsableAsPropertyName(nameType) ? GetPropertyNameFromType(nameType) : null;
                        var targetProp = propertyName is not null ? GetPropertyOfType(target, propertyName) : SymbolRef.None;
                        var issuedElaboration = false;
                        if (targetProp.IsNone)
                        {
                            var indexInfo = GetApplicableIndexInfo(target, nameType);
                            if (indexInfo is not null && !indexInfo.Declaration.IsNone && !program.IsSourceFileDefaultLibrary(FileAt(indexInfo.Declaration.File)))
                            {
                                issuedElaboration = true;
                                AddRelatedInfo(reportedDiag, CreateRelatedForNode(indexInfo.Declaration, Diagnostics.The_expected_type_comes_from_this_index_signature));
                            }
                        }
                        var targetSymbol = Types.Symbol(target);
                        var targetPropDeclarations = targetProp.IsNone ? null : DeclarationsOf(targetProp);
                        var targetSymbolDeclarations = targetSymbol.IsNone ? null : DeclarationsOf(targetSymbol);
                        if (!issuedElaboration && (targetPropDeclarations is { Count: > 0 } || targetSymbolDeclarations is { Count: > 0 }))
                        {
                            var targetNode = targetPropDeclarations is { Count: > 0 } ? targetPropDeclarations[0] : targetSymbolDeclarations![0];
                            if (!program.IsSourceFileDefaultLibrary(FileAt(targetNode.File)))
                            {
                                AddRelatedInfo(reportedDiag, CreateRelatedForNode(targetNode, Diagnostics.The_expected_type_comes_from_property_0_which_is_declared_here_on_type_1,
                                    propertyName is not null && (Types.Flags(nameType) & TypeFlags.UniqueESSymbol) == 0 ? Utilities.UnescapeLeadingUnderscores(propertyName) : TypeToString(nameType),
                                    TypeToString(target)));
                            }
                        }
                    }
                }
            }
        }
        return reportedError;
    }

    private SymbolRef OrUnknownSymbol(SymbolRef symbol) => symbol.IsNone ? unknownSymbol : symbol;

    // checker.ts generateLimitedTupleElements
    private IEnumerable<ElaborationElement> GenerateLimitedTupleElements(NodeRef node, Type target)
    {
        var store = StoreOf(node);
        var elements = store.Nodes(store.Elements(node.Node)).ToArray();
        for (var i = 0; i < elements.Length; i++)
        {
            // Skip elements which do not exist in the target - a length error on the tuple overall is likely better than an error on a mismatched index signature
            if (IsTupleLikeType(target) && GetPropertyOfType(target, i.ToString(System.Globalization.CultureInfo.InvariantCulture)).IsNone)
            {
                continue;
            }
            var elem = elements[i];
            if (store.Kind(elem) == SyntaxKind.OmittedExpression)
            {
                continue;
            }
            var nameType = GetNumberLiteralType(i);
            var checkNode = GetEffectiveCheckNode(At(node, elem));
            yield return new ElaborationElement(checkNode, checkNode, nameType);
        }
    }

    // checker.ts getEffectiveCheckNode
    private NodeRef GetEffectiveCheckNode(NodeRef argument)
    {
        var store = StoreOf(argument);
        var flags = Utilities.IsInJSFile(store, argument.Node)
            ? OuterExpressionKinds.Parentheses | OuterExpressionKinds.Satisfies | OuterExpressionKinds.ExcludeJSDocTypeAssertion
            : OuterExpressionKinds.Parentheses | OuterExpressionKinds.Satisfies;
        return At(argument, FactoryUtilities.SkipOuterExpressions(store, argument.Node, flags));
    }

    // checker.ts elaborateArrayLiteral
    private bool ElaborateArrayLiteral(NodeRef node, Type source, Type target, Relation relation, Func<DiagnosticMessageChain?>? containingMessageChain, ErrorOutputContainer? errorOutputContainer)
    {
        if ((Types.Flags(target) & (TypeFlags.Primitive | TypeFlags.Never)) != 0)
        {
            return false;
        }
        if (IsTupleLikeType(source))
        {
            return ElaborateElementwise(GenerateLimitedTupleElements(node, target), source, target, relation, containingMessageChain, errorOutputContainer);
        }
        // TODO(C5): recreate a tuple from the elements (checkArrayLiteral(node, CheckMode.Contextual, forceTuple) with the
        // target as contextual type) and elaborate elementwise when it is tuple-like.
        return false;
    }

    // checker.ts generateObjectLiteralElements
    private IEnumerable<ElaborationElement> GenerateObjectLiteralElements(NodeRef node)
    {
        var store = StoreOf(node);
        foreach (var prop in store.Nodes(store.Properties(node.Node)).ToArray())
        {
            var kind = store.Kind(prop);
            if (kind == SyntaxKind.SpreadAssignment)
            {
                continue;
            }
            var type = GetLiteralTypeFromProperty(GetSymbolOfDeclaration(At(node, prop)), TypeFlags.StringOrNumberLiteralOrUnique);
            if (type.IsNone || (Types.Flags(type) & TypeFlags.Never) != 0)
            {
                continue;
            }
            var name = At(node, store.Name(prop));
            switch (kind)
            {
                case SyntaxKind.SetAccessor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.ShorthandPropertyAssignment:
                    yield return new ElaborationElement(name, NodeRef.None, type);
                    break;
                case SyntaxKind.PropertyAssignment:
                    yield return new ElaborationElement(name, At(node, store.Initializer(prop)), type,
                        IsComputedNonLiteralName(name) ? Diagnostics.Type_of_computed_property_s_value_is_0_which_is_not_assignable_to_type_1 : null);
                    break;
            }
        }
    }

    // utilities.ts isComputedNonLiteralName
    private bool IsComputedNonLiteralName(NodeRef name)
    {
        var store = StoreOf(name);
        return store.Kind(name.Node) == SyntaxKind.ComputedPropertyName && !Utilities.IsStringOrNumericLiteralLike(store, store.Expression(name.Node));
    }

    // checker.ts elaborateObjectLiteral
    private bool ElaborateObjectLiteral(NodeRef node, Type source, Type target, Relation relation, Func<DiagnosticMessageChain?>? containingMessageChain, ErrorOutputContainer? errorOutputContainer)
    {
        if ((Types.Flags(target) & (TypeFlags.Primitive | TypeFlags.Never)) != 0)
        {
            return false;
        }
        return ElaborateElementwise(GenerateObjectLiteralElements(node), source, target, relation, containingMessageChain, errorOutputContainer);
    }
}
