// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/transformers/esDecorators.ts (transformESDecorators)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/transformers/estransforms/esdecorator.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#; module D4. Standard (ES) decorators for target ES2025: class and member
// decorators with __esDecorate / __runInitializers, the class `static {}` blocks and `_classThis`, private names in
// decorated classes, auto-accessors, decorated class expressions, `super` in static initializers, named evaluation of
// decorated anonymous classes. Parameter decorators (not valid ES decorators) are dropped by visitParameterDeclaration.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>esDecorators.ts transformESDecorators: the closure of the transformer is the instance.</summary>
internal sealed class ESDecoratorsTransformer
{
    // Class/Decorator evaluation order, as it pertains to this transformer:
    //
    // 1. Class decorators are evaluated outside of the private name scope of the class.
    // 2. ClassHeritage clause is evaluated outside of the private name scope of the class.
    // 3. The name of the class is assigned.
    // 4. For each member:
    //    a. Member Decorators are evaluated.
    //    b. Computed Property name is evaluated
    // 5. Static non-field (method/getter/setter/auto-accessor) element decorators are applied
    // 6. Non-static non-field (method/getter/setter/auto-accessor) element decorators are applied
    // 7. Static field (excl. auto-accessor) element decorators are applied
    // 8. Non-static field (excl. auto-accessor) element decorators are applied
    // 9. Class decorators are applied
    // 10. Class binding is initialized
    // 11. Static method extra initializers are evaluated
    // 12. Static fields are initialized (incl. extra initializers) and static blocks are evaluated
    // 13. Class extra initializers are evaluated
    //
    // Class constructor evaluation order, as it pertains to this transformer:
    //
    // 1. Instance method extra initializers are evaluated
    // 2. For each instance field/auto-accessor:
    //    a. The field is initialized and defined on the instance.
    //    b. Extra initializers for the field are evaluated.

    private sealed class MemberInfo
    {
        internal Node MemberDecoratorsName; // used in class definition step 4.a
        internal Node MemberInitializersName; // used in class definition step 12 and constructor evaluation step 2.a
        internal Node MemberExtraInitializersName; // used in class definition step 12 and constructor evaluation step 2.b
        internal Node MemberDescriptorName;
    }

    private sealed class ClassInfo
    {
        internal Node Class;

        internal Node ClassDecoratorsName; // used in class definition step 2
        internal Node ClassDescriptorName; // used in class definition step 10
        internal Node ClassExtraInitializersName; // used in class definition step 13
        internal Node ClassThis; // `_classThis`, if needed.
        internal Node ClassSuper; // `_classSuper`, if needed.
        internal Node MetadataReference;

        // used in class definition step 4.a, 12, and constructor evaluation (a Map: insertion order)
        internal List<(Node Member, MemberInfo Info)>? MemberInfos;

        internal Node InstanceMethodExtraInitializersName; // used in constructor evaluation step 1
        internal Node StaticMethodExtraInitializersName; // used in class definition step 11

        internal List<Node>? StaticNonFieldDecorationStatements;
        internal List<Node>? NonStaticNonFieldDecorationStatements;
        internal List<Node>? StaticFieldDecorationStatements;
        internal List<Node>? NonStaticFieldDecorationStatements;

        internal bool HasStaticInitializers;
        internal bool HasNonAmbientInstanceFields;
        internal bool HasStaticPrivateClassElements;

        internal List<Node>? PendingStaticInitializers;
        internal List<Node>? PendingInstanceInitializers;
    }

    private enum EntryKind
    {
        Class,
        ClassElement,
        Name,
        Other,
    }

    // LexicalEnvironmentStackEntry (the four kinds in one class)
    private sealed class LexicalEntry
    {
        internal EntryKind Kind;
        internal LexicalEntry? Next;
        internal ClassInfo? ClassInfo; // "class"
        internal List<Node>? SavedPendingExpressions; // "class", "other"
        internal Node ClassThis; // "class-element"
        internal Node ClassSuper; // "class-element"
        internal int Depth; // "other"
    }

    // The result of partialTransformClassElement.
    private struct PartialClassElement
    {
        internal NodeList Modifiers;
        internal Node ReferencedName;
        internal Node Name;
        internal Node InitializersName;
        internal Node ExtraInitializersName;
        internal Node DescriptorName;
        internal Node ThisArg;
    }

    private readonly TransformationContext context;
    private readonly NodeFactory factory;
    private readonly ScriptTarget languageVersion;
    private readonly Func<Node, VisitResult> visitor;
    private readonly Func<Node, VisitResult> fallbackVisitor;
    private readonly Func<Node, VisitResult> modifierVisitor;
    private readonly Func<Node, VisitResult> classElementVisitor;
    private readonly Func<Node, VisitResult> discardedValueVisitor;
    private readonly Action<Node> hoistVariableDeclaration;

    private LexicalEntry? top;
    private ClassInfo? classInfo;
    private Node classThis;
    private Node classSuper;
    private List<Node>? pendingExpressions;
    private bool shouldTransformPrivateStaticElementsInFile;

    private ESDecoratorsTransformer(TransformationContext context)
    {
        this.context = context;
        factory = context.Factory;
        languageVersion = context.GetCompilerOptions().GetEmitScriptTarget();
        visitor = Visitor;
        fallbackVisitor = FallbackVisitor;
        modifierVisitor = ModifierVisitor;
        classElementVisitor = ClassElementVisitor;
        discardedValueVisitor = DiscardedValueVisitor;
        hoistVariableDeclaration = context.HoistVariableDeclaration;
        classThis = Node.None;
        classSuper = Node.None;
    }

    private NodeStore store => context.Store;

    /// <summary>transformESDecorators(context).</summary>
    internal static Func<Node, Node> TransformESDecorators(TransformationContext context) => new ESDecoratorsTransformer(context).TransformSourceFile;

    private Node TransformSourceFile(Node node)
    {
        top = null;
        shouldTransformPrivateStaticElementsInFile = false;
        var visited = Visitors.VisitEachChild(node, visitor, context);
        FactoryEmitNode.AddEmitHelpers(store, visited, context.ReadEmitHelpers());
        if (shouldTransformPrivateStaticElementsInFile)
        {
            FactoryEmitNode.AddInternalEmitFlags(store, visited, InternalEmitFlags.TransformPrivateStaticElements);
            shouldTransformPrivateStaticElementsInFile = false;
        }
        return visited;
    }

    private void UpdateState()
    {
        classInfo = null;
        classThis = Node.None;
        classSuper = Node.None;
        switch (top?.Kind)
        {
            case EntryKind.Class:
                classInfo = top.ClassInfo;
                break;
            case EntryKind.ClassElement:
                classInfo = top.Next!.ClassInfo;
                classThis = top.ClassThis;
                classSuper = top.ClassSuper;
                break;
            case EntryKind.Name:
                var grandparent = top.Next!.Next!.Next;
                if (grandparent?.Kind == EntryKind.ClassElement)
                {
                    classInfo = grandparent.Next!.ClassInfo;
                    classThis = grandparent.ClassThis;
                    classSuper = grandparent.ClassSuper;
                }
                break;
        }
    }

    private void EnterClass(ClassInfo? info)
    {
        top = new LexicalEntry { Kind = EntryKind.Class, Next = top, ClassInfo = info, SavedPendingExpressions = pendingExpressions };
        pendingExpressions = null;
        UpdateState();
    }

    private void ExitClass()
    {
        Debug.Assert(top?.Kind == EntryKind.Class, "Incorrect value for top.kind.");
        pendingExpressions = top!.SavedPendingExpressions;
        top = top.Next;
        UpdateState();
    }

    private void EnterClassElement(Node node)
    {
        Debug.Assert(top?.Kind == EntryKind.Class, "Incorrect value for top.kind.");
        top = new LexicalEntry { Kind = EntryKind.ClassElement, Next = top };
        if (store.Kind(node) == SyntaxKind.ClassStaticBlockDeclaration || store.Kind(node) == SyntaxKind.PropertyDeclaration && TransformerUtilities.HasStaticModifier(store, node))
        {
            top.ClassThis = top.Next!.ClassInfo?.ClassThis ?? Node.None;
            top.ClassSuper = top.Next!.ClassInfo?.ClassSuper ?? Node.None;
        }
        UpdateState();
    }

    private void ExitClassElement()
    {
        Debug.Assert(top?.Kind == EntryKind.ClassElement, "Incorrect value for top.kind.");
        Debug.Assert(top!.Next?.Kind == EntryKind.Class, "Incorrect value for top.next.kind.");
        top = top.Next;
        UpdateState();
    }

    private void EnterName()
    {
        Debug.Assert(top?.Kind == EntryKind.ClassElement, "Incorrect value for top.kind.");
        top = new LexicalEntry { Kind = EntryKind.Name, Next = top };
        UpdateState();
    }

    private void ExitName()
    {
        Debug.Assert(top?.Kind == EntryKind.Name, "Incorrect value for top.kind.");
        top = top!.Next;
        UpdateState();
    }

    private void EnterOther()
    {
        if (top?.Kind == EntryKind.Other)
        {
            Debug.Assert(pendingExpressions is null);
            top.Depth++;
        }
        else
        {
            top = new LexicalEntry { Kind = EntryKind.Other, Next = top, Depth = 0, SavedPendingExpressions = pendingExpressions };
            pendingExpressions = null;
            UpdateState();
        }
    }

    private void ExitOther()
    {
        Debug.Assert(top?.Kind == EntryKind.Other, "Incorrect value for top.kind.");
        if (top!.Depth > 0)
        {
            Debug.Assert(pendingExpressions is null);
            top.Depth--;
        }
        else
        {
            pendingExpressions = top.SavedPendingExpressions;
            top = top.Next;
            UpdateState();
        }
    }

    private bool ShouldVisitNode(Node node)
    {
        var flags = store.TransformFlags(node);
        return (flags & TransformFlags.ContainsDecorators) != 0
            || !classThis.IsNone && (flags & TransformFlags.ContainsLexicalThis) != 0
            || !classThis.IsNone && !classSuper.IsNone && (flags & TransformFlags.ContainsLexicalSuper) != 0;
    }

    private VisitResult Visitor(Node node)
    {
        if (!ShouldVisitNode(node))
        {
            return node;
        }

        switch (store.Kind(node))
        {
            case SyntaxKind.Decorator: // elided, will be emitted as part of `visitClassDeclaration`
                throw new InvalidOperationException("Debug Failure. Use `modifierVisitor` instead.");
            case SyntaxKind.ClassDeclaration:
                return VisitClassDeclaration(node);
            case SyntaxKind.ClassExpression:
                return VisitClassExpression(node);
            case SyntaxKind.Constructor:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.ClassStaticBlockDeclaration:
                throw new InvalidOperationException("Debug Failure. Not supported outside of a class. Use 'classElementVisitor' instead.");
            case SyntaxKind.Parameter:
                return VisitParameterDeclaration(node);

            // Support NamedEvaluation to ensure the correct class name for class expressions.
            case SyntaxKind.BinaryExpression:
                return VisitBinaryExpression(node, discarded: false);
            case SyntaxKind.PropertyAssignment:
                return VisitPropertyAssignment(node);
            case SyntaxKind.VariableDeclaration:
                return VisitVariableDeclaration(node);
            case SyntaxKind.BindingElement:
                return VisitBindingElement(node);
            case SyntaxKind.ExportAssignment:
                return VisitExportAssignment(node);
            case SyntaxKind.ThisKeyword:
                return VisitThisExpression(node);
            case SyntaxKind.ForStatement:
                return VisitForStatement(node);
            case SyntaxKind.ExpressionStatement:
                return VisitExpressionStatement(node);
            case SyntaxKind.CommaListExpression:
                return VisitCommaListExpression(node, discarded: false);
            case SyntaxKind.ParenthesizedExpression:
                return VisitParenthesizedExpression(node, discarded: false);
            case SyntaxKind.PartiallyEmittedExpression:
                return VisitPartiallyEmittedExpression(node, discarded: false);
            case SyntaxKind.CallExpression:
                return VisitCallExpression(node);
            case SyntaxKind.TaggedTemplateExpression:
                return VisitTaggedTemplateExpression(node);
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
                return VisitPreOrPostfixUnaryExpression(node, discarded: false);
            case SyntaxKind.PropertyAccessExpression:
                return VisitPropertyAccessExpression(node);
            case SyntaxKind.ElementAccessExpression:
                return VisitElementAccessExpression(node);
            case SyntaxKind.ComputedPropertyName:
                return VisitComputedPropertyName(node);

            case SyntaxKind.MethodDeclaration: // object literal methods and accessors
            case SyntaxKind.SetAccessor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionDeclaration:
            {
                EnterOther();
                var result = Visitors.VisitEachChild(node, fallbackVisitor, context);
                ExitOther();
                return result;
            }

            default:
                return Visitors.VisitEachChild(node, fallbackVisitor, context);
        }
    }

    private VisitResult FallbackVisitor(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.Decorator:
                return VisitResult.None;
            default:
                return Visitor(node);
        }
    }

    private VisitResult ModifierVisitor(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.Decorator: // elided, will be emitted as part of `visitClassDeclaration`
                return VisitResult.None;
            default:
                return node;
        }
    }

    private VisitResult ClassElementVisitor(Node node)
    {
        switch (store.Kind(node))
        {
            case SyntaxKind.Constructor:
                return VisitConstructorDeclaration(node);
            case SyntaxKind.MethodDeclaration:
                return VisitMethodDeclaration(node);
            case SyntaxKind.GetAccessor:
                return VisitGetAccessorDeclaration(node);
            case SyntaxKind.SetAccessor:
                return VisitSetAccessorDeclaration(node);
            case SyntaxKind.PropertyDeclaration:
                return VisitPropertyDeclaration(node);
            case SyntaxKind.ClassStaticBlockDeclaration:
                return VisitClassStaticBlockDeclaration(node);
            default:
                return Visitor(node);
        }
    }

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

    private Node Visit(Node node, Func<Node, VisitResult> v) => Visitors.VisitNode(factory, node, v);

    private NodeList VisitList(NodeList nodes, Func<Node, VisitResult> v) => Visitors.VisitNodes(factory, nodes, v);

    private Node This() => factory.CreateToken(SyntaxKind.ThisKeyword);

    private Node Null() => factory.CreateToken(SyntaxKind.NullKeyword);

    private Node Ident(string text) => factory.CreateIdentifier(text);

    private NodeList Array(params Node[] nodes) => factory.CreateNodeArray(nodes);

    private string GetHelperVariableName(Node node)
    {
        var name = store.Name(node);
        string declarationName =
            !name.IsNone && store.Kind(name) == SyntaxKind.Identifier && !UtilitiesPublic.IsGeneratedIdentifier(store, name) ? PrinterUtilities.IdText(store, name) :
            !name.IsNone && store.Kind(name) == SyntaxKind.PrivateIdentifier && !UtilitiesPublic.IsGeneratedIdentifier(store, name) ? PrinterUtilities.IdText(store, name)[1..] :
            !name.IsNone && store.Kind(name) == SyntaxKind.StringLiteral && Scanner.IsIdentifierText(PrinterUtilities.TextOf(store, name), ScriptTarget.ESNext) ? PrinterUtilities.TextOf(store, name) :
            Utilities.IsClassLike(store, node) ? "class" : "member";
        if (store.Kind(node) == SyntaxKind.GetAccessor)
        {
            declarationName = "get_" + declarationName;
        }
        if (store.Kind(node) == SyntaxKind.SetAccessor)
        {
            declarationName = "set_" + declarationName;
        }
        if (!name.IsNone && store.Kind(name) == SyntaxKind.PrivateIdentifier)
        {
            declarationName = "private_" + declarationName;
        }
        if (Utilities.IsStatic(store, node))
        {
            declarationName = "static_" + declarationName;
        }
        return "_" + declarationName;
    }

    private Node CreateHelperVariable(Node node, string suffix)
    {
        return factory.CreateUniqueName(GetHelperVariableName(node) + "_" + suffix, GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.ReservedInNestedScopes);
    }

    private Node CreateLet(Node name, Node initializer = default)
    {
        return factory.CreateVariableStatement(
            default,
            factory.CreateVariableDeclarationList(Array(
                factory.CreateVariableDeclaration(
                    name,
                    default,
                    default,
                    initializer)), NodeFlags.Let));
    }

    private Node FileLevelName(string text) =>
        factory.CreateUniqueName(text, GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);

    private ObjectTextRange NameOrPastDecorators(Node node) =>
        !store.Name(node).IsNone ? TransformerUtilities.RangeOf(store, store.Name(node)) : TransformerUtilities.MoveRangePastDecorators(store, node);

    private ClassInfo CreateClassInfo(Node node)
    {
        var metadataReference = FileLevelName("_metadata");
        var instanceMethodExtraInitializersName = Node.None;
        var staticMethodExtraInitializersName = Node.None;
        var hasStaticInitializers = false;
        var hasNonAmbientInstanceFields = false;
        var hasStaticPrivateClassElements = false;
        var classThisName = Node.None;
        List<Node>? pendingStaticInitializers = null;
        List<Node>? pendingInstanceInitializers = null;

        // Before visiting we perform a first pass to collect information we'll need
        // as we descend.

        if (TransformerUtilities.NodeIsDecorated(store, useLegacyDecorators: false, node))
        {
            // We do not mark _classThis as FileLevel if it may be reused by class private fields, which requires the
            // ability access the captured `_classThis` of outer scopes.
            var needsUniqueClassThis = false;
            foreach (var member in store.Nodes(store.Members(node)))
            {
                if ((TransformerUtilities.IsPrivateIdentifierClassElementDeclaration(store, member) || TransformerUtilities.IsAutoAccessorPropertyDeclaration(store, member)) && TransformerUtilities.HasStaticModifier(store, member))
                {
                    needsUniqueClassThis = true;
                    break;
                }
            }
            classThisName = factory.CreateUniqueName(
                "_classThis",
                needsUniqueClassThis ?
                    GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.ReservedInNestedScopes :
                    GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);
        }

        foreach (var member in store.Nodes(store.Members(node)))
        {
            if (TransformerUtilities.IsMethodOrAccessor(store, member) && TransformerUtilities.NodeOrChildIsDecorated(store, useLegacyDecorators: false, member, node))
            {
                if (TransformerUtilities.HasStaticModifier(store, member))
                {
                    if (staticMethodExtraInitializersName.IsNone)
                    {
                        staticMethodExtraInitializersName = FileLevelName("_staticExtraInitializers");
                        var initializer = context.GetEmitHelperFactory().CreateRunInitializersHelper(classThisName.IsNone ? This() : classThisName, staticMethodExtraInitializersName);
                        FactoryEmitNode.SetSourceMapRange(store, initializer, NameOrPastDecorators(node));
                        (pendingStaticInitializers ??= []).Add(initializer);
                    }
                }
                else
                {
                    if (instanceMethodExtraInitializersName.IsNone)
                    {
                        instanceMethodExtraInitializersName = FileLevelName("_instanceExtraInitializers");
                        var initializer = context.GetEmitHelperFactory().CreateRunInitializersHelper(This(), instanceMethodExtraInitializersName);
                        FactoryEmitNode.SetSourceMapRange(store, initializer, NameOrPastDecorators(node));
                        (pendingInstanceInitializers ??= []).Add(initializer);
                    }
                }
            }
            if (store.Kind(member) == SyntaxKind.ClassStaticBlockDeclaration)
            {
                if (!NamedEvaluation.IsClassNamedEvaluationHelperBlock(store, member))
                {
                    hasStaticInitializers = true;
                }
            }
            else if (store.Kind(member) == SyntaxKind.PropertyDeclaration)
            {
                if (TransformerUtilities.HasStaticModifier(store, member))
                {
                    hasStaticInitializers = hasStaticInitializers || !store.Initializer(member).IsNone || TransformerUtilities.HasDecorators(store, member);
                }
                else
                {
                    hasNonAmbientInstanceFields = hasNonAmbientInstanceFields || !TransformerUtilities.IsAmbientPropertyDeclaration(store, member);
                }
            }

            if ((TransformerUtilities.IsPrivateIdentifierClassElementDeclaration(store, member) || TransformerUtilities.IsAutoAccessorPropertyDeclaration(store, member)) && TransformerUtilities.HasStaticModifier(store, member))
            {
                hasStaticPrivateClassElements = true;
            }

            // exit early if possible
            if (
                !staticMethodExtraInitializersName.IsNone &&
                !instanceMethodExtraInitializersName.IsNone &&
                hasStaticInitializers &&
                hasNonAmbientInstanceFields &&
                hasStaticPrivateClassElements)
            {
                break;
            }
        }

        return new ClassInfo
        {
            Class = node,
            ClassThis = classThisName,
            MetadataReference = metadataReference,
            InstanceMethodExtraInitializersName = instanceMethodExtraInitializersName,
            StaticMethodExtraInitializersName = staticMethodExtraInitializersName,
            HasStaticInitializers = hasStaticInitializers,
            HasNonAmbientInstanceFields = hasNonAmbientInstanceFields,
            HasStaticPrivateClassElements = hasStaticPrivateClassElements,
            PendingStaticInitializers = pendingStaticInitializers,
            PendingInstanceInitializers = pendingInstanceInitializers,
        };
    }

    private Node TransformClassLike(Node node)
    {
        context.StartLexicalEnvironment();

        // When a class has class decorators we end up transforming it into a statement that would otherwise give it an
        // assigned name. If the class doesn't have an assigned name, we'll give it an assigned name of `""`)
        if (!NamedEvaluation.ClassHasDeclaredOrExplicitlyAssignedName(store, node) && TransformerUtilities.ClassOrConstructorParameterIsDecorated(store, useLegacyDecorators: false, node))
        {
            node = NamedEvaluation.InjectClassNamedEvaluationHelperBlockIfMissing(context, node, factory.CreateStringLiteral(store.Texts.Add("")));
        }

        var classReference = factory.GetLocalName(node, allowComments: false, allowSourceMaps: false, ignoreAssignedName: true);
        var info = CreateClassInfo(node);
        var classDefinitionStatements = new List<Node>();
        // (leadingBlockStatements: TypeScript's `Statement[] | undefined` is always set by `append` below.)
        List<Node>? trailingBlockStatements = null;
        var syntheticConstructor = Node.None;
        var heritageClauses = NodeList.None;
        var shouldTransformPrivateStaticElementsInClass = false;

        // 1. Class decorators are evaluated outside of the private name scope of the class.
        var classDecorators = TransformAllDecoratorsOfDeclaration(TransformerUtilities.GetAllDecoratorsOfClass(store, node));
        if (classDecorators is not null)
        {
            // - Since class decorators don't have privileged access to private names defined inside the class,
            //   they must be evaluated outside of the class body.
            // - Since a class decorator can replace the class constructor, we must define a variable to keep track
            //   of the mutated class.
            // - Since a class decorator can add extra initializers, we must define a variable to keep track of
            //   extra initializers.
            info.ClassDecoratorsName = FileLevelName("_classDecorators");
            info.ClassDescriptorName = FileLevelName("_classDescriptor");
            info.ClassExtraInitializersName = FileLevelName("_classExtraInitializers");
            Debug.Assert(!info.ClassThis.IsNone);
            classDefinitionStatements.Add(CreateLet(info.ClassDecoratorsName, factory.CreateArrayLiteralExpression(factory.CreateNodeArray(classDecorators))));
            classDefinitionStatements.Add(CreateLet(info.ClassDescriptorName));
            classDefinitionStatements.Add(CreateLet(info.ClassExtraInitializersName, factory.CreateArrayLiteralExpression()));
            classDefinitionStatements.Add(CreateLet(info.ClassThis));

            if (info.HasStaticPrivateClassElements)
            {
                shouldTransformPrivateStaticElementsInClass = true;
                shouldTransformPrivateStaticElementsInFile = true;
            }
        }

        // Rewrite `super` in static initializers so that we can use the correct `this`.
        var extendsClause = TransformerUtilities.GetHeritageClause(store, store.HeritageClauses(node), SyntaxKind.ExtendsKeyword);
        var extendsElement = extendsClause.IsNone || store.Length(store.Types(extendsClause)) == 0 ? Node.None : store.Element(store.Types(extendsClause), 0);
        var extendsExpression = extendsElement.IsNone ? Node.None : Visit(store.Expression(extendsElement), visitor);
        if (!extendsExpression.IsNone)
        {
            info.ClassSuper = FileLevelName("_classSuper");

            // Ensure we do not give the class or function an assigned name due to the variable by prefixing it
            // with `0, `.
            var unwrapped = FactoryUtilities.SkipOuterExpressions(store, extendsExpression);
            var safeExtendsExpression =
                store.Kind(unwrapped) == SyntaxKind.ClassExpression && store.Name(unwrapped).IsNone ||
                store.Kind(unwrapped) == SyntaxKind.FunctionExpression && store.Name(unwrapped).IsNone ||
                store.Kind(unwrapped) == SyntaxKind.ArrowFunction ?
                    factory.CreateComma(factory.CreateNumericLiteral(store.Texts.Add("0")), extendsExpression) :
                    extendsExpression;
            classDefinitionStatements.Add(CreateLet(info.ClassSuper, safeExtendsExpression));
            var updatedExtendsElement = factory.UpdateExpressionWithTypeArguments(extendsElement, info.ClassSuper, default);
            var updatedExtendsClause = factory.UpdateHeritageClause(extendsClause, Array(updatedExtendsElement));
            heritageClauses = Array(updatedExtendsClause);
        }

        var renamedClassThis = info.ClassThis.IsNone ? This() : info.ClassThis;

        // 3. The name of the class is assigned.
        //
        // If the class did not have a name, the caller should have performed injectClassNamedEvaluationHelperBlockIfMissing
        // prior to calling this function if a name was needed.

        // 4. For each member:
        //    a. Member Decorators are evaluated
        //    b. Computed Property Name is evaluated, if present

        // We visit members in two passes:
        // - The first pass visits methods, accessors, and fields to collect decorators and computed property names.
        // - The second pass visits the constructor to add instance initializers.
        //
        // NOTE: If there are no constructors, but there are instance initializers, a synthetic constructor is added.
        EnterClass(info);

        var leadingBlockStatements = new List<Node> { CreateMetadata(info.MetadataReference, info.ClassSuper) };

        // Since the constructor can appear anywhere in the class body and its transform depends on other class elements,
        // we must first visit all non-constructor members, then visit the constructor, all while maintaining document order.
        var members = store.Members(node);
        members = VisitList(members, n => store.Kind(n) == SyntaxKind.Constructor ? n : ClassElementVisitor(n));
        members = VisitList(members, n => store.Kind(n) == SyntaxKind.Constructor ? ClassElementVisitor(n) : n);
        if (pendingExpressions is not null)
        {
            var outerThis = Node.None;
            VisitResult ThisVisitor(Node n)
            {
                if ((store.TransformFlags(n) & TransformFlags.ContainsLexicalThis) == 0)
                {
                    return n;
                }

                switch (store.Kind(n))
                {
                    case SyntaxKind.ThisKeyword:
                        if (outerThis.IsNone)
                        {
                            outerThis = factory.CreateUniqueName("_outerThis", GeneratedIdentifierFlags.Optimistic);
                            classDefinitionStatements.Insert(0, CreateLet(outerThis, This()));
                        }
                        return outerThis;

                    default:
                        return Visitors.VisitEachChild(n, ThisVisitor, context);
                }
            }

            foreach (var pending in pendingExpressions)
            {
                // If a pending expression contains a lexical `this`, we'll need to capture the lexical `this` of the
                // container and transform it in the expression. This ensures we use the correct `this` in the resulting
                // class `static` block. We don't use substitution here because the size of the tree we are visiting
                // is likely to be small and doesn't justify the complexity of introducing substitution.
                var expression = Visit(pending, ThisVisitor);

                var statement = factory.CreateExpressionStatement(expression);
                leadingBlockStatements.Add(statement);
            }
            pendingExpressions = null;
        }
        ExitClass();

        if (Core.Some(info.PendingInstanceInitializers) && TransformerUtilities.GetFirstConstructorWithBody(store, node).IsNone)
        {
            var initializerStatements = PrepareConstructor(node, info);
            if (initializerStatements is not null)
            {
                var extendsClauseElement = TransformerUtilities.GetEffectiveBaseTypeNode(store, node);
                var isDerivedClass = !extendsClauseElement.IsNone && store.Kind(FactoryUtilities.SkipOuterExpressions(store, store.Expression(extendsClauseElement))) != SyntaxKind.NullKeyword;
                var constructorStatements = new List<Node>();
                if (isDerivedClass)
                {
                    var spreadArguments = factory.CreateSpreadElement(Ident("arguments"));
                    var superCall = factory.CreateCallExpression(factory.CreateToken(SyntaxKind.SuperKeyword), default, Array(spreadArguments));
                    constructorStatements.Add(factory.CreateExpressionStatement(superCall));
                }

                constructorStatements.AddRange(initializerStatements);

                var constructorBody = factory.CreateBlock(factory.CreateNodeArray(constructorStatements), multiLine: true);
                syntheticConstructor = factory.CreateConstructorDeclaration(default, factory.CreateNodeArray(ReadOnlySpan<Node>.Empty), constructorBody);
            }
        }

        // Used in class definition steps 5, 7, and 11
        if (!info.StaticMethodExtraInitializersName.IsNone)
        {
            classDefinitionStatements.Add(CreateLet(info.StaticMethodExtraInitializersName, factory.CreateArrayLiteralExpression()));
        }

        // Used in class definition steps 6, 8, and during construction
        if (!info.InstanceMethodExtraInitializersName.IsNone)
        {
            classDefinitionStatements.Add(CreateLet(info.InstanceMethodExtraInitializersName, factory.CreateArrayLiteralExpression()));
        }

        // Used in class definition steps 7, 8, 12, and construction
        if (info.MemberInfos is not null)
        {
            foreach (var isStaticPass in new[] { true, false })
            {
                foreach (var (member, memberInfo) in info.MemberInfos)
                {
                    if (Utilities.IsStatic(store, member) != isStaticPass)
                    {
                        continue;
                    }
                    classDefinitionStatements.Add(CreateLet(memberInfo.MemberDecoratorsName));
                    if (!memberInfo.MemberInitializersName.IsNone)
                    {
                        classDefinitionStatements.Add(CreateLet(memberInfo.MemberInitializersName, factory.CreateArrayLiteralExpression()));
                    }
                    if (!memberInfo.MemberExtraInitializersName.IsNone)
                    {
                        classDefinitionStatements.Add(CreateLet(memberInfo.MemberExtraInitializersName, factory.CreateArrayLiteralExpression()));
                    }
                    if (!memberInfo.MemberDescriptorName.IsNone)
                    {
                        classDefinitionStatements.Add(CreateLet(memberInfo.MemberDescriptorName));
                    }
                }
            }
        }

        // 5. Static non-field element decorators are applied
        AddRange(leadingBlockStatements, info.StaticNonFieldDecorationStatements);

        // 6. Non-static non-field element decorators are applied
        AddRange(leadingBlockStatements, info.NonStaticNonFieldDecorationStatements);

        // 7. Static field element decorators are applied
        AddRange(leadingBlockStatements, info.StaticFieldDecorationStatements);

        // 8. Non-static field element decorators are applied
        AddRange(leadingBlockStatements, info.NonStaticFieldDecorationStatements);

        // 9. Class decorators are applied
        // 10. Class binding is initialized
        if (!info.ClassDescriptorName.IsNone && !info.ClassDecoratorsName.IsNone && !info.ClassExtraInitializersName.IsNone && !info.ClassThis.IsNone)
        {
            // produces:
            //  __esDecorate(null, _classDescriptor = { value: this }, _classDecorators, { kind: "class", name: this.name, metadata }, _classExtraInitializers);
            var valueProperty = factory.CreatePropertyAssignment(Ident("value"), renamedClassThis);
            var classDescriptor = factory.CreateObjectLiteralExpression(Array(valueProperty));
            var classDescriptorAssignment = factory.CreateAssignment(info.ClassDescriptorName, classDescriptor);
            var classNameReference = factory.CreatePropertyAccessExpression(renamedClassThis, Ident("name"));
            var esDecorateHelper = context.GetEmitHelperFactory().CreateESDecorateHelper(
                Null(),
                classDescriptorAssignment,
                info.ClassDecoratorsName,
                new ESDecorateContext { Kind = "class", ClassName = classNameReference, Metadata = info.MetadataReference },
                Null(),
                info.ClassExtraInitializersName);
            var esDecorateStatement = factory.CreateExpressionStatement(esDecorateHelper);
            FactoryEmitNode.SetSourceMapRange(store, esDecorateStatement, TransformerUtilities.MoveRangePastDecorators(store, node));
            leadingBlockStatements.Add(esDecorateStatement);

            // produces:
            //  C = _classThis = _classDescriptor.value;
            var classDescriptorValueReference = factory.CreatePropertyAccessExpression(info.ClassDescriptorName, Ident("value"));
            var classThisAssignment = factory.CreateAssignment(info.ClassThis, classDescriptorValueReference);
            var classReferenceAssignment = factory.CreateAssignment(classReference, classThisAssignment);
            leadingBlockStatements.Add(factory.CreateExpressionStatement(classReferenceAssignment));
        }

        // produces:
        //  if (metadata) Object.defineProperty(C, Symbol.metadata, { configurable: true, writable: true, value: metadata });
        leadingBlockStatements.Add(CreateSymbolMetadata(renamedClassThis, info.MetadataReference));

        // 11. Static extra initializers are evaluated
        // 12. Static fields are initialized (incl. extra initializers) and static blocks are evaluated
        if (Core.Some(info.PendingStaticInitializers))
        {
            foreach (var initializer in info.PendingStaticInitializers)
            {
                var initializerStatement = factory.CreateExpressionStatement(initializer);
                FactoryEmitNode.SetSourceMapRange(store, initializerStatement, FactoryEmitNode.GetSourceMapRange(store, initializer));
                (trailingBlockStatements ??= []).Add(initializerStatement);
            }
            info.PendingStaticInitializers = null;
        }

        // 13. Class extra initializers are evaluated
        if (!info.ClassExtraInitializersName.IsNone)
        {
            var runClassInitializersHelper = context.GetEmitHelperFactory().CreateRunInitializersHelper(renamedClassThis, info.ClassExtraInitializersName);
            var runClassInitializersStatement = factory.CreateExpressionStatement(runClassInitializersHelper);
            FactoryEmitNode.SetSourceMapRange(store, runClassInitializersStatement, NameOrPastDecorators(node));
            (trailingBlockStatements ??= []).Add(runClassInitializersStatement);
        }

        // If there are no other static initializers to run, combine the leading and trailing block statements
        if (trailingBlockStatements is not null && !info.HasStaticInitializers)
        {
            leadingBlockStatements.AddRange(trailingBlockStatements);
            trailingBlockStatements = null;
        }

        // prepare a leading `static {}` block, if necessary
        //
        // produces:
        //  class C {
        //      static { ... }
        //      ...
        //  }
        var leadingStaticBlock =
            factory.CreateClassStaticBlockDeclaration(factory.CreateBlock(factory.CreateNodeArray(leadingBlockStatements), multiLine: true));

        if (!leadingStaticBlock.IsNone && shouldTransformPrivateStaticElementsInClass)
        {
            // We use `InternalEmitFlags.TransformPrivateStaticElements` as a marker on a class static block
            // to inform the classFields transform that it shouldn't rename `this` to `_classThis` in the
            // transformed class static block.
            FactoryEmitNode.SetInternalEmitFlags(store, leadingStaticBlock, InternalEmitFlags.TransformPrivateStaticElements);
        }

        // prepare a trailing `static {}` block, if necessary
        //
        // produces:
        //  class C {
        //      ...
        //      static { ... }
        //  }
        var trailingStaticBlock = trailingBlockStatements is null ? Node.None :
            factory.CreateClassStaticBlockDeclaration(factory.CreateBlock(factory.CreateNodeArray(trailingBlockStatements), multiLine: true));

        if (!leadingStaticBlock.IsNone || !syntheticConstructor.IsNone || !trailingStaticBlock.IsNone)
        {
            var newMembers = new List<Node>();
            var memberNodes = store.Nodes(members);

            // add the NamedEvaluation helper block, if needed
            var existingNamedEvaluationHelperBlockIndex = -1;
            for (var i = 0; i < memberNodes.Length; i++)
            {
                if (NamedEvaluation.IsClassNamedEvaluationHelperBlock(store, memberNodes[i]))
                {
                    existingNamedEvaluationHelperBlockIndex = i;
                    break;
                }
            }

            // add the leading `static {}` block
            if (!leadingStaticBlock.IsNone)
            {
                // add the `static {}` block after any existing NamedEvaluation helper block, if one exists.
                newMembers.AddRange(memberNodes[..(existingNamedEvaluationHelperBlockIndex + 1)]);
                newMembers.Add(leadingStaticBlock);
                newMembers.AddRange(memberNodes[(existingNamedEvaluationHelperBlockIndex + 1)..]);
            }
            else
            {
                newMembers.AddRange(memberNodes);
            }

            // append the synthetic constructor, if necessary
            if (!syntheticConstructor.IsNone)
            {
                newMembers.Add(syntheticConstructor);
            }

            // append a trailing `static {}` block, if necessary
            if (!trailingStaticBlock.IsNone)
            {
                newMembers.Add(trailingStaticBlock);
            }

            members = FactoryUtilitiesPublic.SetTextRange(store, factory.CreateNodeArray(newMembers), members);
        }

        var lexicalEnvironment = context.EndLexicalEnvironment();
        Node classExpression;
        if (classDecorators is not null)
        {
            // We use `var` instead of `let` so we can leverage NamedEvaluation to define the class name
            // and still be able to ensure it is initialized prior to any use in `static {}`.

            // produces:
            //  (() => {
            //      let _classDecorators = [...];
            //      let _classDescriptor;
            //      let _classExtraInitializers = [];
            //      let _classThis;
            //      ...
            //      var C = class {
            //          static {
            //              __esDecorate(null, _classDescriptor = { value: this }, _classDecorators, ...);
            //              // `C` is initialized here
            //              C = _classThis = _classDescriptor.value;
            //          }
            //          static x = 1;
            //          static y = C.x; // `C` will already be defined here.
            //          static { ... }
            //      };
            //      return C;
            //  })();

            classExpression = factory.CreateClassExpression(default, default, default, heritageClauses, members);
            if (!info.ClassThis.IsNone)
            {
                classExpression = NamedEvaluation.InjectClassThisAssignmentIfMissing(factory, classExpression, info.ClassThis);
            }

            var classReferenceDeclaration = factory.CreateVariableDeclaration(classReference, default, default, classExpression);
            var classReferenceVarDeclList = factory.CreateVariableDeclarationList(Array(classReferenceDeclaration));
            var returnExpr = !info.ClassThis.IsNone ? factory.CreateAssignment(classReference, info.ClassThis) : classReference;
            classDefinitionStatements.Add(factory.CreateVariableStatement(default, classReferenceVarDeclList));
            classDefinitionStatements.Add(factory.CreateReturnStatement(returnExpr));
        }
        else
        {
            // produces:
            //  return <classExpression>;
            classExpression = factory.CreateClassExpression(default, store.Name(node), default, heritageClauses, members);
            classDefinitionStatements.Add(factory.CreateReturnStatement(classExpression));
        }

        if (shouldTransformPrivateStaticElementsInClass)
        {
            FactoryEmitNode.AddInternalEmitFlags(store, classExpression, InternalEmitFlags.TransformPrivateStaticElements);
            foreach (var member in store.Nodes(store.Members(classExpression)))
            {
                if ((TransformerUtilities.IsPrivateIdentifierClassElementDeclaration(store, member) || TransformerUtilities.IsAutoAccessorPropertyDeclaration(store, member)) && TransformerUtilities.HasStaticModifier(store, member))
                {
                    FactoryEmitNode.AddInternalEmitFlags(store, member, InternalEmitFlags.TransformPrivateStaticElements);
                }
            }
        }

        NodeFactory.SetOriginalNode(store, classExpression, node);
        return factory.CreateImmediatelyInvokedArrowFunction(factory.MergeLexicalEnvironment(classDefinitionStatements, lexicalEnvironment));
    }

    private static void AddRange(List<Node> to, List<Node>? from)
    {
        if (from is not null)
        {
            to.AddRange(from);
        }
    }

    private bool IsDecoratedClassLike(Node node)
    {
        return TransformerUtilities.ClassOrConstructorParameterIsDecorated(store, useLegacyDecorators: false, node) ||
            TransformerUtilities.ChildIsDecorated(store, useLegacyDecorators: false, node);
    }

    private VisitResult VisitClassDeclaration(Node node)
    {
        if (IsDecoratedClassLike(node))
        {
            var statements = new List<Node>();
            var originalClass = TransformerUtilities.GetOriginalNode(store, node, n => Utilities.IsClassLike(store, n));
            if (originalClass.IsNone)
            {
                originalClass = node;
            }
            var className = !store.Name(originalClass).IsNone ? factory.CreateStringLiteralFromNode(store.Name(originalClass)) : factory.CreateStringLiteral(store.Texts.Add("default"));
            var isExport = Utilities.HasSyntacticModifier(store, node, ModifierFlags.Export);
            var isDefault = Utilities.HasSyntacticModifier(store, node, ModifierFlags.Default);
            if (store.Name(node).IsNone)
            {
                node = NamedEvaluation.InjectClassNamedEvaluationHelperBlockIfMissing(context, node, className);
            }
            if (isExport && isDefault)
            {
                var iife = TransformClassLike(node);
                if (!store.Name(node).IsNone)
                {
                    //  let C = (() => { ... })();
                    //  export default C;
                    var varDecl = factory.CreateVariableDeclaration(factory.GetLocalName(node), default, default, iife);
                    NodeFactory.SetOriginalNode(store, varDecl, node);

                    var varDecls = factory.CreateVariableDeclarationList(Array(varDecl), NodeFlags.Let);
                    var varStatement = factory.CreateVariableStatement(default, varDecls);
                    statements.Add(varStatement);

                    var exportStatement = factory.CreateExportDefault(factory.GetDeclarationName(node));
                    NodeFactory.SetOriginalNode(store, exportStatement, node);
                    FactoryEmitNode.SetCommentRange(store, exportStatement, FactoryEmitNode.GetCommentRange(store, node));
                    FactoryEmitNode.SetSourceMapRange(store, exportStatement, TransformerUtilities.MoveRangePastDecorators(store, node));
                    statements.Add(exportStatement);
                }
                else
                {
                    //  export default (() => { ... })();
                    var exportStatement = factory.CreateExportDefault(iife);
                    NodeFactory.SetOriginalNode(store, exportStatement, node);
                    FactoryEmitNode.SetCommentRange(store, exportStatement, FactoryEmitNode.GetCommentRange(store, node));
                    FactoryEmitNode.SetSourceMapRange(store, exportStatement, TransformerUtilities.MoveRangePastDecorators(store, node));
                    statements.Add(exportStatement);
                }
            }
            else
            {
                //  let C = (() => { ... })();
                Debug.Assert(!store.Name(node).IsNone, "A class declaration that is not a default export must have a name.");
                var iife = TransformClassLike(node);
                Func<Node, VisitResult> modifierVisitorNoExport = isExport ? n => store.Kind(n) == SyntaxKind.ExportKeyword ? VisitResult.None : ModifierVisitor(n) : modifierVisitor;
                var modifiers = VisitList(store.Modifiers(node), modifierVisitorNoExport);
                var declName = factory.GetLocalName(node, allowComments: false, allowSourceMaps: true);
                var varDecl = factory.CreateVariableDeclaration(declName, default, default, iife);
                NodeFactory.SetOriginalNode(store, varDecl, node);

                var varDecls = factory.CreateVariableDeclarationList(Array(varDecl), NodeFlags.Let);
                var varStatement = factory.CreateVariableStatement(modifiers, varDecls);
                NodeFactory.SetOriginalNode(store, varStatement, node);
                FactoryEmitNode.SetCommentRange(store, varStatement, FactoryEmitNode.GetCommentRange(store, node));
                statements.Add(varStatement);
                if (isExport)
                {
                    //  export { C };
                    var exportStatement = factory.CreateExternalModuleExport(declName);
                    NodeFactory.SetOriginalNode(store, exportStatement, node);
                    statements.Add(exportStatement);
                }
            }
            return statements.Count == 1 ? statements[0] : VisitResult.Many(statements);
        }
        else
        {
            var modifiers = VisitList(store.Modifiers(node), modifierVisitor);
            var heritageClauses = VisitList(store.HeritageClauses(node), visitor);
            EnterClass(null);
            var members = VisitList(store.Members(node), classElementVisitor);
            ExitClass();
            return factory.UpdateClassDeclaration(node, modifiers, store.Name(node), default, heritageClauses, members);
        }
    }

    private Node VisitClassExpression(Node node)
    {
        if (IsDecoratedClassLike(node))
        {
            var iife = TransformClassLike(node);
            NodeFactory.SetOriginalNode(store, iife, node);
            return iife;
        }
        else
        {
            var modifiers = VisitList(store.Modifiers(node), modifierVisitor);
            var heritageClauses = VisitList(store.HeritageClauses(node), visitor);
            EnterClass(null);
            var members = VisitList(store.Members(node), classElementVisitor);
            ExitClass();
            return factory.UpdateClassExpression(node, modifiers, store.Name(node), default, heritageClauses, members);
        }
    }

    private List<Node>? PrepareConstructor(Node parent, ClassInfo info)
    {
        // Decorated instance members can add "extra" initializers to the instance. If a class contains any instance
        // fields, we'll inject the `__runInitializers()` call for these extra initializers into the initializer of
        // the first class member that will be initialized. However, if the class does not contain any fields that
        // we can piggyback on, we need to synthesize a `__runInitializers()` call in the constructor instead.
        if (Core.Some(info.PendingInstanceInitializers))
        {
            // If there are instance extra initializers we need to add them to the body along with any
            // field initializers
            var statements = new List<Node>
            {
                factory.CreateExpressionStatement(factory.InlineExpressions(info.PendingInstanceInitializers)),
            };

            info.PendingInstanceInitializers = null;
            return statements;
        }
        return null;
    }

    private void AddVisited(List<Node> target, NodeList nodes, int start, int count)
    {
        var visited = Visitors.VisitNodes(factory, nodes, visitor, start, count);
        target.AddRange(store.Nodes(visited));
    }

    private void TransformConstructorBodyWorker(List<Node> statementsOut, NodeList statementsIn, int statementOffset, List<int> superPath, int superPathDepth, List<Node> initializerStatements)
    {
        var superStatementIndex = superPath[superPathDepth];
        var superStatement = store.Element(statementsIn, superStatementIndex);
        AddVisited(statementsOut, statementsIn, statementOffset, superStatementIndex - statementOffset);
        if (store.Kind(superStatement) == SyntaxKind.TryStatement)
        {
            var tryBlockStatements = new List<Node>();

            var tryBlock = store.TryBlock(superStatement);
            TransformConstructorBodyWorker(
                tryBlockStatements,
                store.Statements(tryBlock),
                0,
                superPath,
                superPathDepth + 1,
                initializerStatements);

            var tryBlockStatementsArray = factory.CreateNodeArray(tryBlockStatements);
            FactoryUtilitiesPublic.SetTextRange(store, tryBlockStatementsArray, store.Statements(tryBlock));

            statementsOut.Add(factory.UpdateTryStatement(
                superStatement,
                factory.UpdateBlock(tryBlock, factory.CreateNodeArray(tryBlockStatements)),
                Visit(store.CatchClause(superStatement), visitor),
                Visit(store.FinallyBlock(superStatement), visitor)));
        }
        else
        {
            AddVisited(statementsOut, statementsIn, superStatementIndex, 1);
            statementsOut.AddRange(initializerStatements);
        }
        AddVisited(statementsOut, statementsIn, superStatementIndex + 1, -1);
    }

    private Node VisitConstructorDeclaration(Node node)
    {
        EnterClassElement(node);
        var modifiers = VisitList(store.Modifiers(node), modifierVisitor);
        var parameters = VisitList(store.Parameters(node), visitor);
        var body = Node.None;
        var nodeBody = store.Body(node);
        if (!nodeBody.IsNone && classInfo is not null)
        {
            // If there are instance extra initializers we need to add them to the body along with any
            // field initializers
            var initializerStatements = PrepareConstructor(classInfo.Class, classInfo);
            if (initializerStatements is not null)
            {
                var statements = new List<Node>();
                var bodyStatements = store.Statements(nodeBody);
                var nonPrologueStart = factory.CopyPrologue(store.Nodes(bodyStatements), statements, ensureUseStrict: false, visitor);
                var superStatementIndices = TransformerUtilities.FindSuperStatementIndexPath(store, bodyStatements, nonPrologueStart);
                if (superStatementIndices.Count > 0)
                {
                    TransformConstructorBodyWorker(statements, bodyStatements, nonPrologueStart, superStatementIndices, 0, initializerStatements);
                }
                else
                {
                    statements.AddRange(initializerStatements);
                    statements.AddRange(store.Nodes(VisitList(bodyStatements, visitor)));
                }
                body = factory.CreateBlock(factory.CreateNodeArray(statements), multiLine: true);
                NodeFactory.SetOriginalNode(store, body, nodeBody);
                FactoryUtilitiesPublic.SetTextRange(store, body, nodeBody);
            }
        }

        if (body.IsNone)
        {
            body = Visit(nodeBody, visitor);
        }
        ExitClassElement();
        return factory.UpdateConstructorDeclaration(node, modifiers, parameters, body);
    }

    private Node FinishClassElement(Node updated, Node original)
    {
        if (updated != original)
        {
            // While we emit the source map for the node after skipping decorators and modifiers,
            // we need to emit the comments for the original range.
            FactoryEmitNode.SetCommentRange(store, updated, original);
            FactoryEmitNode.SetSourceMapRange(store, updated, TransformerUtilities.MoveRangePastDecorators(store, original));
        }
        return updated;
    }

    private List<Node> StatementsFor(ClassInfo info, Node member)
    {
        if (TransformerUtilities.IsMethodOrAccessor(store, member) || TransformerUtilities.IsAutoAccessorPropertyDeclaration(store, member))
        {
            return Utilities.IsStatic(store, member) ?
                info.StaticNonFieldDecorationStatements ??= [] :
                info.NonStaticNonFieldDecorationStatements ??= [];
        }
        if (store.Kind(member) == SyntaxKind.PropertyDeclaration && !TransformerUtilities.IsAutoAccessorPropertyDeclaration(store, member))
        {
            return Utilities.IsStatic(store, member) ?
                info.StaticFieldDecorationStatements ??= [] :
                info.NonStaticFieldDecorationStatements ??= [];
        }
        throw new InvalidOperationException("Debug Failure.");
    }

    private PartialClassElement PartialTransformClassElement(Node member, ClassInfo? info, Func<Node, NodeList, Node>? createDescriptor)
    {
        var referencedName = Node.None;
        var name = Node.None;
        var initializersName = Node.None;
        var extraInitializersName = Node.None;
        var thisArg = Node.None;
        var descriptorName = Node.None;
        if (info is null)
        {
            var modifiersNoInfo = VisitList(store.Modifiers(member), modifierVisitor);
            EnterName();
            name = VisitPropertyName(store.Name(member));
            ExitName();
            return new PartialClassElement { Modifiers = modifiersNoInfo, ReferencedName = referencedName, Name = name, InitializersName = initializersName, DescriptorName = descriptorName, ThisArg = thisArg };
        }

        // Member decorators require privileged access to private names. However, computed property
        // evaluation occurs interspersed with decorator evaluation. This means that if we encounter
        // a computed property name we must inline decorator evaluation.
        var memberDecorators = TransformAllDecoratorsOfDeclaration(TransformerUtilities.GetAllDecoratorsOfClassElement(store, member));
        var modifiers = VisitList(store.Modifiers(member), modifierVisitor);

        if (memberDecorators is not null)
        {
            var memberDecoratorsName = CreateHelperVariable(member, "decorators");
            var memberDecoratorsArray = factory.CreateArrayLiteralExpression(factory.CreateNodeArray(memberDecorators));
            var memberDecoratorsAssignment = factory.CreateAssignment(memberDecoratorsName, memberDecoratorsArray);
            var memberInfo = new MemberInfo { MemberDecoratorsName = memberDecoratorsName };
            (info.MemberInfos ??= []).Add((member, memberInfo));
            (pendingExpressions ??= []).Add(memberDecoratorsAssignment);

            // 5. Static non-field (method/getter/setter/auto-accessor) element decorators are applied
            // 6. Non-static non-field (method/getter/setter/auto-accessor) element decorators are applied
            // 7. Static field (excl. auto-accessor) element decorators are applied
            // 8. Non-static field (excl. auto-accessor) element decorators are applied
            var statements = StatementsFor(info, member);

            var kind = store.Kind(member) == SyntaxKind.GetAccessor ? "getter" :
                store.Kind(member) == SyntaxKind.SetAccessor ? "setter" :
                store.Kind(member) == SyntaxKind.MethodDeclaration ? "method" :
                TransformerUtilities.IsAutoAccessorPropertyDeclaration(store, member) ? "accessor" :
                store.Kind(member) == SyntaxKind.PropertyDeclaration ? "field" :
                throw new InvalidOperationException("Debug Failure.");

            var memberName = store.Name(member);
            ESDecorateName propertyName;
            if (store.Kind(memberName) is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier)
            {
                propertyName = new ESDecorateName(Computed: false, memberName);
            }
            else if (Utilities.IsPropertyNameLiteral(store, memberName))
            {
                propertyName = new ESDecorateName(Computed: true, factory.CreateStringLiteralFromNode(memberName));
            }
            else
            {
                var expression = store.Expression(memberName);
                if (Utilities.IsPropertyNameLiteral(store, expression) && store.Kind(expression) != SyntaxKind.Identifier)
                {
                    propertyName = new ESDecorateName(Computed: true, factory.CreateStringLiteralFromNode(expression));
                }
                else
                {
                    EnterName();
                    (referencedName, name) = VisitReferencedPropertyName(memberName);
                    propertyName = new ESDecorateName(Computed: true, referencedName);
                    ExitName();
                }
            }

            var esContext = new ESDecorateContext
            {
                Kind = kind,
                ElementName = propertyName,
                Static = Utilities.IsStatic(store, member),
                Private = store.Kind(memberName) == SyntaxKind.PrivateIdentifier,
                Access = new ESDecorateClassElementAccess(
                    // 15.7.3 CreateDecoratorAccessObject (kind, name)
                    // 2. If _kind_ is ~field~, ~method~, ~accessor~, or ~getter~, then ...
                    Get: store.Kind(member) is SyntaxKind.PropertyDeclaration or SyntaxKind.GetAccessor or SyntaxKind.MethodDeclaration,
                    // 3. If _kind_ is ~field~, ~accessor~, or ~setter~, then ...
                    Set: store.Kind(member) is SyntaxKind.PropertyDeclaration or SyntaxKind.SetAccessor),
                Metadata = info.MetadataReference,
            };

            if (TransformerUtilities.IsMethodOrAccessor(store, member))
            {
                var methodExtraInitializersName = Utilities.IsStatic(store, member) ? info.StaticMethodExtraInitializersName : info.InstanceMethodExtraInitializersName;
                Debug.Assert(!methodExtraInitializersName.IsNone);

                var descriptor = Node.None;
                if (TransformerUtilities.IsPrivateIdentifierClassElementDeclaration(store, member) && createDescriptor is not null)
                {
                    descriptor = createDescriptor(member, VisitList(modifiers, n => store.Kind(n) == SyntaxKind.AsyncKeyword ? n : VisitResult.None));
                    memberInfo.MemberDescriptorName = descriptorName = CreateHelperVariable(member, "descriptor");
                    descriptor = factory.CreateAssignment(descriptorName, descriptor);
                }

                var esDecorateExpression = context.GetEmitHelperFactory().CreateESDecorateHelper(This(), descriptor.IsNone ? Null() : descriptor, memberDecoratorsName, esContext, Null(), methodExtraInitializersName);
                var esDecorateStatement = factory.CreateExpressionStatement(esDecorateExpression);
                FactoryEmitNode.SetSourceMapRange(store, esDecorateStatement, TransformerUtilities.MoveRangePastDecorators(store, member));
                statements.Add(esDecorateStatement);
            }
            else if (store.Kind(member) == SyntaxKind.PropertyDeclaration)
            {
                if (memberInfo.MemberInitializersName.IsNone)
                {
                    memberInfo.MemberInitializersName = CreateHelperVariable(member, "initializers");
                }
                initializersName = memberInfo.MemberInitializersName;
                if (memberInfo.MemberExtraInitializersName.IsNone)
                {
                    memberInfo.MemberExtraInitializersName = CreateHelperVariable(member, "extraInitializers");
                }
                extraInitializersName = memberInfo.MemberExtraInitializersName;

                if (Utilities.IsStatic(store, member))
                {
                    thisArg = info.ClassThis;
                }

                var descriptor = Node.None;
                if (TransformerUtilities.IsPrivateIdentifierClassElementDeclaration(store, member) && TransformerUtilities.HasAccessorModifier(store, member) && createDescriptor is not null)
                {
                    descriptor = createDescriptor(member, NodeList.None);
                    memberInfo.MemberDescriptorName = descriptorName = CreateHelperVariable(member, "descriptor");
                    descriptor = factory.CreateAssignment(descriptorName, descriptor);
                }

                // produces:
                //  _static_field_initializers = __esDecorate(null, null, _static_member_decorators, { kind: "field", name: "...", static: true, private: ..., access: { ... } }, _staticExtraInitializers);
                //  _field_initializers = __esDecorate(null, null, _member_decorators, { kind: "field", name: "...", static: false, private: ..., access: { ... } }, _instanceExtraInitializers);
                var esDecorateExpression = context.GetEmitHelperFactory().CreateESDecorateHelper(
                    TransformerUtilities.IsAutoAccessorPropertyDeclaration(store, member) ? This() : Null(),
                    descriptor.IsNone ? Null() : descriptor,
                    memberDecoratorsName,
                    esContext,
                    initializersName,
                    extraInitializersName);
                var esDecorateStatement = factory.CreateExpressionStatement(esDecorateExpression);
                FactoryEmitNode.SetSourceMapRange(store, esDecorateStatement, TransformerUtilities.MoveRangePastDecorators(store, member));
                statements.Add(esDecorateStatement);
            }
        }

        if (name.IsNone)
        {
            EnterName();
            name = VisitPropertyName(store.Name(member));
            ExitName();
        }

        if (!Core.Some(store.Nodes(modifiers)) && store.Kind(member) is SyntaxKind.MethodDeclaration or SyntaxKind.PropertyDeclaration)
        {
            // Don't emit leading comments on the name for methods and properties without modifiers, otherwise we
            // will end up printing duplicate comments.
            FactoryEmitNode.SetEmitFlags(store, name, EmitFlags.NoLeadingComments);
        }

        return new PartialClassElement { Modifiers = modifiers, ReferencedName = referencedName, Name = name, InitializersName = initializersName, ExtraInitializersName = extraInitializersName, DescriptorName = descriptorName, ThisArg = thisArg };
    }

    private VisitResult VisitMethodDeclaration(Node node)
    {
        EnterClassElement(node);
        var partial = PartialTransformClassElement(node, classInfo, CreateMethodDescriptorObject);
        if (!partial.DescriptorName.IsNone)
        {
            ExitClassElement();
            return FinishClassElement(CreateMethodDescriptorForwarder(partial.Modifiers, partial.Name, partial.DescriptorName), node);
        }
        else
        {
            var parameters = VisitList(store.Parameters(node), visitor);
            var body = Visit(store.Body(node), visitor);
            ExitClassElement();
            return FinishClassElement(factory.UpdateMethodDeclaration(node, partial.Modifiers, store.AsteriskToken(node), partial.Name, default, default, parameters, default, body), node);
        }
    }

    private VisitResult VisitGetAccessorDeclaration(Node node)
    {
        EnterClassElement(node);
        var partial = PartialTransformClassElement(node, classInfo, CreateGetAccessorDescriptorObject);
        if (!partial.DescriptorName.IsNone)
        {
            ExitClassElement();
            return FinishClassElement(CreateGetAccessorDescriptorForwarder(partial.Modifiers, partial.Name, partial.DescriptorName), node);
        }
        else
        {
            var parameters = VisitList(store.Parameters(node), visitor);
            var body = Visit(store.Body(node), visitor);
            ExitClassElement();
            return FinishClassElement(factory.UpdateGetAccessorDeclaration(node, partial.Modifiers, partial.Name, parameters, default, body), node);
        }
    }

    private VisitResult VisitSetAccessorDeclaration(Node node)
    {
        EnterClassElement(node);
        var partial = PartialTransformClassElement(node, classInfo, CreateSetAccessorDescriptorObject);
        if (!partial.DescriptorName.IsNone)
        {
            ExitClassElement();
            return FinishClassElement(CreateSetAccessorDescriptorForwarder(partial.Modifiers, partial.Name, partial.DescriptorName), node);
        }
        else
        {
            var parameters = VisitList(store.Parameters(node), visitor);
            var body = Visit(store.Body(node), visitor);
            ExitClassElement();
            return FinishClassElement(factory.UpdateSetAccessorDeclaration(node, partial.Modifiers, partial.Name, parameters, body), node);
        }
    }

    private VisitResult VisitClassStaticBlockDeclaration(Node node)
    {
        EnterClassElement(node);

        VisitResult result;
        if (NamedEvaluation.IsClassNamedEvaluationHelperBlock(store, node))
        {
            result = Visitors.VisitEachChild(node, visitor, context);
        }
        else if (NamedEvaluation.IsClassThisAssignmentBlock(store, node))
        {
            var savedClassThis = classThis;
            classThis = Node.None;
            result = Visitors.VisitEachChild(node, visitor, context);
            classThis = savedClassThis;
        }
        else
        {
            node = Visitors.VisitEachChild(node, visitor, context);
            result = node;
            if (classInfo is not null)
            {
                classInfo.HasStaticInitializers = true;
                if (Core.Some(classInfo.PendingStaticInitializers))
                {
                    // If we tried to inject the pending initializers into the current block, we might run into
                    // variable name collisions due to sharing this blocks scope. To avoid this, we inject a new
                    // static block that contains the pending initializers that precedes this block.
                    var statements = new List<Node>();
                    foreach (var initializer in classInfo.PendingStaticInitializers)
                    {
                        var initializerStatement = factory.CreateExpressionStatement(initializer);
                        FactoryEmitNode.SetSourceMapRange(store, initializerStatement, FactoryEmitNode.GetSourceMapRange(store, initializer));
                        statements.Add(initializerStatement);
                    }
                    var body = factory.CreateBlock(factory.CreateNodeArray(statements), multiLine: true);
                    var staticBlock = factory.CreateClassStaticBlockDeclaration(body);
                    result = VisitResult.Many([staticBlock, node]);
                    classInfo.PendingStaticInitializers = null;
                }
            }
        }

        ExitClassElement();
        return result;
    }

    private VisitResult VisitPropertyDeclaration(Node node)
    {
        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node, CanIgnoreEmptyStringLiteralInAssignedName(store.Initializer(node)));
        }

        EnterClassElement(node);

        // TODO(rbuckton): We support decorating `declare x` fields with legacyDecorators, but we currently don't
        //                 support them with esDecorators. We need to consider whether we will support them in the
        //                 future, and how. For now, these should be elided by the `ts` transform.
        Debug.Assert(!TransformerUtilities.IsAmbientPropertyDeclaration(store, node), "Not yet implemented.");

        // 10.2.1.3 RS: EvaluateBody
        //   Initializer : `=` AssignmentExpression
        //     ...
        //     3. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true*, then
        //        a. Let _value_ be ? NamedEvaluation of |Initializer| with argument _functionObject_.[[ClassFieldInitializerName]].
        //     ...

        var partial = PartialTransformClassElement(node, classInfo, TransformerUtilities.HasAccessorModifier(store, node) ? CreateAccessorPropertyDescriptorObject : null);
        var modifiers = partial.Modifiers;
        var initializersName = partial.InitializersName;
        var extraInitializersName = partial.ExtraInitializersName;
        var descriptorName = partial.DescriptorName;
        var thisArg = partial.ThisArg;

        context.StartLexicalEnvironment();

        var initializer = Visit(store.Initializer(node), visitor);
        if (!initializersName.IsNone)
        {
            initializer = context.GetEmitHelperFactory().CreateRunInitializersHelper(
                thisArg.IsNone ? This() : thisArg,
                initializersName,
                initializer.IsNone ? factory.CreateVoidZero() : initializer);
        }

        if (Utilities.IsStatic(store, node) && classInfo is not null && !initializer.IsNone)
        {
            classInfo.HasStaticInitializers = true;
        }

        var declarations = context.EndLexicalEnvironment();
        if (Core.Some(declarations))
        {
            var statements = new List<Node>(declarations) { factory.CreateReturnStatement(initializer) };
            initializer = factory.CreateImmediatelyInvokedArrowFunction(statements);
        }

        if (classInfo is not null)
        {
            if (Utilities.IsStatic(store, node))
            {
                initializer = InjectPendingInitializers(classInfo, isStatic: true, initializer);
                if (!extraInitializersName.IsNone)
                {
                    (classInfo.PendingStaticInitializers ??= []).Add(
                        context.GetEmitHelperFactory().CreateRunInitializersHelper(
                            classInfo.ClassThis.IsNone ? This() : classInfo.ClassThis,
                            extraInitializersName));
                }
            }
            else
            {
                initializer = InjectPendingInitializers(classInfo, isStatic: false, initializer);
                if (!extraInitializersName.IsNone)
                {
                    (classInfo.PendingInstanceInitializers ??= []).Add(
                        context.GetEmitHelperFactory().CreateRunInitializersHelper(
                            This(),
                            extraInitializersName));
                }
            }
        }

        ExitClassElement();

        if (TransformerUtilities.HasAccessorModifier(store, node) && !descriptorName.IsNone)
        {
            // given:
            //  accessor #x = 1;
            //
            // emits:
            //  static {
            //      _esDecorate(null, _private_x_descriptor = { get() { return this.#x_1; }, set(value) { this.#x_1 = value; } }, ...)
            //  }
            //  ...
            //  #x_1 = 1;
            //  get #x() { return _private_x_descriptor.get.call(this); }
            //  set #x(value) { _private_x_descriptor.set.call(this, value); }

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

            var modifiersWithoutAccessor = VisitList(modifiers, n => store.Kind(n) != SyntaxKind.AccessorKeyword ? n : VisitResult.None);

            var backingField = FactoryUtilities.CreateAccessorPropertyBackingField(factory, node, modifiersWithoutAccessor, initializer);
            NodeFactory.SetOriginalNode(store, backingField, node);
            FactoryEmitNode.SetEmitFlags(store, backingField, EmitFlags.NoComments);
            FactoryEmitNode.SetSourceMapRange(store, backingField, sourceMapRange);
            FactoryEmitNode.SetSourceMapRange(store, store.Name(backingField), TransformerUtilities.RangeOf(store, store.Name(node)));

            var getter = CreateGetAccessorDescriptorForwarder(modifiersWithoutAccessor, getterName, descriptorName);
            NodeFactory.SetOriginalNode(store, getter, node);
            FactoryEmitNode.SetCommentRange(store, getter, commentRange);
            FactoryEmitNode.SetSourceMapRange(store, getter, sourceMapRange);

            var setter = CreateSetAccessorDescriptorForwarder(modifiersWithoutAccessor, setterName, descriptorName);
            NodeFactory.SetOriginalNode(store, setter, node);
            FactoryEmitNode.SetEmitFlags(store, setter, EmitFlags.NoComments);
            FactoryEmitNode.SetSourceMapRange(store, setter, sourceMapRange);

            return VisitResult.Many([backingField, getter, setter]);
        }

        return FinishClassElement(factory.UpdatePropertyDeclaration(node, modifiers, partial.Name, default, default, initializer), node);
    }

    private VisitResult VisitThisExpression(Node node)
    {
        return classThis.IsNone ? node : classThis;
    }

    private VisitResult VisitCallExpression(Node node)
    {
        if (Utilities.IsSuperProperty(store, store.Expression(node)) && !classThis.IsNone)
        {
            var expression = Visit(store.Expression(node), visitor);
            var argumentsList = VisitList(store.Arguments(node), visitor);
            var invocation = factory.CreateFunctionCallCall(expression, classThis, store.Nodes(argumentsList));
            NodeFactory.SetOriginalNode(store, invocation, node);
            FactoryUtilitiesPublic.SetTextRange(store, invocation, node);
            return invocation;
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitTaggedTemplateExpression(Node node)
    {
        if (Utilities.IsSuperProperty(store, store.Tag(node)) && !classThis.IsNone)
        {
            var tag = Visit(store.Tag(node), visitor);
            var boundTag = factory.CreateFunctionBindCall(tag, classThis, ReadOnlySpan<Node>.Empty);
            NodeFactory.SetOriginalNode(store, boundTag, node);
            FactoryUtilitiesPublic.SetTextRange(store, boundTag, node);
            var template = Visit(store.Template(node), visitor);
            return factory.UpdateTaggedTemplateExpression(node, boundTag, default, template);
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitPropertyAccessExpression(Node node)
    {
        if (Utilities.IsSuperProperty(store, node) && store.Kind(store.Name(node)) == SyntaxKind.Identifier && !classThis.IsNone && !classSuper.IsNone)
        {
            var propertyName = factory.CreateStringLiteralFromNode(store.Name(node));
            var superProperty = factory.CreateReflectGetCall(classSuper, propertyName, classThis);
            NodeFactory.SetOriginalNode(store, superProperty, store.Expression(node));
            FactoryUtilitiesPublic.SetTextRange(store, superProperty, store.Expression(node));
            return superProperty;
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitElementAccessExpression(Node node)
    {
        if (Utilities.IsSuperProperty(store, node) && !classThis.IsNone && !classSuper.IsNone)
        {
            var propertyName = Visit(store.ArgumentExpression(node), visitor);
            var superProperty = factory.CreateReflectGetCall(classSuper, propertyName, classThis);
            NodeFactory.SetOriginalNode(store, superProperty, store.Expression(node));
            FactoryUtilitiesPublic.SetTextRange(store, superProperty, store.Expression(node));
            return superProperty;
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitParameterDeclaration(Node node)
    {
        // 8.6.3 RS: IteratorBindingInitialization
        //   SingleNameBinding : BindingIdentifier Initializer?
        //     ...
        //     5. If |Initializer| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...

        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node, CanIgnoreEmptyStringLiteralInAssignedName(store.Initializer(node)));
        }

        // (Parameter decorators are not valid ES decorators: the modifiers, decorators included, are dropped here.)
        var updated = factory.UpdateParameterDeclaration(
            node,
            default,
            store.DotDotDotToken(node),
            Visit(store.Name(node), visitor),
            default,
            default,
            Visit(store.Initializer(node), visitor));

        if (updated != node)
        {
            // While we emit the source map for the node after skipping decorators and modifiers,
            // we need to emit the comments for the original range.
            FactoryEmitNode.SetCommentRange(store, updated, node);
            var moved = TransformerUtilities.MoveRangePastModifiers(store, node);
            FactoryUtilitiesPublic.SetTextRange(store, updated, moved);
            FactoryEmitNode.SetSourceMapRange(store, updated, moved);
            FactoryEmitNode.SetEmitFlags(store, store.Name(updated), EmitFlags.NoTrailingSourceMap);
        }
        return updated;
    }

    private bool IsAnonymousClassNeedingAssignedName(Node node)
    {
        return store.Kind(node) == SyntaxKind.ClassExpression && store.Name(node).IsNone && IsDecoratedClassLike(node);
    }

    private bool CanIgnoreEmptyStringLiteralInAssignedName(Node node)
    {
        // The IIFE produced for `(@dec class {})` will result in an assigned name of the form
        // `var class_1 = class { };`, and thus the empty string cannot be ignored. However, The IIFE
        // produced for `(class { @dec x; })` will not result in an assigned name since it
        // transforms to `return class { };`, and thus the empty string *can* be ignored.

        var innerExpression = FactoryUtilities.SkipOuterExpressions(store, node);
        return store.Kind(innerExpression) == SyntaxKind.ClassExpression && store.Name(innerExpression).IsNone && !TransformerUtilities.ClassOrConstructorParameterIsDecorated(store, useLegacyDecorators: false, innerExpression);
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
        return Visitors.VisitEachChild(node, discardedValueVisitor, context);
    }

    private VisitResult VisitBinaryExpression(Node node, bool discarded)
    {
        if (Utilities.IsDestructuringAssignment(store, node))
        {
            var left = VisitAssignmentPattern(store.Left(node));
            var right = Visit(store.Right(node), visitor);
            return factory.UpdateBinaryExpression(node, left, store.OperatorToken(node), right);
        }

        if (Utilities.IsAssignmentExpression(store, node))
        {
            // 13.15.2 RS: Evaluation
            //   AssignmentExpression : LeftHandSideExpression `=` AssignmentExpression
            //     1. If |LeftHandSideExpression| is neither an |ObjectLiteral| nor an |ArrayLiteral|, then
            //        a. Let _lref_ be ? Evaluation of |LeftHandSideExpression|.
            //        b. If IsAnonymousFunctionDefinition(|AssignmentExpression|) and IsIdentifierRef of |LeftHandSideExpression| are both *true*, then
            //           i. Let _rval_ be ? NamedEvaluation of |AssignmentExpression| with argument _lref_.[[ReferencedName]].
            //     ...

            if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
            {
                node = NamedEvaluation.TransformNamedEvaluation(context, node, CanIgnoreEmptyStringLiteralInAssignedName(store.Right(node)));
                return Visitors.VisitEachChild(node, visitor, context);
            }

            var nodeLeft = store.Left(node);
            if (Utilities.IsSuperProperty(store, nodeLeft) && !classThis.IsNone && !classSuper.IsNone)
            {
                var setterName = store.Kind(nodeLeft) == SyntaxKind.ElementAccessExpression ? Visit(store.ArgumentExpression(nodeLeft), visitor) :
                    store.Kind(store.Name(nodeLeft)) == SyntaxKind.Identifier ? factory.CreateStringLiteralFromNode(store.Name(nodeLeft)) :
                    Node.None;
                if (!setterName.IsNone)
                {
                    // super.x = ...
                    // super.x += ...
                    // super[x] = ...
                    // super[x] += ...
                    var expression = Visit(store.Right(node), visitor);
                    var operatorKind = store.Kind(store.OperatorToken(node));
                    if (TransformerUtilities.IsCompoundAssignment(operatorKind))
                    {
                        var getterName = setterName;
                        if (!TransformerUtilities.IsSimpleInlineableExpression(store, setterName))
                        {
                            getterName = factory.CreateTempVariable(hoistVariableDeclaration);
                            setterName = factory.CreateAssignment(getterName, setterName);
                        }

                        var superPropertyGet = factory.CreateReflectGetCall(
                            classSuper,
                            getterName,
                            classThis);
                        NodeFactory.SetOriginalNode(store, superPropertyGet, nodeLeft);
                        FactoryUtilitiesPublic.SetTextRange(store, superPropertyGet, nodeLeft);

                        expression = factory.CreateBinaryExpression(
                            superPropertyGet,
                            factory.CreateToken(TransformerUtilities.GetNonAssignmentOperatorForCompoundAssignment(operatorKind)),
                            expression);
                        FactoryUtilitiesPublic.SetTextRange(store, expression, node);
                    }

                    var temp = discarded ? Node.None : factory.CreateTempVariable(hoistVariableDeclaration);
                    if (!temp.IsNone)
                    {
                        expression = factory.CreateAssignment(temp, expression);
                        FactoryUtilitiesPublic.SetTextRange(store, temp, node);
                    }

                    expression = factory.CreateReflectSetCall(
                        classSuper,
                        setterName,
                        expression,
                        classThis);
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
        }

        if (store.Kind(store.OperatorToken(node)) == SyntaxKind.CommaToken)
        {
            var left = Visit(store.Left(node), discardedValueVisitor);
            var right = Visit(store.Right(node), discarded ? discardedValueVisitor : visitor);
            return factory.UpdateBinaryExpression(node, left, store.OperatorToken(node), right);
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitPreOrPostfixUnaryExpression(Node node, bool discarded)
    {
        var @operator = store.Operator(node);
        if (@operator is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken)
        {
            var operand = Utilities.SkipParentheses(store, store.Operand(node));
            if (Utilities.IsSuperProperty(store, operand) && !classThis.IsNone && !classSuper.IsNone)
            {
                var setterName = store.Kind(operand) == SyntaxKind.ElementAccessExpression ? Visit(store.ArgumentExpression(operand), visitor) :
                    store.Kind(store.Name(operand)) == SyntaxKind.Identifier ? factory.CreateStringLiteralFromNode(store.Name(operand)) :
                    Node.None;
                if (!setterName.IsNone)
                {
                    var getterName = setterName;
                    if (!TransformerUtilities.IsSimpleInlineableExpression(store, setterName))
                    {
                        getterName = factory.CreateTempVariable(hoistVariableDeclaration);
                        setterName = factory.CreateAssignment(getterName, setterName);
                    }

                    var expression = factory.CreateReflectGetCall(classSuper, getterName, classThis);
                    NodeFactory.SetOriginalNode(store, expression, node);
                    FactoryUtilitiesPublic.SetTextRange(store, expression, node);

                    // If the result of this expression is discarded (i.e., it's in a position where the result
                    // will be otherwise unused, such as in an expression statement or the left side of a comma), we
                    // don't need to create an extra temp variable to hold the result:
                    //
                    //  source (discarded):
                    //    super.x++;
                    //  generated:
                    //    _a = Reflect.get(_super, "x"), _a++, Reflect.set(_super, "x", _a);
                    //
                    // Above, the temp variable `_a` is used to perform the correct coercion (i.e., number or
                    // bigint). Since the result of the postfix unary is discarded, we don't need to capture the
                    // result of the expression.
                    //
                    //  source (not discarded):
                    //    y = super.x++;
                    //  generated:
                    //    y = (_a = Reflect.get(_super, "x"), _b = _a++, Reflect.set(_super, "x", _a), _b);
                    //
                    // When the result isn't discarded, we introduce a new temp variable (`_b`) to capture the
                    // result of the operation so that we can provide it to `y` when the assignment is complete.

                    var temp = discarded ? Node.None : factory.CreateTempVariable(hoistVariableDeclaration);
                    expression = FactoryUtilities.ExpandPreOrPostfixIncrementOrDecrementExpression(factory, node, expression, hoistVariableDeclaration, temp);
                    expression = factory.CreateReflectSetCall(classSuper, setterName, expression, classThis);
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

    private (Node ReferencedName, Node Name) VisitReferencedPropertyName(Node node)
    {
        if (Utilities.IsPropertyNameLiteral(store, node) || store.Kind(node) == SyntaxKind.PrivateIdentifier)
        {
            var referencedName = factory.CreateStringLiteralFromNode(node);
            var name = Visit(node, visitor);
            return (referencedName, name);
        }

        if (Utilities.IsPropertyNameLiteral(store, store.Expression(node)) && store.Kind(store.Expression(node)) != SyntaxKind.Identifier)
        {
            var referencedName = factory.CreateStringLiteralFromNode(store.Expression(node));
            var name = Visit(node, visitor);
            return (referencedName, name);
        }

        {
            var referencedName = factory.GetGeneratedNameForNode(node);
            hoistVariableDeclaration(referencedName);

            var key = context.GetEmitHelperFactory().CreatePropKeyHelper(Visit(store.Expression(node), visitor));
            var assignment = factory.CreateAssignment(referencedName, key);
            var name = factory.UpdateComputedPropertyName(node, InjectPendingExpressions(assignment));
            return (referencedName, name);
        }
    }

    private Node VisitPropertyName(Node node)
    {
        if (store.Kind(node) == SyntaxKind.ComputedPropertyName)
        {
            return VisitComputedPropertyName(node);
        }
        return Visit(node, visitor);
    }

    private Node VisitComputedPropertyName(Node node)
    {
        var expression = Visit(store.Expression(node), visitor);
        if (!TransformerUtilities.IsSimpleInlineableExpression(store, expression))
        {
            expression = InjectPendingExpressions(expression);
        }
        return factory.UpdateComputedPropertyName(node, expression);
    }

    private VisitResult VisitPropertyAssignment(Node node)
    {
        // 13.2.5.5 RS: PropertyDefinitionEvaluation
        //   PropertyAssignment : PropertyName `:` AssignmentExpression
        //     ...
        //     5. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true* and _isProtoSetter_ is *false*, then
        //        a. Let _popValue_ be ? NamedEvaluation of |AssignmentExpression| with argument _propKey_.
        //     ...

        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node, CanIgnoreEmptyStringLiteralInAssignedName(store.Initializer(node)));
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitVariableDeclaration(Node node)
    {
        // 14.3.1.2 RS: Evaluation
        //   LexicalBinding : BindingIdentifier Initializer
        //     ...
        //     3. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //        a. Let _value_ be ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...

        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node, CanIgnoreEmptyStringLiteralInAssignedName(store.Initializer(node)));
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitBindingElement(Node node)
    {
        // 8.6.3 RS: IteratorBindingInitialization
        //   SingleNameBinding : BindingIdentifier Initializer?
        //     ...
        //     5. If |Initializer| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...

        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node, CanIgnoreEmptyStringLiteralInAssignedName(store.Initializer(node)));
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private Node VisitDestructuringAssignmentTarget(Node node)
    {
        if (store.Kind(node) is SyntaxKind.ObjectLiteralExpression or SyntaxKind.ArrayLiteralExpression)
        {
            return VisitAssignmentPattern(node);
        }

        if (Utilities.IsSuperProperty(store, node) && !classThis.IsNone && !classSuper.IsNone)
        {
            var propertyName = store.Kind(node) == SyntaxKind.ElementAccessExpression ? Visit(store.ArgumentExpression(node), visitor) :
                store.Kind(store.Name(node)) == SyntaxKind.Identifier ? factory.CreateStringLiteralFromNode(store.Name(node)) :
                Node.None;
            if (!propertyName.IsNone)
            {
                var paramName = factory.CreateTempVariable(null);
                var expression = factory.CreateAssignmentTargetWrapper(
                    paramName,
                    factory.CreateReflectSetCall(
                        classSuper,
                        propertyName,
                        paramName,
                        classThis));
                NodeFactory.SetOriginalNode(store, expression, node);
                FactoryUtilitiesPublic.SetTextRange(store, expression, node);
                return expression;
            }
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private Node VisitAssignmentElement(Node node)
    {
        // 13.15.5.5 RS: IteratorDestructuringAssignmentEvaluation
        //   AssignmentElement : DestructuringAssignmentTarget Initializer?
        //     ...
        //     4. If |Initializer| is present and _value_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) and IsIdentifierRef of |DestructuringAssignmentTarget| are both *true*, then
        //           i. Let _v_ be ? NamedEvaluation of |Initializer| with argument _lref_.[[ReferencedName]].
        //     ...

        if (Utilities.IsAssignmentExpression(store, node, excludeCompoundAssignment: true))
        {
            if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
            {
                node = NamedEvaluation.TransformNamedEvaluation(context, node, CanIgnoreEmptyStringLiteralInAssignedName(store.Right(node)));
            }

            var assignmentTarget = VisitDestructuringAssignmentTarget(store.Left(node));
            var initializer = Visit(store.Right(node), visitor);
            return factory.UpdateBinaryExpression(node, assignmentTarget, store.OperatorToken(node), initializer);
        }
        else
        {
            return VisitDestructuringAssignmentTarget(node);
        }
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
        Debug.Assert(TransformerUtilities.IsArrayBindingOrAssignmentElement(store, node));
        if (store.Kind(node) == SyntaxKind.SpreadElement)
        {
            return VisitAssignmentRestElement(node);
        }
        if (store.Kind(node) != SyntaxKind.OmittedExpression)
        {
            return VisitAssignmentElement(node);
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
        // AssignmentProperty : IdentifierReference Initializer?

        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node, CanIgnoreEmptyStringLiteralInAssignedName(store.ObjectAssignmentInitializer(node)));
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
            var elements = VisitList(store.Elements(node), VisitArrayAssignmentElement);
            return factory.UpdateArrayLiteralExpression(node, elements);
        }
        else
        {
            var properties = VisitList(store.Properties(node), VisitObjectAssignmentElement);
            return factory.UpdateObjectLiteralExpression(node, properties);
        }
    }

    private VisitResult VisitExportAssignment(Node node)
    {
        // 16.2.3.7 RS: Evaluation
        //   ExportDeclaration : `export` `default` AssignmentExpression `;`
        //     1. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true*, then
        //        a. Let _value_ be ? NamedEvaluation of |AssignmentExpression| with argument `"default"`.
        //     ...

        if (NamedEvaluation.IsNamedEvaluation(store, node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(context, node, CanIgnoreEmptyStringLiteralInAssignedName(store.Expression(node)));
        }

        return Visitors.VisitEachChild(node, visitor, context);
    }

    private VisitResult VisitParenthesizedExpression(Node node, bool discarded)
    {
        // 8.4.5 RS: NamedEvaluation
        //   ParenthesizedExpression : `(` Expression `)`
        //     ...
        //     2. Return ? NamedEvaluation of |Expression| with argument _name_.

        var visitorFunc = discarded ? discardedValueVisitor : visitor;
        var expression = Visit(store.Expression(node), visitorFunc);
        return factory.UpdateParenthesizedExpression(node, expression);
    }

    private VisitResult VisitPartiallyEmittedExpression(Node node, bool discarded)
    {
        // Emulates 8.4.5 RS: NamedEvaluation

        var visitorFunc = discarded ? discardedValueVisitor : visitor;
        var expression = Visit(store.Expression(node), visitorFunc);
        return factory.UpdatePartiallyEmittedExpression(node, expression);
    }

    private Node InjectPendingExpressionsCommon(List<Node>? pending, Node expression)
    {
        if (Core.Some(pending))
        {
            if (!expression.IsNone)
            {
                if (store.Kind(expression) == SyntaxKind.ParenthesizedExpression)
                {
                    pending.Add(store.Expression(expression));
                    expression = factory.UpdateParenthesizedExpression(expression, factory.InlineExpressions(pending));
                }
                else
                {
                    pending.Add(expression);
                    expression = factory.InlineExpressions(pending);
                }
            }
            else
            {
                expression = factory.InlineExpressions(pending);
            }
        }
        return expression;
    }

    private Node InjectPendingExpressions(Node expression)
    {
        var result = InjectPendingExpressionsCommon(pendingExpressions, expression);
        Debug.Assert(!result.IsNone);
        if (result != expression)
        {
            pendingExpressions = null;
        }
        return result;
    }

    private Node InjectPendingInitializers(ClassInfo info, bool isStatic, Node expression)
    {
        var result = InjectPendingExpressionsCommon(isStatic ? info.PendingStaticInitializers : info.PendingInstanceInitializers, expression);
        if (result != expression)
        {
            if (isStatic)
            {
                info.PendingStaticInitializers = null;
            }
            else
            {
                info.PendingInstanceInitializers = null;
            }
        }
        return result;
    }

    /// <summary>Transforms all of the decorators for a declaration into an array of expressions.</summary>
    private List<Node>? TransformAllDecoratorsOfDeclaration(List<Node>? allDecorators)
    {
        if (allDecorators is null)
        {
            return null;
        }

        var decoratorExpressions = new List<Node>(allDecorators.Count);
        foreach (var decorator in allDecorators)
        {
            decoratorExpressions.Add(TransformDecorator(decorator));
        }
        return decoratorExpressions;
    }

    /// <summary>Transforms a decorator into an expression.</summary>
    private Node TransformDecorator(Node decorator)
    {
        var expression = Visit(store.Expression(decorator), visitor);
        FactoryEmitNode.SetEmitFlags(store, expression, EmitFlags.NoComments);

        // preserve the 'this' binding for an access expression
        var innerExpression = FactoryUtilities.SkipOuterExpressions(store, expression);
        if (Utilities.IsAccessExpression(store, innerExpression))
        {
            var (target, thisArg) = factory.CreateCallBinding(expression, hoistVariableDeclaration, languageVersion, cacheIdentifiers: true);
            return factory.RestoreOuterExpressions(expression, factory.CreateFunctionBindCall(target, thisArg, ReadOnlySpan<Node>.Empty));
        }
        return expression;
    }

    /// <summary>Creates a `value`, `get`, or `set` method for a pseudo-PropertyDescriptor object created for a private element.</summary>
    private Node CreateDescriptorMethod(Node original, Node name, NodeList modifiers, Node asteriskToken, string kind, NodeList parameters, Node body)
    {
        var func = factory.CreateFunctionExpression(
            modifiers,
            asteriskToken,
            default,
            default,
            parameters,
            default,
            body.IsNone ? factory.CreateBlock(factory.CreateNodeArray(ReadOnlySpan<Node>.Empty)) : body);
        NodeFactory.SetOriginalNode(store, func, original);
        FactoryEmitNode.SetSourceMapRange(store, func, TransformerUtilities.MoveRangePastDecorators(store, original));
        FactoryEmitNode.SetEmitFlags(store, func, EmitFlags.NoComments);

        var prefix = kind is "get" or "set" ? kind : null;
        var functionName = factory.CreateStringLiteralFromNode(name);
        var namedFunction = context.GetEmitHelperFactory().CreateSetFunctionNameHelper(func, functionName, prefix);
        var method = factory.CreatePropertyAssignment(Ident(kind), namedFunction);
        NodeFactory.SetOriginalNode(store, method, original);
        FactoryEmitNode.SetSourceMapRange(store, method, TransformerUtilities.MoveRangePastDecorators(store, original));
        FactoryEmitNode.SetEmitFlags(store, method, EmitFlags.NoComments);
        return method;
    }

    /// <summary>Creates a pseudo-PropertyDescriptor object used when decorating a private MethodDeclaration.</summary>
    private Node CreateMethodDescriptorObject(Node node, NodeList modifiers)
    {
        return factory.CreateObjectLiteralExpression(Array(
            CreateDescriptorMethod(
                node,
                store.Name(node),
                modifiers,
                store.AsteriskToken(node),
                "value",
                VisitList(store.Parameters(node), visitor),
                Visit(store.Body(node), visitor))));
    }

    /// <summary>Creates a pseudo-PropertyDescriptor object used when decorating a private GetAccessorDeclaration.</summary>
    private Node CreateGetAccessorDescriptorObject(Node node, NodeList modifiers)
    {
        return factory.CreateObjectLiteralExpression(Array(
            CreateDescriptorMethod(
                node,
                store.Name(node),
                modifiers,
                default,
                "get",
                factory.CreateNodeArray(ReadOnlySpan<Node>.Empty),
                Visit(store.Body(node), visitor))));
    }

    /// <summary>Creates a pseudo-PropertyDescriptor object used when decorating a private SetAccessorDeclaration.</summary>
    private Node CreateSetAccessorDescriptorObject(Node node, NodeList modifiers)
    {
        return factory.CreateObjectLiteralExpression(Array(
            CreateDescriptorMethod(
                node,
                store.Name(node),
                modifiers,
                default,
                "set",
                VisitList(store.Parameters(node), visitor),
                Visit(store.Body(node), visitor))));
    }

    /// <summary>Creates a pseudo-PropertyDescriptor object used when decorating an `accessor` PropertyDeclaration with a private name.</summary>
    private Node CreateAccessorPropertyDescriptorObject(Node node, NodeList modifiers)
    {
        //  {
        //      get() { return this.${privateName}; },
        //      set(value) { this.${privateName} = value; },
        //  }

        return factory.CreateObjectLiteralExpression(Array(
            CreateDescriptorMethod(
                node,
                store.Name(node),
                modifiers,
                default,
                "get",
                factory.CreateNodeArray(ReadOnlySpan<Node>.Empty),
                factory.CreateBlock(Array(
                    factory.CreateReturnStatement(
                        factory.CreatePropertyAccessExpression(
                            This(),
                            factory.GetGeneratedPrivateNameForNode(store.Name(node))))))),
            CreateDescriptorMethod(
                node,
                store.Name(node),
                modifiers,
                default,
                "set",
                Array(factory.CreateParameterDeclaration(default, default, Ident("value"))),
                factory.CreateBlock(Array(
                    factory.CreateExpressionStatement(
                        factory.CreateAssignment(
                            factory.CreatePropertyAccessExpression(
                                This(),
                                factory.GetGeneratedPrivateNameForNode(store.Name(node))),
                            Ident("value"))))))));
    }

    // strip off all but the `static` modifier
    private NodeList StaticModifierOnly(NodeList modifiers) =>
        VisitList(modifiers, n => store.Kind(n) == SyntaxKind.StaticKeyword ? n : VisitResult.None);

    /// <summary>Creates a MethodDeclaration that forwards its invocation to a PropertyDescriptor object.</summary>
    private Node CreateMethodDescriptorForwarder(NodeList modifiers, Node name, Node descriptorName)
    {
        modifiers = StaticModifierOnly(modifiers);
        return factory.CreateGetAccessorDeclaration(
            modifiers,
            name,
            factory.CreateNodeArray(ReadOnlySpan<Node>.Empty),
            default,
            factory.CreateBlock(Array(
                factory.CreateReturnStatement(
                    factory.CreatePropertyAccessExpression(
                        descriptorName,
                        Ident("value"))))));
    }

    /// <summary>Creates a GetAccessorDeclaration that forwards its invocation to a PropertyDescriptor object.</summary>
    private Node CreateGetAccessorDescriptorForwarder(NodeList modifiers, Node name, Node descriptorName)
    {
        modifiers = StaticModifierOnly(modifiers);
        return factory.CreateGetAccessorDeclaration(
            modifiers,
            name,
            factory.CreateNodeArray(ReadOnlySpan<Node>.Empty),
            default,
            factory.CreateBlock(Array(
                factory.CreateReturnStatement(
                    factory.CreateFunctionCallCall(
                        factory.CreatePropertyAccessExpression(
                            descriptorName,
                            Ident("get")),
                        This(),
                        ReadOnlySpan<Node>.Empty)))));
    }

    /// <summary>Creates a SetAccessorDeclaration that forwards its invocation to a PropertyDescriptor object.</summary>
    private Node CreateSetAccessorDescriptorForwarder(NodeList modifiers, Node name, Node descriptorName)
    {
        modifiers = StaticModifierOnly(modifiers);
        return factory.CreateSetAccessorDeclaration(
            modifiers,
            name,
            Array(factory.CreateParameterDeclaration(default, default, Ident("value"))),
            factory.CreateBlock(Array(
                factory.CreateReturnStatement(
                    factory.CreateFunctionCallCall(
                        factory.CreatePropertyAccessExpression(
                            descriptorName,
                            Ident("set")),
                        This(),
                        [Ident("value")])))));
    }

    private Node CreateMetadata(Node name, Node classSuperName)
    {
        var varDecl = factory.CreateVariableDeclaration(
            name,
            default,
            default,
            factory.CreateConditionalExpression(
                factory.CreateLogicalAnd(
                    factory.CreateTypeCheck(Ident("Symbol"), "function"),
                    factory.CreatePropertyAccessExpression(Ident("Symbol"), Ident("metadata"))),
                factory.CreateToken(SyntaxKind.QuestionToken),
                factory.CreateCallExpression(
                    factory.CreatePropertyAccessExpression(Ident("Object"), Ident("create")),
                    default,
                    Array(!classSuperName.IsNone ? CreateSymbolMetadataReference(classSuperName) : Null())),
                factory.CreateToken(SyntaxKind.ColonToken),
                factory.CreateVoidZero()));
        return factory.CreateVariableStatement(default, factory.CreateVariableDeclarationList(Array(varDecl), NodeFlags.Const));
    }

    private Node CreateSymbolMetadata(Node target, Node value)
    {
        var defineProperty = factory.CreateObjectDefinePropertyCall(
            target,
            factory.CreatePropertyAccessExpression(Ident("Symbol"), Ident("metadata")),
            factory.CreatePropertyDescriptor(new NodeFactory.PropertyDescriptorAttributes { Configurable = true, Writable = true, Enumerable = true, Value = value }, singleLine: true));
        return FactoryEmitNode.SetEmitFlags(
            store,
            factory.CreateIfStatement(value, factory.CreateExpressionStatement(defineProperty)),
            EmitFlags.SingleLine);
    }

    private Node CreateSymbolMetadataReference(Node classSuperName)
    {
        return factory.CreateBinaryExpression(
            factory.CreateElementAccessExpression(
                classSuperName,
                factory.CreatePropertyAccessExpression(Ident("Symbol"), Ident("metadata"))),
            factory.CreateToken(SyntaxKind.QuestionQuestionToken),
            Null());
    }
}
