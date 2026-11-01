// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/checker.ts (checkTypeRelatedTo and all its nested functions)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/checker/relater.go (Relater)
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module C4. The closure's variables are the fields of a Relater object
// created per checkTypeRelatedTo call (isRelatedTo, recursiveTypeRelatedTo, structuredTypeRelatedTo(Worker),
// propertiesRelatedTo, signaturesRelatedTo, indexSignaturesRelatedTo, typeArgumentsRelatedTo, the union and
// intersection relations, hasExcessProperties, reportRelationError, reportIncompatibleStack, ...).

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Checker
{
    private bool StrictFunctionTypes => compilerOptions.GetStrictOptionValue(compilerOptions.StrictFunctionTypes);

    /// <summary>
    /// checker.ts checkTypeRelatedTo: whether source is related to target (e.g. assignable to); reports the error with
    /// its elaboration on errorNode when one is given.
    /// </summary>
    internal bool CheckTypeRelatedTo(Type source, Type target, Relation relation, NodeRef errorNode, DiagnosticMessage? headMessage = null,
        Func<DiagnosticMessageChain?>? containingMessageChain = null, ErrorOutputContainer? errorOutputContainer = null) =>
        new Relater(this, relation, errorNode, headMessage).Check(source, target, containingMessageChain, errorOutputContainer);

    // (the error state captured by captureErrorCalculationState)
    private readonly record struct ErrorCalculationState(DiagnosticMessageChain? ErrorInfo, (Type, Type)? LastSkippedInfo, List<(DiagnosticMessage, object?[])>? IncompatibleStack,
        int OverrideNextErrorInfo, int SkipParentCounter, List<ProgramRelatedInformation>? RelatedInfo);

    private sealed class Relater(Checker checker, Relation relationOfCheck, NodeRef errorNode, DiagnosticMessage? headMessage)
    {
        private readonly Checker c = checker;
        private readonly Relation relation = relationOfCheck;
        private NodeRef errorNode = errorNode;
        private DiagnosticMessageChain? errorInfo;
        private List<ProgramRelatedInformation>? relatedInfo;
        private List<RelationKey>? maybeKeys;
        private HashSet<RelationKey>? maybeKeysSet;
        // (module CO5: the stacks are created with maybeKeys, as before; until then both are a shared empty list that
        // nothing writes to, read only up to the depths, which stay 0)
        private List<Type> sourceStack = NoStack;
        private List<Type> targetStack = NoStack;
        private static readonly List<Type> NoStack = [];
        private int maybeCount;
        private int sourceDepth;
        private int targetDepth;
        private ExpandingFlags expandingFlags;
        private bool overflow;
        private int overrideNextErrorInfo; // How many `reportRelationError` calls should be skipped in the elaboration pyramid
        private int skipParentCounter; // How many errors should be skipped 'above' in the elaboration pyramid
        private (Type, Type)? lastSkippedInfo;
        private List<(DiagnosticMessage, object?[])>? incompatibleStack;
        // In Node.js, the maximum number of elements in a map is 2^24. We limit the number of entries an invocation
        // of checkTypeRelatedTo can add to a relation to 1/8th of its remaining capacity.
        private int relationCount = (16_000_000 - relationOfCheck.Results.Count) >> 3;

        private bool IsIdentity => relation == c.identityRelation;

        internal bool Check(Type source, Type target, Func<DiagnosticMessageChain?>? containingMessageChain, ErrorOutputContainer? errorOutputContainer)
        {
            var result = IsRelatedTo(source, target, RecursionFlags.Both, !errorNode.IsNone, headMessage);
            if (incompatibleStack is not null)
            {
                ReportIncompatibleStack();
            }
            if (overflow)
            {
                // Record this relation as having failed such that we don't attempt the overflowing operation again.
                var id = c.GetRelationKey(source, target, IntersectionState.None, relation, ignoreConstraints: false);
                relation.Results[id] = RelationComparisonResult.Failed | (relationCount <= 0 ? RelationComparisonResult.ComplexityOverflow : RelationComparisonResult.StackDepthOverflow);
                var message = relationCount <= 0 ? Diagnostics.Excessive_complexity_comparing_types_0_and_1 : Diagnostics.Excessive_stack_depth_comparing_types_0_and_1;
                var diag = c.Error(!errorNode.IsNone ? errorNode : c.currentNode, message, c.TypeToString(source), c.TypeToString(target));
                if (errorOutputContainer is not null)
                {
                    (errorOutputContainer.Errors ??= []).Add(diag);
                }
            }
            else if (errorInfo is not null)
            {
                if (containingMessageChain is not null)
                {
                    var chain = containingMessageChain();
                    if (chain is not null)
                    {
                        ConcatenateDiagnosticMessageChains(chain, errorInfo);
                        errorInfo = chain;
                    }
                }
                List<DiagnosticRelatedInformation>? relatedInformation = null;
                // Check if we should issue an extra diagnostic to produce a quickfix for a slightly incorrect import statement
                var sourceSymbol = c.Types.Symbol(source);
                if (headMessage is not null && !errorNode.IsNone && result == Ternary.False && !sourceSymbol.IsNone && c.ExportTypeLinks.Has(sourceSymbol))
                {
                    var links = c.ExportTypeLinks.Get(sourceSymbol);
                    if (!links.OriginatingImport.IsNone && !Utilities.IsImportCall(c.StoreOf(links.OriginatingImport), links.OriginatingImport.Node))
                    {
                        var helpfulRetry = c.CheckTypeRelatedTo(c.GetTypeOfSymbol(links.Target), target, relation, errorNode: NodeRef.None);
                        if (helpfulRetry)
                        {
                            // Likely an incorrect import. Issue a helpful diagnostic to produce a quickfix to change the import
                            var info = c.CreateRelatedForNode(links.OriginatingImport, Diagnostics.Type_originates_at_this_import_A_namespace_style_import_cannot_be_called_or_constructed_and_will_cause_a_failure_at_runtime_Consider_using_a_default_import_or_import_require_here_instead);
                            relatedInformation = [info]; // Cause the error to appear with the error that triggered it
                        }
                    }
                }
                var diag = c.CreateDiagnosticForNodeFromMessageChain(errorNode, errorInfo, relatedInformation);
                if (relatedInfo is not null)
                {
                    foreach (var info in relatedInfo)
                    {
                        AddRelatedInfo(diag, info);
                    }
                }
                if (errorOutputContainer is not null)
                {
                    (errorOutputContainer.Errors ??= []).Add(diag);
                }
                if (errorOutputContainer is null || !errorOutputContainer.SkipLogging)
                {
                    c.diagnostics.Add(diag);
                }
            }
            return result != Ternary.False;
        }

        private void ResetErrorInfo(ErrorCalculationState saved)
        {
            errorInfo = saved.ErrorInfo;
            lastSkippedInfo = saved.LastSkippedInfo;
            incompatibleStack = saved.IncompatibleStack;
            overrideNextErrorInfo = saved.OverrideNextErrorInfo;
            skipParentCounter = saved.SkipParentCounter;
            relatedInfo = saved.RelatedInfo;
        }

        private ErrorCalculationState CaptureErrorCalculationState() =>
            new(errorInfo, lastSkippedInfo, incompatibleStack is null ? null : [.. incompatibleStack], overrideNextErrorInfo, skipParentCounter,
                relatedInfo is null ? null : [.. relatedInfo]);

        private void ReportIncompatibleError(DiagnosticMessage message, params object?[] args)
        {
            overrideNextErrorInfo++; // Suppress the next relation error
            lastSkippedInfo = null; // Reset skipped info cache
            (incompatibleStack ??= []).Add((message, args));
        }

        private void ReportIncompatibleStack()
        {
            var stack = incompatibleStack ?? [];
            incompatibleStack = null;
            var info = lastSkippedInfo;
            lastSkippedInfo = null;
            if (stack.Count == 1)
            {
                ReportError(stack[0].Item1, stack[0].Item2);
                if (info is { } i)
                {
                    // Actually do the last relation error
                    ReportRelationError(null, i.Item1, i.Item2);
                }
                return;
            }
            // The first error will be the innermost, while the last will be the outermost - so by popping off the end,
            // we can build from left to right
            var path = "";
            var secondaryRootErrors = new List<(DiagnosticMessage, object?[])>();
            while (stack.Count > 0)
            {
                var (msg, args) = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                switch (msg.Code)
                {
                    case 2326: // Types_of_property_0_are_incompatible
                    {
                        // Parenthesize a `new` if there is one
                        if (path.StartsWith("new ", StringComparison.Ordinal))
                        {
                            path = "(" + path + ")";
                        }
                        var str = Convert.ToString(args[0], System.Globalization.CultureInfo.InvariantCulture) ?? "";
                        // If leading, just print back the arg (irrespective of if it's a valid identifier)
                        if (path.Length == 0)
                        {
                            path = str;
                        }
                        // Otherwise write a dotted name if possible
                        else if (Scanner.IsIdentifierText(str, c.compilerOptions.GetEmitScriptTarget()))
                        {
                            path = path + "." + str;
                        }
                        // Failing that, check if the name is already a computed name
                        else if (str.Length > 0 && str[0] == '[' && str[^1] == ']')
                        {
                            path += str;
                        }
                        // And finally write out a computed name as a last resort
                        else
                        {
                            path = path + "[" + str + "]";
                        }
                        break;
                    }
                    case 2202: // Call_signature_return_types_0_and_1_are_incompatible
                    case 2203: // Construct_signature_return_types_0_and_1_are_incompatible
                    case 2204: // Call_signatures_with_no_arguments_have_incompatible_return_types_0_and_1
                    case 2205: // Construct_signatures_with_no_arguments_have_incompatible_return_types_0_and_1
                    {
                        if (path.Length == 0)
                        {
                            // Don't flatten signature compatability errors at the start of a chain - instead prefer
                            // to unify (the with no arguments bit is excessive for printback) and print them back
                            var mappedMsg = msg;
                            if (msg.Code == 2204)
                            {
                                mappedMsg = Diagnostics.Call_signature_return_types_0_and_1_are_incompatible;
                            }
                            else if (msg.Code == 2205)
                            {
                                mappedMsg = Diagnostics.Construct_signature_return_types_0_and_1_are_incompatible;
                            }
                            secondaryRootErrors.Insert(0, (mappedMsg, [args[0], args[1]]));
                        }
                        else
                        {
                            var prefix = msg.Code is 2203 or 2205 ? "new " : "";
                            var parameters = msg.Code is 2204 or 2205 ? "" : "...";
                            path = prefix + path + "(" + parameters + ")";
                        }
                        break;
                    }
                    case 2626: // Type_at_position_0_in_source_is_not_compatible_with_type_at_position_1_in_target
                        secondaryRootErrors.Insert(0, (Diagnostics.Type_at_position_0_in_source_is_not_compatible_with_type_at_position_1_in_target, [args[0], args[1]]));
                        break;
                    case 2627: // Type_at_positions_0_through_1_in_source_is_not_compatible_with_type_at_position_2_in_target
                        secondaryRootErrors.Insert(0, (Diagnostics.Type_at_positions_0_through_1_in_source_is_not_compatible_with_type_at_position_2_in_target, [args[0], args[1], args[2]]));
                        break;
                    default:
                        throw new InvalidOperationException($"Unhandled Diagnostic: {msg.Code}");
                }
            }
            if (path.Length > 0)
            {
                ReportError(path[^1] == ')' ? Diagnostics.The_types_returned_by_0_are_incompatible_between_these_types : Diagnostics.The_types_of_0_are_incompatible_between_these_types, path);
            }
            else if (secondaryRootErrors.Count > 0)
            {
                // Remove the innermost secondary error as it will duplicate the error already reported by `reportRelationError` on entry
                secondaryRootErrors.RemoveAt(0);
            }
            foreach (var (msg, args) in secondaryRootErrors)
            {
                // (TypeScript temporarily overrides elidedInCompatabilityPyramid to ensure the error is reported)
                ReportErrorWorker(msg, args, forceReport: true);
            }
            if (info is { } last)
            {
                // Actually do the last relation error
                ReportRelationError(null, last.Item1, last.Item2);
            }
        }

        private void ReportError(DiagnosticMessage message, params object?[] args) => ReportErrorWorker(message, args, forceReport: false);

        private void ReportErrorWorker(DiagnosticMessage message, object?[] args, bool forceReport)
        {
            if (incompatibleStack is not null)
            {
                ReportIncompatibleStack();
            }
            if (message.ElidedInCompatibilityPyramid && !forceReport)
            {
                return;
            }
            if (skipParentCounter == 0)
            {
                errorInfo = ChainDiagnosticMessages(errorInfo, message, args);
            }
            else
            {
                skipParentCounter--;
            }
        }

        private void ReportParentSkippedError(DiagnosticMessage message, params object?[] args)
        {
            ReportError(message, args);
            skipParentCounter++;
        }

        private void AssociateRelatedInfo(ProgramRelatedInformation info) => (relatedInfo ??= []).Add(info);

        private void ReportRelationError(DiagnosticMessage? message, Type source, Type target)
        {
            if (incompatibleStack is not null)
            {
                ReportIncompatibleStack();
            }
            var (sourceType, targetType) = c.GetTypeNamesForErrorDisplay(source, target);
            var generalizedSource = source;
            var generalizedSourceType = sourceType;
            // Don't generalize on 'never' - we really want the original type
            // to be displayed for use-cases like 'assertNever'.
            if ((c.Types.Flags(target) & TypeFlags.Never) == 0 && c.IsLiteralType(source) && !c.TypeCouldHaveTopLevelSingletonTypes(target))
            {
                generalizedSource = c.GetBaseTypeOfLiteralType(source);
                // (Debug.assert(!isTypeAssignableTo(generalizedSource, target)): TypeScript evaluates the relation)
                c.IsTypeAssignableTo(generalizedSource, target);
                generalizedSourceType = c.GetTypeNameForErrorDisplay(generalizedSource);
            }
            // If `target` is of indexed access type (And `source` it is not), we use the object type of `target` for better error reporting
            var targetFlags = (c.Types.Flags(target) & TypeFlags.IndexedAccess) != 0 && (c.Types.Flags(source) & TypeFlags.IndexedAccess) == 0
                ? c.Types.Flags(c.Types.Data<IndexedAccessData>(target).ObjectType)
                : c.Types.Flags(target);
            if ((targetFlags & TypeFlags.TypeParameter) != 0 && target != c.markerSuperTypeForCheck && target != c.markerSubTypeForCheck)
            {
                var constraint = c.GetBaseConstraintOfType(target);
                var needsOriginalSource = false;
                if (!constraint.IsNone && (c.IsTypeAssignableTo(generalizedSource, constraint) || (needsOriginalSource = c.IsTypeAssignableTo(source, constraint))))
                {
                    ReportError(Diagnostics._0_is_assignable_to_the_constraint_of_type_1_but_1_could_be_instantiated_with_a_different_subtype_of_constraint_2,
                        needsOriginalSource ? sourceType : generalizedSourceType, targetType, c.TypeToString(constraint));
                }
                else
                {
                    errorInfo = null;
                    ReportError(Diagnostics._0_could_be_instantiated_with_an_arbitrary_type_which_could_be_unrelated_to_1, targetType, generalizedSourceType);
                }
            }
            if (message is null)
            {
                if (relation == c.comparableRelation)
                {
                    message = Diagnostics.Type_0_is_not_comparable_to_type_1;
                }
                else if (sourceType == targetType)
                {
                    message = Diagnostics.Type_0_is_not_assignable_to_type_1_Two_different_types_with_this_name_exist_but_they_are_unrelated;
                }
                else if (c.exactOptionalPropertyTypes && c.GetExactOptionalUnassignableProperties(source, target).Count > 0)
                {
                    message = Diagnostics.Type_0_is_not_assignable_to_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_types_of_the_target_s_properties;
                }
                else
                {
                    if ((c.Types.Flags(source) & TypeFlags.StringLiteral) != 0 && (c.Types.Flags(target) & TypeFlags.Union) != 0)
                    {
                        var suggestedType = c.GetSuggestedTypeForNonexistentStringLiteralType(source, target);
                        if (!suggestedType.IsNone)
                        {
                            ReportError(Diagnostics.Type_0_is_not_assignable_to_type_1_Did_you_mean_2, generalizedSourceType, targetType, c.TypeToString(suggestedType));
                            return;
                        }
                    }
                    message = Diagnostics.Type_0_is_not_assignable_to_type_1;
                }
            }
            else if (message == Diagnostics.Argument_of_type_0_is_not_assignable_to_parameter_of_type_1 && c.exactOptionalPropertyTypes &&
                c.GetExactOptionalUnassignableProperties(source, target).Count > 0)
            {
                message = Diagnostics.Argument_of_type_0_is_not_assignable_to_parameter_of_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_types_of_the_target_s_properties;
            }
            ReportError(message, generalizedSourceType, targetType);
        }

        private void TryElaborateErrorsForPrimitivesAndObjects(Type source, Type target)
        {
            var sourceType = c.SymbolValueDeclarationIsContextSensitive(c.Types.Symbol(source)) ? c.TypeToString(source, c.ValueDeclarationOf(c.Types.Symbol(source))) : c.TypeToString(source);
            var targetType = c.SymbolValueDeclarationIsContextSensitive(c.Types.Symbol(target)) ? c.TypeToString(target, c.ValueDeclarationOf(c.Types.Symbol(target))) : c.TypeToString(target);
            if ((c.globalStringType == source && c.stringType == target) ||
                (c.globalNumberType == source && c.numberType == target) ||
                (c.globalBooleanType == source && c.booleanType == target) ||
                (c.GetGlobalESSymbolType() == source && c.esSymbolType == target))
            {
                ReportError(Diagnostics._0_is_a_primitive_but_1_is_a_wrapper_object_Prefer_using_0_when_possible, targetType, sourceType);
            }
        }

        // Try and elaborate array and tuple errors. Returns false if we have found an elaboration, or we should ignore
        // any other elaborations when relating the `source` and `target` types.
        private bool TryElaborateArrayLikeErrors(Type source, Type target, bool reportErrors)
        {
            // The spec for elaboration is:
            // - If the source is a readonly tuple and the target is a mutable array or tuple, elaborate on mutability and skip property elaborations.
            // - If the source is a tuple then skip property elaborations if the target is an array or tuple.
            // - If the source is a readonly array and the target is a mutable array or tuple, elaborate on mutability and skip property elaborations.
            // - If the source an array then skip property elaborations if the target is a tuple.
            if (c.IsTupleType(source))
            {
                if (c.TupleTarget(source).Readonly && c.IsMutableArrayOrTuple(target))
                {
                    if (reportErrors)
                    {
                        ReportError(Diagnostics.The_type_0_is_readonly_and_cannot_be_assigned_to_the_mutable_type_1, c.TypeToString(source), c.TypeToString(target));
                    }
                    return false;
                }
                return c.IsArrayOrTupleType(target);
            }
            if (c.IsReadonlyArrayType(source) && c.IsMutableArrayOrTuple(target))
            {
                if (reportErrors)
                {
                    ReportError(Diagnostics.The_type_0_is_readonly_and_cannot_be_assigned_to_the_mutable_type_1, c.TypeToString(source), c.TypeToString(target));
                }
                return false;
            }
            if (c.IsTupleType(target))
            {
                return c.IsArrayType(source);
            }
            return true;
        }

        private int IsRelatedToWorker(Type source, Type target, bool reportErrors) => IsRelatedTo(source, target, RecursionFlags.Both, reportErrors);

        // Compare two types and return
        // * Ternary.True if they are related with no assumptions,
        // * Ternary.Maybe if they are related with assumptions of other relationships, or
        // * Ternary.False if they are not related.
        private int IsRelatedTo(Type originalSource, Type originalTarget, RecursionFlags recursionFlags = RecursionFlags.Both, bool reportErrors = false,
            DiagnosticMessage? headMessage = null, IntersectionState intersectionState = IntersectionState.None)
        {
            if (originalSource == originalTarget)
            {
                return Ternary.True;
            }
            // Before normalization: if `source` is type an object type, and `target` is primitive,
            // skip all the checks we don't need and just return `isSimpleTypeRelatedTo` result
            if ((c.Types.Flags(originalSource) & TypeFlags.Object) != 0 && (c.Types.Flags(originalTarget) & TypeFlags.Primitive) != 0)
            {
                if ((relation == c.comparableRelation && (c.Types.Flags(originalTarget) & TypeFlags.Never) == 0 && c.IsSimpleTypeRelatedTo(originalTarget, originalSource, relation)) ||
                    c.IsSimpleTypeRelatedTo(originalSource, originalTarget, relation, reportErrors ? ReportErrorArray : null))
                {
                    return Ternary.True;
                }
                if (reportErrors)
                {
                    ReportErrorResults(originalSource, originalTarget, originalSource, originalTarget, headMessage);
                }
                return Ternary.False;
            }
            // Normalize the source and target types: Turn fresh literal types into regular literal types,
            // turn deferred type references into regular type references, simplify indexed access and
            // conditional types, and resolve substitution types to either the substitution (on the source
            // side) or the type variable (on the target side).
            var source = c.GetNormalizedType(originalSource, writing: false);
            var target = c.GetNormalizedType(originalTarget, writing: true);
            if (source == target)
            {
                return Ternary.True;
            }
            if (IsIdentity)
            {
                if (c.Types.Flags(source) != c.Types.Flags(target))
                {
                    return Ternary.False;
                }
                if ((c.Types.Flags(source) & TypeFlags.Singleton) != 0)
                {
                    return Ternary.True;
                }
                return RecursiveTypeRelatedTo(source, target, false, IntersectionState.None, recursionFlags);
            }
            // We fastpath comparing a type parameter to exactly its constraint, as this is _super_ common,
            // and otherwise, for type parameters in large unions, causes us to need to compare the union to itself,
            // as we break down the _target_ union first, _then_ get the source constraint - so for every
            // member of the target, we attempt to find a match in the source. This avoids that in cases where
            // the target is exactly the constraint.
            if ((c.Types.Flags(source) & TypeFlags.TypeParameter) != 0 && c.GetConstraintOfType(source) == target)
            {
                return Ternary.True;
            }
            // See if we're relating a definitely non-nullable type to a union that includes null and/or undefined
            // plus a single non-nullable type. If so, remove null and/or undefined from the target type.
            if ((c.Types.Flags(source) & TypeFlags.DefinitelyNonNullable) != 0 && (c.Types.Flags(target) & TypeFlags.Union) != 0)
            {
                var types = c.Types.Elements(c.Types.Union(target).Types);
                var candidate = types.Length == 2 && (c.Types.Flags(types[0]) & TypeFlags.Nullable) != 0 ? types[1] :
                    types.Length == 3 && (c.Types.Flags(types[0]) & TypeFlags.Nullable) != 0 && (c.Types.Flags(types[1]) & TypeFlags.Nullable) != 0 ? types[2] :
                    Type.None;
                if (!candidate.IsNone && (c.Types.Flags(candidate) & TypeFlags.Nullable) == 0)
                {
                    target = c.GetNormalizedType(candidate, writing: true);
                    if (source == target)
                    {
                        return Ternary.True;
                    }
                }
            }
            if ((relation == c.comparableRelation && (c.Types.Flags(target) & TypeFlags.Never) == 0 && c.IsSimpleTypeRelatedTo(target, source, relation)) ||
                c.IsSimpleTypeRelatedTo(source, target, relation, reportErrors ? ReportErrorArray : null))
            {
                return Ternary.True;
            }
            if ((c.Types.Flags(source) & TypeFlags.StructuredOrInstantiable) != 0 || (c.Types.Flags(target) & TypeFlags.StructuredOrInstantiable) != 0)
            {
                var isPerformingExcessPropertyChecks = (intersectionState & IntersectionState.Target) == 0 && c.IsObjectLiteralType(source) && (c.Types.ObjectFlagsOf(source) & ObjectFlags.FreshLiteral) != 0;
                if (isPerformingExcessPropertyChecks)
                {
                    if (HasExcessProperties(source, target, reportErrors))
                    {
                        if (reportErrors)
                        {
                            ReportRelationError(headMessage, source, !c.Types.Record(originalTarget).AliasSymbol.IsNone ? originalTarget : target);
                        }
                        return Ternary.False;
                    }
                }
                var isPerformingCommonPropertyChecks = (relation != c.comparableRelation || c.IsUnitType(source)) &&
                    (intersectionState & IntersectionState.Target) == 0 &&
                    (c.Types.Flags(source) & (TypeFlags.Primitive | TypeFlags.Object | TypeFlags.Intersection)) != 0 && source != c.globalObjectType &&
                    (c.Types.Flags(target) & (TypeFlags.Object | TypeFlags.Intersection)) != 0 && c.IsWeakType(target) &&
                    (c.GetPropertiesOfType(source).Count > 0 || c.TypeHasCallOrConstructSignatures(source));
                var isComparingJsxAttributes = (c.Types.ObjectFlagsOf(source) & ObjectFlags.JsxAttributes) != 0;
                if (isPerformingCommonPropertyChecks && !c.HasCommonProperties(source, target, isComparingJsxAttributes))
                {
                    if (reportErrors)
                    {
                        var sourceString = c.TypeToString(!c.Types.Record(originalSource).AliasSymbol.IsNone ? originalSource : source);
                        var targetString = c.TypeToString(!c.Types.Record(originalTarget).AliasSymbol.IsNone ? originalTarget : target);
                        var calls = c.GetSignaturesOfType(source, SignatureKind.Call);
                        var constructs = c.GetSignaturesOfType(source, SignatureKind.Construct);
                        if ((calls.Count > 0 && IsRelatedTo(c.GetReturnTypeOfSignature(calls[0]), target, RecursionFlags.Source, false) != Ternary.False) ||
                            (constructs.Count > 0 && IsRelatedTo(c.GetReturnTypeOfSignature(constructs[0]), target, RecursionFlags.Source, false) != Ternary.False))
                        {
                            ReportError(Diagnostics.Value_of_type_0_has_no_properties_in_common_with_type_1_Did_you_mean_to_call_it, sourceString, targetString);
                        }
                        else
                        {
                            ReportError(Diagnostics.Type_0_has_no_properties_in_common_with_type_1, sourceString, targetString);
                        }
                    }
                    return Ternary.False;
                }
                var skipCaching = ((c.Types.Flags(source) & TypeFlags.Union) != 0 && c.Types.Union(source).Types.Count < 4 && (c.Types.Flags(target) & TypeFlags.Union) == 0) ||
                    ((c.Types.Flags(target) & TypeFlags.Union) != 0 && c.Types.Union(target).Types.Count < 4 && (c.Types.Flags(source) & TypeFlags.StructuredOrInstantiable) == 0);
                var result = skipCaching
                    ? UnionOrIntersectionRelatedTo(source, target, reportErrors, intersectionState)
                    : RecursiveTypeRelatedTo(source, target, reportErrors, intersectionState, recursionFlags);
                if (result != Ternary.False)
                {
                    return result;
                }
            }
            if (reportErrors)
            {
                ReportErrorResults(originalSource, originalTarget, source, target, headMessage);
            }
            return Ternary.False;
        }

        private void ReportErrorArray(DiagnosticMessage message, object?[] args) => ReportError(message, args);

        private DiagnosticMessageChain? CurrentErrorInfo() => errorInfo;

        private void ReportErrorResults(Type originalSource, Type originalTarget, Type source, Type target, DiagnosticMessage? headMessage)
        {
            var sourceHasBase = !c.GetSingleBaseForNonAugmentingSubtype(originalSource).IsNone;
            var targetHasBase = !c.GetSingleBaseForNonAugmentingSubtype(originalTarget).IsNone;
            source = !c.Types.Record(originalSource).AliasSymbol.IsNone || sourceHasBase ? originalSource : source;
            target = !c.Types.Record(originalTarget).AliasSymbol.IsNone || targetHasBase ? originalTarget : target;
            var maybeSuppress = overrideNextErrorInfo > 0;
            if (maybeSuppress)
            {
                overrideNextErrorInfo--;
            }
            if ((c.Types.Flags(source) & TypeFlags.Object) != 0 && (c.Types.Flags(target) & TypeFlags.Object) != 0)
            {
                var currentError = errorInfo;
                TryElaborateArrayLikeErrors(source, target, true);
                if (errorInfo != currentError)
                {
                    maybeSuppress = errorInfo is not null;
                }
            }
            if ((c.Types.Flags(source) & TypeFlags.Object) != 0 && (c.Types.Flags(target) & TypeFlags.Primitive) != 0)
            {
                TryElaborateErrorsForPrimitivesAndObjects(source, target);
            }
            else if (!c.Types.Symbol(source).IsNone && (c.Types.Flags(source) & TypeFlags.Object) != 0 && c.globalObjectType == source)
            {
                ReportError(Diagnostics.The_Object_type_is_assignable_to_very_few_other_types_Did_you_mean_to_use_the_any_type_instead);
            }
            else if ((c.Types.ObjectFlagsOf(source) & ObjectFlags.JsxAttributes) != 0 && (c.Types.Flags(target) & TypeFlags.Intersection) != 0)
            {
                var targetTypes = c.Types.Elements(c.Types.Union(target).Types).ToArray();
                var intrinsicAttributes = c.GetJsxType("IntrinsicAttributes", errorNode);
                var intrinsicClassAttributes = c.GetJsxType("IntrinsicClassAttributes", errorNode);
                if (!c.IsErrorType(intrinsicAttributes) && !c.IsErrorType(intrinsicClassAttributes) &&
                    (Array.IndexOf(targetTypes, intrinsicAttributes) >= 0 || Array.IndexOf(targetTypes, intrinsicClassAttributes) >= 0))
                {
                    // do not report top error
                    return;
                }
            }
            else
            {
                errorInfo = c.ElaborateNeverIntersection(errorInfo, originalTarget);
            }
            // Used by, eg, missing property checking to replace the top-level message with a more informative one.
            if (headMessage is null && maybeSuppress)
            {
                // We suppress a call to `reportRelationError` or not depending on the state of the type checker, so
                // we call `reportRelationError` here and then undo its effects to figure out what would be the diagnostic
                // if we hadn't supress it, and save that as a canonical diagnostic for deduplication purposes.
                var savedErrorState = CaptureErrorCalculationState();
                ReportRelationError(headMessage, source, target);
                var reported = CurrentErrorInfo();
                var canonicalInfo = reported is not null && reported != savedErrorState.ErrorInfo ? reported : null;
                ResetErrorInfo(savedErrorState);
                var restored = CurrentErrorInfo();
                if (canonicalInfo is not null && restored is not null)
                {
                    restored.CanonicalHead = (canonicalInfo.Code, canonicalInfo.MessageText);
                }
                lastSkippedInfo = (source, target);
                return;
            }
            ReportRelationError(headMessage, source, target);
            if ((c.Types.Flags(source) & TypeFlags.TypeParameter) != 0 && c.DeclarationsOf(c.Types.Symbol(source)) is { Count: > 0 } sourceDeclarations && c.GetConstraintOfType(source).IsNone)
            {
                var syntheticParam = c.CloneTypeParameter(source);
                c.Types.TypeParameter(syntheticParam).Constraint = c.InstantiateType(target, MakeUnaryTypeMapper(source, syntheticParam));
                if (c.HasNonCircularBaseConstraint(syntheticParam))
                {
                    var targetConstraintString = c.TypeToString(target, sourceDeclarations[0]);
                    AssociateRelatedInfo(c.CreateRelatedForNode(sourceDeclarations[0], Diagnostics.This_type_parameter_might_need_an_extends_0_constraint, targetConstraintString));
                }
            }
        }

        private Type GetTypeOfPropertyInTypes(Type[] types, string name)
        {
            var propTypes = new List<Type>();
            foreach (var t in types)
            {
                var type = c.GetApparentType(t);
                var prop = (c.Types.Flags(type) & TypeFlags.UnionOrIntersection) != 0 ? c.GetPropertyOfUnionOrIntersectionType(type, c.Names.Intern(name), false) : c.GetPropertyOfObjectType(type, name);
                var propType = !prop.IsNone ? c.GetTypeOfSymbol(prop) : Type.None;
                if (propType.IsNone)
                {
                    propType = c.GetApplicableIndexInfoForName(type, name)?.Type ?? Type.None;
                }
                propTypes.Add(propType.IsNone ? c.undefinedType : propType);
            }
            return c.GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(propTypes));
        }

        private bool HasExcessProperties(Type source, Type target, bool reportErrors)
        {
            if (!c.IsExcessPropertyCheckTarget(target) || (!c.noImplicitAny && (c.Types.ObjectFlagsOf(target) & ObjectFlags.JSLiteral) != 0))
            {
                return false; // Disable excess property checks on JS literals to simulate having an implicit "index signature" - but only outside of noImplicitAny
            }
            var isComparingJsxAttributes = (c.Types.ObjectFlagsOf(source) & ObjectFlags.JsxAttributes) != 0;
            if ((relation == c.assignableRelation || relation == c.comparableRelation) &&
                (c.IsTypeSubsetOf(c.globalObjectType, target) || (!isComparingJsxAttributes && c.IsEmptyObjectType(target))))
            {
                return false;
            }
            var reducedTarget = target;
            Type[]? checkTypes = null;
            if ((c.Types.Flags(target) & TypeFlags.Union) != 0)
            {
                var matching = c.FindMatchingDiscriminantType(source, target, (s, t) => IsRelatedTo(s, t));
                reducedTarget = !matching.IsNone ? matching : c.FilterPrimitivesIfContainsNonPrimitive(target);
                checkTypes = (c.Types.Flags(reducedTarget) & TypeFlags.Union) != 0 ? c.Types.Elements(c.Types.Union(reducedTarget).Types).ToArray() : [reducedTarget];
            }
            foreach (var prop in c.GetPropertiesOfType(source))
            {
                if (ShouldCheckAsExcessProperty(prop, c.Types.Symbol(source)) && !c.IsIgnoredJsxProperty(source, prop))
                {
                    if (!c.IsKnownProperty(reducedTarget, c.NameOf(prop), isComparingJsxAttributes))
                    {
                        if (reportErrors)
                        {
                            // Report error in terms of object types in the target as those are the only ones
                            // we check in isKnownProperty.
                            var errorTarget = c.FilterType(reducedTarget, c.IsExcessPropertyCheckTarget);
                            // We know *exactly* where things went wrong when comparing the types.
                            // Use this property as the error node as this will be more helpful in
                            // reasoning about what went wrong.
                            if (errorNode.IsNone)
                            {
                                throw new InvalidOperationException("no error node");
                            }
                            var errorKind = c.Kind(errorNode);
                            var errorParentKind = c.Parent(errorNode).IsNone ? SyntaxKind.Unknown : c.Kind(c.Parent(errorNode));
                            if (errorKind is SyntaxKind.JsxAttributes or SyntaxKind.JsxOpeningElement or SyntaxKind.JsxSelfClosingElement ||
                                errorParentKind is SyntaxKind.JsxOpeningElement or SyntaxKind.JsxSelfClosingElement)
                            {
                                // JsxAttributes has an object-literal flag and undergo same type-assignablity check as normal object-literal.
                                // However, using an object-literal error message will be very confusing to the users so we give different a message.
                                var valueDeclaration = c.ValueDeclarationOf(prop);
                                if (!valueDeclaration.IsNone && c.Kind(valueDeclaration) == SyntaxKind.JsxAttribute && errorNode.File == valueDeclaration.File)
                                {
                                    // Note that extraneous children (as in `<NoChild>extra</NoChild>`) don't pass this check,
                                    // since `children` is a SyntaxKind.PropertySignature instead of a SyntaxKind.JsxAttribute.
                                    errorNode = At(valueDeclaration, c.StoreOf(valueDeclaration).Name(valueDeclaration.Node));
                                }
                                var propName = c.SymbolToString(prop);
                                var suggestionSymbol = c.GetSuggestedSymbolForNonexistentJSXAttribute(propName, errorTarget);
                                var suggestion = suggestionSymbol.IsNone ? null : c.SymbolToString(suggestionSymbol);
                                if (!string.IsNullOrEmpty(suggestion))
                                {
                                    ReportError(Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_2, propName, c.TypeToString(errorTarget), suggestion);
                                }
                                else
                                {
                                    ReportError(Diagnostics.Property_0_does_not_exist_on_type_1, propName, c.TypeToString(errorTarget));
                                }
                            }
                            else
                            {
                                // use the property's value declaration if the property is assigned inside the literal itself
                                var objectLiteralDeclarations = c.DeclarationsOf(c.Types.Symbol(source));
                                var objectLiteralDeclaration = objectLiteralDeclarations is { Count: > 0 } old ? old[0] : NodeRef.None;
                                string? suggestion = null;
                                var valueDeclaration = c.ValueDeclarationOf(prop);
                                if (!valueDeclaration.IsNone && !objectLiteralDeclaration.IsNone && IsAncestorOrSelf(valueDeclaration, objectLiteralDeclaration) && objectLiteralDeclaration.File == errorNode.File)
                                {
                                    var name = At(valueDeclaration, c.StoreOf(valueDeclaration).Name(valueDeclaration.Node));
                                    errorNode = name;
                                    if (c.Kind(name) == SyntaxKind.Identifier)
                                    {
                                        suggestion = c.GetSuggestionForNonexistentProperty(name, errorTarget);
                                    }
                                }
                                if (suggestion is not null)
                                {
                                    ReportParentSkippedError(Diagnostics.Object_literal_may_only_specify_known_properties_but_0_does_not_exist_in_type_1_Did_you_mean_to_write_2,
                                        c.SymbolToString(prop), c.TypeToString(errorTarget), suggestion);
                                }
                                else
                                {
                                    ReportParentSkippedError(Diagnostics.Object_literal_may_only_specify_known_properties_and_0_does_not_exist_in_type_1,
                                        c.SymbolToString(prop), c.TypeToString(errorTarget));
                                }
                            }
                        }
                        return true;
                    }
                    if (checkTypes is not null && IsRelatedTo(c.GetTypeOfSymbol(prop), GetTypeOfPropertyInTypes(checkTypes, c.NameOf(prop)), RecursionFlags.Both, reportErrors) == Ternary.False)
                    {
                        if (reportErrors)
                        {
                            ReportIncompatibleError(Diagnostics.Types_of_property_0_are_incompatible, c.SymbolToString(prop));
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        // findAncestor(node, d => d === ancestor)
        private bool IsAncestorOrSelf(NodeRef node, NodeRef ancestor)
        {
            if (node.File != ancestor.File)
            {
                return false;
            }
            for (var n = node; !n.IsNone; n = c.Parent(n))
            {
                if (n == ancestor)
                {
                    return true;
                }
            }
            return false;
        }

        private bool ShouldCheckAsExcessProperty(SymbolRef prop, SymbolRef container)
        {
            var propDeclaration = c.ValueDeclarationOf(prop);
            var containerDeclaration = container.IsNone ? NodeRef.None : c.ValueDeclarationOf(container);
            return !propDeclaration.IsNone && !containerDeclaration.IsNone && c.Parent(propDeclaration) == containerDeclaration;
        }

        private int UnionOrIntersectionRelatedTo(Type source, Type target, bool reportErrors, IntersectionState intersectionState)
        {
            // Note that these checks are specifically ordered to produce correct results. In particular,
            // we need to deconstruct unions before intersections (because unions are always at the top),
            // and we need to handle "each" relations before "some" relations for the same kind of type.
            var sourceFlags = c.Types.Flags(source);
            var targetFlags = c.Types.Flags(target);
            if ((sourceFlags & TypeFlags.Union) != 0)
            {
                if ((targetFlags & TypeFlags.Union) != 0)
                {
                    // Intersections of union types are normalized into unions of intersection types, and such normalized
                    // unions can get very large and expensive to relate. The following fast path checks if the source union
                    // originated in an intersection. If so, and if that intersection contains the target type, then we know
                    // the result to be true (for any two types A and B, A & B is related to both A and B).
                    var sourceOrigin = c.Types.Union(source).Origin;
                    if (!sourceOrigin.IsNone && (c.Types.Flags(sourceOrigin) & TypeFlags.Intersection) != 0 && !c.Types.Record(target).AliasSymbol.IsNone &&
                        ContainsType(c.Types.Elements(c.Types.Union(sourceOrigin).Types), target))
                    {
                        return Ternary.True;
                    }
                    // Similarly, in unions of unions the we preserve the original list of unions. This original list is often
                    // much shorter than the normalized result, so we scan it in the following fast path.
                    var targetOrigin = c.Types.Union(target).Origin;
                    if (!targetOrigin.IsNone && (c.Types.Flags(targetOrigin) & TypeFlags.Union) != 0 && !c.Types.Record(source).AliasSymbol.IsNone &&
                        ContainsType(c.Types.Elements(c.Types.Union(targetOrigin).Types), source))
                    {
                        return Ternary.True;
                    }
                }
                return relation == c.comparableRelation
                    ? SomeTypeRelatedToType(source, target, reportErrors && (sourceFlags & TypeFlags.Primitive) == 0, intersectionState)
                    : EachTypeRelatedToType(source, target, reportErrors && (sourceFlags & TypeFlags.Primitive) == 0, intersectionState);
            }
            if ((targetFlags & TypeFlags.Union) != 0)
            {
                return TypeRelatedToSomeType(c.GetRegularTypeOfObjectLiteral(source), target, reportErrors && (sourceFlags & TypeFlags.Primitive) == 0 && (targetFlags & TypeFlags.Primitive) == 0, intersectionState);
            }
            if ((targetFlags & TypeFlags.Intersection) != 0)
            {
                return TypeRelatedToEachType(source, target, reportErrors, IntersectionState.Target);
            }
            // Source is an intersection. For the comparable relation, if the target is a primitive type we hoist the
            // constraints of all non-primitive types in the source into a new intersection. We do this because the
            // intersection may further constrain the constraints of the non-primitive types. For example, given a type
            // parameter 'T extends 1 | 2', the intersection 'T & 1' should be reduced to '1' such that it doesn't
            // appear to be comparable to '2'.
            if (relation == c.comparableRelation && (targetFlags & TypeFlags.Primitive) != 0)
            {
                var types = c.Types.Elements(c.Types.Union(source).Types);
                var constraints = new Type[types.Length];
                var changed = false;
                for (var i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if ((c.Types.Flags(t) & TypeFlags.Instantiable) != 0)
                    {
                        var constraint = c.GetBaseConstraintOfType(t);
                        constraints[i] = constraint.IsNone ? c.unknownType : constraint;
                    }
                    else
                    {
                        constraints[i] = t;
                    }
                    changed |= constraints[i] != t;
                }
                if (changed)
                {
                    source = c.GetIntersectionType(constraints);
                    if ((c.Types.Flags(source) & TypeFlags.Never) != 0)
                    {
                        return Ternary.False;
                    }
                    if ((c.Types.Flags(source) & TypeFlags.Intersection) == 0)
                    {
                        var r = IsRelatedTo(source, target, RecursionFlags.Source, false);
                        return r != Ternary.False ? r : IsRelatedTo(target, source, RecursionFlags.Source, false);
                    }
                }
            }
            // Check to see if any constituents of the intersection are immediately related to the target.
            // Don't report errors though. Elaborating on whether a source constituent is related to the target is
            // not actually useful and leads to some confusing error messages. Instead, we rely on the caller
            // checking whether the full intersection viewed as an object is related to the target.
            return SomeTypeRelatedToType(source, target, false, IntersectionState.Source);
        }

        private int EachTypeRelatedToSomeType(Type source, Type target)
        {
            var result = Ternary.True;
            // (module CO5: the list read in place; the type-list arena only appends, so the span stays a valid snapshot)
            foreach (var sourceType in c.Types.Elements(c.Types.Union(source).Types))
            {
                var related = TypeRelatedToSomeType(sourceType, target, false, IntersectionState.None);
                if (related == Ternary.False)
                {
                    return Ternary.False;
                }
                result &= related;
            }
            return result;
        }

        private int TypeRelatedToSomeType(Type source, Type target, bool reportErrors, IntersectionState intersectionState)
        {
            var targetTypes = c.Types.Elements(c.Types.Union(target).Types);
            if ((c.Types.Flags(target) & TypeFlags.Union) != 0)
            {
                if (ContainsType(targetTypes, source))
                {
                    return Ternary.True;
                }
                var sourceFlags = c.Types.Flags(source);
                if (relation != c.comparableRelation && (c.Types.ObjectFlagsOf(target) & ObjectFlags.PrimitiveUnion) != 0 && (sourceFlags & TypeFlags.EnumLiteral) == 0 &&
                    ((sourceFlags & (TypeFlags.StringLiteral | TypeFlags.BooleanLiteral | TypeFlags.BigIntLiteral)) != 0 ||
                        ((relation == c.subtypeRelation || relation == c.strictSubtypeRelation) && (sourceFlags & TypeFlags.NumberLiteral) != 0)))
                {
                    // When relating a literal type to a union of primitive types, we know the relation is false unless
                    // the union contains the base primitive type or the literal type in one of its fresh/regular forms.
                    // We exclude numeric literals for non-subtype relations because numeric literals are assignable to
                    // numeric enum literals with the same value. Similarly, we exclude enum literal types because
                    // identically named enum types are related (see isEnumTypeRelatedTo). We exclude the comparable
                    // relation in entirety because it needs to be checked in both directions.
                    var alternateForm = source == c.GetRegularTypeOfLiteralType(source) ? c.GetFreshTypeOfLiteralType(source) : c.GetRegularTypeOfLiteralType(source);
                    var primitive = (sourceFlags & TypeFlags.StringLiteral) != 0 ? c.stringType :
                        (sourceFlags & TypeFlags.NumberLiteral) != 0 ? c.numberType :
                        (sourceFlags & TypeFlags.BigIntLiteral) != 0 ? c.bigintType :
                        Type.None;
                    return (!primitive.IsNone && ContainsType(targetTypes, primitive)) || (!alternateForm.IsNone && ContainsType(targetTypes, alternateForm)) ? Ternary.True : Ternary.False;
                }
                var match = c.GetMatchingUnionConstituentForType(target, source);
                if (!match.IsNone)
                {
                    var related = IsRelatedTo(source, match, RecursionFlags.Target, false, null, intersectionState);
                    if (related != Ternary.False)
                    {
                        return related;
                    }
                }
            }
            foreach (var type in targetTypes)
            {
                var related = IsRelatedTo(source, type, RecursionFlags.Target, false, null, intersectionState);
                if (related != Ternary.False)
                {
                    return related;
                }
            }
            if (reportErrors)
            {
                // Elaborate only if we can find a best matching type in the target union
                var bestMatchingType = c.GetBestMatchingType(source, target, (s, t) => IsRelatedTo(s, t));
                if (!bestMatchingType.IsNone)
                {
                    IsRelatedTo(source, bestMatchingType, RecursionFlags.Target, true, null, intersectionState);
                }
            }
            return Ternary.False;
        }

        private int TypeRelatedToEachType(Type source, Type target, bool reportErrors, IntersectionState intersectionState)
        {
            var result = Ternary.True;
            foreach (var targetType in c.Types.Elements(c.Types.Union(target).Types))
            {
                var related = IsRelatedTo(source, targetType, RecursionFlags.Target, reportErrors, null, intersectionState);
                if (related == Ternary.False)
                {
                    return Ternary.False;
                }
                result &= related;
            }
            return result;
        }

        private int SomeTypeRelatedToType(Type source, Type target, bool reportErrors, IntersectionState intersectionState)
        {
            var sourceTypes = c.Types.Elements(c.Types.Union(source).Types);
            if ((c.Types.Flags(source) & TypeFlags.Union) != 0 && ContainsType(sourceTypes, target))
            {
                return Ternary.True;
            }
            var len = sourceTypes.Length;
            for (var i = 0; i < len; i++)
            {
                var related = IsRelatedTo(sourceTypes[i], target, RecursionFlags.Source, reportErrors && i == len - 1, null, intersectionState);
                if (related != Ternary.False)
                {
                    return related;
                }
            }
            return Ternary.False;
        }

        private Type GetUndefinedStrippedTargetIfNeeded(Type source, Type target)
        {
            if ((c.Types.Flags(source) & TypeFlags.Union) != 0 && (c.Types.Flags(target) & TypeFlags.Union) != 0 &&
                (c.Types.Flags(c.Types.Elements(c.Types.Union(source).Types)[0]) & TypeFlags.Undefined) == 0 &&
                (c.Types.Flags(c.Types.Elements(c.Types.Union(target).Types)[0]) & TypeFlags.Undefined) != 0)
            {
                return c.ExtractTypesOfKind(target, ~TypeFlags.Undefined);
            }
            return target;
        }

        private int EachTypeRelatedToType(Type source, Type target, bool reportErrors, IntersectionState intersectionState)
        {
            var result = Ternary.True;
            var sourceTypes = c.Types.Elements(c.Types.Union(source).Types);
            // We strip `undefined` from the target if the `source` trivially doesn't contain it for our correspondence-checking fastpath
            // since `undefined` is frequently added by optionality and would otherwise spoil a potentially useful correspondence
            var undefinedStrippedTarget = GetUndefinedStrippedTargetIfNeeded(source, target);
            for (var i = 0; i < sourceTypes.Length; i++)
            {
                var sourceType = sourceTypes[i];
                if ((c.Types.Flags(undefinedStrippedTarget) & TypeFlags.Union) != 0)
                {
                    var strippedTypes = c.Types.Elements(c.Types.Union(undefinedStrippedTarget).Types);
                    if (sourceTypes.Length >= strippedTypes.Length && sourceTypes.Length % strippedTypes.Length == 0)
                    {
                        // many unions are mappings of one another; in such cases, simply comparing members at the same index can shortcut the comparison
                        // such unions will have identical lengths, and their corresponding elements will match up. Another common scenario is where a large
                        // union has a union of objects intersected with it. In such cases, if the input was, eg `("a" | "b" | "c") & (string | boolean | {} | {whatever})`,
                        // the result will have the structure `"a" | "b" | "c" | "a" & {} | "b" & {} | "c" & {} | "a" & {whatever} | "b" & {whatever} | "c" & {whatever}`
                        // - the resulting union has a length which is a multiple of the original union, and the elements correspond modulo the length of the original union
                        var candidate = strippedTypes[i % strippedTypes.Length];
                        var r = IsRelatedTo(sourceType, candidate, RecursionFlags.Both, false, null, intersectionState);
                        if (r != Ternary.False)
                        {
                            result &= r;
                            continue;
                        }
                    }
                }
                var related = IsRelatedTo(sourceType, target, RecursionFlags.Source, reportErrors, null, intersectionState);
                if (related == Ternary.False)
                {
                    return Ternary.False;
                }
                result &= related;
            }
            return result;
        }

        private int TypeArgumentsRelatedTo(ReadOnlySpan<Type> sourcesSpan, ReadOnlySpan<Type> targetsSpan, VarianceFlags[] variances, bool reportErrors, IntersectionState intersectionState)
        {
            var sources = sourcesSpan.ToArray();
            var targets = targetsSpan.ToArray();
            if (sources.Length != targets.Length && IsIdentity)
            {
                return Ternary.False;
            }
            var length = sources.Length <= targets.Length ? sources.Length : targets.Length;
            var result = Ternary.True;
            for (var i = 0; i < length; i++)
            {
                // When variance information isn't available we default to covariance. This happens
                // in the process of computing variance information for recursive types and when
                // comparing 'this' type arguments.
                var varianceFlags = i < variances.Length ? variances[i] : VarianceFlags.Covariant;
                var variance = varianceFlags & VarianceFlags.VarianceMask;
                // We ignore arguments for independent type parameters (because they're never witnessed).
                if (variance != VarianceFlags.Independent)
                {
                    var s = sources[i];
                    var t = targets[i];
                    int related;
                    if ((varianceFlags & VarianceFlags.Unmeasurable) != 0)
                    {
                        // Even an `Unmeasurable` variance works out without a structural check if the source and target are _identical_.
                        // We can't simply assume invariance, because `Unmeasurable` marks nonlinear relations, for example, a relation tained by
                        // the `-?` modifier in a mapped type (where, no matter how the inputs are related, the outputs still might not be)
                        related = IsIdentity ? IsRelatedTo(s, t, RecursionFlags.Both, false) : c.CompareTypesIdentical(s, t);
                    }
                    else
                    {
                        // Propagate unreliable variance flag
                        if (c.inVarianceComputation && (varianceFlags & VarianceFlags.Unreliable) != 0)
                        {
                            c.InstantiateType(s, c.ReportUnreliableMapper);
                        }
                        if (variance == VarianceFlags.Covariant)
                        {
                            related = IsRelatedTo(s, t, RecursionFlags.Both, reportErrors, null, intersectionState);
                        }
                        else if (variance == VarianceFlags.Contravariant)
                        {
                            related = IsRelatedTo(t, s, RecursionFlags.Both, reportErrors, null, intersectionState);
                        }
                        else if (variance == VarianceFlags.Bivariant)
                        {
                            // In the bivariant case we first compare contravariantly without reporting
                            // errors. Then, if that doesn't succeed, we compare covariantly with error
                            // reporting. Thus, error elaboration will be based on the the covariant check,
                            // which is generally easier to reason about.
                            related = IsRelatedTo(t, s, RecursionFlags.Both, false);
                            if (related == Ternary.False)
                            {
                                related = IsRelatedTo(s, t, RecursionFlags.Both, reportErrors, null, intersectionState);
                            }
                        }
                        else
                        {
                            // In the invariant case we first compare covariantly, and only when that
                            // succeeds do we proceed to compare contravariantly. Thus, error elaboration
                            // will typically be based on the covariant check.
                            related = IsRelatedTo(s, t, RecursionFlags.Both, reportErrors, null, intersectionState);
                            if (related != Ternary.False)
                            {
                                related &= IsRelatedTo(t, s, RecursionFlags.Both, reportErrors, null, intersectionState);
                            }
                        }
                    }
                    if (related == Ternary.False)
                    {
                        return Ternary.False;
                    }
                    result &= related;
                }
            }
            return result;
        }

        // Determine if possibly recursive types are related. First, check if the result is already available in the global cache.
        // Second, check if we have already started a comparison of the given two types in which case we assume the result to be true.
        // Third, check if both types are part of deeply nested chains of generic type instantiations and if so assume the types are
        // equal and infinitely expanding. Fourth, if we have reached a depth of 100 nested comparisons, assume we have runaway recursion
        // and issue an error. Otherwise, actually compare the structure of the two types.
        private int RecursiveTypeRelatedTo(Type source, Type target, bool reportErrors, IntersectionState intersectionState, RecursionFlags recursionFlags)
        {
            if (overflow)
            {
                return Ternary.False;
            }
            var id = c.GetRelationKey(source, target, intersectionState, relation, ignoreConstraints: false);
            if (relation.Results.TryGetValue(id, out var entry))
            {
                if (reportErrors && (entry & RelationComparisonResult.Failed) != 0 && (entry & RelationComparisonResult.Overflow) == 0)
                {
                    // We are elaborating errors and the cached result is a failure not due to a comparison overflow,
                    // so we will do the comparison again to generate an error message.
                }
                else
                {
                    if (c.outofbandVarianceMarkerHandler is not null)
                    {
                        // We're in the middle of variance checking - integrate any unmeasurable/unreliable flags from this cached component
                        var saved = entry & RelationComparisonResult.ReportsMask;
                        if ((saved & RelationComparisonResult.ReportsUnmeasurable) != 0)
                        {
                            c.InstantiateType(source, c.ReportUnmeasurableMapper);
                        }
                        if ((saved & RelationComparisonResult.ReportsUnreliable) != 0)
                        {
                            c.InstantiateType(source, c.ReportUnreliableMapper);
                        }
                    }
                    if (reportErrors && (entry & RelationComparisonResult.Overflow) != 0)
                    {
                        var message = (entry & RelationComparisonResult.ComplexityOverflow) != 0
                            ? Diagnostics.Excessive_complexity_comparing_types_0_and_1
                            : Diagnostics.Excessive_stack_depth_comparing_types_0_and_1;
                        ReportError(message, c.TypeToString(source), c.TypeToString(target));
                        overrideNextErrorInfo++;
                    }
                    return (entry & RelationComparisonResult.Succeeded) != 0 ? Ternary.True : Ternary.False;
                }
            }
            if (relationCount <= 0)
            {
                overflow = true;
                return Ternary.False;
            }
            if (maybeKeys is null)
            {
                maybeKeys = [];
                maybeKeysSet = [];
                sourceStack = [];
                targetStack = [];
            }
            else
            {
                // If source and target are already being compared, consider them related with assumptions
                if (maybeKeysSet!.Contains(id))
                {
                    return Ternary.Maybe;
                }
                // A key that starts with "*" is an indication that we have type references that reference constrained
                // type parameters. For such keys we also check against the key we would have gotten if all type parameters
                // were unconstrained.
                if (id.HasConstraintMarker && maybeKeysSet.Contains(c.GetRelationKey(source, target, intersectionState, relation, ignoreConstraints: true)))
                {
                    return Ternary.Maybe;
                }
                if (sourceDepth == 100 || targetDepth == 100)
                {
                    overflow = true;
                    return Ternary.False;
                }
            }
            var maybeStart = maybeCount;
            SetAt(maybeKeys, maybeCount, id);
            maybeKeysSet!.Add(id);
            maybeCount++;
            var saveExpandingFlags = expandingFlags;
            if ((recursionFlags & RecursionFlags.Source) != 0)
            {
                SetAt(sourceStack, sourceDepth, source);
                sourceDepth++;
                if ((expandingFlags & ExpandingFlags.Source) == 0 && c.IsDeeplyNestedType(source, sourceStack, sourceDepth))
                {
                    expandingFlags |= ExpandingFlags.Source;
                }
            }
            if ((recursionFlags & RecursionFlags.Target) != 0)
            {
                SetAt(targetStack, targetDepth, target);
                targetDepth++;
                if ((expandingFlags & ExpandingFlags.Target) == 0 && c.IsDeeplyNestedType(target, targetStack, targetDepth))
                {
                    expandingFlags |= ExpandingFlags.Target;
                }
            }
            Action<bool>? originalHandler = null;
            var propagatingVarianceFlags = RelationComparisonResult.None;
            if (c.outofbandVarianceMarkerHandler is not null)
            {
                originalHandler = c.outofbandVarianceMarkerHandler;
                c.outofbandVarianceMarkerHandler = onlyUnreliable =>
                {
                    propagatingVarianceFlags |= onlyUnreliable ? RelationComparisonResult.ReportsUnreliable : RelationComparisonResult.ReportsUnmeasurable;
                    originalHandler(onlyUnreliable);
                };
            }
            var result = expandingFlags == ExpandingFlags.Both ? Ternary.Maybe : StructuredTypeRelatedTo(source, target, reportErrors, intersectionState);
            if (c.outofbandVarianceMarkerHandler is not null)
            {
                c.outofbandVarianceMarkerHandler = originalHandler;
            }
            if ((recursionFlags & RecursionFlags.Source) != 0)
            {
                sourceDepth--;
            }
            if ((recursionFlags & RecursionFlags.Target) != 0)
            {
                targetDepth--;
            }
            expandingFlags = saveExpandingFlags;
            if (result != Ternary.False)
            {
                if (result == Ternary.True || (sourceDepth == 0 && targetDepth == 0))
                {
                    // If result is definitely true, record all maybe keys as having succeeded. Also, record Ternary.Maybe
                    // results as having succeeded once we reach depth 0, but never record Ternary.Unknown results.
                    ResetMaybeStack(maybeStart, result == Ternary.True || result == Ternary.Maybe, propagatingVarianceFlags);
                }
                // Note: it's intentional that we don't reset in the else case;
                // we leave them on the stack such that when we hit depth zero
                // above, we can report all of them as successful.
            }
            else
            {
                // A false result goes straight into global cache (when something is false under
                // assumptions it will also be false without assumptions)
                relation.Results[id] = RelationComparisonResult.Failed | propagatingVarianceFlags;
                relationCount--;
                ResetMaybeStack(maybeStart, false, propagatingVarianceFlags);
            }
            return result;
        }

        private void ResetMaybeStack(int maybeStart, bool markAllAsSucceeded, RelationComparisonResult propagatingVarianceFlags)
        {
            for (var i = maybeStart; i < maybeCount; i++)
            {
                maybeKeysSet!.Remove(maybeKeys![i]);
                if (markAllAsSucceeded)
                {
                    relation.Results[maybeKeys[i]] = RelationComparisonResult.Succeeded | propagatingVarianceFlags;
                    relationCount--;
                }
            }
            maybeCount = maybeStart;
        }

        private static void SetAt<T>(List<T> list, int index, T value)
        {
            if (index < list.Count)
            {
                list[index] = value;
            }
            else
            {
                list.Add(value);
            }
        }

        private int StructuredTypeRelatedTo(Type source, Type target, bool reportErrors, IntersectionState intersectionState)
        {
            var saveErrorInfo = CaptureErrorCalculationState();
            var result = StructuredTypeRelatedToWorker(source, target, reportErrors, intersectionState, saveErrorInfo);
            if (!IsIdentity)
            {
                // The combined constraint of an intersection type is the intersection of the constraints of
                // the constituents. When an intersection type contains instantiable types with union type
                // constraints, there are situations where we need to examine the combined constraint. One is
                // when the target is a union type. Another is when the intersection contains types belonging
                // to one of the disjoint domains. For example, given type variables T and U, each with the
                // constraint 'string | number', the combined constraint of 'T & U' is 'string | number' and
                // we need to check this constraint against a union on the target side. Also, given a type
                // variable V constrained to 'string | number', 'V & number' has a combined constraint of
                // 'string & number | number & number' which reduces to just 'number'.
                // This also handles type parameters, as a type parameter with a union constraint compared against a union
                // needs to have its constraint hoisted into an intersection with said type parameter, this way
                // the type param can be compared with itself in the target (with the influence of its constraint to match other parts)
                // For example, if `T extends 1 | 2` and `U extends 2 | 3` and we compare `T & U` to `T & U & (1 | 2 | 3)`
                var sourceFlags = c.Types.Flags(source);
                var targetFlags = c.Types.Flags(target);
                if (result == Ternary.False && ((sourceFlags & TypeFlags.Intersection) != 0 || ((sourceFlags & TypeFlags.TypeParameter) != 0 && (targetFlags & TypeFlags.Union) != 0)))
                {
                    var constraint = c.GetEffectiveConstraintOfIntersection((sourceFlags & TypeFlags.Intersection) != 0 ? c.Types.Elements(c.Types.Union(source).Types).ToArray() : [source],
                        (targetFlags & TypeFlags.Union) != 0);
                    if (!constraint.IsNone && c.EveryType(constraint, ct => ct != source))
                    {
                        // Skip comparison if expansion contains the source itself
                        // TODO: Stack errors so we get a pyramid for the "normal" comparison above, _and_ a second for this
                        result = IsRelatedTo(constraint, target, RecursionFlags.Source, false, null, intersectionState);
                    }
                }
                // When the target is an intersection we need an extra property check in order to detect nested excess
                // properties and nested weak types. The following are motivating examples that all should be errors, but
                // aren't without this extra property check:
                //
                //   let obj: { a: { x: string } } & { c: number } = { a: { x: 'hello', y: 2 }, c: 5 };  // Nested excess property
                //
                //   declare let wrong: { a: { y: string } };
                //   let weak: { a?: { x?: number } } & { c?: string } = wrong;  // Nested weak object type
                //
                if (result != Ternary.False && (intersectionState & IntersectionState.Target) == 0 && (targetFlags & TypeFlags.Intersection) != 0 &&
                    !c.IsGenericObjectType(target) && (sourceFlags & (TypeFlags.Object | TypeFlags.Intersection)) != 0)
                {
                    result &= PropertiesRelatedTo(source, target, reportErrors, null, false, IntersectionState.None);
                    if (result != Ternary.False && c.IsObjectLiteralType(source) && (c.Types.ObjectFlagsOf(source) & ObjectFlags.FreshLiteral) != 0)
                    {
                        result &= IndexSignaturesRelatedTo(source, target, false, reportErrors, IntersectionState.None);
                    }
                }
                // When the source is an intersection we need an extra check of any optional properties in the target to
                // detect possible mismatched property types. For example:
                //
                //   function foo<T extends object>(x: { a?: string }, y: T & { a: boolean }) {
                //     x = y;  // Mismatched property in source intersection
                //   }
                //
                else if (result != Ternary.False && c.IsNonGenericObjectType(target) && !c.IsArrayOrTupleType(target) &&
                    (sourceFlags & TypeFlags.Intersection) != 0 && (c.Types.Flags(c.GetApparentType(source)) & TypeFlags.StructuredType) != 0 &&
                    !ExistsType(c.Types.Elements(c.Types.Union(source).Types), t => t == target || (c.Types.ObjectFlagsOf(t) & ObjectFlags.NonInferrableType) != 0))
                {
                    result &= PropertiesRelatedTo(source, target, reportErrors, null, true, intersectionState);
                }
            }
            if (result != Ternary.False)
            {
                ResetErrorInfo(saveErrorInfo);
            }
            return result;
        }

        private Type GetApparentMappedTypeKeys(Type nameType, Type targetType)
        {
            var modifiersType = c.GetApparentType(c.GetModifiersTypeFromMappedType(targetType));
            var mappedKeys = new List<Type>();
            c.ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType(modifiersType, TypeFlags.StringOrNumberLiteralOrUnique, false,
                t => mappedKeys.Add(c.InstantiateType(nameType, AppendTypeMapping(c.Types.Object(targetType).Mapper, c.GetTypeParameterFromMappedType(targetType), t))));
            return c.GetUnionType(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(mappedKeys));
        }

        private int StructuredTypeRelatedToWorker(Type source, Type target, bool reportErrors, IntersectionState intersectionState, ErrorCalculationState saveErrorInfo)
        {
            var w = new StructuredWorker(this, source, target, reportErrors, intersectionState, saveErrorInfo);
            return w.Run();
        }

        // structuredTypeRelatedToWorker's locals (result, originalErrorInfo, varianceCheckFailed) shared with its nested
        // functions countMessageChainBreadth and relateVariances.
        private sealed class StructuredWorker(Relater r, Type source, Type target, bool reportErrors, IntersectionState intersectionState, ErrorCalculationState saveErrorInfo)
        {
            private readonly Checker c = r.c;
            private int result;
            private DiagnosticMessageChain? originalErrorInfo;
            private bool varianceCheckFailed;

            internal int Run()
            {
                var sourceFlags = c.Types.Flags(source);
                var targetFlags = c.Types.Flags(target);
                if (r.IsIdentity)
                {
                    // We've already checked that source.flags and target.flags are identical
                    if ((sourceFlags & TypeFlags.UnionOrIntersection) != 0)
                    {
                        var identityResult = r.EachTypeRelatedToSomeType(source, target);
                        if (identityResult != Ternary.False)
                        {
                            identityResult &= r.EachTypeRelatedToSomeType(target, source);
                        }
                        return identityResult;
                    }
                    if ((sourceFlags & TypeFlags.Index) != 0)
                    {
                        return r.IsRelatedTo(c.Types.Data<IndexData>(source).Type, c.Types.Data<IndexData>(target).Type, RecursionFlags.Both, false);
                    }
                    if ((sourceFlags & TypeFlags.IndexedAccess) != 0)
                    {
                        if ((result = r.IsRelatedTo(c.Types.Data<IndexedAccessData>(source).ObjectType, c.Types.Data<IndexedAccessData>(target).ObjectType, RecursionFlags.Both, false)) != Ternary.False)
                        {
                            if ((result &= r.IsRelatedTo(c.Types.Data<IndexedAccessData>(source).IndexType, c.Types.Data<IndexedAccessData>(target).IndexType, RecursionFlags.Both, false)) != Ternary.False)
                            {
                                return result;
                            }
                        }
                    }
                    if ((sourceFlags & TypeFlags.Conditional) != 0)
                    {
                        var s = c.Types.Data<ConditionalData>(source);
                        var t = c.Types.Data<ConditionalData>(target);
                        if (s.Root.IsDistributive == t.Root.IsDistributive)
                        {
                            if ((result = r.IsRelatedTo(s.CheckType, t.CheckType, RecursionFlags.Both, false)) != Ternary.False)
                            {
                                if ((result &= r.IsRelatedTo(s.ExtendsType, t.ExtendsType, RecursionFlags.Both, false)) != Ternary.False)
                                {
                                    if ((result &= r.IsRelatedTo(c.GetTrueTypeFromConditionalType(source), c.GetTrueTypeFromConditionalType(target), RecursionFlags.Both, false)) != Ternary.False)
                                    {
                                        if ((result &= r.IsRelatedTo(c.GetFalseTypeFromConditionalType(source), c.GetFalseTypeFromConditionalType(target), RecursionFlags.Both, false)) != Ternary.False)
                                        {
                                            return result;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    if ((sourceFlags & TypeFlags.Substitution) != 0)
                    {
                        if ((result = r.IsRelatedTo(c.Types.Data<SubstitutionData>(source).BaseType, c.Types.Data<SubstitutionData>(target).BaseType, RecursionFlags.Both, false)) != Ternary.False)
                        {
                            if ((result &= r.IsRelatedTo(c.Types.Data<SubstitutionData>(source).Constraint, c.Types.Data<SubstitutionData>(target).Constraint, RecursionFlags.Both, false)) != Ternary.False)
                            {
                                return result;
                            }
                        }
                    }
                    if ((sourceFlags & TypeFlags.TemplateLiteral) != 0)
                    {
                        var s = c.Types.Data<TemplateLiteralData>(source);
                        var t = c.Types.Data<TemplateLiteralData>(target);
                        if (s.Texts.AsSpan().SequenceEqual(t.Texts))
                        {
                            result = Ternary.True;
                            for (var i = 0; i < s.Types.Length; i++)
                            {
                                if ((result &= r.IsRelatedTo(s.Types[i], t.Types[i], RecursionFlags.Both, false)) == Ternary.False)
                                {
                                    break;
                                }
                            }
                            return result;
                        }
                    }
                    if ((sourceFlags & TypeFlags.StringMapping) != 0)
                    {
                        if (c.Types.Symbol(source) == c.Types.Symbol(target))
                        {
                            return r.IsRelatedTo(c.Types.Data<StringMappingData>(source).Type, c.Types.Data<StringMappingData>(target).Type, RecursionFlags.Both, false);
                        }
                    }
                    if ((sourceFlags & TypeFlags.Object) == 0)
                    {
                        return Ternary.False;
                    }
                }
                else if ((sourceFlags & TypeFlags.UnionOrIntersection) != 0 || (targetFlags & TypeFlags.UnionOrIntersection) != 0)
                {
                    if ((result = r.UnionOrIntersectionRelatedTo(source, target, reportErrors, intersectionState)) != Ternary.False)
                    {
                        return result;
                    }
                    // The ordered decomposition above doesn't handle all cases. Specifically, we also need to handle:
                    // Source is instantiable (e.g. source has union or intersection constraint).
                    // Source is an object, target is a union (e.g. { a, b: boolean } <=> { a, b: true } | { a, b: false }).
                    // Source is an intersection, target is an object (e.g. { a } & { b } <=> { a, b }).
                    // Source is an intersection, target is a union (e.g. { a } & { b: boolean } <=> { a, b: true } | { a, b: false }).
                    // Source is an intersection, target instantiable (e.g. string & { tag } <=> T["a"] constrained to string & { tag }).
                    if (!((sourceFlags & TypeFlags.Instantiable) != 0 ||
                        ((sourceFlags & TypeFlags.Object) != 0 && (targetFlags & TypeFlags.Union) != 0) ||
                        ((sourceFlags & TypeFlags.Intersection) != 0 && (targetFlags & (TypeFlags.Object | TypeFlags.Union | TypeFlags.Instantiable)) != 0)))
                    {
                        return Ternary.False;
                    }
                }

                // We limit alias variance probing to only object and conditional types since their alias behavior
                // is more predictable than other, interned types, which may or may not have an alias depending on
                // the order in which things were checked.
                var sourceAlias = c.Types.Record(source).AliasSymbol;
                if ((sourceFlags & (TypeFlags.Object | TypeFlags.Conditional)) != 0 && !sourceAlias.IsNone && c.Types.Record(source).AliasTypeArguments.IsPresent &&
                    sourceAlias == c.Types.Record(target).AliasSymbol && !(c.IsMarkerType(source) || c.IsMarkerType(target)))
                {
                    var variances = c.GetAliasVariances(sourceAlias);
                    if (variances == EmptyVariances)
                    {
                        return Ternary.Unknown;
                    }
                    var parameters = c.GetTypeAliasTypeParameters(sourceAlias)!;
                    var minParams = c.GetMinTypeArgumentCount(parameters);
                    var aliasDeclaration = c.ValueDeclarationOf(sourceAlias);
                    var isJs = !aliasDeclaration.IsNone && Utilities.IsInJSFile(c.StoreOf(aliasDeclaration), aliasDeclaration.Node);
                    var sourceTypes = c.FillMissingTypeArguments(c.Types.Elements(c.Types.Record(source).AliasTypeArguments).ToArray(), parameters, minParams, isJs);
                    var targetTypes = c.FillMissingTypeArguments(c.Types.Elements(c.Types.Record(target).AliasTypeArguments).ToArray(), parameters, minParams, isJs);
                    var varianceResult = RelateVariances(sourceTypes, targetTypes, variances);
                    if (varianceResult is { } vr)
                    {
                        return vr;
                    }
                }

                // For a generic type T and a type U that is assignable to T, [...U] is assignable to T, U is assignable to readonly [...T],
                // and U is assignable to [...T] when U is constrained to a mutable array or tuple type.
                if ((c.IsSingleElementGenericTupleType(source) && !c.TupleTarget(source).Readonly && (result = r.IsRelatedTo(c.GetTypeArguments(source)[0], target, RecursionFlags.Source)) != Ternary.False) ||
                    (c.IsSingleElementGenericTupleType(target) && (c.TupleTarget(target).Readonly || c.IsMutableArrayOrTuple(c.GetBaseConstraintOrType(source))) &&
                        (result = r.IsRelatedTo(source, c.GetTypeArguments(target)[0], RecursionFlags.Target)) != Ternary.False))
                {
                    return result;
                }

                if ((targetFlags & TypeFlags.TypeParameter) != 0)
                {
                    // A source type { [P in Q]: X } is related to a target type T if keyof T is related to Q and X is related to T[Q].
                    if ((c.Types.ObjectFlagsOf(source) & ObjectFlags.Mapped) != 0 && c.MappedTypeDeclarationNameType(source).IsNone &&
                        r.IsRelatedTo(c.GetIndexType(target), c.GetConstraintTypeFromMappedType(source), RecursionFlags.Both) != Ternary.False)
                    {
                        if ((c.GetMappedTypeModifiers(source) & MappedTypeModifiers.IncludeOptional) == 0)
                        {
                            var templateType = c.GetTemplateTypeFromMappedType(source);
                            var indexedAccessType = c.GetIndexedAccessType(target, c.GetTypeParameterFromMappedType(source));
                            if ((result = r.IsRelatedTo(templateType, indexedAccessType, RecursionFlags.Both, reportErrors)) != Ternary.False)
                            {
                                return result;
                            }
                        }
                    }
                    if (r.relation == c.comparableRelation && (sourceFlags & TypeFlags.TypeParameter) != 0)
                    {
                        // This is a carve-out in comparability to essentially forbid comparing a type parameter
                        // with another type parameter unless one extends the other. (Remember: comparability is mostly bidirectional!)
                        var constraint = c.GetConstraintOfTypeParameter(source);
                        if (!constraint.IsNone)
                        {
                            while (!constraint.IsNone && c.SomeType(constraint, ct => (c.Types.Flags(ct) & TypeFlags.TypeParameter) != 0))
                            {
                                if ((result = r.IsRelatedTo(constraint, target, RecursionFlags.Source, false)) != Ternary.False)
                                {
                                    return result;
                                }
                                constraint = c.GetConstraintOfTypeParameter(constraint);
                            }
                        }
                        return Ternary.False;
                    }
                }
                else if ((targetFlags & TypeFlags.Index) != 0)
                {
                    var targetType = c.Types.Data<IndexData>(target).Type;
                    // A keyof S is related to a keyof T if T is related to S.
                    if ((sourceFlags & TypeFlags.Index) != 0)
                    {
                        if ((result = r.IsRelatedTo(targetType, c.Types.Data<IndexData>(source).Type, RecursionFlags.Both, false)) != Ternary.False)
                        {
                            return result;
                        }
                    }
                    if (c.IsTupleType(targetType))
                    {
                        // An index type can have a tuple type target when the tuple type contains variadic elements.
                        // Check if the source is related to the known keys of the tuple type.
                        if ((result = r.IsRelatedTo(source, c.GetKnownKeysOfTupleType(targetType), RecursionFlags.Target, reportErrors)) != Ternary.False)
                        {
                            return result;
                        }
                    }
                    else
                    {
                        // A type S is assignable to keyof T if S is assignable to keyof C, where C is the
                        // simplified form of T or, if T doesn't simplify, the constraint of T.
                        var constraint = c.GetSimplifiedTypeOrConstraint(targetType);
                        if (!constraint.IsNone)
                        {
                            // We require Ternary.True here such that circular constraints don't cause
                            // false positives. For example, given 'T extends { [K in keyof T]: string }',
                            // 'keyof T' has itself as its constraint and produces a Ternary.Maybe when
                            // related to other types.
                            if (r.IsRelatedTo(source, c.GetIndexType(constraint, c.Types.Data<IndexData>(target).IndexFlags | IndexFlags.NoReducibleCheck), RecursionFlags.Target, reportErrors) == Ternary.True)
                            {
                                return Ternary.True;
                            }
                        }
                        else if (c.IsGenericMappedType(targetType))
                        {
                            // generic mapped types that don't simplify or have a constraint still have a very simple set of keys we can compare against
                            // - their nameType or constraintType.
                            // In many ways, this comparison is a deferred version of what `getIndexTypeForMappedType` does to actually resolve the keys for _non_-generic types
                            var nameType = c.GetNameTypeFromMappedType(targetType);
                            var constraintType = c.GetConstraintTypeFromMappedType(targetType);
                            Type targetKeys;
                            if (!nameType.IsNone && c.IsMappedTypeWithKeyofConstraintDeclaration(targetType))
                            {
                                // we need to get the apparent mappings and union them with the generic mappings, since some properties may be
                                // missing from the `constraintType` which will otherwise be mapped in the object
                                var mappedKeys = r.GetApparentMappedTypeKeys(nameType, targetType);
                                // We still need to include the non-apparent (and thus still generic) keys in the target side of the comparison (in case they're in the source side)
                                targetKeys = c.GetUnionType([mappedKeys, nameType]);
                            }
                            else
                            {
                                targetKeys = !nameType.IsNone ? nameType : constraintType;
                            }
                            if (r.IsRelatedTo(source, targetKeys, RecursionFlags.Target, reportErrors) == Ternary.True)
                            {
                                return Ternary.True;
                            }
                        }
                    }
                }
                else if ((targetFlags & TypeFlags.IndexedAccess) != 0)
                {
                    if ((sourceFlags & TypeFlags.IndexedAccess) != 0)
                    {
                        // Relate components directly before falling back to constraint relationships
                        // A type S[K] is related to a type T[J] if S is related to T and K is related to J.
                        if ((result = r.IsRelatedTo(c.Types.Data<IndexedAccessData>(source).ObjectType, c.Types.Data<IndexedAccessData>(target).ObjectType, RecursionFlags.Both, reportErrors)) != Ternary.False)
                        {
                            result &= r.IsRelatedTo(c.Types.Data<IndexedAccessData>(source).IndexType, c.Types.Data<IndexedAccessData>(target).IndexType, RecursionFlags.Both, reportErrors);
                        }
                        if (result != Ternary.False)
                        {
                            return result;
                        }
                        if (reportErrors)
                        {
                            originalErrorInfo = r.errorInfo;
                        }
                    }
                    // A type S is related to a type T[K] if S is related to C, where C is the base
                    // constraint of T[K] for writing.
                    if (r.relation == c.assignableRelation || r.relation == c.comparableRelation)
                    {
                        var objectType = c.Types.Data<IndexedAccessData>(target).ObjectType;
                        var indexType = c.Types.Data<IndexedAccessData>(target).IndexType;
                        var baseObjectType = c.GetBaseConstraintOrType(objectType);
                        var baseIndexType = c.GetBaseConstraintOrType(indexType);
                        if (!c.IsGenericObjectType(baseObjectType) && !c.IsGenericIndexType(baseIndexType))
                        {
                            var accessFlags = AccessFlags.Writing | (baseObjectType != objectType ? AccessFlags.NoIndexSignatures : 0);
                            var constraint = c.GetIndexedAccessTypeOrUndefined(baseObjectType, baseIndexType, accessFlags);
                            if (!constraint.IsNone)
                            {
                                if (reportErrors && originalErrorInfo is not null)
                                {
                                    // create a new chain for the constraint error
                                    r.ResetErrorInfo(saveErrorInfo);
                                }
                                if ((result = r.IsRelatedTo(source, constraint, RecursionFlags.Target, reportErrors, null, intersectionState)) != Ternary.False)
                                {
                                    return result;
                                }
                                // prefer the shorter chain of the constraint comparison chain, and the direct comparison chain
                                if (reportErrors && originalErrorInfo is not null && r.errorInfo is not null)
                                {
                                    r.errorInfo = CountMessageChainBreadth([originalErrorInfo]) <= CountMessageChainBreadth([r.errorInfo]) ? originalErrorInfo : r.errorInfo;
                                }
                            }
                        }
                    }
                    if (reportErrors)
                    {
                        originalErrorInfo = null;
                    }
                }
                else if (c.IsGenericMappedType(target) && !r.IsIdentity)
                {
                    // Check if source type `S` is related to target type `{ [P in Q]: T }` or `{ [P in Q as R]: T}`.
                    var keysRemapped = !c.MappedTypeDeclarationNameType(target).IsNone;
                    var templateType = c.GetTemplateTypeFromMappedType(target);
                    var modifiers = c.GetMappedTypeModifiers(target);
                    if ((modifiers & MappedTypeModifiers.ExcludeOptional) == 0)
                    {
                        // If the mapped type has shape `{ [P in Q]: T[P] }`,
                        // source `S` is related to target if `T` = `S`, i.e. `S` is related to `{ [P in Q]: S[P] }`.
                        if (!keysRemapped && (c.Types.Flags(templateType) & TypeFlags.IndexedAccess) != 0 && c.Types.Data<IndexedAccessData>(templateType).ObjectType == source &&
                            c.Types.Data<IndexedAccessData>(templateType).IndexType == c.GetTypeParameterFromMappedType(target))
                        {
                            return Ternary.True;
                        }
                        if (!c.IsGenericMappedType(source))
                        {
                            // If target has shape `{ [P in Q as R]: T}`, then its keys have type `R`.
                            // If target has shape `{ [P in Q]: T }`, then its keys have type `Q`.
                            var targetKeys = keysRemapped ? c.GetNameTypeFromMappedType(target) : c.GetConstraintTypeFromMappedType(target);
                            // Type of the keys of source type `S`, i.e. `keyof S`.
                            var sourceKeys = c.GetIndexType(source, IndexFlags.NoIndexSignatures);
                            var includeOptional = (modifiers & MappedTypeModifiers.IncludeOptional) != 0;
                            var filteredByApplicability = includeOptional ? c.IntersectTypes(targetKeys, sourceKeys) : Type.None;
                            // A source type `S` is related to a target type `{ [P in Q]: T }` if `Q` is related to `keyof S` and `S[Q]` is related to `T`.
                            // A source type `S` is related to a target type `{ [P in Q as R]: T }` if `R` is related to `keyof S` and `S[R]` is related to `T.
                            // A source type `S` is related to a target type `{ [P in Q]?: T }` if some constituent `Q'` of `Q` is related to `keyof S` and `S[Q']` is related to `T`.
                            // A source type `S` is related to a target type `{ [P in Q as R]?: T }` if some constituent `R'` of `R` is related to `keyof S` and `S[R']` is related to `T`.
                            if (includeOptional
                                ? (c.Types.Flags(filteredByApplicability) & TypeFlags.Never) == 0
                                : r.IsRelatedTo(targetKeys, sourceKeys, RecursionFlags.Both) != Ternary.False)
                            {
                                var typeParameter = c.GetTypeParameterFromMappedType(target);
                                // Fastpath: When the template type has the form `Obj[P]` where `P` is the mapped type parameter, directly compare source `S` with `Obj`
                                // to avoid creating the (potentially very large) number of new intermediate types made by manufacturing `S[P]`.
                                var nonNullComponent = c.ExtractTypesOfKind(templateType, ~TypeFlags.Nullable);
                                if (!keysRemapped && (c.Types.Flags(nonNullComponent) & TypeFlags.IndexedAccess) != 0 && c.Types.Data<IndexedAccessData>(nonNullComponent).IndexType == typeParameter)
                                {
                                    if ((result = r.IsRelatedTo(source, c.Types.Data<IndexedAccessData>(nonNullComponent).ObjectType, RecursionFlags.Target, reportErrors)) != Ternary.False)
                                    {
                                        return result;
                                    }
                                }
                                else
                                {
                                    // We need to compare the type of a property on the source type `S` to the type of the same property on the target type,
                                    // so we need to construct an indexing type representing a property, and then use indexing type to index the source type for comparison.

                                    // If the target type has shape `{ [P in Q]: T }`, then a property of the target has type `P`.
                                    // If the target type has shape `{ [P in Q]?: T }`, then a property of the target has type `P`,
                                    // but the property is optional, so we only want to compare properties `P` that are common between `keyof S` and `Q`.
                                    // If the target type has shape `{ [P in Q as R]: T }`, then a property of the target has type `R`.
                                    // If the target type has shape `{ [P in Q as R]?: T }`, then a property of the target has type `R`,
                                    // but the property is optional, so we only want to compare properties `R` that are common between `keyof S` and `R`.
                                    var indexingType = keysRemapped
                                        ? (!filteredByApplicability.IsNone ? filteredByApplicability : targetKeys)
                                        : !filteredByApplicability.IsNone
                                        ? c.GetIntersectionType([filteredByApplicability, typeParameter])
                                        : typeParameter;
                                    var indexedAccessType = c.GetIndexedAccessType(source, indexingType);
                                    // Compare `S[indexingType]` to `T`, where `T` is the type of a property of the target type.
                                    if ((result = r.IsRelatedTo(indexedAccessType, templateType, RecursionFlags.Both, reportErrors)) != Ternary.False)
                                    {
                                        return result;
                                    }
                                }
                            }
                            originalErrorInfo = r.errorInfo;
                            r.ResetErrorInfo(saveErrorInfo);
                        }
                    }
                }
                else if ((targetFlags & TypeFlags.Conditional) != 0)
                {
                    // If we reach 10 levels of nesting for the same conditional type, assume it is an infinitely expanding recursive
                    // conditional type and bail out with a Ternary.Maybe result.
                    if (c.IsDeeplyNestedType(target, r.targetStack, r.targetDepth, 10))
                    {
                        return Ternary.Maybe;
                    }
                    var cond = c.Types.Data<ConditionalData>(target);
                    // We check for a relationship to a conditional type target only when the conditional type has no
                    // 'infer' positions, is not distributive or is distributive but doesn't reference the check type
                    // parameter in either of the result types, and the source isn't an instantiation of the same
                    // conditional type (as happens when computing variance).
                    if (cond.Root.InferTypeParameters is null && !c.IsDistributionDependent(cond.Root) &&
                        !((sourceFlags & TypeFlags.Conditional) != 0 && c.Types.Data<ConditionalData>(source).Root == cond.Root))
                    {
                        // Check if the conditional is always true or always false but still deferred for distribution purposes.
                        var skipTrue = !c.IsTypeAssignableTo(c.GetPermissiveInstantiation(cond.CheckType), c.GetPermissiveInstantiation(cond.ExtendsType));
                        var skipFalse = !skipTrue && c.IsTypeAssignableTo(c.GetRestrictiveInstantiation(cond.CheckType), c.GetRestrictiveInstantiation(cond.ExtendsType));
                        // TODO: Find a nice way to include potential conditional type breakdowns in error output, if they seem good (they usually don't)
                        if ((result = skipTrue ? Ternary.True : r.IsRelatedTo(source, c.GetTrueTypeFromConditionalType(target), RecursionFlags.Target, false, null, intersectionState)) != Ternary.False)
                        {
                            result &= skipFalse ? Ternary.True : r.IsRelatedTo(source, c.GetFalseTypeFromConditionalType(target), RecursionFlags.Target, false, null, intersectionState);
                            if (result != Ternary.False)
                            {
                                return result;
                            }
                        }
                    }
                }
                else if ((targetFlags & TypeFlags.TemplateLiteral) != 0)
                {
                    if ((sourceFlags & TypeFlags.TemplateLiteral) != 0)
                    {
                        if (r.relation == c.comparableRelation)
                        {
                            return c.TemplateLiteralTypesDefinitelyUnrelated(source, target) ? Ternary.False : Ternary.True;
                        }
                        // Report unreliable variance for type variables referenced in template literal type placeholders.
                        // For example, `foo-${number}` is related to `foo-${string}` even though number isn't related to string.
                        c.InstantiateType(source, c.ReportUnreliableMapper);
                    }
                    if (c.IsTypeMatchedByTemplateLiteralType(source, target))
                    {
                        return Ternary.True;
                    }
                }
                else if ((targetFlags & TypeFlags.StringMapping) != 0)
                {
                    if ((sourceFlags & TypeFlags.StringMapping) == 0)
                    {
                        if (c.IsMemberOfStringMapping(source, target))
                        {
                            return Ternary.True;
                        }
                    }
                }

                if ((sourceFlags & TypeFlags.TypeVariable) != 0)
                {
                    // IndexedAccess comparisons are handled above in the `targetFlags & TypeFlage.IndexedAccess` branch
                    if (!((sourceFlags & TypeFlags.IndexedAccess) != 0 && (targetFlags & TypeFlags.IndexedAccess) != 0))
                    {
                        var constraint = c.GetConstraintOfType(source);
                        if (constraint.IsNone)
                        {
                            constraint = c.unknownType;
                        }
                        // hi-speed no-this-instantiation check (less accurate, but avoids costly `this`-instantiation when the constraint will suffice), see #28231 for report on why this is needed
                        if ((result = r.IsRelatedTo(constraint, target, RecursionFlags.Source, false, null, intersectionState)) != Ternary.False)
                        {
                            return result;
                        }
                        // slower, fuller, this-instantiated check (necessary when comparing raw `this` types from base classes), see `subclassWithPolymorphicThisIsAssignable.ts` test for example
                        else if ((result = r.IsRelatedTo(c.GetTypeWithThisArgument(constraint, source), target, RecursionFlags.Source,
                            reportErrors && constraint != c.unknownType && (targetFlags & sourceFlags & TypeFlags.TypeParameter) == 0, null, intersectionState)) != Ternary.False)
                        {
                            return result;
                        }
                        if (c.IsMappedTypeGenericIndexedAccess(source))
                        {
                            // For an indexed access type { [P in K]: E}[X], above we have already explored an instantiation of E with X
                            // substituted for P. We also want to explore type { [P in K]: E }[C], where C is the constraint of X.
                            var indexConstraint = c.GetConstraintOfType(c.Types.Data<IndexedAccessData>(source).IndexType);
                            if (!indexConstraint.IsNone)
                            {
                                if ((result = r.IsRelatedTo(c.GetIndexedAccessType(c.Types.Data<IndexedAccessData>(source).ObjectType, indexConstraint), target, RecursionFlags.Source, reportErrors)) != Ternary.False)
                                {
                                    return result;
                                }
                            }
                        }
                    }
                }
                else if ((sourceFlags & TypeFlags.Index) != 0)
                {
                    var indexData = c.Types.Data<IndexData>(source);
                    var isDeferredMappedIndex = c.ShouldDeferIndexType(indexData.Type, indexData.IndexFlags) && (c.Types.ObjectFlagsOf(indexData.Type) & ObjectFlags.Mapped) != 0;
                    if ((result = r.IsRelatedTo(c.stringNumberSymbolType, target, RecursionFlags.Source, reportErrors && !isDeferredMappedIndex)) != Ternary.False)
                    {
                        return result;
                    }
                    if (isDeferredMappedIndex)
                    {
                        var mappedType = indexData.Type;
                        var nameType = c.GetNameTypeFromMappedType(mappedType);
                        // Unlike on the target side, on the source side we do *not* include the generic part of the `nameType`, since that comes from a
                        // (potentially anonymous) mapped type local type parameter, so that'd never assign outside the mapped type body, but we still want to
                        // allow assignments of index types of identical (or similar enough) mapped types.
                        // eg, `keyof {[X in keyof A]: Obj[X]}` should be assignable to `keyof {[Y in keyof A]: Tup[Y]}` because both map over the same set of keys (`keyof A`).
                        // Without this source-side breakdown, a `keyof {[X in keyof A]: Obj[X]}` style type won't be assignable to anything except itself, which is much too strict.
                        var sourceMappedKeys = !nameType.IsNone && c.IsMappedTypeWithKeyofConstraintDeclaration(mappedType) ? r.GetApparentMappedTypeKeys(nameType, mappedType)
                            : !nameType.IsNone ? nameType : c.GetConstraintTypeFromMappedType(mappedType);
                        if ((result = r.IsRelatedTo(sourceMappedKeys, target, RecursionFlags.Source, reportErrors)) != Ternary.False)
                        {
                            return result;
                        }
                    }
                }
                else if ((sourceFlags & TypeFlags.TemplateLiteral) != 0 && (targetFlags & TypeFlags.Object) == 0)
                {
                    if ((targetFlags & TypeFlags.TemplateLiteral) == 0)
                    {
                        var constraint = c.GetBaseConstraintOfType(source);
                        if (!constraint.IsNone && constraint != source && (result = r.IsRelatedTo(constraint, target, RecursionFlags.Source, reportErrors)) != Ternary.False)
                        {
                            return result;
                        }
                    }
                }
                else if ((sourceFlags & TypeFlags.StringMapping) != 0)
                {
                    if ((targetFlags & TypeFlags.StringMapping) != 0)
                    {
                        if (c.Types.Symbol(source) != c.Types.Symbol(target))
                        {
                            return Ternary.False;
                        }
                        if ((result = r.IsRelatedTo(c.Types.Data<StringMappingData>(source).Type, c.Types.Data<StringMappingData>(target).Type, RecursionFlags.Both, reportErrors)) != Ternary.False)
                        {
                            return result;
                        }
                    }
                    else
                    {
                        var constraint = c.GetBaseConstraintOfType(source);
                        if (!constraint.IsNone && (result = r.IsRelatedTo(constraint, target, RecursionFlags.Source, reportErrors)) != Ternary.False)
                        {
                            return result;
                        }
                    }
                }
                else if ((sourceFlags & TypeFlags.Conditional) != 0)
                {
                    // If we reach 10 levels of nesting for the same conditional type, assume it is an infinitely expanding recursive
                    // conditional type and bail out with a Ternary.Maybe result.
                    if (c.IsDeeplyNestedType(source, r.sourceStack, r.sourceDepth, 10))
                    {
                        return Ternary.Maybe;
                    }
                    if ((targetFlags & TypeFlags.Conditional) != 0)
                    {
                        // Two conditional types 'T1 extends U1 ? X1 : Y1' and 'T2 extends U2 ? X2 : Y2' are related if
                        // one of T1 and T2 is related to the other, U1 and U2 are identical types, X1 is related to X2,
                        // and Y1 is related to Y2.
                        var s = c.Types.Data<ConditionalData>(source);
                        var t = c.Types.Data<ConditionalData>(target);
                        var sourceParams = s.Root.InferTypeParameters;
                        var sourceExtends = s.ExtendsType;
                        TypeMapper? mapper = null;
                        if (sourceParams is not null)
                        {
                            // If the source has infer type parameters, we instantiate them in the context of the target
                            var ctx = c.CreateInferenceContext(sourceParams, Signature.None, InferenceFlags.None, (s, t) => r.IsRelatedToWorker(s, t, false) != Ternary.False);
                            c.InferTypes(ctx.Inferences, t.ExtendsType, sourceExtends, InferencePriority.NoConstraints | InferencePriority.AlwaysStrict);
                            sourceExtends = c.InstantiateType(sourceExtends, ctx.Mapper);
                            mapper = ctx.Mapper;
                        }
                        if (c.IsTypeIdenticalTo(sourceExtends, t.ExtendsType) &&
                            (r.IsRelatedTo(s.CheckType, t.CheckType, RecursionFlags.Both) != Ternary.False || r.IsRelatedTo(t.CheckType, s.CheckType, RecursionFlags.Both) != Ternary.False))
                        {
                            if ((result = r.IsRelatedTo(c.InstantiateType(c.GetTrueTypeFromConditionalType(source), mapper), c.GetTrueTypeFromConditionalType(target), RecursionFlags.Both, reportErrors)) != Ternary.False)
                            {
                                result &= r.IsRelatedTo(c.GetFalseTypeFromConditionalType(source), c.GetFalseTypeFromConditionalType(target), RecursionFlags.Both, reportErrors);
                            }
                            if (result != Ternary.False)
                            {
                                return result;
                            }
                        }
                    }
                    // conditionals can be related to one another via normal constraint, as, eg, `A extends B ? O : never` should be assignable to `O`
                    // when `O` is a conditional (`never` is trivially assignable to `O`, as is `O`!).
                    var defaultConstraint = c.GetDefaultConstraintOfConditionalType(source);
                    if (!defaultConstraint.IsNone)
                    {
                        if ((result = r.IsRelatedTo(defaultConstraint, target, RecursionFlags.Source, reportErrors)) != Ternary.False)
                        {
                            return result;
                        }
                    }
                    // conditionals aren't related to one another via distributive constraint as it is much too inaccurate and allows way
                    // more assignments than are desirable (since it maps the source check type to its constraint, it loses information).
                    var distributiveConstraint = (targetFlags & TypeFlags.Conditional) == 0 && c.HasNonCircularBaseConstraint(source) ? c.GetConstraintOfDistributiveConditionalType(source) : Type.None;
                    if (!distributiveConstraint.IsNone)
                    {
                        r.ResetErrorInfo(saveErrorInfo);
                        if ((result = r.IsRelatedTo(distributiveConstraint, target, RecursionFlags.Source, reportErrors)) != Ternary.False)
                        {
                            return result;
                        }
                    }
                }
                else
                {
                    // An empty object type is related to any mapped type that includes a '?' modifier.
                    if (r.relation != c.subtypeRelation && r.relation != c.strictSubtypeRelation && c.IsPartialMappedType(target) && c.IsEmptyObjectType(source))
                    {
                        return Ternary.True;
                    }
                    if (c.IsGenericMappedType(target))
                    {
                        if (c.IsGenericMappedType(source))
                        {
                            if ((result = r.MappedTypeRelatedTo(source, target, reportErrors)) != Ternary.False)
                            {
                                return result;
                            }
                        }
                        return Ternary.False;
                    }
                    var sourceIsPrimitive = (sourceFlags & TypeFlags.Primitive) != 0;
                    var structuredSource = source;
                    if (!r.IsIdentity)
                    {
                        structuredSource = c.GetApparentType(source);
                        sourceFlags = c.Types.Flags(structuredSource);
                    }
                    else if (c.IsGenericMappedType(source))
                    {
                        return Ternary.False;
                    }
                    if ((c.Types.ObjectFlagsOf(structuredSource) & ObjectFlags.Reference) != 0 && (c.Types.ObjectFlagsOf(target) & ObjectFlags.Reference) != 0 &&
                        c.Types.Object(structuredSource).Target == c.Types.Object(target).Target &&
                        !c.IsTupleType(structuredSource) && !(c.IsMarkerType(structuredSource) || c.IsMarkerType(target)))
                    {
                        // When strictNullChecks is disabled, the element type of the empty array literal is undefinedWideningType,
                        // and an empty array literal wouldn't be assignable to a `never[]` without this check.
                        if (c.IsEmptyArrayLiteralType(structuredSource))
                        {
                            return Ternary.True;
                        }
                        // We have type references to the same generic type, and the type references are not marker
                        // type references (which are intended by be compared structurally). Obtain the variance
                        // information for the type parameters and relate the type arguments accordingly.
                        var variances = c.GetVariances(c.Types.Object(structuredSource).Target);
                        // We return Ternary.Maybe for a recursive invocation of getVariances (signalled by emptyArray). This
                        // effectively means we measure variance only from type parameter occurrences that aren't nested in
                        // recursive instantiations of the generic type.
                        if (variances == EmptyVariances)
                        {
                            return Ternary.Unknown;
                        }
                        var varianceResult = RelateVariances(c.GetTypeArguments(structuredSource).ToArray(), c.GetTypeArguments(target).ToArray(), variances);
                        if (varianceResult is { } vr)
                        {
                            return vr;
                        }
                    }
                    else if (c.IsReadonlyArrayType(target) ? c.EveryType(structuredSource, c.IsArrayOrTupleType) : c.IsArrayType(target) && c.EveryType(structuredSource, t => c.IsTupleType(t) && !c.TupleTarget(t).Readonly))
                    {
                        if (!r.IsIdentity)
                        {
                            var sourceIndex = c.GetIndexTypeOfType(structuredSource, c.numberType);
                            var targetIndex = c.GetIndexTypeOfType(target, c.numberType);
                            return r.IsRelatedTo(sourceIndex.IsNone ? c.anyType : sourceIndex, targetIndex.IsNone ? c.anyType : targetIndex, RecursionFlags.Both, reportErrors);
                        }
                        // By flags alone, we know that the `target` is a readonly array while the source is a normal array or tuple
                        // or `target` is an array and source is a tuple - in both cases the types cannot be identical, by construction
                        return Ternary.False;
                    }
                    else if (c.IsGenericTupleType(structuredSource) && c.IsTupleType(target) && !c.IsGenericTupleType(target))
                    {
                        var constraint = c.GetBaseConstraintOrType(structuredSource);
                        if (constraint != structuredSource)
                        {
                            return r.IsRelatedTo(constraint, target, RecursionFlags.Source, reportErrors);
                        }
                    }
                    // A fresh empty object type is never a subtype of a non-empty object type. This ensures fresh({}) <: { [x: string]: xxx }
                    // but not vice-versa. Without this rule, those types would be mutual subtypes.
                    else if ((r.relation == c.subtypeRelation || r.relation == c.strictSubtypeRelation) && c.IsEmptyObjectType(target) &&
                        (c.Types.ObjectFlagsOf(target) & ObjectFlags.FreshLiteral) != 0 && !c.IsEmptyObjectType(structuredSource))
                    {
                        return Ternary.False;
                    }
                    // Even if relationship doesn't hold for unions, intersections, or generic type references,
                    // it may hold in a structural comparison.
                    // In a check of the form X = A & B, we will have previously checked if A relates to X or B relates
                    // to X. Failing both of those we want to check if the aggregation of A and B's members structurally
                    // relates to X. Thus, we include intersection types on the source side here.
                    if ((sourceFlags & (TypeFlags.Object | TypeFlags.Intersection)) != 0 && (targetFlags & TypeFlags.Object) != 0)
                    {
                        // Report structural errors only if we haven't reported any errors yet
                        var reportStructuralErrors = reportErrors && r.errorInfo == saveErrorInfo.ErrorInfo && !sourceIsPrimitive;
                        result = r.PropertiesRelatedTo(structuredSource, target, reportStructuralErrors, null, false, intersectionState);
                        if (result != Ternary.False)
                        {
                            result &= r.SignaturesRelatedTo(structuredSource, target, SignatureKind.Call, reportStructuralErrors, intersectionState);
                            if (result != Ternary.False)
                            {
                                result &= r.SignaturesRelatedTo(structuredSource, target, SignatureKind.Construct, reportStructuralErrors, intersectionState);
                                if (result != Ternary.False)
                                {
                                    result &= r.IndexSignaturesRelatedTo(structuredSource, target, sourceIsPrimitive, reportStructuralErrors, intersectionState);
                                }
                            }
                        }
                        if (varianceCheckFailed && result != Ternary.False)
                        {
                            r.errorInfo = originalErrorInfo ?? r.errorInfo ?? saveErrorInfo.ErrorInfo; // Use variance error (there is no structural one) and return false
                        }
                        else if (result != Ternary.False)
                        {
                            return result;
                        }
                    }
                    // If S is an object type and T is a discriminated union, S may be related to T if
                    // there exists a constituent of T for every combination of the discriminants of S
                    // with respect to T. We do not report errors here, as we will use the existing
                    // error result from checking each constituent of the union.
                    if ((sourceFlags & (TypeFlags.Object | TypeFlags.Intersection)) != 0 && (targetFlags & TypeFlags.Union) != 0)
                    {
                        var objectOnlyTarget = c.ExtractTypesOfKind(target, TypeFlags.Object | TypeFlags.Intersection | TypeFlags.Substitution);
                        if ((c.Types.Flags(objectOnlyTarget) & TypeFlags.Union) != 0)
                        {
                            var discriminatedResult = r.TypeRelatedToDiscriminatedType(structuredSource, objectOnlyTarget);
                            if (discriminatedResult != Ternary.False)
                            {
                                return discriminatedResult;
                            }
                        }
                    }
                }
                return Ternary.False;
            }

            private static int CountMessageChainBreadth(List<DiagnosticMessageChain>? info)
            {
                if (info is null)
                {
                    return 0;
                }
                var value = 0;
                foreach (var chain in info)
                {
                    value += 1 + CountMessageChainBreadth(chain.Next);
                }
                return value;
            }

            private int? RelateVariances(Type[] sourceTypeArguments, Type[] targetTypeArguments, VarianceFlags[] variances)
            {
                if ((result = r.TypeArgumentsRelatedTo(sourceTypeArguments, targetTypeArguments, variances, reportErrors, intersectionState)) != Ternary.False)
                {
                    return result;
                }
                if (Array.Exists(variances, v => (v & VarianceFlags.AllowsStructuralFallback) != 0))
                {
                    // If some type parameter was `Unmeasurable` or `Unreliable`, and we couldn't pass by assuming it was identical, then we
                    // have to allow a structural fallback check
                    // We elide the variance-based error elaborations, since those might not be too helpful, since we'll potentially
                    // be assuming identity of the type parameter.
                    originalErrorInfo = null;
                    r.ResetErrorInfo(saveErrorInfo);
                    return null;
                }
                var allowStructuralFallback = c.HasCovariantVoidArgument(targetTypeArguments, variances);
                varianceCheckFailed = !allowStructuralFallback;
                // The type arguments did not relate appropriately, but it may be because we have no variance
                // information (in which case typeArgumentsRelatedTo defaulted to covariance for all type
                // arguments). It might also be the case that the target type has a 'void' type argument for
                // a covariant type parameter that is only used in return positions within the generic type
                // (in which case any type argument is permitted on the source side). In those cases we proceed
                // with a structural comparison. Otherwise, we know for certain the instantiations aren't
                // related and we can return here.
                if (variances != EmptyVariances && !allowStructuralFallback)
                {
                    // In some cases generic types that are covariant in regular type checking mode become
                    // invariant in --strictFunctionTypes mode because one or more type parameters are used in
                    // both co- and contravariant positions. In order to make it easier to diagnose *why* such
                    // types are invariant, if any of the type parameters are invariant we reset the reported
                    // errors and instead force a structural comparison (which will include elaborations that
                    // reveal the reason).
                    // We can switch on `reportErrors` here, since varianceCheckFailed guarantees we return `False`,
                    // we can return `False` early here to skip calculating the structural error message we don't need.
                    if (varianceCheckFailed && !(reportErrors && Array.Exists(variances, v => (v & VarianceFlags.VarianceMask) == VarianceFlags.Invariant)))
                    {
                        return Ternary.False;
                    }
                    // We remember the original error information so we can restore it in case the structural
                    // comparison unexpectedly succeeds. This can happen when the structural comparison result
                    // is a Ternary.Maybe for example caused by the recursion depth limiter.
                    originalErrorInfo = r.errorInfo;
                    r.ResetErrorInfo(saveErrorInfo);
                }
                return null;
            }
        }

        // A type [P in S]: X is related to a type [Q in T]: Y if T is related to S and X' is
        // related to Y, where X' is an instantiation of X in which P is replaced with Q. Notice
        // that S and T are contra-variant whereas X and Y are co-variant.
        private int MappedTypeRelatedTo(Type source, Type target, bool reportErrors)
        {
            var modifiersRelated = relation == c.comparableRelation || (IsIdentity ? c.GetMappedTypeModifiers(source) == c.GetMappedTypeModifiers(target) :
                c.GetCombinedMappedTypeOptionality(source) <= c.GetCombinedMappedTypeOptionality(target));
            if (modifiersRelated)
            {
                var targetConstraint = c.GetConstraintTypeFromMappedType(target);
                var sourceConstraint = c.InstantiateType(c.GetConstraintTypeFromMappedType(source), c.GetCombinedMappedTypeOptionality(source) < 0 ? c.ReportUnmeasurableMapper : c.ReportUnreliableMapper);
                int result;
                if ((result = IsRelatedTo(targetConstraint, sourceConstraint, RecursionFlags.Both, reportErrors)) != Ternary.False)
                {
                    var mapper = CreateTypeMapper([c.GetTypeParameterFromMappedType(source)], [c.GetTypeParameterFromMappedType(target)]);
                    if (c.InstantiateType(c.GetNameTypeFromMappedType(source), mapper) == c.InstantiateType(c.GetNameTypeFromMappedType(target), mapper))
                    {
                        return result & IsRelatedTo(c.InstantiateType(c.GetTemplateTypeFromMappedType(source), mapper), c.GetTemplateTypeFromMappedType(target), RecursionFlags.Both, reportErrors);
                    }
                }
            }
            return Ternary.False;
        }

        private int TypeRelatedToDiscriminatedType(Type source, Type target)
        {
            // 1. Generate the combinations of discriminant properties & types 'source' can satisfy.
            //    a. If the number of combinations is above a set limit, the comparison is too complex.
            // 2. Filter 'target' to the subset of types whose discriminants exist in the matrix.
            //    a. If 'target' does not satisfy all discriminants in the matrix, 'source' is not related.
            // 3. For each type in the filtered 'target', determine if all non-discriminant properties of
            //    'target' are related to a property in 'source'.
            //
            // NOTE: See ~/tests/cases/conformance/types/typeRelationships/assignmentCompatibility/assignmentCompatWithDiscriminatedUnion.ts
            //       for examples.
            var sourceProperties = c.GetPropertiesOfType(source);
            var sourcePropertiesFiltered = c.FindDiscriminantProperties(sourceProperties, target);
            if (sourcePropertiesFiltered is null)
            {
                return Ternary.False;
            }
            // Though we could compute the number of combinations as we generate
            // the matrix, this would incur additional memory overhead due to
            // array allocations. To reduce this overhead, we first compute
            // the number of combinations to ensure we will not surpass our
            // fixed limit before incurring the cost of any allocations:
            var numCombinations = 1;
            foreach (var sourceProperty in sourcePropertiesFiltered)
            {
                numCombinations *= c.CountTypes(c.GetNonMissingTypeOfSymbol(sourceProperty));
                if (numCombinations > 25)
                {
                    // We've reached the complexity limit.
                    return Ternary.False;
                }
            }
            // Compute the set of types for each discriminant property.
            var sourceDiscriminantTypes = new Type[sourcePropertiesFiltered.Count][];
            var excludedProperties = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < sourcePropertiesFiltered.Count; i++)
            {
                var sourceProperty = sourcePropertiesFiltered[i];
                var sourcePropertyType = c.GetNonMissingTypeOfSymbol(sourceProperty);
                sourceDiscriminantTypes[i] = (c.Types.Flags(sourcePropertyType) & TypeFlags.Union) != 0
                    ? c.Types.Elements(c.Types.Union(sourcePropertyType).Types).ToArray()
                    : [sourcePropertyType];
                excludedProperties.Add(c.NameOf(sourceProperty));
            }
            // Match each combination of the cartesian product of discriminant properties to one or more
            // constituents of 'target'. If any combination does not have a match then 'source' is not relatable.
            var discriminantCombinations = CartesianProduct(sourceDiscriminantTypes);
            var matchingTypes = new List<Type>();
            var targetTypes = c.Types.Elements(c.Types.Union(target).Types).ToArray();
            foreach (var combination in discriminantCombinations)
            {
                var hasMatch = false;
                foreach (var type in targetTypes)
                {
                    var matched = true;
                    for (var i = 0; i < sourcePropertiesFiltered.Count; i++)
                    {
                        var sourceProperty = sourcePropertiesFiltered[i];
                        var targetProperty = c.GetPropertyOfType(type, c.NameIdOf(sourceProperty));
                        if (targetProperty.IsNone)
                        {
                            matched = false;
                            break;
                        }
                        if (sourceProperty == targetProperty)
                        {
                            continue;
                        }
                        // We compare the source property to the target in the context of a single discriminant type.
                        var index = i;
                        var related = PropertyRelatedTo(source, target, sourceProperty, targetProperty, _ => combination[index], false, IntersectionState.None,
                            c.strictNullChecks || relation == c.comparableRelation);
                        // If the target property could not be found, or if the properties were not related,
                        // then this constituent is not a match.
                        if (related == Ternary.False)
                        {
                            matched = false;
                            break;
                        }
                    }
                    if (!matched)
                    {
                        continue;
                    }
                    if (!matchingTypes.Contains(type))
                    {
                        matchingTypes.Add(type);
                    }
                    hasMatch = true;
                }
                if (!hasMatch)
                {
                    // We failed to match any type for this combination.
                    return Ternary.False;
                }
            }
            // Compare the remaining non-discriminant properties of each match.
            var result = Ternary.True;
            foreach (var type in matchingTypes)
            {
                result &= PropertiesRelatedTo(source, type, false, excludedProperties, false, IntersectionState.None);
                if (result != Ternary.False)
                {
                    result &= SignaturesRelatedTo(source, type, SignatureKind.Call, false, IntersectionState.None);
                    if (result != Ternary.False)
                    {
                        result &= SignaturesRelatedTo(source, type, SignatureKind.Construct, false, IntersectionState.None);
                        if (result != Ternary.False && !(c.IsTupleType(source) && c.IsTupleType(type)))
                        {
                            // Comparing numeric index types when both `source` and `type` are tuples is unnecessary as the
                            // element types should be sufficiently covered by `propertiesRelatedTo`. It also causes problems
                            // with index type assignability as the types for the excluded discriminants are still included
                            // in the index type.
                            result &= IndexSignaturesRelatedTo(source, type, false, false, IntersectionState.None);
                        }
                    }
                }
                if (result == Ternary.False)
                {
                    return result;
                }
            }
            return result;
        }

        // core.ts cartesianProduct
        private static List<Type[]> CartesianProduct(Type[][] arrays)
        {
            var result = new List<Type[]>();
            CartesianProductWorker(arrays, result, null, 0);
            return result;
        }

        private static void CartesianProductWorker(Type[][] arrays, List<Type[]> result, List<Type>? outer, int index)
        {
            foreach (var element in arrays[index])
            {
                List<Type> inner;
                if (outer is not null)
                {
                    inner = [.. outer];
                    inner.Add(element);
                }
                else
                {
                    inner = [element];
                }
                if (index == arrays.Length - 1)
                {
                    result.Add([.. inner]);
                }
                else
                {
                    CartesianProductWorker(arrays, result, inner, index + 1);
                }
            }
        }

        private Items<SymbolRef> ExcludeProperties(Items<SymbolRef> properties, HashSet<string>? excludedProperties)
        {
            if (excludedProperties is null || properties.Count == 0)
            {
                return properties;
            }
            List<SymbolRef>? result = null;
            for (var i = 0; i < properties.Count; i++)
            {
                if (!excludedProperties.Contains(c.NameOf(properties[i])))
                {
                    result?.Add(properties[i]);
                }
                else
                {
                    result ??= [.. properties.AsSpan()[..i]];
                }
            }
            return result is null ? properties : result;
        }

        private int IsPropertySymbolTypeRelated(SymbolRef sourceProp, SymbolRef targetProp, Func<SymbolRef, Type> getTypeOfSourceProperty, bool reportErrors, IntersectionState intersectionState)
        {
            var targetIsOptional = c.strictNullChecks && (c.GetCheckFlags(targetProp) & CheckFlags.Partial) != 0;
            var effectiveTarget = c.AddOptionality(c.GetNonMissingTypeOfSymbol(targetProp), isProperty: false, targetIsOptional);
            // source could resolve to `any` and that's not related to `unknown` target under strict subtype relation
            if ((c.Types.Flags(effectiveTarget) & (relation == c.strictSubtypeRelation ? TypeFlags.Any : TypeFlags.AnyOrUnknown)) != 0)
            {
                return Ternary.True;
            }
            var effectiveSource = getTypeOfSourceProperty(sourceProp);
            return IsRelatedTo(effectiveSource, effectiveTarget, RecursionFlags.Both, reportErrors, null, intersectionState);
        }

        private int PropertyRelatedTo(Type source, Type target, SymbolRef sourceProp, SymbolRef targetProp, Func<SymbolRef, Type> getTypeOfSourceProperty, bool reportErrors,
            IntersectionState intersectionState, bool skipOptional)
        {
            var sourcePropFlags = c.GetDeclarationModifierFlagsFromSymbol(sourceProp);
            var targetPropFlags = c.GetDeclarationModifierFlagsFromSymbol(targetProp);
            if ((sourcePropFlags & ModifierFlags.Private) != 0 || (targetPropFlags & ModifierFlags.Private) != 0)
            {
                if (c.ValueDeclarationOf(sourceProp) != c.ValueDeclarationOf(targetProp))
                {
                    if (reportErrors)
                    {
                        if ((sourcePropFlags & ModifierFlags.Private) != 0 && (targetPropFlags & ModifierFlags.Private) != 0)
                        {
                            ReportError(Diagnostics.Types_have_separate_declarations_of_a_private_property_0, c.SymbolToString(targetProp));
                        }
                        else
                        {
                            ReportError(Diagnostics.Property_0_is_private_in_type_1_but_not_in_type_2, c.SymbolToString(targetProp),
                                c.TypeToString((sourcePropFlags & ModifierFlags.Private) != 0 ? source : target), c.TypeToString((sourcePropFlags & ModifierFlags.Private) != 0 ? target : source));
                        }
                    }
                    return Ternary.False;
                }
            }
            else if ((targetPropFlags & ModifierFlags.Protected) != 0)
            {
                if (!c.IsValidOverrideOf(sourceProp, targetProp))
                {
                    if (reportErrors)
                    {
                        var sourceClass = c.GetDeclaringClass(sourceProp);
                        var targetClass = c.GetDeclaringClass(targetProp);
                        ReportError(Diagnostics.Property_0_is_protected_but_type_1_is_not_a_class_derived_from_2, c.SymbolToString(targetProp),
                            c.TypeToString(!sourceClass.IsNone ? sourceClass : source), c.TypeToString(!targetClass.IsNone ? targetClass : target));
                    }
                    return Ternary.False;
                }
            }
            else if ((sourcePropFlags & ModifierFlags.Protected) != 0)
            {
                if (reportErrors)
                {
                    ReportError(Diagnostics.Property_0_is_protected_in_type_1_but_public_in_type_2, c.SymbolToString(targetProp), c.TypeToString(source), c.TypeToString(target));
                }
                return Ternary.False;
            }
            // Ensure {readonly a: whatever} is not a subtype of {a: whatever},
            // while {a: whatever} is a subtype of {readonly a: whatever}.
            // This ensures the subtype relationship is ordered, and preventing declaration order
            // from deciding which type "wins" in union subtype reduction.
            // They're still assignable to one another, since `readonly` doesn't affect assignability.
            // This is only applied during the strictSubtypeRelation -- currently used in subtype reduction
            if (relation == c.strictSubtypeRelation && c.IsReadonlySymbol(sourceProp) && !c.IsReadonlySymbol(targetProp))
            {
                return Ternary.False;
            }
            // If the target comes from a partial union prop, allow `undefined` in the target type
            var related = IsPropertySymbolTypeRelated(sourceProp, targetProp, getTypeOfSourceProperty, reportErrors, intersectionState);
            if (related == Ternary.False)
            {
                if (reportErrors)
                {
                    ReportIncompatibleError(Diagnostics.Types_of_property_0_are_incompatible, c.SymbolToString(targetProp));
                }
                return Ternary.False;
            }
            // When checking for comparability, be more lenient with optional properties.
            if (!skipOptional && (c.SymbolFlagsOf(sourceProp) & SymbolFlags.Optional) != 0 && (c.SymbolFlagsOf(targetProp) & SymbolFlags.ClassMember) != 0 &&
                (c.SymbolFlagsOf(targetProp) & SymbolFlags.Optional) == 0)
            {
                // TypeScript 1.0 spec (April 2014): 3.8.3
                // S is a subtype of a type T, and T is a supertype of S if ...
                // S' and T are object types and, for each member M in T..
                // M is a property and S' contains a property N where
                // if M is a required property, N is also a required property
                // (M - property in T)
                // (N - property in S)
                if (reportErrors)
                {
                    ReportError(Diagnostics.Property_0_is_optional_in_type_1_but_required_in_type_2, c.SymbolToString(targetProp), c.TypeToString(source), c.TypeToString(target));
                }
                return Ternary.False;
            }
            return related;
        }

        // diagnosticName(factory.getDeclarationName(declaration)) with anon for a declaration without an identifier name
        private string DeclarationNameForDiagnostic(NodeRef declaration)
        {
            if (declaration.IsNone)
            {
                return "(anonymous)";
            }
            var store = c.StoreOf(declaration);
            var name = Utilities.GetNameOfDeclaration(store, declaration.Node);
            return !name.IsNone && store.Kind(name) == SyntaxKind.Identifier ? Utilities.DeclarationNameToString(store, name) : "(anonymous)";
        }

        private void ReportUnmatchedProperty(Type source, Type target, SymbolRef unmatchedProperty, bool requireOptionalProperties)
        {
            var shouldSkipElaboration = false;
            // give specific error in case where private names have the same description
            var unmatchedDeclaration = c.ValueDeclarationOf(unmatchedProperty);
            var sourceSymbol = c.Types.Symbol(source);
            if (!unmatchedDeclaration.IsNone && UtilitiesPublic.IsNamedDeclaration(c.StoreOf(unmatchedDeclaration), unmatchedDeclaration.Node))
            {
                var unmatchedName = c.StoreOf(unmatchedDeclaration).Name(unmatchedDeclaration.Node);
                if (!unmatchedName.IsNone && c.StoreOf(unmatchedDeclaration).Kind(unmatchedName) == SyntaxKind.PrivateIdentifier && !sourceSymbol.IsNone &&
                    (c.SymbolFlagsOf(sourceSymbol) & SymbolFlags.Class) != 0)
                {
                    var ustore = c.StoreOf(unmatchedDeclaration);
                    var privateIdentifierDescription = ustore.Texts.GetString(ustore.EscapedText(unmatchedName))!;
                    // getSymbolNameForPrivateIdentifier(source.symbol, description): the member of the source class
                    // declared with that private name (its symbol table key carries the class symbol's id)
                    string? symbolTableKey = null;
                    if (c.MembersOf(sourceSymbol) is { } members)
                    {
                        for (var i = 0; i < members.Count; i++)
                        {
                            var key = members.NameAt(i);
                            if (key.StartsWith("__#", StringComparison.Ordinal) && key.EndsWith("@" + privateIdentifierDescription, StringComparison.Ordinal))
                            {
                                symbolTableKey = key;
                                break;
                            }
                        }
                    }
                    if (symbolTableKey is not null && !c.GetPropertyOfType(source, symbolTableKey).IsNone)
                    {
                        var targetSymbol = c.Types.Symbol(target);
                        ReportError(Diagnostics.Property_0_in_type_1_refers_to_a_different_member_that_cannot_be_accessed_from_within_type_2,
                            privateIdentifierDescription, DeclarationNameForDiagnostic(c.ValueDeclarationOf(sourceSymbol)),
                            DeclarationNameForDiagnostic(targetSymbol.IsNone ? NodeRef.None : c.ValueDeclarationOf(targetSymbol)));
                        return;
                    }
                }
            }
            var props = c.GetUnmatchedProperties(source, target, requireOptionalProperties, false);
            if (headMessage is null || (headMessage.Code != Diagnostics.Class_0_incorrectly_implements_interface_1.Code &&
                headMessage.Code != Diagnostics.Class_0_incorrectly_implements_class_1_Did_you_mean_to_extend_1_and_inherit_its_members_as_a_subclass.Code))
            {
                shouldSkipElaboration = true; // Retain top-level error for interface implementing issues, otherwise omit it
            }
            if (props.Count == 1)
            {
                var propName = c.SymbolToString(unmatchedProperty, default, SymbolFlags.None, SymbolFormatFlags.AllowAnyNodeKind | SymbolFormatFlags.WriteComputedProps);
                var (sourceName, targetName) = c.GetTypeNamesForErrorDisplay(source, target);
                ReportError(Diagnostics.Property_0_is_missing_in_type_1_but_required_in_type_2, propName, sourceName, targetName);
                if (c.DeclarationsOf(unmatchedProperty) is { Count: > 0 } declarations)
                {
                    AssociateRelatedInfo(c.CreateRelatedForNode(declarations[0], Diagnostics._0_is_declared_here, propName));
                }
                if (shouldSkipElaboration && errorInfo is not null)
                {
                    overrideNextErrorInfo++;
                }
            }
            else if (TryElaborateArrayLikeErrors(source, target, false))
            {
                if (props.Count > 5)
                {
                    // arbitrary cutoff for too-long list form
                    ReportError(Diagnostics.Type_0_is_missing_the_following_properties_from_type_1_Colon_2_and_3_more, c.TypeToString(source), c.TypeToString(target),
                        string.Join(", ", props.GetRange(0, 4).ConvertAll(p => c.SymbolToString(p))), props.Count - 4);
                }
                else
                {
                    ReportError(Diagnostics.Type_0_is_missing_the_following_properties_from_type_1_Colon_2, c.TypeToString(source), c.TypeToString(target),
                        string.Join(", ", props.ConvertAll(p => c.SymbolToString(p))));
                }
                if (shouldSkipElaboration && errorInfo is not null)
                {
                    overrideNextErrorInfo++;
                }
            }
            // No array like or unmatched property error - just issue top level error (errorInfo = undefined)
        }

        private int PropertiesRelatedTo(Type source, Type target, bool reportErrors, HashSet<string>? excludedProperties, bool optionalsOnly, IntersectionState intersectionState)
        {
            if (IsIdentity)
            {
                return PropertiesIdenticalTo(source, target, excludedProperties);
            }
            var result = Ternary.True;
            if (c.IsTupleType(target))
            {
                if (c.IsArrayOrTupleType(source))
                {
                    var targetTarget = c.TupleTarget(target);
                    if (!targetTarget.Readonly && (c.IsReadonlyArrayType(source) || (c.IsTupleType(source) && c.TupleTarget(source).Readonly)))
                    {
                        return Ternary.False;
                    }
                    var sourceArity = c.GetTypeReferenceArity(source);
                    var targetArity = c.GetTypeReferenceArity(target);
                    var sourceRestFlag = c.IsTupleType(source) ? c.TupleTarget(source).CombinedFlags & ElementFlags.Rest : ElementFlags.Rest;
                    var targetHasRestElement = (targetTarget.CombinedFlags & ElementFlags.Variable) != 0;
                    var sourceMinLength = c.IsTupleType(source) ? c.TupleTarget(source).MinLength : 0;
                    var targetMinLength = targetTarget.MinLength;
                    if (sourceRestFlag == 0 && sourceArity < targetMinLength)
                    {
                        if (reportErrors)
                        {
                            ReportError(Diagnostics.Source_has_0_element_s_but_target_requires_1, sourceArity, targetMinLength);
                        }
                        return Ternary.False;
                    }
                    if (!targetHasRestElement && targetArity < sourceMinLength)
                    {
                        if (reportErrors)
                        {
                            ReportError(Diagnostics.Source_has_0_element_s_but_target_allows_only_1, sourceMinLength, targetArity);
                        }
                        return Ternary.False;
                    }
                    if (!targetHasRestElement && (sourceRestFlag != 0 || targetArity < sourceArity))
                    {
                        if (reportErrors)
                        {
                            if (sourceMinLength < targetMinLength)
                            {
                                ReportError(Diagnostics.Target_requires_0_element_s_but_source_may_have_fewer, targetMinLength);
                            }
                            else
                            {
                                ReportError(Diagnostics.Target_allows_only_0_element_s_but_source_may_have_more, targetArity);
                            }
                        }
                        return Ternary.False;
                    }
                    var sourceTypeArguments = c.GetTypeArguments(source).ToArray();
                    var targetTypeArguments = c.GetTypeArguments(target).ToArray();
                    var targetStartCount = GetStartElementCount(targetTarget, ElementFlags.NonRest);
                    var targetEndCount = GetEndElementCount(targetTarget, ElementFlags.NonRest);
                    var canExcludeDiscriminants = excludedProperties is not null;
                    for (var sourcePosition = 0; sourcePosition < sourceArity; sourcePosition++)
                    {
                        var sourceFlags = c.IsTupleType(source) ? c.TupleTarget(source).ElementFlags![sourcePosition] : ElementFlags.Rest;
                        var sourcePositionFromEnd = sourceArity - 1 - sourcePosition;
                        var targetPosition = targetHasRestElement && sourcePosition >= targetStartCount
                            ? targetArity - 1 - Math.Min(sourcePositionFromEnd, targetEndCount)
                            : sourcePosition;
                        // (a position outside the target, e.g. -1 for two variadic target elements, reads undefined in
                        // TypeScript: no flags)
                        var targetFlags = targetPosition >= 0 && targetPosition < targetTarget.ElementFlags!.Length ? targetTarget.ElementFlags[targetPosition] : ElementFlags.None;
                        if ((targetFlags & ElementFlags.Variadic) != 0 && (sourceFlags & ElementFlags.Variadic) == 0)
                        {
                            if (reportErrors)
                            {
                                ReportError(Diagnostics.Source_provides_no_match_for_variadic_element_at_position_0_in_target, targetPosition);
                            }
                            return Ternary.False;
                        }
                        if ((sourceFlags & ElementFlags.Variadic) != 0 && (targetFlags & ElementFlags.Variable) == 0)
                        {
                            if (reportErrors)
                            {
                                ReportError(Diagnostics.Variadic_element_at_position_0_in_source_does_not_match_element_at_position_1_in_target, sourcePosition, targetPosition);
                            }
                            return Ternary.False;
                        }
                        if ((targetFlags & ElementFlags.Required) != 0 && (sourceFlags & ElementFlags.Required) == 0)
                        {
                            if (reportErrors)
                            {
                                ReportError(Diagnostics.Source_provides_no_match_for_required_element_at_position_0_in_target, targetPosition);
                            }
                            return Ternary.False;
                        }
                        // We can only exclude discriminant properties if we have not yet encountered a variable-length element.
                        if (canExcludeDiscriminants)
                        {
                            if ((sourceFlags & ElementFlags.Variable) != 0 || (targetFlags & ElementFlags.Variable) != 0)
                            {
                                canExcludeDiscriminants = false;
                            }
                            if (canExcludeDiscriminants && excludedProperties!.Contains(sourcePosition.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                            {
                                continue;
                            }
                        }
                        var sourceType = c.RemoveMissingType(sourceTypeArguments[sourcePosition], (sourceFlags & targetFlags & ElementFlags.Optional) != 0);
                        var targetType = targetTypeArguments[targetPosition];
                        var targetCheckType = (sourceFlags & ElementFlags.Variadic) != 0 && (targetFlags & ElementFlags.Rest) != 0 ? c.CreateArrayType(targetType) :
                            c.RemoveMissingType(targetType, (targetFlags & ElementFlags.Optional) != 0);
                        var related = IsRelatedTo(sourceType, targetCheckType, RecursionFlags.Both, reportErrors, null, intersectionState);
                        if (related == Ternary.False)
                        {
                            if (reportErrors && (targetArity > 1 || sourceArity > 1))
                            {
                                if (targetHasRestElement && sourcePosition >= targetStartCount && sourcePositionFromEnd >= targetEndCount && targetStartCount != sourceArity - targetEndCount - 1)
                                {
                                    ReportIncompatibleError(Diagnostics.Type_at_positions_0_through_1_in_source_is_not_compatible_with_type_at_position_2_in_target, targetStartCount, sourceArity - targetEndCount - 1, targetPosition);
                                }
                                else
                                {
                                    ReportIncompatibleError(Diagnostics.Type_at_position_0_in_source_is_not_compatible_with_type_at_position_1_in_target, sourcePosition, targetPosition);
                                }
                            }
                            return Ternary.False;
                        }
                        result &= related;
                    }
                    return result;
                }
                if ((c.TupleTarget(target).CombinedFlags & ElementFlags.Variable) != 0)
                {
                    return Ternary.False;
                }
            }
            var requireOptionalProperties = (relation == c.subtypeRelation || relation == c.strictSubtypeRelation) && !c.IsObjectLiteralType(source) && !c.IsEmptyArrayLiteralType(source) && !c.IsTupleType(source);
            var unmatchedProperty = c.GetUnmatchedProperty(source, target, requireOptionalProperties, false);
            if (!unmatchedProperty.IsNone)
            {
                if (reportErrors && ShouldReportUnmatchedPropertyError(source, target))
                {
                    ReportUnmatchedProperty(source, target, unmatchedProperty, requireOptionalProperties);
                }
                return Ternary.False;
            }
            if (c.IsObjectLiteralType(target))
            {
                foreach (var sourceProp in ExcludeProperties(c.GetPropertiesOfType(source), excludedProperties))
                {
                    if (c.GetPropertyOfObjectType(target, c.NameIdOf(sourceProp)).IsNone)
                    {
                        if (reportErrors)
                        {
                            ReportError(Diagnostics.Property_0_does_not_exist_on_type_1, c.SymbolToString(sourceProp), c.TypeToString(target));
                        }
                        return Ternary.False;
                    }
                }
            }
            // We only call this for union target types when we're attempting to do excess property checking - in those cases, we want to get _all possible props_
            // from the target union, across all members
            var properties = c.GetPropertiesOfType(target);
            var numericNamesOnly = c.IsTupleType(source) && c.IsTupleType(target);
            foreach (var targetProp in ExcludeProperties(properties, excludedProperties))
            {
                var name = c.NameOf(targetProp);
                if ((c.SymbolFlagsOf(targetProp) & SymbolFlags.Prototype) == 0 && (!numericNamesOnly || IsNumericLiteralName(name) || name == "length") &&
                    (!optionalsOnly || (c.SymbolFlagsOf(targetProp) & SymbolFlags.Optional) != 0))
                {
                    var sourceProp = c.GetPropertyOfType(source, name);
                    if (!sourceProp.IsNone && sourceProp != targetProp)
                    {
                        var related = PropertyRelatedTo(source, target, sourceProp, targetProp, c.GetNonMissingTypeOfSymbol, reportErrors, intersectionState, relation == c.comparableRelation);
                        if (related == Ternary.False)
                        {
                            return Ternary.False;
                        }
                        result &= related;
                    }
                }
            }
            return result;
        }

        private int PropertiesIdenticalTo(Type source, Type target, HashSet<string>? excludedProperties)
        {
            if (!((c.Types.Flags(source) & TypeFlags.Object) != 0 && (c.Types.Flags(target) & TypeFlags.Object) != 0))
            {
                return Ternary.False;
            }
            var sourceProperties = ExcludeProperties(c.GetPropertiesOfObjectType(source), excludedProperties);
            var targetProperties = ExcludeProperties(c.GetPropertiesOfObjectType(target), excludedProperties);
            if (sourceProperties.Count != targetProperties.Count)
            {
                return Ternary.False;
            }
            var result = Ternary.True;
            foreach (var sourceProp in sourceProperties)
            {
                var targetProp = c.GetPropertyOfObjectType(target, c.NameIdOf(sourceProp));
                if (targetProp.IsNone)
                {
                    return Ternary.False;
                }
                var related = c.CompareProperties(sourceProp, targetProp, (s, t) => IsRelatedTo(s, t));
                if (related == Ternary.False)
                {
                    return Ternary.False;
                }
                result &= related;
            }
            return result;
        }

        private int SignaturesRelatedTo(Type source, Type target, SignatureKind kind, bool reportErrors, IntersectionState intersectionState)
        {
            if (IsIdentity)
            {
                return SignaturesIdenticalTo(source, target, kind);
            }
            // With respect to signatures, the anyFunctionType wildcard is a subtype of every other function type.
            if (source == c.anyFunctionType)
            {
                return Ternary.True;
            }
            if (target == c.anyFunctionType)
            {
                return Ternary.False;
            }
            var sourceSymbol = c.Types.Symbol(source);
            var targetSymbol = c.Types.Symbol(target);
            var sourceIsJSConstructor = !sourceSymbol.IsNone && c.IsJSConstructor(c.ValueDeclarationOf(sourceSymbol));
            var targetIsJSConstructor = !targetSymbol.IsNone && c.IsJSConstructor(c.ValueDeclarationOf(targetSymbol));
            var sourceSignatures = c.GetSignaturesOfType(source, sourceIsJSConstructor && kind == SignatureKind.Construct ? SignatureKind.Call : kind);
            var targetSignatures = c.GetSignaturesOfType(target, targetIsJSConstructor && kind == SignatureKind.Construct ? SignatureKind.Call : kind);
            if (kind == SignatureKind.Construct && sourceSignatures.Count > 0 && targetSignatures.Count > 0)
            {
                var sourceIsAbstract = (c.Signatures.Record(sourceSignatures[0]).Flags & SignatureFlags.Abstract) != 0;
                var targetIsAbstract = (c.Signatures.Record(targetSignatures[0]).Flags & SignatureFlags.Abstract) != 0;
                if (sourceIsAbstract && !targetIsAbstract)
                {
                    // An abstract constructor type is not assignable to a non-abstract constructor type
                    // as it would otherwise be possible to new an abstract class. Note that the assignability
                    // check we perform for an extends clause excludes construct signatures from the target,
                    // so this check never proceeds.
                    if (reportErrors)
                    {
                        ReportError(Diagnostics.Cannot_assign_an_abstract_constructor_type_to_a_non_abstract_constructor_type);
                    }
                    return Ternary.False;
                }
                if (!ConstructorVisibilitiesAreCompatible(sourceSignatures[0], targetSignatures[0], reportErrors))
                {
                    return Ternary.False;
                }
            }
            var result = Ternary.True;
            var sourceObjectFlags = c.Types.ObjectFlagsOf(source);
            var targetObjectFlags = c.Types.ObjectFlagsOf(target);
            if (((sourceObjectFlags & ObjectFlags.Instantiated) != 0 && (targetObjectFlags & ObjectFlags.Instantiated) != 0 && sourceSymbol == targetSymbol) ||
                ((sourceObjectFlags & ObjectFlags.Reference) != 0 && (targetObjectFlags & ObjectFlags.Reference) != 0 && c.Types.Object(source).Target == c.Types.Object(target).Target))
            {
                // We have instantiations of the same anonymous type (which typically will be the type of a
                // method). Simply do a pairwise comparison of the signatures in the two signature lists instead
                // of the much more expensive N * M comparison matrix we explore below. We erase type parameters
                // as they are known to always be the same.
                for (var i = 0; i < targetSignatures.Count && i < sourceSignatures.Count; i++)
                {
                    var related = SignatureRelatedTo(sourceSignatures[i], targetSignatures[i], true, reportErrors, intersectionState, IncompatibleReporter(kind, sourceSignatures[i], targetSignatures[i]));
                    if (related == Ternary.False)
                    {
                        return Ternary.False;
                    }
                    result &= related;
                }
            }
            else if (sourceSignatures.Count == 1 && targetSignatures.Count == 1)
            {
                // For simple functions (functions with a single signature) we only erase type parameters for
                // the comparable relation. Otherwise, if the source signature is generic, we instantiate it
                // in the context of the target signature before checking the relationship. Ideally we'd do
                // this regardless of the number of signatures, but the potential costs are prohibitive due
                // to the quadratic nature of the logic below.
                var eraseGenerics = relation == c.comparableRelation;
                var sourceSignature = sourceSignatures[0];
                var targetSignature = targetSignatures[0];
                result = SignatureRelatedTo(sourceSignature, targetSignature, eraseGenerics, reportErrors, intersectionState, IncompatibleReporter(kind, sourceSignature, targetSignature));
                var targetSignatureDeclaration = c.Signatures.Record(targetSignature).Declaration;
                var sourceSignatureDeclaration = c.Signatures.Record(sourceSignature).Declaration;
                if (result == Ternary.False && reportErrors && kind == SignatureKind.Construct && (sourceObjectFlags & targetObjectFlags) != 0 &&
                    ((!targetSignatureDeclaration.IsNone && c.Kind(targetSignatureDeclaration) == SyntaxKind.Constructor) ||
                     (!sourceSignatureDeclaration.IsNone && c.Kind(sourceSignatureDeclaration) == SyntaxKind.Constructor)))
                {
                    ReportError(Diagnostics.Type_0_is_not_assignable_to_type_1, c.SignatureToString(sourceSignature, default, TypeFormatFlags.WriteArrowStyleSignature, kind),
                        c.SignatureToString(targetSignature, default, TypeFormatFlags.WriteArrowStyleSignature, kind));
                    ReportError(Diagnostics.Types_of_construct_signatures_are_incompatible);
                    return result;
                }
            }
            else
            {
                foreach (var t in targetSignatures)
                {
                    var saveErrorInfo = CaptureErrorCalculationState();
                    // Only elaborate errors from the first failure
                    var shouldElaborateErrors = reportErrors;
                    var matched = false;
                    foreach (var s in sourceSignatures)
                    {
                        var related = SignatureRelatedTo(s, t, true, shouldElaborateErrors, intersectionState, IncompatibleReporter(kind, s, t));
                        if (related != Ternary.False)
                        {
                            result &= related;
                            ResetErrorInfo(saveErrorInfo);
                            matched = true;
                            break;
                        }
                        shouldElaborateErrors = false;
                    }
                    if (matched)
                    {
                        continue;
                    }
                    if (shouldElaborateErrors)
                    {
                        ReportError(Diagnostics.Type_0_provides_no_match_for_the_signature_1, c.TypeToString(source), c.SignatureToString(t, default, TypeFormatFlags.None, kind));
                    }
                    return Ternary.False;
                }
            }
            return result;
        }

        private bool ShouldReportUnmatchedPropertyError(Type source, Type target)
        {
            var typeCallSignatures = c.GetSignaturesOfStructuredType(source, SignatureKind.Call);
            var typeConstructSignatures = c.GetSignaturesOfStructuredType(source, SignatureKind.Construct);
            var typeProperties = c.GetPropertiesOfObjectType(source);
            if ((typeCallSignatures.Count > 0 || typeConstructSignatures.Count > 0) && typeProperties.Count == 0)
            {
                // target has similar signature kinds to source, still focus on the unmatched property
                return (c.GetSignaturesOfType(target, SignatureKind.Call).Count > 0 && typeCallSignatures.Count > 0) ||
                    (c.GetSignaturesOfType(target, SignatureKind.Construct).Count > 0 && typeConstructSignatures.Count > 0);
            }
            return true;
        }

        // reportIncompatibleCallSignatureReturn / reportIncompatibleConstructSignatureReturn
        private Action<Type, Type> IncompatibleReporter(SignatureKind kind, Signature siga, Signature sigb)
        {
            var noArguments = c.ParametersOf(siga).Length == 0 && c.ParametersOf(sigb).Length == 0;
            var message = kind == SignatureKind.Construct
                ? noArguments ? Diagnostics.Construct_signatures_with_no_arguments_have_incompatible_return_types_0_and_1 : Diagnostics.Construct_signature_return_types_0_and_1_are_incompatible
                : noArguments ? Diagnostics.Call_signatures_with_no_arguments_have_incompatible_return_types_0_and_1 : Diagnostics.Call_signature_return_types_0_and_1_are_incompatible;
            return (source, target) => ReportIncompatibleError(message, c.TypeToString(source), c.TypeToString(target));
        }

        private int SignatureRelatedTo(Signature source, Signature target, bool erase, bool reportErrors, IntersectionState intersectionState, Action<Type, Type> incompatibleReporter)
        {
            var checkMode = relation == c.subtypeRelation ? SignatureCheckMode.StrictTopSignature :
                relation == c.strictSubtypeRelation ? SignatureCheckMode.StrictTopSignature | SignatureCheckMode.StrictArity :
                SignatureCheckMode.None;
            return c.CompareSignaturesRelated(erase ? c.GetErasedSignature(source) : source, erase ? c.GetErasedSignature(target) : target, checkMode, reportErrors,
                ReportErrorArray, incompatibleReporter, (s, t, report) => IsRelatedTo(s, t, RecursionFlags.Both, report, null, intersectionState), c.ReportUnreliableMapper);
        }

        private int SignaturesIdenticalTo(Type source, Type target, SignatureKind kind)
        {
            var sourceSignatures = c.GetSignaturesOfType(source, kind);
            var targetSignatures = c.GetSignaturesOfType(target, kind);
            if (sourceSignatures.Count != targetSignatures.Count)
            {
                return Ternary.False;
            }
            var result = Ternary.True;
            for (var i = 0; i < sourceSignatures.Count; i++)
            {
                var related = c.CompareSignaturesIdentical(sourceSignatures[i], targetSignatures[i], false, false, false, (s, t) => IsRelatedTo(s, t));
                if (related == Ternary.False)
                {
                    return Ternary.False;
                }
                result &= related;
            }
            return result;
        }

        private int MembersRelatedToIndexInfo(Type source, IndexInfo targetInfo, bool reportErrors, IntersectionState intersectionState)
        {
            var result = Ternary.True;
            var keyType = targetInfo.KeyType;
            var props = (c.Types.Flags(source) & TypeFlags.Intersection) != 0 ? c.GetPropertiesOfUnionOrIntersectionType(source) : c.GetPropertiesOfObjectType(source);
            foreach (var prop in props)
            {
                // Skip over ignored JSX and symbol-named members
                if (c.IsIgnoredJsxProperty(source, prop))
                {
                    continue;
                }
                if (c.IsApplicableIndexType(c.GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique), keyType))
                {
                    var propType = c.GetNonMissingTypeOfSymbol(prop);
                    var type = c.exactOptionalPropertyTypes || (c.Types.Flags(propType) & TypeFlags.Undefined) != 0 || keyType == c.numberType || (c.SymbolFlagsOf(prop) & SymbolFlags.Optional) == 0
                        ? propType
                        : c.GetTypeWithFacts(propType, TypeFacts.NEUndefined);
                    var related = IsRelatedTo(type, targetInfo.Type, RecursionFlags.Both, reportErrors, null, intersectionState);
                    if (related == Ternary.False)
                    {
                        if (reportErrors)
                        {
                            ReportError(Diagnostics.Property_0_is_incompatible_with_index_signature, c.SymbolToString(prop));
                        }
                        return Ternary.False;
                    }
                    result &= related;
                }
            }
            foreach (var info in c.GetIndexInfosOfType(source))
            {
                if (c.IsApplicableIndexType(info.KeyType, keyType))
                {
                    var related = IndexInfoRelatedTo(info, targetInfo, reportErrors, intersectionState);
                    if (related == Ternary.False)
                    {
                        return Ternary.False;
                    }
                    result &= related;
                }
            }
            return result;
        }

        private int IndexInfoRelatedTo(IndexInfo sourceInfo, IndexInfo targetInfo, bool reportErrors, IntersectionState intersectionState)
        {
            var related = IsRelatedTo(sourceInfo.Type, targetInfo.Type, RecursionFlags.Both, reportErrors, null, intersectionState);
            if (related == Ternary.False && reportErrors)
            {
                if (sourceInfo.KeyType == targetInfo.KeyType)
                {
                    ReportError(Diagnostics._0_index_signatures_are_incompatible, c.TypeToString(sourceInfo.KeyType));
                }
                else
                {
                    ReportError(Diagnostics._0_and_1_index_signatures_are_incompatible, c.TypeToString(sourceInfo.KeyType), c.TypeToString(targetInfo.KeyType));
                }
            }
            return related;
        }

        private int IndexSignaturesRelatedTo(Type source, Type target, bool sourceIsPrimitive, bool reportErrors, IntersectionState intersectionState)
        {
            if (IsIdentity)
            {
                return IndexSignaturesIdenticalTo(source, target);
            }
            var indexInfos = c.GetIndexInfosOfType(target);
            var targetHasStringIndex = indexInfos.Exists(info => info.KeyType == c.stringType);
            var result = Ternary.True;
            foreach (var targetInfo in indexInfos)
            {
                var related = relation != c.strictSubtypeRelation && !sourceIsPrimitive && targetHasStringIndex && (c.Types.Flags(targetInfo.Type) & TypeFlags.Any) != 0 ? Ternary.True :
                    c.IsGenericMappedType(source) && targetHasStringIndex ? IsRelatedTo(c.GetTemplateTypeFromMappedType(source), targetInfo.Type, RecursionFlags.Both, reportErrors) :
                    TypeRelatedToIndexInfo(source, targetInfo, reportErrors, intersectionState);
                if (related == Ternary.False)
                {
                    return Ternary.False;
                }
                result &= related;
            }
            return result;
        }

        private int TypeRelatedToIndexInfo(Type source, IndexInfo targetInfo, bool reportErrors, IntersectionState intersectionState)
        {
            var sourceInfo = c.GetApplicableIndexInfo(source, targetInfo.KeyType);
            if (sourceInfo is { } info)
            {
                return IndexInfoRelatedTo(info, targetInfo, reportErrors, intersectionState);
            }
            // Intersection constituents are never considered to have an inferred index signature. Also, in the strict subtype relation,
            // only fresh object literals are considered to have inferred index signatures. This ensures { [x: string]: xxx } <: {} but
            // not vice-versa. Without this rule, those types would be mutual strict subtypes.
            if ((intersectionState & IntersectionState.Source) == 0 && (relation != c.strictSubtypeRelation || (c.Types.ObjectFlagsOf(source) & ObjectFlags.FreshLiteral) != 0) &&
                c.IsObjectTypeWithInferableIndex(source))
            {
                return MembersRelatedToIndexInfo(source, targetInfo, reportErrors, intersectionState);
            }
            if (reportErrors)
            {
                ReportError(Diagnostics.Index_signature_for_type_0_is_missing_in_type_1, c.TypeToString(targetInfo.KeyType), c.TypeToString(source));
            }
            return Ternary.False;
        }

        private int IndexSignaturesIdenticalTo(Type source, Type target)
        {
            var sourceInfos = c.GetIndexInfosOfType(source);
            var targetInfos = c.GetIndexInfosOfType(target);
            if (sourceInfos.Count != targetInfos.Count)
            {
                return Ternary.False;
            }
            foreach (var targetInfo in targetInfos)
            {
                var sourceInfo = c.GetIndexInfoOfType(source, targetInfo.KeyType);
                if (!(sourceInfo is not null && IsRelatedTo(sourceInfo.Type, targetInfo.Type, RecursionFlags.Both) != Ternary.False && sourceInfo.IsReadonly == targetInfo.IsReadonly))
                {
                    return Ternary.False;
                }
            }
            return Ternary.True;
        }

        private bool ConstructorVisibilitiesAreCompatible(Signature sourceSignature, Signature targetSignature, bool reportErrors)
        {
            var sourceDeclaration = c.Signatures.Record(sourceSignature).Declaration;
            var targetDeclaration = c.Signatures.Record(targetSignature).Declaration;
            if (sourceDeclaration.IsNone || targetDeclaration.IsNone)
            {
                return true;
            }
            var sourceAccessibility = c.GetSelectedEffectiveModifierFlags(sourceDeclaration, ModifierFlags.NonPublicAccessibilityModifier);
            var targetAccessibility = c.GetSelectedEffectiveModifierFlags(targetDeclaration, ModifierFlags.NonPublicAccessibilityModifier);
            // A public, protected and private signature is assignable to a private signature.
            if (targetAccessibility == ModifierFlags.Private)
            {
                return true;
            }
            // A public and protected signature is assignable to a protected signature.
            if (targetAccessibility == ModifierFlags.Protected && sourceAccessibility != ModifierFlags.Private)
            {
                return true;
            }
            // Only a public signature is assignable to public signature.
            if (targetAccessibility != ModifierFlags.Protected && sourceAccessibility == 0)
            {
                return true;
            }
            if (reportErrors)
            {
                ReportError(Diagnostics.Cannot_assign_a_0_constructor_type_to_a_1_constructor_type, VisibilityToString(sourceAccessibility), VisibilityToString(targetAccessibility));
            }
            return false;
        }

        // utilities.ts visibilityToString
        private static string VisibilityToString(ModifierFlags flags) =>
            flags == ModifierFlags.Private ? "private" : flags == ModifierFlags.Protected ? "protected" : "public";
    }

    /// <summary>utilities.ts concatenateDiagnosticMessageChains.</summary>
    internal static void ConcatenateDiagnosticMessageChains(DiagnosticMessageChain headChain, DiagnosticMessageChain tailChain)
    {
        var lastChain = headChain;
        while (lastChain.Next is { Count: > 0 } next)
        {
            lastChain = next[0];
        }
        lastChain.Next = [tailChain];
    }

    /// <summary>utilities.ts createDiagnosticForNodeFromMessageChain.</summary>
    internal ProgramDiagnostic CreateDiagnosticForNodeFromMessageChain(NodeRef node, DiagnosticMessageChain messageChain, List<DiagnosticRelatedInformation>? relatedInformation)
    {
        var file = FileAt(node.File);
        var span = Utilities.GetErrorSpanForNode(file.Store, file.Root, node.Node);
        return new ProgramDiagnostic
        {
            SourceFile = file,
            File = file.Root,
            StartOrUndefined = span.Start,
            LengthOrUndefined = span.Length,
            Code = messageChain.Code,
            Category = messageChain.Category,
            MessageText = messageChain.MessageText,
            Chain = messageChain.Next is not null ? messageChain : null,
            RelatedInformation = relatedInformation,
            CanonicalHead = messageChain.CanonicalHead,
        };
    }
}
