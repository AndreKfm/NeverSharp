// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/transformers/classFields.ts (transformClassFields) and the private environment of
// src/compiler/transformers/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/transformers/estransforms/classfields.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D4. As far as TypeScript 6.0.3's default options reach it (target
// ES2022 or later, useDefineForClassFields true): auto-accessors (`accessor x`), the private static elements of classes
// with class decorators (InternalEmitFlags.TransformPrivateStaticElements: WeakMap-style storage, the
// __classPrivateFieldGet/Set/In helpers), class aliases for constructor references, named evaluation of anonymous
// classes. Field initializers moved to the constructor (useDefineForClassFields false or a target below ES2022) and
// private names and class static blocks below ES2022 are outside these options: the transformer throws for them.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>classFields.ts transformClassFields: the closure of the transformer is the instance.</summary>
internal sealed class ClassFieldsTransformer
{
    [Flags]
    private enum ClassPropertySubstitutionFlags
    {
        None = 0,
        /// <summary>Enables substitutions for class expressions with static fields which have initializers that reference the class name.</summary>
        ClassAliases = 1 << 0,
        /// <summary>Enables substitutions for class expressions with static fields which have initializers that reference the 'this' or 'super'.</summary>
        ClassStaticThisOrSuperReference = 1 << 1,
    }

    [Flags]
    private enum ClassFacts
    {
        None = 0,
        ClassWasDecorated = 1 << 0,
        NeedsClassConstructorReference = 1 << 1,
        NeedsClassSuperReference = 1 << 2,
        NeedsSubstitutionForThisInClassStaticField = 1 << 3,
        WillHoistInitializersToConstructor = 1 << 4,
    }

    private const string Untransformed = "untransformed";

    /// <summary>PrivateIdentifierInfo (kind "f", "m", "a" or "untransformed").</summary>
    private sealed class PrivateIdentifierInfo
    {
        internal string Kind = Untransformed;
        /// <summary>The WeakMap / WeakSet / constructor used for brand checks.</summary>
        internal Node BrandCheckIdentifier;
        internal bool IsStatic;
        /// <summary>Reserved names (e.g. #constructor) or duplicate identifiers are invalid.</summary>
        internal bool IsValid;
        internal Node GetterName; // Accessor
        internal Node SetterName; // Accessor
        internal Node MethodName; // Method
        internal Node VariableName; // static Field
    }

    /// <summary>PrivateEnvironmentData.</summary>
    private sealed class PrivateEnvironmentData
    {
        internal Node ClassName; // Used for prefixing generated variable names.
        // (weakSetName, used for brand check on private methods below ES2022, is not set under these options)
    }

    /// <summary>transformers/utilities.ts PrivateEnvironment.</summary>
    private sealed class PrivateEnvironment
    {
        internal readonly PrivateEnvironmentData Data = new();
        internal Dictionary<string, PrivateIdentifierInfo>? Identifiers;
        internal Dictionary<Node, PrivateIdentifierInfo>? GeneratedIdentifiers;
    }

    /// <summary>ClassLexicalEnvironment.</summary>
    private sealed class ClassLexicalEnvironment
    {
        internal ClassFacts Facts;
        /// <summary>Used for brand checks on static members, and `this` references in static initializers.</summary>
        internal Node ClassConstructor;
        internal Node ClassThis;
        /// <summary>Used for `super` references in static initializers.</summary>
        internal Node SuperClassReference;
    }

    /// <summary>transformers/utilities.ts LexicalEnvironment.</summary>
    private sealed class LexicalEnv
    {
        internal ClassLexicalEnvironment? Data;
        internal PrivateEnvironment? PrivateEnv;
        internal LexicalEnv? Previous;
    }

    private readonly TransformationContext context;
    private readonly NodeFactory factory;
    private readonly EmitResolver resolver;
    private readonly ScriptTarget languageVersion;
    private readonly bool useDefineForClassFields;

    private readonly Func<Node, VisitResult> visitor;
    private readonly Func<Node, VisitResult> fallbackVisitor;
    private readonly Func<Node, VisitResult> discardedValueVisitor;
    private readonly Func<Node, VisitResult> heritageClauseVisitor;
    private readonly Func<Node, VisitResult> assignmentTargetVisitor;
    private readonly Func<Node, VisitResult> classElementVisitor;
    private readonly Func<Node, VisitResult> propertyNameVisitor;
    private readonly Func<Node, VisitResult> modifierVisitor;
    private readonly Action<Node> hoistVariableDeclaration;

    private readonly Func<EmitHint, Node, Node> previousOnSubstituteNode;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> previousOnEmitNode;

    private bool shouldTransformPrivateStaticElementsInFile;
    private ClassPropertySubstitutionFlags enabledSubstitutions;

    private Dictionary<Node, Node>? classAliases;

    /// <summary>Tracks what computed name expressions originating from elided names must be inlined at the next execution site, in document order.</summary>
    private List<Node>? pendingExpressions;

    /// <summary>Tracks what computed name expression statements and static property initializers must be emitted at the next execution site, in document order (for decorated classes).</summary>
    private List<Node>? pendingStatements;

    private LexicalEnv? lexicalEnvironment;
    private readonly Dictionary<Node, LexicalEnv> lexicalEnvironmentMap = [];

    // Nodes that should not be replaced during emit substitution.
    private readonly HashSet<Node> noSubstitution = [];

    private Node currentClassContainer;
    private Node currentClassElement;
    private bool shouldSubstituteThisWithClassThis;
    private bool previousShouldSubstituteThisWithClassThis;

    private ClassFieldsTransformer(TransformationContext context)
    {
        this.context = context;
        factory = context.Factory;
        resolver = context.GetEmitResolver();
        var compilerOptions = context.GetCompilerOptions();
        languageVersion = compilerOptions.GetEmitScriptTarget();
        useDefineForClassFields = compilerOptions.GetUseDefineForClassFields();
        var legacyDecorators = compilerOptions.ExperimentalDecorators == true;

        // Always transform field initializers using Set semantics when `useDefineForClassFields: false`.
        // Transform field initializers using Define semantics when `useDefineForClassFields: true` and target < ES2022.
        var shouldTransformInitializers = !useDefineForClassFields || languageVersion < ScriptTarget.ES2022;
        // We need to transform private members and class static blocks when target < ES2022.
        var shouldTransformPrivateElementsOrClassStaticBlocks = languageVersion < ScriptTarget.ES2022;
        if (shouldTransformInitializers || shouldTransformPrivateElementsOrClassStaticBlocks || legacyDecorators)
        {
            throw new NotSupportedException("transformClassFields: field initializers in the constructor, private names below ES2022 and legacy decorators are not Phase 4 options.");
        }
        // (Track O: so shouldTransformInitializers, shouldTransformPrivateElementsOrClassStaticBlocks,
        // shouldTransformThisInStaticInitializers and shouldTransformSuperInStaticInitializers are false below, and
        // shouldTransformAutoAccessors is Ternary.True when the target is below ESNext.)
        ShouldTransformAutoAccessors = languageVersion < ScriptTarget.ESNext;

        visitor = Visitor;
        fallbackVisitor = FallbackVisitor;
        discardedValueVisitor = DiscardedValueVisitor;
        heritageClauseVisitor = HeritageClauseVisitor;
        assignmentTargetVisitor = AssignmentTargetVisitor;
        classElementVisitor = ClassElementVisitor;
        propertyNameVisitor = PropertyNameVisitor;
        modifierVisitor = ModifierVisitor;
        hoistVariableDeclaration = context.HoistVariableDeclaration;
        currentClassContainer = Node.None;
        currentClassElement = Node.None;

        previousOnSubstituteNode = context.OnSubstituteNode;
        context.OnSubstituteNode = OnSubstituteNode;

        previousOnEmitNode = context.OnEmitNode;
        context.OnEmitNode = OnEmitNode;
    }

    // shouldTransformAutoAccessors === Ternary.True (useDefineForClassFields true: never Ternary.Maybe here)
    private bool ShouldTransformAutoAccessors { get; }

    private NodeStore store => context.Store;

    /// <summary>transformClassFields(context).</summary>
    internal static Func<Node, Node> TransformClassFields(TransformationContext context) => new ClassFieldsTransformer(context).TransformSourceFile;

    private Node TransformSourceFile(Node node)
    {
        if (store.IsDeclarationFile(node))
        {
            return node;
        }

        lexicalEnvironment = null;
        shouldTransformPrivateStaticElementsInFile = (FactoryEmitNode.GetInternalEmitFlags(store, node) & InternalEmitFlags.TransformPrivateStaticElements) != 0;
        // shouldTransformAnything: shouldTransformAutoAccessors === Ternary.True
        if (!ShouldTransformAutoAccessors && !shouldTransformPrivateStaticElementsInFile)
        {
            return node;
        }

        var visited = Visitors.VisitEachChild(node, visitor, context);
        FactoryEmitNode.AddEmitHelpers(store, visited, context.ReadEmitHelpers());
        return visited;
    }

    private VisitResult ModifierVisitor(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.AccessorKeyword:
                return ShouldTransformAutoAccessorsInCurrentClass() ? VisitResult.None : node;
            default:
                return UtilitiesPublic.IsModifier(store, node) ? node : VisitResult.None;
        }
    }

    private VisitResult Visitor(Node node)
    {
        if ((store.TransformFlags(node) & (TransformFlags.ContainsClassFields | TransformFlags.ContainsLexicalThis | TransformFlags.ContainsLexicalSuper)) == 0)
        {
            return node;
        }

        switch (store.Kind(node))
        {
            case SyntaxKind.ClassDeclaration:
                return VisitClassDeclaration(node);
            case SyntaxKind.ClassExpression:
                return VisitClassExpression(node);
            case SyntaxKind.ClassStaticBlockDeclaration:
            case SyntaxKind.PropertyDeclaration:
                throw new InvalidOperationException("Debug Failure. Use `classElementVisitor` instead.");
            case SyntaxKind.PropertyAssignment:
                return VisitPropertyAssignment(node);
            case SyntaxKind.VariableStatement:
                return VisitVariableStatement(node);
            case SyntaxKind.VariableDeclaration:
                return VisitVariableDeclaration(node);
            case SyntaxKind.Parameter:
                return VisitParameterDeclaration(node);
            case SyntaxKind.BindingElement:
                return VisitBindingElement(node);
            case SyntaxKind.ExportAssignment:
                return VisitExportAssignment(node);
            case SyntaxKind.PrivateIdentifier:
                return VisitPrivateIdentifier(node);
            case SyntaxKind.PropertyAccessExpression:
                return VisitPropertyAccessExpression(node);
            case SyntaxKind.ElementAccessExpression:
                return VisitElementAccessExpression(node);
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
                return VisitPreOrPostfixUnaryExpression(node, discarded: false);
            case SyntaxKind.BinaryExpression:
                return VisitBinaryExpression(node, discarded: false);
            case SyntaxKind.ParenthesizedExpression:
                return VisitParenthesizedExpression(node, discarded: false);
            case SyntaxKind.CallExpression:
                return VisitCallExpression(node);
            case SyntaxKind.ExpressionStatement:
                return VisitExpressionStatement(node);
            case SyntaxKind.TaggedTemplateExpression:
                return VisitTaggedTemplateExpression(node);
            case SyntaxKind.ForStatement:
                return VisitForStatement(node);
            case SyntaxKind.ThisKeyword:
                return VisitThisExpression(node);
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
                // If we are descending into a new scope, clear the current class element
                return SetCurrentClassElementAnd(Node.None, fallbackVisitor, node);
            case SyntaxKind.Constructor:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                // If we are descending into a class element, set the class element
                return SetCurrentClassElementAnd(node, fallbackVisitor, node);
            default:
                return FallbackVisitor(node);
        }
    }

    private VisitResult FallbackVisitor(Node node)
    {
        return Visitors.VisitEachChild(node, visitor, context);
    }

    /// <summary>Visits a node in an expression whose result is discarded.</summary>
    private VisitResult DiscardedValueVisitor(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
                return VisitPreOrPostfixUnaryExpression(node, discarded: true);
            case SyntaxKind.BinaryExpression:
                return VisitBinaryExpression(node, discarded: true);
            case SyntaxKind.CommaListExpression:
                return VisitCommaListExpression(node, discarded: true);
            case SyntaxKind.ParenthesizedExpression:
                return VisitParenthesizedExpression(node, discarded: true);
            default:
                return Visitor(node);
        }
    }

    /// <summary>Visits a node in a HeritageClause.</summary>
    private VisitResult HeritageClauseVisitor(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.HeritageClause:
                return Visitors.VisitEachChild(node, heritageClauseVisitor, context);
            case SyntaxKind.ExpressionWithTypeArguments:
                return VisitExpressionWithTypeArgumentsInHeritageClause(node);
            default:
                return Visitor(node);
        }
    }

    /// <summary>Visits the assignment target of a destructuring assignment.</summary>
    private VisitResult AssignmentTargetVisitor(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.ArrayLiteralExpression:
                return VisitAssignmentPattern(node);
            default:
                return Visitor(node);
        }
    }

    /// <summary>Visits a member of a class.</summary>
    private VisitResult ClassElementVisitor(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.Constructor:
                return SetCurrentClassElementAnd(node, VisitConstructorDeclaration, node);
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.MethodDeclaration:
                return SetCurrentClassElementAnd(node, VisitMethodOrAccessorDeclaration, node);
            case SyntaxKind.PropertyDeclaration:
                return SetCurrentClassElementAnd(node, VisitPropertyDeclaration, node);
            case SyntaxKind.ClassStaticBlockDeclaration:
                return SetCurrentClassElementAnd(node, VisitClassStaticBlockDeclaration, node);
            case SyntaxKind.ComputedPropertyName:
                return VisitComputedPropertyName(node);
            case SyntaxKind.SemicolonClassElement:
                return node;
            default:
                // isModifierLike
                return UtilitiesPublic.IsModifier(store, node) || store.Kind(node) == SyntaxKind.Decorator ? ModifierVisitor(node) : Visitor(node);
        }
    }

    /// <summary>Visits a property name of a class member.</summary>
    private VisitResult PropertyNameVisitor(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.ComputedPropertyName:
                return VisitComputedPropertyName(node);
            default:
                return Visitor(node);
        }
    }

    /// <summary>Visits the results of an auto-accessor field transformation in a second pass.</summary>
    private VisitResult AccessorFieldResultVisitor(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.PropertyDeclaration:
                return TransformFieldInitializer(node);
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                return ClassElementVisitor(node);
            default:
                throw new InvalidOperationException("Debug Failure. Expected node to either be a PropertyDeclaration, GetAccessorDeclaration, or SetAccessorDeclaration");
        }
    }

    private Node Visit(Node node, Func<Node, VisitResult> v) => Visitors.VisitNode(factory, node, v);

    private NodeList VisitList(NodeList nodes, Func<Node, VisitResult> v) => Visitors.VisitNodes(factory, nodes, v);

    private Node This() => factory.CreateToken(SyntaxKind.ThisKeyword);

    /// <summary>
    /// If we visit a private name, this means it is an undeclared private name. (Below ES2022 TypeScript replaces it with
    /// an empty identifier; not under these options.)
    /// </summary>
    private static VisitResult VisitPrivateIdentifier(Node node)
    {
        // !shouldTransformPrivateElementsOrClassStaticBlocks
        return node;
    }

    /// <summary>Visits `#id in expr`.</summary>
    private VisitResult TransformPrivateIdentifierInInExpression(Node node)
    {
        var info = AccessPrivateIdentifier(store.Left(node));
        if (info is not null)
        {
            var receiver = Visit(store.Right(node), visitor);
            return NodeFactory.SetOriginalNode(
                store,
                context.GetEmitHelperFactory().CreateClassPrivateFieldInHelper(info.BrandCheckIdentifier, receiver),
                node);
        }

        // Private name has not been declared. Subsequent transformers will handle this error
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitPropertyAssignment(Node node)
    {
        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node);
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitVariableStatement(Node node)
    {
        var savedPendingStatements = pendingStatements;
        pendingStatements = [];

        var visitedNode = Visitors.VisitEachChild(node, visitor, context);
        VisitResult statement;
        if (Core.Some(pendingStatements))
        {
            var statements = new List<Node>(pendingStatements.Count + 1) { visitedNode };
            statements.AddRange(pendingStatements);
            statement = VisitResult.Many(statements);
        }
        else
        {
            statement = visitedNode;
        }

        pendingStatements = savedPendingStatements;
        return statement;
    }

    private VisitResult VisitVariableDeclaration(Node node)
    {
        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node);
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitParameterDeclaration(Node node)
    {
        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node);
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitBindingElement(Node node)
    {
        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node);
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitExportAssignment(Node node)
    {
        // NOTE: Since emit for `export =` translates to `module.exports = ...`, the assigned nameof the class
        // is `""`.

        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node, ignoreEmptyStringLiteral: true, store.IsExportEquals(node) ? "" : "default");
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private Node InjectPendingExpressions(Node expression)
    {
        if (Core.Some(pendingExpressions))
        {
            if (store.Kind(expression) == SyntaxKind.ParenthesizedExpression)
            {
                pendingExpressions.Add(store.Expression(expression));
                expression = factory.UpdateParenthesizedExpression(expression, factory.InlineExpressions(pendingExpressions));
            }
            else
            {
                pendingExpressions.Add(expression);
                expression = factory.InlineExpressions(pendingExpressions);
            }
            pendingExpressions = null;
        }
        return expression;
    }

    private VisitResult VisitComputedPropertyName(Node node)
    {
        var expression = Visit(store.Expression(node), visitor);
        return factory.UpdateComputedPropertyName(node, InjectPendingExpressions(expression));
    }

    private VisitResult VisitConstructorDeclaration(Node node)
    {
        if (!currentClassContainer.IsNone)
        {
            return TransformConstructor(node, currentClassContainer);
        }
        return FallbackVisitor(node);
    }

    private bool ShouldTransformClassElementToWeakMap(Node node)
    {
        // (shouldTransformPrivateElementsOrClassStaticBlocks is false)
        return TransformerUtilities.HasStaticModifier(store, node) && (FactoryEmitNode.GetInternalEmitFlags(store, node) & InternalEmitFlags.TransformPrivateStaticElements) != 0;
    }

    private VisitResult VisitMethodOrAccessorDeclaration(Node node)
    {
        Debug.Assert(!TransformerUtilities.HasDecorators(store, node));

        if (!TransformerUtilities.IsPrivateIdentifierClassElementDeclaration(store, node) || !ShouldTransformClassElementToWeakMap(node))
        {
            return Visitors.VisitEachChild(node, classElementVisitor, context);
        }

        // leave invalid code untransformed
        var info = AccessPrivateIdentifier(store.Name(node));
        Debug.Assert(info is not null, "Undeclared private name for property declaration.");
        if (!info.IsValid)
        {
            return node;
        }

        var functionName = GetHoistedFunctionName(node);
        if (!functionName.IsNone)
        {
            // filter(node.modifiers, m => isModifier(m) && !isStaticModifier(m) && !isAccessorModifier(m)): the array
            // itself when every element passes
            var nodeModifiers = store.Modifiers(node);
            var modifiers = new List<Node>();
            foreach (var m in store.Nodes(nodeModifiers))
            {
                if (UtilitiesPublic.IsModifier(store, m) && store.Kind(m) is not (SyntaxKind.StaticKeyword or SyntaxKind.AccessorKeyword))
                {
                    modifiers.Add(m);
                }
            }
            GetPendingExpressions().Add(
                factory.CreateAssignment(
                    functionName,
                    factory.CreateFunctionExpression(
                        nodeModifiers.IsNone || modifiers.Count == store.Length(nodeModifiers) ? nodeModifiers : factory.CreateNodeArray(modifiers),
                        store.AsteriskToken(node),
                        functionName,
                        default,
                        Visitors.VisitParameterList(store.Parameters(node), visitor, context),
                        default,
                        Visitors.VisitFunctionBody(store.Body(node), visitor, context))));
        }

        // remove method declaration from class
        return VisitResult.None;
    }

    private VisitResult SetCurrentClassElementAnd(Node classElement, Func<Node, VisitResult> v, Node arg)
    {
        if (classElement != currentClassElement)
        {
            var savedCurrentClassElement = currentClassElement;
            currentClassElement = classElement;
            var result = v(arg);
            currentClassElement = savedCurrentClassElement;
            return result;
        }
        return v(arg);
    }

    private Node GetHoistedFunctionName(Node node)
    {
        Debug.Assert(store.Kind(store.Name(node)) == SyntaxKind.PrivateIdentifier);
        var info = AccessPrivateIdentifier(store.Name(node));
        Debug.Assert(info is not null, "Undeclared private name for property declaration.");

        if (info.Kind == PrivateIdentifierKind.Method)
        {
            return info.MethodName;
        }

        if (info.Kind == PrivateIdentifierKind.Accessor)
        {
            if (store.Kind(node) == SyntaxKind.GetAccessor)
            {
                return info.GetterName;
            }
            if (store.Kind(node) == SyntaxKind.SetAccessor)
            {
                return info.SetterName;
            }
        }
        return Node.None;
    }

    private Node TryGetClassThis()
    {
        var lex = GetClassLexicalEnvironment();
        return !lex.ClassThis.IsNone ? lex.ClassThis :
            !lex.ClassConstructor.IsNone ? lex.ClassConstructor :
            currentClassContainer.IsNone ? Node.None : store.Name(currentClassContainer);
    }

    private VisitResult TransformAutoAccessor(Node node)
    {
        // transforms:
        //      accessor x = 1;
        // into:
        //      #x = 1;
        //      get x() { return this.#x; }
        //      set x(value) { this.#x = value; }

        var commentRange = FactoryEmitNode.GetCommentRange(store, node);
        var sourceMapRange = FactoryEmitNode.GetSourceMapRange(store, node);

        // Since we're creating two declarations where there was previously one, cache
        // the expression for any computed property names.
        var name = store.Name(node);
        var getterName = name;
        var setterName = name;
        if (store.Kind(name) == SyntaxKind.ComputedPropertyName && !TransformerUtilities.IsSimpleInlineableExpression(store, store.Expression(name)))
        {
            var cacheAssignment = FactoryUtilities.FindComputedPropertyNameCacheAssignment(store, name);
            if (!cacheAssignment.IsNone)
            {
                getterName = factory.UpdateComputedPropertyName(name, Visit(store.Expression(name), visitor));
                setterName = factory.UpdateComputedPropertyName(name, store.Left(cacheAssignment));
            }
            else
            {
                var temp = factory.CreateTempVariable(hoistVariableDeclaration);
                FactoryEmitNode.SetSourceMapRange(store, temp, TransformerUtilities.RangeOf(store, store.Expression(name)));
                var expression = Visit(store.Expression(name), visitor);
                var assignment = factory.CreateAssignment(temp, expression);
                FactoryEmitNode.SetSourceMapRange(store, assignment, TransformerUtilities.RangeOf(store, store.Expression(name)));
                getterName = factory.UpdateComputedPropertyName(name, assignment);
                setterName = factory.UpdateComputedPropertyName(name, temp);
            }
        }

        var modifiers = VisitList(store.Modifiers(node), modifierVisitor);
        var backingField = FactoryUtilities.CreateAccessorPropertyBackingField(factory, node, modifiers, store.Initializer(node));
        NodeFactory.SetOriginalNode(store, backingField, node);
        FactoryEmitNode.SetEmitFlags(store, backingField, EmitFlags.NoComments);
        FactoryEmitNode.SetSourceMapRange(store, backingField, sourceMapRange);

        var receiver = Utilities.IsStatic(store, node) ? TryGetClassThis() : Node.None;
        if (receiver.IsNone)
        {
            receiver = This();
        }
        var getter = FactoryUtilities.CreateAccessorPropertyGetRedirector(factory, node, modifiers, getterName, receiver);
        NodeFactory.SetOriginalNode(store, getter, node);
        FactoryEmitNode.SetCommentRange(store, getter, commentRange);
        FactoryEmitNode.SetSourceMapRange(store, getter, sourceMapRange);

        // create a fresh copy of the modifiers so that we don't duplicate comments
        var setterModifiers = factory.CreateModifiersFromModifierFlags(Utilities.ModifiersToFlags(store, modifiers));
        var setter = FactoryUtilities.CreateAccessorPropertySetRedirector(factory, node, setterModifiers, setterName, receiver);
        NodeFactory.SetOriginalNode(store, setter, node);
        FactoryEmitNode.SetEmitFlags(store, setter, EmitFlags.NoComments);
        FactoryEmitNode.SetSourceMapRange(store, setter, sourceMapRange);

        return VisitResult.Many(Visitors.VisitArray(store, [backingField, getter, setter], AccessorFieldResultVisitor));
    }

    private VisitResult TransformPrivateFieldInitializer(Node node)
    {
        if (ShouldTransformClassElementToWeakMap(node))
        {
            // If we are transforming private elements into WeakMap/WeakSet, we should elide the node.
            var info = AccessPrivateIdentifier(store.Name(node));
            Debug.Assert(info is not null, "Undeclared private name for property declaration.");

            // Leave invalid code untransformed
            if (!info.IsValid)
            {
                return node;
            }

            // If we encounter a valid private static field and we're not transforming
            // class static blocks, initialize it
            if (info.IsStatic)
            {
                // TODO: fix
                var statement = TransformPropertyOrClassStaticBlock(node, This());
                if (!statement.IsNone)
                {
                    return factory.CreateClassStaticBlockDeclaration(factory.CreateBlock(factory.CreateNodeArray(new[] { statement }), multiLine: true));
                }
            }

            return VisitResult.None;
        }

        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node);
        }

        return factory.UpdatePropertyDeclaration(
            node,
            VisitList(store.Modifiers(node), modifierVisitor),
            Visit(store.Name(node), propertyNameVisitor),
            default,
            default,
            Visit(store.Initializer(node), visitor));
    }

    private VisitResult TransformPublicFieldInitializer(Node node)
    {
        // (shouldTransformInitializers is false: the field stays a field.)
        return factory.UpdatePropertyDeclaration(
            node,
            VisitList(store.Modifiers(node), modifierVisitor),
            Visit(store.Name(node), propertyNameVisitor),
            default,
            default,
            Visit(store.Initializer(node), visitor));
    }

    private VisitResult TransformFieldInitializer(Node node)
    {
        Debug.Assert(!TransformerUtilities.HasDecorators(store, node), "Decorators should already have been transformed and elided.");
        return TransformerUtilities.IsPrivateIdentifierClassElementDeclaration(store, node) ?
            TransformPrivateFieldInitializer(node) :
            TransformPublicFieldInitializer(node);
    }

    private bool ShouldTransformAutoAccessorsInCurrentClass()
    {
        // shouldTransformAutoAccessors === Ternary.True (Ternary.Maybe needs useDefineForClassFields false)
        return ShouldTransformAutoAccessors;
    }

    private VisitResult VisitPropertyDeclaration(Node node)
    {
        // If this is an auto-accessor, we defer to `transformAutoAccessor`. That function
        // will in turn call `transformFieldInitializer` as needed.
        if (TransformerUtilities.IsAutoAccessorPropertyDeclaration(store, node) && (ShouldTransformAutoAccessorsInCurrentClass() ||
            TransformerUtilities.HasStaticModifier(store, node) && (FactoryEmitNode.GetInternalEmitFlags(store, node) & InternalEmitFlags.TransformPrivateStaticElements) != 0))
        {
            return TransformAutoAccessor(node);
        }

        return TransformFieldInitializer(node);
    }

    private bool ShouldForceDynamicThis()
    {
        return !currentClassElement.IsNone &&
            TransformerUtilities.HasStaticModifier(store, currentClassElement) &&
            TransformerUtilities.IsAccessor(store, currentClassElement) &&
            TransformerUtilities.IsAutoAccessorPropertyDeclaration(store, TransformerUtilities.GetOriginalNode(store, currentClassElement));
    }

    /// <summary>Prevent substitution of `this` to `_classThis` in static getters and setters that wrap `accessor` fields.</summary>
    private void EnsureDynamicThisIfNeeded(Node node)
    {
        if (ShouldForceDynamicThis())
        {
            // do not substitute `this` with `_classThis` when `this`
            // should be bound dynamically.
            var innerExpression = FactoryUtilities.SkipOuterExpressions(store, node);
            if (store.Kind(innerExpression) == SyntaxKind.ThisKeyword)
            {
                noSubstitution.Add(innerExpression);
            }
        }
    }

    private Node CreatePrivateIdentifierAccess(PrivateIdentifierInfo info, Node receiver)
    {
        receiver = Visit(receiver, visitor);
        EnsureDynamicThisIfNeeded(receiver);
        return CreatePrivateIdentifierAccessHelper(info, receiver);
    }

    // utilities.ts moveRangePos(receiver, -1)
    private ObjectTextRange MoveRangePosMinusOne(Node node) => new() { Pos = -1, End = store.End(node) };

    private Node CreatePrivateIdentifierAccessHelper(PrivateIdentifierInfo info, Node receiver)
    {
        FactoryEmitNode.SetCommentRange(store, receiver, MoveRangePosMinusOne(receiver));

        switch (info.Kind)
        {
            case PrivateIdentifierKind.Accessor:
                return context.GetEmitHelperFactory().CreateClassPrivateFieldGetHelper(
                    receiver,
                    info.BrandCheckIdentifier,
                    info.Kind,
                    info.GetterName);
            case PrivateIdentifierKind.Method:
                return context.GetEmitHelperFactory().CreateClassPrivateFieldGetHelper(
                    receiver,
                    info.BrandCheckIdentifier,
                    info.Kind,
                    info.MethodName);
            case PrivateIdentifierKind.Field:
                return context.GetEmitHelperFactory().CreateClassPrivateFieldGetHelper(
                    receiver,
                    info.BrandCheckIdentifier,
                    info.Kind,
                    info.IsStatic ? info.VariableName : Node.None);
            case Untransformed:
                throw new InvalidOperationException("Debug Failure. Access helpers should not be created for untransformed private elements");
            default:
                throw new InvalidOperationException("Debug Failure. Unknown private element type");
        }
    }

    private VisitResult VisitPropertyAccessExpression(Node node)
    {
        if (store.Kind(store.Name(node)) == SyntaxKind.PrivateIdentifier)
        {
            var privateIdentifierInfo = AccessPrivateIdentifier(store.Name(node));
            if (privateIdentifierInfo is not null)
            {
                return FactoryUtilitiesPublic.SetTextRange(
                    store,
                    NodeFactory.SetOriginalNode(
                        store,
                        CreatePrivateIdentifierAccess(privateIdentifierInfo, store.Expression(node)),
                        node),
                    node);
            }
        }
        // (`super` in static initializers: shouldTransformSuperInStaticInitializers is false.)
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitElementAccessExpression(Node node)
    {
        // (`super` in static initializers: shouldTransformSuperInStaticInitializers is false.)
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitPreOrPostfixUnaryExpression(Node node, bool discarded)
    {
        var @operator = store.Operator(node);
        if (@operator is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken)
        {
            var operand = Utilities.SkipParentheses(store, store.Operand(node));

            if (TransformerUtilities.IsPrivateIdentifierPropertyAccessExpression(store, operand))
            {
                var info = AccessPrivateIdentifier(store.Name(operand));
                if (info is not null)
                {
                    var receiver = Visit(store.Expression(operand), visitor);
                    EnsureDynamicThisIfNeeded(receiver);
                    var (readExpression, initializeExpression) = CreateCopiableReceiverExpr(receiver);

                    var expression = CreatePrivateIdentifierAccess(info, readExpression);
                    var temp = store.Kind(node) == SyntaxKind.PrefixUnaryExpression || discarded ? Node.None : factory.CreateTempVariable(hoistVariableDeclaration);
                    expression = FactoryUtilities.ExpandPreOrPostfixIncrementOrDecrementExpression(factory, node, expression, hoistVariableDeclaration, temp);
                    expression = CreatePrivateIdentifierAssignment(
                        info,
                        initializeExpression.IsNone ? readExpression : initializeExpression,
                        expression,
                        SyntaxKind.EqualsToken);
                    NodeFactory.SetOriginalNode(store, expression, node);
                    FactoryUtilitiesPublic.SetTextRange(store, expression, node);
                    if (!temp.IsNone)
                    {
                        expression = factory.CreateComma(expression, temp);
                        FactoryUtilitiesPublic.SetTextRange(store, expression, node);
                    }
                    return expression;
                }
            }
            // (`super` in static initializers: shouldTransformSuperInStaticInitializers is false.)
        }
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitForStatement(Node node)
    {
        return factory.UpdateForStatement(
            node,
            Visit(store.Initializer(node), discardedValueVisitor),
            Visit(store.Condition(node), visitor),
            Visit(store.Incrementor(node), discardedValueVisitor),
            Visitors.VisitIterationBody(store.Statement(node), visitor, context));
    }

    private VisitResult VisitExpressionStatement(Node node)
    {
        return factory.UpdateExpressionStatement(
            node,
            Visit(store.Expression(node), discardedValueVisitor));
    }

    private (Node ReadExpression, Node InitializeExpression) CreateCopiableReceiverExpr(Node receiver)
    {
        var clone = Utilities.NodeIsSynthesized(store, receiver) ? receiver : factory.CloneNode(receiver);
        if (store.Kind(receiver) == SyntaxKind.ThisKeyword && noSubstitution.Contains(receiver))
        {
            noSubstitution.Add(clone);
        }
        if (TransformerUtilities.IsSimpleInlineableExpression(store, receiver))
        {
            return (clone, Node.None);
        }
        var readExpression = factory.CreateTempVariable(hoistVariableDeclaration);
        var initializeExpression = factory.CreateAssignment(readExpression, clone);
        return (readExpression, initializeExpression);
    }

    private NodeList PrependedArguments(Node first, NodeList rest)
    {
        var list = new List<Node>(store.Length(rest) + 1) { first };
        list.AddRange(store.Nodes(rest));
        return factory.CreateNodeArray(list);
    }

    private VisitResult VisitCallExpression(Node node)
    {
        var expression = store.Expression(node);
        if (TransformerUtilities.IsPrivateIdentifierPropertyAccessExpression(store, expression) &&
            AccessPrivateIdentifier(store.Name(expression)) is not null)
        {
            // obj.#x()

            // Transform call expressions of private names to properly bind the `this` parameter.
            var (target, thisArg) = factory.CreateCallBinding(expression, hoistVariableDeclaration, languageVersion);
            if (UtilitiesPublic.IsCallChain(store, node))
            {
                return factory.UpdateCallChain(
                    node,
                    factory.CreatePropertyAccessChain(Visit(target, visitor), store.QuestionDotToken(node), factory.CreateIdentifier("call")),
                    default,
                    default,
                    PrependedArguments(Visit(thisArg, visitor), VisitList(store.Arguments(node), visitor)));
            }
            return factory.UpdateCallExpression(
                node,
                factory.CreatePropertyAccessExpression(Visit(target, visitor), factory.CreateIdentifier("call")),
                default,
                PrependedArguments(Visit(thisArg, visitor), VisitList(store.Arguments(node), visitor)));
        }

        // (`super` in static initializers: shouldTransformSuperInStaticInitializers is false.)
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitTaggedTemplateExpression(Node node)
    {
        var tag = store.Tag(node);
        if (TransformerUtilities.IsPrivateIdentifierPropertyAccessExpression(store, tag) &&
            AccessPrivateIdentifier(store.Name(tag)) is not null)
        {
            // Bind the `this` correctly for tagged template literals when the tag is a private identifier property access.
            var (target, thisArg) = factory.CreateCallBinding(tag, hoistVariableDeclaration, languageVersion);
            return factory.UpdateTaggedTemplateExpression(
                node,
                factory.CreateCallExpression(
                    factory.CreatePropertyAccessExpression(Visit(target, visitor), factory.CreateIdentifier("bind")),
                    default,
                    factory.CreateNodeArray(new[] { Visit(thisArg, visitor) })),
                default,
                Visit(store.Template(node), visitor));
        }
        // (`super` in static initializers: shouldTransformSuperInStaticInitializers is false.)
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private Node TransformClassStaticBlockDeclaration(Node node)
    {
        if (lexicalEnvironment is not null)
        {
            lexicalEnvironmentMap[TransformerUtilities.GetOriginalNode(store, node)] = lexicalEnvironment;
        }

        // (shouldTransformPrivateElementsOrClassStaticBlocks is false: class static blocks stay.)
        return Node.None;
    }

    private bool IsAnonymousClassNeedingAssignedName(Node node)
    {
        if (store.Kind(node) == SyntaxKind.ClassExpression && store.Name(node).IsNone)
        {
            var staticPropertiesOrClassStaticBlocks = TransformerUtilities.GetStaticPropertiesAndClassStaticBlock(store, node);
            foreach (var member in staticPropertiesOrClassStaticBlocks)
            {
                if (NamedEvaluation.IsClassNamedEvaluationHelperBlock(store, member))
                {
                    return false;
                }
            }

            // (TypeScript tests `getInternalEmitFlags(node) && InternalEmitFlags.TransformPrivateStaticElements`, which
            // is true for any internal emit flag.)
            var hasTransformableStatics = FactoryEmitNode.GetInternalEmitFlags(store, node) != 0;
            if (hasTransformableStatics)
            {
                hasTransformableStatics = false;
                foreach (var member in staticPropertiesOrClassStaticBlocks)
                {
                    if (store.Kind(member) == SyntaxKind.ClassStaticBlockDeclaration ||
                        TransformerUtilities.IsPrivateIdentifierClassElementDeclaration(store, member))
                    {
                        hasTransformableStatics = true;
                        break;
                    }
                }
            }
            return hasTransformableStatics;
        }
        return false;
    }

    private VisitResult VisitBinaryExpression(Node node, bool discarded)
    {
        if (Utilities.IsDestructuringAssignment(store, node))
        {
            // ({ x: obj.#x } = ...)
            // ({ x: super.x } = ...)
            // ({ x: super[x] } = ...)
            var savedPendingExpressions = pendingExpressions;
            pendingExpressions = null;
            node = factory.UpdateBinaryExpression(
                node,
                Visit(store.Left(node), assignmentTargetVisitor),
                store.OperatorToken(node),
                Visit(store.Right(node), visitor));
            Node expr;
            if (Core.Some(pendingExpressions))
            {
                var all = new List<Node>(pendingExpressions.Count + 1);
                foreach (var e in pendingExpressions)
                {
                    if (!e.IsNone)
                    {
                        all.Add(e);
                    }
                }
                all.Add(node);
                expr = factory.InlineExpressions(all);
            }
            else
            {
                expr = node;
            }
            pendingExpressions = savedPendingExpressions;
            return expr;
        }
        if (Utilities.IsAssignmentExpression(store, node))
        {
            if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
            {
                node = NamedEvaluation.TransformNamedEvaluation(context, node);
                Debug.Assert(Utilities.IsAssignmentExpression(store, node));
            }

            var left = FactoryUtilities.SkipOuterExpressions(store, store.Left(node), OuterExpressionKinds.PartiallyEmittedExpressions | OuterExpressionKinds.Parentheses);
            if (TransformerUtilities.IsPrivateIdentifierPropertyAccessExpression(store, left))
            {
                // obj.#x = ...
                var info = AccessPrivateIdentifier(store.Name(left));
                if (info is not null)
                {
                    return FactoryUtilitiesPublic.SetTextRange(
                        store,
                        NodeFactory.SetOriginalNode(
                            store,
                            CreatePrivateIdentifierAssignment(info, store.Expression(left), store.Right(node), store.Kind(store.OperatorToken(node))),
                            node),
                        node);
                }
            }
            // (`super` in static initializers: shouldTransformSuperInStaticInitializers is false.)
        }
        if (store.Kind(store.Left(node)) == SyntaxKind.PrivateIdentifier && store.Kind(store.OperatorToken(node)) == SyntaxKind.InKeyword)
        {
            // #x in obj
            return TransformPrivateIdentifierInInExpression(node);
        }
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitCommaListExpression(Node node, bool discarded)
    {
        var elements = discarded ?
            Visitors.VisitCommaListElements(factory, store.Elements(node), discardedValueVisitor) :
            Visitors.VisitCommaListElements(factory, store.Elements(node), visitor, discardedValueVisitor);
        return factory.UpdateCommaListExpression(node, elements);
    }

    private VisitResult VisitParenthesizedExpression(Node node, bool discarded)
    {
        var visitorFunc = discarded ? discardedValueVisitor : visitor;
        var expression = Visit(store.Expression(node), visitorFunc);
        return factory.UpdateParenthesizedExpression(node, expression);
    }

    private Node CreatePrivateIdentifierAssignment(PrivateIdentifierInfo info, Node receiver, Node right, SyntaxKind @operator)
    {
        receiver = Visit(receiver, visitor);
        right = Visit(right, visitor);
        EnsureDynamicThisIfNeeded(receiver);

        if (TransformerUtilities.IsCompoundAssignment(@operator))
        {
            var (readExpression, initializeExpression) = CreateCopiableReceiverExpr(receiver);
            receiver = initializeExpression.IsNone ? readExpression : initializeExpression;
            right = factory.CreateBinaryExpression(
                CreatePrivateIdentifierAccessHelper(info, readExpression),
                factory.CreateToken(TransformerUtilities.GetNonAssignmentOperatorForCompoundAssignment(@operator)),
                right);
        }

        FactoryEmitNode.SetCommentRange(store, receiver, MoveRangePosMinusOne(receiver));

        switch (info.Kind)
        {
            case PrivateIdentifierKind.Accessor:
                return context.GetEmitHelperFactory().CreateClassPrivateFieldSetHelper(
                    receiver,
                    info.BrandCheckIdentifier,
                    right,
                    info.Kind,
                    info.SetterName);
            case PrivateIdentifierKind.Method:
                return context.GetEmitHelperFactory().CreateClassPrivateFieldSetHelper(
                    receiver,
                    info.BrandCheckIdentifier,
                    right,
                    info.Kind,
                    Node.None);
            case PrivateIdentifierKind.Field:
                return context.GetEmitHelperFactory().CreateClassPrivateFieldSetHelper(
                    receiver,
                    info.BrandCheckIdentifier,
                    right,
                    info.Kind,
                    info.IsStatic ? info.VariableName : Node.None);
            case Untransformed:
                throw new InvalidOperationException("Debug Failure. Access helpers should not be created for untransformed private elements");
            default:
                throw new InvalidOperationException("Debug Failure. Unknown private element type");
        }
    }

    private ClassFacts GetClassFacts(Node node)
    {
        var facts = ClassFacts.None;
        var original = TransformerUtilities.GetOriginalNode(store, node);
        if (Utilities.IsClassLike(store, original) && TransformerUtilities.ClassOrConstructorParameterIsDecorated(store, useLegacyDecorators: false, original))
        {
            facts |= ClassFacts.ClassWasDecorated;
        }
        // (shouldTransformPrivateElementsOrClassStaticBlocks, shouldTransformThisInStaticInitializers,
        // shouldTransformSuperInStaticInitializers and the initializer transforms are false.)
        foreach (var member in store.Nodes(store.Members(node)))
        {
            if (Utilities.IsStatic(store, member))
            {
                // (the private-name branch needs shouldTransformPrivateElementsOrClassStaticBlocks, which is false)
                if (TransformerUtilities.IsAutoAccessorPropertyDeclaration(store, member) && ShouldTransformAutoAccessors && store.Name(node).IsNone && (store.EmitNode(node) as EmitNode)?.ClassThis.IsNone != false)
                {
                    facts |= ClassFacts.NeedsClassConstructorReference;
                }
            }
            else if (!Utilities.HasSyntacticModifier(store, TransformerUtilities.GetOriginalNode(store, member), ModifierFlags.Abstract))
            {
                if (TransformerUtilities.IsAutoAccessorPropertyDeclaration(store, member))
                {
                    // containsInstanceAutoAccessors, containsInstancePrivateElements: only read below ES2022
                }
                else if (TransformerUtilities.IsPrivateIdentifierClassElementDeclaration(store, member))
                {
                    if (resolver.HasNodeCheckFlag(member, NodeCheckFlags.ContainsConstructorReference))
                    {
                        facts |= ClassFacts.NeedsClassConstructorReference;
                    }
                }
            }
        }

        // willHoistInitializersToConstructor: false under these options

        return facts;
    }

    private VisitResult VisitExpressionWithTypeArgumentsInHeritageClause(Node node)
    {
        var facts = lexicalEnvironment?.Data?.Facts ?? ClassFacts.None;
        if ((facts & ClassFacts.NeedsClassSuperReference) != 0)
        {
            var temp = factory.CreateTempVariable(hoistVariableDeclaration, reservedInNestedScopes: true);
            GetClassLexicalEnvironment().SuperClassReference = temp;
            return factory.UpdateExpressionWithTypeArguments(
                node,
                factory.CreateAssignment(
                    temp,
                    Visit(store.Expression(node), visitor)),
                default);
        }
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private T VisitInNewClassLexicalEnvironment<T>(Node node, Func<Node, ClassFacts, T> classVisitor)
    {
        var savedCurrentClassContainer = currentClassContainer;
        var savedPendingExpressions = pendingExpressions;
        var savedLexicalEnvironment = lexicalEnvironment;
        currentClassContainer = node;
        pendingExpressions = null;
        StartClassLexicalEnvironment();

        var shouldAlwaysTransformPrivateStaticElements = (FactoryEmitNode.GetInternalEmitFlags(store, node) & InternalEmitFlags.TransformPrivateStaticElements) != 0;
        if (shouldAlwaysTransformPrivateStaticElements)
        {
            var name = Utilities.GetNameOfDeclaration(store, node);
            var assignedName = (store.EmitNode(node) as EmitNode)?.AssignedName ?? Node.None;
            if (!name.IsNone && store.Kind(name) == SyntaxKind.Identifier)
            {
                GetPrivateIdentifierEnvironment().Data.ClassName = name;
            }
            else if (!assignedName.IsNone)
            {
                if (store.Kind(assignedName) == SyntaxKind.StringLiteral)
                {
                    // If the class name was assigned from a string literal based on an Identifier, use the Identifier
                    // as the prefix.
                    var textSourceNode = store.TextSourceNode(assignedName);
                    if (!textSourceNode.IsNone && store.Kind(textSourceNode) == SyntaxKind.Identifier)
                    {
                        GetPrivateIdentifierEnvironment().Data.ClassName = textSourceNode;
                    }
                    // If the class name was assigned from a string literal that is a valid identifier, create an
                    // identifier from it.
                    else if (Scanner.IsIdentifierText(PrinterUtilities.TextOf(store, assignedName), languageVersion))
                    {
                        var prefixName = factory.CreateIdentifier(PrinterUtilities.TextOf(store, assignedName));
                        GetPrivateIdentifierEnvironment().Data.ClassName = prefixName;
                    }
                }
            }
        }

        var facts = GetClassFacts(node);
        if (facts != 0)
        {
            GetClassLexicalEnvironment().Facts = facts;
        }

        if ((facts & ClassFacts.NeedsSubstitutionForThisInClassStaticField) != 0)
        {
            EnableSubstitutionForClassStaticThisOrSuperReference();
        }

        var result = classVisitor(node, facts);
        EndClassLexicalEnvironment();
        Debug.Assert(lexicalEnvironment == savedLexicalEnvironment);
        currentClassContainer = savedCurrentClassContainer;
        pendingExpressions = savedPendingExpressions;
        return result;
    }

    private VisitResult VisitClassDeclaration(Node node)
    {
        return VisitInNewClassLexicalEnvironment(node, VisitClassDeclarationInNewClassLexicalEnvironment);
    }

    private VisitResult VisitClassDeclarationInNewClassLexicalEnvironment(Node node, ClassFacts facts)
    {
        // If a class has private static fields, or a static field has a `this` or `super` reference,
        // then we need to allocate a temp variable to hold on to that reference.
        var pendingClassReferenceAssignment = Node.None;
        if ((facts & ClassFacts.NeedsClassConstructorReference) != 0)
        {
            // If we aren't transforming class static blocks, then we can't reuse `_classThis` since in
            // `class C { ... static { _classThis = ... } }; _classThis = C` the outer assignment would occur *after*
            // class static blocks evaluate and would overwrite the replacement constructor produced by class
            // decorators.
            var temp = factory.CreateTempVariable(hoistVariableDeclaration, reservedInNestedScopes: true);
            GetClassLexicalEnvironment().ClassConstructor = factory.CloneNode(temp);
            pendingClassReferenceAssignment = factory.CreateAssignment(temp, factory.GetInternalName(node));
        }

        var nodeClassThis = (store.EmitNode(node) as EmitNode)?.ClassThis ?? Node.None;
        if (!nodeClassThis.IsNone)
        {
            GetClassLexicalEnvironment().ClassThis = nodeClassThis;
        }

        var isClassWithConstructorReference = resolver.HasNodeCheckFlag(node, NodeCheckFlags.ContainsConstructorReference);
        var isExport = Utilities.HasSyntacticModifier(store, node, ModifierFlags.Export);
        var isDefault = Utilities.HasSyntacticModifier(store, node, ModifierFlags.Default);
        var modifiers = VisitList(store.Modifiers(node), modifierVisitor);
        var heritageClauses = VisitList(store.HeritageClauses(node), heritageClauseVisitor);
        var (members, prologue) = TransformClassMembers(node);

        var statements = new List<Node>();
        if (!pendingClassReferenceAssignment.IsNone)
        {
            GetPendingExpressions().Insert(0, pendingClassReferenceAssignment);
        }

        // Write any pending expressions from elided or moved computed property names
        if (Core.Some(pendingExpressions))
        {
            statements.Add(factory.CreateExpressionStatement(factory.InlineExpressions(pendingExpressions)));
        }

        if ((FactoryEmitNode.GetInternalEmitFlags(store, node) & InternalEmitFlags.TransformPrivateStaticElements) != 0)
        {
            // Emit static property assignment. Because classDeclaration is lexically evaluated,
            // it is safe to emit static property assignment after classDeclaration
            var staticProperties = TransformerUtilities.GetStaticPropertiesAndClassStaticBlock(store, node);
            if (staticProperties.Count > 0)
            {
                AddPropertyOrClassStaticBlockStatements(statements, staticProperties, factory.GetInternalName(node));
            }
        }

        if (statements.Count > 0 && isExport && isDefault)
        {
            modifiers = VisitList(modifiers, n => store.Kind(n) is SyntaxKind.ExportKeyword or SyntaxKind.DefaultKeyword ? VisitResult.None : n);
            statements.Add(factory.CreateExportAssignment(
                default,
                isExportEquals: false,
                factory.GetLocalName(node, allowComments: false, allowSourceMaps: true)));
        }

        var alias = GetClassLexicalEnvironment().ClassConstructor;
        if (isClassWithConstructorReference && !alias.IsNone)
        {
            EnableSubstitutionForClassAliases();
            classAliases![TransformerUtilities.GetOriginalNode(store, node)] = alias;
        }

        var classDecl = factory.UpdateClassDeclaration(
            node,
            modifiers,
            store.Name(node),
            default,
            heritageClauses,
            members);
        statements.Insert(0, classDecl);

        if (!prologue.IsNone)
        {
            statements.Insert(0, factory.CreateExpressionStatement(prologue));
        }

        return statements.Count == 1 ? statements[0] : VisitResult.Many(statements);
    }

    private Node VisitClassExpression(Node node)
    {
        return VisitInNewClassLexicalEnvironment(node, VisitClassExpressionInNewClassLexicalEnvironment);
    }

    private Node VisitClassExpressionInNewClassLexicalEnvironment(Node node, ClassFacts facts)
    {
        // If this class expression is a transformation of a decorated class declaration,
        // then we want to output the pendingExpressions as statements, not as inlined
        // expressions with the class statement.
        //
        // In this case, we use pendingStatements to produce the same output as the
        // class declaration transformation. The VariableStatement visitor will insert
        // these statements after the class expression variable statement.
        var isDecoratedClassDeclaration = (facts & ClassFacts.ClassWasDecorated) != 0;
        var staticPropertiesOrClassStaticBlocks = TransformerUtilities.GetStaticPropertiesAndClassStaticBlock(store, node);
        var isClassWithConstructorReference = resolver.HasNodeCheckFlag(node, NodeCheckFlags.ContainsConstructorReference);
        var requiresBlockScopedVar = resolver.HasNodeCheckFlag(node, NodeCheckFlags.BlockScopedBindingInLoop);

        var temp = Node.None;
        Node CreateClassTempVar()
        {
            // If we aren't transforming class static blocks, then we can't reuse `_classThis` since in
            // `_classThis = class { ... static { _classThis = ... } }` the outer assignment would occur *after*
            // class static blocks evaluate and would overwrite the replacement constructor produced by class
            // decorators.
            var t = requiresBlockScopedVar ?
                factory.CreateTempVariable(context.AddBlockScopedVariable, reservedInNestedScopes: true) :
                factory.CreateTempVariable(hoistVariableDeclaration, reservedInNestedScopes: true);
            GetClassLexicalEnvironment().ClassConstructor = factory.CloneNode(t);
            return t;
        }

        var nodeClassThis = (store.EmitNode(node) as EmitNode)?.ClassThis ?? Node.None;
        if (!nodeClassThis.IsNone)
        {
            GetClassLexicalEnvironment().ClassThis = nodeClassThis;
        }

        if ((facts & ClassFacts.NeedsClassConstructorReference) != 0)
        {
            if (temp.IsNone)
            {
                temp = CreateClassTempVar();
            }
        }

        var modifiers = VisitList(store.Modifiers(node), modifierVisitor);
        var heritageClauses = VisitList(store.HeritageClauses(node), heritageClauseVisitor);
        var (members, prologue) = TransformClassMembers(node);
        var classExpression = factory.UpdateClassExpression(
            node,
            modifiers,
            store.Name(node),
            default,
            heritageClauses,
            members);

        var expressions = new List<Node>();
        if (!prologue.IsNone)
        {
            expressions.Add(prologue);
        }

        // Static initializers are transformed to `static {}` blocks when `useDefineForClassFields: false`
        // and not also transforming static blocks.
        var hasTransformableStatics = false;
        if ((FactoryEmitNode.GetInternalEmitFlags(store, node) & InternalEmitFlags.TransformPrivateStaticElements) != 0)
        {
            foreach (var member in staticPropertiesOrClassStaticBlocks)
            {
                if (store.Kind(member) == SyntaxKind.ClassStaticBlockDeclaration ||
                    TransformerUtilities.IsPrivateIdentifierClassElementDeclaration(store, member))
                {
                    hasTransformableStatics = true;
                    break;
                }
            }
        }

        if (hasTransformableStatics || Core.Some(pendingExpressions))
        {
            if (isDecoratedClassDeclaration)
            {
                Debug.AssertIsDefined(pendingStatements, "Decorated classes transformed by TypeScript are expected to be within a variable declaration.");

                // Write any pending expressions from elided or moved computed property names
                if (Core.Some(pendingExpressions))
                {
                    foreach (var pending in pendingExpressions)
                    {
                        pendingStatements.Add(factory.CreateExpressionStatement(pending));
                    }
                }

                if (staticPropertiesOrClassStaticBlocks.Count > 0)
                {
                    AddPropertyOrClassStaticBlockStatements(pendingStatements, staticPropertiesOrClassStaticBlocks, !nodeClassThis.IsNone ? nodeClassThis : factory.GetInternalName(node));
                }

                if (!temp.IsNone)
                {
                    expressions.Add(factory.CreateAssignment(temp, classExpression));
                }
                else
                {
                    expressions.Add(classExpression);
                }
            }
            else
            {
                if (temp.IsNone)
                {
                    temp = CreateClassTempVar();
                }
                if (isClassWithConstructorReference)
                {
                    // record an alias as the class name is not in scope for statics.
                    EnableSubstitutionForClassAliases();
                    var alias = factory.CloneNode(temp);
                    FactoryEmitNode.GetIdentifierAutoGenerate(store, alias)!.Flags &= ~GeneratedIdentifierFlags.ReservedInNestedScopes;
                    classAliases![TransformerUtilities.GetOriginalNode(store, node)] = alias;
                }

                expressions.Add(factory.CreateAssignment(temp, classExpression));

                // Add any pending expressions leftover from elided or relocated computed property names
                if (pendingExpressions is not null)
                {
                    expressions.AddRange(pendingExpressions);
                }
                expressions.AddRange(GenerateInitializedPropertyExpressionsOrClassStaticBlock(staticPropertiesOrClassStaticBlocks, temp));
                expressions.Add(factory.CloneNode(temp));
            }
        }
        else
        {
            expressions.Add(classExpression);
        }

        if (expressions.Count > 1)
        {
            // To preserve the behavior of the old emitter, we explicitly indent
            // the body of a class with static initializers.
            FactoryEmitNode.AddEmitFlags(store, classExpression, EmitFlags.Indented);
            foreach (var expression in expressions)
            {
                FactoryUtilities.StartOnNewLine(store, expression);
            }
        }

        return factory.InlineExpressions(expressions);
    }

    private VisitResult VisitClassStaticBlockDeclaration(Node node)
    {
        // (shouldTransformPrivateElementsOrClassStaticBlocks is false)
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private static VisitResult VisitThisExpression(Node node)
    {
        // (shouldTransformThisInStaticInitializers is false)
        return node;
    }

    private (NodeList Members, Node Prologue) TransformClassMembers(Node node)
    {
        var shouldTransformPrivateStaticElementsInClass = (FactoryEmitNode.GetInternalEmitFlags(store, node) & InternalEmitFlags.TransformPrivateStaticElements) != 0;

        // Declare private names
        if (shouldTransformPrivateStaticElementsInFile)
        {
            foreach (var member in store.Nodes(store.Members(node)))
            {
                if (TransformerUtilities.IsPrivateIdentifierClassElementDeclaration(store, member))
                {
                    if (ShouldTransformClassElementToWeakMap(member))
                    {
                        AddPrivateIdentifierToEnvironment(member, store.Name(member));
                    }
                    else
                    {
                        var privateEnv = GetPrivateIdentifierEnvironment();
                        SetPrivateIdentifier(privateEnv, store.Name(member), new PrivateIdentifierInfo { Kind = Untransformed });
                    }
                }
            }

            if (ShouldTransformAutoAccessorsInCurrentClass())
            {
                foreach (var member in store.Nodes(store.Members(node)))
                {
                    if (TransformerUtilities.IsAutoAccessorPropertyDeclaration(store, member))
                    {
                        var storageName = factory.GetGeneratedPrivateNameForNode(store.Name(member), prefix: null, "_accessor_storage");
                        if (shouldTransformPrivateStaticElementsInClass && TransformerUtilities.HasStaticModifier(store, member))
                        {
                            AddPrivateIdentifierToEnvironment(member, storageName, propertyDeclaration: true);
                        }
                        else
                        {
                            var privateEnv = GetPrivateIdentifierEnvironment();
                            SetPrivateIdentifier(privateEnv, storageName, new PrivateIdentifierInfo { Kind = Untransformed });
                        }
                    }
                }
            }
        }

        var members = VisitList(store.Members(node), classElementVisitor);

        // Create a synthetic constructor if necessary
        var syntheticConstructor = Node.None;
        var hasConstructor = false;
        foreach (var member in store.Nodes(members))
        {
            if (store.Kind(member) == SyntaxKind.Constructor)
            {
                hasConstructor = true;
                break;
            }
        }
        if (!hasConstructor)
        {
            syntheticConstructor = TransformConstructor(Node.None, node);
        }

        var prologue = Node.None;

        // If there are pending expressions create a class static block in which to evaluate them, but only if
        // class static blocks are not also being transformed. This block will be injected at the top of the class
        // to ensure that expressions from computed property names are evaluated before any other static
        // initializers.
        var syntheticStaticBlock = Node.None;
        if (Core.Some(pendingExpressions))
        {
            var statement = factory.CreateExpressionStatement(factory.InlineExpressions(pendingExpressions));
            if ((store.TransformFlags(statement) & (TransformFlags.ContainsLexicalThis | TransformFlags.ContainsLexicalSuper)) != 0)
            {
                // If there are `this` or `super` references from computed property names, shift the expression
                // into an arrow function to be evaluated in the outer scope so that `this` and `super` are
                // properly captured.
                var temp = factory.CreateTempVariable(hoistVariableDeclaration);
                var arrow = factory.CreateArrowFunction(
                    default,
                    default,
                    factory.CreateNodeArray(ReadOnlySpan<Node>.Empty),
                    default,
                    default,
                    factory.CreateBlock(factory.CreateNodeArray(new[] { statement })));
                prologue = factory.CreateAssignment(temp, arrow);
                statement = factory.CreateExpressionStatement(factory.CreateCallExpression(temp, default, factory.CreateNodeArray(ReadOnlySpan<Node>.Empty)));
            }

            var block = factory.CreateBlock(factory.CreateNodeArray(new[] { statement }));
            syntheticStaticBlock = factory.CreateClassStaticBlockDeclaration(block);
            pendingExpressions = null;
        }

        // If we created a synthetic constructor or class static block, add them to the visited members
        // and return a new array.
        if (!syntheticConstructor.IsNone || !syntheticStaticBlock.IsNone)
        {
            var membersArray = new List<Node>();
            var classThisAssignmentBlock = Node.None;
            var classNamedEvaluationHelperBlock = Node.None;
            foreach (var member in store.Nodes(members))
            {
                if (classThisAssignmentBlock.IsNone && NamedEvaluation.IsClassThisAssignmentBlock(store, member))
                {
                    classThisAssignmentBlock = member;
                }
                if (classNamedEvaluationHelperBlock.IsNone && NamedEvaluation.IsClassNamedEvaluationHelperBlock(store, member))
                {
                    classNamedEvaluationHelperBlock = member;
                }
            }
            if (!classThisAssignmentBlock.IsNone)
            {
                membersArray.Add(classThisAssignmentBlock);
            }
            if (!classNamedEvaluationHelperBlock.IsNone)
            {
                membersArray.Add(classNamedEvaluationHelperBlock);
            }
            if (!syntheticConstructor.IsNone)
            {
                membersArray.Add(syntheticConstructor);
            }
            if (!syntheticStaticBlock.IsNone)
            {
                membersArray.Add(syntheticStaticBlock);
            }
            foreach (var member in store.Nodes(members))
            {
                if (classThisAssignmentBlock.IsNone && classNamedEvaluationHelperBlock.IsNone || member != classThisAssignmentBlock && member != classNamedEvaluationHelperBlock)
                {
                    membersArray.Add(member);
                }
            }
            members = FactoryUtilitiesPublic.SetTextRange(store, factory.CreateNodeArray(membersArray), store.Members(node));
        }

        return (members, prologue);
    }

    private Node TransformConstructor(Node constructor, Node container)
    {
        constructor = Visit(constructor, visitor);
        if (lexicalEnvironment?.Data is null || (lexicalEnvironment.Data.Facts & ClassFacts.WillHoistInitializersToConstructor) == 0)
        {
            return constructor;
        }

        throw new NotSupportedException("transformConstructorBody: initializers hoisted to the constructor are not Phase 4 options.");
    }

    /// <summary>Generates assignment statements for property initializers.</summary>
    private void AddPropertyOrClassStaticBlockStatements(List<Node> statements, List<Node> properties, Node receiver)
    {
        foreach (var property in properties)
        {
            if (Utilities.IsStatic(store, property))
            {
                // (!shouldTransformPrivateElementsOrClassStaticBlocks)
                continue;
            }

            var statement = TransformPropertyOrClassStaticBlock(property, receiver);
            if (statement.IsNone)
            {
                continue;
            }

            statements.Add(statement);
        }
    }

    private Node TransformPropertyOrClassStaticBlock(Node property, Node receiver)
    {
        var expression = store.Kind(property) == SyntaxKind.ClassStaticBlockDeclaration ?
            SetCurrentClassElementAnd(property, n => TransformClassStaticBlockDeclaration(n), property).Node :
            TransformProperty(property, receiver);
        if (expression.IsNone)
        {
            return Node.None;
        }

        var statement = factory.CreateExpressionStatement(expression);
        NodeFactory.SetOriginalNode(store, statement, property);
        FactoryEmitNode.AddEmitFlags(store, statement, Utilities.GetEmitFlags(store, property) & EmitFlags.NoComments);
        FactoryEmitNode.SetCommentRange(store, statement, property);

        var propertyOriginalNode = TransformerUtilities.GetOriginalNode(store, property);
        if (store.Kind(propertyOriginalNode) == SyntaxKind.Parameter)
        {
            // replicate comment and source map behavior from the ts transform for parameter properties.
            FactoryEmitNode.SetSourceMapRange(store, statement, TransformerUtilities.RangeOf(store, propertyOriginalNode));
            FactoryEmitNode.RemoveAllComments(store, statement);
        }
        else
        {
            FactoryEmitNode.SetSourceMapRange(store, statement, TransformerUtilities.MoveRangePastModifiers(store, property));
        }

        // `setOriginalNode` *copies* the `emitNode` from `property`, so now both
        // `statement` and `expression` have a copy of the synthesized comments.
        // Drop the comments from expression to avoid printing them twice.
        FactoryEmitNode.SetSyntheticLeadingComments(store, expression, null);
        FactoryEmitNode.SetSyntheticTrailingComments(store, expression, null);

        // If the property was originally an auto-accessor, don't emit comments here since they will be attached to
        // the synthezized getter.
        if (TransformerUtilities.HasAccessorModifier(store, propertyOriginalNode))
        {
            FactoryEmitNode.AddEmitFlags(store, statement, EmitFlags.NoComments);
        }

        return statement;
    }

    /// <summary>Generates assignment expressions for property initializers.</summary>
    private List<Node> GenerateInitializedPropertyExpressionsOrClassStaticBlock(List<Node> propertiesOrClassStaticBlocks, Node receiver)
    {
        var expressions = new List<Node>();
        foreach (var property in propertiesOrClassStaticBlocks)
        {
            var expression = store.Kind(property) == SyntaxKind.ClassStaticBlockDeclaration ?
                SetCurrentClassElementAnd(property, n => TransformClassStaticBlockDeclaration(n), property).Node :
                SetCurrentClassElementAnd(property, _ => TransformProperty(property, receiver), Node.None).Node;
            if (expression.IsNone)
            {
                continue;
            }
            FactoryUtilities.StartOnNewLine(store, expression);
            NodeFactory.SetOriginalNode(store, expression, property);
            FactoryEmitNode.AddEmitFlags(store, expression, Utilities.GetEmitFlags(store, property) & EmitFlags.NoComments);
            FactoryEmitNode.SetSourceMapRange(store, expression, TransformerUtilities.MoveRangePastModifiers(store, property));
            FactoryEmitNode.SetCommentRange(store, expression, property);
            expressions.Add(expression);
        }

        return expressions;
    }

    /// <summary>Transforms a property initializer into an assignment statement.</summary>
    private Node TransformProperty(Node property, Node receiver)
    {
        var savedCurrentClassElement = currentClassElement;
        var transformed = TransformPropertyWorker(property, receiver);
        if (!transformed.IsNone && TransformerUtilities.HasStaticModifier(store, property) && (lexicalEnvironment?.Data?.Facts ?? ClassFacts.None) != 0)
        {
            // capture the lexical environment for the member
            NodeFactory.SetOriginalNode(store, transformed, property);
            FactoryEmitNode.AddEmitFlags(store, transformed, EmitFlags.AdviseOnEmitNode);
            FactoryEmitNode.SetSourceMapRange(store, transformed, FactoryEmitNode.GetSourceMapRange(store, store.Name(property)));
            lexicalEnvironmentMap[TransformerUtilities.GetOriginalNode(store, property)] = lexicalEnvironment!;
        }
        currentClassElement = savedCurrentClassElement;
        return transformed;
    }

    private Node TransformPropertyWorker(Node property, Node receiver)
    {
        // We generate a name here in order to reuse the value cached by the relocated computed name expression (which uses the same generated name)
        var emitAssignment = !useDefineForClassFields;

        if (NamedEvaluation.IsNamedEvaluation(store, property, IsAnonymousClassNeedingAssignedName))
        {
            property = NamedEvaluation.TransformNamedEvaluation(context, property);
        }

        var propertyNameNode = store.Name(property);
        var propertyName = TransformerUtilities.HasAccessorModifier(store, property) ?
            factory.GetGeneratedPrivateNameForNode(propertyNameNode) :
            store.Kind(propertyNameNode) == SyntaxKind.ComputedPropertyName && !TransformerUtilities.IsSimpleInlineableExpression(store, store.Expression(propertyNameNode)) ?
            factory.UpdateComputedPropertyName(propertyNameNode, factory.GetGeneratedNameForNode(propertyNameNode)) :
            propertyNameNode;

        if (TransformerUtilities.HasStaticModifier(store, property))
        {
            currentClassElement = property;
        }

        if (store.Kind(propertyName) == SyntaxKind.PrivateIdentifier && ShouldTransformClassElementToWeakMap(property))
        {
            var privateIdentifierInfo = AccessPrivateIdentifier(propertyName);
            if (privateIdentifierInfo is not null)
            {
                if (privateIdentifierInfo.Kind == PrivateIdentifierKind.Field)
                {
                    if (!privateIdentifierInfo.IsStatic)
                    {
                        return CreatePrivateInstanceFieldInitializer(
                            receiver,
                            Visit(store.Initializer(property), visitor),
                            privateIdentifierInfo.BrandCheckIdentifier);
                    }
                    else
                    {
                        return CreatePrivateStaticFieldInitializer(
                            privateIdentifierInfo.VariableName,
                            Visit(store.Initializer(property), visitor));
                    }
                }
                else
                {
                    return Node.None;
                }
            }
            else
            {
                throw new InvalidOperationException("Debug Failure. Undeclared private name for property declaration.");
            }
        }
        if ((store.Kind(propertyName) == SyntaxKind.PrivateIdentifier || TransformerUtilities.HasStaticModifier(store, property)) && store.Initializer(property).IsNone)
        {
            return Node.None;
        }

        var propertyOriginalNode = TransformerUtilities.GetOriginalNode(store, property);
        if (Utilities.HasSyntacticModifier(store, propertyOriginalNode, ModifierFlags.Abstract))
        {
            return Node.None;
        }

        var initializer = Visit(store.Initializer(property), visitor);
        if (Utilities.IsParameterPropertyDeclaration(store, propertyOriginalNode, store.Parent(propertyOriginalNode)) && store.Kind(propertyName) == SyntaxKind.Identifier)
        {
            // A parameter-property declaration always overrides the initializer. The only time a parameter-property
            // declaration *should* have an initializer is when decorators have added initializers that need to run before
            // any other initializer
            var localName = factory.CloneNode(propertyName);
            if (!initializer.IsNone)
            {
                // unwrap `(__runInitializers(this, _instanceExtraInitializers), void 0)`
                if (store.Kind(initializer) == SyntaxKind.ParenthesizedExpression &&
                    FactoryUtilities.IsCommaExpression(store, store.Expression(initializer)) &&
                    FactoryUtilities.IsCallToHelper(store, store.Left(store.Expression(initializer)), "___runInitializers") &&
                    store.Kind(store.Right(store.Expression(initializer))) == SyntaxKind.VoidExpression &&
                    store.Kind(store.Expression(store.Right(store.Expression(initializer)))) == SyntaxKind.NumericLiteral)
                {
                    initializer = store.Left(store.Expression(initializer));
                }
                initializer = factory.InlineExpressions([initializer, localName]);
            }
            else
            {
                initializer = localName;
            }
            FactoryEmitNode.SetEmitFlags(store, propertyName, EmitFlags.NoComments | EmitFlags.NoSourceMap);
            FactoryEmitNode.SetSourceMapRange(store, localName, TransformerUtilities.RangeOf(store, store.Name(propertyOriginalNode)));
            FactoryEmitNode.SetEmitFlags(store, localName, EmitFlags.NoComments);
        }
        else if (initializer.IsNone)
        {
            initializer = factory.CreateVoidZero();
        }

        if (emitAssignment || store.Kind(propertyName) == SyntaxKind.PrivateIdentifier)
        {
            var memberAccess = FactoryUtilities.CreateMemberAccessForPropertyName(factory, receiver, propertyName, propertyName);
            FactoryEmitNode.AddEmitFlags(store, memberAccess, EmitFlags.NoLeadingComments);
            var expression = factory.CreateAssignment(memberAccess, initializer);
            return expression;
        }
        else
        {
            var name = store.Kind(propertyName) == SyntaxKind.ComputedPropertyName ? store.Expression(propertyName)
                : store.Kind(propertyName) == SyntaxKind.Identifier ? factory.CreateStringLiteral(store.Texts.Add(UtilitiesPublic.UnescapeLeadingUnderscores(PrinterUtilities.IdText(store, propertyName))))
                : propertyName;
            var descriptor = factory.CreatePropertyDescriptor(new NodeFactory.PropertyDescriptorAttributes { Value = initializer, Configurable = true, Writable = true, Enumerable = true });
            return factory.CreateObjectDefinePropertyCall(receiver, name, descriptor);
        }
    }

    private void EnableSubstitutionForClassAliases()
    {
        if ((enabledSubstitutions & ClassPropertySubstitutionFlags.ClassAliases) == 0)
        {
            enabledSubstitutions |= ClassPropertySubstitutionFlags.ClassAliases;

            // We need to enable substitutions for identifiers. This allows us to
            // substitute class names inside of a class declaration.
            context.EnableSubstitution(SyntaxKind.Identifier);

            // Keep track of class aliases.
            classAliases = [];
        }
    }

    private void EnableSubstitutionForClassStaticThisOrSuperReference()
    {
        if ((enabledSubstitutions & ClassPropertySubstitutionFlags.ClassStaticThisOrSuperReference) == 0)
        {
            enabledSubstitutions |= ClassPropertySubstitutionFlags.ClassStaticThisOrSuperReference;

            // substitute `this` in a static field initializer
            context.EnableSubstitution(SyntaxKind.ThisKeyword);
            context.EnableEmitNotification(SyntaxKind.FunctionDeclaration);
            context.EnableEmitNotification(SyntaxKind.FunctionExpression);
            context.EnableEmitNotification(SyntaxKind.Constructor);
            context.EnableEmitNotification(SyntaxKind.GetAccessor);
            context.EnableEmitNotification(SyntaxKind.SetAccessor);
            context.EnableEmitNotification(SyntaxKind.MethodDeclaration);
            context.EnableEmitNotification(SyntaxKind.PropertyDeclaration);
            context.EnableEmitNotification(SyntaxKind.ComputedPropertyName);
        }
    }

    private void StartClassLexicalEnvironment()
    {
        lexicalEnvironment = new LexicalEnv { Previous = lexicalEnvironment };
    }

    private void EndClassLexicalEnvironment()
    {
        lexicalEnvironment = lexicalEnvironment?.Previous;
    }

    private ClassLexicalEnvironment GetClassLexicalEnvironment()
    {
        Debug.Assert(lexicalEnvironment is not null);
        return lexicalEnvironment.Data ??= new ClassLexicalEnvironment
        {
            Facts = ClassFacts.None,
            ClassConstructor = Node.None,
            ClassThis = Node.None,
            SuperClassReference = Node.None,
        };
    }

    private PrivateEnvironment GetPrivateIdentifierEnvironment()
    {
        Debug.Assert(lexicalEnvironment is not null);
        return lexicalEnvironment.PrivateEnv ??= new PrivateEnvironment();
    }

    private List<Node> GetPendingExpressions()
    {
        return pendingExpressions ??= [];
    }

    // transformers/utilities.ts getPrivateIdentifier
    private PrivateIdentifierInfo? GetPrivateIdentifier(PrivateEnvironment? privateEnv, Node name)
    {
        if (TransformerUtilities.IsGeneratedPrivateIdentifier(store, name))
        {
            return privateEnv?.GeneratedIdentifiers is { } generated && generated.TryGetValue(TransformerUtilities.GetNodeForGeneratedName(store, name), out var entry) ? entry : null;
        }
        return privateEnv?.Identifiers is { } identifiers && identifiers.TryGetValue(store.Texts.GetString(store.EscapedText(name))!, out var info) ? info : null;
    }

    // transformers/utilities.ts setPrivateIdentifier
    private void SetPrivateIdentifier(PrivateEnvironment privateEnv, Node name, PrivateIdentifierInfo entry)
    {
        if (TransformerUtilities.IsGeneratedPrivateIdentifier(store, name))
        {
            (privateEnv.GeneratedIdentifiers ??= [])[TransformerUtilities.GetNodeForGeneratedName(store, name)] = entry;
        }
        else
        {
            (privateEnv.Identifiers ??= new Dictionary<string, PrivateIdentifierInfo>(StringComparer.Ordinal))[store.Texts.GetString(store.EscapedText(name))!] = entry;
        }
    }

    // transformers/utilities.ts accessPrivateIdentifier (walkUpLexicalEnvironments)
    private PrivateIdentifierInfo? AccessPrivateIdentifierCommon(LexicalEnv? env, Node name)
    {
        while (env is not null)
        {
            var result = GetPrivateIdentifier(env.PrivateEnv, name);
            if (result is not null)
            {
                return result;
            }
            env = env.Previous;
        }
        return null;
    }

    /// <summary>Access an already defined PrivateIdentifier in the current PrivateIdentifierEnvironment.</summary>
    private PrivateIdentifierInfo? AccessPrivateIdentifier(Node name)
    {
        var info = AccessPrivateIdentifierCommon(lexicalEnvironment, name);
        return info?.Kind == Untransformed ? null : info;
    }

    // addPrivateIdentifierToEnvironment with addPrivateIdentifierClassElementToEnvironment (or, for the storage name of
    // an auto-accessor, addPrivateIdentifierPropertyDeclarationToEnvironment)
    private void AddPrivateIdentifierToEnvironment(Node node, Node name, bool propertyDeclaration = false)
    {
        var lex = GetClassLexicalEnvironment();
        var privateEnv = GetPrivateIdentifierEnvironment();
        var previousInfo = GetPrivateIdentifier(privateEnv, name);
        var isStatic = TransformerUtilities.HasStaticModifier(store, node);
        var isValid = !IsReservedPrivateName(name) && previousInfo is null;
        if (propertyDeclaration)
        {
            AddPrivateIdentifierPropertyDeclarationToEnvironment(name, lex, privateEnv, isStatic, isValid);
        }
        else
        {
            AddPrivateIdentifierClassElementToEnvironment(node, name, lex, privateEnv, isStatic, isValid, previousInfo);
        }
    }

    private void AddPrivateIdentifierClassElementToEnvironment(Node node, Node name, ClassLexicalEnvironment lex, PrivateEnvironment privateEnv, bool isStatic, bool isValid, PrivateIdentifierInfo? previousInfo)
    {
        if (TransformerUtilities.IsAutoAccessorPropertyDeclaration(store, node))
        {
            AddPrivateIdentifierAutoAccessorPropertyDeclarationToEnvironment(name, lex, privateEnv, isStatic, isValid);
        }
        else if (store.Kind(node) == SyntaxKind.PropertyDeclaration)
        {
            AddPrivateIdentifierPropertyDeclarationToEnvironment(name, lex, privateEnv, isStatic, isValid);
        }
        else if (store.Kind(node) == SyntaxKind.MethodDeclaration)
        {
            AddPrivateIdentifierMethodDeclarationToEnvironment(name, lex, privateEnv, isStatic, isValid);
        }
        else if (store.Kind(node) == SyntaxKind.GetAccessor)
        {
            AddPrivateIdentifierGetAccessorDeclarationToEnvironment(name, lex, privateEnv, isStatic, isValid, previousInfo);
        }
        else if (store.Kind(node) == SyntaxKind.SetAccessor)
        {
            AddPrivateIdentifierSetAccessorDeclarationToEnvironment(name, lex, privateEnv, isStatic, isValid, previousInfo);
        }
    }

    private static Node StaticBrandCheckIdentifier(ClassLexicalEnvironment lex)
    {
        var brandCheckIdentifier = !lex.ClassThis.IsNone ? lex.ClassThis : lex.ClassConstructor;
        Debug.Assert(!brandCheckIdentifier.IsNone, "classConstructor should be set in private identifier environment");
        return brandCheckIdentifier;
    }

    // privateEnv.data.weakSetName: set for the private instance methods and accessors of targets below ES2022 only.
    private static Node WeakSetName(PrivateEnvironment privateEnv)
    {
        _ = privateEnv;
        throw new InvalidOperationException("Debug Failure. weakSetName should be set in private identifier environment");
    }

    private void AddPrivateIdentifierPropertyDeclarationToEnvironment(Node name, ClassLexicalEnvironment lex, PrivateEnvironment privateEnv, bool isStatic, bool isValid)
    {
        if (isStatic)
        {
            var brandCheckIdentifier = StaticBrandCheckIdentifier(lex);

            var variableName = CreateHoistedVariableForPrivateName(name);
            SetPrivateIdentifier(privateEnv, name, new PrivateIdentifierInfo
            {
                Kind = PrivateIdentifierKind.Field,
                IsStatic = true,
                BrandCheckIdentifier = brandCheckIdentifier,
                VariableName = variableName,
                IsValid = isValid,
            });
        }
        else
        {
            var weakMapName = CreateHoistedVariableForPrivateName(name);

            SetPrivateIdentifier(privateEnv, name, new PrivateIdentifierInfo
            {
                Kind = PrivateIdentifierKind.Field,
                IsStatic = false,
                BrandCheckIdentifier = weakMapName,
                IsValid = isValid,
            });

            GetPendingExpressions().Add(factory.CreateAssignment(
                weakMapName,
                factory.CreateNewExpression(
                    factory.CreateIdentifier("WeakMap"),
                    default,
                    factory.CreateNodeArray(ReadOnlySpan<Node>.Empty))));
        }
    }

    private void AddPrivateIdentifierMethodDeclarationToEnvironment(Node name, ClassLexicalEnvironment lex, PrivateEnvironment privateEnv, bool isStatic, bool isValid)
    {
        var methodName = CreateHoistedVariableForPrivateName(name);
        var brandCheckIdentifier = isStatic ? StaticBrandCheckIdentifier(lex) : WeakSetName(privateEnv);

        SetPrivateIdentifier(privateEnv, name, new PrivateIdentifierInfo
        {
            Kind = PrivateIdentifierKind.Method,
            MethodName = methodName,
            BrandCheckIdentifier = brandCheckIdentifier,
            IsStatic = isStatic,
            IsValid = isValid,
        });
    }

    private void AddPrivateIdentifierGetAccessorDeclarationToEnvironment(Node name, ClassLexicalEnvironment lex, PrivateEnvironment privateEnv, bool isStatic, bool isValid, PrivateIdentifierInfo? previousInfo)
    {
        var getterName = CreateHoistedVariableForPrivateName(name, "_get");
        var brandCheckIdentifier = isStatic ? StaticBrandCheckIdentifier(lex) : WeakSetName(privateEnv);

        if (previousInfo?.Kind == PrivateIdentifierKind.Accessor && previousInfo.IsStatic == isStatic && previousInfo.GetterName.IsNone)
        {
            previousInfo.GetterName = getterName;
        }
        else
        {
            SetPrivateIdentifier(privateEnv, name, new PrivateIdentifierInfo
            {
                Kind = PrivateIdentifierKind.Accessor,
                GetterName = getterName,
                SetterName = Node.None,
                BrandCheckIdentifier = brandCheckIdentifier,
                IsStatic = isStatic,
                IsValid = isValid,
            });
        }
    }

    private void AddPrivateIdentifierSetAccessorDeclarationToEnvironment(Node name, ClassLexicalEnvironment lex, PrivateEnvironment privateEnv, bool isStatic, bool isValid, PrivateIdentifierInfo? previousInfo)
    {
        var setterName = CreateHoistedVariableForPrivateName(name, "_set");
        var brandCheckIdentifier = isStatic ? StaticBrandCheckIdentifier(lex) : WeakSetName(privateEnv);

        if (previousInfo?.Kind == PrivateIdentifierKind.Accessor && previousInfo.IsStatic == isStatic && previousInfo.SetterName.IsNone)
        {
            previousInfo.SetterName = setterName;
        }
        else
        {
            SetPrivateIdentifier(privateEnv, name, new PrivateIdentifierInfo
            {
                Kind = PrivateIdentifierKind.Accessor,
                GetterName = Node.None,
                SetterName = setterName,
                BrandCheckIdentifier = brandCheckIdentifier,
                IsStatic = isStatic,
                IsValid = isValid,
            });
        }
    }

    private void AddPrivateIdentifierAutoAccessorPropertyDeclarationToEnvironment(Node name, ClassLexicalEnvironment lex, PrivateEnvironment privateEnv, bool isStatic, bool isValid)
    {
        var getterName = CreateHoistedVariableForPrivateName(name, "_get");
        var setterName = CreateHoistedVariableForPrivateName(name, "_set");
        var brandCheckIdentifier = isStatic ? StaticBrandCheckIdentifier(lex) : WeakSetName(privateEnv);

        SetPrivateIdentifier(privateEnv, name, new PrivateIdentifierInfo
        {
            Kind = PrivateIdentifierKind.Accessor,
            GetterName = getterName,
            SetterName = setterName,
            BrandCheckIdentifier = brandCheckIdentifier,
            IsStatic = isStatic,
            IsValid = isValid,
        });
    }

    // createHoistedVariableForClass(name: string | PrivateIdentifier | undefined, node, suffix)
    private Node CreateHoistedVariableForClass(string? nameText, Node nameNode, Node node, string? suffix = null)
    {
        var className = GetPrivateIdentifierEnvironment().Data.ClassName;
        var prefixPart = !className.IsNone ? new GeneratedNamePart { Prefix = "_", Node = className, Suffix = "_" } : null;
        Node identifier;
        if (!nameNode.IsNone)
        {
            identifier = prefixPart is not null ?
                factory.GetGeneratedNameForNode(nameNode, GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.ReservedInNestedScopes, prefixPart, suffix) :
                factory.GetGeneratedNameForNode(nameNode, GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.ReservedInNestedScopes, "_", suffix);
        }
        else if (nameText is not null)
        {
            identifier = prefixPart is not null ?
                factory.CreateUniqueName(nameText, GeneratedIdentifierFlags.Optimistic, prefixPart, suffix) :
                factory.CreateUniqueName(nameText, GeneratedIdentifierFlags.Optimistic, "_", suffix);
        }
        else
        {
            identifier = prefixPart is not null ?
                factory.CreateTempVariable(reservedInNestedScopes: true, prefixPart, suffix) :
                factory.CreateTempVariable(null, reservedInNestedScopes: true, "_", suffix);
        }

        if (resolver.HasNodeCheckFlag(node, NodeCheckFlags.BlockScopedBindingInLoop))
        {
            context.AddBlockScopedVariable(identifier);
        }
        else
        {
            hoistVariableDeclaration(identifier);
        }

        return identifier;
    }

    private Node CreateHoistedVariableForPrivateName(Node name, string? suffix = null)
    {
        var text = Utilities.TryGetTextOfPropertyName(store, name);
        return text.IsNone ?
            CreateHoistedVariableForClass(null, name, name, suffix) :
            CreateHoistedVariableForClass(store.Texts.GetString(text)![1..], Node.None, name, suffix);
    }

    private Node WrapPrivateIdentifierForDestructuringTarget(Node node)
    {
        var parameter = factory.GetGeneratedNameForNode(node);
        var info = AccessPrivateIdentifier(store.Name(node));
        if (info is null)
        {
            return Visitors.VisitEachChild(node, visitor, context);
        }
        var receiver = store.Expression(node);
        // We cannot copy `this` or `super` into the function because they will be bound
        // differently inside the function.
        if (store.Kind(receiver) == SyntaxKind.ThisKeyword || Utilities.IsSuperProperty(store, node) || !TransformerUtilities.IsSimpleCopiableExpression(store, receiver))
        {
            receiver = factory.CreateTempVariable(hoistVariableDeclaration, reservedInNestedScopes: true);
            GetPendingExpressions().Add(factory.CreateBinaryExpression(receiver, factory.CreateToken(SyntaxKind.EqualsToken), Visit(store.Expression(node), visitor)));
        }
        return factory.CreateAssignmentTargetWrapper(
            parameter,
            CreatePrivateIdentifierAssignment(
                info,
                receiver,
                parameter,
                SyntaxKind.EqualsToken));
    }

    private Node VisitDestructuringAssignmentTarget(Node node)
    {
        if (store.Kind(node) is SyntaxKind.ObjectLiteralExpression or SyntaxKind.ArrayLiteralExpression)
        {
            return VisitAssignmentPattern(node);
        }

        if (TransformerUtilities.IsPrivateIdentifierPropertyAccessExpression(store, node))
        {
            return WrapPrivateIdentifierForDestructuringTarget(node);
        }
        // (`super` in static initializers: shouldTransformSuperInStaticInitializers is false.)
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private Node VisitAssignmentElement(Node node)
    {
        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node);
        }
        if (Utilities.IsAssignmentExpression(store, node, excludeCompoundAssignment: true))
        {
            var left = VisitDestructuringAssignmentTarget(store.Left(node));
            var right = Visit(store.Right(node), visitor);
            return factory.UpdateBinaryExpression(node, left, store.OperatorToken(node), right);
        }
        return VisitDestructuringAssignmentTarget(node);
    }

    private Node VisitAssignmentRestElement(Node node)
    {
        if (UtilitiesPublic.IsLeftHandSideExpression(store, store.Expression(node)))
        {
            var expression = VisitDestructuringAssignmentTarget(store.Expression(node));
            return factory.UpdateSpreadElement(node, expression);
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitArrayAssignmentElement(Node node)
    {
        if (TransformerUtilities.IsArrayBindingOrAssignmentElement(store, node))
        {
            if (store.Kind(node) == SyntaxKind.SpreadElement)
            {
                return VisitAssignmentRestElement(node);
            }
            if (store.Kind(node) != SyntaxKind.OmittedExpression)
            {
                return VisitAssignmentElement(node);
            }
        }
        return Visitors.VisitEachChild(node, visitor, context);
    }

    private Node VisitAssignmentProperty(Node node)
    {
        // AssignmentProperty : PropertyName `:` AssignmentElement
        // AssignmentElement : DestructuringAssignmentTarget Initializer?

        var name = Visit(store.Name(node), visitor);
        var nodeInitializer = store.Initializer(node);
        if (Utilities.IsAssignmentExpression(store, nodeInitializer, excludeCompoundAssignment: true))
        {
            var assignmentElement = VisitAssignmentElement(nodeInitializer);
            return factory.UpdatePropertyAssignment(node, name, assignmentElement);
        }

        if (UtilitiesPublic.IsLeftHandSideExpression(store, nodeInitializer))
        {
            var assignmentElement = VisitDestructuringAssignmentTarget(nodeInitializer);
            return factory.UpdatePropertyAssignment(node, name, assignmentElement);
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private Node VisitShorthandAssignmentProperty(Node node)
    {
        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node);
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private Node VisitAssignmentRestProperty(Node node)
    {
        if (UtilitiesPublic.IsLeftHandSideExpression(store, store.Expression(node)))
        {
            var expression = VisitDestructuringAssignmentTarget(store.Expression(node));
            return factory.UpdateSpreadAssignment(node, expression);
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitObjectAssignmentElement(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.SpreadAssignment:
                return VisitAssignmentRestProperty(node);
            case SyntaxKind.ShorthandPropertyAssignment:
                return VisitShorthandAssignmentProperty(node);
            case SyntaxKind.PropertyAssignment:
                return VisitAssignmentProperty(node);
            case SyntaxKind.BindingElement:
                return Visitors.VisitEachChild(node, visitor, context);
            default:
                throw new InvalidOperationException("Debug Failure. Unexpected node kind " + store.Kind(node) + " in an object assignment pattern.");
        }
    }

    private Node VisitAssignmentPattern(Node node)
    {
        if (store.Kind(node) == SyntaxKind.ArrayLiteralExpression)
        {
            // Transforms private names in destructuring assignment array bindings.
            //
            // Source:
            // ([ this.#myProp ] = [ "hello" ]);
            //
            // Transformation:
            // [ { set value(x) { this.#myProp = x; } }.value ] = [ "hello" ];
            return factory.UpdateArrayLiteralExpression(
                node,
                VisitList(store.Elements(node), VisitArrayAssignmentElement));
        }
        else
        {
            // Transforms private names in destructuring assignment object bindings.
            //
            // Source:
            // ({ stringProperty: this.#myProp } = { stringProperty: "hello" });
            //
            // Transformation:
            // ({ stringProperty: { set value(x) { this.#myProp = x; } }.value }) = { stringProperty: "hello" };
            return factory.UpdateObjectLiteralExpression(
                node,
                VisitList(store.Properties(node), VisitObjectAssignmentElement));
        }
    }

    private void OnEmitNode(EmitHint hint, Node node, Action<EmitHint, Node> emitCallback)
    {
        var original = TransformerUtilities.GetOriginalNode(store, node);
        if (!original.IsNone && lexicalEnvironmentMap.TryGetValue(original, out var lex))
        {
            // If we've associated a lexical environment with the original node for this node, use it explicitly.
            var savedLexicalEnvironment = lexicalEnvironment;
            var savedPreviousShouldSubstituteThisWithClassThis = previousShouldSubstituteThisWithClassThis;
            lexicalEnvironment = lex;
            previousShouldSubstituteThisWithClassThis = shouldSubstituteThisWithClassThis;
            shouldSubstituteThisWithClassThis = store.Kind(original) != SyntaxKind.ClassStaticBlockDeclaration || (FactoryEmitNode.GetInternalEmitFlags(store, original) & InternalEmitFlags.TransformPrivateStaticElements) == 0;
            previousOnEmitNode(hint, node, emitCallback);
            shouldSubstituteThisWithClassThis = previousShouldSubstituteThisWithClassThis;
            previousShouldSubstituteThisWithClassThis = savedPreviousShouldSubstituteThisWithClassThis;
            lexicalEnvironment = savedLexicalEnvironment;
            return;
        }

        switch (store.Kind(node))
        {
            case SyntaxKind.FunctionExpression:
                if (store.Kind(original) == SyntaxKind.ArrowFunction || (Utilities.GetEmitFlags(store, node) & EmitFlags.AsyncFunctionBody) != 0)
                {
                    // Arrow functions and functions that serve as the transformed body of an async function should
                    // preserve the outer lexical environment.
                    break;
                }
                goto case SyntaxKind.FunctionDeclaration;

            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.Constructor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.PropertyDeclaration:
            {
                // Other function bodies and property declarations should clear the lexical environment.
                // Note that this won't happen if a lexical environment was bound to the original node as that
                // was handled above.
                var savedLexicalEnvironment = lexicalEnvironment;
                var savedPreviousShouldSubstituteThisWithClassThis = previousShouldSubstituteThisWithClassThis;
                lexicalEnvironment = null;
                previousShouldSubstituteThisWithClassThis = shouldSubstituteThisWithClassThis;
                shouldSubstituteThisWithClassThis = false;
                previousOnEmitNode(hint, node, emitCallback);
                shouldSubstituteThisWithClassThis = previousShouldSubstituteThisWithClassThis;
                previousShouldSubstituteThisWithClassThis = savedPreviousShouldSubstituteThisWithClassThis;
                lexicalEnvironment = savedLexicalEnvironment;
                return;
            }

            case SyntaxKind.ComputedPropertyName:
            {
                // Computed property names should use the outer lexical environment.
                var savedLexicalEnvironment = lexicalEnvironment;
                var savedShouldSubstituteThisWithClassThis = shouldSubstituteThisWithClassThis;
                lexicalEnvironment = lexicalEnvironment?.Previous;
                shouldSubstituteThisWithClassThis = previousShouldSubstituteThisWithClassThis;
                previousOnEmitNode(hint, node, emitCallback);
                shouldSubstituteThisWithClassThis = savedShouldSubstituteThisWithClassThis;
                lexicalEnvironment = savedLexicalEnvironment;
                return;
            }
        }
        previousOnEmitNode(hint, node, emitCallback);
    }

    /// <summary>Hooks node substitutions.</summary>
    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        node = previousOnSubstituteNode(hint, node);

        if (hint == EmitHint.Expression)
        {
            return SubstituteExpression(node);
        }
        return node;
    }

    private Node SubstituteExpression(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.Identifier:
                return SubstituteExpressionIdentifier(node);
            case SyntaxKind.ThisKeyword:
                return SubstituteThisExpression(node);
        }
        return node;
    }

    private Node SubstituteThisExpression(Node node)
    {
        if ((enabledSubstitutions & ClassPropertySubstitutionFlags.ClassStaticThisOrSuperReference) != 0 &&
            lexicalEnvironment?.Data is not null &&
            !noSubstitution.Contains(node))
        {
            var data = lexicalEnvironment.Data;
            var substituteThis = shouldSubstituteThisWithClassThis ? (!data.ClassThis.IsNone ? data.ClassThis : data.ClassConstructor) : data.ClassConstructor;
            if (!substituteThis.IsNone)
            {
                return FactoryUtilitiesPublic.SetTextRange(
                    store,
                    NodeFactory.SetOriginalNode(
                        store,
                        factory.CloneNode(substituteThis),
                        node),
                    node);
            }
        }
        return node;
    }

    private Node SubstituteExpressionIdentifier(Node node)
    {
        var alias = TrySubstituteClassAlias(node);
        return alias.IsNone ? node : alias;
    }

    private Node TrySubstituteClassAlias(Node node)
    {
        if ((enabledSubstitutions & ClassPropertySubstitutionFlags.ClassAliases) != 0)
        {
            if (resolver.HasNodeCheckFlag(node, NodeCheckFlags.ConstructorReference))
            {
                // Due to the emit for class decorators, any reference to the class from inside of the class body
                // must instead be rewritten to point to a temporary variable to avoid issues with the double-bind
                // behavior of class names in ES6.
                // Also, when emitting statics for class expressions, we must substitute a class alias for
                // constructor references in static property initializers.
                var declaration = resolver.NodeInThisFile(resolver.GetReferencedValueDeclaration(node));
                if (!declaration.IsNone)
                {
                    if (classAliases!.TryGetValue(declaration, out var classAlias))
                    {
                        var clone = factory.CloneNode(classAlias);
                        FactoryEmitNode.SetSourceMapRange(store, clone, TransformerUtilities.RangeOf(store, node));
                        FactoryEmitNode.SetCommentRange(store, clone, node);
                        return clone;
                    }
                }
            }
        }

        return Node.None;
    }

    private Node CreatePrivateStaticFieldInitializer(Node variableName, Node initializer)
    {
        return factory.CreateAssignment(
            variableName,
            factory.CreateObjectLiteralExpression(factory.CreateNodeArray(new[]
            {
                factory.CreatePropertyAssignment(factory.CreateIdentifier("value"), initializer.IsNone ? factory.CreateVoidZero() : initializer),
            })));
    }

    private Node CreatePrivateInstanceFieldInitializer(Node receiver, Node initializer, Node weakMapName)
    {
        return factory.CreateCallExpression(
            factory.CreatePropertyAccessExpression(weakMapName, factory.CreateIdentifier("set")),
            default,
            factory.CreateNodeArray(new[] { receiver, initializer.IsNone ? factory.CreateVoidZero() : initializer }));
    }

    private bool IsReservedPrivateName(Node node)
    {
        return !TransformerUtilities.IsGeneratedPrivateIdentifier(store, node) && store.Texts.Equals(store.EscapedText(node), "#constructor");
    }
}
