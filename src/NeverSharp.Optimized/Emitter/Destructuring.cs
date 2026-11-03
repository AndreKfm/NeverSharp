// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/transformers/destructuring.ts (flattenDestructuringAssignment for a variable declaration,
// flattenBindingOrAssignmentElement, flattenObjectBindingOrAssignmentPattern, flattenArrayBindingOrAssignmentPattern,
// createDefaultValueCheck, createDestructuringPropertyAccess, ensureIdentifier) and src/compiler/factory/utilities.ts
// (getInitializerOfBindingOrAssignmentElement, getRestIndicatorOfBindingOrAssignmentElement,
// tryGetPropertyNameOfBindingOrAssignmentElement, getPropertyNameOfBindingOrAssignmentElement)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Modified: translated from TypeScript to C#; module D2: only the path the TypeScript transformer takes for exported
// variables of a namespace (`export var [a, b] = ...`): a variable declaration with a binding pattern, FlattenLevel.All,
// hoisted temporaries, an assignment callback, no downlevelIteration. Destructuring assignments, flattenDestructuringBinding
// and FlattenLevel.ObjectRest (the class fields, ES decorators and ESNext transformers', module D4) are not ported;
// an object rest element needs the __rest helper, which the default options' transformers do not otherwise request,
// and throws NotSupportedException.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>destructuring.ts FlattenLevel.</summary>
internal enum FlattenLevel
{
    All,
    ObjectRest,
}

/// <summary>destructuring.ts.</summary>
internal static class Destructuring
{
    // destructuring.ts FlattenContext (with the closure of flattenDestructuringAssignment).
    private sealed class FlattenContext
    {
        public required TransformationContext Context;
        public required FlattenLevel Level;
        public required bool HoistTempVariables;
        public required Func<Node, VisitResult> Visitor;
        public required Func<Node, Node, Node, Node> CreateAssignmentCallback;
        public List<Node>? Expressions;

        public NodeFactory Factory => Context.Factory;

        public NodeStore Store => Context.Store;

        public void EmitExpression(Node expression)
        {
            (Expressions ??= []).Add(expression);
        }

        public void EmitBindingOrAssignment(Node target, Node value, Node location, Node original)
        {
            Debug.Assert(Store.Kind(target) == SyntaxKind.Identifier);
            var expression = CreateAssignmentCallback(target, value, location);
            // expression.original = original
            Store.SetOriginal(expression, original);
            EmitExpression(expression);
        }
    }

    /// <summary>
    /// Flattens a VariableDeclaration to an expression (flattenDestructuringAssignment with a createAssignmentCallback).
    /// </summary>
    internal static Node FlattenDestructuringAssignment(
        Node node,
        Func<Node, VisitResult> visitor,
        TransformationContext context,
        FlattenLevel level,
        bool needsValue,
        Func<Node, Node, Node, Node> createAssignmentCallback)
    {
        var store = context.Store;
        Debug.Assert(store.Kind(node) == SyntaxKind.VariableDeclaration, "Only variable declarations are ported (module D2).");
        Debug.Assert(!needsValue);
        // (downlevelIteration: not a compiler option of Track O, so false.)
        var location = node;

        var flattenContext = new FlattenContext
        {
            Context = context,
            Level = level,
            HoistTempVariables = true,
            Visitor = visitor,
            CreateAssignmentCallback = createAssignmentCallback,
        };

        // (value: a variable declaration is not a destructuring assignment, so there is no right-hand value yet.)
        FlattenBindingOrAssignmentElement(flattenContext, node, Node.None, location, skipInitializer: false);

        return flattenContext.Expressions is { Count: > 0 } expressions
            ? context.Factory.InlineExpressions(expressions)
            : context.Factory.CreateOmittedExpression();
    }

    /// <summary>Flattens a BindingOrAssignmentElement into zero or more bindings or assignments.</summary>
    private static void FlattenBindingOrAssignmentElement(
        FlattenContext flattenContext,
        Node element,
        Node value,
        Node location,
        bool skipInitializer)
    {
        var store = flattenContext.Store;
        var bindingTarget = FactoryUtilities.GetTargetOfBindingOrAssignmentElement(store, element);
        if (!skipInitializer)
        {
            var initializer = Visitors.VisitNode(flattenContext.Factory, GetInitializerOfBindingOrAssignmentElement(store, element), flattenContext.Visitor);
            if (!initializer.IsNone)
            {
                // Combine value and initializer
                if (!value.IsNone)
                {
                    value = CreateDefaultValueCheck(flattenContext, value, initializer, location);
                    // If 'value' is not a simple expression, it could contain side-effecting code that should evaluate before an object or array binding pattern.
                    if (!TransformerUtilities.IsSimpleInlineableExpression(store, initializer) && UtilitiesPublic.IsBindingPattern(store, bindingTarget))
                    {
                        value = EnsureIdentifier(flattenContext, value, reuseIdentifierExpressions: true, location);
                    }
                }
                else
                {
                    value = initializer;
                }
            }
            else if (value.IsNone)
            {
                // Use 'void 0' in absence of value and initializer
                value = flattenContext.Factory.CreateVoidZero();
            }
        }
        if (store.Kind(bindingTarget) == SyntaxKind.ObjectBindingPattern)
        {
            FlattenObjectBindingOrAssignmentPattern(flattenContext, element, bindingTarget, value, location);
        }
        else if (store.Kind(bindingTarget) == SyntaxKind.ArrayBindingPattern)
        {
            FlattenArrayBindingOrAssignmentPattern(flattenContext, element, bindingTarget, value, location);
        }
        else
        {
            flattenContext.EmitBindingOrAssignment(bindingTarget, value, location, /*original*/ element);
        }
    }

    /// <summary>Flattens an ObjectBindingOrAssignmentPattern into zero or more bindings or assignments.</summary>
    private static void FlattenObjectBindingOrAssignmentPattern(FlattenContext flattenContext, Node parent, Node pattern, Node value, Node location)
    {
        var store = flattenContext.Store;
        var elements = store.Nodes(FactoryUtilities.GetElementsOfBindingOrAssignmentPattern(store, pattern)).ToArray();
        var numElements = elements.Length;
        if (numElements != 1)
        {
            // For anything other than a single-element destructuring we need to generate a temporary
            // to ensure value is evaluated exactly once. Additionally, if we have zero elements
            // we need to emit *something* to ensure that in case a 'var' keyword was already emitted,
            // so in that case, we'll intentionally create that temporary.
            var reuseIdentifierExpressions = !UtilitiesPublic.IsDeclarationBindingElement(store, parent) || numElements != 0;
            value = EnsureIdentifier(flattenContext, value, reuseIdentifierExpressions, location);
        }
        for (var i = 0; i < numElements; i++)
        {
            var element = elements[i];
            if (GetRestIndicatorOfBindingOrAssignmentElement(store, element).IsNone)
            {
                var propertyName = GetPropertyNameOfBindingOrAssignmentElement(store, element);
                // (FlattenLevel.All: every element is flattened, the binding elements of FlattenLevel.ObjectRest are
                // never collected.)
                Debug.Assert(flattenContext.Level < FlattenLevel.ObjectRest);
                var rhsValue = CreateDestructuringPropertyAccess(flattenContext, value, propertyName);
                FlattenBindingOrAssignmentElement(flattenContext, element, rhsValue, /*location*/ element, skipInitializer: false);
            }
            else if (i == numElements - 1)
            {
                throw new NotSupportedException("Object rest elements need the __rest helper (createRestHelper), which is not ported.");
            }
        }
    }

    /// <summary>Flattens an ArrayBindingOrAssignmentPattern into zero or more bindings or assignments.</summary>
    private static void FlattenArrayBindingOrAssignmentPattern(FlattenContext flattenContext, Node parent, Node pattern, Node value, Node location)
    {
        var store = flattenContext.Store;
        var factory = flattenContext.Factory;
        var elements = store.Nodes(FactoryUtilities.GetElementsOfBindingOrAssignmentPattern(store, pattern)).ToArray();
        var numElements = elements.Length;
        var allOmitted = true;
        foreach (var element in elements)
        {
            if (store.Kind(element) != SyntaxKind.OmittedExpression)
            {
                allOmitted = false;
                break;
            }
        }
        // (flattenContext.downlevelIteration is false: not a Phase 4 option.)
        if ((numElements != 1 && (flattenContext.Level < FlattenLevel.ObjectRest || numElements == 0)) || allOmitted)
        {
            // For anything other than a single-element destructuring we need to generate a temporary
            // to ensure value is evaluated exactly once. Additionally, if we have zero elements
            // we need to emit *something* to ensure that in case a 'var' keyword was already emitted,
            // so in that case, we'll intentionally create that temporary.
            // Or all the elements of the binding pattern are omitted expression such as "var [,] = [1,2]",
            // then we will create temporary variable.
            var reuseIdentifierExpressions = !UtilitiesPublic.IsDeclarationBindingElement(store, parent) || numElements != 0;
            value = EnsureIdentifier(flattenContext, value, reuseIdentifierExpressions, location);
        }
        Debug.Assert(flattenContext.Level < FlattenLevel.ObjectRest);
        for (var i = 0; i < numElements; i++)
        {
            var element = elements[i];
            if (store.Kind(element) == SyntaxKind.OmittedExpression)
            {
                continue;
            }
            else if (GetRestIndicatorOfBindingOrAssignmentElement(store, element).IsNone)
            {
                var rhsValue = factory.CreateElementAccessExpression(value, factory.CreateNumericLiteral(i));
                FlattenBindingOrAssignmentElement(flattenContext, element, rhsValue, /*location*/ element, skipInitializer: false);
            }
            else if (i == numElements - 1)
            {
                // createArraySliceCall(value, i): createMethodCall(value, "slice", [i])
                var rhsValue = factory.CreateCallExpression(
                    factory.CreatePropertyAccessExpression(value, factory.CreateIdentifier("slice")),
                    default,
                    factory.CreateNodeArray(new[] { factory.CreateNumericLiteral(i) }));
                FlattenBindingOrAssignmentElement(flattenContext, element, rhsValue, /*location*/ element, skipInitializer: false);
            }
        }
    }

    /// <summary>Creates an expression used to provide a default value if a value is `undefined` at runtime.</summary>
    private static Node CreateDefaultValueCheck(FlattenContext flattenContext, Node value, Node defaultValue, Node location)
    {
        value = EnsureIdentifier(flattenContext, value, reuseIdentifierExpressions: true, location);
        return flattenContext.Factory.CreateConditionalExpression(flattenContext.Factory.CreateTypeCheck(value, "undefined"), default, defaultValue, default, value);
    }

    /// <summary>
    /// Creates either a PropertyAccessExpression or an ElementAccessExpression for the right-hand side of a
    /// transformed destructuring assignment.
    /// </summary>
    private static Node CreateDestructuringPropertyAccess(FlattenContext flattenContext, Node value, Node propertyName)
    {
        var store = flattenContext.Store;
        var factory = flattenContext.Factory;
        var kind = store.Kind(propertyName);
        if (kind == SyntaxKind.ComputedPropertyName)
        {
            var expression = Visitors.VisitNode(factory, store.Expression(propertyName), flattenContext.Visitor);
            Debug.Assert(!expression.IsNone);
            var argumentExpression = EnsureIdentifier(flattenContext, expression, reuseIdentifierExpressions: false, /*location*/ propertyName);
            return factory.CreateElementAccessExpression(value, argumentExpression);
        }
        else if (kind is SyntaxKind.StringLiteral or SyntaxKind.NoSubstitutionTemplateLiteral or SyntaxKind.NumericLiteral or SyntaxKind.BigIntLiteral)
        {
            var argumentExpression = factory.CloneNode(propertyName);
            return factory.CreateElementAccessExpression(value, argumentExpression);
        }
        else
        {
            var name = factory.CreateIdentifier(UtilitiesPublic.IdText(store, propertyName));
            return factory.CreatePropertyAccessExpression(value, name);
        }
    }

    /// <summary>
    /// Ensures that there exists a declared identifier whose value holds the given expression. Unless
    /// 'reuseIdentifierExpressions' is false, 'value' will be returned if it is just an identifier.
    /// </summary>
    private static Node EnsureIdentifier(FlattenContext flattenContext, Node value, bool reuseIdentifierExpressions, Node location)
    {
        var store = flattenContext.Store;
        if (store.Kind(value) == SyntaxKind.Identifier && reuseIdentifierExpressions)
        {
            return value;
        }
        else
        {
            var temp = flattenContext.Factory.CreateTempVariable(recordTempVariable: null);
            if (flattenContext.HoistTempVariables)
            {
                flattenContext.Context.HoistVariableDeclaration(temp);
                flattenContext.EmitExpression(FactoryUtilitiesPublic.SetTextRange(store, flattenContext.Factory.CreateAssignment(temp, value), location));
            }
            else
            {
                flattenContext.EmitBindingOrAssignment(temp, value, location, /*original*/ Node.None);
            }
            return temp;
        }
    }

    // factory/utilities.ts getInitializerOfBindingOrAssignmentElement (the declaration binding elements)
    private static Node GetInitializerOfBindingOrAssignmentElement(NodeStore store, Node bindingElement)
    {
        if (UtilitiesPublic.IsDeclarationBindingElement(store, bindingElement))
        {
            // `1` in `let { a = 1 } = ...`
            // `1` in `let { a: b = 1 } = ...`
            // `1` in `let { a: {b} = 1 } = ...`
            // `1` in `let { a: [b] = 1 } = ...`
            // `1` in `let [a = 1] = ...`
            // `1` in `let [{a} = 1] = ...`
            // `1` in `let [[a] = 1] = ...`
            return store.Initializer(bindingElement);
        }
        throw new NotSupportedException("Destructuring assignments are not ported (module D2 ports binding patterns only).");
    }

    // factory/utilities.ts getRestIndicatorOfBindingOrAssignmentElement (the declaration binding elements)
    private static Node GetRestIndicatorOfBindingOrAssignmentElement(NodeStore store, Node bindingElement)
    {
        switch (store.Kind(bindingElement))
        {
            case SyntaxKind.Parameter:
            case SyntaxKind.BindingElement:
                // `...` in `let [...a] = ...`
                return store.DotDotDotToken(bindingElement);
        }
        return Node.None;
    }

    // factory/utilities.ts getPropertyNameOfBindingOrAssignmentElement / tryGetPropertyNameOfBindingOrAssignmentElement
    private static Node GetPropertyNameOfBindingOrAssignmentElement(NodeStore store, Node bindingElement)
    {
        if (store.Kind(bindingElement) == SyntaxKind.BindingElement)
        {
            // `a` in `let { a: b } = ...`
            // `[a]` in `let { [a]: b } = ...`
            // `"a"` in `let { "a": b } = ...`
            // `1` in `let { 1: b } = ...`
            var propertyName = store.PropertyName(bindingElement);
            if (!propertyName.IsNone)
            {
                Debug.Assert(store.Kind(propertyName) != SyntaxKind.PrivateIdentifier);
                return store.Kind(propertyName) == SyntaxKind.ComputedPropertyName && store.Kind(store.Expression(propertyName)) is SyntaxKind.StringLiteral or SyntaxKind.NumericLiteral
                    ? store.Expression(propertyName)
                    : propertyName;
            }
        }
        var target = FactoryUtilities.GetTargetOfBindingOrAssignmentElement(store, bindingElement);
        Debug.Assert(!target.IsNone && UtilitiesPublic.IsPropertyName(store, target));
        return target;
    }
}
